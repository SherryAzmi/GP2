using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

/// <summary>
/// Receives the knee angles from the KneeTracker camera app (UDP on this PC, about 30 a second) and moves the
/// hip / knee / ankle joint points to match, so JointAngleCalculator and the games work as they did with the simulator.
/// Packet: {"left": 23.5, "right": 41.0} in degrees, 0 = straight; -1 means the camera can't see that leg.
/// Until the first reading arrives the simulator keeps running; after that it is switched off, and a lost leg
/// holds its last angle with <see cref="TrackingLost"/> set.
/// </summary>
public class CameraPoseJoints : MonoBehaviour
{
    public int port = 5065;
    [Tooltip("Follow the patient's left knee; the games set this from the operated leg")]
    public bool useLeftLeg;
    [Tooltip("Seconds without a reading before the leg counts as lost")]
    public float lostAfterSeconds = 0.5f;
    public SimulatedLegJoints simulator;
    [Tooltip("Gets its smoothing turned off once the camera is connected: KneeTracker already filters the angle")]
    public JointAngleCalculator angleCalculator;
    public Transform hipJoint;
    public Transform kneeJoint;
    public Transform ankleJoint;
    public float thighLength = 0.42f;
    public float shinLength = 0.45f;

    /// <summary>A reading has arrived at least once.</summary>
    public bool Connected { get; private set; }
    /// <summary>The camera was working but cannot see the leg right now.</summary>
    public bool TrackingLost => Connected && Time.time - lastGoodTime > lostAfterSeconds;
    /// <summary>The last angle received for the followed knee, degrees.</summary>
    public float LastAngle { get; private set; }

    [Serializable]
    class Packet
    {
        public float left = -1f;
        public float right = -1f;
    }

    UdpClient client;
    Thread thread;
    volatile bool running;
    readonly object latestLock = new object();
    string latest;
    float lastGoodTime;

    void Awake()
    {
        if (simulator)
        {
            if (!hipJoint) hipJoint = simulator.hipJoint;
            if (!kneeJoint) kneeJoint = simulator.kneeJoint;
            if (!ankleJoint) ankleJoint = simulator.ankleJoint;
        }
        if (!angleCalculator) angleCalculator = GetComponent<JointAngleCalculator>();
    }

    void OnEnable()
    {
        try
        {
            client = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
        }
        catch (SocketException e)
        {
            Debug.LogWarning($"[CameraSensor] Could not listen on port {port}: {e.Message}");
            return;
        }
        running = true;
        thread = new Thread(Receive) { IsBackground = true };
        thread.Start();
    }

    void OnDisable()
    {
        running = false;
        client?.Close(); // unblocks Receive
        client = null;
        thread = null;
    }

    void Receive()
    {
        var from = new IPEndPoint(IPAddress.Any, 0);
        while (running)
        {
            try
            {
                string text = Encoding.UTF8.GetString(client.Receive(ref from));
                lock (latestLock) latest = text;
            }
            catch (SocketException) { if (!running) return; }
            catch (ObjectDisposedException) { return; }
        }
    }

    void Update()
    {
        string text;
        lock (latestLock)
        {
            text = latest;
            latest = null;
        }
        if (text == null) return;

        Packet packet;
        try
        {
            packet = JsonUtility.FromJson<Packet>(text);
        }
        catch (ArgumentException)
        {
            return;
        }
        if (packet == null) return;
        float angle = useLeftLeg ? packet.left : packet.right;
        if (angle < 0f) return; // that leg isn't seen: hold the last pose

        if (!Connected)
        {
            Connected = true;
            if (simulator) simulator.enabled = false;
            if (angleCalculator) angleCalculator.smoothing = 0f;
            Debug.Log("[CameraSensor] KneeTracker connected");
        }
        lastGoodTime = Time.time;
        LastAngle = angle;

        // Rebuild a leg with this knee angle: thigh up from the knee, shin swinging back as the knee bends.
        float rad = angle * Mathf.Deg2Rad;
        kneeJoint.localPosition = Vector3.zero;
        hipJoint.localPosition = new Vector3(0f, thighLength, 0f);
        ankleJoint.localPosition = new Vector3(0f, -Mathf.Cos(rad), Mathf.Sin(rad)) * shinLength;
    }
}
