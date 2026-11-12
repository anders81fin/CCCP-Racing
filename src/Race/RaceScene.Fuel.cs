using System;
using System.Collections.Generic;
using System.Globalization;
using CCCPRacing.Car;
using CCCPRacing.Track;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// Fuel in the rally (FuelTank): the engine drinks by throttle and revs, the gauge sits beside the
/// speedo. Near empty it sputters (throttle cuts, misfires, pops); at empty it dies and the car
/// coasts as far as its speed carries it: a babushka's can or the depot on the way revives it with
/// no penalty. Once it has stopped the MT-Agro comes and drags it off; the lap is void, the norm's
/// window loses a lap (TowLaps) and the car is put down at the fuel depot with a full tank, just
/// short of the line. Stopping at the depot (FuelDepot) fills the tank at 6 l/s while
/// the race clock runs; babushkas hand out canisters (20 l each). A wreck brings a new car
/// with a full tank, as does Restart Race; the mechanic leaves the tank as it is. Stakhanov laps,
/// LAN races with consumption off, the title and the first track (TrackDef.Fuel false) have no fuel at all.
/// </summary>
public partial class RaceScene
{
    /// <summary>What a tow to the depot costs the norm's window.</summary>
    public const int TowLaps = 1;

    FuelTank _tank;
    bool _fuelOnTrack = true;     // TrackDef.Fuel: no fuel at all on the first track
    DepotDef _depotDef;            // TrackDef.Depot: where the depot stands (null: before the line)
    FuelDepot _depot;
    bool _dry, _towCalled, _lowWarned, _refuelling;
    float _dryClock, _sputterCd, _sputterCut, _lapFuel, _refuelFrom;
    int _fuelLaps;
    // OCTANE BOOSTER: a golden can gives 350 % for BoostTime (the engine drinks double); one golden can is
    // out at a time, handed to a carrier at GO and at every line while none is out and no boost runs.
    public const float BoostTime = 20f;
    /// <summary>OCTANE BOOSTER's power: 3.5 times the fuel's (175, 350 or 500 %).</summary>
    int BoostPercent => FuelGrades.BoostPercent(_run == RunMode.Rally ? _rallyFuel : FuelGrade.Petrol);
    /// <summary>The rally's power in percent: the fuel's (the title and the school run on petrol).</summary>
    int RallyPercent => _run == RunMode.Rally ? FuelGrades.PhysicsPercent(_rallyFuel) : 100;      // (the diesel's character is its engine, not a share of the power)
    float _boostLeft;
    PlayerPowerTape _powerTape;
    bool _lapBoost, _goldenTest, _canisterTestResume;
    // The throttle came from the keys (or the autopilot): all or nothing, it drinks KeysRate.
    bool _keyThrottle = true;
    // Tests: --fuel=F (tank fraction at the start of a rally), --fuel-rate=X, --canister-test[=KMH]
    // (--canister-miss=M), --at-depot, --shot-on=EVENT:SECONDS (toss, refuel, dry, tow, towed).
    float _fuelStart = 1f, _fuelRate = 1f, _canisterTest, _canisterMiss = 2f;
    bool _atDepot;
    readonly Dictionary<string, float> _shotOn = new();
    static readonly bool FuelDebug = OS.HasEnvironment("FUELDBG");

    /// <summary>The car's tank (read-only use).</summary>
    public FuelTank Tank => _tank;

    /// <summary>After the babushkas and the KGB: the tank, the depot and the canisters' news.</summary>
    void InitFuel()
    {
        _powerTape = new PlayerPowerTape(
            () => new(_boostLeft, _car.PowerScale, _car.Overdrive, _carVis.Golden, _radBoost),
            state => {
                _boostLeft = state.BoostLeft; _car.PowerScale = state.Power;
                _car.Overdrive = state.Overdrive; _carVis.Golden = state.Golden;
                _hud.FuelBoost = state.BoostLeft;
                _radBoost = state.RadBoost; _hud.RadBoost = state.RadBoost;
                _cam.MaxLead = state.Overdrive > 0 ? 8f : float.PositiveInfinity;
            });
        AddChild(_powerTape);
        _tank = new FuelTank(_params);
        _depot = new FuelDepot { Name = "FuelDepot" };
        AddChild(_depot);
        _depot.Init(_track, _depotDef);
        foreach (string a in CmdArgs())
        {
            if (a.StartsWith("--fuel=")) _fuelStart = float.Parse(a["--fuel=".Length..], CultureInfo.InvariantCulture);  // test: the rally starts with this fraction of a tank
            else if (a.StartsWith("--fuel-rate=")) _fuelRate = float.Parse(a["--fuel-rate=".Length..], CultureInfo.InvariantCulture);  // test: consumption this many times the tuned one
            else if ((a == "--canister-test" || a.StartsWith("--canister-test="))) _canisterTest = a.Contains('=') ? float.Parse(a[(a.IndexOf('=') + 1)..], CultureInfo.InvariantCulture) : 50f;  // test: at GO a carrier holds out her can 40 m ahead; the car passes her at this km/h (default 50)
            else if (a.StartsWith("--canister-miss=")) _canisterMiss = float.Parse(a["--canister-miss=".Length..], CultureInfo.InvariantCulture);  // test: --canister-test passes her this far off (m, default 2)
            else if (a == "--at-depot") _atDepot = true;
            else if (a == "--canister-test-resume") _canisterTestResume = true; // test: return steering to autodrive after the real catch
            else if (a == "--golden-test") _goldenTest = true;      // test: the --canister-test can is the golden one              // test: at GO the car stands at the fuel depot
            else if (a.StartsWith("--shot-on="))                      // test: screenshot this long after a fuel event: toss, refuel, dry, tow, towed
            {
                var ev = a["--shot-on=".Length..].Split(':');
                _shotOn[ev[0]] = ev.Length > 1 ? float.Parse(ev[1], CultureInfo.InvariantCulture) : 0f;
                _shotAt = 1e9f;
            }
        }
        if (_babushkas == null) return;
        _babushkas.CanTossed += _ => FuelShot("toss");
        _babushkas.CanCaught += (_, golden) => CanisterIn(golden);
        _babushkas.CanCaughtForCar += LanCanisterIn;
        _babushkas.CanBurst += (_, at) => FuelSplash(at);
    }

    /// <summary>A new run (or the title): a full tank (or --fuel) in the rally, no fuel otherwise. Restart Race always fills it.</summary>
    void ResetFuel(bool enabled)
    {
        enabled &= _fuelOnTrack;
        _chime?.Reset();      // a new run: the dashboard's lights start out
        if (_babushkasAll != null) _babushkasAll.Canisters = enabled;
        _tank.Enabled = enabled;
        _tank.Rate = _fuelRate * (enabled && _run == RunMode.Rally ? FuelGrades.Burn[(int)_rallyFuel] : 1f);
        _tank.Refill(enabled && !_raceRestart ? _fuelStart : 1f);
        _dry = _towCalled = _lowWarned = _refuelling = false;
        _dryClock = _sputterCut = _lapFuel = 0;
        _fuelLaps = 0;
        EndBoost(false);
        _depot.Pouring = false;
        if (enabled && FuelDebug) GD.Print($"fuel: new rally, {_tank.Litres:F1} l of {_tank.Capacity:F0}, rate x{_tank.Rate:F2}");
    }

    /// <summary>The engine is dry or the tractor is towing it: no R, no restart, no commentary.</summary>
    bool FuelHasCar => _dry || _rescue.Towing;

    void FuelShot(string ev)
    {
        if (_shotOn.Remove(ev, out float after)) _shotAt = _clock + after;
    }

    /// <summary>Test --canister-test and --at-depot at GO.</summary>
    void StartFuelTests()
    {
        if (_canisterTest > 0) _babushkas?.AimPass(_car, 1, 60, _canisterTest / 3.6f, 40f, _canisterMiss, _goldenTest);
        GoldenCan();
        if (_atDepot) _car.Reset(new Vector3(_depot.ParkPos.X, _track.MeshHeight(_depot.ParkPos.X, _depot.ParkPos.Y) + 0.55f, _depot.ParkPos.Y), _depot.ParkYaw);
    }

    /// <summary>
    /// Per physics tick after the driver's controls: near empty the throttle cuts out now and then
    /// (a pop from the exhaust each time); dry, the engine is dead and the car rolls in neutral.
    /// </summary>
    void FuelDrive(float dt)
    {
        if (!_tank.Enabled || _state != State.Racing || _wreck) return;
        if (_dry)
        {
            _car.ThrottleTarget = _car.Throttle = 0;
            _car.Neutral = true;
            return;
        }
        if (!_tank.Sputtering) { _sputterCut = 0; return; }
        _sputterCut -= dt;
        _sputterCd -= dt;
        if (_sputterCd <= 0)
        {
            _sputterCd = _rng.RandfRange(0.15f, 0.6f);
            _sputterCut = _rng.RandfRange(0.06f, 0.25f);
            if (_car.ThrottleTarget > 0.2f)
            {
                float k = _rng.RandfRange(0.3f, 0.6f);
                _snd.Bang(k);
                _fx.ExhaustFlame(_car, k, FuelGrades.PipeXs(EngineFuel));
            }
        }
        if (_sputterCut > 0) _car.ThrottleTarget = 0;
    }

    /// <summary>Per physics tick after the car has moved: drinking, the depot, the low warning, running dry and the tow.</summary>
    void FuelTick(float dt)
    {
        if (!_tank.Enabled) return;
        bool racing = _state == State.Racing, free = racing && !_wreck && !_dry && !_rescue.Active;
        if (free) _lapFuel += _tank.Burn(dt, _car.Throttle, _car.Rpm, (_keyThrottle ? FuelTank.KeysRate : 1f) * (_boostLeft > 0 ? FuelTank.BoostRate : 1f));
        BoostTick(dt, racing);
        // Starts below 99 % (a full tank ticking over at the depot does not flicker) and stops at the brim:
        // 99.9 %, topped up to full there. Not 100 %: the idling engine sips the tank below it every
        // frame, so the pump (and its gurgle) would never stop while the car waits at the depot.
        bool coasting = racing && _dry && !_towCalled && !_rescue.Active && !_wreck;    // dry but still rolling: the depot can save it
        bool fill = (free || coasting) && _tank.Fraction < (_refuelling ? 0.999f : 0.99f) && _depot.FillsCar(new Vector2(_car.Pos.X, _car.Pos.Z), _car.Speed);
        if (fill) { _tank.Fill(FuelDepot.FillRate * dt); if (coasting) EngineRevived(); }
        if (fill != _refuelling)
        {
            _refuelling = fill;
            if (fill)
            {
                _refuelFrom = _tank.Litres;
                _hud.Show("REFUELLING", "THE CLOCK KEEPS RUNNING, TOVARISH", 2.5f, Hud.Paper, plain: "REFUELLING");
                FuelShot("refuel");
            }
            else if (_tank.Fraction >= 0.999f)
            {
                _tank.Refill();
                if (_tank.Litres - _refuelFrom > 1f) _hud.Show("FULL TANK", "GLORY TO THE KOLKHOZ DEPOT", 2f, Hud.Gold, plain: "FULL TANK");
            }
            if (FuelDebug) GD.Print(fill ? $"fuel: refuelling at {_clock:F2} s from {_tank.Litres:F1} l" : $"fuel: refuelled {_tank.Litres - _refuelFrom:F1} l, tank {_tank.Litres:F1} l at {_clock:F2} s");
        }
        _depot.Pouring = fill;
        _depot.Level = _tank.Fraction;
        if (!_tank.Low) _lowWarned = false;
        else if (!_lowWarned && racing && !_dry && _countdown <= -1f && _goAlert == null)     // after the start's norm alert
        {
            _lowWarned = true;
            // With rally cards enabled, CardWatch owns both the first explanation and repeats.
            if (!CardsOn) _hud.Alert("FUEL LOW!", "FIND FUEL OR STOP AT THE DEPOT", 3.5f, Hud.Gold);
            if (FuelDebug) GD.Print($"fuel: LOW at {_clock:F2} s, {_tank.Litres:F1} l");
        }
        if (racing && _tank.Sputtering && !_wreck) _snd.Misfire = Math.Max(_snd.Misfire, 0.55f);
        if (racing && !_dry && _tank.Empty && !_wreck && !PlanHasCar && !_failed) EngineDry();
        if (_dry && racing) DryTick(dt);
    }

    /// <summary>The last drop: the engine dies, the car coasts in neutral.</summary>
    void EngineDry()
    {
        _dry = true;
        _towCalled = false;
        _dryClock = 0;
        _snd.EngineLevel = 0;
        _snd.Bang(0.45f);
        _pops = 0;
        _hud.Show("THE TANK IS EMPTY!", _trackId=="bonus"?"COAST TO THE SUPPLY TROOPS OR DEPOT":"COAST TO A BABUSHKA OR THE DEPOT, TOVARISH", 3.5f, Hud.Red, Prio.Critical);
        if (FuelDebug) GD.Print($"fuel: DRY at {_clock:F2} s, {_car.Speed * 3.6f:F0} km/h");
        FuelShot("dry");
    }

    /// <summary>Dry: the car rolls as far as its speed carries it (a babushka or the depot can still save it); once it has stopped the tractor comes, it costs a lap of the norm and the lap is void.</summary>
    void DryTick(float dt)
    {
        _dryClock += dt;
        if (_towCalled || _rescue.Active || _failed) return;
        if (!(_car.Speed < 0.5f && _dryClock > 0.8f)) return;
        _towCalled = true;
        // A school demo shows the real tow without spending attempts in the player's plan.
        if (!InSchool) { _timer.Void(); _kgb.LogWreck(); }
        bool outOfLaps = !InSchool && _norms.Penalize(TowLaps);
        if (FuelDebug || OS.HasEnvironment("PLANDBG")) GD.Print($"fuel: tow called at {_clock:F2} s: -{TowLaps} lap, norm {_norms.Step + 1}, left {_norms.Left}{(outOfLaps ? " OUT OF LAPS" : "")}");
        if (outOfLaps)
        {
            PlanOutOfLaps(alert: true);
            return;
        }
        _rescue.CallTow(_car);
        _hud.Alert("TOWED TO THE DEPOT", $"-{TowLaps} LAP OF THE NORM. THE LAP IS VOID", 4f, Hud.Red);
        _kgb.SayLiaison($"ОСТАЛОСЬ {_norms.Left}");
        FuelShot("tow");
    }

    /// <summary>The tow is done: the car stands at the depot with a full tank, the void lap ends at the line (just ahead, or a lap's rest away).</summary>
    void FuelTowed()
    {
        Vector2 p = _depot.ParkPos;
        _car.Reset(new Vector3(p.X, _track.MeshHeight(p.X, p.Y) + 0.55f, p.Y), _depot.ParkYaw);
        _car.Neutral = false;
        FuelNewTank();
        if (_depot.AtLine) _timer.SkipToLine();
        else _timer.PutAt(_depot.Metre);         // a depot out on the lap (SIBERIA's at the runway's end): the void lap goes on from there
        _cam.Snap(_car.Pos);
        if (FuelDebug) GD.Print($"fuel: towed to the depot at {_clock:F2} s, full tank");
        FuelShot("towed");
    }

    /// <summary>A full tank and a running engine (after a tow, or a new car after a wreck).</summary>
    void FuelNewTank()
    {
        if (!_tank.Enabled) return;
        _tank.Refill();
        EndBoost(false);
        _dry = _towCalled = _lowWarned = false;
        _sputterCut = 0;
        _snd.EngineLevel = _health.Dead ? 0f : 1f;
    }

    /// <summary>A carrier gets the golden can if none is out and no boost runs (rally only).</summary>
    void GoldenCan()
    {
        if (_tank.Enabled && _boostLeft <= 0 && _babushkas != null && !_babushkas.GoldenOut) _babushkas.MakeGolden(_rng);
    }

    /// <summary>The golden can is in: 350 % (overdrive) for BoostTime, the car turns gold.</summary>
    void StartBoost(float litres)
    {
        _boostLeft = BoostTime;
        _lapBoost = true;
        ApplyTrainingPower();
        _hud.Alert($"OCTANE BOOSTER! {BoostPercent}% POWER", $"+{litres:0} LITRES. {BoostTime:0} SECONDS OF HEROIC LABOUR", 3.5f, Hud.Gold, Prio.Important);
        _kgb.Say("ГЕРОЙ ТРУДА!", approve: true, duration: 3f);
        if (FuelDebug) GD.Print($"fuel: OCTANE BOOSTER at {_clock:F2} s, {BoostPercent} % for {BoostTime:0} s");
    }

    void BoostTick(float dt, bool racing)
    {
        if (_powerTape?.Driven == true) return;
        _hud.FuelBoost = _boostLeft;
        if (_boostLeft <= 0 || !racing) return;
        _boostLeft -= dt;
        _lapBoost = true;
        if (_boostLeft <= 0) EndBoost(true);
    }

    /// <summary>The boost is over (alert: burnt out while driving; else a reset, a new car or the tow).</summary>
    void EndBoost(bool alert)
    {
        bool was = _boostLeft > 0 || alert;
        _boostLeft = 0;
        _hud.FuelBoost = 0;
        if (!was) return;
        ApplyTrainingPower();
        if (alert && !CardsOn) _hud.Show("OCTANE BOOSTER USED UP", $"BACK TO THE PLAN: {RallyPercent}%", 2.5f, Hud.Paper, Prio.Nice);
        if (FuelDebug) GD.Print($"fuel: boost over at {_clock:F2} s");
    }

    /// <summary>A babushka's can landed in the car: 20 l (and a dry engine that has not been towed yet runs again).</summary>
    void CanisterIn(bool golden)
    {
        if (InLanRace || !_tank.Enabled || _state != State.Racing) return;
        float got = _tank.Fill(Babushkas.CanisterLitres);
        if (golden) StartBoost(got);
        else _hud.Alert(_trackId=="bonus"?"SPASIBO, TOVARISH! +FUEL":"SPASIBO, BABUSHKA! +FUEL", $"+{got:0} LITRES IN THE TANK", 3f, Hud.Gold, Prio.Important);
        if (_canisterTestResume) _canisterTest = 0;
        if (FuelDebug) GD.Print($"fuel: canister +{got:F1} l at {_clock:F2} s, tank {_tank.Litres:F1} l");
        EngineRevived();
    }

    /// <summary>Fuel reached a dry engine that has not been towed yet: it runs again, no penalty.</summary>
    void EngineRevived()
    {
        if (!_dry || _towCalled || _rescue.Active || _tank.Litres <= 0) return;
        _dry = false;
        _car.Neutral = false;
        _snd.EngineLevel = 1f;
        _hud.Show("THE ENGINE LIVES!", "NO TRACTOR NEEDED, TOVARISH", 2.5f, Hud.Green, plain: "ENGINE RUNNING");
        if (FuelDebug) GD.Print($"fuel: engine revived at {_clock:F2} s, {_car.Speed * 3.6f:F0} km/h");
    }

    /// <summary>A canister burst against the bumper: a splash of fuel and red tin.</summary>
    void FuelSplash(Vector3 at, bool sound = true)
    {
        for (int i = 0; i < 16; i++)
            _fx.Burst(at, new Vector3(_rng.RandfRange(-3f, 3f), _rng.RandfRange(1f, 4f), _rng.RandfRange(-3f, 3f)),
                new Color(0.85f, 0.72f, 0.3f), 0.8f, 0.12f, 0.05f, 0.6f, 9f, 0.5f, false);
        for (int i = 0; i < 5; i++)
            _fx.Burst(at, new Vector3(_rng.RandfRange(-2.5f, 2.5f), _rng.RandfRange(2f, 4f), _rng.RandfRange(-2.5f, 2.5f)),
                new Color(0.75f, 0.1f, 0.07f), 1f, 0.12f, 0.12f, 1.2f, 9f, 0.2f, true);
        if (sound) _snd.Thump(0.3f);
    }

    /// <summary>A lap (valid or void) reached the line: FUELDBG prints what it drank; a new golden can if none is out.</summary>
    void FuelLap(float time)
    {
        if (!_tank.Enabled) return;
        GoldenCan();
        _fuelLaps++;
        if (FuelDebug) GD.Print($"fuel: lap {_fuelLaps} ({LapTimer.Format(time)}): {_lapFuel:F2} l, tank {_tank.Litres:F1} l ({_tank.Fraction * 100:F0} %)");
        _lapFuel = 0;
    }

    void FuelHud()
    {
        _hud.Fuel = _tank.Enabled ? _tank.Fraction : -1f;
        _hud.FuelLitres = _tank.Litres;
        _hud.Refuelling = _refuelling;
    }
}
