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

    [Header("Scene references")]
    public JointAngleCalculator angleSource;
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

    // Session state
    bool sessionRunning;
    int score, streak, goals, safetyAlerts, balancePauses;
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
    AudioClip readyClip, kickClip, goalClip, missClip, alertClip;
    KickLogger logger;

    float Angle => angleSource ? angleSource.CurrentKneeAngle : 0f;

    IEnumerator Start()
    {
        ballHome = ball.position;
        ballHomeRotation = ball.rotation;
        flash = new float[targets.Length];
        block = new MaterialPropertyBlock();
        SetupAudio();

        while (true)
        {
            yield return WaitForStartPress();
            yield return RunSession();
        }
    }

    void Update()
    {
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
        hud.SetWarning(over ? "Too far! Ease your knee back gently"
                     : leaning ? "Stand steady - step back to your spot" : null);

        UpdateTargetGlow();
    }

    void LateUpdate()
    {
        if (body && head) body.position = new Vector3(head.position.x, body.position.y, head.position.z);
    }

    void OnDestroy() => logger?.Dispose();

    // ---------------- Session flow ----------------

    IEnumerator WaitForStartPress()
    {
        currentTarget = -1f;
        activeTarget = -1;
        hud.SetHold(-1f);
        hud.SetStatus("Press  A  or  Space  to start", WaveHUD.Neutral);
        hud.SetInfo($"Patient {patientId}  ·  {operatedLeg} knee  ·  comfortable max {comfortableMaxAngle:0}°" +
                    (usesSupport ? "  ·  with support" : ""));
        yield return null;
        while (!StartPressed()) yield return null;
    }

    IEnumerator RunSession()
    {
        sessionRunning = true;
        score = streak = goals = safetyAlerts = balancePauses = 0;
        sessionMaxAngle = Angle;
        windUpPercent = startWindUpPercent;
        startHeadXZ = head ? Flat(head.position) : Vector3.zero;
        logger = new KickLogger(patientId, operatedLeg.ToString(), usesSupport);
        Debug.Log($"[KickGame] Logging session to {logger.FilePath}");

        for (int n = 3; n > 0; n--)
        {
            hud.SetStatus($"Get ready...  {n}", WaveHUD.Neutral);
            yield return new WaitForSeconds(1f);
        }

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
        hud.SetStatus($"Session complete!\nScore {score}  ·  goals {goals}/{kicksPerSession}", WaveHUD.Accent);
        hud.SetInfo($"Best knee bend this session: {sessionMaxAngle:0}°");
        yield return new WaitForSeconds(3f);
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
            if (leaning)
            {
                held = 0f;
                hud.SetHold(-1f);
                hud.SetStatus("Stand steady first", WaveHUD.Bad);
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
            goals++;
            windUpPercent = Mathf.Min(maxWindUpPercent, windUpPercent + windUpPercentStep);
            Play(kickClip);
            yield return FlyBall(aim, flightSeconds, 0.6f);
            flash[zone] = 1f;
            Play(goalClip);
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
                yield return FlyBall(ballHome + dir * (1.5f + 5f * fraction), flightSeconds * 1.4f, 0.1f);
                Play(missClip);
                hud.SetStatus(points > 0 ? $"Almost!  +{points}\nStraighten all the way" : "Almost!\nStraighten all the way", WaveHUD.Bad);
            }
            else
            {
                points = 0;
                Play(missClip);
                hud.SetStatus("Missed\nA smaller bend next time", WaveHUD.Bad);
            }
        }
        score += points;
        activeTarget = -1;
        hud.SetInfo($"Kick {index}/{kicksPerSession}  ·  Score {score}  ·  Streak {streak}");
        Debug.Log($"[KickGame] Kick {index}: {targets[zone].name} target {target:0}° max {maxFlexion:0}° min {minExtension:0}° " +
                  $"armed={armed} full={fullExtension} goal={goal} +{points}");

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
        kickClip = GeneratedAudio.Noise("Kick", 0.25f, 18f, 0.5f);
        goalClip = GeneratedAudio.Tone("Goal", new[] { 660f, 880f, 1320f }, 0.12f, 0.35f);
        missClip = GeneratedAudio.Tone("Miss", new[] { 440f, 330f }, 0.15f, 0.3f);
        alertClip = GeneratedAudio.Tone("SafetyAlert", new[] { 1000f, 0f, 1000f, 0f, 1000f }, 0.1f, 0.4f);
    }

    void Play(AudioClip clip)
    {
        if (audioSource && clip) audioSource.PlayOneShot(clip);
    }
}
