using System;
using System.Collections.Generic;
using Godot;

namespace CCCPRacing.Track;

/// <summary>
/// The old water mill below the ford ("watermill" prop): a rubble-stone ground floor with quoins and a plank-sided upper storey under a shingled gable roof with a hoist beam, loading hatch,
/// stone chimney and shuttered windows; an overshot wheel (rims, spokes, buckets, axle on stone piers) in the river on one long side, fed by a plank flume on trestles from the north;
/// millstones, sacks, a handcart, a barrel and a woodpile in the yard. The wheel is a still model (it does not turn). Local frame: the body centred at the origin, ridge along Z, the wheel at
/// (-7.1, 0.9, 0) on the river side (-X), the flume running off to -Z.
/// </summary>
public static class WaterMill
{
    static readonly Color Stone = new(0.55f, 0.52f, 0.46f), Quoin = new(0.72f, 0.69f, 0.6f), Plank = new(0.44f, 0.32f, 0.2f), PlankDark = new(0.3f, 0.21f, 0.13f);
    static readonly Color Shingle = new(0.36f, 0.3f, 0.25f), Dark = new(0.06f, 0.06f, 0.07f), Iron = new(0.2f, 0.2f, 0.22f), Frame = new(0.62f, 0.55f, 0.42f), Shutter = new(0.22f, 0.36f, 0.34f);
    static readonly Color Wet = new(0.3f, 0.24f, 0.17f), Sack = new(0.76f, 0.69f, 0.5f), Millstone = new(0.6f, 0.58f, 0.52f);

    const float Hx = 3.5f, Hz = 4.8f, StoneH = 3.0f, WoodH = 2.5f, EaveY = StoneH + WoodH, Rise = 2.4f;
    internal static readonly Vector3 WheelCentre = new(-7.1f, 0.5f, 0f);
    internal const float WheelR = 2.4f;

    public static IEnumerable<(float x, float z, float r)> Circles()
    {
        for (float z = -3.8f; z <= 3.85f; z += 1.9f) { yield return (-2.0f, z, 1.9f); yield return (2.0f, z, 1.9f); }
        yield return (2.6f, -3.4f, 0.7f);                                       // the chimney stands in the wall line
        foreach (float x in new[] { -8.4f, -7.1f, -5.8f }) yield return (x, 0f, 1.0f);       // the wheel
        foreach (float z in new[] { -1.3f, 1.3f }) yield return (-7.4f, z, 0.8f);            // its piers
        foreach (float z in new[] { -5.6f, -3.8f, -2.0f, -0.3f }) { yield return (-7.9f, z, 0.22f); yield return (-6.3f, z, 0.22f); }   // flume trestles
        yield return (6.6f, -1.2f, 1.0f);                                       // the handcart
        yield return (5.5f, 6.4f, 0.9f);                                        // sacks
    }

    static MeshInstance3D B(Vector3 size, Color c, Vector3 pos, Vector3? rot = null) => Props.Part(new BoxMesh { Size = size }, c, pos, rot);

    static void Window(Node3D n, Vector3 c, bool alongX, float w, float h, bool shutters, Vector3 outward)
    {
        // A framed window in a wall: glass, frame, mullion, sill and (optionally) open shutters.
        Vector3 depth = outward * 0.04f;
        n.AddChild(B(alongX ? new Vector3(w, h, 0.1f) : new Vector3(0.1f, h, w), Dark, c + depth));
        float fw = 0.09f;
        n.AddChild(B(alongX ? new Vector3(w + 0.2f, fw, 0.16f) : new Vector3(0.16f, fw, w + 0.2f), Frame, c + outward * 0.07f + Vector3.Up * (h * 0.5f + fw * 0.5f)));
        n.AddChild(B(alongX ? new Vector3(w + 0.3f, 0.12f, 0.34f) : new Vector3(0.34f, 0.12f, w + 0.3f), Quoin, c + outward * 0.12f + Vector3.Down * (h * 0.5f + 0.06f)));
        foreach (float s in new[] { -1f, 1f })
            n.AddChild(B(alongX ? new Vector3(fw, h, 0.16f) : new Vector3(0.16f, h, fw), Frame, c + outward * 0.07f + (alongX ? new Vector3(s * (w * 0.5f + fw * 0.5f), 0, 0) : new Vector3(0, 0, s * (w * 0.5f + fw * 0.5f)))));
        n.AddChild(B(alongX ? new Vector3(fw * 0.7f, h, 0.14f) : new Vector3(0.14f, h, fw * 0.7f), Frame, c + outward * 0.07f));
        n.AddChild(B(alongX ? new Vector3(w, fw * 0.7f, 0.14f) : new Vector3(0.14f, fw * 0.7f, w), Frame, c + outward * 0.07f));
        if (shutters)
            foreach (float s in new[] { -1f, 1f })
            {
                Vector3 side = alongX ? new Vector3(s * (w * 0.5f + 0.32f), 0, 0) : new Vector3(0, 0, s * (w * 0.5f + 0.32f));
                n.AddChild(B(alongX ? new Vector3(0.5f, h + 0.06f, 0.05f) : new Vector3(0.05f, h + 0.06f, 0.5f), Shutter, c + outward * 0.2f + side, new Vector3(0, alongX ? s * 26f : (outward.X > 0 ? -s * 26f + 90f : s * 26f + 90f), 0)));
            }
    }

    public static Node3D Build()
    {
        var n = new Node3D();
        // ---- the stone ground floor: coursed rubble with quoins and openings
        float sw = 0.55f;
        // West wall (the wheel side): the axle goes through it, one small window.
        BuildKit.Masonry(n, new Vector3(-Hx, 0, -Hz), false, 2 * Hz, StoneH, sw, 0.36f, 0.45f, 1.0f, Stone, 11, 0.13f,
            new BuildKit.Hole(4.5f, 5.1f, 0.2f, 0.85f), new BuildKit.Hole(1.5f, 2.4f, 1.3f, 2.2f), new BuildKit.Hole(7.2f, 8.1f, 1.3f, 2.2f));
        // East wall: the mill door and two windows.
        BuildKit.Masonry(n, new Vector3(Hx, 0, -Hz), false, 2 * Hz, StoneH, sw, 0.36f, 0.45f, 1.0f, Stone, 12, 0.13f,
            new BuildKit.Hole(4.2f, 5.4f, 0f, 2.35f), new BuildKit.Hole(1.5f, 2.4f, 1.3f, 2.2f), new BuildKit.Hole(7.2f, 8.1f, 1.3f, 2.2f));
        // South wall: a wide cart door; north wall: one window.
        BuildKit.Masonry(n, new Vector3(-Hx, 0, Hz), true, 2 * Hx, StoneH, sw, 0.36f, 0.45f, 1.0f, Stone, 13, 0.13f, new BuildKit.Hole(2.4f, 4.6f, 0f, 2.5f));
        BuildKit.Masonry(n, new Vector3(-Hx, 0, -Hz), true, 2 * Hx, StoneH, sw, 0.36f, 0.45f, 1.0f, Stone, 14, 0.13f, new BuildKit.Hole(3.0f, 4.0f, 1.3f, 2.2f));
        // Corner quoins: big pale dressed blocks, alternately long on one face and the other.
        var rq = new RandomNumberGenerator { Seed = 777 };
        foreach (float sx in new[] { -1f, 1f }) foreach (float sz in new[] { -1f, 1f })
            for (int k = 0; k < 8; k++)
            {
                bool even = k % 2 == 0;
                n.AddChild(B(new Vector3(even ? 0.95f : 0.55f, 0.37f, even ? 0.55f : 0.95f), BuildKit.Tone(Quoin, rq, 0.07f), new Vector3(sx * (Hx - (even ? 0.25f : 0.0f)), 0.19f + k * 0.375f, sz * (Hz - (even ? 0.0f : 0.25f)))));
            }
        // Stone plinth course and the timber floor beam that carries the upper storey.
        n.AddChild(B(new Vector3(2 * Hx + 0.5f, 0.12f, 2 * Hz + 0.5f), PlankDark, new Vector3(0, StoneH + 0.04f, 0)));
        // Door (east) and cart door (south): plank leaves, iron straps, stone lintels and a threshold.
        n.AddChild(B(new Vector3(0.12f, 2.3f, 1.1f), PlankDark, new Vector3(Hx - 0.05f, 1.15f, 0.6f)));
        for (float z = 0.1f; z < 1.1f; z += 0.22f) n.AddChild(B(new Vector3(0.05f, 2.25f, 0.025f), Dark, new Vector3(Hx + 0.02f, 1.15f, z)));
        foreach (float y in new[] { 0.5f, 1.7f }) n.AddChild(B(new Vector3(0.06f, 0.1f, 1.0f), Iron, new Vector3(Hx + 0.03f, y, 0.6f)));
        n.AddChild(B(new Vector3(0.7f, 0.3f, 1.7f), Quoin, new Vector3(Hx, 2.5f, 0.6f)));
        n.AddChild(B(new Vector3(1.0f, 0.18f, 1.6f), Quoin, new Vector3(Hx + 0.5f, 0.09f, 0.6f)));
        n.AddChild(B(new Vector3(0.8f, 0.18f, 1.9f), Quoin, new Vector3(Hx + 1.0f, 0.04f, 0.6f)));
        n.AddChild(B(new Vector3(2.1f, 0.12f, 2.9f), PlankDark, new Vector3(0, 1.1f, Hz - 0.1f)));        // (cart-door interior shade)
        n.AddChild(B(new Vector3(2.0f, 2.3f, 0.12f), Dark, new Vector3(-0.1f, 1.25f, Hz - 0.12f)));
        n.AddChild(B(new Vector3(2.6f, 0.3f, 0.7f), Quoin, new Vector3(-0.1f, 2.65f, Hz)));
        foreach (float s in new[] { -1f, 1f }) n.AddChild(B(new Vector3(0.9f, 2.2f, 0.06f), PlankDark, new Vector3(-0.1f + s * 1.0f, 1.25f, Hz + 0.2f), new Vector3(0, s * 62f, 0)));   // the two leaves, thrown open
        // Windows (ground floor).
        foreach (float z in new[] { -Hz + 1.95f, -Hz + 7.65f }) { Window(n, new Vector3(Hx, 1.75f, z), false, 0.9f, 0.9f, true, new Vector3(1, 0, 0)); Window(n, new Vector3(-Hx, 1.75f, z), false, 0.9f, 0.9f, false, new Vector3(-1, 0, 0)); }
        Window(n, new Vector3(Hx, 1.75f, -Hz + 1.95f + 0f), false, 0.9f, 0.9f, true, new Vector3(1, 0, 0));
        Window(n, new Vector3(0.5f, 1.75f, -Hz), true, 0.9f, 0.9f, true, new Vector3(0, 0, -1));
        // The axle's stone-lined opening on the wheel side.
        n.AddChild(B(new Vector3(0.9f, 0.14f, 0.75f), Quoin, new Vector3(-Hx - 0.1f, 0.9f, 0f)));

        // ---- the timber upper storey
        float pw = 0.2f;
        BuildKit.Planks(n, new Vector3(-Hx + 0.1f, StoneH, -Hz + 0.1f), false, 2 * Hz - 0.2f, WoodH, pw, Plank, 21, new BuildKit.Hole(1.5f, 2.4f, 0.75f, 1.75f), new BuildKit.Hole(7.2f, 8.1f, 0.75f, 1.75f));
        BuildKit.Planks(n, new Vector3(Hx - 0.1f, StoneH, -Hz + 0.1f), false, 2 * Hz - 0.2f, WoodH, pw, Plank, 22, new BuildKit.Hole(1.5f, 2.4f, 0.75f, 1.75f), new BuildKit.Hole(7.2f, 8.1f, 0.75f, 1.75f));
        BuildKit.Planks(n, new Vector3(-Hx + 0.1f, StoneH, Hz - 0.1f), true, 2 * Hx - 0.2f, WoodH, pw, Plank, 23, new BuildKit.Hole(2.1f, 4.5f, 0.1f, 2.1f));
        BuildKit.Planks(n, new Vector3(-Hx + 0.1f, StoneH, -Hz + 0.1f), true, 2 * Hx - 0.2f, WoodH, pw, Plank, 24, new BuildKit.Hole(2.9f, 3.9f, 0.8f, 1.7f));
        foreach (float sx in new[] { -1f, 1f }) foreach (float sz in new[] { -1f, 1f })
            n.AddChild(B(new Vector3(0.28f, WoodH, 0.28f), PlankDark, new Vector3(sx * (Hx - 0.05f), StoneH + WoodH * 0.5f, sz * (Hz - 0.05f))));
        foreach (float z in new[] { -Hz + 1.95f, -Hz + 7.65f })
        {
            Window(n, new Vector3(Hx + 0.02f, StoneH + 1.25f, z), false, 0.9f, 0.9f, true, new Vector3(1, 0, 0));
            Window(n, new Vector3(-Hx - 0.02f, StoneH + 1.25f, z), false, 0.9f, 0.9f, true, new Vector3(-1, 0, 0));
        }
        n.AddChild(B(new Vector3(2 * Hx + 0.3f, 0.3f, 0.34f), PlankDark, new Vector3(0, EaveY + 0.0f, Hz)));          // wall plates
        n.AddChild(B(new Vector3(2 * Hx + 0.3f, 0.3f, 0.34f), PlankDark, new Vector3(0, EaveY, -Hz)));
        foreach (float sx in new[] { -1f, 1f }) n.AddChild(B(new Vector3(0.34f, 0.3f, 2 * Hz + 0.3f), PlankDark, new Vector3(sx * Hx, EaveY, 0)));
        // The loading hatch in the south gable wall with its hood, and the hoist beam over it.
        n.AddChild(B(new Vector3(2.4f, 2.1f, 0.12f), Dark, new Vector3(0.0f, StoneH + 1.15f, Hz - 0.2f)));
        foreach (float s in new[] { -1f, 1f }) n.AddChild(B(new Vector3(0.85f, 2.0f, 0.05f), PlankDark, new Vector3(s * 1.45f, StoneH + 1.15f, Hz + 0.35f), new Vector3(0, s * 70f, 0)));
        n.AddChild(B(new Vector3(0.14f, 0.14f, 1.9f), PlankDark, new Vector3(0, EaveY + 2.25f, Hz + 0.85f)));
        n.AddChild(B(new Vector3(0.1f, 0.1f, 1.0f), PlankDark, new Vector3(0, EaveY + 1.9f, Hz + 0.55f), new Vector3(40, 0, 0)));
        n.AddChild(Props.Part(Props.Cyl(0.13f, 0.1f, 8), Iron, new Vector3(0, EaveY + 2.17f, Hz + 1.75f), new Vector3(0, 0, 90)));
        n.AddChild(Props.Part(Props.Cyl(0.015f, 3.6f, 4), Dark, new Vector3(0, EaveY + 0.35f, Hz + 1.75f)));
        n.AddChild(B(new Vector3(0.14f, 0.2f, 0.14f), Iron, new Vector3(0, EaveY - 1.5f, Hz + 1.75f)));
        for (int k = 0; k < 2; k++) n.AddChild(B(new Vector3(2.8f - k * 0.4f, 0.09f, 0.9f), Shingle, new Vector3(0, StoneH + 2.55f - k * 0.0f + (k == 0 ? 0 : 0.16f), Hz + 0.5f), new Vector3(-24 + k * 6, 0, 0)));   // the hood

        // ---- the roof
        float ridgeY = EaveY + Rise, halfSpan = Hx + 0.6f, theta = MathF.Atan2(Rise, Hx);
        foreach (float side in new[] { -1f, 1f }) BuildKit.ShingleSide(n, side, ridgeY, halfSpan, -Hz - 0.65f, Hz + 0.65f, theta, 0.34f, Shingle, side > 0 ? 31 : 32);
        n.AddChild(B(new Vector3(0.34f, 0.17f, 2 * Hz + 1.45f), PlankDark, new Vector3(0, ridgeY + 0.07f, 0)));
        foreach (float zz in new[] { -Hz, Hz })
        {
            n.AddChild(Props.Part(new PrismMesh { Size = new Vector3(2 * Hx - 0.1f, Rise, 0.2f) }, Plank, new Vector3(0, EaveY + Rise * 0.5f, zz - Math.Sign(zz) * 0.05f)));
            for (float x = -2.7f; x <= 2.71f; x += 0.45f)
            {
                float hgt = Rise * (1f - Math.Abs(x) / Hx);
                if (hgt > 0.2f) n.AddChild(B(new Vector3(0.05f, hgt - 0.05f, 0.05f), PlankDark, new Vector3(x, EaveY + hgt * 0.5f + 0.02f, zz + Math.Sign(zz) * 0.08f)));
            }
            foreach (float side in new[] { -1f, 1f })   // barge boards
                n.AddChild(B(new Vector3(MathF.Sqrt(Hx * Hx + Rise * Rise) + 0.9f, 0.26f, 0.08f), PlankDark, new Vector3(side * (Hx + 0.15f) * 0.5f - side * 0.0f, EaveY + Rise * 0.5f - 0.05f, zz + Math.Sign(zz) * 0.58f), new Vector3(0, 0, side > 0 ? -Mathf.RadToDeg(theta) : Mathf.RadToDeg(theta))));
        }
        Window(n, new Vector3(0, EaveY + 1.0f, -Hz - 0.12f), true, 0.7f, 0.7f, true, new Vector3(0, 0, -1));      // the loft window in the north gable
        // Stone chimney through the roof, laid up course by course.
        float cx = 2.6f, cz = -3.4f, ch = 9.4f;
        BuildKit.Masonry(n, new Vector3(cx - 0.45f, 0, cz - 0.45f), true, 0.9f, ch, 0.34f, 0.34f, 0.3f, 0.55f, Stone, 41, 0.12f);
        BuildKit.Masonry(n, new Vector3(cx - 0.45f, 0, cz + 0.45f), true, 0.9f, ch, 0.34f, 0.34f, 0.3f, 0.55f, Stone, 42, 0.12f);
        BuildKit.Masonry(n, new Vector3(cx - 0.45f, 0, cz - 0.45f), false, 0.9f, ch, 0.34f, 0.34f, 0.3f, 0.55f, Stone, 43, 0.12f);
        BuildKit.Masonry(n, new Vector3(cx + 0.45f, 0, cz - 0.45f), false, 0.9f, ch, 0.34f, 0.34f, 0.3f, 0.55f, Stone, 44, 0.12f);
        n.AddChild(B(new Vector3(1.25f, 0.18f, 1.25f), Quoin, new Vector3(cx, ch + 0.09f, cz)));
        n.AddChild(B(new Vector3(0.9f, 0.14f, 0.9f), Dark, new Vector3(cx, ch + 0.25f, cz)));
        n.AddChild(Props.Part(Props.Cyl(0.3f, 0.5f, 8, 0.24f), Iron, new Vector3(cx, ch + 0.5f, cz)));

        // ---- the wheel (overshot): hub, axle, rims, spokes, buckets, piers, bearings
        Vector3 w = WheelCentre;
        n.AddChild(Props.Part(Props.Cyl(0.17f, 3.6f, 8), PlankDark, w, new Vector3(90, 0, 0)));
        n.AddChild(Props.Part(Props.Cyl(0.34f, 1.2f, 10), Plank, w, new Vector3(90, 0, 0)));
        foreach (float z in new[] { -0.75f, 0.75f })
        {
            BuildKit.RimRing(n, w + new Vector3(0, 0, z), WheelR, 20, 0.14f, 0.26f, Plank);
            BuildKit.RimRing(n, w + new Vector3(0, 0, z), WheelR - 0.45f, 20, 0.1f, 0.09f, PlankDark);
            for (int k = 0; k < 8; k++)
            {
                float a = k * MathF.Tau / 8f + (z > 0 ? 0.1f : 0f);
                n.AddChild(B(new Vector3(WheelR - 0.3f, 0.15f, 0.1f), Plank, w + new Vector3(MathF.Cos(a), MathF.Sin(a), 0) * (WheelR - 0.3f) * 0.5f + new Vector3(0, 0, z * 0.95f), new Vector3(0, 0, Mathf.RadToDeg(a))));
            }
        }
        for (int k = 0; k < 20; k++)
        {
            float a = (k + 0.5f) * MathF.Tau / 20f;
            Vector3 radial = new(MathF.Cos(a), MathF.Sin(a), 0);
            n.AddChild(B(new Vector3(0.5f, 0.06f, 1.5f), (k % 3 == 0) ? Wet : Plank, w + radial * (WheelR - 0.2f), new Vector3(0, 0, Mathf.RadToDeg(a) + 34f)));   // the bucket boards, raked
            n.AddChild(B(new Vector3(0.06f, 0.4f, 1.5f), PlankDark, w + radial * (WheelR - 0.55f), new Vector3(0, 0, Mathf.RadToDeg(a))));
        }
        foreach (float z in new[] { -1.35f, 1.35f })
        {
            BuildKit.Masonry(n, new Vector3(-7.9f, -2.3f, z), true, 1.1f, 2.5f, 0.8f, 0.34f, 0.35f, 0.6f, Stone, 51 + (int)z, 0.13f);
            n.AddChild(B(new Vector3(1.3f, 0.26f, 0.9f), PlankDark, new Vector3(-7.35f, 0.22f, z)));
        }
        // ---- the flume: a plank trough on trestles from the north, over the wheel's top
        float fx = WheelCentre.X, y1 = WheelCentre.Y + WheelR + 0.05f;
        float zFar = -6.4f, zNear = 0.55f;
        for (int k = 0; k < 6; k++)
        {
            float z0 = Mathf.Lerp(zFar, zNear, k / 6f), z1 = Mathf.Lerp(zFar, zNear, (k + 1) / 6f), y = Mathf.Lerp(y1 + 0.5f, y1, (k + 0.5f) / 6f);
            n.AddChild(B(new Vector3(1.0f, 0.1f, z1 - z0 + 0.02f), PlankDark, new Vector3(fx, y, (z0 + z1) * 0.5f), new Vector3((y1 + 0.5f - y1) / (zNear - zFar) * 57f * 0 + 0, 0, 0)));
            foreach (float s in new[] { -1f, 1f }) n.AddChild(B(new Vector3(0.08f, 0.5f, z1 - z0 + 0.02f), Plank, new Vector3(fx + s * 0.5f, y + 0.28f, (z0 + z1) * 0.5f)));
        }
        foreach (float z in new[] { -5.6f, -3.8f, -2.0f, -0.3f })
        {
            float y = Mathf.Lerp(y1 + 0.5f, y1, (z - zFar) / (zNear - zFar));
            foreach (float s in new[] { -1f, 1f })
                n.AddChild(B(new Vector3(0.16f, y + 0.5f, 0.16f), PlankDark, new Vector3(fx + s * 0.8f, (y - 0.3f) * 0.5f, z), new Vector3(0, 0, -s * 8f)));
            n.AddChild(B(new Vector3(1.9f, 0.14f, 0.18f), PlankDark, new Vector3(fx, y - 0.12f, z)));
            n.AddChild(B(new Vector3(1.3f, 0.1f, 0.1f), Plank, new Vector3(fx, y - 1.2f, z), new Vector3(0, 0, 0)));
        }
        n.AddChild(B(new Vector3(1.3f, 0.5f, 0.1f), PlankDark, new Vector3(fx, y1 + 0.55f, zFar - 0.05f)));          // the intake's sluice board
        n.AddChild(B(new Vector3(0.1f, 1.1f, 0.1f), Iron, new Vector3(fx, y1 + 0.95f, zFar - 0.05f)));
        n.AddChild(Props.Part(Props.Cyl(0.2f, 0.05f, 10), Iron, new Vector3(fx, y1 + 1.5f, zFar - 0.05f), new Vector3(90, 0, 0)));

        // ---- the yard: millstones, sacks, handcart, barrel, woodpile
        foreach (var (x, y, z, r) in new[] { (5.9f, 0.12f, 3.0f, 0.75f), (5.9f, 0.36f, 3.0f, 0.7f) })
        {
            n.AddChild(Props.Part(Props.Cyl(r, 0.24f, 18), Millstone, new Vector3(x, y, z)));
            n.AddChild(Props.Part(Props.Cyl(0.14f, 0.26f, 8), Dark, new Vector3(x, y, z)));
            for (int k = 0; k < 8; k++) n.AddChild(B(new Vector3(r * 0.8f, 0.02f, 0.025f), BuildKit.Tone(Millstone, rq, 0.1f).Darkened(0.25f), new Vector3(x + MathF.Cos(k * 0.785f) * r * 0.45f, y + 0.13f, z + MathF.Sin(k * 0.785f) * r * 0.45f), new Vector3(0, -k * 45f, 0)));
        }
        n.AddChild(Props.Part(Props.Cyl(0.7f, 0.24f, 18), Millstone, new Vector3(5.2f, 0.72f, -3.1f), new Vector3(0, 0, 78)));
        n.AddChild(Props.Part(Props.Cyl(0.14f, 0.26f, 8), Dark, new Vector3(5.2f, 0.72f, -3.1f), new Vector3(0, 0, 78)));
        var rs = new RandomNumberGenerator { Seed = 99 };
        foreach (var (x, y, z) in new[] { (4.7f, 0.34f, 6.1f), (5.4f, 0.34f, 6.2f), (6.1f, 0.34f, 6.0f), (4.9f, 0.34f, 6.9f), (5.7f, 0.34f, 6.8f), (5.2f, 0.9f, 6.5f) })
        {
            var sk = Props.Part(Props.Ball(0.38f, 8), BuildKit.Tone(Sack, rs, 0.08f), new Vector3(x, y, z), new Vector3(0, rs.RandfRange(0, 180), rs.RandfRange(-8, 8)));
            sk.Scale = new Vector3(1.0f, 1.0f, 1.55f); n.AddChild(sk);
            n.AddChild(Props.Part(Props.Ball(0.1f, 6), Sack.Darkened(0.2f), new Vector3(x, y + 0.36f, z)));
        }
        // handcart
        n.AddChild(B(new Vector3(1.7f, 0.09f, 1.0f), Plank, new Vector3(6.6f, 0.68f, -1.2f)));
        foreach (float s in new[] { -1f, 1f })
        {
            n.AddChild(B(new Vector3(1.7f, 0.3f, 0.06f), PlankDark, new Vector3(6.6f, 0.86f, -1.2f + s * 0.5f)));
            n.AddChild(B(new Vector3(1.9f, 0.06f, 0.07f), PlankDark, new Vector3(7.5f, 0.6f, -1.2f + s * 0.4f), new Vector3(0, 0, -6)));
            n.AddChild(Props.Part(Props.Cyl(0.5f, 0.07f, 16), Plank, new Vector3(6.6f, 0.5f, -1.2f + s * 0.62f), new Vector3(90, 0, 0)));
            for (int k = 0; k < 4; k++) n.AddChild(B(new Vector3(1.0f, 0.06f, 0.05f), PlankDark, new Vector3(6.6f, 0.5f, -1.2f + s * 0.67f), new Vector3(0, 0, k * 45f)));
        }
        n.AddChild(B(new Vector3(0.06f, 0.36f, 0.06f), PlankDark, new Vector3(8.4f, 0.4f, -1.2f)));
        n.AddChild(Props.Part(Props.Cyl(0.32f, 0.75f, 10, 0.28f), Plank, new Vector3(5.2f, 0.38f, 8.0f)));         // barrel with hoops
        foreach (float y in new[] { 0.15f, 0.6f }) n.AddChild(Props.Part(Props.Cyl(0.335f, 0.05f, 10), Iron, new Vector3(5.2f, y, 8.0f)));
        for (int row = 0; row < 3; row++) for (int k = 0; k < 6 - row; k++)           // woodpile on the north side
            n.AddChild(Props.Part(Props.Cyl(0.11f, 1.4f, 7), BuildKit.Tone(PlankDark, rs, 0.2f), new Vector3(-1.2f + k * 0.23f + row * 0.115f, 0.12f + row * 0.2f, -Hz - 1.0f), new Vector3(0, 0, 90)));

        // ---- weathering and the small things that make it lived in
        var Moss = new Color(0.3f, 0.43f, 0.22f); var Ivy = new Color(0.22f, 0.37f, 0.18f); var Glass = new Color(0.55f, 0.7f, 0.74f);
        for (int k = 0; k < 22; k++)       // moss along the foot of the walls and under the wheel side, and damp stains
        {
            float side = rs.Randf() < 0.5f ? -1f : 1f; bool alongZ = rs.Randf() < 0.65f;
            Vector3 p = alongZ ? new Vector3(side * (Hx + 0.28f), 0.08f, rs.RandfRange(-Hz, Hz)) : new Vector3(rs.RandfRange(-Hx, Hx), 0.08f, side * (Hz + 0.28f));
            var m = Props.Part(Props.Ball(rs.RandfRange(0.14f, 0.3f), 5), BuildKit.Tone(Moss, rs, 0.15f), p); m.Scale = new Vector3(1.4f, 0.4f, 1.4f); n.AddChild(m);
        }
        for (int k = 0; k < 7; k++) n.AddChild(B(new Vector3(0.05f, rs.RandfRange(0.5f, 1.4f), rs.RandfRange(0.25f, 0.5f)), Wet, new Vector3(-Hx - 0.3f, rs.RandfRange(0.4f, 1.8f), rs.RandfRange(-4.2f, 4.2f))));
        // Ivy up the north wall and round its window: clustered leaf blobs on thin stems.
        for (int k = 0; k < 40; k++)
        {
            float u = rs.RandfRange(-3.2f, -0.4f), v = rs.RandfRange(0.1f, 3.3f) + (u > -1.8f ? 0.8f : 0f);
            var lf = Props.Part(Props.Ball(rs.RandfRange(0.12f, 0.24f), 5), BuildKit.Tone(Ivy, rs, 0.2f), new Vector3(-Hx + 0.5f + (u + 3.2f) * 0.5f, v, -Hz - 0.32f)); lf.Scale = new Vector3(1.2f, 0.9f, 0.45f); n.AddChild(lf);
        }
        for (int k = 0; k < 4; k++) n.AddChild(B(new Vector3(0.04f, rs.RandfRange(1.2f, 3.4f), 0.04f), PlankDark, new Vector3(-Hx + 0.55f + k * 0.35f, 1.8f, -Hz - 0.3f)));
        // Moss and a few gaps on the roof, and a gutter with a downspout into a rain barrel on the east side.
        foreach (float side in new[] { -1f, 1f })
            for (int k = 0; k < 6; k++)
            {
                float t = rs.RandfRange(0.4f, 4.4f), zz = rs.RandfRange(-5f, 5f);
                var rp = new Vector3(side * t * MathF.Cos(theta), ridgeY - t * MathF.Sin(theta) + 0.05f + 0.012f * (t / 0.34f), zz);
                bool gap = k % 5 == 0;
                n.AddChild(B(gap ? new Vector3(0.28f, 0.02f, 0.4f) : new Vector3(rs.RandfRange(0.25f, 0.5f), 0.02f, rs.RandfRange(0.3f, 0.7f)), gap ? Dark : BuildKit.Tone(new Color(0.27f, 0.33f, 0.21f), rs, 0.12f), rp, new Vector3(0, 0, Mathf.RadToDeg(side > 0 ? -theta : theta))));
            }
        n.AddChild(B(new Vector3(0.14f, 0.12f, 2 * Hz + 1.2f), Iron, new Vector3(Hx + 0.62f, EaveY - 0.62f, 0)));
        n.AddChild(Props.Part(Props.Cyl(0.05f, 3.4f, 6), Iron, new Vector3(Hx + 0.66f, 2.4f, -Hz + 0.4f)));
        n.AddChild(Props.Part(Props.Cyl(0.42f, 0.95f, 10, 0.37f), Plank, new Vector3(Hx + 0.95f, 0.48f, -Hz + 0.4f)));
        foreach (float y in new[] { 0.14f, 0.5f, 0.85f }) n.AddChild(Props.Part(Props.Cyl(0.435f, 0.04f, 10, 0.435f), Iron, new Vector3(Hx + 0.95f, y, -Hz + 0.4f)));
        n.AddChild(Props.Part(Props.Cyl(0.35f, 0.02f, 10), Dark, new Vector3(Hx + 0.95f, 0.95f, -Hz + 0.4f)));
        // A dormer with a hatch window on the east slope, a weathervane at the south end of the ridge.
        float dz = 1.6f, dxx = 2.45f, dy = ridgeY - dxx * MathF.Tan(theta);
        n.AddChild(B(new Vector3(0.1f, 0.95f, 1.15f), Plank, new Vector3(dxx + 0.55f, dy + 0.1f, dz)));
        foreach (float sz in new[] { -1f, 1f }) n.AddChild(B(new Vector3(0.95f, 0.9f, 0.08f), PlankDark, new Vector3(dxx + 0.12f, dy + 0.12f, dz + sz * 0.57f)));
        n.AddChild(B(new Vector3(0.06f, 0.5f, 0.7f), Glass.Darkened(0.5f), new Vector3(dxx + 0.62f, dy + 0.12f, dz)));
        n.AddChild(B(new Vector3(0.12f, 0.06f, 1.4f), Quoin, new Vector3(dxx + 0.62f, dy - 0.38f, dz)));
        n.AddChild(Props.Part(new PrismMesh { Size = new Vector3(1.4f, 0.55f, 1.2f) }, Shingle, new Vector3(dxx + 0.1f, dy + 0.85f, dz), new Vector3(0, 90, 0)));
        n.AddChild(Props.Part(Props.Cyl(0.025f, 1.0f, 5), Iron, new Vector3(0, ridgeY + 0.6f, Hz + 0.4f)));
        n.AddChild(B(new Vector3(0.6f, 0.05f, 0.03f), Iron, new Vector3(0, ridgeY + 1.05f, Hz + 0.4f)));
        n.AddChild(B(new Vector3(0.28f, 0.2f, 0.02f), Iron, new Vector3(0.38f, ridgeY + 1.08f, Hz + 0.4f)));
        // The door: a lantern on its iron bracket, a latch, a plank bench, a broom, a flour-dusted step and the stone that props it.
        n.AddChild(B(new Vector3(0.08f, 0.08f, 0.7f), Iron, new Vector3(Hx + 0.1f, 2.6f, 1.55f)));
        n.AddChild(B(new Vector3(0.22f, 0.3f, 0.22f), Glass, new Vector3(Hx + 0.22f, 2.4f, 1.85f)));
        n.AddChild(B(new Vector3(0.28f, 0.05f, 0.28f), Iron, new Vector3(Hx + 0.22f, 2.58f, 1.85f)));
        n.AddChild(B(new Vector3(0.06f, 0.06f, 0.3f), Iron, new Vector3(Hx + 0.1f, 1.1f, 0.15f)));
        n.AddChild(B(new Vector3(0.5f, 0.06f, 1.5f), Plank, new Vector3(Hx + 0.5f, 0.45f, -0.95f)));
        foreach (float z in new[] { -1.6f, -0.3f }) n.AddChild(B(new Vector3(0.4f, 0.45f, 0.06f), PlankDark, new Vector3(Hx + 0.5f, 0.22f, z)));
        n.AddChild(Props.Part(Props.Cyl(0.015f, 1.5f, 4), Plank, new Vector3(Hx + 0.1f, 0.78f, -2.2f), new Vector3(0, 0, 12)));
        n.AddChild(Props.Part(new CylinderMesh { TopRadius = 0.12f, BottomRadius = 0.04f, Height = 0.3f, RadialSegments = 5 }, new Color(0.7f, 0.6f, 0.3f), new Vector3(Hx + 0.22f, 0.2f, -2.2f)));
        for (int k = 0; k < 8; k++) n.AddChild(B(new Vector3(rs.RandfRange(0.1f, 0.3f), 0.012f, rs.RandfRange(0.1f, 0.3f)), new Color(0.9f, 0.88f, 0.82f), new Vector3(Hx + 0.5f + rs.RandfRange(-0.4f, 0.8f), 0.185f, 0.6f + rs.RandfRange(-0.6f, 0.6f))));
        // Flour barrels and a pallet of sacks by the south door, a sack on the hoist's hook.
        foreach (var (x, z) in new[] { (1.8f, 5.8f), (2.5f, 6.1f), (1.95f, 6.6f) })
        {
            n.AddChild(Props.Part(Props.Cyl(0.34f, 0.8f, 10, 0.3f), Plank, new Vector3(x, 0.4f, z)));
            foreach (float y in new[] { 0.14f, 0.66f }) n.AddChild(Props.Part(Props.Cyl(0.35f, 0.04f, 10), Iron, new Vector3(x, y, z)));
        }
        n.AddChild(B(new Vector3(1.4f, 0.1f, 1.0f), PlankDark, new Vector3(-2.2f, 0.06f, 6.3f)));
        var hs = Props.Part(Props.Ball(0.36f, 8), Sack, new Vector3(0, EaveY - 1.7f, Hz + 1.75f)); hs.Scale = new Vector3(1f, 1.5f, 1f); n.AddChild(hs);
        // At the wheel: a plank footbridge with a rail along the flume, the brake lever, a grease pot, a stone-pitched bank and foam-white splash boards under the buckets.
        n.AddChild(B(new Vector3(0.5f, 0.07f, 6.4f), Plank, new Vector3(fx - 1.0f, y1 + 0.35f, -3.2f)));
        foreach (float z in new[] { -6.0f, -4.5f, -3.0f, -1.5f, -0.2f })
        {
            n.AddChild(B(new Vector3(0.08f, 1.0f, 0.08f), PlankDark, new Vector3(fx - 1.25f, y1 + 0.85f, z)));
            n.AddChild(B(new Vector3(0.08f, 0.08f, 0.08f), PlankDark, new Vector3(fx - 1.25f, y1 + 0.3f, z)));
        }
        n.AddChild(B(new Vector3(0.06f, 0.07f, 6.0f), PlankDark, new Vector3(fx - 1.25f, y1 + 1.2f, -3.1f)));
        n.AddChild(B(new Vector3(0.06f, 0.07f, 6.0f), PlankDark, new Vector3(fx - 1.25f, y1 + 0.85f, -3.1f)));
        n.AddChild(B(new Vector3(0.1f, 1.6f, 0.1f), PlankDark, new Vector3(-7.95f, 0.9f, 1.7f), new Vector3(0, 0, -22)));
        n.AddChild(Props.Part(Props.Cyl(0.15f, 0.2f, 8), Iron, new Vector3(-7.9f, 0.45f, 1.35f)));
        for (int k = 0; k < 7; k++) n.AddChild(B(new Vector3(rs.RandfRange(0.4f, 0.8f), 0.02f, rs.RandfRange(0.15f, 0.3f)), new Color(0.88f, 0.92f, 0.92f), new Vector3(WheelCentre.X + rs.RandfRange(-1.4f, 1.0f), -1.2f + rs.RandfRange(-0.1f, 0.15f), rs.RandfRange(-0.8f, 0.8f))));
        DrunkTractor.BatchStatic(n, null);
        return n;
    }
}
