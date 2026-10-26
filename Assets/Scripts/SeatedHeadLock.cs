using UnityEngine;

/// <summary>
/// Keeps the player seated: the head can look around (rotation) but cannot move
/// away from the seat. Put this on the root of an OVRCameraRig placed at the eye position.
/// </summary>
public class SeatedHeadLock : MonoBehaviour
{
    [Tooltip("TrackingSpace of the OVRCameraRig")]
    public Transform trackingSpace;

    [Tooltip("CenterEyeAnchor of the OVRCameraRig (the headset camera)")]
    public Transform centerEye;

    [Tooltip("Turn the view so the player faces the rig's forward when the game starts")]
    public bool alignYawOnStart = true;

    bool yawAligned;

    void Awake()
    {
        if (!trackingSpace) trackingSpace = transform.Find("TrackingSpace");
        if (!centerEye && trackingSpace) centerEye = trackingSpace.Find("CenterEyeAnchor");
    }

    void OnEnable() => Application.onBeforeRender += Lock;
    void OnDisable() => Application.onBeforeRender -= Lock;
    void LateUpdate() => Lock();

    void Lock()
    {
        if (!trackingSpace || !centerEye) return;

        // Once the headset reports a pose, rotate so the player looks the way the chair faces.
        if (alignYawOnStart && !yawAligned && centerEye.localRotation != Quaternion.identity)
        {
            float headYaw = centerEye.localEulerAngles.y;
            trackingSpace.localRotation = Quaternion.Euler(0f, -headYaw, 0f);
            yawAligned = true;
        }

        // Cancel any head movement so the eyes always stay at the rig origin (the seat).
        Vector3 eyeOffset = trackingSpace.localRotation * centerEye.localPosition;
        trackingSpace.localPosition = -eyeOffset;
    }
}
