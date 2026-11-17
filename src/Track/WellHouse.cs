using System;
using System.Collections.Generic;
using Godot;

namespace CCCPRacing.Track;

/// <summary>
/// The mill's roofed well ("well" prop): a fieldstone shaft ring paved round with flat stones, a log crib above the ground (notched corner logs), two posts and a small shingled gable roof,
/// a windlass with a crank, a coiled rope and a hanging bucket, a second bucket on the kerb, a hollowed-log watering trough on stones with a wet patch, and a stone to step on.
/// Local frame: the well at the origin, the roof's ridge along X, the trough on the +Z side.
/// </summary>
public static class WellHouse
{
    static readonly Color Stone = new(0.52f, 0.5f, 0.45f), Log = new(0.4f, 0.28f, 0.17f), LogDark = new(0.27f, 0.19f, 0.12f), Shingle = new(0.38f, 0.31f, 0.25f);
    static readonly Color Dark = new(0.05f, 0.07f, 0.09f), Iron = new(0.2f, 0.2f, 0.22f), Rope = new(0.66f, 0.58f, 0.4f), Wet = new(0.2f, 0.17f, 0.12f), Moss = new(0.3f, 0.42f, 0.22f);

    public static IEnumerable<(float x, float z, float r)> Circles()
    {
        yield return (0f, 0f, 1.05f);
        yield return (0f, 2.3f, 0.75f);
        yield return (0.7f, 2.3f, 0.7f);
    }

    static MeshInstance3D B(Vector3 size, Color c, Vector3 pos, Vector3? rot = null) => Props.Part(new BoxMesh { Size = size }, c, pos, rot);

    public static Node3D Build()
    {
        var n = new Node3D();
        var rng = new RandomNumberGenerator { Seed = 4242 };
        // Paving: flat stones in a ring round the kerb, of mixed size and a little out of level.
        for (int k = 0; k < 26; k++)
        {
            float a = k * MathF.Tau / 26f + rng.RandfRange(-0.05f, 0.05f), r = rng.RandfRange(1.15f, 1.85f);
            var slab = B(new Vector3(rng.RandfRange(0.4f, 0.7f), 0.07f, rng.RandfRange(0.35f, 0.6f)), BuildKit.Tone(Stone, rng, 0.14f), new Vector3(MathF.Cos(a) * r, 0.03f, MathF.Sin(a) * r), new Vector3(rng.RandfRange(-2, 2), rng.RandfRange(0, 180), rng.RandfRange(-2, 2)));
            n.AddChild(slab);
        }
        // The kerb: a ring of fieldstones in three courses (each stone its own block, turned to the ring).
        for (int row = 0; row < 3; row++)
        {
            int count = 12;
            for (int k = 0; k < count; k++)
            {
                float a = (k + (row % 2) * 0.5f) * MathF.Tau / count + rng.RandfRange(-0.04f, 0.04f);
                float r = 0.92f + rng.RandfRange(-0.03f, 0.05f);
                n.AddChild(B(new Vector3(0.46f, 0.3f, 0.5f + rng.RandfRange(-0.05f, 0.1f)), BuildKit.Tone(Stone, rng, 0.14f), new Vector3(MathF.Cos(a) * r, 0.17f + row * 0.3f, MathF.Sin(a) * r), new Vector3(0, -Mathf.RadToDeg(a), 0)));
            }
        }
        n.AddChild(Props.Part(Props.Cyl(0.7f, 0.04f, 14), Dark, new Vector3(0, 0.78f, 0)));                       // the dark water far down
        for (int k = 0; k < 8; k++)
        {
            float a = k * MathF.Tau / 8f;
            n.AddChild(Props.Part(Props.Ball(0.07f, 5), Moss, new Vector3(MathF.Cos(a) * 0.74f, 0.9f, MathF.Sin(a) * 0.74f)));   // moss in the joints
        }
        // The log crib above the kerb: two notched corner stacks and the roof posts.
        foreach (float z in new[] { -0.78f, 0.78f })
            for (int k = 0; k < 3; k++)
                n.AddChild(Props.Part(Props.Cyl(0.075f, 1.9f, 7), BuildKit.Tone(Log, rng, 0.14f), new Vector3(0, 0.95f + k * 0.15f + 0.02f, z), new Vector3(0, 0, 90)));
        foreach (float x in new[] { -0.92f, 0.92f })
            for (int k = 0; k < 3; k++)
                n.AddChild(Props.Part(Props.Cyl(0.075f, 1.7f, 7), BuildKit.Tone(LogDark, rng, 0.14f), new Vector3(x, 1.025f + k * 0.15f, 0), new Vector3(90, 0, 0)));
        foreach (float x in new[] { -0.9f, 0.9f }) foreach (float z in new[] { -0.8f, 0.8f })
            n.AddChild(Props.Part(Props.Cyl(0.11f, 1.65f, 7), BuildKit.Tone(LogDark, rng, 0.12f), new Vector3(x, 1.75f + 0.0f, z)));          // corner posts rising to the roof
        // The roof: two shingled planes, a ridge, bargeboards and a small drip carving.
        float ridge = 2.95f, half = 1.25f, theta = MathF.Atan2(0.62f, half - 0.2f);
        foreach (float side in new[] { -1f, 1f })
            for (int row = 0; row < 5; row++)
            {
                float t = (row + 0.5f) * 0.33f;
                n.AddChild(B(new Vector3(2.5f, 0.05f, 0.38f), BuildKit.Tone(Shingle, rng, 0.16f), new Vector3(0, ridge - t * MathF.Sin(theta), side * t * MathF.Cos(theta)), new Vector3(Mathf.RadToDeg(side * theta), 0, 0)));
            }
        n.AddChild(B(new Vector3(2.6f, 0.12f, 0.16f), LogDark, new Vector3(0, ridge + 0.04f, 0)));
        foreach (float x in new[] { -1.25f, 1.25f }) foreach (float side in new[] { -1f, 1f })
            n.AddChild(B(new Vector3(0.06f, 0.2f, 1.75f), LogDark, new Vector3(x, ridge - 0.55f, side * 0.82f), new Vector3(Mathf.RadToDeg(side * theta), 0, 0)));
        foreach (float x in new[] { -1.05f, 1.05f }) n.AddChild(Props.Part(new PrismMesh { Size = new Vector3(1.6f, 0.62f, 0.07f) }, LogDark, new Vector3(x, ridge - 0.45f, 0), new Vector3(0, 90, 0)));
        // The windlass: a drum between the posts, the crank at one end, the rope wound on it, the bucket hanging.
        n.AddChild(Props.Part(Props.Cyl(0.13f, 1.8f, 8), Log, new Vector3(0, 2.2f, 0), new Vector3(0, 0, 90)));
        for (int k = 0; k < 7; k++) n.AddChild(Props.Part(Props.Cyl(0.145f, 0.05f, 8), Rope, new Vector3(-0.3f + k * 0.1f, 2.2f, 0), new Vector3(0, 0, 90)));
        n.AddChild(Props.Part(Props.Cyl(0.03f, 0.55f, 5), Iron, new Vector3(1.05f, 2.2f, 0), new Vector3(0, 0, 90)));
        n.AddChild(B(new Vector3(0.05f, 0.5f, 0.05f), Iron, new Vector3(1.28f, 1.97f, 0)));
        n.AddChild(Props.Part(Props.Cyl(0.05f, 0.35f, 5), Log, new Vector3(1.28f, 1.7f, 0.15f), new Vector3(90, 0, 0)));
        n.AddChild(Props.Part(Props.Cyl(0.02f, 0.95f, 4), Rope, new Vector3(0.28f, 1.7f, 0.0f)));
        n.AddChild(Props.Part(Props.Cyl(0.19f, 0.3f, 10, 0.15f), Log, new Vector3(0.28f, 1.1f, 0.0f)));
        n.AddChild(Props.Part(Props.Cyl(0.2f, 0.04f, 10), Iron, new Vector3(0.28f, 1.22f, 0.0f)));
        foreach (float x in new[] { -0.92f, 0.92f }) n.AddChild(B(new Vector3(0.1f, 0.1f, 0.5f), Iron, new Vector3(x, 2.2f, 0)));   // axle bearings
        // A second bucket on the kerb, rope coiled beside it, a flat stone to stand on.
        n.AddChild(Props.Part(Props.Cyl(0.18f, 0.28f, 10, 0.14f), Log, new Vector3(-0.2f, 0.15f, 1.55f)));
        n.AddChild(Props.Part(Props.Cyl(0.19f, 0.04f, 10), Iron, new Vector3(-0.2f, 0.27f, 1.55f)));
        for (int k = 0; k < 3; k++) n.AddChild(Props.Part(Props.Cyl(0.14f + k * 0.03f, 0.03f, 10, 0.13f + k * 0.03f), Rope, new Vector3(0.28f, 0.04f + k * 0.03f, 1.5f)));
        n.AddChild(B(new Vector3(0.9f, 0.14f, 0.6f), Stone, new Vector3(-1.0f, 0.07f, 1.5f), new Vector3(0, 12, 0)));
        // The trough: a hollowed log on two stones, water in it, a dark wet patch under the spout.
        n.AddChild(Props.Part(Props.Cyl(0.34f, 2.3f, 9), Log, new Vector3(0.35f, 0.45f, 2.55f), new Vector3(0, 0, 90)));
        n.AddChild(B(new Vector3(2.15f, 0.04f, 0.38f), Dark, new Vector3(0.35f, 0.75f, 2.55f)));
        foreach (float x in new[] { -0.55f, 1.2f }) n.AddChild(B(new Vector3(0.45f, 0.3f, 0.7f), BuildKit.Tone(Stone, rng, 0.1f), new Vector3(x, 0.15f, 2.55f), new Vector3(0, rng.RandfRange(-10, 10), 0)));
        n.AddChild(Props.Part(Props.Cyl(0.9f, 0.015f, 14), Wet, new Vector3(0.35f, 0.012f, 3.3f)));
        DrunkTractor.BatchStatic(n, null);
        return n;
    }
}
