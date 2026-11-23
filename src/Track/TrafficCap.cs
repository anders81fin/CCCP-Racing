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
    public static float Cap(TrackData t, Vector2 pos, Vector2 fwd, float speed, float halfWidth, Func<int, bool> own, float brake = 3f, object owner = null, bool cars = true)
    {
        Vector2 right = new(-fwd.Y, fwd.X);
        float reach = 14f + speed * 2.2f, cap = float.MaxValue;
        void Consider(Vector2 centre, float radius, Vector2 velocity)
        {
            Vector2 to = centre - pos;
            float along = to.Dot(fwd);
            if (along < -radius || along > reach + radius) return;
            // Where it will be as we get there (a second or two on), across our line.
            float lateral = MathF.Abs((to + velocity * Math.Clamp(along / Math.Max(speed, 4f), 0f, 2.5f)).Dot(right));
            if (lateral > radius + halfWidth + 0.8f) return;
            float room = along - radius - halfWidth - 3f;
            float theirs = Math.Max(0f, velocity.Dot(fwd));
            cap = Math.Min(cap, room <= 0 ? 0f : theirs + MathF.Sqrt(2f * brake * room));
        }
        for (int i = 0; i < t.Obstacles.Count; i++)
        {
            var o = t.Obstacles[i];
            if (o.Velocity.LengthSquared() < MinSpeed * MinSpeed || o.Radius < 0.6f || o.Center.X < -9000f || own(i)) continue;
            Consider(o.Center, o.Radius, o.Velocity);
        }
        // The gulag truck, its officer and the driver, standing or moving, are in the way too.
        foreach (var s in GulagTruck.SolidsFor(owner, cars)) Consider(s.Centre, s.Radius, s.Velocity);
        return cap;
    }
}
