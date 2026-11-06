using System;
using System.Collections.Generic;
using Godot;

namespace CCCPRacing.Audio;

/// <summary>
/// The co-driver's call as a C64 would say it: two square-wave notes for the direction (falling for a left, rising for
/// a right) and a short tick for each step of sharpness beyond medium; a hairpin is slower and lower. No samples.
/// </summary>
public partial class PaceBlip : Node
{
    const float MixRate = 22050f;
    public float Volume = 0.22f;

    AudioStreamPlayer _player;
    AudioStreamGeneratorPlayback _pb;
    readonly Queue<(float freq, float len)> _notes = new();
    float _freq, _left, _total, _gap;
    double _phase;

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
        _player = new AudioStreamPlayer { Stream = new AudioStreamGenerator { MixRate = MixRate, BufferLength = 0.08f }, VolumeDb = EngineSound.GainDb, Bus = AudioMix.BusFor(AudioMix.Warning) };
        AddChild(_player);
        _player.Play();
        _pb = _player.GetStreamPlayback() as AudioStreamGeneratorPlayback;
    }

    /// <summary>Calls a bend: sign +1 right / -1 left, severity 2 MEDIUM, 3 SHARP, 4 HAIRPIN.</summary>
    public void Call(int sign, int severity)
    {
        _notes.Clear();
        bool hairpin = severity >= 4;
        float lo = hairpin ? 330f : 440f, hi = hairpin ? 440f : 587f, len = hairpin ? 0.12f : 0.07f;
        _notes.Enqueue((sign > 0 ? lo : hi, len));
        _notes.Enqueue((sign > 0 ? hi : lo, len * 1.3f));
        for (int i = 0; i < severity - 2 + (hairpin ? 1 : 0); i++)
        {
            _notes.Enqueue((0f, 0.03f));
            _notes.Enqueue((880f, 0.04f));
        }
    }

    public override void _Process(double delta)
    {
        if (_pb == null) return;
        int frames = _pb.GetFramesAvailable();
        for (int i = 0; i < frames; i++)
        {
            if (_left <= 0)
            {
                if (_notes.Count == 0) { _pb.PushFrame(Vector2.Zero); continue; }
                (_freq, _total) = _notes.Dequeue();
                _left = _total;
            }
            _left -= 1f / MixRate;
            _phase += _freq / MixRate;
            // A quarter-width pulse in 4-bit steps, with a quick attack and a falling tail: a SID voice.
            float wave = _freq <= 0 ? 0 : (_phase % 1.0 < 0.25 ? 1f : -1f);
            float k = _total > 0 ? _left / _total : 0;
            float env = Math.Min(1f, (1f - k) * _total / 0.004f) * (0.4f + 0.6f * k);
            float s = MathF.Round(wave * env * 7f) / 7f * Volume;
            _pb.PushFrame(new Vector2(s, s));
        }
    }
}
