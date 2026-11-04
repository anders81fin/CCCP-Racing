using Godot;

namespace CCCPRacing.Track;

/// <summary>A weathered single-axle slurry tanker: straps, hatch, pump, hose and rear spreader.</summary>
public static class SlurryTanker
{
    public static Node3D Build()
    {
        var root = new Node3D { Name = "OchreSlurryTanker" };
        Color ochre = new(0.66f, 0.51f, 0.21f), frame = new(0.23f, 0.29f, 0.18f), steel = new(0.44f, 0.45f, 0.40f);
        Color rust = new(0.34f, 0.20f, 0.10f), dirt = new(0.27f, 0.20f, 0.10f);
        void Box(float x, float y, float z, Vector3 at, Color color) => root.AddChild(Props.Part(Props.Box(x, y, z), color, at));
        void Part(PrimitiveMesh mesh, Color color, Vector3 at, Vector3 rotation = default) => root.AddChild(Props.Part(mesh, color, at, rotation));
        Box(0.16f, 0.16f, 2.6f, new Vector3(0, 0.65f, -1.5f), frame); // hitch reaches tractor drawbar
        foreach (float side in new[] { -1f, 1f })
        {
            Box(0.13f, 0.20f, 3.7f, new Vector3(side * 0.68f, 0.72f, 0), frame);
            Box(1.25f, 0.12f, 0.18f, new Vector3(0, 0.75f, side * 1.4f), steel);
            Part(Props.Cyl(0.67f, 0.42f, 12), Props.Rubber, new Vector3(side * 1.03f, 0.67f, 0.35f), new Vector3(0, 0, 90));
            Part(Props.Cyl(0.34f, 0.46f, 10), frame, new Vector3(side * 1.07f, 0.67f, 0.35f), new Vector3(0, 0, 90));
            Part(Props.Cyl(0.13f, 0.49f, 8), steel, new Vector3(side * 1.09f, 0.67f, 0.35f), new Vector3(0, 0, 90));
            Box(0.55f, 0.10f, 1.6f, new Vector3(side * 1.02f, 1.40f, 0.35f), frame);
            Box(0.42f, 0.48f, 0.06f, new Vector3(side * 1.02f, 0.50f, 1.10f), dirt);
            Box(0.17f, 0.16f, 0.08f, new Vector3(side * 0.9f, 0.85f, 1.88f), new Color(0.60f, 0.08f, 0.05f));
        }
        Box(2.35f, 0.14f, 0.16f, new Vector3(0, 0.66f, 0.35f), steel);
        // The tank axis runs along Z, with rounded end caps and two steel retaining straps.
        Part(Props.Cyl(0.93f, 3.2f, 16), ochre, new Vector3(0, 1.65f, 0), new Vector3(90, 0, 0));
        foreach (float z in new[] { -1.6f, 1.6f })
        {
            var end = Props.Part(Props.Ball(0.93f, 16), ochre.Darkened(0.05f), new Vector3(0, 1.65f, z));
            end.Scale = new Vector3(1, 1, 0.24f); root.AddChild(end);
        }
        foreach (float z in new[] { -1.05f, 1.05f })
            Part(new TorusMesh { InnerRadius = 0.90f, OuterRadius = 0.96f, Rings = 16, RingSegments = 4 }, steel,
                new Vector3(0, 1.65f, z), new Vector3(90, 0, 0));
        Part(Props.Cyl(0.27f, 0.18f, 12), steel, new Vector3(0, 2.62f, -0.45f));
        Part(Props.Cyl(0.31f, 0.07f, 12), frame, new Vector3(0, 2.75f, -0.45f));
        Box(0.24f, 0.07f, 0.06f, new Vector3(0, 2.81f, -0.45f), steel);
        // Side hose and ladder read clearly from the high game camera.
        Part(Props.Cyl(0.095f, 2.55f, 8), dirt, new Vector3(0.91f, 1.65f, 0.05f), new Vector3(90, 0, 0));
        foreach (float z in new[] { -0.75f, -0.15f })
            Box(0.06f, 1.25f, 0.06f, new Vector3(-0.98f, 1.78f, z), steel);
        for (int i = 0; i < 5; i++) Box(0.07f, 0.05f, 0.65f, new Vector3(-1.0f, 1.23f + i * 0.25f, -0.45f), steel);
        Part(Props.Cyl(0.22f, 0.42f, 10), frame, new Vector3(0.35f, 0.9f, -1.55f), new Vector3(0, 0, 90));
        Part(Props.Cyl(0.13f, 0.62f, 8), rust, new Vector3(0, 1.22f, 1.84f));
        Part(Props.Cyl(0.15f, 0.65f, 8), steel, new Vector3(0, 0.9f, 2.05f), new Vector3(90, 0, 0));
        Box(0.65f, 0.06f, 0.30f, new Vector3(0, 0.68f, 2.25f), rust); // splash plate
        Part(new TorusMesh { InnerRadius = 0.13f, OuterRadius = 0.19f, Rings = 8, RingSegments = 4 }, rust,
            new Vector3(0, 1.6f, 1.99f), new Vector3(90, 0, 0)); // valve wheel
        // Visible stains beneath hatch and outlet rather than uniform brown paint.
        Box(0.13f, 0.62f, 0.025f, new Vector3(0.14f, 1.05f, 1.84f), dirt);
        Box(0.20f, 0.26f, 0.025f, new Vector3(-0.10f, 1.19f, 1.84f), rust);
        for (int i = 0; i < 5; i++)
            Box(0.03f, 0.12f + i * 0.04f, 0.19f, new Vector3(-0.925f, 1.65f - i * 0.06f, -0.65f + i * 0.1f), dirt);
        DrunkTractor.BatchStatic(root, null);
        return root;
    }
}
