using System;
using System.Collections.Generic;
using Godot;

namespace CCCPRacing.Track;

public static partial class TrackBuilder
{
    // Atomgrad is supplied from the plant-side grid station: a short visible high-voltage
    // approach feeds separate buried industrial and town distribution circuits.
    static void PlaceAtomgradPower(TrackDef def, TrackData t, List<Vector2> occupied)
    {
        int incoming = Terminal(new Vector2(319, 51), "external grid");
        int approach = Pole(new Vector2(299, 51), Vector2.Right);
        int source = Pole(new Vector2(278, 51), Vector2.Right, "transformer");
        t.PowerSources.Add(source);
        Span(incoming, approach);
        Span(approach, source);

        int plantBox = Cabinet(new Vector2(264, 68), "plant switchyard");
        Cable(source, plantBox, new Vector2(272, 58));
        int townBox = Cabinet(new Vector2(139, 77), "town distribution");
        Cable(plantBox, townBox,
            new Vector2(245, 47), new Vector2(211, 47), new Vector2(195, 48),
            new Vector2(176, 50), new Vector2(157, 56), new Vector2(145, 67));

        // The plant blocks and the turbine hall sit on their own short industrial feeder.
        int plantFeeder = Cabinet(new Vector2(250, 82), "reactor feeder");
        Cable(plantBox, plantFeeder, new Vector2(258, 76));
        foreach (var p in new (Vector2 At, string Name)[]
        {
            (new(205, 90), "turbine hall"), (new(160, 118), "reactor 1"),
            (new(184, 118), "reactor 2"), (new(208, 118), "reactor 3"),
            (new(233, 118), "reactor 4 sarcophagus"), (new(220, 146), "exhaust stack")
        })
            Cable(plantFeeder, Terminal(p.At, p.Name), new Vector2(p.At.X, 83));

        int townFeeder = Cabinet(new Vector2(104, 84), "residential feeder");
        Cable(townBox, townFeeder, new Vector2(123, 78));
        int squareFeeder = Cabinet(new Vector2(42, 168), "culture square feeder");
        Cable(townBox, squareFeeder, new Vector2(119, 92), new Vector2(92, 111), new Vector2(83, 143));

        int apartments = 0;
        int buildings = t.Props.Count;
        for (int i = 0; i < buildings; i++)
        {
            PropInstance p = t.Props[i];
            Vector2 at = new(p.Position.X, p.Position.Z);
            if (p.Type == "apartments")
            {
                Cable(townFeeder, Terminal(at, $"apartment block {++apartments}"),
                    new Vector2(91, at.Y), new Vector2(at.X, at.Y));
            }
            else if (p.Type is "ferriswheel" or "culture" or "bumpercars")
            {
                Cable(squareFeeder, Terminal(at, p.Type), new Vector2(at.X, 164));
            }
            else if (p.Type == "wash")
            {
                Cable(townBox, Terminal(at, "decontamination station"),
                    new Vector2(91, 218), new Vector2(74, 218));
            }
        }

        if (def.SpeedTrap is { } gai)
        {
            var booth = SpeedTrap.BoothTransform(t, gai);
            int service = t.Props.Count;
            AddProp(t, "gaiservice", new Vector2(booth.Origin.X, booth.Origin.Z), booth.Basis.GetEuler().Y, null, occupied);
            Cable(plantBox, service, new Vector2(258, 66), new Vector2(booth.Origin.X, 66));
        }
        if (def.Depot != null)
        {
            var depot = FuelDepot.Layout(t, def.Depot);
            Cable(townBox, Terminal(depot.Tank, "fuel depot"), new Vector2(180, 98), new Vector2(224, 130), new Vector2(depot.Tank.X, 155));
        }
        int serviceProps = t.Props.Count;
        for (int i = 0; i < serviceProps; i++)
        {
            var p = t.Props[i];
            if (p.Type is "railyard" or "crossing")
            {
                var at = new Vector2(p.Position.X, p.Position.Z);
                Cable(townBox, Terminal(at, p.Type == "railyard" ? "rail yard" : "level crossing"),
                    new Vector2(136, 120), new Vector2(136, 210), new Vector2(at.X, 226));
            }
        }

        int Pole(Vector2 p, Vector2 direction, string text = null)
        {
            int index = t.Props.Count;
            AddProp(t, "pole", p, TrackData.YawFor(direction.Normalized()), text, occupied);
            return index;
        }

        int Cabinet(Vector2 p, string label)
        {
            int index = t.Props.Count;
            AddProp(t, "powercabinet", p, 0, label, occupied);
            t.CableEntries[index] = new Vector3(0, 0.5f, -0.36f);
            return index;
        }

        int Terminal(Vector2 p, string label)
        {
            int index = t.Props.Count;
            t.Props.Add(new PropInstance("powerconsumer", new Vector3(p.X, t.MeshHeight(p.X, p.Y), p.Y), 0, label));
            return index;
        }

        void Span(int a, int b)
        {
            t.PowerSpans.Add((a, b));
            Vector3 from = t.Props[a].Position, to = t.Props[b].Position;
            int steps = Math.Max(1, (int)MathF.Ceiling(from.DistanceTo(to) / 2f));
            for (int i = 0; i <= steps; i++)
            {
                Vector3 p = from.Lerp(to, i / (float)steps);
                occupied.Add(new Vector2(p.X, p.Z));
            }
        }

        void Cable(int a, int b, params Vector2[] via)
        {
            var route = new Vector2[via.Length + 2];
            Vector3 start = t.Props[a].Position, end = t.Props[b].Position;
            route[0] = new Vector2(start.X, start.Z);
            route[^1] = new Vector2(end.X, end.Z);
            via.CopyTo(route, 1);
            t.PowerCables.Add(new PowerCable(a, b, route));
        }
    }
}
