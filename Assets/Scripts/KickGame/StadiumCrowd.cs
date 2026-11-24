using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A small stadium stand behind the goal, filled with the beach characters, watching the patient.
/// Spectators sit and sway, clap during the wind-up, jump up cheering on a goal and slump with their hands
/// on their heads on a miss. Challenge music plays in the background; cheers and "awww"s come from the stand.
///
/// Everything is built at runtime from primitives plus the character prefabs, so the scene only needs this
/// component with <see cref="spectatorPrefabs"/> assigned (the same prefabs BeachLife uses).
/// Real recordings can be dropped into the clip fields; empty fields use generated sounds.
/// </summary>
public class StadiumCrowd : MonoBehaviour
{
    public enum Mood { Watch, Clap, Cheer, Groan }

    [Header("Stand (world space; the patient faces -Z toward the goal)")]
    [Tooltip("Centre of the front edge of the stand")]
    public Vector3 frontCenter = new Vector3(0f, 0f, -3.5f);
    public float width = 22f;
    public int tiers = 4;
    public float tierDepth = 0.9f;
    public float tierRise = 0.5f;
    public float firstTierHeight = 0.45f;
    public bool roof = true;

    [Header("Spectators")]
    [Tooltip("Character prefabs with a BeachPersonRig (Dan, Leo, KidMia...)")]
    public GameObject[] spectatorPrefabs;
    public int spectators = 20;
    [Tooltip("Keep the lowest rows right behind the goal free, so the crowd doesn't clutter the targets")]
    public float clearBehindGoal = 4.2f;
    public int seed = 11;
    [Tooltip("Any lit material to copy for the stand (its texture is removed and it is recoloured)")]
    public Renderer materialSource;

    [Header("Audio (empty = generated)")]
    [Tooltip("Background music; empty = the generated match-day challenge track")]
    public AudioClip musicClip;
    public AudioClip cheerClip;
    public AudioClip groanClip;
    [Range(0f, 1f)] public float musicVolume = 0.35f;
    [Range(0f, 1f)] public float reactionVolume = 0.9f;

    static readonly Color Concrete = new Color(0.62f, 0.62f, 0.6f);
    static readonly Color[] SeatColors = { new Color(0.1f, 0.35f, 0.8f), new Color(0.95f, 0.95f, 0.95f), new Color(0.85f, 0.15f, 0.15f) };
    static readonly Color[] Boards = { new Color(0.95f, 0.75f, 0.1f), new Color(0.1f, 0.55f, 0.25f), new Color(0.1f, 0.35f, 0.8f), new Color(0.85f, 0.15f, 0.15f) };

    class Fan
    {
        public Transform root, model, armL, armR, legL, legR;
        public float hip, phase, delay, energy;
        public bool rigged;
    }

    readonly List<Fan> fans = new List<Fan>();
    Material baseMaterial;
    AudioSource music, reactions;
    Mood mood = Mood.Watch;
    float moodTime, moodUntil;

    public Mood CurrentMood => mood;

    void Start()
    {
        baseMaterial = materialSource ? materialSource.sharedMaterial : null;
        var rng = new System.Random(seed);
        BuildStand(rng);
        SeatFans(rng);
        SetupAudio();
    }

    // ---------------- Reactions (called by the game) ----------------

    /// <summary>Goal! Everyone jumps up, arms in the air.</summary>
    public void Cheer(float seconds = 3.5f) => React(Mood.Cheer, seconds, cheerClip);

    /// <summary>Miss or ball out: a disappointed "awww", hands on heads.</summary>
    public void Groan(float seconds = 2.5f) => React(Mood.Groan, seconds, groanClip);

    /// <summary>Rhythmic clapping to encourage the patient (until <see cref="Watch"/>).</summary>
    public void Clap() => SetMood(Mood.Clap, float.MaxValue);

    public void Watch() => SetMood(Mood.Watch, float.MaxValue);

    void React(Mood m, float seconds, AudioClip clip)
    {
        SetMood(m, seconds);
        foreach (var f in fans) f.delay = Random.Range(0f, 0.35f); // not perfectly in sync
        if (reactions && clip) reactions.PlayOneShot(clip, reactionVolume);
        if (music) StartCoroutine(DuckMusic(seconds));
    }

    void SetMood(Mood m, float seconds)
    {
        mood = m;
        moodTime = Time.time;
        moodUntil = Time.time + seconds;
    }

    IEnumerator DuckMusic(float seconds)
    {
        // Let the reaction sit on top, then bring the music back.
        music.volume = musicVolume * 0.4f;
        yield return new WaitForSeconds(seconds * 0.8f);
        for (float t = 0f; t < 1f; t += Time.deltaTime)
        {
            music.volume = Mathf.Lerp(musicVolume * 0.4f, musicVolume, t);
            yield return null;
        }
        music.volume = musicVolume;
    }

    // ---------------- Animation ----------------

    void Update()
    {
        if (Time.time > moodUntil && (mood == Mood.Cheer || mood == Mood.Groan)) SetMood(Mood.Watch, float.MaxValue);
        float now = Time.time;
        foreach (var f in fans) Animate(f, now);
    }

    void Animate(Fan f, float now)
    {
        float t = now - moodTime - f.delay;
        float active = mood == Mood.Cheer || mood == Mood.Groan ? Mathf.Clamp01(t / 0.25f) * Mathf.Clamp01((moodUntil - now) / 0.5f) : 0f;
        float sway = Mathf.Sin(now * 1.3f + f.phase);

        // Height: sitting on the bench, or standing up (and bouncing) when cheering.
        float sitY = -(f.hip - 0.02f), standY = -0.45f; // root is on the bench top; the bench is 0.45 m above the step
        float stand = mood == Mood.Cheer ? active : 0f;
        float bounce = mood == Mood.Cheer ? Mathf.Abs(Mathf.Sin(now * 7f * f.energy + f.phase)) * 0.22f * active : 0f;
        f.model.localPosition = new Vector3(0f, Mathf.Lerp(sitY, standY, stand) + bounce, Mathf.Lerp(0f, 0.25f, stand));

        // Lean: slumped forward on a groan, otherwise a small idle sway.
        float lean = mood == Mood.Groan ? 16f * active : 0f;
        f.model.localRotation = Quaternion.Euler(lean, sway * 3f, sway * 1.5f * (1f - stand));

        if (!f.rigged) return;

        // Legs: bent forward while sitting, straight when standing; they cancel the body's lean so feet stay on the step.
        Rot(f.legL, Quaternion.Euler(Mathf.Lerp(-75f, 0f, stand) - lean, 6f, 0f));
        Rot(f.legR, Quaternion.Euler(Mathf.Lerp(-75f, 0f, stand) - lean, -6f, 0f));

        switch (mood)
        {
            case Mood.Cheer:
                {
                    // Both arms high, waving.
                    float wave = Mathf.Sin(now * 9f * f.energy + f.phase) * 25f;
                    Rot(f.armL, Quaternion.Slerp(RestL(sway), Quaternion.Euler(0f, 0f, -160f + wave), active));
                    Rot(f.armR, Quaternion.Slerp(RestR(sway), Quaternion.Euler(0f, 0f, 160f + wave), active));
                    break;
                }
            case Mood.Groan:
                {
                    // Hands up onto the head, with a slow disbelieving head-shake sway.
                    float shake = Mathf.Sin(now * 5f + f.phase) * 4f;
                    Rot(f.armL, Quaternion.Slerp(RestL(sway), Quaternion.Euler(-168f, 0f, 6f + shake), active));
                    Rot(f.armR, Quaternion.Slerp(RestR(sway), Quaternion.Euler(-168f, 0f, -6f + shake), active));
                    break;
                }
            case Mood.Clap:
                {
                    // Forearms forward, hands meeting in front of the chest about twice a second.
                    float clap = Mathf.Abs(Mathf.Sin((now - moodTime) * 7f * f.energy + f.phase));
                    Rot(f.armL, Quaternion.Euler(-70f, 0f, Mathf.Lerp(-8f, 22f, clap)));
                    Rot(f.armR, Quaternion.Euler(-70f, 0f, Mathf.Lerp(8f, -22f, clap)));
                    break;
                }
            default:
                Rot(f.armL, RestL(sway));
                Rot(f.armR, RestR(sway));
                break;
        }
    }

    // Hands resting on the knees.
    static Quaternion RestL(float sway) => Quaternion.Euler(-45f + sway * 4f, 0f, 6f);
    static Quaternion RestR(float sway) => Quaternion.Euler(-45f - sway * 4f, 0f, -6f);

    static void Rot(Transform limb, Quaternion q)
    {
        if (limb) limb.localRotation = q;
    }

    // ---------------- Building the stand ----------------

    Transform standRoot;

    /// <summary>Front-left-bottom corner of tier i, in world space; tiers step back (-Z) and up.</summary>
    float TierZ(int i) => frontCenter.z - i * tierDepth;
    float TierTop(int i) => frontCenter.y + firstTierHeight + i * tierRise;

    void BuildStand(System.Random rng)
    {
        standRoot = new GameObject("Stand").transform;
        standRoot.SetParent(transform, false);

        for (int i = 0; i < tiers; i++)
        {
            float top = TierTop(i), zFront = TierZ(i);
            // Solid concrete step down to the ground.
            Box("Step" + i, new Vector3(frontCenter.x, top / 2f, zFront - tierDepth / 2f), new Vector3(width, top, tierDepth), Concrete);
            // Bench (seat) at the back of the step, and its backrest; each row a different colour.
            Color seat = SeatColors[i % SeatColors.Length];
            float benchZ = zFront - tierDepth + 0.3f;
            Box("Bench" + i, new Vector3(frontCenter.x, top + 0.42f, benchZ), new Vector3(width - 0.4f, 0.06f, 0.42f), seat);
            Box("BenchLegs" + i, new Vector3(frontCenter.x, top + 0.2f, benchZ), new Vector3(width - 0.6f, 0.4f, 0.3f), seat * 0.55f);
            Box("Backrest" + i, new Vector3(frontCenter.x, top + 0.68f, benchZ - 0.22f), new Vector3(width - 0.4f, 0.42f, 0.05f), seat);
        }

        float backZ = TierZ(tiers) - 0.1f, topH = TierTop(tiers - 1);
        // Back wall and side walls.
        Box("BackWall", new Vector3(frontCenter.x, (topH + 1.4f) / 2f, backZ), new Vector3(width + 0.4f, topH + 1.4f, 0.2f), Concrete * 0.9f);
        float depth = tiers * tierDepth;
        foreach (float side in new[] { -1f, 1f })
            Box("SideWall", new Vector3(frontCenter.x + side * (width / 2f + 0.1f), (topH + 1f) / 2f, frontCenter.z - depth / 2f),
                new Vector3(0.2f, topH + 1f, depth + 0.2f), Concrete * 0.9f);

        // Advertising boards along the front, alternating colours.
        int boards = Mathf.Max(1, Mathf.RoundToInt(width / 3f));
        float bw = width / boards;
        for (int b = 0; b < boards; b++)
        {
            float x = frontCenter.x - width / 2f + bw * (b + 0.5f);
            Box("Board" + b, new Vector3(x, 0.45f, frontCenter.z + 0.05f), new Vector3(bw - 0.08f, 0.9f, 0.08f), Boards[b % Boards.Length]);
        }

        if (roof)
        {
            float roofY = topH + 2.6f;
            Box("Roof", new Vector3(frontCenter.x, roofY, frontCenter.z - depth / 2f + 0.3f), new Vector3(width + 1f, 0.15f, depth + 1.4f), new Color(0.85f, 0.87f, 0.9f));
            for (int c = 0; c <= 4; c++)
            {
                float x = frontCenter.x - width / 2f + width * c / 4f;
                Box("Column" + c, new Vector3(x, roofY / 2f, backZ + 0.25f), new Vector3(0.18f, roofY, 0.18f), new Color(0.3f, 0.32f, 0.36f));
            }
            // Front edge trim in team blue.
            Box("RoofTrim", new Vector3(frontCenter.x, roofY - 0.1f, frontCenter.z + 0.95f), new Vector3(width + 1f, 0.35f, 0.1f), SeatColors[0]);
        }
    }

    void SeatFans(System.Random rng)
    {
        if (spectatorPrefabs == null || spectatorPrefabs.Length == 0)
        {
            Debug.LogWarning("[StadiumCrowd] No spectator prefabs assigned; the stand stays empty.");
            return;
        }

        // Every 0.75 m along every bench is a possible seat; pick the requested number at random.
        var seats = new List<Vector3>();
        float spacing = 0.75f;
        int perRow = Mathf.FloorToInt((width - 1f) / spacing);
        for (int i = 0; i < tiers; i++)
            for (int s = 0; s < perRow; s++)
            {
                float x = frontCenter.x - (perRow - 1) * spacing / 2f + s * spacing;
                if (i < 2 && Mathf.Abs(x - frontCenter.x) < clearBehindGoal) continue;
                seats.Add(new Vector3(x, TierTop(i) + 0.45f, TierZ(i) - tierDepth + 0.33f));
            }
        for (int i = seats.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (seats[i], seats[j]) = (seats[j], seats[i]);
        }

        var fansRoot = new GameObject("Spectators").transform;
        fansRoot.SetParent(transform, false);
        for (int i = 0; i < Mathf.Min(spectators, seats.Count); i++)
        {
            var prefab = spectatorPrefabs[i % spectatorPrefabs.Length];
            if (!prefab) continue;
            bool kid = prefab.name.StartsWith("Kid");
            var root = new GameObject("Fan_" + prefab.name).transform;
            root.SetParent(fansRoot, false);
            root.position = seats[i];
            root.rotation = Quaternion.Euler(0f, (float)(rng.NextDouble() * 16 - 8), 0f); // facing the pitch (+Z)
            root.localScale = Vector3.one * (kid ? 0.95f : 0.9f + (float)rng.NextDouble() * 0.15f);
            var model = new GameObject("Model").transform;
            model.SetParent(root, false);
            var inst = Instantiate(prefab, model);
            inst.transform.localPosition = Vector3.zero;
            inst.transform.localRotation = Quaternion.identity;
            foreach (var col in inst.GetComponentsInChildren<Collider>()) Destroy(col);

            var fan = new Fan
            {
                root = root, model = model, hip = 0.9f,
                phase = (float)rng.NextDouble() * 6.28f, energy = 0.85f + (float)rng.NextDouble() * 0.3f,
            };
            var rig = inst.GetComponentInChildren<BeachPersonRig>();
            if (rig)
            {
                fan.armL = rig.armLeft; fan.armR = rig.armRight; fan.legL = rig.legLeft; fan.legR = rig.legRight;
                fan.hip = rig.hipHeight;
                fan.rigged = fan.armL || fan.armR;
                if (rig.animator) rig.animator.enabled = false; // limbs are posed here
            }
            fans.Add(fan);
        }
    }

    GameObject Box(string name, Vector3 center, Vector3 size, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(standRoot, false);
        go.transform.position = center;
        go.transform.localScale = size;
        Paint(go.GetComponent<Renderer>(), color);
        go.isStatic = true;
        return go;
    }

    readonly Dictionary<Color, Material> materials = new Dictionary<Color, Material>();

    void Paint(Renderer r, Color color)
    {
        if (!materials.TryGetValue(color, out var m))
        {
            m = baseMaterial ? new Material(baseMaterial) : new Material(r.sharedMaterial);
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", null);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", null);
            if (m.HasProperty("_BumpMap")) m.SetTexture("_BumpMap", null);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            // glTF shader (the training-ground models) uses its own property names.
            foreach (var tex in new[] { "baseColorTexture", "normalTexture", "metallicRoughnessTexture", "occlusionTexture", "emissiveTexture" })
                if (m.HasProperty(tex)) m.SetTexture(tex, null);
            if (m.HasProperty("baseColorFactor")) m.SetColor("baseColorFactor", color);
            if (m.HasProperty("emissiveFactor")) m.SetColor("emissiveFactor", Color.black);
            if (m.HasProperty("roughnessFactor")) m.SetFloat("roughnessFactor", 0.85f);
            if (m.HasProperty("metallicFactor")) m.SetFloat("metallicFactor", 0f);
            materials[color] = m;
        }
        r.sharedMaterial = m;
    }

    // ---------------- Audio ----------------

    void SetupAudio()
    {
        if (!musicClip) musicClip = GeneratedAudio.ChallengeMusic("MatchDay");
        if (!cheerClip) cheerClip = GeneratedAudio.Crowd("CrowdCheer", true, 3.6f, 0.9f);
        if (!groanClip) groanClip = GeneratedAudio.Crowd("CrowdGroan", false, 2.4f, 0.8f);

        Vector3 pos = new Vector3(frontCenter.x, TierTop(tiers / 2) + 1f, frontCenter.z - tiers * tierDepth / 2f);
        music = Source("Music", pos, musicVolume);
        music.spatialBlend = 0f; // music is everywhere, not in the stand
        music.clip = musicClip;
        music.loop = true;
        music.Play();
        reactions = Source("Reactions", pos, 1f);
    }

    AudioSource Source(string name, Vector3 pos, float volume)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.position = pos;
        var src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.volume = volume;
        src.spatialBlend = 0.6f;   // heard from the stand, but still loud enough from the penalty spot
        src.minDistance = 12f;
        src.maxDistance = 60f;
        return src;
    }
}
