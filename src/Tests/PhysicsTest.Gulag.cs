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
}
