using System;
using CCCPRacing.Car;
using CCCPRacing.Track;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// The phases of RaceScene._PhysicsProcess. Their order preserves simulation and tape playback timing.
/// Phase order: gates, replay, car controls (RaceScene.CarControl.cs), specials, world ticks before the car, the car step,
/// feedback after the step, the roof rescue, the lap clock and the recorders.
/// </summary>
public partial class RaceScene
{
    /// <summary>What the car step produced that the later phases read (the five values that used to be locals of the one big method).</summary>
    readonly record struct CarStepResult(float SuspHit, float Pothole, float SpeedIn, Vector3 BeforeDiscovery, Vector3 StartNoseBefore);

    /// <summary>The tick's early exits and the clock-driven starts. False: this tick does nothing more (paused, booting, a viewer's own pause, waiting for the start clutch).</summary>
    bool PhysicsGates(double delta, out float dt)
    {
        dt = (float)delta;
        if(LanPlaybackSource!=null) return false;
        if (_booting) return false;
        if (!ViewerMode) TraceTick();      // (a viewer must not reopen and truncate the live scene's WORLDTRACE)
        if (ViewerMode && !ViewerStep(ref dt)) return false;      // RaceScene.Viewer.cs: the viewer's own pause and speed
        if (_state is State.Paused || SchoolFrozen) return false;
        TapeTestTick(dt);
        if (WaitForStartClutch()) return false;
        if (LanGoDue()) StartRacing();
        TrackPreviewDrive(dt);
        return true;
    }

    /// <summary>The title replay or a viewer: recordings pose the car and covered actors; uncovered actors keep their live fallback (docs/replay-tape.md section 6).</summary>
    void ReplayPhysicsTick(float dt)
    {
        long queries0 = _tapeObsCount?.Queries ?? 0;
        _replayClock += dt;
        if (ViewerMode) ViewerApplyCameraKeys(force: false);      // (before the frame that shows this clock)
        bool wrapped = false;
        if (((_replayClock >= TitleGhost.Duration && !(ViewerMode && !ViewerLoop)) || TapeWrapDue()) && !(ViewerMode && ViewerWindowed)) { _replayClock = _titleReplayStart; TitleGhost.Rewind(); _wood.Reset(); _scenery.Reset(); _cones?.Reset(); _carVis.Damage?.Reset(); wrapped = true; }
        if (wrapped) { ClearCarFeedback(); _fx.Clear(); }      // (a wrap leaves no marks, dust or mud of the lap's end; the tape's effects are restored by the seek below)
        var ev = TitleGhost.Apply(_replayClock, _car, dt);
        if (wrapped && ViewerMode) ViewerApplyCameraKeys(force: true);
        TapeReplayTick(dt, wrapped);      // the tape poses the actors it covers (they skip their live tick below)
        if (!_tapeWood) { _wood.Tick(dt); _wood.Smash(_car); }
        _scenery.Tick(dt, _car, false);
        if (_tapeProbe != null) TapeSeekProbe(dt);
        _cones?.Tick(dt, _car);
        if (!_tapeBreakables) _scenery.Smash(_car, ev.Impact);      // (a tape that recorded the breakables breaks them exactly)
        TickActors(dt, replay: true, afterCar: false);
        if (!_tapeBreakables) TankCrush();      // (a tape that recorded the breakables fells what the tanks felled; a frozen hull must not fell a tree of its own)
        _maxImpact = ev.Impact;
        _impactMaterial = ev.ImpactMaterial; _impactBroke = ev.ImpactBroke;
        _impactBirch = false;
        TickActors(dt, replay: true, afterCar: true);
        BabushkaFeedback();
        _vegetation.Tick(dt, _car);
        HitFeedback(dt, ev.Pothole, ev.Suspension);
        // A movie frame can contain several physics ticks. Rebuilding aged
        // cosmetic particles at each tick is redundant; captures restore the
        // same final tape state once immediately before the rendered frame.
        if (TitleGhost.HasDamage && !_tapeShoot) TitleGhost.ApplyDamage(_replayClock, _carVis.Damage, _fx);
        else if (!TitleGhost.HasDamage) ReplayDamage(ev.Impact);
        FenceFeedback();
        SceneryFeedback();
        UpdateBackfire(dt);
        _fx.Emit(_car, dt);
        if (EngineFuel == FuelGrade.Diesel) _fx.DieselSoot(_car, dt); else _fx.PetrolSmoke(_car, dt, EngineFuel is FuelGrade.Rocket or FuelGrade.Sputnik ? 1.5f : 1f, -1, FuelGrades.PipeXs(EngineFuel));
        if (_tapeObsCount != null) _tapeReplayQueries += _tapeObsCount.Queries - queries0;
    }

    /// <summary>What follows the control choice: the LAN authority (False: it took the tick over), the cleanups of the chase, the mechanic hold and the fuel.</summary>
    bool PreStepSpecials(float dt)
    {
        if(InLanRace) {LanAuthorityPhysics(dt); return false;}
        GaiChaseTick(dt);
        bool held = _state == State.Racing && (_drunks is { HoldsCar: true } || AutographHolds || DoseHolds);
        if (held)
        {
            // The mechanic is coming to or at work on the bonnet: the car stays where it is, engine idling.
            _car.SetControls(false, false, 0f, true);   // (braking from a standstill would select reverse)
            _car.Brake = 1;
            _car.Neutral = true;
        }
        else if (_mechanicHeld && _state == State.Racing) _car.Neutral = false;
        _mechanicHeld = held;
        FuelDrive(dt);
        return true;
    }

    /// <summary>The world's own ticks before the car moves.</summary>
    void PreStepWorldTick(float dt)
    {
        _wood.Tick(dt);
        _scenery.Tick(dt, _car, !_rescue.Active);
        _cones?.Tick(dt, _rescue.Active ? null : _car);
        TickActors(dt, replay: false, afterCar: false);
        if (_busSwerveAt > 0 && _clock >= _busSwerveAt) { _busSwerveAt = -1; _bus?.ForceSwerve(); }
        TankCrush();
    }

    /// <summary>The rescue tractor or the substeps of the car.</summary>
    CarStepResult StepCar(float dt)
    {
        _maxImpact = 0;
        _impactMaterial = ImpactMaterial.Unknown; _impactBroke = false;
        float suspHit = 0, pothole = 0, speedIn = _car.Speed;
        Vector3 beforeDiscovery = _car.Pos;
        Vector3 startNoseBefore = _car.Pos + _car.Forward * 2.11f;
        _airBefore = _car.AirTime;
        _fallBefore = -_car.Vel.Y;
        _rescue.Tick(dt, _car);
        if (_rescue.Active)
        {
            // The tractor has the car: engine ticks over, nothing else moves it.
            _car.Throttle = 0;
            _car.Brake = 0;
            _car.Rpm = Mathf.MoveToward(_car.Rpm, _params.IdleRpm, 4000f * dt);
            _car.Slide = _car.BodyScrape = 0;
            foreach (var w in _car.Wheels) w.Contact = false;
        }
        else
            for (int i = 0; i < Substeps; i++)
            {
                _car.Step(dt / Substeps, _state == State.Countdown);
                if (_car.LastImpact > _maxImpact) { _impactLocal = _car.LastImpactLocal; _impactDir = _car.LastImpactDir; _impactBirch = _car.LastImpactBirch;
                    _impactMaterial = _car.LastImpactMaterial; _impactBroke = _car.LastImpactBroke; }
                _maxImpact = Math.Max(_maxImpact, _car.LastImpact);
                suspHit = Math.Max(suspHit, _car.SuspensionHit);
                pothole = Math.Max(pothole, _car.PotholeBangStep);
            }
        return new CarStepResult(suspHit, pothole, speedIn, beforeDiscovery, startNoseBefore);
    }

    /// <summary>Everything that reacts to the step: the false start, feedback, damage, fate, fuel, dose, effects.</summary>
    void PostStepFeedback(float dt, in CarStepResult step)
    {
        float suspHit = step.SuspHit, pothole = step.Pothole, speedIn = step.SpeedIn;
        Vector3 beforeDiscovery = step.BeforeDiscovery, startNoseBefore = step.StartNoseBefore;
        CheckFalseStart(startNoseBefore);
        SnowbankFeedback();
        if (_rescue.JustTowed) FuelTowed();
        if (_rescue.JustRighted)
        {
            _snd.Thump(0.8f);
            _cam.Shake(0.5f);
            Rumb(0.3f, 0.4f, 0.15f, "back on the wheels");
        }
        if (_maxImpact > 3f && OS.HasEnvironment("DAMAGEDBG")) GD.Print($"hit {_maxImpact:F2} m/s{(_car.LastImpactBroke ? " (broke it)" : "")}, speed {speedIn * 3.6f:F1} -> {_car.Speed * 3.6f:F1} km/h at {_clock:F2} s, at ({_car.Pos.X:F0},{_car.Pos.Z:F0}){NearestObstacle()}");
        _wood.Smash(_car);
        TickActors(dt, replay: false, afterCar: true);
        BabushkaFeedback();
        _vegetation.Tick(dt, _car);

        _landedNow = false;
        LandingFeedback(suspHit);
        HitFeedback(dt, pothole, suspHit);
        RumbleLanding();
        _carVis.Damage?.Hit(_maxImpact, _impactLocal, _impactDir, _car, _fx);
        FateTick(dt);
        FuelTick(dt);
        DoseTick(dt);
        CameraDiscoveryTick(beforeDiscovery);
        BusFireHeat(dt);
        FenceFeedback();
        SceneryFeedback();
        UpdateBackfire(dt);

        _fx.Emit(_car, dt);
        if (EngineFuel == FuelGrade.Diesel) _fx.DieselSoot(_car, dt); else _fx.PetrolSmoke(_car, dt, EngineFuel is FuelGrade.Rocket or FuelGrade.Sputnik ? 1.5f : 1f, -1, FuelGrades.PipeXs(EngineFuel));
    }

    /// <summary>On the roof: the hint, the mechanic after AutoRescue s, the title's own reset.</summary>
    void UpsideDownRescue()
    {
        if (_car.UpsideDownTime > 1.5f && !_rescue.Active && !FateHasCar && !_towCalled)
        {
            // Racing: on the roof the driver presses R himself (test players liked that better than
            // waiting for the tractor); after AutoRescue s it is done for him, the same R. Demo: just reset.
            if (_state == State.Racing && !InSchool && !PlanHasCar && !FuelHasCar)
            {
                if (!_roofHinted)
                {
                    _roofHinted = true;
                    _hud.Show(Slogans.Pick(Slogans.Flip), _padMode ? "PRESS B TO GET BACK ON THE ROAD" : "PRESS R TO GET BACK ON THE ROAD", 3f, Hud.Red, Prio.Critical,
                        plain: "UPSIDE DOWN", plainSub: _padMode ? "PRESS B TO GET BACK ON THE ROAD" : "PRESS R TO GET BACK ON THE ROAD");
                }
                if (_car.UpsideDownTime > AutoRescue) CallMechanic();
            }
            else if (_state == State.Title) Rescue(0);
        }
        if (_car.UpsideDownTime <= 0f) _roofHinted = false;
    }

    /// <summary>The lap timer, the lap clock and the two recorders (the ghost and the world tape), the commentary. Runs only while racing outside the school.</summary>
    void LapClockAndRecord(float dt, in CarStepResult step)
    {
        float suspHit = step.SuspHit, pothole = step.Pothole;
        if (_state == State.Racing && !InSchool)      // the school has no laps, ghosts or commentary
        {
            if (FakeLapDue(out float fakeTime))
            {
                _fakeLap = true;
                _timer.CompleteLapForTest(fakeTime);
                _fakeLap = false;
            }
            else _timer.Update(dt, new Vector2(_car.Pos.X, _car.Pos.Z));
            WrongWayTick(dt);
            if (_state != State.Racing) return;     // name entry: the new lap's clock waits too
            _lapClock += dt;
            _rec.Record(_lapClock, _car, _maxImpact, pothole, suspHit, _dayNight.Active ? _dayNight.Clock : -1, _impactMaterial, _impactBroke, _carVis.Damage);
            TapeRecord();
            if (!PlanHasCar && !FateHasCar && !FuelHasCar) Commentary(dt);
        }
    }
}
