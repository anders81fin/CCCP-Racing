using System;
using System.Collections.Generic;
using CCCPRacing.Track;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// The GAI speed trap (Track.SpeedTrap): every pass is measured and commented on, the fastest
/// reading per track goes in the GAI's book (SpeedRecords; the rally and Stakhanov laps apart, LAN
/// races and the school never), the session's best is kept too. Speeding is fined, for a laugh:
/// the kolkhoz pays. Env SPEEDDBG prints every reading.
/// </summary>
public partial class RaceScene
{
    SpeedTrap _trap;
    SpeedRecords _speeds = new(null);
    readonly Dictionary<string, float> _sessionSpeed = new();
    static readonly string[] Fines =
    {
        "FINE: {0} ROUBLES. CHARGED TO THE KOLKHOZ",
        "FINE: {0} CHICKENS. THE KOLKHOZ PAYS",
        "THE FINE GOES TO THE FIVE-YEAR PLAN",
    };

    string SpeedMode => StakhanovRules ? "stakhanov" : _rallyFuel switch { FuelGrade.Diesel => "rally_diesel", FuelGrade.Rocket => "rally_rocket", FuelGrade.Sputnik => "rally_sputnik", _ => "rally" };      // each fuel has its own book

    void InitSpeedTrap(TrackDef def)
    {
        if (def.SpeedTrap == null) return;
        _trap = new SpeedTrap();
        _trap.Init(_track, def.SpeedTrap);
        AddChild(_trap);
        if (_kozlik == null) return;
        // The duty officer's ambush past the radar (the rally): see GaiChaseFrame.
        _kozlik.AddTrapSpot(_trap.At, _trap.SideSign);
        if (_gaiChaseTest) _kozlik.GuardAtTrap();
        _kozlik.Caught += OnAutograph;
        _kozlik.GaveUp += () =>
        {
            if (_state == State.Racing) _hud.Show("YOU OUTRAN THE BEAR", "HE SHAKES HIS PAW AFTER YOU", 2.5f, Hud.Gold, Prio.Important, plain: "OUTRAN THE BEAR");
            Log("gave up");
        };
    }

    // ------------------------------------------------------------ the duty officer's chase

    int _chaseLap = -1;          // the lap a chase started on: one a lap
    float _autographT;           // the car is held while the bear gets his autograph
    bool _gaiChaseTest;

    const float AutographTime = 6f;

    static void Log(string what)
    {
        if (OS.HasEnvironment("KOZLIKDBG")) GD.Print($"GAI chase: {what}");
    }

    /// <summary>Caught speeding with the Kozlik in ambush (the rally, once a lap): it comes after the car.</summary>
    bool StartGaiChase(float kmh)
    {
        if (_kozlik == null || StakhanovRules || !_kozlik.AtTrap || _timer.LapsDone == _chaseLap) return false;
        _chaseLap = _timer.LapsDone;
        _kozlik.StartChase();
        if (CardsOn)
            QueueCard("gai_chase", "RADAR TRAP!", Hud.Red, "RADAR TRAP!", "THE OFFICER IS AFTER YOU!",
                $"YOU PASSED THE GAI AT {kmh:0} KM/H. THE LIMIT IS {_trap.Limit:0}.",
                "THE DUTY OFFICER IS AFTER YOU!", "OUTRUN HIM, OR FACE SOVIET JUSTICE!");
        else _hud.Show("THE DUTY OFFICER IS AFTER YOU!", $"{kmh:0} KM/H. OUTRUN THE BEAR", 3f, Hud.Red, Prio.Critical, urgent: true);
        Log($"starts at {_clock:F1} s, {kmh:F0} km/h, lap {_timer.LapsDone + 1}");
        return true;
    }

    void OnAutograph()
    {
        _autographT = AutographTime;
        if (CardsOn)
            QueueCard("gai_fine", "STOPPED BY THE DUTY OFFICER!", Hud.Gold,
                "AUTOGRAPH, TOVARISH?", "SIGN AND DRIVE ON!",
                "YOUR FINE: AN AUTOGRAPH. FOR HIS WIFE.",
                "SHE IS YOUR BIGGEST FAN.", "SIGN AND DRIVE ON, TOVARISH.");
        else _hud.Show("AUTOGRAPH, TOVARISH?", "SIGN AND DRIVE ON!", 3.2f, Hud.Gold, Prio.Critical, urgent: true);
        Log($"caught at {_clock:F1} s, {_car.Speed * 3.6f:F0} km/h");
    }

    /// <summary>Per physics tick: the autograph's hold runs out; a chase ends with the car out of the race.</summary>
    void GaiChaseTick(float dt)
    {
        if (_kozlik == null) return;
        if (_autographT > 0)
        {
            float was = _autographT;
            _autographT -= dt;
            // The first line has had its time: he signs.
            if (was > AutographTime - 3.2f && _autographT <= AutographTime - 3.2f && _state == State.Racing)
            {
                string name = _rallyBoard.LastName is { Length: > 0 } n ? n : Leaderboard.DefaultName;
                _hud.Show($"SIGNED: {name}", "FOR LUDMILA, WITH SOCIALIST GREETINGS", 2.6f, Hud.Paper, Prio.Critical);
            }
            if (_autographT <= 0) { _kozlik.EndAutograph(); Log($"autograph done at {_clock:F1} s"); }
        }
        if (_kozlik.Chasing && (_state != State.Racing || FateHasCar || _rescue.Active || PlanHasCar))
        {
            _kozlik.StopChase();
            _autographT = 0;
            Log($"stopped at {_clock:F1} s: the car is out of it (state {_state}, wreck/dead {FateHasCar}, rescue {_rescue.Active}, plan {PlanHasCar})");
        }
    }

    /// <summary>The car stands still while the bear asks (as for the mechanic).</summary>
    bool AutographHolds => _autographT > 0 && _state == State.Racing;

    void ResetGaiChase()
    {
        _chaseLap = -1;
        _autographT = 0;
    }

    /// <summary>Per frame while racing: a pass through the radar, its reading, the book and the comment.</summary>
    void SpeedTrapFrame()
    {
        if (_trap != null) _trap.HoldReading = InSchool && SchoolFrozen;
        if (_trap == null || _state != State.Racing || InSchool || _countdown > 0f) return;
        if (!_trap.Check(_car.Pos, _car.Vel, out float kmh)) return;
        if (OS.HasEnvironment("SPEEDDBG")) GD.Print($"speed trap: {(float.IsNaN(kmh) ? "???" : $"{kmh:F1}")} km/h ({SpeedMode}, {_trackId}) at {_clock:F2} s");
        if (float.IsNaN(kmh))
        {
            _hud.Show("THE RADAR GAVE UP", "TOO FAST FOR SOVIET SCIENCE", 2.5f, Hud.Gold, Prio.Nice);
            return;
        }
        bool counts = !InLanRace && !_autodrive;
        string key = SpeedMode;
        bool session = !_sessionSpeed.TryGetValue(key, out float sb) || kmh > sb;
        if (session) _sessionSpeed[key] = kmh;
        bool record = counts && _speeds.Offer(key, _trackId, kmh);
        bool speeding = kmh > _trap.Limit;
        if ((speeding || _gaiChaseTest) && StartGaiChase(kmh)) return;     // (a record is on the book all the same)
        string fine = string.Format(Fines[(int)(GD.Randi() % (uint)Fines.Length)], 1 + (int)((kmh - _trap.Limit) / 10f));
        if (record)
            _hud.Show($"NEW SPEED RECORD: {kmh:0} KM/H", speeding ? fine : "THE GAI WRITES IT IN THE BOOK", 3f, Hud.Gold, plain: $"NEW SPEED RECORD: {kmh:0} KM/H", plainSub: speeding ? fine : null);
        else if (kmh < 50f)
            _hud.Show("TOO SLOW, TOVARISH.", "ARE YOU A KULAK?", 2.5f, Hud.Paper, Prio.Nice);
        else if (speeding)
            _hud.Show($"SPEEDING! {kmh:0} KM/H", fine, 2.5f, Hud.Red, Prio.Nice);
        else
            _hud.Show($"GAI: {kmh:0} KM/H", "WITHIN THE NORM. NOTED IN THE BOOK", 2.5f, Hud.Paper, Prio.Nice);
    }

    /// <summary>The GAI's record for a track in a mode, for the cards and the board ("" when none).</summary>
    string SpeedText(string mode, string track)
    {
        float b = _speeds.Best(mode, track);
        return b > 0f ? $"GAI {b:0} KM/H" : "";
    }
}
