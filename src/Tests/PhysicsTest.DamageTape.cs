using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CCCPRacing.Car;
using CCCPRacing.Race;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    void DamageTapeTest()
    {
        string output = Path.Combine("media", "damage-tape-tests-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(output);
        var ground = new FlatGround(); var parameters = new CarParams();
        var car = new CarSim(parameters, ground); car.Reset(new(0, 2, 0), 0);
        var live = CarVisual.Create(parameters, Colors.Red); AddChild(live);
        var replay = CarVisual.Create(parameters, Colors.Red); AddChild(replay);
        var track = TrackBuilder.Build(TrackDef.Load("res://tracks/ilyich.json"));
        var fx = new TyreEffects(); fx.Init(track); AddChild(fx);
        var replayFx = new TyreEffects(); replayFx.Init(track); AddChild(replayFx);
        var damage = live.Damage;
        Check(damage != null && replay.Damage != null, "damage tape: real production car model loaded");
        if (damage == null) return;

        // Hash rendered vertices, normals, discrete part state and poses, not the capture cache.
        ulong MeshHash(CarVisual visual)
        {
            var h = TapeHash.Start();
            void Visit(Node node)
            {
                if (node is MeshInstance3D m && m.Mesh is ArrayMesh mesh)
                {
                    h = h.Add(m.Visible ? 1 : 0);
                    for (int s = 0; s < mesh.GetSurfaceCount(); s++)
                    {
                        var arrays = mesh.SurfaceGetArrays(s);
                        foreach (var v in arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array()) h = h.Add(v.X).Add(v.Y).Add(v.Z);
                        foreach (var v in arrays[(int)Mesh.ArrayType.Normal].AsVector3Array()) h = h.Add(v.X).Add(v.Y).Add(v.Z);
                    }
                }
                foreach (var child in node.GetChildren()) Visit(child);
            }
            Visit(visual);
            return h.Add(visual.Damage.Dents).Add(visual.Damage.HeadlightWorks(true) ? 1 : 0)
                .Add(visual.Damage.HeadlightWorks(false) ? 1 : 0).Value;
        }
        // Begin with damage carried from a previous lap, including a loose front bumper.
        live.Transform = car.Transform;
        damage.Hit(10, new(.7f, -.2f, -1.95f), Vector3.Back, car, fx);
        Check(damage.IsDamaged, "damage tape: initial lap already carries real dents");
        var ghost = new Ghost(); var expected = new List<ulong>();
        var states = new List<CarDamage.State>();
        for (int frame = 0; frame < 210; frame++)
        {
            car.Pos = new(frame * .1f, 2, -frame * .15f); live.Transform = car.Transform;
            if (frame == 10) damage.ShootHeadlamp(car, fx);
            if (frame == 25) damage.Hit(35, new(.7f, .8f, -1.9f), Vector3.Back, car, fx);
            if (frame == 80) damage.Straighten(.4f);
            if (frame == 120) damage.Reset();
            if (frame == 150) damage.Hit(12, new(0, -.2f, 1.95f), Vector3.Forward, car, fx);
            damage.Tick(1f / 30, ground);
            ghost.Record(frame / 30f + .00001f, car, 0, 0, 0, 400, damage: damage);
            expected.Add(MeshHash(live)); states.Add(damage.CaptureTapeState());
        }
        ghost = ghost.CopyWithTime(7);
        var index = new WorldTapeIndex(new List<(string Key, Node3D Root)>());
        var recorder = new WorldTapeRecorder(); recorder.Begin(index); recorder.Record(0); recorder.Record(7);
        var tape = recorder.Finish(7, WorldTapeFingerprint.Of(ghost)); tape.TrackId = "ilyich";
        string gp = Path.Combine(output, "ghost_ilyich.bin"), tp = Path.Combine(output, "tape_ilyich.bin");
        var saved = WorldTapeFile.SavePair(gp, tp, ghost, tape, WorldTapeRealOps.Instance, WorldTapeCodec.Encode, WorldTapeCodec.Decode);
        var loaded = Ghost.Load(gp, parameters, ground);
        Check(saved.Whole && loaded?.HasDamage == true && loaded.Count == ghost.Count,
            "damage tape: production paired save/load retains V4 damage and lap copy");
        if (loaded == null) return;
        Check(WorldTapeFile.Load(tp, loaded, WorldTapeCodec.Decode) != null,
            "damage tape: world tape remains paired to the saved car recording");
        Check(loaded.ToBytes().SequenceEqual(ghost.ToBytes()), "damage tape: lossless file roundtrip");
        var puppet = new CarSim(parameters, ground);
        bool meshes = true, parts = true, suppressed = true; float poseError = 0;
        foreach (int frame in Enumerable.Range(0, 210).Concat(new[] { 87, 13, 0, 155, 120, 40, 209, 0, 40 }))
        {
            float clock = frame / 30f + .00001f;
            loaded.Apply(clock, puppet, 0); replay.Transform = puppet.Transform;
            bool applied = loaded.ApplyDamage(clock, replay.Damage, replayFx);
            meshes &= applied && MeshHash(replay) == expected[frame];
            var actual = replay.Damage.CaptureTapeState();
            for (int p = 0; p < actual.Parts.Length; p++)
            {
                var a = actual.Parts[p]; var b = states[frame].Parts[p];
                float error = Math.Max(a.Pose.Origin.DistanceTo(b.Pose.Origin),
                    Math.Max((a.Pose.Basis.X - b.Pose.Basis.X).Length(),
                        Math.Max((a.Pose.Basis.Y - b.Pose.Basis.Y).Length(), (a.Pose.Basis.Z - b.Pose.Basis.Z).Length())));
                poseError = Math.Max(poseError, error);
                parts &= a.Detached == b.Detached && error < .001f;
            }
            ulong before = MeshHash(replay);
            replay.Damage.Tick(.5f, ground); replay.Damage.Hit(60, Vector3.Zero, Vector3.Back, puppet, replayFx);
            suppressed &= MeshHash(replay) == before;
        }
        Check(meshes, "damage tape: actual rendered dents/glass/lamp states match live on playback, seek and loop");
        Check(parts, $"damage tape: detached flight/rest and attached poses survive save/load and backward seek (max {poseError:F6} m)");
        Check(suppressed, "damage tape: playback suppresses live damage and debris physics");
        loaded.ApplyDamage(1, replay.Damage, replayFx);
        int particles = replayFx.ReplayParticles;
        loaded.ApplyDamage(1, replay.Damage, replayFx);
        Check(particles > 0 && particles == replayFx.ReplayParticles, "damage tape: particle age restored without duplication on repeated seek");
        replay.Damage.ReleaseTape(replayFx);
        Check(!replay.Damage.TapeDriven && !replay.Damage.IsDamaged && replayFx.ReplayParticles == 0
            && replay.Damage.HeadlightWorks(true), "damage tape: replay exit cleans geometry, lamps, debris and chips");
        var state = states[40]; var bad = state with { Shape = state.Shape with { Signature = 123 } };
        Check(!replay.Damage.ApplyTapeState(bad, bad, 0) && !replay.Damage.IsDamaged,
            "damage tape: incompatible model rejected without modifying geometry");
        // An actual old recording, copied only into this fresh test directory.
        string oldPath = "media/tape-takes-v1/takes/atomgrad-session01-lap01/data/ghost_atomgrad.bin";
        var old = Ghost.Load(oldPath, parameters, ground);
        Check(old != null && !old.HasDamage && old.ToBytes().SequenceEqual(File.ReadAllBytes(oldPath)),
            "damage tape: existing V3 original stays byte-identical and readable");
        var corrupt = ghost.ToBytes(); Array.Resize(ref corrupt, corrupt.Length - 5);
        string badPath = Path.Combine(output, "truncated.bin"); File.WriteAllBytes(badPath, corrupt);
        Check(Ghost.Load(badPath, parameters, ground) == null, "damage tape: truncated damage extension fails safely");
        GD.Print($"DAMAGE TAPE TEST OUTPUT {Path.GetFullPath(output)}; ghost {new FileInfo(gp).Length} bytes");
        live.Free(); replay.Free(); fx.Free(); replayFx.Free();
    }
}
