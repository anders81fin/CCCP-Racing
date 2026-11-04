using System;
using System.Collections.Generic;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    void VillagePowerTest()
    {
        GD.Print("Village electricity: connected consumers, dry poles and road clearance");
        var t = TrackBuilder.Build(TrackDef.Load("res://tracks/ilyich.json"));
        int source = t.Props.FindIndex(p => p.Type == "pole" && p.Text == "transformer");
        var reached = new HashSet<int> { source };
        bool changed;
        do
        {
            changed = false;
            foreach (var (a, b) in t.PowerSpans)
            {
                if (reached.Contains(a)) changed |= reached.Add(b);
                if (reached.Contains(b)) changed |= reached.Add(a);
            }
        } while (changed);
        bool supplied = source >= 0, dry = true, roadside = true, clearance = true;
        int lamps = 0;
        for (int i = 0; i < t.Props.Count; i++)
        {
            var p = t.Props[i];
            if (p.Type is "izba" or "barn" or "startbarn" or "silo" or "church" or "pole" or "streetlamp" or "gaiservice" or "partydacha")
                supplied &= reached.Contains(i);
            if (p.Type == "streetlamp") lamps++;
            if (p.Type is not ("pole" or "streetlamp")) continue;
            var pos = new Vector2(p.Position.X, p.Position.Z);
            int ci = t.NearestCenter(pos, out float d);
            if (d <= t.HalfWidth[ci] + 0.7f)
            {
                GD.Print($"  pole on road: {pos}, edge distance {d - t.HalfWidth[ci]:F2} m");
                roadside = false;
            }
            for (float dz = -2; dz <= 2; dz += 1)
            for (float dx = -2; dx <= 2; dx += 1)
                dry &= t.TileSurface((int)((pos.X + dx) / t.Tile), (int)((pos.Y + dz) / t.Tile))
                    is not (SurfaceType.Water or SurfaceType.Shallow or SurfaceType.Ice);
        }
        foreach (var (a, b) in t.PowerSpans)
        for (int w = 0; w < Props.WireCount(t, a, b); w++)
        {
            var pa = t.Props[a]; var pb = t.Props[b];
            var from = pa.Position + new Basis(Vector3.Up, pa.Rotation) * Props.WireAttachment(t, a, w);
            var to = pb.Position + new Basis(Vector3.Up, pb.Rotation) * Props.WireAttachment(t, b, w);
            float len = new Vector2(to.X - from.X, to.Z - from.Z).Length();
            for (float f = 0.02f; f < 1; f += 0.02f)
            {
                var p = from.Lerp(to, f) - Vector3.Up * (4 * Props.WireSag(a, w, len) * f * (1 - f));
                int ci = t.NearestCenter(new Vector2(p.X, p.Z), out float d);
                float height = p.Y - t.MeshHeight(p.X, p.Z);
                if (d < t.HalfWidth[ci] && height < 4.5f)
                {
                    GD.Print($"  low wire {a}->{b}: {height:F2} m at ({p.X:F1},{p.Z:F1})");
                    clearance = false;
                }
            }
        }
        Check(supplied, "every village consumer and pole connects to the farm transformer");
        Check(dry && roadside, "poles and lanterns stand on dry ground outside the driving surface");
        Check(clearance, "every wire crossing the road clears it by at least 4.5 m");
        Check(lamps == t.BusStops.Count, "each bus stop has a supplied lantern");
        var gai = t.Props.Find(p => p.Type == "gaiservice");
        var booth = SpeedTrap.BoothTransform(t, TrackDef.Load("res://tracks/ilyich.json").SpeedTrap);
        Check(gai != null && gai.Position.DistanceTo(booth.Origin) < 0.01f
            && MathF.Abs(Mathf.AngleDifference(gai.Rotation, booth.Basis.GetEuler().Y)) < 0.01f,
            "GAI service enters the actual booth and connects to the same transformer");
        ServiceWireFallTest(t);
    }

    void ServiceWireFallTest(TrackData track)
    {
        var service = track.PowerSpans.Find(s => track.Props[s.B].Type == "izba");
        var t = new TrackData { N = track.N, Tile = track.Tile, Heights = track.Heights, Surf = track.Surf };
        t.Props.Add(track.Props[service.A]);
        t.Props.Add(track.Props[service.B]);
        t.PowerAnchors[1] = track.PowerAnchors[service.B];
        t.PowerSpans.Add((0, 1));
        t.Obstacles.Add(track.Obstacles[track.PropObstacle[service.A]]);
        t.PropObstacle[0] = 0;
        int glows = NightGlow.Glows.Count;
        var props = Props.Build(t);
        AddChild(props);
        var breakables = new Breakables();
        breakables.Init(t, null, props, null);
        var wire = (MeshInstance3D)props.GetNode("PowerLines").GetChild(0);
        var before = wire.Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        bool broke = breakables.TryBreak(0, 30, Vector3.Right * 30, t.Props[0].Position);
        breakables.Tick(0.4f, null, false);
        var after = wire.Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        Check(broke && after[0].DistanceTo(before[0]) > 0.1f && after[^1].DistanceTo(before[^1]) < 0.001f,
            "a falling pole pulls its service wire down while the building attachment stays fixed");
        breakables.Reset();
        Check(wire.Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array()[0].DistanceTo(before[0]) < 0.001f,
            "reset restores the service wire with the pole");
        props.Free();
        breakables.Free();
        NightGlow.Glows.RemoveRange(glows, NightGlow.Glows.Count - glows);
    }
}
