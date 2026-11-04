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
    void GaiGazeTests()
    {
        string output = ProjectSettings.GlobalizePath("res://media/gai-gaze-v1/tests-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff"));
        Directory.CreateDirectory(output);
        foreach (string id in new[] { "ilyich", "siberia", "atomgrad", "bonus" }) GaiTrackTest(id, output);
        GD.Print("GAI GAZE TEST OUTPUT " + output);
    }

    void GaiTrackTest(string id, string output)
    {
        const string key = "None:SpeedTrap";
        const float dt = 1f / 120;
        var def = TrackDef.Load($"res://tracks/{id}.json"); var track = TrackBuilder.Build(def);
        SpeedTrap Make() { var a = new SpeedTrap(); a.Init(track, def.SpeedTrap); AddChild(a); return a; }
        Vector3 At(Vector2 p) => new(p.X, track.MeshHeight(p.X, p.Y) + .7f, p.Y);
        var source = Make();
        Vector3 road = new(-source.Side.X, 0, -source.Side.Y);
        Check((-source.InspectorNode.GlobalBasis.Z.Normalized()).Dot(road) > .999f, $"GAI {id}: inspector initially faces from verge toward road");
        Vector3 left = At(source.Line - source.Dir * 15), right = At(source.Line + source.Dir * 15);
        for (int n = 0; n < 360; n++) source.Tick(dt, left);
        Vector3 aim = (left + Vector3.Up * .65f - source.HeadNode.GlobalPosition).Normalized();
        Check(source.SeesCar && (-source.HeadNode.GlobalBasis.Z.Normalized()).Dot(aim) > .985f,
            $"GAI {id}: real head/body follow approaching visible player");
        float prior = source.HeadNode.Rotation.Y;
        source.Tick(dt, right);
        Check(Math.Abs(Mathf.AngleDifference(prior, source.HeadNode.Rotation.Y)) <= 3.5f * dt + .0001f,
            $"GAI {id}: target reversal turns smoothly instead of snapping");
        for (int n = 0; n < 360; n++) source.Tick(dt, right);
        aim = (right + Vector3.Up * .65f - source.HeadNode.GlobalPosition).Normalized();
        Check(source.SeesCar && (-source.HeadNode.GlobalBasis.Z.Normalized()).Dot(aim) > .985f,
            $"GAI {id}: gaze follows player after passing radar");
        for (int n = 0; n < 480; n++) source.Tick(dt, At(source.Line + source.Dir * 150));
        Check(!source.SeesCar && Math.Abs(source.HeadNode.Rotation.Y) < .001f && (-source.InspectorNode.GlobalBasis.Z.Normalized()).Dot(road) > .999f,
            $"GAI {id}: outside sight distance returns to road-facing idle");
        source.Reset();
        // Use an actual cached building mesh as the vision blocker, then remove it.
        Vector3 eye = source.HeadNode.GlobalPosition;
        var wall = new Node3D { Position = eye.Lerp(left, .5f) };
        wall.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(7, 12, 7) } });
        wall.AddChild(new SeeThrough()); AddChild(wall);
        source.Tick(.11f, left);
        Check(!source.SeesCar, $"GAI {id}: building wall blocks visual acquisition");
        wall.Free(); source.Reset();
        var heights = (float[])track.Heights.Clone();
        Array.Fill(track.Heights, 100f); source.Tick(.11f, left);
        Check(!source.SeesCar, $"GAI {id}: terrain ridge blocks visual acquisition");
        Array.Copy(heights, track.Heights, heights.Length); source.Reset();
        var index = new WorldTapeIndex(new[] { (key, (Node3D)source) });
        var rec = new WorldTapeRecorder(); rec.Begin(index); rec.Record(0);
        var host = new DetailRecordHost { Recorder = rec, Source = key }; source.TapeFx = host;
        var ghost = new Ghost(); var car = new CarSim(new CarParams(), track); car.Reset(left, 0);
        ghost.Record(0, car, 0, 0, 0);
        var expected = new Dictionary<int, (Transform3D Pose, bool Visible)[]>();
        (Transform3D, bool)[] Snapshot(IWorldTapeIndex ix) => Enumerable.Range(0, ix.Count).Select(i => (ix.NodeOf(i).Transform, ix.NodeOf(i).Visible)).ToArray();
        expected[0] = Snapshot(index);
        for (int n = 1; n <= 960; n++)
        {
            float t = n * dt; host.Clock = t;
            car.Pos = t < 2 ? left : right;
            if (n == 120 || n == 180)
            {
                source.Check(At(source.Line - source.Dir * 2), new Vector3(source.Dir.X, 0, source.Dir.Y) * 30, out _);
                source.Check(At(source.Line + source.Dir), new Vector3(source.Dir.X, 0, source.Dir.Y) * (n == 120 ? 30 : 100), out _);
            }
            if (n == 480) source.Knocks().First().hit(new Vector3(20, 0, 0));
            if (n == 540) source.Knocks().Last().hit(new Vector3(0, 0, 20));
            source.Tick(dt, car.Pos);
            int old = rec.Samples; rec.Record(t); ghost.Record(t, car, 0, 0, 0);
            if (rec.Samples > old) expected[rec.Samples - 1] = Snapshot(index);
        }
        var savedGhost = ghost.CopyWithTime(8);
        var tape = rec.Finish(8, WorldTapeFingerprint.Of(savedGhost)); tape.TrackId = id;
        string folder = output + "/" + id; Directory.CreateDirectory(folder);
        var pair = WorldTapeFile.SavePair(folder + $"/ghost_{id}.bin", folder + $"/tape_{id}.bin", savedGhost, tape,
            WorldTapeRealOps.Instance, WorldTapeCodec.Encode, WorldTapeCodec.Decode);
        File.WriteAllText(folder + $"/track_{id}.sig", TrackSignature.Compute(track) + "\n");
        File.WriteAllText(folder + $"/records_{id}.ver", RecordStore.Layout + "\n");
        Vector3 officer = source.InspectorNode.GlobalPosition;
        Vector3 lens = officer + road * 10 - new Vector3(source.Dir.X, 0, source.Dir.Y) * 6 + Vector3.Up * 6;
        Vector3 look = officer + Vector3.Up * 2.3f;
        File.WriteAllText(folder + "/camera-sequence.json", System.Text.Json.JsonSerializer.Serialize(new { shots = new[] {
            new { id = "gai-gaze", start = .6f, end = 3.9f, camera = "trailer-fixed", eye = new[] {lens.X,lens.Y,lens.Z}, look = new[] {look.X,look.Y,look.Z}, fov = 42 }
        }}));
        var loaded = WorldTapeFile.Load(folder + $"/tape_{id}.bin", savedGhost, WorldTapeCodec.Decode);
        Check(pair.Whole && loaded != null && host.Events.Count == 2 && host.Events.Last().Param < 0,
            $"GAI {id}: production save/load preserves gaze, knockdowns and exact radar readings");
        var replay = Make(); var replayIndex = new WorldTapeIndex(new[] { (key, (Node3D)replay) });
        var particles = new TyreEffects(); particles.Init(track); AddChild(particles);
        var root = new Node3D(); AddChild(root);
        var fx = new TapeFxPlayer(particles, track, null, root) { Radar = replay.RestoreTapeReading };
        var player = new WorldTapePlayer(loaded, replayIndex, new WoodTapeSink(fx));
        Check(player.Covers(key), $"GAI {id}: fresh production inspector structure matches recorded actor");
        replay.SetTapeDriven(true);
        float error = 0; bool visible = true;
        foreach (var (sample, values) in expected)
        {
            player.Apply(sample / WorldTapeConst.TapeRate, 1 / WorldTapeConst.TapeRate); fx.Update(sample / WorldTapeConst.TapeRate);
            for (int i = 0; i < replayIndex.Count; i++)
            {
                error = Math.Max(error, TapeWoodPoseDiff(new[] { values[i].Pose }, new[] { replayIndex.NodeOf(i).Transform }));
                visible &= values[i].Visible == replayIndex.NodeOf(i).Visible;
            }
        }
        Check(error < .012f && visible, $"GAI {id}: all recorded moving parts/flash reproduce ({error:F5} error)");
        player.Seek(2); fx.Update(2);
        Check(replay.ActiveShouts().Single().text == "???", $"GAI {id}: aged seek restores latest overlapping reading");
        var frozen = replay.HeadNode.Transform; replay.Tick(5, left);
        Check(replay.HeadNode.Transform == frozen && !replay.Check(left, Vector3.One * 100, out _),
            $"GAI {id}: replay suppresses live gaze and radar simulation");
        player.Seek(0); fx.Update(0);
        Check(!replay.ActiveShouts().Any(), $"GAI {id}: backward seek/loop clears old reading");
        player.Seek(1.7f); fx.Update(1.7f); player.Apply(5, dt); fx.Update(5);
        Check(!replay.ActiveShouts().Any(), $"GAI {id}: reading expires against tape clock");
        player.Release(); replay.SetTapeDriven(false);
        Check(!replay.ActiveShouts().Any() && !replay.TapeDriven && replay.Knocks().All(k => track.Obstacles[k.obstacle].Center.X > -1e3),
            $"GAI {id}: replay exit clears reading and restores knockable obstacles");
        Tape.Version(replay, 2);
        var bad = new WorldTapePlayer(loaded, new WorldTapeIndex(new[] { (key, (Node3D)replay) }), new WoodTapeSink(fx));
        bad.Seek(2); fx.Update(2);
        Check(!bad.Covers(key) && !replay.ActiveShouts().Any(), $"GAI {id}: incompatible actor cannot replay stray radar effects");
        bad.Release();
        var legacy = WorldTapeCodec.Decode(WorldTapeCodec.Encode(loaded)); legacy.ActorStructure.Remove(key);
        var oldPlayer = new WorldTapePlayer(legacy, replayIndex, new WoodTapeSink(fx)); oldPlayer.Seek(2); fx.Update(2);
        replay.Tick(3, left);
        Check(!oldPlayer.Covers(key) && replay.SeesCar && !replay.ActiveShouts().Any(),
            $"GAI {id}: legacy tape without inspector safely leaves gaze live and invents no readings");
        oldPlayer.Release(); fx.Free(); root.Free(); particles.Free(); replay.Free(); source.Free();
    }

    void GaiGazeSceneTests()
    {
        string output = ProjectSettings.GlobalizePath("res://media/gai-gaze-v1/production-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff"));
        Directory.CreateDirectory(output);
        string laps = output + "/laps"; Directory.CreateDirectory(laps);
        var record = TapeGame(output + "/record-data", new[] { "--track=ilyich", "--start", "--autodrive", "--no-record", "--tape-debug",
            "--tape-session-laps=" + laps, "--tape-quit-laps=1" }, 150);
        Check(record.Exit == 0 && !record.TimedOut && !record.Crashed, "GAI scene: production game records one complete isolated lap");
        var lap = Directory.GetDirectories(laps, "lap-*").OrderBy(x => x).FirstOrDefault();
        if (lap == null) { Check(false, "GAI scene: completed paired lap exists"); return; }
        var track = TrackBuilder.Build(TrackDef.Load("res://tracks/ilyich.json"));
        var ghost = Ghost.Load(lap + "/ghost_ilyich.bin", new CarParams(), track);
        var tape = WorldTapeFile.Load(lap + "/tape_ilyich.bin", ghost, WorldTapeCodec.Decode);
        var body = tape == null ? null : WorldTapeCodec.ReadBody(tape.Payload);
        var readings = body?.Events.Where(e => e.Kind == TapeFxKind.RadarReading).ToArray();
        Check(tape != null && tape.ActorStructure.ContainsKey("None:SpeedTrap") && readings.Length > 0,
            "GAI scene: production actor roster saves inspector gaze and actual radar reading");
        var replay = TapeGame(lap, new[] { "--track=ilyich", "--watch", "--tape-debug", "--tape-title-nopoll", "--tape-wrap-every=2", "--tape-replay-quit=6" }, 90);
        var covered = replay.All("from tape: covers").Any(x => x.Split("], live")[0].Contains("None:SpeedTrap"));
        Check(replay.Exit == 0 && !replay.Crashed && covered,
            "GAI scene: real watch playback drives inspector from tape across loops and cleanup");
        File.WriteAllText(output + "/verification.json", System.Text.Json.JsonSerializer.Serialize(new {
            record_exit = record.Exit, replay_exit = replay.Exit, inspector_covered = covered,
            first_radar_time = readings?.FirstOrDefault().Time, lap = lap,
            officer = new[] { track.Center[(int)TrackDef.Load("res://tracks/ilyich.json").SpeedTrap.At].X, track.Center[(int)TrackDef.Load("res://tracks/ilyich.json").SpeedTrap.At].Y }
        }));
        GD.Print("GAI PRODUCTION TEST OUTPUT " + output);
    }
}
