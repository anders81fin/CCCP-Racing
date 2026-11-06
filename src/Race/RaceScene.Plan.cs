using System;
using System.Globalization;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// The plan: the liaison officer's lap-time norms (NormPlan), 1:15 down to the Stakhanovite
/// 0:52.50 in steps of 2.5 s, five laps for each. Beating a norm hands out the next (or a later one: a lap can skip
/// steps); beating the last fulfils the plan: the car coasts and the PLAN FULFILLED screen follows.
/// A window used up: the car brakes to a stop past the line, the KGB supervisor shakes his head and
/// the mission has failed (the truck, then the GULAG). Wrecks (RaceScene.Fate), empty-tank tows
/// (RaceScene.Fuel) take laps from the window and can use it up too. A restart request (pause, R:
/// back to the start line, the run goes on) and Esc (back to track selection) cost nothing.
/// Training laps, LAN races and the title demo have no norms.
/// </summary>
public partial class RaceScene
{
    const float StopVerdictAfter = 2.5f;   // s from the line: the supervisor shakes his head (car stopped)
    const float StopFailAfter = 1.6f;      // s after the verdict: MissionFailed
    const float CoastFor = 4.5f;           // s of coasting after PLAN FULFILLED before the screen
    const float PullOver = 2.5f;           // m beyond the road edge the out-of-laps car stops at

    readonly NormPlan _norms = new();
    // Tests: --laps-left=N (the first norm's window has N laps left), --norm-step=N (start at norm N, 1-based) or --target=SECONDS (the first norm at or under it).
    int _lapsLeftTest = -1, _normStepTest = -1;
    // Test: --shot-after-plan=SECONDS takes the screenshot this long after the plan ends (either way).
    float _shotAfterPlan = -1;
    // Test: --shot-on-norm=SECONDS takes the screenshot this long after the first NEW NORM.
    float _shotOnNorm = -1;
    // A lap outcome waits while the name entry is open; it is applied once the name is in.
    // Out of laps: braking past the line, verdict, then the mission fails.
    bool _planStop, _planVerdict;
    float _planClock;
    // Fulfilled: coasting (State.Finished), then the screen.
    bool _planScreen;
    float _planLap;
    // One message shown a little later, so it does not wipe the lap time off the screen.
    string _deferText, _deferSub;
    Color _deferColor;
    float _deferAt = -1;
    // Test (--lap-done-at=T:SECONDS[,T:SECONDS...], may repeat): ranked laps completing at race times T.
    readonly System.Collections.Generic.List<(float at, float time)> _fakeLaps = new();

    // Restart requests.
    bool _planRestart;                    // A rally restart retains plan progress and the time of day.
    bool _raceRestart;                    // Manual restart restores a factory-fresh car, ignoring debug starting conditions.
    bool _fileAlert;                      // the run's first countdown reads the driver's file out
    (string text, string sub)? _goAlert;  // an alert that waits for GO (the countdown lights are where alerts go)

    /// <summary>The norms of the current run (read-only use).</summary>
    public NormPlan Plan => _norms;

    /// <summary>
    /// Seconds the climate adds to every norm: the day-night allowance of the lap being driven (its
    /// phase at the lap start; the line check runs before the next frame moves it on, so a night lap
    /// is judged with the night allowance). The check, the alerts and the HUD all use ladder + this.
    /// </summary>
    float ClimateAllowance() => StakhanovRules ? 0f : (_dayNight?.ClimateAllowance ?? 0f) * (_rallyFuel == FuelGrade.Diesel ? 2f / 3.5f : NormPlan.Factor);

    /// <summary>A norm for the texts.</summary>
    static string NormText(float t) => LapTimer.Format(t);

    /// <summary>
    /// Pause, R: return to the start with a repaired car, full tank and zero dose.
    /// A rally retains its current norm/window and time of day. A wreck or empty tank may restart too.
    /// </summary>
    void PlanRestart()
    {
        if (_norms.Enabled && (PlanHasCar || _norms.Finished)) return;
        if (OS.HasEnvironment("PLANDBG"))
            GD.Print($"plan: RESTART REQUEST at {_clock:F2} s, norm {_norms.Step + 1}, left {_norms.Left}, wrecks {_wrecks}, health {_health.Health * 100:F0}%, dented {_carVis.Damage?.IsDamaged ?? false}, laps done {_timer.LapsDone}");
        _planRestart = _norms.Enabled;
        _raceRestart = true;
        try { EnterCountdown(); }
        finally { _planRestart = _raceRestart = false; }
    }

    /// <summary>The run's first countdown: the liaison officer hands out the first norm (the alert after GO).</summary>
    void PlanFileAlert()
    {
        _fileAlert = false;
        _kgb.SayLiaison($"НОРМА {NormText(_norms.Norm)}", 4f);
        _goAlert = ($"NORM {_norms.Step + 1}: {NormText(_norms.Norm)}", _norms.Left == NormPlan.WindowLaps ? "FIVE LAPS, TOVARISH" : $"{_norms.Left} LAPS LEFT, TOVARISH");
    }

    void InitPlan()
    {
        foreach (string a in CmdArgs())
        {
            if (a.StartsWith("--laps-left=")) _lapsLeftTest = int.Parse(a["--laps-left=".Length..]);  // test: the first norm's window has N laps left
            else if (a.StartsWith("--shot-after-plan=")) { _shotAfterPlan = float.Parse(a["--shot-after-plan=".Length..], CultureInfo.InvariantCulture); _shotAt = 1e9f; }  // test: screenshot this long after the plan is fulfilled or the laps run out
            else if (a.StartsWith("--shot-on-norm=")) { _shotOnNorm = float.Parse(a["--shot-on-norm=".Length..], CultureInfo.InvariantCulture); _shotAt = 1e9f; }  // test: screenshot this long after the first NEW NORM
            else if (a.StartsWith("--norm-step=")) _normStepTest = Math.Clamp(int.Parse(a["--norm-step=".Length..]), 1, NormPlan.Steps) - 1;  // test: the rally starts at norm N (1-9)
            else if (a.StartsWith("--target="))      // test: the rally starts at the first norm at or under SECONDS
            {
                float target = float.Parse(a["--target=".Length..], CultureInfo.InvariantCulture);
                _normStepTest = Array.FindIndex(NormPlan.Ladder, t => t <= target + 1e-3f);
                if (_normStepTest < 0) _normStepTest = NormPlan.Steps - 1;
            }
        }
        _norms.Climate = ClimateAllowance;
    }

    /// <summary>Parses one --lap-done-at value: T:SECONDS, several separated by commas.</summary>
    void AddFakeLaps(string list)
    {
        foreach (string item in list.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var tl = item.Split(':');
            _fakeLaps.Add((float.Parse(tl[0], CultureInfo.InvariantCulture), float.Parse(tl[1], CultureInfo.InvariantCulture)));
        }
        _fakeLaps.Sort((x, y) => x.at.CompareTo(y.at));
    }

    /// <summary>Test laps: completes the next --lap-done-at lap once the race clock reaches it.</summary>
    bool FakeLapDue(out float time)
    {
        time = 0;
        if (_fakeLaps.Count == 0 || _raceClock < _fakeLaps[0].at) return false;
        time = _fakeLaps[0].time;
        _fakeLaps.RemoveAt(0);
        return true;
    }

    /// <summary>A new run (or the title): the first norm in a real race, none otherwise.</summary>
    void ResetPlan(bool enabled)
    {
        if (_planRestart) return;     // a restart request: the plan goes on
        _norms.Reset(enabled, _normStepTest >= 0 ? _normStepTest : 0, _lapsLeftTest);
        _fileAlert = enabled;
        _hud.ResetMedalCelebration();
        _runMedal = NormPlan.Medal.None;
        _goAlert = null;
        if (enabled && OS.HasEnvironment("PLANDBG")) GD.Print($"plan: new rally run from norm {_norms.Step + 1} {NormText(_norms.Norm)}, {_norms.Left} laps");
        _planStop = _planVerdict = _planScreen = false;
        _planClock = 0;
        _deferAt = -1;
    }

    /// <summary>
    /// A lap (valid or void) reached the line: the norm has its say. The outcome is applied now,
    /// or after the name entry if the lap made the board.
    /// </summary>
    void PlanLap(float time, bool ranked)
    {
        FuelLap(time);
        if (!_norms.Enabled || _labRun) return;      // (a lab lap is judged by no norm: it is unranked and must not fail the rally)
        float was = _norms.Norm;
        var outcome = _norms.OnLap(time, ranked);
        if (OS.HasEnvironment("PLANDBG")) GD.Print($"plan: lap {LapTimer.Format(time)} ranked={ranked} vs norm {NormText(was)} -> {outcome}, norm {_norms.Step + 1}/{NormPlan.Steps} {NormText(_norms.Norm)}, laps left {_norms.Left}, used {_norms.Used}");
        PlanOutcome(outcome, time, was, ranked);
    }

    /// <summary>norm: the norm the lap was judged against; ranked: neither void nor unranked.</summary>
    void PlanOutcome(NormPlan.Outcome outcome, float time, float norm, bool ranked)
    {
        // Medal laps celebrate while driving: do not put a norm card in front of the award.
        bool medalWon = outcome == NormPlan.Outcome.NewNorm && NormPlan.MedalFor(_norms.Step) > _runMedal;
        if (outcome is NormPlan.Outcome.Continue or NormPlan.Outcome.OutOfLaps)
        {
            _hud.MissNorm(time, norm, ranked, _norms.Left, CardsOn && !CardSeen("norm_missed"));
            FirstNormMissCard(time, norm, ranked);
        }
        if (_shotAfterPlan >= 0 && outcome is NormPlan.Outcome.Success or NormPlan.Outcome.OutOfLaps) _shotAt = _clock + _shotAfterPlan;
        if (outcome != NormPlan.Outcome.Continue) _goAlert = null;     // the start's norm alert is stale now
        switch (outcome)
        {
            case NormPlan.Outcome.Continue:
                _kgb.SayLiaison($"ОСТАЛОСЬ {_norms.Left}");
                break;
            case NormPlan.Outcome.NewNorm:
                CheckMedal(_norms.Step);      // RaceScene.Tracks.cs: the norms beaten so far
                if (!medalWon) _hud.ReactToNorm(true);
                _kgb.SayLiaison($"НОРМА {NormText(_norms.Norm)}", 4f);
                _kgb.Say("ХОРОШО.", true);
                _deferAt = -1;
                if (_shotOnNorm >= 0) { _shotAt = _clock + _shotOnNorm; _shotOnNorm = -1; }
                break;
            case NormPlan.Outcome.Success:
                CheckMedal(NormPlan.Steps);
                _hud.Show("PLAN FULFILLED!", $"{LapTimer.Format(time)} - THE STAKHANOVITE NORM WAS {NormText(_norms.Norm)}", CoastFor, Hud.Gold, Prio.Critical);
                _kgb.Say("ПЛАН ВЫПОЛНЕН!", true, 4f);
                _kgb.SayLiaison("СТАХАНОВЕЦ!", 4f);
                _deferAt = -1;
                _planLap = time;
                _planClock = 0;
                _timer.Stop();
                _state = State.Finished;
                break;
            case NormPlan.Outcome.OutOfLaps:
                PlanOutOfLaps(alert: false);
                break;
        }
    }

    /// <summary>
    /// The laps are used up (at the line, or by a wreck: then as an alert that ignores the comments
    /// setting): the clock stops and the plan brings the car to a stop.
    /// </summary>
    void PlanOutOfLaps(bool alert)
    {
        if (alert && _shotAfterPlan >= 0) _shotAt = _clock + _shotAfterPlan;
        string sub = $"NORM {_norms.Step + 1}: {NormText(_norms.Norm)} - THE PLAN IS NOT FULFILLED";
        if (alert) _hud.Alert("THE NORM WAS NOT MET", sub, 4.5f, Hud.Red);
        else if (!_hud.NormMissActive) _hud.Show("THE NORM WAS NOT MET", sub, 4.5f, Hud.Red, Prio.Critical);
        _kgb.SayLiaison("ПОЗОР!", 3f);
        _kgb.Say("НУ-НУ...", null, 2.5f);
        _deferAt = -1;
        _planStop = true;
        _planClock = 0;
        _timer.Stop();
    }

    void Defer(string text, string sub, Color color, float after = 3f)
    {
        _deferText = text;
        _deferSub = sub;
        _deferColor = color;
        _deferAt = _clock + after;
    }

    /// <summary>
    /// Per physics tick while the plan has the car (out of laps, or fulfilled and coasting): the
    /// autopilot steers, the brakes stop it. True when the player's controls are ignored.
    /// </summary>
    bool PlanDrive(float dt)
    {
        if (!_planStop && _state != State.Finished) return false;
        _planClock += dt;
        _car.Clutch = false;
        _ap.Drive(_car, dt);          // keeps it pointed down the road
        _car.ThrottleTarget = 0;
        _car.Handbrake = 0;
        if (_state == State.Finished)
        {
            _car.Brake = _car.Speed > 6f ? 0.12f : 0.4f;   // coast, then roll to a halt
            if (_planClock >= CoastFor && !_planScreen) EnterPlanScreen();
            return true;
        }
        // Pull over to the right verge (off the runway centre line, where the Balalaikas take off) while
        // braking; gently enough that the front wheels still steer, then hard to a halt.
        var pos = new Vector2(_car.Pos.X, _car.Pos.Z);
        int i = (_track.NearestCenter(pos, out _) + 10) % _track.Center.Length;
        Vector2 dir = _track.CenterDir(i), aim = _track.Center[i] + new Vector2(-dir.Y, dir.X) * (_track.HalfWidth[i] + PullOver);
        var fwd = new Vector2(_car.Forward.X, _car.Forward.Z);
        _car.SteerIn = _car.Speed < 0.5f ? 0f : Math.Clamp(fwd.AngleTo(aim - pos) * 2f, -1f, 1f);
        _car.Brake = _car.Speed > 4f ? 0.45f : 1f;
        // Stopped (or long enough): the supervisor shakes his head, then the truck is called.
        if (!_planVerdict && (_planClock >= StopVerdictAfter && _car.Speed < 1f || _planClock >= StopVerdictAfter + 3f))
        {
            _planVerdict = true;
            _planClock = 0;
            _kgb.Say("ПЛАН НЕ ВЫПОЛНЕН", false, 4f);
        }
        else if (_planVerdict && _planClock >= StopFailAfter)
        {
            _planStop = false;
            MissionFailed(ReasonPlan);
        }
        return true;
    }

    /// <summary>Out of laps, once the car has slowed: the camera swings back to take in the KGB officers too.</summary>
    bool PlanCamera(float dt)
    {
        if (!_planStop || _car.Speed > 5f) return false;
        Vector2 boss = _kgb.SupervisorPosition;
        if (boss.DistanceTo(new Vector2(_car.Pos.X, _car.Pos.Z)) > 120f) return false;   // a wreck far out: stay on it
        var at = _car.Pos.Lerp(new Vector3(boss.X, _track.MeshHeight(boss.X, boss.Y), boss.Y), 0.45f);
        _cam.Follow(at, Vector3.Zero, dt);
        return true;
    }

    /// <summary>PLAN FULFILLED screen: the next kolkhoz is under construction.</summary>
    void EnterPlanScreen()
    {
        _planScreen = true;
        _overlayFrame = Engine.GetProcessFrames();
        _hud.ClearMessages();
        _hud.Mode = HudMode.Plan;
        _hud.PlanUsed = _norms.Used;
        _hud.PlanTarget = _norms.Norm;
        _hud.PlanLapTime = _planLap;
        _hud.PlanTrack = TrackName(_trackId);
        _hud.PlanMedal = Campaign.Name(NormPlan.Medal.Gold);     // the track is named over the title
        _hud.PlanNext = _bonusOpened ? "BONUS TRACK OPEN"
            : NextTrack(_trackId) is { } nx ? $"NEXT: {TrackName(nx)} - NOW OPEN" : "NEXT: KOLKHOZ NO.8";
        _hud.EndWrecks = _wrecks;
        _snd.Volume = 0f;
        _carRadio.Suspend(true);
        if (OS.HasEnvironment("PLANDBG")) GD.Print($"PLAN FULFILLED screen at {_clock:F2} s: {LapTimer.Format(_planLap)}, {_norms.Used} laps from norm {_norms.StartStep + 1}");
    }

    /// <summary>PLAN FULFILLED screen: any key, mouse or joypad button returns to the title.</summary>
    bool PlanInput(InputEvent e)
    {
        if (!_planScreen) return false;
        if (Engine.GetProcessFrames() <= _overlayFrame) return true;
        bool press = e is InputEventKey { Pressed: true, Echo: false } or InputEventMouseButton { Pressed: true }
            or InputEventJoypadButton { Pressed: true };
        if (!press) return true;
        _planScreen = false;
        _overlayExitFrame = Engine.GetProcessFrames();
        if (_lastPlayed == MenuItem.Race) ReturnRallyToTrackSelect();
        else EnterTitle();
        return true;
    }

    /// <summary>Esc while coasting skips the coasting period and opens the plan screen.</summary>
    void PlanSkip()
    {
        if (_state == State.Finished && !_planScreen && Input.IsActionJustPressed("pause")) EnterPlanScreen();
    }

    /// <summary>R does nothing while the plan brings the car to a stop.</summary>
    bool PlanHasCar => _planStop;

    void PlanHud()
    {
        bool on = _norms.Enabled && _state is State.Countdown or State.Racing or State.Paused or State.Finished or State.Failed;
        _hud.PauseHasNorms = _norms.Enabled;
        _hud.PlanLaps = on ? NormPlan.WindowLaps : 0;
        _hud.PlanLeft = _norms.Left;
        _hud.PlanStep = _norms.Step;
        _hud.PlanSteps = NormPlan.Steps;
        _hud.PlanTarget = _norms.Norm;
        _hud.PlanClimate = on ? _norms.Allowance : 0f;
        _hud.PlanFuel = on && _run == RunMode.Rally && _rallyFuel != FuelGrade.Petrol ? FuelGrades.Names[(int)_rallyFuel] : "";
        _hud.PlanLastLap = _norms.OnLastLap;
        NormCardHud();
        if (_fileAlert && _state == State.Countdown && !_showKeys) PlanFileAlert();
        if (_goAlert is var (goText, goSub) && _state == State.Racing && _countdown <= -1f)
        {
            _goAlert = null;
            _hud.Alert(goText, goSub, 4.5f, Hud.Red);
        }
        if (_deferAt > 0 && _clock >= _deferAt && _state == State.Racing)
        {
            _deferAt = -1;
            _hud.Show(_deferText, _deferSub, 3.5f, _deferColor, Prio.Critical);
        }
    }
}
