using System;
using System.Linq;
using CCCPRacing.Car;
using CCCPRacing.Race;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    /// <summary>The water mill's wheel turns slowly (one merged mesh on its own pivot, not batched into the mill) and a few pale streaks fall at its discharge.</summary>
    void WaterMillTest()
    {
        var mill = WaterMill.Build();
        AddChild(mill);
        var wheel = mill.GetChildren().OfType<MillWheel>().SingleOrDefault();
        var spray = mill.GetChildren().OfType<MillSpray>().SingleOrDefault();
        bool parts = wheel != null && spray != null && wheel.GetChildren().OfType<MeshInstance3D>().Count() == 1;
        float before = wheel?.Rotation.Z ?? 0f;
        wheel?._Process(2.0);
        float turned = Mathf.AngleDifference(before, wheel?.Rotation.Z ?? 0f);
        var kids = spray?.GetChildren().OfType<MeshInstance3D>().ToArray() ?? Array.Empty<MeshInstance3D>();
        float y0 = kids.Length > 0 ? kids[0].Position.Y : 0f;
        spray?._Process(0.25);
        bool falls = kids.Length >= 6 && kids.Any(k => MathF.Abs(k.Position.Y - y0) > 0.02f) && kids.All(k => k.Position.Y < 0f && k.Position.Y > -2.2f);
        Check(parts && MathF.Abs(turned - MillWheel.Rate * 2f) < 1e-3f && falls,
            $"water mill: the wheel is one merged turning part ({parts}), turns {turned:F2} rad in 2 s ({MillWheel.Rate:F2} rad/s: about {Mathf.Tau / MillWheel.Rate:F0} s a revolution) and {kids.Length} spray streaks fall below its centre ({falls})");
        mill.QueueFree();
    }
}

public partial class PhysicsTest
{
    /// <summary>The catfish jump: the tail beats hard in the launch, the body bends into a C at the top of the arc and is straight again in the water; the launch throws a heavy splash (many more drops than a plain one).</summary>
    void CatfishTest()
    {
        var def = TrackDef.Load("res://tracks/atomgrad.json");
        var t = TrackBuilder.Build(def);
        var fx = new TyreEffects(); AddChild(fx); fx.Init(t);
        var zone = new ZoneLife { Name = "ZoneLife" }; AddChild(zone);
        zone.Init(t, fx, def.Zone);
        zone.CueSchoolFish();
        var rear = zone.GetNode<Node3D>("Catfish/CatfishRear"); var tail = rear.GetNode<Node3D>("CatfishTail");
        float tailEarly = 0, rearApex = 0, tailApex = 0, endBend = 0; int dropsAtLaunch = 0; bool started = false;
        for (int tick = 0; tick < 60 * 4; tick++)
        {
            zone.Tick(1f / 60f, null);
            var fish = zone.GetNode<Node3D>("Catfish");
            if (!fish.Visible) { if (started) { endBend = MathF.Max(MathF.Abs(rear.Rotation.Y), MathF.Abs(tail.Rotation.Y)); break; } continue; }
            if (!started) { started = true; dropsAtLaunch = fx.ParticleCount; }
        }
        // Sample the bend over the jump directly (the same function the tick uses).
        for (float u = 0; u <= 1f; u += 0.005f)
        {
            var (r, tl) = ZoneLife.FishBend(u);
            if (u < 0.22f) tailEarly = MathF.Max(tailEarly, MathF.Abs(tl));
            if (MathF.Abs(u - 0.5f) < 0.02f) { rearApex = MathF.Max(rearApex, MathF.Abs(r)); tailApex = MathF.Max(tailApex, MathF.Abs(tl)); }
        }
        var (r1, t1) = ZoneLife.FishBend(1f);
        Check(started && tailEarly > 0.8f && rearApex > 0.45f && tailApex > 0.3f && MathF.Abs(r1) < 1e-3f && MathF.Abs(t1) < 1e-3f && endBend < 1e-3f && dropsAtLaunch > 80,
            $"catfish: the tail beats {tailEarly:F2} rad in the launch, the rear body bends {rearApex:F2} and the tail {tailApex:F2} rad at the apex, straight at the end ({endBend:F3}); {dropsAtLaunch} drops at the launch");
        fx.QueueFree(); zone.QueueFree();
        CatfishSplashTapeTest(t, def);
        CatfishTapeTest(t, def);
    }

    /// <summary>The fish's splashes are tape events: a jump with a recording host reports the launch and the dive; the replay's player makes the spray from each with the live functions, restored by age (a seek into the middle shows it already flying, a seek past it nothing).</summary>
    void CatfishSplashTapeTest(TrackData t, TrackDef def)
    {
        var fx = new TyreEffects(); AddChild(fx); fx.Init(t);
        var host = new FxCollector();
        var zone = new ZoneLife { Name = "ZoneLife", TapeFx = host }; AddChild(zone);
        zone.Init(t, fx, def.Zone);
        zone.CueSchoolFish();
        for (int tick = 0; tick < 60 * 4 && host.Events.Count < 2; tick++) zone.Tick(1f / 60f, null);
        var launch = host.Events.FirstOrDefault(e => e.Kind == TapeFxKind.CatfishLaunch); var dive = host.Events.FirstOrDefault(e => e.Kind == TapeFxKind.CatfishSplash);
        var root = new Node3D(); AddChild(root);
        var replayFx = new TyreEffects(); AddChild(replayFx); replayFx.Init(t);
        var player = new TapeFxPlayer(replayFx, t, null, root);
        player.Effect(launch, 0.2f); int fresh = replayFx.ReplayParticles;
        replayFx.Clear(); player.Effect(launch, 2.0f); int stale = replayFx.ReplayParticles;      // (older than its life: nothing)
        player.Effect(dive, 0.1f); int dived = replayFx.ReplayParticles;
        Check(host.Events.Count == 2 && launch.Life > 1f && fresh > 60 && stale == 0 && dived > 10 && player.Made.TryGetValue(TapeFxKind.CatfishLaunch, out int madeLaunch) && madeLaunch >= 1,
            $"catfish splash tape: the jump reports its launch and dive ({host.Events.Count} events), the replay makes {fresh} tagged drops 0.2 s into the launch, none once it is over, {dived} for the dive");
        player.Free(); root.QueueFree(); replayFx.QueueFree(); fx.QueueFree(); zone.QueueFree();
    }

    /// <summary>The fish's rear body and tail are tape parts: a jump recorded and replayed (after a file-format round trip, seeking forward and back) bends the replayed fish the same way.</summary>
    void CatfishTapeTest(TrackData t, TrackDef def)
    {
        const string key = "Zone:ZoneLife";
        var source = new ZoneLife { Name = "ZoneLife" }; AddChild(source); source.Init(t, null, def.Zone);
        var rec = new WorldTapeRecorder(); rec.Begin(new WorldTapeIndex(new[] { (key, (Node3D)source) })); rec.Record(0);
        source.CueSchoolFish();
        var bend = new System.Collections.Generic.Dictionary<int, (float rear, float tail, bool shown)>();
        const float dt = 1f / 60f;
        for (int frame = 1; frame <= 60 * 3; frame++)
        {
            source.Tick(dt, null); rec.Record(frame * dt);
            if (frame % 4 == 0) bend[frame] = (source.GetNode<Node3D>("Catfish/CatfishRear").Rotation.Y, source.GetNode<Node3D>("Catfish/CatfishRear/CatfishTail").Rotation.Y, source.GetNode<Node3D>("Catfish").Visible);
        }
        var loaded = WorldTapeCodec.Decode(WorldTapeCodec.Encode(rec.Finish(3, 1UL)));
        var replay = new ZoneLife { Name = "ZoneLife" }; AddChild(replay); replay.Init(t, null, def.Zone);
        var player = new WorldTapePlayer(loaded, new WorldTapeIndex(new[] { (key, (Node3D)replay) }), new TapeSink());
        bool covered = player.Covers(key);
        float worst = 0; int jumping = 0;
        foreach (int frame in new[] { 20, 40, 60, 80, 100, 60, 20, 120 })      // seeks forward, back and on
        {
            player.Seek(frame * dt);
            var want = bend[frame - frame % 4 == frame ? frame : frame - frame % 4];
            var got = (replay.GetNode<Node3D>("Catfish/CatfishRear").Rotation.Y, replay.GetNode<Node3D>("Catfish/CatfishRear/CatfishTail").Rotation.Y);
            worst = MathF.Max(worst, MathF.Max(MathF.Abs(want.rear - got.Item1), MathF.Abs(want.tail - got.Item2)));
            if (want.shown && (MathF.Abs(want.rear) > 0.1f || MathF.Abs(want.tail) > 0.1f)) jumping++;
        }
        Check(covered && worst < 0.12f && jumping >= 3, $"catfish tape: record, file round trip and seeks replay the bend of the rear body and tail (worst {worst:F3} rad over {jumping} bent samples, covered {covered})");
        source.QueueFree(); replay.QueueFree();
    }
}
