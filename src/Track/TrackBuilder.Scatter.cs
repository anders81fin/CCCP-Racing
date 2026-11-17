using System;
using System.Collections.Generic;
using Godot;

namespace CCCPRacing.Track;

public static partial class TrackBuilder
{
    /// <summary>
    /// TrackDef.Scatter: loose rocks, boulders, stumps and extra trees over the open grass. Runs after all other props (their indices stay as they were), with a generator of its own per entry.
    /// A spot must be grass, off the road by RoadGap, clear of water, pads and other props; the clump noise leaves calm stretches between the groups.
    /// </summary>
    static IEnumerable<int> ScatterExtraSteps(TrackDef def, TrackData t, CenterGrid grid, List<Vector2> occupied)
    {
        var slice = System.Diagnostics.Stopwatch.StartNew();
        int entry = 0;
        foreach (var sc in def.Scatter)
        {
            var rng = new RandomNumberGenerator { Seed = (ulong)(def.Seed * 7919 + 4242 + sc.Seed * 131 + entry++) };
            int placed = 0;
            for (int tries = 0; placed < sc.Count && tries < sc.Count * 60; tries++)
            {
                if (slice.ElapsedMilliseconds > 10) { yield return 0; slice.Restart(); }
                var p = new Vector2(rng.RandfRange(14f, t.WorldSize - 14f), rng.RandfRange(14f, t.WorldSize - 14f));
                if (sc.Clump > 0f && Noise.Value(p.X / sc.ClumpScale, p.Y / sc.ClumpScale, def.Seed + 311 + sc.Seed) < rng.RandfRange(-0.3f, 0.3f) + sc.Clump) continue;
                if (!InsideMap(t, p, 14f) || InPad(t, p, 3f) || NearWater(t, p, 2.5f)) continue;
                if (t.TileSurface((int)(p.X / t.Tile), (int)(p.Y / t.Tile)) is not (SurfaceType.Grass or SurfaceType.DeadGrass)) continue;
                int ci = grid.Nearest(p, 40f, out float d);
                if (ci >= 0 && d < t.HalfWidth[ci] + sc.RoadGap) continue;
                if (!FreeSpot(p, occupied, sc.PropGap)) continue;
                if (OnEarthNearby(t, p, 1.6f)) continue;      // not on or at the edge of a path, a yard or a road shoulder
                if (t.Id == "ilyich" && SlurryTractor.NearRoute(p, 6f)) continue;      // nothing grows on the slurry tractor's circuit
                AddProp(t, sc.Type, p, rng.RandfRange(0, MathF.Tau), null, occupied, rng.RandfRange(sc.MinSize, sc.MaxSize));
                placed++;
            }
        }
    }

    /// <summary>
    /// TrackDef.Belts: windbreak belts (лесополоса) along field edges: two staggered rows of mixed birch, pine and spruce on a line, trees skipped where the road, water, a pad or another prop is
    /// in the way, so a belt has the gaps a real one has. Placed after everything else, like the scatter.
    /// </summary>
    static IEnumerable<int> PlaceBeltsSteps(TrackDef def, TrackData t, CenterGrid grid, List<Vector2> occupied)
    {
        var slice = System.Diagnostics.Stopwatch.StartNew();
        int entry = 0;
        foreach (var b in def.Belts)
        {
            var rng = new RandomNumberGenerator { Seed = (ulong)(def.Seed * 6007 + 31337 + entry++) };
            var a = new Vector2(b.X0, b.Z0); var e = new Vector2(b.X1, b.Z1); var dir = (e - a).Normalized(); var n = new Vector2(-dir.Y, dir.X);
            float len = a.DistanceTo(e);
            for (int row = 0; row < b.Rows; row++)
                for (float d = (row % 2) * b.Spacing * 0.5f; d <= len; d += b.Spacing)
                {
                    if (slice.ElapsedMilliseconds > 10) { yield return 0; slice.Restart(); }
                    var p = a + dir * (d + rng.RandfRange(-0.45f, 0.45f)) + n * ((row - (b.Rows - 1) * 0.5f) * b.RowGap + rng.RandfRange(-0.3f, 0.3f));
                    if (!InsideMap(t, p, 14f) || InPad(t, p, 2f) || NearWater(t, p, 3f)) continue;
                    if (t.TileSurface((int)(p.X / t.Tile), (int)(p.Y / t.Tile)) is not (SurfaceType.Grass or SurfaceType.DeadGrass)) continue;
                    int ci = grid.Nearest(p, 40f, out float dist);
                    if (ci >= 0 && dist < t.HalfWidth[ci] + b.Gap) continue;
                    if (!FreeSpot(p, occupied, 2.3f)) continue;
                    if (OnEarthNearby(t, p, 1.6f)) continue;
                    if (t.Id == "ilyich" && SlurryTractor.NearRoute(p, 6f)) continue;
                    AddProp(t, b.Kinds[rng.RandiRange(0, b.Kinds.Count - 1)], p, rng.RandfRange(0, MathF.Tau), null, occupied, rng.RandfRange(b.MinSize, b.MaxSize));
                }
        }
    }

    /// <summary>True if the ground at p or within r metres of it is bare earth, gravel, mud or concrete (a path, a yard, a road shoulder): scattered things keep off such ground.</summary>
    static bool OnEarthNearby(TrackData t, Vector2 p, float r)
    {
        foreach (var o in new[] { Vector2.Zero, new Vector2(r, 0), new Vector2(-r, 0), new Vector2(0, r), new Vector2(0, -r) })
        {
            var q = p + o;
            if (t.TileSurface((int)(q.X / t.Tile), (int)(q.Y / t.Tile)) is SurfaceType.Dirt or SurfaceType.Gravel or SurfaceType.Mud or SurfaceType.Concrete or SurfaceType.Field) return true;
        }
        return false;
    }
}
