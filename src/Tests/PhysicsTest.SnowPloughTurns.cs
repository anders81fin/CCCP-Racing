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
    void SnowPloughTurnTest()
    {
        var track = TrackBuilder.Build(TrackDef.Load("res://tracks/siberia.json"));
        foreach (bool against in new[] { false, true })
        foreach (float lane in new[] { .42f, .68f })
        {
            float maxTurn = 0, maxStep = 0;
            // Include the lap seam and the backwards wrap used by the oncoming plough.
            for (int i = 0; i <= track.Center.Length; i++)
            {
                var before = SnowPloughs.RoadPose(track, i - .001f, against, lane);
                var after = SnowPloughs.RoadPose(track, i + .001f, against, lane);
                maxTurn = Math.Max(maxTurn, Math.Abs(before.forward.AngleTo(after.forward)));
                maxStep = Math.Max(maxStep, before.position.DistanceTo(after.position));
            }
            Check(maxTurn < .001f && maxStep < .01f,
                $"plough direction {(against ? "against" : "with")} lane {lane}: continuous turns {maxTurn:F5} rad, position {maxStep:F4} m");
        }
        var ploughs = new SnowPloughs(); AddChild(ploughs);
        int first = track.Obstacles.Count;
        ploughs.Init(track, null, new List<PloughDef> { new() { Start = 315 } });
        var model = ploughs.GetChild<Node3D>(0);
        float maximum = 0;
        bool collision = true;
        Vector2 previous = new(-model.GlobalBasis.Z.X, -model.GlobalBasis.Z.Z);
        for (int i = 0; i < 30 * 120; i++)
        {
            ploughs.Tick(1f / 120, null);
            Vector2 forward = new Vector2(-model.GlobalBasis.Z.X, -model.GlobalBasis.Z.Z).Normalized();
            maximum = Math.Max(maximum, Math.Abs(previous.AngleTo(forward)));
            previous = forward;
            var tip = new Vector2(model.GlobalPosition.X, model.GlobalPosition.Z)
                + forward * (6.4f * model.GlobalBasis.Z.Length());
            collision &= track.Obstacles[first].Center.DistanceTo(tip) < .001f;
        }
        Check(maximum < .04f && collision, $"live plough: smooth bend rotation {maximum:F4} rad/frame and blade collision follows model");
        ploughs.Free();
        SnowPloughTapeLoadTest();
    }

    void SnowPloughTapeLoadTest()
    {
        const string key = "Ploughs:SnowPloughs";
        const float dt = 1f / 60;
        SnowPloughs Make()
        {
            var t = TrackBuilder.Build(TrackDef.Load("res://tracks/siberia.json"));
            var p = new SnowPloughs { Name = "SnowPloughs" }; AddChild(p);
            p.Init(t, null, new List<PloughDef> { new() { Start = 315 } });
            return p;
        }
        Node3D Model(SnowPloughs p) => p.GetChild<Node3D>(0);
        Node3D[] Heaps(SnowPloughs p) => Model(p).GetChildren().OfType<Node3D>()
            .Select(n => n.GetNodeOrNull<Node3D>("SnowHeap")).Where(n => n != null).ToArray();
        float Speed(SnowPloughs p) => Tape.SoundsOf(Model(p)).Single(r => r.Channel == TapeSoundChannel.DieselSpeed).Get();
        CarSim Blocker(SnowPloughs p)
        {
            var xf = Model(p).GlobalTransform;
            return new CarSim(new CarParams(), new FlatGround()) { Pos = xf.Origin - xf.Basis.Z.Normalized() * 10 };
        }
        var source = Make(); var control = Make();
        var index = new WorldTapeIndex(new[] { (key, (Node3D)source) });
        var rec = new WorldTapeRecorder(); rec.Begin(index); rec.Record(0);
        var loads = new Dictionary<int, float> { [0] = Heaps(source)[0].Scale.Y };
        bool unchanged = true; float lowest = 1, highest = 0;
        for (int frame = 1; frame <= 600; frame++)
        {
            bool stop = frame >= 120 && frame < 360;
            source.Tick(dt, stop ? Blocker(source) : null);
            control.Tick(dt, stop ? Blocker(control) : null);
            rec.Record(frame * dt);
            unchanged &= Model(source).Transform.IsEqualApprox(Model(control).Transform)
                && Heaps(source)[0].Scale.IsEqualApprox(Heaps(control)[0].Scale);
            if (frame % 6 == 0)
            {
                float load = Heaps(source)[0].Scale.Y;
                loads[frame] = load; lowest = Math.Min(lowest, load); highest = Math.Max(highest, load);
            }
        }
        Check(unchanged && lowest <= .051f && highest > .6f,
            "plough tape: real driving stops and accelerates; recording leaves live motion and snow unchanged");
        var tape = rec.Finish(10, 1UL);
        string path = Path.Combine(Path.GetTempPath(), "cccp-plough-snow-" + Guid.NewGuid().ToString("N") + ".bin");
        File.WriteAllBytes(path, WorldTapeCodec.Encode(tape));
        var loaded = WorldTapeCodec.Decode(File.ReadAllBytes(path));
        var replay = Make();
        var player = new WorldTapePlayer(loaded, new WorldTapeIndex(new[] { (key, (Node3D)replay) }), new TapeSink());
        Check(player.Covers(key), "plough tape: saved/loaded production actor remains structurally compatible");
        replay.TapeDriven = true;
        float worst = 0;
        foreach (var frame in loads.Keys.OrderBy(f => f))
        {
            player.Apply(frame * dt, .1f);
            foreach (var heap in Heaps(replay)) worst = Math.Max(worst, Math.Abs(heap.Scale.Y - loads[frame]));
        }
        // One sound byte represents 40/255 m/s, or at most .00785 heap scale error.
        Check(worst <= .008f, $"plough tape: both heap scales match recorded motion after file roundtrip (max {worst:F5})");
        bool seeks = true;
        foreach (int frame in new[] { 480, 240, 0, 600, 240, 0 })
        {
            player.Seek(frame * dt);
            foreach (var heap in Heaps(replay)) seeks &= Math.Abs(heap.Scale.Y - loads[frame]) <= .008f;
            var before = Model(replay).Transform; float load = Heaps(replay)[0].Scale.Y;
            replay.Tick(1, Blocker(replay));
            seeks &= before.IsEqualApprox(Model(replay).Transform) && Math.Abs(Heaps(replay)[0].Scale.Y - load) < 1e-6f;
        }
        Check(seeks, "plough tape: forward/backward seeks and repeated loop restore snow; live tick cannot overwrite it");
        player.Release(); replay.TapeDriven = false;
        Check(Heaps(replay).All(h => Math.Abs(h.Scale.Y - 1) < 1e-6f) && Math.Abs(Speed(replay) - 10) < 1e-6f,
            "plough tape: replay exit restores live heap and sound state");
        replay.Reset(); replay.Tick(dt, null);
        Check(Heaps(replay).All(h => Math.Abs(h.Scale.Y - Math.Clamp(Speed(replay) / 10, .05f, 1.2f)) < 1e-6f),
            "plough tape: live snow responds to speed after reset and replay exit");
        source.Free(); control.Free(); replay.Free();
    }
}
