using System;
using System.Globalization;
using System.IO;
using UnityEngine;

/// <summary>
/// Writes one CSV row per kick (GP1 FR-06 / section 4.4), like SessionLogger does per wave.
/// Every row is flushed straight away so a crash never loses the session.
/// Files go to: [persistentDataPath]/Sessions/
/// </summary>
public class KickLogger : IDisposable
{
    public string FilePath { get; }

    StreamWriter writer;

    const string Header = "session_id,patient_id,operated_leg,support,kick,time_utc,target_zone,windup_percent,windup_target_deg," +
                          "max_flexion,min_extension,windup_reached,full_extension,hit,points,total_score,session_max_angle," +
                          "safety_alerts,balance_pauses";

    readonly string sessionId;
    readonly string patientId;
    readonly string operatedLeg;
    readonly bool support;

    public KickLogger(string patientId, string operatedLeg, bool support)
    {
        this.patientId = Clean(patientId);
        this.operatedLeg = operatedLeg;
        this.support = support;
        sessionId = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);

        string dir = Path.Combine(Application.persistentDataPath, "Sessions");
        Directory.CreateDirectory(dir);
        FilePath = Path.Combine(dir, $"kick_session_{this.patientId}_{sessionId}.csv");
        writer = new StreamWriter(FilePath, false);
        writer.WriteLine(Header);
        writer.Flush();
    }

    public void LogKick(int kick, string zone, float windUpPercent, float windUpTarget, float maxFlexion, float minExtension,
                        bool windUpReached, bool fullExtension, bool hit, int points, int totalScore,
                        float sessionMaxAngle, int safetyAlerts, int balancePauses)
    {
        if (writer == null) return;
        var c = CultureInfo.InvariantCulture;
        writer.WriteLine(string.Join(",",
            sessionId, patientId, operatedLeg, support ? "1" : "0", kick.ToString(c),
            DateTime.UtcNow.ToString("o", c), zone,
            windUpPercent.ToString("F2", c), windUpTarget.ToString("F1", c),
            maxFlexion.ToString("F1", c), minExtension.ToString("F1", c),
            windUpReached ? "1" : "0", fullExtension ? "1" : "0", hit ? "1" : "0",
            points.ToString(c), totalScore.ToString(c), sessionMaxAngle.ToString("F1", c),
            safetyAlerts.ToString(c), balancePauses.ToString(c)));
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
