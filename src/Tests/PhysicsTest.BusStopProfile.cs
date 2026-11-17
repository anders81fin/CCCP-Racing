using System;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    // --busstop-profile=ID: the ground heights (cm relative to the road centre there) across the road at the first bus stop, every 2 m along it: the road's own evenness in front of the shelter.
    void BusStopProfile(string id)
    {
        var t = TrackBuilder.Build(TrackDef.Load($"res://tracks/{id}.json"));
        GD.Print($"{id} track signature {TrackSignature.Compute(t)}");
        foreach (var pr in t.Props) if (pr.Type == "billboard") { var q = new Vector2(pr.Position.X, pr.Position.Z); int ci = t.NearestCenter(q, out float dd); float near = 99; foreach (var o in t.Obstacles) near = MathF.Min(near, o.Center.DistanceTo(q) - o.Radius); GD.Print($"billboard {pr.Text} at {q}: {dd - t.HalfWidth[ci]:F1} m from the road edge, nearest obstacle {near:F1} m, ground {pr.Position.Y:F2}"); }
        {
            string g = System.Environment.GetEnvironmentVariable("HGRID");
            if (g != null)
            {
                var v = Array.ConvertAll(g.Split(','), x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture));
                string head2 = "  z\\x  "; for (float x = v[0]; x <= v[2]; x += v[4]) head2 += $"{x,6:F0}"; GD.Print(head2);
                for (float z = v[1]; z <= v[3]; z += v[4]) { string row = $"{z,6:F0}  "; for (float x = v[0]; x <= v[2]; x += v[4]) row += $"{t.MeshHeight(x, z),6:F1}"; GD.Print(row); }
                return;
            }
        }
        { var c = new System.Collections.Generic.Dictionary<string,int>(); foreach (var pr in t.Props) { c.TryGetValue(pr.Type, out int cv); c[pr.Type] = cv + 1; } GD.Print("props: " + string.Join(", ", c)); }
        {
            string xp = System.Environment.GetEnvironmentVariable("XPROF");
            if (xp != null)
            {
                var v = Array.ConvertAll(xp.Split(','), x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture));
                string hd = "  metre:"; for (float l = 0; l <= 10; l += 1f) hd += $"{l,5:F0}"; GD.Print("cm above road height, metres from the road edge towards the side:  " + hd);
                for (float m = v[0]; m <= v[1]; m += v[2]) { int i = (int)m; Vector2 d = t.CenterDir(i), r = new Vector2(-d.Y, d.X) * v[3]; string row = $"{m,8:F0}:"; for (float l = 0; l <= 10; l += 1f) { Vector2 p = t.Center[i] + r * (t.HalfWidth[i] + l); row += $"{(t.MeshHeight(p.X, p.Y) - t.CenterH[i]) * 100,5:F0}"; } GD.Print(row); }
                return;
            }
        }
        {
            string nr = System.Environment.GetEnvironmentVariable("NEAR");
            if (nr != null)
            {
                var v = Array.ConvertAll(nr.Split(','), x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture));
                for (float dx = -16; dx <= 16; dx += 8) { string row = ""; for (float dz = -16; dz <= 16; dz += 8) { var q = new Vector2(v[0] + dx, v[1] + dz); int ci = t.NearestCenter(q, out float dd); row += $"  ({q.X:F0},{q.Y:F0}): {dd - t.HalfWidth[ci]:F1}m"; } GD.Print("EDGE" + row); }
                return;
            }
        }
        {
            string sg = System.Environment.GetEnvironmentVariable("SGRID");
            if (sg != null)
            {
                var v = Array.ConvertAll(sg.Split(','), x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture));
                GD.Print($"surface grid ({t.Tile} m tiles): G grass, D dirt, F field, V gravel, M mud, c concrete, W water, . other");
                for (float z = v[1]; z <= v[3]; z += t.Tile) { string row = $"{z,6:F0}  "; for (float x = v[0]; x <= v[2]; x += t.Tile) { var sf = t.TileSurface((int)(x / t.Tile), (int)(z / t.Tile)); row += sf switch { SurfaceType.Grass => "G", SurfaceType.Dirt => "D", SurfaceType.Field => "F", SurfaceType.Gravel => "V", SurfaceType.Mud => "M", SurfaceType.Concrete => "c", SurfaceType.Water or SurfaceType.Shallow => "W", _ => "." }; } GD.Print(row); }
                return;
            }
        }
        if (System.Environment.GetEnvironmentVariable("DACHACHK") != null)
        {
            foreach (var point in new[] { PartyDacha.Site, PartyDacha.Gate, PartyDacha.Car, PartyDacha.FinnishCar, new Vector2(202, 164), new Vector2(220, 186), new Vector2(207, 170), new Vector2(233, 187) })
                GD.Print($"  level {point}: {t.MeshHeight(point.X, point.Y) - t.WaterLevel:F3}");
            foreach (var solid in PartyDacha.SolidFootprints())
                if (t.Sample(solid.At.X, solid.At.Y).Surface is SurfaceType.Water or SurfaceType.Shallow) GD.Print($"  wet solid at {solid.At} r={solid.Radius}");
            return;
        }
        if (System.Environment.GetEnvironmentVariable("VROW") != null)
        {
            var v = Array.ConvertAll(System.Environment.GetEnvironmentVariable("VROW").Split(','), x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture));
            int nvv = t.N + 1; int ixv = (int)(v[0] / t.Tile);
            GD.Print($"tile {t.Tile}, N {t.N}; vertex column x={ixv * t.Tile}:");
            for (int izv = (int)(v[1] / t.Tile); izv <= (int)(v[2] / t.Tile); izv++) GD.Print($"  z={izv * t.Tile}: {t.Heights[izv * nvv + ixv]:F2}  MeshHeight {t.MeshHeight(ixv * t.Tile, izv * t.Tile):F2}");
            return;
        }
        var stop = t.BusStops[0]; int cn = t.Center.Length, bi = stop.Index;
        Vector2 rb = new(-t.CenterDir(bi).Y, t.CenterDir(bi).X); float sign = (stop.Position - t.Center[bi]).Dot(rb) > 0 ? 1f : -1f;
        GD.Print($"{id} stop at metre {bi}, shelter {stop.Position}, half width {t.HalfWidth[bi]:F1}; rows = metres along the road, columns = lateral metres towards the shelter side (0 = centre line), cm vs the centre line");
        string head = "   along:"; for (float lat = -6; lat <= 12; lat += 1.5f) head += $"{lat,6:F1}"; GD.Print(head);
        for (int a = -16; a <= 16; a += 2)
        {
            int i = ((bi + a) % cn + cn) % cn; Vector2 d = t.CenterDir(i), r = new Vector2(-d.Y, d.X) * sign; float h0 = t.MeshHeight(t.Center[i].X, t.Center[i].Y);
            string row = $"{a,8}:";
            for (float lat = -6; lat <= 12; lat += 1.5f) { Vector2 p = t.Center[i] + r * lat; row += $"{(t.MeshHeight(p.X, p.Y) - h0) * 100,6:F0}"; }
            GD.Print(row);
        }
    }
}
