using System;
using System.Linq;
using System.Threading.Tasks;
using CCCPRacing.Audio;
using CCCPRacing.Camera;
using CCCPRacing.Car;
using CCCPRacing.Track;
using CCCPRacing.UI;
using CCCPRacing.Replay;
using CCCPRacing.Net;
using Godot;

namespace CCCPRacing.Race;

/// <summary>Time trial on one track: title/attract mode, countdown, laps, ghost, messages.</summary>
public partial class RaceScene : Node3D
{
    // Failed: the mission has failed (RaceScene.Fate.cs): truck, then the GULAG screen.
    // Finished: the plan is fulfilled (RaceScene.Plan.cs): the car coasts, then the PLAN FULFILLED screen.
    enum State { Title, Countdown, Racing, Paused, Failed, Finished }

    const int Substeps = 2;

    TrackData _track;
    CarParams _params;
    CarSim _car, _ghostCar;
    CarVisual _carVis, _ghostVis;
    TyreEffects _fx;
    bool _followSlurry;
    float _slurryWarp;
    TrafficCones _cones;
    Weather _weather;          // the rainy autumn (null: no weather)
    KgbOfficers _kgb;
    KolkhozWorkers _workers;          // the kolkhoz crews (Props builds them): they fall over when hit
    KnockGroup _guards;               // the dacha's security men, likewise
    Vegetation _vegetation;
    WoodFences _wood;
    Breakables _scenery;
    RescueTractor _rescue;
    float _jetWarnCd, _tankWarnCd, _incomingCd;
    float _drunkCd, _busWarnCd, _busHitCd, _busHeatDent, _busSwerveAt = -1;
    bool _busHeatTest;
    float _tractorWarnCd, _tractorHitCd, _fenceCd, _breakCd;
    ChaseCamera _cam;
    Hud _hud;
    EngineSound _snd;
    PaceBlip _paceBlip;
    DashChime _chime;      // the dashboard's warning lights (HudWarningLights.cs) sound
    readonly PaceNotes _pace = new();
    HornSound _horn;
    float _honkLeft;    // the school: a honk that woke someone at once still sounds this long (s)
    bool _mechanicHeld;      // the mechanic has the car in neutral, brakes on
    RadioMusic _radio;
    /// <summary>Already playing under the loading cover; owned by PixelFrame so disabling the race does not pause it.</summary>
    public RadioMusic StartupMusic { get; set; }
    /// <summary>The launch notice can be drawn above the loading cover before the world resumes.</summary>
    public bool StartupIntroVisible => _showLog && _hud?.Mode == HudMode.Intro;
    CarRadio _carRadio;
    LapTimer _timer;
    AutoPilot _ap;
    readonly Ghost _rec = new();

    State _state;
    float _countdown, _lapClock, _clock;
    float _grassCd, _airCd, _crashCd, _slowCd, _slowTime;
    float _missileEvade;
    bool _fenceTest, _flipTest, _autodrive, _wasAirborne, _donut, _pulse, _stomp, _followTractor, _followBabushka, _angryBabushka, _followBus, _followJet, _followTank, _followKozlik, _shotOnShell;
    Vector3 _lastJet;
    float _tractorWarp;
    Vector2? _lookAt;
    int _radioAt = -1, _radioSwitch = -1;
    float _raceClock;
    int _startAt = -1;
    float _babushkaHit, _babushkaDist = 16f;
    float _hornAt = -1, _hornHold = 1f;   // test (--horn-at): the horn sounds this long from this race time
    float _maxImpact;
    bool _impactBirch;
    ImpactMaterial _impactMaterial;
    bool _impactBroke;
    Vector3 _impactLocal, _impactDir;   // where the strongest hit of the tick landed (body frame)
    bool _crashTest;
    Vector2? _shellTree;
    string _crashKind = "wall";
    float _crashKmh;
    Vector2 _crashTarget;
    float _crashBack;
    int _crashRams = int.MaxValue;
    float _apSpeed = 1f, _apCorner = 1f, _launchKmh;
    // Title screen: personal record, bundled demonstration, then autopilot.
    // The replay poses _car itself, so visuals, tyre effects, sound and camera work unchanged.
    float _replayClock;
    float _titleReplayStart;
    bool Replaying => !PresentationActive && _state == State.Title && TitleGhost != null && TitleGhost.Count > 30;
    Ghost _bundledTitleGhost;
    bool BundledTitleReplay => _bundledTitleGhost != null && Array.TrueForAll(_ghosts, g => g == null);
    /// <summary>Personal replay takes priority; the bundled lap is title-only.</summary>
    Ghost TitleGhost => BundledTitleReplay ? _bundledTitleGhost : _ghosts[TitleSet];
    /// <summary>Which record set the title replays: the petrol rally's, else Stakhanov's, else the diesel's or the rocket fuel's.</summary>
    int TitleSet => BundledTitleReplay ? 1 : _ghosts[0] != null ? 0 : _ghosts[1] != null ? 1 : _ghosts[2] != null ? 2 : _ghosts[3] != null ? 3 : _ghosts[4] != null ? 4 : 0;
    bool TitleStakhanov => TitleSet == 1;
    // Backfire state: pops queued after lifting off the throttle at high revs.
    float _prevThrottle, _popTimer, _thumpCd, _collisionQuiet;
    int _pops;
    readonly RandomNumberGenerator _rng = new();

    // Command line: -- --start --autodrive --debug --shot=path.png --shot-at=6
    string _shotPath, _recordPath, _recordBus = "Master";
    AudioEffectRecord _recorder;
    float _shotAt = 5f;
    float _voidAt = -1;
    float _clutchKickAt = -1;

    /// <summary>What the scene runs, one at a time: the title (its replay, previews and boards), a rally run,
    /// Stakhanov laps (Training), a LAN race, a school. Set where each starts; the title ends them all.</summary>
    enum RunMode { Title, Rally, Training, Lan, School }
    RunMode _runMode;
    /// <summary>The kind of run now. A rally's norms are set at its fuel's pace (NormPlan.UseFuel), every other run's at petrol's.</summary>
    RunMode _run
    {
        get => _runMode;
        set
        {
            _runMode = value;
            NormPlan.UseFuel(value == RunMode.Rally ? _rallyFuel : FuelGrade.Petrol);
        }
    }
    /// <summary>Stakhanov laps and the LAN race: the power keys, the empty road, no plan, fuel or engine to lose.</summary>
    bool StakhanovRules => _run is RunMode.Training or RunMode.Lan;
    bool InLanRace => _run == RunMode.Lan;
    bool InSchool => _run == RunMode.School;
    // Training laps (chosen on the title screen): keys 1-4 pick the power (Training.cs); a lap is
    // ranked at the highest power used during it (_lapPeak). A restart keeps the power.
    int _power = Training.DefaultPercent, _powerStart = Training.DefaultPercent, _lapPeak;
    bool _heroNodded;
    bool? _commentsForce;
    // Title menu (RaceScene.Menu.cs): two levels of rows, plus the replay line under them while a record lap replays.
    bool _replaySelected;
    // Replay focus (_watching): Enter on the replay line hides the title, only the blinking line
    // stays; Enter or Esc brings the title back. The leaderboard and the changelog (Up/Down scroll
    // it) close on any key. The frame numbers keep the press that opens one from closing it, and
    // the press that closes it from also driving the menu. _showLog also covers the TEST VERSION
    // card at launch (a notice, HudMode.Intro).
    bool _watching, _showBoard, _showLog;
    bool _tapeShoot; // Opt-in clean replay capture with the recorded world sound.
    bool _tapeSceneryOpaque; // Capture-only: keep FadeUnder and SeeThrough buildings solid for composed shots.
    bool _tapeShootOverview; // Hold the replay's overview camera for one continuous captured shot.
    ulong _overlayFrame, _overlayExitFrame;
    // Key card (HudKeys.cs): shown when a run starts from the title menu (the countdown waits, the
    // car can rev) and on K, which freezes the countdown or the race like the pause; any press closes it.
    bool _showKeys, _keysCard;
    State _keysFrom;
    ulong _keysFrame;
    // Title world as a faded poster (PosterGrade), colour in replay focus (0..1).
    Godot.Environment _env;
    float _saturation = 1f;
    // Records per run, [0] SOCIALIST RALLY and [1] STAKHANOV LAPS: the record-lap ghost, the best lap,
    // the top-10 board (a qualifying lap stops the game for the name) and the entry added this session
    // (shown highlighted). _best and _board are the current run's; the title always shows the rally's.
    readonly Ghost[] _ghosts = new Ghost[RecordStore.Sets];
    readonly float[] _bestLaps = { -1, -1, -1, -1, -1 };
    readonly Leaderboard[] _boards = new Leaderboard[RecordStore.Sets];
    readonly int[] _boardHighlights = { -1, -1, -1, -1, -1 };
    int RecordSet => _run == RunMode.Rally ? RecordStore.SetOf(_rallyFuel) : StakhanovRules ? 1 : 0;
    Ghost _best { get => _ghosts[RecordSet]; set => _ghosts[RecordSet] = value; }
    Leaderboard _board => _boards[RecordSet];
    // The rally's ghost car drives the best lap of this session (since the game was started), not the
    // record on file: the record still makes the title's replay. Stakhanov laps race the record.
    Ghost _sessionGhost;
    /// <summary>The Socialist Rally's session best lap (the top-left BEST): the fastest valid ranked lap since this run's start or restart; -1 before the first. Not the leaderboard record (_timer.BestLap keeps that, seeded from the files),
    /// and not stored in a save: a resumed rally starts a fresh session best.</summary>
    float _sessionBest = -1f;
    Ghost RaceGhost => StakhanovRules ? _best : _sessionGhost;
    Leaderboard _rallyBoard => _boards[0];
    int _boardPage;                     // the leaderboard screen shows one board: 0 rally, 1 Stakhanov
    int _boardTrack;                    // ...of this track (Tracks); other tracks' boards are read from file
    string _boardTrackArg;
    float _replayNextAt = -1;
    static bool _replayNextDone;
    string _boardPath;
    string _entryName = "";       // the name being typed on the title (RaceScene.Name.cs)
    // Test (--lap-done-at, RaceScene.Plan.cs): set while a test lap completes.
    bool _fakeLap;
    bool _noRecord;
    // Test: synthetic key presses (--key-at, --watch-exit-at), released 0.1 s later.
    readonly System.Collections.Generic.List<(float at, Key key, bool pressed)> _keyQueue = new();

    // Ghost, leaderboard and track signature; --data-dir moves them (tests).
    RecordStore _records;
    string _dataDir;
    string GhostPath => _records.GhostForSet(RecordSet);

    public override void _ExitTree()
    {
        if (LanPlaybackSource!=null) { _lanExportUi?.Dispose();LanPlaybackReleaseAccess();ViewerExit();LanPlaybackSource.Dispose();Props.ReleaseCache();return; }
        if (ViewerMode) { ViewerExit(); return; }      // RaceScene.Viewer.cs: nothing of the live world's is saved, flushed or reset
        LabExit();
        if(InstructionTrack==null) LanReplayShutdownAccess();_lanReplayCapture?.Dispose();
        DetachLan();
        TapeExit(); Props.ReleaseCache();
        FadeUnder.Off = false; SeeThrough.Off = false;
    }       // the shared meshes of the props (RaceScene leaves the tree on a track change and at the quit)

    // While a menu-started run is loading (TrackLoadingScreen), the world build yields at
    // checkpoints when its work budget is spent; the world does not run until it is whole.
    bool _booting;
    int _viewerDiagFrames;
    public bool Booting => _booting;

    const int LoadingWorkSliceMs = 25;
    readonly System.Diagnostics.Stopwatch _loadingWorkSlice = new();

    // Builders offer checkpoints, not mandatory frames. Share one work budget across all
    // phases, and start the next slice after the frame so rendering/waiting is not build work.
    async Task LoadingFrame([System.Runtime.CompilerServices.CallerLineNumber] int profLine = 0)
    {
        if (AudioMix.Closing) throw new OperationCanceledException("Application shutdown");
        if (_booting) LoadStep();      // RaceScene.LoadProgress.cs: one more checkpoint for the loading card's bar
        if (!_booting || _loadingWorkSlice.ElapsedMilliseconds < LoadingWorkSliceMs) { LoadProf.Checkpoint(profLine, false); return; }   // LOADPROF
        LoadProf.Checkpoint(profLine, true);   // LOADPROF
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (AudioMix.Closing) throw new OperationCanceledException("Application shutdown");
        LoadProf.Resume();   // LOADPROF
        if(LanPlaybackSource!=null && !_lanPlaybackExternallyDriven && !LanPlaybackCheckAccess()) throw new OperationCanceledException("LAN replay access ended");
        if (ViewerMode) ViewerBuildCheckpoint();      // RaceScene.Viewer.cs: a cancelled viewer stops building here
        _loadingWorkSlice.Restart();
    }

    public override async void _Ready()
    {
        if (!ViewerMode && LanPlaybackSource == null && InstructionTrack == null && CCCPRacing.Race.ExportChild.Active) CCCPRacing.Race.ExportChild.Begin(this);      // the MP4 exporter child (ExportChild.cs)
        try { await BuildWorld(); }
        catch (OperationCanceledException) when (AudioMix.Closing) { }
        catch (OperationCanceledException) when (LanPlaybackSource!=null && _lanPlaybackClosing) {CCCPRacing.Audio.AudioMix.Quit(this);}
        catch (Exception e) when (LanPlaybackSource!=null) {GD.PushError("LAN replay build failed: "+e.Message);CCCPRacing.Audio.AudioMix.Quit(this, 2);}
        catch (Exception e) when (ViewerMode && e is ObjectDisposedException) { }      // a viewer freed under its own build (RaceScene.Viewer.cs)
        catch (Exception e) when (ViewerMode) { ViewerError = "the viewer world could not be built: " + e.Message; GD.PushError(ViewerError); }
        finally { if (ViewerMode) ViewerBuildDone = true; }
    }

    async Task BuildWorld()
    {
        _booting = ViewerMode || LanPlaybackSource!=null || TrackLoadingScreen.Active != null || OS.HasEnvironment("FORCEBOOT");   // FORCEBOOT: test the loading path headless; a viewer always builds in slices
        _loadingWorkSlice.Restart();
        LoadStepsBegin();      // RaceScene.LoadProgress.cs
        LoadProf.Start();   // LOADPROF
        InputSetup.Ensure();
        AudioMix.Setup(this);   // Master limiter: nothing ever clips

        var def = TrackDef.Load($"res://tracks/{TrackToBuild()}.json");   // RaceScene.Tracks.cs
        NormPlan.UseTrack(def.Norms);                                      // the track's norms and medals
        // The big builds offer sliced checkpoints. LoadingFrame decides when to draw the
        // next frame so the card keeps typing without waiting after every small build step.
        foreach (var step in TrackBuilder.BuildSteps(def))
        {
            if (step != null) _track = step;
            else if (_booting) await LoadingFrame();
        }
        MeshInstance3D terrain = null;
        foreach (var step in TerrainMesh.BuildSteps(_track))
        {
            if (step != null) terrain = step;
            else if (_booting) await LoadingFrame();
        }
        AddChild(_terrainMesh = terrain);
        Node3D props = null;
        foreach (var step in Props.BuildSteps(_track))
        {
            if (step != null) props = step;
            else if (_booting) await LoadingFrame();
        }
        LoadProf.Mark("Props last step");
        await AddSliced(this, props);
        LoadProf.Mark("AddChild(props)");
        if (_booting) await LoadingFrame();
        AddChild(Airbase.BuildRunway(_track));
        AddChild(await BuildSliced(WaterMesh.BuildSteps(_track)));      // LOADSLICES
        _vegetation = new Vegetation { Name = "Vegetation" };
        AddChild(_vegetation);

        var sky = new Color(0.62f, 0.64f, 0.62f);
        AddChild(new WorldEnvironment
        {
            Environment = _env = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = sky,
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.62f, 0.6f, 0.6f),
                AmbientLightEnergy = 0.9f,
                FogEnabled = true,
                FogLightColor = sky,
                FogDensity = 0.0012f,
            },
        });
        var sun = _sunLight = new DirectionalLight3D { LightEnergy = 0.9f, ShadowEnabled = false };
        AddChild(sun);
        sun.GlobalTransform = new Transform3D(Basis.LookingAt(-TerrainMesh.SunDir, Vector3.Up), Vector3.Zero);

        _params = CarParams.ForTrack(def);
        if (_booting) await LoadingFrame();      // LOADSLICES
        _car = new CarSim(_params, _track) { Obstacles = _track.Obstacles, WorldMax = _track.WorldSize - 4f, Banks = _track.Banks };
        _carVis = CarVisual.Create(_params, new Color(0.7f, 0.13f, 0.1f));
        AddChild(_carVis);
        if (_booting) await LoadingFrame();      // LOADSLICES
        _ghostVis = CarVisual.Create(_params, new Color(0.9f, 0.9f, 1f), ghost: true);
        _ghostVis.Visible = false;
        AddChild(_ghostVis);
        _ghostCar = new CarSim(_params, _track);

        _fx = new TyreEffects { Name = "TyreEffects" };
        _fx.Init(_track);
        AddChild(_fx);
        _vegetation.Init(_track, _fx);
        AddChild(Fences.Build(_track, _fx, out _wood, out var stone));
        if (_booting) await LoadingFrame();      // LOADSLICES
        if (_track.Banks != null) { AddChild(SnowbankMesh.Build(_track)); AddChild(SnowPoles.Build(_track)); }
        if (_booting) await LoadingFrame();
        _cones = TrafficCones.Build(_track);      // the road's edges over the ice
        if (_cones != null) AddChild(_cones);
        if (def.Weather == "rain") AddChild(_weather = Weather.Create(_carVis));
        if (_track.Winter) { AddChild(Snowfall.Create(_carVis)); AddChild(await BuildSliced(SnowTracks.BuildSteps(_track))); AddChild(await BuildSliced(TaigaMesh.BuildSteps(_track))); }      // LOADSLICES
        if (_track.LogPiles.Count > 0) AddChild(await BuildSliced(LogPiles.BuildSteps(_track)));      // LOADSLICES
        foreach (var p in _track.Props)
            if (p.Type == "lorrywreck")
                AddChild(new WreckSmoke { Fx = _fx, Position = p.Position + new Basis(Vector3.Up, p.Rotation) * LogPiles.WreckSmokeAt });
        if (def.Gulag != null) AddChild(GulagCamp.Build(_track, def.Gulag, _track.Obstacles));
        _scenery = new Breakables { Name = "Breakables" };
        _scenery.Init(_track, _fx, props, stone);
        AddChild(_scenery);
        _car.BreakObstacle = _scenery.TryBreak;
        InitLensFx();             // RaceScene.LensFx.cs: summer on the lens (Ilyich's Lamp)
        _rescue = new RescueTractor { Name = "RescueTractor" };
        AddChild(_rescue);
        if (_booting) await LoadingFrame();      // LOADSLICES
        _rescue.Init(_track, _fx);
        if (_booting) await LoadingFrame();      // LOADSLICES
        if (_booting) await LoadingFrame();
        InitFate();
        if (_booting) await LoadingFrame();      // LOADSLICES
        InitPlan();
        InitCards();

        int actorObs = _track.Obstacles.Count;     // the actors' collision slots, for the empty world (RaceScene.World.cs)
        foreach (var p in _track.Props)
            if (p.Type == "drunktractor")
            {
                _tractorAll = new DrunkTractor { Name = "DrunkTractor" };
                AddChild(_tractorAll);
                _tractorAll.Init(_track, _fx, new Vector2(p.Position.X, p.Position.Z), p.Rotation);
                break;
            }

        int slurryObs = _track.Obstacles.Count;
        if (_track.Id == "ilyich")
        {
            _slurryAll = new SlurryTractor { Name = "SlurryTractor" };
            AddChild(_slurryAll);
            _slurryAll.Init(_track);
            if (_booting) await LoadingFrame();      // LOADSLICES
            BarnDoors smallDoor = null, bigDoor = null;
            foreach (Node child in props.GetChildren())
            {
                if (child is not Node3D building || !building.HasMeta("prop")) continue;
                int index = building.GetMeta("prop").AsInt32();
                if (index < 0 || index >= _track.Props.Count) continue;
                var door = building.GetNodeOrNull<BarnDoors>("BarnDoors");
                if (_track.Props[index].Type == "barn") smallDoor = door;
                if (_track.Props[index].Type == "bigbarn") bigDoor = door;
            }
            if (smallDoor != null) _tractorAll?.ConfigureDeparture(smallDoor);
            if (bigDoor != null)
            {
                var barn = _track.Props.Find(p => p.Type == "bigbarn");
                _slurryAll.ConfigureDeparture(bigDoor, new Vector2(barn.Position.X, barn.Position.Z));
            }
        }
        int babushkaObs = _track.Obstacles.Count;
        if (_track.BabushkaCount > 0)
        {
            _babushkasAll = _track.Id == "bonus" ? new Troopers { Name = "Troopers" } : new Babushkas { Name = "Babushkas" };
            AddChild(_babushkasAll);
            _babushkasAll.Init(_track, _track.BabushkaCount);
            if (_booting) await LoadingFrame();      // LOADSLICES
        }

        int drunkObs = _track.Obstacles.Count, busObs = drunkObs;
        if (_track.BusStops.Count > 0 || _track.MechanicPost != null)
        {
            _drunksAll = new DrunkMen { Name = "DrunkMen" };
            AddChild(_drunksAll);
            _drunksAll.Init(_track);
            if (_booting) await LoadingFrame();      // LOADSLICES
            string where = _track.Id == "bonus" ? "STOP BY THE PARKED AIRCRAFT" : _track.BusStops.Count > 0 ? "STOP BY THE SHELTER" : "STOP BY THE BARRACK";
            _drunksAll.Woke += () => { if(!InLanRace || LanMechanicIsLocal) _hud.Show("THE MECHANIC IS UP!", "HE HEARD THE HORN. " + where, 3f, Hud.Gold); };
            _drunksAll.RepairStarted += () => { if(!InLanRace || LanMechanicIsLocal) _hud.Show("THE MECHANIC IS ON THE JOB", "THE ENGINE MENDS BY THE SECOND. THROTTLE BREAKS IT OFF", 3f, Hud.Paper); };
            _drunksAll.NothingToFix += () => { if(!InLanRace || LanMechanicIsLocal) _hud.Show("THE MECHANIC: NOTHING TO FIX", "GO AWAY, COMRADE. HE GOES BACK TO SLEEP", 3f, Hud.Paper, Prio.Nice); };
            _drunksAll.Whack += OnMechanicWhack;
            _drunksAll.Repaired += OnMechanicRepaired;
            _drunksAll.RepairInterrupted += OnMechanicRepairInterrupted;
            busObs = _track.Obstacles.Count;
        }
        if (_track.BusStops.Count > 0)
        {
            // The bus starts on the far side of the lap and runs against the race.
            _busAll = new SovietBus { Name = "Bus" };
            AddChild(_busAll);
            _busAll.Init(_track, _fx, _track.Center.Length * 0.5f, _wood, actorObs);
            if (_booting) await LoadingFrame();      // LOADSLICES
            _busAll.Swerved += () => BusComment("THE BUS DRIVER TOOK THE DITCH", "HE HEARD YOUR HORN", Hud.Gold);   // a joke (Prio.Nice)
            _busAll.CaughtFire += () => { if (_busHeatTest) ParkByFire(); };     // the driver shouts it (no comment: docs/comments.csv)
        }

        int tankObs = _track.Obstacles.Count;
        if(_trackId=="bonus") _bonusExercise=new BonusExerciseControl();
        if (_track.TankRange is { } tr)
        {
            _tanksAll = new TankBattle { Name = "TankBattle" };
            AddChild(_tanksAll);
            foreach (var _ in _tanksAll.InitSteps(_track, _fx, new Rect2(tr.X0, tr.Z0, tr.X1 - tr.X0, tr.Z1 - tr.Z0), tr.Tanks))
                if (_booting) await LoadingFrame();
            _tanksAll.Exercise=_bonusExercise;
            _tanksAll.ShellLanded += OnShell;
            _tanksAll.Fired += muzzle =>
            {
                _hindsAll?.GroundShot(muzzle,_state == State.Racing ? _car : null);
                if(InLanRace && _net.IsHost) _net.SendEffect(muzzle,5);
                TankBlastFeel(muzzle);
            };
        }

        int jetObs = _track.Obstacles.Count;
        if (_trackId == "bonus")
        {
            _hindsAll = new HindSorties { Name = "HindSorties" };
            AddChild(_hindsAll);
            foreach (var _ in _hindsAll.InitSteps(_track, _fx))
                if (_booting) await LoadingFrame();
            _hindsAll.Exercise=_bonusExercise;
            _hindsAll.Crashed += OnJetCrash;
            _hindsAll.CannonImpact += OnHindCannon;
            _hindsAll.CannonTargets = HindCannonTargets;
            _hindsAll.AttackStarted += p =>
            {
                if (_state == State.Racing && p.DistanceTo(_car.Pos) < 200)
                    _hud.Show("HIND: CANNON RUN!", "KEEP CLEAR OF THE IMPACT LINE", 2.8f, Hud.Red, Prio.Important, urgent: true);
            };
            _hindsAll.AttackApproaching += OnHindWarning;
            _hindsAll.SoupUnshaken += () =>
            {
                if (_state == State.Racing) _hud.Show("THE SOUP HAS SEEN WORSE.","THE COOK CONTINUES.",3,Hud.Paper,Prio.Nice);
            };
            _hindsAll.Collision += p =>
            {
                if (_state == State.Racing) _hud.Show("EXERCISE COMPLETED", "TWO LANDINGS CONFIRMED", 3f, Hud.Gold, Prio.Important);
            };
        }
        else
        {
            _jetsAll = new BalalaikaSorties { Name = "BalalaikaSorties" };
            AddChild(_jetsAll);
            _jetsAll.Init(_track, _fx);
            _jetsAll.Crashed += OnJetCrash;
            _jetsAll.MissileExploded += OnMissile;
        }
        InitRevenge();
        int actorObsEnd = _track.Obstacles.Count;

        if (_booting) await LoadingFrame();
        _kgb = new KgbOfficers { Name = "KgbOfficers" };
        AddChild(_kgb);
        _kgb.Init(_track, _fx);
        _fuelOnTrack = def.Fuel;
        _depotDef = def.Depot;
        InitFuel();      // RaceScene.Fuel.cs: the tank, the depot, the babushkas' canisters
        InitDose(def);   // RaceScene.Dose.cs: the dosimeter (ATOMGRAD-4)

        // The guard Kozlik with its bear: about half a lap ahead, in the racing direction.
        int kozlikObs = _track.Obstacles.Count;
        _kozlikAll = new KozlikPatrol { Name = "Kozlik" };
        AddChild(_kozlikAll);
        _kozlikAll.Init(_track, _fx, _jetsAll, _busAll, _track.Center.Length * 0.45f);
        if (_booting) await LoadingFrame();      // LOADSLICES
        _kozlikAll.StrikeAircraft = _hindsAll;
        int ploughObs = _track.Obstacles.Count;
        if (def.Ploughs.Count > 0)
        {
            _ploughsAll = new SnowPloughs { Name = "SnowPloughs" };
            AddChild(_ploughsAll);
            _ploughsAll.Init(_track, _fx, def.Ploughs);
            if (_booting) await LoadingFrame();      // LOADSLICES
            _jetsAll.Ploughs = _ploughsAll;
        }
        int columnObs = _track.Obstacles.Count;
        if (def.Column is { } col)
        {
            _columnAll = new PrisonerColumn { Name = "PrisonerColumn", Ploughs = _ploughsAll };
            AddChild(_columnAll);
            _columnAll.Init(_track, col.Start, col.Prisoners);
        }
        int zoneObs = _track.Obstacles.Count;
        if (def.Zone != null)
        {
            _zoneAll = new ZoneLife { Name = "ZoneLife" };
            AddChild(_zoneAll);
            _zoneAll.Init(_track, _fx, def.Zone);
            if (_booting) await LoadingFrame();      // LOADSLICES
        }
        int detailsObs = _track.Obstacles.Count;
        _detailsAll = await BuildSliced(SiberiaDetails.BuildSteps(_track, _jetsAll));      // LOADSLICES
        AddChild(_detailsAll);
        int btrObs=_track.Obstacles.Count;
        if (_trackId == "bonus")
        {
            _btrAll = new BtrPatrols { Name = "BtrPatrols" };
            AddChild(_btrAll);
            foreach (var _ in _btrAll.InitSteps(_track, _fx))
                if (_booting) await LoadingFrame();
        }
        if(_trackId=="bonus") { _campGate=new CampGate {Name="CampGate"};AddChild(_campGate);_campGate.Init(_track);_campGate.Opened+=()=>_hud.Show("THE GUARD RAISES THE BARRIER","DRIVE THROUGH OR BREAK IT",2.5f,Hud.Gold,Prio.Nice); }
        int dynamicObstaclesEnd = _track.Obstacles.Count;
        InitSpeedTrap(def); // Build before the world-tape index; its own replay hook parks its two knockable slots.
        if (_booting) await LoadingFrame();      // LOADSLICES
        KeepWorld((Actors.Btr,btrObs,dynamicObstaclesEnd),(Actors.Zone, zoneObs, detailsObs), (Actors.Details, detailsObs, btrObs), (Actors.Ploughs, ploughObs, columnObs), (Actors.Column, columnObs, zoneObs),
            (Actors.Tractor, actorObs, slurryObs), (Actors.Slurry, slurryObs, babushkaObs), (Actors.Babushkas, babushkaObs, drunkObs), (Actors.Drunks, drunkObs, busObs),
            (Actors.Bus, busObs, tankObs), (Actors.Tanks, tankObs, jetObs), (Actors.Jets, jetObs, actorObsEnd), (Actors.Kozlik, kozlikObs, ploughObs),
            (Actors.Liaison, _kgb.LiaisonObstacle, _kgb.LiaisonObstacle + 1), (Actors.Depot, _depot.Obstacle, _depot.Obstacle + 1));
        HideDepotWithoutFuel();

        _cam = new ChaseCamera();
        if (_booting) await LoadingFrame();      // LOADSLICES
        AddChild(_cam);
        if (ViewerMode) _cam.Snap(_track.StartPos);      // (the viewer looks at the start from now on: what is drawn there is drawn, and its pipelines compiled, as the world builds up, not all in the one frame the camera first gets there)

        var layer = new CanvasLayer { Layer = 2, Visible = !ViewerMode };      // (a viewer never draws its hud: hidden from the start, so its first canvas frame and font glyphs are not rendered while the world builds) above the softening (layer 1), so the HUD stays sharp
        if (_booting) await LoadingFrame();
        _hud = new Hud { TrackName = _track.Name };
        layer.AddChild(_hud);
        if (_booting) await LoadingFrame();      // LOADSLICES
        AddChild(layer);
        if (_booting) await LoadingFrame();      // LOADSLICES

        _snd = new EngineSound();
        AddChild(_snd);
        _paceBlip = new PaceBlip();
        AddChild(_paceBlip);
        _chime = new DashChime();
        AddChild(_chime);
        _horn = new HornSound();
        AddChild(_horn);
        _radio = StartupMusic;
        if (_radio == null)
        {
            _radio = new RadioMusic { Subtle = true, VolumeDb = 2f };   // title music: louder, barely a radio
            AddChild(_radio);
        }
        _carRadio = new CarRadio();
        if (_booting) await LoadingFrame();      // LOADSLICES
        AddChild(_carRadio);
        _carRadio.Announce += text => _hud.ShowRadio(text);

        if (_booting) await LoadingFrame();
        _timer = new LapTimer(_track);
        _timer.LapCompleted += OnLap;
        _timer.LineMissed += () => _hud.Alert("MISSED THE LINE!", "THE LAP ENDS BETWEEN THE POSTS. BACK THROUGH, TOVARISH", 4f, Hud.Red, Prio.Critical, urgent: true);
        _timer.VoidLapCompleted += OnVoidLap;
        _timer.ShortcutDetected += () => _hud.Show(Slogans.Shortcut, "GO BACK TO THE ROAD", 3f, Hud.Red, Prio.Critical);

        bool start = false, training = false, watch = false, board = false, intro = true;
        foreach (string a in InstructionTrack == null ? CmdArgs() : Array.Empty<string>())
        {
            if (a == "--start") start = true;
            else if (a == "--no-record") _noRecord = true;                // test: ignore the saved record lap (and never save one)
            else if (a.StartsWith("--rng-seed=")) _rng.Seed = ulong.Parse(a["--rng-seed=".Length..]);   // test: the same backfires and sputters each run (a backfire's camera jolt can decide a babushka in the lens)
            else if (a == "--watch") watch = true;                        // test: start on the title in replay focus
            else if (a == "--tape-shoot") _tapeShoot = true;
            else if (a == "--tape-scenery-opaque") _tapeSceneryOpaque = true;
            else if (a.StartsWith("--tape-camera-sequence=")) _tapeCameraSequencePath = a["--tape-camera-sequence=".Length..];
            else if (a == "--tape-camera=overview") _tapeShootOverview = true;
            else if (a is "--tape-camera=trailer-bonnet" or "--tape-camera=trailer-front"
                or "--tape-camera=trailer-side" or "--tape-camera=trailer-crane"
                or "--tape-camera=trailer-overhead" or "--tape-camera=trailer-world")
                _tapeShootTrailerDetail = a["--tape-camera=".Length..];
            else if (a.StartsWith("--tape-eye=")) _tapeTrailerEye = TrailerTapeVector(a["--tape-eye=".Length..]);
            else if (a.StartsWith("--tape-eye-end=")) _tapeTrailerEyeEnd = TrailerTapeVector(a["--tape-eye-end=".Length..]);
            else if (a.StartsWith("--tape-look=")) _tapeTrailerLook = TrailerTapeVector(a["--tape-look=".Length..]);
            else if (a is "--tape-camera=siberia-ridge" or "--tape-camera=siberia-runway"
                or "--tape-camera=siberia-ice" or "--tape-camera=siberia-descent"
                or "--tape-camera=siberia-bonnet" or "--tape-camera=siberia-outer-crane"
                or "--tape-camera=siberia-front-follow" or "--tape-camera=siberia-inner-bank"
                or "--tape-camera=siberia-hairpin-overhead" or "--tape-camera=siberia-airbase-wide"
                or "--tape-camera=siberia-jet-hero")
                _tapeShootSiberiaDetail = a["--tape-camera=".Length..];
            else if (a is "--tape-camera=ilyich-impact" or "--tape-camera=ilyich-ford" or "--tape-camera=ilyich-east" or "--tape-camera=ilyich-sweep")
                _tapeShootIlyichDetail = a["--tape-camera=".Length..];
            else if (a == "--tape-camera=ilyich-plan") _tapeShootIlyichPlan = true;
            else if (a == "--tape-camera=ilyich-plan-v2") _tapeShootIlyichPlanV2 = true;
            else if (a == "--tape-camera=ilyich-plan-v3") _tapeShootIlyichPlanV3 = true;
            else if (a.StartsWith("--tape-camera-marks="))
                _tapeCameraMarks = Array.ConvertAll(a["--tape-camera-marks=".Length..].Split(','),
                    s => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture));
            else if (a == "--keys-card") _keysCard = true;                // test: show the key card at the countdown with --start
            else if (a == "--leaderboard") board = true;                  // test: start on the leaderboard screen
            else if (a.StartsWith("--replay-next-at=") && !_replayNextDone) _replayNextAt = float.Parse(a["--replay-next-at=".Length..], System.Globalization.CultureInfo.InvariantCulture);  // test: at this time select the replay line and press Right (once)
            else if (a.StartsWith("--board-track=")) _boardTrackArg = a["--board-track=".Length..];  // test: ...showing this track's boards
            else if (a == "--no-intro") intro = false;                    // test: skip the test-version card
            else if (a == "--menu-single") _menuSingleTest = true;         // test: the title opens on the SINGLE PLAYER row (its hint box)
            else if (a.StartsWith("--data-dir=")) _dataDir = a["--data-dir=".Length..];  // test: keep ghost, leaderboard and signature in this directory instead of user://
            else if (a.StartsWith("--leaderboard-file=")) _boardPath = a["--leaderboard-file=".Length..];  // test: use this leaderboard file instead of the real one
            else if (a.StartsWith("--lap-done-at=")) AddFakeLaps(a["--lap-done-at=".Length..]);  // test: complete the lap at race time T with this lap time, e.g. --lap-done-at=5:58.2 or a list 5:58.2,9:49 (repeatable; never saves a ghost)
            else if (a.StartsWith("--watch-exit-at=")) QueueKey(float.Parse(a["--watch-exit-at=".Length..], System.Globalization.CultureInfo.InvariantCulture), Key.Enter);  // test: press Enter at this time (leaves replay focus)
            else if (a.StartsWith("--key-at=") && _resume == null && _previewResume < 0)       // test: press a key at a time, e.g. --key-at=2.5:Right (the first build only, not after a track change)
            {
                var tk = a["--key-at=".Length..].Split(':');
                QueueKey(float.Parse(tk[0], System.Globalization.CultureInfo.InvariantCulture), OS.FindKeycodeFromString(tk[1]),
                    tk.Length > 2 ? float.Parse(tk[2], System.Globalization.CultureInfo.InvariantCulture) : 0.1f);
            }
            else if (a.StartsWith("--comments=")) _commentsForce = a.EndsWith("=1");  // test: comments on (1) or off (0) at every run start
            else if (a == "--training") training = true;                 // test: training laps (preselected on the title)
            else if (a.StartsWith("--grade=")) _rallyFuel = FuelGrades.Parse(a["--grade=".Length..]);      // test: the rally's fuel (diesel, petrol, rocket)
            else if (a.StartsWith("--power=")) _powerStart = int.Parse(a["--power=".Length..]);  // test: Stakhanov power in percent at the start
            else if (a == "--autodrive") _autodrive = true;
            else if (a.StartsWith("--launch=")) _launchKmh = float.Parse(a["--launch=".Length..], System.Globalization.CultureInfo.InvariantCulture);  // test: at GO the car is already doing this many km/h (camera at 380 km/h)
            else if (a.StartsWith("--ap-corner=")) _apCorner = float.Parse(a["--ap-corner=".Length..], System.Globalization.CultureInfo.InvariantCulture);  // test: the autopilot's corner speeds use this many times the lateral grip
            else if (a.StartsWith("--ap-speed=")) _apSpeed = float.Parse(a["--ap-speed=".Length..], System.Globalization.CultureInfo.InvariantCulture);  // test: autopilot target speeds this many times higher (overdrive at speed)
            else if (a == "--debug") _hud.ShowDebug = true;
            else if (a == "--clean-hud") _hud.Clean = true;              // trailer capture: no HUD panels, title text or messages (speech bubbles stay)
            else if (a.StartsWith("--cam-zoom=")) _cam.Offset /= float.Parse(a["--cam-zoom=".Length..], System.Globalization.CultureInfo.InvariantCulture);  // trailer capture: camera this many times closer, same angle
            else if (a == "--donut") _donut = true;                       // test: full throttle + lock
            else if (a == "--pulse") _pulse = true;                       // test: throttle 1.2 s on, 0.8 s off
            else if (a == "--stomp") _stomp = true;
            else if (a == "--fence") _fenceTest = true;
            else if (a == "--crash") _crashTest = true;                   // test: flat out into the nearest stone wall, again and again
            else if (a.StartsWith("--crash=")) { _crashTest = true; _crashRams = int.Parse(a["--crash=".Length..]); }  // test: ...only N times, then stop
            else if (a.StartsWith("--crash-target=")) { _crashTest = true; _crashKind = a["--crash-target=".Length..]; }  // test: --crash into the nearest wall (default), pole, tree (big) or sapling instead
            else if (a == "--shell-tree" && _tanks != null)                 // test: the first tank round with a tree near its line of fire lands next to that tree; camera on it, screenshot when it has fallen
            {
                _shotAt = 1e9f;
                _tanks.TestAim = (from, to) =>
                {
                    bool InSight(Vector2 tree)                                    // no hill in the way
                    {
                        float h0 = _track.MeshHeight(from.X, from.Y) + 2.5f, h1 = _track.MeshHeight(tree.X, tree.Y);
                        for (float f = 0.1f; f < 0.95f; f += 0.05f)
                        {
                            Vector2 q = from.Lerp(tree, f);
                            if (_track.MeshHeight(q.X, q.Y) > Mathf.Lerp(h0, h1, f) - 0.3f) return false;
                        }
                        return true;
                    }
                    if (_shellTree != null || _scenery.TreeInLine(from, to, InSight) is not Vector2 tree) return null;
                    _shellTree = _lookAt = tree;
                    GD.Print($"shell-tree: firing from {from} at the tree at {tree}");
                    return tree + (from - tree).Normalized() * 1.5f;
                };
            }
            else if (a == "--no-break") _car.BreakObstacle = null;       // test: trees, poles and walls never break (as before)
            else if (a.StartsWith("--crash-speed=")) _crashKmh = float.Parse(a["--crash-speed=".Length..], System.Globalization.CultureInfo.InvariantCulture);  // test: --crash no faster than this many km/h
            else if (a == "--flip") _flipTest = true;                     // test: car lands on its roof at GO                   // test: flat out through the nearest wooden fence                       // test: 3.5 s flat out, then full brake
            else if (a.StartsWith("--slurry-warp=")) _slurryWarp = float.Parse(a["--slurry-warp=".Length..], System.Globalization.CultureInfo.InvariantCulture);
            else if (a == "--follow-slurry") _followSlurry = true;
            else if (a == "--follow-tractor") _followTractor = true;      // test: camera on the tractor
            else if (a == "--follow-babushka") _followBabushka = true;    // test: camera on babushka 0
            else if (a == "--babushka-angry") _angryBabushka = true;      // test: babushka 0 shakes her fist
            else if (a.StartsWith("--babushka-hit")) _babushkaHit = a.Contains('=') ? float.Parse(a[(a.IndexOf('=') + 1)..], System.Globalization.CultureInfo.InvariantCulture) : 60f;  // test: at GO, cruise into babushka 0 at this km/h (default 60, negative: against the race direction)
            else if (a.StartsWith("--horn-at="))                          // test: sound the horn at this race time (s after GO), for :hold seconds (default 1)
            {
                var hv = a["--horn-at=".Length..].Split(':');
                _hornAt = float.Parse(hv[0], System.Globalization.CultureInfo.InvariantCulture);
                if (hv.Length > 1) _hornHold = float.Parse(hv[1], System.Globalization.CultureInfo.InvariantCulture);
            }
            else if (a.StartsWith("--babushka-dist=")) _babushkaDist = float.Parse(a["--babushka-dist=".Length..], System.Globalization.CultureInfo.InvariantCulture);  // test: --babushka-hit starts this many m before her (default 16)
            else if (a.StartsWith("--babushka-lens") && _babushkas != null) _babushkas.ForceLens = !a.EndsWith("=0");  // test: babushka hits go into the camera lens (=0 never)
            else if (a == "--follow-bus") _followBus = true;              // test: camera on the bus
            else if (a.StartsWith("--bus-swerve-at=")) _busSwerveAt = float.Parse(a["--bus-swerve-at=".Length..], System.Globalization.CultureInfo.InvariantCulture);  // test: the bus driver hears a horn at this time and takes the ditch
            else if (a == "--bus-heat") _busHeatTest = true;             // test: when the bus catches fire, the car is parked 2 m from it
            else if (a.StartsWith("--bus-swerve-into=") && _bus != null) _bus.TestInto = a["--bus-swerve-into=".Length..];  // test: every run starts with the bus swerving where it ends up in the ditch, dented against something (dent) or on fire (fire)
            else if (a == "--follow-jet") _followJet = true;              // test: camera on the first Balalaika
            else if (a == "--missile-test" && _jets != null) _jets.TestTarget = () => _car.Pos;  // test: the first flying Balalaika fires both missiles at the car
            else if (a == "--missile-test-repeat" && _jets != null) { _jets.TestTarget = () => _car.Pos; _jets.TestRepeat = true; }  // test: --missile-test for every jet that flies ahead of the car
            else if (a.StartsWith("--missile-evade"))                     // test: autopilot, but swerve hard across a missile's path this many s before impact (default 0.6)
            {
                _autodrive = true;
                _missileEvade = a.Contains('=') ? float.Parse(a[(a.IndexOf('=') + 1)..], System.Globalization.CultureInfo.InvariantCulture) : 0.6f;
            }
            else if (a == "--follow-tank") _followTank = true;            // test: camera on the first tank
            else if (a == "--follow-kozlik") _followKozlik = true;              // test: camera on the guard Kozlik
            else if (a.StartsWith("--kozlik-at=")) _kozlik.StartS = float.Parse(a["--kozlik-at=".Length..], System.Globalization.CultureInfo.InvariantCulture);  // test: Kozlik starts at this road metre
            else if (a == "--kozlik-guard") _kozlik.AlwaysGuard = true;         // test: Kozlik stands guard at every pass
            else if (a == "--gai-chase") _gaiChaseTest = true;                   // test: the Kozlik waits in the GAI ambush from the start and chases the car at the radar, whatever its speed
            else if (a == "--kozlik-ram") { _kozlik.CentreLine = true; _kozlik.StartS = -60f; }  // test: Kozlik drives the centre line into the car parked on the start line
            else if (a == "--shot-on-shell") { _shotOnShell = true; _shotAt = 1e9f; }  // test: screenshot right after a nearby shell lands
            else if (a.StartsWith("--radio=")) _radioAt = int.Parse(a["--radio=".Length..]);  // test: tune the car radio after GO
            else if (a.StartsWith("--radio-switch=")) _radioSwitch = int.Parse(a["--radio-switch=".Length..]);  // test: change station 3 s later
            else if (a.StartsWith("--look="))                             // test: camera on a map point x,z
            {
                var xz = a["--look=".Length..].Split(',');
                _lookAt = new Vector2(float.Parse(xz[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(xz[1], System.Globalization.CultureInfo.InvariantCulture));
            }
            else if (a.StartsWith("--tractor-warp=")) _tractorWarp = float.Parse(a["--tractor-warp=".Length..], System.Globalization.CultureInfo.InvariantCulture);
            else if (a.StartsWith("--at=")) _startAt = int.Parse(a["--at=".Length..]); // test: start at centre index
            else if (a.StartsWith("--shot=")) _shotPath = a["--shot=".Length..];
            else if (a.StartsWith("--record=")) _recordPath = a["--record=".Length..];   // test: capture audio to WAV
            else if (a.StartsWith("--record-bus=")) _recordBus = a["--record-bus=".Length..];  // test: which bus (default Master)
            else if (a.StartsWith("--clutch-kick=")) _clutchKickAt = float.Parse(a["--clutch-kick=".Length..], System.Globalization.CultureInfo.InvariantCulture);  // test: flat out with the clutch in, dump it at this race time
            else if (a.StartsWith("--void-at=")) _voidAt = float.Parse(a["--void-at=".Length..], System.Globalization.CultureInfo.InvariantCulture);  // test: press R at this time
            else if (a.StartsWith("--shot-at=")) _shotAt = float.Parse(a["--shot-at=".Length..], System.Globalization.CultureInfo.InvariantCulture);
        }

        if (_recordPath != null)
        {
            _recorder = new AudioEffectRecord { Format = AudioStreamWav.FormatEnum.Format16Bits };
            if (AudioServer.GetBusIndex(_recordBus) < 0)
            {
                // A test bus (e.g. "Jet", which CCCP_JET_DEBUG routes the jet sounds to) feeding Master.
                AudioServer.AddBus();
                AudioServer.SetBusName(AudioServer.BusCount - 1, _recordBus);
                AudioServer.SetBusSend(AudioServer.BusCount - 1, "Master");
            }
            AudioServer.AddBusEffect(AudioServer.GetBusIndex(_recordBus), _recorder);
            _recorder.SetRecordingActive(true);
        }

        LoadProf.Mark("setup 538..files");
        if (ViewerMode || LanPlaybackSource!=null) ViewerInitFiles();      // RaceScene.Viewer.cs: no record, profile or campaign file is read or written
        else await LoadPlayerFiles();

        LoadProf.Mark("files");
        if (LanPlaybackSource!=null) { }
        else if (ViewerMode) await ViewerEnter();      // RaceScene.Viewer.cs: the lap plays in this private world, no menu, radio or run
        else if (start && !_tapeReloaded) { _run = training ? RunMode.Training : RunMode.Rally; MenuPlayed(training ? MenuItem.Training : MenuItem.Race); NewRunPower(); _carRadio.SetOn(RadioPreference(), false); DefaultComments(); EnterCountdown(); if (_keysCard) ShowKeys(); }
        else
        {
            EnterTitle();
            if (training) MenuPlayed(MenuItem.Training);
            if (watch) EnterWatch();
            if (board) EnterBoard();
            if (intro && !(watch || board || _hud.Clean)) EnterIntro();
        }

        if (!ViewerMode && LanPlaybackSource==null) { bool sliced=_booting;_booting=true;await InitLan();_booting=sliced; }     // RaceScene.Lan.cs; --lan-host / --lan-join go straight to the lobby
        if (_gaiChaseTest) _kozlik?.GuardAtTrap(); // Command-line flags are parsed after world construction.
        if (_track.Props.Exists(p => p.Type == "checkpoint")) { _dosimetrist = ZoneDosimetrist.Build(_track); AddChild(_dosimetrist); }
        // People and the speed sign fall over when the car hits them (Knockdown), not a wall.
        foreach (var k in _trap?.Knocks() ?? Array.Empty<(int, Func<Vector3, bool>)>()) _scenery.AddKnock(k.Item1, k.Item2);
        foreach (var k in _dosimetrist?.Knocks() ?? Array.Empty<(int, Func<Vector3, bool>)>()) _scenery.AddKnock(k.Item1, k.Item2);
        foreach (var k in _kgb?.Knocks() ?? Array.Empty<(int, Func<Vector3, bool>)>()) _scenery.AddKnock(k.Item1, k.Item2);
        foreach (var k in _drunksAll?.Knocks() ?? Array.Empty<(int, Func<Vector3, bool>)>()) _scenery.AddKnock(k.Item1, k.Item2);
        _workers = FindChild("KolkhozWorkers", true, false) as KolkhozWorkers;      // the field crews, loaders and anglers
        foreach (var k in _workers?.Knocks() ?? Array.Empty<(int, Func<Vector3, bool>)>()) _scenery.AddKnock(k.Item1, k.Item2);
        if (_campGate != null)
        {
            foreach (var k in _campGate.Knocks()) _scenery.AddKnock(k.Item1, k.Item2);
            _scenery.Restarted += _campGate.Reset;
        }
        _guards = FindChild("DachaGuards", true, false) as KnockGroup;           // the dacha's security men
        foreach (var k in _guards?.Knocks() ?? Array.Empty<(int, Func<Vector3, bool>)>()) _scenery.AddKnock(k.Item1, k.Item2);
        LoadProf.Mark("knocks+misc 704..DayNight");
        InitDayNight();   // RaceScene.DayNight.cs: last, the world is all there
        LoadProf.Mark("InitDayNight");
        if (_booting)
            foreach (var _ in _dayNight.CollectInitialSteps()) await LoadingFrame();
        LoadProf.Mark("CollectInitialSteps tail");
        InitMinimap();    // RaceScene.Minimap.cs
        if (_state == State.Countdown) LabRunStarts();      // (a --start run began its countdown before the lab existed)
        LoadProf.Mark("InitMinimap");
        if (_booting && LoadProf.On) { LoadProf.Report(_track.Id); if (LoadProf.Quit) CCCPRacing.Audio.AudioMix.Quit(this); }   // LOADPROF
        LoadStepsEnd();      // RaceScene.LoadProgress.cs
        _booting = false;
        if (LanPlaybackSource!=null) { LanPlaybackEnter();return; }
        if (InstructionTrack != null)
        {
            _showLog = _askName = false;
            if (ViewerMode) return;      // replay viewer: a plain world, no lesson (RaceScene.Viewer.cs)
            EnterSchool(_instructionCourse, true, _instructionChapter);
            return;
        }
        SchoolFlagStart();
        if(FinishLanWorldRebuild()) return;
        ResumeAfterReload();   // RaceScene.Tracks.cs: a track change comes back to its card
        StartTrackPreviewMovie();
        StartCarTourFromArgs();      // RaceScene.CarTour.cs: --car-tour=<fuel>
        CheatSelfTestFromArgs();      // RaceScene.Cheat.cs: --cheat-test
        StartTrailerCapture();

        // Test: let the tractor plough for a while before the first frame.
        for (float t = 0; _tractor != null && t < _tractorWarp; t += 1f / 60f) _tractor.Tick(1f / 60f);
        for (float t = 0; _slurry != null && t < _slurryWarp; t += 1f / 60f) _slurry.Tick(1f / 60f);
    }

    // ------------------------------------------------------------ states

    void PlaceAtStart()
    {
        ClearCarFeedback();
        TapeFxReset();
        TapeRelease();        // the title replay's tape lets go of its parts before the world is reset
        PrepareReplayCamera();
        _wrongWay = 0;
        _hud.WrongWay = false;
        ApplyWorld(_state == State.Countdown && StakhanovRules && (!InLanRace || !_lanReveal.Obstacles));     // Stakhanov laps: an empty road
        LoadProf.Mark("PlaceAtStart: after ApplyWorld");
        _scenery.Reset();                                        // before --crash picks its target
        LoadProf.Mark("PlaceAtStart: after scenery.Reset");
        _cones?.Reset();
        _trap?.Reset();
        _dosimetrist?.Reset();
        _workers?.ResetKnocks();      // knocked-over workers stand again
        _guards?.ResetKnocks();
        if (_startAt >= 0)
            _car.Reset(_track.CenterPos(_startAt) + Vector3.Up * 0.7f, TrackData.YawFor(_track.CenterDir(_startAt)));
        else
            _car.Reset(_track.StartPos, _track.StartYaw);
        if (InLanRace) LanPlaceOnGrid();
        if (_fenceTest)
        {
            var (mid, dir) = _wood.NearestSpan(new Vector2(_car.Pos.X, _car.Pos.Z));
            var n = new Vector2(-dir.Y, dir.X);
            Vector2 from = mid - n * 30f;
            _car.Reset(new Vector3(from.X, _track.MeshHeight(from.X, from.Y) + 0.7f, from.Y), TrackData.YawFor(n));
        }
        if (_crashTest) AimAtObstacle();
        _car.Neutral = _car.Clutch = false;
        _carVis.Damage?.Reset();
        _fx.Clear();
        LoadProf.Mark("PlaceAtStart: after fx.Clear");
        ResetActors();      // not the ones out of the world: Reset puts their collision circles back on the road
        LoadProf.Mark("PlaceAtStart: after ResetActors");
        if (_gaiChaseTest) _kozlik?.GuardAtTrap();
        ResetGaiChase();
        _wood.Reset();
        LoadProf.Mark("PlaceAtStart: after wood.Reset");
        _rescue.Reset();
        LoadProf.Mark("PlaceAtStart: after rescue.Reset");
        if (OS.HasEnvironment("WORLDDBG")) WorldStrays();
        ResetFate(_state == State.Countdown && (!StakhanovRules || InLanRace));    // a restart repairs the engine too
        LoadProf.Mark("PlaceAtStart: after ResetFate");
        ResetRevenge();
        ResetPlan(_state == State.Countdown && !StakhanovRules && _tapeSessionLapsDir == null);    // trailer lap sessions have no finite norm window
        LoadProf.Mark("PlaceAtStart: after ResetPlan");
        ResetCards();
        LoadProf.Mark("PlaceAtStart: after ResetCards");
        ResetFuel(_state == State.Countdown && !StakhanovRules);    // ...and the fuel
        ResetDose(_state == State.Countdown && !StakhanovRules);    // ...and the dose
        _ap = new AutoPilot(_track) { Aggression = _apSpeed, CornerGrip = _apCorner };
        LoadProf.Mark("PlaceAtStart: after AutoPilot");
        _cam.Snap(_car.Pos);
    }

    void EnterTitle()
    {
        _replayCamera.Reset();
        _demoCameraClock = 0;
        _state = State.Title;
        _hud.Mode = HudMode.Title;
        _titleReplayStart = TapeStart(TitleReplayStart());
        _replayClock = _titleReplayStart;
        if (TrackLoadingScreen.Active is not { HasMusic: true }) _radio.Play();     // (a loading card already carries the music on)
        AudioMix.MuteSfx(!_tapeShoot);   // a tape shoot needs the replay's engine and world sound
        _carRadio.Suspend(true);
        _timer.Stop();
        _ghostVis.Visible = false;
        _snd.Volume = _tapeShoot ? 1f : 0.1f;      // demo car stays under the radio unless shooting
        _run = RunMode.Title;     // the title replay runs in the full rally world
        ResetMenu();             // the top row, on the parent of the mode played last
        if (_menuSingleTest) OpenMenu(MenuItem.Single);
        _watching = _showBoard = _boardScoped = _showLog = _showFiles = _showLessons = _showKeys = _replaySelected = false;
        if (!_hud.Clean) SetSaturation(0f);     // the menu's world is a faded poster (PosterGrade)
        ApplyTrainingPower();
        PlaceAtStart();
        _ap.Aggression = 0.9f;
        if (Replaying)
        {
            TitleGhost.Rewind();
            TitleGhost.Apply(_replayClock, _car, 0);
            TapeReplayEnter();
            _cam.Snap(_car.Pos);
        }
        if (_tapeShoot)
        {
            if (!Replaying || _tapePlayer == null)
            {
                GD.PushError("Tape shoot requires a compatible saved ghost and world tape for this track");
                CCCPRacing.Audio.AudioMix.Quit(this, 1);
            }
            else if ((_tapeShootIlyichPlan || _tapeShootIlyichPlanV2 || _tapeShootIlyichPlanV3)
                && (_trackId != "ilyich" || _tapeCameraMarks.Length != (_tapeShootIlyichPlanV3 ? 8 : 6)
                || _tapeCameraMarks[0] < 1f || _tapeCameraMarks[^1] >= TitleGhost.Duration - 1f
                || Enumerable.Range(1, _tapeCameraMarks.Length - 1).Any(i => _tapeCameraMarks[i] - _tapeCameraMarks[i - 1] < 1f)))
            {
                GD.PushError("Ilyich tape camera plan needs increasing marks inside this lap");
                CCCPRacing.Audio.AudioMix.Quit(this, 1);
            }
            else GD.Print($"tape shoot: replay begins at movie frame {Engine.GetProcessFrames()}, tape clock {_replayClock:F3}");
        }
        else if (_track.Id == "ilyich")
        {
            // With no saved lap, the autopilot starts far enough along for the barn to leave the camera.
            _car.Reset(_track.CenterPos(130) + Vector3.Up * 0.7f, TrackData.YawFor(_track.CenterDir(130)));
            _cam.Snap(_car.Pos);
        }
    }

    /// <summary>Begin the Ilyich title replay once the start barn has left the camera view.</summary>
    float TitleReplayStart()
    {
        if (_track.Id != "ilyich" || TitleGhost == null || TitleGhost.Count <= 30) return 0f;
        Vector2 line = _track.Center[0], along = _track.CenterDir(0);
        for (int i = 0; i < TitleGhost.Count; i++)
        {
            Vector3 p = TitleGhost.Sample(i / Ghost.SampleRate).Origin;
            if ((new Vector2(p.X, p.Z) - line).Dot(along) >= 130f) return i / Ghost.SampleRate;
        }
        return 0f;
    }

    /// <summary>Enter on the replay line: replay focus, the replay carries on in colour with only the line over it.</summary>
    void EnterWatch()
    {
        if (!Replaying) return;
        _watching = _replaySelected = true;
        _overlayFrame = Engine.GetProcessFrames();
        _hud.Mode = HudMode.Watch;
    }

    /// <summary>Key card over the countdown, the race or the pause menu; freeze: the game stops under it like the pause.</summary>
    void ShowKeys(bool freeze = false)
    {
        _showKeys = true;
        _keysFrame = Engine.GetProcessFrames();
        _keysFrom = _state;
        if (freeze) _state = State.Paused;
        _hud.Mode = HudMode.Keys;
    }

    void CloseKeys()
    {
        _showKeys = false;
        _overlayExitFrame = Engine.GetProcessFrames();
        _state = _keysFrom;
        _hud.Mode = _state == State.Paused ? HudMode.Paused : _state == State.Countdown ? HudMode.Countdown : HudMode.Race;
    }

    /// <summary>The world's colour: 1 its own, 0 the title's faded poster look (PosterGrade), between them a fade.</summary>
    void SetSaturation(float s)
    {
        _saturation = s;
        PosterGrade.Apply(_env, 1f - s);      // off in colour, so the picture is exactly as without it
    }

    /// <summary>Title menu LEADERBOARD: the board of honour over the title replay.</summary>
    void EnterBoard()
    {
        _showBoard = true;
        _boardScoped = false;      // (the office board is the unrestricted one)
        _boardReplaySelected = 0;
        _boardTrack = Math.Max(0, Array.FindIndex(Tracks, t => t.id == (_boardTrackArg ?? _trackId)));
        string bid = Tracks[_boardTrack].id;
        _boardPage = BoardPageWithLaps(bid, _boardPage);
        _menu = MenuItem.Board;
        _overlayFrame = Engine.GetProcessFrames();
        _hud.Mode = HudMode.Board;
    }

    /// <summary>PARTY OFFICE CREDITS: the credits file in the changelog's panel (closes like it).</summary>
    void EnterCredits()
    {
        _showLog = true;
        _menu = MenuItem.Credits;
        _overlayFrame = Engine.GetProcessFrames();
        _hud.Mode = HudMode.Changelog;
        _hud.ResetChangelog(credits: true);
    }

    /// <summary>At launch: the TEST VERSION card (version and a notice); it closes like the CHANGELOG screen.</summary>
    void EnterIntro()
    {
        _showLog = true;
        _overlayFrame = Engine.GetProcessFrames();
        _hud.Mode = HudMode.Intro;
        _hud.ResetChangelog();
    }

    /// <summary>
    /// Raw input from the window (PixelFrame forwards it; the game viewport gets none itself).
    /// On the leaderboard, in the changelog and on the key card any key, mouse or joypad button
    /// press closes it (in the changelog Up/Down, the wheel and the d-pad scroll); replay focus
    /// ends on Enter or Esc only; in the title's name question it is the typing.
    /// </summary>
    public void RawInput(InputEvent e)
    {
        if(LanPlaybackSource!=null) {if(!_lanPlaybackExternallyDriven) LanPlaybackInput(e);return;}
        if (ViewerMode) return;      // a viewer takes no input of its own; the session owner drives it
        if (StakhanovBoardRawInput(e)) return;
        if (_replayViewer is { State: not ReplayViewerState.Closed } rv) { rv.RouteInput(e); return; }      // an open viewer owns the input: the frozen live scene gets none (RaceScene.ReplayViewer.cs)
        if (TrackCardBoardsRawInput(e)) return;        // RaceScene.TrackCardBoards.cs: CHOOSE TRACK's read-only boards, before the office board's replay keys
        if (_showBoard && LeaderboardReplayInput(e)) return;
        if (_lanLeaveAsk) { if (!PadRawInput(e)) LanLeaveRawInput(e); return; }
        if (TrackLoadingScreen.Active != null) return;
        if (_instructionPlayer != null) { _instructionPlayer.RawInput(e); return; }
        if (ResignInput(e)) return;                    // RaceScene.Resign.cs: quitting is a resignation
        if (DroneCardInput(e)) return;                 // RaceScene.DroneTour.cs: any key starts the tour's flight
        if (CarTourRawInput(e)) return;                // RaceScene.CarTour.cs: any key starts a car's tour from its black card
        if (CheatCodeInput(e)) return;                 // RaceScene.Cheat.cs: the Konami code opens every fuel and track
        if (CardInput(e) || PadRawInput(e) || GulagInput(e) || PlanInput(e) || LanKeepRawInput(e) || LanRawInput(e)) return;      // LanKeep: the KEEP question after a LAN race (RaceScene.LanKeep.cs)
        if (e is InputEventKey labKey && LabKeyInput(labKey)) return;      // RaceScene.Lab.cs
        if (RecordingsRawInput(e)) return;             // RaceScene.Recordings.cs: the RECORDINGS list, KEEP and its name field
        if (SchoolChapterInput(e)) return;
        if (_state == State.Paused && e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
        {
            if (_hud.RallyExitOpen)
            {
                if (_hud.RallyExitRowAt(_hud.GetLocalMousePosition()) is >= 0 and var exitRow) ChooseRallyExit(exitRow);
            }
            else if (_hud.VisualOpen)
            {
                if (_hud.CameraHintOpen) CloseCameraHint();
                else if (_hud.VisualRowAt(_hud.GetLocalMousePosition()) is >= 0 and var visualRow) ChooseVisual(visualRow, toggleValue: _hud.VisualSwitchValueAt(visualRow, _hud.GetLocalMousePosition()));
            }
            else if (_hud.ReplayListOpen)
            {
                if (_replayOpenTask == null && _hud.ReplayRowAt(_hud.GetLocalMousePosition()) is >= 0 and var replayRow) { _hud.ReplayList.Selected = replayRow; ChooseReplayRow(replayRow); }
            }
            else if (_hud.MixerOpen)
            {
                if (_hud.MixerRowAt(_hud.GetLocalMousePosition()) is >= 0 and var mixerRow) ChooseMixer(mixerRow, toggleValue: _hud.MixerSwitchValueAt(mixerRow, _hud.GetLocalMousePosition()));
            }
            else if (_hud.SettingsOpen)
            {
                if (_hud.SettingsRowAt(_hud.GetLocalMousePosition()) is >= 0 and var settingsRow) ChooseSettings(settingsRow, _hud.SettingsSwitchValueAt(settingsRow, _hud.GetLocalMousePosition()));
            }
            else if (_hud.PauseNormsOpen) ClosePauseNorms();
            else if (_hud.PauseRowAt(_hud.GetLocalMousePosition()) is >= 0 and var row)
            {
                _hud.PauseSelection = row;
                PauseChoose(row);
            }
            return;
        }
        if (_askName)
        {
            if (e is InputEventKey { Pressed: true } key) NameAskKey(key);
            return;
        }
        if (FilesInput(e) || LessonsInput(e)) return;      // RaceScene.Files.cs, .SchoolMenu.cs
        bool press = e is InputEventKey { Pressed: true, Echo: false } or InputEventMouseButton { Pressed: true }
            or InputEventJoypadButton { Pressed: true };
        if (_showKeys)
        {
            if (press && Engine.GetProcessFrames() > _keysFrame) CloseKeys();
            return;
        }
        // Stakhanov laps: 1-4 pick the power (the rally is always 100 %).
        if (press && StakhanovRules && _state is (State.Countdown or State.Racing or State.Paused)
            && e is InputEventKey pk && PowerKey(pk) is > 0 and var n && n <= Training.LapsCount)
        {
            SetPower(Training.PercentForKey(n));
            return;
        }
        if (!(_watching || _showBoard || _showLog) || Engine.GetProcessFrames() <= _overlayFrame) return;
        if (_showLog && LogScroll(e) is int rows)
        {
            _hud.ScrollChangelog(rows);
            return;
        }
        if (!press || _hud.Mode == HudMode.Intro && e is InputEventKey { Keycode: Key.Alt }) return;   // Alt(+Enter) keeps the card
        if (_watching && e is not InputEventKey { Keycode: Key.Enter or Key.KpEnter or Key.Escape, AltPressed: false }) return;
        _watching = _showBoard = _showLog = false;
        _overlayExitFrame = Engine.GetProcessFrames();
        _hud.Mode = HudMode.Title;      // the replay just carries on behind the menu
    }

    /// <summary>Rows to scroll the changelog by for this event (held arrows repeat), or null when it is not a scroll.</summary>
    static int? LogScroll(InputEvent e) => e switch
    {
        InputEventKey { Pressed: true, Keycode: Key.Up } => -1,
        InputEventKey { Pressed: true, Keycode: Key.Down } => 1,
        InputEventKey { Pressed: true, Keycode: Key.Pageup } => -10,
        InputEventKey { Pressed: true, Keycode: Key.Pagedown } => 10,
        InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp } => -3,
        InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown } => 3,
        InputEventJoypadButton { Pressed: true, ButtonIndex: JoyButton.DpadUp } => -1,
        InputEventJoypadButton { Pressed: true, ButtonIndex: JoyButton.DpadDown } => 1,
        InputEventKey { Keycode: Key.Up or Key.Down or Key.Pageup or Key.Pagedown } => 0,   // releases
        InputEventMouseButton { ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } => 0,
        _ => null,
    };

    void QueueKey(float at, Key key, float hold = 0.1f)
    {
        _keyQueue.Add((at, key, true));
        _keyQueue.Add((at + hold, key, false));
    }

    void EnterCountdown()
    {
        _sessionLapArchives.Clear(); _sessionLapOrder.Clear(); _sessionLapSequence = 0;
        _replayListTask = null; _replayInfos.Clear(); _replayKnownLaps = -1;
        _sessionBest = -1f;      // a run's start or a restart opens a new session; a norm advancing, a pause or a viewer visit never reaches here
        StakhanovSessionReset();
        _state = State.Countdown;
        _hud.Mode = HudMode.Countdown;
        _startRevLeft = !InLanRace && !InSchool ? StartRevDuration : 0;
        _hud.ReserveStartMessages(8.5f + _startRevLeft);
        _hud.SetPauseNorms(false);
        _hud.TitleNotice = null;     // shown on the title until the first race
        _radio.Stop();
        AudioMix.MuteSfx(false);
        _carRadio.Suspend(false);
        _countdown = 3f;
        _falseStart = false;
        _startClutchAccepted = false;
        _pace.Reset();
        _timer.Stop();
        _ghostVis.Visible = false;
        _snd.Volume = 0.8f;
        LabRunStarts();      // RaceScene.Lab.cs: a lab run or a normal one
        _lapPeak = _power;       // a restart keeps the chosen power
        LapFuelsReset();
        if (!_planRestart) DayNightNewRun();     // a restart request keeps the time of day
        ApplyTrainingPower();
        PlaceAtStart();
    }

    /// <summary>Engine power (Stakhanov: the chosen power) and the run's record set (best lap).</summary>
    void ApplyTrainingPower()
    {
        ApplyFuelEngine();
        if (_powerTape?.Driven == true)
        { _timer.BestLap = _bestLaps[RecordSet]; return; }
        // Stakhanov: the chosen power; the rally: the fuel's (50, 100 or 200 %), or OCTANE BOOSTER's 3.5 times that while a golden can lasts.
        bool boost = !StakhanovRules && _boostLeft > 0;
        FuelGrade? tg = _run is RunMode.Training or RunMode.Lan ? Training.GradeOf(_power) : null;      // Stakhanov laps: the four powers are the four fuels
        _hud.PowerLabel = tg is FuelGrade g0 ? FuelGrades.Names[(int)g0] : "";
        int pct = tg is FuelGrade g1 ? FuelGrades.PhysicsPercent(g1) : StakhanovRules ? _power : boost ? BoostPercent : RallyPercent;
        _car.PowerScale = pct / 100f * EnginePower;
        _car.TurboGain = boost ? 0f : _run == RunMode.Rally ? FuelGrades.TurboGainFor(_rallyFuel) : tg is FuelGrade g2 ? FuelGrades.TurboGainFor(g2) : 0f;      // (OCTANE BOOSTER's nitro takes the place of the turbo while it lasts)
        _car.Overdrive = StakhanovRules || boost ? Training.Overdrive(pct) : 0f;
        _cam.MaxLead = _car.Overdrive > 0 ? 8f : float.PositiveInfinity;    // keeps the car on screen at 380 km/h
        _carVis.Golden = boost;   // LAN: every car keeps its own colour
        _timer.BestLap = _bestLaps[RecordSet];
    }

    FuelGrade? _engineFuelApplied;

    /// <summary>The fuel whose engine the car has now: the rally's, or (in a replay, the title's demonstration) the one the recorded lap was driven on; petrol otherwise.</summary>
    FuelGrade EngineFuel => CarTourActive ? _carTourFuel : (_run is RunMode.Training or RunMode.Lan) && !Replaying && !ViewerMode ? Training.GradeOf(_power) ?? FuelGrade.Petrol : Replaying || ViewerMode ? ViewerFuelNow() ?? TitleSet switch { 2 => FuelGrade.Diesel, 3 => FuelGrade.Rocket, 4 => FuelGrade.Sputnik, _ => FuelGrade.Petrol } : _run == RunMode.Rally ? _rallyFuel : FuelGrade.Petrol;

    /// <summary>The car's engine for the fuel (the diesel's, FuelGrades.ApplyEngine); a lab run's sliders are the owner's, a lab diesel's come from the LAB preset.</summary>
    static readonly Color FuelOlive = new(0.33f, 0.37f, 0.2f), FuelRed = new(0.78f, 0.05f, 0.05f), FuelWhite = new(0.93f, 0.94f, 0.95f);      // diesel: the duty officer's UAZ olive (KozlikPatrol)

    void ApplyFuelEngine()
    {
        _engineFuelApplied = EngineFuel;
        int wing = EngineFuel switch { FuelGrade.Diesel => 3, FuelGrade.Rocket => 1, FuelGrade.Sputnik => 2, _ => 0 };      // the rear wing: the duck-tail lip, low, middle, high
        if (_carVis != null) FuelGrades.ApplyVisual(_carVis, EngineFuel);
        if (_ghostVis != null) _ghostVis.Wing = wing;
        if (_carVis != null) _carVis.FuelPaint = _run == RunMode.Lan ? null : EngineFuel switch { FuelGrade.Diesel => FuelOlive, FuelGrade.Rocket => FuelRed, FuelGrade.Sputnik => FuelWhite, _ => null };
        if (_params != null && !_labRun) FuelGrades.ApplyEngine(_params, EngineFuel);
    }

    /// <summary>A run started from the title (or --start): Stakhanov laps begin on the CHOOSE FUEL card's fuel (start), else at 100 % (or --power).</summary>
    void NewRunPower(int? start = null)
    {
        _power = _lapPeak = _run == RunMode.Training && start is int chosen ? chosen : _powerStart;
        if (_run == RunMode.Training && Training.GradeOf(_power) is FuelGrade sg && FuelLocked(sg)) _power = _lapPeak = Training.PercentForKey((int)OpenFuel(sg) + 1);      // a closed fuel does not start the laps
        _heroNodded = false;
    }

    /// <summary>Number keys 1-9 (also the keypad) as 1..9, else 0.</summary>
    static int PowerKey(InputEventKey k) =>
        k.PhysicalKeycode is >= Key.Key1 and <= Key.Key9 ? (int)(k.PhysicalKeycode - Key.Key0)
        : k.Keycode is >= Key.Kp1 and <= Key.Kp9 ? (int)(k.Keycode - Key.Kp0) : 0;

    /// <summary>
    /// Stakhanov laps, keys 1-4: the power changes at once, the lap's peak rises with it and the
    /// confirmation shows even with the comments off. The first switch to 200 % or more earns the nod.
    /// </summary>
    void SetPower(int pct)
    {
        if (_run == RunMode.Training && Training.GradeOf(pct) is FuelGrade lg && FuelLocked(lg))
        {
            _hud.ShowAlways(FuelGrades.Names[(int)lg] + " IS CLOSED", $"A SILVER ON {TrackName(TrainingTrack)} IN THE RALLY ON {FuelGrades.Names[(int)FuelKey(lg).Value]} OPENS IT", 3f, Hud.Red);
            return;
        }
        _power = pct;
        _lapPeak = Math.Max(_lapPeak, pct);
        LapFuelsNote();
        ApplyTrainingPower();
        _hud.ShowAlways(_run == RunMode.Training ? Training.FuelName(pct) : $"{Training.FuelName(pct)} {pct}%", _run == RunMode.Training && Training.GradeOf(pct) is FuelGrade jg ? FuelGrades.CardLine((int)jg) : Training.Joke(pct), 2f, pct < 100 ? Hud.Red : pct == 100 ? Hud.Gold : Hud.Green);
        if (Training.Hero(pct) && !_heroNodded)
        {
            _heroNodded = true;
            _kgb.Say("ГЕРОЙ ТРУДА!", approve: true, duration: 4f);    // hero of labour: the supervisor nods
        }
    }

    void StartRacing()
    {
        if (_radioAt > 0) { _carRadio.Select(_radioAt - 1); _radioAt = -1; }
        _state = State.Racing;
        _car.Neutral = false;     // the driver's clutch stays held until they release it
        if (_launchKmh > 0) _car.Vel = _car.Forward * (_launchKmh / 3.6f);
        _timer.Start(keepLaps: _norms.Enabled && _runClock > 0);    // a restart request: the run goes on
        if (_falseStart)
        {
            _timer.Void();
            int startMetre = _track.NearestCenter(new Vector2(_car.Pos.X, _car.Pos.Z), out _);
            if (startMetre < _track.Center.Length / 2) _timer.PutAt(startMetre);
            _hud.Alert("FALSE START - LAP VOID", "COMPLETE THIS LAP TO TRY AGAIN", 4f, Hud.Red, urgent: true);
        }
        _lapClock = 0;
        LapFuelsReset();
        _lapPad = false;
        _lapBoost = _boostLeft > 0;
        _rec.Clear();
        TapeBegin();
        _rec.Record(0, _car, 0, 0, 0, _dayNight.Active ? _dayNight.Clock : -1, damage: _carVis.Damage);
        if (_babushkaHit != 0) _babushkas?.AimCar(_car, 0, _babushkaHit / 3.6f, _babushkaDist);
        StartRevengeTests();
        StartFuelTests();
        StartDoseTests();
        if (_flipTest)
        {
            _car.Rot = new Basis(_car.Forward, MathF.PI) * _car.Rot;
            _car.Pos += Vector3.Up * 1.2f;
        }
        if (_norms.OnLastLap) _hud.Show("LAST LAP FOR THIS NORM", "THE TRUCK IS WAITING", 3f, Hud.Red, Prio.Critical);
        if (InLanRace) LanStarted();
    }

    void OnLap(float time, bool record)
    {
        DayNightLap();
        DoseAtLine();
        if (InLanRace) { LanLap(time); return; }     // a LAN race never touches the records
        _kgb.LogLap(time, record);
        if (OS.HasEnvironment("LAPDBG")) GD.Print($"lap {time:F2} s, flicks {_car.Flicks}{(StakhanovRules ? $" at {_lapPeak}%" : "")}, driven with {(_lapPad ? "a pad" : "the keys")}");
        // Each run has its own records: a Stakhanov lap at any power is ranked at the highest power
        // used during it; the next lap starts from the power chosen now.
        bool fake = _fakeLap;
        StakhanovSessionLap(time, _timer.LastLapRanked, false, fake);
        TapeLapReport(time, record, fake);
        if (!fake) TapeSaveSessionLap(time);
        if (record && !fake)     // a test lap (--lap-done-at) never becomes the ghost
        {
            _best = _rec.CopyWithTime(time);
            if (!NoRecords) TapeSaveRecord(time);      // the ghost, with its world tape as a pair when one was recorded (RaceScene.Tape.cs)
            else _tapes[RecordSet] = new TapeSlot { Ghost = _best };
        }
        _bestLaps[RecordSet] = _timer.BestLap;
        if (_run == RunMode.Rally && !fake && _timer.LastLapRanked && time > 0 && (_sessionBest < 0 || time < _sessionBest)) _sessionBest = time;      // (the same validity as a record lap: not void, not unranked, not a test lap)
        if (!StakhanovRules && !fake && _timer.LastLapRanked && (_sessionGhost == null || time < _sessionGhost.LapTime))
        {
            _sessionGhost = _rec.CopyWithTime(time);       // the session's best lap: the next laps race it
        }
        LapArchiveCapture(time, record && !fake ? LapKind.Record : LapKind.Normal, _timer.LastLapRanked, fake);
        _rec.Clear();
        TapeBegin();
        _rec.Record(0, _car, 0, 0, 0, _dayNight.Active ? _dayNight.Clock : -1, damage: _carVis.Damage);
        _lapClock = 0;
        LapFuelsReset();
        string lap = "LAP " + LapTimer.Format(time) + (StakhanovRules ? "  " + (_run == RunMode.Training ? Training.FuelShort(_lapPeak) : _lapPeak + "%") : "");
        // A top-10 lap by a human driver stops the game for the name; the autopilot is not asked.
        int rank = _timer.LastLapRanked && (!_autodrive || fake) ? _board.RankFor(time, _lapPeak) : 0;
        if (rank > 0)
            BoardLap(time, record);      // under the name given at the start (RaceScene.Name.cs)
        else if (record)
            _hud.Show(Slogans.Pick(Slogans.Record), lap, 3.5f, Hud.Gold, plain: "RECORD LAP", plainSub: lap);
        else if (!CardsOn)
            _hud.Show(Slogans.Pick(Slogans.Lap), lap, 3f, Hud.Paper, StakhanovRules ? Prio.Important : Prio.Nice, plain: "LAP DONE", plainSub: lap);   // Stakhanov: the lap time is the point
        _lapPeak = _power;
        _lapPad = false;
        _lapBoost = _boostLeft > 0;
        PlanLap(time, _timer.LastLapRanked);
        TapeLapDone();
    }

    void OnVoidLap(float time)
    {
        DayNightLap();
        DoseAtLine();
        if (InLanRace) { LanLap(time); return; }
        // A rescued or false-start lap still contains useful trailer action.
        // Keep it only in an opt-in session, never in the player's records.
        TapeSaveSessionLap(time, ranked: false);
        StakhanovSessionLap(time, false, true, _fakeLap);
        _kgb.LogLap();
        if (!CardsOn) _hud.Show("LAP NOT COUNTED", "VOID LAP " + LapTimer.Format(time), 3f, Hud.Red);
        _lapPeak = _power;
        _lapPad = false;
        _lapBoost = _boostLeft > 0;
        PlanLap(time, false);
        LapArchiveCapture(time, LapKind.Void, false, false);
        _rec.Clear();     // a rescued lap never becomes the ghost
        TapeBegin();
        _lapClock = 0;
        LapFuelsReset();
        if (_tapeSessionLapsDir != null) TapeLapDone();
    }

    /// <summary>Seconds on the roof before the rescue is done for the driver.</summary>
    const float AutoRescue = 5f;
    bool _roofHinted;

    /// <summary>R while racing: back onto the road, but the lap no longer counts.</summary>
    void CallMechanic()
    {
        _scRescued = true;       // the driving school's R lesson
        if (InLanRace)
        {
            _net.RequestRescue();         // host retains the validated checkpoint progress
            _hud.Show("BACK ON THE ROAD", "THE CLOCK KEEPS RUNNING", 2.5f, Hud.Red, plain: "BACK ON THE ROAD");
            return;
        }
        Rescue(0);
        _timer.Void();
        _hud.Show("LAP VOID", "BACK ON THE ROAD", 2.5f, Hud.Red, Prio.Critical);
    }

    float _wrongWay;      // seconds the car has been driving the course backwards (>0) or forwards again (<0)

    /// <summary>
    /// Driving the course the wrong way round: after a moment the HUD says so, big and blinking, until
    /// the car runs the right way again for a moment. A spin or a short reverse does not count.
    /// </summary>
    void WrongWayTick(float dt)
    {
        int i = _timer.Progress;
        float along = new Vector2(_car.Vel.X, _car.Vel.Z).Dot(_track.CenterDir(i));
        if (along < -3f) _wrongWay = Math.Max(0f, _wrongWay) + dt;
        else if (along > 2f) _wrongWay = Math.Min(0f, _wrongWay) - dt;
        bool on = _hud.WrongWay ? _wrongWay > -0.8f : _wrongWay > 1.5f;
        if (on != _hud.WrongWay && OS.HasEnvironment("LAPDBG")) GD.Print($"wrong way {(on ? "ON" : "off")} at {_clock:F1} s, road metre {i}");
        _hud.WrongWay = on;
    }

    void Rescue(float penalty)
    {
        _rescue.Reset();
        // Back onto the stretch of road the car was on (followed from the lap's progress, so a
        // hairpin's other leg or a nearby stretch never steals it), a few metres back.
        int i = _timer.RescueMetre(new Vector2(_car.Pos.X, _car.Pos.Z));
        if (OS.HasEnvironment("RESCUEDBG")) GD.Print($"rescue: car at ({_car.Pos.X:F0},{_car.Pos.Z:F0}), road metre {i} (global nearest {_track.NearestCenter(new Vector2(_car.Pos.X, _car.Pos.Z), out _)}, progress {_timer.Progress})");
        i = (i - 6 + _track.Center.Length) % _track.Center.Length;
        _car.Reset(_track.CenterPos(i) + Vector3.Up * 0.7f, TrackData.YawFor(_track.CenterDir(i)));
        if (penalty > 0)
        {
            _timer.AddPenalty(penalty);
            _hud.Show(Slogans.Pick(Slogans.Flip), $"+{penalty:0} SEC", 3f, Hud.Red, plain: "ROLLED OVER", plainSub: $"+{penalty:0} SEC");
        }
    }

    // ------------------------------------------------------------ simulation

    public override void _PhysicsProcess(double delta)
    {
        if(LanPlaybackSource!=null) return;
        if (_booting) return;
        if (!ViewerMode) TraceTick();      // (a viewer must not reopen and truncate the live scene's WORLDTRACE)
        float dt = (float)delta;
        if (ViewerMode && !ViewerStep(ref dt)) return;      // RaceScene.Viewer.cs: the viewer's own pause and speed
        if (_state is State.Paused || SchoolFrozen) return;
        TapeTestTick(dt);
        if (WaitForStartClutch()) return;
        if (LanGoDue()) StartRacing();
        TrackPreviewDrive(dt);

        if (Replaying)
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
            if (EngineFuel == FuelGrade.Diesel) _fx.DieselSoot(_car, dt);
            if (_tapeObsCount != null) _tapeReplayQueries += _tapeObsCount.Queries - queries0;
            return;
        }

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
                if (_clutchKickAt > 0)
                {
                    _car.SetControls(true, false, 0f, false);
                    if (OS.HasEnvironment("CLUTCHDBG") && Math.Abs(_raceClock - _clutchKickAt) < 0.6f)
                        GD.Print($"clutch t={_raceClock - _clutchKickAt:F3} in={_car.Clutch} rpm={_car.Rpm:F0} gear={_car.Gear} v={_car.ForwardSpeed * 3.6f:F1} spin={(_car.Wheels[2].Spinning ? 1 : 0)}{(_car.Wheels[3].Spinning ? 1 : 0)} lim={_car.Limiter}");
                }
                else if (_donut) _car.SetControls(true, false, 1f, false);
                else if (_pulse) _car.SetControls(_raceClock % 2f < 1.2f, false, 0f, false);
                else if (_fenceTest) _car.SetControls(true, false, 0f, false);
                else if (_crashTest) CrashDrive(dt);
                else if (_canisterTest > 0) _car.SetControls(_car.ForwardSpeed * 3.6f < _canisterTest, false, 0f, false);
                else if (_atDepot && _autodrive && _raceClock < 9f) _car.SetControls(false, false, 0f, false);   // --at-depot: stand and fill first
                else if (_babushkaHit != 0) BabushkaCruise();
                else if (_stomp) _car.SetControls(_raceClock < 3.5f, _raceClock >= 3.5f && _car.ForwardSpeed > 1.5f, 0f, false);
                else if (_autodrive && !_padAuto) { _ap.Drive(_car, dt); if (_missileEvade > 0 && _jets != null) MissileEvade(); }
                else if (ctl.Pad || _padAuto)
                {
                    _keyThrottle = !(ctl.PadThrottle > 0f && !Input.IsActionPressed("accelerate"));    // the trigger's own throttle: no keys compensation
                    // A pad drives (alone or with the keys): analog pedals and steering (DriverInput).
                    if (_padAuto) PadAutoDrive(dt);
                    _lapPad = true;
                    if (InLanRace) _lanPad = true;
                    _car.SetControls(ctl.Throttle, ctl.Brake, ctl.Steer, ctl.Handbrake, ctl.PadSteer);
                }
                else
                {
                    float steer = Input.GetActionStrength("steer_right") - Input.GetActionStrength("steer_left");
                    _car.SetControls(Input.IsActionPressed("accelerate"), Input.IsActionPressed("brake"),
                        steer, Input.IsActionPressed("handbrake"));
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
        if(InLanRace) {LanAuthorityPhysics(dt); return;}
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

        _wood.Tick(dt);
        _scenery.Tick(dt, _car, !_rescue.Active);
        _cones?.Tick(dt, _rescue.Active ? null : _car);
        TickActors(dt, replay: false, afterCar: false);
        if (_busSwerveAt > 0 && _clock >= _busSwerveAt) { _busSwerveAt = -1; _bus?.ForceSwerve(); }
        TankCrush();

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
        if (EngineFuel == FuelGrade.Diesel) _fx.DieselSoot(_car, dt);


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

    /// <summary>Babushka hits: a soft knock in the car; one in the lens cracks the screen.</summary>
    void BabushkaFeedback()
    {
        if (_babushkas == null) return;
        if (Replaying && _tapePlayer?.Covers("Babushkas:" + _babushkas.Name) == true) return;
        if (_babushkas.HitsThisTick > 0)
        {
            float k = Math.Clamp(_babushkas.HitSpeedThisTick / 28f, 0f, 1f);
            _snd.BodyHit(0.35f + 0.65f * k);
            _snd.Thump(0.3f + 0.4f * k);
            _snd.Impact(2f + 3f * k);
            _cam.Shake(0.15f + 0.35f * k);
            Rumb(0.4f, 0.2f + 0.4f * k, 0.12f + 0.08f * k, "babushka");
        }
        if (_babushkas.LensHitThisTick)
        {
            _snd.Thump(1f);
            _snd.Impact(6f);
            _cam.Shake(1.2f);
            Rumb(0.5f, 0.7f, 0.3f, "babushka in the lens");
            _hud.LensSplat(_babushkas.LensScarf, Babushkas.LensHold, _trackId=="bonus", _babushkas.LensSeedThisTick);
        }
    }

    /// <summary>
    /// Wooden fences: the car crashing through one, or the drunk tractorist ploughing one down
    /// within earshot.
    /// </summary>
    void FenceFeedback()
    {
        if (Replaying && _tapeWood) return;
        if (_tractor != null)
        {
            var tv = _tractor.Forward2D * _tractor.Speed;
            if (_wood.Smash(_tractor.Position2D, 1.5f, new Vector3(tv.X, 0, tv.Y)) > 0)
            {
                float d = _tractor.Position2D.DistanceTo(new Vector2(_car.Pos.X, _car.Pos.Z));
                if (d < 60f) _snd.Crack(0.5f * (1f - d / 60f) + 0.1f);
            }
        }
        if (_bus is { FencesBrokenThisTick: > 0 })
        {
            float d = _bus.Position2D.DistanceTo(new Vector2(_car.Pos.X, _car.Pos.Z));
            if (d < 60f) _snd.Crack(0.5f * (1f - d / 60f) + 0.1f);
        }
        if (_wood.CrackStrength <= 0) return;
        if (OS.HasEnvironment("FENCEDBG")) GD.Print($"fence crack {_wood.CrackStrength:F2} at {_clock:F2} s");
        _snd.Crack(_wood.CrackStrength);
        _cam.Shake(0.25f + 0.35f * _wood.CrackStrength);
        Rumb(0.25f + 0.25f * _wood.CrackStrength, 0.3f * _wood.CrackStrength, 0.1f, "fence");
        if (_state == State.Racing && _fenceCd <= 0)
        {
            _hud.Show(Slogans.Pick(Slogans.Fence), "THE CHAIRMAN WILL HEAR OF THIS", 2.5f, Hud.Red, Prio.Nice);
            _fenceCd = 10f;
        }
    }

    /// <summary>A tree or pole felled by something else (a tank, a shell): its crack, if within earshot.</summary>
    void RemoteCrack(Vector3 p)
    {
        float d = p.DistanceTo(_car.Pos);
        if (d < 80f) _snd.Crack(0.6f * (1f - d / 80f) + 0.1f);
    }

    /// <summary>Tanks push over whatever tree or pole their hull touches.</summary>
    void TankCrush()
    {
        if (_tanks == null || !_tanks.Visible) return;
        foreach (int i in _tanks.HullObstacles)
        {
            var hull = _track.Obstacles[i];
            if (_scenery.Crush(hull) > 0) RemoteCrack(new Vector3(hull.Center.X, _car.Pos.Y, hull.Center.Y));
        }
    }

    /// <summary>Trees, poles and walls the car has knocked down: the crack, the thud, a word from the Ministry.</summary>
    void SceneryFeedback()
    {
        if (_scenery.KnockedThisTick > 0 && _knockCd <= 0)
        {
            // Any figure the bumper sends flying (the militia, the officers, the workers, the men at the stop, the drunks): the same dull tump as a babushka.
            float k = Math.Clamp(_scenery.KnockSpeed / 25f, 0.1f, 1f);
            _snd.BodyHit(0.3f + 0.6f * k);
            _cam.Shake(0.1f + 0.25f * k);
            Rumb(0.3f, 0.15f + 0.3f * k, 0.1f, "knocked over");
            _knockCd = 0.2f;
        }
        _knockCd -= 1f / 60f;
        if (_scenery.ThumpStrength > 0)
        {
            if (OS.HasEnvironment("DAMAGEDBG")) GD.Print($"scenery thump {_scenery.ThumpStrength:F2} at {_clock:F2} s, car {_car.Speed * 3.6f:F0} km/h");
            _snd.Thump(_scenery.ThumpStrength);
            _cam.Shake(0.1f + 0.2f * _scenery.ThumpStrength);
            Rumb(0.3f, 0.4f * _scenery.ThumpStrength, 0.1f, "scenery thump");
        }
        if (_scenery.BrokeKind is not { } kind) return;
        if (OS.HasEnvironment("DAMAGEDBG")) GD.Print($"broke {kind} ({_scenery.CrackStrength:F2}) at {_clock:F2} s, impact {_maxImpact:F1}");
        // The contact voice already contains this fracture; keep separate sounds
        // for other scenery activity without adding a second main body hit.
        if (!(_impactBroke && _maxImpact > 3))
        {
            if (kind == "wall") _snd.Thump(0.8f);
            else _snd.Crack(_scenery.CrackStrength);
        }
        _cam.Shake(0.3f + 0.4f * _scenery.CrackStrength);
        if (_state == State.Racing && _breakCd <= 0)
        {
            string[] words = kind switch { "pole" => Slogans.PoleDown, "wall" => Slogans.WallDown, "birch" => Slogans.BirchDown, _ => Slogans.TreeDown };
            _hud.Show(Slogans.Pick(words), null, 2.5f, Hud.Red, Prio.Nice);
            _breakCd = 8f;
            _crashCd = Math.Max(_crashCd, 4f);   // not the generic crash line on top
        }
    }

    /// <summary>
    /// The record lap only stores how hard each hit was, not where: a rolled car takes it on the
    /// roof, otherwise the end it was driving with.
    /// </summary>
    void ReplayDamage(float impact)
    {
        if (impact <= 0 || _carVis.Damage == null) return;
        if (_car.Up.Y < 0.3f) _carVis.Damage.Hit(impact, new Vector3(0, 0.9f, -0.2f), Vector3.Down, _car, _fx);
        else if (_car.ForwardSpeed >= 0) _carVis.Damage.Hit(impact, new Vector3(0, -0.05f, -2.05f), Vector3.Back, _car, _fx);
        else _carVis.Damage.Hit(impact, new Vector3(0, -0.05f, 2.05f), Vector3.Forward, _car, _fx);
    }

    /// <summary>
    /// Test (--crash): puts the car 40 m north of the nearest stone wall (or --crash-target: pole,
    /// tree, sapling: 60 m out, 30 m with --launch, from any side) with a clear run-up, facing it,
    /// so the crushed front and windscreen (or what breaks) show.
    /// </summary>
    void AimAtObstacle()
    {
        bool wall = _crashKind == "wall";
        float runUp = wall ? 40f : _launchKmh > 0 ? 30f : 60f;
        float breakAt = _crashKind switch { "pole" => Breakables.PoleBreak, "tree" => Breakables.BigTreeBreak, "sapling" => Breakables.SmallTreeBreak, _ => Breakables.WallBreak };
        var start = new Vector2(_car.Pos.X, _car.Pos.Z);
        Obstacle? best = null;
        Vector2 bestDir = Vector2.Zero;
        foreach (var o in _track.Obstacles)
        {
            if (o.BreakAt != breakAt || o.Velocity != Vector2.Zero) continue;
            if (wall && Math.Abs(o.Radius - 0.45f) > 0.01f) continue;       // stone walls: low, the car stays in view
            if (best is { } b && b.Center.DistanceTo(start) <= o.Center.DistanceTo(start)) continue;
            // The classic wall test from the north only; the rest from whichever side is clear, north first.
            for (int k = 0; k < (wall ? 1 : 8); k++)
            {
                float ang = k * MathF.PI / 4f;
                var dir = new Vector2(MathF.Sin(ang), MathF.Cos(ang));      // travel direction, (0, 1) = south
                Vector2 from = o.Center - dir * runUp, to = o.Center - dir * (o.Radius + 2.5f);
                bool clear = from.X > 10 && from.Y > 10 && from.X < _track.WorldSize - 10 && from.Y < _track.WorldSize - 10;
                for (float d = 0; clear && d < runUp - 2f; d += 2f)
                {
                    Vector2 p = from + dir * d, p2 = p + dir * 2f;
                    clear = _track.Sample(p.X, p.Y).Surface is not (SurfaceType.Field or SurfaceType.Mud or SurfaceType.Puddle or SurfaceType.Pothole or SurfaceType.Water or SurfaceType.Shallow)
                        && (wall || Math.Abs(_track.MeshHeight(p2.X, p2.Y) - _track.MeshHeight(p.X, p.Y)) < 0.25f);   // no ditch or bank on the way
                }
                foreach (var q in _track.Obstacles)
                {
                    if (!clear) break;
                    Vector2 ab = to - from;
                    float t = Math.Clamp((q.Center - from).Dot(ab) / ab.LengthSquared(), 0f, 1f);
                    // Keep well clear of roofs and trees too, so the camera sees the car.
                    if (q.Center.DistanceTo(o.Center) > 3f && q.Center.DistanceTo(from + ab * t) < q.Radius + (wall ? 5f : 2f)) clear = false;
                }
                foreach (var run in _track.Fences)                                // no fence in the way either
                    foreach (var fp in run.Points)
                    {
                        if (!clear || wall) break;
                        Vector2 ab = to - from, q2 = new(fp.X, fp.Z);
                        float t = Math.Clamp((q2 - from).Dot(ab) / ab.LengthSquared(), 0f, 1f);
                        if (q2.DistanceTo(from + ab * t) < 3f) clear = false;
                    }
                if (clear) { best = o; bestDir = dir; break; }
            }
        }
        if (best is not { } target) return;
        Vector2 at = target.Center - bestDir * runUp;
        _crashTarget = target.Center;
        _car.Reset(new Vector3(at.X, _track.MeshHeight(at.X, at.Y) + 0.7f, at.Y), TrackData.YawFor(bestDir));
        if (OS.HasEnvironment("DAMAGEDBG")) GD.Print($"crash test: {_crashKind} r {target.Radius:F2} at {target.Center}, from {at}");
    }

    /// <summary>Test (--babushka-hit): cruise at the test speed, steering at babushka 0 until 60 m out (before she can hear a horn), then straight on.</summary>
    void BabushkaCruise()
    {
        float steer = 0;
        if (_babushkas != null)
        {
            Vector2 to = _babushkas.WalkerPosition(0) - new Vector2(_car.Pos.X, _car.Pos.Z);
            if (to.Length() > 60f) steer = Math.Clamp(new Vector2(_car.Forward.X, _car.Forward.Z).AngleTo(to) * 2f, -1f, 1f);
        }
        _car.SetControls(_car.ForwardSpeed * 3.6f < Math.Abs(_babushkaHit), false, steer, false);
    }

    /// <summary>Test (--crash): flat out at the target, back off after each hit (or break) and ram it again.</summary>
    void CrashDrive(float dt)
    {
        _crashBack -= dt;
        if ((_maxImpact > 4f || _scenery.BrokeKind != null) && _crashBack <= 0) { _crashBack = 1.6f; _crashRams--; }
        Vector2 to = _crashTarget - new Vector2(_car.Pos.X, _car.Pos.Z);
        var fwd = new Vector2(_car.Forward.X, _car.Forward.Z);
        float steer = Math.Clamp(fwd.AngleTo(to) * 2f, -1f, 1f);
        if (_crashRams <= 0) _car.SetControls(false, _car.ForwardSpeed > 1.5f, 0f, true);
        else if (_crashBack > 0) _car.SetControls(false, true, 0f, false);
        else _car.SetControls(_crashKmh <= 0 || _car.ForwardSpeed * 3.6f < _crashKmh, false, steer, false);
    }

    /// <summary>Potholes, slab joints and crashes: thumps, bangs and camera jolts.</summary>
    float _bankThumpCd;

    /// <summary>
    /// In a snow bank: powder sprays off the bank side as long as the car ploughs, a burst and a
    /// soft thump when it runs in (the dents and the shake come from a hard hit's impact share).
    /// </summary>
    void SnowbankFeedback()
    {
        float hit = _car.SnowbankHit, plough = _car.SnowbankPlough;
        SiberiaBankFeedback(plough);
        _car.SnowbankHit = _car.SnowbankPlough = 0;
        float dt = (float)GetPhysicsProcessDeltaTime();
        _bankThumpCd -= dt;
        if (plough > 0.3f)
        {
            Vector3 away = _car.SnowbankPoint - _car.Pos;
            away.Y = 0;
            _fx.SnowSpray(_car.SnowbankPoint, plough, _car.Vel, away.LengthSquared() > 1e-4f ? away.Normalized() : Vector3.Zero, dt);
        }
        if (hit < 1.5f || _bankThumpCd > 0) return;
        _bankThumpCd = 0.35f;
        _fx.SnowPuff(_car.SnowbankPoint, hit, _car.Vel);
        _snd.Thump(Math.Clamp(0.2f + hit * 0.05f, 0f, 0.8f));
    }

    float _knockCd;
    bool _landedNow;
    float _landCd, _fallBefore;

    /// <summary>
    /// The car comes down after a jump (live: the air time before it met the ground, and the spike in the springs): the mass should be felt. A deep boom
    /// and the body's crunch, the camera slammed, the pad's weight, a ring of dust or snow and clods thrown up from the wheels.
    /// </summary>
    void LandingFeedback(float suspHit)
    {
        // A real jump only: racing (not the start's countdown, a rescue or a placement), a good second in the air and coming down fast. The straight's bumps are
        // a few tenths of a second and a few m/s.
        if (_state != State.Racing || _rescue.Active) return;
        if (!(_airBefore > 0.6f && _car.AirTime == 0f && _fallBefore > 3f)) return;
        float kAir = Math.Clamp((_airBefore - 0.6f) / 1.0f, 0f, 1f), kSus = Math.Clamp((suspHit - 3f) / 10f, 0f, 1f);
        float k = Math.Max(kAir, kSus);
        if (OS.HasEnvironment("LANDDBG")) GD.Print($"landing: air {_airBefore:F2} s, fall {_fallBefore:F1} m/s, spring spike {suspHit:F2}, strength {k:F2}, {_car.Speed * 3.6f:F0} km/h");
        Landed(k);
    }

    /// <summary>The landing's sound, camera, dust and pad, once.</summary>
    void Landed(float k)
    {
        if (_landCd > 0 || k <= 0.05f) return;
        _landCd = 0.5f; _landedNow = true;
        _snd.Landing(k);
        _cam.Shake(0.35f + 0.65f * k);
        _fx.Landing(_car, k);
    }

    void HitFeedback(float dt, float pothole, float suspHit)
    {
        _thumpCd -= dt;
        _landCd -= dt;
        if (pothole > 0f)
        {
            // Pothole: a deep thump with a rattle from the Pyatyorka's front end, camera jolts.
            _snd.Thump(Math.Clamp(0.5f + pothole * 0.4f, 0f, 1f));
            _snd.Impact(2f + pothole * 4f);
            _cam.Shake(Math.Min(0.8f, 0.15f + pothole * 0.3f));
            Rumb(Math.Min(0.8f, 0.35f + pothole * 0.3f), Math.Min(0.4f, pothole * 0.15f), 0.1f, $"pothole {pothole:F2}");
            _thumpCd = 0.1f;
        }
        else if (Replaying && !_landedNow && suspHit >= 8f && _landCd <= 0f)
        {
            // A ghost's replay has no air time of its own: only a very hard spike in the springs is a landing (the bumps of the road are far below it).
            Landed(Math.Clamp((suspHit - 8f) / 10f + 0.2f, 0.2f, 1f));
        }
        else if (suspHit > 0f && _thumpCd <= 0f)
        {
            // Slab joints and small bumps: just a muffled tump through the springs.
            _snd.Thump(Math.Clamp(0.15f + suspHit * 0.12f, 0f, 0.45f));
            Rumb(Math.Clamp(0.12f + suspHit * 0.06f, 0f, 0.3f), 0f, 0.05f, $"bump {suspHit:F2}");
            _thumpCd = 0.09f;
        }

        if (_maxImpact > 3f)
        {
            var response = CollisionResponse.Of(_maxImpact, _params.Mass);
            _collisionQuiet = Math.Max(_collisionQuiet, response.Duration + .15f);
            _snd.ClearDrivetrain(); _snd.ClearBackfire();
            _snd.Collision(_maxImpact, _params.Mass, _impactMaterial, _impactBroke);
            if (OS.HasEnvironment("DAMAGEDBG")) GD.Print($"feedback: collision {_maxImpact:F2} m/s at {(Replaying ? _replayClock : _raceClock):F3}");
            if (!ViewerMode) AudioMix.Duck(AudioMix.Diesel, 2f + 5f * response.Weight, response.Duration);      // (a viewer must not duck the shared buses)
            _cam.Collision(_maxImpact, _params.Mass, _car.Pos);
            float k = response.Weight;
            Rumb(0.3f + 0.4f * k, 0.25f + 0.75f * k, 0.1f + 0.25f * k, $"crash {_maxImpact:F1} m/s");
        }
    }

    /// <summary>
    /// Lifting off at high revs makes the tuned Pyatyorka crackle: 2-5 bangs with a flame and a
    /// small camera jolt each. Also works while revving on the start line.
    /// </summary>
    ulong _heardPowerUpshifts;

    void ClearCarFeedback()
    {
        _snd.ClearCollision(); _cam.ClearCollision(); _snd.ClearDrivetrain(); _snd.ClearBackfire();
        _collisionQuiet = 0; _pops = 0; _popTimer = 0; _prevThrottle = 0; _popArmed = false;
        _heardPowerUpshifts = _car?.PowerUpshifts ?? 0;
    }

    void UpdateBackfire(float dt)
    {
        float throttle = _car.Throttle, rpm = _car.Rpm;
        _collisionQuiet = Math.Max(0, _collisionQuiet - dt);
        if (_collisionQuiet > 0)
        {
            if (OS.HasEnvironment("DAMAGEDBG") && (_pops > 0 || _prevThrottle > .6f && throttle < .1f))
                GD.Print($"feedback: suppress exhaust burst during collision recovery at {(Replaying ? _replayClock : _raceClock):F3}");
            // A crash can throw the driver off the throttle at high revs. Do not
            // misread that recovery as an intentional 2–5-shot exhaust burst.
            _pops = 0; _popTimer = 0; _prevThrottle = throttle; _popArmed = false;
            _heardPowerUpshifts = _car.PowerUpshifts;
            return;
        }

        if (_heardPowerUpshifts != _car.PowerUpshifts)
        {
            _heardPowerUpshifts = _car.PowerUpshifts;
            if (!Replaying && _state == State.Racing && !InSchool)
            {
                float shiftStrength = _car.PowerUpshiftStrength;
                _snd.PowerShift(shiftStrength);
                _fx.ExhaustFlame(_car, shiftStrength * 0.7f);
                _cam.Shake(0.1f * shiftStrength);
            }
        }

        // Armed by a firm pedal at high revs, fired when the pedal comes off, however slowly (a metered lift or the engine brake): the old "0.6 to 0.1 in one tick" missed most of them.
        if (throttle > 0.6f && rpm > _params.Redline * 0.35f) _popArmed = true;      // (a share of the redline: the diesel and the tall gears reach 3200 only very fast)
        if (_popArmed && throttle < 0.3f)
        {
            _popArmed = false;
            if (rpm > _params.Redline * 0.3f && _pops == 0) PopBurst(rpm);
        }
        _prevThrottle = throttle;

        // The Sputnik's anti-lag: with the pedal up and the turbo still spinning or the revs high, the pipe keeps crackling, small bangs, now and then a bigger one with a flame.
        if (EngineFuel == FuelGrade.Sputnik && !Replaying && (_state == State.Racing || CarTourActive) && throttle < 0.15f && rpm > _params.Redline * 0.4f && _pops == 0)
        {
            _antiLagTimer -= dt;
            if (_antiLagTimer <= 0)
            {
                float load = Math.Clamp((rpm / _params.Redline - 0.4f) / 0.5f, 0f, 1f);
                _antiLagBeat = (_antiLagBeat + 1) % 5;      // the five-cylinder's beat: four quick ones and a longer breath
                bool big = _rng.Randf() < (_antiLagBeat == 4 ? 0.5f : 0.08f);
                float s = Math.Clamp(0.3f + 0.4f * load * _rng.RandfRange(0.5f, 1f), 0.3f, 0.7f);
                if (big) { _snd.Explosion(0.55f + 0.25f * load); _fx.ExhaustFlame(_car, 1f); _cam.Shake(0.05f * load); }
                else { _snd.Bang(s); if (_rng.Randf() < 0.5f) _fx.ExhaustFlame(_car, 0.5f); }
                _antiLagTimer = AntiLagBeats[_antiLagBeat] * (1.5f - 0.6f * load) * _rng.RandfRange(0.85f, 1.15f);
            }
        }
        else _antiLagTimer = 0.05f;

        if (_pops <= 0) { _popBoom = false; return; }
        if (throttle > 0.5f) { _pops = 0; _popBoom = false; return; }
        _popTimer -= dt;
        if (_popTimer > 0) return;
        float strength = _popStrength * _rng.RandfRange(0.6f, 1f);
        if (_popBoom) _snd.Explosion(strength); else _snd.Bang(strength);
        if (_popBoom) { _fx.ExhaustFlame(_car, 1f); _cam.Shake(0.12f * strength); }
        if (_pops == 1) _popBoom = false;
        if (OS.HasEnvironment("DAMAGEDBG")) GD.Print($"feedback: exhaust pop at {(Replaying ? _replayClock : _raceClock):F3}");
        _cam.Shake(0.06f * strength);
        _fx.ExhaustFlame(_car, strength);
        _pops--;
        _popTimer = _rng.RandfRange(0.06f, 0.22f);
    }

    bool _popArmed;
    float _antiLagTimer;
    int _antiLagBeat;
    static readonly float[] AntiLagBeats = { 0.06f, 0.06f, 0.07f, 0.09f, 0.26f };

    /// <summary>The SPUTNIK's wastegate: a puff of white from the thin pipe beside the fat one, as the throttle is lifted with the turbo spinning (the blow-off's sound is the engine's).</summary>
    void WastegatePuff()
    {
        float boost = Math.Clamp(_car.Boost, 0f, 1f);
        if (boost < 0.2f) return;
        Vector3 tip = _car.Pos + _car.Rot * new Vector3(0.60f, -0.30f, 2.02f);
        for (int i = 0; i < 4; i++)
            _fx.Smoke(tip, _car.Vel * 0.6f + _car.Rot * new Vector3(_rng.RandfRange(-0.3f, 0.5f), _rng.RandfRange(0.2f, 0.6f), _rng.RandfRange(2.5f, 4.5f)),
                new Color(0.9f, 0.92f, 0.95f), 0.5f + 0.3f * boost, 0.12f, 0.8f + 0.5f * boost, _rng.RandfRange(0.35f, 0.6f));
    }
    void PopBurst(float rpm)
    {
        _pops = _rng.RandiRange(2, 5);
        _popTimer = _rng.RandfRange(0.03f, 0.12f);
        _popStrength = Math.Clamp(rpm / (_params.Redline * 0.93f), 0.5f, 1f);
        if (EngineFuel == FuelGrade.Sputnik) WastegatePuff();
        if (EngineFuel is FuelGrade.Rocket or FuelGrade.Sputnik && _car.TurboGain > 0f) { _pops += 2; _popStrength = 1f; _popBoom = true; }      // the lift on the turbo: detonations, with the blow-off
    }
    float _popStrength = 1f;
    bool _popBoom;      // this burst is the rocket fuel's: every pop an explosion

    void OnShell(Vector3 p, float distToCar)
    {
        if(LanWorldHit(p,0)) return;
        if (OS.HasEnvironment("TANKDBG")) GD.Print($"shell lands at {_clock:F2} s");
        if (_scenery.Blast(p, Breakables.ShellReach) > 0) RemoteCrack(p);
        if (_shellTree is Vector2 st && _shotAt > 1e8f)
        {
            GD.Print($"shell-tree: shell landed {new Vector2(p.X, p.Z).DistanceTo(st):F1} m from the tree at {_clock:F2} s");
            _shotAt = _clock + 3f;
        }
        if (_shotOnShell && _followTank && _tanks?.FirstTank is Vector2 ft && new Vector2(p.X, p.Z).DistanceTo(ft) < 35f)
        {
            _shotAt = _clock + 0.12f;
            _shotOnShell = false;
        }
        float d = p.DistanceTo(_car.Pos);
        if (d < 60f) _cam.Shake(Math.Clamp(1.2f - d / 50f, 0.15f, 1.2f));
        if (d < 45f) Rumb(0.35f * (1f - d / 45f), 0.9f * MathF.Pow(1f - d / 45f, 1.5f), 0.15f + 0.2f * (1f - d / 45f), $"shell {d:F0} m");
        if (_state != State.Racing) return;
        if (distToCar < 12f)
        {
            _car.Blast(p, 1f);
            if (_incomingCd <= 0)
            {
                _hud.Show("INCOMING!", distToCar < 5f ? "FRIENDLY FIRE, TOVARISH" : "ACCURACY: ACCORDING TO PLAN", 2f, Hud.Red, Prio.Important, urgent: true, plain: "INCOMING!");
                _incomingCd = 6f;
            }
            if (distToCar <= EngineHealth.ShellLethal) DestroyCar(p, "TANK SHELL");
            else HealthChanged(_health.Damage(EngineHealth.ShellDamage(distToCar), "shell", distToCar));
        }
    }

    void ShowHindWarning(Vector3 p)
    {
        AddChild(new ExerciseWarningSound {Position=p});
        if(_state==State.Racing && p.DistanceTo(_car.Pos)<260)
            _hud.Show("HIND: INBOUND","AMBER LIGHTS MARK THE FIRING LINE",3,Hud.Gold,Prio.Important,urgent:true);
    }

    void OnHindWarning(Vector3 p)
    {
        if(InLanRace && _net.IsHost) _net.SendEffect(p,4);
        ShowHindWarning(p);
    }

    void TankBlastFeel(Vector3 muzzle)
    {
        if(_state!=State.Racing) return;
        float distance=muzzle.DistanceTo(_car.Pos);
        // The shot is felt far out and hits like a wall close in: a camera slam, a white-out and a ringing in the ears.
        if(distance<150) _cam.Shake(2.4f*MathF.Pow(1-distance/150,1.3f));
        if(distance<75) _cam.Blast(MathF.Pow(1-distance/75,1.2f));
        if(distance<120) Rumb(.6f+.4f*(1-distance/120),1f*(1-distance/120)+.15f,.25f+.4f*(1-distance/120),"tank muzzle blast");
        if(distance<50)
        {
            float k=1-distance/50;
            AddChild(new CCCPRacing.UI.BlastFlash {Strength=.6f*k});
            AddChild(new EarRingSound {Level=k});
        }
        if(distance<110)
        {
            float strength=1-distance/110;
            if (!ViewerMode) { AudioMix.Duck(AudioMix.Engine,14*strength,.9f); AudioMix.Duck(AudioMix.Diesel,10*strength,.9f); }
        }
    }

    void HindStrikeFeel(Vector3 p)
    {
        if(_state!=State.Racing) return;
        float distance=ProjectileHits.Distance(_car,p);
        if (distance < 32) _cam.Shake(.24f * MathF.Pow(1-distance/32,1.4f));
        if (distance < 18) Rumb(.08f,.3f*(1-distance/18),.06f,"Hind cannon strike");
    }

    void OnHindCannon(Vector3 p)
    {
        HindStrikeFeel(p);
        if(LanWorldHit(p,3)) return;
        if (_state != State.Racing) return;
        if(_scenery.Blast(p,1.2f)>0) RemoteCrack(p);
        float distance = ProjectileHits.Distance(_car,p);
        float damage=EngineHealth.CannonDamage(distance);
        if(damage<=0) return;
        _carVis.Damage?.Hit(10f,_car.Rot.Transposed()*(p-_car.Pos),(_car.Pos-p).Normalized(),_car,_fx);
        if(damage>=1) DestroyCar(p,"MI-24");
        else HealthChanged(_health.Damage(damage,"MI-24 cannon",distance));
    }

    void OnJetCrash(Vector3 p)
    {
        if(LanWorldHit(p,1)) return;
        if (OS.HasEnvironment("JETDBG")) GD.Print($"Balalaika crash at {_clock:F1} s, ({p.X:F0},{p.Z:F0})");
        if (_scenery.Blast(p, Breakables.BlastReach) > 0) RemoteCrack(p);
        float d = p.DistanceTo(_car.Pos);
        if (d < 160f) _cam.Shake(Math.Clamp(2.2f - d / 70f, 0.3f, 2f));
        if (d < 130f) Rumb(0.3f * (1f - d / 130f), 0.9f * (1f - d / 130f), 0.35f, $"jet crash {d:F0} m");
        if (_state == State.Racing && d <= EngineHealth.JetLethal) DestroyCar(p, _trackId == "bonus" ? "MI-24" : "MIK-21");
        else if (_state == State.Racing) HealthChanged(_health.Damage(EngineHealth.JetDamage(d), "jet", d));
    }

    /// <summary>Test: flat out on full lock as a missile closes in: towards running square across
    /// its path, or, already crossing it, hard away from the side it comes from.</summary>
    void MissileEvade()
    {
        foreach (var m in _jets.Missiles)
        {
            if (!m.Alive || _car.Pos.DistanceTo(m.Pos) > m.Vel.Length() * _missileEvade) continue;
            Vector2 fwd = new Vector2(_car.Forward.X, _car.Forward.Z).Normalized(), right = new Vector2(_car.Right.X, _car.Right.Z).Normalized();
            Vector2 across = new Vector2(-m.Vel.Z, m.Vel.X).Normalized();
            if (across.Dot(fwd) < 0) across = -across;                   // whichever perpendicular is nearer the nose
            float err = MathF.Atan2(across.Dot(right), across.Dot(fwd));
            float away = right.Dot(new Vector2(m.Pos.X - _car.Pos.X, m.Pos.Z - _car.Pos.Z)) > 0 ? -1f : 1f;
            _car.SetControls(true, false, Math.Abs(err) > 0.3f ? Math.Sign(err) : away, false);
            return;
        }
    }

    /// <summary>A Balalaika's missile went off: shake, and throw the car about if it was close.</summary>
    void OnMissile(Vector3 p, float distToTarget)
    {
        if(LanWorldHit(p,2)) return;
        if (_scenery.Blast(p, Breakables.BlastReach) > 0) RemoteCrack(p);
        float d = p.DistanceTo(_car.Pos);
        if (OS.HasEnvironment("MISSILEDBG")) GD.Print($"missile boom at {_clock:F2} s, {d:F1} m from the car, car {_car.Vel.Length() * 3.6f:F0} km/h");
        if (d < 90f) _cam.Shake(Math.Clamp(1.8f - d / 45f, 0.2f, 1.8f));
        if (d < 60f) Rumb(0.35f * (1f - d / 60f), MathF.Pow(1f - d / 60f, 1.5f), 0.2f + 0.25f * (1f - d / 60f), $"missile {d:F0} m");
        if (_state != State.Racing) return;
        if (d < 12f) _car.Blast(p, 1.5f);
        MissileHitsCar(p, distToTarget, d);
    }

    /// <summary>The bus's news (ditch, fire, spare bus), only while you are racing.</summary>
    void BusComment(string title, string sub, Color color)
    {
        if (_state == State.Racing && !PlanHasCar && !FateHasCar && !FuelHasCar) _hud.Show(title, sub, 3f, color, Prio.Nice);
    }

    const float BusHeatRange = 4f, BusHeatDamage = 0.025f;   // m from the burning bus; health per second right beside it

    /// <summary>Test (--bus-heat): the car parked beside the burning bus.</summary>
    void ParkByFire()
    {
        Vector2 cp = new(_car.Pos.X, _car.Pos.Z);
        _bus.FireDistance(cp, out Vector2 hot);
        Vector2 away = (cp - hot).Normalized();
        Vector2 p = hot + away * 2f;
        _car.Reset(new Vector3(p.X, _track.MeshHeight(p.X, p.Y) + 0.55f, p.Y), TrackData.YawFor(new Vector2(-away.Y, away.X)));
    }

    /// <summary>Too close to the burning bus: the engine suffers a little and the paint blisters now and then.</summary>
    void BusFireHeat(float dt)
    {
        if (_bus == null || _state != State.Racing || FateHasCar) return;
        float d = _bus.FireDistance(new Vector2(_car.Pos.X, _car.Pos.Z), out Vector2 hot);
        if (d > BusHeatRange) { _busHeatDent = 1f; return; }
        HealthChanged(_health.Damage(BusHeatDamage * Math.Max(0.3f, 1f - d / BusHeatRange) * dt, "BUS FIRE"));
        if ((_busHeatDent -= dt) > 0) return;
        _busHeatDent = 2.5f;
        if (OS.HasEnvironment("BUSDBG")) GD.Print($"bus fire: the car {d:F1} m from it at {_clock:F1} s, health {_health.Health * 100:F1}%");
        Vector3 at = new(hot.X, _car.Pos.Y + 0.3f, hot.Y);
        _carVis.Damage?.Hit(5.5f, _car.Rot.Transposed() * (at - _car.Pos), (_car.Pos - at).Normalized(), _car, _fx);
    }

    /// <summary>The political officer comments on your driving.</summary>
    void Commentary(float dt)
    {
        _incomingCd -= dt;
        _fenceCd -= dt;
        _breakCd -= dt;
        _tankWarnCd -= dt;
        if (_tanks != null && _tankWarnCd <= 0 && _track.TankRange is { } rng
            && _car.Pos.X > rng.X0 && _car.Pos.X < rng.X1 && _car.Pos.Z > rng.Z0 && _car.Pos.Z < rng.Z1)
        {
            _hud.Show("TANK EXERCISE IN PROGRESS!", "ACCURACY: ACCORDING TO PLAN", 2.5f, Hud.Gold, Prio.Important, urgent: true, plain: "TANK EXERCISE IN PROGRESS!");
            _tankWarnCd = 40f;
        }

        _jetWarnCd -= dt;
        if (_jetWarnCd <= 0 && ((_jets?.RunwayBusyAt(new Vector2(_car.Pos.X, _car.Pos.Z)) ?? false)
            || (_hinds?.RunwayBusyAt(new Vector2(_car.Pos.X, _car.Pos.Z)) ?? false)))
        {
            _hud.Show(_hinds != null ? "MI-24 HIND TAKING OFF!" : "MIK TAKING OFF!", "CLEAR THE RUNWAY, TOVARISH", 2.5f, Hud.Red, Prio.Critical, urgent: true);
            _jetWarnCd = 10f;
        }

        _drunkCd -= dt;
        if (_drunks is { HitThisTick: true } && _drunkCd <= 0)
        {
            _drunkCd = 6f;
        }
        if (_bus != null)
        {
            _busWarnCd -= dt;
            _busHitCd -= dt;
            Vector2 cp = new(_car.Pos.X, _car.Pos.Z);
            float bd = _bus.Position2D.DistanceTo(cp);
            bool headOn = (cp - _bus.Position2D).Dot(_bus.Forward2D) > 0 && _car.Vel.Dot(new Vector3(_bus.Forward2D.X, 0, _bus.Forward2D.Y)) < -3f;
            if (bd < 6f && _maxImpact > 2f && _busHitCd <= 0)
            {
                _busHitCd = 8f;
                _busWarnCd = 20f;
            }
            else if (bd < 40f && headOn && _busWarnCd <= 0)
            {
                _hud.Show("BUS! KEEP RIGHT, TOVARISH!", "IT RUNS AGAINST THE RACE", 2.5f, Hud.Gold, Prio.Critical, urgent: true, plain: "BUS! KEEP RIGHT", plainSub: "IT RUNS AGAINST THE RACE");
                _busWarnCd = 30f;
            }
        }

        _kozlik?.Commentary(dt, _car, _hud);

        if (_babushkas is { HitsThisTick: > 0 } b)
        {
            _hud.Show(_trackId=="bonus"?"TROOPER DOWN!":Slogans.Pick(Babushkas.HitSlogans), _trackId=="bonus"?"HE WILL REPORT TO THE COMMANDER":"SHE WILL TELL THE WHOLE VILLAGE", 2.5f, Hud.Red, plain: _trackId=="bonus"?"SUPPLY TROOPER HIT":"BABUSHKA HIT");
        }
        if (_babushkas is { LensHitThisTick: true })
            _hud.Show(_trackId=="bonus"?"HE IS UNHARMED.":"SHE IS UNHARMED.", "THE CAMERA IS NOT.", 3f, Hud.Red, Prio.Nice);

        if (_tractor != null)
        {
            _tractorWarnCd -= dt;
            _tractorHitCd -= dt;
            float d = _tractor.Position2D.DistanceTo(new Vector2(_car.Pos.X, _car.Pos.Z));
            if (d < 5.5f && _maxImpact > 2f && _tractorHitCd <= 0)
            {
                _tractorHitCd = 8f;
                _tractorWarnCd = 20f;
            }
            else if (d < 30f && (_tractor.CrossingBarnRoad || _tractor.HasDeparted) && _tractorWarnCd <= 0)
            {
                _hud.Show("DRUNK TRACTORIST AHEAD!", _tractor.Dozing ? "HE IS SLEEPING AT THE WHEEL" : "HE HAS HAD HIS 300 GRAMS", 2.5f, Hud.Gold, Prio.Important, urgent: true, plain: "TRACTOR AHEAD!");
                _tractorWarnCd = 35f;
            }
        }

        _grassCd -= dt; _airCd -= dt; _crashCd -= dt; _slowCd -= dt;

        if (_car.OffRoadTime > 2.5f && _grassCd <= 0)
        {
            bool snow = Array.Exists(_car.Wheels, w => w.Contact && w.Surface == SurfaceType.DeepSnow);
            _hud.Show(Slogans.Pick(snow ? Slogans.Snow : Slogans.Grass), null, 2f, Hud.Paper, Prio.Nice);
            _grassCd = 12f;
        }
        bool air = _car.AirTime > 0.45f;
        if (air && !_wasAirborne && _airCd <= 0)
        {
            _hud.Show(Slogans.Pick(Slogans.Air), null, 2f, Hud.Gold, Prio.Nice);
            _airCd = 8f;
        }
        _wasAirborne = air;
        if (_maxImpact > 7f && _crashCd <= 0)
        {
            _crashCd = 6f;
        }
        _slowTime = _car.Speed < 3f && !(_drunks is { MechanicUp: true })
            && !GameplayCinematicCamera.RoadForward(_track, _car.Pos).HasValue ? _slowTime + dt : 0;
        if (_slowTime > 6f && _slowCd <= 0)
        {
            _hud.Show(Slogans.Pick(Slogans.Slow), (_padMode ? "B" : "R") + " RETURNS YOU TO THE ROAD" + (InLanRace ? "" : " (LAP VOID)"), 2.5f, Hud.Paper, plain: "STUCK?", plainSub: (_padMode ? "B" : "R") + " RETURNS YOU TO THE ROAD" + (InLanRace ? "" : " (LAP VOID)"));
            _slowCd = 15f;
        }
    }

    // ------------------------------------------------------------ frame

    public override void _Process(double delta)
    {
        if(LanPlaybackSource!=null) {if(!_lanPlaybackExternallyDriven) {if(!LanPlaybackCheckAccess()) return;if(!_booting) {LanExportTick();LanExportUiProbe();LanPlaybackFrame((float)delta);}}return;}
        if(!ViewerMode && InstructionTrack==null) LanReplayPoll();
        if (_booting) return;
        if (InstructionTrack != null && GetParent() is SubViewport lessonViewport
            && lessonViewport.GetParent() is Viewport raceViewport)
            PixelFrame.Mirror(lessonViewport, raceViewport);      // (the window's size and VISUALS > RENDERING: the parent's framebuffer and logical size)
        float dt = (float)delta;
        if (OS.HasEnvironment("FPSDBG") && (int)(_clock / 5f) != (int)((_clock + dt) / 5f))
            GD.Print($"fps {Engine.GetFramesPerSecond():0} at {_clock + dt:F0} s, {RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame)} draw calls, car ({_car.Pos.X:F0},{_car.Pos.Z:F0}) {_car.Speed * 3.6f:F0} km/h, road m {_timer.Progress}");
        _clock += dt;
        LabFrame(dt);
        if (ViewerMode) { long vt = LoadProf.On ? System.Diagnostics.Stopwatch.GetTimestamp() : 0; ViewerFrame(dt); if (LoadProf.On && ++_viewerDiagFrames <= 12) GD.Print($"LOADPROF viewer _Process #{_viewerDiagFrames}: {(System.Diagnostics.Stopwatch.GetTimestamp() - vt) * 1000.0 / System.Diagnostics.Stopwatch.Frequency:F1} ms"); return; }      // RaceScene.Viewer.cs: the camera, effects and sound of the replay only
        
        TapeMeasureFrame(dt);
        for (int i = _keyQueue.Count - 1; i >= 0; i--)
            if (_clock >= _keyQueue[i].at && !_lanKeep.OwnsInput)      // (scripted keys wait while the KEEP question is answered by its probe)
            {
                var (_, key, pressed) = _keyQueue[i];
                _keyQueue.RemoveAt(i);
                // Printable keys carry their character too, so --key-at can type a name.
                long uni = key is >= Key.A and <= Key.Z or >= Key.Key0 and <= Key.Key9 or Key.Space or Key.Minus ? (long)key : 0;
                Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Unicode = pressed ? uni : 0, Pressed = pressed });
            }
        PadFrame(dt);
        bool opaqueTapeScenery = _tapeShoot && _tapeSceneryOpaque;
        FadeUnder.Off = opaqueTapeScenery;
        FadeUnder.Car = _car.Pos;     // the start barn's roof goes see-through over the car
        SeeThrough.Car = _car.Pos;    // ...and a building between the camera and the car
        SeeThrough.Off = opaqueTapeScenery || (_droneTour && (_trackId != "bonus" || _droneStartT < 0));
        ArchiveAtLaunch();      // RaceScene.Files.cs: the KGB ARCHIVE before the main menu
        LapsRewardFrame(dt);
        HandleInput();
        if (TrackLoadingScreen.Active != null)
        {
            if(TrackLoadingScreen.Active.IsLan) {LanFrame(dt);LanHud();}
            return;
        }
        if (WaitForStartClutch())
        {
            UpdateHud();
            UpdateSound(dt);
            return;     // the world, effects and countdown wait for the driver's own clutch
        }
        SchoolFrame(dt);
        TrackPreviewFrame(dt);
        CarTourFrame(dt);
        _cam.ShakeEnabled = !PresentationActive && (!InSchool || SchoolLetsDrive);
        LanFrame(dt);

        // The title's world is a faded poster, fading to colour in replay focus.
        float sat = _state != State.Title || PresentationActive || _watching || _hud.Clean ? 1f : 0f;
        if (_saturation != sat) SetSaturation(_state != State.Title ? 1f : Mathf.MoveToward(_saturation, sat, dt / 0.5f));

        if (_state == State.Countdown)
        {
            if (!_showKeys && !InLanRace && !StartRevving) _countdown -= dt;     // preparation and the key card hold the lights; LAN uses the host clock
            if (_countdown <= 0) StartRacing();
        }
        else if (_state == State.Racing && _countdown > -1f)
        {
            _countdown -= dt;
        }

        if (_tapeShoot && Replaying && TitleGhost?.HasDamage == true)
            TitleGhost.ApplyDamage(_replayClock, _carVis.Damage, _fx);
        _carVis.Sync(_car, _track);
        _carRadio.CarPos = _car.Pos;
        PrepareReplayCamera();
        PrepareGameplayCamera();
        if (_followTank && _tanks?.FirstTank is Vector2 tp0)
            _cam.Follow(new Vector3(tp0.X, _track.MeshHeight(tp0.X, tp0.Y), tp0.Y), Vector3.Zero, dt * 20f);
        else if (_followJet && (_jets != null || _hinds != null))
        {
            if ((_hinds?.FirstJet ?? _jets?.FirstJet) is Vector3 mp) _lastJet = mp;
            _cam.Follow(_lastJet == Vector3.Zero ? _car.Pos : _lastJet, Vector3.Zero, dt * 20f);
        }
        else if (_followKozlik && _kozlik != null)
        {
            var up = _kozlik.Position2D;
            _cam.Follow(new Vector3(up.X, _track.MeshHeight(up.X, up.Y), up.Y), Vector3.Zero, dt * 20f);
        }
        else if (_followBus && _bus != null)
        {
            var bp = _bus.Focus2D;
            _cam.Follow(new Vector3(bp.X, _track.MeshHeight(bp.X, bp.Y), bp.Y), Vector3.Zero, dt * 20f);
        }
        else if (_lookAt is Vector2 look)
            _cam.Follow(new Vector3(look.X, _track.MeshHeight(look.X, look.Y), look.Y), Vector3.Zero, dt * 20f);
        else if (_followBabushka && _babushkas != null)
        {
            if (_angryBabushka) _babushkas.Enrage(0, 1f);
            var bp = _babushkas.WalkerPosition(0);
            _cam.Follow(new Vector3(bp.X, _track.MeshHeight(bp.X, bp.Y), bp.Y), Vector3.Zero, dt * 20f);
        }
        else if (_followSlurry && _slurry != null)
        {
            var sp = _slurry.Position2D;
            _cam.Follow(new Vector3(sp.X, _track.MeshHeight(sp.X, sp.Y), sp.Y), Vector3.Zero, dt * 20f);
        }
        else if (_followTractor && _tractor != null)
        {
            var tp = _tractor.Position2D;
            _cam.Follow(new Vector3(tp.X, _track.MeshHeight(tp.X, tp.Y), tp.Y), Vector3.Zero, dt * 20f);
        }
        else if (!SchoolCamera(dt) && !FateCamera(dt) && !PlanCamera(dt) && !ReplayCamera(dt) && !GameplayCamera(dt))
        {
            CameraFeel(dt);           // RaceScene.CameraFeel.cs: speed, ground and jumps in the view
            _cam.Follow(_car.Pos, _car.Vel, dt);
        }
        bool frozen = _state is State.Paused || SchoolFrozen;
        _fx.Tick(frozen ? 0f : dt, _cam.GlobalTransform.Basis);
        LensFx(frozen ? 0f : dt);

        var raceGhost = RaceGhost;
        bool ghost = (_state is State.Racing or State.Paused) && raceGhost != null && _lapClock < raceGhost.Duration && !InLanRace && !InSchool;
        _ghostVis.Visible = ghost;
        if (ghost)
        {
            raceGhost.Apply(_lapClock, _ghostCar, frozen ? 0 : dt);
            _ghostVis.Sync(_ghostCar, _track);
        }

        UpdateHud();
        ResignTick(dt);
        DayNightFrame(dt);
        CardFrame(dt);
        SpeedTrapFrame();
        UpdateSound(dt);
        AudioMix.Tick(dt);
        PaceNotesTick();
        Screenshot();
    }

    void HandleInput()
    {
        // Replay focus, the leaderboard, the key card and the name entry listen to raw input only; the
        // press that ended them is swallowed here (Enter/Esc would otherwise start, pause or quit).
        if (_watching || _showBoard || _showLog || _showFiles || _showLessons || _showKeys || CardShown || _gulag || _planScreen || _askName || _resignOut >= 0 || _recordingsOpen || _keepNameOpen || _lanKeep.OwnsInput || _hud.StakhanovBoardOpen) return;      // modal screens own the keys, including the paused Stakhanov board
        if (Engine.GetProcessFrames() <= _overlayExitFrame + 1) return;
        if(LanSession.Current?.LeavePrompt is {ConsumedFrame:>0} leave && Engine.GetProcessFrames()<=leave.ConsumedFrame+1) return;
        if (LanInput() || TitleCardInput() || SchoolInput()) return;
        bool alt = Input.IsKeyPressed(Key.Alt);
        switch (_state)
        {
            case State.Title:
                if (_replayNextAt > 0 && _clock >= _replayNextAt)
                {
                    _replayNextAt = -1;
                    _replayNextDone = true;
                    SwitchReplayTrack(1);
                }
                if (Input.IsActionJustPressed("start") && !alt) TitleStart();
                else if (Input.IsActionJustPressed("pause") && !MenuBack()) AskResignation();
                // Menu: Left/A and Right/D move along the row.
                else if (Input.IsActionJustPressed("steer_left")) TitleSide(-1);
                else if (Input.IsActionJustPressed("steer_right")) TitleSide(1);
                break;
            case State.Countdown:
            case State.Racing:
                if (Input.IsActionJustPressed("pause")) { _state = State.Paused; _hud.Mode = HudMode.Paused; _hud.PauseSelection = PauseStartRow; }
                else if (Input.IsActionJustPressed("keys")) ShowKeys(freeze: true);
                else if (_state == State.Racing && !PlanHasCar && !FateHasCar && !FuelHasCar && (Input.IsActionJustPressed("rescue") || (_voidAt > 0 && _clock >= _voidAt)))
                {
                    _voidAt = -1;
                    CallMechanic();
                }
                break;
            case State.Paused:
                PauseInput(alt);
                break;
            case State.Failed:
                FateInput();
                break;
            case State.Finished:
                PlanSkip();
                break;
        }
        GameplayCameraInput();
        if (Input.IsActionJustPressed("debug")) _hud.ShowDebug = !_hud.ShowDebug;
        if (_state is State.Countdown or State.Racing or State.Paused && Input.IsActionJustPressed("minimap") && !InSchool) ToggleMinimap();
        if (_state is State.Countdown or State.Racing or State.Paused && Input.IsActionJustPressed("comments"))
            _hud.SetComments(!_hud.Comments, confirm: true);

        // Car radio: in the car only (the title screen has its own music), not while typing a name.
        if (_state is not (State.Title))
        {
            if (Input.IsActionJustPressed("radio_toggle")) { _carRadio.Toggle(); RememberRadio(); }
            if (Input.IsActionJustPressed("radio_volume_down")) _carRadio.StepVolume(-1);
            if (Input.IsActionJustPressed("radio_volume_up")) _carRadio.StepVolume(1);
        }
    }

    /// <summary>A run from the title: the rally is commented, Stakhanov laps are quiet (TAB toggles; a restart keeps it).</summary>
    void DefaultComments() => _hud.SetComments(_commentsForce ?? !StakhanovRules, confirm: false);

    void UpdateHud()
    {
        if (_state == State.Racing)
            _hud.Mode = _countdown > -1f ? HudMode.Countdown : HudMode.Race;
        _hud.Countdown = _countdown;
        _hud.StartRevving = StartRevving;
        _hud.StartClutchWaiting = StartClutchWaiting || (LanGridWaiting && !_car.Clutch);
        _hud.LanGridWaiting = LanGridWaiting;
        _hud.LanGridReady = _lanReadyCount;
        _hud.StartClutchPrompt = _state == State.Countdown && !StartRevving && !InSchool;
        _hud.FalseStart = _falseStart && _state == State.Countdown;
        _hud.TitleReplay = Replaying;
        _hud.RecordTrack = TrackName(_trackId) + TitleSet switch { 1 => " STAKHANOV", 2 => " DIESEL", 3 => " JET FUEL", 4 => " SPUTNIK", _ => "" };
        // The message rule (UI/HudMessages.cs): clocks stop under a pause or a card; no jokes in danger.
        _hud.Frozen = _state is State.Paused || SchoolFrozen || StartClutchWaiting;
        _hud.Danger = _state == State.Racing && (_hud.MissileMarks.Count > 0 || _tanks is { Chasing: true } || _jets is { RevengeActive: true }
                      || _norms.OnLastLap || _tank.Low || _dry || FateHasCar);
        _hud.Bubbles.Clear();
        foreach (var (pos, text) in ActorShouts())
            if (!_cam.IsPositionBehind(pos))
                _hud.Bubbles.Add((_cam.UnprojectPosition(pos), text));
        if (_rad != null)
            foreach (var (pos, text) in DoseShouts())
                if (!_cam.IsPositionBehind(pos))
                    _hud.Bubbles.Add((_cam.UnprojectPosition(pos), text));
        if (_trap != null)
            foreach (var (pos, text) in _trap.ActiveShouts())
                if (!_cam.IsPositionBehind(pos))
                    _hud.Bubbles.Add((_cam.UnprojectPosition(pos), text));
        foreach (var (pos, text) in _truck.ActiveShouts())
            if (!_cam.IsPositionBehind(pos))
                _hud.Bubbles.Add((_cam.UnprojectPosition(pos), text));
        _hud.LapTime = _timer.Running ? _timer.LapTime : 0;
        _hud.SessionBestLap = _sessionBest;
        _hud.BestIsSession = _run == RunMode.Rally && _state != State.Title && !InSchool && !InLanRace && !ViewerMode;
        _hud.BestLap = _state == State.Title && BundledTitleReplay ? TitleGhost.LapTime : _state == State.Title && TitleSet != 0 ? _bestLaps[TitleSet] : _timer.BestLap;
        _hud.LastLap = _timer.LastLap;
        _hud.LapVoid = _state == State.Countdown ? _falseStart : _timer.LapVoid;
        _hud.LastLapVoid = _timer.LastLapVoid;
        StakhanovSessionHud();
        StakhanovBoardHud();
        _hud.Lap = _timer.LapsDone + 1;
        _hud.Training = StakhanovRules;
        MenuHud();
        int page = _showBoard ? _boardPage : RecordSet;
        string boardTrack = _showBoard ? Tracks[_boardTrack].id : _trackId;
        bool built = boardTrack == _trackId;
        _hud.Board = built ? _boards[page].Entries : OtherBoard(boardTrack, page).Entries;
        _hud.BoardHighlight = built ? _boardHighlights[page] : -1;
        _hud.BoardStakhanov = page == 1;
        _hud.BoardFuels = new[] { 0, 2, 3, 4 }.Select(f => new Hud.FuelBoard(
            f switch { 2 => "DIESEL", 3 => "JET FUEL", 4 => "SPUTNIK", _ => "PETROL" }, BoardFor(boardTrack, f),
            built ? _boardHighlights[f] : -1,
            SpeedText(f switch { 2 => "rally_diesel", 3 => "rally_rocket", 4 => "rally_sputnik", _ => "rally" }, boardTrack))).ToArray();
        _hud.BoardSelected = _boardReplaySelected;
        _hud.BoardReplayNotice = _showBoard ? BoardReplayStatus() : "";
        _hud.BoardFuel = page switch { 2 => "DIESEL", 3 => "JET FUEL", 4 => "SPUTNIK", 0 => "PETROL", _ => "" };
        _hud.BoardTrack = TrackName(boardTrack);
        _hud.BoardSpeed = SpeedText(page switch { 1 => "stakhanov", 2 => "rally_diesel", 3 => "rally_rocket", 4 => "rally_sputnik", _ => "rally" }, boardTrack);
        _hud.BoardScoped = false;
        if (BoardScoped) TrackCardBoardsHud();      // RaceScene.TrackCardBoards.cs: the track card's own boards replace every field above
        _hud.Pad = _padMode;
        _hud.PadConnected = _padMode || Input.GetConnectedJoypads().Count > 0;
        _hud.Rumble = _rumble.Enabled;
        // The comments keep clear of the car: where it is on the screen and which way it goes down it.
        if (_state is State.Countdown or State.Racing or State.Paused or State.Finished or State.Failed && !_cam.IsPositionBehind(_car.Pos))
        {
            Vector2 at = _cam.UnprojectPosition(_car.Pos), ahead = _cam.UnprojectPosition(_car.Pos + _car.Vel * 0.25f);
            _hud.CarOnScreen = at;
            _hud.CarScreenDy = (ahead.Y - at.Y) * 4f;
        }
        else _hud.CarOnScreen = null;
        _hud.GameplayCameraName = GameplayCameraAllowed ? GameplayCameraName : null;
        _hud.CameraUnlocksMissing = GameplayCameraAllowed && HasUndiscoveredCameras;
        _hud.CameraSomeFound = HasDiscoveredCamera;
        _hud.CapitalistLuxuriesEnabled = PixelFrame.NativeActive || _aaMode != 0 || _softLevel != 0;
        if (_state != State.Paused) _padLost = false;
        _hud.PadLost = _padLost;
        // The record holder: the board's top name when it holds the record time.
        var holderBoard = _state == State.Title ? _boards[TitleSet] : _board;
        _hud.RecordHolder = _state == State.Title && BundledTitleReplay ? "ANTTI" : holderBoard.Entries.Count > 0 && _hud.BestLap > 0 && Math.Abs(holderBoard.Entries[0].Time - _hud.BestLap) < 0.005f
            ? holderBoard.Entries[0].Name : null;
        _hud.EntryName = _entryName;
        RecordingsTick();      // (every frame in every state: the physics tick does not run in the Party Meeting)
        _hud.KeepNameOpen = _keepNameOpen; _hud.KeepName = _keepName;
        _hud.ReplayDeleteOpen = _replayDeleteId != null;
        _hud.ReplayDeleteConfirm = _replayDeleteConfirm;
        _hud.NameAsk = _askName && _state == State.Title;
        _hud.NameAskSub = $"A NEW PERSONNEL FILE - NO. {_askFile}";
        _hud.FileLabel = FileLabel();
        _hud.PowerScale = _car.PowerScale;
        FateHud();
        RevengeHud();
        PlanHud();
        FuelHud();
        if (_rad != null) DoseHud();
        SchoolHud();     // after the plan and fuel: the school draws over (and mocks) them
        MinimapHud();
        _hud.SpeedKmh = Math.Abs(_car.ForwardSpeed) * 3.6f;
        _hud.Gear = _car.Neutral ? 0 : _car.Gear;
        _hud.Clutch = _state is State.Countdown or State.Racing && _car.Clutch;
        // The tachometer starts at zero RPM; a running idle must light its lowest bars.
        _hud.RpmFrac = _snd.EngineLevel > 0
            ? (StartClutchWaiting ? _params.IdleRpm : _car.Rpm) / _params.Redline : 0;

        if (_hud.ShowDebug)
        {
            var l = _hud.DebugLines;
            l.Clear();
            Vector2I vs = (Vector2I)GetViewport().GetVisibleRect().Size;
            var frame = GetParent()?.GetParent() as PixelFrame;
            l.Add($"FPS {Engine.GetFramesPerSecond():0}  RES {vs.X}X{vs.Y} X{frame?.PixelScale ?? 1}{((frame?.RenderScale ?? 1) > 1 ? " NATIVE" : "")}");
            l.Add($"RPM {_car.Rpm:0}  THR {_car.Throttle:0.00}  GEAR {_car.Gear}");
            l.Add($"CLUTCH {(_car.Clutch ? "IN" : _car.Neutral ? "NEUTRAL" : _car.ClutchKick > 0 ? "DUMP" : "OUT")}");
            l.Add($"ROLL {Mathf.RadToDeg(MathF.Asin(Math.Clamp(_car.Right.Y, -1, 1))):0}  PITCH {Mathf.RadToDeg(MathF.Asin(Math.Clamp(_car.Forward.Y, -1, 1))):0}");
            l.Add($"SLIDE {_car.Slide:0.00}  AIR {_car.AirTime:0.0}");
            l.Add($"SURFACE {_car.Wheels[2].Surface.ToString().ToUpperInvariant()}");
            l.Add($"GATE {_timer.NextGate}  PROG {_timer.Progress}");
            l.Add($"FX {_fx.ParticleCount}  SPIN {(_car.Wheels[2].Spinning ? 1 : 0)}{(_car.Wheels[3].Spinning ? 1 : 0)}  LAT {_car.Wheels[2].SlipLat:0.0}");
            for (int i = 0; i < 4; i++) _hud.WheelLoads[i] = _car.Wheels[i].Load;
        }
        LanHud();
    }

    /// <summary>The co-driver calls the next bend (comments on, racing): the blips, and the words in the radio box.</summary>
    void PaceNotesTick()
    {
        if (_state != State.Racing || Replaying || InSchool || !_hud.Comments || _wreck || _rescue.Active) return;
        if (!_pace.Tick(_track, new Vector2(_car.Pos.X, _car.Pos.Z), _car.Speed, out var bend)) return;
        _paceBlip.Call(bend.Sign, bend.Severity);
        _hud.ShowRadio((bend.Sign > 0 ? "RIGHT " : "LEFT ") + PaceNotes.Names[bend.Severity]);
    }

    void UpdateSound(float dt)
    {
        bool paused = _state is State.Paused || SchoolFrozen || StartClutchWaiting;
        _hindsAll?.SetAudioPaused(paused || !InWorld(Actors.Jets));
        _btrAll?.SetAudioPaused(paused || !InWorld(Actors.Btr));
        if (_state == State.Paused && MixerPreview(dt))
        {
            _snd.Volume = 0.8f;
            return;
        }
        // The dashboard's lights sound when one comes on (live drive only: a replay and a viewer have the lights but not the chime).
        if (_chime != null)
        {
            bool live = !paused && _state == State.Racing && !Replaying && !ViewerMode && !InSchool && !_rescue.Active;
            _chime.SetLive(live);
            if (live) _chime.Lights(_hud.WarningLevels());
        }
        // The fuel's voice: the rally's own, or (in a replay, the title's demonstration) the one the recorded lap was driven on; a nitro only in a live run.
        if (_engineFuelApplied != EngineFuel) ApplyFuelEngine();      // (a replay, the viewer or the title shows the car of the lap's fuel: its redline on the tachometer too)
        _snd.TurboSpool = _params.TurboSpool;
        _snd.FiveCylinder = EngineFuel == FuelGrade.Sputnik;
        // The five-cylinder shows: the whole car trembles in a beat of five at idle and the fat pipe glows under boost (cosmetic only: RaceScene.cs, CarVisual.cs).
        bool five = EngineFuel == FuelGrade.Sputnik && !paused;
        _carVis.Shudder = five ? Math.Clamp(1f - (_car.Rpm - _params.IdleRpm) / 2500f, 0f, 1f) * (_car.Throttle < 0.4f ? 1f : 0.35f) : 0f;
        _carVis.PipeGlow(five ? Math.Clamp(_car.Boost * 0.9f, 0f, 1f) : 0f);
        _snd.Grade = EngineFuel == FuelGrade.Sputnik ? (int)FuelGrade.Rocket : (int)EngineFuel;      // (the Sputnik has the jet fuel's voice)
        _snd.Boosted = !paused && !Replaying && !ViewerMode && _boostLeft > 0;
        _snd.Rpm = paused ? _params.IdleRpm : _car.Rpm;
        _snd.Throttle = paused ? 0 : _car.Throttle;
        _snd.Slide = paused ? 0 : _car.Slide;
        _snd.Loose = _car.LooseSurface;
        _snd.Ice = _car.OnIce;
        _snd.Enclosure=paused?0:_track.MaintenanceHall?.Enclosure(new(_car.Pos.X,_car.Pos.Z)) ?? 0;
        _snd.Ground = _car.GroundSurface ?? _snd.Ground;      // in the air: as it was
        _snd.Scrape = paused ? 0 : _car.BodyScrape;
        _snd.Limiter = !paused && _car.Limiter;
        _snd.Speed = paused ? 0 : _car.Speed;
        _snd.Drivetrain(_car.Gear, _car.Clutch, _car.Neutral, _car.Throttle, _car.Rpm,
            !paused && _collisionQuiet <= 0 && (_state == State.Racing || Replaying));
        _snd.Gear = _car.Gear;
        _snd.Driven = _car.Neutral || _car.Clutch ? 0f : 1f;
        _snd.Airborne = _car.AirTime > 0.1f;
        // Brakes squeal hardest at speed and give one last "iik" as the car stops.
        float brakeSpeed = Math.Clamp((_car.Speed - 1f) / 9f, 0f, 1f);
        bool honk = !paused && _state is State.Countdown or State.Racing or State.Failed or State.Finished
                    && !_watching && !_showBoard && !_showLog && !_showKeys && !_gulag && !_planScreen && (_driver.Read().Horn && SchoolLetsHonk || _sc.Horn);
        // In the school one honk can end the step on the same tick (the mechanic wakes at once): it still sounds.
        _honkLeft = honk && InSchool ? 0.6f : Math.Max(0f, _honkLeft - dt);
        _horn.Pressed = honk || !paused && _honkLeft > 0f
                        || _state == State.Racing && _hornAt >= 0 && _raceClock >= _hornAt && _raceClock < _hornAt + _hornHold;
        _snd.BrakeSqueal = paused || _state == State.Countdown ? 0 : Math.Max(_car.Brake, _car.Handbrake * 0.5f) * brakeSpeed;
    }

    void Screenshot()
    {
        if (_recordPath != null && _clock >= _shotAt)
        {
            _recorder.SetRecordingActive(false);
            var wav = _recorder.GetRecording();
            wav?.SaveToWav(_recordPath);
            GD.Print(wav == null ? "No audio recorded" : $"Audio saved to {_recordPath}");
            _recordPath = null;
            if (_shotPath == null) CCCPRacing.Audio.AudioMix.Quit(this);
        }
        if (_shotPath == null || _clock < _shotAt) return;
        GetViewport().GetTexture().GetImage().SavePng(_shotPath);
        GetTree().Root.GetTexture().GetImage().SavePng(_shotPath.Replace(".png", "_full.png"));
        GD.Print($"Screenshot saved to {_shotPath}");
        _shotPath = null;
        CCCPRacing.Audio.AudioMix.Quit(this);
    }
}
