using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>What one leg achieved during a session.</summary>
public class StageResult
{
    public WaveGameManager.Leg leg;
    public int waves, cleared, score, safetyAlerts;
    public float bestAngle, highestClearedTarget, avgHeld;

    public float ClearedFraction => waves > 0 ? (float)cleared / waves : 0f;
}

/// <summary>
/// End-of-session summary shown on the therapist's screen: one card per trained leg
/// (right and left side by side when both were trained), an overall rating and a New session button.
/// Builds itself at runtime like the other panels.
/// </summary>
public class ResultsPanel : MonoBehaviour
{
    const float CardW = 1500f, CardH = 900f;

    public bool IsOpen { get; private set; }

    GameObject root;
    RectTransform card, body;
    Text subText, ratingText, noteText;

    static readonly Color Dim = new Color(0.04f, 0.12f, 0.22f, 0.94f);
    static readonly Color CardColor = new Color(0.08f, 0.2f, 0.34f, 1f);
    static readonly Color StageColor = new Color(1f, 1f, 1f, 0.08f);
    static readonly Color Muted = new Color(1f, 1f, 1f, 0.65f);

    bool inPatientView;

    public static ResultsPanel Create(bool patientView)
    {
        var go = new GameObject("ResultsPanel");
        var panel = go.AddComponent<ResultsPanel>();
        panel.inPatientView = patientView;
        panel.Build();
        return panel;
    }

    // What the panel is showing, so the Results / Leaderboard button can switch views.
    string patientName;
    List<StageResult> lastResults;
    List<LeaderboardEntry> board;
    LeaderboardEntry mine;
    int myRank;
    bool showingBoard;
    Text toggleLabel;

    public void Show(string patient, List<StageResult> results, float minTarget, float maxTarget,
                     List<LeaderboardEntry> leaderboard, LeaderboardEntry current, int rank)
    {
        patientName = patient;
        lastResults = results;
        board = leaderboard;
        mine = current;
        myRank = rank;
        subText.text = $"Patient: {patient}   ·   {System.DateTime.Now:d MMM yyyy, HH:mm}   ·   target range {minTarget:0}°–{maxTarget:0}°";
        ShowResultsView();
        IsOpen = true;
        DoctorPanel.PlaceInFront(root, 1.8f);
        root.SetActive(true);
    }

    void ToggleView()
    {
        if (showingBoard) ShowResultsView(); else ShowBoardView();
    }

    void ClearBody()
    {
        for (int i = body.childCount - 1; i >= 0; i--) Destroy(body.GetChild(i).gameObject);
    }

    void ShowBoardView()
    {
        showingBoard = true;
        toggleLabel.text = "VIEW RESULTS";
        ClearBody();
        SetRating("Leaderboard", WaveHUD.Accent);
        noteText.text = myRank > 0 && myRank <= board.Count
            ? $"{patientName} placed #{myRank} of {board.Count}"
            : $"{patientName} did not make the top {board.Count}";

        const int rows = 8;
        float w = 1100f;
        var head = DoctorPanel.Child("Head", body, StageColor);
        head.anchoredPosition = new Vector2(0, 240f);
        head.sizeDelta = new Vector2(w, 46f);
        BoardRow(head, "#", "PATIENT", "SCORE", "LEGS", "CLEARED", "DATE", Muted, 24, w);

        for (int i = 0; i < Mathf.Min(rows, board.Count); i++)
        {
            var e = board[i];
            bool isMine = e == mine;
            var row = DoctorPanel.Child("Row" + i, body, isMine ? new Color(1f, 0.85f, 0.25f, 0.25f) : (i % 2 == 0 ? new Color(1f, 1f, 1f, 0.04f) : Color.clear));
            row.anchoredPosition = new Vector2(0, 180f - i * 56f);
            row.sizeDelta = new Vector2(w, 52f);
            string date = System.DateTime.TryParse(e.date, null, System.Globalization.DateTimeStyles.RoundtripKind, out var d)
                ? d.ToLocalTime().ToString("d MMM yyyy") : "";
            BoardRow(row, (i + 1).ToString(), e.patient, e.score.ToString(), e.mode, $"{e.cleared}/{e.waves}", date,
                     isMine ? WaveHUD.Accent : Color.white, 30, w);
        }

        // The current patient's row, when they are below the visible part of the board.
        if (myRank > rows)
        {
            var row = DoctorPanel.Child("MyRow", body, new Color(1f, 0.85f, 0.25f, 0.25f));
            row.anchoredPosition = new Vector2(0, 180f - rows * 56f - 10f);
            row.sizeDelta = new Vector2(w, 52f);
            BoardRow(row, myRank.ToString(), mine.patient, mine.score.ToString(), mine.mode, $"{mine.cleared}/{mine.waves}", "",
                     WaveHUD.Accent, 30, w);
        }
        if (board.Count == 0) DoctorPanel.Label(body, "No scores yet", Vector2.zero, new Vector2(600f, 60f), 36, TextAnchor.MiddleCenter, Muted);
    }

    static void BoardRow(RectTransform row, string rank, string name, string score, string legs, string cleared, string date,
                         Color color, int size, float w)
    {
        float[] x = { -w * 0.45f, -w * 0.22f, w * 0.0f, w * 0.14f, w * 0.27f, w * 0.42f };
        string[] text = { rank, name, score, legs, cleared, date };
        float[] width = { 80f, 330f, 140f, 120f, 150f, 200f };
        for (int i = 0; i < text.Length; i++)
            DoctorPanel.Label(row, text[i], new Vector2(x[i], 0), new Vector2(width[i], 46f), size,
                              i == 1 ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter, color);
    }

    void ShowResultsView()
    {
        showingBoard = false;
        toggleLabel.text = "VIEW LEADERBOARD";
        var results = lastResults;
        ClearBody();

        float colW = results.Count > 1 ? (CardW - 150f) / 2f : 700f;
        for (int i = 0; i < results.Count; i++)
        {
            float x = results.Count > 1 ? (i == 0 ? -(colW / 2f + 25f) : colW / 2f + 25f) : 0f;
            BuildStage(results[i], new Vector2(x, 0), colW);
        }

        int waves = 0, cleared = 0, alerts = 0;
        foreach (var r in results) { waves += r.waves; cleared += r.cleared; alerts += r.safetyAlerts; }
        float fraction = waves > 0 ? (float)cleared / waves : 0f;
        if (fraction >= 0.8f) SetRating("Excellent work!", WaveHUD.Good);
        else if (fraction >= 0.5f) SetRating("Good progress", WaveHUD.Accent);
        else SetRating("Keep practising — every session helps", new Color(1f, 0.65f, 0.35f));

        noteText.text = results.Count == 2 ? Compare(results[0], results[1]) : "";
        if (alerts > 0)
            noteText.text += (noteText.text.Length > 0 ? "\n" : "") + $"Safety limit was passed {alerts} time(s) — review with the patient.";
    }

    void SetRating(string text, Color color)
    {
        ratingText.text = text;
        ratingText.color = color;
    }

    /// <summary>Plain-language comparison of the first and second leg trained.</summary>
    static string Compare(StageResult a, StageResult b)
    {
        float diff = a.bestAngle - b.bestAngle;
        if (Mathf.Abs(diff) < 2f) return $"Both legs reached a similar knee bend ({a.bestAngle:0}° / {b.bestAngle:0}°).";
        var better = diff > 0 ? a : b;
        var other = diff > 0 ? b : a;
        return $"The {better.leg.ToString().ToLower()} leg bent {Mathf.Abs(diff):0}° further than the {other.leg.ToString().ToLower()} leg.";
    }

    void BuildStage(StageResult r, Vector2 pos, float width)
    {
        var col = DoctorPanel.Child("Stage_" + r.leg, body, StageColor);
        col.anchoredPosition = pos;
        col.sizeDelta = new Vector2(width, 520f);

        DoctorPanel.Label(col, $"{r.leg.ToString().ToUpper()} LEG", new Vector2(0, 220f), new Vector2(width - 40f, 60f), 42,
                          TextAnchor.MiddleCenter, WaveHUD.Accent);

        // Waves cleared, with a bar.
        DoctorPanel.Label(col, $"{r.cleared} / {r.waves}", new Vector2(0, 150f), new Vector2(width - 40f, 80f), 64,
                          TextAnchor.MiddleCenter, Color.white);
        DoctorPanel.Label(col, "waves cleared", new Vector2(0, 100f), new Vector2(width - 40f, 36f), 24, TextAnchor.MiddleCenter, Muted);
        var barBack = DoctorPanel.Child("Bar", col, new Color(1f, 1f, 1f, 0.15f));
        barBack.anchoredPosition = new Vector2(0, 62f);
        barBack.sizeDelta = new Vector2(width - 80f, 22f);
        var fill = DoctorPanel.Child("Fill", barBack, r.ClearedFraction >= 0.8f ? WaveHUD.Good : r.ClearedFraction >= 0.5f ? WaveHUD.Accent : WaveHUD.Bad);
        fill.anchorMin = fill.anchorMax = fill.pivot = new Vector2(0, 0.5f);
        fill.anchoredPosition = Vector2.zero;
        fill.sizeDelta = new Vector2((width - 80f) * r.ClearedFraction, 22f);

        float y = 26f;
        Row(col, "Score", r.score.ToString(), Color.white, ref y, width);
        Row(col, "Best knee bend", $"{r.bestAngle:0}°", Color.white, ref y, width);
        Row(col, "Highest target reached", r.highestClearedTarget > 0f ? $"{r.highestClearedTarget:0}°" : "—", Color.white, ref y, width);
        Row(col, "Hold accuracy", $"{r.avgHeld * 100f:0}%", Color.white, ref y, width);
        Row(col, "Safety alerts", r.safetyAlerts.ToString(), r.safetyAlerts > 0 ? WaveHUD.Bad : WaveHUD.Good, ref y, width);
    }

    static void Row(RectTransform col, string label, string value, Color valueColor, ref float y, float width)
    {
        y -= 50f;
        DoctorPanel.Label(col, label, new Vector2(-width * 0.2f, y), new Vector2(width * 0.55f, 46f), 28, TextAnchor.MiddleLeft, Muted);
        DoctorPanel.Label(col, value, new Vector2(width * 0.3f, y), new Vector2(width * 0.35f, 46f), 32, TextAnchor.MiddleRight, valueColor);
    }

    // ---------------- UI construction ----------------

    void Build()
    {
        root = new GameObject("Root", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.transform.SetParent(transform, false);
        DoctorPanel.ConfigureCanvas(root, inPatientView, 110);

        var rt = (RectTransform)root.transform;
        var dim = DoctorPanel.Child("Dim", rt, Dim);
        dim.anchorMin = Vector2.zero; dim.anchorMax = Vector2.one; dim.offsetMin = dim.offsetMax = Vector2.zero;
        if (inPatientView) dim.gameObject.SetActive(false);

        card = DoctorPanel.Child("Card", rt, CardColor);
        card.anchorMin = card.anchorMax = card.pivot = new Vector2(0.5f, 0.5f);
        card.sizeDelta = new Vector2(CardW, CardH);

        DoctorPanel.Label(card, "Session results", new Vector2(0, 390f), new Vector2(CardW - 80f, 70f), 54, TextAnchor.MiddleCenter, Color.white);
        subText = DoctorPanel.Label(card, "", new Vector2(0, 335f), new Vector2(CardW - 80f, 40f), 26, TextAnchor.MiddleCenter, Muted);
        ratingText = DoctorPanel.Label(card, "", new Vector2(0, 270f), new Vector2(CardW - 80f, 60f), 40, TextAnchor.MiddleCenter, WaveHUD.Good);

        body = DoctorPanel.Child("Body", card, Color.clear);
        body.anchoredPosition = new Vector2(0, -30f);
        body.sizeDelta = new Vector2(CardW - 80f, 540f);
        body.GetComponent<Image>().raycastTarget = false;

        noteText = DoctorPanel.Label(card, "", new Vector2(0, -330f), new Vector2(CardW - 120f, 80f), 26, TextAnchor.MiddleCenter, Color.white);

        var toggle = DoctorPanel.Child("ToggleView", card, new Color(1f, 1f, 1f, 0.15f));
        toggle.anchoredPosition = new Vector2(-300f, -400f);
        toggle.sizeDelta = new Vector2(380f, 70f);
        var toggleBtn = toggle.gameObject.AddComponent<Button>();
        toggleBtn.targetGraphic = toggle.GetComponent<Image>();
        toggleBtn.onClick.AddListener(ToggleView);
        toggleLabel = DoctorPanel.Label(toggle, "VIEW LEADERBOARD", Vector2.zero, new Vector2(380f, 70f), 28, TextAnchor.MiddleCenter, Color.white);

        var button = DoctorPanel.Child("NewSession", card, new Color(0.2f, 0.75f, 0.4f, 1f));
        button.anchoredPosition = new Vector2(300f, -400f);
        button.sizeDelta = new Vector2(360f, 70f);
        var btn = button.gameObject.AddComponent<Button>();
        btn.targetGraphic = button.GetComponent<Image>();
        btn.onClick.AddListener(Close);
        DoctorPanel.Label(button, "NEW SESSION", Vector2.zero, new Vector2(360f, 70f), 34, TextAnchor.MiddleCenter, Color.white);

        root.SetActive(false);
    }

    void Close()
    {
        IsOpen = false;
        root.SetActive(false);
    }
}
