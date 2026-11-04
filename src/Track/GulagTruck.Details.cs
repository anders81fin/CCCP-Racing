using Godot;

namespace CCCPRacing.Track;

public partial class GulagTruck
{
    void BuildTruckDetails(Node3D body)
    {
        var n = new Node3D { Name = "TruckDetails" };
        body.AddChild(n);
        var steel = new Color(0.25f, 0.26f, 0.22f);
        var dirt = new Color(0.3f, 0.25f, 0.17f);
        var canvasSeam = new Color(0.31f, 0.32f, 0.22f);
        var amber = new Color(0.96f, 0.48f, 0.08f);
        void Box(Node3D parent, Vector3 size, Color color, Vector3 at, Vector3 rot = default)
            => parent.AddChild(Props.Part(Props.Box(size.X, size.Y, size.Z), color, at, rot));
        void B(float x, float y, float z, Color color, float px, float py, float pz, Vector3 rot = default)
            => Box(n, new(x, y, z), color, new(px, py, pz), rot);

        // Split windscreen, wipers, bonnet ribs, side cooling slots and readable lamp guards.
        B(0.055f, 0.57f, 0.045f, OliveDark, 0, 2.5f, -1.83f);
        foreach (float side in new[] { -1f, 1f })
        {
            B(0.035f, 0.36f, 0.035f, Black, side * 0.55f, 2.4f, -1.84f, new(0, 0, -side * 22));
            B(0.045f, 0.025f, 1.35f, OliveDark, side * 0.57f, 2.06f, -2.55f);
            for (int k = 0; k < 5; k++)
                B(0.025f, 0.21f, 0.045f, Black, side * 0.76f, 1.82f, -2.9f + k * 0.15f);
            B(0.31f, 0.04f, 0.06f, steel, side * 0.95f, 1.55f, -3.33f);
            B(0.04f, 0.31f, 0.06f, steel, side * 0.95f, 1.55f, -3.33f);
            B(0.18f, 0.12f, 0.08f, amber, side * 1.04f, 1.81f, -3.16f);
            B(0.2f, 0.06f, 0.09f, steel, side * 0.85f, 1.02f, -3.63f); // tow eyes
            B(0.04f, 0.04f, 0.48f, steel, side * 1.32f, 2.48f, -1.5f); // mirror stay
            B(0.36f, 0.09f, 0.78f, steel, side * 1.26f, 1.24f, -1.03f); // cab step
            for (int k = 0; k < 4; k++)
                B(0.25f, 0.015f, 0.035f, Black, side * 1.3f, 1.29f, -1.3f + k * 0.17f);
            B(0.38f, 0.55f, 1.0f, OliveDark, side * 0.93f, 1.13f, 0.2f); // fuel tanks
            B(0.4f, 0.08f, 0.08f, steel, side * 0.94f, 1.12f, -0.12f);
            B(0.4f, 0.08f, 0.08f, steel, side * 0.94f, 1.12f, 0.52f);
            B(0.5f, 0.42f, 0.06f, Props.Rubber, side * 1.02f, 0.58f, 3.39f); // mud flaps
            B(0.51f, 0.13f, 0.065f, dirt, side * 1.02f, 0.41f, 3.4f);
            B(0.24f, 0.14f, 0.08f, Red, side * 1.02f, 1.35f, 4.13f);
            B(0.09f, 0.14f, 0.085f, amber, side * 0.85f, 1.35f, 4.13f);
            // Dropside hinges, reinforcing battens, canvas seams and hanging ties.
            foreach (float z in new[] { 0.15f, 1.1f, 2.1f, 3.1f, 3.9f })
            {
                B(0.06f, 0.58f, 0.065f, OliveDark, side * 1.27f, 1.86f, z);
                B(0.09f, 0.09f, 0.2f, steel, side * 1.27f, 1.58f, z);
                B(0.035f, 1.25f, 0.045f, canvasSeam, side * 1.26f, 2.86f, z);
                B(0.035f, 0.32f, 0.035f, dirt, side * 1.27f, 2.12f, z, new(side * 7, 0, 0));
            }
            for (int k = 0; k < 3; k++)
                B(0.035f, 0.025f, 4.12f, dirt, side * 1.255f, 1.67f + k * 0.18f, 1.95f);
        }
        B(0.2f, 0.13f, 0.035f, steel, 0, 1.85f, -3.44f); // grille badge
        B(0.6f, 0.25f, 0.04f, Black, 0, 1.02f, -3.6f);
        B(0.12f, 0.15f, 0.18f, steel, 0, 1.05f, 4.17f); // rear hitch
        foreach (float z in new[] { -2.3f, 1.3f, 2.75f })
        {
            B(2.12f, 0.13f, 0.15f, Black, 0, 0.58f, z); // axles/differentials
            n.AddChild(Props.Part(Props.Ball(0.22f, 6), steel, new(0, 0.58f, z)));
            foreach (float side in new[] { -1f, 1f })
            {
                B(0.13f, 0.09f, 0.85f, steel, side * 0.65f, 0.8f, z);
                n.AddChild(Props.Part(Props.Cyl(0.14f, 0.04f, 8), Black, new(side * 1.28f, 0.58f, z), new(0, 0, 90)));
                for (int k = 0; k < 10; k++)
                {
                    float angle = k * Mathf.Tau / 10;
                    B(0.44f, 0.06f, 0.12f, Black, side * 1.05f,
                        0.58f + Mathf.Cos(angle) * 0.57f, z + Mathf.Sin(angle) * 0.57f,
                        new(Mathf.RadToDeg(angle), 0, side * 15));
                }
            }
        }
        // These details move with their respective hinges, rather than floating when opened.
        Box(_door, new(0.06f, 0.055f, 0.19f), Black, new(0.07f, 0.64f, 0.75f));
        Box(body, new(0.06f, 0.055f, 0.19f), Black, new(-1.2f, 2.19f, -0.97f));
        foreach (float x in new[] { -0.95f, 0.95f })
        {
            Box(_tailgate, new(0.12f, 0.57f, 0.035f), OliveDark, new(x, 0.3f, 0.07f));
            Box(_tailgate, new(0.23f, 0.08f, 0.07f), steel, new(x, 0.05f, 0.08f));
        }
        // Stencil marks on the rear plate, visible at the arrest camera distance.
        for (int k = 0; k < 4; k++)
            Box(_tailgate, new(0.045f, 0.15f, 0.015f), Black, new(-0.15f + k * 0.1f, 0.3f, 0.08f));
        DrunkTractor.BatchStatic(n, null);
    }
}
