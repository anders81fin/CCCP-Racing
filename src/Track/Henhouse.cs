using System;
using System.Collections.Generic;
using Godot;

namespace CCCPRacing.Track;

/// <summary>
/// A village henhouse with its wire run ("henhouse" prop): a plank coop on legs under a shingled roof with a pop-hole and cleated ramp, a hinged-lid nest box, a window and a perch beam; a netted run
/// with a feeder, a water trough, straw and a dust-bath hollow; six hens and a rooster. Set on the ground under it. Local frame: the coop at (-1.8, 0), the run to +X (6 m by 4 m), origin the centre.
/// </summary>
public static class Henhouse
{
    static readonly Color Plank = new(0.5f, 0.38f, 0.24f), PlankDark = new(0.33f, 0.24f, 0.15f), Shingle = new(0.38f, 0.32f, 0.26f), Wire = new(0.55f, 0.56f, 0.58f), Straw = new(0.82f, 0.72f, 0.4f);

    public static IEnumerable<(float x, float z, float r)> Circles()
    {
        yield return (-1.8f, 0f, 1.1f);
        foreach (var (x, z) in new[] { (0.7f, -2f), (0.7f, 2f), (6.7f, -2f), (6.7f, 2f) }) yield return (x, z, 0.15f);
    }

    static MeshInstance3D B(Vector3 size, Color c, Vector3 pos, Vector3? rot = null) => Props.Part(new BoxMesh { Size = size }, c, pos, rot);

    public static Node3D Build(TrackData t, Vector3 origin, float rot, int seed)
    {
        var n = new Node3D();
        var rng = new RandomNumberGenerator { Seed = (ulong)Math.Abs(seed) + 91 };
        float s = MathF.Sin(rot), c = MathF.Cos(rot);
        float G(float lx, float lz) => t.MeshHeight(origin.X + lx * c + lz * s, origin.Z - lx * s + lz * c) - origin.Y;
        Vector3 P(float lx, float y, float lz) => new(lx, G(lx, lz) + y, lz);
        // The coop: planks on a frame standing on four posts, so the floor is level whatever the slope.
        float floor = Math.Max(Math.Max(G(-2.5f, -0.7f), G(-1.1f, -0.7f)), Math.Max(G(-2.5f, 0.7f), G(-1.1f, 0.7f))) + 0.65f;
        foreach (float x in new[] { -2.5f, -1.1f }) foreach (float z in new[] { -0.7f, 0.7f })
            n.AddChild(B(new Vector3(0.14f, floor - G(x, z) + 0.05f, 0.14f), PlankDark, new Vector3(x, (floor + G(x, z)) * 0.5f, z)));
        BuildKit.Planks(n, new Vector3(-2.5f, floor, -0.75f), true, 1.4f, 1.15f, 0.06f, Plank, 301);
        BuildKit.Planks(n, new Vector3(-2.5f, floor, 0.75f), true, 1.4f, 1.15f, 0.06f, Plank, 302, new BuildKit.Hole(0.5f, 0.95f, 0.1f, 0.75f));
        BuildKit.Planks(n, new Vector3(-2.55f, floor, -0.75f), false, 1.5f, 1.15f, 0.06f, Plank, 303, new BuildKit.Hole(0.5f, 1.0f, 0.55f, 1.0f));
        BuildKit.Planks(n, new Vector3(-1.05f, floor, -0.75f), false, 1.5f, 1.15f, 0.06f, Plank, 304);
        n.AddChild(B(new Vector3(1.5f, 0.07f, 1.6f), PlankDark, new Vector3(-1.8f, floor, 0)));
        n.AddChild(B(new Vector3(0.5f, 0.45f, 0.03f), new Color(0.04f, 0.04f, 0.05f), new Vector3(-1.05f + 0.03f, floor + 0.35f, 0.0f)));    // the pop-hole (a dark opening)
        n.AddChild(B(new Vector3(0.05f, 0.5f, 0.5f), new Color(0.05f, 0.06f, 0.07f), new Vector3(-2.58f, floor + 0.78f, -0.2f)));         // the little window
        n.AddChild(B(new Vector3(0.05f, 0.04f, 0.56f), Plank, new Vector3(-2.6f, floor + 0.52f, -0.2f)));
        // Ramp with cleats from the pop-hole down into the run.
        n.AddChild(B(new Vector3(0.42f, 0.04f, 1.0f), PlankDark, new Vector3(-0.6f, floor * 0.5f + G(-0.6f, 0f) * 0.5f + 0.1f, 0f), new Vector3(0, 0, -Mathf.RadToDeg(MathF.Atan2(floor - G(-0.4f, 0f), 1.0f)))));
        for (int k = 0; k < 5; k++) n.AddChild(B(new Vector3(0.04f, 0.03f, 0.4f), Plank, new Vector3(-0.85f + k * 0.18f, Mathf.Lerp(floor, G(-0.4f, 0f), k / 5f) - 0.08f, 0f)));
        // The roof: shingled planes over the coop, a ridge board and a bargeboard each end.
        float ridge = floor + 1.95f, theta = Mathf.DegToRad(30f);
        foreach (float side in new[] { -1f, 1f })
            for (int row = 0; row < 3; row++)
            {
                float tt = 0.22f + row * 0.33f;
                n.AddChild(B(new Vector3(1.75f, 0.05f, 0.4f), BuildKit.Tone(Shingle, rng, 0.14f), new Vector3(-1.8f, ridge - tt * MathF.Sin(theta), side * tt * MathF.Cos(theta)), new Vector3(Mathf.RadToDeg(side * theta), 0, 0)));
            }
        n.AddChild(B(new Vector3(1.8f, 0.07f, 0.12f), PlankDark, new Vector3(-1.8f, ridge + 0.03f, 0)));
        foreach (float x in new[] { -2.62f, -0.98f }) n.AddChild(Props.Part(new PrismMesh { Size = new Vector3(1.55f, 0.77f, 0.05f) }, Plank, new Vector3(x, floor + 1.15f + 0.38f, 0), new Vector3(0, 90, 0)));
        // The nest box on the long side, its lid raised on a stick.
        n.AddChild(B(new Vector3(1.0f, 0.5f, 0.5f), Plank, new Vector3(-1.8f, floor + 0.35f, 1.0f)));
        n.AddChild(B(new Vector3(1.05f, 0.04f, 0.55f), PlankDark, new Vector3(-1.8f, floor + 0.75f, 1.0f + 0.05f), new Vector3(-25, 0, 0)));
        n.AddChild(B(new Vector3(0.9f, 0.06f, 0.4f), Straw, new Vector3(-1.8f, floor + 0.62f, 1.0f)));
        n.AddChild(Props.Part(Props.Cyl(0.015f, 0.5f, 4), PlankDark, new Vector3(-1.4f, floor + 0.9f, 1.3f), new Vector3(60, 0, 0)));
        // A perch beam and a hanging lamp bracket seen through the front.
        n.AddChild(Props.Part(Props.Cyl(0.03f, 1.4f, 5), PlankDark, new Vector3(-1.8f, floor + 0.45f, -0.35f), new Vector3(0, 0, 90)));
        // The run: wire on posts, a gate in the south side, a roofline of wire, the ground scratched bare.
        float x0 = 0.7f, x1 = 6.7f, z0 = -2f, z1 = 2f;
        void Panel(float ax, float az, float bx, float bz, bool door)
        {
            float len = MathF.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az)); int posts = Math.Max(1, (int)(len / 1.5f));
            float yaw = Mathf.RadToDeg(MathF.Atan2(-(bz - az), bx - ax));
            for (int k = 0; k <= posts; k++)
            {
                float f = k / (float)posts, px = Mathf.Lerp(ax, bx, f), pz = Mathf.Lerp(az, bz, f);
                n.AddChild(Props.Part(Props.Cyl(0.05f, 1.75f, 6), PlankDark, P(px, 0.87f, pz)));
            }
            for (int k = 0; k < posts; k++)
            {
                float f = (k + 0.5f) / posts, px = Mathf.Lerp(ax, bx, f), pz = Mathf.Lerp(az, bz, f);
                foreach (float y in new[] { 0.2f, 0.7f, 1.2f, 1.7f }) n.AddChild(B(new Vector3(len / posts, 0.012f, 0.012f), Wire, P(px, y, pz), new Vector3(0, yaw, 0)));
                for (int m = -2; m <= 2; m++) n.AddChild(B(new Vector3(0.012f, 1.5f, 0.012f), Wire, P(px + MathF.Cos(Mathf.DegToRad(yaw)) * m * 0.28f, 0.85f, pz - MathF.Sin(Mathf.DegToRad(yaw)) * m * 0.28f)));
            }
            if (door) n.AddChild(B(new Vector3(0.9f, 1.5f, 0.04f), PlankDark, P(Mathf.Lerp(ax, bx, 0.5f), 0.8f, Mathf.Lerp(az, bz, 0.5f))));
        }
        Panel(x0, z0, x1, z0, false); Panel(x0, z1, x1, z1, true); Panel(x1, z0, x1, z1, false);
        Panel(x0, z0, x0, -0.6f, false); Panel(x0, 0.6f, x0, z1, false);
        for (float x = x0 + 0.4f; x < x1; x += 1.2f) n.AddChild(B(new Vector3(0.9f, 0.012f, 0.012f), Wire, P(x, 1.75f, 0), new Vector3(0, 90, 0)));
        // Feeder, water trough, straw, a dust-bath hollow.
        n.AddChild(Props.Part(Props.Cyl(0.22f, 0.3f, 10, 0.15f), new Color(0.45f, 0.45f, 0.48f), P(3.4f, 0.15f, -1.2f)));
        n.AddChild(Props.Part(Props.Cyl(0.12f, 0.35f, 8), new Color(0.42f, 0.42f, 0.45f), P(3.4f, 0.45f, -1.2f)));
        n.AddChild(B(new Vector3(0.8f, 0.14f, 0.25f), PlankDark, P(5.2f, 0.07f, 1.3f)));
        n.AddChild(B(new Vector3(0.7f, 0.02f, 0.18f), new Color(0.05f, 0.09f, 0.12f), P(5.2f, 0.14f, 1.3f)));
        for (int k = 0; k < 24; k++) n.AddChild(B(new Vector3(rng.RandfRange(0.15f, 0.4f), 0.01f, 0.02f), Straw, P(rng.RandfRange(x0 + 0.3f, x1 - 0.3f), 0.03f, rng.RandfRange(z0 + 0.3f, z1 - 0.3f)), new Vector3(0, rng.RandfRange(0, 180), 0)));
        var dust = Props.Part(Props.Cyl(0.7f, 0.01f, 12), new Color(0.38f, 0.3f, 0.2f), P(2.0f, 0.02f, 1.0f)); n.AddChild(dust);
        // The birds.
        for (int k = 0; k < 7; k++)
        {
            bool cock = k == 0;
            Color body = cock ? new Color(0.5f, 0.22f, 0.1f) : (k % 3 == 0 ? new Color(0.88f, 0.84f, 0.74f) : k % 3 == 1 ? new Color(0.5f, 0.3f, 0.15f) : new Color(0.25f, 0.2f, 0.17f));
            float px = rng.RandfRange(1.4f, 6.2f), pz = rng.RandfRange(-1.6f, 1.6f), yaw = rng.RandfRange(0, 360), sc = cock ? 1.25f : 1.0f;
            Vector3 b0 = P(px, 0.3f * sc, pz); bool peck = k % 2 == 1;
            var bd = Props.Part(Props.Ball(0.17f * sc, 7), body, b0, new Vector3(peck ? 22 : 0, yaw, 0)); bd.Scale = new Vector3(0.9f, 0.85f, 1.25f); n.AddChild(bd);
            Vector3 fwd = new Vector3(MathF.Sin(Mathf.DegToRad(yaw)), 0, MathF.Cos(Mathf.DegToRad(yaw)));
            Vector3 hd = b0 + fwd * 0.2f * sc + Vector3.Up * (peck ? -0.02f : 0.16f * sc);
            n.AddChild(Props.Part(Props.Ball(0.07f * sc, 6), cock ? body.Lightened(0.15f) : body, hd));
            n.AddChild(B(new Vector3(0.025f, 0.02f, 0.06f), new Color(0.9f, 0.7f, 0.2f), hd + fwd * 0.07f * sc, new Vector3(0, yaw, 0)));
            n.AddChild(B(new Vector3(0.02f, 0.05f * sc, 0.06f), new Color(0.75f, 0.1f, 0.08f), hd + Vector3.Up * 0.07f * sc, new Vector3(0, yaw, 0)));
            n.AddChild(B(new Vector3(0.05f, 0.2f * sc, 0.16f), cock ? new Color(0.1f, 0.35f, 0.3f) : body.Darkened(0.2f), b0 - fwd * 0.2f * sc + Vector3.Up * 0.1f * sc, new Vector3(35, yaw, 0)));
            foreach (float lx in new[] { -0.05f, 0.05f }) n.AddChild(Props.Part(Props.Cyl(0.01f, 0.2f * sc, 4), new Color(0.85f, 0.65f, 0.2f), P(px + lx, 0.1f, pz)));
        }
        DrunkTractor.BatchStatic(n, null);
        return n;
    }
}
