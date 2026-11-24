using UnityEngine;

/// <summary>
/// Game sounds made in code (tones and soft noise bursts), so the games need no audio files.
/// </summary>
public static class GeneratedAudio
{
    const int Rate = 44100;

    /// <summary>A short sequence of sine notes; a note of 0 Hz is a pause.</summary>
    public static AudioClip Tone(string name, float[] notes, float noteSeconds, float volume)
    {
        int perNote = (int)(Rate * noteSeconds);
        var data = new float[perNote * notes.Length];
        for (int n = 0; n < notes.Length; n++)
            for (int i = 0; i < perNote; i++)
            {
                float env = Mathf.Min(1f, i / 400f) * (1f - (float)i / perNote);
                data[n * perNote + i] = notes[n] > 0f ? Mathf.Sin(2f * Mathf.PI * notes[n] * i / Rate) * env * volume : 0f;
            }
        var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    /// <summary>Filtered noise with a fast attack and exponential decay (splash, wave, thump).</summary>
    public static AudioClip Noise(string name, float seconds, float decay, float volume)
    {
        var data = new float[(int)(Rate * seconds)];
        var rng = new System.Random(name.GetHashCode());
        float smooth = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            float t = (float)i / data.Length;
            float env = Mathf.Min(1f, t * 8f) * Mathf.Exp(-decay * t);
            smooth = Mathf.Lerp(smooth, (float)rng.NextDouble() * 2f - 1f, 0.25f); // soft, wash-like noise
            data[i] = smooth * env * volume;
        }
        var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // ---------------- Stadium sounds ----------------

    const int CrowdRate = 22050; // crowd sounds have little above 8 kHz, so half rate halves the build time

    /// <summary>Two-pole resonant band-pass (state-variable filter), one sample at a time.</summary>
    struct BandPass
    {
        float low, band, f, q;
        public BandPass(float hz, float damping, int rate)
        {
            low = band = 0f;
            f = 2f * Mathf.Sin(Mathf.PI * Mathf.Min(hz, rate * 0.2f) / rate);
            q = damping;
        }
        public void Tune(float hz, int rate) => f = 2f * Mathf.Sin(Mathf.PI * Mathf.Min(hz, rate * 0.2f) / rate);
        public float Run(float x)
        {
            low += f * band;
            float high = x - low - q * band;
            band += f * high;
            return band;
        }
    }

    /// <summary>
    /// A crowd of voices. Each voice is a buzzy pulse at its own pitch with vibrato and a random onset;
    /// the mix goes through two vowel formants and is layered with a breathy roar.
    /// <paramref name="cheer"/> true: rising "YEAAH!" with whoops and whistles. False: a falling "awww".
    /// </summary>
    public static AudioClip Crowd(string name, bool cheer, float seconds, float volume, int voices = 36)
    {
        int n = (int)(CrowdRate * seconds);
        var data = new float[n];
        var rng = new System.Random(name.GetHashCode());
        float R() => (float)rng.NextDouble();

        for (int v = 0; v < voices; v++)
        {
            float f0 = cheer ? Mathf.Lerp(170f, 420f, R()) : Mathf.Lerp(130f, 300f, R());
            float onset = R() * (cheer ? 0.25f : 0.35f) * seconds;
            float length = seconds - onset;
            float vibRate = 4f + R() * 3f, vibDepth = 0.01f + R() * 0.02f;
            float gain = 0.5f + R() * 0.5f;
            float phase = R();
            int start = (int)(onset * CrowdRate);
            for (int i = start; i < n; i++)
            {
                float t = (i - start) / (float)CrowdRate;
                float k = t / length;
                // Cheer: pitch jumps up and stays excited; groan: slides down a third.
                float glide = cheer ? 1f + 0.18f * Mathf.Min(1f, k * 6f) : 1f - 0.28f * Mathf.SmoothStep(0f, 1f, k);
                float f = f0 * glide * (1f + vibDepth * Mathf.Sin(2f * Mathf.PI * vibRate * t));
                phase += f / CrowdRate;
                phase -= Mathf.Floor(phase);
                float pulse = phase < 0.3f ? 1f : -0.43f; // buzzy glottal-ish pulse
                float env = Mathf.Min(1f, t / 0.06f) * (cheer ? Mathf.Pow(1f - k, 0.7f) : Mathf.Pow(1f - k, 1.3f));
                data[i] += pulse * env * gain;
            }
        }

        // Vowel formants: "ah" (cheer) or "ah" sliding to "oh" (groan); plus breathy noise.
        var f1 = new BandPass(800f, 0.35f, CrowdRate);
        var f2 = new BandPass(1250f, 0.4f, CrowdRate);
        var air = new BandPass(2500f, 0.9f, CrowdRate);
        float peak = 1e-4f;
        for (int i = 0; i < n; i++)
        {
            float k = (float)i / n;
            if (!cheer && (i & 63) == 0)
            {
                f1.Tune(Mathf.Lerp(800f, 520f, k), CrowdRate);
                f2.Tune(Mathf.Lerp(1250f, 880f, k), CrowdRate);
            }
            float env = Mathf.Min(1f, k * (cheer ? 12f : 6f)) * (cheer ? Mathf.Pow(1f - k, 0.8f) : Mathf.Pow(1f - k, 1.2f));
            float voice = f1.Run(data[i]) + 0.6f * f2.Run(data[i]);
            float roar = air.Run(R() * 2f - 1f) * env * (cheer ? 2.5f : 1.2f);
            data[i] = voice + roar;
            peak = Mathf.Max(peak, Mathf.Abs(data[i]));
        }

        if (cheer)
        {
            // A few whoops and finger whistles on top.
            for (int w = 0; w < 5; w++)
            {
                float at = 0.1f * seconds + R() * 0.5f * seconds, len = 0.35f + R() * 0.4f;
                float from = 1800f + R() * 900f, to = from * (R() < 0.5f ? 1.35f : 0.8f);
                float ph = 0f;
                for (int i = (int)(at * CrowdRate); i < Mathf.Min(n, (int)((at + len) * CrowdRate)); i++)
                {
                    float k = (i - at * CrowdRate) / (len * CrowdRate);
                    ph += Mathf.Lerp(from, to, k) / CrowdRate;
                    data[i] += Mathf.Sin(2f * Mathf.PI * ph) * Mathf.Sin(Mathf.PI * k) * peak * 0.18f;
                }
            }
        }

        Normalize(data, volume);
        var clip = AudioClip.Create(name, n, 1, CrowdRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    /// <summary>Seamless looping stadium murmur: low chatter with slow swells.</summary>
    public static AudioClip Murmur(string name, float seconds, float volume)
    {
        int n = (int)(CrowdRate * seconds);
        var data = new float[n];
        var rng = new System.Random(name.GetHashCode());
        var f1 = new BandPass(500f, 0.5f, CrowdRate);
        var f2 = new BandPass(1100f, 0.6f, CrowdRate);
        // Swell rates are whole cycles per clip, so the loop point is invisible.
        int c1 = Mathf.Max(1, Mathf.RoundToInt(seconds * 0.3f)), c2 = Mathf.Max(1, Mathf.RoundToInt(seconds * 0.9f));
        for (int i = 0; i < n; i++)
        {
            float k = (float)i / n;
            float x = (float)rng.NextDouble() * 2f - 1f;
            float swell = 0.75f + 0.15f * Mathf.Sin(2f * Mathf.PI * c1 * k) + 0.1f * Mathf.Sin(2f * Mathf.PI * c2 * k + 1.3f);
            data[i] = (f1.Run(x) + 0.5f * f2.Run(x)) * swell;
        }
        // Crossfade the tail into the head to hide the filter warm-up at the loop seam.
        int fade = CrowdRate / 2;
        for (int i = 0; i < fade; i++)
        {
            float a = (float)i / fade;
            data[i] = data[i] * a + data[n - fade + i] * (1f - a);
        }
        System.Array.Resize(ref data, n - fade);
        Normalize(data, volume);
        var clip = AudioClip.Create(name, data.Length, 1, CrowdRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    /// <summary>Referee pea whistle: a bright tone with the pea's fast warble. <paramref name="blasts"/> as seconds, 0 = gap.</summary>
    public static AudioClip Whistle(string name, float[] blasts, float volume)
    {
        float total = 0f;
        foreach (var b in blasts) total += Mathf.Abs(b) + 0.08f;
        var data = new float[(int)(Rate * total)];
        var rng = new System.Random(name.GetHashCode());
        int at = 0;
        float ph = 0f;
        foreach (var b in blasts)
        {
            int len = (int)(Rate * b);
            for (int i = 0; i < len && at + i < data.Length; i++)
            {
                float t = (float)i / Rate;
                float env = Mathf.Min(1f, t / 0.015f) * Mathf.Min(1f, (len - i) / (Rate * 0.04f));
                float warble = Mathf.Sin(2f * Mathf.PI * 38f * t) * 0.5f + 0.5f;
                ph += (2900f + 160f * warble) / Rate;
                float tone = Mathf.Sin(2f * Mathf.PI * ph) + 0.25f * Mathf.Sin(4f * Mathf.PI * ph);
                float breath = ((float)rng.NextDouble() * 2f - 1f) * 0.12f;
                data[at + i] = (tone * (0.75f + 0.25f * warble) + breath) * env * volume;
            }
            at += len + (int)(Rate * 0.08f);
        }
        var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    /// <summary>Boot on ball: a short click and a deep thump that drops in pitch.</summary>
    public static AudioClip Kick(string name, float volume)
    {
        var data = new float[(int)(Rate * 0.3f)];
        var rng = new System.Random(name.GetHashCode());
        float ph = 0f;
        var body = new BandPass(1800f, 0.8f, Rate);
        for (int i = 0; i < data.Length; i++)
        {
            float t = (float)i / Rate;
            ph += Mathf.Lerp(45f, 170f, Mathf.Exp(-t * 35f)) / Rate;
            float thump = Mathf.Sin(2f * Mathf.PI * ph) * Mathf.Exp(-t * 16f);
            float slap = body.Run((float)rng.NextDouble() * 2f - 1f) * Mathf.Exp(-t * 90f) * 1.6f;
            data[i] = (thump + slap) * Mathf.Min(1f, t / 0.001f) * volume;
        }
        var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    /// <summary>Ball hitting the net: a soft swish that rises and falls.</summary>
    public static AudioClip NetSwish(string name, float volume)
    {
        var data = new float[(int)(Rate * 0.5f)];
        var rng = new System.Random(name.GetHashCode());
        var bp = new BandPass(3500f, 0.7f, Rate);
        for (int i = 0; i < data.Length; i++)
        {
            float k = (float)i / data.Length;
            bp.Tune(Mathf.Lerp(4500f, 1800f, k), Rate);
            data[i] = bp.Run((float)rng.NextDouble() * 2f - 1f) * Mathf.Sin(Mathf.PI * Mathf.Pow(k, 0.5f)) * volume;
        }
        var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // ---------------- Music ----------------

    /// <summary>
    /// A loopable, driving "match day / challenge" track in A minor: four-on-the-floor kick, claps, hi-hats,
    /// a pumping bass line, brassy chord stabs and (second half) a 16th-note arpeggio that builds tension.
    /// </summary>
    public static AudioClip ChallengeMusic(string name, float bpm = 128f, float volume = 0.8f)
    {
        const int rate = 22050;
        const int bars = 8;
        float beat = 60f / bpm, bar = beat * 4f;
        int n = Mathf.RoundToInt(bar * bars * rate);
        var data = new float[n];
        var rng = new System.Random(name.GetHashCode());

        // Chords per bar (root Hz, and the triad as semitones above the root).
        float A = 110f, F = 87.31f, G = 98f, E = 82.41f, C = 130.81f;
        float[] roots = { A, A, F, G, A, C, F, E };
        int[][] triads = { new[] { 0, 3, 7 }, new[] { 0, 3, 7 }, new[] { 0, 4, 7 }, new[] { 0, 4, 7 },
                           new[] { 0, 3, 7 }, new[] { 0, 4, 7 }, new[] { 0, 4, 7 }, new[] { 0, 4, 7 } };

        for (int b = 0; b < bars; b++)
        {
            float t0 = b * bar;
            float root = roots[b];
            for (int q = 0; q < 4; q++)
            {
                float tb = t0 + q * beat;
                Kick(data, rate, tb, 0.9f);
                if (q == 1 || q == 3) Clap(data, rate, tb, 0.5f, rng);
                Hat(data, rate, tb + beat * 0.5f, q == 3 ? 0.16f : 0.05f, 0.22f, rng);
                Hat(data, rate, tb, 0.03f, 0.12f, rng);
            }
            // Bass: pumping 8ths, octave jump on the last two.
            for (int e = 0; e < 8; e++)
            {
                float f = root * (e >= 6 ? 2f : 1f);
                Note(data, rate, t0 + e * beat * 0.5f, beat * 0.42f, f, 0.38f, 0.10f, saw: true);
            }
            // Brass stabs on the off-beats of 2 and 4, and a long one opening every other bar.
            foreach (float at in new[] { 1.5f, 3.5f })
                foreach (int semi in triads[b])
                    Note(data, rate, t0 + at * beat, beat * 0.3f, root * 4f * Semi(semi), 0.11f, 0.35f, saw: true);
            if (b % 2 == 0)
                foreach (int semi in triads[b])
                    Note(data, rate, t0, beat * 1.2f, root * 2f * Semi(semi + 12), 0.07f, 0.2f, saw: true);
            // Second half: rising 16th-note arpeggio (tension).
            if (b >= 4)
                for (int s = 0; s < 16; s++)
                {
                    int semi = triads[b][s % 3] + (s / 3 % 2) * 12;
                    Note(data, rate, t0 + s * beat * 0.25f, beat * 0.2f, root * 4f * Semi(semi), 0.06f + 0.01f * (b - 4), 0.5f, saw: false);
                }
        }
        // Crash at the top of the loop and a short noise riser into it.
        Crash(data, rate, 0f, 0.35f, rng);
        Riser(data, rate, bar * bars - bar, bar, 0.18f, rng);

        Normalize(data, volume);
        var clip = AudioClip.Create(name, n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    static float Semi(int s) => Mathf.Pow(2f, s / 12f);

    static void Kick(float[] d, int rate, float t, float vol)
    {
        int s = (int)(t * rate);
        float ph = 0f;
        for (int i = 0; i < rate * 0.3f && s + i < d.Length; i++)
        {
            float x = (float)i / rate;
            ph += Mathf.Lerp(42f, 150f, Mathf.Exp(-x * 30f)) / rate;
            d[s + i] += Mathf.Sin(2f * Mathf.PI * ph) * Mathf.Exp(-x * 9f) * vol;
        }
    }

    static void Clap(float[] d, int rate, float t, float vol, System.Random rng)
    {
        int s = (int)(t * rate);
        var bp = new BandPass(1600f, 0.6f, rate);
        for (int i = 0; i < rate * 0.22f && s + i < d.Length; i++)
        {
            float x = (float)i / rate;
            // Three quick bursts then a tail, like hands clapping together.
            float env = x < 0.03f ? (Mathf.Repeat(x, 0.01f) < 0.004f ? 1f : 0.3f) : Mathf.Exp(-(x - 0.03f) * 22f);
            float body = Mathf.Sin(2f * Mathf.PI * 190f * x) * Mathf.Exp(-x * 40f) * 0.4f;
            d[s + i] += (bp.Run((float)rng.NextDouble() * 2f - 1f) * 1.6f + body) * env * vol;
        }
    }

    static void Hat(float[] d, int rate, float t, float length, float vol, System.Random rng)
    {
        int s = (int)(t * rate);
        float prev = 0f;
        for (int i = 0; i < rate * length && s + i < d.Length; i++)
        {
            float x = (float)i / rate;
            float noise = (float)rng.NextDouble() * 2f - 1f;
            float high = noise - prev; // crude high-pass: keeps the sizzle
            prev = noise;
            d[s + i] += high * Mathf.Exp(-x / length * 4f) * vol;
        }
    }

    static void Crash(float[] d, int rate, float t, float vol, System.Random rng)
    {
        int s = (int)(t * rate);
        float prev = 0f;
        for (int i = 0; i < rate * 1.5f && s + i < d.Length; i++)
        {
            float x = (float)i / rate;
            float noise = (float)rng.NextDouble() * 2f - 1f;
            d[s + i] += (noise - prev * 0.6f) * Mathf.Exp(-x * 2.6f) * vol;
            prev = noise;
        }
    }

    static void Riser(float[] d, int rate, float t, float length, float vol, System.Random rng)
    {
        int s = (int)(t * rate);
        var bp = new BandPass(500f, 0.5f, rate);
        for (int i = 0; i < rate * length && s + i < d.Length; i++)
        {
            float k = i / (rate * length);
            if ((i & 31) == 0) bp.Tune(Mathf.Lerp(400f, 5000f, k * k), rate);
            d[s + i] += bp.Run((float)rng.NextDouble() * 2f - 1f) * k * k * vol;
        }
    }

    /// <summary>One synth note: saw (brassy, bass) or square (arpeggio), through a simple low-pass, plucky envelope.</summary>
    static void Note(float[] d, int rate, float t, float length, float hz, float vol, float brightness, bool saw)
    {
        int s = (int)(t * rate);
        float ph = 0f, lp = 0f;
        float a = Mathf.Clamp01(brightness);
        int len = (int)(length * rate), tail = (int)(0.03f * rate);
        for (int i = 0; i < len + tail && s + i < d.Length; i++)
        {
            ph += hz / rate;
            ph -= Mathf.Floor(ph);
            float osc = saw ? ph * 2f - 1f : (ph < 0.5f ? 1f : -1f);
            float env = Mathf.Min(1f, i / (rate * 0.004f)) * (i < len ? Mathf.Lerp(1f, 0.55f, (float)i / len) : 0.55f * (1f - (float)(i - len) / tail));
            float cutoff = a * (0.4f + 0.6f * Mathf.Exp(-(float)i / rate * 12f)); // filter opens on the attack
            lp += (osc - lp) * Mathf.Clamp01(cutoff);
            d[s + i] += lp * env * vol;
        }
    }

    static void Normalize(float[] data, float volume)
    {
        float peak = 1e-4f;
        foreach (var x in data) peak = Mathf.Max(peak, Mathf.Abs(x));
        float g = volume / peak;
        for (int i = 0; i < data.Length; i++) data[i] *= g;
    }
}
