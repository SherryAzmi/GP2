using UnityEngine;

/// <summary>
/// With floor-level tracking and no head pose coming in, nothing lifts the camera, so it sits on the ground.
/// This puts the eyes at a standing height for testing on the monitor; once the headset is on and tracked it does nothing.
/// It asks whether the head is tracked, not whether a headset is connected: with Quest Link running but the headset
/// off the head, a headset counts as connected yet sends no pose.
/// Put it on the root of an OVRCameraRig.
/// </summary>
public class DesktopEyeHeight : MonoBehaviour
{
    [Tooltip("TrackingSpace of the OVRCameraRig")]
    public Transform trackingSpace;
    [Tooltip("Eye height used while no headset is tracked, metres")]
    public float eyeHeight = 1.65f;

    void Awake()
    {
        if (!trackingSpace) trackingSpace = transform.Find("TrackingSpace");
    }

    void LateUpdate()
    {
        if (!trackingSpace) return;
        trackingSpace.localPosition = OpenXRAnchors.HeadTracked ? Vector3.zero : new Vector3(0f, eyeHeight, 0f);
    }
}
