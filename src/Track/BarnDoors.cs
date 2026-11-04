using Godot;

namespace CCCPRacing.Track;

/// <summary>Two hinged leaves at the road-facing end of a tractor barn.</summary>
public partial class BarnDoors : Node3D
{
    readonly Node3D _left = new(), _right = new();

    public BarnDoors(float width, float height, float front, Color wood, Color trim)
    {
        Name = "BarnDoors";
        float half = width / 2f;
        foreach (var (hinge, side) in new[] { (_left, -1f), (_right, 1f) })
        {
            hinge.Position = new Vector3(side * half, 0, front + 0.12f);
            AddChild(hinge);
            float leaf = half - 0.06f;
            Vector3 center = new(-side * leaf / 2f, height / 2f, 0);
            hinge.AddChild(Props.Part(Props.Box(leaf, height - 0.1f, 0.17f), wood, center));
            for (float x = 0.45f; x < leaf; x += 0.55f)
                hinge.AddChild(Props.Part(Props.Box(0.07f, height - 0.14f, 0.20f), trim,
                    center + new Vector3(side * (x - leaf / 2f), 0, 0.02f)));
            foreach (float y in new[] { 0.5f, height - 0.5f })
                hinge.AddChild(Props.Part(Props.Box(leaf - 0.1f, 0.13f, 0.21f), trim,
                    new Vector3(center.X, y, 0.02f)));
            hinge.AddChild(Props.Part(Props.Box(0.11f, 0.3f, 0.12f), trim,
                new Vector3(-side * (leaf - 0.3f), height / 2f, 0.17f)));
        }
    }

    /// <param name="amount">0 closed, 1 fully open.</param>
    public void SetOpen(float amount)
    {
        float angle = Mathf.DegToRad(88f) * Mathf.Clamp(amount, 0f, 1f);
        _left.Rotation = new Vector3(0, -angle, 0);
        _right.Rotation = new Vector3(0, angle, 0);
    }
}
