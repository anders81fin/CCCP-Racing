using System.Collections.Generic;
using Godot;

namespace CCCPRacing.Track;

public static partial class Village
{
    /// <summary>The same door-to-road route drives both the visible path and fence openings.</summary>
    public static List<Vector2> HouseAccessPath(TrackData t, PropInstance house)
    {
        Vector2 World(float x, float z)
        {
            Vector3 offset = new Basis(Vector3.Up, house.Rotation) * new Vector3(x, 0, z);
            return new Vector2(house.Position.X + offset.X, house.Position.Z + offset.Z);
        }
        var path = new List<Vector2>
        {
            World(3.72f, 0.5f), World(3.8f, 1.8f), World(2.8f, 3.15f),
            World(0, 3.15f), World(0, 5.35f),
        };
        Vector2 front = path[^1];
        int ci = t.NearestCenter(front, out _);
        Vector2 away = (front - t.Center[ci]).Normalized();
        // Meet the actual driving surface so a strip of grass cannot sever the connection.
        Vector2 end = t.Center[ci] + away * (t.HalfWidth[ci] - 0.15f);
        int steps = System.Math.Max(1, (int)System.MathF.Ceiling(front.DistanceTo(end) / 1.2f));
        for (int k = 1; k <= steps; k++) path.Add(front.Lerp(end, k / (float)steps));
        return path;
    }

    public static bool NearAccessPath(Vector2 p, IReadOnlyList<Vector2> path, float clearance)
    {
        for (int k = 0; k + 1 < path.Count; k++)
        {
            Vector2 a = path[k], ab = path[k + 1] - a;
            float u = ab.LengthSquared() > 0.0001f
                ? Mathf.Clamp((p - a).Dot(ab) / ab.LengthSquared(), 0, 1) : 0;
            if (p.DistanceSquaredTo(a + ab * u) < clearance * clearance) return true;
        }
        return false;
    }
}
