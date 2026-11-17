using System;
using CCCPRacing.Car;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    // --tractor-hedge-test: neither the ploughing tractor nor the slurry tractor (its circuit or its revenge) ever comes within a body's reach of the dacha's hedge or stone walls (their visible lines, not just their collision circles).
    void TractorHedgeTest()
    {
        var t = TrackBuilder.Build(TrackDef.Load("res://tracks/ilyich.json"));
        var fx = new TyreEffects(); AddChild(fx); fx.Init(t);
        var lines = new System.Collections.Generic.List<(Vector2 a, Vector2 b, string what)>();
        foreach (var run in PartyDacha.Boundary) for (int i = 0; i + 1 < run.Length; i++) lines.Add((run[i], run[i + 1], "hedge"));
        foreach (var f in t.Fences) if (f.Type == "stone") for (int i = 0; i + 1 < f.Points.Count; i++) lines.Add((new Vector2(f.Points[i].X, f.Points[i].Z), new Vector2(f.Points[i + 1].X, f.Points[i + 1].Z), "stone wall"));
        float Nearest(Vector2 p, out string what) { float best = 1e9f; what = ""; foreach (var (a, b, w) in lines) { float d = Geometry2D.GetClosestPointToSegment(p, a, b).DistanceTo(p); if (d < best) { best = d; what = w; } } return best; }
        // 1) the ploughing tractor
        var plough = new DrunkTractor(); AddChild(plough); plough.Init(t, fx, new Vector2(142, 36), MathF.PI);
        float minPlough = 1e9f; string whatP = ""; Vector2 at = default;
        for (int i = 0; i < 60 * 60 * 20; i++)
        {
            plough.Tick(1f / 60f); if (!plough.HasDeparted) continue;
            foreach (var q in new[] { t.Obstacles[plough.BodyObstacle].Center, t.Obstacles[plough.PloughObstacle].Center })
            { float d = Nearest(q, out var w); if (d < minPlough) { minPlough = d; whatP = w; at = q; } }
        }
        GD.Print($"TRACTOR vs HEDGE: the ploughing tractor's closest approach to a {whatP} {minPlough:F2} m at {at}");
        Check(minPlough > 1.3f + 0.4f, "the ploughing tractor never reaches the hedge or the stone walls");
        // 2) the slurry tractor: circuit, then revenge
        var slurry = new SlurryTractor(); AddChild(slurry); slurry.Init(t);
        float minSlurry = 1e9f; string whatS = ""; Vector2 atS = default; int first = t.Obstacles.Count - 2;
        void Watch(string phase)
        {
            for (int slot = first; slot < t.Obstacles.Count; slot++)
            { float d = Nearest(t.Obstacles[slot].Center, out var w); if (d < minSlurry) { minSlurry = d; whatS = $"{w} ({phase})"; atS = t.Obstacles[slot].Center; } }
        }
        for (int i = 0; i < 6000; i++) { slurry.Tick(0.1f); Watch("circuit"); }
        slurry.StartRevenge();
        for (int i = 0; i < 12000; i++) { slurry.Tick(0.1f); Watch("revenge"); }
        GD.Print($"TRACTOR vs HEDGE: the slurry tractor's closest approach to a {whatS} {minSlurry:F2} m at {atS}");
        Check(minSlurry > 1.5f + 0.4f, "the slurry tractor and its tanker never reach the hedge or the stone walls, on the circuit or in the revenge");
    }

    // --slurry-depart-test: the slurry tractor and its tanker, from the barn across the country to its circuit (and back after a revenge), never touch a fixed obstacle, water or the dacha's hedge.
    void SlurryDepartureTest()
    {
        var t = TrackBuilder.Build(TrackDef.Load("res://tracks/ilyich.json"));
        var fx = new TyreEffects(); AddChild(fx); fx.Init(t);
        var slurry = new SlurryTractor(); AddChild(slurry); slurry.Init(t);
        var doors = new BarnDoors(5, 5, 5, Colors.Brown, Colors.Gray); AddChild(doors);
        var barn = t.Props.Find(p => p.Type == "bigbarn");
        slurry.ConfigureDeparture(doors, new Vector2(barn.Position.X, barn.Position.Z));
        int staticCount = t.Obstacles.Count - 2, first = staticCount;
        var lines = new System.Collections.Generic.List<(Vector2 a, Vector2 b)>();
        foreach (var run in PartyDacha.Boundary) for (int i = 0; i + 1 < run.Length; i++) lines.Add((run[i], run[i + 1]));
        int hits = 0; float worst = 1e9f; Vector2 where = default; string what = ""; bool wet = false;
        void Watch(string phase)
        {
            for (int slot = first; slot < t.Obstacles.Count; slot++)
            {
                var m = t.Obstacles[slot]; if (m.Center.X < -9000f) continue;
                bool wetNow = t.Sample(m.Center.X, m.Center.Y).Surface is SurfaceType.Water or SurfaceType.Shallow; if (wetNow && !wet && !phase.StartsWith("to the road")) GD.Print($"  wet first at {m.Center} ({phase})"); wet |= wetNow && !phase.StartsWith("to the road");      // (the revenge follows the road through the ford)
                if (phase == "departure" && m.Center.DistanceTo(new Vector2(barn.Position.X, barn.Position.Z)) < 24f) continue;      // (leaving the barn itself)
                for (int j = 0; j < staticCount; j++)
                {
                    float gap = m.Center.DistanceTo(t.Obstacles[j].Center) - m.Radius - t.Obstacles[j].Radius;
                    if (gap < worst) { worst = gap; where = m.Center; what = $"obstacle {t.Obstacles[j].Center} r={t.Obstacles[j].Radius:F2} ({phase})"; }
                    if (gap < 0.1f) hits++;
                }
                foreach (var (a, b) in lines) { float d = Geometry2D.GetClosestPointToSegment(m.Center, a, b).DistanceTo(m.Center) - m.Radius - 0.56f; if (d < worst) { worst = d; where = m.Center; what = $"hedge ({phase})"; } if (d < 0.1f) hits++; }
            }
        }
        for (int i = 0; i < 12000 && !slurry.HasDeparted; i++) { slurry.Tick(0.1f); Watch("departure"); }
        Check(slurry.HasDeparted, "the slurry tractor leaves its barn and reaches its circuit");
        for (int i = 0; i < 2000; i++) { slurry.Tick(0.1f); Watch("circuit"); }
        slurry.StartRevenge();
        for (int i = 0; i < 6000; i++) { slurry.Tick(0.1f); Watch("to the road / revenge"); }
        for (int k = 0; k < 3; k++) slurry.RevengeLapDone();
        for (int i = 0; i < 12000 && !slurry.HasDeparted; i++) { slurry.Tick(0.1f); Watch("back to the fields"); }
        GD.Print($"SLURRY DEPARTURE: closest to anything fixed {worst:F2} m at {where}: {what}; overlapping steps {hits}; wet {wet}");
        Check(hits == 0 && !wet, "the slurry tractor never touches a tree, a building, the hedge or water on its way out, to the road and back");
    }

    // --slurry-path-test: no way, a gap too narrow for the tractor and its tanker, and a gap wide enough: the planner never lets the vehicle (body 1.3 m and tank 1.5 m circles, over every turn) touch a fixed obstacle; with no safe path it stands still instead of taking a straight line.
    void SlurryPathTest()
    {
        foreach (float gapWidth in new[] { -1f, 0f, 5.0f, 14.0f })
        {
            var t = TrackBuilder.Build(TrackDef.Load("res://tracks/ilyich.json"));
            var fx = new TyreEffects(); AddChild(fx); fx.Init(t);
            var slurry = new SlurryTractor(); AddChild(slurry); slurry.Init(t);
            for (int i = 0; i < 300; i++) slurry.Tick(0.1f);
            Vector2 c = slurry.Position2D; int k = t.NearestCenter(c, out _); Vector2 toRoad = (t.Center[k] - c).Normalized();
            { var open = SlurryTractor.Plan(t, c, t.Center[k], slurry.BodyObstacle, slurry.TankObstacle); if (open != null) foreach (var wp in open) if (wp.DistanceTo(c) > 14f) { toRoad = (wp - c).Normalized(); break; } }      // (the gap faces where the free route leaves)
            int staticCount = t.Obstacles.Count - 2;
            const float Ring = 14f;
            float step = 0.8f / Ring; int placed = 0;
            for (float a = 0; a < MathF.Tau; a += step)
            {
                Vector2 dir = new(MathF.Cos(a), MathF.Sin(a));
                if (gapWidth < 0f) break;      // (no ring at all)
                if (gapWidth > 0f && dir.Dot(toRoad) > 0f && MathF.Abs(dir.Cross(toRoad)) * Ring < gapWidth * 0.5f) continue;      // (the gap faces the road)
                t.Obstacles.Add(new Obstacle(c + dir * Ring, 0.5f)); placed++;
            }
            // The new obstacles sit after the tractor's own two slots: tell the check which are fixed.
            int first = staticCount; var fixedList = new System.Collections.Generic.List<int>();
            for (int j = 0; j < t.Obstacles.Count; j++) if (j != slurry.BodyObstacle && j != slurry.TankObstacle) fixedList.Add(j);
            { var dbg = SlurryTractor.Plan(t, c, t.Center[k], slurry.BodyObstacle, slurry.TankObstacle); GD.Print("  planner: " + (dbg == null ? "no path" : dbg.Count + " waypoints") + " from " + c + " to " + t.Center[k]); }
            slurry.StartRevenge();
            float worst = 1e9f, farthest = 0f;
            for (int i = 0; i < 4000; i++)
            {
                slurry.Tick(0.1f);
                farthest = MathF.Max(farthest, slurry.Position2D.DistanceTo(c));
                foreach (int slot in new[] { slurry.BodyObstacle, slurry.TankObstacle })
                {
                    var m = t.Obstacles[slot];
                    foreach (int j in fixedList) worst = MathF.Min(worst, m.Center.DistanceTo(t.Obstacles[j].Center) - m.Radius - t.Obstacles[j].Radius);
                }
            }
            bool escaped = farthest > Ring + 3f;
            GD.Print($"SLURRY PATH: gap {gapWidth:F1} m ({placed} ring posts): farthest from the start {farthest:F1} m, closest to a post {worst:F2} m, escaped {escaped}");
            Check(worst > 0.1f, $"slurry tractor, ring gap {gapWidth:F1} m: the body and the tanker never touch a post, in every turn");
            if (gapWidth < 0f) Check(slurry.Avenging && farthest > 8f, "slurry tractor, open country: the revenge reaches the road");
            else if (gapWidth < 6f) Check(!escaped, $"slurry tractor, ring gap {gapWidth:F1} m: no safe way, so it stands still (no straight line through the posts)");
            else Check(escaped, $"slurry tractor, ring gap {gapWidth:F1} m: wide enough, so it gets through");
            RemoveChild(slurry); slurry.Free(); RemoveChild(fx); fx.Free();
        }
    }
}
