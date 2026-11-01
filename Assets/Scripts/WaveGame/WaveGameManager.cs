using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Wave-avoidance core loop (GP1 4.3.2): a wave runs up the beach toward the patient's feet,
/// the patient pulls the heel back (knee flexion) to the target angle and holds it until the wave
/// retreats. Targets are a percentage of the patient's comfortable range and never pass the safety limit.
///
/// Loop: wave comes in -> hold the target angle -> cleared (points) or splashed -> logged to CSV -> next wave.
/// </summary>
public class WaveGameManager : MonoBehaviour
{
    public enum Leg { Left, Right }
    /// <summary>Both = right leg first, then the left, with an instruction screen between them.</summary>
    public enum LegMode { Left, Right, Both }

    [Header("Patient (defaults for the therapist panel)")]
    [Tooltip("Patient name; also used in the session CSV file name")]
    public string patientId = "P001";
    public Leg operatedLeg = Leg.Right;
    [Tooltip("Which leg(s) to train this session; Both trains the right leg, then the left")]
    public LegMode legMode = LegMode.Right;
    [Tooltip("Show the therapist setup panel before each session")]
    public bool useDoctorPanel = true;
    [Tooltip("Easiest target for this patient, in degrees")]
    public float minTargetAngle = 30f;
    [Tooltip("Hardest target for this patient (their comfortable maximum), in degrees")]
    public float maxTargetAngle = 90f;
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
    [Tooltip("Degrees below the target that still count as holding")]
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
    public VirtualLegPose leftLeg;
    public VirtualLegPose rightLeg;
    public WaveHUD hud;
    [Tooltip("Water sheet that runs up the beach (a copy is made at runtime)")]
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
    float targetLevel, holdSeconds, sessionMaxAngle, heldSum, highestCleared;
    int wavesDone;
    readonly List<StageResult> results = new List<StageResult>();
    ResultsPanel resultsPanel;
    float currentTarget = -1f;
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
    AudioClip clearClip, splashClip, alertClip, waveClip, backwashClip;
    SessionLogger logger;

    VirtualLegPose OperatedLeg => operatedLeg == Leg.Left ? leftLeg : rightLeg;
    float Angle => angleSource ? angleSource.CurrentKneeAngle : 0f;

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
        if (useDoctorPanel)
        {
            panel = DoctorPanel.Create(this);
            resultsPanel = ResultsPanel.Create();
        }

        while (true)
        {
            yield return WaitForStartPress();
            yield return RunSession();
        }
    }

    /// <summary>Called by the therapist panel's Start button.</summary>
    public void ApplyDoctorSettings(string patientName, LegMode mode, float minAngle, float maxAngle,
                                    int waves, float startHold, float maxHold)
    {
        patientId = patientName;
        legMode = mode;
        minTargetAngle = minAngle;
        maxTargetAngle = maxAngle;
        wavesPerSession = waves;
        startHoldSeconds = startHold;
        maxHoldSeconds = maxHold;
    }

    // Only the operated leg follows the sensor; the other one rests in a relaxed pose.
    void ApplyOperatedLeg()
    {
        if (leftLeg) leftLeg.angleSource = operatedLeg == Leg.Left ? leftSource : null;
        if (rightLeg) rightLeg.angleSource = operatedLeg == Leg.Right ? rightSource : null;
    }

    void SwitchToLeg(Leg leg)
    {
        operatedLeg = leg;
        ApplyOperatedLeg();
        if (OperatedLeg) feetZ = OperatedLeg.HeelPosition.z;
    }

    void Update()
    {
        if (!hud) return;
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
        hud.SetWarning(over ? "Too far! Ease your knee back gently" : null);
    }

    void LateUpdate() => RippleWave();

    void OnDestroy() => logger?.Dispose();

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
            panel.Show();
            // The panel's Start button is the only way to begin (Space would type into its fields).
            while (!startRequested) yield return null;
            startRequested = false;
            yield break;
        }
        hud.SetStatus("Press  A  or  Space  to start", WaveHUD.Neutral);
        hud.SetInfo($"Patient {patientId}  ·  {legMode} knee  ·  targets {minTargetAngle:0}°–{maxTargetAngle:0}°");
        yield return null;
        while (!StartPressed()) yield return null;
    }

    /// <summary>One whole session: the chosen leg, or right then left when training both.</summary>
    IEnumerator RunSession()
    {
        Leg[] stages = legMode == LegMode.Both ? new[] { Leg.Right, Leg.Left }
                     : new[] { legMode == LegMode.Left ? Leg.Left : Leg.Right };
        results.Clear();

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
            patient = patientId, score = TotalScore(), cleared = totalCleared, waves = totalWaves,
            bestAngle = best, mode = legMode.ToString(), date = System.DateTime.UtcNow.ToString("o"),
        };
        var board = Leaderboard.Add(entry, out int rank);
        if (resultsPanel)
        {
            resultsPanel.Show(patientId, results, minTargetAngle, maxTargetAngle, board, entry, rank);
            while (resultsPanel.IsOpen) yield return null;
        }
        else yield return new WaitForSeconds(3f);
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
            if (StartPressed()) yield break;
            yield return null;
        }
    }

    IEnumerator RunLeg()
    {
        sessionRunning = true;
        score = streak = cleared = safetyAlerts = wavesDone = 0;
        heldSum = highestCleared = 0f;
        sessionMaxAngle = Angle;
        targetLevel = startTargetLevel;
        holdSeconds = startHoldSeconds;
        logger = new SessionLogger(patientId, operatedLeg.ToString());
        Debug.Log($"[WaveGame] Logging session to {logger.FilePath}");

        for (int n = 3; n > 0; n--)
        {
            hud.SetStatus($"Get ready...  {n}", WaveHUD.Neutral);
            yield return new WaitForSeconds(1f);
        }

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
        });
        hud.SetStatus($"{operatedLeg} leg finished!\nScore {score}  ·  cleared {cleared}/{wavesPerSession}", WaveHUD.Accent);
        hud.SetInfo($"Best knee bend: {sessionMaxAngle:0}°");
        yield return new WaitForSeconds(2f);
    }

    IEnumerator RunWave(int index)
    {
        float target = TargetAngle();
        float levelUsed = targetLevel;
        float hold = holdSeconds;
        currentTarget = target;
        hud.SetInfo($"Wave {index}/{wavesPerSession}  ·  Score {score}  ·  Streak {streak}");
        hud.SetHold(-1f);

        // 1. Wave runs up the beach. The water stops just in front of where the heel is at the target angle.
        float startEdge = waveSpawn ? waveSpawn.position.z : -15f;
        float reachEdge = OperatedLeg.HeelPositionAt(target).z - waterGapInFrontOfHeel;
        hud.SetStatus("Wave coming!\nPull your heel back", WaveHUD.Accent);
        Play(waveClip);
        waveActive = true;
        for (float t = 0f; t < approachSeconds; t += Time.deltaTime)
        {
            float k = t / approachSeconds;
            // Fast surge at first, slowing as the water runs out of energy on the sand.
            float run = 1f - Mathf.Pow(1f - k, 3f);
            SetWave(Mathf.Lerp(startEdge, reachEdge, run));
            yield return null;
        }

        // 2. Hold: the angle must stay on target while the water sits at the feet.
        float heldTime = 0f, minAngle = float.MaxValue, maxAngle = float.MinValue;
        for (float t = 0f; t < hold; t += Time.deltaTime)
        {
            float angle = Angle;
            bool overLimit = angle > safetyCeilingAngle;
            // Going past the safety limit never counts as holding.
            bool holding = angle >= target - angleTolerance && !overLimit;
            if (holding) heldTime += Time.deltaTime;
            minAngle = Mathf.Min(minAngle, angle);
            maxAngle = Mathf.Max(maxAngle, angle);

            hud.SetHold(t / hold);
            if (overLimit) hud.SetStatus("Too far!\nEase back a little", WaveHUD.Bad);
            else if (holding) hud.SetStatus("Hold it!", WaveHUD.Good);
            else hud.SetStatus("Feet in the water!\nPull your heel back", WaveHUD.Bad);
            SetWave(reachEdge + Mathf.Sin(t * 3f) * 0.03f);
            yield return null;
        }

        // 3. Result, scoring and difficulty.
        float fraction = hold > 0f ? heldTime / hold : 0f;
        bool waveCleared = fraction >= clearFraction;
        wavesDone++;
        heldSum += fraction;
        if (waveCleared) highestCleared = Mathf.Max(highestCleared, target);
        int points;
        if (waveCleared)
        {
            points = 100 + streak * 20;
            streak++;
            cleared++;
            targetLevel = Mathf.Min(1f, targetLevel + targetLevelStep);
            holdSeconds = Mathf.Min(maxHoldSeconds, holdSeconds + holdSecondsStep);
            hud.SetStatus($"Wave cleared!  +{points}", WaveHUD.Good);
            Play(clearClip);
        }
        else
        {
            points = Mathf.RoundToInt(fraction * 40f);
            streak = 0;
            targetLevel = Mathf.Max(0f, targetLevel - targetLevelStep);
            hud.SetStatus(points > 0 ? $"Splash!  +{points}" : "Splash!", WaveHUD.Bad);
            Play(splashClip);
            if (splash) StartCoroutine(SplashAt(OperatedLeg.HeelPosition));
        }
        score += points;
        hud.SetHold(-1f);
        hud.SetInfo($"Wave {index}/{wavesPerSession}  ·  Score {score}  ·  Streak {streak}");

        logger.LogWave(index, levelUsed, target, hold, minAngle, maxAngle, fraction, waveCleared,
                       points, score, sessionMaxAngle, safetyAlerts);

        // 4. Wave pulls back out to sea.
        currentTarget = -1f;
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

    float TargetAngle()
    {
        float top = Mathf.Min(maxTargetAngle, safetyCeilingAngle - 5f);
        return Mathf.Clamp(Mathf.Lerp(minTargetAngle, top, targetLevel), 5f, top);
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
        clearClip = Tone("Cleared", new[] { 660f, 880f, 1320f }, 0.12f, 0.35f);
        alertClip = Tone("SafetyAlert", new[] { 1000f, 0f, 1000f, 0f, 1000f }, 0.1f, 0.4f);
        splashClip = Noise("Splash", 0.6f, 6f, 0.35f);
        waveClip = Surf("WaveIn", 4.5f, 0.7f, 0.5f);       // builds up, crashes, hisses out
        backwashClip = Surf("Backwash", 3f, 0.25f, 0.3f);  // quieter drain back to sea
    }

    void Play(AudioClip clip)
    {
        if (audioSource && clip) audioSource.PlayOneShot(clip);
    }

    static AudioClip Tone(string name, float[] notes, float noteSeconds, float volume)
    {
        const int rate = 44100;
        int perNote = (int)(rate * noteSeconds);
        var data = new float[perNote * notes.Length];
        for (int n = 0; n < notes.Length; n++)
            for (int i = 0; i < perNote; i++)
            {
                float env = Mathf.Min(1f, i / 400f) * (1f - (float)i / perNote);
                data[n * perNote + i] = notes[n] > 0f ? Mathf.Sin(2f * Mathf.PI * notes[n] * i / rate) * env * volume : 0f;
            }
        var clip = AudioClip.Create(name, data.Length, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
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

    static AudioClip Noise(string name, float seconds, float decay, float volume)
    {
        const int rate = 44100;
        var data = new float[(int)(rate * seconds)];
        var rng = new System.Random(name.GetHashCode());
        float smooth = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            float t = (float)i / data.Length;
            float env = Mathf.Min(1f, t * 8f) * Mathf.Exp(-decay * t);
            smooth = Mathf.Lerp(smooth, (float)rng.NextDouble() * 2f - 1f, 0.25f); // soft, wash-like noise
            data[i] = smooth * env * volume;
        }
        var clip = AudioClip.Create(name, data.Length, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
