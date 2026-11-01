using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Brings the beach to life: looping ambience (sea, birds, crowd), and people who play catch, walk the
/// shoreline, paddle in the shallows, wave, sunbathe, sit on the sand and build sandcastles.
///
/// People are placed by the editable <see cref="spots"/> list. Until prefabs are assigned to
/// <see cref="personPrefabs"/> they are simple placeholder figures built from primitives; to switch to
/// realistic models, drop prefabs (optionally carrying a <see cref="BeachPersonRig"/>) into that array.
/// </summary>
public class BeachLife : MonoBehaviour
{
    public enum Activity { Throw, Walk, Splash, Wave, Sunbathe, Sit, BuildCastle }

    [System.Serializable]
    public class Spot
    {
        public Activity activity;
        public Vector3 position;
        [Tooltip("Walk: the other end of the path")]
        public Vector3 walkTo;
        public float yaw;
        [Tooltip("1 = adult, ~0.6 = child")]
        public float size = 1f;
        [Tooltip("Walking speed, or a timing offset for other activities")]
        public float speed = 1f;
    }

    [Header("Audio")]
    public AudioClip ambience;
    public AudioClip birds;
    public AudioClip crowd;
    [Range(0f, 1f)] public float ambienceVolume = 0.35f;
    [Range(0f, 1f)] public float birdsVolume = 0.4f;
    [Range(0f, 1f)] public float crowdVolume = 0.6f;

    [Header("People")]
    [Tooltip("Realistic character prefabs. Leave empty to use the placeholder figures")]
    public GameObject[] personPrefabs;
    public List<Spot> spots = DefaultSpots();
    [Tooltip("Sand height under the patient's feet")]
    public float feetSandHeight = 0.072f;
    [Tooltip("Beach slope toward the sea (metres down per metre)")]
    public float beachSlope = 0.08f;
    [Tooltip("Z of the patient's feet; the sea is at lower Z")]
    public float feetZ = -0.65f;
    [Tooltip("Existing object whose material is copied for the placeholder people (any lit material)")]
    public Renderer materialSource;
    public Color sandColor = new Color(0.92f, 0.8f, 0.55f);
    public int seed = 7;

    static readonly Color[] Skin =
    {
        new Color(0.96f, 0.80f, 0.69f), new Color(0.85f, 0.65f, 0.48f),
        new Color(0.66f, 0.46f, 0.33f), new Color(0.45f, 0.30f, 0.22f),
    };
    static readonly Color[] Cloth =
    {
        new Color(0.9f, 0.25f, 0.25f), new Color(0.2f, 0.55f, 0.9f), new Color(0.95f, 0.8f, 0.2f),
        new Color(0.3f, 0.75f, 0.45f), new Color(0.95f, 0.5f, 0.75f), new Color(0.95f, 0.95f, 0.95f),
    };

    readonly List<Person> people = new List<Person>();
    readonly List<Seagull> gulls = new List<Seagull>();
    readonly List<Castle> castles = new List<Castle>();
    Material baseMaterial;
    Transform ball;
    Person throwerA, throwerB;
    float ballT;

    class Person
    {
        public Spot spot;
        public Transform root, model, armL, armR, legL, legR;
        public Animator animator;
        public bool procedural = true;
        public Vector3 home;
        public float phase;
        public float hipHeight = 0.72f;
    }

    class Castle { public Transform mound; public float phase; }
    class Seagull { public Transform t; public Vector3 center; public float radius, speed, phase; }

    /// <summary>The initial cast. Edit the list on the component afterwards to move people around.</summary>
    static List<Spot> DefaultSpots() => new List<Spot>
    {
        // In the shallows in front of the patient.
        new Spot { activity = Activity.Throw, position = new Vector3(-5f, 0, -3.2f), yaw = 90f },
        new Spot { activity = Activity.Throw, position = new Vector3(-2.4f, 0, -3.2f), yaw = -90f, speed = 1f },
        new Spot { activity = Activity.Walk, position = new Vector3(-14f, 0, -6f), walkTo = new Vector3(14f, 0, -6f), speed = 0.9f },
        new Spot { activity = Activity.Walk, position = new Vector3(16f, 0, -8f), walkTo = new Vector3(-10f, 0, -8f), speed = 0.7f },
        new Spot { activity = Activity.Splash, position = new Vector3(3.8f, 0, -3.6f), yaw = 180f, size = 0.6f },
        new Spot { activity = Activity.Splash, position = new Vector3(5.1f, 0, -4f), yaw = 160f, size = 0.6f, speed = 2f },
        new Spot { activity = Activity.Wave, position = new Vector3(7f, 0, -5.5f), yaw = 200f },

        // On the sand strip: a family on each side, a parent sitting and a child building a sandcastle.
        new Spot { activity = Activity.Sit, position = new Vector3(-3.1f, 0, -0.8f), yaw = 180f },
        new Spot { activity = Activity.BuildCastle, position = new Vector3(-2.1f, 0, -0.7f), yaw = 180f, size = 0.6f },
        new Spot { activity = Activity.Sit, position = new Vector3(3.0f, 0, -0.8f), yaw = 170f },
        new Spot { activity = Activity.BuildCastle, position = new Vector3(3.9f, 0, -0.7f), yaw = 190f, size = 0.55f, speed = 3f },

        // Behind the patient, out of view unless they turn around.
        new Spot { activity = Activity.Sunbathe, position = new Vector3(9.5f, 0, 1.5f), yaw = 20f },
        new Spot { activity = Activity.Sunbathe, position = new Vector3(11f, 0, 0.6f), yaw = -15f },
    };

    float SandY(float z) => feetSandHeight - beachSlope * Mathf.Max(0f, feetZ - z);

    void Start()
    {
        baseMaterial = materialSource ? materialSource.sharedMaterial : null;
        SetupAudio();
        var rng = new System.Random(seed);

        foreach (var spot in spots)
        {
            var p = Spawn(spot, rng);
            if (spot.activity != Activity.Throw) continue;
            if (throwerA == null) throwerA = p; else if (throwerB == null) throwerB = p;
        }
        if (throwerA != null && throwerB != null) MakeBall();

        foreach (var go in FindSeagulls()) gulls.Add(new Seagull
        {
            t = go.transform, center = go.transform.position,
            radius = Random.Range(1.5f, 3.5f), speed = Random.Range(0.3f, 0.6f), phase = Random.Range(0f, 6.28f),
        });
    }

    void Update()
    {
        float t = Time.time;
        foreach (var p in people) Animate(p, t);
        foreach (var c in castles) GrowCastle(c, t);
        UpdateBall();
        foreach (var g in gulls)
        {
            float a = t * g.speed + g.phase;
            Vector3 pos = g.center + new Vector3(Mathf.Cos(a) * g.radius, Mathf.Sin(a * 2f) * 0.3f, Mathf.Sin(a) * g.radius);
            Vector3 vel = pos - g.t.position;
            g.t.position = pos;
            if (vel.sqrMagnitude > 1e-8f)
                g.t.rotation = Quaternion.Slerp(g.t.rotation, Quaternion.LookRotation(vel.normalized), Time.deltaTime * 3f);
        }
    }

    // ---------------- Audio ----------------

    void SetupAudio()
    {
        MakeSource("Ambience", ambience, ambienceVolume, 0f, transform.position);
        MakeSource("Birds", birds, birdsVolume, 0.3f, new Vector3(-3f, 5f, 3f));
        // The crowd sits where the people are, so it is heard from the side, not inside the head.
        MakeSource("Crowd", crowd, crowdVolume, 1f, new Vector3(-2f, 1f, -4f));
    }

    void MakeSource(string name, AudioClip clip, float volume, float spatial, Vector3 pos)
    {
        if (!clip) return;
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.position = pos;
        var src = go.AddComponent<AudioSource>();
        src.clip = clip;
        src.loop = true;
        src.volume = volume;
        src.spatialBlend = spatial;
        src.minDistance = 6f;
        src.maxDistance = 40f;
        src.Play();
    }

    // ---------------- Spawning ----------------

    Person Spawn(Spot s, System.Random rng)
    {
        Vector3 pos = s.position;
        pos.y = SandY(pos.z);

        var root = new GameObject(s.activity + "_Person").transform;
        root.SetParent(transform, false);
        root.position = pos;
        root.rotation = Quaternion.Euler(0, s.yaw, 0);
        root.localScale = Vector3.one * s.size;

        var p = new Person { spot = s, root = root, home = pos, phase = (float)rng.NextDouble() * 6f + s.speed };
        var model = new GameObject("Model").transform;
        model.SetParent(root, false);
        p.model = model;

        if (personPrefabs != null && personPrefabs.Length > 0)
            BuildFromPrefab(p, personPrefabs[rng.Next(personPrefabs.Length)]);
        else
            BuildPlaceholder(p, rng);

        ApplyPose(p);
        if (s.activity == Activity.BuildCastle) BuildCastle(p, rng);
        people.Add(p);
        return p;
    }

    void BuildFromPrefab(Person p, GameObject prefab)
    {
        var instance = Instantiate(prefab, p.model);
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        var rig = instance.GetComponentInChildren<BeachPersonRig>();
        if (!rig) return;
        p.armL = rig.armLeft; p.armR = rig.armRight; p.legL = rig.legLeft; p.legR = rig.legRight;
        p.hipHeight = rig.hipHeight;
        p.animator = rig.animator;
        if (p.animator) p.procedural = false;
    }

    void BuildPlaceholder(Person p, System.Random rng)
    {
        Color skin = Skin[rng.Next(Skin.Length)];
        Color top = Cloth[rng.Next(Cloth.Length)];
        Color shorts = Cloth[rng.Next(Cloth.Length)];
        Part(p.model, PrimitiveType.Capsule, new Vector3(0, 1.05f, 0), new Vector3(0.36f, 0.28f, 0.22f), top);
        Part(p.model, PrimitiveType.Sphere, new Vector3(0, 1.58f, 0), Vector3.one * 0.22f, skin);
        p.armL = Limb(p.model, new Vector3(-0.24f, 1.32f, 0), 0.5f, 0.09f, skin);
        p.armR = Limb(p.model, new Vector3(0.24f, 1.32f, 0), 0.5f, 0.09f, skin);
        p.legL = Limb(p.model, new Vector3(-0.1f, 0.72f, 0), 0.72f, 0.11f, shorts);
        p.legR = Limb(p.model, new Vector3(0.1f, 0.72f, 0), 0.72f, 0.11f, shorts);
    }

    /// <summary>Static part of each pose: lying down, or lowered onto the sand with legs stretched out.</summary>
    void ApplyPose(Person p)
    {
        switch (p.spot.activity)
        {
            case Activity.Sunbathe:
                p.root.rotation = Quaternion.Euler(-90f, p.spot.yaw, 0f);
                p.root.position += Vector3.up * 0.14f * p.spot.size;
                p.home = p.root.position;
                break;
            case Activity.Sit:
            case Activity.BuildCastle:
                p.model.localPosition = new Vector3(0, -(p.hipHeight - 0.08f), 0);
                if (p.procedural)
                {
                    Rot(p.legL, Quaternion.Euler(-80f, 8f, 0));
                    Rot(p.legR, Quaternion.Euler(-80f, -8f, 0));
                }
                break;
        }
        string state = p.spot.activity.ToString();
        if (p.animator && p.animator.HasState(0, Animator.StringToHash(state))) p.animator.CrossFade(state, 0.2f);
    }

    // ---------------- Animation ----------------

    void Animate(Person p, float t)
    {
        switch (p.spot.activity)
        {
            case Activity.Throw:
                if (p.procedural)
                {
                    var arms = Quaternion.Euler(-100f - Mathf.Sin(t * 3f + p.phase * 3f) * 25f, 0, 0);
                    Rot(p.armL, arms); Rot(p.armR, arms);
                }
                break;
            case Activity.Splash:
                float hop = Mathf.Max(0f, Mathf.Sin(t * 4f + p.phase));
                p.root.position = p.home + Vector3.up * hop * 0.25f * p.spot.size;
                if (p.procedural)
                {
                    Rot(p.armL, Quaternion.Euler(0, 0, -150f * hop));
                    Rot(p.armR, Quaternion.Euler(0, 0, 150f * hop));
                }
                break;
            case Activity.Wave:
                if (p.procedural) Rot(p.armR, Quaternion.Euler(0, 0, 160f + Mathf.Sin(t * 6f) * 20f));
                break;
            case Activity.Sunbathe:
                p.root.position = p.home + Vector3.up * Mathf.Sin(t * 1.2f + p.phase) * 0.008f;
                break;
            case Activity.Walk:
                Walk(p, t);
                break;
            case Activity.Sit:
                // Gentle sway, hands resting on the sand.
                if (p.procedural)
                {
                    Rot(p.armL, Quaternion.Euler(-40f + Mathf.Sin(t * 0.8f + p.phase) * 6f, 0, 8f));
                    Rot(p.armR, Quaternion.Euler(-40f - Mathf.Sin(t * 0.8f + p.phase) * 6f, 0, -8f));
                }
                break;
            case Activity.BuildCastle:
                // Scooping and patting the sand with alternating hands.
                if (p.procedural)
                {
                    float scoop = Mathf.Sin(t * 3.2f + p.phase);
                    Rot(p.armL, Quaternion.Euler(-70f - scoop * 35f, 0, 5f));
                    Rot(p.armR, Quaternion.Euler(-70f + scoop * 35f, 0, -5f));
                }
                break;
        }
    }

    void Walk(Person p, float t)
    {
        float rate = p.spot.speed * 0.05f;
        float k = Mathf.PingPong(t * rate + p.phase, 1f);
        bool outbound = Mathf.Repeat(t * rate + p.phase, 2f) < 1f;
        Vector3 a = p.spot.position, b = p.spot.walkTo;
        Vector3 pos = Vector3.Lerp(a, b, k);
        float stride = t * 5f * p.spot.speed;
        pos.y = SandY(pos.z) + Mathf.Abs(Mathf.Sin(stride)) * 0.03f;
        p.root.position = pos;
        Vector3 dir = outbound ? b - a : a - b;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.0001f) p.root.rotation = Quaternion.LookRotation(dir);
        if (p.procedural)
        {
            float swing = Mathf.Sin(stride) * 28f;
            Rot(p.legL, Quaternion.Euler(swing, 0, 0)); Rot(p.legR, Quaternion.Euler(-swing, 0, 0));
            Rot(p.armL, Quaternion.Euler(-swing * 0.8f, 0, 0)); Rot(p.armR, Quaternion.Euler(swing * 0.8f, 0, 0));
        }
    }

    // ---------------- Sandcastle (independent of the person model) ----------------

    void BuildCastle(Person p, System.Random rng)
    {
        // The castle sits in front of the builder, whatever model they use.
        var root = new GameObject("SandCastle").transform;
        root.SetParent(transform, false);
        root.position = p.home + p.root.forward * 0.55f * p.spot.size;
        root.rotation = p.root.rotation;

        var mound = new GameObject("Mound").transform;
        mound.SetParent(root, false);
        Part(mound, PrimitiveType.Cylinder, new Vector3(0, 0.05f, 0), new Vector3(0.5f, 0.05f, 0.5f), sandColor);
        Part(mound, PrimitiveType.Cylinder, new Vector3(0, 0.15f, 0), new Vector3(0.34f, 0.07f, 0.34f), sandColor);
        Part(mound, PrimitiveType.Cylinder, new Vector3(0, 0.27f, 0), new Vector3(0.18f, 0.06f, 0.18f), sandColor);

        // A bucket and spade beside it.
        Part(root, PrimitiveType.Cylinder, new Vector3(0.35f, 0.08f, 0.05f), new Vector3(0.12f, 0.08f, 0.12f), Cloth[rng.Next(Cloth.Length)]);
        var spade = Part(root, PrimitiveType.Cube, new Vector3(-0.35f, 0.02f, 0.1f), new Vector3(0.06f, 0.015f, 0.2f), Cloth[rng.Next(Cloth.Length)]);
        spade.transform.localRotation = Quaternion.Euler(0, 25f, 0);

        castles.Add(new Castle { mound = mound, phase = p.phase });
    }

    /// <summary>The castle slowly rises while the child works, then restarts so it never looks finished.</summary>
    void GrowCastle(Castle c, float t)
    {
        float k = Mathf.Repeat(t * 0.03f + c.phase * 0.1f, 1f);
        float grow = Mathf.SmoothStep(0.55f, 1f, Mathf.Min(1f, k * 1.5f));
        c.mound.localScale = new Vector3(1f, grow, 1f);
    }

    // ---------------- Ball ----------------

    void MakeBall()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(go.GetComponent<Collider>());
        go.name = "TossBall";
        go.transform.SetParent(transform, false);
        go.transform.localScale = Vector3.one * 0.3f;
        Paint(go.GetComponent<Renderer>(), new Color(1f, 0.35f, 0.3f));
        ball = go.transform;
    }

    void UpdateBall()
    {
        if (!ball || throwerA == null || throwerB == null) return;
        ballT += Time.deltaTime * 0.45f;
        float leg = Mathf.Repeat(ballT, 1f);
        bool toB = Mathf.Repeat(ballT, 2f) < 1f;
        Vector3 from = (toB ? throwerA : throwerB).home + Vector3.up * 1.3f;
        Vector3 to = (toB ? throwerB : throwerA).home + Vector3.up * 1.3f;
        Vector3 pos = Vector3.Lerp(from, to, leg);
        pos.y += Mathf.Sin(leg * Mathf.PI) * 1.4f;
        ball.position = pos;
        ball.Rotate(Vector3.right, 240f * Time.deltaTime);
    }

    // ---------------- Helpers ----------------

    static void Rot(Transform limb, Quaternion rotation)
    {
        if (limb) limb.localRotation = rotation;
    }

    Transform Limb(Transform parent, Vector3 shoulder, float length, float width, Color color)
    {
        var pivot = new GameObject("Limb").transform;
        pivot.SetParent(parent, false);
        pivot.localPosition = shoulder;
        Part(pivot, PrimitiveType.Capsule, new Vector3(0, -length * 0.5f, 0), new Vector3(width * 2f, length * 0.5f, width * 2f), color);
        return pivot;
    }

    GameObject Part(Transform parent, PrimitiveType type, Vector3 localPos, Vector3 scale, Color color)
    {
        var go = GameObject.CreatePrimitive(type);
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = scale;
        Paint(go.GetComponent<Renderer>(), color);
        return go;
    }

    void Paint(Renderer r, Color color)
    {
        if (baseMaterial)
        {
            var m = new Material(baseMaterial);
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", null);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", null);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            r.sharedMaterial = m;
        }
        else r.material.color = color;
    }

    static IEnumerable<GameObject> FindSeagulls()
    {
        foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
            if (t.name.StartsWith("Seagull") && t.parent && !t.parent.name.StartsWith("Seagull")) yield return t.gameObject;
    }
}
