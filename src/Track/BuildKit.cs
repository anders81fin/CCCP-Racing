using System;
using System.Collections.Generic;
using Godot;

namespace CCCPRacing.Track;

/// <summary>
/// Shared low-level masonry/carpentry helpers for the south-side buildings (the mill, the MTS yard, the water tower): coursed rubble stone and brick walls with openings, vertical plank siding,
/// shingled or corrugated roof planes, wheels. Only primitives merged by DrunkTractor.BatchStatic; no finished building shapes are shared here.
/// </summary>
internal static class BuildKit
{
    internal static Color Tone(Color c, RandomNumberGenerator rng, float amount)
    {
        float k = 1f + rng.RandfRange(-amount, amount);
        return new Color(Math.Clamp(c.R * k, 0, 1), Math.Clamp(c.G * k * rng.RandfRange(0.97f, 1.03f), 0, 1), Math.Clamp(c.B * k, 0, 1));
    }

    /// <summary>An opening in a wall: u0..u1 along the wall, y0..y1 up it.</summary>
    internal readonly record struct Hole(float U0, float U1, float Y0, float Y1);

    /// <summary>
    /// A coursed wall of separate stones or bricks. The wall runs along X (alongX) or Z from <paramref name="start"/> (its u = 0 end, the base centre line) for <paramref name="length"/>; courses of
    /// <paramref name="course"/> m, stones of <paramref name="minLen"/>..<paramref name="maxLen"/>, thickness <paramref name="thick"/> with a little relief, joints staggered per course; holes are left open.
    /// </summary>
    internal static void Masonry(Node3D n, Vector3 start, bool alongX, float length, float height, float thick, float course, float minLen, float maxLen, Color baseColor, int seed, float toneJitter, params Hole[] holes)
    {
        var rng = new RandomNumberGenerator { Seed = (ulong)Math.Abs(seed) + 101 };
        int rows = Math.Max(1, (int)MathF.Round(height / course));
        float ch = height / rows;
        for (int r = 0; r < rows; r++)
        {
            float y0 = r * ch, y1 = y0 + ch;
            float u = (r % 2 == 0) ? 0f : -rng.RandfRange(0f, minLen * 0.8f);
            while (u < length)
            {
                float len = rng.RandfRange(minLen, maxLen), a = Math.Max(0f, u), b = Math.Min(length, u + len);
                u += len;
                if (b - a < 0.08f) continue;
                float mid = (a + b) * 0.5f, ym = (y0 + y1) * 0.5f;
                bool cut = false;
                foreach (var h in holes) if (mid > h.U0 && mid < h.U1 && ym > h.Y0 && ym < h.Y1) { cut = true; break; }
                if (cut) continue;
                float relief = rng.RandfRange(-0.03f, 0.05f), t = thick + relief;
                var size = alongX ? new Vector3(b - a - 0.02f, ch - 0.02f, t) : new Vector3(t, ch - 0.02f, b - a - 0.02f);
                var pos = start + (alongX ? new Vector3(mid, ym, relief * 0.5f) : new Vector3(relief * 0.5f, ym, mid));
                n.AddChild(Props.Part(new BoxMesh { Size = size }, Tone(baseColor, rng, toneJitter), pos));
            }
        }
    }

    /// <summary>A plank-sided wall: vertical boards of mixed widths and weathering with a few battens.</summary>
    internal static void Planks(Node3D n, Vector3 start, bool alongX, float length, float height, float thick, Color baseColor, int seed, params Hole[] holes)
    {
        var rng = new RandomNumberGenerator { Seed = (ulong)Math.Abs(seed) + 211 };
        float u = 0;
        while (u < length)
        {
            float w = rng.RandfRange(0.17f, 0.27f), a = u, b = Math.Min(length, u + w); u += w;
            float mid = (a + b) * 0.5f;
            bool open = false;
            float y0 = 0f, y1 = height;
            foreach (var h in holes) if (mid > h.U0 && mid < h.U1) { if (h.Y0 <= 0.01f && h.Y1 >= height - 0.01f) { open = true; break; } }
            if (open) continue;
            // Boards are split by any window / hatch into a lower and an upper piece.
            var cuts = new List<(float a, float b)> { (0f, height) };
            foreach (var h in holes) if (mid > h.U0 && mid < h.U1 && !(h.Y0 <= 0.01f && h.Y1 >= height - 0.01f))
            {
                var next = new List<(float, float)>();
                foreach (var (ca, cb) in cuts)
                {
                    if (h.Y1 <= ca || h.Y0 >= cb) { next.Add((ca, cb)); continue; }
                    if (h.Y0 > ca) next.Add((ca, h.Y0));
                    if (h.Y1 < cb) next.Add((h.Y1, cb));
                }
                cuts = next;
            }
            foreach (var (ca, cb) in cuts)
            {
                float hh = cb - ca; if (hh < 0.05f) continue;
                float relief = rng.RandfRange(-0.01f, 0.035f);
                var size = alongX ? new Vector3(b - a - 0.015f, hh, thick + relief) : new Vector3(thick + relief, hh, b - a - 0.015f);
                var pos = start + (alongX ? new Vector3(mid, ca + hh * 0.5f, relief * 0.5f) : new Vector3(relief * 0.5f, ca + hh * 0.5f, mid));
                n.AddChild(Props.Part(new BoxMesh { Size = size }, Tone(baseColor, rng, 0.14f), pos));
            }
        }
        foreach (float y in new[] { height * 0.2f, height * 0.55f, height * 0.9f })
        {
            var size = alongX ? new Vector3(length, 0.1f, thick + 0.06f) : new Vector3(thick + 0.06f, 0.1f, length);
            var pos = start + (alongX ? new Vector3(length * 0.5f, y, 0.01f) : new Vector3(0.01f, y, length * 0.5f));
            n.AddChild(Props.Part(new BoxMesh { Size = size }, Tone(baseColor, rng, 0.05f).Darkened(0.2f), pos));
        }
    }

    /// <summary>One side of a gable roof of shingle (or board) courses laid from the ridge down: ridge along Z at (x = 0, y = ridgeY), slope angle, side = +1 / -1, eave at x = side * halfSpan.</summary>
    internal static void ShingleSide(Node3D n, float side, float ridgeY, float halfSpan, float z0, float z1, float theta, float rowLen, Color baseColor, int seed, float jitter = 0.16f)
    {
        var rng = new RandomNumberGenerator { Seed = (ulong)Math.Abs(seed) + 307 };
        float slope = halfSpan / MathF.Cos(theta);
        int rows = Math.Max(1, (int)MathF.Ceiling(slope / rowLen));
        float len = z1 - z0;
        for (int i = 0; i < rows; i++)
        {
            float t = (i + 0.5f) * (slope / rows), rl = slope / rows + 0.04f;
            int segs = Math.Max(2, (int)MathF.Round(len / 2.4f));
            for (int s = 0; s < segs; s++)
            {
                float a = z0 + len * s / segs, b = z0 + len * (s + 1) / segs;
                var pos = new Vector3(side * t * MathF.Cos(theta), ridgeY - t * MathF.Sin(theta) + i * 0.012f, (a + b) * 0.5f);
                n.AddChild(Props.Part(new BoxMesh { Size = new Vector3(rl, 0.06f, b - a - 0.02f) }, Tone(baseColor, rng, jitter), pos, new Vector3(0, 0, Mathf.RadToDeg(side > 0 ? -theta : theta))));
            }
        }
    }

    /// <summary>A ring of straight rim segments (a wheel's felloe) in the XY plane around (cx, cy), at depth z.</summary>
    internal static void RimRing(Node3D n, Vector3 centre, float radius, int segments, float width, float thick, Color c)
    {
        float chord = 2f * radius * MathF.Sin(MathF.PI / segments);
        for (int k = 0; k < segments; k++)
        {
            float a = k * MathF.Tau / segments;
            var pos = centre + new Vector3(MathF.Cos(a) * radius, MathF.Sin(a) * radius, 0);
            n.AddChild(Props.Part(new BoxMesh { Size = new Vector3(thick, chord + 0.03f, width) }, c, pos, new Vector3(0, 0, Mathf.RadToDeg(a))));
        }
    }
}
