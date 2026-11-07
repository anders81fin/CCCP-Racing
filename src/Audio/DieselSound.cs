using System;
using Godot;

namespace CCCPRacing.Audio;

/// <summary>
/// Positional diesel thump for the kolkhoz machinery (tractor, bus): firing pulses whose rate
/// follows speed, a cough for hiccups/backfires and a two-tone horn. Loud right next to the car,
/// a distant drone at 50 m and faded out completely by <see cref="Reach"/>.
/// </summary>
public partial class DieselSound : AudioStreamPlayer3D
{
    const float MixRate = 22050f;

    public float Speed;
    public bool Dozing;
    public enum EngineVoice { Utility, Tank, Armoured, Petrol }
    EngineVoice _voice;
    public EngineVoice Voice
    {
        get=>_voice;
        set
        {
            _voice=value;
            UnitSize=value switch {EngineVoice.Tank=>65,EngineVoice.Armoured=>45,EngineVoice.Petrol=>30,_=>14};
            // The volume stops growing at this gain: the tank's and the armoured car's rumble used to be capped at 0 dB (all of it within 65 and 45 m), so going right up to one did not make it louder;
            // now it keeps growing to +9 / +6 dB (inside ~23 m of a tank, ~22 m of a BTR).
            MaxDb=value switch {EngineVoice.Utility=>3,EngineVoice.Tank=>9,EngineVoice.Armoured=>6,_=>0};
            AttenuationFilterDb=value==EngineVoice.Utility?-24:-12;
        }
    }
    /// <summary>Firings per second at idle and extra per m/s of speed.</summary>
    public float BaseRate = 13f, RatePerSpeed = 4f;
    /// <summary>Lower = deeper, boomier engine note.</summary>
    public float Body = 0.06f;
    /// <summary>Level in dB at 14 m (set before adding to the tree).</summary>
    public float Loudness = 2f;
    /// <summary>Faded out completely at this distance (m); the fade starts at 60 % of it.</summary>
    public float Reach = 100f;
    /// <summary>A heavy truck's air horn instead of the old bus horn: a low, menacing three-tone
    /// blast (a tritone at the bottom) that bends up to pitch as the air comes, growling.</summary>
    public bool AirHorn;
    /// <summary>A snowplough: the blade's low rumble (snow and ice dragged along the road) joins the engine, as heavy as the truck's speed, and the whole voice carries
    /// further and hits harder close by. The load follows <see cref="Speed"/> (which the tape carries), so a replay rumbles the same.</summary>
    public bool Plough;

    AudioStreamGeneratorPlayback _pb;
    double _phase, _bodyPhase, _hornPhase1, _hornPhase2, _hornPhase3;
    float _hornT, _hornLp;
    float _env, _lp, _cough, _horn, _fade, _rumbleA, _rumbleB, _rumbleLoad;
    readonly Random _rng = new(1986);

    public override void _ExitTree()
    {
        using var driverLock = AudioMix.LockDriver();
        using var generator = Stream as AudioStreamGenerator;
        // Drop the player's native playback ownership before the managed wrapper.
        StreamPaused = false; Stop();
        Stream = null;
        if (GodotObject.IsInstanceValid(_pb)) _pb.Stop(); _pb?.Dispose();
        _pb = null;
    }

    public override void _Ready()
    {
        Stream = new AudioStreamGenerator { MixRate = MixRate, BufferLength = 0.1f };
        // Inverse distance: capped at +3 dB close by, -9 dB at 50 m, then faded to silence below.
        Voice=_voice;
        if (Plough) { UnitSize = 44f; MaxDb = 9f; AttenuationFilterDb = -6f; }      // a heavy truck: loud close by, its low end carried out
        MaxDistance = Reach;
        VolumeDb = Loudness;
        Bus = AudioMix.BusFor(AudioMix.Diesel);
        Play();
        _pb = GetStreamPlayback() as AudioStreamGeneratorPlayback;
    }

    public void Cough() => _cough = 1f;

    /// <summary>Two-tone horn blast (~0.6 s, or as long as asked).</summary>
    public void Honk(float seconds = 0.6f)
    {
        _horn = 1f;
        _hornLen = seconds;
        _hornT = 0;
    }

    float _hornLen = 0.6f;
    Doppler _doppler;

    public override void _Process(double delta)
    {
        if (_pb == null) return;
        int frames = _pb.GetFramesAvailable();
        float rate = Dozing ? BaseRate * 0.7f : BaseRate + Speed * RatePerSpeed;
        // Smooth fade over the last 40 % of the reach, instead of Godot's hard cut at MaxDistance.
        var vp = GetViewport();
        Vector3 ear = vp?.GetAudioListener3D()?.GlobalPosition ?? vp?.GetCamera3D()?.GlobalPosition ?? GlobalPosition;
        float f = Math.Clamp((Reach - GlobalPosition.DistanceTo(ear)) / (Reach * 0.4f), 0f, 1f);
        f *= f;
        float pitch = _doppler.Update(GlobalPosition, ear, (float)delta);      // passing by bends the note down
        rate *= pitch;
        float fk = frames > 0 ? 1f / frames : 0f;
        for (int i = 0; i < frames; i++)
        {
            _phase += rate / MixRate;
            if (_phase >= 1)
            {
                _phase -= 1;
                _env = 0.7f + (float)_rng.NextDouble() * 0.3f;
            }
            float n = (float)_rng.NextDouble() * 2f - 1f;
            _lp += (n - _lp) * Body;
            float thump = (_lp * 2.2f + (float)Math.Sin(_phase * Math.Tau * 2) * 0.3f) * _env;
            _env *= 0.9975f;
            _bodyPhase=(_bodyPhase+rate*.5f/MixRate)%1;
            float engine=thump*.28f;
            if(Voice!=EngineVoice.Utility)
            {
                float fundamental=(float)Math.Sin(_bodyPhase*Math.Tau);
                float firing=(float)Math.Sin(_phase*Math.Tau);
                // Continuous crankcase/exhaust energy remains present at idle between firing pulses.
                engine=Voice switch {
                    EngineVoice.Tank=>thump*.80f+_lp*1.3f+fundamental*.30f+firing*.18f,
                    EngineVoice.Armoured=>thump*.68f+_lp*.9f+fundamental*.18f+firing*.20f+n*.035f,
                    _=>thump*.65f+_lp*.35f+fundamental*.10f+firing*.15f+n*.025f };
                engine=MathF.Tanh(engine*1.3f)*.80f;
                if (Voice == EngineVoice.Tank) engine *= 0.5f; // half the tank's rumble; other vehicle voices retain their level
            }

            float cough = 0;
            if (_cough > 0.001f)
            {
                cough = n * _cough * 0.6f;
                _cough *= 0.9994f;
            }

            float horn = 0;
            if (_horn > 0.001f && AirHorn)
            {
                _hornT += 1f / MixRate;
                float bend = 1f - 0.12f * MathF.Exp(-_hornT / 0.09f);      // the air pressure builds
                _hornPhase1 += 103.8 * bend * pitch / MixRate;
                _hornPhase2 += 146.8 * bend * pitch / MixRate;
                _hornPhase3 += 207.6 * bend * pitch / MixRate;
                float saw = (float)(_hornPhase1 % 1) * 2f - 1f + (float)(_hornPhase2 % 1) * 2f - 1f + ((float)(_hornPhase3 % 1) * 2f - 1f) * 0.6f;
                _hornLp += (saw - _hornLp) * 0.3f;
                float env = Math.Min(1f, _hornT / 0.06f) * Math.Min(1f, _horn * 8f);
                horn = MathF.Tanh(_hornLp * 1.4f) * 0.42f * env;
                _horn -= 1f / (_hornLen * MixRate);
            }
            else if (_horn > 0.001f)
            {
                // Two slightly sour square-ish tones, like an old bus horn.
                _hornPhase1 += 330.0 * pitch / MixRate;
                _hornPhase2 += 415.0 * pitch / MixRate;
                float h = (_hornPhase1 % 1 < 0.5 ? 1f : -1f) + (_hornPhase2 % 1 < 0.5 ? 1f : -1f);
                horn = h * 0.18f * Math.Min(1f, _horn * 6f);
                _horn -= 1f / (_hornLen * MixRate);
            }

            float rumble = 0;
            if (Plough)
            {
                // Noise through two slow low-pass poles: a growl under 80 Hz, beating with the engine's firing.
                _rumbleLoad += (Math.Clamp(Speed * 0.35f, 0f, 1f) - _rumbleLoad) * 0.0003f;
                _rumbleA += (n - _rumbleA) * 0.03f;
                _rumbleB += (_rumbleA - _rumbleB) * 0.06f;
                rumble = MathF.Tanh(_rumbleB * 10f) * (0.6f + 0.2f * (float)Math.Sin(_phase * Math.Tau)) * _rumbleLoad * 0.5f; // half the blade rumble, with the engine and horn unchanged
            }

            _fade += (f - _fade) * fk;
            float s = Math.Clamp((engine + rumble + cough * .8f + horn) * _fade, -1f, 1f);
            _pb.PushFrame(new Vector2(s, s));
        }
    }
}
