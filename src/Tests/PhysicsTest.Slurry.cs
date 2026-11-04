using System;
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
        RemoveChild(actor); actor.Free();
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
}
