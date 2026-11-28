using System.Collections;
using UnityEngine;

/// <summary>
/// AR check-up: the doctor wears the headset, sees the real room (passthrough) with the patient's live knee angle,
/// a graph and a bone overlay that bends with the knee. No game. Hold the right trigger to move the overlay onto the
/// patient's knee (placed by hand: the webcam does not tell the headset where the leg is).
/// End with Esc, the left menu button or STOP &amp; SAVE on the PC. The angle curve is saved to the patient.
/// Builds itself; the scene only needs the camera rig (with passthrough) and this component.
/// </summary>
public class CheckUpSession : MonoBehaviour
{
    public const string SceneName = "CheckUp";
    const float Thigh = 0.42f, Shin = 0.45f;

    JointAngleCalculator angle;
    CameraPoseJoints sensor;
    TherapistMonitor monitor;
    Transform overlay, shin, rightHand;
    float started, highest = float.MinValue, lowest = float.MaxValue, limit = PatientRecord.DefaultSafetyLimit;
    int alerts;
    bool above, saved;
    AudioSource audioSource;
    AudioClip alertClip;

    float Angle => angle ? angle.CurrentKneeAngle : 0f;

    IEnumerator Start()
    {
        BuildSensor();
        BuildOverlay();
        var p = SessionConfig.Patient;
        if (p != null)
        {
            limit = p.SafetyLimit;
            sensor.useLeftLeg = p.IsLeftLeg;
        }
        var rig = FindFirstObjectByType<OVRCameraRig>();
        if (rig) rightHand = rig.rightHandAnchor;
        audioSource = gameObject.AddComponent<AudioSource>();
        alertClip = GeneratedAudio.Tone("SafetyAlert", new[] { 1000f, 0f, 1000f }, 0.1f, 0.4f);

        yield return new WaitForSeconds(1f); // let the headset pose arrive before placing things
        var head = Camera.main ? Camera.main.transform : transform;
        Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
        if (forward == Vector3.zero) forward = Vector3.forward;
        overlay.SetPositionAndRotation(head.position + forward * 1.1f + Vector3.down * 0.7f,
                                       Quaternion.LookRotation(Vector3.Cross(Vector3.up, forward))); // seen from the side

        started = Time.time;
        monitor = TherapistMonitor.Create(transform, Read, true);
        monitor.SetVisible(true);
        SessionControls.Create(transform, End).SetVisible(true);
    }

    LiveReading Read() => new LiveReading
    {
        angle = Angle, target = -1f, limit = limit, alerts = alerts,
        cameraConnected = sensor.Connected, cameraLost = sensor.TrackingLost,
        progress = $"AR check-up  ·  bend {(highest > -1f ? highest : 0f):0}°  ·  straightest {(lowest < 1000f ? lowest : 0f):0}°",
    };

    void Update()
    {
        if (RehabInput.MenuPressed) { End(); return; }
        float a = Angle;
        shin.localRotation = Quaternion.AngleAxis(a, Vector3.right); // 0 = straight, 90 = heel straight down
        if (rightHand && OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch))
            overlay.SetPositionAndRotation(rightHand.position, Quaternion.Euler(0f, rightHand.eulerAngles.y, 0f));

        if (!monitor || sensor.TrackingLost || SessionControls.Paused) return;
        highest = Mathf.Max(highest, a);
        lowest = Mathf.Min(lowest, a);
        bool over = a > limit;
        if (over && !above)
        {
            alerts++;
            audioSource.PlayOneShot(alertClip);
        }
        above = over;
    }

    void End()
    {
        Save();
        if (Application.CanStreamedLevelBeLoaded(SessionConfig.MenuScene)) SessionConfig.ReturnToMenu();
    }

    void OnApplicationQuit() => Save();

    void Save()
    {
        if (saved || !monitor || !SessionConfig.Active) return;
        saved = true;
        if (highest > float.MinValue)
            PatientStore.RecordCheckUp(SessionConfig.PatientId, Time.time - started, highest, lowest, alerts, sensor.Connected);
    }

    // The same sensor chain as the games: simulator (keyboard S / W) until the camera sends readings.
    void BuildSensor()
    {
        var go = new GameObject("KneeSensor");
        go.SetActive(false); // assign the joints before the components wake up
        go.transform.SetParent(transform, false);
        Transform Joint(string name, Vector3 at)
        {
            var joint = new GameObject(name).transform;
            joint.SetParent(go.transform, false);
            joint.localPosition = at;
            return joint;
        }
        var hip = Joint("Hip", new Vector3(0f, Thigh, 0f));
        var knee = Joint("Knee", Vector3.zero);
        var ankle = Joint("Ankle", new Vector3(0f, -Shin, 0f));
        var simulator = go.AddComponent<SimulatedLegJoints>();
        simulator.hipJoint = hip;
        simulator.kneeJoint = knee;
        simulator.ankleJoint = ankle;
        angle = go.AddComponent<JointAngleCalculator>();
        angle.hipJoint = hip;
        angle.kneeJoint = knee;
        angle.ankleJoint = ankle;
        sensor = go.AddComponent<CameraPoseJoints>();
        sensor.simulator = simulator;
        sensor.angleCalculator = angle;
        go.SetActive(true);
    }

    // A simple femur and tibia. Knee at the root, thigh pointing back, shin pivoting at the knee.
    void BuildOverlay()
    {
        overlay = new GameObject("BoneOverlay").transform;
        Bone(overlay, new Vector3(0f, 0f, -Thigh / 2f), Thigh, 0.05f);
        Ball(overlay, new Vector3(0f, 0f, -Thigh), 0.09f); // head of the femur
        Ball(overlay, Vector3.zero, 0.1f);                 // knee joint
        shin = new GameObject("Shin").transform;
        shin.SetParent(overlay, false);
        Bone(shin, new Vector3(0f, 0f, Shin / 2f), Shin, 0.042f);
        Ball(shin, new Vector3(0f, 0f, Shin), 0.07f);      // ankle
    }

    static void Bone(Transform parent, Vector3 centre, float length, float width)
    {
        var part = Part(PrimitiveType.Cylinder, parent, centre);
        part.localRotation = Quaternion.Euler(90f, 0f, 0f);
        part.localScale = new Vector3(width, length / 2f, width);
    }

    static void Ball(Transform parent, Vector3 centre, float size) =>
        Part(PrimitiveType.Sphere, parent, centre).localScale = Vector3.one * size;

    static Transform Part(PrimitiveType type, Transform parent, Vector3 centre)
    {
        var go = GameObject.CreatePrimitive(type);
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = centre;
        var block = new MaterialPropertyBlock();
        block.SetColor("_BaseColor", new Color(0.93f, 0.89f, 0.76f));
        go.GetComponent<Renderer>().SetPropertyBlock(block);
        return go.transform;
    }
}
