using System;
using CCCPRacing.Audio;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// SOUND BALANCE (PARTY MEETING): the mixer for the machinery, the road and the surroundings (SoundBalance). While it is
/// open the selected group plays a demonstration (the engine revving and shifting, the car rolling over concrete and gravel,
/// a tractor with its horn) so the level can be set by ear.
/// </summary>
public partial class RaceScene
{
    DieselSound _mixTractor;
    float _mixClock;

    bool MixerInput(bool alt)
    {
        if (!_hud.MixerOpen) return false;
        if (Input.IsActionJustPressed("pause")) CloseMixer();
        else if (Input.IsActionJustPressed("accelerate")) _hud.MixerSelection = (_hud.MixerSelection + _hud.MixerRowCount - 1) % _hud.MixerRowCount;
        else if (Input.IsActionJustPressed("brake")) _hud.MixerSelection = (_hud.MixerSelection + 1) % _hud.MixerRowCount;
        else if (Input.IsActionJustPressed("steer_left")) ChooseMixer(_hud.MixerSelection, -1);
        else if (Input.IsActionJustPressed("steer_right") || Input.IsActionJustPressed("start") && !alt)
            ChooseMixer(_hud.MixerSelection);
        return true;
    }

    void OpenMixer()
    {
        _hud.SetMixerOpen(true);
        SyncMixerHud();
    }

    void CloseMixer()
    {
        _hud.SetMixerOpen(false);
        DropMixerTractor();
        _overlayExitFrame = Engine.GetProcessFrames();
    }

    void SyncMixerHud()
    {
        Array.Copy(SoundBalance.Levels, _hud.MixerLevels, SoundBalance.Count);
        _hud.MixerLevels[SoundBalance.Count] = _carRadio.Volume;
    }

    /// <summary>SOUND SETTINGS: first the radio (on/off), then its station (only while the radio is on), then the balance rows as before.</summary>
    int MixerLead => _carRadio.On ? 2 : 1;

    void ChooseMixer(int row, int step = 1)
    {
        _hud.MixerSelection = row;
        if (row == 0) { _carRadio.Toggle(); RememberRadio(); }
        else if (_carRadio.On && row == 1) _carRadio.StepStation(step < 0 ? -1 : 1);
        else
        {
            row -= MixerLead;
            if (row < SoundBalance.Count)
            {
                // Step up; past the top (a click, Enter) it comes round to mute.
                if (step > 0 && SoundBalance.Levels[row] >= SoundBalance.Max) SoundBalance.Step(row, -SoundBalance.Max);
                else SoundBalance.Step(row, step);
                SoundBalance.Save(_records.Dir + "settings.cfg");
            }
            else if (row == SoundBalance.Count)      // the car radio's own volume (saved by the radio)
                _carRadio.StepVolume(step > 0 && _carRadio.Volume >= CarRadio.MaxVolume ? -CarRadio.MaxVolume : step);
            else if (row == SoundBalance.Count + 1)
            {
                SoundBalance.Reset();
                SoundBalance.Save(_records.Dir + "settings.cfg");
                _carRadio.StepVolume(CarRadio.DefaultVolume - _carRadio.Volume);
            }
            else CloseMixer();
        }
        _hud.MixerSelection = Math.Min(_hud.MixerSelection, _hud.MixerRowCount - 1);      // (turning the radio off takes the station row away)
        SyncPauseHud();
        SyncMixerHud();
    }

    // The radio on or off is remembered for each kind of run (the rally on by default, the Stakhanov laps off).
    bool RadioPreference()
    {
        var cfg = new ConfigFile();
        cfg.Load(_records.Dir + "settings.cfg");
        var v = cfg.GetValue("radio", StakhanovRules ? "on_laps" : "on_rally", !StakhanovRules);
        return v.VariantType == Variant.Type.Bool ? (bool)v : !StakhanovRules;
    }

    void RememberRadio()
    {
        if (_run is not (RunMode.Rally or RunMode.Training)) return;
        string path = _records.Dir + "settings.cfg";
        var cfg = new ConfigFile();
        cfg.Load(path);
        cfg.SetValue("radio", StakhanovRules ? "on_laps" : "on_rally", _carRadio.On);
        cfg.Save(path);
    }

    void DropMixerTractor()
    {
        if (_mixTractor == null) return;
        _mixTractor.QueueFree();
        _mixTractor = null;
    }

    /// <summary>The demonstration of the selected group, in place of the paused car's silence; true when it drives the sound.</summary>
    bool MixerPreview(float dt)
    {
        // On the RADIO row the music plays even with the radio off, so the level can be found by ear.
        int sel = _hud.MixerSelection - MixerLead;      // the balance rows after the radio's on/off and station rows
        _carRadio.SetPreview(_hud.MixerOpen && sel == SoundBalance.Count);
        if (!_hud.MixerOpen || sel < 0) { DropMixerTractor(); return false; }
        _mixClock += dt;
        float t = _mixClock, cycle = t % 3f;
        int group = sel;
        _snd.Slide = 0;
        _snd.BrakeSqueal = 0;
        _snd.Scrape = 0;
        _snd.Limiter = false;
        _snd.Driven = 1f;
        _snd.Airborne = false;
        if (group != SoundBalance.World) DropMixerTractor();
        if (group != SoundBalance.Alerts && _geiger != null) _geiger.Rate = 0f;
        switch (group)
        {
            case SoundBalance.Engine:
                // Revs up and down, an upshift's report each lap of the sweep, the brakes squealing at the end.
                _snd.Rpm = 2200f + 2000f * MathF.Sin(t * 2.1f);
                _snd.Throttle = _snd.Rpm > 2200f ? 0.8f : 0.05f;
                _snd.Speed = 14f;
                _snd.Gear = 3;
                _snd.Ground = Track.SurfaceType.Concrete;
                if (cycle < 0.03f && _mixPrevCycle > cycle) { _snd.PowerShift(0.8f); _snd.GearChange(true); }
                _snd.BrakeSqueal = cycle > 2.1f ? 0.8f : 0f;
                break;
            case SoundBalance.Road:
                // 100 km/h, concrete and then gravel, a slide in each, a bump to finish.
                bool gravel = t % 6f >= 3f;
                _snd.Rpm = 2600f;
                _snd.Throttle = 0.15f;
                _snd.Speed = 27f;
                _snd.Gear = 4;
                _snd.Ground = gravel ? Track.SurfaceType.Gravel : Track.SurfaceType.Concrete;
                _snd.Slide = cycle > 1.0f && cycle < 1.9f ? 0.8f : 0f;
                if (cycle > 2.5f && _mixPrevCycle <= 2.5f) { _snd.Thump(0.7f); _snd.Impact(4f); }
                break;
            case SoundBalance.Alerts:
                // The co-driver's calls and the dosimeter's ticks, in turn.
                _snd.Rpm = _params.IdleRpm;
                _snd.Throttle = 0;
                _snd.Speed = 0;
                _snd.Ground = Track.SurfaceType.Concrete;
                if (cycle < 0.03f && _mixPrevCycle > cycle) _paceBlip.Call(t % 6f < 3f ? 1 : -1, 3);
                if (_geiger != null) _geiger.Rate = cycle > 1.2f ? 25f : 0f;
                break;
            case SoundBalance.World:
                _snd.Rpm = _params.IdleRpm;
                _snd.Throttle = 0;
                _snd.Speed = 0;
                _snd.Ground = Track.SurfaceType.Concrete;
                MixerTractor(cycle);
                break;
        }
        _mixPrevCycle = cycle;
        return true;
    }

    float _mixPrevCycle;

    // A tractor a little way off the car's right side that now and then sounds its horn.
    void MixerTractor(float cycle)
    {
        if (_mixTractor == null)
        {
            _mixTractor = new DieselSound { BaseRate = 10f, RatePerSpeed = 2.5f, Body = 0.04f, Loudness = 3f, Reach = 110f, Speed = 4f };
            AddChild(_mixTractor);
        }
        _mixTractor.GlobalPosition = _cam.GlobalPosition + _cam.GlobalTransform.Basis * new Vector3(7f, -1f, -16f);
        if (cycle < 0.03f && _mixPrevCycle > cycle) _mixTractor.Honk(0.5f);
    }
}
