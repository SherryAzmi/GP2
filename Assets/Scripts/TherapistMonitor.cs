using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>One moment of a running session, for the therapist's monitor.</summary>
public struct LiveReading
{
    public float angle, target, limit; // degrees; target below 0 = none right now
    public int alerts;
    public bool cameraConnected, cameraLost;
    public string progress;            // e.g. "wave 3 of 6"
}

/// <summary>
/// Live view on the PC monitor (not in the headset): knee angle now, a 20 second graph with the target and
/// safety-limit lines, alerts and camera state.
/// </summary>
public class TherapistMonitor : MonoBehaviour
{
    const int W = 400, H = 160;
    const float Seconds = 20f, Top = 140f; // graph spans 0..140 degrees

    Func<LiveReading> read;
    GameObject root;
    Texture2D graph;
    Color32[] pixels;
    readonly float[] history = new float[W];
    Text angleText, targetText, infoText;
    float nextSample;
    bool inWorld; // floating in front of the headset wearer instead of on the PC monitor
    System.IO.StreamWriter log;
    float logStart;

    /// <summary>Knee-angle recording of the latest session, file name inside Sessions/.</summary>
    public static string CurrentFile { get; private set; }

    // Every sample of the knee angle goes to a file: this is the data the physiotherapist asks for.
    void OpenLog()
    {
        CloseLog();
        string id = SessionConfig.Active ? SessionConfig.PatientId : "unknown";
        CurrentFile = $"angles_{id}_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
        string folder = System.IO.Path.Combine(Application.persistentDataPath, "Sessions");
        System.IO.Directory.CreateDirectory(folder);
        log = new System.IO.StreamWriter(System.IO.Path.Combine(folder, CurrentFile)) { AutoFlush = true };
        log.WriteLine("seconds,angle_deg,target_deg,from_camera,progress");
        logStart = Time.time;
    }

    void CloseLog()
    {
        log?.Dispose();
        log = null;
    }

    void Record(LiveReading r)
    {
        if (log == null || SessionControls.Paused) return;
        var c = System.Globalization.CultureInfo.InvariantCulture;
        // An empty angle means the camera could not see the leg: never record a held value as measured.
        log.WriteLine(string.Join(",", (Time.time - logStart).ToString("0.00", c),
            r.cameraLost ? "" : r.angle.ToString("0.0", c), r.target >= 0f ? r.target.ToString("0.0", c) : "",
            r.cameraConnected ? "1" : "0", r.progress.Replace(',', ' ').Replace("  ·  ", " / ")));
    }

    public static TherapistMonitor Create(Transform owner, Func<LiveReading> read, bool inWorld = false)
    {
        var go = new GameObject("TherapistMonitor");
        go.transform.SetParent(owner, false);
        var monitor = go.AddComponent<TherapistMonitor>();
        monitor.read = read;
        monitor.inWorld = inWorld;
        monitor.Build();
        return monitor;
    }

    public void SetVisible(bool visible)
    {
        if (visible && !root.activeSelf)
        {
            for (int i = 0; i < W; i++) history[i] = -1f;
            OpenLog();
        }
        else if (!visible) CloseLog();
        root.SetActive(visible);
        if (visible && inWorld) DoctorPanel.PlaceInFront(root, 1.3f);
    }

    void Update()
    {
        if (!root.activeSelf || Time.unscaledTime < nextSample) return;
        nextSample = Time.unscaledTime + Seconds / W;

        var r = read();
        Record(r);
        if (!r.cameraLost && !SessionControls.Paused)
            CloudSync.SendLive(r.angle, r.target, r.limit, r.progress);
        Array.Copy(history, 1, history, 0, W - 1);
        history[W - 1] = r.cameraLost || SessionControls.Paused ? -1f : r.angle;

        angleText.text = $"{r.angle:0}°";
        angleText.color = r.angle > r.limit ? WaveHUD.Bad : Color.white;
        targetText.text = (r.target >= 0f ? $"target {r.target:0}°" : "no target now") + $"   ·   limit {r.limit:0}°";
        string camera = !r.cameraConnected ? "camera NOT connected (simulator)"
                      : r.cameraLost ? "camera cannot see the leg" : "camera connected";
        infoText.text = $"{r.progress}\nalerts {r.alerts}  ·  {camera}";
        Draw(r);
    }

    void Draw(LiveReading r)
    {
        var back = new Color32(2, 15, 31, 255);
        for (int i = 0; i < pixels.Length; i++) pixels[i] = back;
        if (r.target >= 0f) Line(r.target, new Color32(255, 217, 64, 255));
        Line(r.limit, new Color32(255, 100, 85, 255));
        var trace = new Color32(90, 210, 255, 255);
        int previous = -1;
        for (int x = 0; x < W; x++)
        {
            if (history[x] < 0f) { previous = -1; continue; }
            int y = Row(history[x]);
            int from = previous < 0 ? y : previous;
            for (int j = Mathf.Min(from, y); j <= Mathf.Max(from, y); j++) pixels[j * W + x] = trace;
            pixels[Mathf.Min(H - 1, y + 1) * W + x] = trace;
            previous = y;
        }
        graph.SetPixels32(pixels);
        graph.Apply(false);
    }

    static int Row(float degrees) => Mathf.Clamp(Mathf.RoundToInt(degrees / Top * (H - 1)), 0, H - 1);

    void Line(float degrees, Color32 colour)
    {
        int y = Row(degrees);
        for (int x = 0; x < W; x += 2) pixels[y * W + x] = colour;
    }

    void OnDestroy()
    {
        CloseLog();
        if (graph) Destroy(graph);
    }

    void Build()
    {
        root = new GameObject("Root", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.transform.SetParent(transform, false);
        DoctorPanel.ConfigureCanvas(root, inWorld, 89);
        var card = DoctorPanel.Child("Card", (RectTransform)root.transform, new Color(0.02f, 0.13f, 0.24f, 0.95f));
        card.anchorMin = card.anchorMax = card.pivot = Vector2.zero;
        card.anchoredPosition = new Vector2(20f, 20f);
        card.sizeDelta = new Vector2(440f, 340f);
        if (inWorld) // to the left of the centre, so the patient's leg stays visible
        {
            card.anchorMin = card.anchorMax = card.pivot = new Vector2(0.5f, 0.5f);
            card.anchoredPosition = new Vector2(-480f, 120f);
        }

        DoctorPanel.Label(card, "PHYSIOTHERAPIST VIEW", new Vector2(0, 148f), new Vector2(400f, 26f), 20, TextAnchor.MiddleLeft, WaveHUD.Accent);
        angleText = DoctorPanel.Label(card, "", new Vector2(0, 110f), new Vector2(400f, 52f), 46, TextAnchor.MiddleLeft, Color.white);
        targetText = DoctorPanel.Label(card, "", new Vector2(60f, 105f), new Vector2(280f, 30f), 19, TextAnchor.MiddleRight, Color.white);

        graph = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        pixels = new Color32[W * H];
        var image = new GameObject("Graph", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
        image.transform.SetParent(card, false);
        image.rectTransform.anchoredPosition = new Vector2(0, -2f);
        image.rectTransform.sizeDelta = new Vector2(W, H);
        image.texture = graph;
        image.raycastTarget = false;
        DoctorPanel.Label(card, "last 20 seconds  ·  0 to 140°", new Vector2(0, -94f), new Vector2(400f, 22f), 15,
                          TextAnchor.MiddleRight, new Color(1f, 1f, 1f, 0.6f));
        infoText = DoctorPanel.Label(card, "", new Vector2(0, -134f), new Vector2(400f, 54f), 19, TextAnchor.MiddleLeft, Color.white);
        for (int i = 0; i < W; i++) history[i] = -1f;
        root.SetActive(false);
    }
}
