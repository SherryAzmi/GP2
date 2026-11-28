using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Start menu for the therapist's screen (mouse + keyboard), in the same style as the therapist setup panel:
/// sand-trimmed card, blue header, navy sections with yellow section titles, dark input fields.
///
/// First a title screen with START, then a clinic-style dashboard with a sidebar:
///   Home          - overview: patients, sessions this week, training time, recent sessions of everyone.
///   Patients      - searchable patient table; "+ New patient" in the header.
///   Patient file  - details (surgery, therapist, phone, height/weight...), key numbers, knee-bend chart, last sessions.
///   Start session - one card per game with its five stages (passed / open / locked).
///   Progress      - charts: knee bend over time, score over time, left vs right leg.
///   Session log   - every session with duration, leg, best / average bend, success, score and safety alerts.
///   Settings      - therapist name, training rules, where the data is saved.
/// Builds itself at runtime; the scene only needs a camera and this component.
/// </summary>
public class MainMenu : MonoBehaviour
{
    enum Page { Home, Patients, PatientFile, Form, Start, Progress, Log, Settings }

    // Therapist-panel palette
    static readonly Color Dim = new Color(0.02f, 0.06f, 0.12f, 1f);
    static readonly Color CardCol = new Color(0.02f, 0.13f, 0.24f, 1f);
    static readonly Color Edge = new Color(0.98f, 0.86f, 0.6f, 1f);
    static readonly Color HeaderCol = new Color(0.05f, 0.45f, 0.65f, 1f);
    static readonly Color Section = new Color(0.04f, 0.18f, 0.3f, 1f);
    static readonly Color Row = new Color(0.06f, 0.22f, 0.36f, 1f);
    static readonly Color FieldCol = new Color(0.01f, 0.06f, 0.12f, 1f);
    static readonly Color Off = new Color(0.09f, 0.22f, 0.35f, 1f);
    static readonly Color Blue = new Color(0.16f, 0.6f, 0.95f, 1f);
    static readonly Color Gold = new Color(1f, 0.85f, 0.25f, 1f);
    static readonly Color Green = new Color(0.2f, 0.75f, 0.4f, 1f);
    static readonly Color GoodText = new Color(0.36f, 0.9f, 0.52f, 1f);
    static readonly Color Muted = new Color(1f, 1f, 1f, 0.7f);
    static readonly Color Locked = new Color(0.12f, 0.2f, 0.28f, 1f);
    static readonly Color SeaLine = new Color(0.3f, 0.75f, 1f, 1f);
    static readonly Color PitchLine = new Color(0.45f, 0.9f, 0.45f, 1f);
    const string TherapistKey = "GP2.TherapistName";

    const float CW = 1530f, CH = 885f; // content area
    static readonly Vector2 ContentCenter = new Vector2(155f, -57f);

    RectTransform canvasRoot, titleScreen, dashboard;
    readonly Dictionary<Page, RectTransform> pages = new Dictionary<Page, RectTransform>();
    readonly Dictionary<Page, Image> navButtons = new Dictionary<Page, Image>();
    RectTransform noPatient;
    PatientRecord selected;
    Page current;

    Text headerSub, sideName, sideInfo;

    void Start()
    {
        if (!FindFirstObjectByType<EventSystem>())
        {
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            es.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }
        BuildCanvas();
        BuildTitle();
        BuildDashboard();

        // Coming back from a session: straight to that patient's file.
        var back = SessionConfig.PatientId != null ? PatientStore.Get(SessionConfig.PatientId) : null;
        if (back != null)
        {
            ShowDashboard();
            SelectPatient(back);
            Show(Page.PatientFile);
        }
        else
        {
            titleScreen.gameObject.SetActive(true);
            dashboard.gameObject.SetActive(false);
        }
    }

    // ================= Navigation =================

    void ShowDashboard()
    {
        titleScreen.gameObject.SetActive(false);
        dashboard.gameObject.SetActive(true);
        RefreshHeader();
        Show(Page.Home);
    }

    static bool NeedsPatient(Page p) => p == Page.PatientFile || p == Page.Start || p == Page.Progress || p == Page.Log;

    void Show(Page page)
    {
        current = page;
        bool blocked = NeedsPatient(page) && selected == null;
        foreach (var kv in pages) kv.Value.gameObject.SetActive(!blocked && kv.Key == page);
        noPatient.gameObject.SetActive(blocked);
        foreach (var kv in navButtons)
        {
            bool on = kv.Key == page || (page == Page.Form && kv.Key == Page.PatientFile);
            kv.Value.color = on ? Blue : Off;
            var t = kv.Value.GetComponentInChildren<Text>();
            t.color = on ? Color.white : Muted;
            t.fontStyle = on ? FontStyle.Bold : FontStyle.Normal;
        }
        if (blocked) return;
        switch (page)
        {
            case Page.Home: RefreshHome(); break;
            case Page.Patients: RefreshPatients(); break;
            case Page.PatientFile: RefreshFile(); break;
            case Page.Start: RefreshStart(); break;
            case Page.Progress: RefreshProgress(); break;
            case Page.Log: RefreshLog(); break;
            case Page.Settings: RefreshSettings(); break;
        }
    }

    void SelectPatient(PatientRecord p)
    {
        selected = p;
        if (p != null)
        {
            beachCard.chosen = p.beach.unlockedStage;
            footballCard.chosen = p.football.unlockedStage;
        }
        RefreshSidebar();
    }

    void RefreshHeader()
    {
        string therapist = PlayerPrefs.GetString(TherapistKey, "");
        headerSub.text = System.DateTime.Now.ToString("dddd d MMMM yyyy", CultureInfo.InvariantCulture) +
                         (therapist.Length > 0 ? $"   ·   therapist: {therapist}" : "");
    }

    void RefreshSidebar()
    {
        if (selected == null)
        {
            sideName.text = "None selected";
            sideInfo.text = "Choose one in Patients";
            return;
        }
        sideName.text = selected.name;
        var bits = new List<string>();
        if (selected.age > 0) bits.Add($"Age {selected.age}");
        bits.Add($"{selected.operatedLeg} knee");
        int w = selected.WeeksSinceSurgery;
        if (w >= 0) bits.Add($"week {w} after surgery");
        sideInfo.text = string.Join("  ·  ", bits);
    }

    // ================= Canvas, title, frame =================

    void BuildCanvas()
    {
        var root = new GameObject("MenuCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.transform.SetParent(transform, false);
        root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasRoot = (RectTransform)root.transform;
        var bg = Box("Background", canvasRoot, Vector2.zero, Vector2.zero, Dim, 0f).rectTransform;
        bg.anchorMin = Vector2.zero; bg.anchorMax = Vector2.one; bg.offsetMin = bg.offsetMax = Vector2.zero;
    }

    /// <summary>A card in the therapist-panel style: sand trim, navy body, blue header bar.</summary>
    RectTransform PanelCard(Transform parent, Vector2 pos, Vector2 size, float headerH, out RectTransform header)
    {
        Box("Edge", parent, pos, size + new Vector2(14f, 14f), Edge, 1f).raycastTarget = false;
        var card = Box("Card", parent, pos, size, CardCol, 1f).rectTransform;
        header = Box("Header", card, new Vector2(0, size.y / 2f - 14f - headerH / 2f), new Vector2(size.x - 28f, headerH), HeaderCol, 0.8f).rectTransform;
        return card;
    }

    void BuildTitle()
    {
        titleScreen = UIStyle.Rect("TitleScreen", canvasRoot, Vector2.zero, new Vector2(1920f, 1080f));
        var card = PanelCard(titleScreen, new Vector2(0, 20f), new Vector2(1100f, 700f), 200f, out var header);
        Lbl(header, "KNEE REHAB VR", new Vector2(0, 30f), new Vector2(1000f, 100f), 84, Color.white, true);
        Lbl(header, "Virtual reality knee rehabilitation  ·  therapist dashboard", new Vector2(0, -50f), new Vector2(1000f, 40f), 28, new Color(1f, 1f, 1f, 0.85f));

        var games = Box("Games", card, new Vector2(0, 40f), new Vector2(980f, 150f), Section, 0.8f).transform;
        Lbl(games, "TRAINING GAMES", new Vector2(-340f, 45f), new Vector2(260f, 30f), 22, Gold, true, TextAnchor.MiddleLeft);
        Lbl(games, "BEACH WAVES", new Vector2(-230f, -5f), new Vector2(420f, 44f), 34, SeaLine, true);
        Lbl(games, "Seated  ·  bend and straighten", new Vector2(-230f, -42f), new Vector2(420f, 30f), 21, Muted);
        Lbl(games, "PENALTY PRACTICE", new Vector2(230f, -5f), new Vector2(420f, 44f), 34, PitchLine, true);
        Lbl(games, "Standing  ·  wind up and kick", new Vector2(230f, -42f), new Vector2(420f, 30f), 21, Muted);

        var start = Button(card, "START", new Vector2(0, -150f), new Vector2(520f, 120f), Green, Color.white, ShowDashboard, 64);
        start.gameObject.AddComponent<Pulse>();
        Lbl(card, System.DateTime.Now.ToString("dddd d MMMM yyyy", CultureInfo.InvariantCulture) + "   ·   use the mouse",
             new Vector2(0, -290f), new Vector2(1000f, 34f), 22, Muted);
    }

    void BuildDashboard()
    {
        dashboard = UIStyle.Rect("Dashboard", canvasRoot, Vector2.zero, new Vector2(1920f, 1080f));
        var card = PanelCard(dashboard, Vector2.zero, new Vector2(1866f, 1026f), 100f, out var header);
        Lbl(header, "Knee Rehab VR   ·   therapist dashboard", new Vector2(-380f, 18f), new Vector2(1000f, 50f), 38, Color.white, true, TextAnchor.MiddleLeft);
        headerSub = Lbl(header, "", new Vector2(-380f, -26f), new Vector2(1000f, 30f), 22, new Color(1f, 1f, 1f, 0.85f), false, TextAnchor.MiddleLeft);
        Button(header, "+  NEW PATIENT", new Vector2(780f, 0f), new Vector2(270f, 64f), Green, Color.white, () => OpenForm(true), 26);
        Button(header, "EXIT", new Vector2(590f, 0f), new Vector2(110f, 64f), new Color(0f, 0f, 0f, 0.3f), Color.white, () =>
        {
            dashboard.gameObject.SetActive(false);
            titleScreen.gameObject.SetActive(true);
        }, 22);

        BuildSidebar(card);

        foreach (Page p in System.Enum.GetValues(typeof(Page)))
            pages[p] = UIStyle.Rect(p.ToString(), card, ContentCenter, new Vector2(CW, CH));

        noPatient = SectionBox(card, "NO PATIENT SELECTED", ContentCenter, new Vector2(CW, 300f));
        Lbl(noPatient, "This page shows one patient's information. Choose a patient first.", new Vector2(0, 10f), new Vector2(CW - 100f, 40f), 28, Muted);
        Button(noPatient, "OPEN PATIENTS", new Vector2(0, -70f), new Vector2(340f, 70f), Blue, Color.white, () => Show(Page.Patients), 28);

        BuildHome(pages[Page.Home]);
        BuildPatients(pages[Page.Patients]);
        BuildFile(pages[Page.PatientFile]);
        BuildForm(pages[Page.Form]);
        BuildStart(pages[Page.Start]);
        BuildProgress(pages[Page.Progress]);
        BuildLog(pages[Page.Log]);
        BuildSettings(pages[Page.Settings]);
        RefreshSidebar();
    }

    void BuildSidebar(RectTransform card)
    {
        var side = SectionBox(card, "MENU", new Vector2(-770f, -57f), new Vector2(290f, CH));
        var items = new (Page page, string label)[]
        {
            (Page.Home, "Home"), (Page.Patients, "Patients"), (Page.PatientFile, "Patient file"),
            (Page.Start, "Start session"), (Page.Progress, "Progress"), (Page.Log, "Session log"), (Page.Settings, "Settings"),
        };
        float y = CH / 2f - 95f;
        foreach (var item in items)
        {
            var page = item.page;
            var img = Box("Nav_" + item.label, side, new Vector2(0, y), new Vector2(250f, 62f), Off, 1f);
            var b = img.gameObject.AddComponent<UnityEngine.UI.Button>();
            b.targetGraphic = img;
            Tint(b);
            b.onClick.AddListener(() => Show(page));
            UISound.Attach(b);
            Lbl(img.transform, item.label, new Vector2(10f, 0), new Vector2(210f, 62f), 26, Muted, false, TextAnchor.MiddleLeft);
            navButtons[page] = img;
            y -= 74f;
        }

        var cur = Box("Current", side, new Vector2(0, -CH / 2f + 105f), new Vector2(256f, 170f), FieldCol, 0.8f).transform;
        Lbl(cur, "CURRENT PATIENT", new Vector2(0, 58f), new Vector2(230f, 28f), 18, Gold, true, TextAnchor.MiddleLeft);
        sideName = Lbl(cur, "", new Vector2(0, 18f), new Vector2(230f, 40f), 28, Color.white, true, TextAnchor.MiddleLeft);
        sideInfo = Lbl(cur, "", new Vector2(0, -36f), new Vector2(230f, 70f), 18, Muted, false, TextAnchor.UpperLeft);
    }

    // ================= Home =================

    Text homeWelcome, homeSummary;
    Text[] homeKpis;
    RectTransform homeTable;
    UnityEngine.UI.Button continueButton;
    Text continueLabel;

    void BuildHome(RectTransform page)
    {
        var welcome = SectionBox(page, "WELCOME", new Vector2(0, CH / 2f - 85f), new Vector2(CW, 170f));
        homeWelcome = Lbl(welcome, "", new Vector2(-260f, 22f), new Vector2(960f, 50f), 38, Color.white, true, TextAnchor.MiddleLeft);
        homeSummary = Lbl(welcome, "", new Vector2(-260f, -30f), new Vector2(960f, 36f), 24, Muted, false, TextAnchor.MiddleLeft);
        Button(welcome, "OPEN PATIENTS", new Vector2(330f, 0f), new Vector2(260f, 66f), Blue, Color.white, () => Show(Page.Patients), 24);
        var cb = Button(welcome, "CONTINUE", new Vector2(615f, 0f), new Vector2(290f, 66f), Green, Color.white, null, 21);
        continueButton = cb.GetComponent<UnityEngine.UI.Button>();
        continueLabel = cb.GetComponentInChildren<Text>();

        homeKpis = KpiRow(page, CH / 2f - 255f, new[] { "Patients", "Sessions this week", "Training time this week", "Stages passed this week" });

        var recent = SectionBox(page, "RECENT SESSIONS  ·  ALL PATIENTS", new Vector2(0, -CH / 2f + 265f), new Vector2(CW, 530f));
        homeTable = UIStyle.Rect("Table", recent, new Vector2(0, -20f), new Vector2(CW - 40f, 460f));
    }

    void RefreshHome()
    {
        RefreshHeader();
        string therapist = PlayerPrefs.GetString(TherapistKey, "");
        int hour = System.DateTime.Now.Hour;
        homeWelcome.text = (hour < 12 ? "Good morning" : hour < 18 ? "Good afternoon" : "Good evening") + (therapist.Length > 0 ? $", {therapist}" : "");

        var all = new List<(PatientRecord p, SessionEntry e)>();
        foreach (var p in PatientStore.All) foreach (var e in p.history) all.Add((p, e));
        all.Sort((a, b) => string.CompareOrdinal(b.e.date, a.e.date));
        var weekAgo = System.DateTime.UtcNow.AddDays(-7);
        int week = 0, today = 0, passed = 0;
        float minutes = 0f;
        foreach (var x in all)
        {
            if (!System.DateTime.TryParse(x.e.date, null, DateTimeStyles.RoundtripKind, out var d)) continue;
            if (d >= weekAgo)
            {
                week++;
                minutes += x.e.durationSeconds / 60f;
                if (x.e.passed) passed++;
            }
            if (d.ToLocalTime().Date == System.DateTime.Today) today++;
        }
        int n = PatientStore.All.Count;
        homeSummary.text = n == 0 ? "No patients yet. Press  + NEW PATIENT  to add the first one."
                                  : $"{n} patient{(n == 1 ? "" : "s")}   ·   {today} session{(today == 1 ? "" : "s")} today   ·   {all.Count} session{(all.Count == 1 ? "" : "s")} in total";
        homeKpis[0].text = n.ToString();
        homeKpis[1].text = week.ToString();
        homeKpis[2].text = $"{minutes:0} min";
        homeKpis[3].text = passed.ToString();

        // Continue with the most recently seen patient.
        PatientRecord last = null;
        foreach (var p in PatientStore.All)
            if (last == null || string.CompareOrdinal(p.lastVisit ?? p.created ?? "", last.lastVisit ?? last.created ?? "") > 0) last = p;
        continueButton.gameObject.SetActive(last != null);
        continueButton.onClick.RemoveAllListeners();
        if (last != null)
        {
            continueLabel.text = "CONTINUE: " + Short(last.name, 10).ToUpperInvariant();
            var captured = last;
            continueButton.onClick.AddListener(() => { SelectPatient(captured); Show(Page.PatientFile); });
        }

        var rows = new List<string[]>();
        var clicks = new List<UnityEngine.Events.UnityAction>();
        for (int i = 0; i < Mathf.Min(9, all.Count); i++)
        {
            var (p, e) = all[i];
            rows.Add(new[] { DateTimeText(e.date), p.name, GameName(e.game), "S" + e.stage, e.leg ?? "-",
                             Duration(e.durationSeconds), $"{e.bestAngle:0}°", SuccessText(e) });
            var captured = p;
            clicks.Add(() => { SelectPatient(captured); Show(Page.PatientFile); });
        }
        Table(homeTable, new[] { ("Date", 200f), ("Patient", 260f), ("Game", 200f), ("Stage", 100f), ("Leg", 120f), ("Time", 130f), ("Best bend", 150f), ("Success", 330f) },
              rows, 44f, clicks, "No sessions yet. Start one from a patient's file.");
    }

    // ================= Patients =================

    InputField search;
    RectTransform patientTable;

    void BuildPatients(RectTransform page)
    {
        var top = SectionBox(page, "FIND A PATIENT", new Vector2(0, CH / 2f - 70f), new Vector2(CW, 140f));
        search = Field(top, new Vector2(-180f, -14f), 1050f, "Type a name to search", InputField.ContentType.Standard);
        search.onValueChanged.AddListener(_ => RefreshPatients());
        Button(top, "+  NEW PATIENT", new Vector2(560f, -14f), new Vector2(300f, 62f), Green, Color.white, () => OpenForm(true), 26);

        var list = SectionBox(page, "ALL PATIENTS  ·  click a row to open the patient file", new Vector2(0, -85f), new Vector2(CW, 715f));
        patientTable = UIStyle.Rect("Table", list, new Vector2(0, -20f), new Vector2(CW - 40f, 650f));
    }

    void RefreshPatients()
    {
        string q = (search.text ?? "").Trim().ToLowerInvariant();
        var patients = new List<PatientRecord>(PatientStore.All);
        patients.Sort((a, b) => string.CompareOrdinal(b.lastVisit ?? b.created ?? "", a.lastVisit ?? a.created ?? ""));
        var rows = new List<string[]>();
        var clicks = new List<UnityEngine.Events.UnityAction>();
        foreach (var p in patients)
        {
            if (q.Length > 0 && !p.name.ToLowerInvariant().Contains(q)) continue;
            int w = p.WeeksSinceSurgery;
            rows.Add(new[]
            {
                p.name, p.age > 0 ? p.age.ToString() : "-", p.operatedLeg, Empty(p.surgeryType), w >= 0 ? $"week {w}" : "-",
                Empty(p.therapist), p.history.Count.ToString(), ShortDate(p.lastVisit), $"S{p.beach.unlockedStage} / S{p.football.unlockedStage}",
            });
            var captured = p;
            clicks.Add(() => { SelectPatient(captured); Show(Page.PatientFile); });
        }
        Table(patientTable, new[] { ("Name", 250f), ("Age", 80f), ("Knee", 100f), ("Surgery", 240f), ("Since surgery", 170f),
                                    ("Therapist", 200f), ("Sessions", 120f), ("Last visit", 150f), ("Stages", 180f) },
              rows, 52f, clicks, PatientStore.All.Count == 0 ? "No patients yet. Press  + NEW PATIENT." : "No patient matches the search.");
    }

    // ================= Patient file =================

    Text fileName, fileDetails;
    Text[] fileKpis;
    RectTransform fileChart, fileTable;

    void BuildFile(RectTransform page)
    {
        var details = SectionBox(page, "PATIENT DETAILS", new Vector2(-CW / 4f - 5f, CH / 2f - 170f), new Vector2(CW / 2f - 10f, 340f));
        fileName = Lbl(details, "", new Vector2(0, 100f), new Vector2(CW / 2f - 60f, 50f), 38, Color.white, true, TextAnchor.MiddleLeft);
        fileDetails = Lbl(details, "", new Vector2(0, -15f), new Vector2(CW / 2f - 60f, 190f), 22, Color.white, false, TextAnchor.UpperLeft);
        fileDetails.supportRichText = true;
        fileDetails.lineSpacing = 1.15f;
        Button(details, "EDIT PROFILE", new Vector2(-170f, -135f), new Vector2(240f, 56f), Off, Color.white, () => OpenForm(false), 22);
        Button(details, "START SESSION", new Vector2(110f, -135f), new Vector2(280f, 56f), Green, Color.white, () => Show(Page.Start), 24);

        var kpis = SectionBox(page, "KEY NUMBERS", new Vector2(CW / 4f + 5f, CH / 2f - 170f), new Vector2(CW / 2f - 10f, 340f));
        fileKpis = new Text[6];
        string[] labels = { "Sessions", "Training time", "Best knee bend", "Avg bend (last session)", "Beach stage", "Football stage" };
        for (int i = 0; i < 6; i++)
        {
            float x = -245f + (i % 3) * 245f, y = 50f - (i / 3) * 135f;
            var k = Box("Kpi", kpis, new Vector2(x, y), new Vector2(230f, 120f), FieldCol, 0.6f).transform;
            Lbl(k, labels[i], new Vector2(0, 30f), new Vector2(210f, 30f), 18, Muted, false, TextAnchor.MiddleLeft);
            fileKpis[i] = Lbl(k, "", new Vector2(0, -14f), new Vector2(210f, 54f), 34, Color.white, true, TextAnchor.MiddleLeft);
            fileKpis[i].supportRichText = true;
        }

        var chart = SectionBox(page, "KNEE BEND OVER TIME  ·  best angle per session", new Vector2(0, -25f), new Vector2(CW, 250f));
        fileChart = UIStyle.Rect("Chart", chart, new Vector2(0, -15f), new Vector2(CW - 60f, 200f));

        var last = SectionBox(page, "LAST SESSIONS", new Vector2(0, -CH / 2f + 145f), new Vector2(CW, 290f));
        fileTable = UIStyle.Rect("Table", last, new Vector2(0, -20f), new Vector2(CW - 40f, 230f));
    }

    void RefreshFile()
    {
        var p = selected;
        fileName.text = p.age > 0 ? $"{p.name}   <size=26><color=#B3C3D3>age {p.age}</color></size>" : p.name;
        fileName.supportRichText = true;
        int w = p.WeeksSinceSurgery;
        string bmi = p.heightCm > 50f && p.weightKg > 0f ? $"   ·   BMI {p.weightKg / Mathf.Pow(p.heightCm / 100f, 2f):0.0}" : "";
        fileDetails.text =
            $"<color=#FFD94A>Knee</color>   {p.operatedLeg} knee   ·   {Empty(p.surgeryType, "surgery type not set")}\n" +
            $"<color=#FFD94A>Surgery</color>   {Empty(p.surgeryDate, "date not set")}" + (w >= 0 ? $"   ·   week {w} after surgery" : "") + "\n" +
            $"<color=#FFD94A>Therapist</color>   {Empty(p.therapist)}   ·   <color=#FFD94A>Phone</color>   {Empty(p.phone)}\n" +
            $"<color=#FFD94A>Height / weight</color>   " + (p.heightCm <= 0 && p.weightKg <= 0 ? "not set"
                : $"{(p.heightCm > 0 ? $"{p.heightCm:0} cm" : "-")}   ·   {(p.weightKg > 0 ? $"{p.weightKg:0.#} kg" : "-")}{bmi}") + "\n" +
            $"<color=#FFD94A>Comfortable max bend</color>   {p.comfortableMaxAngle:0}°   ·   limit {p.SafetyLimit:0}°   ·   straighten to {p.extensionGoal:0}°\n" +
            $"<color=#FFD94A>Notes</color>   {Empty(p.notes)}";

        var h = p.history;
        float best = 0f, first = h.Count > 0 ? h[0].bestAngle : 0f;
        foreach (var e in h) best = Mathf.Max(best, e.bestAngle);
        fileKpis[0].text = h.Count.ToString();
        fileKpis[1].text = $"{p.TotalMinutes:0} min";
        fileKpis[2].text = h.Count == 0 ? "-" : $"{best:0}°" + (h.Count > 1 && best > first + 0.5f ? $" <size=20><color=#5BE584>+{best - first:0}°</color></size>" : "");
        fileKpis[3].text = h.Count == 0 || h[h.Count - 1].avgAngle <= 0f ? "-" : $"{h[h.Count - 1].avgAngle:0}°";
        fileKpis[4].text = $"{p.beach.unlockedStage} / {Stages.Count}";
        fileKpis[5].text = $"{p.football.unlockedStage} / {Stages.Count}";

        var beach = new List<float>();
        var foot = new List<float>();
        var labels = new List<string>();
        foreach (var e in h)
        {
            beach.Add(e.game == "Beach" ? e.bestAngle : -1f);
            foot.Add(e.game == "Football" ? e.bestAngle : -1f);
            labels.Add(ShortDate(e.date));
        }
        LineChart(fileChart, new[] { ("Beach waves", SeaLine, beach), ("Penalty practice", PitchLine, foot) }, labels, "°", 0f, -1f,
                  "No sessions yet. The chart fills in after the first session.");

        var rows = new List<string[]>();
        for (int i = h.Count - 1; i >= Mathf.Max(0, h.Count - 4); i--)
        {
            var e = h[i];
            rows.Add(new[] { DateTimeText(e.date), GameName(e.game), "S" + e.stage, e.leg ?? "-", Duration(e.durationSeconds),
                             $"{e.bestAngle:0}° / {(e.avgAngle > 0 ? $"{e.avgAngle:0}°" : "-")}", SuccessText(e), e.score.ToString() });
        }
        Table(fileTable, new[] { ("Date", 210f), ("Game", 220f), ("Stage", 100f), ("Leg", 110f), ("Time", 130f), ("Best / avg bend", 220f), ("Success", 270f), ("Score", 120f) },
              rows, 44f, null, "No sessions yet.");
    }

    // ================= New / edit patient =================

    Text formTitle, formNote, formSave;
    InputField fName, fAge, fPhone, fHeight, fWeight, fSurgery, fDate, fTherapist, fMax, fNotes;
    Image fLeft, fRight;
    string legChoice = "Right";
    bool formIsNew;

    void BuildForm(RectTransform page)
    {
        var title = Box("FormTitle", page, new Vector2(0, CH / 2f - 40f), new Vector2(CW, 80f), HeaderCol, 0.8f).transform;
        formTitle = Lbl(title, "NEW PATIENT", Vector2.zero, new Vector2(CW - 60f, 60f), 36, Color.white, true);

        // Two sections side by side (five rows each), like the therapist panel, then training below.
        float colW = CW / 2f - 10f, lx = -CW / 4f - 5f, rx = CW / 4f + 5f;
        var personal = SectionBox(page, "PERSONAL", new Vector2(lx, 142f), new Vector2(colW, 420f));
        fName = FormRow(personal, "Name", 130f, 420f, InputField.ContentType.Standard, "Patient's full name");
        fAge = FormRow(personal, "Age", 60f, 140f, InputField.ContentType.IntegerNumber, "-");
        fPhone = FormRow(personal, "Phone", -10f, 420f, InputField.ContentType.Standard, "Phone number");
        fHeight = FormRow(personal, "Height (cm)", -80f, 140f, InputField.ContentType.DecimalNumber, "-");
        fWeight = FormRow(personal, "Weight (kg)", -150f, 140f, InputField.ContentType.DecimalNumber, "-");

        var surgery = SectionBox(page, "SURGERY", new Vector2(rx, 142f), new Vector2(colW, 420f));
        Lbl(surgery, "Operated knee", new Vector2(-colW / 2f + 30f + 125f, 130f), new Vector2(250f, 50f), 26, Color.white, false, TextAnchor.MiddleLeft);
        fLeft = Toggle(surgery, "Left", new Vector2(-colW / 2f + 300f + 75f, 130f), 150f, () => SetLeg("Left"));
        fRight = Toggle(surgery, "Right", new Vector2(-colW / 2f + 300f + 240f, 130f), 150f, () => SetLeg("Right"));
        fSurgery = FormRow(surgery, "Surgery type", 60f, 420f, InputField.ContentType.Standard, "ACL reconstruction, knee replacement...");
        fDate = FormRow(surgery, "Surgery date", -10f, 420f, InputField.ContentType.Standard, "2026-10-02  (year-month-day)");
        fTherapist = FormRow(surgery, "Therapist", -80f, 420f, InputField.ContentType.Standard, "Therapist's name");
        Lbl(surgery, "The weeks since surgery are worked out from the date.", new Vector2(0, -150f), new Vector2(colW - 60f, 30f), 19, Muted);

        var training = SectionBox(page, "TRAINING", new Vector2(0, -183f), new Vector2(CW, 200f));
        fMax = FormRow(training, "Comfortable max bend (°)", 25f, 140f, InputField.ContentType.DecimalNumber, "90", 380f);
        Lbl(training, "Safety limit (°)", new Vector2(-65f, 25f), new Vector2(220f, 50f), 26, Color.white, false, TextAnchor.MiddleLeft);
        fLimit = Field(training, new Vector2(110f, 25f), 120f, "110", InputField.ContentType.DecimalNumber);
        Lbl(training, "Straightening goal (°)", new Vector2(345f, 25f), new Vector2(290f, 50f), 26, Color.white, false, TextAnchor.MiddleLeft);
        fGoal = Field(training, new Vector2(570f, 25f), 120f, "0", InputField.ContentType.DecimalNumber);
        fNotes = FormRow(training, "Notes", -50f, 1080f, InputField.ContentType.Standard, "Pain, walker, precautions...", 380f);

        formNote = Lbl(page, "", new Vector2(0, -310f), new Vector2(CW, 34f), 24, WaveHUD.Bad, true);
        Button(page, "CANCEL", new Vector2(-200f, -380f), new Vector2(240f, 70f), Off, Color.white, () => Show(formIsNew ? Page.Patients : Page.PatientFile), 26);
        deleteButton = Button(page, "DELETE PATIENT", new Vector2(600f, -380f), new Vector2(260f, 70f), Off, Color.white, DeletePatient, 22);
        formSave = Button(page, "CREATE PATIENT", new Vector2(110f, -380f), new Vector2(340f, 70f), Green, Color.white, SubmitForm, 28).GetComponentInChildren<Text>();
    }

    /// <summary>A caption on the left of a section and an input field after it.</summary>
    InputField FormRow(Transform section, string label, float y, float width, InputField.ContentType type, string placeholder, float labelW = 270f)
    {
        float left = -((RectTransform)section).sizeDelta.x / 2f + 30f;
        Lbl(section, label, new Vector2(left + labelW / 2f, y), new Vector2(labelW, 50f), 26, Color.white, false, TextAnchor.MiddleLeft);
        return Field(section, new Vector2(left + labelW + width / 2f, y), width, placeholder, type);
    }

    void OpenForm(bool isNew)
    {
        if (!isNew && selected == null) { Show(Page.Patients); return; }
        formIsNew = isNew;
        formTitle.text = isNew ? "NEW PATIENT" : $"EDIT PROFILE  ·  {selected.name}";
        formSave.text = isNew ? "CREATE PATIENT" : "SAVE CHANGES";
        formNote.text = "";
        var p = isNew ? null : selected;
        fName.text = p?.name ?? "";
        fAge.text = p != null && p.age > 0 ? p.age.ToString() : "";
        fPhone.text = p?.phone ?? "";
        fHeight.text = p != null && p.heightCm > 0 ? p.heightCm.ToString("0", CultureInfo.InvariantCulture) : "";
        fWeight.text = p != null && p.weightKg > 0 ? p.weightKg.ToString("0.#", CultureInfo.InvariantCulture) : "";
        fSurgery.text = p?.surgeryType ?? "";
        fDate.text = p?.surgeryDate ?? "";
        fTherapist.text = p != null ? p.therapist : PlayerPrefs.GetString(TherapistKey, "");
        fMax.text = p != null ? p.comfortableMaxAngle.ToString("0", CultureInfo.InvariantCulture) : "90";
        fNotes.text = p?.notes ?? "";
        fLimit.text = (p != null ? p.SafetyLimit : PatientRecord.DefaultSafetyLimit).ToString("0", CultureInfo.InvariantCulture);
        fGoal.text = (p != null ? p.extensionGoal : 0f).ToString("0", CultureInfo.InvariantCulture);
        deleteArmed = false;
        deleteButton.gameObject.SetActive(!isNew);
        SetLeg(p != null ? p.operatedLeg : "Right");
        Show(Page.Form);
        fName.Select();
        fName.ActivateInputField();
    }

    InputField fLimit, fGoal;
    Image deleteButton;
    bool deleteArmed;

    // Two clicks: the first only asks.
    void DeletePatient()
    {
        if (!deleteArmed)
        {
            deleteArmed = true;
            formNote.text = $"Click DELETE again to remove {selected.name} and all their history.";
            return;
        }
        PatientStore.Delete(selected);
        if (SessionConfig.PatientId == selected.id) SessionConfig.PatientId = null;
        SelectPatient(null);
        Show(Page.Patients);
    }

    void SetLeg(string leg)
    {
        legChoice = leg;
        Paint(fLeft, leg == "Left");
        Paint(fRight, leg == "Right");
    }

    void SubmitForm()
    {
        float max = ParseF(fMax.text), height = ParseF(fHeight.text), weight = ParseF(fWeight.text);
        int.TryParse(fAge.text, out int age);
        float limit = ParseF(fLimit.text), goal = ParseF(fGoal.text);
        string error = string.IsNullOrWhiteSpace(fName.text) ? "Enter the patient's name."
                     : limit < 40f || limit > 110f ? "Safety limit must be between 40° and 110°."
                     : goal < 0f || goal > 40f ? "Straightening goal must be between 0° and 40°."
                     : max < 20f || max > 140f ? "Comfortable max bend must be between 20° and 140°."
                     : age < 0 || age > 120 ? "Check the age."
                     : (height != 0f && (height < 50f || height > 250f)) ? "Height should be in centimetres (50–250)."
                     : (weight != 0f && (weight < 15f || weight > 300f)) ? "Weight should be in kilograms (15–300)."
                     : null;
        if (error != null)
        {
            formNote.text = error;
            UISound.Error();
            return;
        }
        var p = formIsNew ? PatientStore.Create(fName.text.Trim()) : selected;
        p.name = fName.text.Trim();
        p.age = age;
        p.phone = fPhone.text.Trim();
        p.heightCm = height;
        p.weightKg = weight;
        p.surgeryType = fSurgery.text.Trim();
        p.surgeryDate = fDate.text.Trim();
        p.therapist = fTherapist.text.Trim();
        p.comfortableMaxAngle = max;
        p.safetyLimit = limit;
        p.extensionGoal = goal;
        p.notes = fNotes.text.Trim();
        p.operatedLeg = legChoice;
        PatientStore.Save();
        UISound.Confirm();
        SelectPatient(p);
        Show(Page.PatientFile);
    }

    // ================= Start session =================

    class Card
    {
        public TrainingGame game;
        public Image[] dots = new Image[Stages.Count];
        public Text[] labels = new Text[Stages.Count];
        public Outline[] rings = new Outline[Stages.Count];
        public Text stageName, stageInfo, stats, note;
        public int chosen = 1;
    }
    Card beachCard, footballCard;
    Image bendBtn, straightenBtn, alternateBtn;
    WaveGameManager.Movement movement = WaveGameManager.Movement.Bend;
    Text startTitle;

    void BuildStart(RectTransform page)
    {
        var title = Box("StartTitle", page, new Vector2(0, CH / 2f - 40f), new Vector2(CW, 80f), HeaderCol, 0.8f).transform;
        startTitle = Lbl(title, "", Vector2.zero, new Vector2(CW - 60f, 60f), 34, Color.white, true);
        beachCard = GameCard(page, TrainingGame.Beach, new Vector2(-CW / 4f - 5f, -45f));
        footballCard = GameCard(page, TrainingGame.Football, new Vector2(CW / 4f + 5f, -45f));
    }

    Card GameCard(RectTransform page, TrainingGame game, Vector2 pos)
    {
        var c = new Card { game = game };
        bool beach = game == TrainingGame.Beach;
        var pal = UIStyle.For(beach ? UIStyle.Theme.Sea : UIStyle.Theme.Football);
        var size = new Vector2(CW / 2f - 24f, 760f);
        Box("Edge", page, pos, size + new Vector2(12f, 12f), pal.edge, 1f).raycastTarget = false;
        var card = Box(game + "Card", page, pos, size, pal.panel, 1f).transform;
        var head = Box("Header", card, new Vector2(0, size.y / 2f - 66f), new Vector2(size.x - 28f, 108f), pal.header, 0.8f).transform;
        Lbl(head, pal.title, new Vector2(0, 16f), new Vector2(size.x - 60f, 56f), 44, Color.white, true);
        Lbl(head, beach ? "Seated  ·  bend and straighten the knee to escape the waves" : "Standing  ·  wind up and kick into the lit target",
             new Vector2(0, -28f), new Vector2(size.x - 60f, 30f), 21, new Color(1f, 1f, 1f, 0.85f));

        var how = Box("How", card, new Vector2(0, 175f), new Vector2(size.x - 50f, 110f), Section, 0.6f).transform;
        Lbl(how, "HOW IT WORKS", new Vector2(-size.x / 2f + 120f, 34f), new Vector2(200f, 28f), 18, Gold, true, TextAnchor.MiddleLeft);
        Lbl(how, beach
                ? "A wave runs up the beach. Bend (or lift) the knee to the target and hold it until the water drains back. Trains knee flexion and extension while seated."
                : "A target lights up in the goal. Pull the heel back to the target, hold it, then straighten the leg fully to kick. Trains flexion, full extension and standing balance.",
             new Vector2(0, -12f), new Vector2(size.x - 90f, 70f), 19, Color.white, false, TextAnchor.UpperLeft);

        Lbl(card, "STAGE", new Vector2(0, 92f), new Vector2(300f, 28f), 18, Gold, true);
        Box("Path", card, new Vector2(0, 35f), new Vector2(520f, 8f), new Color(1f, 1f, 1f, 0.15f), 0.2f).raycastTarget = false;
        for (int i = 0; i < Stages.Count; i++)
        {
            int stage = i + 1;
            var dot = Box("Stage" + stage, card, new Vector2(-260f + i * 130f, 35f), new Vector2(84f, 84f), Locked, 2.5f);
            var b = dot.gameObject.AddComponent<UnityEngine.UI.Button>();
            b.targetGraphic = dot;
            Tint(b);
            b.onClick.AddListener(() => ChooseStage(c, stage));
            UISound.Attach(b, true);
            var ring = dot.gameObject.AddComponent<Outline>();
            ring.effectColor = Color.white;
            ring.effectDistance = new Vector2(5f, -5f);
            c.rings[i] = ring;
            c.dots[i] = dot;
            c.labels[i] = Lbl(dot.transform, stage.ToString(), Vector2.zero, new Vector2(84f, 84f), 38, Color.white, true);
        }
        c.stageName = Lbl(card, "", new Vector2(0, -38f), new Vector2(size.x - 60f, 46f), 34, Color.white, true);
        c.stageInfo = Lbl(card, "", new Vector2(0, -76f), new Vector2(size.x - 60f, 30f), 21, pal.soft);
        c.note = Lbl(card, "", new Vector2(0, -108f), new Vector2(size.x - 60f, 28f), 19, Gold);

        if (beach)
        {
            Lbl(card, "MOVEMENT", new Vector2(0, -145f), new Vector2(300f, 26f), 18, Gold, true);
            bendBtn = Toggle(card, "Bend", new Vector2(-210f, -188f), 200f, () => SetMovement(WaveGameManager.Movement.Bend));
            straightenBtn = Toggle(card, "Straighten", new Vector2(0f, -188f), 200f, () => SetMovement(WaveGameManager.Movement.Straighten));
            alternateBtn = Toggle(card, "Alternate", new Vector2(210f, -188f), 200f, () => SetMovement(WaveGameManager.Movement.Alternate));
            SetMovement(WaveGameManager.Movement.Bend);
        }
        else
            Lbl(card, "Wind-up targets are a share of the patient's comfortable max bend.\nKeyboard test: S = bend the knee, W = kick.",
                 new Vector2(0, -170f), new Vector2(size.x - 80f, 60f), 20, Muted);

        c.stats = Lbl(card, "", new Vector2(0, -240f), new Vector2(size.x - 60f, 30f), 20, Muted);
        Button(card, beach ? "START  BEACH WAVES" : "START  PENALTIES", new Vector2(0, -305f), new Vector2(520f, 80f), Green, Color.white, () => Launch(c), 34);
        if (beach)
            Lbl(card, "Opens the therapist setup filled in from this stage. Every value can still be changed there.",
                 new Vector2(0, -360f), new Vector2(size.x - 60f, 24f), 17, Muted);
        return c;
    }

    void RefreshStart()
    {
        startTitle.text = $"START SESSION  ·  {selected.name}  ·  {selected.operatedLeg} knee  ·  comfortable max {selected.comfortableMaxAngle:0}°";
        RefreshCard(beachCard);
        RefreshCard(footballCard);
    }

    void RefreshCard(Card c)
    {
        var prog = selected.Progress(c.game);
        int unlocked = prog.unlockedStage;
        for (int i = 0; i < Stages.Count; i++)
        {
            int stage = i + 1;
            bool passed = stage < unlocked, open = stage <= unlocked;
            c.dots[i].color = passed ? Green : open ? Gold : Locked;
            c.labels[i].color = open ? (passed ? Color.white : new Color(0.15f, 0.1f, 0f)) : new Color(1f, 1f, 1f, 0.3f);
            c.rings[i].enabled = stage == c.chosen;
        }
        c.stageName.text = Stages.Name(c.chosen);
        c.stageInfo.text = Stages.Describe(c.game, c.chosen);
        c.stats.text = $"Sessions {prog.sessions}   ·   best score {prog.bestScore}   ·   best success {prog.bestPercent}%";
        c.note.text = unlocked < Stages.Count
            ? $"Pass Stage {unlocked} with {Stages.PassFraction * 100f:0}% to unlock Stage {unlocked + 1}"
            : "All stages unlocked";
    }

    void ChooseStage(Card c, int stage)
    {
        if (stage > selected.Progress(c.game).unlockedStage)
        {
            UISound.Error();
            c.note.text = $"Stage {stage} is locked: pass Stage {stage - 1} with {Stages.PassFraction * 100f:0}% first";
            return;
        }
        c.chosen = stage;
        RefreshCard(c);
    }

    void SetMovement(WaveGameManager.Movement m)
    {
        movement = m;
        Paint(bendBtn, m == WaveGameManager.Movement.Bend);
        Paint(straightenBtn, m == WaveGameManager.Movement.Straighten);
        Paint(alternateBtn, m == WaveGameManager.Movement.Alternate);
    }

    void Launch(Card c)
    {
        UISound.Confirm();
        SessionConfig.Launch(selected, c.game, c.chosen, movement);
    }

    // ================= Progress =================

    Text[] progressKpis;
    RectTransform angleChart, scoreChart, legChart;
    Text legNote;

    void BuildProgress(RectTransform page)
    {
        progressKpis = KpiRow(page, CH / 2f - 60f, new[] { "First session best bend", "Best bend so far", "Improvement", "Success: first → latest" });
        var a = SectionBox(page, "KNEE BEND AND STRAIGHTENING OVER TIME  ·  straightest: closer to 0° is better", new Vector2(0, 100f), new Vector2(CW, 380f));
        angleChart = UIStyle.Rect("Chart", a, new Vector2(0, -15f), new Vector2(CW - 60f, 320f));
        var s = SectionBox(page, "SCORE OVER TIME", new Vector2(-CW / 4f - 5f, -268f), new Vector2(CW / 2f - 10f, 345f));
        scoreChart = UIStyle.Rect("Chart", s, new Vector2(0, -15f), new Vector2(CW / 2f - 60f, 285f));
        var l = SectionBox(page, "LEFT VS RIGHT LEG  ·  best bend", new Vector2(CW / 4f + 5f, -268f), new Vector2(CW / 2f - 10f, 345f));
        legChart = UIStyle.Rect("Chart", l, new Vector2(0, 0f), new Vector2(CW / 2f - 60f, 250f));
        legNote = Lbl(l, "", new Vector2(0, -152f), new Vector2(CW / 2f - 60f, 26f), 17, Muted);
    }

    void RefreshProgress()
    {
        var h = selected.history;
        var labels = new List<string>();
        var beachA = new List<float>(); var footA = new List<float>();
        var beachS = new List<float>(); var footS = new List<float>();
        var left = new List<float>(); var right = new List<float>();
        foreach (var e in h)
        {
            labels.Add(ShortDate(e.date));
            bool b = e.game == "Beach";
            beachA.Add(b ? e.bestAngle : -1f); footA.Add(b ? -1f : e.bestAngle);
            beachS.Add(b ? e.score : -1f); footS.Add(b ? -1f : e.score);
            float l = e.bestLeft, r = e.bestRight;
            if (l <= 0f && r <= 0f) { string leg = string.IsNullOrEmpty(e.leg) ? selected.operatedLeg : e.leg; if (leg == "Left") l = e.bestAngle; else if (leg == "Right") r = e.bestAngle; } // older records
            left.Add(l > 0f ? l : -1f); right.Add(r > 0f ? r : -1f);
        }
        var straight = new List<float>();
        foreach (var e in h) straight.Add(e.hasExtension ? e.bestExtension : -1f);
        LineChart(angleChart, new[] { ("Beach bend", SeaLine, beachA), ("Penalty bend", PitchLine, footA), ("Straightest", Gold, straight) }, labels, "°", 0f, -1f, "No sessions yet.");
        LineChart(scoreChart, new[] { ("Beach waves", SeaLine, beachS), ("Penalty practice", PitchLine, footS) }, labels, "", 0f, -1f, "No sessions yet.");
        LineChart(legChart, new[] { ("Left leg", Gold, left), ("Right leg", new Color(1f, 0.45f, 0.45f), right) }, labels, "°", 0f, -1f, "No sessions yet.");
        legNote.text = selected.IsLeftLeg ? "Operated: left knee. Train 'Both' in Beach Waves to compare with the healthy leg."
                                          : "Operated: right knee. Train 'Both' in Beach Waves to compare with the healthy leg.";

        if (h.Count == 0)
        {
            foreach (var k in progressKpis) k.text = "-";
            return;
        }
        float first = h[0].bestAngle, best = 0f;
        foreach (var e in h) best = Mathf.Max(best, e.bestAngle);
        progressKpis[0].text = $"{first:0}°";
        progressKpis[1].text = $"{best:0}°";
        progressKpis[2].text = best > first + 0.5f ? $"<color=#5BE584>+{best - first:0}°</color>" : "0°";
        progressKpis[3].text = $"{h[0].percent}% → {h[h.Count - 1].percent}%";
    }

    // ================= Session log =================

    Text logTitle;
    RectTransform logTable;

    void BuildLog(RectTransform page)
    {
        var title = Box("LogTitle", page, new Vector2(0, CH / 2f - 40f), new Vector2(CW, 80f), HeaderCol, 0.8f).transform;
        logTitle = Lbl(title, "", Vector2.zero, new Vector2(CW - 60f, 60f), 34, Color.white, true);
        Button(title, "EXPORT CSV", new Vector2(CW / 2f - 130f, 0f), new Vector2(220f, 56f), Green, Color.white, ExportSessions, 22);
        var sec = SectionBox(page, "EVERY SESSION  ·  click a row for pain and notes  ·  newest first", new Vector2(0, -50f), new Vector2(CW, 780f));
        logTable = UIStyle.Rect("Table", sec, new Vector2(0, -20f), new Vector2(CW - 40f, 715f));
    }

    void RefreshLog()
    {
        var h = selected.history;
        float minutes = selected.TotalMinutes;
        logTitle.text = $"SESSION LOG  ·  {selected.name}  ·  {h.Count} session{(h.Count == 1 ? "" : "s")}  ·  {minutes:0} min in total";
        var rows = new List<string[]>();
        var clicks = new List<UnityEngine.Events.UnityAction>();
        CloseSession();
        for (int i = h.Count - 1; i >= 0; i--)
        {
            var e = h[i];
            rows.Add(new[]
            {
                DateTimeText(e.date), GameName(e.game), "S" + e.stage, e.leg ?? "-", Duration(e.durationSeconds),
                $"{e.bestAngle:0}°", e.avgAngle > 0 ? $"{e.avgAngle:0}°" : "-", SuccessText(e), e.score.ToString(),
                e.safetyAlerts > 0 ? $"<color=#FF7A66>{e.safetyAlerts}</color>" : "0",
                e.hasExtension ? $"{e.bestExtension:0}°" : "-", e.hasPain ? $"{e.pain}/10" : "-",
            });
            var entry = e;
            clicks.Add(() => OpenSession(entry));
        }
        Table(logTable, new[] { ("Date", 200f), ("Game", 200f), ("Stage", 90f), ("Leg", 100f), ("Time", 110f), ("Best bend", 130f),
                                ("Avg bend", 120f), ("Success", 250f), ("Score", 110f), ("Safety alerts", 130f),
                                ("Straight", 130f), ("Pain", 90f) },
              rows, 44f, clicks, "No sessions yet.");
    }

    /// <summary>The selected patient's sessions as a spreadsheet file in Documents/KneeRehabReports.</summary>
    void ExportSessions()
    {
        if (selected == null) return;
        var c = CultureInfo.InvariantCulture;
        string Q(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("patient_id,date_utc,game,stage,leg,duration_s,done,planned,stopped_early,eased,success_percent,score," +
                      "best_bend_deg,avg_bend_deg,straightest_deg,safety_alerts,balance_pauses,pain_0_10,settings,note");
        foreach (var e in selected.history)
            sb.AppendLine(string.Join(",", selected.id, e.date, e.game, e.stage.ToString(c), e.leg, e.durationSeconds.ToString("0", c),
                e.done.ToString(c), e.planned.ToString(c), e.stoppedEarly ? "1" : "0", e.easier ? "1" : "0", e.percent.ToString(c),
                e.score.ToString(c), e.bestAngle.ToString("0.0", c), e.avgAngle.ToString("0.0", c),
                e.hasExtension ? e.bestExtension.ToString("0.0", c) : "", e.safetyAlerts.ToString(c), e.balancePauses.ToString(c),
                e.hasPain ? e.pain.ToString(c) : "", Q(e.settings), Q(e.note)));
        try
        {
            string folder = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments), "KneeRehabReports");
            System.IO.Directory.CreateDirectory(folder);
            string file = System.IO.Path.Combine(folder, $"sessions_{selected.id}_{System.DateTime.Now:yyyyMMdd_HHmmss}.csv");
            System.IO.File.WriteAllText(file, sb.ToString(), new System.Text.UTF8Encoding(true));
            logTitle.text = "Saved: " + file;
            Debug.Log("[Export] " + file);
            UISound.Confirm();
        }
        catch (System.Exception ex)
        {
            logTitle.text = "Could not save the file: " + ex.Message;
            UISound.Error();
        }
    }

    // ---------------- One session: pain, note, delete ----------------

    RectTransform sessionEditor;
    InputField noteField;
    SessionEntry editing;
    readonly Image[] painButtons = new Image[11];
    Text editorInfo;
    int painChoice = -1;
    bool removeArmed;

    void OpenSession(SessionEntry e)
    {
        if (!sessionEditor) BuildSessionEditor();
        editing = e;
        removeArmed = false;
        painChoice = e.hasPain ? e.pain : -1;
        PaintPain();
        noteField.text = e.note ?? "";
        editorInfo.text = $"{DateTimeText(e.date)}  ·  {GameName(e.game)}  ·  stage {e.stage}\n{e.settings}";
        sessionEditor.gameObject.SetActive(true);
        sessionEditor.SetAsLastSibling();
    }

    void CloseSession()
    {
        if (sessionEditor) sessionEditor.gameObject.SetActive(false);
    }

    void PaintPain()
    {
        for (int i = 0; i < painButtons.Length; i++) Paint(painButtons[i], i == painChoice);
    }

    void BuildSessionEditor()
    {
        sessionEditor = SectionBox(pages[Page.Log], "SESSION  ·  pain and note", new Vector2(0, -40f), new Vector2(1100f, 480f));
        sessionEditor.GetComponent<Image>().color = CardCol;
        sessionEditor.GetComponent<Image>().raycastTarget = true; // block the table behind
        editorInfo = Lbl(sessionEditor, "", new Vector2(0, 135f), new Vector2(1040f, 90f), 22, Color.white);
        Lbl(sessionEditor, "Pain (0 = none, 10 = worst)", new Vector2(0, 65f), new Vector2(600f, 30f), 22, Gold);
        for (int i = 0; i <= 10; i++)
        {
            int value = i;
            painButtons[i] = Toggle(sessionEditor, i.ToString(), new Vector2(-440f + i * 88f, 15f), 78f, () => { painChoice = value; PaintPain(); });
        }
        noteField = Field(sessionEditor, new Vector2(0, -65f), 1000f, "Therapist's note", InputField.ContentType.Standard);
        Button(sessionEditor, "SAVE", new Vector2(300f, -170f), new Vector2(240f, 66f), Green, Color.white, () =>
        {
            editing.hasPain = painChoice >= 0;
            editing.pain = Mathf.Max(0, painChoice);
            editing.note = noteField.text.Trim();
            PatientStore.Save();
            UISound.Confirm();
            RefreshLog();
        }, 26);
        Button(sessionEditor, "CLOSE", new Vector2(0f, -170f), new Vector2(240f, 66f), Off, Color.white, CloseSession, 26);
        Button(sessionEditor, "DELETE (click twice)", new Vector2(-320f, -170f), new Vector2(280f, 66f), Off, Color.white, () =>
        {
            if (!removeArmed) { removeArmed = true; editorInfo.text = "Click DELETE again to remove this session."; return; }
            selected.RemoveSession(editing);
            PatientStore.Save();
            RefreshLog();
        }, 22);
    }

    // ================= Settings =================

    InputField therapistField;
    Text settingsInfo;

    void BuildSettings(RectTransform page)
    {
        var t = SectionBox(page, "THERAPIST", new Vector2(0, CH / 2f - 90f), new Vector2(CW, 180f));
        therapistField = FormRow(t, "Your name (shown in the header)", -10f, 500f, InputField.ContentType.Standard, "Therapist name", 420f);
        Button(t, "SAVE", new Vector2(560f, -10f), new Vector2(200f, 62f), Green, Color.white, () =>
        {
            PlayerPrefs.SetString(TherapistKey, therapistField.text.Trim());
            PlayerPrefs.Save();
            UISound.Confirm();
            RefreshHeader();
        }, 26);

        var rules = SectionBox(page, "TRAINING RULES", new Vector2(0, -55f), new Vector2(CW, 390f));
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Each game has {Stages.Count} stages. A new patient starts at Stage 1.");
        sb.AppendLine($"A stage is passed with at least {Stages.PassFraction * 100f:0}% (waves cleared or goals scored); the next stage then opens. Passed stages can always be replayed.");
        sb.AppendLine("Knee-bend targets are a share of each patient's comfortable max bend and never pass the safety limit (110°).");
        sb.AppendLine();
        for (int s = 1; s <= Stages.Count; s++)
            sb.AppendLine($"<color=#FFD94A>{Stages.Name(s)}</color>    Beach: {Stages.Describe(TrainingGame.Beach, s)}     Football: {Stages.Describe(TrainingGame.Football, s)}");
        var r = Lbl(rules, sb.ToString(), new Vector2(0, -20f), new Vector2(CW - 60f, 360f), 20, Color.white, false, TextAnchor.UpperLeft);
        r.supportRichText = true;

        var data = SectionBox(page, "DATA", new Vector2(0, -CH / 2f + 90f), new Vector2(CW, 180f));
        settingsInfo = Lbl(data, "", new Vector2(0, -15f), new Vector2(CW - 60f, 110f), 21, Color.white, false, TextAnchor.UpperLeft);
    }

    void RefreshSettings()
    {
        therapistField.text = PlayerPrefs.GetString(TherapistKey, "");
        int sessions = 0;
        foreach (var p in PatientStore.All) sessions += p.history.Count;
        settingsInfo.text = $"Patient records are saved on this computer in:\n{System.IO.Path.Combine(Application.persistentDataPath, "patients.json")}\n" +
                            $"{PatientStore.All.Count} patients  ·  {sessions} session{(sessions == 1 ? "" : "s")}.  Each game also writes a detailed CSV log per session.";
    }

    // ================= Charts and tables =================

    /// <summary>
    /// Line chart: one line per series, points at each session (values below 0 are "not in this series" and skipped;
    /// the line joins the remaining points). Grid with axis labels, legend at the top right.
    /// </summary>
    void LineChart(RectTransform area, (string name, Color color, List<float> values)[] series, List<string> xLabels,
                   string unit, float yMin, float yMax, string emptyText)
    {
        Clear(area);
        float w = area.sizeDelta.x, h = area.sizeDelta.y;
        float left = -w / 2f + 60f, right = w / 2f - 20f, bottom = -h / 2f + 34f, top = h / 2f - 30f;

        bool any = false;
        float max = 0f;
        foreach (var s in series) foreach (var v in s.values) if (v >= 0f) { any = true; max = Mathf.Max(max, v); }
        if (yMax <= yMin) yMax = Nice(Mathf.Max(max * 1.1f, unit == "°" ? 40f : 100f));

        for (int i = 0; i <= 4; i++)
        {
            float v = Mathf.Lerp(yMin, yMax, i / 4f), y = Mathf.Lerp(bottom, top, i / 4f);
            Box("Grid", area, new Vector2((left + right) / 2f, y), new Vector2(right - left, 2f), new Color(1f, 1f, 1f, 0.1f), 0f).raycastTarget = false;
            Lbl(area, $"{v:0}{unit}", new Vector2(left - 34f, y), new Vector2(64f, 22f), 16, Muted, false, TextAnchor.MiddleRight);
        }

        // Legend
        float lx = right;
        for (int s = series.Length - 1; s >= 0; s--)
        {
            Lbl(area, series[s].name, new Vector2(lx - 90f, top + 18f), new Vector2(180f, 24f), 17, Color.white, false, TextAnchor.MiddleLeft);
            Box("Key", area, new Vector2(lx - 196f, top + 18f), new Vector2(24f, 6f), series[s].color, 0.2f).raycastTarget = false;
            lx -= 230f;
        }

        if (!any)
        {
            Lbl(area, emptyText, new Vector2((left + right) / 2f, Mathf.Lerp(bottom, top, 0.62f)), new Vector2(right - left, 40f), 24, Muted);
            return;
        }

        int n = xLabels.Count;
        float X(int i) => n <= 1 ? (left + right) / 2f : Mathf.Lerp(left + 20f, right - 20f, (float)i / (n - 1));
        float Y(float v) => Mathf.Lerp(bottom, top, Mathf.InverseLerp(yMin, yMax, v));

        int step = Mathf.Max(1, Mathf.CeilToInt(n / 10f));
        for (int i = 0; i < n; i += step)
            Lbl(area, xLabels[i], new Vector2(X(i), bottom - 18f), new Vector2(90f, 22f), 15, Muted);

        foreach (var s in series)
        {
            Vector2? prev = null;
            for (int i = 0; i < s.values.Count; i++)
            {
                if (s.values[i] < 0f) continue;
                var pt = new Vector2(X(i), Y(s.values[i]));
                if (prev.HasValue) Segment(area, prev.Value, pt, s.color);
                prev = pt;
            }
            for (int i = 0; i < s.values.Count; i++)
            {
                if (s.values[i] < 0f) continue;
                var pt = new Vector2(X(i), Y(s.values[i]));
                Box("Dot", area, pt, new Vector2(14f, 14f), s.color, 2f).raycastTarget = false;
                if (n <= 14) Lbl(area, $"{s.values[i]:0}", pt + new Vector2(0, 17f), new Vector2(60f, 20f), 14, Color.white);
            }
        }
    }

    static float Nice(float v)
    {
        // Maximums that split into 4 round steps.
        float[] steps = { 20, 40, 80, 120, 160, 200, 400, 800, 1200, 1600, 2000, 4000, 8000, 12000, 20000 };
        foreach (var s in steps) if (v <= s) return s;
        return Mathf.Ceil(v / 1000f) * 1000f;
    }

    static void Segment(RectTransform area, Vector2 a, Vector2 b, Color color)
    {
        var d = b - a;
        var img = UIStyle.Box("Line", area, (a + b) / 2f, new Vector2(d.magnitude, 4f), color, 0f);
        img.rectTransform.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
    }

    /// <summary>Header row plus striped rows (scrolls when there are many). Rows are clickable when actions are given.</summary>
    void Table(RectTransform area, (string title, float width)[] cols, List<string[]> rows, float rowH,
               List<UnityEngine.Events.UnityAction> onClick, string emptyText)
    {
        Clear(area);
        float w = area.sizeDelta.x, h = area.sizeDelta.y;
        float total = 0f;
        foreach (var c in cols) total += c.width;
        float scale = (w - 20f) / total;

        var head = Box("Head", area, new Vector2(0, h / 2f - 22f), new Vector2(w, 44f), FieldCol, 0.5f).transform;
        float x = -w / 2f + 18f;
        foreach (var c in cols)
        {
            Lbl(head, c.title.ToUpperInvariant(), new Vector2(x + c.width * scale / 2f, 0), new Vector2(c.width * scale - 8f, 40f), 17, Gold, true, TextAnchor.MiddleLeft);
            x += c.width * scale;
        }

        var view = Box("Rows", area, new Vector2(0, -24f), new Vector2(w, h - 48f), new Color(0, 0, 0, 0.001f), 0f).rectTransform;
        view.gameObject.AddComponent<RectMask2D>();
        float contentH = Mathf.Max(rows.Count * (rowH + 4f) + 4f, 1f);
        var content = UIStyle.Rect("Content", view, Vector2.zero, new Vector2(w, contentH));
        content.anchorMin = content.anchorMax = content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        // Scroll only when the rows don't fit (a short list would otherwise be pushed down).
        if (contentH > h - 48f)
        {
            var scroll = view.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;
        }

        if (rows.Count == 0)
        {
            Lbl(view, emptyText, new Vector2(0, 0), new Vector2(w - 40f, 40f), 22, Muted);
            return;
        }
        for (int r = 0; r < rows.Count; r++)
        {
            var row = Box("Row", content, new Vector2(0, -r * (rowH + 4f) - rowH / 2f - 4f), new Vector2(w, rowH), r % 2 == 0 ? Row : Section, 0.4f);
            if (onClick != null)
            {
                var b = row.gameObject.AddComponent<UnityEngine.UI.Button>();
                b.targetGraphic = row;
                Tint(b);
                b.onClick.AddListener(onClick[r]);
                UISound.Attach(b);
            }
            x = -w / 2f + 18f;
            for (int c = 0; c < cols.Length && c < rows[r].Length; c++)
            {
                var t = Lbl(row.transform, rows[r][c], new Vector2(x + cols[c].width * scale / 2f, 0), new Vector2(cols[c].width * scale - 8f, rowH), 20,
                             Color.white, c == 0 && onClick != null, TextAnchor.MiddleLeft);
                t.supportRichText = true;
                t.horizontalOverflow = HorizontalWrapMode.Wrap;
                t.verticalOverflow = VerticalWrapMode.Truncate;
                x += cols[c].width * scale;
            }
        }
    }

    Text[] KpiRow(RectTransform page, float y, string[] labels)
    {
        var values = new Text[labels.Length];
        float w = (CW - 20f * (labels.Length - 1)) / labels.Length;
        for (int i = 0; i < labels.Length; i++)
        {
            float x = -CW / 2f + w / 2f + i * (w + 20f);
            Box("KpiEdge", page, new Vector2(x, y), new Vector2(w + 6f, 126f), new Color(Edge.r, Edge.g, Edge.b, 0.5f), 0.8f).raycastTarget = false;
            var k = Box("Kpi", page, new Vector2(x, y), new Vector2(w, 120f), Section, 0.8f).transform;
            Lbl(k, labels[i].ToUpperInvariant(), new Vector2(0, 32f), new Vector2(w - 30f, 28f), 17, Gold, true, TextAnchor.MiddleLeft);
            values[i] = Lbl(k, "-", new Vector2(0, -14f), new Vector2(w - 30f, 56f), 40, Color.white, true, TextAnchor.MiddleLeft);
            values[i].supportRichText = true;
        }
        return values;
    }

    // ================= Small helpers =================

    static void Clear(RectTransform t)
    {
        for (int i = t.childCount - 1; i >= 0; i--) Destroy(t.GetChild(i).gameObject);
    }

    static float ParseF(string s)
    {
        float.TryParse((s ?? "").Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float v);
        return v;
    }

    static string Empty(string s, string fallback = "-") => string.IsNullOrWhiteSpace(s) ? fallback : s;
    static string Short(string s, int max) => s.Length <= max ? s : s.Substring(0, max - 1) + ".";
    static string GameName(string g) => g == "Beach" ? "Beach waves" : g == "Football" ? "Penalty practice" : g;

    static string SuccessText(SessionEntry e) =>
        e.stoppedEarly ? $"<color=#FF7A66>stopped {e.done}/{e.planned}</color>" :
        e.easier ? $"<color=#FFD94A>{e.percent}%</color>  eased" :
        e.passed ? $"<color=#5BE584>{e.percent}%  passed</color>" : $"<color=#FFD94A>{e.percent}%</color>  practice";

    static string Duration(float seconds)
    {
        if (seconds <= 0f) return "-";
        int m = Mathf.FloorToInt(seconds / 60f), s = Mathf.RoundToInt(seconds % 60f);
        return m > 0 ? $"{m} min {s:00} s" : $"{s} s";
    }

    static string ShortDate(string iso) =>
        System.DateTime.TryParse(iso, null, DateTimeStyles.RoundtripKind, out var d) ? d.ToLocalTime().ToString("d MMM", CultureInfo.InvariantCulture) : "-";

    static string DateTimeText(string iso) =>
        System.DateTime.TryParse(iso, null, DateTimeStyles.RoundtripKind, out var d) ? d.ToLocalTime().ToString("d MMM  HH:mm", CultureInfo.InvariantCulture) : "-";

    // ---------------- Widgets (therapist-panel style) ----------------

    static Image Box(string name, Transform parent, Vector2 pos, Vector2 size, Color color, float corner)
    {
        var img = UIStyle.Box(name, parent, pos, size, color, corner);
        img.raycastTarget = true;
        return img;
    }

    /// <summary>Navy group box with a small yellow title in its top-left corner, like the therapist panel sections.</summary>
    static RectTransform SectionBox(Transform parent, string title, Vector2 pos, Vector2 size)
    {
        var box = Box("Section_" + title, parent, pos, size, Section, 0.8f).rectTransform;
        box.GetComponent<Image>().raycastTarget = false;
        Lbl(box, title, new Vector2(-size.x / 2f + 20f + (size.x - 40f) / 2f, size.y / 2f - 24f), new Vector2(size.x - 40f, 30f), 21, Gold, true, TextAnchor.MiddleLeft);
        return box;
    }

    static Text Lbl(Transform parent, string text, Vector2 pos, Vector2 size, int fontSize, Color color, bool bold = false, TextAnchor anchor = TextAnchor.MiddleCenter)
    {
        return UIStyle.Label("Text", parent, pos, size, fontSize, anchor, color, bold ? FontStyle.Bold : FontStyle.Normal, text);
    }

    static InputField Field(Transform parent, Vector2 pos, float width, string placeholder, InputField.ContentType type)
    {
        var box = Box("Field", parent, pos, new Vector2(width, 54f), FieldCol, 0.6f);
        var outline = box.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(1f, 1f, 1f, 0.22f);
        outline.effectDistance = new Vector2(2f, -2f);
        var text = UIStyle.Label("Text", box.transform, Vector2.zero, new Vector2(width - 28f, 54f), 26, TextAnchor.MiddleLeft, Color.white, FontStyle.Bold);
        text.supportRichText = false;
        var ph = UIStyle.Label("Placeholder", box.transform, Vector2.zero, new Vector2(width - 28f, 54f), 22, TextAnchor.MiddleLeft,
                               new Color(1f, 1f, 1f, 0.3f), FontStyle.Italic, placeholder);
        var input = box.gameObject.AddComponent<InputField>();
        input.targetGraphic = box;
        input.textComponent = text;
        input.placeholder = ph;
        input.contentType = type;
        input.caretColor = Gold;
        input.customCaretColor = true;
        input.caretWidth = 3;
        input.selectionColor = new Color(0.25f, 0.65f, 1f, 0.5f);
        return input;
    }

    static Image Toggle(Transform parent, string label, Vector2 pos, float width, UnityEngine.Events.UnityAction onClick)
    {
        var img = Box("Toggle_" + label, parent, pos, new Vector2(width, 54f), Off, 1.1f);
        var b = img.gameObject.AddComponent<UnityEngine.UI.Button>();
        b.targetGraphic = img;
        Tint(b);
        b.onClick.AddListener(onClick);
        UISound.Attach(b, true);
        Lbl(img.transform, label, Vector2.zero, new Vector2(width, 54f), 25, Muted);
        return img;
    }

    static Image Button(Transform parent, string label, Vector2 pos, Vector2 size, Color color, Color textColor, UnityEngine.Events.UnityAction onClick, int fontSize = 28)
    {
        var img = Box("Button_" + label, parent, pos, size, color, 1.4f);
        var b = img.gameObject.AddComponent<UnityEngine.UI.Button>();
        b.targetGraphic = img;
        Tint(b);
        if (onClick != null) b.onClick.AddListener(onClick);
        UISound.Attach(b);
        Lbl(img.transform, label, Vector2.zero, size, fontSize, textColor, true);
        return img;
    }

    static void Paint(Image pill, bool on)
    {
        if (!pill) return;
        pill.color = on ? Blue : Off;
        var t = pill.GetComponentInChildren<Text>();
        t.fontStyle = on ? FontStyle.Bold : FontStyle.Normal;
        t.color = on ? Color.white : Muted;
    }

    static void Tint(UnityEngine.UI.Button b)
    {
        var c = b.colors;
        c.highlightedColor = new Color(1.2f, 1.2f, 1.2f, 1f);
        c.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
        c.selectedColor = Color.white;
        c.colorMultiplier = 1.3f;
        c.fadeDuration = 0.08f;
        b.colors = c;
    }

    /// <summary>Gentle breathing scale for the big START button.</summary>
    class Pulse : MonoBehaviour
    {
        void Update() => transform.localScale = Vector3.one * (1f + 0.03f * Mathf.Sin(Time.unscaledTime * 3.5f));
    }
}
