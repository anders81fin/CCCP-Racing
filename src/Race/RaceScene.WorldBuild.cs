using System;
using System.Linq;
using System.Threading.Tasks;
using CCCPRacing.Audio;
using CCCPRacing.Camera;
using CCCPRacing.Car;
using CCCPRacing.Track;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// The phases of RaceScene.BuildWorld, in construction order. What crosses a phase boundary is
/// only the track definition, the props node and, after the command line, the start intent. The actors' 13 obstacle counters stay local to BuildActors (they are read
/// together by KeepWorld). The tail of the build (files, mode entry, LAN, wiring, day/night, dispatch) stays in BuildWorld.
/// </summary>
public partial class RaceScene
{
    /// <summary>What the command line asked for, read by the end of the build (the old locals start, training, watch, board and intro).</summary>
    readonly record struct StartIntent(bool Start, bool Training, bool Watch, bool Board, bool Intro);

    TrackDef BuildBoot()
    {
        _booting = ViewerMode || LanPlaybackSource!=null || TrackLoadingScreen.Active != null || OS.HasEnvironment("FORCEBOOT");   // FORCEBOOT: test the loading path headless; a viewer always builds in slices
        _loadingWorkSlice.Restart();
        LoadStepsBegin();      // RaceScene.LoadProgress.cs
        LoadProf.Start();   // LOADPROF
        InputSetup.Ensure();
        AudioMix.Setup(this);   // Master limiter: nothing ever clips

        var def = TrackDef.Load($"res://tracks/{TrackToBuild()}.json");   // RaceScene.Tracks.cs
        NormPlan.UseTrack(def.Norms);                                      // the track's norms and medals
        return def;
    }

    async Task<Node3D> BuildGround(TrackDef def)
    {
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
        return props;
    }

    void BuildEnvironment()
    {
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
    }

    async Task BuildCarAndWorldKit(TrackDef def, Node3D props)
    {
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
    }

    async Task BuildActors(TrackDef def, Node3D props)
    {
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
            _slurryAll.Fx = _fx;
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
    }

    async Task BuildCameraHudAudioTimer()
    {
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
    }

    StartIntent ApplyCommandLine()
    {
        bool start = false, training = false, watch = false, board = false, intro = true;
        foreach (string a in InstructionTrack == null ? CmdArgs() : Array.Empty<string>())
        {
            if (a == "--start") start = true;
            else if (a.StartsWith("--import-take=")) _importTakeDir = a["--import-take=".Length..];      // RaceScene.TakeImport.cs: a title take goes into the lap archive, then the game quits
            else if (a.StartsWith("--title-rec="))
            {
                _titleRec = true; start = true; intro = false; _noRecord = true;      // (never a record: the take goes to its own folder)
                _tapeSessionLapsDir = a["--title-rec=".Length..]; _tapeQuitLaps = 1;
                _rallyFuel = FuelGrade.Diesel; _powerStart = Training.PercentForKey(1); _trackId = "ilyich";
            }
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
        return new StartIntent(start, training, watch, board, intro);
    }
}
