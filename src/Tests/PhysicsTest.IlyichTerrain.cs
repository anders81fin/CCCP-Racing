using System;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    // ILYICHLAB=terrain: church foundation, surrounding hairpin and flowing road grades.
    void IlyichTerrainTest(TrackData track)
    {
        float yard = track.MeshHeight(57, 192);
        float low = float.PositiveInfinity, high = float.NegativeInfinity;
        for (float z = 189; z <= 195; z += 1)
        for (float x = 49; x <= 65; x += 1)
        {
            float h = track.MeshHeight(x, z);
            low = Math.Min(low, h); high = Math.Max(high, h);
        }
        Check(high - low < 0.12f, $"church foundation level: range {high - low:F3} m");
        foreach (var p in new[] { new Vector2(58, 206), new Vector2(78, 192), new Vector2(58, 178) })
        {
            float rise = yard - track.MeshHeight(p.X, p.Y);
            Check(rise > 1.4f && rise < 3.5f, $"churchyard above hairpin at {p}: {rise:F2} m");
        }
        foreach (var range in new[] { (232, 314), (888, 984), (735, 865) })
        {
            float grade = 0;
            for (int i = range.Item1; i < range.Item2; i++)
                grade = Math.Max(grade, Math.Abs(track.CenterH[i + 1] - track.CenterH[i]));
            // The supported rally leap deliberately replaces the old sub-18% rollers.
            Check(grade < (range.Item1==232 && track.RoadLandforms.Count>0 ? .36f : .18f), $"road grade {range.Item1}-{range.Item2}: max {grade * 100:F1}%");
        }
    }
}
