using System;
using System.Collections.Generic;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

enum SchoolPhase { Show, Tell, Try, Praise, Again, Replay, Recap }

/// <summary>A piece of a demo: set up, then driven each physics tick until Drive says it is done.</summary>
sealed class Beat
{
    public string Caption;
    public Action Setup;
    public Func<float, float, bool> Drive;      // (beat time, dt) -> done
    public Func<Vector3?> Arrow;
    public SchoolBox Box;
    public float Speed = 1f, MaxTime = 20f;
}

/// <summary>A try: set up, then judged each physics tick (the player drives, or Auto with --school-auto).</summary>
sealed class Trial
{
    public Action Setup;
    public Func<float, float, Verdict> Judge;   // (try time, dt)
    public Action<float, float> Auto;
    public Func<Vector3?> Arrow;
    public SchoolBox Box;
    public float TimeOut = SchoolScript.TryTimeout;
}

/// <summary>What the school needs of the race: the lessons' demos in the world, the car stopped, the way out.</summary>
interface ISchoolHost
{
    /// <summary>A step's demo, and its try.</summary>
    List<Beat> BeatsFor(string id);
    Trial TrialFor(string id);
    /// <summary>The recap: a fresh car at the start line.</summary>
    void RecapStart();
    /// <summary>Brakes to a stop and holds it there.</summary>
    void StopCar();
    /// <summary>SCHOOLDBG: where the car was as a beat ended.</summary>
    void BeatDoneLog();
    /// <summary>The instructor's praise after a try.</summary>
    void Praise();
    /// <summary>The school is over: its world cleaned up, then the run, the title or quit.</summary>
    void SchoolEnded(SchoolCourse course, bool finished);
    /// <summary>--trailer-shots: demos only, at normal speed, one after another.</summary>
    bool TrailerCapture { get; }
    void TrailerShotDone();
    /// <summary>The keys card or the pause menu is up (the school's clocks wait).</summary>
    bool PausedForSchool { get; }
    /// <summary>Per frame: the scenic tour's music.</summary>
    void SchoolAudio(float real, bool paused);
    bool PadMode { get; }
    /// <summary>--school-shots: the frame saved to this path.</summary>
    void SaveShot(string path);
}

/// <summary>
/// The school's running order, apart from the world its demos drive in (RaceScene.School.cs is its host).
/// Each step of a lesson (SchoolScript) is SHOW (a scripted demo with slow motion at the key moment,
/// an arrow and a pulsing box), TELL (a frozen card with the instructor and the controls drawn) and TRY
/// (the player drives the same situation; three misses show the demo once more and move on). Enter
/// (pad A, X in a try) advances cards or skips tries; scripted driving always runs to its endpoint so
/// the tour and its cards stay in sync. Esc (pad START) asks to leave.
/// The track previews play their beats with the same clocks (RaceScene.TrackPreview.cs).
/// Tests: --school-chapter=N, --school-step=N, --school-auto (the tries done by script, results
/// printed), --school-quit, --school-pad (pad prompts), --school-shots=DIR; env SCHOOLDBG.
/// </summary>
sealed class SchoolRunner
{
    // Slow enough for a child to read the card out loud (Enter goes on at once); --school-auto keeps the old quick pace.
    public const float LessonTime = 4.5f, TellTime = 16f, PraiseTime = 3.5f, AgainTime = 3f, RecapTime = 45f, SlowScale = 0.3f;
    const float AutoTell = 5f, AutoPraise = 2.4f, AutoAgain = 1.8f;
    public const float ShowPause = 2f;
    const int ReadySteps = 3;
    const float ReadyStep = 0.8f;

    readonly ISchoolHost _host;
    readonly Hud _hud;

    public SchoolCourse? Course;
    public bool Active => Course != null;
    public bool FromMenu, Ask;
    string _trackId;
    public (int chapter, SchoolStep step)[] Steps;
    public int Step, Fails, Tries;
    public SchoolPhase Phase;
    public float Real, Sim, Slow, Speed = 1f, Total, StepFrom, ShowDelay;
    public List<Beat> Beats = new();
    public int BeatIndex;
    public Trial Trial;
    public string Caption, Why;
    public Func<Vector3?> Arrow;
    public SchoolBox Box;
    public bool Horn;           // a demo honks this tick
    // A beat's (or try's) own clocks and its one-off; set back at each beat.
    public float T0 = -1, T1 = -1;
    public bool Once;
    float _ready;
    int _readyNum;
    // Tests.
    public bool Auto, Quit, MenuFlag, PadFlag;
    public string StartFlag;
    string _shots, _shotKind = "show";
    bool _shot;
    float _shotAt = -1;
    int _startChapter = 1, _startStep = 1, _shotNo;
    public readonly List<string> Results = new();
    int _tickRate = 120, _maxSteps = 8;

    public static bool Dbg => OS.HasEnvironment("SCHOOLDBG");

    public (int chapter, SchoolStep step) Current => Steps[Step];

    public SchoolRunner(ISchoolHost host, Hud hud)
    {
        _host = host;
        _hud = hud;
        _tickRate = Engine.PhysicsTicksPerSecond;
        _maxSteps = Engine.MaxPhysicsStepsPerFrame;
        foreach (string a in OS.GetCmdlineUserArgs())
        {
            if (a.StartsWith("--school=")) StartFlag = a["--school=".Length..];                  // test: start the track's rally school at once (driving: the DRIVING SCHOOL)
            else if (a == "--school-menu") MenuFlag = true;                                        // test: ...and back to the title after it
            else if (a.StartsWith("--school-chapter=")) _startChapter = int.Parse(a["--school-chapter=".Length..]);   // test: from lesson N (1-based)
            else if (a.StartsWith("--school-step=")) _startStep = int.Parse(a["--school-step=".Length..]);            // test: ...and its step N (1-based)
            else if (a == "--school-auto") Auto = true;                                            // test: the tries are driven by script; each step's result printed
            else if (a == "--school-quit") Quit = true;                                            // test: quit when the school ends
            else if (a == "--school-pad") PadFlag = true;                                          // test: pad prompts and pictures
            else if (a.StartsWith("--school-shots=")) _shots = a["--school-shots=".Length..];     // test: a screenshot at every lesson card, TELL, TRY start and slow motion
        }
    }

    // ------------------------------------------------------------ the flow

    /// <summary>A school starts: its steps, and the one it starts at (the test flags only for the first school).</summary>
    public int Begin(SchoolCourse course, bool fromMenu, string trackId, (int chapter, SchoolStep step)[] steps)
    {
        Course = course;
        FromMenu = fromMenu;
        _trackId = trackId;
        Steps = steps;
        Ask = false;
        Results.Clear();
        Total = 0;
        int start = Array.FindIndex(Steps, s => s.chapter == _startChapter - 1);
        start = Math.Clamp(Math.Max(0, start) + _startStep - 1, 0, Steps.Length - 1);
        _startChapter = _startStep = 1;
        if (Dbg || Auto) GD.Print($"school: {course} school starts{(fromMenu ? " (from the menu)" : "")} at step {start + 1} ({Steps[start].step.Id})");
        return start;
    }

    void SetPhase(SchoolPhase p)
    {
        Phase = p;
        Real = 0;
        ShowDelay = !_host.TrailerCapture && p is (SchoolPhase.Show or SchoolPhase.Replay) ? ShowPause : 0f;
        _shot = false;
        if (Dbg) GD.Print($"school: {Current.step.Id} {p} at {Total:F1} s");
    }

    public void StartStep(int i)
    {
        Step = i;
        Fails = Tries = 0;
        StepFrom = Total;
        var (ch, step) = Steps[i];
        if (step.Id == "recap")
        {
            _host.RecapStart();
            SetPhase(SchoolPhase.Recap);
            return;
        }
        BeginShow(false);      // no title band: the top tag already names the lesson
    }

    public void BeginShow(bool replay)
    {
        Beats = _host.BeatsFor(Current.step.Id);
        BeatIndex = -1;
        SetPhase(replay ? SchoolPhase.Replay : SchoolPhase.Show);
        NextBeat();
    }

    /// <summary>The lesson numbered so (1-based) starts again: its first step, or -1 if there is none.</summary>
    public int ChapterStart(int number) => Array.FindIndex(Steps, s => s.chapter == number - 1);

    /// <summary>Before a lesson restarts: nothing of the old step left running.</summary>
    public void ClearForRestart()
    {
        Ask = false;
        Horn = false;
        Slow = 0;
        Speed = 1f;
        Arrow = null;
        _shotAt = -1;
        Trial = null;
        _hud.ClearSchoolBanner();
        TimeScale(1f);
    }

    /// <summary>A beat's own clocks and one-off set back (the track previews play beats without the phases).</summary>
    public void ClearBeatClocks()
    {
        T0 = T1 = _shotAt = -1;
        Once = false;
    }

    public void NextBeat()
    {
        if (Dbg && BeatIndex >= 0) _host.BeatDoneLog();
        if (++BeatIndex >= Beats.Count) { ShowDone(); return; }
        var b = Beats[BeatIndex];
        Sim = 0;
        Speed = b.Speed;
        Slow = 0;
        Caption = b.Caption;
        Arrow = b.Arrow;
        Box = b.Box;
        T0 = T1 = _shotAt = -1;
        Once = false;
        b.Setup?.Invoke();
    }

    void ShowDone()
    {
        Slow = 0;
        Speed = 1f;
        TimeScale(1f);
        if (_host.TrailerCapture) { _host.TrailerShotDone(); return; }
        if (Phase == SchoolPhase.Replay)
        {
            Results.Add($"{Current.step.Id}: {SchoolScript.MaxTries} misses, demo shown again ({Total - StepFrom:F1} s)");
            NextStep();
        }
        else SetPhase(SchoolPhase.Tell);
    }

    void TellDone()
    {
        if (Current.step.Try && SchoolScript.HasTries(Course.Value)) BeginTry();
        else
        {
            Results.Add($"{Current.step.Id}: shown ({Total - StepFrom:F1} s)");
            NextStep();
        }
    }

    void BeginTry()
    {
        Tries++;
        Trial = _host.TrialFor(Current.step.Id);
        Arrow = Trial.Arrow;
        Box = Trial.Box;
        Caption = null;
        T0 = T1 = _shotAt = -1;
        Once = false;
        Trial.Setup();
        Sim = 0;
        Why = null;
        SetPhase(SchoolPhase.Try);
        // A moment to take the controls: the game holds still, 3, 2, 1, GO! (the car keeps its speed).
        _ready = Auto ? 0.2f : ReadySteps * ReadyStep;
        _readyNum = -1;
        ReadyTick(0f);
    }

    /// <summary>The countdown before a try: YOUR TURN! with 3, 2, 1, then GO! and the car is the driver's.</summary>
    void ReadyTick(float real)
    {
        if (_ready <= 0) return;
        _ready -= real;
        string tryOf = Tries > 1 ? $"TRY {Tries} OF {SchoolScript.MaxTries} - " : "";
        if (_ready <= 0) { _hud.SchoolBanner("GO!", null, 0.8f, Hud.Green); return; }
        int num = (int)MathF.Ceiling(_ready / ReadyStep);
        if (num == _readyNum) return;
        _readyNum = num;
        _hud.SchoolBanner("YOUR TURN!", $"{tryOf}GET READY... {num}", ReadyStep + 0.05f, Hud.Gold);
    }

    void TryOver(Verdict v)
    {
        string id = Current.step.Id;
        if (v == Verdict.Success)
        {
            if (Dbg || Auto) GD.Print($"school: {id} SUCCESS on try {Tries} after {Sim:F1} s");
            Results.Add($"{id}: success on try {Tries} ({Sim:F1} s in the try, {Total - StepFrom:F1} s the step)");
            SetPhase(SchoolPhase.Praise);
            _hud.SchoolBanner("WELL DONE, TOVARISH!", null, Auto ? AutoPraise : PraiseTime, Hud.Gold);
            _host.Praise();
            return;
        }
        Fails++;
        if (Dbg || Auto) GD.Print($"school: {id} FAIL on try {Tries} after {Sim:F1} s: {Why}");
        if (Fails >= SchoolScript.MaxTries)
        {
            _hud.SchoolBanner("WATCH ONCE MORE", Why, 2.5f, Hud.Paper);
            BeginShow(true);
        }
        else
        {
            SetPhase(SchoolPhase.Again);
            _hud.SchoolBanner("AGAIN!", Why, Auto ? AutoAgain : AgainTime, Hud.Red);
        }
    }

    void NextStep()
    {
        if (Step + 1 < Steps.Length) StartStep(Step + 1);
        else End(true);
    }

    /// <summary>Enter advances cards or skips tries. Scripted driving must reach its endpoint.</summary>
    void Skip()
    {
        switch (Phase)
        {
            case SchoolPhase.Tell: if (Real > 0.3f) TellDone(); break;
            case SchoolPhase.Try or SchoolPhase.Again:
                Results.Add($"{Current.step.Id}: skipped on try {Tries}");
                NextStep();
                break;
            case SchoolPhase.Praise:
                TimeScale(1f);
                NextStep();
                break;
            case SchoolPhase.Recap: End(true); break;
        }
    }

    /// <summary>The school is over (finished: to the end, skips and all); the host takes it from here.</summary>
    public void End(bool finished)
    {
        var course = Course ?? SchoolCourse.Rally;
        TimeScale(1f);
        Course = null;
        Ask = false;
        Horn = false;
        _host.SchoolEnded(course, finished);
    }

    // ------------------------------------------------------------ the track previews

    public enum PreviewMove { Beat, Step, End }

    /// <summary>A track preview starts: the steps' beats one after another (no phases, cards or time scale).</summary>
    public void BeginPreview((int chapter, SchoolStep step)[] steps)
    {
        Total = Sim = 0;
        Step = 0;
        BeatIndex = -1;
        Steps = steps;
        Caption = null;
    }

    /// <summary>The preview's first beat, after its title.</summary>
    public void PreviewFirst()
    {
        Beats = _host.BeatsFor(Current.step.Id);
        PreviewNext();
    }

    /// <summary>The preview's beat drives a tick (Sim already on): true when it is over.</summary>
    public bool PreviewBeatOver(float dt)
    {
        var b = Beats[BeatIndex];
        return b.Drive?.Invoke(Sim, dt) == true || Sim >= b.MaxTime;
    }

    /// <summary>The preview's next beat, on into the next step (Step), or past the last one (End: nothing set up).</summary>
    public PreviewMove PreviewNext()
    {
        var move = PreviewMove.Beat;
        if (++BeatIndex >= Beats.Count)
        {
            if (++Step >= Steps.Length) return PreviewMove.End;
            move = PreviewMove.Step;
            Beats = _host.BeatsFor(Current.step.Id);
            BeatIndex = 0;
        }
        var b = Beats[BeatIndex];
        Sim = 0;
        Caption = b.Caption;
        Arrow = b.Arrow;
        ClearBeatClocks();
        b.Setup?.Invoke();
        return move;
    }

    /// <summary>The player's horn counts: outside the school, or in a try the player drives.</summary>
    public bool LetsDrive => !Active || Phase == SchoolPhase.Try && !Auto;

    /// <summary>The player's horn sounds: as LetsDrive, and on through the praise after a try (the honk that won it).</summary>
    public bool LetsHonk => LetsDrive || Phase == SchoolPhase.Praise && !Auto;

    /// <summary>The game stands still: cards, the leave question and the pause before an example.</summary>
    public bool Frozen => Active && (Ask || ShowDelay > 0 || Phase == SchoolPhase.Tell || Phase == SchoolPhase.Recap
                                     || Phase == SchoolPhase.Try && _ready > 0);     // the countdown before a try

    /// <summary>Slow motion for this long (real seconds) from now.</summary>
    public void SlowMo(float seconds)
    {
        if (_host.TrailerCapture) return;
        Slow = seconds;
        ShotSoon("show", 0.3f);
    }

    /// <summary>--school-shots: a screenshot of the demo shortly.</summary>
    public void ShotSoon(string kind, float after = 0f)
    {
        if (_shots == null) return;
        _shotAt = Total + after;
        _shotKind = kind;
    }

    /// <summary>
    /// Time scale for demos: slow motion or fast forward. The physics tick rate goes with it, so a
    /// tick is still 1/120 s of game time and the car behaves exactly as at full speed (smaller
    /// steps change how the tyres lock); the audio's pitch is left alone.
    /// </summary>
    public void TimeScale(float s)
    {
        if (Math.Abs(Engine.TimeScale - s) < 1e-4f) return;
        Engine.TimeScale = s;
        Engine.PhysicsTicksPerSecond = Math.Max(1, (int)MathF.Round(_tickRate * s));
        Engine.MaxPhysicsStepsPerFrame = s > 1f ? Math.Max(_maxSteps, 16) : _maxSteps;
    }

    // ------------------------------------------------------------ per tick and per frame

    /// <summary>
    /// Physics tick in the school: the demo drives, the try is judged (and, with --school-auto,
    /// driven). True when the player's controls are not wanted this tick.
    /// </summary>
    public bool Drive(float dt)
    {
        if (!Active) return false;
        Horn = false;
        switch (Phase)
        {
            case SchoolPhase.Show or SchoolPhase.Replay:
            {
                Sim += dt;
                var b = Beats[BeatIndex];
                if (b.Drive(Sim, dt) || Sim > b.MaxTime) NextBeat();
                return true;
            }
            case SchoolPhase.Try:
            {
                Sim += dt;
                var v = Trial.Judge(Sim, dt);
                if (v == Verdict.Pending && Sim > Trial.TimeOut) { v = Verdict.Fail; Why = "TIME IS UP"; }
                if (v != Verdict.Pending) { TryOver(v); _host.StopCar(); return true; }
                if (!Auto) return false;
                Trial.Auto(Sim, dt);
                return true;
            }
            default:
                _host.StopCar();
                return true;
        }
    }

    /// <summary>Per frame: the phases' clocks (real time), the time scale, screenshots.</summary>
    public void Frame(float dt)
    {
        if (!Active) return;
        float real = Engine.TimeScale > 1e-4 ? dt / (float)Engine.TimeScale : dt;
        bool paused = _host.PausedForSchool || Ask;
        if (!paused)
        {
            Real += real;
            Total += real;
            Slow = Math.Max(0, Slow - real);
            if (ShowDelay > 0)
            {
                ShowDelay = Math.Max(0, ShowDelay - real);
                if (ShowDelay == 0 && Dbg)
                    GD.Print($"school: {Current.step.Id} driving starts at {Total:F1} s after {ShowPause:F1} s pause");
            }
        }
        TimeScale(_host.TrailerCapture || paused || ShowDelay > 0 || Phase is not (SchoolPhase.Show or SchoolPhase.Replay) ? 1f : Slow > 0 ? SlowScale : Speed);
        _host.SchoolAudio(real, paused);
        if (paused) return;
        if (Phase == SchoolPhase.Try) ReadyTick(real);
        switch (Phase)
        {
            case SchoolPhase.Tell when Real >= (Auto ? AutoTell : TellTime): TellDone(); break;
            case SchoolPhase.Praise when Real >= (Auto ? AutoPraise : PraiseTime): NextStep(); break;
            case SchoolPhase.Again when Real >= (Auto ? AutoAgain : AgainTime): BeginTry(); break;
            case SchoolPhase.Recap when Real >= (Auto ? 3f : RecapTime): End(true); break;
        }
        if (Active) Shots();
    }

    void Shots()
    {
        if (_shots == null) return;
        string kind = Phase switch
        {
            SchoolPhase.Tell when !_shot && Real >= 0.9f => "tell",
            SchoolPhase.Try when !_shot && Real >= 1.0f => "try",
            SchoolPhase.Recap when !_shot && Real >= 0.9f => "recap",
            SchoolPhase.Show or SchoolPhase.Replay or SchoolPhase.Try or SchoolPhase.Praise or SchoolPhase.Again when _shotAt >= 0 && Total >= _shotAt => _shotKind,
            _ => null,
        };
        if (kind == null) return;
        if (_shotAt >= 0 && Total >= _shotAt && kind == _shotKind) _shotAt = -1;
        else _shot = true;
        DirAccess.MakeDirRecursiveAbsolute(_shots);
        _host.SaveShot($"{_shots}/{++_shotNo:00}_{Course.ToString().ToLowerInvariant()}_{Current.step.Id}_{kind}{(_host.PadMode ? "_pad" : "")}.png");
    }

    /// <summary>Enter advances cards/tries, never scripted driving. Esc asks to leave (Enter then leaves);
    /// R only counts in a try. False: the race's keys (R, K) go on.</summary>
    public bool Input(bool racing)
    {
        if (!Active || !racing) return false;
        bool enter = Godot.Input.IsActionJustPressed("start") && !Godot.Input.IsKeyPressed(Key.Alt), esc = Godot.Input.IsActionJustPressed("pause");
        if (Ask)
        {
            if (enter) End(false);
            else if (esc) Ask = false;
            return true;
        }
        if (esc) { Ask = true; return true; }
        if (enter) { Skip(); return true; }
        return Godot.Input.IsActionJustPressed("rescue") && (Phase != SchoolPhase.Try || Auto);
    }

    /// <summary>The pad in the school: START asks to leave; A goes on (in a try A is the handbrake: X skips, B is R).</summary>
    public Key PadKey(JoyButton b)
    {
        bool trying = Phase == SchoolPhase.Try && !Auto;
        if (Ask) return b switch { JoyButton.A => Key.Enter, JoyButton.B or JoyButton.Start => Key.Escape, _ => Key.None };
        return b switch
        {
            JoyButton.Start => Key.Escape,
            JoyButton.X => Key.Enter,
            JoyButton.A when !trying => Key.Enter,
            JoyButton.B => trying ? Key.R : Key.Escape,
            JoyButton.Back => Key.Backspace,
            _ => Key.None,
        };
    }

    /// <summary>The school's words on the HUD: the lesson, the demo's caption, the card, the hint, the recap
    /// (what points into the world, the arrow and the road's words, the host draws).</summary>
    public void ShowOnHud()
    {
        _hud.School = Active;
        if (!Active) return;
        var (ch, step) = Current;
        var chapters = SchoolScript.Chapters(Course.Value, _trackId);
        // One lesson on its own (chosen in the lessons' tree): nothing to jump to.
        _hud.SchoolChapterCount = Steps[0].chapter == Steps[^1].chapter ? 0 : chapters.Length;
        bool show = Phase is SchoolPhase.Show or SchoolPhase.Replay;
        _hud.SchoolScenic = false;
        string doing = Phase switch
        {
            SchoolPhase.Show => "WATCH!", SchoolPhase.Replay => "WATCH ONCE MORE", SchoolPhase.Try or SchoolPhase.Again or SchoolPhase.Praise => "YOUR TURN",
            _ => null,
        };
        // One lesson on its own: "LESSON: NAME" without the numbering; the numbering only when a whole category plays through.
        string lesson = _hud.SchoolChapterCount == 0 ? $"LESSON: {chapters[ch]}" : $"LESSON {ch + 1}/{chapters.Length}: {chapters[ch]}";
        _hud.SchoolTag = lesson + (doing != null ? "  -  " + doing : "");
        _hud.SchoolCaption = show ? Caption : null;
        _hud.SchoolSubtitle = show && ShowDelay > 0 && Course == SchoolCourse.Driving
            ? step.Id switch
            {
                "kick" => "CLUTCH KICK",
                "flick" => "SCANDINAVIAN FLICK / PENDULUM TURN",
                "hairpin" => "HANDBRAKE TURN / BOOTLEGGER'S TURN",
                _ => null,
            } : null;
        _hud.SchoolTell = Phase == SchoolPhase.Tell ? (_host.PadMode && step.PadTell != null ? step.PadTell : step.Tell) : null;
        _hud.SchoolTellAtTop = Phase == SchoolPhase.Tell && PointsOut;
        _hud.SchoolTellKeys = step.Keys;
        _hud.SchoolHint = Phase == SchoolPhase.Try ? (_host.PadMode && step.PadHint != null ? step.PadHint : step.Hint) : null;
        _hud.SchoolHintKeys = step.Keys;
        _hud.SchoolTry = Tries;
        _hud.SchoolBubble = Phase == SchoolPhase.Praise ? "ХОРОШО!" : null;
        _hud.SchoolHighlight = Phase == SchoolPhase.Recap ? SchoolBox.None : Box;
        _hud.SchoolAsk = Ask;
        _hud.SchoolRecap = Phase == SchoolPhase.Recap ? SchoolScript.Recap(Course.Value) : null;
        _hud.SchoolRecapGo = _host.PadMode ? "A: " : "ENTER: ";
        _hud.SchoolRecapGo += FromMenu ? "BACK TO THE KOLKHOZ" : "TO THE RALLY!";
    }

    /// <summary>A card that points at a landmark (the arrow stays up over it, the card at the top).</summary>
    public bool PointsOut
    {
        get
        {
            string id = Current.step.Id;
            bool gaiTour = id is "tour_gai" or "tour_officer" or "gai_ambush" or "gai_chase";
            bool landmarkTour = id is "tour_rollers" or "tour_ford" or "tour_bends" or "tour_hairpin" or "tour_finish";
            return gaiTour || landmarkTour || id == "gulag" || id.StartsWith("kolkhoz_");
        }
    }

    /// <summary>The demo, the try or the card pointing out: the arrow and the road's words are up.</summary>
    public bool Showing => Phase is SchoolPhase.Show or SchoolPhase.Replay;
    public bool Driven => Showing || Phase is SchoolPhase.Try or SchoolPhase.Praise or SchoolPhase.Again;
}
