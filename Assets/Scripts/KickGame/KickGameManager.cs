using System.Collections;
using UnityEngine;

/// <summary>
/// Kick-at-the-targets loop for the Training Ground, played standing.
/// One target in the goal lights up; the patient pulls the heel back to the wind-up angle (knee flexion),
/// holds it for a moment, then straightens the leg fully to kick (knee extension). Reaching both sends the
/// ball into the lit target. Wind-up targets are a percentage of the patient's comfortable range and never
/// pass the safety limit; drifting off the spot (balance) pauses the kick.
///
/// Loop: target lights -> wind-up -> kick -> goal or miss -> logged to CSV -> ball back on the spot -> next kick.
/// </summary>
public class KickGameManager : MonoBehaviour
{
    public enum Leg { Left, Right }

    [Header("Patient (set by the therapist)")]
    public string patientId = "P001";
    public Leg operatedLeg = Leg.Right;
    [Tooltip("Largest knee bend this patient can comfortably reach, in degrees")]
    public float comfortableMaxAngle = 90f;
    [Tooltip("Absolute safety limit (GP1 NFR-04). Never asked for; passing it raises an alert")]
    public float safetyCeilingAngle = 110f;
    [Tooltip("Knee angle that counts as a straight leg at the end of the kick")]
    public float fullExtensionAngle = 10f;
    [Tooltip("Patient holds a walker or bar: shows the support rail and allows a little more lean")]
    public bool usesSupport;

    [Header("Session")]
    public int kicksPerSession = 10;
    [Range(0.2f, 1f)] public float startWindUpPercent = 0.4f;
    [Range(0.2f, 1f)] public float minWindUpPercent = 0.3f;
    [Range(0.2f, 1f)] public float maxWindUpPercent = 0.8f;
    [Tooltip("Wind-up change after each kick: up on a goal, down on a miss")]
    public float windUpPercentStep = 0.05f;
    [Tooltip("Degrees below the wind-up target that still count")]
    public float angleTolerance = 3f;
    [Tooltip("How long the bent knee must be held before the kick, so a twitch doesn't count")]
    public float windUpHoldSeconds = 0.3f;
    [Tooltip("Time to reach the wind-up (and then to kick) before the attempt counts as missed")]
    public float attemptSeconds = 12f;
    [Tooltip("Once the leg starts to straighten, time to reach full extension")]
    public float extendSeconds = 3f;

    [Header("Balance (standing)")]
    [Tooltip("Head drift from the start spot that pauses the kick, metres")]
    public float maxLean = 0.25f;
    public float maxLeanWithSupport = 0.35f;

    [Header("Timing (seconds)")]
    public float flightSeconds = 1f;
    public float resultSeconds = 1.5f;
    public float restSeconds = 2f;

    [Header("Therapist")]
    [Tooltip("Setup screen on the PC before each session; off = start with A / Space")]
    public bool useDoctorPanel = true;

    [Header("Scene references")]
    public JointAngleCalculator angleSource;
    [Tooltip("Camera knee sensor; until it sends readings the simulator moves the leg")]
    public CameraPoseJoints cameraSensor;
    public WaveHUD hud;
    [Tooltip("Headset camera (CenterEyeAnchor)")]
    public Transform head;
    [Tooltip("Virtual legs root; kept under the head so the legs stand where the patient stands")]
    public Transform body;
    public Transform ball;
    [Tooltip("Target rings in the goal; their children (the fills) light with them")]
    public Renderer[] targets;
    public GameObject supportRail;
    public Color targetColor = new Color(0.3f, 0.85f, 1f);
    [Tooltip("Spectators in the stand; found automatically when left empty")]
    public StadiumCrowd crowd;

    [Header("Sounds (empty = generated)")]
    public AudioClip kickSound;
    public AudioClip netSound;
    public AudioClip whistleSound;
    public AudioClip finalWhistleSound;

    // Session state
    bool sessionRunning;
    float sessionStartTime, peakSum;  // for the patient record: session length and average wind-up bend
    int peakCount;
    int score, streak, bestStreak, goals, safetyAlerts, balancePauses;
    float windUpPercent, sessionMaxAngle;
    float currentTarget = -1f;
    bool aboveCeiling, leaning;
    bool startRequested;
    int activeTarget = -1, lastTarget = -1;
    Vector3 startHeadXZ;

    Vector3 ballHome;
    Quaternion ballHomeRotation;
    float[] flash;
    MaterialPropertyBlock block;
    static readonly int EmissiveId = Shader.PropertyToID("emissiveFactor");

    AudioSource audioSource;
    AudioClip readyClip, kickClip, goalClip, missClip, alertClip, netClip, whistleClip, finalWhistleClip;
    KickLogger logger;
    bool recordOpen;  // a session has started and is not yet in the patient's history
    FootballSetupPanel panel;
    float sessionMinExtension = float.MaxValue; // straightest knee reached in a kick
    SessionControls controls;

    float Angle => angleSource ? angleSource.CurrentKneeAngle : 0f;
    bool SensorLost => cameraSensor && cameraSensor.TrackingLost;

    IEnumerator Start()
    {
        ballHome = ball.position;
        ballHomeRotation = ball.rotation;
        flash = new float[targets.Length];
        if (cameraSensor) cameraSensor.useLeftLeg = operatedLeg == Leg.Left;
        block = new MaterialPropertyBlock();
        if (!crowd) crowd = FindFirstObjectByType<StadiumCrowd>();
        hud.ApplyTheme(UIStyle.Theme.Football);
        SetupAudio();
        if (useDoctorPanel) panel = FootballSetupPanel.Create(this);
        controls = SessionControls.Create(transform, StopSession);

        while (true)
        {
            yield return WaitForStartPress();
            controls.SetVisible(true);
            yield return RunSession();
            controls.SetVisible(false);
        }
    }

    void Update()
    {
        if (RehabInput.MenuPressed)
        {
            SaveStoppedSession();
            SessionConfig.ReturnToMenu();
        }
        if (supportRail && supportRail.activeSelf != usesSupport) supportRail.SetActive(usesSupport);

        float angle = Angle;
        hud.SetAngle(angle, currentTarget, safetyCeilingAngle);
        if (sessionRunning) sessionMaxAngle = Mathf.Max(sessionMaxAngle, angle);

        bool over = angle > safetyCeilingAngle;
        if (over && !aboveCeiling)
        {
            Play(alertClip);
            if (sessionRunning) safetyAlerts++;
        }
        aboveCeiling = over;

        bool wasLeaning = leaning;
        leaning = sessionRunning && head && HeadDrift() > (usesSupport ? maxLeanWithSupport : maxLean);
        if (leaning && !wasLeaning)
        {
            Play(alertClip);
            balancePauses++;
        }
        hud.SetWarning(SessionControls.Paused ? "Paused - rest your leg"
                     : over ? "Too far! Ease your knee back gently"
                     : leaning ? "Stand steady - step back to your spot"
                     : SensorLost ? "The camera can't see your leg" : null);

        UpdateTargetGlow();
    }

    void LateUpdate()
    {
        if (body && head) body.position = new Vector3(head.position.x, body.position.y, head.position.z);
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

    /// <summary>True when the therapist lowered anything below the chosen stage, so it must not unlock the next one.</summary>
    bool IsEasierThanStage()
    {
        var p = SessionConfig.Patient;
        if (p == null) return false;
        int s = SessionConfig.Stage;
        return kicksPerSession < Stages.Kicks(s)
               || comfortableMaxAngle < p.comfortableMaxAngle - 0.5f
               || minWindUpPercent < Stages.KickMinPercent(s) - 0.005f
               || maxWindUpPercent < Stages.KickMaxPercent(s) - 0.005f
               || windUpHoldSeconds < Stages.KickHoldSeconds(s) - 0.05f
               || attemptSeconds > Stages.KickAttemptSeconds(s) + 0.5f
               || fullExtensionAngle > 10.5f || angleTolerance > 3.5f;
    }

    /// <summary>A session ended early (menu or quit) still goes into the history, marked as stopped.</summary>
    void SaveStoppedSession()
    {
        if (!recordOpen || !SessionConfig.Active || SessionConfig.Game != TrainingGame.Football) return;
        recordOpen = false;
        var extras = SessionExtrasNow();
        extras.stoppedEarly = true;
        PatientStore.RecordSession(SessionConfig.PatientId, TrainingGame.Football, SessionConfig.Stage, score,
                                   peakCount > 0 ? (float)goals / peakCount : 0f, sessionMaxAngle, extras, out _);
    }

    SessionExtras SessionExtrasNow() => new SessionExtras
    {
        durationSeconds = Time.time - sessionStartTime,
        avgAngle = peakCount > 0 ? peakSum / peakCount : 0f,
        safetyAlerts = safetyAlerts,
        balancePauses = balancePauses,
        leg = operatedLeg.ToString(),
        bestLeft = operatedLeg == Leg.Left ? sessionMaxAngle : 0f,
        bestRight = operatedLeg == Leg.Right ? sessionMaxAngle : 0f,
        planned = kicksPerSession,
        hasExtension = sessionMinExtension >= 0f && sessionMinExtension <= 180f,
        bestExtension = sessionMinExtension <= 180f ? sessionMinExtension : 0f,
        easier = IsEasierThanStage(),
        done = peakCount,
        settings = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                                 "wind-up {0:0}-{1:0}% of {2:0} deg; hold {3:0.#} s; support {4}",
                                 minWindUpPercent * 100f, maxWindUpPercent * 100f, comfortableMaxAngle,
                                 windUpHoldSeconds, usesSupport ? "yes" : "no"),
    };

    // ---------------- Session flow ----------------

    IEnumerator WaitForStartPress()
    {
        currentTarget = -1f;
        activeTarget = -1;
        hud.SetHold(-1f);
        hud.SetScore(-1, 0);
        ApplySessionConfig(); // the stage may have moved up after the last session
        hud.SetStatus("Press  A  or  Space  to start", WaveHUD.Neutral);
        string who = $"Patient {patientId}  ·  {operatedLeg} knee  ·  comfortable max {comfortableMaxAngle:0}°" +
                     (usesSupport ? "  ·  with support" : "") +
                     (SessionConfig.Active ? $"\n{Stages.Name(SessionConfig.Stage)}" : "");
        hud.SetInfo(who.Replace("\n", "  ·  "));
        // After a session the results card stays up and its button starts the next one; otherwise the start card.
        if (panel)
        {
            startRequested = false;
            if (!hud.ResultsShowing) hud.ShowTitleCard($"{who}\nYour therapist is setting up", "PLEASE WAIT");
            panel.Show(who.Replace("\n", "  ·  "));
            // Only the panel's Start button begins: Space would type into its fields.
            while (!startRequested) yield return null;
            startRequested = false;
            hud.HideTitleCard();
            hud.HideResults();
            yield break;
        }
        if (!hud.ResultsShowing)
            hud.ShowTitleCard($"{who}\n{kicksPerSession} penalties  ·  pull your heel back, then kick!", "PRESS  A  TO  KICK OFF");
        yield return null;
        while (!StartPressed()) yield return null;
        UISound.Click();
        hud.HideTitleCard();
        hud.HideResults();
    }

    IEnumerator RunSession()
    {
        sessionRunning = true;
        sessionStartTime = Time.time;
        recordOpen = true;
        sessionMinExtension = float.MaxValue;
        peakSum = 0f;
        peakCount = 0;
        score = streak = bestStreak = goals = safetyAlerts = balancePauses = 0;
        hud.SetScore(0, 0);
        sessionMaxAngle = Angle;
        windUpPercent = startWindUpPercent;
        startHeadXZ = head ? Flat(head.position) : Vector3.zero;
        logger = new KickLogger(SessionConfig.LogId(patientId), operatedLeg.ToString(), usesSupport);
        Debug.Log($"[KickGame] Logging session to {logger.FilePath}");

        for (int n = 3; n > 0; n--)
        {
            hud.SetStatus($"Get ready...  {n}", WaveHUD.Neutral);
            hud.Popup(n.ToString(), "", WaveHUD.Accent, 0.9f);
            UISound.Tick();
            yield return new WaitForSeconds(1f);
        }
        hud.Popup("KICK OFF!", "", WaveHUD.Good, 1.2f);
        UISound.Go();
        Play(whistleClip);

        for (int i = 1; i <= kicksPerSession; i++)
        {
            yield return RunKick(i);
            if (i < kicksPerSession)
            {
                hud.SetStatus("Relax your leg", WaveHUD.Neutral);
                yield return new WaitForSeconds(restSeconds);
            }
        }

        sessionRunning = false;
        currentTarget = -1f;
        logger.Dispose();
        logger = null;
        Play(finalWhistleClip);
        hud.SetStatus($"Session complete!\nScore {score}  ·  goals {goals}/{kicksPerSession}", WaveHUD.Accent);
        hud.SetInfo($"Best knee bend this session: {sessionMaxAngle:0}°");
        hud.Popup("FULL TIME!", $"{goals} / {kicksPerSession} goals", WaveHUD.Accent, 2.2f);
        if (goals > 0) crowd?.Cheer(4f);
        yield return new WaitForSeconds(2.5f);

        float fraction = (float)goals / Mathf.Max(1, kicksPerSession);
        int stars = fraction >= 0.8f ? 3 : fraction >= 0.5f ? 2 : goals > 0 ? 1 : 0;
        string rating = stars == 3 ? "Champion! Brilliant kicking" : stars == 2 ? "Great match - well played"
                      : stars == 1 ? "Good effort - keep practising" : "Every kick makes you stronger";
        // Started from the menu: save to the patient's record and maybe unlock the next stage.
        string prompt = "PRESS  A  TO  PLAY AGAIN";
        var fourthRow = ("Best streak", bestStreak.ToString(), Color.white);
        if (SessionConfig.Active && SessionConfig.Game == TrainingGame.Football)
        {
            int played = SessionConfig.Stage;
            var extras = SessionExtrasNow();
            recordOpen = false;
            bool unlocked = PatientStore.RecordSession(SessionConfig.PatientId, TrainingGame.Football, played, score,
                                                       fraction, sessionMaxAngle, extras, out bool passed);
            fourthRow = (Stages.Name(played), passed ? "PASSED" : extras.easier ? "easier settings" : $"need {Stages.PassFraction * 100f:0}%", passed ? WaveHUD.Good : WaveHUD.Accent);
            if (unlocked)
            {
                SessionConfig.Stage = played + 1;
                UISound.Confirm();
                crowd?.Cheer(3f);
                hud.Popup($"STAGE {played + 1} UNLOCKED!", Stages.Name(played + 1), WaveHUD.Accent, 2.6f);
                yield return new WaitForSeconds(2.8f);
            }
            prompt = $"A: PLAY STAGE {SessionConfig.Stage}   ·   ESC: MENU";
        }
        hud.ShowResults("FULL TIME", stars, rating, stars >= 2 ? WaveHUD.Good : WaveHUD.Accent, new[]
        {
            ("Score", score.ToString(), Color.white),
            ("Goals", $"{goals} / {kicksPerSession}", Color.white),
            fourthRow,
            ("Best knee bend", $"{sessionMaxAngle:0}°", Color.white),
            ("Balance pauses", balancePauses.ToString(), balancePauses > 0 ? WaveHUD.Accent : WaveHUD.Good),
            ("Safety alerts", safetyAlerts.ToString(), safetyAlerts > 0 ? WaveHUD.Bad : WaveHUD.Good),
        }, prompt);
    }

    /// <summary>
    /// Applies the patient and stage chosen in the start menu (if the session came from there).
    /// Wind-up targets are a share of the patient's own comfortable maximum, capped by the safety limit.
    /// </summary>
    bool ApplySessionConfig()
    {
        var p = SessionConfig.Game == TrainingGame.Football ? SessionConfig.Patient : null;
        if (p == null) return false;
        int s = SessionConfig.Stage;
        patientId = p.name;
        operatedLeg = p.IsLeftLeg ? Leg.Left : Leg.Right;
        if (cameraSensor) cameraSensor.useLeftLeg = operatedLeg == Leg.Left;
        comfortableMaxAngle = p.comfortableMaxAngle;
        safetyCeilingAngle = p.SafetyLimit;
        kicksPerSession = Stages.Kicks(s);
        startWindUpPercent = Stages.KickStartPercent(s);
        minWindUpPercent = Stages.KickMinPercent(s);
        maxWindUpPercent = Stages.KickMaxPercent(s);
        windUpHoldSeconds = Stages.KickHoldSeconds(s);
        attemptSeconds = Stages.KickAttemptSeconds(s);
        return true;
    }

    IEnumerator RunKick(int index)
    {
        int zone = PickTarget();
        activeTarget = zone;
        float target = WindUpAngle();
        float percentUsed = windUpPercent;
        currentTarget = target;
        hud.SetInfo($"Kick {index}/{kicksPerSession}  ·  Score {score}  ·  Streak {streak}");

        // 1. Wind-up: pull the heel back to the target and hold it briefly. Losing balance pauses the clock.
        float maxFlexion = Angle, minExtension = Angle;
        float held = 0f, waited = 0f;
        bool armed = false;
        while (!armed && waited < attemptSeconds)
        {
            float angle = Angle;
            maxFlexion = Mathf.Max(maxFlexion, angle);
            if (leaning || SensorLost)
            {
                held = 0f;
                hud.SetHold(-1f);
                hud.SetStatus(SensorLost ? "Waiting for the camera" : "Stand steady first", WaveHUD.Bad);
                yield return null;
                continue;
            }
            bool overLimit = angle > safetyCeilingAngle;
            if (angle >= target - angleTolerance && !overLimit) held += Time.deltaTime;
            else held = 0f;
            hud.SetHold(held / windUpHoldSeconds);
            hud.SetStatus(overLimit ? "Too far!\nEase back a little"
                        : held > 0f ? "Hold it..." : "Pull your heel back", held > 0f ? WaveHUD.Good : WaveHUD.Accent);
            waited += Time.deltaTime;
            armed = held >= windUpHoldSeconds;
            yield return null;
        }

        // 2. Kick: straighten all the way. Timed from the moment the leg starts to straighten.
        bool fullExtension = false;
        float peak = Angle;
        if (armed)
        {
            currentTarget = -1f;
            hud.SetHold(-1f);
            hud.SetStatus("Now kick!\nStraighten your leg", WaveHUD.Good);
            Play(readyClip);
            crowd?.Clap(); // the crowd claps the patient through the kick
            float straightening = -1f;
            waited = 0f;
            minExtension = Angle;
            while (waited < attemptSeconds)
            {
                float angle = Angle;
                peak = Mathf.Max(peak, angle);
                maxFlexion = Mathf.Max(maxFlexion, angle);
                minExtension = Mathf.Min(minExtension, angle);
                if (angle <= fullExtensionAngle)
                {
                    fullExtension = true;
                    break;
                }
                if (straightening < 0f && angle < peak - 15f) straightening = 0f;
                if (straightening >= 0f && (straightening += Time.deltaTime) > extendSeconds) break;
                waited += Time.deltaTime;
                yield return null;
            }
        }

        // 3. Result, scoring and difficulty.
        bool goal = armed && fullExtension;
        int points;
        Vector3 aim = targets[zone].bounds.center;
        hud.SetHold(-1f);
        currentTarget = -1f;
        if (goal)
        {
            points = 100 + streak * 20;
            streak++;
            bestStreak = Mathf.Max(bestStreak, streak);
            goals++;
            windUpPercent = Mathf.Min(maxWindUpPercent, windUpPercent + windUpPercentStep);
            Play(kickClip);
            yield return FlyBall(aim, flightSeconds, 0.6f);
            flash[zone] = 1f;
            Play(netClip);
            Play(goalClip);
            crowd?.Cheer();
            hud.Popup("GOAL!", streak >= 2 ? $"+{points}   ·   {streak} in a row!" : $"+{points}", WaveHUD.Good, 2f);
            hud.SetStatus($"Goal!  +{points}", WaveHUD.Good);
        }
        else
        {
            streak = 0;
            windUpPercent = Mathf.Max(minWindUpPercent, windUpPercent - windUpPercentStep);
            if (armed)
            {
                // Partial kick: the ball rolls as far as the leg straightened.
                float fraction = Mathf.Clamp01((peak - minExtension) / Mathf.Max(1f, peak - fullExtensionAngle));
                points = Mathf.RoundToInt(fraction * 40f);
                Vector3 dir = Flat(aim - ballHome).normalized;
                Play(kickClip);
                bool wide = fraction >= 0.5f;
                if (wide)
                {
                    // A strong but incomplete kick slices past the post and goes out.
                    float side = Mathf.Sign(aim.x - ballHome.x);
                    if (side == 0f) side = Random.value < 0.5f ? -1f : 1f;
                    Vector3 outside = new Vector3(side * 5.2f, 0.6f, aim.z - 1.5f);
                    yield return FlyBall(outside, flightSeconds * 1.2f, 0.9f);
                }
                else yield return FlyBall(ballHome + dir * (1.5f + 5f * fraction), flightSeconds * 1.4f, 0.1f);
                Play(missClip);
                crowd?.Groan();
                hud.Popup(wide ? "WIDE!" : "SO CLOSE!", "Straighten all the way", WaveHUD.Bad, 1.8f);
                hud.SetStatus(points > 0 ? $"Almost!  +{points}\nStraighten all the way" : "Almost!\nStraighten all the way", WaveHUD.Bad);
            }
            else
            {
                points = 0;
                Play(missClip);
                crowd?.Groan();
                hud.Popup("MISSED", "A smaller bend next time", WaveHUD.Bad, 1.8f);
                hud.SetStatus("Missed\nA smaller bend next time", WaveHUD.Bad);
            }
        }
        score += points;
        hud.SetScore(score, streak);
        activeTarget = -1;
        hud.SetInfo($"Kick {index}/{kicksPerSession}  ·  Score {score}  ·  Streak {streak}");
        Debug.Log($"[KickGame] Kick {index}: {targets[zone].name} target {target:0}° max {maxFlexion:0}° min {minExtension:0}° " +
                  $"armed={armed} full={fullExtension} goal={goal} +{points}");

        sessionMinExtension = Mathf.Min(sessionMinExtension, minExtension);
        peakSum += maxFlexion; // deepest wind-up of this kick, for the patient record
        peakCount++;
        logger.LogKick(index, targets[zone].name, percentUsed, target, maxFlexion, minExtension, armed, fullExtension, goal,
                       points, score, sessionMaxAngle, safetyAlerts, balancePauses);

        // 4. Ball back on the spot.
        yield return new WaitForSeconds(resultSeconds);
        ball.SetPositionAndRotation(ballHome, ballHomeRotation);
    }

    float WindUpAngle()
    {
        float limit = Mathf.Min(comfortableMaxAngle, safetyCeilingAngle - 5f);
        return Mathf.Clamp(windUpPercent * comfortableMaxAngle, fullExtensionAngle + 10f, limit);
    }

    int PickTarget()
    {
        int zone = Random.Range(0, targets.Length);
        if (zone == lastTarget && targets.Length > 1) zone = (zone + 1 + Random.Range(0, targets.Length - 1)) % targets.Length;
        lastTarget = zone;
        return zone;
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

    float HeadDrift() => Vector3.Distance(Flat(head.position), startHeadXZ);

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    // ---------------- Ball and targets ----------------

    /// <summary>Moves the ball along an arc (no physics, so every kick lands where the game decided).</summary>
    IEnumerator FlyBall(Vector3 to, float duration, float arcHeight)
    {
        Vector3 from = ball.position;
        Vector3 spinAxis = Vector3.Cross(Vector3.up, Flat(to - from).normalized);
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            float k = t / duration;
            float eased = 1f - (1f - k) * (1f - k); // fast off the foot, slowing down
            ball.position = Vector3.Lerp(from, to, eased) + Vector3.up * arcHeight * 4f * eased * (1f - eased);
            ball.Rotate(spinAxis, 540f * Time.deltaTime * (1f - k), Space.World);
            yield return null;
        }
        ball.position = to;
    }

    /// <summary>Dim when idle, a slow breathing glow on the lit target, a short flash on a goal.</summary>
    void UpdateTargetGlow()
    {
        for (int i = 0; i < targets.Length; i++)
        {
            float intensity = i == activeTarget ? 1.6f + 0.4f * Mathf.Sin(Time.time * 3f) : 0.35f;
            intensity += flash[i] * 4f;
            flash[i] = Mathf.Max(0f, flash[i] - Time.deltaTime / 0.6f);
            foreach (var r in targets[i].GetComponentsInChildren<Renderer>())
            {
                r.GetPropertyBlock(block);
                block.SetColor(EmissiveId, targetColor * intensity);
                r.SetPropertyBlock(block);
            }
        }
    }

    // ---------------- Audio ----------------

    void SetupAudio()
    {
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        readyClip = GeneratedAudio.Tone("Ready", new[] { 880f }, 0.1f, 0.25f);
        kickClip = kickSound ? kickSound : GeneratedAudio.Kick("Kick", 0.8f);
        netClip = netSound ? netSound : GeneratedAudio.NetSwish("Net", 0.5f);
        whistleClip = whistleSound ? whistleSound : GeneratedAudio.Whistle("Whistle", new[] { 0.7f }, 0.35f);
        finalWhistleClip = finalWhistleSound ? finalWhistleSound : GeneratedAudio.Whistle("FinalWhistle", new[] { 0.3f, 0.3f, 0.9f }, 0.35f);
        goalClip = GeneratedAudio.Tone("Goal", new[] { 660f, 880f, 1320f }, 0.12f, 0.35f);
        missClip = GeneratedAudio.Tone("Miss", new[] { 440f, 330f }, 0.15f, 0.3f);
        alertClip = GeneratedAudio.Tone("SafetyAlert", new[] { 1000f, 0f, 1000f, 0f, 1000f }, 0.1f, 0.4f);
    }

    void Play(AudioClip clip)
    {
        if (audioSource && clip) audioSource.PlayOneShot(clip);
    }
}
