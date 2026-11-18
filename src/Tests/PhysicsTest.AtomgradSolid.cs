using System;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    // --atomgrad-solid-test: the town's blocks, the palace of culture and the bumper-car hall have no gap (over 0.7 m: a circle rounds a corner by 0.6 m) in their footprint: every point of its outer 2 m (0.4 m grid, to the very corners) is inside some obstacle circle.
    void AtomgradSolidTest()
    {
        var track = TrackBuilder.Build(TrackDef.Load("res://tracks/atomgrad.json"));
        int checkedBuildings = 0; float worst = 0f;
        foreach (var p in track.Props)
        {
            (float sx, float sz) = p.Type switch { "apartments" => (12f, 50f), "culture" => (16f, 26f), "bumpercars" => (10f, 14f), _ => (0f, 0f) };
            if (sx == 0f) continue;
            checkedBuildings++;
            Vector2 c = new(p.Position.X, p.Position.Z); float rot = p.Rotation + MathF.PI / 2f;
            Vector2 along = new(MathF.Sin(rot), MathF.Cos(rot)), across = new(along.Y, -along.X);
            int open = 0; float deepestGap = 0f;
            for (float u = -sz / 2f + 0.05f; u <= sz / 2f; u += 0.4f)
                for (float v = -sx / 2f + 0.05f; v <= sx / 2f; v += 0.4f)
                {
                    if (MathF.Min(sz / 2f - MathF.Abs(u), sx / 2f - MathF.Abs(v)) > 2.0f) continue;      // (the walls and corners; deep inside, a car cannot get)
                    Vector2 q = c + along * u + across * v; float best = 1e9f;
                    foreach (var o in track.Obstacles) best = MathF.Min(best, q.DistanceTo(o.Center) - o.Radius);
                    if (best > 0.7f) { open++; deepestGap = MathF.Max(deepestGap, best); }
                }
            worst = MathF.Max(worst, deepestGap);
            Check(open == 0, $"{p.Type} at ({c.X:F0}, {c.Y:F0}): {open} open points in the footprint (largest gap {deepestGap:F2} m)");
        }
        Check(checkedBuildings >= 7, $"the town's {checkedBuildings} buildings were checked");
    }
}
