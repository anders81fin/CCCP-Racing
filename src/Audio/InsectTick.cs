using System;
using Godot;

namespace CCCPRacing.Audio;

/// <summary>
/// A bug hitting the lens: one tiny dry "tick" (a few milliseconds of high noise and a short ping).
/// Not spatial, on Master, quiet. Call Tick() to play.
/// </summary>
public partial class InsectTick : AudioStreamPlayer
{
    const int Rate = 22050;
    static AudioStreamWav _wav;

    public override void _Ready()
    {
        Stream = _wav ??= Make();
        VolumeDb = -17f;
    }

    public void Tick() => Play();

    static AudioStreamWav Make()
    {
        var samples = new short[(int)(Rate * 0.06f)];
        var random = new Random(77);
        float prev = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            float t = (float)i / Rate;
            float n = (float)random.NextDouble() * 2 - 1;
            float hp = n - prev * 0.8f;                 // roughly a high-pass
            prev = n;
            float s = hp * 0.7f * MathF.Exp(-t * 350f) + MathF.Sin(MathF.Tau * 2300f * t) * 0.5f * MathF.Exp(-t * 220f);
            samples[i] = (short)(Math.Clamp(s, -1f, 1f) * 0.8f * short.MaxValue);
        }
        var bytes = new byte[samples.Length * 2];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = Rate, Stereo = false, Data = bytes };
    }
}
