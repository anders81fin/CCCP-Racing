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
    void RescueRopeTapeTests()
    {
        string output = ProjectSettings.GlobalizePath("res://media/rescue-rope-tape-v1/tests-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff"));
        Directory.CreateDirectory(output);
        foreach (string id in new[] { "ilyich", "siberia", "atomgrad", "bonus" })
            RescueRopeTapeTest(id, id is "siberia" or "bonus", output);
        GD.Print("RESCUE ROPE TAPE TEST OUTPUT " + output);
    }

    void RescueRopeTapeTest(string id, bool tow, string output)
    {
        const string tractorKey = "None:RescueTractor", ropeKey = "None:RescueTowRope";
        const float dt = 1f / 120;
        var track = TrackBuilder.Build(TrackDef.Load($"res://tracks/{id}.json"));
        var fx = new TyreEffects(); fx.Init(track); AddChild(fx);
        RescueTractor Make()
        {
            var a = new RescueTractor { Name = "RescueTractor" }; AddChild(a); a.Init(track, fx); return a;
        }
        WorldTapeIndex Index(RescueTractor a) => new(new[] { (tractorKey, (Node3D)a), (ropeKey, a.TapeRopeRoot) });
        var source = Make(); var index = Index(source);
        var legacyIndex = new WorldTapeIndex(new[] { (tractorKey, (Node3D)source) });
        var legacyRecorder = new WorldTapeRecorder(); legacyRecorder.Begin(legacyIndex);
        Check(legacyIndex.Count == 4 && legacyIndex.StructureOf(tractorKey).Version == 1,
            $"rope tape {id}: existing version-1 tractor keeps its four original parts and hierarchy");
        var car = new CarSim(new CarParams(), track); car.Reset(track.StartPos, track.StartYaw);
        if (!tow) car.Rot *= new Basis(Vector3.Forward, MathF.PI);
        var ghost = new Ghost(); var rec = new WorldTapeRecorder(); rec.Begin(index);
        var expected = new Dictionary<int, (bool Visible, Transform3D Pose)>();
        int active = 0; bool completed = false;
        for (int n = 0; n <= 22 * 120; n++)
        {
            float clock = n * dt;
            if (n == 60) { if (tow) source.CallTow(car); else source.Call(car); }
            source.Tick(dt, car); completed |= tow ? source.JustTowed : source.JustRighted;
            int before = rec.Samples; rec.Record(clock); legacyRecorder.Record(clock); ghost.Record(clock, car, 0, 0, 0);
            if (rec.Samples > before)
            {
                var rope = source.RopeMesh; expected[rec.Samples - 1] = (rope.Visible, rope.GlobalTransform);
                if (rope.Visible) active++;
            }
        }
        var savedGhost = ghost.CopyWithTime(22); var tape = rec.Finish(22, WorldTapeFingerprint.Of(savedGhost)); tape.TrackId = id;
        string folder = output + "/" + id; Directory.CreateDirectory(folder);
        var pair = WorldTapeFile.SavePair(folder + $"/ghost_{id}.bin", folder + $"/tape_{id}.bin", savedGhost, tape,
            WorldTapeRealOps.Instance, WorldTapeCodec.Encode, WorldTapeCodec.Decode);
        File.WriteAllText(folder + $"/track_{id}.sig", TrackSignature.Compute(track) + "\n");
        File.WriteAllText(folder + $"/records_{id}.ver", RecordStore.Layout + "\n");
        var loaded = WorldTapeFile.Load(folder + $"/tape_{id}.bin", savedGhost, WorldTapeCodec.Decode);
        Check(pair.Whole && loaded != null && completed && active > 10 && rec.UnscaledChanges == 0,
            $"rope tape {id}: real {(tow ? "fuel tow" : "rollover rescue")} completes and saves moving endpoints without scaled tape parts");
        var target = Make(); var targetIndex = Index(target); var player = new WorldTapePlayer(loaded, targetIndex, new TapeSink());
        var rest = Enumerable.Range(0, targetIndex.Count).Select(i => (Node: targetIndex.NodeOf(i), Pose: targetIndex.NodeOf(i).Transform, Visible: targetIndex.NodeOf(i).Visible)).ToArray();
        target.SetTapeDriven(true); target.SetTapeRopeDriven(true);
        Check(player.Covers(tractorKey) && player.Covers(ropeKey), $"rope tape {id}: fresh production tractor and rope structures match");
        float worst = 0; bool visible = true;
        foreach (var (sample, want) in expected)
        {
            player.Apply(sample / WorldTapeConst.TapeRate, 1 / WorldTapeConst.TapeRate); target.RestoreTapeRope();
            visible &= target.RopeMesh.Visible == want.Visible;
            if (want.Visible) worst = Math.Max(worst, TapeWoodPoseDiff(new[] { want.Pose }, new[] { target.RopeMesh.GlobalTransform }));
        }
        Check(visible && worst < .015f, $"rope tape {id}: saved endpoints restore full rope position, direction and changing length ({worst:F5} error)");
        var on = expected.Where(kv => kv.Value.Visible).Select(kv => kv.Key).ToArray();
        foreach (int sample in new[] { on.First(), on[on.Length / 2], 0, on.Last(), on.First(), expected.Keys.Last() })
        {
            player.Seek(sample / WorldTapeConst.TapeRate); target.RestoreTapeRope(); var want = expected[sample];
            Check(target.RopeMesh.Visible == want.Visible && (!want.Visible || TapeWoodPoseDiff(new[] { want.Pose }, new[] { target.RopeMesh.GlobalTransform }) < .015f),
                $"rope tape {id}: seek/loop sample {sample} restores rope geometry and attachment lifetime");
        }
        int middle = on[on.Length / 2]; player.Seek(middle / WorldTapeConst.TapeRate); target.RestoreTapeRope();
        var fixedPose = target.RopeMesh.GlobalTransform; var carPose = car.Pos;
        target.Tick(20, car); target.RestoreTapeRope();
        Check(target.RopeMesh.GlobalTransform == fixedPose && car.Pos == carPose,
            $"rope tape {id}: live rescue cannot move rope or player during tape playback");
        player.Release(); target.SetTapeDriven(false); target.SetTapeRopeDriven(false);
        Check(!target.RopeMesh.Visible && !target.TapeRopeDriven && !target.Model.Visible,
            $"rope tape {id}: exit clears rope and restores stopped live tractor");
        var oldTape = WorldTapeCodec.Decode(WorldTapeCodec.Encode(legacyRecorder.Finish(22, 1)));
        var oldPlayer = new WorldTapePlayer(oldTape, targetIndex, new TapeSink());
        oldPlayer.Seek(middle / WorldTapeConst.TapeRate);
        Check(oldPlayer.Covers(tractorKey) && !oldPlayer.Covers(ropeKey) && !target.RopeMesh.Visible,
            $"rope tape {id}: legacy tractor still replays while missing rope is not invented");
        oldPlayer.Release();
        // A production index is built before animation, never from posed replay parts.
        foreach (var state in rest) { state.Node.Transform = state.Pose; state.Node.Visible = state.Visible; }
        Tape.Version(target.TapeRopeRoot, 2);
        var incompatible = new WorldTapePlayer(loaded, Index(target), new TapeSink()); incompatible.Seek(middle / WorldTapeConst.TapeRate);
        Check(incompatible.Covers(tractorKey) && !incompatible.Covers(ropeKey) && !target.RopeMesh.Visible,
            $"rope tape {id}: incompatible rope falls back independently of compatible tractor");
        incompatible.Release();
        Vector3 look = expected[middle].Pose.Origin;
        File.WriteAllText(folder + "/camera-sequence.json", System.Text.Json.JsonSerializer.Serialize(new { shots = new[] {
            new { id="rescue-rope", start=0f, end=22f, camera="trailer-fixed", eye=new[]{look.X+9,look.Y+7,look.Z+12}, look=new[]{look.X,look.Y,look.Z}, fov=50 }
        }}));
        File.WriteAllText(folder + "/active-range.json", System.Text.Json.JsonSerializer.Serialize(new {
            start=on.First()/WorldTapeConst.TapeRate, end=on.Last()/WorldTapeConst.TapeRate, tow, active_samples=active, max_pose_error=worst
        }));
        target.TapeRopeRoot.Free(); target.Free(); source.TapeRopeRoot.Free(); source.Free(); fx.Free();
    }
}
