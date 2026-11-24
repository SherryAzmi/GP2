using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Patient HUD (GP1 4.3.4), shared by the beach and football games and themed per game.
///  - A world-space panel with a title header, score and streak chips, high-contrast status text,
///    a knee-angle gauge with target and safety-limit markers, a hold bar and a warning pill.
///  - A start card and a results card that float straight ahead of the patient.
///  - Big pop-up banners ("GOAL!", "WAVE CLEARED!") that punch in and fade out.
/// Builds itself at runtime, so the scene only needs this component.
/// </summary>
public class WaveHUD : MonoBehaviour
{
    [Tooltip("Colours and title of the panel")]
    public UIStyle.Theme theme = UIStyle.Theme.Sea;
    [Tooltip("Gauge full scale, degrees")]
    public float gaugeMaxAngle = 140f;

    const float Width = 1000f, Height = 600f, GaugeWidth = 820f;

    public static readonly Color Neutral = Color.white;
    public static readonly Color Good = new Color(0.35f, 1f, 0.5f);
    public static readonly Color Bad = new Color(1f, 0.45f, 0.35f);
    public static readonly Color Accent = new Color(1f, 0.85f, 0.25f);
    static readonly Color GaugeBack = new Color(1f, 1f, 1f, 0.14f);

    UIStyle.Palette pal;
    Image panelImage, edgeImage, headerImage;
    Text titleText, scoreText, streakText, statusText, angleText, infoText, warningText;
    GameObject scoreChip, streakChip, warningPill;
    RectTransform gaugeFill, targetMarker, ceilingMarker, holdFill;
    Image gaugeFillImage;
    GameObject holdGroup;

    // Floating cards and pop-up
    RectTransform titleCard, resultsCard, popup;
    Text cardTitle, cardSub, cardPrompt, resTitle, resRating, resPrompt;
    Text[] resStars;
    RectTransform resRows;
    Text popupBig, popupSmall;
    Image popupBack, popupEdge;
    Coroutine popupRoutine;
    float popupScale = 0.0022f;

    void Awake()
    {
        pal = UIStyle.For(theme);
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var rt = (RectTransform)transform;
        rt.sizeDelta = new Vector2(Width, Height);
        rt.localScale = Vector3.one * 0.0013f;

        // Panel with a thin coloured trim.
        edgeImage = UIStyle.Box("Edge", rt, Vector2.zero, new Vector2(Width + 12f, Height + 12f), pal.edge);
        panelImage = UIStyle.Box("Panel", rt, Vector2.zero, new Vector2(Width, Height), pal.panel);

        // Header: game title on the left, score and streak chips on the right.
        headerImage = UIStyle.Box("Header", rt, new Vector2(0, Height / 2f - 50f), new Vector2(Width - 24f, 76f), pal.header, 0.8f);
        titleText = UIStyle.Label("Title", headerImage.transform, new Vector2(-180f, 0), new Vector2(560f, 70f), 40,
                                  TextAnchor.MiddleLeft, Color.white, FontStyle.Bold, pal.title);
        scoreChip = Chip("Score", headerImage.transform, new Vector2(250f, 0), 210f, out scoreText);
        streakChip = Chip("Streak", headerImage.transform, new Vector2(400f, 0), 130f, out streakText);
        SetScore(-1, 0);

        infoText = UIStyle.Label("Info", rt, new Vector2(0, 180), new Vector2(940, 46), 30, TextAnchor.MiddleCenter, pal.soft);
        statusText = UIStyle.Label("Status", rt, new Vector2(0, 90), new Vector2(940, 130), 60, TextAnchor.MiddleCenter, Neutral, FontStyle.Bold);
        angleText = UIStyle.Label("Angle", rt, new Vector2(0, -10), new Vector2(940, 64), 44, TextAnchor.MiddleCenter, Neutral);

        var gaugeBack = UIStyle.Box("Gauge", rt, new Vector2(0, -85), new Vector2(GaugeWidth, 46), GaugeBack, 0.9f).rectTransform;
        gaugeFillImage = UIStyle.Box("Fill", gaugeBack, Vector2.zero, new Vector2(0, 46), Color.white, 0.9f);
        gaugeFill = gaugeFillImage.rectTransform;
        gaugeFill.anchorMin = gaugeFill.anchorMax = gaugeFill.pivot = new Vector2(0, 0.5f);
        targetMarker = MakeMarker("Target", gaugeBack, Accent, 72);
        ceilingMarker = MakeMarker("SafetyLimit", gaugeBack, Bad, 72);

        holdGroup = UIStyle.Rect("Hold", rt, Vector2.zero, Vector2.zero).gameObject;
        var holdBack = UIStyle.Box("HoldBack", holdGroup.transform, new Vector2(0, -150), new Vector2(GaugeWidth, 22), GaugeBack, 0.45f).rectTransform;
        var holdImg = UIStyle.Box("HoldFill", holdBack, Vector2.zero, new Vector2(0, 22), Accent, 0.45f);
        holdFill = holdImg.rectTransform;
        holdFill.anchorMin = holdFill.anchorMax = holdFill.pivot = new Vector2(0, 0.5f);

        var pill = UIStyle.Box("Warning", rt, new Vector2(0, -230), new Vector2(900, 74), new Color(0.75f, 0.12f, 0.1f, 0.95f), 1.4f);
        warningPill = pill.gameObject;
        warningText = UIStyle.Label("Text", pill.transform, Vector2.zero, new Vector2(880, 70), 38, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);

        BuildTitleCard();
        BuildResultsCard();
        BuildPopup();

        SetHold(-1f);
        SetWarning(null);
    }

    void OnDestroy()
    {
        if (titleCard) Destroy(titleCard.gameObject);
        if (resultsCard) Destroy(resultsCard.gameObject);
        if (popup) Destroy(popup.gameObject);
    }

    /// <summary>Switches colours and title at runtime (e.g. the football game sets Football).</summary>
    public void ApplyTheme(UIStyle.Theme newTheme)
    {
        theme = newTheme;
        pal = UIStyle.For(theme);
        if (!panelImage) return;
        panelImage.color = pal.panel;
        edgeImage.color = pal.edge;
        headerImage.color = pal.header;
        titleText.text = pal.title;
        infoText.color = pal.soft;
        foreach (var card in new[] { titleCard, resultsCard })
        {
            card.Find("Edge").GetComponent<Image>().color = pal.edge;
            card.Find("Panel").GetComponent<Image>().color = pal.panel;
            card.Find("Header").GetComponent<Image>().color = pal.header;
        }
        popupBack.color = pal.panel;
        popupEdge.color = pal.edge;
    }

    // ---------------- Panel API ----------------

    public void SetInfo(string text) => infoText.text = text;

    public void SetStatus(string text, Color color)
    {
        statusText.text = text;
        statusText.color = color;
    }

    /// <summary>Score chip in the header; a negative score hides both chips. The streak chip shows from 2 in a row.</summary>
    public void SetScore(int score, int streak)
    {
        scoreChip.SetActive(score >= 0);
        streakChip.SetActive(score >= 0 && streak >= 2);
        scoreText.text = $"SCORE  {Mathf.Max(0, score)}";
        streakText.text = $"x{streak}";
    }

    /// <param name="target">Target angle, or a negative value to hide the marker.</param>
    /// <param name="straighten">The target is reached by coming down to it (extension) rather than up to it.</param>
    public void SetAngle(float angle, float target, float ceiling, bool straighten = false)
    {
        angleText.text = target < 0f ? $"Knee  {angle:0}°"
                       : straighten ? $"Knee  {angle:0}°   /   straighten to  {target:0}°"
                       : $"Knee  {angle:0}°   /   target  {target:0}°";
        bool onTarget = target >= 0f && angle <= ceiling && (straighten ? angle <= target : angle >= target);
        angleText.color = onTarget ? Good : Neutral;
        gaugeFillImage.color = angle > ceiling ? Bad : onTarget ? Good : new Color(0.4f, 0.75f, 1f);

        gaugeFill.sizeDelta = new Vector2(Mathf.Max(28f, GaugeX(angle)), gaugeFill.sizeDelta.y);
        targetMarker.gameObject.SetActive(target >= 0f);
        targetMarker.anchoredPosition = new Vector2(GaugeX(target), 0);
        ceilingMarker.anchoredPosition = new Vector2(GaugeX(ceiling), 0);
    }

    /// <param name="fraction">0..1 progress, or a negative value to hide the bar.</param>
    /// <param name="active">The patient is on target right now; the loading tone plays only then.</param>
    public void SetHold(float fraction, bool active = true)
    {
        UISound.Hold(fraction, active);
        holdGroup.SetActive(fraction >= 0f);
        if (fraction >= 0f) holdFill.sizeDelta = new Vector2(Mathf.Max(22f, GaugeWidth * Mathf.Clamp01(fraction)), holdFill.sizeDelta.y);
    }

    public void SetWarning(string text)
    {
        warningPill.SetActive(!string.IsNullOrEmpty(text));
        warningText.text = text;
    }

    float GaugeX(float angle) => GaugeWidth * Mathf.Clamp01(angle / gaugeMaxAngle);

    // ---------------- Start card ----------------

    /// <summary>Start screen floating in front of the patient: game title, who is playing and how to begin.</summary>
    public void ShowTitleCard(string subtitle, string prompt)
    {
        HideResults();
        cardTitle.text = pal.title;
        cardSub.text = subtitle;
        cardPrompt.text = prompt;
        UIStyle.PlaceAhead(titleCard, 2.4f, -0.15f);
        titleCard.gameObject.SetActive(true);
        SetPanelVisible(false);
    }

    public void HideTitleCard()
    {
        if (titleCard) titleCard.gameObject.SetActive(false);
        SetPanelVisible(!ResultsShowing);
    }

    /// <summary>The cards repeat what the panel says, and in the beach scene they would overlap it, so the panel hides.</summary>
    void SetPanelVisible(bool visible)
    {
        var canvas = GetComponent<Canvas>();
        if (canvas) canvas.enabled = visible;
    }

    public bool ResultsShowing => resultsCard && resultsCard.gameObject.activeSelf;

    // ---------------- Results card ----------------

    /// <summary>End-of-session card: 0-3 stars, a rating line and label/value rows.</summary>
    public void ShowResults(string title, int stars, string rating, Color ratingColor, (string label, string value, Color color)[] rows, string prompt)
    {
        HideTitleCard();
        resTitle.text = title;
        resRating.text = rating;
        resRating.color = ratingColor;
        resPrompt.text = prompt;
        for (int i = 0; i < resStars.Length; i++) resStars[i].color = i < stars ? Accent : new Color(1f, 1f, 1f, 0.18f);

        for (int i = resRows.childCount - 1; i >= 0; i--) Destroy(resRows.GetChild(i).gameObject);
        for (int i = 0; i < rows.Length; i++)
        {
            float y = -i * 58f;
            if (i % 2 == 0) UIStyle.Box("Stripe", resRows, new Vector2(0, y), new Vector2(760f, 54f), new Color(1f, 1f, 1f, 0.06f), 0.5f);
            UIStyle.Label("L", resRows, new Vector2(-170f, y), new Vector2(400f, 50f), 32, TextAnchor.MiddleLeft, pal.soft, FontStyle.Normal, rows[i].label);
            UIStyle.Label("V", resRows, new Vector2(200f, y), new Vector2(320f, 50f), 36, TextAnchor.MiddleRight, rows[i].color, FontStyle.Bold, rows[i].value);
        }
        UIStyle.PlaceAhead(resultsCard, 2.4f, -0.1f);
        resultsCard.gameObject.SetActive(true);
        SetPanelVisible(false);
        if (isActiveAndEnabled) StartCoroutine(StarPop(stars));
    }

    public void HideResults()
    {
        if (resultsCard) resultsCard.gameObject.SetActive(false);
        SetPanelVisible(!(titleCard && titleCard.gameObject.activeSelf));
    }

    IEnumerator StarPop(int stars)
    {
        foreach (var s in resStars) s.transform.localScale = Vector3.zero;
        for (int i = 0; i < resStars.Length; i++)
        {
            yield return new WaitForSeconds(0.25f);
            for (float t = 0f; t < 0.3f; t += Time.deltaTime)
            {
                resStars[i].transform.localScale = Vector3.one * Punch(t / 0.3f);
                yield return null;
            }
            resStars[i].transform.localScale = Vector3.one;
        }
    }

    // ---------------- Pop-up banner ----------------

    /// <summary>Big banner straight ahead of the patient that punches in, holds and fades out.</summary>
    public void Popup(string big, string small, Color color, float seconds = 1.6f)
    {
        if (!isActiveAndEnabled) return;
        if (popupRoutine != null) StopCoroutine(popupRoutine);
        popupBig.text = big;
        popupBig.color = color;
        popupSmall.text = small;
        popupSmall.gameObject.SetActive(!string.IsNullOrEmpty(small));
        UIStyle.PlaceAhead(popup, 3f, 0.35f);
        popupScale = 0.0022f;
        // When this panel is straight ahead (beach game), float the banner just in front of it, scaled down to look
        // the same size, so it overlays the panel instead of hiding behind it (or behind the umbrella above).
        var cam = Camera.main;
        if (cam)
        {
            Vector3 eye = cam.transform.position;
            Vector3 toPanel = transform.position - eye, toPopup = popup.position - eye;
            Vector3 flatPanel = new Vector3(toPanel.x, 0f, toPanel.z), flatPopup = new Vector3(toPopup.x, 0f, toPopup.z);
            if (Vector3.Angle(flatPanel, flatPopup) < 30f && toPanel.magnitude < 3.2f)
            {
                float k = toPanel.magnitude * 0.8f / 3f;
                popup.position = eye + toPanel * 0.8f + Vector3.up * 0.06f;
                popup.rotation = Quaternion.LookRotation(flatPanel.normalized);
                popupScale *= k;
            }
        }
        popup.gameObject.SetActive(true);
        popupRoutine = StartCoroutine(PopupRoutine(seconds));
    }

    IEnumerator PopupRoutine(float seconds)
    {
        var group = popup.GetComponent<CanvasGroup>();
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            float inK = Mathf.Clamp01(t / 0.35f);
            float outK = Mathf.Clamp01((seconds - t) / 0.35f);
            popup.localScale = Vector3.one * popupScale * Punch(inK) * (0.9f + 0.1f * outK);
            group.alpha = outK;
            popupBig.transform.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(t * 9f) * 2f * (1f - inK * 0.5f));
            yield return null;
        }
        popup.gameObject.SetActive(false);
        popupRoutine = null;
    }

    /// <summary>0 → overshoot → 1, for a springy pop.</summary>
    static float Punch(float k)
    {
        k = Mathf.Clamp01(k);
        return 1f + 2.2f * Mathf.Pow(k - 1f, 3f) + 1.2f * Mathf.Pow(k - 1f, 2f);
    }

    // ---------------- Construction ----------------

    static GameObject Chip(string name, Transform parent, Vector2 pos, float width, out Text label)
    {
        var chip = UIStyle.Box(name, parent, pos, new Vector2(width, 54f), new Color(0f, 0f, 0f, 0.35f), 1.1f);
        label = UIStyle.Label("Text", chip.transform, Vector2.zero, new Vector2(width - 10f, 50f), 30, TextAnchor.MiddleCenter, Accent, FontStyle.Bold);
        return chip.gameObject;
    }

    RectTransform Card(string name, Vector2 size)
    {
        var card = UIStyle.WorldCanvas(name, size, 0.0016f, 50);
        UIStyle.Box("Edge", card, Vector2.zero, size + new Vector2(14f, 14f), pal.edge);
        UIStyle.Box("Panel", card, Vector2.zero, size, pal.panel);
        UIStyle.Box("Header", card, new Vector2(0, size.y / 2f - 70f), new Vector2(size.x - 30f, 116f), pal.header, 0.8f);
        card.gameObject.SetActive(false);
        return card;
    }

    void BuildTitleCard()
    {
        titleCard = Card("StartCard", new Vector2(1000f, 560f));
        var header = titleCard.Find("Header");
        cardTitle = UIStyle.Label("Title", header, Vector2.zero, new Vector2(940f, 110f), 72, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
        cardSub = UIStyle.Label("Sub", titleCard, new Vector2(0, 40f), new Vector2(920f, 160f), 36, TextAnchor.MiddleCenter, pal.soft);
        var button = UIStyle.Box("Button", titleCard, new Vector2(0, -170f), new Vector2(620f, 110f), new Color(0.2f, 0.75f, 0.4f, 1f), 2f);
        cardPrompt = UIStyle.Label("Prompt", button.transform, Vector2.zero, new Vector2(600f, 100f), 44, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
        pulseA = button.transform;
    }

    void BuildResultsCard()
    {
        resultsCard = Card("ResultsCard", new Vector2(1000f, 900f));
        var header = resultsCard.Find("Header");
        resTitle = UIStyle.Label("Title", header, Vector2.zero, new Vector2(940f, 110f), 64, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);

        resStars = new Text[3];
        for (int i = 0; i < 3; i++)
        {
            // U+2605 BLACK STAR is in the built-in font.
            resStars[i] = UIStyle.Label("Star" + i, resultsCard, new Vector2((i - 1) * 150f, i == 1 ? 250f : 230f),
                                        new Vector2(160f, 160f), i == 1 ? 150 : 120, TextAnchor.MiddleCenter, Accent, FontStyle.Normal, "★");
        }
        resRating = UIStyle.Label("Rating", resultsCard, new Vector2(0, 130f), new Vector2(920f, 70f), 46, TextAnchor.MiddleCenter, Good, FontStyle.Bold);
        resRows = UIStyle.Rect("Rows", resultsCard, new Vector2(0, 50f), new Vector2(800f, 10f));
        var button = UIStyle.Box("Button", resultsCard, new Vector2(0, -370f), new Vector2(620f, 100f), new Color(0.2f, 0.75f, 0.4f, 1f), 2f);
        resPrompt = UIStyle.Label("Prompt", button.transform, Vector2.zero, new Vector2(590f, 80f), 40, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
        // Long prompts ("A: PLAY STAGE 3 · ESC: MENU") shrink to stay on one line.
        resPrompt.horizontalOverflow = HorizontalWrapMode.Overflow;
        resPrompt.resizeTextForBestFit = true;
        resPrompt.resizeTextMinSize = 20;
        resPrompt.resizeTextMaxSize = 40;
        resPrompt.horizontalOverflow = HorizontalWrapMode.Wrap;
        resPrompt.verticalOverflow = VerticalWrapMode.Truncate;
        pulseB = button.transform;
    }

    void BuildPopup()
    {
        popup = UIStyle.WorldCanvas("Popup", new Vector2(900f, 300f), 0.0022f, 60);
        popup.gameObject.AddComponent<CanvasGroup>();
        popupEdge = UIStyle.Box("Edge", popup, Vector2.zero, new Vector2(894f, 274f), pal.edge, 3f);
        popupBack = UIStyle.Box("Back", popup, Vector2.zero, new Vector2(880f, 260f), pal.panel, 3f);
        popupBig = UIStyle.Label("Big", popup, new Vector2(0, 30f), new Vector2(840f, 150f), 130, TextAnchor.MiddleCenter, Good, FontStyle.Bold);
        // Long words ("WAVE CLEARED!") shrink to stay on one line.
        popupBig.resizeTextForBestFit = true;
        popupBig.resizeTextMinSize = 50;
        popupBig.resizeTextMaxSize = 130;
        popupBig.horizontalOverflow = HorizontalWrapMode.Wrap;
        popupBig.verticalOverflow = VerticalWrapMode.Truncate;
        var outline = popupBig.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.6f);
        outline.effectDistance = new Vector2(4f, -4f);
        popupSmall = UIStyle.Label("Small", popup, new Vector2(0, -85f), new Vector2(860f, 70f), 46, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
        popup.gameObject.SetActive(false);
    }

    Transform pulseA, pulseB;

    /// <summary>Gentle breathing scale on the "press to start" buttons.</summary>
    void Update()
    {
        var s = Vector3.one * (1f + 0.04f * Mathf.Sin(Time.time * 4f));
        if (pulseA) pulseA.localScale = s;
        if (pulseB) pulseB.localScale = s;

        // Cards trail the player's view slowly, so they stay readable if the head turns or the rig settles.
        float blend = 1f - Mathf.Exp(-Time.deltaTime * 2f);
        if (titleCard && titleCard.gameObject.activeSelf) UIStyle.FollowAhead(titleCard, 2.4f, -0.15f, blend);
        if (resultsCard && resultsCard.gameObject.activeSelf) UIStyle.FollowAhead(resultsCard, 2.4f, -0.1f, blend);
    }

    void OnDisable()
    {
        // Coroutines stop with the object; don't leave a half-faded banner floating.
        if (popup) popup.gameObject.SetActive(false);
        popupRoutine = null;
    }

    static RectTransform MakeMarker(string name, RectTransform gauge, Color color, float height)
    {
        var m = UIStyle.Box(name, gauge, Vector2.zero, new Vector2(10, height), color, 0.2f).rectTransform;
        m.anchorMin = m.anchorMax = new Vector2(0, 0.5f);
        return m;
    }
}
