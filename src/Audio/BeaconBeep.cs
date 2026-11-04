using System;
using Godot;

namespace CCCPRacing.Audio;

/// <summary>
/// The road service beacon's warning beeper: a plain single-tone industrial buzzer, like a reversing alarm on a heavy
/// truck: about 1 kHz, a hard-edged sawtooth with a coarse buzz in it (a vibrating reed, not a clean tone), on for half
/// of each beat, its pitch dropping a little as each beep starts. Beeps once for every second flash of the beacon, heard further than the truck's diesel so a plough shows itself over a crest or behind a bend by
/// day too. It sounds only while the truck moves (<see cref="Moving"/>). Positional, Doppler-bent, faded out by
/// <see cref="Reach"/>. No samples. Decoration: like the beacon's sweep it runs on the wall clock and is not taped, but
/// the truck's own speed (which the tape carries) gates it.
/// </summary>
public partial class BeaconBeep : AudioStreamPlayer3D
{
    const float MixRate = 22050f, Duty = 0.5f, Hz = 1000f, BuzzHz = 95f;

    /// <summary>Beeps per second: one for every second flash of the beacon.</summary>
    public float BeepsPerSecond = 0.85f;
    /// <summary>Phase offset in beeps, so two trucks do not beep in step.</summary>
    public float Phase;
    /// <summary>Level in dB at 14 m (set before adding to the tree).</summary>
    public float Loudness = -3f;
    /// <summary>Silent beyond this distance (m); a straight fade from <see cref="FullTo"/> down to it.</summary>
    public float Reach = 150f;
    /// <summary>True while the truck is on the move.</summary>
    public Func<bool> Moving;
    /// <summary>Full level out to this distance (m).</summary>
    public float FullTo = 100f;

    AudioStreamGeneratorPlayback _pb;
    double _beat, _tone, _buzz;
    float _lp, _gate;
    Doppler _doppler;

    public override void _ExitTree()
    {
        _pb?.Dispose();
        _pb = null;
    }

    public override void _Ready()
    {
        Stream = new AudioStreamGenerator { MixRate = MixRate, BufferLength = 0.1f };
        // No distance attenuation of the engine's kind: the beep must be as clear far out as near, the car's own engine is on top of it.
        // The direction and the Doppler stay; the level is faded out by Reach below.
        AttenuationModel = AttenuationModelEnum.Disabled;
        MaxDistance = Reach;
        VolumeDb = Loudness;
        Bus = AudioMix.BusFor(AudioMix.Warning);      // an alert (the ALERTS level), not part of the surroundings
        Play();
        _pb = GetStreamPlayback() as AudioStreamGeneratorPlayback;
    }

    public override void _Process(double delta)
    {
        if (_pb == null) return;
        int frames = _pb.GetFramesAvailable();
        var vp = GetViewport();
        Vector3 ear = vp?.GetAudioListener3D()?.GlobalPosition ?? vp?.GetCamera3D()?.GlobalPosition ?? GlobalPosition;
        float dist = GlobalPosition.DistanceTo(ear);
        float pitch = _doppler.Update(GlobalPosition, ear, (float)delta);
        bool on = (Moving?.Invoke() ?? true) && dist < Reach;
        if (!on && _gate < 0.001f) { for (int i = 0; i < frames; i++) _pb.PushFrame(Vector2.Zero); return; }
        float f = Math.Clamp((Reach - dist) / Math.Max(1f, Reach - FullTo), 0f, 1f);      // full to FullTo, then a straight fade to nothing at Reach
        _beat = Time.GetTicksMsec() * 0.001 * BeepsPerSecond + Phase;      // the wall clock, so the beeps keep the beacon's rhythm
        double step = BeepsPerSecond / MixRate;
        for (int i = 0; i < frames; i++)
        {
            _gate += ((on ? 1f : 0f) - _gate) * 0.0004f;
            double x = _beat % 1.0;
            float into = (float)(x / BeepsPerSecond);      // seconds into this beep
            float env = x < Duty ? Math.Min(1f, into / 0.004f) * (0.85f + 0.15f * MathF.Exp(-into * 25f)) * Math.Min(1f, (float)(Duty - x) / 0.02f / BeepsPerSecond * 1f + 0.0f) : 0f;
            float hz = Hz * (1f + 0.04f * MathF.Exp(-into * 40f));      // the reed settles
            _tone = (_tone + hz * pitch / MixRate) % 1.0;
            _buzz = (_buzz + BuzzHz / MixRate) % 1.0;
            float saw = (float)(_tone * 2.0 - 1.0);
            _lp += (saw - _lp) * 0.6f;      // bright: the engine's harmonics fill the low half of the spectrum
            float rasp = _buzz < 0.5 ? 1f : 0.55f;
            float s = Math.Clamp(_lp * rasp * env * _gate * f * 0.32f, -1f, 1f);
            _pb.PushFrame(new Vector2(s, s));
            _beat += step;
        }
    }
}
