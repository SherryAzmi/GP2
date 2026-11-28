using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Therapist setup screen shown before every session (mouse + keyboard, flat screen).
/// The therapist enters the patient's name, the operated leg, the movement (bend, straighten or both in turn),
/// that patient's own target ranges for bending and straightening, how many targets (waves) to run and the hold
/// times, then presses Start.
/// Builds itself at runtime, so the scene only needs the WaveGameManager.
/// </summary>
public class DoctorPanel : MonoBehaviour
{
    const float PanelW = 760f, RowH = 62f;

    WaveGameManager game;
    GameObject root;
    InputField nameField, minField, maxField, straightEasyField, straightHardField, wavesField, holdField, maxHoldField;
    Image leftButton, rightButton, bothButton, bendButton, straightenButton, alternateButton;
    Text hintText, errorText, legNote, subtitleText;
    const string DefaultSubtitle = "BEACH WAVES   ·   knee rehabilitation session";
    WaveGameManager.LegMode legMode;
    WaveGameManager.Movement movement;

    static readonly Color Dim = new Color(0.04f, 0.12f, 0.22f, 0.92f);
    static readonly Color FieldColor = new Color(0.01f, 0.06f, 0.12f, 1f);
    static readonly Color Off = new Color(0.09f, 0.22f, 0.35f, 1f);
    static readonly Color On = new Color(0.16f, 0.6f, 0.95f, 1f);

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

    /// <param name="subtitle">Line under the title, e.g. the patient and stage chosen in the start menu.</param>
    public void Show(string subtitle = null)
    {
        nameField.text = game.patientId;
        // Started from the menu: the record is already chosen, so the name cannot be changed here.
        nameField.interactable = !(SessionConfig.Active && SessionConfig.Game == TrainingGame.Beach);
        minField.text = game.minTargetAngle.ToString("0.#", CultureInfo.InvariantCulture);
        maxField.text = game.maxTargetAngle.ToString("0.#", CultureInfo.InvariantCulture);
        straightEasyField.text = game.straightenEasiestAngle.ToString("0.#", CultureInfo.InvariantCulture);
        straightHardField.text = game.straightenHardestAngle.ToString("0.#", CultureInfo.InvariantCulture);
        wavesField.text = game.wavesPerSession.ToString(CultureInfo.InvariantCulture);
        holdField.text = game.startHoldSeconds.ToString("0.#", CultureInfo.InvariantCulture);
        maxHoldField.text = game.maxHoldSeconds.ToString("0.#", CultureInfo.InvariantCulture);
        SetLeg(game.legMode);
        SetMovement(game.movement);
        hintText.text = $"Bend: the knee must bend to the target or more.  Straighten: it must straighten to the target or less.\n" +
                        $"Safety limit is {game.safetyCeilingAngle:0}°; bend targets are kept at least 5° below it.";
        errorText.text = "";
        subtitleText.text = subtitle ?? DefaultSubtitle;
        PlaceInFront(root, 1.3f);
        root.SetActive(true);
    }

    public void Hide() => root.SetActive(false);

    void OnStartClicked()
    {
        var c = CultureInfo.InvariantCulture;
        float min = ParseFloat(minField.text), max = ParseFloat(maxField.text);
        float straightEasy = ParseFloat(straightEasyField.text), straightHard = ParseFloat(straightHardField.text);
        float hold = ParseFloat(holdField.text), maxHold = ParseFloat(maxHoldField.text);
        int.TryParse(wavesField.text, NumberStyles.Integer, c, out int waves);
        float top = game.safetyCeilingAngle - 5f;

        string error = null;
        if (string.IsNullOrWhiteSpace(nameField.text)) error = "Enter the patient's name.";
        else if (min < 5f) error = "Bend: easiest angle must be at least 5°.";
        else if (max <= min) error = "Bend: hardest angle must be greater than the easiest.";
        else if (max > top) error = $"Bend: hardest angle can't be above {top:0}° (safety limit).";
        else if (straightHard < 0f) error = "Straighten: hardest angle can't be below 0° (a straight leg).";
        else if (straightEasy <= straightHard) error = "Straighten: easiest angle must be greater than the hardest.";
        else if (straightEasy > top) error = $"Straighten: easiest angle can't be above {top:0}°.";
        else if (waves < 1 || waves > 50) error = "Number of targets must be between 1 and 50.";
        else if (hold < 0.5f) error = "Hold time must be at least 0.5 s.";
        else if (maxHold < hold) error = "Max hold time must be at least the starting hold time.";

        if (error != null)
        {
            errorText.text = error;
            UISound.Error();
            return;
        }
        UISound.Confirm();

        game.ApplyDoctorSettings(nameField.text.Trim(), legMode, movement, min, max, straightEasy, straightHard, waves, hold, maxHold);
        Hide();
        game.RequestStart();
    }

    void SetLeg(WaveGameManager.LegMode value)
    {
        legMode = value;
        Paint(leftButton, legMode == WaveGameManager.LegMode.Left);
        Paint(rightButton, legMode == WaveGameManager.LegMode.Right);
        Paint(bothButton, legMode == WaveGameManager.LegMode.Both);
        legNote.text = legMode == WaveGameManager.LegMode.Both
            ? "Right leg first, then the left. The patient is told which leg is next." : "";
    }

    void SetMovement(WaveGameManager.Movement value)
    {
        movement = value;
        Paint(bendButton, movement == WaveGameManager.Movement.Bend);
        Paint(straightenButton, movement == WaveGameManager.Movement.Straighten);
        Paint(alternateButton, movement == WaveGameManager.Movement.Alternate);
    }

    /// <summary>Selected option: solid sea-blue pill with bold white text; otherwise a faint pill with muted text.</summary>
    static void Paint(Image pill, bool on)
    {
        pill.color = on ? On : Off;
        var text = pill.GetComponentInChildren<Text>();
        text.fontStyle = on ? FontStyle.Bold : FontStyle.Normal;
        text.color = on ? Color.white : Muted;
    }

    static float ParseFloat(string s)
    {
        float.TryParse((s ?? "").Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float v);
        return v;
    }

    // ---------------- UI construction ----------------

    const float CardW = 1240f, CardH = 880f, ColW = 570f;

    void Build()
    {
        var pal = UIStyle.For(UIStyle.Theme.Sea);
        root = new GameObject("Root", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.transform.SetParent(transform, false);
        ConfigureCanvas(root, inPatientView, 100);

        var rt = (RectTransform)root.transform;
        var dim = Child("Dim", rt, Dim);
        Stretch(dim);
        if (inPatientView) dim.gameObject.SetActive(false); // keep the beach visible around the card

        // Card with a sand-coloured trim, like the patient HUD.
        var edge = Child("Edge", rt, pal.edge);
        edge.sizeDelta = new Vector2(CardW + 14f, CardH + 14f);
        UIStyle.Round(edge.GetComponent<Image>(), 1f);
        var card = Child("Card", rt, pal.panel);
        card.sizeDelta = new Vector2(CardW, CardH);
        UIStyle.Round(card.GetComponent<Image>(), 1f);

        // Header
        var header = Child("Header", card, pal.header);
        header.anchoredPosition = new Vector2(0, CardH / 2f - 68f);
        header.sizeDelta = new Vector2(CardW - 28f, 112f);
        UIStyle.Round(header.GetComponent<Image>(), 0.8f);
        var title = Label(header, "Therapist setup", new Vector2(0, 16f), new Vector2(CardW - 80f, 60f), 50, TextAnchor.MiddleCenter, Color.white);
        title.fontStyle = FontStyle.Bold;
        subtitleText = Label(header, DefaultSubtitle, new Vector2(0, -32f), new Vector2(CardW - 300f, 34f), 24,
              TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.8f));

        // Back to the start menu (only when the menu is part of the build).
        if (Application.CanStreamedLevelBeLoaded(SessionConfig.MenuScene))
        {
            var back = Child("Menu", header, new Color(0f, 0f, 0f, 0.3f));
            back.anchoredPosition = new Vector2(-(CardW - 28f) / 2f + 90f, 0f);
            back.sizeDelta = new Vector2(140f, 56f);
            UIStyle.Round(back.GetComponent<Image>(), 1.2f);
            var bb = back.gameObject.AddComponent<Button>();
            bb.targetGraphic = back.GetComponent<Image>();
            Tint(bb);
            bb.onClick.AddListener(SessionConfig.ReturnToMenu);
            UISound.Attach(bb);
            var bt = Label(back, "< MENU", Vector2.zero, new Vector2(140f, 56f), 24, TextAnchor.MiddleCenter, Color.white);
            bt.fontStyle = FontStyle.Bold;
        }

        float lx = -(ColW / 2f + 15f), rx = ColW / 2f + 15f;

        // ---- Left column ----
        var patient = Section(card, "PATIENT", new Vector2(lx, 165f), new Vector2(ColW, 250f));
        Label(patient, "Patient name", new Vector2(-150f, 40f), new Vector2(220f, 50f), 28, TextAnchor.MiddleLeft, Color.white);
        nameField = Field(patient, new Vector2(115f, 40f), 300f, InputField.ContentType.Standard, TextAnchor.MiddleLeft);
        Label(patient, "Leg to train", new Vector2(-150f, -35f), new Vector2(220f, 50f), 28, TextAnchor.MiddleLeft, Color.white);
        leftButton = ToggleButton(patient, "Left", new Vector2(15f, -35f), 94f, () => SetLeg(WaveGameManager.LegMode.Left));
        rightButton = ToggleButton(patient, "Right", new Vector2(115f, -35f), 94f, () => SetLeg(WaveGameManager.LegMode.Right));
        bothButton = ToggleButton(patient, "Both", new Vector2(215f, -35f), 94f, () => SetLeg(WaveGameManager.LegMode.Both));
        legNote = Label(patient, "", new Vector2(0, -92f), new Vector2(ColW - 40f, 34f), 20, TextAnchor.MiddleCenter, Muted);

        var move = Section(card, "MOVEMENT", new Vector2(lx, -125f), new Vector2(ColW, 290f));
        bendButton = ToggleButton(move, "Bend", new Vector2(-180f, 60f), 166f, () => SetMovement(WaveGameManager.Movement.Bend));
        straightenButton = ToggleButton(move, "Straighten", new Vector2(0f, 60f), 166f, () => SetMovement(WaveGameManager.Movement.Straighten));
        alternateButton = ToggleButton(move, "Alternate", new Vector2(180f, 60f), 166f, () => SetMovement(WaveGameManager.Movement.Alternate));
        hintText = Label(move, "", new Vector2(0, -45f), new Vector2(ColW - 50f, 140f), 21, TextAnchor.MiddleCenter, Muted);
        hintText.horizontalOverflow = HorizontalWrapMode.Wrap;

        // ---- Right column ----
        var bend = Section(card, "BEND TARGETS  (°)", new Vector2(rx, 215f), new Vector2(ColW, 150f));
        minField = LabelledField(bend, "Easiest", new Vector2(-135f, -18f), 230f, InputField.ContentType.DecimalNumber);
        maxField = LabelledField(bend, "Hardest", new Vector2(135f, -18f), 230f, InputField.ContentType.DecimalNumber);

        var straight = Section(card, "STRAIGHTEN TARGETS  (°)", new Vector2(rx, 50f), new Vector2(ColW, 150f));
        straightEasyField = LabelledField(straight, "Easiest", new Vector2(-135f, -18f), 230f, InputField.ContentType.DecimalNumber);
        straightHardField = LabelledField(straight, "Hardest", new Vector2(135f, -18f), 230f, InputField.ContentType.DecimalNumber);

        var session = Section(card, "SESSION", new Vector2(rx, -130f), new Vector2(ColW, 180f));
        wavesField = LabelledField(session, "Waves", new Vector2(-180f, -25f), 160f, InputField.ContentType.IntegerNumber);
        holdField = LabelledField(session, "Start hold (s)", new Vector2(0f, -25f), 160f, InputField.ContentType.DecimalNumber);
        maxHoldField = LabelledField(session, "Max hold (s)", new Vector2(180f, -25f), 160f, InputField.ContentType.DecimalNumber);

        // ---- Footer ----
        errorText = Label(card, "", new Vector2(0, -300f), new Vector2(CardW - 80f, 40f), 26, TextAnchor.MiddleCenter, WaveHUD.Bad);
        errorText.fontStyle = FontStyle.Bold;

        var start = Child("Start", card, new Color(0.2f, 0.75f, 0.4f, 1f));
        start.anchoredPosition = new Vector2(0, -370f);
        start.sizeDelta = new Vector2(440f, 86f);
        UIStyle.Round(start.GetComponent<Image>(), 1.6f);
        var btn = start.gameObject.AddComponent<Button>();
        btn.targetGraphic = start.GetComponent<Image>();
        Tint(btn);
        btn.onClick.AddListener(OnStartClicked);
        var startText = Label(start, "START SESSION", Vector2.zero, new Vector2(440f, 86f), 40, TextAnchor.MiddleCenter, Color.white);
        startText.fontStyle = FontStyle.Bold;
        startText.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.35f);
    }

    static readonly Color Muted = new Color(1f, 1f, 1f, 0.7f);

    /// <summary>A softly filled group box with a small accent title in its top-left corner.</summary>
    static RectTransform Section(RectTransform parent, string title, Vector2 center, Vector2 size)
    {
        var box = Child("Section_" + title, parent, new Color(0.04f, 0.18f, 0.3f, 1f));
        box.anchoredPosition = center;
        box.sizeDelta = size;
        var t = Label(box, title, new Vector2(-size.x / 2f + 20f + 200f, size.y / 2f - 24f), new Vector2(400f, 32f), 22,
                      TextAnchor.MiddleLeft, WaveHUD.Accent);
        t.fontStyle = FontStyle.Bold;
        return box;
    }

    InputField LabelledField(RectTransform parent, string label, Vector2 pos, float width, InputField.ContentType type)
    {
        Label(parent, label, pos + new Vector2(0f, 45f), new Vector2(width, 30f), 22, TextAnchor.MiddleCenter, Muted);
        return Field(parent, pos, width, type, TextAnchor.MiddleCenter);
    }

    /// <summary>Dark rounded input box with a faint outline; the outline lights up while editing.</summary>
    InputField Field(RectTransform parent, Vector2 pos, float width, InputField.ContentType type, TextAnchor align)
    {
        var box = Child("Field", parent, FieldColor);
        box.anchoredPosition = pos;
        box.sizeDelta = new Vector2(width, 54f);
        var outline = box.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(1f, 1f, 1f, 0.22f);
        outline.effectDistance = new Vector2(2f, -2f);

        var text = Label(box, "", Vector2.zero, new Vector2(width - 28f, 54f), 30, align, Color.white);
        text.fontStyle = FontStyle.Bold;
        text.supportRichText = false;
        var input = box.gameObject.AddComponent<InputField>();
        input.targetGraphic = box.GetComponent<Image>();
        input.textComponent = text;
        input.contentType = type;
        input.selectionColor = new Color(0.25f, 0.65f, 1f, 0.5f);
        input.caretColor = WaveHUD.Accent;
        input.customCaretColor = true;
        input.caretWidth = 3;
        var colors = input.colors;
        colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
        colors.selectedColor = new Color(1.5f, 1.5f, 1.6f, 1f);
        input.colors = colors;
        return input;
    }

    Image ToggleButton(RectTransform parent, string label, Vector2 pos, float width, UnityEngine.Events.UnityAction onClick)
    {
        var rt = Child("Toggle_" + label, parent, Off);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(width, 52f);
        UIStyle.Round(rt.GetComponent<Image>(), 1.1f);
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = rt.GetComponent<Image>();
        Tint(btn);
        btn.onClick.AddListener(onClick);
        UISound.Attach(btn, true);
        Label(rt, label, Vector2.zero, new Vector2(width, 52f), 26, TextAnchor.MiddleCenter, Muted);
        return rt.GetComponent<Image>();
    }

    /// <summary>Hover brightens, press darkens.</summary>
    static void Tint(Button b)
    {
        var c = b.colors;
        c.highlightedColor = new Color(1.2f, 1.2f, 1.2f, 1f);
        c.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
        c.selectedColor = Color.white;
        c.colorMultiplier = 1.3f;
        c.fadeDuration = 0.08f;
        b.colors = c;
    }

    internal static RectTransform Child(string name, RectTransform parent, Color color)
    {
        var rt = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        var img = rt.GetComponent<Image>();
        img.color = color;
        // Rounded corners everywhere except full-screen dims (those are stretched later and stay square at the edges).
        if (name != "Dim") UIStyle.Round(img, 0.6f);
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
