using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// All game input in one place, read through Unity's Input System so it works with the
/// OpenXR + Meta Quest Touch setup (OVRInput gets no controller data under OpenXR here).
///   Start:  A or X on the controllers, Space or Enter on the keyboard.
///   Bend:   thumbstick back = pull heel back, forward = straighten; S/Down and W/Up on the keyboard.
/// </summary>
public static class RehabInput
{
    static InputAction start;
    static InputAction stick;

    static void Init()
    {
        if (start != null) return;

        start = new InputAction("Start", InputActionType.Button);
        start.AddBinding("<XRController>{RightHand}/primaryButton"); // A
        start.AddBinding("<XRController>{LeftHand}/primaryButton");  // X
        start.AddBinding("<Keyboard>/space");
        start.AddBinding("<Keyboard>/enter");
        start.AddBinding("<Gamepad>/buttonSouth");
        start.Enable();

        stick = new InputAction("Stick", InputActionType.Value, expectedControlType: "Vector2");
        stick.AddBinding("<XRController>{RightHand}/thumbstick");
        stick.AddBinding("<XRController>{LeftHand}/thumbstick");
        stick.AddBinding("<Gamepad>/leftStick");
        stick.Enable();
    }

    public static bool StartPressed
    {
        get
        {
            Init();
            return start.WasPressedThisFrame();
        }
    }

    /// <summary>+1 = bend the knee (pull heel back), -1 = straighten.</summary>
    public static float Bend
    {
        get
        {
            Init();
            float input = 0f;
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) input += 1f;
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) input -= 1f;
            }
            float y = stick.ReadValue<Vector2>().y;
            if (Mathf.Abs(y) > 0.15f) input -= y; // stick pulled back (negative y) bends the knee
            return Mathf.Clamp(input, -1f, 1f);
        }
    }
}
