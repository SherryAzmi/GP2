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
}
