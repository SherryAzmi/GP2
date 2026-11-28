using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Optional upload to the clinic's Cloudflare Worker (folder cloud/). Does nothing unless
/// persistentDataPath/cloud.json holds {"url":"...","key":"..."}; that file stays on this PC and is never in git.
/// Everything is saved locally first. Names, phones and notes are never sent.
/// </summary>
public class CloudSync : MonoBehaviour
{
    [System.Serializable] class Config { public string url; public string key; }

    [System.Serializable]
    class Patient
    {
        public string id, operated_leg, surgery_type, surgery_date, updated;
        public int age;
        public float height_cm, weight_kg, safety_limit, comfortable_max, extension_goal;
    }

    [System.Serializable]
    class Session
    {
        public string date, game, leg, settings;
        public int stage, done, planned, stopped_early, eased, success_percent, score, safety_alerts, balance_pauses, pain;
        public float duration_s, best_bend, avg_bend, straightest;
    }

    [System.Serializable] class Upload { public Patient patient; public List<Session> sessions = new List<Session>(); }

    [System.Serializable]
    class Live
    {
        public string type, patient, leg;
        public float angle, target, limit;
        public int ms;
        public long t; // Unix milliseconds, so a listener can ignore old messages
    }

    static CloudSync instance;
    static Config config;
    static bool loaded;
    float nextLive;

    public static bool Enabled
    {
        get
        {
            if (!loaded)
            {
                loaded = true;
                string path = Path.Combine(Application.persistentDataPath, "cloud.json");
                if (File.Exists(path)) config = JsonUtility.FromJson<Config>(File.ReadAllText(path));
            }
            return config != null && !string.IsNullOrEmpty(config.url) && !string.IsNullOrEmpty(config.key);
        }
    }

    static CloudSync Instance
    {
        get
        {
            if (!instance)
            {
                var go = new GameObject("CloudSync");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<CloudSync>();
            }
            return instance;
        }
    }

    /// <summary>Sends one patient with all their sessions. Sending again is safe, so a lost upload is repeated next time.</summary>
    public static void Push(PatientRecord p)
    {
        if (!Enabled || p == null || !Application.isPlaying) return;
        var upload = new Upload
        {
            patient = new Patient
            {
                id = p.id, operated_leg = p.operatedLeg, age = p.age, height_cm = p.heightCm, weight_kg = p.weightKg,
                surgery_type = p.surgeryType, surgery_date = p.surgeryDate, safety_limit = p.SafetyLimit,
                comfortable_max = p.comfortableMaxAngle, extension_goal = p.extensionGoal,
                updated = System.DateTime.UtcNow.ToString("o"),
            },
        };
        // The newest 200 are enough: older ones were sent before.
        for (int i = Mathf.Max(0, p.history.Count - 200); i < p.history.Count; i++)
        {
            var e = p.history[i];
            upload.sessions.Add(new Session
            {
                date = e.date, game = e.game, stage = e.stage, leg = e.leg, duration_s = e.durationSeconds,
                done = e.done, planned = e.planned, stopped_early = e.stoppedEarly ? 1 : 0, eased = e.easier ? 1 : 0,
                success_percent = e.percent, score = e.score, best_bend = e.bestAngle, avg_bend = e.avgAngle,
                straightest = e.hasExtension ? e.bestExtension : -1f, safety_alerts = e.safetyAlerts,
                balance_pauses = e.balancePauses, pain = e.hasPain ? e.pain : -1, settings = e.settings,
            });
        }
        Instance.StartCoroutine(Post("/sync", JsonUtility.ToJson(upload)));
    }

    /// <summary>Live knee angle, at most 5 times a second.</summary>
    public static void SendLive(float angle, float target, float limit, string leg)
    {
        if (!Enabled || Time.unscaledTime < Instance.nextLive) return;
        Instance.nextLive = Time.unscaledTime + 0.2f;
        Send(new Live { type = "angle", angle = angle, target = target, limit = limit, leg = leg });
    }

    /// <summary>Tells the spray device to run. The device must enforce its own maximum time.</summary>
    public static void SendSplash(int milliseconds)
    {
        if (Enabled) Send(new Live { type = "splash", ms = milliseconds });
    }

    static void Send(Live message)
    {
        message.patient = SessionConfig.Active ? SessionConfig.PatientId : "";
        message.t = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Instance.StartCoroutine(Post("/live", JsonUtility.ToJson(message)));
    }

    static IEnumerator Post(string path, string body)
    {
        using var request = new UnityWebRequest(config.url.TrimEnd('/') + path, "POST");
        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", "Bearer " + config.key);
        request.timeout = 10;
        yield return request.SendWebRequest();
        if (request.result != UnityWebRequest.Result.Success)
            Debug.LogWarning($"[Cloud] {path} failed: {request.responseCode} {request.error}");
    }
}
