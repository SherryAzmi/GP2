using UnityEngine;

/// <summary>
/// Bends the patient's virtual shin in the beach scene to match the measured knee angle,
/// so the player sees their own leg move. The shin's pivot must be at the knee.
/// </summary>
public class VirtualLegPose : MonoBehaviour
{
    public JointAngleCalculator angleSource;
    [Tooltip("Virtual shin to rotate (pivot at the knee)")]
    public Transform shin;

    [Tooltip("Shin local X rotation when the leg is straight (0 degrees)")]
    public float straightLocalX = 342f;
    [Tooltip("Point at the heel, in the shin's local space")]
    public Vector3 heelLocalPoint;
    [Tooltip("Knee angle used when there is no angle source (e.g. the non-operated leg resting)")]
    public float fixedAngle = 45f;

    Quaternion straightRotation;

    void Awake()
    {
        if (!shin) shin = transform;
        Vector3 e = shin.localEulerAngles;
        straightRotation = Quaternion.Euler(straightLocalX, e.y, e.z);
    }

    void LateUpdate()
    {
        shin.localRotation = RotationFor(angleSource ? angleSource.CurrentKneeAngle : fixedAngle);
    }

    Quaternion RotationFor(float kneeAngle) => straightRotation * Quaternion.Euler(-kneeAngle, 0f, 0f);

    /// <summary>Where the heel would be (world space) if the knee were bent to this angle.</summary>
    public Vector3 HeelPositionAt(float kneeAngle)
    {
        Vector3 local = shin.localPosition + RotationFor(kneeAngle) * Vector3.Scale(heelLocalPoint, shin.localScale);
        return shin.parent ? shin.parent.TransformPoint(local) : local;
    }

    public Vector3 HeelPosition => shin.TransformPoint(heelLocalPoint);
}
