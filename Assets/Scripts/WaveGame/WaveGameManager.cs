using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Wave-avoidance core loop (GP1 4.3.2): a wave runs up the beach toward the patient's feet and the patient
/// keeps them out of the water until it retreats. The therapist picks the movement: Bend pulls the heel back
/// (knee flexion: the angle must reach the target or more), Straighten lifts the foot up (knee extension: the angle
/// must come down to the target or less), Alternate switches every wave. Each movement has its own target range
/// and its own difficulty; bend targets never pass the safety limit.
///
/// Loop: wave comes in -> hold the target angle -> cleared (points) or splashed -> logged to CSV -> next wave.
/// </summary>
public class WaveGameManager : MonoBehaviour
{
    public enum Leg { Left, Right }
    /// <summary>Both = right leg first, then the left, with an instruction screen between them.</summary>
    public enum LegMode { Left, Right, Both }
    /// <summary>Bend = pull the heel back (flexion), Straighten = lift the foot up (extension), Alternate = both in turn.</summary>
    public enum Movement { Bend, Straighten, Alternate }

    [Header("Patient (defaults for the therapist panel)")]
    [Tooltip("Patient name; also used in the session CSV file name")]
    public string patientId = "P001";
    public Leg operatedLeg = Leg.Right;
    [Tooltip("Which leg(s) to train this session; Both trains the right leg, then the left")]
    public LegMode legMode = LegMode.Right;
    [Tooltip("Show the therapist setup panel before each session")]
    public bool useDoctorPanel = true;
    [Tooltip("ON: the setup and results panels float in the beach scene in front of the patient (for recording a video). " +
             "OFF: they are screen overlays on the therapist's monitor only")]
    public bool panelsInPatientView = false;
    [Tooltip("Which knee movement the waves train")]
    public Movement movement = Movement.Bend;
    [Tooltip("Bend: easiest target for this patient, in degrees")]
    public float minTargetAngle = 30f;
    [Tooltip("Bend: hardest target for this patient (their comfortable maximum), in degrees")]
    public float maxTargetAngle = 90f;
    [Tooltip("Straighten: easiest target, in degrees (a small lift of the foot)")]
    public float straightenEasiestAngle = 60f;
    [Tooltip("Straighten: hardest target, in degrees (0 = a fully straight leg)")]
    public float straightenHardestAngle = 5f;
    [Tooltip("Knee angle of a foot resting on the ground while seated; Straighten waves wash over a foot there")]
    public float restingKneeAngle = 90f;
    [Tooltip("Absolute safety limit (GP1 NFR-04). Never asked for; passing it raises an alert")]
    public float safetyCeilingAngle = 110f;

    [Header("Session")]
    public int wavesPerSession = 10;
    [Range(0f, 1f)] public float startTargetLevel = 0.3f;
    [Tooltip("Target change after each wave, as a share of the min-max range: up when cleared, down when missed")]
    public float targetLevelStep = 0.15f;
    public float startHoldSeconds = 2f;
    public float maxHoldSeconds = 4f;
    public float holdSecondsStep = 0.25f;
    [Tooltip("Part of the hold time the angle must stay on target to clear the wave")]
    [Range(0.5f, 1f)] public float clearFraction = 0.8f;
    [Tooltip("Degrees short of the target that still count as holding")]
    public float angleTolerance = 3f;

    [Header("Timing (seconds)")]
    public float approachSeconds = 4f;
    public float resultSeconds = 1.5f;
    public float retreatSeconds = 2f;
    public float restSeconds = 2.5f;
    [Tooltip("How long the 'now the right/left leg' screen shows before the leg starts by itself")]
    public float introSeconds = 10f;

    [Header("Scene references")]
    public JointAngleCalculator angleSource;
    [Tooltip("Camera knee sensor; until it sends readings the simulator moves the leg")]
    public CameraPoseJoints cameraSensor;
    public VirtualLegPose leftLeg;
    public VirtualLegPose rightLeg;
    public WaveHUD hud;
    [Tooltip("Material with the GP2/ShoreWave shader. Set: a realistic breaking wave is built in code. " +
             "Empty: the old water sheet (a copy of waveTemplate) slides in instead")]
    public Material waveMaterial;
    [Tooltip("Material with the GP2/GhostLeg shader for the dark guide leg that shows where to put the leg")]
    public Material ghostMaterial;
    public bool showGuideLeg = true;
    [Tooltip("Static scenery water to hide while the breaking wave is used (e.g. the permanent swash sheet at the feet)")]
    public GameObject[] hideWithShoreWave;
    [Tooltip("Water sheet that runs up the beach (a copy is made at runtime); only used without waveMaterial")]
    public Renderer waveTemplate;
    public Transform waveSpawn;
    [Tooltip("Sand height under the patient's feet")]
    public float feetSandHeight = 0.072f;
    [Tooltip("How deep the water looks at its front edge")]
    public float waterDepth = 0.05f;
    [Tooltip("Beach slope toward the sea (metres down per metre); the water's front edge follows it")]
    public float beachSlope = 0.08f;
    [Tooltip("How far in front of the heel the water stops when the patient is exactly on target")]
    public float waterGapInFrontOfHeel = 0.04f;
    public Transform splashTemplate;
    public Vector3 splashScale = new Vector3(100f, 100f, 100f);

    // Session state
    bool sessionRunning;
    int score, streak, cleared, safetyAlerts;
    float bendLevel, straightenLevel, holdSeconds, sessionMaxAngle, sessionMinAngle, heldSum, highestCleared;
    float lowestStraightenCleared;
    int wavesDone, bendWaves, bendCleared, straightenWaves, straightenCleared;
    readonly List<StageResult> results = new List<StageResult>();
    ResultsPanel resultsPanel;
    float currentTarget = -1f;
    Movement currentMovement = Movement.Bend;  // what the wave in progress asks for
    bool aboveCeiling;
    bool startRequested;
    DoctorPanel panel;
    JointAngleCalculator leftSource, rightSource;

    Transform wave;
    float waveEdgeOffset, waveTopOffset, feetZ;
    Mesh waveMesh;
    Vector3[] waveBase, waveShown;
    float waveFrontZ;
    bool waveActive;
    Transform splash;
    AudioSource audioSource;
    AudioClip clearClip, splashClip, alertClip, waveClip, backwashClip, crashClip;
    ShoreWave shoreWave;
    float sessionStartTime, peakSum;  // for the patient record: session length and average peak bend
    int peakCount;
    SessionLogger logger;
    bool recordOpen;  // a session has started and is not yet in the patient's history
    SessionControls controls;
    TherapistMonitor monitor;

    LiveReading Live() => new LiveReading
    {
        angle = Angle, target = currentTarget, limit = safetyCeilingAngle, alerts = safetyAlerts,
        cameraConnected = cameraSensor && cameraSensor.Connected, cameraLost = SensorLost,
        progress = $"{operatedLeg} leg  ·  waves done {wavesDone} of {wavesPerSession}",
    };

    VirtualLegPose OperatedLeg => operatedLeg == Leg.Left ? leftLeg : rightLeg;
    float Angle => angleSource ? angleSource.CurrentKneeAngle : 0f;
    bool SensorLost => cameraSensor && cameraSensor.TrackingLost;

    IEnumerator Start()
    {
        if (leftLeg) leftSource = leftLeg.angleSource;
        if (rightLeg) rightSource = rightLeg.angleSource;
        ApplyOperatedLeg();

        CreateWave();
        if (splashTemplate)
        {
            splash = Instantiate(splashTemplate, transform);
            splash.name = "GameSplash";
            splash.localScale = Vector3.zero;
        }
        SetupAudio();
        // Started from the start menu: the patient's record and stage fill in the therapist panel;
        // the therapist can still change anything there before pressing Start.
        ApplySessionConfig();
        if (useDoctorPanel)
        {
            panel = DoctorPanel.Create(this);
            resultsPanel = ResultsPanel.Create(panelsInPatientView);
        }

        while (true)
        {
            yield return WaitForStartPress();
            yield return RunSession();
        }
    }

    /// <summary>
    /// Applies the patient and stage chosen in the start menu (if the session came from there).
    /// Target ranges scale with the patient's comfortable maximum; the safety limit still caps them.
    /// </summary>
    bool ApplySessionConfig()
    {
        var p = SessionConfig.Game == TrainingGame.Beach ? SessionConfig.Patient : null;
        if (p == null) return false;
        int s = SessionConfig.Stage;
        safetyCeilingAngle = p.SafetyLimit;
        float top = safetyCeilingAngle - 5f;
        patientId = p.name;
        legMode = p.IsLeftLeg ? LegMode.Left : LegMode.Right;
        operatedLeg = p.IsLeftLeg ? Leg.Left : Leg.Right;
        movement = SessionConfig.Movement;
        maxTargetAngle = Mathf.Min(top, Stages.BeachBendMax(s, p.comfortableMaxAngle));
        minTargetAngle = Mathf.Min(maxTargetAngle - 5f, Stages.BeachBendMin(s, p.comfortableMaxAngle));
        straightenEasiestAngle = p.StraightenEasiest(s);
        straightenHardestAngle = p.StraightenHardest(s);
        wavesPerSession = Stages.BeachWaves(s);
        startHoldSeconds = Stages.BeachStartHold(s);
        maxHoldSeconds = Stages.BeachMaxHold(s);
        return true;
    }

    /// <summary>Called by the therapist panel's Start button.</summary>
    public void ApplyDoctorSettings(string patientName, LegMode mode, Movement move, float minAngle, float maxAngle,
                                    float straightenEasiest, float straightenHardest, int waves, float startHold, float maxHold)
    {
        patientId = patientName;
        legMode = mode;
        movement = move;
        minTargetAngle = minAngle;
        maxTargetAngle = maxAngle;
        straightenEasiestAngle = straightenEasiest;
        straightenHardestAngle = straightenHardest;
        wavesPerSession = waves;
        startHoldSeconds = startHold;
        maxHoldSeconds = maxHold;
    }

    // Only the operated leg follows the sensor; the other one rests in a relaxed pose.
    void ApplyOperatedLeg()
    {
        if (leftLeg) leftLeg.angleSource = operatedLeg == Leg.Left ? leftSource : null;
        if (rightLeg) rightLeg.angleSource = operatedLeg == Leg.Right ? rightSource : null;
        if (cameraSensor) cameraSensor.useLeftLeg = operatedLeg == Leg.Left;
    }

    void SwitchToLeg(Leg leg)
    {
        operatedLeg = leg;
        ApplyOperatedLeg();
        if (OperatedLeg) feetZ = OperatedLeg.HeelPosition.z;
    }

    void Update()
    {
        if (RehabInput.MenuPressed)
        {
            SaveStoppedSession();
            SessionConfig.ReturnToMenu();
        }
        if (!hud) return;
        float angle = Angle;
        hud.SetAngle(angle, currentTarget, safetyCeilingAngle, currentMovement == Movement.Straighten);
        UpdateGuideLeg(angle);

        if (sessionRunning)
        {
            sessionMaxAngle = Mathf.Max(sessionMaxAngle, angle);
            sessionMinAngle = Mathf.Min(sessionMinAngle, angle);
        }

        bool over = angle > safetyCeilingAngle;
        if (over && !aboveCeiling)
        {
            Play(alertClip);
            if (sessionRunning) safetyAlerts++;
        }
        aboveCeiling = over;
        hud.SetWarning(over ? "Too far! Ease your knee back gently"
                     : SensorLost ? "The camera can't see your leg" : null);
    }

    void LateUpdate() => RippleWave();

    /// <summary>The dark guide leg shows the target pose whenever there is a target; it turns green when matched.</summary>
    void UpdateGuideLeg(float angle)
    {
        var other = operatedLeg == Leg.Left ? rightLeg : leftLeg;
        if (other) other.HideGhost();
        if (!OperatedLeg) return;
        if (!showGuideLeg || currentTarget < 0f)
        {
            OperatedLeg.HideGhost();
            return;
        }
        bool straighten = currentMovement == Movement.Straighten;
        bool matched = angle <= safetyCeilingAngle &&
                       (straighten ? angle <= currentTarget + angleTolerance : angle >= currentTarget - angleTolerance);
        OperatedLeg.ShowGhost(currentTarget, matched, ghostMaterial, feetSandHeight);
    }

    void OnDestroy() => logger?.Dispose();

    void OnApplicationQuit() => SaveStoppedSession();

    /// <summary>Therapist's STOP button: keep what was done, then go back to the menu (or restart the scene).</summary>
    public void StopSession()
    {
        SaveStoppedSession();
        logger?.Dispose();
        logger = null;
        if (Application.CanStreamedLevelBeLoaded(SessionConfig.MenuScene)) SessionConfig.ReturnToMenu();
        else UnityEngine.SceneManagement.SceneManager.LoadScene(gameObject.scene.name);
    }

    /// <summary>A session ended early (menu or quit) still goes into the history, marked as stopped.</summary>
    void SaveStoppedSession()
    {
        if (!recordOpen || !SessionConfig.Active || SessionConfig.Game != TrainingGame.Beach) return;
        recordOpen = false;
        var extras = SessionExtrasNow();
        int done = 0, cleared_ = 0, alerts = 0, total = 0;
        float best = 0f;
        void Add(Leg leg, int waves, int ok, int warnings, int points, float bend)
        {
            done += waves; cleared_ += ok; alerts += warnings; total += points;
            best = Mathf.Max(best, bend);
            if (leg == Leg.Left) extras.bestLeft = Mathf.Max(extras.bestLeft, bend);
            else extras.bestRight = Mathf.Max(extras.bestRight, bend);
        }
        foreach (var r in results) Add(r.leg, r.waves, r.cleared, r.safetyAlerts, r.score, r.bestAngle);
        if (logger != null) Add(operatedLeg, wavesDone, cleared, safetyAlerts, score, sessionMaxAngle); // leg in progress
        AddExtension(ref extras);
        extras.stoppedEarly = done < extras.planned;
        extras.done = done;
        extras.safetyAlerts = alerts;
        PatientStore.RecordSession(SessionConfig.PatientId, TrainingGame.Beach, SessionConfig.Stage, total,
                                   done > 0 ? (float)cleared_ / done : 0f, best, extras, out _);
    }

    /// <summary>Straightest knee of the session, taken from the operated leg when both were trained.</summary>
    void AddExtension(ref SessionExtras extras)
    {
        var p = SessionConfig.Patient;
        Leg wanted = p != null && p.IsLeftLeg ? Leg.Left : Leg.Right;
        foreach (var r in results)
        {
            if (legMode == LegMode.Both && r.leg != wanted) continue;
            extras.hasExtension = true;
            extras.bestExtension = r.bestStraighten;
        }
        if (logger != null && wavesDone > 0 && (legMode != LegMode.Both || operatedLeg == wanted))
        {
            extras.hasExtension = true;
            extras.bestExtension = sessionMinAngle;
        }
    }

    SessionExtras SessionExtrasNow()
    {
        var c = System.Globalization.CultureInfo.InvariantCulture;
        return new SessionExtras
        {
            durationSeconds = Time.time - sessionStartTime,
            avgAngle = peakCount > 0 ? peakSum / peakCount : 0f,
            leg = legMode.ToString(),
            planned = wavesPerSession * (legMode == LegMode.Both ? 2 : 1),
            easier = IsEasierThanStage(),
            settings = string.Format(c, "{0}; bend {1:0}-{2:0} deg; straighten {3:0}-{4:0} deg; hold {5:0.#}-{6:0.#} s",
                                     movement, minTargetAngle, maxTargetAngle, straightenEasiestAngle,
                                     straightenHardestAngle, startHoldSeconds, maxHoldSeconds),
        };
    }

    /// <summary>True when the therapist lowered anything below the chosen stage, so it must not unlock the next one.</summary>
    bool IsEasierThanStage()
    {
        var p = SessionConfig.Patient;
        if (p == null) return false;
        int s = SessionConfig.Stage;
        float top = safetyCeilingAngle - 5f;
        float stageMax = Mathf.Min(top, Stages.BeachBendMax(s, p.comfortableMaxAngle));
        float stageMin = Mathf.Min(stageMax - 5f, Stages.BeachBendMin(s, p.comfortableMaxAngle));
        const float slack = 0.5f; // the setup panel shows one decimal
        bool bend = movement != Movement.Straighten &&
                    (maxTargetAngle < stageMax - slack || minTargetAngle < stageMin - slack);
        bool straighten = movement != Movement.Bend &&
                          (straightenEasiestAngle > p.StraightenEasiest(s) + slack ||
                           straightenHardestAngle > p.StraightenHardest(s) + slack);
        return bend || straighten || wavesPerSession < Stages.BeachWaves(s) ||
               startHoldSeconds < Stages.BeachStartHold(s) - 0.05f || maxHoldSeconds < Stages.BeachMaxHold(s) - 0.05f;
    }

    // ---------------- Session flow ----------------

    IEnumerator WaitForStartPress()
    {
        currentTarget = -1f;
        hud.SetHold(-1f);
        startRequested = false;
        if (panel)
        {
            hud.SetStatus("Therapist is setting up\nthe session...", WaveHUD.Neutral);
            hud.SetInfo("");
            ApplySessionConfig(); // pre-fill from the patient and stage (the stage may have moved up)
            panel.Show(SessionConfig.Active && SessionConfig.Game == TrainingGame.Beach
                ? $"{patientId}   ·   {Stages.Name(SessionConfig.Stage)}   ·   values from the stage, change any you need" : null);
            // A panel floating in the scene would overlap the patient HUD, so hide the HUD meanwhile.
            if (panelsInPatientView) hud.gameObject.SetActive(false);
            // The panel's Start button is the only way to begin (Space would type into its fields).
            while (!startRequested) yield return null;
            startRequested = false;
            hud.gameObject.SetActive(true);
            yield break;
        }
        ApplySessionConfig(); // the stage may have moved up after the last session
        hud.SetStatus("Press  A  or  Space  to start", WaveHUD.Neutral);
        string stageText = SessionConfig.Active ? $"  ·  {Stages.Name(SessionConfig.Stage)}" : "";
        hud.SetInfo($"Patient {patientId}  ·  {legMode} knee  ·  {movement}{stageText}");
        hud.SetScore(-1, 0);
        if (!hud.ResultsShowing)
            hud.ShowTitleCard($"{patientId}  ·  {legMode} knee  ·  {movement}{stageText}\n{wavesPerSession} waves  ·  keep your feet dry!",
                              "PRESS  A  TO  START");
        yield return null;
        while (!StartPressed()) yield return null;
        UISound.Click();
        hud.HideTitleCard();
        hud.HideResults();
    }

    /// <summary>One whole session: the chosen leg, or right then left when training both.</summary>
    IEnumerator RunSession()
    {
        Leg[] stages = legMode == LegMode.Both ? new[] { Leg.Right, Leg.Left }
                     : new[] { legMode == LegMode.Left ? Leg.Left : Leg.Right };
        results.Clear();
        sessionStartTime = Time.time;
        peakSum = 0f;
        peakCount = 0;
        recordOpen = true;
        if (!controls) controls = SessionControls.Create(transform, StopSession);
        controls.SetVisible(true);
        if (!monitor) monitor = TherapistMonitor.Create(transform, Live);
        monitor.SetVisible(true);

        for (int s = 0; s < stages.Length; s++)
        {
            SwitchToLeg(stages[s]);
            if (stages.Length > 1) yield return ShowStageIntro(s, stages);
            yield return RunLeg();
            if (stages.Length > 1 && s < stages.Length - 1)
            {
                hud.SetStatus($"{stages[s]} leg done!\nWell done — take a short rest", WaveHUD.Good);
                hud.SetInfo("");
                yield return new WaitForSeconds(3f);
            }
        }

        hud.SetStatus("Session complete!", WaveHUD.Accent);
        hud.SetInfo(legMode == LegMode.Both ? $"Both legs done  ·  Total score {TotalScore()}" : $"Score {results[0].score}");
        int totalCleared = 0, totalWaves = 0;
        float best = 0f;
        foreach (var r in results) { totalCleared += r.cleared; totalWaves += r.waves; best = Mathf.Max(best, r.bestAngle); }
        var entry = new LeaderboardEntry
        {
            patient = patientId, patientId = SessionConfig.Active ? SessionConfig.PatientId : "", score = TotalScore(), cleared = totalCleared, waves = totalWaves,
            bestAngle = best, mode = legMode.ToString(), date = System.DateTime.UtcNow.ToString("o"),
        };
        var board = Leaderboard.Add(entry, out int rank);
        float fraction = totalWaves > 0 ? (float)totalCleared / totalWaves : 0f;
        int alerts = 0;
        foreach (var r in results) alerts += r.safetyAlerts;

        // Started from the menu: save to the patient's record and maybe unlock the next stage.
        string prompt = "PRESS  A  TO  PLAY AGAIN";
        var stageRow = ("Stage", "-", Color.white);
        bool unlocked = false;
        int played = SessionConfig.Stage;
        if (SessionConfig.Active && SessionConfig.Game == TrainingGame.Beach)
        {
            var extras = SessionExtrasNow();
            extras.safetyAlerts = alerts;
            extras.done = totalWaves;
            AddExtension(ref extras);
            recordOpen = false;
            foreach (var r in results)
                if (r.leg == Leg.Left) extras.bestLeft = Mathf.Max(extras.bestLeft, r.bestAngle);
                else extras.bestRight = Mathf.Max(extras.bestRight, r.bestAngle);
            unlocked = PatientStore.RecordSession(SessionConfig.PatientId, TrainingGame.Beach, played, TotalScore(),
                                                  fraction, best, extras, out bool passed);
            stageRow = (Stages.Name(played), passed ? "PASSED" : extras.easier ? "easier settings" : $"need {Stages.PassFraction * 100f:0}%", passed ? WaveHUD.Good : WaveHUD.Accent);
            if (unlocked) SessionConfig.Stage = played + 1;
            prompt = $"A: PLAY STAGE {SessionConfig.Stage}   ·   ESC: MENU";
        }

        controls.SetVisible(false);
        monitor.SetVisible(false);
        hud.Popup("SESSION DONE!", $"{totalCleared} / {totalWaves} waves cleared", WaveHUD.Accent, 2.2f);
        yield return new WaitForSeconds(2.5f);
        if (unlocked)
        {
            UISound.Confirm();
            hud.Popup($"STAGE {played + 1} UNLOCKED!", Stages.Name(played + 1), WaveHUD.Accent, 2.6f);
            yield return new WaitForSeconds(2.8f);
        }

        if (resultsPanel)
        {
            // Therapist's results screen; its New session button brings back the setup panel.
            resultsPanel.Show(patientId, results, minTargetAngle, maxTargetAngle, board, entry, rank);
            if (panelsInPatientView) hud.gameObject.SetActive(false);
            while (resultsPanel.IsOpen) yield return null;
            hud.gameObject.SetActive(true);
        }
        else
        {
            // No therapist panel: show the patient their own results card; A / Space starts the next session.
            int stars = fraction >= 0.8f ? 3 : fraction >= 0.5f ? 2 : totalCleared > 0 ? 1 : 0;
            hud.ShowResults("SESSION RESULTS", stars,
                stars == 3 ? "Amazing - you beat the waves!" : stars == 2 ? "Great work - nearly dry!" : "Good effort - keep practising",
                stars >= 2 ? WaveHUD.Good : WaveHUD.Accent, new[]
                {
                    ("Score", TotalScore().ToString(), Color.white),
                    ("Waves cleared", $"{totalCleared} / {totalWaves}", Color.white),
                    ("Best knee bend", $"{best:0}°", Color.white),
                    stageRow.Item2 != "-" ? stageRow : ("Leaderboard rank", rank > 0 ? $"#{rank}" : "-", WaveHUD.Accent),
                    ("Safety alerts", alerts.ToString(), alerts > 0 ? WaveHUD.Bad : WaveHUD.Good),
                }, prompt);
        }
    }

    int TotalScore()
    {
        int total = 0;
        foreach (var r in results) total += r.score;
        return total;
    }

    /// <summary>
    /// Tells the patient which leg is next. Starts when they press A / Space, or by itself after
    /// <see cref="introSeconds"/> so the session never looks stuck.
    /// </summary>
    IEnumerator ShowStageIntro(int index, Leg[] stages)
    {
        string which = stages[index].ToString().ToUpper();
        string order = index == 0
            ? $"then the {stages[1].ToString().ToLower()} leg"
            : "rest the other leg";
        hud.SetHold(-1f);
        currentTarget = -1f;
        startRequested = false;
        yield return null;
        hud.SetWarning(null);

        for (float left = introSeconds; left > 0f; left -= Time.deltaTime)
        {
            hud.SetStatus($"Part {index + 1} of {stages.Length}:  {which} LEG\n{order}", WaveHUD.Accent);
            hud.SetInfo($"Press A / Space when ready  ·  starting in {Mathf.CeilToInt(left)}");
            if (StartPressed())
            {
                UISound.Click();
                yield break;
            }
            yield return null;
        }
    }

    IEnumerator RunLeg()
    {
        sessionRunning = true;
        score = streak = cleared = safetyAlerts = wavesDone = 0;
        bendWaves = bendCleared = straightenWaves = straightenCleared = 0;
        heldSum = highestCleared = lowestStraightenCleared = 0f;
        sessionMaxAngle = sessionMinAngle = Angle;
        bendLevel = straightenLevel = startTargetLevel;
        holdSeconds = startHoldSeconds;
        logger = new SessionLogger(SessionConfig.LogId(patientId), operatedLeg.ToString());
        Debug.Log($"[WaveGame] Logging session to {logger.FilePath}");

        hud.SetScore(0, 0);
        for (int n = 3; n > 0; n--)
        {
            hud.SetStatus($"Get ready...  {n}", WaveHUD.Neutral);
            hud.Popup(n.ToString(), "", WaveHUD.Accent, 0.9f);
            UISound.Tick();
            yield return new WaitForSeconds(1f);
        }
        hud.Popup("SURF'S UP!", "", WaveHUD.Good, 1.2f);
        UISound.Go();

        for (int i = 1; i <= wavesPerSession; i++)
        {
            yield return RunWave(i);
            if (i < wavesPerSession)
            {
                hud.SetStatus("Relax your leg", WaveHUD.Neutral);
                yield return new WaitForSeconds(restSeconds);
            }
        }

        sessionRunning = false;
        currentTarget = -1f;
        logger.Dispose();
        logger = null;

        results.Add(new StageResult
        {
            leg = operatedLeg, waves = wavesDone, cleared = cleared, score = score,
            safetyAlerts = safetyAlerts, bestAngle = sessionMaxAngle,
            highestClearedTarget = highestCleared, avgHeld = wavesDone > 0 ? heldSum / wavesDone : 0f,
            bendWaves = bendWaves, bendCleared = bendCleared, straightenWaves = straightenWaves,
            straightenCleared = straightenCleared, bestStraighten = sessionMinAngle,
            lowestStraightenCleared = lowestStraightenCleared,
        });
        hud.SetStatus($"{operatedLeg} leg finished!\nScore {score}  ·  cleared {cleared}/{wavesPerSession}", WaveHUD.Accent);
        hud.SetInfo(straightenWaves == 0 ? $"Best knee bend: {sessionMaxAngle:0}°"
                  : bendWaves == 0 ? $"Straightest knee: {sessionMinAngle:0}°"
                  : $"Best knee bend: {sessionMaxAngle:0}°  ·  straightest: {sessionMinAngle:0}°");
        yield return new WaitForSeconds(2f);
    }

    IEnumerator RunWave(int index)
    {
        currentMovement = movement != Movement.Alternate ? movement
                        : index % 2 == 1 ? Movement.Bend : Movement.Straighten;
        bool straighten = currentMovement == Movement.Straighten;
        string instruction = straighten ? "Lift your foot up" : "Pull your heel back";
        float target = TargetAngle(currentMovement);
        float levelUsed = straighten ? straightenLevel : bendLevel;
        float hold = holdSeconds;
        currentTarget = target;
        hud.SetInfo($"Wave {index}/{wavesPerSession}  ·  Score {score}  ·  Streak {streak}");
        hud.SetHold(-1f);

        // 1. Wave runs up the beach. Bend: the water stops just in front of where the heel is at the target angle.
        // Straighten: it washes over a foot resting on the sand, so only a lifted foot stays dry.
        float startEdge = waveSpawn ? waveSpawn.position.z : -15f;
        float reachEdge = straighten
            ? OperatedLeg.HeelPositionAt(restingKneeAngle).z + waterGapInFrontOfHeel
            : OperatedLeg.HeelPositionAt(target).z - waterGapInFrontOfHeel;
        hud.SetStatus($"Wave coming!\n{instruction}", WaveHUD.Accent);
        Play(waveClip);
        waveActive = true;
        if (shoreWave)
        {
            // Swell rolls in, curls, crashes about two metres in front of the patient, then the swash runs up.
            shoreWave.feetZ = feetZ;
            yield return shoreWave.Approach(startEdge, reachEdge, approachSeconds);
        }
        else
            for (float t = 0f; t < approachSeconds; t += Time.deltaTime)
            {
                float k = t / approachSeconds;
                // Fast surge at first, slowing as the water runs out of energy on the sand.
                float run = 1f - Mathf.Pow(1f - k, 3f);
                SetWave(Mathf.Lerp(startEdge, reachEdge, run));
                yield return null;
            }
        float lapTime = 0f;

        // 2. Hold: the angle must stay on target while the water sits at the feet.
        float heldTime = 0f, minAngle = float.MaxValue, maxAngle = float.MinValue;
        for (float t = 0f; t < hold; t += Time.deltaTime)
        {
            float angle = Angle;
            bool overLimit = angle > safetyCeilingAngle;
            // Going past the safety limit never counts as holding, and neither does a frozen angle while the
            // camera has lost the leg.
            bool onTarget = straighten ? angle <= target + angleTolerance : angle >= target - angleTolerance;
            bool holding = onTarget && !overLimit && !SensorLost;
            if (holding) heldTime += Time.deltaTime;
            minAngle = Mathf.Min(minAngle, angle);
            maxAngle = Mathf.Max(maxAngle, angle);

            hud.SetHold(t / hold, holding);
            if (overLimit) hud.SetStatus("Too far!\nEase back a little", WaveHUD.Bad);
            else if (holding) hud.SetStatus("Hold it!", WaveHUD.Good);
            else hud.SetStatus($"Feet in the water!\n{instruction}", WaveHUD.Bad);
            if (shoreWave) shoreWave.Hold(reachEdge, lapTime += Time.deltaTime);
            else SetWave(reachEdge + Mathf.Sin(t * 3f) * 0.03f);
            yield return null;
        }

        // 3. Result, scoring and difficulty.
        float fraction = hold > 0f ? heldTime / hold : 0f;
        if (maxAngle > float.MinValue)
        {
            peakSum += maxAngle; // the deepest bend reached during this wave
            peakCount++;
        }
        bool waveCleared = fraction >= clearFraction;
        wavesDone++;
        heldSum += fraction;
        if (straighten) straightenWaves++; else bendWaves++;
        if (waveCleared && straighten)
        {
            straightenCleared++;
            lowestStraightenCleared = lowestStraightenCleared > 0f ? Mathf.Min(lowestStraightenCleared, target) : target;
        }
        else if (waveCleared)
        {
            bendCleared++;
            highestCleared = Mathf.Max(highestCleared, target);
        }
        int points;
        if (waveCleared)
        {
            points = 100 + streak * 20;
            streak++;
            cleared++;
            ChangeLevel(currentMovement, +targetLevelStep);
            holdSeconds = Mathf.Min(maxHoldSeconds, holdSeconds + holdSecondsStep);
            hud.SetStatus($"Wave cleared!  +{points}", WaveHUD.Good);
            hud.Popup("WAVE CLEARED!", streak >= 2 ? $"+{points}   ·   {streak} in a row!" : $"+{points}", WaveHUD.Good, 1.8f);
            Play(clearClip);
        }
        else
        {
            points = Mathf.RoundToInt(fraction * 40f);
            streak = 0;
            ChangeLevel(currentMovement, -targetLevelStep);
            hud.SetStatus(points > 0 ? $"Splash!  +{points}" : "Splash!", WaveHUD.Bad);
            hud.Popup("SPLASH!", points > 0 ? $"+{points}  ·  keep it up!" : "Try again on the next wave", WaveHUD.Bad, 1.8f);
            Play(splashClip);
            if (splash) StartCoroutine(SplashAt(OperatedLeg.HeelPosition));
        }
        score += points;
        hud.SetScore(score, streak);
        hud.SetHold(-1f);
        hud.SetInfo($"Wave {index}/{wavesPerSession}  ·  Score {score}  ·  Streak {streak}");

        logger.LogWave(index, levelUsed, target, hold, minAngle, maxAngle, fraction, waveCleared,
                       points, score, sessionMaxAngle, safetyAlerts, currentMovement.ToString());

        // 4. Wave pulls back out to sea.
        currentTarget = -1f;
        currentMovement = Movement.Bend;
        if (shoreWave)
        {
            for (float t = 0f; t < resultSeconds; t += Time.deltaTime)
            {
                shoreWave.Hold(reachEdge, lapTime += Time.deltaTime);
                yield return null;
            }
            Play(backwashClip);
            yield return shoreWave.Retreat(reachEdge, Mathf.Max(startEdge, reachEdge - 3.5f), retreatSeconds);
            waveActive = false;
            yield break;
        }
        yield return new WaitForSeconds(resultSeconds);
        Play(backwashClip);
        for (float t = 0f; t < retreatSeconds; t += Time.deltaTime)
        {
            float k = t / retreatSeconds;
            SetWave(Mathf.Lerp(reachEdge, startEdge, k * k)); // backwash starts slow, then drains faster
            yield return null;
        }
        SetWave(startEdge);
        waveActive = false;
    }

    float TargetAngle(Movement move)
    {
        if (move == Movement.Straighten)
            return Mathf.Clamp(Mathf.Lerp(straightenEasiestAngle, straightenHardestAngle, straightenLevel), 0f, straightenEasiestAngle);
        float top = Mathf.Min(maxTargetAngle, safetyCeilingAngle - 5f);
        return Mathf.Clamp(Mathf.Lerp(minTargetAngle, top, bendLevel), 5f, top);
    }

    /// <summary>Harder (+) or easier (-) for one movement only, so alternating waves keep separate difficulties.</summary>
    void ChangeLevel(Movement move, float step)
    {
        if (move == Movement.Straighten) straightenLevel = Mathf.Clamp01(straightenLevel + step);
        else bendLevel = Mathf.Clamp01(bendLevel + step);
    }

    /// <summary>Starts a session from code, e.g. a therapist menu button.</summary>
    public void RequestStart() => startRequested = true;

    bool StartPressed()
    {
        if (startRequested)
        {
            startRequested = false;
            return true;
        }
        return RehabInput.StartPressed;
    }

    // ---------------- Wave visuals ----------------

    void CreateWave()
    {
        if (waveMaterial)
        {
            var sw = new GameObject("ShoreWave", typeof(MeshFilter), typeof(MeshRenderer));
            sw.transform.SetParent(transform, false);
            var r = sw.GetComponent<MeshRenderer>();
            r.sharedMaterial = waveMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            feetZ = OperatedLeg.HeelPosition.z;
            shoreWave = sw.AddComponent<ShoreWave>();
            shoreWave.feetSandHeight = feetSandHeight;
            shoreWave.beachSlope = beachSlope;
            shoreWave.feetZ = feetZ;
            shoreWave.OnBreak = () => Play(crashClip);
            if (hideWithShoreWave != null)
                foreach (var hidden in hideWithShoreWave) if (hidden) hidden.SetActive(false);
            return;
        }
        if (!waveTemplate) return;
        var go = new GameObject("GameWave", typeof(MeshFilter), typeof(MeshRenderer));
        wave = go.transform;
        wave.SetParent(transform, true);
        wave.SetPositionAndRotation(waveTemplate.transform.position, Quaternion.identity);
        waveMesh = BuildUpwardWaterMesh(waveTemplate);
        go.GetComponent<MeshFilter>().sharedMesh = waveMesh;
        go.GetComponent<MeshRenderer>().sharedMaterials = waveTemplate.sharedMaterials;

        // Front (shore-side) edge and top of the water, relative to its pivot.
        Bounds b = waveMesh.bounds;
        waveEdgeOffset = b.max.z;
        waveTopOffset = b.max.y;
        waveFrontZ = b.max.z;
        if (waveMesh.vertexCount <= 30000)
        {
            waveMesh.MarkDynamic();
            waveBase = waveMesh.vertices;
            waveShown = new Vector3[waveBase.Length];
        }
        feetZ = OperatedLeg.HeelPosition.z;
        SetWave(waveSpawn ? waveSpawn.position.z : -15f);
    }

    /// <summary>
    /// Copies the imported water into a plain world-aligned mesh with its faces pointing up.
    /// The Blender wave is a skinned mesh (its renderer bounds are a metre off) whose faces point down,
    /// so seen from the chair it was invisible.
    /// </summary>
    static Mesh BuildUpwardWaterMesh(Renderer template)
    {
        Transform t = template.transform;
        Mesh source;
        var skinned = template as SkinnedMeshRenderer;
        if (skinned)
        {
            source = new Mesh();
            skinned.BakeMesh(source, false); // unscaled local space; the transform is applied below
        }
        else source = template.GetComponent<MeshFilter>().sharedMesh;

        var vertices = source.vertices;
        for (int i = 0; i < vertices.Length; i++) vertices[i] = t.TransformPoint(vertices[i]) - t.position;

        var normals = source.normals;
        Vector3 average = Vector3.zero;
        for (int i = 0; i < normals.Length; i++)
        {
            normals[i] = t.rotation * normals[i];
            average += normals[i];
        }
        bool flip = average.y < 0f;

        var mesh = new Mesh { name = "GameWaveMesh" };
        if (vertices.Length > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = vertices;
        mesh.uv = source.uv;
        mesh.subMeshCount = source.subMeshCount;
        for (int s = 0; s < source.subMeshCount; s++)
        {
            var tris = source.GetTriangles(s);
            if (flip) for (int i = 0; i < tris.Length; i += 3) (tris[i + 1], tris[i + 2]) = (tris[i + 2], tris[i + 1]);
            mesh.SetTriangles(tris, s);
        }
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        if (skinned) Destroy(source);
        return mesh;
    }

    /// <summary>Moves the water so its front edge is at edgeZ, riding just above the sloping sand.</summary>
    void SetWave(float edgeZ)
    {
        if (!wave) return;
        float edgeTop = feetSandHeight + waterDepth - beachSlope * Mathf.Max(0f, feetZ - edgeZ);
        wave.position = new Vector3(wave.position.x, edgeTop - waveTopOffset, edgeZ - waveEdgeOffset);
    }

    /// <summary>
    /// Makes the water's leading edge irregular and alive: a wobbling, uneven front line
    /// (stronger the closer a vertex is to the edge) with a small swell behind it.
    /// </summary>
    void RippleWave()
    {
        if (!waveActive || waveBase == null) return;
        float time = Time.time;
        const float reach = 0.8f; // how far behind the edge the ripple fades out
        for (int i = 0; i < waveBase.Length; i++)
        {
            Vector3 v = waveBase[i];
            float w = Mathf.Clamp01((v.z - (waveFrontZ - reach)) / reach);
            w *= w;
            float push = Mathf.Sin(v.x * 5.5f + time * 1.7f) * 0.05f + Mathf.Sin(v.x * 13f - time * 2.6f) * 0.02f;
            v.z += w * push;
            v.y += Mathf.Sin(v.x * 3f + v.z * 4f + time * 2.2f) * 0.008f * (0.3f + w);
            waveShown[i] = v;
        }
        waveMesh.vertices = waveShown;
        waveMesh.RecalculateBounds();
    }

    IEnumerator SplashAt(Vector3 position)
    {
        splash.position = new Vector3(position.x, feetSandHeight + 0.03f, position.z);
        const float duration = 0.8f;
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            splash.localScale = splashScale * Mathf.Sin(t / duration * Mathf.PI);
            yield return null;
        }
        splash.localScale = Vector3.zero;
    }

    // ---------------- Audio (generated, no asset files needed) ----------------

    void SetupAudio()
    {
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        clearClip = GeneratedAudio.Tone("Cleared", new[] { 660f, 880f, 1320f }, 0.12f, 0.35f);
        alertClip = GeneratedAudio.Tone("SafetyAlert", new[] { 1000f, 0f, 1000f, 0f, 1000f }, 0.1f, 0.4f);
        splashClip = GeneratedAudio.Noise("Splash", 0.6f, 6f, 0.35f);
        // Roar builds while the swell rolls in and peaks when it breaks (60% of the approach).
        float roar = approachSeconds + 1.2f;
        waveClip = Surf("WaveIn", roar, approachSeconds * 0.6f / roar, 0.45f);
        crashClip = Surf("Crash", 1.8f, 0.06f, 0.6f);      // the lip hitting the water: sudden, then fizzing foam
        backwashClip = Surf("Backwash", 3f, 0.25f, 0.3f);  // quieter drain back to sea
    }

    void Play(AudioClip clip)
    {
        if (audioSource && clip) audioSource.PlayOneShot(clip);
    }

    /// <summary>Sea-wave sound: noise with a swell envelope peaking at <paramref name="peak"/> (0..1) of its length,
    /// getting brighter at the crest and duller as it fades.</summary>
    static AudioClip Surf(string name, float seconds, float peak, float volume)
    {
        const int rate = 44100;
        var data = new float[(int)(rate * seconds)];
        var rng = new System.Random(name.GetHashCode());
        float a = 0f, b = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            float t = (float)i / data.Length;
            float env = t < peak ? Mathf.Pow(t / peak, 2f) : Mathf.Pow(1f - (t - peak) / (1f - peak), 1.5f);
            float cutoff = Mathf.Lerp(0.03f, 0.35f, env); // brighter at the crest
            float n = (float)rng.NextDouble() * 2f - 1f;
            a = Mathf.Lerp(a, n, cutoff);
            b = Mathf.Lerp(b, a, cutoff * 1.5f);
            float fizz = 1f + 0.35f * Mathf.Sin(t * 40f); // foamy shimmer
            data[i] = b * env * fizz * volume * 3f;
        }
        var clip = AudioClip.Create(name, data.Length, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
