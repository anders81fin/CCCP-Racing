using System;
using Godot;

namespace CCCPRacing.Audio;

/// <summary>
/// The duty officer's siren on the Kozlik: a wail sweeping up and down (about 550-1050 Hz,
/// 1.6 s a sweep), a buzzy filtered square wave, positional like <see cref="DieselSound"/> and
/// faded out by <see cref="Reach"/>. <see cref="On"/> fades it in and out. No samples.
/// </summary>
public partial class SirenSound : AudioStreamPlayer3D
{
    const float MixRate = 22050f;
    const float Low = 550f, High = 1050f, Sweep = 1.6f;

    public bool On;
    /// <summary>Level in dB at 14 m (set before adding to the tree).</summary>
    public float Loudness = 1f;
    /// <summary>Faded out completely at this distance (m); the fade starts at 60 % of it.</summary>
    public float Reach = 220f;

    AudioStreamGeneratorPlayback _pb;
    double _tone, _sweep;
    float _lp, _env;
    Doppler _doppler;

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
        UnitSize = 14f;
        MaxDistance = Reach;
        VolumeDb = Loudness;
        Bus = AudioMix.BusFor(AudioMix.Diesel);
        Play();
        _pb = GetStreamPlayback() as AudioStreamGeneratorPlayback;
    }

    public override void _Process(double delta)
    {
        if (_pb == null) return;
        int frames = _pb.GetFramesAvailable();
        var vp = GetViewport();
        Vector3 ear = vp?.GetAudioListener3D()?.GlobalPosition ?? vp?.GetCamera3D()?.GlobalPosition ?? GlobalPosition;
        float f = Math.Clamp((Reach - GlobalPosition.DistanceTo(ear)) / (Reach * 0.4f), 0f, 1f);
        f *= f;
        float pitch = _doppler.Update(GlobalPosition, ear, (float)delta);      // the wail bends as the Kozlik passes
        for (int i = 0; i < frames; i++)
        {
            _env += ((On ? 1f : 0f) - _env) * 0.0004f;     // ~0.1 s in and out
            if (_env < 0.001f && !On) { _pb.PushFrame(Vector2.Zero); continue; }
            _sweep = (_sweep + 1.0 / (Sweep * MixRate)) % 1.0;
            float up = 0.5f - 0.5f * MathF.Cos((float)(_sweep * Math.Tau));
            _tone = (_tone + (Low + (High - Low) * up) * pitch / MixRate) % 1.0;
            float sq = _tone < 0.5 ? 1f : -1f;
            _lp += (sq - _lp) * 0.22f;
            float s = Math.Clamp((_lp * 0.7f + MathF.Sin((float)(_tone * Math.Tau)) * 0.3f) * _env * f * 0.5f, -1f, 1f);
            _pb.PushFrame(new Vector2(s, s));
        }
    }
}
