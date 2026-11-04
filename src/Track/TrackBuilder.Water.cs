using System;
using System.Collections.Generic;
using Godot;

namespace CCCPRacing.Track;

/// <summary>
/// Ponds and rivers (TrackDef.Water). All the water lies at one level: the road's height where
/// the river crosses it, the ford. Inside the water the ground becomes its bed: deep in the
/// middle, shelving up to the banks; on the road the bed follows the ford's two lines: the deep,
/// short line on the inside of the bend and a gravel bar a hand deep round the outside, longer but
/// faster - the choice the ford is about. Banks lower than
/// the water are raised a little above it, so the water never spills over the meadow. The tiles
/// under the water are Water, or Shallow where the bed is less than ShallowDepth down.
/// </summary>
public static partial class TrackBuilder
{
    const float ShallowDepth = 0.22f, BankWidth = 3f, BankRise = 0.15f, ShelfWidth = 2.5f;

    /// <summary>After the road and the pads, before the tile surfaces: the ford, the level, the beds and the banks.</summary>
    /// <summary>CarveWater in slices: every item is a point where a frame may go by (a loading card keeps running).</summary>
    static IEnumerable<int> CarveWaterSteps(TrackDef def, TrackData t, CenterGrid grid, float[] roadH)
    {
        var slice = System.Diagnostics.Stopwatch.StartNew();
        var w = def.Water;
        if (w == null || (w.Rivers.Count == 0 && w.Ponds.Count == 0)) yield break;
        if (w.Frozen)
        {
            foreach (var step in CarveFrozenSteps(def, t, grid, roadH)) { yield return step; slice.Restart(); }
            foreach (var step in EaseBanksAtEdgeSteps(def, t, grid)) { yield return step; slice.Restart(); }
            yield break;
        }
        int cn = t.Center.Length, nv = t.N + 1;

        // The ford: the centre-line sample nearest to a river. Without one the water lies at the
        // lowest ground under it.
        int ford = -1;
        float best = float.MaxValue;
        foreach (var r in w.Rivers)
            for (int i = 0; i < cn; i++)
            {
                float d = r.EdgeDistance(t.Center[i]);
                if (d < 0f && d < best) { best = d; ford = i; }
            }
        float level = ford >= 0 ? roadH[ford] : float.MaxValue;
        if (ford < 0)
            for (int iz = 0; iz < nv; iz++)
            {
            if (slice.ElapsedMilliseconds > 10) { yield return 0; slice.Restart(); }
            for (int ix = 0; ix < nv; ix++)
                if (WaterDistance(w, new Vector2(ix * t.Tile, iz * t.Tile), out _) < 0) level = Math.Min(level, t.Heights[iz * nv + ix]);
            }
        t.WaterLevel = level;
        t.FordIndex = ford;

        // The ford's bar: the outside of the road's bend there (+1: right of the driving direction).
        float bar = 1f;
        if (ford >= 0)
        {
            Vector2 a = t.CenterDir((ford - 8 + cn) % cn), b = t.CenterDir((ford + 8) % cn);
            bar = a.Cross(b) >= 0 ? -1f : 1f;      // z points down: a positive cross turns right, so the outside is left
        }

        for (int iz = 0; iz < nv; iz++)
        {
        if (slice.ElapsedMilliseconds > 10) { yield return 0; slice.Restart(); }
        for (int ix = 0; ix < nv; ix++)
        {
            var p = new Vector2(ix * t.Tile, iz * t.Tile);
            float wd = WaterDistance(w, p, out float full);       // < 0 inside the water
            ref float h = ref t.Heights[iz * nv + ix];
            if (wd >= BankWidth) continue;
            int ci = grid.Nearest(p, 20f, out float rd);
            if (wd >= 0f)
            {
                // Banks lower than the water rise a little above it (not the road: no lip at the ford).
                if (ci < 0 || rd > t.HalfWidth[ci] + 1f) h = Math.Max(h, level + BankRise * (1f - wd / BankWidth));
                continue;
            }
            float depth = full * SmoothStep(0f, ShelfWidth, -wd);
            if (ford >= 0 && ci >= 0 && Math.Abs(ci - ford) < 40 && rd < t.HalfWidth[ci] + 2f)
            {
                Vector2 dir = t.CenterDir(ci), right = new(-dir.Y, dir.X);
                float lateral = (p - t.Center[ci]).Dot(right) * bar;   // > 0 towards the bar (the outside of the bend)
                float k = SmoothStep(-1.5f, 1.5f, lateral);
                depth = Mathf.Lerp(w.FordDeep, w.FordShallow, k);
            }
            h = level - depth;
        }
        }

        // The centre line follows its new ground through the ford.
        for (int i = 0; i < cn; i++)
            if (WaterDistance(w, t.Center[i], out _) < 0f) roadH[i] = t.MeshHeight(t.Center[i].X, t.Center[i].Y);
        yield return 0; slice.Restart();
        foreach (var step in EaseBanksAtEdgeSteps(def, t, grid)) { yield return step; slice.Restart(); }
    }

    const float EdgeEase = 44f, EaseBankReach = 32f;

    /// <summary>
    /// Where water runs to the edge of the map the beyond (WorldBackdrop) continues it in a wide, gentle bed, so
    /// the steep banks and the wall at the edge would show as a step. Within EdgeEase of the edge the ground
    /// round the water is relaxed (blurred, the water and the road kept) towards a soft valley.
    /// </summary>
    static IEnumerable<int> EaseBanksAtEdgeSteps(TrackDef def, TrackData t, CenterGrid grid)
    {
        var slice = System.Diagnostics.Stopwatch.StartNew();
        var w = def.Water;
        int nv = t.N + 1;
        float world = t.WorldSize;
        var weight = new float[nv * nv];
        var floor = new float[nv * nv];
        bool any = false;
        for (int iz = 0; iz < nv; iz++)
        {
        if (slice.ElapsedMilliseconds > 10) { yield return 0; slice.Restart(); }
        for (int ix = 0; ix < nv; ix++)
        {
            var p = new Vector2(ix * t.Tile, iz * t.Tile);
            float e = MathF.Min(MathF.Min(p.X, p.Y), MathF.Min(world - p.X, world - p.Y));
            if (e >= EdgeEase) continue;
            float wd = WaterDistance(w, p, out _);
            if (wd < 0f || wd >= EaseBankReach) continue;
            int ci = grid.Nearest(p, 20f, out float rd);
            if (ci >= 0 && rd < t.HalfWidth[ci] + 6f) continue;
            weight[iz * nv + ix] = (1f - SmoothStep(0f, EdgeEase, e)) * (1f - SmoothStep(EaseBankReach * 0.45f, EaseBankReach, wd));
            any = true;
        }
        }
        if (!any) yield break;
        // A bank never sinks to the water: the highest ground of the water beside it, and a little more.
        for (int iz = 0; iz < nv; iz++)
        {
        if (slice.ElapsedMilliseconds > 10) { yield return 0; slice.Restart(); }
        for (int ix = 0; ix < nv; ix++)
        {
            if (weight[iz * nv + ix] <= 0f) continue;
            float f = float.IsNaN(t.WaterLevel) ? float.MinValue : t.WaterLevel;
            for (int dz = -3; dz <= 3; dz++)
            for (int dx = -3; dx <= 3; dx++)
            {
                int jx = ix + dx, jz = iz + dz;
                if (jx < 0 || jz < 0 || jx >= nv || jz >= nv) continue;
                if (WaterDistance(w, new Vector2(jx * t.Tile, jz * t.Tile), out _) < 0f) f = MathF.Max(f, t.Heights[jz * nv + jx]);
            }
            floor[iz * nv + ix] = f + 0.12f;
        }
        }
        for (int pass = 0; pass < 14; pass++)
        {
            var next = (float[])t.Heights.Clone();
            for (int iz = 0; iz < nv; iz++)
            {
            if (slice.ElapsedMilliseconds > 10) { yield return 0; slice.Restart(); }
            for (int ix = 0; ix < nv; ix++)
            {
                float k = weight[iz * nv + ix];
                if (k <= 0f) continue;
                float sum = 0; int cnt = 0;
                for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int jx = ix + dx, jz = iz + dz;
                    if (jx < 0 || jz < 0 || jx >= nv || jz >= nv) continue;
                    sum += t.Heights[jz * nv + jx]; cnt++;
                }
                float blurred = Mathf.Lerp(t.Heights[iz * nv + ix], sum / cnt, k);
                next[iz * nv + ix] = MathF.Max(blurred, floor[iz * nv + ix]);
            }
            }
            t.Heights = next;
        }
    }

    const float IceSink = 0.6f, IceBank = 4f;

    static List<Vector2> FrozenRiverPoints(RiverDef river)
    {
        var points = new List<Vector2>();
        for (int i = 0; i + 1 < river.Points.Length; i++)
        {
            var a = new Vector2(river.Points[i][0], river.Points[i][1]);
            var b = new Vector2(river.Points[i + 1][0], river.Points[i + 1][1]);
            int steps = Math.Max(1, (int)MathF.Ceiling(a.DistanceTo(b) / 2f));
            for (int k = 0; k < steps; k++) points.Add(a.Lerp(b, k / (float)steps));
        }
        points.Add(new Vector2(river.Points[^1][0], river.Points[^1][1]));
        return points;
    }

    // Shared by road grading and the ice mesh: the driving surface and the river beside it
    // must have the same continuously interpolated, downstream-falling height.
    static float[] ValleyIce(TrackDef def, TrackData t, List<Vector2> points)
    {
        var ice = new float[points.Count];
        for (int k = 0; k < ice.Length; k++)
        {
            ice[k] = NaturalHeight(def, points[k].X, points[k].Y, t.WorldSize);
            if (k > 0) ice[k] = Math.Min(ice[k], ice[k - 1]);
        }
        for (int pass = 0; pass < 2; pass++)
        {
            var src = ice;
            ice = new float[src.Length];
            for (int k = 0; k < src.Length; k++)
            {
                int lo = Math.Max(0, k - 3), hi = Math.Min(src.Length - 1, k + 3);
                for (int j = lo; j <= hi; j++) ice[k] += src[j];
                ice[k] /= hi - lo + 1;
            }
        }
        return ice;
    }

    static float ValleyIceAt(Vector2 p, List<Vector2> points, float[] ice)
    {
        float best = float.MaxValue, height = ice[0];
        for (int k = 0; k + 1 < points.Count; k++)
        {
            Vector2 ab = points[k + 1] - points[k];
            float u = Math.Clamp((p - points[k]).Dot(ab) / Math.Max(0.0001f, ab.LengthSquared()), 0f, 1f);
            float distance = p.DistanceSquaredTo(points[k] + ab * u);
            if (distance >= best) continue;
            best = distance;
            height = Mathf.Lerp(ice[k], ice[k + 1], u);
        }
        return height;
    }

    static void GradeFrozenValleyRoad(TrackDef def, TrackData t, float[] roadH)
    {
        if (def.Water is not { Frozen: true, ValleyDepth: > 0 }) return;
        int n = roadH.Length;
        const int Ease = 55;
        foreach (var river in def.Water.Rivers)
        {
            var points = FrozenRiverPoints(river);
            var ice = ValleyIce(def, t, points);
            var inside = new bool[n];
            var delta = new float[n];
            for (int i = 0; i < n; i++)
            {
                inside[i] = river.EdgeDistance(t.Center[i]) < 0;
                delta[i] = ValleyIceAt(t.Center[i], points, ice) - roadH[i];
            }
            for (int i = 0; i < n; i++)
            {
                if (inside[i]) { roadH[i] += delta[i]; continue; }
                // Ease along the road, not across the river: the nearby camp road stays on its bank.
                for (int k = 1; k <= Ease; k++)
                {
                    int before = (i - k + n) % n, after = (i + k) % n;
                    if (!inside[before] && !inside[after]) continue;
                    int edge = inside[before] ? before : after;
                    roadH[i] += delta[edge] * (1f - SmoothStep(0, Ease, k));
                    break;
                }
            }
        }
    }

    /// <summary>
    /// A frozen river: the ice lies IceSink below the ground along it and only ever falls
    /// downstream (the points run from the source), smoothed; where the road runs on the river or
    /// crosses it, the road's own height is the ice's. Inside, the ground is the flat ice; beside
    /// it the banks rise at least a little above it (not under the road). The centre line samples
    /// on it are ice.
    /// </summary>
    static IEnumerable<int> CarveFrozenSteps(TrackDef def, TrackData t, CenterGrid grid, float[] roadH)
    {
        var slice = System.Diagnostics.Stopwatch.StartNew();
        int nv = t.N + 1, cn = t.Center.Length;
        foreach (var r in def.Water.Rivers)
        {
            // The river's line every 2 m and the ice height along it.
            var pts = new List<Vector2>();
            for (int k = 0; k + 1 < r.Points.Length; k++)
            {
                var a = new Vector2(r.Points[k][0], r.Points[k][1]);
                var b = new Vector2(r.Points[k + 1][0], r.Points[k + 1][1]);
                int steps = Math.Max(1, (int)(a.DistanceTo(b) / 2f));
                for (int s = 0; s < steps; s++) pts.Add(a.Lerp(b, s / (float)steps));
            }
            pts.Add(new Vector2(r.Points[^1][0], r.Points[^1][1]));
            var ice = new float[pts.Count];
            for (int k = 0; k < pts.Count; k++)
            {
                int ci = grid.Nearest(pts[k], 30f, out float d);
                ice[k] = ci >= 0 && d < r.Width / 2f + 2f ? roadH[ci] : t.MeshHeight(pts[k].X, pts[k].Y) - IceSink;
            }
            for (int k = 1; k < ice.Length; k++) ice[k] = Math.Min(ice[k], ice[k - 1]);   // never uphill
            for (int pass = 0; pass < 2; pass++)
            {
                // A box blur that stops at the ends (BoxBlur wraps round, for the closed road).
                var src = ice;
                ice = new float[src.Length];
                for (int k = 0; k < src.Length; k++)
                {
                    float sum = 0;
                    int cnt = 0;
                    for (int m = Math.Max(0, k - 3); m <= Math.Min(src.Length - 1, k + 3); m++) { sum += src[m]; cnt++; }
                    ice[k] = sum / cnt;
                }
            }

            bool valley = def.Water.ValleyDepth > 0;
            if (valley)
            {
                pts = FrozenRiverPoints(r);
                ice = ValleyIce(def, t, pts);
            }

            float IceAt(Vector2 p, out float dist)
            {
                if (valley) { dist = 0; return ValleyIceAt(p, pts, ice); }
                int best = 0;
                float bd = float.MaxValue;
                for (int k = 0; k < pts.Count; k++) { float d = pts[k].DistanceSquaredTo(p); if (d < bd) { bd = d; best = k; } }
                dist = MathF.Sqrt(bd);
                return ice[best];
            }

            for (int iz = 0; iz < nv; iz++)
            {
            if (slice.ElapsedMilliseconds > 10) { yield return 0; slice.Restart(); }
            for (int ix = 0; ix < nv; ix++)
            {
                var p = new Vector2(ix * t.Tile, iz * t.Tile);
                float wd = r.EdgeDistance(p);
                float bank = valley ? Math.Max(1f, def.Water.ValleyBankWidth) : IceBank;
                if (wd >= bank) continue;
                float level = IceAt(p, out _);
                ref float h = ref t.Heights[iz * nv + ix];
                int ci = grid.Nearest(p, 20f, out float rd);
                bool road = ci >= 0 && rd < t.HalfWidth[ci] + 1f;
                if (wd < 0f) h = road ? roadH[ci] : level;
                else if (!road)
                    h = valley ? Mathf.Lerp(level, h, SmoothStep(0, bank, wd))
                        : Math.Max(h, level + IceSink * (1f - SmoothStep(0f, IceBank, wd)));
            }
            }
            for (int i = 0; i < cn; i++)
                if (r.EdgeDistance(t.Center[i]) < 0f) t.CenterSurf[i] = SurfaceType.Ice;
        }
    }

    /// <summary>After the tile surfaces: water over everything under the level, Shallow where the bed is near it.</summary>
    static IEnumerable<int> WaterSurfacesSteps(TrackDef def, TrackData t)
    {
        var slice = System.Diagnostics.Stopwatch.StartNew();
        if (def.Water is { Frozen: true })
        {
            // Frozen: every tile inside a river is ice.
            for (int iz = 0; iz < t.N; iz++)
            {
            if (slice.ElapsedMilliseconds > 10) { yield return 0; slice.Restart(); }
            for (int ix = 0; ix < t.N; ix++)
            {
                var p = new Vector2((ix + 0.5f) * t.Tile, (iz + 0.5f) * t.Tile);
                if (WaterDistance(def.Water, p, out _) < 0f) t.Surf[iz * t.N + ix] = SurfaceType.Ice;
            }
            }
            yield break;
        }
        if (float.IsNaN(t.WaterLevel)) yield break;
        int n = t.N;
        for (int iz = 0; iz < n; iz++)
        {
        if (slice.ElapsedMilliseconds > 10) { yield return 0; slice.Restart(); }
        for (int ix = 0; ix < n; ix++)
        {
            var p = new Vector2((ix + 0.5f) * t.Tile, (iz + 0.5f) * t.Tile);
            if (WaterDistance(def.Water, p, out _) >= 0f) continue;
            float depth = t.WaterLevel - t.MeshHeight(p.X, p.Y);
            t.Surf[iz * n + ix] = depth < ShallowDepth ? SurfaceType.Shallow : SurfaceType.Water;
        }
        }
    }

    /// <summary>Water (or the ford's bar) within r metres: no trees or bushes there.</summary>
    static bool NearWater(TrackData t, Vector2 p, float r)
    {
        if (float.IsNaN(t.WaterLevel) && !t.Winter) return false;
        for (float dz = -r; dz <= r; dz += t.Tile)
        for (float dx = -r; dx <= r; dx += t.Tile)
            if (t.TileSurface((int)((p.X + dx) / t.Tile), (int)((p.Y + dz) / t.Tile)) is SurfaceType.Water or SurfaceType.Shallow or SurfaceType.Ice) return true;
        return false;
    }

    /// <summary>Signed distance to the nearest water edge (negative inside) and that water's full depth.</summary>
    static float WaterDistance(WaterDef w, Vector2 p, out float depth)
    {
        float best = float.MaxValue;
        depth = 0f;
        foreach (var r in w.Rivers)
        {
            float d = r.EdgeDistance(p);
            if (d < best) { best = d; depth = r.Depth; }
        }
        foreach (var o in w.Ponds)
        {
            float d = p.DistanceTo(new Vector2(o.X, o.Z)) - o.R;
            if (d < best) { best = d; depth = o.Depth; }
        }
        return best;
    }
}
