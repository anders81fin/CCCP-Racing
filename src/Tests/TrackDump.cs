using System;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

/// <summary>Prints the centre line every 20 m: position, surface, road height and curvature.
/// Run: godot-mono --headless --path . res://scenes/tests/TrackDump.tscn</summary>
public partial class TrackDump : Node
{
    /// <summary>Counts potholes/puddles on the road and how close together they sit.</summary>
    static void HazardStats(TrackData t)
    {
        foreach (var type in new[] { SurfaceType.Pothole, SurfaceType.Puddle })
        {
            var pts = new System.Collections.Generic.List<Vector2>();
            for (int iz = 0; iz < t.N; iz++)
            for (int ix = 0; ix < t.N; ix++)
            {
                if (t.Surf[iz * t.N + ix] != type) continue;
                var p = new Vector2((ix + 0.5f) * t.Tile, (iz + 0.5f) * t.Tile);
                t.NearestCenter(p, out float d);
                if (d < 6f) pts.Add(p);
            }
            var nn = new System.Collections.Generic.List<float>();
            foreach (var p in pts)
            {
                float best = float.MaxValue;
                foreach (var q in pts) if (q != p) best = Math.Min(best, p.DistanceTo(q));
                nn.Add(best);
            }
            nn.Sort();
            int touching = nn.FindAll(x => x < 2.5f).Count;
            GD.Print($"{type}: {pts.Count} on the road, nearest-neighbour min {(nn.Count > 0 ? nn[0] : 0):F1} m, " +
                     $"median {(nn.Count > 0 ? nn[nn.Count / 2] : 0):F1} m, {touching} touching another");
        }
    }

    /// <summary>Terrain extremes, steepest road, and how level the ground is under the buildings.</summary>
    static void TerrainStats(TrackData t)
    {
        int nv = t.N + 1;
        float lo = float.MaxValue, hi = float.MinValue, loIn = float.MaxValue, hiIn = float.MinValue;
        Vector2 loP = default, hiP = default, loInP = default, hiInP = default;
        for (int iz = 0; iz < nv; iz++)
        for (int ix = 0; ix < nv; ix++)
        {
            float h = t.Heights[iz * nv + ix];
            var p = new Vector2(ix * t.Tile, iz * t.Tile);
            if (h < lo) { lo = h; loP = p; }
            if (h > hi) { hi = h; hiP = p; }
            // Inside the playable area (the edge wall of hills excluded).
            if (p.X < 34 || p.Y < 34 || p.X > t.WorldSize - 34 || p.Y > t.WorldSize - 34) continue;
            if (h < loIn) { loIn = h; loInP = p; }
            if (h > hiIn) { hiIn = h; hiInP = p; }
        }
        GD.Print($"terrain min {lo:F1} m at ({loP.X:F0},{loP.Y:F0}), max {hi:F1} m at ({hiP.X:F0},{hiP.Y:F0})");
        GD.Print($"inside 34 m of the edge: min {loIn:F1} m at ({loInP.X:F0},{loInP.Y:F0}), max {hiIn:F1} m at ({hiInP.X:F0},{hiInP.Y:F0})");
        int n = t.Center.Length;
        float rlo = float.MaxValue, rhi = float.MinValue; int rloI = 0, rhiI = 0;
        float g10 = 0; int g10I = 0;
        for (int i = 0; i < n; i++)
        {
            if (t.CenterH[i] < rlo) { rlo = t.CenterH[i]; rloI = i; }
            if (t.CenterH[i] > rhi) { rhi = t.CenterH[i]; rhiI = i; }
            float g = Math.Abs(t.CenterH[(i + 10) % n] - t.CenterH[i]) / 10f;
            if (g > g10) { g10 = g; g10I = i; }
        }
        GD.Print($"road height {rlo:F1} m (at {rloI} m) .. {rhi:F1} m (at {rhiI} m); steepest 10 m grade {g10 * 100:F1} % at {g10I} m");
        foreach (var pr in t.Props)
        {
            float r = pr.Type switch { "hangar" => 11f, "barracks" => 15f, "barn" or "ruinbarn" => 6.5f, "bigbarn" => 9f, "dormitory" => 4f, "radar" => 4.5f, "silo" => 2.6f, "statue" => 1.7f, _ => 0f };
            if (r == 0) continue;
            float mn = float.MaxValue, mx = float.MinValue;
            for (float a = 0; a < MathF.Tau; a += 0.3f)
            for (float rr = 0; rr <= r; rr += r / 3f)
            {
                float h = t.MeshHeight(pr.Position.X + MathF.Cos(a) * rr, pr.Position.Z + MathF.Sin(a) * rr) - pr.Position.Y;
                mn = Math.Min(mn, h); mx = Math.Max(mx, h);
            }
            GD.Print($"{pr.Type,-9} ({pr.Position.X:F0},{pr.Position.Z:F0}) base {pr.Position.Y:F1} m: ground within {r} m from {mn:+0.00;-0.00} to {mx:+0.00;-0.00} m");
        }
    }

    public override void _Ready()
    {
        string id = OS.GetEnvironment("TRACK");      // TRACK=id: another track
        var t = TrackBuilder.Build(TrackDef.Load($"res://tracks/{(id.Length > 0 ? id : "bonus")}.json"));
        int n = t.Center.Length;
        // DUMP_STEP=2 DUMP_FROM=200 DUMP_TO=360: a finer look at one stretch.
        int step = int.TryParse(System.Environment.GetEnvironmentVariable("DUMP_STEP"), out int st) ? st : 20;
        int from = int.TryParse(System.Environment.GetEnvironmentVariable("DUMP_FROM"), out int fr) ? fr : 0;
        int to = int.TryParse(System.Environment.GetEnvironmentVariable("DUMP_TO"), out int tt) ? Math.Min(tt, n) : n;
        for (int i = from; i < to; i += step)
        {
            float turn = Mathf.RadToDeg(t.CenterDir(i).AngleTo(t.CenterDir((i + 20) % n)));
            if (step < 20) { GD.Print($"{i,5} m  ({t.Center[i].X,5:F0},{t.Center[i].Y,5:F0})  {t.CenterSurf[i],-9} h={t.CenterH[i],6:F2}  ground L/R {t.MeshHeight(t.Center[i].X - t.CenterDir(i).Y * 8, t.Center[i].Y + t.CenterDir(i).X * 8) - t.CenterH[i],6:F2} {t.MeshHeight(t.Center[i].X + t.CenterDir(i).Y * 8, t.Center[i].Y - t.CenterDir(i).X * 8) - t.CenterH[i],6:F2}  turn/20m={turn,6:F1}°"); continue; }
            GD.Print($"{i,5} m  ({t.Center[i].X,5:F0},{t.Center[i].Y,5:F0})  {t.CenterSurf[i],-9} h={t.CenterH[i],6:F2}  turn/20m={turn,6:F1}°");
        }
        HazardStats(t);
        foreach (var f in t.Fences)
            GD.Print($"fence {f.Type}: {f.Points.Count} posts from ({f.Points[0].X:F0},{f.Points[0].Z:F0}) to ({f.Points[^1].X:F0},{f.Points[^1].Z:F0})");
        TerrainStats(t);
        int Count(string type) => t.Props.FindAll(p => p.Type == type).Count;
        GD.Print($"trees: birch {Count("birch")}, spruce {Count("spruce")}, pine {Count("pine")}, dead {Count("deadtree")}; " +
                 $"power line: {Count("pole")} poles, {t.PowerSpans.Count} wired spans");
        GD.Print($"signature {TrackSignature.Compute(t)}");
        GetTree().Quit();
    }
}
