using System;
using System.Collections.Generic;
using Godot;

namespace CCCPRacing.Track;

/// <summary>
/// A village vegetable garden (огород, "garden" prop) behind a house: a picket fence round raised beds of cabbages, potato rows, bean poles, carrots and sunflowers, a scarecrow, a rain barrel,
/// a compost heap with pumpkins, a wheelbarrow and a watering can. Every element is set on the ground under it (the plots lie on a slope). Local frame: 11 m (X) by 10 m (Z), gate on the -X side
/// (towards the house), origin the centre.
/// </summary>
public static class Garden
{
    public const float W = 11f, D = 10f;
    static readonly Color Pale = new(0.55f, 0.6f, 0.4f), Wood = new(0.5f, 0.4f, 0.27f), WoodDark = new(0.33f, 0.24f, 0.15f), Soil = new(0.25f, 0.18f, 0.12f), Straw = new(0.8f, 0.7f, 0.38f);
    static readonly Color Cabbage = new(0.45f, 0.62f, 0.34f), CabbageDark = new(0.33f, 0.5f, 0.28f), Potato = new(0.2f, 0.38f, 0.17f), Bean = new(0.26f, 0.5f, 0.2f), Iron = new(0.2f, 0.2f, 0.22f);

    public static IEnumerable<(float x, float z, float r)> Circles()
    {
        foreach (var (x, z) in new[] { (-W / 2, -D / 2), (W / 2, -D / 2), (W / 2, D / 2), (-W / 2, D / 2), (-W / 2, -0.8f), (-W / 2, 0.8f) }) yield return (x, z, 0.22f);
        yield return (0.5f, -3.2f, 0.35f);           // the scarecrow
        yield return (4.4f, 3.9f, 0.9f);             // compost
        yield return (-4.6f, -3.9f, 0.45f);          // the barrel
    }

    static MeshInstance3D B(Vector3 size, Color c, Vector3 pos, Vector3? rot = null) => Props.Part(new BoxMesh { Size = size }, c, pos, rot);

    public static Node3D Build(TrackData t, Vector3 origin, float rot, int seed)
    {
        var n = new Node3D();
        var rng = new RandomNumberGenerator { Seed = (ulong)Math.Abs(seed) + 7 };
        float s = MathF.Sin(rot), c = MathF.Cos(rot);
        float G(float lx, float lz) => t.MeshHeight(origin.X + lx * c + lz * s, origin.Z - lx * s + lz * c) - origin.Y;      // ground under a local point
        Vector3 P(float lx, float y, float lz) => new(lx, G(lx, lz) + y, lz);

        // ---- the picket fence: pickets, two rails, corner posts, a gate on the house side
        void Fence(float x0, float z0, float x1, float z1, bool gate)
        {
            float len = MathF.Sqrt((x1 - x0) * (x1 - x0) + (z1 - z0) * (z1 - z0)); int count = (int)(len / 0.3f);
            float yaw = Mathf.RadToDeg(MathF.Atan2(-(z1 - z0), x1 - x0));
            for (int k = 0; k <= count; k++)
            {
                float f = k / (float)count, x = Mathf.Lerp(x0, x1, f), z = Mathf.Lerp(z0, z1, f);
                if (gate && Math.Abs(z) < 0.75f) continue;
                float h = 0.85f + rng.RandfRange(-0.08f, 0.06f);
                n.AddChild(B(new Vector3(0.1f, h, 0.03f), BuildKit.Tone(Wood, rng, 0.16f), P(x, h * 0.5f, z), new Vector3(0, yaw + rng.RandfRange(-3, 3), rng.RandfRange(-2, 2))));
                if (k % 3 == 0) n.AddChild(Props.Part(new PrismMesh { Size = new Vector3(0.1f, 0.08f, 0.03f) }, BuildKit.Tone(Wood, rng, 0.1f), P(x, h + 0.04f, z), new Vector3(0, yaw, 0)));
            }
            int rails = Math.Max(1, (int)(len / 1.5f));
            for (int k = 0; k < rails; k++)
            {
                float f = (k + 0.5f) / rails, x = Mathf.Lerp(x0, x1, f), z = Mathf.Lerp(z0, z1, f);
                if (gate && Math.Abs(z) < 0.9f) continue;
                foreach (float y in new[] { 0.22f, 0.62f }) n.AddChild(B(new Vector3(len / rails + 0.04f, 0.06f, 0.05f), WoodDark, P(x, y, z), new Vector3(0, yaw, 0)));
            }
        }
        Fence(-W / 2, -D / 2, W / 2, -D / 2, false); Fence(-W / 2, D / 2, W / 2, D / 2, false);
        Fence(W / 2, -D / 2, W / 2, D / 2, false); Fence(-W / 2, -D / 2, -W / 2, D / 2, true);
        foreach (var (x, z) in new[] { (-W / 2, -D / 2), (W / 2, -D / 2), (W / 2, D / 2), (-W / 2, D / 2), (-W / 2, -0.85f), (-W / 2, 0.85f) })
            n.AddChild(B(new Vector3(0.14f, 1.15f, 0.14f), WoodDark, P(x, 0.57f, z)));
        n.AddChild(B(new Vector3(0.05f, 0.7f, 0.75f), Wood, P(-W / 2 - 0.15f, 0.45f, -0.4f), new Vector3(0, 55, 0)));   // the gate leaf, swung half open

        // ---- the beds: raised, edged with boards, each with its own crop
        float[] bedX = { -3.4f, -1.7f, 0.0f, 1.7f, 3.4f }; float bz0 = -4.3f, bz1 = 2.5f;
        for (int b = 0; b < 5; b++)
        {
            float x = bedX[b] + (b > 2 ? 0.9f : 0f);
            float mid = (bz0 + bz1) * 0.5f, len = bz1 - bz0;
            // The bed in 0.7 m lengths, each on the ground under it (a long box would float at one end on a slope), sunk a little into the slope across it.
            int segs = Math.Max(1, (int)MathF.Ceiling(len / 0.7f));
            for (int k = 0; k < segs; k++)
            {
                float zc = bz0 + (k + 0.5f) * len / segs, sl = len / segs + 0.02f;
                float gy = Math.Max(G(x - 0.6f, zc), G(x + 0.6f, zc));
                n.AddChild(B(new Vector3(1.25f, 0.34f, sl), Soil, new Vector3(x, gy + 0.08f, zc)));
                foreach (float e in new[] { -0.64f, 0.64f }) n.AddChild(B(new Vector3(0.05f, 0.3f, sl), WoodDark, new Vector3(x + e, G(x + e, zc) + 0.15f, zc)));
            }
            for (float z = bz0 + 0.4f; z < bz1; z += 0.55f)
            {
                switch (b)
                {
                    case 0:      // cabbages: two rows of big flat heads
                        foreach (float dx in new[] { -0.3f, 0.3f })
                        {
                            var cb = Props.Part(Props.Ball(0.26f, 7), (z * 7 % 2 > 1) ? Cabbage : CabbageDark, P(x + dx, 0.32f, z + rng.RandfRange(-0.06f, 0.06f))); cb.Scale = new Vector3(1.0f, 0.75f, 1.0f); n.AddChild(cb);
                            var lf = Props.Part(Props.Ball(0.18f, 5), Pale, P(x + dx, 0.45f, z)); lf.Scale = new Vector3(1.2f, 0.4f, 1.2f); n.AddChild(lf);
                        }
                        break;
                    case 1:      // potatoes: a ridge with leafy clumps
                        var pt = Props.Part(Props.Ball(0.3f, 6), BuildKit.Tone(Potato, rng, 0.15f), P(x + rng.RandfRange(-0.15f, 0.15f), 0.32f, z)); pt.Scale = new Vector3(1.3f, 0.8f, 1.0f); n.AddChild(pt);
                        break;
                    case 2:      // beans: poles in tent pairs with leaves
                        if (((int)((z - bz0) / 0.55f)) % 3 == 0)
                        {
                            foreach (float dx in new[] { -0.25f, 0.25f })
                            {
                                n.AddChild(Props.Part(Props.Cyl(0.02f, 2.0f, 4), WoodDark, P(x + dx, 1.0f, z), new Vector3(0, 0, -dx * 80f)));
                                var lf2 = Props.Part(Props.Ball(0.16f, 5), BuildKit.Tone(Bean, rng, 0.2f), P(x + dx * 0.55f, rng.RandfRange(0.5f, 1.6f), z)); lf2.Scale = new Vector3(1f, 1.6f, 0.7f); n.AddChild(lf2);
                            }
                            n.AddChild(Props.Part(Props.Cyl(0.015f, 0.8f, 4), WoodDark, P(x, 1.95f, z), new Vector3(0, 0, 90)));
                        }
                        break;
                    case 3:      // carrots and onions: rows of thin green tufts
                        foreach (float dx in new[] { -0.35f, 0f, 0.35f })
                            n.AddChild(B(new Vector3(0.03f, 0.3f, 0.03f), (z * 3 % 2 > 1) ? Bean : Potato, P(x + dx, 0.34f, z), new Vector3(rng.RandfRange(-15, 15), 0, rng.RandfRange(-15, 15))));
                        break;
                    default:     // sunflowers: tall stalks, a leaf pair and the yellow-brown head turned to the south
                        if (((int)((z - bz0) / 0.55f)) % 2 == 0)
                        {
                            float hgt = rng.RandfRange(1.5f, 2.0f);
                            n.AddChild(Props.Part(Props.Cyl(0.03f, hgt, 5), Bean, P(x, hgt * 0.5f, z)));
                            n.AddChild(Props.Part(Props.Cyl(0.17f, 0.06f, 9), new Color(0.9f, 0.7f, 0.1f), P(x, hgt + 0.02f, z + 0.08f), new Vector3(70, 0, 0)));
                            n.AddChild(Props.Part(Props.Cyl(0.1f, 0.07f, 8), new Color(0.3f, 0.2f, 0.1f), P(x, hgt + 0.02f, z + 0.1f), new Vector3(70, 0, 0)));
                            n.AddChild(B(new Vector3(0.3f, 0.02f, 0.14f), Bean, P(x, hgt * 0.55f, z), new Vector3(0, 0, 25)));
                        }
                        break;
                }
            }
        }
        // ---- the scarecrow
        var sc = P(0.5f, 0f, -3.2f);
        n.AddChild(Props.Part(Props.Cyl(0.035f, 2.0f, 5), WoodDark, sc + Vector3.Up * 1.0f));
        n.AddChild(Props.Part(Props.Cyl(0.03f, 1.5f, 5), WoodDark, sc + Vector3.Up * 1.55f, new Vector3(0, 0, 90)));
        n.AddChild(B(new Vector3(0.38f, 0.6f, 0.2f), new Color(0.46f, 0.4f, 0.28f), sc + Vector3.Up * 1.4f));
        foreach (float sx in new[] { -1f, 1f })
        {
            n.AddChild(B(new Vector3(0.45f, 0.35f, 0.04f), new Color(0.5f, 0.42f, 0.3f), sc + new Vector3(sx * 0.55f, 1.4f, 0), new Vector3(0, 0, sx * 12)));      // sleeves
            n.AddChild(B(new Vector3(0.04f, 0.3f, 0.02f), Straw, sc + new Vector3(sx * 0.82f, 1.2f, 0)));
            n.AddChild(B(new Vector3(0.18f, 0.3f, 0.03f), new Color(0.6f, 0.2f, 0.15f), sc + new Vector3(sx * 0.12f, 1.0f, 0.12f), new Vector3(0, 0, sx * 10)));      // rags
        }
        n.AddChild(Props.Part(Props.Ball(0.17f, 6), new Color(0.78f, 0.68f, 0.5f), sc + Vector3.Up * 1.88f));
        n.AddChild(Props.Part(Props.Cyl(0.27f, 0.04f, 9), new Color(0.35f, 0.28f, 0.18f), sc + Vector3.Up * 1.99f));
        n.AddChild(Props.Part(Props.Cyl(0.14f, 0.2f, 8, 0.12f), new Color(0.35f, 0.28f, 0.18f), sc + Vector3.Up * 2.1f));
        // ---- the corner by the gate: rain barrel with a downpipe-less lid, watering can, tools
        var bar = P(-4.6f, 0f, -3.9f);
        n.AddChild(Props.Part(Props.Cyl(0.38f, 0.9f, 10, 0.34f), Wood, bar + Vector3.Up * 0.45f));
        foreach (float y in new[] { 0.14f, 0.7f }) n.AddChild(Props.Part(Props.Cyl(0.385f, 0.04f, 10), Iron, bar + Vector3.Up * y));
        n.AddChild(Props.Part(Props.Cyl(0.33f, 0.02f, 10), new Color(0.05f, 0.08f, 0.1f), bar + Vector3.Up * 0.9f));
        var can = P(-4.0f, 0f, -3.2f);
        n.AddChild(Props.Part(Props.Cyl(0.15f, 0.3f, 8, 0.13f), new Color(0.35f, 0.45f, 0.5f), can + Vector3.Up * 0.15f));
        n.AddChild(B(new Vector3(0.4f, 0.03f, 0.03f), new Color(0.35f, 0.45f, 0.5f), can + new Vector3(0.25f, 0.28f, 0), new Vector3(0, 0, 30)));
        n.AddChild(B(new Vector3(0.03f, 1.5f, 0.05f), WoodDark, P(-5.2f, 0.7f, 2.8f), new Vector3(0, 0, -10)));       // a spade and a hoe on the fence
        n.AddChild(B(new Vector3(0.2f, 0.25f, 0.03f), Iron, P(-5.28f, 1.4f, 2.8f)));
        n.AddChild(B(new Vector3(0.03f, 1.4f, 0.05f), WoodDark, P(-5.2f, 0.7f, 3.3f), new Vector3(0, 0, -8)));
        n.AddChild(B(new Vector3(0.25f, 0.03f, 0.04f), Iron, P(-5.28f, 1.33f, 3.3f)));
        // wheelbarrow
        var wb = P(-4.3f, 0f, 3.6f);
        n.AddChild(B(new Vector3(0.9f, 0.1f, 0.55f), Wood, wb + new Vector3(0, 0.45f, 0)));
        foreach (float sz in new[] { -1f, 1f }) { n.AddChild(B(new Vector3(0.9f, 0.22f, 0.04f), Wood, wb + new Vector3(0, 0.58f, sz * 0.27f))); n.AddChild(B(new Vector3(0.95f, 0.04f, 0.04f), WoodDark, wb + new Vector3(-0.1f, 0.4f, sz * 0.25f), new Vector3(0, 0, 8))); }
        n.AddChild(Props.Part(Props.Cyl(0.22f, 0.06f, 12), new Color(0.12f, 0.12f, 0.12f), wb + new Vector3(0.58f, 0.22f, 0), new Vector3(90, 0, 0)));
        // compost heap with pumpkins
        var cp = P(4.4f, 0f, 3.9f);
        for (int k = 0; k < 9; k++) { var h = Props.Part(Props.Ball(rng.RandfRange(0.3f, 0.5f), 6), BuildKit.Tone(new Color(0.3f, 0.24f, 0.14f), rng, 0.25f), cp + new Vector3(rng.RandfRange(-0.6f, 0.6f), rng.RandfRange(0.15f, 0.4f), rng.RandfRange(-0.6f, 0.6f))); h.Scale = new Vector3(1.3f, 0.7f, 1.1f); n.AddChild(h); }
        foreach (var (px, pz) in new[] { (-0.8f, 0.5f), (-1.1f, -0.1f), (-0.9f, 1.0f) })
        {
            var pk = Props.Part(Props.Ball(0.22f, 7), new Color(0.86f, 0.5f, 0.12f), P(4.4f + px, 0.2f, 3.9f + pz)); pk.Scale = new Vector3(1.1f, 0.85f, 1.1f); n.AddChild(pk);
            n.AddChild(Props.Part(Props.Cyl(0.03f, 0.1f, 4), Bean, P(4.4f + px, 0.4f, 3.9f + pz)));
        }
        DrunkTractor.BatchStatic(n, null);
        return n;
    }
}
