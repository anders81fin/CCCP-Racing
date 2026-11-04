using System;
using Godot;

namespace CCCPRacing.Track;

public partial class SiberiaDetails
{
    static readonly Color ServiceSteel = new(.30f, .35f, .35f), ServiceOlive = new(.33f, .39f, .28f);

    static void AddRadar(TrackData t, ActivityLayout plan)
    {
        var radar = t.Props.Find(p => p.Type == "radar");
        if (radar == null) return;
        // The bunker is 7m square, rotated 180 degrees. Keep the east aisle and north gate distinct.
        Vector2 site = new(radar.Position.X, radar.Position.Z);
        Vector2 worker = site + new Vector2(4.7f, 3f);
        plan.Workers.Add(new("radar-technician", WinterJob.Inspect, worker, worker, MathF.PI / 2, 2, 17));
        plan.Reserve(worker, 2.0f);
        plan.AddProp("radar-meter", site + new Vector2(3.58f, 3f), MathF.PI / 2, InspectionMeter);
        plan.AddProp("radar-toolbox", site + new Vector2(5.7f, -2.5f), 0, ToolBox, (Vector2.Zero, .45f));
        plan.AddProp("radar-service-ladder", site + new Vector2(3.7f, -2.6f), MathF.PI / 2,
            () => ServiceLadder(2.25f)); // Existing bunker collider already covers the wall-side ladder.
    }

    static void AddAirbase(TrackData t, ActivityLayout plan)
    {
        var aircraft = t.Props.Find(p => p.Type == "balalaika");
        if (aircraft == null) return;
        Vector2 center = new(aircraft.Position.X, aircraft.Position.Z);
        var basis = new Basis(Vector3.Up, aircraft.Rotation);
        Vector2 Local(float x, float z)
        {
            Vector3 v = basis * new Vector3(x, 0, z);
            return center + new Vector2(v.X, v.Z);
        }
        Vector2 mechanic = Local(2.2f, -2.6f);
        plan.Workers.Add(new("airbase-mechanic", WinterJob.Mechanic, mechanic, mechanic,
            TrackData.YawFor(Local(.5f, -2.6f) - mechanic), 2, 18, 2));
        Vector2 carryA = Local(5.8f, -3f), carryB = Local(5.8f, 1f);
        plan.Workers.Add(new("airbase-box-carrier", WinterJob.CarryBox, carryA, carryB,
            TrackData.YawFor(carryB - carryA), 6, 23, 5));
        // North of the real taxi lane, where the marshaller can see aircraft waiting for the plough.
        Vector2 signal = new(179, 25);
        plan.Workers.Add(new("airbase-marshaller", WinterJob.Signal, signal, signal,
            TrackData.YawFor(new Vector2(0, 1)), 2, 20));
        plan.Reserve(center, 8);
        plan.Reserve(signal, 2);
        Vector2 cart = Local(3.8f, -3f);
        plan.AddProp("airbase-service-cart", cart, aircraft.Rotation, ServiceCart,
            (new Vector2(0, -.45f), .6f), (new Vector2(0, .45f), .6f));
        plan.AddProp("airbase-service-ladder", Local(1.25f, -2.6f), aircraft.Rotation,
            () => ServiceLadder(1.5f), (Vector2.Zero, .3f));
        plan.AddProp("airbase-supply-box", Local(5.8f, 2.1f), aircraft.Rotation, ToolBox, (Vector2.Zero, .45f));
        // Short visible equipment lead remains inside the parked aircraft's maintenance bay.
        plan.AddProp("airbase-equipment-lead", cart, aircraft.Rotation, EquipmentLead);
        foreach (float z in new[] { -3.3f, .9f })
        {
            float x = z < 0 ? 0 : 1.25f;
            plan.AddProp(z < 0 ? "airbase-nose-chock" : "airbase-wheel-chock", Local(x, z - .4f), aircraft.Rotation,
                WheelChock); // Low chocks lie within the existing parked-jet footprint.
        }
    }

    static Node3D ToolBox()
    {
        var n = new Node3D();
        n.AddChild(Props.Part(Props.Box(.8f, .42f, .5f), ServiceOlive, new Vector3(0, .21f, 0)));
        n.AddChild(Props.Part(Props.Box(.84f, .08f, .54f), ServiceSteel, new Vector3(0, .46f, 0)));
        foreach (float x in new[] { -.22f, .22f })
            n.AddChild(Props.Part(Props.Box(.08f, .14f, .04f), PaleMetal, new Vector3(x, .38f, -.27f)));
        n.AddChild(Props.Part(Props.Box(.35f, .06f, .07f), PaleMetal, new Vector3(0, .55f, 0)));
        DrunkTractor.BatchStatic(n, null);
        return n;
    }
    static readonly Color PaleMetal = new(.72f, .75f, .72f);

    static Node3D ServiceLadder(float height)
    {
        var n = new Node3D();
        foreach (float x in new[] { -.3f, .3f })
            n.AddChild(Props.Part(Props.Box(.075f, height, .075f), PaleMetal, new Vector3(x, height / 2, 0), new Vector3(-10, 0, 0)));
        for (float y = .3f; y < height; y += .3f)
            n.AddChild(Props.Part(Props.Box(.6f, .065f, .065f), ServiceSteel, new Vector3(0, y, -MathF.Sin(Mathf.DegToRad(10)) * (y - height / 2))));
        DrunkTractor.BatchStatic(n, null);
        return n;
    }

    static Node3D InspectionMeter()
    {
        var n = new Node3D();
        n.AddChild(Props.Part(Props.Box(.7f, .8f, .14f), ServiceSteel, new Vector3(0, 1.55f, 0)));
        n.AddChild(Props.Part(Props.Box(.52f, .43f, .025f), new Color(.70f, .80f, .81f), new Vector3(0, 1.66f, .085f)));
        n.AddChild(Props.Part(Props.Box(.035f, .23f, .03f), new Color(.21f, .25f, .24f), new Vector3(.04f, 1.67f, .11f), new Vector3(0, 0, -25)));
        n.AddChild(Props.Part(Props.Cyl(.07f, .05f), PaleMetal, new Vector3(.2f, 1.28f, .1f), new Vector3(90, 0, 0)));
        DrunkTractor.BatchStatic(n, null);
        return n;
    }

    static Node3D ServiceCart()
    {
        var n = new Node3D();
        n.AddChild(Props.Part(Props.Box(1f, .9f, 1.8f), ServiceOlive, new Vector3(0, .8f, 0)));
        n.AddChild(Props.Part(Props.Box(1.08f, .08f, 1.88f), ServiceSteel, new Vector3(0, 1.3f, 0)));
        foreach (float x in new[] { -.55f, .55f })
        foreach (float z in new[] { -.55f, .55f })
            n.AddChild(Props.Part(Props.Cyl(.25f, .14f), new Color(.10f, .11f, .12f), new Vector3(x, .25f, z), new Vector3(0, 0, 90)));
        for (int k = 0; k < 5; k++)
            n.AddChild(Props.Part(Props.Box(.025f, .43f, .055f), PaleMetal, new Vector3(.515f, .88f, -.45f + k * .22f)));
        n.AddChild(Props.Part(Props.Box(.65f, .32f, .08f), ServiceSteel, new Vector3(0, 1f, -.94f)));
        n.AddChild(Props.Part(Props.Box(.4f, .2f, .03f), PaleMetal, new Vector3(0, 1.03f, -.99f)));
        DrunkTractor.BatchStatic(n, null);
        return n;
    }

    static Node3D EquipmentLead()
    {
        var n = new Node3D();
        Vector3[] points = { new(-.52f, .8f, 0), new(-.7f, .08f, 0), new(-1.25f, .06f, .1f), new(-2.0f, .1f, .3f), new(-2.7f, 1.15f, .4f) };
        for (int i = 0; i + 1 < points.Length; i++)
        {
            Vector3 d = points[i + 1] - points[i];
            var part = Props.Part(Props.Box(.055f, .055f, d.Length()), new Color(.10f, .11f, .10f), (points[i] + points[i + 1]) / 2);
            part.Basis = Basis.LookingAt(d.Normalized(), Vector3.Up);
            n.AddChild(part);
        }
        DrunkTractor.BatchStatic(n, null);
        return n;
    }

    static Node3D WheelChock()
    {
        var n = new Node3D();
        n.AddChild(Props.Part(new PrismMesh { Size = new Vector3(.38f, .24f, .38f) }, new Color(.76f, .55f, .17f), new Vector3(0, .12f, 0), new Vector3(0, 90, 0)));
        return n;
    }
}
