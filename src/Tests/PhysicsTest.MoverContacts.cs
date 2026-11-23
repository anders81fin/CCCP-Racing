using System;
using System.Collections.Generic;
using CCCPRacing.Car;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    /// <summary>The vehicles of the world and the player's car and the people (the owner's report of 2026-11-22: the car and the mechanic were air to the moving things):
    /// the car is among the solids the vehicles yield to; a tractor driven at a parked car stops short of it and never overlaps it; a tractor rolling into a person knocks them over
    /// and goes on; a slow or distant mover knocks nobody; nothing is left in the static list once it is cleared.</summary>
    void MoverContactTest()
    {
        var t = TrackBuilder.Build(TrackDef.Load("res://tracks/ilyich.json"));
        var fx = new TyreEffects(); AddChild(fx); fx.Init(t);
        // an open stretch: 90 m along +X with nothing within 4.5 m of its line
        Vector2 start = default; bool found = false;
        for (float y = 30; y < t.WorldSize - 30 && !found; y += 10)
            for (float x = 30; x < t.WorldSize - 130 && !found; x += 10)
            {
                bool clear = true;
                foreach (var o in t.Obstacles)
                {
                    if (o.Center.X < -9000f) continue;
                    if (o.Center.X > x - 6 && o.Center.X < x + 90 && MathF.Abs(o.Center.Y - y) < o.Radius + 4.5f) { clear = false; break; }
                }
                if (clear) { start = new Vector2(x, y); found = true; }
            }
        Check(found, "an open stretch of ground for the contact test");
        if (!found) { fx.Free(); return; }

        // 1: the car among the solids
        var car = new CarSim(new CarParams(), t);
        var carAt = start + new Vector2(30, 0);
        car.Pos = new Vector3(carAt.X, t.MeshHeight(carAt.X, carAt.Y) + 0.5f, carAt.Y);
        MoverBlockers.Clear();
        Check(!GulagTruck.BlocksAt(carAt, 1.3f), "no car registered: nothing blocks at its place");
        MoverBlockers.AddCar(car);
        Check(GulagTruck.BlocksAt(carAt, 1.3f) && !GulagTruck.BlocksAt(carAt + new Vector2(0, 12), 1.3f), "the player's car is a solid for BlocksAt (the tractors, the Kozlik, the bus), and only where it is");
        bool inSolids = false; foreach (var s in GulagTruck.Solids) inSolids |= s.Centre.DistanceTo(carAt) < 2f;
        Check(inSolids, "the car is in GulagTruck.Solids (the bus's look-ahead, the ploughs, TrafficCap, the BTRs, the tanks)");
        MoverBlockers.Clear();
        Check(!GulagTruck.BlocksAt(carAt, 1.3f) && MoverBlockers.Cars.Count == 0, "cleared: the list is empty again");

        // 1b: the Kozlik on the car's tail is not solid to the car, but it is to the traffic (and not to itself)
        var ghostOwner = new object();
        MoverBlockers.SetGhost(ghostOwner, new[] { new MoverBlockers.Circle(start + new Vector2(0, 20), 1.2f, Vector2.Zero), new MoverBlockers.Circle(start + new Vector2(2, 20), 1.2f, Vector2.Zero) });
        Check(GulagTruck.BlocksAt(start + new Vector2(0, 20), 1.3f) && !GulagTruck.BlocksAt(start + new Vector2(0, 20), 1.3f, ghostOwner), "a ghost vehicle (the Kozlik on the car's tail) blocks the other vehicles, never itself");
        Check(!GulagTruck.BlocksAt(carAt, 1.3f) && GulagTruck.BlocksAt(start + new Vector2(0, 20), 1.3f, null, false), "and it is not among the cars");
        MoverBlockers.SetGhost(ghostOwner, null);
        Check(!GulagTruck.BlocksAt(start + new Vector2(0, 20), 1.3f), "the ghost is gone when it is cleared");

        // 2: a tractor driven at the parked car stops short of it (the tractor wanders and weaves: the test asks only that it never overlaps the car and that it did come close)
        var nearCar = start + new Vector2(8, 0);
        car.Pos = new Vector3(nearCar.X, t.MeshHeight(nearCar.X, nearCar.Y) + 0.5f, nearCar.Y);
        var tractor = new DrunkTractor(); AddChild(tractor); tractor.Init(t, fx, start, 0f);
        tractor.PlaceForTest(start, Vector2.Right, start + new Vector2(80, 0), 3.5f);
        float minGap = 99f; float topVelocity = 0f;
        for (int i = 0; i < 60 * 12; i++)
        {
            MoverBlockers.Clear(); if (!OS.HasEnvironment("MOVERTEST_NOCAR")) MoverBlockers.AddCar(car);      // (the env var runs the old behaviour: the test must fail then)
            tractor.Tick(1f / 60f, true);
            if (OS.HasEnvironment("MOVERTEST_NOCAR")) MoverBlockers.AddCar(car);
            Vector2 body = t.Obstacles[tractor.BodyObstacle].Center, plough = t.Obstacles[tractor.PloughObstacle].Center;
            topVelocity = MathF.Max(topVelocity, t.Obstacles[tractor.BodyObstacle].Velocity.Length());
            foreach (var c in MoverBlockers.Cars)
                minGap = MathF.Min(minGap, MathF.Min(body.DistanceTo(c.Centre) - 1.3f - c.Radius, plough.DistanceTo(c.Centre) - 1.1f - c.Radius));
        }
        MoverBlockers.Clear();
        Check(minGap > -0.05f, $"the tractor never overlaps the parked car (smallest gap {minGap:F2} m)");
        Check(minGap < 2.5f, $"it did come up to the car (closest gap {minGap:F2} m): the test means something");
        Check(topVelocity > 1f, $"the tractor's obstacle carries its velocity (top {topVelocity:F1} m/s): the cars, the people and the other vehicles see that it moves");

        // 3: a vehicle rolling into a person knocks them over, and goes on (a synthetic mover: the pass is the same for every vehicle that sets its obstacle's velocity)
        int mover = t.Obstacles.Count; t.Obstacles.Add(new Obstacle(start, 1.3f, new Vector2(3f, 0), Material: ImpactMaterial.Metal));
        t.Obstacles.Add(new Obstacle(start + new Vector2(20, 0.3f), 0.45f, default, false, 0.5f, true, 2.5f, Material: ImpactMaterial.Soft)); int man = t.Obstacles.Count - 1;
        t.Obstacles.Add(new Obstacle(start + new Vector2(20, 14f), 0.45f, default, false, 0.5f, true, 2.5f, Material: ImpactMaterial.Soft)); int bystander = t.Obstacles.Count - 1;
        bool manDown = false, bystanderDown = false; Vector3 knockedBy = default;
        var knockables = new List<int> { man, bystander };
        for (int i = 0; i < 60 * 15; i++)
        {
            t.Obstacles[mover] = new Obstacle(start + new Vector2(3f * i / 60f, 0), 1.3f, new Vector2(3f, 0), Material: ImpactMaterial.Metal);
            MoverBlockers.KnockPass(t, knockables, (idx, vel) =>
            {
                if (idx == man && !manDown) { manDown = true; knockedBy = vel; knockables.Remove(man); t.Obstacles[man] = new Obstacle(new Vector2(-1e4f, -1e4f), 0.1f); return true; }
                if (idx == bystander) { bystanderDown = true; return true; }
                return false;
            });
        }
        Check(manDown && knockedBy.Length() > 1f, $"a vehicle rolling into the man knocked him over (its speed {knockedBy.Length():F1} m/s)");
        Check(!bystanderDown, "a bystander it never touched stays on his feet");
        t.Obstacles[mover] = new Obstacle(new Vector2(-1e4f, -1e4f), 0.1f);

        // 4: a slow mover, or one a long way off, knocks nobody
        var slow = new List<int>(); int slowMan = t.Obstacles.Count; t.Obstacles.Add(new Obstacle(new Vector2(300, 300), 0.45f)); slow.Add(slowMan);
        int slowMover = t.Obstacles.Count; t.Obstacles.Add(new Obstacle(new Vector2(300.5f, 300), 1.3f, new Vector2(0.3f, 0)));
        int fell = MoverBlockers.KnockPass(t, slow, (i, v) => true);
        Check(fell == 0, "a mover creeping at 0.3 m/s knocks nobody");
        t.Obstacles[slowMover] = new Obstacle(new Vector2(300.5f, 300), 1.3f, new Vector2(2f, 0));
        Check(MoverBlockers.KnockPass(t, slow, (i, v) => true) == 1, "the same mover at 2 m/s does");
        t.Obstacles[slowMover] = new Obstacle(new Vector2(310f, 300), 1.3f, new Vector2(2f, 0));
        Check(MoverBlockers.KnockPass(t, slow, (i, v) => true) == 0, "and a mover ten metres away does not");

        RemoveChild(tractor); tractor.Free(); RemoveChild(fx); fx.Free();

        // 5: the mechanic is a person like the others once he is up: a vehicle (or the car) that rolls into him knocks him over, the job is off and the car is let go
        foreach (string id in new[] { "ilyich", "bonus" })
        {
            var track = TrackBuilder.Build(TrackDef.Load($"res://tracks/{id}.json"));
            var crew = new DrunkMen(); AddChild(crew); crew.Init(track);
            var mcar = new CarSim(new CarParams(), new FlatGround());
            var post = crew.MechanicPostPosition.Value;
            mcar.Reset(new Vector3(post.X + 5, .6f, post.Y), 0);
            var knocks = new Dictionary<int, Func<Vector3, bool>>(); foreach (var k in crew.Knocks()) knocks[k.Item1] = k.Item2;
            int asleepObstacle = -1; foreach (var (idx, _) in knocks) if (track.Obstacles[idx].Center.X < -9000f) asleepObstacle = idx;
            Check(asleepObstacle >= 0, $"mechanic {id}: his obstacle exists and is parked out of the world while he sleeps on his bench");
            bool walking = false;
            for (int i = 0; i < 60 * 40 && !walking; i++) { crew.MechanicTick(1f / 60f, mcar, true, i < 5, true, .5f); crew.Tick(1f / 60f, mcar, 0f); walking = crew.HoldsCar && crew.MechanicUp; if (walking && crew.Repairing) break; }
            Check(walking, $"mechanic {id}: the horn wakes him and he sets off for the car (holding it)");
            int mi = -1; float best = 99f; foreach (var (idx, _) in knocks) { float d = track.Obstacles[idx].Center.DistanceTo(crew.MechanicPosition); if (d < best) { best = d; mi = idx; } }
            Check(mi >= 0 && best < 0.1f, $"mechanic {id}: his circle is where he is ({best:F2} m off)");
            int mover2 = track.Obstacles.Count; track.Obstacles.Add(new Obstacle(crew.MechanicPosition + new Vector2(-1f, 0), 1.3f, new Vector2(3f, 0), Material: ImpactMaterial.Metal));
            int hit = MoverBlockers.KnockPass(track, knocks.Keys, (i, v) => knocks[i](v));
            Check(hit >= 1, $"mechanic {id}: a vehicle rolling into him knocks him over");
            crew.MechanicTick(1f / 60f, mcar, true, false, true, .5f);
            Check(!crew.HoldsCar, $"mechanic {id}: knocked down, the job is off and the car is let go");
            Check(track.Obstacles[mi].Center.X < -9000f, $"mechanic {id}: while he lies his circle is out of the way");
            track.Obstacles[mover2] = new Obstacle(new Vector2(-1e4f, -1e4f), 0.1f);
            for (int i = 0; i < 60 * 6; i++) { crew.MechanicTick(1f / 60f, mcar, true, false, true, .5f); crew.Tick(1f / 60f, mcar, 0f); }
            Check(track.Obstacles[mi].Center.X > -9000f || !crew.MechanicUp, $"mechanic {id}: he gets up again (his circle is back, or he is back on his bench)");
            crew.Free();
        }
    }
}
