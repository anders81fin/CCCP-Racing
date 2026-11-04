using System;
using Godot;

namespace CCCPRacing.Audio;

/// <summary>The high whine left in the ears after a very close blast: a thin tone that swells for a moment and dies away.</summary>
public partial class EarRingSound : AudioStreamPlayer
{
    const float MixRate = 22050f;
    public float Level = .5f;
    AudioStreamGeneratorPlayback _pb;
    double _phase;
    float _t;

    public override void _ExitTree() { _pb?.Dispose(); _pb = null; }
    public override void _Ready()
    {
        Stream = new AudioStreamGenerator { MixRate = MixRate, BufferLength = 0.2f };
        Bus = AudioMix.BusFor(AudioMix.Warning);
        VolumeDb = -22f + 12f * Level;
        Play();
        _pb = GetStreamPlayback() as AudioStreamGeneratorPlayback;
    }
    public override void _Process(double delta)
    {
        if (_pb == null) return;
        int frames = _pb.GetFramesAvailable();
        for (int i = 0; i < frames; i++)
        {
            _t += 1f / MixRate;
            _phase += 3900.0 / MixRate;
            float envelope = Math.Min(1f, _t * 12f) * MathF.Exp(-_t * 1.1f);
            float tone = (float)(Math.Sin(_phase * Math.Tau) + .4 * Math.Sin(_phase * 2.31 * Math.Tau)) * .4f * envelope;
            _pb.PushFrame(new Vector2(tone, tone));
        }
        if (_t > 3.5f) QueueFree();
    }
}
