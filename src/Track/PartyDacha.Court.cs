using System;
using System.Collections.Generic;
using Godot;

namespace CCCPRacing.Track;

public static partial class PartyDacha
{
    /// <summary>The ceremonial entrance court: a paved roundabout in front of the gate with a flower rondel and flagpole, urns on a kerbed rim, flagpoles, the gate's iron leaves, a guard booth and a striped barrier.</summary>
    public static readonly Vector2 CourtC = new(246f, 176f);
    public const float CourtR = 8.2f;
    public static bool InCourt(Vector2 p, float margin = 0f) => p.DistanceTo(CourtC) < CourtR + margin;
    public static readonly Vector2 BoothAt = new(259.5f, 184.5f);
    public static readonly Vector2[] PorchPath = { new(240f, 176f), new(234.4f, 176f) };      // the paved way from the court to the porch steps
    public static readonly Vector2[] BoothPath = { new(259.5f, 178.5f), new(259.5f, 183.2f) };

    static IEnumerable<(Vector2 At, float Radius)> CourtSolids()
    {
        yield return (CourtC, 3.3f);                                                    // the rondel
        foreach (var u in UrnSites()) yield return (u, 0.5f);
        for (float dx = -1.0f; dx <= 1.01f; dx += 1.0f) for (float dz = -1.0f; dz <= 1.01f; dz += 1.0f) yield return (BoothAt + new Vector2(dx, dz), 0.9f);      // the booth
    }

    static IEnumerable<Vector2> UrnSites()
    {
        for (int k = 0; k < 12; k++)
        {
            float a = k * MathF.Tau / 12f + 0.13f; var d = new Vector2(MathF.Cos(a), MathF.Sin(a));
            if (Math.Abs(Mathf.AngleDifference(a, 0f)) < 0.6f || Math.Abs(Mathf.AngleDifference(a, MathF.PI)) < 0.45f || Math.Abs(Mathf.AngleDifference(a, MathF.PI / 2f)) < 0.6f) continue;      // keep the entrance and the porch side open
            yield return CourtC + d * (CourtR - 0.6f);
        }
    }

    static void BuildCourt(Node3D objects, Func<Vector2, float, Vector3> At)
    {
        var rng = new RandomNumberGenerator { Seed = 8080 };
        void Box(Vector3 size, Vector3 at, Color col, Vector3 rot = default) => objects.AddChild(Props.Part(Props.Box(size.X, size.Y, size.Z), col, at, rot));
        void Cyl(float r, float h, Vector3 at, Color col, int seg = 12, float top = -1, Vector3 rot = default) => objects.AddChild(Props.Part(Props.Cyl(r, h, seg, top), col, at, rot));
        void Ball(float r, Vector3 at, Color col, Vector3? scale = null) { var b = Props.Part(Props.Ball(r, 6), col, at); if (scale != null) b.Scale = scale.Value; objects.AddChild(b); }
        Color Tone(Color c, float v) { float k = 1f + rng.RandfRange(-v, v); return new Color(c.R * k, c.G * k, c.B * k); }
        var stone = new Color(0.74f, 0.73f, 0.68f); var dark = new Color(0.5f, 0.5f, 0.47f); var gold = new Color(0.88f, 0.72f, 0.2f); var iron = new Color(0.1f, 0.11f, 0.12f);
        var red = new Color(0.78f, 0.1f, 0.08f); var yellow = new Color(0.95f, 0.78f, 0.15f); var white = new Color(0.93f, 0.92f, 0.88f);
        // The kerbed rim: stone blocks round the court, open at the gate and at the porch side.
        for (int k = 0; k < 44; k++)
        {
            float a = k * MathF.Tau / 44f;
            if (Math.Abs(Mathf.AngleDifference(a, 0f)) < 0.42f || Math.Abs(Mathf.AngleDifference(a, MathF.PI)) < 0.34f || Math.Abs(Mathf.AngleDifference(a, MathF.PI / 2f)) < 0.45f) continue;      // gaps at the gate, the porch side and the way to the car park
            Vector2 p = CourtC + new Vector2(MathF.Cos(a), MathF.Sin(a)) * (CourtR + 0.1f);
            Box(new Vector3(1.2f, 0.2f, 0.34f), At(p, 0.1f), Tone(stone, 0.07f), new Vector3(0, -Mathf.RadToDeg(a) + 90f, 0));
        }
        // The rondel: a kerb of twelve cut stones, dark soil, concentric blooms, a stepped plinth and a flagpole with a flag and a gilded ball.
        for (int k = 0; k < 14; k++)
        {
            float a = k * MathF.Tau / 14f; Vector2 p = CourtC + new Vector2(MathF.Cos(a), MathF.Sin(a)) * 3.05f;
            Box(new Vector3(1.45f, 0.4f, 0.5f), At(p, 0.2f), Tone(stone, 0.06f), new Vector3(0, -Mathf.RadToDeg(a) + 90f, 0));
            Box(new Vector3(1.5f, 0.07f, 0.56f), At(p, 0.43f), dark, new Vector3(0, -Mathf.RadToDeg(a) + 90f, 0));
        }
        Cyl(2.8f, 0.12f, At(CourtC, 0.06f), new Color(0.22f, 0.16f, 0.1f), 18);
        var ringColours = new[] { red, yellow, white, red, yellow };
        for (int ring = 0; ring < ringColours.Length; ring++)
        {
            float r = 2.55f - ring * 0.5f; int count = (int)(r * 7f);
            for (int k = 0; k < count; k++)
            {
                float a = k * MathF.Tau / count + ring * 0.3f;
                Ball(rng.RandfRange(0.1f, 0.15f), At(CourtC + new Vector2(MathF.Cos(a), MathF.Sin(a)) * r, rng.RandfRange(0.2f, 0.3f)), Tone(ringColours[ring], 0.1f), new Vector3(1f, 0.85f, 1f));
            }
        }
        for (int k = 0; k < 3; k++) Cyl(0.95f - k * 0.2f, 0.28f, At(CourtC, 0.2f + k * 0.28f), k % 2 == 0 ? stone : dark, 12);
        Cyl(0.09f, 9.2f, At(CourtC, 4.9f), new Color(0.82f, 0.82f, 0.84f), 8, 0.06f);
        Ball(0.17f, At(CourtC, 9.6f), gold);
        Box(new Vector3(2.3f, 1.4f, 0.04f), At(CourtC + new Vector2(1.2f, 0.04f), 8.55f), red, new Vector3(0, 0, -4));
        Box(new Vector3(0.36f, 0.36f, 0.05f), At(CourtC + new Vector2(0.55f, 0.06f), 8.8f), yellow, new Vector3(0, 0, 41));
        // Urns with flowers round the rim.
        foreach (var u in UrnSites())
        {
            Box(new Vector3(0.7f, 0.7f, 0.7f), At(u, 0.35f), dark); Box(new Vector3(0.85f, 0.14f, 0.85f), At(u, 0.76f), stone);
            Cyl(0.42f, 0.6f, At(u, 1.12f), white, 12, 0.3f); Cyl(0.46f, 0.08f, At(u, 1.46f), white, 12);
            for (int k = 0; k < 9; k++) Ball(rng.RandfRange(0.11f, 0.17f), At(u + new Vector2(rng.RandfRange(-0.3f, 0.3f), rng.RandfRange(-0.3f, 0.3f)), 1.58f + rng.RandfRange(0f, 0.18f)), k % 2 == 0 ? red : yellow);
        }
        // The gate: iron leaves swung open on the posts, bars with gilt spear heads and a star medallion; a striped barrier pole at the booth.
        foreach (float side in new[] { -1f, 1f })
        {
            Vector2 hinge = Gate + new Vector2(0.75f, side * 3.4f); float phi = 0f;      // swung open outward, flat along the drive's edge on the road side
            Vector2 dir = new(MathF.Cos(phi), MathF.Sin(phi)); float yaw = Mathf.RadToDeg(-phi);
            for (int i = 0; i < 12; i++)
            {
                Vector2 p = hinge + dir * (0.15f + i * 0.2f);
                Box(new Vector3(0.035f, 2.05f, 0.035f), At(p, 1.05f), iron, new Vector3(0, yaw, 0));
                Box(new Vector3(0.09f, 0.14f, 0.09f), At(p, 2.15f), gold, new Vector3(0, yaw, 45));
            }
            foreach (float y in new[] { 0.25f, 1.0f, 1.95f }) Box(new Vector3(2.5f, 0.07f, 0.06f), At(hinge + dir * 1.3f, y), iron, new Vector3(0, yaw, 0));
            Box(new Vector3(0.5f, 0.5f, 0.05f), At(hinge + dir * 1.3f, 1.5f), gold, new Vector3(0, yaw, 45));
        }
        // The guard booth outside the gate: whitewashed walls, a window on three sides, a hipped roof, a door and a step; the barrier pole across the drive's south edge.
        Vector2 b = BoothAt;
        Box(new Vector3(2.7f, 0.3f, 2.7f), At(b, 0.15f), dark);
        Box(new Vector3(2.5f, 2.3f, 2.5f), At(b, 1.45f), new Color(0.9f, 0.87f, 0.76f));
        foreach (var (dx, dz, w, d) in new[] { (-1.26f, 0f, 0.06f, 1.2f), (1.26f, 0f, 0.06f, 1.2f), (0f, 1.26f, 1.2f, 0.06f) })
        {
            Box(new Vector3(w, 0.85f, d), At(b + new Vector2(dx, dz), 1.65f), new Color(0.07f, 0.1f, 0.13f));
            Box(new Vector3(w + (w > 0.1f ? 0.2f : 0.06f), 0.08f, d + (d > 0.1f ? 0.2f : 0.06f)), At(b + new Vector2(dx, dz), 1.14f), stone);
        }
        Box(new Vector3(0.9f, 1.9f, 0.06f), At(b + new Vector2(0, -1.26f), 1.1f), new Color(0.3f, 0.2f, 0.12f));
        Box(new Vector3(3.3f, 0.14f, 3.3f), At(b, 2.7f), new Color(0.3f, 0.34f, 0.3f));
        Cyl(2.35f, 0.8f, At(b, 3.15f), new Color(0.26f, 0.38f, 0.3f), 4, 0.12f, new Vector3(0, 45, 0));
        Ball(0.1f, At(b, 3.6f), gold);
        Box(new Vector3(1.1f, 0.12f, 0.8f), At(b + new Vector2(0, -1.9f), 0.06f), dark);
    }
}
