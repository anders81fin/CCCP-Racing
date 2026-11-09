using Godot;

namespace CCCPRacing.Track;

/// <summary>Farm transformer, service entries and a modest roadside lantern.</summary>
public static class VillagePower
{
    static readonly Color Steel = new(0.26f, 0.3f, 0.28f);
    static readonly Color Porcelain = new(0.76f, 0.8f, 0.73f);

    public static Node3D Cabinet(bool compound = false)
    {
        var n = new Node3D();
        n.AddChild(Props.Part(Props.Box(1.2f, 0.25f, 0.85f), new Color(0.48f, 0.48f, 0.45f), new Vector3(0, 0.125f, 0)));
        n.AddChild(Props.Part(Props.Box(0.95f, 1.2f, 0.65f), Steel, new Vector3(0, 0.85f, 0)));
        n.AddChild(Props.Part(Props.Box(0.055f, 0.16f, 0.04f), Porcelain, new Vector3(0.32f, 0.85f, 0.345f)));
        if (compound)
        {
            Color rust = new(0.35f, 0.28f, 0.21f);
            // Open steel paling enclosure: the northern half surrounds the transformer pole.
            foreach (float x in new[] { -3f, 3f })
                for (int z = -5; z <= 3; z++)
                    n.AddChild(Props.Part(Props.Box(0.06f, 1.85f, 0.06f), rust, new Vector3(x, 0.95f, z)));
            foreach (float z in new[] { -5f, 3f })
                for (int x = -3; x <= 3; x++)
                    n.AddChild(Props.Part(Props.Box(0.06f, 1.85f, 0.06f), rust, new Vector3(x, 0.95f, z)));
            foreach (float y in new[] { 0.5f, 1.5f })
            {
                foreach (float x in new[] { -3f, 3f })
                    n.AddChild(Props.Part(Props.Box(0.08f, 0.08f, 8f), Steel, new Vector3(x, y, -1)));
                foreach (float z in new[] { -5f, 3f })
                    n.AddChild(Props.Part(Props.Box(6f, 0.08f, 0.08f), Steel, new Vector3(0, y, z)));
            }
            n.AddChild(Props.Part(Props.Box(0.7f, 0.55f, 0.04f), new Color(0.85f, 0.65f, 0.12f), new Vector3(0, 1.3f, 3.06f)));
            n.AddChild(Props.Part(Props.Box(0.08f, 0.38f, 0.05f), Steel, new Vector3(0, 1.3f, 3.09f), new Vector3(0, 0, -25)));
            DrunkTractor.BatchStatic(n, null);
        }
        return n;
    }

    public static Node3D CableEntry(Vector3 entry, bool pole)
    {
        var n = new Node3D();
        float h = entry.Y;
        n.AddChild(Props.Part(Props.Cyl(0.06f, h, 6), Steel, new Vector3(entry.X, h / 2, entry.Z)));
        if (!pole)
            n.AddChild(Props.Part(Props.Box(0.32f, 0.45f, 0.18f), Steel, entry));
        return n;
    }

    public static Node3D ServiceMast(string type, Vector3 anchor)
    {
        var n = new Node3D();
        float length = type == "izba" ? 2.8f : type == "gaiservice" ? 3.2f : 1.1f;
        n.AddChild(Props.Part(Props.Cyl(0.045f, length, 6), Steel, anchor - Vector3.Up * (length / 2f)));
        n.AddChild(Props.Part(Props.Cyl(0.1f, 0.14f, 6), Porcelain, anchor));
        n.AddChild(Props.Part(Props.Box(0.22f, 0.3f, 0.14f), Steel, anchor - Vector3.Up * (length - 0.15f)));
        return n;
    }

    // An underground incoming feeder rises in the protective conduit to this local transformer.
    public static Node3D Transformer()
    {
        var n = new Node3D();
        n.AddChild(Props.Part(Props.Box(1f, 1.25f, 0.72f), new Color(0.4f, 0.44f, 0.4f), new Vector3(0, 4.9f, 0.5f)));
        foreach (float x in new[] { -0.38f, -0.19f, 0f, 0.19f, 0.38f })
            n.AddChild(Props.Part(Props.Box(0.055f, 1.03f, 0.18f), Steel, new Vector3(x, 4.9f, 0.91f)));
        n.AddChild(Props.Part(Props.Box(1.2f, 0.12f, 0.92f), Steel, new Vector3(0, 4.2f, 0.4f)));
        foreach (float x in new[] { -0.3f, 0f, 0.3f })
            n.AddChild(Props.Part(Props.Cyl(0.08f, 0.25f, 6), Porcelain, new Vector3(x, 5.65f, 0.5f)));
        n.AddChild(Props.Part(Props.Cyl(0.055f, 4.3f, 6), Steel, new Vector3(0.2f, 2.15f, 0.25f)));
        n.AddChild(Props.Part(Props.Box(0.42f, 0.65f, 0.26f), Steel, new Vector3(0.2f, 0.65f, 0.3f)));
        // Short insulated riser from the transformer output to the crossarm.
        n.AddChild(Props.Part(Props.Cyl(0.035f, 1.15f, 6), new Color(0.12f, 0.12f, 0.12f), new Vector3(0, 6.2f, 0.5f)));
        return n;
    }

    /// <summary>A low lantern post for the dacha pond's bank: a round warm lantern on a short pole, lit at night (one shadow-free light over the water's edge).</summary>
    public static Node3D PondLamp()
    {
        var n = new Node3D();
        var warm = new Color(1f, 0.74f, 0.4f);
        n.AddChild(Props.Part(Props.Cyl(0.07f, 3.4f, 8), Steel, new Vector3(0, 1.7f, 0)));
        n.AddChild(Props.Part(Props.Cyl(0.16f, 0.4f, 8), new Color(0.57f, 0.54f, 0.47f), new Vector3(0, 0.2f, 0)));
        n.AddChild(Props.Part(Props.Cyl(0.2f, 0.06f, 8), Steel, new Vector3(0, 3.42f, 0)));
        n.AddChild(NightGlow.Part(Props.Ball(0.26f), new Color(0.9f, 0.86f, 0.7f), warm, 3f, new Vector3(0, 3.7f, 0)));
        n.AddChild(Props.Part(Props.Cyl(0.04f, 0.14f, 8, 0.28f), Steel, new Vector3(0, 4.02f, 0)));
        var light = new OmniLight3D
        {
            Name = "PondLight", Position = new Vector3(0, 3.7f, 0),
            LightColor = warm, LightEnergy = 0, OmniRange = 15f, OmniAttenuation = 1.2f,
            ShadowEnabled = false, LightSpecular = 0, Visible = false,
        };
        light.SetMeta("night_energy", 6f);
        n.AddChild(light);
        return n;
    }

    public static Node3D StreetLamp()
    {
        var n = new Node3D();
        var warm = new Color(1f, 0.79f, 0.46f);
        n.AddChild(Props.Part(Props.Cyl(0.11f, 5.7f, 8), Steel, new Vector3(0, 2.85f, 0)));
        n.AddChild(Props.Part(Props.Cyl(0.2f, 0.55f, 8), new Color(0.57f, 0.54f, 0.47f), new Vector3(0, 0.27f, 0)));
        n.AddChild(Props.Part(Props.Box(0.1f, 0.1f, 2.05f), Steel, new Vector3(0, 5.6f, -0.95f)));
        n.AddChild(Props.Part(Props.Box(0.48f, 0.18f, 0.7f), Steel, new Vector3(0, 5.5f, -1.8f)));
        n.AddChild(NightGlow.Part(Props.Box(0.36f, 0.22f, 0.58f), new Color(0.88f, 0.84f, 0.68f), warm, 2.8f, new Vector3(0, 5.28f, -1.8f)));
        n.AddChild(Props.Part(Props.Cyl(0.09f, 0.16f, 6), Porcelain, new Vector3(0, 5.5f, 0)));
        // A single shadow-free light illuminates the shelter, waiting area and roadside.
        var light = new OmniLight3D
        {
            Name = "StopLight", Position = new Vector3(0, 4.8f, -1.8f),
            LightColor = warm, LightEnergy = 0, OmniRange = 14f, OmniAttenuation = 1.2f,
            ShadowEnabled = false, LightSpecular = 0, Visible = false,
        };
        light.SetMeta("night_energy", 5f);
        n.AddChild(light);
        return n;
    }
}
