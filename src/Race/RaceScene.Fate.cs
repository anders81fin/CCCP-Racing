using System;
using CCCPRacing.Audio;
using CCCPRacing.Car;
using CCCPRacing.Track;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// Engine health, wrecks and the gulag ending. A real race run has an engine that crashes and
/// explosions wear down (less power, smoke, then it seizes); a direct hit wrecks the car. In the
/// rally a wreck is the end: the state does not send a second Pyatyorka to a saboteur. The car
/// burns or steams, a troop truck comes for the driver and the game ends on the GULAG screen, as
/// when the plan runs out of laps. In the driving school and a LAN race the old way stays: the car
/// burns or steams for ten seconds (the clock runs on), then a new Pyatyorka comes (a full tank),
/// the lap in progress void (in the rally of old it cost two laps of the norm for a destroyed car,
/// one for a seized engine). Training laps and the title demo have no health.
/// </summary>
public partial class RaceScene
{
    public const string ReasonPlan = "PLAN NOT FULFILLED";
    public const string ReasonDestroyed = "STATE PROPERTY DESTROYED", ReasonSeized = "ENGINE SEIZED. SABOTAGE!";
    const float WreckTruckDelay = 4f;     // s of the burning or steaming wreck before the truck sets off

    readonly EngineHealth _health = new();
    EngineEffects _engineFx;
    GulagTruck _truck;
    bool _failed, _gulag, _truckCalled;
    string _failReason;
    float _failClock, _truckDelay, _runClock;
    // Tests: --health=N (percent at the start of a run), --destroy-at=T[,T...], --gulag-at=T (process clock).
    float _startHealth = 1f, _gulagAt = -1;
    readonly System.Collections.Generic.Queue<float> _destroyAt = new();
    // Test: --seize-at=T (process clock) seizes the engine.
    float _seizeAt = -1;

    const float WreckTime = 10f;      // s of burning wreck before the new Pyatyorka
    const float WreckGrace = 3f;      // s after a respawn in which direct hits only dent and shove
    const int DestroyedLaps = 2, SeizedLaps = 1;   // what a wreck costs the plan
    // The wreck phase: the car is out of the player's hands, the countdown runs (not while paused).
    bool _wreck, _wreckOut;
    float _wreckLeft, _grace;
    int _wrecks;

    /// <summary>The engine of the current run (read-only use: Health, Dead, Destroyed, PowerFactor).</summary>
    public EngineHealth CarEngine => _health;
    /// <summary>The mission has failed: the car is out of the player's hands until the GULAG screen.</summary>
    public bool MissionHasFailed => _failed;

    /// <summary>Engine power multiplier for this run (1 in training and the demo).</summary>
    float EnginePower => _health.PowerFactor;

    void InitFate()
    {
        _engineFx = new EngineEffects { Name = "EngineEffects" };
        AddChild(_engineFx);
        _engineFx.Init(_track, _fx);
        _truck = new GulagTruck { Name = "GulagTruck" };
        AddChild(_truck);
        _truck.Init(_track, _fx);
        _truck.WarningShot += car =>
        {
            _carVis.Damage?.ShootHeadlamp(car, _fx);
            _cam.Shake(0.35f);
            if (InSchool) ShotSoon("gulag_warning_shot", 0.02f);
        };
        foreach (string a in CmdArgs())
        {
            if (a.StartsWith("--health=")) _startHealth = float.Parse(a["--health=".Length..], System.Globalization.CultureInfo.InvariantCulture) / 100f;  // test: engine health in percent at the start of a run
            else if (a.StartsWith("--destroy-at="))  // test: a direct hit on the car at these times (T[,T...])
                foreach (string t in a["--destroy-at=".Length..].Split(',')) _destroyAt.Enqueue(float.Parse(t, System.Globalization.CultureInfo.InvariantCulture));
            else if (a.StartsWith("--seize-at=")) _seizeAt = float.Parse(a["--seize-at=".Length..], System.Globalization.CultureInfo.InvariantCulture);  // test: the engine seizes at this time
            else if (a.StartsWith("--gulag-at=")) _gulagAt = float.Parse(a["--gulag-at=".Length..], System.Globalization.CultureInfo.InvariantCulture);  // test: MissionFailed (plan not fulfilled) at this time
        }
    }

    /// <summary>A new run (or the title): fresh engine, clean car, no truck.</summary>
    void ResetFate(bool enabled)
    {
        _health.Reset(enabled);
        if (enabled && !_raceRestart && _startHealth < 1f) _health.Set(_startHealth);
        _engineFx?.Reset();
        _drunks?.ResetMechanic();
        _truck?.Reset();
        _failed = _gulag = _truckCalled = false;
        _failClock = 0;
        if (!_planRestart) _runClock = 0;
        _wreck = _wreckOut = false;
        _wreckLeft = _grace = 0;
        if (!_planRestart) _wrecks = 0;
        _snd.EngineLevel = 1f;
        _snd.Misfire = 0;
        ApplyTrainingPower();
    }

    /// <summary>
    /// A direct hit (tank shell, Balalaika, a missile): fireball, the car is thrown and burns out, and a
    /// new one costs two laps of the plan. Only in a real race; training laps shrug it off, and
    /// just after a respawn it only dents and shoves the car.
    /// </summary>
    public void DestroyCar(Vector3 hitPoint, string cause)
    {
        if (_state != State.Racing || _failed || _wreck) return;
        if (_grace > 0 && _health.Enabled)
        {
            _car.Blast(hitPoint, 1f);
            _carVis.Damage?.Hit(14f, _car.Rot.Transposed() * (hitPoint - _car.Pos), (_car.Pos - hitPoint).Normalized(), _car, _fx);
            _snd.Bang(0.6f);
            _snd.Impact(6f);
            _cam.Shake(1f);
            if (OS.HasEnvironment("ENGINEDBG")) GD.Print($"engine: {cause} at {_clock:F2} s in the respawn grace: dent only, health {_health.Health * 100:F0}%");
            return;
        }
        if (_health.Destroy(cause) != EngineHealth.Change.Destroyed) return;
        bool gulag = WreckIsTheEnd;
        _car.Blast(hitPoint, 2.2f, 8f);
        _car.Vel += Vector3.Up * 3f;
        _engineFx.Explode(_car, _carVis, hitPoint);
        _carVis.Damage?.Hit(22f, _car.Rot.Transposed() * (hitPoint - _car.Pos), (_car.Pos - hitPoint).Normalized(), _car, _fx);
        AddChild(new ExplosionSound { Position = _car.Pos, Loudness = 2f });
        _snd.Bang(1f);
        _snd.Impact(12f);
        _snd.EngineLevel = 0;
        _cam.Shake(2.2f);
        _hud.Show("DIRECT HIT!", $"{cause} - THE PYATYORKA IS NO MORE", 3.5f, Hud.Red, Prio.Critical);
        // A tank that got the car has made its point: the revenge is over.
        if (cause == "TANK SHELL" && _tanks is { Chasing: true }) _tanks.EndRevenge();
        if (gulag) WreckToGulag(ReasonDestroyed);
        else EnterWreck(DestroyedLaps, "THE PEOPLE'S PYATYORKA WAS DESTROYED");
    }

    /// <summary>
    /// The plan has run out (the lap budget, possibly after a wreck): the clock stops, the car is
    /// no longer driven, the truck comes and the GULAG screen follows. Ignored in training laps
    /// and outside a race.
    /// </summary>
    public void MissionFailed(string reason)
    {
        if (_state != State.Racing || _failed || StakhanovRules) return;
        _hud.Show("MISSION FAILED", reason, 3f, Hud.Red, Prio.Critical);
        EnterFailed(reason, 1.2f);
    }

    void EnterFailed(string reason, float truckDelay)
    {
        _failed = true;
        _failReason = reason;
        _failClock = 0;
        _truckDelay = truckDelay;
        _state = State.Failed;
        _hud.Mode = HudMode.Race;
        _rescue.Reset();
        _pops = 0;
        // Test contract: an unattended autodrive lap run that cannot finish says so and ends (exit 3). Needs BOTH flags; normal play is untouched.
        if (_autodrive && _tapeQuitLaps > 0)
        {
            GD.Print($"AUTODRIVE FAILED: {reason} at lap clock {_timer.LapTime:F2}, lap {_timer.LapsDone + 1}");
            GetTree().Quit(3);
        }
        if (OS.HasEnvironment("ENGINEDBG")) GD.Print($"mission failed: {reason} at {_clock:F2} s, lap {_timer.LapsDone + 1}, clock {_timer.LapTime:F2}");
    }

    /// <summary>A wreck ends the run (the rally; not the school, not a LAN race), unless the plan already has the car.</summary>
    bool WreckIsTheEnd => !InSchool && !InLanRace && _norms.Enabled && !PlanHasCar;

    /// <summary>The rally's wreck: no new car. The clock stops, the wreck burns (or steams) and the truck comes for the saboteur.</summary>
    void WreckToGulag(string reason)
    {
        _pops = 0;
        _timer.Void();
        _kgb.LogWreck();
        _hud.Alert("GAME OVER, TOVARISH!", reason == ReasonDestroyed ? "THE STATE WILL NOT SEND ANOTHER PYATYORKA" : "THE PEOPLES ENGINE, RUINED. THE TRUCK IS COMING", 4f, Hud.Red);
        EnterFailed(reason, WreckTruckDelay);
    }

    /// <summary>
    /// The car is wrecked (destroyed or seized) in the school or a LAN race: the plan pays laps for a new one and the lap in
    /// progress is void. The wreck stands for ten seconds; then a new Pyatyorka, or, if the plan
    /// has no lap left for it, the out-of-laps ending. While the plan already has the car (out of
    /// laps, braking) only the wreck is left: the plan's own ending goes on.
    /// </summary>
    void EnterWreck(int laps, string text)
    {
        _pops = 0;
        if (PlanHasCar) return;
        _wreck = true;
        _wreckLeft = WreckTime;
        _wrecks++;
        _rescue.Reset();
        _timer.Void();
        _wreckOut = _norms.Penalize(laps);
        _hud.Alert(text, $"COST: {laps} LAP{(laps == 1 ? "" : "S")} OF THE NORM", 4f, Hud.Red);
        _kgb.LogWreck();
        if (OS.HasEnvironment("ENGINEDBG") || OS.HasEnvironment("PLANDBG"))
            GD.Print($"wreck {_wrecks} at {_clock:F2} s: -{laps} laps, norm {_norms.Step + 1}, left {_norms.Left}{(_wreckOut ? " OUT OF LAPS" : "")}");
    }

    /// <summary>Per physics tick in a race: the wreck is out of the player's hands. True while it is.</summary>
    bool WreckDrive()
    {
        if (!_wreck) return false;
        _car.Neutral = true;
        _car.SetControls(false, false, 0f, false);
        _car.Brake = 0.7f;
        return true;
    }

    /// <summary>Ten seconds are up: bring a new car, or end an exhausted plan.</summary>
    void EndWreck()
    {
        _wreck = false;
        if (_wreckOut)
        {
            PlanOutOfLaps(alert: true);
            return;
        }
        Rescue(0);
        _car.Neutral = false;
        _health.Reset(true);
        _carVis.Damage?.Reset();
        _engineFx.Reset();
        _snd.EngineLevel = 1f;
        _snd.Misfire = 0;
        FuelNewTank();                  // a new Pyatyorka comes with a full tank
        ApplyTrainingPower();
        _grace = WreckGrace;
        if (OS.HasEnvironment("ENGINEDBG") || OS.HasEnvironment("PLANDBG"))
            GD.Print($"respawn at {_clock:F2} s: health {_health.Health * 100:F0}%, dented {_carVis.Damage?.IsDamaged ?? false}, lap void {_timer.LapVoid}, norm {_norms.Step + 1}, left {_norms.Left}, power {EnginePower:F2}");
    }

    /// <summary>The car is a wreck (standing, or left for the truck): no R, no tractor, no mechanic.</summary>
    bool FateHasCar => _wreck || (_health.Enabled && _health.Dead);

    /// <summary>Per physics tick, after the car has moved: damage, effects and the failure sequence.</summary>
    void FateTick(float dt)
    {
        if (_state == State.Racing)
        {
            _runClock += dt;
            _grace = Math.Max(0, _grace - dt);
            if (_wreck && (_wreckLeft -= dt) <= 0) EndWreck();
            if (_destroyAt.Count > 0 && _clock >= _destroyAt.Peek())
            {
                _destroyAt.Dequeue();
                DestroyCar(_car.Pos + _car.Forward * 1.2f + Vector3.Up * 0.2f, "TEST SHELL");
            }
            else if (_seizeAt > 0 && _clock >= _seizeAt)
            {
                _seizeAt = -1;
                HealthChanged(_health.Damage(1f, "test seize"));
            }
            else if (_gulagAt > 0 && _clock >= _gulagAt)
            {
                _gulagAt = -1;
                MissionFailed(ReasonPlan);
            }
            else if (_health.Dead && !_failed && !_wreck && !PlanHasCar && _health.Enabled) EngineDied();   // --health=0
            else HealthChanged(_health.Impact(_maxImpact, dt));
        }
        // A sick engine misfires, more the closer it is to the end.
        _snd.Misfire = _health.Smoking ? 0.3f + 0.7f * (EngineHealth.SmokeAt - _health.Health) / EngineHealth.SmokeAt : 0f;
        _engineFx.Tick(dt, _car, _health);

        if (_state != State.Failed) return;
        // Out of the player's hands: clutch out of the dead engine, brakes on.
        _car.Neutral = true;
        _car.SetControls(false, false, 0f, false);
        _car.Brake = 0.7f;
        _failClock += dt;
        if (OS.HasEnvironment("ENGINEDBG") && (int)(_failClock * 2) != (int)((_failClock - dt) * 2))
            GD.Print($"failed t={_failClock:F1} v={_car.Speed:F1} thr={_car.Throttle:F2} brake={_car.Brake:F2} gear={_car.Gear} state={_state} pos=({_car.Pos.X:F0},{_car.Pos.Z:F0})");
        if (!_truckCalled && _failClock >= _truckDelay)
        {
            _truckCalled = true;
            _truck.Call(_car);
        }
        _truck.Tick(dt, _car);
        if (_truck.Gone && !_gulag) EnterGulag();
    }

    /// <summary>The engine is worn or the body dented: the mechanic has something to do.</summary>
    bool CarNeedsMechanic => _health.Health < 0.995f || (_carVis.Damage?.IsDamaged ?? false);

    /// <summary>A sledgehammer blow on the bonnet: clang, a jolt, sparks and a puff from the engine.</summary>
    void OnMechanicWhack(Vector3 at, bool big)
    {
        if(InLanRace && _net.IsHost)
        {
            var target=_lanMechanicTarget;
            if(target==null) return;
            AddChild(new ClangSound { Strength = big ? 1f : 0.6f });
            float lanKeep=1f-_drunksAll.RepairProgress;
            if(target.Car==_car)
            {
                _cam.Shake(big ? .5f : .25f);
                _carVis.Damage?.Straighten(lanKeep/target.DentsKept);
                var lanRng=new RandomNumberGenerator();
                for(int i=0;i<(big?14:7);i++)
                    _fx.Burst(at,new Vector3(lanRng.RandfRange(-2.5f,2.5f),lanRng.RandfRange(1.5f,4f),lanRng.RandfRange(-2.5f,2.5f)),new Color(1f,.8f,.3f),1f,.09f,.03f,.35f,9f,.4f,false);
                for(int i=0;i<3;i++) _fx.Smoke(at+Vector3.Up*.1f,new Vector3(lanRng.RandfRange(-.6f,.6f),lanRng.RandfRange(1.5f,2.5f),lanRng.RandfRange(-.6f,.6f)),new Color(.35f,.35f,.33f),.7f,.5f,1.8f,1.1f);
            }
            target.DentsKept=Math.Max(lanKeep,.001f);
            target.Car.Vel+=Vector3.Down*(big?1.2f:.7f);
            return;
        }
        if (OS.HasEnvironment("DRUNKDBG")) GD.Print($"mechanic: whack at {_clock:F2} s{(big ? " (big)" : "")}");
        AddChild(new ClangSound { Strength = big ? 1f : 0.6f });
        _cam.Shake(big ? 0.5f : 0.25f);
        float keep = 1f - _drunksAll.RepairProgress;      // each blow knocks the dents out a little further
        _carVis.Damage?.Straighten(keep / _dentsKept);
        _dentsKept = Math.Max(keep, 0.001f);
        _car.Vel += Vector3.Down * (big ? 1.2f : 0.7f);
        var rng = new RandomNumberGenerator();
        rng.Seed = unchecked((uint)_drunksAll.LastWhackSeed);
        DrunkMen.WhackParticles(_fx, rng, at, big);
    }

    float _repairFrom, _driveOffT, _dentsKept = 1f;

    /// <summary>The player holds the throttle while the mechanic walks over or works: he gives up (the repair can be broken off any time).</summary>
    void MechanicDriveOff(float dt)
    {
        bool wants = _drunks is { HoldsCar: true } && !_autodrive && !InSchool
            && (Input.IsActionPressed("accelerate") || _driver.Read().Throttle > 0.3f);
        _driveOffT = wants ? _driveOffT + dt : 0;
        if (_driveOffT > 0.25f) { _driveOffT = 0; _drunksAll.Interrupt(); }
    }

    /// <summary>While he hammers, the engine's health climbs evenly, like the tank filling.</summary>
    void MechanicRepairTick()
    {
        if (!_drunksAll.Repairing) { _repairFrom = -1; return; }
        if (_repairFrom < 0) { _repairFrom = _health.Health; _dentsKept = 1f; }
        _health.RepairTo(_repairFrom + (1f - _repairFrom) * _drunksAll.RepairProgress);
        ApplyTrainingPower();
    }

    void OnMechanicRepairInterrupted()
    {
        if(InLanRace && _net.IsHost)
        {
            if(LanMechanicIsLocal) _hud.Show("REPAIR BROKEN OFF",$"ENGINE AT {Math.Round(_lanMechanicTarget?.Health.Health*100 ?? _health.Health*100)} %",2.5f,Hud.Paper);
            return;
        }
        _hud.Show("REPAIR BROKEN OFF", $"ENGINE AT {Math.Round(_health.Health * 100)} %", 2.5f, Hud.Paper);
    }

    /// <summary>The last blow: engine and body as they left the factory.</summary>
    void OnMechanicRepaired()
    {
        if(InLanRace && _net.IsHost)
        {
            var target=_lanMechanicTarget;
            if(target==null) return;
            target.Health.Repair();target.Dented=false;target.RepairSerial++;target.BrokenHeadlights=0;target.RepairFrom=-1;target.DentsKept=1;
            if(target.Car==_car)
            {
                _carVis.Damage?.Reset();
                ApplyTrainingPower();
            }
            if(LanMechanicIsLocal) _hud.Show("AS GOOD AS NEW!","THE MECHANIC'S FEE: A BOTTLE",3.5f,Hud.Gold,plain:"REPAIRED");
            return;
        }
        _health.Repair();
        _carVis.Damage?.Reset();
        ApplyTrainingPower();
        _hud.Show("AS GOOD AS NEW!", "THE MECHANIC'S FEE: A BOTTLE", 3.5f, Hud.Gold, plain: "REPAIRED");
    }

    void HealthChanged(EngineHealth.Change c)
    {
        switch (c)
        {
            case EngineHealth.Change.Damaged:
                ApplyTrainingPower();
                break;
            case EngineHealth.Change.Smoking:
                ApplyTrainingPower();
                _hud.Show("THE ENGINE IS SMOKING!", "NURSE IT ALONG, TOVARISH", 3f, Hud.Red, plain: "ENGINE SMOKING");
                break;
            case EngineHealth.Change.Died:
                EngineDied();
                break;
        }
    }

    /// <summary>The engine seizes: bang, rods clattering, oil on the road, steam. In the rally the end; elsewhere a new engine.</summary>
    void EngineDied()
    {
        ApplyTrainingPower();
        _snd.Bang(1f);
        _snd.Seize();
        _snd.EngineLevel = 0;
        _snd.Impact(6f);
        _cam.Shake(1f);
        _engineFx.Seize(_car);
        _hud.Show("THE ENGINE HAS SEIZED!", "OIL ON THE ROAD. SABOTAGE IS SUSPECTED", 3.5f, Hud.Red, Prio.Critical);
        if (WreckIsTheEnd) WreckToGulag(ReasonSeized);
        else EnterWreck(SeizedLaps, "ENGINE REPLACED AT STATE EXPENSE");
    }

    void EnterGulag()
    {
        _gulag = true;
        _overlayFrame = Engine.GetProcessFrames();
        _hud.ClearMessages();
        _hud.Mode = HudMode.Gulag;
        _hud.GulagReason = _failReason;
        _hud.GulagAfter = BronzeStakhanovNote() ?? "";
        _hud.GulagLaps = _timer.LapsDone;
        _hud.GulagRunTime = _runClock;
        _hud.GulagLastLap = _timer.LapsDone > 0 && !_timer.LastLapVoid ? _timer.LastLap : -1;
        _hud.EndWrecks = _wrecks;
        _snd.Volume = 0f;
        _carRadio.Suspend(true);
        if (OS.HasEnvironment("ENGINEDBG")) GD.Print($"GULAG screen at {_clock:F2} s: {_failReason}");
    }

    /// <summary>GULAG screen: any key, mouse or joypad button returns to the Single Player menu.</summary>
    bool GulagInput(InputEvent e)
    {
        if (!_gulag) return false;
        if (Engine.GetProcessFrames() <= _overlayFrame) return true;
        bool press = e is InputEventKey { Pressed: true, Echo: false } or InputEventMouseButton { Pressed: true }
            or InputEventJoypadButton { Pressed: true };
        if (!press) return true;
        _gulag = false;
        _overlayExitFrame = Engine.GetProcessFrames();
        if (_lastPlayed == MenuItem.Race) ReturnRallyToTrackSelect();
        else { EnterTitle(); OpenMenu(MenuItem.Single, _lastPlayed); }
        return true;
    }

    /// <summary>Esc during the failure sequence skips straight to the GULAG screen once the truck is on its way.</summary>
    void FateInput()
    {
        if (!_gulag && _truckCalled && Input.IsActionJustPressed("pause")) EnterGulag();
    }

    /// <summary>During the failure sequence the camera goes with the truck once it leaves.</summary>
    bool FateCamera(float dt)
    {
        if (_state != State.Failed || !_truck.Active) return false;
        if (_truck.Leaving)
            _cam.Follow(_truck.Position3D, _truck.Velocity, dt);
        else if (_truck.Boarding)
            _cam.Follow(_car.Pos.Lerp(_truck.Position3D, 0.4f), Vector3.Zero, dt);
        else return false;
        return true;
    }

    void FateHud()
    {
        _hud.EngineHealth = _health.Enabled ? _health.Health : -1f;
        _hud.WreckCountdown = InLanRace ? (int)MathF.Ceiling(_lanWreckLeft) : _wreck && !_wreckOut && _state == State.Racing ? (int)MathF.Ceiling(_wreckLeft) : 0;
        _hud.Wrecks = _norms.Enabled ? _wrecks : 0;
        if (_state == State.Failed && !_gulag) _hud.Mode = HudMode.Race;
    }
}
