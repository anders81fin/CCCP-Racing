using System;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// Who drives the car this tick. The Racing state's final choice among the dev test drives, the autopilot, the pad and the keys is the pure
/// CarControlChoice (CarControlChoice.cs); everything with a side effect stays here in the order it always had: the early-exit drives (plan, wreck, LAN, school), the
/// clutch, the per-source actions, and the scenic override after the switch. Title, Countdown and Finished are unchanged.
/// </summary>
public partial class RaceScene
{
    void SelectCarControls(float dt)
    {
        switch (_state)
        {
            case State.Title:
                if (CarTourActive) CarTourDrive();      // (a car's tour revs the parked car: RaceScene.CarTour.cs)
                else if (!_droneTour) _ap.Drive(_car, dt);      // (the drone tour drives the car itself)
                break;
            case State.Countdown:
                if (StartRevControls(dt)) break;
                // The driver holds the clutch: releasing it early really drives the car.
                _car.Neutral = false;
                var rev = _driver.Read();
                _car.Clutch = rev.Clutch || _clutchKickAt > 0 || (InLanRace && _autodrive);   // held across GO: launch yourself
                if (_showKeys || _lanLeaveAsk) { _car.SetControls(false, false, 0f, true); _car.Brake = 1; }
                else _car.SetControls(_clutchKickAt > 0 ? 1f : rev.Throttle, rev.Brake, rev.Steer, rev.Handbrake, rev.PadSteer);
                break;
            case State.Racing:
                _keyThrottle = true;
                if (PlanDrive(dt) || WreckDrive()) break;
                _raceClock += dt;
                if (LanDrive(dt)) break;
                if (SchoolDrive(dt)) break;     // the driving school's demo (or scripted try)
                if (_radioSwitch > 0 && _raceClock >= 3f) { _carRadio.Select(_radioSwitch - 1); _radioSwitch = -1; }
                var ctl = _driver.Read();
                _car.Clutch = _clutchKickAt > 0 ? _raceClock < _clutchKickAt : ctl.Clutch;
                var source = CarControlChoice.Racing(new RacingControlInputs(
                    ClutchKick: _clutchKickAt > 0, Donut: _donut, Pulse: _pulse, FenceTest: _fenceTest, CrashTest: _crashTest,
                    CanisterTest: _canisterTest > 0, DepotHold: _atDepot && _autodrive && _raceClock < 9f, BabushkaCruise: _babushkaHit != 0,
                    Stomp: _stomp, Autopilot: _autodrive && !_padAuto, Pad: ctl.Pad || _padAuto));
                // The choice is pure (CarControlChoice.cs); what each source does stays here, with its side effects, as it was.
                switch (source)
                {
                    case CarControlSource.ClutchKick:
                        _car.SetControls(true, false, 0f, false);
                        if (OS.HasEnvironment("CLUTCHDBG") && Math.Abs(_raceClock - _clutchKickAt) < 0.6f)
                            GD.Print($"clutch t={_raceClock - _clutchKickAt:F3} in={_car.Clutch} rpm={_car.Rpm:F0} gear={_car.Gear} v={_car.ForwardSpeed * 3.6f:F1} spin={(_car.Wheels[2].Spinning ? 1 : 0)}{(_car.Wheels[3].Spinning ? 1 : 0)} lim={_car.Limiter}");
                        break;
                    case CarControlSource.Donut:
                        _car.SetControls(true, false, 1f, false);
                        break;
                    case CarControlSource.Pulse:
                        _car.SetControls(_raceClock % 2f < 1.2f, false, 0f, false);
                        break;
                    case CarControlSource.FenceTest:
                        _car.SetControls(true, false, 0f, false);
                        break;
                    case CarControlSource.CrashTest:
                        CrashDrive(dt);
                        break;
                    case CarControlSource.CanisterTest:
                        _car.SetControls(_car.ForwardSpeed * 3.6f < _canisterTest, false, 0f, false);
                        break;
                    case CarControlSource.DepotHold:
                        _car.SetControls(false, false, 0f, false);   // --at-depot: stand and fill first
                        break;
                    case CarControlSource.BabushkaCruise:
                        BabushkaCruise();
                        break;
                    case CarControlSource.Stomp:
                        _car.SetControls(_raceClock < 3.5f, _raceClock >= 3.5f && _car.ForwardSpeed > 1.5f, 0f, false);
                        break;
                    case CarControlSource.Autopilot:
                        _ap.Drive(_car, dt); if (_missileEvade > 0 && _jets != null) MissileEvade();
                        break;
                    case CarControlSource.Pad:
                        _keyThrottle = !(ctl.PadThrottle > 0f && !Input.IsActionPressed("accelerate"));    // the trigger's own throttle: no keys compensation
                        // A pad drives (alone or with the keys): analog pedals and steering (DriverInput).
                        if (_padAuto) PadAutoDrive(dt);
                        _lapPad = true;
                        if (InLanRace) _lanPad = true;
                        _car.SetControls(ctl.Throttle, ctl.Brake, ctl.Steer, ctl.Handbrake, ctl.PadSteer);
                        break;
                    case CarControlSource.Keys:
                        float steer = Input.GetActionStrength("steer_right") - Input.GetActionStrength("steer_left");
                        _car.SetControls(Input.IsActionPressed("accelerate"), Input.IsActionPressed("brake"),
                            steer, Input.IsActionPressed("handbrake"));
                        break;
                }
                break;
            case State.Finished:
                PlanDrive(dt);
                break;
        }

        // Scenic captions control the camera; the car keeps racing independently of each shot.
        if (_scEnvironment && !_droneTour)      // (the drone tour drives the car itself)
        {
            _car.Clutch = false;
            _car.Neutral = _sc.Total - _scEnvironmentStart < 10f;
            if (_car.Neutral) ScStop();
            else _ap.Drive(_car, dt);
        }
    }
}
