using System;
using Godot;

namespace CCCPRacing.Audio;

/// <summary>A short dry pistol crack, spatially placed at the officer's muzzle.</summary>
public partial class PistolSound : AudioStreamPlayer3D
{
    static AudioStreamWav _report;
    /// <summary>The report itself, for a non-spatial player too (HammerSound).</summary>
    internal static AudioStreamWav Shared => _report ??= Report();

    public override void _Ready()
    {
        Stream = _report ??= Report();
        Bus = AudioMix.BusFor(AudioMix.Boom);
        UnitSize = 18;
        MaxDistance = 180;
        VolumeDb = -2;
        Finished += QueueFree;
    }

    static AudioStreamWav Report()
    {
        const int rate = 22050;
        var bytes = new byte[(int)(rate * 0.45f) * 2];
        var random = new Random(1937);
        float low = 0;
        for (int i = 0; i < bytes.Length / 2; i++)
        {
            float t = (float)i / rate;
            float noise = (float)random.NextDouble() * 2 - 1;
            low += (noise - low) * 0.2f;
            float signal = noise * MathF.Exp(-t * 85) * 0.7f
                + low * MathF.Exp(-t * 22) * 0.35f
                + MathF.Sin(t * MathF.Tau * 170) * MathF.Exp(-t * 38) * 0.2f;
            short sample = (short)(Math.Clamp(signal, -1, 1) * short.MaxValue);
            bytes[i * 2] = (byte)(sample & 255);
            bytes[i * 2 + 1] = (byte)((sample >> 8) & 255);
        }
        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = rate, Data = bytes };
    }
}
