using Godot;

namespace CCCPRacing.Track;

public partial class GulagTruck
{
    void BuildDriverDetails(Node3D driver)
    {
        var white = new Color(0.94f, 0.94f, 0.88f);
        var blue = new Color(0.12f, 0.19f, 0.42f);
        var seam = new Color(0.24f, 0.38f, 0.7f);
        var n = new Node3D { Name = "RallySuitDetails" };
        driver.AddChild(n);
        void Box(Node3D parent, Vector3 size, Color c, Vector3 at)
            => parent.AddChild(Props.Part(Props.Box(size.X, size.Y, size.Z), c, at));
        // Stand-up fireproof collar, zipper, zipped chest pockets and a red national patch.
        n.AddChild(Props.Part(Props.Cyl(0.105f, 0.1f, 8), blue, new(0, 1.38f, 0)));
        Box(n, new(0.018f, 0.49f, 0.018f), new(0.68f, 0.69f, 0.65f), new(0, 1.08f, -0.145f));
        Box(n, new(0.05f, 0.04f, 0.02f), Black, new(0, 1.27f, -0.155f));
        foreach (float side in new[] { -1f, 1f })
        {
            Box(n, new(0.14f, 0.1f, 0.015f), blue, new(side * 0.13f, 1.17f, -0.142f));
            Box(n, new(0.13f, 0.012f, 0.02f), white, new(side * 0.13f, 1.22f, -0.155f));
            n.AddChild(Props.Part(Props.Ball(0.1f, 6), seam, new(side * 0.19f, 1.28f, 0)));
            Box(n, new(0.2f, 0.025f, 0.025f), white, new(side * 0.12f, 1.3f, -0.14f));
        }
        Box(n, new(0.09f, 0.065f, 0.018f), Red, new(0.14f, 1.07f, -0.155f));
        Box(n, new(0.04f, 0.012f, 0.02f), white, new(0.14f, 1.07f, -0.167f));
        Box(n, new(0.025f, 0.43f, 0.02f), white, new(-0.1f, 1.08f, 0.14f));
        Box(n, new(0.025f, 0.43f, 0.02f), white, new(0.1f, 1.08f, 0.14f));
        DrunkTractor.BatchStatic(n, null);
        foreach (var leg in new[] { _legL, _legR })
        {
            Box(leg, new(0.11f, 0.13f, 0.025f), blue, new(0, -0.34f, -0.1f)); // knee reinforcement
            Box(leg, new(0.025f, 0.61f, 0.02f), white, new(0.07f, -0.34f, -0.03f));
            Box(leg, new(0.16f, 0.12f, 0.19f), blue, new(0, -0.62f, 0)); // tall suede racing boot
            Box(leg, new(0.16f, 0.025f, 0.27f), Black, new(0, -0.75f, -0.045f));
            for (int k = 0; k < 3; k++)
                Box(leg, new(0.09f, 0.012f, 0.012f), white, new(0, -0.59f - k * 0.035f, -0.105f));
        }
        // A curved wraparound visor follows the shell rather than looking like a black box.
        var st = new SurfaceTool(); st.Begin(Mesh.PrimitiveType.Triangles);
        Vector3 Point(float yaw, float pitch) => new(Mathf.Sin(yaw) * Mathf.Cos(pitch) * 0.225f,
            1.62f + Mathf.Sin(pitch) * 0.225f, -Mathf.Cos(yaw) * Mathf.Cos(pitch) * 0.225f);
        for (int x = 0; x < 10; x++)
        for (int y = 0; y < 2; y++)
        {
            float a = Mathf.DegToRad(-68 + x * 13.6f), b = Mathf.DegToRad(-68 + (x + 1) * 13.6f);
            float c = Mathf.DegToRad(-16 + y * 15), d = Mathf.DegToRad(-16 + (y + 1) * 15);
            foreach (Vector3 p in new[] { Point(a, c), Point(b, c), Point(b, d), Point(a, c), Point(b, d), Point(a, d) })
            { st.SetNormal((p - new Vector3(0, 1.62f, 0)).Normalized()); st.AddVertex(p); }
        }
        var visor = Props.Part(st.Commit(), new Color(0.06f, 0.09f, 0.13f), Vector3.Zero);
        // The curved strip is thin; show it from both sides at low chase-camera resolution.
        if (visor.MaterialOverride is StandardMaterial3D mat) mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        driver.AddChild(visor);
        foreach (float side in new[] { -1f, 1f })
        {
            driver.AddChild(Props.Part(Props.Cyl(0.035f, 0.025f, 6), Black, new(side * 0.21f, 1.62f, -0.02f), new(0, 0, 90)));
            Box(driver, new(0.035f, 0.025f, 0.2f), blue, new(side * 0.1f, 1.805f, 0));
        }
        Box(driver, new(0.24f, 0.04f, 0.045f), Black, new(0, 1.45f, -0.205f)); // chin vent
    }
}
