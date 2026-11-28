using System.Collections;
using UnityEngine;

/// <summary>
/// Fireworks behind the stadium stand, launched on every goal.
/// Each rocket whooshes up with a glowing trail from behind the stand, bursts into a sphere of coloured sparks
/// (with a white flash and a crackling glitter) and the sparks fall and fade. More goals in a row = more rockets:
/// 3 for a single goal, 4 for two in a row, 5 for three or more.
/// The sparks use the GP2/Spark shader through <see cref="sparkMaterial"/>; all sounds are generated in code.
/// </summary>
public class Fireworks : MonoBehaviour
{
    [Tooltip("Material with the GP2/Spark shader")]
    public Material sparkMaterial;

    [Header("Where (world space; the patient looks toward -Z)")]
    [Tooltip("Centre of the launch line behind the stand")]
    public Vector3 launchCenter = new Vector3(0f, 0.5f, -11f);
    [Tooltip("Rockets start anywhere along this width")]
    public float launchWidth = 18f;
    public Vector2 burstHeight = new Vector2(8.5f, 12.5f);

    [Header("Show")]
    public int baseRockets = 3;
    public int maxRockets = 5;
    [Tooltip("Sparks per burst")]
    public int sparks = 140;
    [Range(0f, 1f)] public float volume = 0.8f;

    static readonly Color[] Palette =
    {
        new Color(1f, 0.8f, 0.2f),   // gold
        new Color(1f, 0.25f, 0.25f), // red
        new Color(0.3f, 0.6f, 1f),   // blue
        new Color(0.35f, 1f, 0.45f), // green
        new Color(1f, 0.4f, 0.9f),   // pink
        new Color(1f, 1f, 1f),       // white
    };

    ParticleSystem trails, burst, glitter;
    AudioSource audioSource;
    AudioClip whoosh, bang, crackle;

    void Awake()
    {
        trails = MakeSystem("RocketTrails", 400, 0.35f, true);
        burst = MakeSystem("Bursts", 2000, 0.25f, true);
        glitter = MakeSystem("Glitter", 1200, 0f, false);

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0.4f; // from the stand, but still clear at the penalty spot
        audioSource.minDistance = 15f;
        audioSource.maxDistance = 80f;
        transform.position = launchCenter;
        whoosh = Whoosh();
        bang = Bang();
        crackle = Crackle();
    }

    /// <summary>Starts the show for a goal; <paramref name="streak"/> is the number of goals in a row (1 = first).</summary>
    public void Celebrate(int streak)
    {
        int rockets = Mathf.Clamp(baseRockets + Mathf.Max(0, streak - 1), baseRockets, maxRockets);
        StartCoroutine(Show(rockets));
    }

    IEnumerator Show(int rockets)
    {
        for (int i = 0; i < rockets; i++)
        {
            StartCoroutine(Rocket(i, rockets));
            yield return new WaitForSeconds(Random.Range(0.18f, 0.35f));
        }
    }

    IEnumerator Rocket(int index, int count)
    {
        // Spread the rockets across the width so they don't overlap; a little randomness keeps it lively.
        float slot = count > 1 ? (float)index / (count - 1) - 0.5f : 0f;
        Vector3 start = launchCenter + new Vector3(slot * launchWidth + Random.Range(-1.5f, 1.5f), 0f, Random.Range(-1.5f, 1.5f));
        Vector3 top = start + new Vector3(Random.Range(-1.5f, 1.5f), Random.Range(burstHeight.x, burstHeight.y), 0f);
        Color color = Palette[Random.Range(0, Palette.Length)];
        Color second = Palette[Random.Range(0, Palette.Length)];
        float rise = Random.Range(0.75f, 1f);

        audioSource.PlayOneShot(whoosh, volume * 0.5f);
        var p = new ParticleSystem.EmitParams { applyShapeToPosition = false };
        for (float t = 0f; t < rise; t += Time.deltaTime)
        {
            float k = t / rise;
            Vector3 pos = Vector3.Lerp(start, top, 1f - (1f - k) * (1f - k)); // fast off the ground, slowing at the top
            // A bright head and a few falling embers behind it.
            p.position = pos;
            p.velocity = Random.insideUnitSphere * 0.3f + Vector3.down * 0.5f;
            p.startColor = Color.Lerp(new Color(1f, 0.85f, 0.6f), color, 0.3f);
            p.startSize = 0.45f;
            p.startLifetime = 0.5f;
            trails.Emit(p, 2);
            yield return null;
        }

        // Burst: a sphere of sparks in one or two colours, a white flash at the centre, then glitter.
        audioSource.PlayOneShot(bang, volume);
        float speed = Random.Range(9f, 12f);
        for (int s = 0; s < sparks; s++)
        {
            p.position = top;
            p.velocity = Random.onUnitSphere * speed * Random.Range(0.85f, 1.05f);
            p.startColor = s % 3 == 0 ? second : color;
            p.startSize = Random.Range(0.4f, 0.6f);
            p.startLifetime = Random.Range(1.2f, 1.8f);
            burst.Emit(p, 1);
        }
        p.position = top;
        p.velocity = Vector3.zero;
        p.startColor = Color.white;
        p.startSize = 3.5f;
        p.startLifetime = 0.25f;
        glitter.Emit(p, 1);

        yield return new WaitForSeconds(0.5f);
        audioSource.PlayOneShot(crackle, volume * 0.6f);
        for (int s = 0; s < sparks / 2; s++)
        {
            p.position = top + Random.insideUnitSphere * speed * 0.7f;
            p.velocity = Vector3.down * Random.Range(0.3f, 1.2f);
            p.startColor = Color.Lerp(color, Color.white, 0.6f);
            p.startSize = Random.Range(0.2f, 0.32f);
            p.startLifetime = Random.Range(0.3f, 0.9f);
            glitter.Emit(p, 1);
        }
    }

    ParticleSystem MakeSystem(string name, int max, float trailLife, bool gravity)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.maxParticles = max;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = gravity ? 0.35f : 0f;

        var emission = ps.emission;
        emission.enabled = false;
        var shape = ps.shape;
        shape.enabled = false;

        // Sparks slow down quickly in the air, then drift down.
        var limit = ps.limitVelocityOverLifetime;
        limit.enabled = gravity;
        limit.limit = 0.8f;
        limit.dampen = 0.045f;

        var fade = ps.colorOverLifetime;
        fade.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.9f, 0.6f), new GradientAlphaKey(0f, 1f) });
        fade.color = g;

        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.3f));

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.sharedMaterial = sparkMaterial;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;

        if (trailLife > 0f)
        {
            // Streaks behind each spark.
            var tr = ps.trails;
            tr.enabled = true;
            tr.lifetime = trailLife;
            tr.minVertexDistance = 0.08f;
            tr.widthOverTrail = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
            tr.inheritParticleColor = true;
            tr.dieWithParticles = false;
            r.trailMaterial = sparkMaterial;
        }
        ps.Play();
        return ps;
    }

    // ---------------- Sounds (generated) ----------------

    const int Rate = 22050;

    /// <summary>Rising, hissing whistle of the rocket going up.</summary>
    static AudioClip Whoosh()
    {
        var d = new float[(int)(Rate * 0.9f)];
        var rng = new System.Random(11);
        float ph = 0f, lp = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            float k = (float)i / d.Length;
            float noise = (float)rng.NextDouble() * 2f - 1f;
            lp += (noise - lp) * Mathf.Lerp(0.15f, 0.6f, k);
            ph += Mathf.Lerp(900f, 2600f, k) / Rate;
            float env = Mathf.Sin(Mathf.PI * Mathf.Min(1f, k * 1.1f));
            d[i] = (lp * 0.7f + Mathf.Sin(2f * Mathf.PI * ph) * 0.12f) * env * 0.6f;
        }
        return Clip("FireworkWhoosh", d);
    }

    /// <summary>Deep bang with a sharp crack at the start.</summary>
    static AudioClip Bang()
    {
        var d = new float[(int)(Rate * 1.4f)];
        var rng = new System.Random(23);
        float lp = 0f, ph = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            float t = (float)i / Rate;
            float noise = (float)rng.NextDouble() * 2f - 1f;
            lp += (noise - lp) * 0.08f;
            ph += Mathf.Lerp(40f, 90f, Mathf.Exp(-t * 20f)) / Rate;
            float boom = Mathf.Sin(2f * Mathf.PI * ph) * Mathf.Exp(-t * 4f);
            float crack = noise * Mathf.Exp(-t * 60f);
            d[i] = (boom * 0.8f + lp * 2.5f * Mathf.Exp(-t * 3f) + crack * 0.6f) * 0.7f;
        }
        return Clip("FireworkBang", d);
    }

    /// <summary>Glittering crackle: many tiny random pops that thin out.</summary>
    static AudioClip Crackle()
    {
        var d = new float[(int)(Rate * 1.3f)];
        var rng = new System.Random(37);
        for (int pop = 0; pop < 160; pop++)
        {
            float at = (float)rng.NextDouble();
            at = at * at; // most pops early
            int start = (int)(at * (d.Length - 400));
            float amp = (float)(0.3 + rng.NextDouble() * 0.7) * (1f - at * 0.7f);
            for (int i = 0; i < 300; i++)
                d[start + i] += ((float)rng.NextDouble() * 2f - 1f) * Mathf.Exp(-i / 40f) * amp * 0.5f;
        }
        return Clip("FireworkCrackle", d);
    }

    static AudioClip Clip(string name, float[] d)
    {
        float peak = 1e-4f;
        foreach (var x in d) peak = Mathf.Max(peak, Mathf.Abs(x));
        for (int i = 0; i < d.Length; i++) d[i] *= 0.9f / peak;
        var c = AudioClip.Create(name, d.Length, 1, Rate, false);
        c.SetData(d, 0);
        return c;
    }
}
