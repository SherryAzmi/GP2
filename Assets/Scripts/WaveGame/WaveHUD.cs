using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Patient HUD (GP1 4.3.4): one large world-space panel with high-contrast text,
/// a knee-angle gauge with the target and safety-limit markers, and a hold bar.
/// Builds itself at runtime, so the scene only needs this component.
/// </summary>
public class WaveHUD : MonoBehaviour
{
    [Tooltip("Gauge full scale, degrees")]
    public float gaugeMaxAngle = 140f;

    const float Width = 1000f, Height = 560f, GaugeWidth = 820f;

    Text statusText, angleText, infoText, warningText;
    RectTransform gaugeFill, targetMarker, ceilingMarker, holdFill;
    Image gaugeFillImage;
    GameObject holdGroup;

    static readonly Color Panel = new Color(0.04f, 0.12f, 0.22f, 0.78f);
    static readonly Color GaugeBack = new Color(1f, 1f, 1f, 0.15f);
    public static readonly Color Neutral = Color.white;
    public static readonly Color Good = new Color(0.35f, 1f, 0.5f);
    public static readonly Color Bad = new Color(1f, 0.45f, 0.35f);
    public static readonly Color Accent = new Color(1f, 0.85f, 0.25f);

    void Awake()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var rt = (RectTransform)transform;
        rt.sizeDelta = new Vector2(Width, Height);
        rt.localScale = Vector3.one * 0.0013f;

        var bg = MakeRect("Panel", rt, Vector2.zero, new Vector2(Width, Height));
        bg.gameObject.AddComponent<Image>().color = Panel;

        infoText = MakeText("Info", rt, new Vector2(0, 230), new Vector2(940, 50), 34, TextAnchor.MiddleCenter);
        statusText = MakeText("Status", rt, new Vector2(0, 140), new Vector2(940, 130), 58, TextAnchor.MiddleCenter);
        angleText = MakeText("Angle", rt, new Vector2(0, 30), new Vector2(940, 70), 46, TextAnchor.MiddleCenter);

        var gaugeBack = MakeRect("Gauge", rt, new Vector2(0, -50), new Vector2(GaugeWidth, 46));
        gaugeBack.gameObject.AddComponent<Image>().color = GaugeBack;
        gaugeFill = MakeRect("Fill", gaugeBack, Vector2.zero, new Vector2(0, 46));
        gaugeFill.anchorMin = gaugeFill.anchorMax = gaugeFill.pivot = new Vector2(0, 0.5f);
        gaugeFillImage = gaugeFill.gameObject.AddComponent<Image>();
        targetMarker = MakeMarker("Target", gaugeBack, Accent, 70);
        ceilingMarker = MakeMarker("SafetyLimit", gaugeBack, Bad, 70);

        holdGroup = new GameObject("Hold", typeof(RectTransform));
        holdGroup.transform.SetParent(rt, false);
        var holdBack = MakeRect("HoldBack", (RectTransform)holdGroup.transform, new Vector2(0, -120), new Vector2(GaugeWidth, 22));
        holdBack.gameObject.AddComponent<Image>().color = GaugeBack;
        holdFill = MakeRect("HoldFill", holdBack, Vector2.zero, new Vector2(0, 22));
        holdFill.anchorMin = holdFill.anchorMax = holdFill.pivot = new Vector2(0, 0.5f);
        holdFill.gameObject.AddComponent<Image>().color = Accent;

        warningText = MakeText("Warning", rt, new Vector2(0, -205), new Vector2(940, 80), 44, TextAnchor.MiddleCenter);
        warningText.color = Bad;

        SetHold(-1f);
        SetWarning(null);
    }

    public void SetInfo(string text) => infoText.text = text;

    public void SetStatus(string text, Color color)
    {
        statusText.text = text;
        statusText.color = color;
    }

    /// <param name="target">Target angle, or a negative value to hide the marker.</param>
    public void SetAngle(float angle, float target, float ceiling)
    {
        angleText.text = target >= 0f
            ? $"Knee  {angle:0}°   /   target  {target:0}°"
            : $"Knee  {angle:0}°";
        bool onTarget = target >= 0f && angle >= target && angle <= ceiling;
        angleText.color = onTarget ? Good : Neutral;
        gaugeFillImage.color = angle > ceiling ? Bad : onTarget ? Good : new Color(0.4f, 0.75f, 1f);

        gaugeFill.sizeDelta = new Vector2(GaugeX(angle), gaugeFill.sizeDelta.y);
        targetMarker.gameObject.SetActive(target >= 0f);
        targetMarker.anchoredPosition = new Vector2(GaugeX(target), 0);
        ceilingMarker.anchoredPosition = new Vector2(GaugeX(ceiling), 0);
    }

    /// <param name="fraction">0..1 progress, or a negative value to hide the bar.</param>
    public void SetHold(float fraction)
    {
        holdGroup.SetActive(fraction >= 0f);
        if (fraction >= 0f) holdFill.sizeDelta = new Vector2(GaugeWidth * Mathf.Clamp01(fraction), holdFill.sizeDelta.y);
    }

    public void SetWarning(string text)
    {
        warningText.gameObject.SetActive(!string.IsNullOrEmpty(text));
        warningText.text = text;
    }

    float GaugeX(float angle) => GaugeWidth * Mathf.Clamp01(angle / gaugeMaxAngle);

    static RectTransform MakeRect(string name, RectTransform parent, Vector2 pos, Vector2 size)
    {
        var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return rt;
    }

    static RectTransform MakeMarker(string name, RectTransform gauge, Color color, float height)
    {
        var m = MakeRect(name, gauge, Vector2.zero, new Vector2(8, height));
        m.anchorMin = m.anchorMax = new Vector2(0, 0.5f);
        m.gameObject.AddComponent<Image>().color = color;
        return m;
    }

    static Text MakeText(string name, RectTransform parent, Vector2 pos, Vector2 size, int fontSize, TextAnchor anchor)
    {
        var t = MakeRect(name, parent, pos, size).gameObject.AddComponent<Text>();
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = fontSize;
        t.alignment = anchor;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.color = Neutral;
        return t;
    }
}
