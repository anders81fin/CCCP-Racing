using System;
using System.Collections.Generic;
using Godot;

namespace CCCPRacing.Track;

public static partial class TrackBuilder
{
    // A portable generator feeds the field post and service bay through ground cables.
    // Former farm buildings stay disconnected; no runway lights or permanent base grid remain.
    static void PlaceBonusPower(TrackDef def, TrackData t, List<Vector2> occupied)
    {
        int source=t.Props.Count;
        AddProp(t,"fieldgenerator",BonusCampLayout.Generator,0,null,occupied);
        t.PowerSources.Add(source);
        int airbase = Cabinet(BonusCampLayout.Distribution, "field distribution");
        Cable(source,airbase);
        int farmSource=Terminal(BonusCampLayout.CableJunction,"field cable junction");
        Cable(airbase,farmSource,new Vector2(124,119));
        int buildings=t.Props.Count;
        for(int i=0;i<buildings;i++)
            if(t.Props[i].Type=="fieldtent")
            {
                t.CableEntries[i]=new Vector3(-3.12f,.5f,0);
                Vector2 entry=CablePoint(i);
                Cable(airbase,i,new Vector2(entry.X,125));
            }
        if (def.SpeedTrap is { } gai)
        {
            var booth = SpeedTrap.BoothTransform(t, gai);
            int service = t.Props.Count;
            AddProp(t, "gaiservice", new Vector2(booth.Origin.X, booth.Origin.Z), booth.Basis.GetEuler().Y, null, occupied);
            t.CableEntries[service] = new Vector3(0, 0.8f, 1.04f);
            Cable(airbase, service, new Vector2(100,125), new Vector2(85,140),
                new Vector2(70,180), new Vector2(booth.Origin.X,booth.Origin.Z-5));
        }
        if (def.Depot != null)
        {
            var depot = FuelDepot.Layout(t, def.Depot);
            int terminal = Terminal(depot.Tank, "fuel depot", TrackData.YawFor(depot.Dir));
            t.CableEntries[terminal] = new Vector3(1.06f, 0.8f, -1.7f);
            Cable(farmSource, terminal, new Vector2(122, 115), new Vector2(depot.Tank.X, 115));
        }

        int Cabinet(Vector2 p, string text)
        {
            int index = t.Props.Count;
            AddProp(t, "powercabinet", p, 0, text, occupied);
            t.CableEntries[index] = new Vector3(0, 0.5f, -0.36f);
            if (text == "bonus main distribution")
            {
                // Reserve the fenced yard before procedural vegetation is placed.
                for (int x = -3; x <= 3; x++)
                    for (int z = -5; z <= 3; z++) occupied.Add(p + new Vector2(x, z));
                for (int z = -5; z <= 3; z++)
                    foreach (float x in new[] { -3f, 3f })
                        t.Obstacles.Add(new Obstacle(p + new Vector2(x, z), 0.55f));
                for (int x = -2; x <= 2; x++)
                    foreach (float z in new[] { -5f, 3f })
                        t.Obstacles.Add(new Obstacle(p + new Vector2(x, z), 0.55f));
            }
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
