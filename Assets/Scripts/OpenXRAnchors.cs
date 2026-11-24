using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

/// <summary>
/// Feeds the OVRCameraRig's head and hand anchors from Unity's OpenXR input whenever Meta's OVRPlugin has no pose.
/// With this project's OpenXR + Quest Link setup OVRPlugin reports nothing (not tracked, 0,0,0), so the rig's camera
/// sat at the rig origin and never followed the head, while Unity's XR input has the real poses.
/// Added to every OVRCameraRig at scene load, so no scene needs editing.
/// </summary>
public class OpenXRAnchors : MonoBehaviour
{
    OVRCameraRig rig;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        SceneManager.sceneLoaded += (scene, mode) => AttachAll();
        AttachAll();
    }

    static void AttachAll()
    {
        foreach (var rig in FindObjectsByType<OVRCameraRig>(FindObjectsSortMode.None))
            if (!rig.GetComponent<OpenXRAnchors>()) rig.gameObject.AddComponent<OpenXRAnchors>();
    }

    void Awake() => rig = GetComponent<OVRCameraRig>();

    // The rig raises UpdatedAnchors every time it writes the anchors (Update and before render), so this runs right after.
    void OnEnable()
    {
        if (rig) rig.UpdatedAnchors += Apply;
    }

    void OnDisable()
    {
        if (rig) rig.UpdatedAnchors -= Apply;
    }

    void Apply(OVRCameraRig r)
    {
        if (!OVRPlugin.GetNodePositionTracked(OVRPlugin.Node.Head) && Copy(XRNode.Head, r.centerEyeAnchor))
        {
            r.leftEyeAnchor.SetLocalPositionAndRotation(r.centerEyeAnchor.localPosition, r.centerEyeAnchor.localRotation);
            r.rightEyeAnchor.SetLocalPositionAndRotation(r.centerEyeAnchor.localPosition, r.centerEyeAnchor.localRotation);
        }
        if (!OVRPlugin.GetNodePositionTracked(OVRPlugin.Node.HandLeft)) Copy(XRNode.LeftHand, r.leftHandAnchor);
        if (!OVRPlugin.GetNodePositionTracked(OVRPlugin.Node.HandRight)) Copy(XRNode.RightHand, r.rightHandAnchor);
    }

    /// <summary>Puts the OpenXR pose of a device on an anchor; false when the device isn't tracked.</summary>
    static bool Copy(XRNode node, Transform anchor)
    {
        var device = InputDevices.GetDeviceAtXRNode(node);
        if (!device.isValid || !device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) || !tracked) return false;
        if (!device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 position)) return false;
        if (!device.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rotation)) return false;
        anchor.SetLocalPositionAndRotation(position, rotation);
        return true;
    }

    /// <summary>The headset is on and sending a pose (through either OVRPlugin or OpenXR).</summary>
    public static bool HeadTracked
    {
        get
        {
            if (OVRPlugin.GetNodePositionTracked(OVRPlugin.Node.Head)) return true;
            var device = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            return device.isValid && device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && tracked;
        }
    }
}
