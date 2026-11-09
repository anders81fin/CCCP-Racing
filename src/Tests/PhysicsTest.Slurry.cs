using System;
using System.Linq;
using CCCPRacing.Car;
using CCCPRacing.Race;
using Godot;
using CCCPRacing.Track;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    void SlurryTest()
    {
        GD.Print("Slurry tanker: temporary grip loss and a clear field route");
        var trail = new SlurryTrail();
        trail.Deposit(Vector2.Zero);
        Check(trail.Wetness(Vector2.Zero) == 1 && trail.Wetness(new Vector2(4, 0)) == 0, "slurry affects only its landing area");
        trail.Tick(7.5f);
        Check(Math.Abs(trail.Wetness(Vector2.Zero) - 0.5f) < 0.001f, "slurry fades and regains grip over time");
        trail.Deposit(new Vector2(10, 0));
        trail.Tick(7.5f);
        Check(trail.Wetness(Vector2.Zero) == 0 && trail.Wetness(new Vector2(10, 0)) == 0.5f, "each deposit expires independently at 15 seconds");
        trail.Clear();
        Check(trail.Patches.Count == 0 && trail.Wetness(Vector2.Zero) == 0, "restart removes every wet patch");
        float Stop(float wet)
        {
            var car = NewCar(new FlatGround { SlurryWetness = wet });
            Run(car, 1f);
            car.Vel = car.Forward * 15f;
            var start = car.Pos;
            Run(car, 7f, () => car.SetControls(false, car.ForwardSpeed > 0.3f, 0, false));
            return car.Pos.DistanceTo(start);
        }
        float clean = Stop(0), slippery = Stop(1), faded = Stop(0.5f);
        GD.Print($"  stopping distances: clean {clean:F1}, half dried {faded:F1}, fresh slurry {slippery:F1} m");
        Check(slippery > clean * 1.4f && faded > clean && faded < slippery, "wheel physics loses grip on fresh slurry and recovers as it fades");

        {
            // The film stays on the tread after the car leaves the patch and wipes off over CarSim.SlickClean seconds (faster through water).
            var ground = new FlatGround { SlurryWetness = 1f };
            var car = NewCar(ground);
            Run(car, 1f);
            car.Vel = car.Forward * 15f;
            Run(car, 0.5f);
            bool soaked = car.Wheels.All(w => w.Slick > 0.99f);
            ground.SlurryWetness = 0f;
            Run(car, 0.2f);
            bool stillSlick = car.Wheels.All(w => w.Slick > 0.9f);
            Run(car, CarSim.SlickClean * 0.5f);
            bool half = car.Wheels.All(w => w.Slick is > 0.3f and < 0.7f);
            Run(car, CarSim.SlickClean);
            bool wiped = car.Wheels.All(w => w.Slick == 0f);
            Check(soaked && stillSlick && half && wiped, "slurry or oil on the tread is picked up at once, stays after the patch and is wiped off over the clean-up time");
            car = NewCar(new FlatGround { Surface = SurfaceType.Puddle });
            foreach (var w in car.Wheels) w.Slick = 1f;
            Run(car, CarSim.SlickClean / 3f * 1.05f);
            Check(car.Wheels.All(w => w.Slick < 0.05f), "water washes the tyres three times faster");
        }
        var t = TrackBuilder.Build(TrackDef.Load("res://tracks/ilyich.json"));
        var actor = new SlurryTractor(); AddChild(actor); actor.Init(t);
        int staticCount = t.Obstacles.Count - 2;
        bool clear = true, dry = true, roadDeposit = false, fieldDeposit = false;
        var overlaps = new System.Collections.Generic.HashSet<int>();
        for (int i = 0; i < 2000; i++)
        {
            actor.Tick(0.1f);
            for (int slot = staticCount; slot < t.Obstacles.Count; slot++)
            {
                var mover = t.Obstacles[slot];
                dry &= t.Sample(mover.Center.X, mover.Center.Y).Surface is not (SurfaceType.Water or SurfaceType.Shallow);
                for (int j = 0; j < staticCount; j++)
                    if (mover.Center.DistanceTo(t.Obstacles[j].Center) < mover.Radius + t.Obstacles[j].Radius + 0.15f)
                    { clear = false; overlaps.Add(j); }
            }
            foreach (var patch in actor.Trail.Patches)
            {
                int ci = t.NearestCenter(patch.Center, out float d);
                roadDeposit |= d < t.HalfWidth[ci];
                fieldDeposit |= d > t.HalfWidth[ci] + 5f;
            }
        }
        foreach (int j in overlaps) GD.Print($"  route overlaps #{j}: {t.Obstacles[j].Center}, radius {t.Obstacles[j].Radius:F2}");
        Check(clear && dry, "tractor and articulated tanker avoid static obstacles and water for complete circuits");
        Check(roadDeposit && fieldDeposit, "short spreading runs reach the fields and leave a late-shutoff tail on the road");
        Check(t.Slurry == actor.Trail && actor.Trail.Patches.Count <= SlurryTrail.Capacity, "track samples the bounded temporary slurry overlay");
        actor.Reset();
        Check(t.Slurry.Patches.Count == 0, "reset clears both visual and physical deposits");

        // The world tape: every patch is an effect event, the spray a sound channel; a tape-driven tractor lays none of its own and shows what the tape says.
        {
            var host = new SlurryEventLog();
            actor.TapeFx = host;
            for (int i = 0; i < 700; i++) actor.Tick(0.1f);
            int made = host.Events.Count;
            bool allKind = host.Events.TrueForAll(e => e.Kind == TapeFxKind.SlurryPatch && e.Life == SlurryTrail.Lifetime && e.Param == 0f);
            Check(made > 20 && allKind, "every patch the live tractor lays is a slurry-patch event with its life");
            var reg = System.Linq.Enumerable.FirstOrDefault(Tape.SoundsOf(actor.Model) ?? new System.Collections.Generic.List<TapeSoundReg>(), r => r.Channel == TapeSoundChannel.SlurrySpray);
            Check(reg != null && TapeSoundRange.Of(TapeSoundChannel.SlurrySpray) == (0f, 2f) && TapeSoundRange.FromByte(TapeSoundChannel.SlurrySpray, TapeSoundRange.ToByte(TapeSoundChannel.SlurrySpray, 2f)) == 2f
                && actor.GetMeta(WorldTapeConst.VersionMeta, 0).AsInt32() == 3, "the spray is a registered sound channel with an exact 0, 1, 2 range");
            actor.TapeDriven = true;
            Check(actor.Trail.Patches.Count == 0 && !actor.Spraying, "a tape-driven tractor starts with a clean trail");
            reg.Set(1f);
            for (int i = 0; i < 10; i++) actor.Tick(0.1f);
            bool sprays = actor.Spraying;
            reg.Set(0f);
            actor.Tick(0.1f);
            Check(sprays && !actor.Spraying && actor.Trail.Patches.Count == 0, "the tape's spray channel shows the fan and lays no patch of its own");
            var last = host.Events[^1];
            actor.ReplayPatch(new Vector2(last.Pos.X, last.Pos.Z), false, 4f);
            actor.ReplayPatch(new Vector2(last.Pos.X + 50f, last.Pos.Z), true, 10f);
            actor.ReplayPatch(new Vector2(last.Pos.X + 100f, last.Pos.Z), false, 20f);      // dried long ago: none
            float w0 = actor.Trail.Wetness(new Vector2(last.Pos.X, last.Pos.Z)), w1 = actor.Trail.Wetness(new Vector2(last.Pos.X + 50f, last.Pos.Z));
            Check(actor.Trail.Patches.Count == 2 && Math.Abs(w0 - (1f - 4f / SlurryTrail.Lifetime)) < 0.01f && Math.Abs(w1 - (1f - 10f / SlurryTrail.RevengeLifetime)) < 0.01f,
                "a patch restored from the tape has the wetness of its age (the revenge's lasts longer)");
            actor.ReplayClear();
            Check(actor.Trail.Patches.Count == 0, "a seek takes the replay's patches away");
            actor.TapeDriven = false;
            actor.TapeFx = null;
            actor.Reset();
        }

        // The driver's grandmother: the tractor goes to the road, spreads along it for three laps, then drives home.
        for (int i = 0; i < 100; i++) actor.Tick(0.1f);
        bool started = actor.StartRevenge() && actor.Avenging && actor.RevengeLapsLeft == SlurryTractor.RevengeLaps;
        bool onRoad = false, wetRoad = false, bounded = true;
        float worstEdge = 0;
        for (int i = 0; i < 4000; i++)
        {
            actor.Tick(0.1f);
            bounded &= actor.Trail.Patches.Count <= SlurryTrail.Capacity;
            if (!onRoad && actor.Avenging && actor.Spraying) onRoad = true;
            if (onRoad)
            {
                int ci = t.NearestCenter(actor.Position2D, out float dist);
                worstEdge = Math.Max(worstEdge, dist - t.HalfWidth[ci]);
                wetRoad |= t.Sample(actor.Position2D.X, actor.Position2D.Y).SlurryWetness > 0 || actor.Trail.Patches.Count > 20;
            }
            if (onRoad && i > 600) break;
        }
        Check(started && onRoad && wetRoad && bounded, "the revenge: the tractor reaches the road and spreads slurry along it (bounded patches)");
        Check(worstEdge < 0.5f, "while avenging the tractor stays on the road");
        var lapsLeft = new System.Collections.Generic.List<int>();
        actor.RevengeLapPassed += n => lapsLeft.Add(n);
        actor.RevengeLapDone(); actor.RevengeLapDone();
        bool twoLaps = actor.Avenging;
        actor.RevengeLapDone();
        Check(twoLaps && !actor.Avenging, "the revenge lasts exactly three laps");
        Check(lapsLeft.SequenceEqual(new[] { 2, 1 }), "the laps left are reported after the first and the second lap");
        for (int i = 0; i < 6000 && !actor.HasDeparted; i++) actor.Tick(0.1f);
        Check(actor.HasDeparted, "after the revenge the tractor drives back to its fields");
        actor.Reset();
        RemoveChild(actor); actor.Free();
        SlurryBusMeetTest();
    }

    /// <summary>The grandmother's revenge on the road meets the bus coming the other way: they pass each other in their lanes instead of standing nose to nose.</summary>
    void SlurryBusMeetTest()
    {
        var t = TrackBuilder.Build(TrackDef.Load("res://tracks/ilyich.json"));
        var fx = new TyreEffects(); AddChild(fx); fx.Init(t);
        int staticObstacles = t.Obstacles.Count;
        var actor = new SlurryTractor(); AddChild(actor); actor.Init(t);
        var bus = new SovietBus(); AddChild(bus);
        int n = t.Center.Length;
        bus.Init(t, fx, 0, null, staticObstacles);
        for (int i = 0; i < 100; i++) actor.Tick(0.1f);
        actor.StartRevenge();
        // Get the tractor onto the road, then put the bus 80 m ahead of it on its own way (the bus drives against the lap).
        for (int i = 0; i < 1200 && !(actor.Avenging && actor.Spraying); i++) { actor.Tick(0.1f, true, bus); bus.Tick(0.1f, null); }
        if (!actor.Spraying) { Check(false, "the revenge tractor reaches the road with a bus on the route"); RemoveChild(bus); bus.Free(); RemoveChild(actor); actor.Free(); RemoveChild(fx); fx.Free(); return; }
        int k = t.NearestCenter(actor.Position2D, out _);
        bus.Warp(k + 90);
        float standing = 0f, worst = 0f; bool passed = false;
        Vector2 dir = t.CenterDir(k);
        for (int i = 0; i < 3600; i++)      // six minutes
        {
            actor.Tick(0.1f, true, bus); bus.Tick(0.1f, null, false, null, actor);
            bool both = actor.Position2D.DistanceTo(bus.Position2D) < 20f && bus.Speed < 0.2f;
            standing = both ? standing + 0.1f : 0f;
            worst = Math.Max(worst, standing);
            if ((bus.Position2D - actor.Position2D).Dot(t.CenterDir(t.NearestCenter(actor.Position2D, out _))) < -3f) { passed = true; break; }
        }
        Check(passed && worst < 8f, $"the revenge tractor and the bus pass each other on the road (longest standstill face to face {worst:F1} s)");
        RemoveChild(bus); bus.Free(); RemoveChild(actor); actor.Free(); RemoveChild(fx); fx.Free();
    }

    void VehicleTrafficTest()
    {
        var t = TrackBuilder.Build(TrackDef.Load("res://tracks/ilyich.json"));
        var fx = new CCCPRacing.Car.TyreEffects(); AddChild(fx); fx.Init(t);
        int staticObstacles = t.Obstacles.Count;
        var tractor = new DrunkTractor(); AddChild(tractor);
        tractor.Init(t, fx, new Vector2(142, 36), MathF.PI);
        var bus = new SovietBus(); AddChild(bus);
        int crossing = t.NearestCenter(new Vector2(169, 50), out _);
        bus.Init(t, fx, crossing + 35, null, staticObstacles);

        // Hold a tractor in the bus's lane and let the timetable approach it.
        bus.Warp(crossing);
        Vector2 crossingPoint = bus.Position2D;
        bus.Warp(crossing + 35);
        t.Obstacles[tractor.BodyObstacle] = new Obstacle(crossingPoint, 1.3f);
        t.Obstacles[tractor.PloughObstacle] = new Obstacle(new Vector2(-1e4f, -1e4f), 0.1f);
        bool clear = true;
        for (int frame = 0; frame < 600; frame++)
        {
            bus.Tick(1f / 60f, null, false, tractor);
            clear &= !bus.Blocks(crossingPoint, 1.3f);
        }
        Check(clear && bus.Speed < 0.5f, "bus brakes and stops before a tractor in its lane");
        RemoveChild(bus); bus.Free();
        RemoveChild(tractor); tractor.Free();
        RemoveChild(fx); fx.Free();
    }

    sealed class SlurryEventLog : ITapeFxHost
    {
        public readonly System.Collections.Generic.List<TapeEvent> Events = new();
        public void Fx(in TapeEvent e) => Events.Add(e);
    }
}
