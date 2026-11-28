using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Therapist setup screen for the Training Ground, shown before every session (mouse + keyboard on the PC),
/// like DoctorPanel for the beach. Built at runtime, so the scene only needs the KickGameManager.
/// </summary>
public class FootballSetupPanel : MonoBehaviour
{
    const float CardW = 1240f, CardH = 860f;
    static readonly Color FieldColor = new Color(0.01f, 0.06f, 0.12f, 1f);
    static readonly Color Off = new Color(0.09f, 0.22f, 0.35f, 1f);
    static readonly Color On = new Color(0.16f, 0.6f, 0.95f, 1f);

    KickGameManager game;
    GameObject root;
    InputField nameField, comfortField, safetyField, kicksField, minField, maxField, holdField, attemptField,
               extensionField, toleranceField, leanField, restField;
    Image leftButton, rightButton, supportButton;
    Text subtitle, error;
    KickGameManager.Leg leg;
    bool support;

    public static FootballSetupPanel Create(KickGameManager game)
    {
        if (!FindFirstObjectByType<EventSystem>())
        {
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            es.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }
        var go = new GameObject("FootballSetupPanel");
        go.transform.SetParent(game.transform, false);
        var panel = go.AddComponent<FootballSetupPanel>();
        panel.game = game;
        panel.Build();
        return panel;
    }

    public void Show(string line)
    {
        subtitle.text = line;
        nameField.text = game.patientId;
        nameField.interactable = !SessionConfig.Active; // the menu already chose the record
        Set(comfortField, game.comfortableMaxAngle);
        Set(safetyField, game.safetyCeilingAngle);
        Set(kicksField, game.kicksPerSession);
        Set(minField, game.minWindUpPercent * 100f);
        Set(maxField, game.maxWindUpPercent * 100f);
        Set(holdField, game.windUpHoldSeconds);
        Set(attemptField, game.attemptSeconds);
        Set(extensionField, game.fullExtensionAngle);
        Set(toleranceField, game.angleTolerance);
        Set(leanField, (game.usesSupport ? game.maxLeanWithSupport : game.maxLean) * 100f);
        Set(restField, game.restSeconds);
        SetLeg(game.operatedLeg);
        support = game.usesSupport;
        PaintSupport();
        error.text = "";
        root.SetActive(true);
    }

    static void Set(InputField f, float v) => f.text = v.ToString("0.#", CultureInfo.InvariantCulture);

    static float Get(InputField f)
    {
        float.TryParse((f.text ?? "").Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float v);
        return v;
    }

    void OnStart()
    {
        float comfort = Get(comfortField), safety = Get(safetyField), kicks = Get(kicksField);
        float min = Get(minField), max = Get(maxField), hold = Get(holdField), attempt = Get(attemptField);
        float extension = Get(extensionField), tolerance = Get(toleranceField), lean = Get(leanField), rest = Get(restField);

        string problem =
            string.IsNullOrWhiteSpace(nameField.text) ? "Enter the patient's name."
            : safety < 40f || safety > 110f ? "Safety limit must be between 40 and 110 degrees."
            : comfort < 20f || comfort > 140f ? "Comfortable max bend must be between 20 and 140 degrees."
            : kicks < 1f || kicks > 50f ? "Kicks must be between 1 and 50."
            : min < 20f || max > 100f || max < min ? "Wind-up: 20-100 %, and the highest not below the lowest."
            : hold < 0f || hold > 10f ? "Hold must be between 0 and 10 s."
            : attempt < 3f || attempt > 60f ? "Time per kick must be between 3 and 60 s."
            : extension < 0f || extension > 40f ? "Straight-leg angle must be between 0 and 40 degrees."
            : tolerance < 0f || tolerance > 15f ? "Tolerance must be between 0 and 15 degrees."
            : lean < 10f || lean > 80f ? "Lean limit must be between 10 and 80 cm."
            : rest < 0f || rest > 60f ? "Rest must be between 0 and 60 s."
            : null;
        if (problem != null)
        {
            error.text = problem;
            UISound.Error();
            return;
        }

        UISound.Confirm();
        game.patientId = nameField.text.Trim();
        game.operatedLeg = leg;
        game.comfortableMaxAngle = comfort;
        game.safetyCeilingAngle = safety;
        game.kicksPerSession = Mathf.RoundToInt(kicks);
        game.minWindUpPercent = min / 100f;
        game.maxWindUpPercent = max / 100f;
        game.startWindUpPercent = Mathf.Clamp(game.startWindUpPercent, game.minWindUpPercent, game.maxWindUpPercent);
        game.windUpHoldSeconds = hold;
        game.attemptSeconds = attempt;
        game.fullExtensionAngle = extension;
        game.angleTolerance = tolerance;
        game.usesSupport = support;
        if (support) game.maxLeanWithSupport = lean / 100f; else game.maxLean = lean / 100f;
        game.restSeconds = rest;
        if (game.cameraSensor) game.cameraSensor.useLeftLeg = leg == KickGameManager.Leg.Left;
        root.SetActive(false);
        game.RequestStart();
    }

    void SetLeg(KickGameManager.Leg value)
    {
        leg = value;
        Paint(leftButton, leg == KickGameManager.Leg.Left);
        Paint(rightButton, leg == KickGameManager.Leg.Right);
    }

    void PaintSupport()
    {
        Paint(supportButton, support);
        supportButton.GetComponentInChildren<Text>().text = support ? "Holds a support: YES" : "Holds a support: NO";
    }

    static void Paint(Image pill, bool on) => pill.color = on ? On : Off;

    void Build()
    {
        var pal = UIStyle.For(UIStyle.Theme.Football);
        root = new GameObject("Root", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.transform.SetParent(transform, false);
        DoctorPanel.ConfigureCanvas(root, false, 100);

        var rt = (RectTransform)root.transform;
        var edge = DoctorPanel.Child("Edge", rt, pal.edge);
        edge.sizeDelta = new Vector2(CardW + 14f, CardH + 14f);
        var card = DoctorPanel.Child("Card", rt, pal.panel);
        card.sizeDelta = new Vector2(CardW, CardH);
        var header = DoctorPanel.Child("Header", card, pal.header);
        header.anchoredPosition = new Vector2(0, CardH / 2f - 68f);
        header.sizeDelta = new Vector2(CardW - 28f, 112f);
        DoctorPanel.Label(header, "Therapist setup  ·  Penalty practice", new Vector2(0, 16f), new Vector2(CardW - 360f, 60f), 42,
                          TextAnchor.MiddleCenter, Color.white);
        subtitle = DoctorPanel.Label(header, "", new Vector2(0, -32f), new Vector2(CardW - 360f, 34f), 22,
                                     TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.85f));
        if (Application.CanStreamedLevelBeLoaded(SessionConfig.MenuScene))
            MakeButton(header, "< MENU", new Vector2(-(CardW - 28f) / 2f + 90f, 0f), new Vector2(140f, 56f),
                       new Color(0f, 0f, 0f, 0.3f), SessionConfig.ReturnToMenu);

        const float x1 = -400f, x2 = 0f, x3 = 400f;
        Caption(card, "Patient name", x1, 250f);
        nameField = Field(card, x1, 205f);
        Caption(card, "Operated leg", x2, 250f);
        leftButton = MakeButton(card, "Left", new Vector2(x2 - 85f, 205f), new Vector2(160f, 54f), Off, () => SetLeg(KickGameManager.Leg.Left));
        rightButton = MakeButton(card, "Right", new Vector2(x2 + 85f, 205f), new Vector2(160f, 54f), Off, () => SetLeg(KickGameManager.Leg.Right));
        Caption(card, "Balance", x3, 250f);
        supportButton = MakeButton(card, "Support", new Vector2(x3, 205f), new Vector2(330f, 54f), Off, () => { support = !support; PaintSupport(); });

        comfortField = Number(card, "Comfortable max bend (°)", x1, 90f);
        safetyField = Number(card, "Safety limit (°)", x2, 90f);
        kicksField = Number(card, "Kicks", x3, 90f);
        minField = Number(card, "Wind-up lowest (% of max)", x1, -20f);
        maxField = Number(card, "Wind-up highest (% of max)", x2, -20f);
        holdField = Number(card, "Hold before kick (s)", x3, -20f);
        extensionField = Number(card, "Straight leg is below (°)", x1, -130f);
        toleranceField = Number(card, "Angle tolerance (°)", x2, -130f);
        attemptField = Number(card, "Time per kick (s)", x3, -130f);
        leanField = Number(card, "Lean limit (cm)", x1, -240f);
        restField = Number(card, "Rest between kicks (s)", x2, -240f);

        error = DoctorPanel.Label(card, "", new Vector2(0, -305f), new Vector2(CardW - 80f, 36f), 26, TextAnchor.MiddleCenter, WaveHUD.Bad);
        MakeButton(card, "START SESSION", new Vector2(0, -370f), new Vector2(440f, 80f), new Color(0.2f, 0.75f, 0.4f, 1f), OnStart);
        root.SetActive(false);
    }

    static void Caption(RectTransform parent, string text, float x, float y) =>
        DoctorPanel.Label(parent, text, new Vector2(x, y), new Vector2(360f, 30f), 22, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.75f));

    InputField Number(RectTransform parent, string caption, float x, float y)
    {
        Caption(parent, caption, x, y + 45f);
        var f = Field(parent, x, y);
        f.contentType = InputField.ContentType.DecimalNumber;
        return f;
    }

    static InputField Field(RectTransform parent, float x, float y)
    {
        var box = DoctorPanel.Child("Field", parent, FieldColor);
        box.anchoredPosition = new Vector2(x, y);
        box.sizeDelta = new Vector2(330f, 54f);
        var text = DoctorPanel.Label(box, "", Vector2.zero, new Vector2(300f, 54f), 30, TextAnchor.MiddleCenter, Color.white);
        text.supportRichText = false;
        var input = box.gameObject.AddComponent<InputField>();
        input.targetGraphic = box.GetComponent<Image>();
        input.textComponent = text;
        input.customCaretColor = true;
        input.caretColor = WaveHUD.Accent;
        input.caretWidth = 3;
        return input;
    }

    static Image MakeButton(RectTransform parent, string label, Vector2 pos, Vector2 size, Color color, UnityEngine.Events.UnityAction onClick)
    {
        var rt = DoctorPanel.Child("Button_" + label, parent, color);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var img = rt.GetComponent<Image>();
        var button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = img;
        button.onClick.AddListener(onClick);
        UISound.Attach(button);
        DoctorPanel.Label(rt, label, Vector2.zero, size, 28, TextAnchor.MiddleCenter, Color.white);
        return img;
    }
}
