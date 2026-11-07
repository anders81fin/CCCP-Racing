using System;
using System.Collections.Generic;
using CCCPRacing.Audio;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// The KOLKHOZ DRIVING SCHOOL in the race: the world its demos drive in. The title's DRIVING SCHOOL
/// (SINGLE PLAYER) teaches car control on ILYICH'S LAMP and comes back to the title (SchoolCourse.Driving).
/// Starting a rally from the title on a track with a school asks INSTRUCTIONS, TOVARISH? first
/// (RaceScene.TitleCards.cs; each track's school seen once is kept in [tutorial] in settings.cfg);
/// Stakhanov laps start straight away. SchoolRunner keeps the running order (lessons, demos, cards,
/// tries); this side sets the world up for each step (RaceScene.SchoolSteps.cs and the tracks' own),
/// points the camera and draws the arrow and the road's words. Esc (pad START) asks to leave: then the
/// real run starts as it would have (or the title, with --school-menu).
/// The school runs in State.Racing with the lap timer stopped and the norms off, so no lap, ghost,
/// board, name entry, plan or KGB file is ever touched; fuel, damage and the mechanic work as in the
/// rally and are reset at every step. Only the babushkas, the mechanic, the liaison officer and the
/// depot stay in the rally school's world (the bus comes for the horn lesson). Stakhanov laps have no
/// school. Tests: --school=rally (--school-menu: back to the title after it), and SchoolRunner's.
/// </summary>
public partial class RaceScene : ISchoolHost
{
    TutorialFile _tutorial = new(null);
    SchoolRunner _sc;
    Func<Vector3?> _scLook;
    // What happened in the world this step (events).
    bool _scStartleSeen;
    bool _scTossed, _scCaught, _scBurst, _scHit, _scStartled, _scSwerved, _scRescued, _scRepairing, _scRepaired;

    static bool SchoolDbg => SchoolRunner.Dbg;

    /// <summary>After the records are known: the [tutorial] file, the test flags, the world's news the lessons listen to.</summary>
    void InitSchool()
    {
        _tutorial = new TutorialFile(FileDir);
        _sc = new SchoolRunner(this, _hud);
        _scMarkNode = new SchoolMarks { Name = "SchoolMarks" };
        AddChild(_scMarkNode);
        _scMarkNode.Init(_track);
        var b = _babushkasAll;
        if (b != null)
        {
            b.Startled += i =>
            {
                _scStartled = true;
                _scStartleSeen = InView(b.WalkerPosition(i)) && WellInFrame(_car.Pos);
                if (_sc.Phase == SchoolPhase.Try) ShotSoon("dive", 0.2f);
                if (SchoolDbg) GD.Print($"school: babushka {i} startled {b.WalkerPosition(i).DistanceTo(new Vector2(_car.Pos.X, _car.Pos.Z)):F1} m from the car, in view {_scStartleSeen}");
            };
            b.CanTossed += _ => _scTossed = true;
            b.CanCaught += (_, _) => _scCaught = true;
            b.CanBurst += (_, _) => _scBurst = true;
        }
        if (_busAll != null) _busAll.Swerved += () => _scSwerved = true;
        if (_drunksAll != null)
        {
            _drunksAll.RepairStarted += () => _scRepairing = true;
            _drunksAll.Repaired += () => _scRepaired = true;
        }
    }

    /// <summary>End of _Ready: --school starts one straight away.</summary>
    void SchoolFlagStart()
    {
        if (_sc.StartFlag == null) return;
        if (_sc.StartFlag.StartsWith("driv", StringComparison.OrdinalIgnoreCase)) EnterSchool(SchoolCourse.Driving, true);   // --school=driving (with --track=ilyich)
        else EnterSchool(SchoolCourse.Rally, _sc.MenuFlag);
    }

    /// <summary>The track's school for this course has been gone through (each track its own rally school;
    /// Red Dawn keeps the old key, so its school stays seen).</summary>
    bool SchoolSeen(SchoolCourse c) => c == SchoolCourse.Driving ? _tutorial.SeenKey("school_driving")
        : _trackId == TrainingTrack ? _tutorial.Seen(c) : _tutorial.SeenKey("school_rally_" + _trackId);

    void MarkSchoolSeen(SchoolCourse c)
    {
        if (c == SchoolCourse.Driving) _tutorial.MarkKey("school_driving");
        else if (_trackId == TrainingTrack) _tutorial.MarkSeen(c);
        else _tutorial.MarkKey("school_rally_" + _trackId);
    }

    // ------------------------------------------------------------ the flow

    /// <summary>A school: all of it, or one chapter (0-based) chosen in the lessons' tree.</summary>
    void EnterSchool(SchoolCourse course, bool fromMenu, int chapter = -1)
    {
        var steps = SchoolScript.For(course, _trackId);
        if (steps == null) return;
        _card = TitleCard.None;
        _run = RunMode.School;
        MenuPlayed(course == SchoolCourse.Driving ? MenuItem.School : MenuItem.Race);
        NewRunPower();
        _carRadio.SetOn(false, false);
        _hud.SetComments(true, false);
        _watching = _showBoard = _boardScoped = _showLog = _showFiles = _showLessons = _showKeys = _replaySelected = false;
        _hud.TitleNotice = null;
        _radio.Stop();
        AudioMix.MuteSfx(false);
        _carRadio.Suspend(false);
        _timer.Stop();
        _ghostVis.Visible = false;
        _snd.Volume = 0.8f;
        _state = State.Racing;
        _hud.Mode = HudMode.Race;
        _countdown = -2f;
        if (_sc.PadFlag) _padMode = true;
        if (_babushkasAll != null) _babushkasAll.ForceDive = true;
        if (chapter >= 0) steps = Array.FindAll(steps, s => s.chapter == chapter);
        int start = _sc.Begin(course, fromMenu, _trackId, steps);
        ScFresh();
        _car.Reset(_track.StartPos, _track.StartYaw);
        _cam.Snap(_car.Pos);
        _sc.StartStep(start);
    }

    /// <summary>Number keys restart a numbered lesson, including during a demo or the leave question.</summary>
    bool SchoolChapterInput(InputEvent e)
    {
        if (!InSchool || _state != State.Racing
            || e is not InputEventKey { Pressed: true, Echo: false, AltPressed: false, CtrlPressed: false, MetaPressed: false } key)
            return false;
        int number = PowerKey(key);
        if (number == 0) return false;
        int start = _sc.ChapterStart(number);
        if (start < 0) return true;

        _sc.ClearForRestart();
        _honkLeft = 0;
        ScFresh();
        _trap?.Reset();
        _dosimetrist?.Reset();
        _car.Reset(_track.StartPos, _track.StartYaw);
        _cam.Snap(_car.Pos);
        if (SchoolDbg) GD.Print($"school: restart lesson {number} at { _sc.Steps[start].step.Id}");
        _sc.StartStep(start);
        return true;
    }

    // ------------------------------------------------------------ ISchoolHost

    List<Beat> ISchoolHost.BeatsFor(string id) => BeatsFor(id);
    Trial ISchoolHost.TrialFor(string id) => TrialFor(id);

    void ISchoolHost.RecapStart()
    {
        ScFresh();
        _car.Reset(_track.StartPos, _track.StartYaw);
        _cam.Snap(_car.Pos);
    }

    void ISchoolHost.StopCar() => ScStop();

    void ISchoolHost.BeatDoneLog()
    {
        float over = SchoolScript.OffRoad(_track, new Vector2(_car.Pos.X, _car.Pos.Z), out int idx);
        GD.Print($"school: {_sc.Current.step.Id} beat {_sc.BeatIndex + 1} done after {_sc.Sim:F1} s: \"{_sc.Caption}\" at road metre {idx} ({over:F1} m past the edge), {_car.Speed * 3.6f:F0} km/h"
            + $", engine {_health.Health * 100:F0}%, fuel {_tank.Litres:F1} l, gold {_carVis.Golden} boost {_boostLeft:F1} power {_car.PowerScale:F2}{(_scTossed ? ", can tossed" : "")}{(_scCaught ? ", caught" : "")}{(_scStartled ? ", startled" : "")}"
            + $"{(_scSwerved ? ", bus swerved" : "")}{(_scRepaired ? ", repaired" : "")}{(_scRescued ? ", rescued" : "")}{(_scHit ? ", babushka hit" : "")}{(_scLapTime > 0 ? $", lap {LapTimer.Format(_scLapTime)}" : "")}, technique {_car.LastTechnique}");
    }

    void ISchoolHost.Praise() => _kgb.Say("ХОРОШО!", true, 3f);

    /// <summary>The school is over (finished: to the end, skips and all): the real run, or the title (--school-menu).</summary>
    void ISchoolHost.SchoolEnded(SchoolCourse course, bool finished)
    {
        _run = RunMode.Title;      // the title or the rally run sets its own
        if (finished) MarkSchoolSeen(course);
        _scLook = null;
        EndEnvironmentTour();
        _scLightClock = null;
        _scMarks.Clear();
        _scMarkNode?.Clear();      // the road markings go with the school (not left on the road for the next run)
        _hud.SchoolMarks.Clear();
        _hud.School = _hud.HideLapPanel = _hud.SchoolScenic = false;
        _hud.ClearSchoolBanner();
        _hud.ClearMessages();
        if (_babushkasAll != null) _babushkasAll.ForceDive = false;
        if (SchoolDbg || _sc.Auto)
        {
            GD.Print($"school: {course} school {(finished ? "finished" : "left")} after {_sc.Total:F1} s");
            foreach (var r in _sc.Results) GD.Print("  " + r);
        }
        if (InstructionTrack != null) { InstructionFinished?.Invoke(); return; }
        if (_sc.Quit) { CCCPRacing.Audio.AudioMix.Quit(this); return; }
        if (_sc.FromMenu && LessonsBack()) return;      // from the lessons' tree: back to it
        if (_sc.FromMenu)
        {
            EnterTitle();
            OpenMenu(MenuItem.Single, course == SchoolCourse.Driving ? MenuItem.School : MenuItem.Race);
        }
        else StartRun(MenuItem.Race);
    }

    bool ISchoolHost.TrailerCapture => _trailerCapture;
    void ISchoolHost.TrailerShotDone() => TrailerShotDone();
    bool ISchoolHost.PausedForSchool => _showKeys || _state == State.Paused;
    void ISchoolHost.SchoolAudio(float real, bool paused) => EnvironmentMusic(real, paused);
    bool ISchoolHost.PadMode => _padMode;

    void ISchoolHost.SaveShot(string path)
    {
        var img = GetViewport().GetTexture()?.GetImage();
        if (img == null) return;
        img.SavePng(path);
        GetTree().Root.GetTexture().GetImage().SavePng(path.Replace(".png", "_full.png"));   // the window's frame, as --shot
        GD.Print($"school: screenshot {path}");
    }

    // ------------------------------------------------------------ the race's calls

    bool SchoolFrozen => _sc is { Frozen: true };
    bool SchoolLetsDrive => _sc?.LetsDrive ?? true;
    bool SchoolLetsHonk => _sc?.LetsHonk ?? true;
    bool SchoolDrive(float dt) => _sc.Drive(dt);
    bool SchoolInput() => _sc.Input(_state == State.Racing);
    Key SchoolPadKey(JoyButton b) => _sc.PadKey(b);
    void SlowMo(float seconds) { if (!_droneTour) _sc.SlowMo(seconds); }      // the drone film never slows the world
    void ShotSoon(string kind, float after = 0f) => _sc.ShotSoon(kind, after);

    /// <summary>Per frame: the title's cards, then the school's clocks.</summary>
    void SchoolFrame(float dt)
    {
        CardHud();
        _sc.Frame(dt);
        if (InstructionTrack != null && _hud.SchoolRecap != null)
            _hud.SchoolRecapGo = (_padMode ? "A: " : "ENTER: ") + "BACK TO PARTY MEETING";
    }

    /// <summary>
    /// Where a world point is in a 16:9 game frame (0..1 across and down), through the game camera.
    /// The camera keeps its height, so the fraction across is rescaled from whatever the window is
    /// (a headless run's viewport is 64x64) to the 480x270 frame the player sees.
    /// </summary>
    Vector2 FrameFrac(Vector3 p)
    {
        if (_cam.IsPositionBehind(p)) return new Vector2(-1, -1);
        Vector2 sp = _cam.UnprojectPosition(p), vs = GetViewport().GetVisibleRect().Size;
        return new Vector2(0.5f + (sp.X - vs.X / 2) / vs.Y * (9f / 16f), sp.Y / vs.Y);
    }

    /// <summary>At least 15 % in from every edge of the frame.</summary>
    bool WellInFrame(Vector3 p)
    {
        var f = FrameFrac(p);
        return f.X >= 0.15f && f.X <= 0.85f && f.Y >= 0.15f && f.Y <= 0.85f;
    }

    bool InView(Vector2 p) => WellInFrame(Ground(p, 1f));

    /// <summary>The camera on something else than the car (the truck coming for the driver).</summary>
    bool SchoolCamera(float dt)
    {
        if (_trailerV2) { TrailerV2Camera(dt); return true; }
        if (_droneTour) { DroneTourCamera(dt); return true; }
        if (_cinematicTour) { CinematicTourCamera(dt); return true; }
        if (CarTourActive) { CarTourCamera(dt); return true; }      // RaceScene.CarTour.cs
        if (!InSchool && !TrackPreviewActive) return false;
        if (_scScenic != null)
        {
            var shot = _scScenic();
            _scScenicTarget = shot.target;
            _scScenicOffset = shot.offset;
            _cam.Scenic(shot.target, shot.offset, dt);
            return true;
        }
        if (_scLook?.Invoke() is not Vector3 p) return false;
        _cam.FramePair(_car.Pos, p, dt);
        return true;
    }

    int _scSteerKey;
    float _scSteerKeyAt;

    /// <summary>A point beside the car on its own left (-1) or right (1) side, on the screen (null: behind the camera).</summary>
    Vector2? SideTagAt(float side)
    {
        Vector3 p = _car.Pos + _car.Right * (side * 3.6f) + Vector3.Up * 0.8f;
        return _cam.IsPositionBehind(p) ? null : _cam.UnprojectPosition(p);
    }

    /// <summary>The school's HUD: its words (SchoolRunner), and what points into the world.</summary>
    void SchoolHud()
    {
        _sc.ShowOnHud();
        if (!InSchool) return;
        var step = _sc.Current.step;
        _hud.HideLapPanel = !_scMock;
        bool show = _sc.Showing;
        _hud.SchoolArrow = null;
        if ((show || _sc.Phase == SchoolPhase.Try || _sc.Phase == SchoolPhase.Tell && _sc.PointsOut)
            && _sc.Arrow?.Invoke() is Vector3 at)
        {
            if (!_cam.IsPositionBehind(at)) _hud.SchoolArrow = _cam.UnprojectPosition(at);
            else
            {
                // Far down the screen is behind the tilted camera's plane: a point 15 m that way, pushed far off the edge.
                Vector3 flat = (at - _car.Pos) with { Y = 0 };
                Vector3 toward = _car.Pos + (flat.Length() > 0.1f ? flat.Normalized() * 15f : Vector3.Zero);
                Vector2 c = _cam.UnprojectPosition(_car.Pos), n = _cam.UnprojectPosition(toward);
                _hud.SchoolArrow = c + (n - c) * 50f;
            }
        }
        // The steering lesson: the car's own LEFT and RIGHT beside it, the held one lit (demo: the autopilot's, try: the player's).
        bool tags = step.Id == "steer" && _sc.Driven;
        _hud.SchoolLeftTag = tags ? SideTagAt(-1f) : null;
        _hud.SchoolRightTag = tags ? SideTagAt(1f) : null;
        // The key held lights up, and stays lit a moment (no flicker in a gentle bend).
        if (MathF.Abs(_car.SteerIn) > 0.1f) { _scSteerKey = MathF.Sign(_car.SteerIn); _scSteerKeyAt = _clock; }
        else if (_clock - _scSteerKeyAt > 0.4f) _scSteerKey = 0;
        _hud.SchoolSteerKey = tags ? _scSteerKey : 0;
        _hud.SchoolSteerKeys = tags && show;
        // The slide lessons' road markings: each part's words over the start of its strip, lit while the car is on it.
        _hud.SchoolMarks.Clear();
        if (_scMarks.Count > 0 && _sc.Driven)
        {
            int s = SlideAlong(_scMarkEntry);
            foreach (var m in _scMarks)
            {
                if (m.text.Length == 0) continue;
                Vector3 mp = Ground(SchoolScript.RoadPoint(_track, _scMarkEntry + m.from, 0), 1.2f);
                if (_cam.IsPositionBehind(mp)) continue;
                _hud.SchoolMarks.Add((_cam.UnprojectPosition(mp), m.text, m.col, s >= m.from && s < m.to));
            }
        }
        if (_scMock)
        {
            _hud.PlanLaps = NormPlan.WindowLaps;
            _hud.PlanLeft = NormPlan.WindowLaps;
            _hud.PlanStep = _scMockStep;
            _hud.PlanSteps = NormPlan.Steps;
            _hud.PlanTarget = NormPlan.Ladder[_scMockStep];
            _hud.PlanClimate = 0;
            _hud.PlanLastLap = false;
            _hud.LapTime = _scLap is { Running: true } && _scLapTime < 0 ? _scLap.LapTime : 0;
            _hud.LastLap = _scLapTime;
            _hud.LastLapVoid = false;
            _hud.BestLap = _scLapTime;
            _hud.Lap = _scLapTime > 0 ? 2 : 1;
        }
    }
}
