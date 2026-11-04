using System;
using Godot;

namespace CCCPRacing.Track;

/// <summary>
/// Vehicles on the road that watch the others moving ahead: the Kozlik and the snow ploughs ask here how fast they may go.
/// The moving obstacles (the bus, tractors, ploughs, a tank, a train, people on foot) are the ones with a velocity; parked things are the
/// road's business. A vehicle in the lane ahead (its radius and ours, plus a margin), where it will be as we get there, caps the speed
/// to what still stops short of it; the vehicle's own speed along our way counts.
/// </summary>
public static class TrafficCap
{
    /// <summary>Slower than this is parked, not traffic (m/s).</summary>
    public const float MinSpeed = 0.3f;

    /// <param name="halfWidth">Half the width of the one asking (m).</param>
    /// <param name="brake">How hard the asker really brakes (m/s squared): the stopping distance is worked out with it.</param>
    /// <param name="own">The asker's own obstacles (and any others that are none of its business, such as its convoy).</param>
    public static float Cap(TrackData t, Vector2 pos, Vector2 fwd, float speed, float halfWidth, Func<int, bool> own, float brake = 3f)
    {
        Vector2 right = new(-fwd.Y, fwd.X);
        float reach = 14f + speed * 2.2f, cap = float.MaxValue;
        for (int i = 0; i < t.Obstacles.Count; i++)
        {
            var o = t.Obstacles[i];
            if (o.Velocity.LengthSquared() < MinSpeed * MinSpeed || o.Radius < 0.6f || o.Center.X < -9000f || own(i)) continue;
            Vector2 to = o.Center - pos;
            float along = to.Dot(fwd);
            if (along < -o.Radius || along > reach + o.Radius) continue;
            // Where it will be as we get there (a second or two on), across our line.
            float lateral = MathF.Abs((to + o.Velocity * Math.Clamp(along / Math.Max(speed, 4f), 0f, 2.5f)).Dot(right));
            if (lateral > o.Radius + halfWidth + 0.8f) continue;
            float room = along - o.Radius - halfWidth - 3f;
            float theirs = Math.Max(0f, o.Velocity.Dot(fwd));
            cap = Math.Min(cap, room <= 0 ? 0f : theirs + MathF.Sqrt(2f * brake * room));
        }
        return cap;
    }
}
