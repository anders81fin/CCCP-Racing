using System;
using Godot;

namespace CCCPRacing.Audio;

/// <summary>
/// Radar-warning receiver bolted under the dashboard: short high beeps at <see cref="Rate"/> per
/// second while a missile is after the car, silent at 0. The first beep comes at once. No samples:
/// a filtered square wave into an AudioStreamGenerator.
/// </summary>
public partial class MissileWarning : Node
{
    const float MixRate = 22050f;
    const float ToneHz = 1250f, BeepLen = 0.05f;

    /// <summary>Beeps per second; 0 = silent.</summary>
    public float Rate;
    public float Volume = 0.35f;

    AudioStreamPlayer _player;
    AudioStreamGeneratorPlayback _pb;
    double _tone, _beat = 1.0;
    float _beepT = 99f, _lp;

    public override void _ExitTree()
    {
        // Release the managed reference before Godot tears down the audio server.
        _pb?.Dispose();
        _pb = null;
    }

    public override void _Ready()
    {
        var gen = new AudioStreamGenerator { MixRate = MixRate, BufferLength = 0.08f };
        _player = new AudioStreamPlayer { Stream = gen, VolumeDb = EngineSound.GainDb, Bus = AudioMix.BusFor(AudioMix.Warning) };
        AddChild(_player);
        _player.Play();
        _pb = _player.GetStreamPlayback() as AudioStreamGeneratorPlayback;
    }

    public override void _Process(double delta)
    {
        if (_pb == null) return;
        int frames = _pb.GetFramesAvailable();
        float len = Rate > 0 ? Math.Min(BeepLen, 0.45f / Rate) : BeepLen;
        for (int i = 0; i < frames; i++)
        {
            if (Rate > 0)
            {
                _beat += Rate / MixRate;
                if (_beat >= 1.0) { _beat -= Math.Floor(_beat); _beepT = 0; }
            }
            else _beat = 1.0;   // the next warning starts with a beep
            _beepT += 1f / MixRate;
            if (_beepT > len) { _pb.PushFrame(Vector2.Zero); continue; }
            _tone = (_tone + ToneHz / MixRate) % 1.0;
            // Click-free: 3 ms ramps at both ends.
            float env = Math.Min(1f, Math.Min(_beepT, len - _beepT) / 0.003f);
            float sq = _tone < 0.5 ? 1f : -1f;
            _lp += (sq - _lp) * 0.35f;
            float s = Math.Clamp(_lp * env * Volume, -1f, 1f);
            _pb.PushFrame(new Vector2(s, s));
        }
    }
}
