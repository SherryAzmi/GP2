using System.Collections;
using UnityEngine;

/// <summary>
/// A breaking shore wave, built and animated in code.
///
/// One wave goes through the stages of a real one:
///   1. Swell  - a low crest rolls in across the sea, steepening and growing as it reaches shallow water.
///   2. Curl   - the crest leans forward, its lip turns white.
///   3. Crash  - the lip collapses into a foaming bore with a burst of spray (OnBreak fires here).
///   4. Swash  - a thin, fast sheet of foamy water rushes up the sand, slowing down to its furthest reach.
///   5. Hold   - the water's edge laps at that spot.
///   6. Backwash - the sheet drains back down the slope, leaving lacy foam that fades out.
///
/// The surface is a height-field grid laid out behind the moving front edge, so the whole profile travels with
/// it. Colour depth, foam and opacity are written per vertex and drawn by the GP2/ShoreWave shader.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class ShoreWave : MonoBehaviour
{
    [Header("Beach (world space; the sea is at lower Z)")]
    public float seaLevel = 0f;
    [Tooltip("Sand height at the patient's feet")]
    public float feetSandHeight = 0.072f;
    public float feetZ = -0.65f;
    [Tooltip("Metres down per metre toward the sea")]
    public float beachSlope = 0.08f;

    [Header("Shape")]
    public float width = 32f;
    [Tooltip("How far behind the front edge the water surface reaches before fading into the sea")]
    public float length = 7f;
    [Tooltip("Crest height just before it breaks (metres)")]
    public float maxCrestHeight = 0.6f;
    [Tooltip("Where it breaks, as metres in front of the patient's furthest-reaching water")]
    public float breakDistance = 1.9f;
    public int columns = 96, rows = 46;

    [Header("Colours")]
    public Color shallow = new Color(0.55f, 0.9f, 0.86f, 0.5f);
    public Color mid = new Color(0.13f, 0.64f, 0.7f, 0.82f);
    public Color deep = new Color(0.03f, 0.38f, 0.52f, 0.95f);
    [Tooltip("Sunlight through the thin lip of the curling crest")]
    public Color backlit = new Color(0.32f, 0.86f, 0.74f, 0.85f);

    public System.Action OnBreak;

    // Live shape state, driven by the stage coroutines.
    float frontZ, crest, curl, foamLip, foamFront, foamBody, flow, wobble, fade;

    Mesh mesh;
    Vector3[] verts;
    Color[] colors;
    Vector2[] data;
    float[] rowD;
    MeshRenderer meshRenderer;
    ParticleSystem spray;

    public bool Visible => meshRenderer && meshRenderer.enabled;

    // Wet-sand trail for the GP2/Sand shader: how far up the beach the water ran, and how wet that still is.
    static readonly int SwashZId = Shader.PropertyToID("_GP2_SwashZ"), SwashWetId = Shader.PropertyToID("_GP2_SwashWet");
    float swashMaxZ = -99f, swashWet;
    bool waterOnSand;
    [Tooltip("Seconds for sand the swash ran over to dry out")]
    public float drySeconds = 8f;

    void Update()
    {
        if (waterOnSand) swashWet = 1f;
        else if (swashWet > 0f && (swashWet -= Time.deltaTime / drySeconds) <= 0f) swashMaxZ = -99f;
        Shader.SetGlobalFloat(SwashZId, swashMaxZ);
        Shader.SetGlobalFloat(SwashWetId, Mathf.Clamp01(swashWet));
    }

    void OnDisable()
    {
        Shader.SetGlobalFloat(SwashWetId, 0f);
    }

    float SandY(float z) => feetSandHeight - beachSlope * Mathf.Max(0f, feetZ - z);
    float GroundY(float z) => Mathf.Max(seaLevel, SandY(z));

    void Awake()
    {
        meshRenderer = GetComponent<MeshRenderer>();
        BuildMesh();
        BuildSpray();
        meshRenderer.enabled = false;
    }

    // ---------------- Stages (driven by the game) ----------------

    /// <summary>Stages 1-4: from far out at sea to the furthest reach of the swash, over <paramref name="seconds"/>.</summary>
    public IEnumerator Approach(float startZ, float reachZ, float seconds)
    {
        bool broke = false;
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            float k = t / seconds;
            if (!broke && k >= BreakAt)
            {
                broke = true;
                BurstSpray(BreakZ(startZ, reachZ));
                OnBreak?.Invoke();
            }
            EvaluateApproach(k, startZ, reachZ);
            yield return null;
        }
        EvaluateApproach(1f, startZ, reachZ);
    }

    const float BreakAt = 0.6f, CrashEnd = 0.72f;

    float BreakZ(float startZ, float reachZ) => Mathf.Max(startZ + 2f, reachZ - breakDistance);

    /// <summary>Shows the approaching wave at progress <paramref name="k"/> (0 = far out, 1 = furthest reach).</summary>
    public void EvaluateApproach(float k, float startZ, float reachZ)
    {
        float breakZ = BreakZ(startZ, reachZ);
        float crashEndZ = breakZ + breakDistance * 0.45f;
        const float kBreak = BreakAt, kCrash = CrashEnd;
        meshRenderer.enabled = true;
        fade = 1f;
        {
            if (k < kBreak)
            {
                // Swell: steady roll in, the crest growing and steepening in the shallows.
                float u = k / kBreak;
                frontZ = Mathf.Lerp(startZ, breakZ, u * (0.75f + 0.25f * u));
                crest = Mathf.Lerp(0.1f, maxCrestHeight, Mathf.SmoothStep(0f, 1f, u));
                curl = Mathf.SmoothStep(0f, 1f, (u - 0.55f) / 0.45f);
                foamLip = Mathf.SmoothStep(0f, 1f, (u - 0.5f) / 0.5f) * 0.9f;
                foamFront = 0.05f;
                foamBody = 0.12f;
                flow = 0.2f;
                wobble = 0.3f;
            }
            else if (k < kCrash)
            {
                // Crash: the lip pitches forward and collapses into white water.
                float u = (k - kBreak) / (kCrash - kBreak);
                frontZ = Mathf.Lerp(breakZ, crashEndZ, u);
                crest = Mathf.Lerp(maxCrestHeight, 0.09f, Mathf.Pow(u, 0.6f));
                curl = Mathf.Lerp(1f, 1.4f, u) * (1f - u * u);
                foamLip = 1f;
                foamFront = Mathf.Lerp(0.3f, 1f, u);
                foamBody = Mathf.Lerp(0.15f, 0.85f, u);
                flow = 1f;
                wobble = Mathf.Lerp(0.3f, 0.8f, u);
            }
            else
            {
                // Swash: a thin foamy sheet rushes up the sand and runs out of energy.
                float u = (k - kCrash) / (1f - kCrash);
                frontZ = Mathf.Lerp(crashEndZ, reachZ, 1f - Mathf.Pow(1f - u, 2.6f));
                crest = Mathf.Lerp(0.09f, 0.018f, Mathf.SmoothStep(0f, 1f, u));
                curl = 0f;
                foamLip = 0f;
                foamFront = Mathf.Lerp(1f, 0.8f, u);
                foamBody = Mathf.Lerp(0.85f, 0.55f, u);
                flow = 1f - u;
                wobble = 1f;
            }
            UpdateMesh();
        }
    }

    /// <summary>Stage 5: the edge laps gently at <paramref name="reachZ"/>; call every frame with the time held so far.</summary>
    public void Hold(float reachZ, float t)
    {
        frontZ = reachZ + Mathf.Sin(t * 2.4f) * 0.025f - Mathf.Clamp01(t) * 0.01f;
        crest = 0.015f;
        curl = foamLip = 0f;
        foamFront = Mathf.Lerp(0.8f, 0.5f, Mathf.Clamp01(t / 2f));
        foamBody = Mathf.Lerp(0.55f, 0.32f, Mathf.Clamp01(t / 2f));
        flow = 0f;
        wobble = 1f;
        UpdateMesh();
    }

    /// <summary>Stage 6: the sheet drains back down the slope and fades out at sea.</summary>
    public IEnumerator Retreat(float reachZ, float endZ, float seconds)
    {
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            float k = t / seconds;
            frontZ = Mathf.Lerp(reachZ, endZ, k * k * (0.6f + 0.4f * k)); // slow at first, then drains faster
            crest = Mathf.Lerp(0.015f, 0.006f, k);
            foamFront = Mathf.Lerp(0.5f, 0.15f, k);
            foamBody = Mathf.Lerp(0.32f, 0.08f, k);
            flow = -0.8f;
            wobble = 1f;
            fade = 1f - Mathf.SmoothStep(0f, 1f, (k - 0.55f) / 0.45f);
            UpdateMesh();
            yield return null;
        }
        meshRenderer.enabled = false;
        waterOnSand = false;
    }

    // ---------------- Surface ----------------

    void BuildMesh()
    {
        int n = (columns + 1) * (rows + 1);
        verts = new Vector3[n];
        colors = new Color[n];
        data = new Vector2[n];
        var uv = new Vector2[n];
        rowD = new float[rows + 1];
        for (int j = 0; j <= rows; j++) rowD[j] = length * Mathf.Pow((float)j / rows, 1.8f); // dense near the front
        for (int i = 0; i < n; i++) uv[i] = new Vector2(0.5f, 0.5f);

        var tris = new int[columns * rows * 6];
        int ti = 0;
        for (int j = 0; j < rows; j++)
            for (int i = 0; i < columns; i++)
            {
                int a = j * (columns + 1) + i, b = a + 1, c = a + columns + 1, d = c + 1;
                tris[ti++] = a; tris[ti++] = b; tris[ti++] = c;
                tris[ti++] = b; tris[ti++] = d; tris[ti++] = c;
            }

        mesh = new Mesh { name = "ShoreWave" };
        mesh.MarkDynamic();
        mesh.vertices = verts;
        mesh.uv = uv;
        mesh.colors = colors;
        mesh.SetUVs(1, data);
        mesh.triangles = tris;
        GetComponent<MeshFilter>().sharedMesh = mesh;
        transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        transform.localScale = Vector3.one;
    }

    void UpdateMesh()
    {
        // The water's edge is up on the sand (above sea level) and still visible: it wets the sand there.
        waterOnSand = fade > 0.4f && SandY(frontZ) > seaLevel + 0.005f && meshRenderer.enabled;
        if (waterOnSand) swashMaxZ = Mathf.Max(swashMaxZ, frontZ);

        float time = Time.time;
        float half = width / 2f;
        for (int i = 0; i <= columns; i++)
        {
            float x = -half + width * i / columns;
            // An uneven front line, and a crest that is taller in some places than others.
            float edge = (Mathf.Sin(x * 0.8f + time * 0.6f) * 0.1f + Mathf.Sin(x * 2.1f - time * 1.1f) * 0.05f
                        + Mathf.Sin(x * 4.7f + time * 1.9f) * 0.03f) * wobble;
            float crestAlong = 1f + 0.16f * Mathf.Sin(x * 0.33f + time * 0.15f) + 0.08f * Mathf.Sin(x * 0.9f + 1.7f);
            float H = crest * crestAlong;
            float peakD = 0.18f + 0.95f * H;          // crest sits this far behind the front foot
            float backW = 0.7f + 2.6f * H;            // long gentle back slope
            float sideFade = Mathf.SmoothStep(0f, 1f, (half - Mathf.Abs(x)) / 4f);

            for (int j = 0; j <= rows; j++)
            {
                float d = rowD[j];
                // Height above the water/sand underneath: a concave front face up to the crest, a long back.
                float h = d < peakD
                    ? H * Mathf.Pow(d / peakD, 1.7f)
                    : H * (0.22f + 0.78f * Mathf.Exp(-Mathf.Pow((d - peakD) / backW, 2f)));
                // Even the thinnest swash has a film with a rounded front lip.
                float film = Mathf.Min(0.035f, crest * 1.4f + 0.006f) * (1f - Mathf.Exp(-d / 0.12f));
                h = Mathf.Max(h, film);

                float z = frontZ + edge - d;
                float rel = H > 0.001f ? Mathf.Clamp01(h / H) : 0f;
                // The curling lip leans forward over the face (stronger near the top).
                z += curl * H * 1.05f * rel * rel * rel * (d < peakD * 1.6f ? 1f : 0f);
                float ground = GroundY(z);
                float y = Mathf.Max(ground + h, SandY(z) + 0.004f);

                int v = j * (columns + 1) + i;
                verts[v] = new Vector3(x, y, z);

                // Colour by water thickness; sunlight shining through the thin top of a curling crest.
                float depth = y - SandY(z);
                Color c = Color.Lerp(shallow, mid, Mathf.Clamp01(depth / 0.14f));
                c = Color.Lerp(c, deep, Mathf.Clamp01((depth - 0.14f) / 0.6f));
                c = Color.Lerp(c, backlit, curl * rel * rel * 0.7f);
                float frontSoft = Mathf.Clamp01(d / 0.05f) * 0.5f + 0.5f;
                float backFade = 1f - Mathf.SmoothStep(0f, 1f, (d - length * 0.45f) / (length * 0.55f));
                c.a *= frontSoft * backFade * sideFade * fade;
                colors[v] = c;

                float foam = foamLip * Mathf.SmoothStep(0f, 1f, (rel - 0.72f) / 0.28f) * (d < peakD * 1.5f ? 1f : 0.3f)
                           + foamFront * Mathf.Exp(-d / 0.45f)
                           + foamBody * 0.75f * Mathf.Exp(-d / 2.2f) * (0.6f + 0.4f * Mathf.Sin(x * 1.3f + d * 2f));
                data[v] = new Vector2(Mathf.Clamp01(foam) * backFade * sideFade * fade, Mathf.Abs(flow));
            }
        }
        mesh.vertices = verts;
        mesh.colors = colors;
        mesh.SetUVs(1, data);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
    }

    // ---------------- Spray ----------------

    void BuildSpray()
    {
        var go = new GameObject("Spray");
        go.transform.SetParent(transform, false);
        go.transform.rotation = Quaternion.Euler(-50f, 0f, 0f); // emit up and toward the shore
        spray = go.AddComponent<ParticleSystem>();
        spray.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = spray.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = 1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.15f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.6f, 3.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.035f, 0.12f);
        main.startColor = new Color(0.96f, 0.99f, 1f, 0.9f);
        main.gravityModifier = 1f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 700;

        var emission = spray.emission;
        emission.rateOverTime = 0f;

        var shape = spray.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(width * 0.6f, 0.05f, 0.1f);
        shape.randomDirectionAmount = 0.25f;

        var col = spray.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.7f, 0.5f), new GradientAlphaKey(0f, 1f) });
        col.color = g;

        var size = spray.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.4f));

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.sharedMaterial = meshRenderer.sharedMaterial; // same shader: round, lit white droplets
    }

    void BurstSpray(float breakZ)
    {
        if (!spray) return;
        spray.transform.position = new Vector3(0f, GroundY(breakZ) + maxCrestHeight * 0.9f, breakZ - 0.3f);
        spray.Emit(420);
    }
}
