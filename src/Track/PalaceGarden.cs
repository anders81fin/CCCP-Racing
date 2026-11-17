using System;
using System.Collections.Generic;
using Godot;

namespace CCCPRacing.Track;

/// <summary>
/// The rest home's formal garden ("palacegarden" prop): gravel walks in a cross and a ring round a stone fountain basin with a tiered pedestal, bowl and jets; four box-edged parterres of concentric
/// flower beds each with a clipped topiary, a domed rotunda at the far end, a rose arch and trellis at the near end, statues on plinths at the junctions, urns, benches and iron lanterns, a border
/// of conical cypress along the hedge. Everything is set on the ground under it. Local frame: 17 m (X) by 24 m (Z), origin the centre; the rotunda at -Z, the rose arch at +Z.
/// </summary>
public static class PalaceGarden
{
    public const float W = 17f, D = 24f;
    static readonly Color Gravel = new(0.78f, 0.72f, 0.58f), Stone = new(0.78f, 0.77f, 0.72f), StoneDark = new(0.58f, 0.57f, 0.53f), Box = new(0.12f, 0.3f, 0.16f), Cypress = new(0.08f, 0.22f, 0.12f);
    static readonly Color Red = new(0.78f, 0.12f, 0.1f), Yellow = new(0.95f, 0.78f, 0.15f), White = new(0.93f, 0.92f, 0.88f), Violet = new(0.5f, 0.25f, 0.6f), Pink = new(0.9f, 0.45f, 0.55f), Green = new(0.2f, 0.42f, 0.18f);
    static readonly Color Iron = new(0.13f, 0.14f, 0.15f), Gold = new(0.85f, 0.7f, 0.2f), Dome = new(0.22f, 0.45f, 0.36f), Wood = new(0.5f, 0.38f, 0.24f), Water = new(0.45f, 0.62f, 0.7f);

    public static IEnumerable<(float x, float z, float r)> Circles()
    {
        yield return (0f, 0f, 2.35f);                                    // fountain
        yield return (0f, -9.6f, 2.3f);                                  // rotunda
        foreach (var (x, z) in new[] { (-3.2f, -3.2f), (3.2f, -3.2f), (-3.2f, 3.2f), (3.2f, 3.2f) }) yield return (x, z, 0.5f);       // statues on plinths
        foreach (var (x, z) in new[] { (-4.7f, -5.2f), (4.7f, -5.2f), (-4.7f, 5.2f), (4.7f, 5.2f) }) yield return (x, z, 0.55f);       // topiaries
        foreach (var (x, z) in new[] { (-7.2f, 0f), (7.2f, 0f) }) yield return (x, z, 0.7f);                                             // benches
    }

    static MeshInstance3D B(Vector3 size, Color c, Vector3 pos, Vector3? rot = null) => Props.Part(new BoxMesh { Size = size }, c, pos, rot);

    public static Node3D Build(TrackData t, Vector3 origin, float rot, int seed)
    {
        var n = new Node3D();
        var rng = new RandomNumberGenerator { Seed = (ulong)Math.Abs(seed) + 555 };
        float s = MathF.Sin(rot), c = MathF.Cos(rot);
        float G(float lx, float lz) => t.MeshHeight(origin.X + lx * c + lz * s, origin.Z - lx * s + lz * c) - origin.Y;
        Vector3 P(float lx, float y, float lz) => new(lx, G(lx, lz) + y, lz);
        Vector3 Level(float lx, float lz, float y) => new(lx, Math.Max(Math.Max(G(lx - 1.2f, lz - 1.2f), G(lx + 1.2f, lz + 1.2f)), Math.Max(G(lx - 1.2f, lz + 1.2f), G(lx + 1.2f, lz - 1.2f))) + y, lz);

        // ---- the walks: a cross, a ring round the fountain, an outer loop, all of fine gravel (a pale ribbon just above the lawn)
        void Walk(float x0, float z0, float x1, float z1, float width)
        {
            float len = MathF.Sqrt((x1 - x0) * (x1 - x0) + (z1 - z0) * (z1 - z0)); int cnt = Math.Max(1, (int)(len / 0.8f));
            float yaw = Mathf.RadToDeg(MathF.Atan2(-(z1 - z0), x1 - x0));
            for (int k = 0; k < cnt; k++)
            {
                float f = (k + 0.5f) / cnt, x = Mathf.Lerp(x0, x1, f), z = Mathf.Lerp(z0, z1, f);
                n.AddChild(B(new Vector3(len / cnt + 0.05f, 0.05f, width), BuildKit.Tone(Gravel, rng, 0.05f), P(x, 0.03f, z), new Vector3(0, yaw, 0)));
            }
        }
        Walk(0, -D / 2 + 1.2f, 0, D / 2 - 0.5f, 1.9f);
        Walk(-W / 2 + 1.2f, 0, W / 2 - 1.2f, 0, 1.7f);
        Walk(-W / 2 + 1.0f, -D / 2 + 1.6f, W / 2 - 1.0f, -D / 2 + 1.6f, 1.2f);
        Walk(-W / 2 + 1.0f, D / 2 - 1.6f, W / 2 - 1.0f, D / 2 - 1.6f, 1.2f);
        Walk(-W / 2 + 1.0f, -D / 2 + 1.6f, -W / 2 + 1.0f, D / 2 - 1.6f, 1.2f);
        Walk(W / 2 - 1.0f, -D / 2 + 1.6f, W / 2 - 1.0f, D / 2 - 1.6f, 1.2f);
        for (int k = 0; k < 16; k++)           // the ring of gravel round the basin
        {
            float a = k * MathF.Tau / 16f;
            n.AddChild(B(new Vector3(1.5f, 0.05f, 1.7f), Gravel, P(MathF.Cos(a) * 3.6f, 0.03f, MathF.Sin(a) * 3.6f), new Vector3(0, -Mathf.RadToDeg(a), 0)));
        }
        // ---- the fountain: a stone basin of eight cut sections, dark water, a tiered pedestal, a bowl, a finial and four jets
        Vector3 fc = Level(0, 0, 0f);
        for (int k = 0; k < 8; k++)
        {
            float a = (k + 0.5f) * MathF.Tau / 8f; Vector3 p = fc + new Vector3(MathF.Cos(a) * 1.95f, 0.3f, MathF.Sin(a) * 1.95f);
            n.AddChild(B(new Vector3(1.55f, 0.6f, 0.4f), Stone, p, new Vector3(0, -Mathf.RadToDeg(a) + 90f, 0)));
            n.AddChild(B(new Vector3(1.6f, 0.1f, 0.5f), StoneDark, p + Vector3.Up * 0.33f, new Vector3(0, -Mathf.RadToDeg(a) + 90f, 0)));
        }
        n.AddChild(Props.Part(Props.Cyl(1.78f, 0.06f, 16), new Color(0.12f, 0.22f, 0.28f), fc + Vector3.Up * 0.5f));
        n.AddChild(Props.Part(Props.Cyl(0.7f, 0.5f, 12, 0.55f), Stone, fc + Vector3.Up * 0.45f));
        n.AddChild(Props.Part(Props.Cyl(0.32f, 1.3f, 10, 0.26f), Stone, fc + Vector3.Up * 1.3f));
        n.AddChild(Props.Part(Props.Cyl(1.05f, 0.25f, 14, 0.55f), StoneDark, fc + Vector3.Up * 2.05f));
        n.AddChild(Props.Part(Props.Cyl(0.95f, 0.04f, 14), new Color(0.2f, 0.35f, 0.42f), fc + Vector3.Up * 2.17f));
        n.AddChild(Props.Part(Props.Cyl(0.12f, 0.9f, 8, 0.05f), Stone, fc + Vector3.Up * 2.6f));
        n.AddChild(Props.Part(Props.Ball(0.14f, 7), Gold, fc + Vector3.Up * 3.1f));
        for (int k = 0; k < 4; k++)
        {
            float a = k * MathF.Tau / 4f + 0.4f;
            n.AddChild(Props.Part(Props.Cyl(0.02f, 1.0f, 4), Water, fc + new Vector3(MathF.Cos(a) * 0.5f, 1.0f, MathF.Sin(a) * 0.5f), new Vector3(MathF.Sin(a) * 28f, 0, -MathF.Cos(a) * 28f)));
            n.AddChild(Props.Part(Props.Cyl(0.02f, 0.9f, 4), Water, fc + new Vector3(MathF.Cos(a) * 1.3f, 0.7f, MathF.Sin(a) * 1.3f), new Vector3(-MathF.Sin(a) * 40f, 0, MathF.Cos(a) * 40f)));
        }
        // ---- four parterres: a box-hedge border, concentric flower rows, a clipped topiary at the centre
        void Bed(float cx, float cz, float w, float d, Color[] rings, bool sphere)
        {
            // The soil: a grid of 0.9 m cells, each on the ground under it and sunk a little into the slope, so no part of the bed floats or hides.
            int cx0 = (int)MathF.Ceiling(w / 0.9f), cz0 = (int)MathF.Ceiling(d / 0.9f);
            for (int a = 0; a < cx0; a++) for (int bq = 0; bq < cz0; bq++)
            {
                float sx = cx - w / 2 + (a + 0.5f) * w / cx0, sz = cz - d / 2 + (bq + 0.5f) * d / cz0;
                float gy = Math.Max(Math.Max(G(sx - 0.45f, sz - 0.45f), G(sx + 0.45f, sz + 0.45f)), Math.Max(G(sx - 0.45f, sz + 0.45f), G(sx + 0.45f, sz - 0.45f)));
                n.AddChild(B(new Vector3(w / cx0 + 0.02f, 0.4f, d / cz0 + 0.02f), Soil(), new Vector3(sx, gy + 0.02f, sz)));
            }
            for (int ring = 0; ring < rings.Length; ring++)
            {
                float rw = w - ring * 1.1f, rd = d - ring * 1.1f; if (rw < 0.6f || rd < 0.6f) break;
                // the rows of blooms along this ring's edges
                int along = (int)(rw / 0.32f), side = (int)(rd / 0.32f);
                for (int k = 0; k <= along; k++) foreach (float sz in new[] { -1f, 1f })
                    Bloom(cx - rw / 2 + k * rw / along, cz + sz * (rd / 2 - 0.12f), rings[ring]);
                for (int k = 1; k < side; k++) foreach (float sx in new[] { -1f, 1f })
                    Bloom(cx + sx * (rw / 2 - 0.12f), cz - rd / 2 + k * rd / side, rings[ring]);
            }
            // box-hedge border and its corner finials
            for (float u = -w / 2 - 0.15f; u < w / 2 + 0.15f; u += 0.7f) foreach (float sz in new[] { -1f, 1f })
            { float ux = Math.Min(u + 0.35f, w / 2 + 0.15f); n.AddChild(B(new Vector3(0.74f, 0.42f, 0.34f), BuildKit.Tone(Box, rng, 0.1f), P(cx + ux - 0.0f, 0.21f, cz + sz * (d / 2 + 0.1f)))); }
            for (float u = -d / 2 - 0.15f; u < d / 2 + 0.15f; u += 0.7f) foreach (float sx in new[] { -1f, 1f })
            { float uz = Math.Min(u + 0.35f, d / 2 + 0.15f); n.AddChild(B(new Vector3(0.34f, 0.42f, 0.74f), BuildKit.Tone(Box, rng, 0.1f), P(cx + sx * (w / 2 + 0.1f), 0.21f, cz + uz))); }
            foreach (float sx in new[] { -1f, 1f }) foreach (float sz in new[] { -1f, 1f })
                n.AddChild(Props.Part(Props.Ball(0.26f, 7), Box.Lightened(0.1f), P(cx + sx * (w / 2 + 0.1f), 0.5f, cz + sz * (d / 2 + 0.1f))));
            if (sphere)
            {
                n.AddChild(Props.Part(Props.Cyl(0.06f, 0.7f, 5), new Color(0.3f, 0.2f, 0.12f), P(cx, 0.45f, cz)));
                n.AddChild(Props.Part(Props.Ball(0.5f, 9), BuildKit.Tone(Box, rng, 0.06f), P(cx, 1.15f, cz)));
                n.AddChild(Props.Part(Props.Ball(0.26f, 8), BuildKit.Tone(Box, rng, 0.06f), P(cx, 1.78f, cz)));
            }
            else
            {
                n.AddChild(Props.Part(Props.Cyl(0.5f, 1.8f, 9, 0.04f), BuildKit.Tone(Cypress, rng, 0.08f), P(cx, 1.0f, cz)));
            }
        }
        Color Soil() => new Color(0.24f, 0.17f, 0.11f);
        void Bloom(float x, float z, Color col)
        {
            var b = Props.Part(Props.Ball(rng.RandfRange(0.1f, 0.15f), 5), BuildKit.Tone(col, rng, 0.1f), P(x + rng.RandfRange(-0.04f, 0.04f), rng.RandfRange(0.3f, 0.4f), z + rng.RandfRange(-0.04f, 0.04f))); b.Scale = new Vector3(1f, 0.85f, 1f); n.AddChild(b);
        }
        Bed(-4.7f, -5.2f, 5.6f, 5.2f, new[] { Red, Yellow, White }, true);
        Bed(4.7f, -5.2f, 5.6f, 5.2f, new[] { Violet, Pink, White }, false);
        Bed(-4.7f, 5.2f, 5.6f, 5.2f, new[] { Yellow, Red, Pink }, false);
        Bed(4.7f, 5.2f, 5.6f, 5.2f, new[] { White, Violet, Yellow }, true);
        // ---- statues on plinths at the four junctions of the cross
        foreach (var (sx, sz) in new[] { (-3.2f, -3.2f), (3.2f, -3.2f), (-3.2f, 3.2f), (3.2f, 3.2f) })
        {
            n.AddChild(B(new Vector3(0.85f, 0.2f, 0.85f), StoneDark, P(sx, 0.1f, sz)));
            n.AddChild(B(new Vector3(0.6f, 1.0f, 0.6f), Stone, P(sx, 0.7f, sz)));
            n.AddChild(B(new Vector3(0.72f, 0.14f, 0.72f), StoneDark, P(sx, 1.27f, sz)));
            Vector3 b = P(sx, 1.34f, sz); float face = rng.RandfRange(0, 360);
            n.AddChild(Props.Part(Props.Cyl(0.2f, 0.9f, 8, 0.14f), White, b + Vector3.Up * 0.55f));              // a draped figure
            n.AddChild(Props.Part(Props.Ball(0.13f, 7), White, b + Vector3.Up * 1.14f));
            n.AddChild(B(new Vector3(0.07f, 0.55f, 0.07f), White, b + new Vector3(0.22f, 0.95f, 0), new Vector3(0, face, -35)));
            n.AddChild(B(new Vector3(0.07f, 0.5f, 0.07f), White, b + new Vector3(-0.22f, 0.85f, 0), new Vector3(0, face, 28)));
        }
        // ---- the rotunda at the far end: a stepped plinth, eight columns, an entablature, a dome and a finial
        Vector3 rc = Level(0, -9.6f, 0f);
        for (int k = 0; k < 3; k++) n.AddChild(Props.Part(Props.Cyl(2.4f - k * 0.25f, 0.18f, 16), Stone, rc + Vector3.Up * (0.09f + k * 0.18f)));
        for (int k = 0; k < 8; k++)
        {
            float a = k * MathF.Tau / 8f; Vector3 p = rc + new Vector3(MathF.Cos(a) * 1.75f, 0, MathF.Sin(a) * 1.75f);
            n.AddChild(Props.Part(Props.Cyl(0.19f, 2.6f, 10, 0.16f), White, p + Vector3.Up * 1.85f));
            n.AddChild(Props.Part(Props.Cyl(0.27f, 0.14f, 8), Stone, p + Vector3.Up * 0.62f));
            n.AddChild(Props.Part(Props.Cyl(0.3f, 0.14f, 8), Stone, p + Vector3.Up * 3.2f));
        }
        n.AddChild(Props.Part(Props.Cyl(2.1f, 0.35f, 16), Stone, rc + Vector3.Up * 3.45f));
        n.AddChild(Props.Part(Props.Cyl(2.2f, 0.12f, 16), Gold, rc + Vector3.Up * 3.68f));
        n.AddChild(Props.Part(Props.Cyl(0.1f, 1.5f, 16, 2.0f), Dome, rc + Vector3.Up * 4.5f));
        n.AddChild(Props.Part(Props.Ball(0.26f, 8), Gold, rc + Vector3.Up * 5.4f));
        n.AddChild(Props.Part(Props.Cyl(0.04f, 0.6f, 5), Gold, rc + Vector3.Up * 5.75f));
        n.AddChild(Props.Part(Props.Cyl(0.5f, 0.55f, 10, 0.4f), Stone, rc + Vector3.Up * 0.5f));                 // a pedestal with a bust inside
        n.AddChild(Props.Part(Props.Ball(0.2f, 8), White, rc + Vector3.Up * 1.05f));
        // ---- the rose arch and trellis at the near end
        foreach (float z in new[] { D / 2 - 0.6f, D / 2 - 2.2f, D / 2 - 3.8f })
        {
            foreach (float x in new[] { -1.15f, 1.15f }) n.AddChild(B(new Vector3(0.12f, 2.6f, 0.12f), White, P(x, 1.3f, z)));
            for (int k = 0; k <= 8; k++)
            {
                float a = Mathf.Pi * k / 8f;
                n.AddChild(B(new Vector3(0.12f, 0.45f, 0.12f), White, P(MathF.Cos(a) * 1.15f, 2.7f + MathF.Sin(a) * 0.85f, z), new Vector3(0, 0, Mathf.RadToDeg(a) - 90f)));
            }
        }
        for (int k = 0; k < 30; k++)           // roses on the arches
        {
            float a = rng.RandfRange(0, Mathf.Pi), z = rng.RandfRange(D / 2 - 3.9f, D / 2 - 0.5f);
            n.AddChild(Props.Part(Props.Ball(rng.RandfRange(0.1f, 0.18f), 5), rng.Randf() < 0.6f ? Red : Pink, P(MathF.Cos(a) * 1.18f, 2.7f + MathF.Sin(a) * 0.88f, z)));
        }
        foreach (float x in new[] { -1.15f, 1.15f }) for (float z = D / 2 - 3.8f; z <= D / 2 - 0.6f; z += 0.7f)
            n.AddChild(Props.Part(Props.Ball(0.2f, 6), Green, P(x, rng.RandfRange(0.4f, 2.4f), z)));
        // ---- benches, urns, lanterns
        foreach (float x in new[] { -7.2f, 7.2f })
        {
            // The bench stands level whatever the slope: the seat at the higher end's height, each leg reaching down to the ground under it.
            float gA = G(x, -0.75f), gB = G(x, 0.75f), top = Math.Max(gA, gB), seat = top + 0.46f;
            n.AddChild(B(new Vector3(0.5f, 0.06f, 1.7f), Wood, new Vector3(x, seat, 0)));
            n.AddChild(B(new Vector3(0.06f, 0.5f, 1.7f), Wood, new Vector3(x - Math.Sign(x) * 0.22f, seat + 0.28f, 0), new Vector3(0, 0, Math.Sign(x) * -8)));
            foreach (float z in new[] { -0.7f, 0.7f })
            {
                float g = G(x, z);
                n.AddChild(B(new Vector3(0.5f, seat - g, 0.08f), White, new Vector3(x, (seat + g) * 0.5f - 0.015f, z)));
            }
        }
        foreach (var (ux, uz) in new[] { (-1.4f, -D / 2 + 1.6f), (1.4f, -D / 2 + 1.6f), (-1.4f, D / 2 - 5f), (1.4f, D / 2 - 5f), (-1.3f, 0f), (1.3f, 0f) })
        {
            n.AddChild(B(new Vector3(0.5f, 0.55f, 0.5f), StoneDark, P(ux, 0.28f, uz)));
            n.AddChild(Props.Part(Props.Cyl(0.3f, 0.45f, 10, 0.2f), White, P(ux, 0.78f, uz)));
            for (int k = 0; k < 7; k++) n.AddChild(Props.Part(Props.Ball(0.11f, 5), k % 2 == 0 ? Red : Yellow, P(ux + rng.RandfRange(-0.17f, 0.17f), 1.12f + rng.RandfRange(0, 0.12f), uz + rng.RandfRange(-0.17f, 0.17f))));
        }
        foreach (var (lx, lz) in new[] { (-1.4f, -4f), (1.4f, -4f), (-1.4f, 4f), (1.4f, 4f), (-7.5f, -7f), (7.5f, -7f), (-7.5f, 7f), (7.5f, 7f) })
        {
            n.AddChild(Props.Part(Props.Cyl(0.07f, 3.2f, 6, 0.045f), Iron, P(lx, 1.6f, lz)));
            n.AddChild(Props.Part(Props.Cyl(0.15f, 0.12f, 6), Iron, P(lx, 0.06f, lz)));
            n.AddChild(B(new Vector3(0.34f, 0.4f, 0.34f), new Color(0.9f, 0.85f, 0.6f), P(lx, 3.35f, lz)));
            n.AddChild(Props.Part(Props.Cyl(0.03f, 0.22f, 4, 0.3f), Iron, P(lx, 3.65f, lz)));
            n.AddChild(Props.Part(Props.Cyl(0.3f, 0.06f, 6), Iron, P(lx, 3.1f, lz)));
        }
        // ---- a cypress at every corner and along the long edges, clipped to a cone
        for (float z = -D / 2 + 0.8f; z <= D / 2 - 0.4f; z += 2.8f)
            foreach (float x in new[] { -W / 2 + 0.55f, W / 2 - 0.55f })
            {
                float h = rng.RandfRange(2.2f, 2.9f);
                n.AddChild(Props.Part(Props.Cyl(0.5f, h, 8, 0.05f), BuildKit.Tone(Cypress, rng, 0.1f), P(x, h * 0.5f, z)));
            }
        DrunkTractor.BatchStatic(n, null);
        return n;
    }
}
