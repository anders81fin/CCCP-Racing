using System;
using Godot;

namespace CCCPRacing.Audio;

/// <summary>Original retro crowd: layered voiced U-RAA syllables and a short celebratory impact.</summary>
public partial class MedalCheer : AudioStreamPlayer
{
    public bool Brief { get; set; }
    public bool Boo { get; set; }
    const int Rate = 22050, TableSize = 1024;
    public override void _Ready()
    {
        Bus = AudioMix.BusFor(AudioMix.Boom);
        VolumeDb = -5f;
        Stream ??= Make(Brief, Boo); // build before the race, never on the award frame
    }

    // The HUD layer is reparented (the track preview, the launch cover) and leaves the tree for a moment:
    // _ExitTree freed the stream and _Ready does not run again, so the cheer stayed silent from then on.
    public override void _EnterTree() => Stream ??= Make(Brief, Boo);

    public override void _ExitTree()
    {
        Stop();
        var owned = Stream;
        Stream = null;
        owned?.Dispose();
    }

    static float[] Vowel(float pitch, bool open)
    {
        var table = new float[TableSize];
        float[] formants = open ? new[] { 760f, 1180f, 2600f } : new[] { 330f, 750f, 2200f };
        for (int harmonic = 1; harmonic <= 36; harmonic++)
        {
            float hz = pitch * harmonic, weight = 0.045f / harmonic;
            foreach (float formant in formants)
                weight += MathF.Exp(-MathF.Pow((hz - formant) / (formant * 0.14f + 70), 2)) / MathF.Sqrt(harmonic);
            for (int i = 0; i < TableSize; i++)
                table[i] += MathF.Sin(MathF.Tau * harmonic * i / TableSize) * weight;
        }
        return table;
    }

    static AudioStreamWav Make(bool brief, bool boo)
    {
        var samples = new float[(int)(Rate * (brief ? 1.4f : 3f))];
        var rng = new Random(1945);
        int voices = brief ? 8 : 14;
        for (int voice = 0; voice < voices; voice++)
        {
            float pitch = (boo ? 80f : 105f) + (float)rng.NextDouble() * (boo ? 65f : 110f);
            var u = Vowel(pitch, false); var a = Vowel(pitch, true);
            float start = (brief ? 0.03f : 0.10f) + (float)rng.NextDouble() * (brief ? 0.12f : 0.22f);
            float duration = (brief ? 0.65f : 1.15f) + (float)rng.NextDouble() * (brief ? 0.2f : 0.45f);
            double phase = rng.NextDouble();
            float pan = (float)rng.NextDouble();
            for (int i = (int)(start * Rate); i < samples.Length; i++)
            {
                float t = i / (float)Rate - start;
                if (t >= duration) break;
                phase += pitch * (1 + 0.025f * MathF.Sin(t * 20 + voice) - (boo ? 0.27f : 0.09f) * t / duration) / Rate;
                int index = (int)(phase * TableSize) & (TableSize - 1);
                float vowel = boo ? 0f : Mathf.SmoothStep(brief ? 0.13f : 0.22f, brief ? 0.25f : 0.40f, t);
                float trill = !boo && t > (brief ? 0.13f : 0.23f) && t < (brief ? 0.25f : 0.37f)
                    ? 0.35f + 0.65f * MathF.Abs(MathF.Sin(t * 95)) : 1f;
                float env = Math.Min(1f, t / 0.04f) * Math.Min(1f, (duration - t) / 0.35f);
                samples[i] += Mathf.Lerp(u[index], a[index], vowel) * env * trill * (0.55f + pan * 0.25f) / voices;
            }
        }
        float peak = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)Rate;
            if (!brief) samples[i] += MathF.Sin(MathF.Tau * (95f * t - 30f * t * t)) * MathF.Exp(-t * 24) * 0.5f;
            // A short diffuse echo gives the crowd some space.
            if (i >= Rate / 7) samples[i] += samples[i - Rate / 7] * 0.17f;
            peak = Math.Max(peak, Math.Abs(samples[i]));
        }
        var bytes = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short value = (short)(samples[i] / Math.Max(peak, 0.001f) * 0.9f * short.MaxValue);
            bytes[i * 2] = (byte)(value & 255); bytes[i * 2 + 1] = (byte)((value >> 8) & 255);
        }
        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = Rate, Data = bytes };
    }
}
