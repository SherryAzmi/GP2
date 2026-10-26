using System;
using System.Globalization;
using System.IO;
using UnityEngine;

/// <summary>
/// Writes one CSV row per wave (GP1 FR-06 / section 4.4).
/// Every row is flushed straight away so a crash never loses the session.
/// Files go to: [persistentDataPath]/Sessions/
/// </summary>
public class SessionLogger : IDisposable
{
    public string FilePath { get; }
    StreamWriter writer;

    const string Header = "session_id,patient_id,operated_leg,wave,time_utc,target_percent,target_deg,hold_s," +
                          "min_angle_in_hold,max_angle_in_hold,held_fraction,cleared,points,total_score,session_max_angle,safety_alerts";

    readonly string sessionId;
    readonly string patientId;
    readonly string operatedLeg;

    public SessionLogger(string patientId, string operatedLeg)
    {
        this.patientId = Clean(patientId);
        this.operatedLeg = operatedLeg;
        sessionId = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);

        string dir = Path.Combine(Application.persistentDataPath, "Sessions");
        Directory.CreateDirectory(dir);
        FilePath = Path.Combine(dir, $"wave_session_{this.patientId}_{sessionId}.csv");

        writer = new StreamWriter(FilePath, false);
        writer.WriteLine(Header);
        writer.Flush();
    }

    public void LogWave(int wave, float targetPercent, float targetDeg, float holdSeconds,
                        float minAngle, float maxAngle, float heldFraction, bool cleared,
                        int points, int totalScore, float sessionMaxAngle, int safetyAlerts)
    {
        if (writer == null) return;
        var c = CultureInfo.InvariantCulture;
        writer.WriteLine(string.Join(",",
            sessionId, patientId, operatedLeg, wave.ToString(c),
            DateTime.UtcNow.ToString("o", c),
            targetPercent.ToString("F2", c), targetDeg.ToString("F1", c), holdSeconds.ToString("F2", c),
            minAngle.ToString("F1", c), maxAngle.ToString("F1", c), heldFraction.ToString("F2", c),
            cleared ? "1" : "0", points.ToString(c), totalScore.ToString(c),
            sessionMaxAngle.ToString("F1", c), safetyAlerts.ToString(c)));
        writer.Flush();
    }

    public void Dispose()
    {
        writer?.Dispose();
        writer = null;
    }

    static string Clean(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "anonymous";
        foreach (char ch in Path.GetInvalidFileNameChars()) s = s.Replace(ch, '_');
        return s.Replace(',', '_').Trim();
    }
}
