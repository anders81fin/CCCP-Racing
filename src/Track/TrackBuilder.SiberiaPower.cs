using System;
using System.Collections.Generic;
using Godot;

namespace CCCPRacing.Track;

public static partial class TrackBuilder
{
    // A connected utility network, not a decorative loop round the race course.
    static void PlaceSiberiaPower(TrackDef def, TrackData t, List<Vector2> occupied)
    {
        int source = Pole(new Vector2(22, 272), new Vector2(0, -1), "transformer");
        t.PowerSources.Add(source);
        Cable(Terminal(new Vector2(0, 272), "incoming grid"), source);
        var west = Line(source, new Vector2(22, 246), new Vector2(22, 220), new Vector2(25, 194), new Vector2(28, 168));
        var camp = Line(west[2], new Vector2(49, 182), new Vector2(74, 174), new Vector2(100, 174));
        int campBox = Cabinet(new Vector2(100, 171), "camp distribution");
        Cable(camp[^1], campBox);
        int hill = Pole(new Vector2(65, 88), Vector2.Right);
        Cable(west[^1], hill, new Vector2(58, 156), new Vector2(65, 112));
        var south = Line(hill, new Vector2(90, 88), new Vector2(115, 88), new Vector2(140, 88), new Vector2(165, 88), new Vector2(190, 88));
        int fieldBox = Cabinet(new Vector2(140, 85), "airbase feeder");
        Cable(south[2], fieldBox);
        // This low cabinet is beside the apron, outside both the taxiway and hangar mouths.
        int baseBox = Cabinet(new Vector2(140, 8), "airbase distribution");
        Cable(fieldBox, baseBox, new Vector2(140, 60), new Vector2(140, 30));

        int buildings = t.Props.Count;
        for (int i = 0; i < buildings; i++)
        {
            var p = t.Props[i];
            if (p.Type == "hangar")
            {
                t.CableEntries[i] = new Vector3(0, 0.8f, -11.06f);
                var target = CablePoint(i);
                Cable(baseBox, i, new Vector2(target.X, 30));
            }
            else if (p.Type == "barracks")
            {
                t.CableEntries[i] = new Vector3(15.56f, 0.8f, 0);
                var target = CablePoint(i);
                Cable(baseBox, i, new Vector2(target.X, 30));
            }
            else if (p.Type == "radar")
            {
                int radarBox = Cabinet(new Vector2(275, 161), "radar distribution");
                Cable(south[^1], radarBox, new Vector2(210, 106), new Vector2(236, 118), new Vector2(260, 118), new Vector2(260, 145));
                t.CableEntries[i] = new Vector3(-3.56f, 0.8f, -1);
                Cable(radarBox, i);
            }
        }

        if (def.SpeedTrap is { } gai)
        {
            var booth = SpeedTrap.BoothTransform(t, gai);
            int service = t.Props.Count;
            AddProp(t, "gaiservice", new Vector2(booth.Origin.X, booth.Origin.Z), booth.Basis.GetEuler().Y, null, occupied);
            t.CableEntries[service] = new Vector3(0, 0.8f, 1.04f);
            Cable(south[0], service);
        }
        // Every physical runway lamp gets a buried service; the positions are shared with rendering.
        int light = 0;
        foreach (var p in Airbase.EdgeLights(t))
            Cable(baseBox, Terminal(p, $"runway light {light++}"), new Vector2(p.X, 30));

        if (def.Gulag is { } gulag)
        {
            int hut = 0, tower = 0;
            foreach (var site in GulagCamp.Huts(t, gulag))
            {
                int index = Terminal(site.Position, $"camp barrack {hut++}", site.Yaw);
                t.CableEntries[index] = new Vector3(3.06f, 0.7f, 0);
                Cable(campBox, index);
            }
            foreach (var p in GulagCamp.TowerSites(t, gulag))
                Cable(campBox, Terminal(p, $"camp tower {tower++}"));
        }
        if (def.Depot != null)
        {
            var depot = FuelDepot.Layout(t, def.Depot);
            int terminal = Terminal(depot.Tank, "fuel depot", TrackData.YawFor(depot.Dir));
            t.CableEntries[terminal] = new Vector3(1.06f, 0.8f, -1.7f);
            Cable(baseBox, terminal, new Vector2(depot.Tank.X, 30));
        }
        var maintenance = t.SiberiaActivities?.Props.Find(p => p.Id == "airbase-service-cart");
        if (maintenance != null)
            Cable(baseBox, Terminal(maintenance.Position, "airbase maintenance"), new Vector2(maintenance.Position.X, 8));

        int Pole(Vector2 p, Vector2 direction, string text = null)
        {
            if (NearWater(t, p, 3)) throw new InvalidOperationException($"Siberia power pole in water at {p}.");
            int index = t.Props.Count;
            AddProp(t, "pole", p, TrackData.YawFor(direction.Normalized()), text, occupied);
            return index;
        }

        List<int> Line(int from, params Vector2[] route)
        {
            var result = new List<int>();
            foreach (var p in route)
            {
                var previous = t.Props[from].Position;
                int next = Pole(p, p - new Vector2(previous.X, previous.Z));
                t.PowerSpans.Add((from, next));
                int steps = Math.Max(1, (int)MathF.Ceiling(new Vector2(previous.X, previous.Z).DistanceTo(p) / 2));
                for (int k = 0; k <= steps; k++) occupied.Add(new Vector2(previous.X, previous.Z).Lerp(p, k / (float)steps));
                result.Add(next); from = next;
            }
            return result;
        }

        int Cabinet(Vector2 p, string text)
        {
            int index = t.Props.Count;
            AddProp(t, "powercabinet", p, 0, text, occupied);
            t.CableEntries[index] = new Vector3(0, 0.5f, -0.36f);
            return index;
        }

        int Terminal(Vector2 p, string text, float yaw = 0)
        {
            // Existing scene geometry owns these consumers; no extra collision or visible prop.
            int index = t.Props.Count;
            t.Props.Add(new PropInstance("powerconsumer", new Vector3(p.X, t.MeshHeight(p.X, p.Y), p.Y), yaw, text));
            return index;
        }

        Vector2 CablePoint(int i)
        {
            var p = t.Props[i];
            var world = p.Position + new Basis(Vector3.Up, p.Rotation) * t.CableEntries.GetValueOrDefault(i);
            return new Vector2(world.X, world.Z);
        }

        void Cable(int a, int b, params Vector2[] via)
        {
            foreach (int index in new[] { a, b })
                if (t.Props[index].Type == "pole" && t.Props[index].Text != "transformer")
                    t.CableEntries[index] = new Vector3(0.2f, 6.65f, 0.25f);
            var route = new Vector2[via.Length + 2];
            route[0] = CablePoint(a); route[^1] = CablePoint(b);
            via.CopyTo(route, 1);
            t.PowerCables.Add(new PowerCable(a, b, route));
        }
    }
}
