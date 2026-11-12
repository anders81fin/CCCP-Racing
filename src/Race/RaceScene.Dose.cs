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
    /// <summary>The radiation power on top of the engine (0..0.15, from the dose collected), and the engine power it is applied to (set by ApplyTrainingPower).</summary>
    float _radBoost, _powerBase = 1f;
    /// <summary>A LAN race on a radiation track with the damage vote on: the car's dose share (host: exact; client: read from its boost), shown on the same gauge and hint as the rally's. Cleared at every round start (StartLanRace).</summary>
    readonly LanRadiationState _lanRad = new();

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
        SetRadBoost(0f);
    }

    /// <summary>The radiation power: PowerScale = the engine's own power x (1 + boost). The tape records PowerScale and the boost itself (PlayerPowerTape).</summary>
    void SetRadBoost(float boost)
    {
        if (MathF.Abs(boost - _radBoost) < 1e-4f) return;
        _radBoost = boost;
        if (_hud != null) _hud.RadBoost = boost;
        if (_car != null && _powerTape?.Driven != true) _car.PowerScale = _powerBase * (1f + boost);
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
        SetRadBoost(free && _health.Enabled && !_health.Dead ? EngineHealth.RadiationBoost(_dose / _rad.Limit) : 0f);      // the dose collected so far gives the power, the wash takes it away
        // The radiation eats the engine too, the more the stronger it is (EngineHealth.RadiationLoss).
        if (free && _health.Enabled && !_health.Dead)
        {
            float loss = EngineHealth.RadiationLoss(_doseRate, dt) + EngineHealth.ContaminationLoss(_dose / _rad.Limit, dt);
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

    /// <summary>LAN: this car's dose share (host: exact; client: from the boost it is sent), the boost and the dose rate, for the gauge and the hint (DoseHud).</summary>
    void LanRadiationHud(float doseShare, float boost, float rate) => _lanRad.Set(doseShare, boost, rate);

    void DoseHud()
    {
        if (_doseSchoolMock) return; // the lesson's staged example is drawn over the live, inactive rally gauge
        bool lan = InLanRace && _lanRad.On;
        var (hudShare, boost) = _lanRad.View(InLanRace, _doseOn ? _dose / _rad.Limit : 0f, _doseOn, _radBoost);      // negative share: no gauge
        float share = Math.Max(0f, hudShare);
        // The car glows green from 30 % of the limit, more with the dose; over it, it throbs.
        float g = Math.Clamp((share - 0.3f) / 0.7f, 0f, 1f);
        if (share >= 1f) g *= 0.75f + 0.25f * MathF.Sin(_clock * 6f);
        _carVis.SetRadGlow(g, _dayNight.Glow);
        _hud.Dose = hudShare;
        _hud.DoseMsv = share * _rad.Limit;
        _hud.DoseRate = lan ? _lanRad.Rate : _doseRate;
        _hud.Washing = !lan && (_washing || _doseCheckT > 0);
        _hud.RadBoost = boost;
    }
}

/// <summary>
/// The radiation HUD of a LAN race (ATOMGRAD-4) and its life cycle: set while the car has a dose and a boost (the host from its own driver, a client from the PowerScale it is sent),
/// cleared at every round start so a round without the damage vote (or on another track) shows no gauge and no hint, whatever the round before left. Pure state, tested without a scene.
/// </summary>
internal sealed class LanRadiationState
{
    public bool On { get; private set; }
    public float Share { get; private set; }
    public float Boost { get; private set; }
    public float Rate { get; private set; }

    public void Set(float doseShare, float boost, float rate) { On = true; Share = doseShare; Boost = boost; Rate = rate; }
    public void Reset() { On = false; Share = Boost = Rate = 0f; }

    /// <summary>What the HUD shows: the dose share (negative: no gauge) and the boost. In a LAN race with this state set it is the LAN's, otherwise the rally's.</summary>
    public (float DoseShare, float Boost) View(bool inLanRace, float rallyShare, bool rallyOn, float rallyBoost)
        => inLanRace && On ? (Share, Boost) : (rallyOn ? rallyShare : -1f, rallyBoost);
}
