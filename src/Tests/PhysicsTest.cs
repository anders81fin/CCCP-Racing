using System;
using System.Linq;
using CCCPRacing.Car;
using CCCPRacing.Race;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

/// <summary>
/// Headless sanity checks for the car model and track.
/// Run: godot-mono --headless --path . res://scenes/tests/PhysicsTest.tscn
/// </summary>
public partial class PhysicsTest : Node
{
    const float Dt = 1f / 240f;
    int _failures;

    class FlatGround : IGround
    {
        public float SlurryWetness;
        public float OilFilm;
        public SurfaceType Surface = SurfaceType.Concrete;
        public bool Verge;                 // optional: the verge strip beside the road
        public float BumpX = float.NaN;    // optional 0.3 m step across the road at this X
        public Vector2? Hole;              // optional pothole centre
        public float PuddleX = float.NaN;  // optional 6 m puddle under the left wheels (z < 0) from this X
        float H(float x, float z) => Hole is Vector2 h ? Pothole.Height(x - h.X, z - h.Y, 3, 7) : 0f;
        public GroundSample Sample(float x, float z)
        {
            bool inHole = Hole is Vector2 c && new Vector2(x - c.X, z - c.Y).Length() < 1.3f;
            bool inPuddle = !float.IsNaN(PuddleX) && x > PuddleX && x < PuddleX + 6f && z < 0f;
            const float e = 0.05f;
            return new()
            {
                Height = (!float.IsNaN(BumpX) && x > BumpX && x < BumpX + 0.6f ? 0.3f : 0f) + H(x, z) + (inPuddle ? -0.08f : 0f),
                Normal = Vector3.Up,
                Slope = inHole ? new Vector2((H(x + e, z) - H(x - e, z)) / (2 * e), (H(x, z + e) - H(x, z - e)) / (2 * e)) : Vector2.Zero,
                SlurryWetness = SlurryWetness, OilFilm=OilFilm,
                Surface = inHole ? SurfaceType.Pothole : inPuddle ? SurfaceType.Puddle : Surface,
                Verge = Verge,
            };
        }
    }

    public override void _Ready()
    {
        if(Array.Exists(OS.GetCmdlineUserArgs(),a=>a=="--only-kgb")){KgbTest();return;}
        var kozlikReview=Array.Find(OS.GetCmdlineUserArgs(),a=>a.StartsWith("--kozlik-scale-review="));
        if(kozlikReview!=null){KozlikScaleReview(kozlikReview["--kozlik-scale-review=".Length..]);return;}
        var ridgeReview=Array.Find(OS.GetCmdlineUserArgs(),a=>a.StartsWith("--ridge-review="));
        if(ridgeReview!=null){RidgeReview(ridgeReview["--ridge-review=".Length..]);return;}
        var roadReview = Array.Find(OS.GetCmdlineUserArgs(), a => a.StartsWith("--road-review="));
        if(roadReview!=null){RoadReview(roadReview["--road-review=".Length..]);return;}
        var meadowReview = Array.Find(OS.GetCmdlineUserArgs(), a => a.StartsWith("--meadow-review="));
        if(meadowReview!=null){MeadowReview(meadowReview["--meadow-review=".Length..]);return;}
        var riverReview = Array.Find(OS.GetCmdlineUserArgs(), a => a.StartsWith("--ilyich-river-review="));
        if (riverReview != null) { RiverReview(riverReview["--ilyich-river-review=".Length..]); return; }
        var reviewPath = Array.Find(OS.GetCmdlineUserArgs(), arg => arg.StartsWith("--model-review="));
        if (reviewPath != null) { ModelReview(reviewPath["--model-review=".Length..]); return; }
        var legacyPath = Array.Find(OS.GetCmdlineUserArgs(), arg => arg.StartsWith("--legacy-damage-model-test="));
        if (legacyPath != null)
        { try { LegacyDamageModelTest(legacyPath["--legacy-damage-model-test=".Length..]); } catch (Exception e) { GD.PrintErr(e); _failures++; } GetTree().Quit(_failures == 0 ? 0 : 1); return; }
        var restPath = Array.Find(OS.GetCmdlineUserArgs(), arg => arg.StartsWith("--damage-rest-reference="));
        if (restPath != null)
        { try { DamageRestReferenceTest(restPath["--damage-rest-reference=".Length..]); } catch (Exception e) { GD.PrintErr(e); _failures++; } GetTree().Quit(_failures == 0 ? 0 : 1); return; }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--leaderboard-replay-tests"))
        { try { LeaderboardReplayTests(); } catch (Exception e) { GD.PrintErr(e); _failures++; } GetTree().Quit(_failures == 0 ? 0 : 1); return; }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--leaderboard-tests"))
        { LeaderboardUiTests(); return; }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--camera-motion-tests"))
        { try { CameraMotionTests(); } catch (Exception e) { GD.PrintErr(e); _failures++; } GetTree().Quit(_failures == 0 ? 0 : 1); return; }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--free-camera-tests"))
        { try { FreeCameraTests(); } catch (Exception e) { GD.PrintErr(e); _failures++; } GetTree().Quit(_failures == 0 ? 0 : 1); return; }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--free-camera-key-tests"))
        { try { FreeCameraKeyTests(); } catch (Exception e) { GD.PrintErr(e); _failures++; } GetTree().Quit(_failures == 0 ? 0 : 1); return; }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--free-camera-export-contract-tests"))
        { try { FreeCameraExportContractTests(); } catch (Exception e) { GD.PrintErr(e); _failures++; } GetTree().Quit(_failures == 0 ? 0 : 1); return; }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--lab-tests"))
        { try { LabTests(); } catch (Exception e) { GD.PrintErr(e); _failures++; } GetTree().Quit(_failures == 0 ? 0 : 1); return; }
        if (ViewerWindowedChildHook(this)) return;      // viewer windowed: a child game (RaceScene.ViewerWindowedTest.cs)
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--viewer-windowed-tests")) { try { ViewerWindowedTests(); } catch (Exception e) { GD.PrintErr(e); _failures++; } GetTree().Quit(_failures == 0 ? 0 : 1); return; }      // viewer windowed
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--resource-criterion-tests")) { ResourceCriterionTests(); return; }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--lan-export-tests")) { LanExportTests(); return; }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--replay-export-tests")) { ReplayExportTests(); return; }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--replay-ui-tests")) { ReplayUiTests(); return; }      // replay UI (draw only)
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--menu-ui-tests")) { MenuUiTests(); return; }      // Party Meeting screens (draw only)
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--batch-equiv-test")) { try { BatchEquivTest(); } catch (Exception e) { GD.PrintErr(e); _failures++; } GetTree().Quit(_failures == 0 ? 0 : 1); return; }      // batch merge equivalence
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--lap-archive-tests"))
        {
            try { LapArchiveTests(); } catch (Exception e) { GD.PrintErr(e); _failures++; }
            GetTree().Quit(_failures == 0 ? 0 : 1); return;
        }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--lap-delete-tests"))
        {
            try { LapArchiveDeleteTests(); } catch (Exception e) { GD.PrintErr(e); _failures++; }
            GetTree().Quit(_failures == 0 ? 0 : 1); return;
        }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--lap-edits-tests"))
        {
            try { LapEditsTests(); } catch (Exception e) { GD.PrintErr(e); _failures++; }
            GetTree().Quit(_failures == 0 ? 0 : 1); return;
        }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--lap-playback-tests"))
        {
            try { LapPlaybackTests(); } catch (Exception e) { GD.PrintErr(e); _failures++; }
            GetTree().Quit(_failures == 0 ? 0 : 1); return;
        }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--lap-archive-scene-tests"))
        {
            try { LapArchiveSceneTests(); } catch (Exception e) { GD.PrintErr(e); _failures++; }
            GetTree().Quit(_failures == 0 ? 0 : 1); return;
        }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--viewer-spike-tests"))
        {
            try { ViewerSpikeTests(); } catch (Exception e) { GD.PrintErr(e); _failures++; }
            GetTree().Quit(_failures == 0 ? 0 : 1); return;
        }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--viewer-isolation-tests"))
        {
            try { ViewerIsolationTests(); } catch (Exception e) { GD.PrintErr(e); _failures++; }
            GetTree().Quit(_failures == 0 ? 0 : 1); return;
        }
        if (System.Linq.Enumerable.FirstOrDefault(OS.GetCmdlineUserArgs(), arg => arg.StartsWith("--viewer-isolation-case=")) is { } isoCase)
        { ViewerIsolationCase(isoCase["--viewer-isolation-case=".Length..]); return; }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--viewer-freeze-tests"))
        {
            try { ViewerFreezeTests(); } catch (Exception e) { GD.PrintErr(e); _failures++; }
            GetTree().Quit(_failures == 0 ? 0 : 1); return;
        }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--viewer-lifecycle-tests"))
        {
            try { ViewerLifecycleTests(); } catch (Exception e) { GD.PrintErr(e); _failures++; }
            GetTree().Quit(_failures == 0 ? 0 : 1); return;
        }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--viewer-fidelity-tests"))
        {
            try { ViewerFidelityTests(); } catch (Exception e) { GD.PrintErr(e); _failures++; }
            GetTree().Quit(_failures == 0 ? 0 : 1); return;
        }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--rescue-rope-tape-tests"))
        {
            try { RescueRopeTapeTests(); } catch (Exception e) { GD.PrintErr(e); _failures++; }
            GetTree().Quit(_failures == 0 ? 0 : 1); return;
        }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--fuel-tape-scene-tests"))
        {
            try { FuelTapeSceneTests(); } catch (Exception e) { GD.PrintErr(e); _failures++; }
            GetTree().Quit(_failures == 0 ? 0 : 1); return;
        }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--default-tape-camera-tests"))
        {
            try { DefaultTapeCameraTests(); } catch (Exception e) { GD.PrintErr(e); _failures++; }
            GetTree().Quit(_failures == 0 ? 0 : 1); return;
        }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--octane-tape-scene-tests"))
        {
            try { OctaneTapeSceneTests(); } catch (Exception e) { GD.PrintErr(e); _failures++; }
            GetTree().Quit(_failures == 0 ? 0 : 1); return;
        }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--octane-tape-tests"))
        {
            try { InstallRealTape(); OctaneTapeTests(); } catch (Exception e) { GD.PrintErr(e); _failures++; }
            GetTree().Quit(_failures == 0 ? 0 : 1); return;
        }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--fuel-tape-tests"))
        {
            try { InstallRealTape(); TapeT15(); FuelTapeTests(); } catch (Exception e) { GD.PrintErr(e); _failures++; }
            GetTree().Quit(_failures == 0 ? 0 : 1); return;
        }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--gai-gaze-scene-tests"))
        {
            try { GaiGazeSceneTests(); } catch (Exception e) { GD.PrintErr(e); _failures++; }
            GetTree().Quit(_failures == 0 ? 0 : 1); return;
        }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--gai-gaze-tests"))
        {
            try { GaiGazeTests(); } catch (Exception e) { GD.PrintErr(e); _failures++; }
            GetTree().Quit(_failures == 0 ? 0 : 1); return;
        }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--replay-detail-tests"))
        {
            try { ReplayDetailTests(); } catch (Exception e) { GD.PrintErr(e); _failures++; }
            GetTree().Quit(_failures == 0 ? 0 : 1); return;
        }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--idle-scene-tests"))
        { GetTree().Root.CallDeferred(Node.MethodName.AddChild, new IdleAudioSceneTest()); return; }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--pad-tests"))
        { PadTest(); GetTree().Quit(_failures == 0 ? 0 : 1); return; }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--idle-tests"))
        { IdleTest(); GetTree().Quit(_failures == 0 ? 0 : 1); return; }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--damage-tape-tests"))
        { DamageTapeTest(); GetTree().Quit(_failures == 0 ? 0 : 1); return; }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--collision-feedback-tests"))
        { CollisionFeedbackTest(); GetTree().Quit(_failures == 0 ? 0 : 1); return; }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--replay-camera-tests"))
        { ReplayCameraTest(); GetTree().Quit(_failures == 0 ? 0 : 1); return; }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--atomgrad-terrain-tests"))
        { AtomgradTerrainTest(); GetTree().Quit(_failures == 0 ? 0 : 1); return; }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--snowplough-turn-tests"))
        { SnowPloughTurnTest(); GetTree().Quit(_failures == 0 ? 0 : 1); return; }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--siberia-terrain-tests"))
        { SiberiaTerrainTest(); GetTree().Quit(_failures == 0 ? 0 : 1); return; }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--ground-engine-tests"))
        { GroundEngineSoundTest();GetTree().Quit(_failures==0?0:1);return; }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--whine-balance-tests"))
        { WhineBalanceTest();GetTree().Quit(_failures==0?0:1);return; }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--engine-character-tests"))
        { EngineCharacterTest();GetTree().Quit(_failures==0?0:1);return; }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--handbrake-throttle-tests"))
        { HandbrakeThrottleTest();GetTree().Quit(_failures==0?0:1);return; }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--start-hill-assist-tests"))
        { StartHillAssistTest();GetTree().Quit(_failures==0?0:1);return; }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--military-motion-tests"))
        { MilitaryMotionTest();GetTree().Quit(_failures==0?0:1);return; }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--drone-tour-tests"))
        { DroneTourTest();GetTree().Quit(_failures==0?0:1);return; }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--mechanic-tests"))
        { MechanicRepairTest();GetTree().Quit(_failures==0?0:1);return; }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--ghost-bytes-worker-tests"))
        { GhostBytesWorkerTests();GetTree().Quit(_failures==0?0:1);return; }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--tape-tests"))
        { TapeTests();GetTree().Quit(_failures==0?0:1);return; }
        if (Array.Find(OS.GetCmdlineUserArgs(), a => a.StartsWith("--print-setting=")) is { } ps)
        { string name = ps["--print-setting=".Length..]; GD.Print($"SETTING {name} = {(ProjectSettings.HasSetting(name) ? ProjectSettings.GetSetting(name).ToString() : "<not set>")}");GetTree().Quit();return; }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--norm-card-tests"))
        { NormCardTest();GetTree().Quit(_failures==0?0:1);return; }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--pause-rows-tests"))
        { PauseRowsTest();GetTree().Quit(_failures==0?0:1);return; }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--tape-recycle-tests"))
        { TapeRecycleTests();GetTree().Quit(_failures==0?0:1);return; }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--tape-scene-tests"))
        { TapeSceneTests();GetTree().Quit(_failures==0?0:1);return; }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--tape-mutation-tests"))
        { TapeMutationTests();GetTree().Quit(_failures==0?0:1);return; }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--maintenance-hall-tests"))
        { MaintenanceHallTest();GetTree().Quit(_failures==0?0:1);return; }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--military-light-tests"))
        { MilitaryNightLightsTest();GetTree().Quit(_failures==0?0:1);return; }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--poligon-tests"))
        { PoligonTest();BtrPatrolTest();CampGateTest();BonusPerimeterTest();TrainingLaps();LanBallotTest();GetTree().Quit(_failures==0?0:1);return; }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--hind-impact-tests"))
        { HindImpactTest();GetTree().Quit(_failures==0?0:1);return; }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--bonus-perimeter-tests"))
        { BonusPerimeterTest();GetTree().Quit(_failures==0?0:1);return; }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--bonus-soldier-tests"))
        { BonusSoldierContactTest();GetTree().Quit(_failures==0?0:1);return; }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--bonus-polish-tests"))
        { BonusPolishTest();GetTree().Quit(_failures==0?0:1);return; }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--lan-authority-tests"))
        {
            LanBallotTest(); LanAuthorityTest(); LanSnapshotTest(); LanGridLapsTest();
            GetTree().Quit(_failures==0?0:1);return;
        }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--lan-replay-access-tests"))
        {LanReplayAccessTest();GetTree().Quit(_failures==0?0:1);return;}
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--hind-exercise-tests"))
        { HindExerciseTest();GetTree().Quit(_failures==0?0:1);return; }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--hind-tests"))
        {
            HindAirbaseTest();
            GetTree().Quit(_failures == 0 ? 0 : 1);
            return;
        }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--tank-tests"))
        {
            RevengeTest();
            GetTree().Quit(_failures == 0 ? 0 : 1);
            return;
        }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--handling-tests"))
        {
            Settle();
            OffRoadSurfaces();
            WinterSurfaces();
            SnowbankHits();
            Cornering();
            Bump();
            PotholeHit();
            PuddleHit();
            TurnResponse(SurfaceType.Concrete, 32f);
            TurnResponse(SurfaceType.Gravel, 31f);
            SlideRecovery();
            PowerOnGravel();
            DriftSpeed();
            foreach (var surface in new[] { SurfaceType.Concrete, SurfaceType.Gravel, SurfaceType.Grass, SurfaceType.Mud })
                HoldFullLock(surface);
            Rollover();
            OverdriveOnTrack();
            PadMappingTest();
            PadSteerRateTest();
            SpinTest();
            SlideTechniquesTest();
            GetTree().Quit(_failures == 0 ? 0 : 1);
            return;
        }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--fast-steering-tests"))
        {
            FastSteeringTest();
            GetTree().Quit(_failures == 0 ? 0 : 1);
            return;
        }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--power-corner-tests"))
        {
            PowerCornerTest();
            GetTree().Quit(_failures == 0 ? 0 : 1);
            return;
        }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--braking-tests"))
        {
            BrakingBalanceTest();
            BrakeLockup(SurfaceType.Concrete);
            BrakeLockup(SurfaceType.Gravel);
            GetTree().Quit(_failures == 0 ? 0 : 1);
            return;
        }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--attack-tests"))
        {
            MaximumAttackTest();
            TractionBiteTest();
            AirborneEngineTest();
            GetTree().Quit(_failures == 0 ? 0 : 1);
            return;
        }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--traction-tests"))
        {
            TractionBiteTest();
            AirborneEngineTest();
            StartLineRev();
            GetTree().Quit(_failures == 0 ? 0 : 1);
            return;
        }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--gearbox-analysis"))
        {
            TrackGearboxAnalysis();
            GetTree().Quit(_failures == 0 ? 0 : 1);
            return;
        }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--drivetrain-tests"))
        {
            Acceleration();
            StartLineRev();
            ClutchCoast();
            ClutchDump();
            AirborneEngineTest();
            RallyGearUsageTest();
            OverdriveTest();
            GetTree().Quit(_failures == 0 ? 0 : 1);
            return;
        }
        if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--rally-resume-tests"))
        {
            RallyResumeTest();
            GetTree().Quit(_failures == 0 ? 0 : 1);
            return;
        }
        if (OS.HasEnvironment("MESSAGELAB") || Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--message-tests"))
        {
            MessageRuleTest();
            GetTree().Quit(_failures == 0 ? 0 : 1);
            return;
        }
        if (OS.HasEnvironment("TRACTORLAB"))
        {
            TractorResetCollisionTest();
            GetTree().Quit(_failures == 0 ? 0 : 1);
            return;
        }
        if (OS.HasEnvironment("EDGELAB"))
        {
            IlyichEdgesTest();
            GetTree().Quit(_failures == 0 ? 0 : 1);
            return;
        }
        if (OS.HasEnvironment("DISCOVERYLAB"))
        {
            CameraDiscoveriesTest();
            BonusMigrationTest();
            GetTree().Quit(_failures == 0 ? 0 : 1);
            return;
        }
        if (OS.HasEnvironment("SERVICESLAB"))
        {
            AtomgradServicesTest();
            GetTree().Quit(_failures == 0 ? 0 : 1);
            return;
        }
        if (OS.HasEnvironment("CAMERALAB"))
        {
            GameplayCameraTest();
            GetTree().Quit(_failures == 0 ? 0 : 1);
            return;
        }
        if (OS.HasEnvironment("VIEWLAB"))
        {
            BuildingVisibilityTest();
            GetTree().Quit(_failures == 0 ? 0 : 1);
            return;
        }
        if (OS.HasEnvironment("GULAGLAB"))
        {
            GulagWalkingTest();
            SchoolFramingTest();
            KozlikSteeringTest();
            GetTree().Quit(_failures == 0 ? 0 : 1);
            return;
        }
        if (System.Environment.GetEnvironmentVariable("SCHOOLLAB") != null)
        {
            SchoolTest();     // the driving school's lab and checks only
            GetTree().Quit(_failures == 0 ? 0 : 1);
            return;
        }
        if (OS.HasEnvironment("BACKDROPLAB"))
        {
            WorldBackdropTest();
            GetTree().Quit(_failures == 0 ? 0 : 1);
            return;
        }
        if (OS.HasEnvironment("DACHALAB"))
        {
            PartyDachaTest();
            BuildingVisibilityTest();
            KolkhozWorkersTest();
            VillagePowerTest();
            SlurryTest();
            GetTree().Quit(_failures == 0 ? 0 : 1);
            return;
        }
        if (OS.HasEnvironment("SLURRYLAB"))
        {
            SlurryTest();
            GetTree().Quit(_failures == 0 ? 0 : 1);
            return;
        }
        if (OS.HasEnvironment("TRAFFICLAB"))
        {
            VehicleTrafficTest();
            GetTree().Quit(_failures == 0 ? 0 : 1);
            return;
        }
        if (OS.HasEnvironment("WORKERLAB"))
        {
            KolkhozWorkersTest();
            GetTree().Quit(_failures == 0 ? 0 : 1);
            return;
        }
        if (OS.HasEnvironment("POWERLAB"))
        {
            VillagePowerTest();
            SiberiaPowerTest();
            GetTree().Quit(_failures == 0 ? 0 : 1);
            return;
        }
        if (OS.HasEnvironment("RAILYARDLAB"))
        {
            AtomgradRailYardTest();
            GetTree().Quit(_failures == 0 ? 0 : 1);
            return;
        }
        if (OS.HasEnvironment("SIBERIADETAILSLAB"))
        {
            SiberiaDetailsTest();
            GetTree().Quit(_failures == 0 ? 0 : 1);
            return;
        }
        if (OS.HasEnvironment("SLIDEPROBE")) { ProbeSlides(); return; }   // the slide techniques measured (PhysicsTest.Probe.cs)
        if (OS.HasEnvironment("ILYICHLAB")) { IlyichLab(); return; }   // the Ilyich's Lamp school's bends (PhysicsTest.IlyichLab.cs)
        if (OS.HasEnvironment("SOUNDGRADES")) { SoundGradesRender(); GetTree().Quit(0); return; }
        if (OS.HasEnvironment("TURBOLAB")) { TurboLab(); GetTree().Quit(0); return; }
        if (OS.HasEnvironment("FUELLAPS")) { FuelLapsLab(); GetTree().Quit(0); return; }
        if (OS.HasEnvironment("FUELTEST")) { FuelTest(); GetTree().Quit(_failures == 0 ? 0 : 1); return; }   // the tank alone (PhysicsTest.Fuel.cs)
        if (OS.HasEnvironment("WATERLAB")) { WaterEntryTest(); GetTree().Quit(_failures == 0 ? 0 : 1); return; }   // the car into water (PhysicsTest.WaterEntry.cs)
        if (OS.HasEnvironment("FUELLAB")) { FuelGradesTest(); GetTree().Quit(_failures == 0 ? 0 : 1); return; }   // the rally's fuels (PhysicsTest.FuelGrades.cs)
        if (OS.HasEnvironment("PACELAB")) { PaceLab(); GetTree().Quit(_failures == 0 ? 0 : 1); return; }   // the co-driver's bends (PhysicsTest.PaceNotes.cs)
        if (OS.HasEnvironment("CRUISEWAV")) { CruiseWav(OS.GetEnvironment("CRUISEWAV")); GetTree().Quit(); return; }   // a steady drive on each ground (PhysicsTest.CruiseSound.cs)
        if (OS.HasEnvironment("SKIDWAV")) { SkidWav(OS.GetEnvironment("SKIDWAV")); GetTree().Quit(); return; }   // the tyres' sound on each ground (PhysicsTest.SkidSound.cs)
        RearWingTest();
        WorldBackdropTest();
        AtomgradRailYardTest();
        AtomgradServicesTest();
        SiberiaDetailsTest();
        Settle();
        Acceleration();
        StartLineRev();
        ClutchCoast();
        ClutchDump();
        AirborneEngineTest();
        Launch(SurfaceType.Gravel);
        Launch(SurfaceType.Mud);
        OffRoadSurfaces();
        WinterSurfaces();
        SnowbankHits();
        BrakeLockup(SurfaceType.Concrete);
        BrakeLockup(SurfaceType.Gravel);
        Cornering();
        Bump();
        PotholeHit();
        PuddleHit();
        TurnResponse(SurfaceType.Concrete, 32f);
        TurnResponse(SurfaceType.Gravel, 31f);
        SlideRecovery();
        PowerOnGravel();
        DriftSpeed();
        HoldFullLock(SurfaceType.Concrete);
        HoldFullLock(SurfaceType.Gravel);
        HoldFullLock(SurfaceType.Grass);
        HoldFullLock(SurfaceType.Mud);
        Rollover();
        TrackLaps();
        VoidLap();
        TrainingLaps();
        OverdriveTest();
        OverdriveOnTrack();
        EngineHealthTest();
        MechanicRepairTest();
        StartHillAssistTest();
        HandbrakeThrottleTest();
        DroneTourTest();
        GhostRoundTrip();
        LeaderboardTest();
        KgbTest();
        SignatureTest();
        RecordArchiveTest();
        MixLimiterTest();
        RevengeTest();
        HindAirbaseTest();
        HindExerciseTest();
        LapBudgetTest();
        FuelTest();
        WaterEntryTest();
        BreakablesTest();
        LanTest();
        PadTest();
        DayNightTest();
        SchoolTest();
        SpinTest();
        SlideTechniquesTest();      // PhysicsTest.Probe.cs
        LinesTest();                // PhysicsTest.Lines.cs
        VillagePowerTest();
        SiberiaPowerTest();
        KolkhozWorkersTest();
        PartyDachaTest();
        BuildingVisibilityTest();
        GameplayCameraTest();
        PlayerFilesTest();
        CameraDiscoveriesTest();
        BonusMigrationTest();
        SlurryTest();
        MessageRuleTest();
        RallyResumeTest();
        FordTest();
        GD.Print(_failures == 0 ? "ALL TESTS PASSED" : $"{_failures} TEST(S) FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    /// <summary>The Master bus gets exactly one limiter below full scale, however often it is set up.</summary>
    void MixLimiterTest()
    {
        CCCPRacing.Audio.AudioMix.Setup(null);
        CCCPRacing.Audio.AudioMix.Setup(null);
        int master = AudioServer.GetBusIndex("Master"), n = 0;
        float ceiling = 0f;
        for (int i = 0; i < AudioServer.GetBusEffectCount(master); i++)
            if (AudioServer.GetBusEffect(master, i) is AudioEffectHardLimiter l) { n++; ceiling = l.CeilingDb; }
        Check(n == 1 && ceiling <= -0.9f, $"one Master limiter, ceiling {ceiling:F1} dBFS");
    }

    void Check(bool ok, string what)
    {
        GD.Print($"  [{(ok ? "ok" : "FAIL")}] {what}");
        if (!ok) _failures++;
    }

    static CarSim NewCar(IGround g, CarParams p = null)
    {
        var car = new CarSim(p ?? new CarParams(), g) { WorldMin = -1e6f, WorldMax = 1e6f };
        car.Reset(new Vector3(0, 0.8f, 0), 0);
        return car;
    }

    static void Run(CarSim car, float seconds, Action each = null)
    {
        for (float t = 0; t < seconds; t += Dt)
        {
            each?.Invoke();
            car.Step(Dt);
        }
    }

    static float RollDeg(CarSim c) => Mathf.RadToDeg(MathF.Asin(Math.Clamp(c.Right.Y, -1, 1)));

    void Settle()
    {
        GD.Print("Settle on flat concrete");
        var car = NewCar(new FlatGround());
        Run(car, 3f);
        float sum = 0;
        foreach (var w in car.Wheels) sum += w.Load;
        GD.Print($"  CG height {car.Pos.Y:F3} m, loads {car.Wheels[0].Load:F0} {car.Wheels[1].Load:F0} {car.Wheels[2].Load:F0} {car.Wheels[3].Load:F0}");
        Check(Math.Abs(sum - 1000 * 9.81f) < 500, $"total load ≈ weight ({sum:F0} N)");
        Check(car.Up.Y > 0.995f && car.Speed < 0.05f, "stands still and level");
    }

    void Acceleration()
    {
        GD.Print("Full throttle, flat concrete");
        var car = NewCar(new FlatGround());
        Run(car, 1f);
        float t50 = -1, t100 = -1, t = 0, limiterTime = 0;
        int gear = car.Gear;
        var shifts = new System.Collections.Generic.List<string>();
        var limiterBeforeShift = new System.Collections.Generic.List<float>();
        Run(car, 20f, () =>
        {
            car.SetControls(true, false, 0, false);
            t += Dt;
            if (t50 < 0 && car.Speed > 50f / 3.6f) t50 = t;
            if (t100 < 0 && car.Speed > 100f / 3.6f) t100 = t;
            if (car.Limiter) limiterTime += Dt;
            if (car.Gear != gear)
            {
                shifts.Add($"{gear}->{car.Gear} @{car.Speed * 3.6f:F0} km/h after {limiterTime:F2} s on the limiter");
                limiterBeforeShift.Add(limiterTime);
                limiterTime = 0;
                gear = car.Gear;
            }
        });
        foreach (var sh in shifts) GD.Print("  " + sh);
        Check(limiterBeforeShift.Count == car.P.Gears.Length - 1 && limiterBeforeShift.TrueForAll(x => x > 0.15f), "flat out: rattles the limiter before every upshift");
        GD.Print($"  0-50 km/h {t50:F2} s, 0-100 km/h {t100:F1} s, speed after 20 s {car.Speed * 3.6f:F0} km/h, gear {car.Gear}");
        Check(t100 > 0 && t100 < 9f, "0-100 under 9 s (power: ACCORDING TO PLAN)");
        Check(car.Speed * 3.6f is > 130 and < 230, "top speed sane");
        Check(Math.Abs(car.Pos.X) < 2f, "drives straight");
    }

    void Launch(SurfaceType surface)
    {
        var car = NewCar(new FlatGround { Surface = surface });
        Run(car, 1f);
        float t50 = -1, t = 0;
        int spinSteps = 0, steps = 0;
        Run(car, 8f, () =>
        {
            car.SetControls(true, false, 0, false);
            t += Dt;
            steps++;
            if (car.Wheels[2].Spinning || car.Wheels[3].Spinning) spinSteps++;
            if (t50 < 0 && car.Speed > 50f / 3.6f) t50 = t;
        });
        GD.Print($"Launch on {surface}: 0-50 km/h {t50:F2} s, wheelspin {100f * spinSteps / steps:F0}% of 8 s, speed after 8 s {car.Speed * 3.6f:F0} km/h");
    }

    /// <summary>Off the road: flat out for 20 s (speed after 8 s and 20 s), and a fixed-steer bend at 20 m/s: speed lost and radius.</summary>
    void OffRoadSurfaces()
    {
        GD.Print("Off the road (flat out: 0-50, speed after 8 s and 20 s; half lock at 20 m/s for 4 s: speed kept)");
        foreach (var (surface, verge) in new[] { (SurfaceType.Concrete, false), (SurfaceType.Dirt, true), (SurfaceType.Gravel, false), (SurfaceType.Dirt, false), (SurfaceType.Grass, false), (SurfaceType.Field, false), (SurfaceType.DeadGrass, false) })
        {
            var car = NewCar(new FlatGround { Surface = surface, Verge = verge });
            Run(car, 1f);
            float t50 = -1, t = 0, v8 = 0;
            Run(car, 20f, () => { car.SetControls(true, false, 0, false); t += Dt; if (t50 < 0 && car.Speed > 50f / 3.6f) t50 = t; if (v8 == 0 && t >= 8f) v8 = car.Speed * 3.6f; });
            float v20 = car.Speed * 3.6f;
            car = NewCar(new FlatGround { Surface = surface, Verge = verge });
            Run(car, 1f);
            car.Vel = car.Forward * 20f;
            Run(car, 4f, () => car.SetControls(true, false, 0.5f, false));
            GD.Print($"  {surface + (verge ? " verge" : ""),-12} 0-50 {(t50 < 0 ? "  - " : $"{t50:F2}")} s, 8 s {v8,3:F0} km/h, 20 s {v20,3:F0} km/h | bend 20 m/s: {car.Speed * 3.6f:F0} km/h after 4 s");
        }
    }

    /// <summary>Ice and snow next to the summer surfaces: launch, a straight stop, the most sideways grip in a slow ramp.</summary>
    void WinterSurfaces()
    {
        GD.Print("Winter surfaces (0-50, after 8 s, stop from 80 km/h straight, peak lateral g at a fixed 40 % steer)");
        var res = new System.Collections.Generic.Dictionary<SurfaceType, (float t50, float v8, float stop, float latG)>();
        foreach (var surface in new[] { SurfaceType.Concrete, SurfaceType.Gravel, SurfaceType.Mud, SurfaceType.Snow, SurfaceType.Ice, SurfaceType.DeepSnow })
        {
            var car = NewCar(new FlatGround { Surface = surface });
            Run(car, 1f);
            float t50 = -1, t = 0;
            Run(car, 8f, () => { car.SetControls(true, false, 0, false); t += Dt; if (t50 < 0 && car.Speed > 50f / 3.6f) t50 = t; });
            float v8 = car.Speed * 3.6f;

            car = NewCar(new FlatGround { Surface = surface });
            Run(car, 1f);
            car.Vel = car.Forward * (80f / 3.6f);
            Vector3 p0 = car.Pos;
            Run(car, 12f, () =>
            {
                car.SetControls(false, car.ForwardSpeed > 0.5f, 0f, false);
                if (car.Wheels[0].Locked || car.Wheels[1].Locked) car.Brake = 0.3f;     // pump like the autopilot
            });
            float stop = car.Pos.DistanceTo(p0);

            // Fixed steering, the throttle creeping up: lateral g = speed x yaw rate, its peak before it lets go.
            car = NewCar(new FlatGround { Surface = surface });
            Run(car, 1f);
            car.Vel = car.Forward * 5f;
            float latG = 0, thr = 0.15f, prevH = Heading(car);
            Run(car, 14f, () =>
            {
                thr = Math.Min(1f, thr + Dt * 0.06f);
                car.SetControls(false, false, 0.4f, false);
                car.Throttle = car.ThrottleTarget = thr;
                float h = Heading(car);
                float yaw = Mathf.Wrap(h - prevH, -MathF.PI, MathF.PI) / Dt;
                prevH = h;
                if (car.Slide < 0.6f) latG = Math.Max(latG, Math.Abs(car.ForwardSpeed * yaw) / 9.81f);
            });
            res[surface] = (t50, v8, stop, latG);
            GD.Print($"  {surface,-9} 0-50 {(t50 < 0 ? "  - " : $"{t50:F2}")} s, 8 s {v8,3:F0} km/h, stop {stop,5:F1} m, lateral {latG:F2} g");
        }
        var c = res[SurfaceType.Concrete]; var g = res[SurfaceType.Gravel]; var sn = res[SurfaceType.Snow]; var ice = res[SurfaceType.Ice]; var ds = res[SurfaceType.DeepSnow];
        Check(ice.stop > g.stop * 1.8f && ice.stop > sn.stop * 1.4f, "ice: the longest stop by far");
        Check(ice.latG < sn.latG && sn.latG < g.latG, "sideways grip: ice < snow < gravel");
        float T(float t50) => t50 < 0 ? 99f : t50;
        Check(T(sn.t50) > T(g.t50) && T(ice.t50) > T(sn.t50) && ice.v8 > 50f, "launch: gravel beats snow beats ice, but ice still gets going");
        Check(ds.v8 < sn.v8 * 0.85f, "deep snow holds the car back");
    }

    /// <summary>On the Siberia start straight: into the right-hand snow bank at 70 km/h at several angles, hands off.</summary>
    void SnowbankHits()
    {
        GD.Print("Snow banks: 70 km/h into the bank, hands off for 3 s");
        var track = TrackBuilder.Build(TrackDef.Load("res://tracks/siberia.json"));
        int i = 40;
        Vector2 c = track.Center[i], dir = track.CenterDir(i);
        var res = new System.Collections.Generic.Dictionary<int, (float speed, float lat, float turned)>();
        foreach (int deg in new[] { 0, 8, 15, 30, 60, 90 })
        {
            var car = new CarSim(new CarParams(), track) { Obstacles = Array.Empty<Obstacle>(), Banks = track.Banks, WorldMax = track.WorldSize - 4f };
            if (deg == 0) car.Banks = null;      // the same coast down the straight with no bank: what snow takes anyway
            // Start on the centre line, 4 m short of the bank's line in the direction of travel.
            float a = Mathf.DegToRad(deg);
            Vector2 right = new(-dir.Y, dir.X);
            Vector2 heading = dir * MathF.Cos(a) + right * MathF.Sin(a);
            float toBank = track.Banks.Inner(i) - 2.5f;
            Vector2 start = c + right * (track.HalfWidth[i] * 0.2f) - heading * (deg >= 60 ? 1f : 0f);
            car.Reset(new Vector3(start.X, track.CenterH[i] + 0.7f, start.Y), TrackData.YawFor(heading));
            Run(car, 0.5f);
            car.Vel = new Vector3(heading.X, 0, heading.Y) * (70f / 3.6f);
            float h0 = Heading(car), hit = 0;
            Run(car, 3f, () => { car.SetControls(false, false, 0f, false); hit = Math.Max(hit, car.SnowbankHit); });
            int k = track.NearestCenter(new Vector2(car.Pos.X, car.Pos.Z), out float d);
            float turned = Mathf.RadToDeg(Mathf.Wrap(Heading(car) - h0, -MathF.PI, MathF.PI));
            res[deg] = (car.Speed * 3.6f, d - track.HalfWidth[k], turned);
            GD.Print($"  {deg,2}°: hit {hit,4:F1} m/s, after 3 s {car.Speed * 3.6f,3:F0} km/h, {(d <= track.HalfWidth[k] ? "on the road" : $"{d - track.HalfWidth[k]:F1} m off it")}, turned {turned:F0}°, toBank {toBank:F1}");
        }
        Check(res[90].speed < 8f, "square into the bank: stopped");
        Check(res[8].speed > res[90].speed + 30f && res[8].lat <= 0.5f, "a shallow brush: back on the road with most of the speed");
        Check(res[15].speed > res[60].speed, "the shallower, the more speed kept");
    }

    void StartLineRev()
    {
        GD.Print("Start line: clutch in, brakes on, floor it");
        var car = NewCar(new FlatGround());
        Run(car, 1f);
        Vector3 p0 = car.Pos;
        float tRed = -1, t = 0;
        int cuts = 0;
        bool wasCut = false;
        Run(car, 1.5f, () =>
        {
            car.Neutral = true;
            car.SetControls(true, false, 0, true);
            car.Brake = 1;
            t += Dt;
            if (tRed < 0 && car.Rpm >= 6900f) tRed = t;
            if (car.IgnitionCut && !wasCut) cuts++;
            wasCut = car.IgnitionCut;
        });
        float moved = car.Pos.DistanceTo(p0);
        float t0 = t;
        Run(car, 1.5f, () =>
        {
            car.SetControls(false, false, 0, true);
            car.Brake = 1;
        });
        GD.Print($"  idle -> limiter in {tRed:F2} s, {cuts} limiter cuts, moved {moved * 100:F1} cm, rpm after release {car.Rpm:F0}");
        Check(tRed > 0 && tRed < 1f, "revs to the limiter in under a second");
        Check(cuts >= 3, "bounces off the rev limiter");
        Check(moved < 0.05f, "car does not creep");
        Check(car.Rpm < 1210f, "drops back to idle when released");      // (the idle is 1200 exactly: the revs rest on it)
    }

    /// <summary>Shift held at speed: the engine free-revs, nothing drives or engine-brakes, the car coasts.</summary>
    void ClutchCoast()
    {
        GD.Print("Clutch in at 80 km/h on concrete, flat out");
        (float speed, float maxRpm, int gear, bool limiter, float v0) Coast(bool clutch)
        {
            var car = NewCar(new FlatGround());
            Run(car, 1f);
            car.Vel = car.Forward * (80f / 3.6f);
            Run(car, 0.5f, () => car.SetControls(true, false, 0, false));
            int gear = car.Gear;
            float v0 = car.Speed * 3.6f;
            float maxRpm = 0;
            bool limiter = false, shifted = false;
            Run(car, 3f, () =>
            {
                car.Clutch = clutch;
                car.SetControls(clutch, false, 0, false);
                maxRpm = Math.Max(maxRpm, car.Rpm);
                limiter |= car.Limiter;
                shifted |= car.Gear != gear;
            });
            return (car.Speed * 3.6f, maxRpm, shifted ? -99 : gear, limiter, v0);
        }
        var c = Coast(true);
        var lift = Coast(false);
        GD.Print($"  clutch in + flat out: {c.v0:F1} -> {c.speed:F1} km/h in 3 s, max rpm {c.maxRpm:F0}, limiter {c.limiter}; lifted off: {lift.v0:F1} -> {lift.speed:F1} km/h");
        Check(c.speed < c.v0 && c.speed > c.v0 - 8f, "no drive: coasts, speed decays slowly");
        Check(c.speed - c.v0 > lift.speed - lift.v0 + 1f, "no engine braking with the clutch in");
        Check(c.limiter && c.maxRpm > 6900f, "engine free-revs against the limiter");
        Check(c.gear != -99, "gearbox does not shift while the clutch is in");
    }

    /// <summary>
    /// Revving on the start line with Shift held across GO, then dumping the clutch on gravel:
    /// the rear wheels break loose; excessive slip trades some launch bite for rotation.
    /// </summary>
    void ClutchDump()
    {
        GD.Print("Clutch dump on gravel vs plain launch");
        (float speed, float spin, bool finite, float rpm02) Launch(bool dump)
        {
            var car = NewCar(new FlatGround { Surface = SurfaceType.Gravel });
            Run(car, 1f);
            if (dump)
            {
                Run(car, 1.5f, () => { car.Neutral = car.Clutch = true; car.SetControls(true, false, 0, true); car.Brake = 1; });
                car.Neutral = false;   // GO, but Shift is still held
                Run(car, 0.3f, () => car.SetControls(true, false, 0, false));
                if (car.Speed > 0.3f) return (-1, 0, false, 0);
                car.Clutch = false;
            }
            float t = 0, rpm02 = 0;
            int spin = 0, steps = 0;
            bool finite = true;
            Run(car, 0.5f, () =>
            {
                car.SetControls(true, false, 0, false);
                t += Dt;
                steps++;
                if (car.Wheels[2].Spinning || car.Wheels[3].Spinning) spin++;
                if (Math.Abs(t - 0.2f) < Dt / 2) rpm02 = car.Rpm;
                finite &= float.IsFinite(car.Vel.X + car.Vel.Y + car.Vel.Z + car.AngVel.Length() + car.Rpm);
            });
            return (car.Speed * 3.6f, (float)spin / steps, finite, rpm02);
        }
        var d = Launch(true);
        var plain = Launch(false);
        GD.Print($"  after 0.5 s: dump {d.speed:F1} km/h, wheelspin {d.spin * 100:F0}%, rpm at 0.2 s {d.rpm02:F0}; plain {plain.speed:F1} km/h, wheelspin {plain.spin * 100:F0}%");
        Check(d.speed >= 0, "holding Shift across GO keeps the clutch in (car stays put)");
        Check(d.finite, "no NaN");
        Check(d.spin > 0.5f, "dump spins the rear wheels");
        // An uncontrolled limiter dump now loses bite instead of receiving a wheelspin force bonus.
        Check(d.spin > plain.spin && d.speed > plain.speed * 0.75f,
            "dump trades launch grip for extra wheelspin without stalling the car");

        // Dump at speed: the kick is capped by tyre grip, never a huge impulse.
        var car = NewCar(new FlatGround { Surface = SurfaceType.Concrete });
        Run(car, 1f);
        car.Vel = car.Forward * (120f / 3.6f);
        Run(car, 1f, () => { car.Clutch = true; car.SetControls(true, false, 0, false); });
        float v0 = car.Speed, maxAcc = 0, prev = car.Speed;
        car.Clutch = false;
        Run(car, 0.5f, () =>
        {
            car.SetControls(true, false, 0, false);
            maxAcc = Math.Max(maxAcc, Math.Abs(car.Speed - prev) / Dt);
            prev = car.Speed;
        });
        GD.Print($"  dump at {v0 * 3.6f:F0} km/h: gear {car.Gear}, peak accel {maxAcc:F1} m/s², {car.Speed * 3.6f:F0} km/h after 0.5 s");
        Check(maxAcc < 15f && float.IsFinite(car.Speed), "dump at speed stays within tyre grip");

        // Releasing below road speed: a jolt of engine braking, then normal driving.
        car = NewCar(new FlatGround());
        Run(car, 1f);
        car.Vel = car.Forward * (80f / 3.6f);
        Run(car, 1f, () => { car.Clutch = true; car.SetControls(false, false, 0, false); });
        float before = car.Speed;
        car.Clutch = false;
        Run(car, 0.3f, () => car.SetControls(false, false, 0, false));
        float jolt = (before - car.Speed) * 3.6f;
        GD.Print($"  clutch let out off-throttle at 80 km/h: gear {car.Gear}, lost {jolt:F1} km/h in 0.3 s, rpm {car.Rpm:F0}");
        Check(jolt > 1f && jolt < 15f, "off-throttle release jolts, gently enough");
    }

    /// <summary>Locked braking retains limited steering; releasing the brake restores authority.</summary>
    void BrakeLockup(SurfaceType surface)
    {
        GD.Print($"Brake modulation at 60 km/h on {surface}");
        (float turned, float lockedShare) Turn(bool brake, bool pump)
        {
            var car = NewCar(new FlatGround { Surface = surface });
            Run(car, 1);
            car.Gear = 3;
            car.Vel = car.Forward * (60 / 3.6f);
            foreach (var wheel in car.Wheels) wheel.SpinSpeed = car.ForwardSpeed / car.P.WheelRadius;
            Vector3 heading = car.Forward;
            float locked = 0, t = 0;
            Run(car, 0.7f, () =>
            {
                bool applied = brake && (!pump || (int)(t / 0.12f) % 2 == 0);
                car.SetControls(0f, applied ? 1f : 0f, 0.4f, false, false);
                if (car.Wheels[0].Locked || car.Wheels[1].Locked) locked += Dt;
                t += Dt;
            });
            return (-Mathf.RadToDeg(heading.SignedAngleTo(car.Forward, Vector3.Up)), locked / t);
        }
        var stomp = Turn(true, false);
        var pump = Turn(true, true);
        var coast = Turn(false, false);
        GD.Print($"  stomp: front lock {stomp.lockedShare:P0}, turn {stomp.turned:F1}; pump: {pump.lockedShare:P0}, {pump.turned:F1}; coast: {coast.turned:F1} deg");
        Check(stomp.lockedShare > 0.5f, "hard braking while turning still locks front tyres");
        // The steering defaults of 2026-11-02 (MaxSteer 0.8) steer so well that on concrete the front locks only 60 % of the time and the stomp turns almost as
        // much as the coast (31 / 33.5 deg): the clear understeer is asked for where the front is locked through (gravel, 99 %), the margin elsewhere is smaller.
        bool full = stomp.lockedShare > 0.9f;
        Check(stomp.turned > 0 && stomp.turned < coast.turned * (full ? 0.85f : 0.97f),
            "locked braking retains limited steering, with clear understeer");
        Check(pump.lockedShare < stomp.lockedShare && (pump.turned > stomp.turned || !full),
            "releasing the brake restores steering authority");
    }

    void Cornering()
    {
        GD.Print("Steady corner at 15 m/s");
        var car = NewCar(new FlatGround());
        Run(car, 1f);
        car.Vel = car.Forward * 15f;
        float maxRoll = 0, minInner = float.MaxValue;
        Run(car, 4f, () =>
        {
            car.SetControls(car.Speed < 15f, false, 0.6f, false);
            maxRoll = Math.Max(maxRoll, Math.Abs(RollDeg(car)));
            minInner = Math.Min(minInner, Math.Min(car.Wheels[1].Load, car.Wheels[3].Load));
        });
        GD.Print($"  max roll {maxRoll:F1}°, min inner wheel load {minInner:F0} N, up.y {car.Up.Y:F2}");
        Check(maxRoll > 3f, "body rolls noticeably (Suspension: NYET)");
        Check(car.Up.Y > 0.8f, "moderate corner does not flip");
    }

    void Bump()
    {
        GD.Print("Hit a 0.3 m kerb at 20 m/s");
        var g = new FlatGround { BumpX = 30f };
        var car = NewCar(g);
        car.Reset(new Vector3(0, 0.8f, 0), -MathF.PI / 2); // facing +X
        Run(car, 1f);
        car.Vel = car.Forward * 20f;
        float maxPitch = 0;
        Run(car, 4f, () =>
        {
            car.SetControls(false, false, 0, false);
            maxPitch = Math.Max(maxPitch, Math.Abs(Mathf.RadToDeg(MathF.Asin(Math.Clamp(car.Forward.Y, -1, 1)))));
        });
        GD.Print($"  max pitch {maxPitch:F1}°, final pos {car.Pos}, up.y {car.Up.Y:F2}");
        Check(!float.IsNaN(car.Pos.X) && car.Pos.X > 40, "survives kerb, keeps going");
    }

    static float SlipDeg(CarSim c)
    {
        float vF = c.ForwardSpeed, vL = c.Vel.Dot(c.Right);
        return Mathf.RadToDeg(MathF.Atan2(Math.Abs(vL), Math.Max(Math.Abs(vF), 0.5f)));
    }

    static float Heading(CarSim c) => MathF.Atan2(-c.Forward.X, -c.Forward.Z);

    void TurnResponse(SurfaceType surface, float minDeg)
    {
        GD.Print($"Turn-in at 20 m/s on {surface}");
        var car = NewCar(new FlatGround { Surface = surface });
        Run(car, 1f);
        car.Vel = car.Forward * 20f;
        float h0 = Heading(car), maxSlip = 0;
        Run(car, 1f, () =>
        {
            car.SetControls(true, false, 1f, false);
            maxSlip = Math.Max(maxSlip, SlipDeg(car));
        });
        float turned = Math.Abs(Mathf.RadToDeg(Mathf.AngleDifference(h0, Heading(car))));
        GD.Print($"  heading change in 1 s: {turned:F0}°, max slip {maxSlip:F0}°, speed {car.Speed * 3.6f:F0} km/h");
        Check(turned > minDeg, $"turns at least {minDeg}° in a second");
        Check(maxSlip < 30f && car.Up.Y > 0.5f, "does not spin or flip on turn-in");
    }

    void SlideRecovery()
    {
        GD.Print("Recover from a slide on gravel, hands off");
        var car = NewCar(new FlatGround { Surface = SurfaceType.Gravel });
        Run(car, 1f);
        car.Vel = car.Forward * 18f + car.Right * 6f;
        car.AngVel = Vector3.Up * 1.2f;
        Run(car, 1.5f, () => car.SetControls(false, false, 0f, false));
        GD.Print($"  after 1.5 s: slip {SlipDeg(car):F1}°, yaw rate {car.AngVel.Y:F2} rad/s");
        Check(SlipDeg(car) < 8f && Math.Abs(car.AngVel.Y) < 0.25f, "slide settles by itself");
    }

    void PowerOnGravel()
    {
        GD.Print("Full throttle + full lock on gravel, then straighten");
        var car = NewCar(new FlatGround { Surface = SurfaceType.Gravel });
        Run(car, 1f);
        car.Vel = car.Forward * 12f;
        float maxSlip = 0;
        Run(car, 2f, () =>
        {
            car.SetControls(true, false, 1f, false);
            if (car.Speed > 8f) maxSlip = Math.Max(maxSlip, SlipDeg(car));
        });
        Run(car, 2f, () => car.SetControls(true, false, 0f, false));
        GD.Print($"  max slip {maxSlip:F0}°, after straightening slip {SlipDeg(car):F1}°, yaw {car.AngVel.Y:F2}, speed {car.Speed * 3.6f:F0} km/h");
        Check(SlipDeg(car) < 10f && Math.Abs(car.AngVel.Y) < 0.3f, "power slide is catchable");
        Check(car.Up.Y > 0.5f, "stays on its wheels");
    }

    void DriftSpeed()
    {
        GD.Print("Handbrake flick into a power drift on gravel at 20 m/s");
        var car = NewCar(new FlatGround { Surface = SurfaceType.Gravel });
        Run(car, 1f);
        car.Vel = car.Forward * 20f;
        float h0 = Heading(car), maxSlip = 0, t = 0;
        Run(car, 2f, () =>
        {
            car.SetControls(t > 0.3f, false, 1f, t < 0.3f);
            t += Dt;
            maxSlip = Math.Max(maxSlip, SlipDeg(car));
        });
        float turned = Math.Abs(Mathf.RadToDeg(Mathf.AngleDifference(h0, Heading(car))));
        GD.Print($"  turned {turned:F0}° in 2 s, max slip {maxSlip:F0}°, speed {car.Speed * 3.6f:F0} km/h (from 72)");
        Check(turned > 90f, "drift turns the car round a hairpin");
        Check(car.Speed > 11f, "drift keeps its speed (> 40 km/h)");
    }

    /// <summary>Keyboard steering is all-or-nothing: holding full lock with full throttle must
    /// circle at a decent speed, not bog down spinning in place.</summary>
    void HoldFullLock(SurfaceType surface)
    {
        GD.Print($"Hold full throttle + full lock for 4 s on {surface} from 15 m/s");
        (float radius, float minSpeed, float endSpeed, float turned, float up) Measure(bool assist)
        {
            var car = NewCar(new FlatGround { Surface = surface },
                new CarParams { FastSteerAssist = assist ? 0.25f : 0 });
            Run(car, 1);
            car.Vel = car.Forward * 15;
            float minSpeed = float.MaxValue, turned = 0, prev = Heading(car), distance = 0;
            Run(car, 4, () =>
            {
                car.SetControls(true, false, 1, false);
                minSpeed = Math.Min(minSpeed, car.Speed);
                float heading = Heading(car);
                turned += Math.Abs(Mathf.AngleDifference(prev, heading));
                prev = heading;
                distance += car.Speed * Dt;
            });
            return (distance / Math.Max(turned, 1e-3f), minSpeed * 3.6f,
                car.Speed * 3.6f, Mathf.RadToDeg(turned), car.Up.Y);
        }
        var before = Measure(false);
        var after = Measure(true);
        GD.Print($"  radius without/with fast assist {before.radius:F1}/{after.radius:F1} m, final speed {before.endSpeed:F0}/{after.endSpeed:F0} km/h, turned {after.turned:F0} deg");
        Check(after.minSpeed > 36, "keeps its speed (> 36 km/h) on full lock");
        Check(after.up > 0.8f, "sustained full throttle and lock stays upright");
        // The stronger engine now reaches over 90 km/h in this accelerating test.
        // Compare the same vehicle with/without steering assist instead of the
        // obsolete fixed 20 m radius recorded with the old, slower engine.
        Check(after.radius <= before.radius * 1.02f, "fast steering does not widen the sustained turn");
        if (surface == SurfaceType.Concrete)
            Check(after.radius < before.radius * 0.95f, "fast steering tightens the concrete turn by at least 5%");
    }

    /// <summary>Left wheels drive over an obstacle (pothole or puddle) hands off; returns effect.</summary>
    (float roll, float yaw, float loss) LeftSideHit(FlatGround g, float speed)
    {
        // Car drives along +X; left wheels run at z = -HalfTrack.
        var car = NewCar(g);
        car.Reset(new Vector3(0, 0.7f, 0), -MathF.PI / 2);
        Run(car, 1f);
        car.Vel = car.Forward * speed;
        car.Pos = new Vector3(30f - speed * 1.0f, car.Pos.Y, 0);   // reach the obstacle after ~1 s
        float h0 = Heading(car), maxRoll = 0, v0 = speed;
        Run(car, 1.0f + 12f / speed, () =>
        {
            car.SetControls(false, false, 0, false);
            maxRoll = Math.Max(maxRoll, Math.Abs(RollDeg(car)));
        });
        // Compare with the same run on clean ground so rolling resistance/drag cancel out.
        var clean = NewCar(new FlatGround());
        clean.Reset(new Vector3(0, 0.7f, 0), -MathF.PI / 2);
        Run(clean, 1f);
        clean.Vel = clean.Forward * speed;
        Run(clean, 1.0f + 12f / speed, () => clean.SetControls(false, false, 0, false));
        float yaw = Math.Abs(Mathf.RadToDeg(Mathf.AngleDifference(h0, Heading(car))));
        if (car.PotholeHits > 0) GD.Print($"    ({car.PotholeHits} wall hits, last severity {car.LastPotholeSeverity:F2})");
        return (maxRoll, yaw, (clean.Speed - car.Speed) * 3.6f);
    }

    void PotholeHit()
    {
        GD.Print("Left wheels through a pothole at different speeds (hands off)");
        var r = new (float roll, float yaw, float loss)[4];
        float[] speeds = { 5f, 10f, 20f, 30f };
        for (int i = 0; i < 4; i++)
        {
            r[i] = LeftSideHit(new FlatGround { Hole = new Vector2(30f, -new CarParams().HalfTrack) }, speeds[i]);
            GD.Print($"  {speeds[i] * 3.6f,3:F0} km/h: roll {r[i].roll,4:F1}°, heading kick {r[i].yaw,4:F1}°, speed loss {r[i].loss,4:F1} km/h");
        }
        Check(r[0].loss < 1.5f && r[0].yaw < 1f, "crawling through barely matters");
        Check(r[1].loss < r[2].loss && r[2].loss < r[3].loss, "speed loss grows with speed");
        Check(r[1].roll < r[2].roll && r[2].roll < r[3].roll, "body jolt grows with speed");
        Check(r[3].loss > 6f && r[3].roll > 4f, "at 108 km/h a pothole really hurts");
    }

    void PuddleHit()
    {
        GD.Print("Left wheels through a 6 m puddle at different speeds (hands off)");
        var r = new (float roll, float yaw, float loss)[4];
        float[] speeds = { 5f, 10f, 20f, 30f };
        for (int i = 0; i < 4; i++)
        {
            r[i] = LeftSideHit(new FlatGround { PuddleX = 30f }, speeds[i]);
            GD.Print($"  {speeds[i] * 3.6f,3:F0} km/h: heading pull {r[i].yaw,4:F1}°, speed loss {r[i].loss,4:F1} km/h");
        }
        Check(r[0].loss < 1.5f && r[0].loss < r[3].loss * 0.35f, "slow splash costs little");
        Check(r[1].loss < r[2].loss && r[2].loss < r[3].loss, "water drag grows with speed");
        Check(r[3].yaw > 3f && r[3].yaw > r[1].yaw * 3f, "one-sided puddle pulls hard at speed");
    }

    void Rollover()
    {
        GD.Print("Sticky tyres, full lock at 25 m/s (should roll)");
        var p = new CarParams { FrontGrip = 1.8f, RearGrip = 1.8f };
        var car = NewCar(new FlatGround(), p);
        Run(car, 1f);
        car.Vel = car.Forward * 25f;
        bool flipped = false;
        Run(car, 4f, () =>
        {
            car.SetControls(false, false, 1f, false);
            if (car.Up.Y < 0.3f) flipped = true;
        });
        Check(flipped, "high-grip hard turn rolls the car");
    }

    void TrackLaps()
    {
        GD.Print("Autopilot on bonus track");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var def = TrackDef.Load("res://tracks/bonus.json");
        var track = TrackBuilder.Build(def);
        GD.Print($"  built in {sw.ElapsedMilliseconds} ms: length {track.Length:F0} m, {track.Props.Count} props, {track.Obstacles.Count} obstacles");

        var car = new CarSim(new CarParams(), track) { Obstacles = track.Obstacles, WorldMax = track.WorldSize - 4 };
        car.Reset(track.StartPos, track.StartYaw);
        var ap = new AutoPilot(track);
        var timer = new LapTimer(track);
        int flips = 0, shortcuts = 0;
        float airTime = 0, maxRoll = 0, lastAir = 0;
        timer.LapCompleted += (t, rec) => GD.Print($"  lap {LapTimer.Format(t)}{(rec ? " (record)" : "")}");
        timer.ShortcutDetected += () => shortcuts++;
        timer.Start();
        for (float t = 0; t < 300f; t += Dt)
        {
            ap.Drive(car, Dt);
            car.Step(Dt);
            timer.Update(Dt, new Vector2(car.Pos.X, car.Pos.Z));
            if (car.AirTime > 0) airTime += Dt;
            if (System.Environment.GetEnvironmentVariable("AIRDBG") != null && car.AirTime == 0 && lastAir > 0.15f)
                GD.Print($"    landed at {track.NearestCenter(new Vector2(car.Pos.X, car.Pos.Z), out _)} m after {lastAir:F2} s, roll {RollDeg(car):F0}°");
            lastAir = car.AirTime;
            if (System.Environment.GetEnvironmentVariable("SECDBG") is string sec && (int)(t / Dt) % 12 == 0)
            {
                var r = sec.Split('-');
                int ci = track.NearestCenter(new Vector2(car.Pos.X, car.Pos.Z), out float off);
                if (ci >= int.Parse(r[0]) && ci <= int.Parse(r[1]))
                    GD.Print($"    SEC {ci} m off {off:F1} v={car.Speed * 3.6f:F0} roll {RollDeg(car):F0} steer {car.SteerAngle:F2} air {car.AirTime:F2} | {string.Join(" | ", System.Linq.Enumerable.Select(car.Wheels, w => $"{w.Load:F0} {w.Surface} c={w.Compression:F2}"))}");
            }
            if (System.Environment.GetEnvironmentVariable("AIRDBG") != null && car.AirTime > 0 && car.AirTime <= Dt * 1.5f)
                GD.Print($"    airborne at {track.NearestCenter(new Vector2(car.Pos.X, car.Pos.Z), out _)} m, {car.Speed * 3.6f:F0} km/h");
            if (Math.Abs(RollDeg(car)) > maxRoll && System.Environment.GetEnvironmentVariable("AIRDBG") != null && Math.Abs(RollDeg(car)) > 20)
                GD.Print($"    roll {RollDeg(car):F0}° at {track.NearestCenter(new Vector2(car.Pos.X, car.Pos.Z), out _)} m");
            maxRoll = Math.Max(maxRoll, Math.Abs(RollDeg(car)));
            if (car.UpsideDownTime > 1.5f)
            {
                flips++;
                if (System.Environment.GetEnvironmentVariable("AIRDBG") != null)
                    GD.Print($"    FLIP at {track.NearestCenter(new Vector2(car.Pos.X, car.Pos.Z), out _)} m");
                int i = track.NearestCenter(new Vector2(car.Pos.X, car.Pos.Z), out _);
                car.Reset(track.CenterPos(i) + Vector3.Up * 0.7f, TrackData.YawFor(track.CenterDir(i)));
            }
            if (float.IsNaN(car.Pos.X)) break;
            if (OS.HasFeature("debug_track") || System.Environment.GetEnvironmentVariable("TRACKDBG") != null)
                if ((int)(t / Dt) % (int)(5f / Dt) == 0)
                    GD.Print($"    t={t:F0} pos=({car.Pos.X:F0},{car.Pos.Z:F0}) v={car.Speed:F1} gate={timer.NextGate} prog={timer.Progress} gear={car.Gear} thr={car.Throttle} brk={car.Brake} rpm={car.Rpm:F0} scrape={car.BodyScrape:F2} up={car.Up.Y:F2} fy={car.Forward.Y:F2}\n      wheels: {string.Join(" | ", System.Linq.Enumerable.Select(car.Wheels, w => $"{w.Load:F0}{(w.Spinning ? "S" : "")} {w.Surface} c={w.Compression:F2}"))}");
        }
        GD.Print($"  laps {timer.LapsDone}, flips {flips}, shortcut warnings {shortcuts}, air time {airTime:F1} s, max roll {maxRoll:F0}°");
        Check(!float.IsNaN(car.Pos.X), "no NaN");
        Check(timer.LapsDone >= 2, "autopilot completes laps");
        Check(shortcuts == 0, "autopilot is not flagged for shortcuts");
    }

    /// <summary>
    /// A recorded lap survives save/load with its car state, and an old pose-only file gets
    /// believable steering and revs reconstructed from the motion.
    /// </summary>
    /// <summary>R voids the lap: it runs to the line but is never a record; the next lap counts again.</summary>
    void VoidLap()
    {
        GD.Print("Void lap (R calls the mechanic)");
        var track = TrackBuilder.Build(TrackDef.Load("res://tracks/bonus.json"));
        var timer = new LapTimer(track);
        var laps = new System.Collections.Generic.List<(float time, bool record, bool isVoid)>();
        timer.LapCompleted += (t, rec) => laps.Add((t, rec, false));
        timer.VoidLapCompleted += t => laps.Add((t, false, true));
        timer.Start();
        int n = track.Center.Length;
        // Drive the centre line: lap 1 slow, lap 2 fastest but voided halfway, lap 3 in between.
        float[] dtPerMetre = { 0.05f, 0.02f, 0.04f };
        bool voidSeen = false;
        for (int k = 1; laps.Count < 3 && k < n * 4; k++)
        {
            int lap = laps.Count;
            if (lap == 1 && k % n == n / 2) { timer.Void(); voidSeen = timer.LapVoid; }
            timer.Update(dtPerMetre[lap], track.Center[k % n]);
        }
        foreach (var l in laps) GD.Print($"  lap {LapTimer.Format(l.time)}{(l.record ? " (record)" : "")}{(l.isVoid ? " VOID" : "")}");
        Check(laps.Count == 3 && voidSeen, "three laps, the middle one voided");
        if (laps.Count < 3) return;
        Check(laps[0].record && !laps[0].isVoid, "first lap counts as the record");
        Check(laps[1].isVoid && !laps[1].record && laps[1].time < laps[0].time, "void lap is faster but not a record");
        Check(laps[2].record && !laps[2].isVoid && timer.BestLap == laps[2].time && laps[2].time < laps[0].time,
              "next lap is valid and takes the record");
        Check(!timer.LapVoid && !timer.LastLapVoid && timer.LapsDone == 3, "void state cleared after a valid lap");
        float best = timer.BestLap;
        timer.Start();
        Check(timer.LapsDone == 0 && timer.BestLap == best, "restart begins at lap 1 and keeps the best lap");
    }

    /// <summary>Training laps: keys 1-4 pick fuel power (100 % at the start); every valid lap is ranked.</summary>
    void TrainingLaps()
    {
        GD.Print("Training laps (chosen power)");
        float To50(float scale, out float after20)
        {
            var car = NewCar(new FlatGround());
            car.PowerScale = scale;
            Run(car, 1f);
            float t = 0, t50 = -1;
            Run(car, 20f, () =>
            {
                car.SetControls(true, false, 0, false);
                t += Dt;
                if (t50 < 0 && car.Speed > 50f / 3.6f) t50 = t;
            });
            after20 = car.Speed * 3.6f;
            return t50;
        }
        var t50 = new float[4];
        var v20 = new float[4];
        for (int i = 0; i < 4; i++) t50[i] = To50((i + 1) / 4f, out v20[i]);
        GD.Print($"  0-50 km/h at 25/50/75/100 %: {t50[0]:F2} / {t50[1]:F2} / {t50[2]:F2} / {t50[3]:F2} s; after 20 s {v20[0]:F0} / {v20[1]:F0} / {v20[2]:F0} / {v20[3]:F0} km/h");
        Check(t50[3] > 0 && t50[0] > t50[3] * 2f, "25 % power: 0-50 clearly slower (over twice the time)");
        Check(t50[0] > t50[1] && t50[1] > t50[2] && t50[2] + 0.2f > t50[3], "each step of the plan accelerates faster (the last one up to a wheelspin's worth: with the petrol's extra torque 100 % spins the wheels off the line)");
        Check(v20[0] < v20[3] * 0.87f, "25 % power: lower speed after 20 s (the taller rally gearing gets it to about 81 % of the top speed)");

        var levels = new int[Training.Count];
        for (int k = 1; k <= Training.Count; k++) levels[k - 1] = Training.PercentForKey(k);
        Check(string.Join(",", levels) == "50,100,200" && Training.DefaultPercent == 100,
              "keys 1-3: diesel 50, petrol 100, rocket fuel 200 %; petrol at the start");
        Check(!Training.Hero(175) && Training.Hero(200) && Training.Hero(500), "200 % and up: hero of socialist labour");
        bool jokes = true;
        foreach (int p in levels) foreach (char ch in Training.Joke(p)) jokes &= CCCPRacing.UI.PixelFont.CanDraw(ch);
        Check(jokes, "every power joke is in the pixel font");
        float t200 = To50(2f, out float v200);
        GD.Print($"  200 %: 0-50 km/h {t200:F2} s, after 20 s {v200:F0} km/h");
        Check(t200 > 0 && v200 >= v20[3] * 0.99f, "200 % power: no slower after 20 s (the first 50 km/h spin the wheels and take longer than at 100 %; 0-100 is quicker, OverdriveTest)");
        float t500 = To50(5f, out float v500);
        GD.Print($"  500 %: 0-50 km/h {t500:F3} s (200 %: {t200:F3}), after 20 s {v500:F1} km/h (200 %: {v200:F1})");
        Check(t500 > 0 && t500 <= t200 + 0.05f && float.IsFinite(v500) && v500 >= v200 * 0.98f, "500 % power: finite, no slower than 200 % (grip and top speed limit both)");

        // Each run has its own best lap (the race swaps BestLap): a Stakhanov lap at any power is
        // ranked and is a record against the Stakhanov best only, not the rally one.
        var track = TrackBuilder.Build(TrackDef.Load("res://tracks/bonus.json"));
        var timer = new LapTimer(track);
        var laps = new System.Collections.Generic.List<(float time, bool record, bool ranked)>();
        timer.LapCompleted += (t, rec) => laps.Add((t, rec, timer.LastLapRanked));
        timer.Start();
        timer.BestLap = 55f;                 // rally record
        timer.CompleteLapForTest(60f);
        timer.BestLap = -1;                  // Stakhanov: no record yet
        timer.CompleteLapForTest(60f);
        timer.CompleteLapForTest(48f);       // a 200 % lap under the rally record
        Check(laps.Count == 3 && !laps[0].record && laps[1].record && laps[2].record && laps.TrueForAll(l => l.ranked)
              && timer.BestLap == 48f, "a lap is a record against its own run's best; every valid lap is ranked");
    }

    /// <summary>Engine health: damage curve, power mapping, training immunity, direct-hit thresholds.</summary>
    void EngineHealthTest()
    {
        GD.Print("Engine health");
        bool mono = true;
        for (float v = 0; v < 40f; v += 0.5f)
            mono &= EngineHealth.ImpactDamage(v + 0.5f) >= EngineHealth.ImpactDamage(v);
        float d5 = EngineHealth.ImpactDamage(5f), d12 = EngineHealth.ImpactDamage(12f), d20 = EngineHealth.ImpactDamage(20f);
        GD.Print($"  damage at 4/5/8/12/20/30 m/s: {EngineHealth.ImpactDamage(4f) * 100:F1} / {d5 * 100:F1} / {EngineHealth.ImpactDamage(8f) * 100:F1} / {d12 * 100:F1} / {d20 * 100:F1} / {EngineHealth.ImpactDamage(30f) * 100:F1} %");
        Check(mono && EngineHealth.ImpactDamage(4f) == 0f, "damage grows monotonically, nothing up to 4 m/s");
        Check(Math.Abs(d12 - 0.1f) < 0.015f && Math.Abs(d20 - 0.225f) < 0.025f, "12 m/s wall about -10 %, 20 m/s about -22 %");
        Check(d20 / 20f > d12 / 12f && d12 / 12f > d5 / 5f, "faster than linear");
        Check(d5 < 0.01f, "a scrape at 5 m/s costs under 1 %");

        Check(EngineHealth.PowerFor(1f) == 1f && Math.Abs(EngineHealth.PowerFor(0.5f) - 0.854f) < 0.01f
              && EngineHealth.PowerFor(0.01f) > 0.5f && EngineHealth.PowerFor(0.25f) > 0.25f, "power drops less than health (half health: 85 %)");
        var h = new EngineHealth();
        h.Reset(true);
        var ticks = new System.Collections.Generic.List<EngineHealth.Change>();
        // One crash that registers over three ticks is charged once for its peak.
        foreach (float v in new[] { 9f, 12f, 7f }) ticks.Add(h.Impact(v, 1f / 60f));
        Check(Math.Abs(1f - h.Health - d12) < 0.001f, $"a multi-tick crash is charged once ({(1f - h.Health) * 100:F1} %)");
        for (int i = 0; i < 200 && !h.Dead; i++) ticks.Add(h.Impact(12f, 0.5f));
        Check(ticks.Contains(EngineHealth.Change.Smoking) && ticks[^1] == EngineHealth.Change.Died && h.PowerFactor == 0f, "smokes at 25 %, then dies; dead engine has no power");

        var t = new EngineHealth();
        t.Reset(false);        // training laps
        Check(t.Impact(25f, 0.1f) == EngineHealth.Change.None && t.Damage(0.9f, "shell") == EngineHealth.Change.None
              && t.Destroy("shell") == EngineHealth.Change.None && t.Health == 1f && t.PowerFactor == 1f, "training: no health, nothing destroys the car");

        Check(EngineHealth.ShellDamage(2f) >= 1f && 2f <= EngineHealth.ShellLethal, "shell at 2 m: direct hit, destroyed");
        float s5 = EngineHealth.ShellDamage(5f);
        Check(5f > EngineHealth.ShellLethal && s5 > 0.025f && s5 < 0.175f, $"shell at 5 m only damages ({s5 * 100:F0} %)");
        Check(EngineHealth.ShellDamage(12f) == 0f, "shell at 12 m: harmless");
        float m3 = EngineHealth.MissileDamage(3f), m6 = EngineHealth.MissileDamage(6f);
        Check(EngineHealth.MissileDamage(1f) >= 1f && m3 >= 0.4f && m3 <= 0.7f && m6 >= 0.1f && m6 <= 0.3f && EngineHealth.MissileDamage(7.5f) == 0f,
            $"missile: direct hit within 1.5 m, heavy at 3 m ({m3 * 100:F0} %), shrapnel at 6 m ({m6 * 100:F0} %), nothing at 7.5 m");
        Check(EngineHealth.JetDamage(5f) >= 1f && EngineHealth.JetDamage(10f) < 0.25f && EngineHealth.JetDamage(30f) == 0f, "Balalaika: lethal within 6 m, damage further out");
        var d = new EngineHealth();
        d.Reset(true);
        Check(d.Destroy("TANK SHELL") == EngineHealth.Change.Destroyed && d.Dead && d.Destroyed && d.PowerFactor == 0f, "a direct hit destroys a healthy car");

        // Training's power and the engine's combine multiplicatively on the car.
        var car = NewCar(new FlatGround());
        Run(car, 1f);
        car.PowerScale = Training.PercentForKey(1) / 100f * EngineHealth.PowerFor(0.5f);
        float v0 = 0;
        Run(car, 5f, () => car.SetControls(true, false, 0, false));
        v0 = car.Speed;
        var car2 = NewCar(new FlatGround());
        Run(car2, 1f);
        car2.PowerScale = EngineHealth.PowerFor(0.1f);
        Run(car2, 5f, () => car2.SetControls(true, false, 0, false));
        var car3 = NewCar(new FlatGround());
        Run(car3, 1f);
        Run(car3, 5f, () => car3.SetControls(true, false, 0, false));
        GD.Print($"  speed after 5 s: healthy {car3.Speed * 3.6f:F0}, 10 % health {car2.Speed * 3.6f:F0}, training 50 % x half health {v0 * 3.6f:F0} km/h");
        Check(car2.Speed < car3.Speed && car2.Speed > car3.Speed * 0.6f, "sick engine is slower, but still drives");
    }

    void GhostRoundTrip()
    {
        GD.Print("Ghost recording");
        var track = TrackBuilder.Build(TrackDef.Load("res://tracks/bonus.json"));
        var p = new CarParams();
        var car = new CarSim(p, track) { Obstacles = track.Obstacles, WorldMax = track.WorldSize - 4 };
        car.Reset(track.StartPos, track.StartYaw);
        var ap = new AutoPilot(track);
        var rec = new Ghost();
        float clock = 0;
        for (; clock < 25f; clock += Dt)
        {
            ap.Drive(car, Dt);
            car.Step(Dt);
            rec.Record(clock, car, car.LastImpact, car.PotholeBangStep, car.SuspensionHit, 6f + clock / 25f);   // dusk into night
        }

        string v2 = "user://test_ghost_v2.bin", v1 = "user://test_ghost_v1.bin";
        rec.CopyWithTime(25f).Save(v2);
        var loaded = Ghost.Load(v2, p, track);
        var a = new CarSim(p, track);
        var b = new CarSim(p, track);
        float posErr = 0, steerErr = 0, rpmErr = 0;
        int wheelsDiffer = 0, n = 0;
        for (float t = 0.5f; t < 24f; t += 0.37f, n++)
        {
            rec.Apply(t, a, 0);
            loaded.Apply(t, b, 0);
            posErr = Math.Max(posErr, a.Pos.DistanceTo(b.Pos));
            steerErr = Math.Max(steerErr, Math.Abs(a.SteerAngle - b.SteerAngle));
            rpmErr = Math.Max(rpmErr, Math.Abs(a.Rpm - b.Rpm));
            for (int k = 0; k < 4; k++)
                if (a.Wheels[k].Surface != b.Wheels[k].Surface || a.Wheels[k].Spinning != b.Wheels[k].Spinning
                    || Math.Abs(a.Wheels[k].VisualLength - b.Wheels[k].VisualLength) > 1e-4f) wheelsDiffer++;
        }
        Check(loaded.Count == rec.Count && posErr < 1e-3f && steerErr < 1e-4f && rpmErr < 0.1f && wheelsDiffer == 0,
            $"v3 save/load keeps pose, steering, revs and wheels ({loaded.Count} samples)");
        float dayErr = 0;
        for (float t = 0.5f; t < 24f; t += 0.37f) dayErr = Math.Max(dayErr, Math.Abs(loaded.DayClockAt(t) - (6f + t / 25f)));
        Check(loaded.HasDayClock && dayErr < 0.01f, $"v3 keeps the time of day (worst {dayErr:F4} laps)");

        // Old format: poses only, written the way v0.1 did.
        using (var f = FileAccess.Open(v1, FileAccess.ModeFlags.Write))
        {
            f.Store32(0x43434350);
            f.StoreFloat(25f);
            f.Store32((uint)rec.Count);
            for (int i = 0; i < rec.Count; i++)
            {
                Transform3D x = rec.Sample(i / Ghost.SampleRate);
                Quaternion q = x.Basis.GetRotationQuaternion();
                f.StoreFloat(x.Origin.X); f.StoreFloat(x.Origin.Y); f.StoreFloat(x.Origin.Z);
                f.StoreFloat(q.X); f.StoreFloat(q.Y); f.StoreFloat(q.Z); f.StoreFloat(q.W);
            }
        }
        var old = Ghost.Load(v1, p, track);
        float sxy = 0, sxx = 0, syy = 0, rpmSum = 0, rpmTrue = 0;
        int sameSign = 0, turning = 0;
        for (float t = 1f; t < 24f; t += 0.1f)
        {
            rec.Apply(t, a, 0);
            old.Apply(t, b, 0);
            sxy += a.SteerAngle * b.SteerAngle; sxx += a.SteerAngle * a.SteerAngle; syy += b.SteerAngle * b.SteerAngle;
            if (Math.Abs(a.SteerAngle) > 0.08f) { turning++; if (Math.Sign(a.SteerAngle) == Math.Sign(b.SteerAngle)) sameSign++; }
            rpmSum += b.Rpm; rpmTrue += a.Rpm;
        }
        float corr = sxy / MathF.Sqrt(sxx * syy + 1e-9f);
        GD.Print($"  v1 reconstruction: steer correlation {corr:F2}, same direction {sameSign}/{turning}, mean rpm {rpmSum / 230:F0} vs {rpmTrue / 230:F0}");
        Check(old.Count == rec.Count && corr > 0.4f && sameSign > turning * 0.8f, "v1 ghost: reconstructed steering follows the real one");
        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(v1));
        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(v2));
    }

    /// <summary>Top-10 board: ordering, cap, ties, save/load, seeding, and which laps may enter.</summary>
    async void LeaderboardUiTests()
    {
        try
        {
            LeaderboardTest();
            foreach (var size in ReplayUiSizes)
            {
                var vp = new SubViewport { Size = new Vector2I((int)size.X, (int)size.Y), RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
                AddChild(vp);
                var hud = new CCCPRacing.UI.Hud { Mode = CCCPRacing.UI.HudMode.Board, BoardTrack = "ILYICH", BoardStakhanov = false, BoardSelected = 0, BoardReplayNotice = "ENTER REPLAY" };
                var rows = new[] { new Leaderboard.Entry("LONGEST-NAME", 53.12f, "2026-10-29", Pad: true, Night: true, Boost: true),
                    new Leaderboard.Entry("TOVARISH", 54f, "2026-10-28"), new Leaderboard.Entry("ANTTI", 55f, "2026-10-27") };
                hud.BoardFuels = new[] { new CCCPRacing.UI.Hud.FuelBoard("PETROL", rows, 0, "GAI 128 KM/H"),
                    new CCCPRacing.UI.Hud.FuelBoard("DIESEL", rows, 1, "GAI 130 KM/H"),
                    new CCCPRacing.UI.Hud.FuelBoard("ROCKET FUEL", rows, 2, "GAI 150 KM/H") };
                vp.AddChild(hud);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                string dir = Array.Find(OS.GetCmdlineUserArgs(), a => a.StartsWith("--board-review-dir="))?["--board-review-dir=".Length..] ?? "media/leaderboard-top3-20261029";
                System.IO.Directory.CreateDirectory(dir);
                var image = vp.GetTexture().GetImage();
                Check(image.SavePng($"{dir}/{size.X}x{size.Y}.png") == Error.Ok, $"three fuel groups render at {size}");
                hud.BoardStakhanov = true;
                hud.Board = System.Linq.Enumerable.Range(0, 10).Select(i => rows[i % 3] with { Power = 150 + i }).ToArray();
                hud.QueueRedraw();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                Check(vp.GetTexture().GetImage().SavePng($"{dir}/stakhanov-{size.X}x{size.Y}.png") == Error.Ok,
                    $"Stakhanov ten-place board renders at {size}");
                vp.QueueFree();
            }
        }
        catch (Exception e) { GD.PrintErr(e); _failures++; }
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    void LeaderboardTest()
    {
        GD.Print("Leaderboard");
        string path = "user://test_leaderboard.cfg";
        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(path));

        var empty = Leaderboard.Load(path);
        Check(empty.Entries.Count == 0 && empty.Qualifies(99f) && !empty.Qualifies(0f) && !empty.Qualifies(-1f),
            "missing file: empty board, any real lap qualifies");
        var seeded = Leaderboard.Load(path, 55.84f);
        Check(seeded.Entries.Count == 1 && seeded.Entries[0].Name == "TOVARISH" && seeded.Entries[0].Time == 55.84f
              && seeded.Entries[0].Date == "", "no file but a record: seeded as TOVARISH, no date");

        var b = new Leaderboard { Path = path };
        float[] times = { 62f, 58f, 70f, 60f, 65f, 59f, 61f, 64f, 63f, 66f };
        foreach (float t in times) b.Insert($"D{t:0}", t, "2026-09-25");
        bool sorted = true;
        for (int i = 1; i < b.Entries.Count; i++) sorted &= b.Entries[i - 1].Time <= b.Entries[i].Time;
        Check(b.Entries.Count == 3 && sorted && b.Entries[0].Time == 58f && b.Entries[2].Time == 60f, "three laps sorted fastest first");
        Check(!b.Qualifies(61f) && !b.Qualifies(60f) && b.Qualifies(59.99f), "full board: slower or equal to third does not qualify");
        Check(b.Insert("SLOW", 75f) == 0 && b.Entries.Count == 3, "a slower lap stays off the top three");
        int tie = b.Insert("TIE", 59f, "2026-09-26");
        Check(tie == 3 && b.Entries[1].Name == "D59" && b.Entries[2].Name == "TIE", "a tie goes after the existing equal time");
        Check(b.Entries.Count == 3 && b.Entries[2].Time == 59f, "capped at three: the slowest drops off");
        Check(b.Insert("äijä kÖÖ-9 ?!", 50f) == 1 && b.Entries[0].Name == "ÄIJÄ KÖÖ-9", "name cleaned: uppercase, allowed characters only");
        Check(Leaderboard.CleanName("abcdefghijklmnopq") == "ABCDEFGHIJKL" && Leaderboard.CleanName("  ") == "", "names capped at 12 characters");
        Check(b.Insert("", 51f) == 2 && b.Entries[1].Name == "TOVARISH", "empty name becomes TOVARISH");
        b.LastName = "ANTTI";
        b.Save();

        var l = Leaderboard.Load(path, 1f);
        bool same = l.Entries.Count == b.Entries.Count;
        for (int i = 0; same && i < l.Entries.Count; i++)
            same = l.Entries[i].Name == b.Entries[i].Name && Math.Abs(l.Entries[i].Time - b.Entries[i].Time) < 1e-4f && l.Entries[i].Date == b.Entries[i].Date;
        Check(same && l.LastName == "ANTTI", "save/load round trip keeps names, times, dates and the last name (no seeding over a file)");

        var legacy = new ConfigFile();
        for (int i = 0; i < 10; i++) { legacy.SetValue($"entry_{i}", "name", "OLD"); legacy.SetValue($"entry_{i}", "time", 70f - i); }
        legacy.Save(path);
        var migrated = Leaderboard.Load(path);
        Check(migrated.Entries.Count == 3 && migrated.Entries[0].Time == 61f && migrated.Entries[2].Time == 63f,
            "legacy ten-entry board is sorted before selecting its fastest three");
        migrated.Save();
        var saved = new ConfigFile(); saved.Load(path);
        Check(saved.HasSection("entry_2") && !saved.HasSection("entry_3"), "save writes only the top three");

        GD.Print("  (a ConfigFile parse error below is expected)");
        using (var f = FileAccess.Open(path, FileAccess.ModeFlags.Write)) f.StoreString("[entry_0\nname=((garbage");
        var bad = Leaderboard.Load(path, 55.84f);
        Check(bad.Entries.Count == 1 && bad.Entries[0].Name == "TOVARISH", "corrupt file: treated as missing");
        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(path));

        // Only ranked laps may enter: void (R) laps are not.
        var track = TrackBuilder.Build(TrackDef.Load("res://tracks/bonus.json"));
        var timer = new LapTimer(track);
        var ranked = new System.Collections.Generic.List<bool>();
        timer.LapCompleted += (_, _) => ranked.Add(timer.LastLapRanked);
        timer.VoidLapCompleted += _ => ranked.Add(timer.LastLapRanked);
        timer.Start();
        timer.CompleteLapForTest(60f);
        timer.Void();
        timer.CompleteLapForTest(50f);
        timer.CompleteLapForTest(59f);
        Check(ranked.Count == 3 && ranked[0] && !ranked[1] && ranked[2], "void laps are not ranked, normal laps are");

        // Stakhanov entries keep their power; rally entries (and old files) have none.
        var st = new Leaderboard(Leaderboard.StakhanovSize) { Path = path };
        st.Insert("A", 61f, "2026-09-28", 120);
        st.Insert("B", 49f, "2026-09-28", 200);
        st.Insert("C", 70f, "2026-09-28");
        st.Save();
        var st2 = Leaderboard.Load(path, capacity: Leaderboard.StakhanovSize);
        Check(st2.Entries.Count == 3 && st2.Entries[0].Power == 200 && st2.Entries[1].Power == 120 && st2.Entries[2].Power == 0,
              "power saved and loaded per entry, sorted by time only");
        Check(!FileAccess.GetFileAsString(path).Contains("power=0"), "no power key for an entry without power");
        for (int i = 0; i < 12; i++) st.Insert("EXTRA", 80f + i);
        st.Save();
        st2 = Leaderboard.Load(path, capacity: Leaderboard.StakhanovSize);
        Check(st2.Entries.Count == 10 && !st2.Qualifies(st2.Entries[9].Time) && st2.Qualifies(st2.Entries[9].Time - .01f),
            "Stakhanov keeps ten ranked laps across save/load and rejects the eleventh");
        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(path));
    }

    /// <summary>The KGB officers at the start line: solid, and the clerk writes down laps and line crossings.</summary>
    void KgbTest()
    {
        GD.Print("KGB officers at the start line");
        var track = TrackBuilder.Build(TrackDef.Load("res://tracks/bonus.json"));
        var kgb = new KgbOfficers();
        kgb.Init(track, null);
        Vector2 clerk = kgb.ClerkPosition, dir = track.CenterDir(0);
        float fromCentre = track.NearestCenter(clerk, out float dc) >= 0 ? dc : 0;
        Check(fromCentre > track.HalfWidth[0] + 2f, $"clerk stands beyond the road edge ({fromCentre:F1} m from the centre line)");
        Check(track.Obstacles.Exists(o => o.Center == clerk) && track.Obstacles.Exists(o => o.Center == kgb.SupervisorPosition), "both officers are obstacles");

        // Straight at the clerk at 50 km/h: the car stops at him, he does not move.
        var car = new CarSim(new CarParams(), track) { Obstacles = track.Obstacles, WorldMax = track.WorldSize - 4 };
        // Off the road at an angle, straight at him (the verge slopes, so aim from close by).
        Vector2 aim = (clerk - (track.Center[0] - dir * 14f)).Normalized();
        Vector2 from = clerk - aim * 12f;
        car.Reset(new Vector3(from.X, track.MeshHeight(from.X, from.Y) + 0.6f, from.Y), TrackData.YawFor(aim));
        car.Vel = new Vector3(aim.X, 0, aim.Y) * (50f / 3.6f);
        float maxImpact = 0, minDist = 1e9f;
        Run(car, 2.5f, () =>
        {
            car.SetControls(false, false, 0f, false);
            maxImpact = Math.Max(maxImpact, car.LastImpact);
            if (OS.HasEnvironment("KGBDBG")) GD.Print($"    {car.Pos.X:F2} {car.Pos.Z:F2} v {car.Speed*3.6f:F0} impact {car.LastImpact:F1} y {car.Pos.Y:F2} gnd {track.MeshHeight(car.Pos.X, car.Pos.Z):F2} scrape {car.BodyScrape:F2} d {new Vector2(car.Pos.X, car.Pos.Z).DistanceTo(clerk):F2}");
            minDist = Math.Min(minDist, new Vector2(car.Pos.X, car.Pos.Z).DistanceTo(clerk));
        });
        float past = (new Vector2(car.Pos.X, car.Pos.Z) - clerk).Dot(aim);
        GD.Print($"  car ends {past:F1} m along from the clerk, {car.Speed * 3.6f:F0} km/h, impact {maxImpact:F1}, closest {minDist:F2} m");
        Check(minDist > 1.5f && maxImpact > 3f, "the car crashes into the clerk and does not pass through");      // (it may glance off him and roll past: the steering defaults of 2026-11-02 turn it sooner; what must not happen is going through him)
        Check(kgb.ClerkPosition == clerk, "a KGB officer does not move for anybody");

        // Line crossing (the title replay car too) and laps from the timer.
        var c2 = new CarSim(new CarParams(), track);
        Vector2 a = track.Center[0] - dir * 3f, b = track.Center[0] + dir * 1f;
        c2.Reset(new Vector3(a.X, 1, a.Y), TrackData.YawFor(dir));
        kgb.Tick(0.1f, c2);
        c2.Pos = new Vector3(b.X, 1, b.Y);
        kgb.Tick(0.1f, c2);
        Check(kgb.Scribbling, "the clerk writes when the car crosses the line");
        kgb.LogLap(57.12f);
        var shouts = new System.Collections.Generic.List<(Vector3, string)>(kgb.ActiveShouts());
        Check(kgb.LapsLogged == 1 && shouts.Exists(x => x.Item2 == "0:57.12 ЗАПИСАНО"), "LogLap records the lap and shows the time");
        Check(shouts.TrueForAll(x => x.Item2.ToCharArray() is var cs && Array.TrueForAll(cs, ch => ch == ' ' || CCCPRacing.UI.PixelFont.CanDraw(ch))), "bubble text only uses PixelFont glyphs");
        kgb.Free();
    }

    /// <summary>The track signature follows the road's shape and nothing else.</summary>
    void SignatureTest()
    {
        GD.Print("Track signature");
        var def = TrackDef.Load("res://tracks/bonus.json");
        var a = TrackBuilder.Build(def);
        string sa = TrackSignature.Compute(a);
        string sb = TrackSignature.Compute(TrackBuilder.Build(TrackDef.Load("res://tracks/bonus.json")));
        GD.Print($"  signature {sa} (v0.2: {TrackSignature.LegacyBonus})");
        Check(sa == sb && sa.Length == 16, "same track builds to the same signature");
        Check(sa != TrackSignature.LegacyBonus, "the reshaped bonus track differs from the v0.2 track");
        a.CenterH[500] += 0.2f;
        Check(TrackSignature.Compute(a) != sa, "a 20 cm change in one road height changes it");
        a.CenterH[500] -= 0.2f;
        def.Hills[0].Height += 1f;
        Check(TrackSignature.Compute(TrackBuilder.Build(def)) != sa, "a taller massif changes it");
        def.Hills[0].Height -= 1f;
        def.Birches += 20;
        def.Props.RemoveAt(0);
        Check(TrackSignature.Compute(TrackBuilder.Build(def)) == sa, "scenery (trees, props) does not");
    }

    /// <summary>Records from another road shape are renamed out of the way, never deleted.</summary>
    void RecordArchiveTest()
    {
        GD.Print("Record archive");
        const string dir = "user://test_records";
        void Clean()
        {
            var d = DirAccess.Open(dir);
            if (d == null) return;
            foreach (string f in d.GetFiles()) d.Remove(f);
        }
        void Put(string name, string text) { using var f = FileAccess.Open($"{dir}/{name}", FileAccess.ModeFlags.Write); f.StoreString(text); }
        bool Has(string name) => FileAccess.FileExists($"{dir}/{name}");
        Clean();

        var store = new RecordStore(dir, "bonus", "0123456789abcdef");
        Check(store.ArchiveIfChanged().Count == 0, "no records: nothing to archive");
        Put("ghost_bonus.bin", "old ghost");
        Put("leaderboard_bonus.cfg", "old board");
        var moved = store.ArchiveIfChanged();
        Check(moved.Count == 2 && !Has("ghost_bonus.bin") && !Has("leaderboard_bonus.cfg")
              && FileAccess.GetFileAsString($"{dir}/ghost_bonus_v02.bin") == "old ghost"
              && FileAccess.GetFileAsString($"{dir}/leaderboard_bonus_v02.cfg") == "old board",
              "unsigned (v0.2) records on a new track are renamed to *_v02");

        Put("ghost_bonus.bin", "new ghost");
        store.WriteSignature();
        Check(store.ArchiveIfChanged().Count == 0 && Has("ghost_bonus.bin"), "records saved on this track stay");

        var legacy = new RecordStore(dir, "bonus", TrackSignature.LegacyBonus);
        Put("leaderboard_bonus.cfg", "board");
        moved = legacy.ArchiveIfChanged();
        Check(moved.Count == 3 && Has("ghost_bonus_v02_2.bin") && Has("leaderboard_bonus_v02_2.cfg") && Has("track_bonus_v02_2.sig")
              && Has("ghost_bonus_v02.bin"), "a taken archive name gets a number; the signature goes with its records");
        Put("ghost_bonus.bin", "v0.2 ghost");
        Check(legacy.ArchiveIfChanged().Count == 0, "unsigned records on the v0.2 track stay");
        Clean();

        // A terrain change archives both runs' records.
        Put("ghost_bonus.bin", "g");
        Put("leaderboard_bonus_stakhanov.cfg", "sb");
        Put("ghost_bonus_stakhanov.bin", "sg");
        moved = store.ArchiveIfChanged();
        Check(moved.Count == 3 && Has("ghost_bonus_v02.bin") && Has("leaderboard_bonus_stakhanov_v02.cfg")
              && Has("ghost_bonus_stakhanov_v02.bin") && !Has("ghost_bonus_stakhanov.bin"), "a terrain change archives the Stakhanov records too");
        Clean();

        // One set per run: the old (v0.3) records are archived once, then the layout is marked.
        Check(store.ArchiveOldLayout().Count == 0 && store.StoredLayout() == RecordStore.Layout, "fresh directory: nothing archived, layout written");
        Clean();
        Put("ghost_bonus.bin", "v03 ghost");
        Put("leaderboard_bonus.cfg", "v03 board");
        Put("ghost_bonus_v03.bin", "taken");
        store.WriteSignature();
        moved = store.ArchiveOldLayout();
        Check(moved.Count == 2 && !Has("ghost_bonus.bin") && !Has("leaderboard_bonus.cfg")
              && FileAccess.GetFileAsString($"{dir}/ghost_bonus_v03_2.bin") == "v03 ghost"
              && FileAccess.GetFileAsString($"{dir}/leaderboard_bonus_v03_2.cfg") == "v03 board"
              && Has("track_bonus.sig") && store.StoredLayout() == RecordStore.Layout, "old records renamed to *_v03 (numbered if taken), nothing else moves");
        Put("ghost_bonus.bin", "new ghost");
        Check(store.ArchiveOldLayout().Count == 0 && Has("ghost_bonus.bin"), "a second launch archives nothing");
        Clean();
        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(dir));
    }

    void RevengeTest()
    {
        GD.Print("Revenge babushkas: mothers, avenger Balalaika, tank chase state");
        var track = TrackBuilder.Build(TrackDef.Load("res://tracks/bonus.json"));
        var b = new Babushkas();
        AddChild(b);
        b.Init(track, Math.Max(2, track.BabushkaCount));
        bool distinct = true, inRange = true;
        for (int k = 0; k < 20; k++)
        {
            b.Reset();
            distinct &= b.PilotMother != b.TankMother;
            inRange &= b.PilotMother >= 0 && b.TankMother >= 0 && b.PilotMother < Math.Max(2, track.BabushkaCount);
        }
        Check(distinct && inRange, "each run has two different mothers");
        b.ForcePilotMother = true;
        b.Reset();
        Check(b.PilotMother == 0 && b.TankMother != 0, "--pilot-mother makes babushka 0 the pilot's mother");
        b.ForceTankMother = true;
        b.Reset();
        Check(b.PilotMother == 0 && b.TankMother == 1, "both flags: pilot's mother 0, tank driver's mother 1");
        b.QueueFree();

        var jets = new BalalaikaSorties();
        AddChild(jets);
        // The bonus base has Hinds and no hangar; exercise Balalaika revenge at its Siberia base.
        jets.Init(TrackBuilder.Build(TrackDef.Load("res://tracks/siberia.json")), null);
        Check(!jets.RevengeActive && jets.StartRevenge() && jets.RevengeActive, "pilot's mother: a revenge Balalaika is on");
        Check(!jets.StartRevenge(), "a second hit does not stack");
        jets.Reset();
        Check(!jets.RevengeActive && jets.AvengerId == -1, "a new run resets the Balalaika revenge");
        jets.QueueFree();

        if (track.TankRange is { } tr)
        {
            Check(TankBattle.EncounterWindow(10f, 8f, 120f), "tank crossing: approaching car gets a visible road obstruction");
            Check(!TankBattle.EncounterWindow(8f, 8f, 120f), "tank crossing: refuse a late start");
            Check(!TankBattle.EncounterWindow(20f, 8f, 240f), "tank crossing: wait when the car is still far away");
            Check(!TankBattle.EncounterWindow(10f, 8f, 20f), "tank crossing: never start in front of a nearby car");
            var tanks = new TankBattle();
            AddChild(tanks);
            var tankFx = new TyreEffects(); AddChild(tankFx); tankFx.Init(track);
            tanks.Init(track, tankFx, new Rect2(tr.X0, tr.Z0, tr.X1 - tr.X0, tr.Z1 - tr.Z0), tr.Tanks);
            var approaching = new CarSim(new CarParams(), track);
            bool encounter = false;
            for (int i = 0; i < track.Center.Length && !encounter; i += 4)
                foreach (int direction in new[] { 1, -1 })
                {
                    tanks.Reset();
                    approaching.Pos = track.CenterPos(i);
                    var v = track.CenterDir(i) * (20f * direction);
                    approaching.Vel = new Vector3(v.X, 0, v.Y);
                    tanks.Tick(0, approaching);
                    if (tanks.EncounterStarted) { encounter = true; break; }
                }
            Check(encounter, "bonus: real firing positions and obstacles permit a player-timed crossing");
            approaching.Pos = new Vector3(230, 0, 58);
            approaching.Vel = Vector3.Zero;
            tanks.Tick(0, approaching);
            Check(tanks.EncounterStarted, "tank crossing: leaving does not rearm an unfinished crossing");
            for (int frame = 0; frame < 120 * 30 && tanks.EncounterStarted; frame++)
                tanks.Tick(1f / 30, approaching);
            Check(!tanks.EncounterStarted, "tank crossing: leaving and returning the tank rearms the next lap");
            tanks.Reset();
            Check(!tanks.EncounterStarted, "tank crossing: restart rearms the encounter");
            Check(tanks.StartChase() && tanks.Chasing && tanks.ShotsLeft == TankBattle.ChaseShots, "tank driver's mother: the tanks open fire, ten shots");
            Check(!tanks.StartChase(), "a second hit does not stack");
            tanks.EndRevenge();
            Check(!tanks.Chasing && tanks.ShotsLeft == 0, "the revenge can be called off");
            Check(tanks.StartChase(), "...and started again");
            tanks.Reset();
            Check(!tanks.Chasing && tanks.ShotsLeft == 0, "a new run resets the tank revenge");
            tanks.QueueFree();
            tankFx.QueueFree();
        }
    }
}
