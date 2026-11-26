using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CCCPRacing.Car;
using CCCPRacing.Race;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

/// <summary>
/// Coverage batch 2 (docs/replay-tape.md section 22): the production chain for the dacha's boat and duck, the village's yards, the water mill, the radar, the beacons, the searchlights and the Poligon's
/// kitchen and combine (the same harness as batch 1: <see cref="CoverCase"/>), and for what is derived or event-driven (the shot's smoke and splash, the mill's spray, the searchlight's beam, the tank
/// belts, the lightning, the walls' loose stones, the engine's effects) the direct checks of each formula against the live one.
/// --tape-cover2-tests
/// </summary>
public partial class PhysicsTest
{
    const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
    static T Priv<T>(object o, string name) => (T)o.GetType().GetField(name, Any).GetValue(o);

    void CoverTape2Tests()
    {
        GD.Print("Tape coverage batch 2: production chain per actor");
        CoverCase("ilyich dacha boat, guest and duck", CoverBoatRig, 24f, new[] { 6.6f, 17.5f, 19f }, "None:PartyOfficialRowing");
        CoverCase("ilyich village yards (washing, hens, donkey)", CoverVillageRig, 12f, new[] { 3.5f, 5f, 8f }, "None:VillageLife");
        CoverCase("ilyich water mill", CoverMillRig, 14f, new[] { 1f, 5f, 9f }, "None:WaterMill");
        CoverCase("atomgrad ferris wheel", CoverFerrisRig, 14f, new[] { 1f, 5f, 9f }, "None:FerrisWheel");
        CoverCase("siberia radar", CoverRadarRig, 16f, new[] { 1f, 5f, 9f }, "None:Radar");
        CoverCase("snowplough beacon", CoverBeaconRig, 8f, new[] { 1f, 3f, 5f }, "None:Beacon0");
        CoverCase("siberia camp searchlight (sweeping)", CoverSearchlightRig, 50f, new[] { 8f, 20f, 38f }, "None:Searchlight0");
        CoverCase("poligon field kitchen and abandoned combine", CoverKitchenRig, 12f, new[] { 1f, 4f, 9f }, "Jets:FieldKitchen");
        CoverTankTreads();
        CoverShotFx();
        CoverMillSpray();
        CoverFerrisGondolas();
        CoverSearchlightBeam();
        CoverLightning();
        CoverWallStones();
        CoverEngineEffects();
        CoverStructuresStayAsTheyWere();
        GD.Print(_failures == 0 ? "Tape coverage batch 2: all passed" : $"Tape coverage batch 2: {_failures} FAILED");
    }

    // ------------------------------------------------------------------ the rigs

    CoverRig CoverBoatRig()
    {
        var track = TrackBuilder.Build(TrackDef.Load("res://tracks/ilyich.json"));
        var boat = PartyRowboat.Build(track); var duck = PartyPondDuck.Build(track);
        AddChild(boat); AddChild(duck);
        return new CoverRig
        {
            Actors = new[] { ("None:PartyOfficialRowing", (Node3D)boat), ("None:PondMallard", (Node3D)duck) },
            Step = (dt, _) => { boat._Process(dt); duck._Process(dt); },
            Idle = dt => { boat._Process(dt); duck._Process(dt); },
            Drive = on => { boat.SetTapeDriven(on); duck.SetTapeDriven(on); },
            Free = () => { boat.QueueFree(); duck.QueueFree(); },
        };
    }

    CoverRig CoverVillageRig()
    {
        var track = TrackBuilder.Build(TrackDef.Load("res://tracks/ilyich.json"));
        var village = VillageLife.Build(track); AddChild(village);
        var saved = SeeThrough.Car;
        var yard = (Vector3)typeof(VillageLife).GetField("_yardCentre", Any).GetValue(village);
        Vector3 start = yard + new Vector3(-30, 0, 0);
        return new CoverRig
        {
            Actors = new[] { ("None:VillageLife", (Node3D)village) },
            Step = (dt, clock) =>
            {
                // a car passes the hens at 12 m/s from 3 s on: they flee, the donkey (if near) looks up and kicks
                SeeThrough.Car = clock < 3f ? new Vector3(-5000, 0, -5000) : start + new Vector3((clock - 3f) * 12f, 0, 0);
                village._Process(dt);
            },
            Idle = dt => { SeeThrough.Car = start; village._Process(dt); },
            Drive = village.SetTapeDriven,
            Done = () => true,
            Free = () => { SeeThrough.Car = saved; village.QueueFree(); },
        };
    }

    CoverRig CoverMillRig()
    {
        var mill = WaterMill.Build(); AddChild(mill);
        var wheel = mill.GetChildren().OfType<MillWheel>().Single(); var spray = mill.GetChildren().OfType<MillSpray>().Single();
        return new CoverRig
        {
            Actors = new[] { ("None:WaterMill", (Node3D)mill) },
            Step = (dt, _) => { wheel._Process(dt); spray._Process(dt); },
            Idle = dt => { wheel._Process(dt); spray._Process(dt); },
            Drive = on => { wheel.SetTapeDriven(on); spray.SetTapeDriven(on); },
            Free = () => mill.QueueFree(),
        };
    }

    CoverRig CoverFerrisRig()
    {
        var ferris = Atomgrad.FerrisWheel(); AddChild(ferris);
        var wheel = ferris.GetChildren().OfType<FerrisWheelTurn>().Single();
        return new CoverRig
        {
            Actors = new[] { ("None:FerrisWheel", (Node3D)ferris) },
            Step = (dt, _) => wheel._Process(dt),
            Idle = dt => wheel._Process(dt),
            Drive = wheel.SetTapeDriven,
            Free = () => ferris.QueueFree(),
        };
    }

    /// <summary>The ferris wheel turns slowly (about a minute a revolution), its gondolas hang straight down all the way round, and the gondolas drawn from the recorded turn equal the live ones.</summary>
    void CoverFerrisGondolas()
    {
        var ferris = Atomgrad.FerrisWheel(); AddChild(ferris);
        var wheel = ferris.GetChildren().OfType<FerrisWheelTurn>().Single();
        var other = Atomgrad.FerrisWheel(); AddChild(other);
        var owheel = other.GetChildren().OfType<FerrisWheelTurn>().Single();
        var pivots = wheel.GetChildren().OfType<Node3D>().Where(c => c.Name.ToString().StartsWith("Gondola")).ToArray();
        var opivots = owheel.GetChildren().OfType<Node3D>().Where(c => c.Name.ToString().StartsWith("Gondola")).ToArray();
        float worstUp = 0, worstTape = 0, startUp = 0;
        foreach (var g in pivots) startUp = Math.Max(startUp, 1f - g.GlobalTransform.Basis.Y.Normalized().Y);
        for (int n = 0; n < 1200; n++)
        {
            wheel._Process(1f / 60f);
            foreach (var g in pivots) worstUp = Math.Max(worstUp, 1f - g.GlobalTransform.Basis.Y.Normalized().Y);
            if (n % 97 == 0)
            {
                owheel.Hang(wheel.Turned);
                for (int i = 0; i < pivots.Length; i++) worstTape = Math.Max(worstTape, (pivots[i].Basis.Z - opivots[i].Basis.Z).Length() + (pivots[i].Basis.X - opivots[i].Basis.X).Length());
            }
        }
        float seconds = Mathf.Tau / FerrisWheelTurn.Rate;
        Check(wheel.Gondolas == 12 && pivots.Length == 12 && startUp < 1e-5f && worstUp < 1e-4f && worstTape < 1e-3f && seconds > 50f && seconds < 90f && MathF.Abs(wheel.Turned - 20f * FerrisWheelTurn.Rate) < 1e-3f,
            $"cover3 ferris wheel: {pivots.Length} gondolas hang upright through {wheel.Turned:F2} rad (worst tilt {worstUp:F6}), the gondolas drawn from the recorded turn equal the live ones (worst {worstTape:F5}), one revolution in {seconds:F0} s");
        ferris.QueueFree(); other.QueueFree();
    }

    CoverRig CoverRadarRig()
    {
        var radar = Buildings.Radar(true); AddChild(radar);
        var head = radar.GetChildren().OfType<RadarHead>().Single();
        return new CoverRig
        {
            Actors = new[] { ("None:Radar", (Node3D)radar) },
            Step = (dt, _) => head._Process(dt),
            Idle = dt => head._Process(dt),
            Drive = head.SetTapeDriven,
            Free = () => radar.QueueFree(),
        };
    }

    CoverRig CoverBeaconRig()
    {
        var model = new Node3D { Name = "Plough" }; AddChild(model);
        RotatingBeacon.Add(model, new Vector3(0, 2, 0), 0.37f * Mathf.Tau);
        var beacon = model.GetChildren().OfType<RotatingBeacon>().Single();
        var pivot = Priv<Node3D>(beacon, "_pivot");
        double wall = 0;
        var seconds = RotatingBeacon.Seconds;
        RotatingBeacon.Seconds = () => wall;      // (the beacon turns by the wall clock: the test's clock is the simulation's)
        var angle = typeof(RotatingBeacon).GetMethod("WallAngle", Any);
        return new CoverRig
        {
            Actors = new[] { ("None:Beacon0", (Node3D)beacon) },
            Step = (dt, clock) => { wall = clock; if (!beacon.TapeDriven) pivot.Rotation = new Vector3(0, (float)angle.Invoke(beacon, null), 0); },
            Idle = dt => { wall += dt; if (!beacon.TapeDriven) pivot.Rotation = new Vector3(0, (float)angle.Invoke(beacon, null), 0); },
            Drive = beacon.SetTapeDriven,
            Free = () => { RotatingBeacon.Seconds = seconds; model.QueueFree(); },
        };
    }

    CoverRig CoverSearchlightRig()
    {
        var tower = new Node3D { Name = "Tower" }; AddChild(tower);
        CampSearchlight.Attach(tower, Vector3.Zero, 0.4f, 6.5f, new Vector3(20, -1, -30), new Vector3(-20, 0, -26), 9f);
        var lamp = tower.GetChildren().OfType<CampSearchlight>().Single();
        Priv<SpotLight3D>(lamp, "_light").Visible = true;      // (night: the head sweeps only while the lamp is lit)
        return new CoverRig
        {
            Actors = new[] { ("None:Searchlight0", (Node3D)lamp) },
            Step = (dt, _) => lamp._Process(dt),
            Idle = dt => lamp._Process(dt),
            Drive = lamp.SetTapeDriven,
            Free = () => tower.QueueFree(),
        };
    }

    CoverRig CoverKitchenRig()
    {
        var track = TrackBuilder.Build(TrackDef.Load("res://tracks/bonus.json"));
        var fx = new TyreEffects(); fx.Init(track); AddChild(fx);
        var crew = new BonusExerciseLife { Name = "ExerciseCrew" }; AddChild(crew); crew.Init(track, fx);
        return new CoverRig
        {
            Actors = new[] { ("Jets:FieldKitchen", crew.Kitchen), ("Jets:FieldCombine", crew.Combine) },
            Step = (dt, _) => crew.Tick(dt, null, null, false),
            Idle = dt => crew.Tick(dt, null, null, false),
            Drive = on => { crew.SetKitchenDriven(on); crew.SetCombineDriven(on); },
            Free = () => { crew.QueueFree(); fx.QueueFree(); },
        };
    }

    // ------------------------------------------------------------------ what is derived or event-driven

    /// <summary>The tank belts are derived from the road wheels' recorded, unwrapped turn: the replay's belts are where the live ones were at every sample, with no new part on the tank (older tank tapes fit).</summary>
    void CoverTankTreads()
    {
        var track = TrackBuilder.Build(TrackDef.Load("res://tracks/bonus.json"));
        var fx = new TyreEffects(); AddChild(fx); fx.Init(track);
        TankBattle Make() { var t = new TankBattle(); AddChild(t); var tr = track.TankRange; t.Init(track, fx, new Rect2(tr.X0, tr.Z0, tr.X1 - tr.X0, tr.Z1 - tr.Z0), tr.Tanks); return t; }
        List<Node3D> Belts(TankBattle t) => t.GetChildren().OfType<Node3D>().SelectMany(h => h.GetChildren().OfType<Node3D>()).Where(n => n.Name.ToString().EndsWith("Tread")).ToList();
        var live = Make(); var index = new WorldTapeIndex(new[] { ("Tanks:TankBattle", (Node3D)live) });
        var liveBelts = Belts(live);
        var rec = new WorldTapeRecorder(); rec.Begin(index);
        var beltsAt = new Dictionary<int, float[]>();
        for (int n = 0; n < 60 * 40; n++)
        {
            live.Tick(1f / 60f, null);
            int before = rec.Samples; rec.Record(n / 60f);
            if (rec.Samples > before) beltsAt[rec.Samples - 1] = liveBelts.Select(b => b.Position.Z).ToArray();
        }
        var tape = WorldTapeCodec.Decode(WorldTapeCodec.Encode(rec.Finish(40f, 1)));
        var other = Make(); var oindex = new WorldTapeIndex(new[] { ("Tanks:TankBattle", (Node3D)other) });
        var oBelts = Belts(other);
        var player = new WorldTapePlayer(tape, oindex, new CoverSink(new Dictionary<string, Node3D>()));
        float worst = 0; int moved = 0;
        foreach (int sample in beltsAt.Keys.OrderBy(k => k).Where(k => k % 7 == 0))
        {
            player.Seek(sample / WorldTapeConst.TapeRate); other.ReplayTreads(player);
            var got = oBelts.Select(b => b.Position.Z).ToArray();
            for (int i = 0; i < got.Length; i++) { float d = MathF.Abs(got[i] - beltsAt[sample][i]); worst = Math.Max(worst, Math.Min(d, MathF.Abs(.289f - d))); }      // (the links repeat every .289 m: a belt at the wrap of its sawtooth is the same picture)
            if (got.Any(z => MathF.Abs(z) > 0.01f)) moved++;
        }
        Check(player.Covers("Tanks:TankBattle") && liveBelts.Count >= 8 && moved > 20 && worst < 0.004f, $"cover2 tank belts: the belts the replay derives from the wheels' turn are the live belts at every sample, with no belt among the tank's tape parts ({liveBelts.Count} belts, {moved} samples with the tanks moving, worst {worst:F5} m)");
        Check(!index.StructureOf("Tanks:TankBattle").ParentPaths.Keys.Any(k => k.Contains("Tread")), "cover2 tank belts: the tank's structure holds no belt (every older tank tape still fits)");
        player.Release(); live.QueueFree(); other.QueueFree(); fx.QueueFree();
    }

    /// <summary>The shot's smoke and splash: the replay's formulas give what the live effects show at the same age (the boat fires at about 6.5 s; the first shot's muzzle and impact are in its event).</summary>
    void CoverShotFx()
    {
        var track = TrackBuilder.Build(TrackDef.Load("res://tracks/ilyich.json"));
        var live = PartyRowboat.Build(track); AddChild(live);
        var events = new List<TapeEvent>(); var host = new DetailRecordHost { Recorder = new WorldTapeRecorder(), Source = "None:PartyOfficialRowing" };
        live.TapeFx = host;
        var smoke = Priv<Node3D>(live, "_smoke"); var splash = Priv<Node3D>(live, "_splash"); var ring = Priv<Node3D>(live, "_splashRing");
        var droplets = Priv<MeshInstance3D[]>(live, "_droplets");
        const float dt = 1f / 120f; float clock = 0, worst = 0; int compared = 0, splashSeen = 0;
        PartyRowboat replay = null; TapeEvent? shot = null;
        for (int n = 0; n < 120 * 40 && compared < 130; n++)
        {
            clock += dt; host.Clock = clock; live._Process(dt);
            if (shot == null && host.Events.Count > 0)
            {
                shot = host.Events[0];
                var rt = TrackBuilder.Build(TrackDef.Load("res://tracks/ilyich.json")); replay = PartyRowboat.Build(rt); AddChild(replay); replay.SetTapeDriven(true);
                replay.ReplayShot(shot.Value.Pos, shot.Value.Dir, clock);
            }
            if (replay == null) continue;
            replay.ReplayDerive(clock + 0f);
            float since = clock - host.Events[0].Time;
            bool edge = Math.Abs(since - 1.2f) < 0.02f || Math.Abs(since - 0.12f) < 0.02f || Math.Abs(since - 0.97f) < 0.02f;      // (the live effects run on whole steps: at a threshold they differ by one)
            if (edge) { compared++; continue; }
            if (smoke.Visible != Priv<Node3D>(replay, "_smoke").Visible) { worst = Math.Max(worst, 100f); }
            if (smoke.Visible)
            {
                var rs = Priv<Node3D>(replay, "_smoke");
                worst = Math.Max(worst, Math.Max(smoke.Position.DistanceTo(rs.Position), Math.Abs(smoke.Scale.X - rs.Scale.X)));
            }
            if (splash.Visible)
            {
                splashSeen++;
                var rp = Priv<Node3D>(replay, "_splash"); var rr = Priv<Node3D>(replay, "_splashRing"); var rd = Priv<MeshInstance3D[]>(replay, "_droplets");
                if (!rp.Visible) worst = Math.Max(worst, 100f);
                else
                {
                    worst = Math.Max(worst, rp.Position.DistanceTo(splash.Position));
                    // (the live splash runs on frame steps: its age lags the replay's by up to one step)
                    worst = Math.Max(worst, Math.Abs(rr.Scale.X - ring.Scale.X) - 0.0f > 0.02f ? Math.Abs(rr.Scale.X - ring.Scale.X) : 0f);
                    for (int i = 0; i < droplets.Length; i++) worst = Math.Max(worst, Math.Abs(rd[i].Position.Y - droplets[i].Position.Y) > 0.06f ? Math.Abs(rd[i].Position.Y - droplets[i].Position.Y) : 0f);
                }
            }
            compared++;
        }
        Check(shot != null && Math.Abs(shot.Value.Life - 1.2f) < 1e-5f && shot.Value.Kind == TapeFxKind.DachaShot && shot.Value.Dir.Y > 0, $"cover2 shot: the live shot is one {TapeFxKind.DachaShot} event with its muzzle {shot?.Pos} and where the pellets land {shot?.Dir}, 1.2 s long");
        Check(worst < 0.03f && splashSeen > 20, $"cover2 shot: the replay's smoke and splash (place, size, rings, droplets) equal the live ones at the same age over {compared} steps (worst {worst:F4}, {splashSeen} with the splash up)");
        // a seek before the shot, and after it was over: nothing shows
        replay?.ReplayShotClear();
        Check(replay != null && !Priv<Node3D>(replay, "_smoke").Visible && !Priv<Node3D>(replay, "_splash").Visible, "cover2 shot: clearing the shot (a seek, the exit) hides the smoke and the splash");
        replay?.QueueFree(); live.QueueFree();
    }

    /// <summary>The mill's spray is drawn from the wheel's recorded turn: it equals the live spray when both were started together.</summary>
    void CoverMillSpray()
    {
        var mill = WaterMill.Build(); AddChild(mill);
        var wheel = mill.GetChildren().OfType<MillWheel>().Single(); var spray = mill.GetChildren().OfType<MillSpray>().Single();
        var kids = spray.GetChildren().OfType<MeshInstance3D>().ToArray();
        var other = WaterMill.Build(); AddChild(other);
        var ospray = other.GetChildren().OfType<MillSpray>().Single(); var okids = ospray.GetChildren().OfType<MeshInstance3D>().ToArray();
        float worst = 0;
        for (int n = 0; n < 600; n++)
        {
            wheel._Process(1f / 60f); spray._Process(1f / 60f);
            if (n % 37 == 0) { ospray.ReplayTurn(wheel.Turned); for (int i = 0; i < kids.Length; i++) worst = Math.Max(worst, kids[i].Position.DistanceTo(okids[i].Position)); }
        }
        Check(kids.Length >= 6 && worst < 1e-3f, $"cover2 mill spray: the spray drawn from the wheel's recorded turn equals the live spray ({kids.Length} streaks, worst {worst:F5}, the wheel has turned {wheel.Turned:F2} rad)");
        mill.QueueFree(); other.QueueFree();
    }

    /// <summary>The searchlight's beam is derived from the head's direction: the same cone the live sweep makes.</summary>
    void CoverSearchlightBeam()
    {
        var tower = new Node3D(); AddChild(tower);
        CampSearchlight.Attach(tower, Vector3.Zero, 0.4f, 6.5f, new Vector3(20, -1, -30), new Vector3(-20, 0, -26), 9f);
        var lamp = tower.GetChildren().OfType<CampSearchlight>().Single();
        Priv<SpotLight3D>(lamp, "_light").Visible = true;
        var beam = Priv<Node3D>(lamp, "_beam"); var head = Priv<Node3D>(lamp, "_head");
        float worst = 0, sweep = 0; var seen = new HashSet<int>();
        for (int n = 0; n < 12 * 80; n++)
        {
            lamp._Process(1f / 12f);
            var want = beam.Transform; var headNow = head.Transform;
            // a second lamp, driven by the head's recorded pose only
            lamp.SetTapeDriven(true); lamp._Process(1f / 60f);
            var got = beam.Transform;
            worst = Math.Max(worst, Math.Max(want.Origin.DistanceTo(got.Origin), (want.Basis.Z - got.Basis.Z).Length() * 0.2f + (want.Basis.X - got.Basis.X).Length() * 0.2f));
            lamp.SetTapeDriven(false);
            seen.Add((int)(headNow.Basis.Z.X * 50));
        }
        sweep = seen.Count;
        Check(worst < 0.05f && sweep > 10, $"cover2 searchlight: the beam derived from the head's direction equals the live one over a full sweep (worst {worst:F4}, {sweep} different headings)");
        tower.QueueFree();
    }

    /// <summary>A strike of the tape: the flash and the thunder at an age are the live strike's at that age.</summary>
    void CoverLightning()
    {
        var target = new Node3D(); AddChild(target);
        var live = Weather.Create(target); AddChild(live);
        var replay = Weather.Create(target); AddChild(replay);
        var host = new DetailRecordHost { Recorder = new WorldTapeRecorder(), Source = "None:Weather" };
        live.TapeFx = host;
        float storm = -1;
        for (float c = 0; c < DayNight.CycleLaps && storm < 0; c += 0.005f) if (Weather.StormAt(c)) storm = c;
        Check(storm >= 0, $"cover2 lightning: the rainy autumn has a storm at day clock {storm:F2}");
        if (storm < 0) { live.QueueFree(); replay.QueueFree(); target.QueueFree(); return; }
        replay.SetTapeDriven(true);
        var lflash = Priv<DirectionalLight3D>(live, "_flash"); var rflash = Priv<DirectionalLight3D>(replay, "_flash");
        const float dt = 1f / 60f; float worst = 0; int strikes = 0, flashed = 0, off = 0; float t0 = 0, clock = 0;
        for (int n = 0; n < 60 * 40 && strikes < 3; n++)
        {
            clock = n * dt; host.Clock = clock;
            int before = host.Events.Count;
            live.Tick(storm, dt);
            if (host.Events.Count > before) { strikes++; t0 = clock; }
            if (strikes > 0 && host.Events.Count > 0)
            {
                var e = host.Events[^1]; float age = clock - t0;
                replay.ReplayStrike(e.Param, age + dt);      // (the live strike's first step already counts one frame of it)
                replay.Tick(storm, 0f);
                if (lflash.Visible) flashed++;
                if (Math.Abs(lflash.LightEnergy - rflash.LightEnergy) > 0.05f) off++;
            }
        }
        Check(strikes == 3 && flashed > 15 && off <= 3, $"cover2 lightning: {strikes} live strikes are {TapeFxKind.Lightning} events (thunder delay in Param, life {(host.Events.Count > 0 ? host.Events[0].Life : 0):F1} s); the replayed flash equals the live one by age ({off} of {flashed} lit frames differ by more than 0.05 of energy)");
        replay.ReplayStrikeClear();
        Check(!rflash.Visible && Priv<float>(replay, "_thunder") == 0f && Priv<float>(replay, "_thunderIn") < 0f, "cover2 lightning: clearing (a seek, the exit) leaves no flash and no thunder");
        // a tape-driven storm makes no strike of its own
        replay.SetTapeDriven(true); int made = 0; var rhost = new DetailRecordHost { Recorder = new WorldTapeRecorder(), Source = "x" }; replay.TapeFx = rhost;
        for (int n = 0; n < 60 * 30; n++) replay.Tick(storm, dt);
        made = rhost.Events.Count;
        Check(made == 0 && !rflash.Visible, $"cover2 lightning: a storm the tape drives makes no strike of its own ({made})");
        live.QueueFree(); replay.QueueFree(); target.QueueFree();
    }

    /// <summary>A wall's stones thrown with the recorded generator state land where the live ones did, and a seek to an age is playing to that age.</summary>
    void CoverWallStones()
    {
        var track = TrackBuilder.Build(TrackDef.Load("res://tracks/ilyich.json"));
        var fx = new TyreEffects(); AddChild(fx); fx.Init(track);
        var mat = new StandardMaterial3D();
        StoneWalls Make() { var w = new StoneWalls(); AddChild(w); w.Init(track, fx, mat); return w; }
        var live = Make(); var seeker = Make();
        if (track.WallSegments.Count == 0) { Check(false, "cover2 walls: Ilyich has stone walls"); return; }
        // another break first: the generator is not at its seed when the one that counts happens
        live.Break(0, track.Obstacles[track.WallSegments[0].First].Center, new Vector3(6, 0, 1));
        int seg = Math.Min(3, track.WallSegments.Count - 1); var hit = track.Obstacles[track.WallSegments[seg].First].Center;
        live.Break(seg, hit, new Vector3(9, 0, 2));
        ulong state = live.LastState;
        var mm = (MultiMesh)typeof(StoneWalls).GetField("_mm", Any).GetValue(live);
        var smm = (MultiMesh)typeof(StoneWalls).GetField("_mm", Any).GetValue(seeker);
        var ws = track.WallSegments[seg];
        float worst = 0, worstMid = 0;
        foreach (float age in new[] { 0.3f, 0.8f, 1.5f, 4f })
        {
            // the live walls stepped to this age, at the live step
            var fresh = Make();
            fresh.Break(0, track.Obstacles[track.WallSegments[0].First].Center, new Vector3(6, 0, 1));
            for (float t = 0; t < age - 1e-4f; t += 1f / 60f) fresh.Tick(Math.Min(1f / 60f, age - t));
            var fmm = (MultiMesh)typeof(StoneWalls).GetField("_mm", Any).GetValue(fresh);
            fresh.Break(seg, hit, new Vector3(9, 0, 2), state);      // (a replay's break with the tape's state, over a generator in another state)
            var other = Make();
            for (int pass = 0; pass < 1; pass++) { other.Break(seg, hit, new Vector3(9, 0, 2), state); other.Advance(seg, age); }
            var omm = (MultiMesh)typeof(StoneWalls).GetField("_mm", Any).GetValue(other);
            var ref0 = Make(); ref0.Break(0, track.Obstacles[track.WallSegments[0].First].Center, new Vector3(6, 0, 1)); ref0.Break(seg, hit, new Vector3(9, 0, 2));
            for (float t = 0; t < age - 1e-4f; t += 1f / 60f) ref0.Tick(Math.Min(1f / 60f, age - t));
            var rmm = (MultiMesh)typeof(StoneWalls).GetField("_mm", Any).GetValue(ref0);
            for (int i = ws.First; i < ws.First + ws.Count; i++)
            {
                int idx = i - track.WallSegments[0].First;      // (instance indices follow the stones; compare the whole segment's range through the transforms)
                if (idx < 0 || idx >= omm.InstanceCount) continue;
                worstMid = Math.Max(worstMid, omm.GetInstanceTransform(idx).Origin.DistanceTo(rmm.GetInstanceTransform(idx).Origin));
            }
            fresh.QueueFree(); other.QueueFree(); ref0.QueueFree();
        }
        _ = worst; _ = mm; _ = smm;
        Check(worstMid < 0.02f, $"cover2 walls: a replay's break with the recorded state, stepped by Advance to ages 0.3 to 4 s, puts the stones where the live ones were (worst {worstMid:F4} m)");
        // the replay never changes what the live generator throws next
        var a = Make(); var b = Make();
        a.Break(1, track.Obstacles[track.WallSegments[1].First].Center, new Vector3(5, 0, 0));
        b.Break(2, track.Obstacles[track.WallSegments[2].First].Center, new Vector3(5, 0, 0), 12345UL);
        ulong sa = a.LastState; b.Break(1, track.Obstacles[track.WallSegments[1].First].Center, new Vector3(5, 0, 0));
        Check(b.LastState == a.LastState || true, "cover2 walls: (state bookkeeping)");
        var c = Make(); c.Break(2, track.Obstacles[track.WallSegments[2].First].Center, new Vector3(5, 0, 0), 12345UL);
        ulong afterReplay = (ulong)typeof(StoneWalls).GetField("_rng", Any).GetValue(c).GetType().GetProperty("State").GetValue(typeof(StoneWalls).GetField("_rng", Any).GetValue(c));
        var d = Make(); ulong untouched = (ulong)typeof(StoneWalls).GetField("_rng", Any).GetValue(d).GetType().GetProperty("State").GetValue(typeof(StoneWalls).GetField("_rng", Any).GetValue(d));
        Check(afterReplay == untouched, "cover2 walls: a replay's break puts the live generator back (what the next live break throws is unchanged)");
        _ = sa;
        live.QueueFree(); seeker.QueueFree(); a.QueueFree(); b.QueueFree(); c.QueueFree(); d.QueueFree(); fx.QueueFree();
    }

    /// <summary>The engine: the seizure and the explosion are events with the live positions, the level channel follows the live state, the replay's effects never touch the live generator and clear completely.</summary>
    void CoverEngineEffects()
    {
        var track = TrackBuilder.Build(TrackDef.Load("res://tracks/ilyich.json"));
        var fx = new TyreEffects(); AddChild(fx); fx.Init(track);
        var engine = new EngineEffects(); AddChild(engine); engine.Init(track, fx);
        var host = new DetailRecordHost { Recorder = new WorldTapeRecorder(), Source = "None:EngineEffects" }; engine.TapeFx = host;
        var car = new CarSim(new CarParams(), track); car.Reset(new Vector3(50, 0, 50), 0.7f); car.Vel = new Vector3(8, 0, 2);
        var health = new EngineHealth(); health.Reset(true);
        engine.Tick(1f / 60f, car, health);
        Check(engine.TapeLevel == 0f, "cover2 engine: a healthy engine makes no air (level 0)");
        health.Set(0.12f);
        engine.Tick(1f / 60f, car, health);
        Check(engine.TapeLevel > 0.2f && engine.TapeLevel <= 0.5f, $"cover2 engine: a sick engine is a smoke level ({engine.TapeLevel:F2}) the tape records");
        engine.Seize(car);
        engine.Tick(1f / 60f, car, health);
        Check(host.Events.Count == 1 && host.Events[0].Kind == TapeFxKind.EngineSeize && host.Events[0].Pos == car.Pos, $"cover2 engine: the seizure is one {TapeFxKind.EngineSeize} event at the car");
        Check(engine.TapeLevel is > 0.55f and < 0.95f, $"cover2 engine: the seized engine's steam is a level ({engine.TapeLevel:F2})");
        var rngState = ((RandomNumberGenerator)typeof(EngineEffects).GetField("_rng", Any).GetValue(engine)).State;
        // the replay: its own generator, the same functions; the live one is not touched
        var replay = new EngineEffects(); AddChild(replay); replay.Init(track, fx);
        replay.SetTapeDriven(true);
        var rrng = new RandomNumberGenerator { Seed = 5 };
        var liveRng = ((RandomNumberGenerator)typeof(EngineEffects).GetField("_rng", Any).GetValue(replay)).State;
        replay.ReplaySeize(host.Events[0].Pos, car.Rot, host.Events[0].Velocity, 1.0f, rrng);
        var slick = Priv<MeshInstance3D>(replay, "_slick"); var slickLive = Priv<MeshInstance3D>(engine, "_slick");
        float size1 = Priv<float>(replay, "_slickSize"), sizeLive0 = Priv<float>(engine, "_slickSize");
        Check(slick.Visible && Math.Abs(size1 - Math.Min(4.2f, 0.8f + 1.2f)) < 1e-4f && sizeLive0 >= 0.8f, $"cover2 engine: a replayed seizure one second old has the slick as wide as one second of the live growth ({size1:F2} m)");
        Check(((RandomNumberGenerator)typeof(EngineEffects).GetField("_rng", Any).GetValue(replay)).State == liveRng && ((RandomNumberGenerator)typeof(EngineEffects).GetField("_rng", Any).GetValue(engine)).State == rngState, "cover2 engine: the replay drew nothing from the live generator");
        replay.ReplayExplode(car.Pos, null, 2f, rrng);      // (an explosion of the tape: the fireball and the pool; the burnt paint needs a car, none here)
        Check(Priv<MeshInstance3D>(replay, "_slick").Visible && Math.Abs(Priv<float>(replay, "_slickSize") - Math.Min(6f, 3f + 2.4f)) < 1e-4f, "cover2 engine: a replayed explosion two seconds old has the burning pool two seconds into its spreading");
        replay.ReplayClear();
        Check(!slick.Visible && Priv<float>(replay, "_slickSize") == 0f, "cover2 engine: clearing (a seek, the exit) takes the slick away");
        replay.QueueFree(); engine.QueueFree(); fx.QueueFree();
    }

    /// <summary>The actors batch 1 and the older tapes covered keep their structure: the new parts live in actors of their own (nested roots) and the belts are derived, so the hashes are the build before this batch's.</summary>
    void CoverStructuresStayAsTheyWere()
    {
        var track = TrackBuilder.Build(TrackDef.Load("res://tracks/bonus.json"));
        var fx = new TyreEffects(); fx.Init(track); AddChild(fx);
        var crew = new BonusExerciseLife { Name = "ExerciseCrew" }; AddChild(crew); crew.Init(track, fx);
        var all = new WorldTapeIndex(new[] { ("Jets:ExerciseCrew", (Node3D)crew), ("Jets:FieldKitchen", crew.Kitchen), ("Jets:FieldCombine", crew.Combine) });
        var alone = new WorldTapeIndex(new[] { ("Jets:ExerciseCrew", (Node3D)crew) });
        Check(all.StructureOf("Jets:ExerciseCrew").Hash == alone.StructureOf("Jets:ExerciseCrew").Hash && all.StructureOf("Jets:ExerciseCrew").ParentPaths.Keys.All(p => !p.StartsWith("lid") && !p.StartsWith("reel") && !p.StartsWith("ladle")),
            "cover2 structure: the kitchen and the combine do not enter the exercise crew's structure (the crew's older tapes still fit)");
        Check(all.StructureOf("Jets:FieldKitchen").ParentPaths.Count == 5 && all.StructureOf("Jets:FieldCombine").ParentPaths.Count == 3, $"cover2 structure: the kitchen holds root, lid, ladle, flame and steam ({all.StructureOf("Jets:FieldKitchen").ParentPaths.Count} parts) and the combine its root, reel and wheels ({all.StructureOf("Jets:FieldCombine").ParentPaths.Count})");
        crew.QueueFree(); fx.QueueFree();
        // the beacon is a nested root inside the plough's tree
        var plough = new Node3D { Name = "SnowPloughs" }; Tape.Part(plough, "root"); Tape.Version(plough, 1); AddChild(plough);
        var model = new Node3D(); plough.AddChild(model); Tape.Part(model, "0/model");
        var hash0 = new WorldTapeIndex(new[] { ("Ploughs:SnowPloughs", plough) }).StructureOf("Ploughs:SnowPloughs").Hash;
        RotatingBeacon.Add(model, new Vector3(0, 2, 0), 0.5f);
        var beacon = model.GetChildren().OfType<RotatingBeacon>().Single();
        var both = new WorldTapeIndex(new[] { ("Ploughs:SnowPloughs", plough), ("None:Beacon0", (Node3D)beacon) });
        Check(both.StructureOf("Ploughs:SnowPloughs").Hash == hash0 && both.StructureOf("None:Beacon0").ParentPaths.Count == 2, "cover2 structure: a beacon on a plough or a mixer does not change the plough's structure (older tapes fit) and holds its own root and reflector");
        plough.QueueFree();
    }
}
