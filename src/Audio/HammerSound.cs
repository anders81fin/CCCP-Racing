using System;
using Godot;

namespace CCCPRacing.Audio;

/// <summary>
/// A revolver's hammer drawn back in a silent room: the dry ratchet of the cylinder turning, the sharp
/// click of the sear catching, a pause, then the shot: the officer's pistol crack (PistolSound) over a
/// heavy, close blast with a deep thump and no echo. Not spatial; plays once (the resignation's last word).
/// Length: the game waits for it before quitting.
/// </summary>
public partial class HammerSound : AudioStreamPlayer
{
    const int Rate = 22050;
    /// <summary>When the shot falls after the start, and how long the whole sound lasts (seconds).</summary>
    public const float ShotAt = 1.3f, Length = 2.0f;
    static AudioStreamWav _cock, _blast;
    AudioStreamPlayer _shot, _boom;
    float _t;

    public override void _Ready()
    {
        Stream = _cock ??= MakeCock();
        VolumeDb = -3f;
        Play();
        // The shot 2.5 times the amplitude it was first tuned at (+8 dB); Master's limiter keeps it clean.
        AddChild(_shot = new AudioStreamPlayer { Stream = PistolSound.Shared, VolumeDb = 10f });
        AddChild(_boom = new AudioStreamPlayer { Stream = _blast ??= MakeBlast(), VolumeDb = 11f });
    }

    public override void _Process(double delta)
    {
        float before = _t;
        _t += (float)delta;
        if (before < ShotAt && _t >= ShotAt) { _shot.Play(); _boom.Play(); }
    }

    /// <summary>
    /// The weight of the shot: the shotgun's crack and chesty boom (ShotgunSound) without the pond's
    /// echoes, a deep thump under it, driven into soft saturation so it hits loud.
    /// </summary>
    static AudioStreamWav MakeBlast()
    {
        var samples = new float[(int)(Rate * 0.7f)];
        var random = new Random(1941);
        float low = 0, peak = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            float t = (float)i / Rate;
            float noise = (float)random.NextDouble() * 2 - 1;
            low += (noise - low) * 0.07f;
            float s = noise * 1.7f * MathF.Exp(-t * 60f)
                + MathF.Sin(MathF.Tau * (110f * t - 40f * t * t)) * 0.9f * MathF.Exp(-t * 14f)
                + MathF.Sin(MathF.Tau * (58f * t - 12f * t * t)) * 1.1f * MathF.Exp(-t * 9f)
                + low * 2.2f * MathF.Exp(-t * 11f);
            samples[i] = MathF.Tanh(s * 1.8f);
            peak = Math.Max(peak, Math.Abs(samples[i]));
        }
        return Pcm(samples, peak);
    }

    static AudioStreamWav Pcm(float[] samples, float peak)
    {
        var bytes = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short v = (short)Math.Clamp(samples[i] / peak * 0.95f * short.MaxValue, short.MinValue, short.MaxValue);
            bytes[2 * i] = (byte)(v & 0xff);
            bytes[2 * i + 1] = (byte)((v >> 8) & 0xff);
        }
        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = Rate, Stereo = false, Data = bytes };
    }

    static AudioStreamWav MakeCock()
    {
        var samples = new float[(int)(Rate * 0.75f)];
        var random = new Random(1937);
        float hp = 0, prev = 0, peak = 0;
        // (time, strength, ring Hz): a soft pawl tick, the cylinder ratchet, the hard sear click.
        (float At, float Gain, float Ring)[] clicks =
        {
            (0.00f, 0.35f, 2900f), (0.07f, 0.45f, 3400f), (0.12f, 0.4f, 3100f), (0.27f, 1f, 4200f),
        };
        for (int i = 0; i < samples.Length; i++)
        {
            float t = (float)i / Rate;
            float noise = (float)random.NextDouble() * 2 - 1;
            hp = 0.85f * (hp + noise - prev);     // high-passed noise: metal, not thud
            prev = noise;
            float s = 0;
            foreach (var (at, gain, ring) in clicks)
            {
                float u = t - at;
                if (u < 0) continue;
                s += gain * (hp * MathF.Exp(-u * 420f) + 0.55f * MathF.Sin(MathF.Tau * ring * u) * MathF.Exp(-u * 160f)
                    + 0.25f * MathF.Sin(MathF.Tau * ring * 1.53f * u) * MathF.Exp(-u * 220f));
            }
            // The steel body of the gun answers the last click, low and short.
            if (t >= 0.27f) s += 0.18f * MathF.Sin(MathF.Tau * 610f * (t - 0.27f)) * MathF.Exp(-(t - 0.27f) * 38f);
            samples[i] = s;
            peak = Math.Max(peak, Math.Abs(s));
        }
        return Pcm(samples, peak / 0.95f);
    }
}
