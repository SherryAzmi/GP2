using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[Serializable]
public class LeaderboardEntry
{
    public string patient;
    public int score;
    public int cleared, waves;
    public float bestAngle;
    public string mode;   // Left, Right or Both
    public string date;   // ISO 8601, UTC
}

/// <summary>
/// All-time high scores, kept in a small JSON file next to the session CSVs:
/// [persistentDataPath]/leaderboard.json. Ranked by total score of a session.
/// </summary>
public static class Leaderboard
{
    [Serializable]
    class Store { public List<LeaderboardEntry> entries = new List<LeaderboardEntry>(); }

    const int MaxEntries = 100;
    static string FilePath => Path.Combine(Application.persistentDataPath, "leaderboard.json");

    public static List<LeaderboardEntry> Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var store = JsonUtility.FromJson<Store>(File.ReadAllText(FilePath));
                if (store?.entries != null) return store.entries;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Leaderboard] Could not read {FilePath}: {e.Message}");
        }
        return new List<LeaderboardEntry>();
    }

    /// <summary>Saves the entry and returns the sorted list and the entry's 1-based rank.</summary>
    public static List<LeaderboardEntry> Add(LeaderboardEntry entry, out int rank)
    {
        var list = Load();
        list.Add(entry);
        list.Sort((a, b) => b.score != a.score ? b.score.CompareTo(a.score) : string.CompareOrdinal(a.date, b.date));
        if (list.Count > MaxEntries) list.RemoveRange(MaxEntries, list.Count - MaxEntries);
        rank = list.IndexOf(entry) + 1; // 0 + 1 = not on the board any more
        try
        {
            File.WriteAllText(FilePath, JsonUtility.ToJson(new Store { entries = list }, true));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Leaderboard] Could not save {FilePath}: {e.Message}");
        }
        return list;
    }
}
