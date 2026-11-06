using System;
using Godot;

namespace CCCPRacing.Audio;

/// <summary>
/// Fuel pouring into the tank while <see cref="Pouring"/> is set: a hissing low-passed rush with
/// glugs on top (short rising sine chirps, a dozen a second, pitched up as the tank fills). No
/// samples: synthesised into an AudioStreamGenerator.
/// </summary>
public partial class GurgleSound : Node
{
    const float MixRate = 22050f;

    public bool Pouring;
    /// <summary>How full the tank is, 0..1: the glugs rise with it.</summary>
    public float Level;
    public float Volume = 0.55f;

    AudioStreamPlayer _player;
    AudioStreamGeneratorPlayback _pb;
    readonly Random _rng = new(1961);
    float _env, _lp, _lp2, _glugT = 1f, _glugLen, _glugF, _next;
    double _glugPhase;

    // Restore continuous sound without a fresh fade-in, including its valve-close tail.
    internal float Envelope { get => _env; set => _env = Math.Clamp(value, 0f, 1f); }

    internal void ClearReplayAudio()
    {
        // Godot cannot clear an active generator. Restart its playback so queued
        // samples from the old tape time cannot leak across a seek or replay exit.
        if (!AudioMix.Closing && _player != null)
        {
            using var driverLock = AudioMix.LockDriver();
            bool paused = _player.StreamPaused;
            _player.StreamPaused = true;
            _pb.Stop(); _pb.ClearBuffer(); _pb.Start();
            _player.StreamPaused = paused;
        }
        _env = _lp = _lp2 = _next = 0;
        _glugT = 1f; _glugPhase = 0;
    }

    public override void _ExitTree()
    {
        using var driverLock = AudioMix.LockDriver();
        using var generator = GodotObject.IsInstanceValid(_player) ? _player.Stream as AudioStreamGenerator : null;
        if (GodotObject.IsInstanceValid(_player))
        {
            _player.StreamPaused = false; _player.Stop();
            _player.Stream = null;
        }
        if (GodotObject.IsInstanceValid(_pb)) _pb.Stop(); _pb?.Dispose();
        _pb = null;
    }

    public override void _Ready()
    {
        var gen = new AudioStreamGenerator { MixRate = MixRate, BufferLength = 0.08f };
        _player = new AudioStreamPlayer { Stream = gen, VolumeDb = EngineSound.GainDb, Bus = AudioMix.BusFor(AudioMix.Engine) };
        AddChild(_player);
        _player.Play();
        _pb = _player.GetStreamPlayback() as AudioStreamGeneratorPlayback;
    }

    public override void _Process(double delta)
    {
        if (_pb == null) return;
        int frames = _pb.GetFramesAvailable();
        for (int i = 0; i < frames; i++)
            _pb.PushFrame(Vector2.One * Sample());
    }

    /// <summary>The synthesis used by both the live generator and replay.</summary>
    internal float Sample()
    {
        _env += ((Pouring ? 1f : 0f) - _env) * (Pouring ? 0.0015f : 0.004f);
        if (_env < 0.0005f && !Pouring) return 0;
        // The rush of fuel down the hose: noise, low-passed twice.
        float n = (float)_rng.NextDouble() * 2f - 1f;
        _lp += (n - _lp) * 0.12f;
        _lp2 += (_lp - _lp2) * 0.2f;
        float s = _lp2 * 0.9f;
        // Glugs: an air bubble each, a quick upward chirp with a fast decay.
        _next -= 1f / MixRate;
        if (_next <= 0f)
        {
            _next = 0.05f + (float)_rng.NextDouble() * 0.09f;
            _glugT = 0f;
            _glugLen = 0.03f + (float)_rng.NextDouble() * 0.03f;
            _glugF = (170f + 160f * Level) * (0.85f + 0.3f * (float)_rng.NextDouble());
        }
        if (_glugT < _glugLen * 2.5f)
        {
            float u = _glugT / _glugLen;
            _glugPhase = (_glugPhase + _glugF * (1f + 1.2f * Math.Min(u, 1f)) / MixRate) % 1.0;
            s += MathF.Sin((float)(_glugPhase * Math.Tau)) * MathF.Exp(-u * 2.2f) * MathF.Min(1f, u * 8f) * 0.8f;
            _glugT += 1f / MixRate;
        }
        s = Math.Clamp(s * _env * Volume, -1f, 1f);
        return s;
    }
}
