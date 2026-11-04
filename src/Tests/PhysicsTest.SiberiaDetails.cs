using System;
using System.Linq;
using CCCPRacing.Track;
using Godot;
using Noise = CCCPRacing.Track.Noise;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    void SiberiaDetailsTest()
    {
        GD.Print("Siberia work scenes: complete routes, real footprints and synchronized runtime collision");
        var def = TrackDef.Load("res://tracks/siberia.json");
        var t = TrackBuilder.Build(def);
        var plan = t.SiberiaActivities;
        Check(plan != null && plan.Workers.Count == 10 && plan.Workers.Select(w => w.Id).Distinct().Count() == 10,
            "four Siberian work scenes contain ten uniquely named workers");
        if (plan == null) return;
        Check(plan.Workers.Count(w => w.Id.StartsWith("forestry-")) == 3
            && plan.Workers.Count(w => w.Id.StartsWith("camp-")) == 3
            && plan.Workers.Count(w => w.Id.StartsWith("radar-")) == 1
            && plan.Workers.Count(w => w.Id.StartsWith("airbase-")) == 3,
            "forestry, camp, radar and airbase each receive their planned work detail");
        var repeat = SiberiaDetails.Layout(t, def);
        Check(plan.Workers.SequenceEqual(repeat.Workers) && plan.Solids.SequenceEqual(repeat.Solids)
            && plan.Saws.SequenceEqual(repeat.Saws), "authored scene placement and work routes are deterministic");
        string id = t.Id;
        t.Id = "ilyich";
        var other = SiberiaDetails.Layout(t, def);
        t.Id = id;
        Check(other.Workers.Count == 0 && other.Props.Count == 0 && other.Solids.Count == 0 && other.Saws.Count == 0,
            "winter work scenes are restricted to Siberia");

        bool roads = true, obstacles = true, footprints = true, radarAccess = true, forest = true;
        float radius = SiberiaDetails.BodyRadius;
        foreach (var w in plan.Workers)
        {
            bool reportedRoad = false, reportedObstacle = false;
            int steps = Math.Max(1, (int)MathF.Ceiling(w.A.DistanceTo(w.B) / .25f));
            for (int i = 0; i <= steps; i++)
            {
                Vector2 p = w.A.Lerp(w.B, i / (float)steps);
                int ci = t.NearestCenter(p, out float distance);
                if (distance - t.HalfWidth[ci] - radius < 3)
                { if (!reportedRoad) GD.Print($"  {w.Id} road margin {distance - t.HalfWidth[ci] - radius:F2} at {p}"); reportedRoad = true; roads = false; }
                foreach (var o in t.Obstacles)
                    if (p.DistanceTo(o.Center) < o.Radius + radius)
                    { if (!reportedObstacle) GD.Print($"  {w.Id} blocked by {o.Center} r{o.Radius:F2} at {p}"); reportedObstacle = true; obstacles = false; }
                foreach (var tree in t.Taiga)
                    forest &= p.DistanceTo(new Vector2(tree.Pos.X, tree.Pos.Z)) > radius + .35f;
                foreach (var prop in t.Props)
                {
                    Vector2 center = new(prop.Position.X, prop.Position.Z);
                    if (prop.Type == "hangar") footprints &= DetailBoxDistance(p, center, prop.Rotation, 18.5f, 22) >= radius;
                    if (prop.Type == "barracks") footprints &= DetailBoxDistance(p, center, prop.Rotation + MathF.PI / 2, 11, 31) >= radius;
                    if (prop.Type == "radar") footprints &= DetailBoxDistance(p, center, prop.Rotation, 7, 7) >= radius;
                }
                foreach (var hut in GulagCamp.Huts(t, def.Gulag))
                {
                    float halfLength = 7.5f + 2.5f * Noise.Hash01(hut.Offset, (int)hut.Side, 1937);
                    footprints &= DetailBoxDistance(p, hut.Position, hut.Yaw, 6, halfLength * 2) >= radius;
                }
                radarAccess &= p.DistanceTo(SiberiaRadar.Guard) >= 1.2f + radius;
                for (int segment = 0; segment + 1 < SiberiaRadar.Drive.Length; segment++)
                    radarAccess &= Geometry2D.GetClosestPointToSegment(p, SiberiaRadar.Drive[segment], SiberiaRadar.Drive[segment + 1]).DistanceTo(p) >= 1.2f + radius;
            }
        }
        Check(roads, "every complete worker route keeps its body at least three metres beyond the road edge");
        Check(obstacles && forest, "worker bodies clear scene solids, existing collision shapes and reserved forest for the full route");
        Check(footprints, "worker bodies clear rotated hangar, barracks and radar bunker footprints");
        Check(radarAccess, "work routes leave the radar patrol bay and driveway clear");

        var jets = new BalalaikaSorties();
        try
        {
            jets.Init(t, null);
            bool taxiClear = jets.TaxiPath.Count > 2;
            foreach (var w in plan.Workers)
            {
                int steps = Math.Max(1, (int)MathF.Ceiling(w.A.DistanceTo(w.B) / .25f));
                for (int i = 0; i <= steps; i++)
                {
                    Vector2 p = w.A.Lerp(w.B, i / (float)steps);
                    for (int j = 0; j + 1 < jets.TaxiPath.Count; j++)
                        taxiClear &= Geometry2D.GetClosestPointToSegment(p, jets.TaxiPath[j], jets.TaxiPath[j + 1]).DistanceTo(p) >= 3.5f + radius;
                }
            }
            Check(taxiClear, "complete work routes remain outside the actual taxi path and seven-metre wingspan");
        }
        finally { jets.Free(); }

        bool journeys = true;
        foreach (var w in plan.Workers.Where(w => w.A.DistanceSquaredTo(w.B) > .001f))
        {
            var start = SiberiaDetails.Route(w, .05f * w.Period);
            var outward = SiberiaDetails.Route(w, .26f * w.Period);
            var far = SiberiaDetails.Route(w, .52f * w.Period);
            var homeward = SiberiaDetails.Route(w, .76f * w.Period);
            var home = SiberiaDetails.Route(w, .96f * w.Period);
            journeys &= start.Position.DistanceTo(w.A) < .001f && !start.Walking
                && far.Position.DistanceTo(w.B) < .001f && !far.Walking
                && home.Position.DistanceTo(w.A) < .001f && !home.Walking
                && outward.Walking && homeward.Walking
                && outward.Position.DistanceTo(w.A.Lerp(w.B, .5f)) < .001f
                && homeward.Position.DistanceTo(w.A.Lerp(w.B, .5f)) < .001f
                && MathF.Abs(MathF.Cos(outward.Yaw - homeward.Yaw) + 1) < .001f;
        }
        Check(journeys && plan.Workers.Any(w => w.A != w.B), "carriers pause at both endpoints, walk both legs and reverse their facing");
        Check(MathF.Abs(SiberiaDetails.SawStroke(.4f) - 1) < .001f
            && MathF.Abs(SiberiaDetails.SawStroke(1.2f) + 1) < .001f
            && SiberiaDetails.SawStroke(8) == 0,
            "shared forestry saw alternates direction and rests between work bouts");

        int staticCount = t.Obstacles.Count;
        var runtime = SiberiaDetails.Build(t, null);
        try
        {
            Check(t.Obstacles.Count == staticCount + plan.Workers.Count,
                "runtime allocates exactly one moving collision body per worker");
            runtime.Tick(3);
            bool following = true, moving = false;
            for (int i = 0; i < plan.Workers.Count; i++)
            {
                var w = plan.Workers[i];
                var model = runtime.GetNode<WinterWorkerModel>(w.Id);
                var expected = SiberiaDetails.Route(w, w.Phase + 3);
                Vector2 rendered = new(model.Position.X, model.Position.Z);
                var body = t.Obstacles[staticCount + i];
                following &= rendered.DistanceTo(expected.Position) < .001f
                    && body.Center.DistanceTo(rendered) < .001f && MathF.Abs(body.Radius - radius) < .001f
                    && model.Walking == expected.Walking;
                moving |= body.Velocity.LengthSquared() > .001f;
            }
            Check(following && moving, "animated worker position, gait and moving collision update together");
            runtime.Reset(); runtime.Reset();
            bool restored = t.Obstacles.Count == staticCount + plan.Workers.Count;
            for (int i = 0; i < plan.Workers.Count; i++)
                restored &= t.Obstacles[staticCount + i].Center.DistanceTo(SiberiaDetails.Route(plan.Workers[i], plan.Workers[i].Phase).Position) < .001f
                    && t.Obstacles[staticCount + i].Velocity == Vector2.Zero;
            Check(restored, "repeated reset restores route phases without leaking collision slots or retaining velocity");
            bool synchronized = plan.Saws.Count == 1;
            foreach (var saw in plan.Saws)
            {
                var blade = runtime.GetNode<Node3D>(saw.Id);
                Vector3 rest = new(saw.Position.X, t.MeshHeight(saw.Position.X, saw.Position.Y) + 1.45f, saw.Position.Y);
                synchronized &= blade.Position.DistanceTo(rest) < .001f;
                runtime.Tick(.4f);
                Vector3 delta = blade.Position - rest;
                foreach (var worker in plan.Workers.Where(w => w.Job == WinterJob.Saw))
                {
                    var model = runtime.GetNode<WinterWorkerModel>(worker.Id);
                    var arm = model.GetNode<Node3D>("Body/RightArm");
                    Vector3 handStroke = model.Basis * new Vector3(0, 0, arm.Position.Z);
                    synchronized &= handStroke.DistanceTo(delta) < .001f;
                }
            }
            Check(synchronized, "both opposed sawyers follow the single blade stroke and reset to its rest position");
        }
        finally { runtime.Free(); }
    }

    static float DetailBoxDistance(Vector2 p, Vector2 center, float yaw, float width, float length)
    {
        var basis = new Basis(Vector3.Up, yaw);
        Vector3 local = basis.Inverse() * new Vector3(p.X - center.X, 0, p.Y - center.Y);
        return new Vector2(MathF.Max(0, MathF.Abs(local.X) - width * .5f),
            MathF.Max(0, MathF.Abs(local.Z) - length * .5f)).Length();
    }
}
