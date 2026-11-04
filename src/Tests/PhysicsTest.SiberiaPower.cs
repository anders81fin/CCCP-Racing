using System;
using System.Collections.Generic;
using System.Linq;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    void SiberiaPowerTest()
    {
        GD.Print("Siberia radar: buried airbase services, connected distribution and clear access");
        var def = TrackDef.Load("res://tracks/siberia.json");
        var t = TrackBuilder.Build(def);
        var reached = new HashSet<int>(t.PowerSources);
        bool changed;
        do
        {
            changed = false;
            foreach (var (a, b) in t.PowerSpans.Concat(t.PowerCables.Select(c => (c.A, c.B))))
            {
                if (reached.Contains(a)) changed |= reached.Add(b);
                if (reached.Contains(b)) changed |= reached.Add(a);
            }
        } while (changed);
        bool supplied = t.PowerSources.Count == 1;
        for (int i = 0; i < t.Props.Count; i++)
            if (t.Props[i].Type is "pole" or "powercabinet" or "powerconsumer" or "radar" or "hangar" or "barracks" or "gaiservice")
                supplied &= reached.Contains(i);
        Check(supplied, "all poles, radar, camp, runway lamps and airbase buildings reach one source through aerial or buried connections");
        Check(t.PowerSpans.Count > 10 && t.PowerSpans.All(s => t.Props[s.A].Type == "pole" && t.Props[s.B].Type == "pole"),
            "only pole-to-pole aerial spans are rendered; all consumers use buried services");
        bool dry = true, clear = true, trees = true, wires = true, flight = true;
        foreach (var p in t.Props.Where(p => p.Type == "pole"))
        {
            var pos = new Vector2(p.Position.X, p.Position.Z);
            int ci = t.NearestCenter(pos, out float d), hint = -1;
            clear &= d > t.HalfWidth[ci] + 4 && !t.Banks.Query(pos, 0.5f, ref hint, out _, out _, out _);
            for (float z = -3; z <= 3; z++)
            for (float x = -3; x <= 3; x++)
                dry &= t.Sample(pos.X + x, pos.Y + z).Surface is not (SurfaceType.Ice or SurfaceType.Water or SurfaceType.Shallow);
        }
        foreach (var (a, b) in t.PowerSpans)
        {
            var pa = t.Props[a]; var pb = t.Props[b];
            flight &= pa.Position.Z >= 88 && pb.Position.Z >= 88;
            float len = pa.Position.DistanceTo(pb.Position);
            for (int w = 0; w < Props.WireCount(t, a, b); w++)
            {
                var from = pa.Position + new Basis(Vector3.Up, pa.Rotation) * Props.WireAttachment(t, a, w);
                var to = pb.Position + new Basis(Vector3.Up, pb.Rotation) * Props.WireAttachment(t, b, w);
                for (float f = 0; f <= 1; f += 0.02f)
                {
                    var p = from.Lerp(to, f) - Vector3.Up * (4 * Props.WireSag(a, w, len) * f * (1 - f));
                    float height = p.Y - t.MeshHeight(p.X, p.Z);
                    if (height < 4.5f) { GD.Print($"  low Siberia wire {a}->{b}: {height:F2} m at {p}"); wires = false; }
                }
            }
            var a2 = new Vector2(pa.Position.X, pa.Position.Z); var b2 = new Vector2(pb.Position.X, pb.Position.Z);
            foreach (var tree in t.Taiga)
            {
                var p = new Vector2(tree.Pos.X, tree.Pos.Z);
                trees &= Geometry2D.GetClosestPointToSegment(p, a2, b2).DistanceTo(p) > 2.5f;
            }
            foreach (var tree in t.Props.Where(p => p.Type is "pine" or "spruce" or "birch"))
            {
                var p = new Vector2(tree.Position.X, tree.Position.Z);
                trees &= Geometry2D.GetClosestPointToSegment(p, a2, b2).DistanceTo(p) > 2.5f;
            }
        }
        Check(dry && clear, "poles stand beyond roads and snowbanks, away from the frozen river");
        Check(wires && trees, "all sagging conductors stay above 4.5 m and their corridor is free of trees");
        Check(flight, "no aerial span reaches the runway, apron or northern airbase");

        int radar = t.Props.FindIndex(p => p.Type == "radar");
        Check(radar >= 0 && new Vector2(t.Props[radar].Position.X, t.Props[radar].Position.Z) == SiberiaRadar.Site
            && t.CableEntries.ContainsKey(radar) && !t.PowerAnchors.ContainsKey(radar),
            "radar sits on the eastern shoulder with a buried entry on the bunker wall");
        var baseBox = t.Props.FindIndex(p => p.Text == "airbase distribution");
        var fieldBox = t.Props.FindIndex(p => p.Text == "airbase feeder");
        var crossing = t.PowerCables.Find(c => c.A == fieldBox && c.B == baseBox);
        bool crosses = false;
        for (int i = 0; i + 1 < crossing.Route.Length; i++)
        for (float f = 0; f <= 1; f += 0.02f)
        {
            var p = crossing.Route[i].Lerp(crossing.Route[i + 1], f);
            int ci = t.NearestCenter(p, out float d);
            crosses |= d < t.HalfWidth[ci] && t.CenterSurf[ci] == SurfaceType.Runway;
        }
        Check(crosses, "airbase feeder crosses beneath the actual runway");
        Check(t.Props.Count(p => p.Text?.StartsWith("runway light ") == true) == Airbase.EdgeLights(t).Count()
            && t.Props.Count(p => p.Text?.StartsWith("camp barrack ") == true) == GulagCamp.Huts(t, def.Gulag).Count()
            && t.Props.Count(p => p.Text?.StartsWith("camp tower ") == true) == GulagCamp.TowerSites(t, def.Gulag).Count(),
            "buried services use the same lamp, barrack and watchtower layouts as the scene");

        bool access = true, snow = true, bank = false;
        for (int i = 0; i + 1 < SiberiaRadar.Drive.Length; i++)
        for (float f = 0; f <= 1; f += 0.05f)
        {
            var p = SiberiaRadar.Drive[i].Lerp(SiberiaRadar.Drive[i + 1], f);
            int hint = -1;
            bank |= t.Banks.Query(p, 1.1f, ref hint, out _, out _, out _);
            foreach (var o in t.Obstacles)
                if (o.Center.DistanceTo(p) <= o.Radius + 1.1f)
                { GD.Print($"  radar driveway blocked at {p} by {o.Center}"); access = false; }
            snow &= t.Sample(p.X, p.Y).Surface is SurfaceType.Snow or SurfaceType.Concrete;
        }
        Check(access && snow && !bank, "a clear ploughed service spur passes through the snowbank opening and radar gate");
        Check(t.Taiga.All(p => !SiberiaRadar.Reserved(new(p.Pos.X, p.Pos.Z)))
            && t.Props.Where(p => p.Type is "birch" or "pine" or "spruce").All(p => !SiberiaRadar.Reserved(new(p.Position.X, p.Position.Z))),
            "radar grounds, guard bay and access stay free of random trees");

        bool guardClear = true;
        int guardHint = -1;
        foreach (var o in t.Obstacles) guardClear &= o.Center.DistanceTo(SiberiaRadar.Guard) > o.Radius + 1.2f;
        Check(guardClear && !t.Banks.Query(SiberiaRadar.Guard, 1.2f, ref guardHint, out _, out _, out _),
            "radar guard bay is outside the fence, service road and snowbanks");

        var baselineDef = TrackDef.Load("res://tracks/siberia.json");
        baselineDef.Id = "siberia-baseline";
        baselineDef.Props.RemoveAll(p => p.Type == "radar");
        baselineDef.Pads.RemoveAll(p => p.X0 == 265 && p.Z0 == 151);
        var baseline = TrackBuilder.Build(baselineDef);
        bool roadUnchanged = TrackSignature.Compute(baseline) == TrackSignature.Compute(t);
        for (int i = 0; i < t.Center.Length; i++)
        {
            var p = t.Center[i];
            roadUnchanged &= MathF.Abs(t.MeshHeight(p.X, p.Y) - baseline.MeshHeight(p.X, p.Y)) < 0.001f;
        }
        Check(roadUnchanged, "radar foundation and access preserve the main road heights and existing lap-record signature");

        var props = Props.Build(t);
        Check(props.GetNode("PowerLines").GetChildCount() == t.PowerSpans.Count,
            "buried links never produce a rendered wire or a falling-wire attachment");
        props.Free();
    }
}
