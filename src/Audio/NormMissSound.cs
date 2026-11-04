using System;
using Godot;

namespace CCCPRacing.Audio;

/// <summary>A rubber stamp and a short, drooping brass phrase. Samples are prepared before driving.</summary>
public partial class NormMissSound : AudioStreamPlayer
{
    readonly AudioStreamWav[] _sounds = new AudioStreamWav[3];

    public override void _EnterTree()
    {
        for (int i = 0; i < _sounds.Length; i++) _sounds[i] ??= Make(i);
    }

    public override void _Ready()
    {
        Bus = AudioMix.BusFor(AudioMix.Warning);
        VolumeDb = -5;
    }

    public void React(bool close, bool final)
    {
        Stop();
        Stream = _sounds[final ? 2 : close ? 0 : 1];
        Play();
    }

    public override void _ExitTree()
    {
        Stop();
        Stream = null;
        for (int i = 0; i < _sounds.Length; i++)
        {
            _sounds[i]?.Dispose();
            _sounds[i] = null;
        }
    }

    static AudioStreamWav Make(int kind)
    {
        const int rate = 22050;
        float duration = kind == 0 ? 0.4f : kind == 1 ? 0.62f : 0.86f;
        var data = new byte[(int)(duration * rate) * 2];
        var rng = new Random(1957);
        double phase = 0;
        for (int i = 0; i < data.Length / 2; i++)
        {
            float t = (float)i / rate;
            float noise = (float)rng.NextDouble() * 2 - 1;
            float stamp = noise * MathF.Exp(-t * 125) * 0.55f
                + MathF.Sin(MathF.Tau * 130 * t) * MathF.Exp(-t * 65) * 0.65f;
            float sound = stamp * (kind == 0 ? 0.6f : 1);
            float u = t - 0.07f;
            if (u >= 0)
            {
                float split = kind == 2 ? 0.29f : 0.21f;
                bool second = kind != 0 && u >= split;
                float local = second ? u - split : u;
                float noteLength = kind == 0 ? 0.29f : second ? duration - 0.07f - split : split;
                float freq = (kind == 2 ? 174.61f : 233.08f) * (second ? 0.749f : 1)
                    * (1 - 0.07f * local / noteLength);
                phase += freq / rate;
                float brass = 0;
                for (int h = 1; h <= 9; h++)
                    brass += MathF.Sin((float)(phase * Math.Tau * h))
                        * MathF.Exp(-MathF.Pow((h * freq - 950) / 1400, 2)) / MathF.Pow(h, 0.85f);
                float envelope = Math.Min(1, local / 0.025f)
                    * Math.Clamp((noteLength - local) / 0.065f, 0, 1);
                sound += brass * envelope * (kind == 0 ? 0.17f : 0.29f);
            }
            short value = (short)(Math.Clamp(sound * Math.Min(1, t * 3000), -0.95f, 0.95f) * short.MaxValue);
            data[2 * i] = (byte)(value & 255);
            data[2 * i + 1] = (byte)((value >> 8) & 255);
        }
        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = rate, Data = data };
    }
}
