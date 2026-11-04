using System;
using System.Collections.Generic;
using Godot;

namespace CCCPRacing.Track;

/// <summary>
/// The taiga of a winter track: dense conifer forest everywhere it can stand (TrackData.Taiga,
/// drawn by TaigaMesh). Scenery only, so it keeps where a car cannot get: right behind a snow bank,
/// or well away from the road where there is none (the gaps, the ice, the runway, the camp). It
/// keeps off the frozen river and its banks, the airbase and its apron, the camp, the props and
/// the map's very edge; a few clearings come from noise.
/// </summary>
public static partial class TrackBuilder
{
    const float TaigaSpacing = 4.6f;

    /// <summary>
    /// The log piles: lengthwise along the road at their metre, just behind the bank on their side
    /// (a car cannot reach them), or where there is no bank Out metres beyond the road's edge, then
    /// as obstacles. Their ground is kept free of trees.
    /// </summary>
    static void PlaceLogPiles(TrackDef def, TrackData t, List<Vector2> occupied)
    {
        int n = t.Center.Length;
        for (int k = 0; k < def.LogPiles.Count; k++)
        {
            var d = def.LogPiles[k];
            int i = ((int)d.At % n + n) % n;
            bool left = d.Side == "left";
            Vector2 dir = t.CenterDir(i), side = left ? new Vector2(dir.Y, -dir.X) : new Vector2(-dir.Y, dir.X);
            bool banked = t.Banks != null && t.Banks.Closed(i, left) && t.Banks.Taper(i, left) > 0.9f;
            float lat = banked ? t.Banks.Inner(i, left) + Snowbanks.Width + 1.4f + d.Out : t.HalfWidth[i] + 2.5f + d.Out;
            Vector2 p = t.Center[i] + side * lat;
            var pile = new LogPiles.Pile(new Vector3(p.X, t.MeshHeight(p.X, p.Y), p.Y), TrackData.YawFor(dir), def.Seed + 31 * k);
            t.LogPiles.Add(pile);
            for (float z = -2f; z <= 2f; z += 2f) occupied.Add(p + dir * z);
            if (!banked) t.Obstacles.AddRange(LogPiles.Obstacles(pile));
        }
    }

    static void PlaceTaiga(TrackDef def, TrackData t, CenterGrid grid, List<Vector2> occupied)
    {
        if (!t.Winter) return;
        var rng = new RandomNumberGenerator { Seed = (ulong)(def.Seed + 404) };
        int n = t.Center.Length;
        float world = t.WorldSize;
        for (float z = 6f; z < world - 6f; z += TaigaSpacing)
        for (float x = 6f; x < world - 6f; x += TaigaSpacing)
        {
            var p = new Vector2(x + rng.RandfRange(-1.8f, 1.8f), z + rng.RandfRange(-1.8f, 1.8f));
            float keep = rng.Randf();
            if (!InsideMap(t, p, 4f) || keep > 0.88f) continue;
            if (Noise.Value(p.X / 38f, p.Y / 38f, def.Seed + 405) < -0.6f) continue;          // a clearing
            if (t.TileSurface((int)(p.X / t.Tile), (int)(p.Y / t.Tile)) != SurfaceType.DeepSnow) continue;
            if (InPad(t, p, 12f)) continue;
            if (def.Water?.Rivers is { } rivers && WaterDistance(def.Water, p, out _) < 10f) continue;

            int ci = t.NearestCenter(p, out float d);
            if (t.CenterSurf[ci] == SurfaceType.Runway && d < 30f) continue;
            if (def.Gulag is { } g)
            {
                int k = ((ci - (int)g.From) % n + n) % n;
                if (k <= ((int)g.To - (int)g.From + n) % n + 6 && d < 34f) continue;
            }
            // Behind a full bank it may stand close; elsewhere a car could reach it.
            Vector2 dir = t.CenterDir(ci);
            bool left = (p - t.Center[ci]).Dot(new Vector2(dir.Y, -dir.X)) > 0;
            float clear = t.Banks != null && t.Banks.Closed(ci, left) && t.Banks.Taper(ci, left) > 0.9f
                ? t.Banks.Inner(ci, left) + Snowbanks.Width + 1.2f : t.HalfWidth[ci] + 22f;
            if (d < clear) continue;
            if (!FreeSpot(p, occupied, 3f)) continue;
            bool byBig = false;
            foreach (var pr in t.Props)
                if (pr.Type is "lorrywreck" && new Vector2(pr.Position.X, pr.Position.Z).DistanceTo(p) < 10f) { byBig = true; break; }
            if (byBig) continue;
            float scale = 0.75f + 0.8f * MathF.Pow(rng.Randf(), 1.3f);
            // Mostly spruce; a few pine stands, their trees a little smaller.
            int kind = Noise.Value(p.X / 26f, p.Y / 26f, def.Seed + 78) > 0.5f && rng.Randf() < 0.7f ? 2 : rng.Randf() < 0.5f ? 0 : 1;
            if (kind == 2) scale = Math.Min(scale, 1.1f);
            t.Taiga.Add((new Vector3(p.X, t.MeshHeight(p.X, p.Y), p.Y), scale, rng.RandfRange(0, MathF.Tau), kind));
        }
    }
}
