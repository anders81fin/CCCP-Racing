using System;
using Godot;

namespace CCCPRacing.Audio;

/// <summary>
/// Dashboard dosimeter: random (Poisson) electrical snaps with a gritty noise tail, from
/// separate background ticks to an insistent crackle in a hot spot. No samples.
/// </summary>
public partial class GeigerSound : Node
{
    const float MixRate = 22050f;

    /// <summary>Clicks per second; 0 = silent.</summary>
    public float Rate;
    public float Volume = 0.8f;

    AudioStreamPlayer _player;
    AudioStreamGeneratorPlayback _pb;
    readonly Random _rng = new(1986);
    float _env, _snap, _lp, _hp, _wait = 1f;

    public override void _ExitTree()
    {
        // Release the managed reference before Godot tears down the audio server.
        _pb?.Dispose();
        _pb = null;
    }

    public override void _Ready()
    {
        _player = new AudioStreamPlayer { Stream = new AudioStreamGenerator { MixRate = MixRate, BufferLength = 0.08f }, VolumeDb = EngineSound.GainDb, Bus = AudioMix.BusFor(AudioMix.Warning) };
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
            _wait -= 1f / MixRate;
            if (_wait <= 0 && Rate > 0.01f)
            {
                _env = 0.7f + 0.3f * (float)_rng.NextDouble();
                _snap = _rng.Next(2) == 0 ? -1.2f : 1.2f;
                _wait = -MathF.Log(1f - (float)_rng.NextDouble() * 0.999f) / Rate;    // the next decay
            }
            else if (Rate <= 0.01f) _wait = 0.2f;
            float n = (float)_rng.NextDouble() * 2f - 1f;
            float x = n * _env * 1.8f + _snap;
            _env *= 0.955f;                     // ~1 ms decay: a rough crackle instead of a pinprick
            _snap *= -0.62f;                    // brief bipolar electrical snap at the attack
            _lp += (x - _lp) * 0.8f;
            _hp += (_lp - _hp) * 0.04f;         // retain more of the low-mid body
            float driven = (_lp - _hp) * Volume * 2.4f;
            float s = driven / (1f + Math.Abs(driven)); // rounded saturation adds bite without hard clipping
            _pb.PushFrame(new Vector2(s, s));
        }
    }
}
