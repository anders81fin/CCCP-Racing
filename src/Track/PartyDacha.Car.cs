using Godot;

namespace CCCPRacing.Track;

public static partial class PartyDacha
{
    /// <summary>A fictional late-Soviet official saloon, modelled at the existing parked-car scale.</summary>
    static Node3D OfficialCar(Vector3 position, bool finnishFlags = false, float yaw = 0)
    {
        var root = new Node3D { Name = "OfficialSaloon", Position = position, Rotation = new Vector3(0, yaw, 0) };
        Color paint = new(0.065f, 0.080f, 0.078f), highlight = new(0.105f, 0.125f, 0.119f);
        Color rubber = new(0.035f, 0.04f, 0.038f), chrome = new(0.61f, 0.65f, 0.62f);
        Color glass = new(0.20f, 0.32f, 0.34f), lamps = new(0.84f, 0.80f, 0.59f);
        void Box(Vector3 size, Vector3 at, Color color, Vector3 rotation = default) =>
            root.AddChild(Props.Part(Props.Box(size.X, size.Y, size.Z), color, at, rotation));
        void Cylinder(float radius, float depth, Vector3 at, Color color, Vector3 rotation) =>
            root.AddChild(Props.Part(Props.Cyl(radius, depth, 16), color, at, rotation));
        // Tapered surfaces give the bonnet, boot and cabin their own silhouette.
        void Loft(float bottomWidth, float topWidth, float bottomY, float topY,
            float bottomFront, float bottomBack, float topFront, float topBack, Color color)
        {
            Vector3[] v = {
                new(-bottomWidth, bottomY, bottomFront), new(bottomWidth, bottomY, bottomFront),
                new(bottomWidth, bottomY, bottomBack), new(-bottomWidth, bottomY, bottomBack),
                new(-topWidth, topY, topFront), new(topWidth, topY, topFront),
                new(topWidth, topY, topBack), new(-topWidth, topY, topBack),
            };
            var mesh = new SurfaceTool(); mesh.Begin(Mesh.PrimitiveType.Triangles);
            void Face(int a, int b, int c, int d)
            {
                foreach (int index in new[] { a, c, b, a, d, c }) mesh.AddVertex(v[index]);
            }
            Face(0, 1, 5, 4); Face(1, 2, 6, 5); Face(2, 3, 7, 6); Face(3, 0, 4, 7); Face(4, 5, 6, 7); Face(3, 2, 1, 0);
            mesh.GenerateNormals();
            root.AddChild(new MeshInstance3D { Mesh = mesh.Commit(), MaterialOverride = Props.Mat(color) });
        }
        Box(new Vector3(1.62f, 0.25f, 4.6f), new Vector3(0, 0.53f, 0), rubber);
        Loft(1.01f, 0.97f, 0.70f, 1.23f, -2.42f, 2.42f, -2.34f, 2.32f, paint);
        Loft(0.96f, 0.90f, 1.23f, 1.34f, -2.33f, -1.12f, -2.23f, -1.10f, highlight);
        Loft(0.96f, 0.91f, 1.23f, 1.32f, 1.36f, 2.32f, 1.37f, 2.23f, highlight);
        Loft(0.91f, 0.77f, 1.28f, 1.94f, -1.12f, 1.37f, -0.80f, 1.07f, glass);
        Loft(0.79f, 0.75f, 1.94f, 2.03f, -0.83f, 1.10f, -0.77f, 1.04f, paint);
        foreach (float side in new[] { -1f, 1f })
        {
            // Window pillars, chrome gutters and sills divide four separate doors and windows.
            Box(new Vector3(0.065f, 0.075f, 1.90f), new Vector3(side * 0.79f, 1.94f, 0.13f), chrome);
            Box(new Vector3(0.055f, 0.065f, 2.55f), new Vector3(side * 0.925f, 1.29f, 0.12f), chrome);
            Box(new Vector3(0.08f, 0.73f, 0.085f), new Vector3(side * 0.85f, 1.60f, -0.96f), chrome, new Vector3(26, 0, side * 12));
            Box(new Vector3(0.10f, 0.70f, 0.13f), new Vector3(side * 0.85f, 1.60f, 0.14f), paint, new Vector3(0, 0, side * 12));
            Box(new Vector3(0.10f, 0.73f, 0.16f), new Vector3(side * 0.85f, 1.60f, 1.22f), chrome, new Vector3(-25, 0, side * 12));
            Box(new Vector3(0.04f, 0.055f, 4.42f), new Vector3(side * 1.0f, 0.96f, 0), chrome);
            Box(new Vector3(0.045f, 0.055f, 2.6f), new Vector3(side * 1.01f, 0.68f, 0.1f), chrome);
            foreach (float z in new[] { -1.02f, 0.14f, 1.35f })
                Box(new Vector3(0.025f, 0.48f, 0.025f), new Vector3(side * 1.005f, 0.98f, z), rubber);
            foreach (float z in new[] { -0.03f, 1.15f })
                Box(new Vector3(0.065f, 0.065f, 0.24f), new Vector3(side * 1.04f, 1.16f, z), chrome);
            Box(new Vector3(0.22f, 0.05f, 0.07f), new Vector3(side * 1.06f, 1.39f, -0.90f), chrome);
            Box(new Vector3(0.11f, 0.18f, 0.29f), new Vector3(side * 1.17f, 1.47f, -0.90f), chrome);
            foreach (float z in new[] { -1.57f, 1.59f })
            {
                Cylinder(0.43f, 0.26f, new Vector3(side * 0.99f, 0.43f, z), rubber, new Vector3(0, 0, 90));
                Cylinder(0.29f, 0.035f, new Vector3(side * 1.135f, 0.43f, z), chrome, new Vector3(0, 0, 90));
                Cylinder(0.16f, 0.045f, new Vector3(side * 1.16f, 0.43f, z), paint, new Vector3(0, 0, 90));
                Cylinder(0.09f, 0.05f, new Vector3(side * 1.18f, 0.43f, z), chrome, new Vector3(0, 0, 90));
                // Thin polished eyebrow above each wheel and a mud flap behind it.
                Box(new Vector3(0.06f, 0.045f, 0.83f), new Vector3(side * 1.02f, 0.91f, z), chrome);
                Box(new Vector3(0.26f, 0.34f, 0.06f), new Vector3(side * 0.98f, 0.35f, z + 0.42f), rubber);
            }
            foreach (float x in new[] { 0.61f, 0.84f })
            {
                Cylinder(0.135f, 0.065f, new Vector3(side * x, 1.06f, -2.43f), chrome, new Vector3(90, 0, 0));
                Cylinder(0.103f, 0.075f, new Vector3(side * x, 1.06f, -2.45f), lamps, new Vector3(90, 0, 0));
            }
            Box(new Vector3(0.29f, 0.075f, 0.07f), new Vector3(side * 0.76f, 0.86f, -2.44f), new Color(0.77f, 0.42f, 0.12f));
            Box(new Vector3(0.48f, 0.24f, 0.09f), new Vector3(side * 0.68f, 1.02f, 2.43f), chrome);
            Box(new Vector3(0.39f, 0.17f, 0.11f), new Vector3(side * 0.70f, 1.02f, 2.45f), new Color(0.55f, 0.075f, 0.045f));
            Box(new Vector3(0.035f, 0.015f, 1.1f), new Vector3(side * 0.69f, 1.347f, -1.68f), rubber);
        }
        // Wide radiator grille with individual upright bars and a small bonnet ornament.
        Box(new Vector3(1.05f, 0.37f, 0.10f), new Vector3(0, 1.04f, -2.44f), chrome);
        Box(new Vector3(0.96f, 0.29f, 0.12f), new Vector3(0, 1.04f, -2.46f), rubber);
        for (float x = -0.43f; x <= 0.44f; x += 0.085f)
            Box(new Vector3(0.025f, 0.28f, 0.14f), new Vector3(x, 1.04f, -2.47f), chrome);
        Box(new Vector3(0.07f, 0.16f, 0.035f), new Vector3(0, 1.43f, -2.0f), chrome);
        foreach (float z in new[] { -2.49f, 2.49f })
        {
            Box(new Vector3(2.12f, 0.16f, 0.18f), new Vector3(0, 0.69f, z), chrome);
            Box(new Vector3(1.96f, 0.045f, 0.19f), new Vector3(0, 0.70f, z), rubber);
            Box(new Vector3(0.47f, 0.19f, 0.035f), new Vector3(0, 0.89f, z * 1.012f), rubber);
            foreach (float x in new[] { -0.14f, -0.04f, 0.07f, 0.15f })
                Box(new Vector3(0.035f, 0.085f, 0.04f), new Vector3(x, 0.89f, z * 1.022f), lamps);
        }
        Box(new Vector3(0.045f, 0.025f, 0.56f), new Vector3(-0.43f, 1.39f, -1.02f), rubber, new Vector3(0, -20, 0));
        Box(new Vector3(0.045f, 0.025f, 0.56f), new Vector3(0.36f, 1.39f, -1.02f), rubber, new Vector3(0, -20, 0));
        Box(new Vector3(0.10f, 0.10f, 0.36f), new Vector3(-0.72f, 0.38f, 2.42f), chrome);
        if (finnishFlags)
        {
            Color white = new(0.92f, 0.92f, 0.85f), blue = new(0.025f, 0.18f, 0.49f);
            foreach (float side in new[] { -1f, 1f })
            {
                // Small diplomatic pennant staffs on the front wings. Horizontal flags remain
                // legible from the overhead camera, with a correctly offset Nordic cross.
                Cylinder(0.025f, 1.0f, new Vector3(side * 0.96f, 1.85f, -1.90f), chrome, Vector3.Zero);
                Cylinder(0.045f, 0.06f, new Vector3(side * 0.96f, 2.37f, -1.90f), chrome, Vector3.Zero);
                var flag = new Node3D { Position = new Vector3(side * 0.96f, 2.29f, -1.90f), RotationDegrees = new Vector3(32, side * -12, 0) };
                root.AddChild(flag);
                void Cloth(Vector3 size, Vector3 at, Color color) => flag.AddChild(Props.Part(Props.Box(size.X, size.Y, size.Z), color, at));
                Cloth(new Vector3(0.018f, 0.50f, 0.82f), new Vector3(0, -0.25f, 0.41f), white);
                Cloth(new Vector3(0.025f, 0.50f, 0.14f), new Vector3(0, -0.25f, 0.30f), blue);
                Cloth(new Vector3(0.028f, 0.14f, 0.82f), new Vector3(0, -0.25f, 0.41f), blue);
            }
        }
        return root; // Grounds batches the car together with the remaining fixed estate details.
    }
}
