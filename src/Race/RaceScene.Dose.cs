using System;
using System.Collections.Generic;
using System.Globalization;
using CCCPRacing.Audio;
using CCCPRacing.Car;
using CCCPRacing.Track;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// The zone's radiation (TrackDef.Radiation, ATOMGRAD-4). The dosimeter clicks on every run on the
/// track (GeigerSound), faster in the hot spots (the red forest, by block 4) and off the hard road.
/// In the rally the dose adds up (the DOSE gauge beside the fuel): over the limit, the dosimetrist at
/// the checkpoint stops the car at the line for a check and a wash-down on the spot (DoseCheckTime);
/// the decontamination station beside the birch avenue washes it off quicker: drive into its bay
/// slowly and the dose runs down while the clock runs. Tests: --dose=F (the rally starts at this
/// share of the limit), --at-wash (the car starts at the wash, nose in); env DOSEDBG.
/// </summary>
public partial class RaceScene
{
    const float DoseCheckTime = 10f, WashRate = 8f, WashSpeed = 30f / 3.6f;
    const float WashHalfX = 3.4f, WashHalfZ = 8f;

    RadiationDef _rad;
    ZoneDosimetrist _dosimetrist;      // the figure at the checkpoint, knocked over like the other people
    GeigerSound _geiger;
    float _dose, _doseRate, _doseCheckT, _doseStart, _doseDbg;
    bool _doseOn, _doseWarned, _doseOver, _washing, _atWash, _radEngineWarned;
    bool _doseSchoolMock;

    /// <summary>At GO: the zone's word in the rally; test --at-wash: the car at the bay's north end, pointing in (south).</summary>
    void StartDoseTests()
    {
        if (_doseOn) _hud.Show("THE ZONE: WATCH YOUR DOSE", $"OVER {_rad.Limit:0} MSV THE DOSIMETRIST STOPS YOU. WASH AT THE BIRCHES", 4f, Hud.Gold, Prio.Important);
        if (!_atWash || _rad == null) return;
        _car.Reset(new Vector3(_rad.WashX, _track.MeshHeight(_rad.WashX, _rad.WashZ - WashHalfZ - 4f) + 0.55f, _rad.WashZ - WashHalfZ - 4f), TrackData.YawFor(new Vector2(0, 1)));
    }
    static readonly bool DoseDebug = OS.HasEnvironment("DOSEDBG");

    /// <summary>After the track is built: the dosimeter (on a track with radiation).</summary>
    void InitDose(TrackDef def)
    {
        _rad = def.Radiation;
        if (_rad == null) return;
        AddChild(Atomgrad.HotSpotGlow(_track, _rad));
        _geiger = new GeigerSound { Name = "Geiger" };
        AddChild(_geiger);
        foreach (string a in CmdArgs())
            if (a.StartsWith("--dose=")) _doseStart = float.Parse(a["--dose=".Length..], CultureInfo.InvariantCulture);   // test: the rally starts at this share of the limit
            else if (a == "--at-wash") _atWash = true;      // test: at GO the car stands at the wash's entrance, nose in
    }

    /// <summary>A new run: the dose counts in the rally only (from nothing, or --dose).</summary>
    void ResetDose(bool rally)
    {
        _doseSchoolMock = false;
        if (_rad == null) return;
        _doseOn = rally;
        _dose = rally && !_raceRestart ? _doseStart * _rad.Limit : 0f;
        _doseCheckT = 0;
        _doseWarned = _doseOver = _washing = _radEngineWarned = false;
    }

    /// <summary>The dose rate (mSv/s) at a point: background and the hot spots, off the hard road times OffRoad.</summary>
    float DoseRateAt(Vector2 p, bool offRoad)
    {
        float r = _rad.Background;
        foreach (var h in _rad.HotSpots)
        {
            float d = p.DistanceTo(new Vector2(h.X, h.Z));
            if (d < h.R) { float k = 1f - d / h.R; r += h.Rate * k * k; }
        }
        return offRoad ? r * _rad.OffRoad : r;
    }

    /// <summary>The car in the wash station's bay (along Z through its open ends).</summary>
    bool InWash(Vector2 p) => MathF.Abs(p.X - _rad.WashX) < WashHalfX && MathF.Abs(p.Y - _rad.WashZ) < WashHalfZ;

    /// <summary>Per physics tick after the car has moved: the clicks, the dose, the wash, the dosimetrist's check.</summary>
    void DoseTick(float dt)
    {
        if (_rad == null) return;
        var p = new Vector2(_car.Pos.X, _car.Pos.Z);
        var s = _car.Wheels[2].Surface;
        bool offRoad = s is not (SurfaceType.Concrete or SurfaceType.Pothole or SurfaceType.Runway or SurfaceType.Shallow);
        _doseRate = DoseRateAt(p, offRoad);
        bool live = _state is State.Racing or State.Countdown or State.Finished;
        _geiger.Rate = live ? 1.2f + _doseRate * 55f : 0f;
        if (!_doseOn) return;

        bool racing = _state == State.Racing, free = racing && !_wreck && !_rescue.Active && _doseCheckT <= 0;
        if (free) _dose += _doseRate * dt;
        // The radiation eats the engine too, the more the stronger it is (EngineHealth.RadiationLoss).
        if (free && _health.Enabled && !_health.Dead)
        {
            float loss = EngineHealth.RadiationLoss(_doseRate, dt);
            if (loss > 0f)
            {
                HealthChanged(_health.Damage(loss, "RADIATION"));
                if (!_radEngineWarned && _doseRate > 0.4f)
                {
                    _radEngineWarned = true;
                    _hud.Show("THE RADIATION IS EATING THE ENGINE", "GET OUT OF THE HOT SPOT, TOVARISH", 3f, Hud.Red, Prio.Important, plain: "RADIATION DAMAGE");
                }
            }
        }

        // The wash: slow in the bay, the dose runs down; a spray over the car.
        bool wash = free && InWash(p) && _car.Speed < WashSpeed && _dose > 0f;
        if (wash)
        {
            _dose = Math.Max(0f, _dose - WashRate * dt);
            if (_rng.Randf() < dt * 40f)
                _fx.Burst(_car.Pos + new Vector3(_rng.RandfRange(-1.5f, 1.5f), 3.2f, _rng.RandfRange(-2f, 2f)), Vector3.Down * 3f + new Vector3(_rng.RandfRange(-1f, 1f), 0, _rng.RandfRange(-1f, 1f)),
                    new Color(0.75f, 0.85f, 1f), 0.6f, 0.25f, 0.6f, 0.7f, 6f, 0.6f, false);
        }
        if (wash != _washing)
        {
            _washing = wash;
            if (wash) _hud.Show("DECONTAMINATION", "THE CLOCK KEEPS RUNNING, TOVARISH", 2.5f, Hud.Paper, plain: "DECONTAMINATION");
            else if (_dose <= 0.01f) _hud.Show("CLEAN AS A NEW KOPECK", "GLORY TO SOVIET HYGIENE", 2.5f, Hud.Gold, plain: "DOSE CLEARED");
            if (DoseDebug) GD.Print($"dose: {(wash ? "washing" : "out of the wash")} at {_clock:F1} s, {_dose:F1} mSv");
        }
        if (_dose >= 0.7f * _rad.Limit && !_doseWarned && racing)
        {
            _doseWarned = true;
            _hud.Alert("YOUR DOSE IS HIGH", "WASH AT THE DECONTAMINATION STATION BY THE BIRCHES", 3.5f, Hud.Gold);
        }
        if (_dose < 0.5f * _rad.Limit) _doseWarned = false;
        bool over = _dose >= _rad.Limit;
        if (over && !_doseOver && racing)
            _hud.Alert("DOSE OVER THE NORM!", "THE DOSIMETRIST WILL STOP YOU AT THE CHECKPOINT", 3.5f, Hud.Red, Prio.Important);
        _doseOver = over;

        // The dosimetrist's check: the car is held, then washed down on the spot.
        if (_doseCheckT > 0)
        {
            _doseCheckT -= dt;
            if (_doseCheckT <= 0)
            {
                _dose = 0;
                _hud.Show("CHECKED AND WASHED", "DRIVE ON, TOVARISH", 2f, Hud.Paper, plain: "CHECKED AND WASHED");
                if (DoseDebug) GD.Print($"dose: check done at {_clock:F1} s");
            }
        }
        _doseDbg -= dt;
        if (DoseDebug && _doseDbg <= 0)
        {
            _doseDbg = 3f;
            GD.Print($"dose: {_dose:F2} mSv of {_rad.Limit:F0}, rate {_doseRate * 1000f:F0} µSv/s{(offRoad ? " off-road" : "")} at ({p.X:F0},{p.Y:F0}), t {_clock:F1}");
        }
    }

    /// <summary>The car crosses the line (a lap or a void lap): over the limit, the dosimetrist stops it.</summary>
    void DoseAtLine()
    {
        if (!_doseOn || _dose < _rad.Limit || _doseCheckT > 0) return;
        _doseCheckT = DoseCheckTime;
        _hud.Show("DOSIMETRIC CHECK!", $"{_dose:0} mSv. {DoseCheckTime:0} SECONDS OF PAPERWORK AND A HOSE", 3.5f, Hud.Red, Prio.Critical, urgent: true);
        if (DoseDebug) GD.Print($"dose: the dosimetrist stops the car at {_clock:F1} s, {_dose:F1} mSv");
    }

    /// <summary>The car stands still while the dosimetrist checks it (as for the mechanic).</summary>
    bool DoseHolds => _doseCheckT > 0 && _state == State.Racing;

    /// <summary>The dosimetrist's word over the checkpoint while he checks the car.</summary>
    IEnumerable<(Vector3 pos, string text)> DoseShouts()
    {
        if (_doseCheckT <= 0) yield break;
        Vector3 at = _dosimetrist?.HeadAt ?? _car.Pos + _car.Right * 2.2f + Vector3.Up * 4.1f;
        yield return (at, _doseCheckT > DoseCheckTime - 4f ? "ФОНИТ!" : _doseCheckT > 2f ? "ДЕЗАКТИВАЦИЯ" : "СВОБОДЕН");
    }

    void DoseHud()
    {
        if (_doseSchoolMock) return; // the lesson's staged example is drawn over the live, inactive rally gauge
        // The car glows green from 30 % of the limit, more with the dose; over it, it throbs.
        float g = _doseOn ? Math.Clamp((_dose / _rad.Limit - 0.3f) / 0.7f, 0f, 1f) : 0f;
        if (_dose >= _rad.Limit) g *= 0.75f + 0.25f * MathF.Sin(_clock * 6f);
        _carVis.SetRadGlow(g, _dayNight.Glow);
        _hud.Dose = _doseOn ? _dose / _rad.Limit : -1f;
        _hud.DoseMsv = _dose;
        _hud.DoseRate = _doseRate;
        _hud.Washing = _washing || _doseCheckT > 0;
    }
}
