using System.Collections;
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

    [Header("Patient (set by the therapist)")]
    public string patientId = "P001";
    public Leg operatedLeg = Leg.Right;
    [Tooltip("Largest knee bend this patient can comfortably reach, in degrees")]
    public float comfortableMaxAngle = 90f;
    [Tooltip("Absolute safety limit (GP1 NFR-04). Never asked for; passing it raises an alert")]
    public float safetyCeilingAngle = 110f;

    [Header("Session")]
    public int wavesPerSession = 10;
    [Range(0.2f, 1f)] public float startTargetPercent = 0.5f;
    [Range(0.2f, 1f)] public float minTargetPercent = 0.35f;
    [Range(0.2f, 1f)] public float maxTargetPercent = 0.9f;
    [Tooltip("Target change after each wave: up when cleared, down when missed")]
    public float targetPercentStep = 0.05f;
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
    float targetPercent, holdSeconds, sessionMaxAngle;
    float currentTarget = -1f;
    bool aboveCeiling;
    bool startRequested;

    Transform wave;
    float waveEdgeOffset, waveTopOffset, feetZ;
    Transform splash;
    AudioSource audioSource;
    AudioClip clearClip, splashClip, alertClip, waveClip;
    SessionLogger logger;

    VirtualLegPose OperatedLeg => operatedLeg == Leg.Left ? leftLeg : rightLeg;
    float Angle => angleSource ? angleSource.CurrentKneeAngle : 0f;

    IEnumerator Start()
    {
        // Only the operated leg follows the sensor; the other one rests in a relaxed pose.
        if (leftLeg && operatedLeg != Leg.Left) leftLeg.angleSource = null;
        if (rightLeg && operatedLeg != Leg.Right) rightLeg.angleSource = null;

        CreateWave();
        if (splashTemplate)
        {
            splash = Instantiate(splashTemplate, transform);
            splash.name = "GameSplash";
            splash.localScale = Vector3.zero;
        }
        SetupAudio();

        while (true)
        {
            yield return WaitForStartPress();
            yield return RunSession();
        }
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

    void OnDestroy() => logger?.Dispose();

    // ---------------- Session flow ----------------

    IEnumerator WaitForStartPress()
    {
        currentTarget = -1f;
        hud.SetHold(-1f);
        hud.SetStatus("Press  A  or  Space  to start", WaveHUD.Neutral);
        hud.SetInfo($"Patient {patientId}  ·  {operatedLeg} knee  ·  comfortable max {comfortableMaxAngle:0}°");
        yield return null;
        while (!StartPressed()) yield return null;
    }

    IEnumerator RunSession()
    {
        sessionRunning = true;
        score = streak = cleared = safetyAlerts = 0;
        sessionMaxAngle = Angle;
        targetPercent = startTargetPercent;
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
        hud.SetStatus($"Session complete!\nScore {score}  ·  cleared {cleared}/{wavesPerSession}", WaveHUD.Accent);
        hud.SetInfo($"Best knee bend this session: {sessionMaxAngle:0}°");
        yield return new WaitForSeconds(3f);
    }

    IEnumerator RunWave(int index)
    {
        float target = TargetAngle();
        float percentUsed = targetPercent;
        float hold = holdSeconds;
        currentTarget = target;
        hud.SetInfo($"Wave {index}/{wavesPerSession}  ·  Score {score}  ·  Streak {streak}");
        hud.SetHold(-1f);

        // 1. Wave runs up the beach. The water stops just in front of where the heel is at the target angle.
        float startEdge = waveSpawn ? waveSpawn.position.z : -15f;
        float reachEdge = OperatedLeg.HeelPositionAt(target).z - waterGapInFrontOfHeel;
        hud.SetStatus("Wave coming!\nPull your heel back", WaveHUD.Accent);
        Play(waveClip);
        for (float t = 0f; t < approachSeconds; t += Time.deltaTime)
        {
            float k = t / approachSeconds;
            SetWave(Mathf.Lerp(startEdge, reachEdge, 1f - Mathf.Pow(1f - k, 3f)));
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
        int points;
        if (waveCleared)
        {
            points = 100 + streak * 20;
            streak++;
            cleared++;
            targetPercent = Mathf.Min(maxTargetPercent, targetPercent + targetPercentStep);
            holdSeconds = Mathf.Min(maxHoldSeconds, holdSeconds + holdSecondsStep);
            hud.SetStatus($"Wave cleared!  +{points}", WaveHUD.Good);
            Play(clearClip);
        }
        else
        {
            points = Mathf.RoundToInt(fraction * 40f);
            streak = 0;
            targetPercent = Mathf.Max(minTargetPercent, targetPercent - targetPercentStep);
            hud.SetStatus(points > 0 ? $"Splash!  +{points}" : "Splash!", WaveHUD.Bad);
            Play(splashClip);
            if (splash) StartCoroutine(SplashAt(OperatedLeg.HeelPosition));
        }
        score += points;
        hud.SetHold(-1f);
        hud.SetInfo($"Wave {index}/{wavesPerSession}  ·  Score {score}  ·  Streak {streak}");

        logger.LogWave(index, percentUsed, target, hold, minAngle, maxAngle, fraction, waveCleared,
                       points, score, sessionMaxAngle, safetyAlerts);

        // 4. Wave pulls back out to sea.
        currentTarget = -1f;
        yield return new WaitForSeconds(resultSeconds);
        for (float t = 0f; t < retreatSeconds; t += Time.deltaTime)
        {
            float k = t / retreatSeconds;
            SetWave(Mathf.Lerp(reachEdge, startEdge, k * k));
            yield return null;
        }
        SetWave(startEdge);
    }

    float TargetAngle()
    {
        float limit = Mathf.Min(comfortableMaxAngle, safetyCeilingAngle - 5f);
        return Mathf.Clamp(targetPercent * comfortableMaxAngle, 5f, limit);
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
        go.GetComponent<MeshFilter>().sharedMesh = BuildUpwardWaterMesh(waveTemplate);
        go.GetComponent<MeshRenderer>().sharedMaterials = waveTemplate.sharedMaterials;

        // Front (shore-side) edge and top of the water, relative to its pivot.
        Bounds b = go.GetComponent<MeshFilter>().sharedMesh.bounds;
        waveEdgeOffset = b.max.z;
        waveTopOffset = b.max.y;
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
        waveClip = Noise("WaveIn", 1.6f, 1.5f, 0.12f);
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
