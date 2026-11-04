using System;
using Godot;

namespace CCCPRacing.Audio;

/// <summary>A sharp sporting shotgun report with short echoes across the pond. Shared PCM, spatial playback.</summary>
public partial class ShotgunSound : AudioStreamPlayer3D
{
    public const float Reach = 750f;
    const int Rate = 22050;
    static AudioStreamWav _report;
    float _delay;

    public override void _Ready()
    {
        Stream = _report ??= MakeReport();
        Bus = AudioMix.BusFor(AudioMix.Boom);
        UnitSize = 180f;
        MaxDistance = Reach;
        VolumeDb = 4f;
        MaxDb = 1f;
        AttenuationFilterDb = -10f;
        Finished += QueueFree;
        var viewport = GetViewport();
        Vector3? ear = viewport?.GetAudioListener3D()?.GlobalPosition ?? viewport?.GetCamera3D()?.GlobalPosition;
        _delay = ear is Vector3 p ? GlobalPosition.DistanceTo(p) / 343f : 0;
        if (_delay <= 0) { Play(); SetProcess(false); }
    }

    public override void _Process(double delta)
    {
        _delay -= (float)delta;
        if (_delay > 0) return;
        Play();
        SetProcess(false);
    }

    static AudioStreamWav MakeReport()
    {
        var samples = new float[(int)(Rate * 1.65f)];
        var random = new Random(1939);
        float[] echoes = { 0.14f, 0.32f, 0.53f };
        float low = 0, peak = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            float t = (float)i / Rate;
            float noise = (float)random.NextDouble() * 2 - 1;
            low += (noise - low) * 0.055f;
            // A dry crack, a short chesty bang, then the pond and trees answering it.
            float s = noise * 1.7f * MathF.Exp(-t * 65f)
                + MathF.Sin(MathF.Tau * (120f * t - 24f * t * t)) * 0.48f * MathF.Exp(-t * 16f)
                + low * 1.4f * MathF.Exp(-t * 5f);
            foreach (float echo in echoes)
                if (t >= echo) s += low * 0.7f * MathF.Exp(-(t - echo) * 12f) * MathF.Exp(-echo * 2f);
            samples[i] = s;
            peak = Math.Max(peak, Math.Abs(s));
        }
        var bytes = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short s = (short)(samples[i] / peak * 0.92f * short.MaxValue);
            bytes[i * 2] = (byte)(s & 255);
            bytes[i * 2 + 1] = (byte)((s >> 8) & 255);
        }
        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = Rate, Data = bytes };
    }
}
