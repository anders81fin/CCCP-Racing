using System;
using Godot;

namespace CCCPRacing.Audio;

/// <summary>
/// The car's horn: two buzzing reed tones a minor third apart, sounding as long as
/// <see cref="Pressed"/> is set. No samples: synthesised into an AudioStreamGenerator.
/// </summary>
public partial class HornSound : Node
{
    const float MixRate = 22050f;
    const float LowHz = 335f, HighHz = 400f;

    public bool Pressed;
    public float Volume = 0.6f;

    AudioStreamPlayer _player;
    AudioStreamGeneratorPlayback _pb;
    double _phaseLow, _phaseHigh, _buzzPhase;
    float _env, _lp;

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
        {
            // Quick attack, a little slower release as the diaphragm stops.
            _env += ((Pressed ? 1f : 0f) - _env) * (Pressed ? 0.02f : 0.006f);
            if (_env < 0.0005f && !Pressed) { _pb.PushFrame(Vector2.Zero); continue; }

            _phaseLow = (_phaseLow + LowHz / MixRate) % 1.0;
            _phaseHigh = (_phaseHigh + HighHz / MixRate) % 1.0;
            _buzzPhase = (_buzzPhase + 96.0 / MixRate) % 1.0;   // the reed rattling: a rough edge on the note
            float buzz = 0.82f + 0.18f * MathF.Sin((float)(_buzzPhase * Math.Tau));
            // Saw plus pulse gives the nasal, square-ish honk; the low-pass takes off the fizz.
            float lo = (float)(_phaseLow * 2 - 1) * 0.6f + (_phaseLow < 0.35 ? 0.4f : -0.4f);
            float hi = (float)(_phaseHigh * 2 - 1) * 0.6f + (_phaseHigh < 0.35 ? 0.4f : -0.4f);
            float raw = (lo + hi * 0.9f) * 0.5f * buzz;
            _lp += (raw - _lp) * 0.32f;
            float s = Math.Clamp(_lp * _env * Volume * SoundBalance.EngineGain, -1f, 1f);
            _pb.PushFrame(new Vector2(s, s));
        }
    }
}
