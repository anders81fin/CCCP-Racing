using System;
using Godot;

namespace CCCPRacing.Audio;

/// <summary>Rocket motor of a launched missile: a sharp ignition whoosh, then a hissing roar that
/// dies with the burn. Non-positional; the owner sets <see cref="Gain"/> from its distance.</summary>
public partial class MissileSound : AudioStreamPlayer
{
    const float MixRate = 22050f;
    /// <summary>Seconds of motor burn (the roar fades out over its last half second).</summary>
    public float BurnTime = 5.5f;
    /// <summary>0..1 loudness, e.g. from the distance to the camera.</summary>
    public float Gain = 1f;
    AudioStreamGeneratorPlayback _pb;
    float _t, _lp, _hp, _bp, _g = -1f;
    readonly Random _rng = new();

    public override void _ExitTree()
    {
        using var driverLock = AudioMix.LockDriver();
        using var generator = Stream as AudioStreamGenerator;
        StreamPaused = false; Stop();
        Stream = null;
        if (GodotObject.IsInstanceValid(_pb)) _pb.Stop(); _pb?.Dispose();
        _pb = null;
    }

    public override void _Ready()
    {
        Stream = new AudioStreamGenerator { MixRate = MixRate, BufferLength = 0.1f };
        Bus = AudioMix.BusFor(AudioMix.Boom);
        VolumeDb = -2f;
        Play();
        _pb = GetStreamPlayback() as AudioStreamGeneratorPlayback;
    }

    public override void _Process(double delta)
    {
        if (_pb == null) return;
        if (_g < 0) _g = Gain;
        int frames = _pb.GetFramesAvailable();
        for (int i = 0; i < frames; i++)
        {
            _t += 1f / MixRate;
            _g += (Gain - _g) * 0.001f;
            float n = (float)_rng.NextDouble() * 2f - 1f;
            // Whoosh: noise through a low-pass whose cutoff sweeps up as the missile leaves the rail.
            float sweep = Math.Clamp(0.04f + _t * 0.5f, 0.04f, 0.45f);
            _bp += (n - _bp) * sweep;
            float whoosh = _bp * 1.6f * MathF.Min(1f, _t * 25f) * MathF.Exp(-_t * 2.5f);
            // Motor roar: rumbling low noise plus hiss, cut when the burn ends.
            _lp += (n - _lp) * 0.06f;
            _hp += (n - _hp) * 0.5f;
            float burn = Math.Clamp((BurnTime - _t) * 2f, 0f, 1f) * MathF.Min(1f, _t * 6f);
            float roar = (_lp * 2.2f + (n - _hp) * 0.25f) * burn;
            float s = Math.Clamp((whoosh + roar) * _g, -1f, 1f);
            _pb.PushFrame(new Vector2(s, s));
        }
        if (_t > BurnTime + 0.5f) QueueFree();
    }
}
