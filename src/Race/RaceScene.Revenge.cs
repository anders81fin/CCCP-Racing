using System;
using CCCPRacing.Audio;
using CCCPRacing.Car;
using CCCPRacing.Track;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// The revenge babushkas. Each run one babushka is secretly the Balalaika pilot's grandmother and another
/// the tank driver's; nothing gives them away until the car runs one over. The pilot's grandmother:
/// the next sortie hunts the car with both missiles, and the revenge ends when that jet goes
/// down. The tank driver's grandmother: both T-72s hold their firing positions, turn their guns on the
/// car and fire ten shells at it between them, then go back to their exercise. Only a race run triggers them (training
/// too, where a hit costs nothing); the title demo and a new run reset everything.
/// </summary>
public partial class RaceScene
{
    static readonly bool RevengeDbg = OS.HasEnvironment("REVENGEDBG");
    // Tests: --revenge-jet / --revenge-tanks start a revenge at GO.
    bool _revengeJetTest, _revengeTanksTest;
    // Test (--missile-dry): missile outcomes are only logged, the car is never damaged by them.
    bool _missileDry;
    MissileWarning _missileWarn;

    void InitRevenge()
    {
        foreach (string a in CmdArgs())
        {
            if (a == "--pilot-mother" && _babushkas != null) _babushkas.ForcePilotMother = true;      // test: babushka 0 is the Balalaika pilot's mother
            else if (a == "--tank-mother" && _babushkas != null) _babushkas.ForceTankMother = true;   // test: babushka 0 is the tank driver's mother
            else if (a == "--revenge-jet") _revengeJetTest = true;       // test: the Balalaika revenge starts at GO
            else if (a == "--revenge-tanks") _revengeTanksTest = true;   // test: the tank revenge starts at GO
            else if (a == "--missile-dry") _missileDry = true;           // test: missiles only log what they would have done to the car (MISSILEDBG)
            else if (a.StartsWith("--babushka-seed=") && _babushkas != null) _babushkas.SeedForTest(ulong.Parse(a["--babushka-seed=".Length..]));  // test: the same mothers and reactions each run
            else if (a.StartsWith("--missile-seed=")) Missile.SeedBase = int.Parse(a["--missile-seed=".Length..]);  // test: another set of missile random draws
        }
        if (_babushkas != null) _babushkas.Hit += OnBabushkaHit;
        if (_jets != null)
        {
            _missileWarn = new MissileWarning();
            AddChild(_missileWarn);
            _jets.MissileLaunched += m =>
            {
                if (_state == State.Racing && AtCar(m)) _hud.Alert("MISSILE LAUNCH!", "TURN HARD AS IT CLOSES IN", 2.5f, Hud.Red);
            };
            _jets.AvengerTarget = () => _car.Pos;
            _jets.AvengerRolling += id =>
            {
                if (RevengeDbg) GD.Print($"revenge Balalaika: avenger #{id} rolls at {_clock:F1} s");
                if (_state == State.Racing) _hud.Alert("MIK TAKING OFF. IT IS PERSONAL.", "HER GRANDSON IS AT THE CONTROLS", 3f, Hud.Red);
            };
            _jets.AvengerFired += (_, side, _) =>
            {
                if (RevengeDbg) GD.Print($"revenge Balalaika: launch at {_clock:F1} s, car {_car.Speed * 3.6f:F0} km/h");
                if (_state == State.Racing) _hud.Alert("MISSILE LAUNCH!", side < 0 ? "FOR GRANDMA!" : "AND ONE MORE FOR GRANDMA!", 2.5f, Hud.Red);
            };
            _jets.AvengerGone += () => { if (RevengeDbg) GD.Print($"revenge Balalaika over at {_clock:F1} s"); };
        }
        if (_slurryAll != null)
        {
            _slurryAll.RevengeOnTheRoad += () =>
            {
                if (_state == State.Racing) _hud.Alert("THE TRACTOR IS ON THE ROAD", "SLURRY AHEAD. DO NOT BRAKE IN IT", 3f, Hud.Red, Prio.Important);
            };
            _slurryAll.RevengeOver += () =>
            {
                if (_state == State.Racing) _hud.Alert("THE TRACTOR GOES BACK TO ITS FIELDS", "THE FAMILY HAS MADE ITS POINT", 3f, Hud.Gold, Prio.Important);
            };
        }
        if (_tanks != null)
        {
            _tanks.ChaseShot += n => { if (RevengeDbg) GD.Print($"revenge tanks: shot {n} at {_clock:F1} s"); };
            _tanks.ChaseOver += () =>
            {
                if (RevengeDbg) GD.Print($"revenge tanks: chase over at {_clock:F1} s");
                if (_state == State.Racing) _hud.Alert("THE TANKS RETURN TO THE EXERCISE", "THE ARMY HAS MADE ITS POINT", 3f, Hud.Gold, Prio.Important);
            };
        }
    }

    /// <summary>The car launched babushka i. Only a race run counts (never the title replay or demo),
    /// and never a burning wreck sliding into her: that would start a revenge on the new car.</summary>
    void OnBabushkaHit(int i)
    {
        if (InSchool) { _scHit = true; return; }      // the driving school: no revenge, the lesson just notes it
        if (_state != State.Racing || _babushkas == null || FateHasCar) return;
        if (RevengeDbg) GD.Print($"revenge: babushka {i} hit at {_clock:F1} s (pilot's mother {_babushkas.PilotMother}, tank driver's mother {_babushkas.TankMother})");
        if (i == _babushkas.PilotMother) StartJetRevenge();
        else if (i == _babushkas.TankMother)
        {
            if (_slurry != null && _tanks == null) StartSlurryRevenge();      // (Kolkhoz has no tanks: the second grandmother belongs to the slurry tractor's driver)
            else StartTankRevenge();
        }
    }

    void StartJetRevenge()
    {
        if(_hinds!=null) {
            if(_hinds.CallRetaliation(_car.Pos)) Reveal("THAT WAS THE PILOT'S CLOSE FRIEND!", "THIS WILL NOT BE DISCUSSED");
            return;
        }
        if (_jets == null || !_jets.StartRevenge()) return;
        Reveal("THAT WAS THE PILOT'S GRANDMA!", "THE REGIMENT WILL REMEMBER THIS");
        CardRevenge(jet: true);
    }

    void StartTankRevenge()
    {
        if (_tanks == null || !_tanks.StartChase()) return;
        Reveal(_trackId=="bonus"?"THAT WAS THE TANK CREW'S COMRADE!":"THAT WAS THE TANK DRIVER'S GRANDMA!", "THE 1ST GUARDS TANK ARMY OPENS FIRE");
        CardRevenge(jet: false);
    }

    /// <summary>The slurry tractor driver's grandmother: the tractor leaves its fields, follows the road and spreads slurry for three laps.</summary>
    void StartSlurryRevenge()
    {
        if (_slurry == null || !_slurry.StartRevenge()) return;
        Reveal("THAT WAS THE TRACTOR DRIVER'S GRANDMA!", "HE WILL SPREAD HIS GRIEF ON THE ROAD FOR THREE LAPS");
    }

    void Reveal(string title, string sub)
    {
        if (!CardsOn) _hud.Alert(title, sub, 4f, Hud.Red);    // with the cards on, the revenge card says it all
    }

    /// <summary>At GO: the test revenges.</summary>
    void StartRevengeTests()
    {
        if (_revengeJetTest) StartJetRevenge();
        if (_revengeTanksTest) StartTankRevenge();
    }

    /// <summary>A new run or the title: BalalaikaSorties/TankBattle/Babushkas reset their own parts.</summary>
    void ResetRevenge()
    {
        _hud.ClearAlert();
        if (RevengeDbg && _babushkas != null && _state == State.Countdown)
            GD.Print($"revenge: new run, pilot's mother is babushka {_babushkas.PilotMother}, tank driver's mother {_babushkas.TankMother}");
    }

    /// <summary>A missile went off near the car: a direct hit wrecks it, a heavy hit or shrapnel
    /// costs engine health and dents the body by how close it was (EngineHealth.MissileDamage).</summary>
    void MissileHitsCar(Vector3 p, float distToTarget, float dist)
    {
        float d = Math.Min(dist, distToTarget);
        float loss = EngineHealth.MissileDamage(d);
        if (OS.HasEnvironment("MISSILEDBG"))
            GD.Print($"missile outcome: {(loss >= 1f ? "destroyed" : d < EngineHealth.MissileHeavy ? "heavy" : loss > 0 ? "shrapnel" : "missed")} at {d:F1} m, damage {loss * 100f:F0} %");
        if (_missileDry || loss <= 0) return;
        if (loss >= 1f) { DestroyCar(p, "MIK-21 MISSILE"); return; }
        _carVis.Damage?.Hit(16f - 1.6f * d, _car.Rot.Transposed() * (p - _car.Pos), (_car.Pos - p).Normalized(), _car, _fx);
        HealthChanged(_health.Damage(loss, "missile", d));
    }

    /// <summary>A missile fired at the car (the avenger's, or --missile-test), not at something else.</summary>
    bool AtCar(Missile m) => m.TargetPos.DistanceTo(_car.Pos) < 2f;

    /// <summary>Edge arrows towards missiles flying at the car, and the radar-warning beep: 2 Hz at
    /// 400 m up to 10 Hz at 30 m from the nearest one still locked on.</summary>
    void MissileWarnings()
    {
        _hud.MissileMarks.Clear();
        float nearest = float.MaxValue;
        if (_jets != null && _state == State.Racing)
            foreach (var m in _jets.Missiles)
            {
                if (!m.Alive || !AtCar(m)) continue;
                if (m.Locked) nearest = Math.Min(nearest, m.Pos.DistanceTo(_car.Pos));
                Vector3 local = _cam.GlobalTransform.AffineInverse() * m.Pos;
                if (local.Z < -0.5f) _hud.MissileMarks.Add(_cam.UnprojectPosition(m.Pos));
                else _hud.MissileMarks.Add(_hud.GetViewportRect().Size / 2 + new Vector2(local.X, -local.Y).Normalized() * 1e4f);   // behind: off the edge that way
            }
        if (_missileWarn != null)
            _missileWarn.Rate = nearest < 1e6f ? Mathf.Lerp(2f, 10f, Math.Clamp((400f - nearest) / 370f, 0f, 1f)) : 0f;
    }

    void RevengeHud()
    {
        MissileWarnings();
        string jet = null, tanks = null;
        if (_state is State.Racing or State.Paused or State.Failed)
        {
            if (_jets is { RevengeActive: true }) jet = "MIK: PERSONAL";
            if (_tanks is { Chasing: true }) tanks = $"TANKS: {_tanks.ShotsLeft} SHOTS LEFT";
        }
        _hud.RevengeJet = jet;
        _hud.RevengeTanks = tanks;
    }
}
