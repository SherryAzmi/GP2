using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// With floor-level tracking and no headset connected, nothing lifts the camera, so it sits on the ground.
/// This puts the eyes at a standing height for testing in the editor; with a headset on it does nothing.
/// Put it on the root of an OVRCameraRig.
/// </summary>
public class DesktopEyeHeight : MonoBehaviour
{
    [Tooltip("TrackingSpace of the OVRCameraRig")]
    public Transform trackingSpace;
    [Tooltip("Eye height used when no headset is connected, metres")]
    public float eyeHeight = 1.65f;

    void Awake()
    {
        if (!trackingSpace) trackingSpace = transform.Find("TrackingSpace");
    }

    void LateUpdate()
    {
        if (!trackingSpace) return;
        trackingSpace.localPosition = XRSettings.isDeviceActive ? Vector3.zero : new Vector3(0f, eyeHeight, 0f);
    }
}
