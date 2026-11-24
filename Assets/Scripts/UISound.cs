using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Interface sounds shared by both games, all generated in code:
///   Click()      button press            Tick()  countdown "3, 2, 1" beep
///   Toggle()     option switched         Go()    "go!" at the end of the countdown
///   Hold(f)      rising "loading" tone while a hold bar fills, then a ding when it is full
/// Lives on one hidden object that survives scene changes.
/// </summary>
public static class UISound
{
    static AudioSource oneShots, holdSource;
    static AudioClip click, toggle, tick, go, done, error, confirm;
    static float holdLast = -1f;
    static bool holdDinged;

    static void Init()
    {
        if (oneShots) return;
        var obj = new GameObject("UISound");
        obj.hideFlags = HideFlags.HideInHierarchy;
        Object.DontDestroyOnLoad(obj);
        oneShots = obj.AddComponent<AudioSource>();
        oneShots.playOnAwake = false;
        oneShots.spatialBlend = 0f;

        holdSource = obj.AddComponent<AudioSource>();
        holdSource.playOnAwake = false;
        holdSource.loop = true;
        holdSource.spatialBlend = 0f;
        holdSource.volume = 0f;
        holdSource.clip = LoopTone("HoldTone", 330f, 1f);

        click = Blip("Click", 1400f, 0.045f, 0.35f, 900f);
        toggle = Blip("Toggle", 950f, 0.06f, 0.3f, 1250f);
        tick = Blip("Tick", 880f, 0.16f, 0.45f, 880f);
        go = GeneratedAudio.Tone("Go", new[] { 1046f, 1568f }, 0.16f, 0.45f);
        done = GeneratedAudio.Tone("HoldDone", new[] { 1318f, 1975f }, 0.09f, 0.35f);
        error = GeneratedAudio.Tone("Error", new[] { 220f, 0f, 185f }, 0.09f, 0.4f);
        confirm = GeneratedAudio.Tone("Confirm", new[] { 784f, 1046f, 1318f }, 0.07f, 0.35f);
    }

    public static void Click() { Init(); oneShots.PlayOneShot(click); }
    public static void Toggle() { Init(); oneShots.PlayOneShot(toggle); }
    public static void Tick() { Init(); oneShots.PlayOneShot(tick); }
    public static void Go() { Init(); oneShots.PlayOneShot(go); }
    public static void Error() { Init(); oneShots.PlayOneShot(error); }
    public static void Confirm() { Init(); oneShots.PlayOneShot(confirm); }

    /// <summary>Adds the click sound to a button.</summary>
    public static void Attach(Button button, bool isToggle = false)
    {
        if (button) button.onClick.AddListener(isToggle ? (UnityEngine.Events.UnityAction)Toggle : Click);
    }

    /// <summary>
    /// Call every frame with the hold bar's fill (0..1), or a negative value when there is no hold bar.
    /// <paramref name="active"/> false (the patient is off target) fades the tone out without hiding it.
    /// The pitch climbs with the fill; reaching full plays a short ding once.
    /// </summary>
    public static void Hold(float fraction, bool active = true)
    {
        Init();
        if (fraction < 0f)
        {
            // Bar hidden: silence at once (no more calls will come to fade it out).
            holdSource.Stop();
            holdSource.volume = 0f;
            holdLast = -1f;
            return;
        }
        fraction = Mathf.Min(fraction, 1f);
        bool sounding = fraction > 0.01f && fraction < 0.999f && active;
        float target = sounding ? 0.22f : 0f;
        holdSource.volume = Mathf.MoveTowards(holdSource.volume, target, Time.unscaledDeltaTime * 2.5f);
        if (sounding)
        {
            holdSource.pitch = Mathf.Lerp(0.8f, 2.1f, fraction);
            if (!holdSource.isPlaying) holdSource.Play();
        }
        else if (holdSource.volume <= 0f && holdSource.isPlaying) holdSource.Stop();

        if (fraction >= 0.999f && holdLast < 0.999f && holdLast >= 0f && !holdDinged)
        {
            oneShots.PlayOneShot(done);
            holdDinged = true;
        }
        if (fraction < 0.5f) holdDinged = false;
        holdLast = fraction;
    }

    /// <summary>Short pitched blip that slides from <paramref name="from"/> to <paramref name="to"/> Hz.</summary>
    static AudioClip Blip(string name, float from, float seconds, float volume, float to)
    {
        const int rate = 44100;
        var data = new float[(int)(rate * seconds)];
        float ph = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            float k = (float)i / data.Length;
            ph += Mathf.Lerp(from, to, k) / rate;
            float env = Mathf.Min(1f, i / 120f) * Mathf.Pow(1f - k, 2f);
            data[i] = (Mathf.Sin(2f * Mathf.PI * ph) + 0.3f * Mathf.Sin(4f * Mathf.PI * ph)) * env * volume;
        }
        var clip = AudioClip.Create(name, data.Length, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    /// <summary>Soft seamless loop (a whole number of cycles), warm with a fifth and a gentle shimmer.</summary>
    static AudioClip LoopTone(string name, float hz, float seconds)
    {
        const int rate = 44100;
        int cycles = Mathf.RoundToInt(hz * seconds);
        int n = Mathf.RoundToInt(cycles * rate / hz);
        var data = new float[n];
        for (int i = 0; i < n; i++)
        {
            float p = (float)i / n * cycles;
            float shimmer = 1f + 0.15f * Mathf.Sin(2f * Mathf.PI * 4f * i / n); // 4 Hz tremolo, also whole cycles
            data[i] = (Mathf.Sin(2f * Mathf.PI * p) * 0.6f + Mathf.Sin(2f * Mathf.PI * p * 1.5f) * 0.25f
                     + Mathf.Sin(2f * Mathf.PI * p * 2f) * 0.15f) * 0.5f * shimmer;
        }
        var clip = AudioClip.Create(name, n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
