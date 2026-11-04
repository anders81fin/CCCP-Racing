using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CCCPRacing.Audio;
using CCCPRacing.Car;
using CCCPRacing.Race;
using CCCPRacing.Track;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    sealed class DetailRecordHost : ITapeFxHost
    {
        public WorldTapeRecorder Recorder;
        public string Source;
        public float Clock;
        public readonly List<TapeEvent> Events = new();
        public void Fx(in TapeEvent e)
        {
            var hit = e with { Time = Clock, Source = Source };
            Events.Add(hit); Recorder.Event(hit);
        }
    }

    static Node3D DetailHammer(DrunkMen crew)
        => (Node3D)typeof(DrunkMen).GetField("_hammer", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(crew);

    void ReplayDetailTests()
    {
        string output = ProjectSettings.GlobalizePath("res://media/replay-detail-tests-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
        System.IO.Directory.CreateDirectory(output);
        foreach (string id in new[] { "ilyich", "bonus" })
        {
            MechanicDetailTest(id, output);
            LensDetailTest(id, output);
        }
        GD.Print("REPLAY DETAIL TEST OUTPUT " + output);
    }

    WorldTape DetailSaveLoad(WorldTape tape, string path)
    {
        var bytes = WorldTapeCodec.Encode(tape);
        System.IO.File.WriteAllBytes(path, bytes);
        return WorldTapeCodec.Decode(System.IO.File.ReadAllBytes(path));
    }

    void MechanicDetailTest(string id, string output)
    {
        const string key = "Drunks:DrunkMen";
        const float dt = 1f / 120;
        var track = TrackBuilder.Build(TrackDef.Load($"res://tracks/{id}.json"));
        DrunkMen Make()
        {
            var crew = new DrunkMen { Name = "DrunkMen" }; AddChild(crew); crew.Init(track); return crew;
        }
        var source = Make();
        var index = new WorldTapeIndex(new[] { (key, (Node3D)source) });
        var rest = Enumerable.Range(0, index.Count).Select(i => (Node: index.NodeOf(i), Pose: index.NodeOf(i).Transform, Visible: index.NodeOf(i).Visible)).ToArray();
        uint legacyHash = index.StructureOf(key).Hash;
        var rec = new WorldTapeRecorder(); rec.Begin(index); rec.Record(0);
        var host = new DetailRecordHost { Recorder = rec, Source = key }; source.TapeFx = host;
        var car = new CarSim(new CarParams(), new FlatGround());
        var post = source.MechanicPostPosition.Value; car.Reset(new Vector3(post.X + 5, .6f, post.Y), 0);
        var health = new EngineHealth(); health.Reset(true); health.Set(.05f);
        bool done = false; int blows = 0;
        source.Repaired += () => { done = true; health.Repair(); };
        source.Whack += (_, _) => blows++;
        var expected = new Dictionary<int, (bool Visible, Transform3D Pose)> { [0] = (false, Transform3D.Identity) };
        for (int step = 1; step <= 30 * 120; step++)
        {
            float t = step * dt; host.Clock = t;
            source.MechanicTick(dt, car, true, true, !done, health.Health);
            if (source.Repairing && !done) health.RepairTo(.05f + .95f * source.RepairProgress);
            source.Tick(dt, car, 0);
            int prior = rec.Samples; rec.Record(t);
            if (rec.Samples > prior)
            {
                var hammer = DetailHammer(source);
                expected[rec.Samples - 1] = (hammer?.Visible == true, hammer?.GlobalTransform ?? Transform3D.Identity);
            }
        }
        var loaded = DetailSaveLoad(rec.Finish(30, 0xD371UL), output + $"/{id}-mechanic.bin");
        var body = WorldTapeCodec.ReadBody(loaded.Payload);
        Check(done && blows > 0 && body.Events.Count(e => e.Kind == TapeFxKind.MechanicWhack) == blows,
            $"replay mechanic {id}: real repair records every blow, including its seeded effect and strength");
        foreach (var part in rest) { part.Node.Transform = part.Pose; part.Node.Visible = part.Visible; }
        var freshIndex = new WorldTapeIndex(new[] { (key, (Node3D)source) });
        var replay = Make(); var replayIndex = new WorldTapeIndex(new[] { (key, (Node3D)replay) });
        var player = new WorldTapePlayer(loaded, replayIndex, new TapeSink());
        Check(legacyHash == freshIndex.StructureOf(key).Hash && player.Covers(key) && DetailHammer(replay) == null,
            $"replay mechanic {id}: legacy actor structure remains compatible before and after lazy tool creation");
        int shown = 0; float worst = 0; bool visibility = true;
        foreach (var (sample, state) in expected)
        {
            float t = sample / WorldTapeConst.TapeRate;
            player.Apply(t, 1 / WorldTapeConst.TapeRate); replay.RestoreMechanicTapeTool();
            var hammer = DetailHammer(replay);
            visibility &= hammer.Visible == state.Visible;
            if (state.Visible)
            {
                shown++;
                worst = Math.Max(worst, TapeWoodPoseDiff(new[] { state.Pose }, new[] { hammer.GlobalTransform }));
            }
        }
        Check(shown > 5 && visibility && worst < .012f,
            $"replay mechanic {id}: saved arm and bottle state restore actual hammer world pose and visibility ({shown} working samples, error {worst:F5})");
        var active = expected.Where(x => x.Value.Visible).Select(x => x.Key).ToArray();
        if (active.Length == 0) { Check(false, $"replay mechanic {id}: fixture reaches a working repair"); source.Free(); replay.Free(); return; }
        foreach (int sample in new[] { active.First(), active[active.Length / 2], 0, active.Last(), active.First() })
        {
            player.Seek(sample / WorldTapeConst.TapeRate); replay.RestoreMechanicTapeTool();
            var want = expected[sample]; var got = DetailHammer(replay);
            Check(got.Visible == want.Visible && (!want.Visible || TapeWoodPoseDiff(new[] { want.Pose }, new[] { got.GlobalTransform }) < .012f),
                $"replay mechanic {id}: seek/loop at sample {sample} restores the production tool");
        }
        player.Release(); replay.ClearMechanicTapeTool();
        Check(!DetailHammer(replay).Visible, $"replay mechanic {id}: replay exit hides its derived tool");
        var fx = new TyreEffects(); fx.Init(track); AddChild(fx);
        var root = new Node3D(); AddChild(root); var effect = new TapeFxPlayer(fx, track, null, root);
        var blow = body.Events.First(e => e.Kind == TapeFxKind.MechanicWhack);
        effect.Effect(blow, .25f);
        Check(effect.Particles > 0 && root.GetChildren().OfType<ClangSound>().Any(s => Math.Abs(s.StartAge - .25f) < 1e-5f),
            $"replay mechanic {id}: aged blow restores particles and resumes the ring without a fresh strike");
        effect.Clear(); Check(effect.Particles == 0 && root.GetChildCount() == 0, $"replay mechanic {id}: seek/exit removes impact particles and sound");
        effect.Free(); root.Free(); fx.Free(); source.Free(); replay.Free();
    }

    void LensDetailTest(string id, string output)
    {
        string key = id == "bonus" ? "Babushkas:Troopers" : "Babushkas:Babushkas";
        const float dt = 1f / 120;
        var track = TrackBuilder.Build(TrackDef.Load($"res://tracks/{id}.json"));
        Babushkas walkers = id == "bonus" ? new Troopers { Name = "Troopers" } : new Babushkas { Name = "Babushkas" };
        walkers.ForceLens = true; AddChild(walkers); walkers.Init(track, track.BabushkaCount);
        var idx = new WorldTapeIndex(new[] { (key, (Node3D)walkers) });
        walkers.PutOnRoad(0, 100, 0); walkers.KeepAway(walkers.WalkerPosition(0), 180, 0);
        var rec = new WorldTapeRecorder(); rec.Begin(idx); rec.Record(0);
        var host = new DetailRecordHost { Recorder = rec, Source = key }; walkers.TapeFx = host;
        var car = new CarSim(new CarParams(), track); walkers.AimCar(car, 0, 25, 3);
        var recordingGhost = new Ghost(); recordingGhost.Record(0, car, 0, 0, 0, .4f);
        var camera = new Camera3D(); AddChild(camera);
        var native = new Hud { Mode = HudMode.Race }; AddChild(native);
        int lensHits = 0;
        for (int step = 1; step <= 4 * 120; step++)
        {
            float t = step * dt; host.Clock = t; native._Process(dt);
            car.Pos += car.Vel * dt;
            camera.GlobalPosition = car.Pos + new Vector3(0, 31, 23); camera.LookAt(car.Pos, Vector3.Up);
            walkers.Tick(dt, car, 0, camera);
            if (walkers.LensHitThisTick)
            {
                lensHits++; native.LensSplat(walkers.LensScarf, Babushkas.LensHold, id == "bonus", walkers.LensSeedThisTick);
            }
            rec.Record(t);
            recordingGhost.Record(t, car, 0, 0, 0, .4f);
        }
        var ghost = recordingGhost.CopyWithTime(4);
        var tape = rec.Finish(4, WorldTapeFingerprint.Of(ghost)); tape.TrackId = id; tape.Set = 0;
        string pairDir = output + $"/{id}-lens-scene"; System.IO.Directory.CreateDirectory(pairDir);
        var pair = WorldTapeFile.SavePair(pairDir + $"/ghost_{id}.bin", pairDir + $"/tape_{id}.bin", ghost, tape,
            WorldTapeRealOps.Instance, WorldTapeCodec.Encode, WorldTapeCodec.Decode);
        System.IO.File.WriteAllText(pairDir + $"/track_{id}.sig", TrackSignature.Compute(track) + "\n");
        System.IO.File.WriteAllText(pairDir + $"/records_{id}.ver", RecordStore.Layout + "\n");
        Check(pair.Whole, $"replay lens {id}: production pair-save writes matching ghost and world tape for scene playback");
        var loaded = DetailSaveLoad(tape, output + $"/{id}-lens.bin");
        var body = WorldTapeCodec.ReadBody(loaded.Payload);
        var hits = body.Events.Where(e => e.Kind == TapeFxKind.BabushkaLens).ToArray();
        Check(lensHits == 1 && hits.Length == 1 && hits[0].Source == key && (hits[0].Param > .5f) == (id == "bonus"),
            $"replay lens {id}: real forced collision records civilian/trooper overlay colour, seed and lifetime");
        if (hits.Length != 1) { walkers.Free(); native.Free(); camera.Free(); return; }
        var hit = hits[0];
        var hud = new Hud { Mode = HudMode.Watch, Clean = true }; AddChild(hud);
        var particles = new TyreEffects(); particles.Init(track); AddChild(particles);
        var root = new Node3D(); AddChild(root);
        int attacks = 0;
        var fx = new TapeFxPlayer(particles, track, null, root) { Lens = hud.RestoreTapeLensEvent, Feel = (e, age) => { if (e.Kind == TapeFxKind.BabushkaLens) attacks++; } };
        var player = new WorldTapePlayer(loaded, idx, new WoodTapeSink(fx));
        player.Seek(hit.Time + .37f); fx.Update(hit.Time + .37f);
        var restored = hud.CurrentLensSplat;
        native.LensSplat(walkers.LensScarf, Babushkas.LensHold, id == "bonus", walkers.LensSeedThisTick); native._Process(.37);
        var reference = native.CurrentLensSplat;
        Check(hud.TapeLensActive && Math.Abs(restored.Age - reference.Age) < 1e-4f && restored.Scarf == reference.Scarf && restored.Seed == reference.Seed && restored.Military == reference.Military && attacks == 0,
            $"replay lens {id}: aged seek restores the real HUD face/cracks in clean watch without retriggering attack");
        hud._Process(.5); Check(Math.Abs(hud.CurrentLensSplat.Age - .37f) < 1e-4f, $"replay lens {id}: movie/HUD frame rate cannot advance tape-controlled overlay age");
        player.Seek(0); fx.Update(0); Check(!hud.TapeLensActive, $"replay lens {id}: backward seek clears the overlay");
        float end = hit.Time + hit.Life + .1f;
        for (float t = 0; t <= end; t += dt) { player.Apply(t, dt); fx.Update(t); }
        Check(attacks == 1 && !hud.TapeLensActive, $"replay lens {id}: forward playback makes one impact and expires it on time");
        player.Seek(hit.Time + .8f); fx.Update(hit.Time + .8f);
        Check(hud.TapeLensActive && Math.Abs(hud.CurrentLensSplat.Age - .8f) < 1e-4f && attacks == 1,
            $"replay lens {id}: loop/aged seek restores the current phase without another attack");
        player.Release(); Check(!hud.TapeLensActive && root.GetChildCount() == 0, $"replay lens {id}: release clears overlay and replay effects");
        attacks = 0;
        fx.Effect(hit, 0); fx.Effect(hit with { Instance = hit.Instance + 1 }, 0);
        Check(attacks == 1 && hud.CurrentLensSplat.Seed == hit.Instance + 1,
            $"replay lens {id}: simultaneous faces retain the latest appearance and one impact, matching live feedback");
        fx.Clear();
        Tape.Version(walkers, 2); var incompatible = new WorldTapeIndex(new[] { (key, (Node3D)walkers) });
        var rejected = new WorldTapePlayer(loaded, incompatible, new WoodTapeSink(fx)); rejected.Seek(hit.Time + .2f); fx.Update(hit.Time + .2f);
        Check(!rejected.Covers(key) && !hud.TapeLensActive, $"replay lens {id}: incompatible actor cannot create a stray screen overlay");
        rejected.Release(); fx.Free(); root.Free(); particles.Free(); hud.Free(); native.Free(); camera.Free(); walkers.Free();
    }
}
