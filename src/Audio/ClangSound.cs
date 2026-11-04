using System;
using Godot;

namespace CCCPRacing.Audio;

/// <summary>One sledgehammer blow on sheet metal: a hard click, then a ringing set of inharmonic
/// partials that die away (bigger blows ring longer and lower). Frees itself when done.</summary>
public partial class ClangSound : AudioStreamPlayer
{
    const float MixRate = 22050f;
    static readonly float[] Freqs = { 310f, 745f, 1290f, 1980f, 2710f };
    static readonly float[] Amps = { 1f, 0.7f, 0.5f, 0.32f, 0.2f };
    static readonly float[] Decays = { 9f, 12f, 16f, 22f, 30f };   // 1/s: the high partials die first

    /// <summary>0..1: how hard the blow was.</summary>
    public float Strength = 0.6f;
    /// <summary>Replay entry age: resume the decaying ring without a new strike.</summary>
    public float StartAge;
    AudioStreamGeneratorPlayback _pb;
    readonly double[] _phase = new double[Freqs.Length];
    float _t, _lp;
    readonly Random _rng = new();

    public override void _ExitTree()
    {
        // Release the managed reference before Godot tears down the audio server.
        Stop();
        _pb?.Dispose();
        _pb = null;
        Stream = null;
    }

    public override void _Ready()
    {
        _t = Math.Max(0, StartAge);
        if (_t >= .8f) { QueueFree(); return; }
        for (int k = 0; k < Freqs.Length; k++) _phase[k] = Freqs[k] * (1f - .12f * Strength) * _t % 1.0;
        Stream = new AudioStreamGenerator { MixRate = MixRate, BufferLength = 0.1f };
        Bus = AudioMix.BusFor(AudioMix.Engine);
        VolumeDb = -3f;
        Play();
        _pb = GetStreamPlayback() as AudioStreamGeneratorPlayback;
    }

    public override void _Process(double delta)
    {
        if (_pb == null) return;
        float pitch = 1f - 0.12f * Strength;       // hard blows are lower and heavier
        float slow = 1f - 0.35f * Strength;        // ...and ring longer
        int frames = _pb.GetFramesAvailable();
        for (int i = 0; i < frames; i++)
        {
            _t += 1f / MixRate;
            float s = 0;
            for (int k = 0; k < Freqs.Length; k++)
            {
                _phase[k] = (_phase[k] + Freqs[k] * pitch / MixRate) % 1.0;
                s += MathF.Sin((float)(_phase[k] * Math.Tau)) * Amps[k] * MathF.Exp(-_t * Decays[k] * slow);
            }
            // The strike itself: a few ms of noise through a low-pass.
            float n = (float)_rng.NextDouble() * 2f - 1f;
            _lp += (n - _lp) * 0.5f;
            s = s * 0.28f + _lp * 0.9f * MathF.Exp(-_t * 350f);
            s = Math.Clamp(s * (0.55f + 0.45f * Strength), -1f, 1f);
            _pb.PushFrame(new Vector2(s, s));
        }
        if (_t > 0.8f) QueueFree();
    }
}
