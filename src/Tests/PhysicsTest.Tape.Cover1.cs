using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CCCPRacing.Car;
using CCCPRacing.Race;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

/// <summary>
/// Coverage batch 1 (docs/replay-tape.md section 21): the production chain for each newly taped actor, with the real actors on the real tracks, the real recorder, the real
/// codec and pair file (SavePair, Load), the real player, a seek into the middle of each event, the loop (a jump back), the exit (Release and TapeDriven off) and the
/// compatibility with a tape that does not know the actor. Each case also holds a negative control: the same live update with the actor NOT gated overwrites the replay's poses.
/// --tape-cover1-tests
/// </summary>
public partial class PhysicsTest
{
    /// <summary>One actor group as the test needs it: how to make it (a fresh world each call), how to drive it live, how it behaves when the tape owns it.</summary>
    sealed class CoverRig
    {
        public (string Key, Node3D Root)[] Actors;
        public Action<float, float> Step;           // (dt, clock): the live update of one physics tick, including the forced events
        public Action<float> Idle;                  // what the live code does in a frame when nobody forced anything (the thing the TapeDriven gate must stop)
        public Action<bool> Drive;                  // TapeDriven on all the actors
        public Action Free;
        public Func<bool> Done = () => true;
        public Action<Node3D, string> BornHook;
        public CoverHost Host;
        public string[] ExpectLive = Array.Empty<string>();
    }

    /// <summary>What an actor that makes and frees models tells the tape: the same bookkeeping the scene does (RaceScene.Tape.Instances.cs).</summary>
    sealed class CoverHost : ITapeInstanceHost
    {
        public WorldTapeRecorder Rec; public WorldTapeIndex Index; public string Key; public int Born_, Gone_;
        readonly Dictionary<Node3D, int> _live = new(); int _next;
        public void Born(Node3D n)
        {
            if (!n.HasMeta(WorldTapeConst.KindMeta)) return;
            int id = ++_next; _live[n] = id; Index.AddInstance(Key, id, n); Born_++;
            Rec.Event(new TapeEvent(Rec.Samples / WorldTapeConst.TapeRate, TapeEventType.Spawn, Source: Key, Kind: (string)n.GetMeta(WorldTapeConst.KindMeta), Parent: Key, Instance: id));
        }
        public void Gone(Node3D n)
        {
            if (!_live.Remove(n, out int id)) return;
            Index.RemoveInstance(id); Gone_++;
            Rec.Event(new TapeEvent(Rec.Samples / WorldTapeConst.TapeRate, TapeEventType.Despawn, Source: Key, Instance: id));
        }
    }

    sealed class CoverSink : IWorldTapeSink
    {
        readonly Dictionary<string, Node3D> _roots; public readonly Dictionary<int, Node3D> Live = new(); public int Spawned, Despawned;
        public CoverSink(Dictionary<string, Node3D> roots) { _roots = roots; }
        public Node3D Spawn(string kind, string parent, int instance)
        {
            Node3D n = kind != null && kind.StartsWith("canister/") ? Babushkas.BuildReplayCan(kind["canister/".Length..]) : null;
            if (n == null || !_roots.TryGetValue(parent, out var root)) return null;
            root.AddChild(n); Live[instance] = n; Spawned++; return n;
        }
        public void Despawn(int instance) { if (Live.Remove(instance, out var n)) { n.GetParent()?.RemoveChild(n); n.Free(); Despawned++; } }
        public void Effect(in TapeEvent e, float age) { }
        public void ClearEffects() { }
        public void SetBrokenBreakables(IReadOnlyCollection<int> ids) { }
    }

    static TrackData _coverGhostTrack;

    static string CoverSnapKey(TapePartId id) => id.ActorKey + "|" + id.Part;

    static Dictionary<string, (Transform3D T, bool V)> CoverSnap(WorldTapeIndex index)
    {
        var d = new Dictionary<string, (Transform3D, bool)>();
        for (int i = 0; i < index.Count; i++) { var n = index.NodeOf(i); d[CoverSnapKey(index.IdOf(i))] = (n.Transform, n.Visible); }
        return d;
    }

    /// <summary>The recorded sound values (the limbs' stretch) of every part that has any, by part and channel.</summary>
    static Dictionary<string, float> CoverSounds(WorldTapeIndex index)
    {
        var d = new Dictionary<string, float>();
        for (int i = 0; i < index.Count; i++)
            if (Tape.SoundsOf(index.NodeOf(i)) is { } regs) foreach (var r in regs) d[CoverSnapKey(index.IdOf(i)) + "#" + r.Channel] = r.Get();
        return d;
    }

    /// <summary>The worst difference between two snapshots: position, basis columns, and 1000 for a visibility or part-set mismatch.</summary>
    static float CoverDiff(Dictionary<string, (Transform3D T, bool V)> want, Dictionary<string, (Transform3D T, bool V)> got, out string worstPart)
    {
        worstPart = null; float worst = 0;
        foreach (var (k, w) in want)
        {
            float d;
            if (!got.TryGetValue(k, out var g)) d = 1000f;
            else
            {
                d = Math.Max(w.T.Origin.DistanceTo(g.T.Origin), Math.Max((w.T.Basis.X - g.T.Basis.X).Length(), Math.Max((w.T.Basis.Y - g.T.Basis.Y).Length(), (w.T.Basis.Z - g.T.Basis.Z).Length())));
                if (w.V != g.V) d = 1000f;
            }
            if (d > worst) { worst = d; worstPart = k; }
        }
        foreach (var k in got.Keys) if (!want.ContainsKey(k)) { worst = 1000f; worstPart = k + " (extra)"; }
        return worst;
    }

    void CoverTape1Tests()
    {
        GD.Print("Tape coverage batch 1: production chain per actor");
        CoverCase("siberia traffic cones", CoverConesRig, 8f, new[] { 1.0f, 2.0f, 3.5f }, "None:TrafficCones");
        CoverCase("ilyich barn doors", CoverDoorsRig, 10f, new[] { 1.5f, 3.0f, 8.0f }, "None:BarnDoorsTractor");
        CoverCase("ilyich kolkhoz workers", CoverWorkersRig, 10f, new[] { 2.0f, 3.2f, 4.5f }, "None:KolkhozWorkers");
        CoverCase("ilyich dacha guards", CoverGuardsRig, 10f, new[] { 1.0f, 3.2f, 4.5f }, "None:DachaGuards");
        CoverCase("poligon exercise crew", CoverCrewRig, 12f, new[] { 1.2f, 3.5f, 8.5f }, "Jets:ExerciseCrew");
        CoverCase("canisters flying (ilyich babushkas)", () => CoverCanisterRig("ilyich", false), 8f, new[] { 1.9f, 2.1f, 2.25f }, "Babushkas:Babushkas");
        CoverCase("canisters flying, golden (siberia babushkas)", () => CoverCanisterRig("siberia", true), 8f, new[] { 1.9f, 2.1f, 2.25f }, "Babushkas:Babushkas");
        CoverNestedRootCase();
        GD.Print(_failures == 0 ? "Tape coverage batch 1: all passed" : $"Tape coverage batch 1: {_failures} FAILED");
    }

    void CoverCase(string label, Func<CoverRig> make, float seconds, float[] eventTimes, string coverKey)
    {
        const float dt = 1f / 120f;
        string dir = ProjectSettings.GlobalizePath("user://cover1-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        var src = make();
        var index = new WorldTapeIndex(src.Actors);
        Check(index.Count > 0 && src.Actors.All(a => index.StructureOf(a.Key).ParentPaths.ContainsKey("root")), $"cover1 {label}: the actors are indexed ({index.Count} parts) and their roots are marked");
        var rest = CoverSnap(index);
        var rec = new WorldTapeRecorder(); rec.Begin(index);
        if (src.Host != null) { src.Host.Rec = rec; src.Host.Index = index; }
        var expected = new Dictionary<int, Dictionary<string, (Transform3D T, bool V)>>();
        var soundAt = new List<Dictionary<string, float>>();
        _coverGhostTrack ??= TrackBuilder.Build(TrackDef.Load("res://tracks/ilyich.json"));
        var ghost = new Ghost(); var ghostCar = new CarSim(new CarParams(), _coverGhostTrack); ghostCar.Reset(Vector3.Zero, 0);
        int ticks = (int)MathF.Round(seconds / dt);
        for (int n = 0; n <= ticks; n++)
        {
            float clock = n * dt;
            src.Step(dt, clock);
            int before = rec.Samples; rec.Record(clock); ghost.Record(clock, ghostCar, 0, 0, 0);
            if (rec.Samples > before) expected[rec.Samples - 1] = CoverSnap(index);
            soundAt.Add(CoverSounds(index));
        }
        Check(src.Done(), $"cover1 {label}: the forced events happened in the live run");
        var savedGhost = ghost.CopyWithTime(seconds); var tape = rec.Finish(seconds, WorldTapeFingerprint.Of(savedGhost)); tape.TrackId = "cover";
        var pair = WorldTapeFile.SavePair(dir + "/ghost_cover.bin", dir + "/tape_cover.bin", savedGhost, tape, WorldTapeRealOps.Instance, WorldTapeCodec.Encode, WorldTapeCodec.Decode);
        var loaded = WorldTapeFile.Load(dir + "/tape_cover.bin", savedGhost, WorldTapeCodec.Decode);
        long bytes = new FileInfo(dir + "/tape_cover.bin").Length;
        Check(pair.Whole && loaded != null && rec.UnscaledChanges == 0, $"cover1 {label}: the pair is saved and reloaded ({tape.SampleCount} samples, {bytes / 1024} KB), no part of it changes scale (unscaled changes {rec.UnscaledChanges})");
        GD.Print($"cover1 {label}: {index.Count} parts, {tape.SampleCount} samples, {bytes / 1024} KB, unregistered spins {rec.UnregisteredSpinners}");
        if (loaded == null) { src.Free(); return; }
        src.Free();

        // ---- the replay world: built fresh, index taken at build, the player and a sink that can make the instances
        var target = make();
        var tindex = new WorldTapeIndex(target.Actors);
        var tsink = new CoverSink(target.Actors.ToDictionary(a => a.Key, a => a.Root));
        var player = new WorldTapePlayer(loaded, tindex, tsink);
        Check(target.Actors.All(a => player.Covers(a.Key)), $"cover1 {label}: a fresh production world's structure matches the tape for {string.Join(", ", target.Actors.Select(a => a.Key))}");
        target.Drive(true);
        int[] samples = expected.Keys.OrderBy(k => k).ToArray();
        string worstAt = null; float worstSeq = 0; float worstSound = 0; string worstSoundAt = null;
        foreach (int s in samples)
        {
            player.Apply(s / WorldTapeConst.TapeRate, 1f / WorldTapeConst.TapeRate);
            float d = CoverDiff(expected[s], CoverSnap(tindex), out var w);
            if (d > worstSeq) { worstSeq = d; worstAt = $"sample {s} {w}"; }
            // the 10 Hz sound channels: the replay holds the value of the last sound sample at or before this time
            int tick = (int)MathF.Round(MathF.Floor(s / WorldTapeConst.TapeRate * WorldTapeConst.SoundRate + 1e-3f) / WorldTapeConst.SoundRate / dt);
            if (tick >= 0 && tick < soundAt.Count)
                foreach (var (k, v) in CoverSounds(tindex))
                    if (soundAt[tick].TryGetValue(k, out var want) && Math.Abs(want - v) > worstSound) { worstSound = Math.Abs(want - v); worstSoundAt = $"sample {s} {k}"; }
        }
        Check(worstSeq < 0.02f, $"cover1 {label}: playing it frame by frame reproduces every recorded pose (worst {worstSeq:F4} at {worstAt})");
        Check(worstSound < 0.05f, $"cover1 {label}: the recorded limb stretch comes back (worst {worstSound:F4} {worstSoundAt})");
        // seek into the middle of each event, and 24 pseudo-random clocks
        var rng = new Random(11);
        var seeks = new List<int>();
        foreach (float te in eventTimes) { int s0 = (int)MathF.Round(te * WorldTapeConst.TapeRate); foreach (int o in new[] { 0, 1, 3 }) seeks.Add(Math.Min(samples[^1], s0 + o)); }
        for (int k = 0; k < 24; k++) seeks.Add(samples[rng.Next(samples.Length)]);
        float worstSeek = 0; string worstSeekAt = null;
        foreach (int s in seeks)
        {
            player.Seek(s / WorldTapeConst.TapeRate);
            float d = CoverDiff(expected[s], CoverSnap(tindex), out var w);
            if (d > worstSeek) { worstSeek = d; worstSeekAt = $"sample {s} {w}"; }
        }
        Check(worstSeek < 0.02f, $"cover1 {label}: a seek into the middle of an event (and 24 random ones) equals what playing shows (worst {worstSeek:F4} at {worstSeekAt})");
        // the loop: the clock jumps back to the start (Apply sees it as a jump and seeks), ten times
        float worstLoop = 0;
        for (int loop = 0; loop < 10; loop++)
        {
            player.Apply(samples[^1] / WorldTapeConst.TapeRate, 1f / 15f);
            player.Apply(0f, 1f / 15f);
            worstLoop = Math.Max(worstLoop, CoverDiff(expected[0], CoverSnap(tindex), out _));
        }
        Check(worstLoop < 0.02f && tsink.Live.Count == 0, $"cover1 {label}: ten loops restore the first sample each time and leave no instance behind ({tsink.Live.Count} left, worst {worstLoop:F4})");
        // the live update cannot overwrite the replay while the actor is tape-driven (an instance rig, the cans in the air, has no live update to gate: its replay model has no flight code at all)
        int mid = seeks[4]; player.Seek(mid / WorldTapeConst.TapeRate);
        var posed = CoverSnap(tindex);
        if (target.Host == null)
        {
            for (int k = 0; k < 30; k++) target.Idle(dt);
            float held = CoverDiff(posed, CoverSnap(tindex), out var heldPart);
            Check(held < 1e-5f, $"cover1 {label}: the live update, run 30 frames over the replay, changes nothing while the tape drives ({held:F6} {heldPart})");
        }
        else Check(tsink.Live.Count > 0 && tindex.Count > tsink.Live.Count, $"cover1 {label}: a seek into the flight makes the can ({tsink.Live.Count} instance(s) in the replay, made by the sink's factory)");
        // leaving the replay: everything the replay made is gone, the actors are live again
        player.Release(); target.Drive(false);
        Check(tsink.Live.Count == 0, $"cover1 {label}: leaving the replay frees every instance it made");
        target.Free();

        // negative control: the same replay with the actor NOT gated, then the live update, must damage the poses (so the check above can fail); an instance rig: a sink that cannot make the model
        var control = make(); var cindex = new WorldTapeIndex(control.Actors);
        var cplayer = new WorldTapePlayer(loaded, cindex, new CoverSink(control.Host == null ? control.Actors.ToDictionary(a => a.Key, a => a.Root) : new Dictionary<string, Node3D>()));
        cplayer.Seek(mid / WorldTapeConst.TapeRate);
        var cposed = CoverSnap(cindex);
        string damagedPart;
        float damaged;
        if (control.Host == null)
        {
            for (int k = 0; k < 30; k++) control.Idle(dt);
            damaged = CoverDiff(cposed, CoverSnap(cindex), out damagedPart);
        }
        else damaged = CoverDiff(expected[mid], cposed, out damagedPart);
        Check(damaged > 1e-3f, $"cover1 {label}: negative control: {(control.Host == null ? "without the TapeDriven gate the live update does overwrite the replay" : "a sink that cannot make the can leaves the replay without it")} ({damaged:F4} at {damagedPart})");
        cplayer.Release(); control.Free();

        // compatibility: a tape that does not know this actor (an older recording) covers nothing of it, and invents nothing
        var legacyWorld = make(); var legacyIdx = new WorldTapeIndex(legacyWorld.Actors);
        var oldRec = new WorldTapeRecorder(); var emptyIndex = new WorldTapeIndex(Array.Empty<(string, Node3D)>());
        oldRec.Begin(emptyIndex); for (int n = 0; n < 120; n++) oldRec.Record(n * dt);
        var oldTape = WorldTapeCodec.Decode(WorldTapeCodec.Encode(oldRec.Finish(1f, 1)));
        var oldPlayer = new WorldTapePlayer(oldTape, legacyIdx, new CoverSink(new Dictionary<string, Node3D>()));
        var before0 = CoverSnap(legacyIdx);
        oldPlayer.Seek(0.5f);
        Check(!oldPlayer.Covers(coverKey) && CoverDiff(before0, CoverSnap(legacyIdx), out _) == 0f, $"cover1 {label}: an older tape without this actor covers nothing of it and poses nothing ({coverKey} stays live)");
        legacyWorld.Free();
        TapeSceneRm(dir);
    }

    // ------------------------------------------------------------------ the rigs

    CoverRig CoverConesRig()
    {
        var track = TrackBuilder.Build(TrackDef.Load("res://tracks/siberia.json"));
        var cones = TrafficCones.Build(track); AddChild(cones);
        Check(cones != null, "cover1 siberia cones: the track has cones");
        var car = new CarSim(new CarParams(), track);
        Vector3 Home(int k) => ((Node3D)cones.GetChild(k)).Position;
        void Away() { car.Pos = new Vector3(-5000, 0, -5000); car.Vel = Vector3.Zero; }
        void Hit(int k) { car.Pos = Home(k) + new Vector3(0.8f, 0, 0.3f); car.Vel = new Vector3(14f, 0, 2f); }
        int hits = 0;
        return new CoverRig
        {
            Actors = new[] { ("None:TrafficCones", (Node3D)cones) },
            Step = (dt, clock) =>
            {
                if (hits == 0 && clock >= 1.0f) { Hit(4); hits++; }
                else if (hits == 1 && clock >= 2.0f) { Hit(11); hits++; }
                else if (hits == 2 && clock >= 3.5f) { Hit(12); hits++; }
                cones.Tick(dt, car);
                Away();
            },
            Idle = dt => { Hit(25); cones.Tick(dt, car); Away(); },
            Drive = cones.SetTapeDriven,
            Done = () => cones.Knocked >= 3,
            Free = () => { cones.QueueFree(); car = null; },
        };
    }

    CoverRig CoverDoorsRig()
    {
        var door = new BarnDoors(3.44f, 3.45f, 6.55f, new Color(0.4f, 0.25f, 0.13f), new Color(0.2f, 0.12f, 0.06f)); AddChild(door);
        bool opened = false;
        return new CoverRig
        {
            Actors = new[] { ("None:BarnDoorsTractor", (Node3D)door) },
            Step = (dt, clock) => { float a = clock < 1f ? 0f : clock < 4f ? (clock - 1f) / 3f : clock < 6f ? 1f : Math.Max(0f, 1f - (clock - 6f) / 2f); door.SetOpen(a); opened |= a >= 1f; },
            Idle = dt => door.SetOpen(1f),
            Drive = door.SetTapeDriven,
            Done = () => opened,
            Free = () => door.QueueFree(),
        };
    }

    CoverRig CoverWorkersRig()
    {
        var track = TrackBuilder.Build(TrackDef.Load("res://tracks/ilyich.json"));
        var workers = KolkhozWorkers.Build(track); AddChild(workers);
        var hits = workers.Knocks().ToArray();
        Check(workers.Count >= 20 && hits.Length == workers.Count, $"cover1 workers: Ilyich has {workers.Count} workers, each with his own obstacle ({hits.Length})");
        Vector3 start = ((Node3D)workers.GetChild(0)).Position + new Vector3(-50f, 0, -2f);
        int knocked = 0;
        var saved = SeeThrough.Car;
        return new CoverRig
        {
            Actors = new[] { ("None:KolkhozWorkers", (Node3D)workers) },
            Step = (dt, clock) =>
            {
                SeeThrough.Car = start + new Vector3(20f * clock, 0, 0);      // a car tearing past the first worker (the fist and the glance)
                if (knocked == 0 && clock >= 2.0f) { knocked += hits[2].hit(new Vector3(6, 0, 2)) ? 1 : 0; }
                if (knocked == 1 && clock >= 3.2f) { knocked += hits[15].hit(new Vector3(-4, 0, 3)) ? 1 : 0; }
                if (knocked == 2 && clock >= 4.5f) { knocked += hits[24 < hits.Length ? 24 : hits.Length - 1].hit(new Vector3(0, 0, 6)) ? 1 : 0; }
                workers._Process(dt);
            },
            Idle = dt => { workers._Process(dt); },
            Drive = workers.SetTapeDriven,
            Done = () => knocked == 3,
            Free = () => { SeeThrough.Car = saved; workers.QueueFree(); },
        };
    }

    CoverRig CoverGuardsRig()
    {
        var track = TrackBuilder.Build(TrackDef.Load("res://tracks/ilyich.json"));
        var grounds = PartyDacha.Grounds(track); AddChild(grounds);
        var guards = grounds.FindChild("DachaGuards", true, false) as KnockGroup;
        Check(guards != null && guards.Count == 3, "cover1 guards: the dacha has its three security men");
        var patrol = guards.GetChildren().OfType<DachaPatrol>().First();
        var hits = guards.Knocks().ToArray();
        int knocked = 0; var saved = SeeThrough.Car;
        return new CoverRig
        {
            Actors = new[] { ("None:DachaGuards", (Node3D)guards) },
            Step = (dt, clock) =>
            {
                SeeThrough.Car = guards.Figure(0).Position + new Vector3(8f - clock, 0, 3f);      // a car near the gate man: he turns to watch it
                if (knocked == 0 && clock >= 1.0f) knocked += hits[0].hit(new Vector3(5, 0, 1)) ? 1 : 0;
                if (knocked == 1 && clock >= 3.2f) knocked += hits[2].hit(new Vector3(-3, 0, 4)) ? 1 : 0;
                patrol._Process(dt); guards._Process(dt);
            },
            Idle = dt => { patrol._Process(dt); guards._Process(dt); },
            Drive = guards.SetTapeDriven,
            Done = () => knocked == 2,
            Free = () => { SeeThrough.Car = saved; grounds.QueueFree(); },
        };
    }

    CoverRig CoverCrewRig()
    {
        var track = TrackBuilder.Build(TrackDef.Load("res://tracks/bonus.json"));
        var fx = new TyreEffects(); fx.Init(track); AddChild(fx);
        var crew = new BonusExerciseLife { Name = "ExerciseCrew" }; AddChild(crew); crew.Init(track, fx);
        var car = new CarSim(new CarParams(), track);
        var probe = new WorldTapeIndex(new[] { ("Jets:ExerciseCrew", (Node3D)crew) });
        probe.TryFind(new TapePartId("Jets:ExerciseCrew", "1/figure"), out int figure1);
        var person1 = probe.NodeOf(figure1);
        void Away() { car.Pos = new Vector3(-5000, 0, -5000); car.Vel = Vector3.Zero; }
        bool hit = false;
        Vector3 shot = person1.Position;
        return new CoverRig
        {
            Actors = new[] { ("Jets:ExerciseCrew", (Node3D)crew) },
            Step = (dt, clock) =>
            {
                if (!hit && clock >= 1.0f) { car.Pos = person1.Position + new Vector3(0.5f, 0, 0); car.Vel = new Vector3(6, 0, 0); hit = true; }
                else Away();
                if (clock >= 8.0f && clock < 8.0f + dt) crew.Shot(shot, null);
                crew.Tick(dt, car, null, clock >= 3f && clock < 6f);
                Away();
            },
            Idle = dt => { crew.Tick(dt, null, null, true); },
            Drive = crew.SetTapeDriven,
            Done = () => hit,
            Free = () => { crew.QueueFree(); fx.QueueFree(); },
        };
    }

    CoverRig CoverCanisterRig(string trackId, bool golden)
    {
        var track = TrackBuilder.Build(TrackDef.Load($"res://tracks/{trackId}.json"));
        var babushkas = trackId == "bonus" ? new Troopers { Name = "Troopers" } : new Babushkas { Name = "Babushkas" };
        AddChild(babushkas); babushkas.Init(track, track.BabushkaCount);
        babushkas.Canisters = true;
        var car = new CarSim(new CarParams(), track);
        var host = new CoverHost { Key = "Babushkas:Babushkas" };
        babushkas.TapeHost = host;
        int caught = 0, tossed = 0; bool goldenSeen = false;
        babushkas.CanCaught += (_, g) => { caught++; goldenSeen |= g; };
        babushkas.CanTossed += _ => tossed++;
        bool aimed = false;
        return new CoverRig
        {
            Host = host,
            Actors = new[] { ("Babushkas:Babushkas", (Node3D)babushkas) },
            Step = (dt, clock) =>
            {
                if (!aimed && clock >= 0.5f) { babushkas.AimPass(car, 1, 60, 50 / 3.6f, 20f, 2f, golden); aimed = true; }
                if (aimed) car.Pos += car.Vel * dt;
                babushkas.Tick(dt, car, 0f, null, false, null);
            },
            Idle = dt => { },
            Drive = _ => { },
            Done = () => tossed == 1 && caught == 1 && goldenSeen == golden && host.Born_ == 1 && host.Gone_ == 1,
            Free = () => babushkas.QueueFree(),
        };
    }

    /// <summary>The crew lives inside the sorties' tree: the sorties' structure must not contain it (a nested root is another actor), and the crew's must hold its own parts.</summary>
    void CoverNestedRootCase()
    {
        var track = TrackBuilder.Build(TrackDef.Load("res://tracks/bonus.json"));
        var fx = new TyreEffects(); fx.Init(track); AddChild(fx);
        var outer = new Node3D { Name = "HindSorties" }; Tape.Part(outer, "root"); Tape.Version(outer, 3); AddChild(outer);
        var inner = new Node3D { Name = "ExerciseCrew" }; Tape.Part(inner, "root"); outer.AddChild(inner);
        var leaf = new Node3D { Name = "Figure" }; Tape.Part(leaf, "0/figure"); inner.AddChild(leaf);
        var alone = new Node3D { Name = "HindSorties" }; Tape.Part(alone, "root"); Tape.Version(alone, 3); AddChild(alone);
        var both = new WorldTapeIndex(new[] { ("Jets:HindSorties", outer), ("Jets:ExerciseCrew", inner) });
        var onlyOuter = new WorldTapeIndex(new[] { ("Jets:HindSorties", alone) });
        Check(both.StructureOf("Jets:HindSorties").Hash == onlyOuter.StructureOf("Jets:HindSorties").Hash && both.StructureOf("Jets:HindSorties").ParentPaths.Count == 1,
            "cover1 nested root: an actor of its own inside another's tree does not change the outer actor's structure (older tapes of the outer one stay compatible)");
        Check(both.StructureOf("Jets:ExerciseCrew").ParentPaths.Count == 2, "cover1 nested root: and the inner actor holds its own parts");
        outer.QueueFree(); alone.QueueFree(); fx.QueueFree();
    }
}
