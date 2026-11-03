using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Therapist setup screen shown before every session (mouse + keyboard, flat screen).
/// The therapist enters the patient's name, the operated leg and that patient's own target range
/// (min and max knee angle) plus how many targets (waves) to run and the hold times, then presses Start.
/// Builds itself at runtime, so the scene only needs the WaveGameManager.
/// </summary>
public class DoctorPanel : MonoBehaviour
{
    const float PanelW = 760f, RowH = 62f;

    WaveGameManager game;
    GameObject root;
    InputField nameField, minField, maxField, wavesField, holdField, maxHoldField;
    Image leftButton, rightButton, bothButton;
    Text hintText, errorText, legNote;
    WaveGameManager.LegMode legMode;

    static readonly Color Dim = new Color(0.04f, 0.12f, 0.22f, 0.92f);
    static readonly Color Field = new Color(1f, 1f, 1f, 0.12f);
    static readonly Color Off = new Color(1f, 1f, 1f, 0.15f);
    static readonly Color On = new Color(0.25f, 0.65f, 1f, 1f);

    const float WorldScale = 0.0014f; // metres per UI pixel when the panel floats in the scene

    bool inPatientView;

    /// <summary>
    /// Screen overlay (therapist's monitor) or, with <paramref name="patientView"/>, a world-space canvas
    /// floating in front of the patient so the panel is part of what the headset camera sees.
    /// </summary>
    internal static void ConfigureCanvas(GameObject root, bool patientView, int sortingOrder)
    {
        var canvas = root.GetComponent<Canvas>();
        var scaler = root.GetComponent<CanvasScaler>();
        canvas.sortingOrder = sortingOrder;
        if (patientView)
        {
            canvas.renderMode = RenderMode.WorldSpace;
            scaler.enabled = false;
            var rt = (RectTransform)root.transform;
            rt.sizeDelta = new Vector2(1920f, 1080f);
            rt.localScale = Vector3.one * WorldScale;
            return;
        }
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
    }

    /// <summary>Puts a world-space panel straight ahead of the patient, facing them, and lets the mouse click it.</summary>
    internal static void PlaceInFront(GameObject root, float distance)
    {
        var canvas = root.GetComponent<Canvas>();
        if (canvas.renderMode != RenderMode.WorldSpace) return;
        var cam = Camera.main;
        if (!cam) return;
        canvas.worldCamera = cam;
        Vector3 forward = cam.transform.forward;
        forward.y = 0f;
        forward = forward.sqrMagnitude < 0.01f ? Vector3.forward : forward.normalized;
        // A little below eye level, so the umbrella canopy above the chair does not cut off the top of the panel.
        root.transform.SetPositionAndRotation(cam.transform.position + forward * distance + Vector3.down * 0.25f, Quaternion.LookRotation(forward));
    }

    public static DoctorPanel Create(WaveGameManager game)
    {
        if (!FindFirstObjectByType<EventSystem>())
        {
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            es.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }
        var go = new GameObject("DoctorPanel");
        go.transform.SetParent(game.transform, false);
        var panel = go.AddComponent<DoctorPanel>();
        panel.game = game;
        panel.inPatientView = game.panelsInPatientView;
        panel.Build();
        return panel;
    }

    public void Show()
    {
        nameField.text = game.patientId;
        minField.text = game.minTargetAngle.ToString("0.#", CultureInfo.InvariantCulture);
        maxField.text = game.maxTargetAngle.ToString("0.#", CultureInfo.InvariantCulture);
        wavesField.text = game.wavesPerSession.ToString(CultureInfo.InvariantCulture);
        holdField.text = game.startHoldSeconds.ToString("0.#", CultureInfo.InvariantCulture);
        maxHoldField.text = game.maxHoldSeconds.ToString("0.#", CultureInfo.InvariantCulture);
        SetLeg(game.legMode);
        hintText.text = $"Safety limit is {game.safetyCeilingAngle:0}°. Targets are kept at least 5° below it.";
        errorText.text = "";
        PlaceInFront(root, 1.3f);
        root.SetActive(true);
    }

    public void Hide() => root.SetActive(false);

    void OnStartClicked()
    {
        var c = CultureInfo.InvariantCulture;
        float min = ParseFloat(minField.text), max = ParseFloat(maxField.text);
        float hold = ParseFloat(holdField.text), maxHold = ParseFloat(maxHoldField.text);
        int.TryParse(wavesField.text, NumberStyles.Integer, c, out int waves);
        float top = game.safetyCeilingAngle - 5f;

        string error = null;
        if (string.IsNullOrWhiteSpace(nameField.text)) error = "Enter the patient's name.";
        else if (min < 5f) error = "Minimum angle must be at least 5°.";
        else if (max <= min) error = "Maximum angle must be greater than the minimum.";
        else if (max > top) error = $"Maximum angle can't be above {top:0}° (safety limit).";
        else if (waves < 1 || waves > 50) error = "Number of targets must be between 1 and 50.";
        else if (hold < 0.5f) error = "Hold time must be at least 0.5 s.";
        else if (maxHold < hold) error = "Max hold time must be at least the starting hold time.";

        if (error != null)
        {
            errorText.text = error;
            return;
        }

        game.ApplyDoctorSettings(nameField.text.Trim(), legMode, min, max, waves, hold, maxHold);
        Hide();
        game.RequestStart();
    }

    void SetLeg(WaveGameManager.LegMode value)
    {
        legMode = value;
        leftButton.color = legMode == WaveGameManager.LegMode.Left ? On : Off;
        rightButton.color = legMode == WaveGameManager.LegMode.Right ? On : Off;
        bothButton.color = legMode == WaveGameManager.LegMode.Both ? On : Off;
        legNote.text = legMode == WaveGameManager.LegMode.Both
            ? "Both: right leg first, then the left. The patient is told which leg is next." : "";
    }

    static float ParseFloat(string s)
    {
        float.TryParse((s ?? "").Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float v);
        return v;
    }

    // ---------------- UI construction ----------------

    void Build()
    {
        root = new GameObject("Root", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.transform.SetParent(transform, false);
        ConfigureCanvas(root, inPatientView, 100);

        var rt = (RectTransform)root.transform;
        var dim = Child("Dim", rt, Dim);
        Stretch(dim);
        if (inPatientView) dim.gameObject.SetActive(false); // keep the beach visible around the card

        var card = Child("Card", rt, new Color(0.08f, 0.2f, 0.34f, 1f));
        card.anchorMin = card.anchorMax = card.pivot = new Vector2(0.5f, 0.5f);
        card.sizeDelta = new Vector2(PanelW, 940f);

        float y = 410f;
        Label(card, "Therapist setup", new Vector2(0, y), new Vector2(PanelW - 40f, 60f), 44, TextAnchor.MiddleCenter, Color.white);
        y -= 80f;

        nameField = Row(card, "Patient name", ref y, InputField.ContentType.Standard);

        Label(card, "Leg to train", new Vector2(-250f, y), new Vector2(240f, 50f), 28, TextAnchor.MiddleLeft, Color.white);
        leftButton = ToggleButton(card, "Left", new Vector2(-40f, y), () => SetLeg(WaveGameManager.LegMode.Left));
        rightButton = ToggleButton(card, "Right", new Vector2(100f, y), () => SetLeg(WaveGameManager.LegMode.Right));
        bothButton = ToggleButton(card, "Both", new Vector2(240f, y), () => SetLeg(WaveGameManager.LegMode.Both));
        y -= RowH - 4f;
        legNote = Label(card, "", new Vector2(0, y), new Vector2(PanelW - 60f, 40f), 20, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.7f));
        y -= RowH - 14f;

        minField = Row(card, "Min target angle (°)", ref y, InputField.ContentType.DecimalNumber);
        maxField = Row(card, "Max target angle (°)", ref y, InputField.ContentType.DecimalNumber);
        wavesField = Row(card, "Number of targets", ref y, InputField.ContentType.IntegerNumber);
        holdField = Row(card, "Starting hold (s)", ref y, InputField.ContentType.DecimalNumber);
        maxHoldField = Row(card, "Max hold (s)", ref y, InputField.ContentType.DecimalNumber);

        hintText = Label(card, "", new Vector2(0, y), new Vector2(PanelW - 60f, 60f), 22, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.7f));
        y -= 60f;
        errorText = Label(card, "", new Vector2(0, y), new Vector2(PanelW - 60f, 50f), 26, TextAnchor.MiddleCenter, WaveHUD.Bad);
        y -= 80f;

        var start = Child("Start", card, new Color(0.2f, 0.75f, 0.4f, 1f));
        start.anchoredPosition = new Vector2(0, y);
        start.sizeDelta = new Vector2(320f, 80f);
        var btn = start.gameObject.AddComponent<Button>();
        btn.targetGraphic = start.GetComponent<Image>();
        btn.onClick.AddListener(OnStartClicked);
        Label(start, "START", Vector2.zero, new Vector2(320f, 80f), 40, TextAnchor.MiddleCenter, Color.white);
    }

    InputField Row(RectTransform parent, string label, ref float y, InputField.ContentType type)
    {
        Label(parent, label, new Vector2(-170f, y), new Vector2(380f, 50f), 28, TextAnchor.MiddleLeft, Color.white);
        var box = Child("Field_" + label, parent, Field);
        box.anchoredPosition = new Vector2(190f, y);
        box.sizeDelta = new Vector2(280f, 52f);

        var text = Label(box, "", Vector2.zero, new Vector2(260f, 52f), 28, TextAnchor.MiddleLeft, Color.white);
        text.supportRichText = false;
        var input = box.gameObject.AddComponent<InputField>();
        input.targetGraphic = box.GetComponent<Image>();
        input.textComponent = text;
        input.contentType = type;
        y -= RowH;
        return input;
    }

    Image ToggleButton(RectTransform parent, string label, Vector2 pos, UnityEngine.Events.UnityAction onClick)
    {
        var rt = Child("Leg_" + label, parent, Off);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(130f, 52f);
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = rt.GetComponent<Image>();
        btn.onClick.AddListener(onClick);
        Label(rt, label, Vector2.zero, new Vector2(130f, 52f), 28, TextAnchor.MiddleCenter, Color.white);
        return rt.GetComponent<Image>();
    }

    internal static RectTransform Child(string name, RectTransform parent, Color color)
    {
        var rt = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.GetComponent<Image>().color = color;
        return rt;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    internal static Text Label(RectTransform parent, string text, Vector2 pos, Vector2 size, int fontSize, TextAnchor anchor, Color color)
    {
        var rt = new GameObject("Text", typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var t = rt.gameObject.AddComponent<Text>();
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = fontSize;
        t.alignment = anchor;
        t.color = color;
        t.text = text;
        t.raycastTarget = false;
        return t;
    }
}
