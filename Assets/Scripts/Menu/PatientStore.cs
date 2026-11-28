using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum TrainingGame { Beach, Football }

/// <summary>One finished session, kept in the patient's history.</summary>
[System.Serializable]
public class SessionEntry
{
    public string date;       // ISO 8601, UTC
    public string game;       // "Beach" or "Football"
    public int stage;
    public int score;
    public int percent;       // waves cleared / goals scored, 0..100
    public bool passed;
    public float bestAngle;
    // Added later; older records read these as 0 / empty.
    public float durationSeconds;
    public float avgAngle;     // average peak knee bend per wave / kick
    public int safetyAlerts;
    public string leg;         // "Left", "Right" or "Both"
    public float bestLeft;     // best bend of each leg in this session (0 = not trained)
    public float bestRight;
    // What was really run. Older records read these as false / 0 / empty.
    public bool stoppedEarly;   // ended before all waves / kicks were done
    public bool easier;         // therapist made values easier than the stage, so it cannot unlock
    public int planned, done;   // waves / kicks
    public int balancePauses;
    public string settings;     // targets, holds, movement or support, as used
    public bool hasExtension;   // bestExtension was measured (0 is a real value: a straight knee)
    public float bestExtension; // lowest knee angle reached, degrees
    public bool hasPain;
    public int pain;            // 0..10, entered by the therapist
    public string note;
    public string angleFile;     // full knee-angle recording, in Sessions/
}

/// <summary>Extra numbers a game reports when a session ends.</summary>
public struct SessionExtras
{
    public float durationSeconds, avgAngle, bestLeft, bestRight;
    public int safetyAlerts;
    public string leg;
    public bool stoppedEarly, easier;
    public int planned, done, balancePauses;
    public string settings;
    public bool hasExtension;
    public float bestExtension;
}

/// <summary>A patient's progress in one game: highest stage they may play, sessions and best results.</summary>
[System.Serializable]
public class GameProgress
{
    public int unlockedStage = 1;
    public int sessions;
    public int bestScore;
    public int bestPercent;
}

[System.Serializable]
public class PatientRecord
{
    public string id;
    public string name;
    public int age;
    public string operatedLeg = "Right";
    [Tooltip("Largest knee bend this patient can comfortably reach; stage targets scale with it")]
    public float comfortableMaxAngle = 90f;
    public string notes = "";
    public float safetyLimit;    // 0 = not set, use the default
    public float extensionGoal;  // straightest angle to ask for; 0 = fully straight
    public bool sprayAllowed;    // water spray on a beach splash; off until a clinician allows it (healed wound)

    public const float DefaultSafetyLimit = 110f;
    public float SafetyLimit => safetyLimit > 0f ? Mathf.Clamp(safetyLimit, 40f, DefaultSafetyLimit) : DefaultSafetyLimit;
    /// <summary>Stage straighten targets, never straighter than this patient's goal.</summary>
    public float StraightenHardest(int stage) => Mathf.Max(Stages.BeachStraightenHardest(stage), extensionGoal);
    public float StraightenEasiest(int stage) => Mathf.Max(Stages.BeachStraightenEasiest(stage), StraightenHardest(stage) + 5f);

    /// <summary>Removes one session and recounts the totals; unlocked stages stay as they are.</summary>
    public void RemoveSession(SessionEntry entry)
    {
        if (!history.Remove(entry)) return;
        if (!string.IsNullOrEmpty(entry.angleFile)) PatientStore.DeleteLogs(entry.angleFile);
        foreach (var game in new[] { TrainingGame.Beach, TrainingGame.Football })
        {
            var progress = Progress(game);
            progress.sessions = progress.bestScore = progress.bestPercent = 0;
            foreach (var e in history)
            {
                if (e.game != game.ToString()) continue;
                progress.sessions++;
                if (e.stoppedEarly) continue;
                progress.bestScore = Mathf.Max(progress.bestScore, e.score);
                progress.bestPercent = Mathf.Max(progress.bestPercent, e.percent);
            }
        }
    }
    public string surgeryType = "";
    public string surgeryDate = "";   // as typed, e.g. 2026-10-02 or 2/10/2026
    public string therapist = "";
    public string phone = "";
    public float heightCm;
    public float weightKg;
    public string created;
    public string lastVisit;
    public GameProgress beach = new GameProgress();
    public GameProgress football = new GameProgress();
    public List<SessionEntry> history = new List<SessionEntry>();

    public GameProgress Progress(TrainingGame g) => g == TrainingGame.Beach ? beach : football;
    public bool IsLeftLeg => operatedLeg == "Left";

    /// <summary>Whole weeks since surgery, or -1 when the date is missing or not readable.</summary>
    public int WeeksSinceSurgery
    {
        get
        {
            if (string.IsNullOrWhiteSpace(surgeryDate)) return -1;
            string[] formats = { "yyyy-MM-dd", "d/M/yyyy", "d-M-yyyy", "d.M.yyyy", "d MMM yyyy", "d MMMM yyyy" };
            if (!System.DateTime.TryParseExact(surgeryDate.Trim(), formats, System.Globalization.CultureInfo.InvariantCulture,
                                               System.Globalization.DateTimeStyles.None, out var d)) return -1;
            int days = (System.DateTime.Today - d.Date).Days;
            return days < 0 ? -1 : days / 7;
        }
    }

    public float TotalMinutes
    {
        get
        {
            float s = 0f;
            foreach (var e in history) s += e.durationSeconds;
            return s / 60f;
        }
    }
}

/// <summary>
/// Training stages. Each game has five, from a gentle warm-up to the hardest settings. A patient starts at stage 1;
/// finishing a session at their highest unlocked stage with at least <see cref="PassFraction"/> success
/// (waves cleared / goals scored) unlocks the next one. Earlier stages can always be replayed.
/// Bend targets scale with the patient's own comfortable maximum, so stage 5 is hard for everyone, never unsafe.
/// </summary>
public static class Stages
{
    public const int Count = 5;
    public const float PassFraction = 0.7f;

    static readonly string[] Names = { "Warm-up", "Building", "Steady", "Strong", "Champion" };
    public static string Name(int stage) => $"Stage {stage} · {Names[Mathf.Clamp(stage, 1, Count) - 1]}";

    static int I(int stage) => Mathf.Clamp(stage, 1, Count) - 1;

    // ---- Beach waves (seated) ----
    public static int BeachWaves(int s) => new[] { 6, 8, 10, 12, 14 }[I(s)];
    public static float BeachStartHold(int s) => new[] { 1.5f, 2f, 2.5f, 3f, 3.5f }[I(s)];
    public static float BeachMaxHold(int s) => new[] { 3f, 3.5f, 4f, 5f, 6f }[I(s)];
    public static float BeachBendMin(int s, float comfortableMax) => new[] { 0.25f, 0.3f, 0.35f, 0.4f, 0.45f }[I(s)] * comfortableMax;
    public static float BeachBendMax(int s, float comfortableMax) => new[] { 0.6f, 0.7f, 0.8f, 0.9f, 1f }[I(s)] * comfortableMax;
    public static float BeachStraightenEasiest(int s) => new[] { 60f, 50f, 40f, 30f, 25f }[I(s)];
    public static float BeachStraightenHardest(int s) => new[] { 25f, 18f, 12f, 6f, 0f }[I(s)];

    // ---- Penalty practice (standing) ----
    public static int Kicks(int s) => new[] { 6, 8, 10, 12, 14 }[I(s)];
    public static float KickStartPercent(int s) => new[] { 0.3f, 0.35f, 0.4f, 0.45f, 0.5f }[I(s)];
    public static float KickMinPercent(int s) => new[] { 0.25f, 0.3f, 0.3f, 0.35f, 0.4f }[I(s)];
    public static float KickMaxPercent(int s) => new[] { 0.55f, 0.65f, 0.75f, 0.85f, 0.95f }[I(s)];
    public static float KickHoldSeconds(int s) => new[] { 0.3f, 0.5f, 0.7f, 0.9f, 1.2f }[I(s)];
    public static float KickAttemptSeconds(int s) => new[] { 15f, 13f, 12f, 10f, 9f }[I(s)];

    public static string Describe(TrainingGame g, int s) => g == TrainingGame.Beach
        ? $"{BeachWaves(s)} waves  ·  hold {BeachStartHold(s):0.#}–{BeachMaxHold(s):0.#} s  ·  bend up to {BeachBendMax(s, 100f):0}% of max"
        : $"{Kicks(s)} kicks  ·  hold {KickHoldSeconds(s):0.#} s  ·  wind-up up to {KickMaxPercent(s) * 100f:0}% of max";
}

/// <summary>Saves and loads patient records as JSON in the app's persistent data folder.</summary>
public static class PatientStore
{
    [System.Serializable] class Root { public List<PatientRecord> patients = new List<PatientRecord>(); }

    static Root data;
    static string FilePath => Path.Combine(Application.persistentDataPath, "patients.json");

    static Root Data
    {
        get
        {
            if (data != null) return data;
            data = Read(FilePath);
            if (data == null && File.Exists(FilePath))
            {
                // Damaged file: keep a copy for recovery before anything is saved over it, then try the backup.
                File.Copy(FilePath, FilePath + $".damaged-{System.DateTime.Now:yyyyMMdd_HHmmss}", true);
                data = Read(FilePath + ".bak");
            }
            if (data == null) data = new Root();
            return data;
        }
    }

    static Root Read(string path)
    {
        try
        {
            return File.Exists(path) ? JsonUtility.FromJson<Root>(File.ReadAllText(path)) : null;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[PatientStore] Could not read {path}: {e.Message}");
            return null;
        }
    }

    public static IReadOnlyList<PatientRecord> All => Data.patients;

    public static PatientRecord Get(string id) => Data.patients.Find(p => p.id == id);

    public static void Delete(PatientRecord p)
    {
        if (!Data.patients.Remove(p)) return;
        Save();
        // The backup would still hold the deleted patient, so replace it too.
        File.Copy(FilePath, FilePath + ".bak", true);
        DeleteLogs($"*_{p.id}_*.csv");
        Leaderboard.Remove(p.id);
    }

    /// <summary>Removes recordings from Sessions/ (a file name or a pattern).</summary>
    public static void DeleteLogs(string pattern)
    {
        try
        {
            string folder = Path.Combine(Application.persistentDataPath, "Sessions");
            if (!Directory.Exists(folder)) return;
            foreach (var file in Directory.GetFiles(folder, pattern)) File.Delete(file);
        }
        catch (IOException e)
        {
            Debug.LogWarning($"[PatientStore] Could not delete {pattern}: {e.Message}");
        }
    }

    public static PatientRecord Create(string name)
    {
        var p = new PatientRecord
        {
            id = System.Guid.NewGuid().ToString("N").Substring(0, 8),
            name = name,
            age = 0,
            created = System.DateTime.UtcNow.ToString("o"),
        };
        Data.patients.Add(p);
        Save();
        return p;
    }

    public static void Save()
    {
        try
        {
            // Write beside the file first, so a crash half-way never leaves a broken patient list.
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonUtility.ToJson(Data, true));
            if (File.Exists(FilePath)) File.Replace(temp, FilePath, FilePath + ".bak");
            else File.Move(temp, FilePath);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[PatientStore] Could not save {FilePath}: {e.Message}");
        }
    }

    /// <summary>
    /// Stores a finished session. Returns true when it unlocked a new stage
    /// (it was played at the highest unlocked stage and reached <see cref="Stages.PassFraction"/>).
    /// </summary>
    public static bool RecordSession(string id, TrainingGame game, int stage, int score, float fraction, float bestAngle, out bool passed)
        => RecordSession(id, game, stage, score, fraction, bestAngle, default, out passed);

    public static bool RecordSession(string id, TrainingGame game, int stage, int score, float fraction, float bestAngle,
                                     SessionExtras extras, out bool passed)
    {
        passed = !extras.stoppedEarly && !extras.easier && fraction >= Stages.PassFraction;
        var p = Get(id);
        if (p == null) return false;
        var prog = p.Progress(game);
        int percent = Mathf.RoundToInt(fraction * 100f);
        prog.sessions++;
        if (!extras.stoppedEarly)
        {
            prog.bestScore = Mathf.Max(prog.bestScore, score);
            prog.bestPercent = Mathf.Max(prog.bestPercent, percent);
        }
        bool unlocked = passed && stage >= prog.unlockedStage && prog.unlockedStage < Stages.Count;
        if (unlocked) prog.unlockedStage = stage + 1;
        p.lastVisit = System.DateTime.UtcNow.ToString("o");
        p.history.Add(new SessionEntry
        {
            date = p.lastVisit, game = game.ToString(), stage = stage, score = score,
            percent = percent, passed = passed, bestAngle = bestAngle,
            durationSeconds = extras.durationSeconds, avgAngle = extras.avgAngle, safetyAlerts = extras.safetyAlerts,
            leg = extras.leg ?? p.operatedLeg, bestLeft = extras.bestLeft, bestRight = extras.bestRight,
            stoppedEarly = extras.stoppedEarly, easier = extras.easier, planned = extras.planned, done = extras.done,
            balancePauses = extras.balancePauses, settings = extras.settings,
            hasExtension = extras.hasExtension, bestExtension = extras.bestExtension,
            angleFile = TherapistMonitor.CurrentFile,
        });
        Save();
        CloudSync.Push(p); // only when cloud.json exists
        return unlocked;
    }
}

/// <summary>What the start menu asked for; read by the game scene when it starts.</summary>
public static class SessionConfig
{
    public const string MenuScene = "MainMenu";

    public static bool Active;
    public static string PatientId;
    public static TrainingGame Game;
    public static int Stage = 1;
    public static WaveGameManager.Movement Movement = WaveGameManager.Movement.Bend;

    public static PatientRecord Patient => Active ? PatientStore.Get(PatientId) : null;

    /// <summary>Key for log files: the patient's id when launched from the menu, otherwise the typed name.</summary>
    public static string LogId(string typedName) => Active ? PatientId : typedName;

    public static void Launch(PatientRecord patient, TrainingGame game, int stage, WaveGameManager.Movement movement)
    {
        Active = true;
        PatientId = patient.id;
        Game = game;
        Stage = stage;
        Movement = movement;
        SceneManager.LoadScene(game == TrainingGame.Beach ? "BeachScene" : "TrainingGround");
    }

    /// <summary>Back to the start menu (when it is part of the build).</summary>
    public static void ReturnToMenu()
    {
        if (!Application.CanStreamedLevelBeLoaded(MenuScene)) return;
        Active = false;
        SceneManager.LoadScene(MenuScene);
    }
}
