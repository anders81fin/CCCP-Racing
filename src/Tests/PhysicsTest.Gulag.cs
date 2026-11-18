using System.Linq;
using System;
using CCCPRacing.Camera;
using CCCPRacing.Car;
using CCCPRacing.Race;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    void SchoolFramingTest()
    {
        Vector3 offset = new(0, 31, 23);
        bool framed = true;
        foreach (float aspect in new[] { 16f / 9f, 4f / 3f, 9f / 16f })
        foreach (Vector3 subject in new[] { new Vector3(50, 0, 0), new(0, 0, 70), new(-30, 18, -45), Vector3.Zero })
        {
            Vector3 car = Vector3.Zero, center = car.Lerp(subject, 0.5f);
            float zoom = ChaseCamera.PairZoom(car, subject, center, offset, 45, aspect);
            Basis basis = Basis.LookingAt(-offset.Normalized(), Vector3.Up);
            foreach (Vector3 point in new[] { car, subject })
            {
                Vector3 p = basis.Inverse() * (point - center - offset * zoom);
                float tan = MathF.Tan(Mathf.DegToRad(22.5f));
                framed &= -p.Z > 0 && (Math.Abs(p.X) + 5) / (-p.Z * tan * aspect) <= 0.781f
                    && (Math.Abs(p.Y) + 5) / (-p.Z * tan) <= 0.621f;
            }
        }
        Check(framed, "school camera: car and subject fit with model/caption margins across screen shapes");
    }

    void GulagWalkingTest()
    {
        bool clear = true;
        for (int angle = 0; angle < 360; angle += 15)
        {
            var frame = new Transform3D(new Basis(Vector3.Up, Mathf.DegToRad(angle)), new(12, 0, -8));
            var blocks = new[] {
                new GulagTruck.WalkBlock(frame, new(-2.2f, -5), new(2.2f, 5.8f)),
                new GulagTruck.WalkBlock(new Transform3D(new Basis(Vector3.Up, 0.7f), frame * new Vector3(0, 0, 12)), new(-1.5f, -2.7f), new(1.5f, 2.7f)),
                new GulagTruck.WalkBlock(new Transform3D(Basis.Identity, frame * new Vector3(3, 0, 8)), new(-1.1f, -1.1f), new(1.1f, 1.1f)) };
            Vector3 from = frame * new Vector3(2.6f, 0, -1);
            Vector3 to = frame * new Vector3(-3, 0, 12);
            var path = GulagTruck.WalkingRoute(from, to, blocks);
            clear &= path.Length > 1 && path[^1].DistanceTo(to) < 0.01f;
            for (int i = 1; i < path.Length; i++)
                foreach (var block in blocks) clear &= !block.Cuts(path[i - 1], path[i]);
        }
        Check(clear, "Gulag walking: routes clear both rotated vehicles and the player for 24 headings");

        var track = TrackBuilder.Build(TrackDef.Load("res://tracks/bonus.json"));
        var fx = new TyreEffects(); fx.Init(track); AddChild(fx);
        foreach (int road in new[] { 40, 300, 600, 950 })
        {
            var car = new CarSim(new CarParams(), track);
            SchoolScript.SetRolling(car, track, SchoolScript.RoadPoint(track, road, 0), track.CenterDir(road), 0);
            var truck = new GulagTruck(); AddChild(truck); truck.Init(track, fx); truck.Call(car);
            var officer = truck.GetNode<Node3D>("Officer");
            var driver = truck.GetNode<Node3D>("Driver");
            var arm = officer.GetNode<Node3D>("GunArm");
            int shotEvents = 0;
            bool seatedAtShot = true, exitsAfterShot = true;
            var visual = CarVisual.Create(new CarParams(), Colors.Red); AddChild(visual);
            truck.WarningShot += hitCar =>
            {
                shotEvents++;
                seatedAtShot &= !driver.Visible;
                visual.Damage.ShootHeadlamp(hitCar, fx);
            };
            bool noOverlap = true, aims = true;
            bool aimReported = false, overlapReported = false;
            int aimedFrames = 0;
            var carBlock = new GulagTruck.WalkBlock(new Transform3D(Basis.LookingAt(-(car.Rot.Z with { Y = 0 }).Normalized(), Vector3.Up), car.Pos), new(-1.45f, -2.65f), new(1.45f, 2.65f));
            for (int frame = 0; frame < 60 * 90 && !truck.Gone; frame++)
            {
                truck.Tick(1f / 60, car);
                if (driver.Visible) exitsAfterShot &= shotEvents == 1;
                if (!officer.Visible) continue;
                noOverlap &= !carBlock.Contains(officer.GlobalPosition);
                Vector3 local = truck.Model.ToLocal(officer.GlobalPosition);
                bool atCabDoor = local.X > 1.1f && local.X < 2.7f && local.Z > -2.5f && local.Z < -0.1f;
                var truckBlock = new GulagTruck.WalkBlock(truck.Model.GlobalTransform, new(-2.15f, -4.95f), new(2.15f, 5.7f));
                noOverlap &= atCabDoor || !truckBlock.Contains(officer.GlobalPosition);
                if (driver.Visible) noOverlap &= ((driver.GlobalPosition - officer.GlobalPosition) with { Y = 0 }).Length() > 0.9f;
                if (!noOverlap && !overlapReported) { GD.Print($"overlap road {road} frame {frame}: off={officer.GlobalPosition}, driver={driver.GlobalPosition}, car={car.Pos}, dist={officer.GlobalPosition.DistanceTo(driver.GlobalPosition)}"); overlapReported = true; }
                Vector3 barrel = -arm.GlobalBasis.Y.Normalized();
                if (Math.Abs(barrel.Y) < 0.8f && truck.Boarding)
                {
                    Vector3 target = truck.AimTarget;
                    float dot = barrel.Dot((target - arm.GlobalPosition).Normalized());
                    aims &= dot > 0.999f;
                    if (!aims && !aimReported) { GD.Print($"aim road {road} frame {frame}: dot={dot}, barrel={barrel}, target={target}, arm={arm.GlobalPosition}, rot={arm.Rotation}"); aimReported = true; }
                    aimedFrames++;
                }
            }
            Check(truck.Gone && noOverlap && aims && aimedFrames > 60 && truck.WarningShots == 1 && shotEvents == 1 && seatedAtShot && exitsAfterShot,
                $"Gulag arrest at road {road}: completes, avoids car/player, tracks pistol ({aimedFrames} frames, overlap={!noOverlap}, aim={aims}, state={truck.State})");
            Check(!visual.Damage.HeadlightWorks(true) && visual.Damage.HeadlightWorks(false) && visual.Damage.Dents == 0,
                "Gulag warning shot breaks only the left headlamp");
            visual.Damage.Reset();
            Check(visual.Damage.HeadlightWorks(true) && !visual.Damage.IsDamaged, "Gulag lamp damage resets for the next session");
            visual.Free();
            truck.Free();
        }
        fx.Free();
    }

    /// <summary>The ZIL that comes for a wrecked car inside Siberia's labour camp stays on the road: its whole path keeps clear of the huts, towers and wire (it used to cut across the camp in a straight line to the car).</summary>
    void GulagCampTruckTest()
    {
        var def = TrackDef.Load("res://tracks/siberia.json");
        var track = TrackBuilder.Build(def);
        var camp = new Node3D(); AddChild(camp);
        camp.AddChild(GulagCamp.Build(track, def.Gulag, track.Obstacles));      // (the huts, towers and wire are obstacles once the camp is built)
        var fx = new TyreEffects(); fx.Init(track); AddChild(fx);
        var spots = new System.Collections.Generic.List<Vector2>();
        int n = track.Center.Length, mid = ((int)(def.Gulag.From + def.Gulag.To) / 2 % n + n) % n;
        spots.Add(track.Center[mid]);
        foreach (var hut in GulagCamp.Huts(track, def.Gulag)) spots.Add(hut.Position + (track.Center[hut.Index] - hut.Position).Normalized() * 4.5f);
        bool clear = true; int checkedSpots = 0; float worst = 99f;
        foreach (var spot in spots.Take(8))
        {
            var car = new CarSim(new CarParams(), track);
            car.Reset(new Vector3(spot.X, track.MeshHeight(spot.X, spot.Y) + .6f, spot.Y), 0);
            var truck = new GulagTruck(); AddChild(truck); truck.Init(track, fx); truck.Call(car);
            var path = (System.Collections.Generic.List<Vector2>)typeof(GulagTruck).GetField("_path", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(truck);
            checkedSpots++;
            foreach (var o in track.Obstacles)
            {
                if (o.Radius < .9f || o.Velocity != Vector2.Zero || o.Center.X < -1000f) continue;
                for (int k = 0; k + 1 < path.Count; k++)
                {
                    var seg = path[k + 1] - path[k];
                    float u = Math.Clamp((o.Center - path[k]).Dot(seg) / Math.Max(.01f, seg.LengthSquared()), 0f, 1f);
                    float room = o.Center.DistanceTo(path[k] + seg * u) - o.Radius;
                    worst = Math.Min(worst, room);
                    if (room < 1.4f) clear = false;
                }
            }
            truck.Free();
        }
        Check(clear && checkedSpots >= 2, $"Gulag camp: the truck's path keeps clear of every solid, standing obstacle ({checkedSpots} wrecks in the camp, least room {worst:0.0} m)");
        fx.Free(); camp.Free();
    }

    /// <summary>The gulag truck, its officer and its driver are solid: every vehicle stops short of them (BlocksAt, TrafficCap), and the walkers go round the standing obstacles of the scene.</summary>
    void GulagSolidsTest()
    {
        var def = TrackDef.Load("res://tracks/siberia.json");
        var track = TrackBuilder.Build(def);
        var fx = new TyreEffects(); fx.Init(track); AddChild(fx);
        Vector2 spot = track.Center[120];
        var car = new CarSim(new CarParams(), track);
        car.Reset(new Vector3(spot.X, track.MeshHeight(spot.X, spot.Y) + .6f, spot.Y), 0);
        var truck = new GulagTruck(); AddChild(truck); truck.Init(track, fx); truck.Call(car);
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var officer = (Node3D)typeof(GulagTruck).GetField("_officer", flags).GetValue(truck);
        var driver = (Node3D)typeof(GulagTruck).GetField("_driver", flags).GetValue(truck);
        Check(GulagTruck.Solids.Count() == 3, "gulag solids: on the road the truck is three solid circles");
        officer.Visible = true; officer.GlobalPosition = new Vector3(spot.X + 40f, 0, spot.Y + 40f);
        driver.Visible = true; driver.GlobalPosition = new Vector3(spot.X + 50f, 0, spot.Y + 40f);
        var people = GulagTruck.Solids.Skip(3).ToList();
        Check(people.Count == 2 && people.All(p => p.Radius >= 0.5f), "gulag solids: the officer and the driver are solid when out of the truck");
        Vector2 officerAt = new(officer.GlobalPosition.X, officer.GlobalPosition.Z);
        Check(GulagTruck.BlocksAt(officerAt + new Vector2(1.2f, 0), 1.5f) && !GulagTruck.BlocksAt(officerAt + new Vector2(0, -9f), 1.5f),
            "gulag solids: a vehicle's body circle that would overlap the officer is blocked, one beside him is not");
        float near = TrafficCap.Cap(track, officerAt - new Vector2(8f, 0), new Vector2(1, 0), 12f, 1.3f, i => true);
        float far = TrafficCap.Cap(track, officerAt - new Vector2(8f, 25f), new Vector2(1, 0), 12f, 1.3f, i => true);
        Check(near < 5f && far > 1e6f, $"gulag solids: a vehicle 8 m behind the officer is capped to a stop ({near:F1} m/s), one in another lane is not");
        // The walkers go round the standing obstacles of the scene.
        Vector2 a = spot + new Vector2(-6, 0), b = spot + new Vector2(6, 0), mid = spot;
        track.Obstacles.Add(new Obstacle(mid, 0.8f));
        var blocks = (GulagTruck.WalkBlock[])typeof(GulagTruck).GetMethod("FixedBlocksNear", flags).Invoke(truck, new object[] { spot });
        Check(blocks.Any(bl => bl.Contains(new Vector3(mid.X, 0, mid.Y))), "gulag solids: a standing obstacle by the arrest is a block for the walkers");
        var route = GulagTruck.WalkingRoute(new Vector3(a.X, 0, a.Y), new Vector3(b.X, 0, b.Y), blocks);
        bool through = false;
        for (int i = 0; i + 1 < route.Length; i++) foreach (var bl in blocks) if (bl.Cuts(route[i], route[i + 1])) through = true;
        Check(route.Length >= 3 && !through, $"gulag solids: the route from one side of a post to the other goes round it ({route.Length} points), never through");
        track.Obstacles.RemoveAt(track.Obstacles.Count - 1);
        truck.Free(); fx.Free();
    }

    // --kozlik-gulag-test: the UAZ patrol drives the road with the gulag truck standing in it, and later with the officer standing in his lane: it stops short of them, never into them.
    void KozlikGulagTest()
    {
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        foreach (string id in new[] { "ilyich", "siberia" })
        foreach (bool officerOnly in new[] { false, true })
        {
            var track = TrackBuilder.Build(TrackDef.Load($"res://tracks/{id}.json"));
            var fx = new TyreEffects(); fx.Init(track); AddChild(fx);
            var patrol = new KozlikPatrol(); AddChild(patrol);
            patrol.Init(track, fx, null, null, 100);
            int n = track.Center.Length, carIndex = 300 % n;
            Vector2 carAt = track.Center[carIndex];
            var car = new CarSim(new CarParams(), track);
            car.Reset(new Vector3(carAt.X, track.MeshHeight(carAt.X, carAt.Y) + .6f, carAt.Y), 0);
            var truck = new GulagTruck(); AddChild(truck); truck.Init(track, fx); truck.Call(car);
            var officer = (Node3D)typeof(GulagTruck).GetField("_officer", flags).GetValue(truck);
            if (officerOnly)
            {
                // The truck far off, the officer standing on the road in the Kozlik's way.
                var posField = typeof(GulagTruck).GetField("_pos", flags); posField.SetValue(truck, new Vector2(-500, -500));
                Vector2 at = track.Center[250 % n];
                officer.Visible = true; officer.GlobalPosition = new Vector3(at.X, track.MeshHeight(at.X, at.Y), at.Y);
            }
            var obs = (int[])typeof(KozlikPatrol).GetField("_obs", flags).GetValue(patrol);
            float closest = 99f; bool overlapped = false;
            for (int frame = 0; frame < 60 * 120; frame++)
            {
                patrol.Tick(1f / 60, null, 0);
                if (patrol.Guarding) continue;
                // (the UAZ's own three obstacle circles, as the track holds them)
                foreach (int index in obs)
                {
                    var own = track.Obstacles[index];
                    if (own.Center.X < -9000f) continue;
                    foreach (var s in GulagTruck.Solids)
                    {
                        float gap = own.Center.DistanceTo(s.Centre) - s.Radius - own.Radius;
                        closest = MathF.Min(closest, gap);
                        if (gap < -0.05f) overlapped = true;
                    }
                }
            }
            GD.Print($"KOZLIK GULAG {id} {(officerOnly ? "officer" : "truck")}: closest {closest:F2} m, overlapped {overlapped}");
            Check(!overlapped && closest < 20f, $"{id} ({(officerOnly ? "the officer" : "the truck")} in the road): the UAZ never drives into it (closest {closest:F1} m)");
            RemoveChild(truck); truck.Free(); RemoveChild(patrol); patrol.Free(); RemoveChild(fx); fx.Free();
        }
    }
}
