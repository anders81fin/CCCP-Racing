using System;
using Godot;

namespace CCCPRacing.Audio;

/// <summary>
/// A big fire (the burning bus): a low roar of flames with sharp crackles and the odd pop of
/// something giving way, like the explosion's burning-debris tail but lasting. <see cref="Level"/>
/// 1 = blazing, lower = smouldering. Heard from ~15 m, gone by <see cref="Reach"/>.
/// </summary>
public partial class FireSound : AudioStreamPlayer3D
{
    const float MixRate = 22050f;

    public float Level = 1f;
    public float Reach = 90f;
    AudioStreamGeneratorPlayback _pb;
    float _lp, _lp2, _pop, _popLp, _fade, _lvl, _t;
    readonly Random _rng = new();

    /// <summary>Restore an already burning replay's envelope instead of fading it up as a new fire.</summary>
    public void RestoreAge(float age)
    {
        _t = Math.Max(0f, age);
        _lvl = Level * (1f - MathF.Exp(-0.0005f * MixRate * _t));
        var vp = GetViewport();
        Vector3 ear = vp?.GetAudioListener3D()?.GlobalPosition ?? vp?.GetCamera3D()?.GlobalPosition ?? GlobalPosition;
        float f = Math.Clamp((Reach - GlobalPosition.DistanceTo(ear)) / (Reach * 0.4f), 0f, 1f);
        _fade = f * f;
    }

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
        UnitSize = 12f;
        MaxDistance = Reach;
        VolumeDb = 2f;
        Bus = AudioMix.BusFor(AudioMix.Boom);
        Play();
        _pb = GetStreamPlayback() as AudioStreamGeneratorPlayback;
    }

    public override void _Process(double delta)
    {
        if (_pb == null) return;
        int frames = _pb.GetFramesAvailable();
        // Smooth fade over the last 40 % of the reach, like the diesels.
        var vp = GetViewport();
        Vector3 ear = vp?.GetAudioListener3D()?.GlobalPosition ?? vp?.GetCamera3D()?.GlobalPosition ?? GlobalPosition;
        float f = Math.Clamp((Reach - GlobalPosition.DistanceTo(ear)) / (Reach * 0.4f), 0f, 1f);
        float fk = frames > 0 ? 1f / frames : 0f;
        for (int i = 0; i < frames; i++)
        {
            _lvl += (Level - _lvl) * 0.0005f;
            float n = (float)_rng.NextDouble() * 2f - 1f;
            // Roar: deep filtered noise that breathes a little.
            _lp += (n - _lp) * 0.02f;
            _lp2 += (_lp - _lp2) * 0.05f;
            _t += 1f / MixRate;
            float roar = _lp2 * 5f * (0.8f + 0.2f * MathF.Sin(_t * 1.7f) * MathF.Sin(_t * 0.6f));
            // Crackle: sparse sharp clicks, now and then a louder pop with a short tail.
            float crackle = _rng.Next(220) == 0 ? n * 0.7f : 0f;
            if (_rng.Next(9000) == 0) _pop = 0.9f;
            _pop *= 0.9992f;
            _popLp += (n * _pop - _popLp) * 0.3f;
            _fade += (f * f - _fade) * fk;
            float s = (roar * (0.3f + 0.7f * _lvl) + (crackle + _popLp) * (0.4f + 0.6f * _lvl)) * _fade;
            s = Math.Clamp(s, -1f, 1f);
            _pb.PushFrame(new Vector2(s, s));
        }
    }
}
