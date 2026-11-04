using System;
using Godot;

namespace CCCPRacing.Track;

/// <summary>Model-local cosmetic parts. Opaque additions are merged; no obstacles or actor state.</summary>
internal static class SiberiaModelDetails
{
    static readonly Color Steel = new(0.26f, 0.29f, 0.29f), Pale = new(0.76f, 0.78f, 0.73f);
    static readonly Color Snow = new(0.95f, 0.96f, 0.99f), Rust = new(0.35f, 0.22f, 0.14f);
    static readonly Color Black = new(0.08f, 0.09f, 0.10f), Orange = new(0.88f, 0.39f, 0.08f);

    sealed class Parts
    {
        readonly Node3D root = new() { Name = "SurfaceDetails" };
        public void Box(Vector3 size, Vector3 at, Color color, Vector3? rotation = null) =>
            root.AddChild(Props.Part(Props.Box(size.X, size.Y, size.Z), color, at, rotation));
        public void Cylinder(float radius, float length, Vector3 at, Color color, Vector3? rotation = null) =>
            root.AddChild(Props.Part(Props.Cyl(radius, length, 10), color, at, rotation));
        public void Bar(Vector3 a, Vector3 b, float width, Color color)
        {
            Vector3 d = b - a;
            var part = Props.Part(Props.Box(width, width, d.Length()), color, (a + b) / 2);
            part.Basis = Basis.LookingAt(d.Normalized(), MathF.Abs(d.Normalized().Y) > .99f ? Vector3.Right : Vector3.Up);
            root.AddChild(part);
        }
        public void Attach(Node3D parent)
        {
            DrunkTractor.BatchStatic(root, null);
            root.GetChild(0).Name = "DetailsMesh";
            parent.AddChild(root);
        }
    }

    public static void Plough(Node3D model)
    {
        var p = new Parts();
        foreach (float s in new[] { -1f, 1f })
        {
            // Cab doors, handles, mirrors and access steps.
            p.Box(new(.035f, .65f, 1.65f), new(s * 1.36f, 2.05f, -2.2f), Orange * .8f);
            p.Box(new(.06f, .08f, .35f), new(s * 1.40f, 2.34f, -1.9f), Pale);
            p.Bar(new(s * 1.3f, 2.85f, -3.05f), new(s * 1.65f, 2.85f, -3.25f), .07f, Steel);
            p.Box(new(.10f, .38f, .24f), new(s * 1.66f, 2.8f, -3.25f), Black);
            p.Box(new(.36f, .12f, 1.15f), new(s * 1.37f, 1.28f, -2.0f), Steel);
            p.Box(new(.10f, .32f, .22f), new(s * 1.25f, 1.6f, 4.23f), new(.65f, .08f, .04f));
            p.Box(new(.035f, .16f, 4.6f), new(s * 1.37f, 2.35f, 1.6f), Pale);
            // Hopper ribs, salt stains and wheel hubs.
            for (int k = 0; k < 4; k++)
                p.Box(new(.10f, 1.3f, .12f), new(s * 1.39f, 2f, -.55f + k * 1.4f), Steel);
            for (int k = 0; k < 3; k++)
                p.Box(new(.04f, .28f + .08f * k, .65f), new(s * 1.38f, 1.43f, .1f + k * 1.5f), Pale * .65f);
            foreach (float z in new[] { -2.9f, 1.6f, 3.3f })
            {
                p.Cylinder(.27f, .03f, new(s * 1.56f, .62f, z), Steel, new(0, 0, 90));
                p.Cylinder(.10f, .05f, new(s * 1.59f, .62f, z), Rust, new(0, 0, 90));
            }
            p.Bar(new(s * .65f, 2.4f, -3.31f), new(s * .95f, 2.98f, -3.31f), .055f, Black);
            // Hydraulic cylinders remain inside the existing blade envelope.
            p.Bar(new(s * .8f, .9f, -4.2f), new(s * 1.5f, 1.1f, -5.15f), .16f, Steel);
            p.Bar(new(s * 1.5f, 1.1f, -5.15f), new(s * 1.8f, 1.16f, -5.55f), .09f, Pale);
        }
        for (int k = 0; k < 9; k++)
            p.Box(new(.07f, .23f, .035f), new(-.88f + k * .22f, 1.55f, -4.56f), Pale * .65f);
        p.Box(new(2.45f, .12f, 1.7f), new(0, 3.35f, -2.25f), Snow);
        p.Cylinder(.09f, 1.45f, new(1.12f, 2.63f, -.9f), Steel);
        p.Cylinder(.14f, .12f, new(1.12f, 3.38f, -.9f), Black);
        p.Box(new(2.3f, .15f, .18f), new(0, 1.15f, 4.5f), Pale);
        p.Attach(model);
    }

    public static void Jet(Node3D model, Node3D gear)
    {
        var p = new Parts();
        const float Y = 1.6f;
        Color seam = new(.60f, .63f, .63f);
        // Fuselage panel bands, darker heat-stained exhaust and intake lip.
        foreach (float z in new[] { -4.55f, -.7f, 2.0f, 4.7f })
            p.Cylinder(.628f, .045f, new(0, Y, z), Pale * .76f, new(90, 0, 0));
        p.Cylinder(.56f, .10f, new(0, Y, -5.42f), Pale, new(90, 0, 0));
        p.Cylinder(.505f, .13f, new(0, Y, 6.15f), new(.40f, .32f, .24f), new(90, 0, 0));
        p.Cylinder(.38f, .02f, new(0, Y, 6.27f), Black, new(90, 0, 0));
        p.Bar(new(.12f, Y + .1f, -5.95f), new(.12f, Y + .1f, -6.65f), .035f, Steel);
        p.Box(new(.12f, .48f, .5f), new(0, Y + .88f, .6f), Steel, new(-25, 0, 0));
        // Canopy sill and frames; the glass keeps its existing material.
        foreach (float s in new[] { -1f, 1f })
        {
            p.Bar(new(s * .27f, Y + .53f, -3.3f), new(s * .27f, Y + .53f, -2.05f), .07f, Steel);
            p.Bar(new(s * .27f, Y + .53f, -3.1f), new(0, Y + .86f, -3.1f), .055f, Pale);
            p.Bar(new(s * .27f, Y + .53f, -2.25f), new(0, Y + .86f, -2.25f), .055f, Pale);
            // Control-surface seams sit just above the delta wing and tailplane.
            p.Bar(new(s * .75f, Y - .175f, 3.32f), new(s * 3.05f, Y - .25f, 3.32f), .08f, seam);
            p.Bar(new(s * .75f, Y - .175f, 1.1f), new(s * .75f, Y - .175f, 3.3f), .08f, seam);
            p.Bar(new(s * .7f, Y + .025f, 5.45f), new(s * 1.7f, Y - .01f, 5.45f), .07f, seam);
            p.Box(new(.045f, .15f, 1.1f), new(s * .625f, Y, 2.9f), Steel);
            var number = Buildings.TextPanel("21", 1.0f, .45f, new(s * .64f, Y + .04f, -1.55f), new(.72f, .74f, .72f), new(.65f, .08f, .06f));
            number.RotationDegrees = new(0, s * 90, 0);
            model.AddChild(number);
        }
        p.Attach(model);
        var g = new Parts();
        foreach (var (x, z) in new[] { (0f, -3.3f), (-1.25f, .9f), (1.25f, .9f) })
        {
            g.Cylinder(.14f, .025f, new(x + .115f, .32f, z), Pale, new(0, 0, 90));
            g.Box(new(.24f, .6f, .09f), new(x + .12f, .86f, z + .15f), Pale);
            g.Bar(new(x, .5f, z), new(x, 1.25f, z + .32f), .065f, Pale);
        }
        g.Attach(gear); // Gear still retracts as one node; missiles retain their named roots.
    }

    public static void Hangar(Node3D model, bool winter)
    {
        var p = new Parts();
        // Exposed arch joints and interior ribs, leaving the full mouth open.
        foreach (float z in new[] { -7f, 0f, 7f, 11.04f })
        for (int k = 0; k < 14; k++)
        {
            float a = MathF.PI * k / 14, b = MathF.PI * (k + 1) / 14;
            float r = z > 11 ? 9.02f : 8.18f;
            p.Bar(new(MathF.Cos(a) * r, MathF.Sin(a) * r, z), new(MathF.Cos(b) * r, MathF.Sin(b) * r, z), .11f, Pale * .7f);
        }
        foreach (float s in new[] { -1f, 1f })
        {
            p.Box(new(.5f, 2.0f, .12f), new(s * 8.65f, 1.1f, 11.08f), Steel);
            for (int k = 0; k < 5; k++)
                p.Box(new(.52f, .16f, .06f), new(s * 8.65f, .35f + k * .33f, 11.16f), k % 2 == 0 ? new(.82f, .63f, .19f) : Black);
        }
        if (winter)
        {
            for (int k = 4; k < 10; k++)
            {
                float a = MathF.PI * (k + .5f) / 14;
                p.Box(new(1.95f, .14f, 20.8f), new(MathF.Cos(a) * 9.04f, MathF.Sin(a) * 9.04f, -.3f), Snow, new(0, 0, Mathf.RadToDeg(a) - 90));
            }
        }
        p.Attach(model);
    }

    public static void Radar(Node3D model, bool winter)
    {
        var p = new Parts();
        // Concrete formwork seams, louvred vents, door hinges and tower access ladder.
        foreach (float s in new[] { -1f, 1f })
        {
            p.Box(new(.035f, .045f, 6.9f), new(s * 3.52f, 1.5f, 0), Steel);
            p.Box(new(.035f, 2.8f, .045f), new(s * 3.52f, 1.5f, -1.6f), Steel);
            p.Box(new(.06f, .9f, 1.35f), new(s * 3.54f, 1.9f, .7f), Black);
            for (int k = 0; k < 5; k++)
                p.Box(new(.08f, .07f, 1.25f), new(s * 3.58f, 1.55f + k * .16f, .7f), Pale * .7f);
            p.Bar(new(s * .36f, 3.1f, 1.36f), new(s * .36f, 8.8f, 1.36f), .065f, Steel);
        }
        for (int k = 0; k < 15; k++)
            p.Bar(new(-.36f, 3.25f + k * .36f, 1.36f), new(.36f, 3.25f + k * .36f, 1.36f), .06f, Pale);
        p.Box(new(.12f, .12f, .06f), new(.4f, 1.1f, 3.59f), Pale);
        for (int k = 0; k < 2; k++)
            p.Box(new(.13f, .22f, .08f), new(-.56f, .45f + k * 1.1f, 3.59f), Steel);
        if (winter) p.Box(new(6.9f, .14f, 6.9f), new(0, 3.08f, 0), Snow);
        p.Attach(model);
    }

    public static void Barracks(Node3D model, bool winter)
    {
        var p = new Parts();
        foreach (float s in new[] { -1f, 1f })
        {
            // Drainpipes and window crossbars break up the prefab facade.
            p.Cylinder(.09f, 9.0f, new(s * 14.4f, 4.55f, 5.16f), Steel);
            for (int floor = 0; floor < 3; floor++)
            for (int k = 0; k < 10; k++)
            {
                if (s > 0 && floor == 0 && k == 5) continue;
                float x = -13.5f + k * 3, y = 1.6f + floor * 3;
                p.Box(new(1.55f, .10f, .16f), new(x, y - .68f, s * 5.08f), Pale);
                p.Box(new(.06f, 1.3f, .04f), new(x, y, s * 5.07f), ConcreteFrame);
            }
        }
        p.Box(new(2.4f, .5f, 1.8f), new(-8, 9.8f, 0), Steel);
        for (int k = 0; k < 4; k++)
            p.Box(new(2.1f, .055f, .055f), new(-8, 9.65f + k * .1f, .93f), Pale);
        if (winter)
        {
            p.Box(new(29.7f, .16f, 9.7f), new(0, 9.66f, 0), Snow);
            p.Box(new(3.35f, .1f, 1.55f), new(0, 2.75f, 5.8f), Snow);
        }
        p.Attach(model);
    }
    static readonly Color ConcreteFrame = new(.42f, .43f, .40f);

    public static void CampHut(Node3D model, float length)
    {
        var p = new Parts();
        foreach (float s in new[] { -1f, 1f })
        {
            for (float z = -length / 2 + 2; z < length / 2 - 1; z += 3)
            {
                foreach (float y in new[] { 1.2f, 1.8f })
                    p.Box(new(.1f, .08f, 1.0f), new(s * 3.08f, y, z), Pale * .55f);
                p.Box(new(.1f, .6f, .07f), new(s * 3.08f, 1.5f, z), Pale * .55f);
            }
            for (int k = 0; k < 8; k++)
                p.Box(new(.26f, .15f, .28f), new(s * 2.9f, .25f + k * .28f, length / 2 + .06f), Rust);
        }
        p.Cylinder(.22f, .08f, new(1.2f, 4.96f, length * .25f), Steel);
        p.Box(new(.68f, .12f, .68f), new(1.2f, 5.15f, length * .25f), Steel);
        p.Attach(model);
    }

    public static void CampTower(Node3D model, float legs)
    {
        var p = new Parts();
        for (int k = 0; k < 11; k++)
            p.Bar(new(-.3f, .4f + k * (legs - .4f) / 11, 1.1f), new(.3f, .4f + k * (legs - .4f) / 11, 1.1f), .08f, Pale * .5f);
        foreach (float s in new[] { -1f, 1f })
        {
            p.Bar(new(s * .3f, .3f, 1.1f), new(s * .3f, legs - .1f, 1.1f), .09f, Rust);
            p.Bar(new(s * 1.0f, .5f, -1), new(-s * 1.0f, legs - .2f, -1), .12f, Steel);
            p.Box(new(.09f, .45f, 2.5f), new(s * .6f, legs + 1.35f, 0), Rust);
        }
        p.Attach(model);
    }

    public static void FuelTank(Node3D model)
    {
        var p = new Parts();
        foreach (float z in new[] { -1.65f, 1.65f })
        {
            p.Cylinder(1.065f, .08f, new(0, 3.55f, z), Rust, new(90, 0, 0));
            p.Box(new(1.95f, .12f, .15f), new(0, 2.65f, z), Steel);
        }
        // End-cap fasteners and a large readable pressure dial beside the ladder.
        for (int k = 0; k < 8; k++)
        {
            float a = k * MathF.Tau / 8;
            p.Cylinder(.045f, .035f, new(MathF.Cos(a) * .81f, 3.55f + MathF.Sin(a) * .81f, -2.28f), Rust, new(90, 0, 0));
        }
        p.Cylinder(.24f, .10f, new(.55f, 3.8f, -2.3f), Black, new(90, 0, 0));
        p.Cylinder(.19f, .025f, new(.55f, 3.8f, -2.37f), Pale, new(90, 0, 0));
        p.Bar(new(.55f, 3.8f, -2.40f), new(.64f, 3.9f, -2.40f), .025f, Black);
        p.Box(new(.8f, .07f, 2.7f), new(0, 4.59f, -.3f), Snow);
        p.Attach(model);
    }

    public static void Wreck(Node3D truck)
    {
        var p = new Parts();
        // (The cab's right side is open now - the door hangs off its hinge, see LogPiles.Cab - so only the left side keeps its panel.)
        p.Box(new(.035f, .6f, 1.25f), new(-1.22f, .75f, -3.25f), Rust);
        p.Box(new(.05f, .07f, .28f), new(-1.26f, 1.05f, -3f), Pale);
        p.Box(new(.04f, .6f, .8f), new(-1.23f, 1.45f, -3.25f), new(.14f, .18f, .2f));
        foreach (float s in new[] { -1f, 1f })
        {
            foreach (float z in new[] { -4f, 1.4f, 3.1f })
                p.Cylinder(.23f, .04f, new(s * 1.39f, -.55f, z), Rust, new(0, 0, 90));
            p.Box(new(.3f, .22f, .06f), new(s * .85f, .6f, -5.48f), Pale * .6f);
        }
        for (int k = 0; k < 7; k++)
            p.Box(new(.065f, .5f, .035f), new(-.65f + k * .21f, .45f, -5.5f), Steel);
        foreach (float z in new[] { -.9f, 2.7f })
            p.Bar(new(-1.12f, 1.25f, z), new(1.12f, 1.25f, z), .065f, Steel);
        p.Bar(new(-.75f, 1.1f, -4.31f), new(.7f, 1.7f, -4.31f), .035f, Pale * .7f);
        p.Attach(truck);
    }
}
