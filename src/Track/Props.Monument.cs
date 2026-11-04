using Godot;

namespace CCCPRacing.Track;

public static partial class Props
{
    /// <summary>Broad sculptural shapes and an open gesture read from the elevated race camera.</summary>
    static void AddIlyichFigure(Node3D solid)
    {
        var bronze = new Color(0.43f, 0.38f, 0.25f);
        var light = new Color(0.56f, 0.49f, 0.32f);
        var dark = new Color(0.25f, 0.27f, 0.21f);
        void B(Vector3 size, Color color, Vector3 at, Vector3 rotation = default) =>
            solid.AddChild(Part(Box(size.X, size.Y, size.Z), color, at, rotation));
        void Oval(Vector3 size, Color color, Vector3 at)
        {
            var part = Part(Ball(0.5f, 10), color, at);
            part.Scale = size;
            solid.AddChild(part);
        }
        void Limb(Vector3 from, Vector3 to, float radius, Color color, float tip = -1)
        {
            var part = Part(Cyl(radius, from.DistanceTo(to), 8, tip < 0 ? radius : tip), color, (from + to) / 2);
            part.Quaternion = new Quaternion(Vector3.Up, (to - from).Normalized());
            solid.AddChild(part);
        }
        // Staggered feet and separate trouser legs, with a visible gap beneath the coat.
        B(new Vector3(0.43f, 0.25f, 0.79f), dark, new Vector3(-0.34f, 2.73f, -0.22f));
        B(new Vector3(0.43f, 0.25f, 0.72f), dark, new Vector3(0.34f, 2.73f, 0.12f));
        Limb(new Vector3(-0.34f, 2.85f, -0.1f), new Vector3(-0.28f, 4.23f, 0), 0.22f, bronze, 0.26f);
        Limb(new Vector3(0.34f, 2.85f, 0.2f), new Vector3(0.28f, 4.23f, 0), 0.22f, bronze, 0.26f);
        // A flared jacket, broad chest and sloping shoulders rather than a thin column.
        var coat = Part(Cyl(0.72f, 1.15f, 10, 0.53f), bronze, new Vector3(0, 4.43f, 0));
        coat.Scale = new Vector3(1, 1, 0.65f);
        solid.AddChild(coat);
        Oval(new Vector3(1.24f, 1.12f, 0.69f), bronze, new Vector3(0, 5.15f, 0));
        Limb(new Vector3(-0.68f, 5.4f, 0), new Vector3(0.65f, 5.43f, 0), 0.3f, bronze);
        // Shirt, tie and wide diagonal lapels create a readable suit front.
        B(new Vector3(0.27f, 0.65f, 0.12f), light, new Vector3(0, 5.25f, -0.36f));
        B(new Vector3(0.11f, 0.47f, 0.08f), dark, new Vector3(0, 5.2f, -0.44f));
        B(new Vector3(0.2f, 0.8f, 0.13f), light, new Vector3(-0.25f, 5.18f, -0.36f), new Vector3(0, 0, -27));
        B(new Vector3(0.2f, 0.8f, 0.13f), light, new Vector3(0.25f, 5.18f, -0.36f), new Vector3(0, 0, 27));
        B(new Vector3(0.045f, 0.95f, 0.055f), dark, new Vector3(0, 4.42f, -0.46f));
        B(new Vector3(0.3f, 0.07f, 0.07f), dark, new Vector3(-0.4f, 4.65f, -0.43f));
        foreach (float y in new[] { 4.45f, 4.72f }) Oval(new Vector3(0.1f, 0.1f, 0.08f), light, new Vector3(0.12f, y, -0.47f));
        // Bald dome, ears, brow, projecting nose and a wedge-shaped goatee: Lenin's profile.
        Limb(new Vector3(0, 5.52f, 0), new Vector3(0, 5.95f, -0.04f), 0.2f, bronze);
        Oval(new Vector3(0.86f, 0.94f, 0.8f), bronze, new Vector3(0, 6.19f, -0.06f));
        Oval(new Vector3(0.7f, 0.46f, 0.52f), light, new Vector3(0, 6.47f, -0.17f));
        foreach (float x in new[] { -0.43f, 0.43f }) Oval(new Vector3(0.17f, 0.27f, 0.16f), bronze, new Vector3(x, 6.16f, -0.02f));
        B(new Vector3(0.61f, 0.1f, 0.12f), dark, new Vector3(0, 6.26f, -0.43f));
        B(new Vector3(0.18f, 0.23f, 0.24f), light, new Vector3(0, 6.12f, -0.5f), new Vector3(-12, 0, 0));
        B(new Vector3(0.37f, 0.08f, 0.12f), dark, new Vector3(0, 5.99f, -0.46f));
        var beard = Part(Cyl(0.08f, 0.3f, 6, 0.22f), dark, new Vector3(0, 5.83f, -0.39f));
        beard.Scale = new Vector3(1, 1, 0.65f);
        solid.AddChild(beard);
        // The raised arm opens out to the side; its elbow, cuff and hand remain distinct in projection.
        Vector3 shoulder = new(0.65f, 5.38f, 0), elbow = new(1.22f, 5.62f, -0.36f), wrist = new(1.86f, 6.03f, -0.63f);
        Limb(shoulder, elbow, 0.25f, bronze, 0.21f);
        Oval(new Vector3(0.43f, 0.43f, 0.43f), bronze, elbow);
        Limb(elbow, wrist, 0.21f, bronze, 0.16f);
        Limb(wrist, new Vector3(2.02f, 6.13f, -0.7f), 0.17f, dark);
        Oval(new Vector3(0.39f, 0.23f, 0.31f), light, new Vector3(2.08f, 6.17f, -0.74f));
        Limb(new Vector3(2.16f, 6.2f, -0.77f), new Vector3(2.47f, 6.37f, -0.9f), 0.075f, light, 0.05f);
        // The other hand holds a broad worker's cap away from the coat silhouette.
        Limb(new Vector3(-0.65f, 5.37f, 0), new Vector3(-0.95f, 4.7f, -0.04f), 0.24f, bronze, 0.19f);
        Limb(new Vector3(-0.95f, 4.7f, -0.04f), new Vector3(-1.03f, 4.27f, -0.26f), 0.19f, bronze, 0.14f);
        Oval(new Vector3(0.26f, 0.3f, 0.25f), light, new Vector3(-1.03f, 4.2f, -0.26f));
        Oval(new Vector3(0.68f, 0.29f, 0.58f), dark, new Vector3(-1.04f, 3.99f, -0.24f));
        B(new Vector3(0.57f, 0.08f, 0.32f), bronze, new Vector3(-1.04f, 3.93f, -0.54f), new Vector3(-12, 0, 0));
    }
}
