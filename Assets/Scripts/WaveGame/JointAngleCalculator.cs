using UnityEngine;

/// <summary>
/// Knee flexion angle from hip, knee and ankle joints (GP1 Code Listing 4.1).
/// 0 degrees = straight leg, larger = more bent.
/// The joints can be moved by the leg simulator now, or by Azure Kinect body tracking later.
/// </summary>
public class JointAngleCalculator : MonoBehaviour
{
    public Transform hipJoint;
    public Transform kneeJoint;
    public Transform ankleJoint;

    [Tooltip("Low-pass filter strength (GP1 Challenge 1). 0 = no smoothing, higher = smoother but slower")]
    [Range(0f, 0.95f)] public float smoothing = 0.6f;

    public float RawKneeAngle { get; private set; }
    public float CurrentKneeAngle { get; private set; }
    public bool HasJoints => hipJoint != null && kneeJoint != null && ankleJoint != null;

    bool initialized;

    void Update()
    {
        if (!HasJoints) return;

        Vector3 upperLegVector = hipJoint.position - kneeJoint.position;
        Vector3 lowerLegVector = ankleJoint.position - kneeJoint.position;
        float rawAngle = Vector3.Angle(upperLegVector, lowerLegVector);
        RawKneeAngle = 180f - rawAngle;

        if (!initialized)
        {
            CurrentKneeAngle = RawKneeAngle;
            initialized = true;
        }
        else
        {
            // Frame-rate independent exponential smoothing.
            float k = 1f - Mathf.Pow(smoothing, Time.deltaTime * 30f);
            CurrentKneeAngle = Mathf.Lerp(CurrentKneeAngle, RawKneeAngle, k);
        }
    }
}
