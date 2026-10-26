using UnityEngine;

/// <summary>
/// Stands in for the Azure Kinect until the sensor is available.
/// Moves hip / knee / ankle joint points the same way body tracking would,
/// so JointAngleCalculator and the game don't know the difference.
///
/// Controls: S / Down arrow = pull heel back (bend knee), W / Up arrow = straighten.
/// In the headset: pull either thumbstick back to bend, push forward to straighten.
/// </summary>
public class SimulatedLegJoints : MonoBehaviour
{
    public Transform hipJoint;
    public Transform kneeJoint;
    public Transform ankleJoint;

    public float thighLength = 0.42f;
    public float shinLength = 0.45f;

    [Tooltip("Knee angle the simulated patient starts at")]
    public float restAngle = 20f;
    [Tooltip("How fast the simulated knee bends, degrees per second")]
    public float bendSpeed = 45f;
    public float minAngle = 0f;
    public float maxAngle = 140f;

    [Tooltip("Random noise in degrees, to mimic real sensor jitter")]
    public float jitter = 0.5f;

    public float SimulatedAngle { get; private set; }

    void Awake()
    {
        if (!hipJoint) hipJoint = CreateJoint("Hip");
        if (!kneeJoint) kneeJoint = CreateJoint("Knee");
        if (!ankleJoint) ankleJoint = CreateJoint("Ankle");
        SimulatedAngle = restAngle;
        ApplyPose();
    }

    Transform CreateJoint(string jointName)
    {
        var t = new GameObject(jointName).transform;
        t.SetParent(transform, false);
        return t;
    }

    void Update()
    {
        SimulatedAngle = Mathf.Clamp(SimulatedAngle + RehabInput.Bend * bendSpeed * Time.deltaTime, minAngle, maxAngle);
        ApplyPose();
    }

    void ApplyPose()
    {
        // Side view in local space: thigh points back (+Z), straight shin points forward (-Z),
        // bending rotates the shin down and then back under the seat.
        float angle = SimulatedAngle + (jitter > 0f ? Random.Range(-jitter, jitter) : 0f);
        float rad = angle * Mathf.Deg2Rad;
        kneeJoint.localPosition = Vector3.zero;
        hipJoint.localPosition = new Vector3(0f, 0f, thighLength);
        ankleJoint.localPosition = new Vector3(0f, -Mathf.Sin(rad), -Mathf.Cos(rad)) * shinLength;
    }
}
