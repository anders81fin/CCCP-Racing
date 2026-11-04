using System;
using Godot;
using CCCPRacing.Track;

namespace CCCPRacing.Camera;

/// <summary>A three-quarter chase aligned with the road on track, and actual motion off road.</summary>
internal sealed class GameplayCinematicCamera
{
    bool _ready;
    float _yaw;
    Vector3 _offset, _lead, _lastCar;

    public void Reset() => _ready = false;

    // Project onto the road segments, rather than testing distance to discrete samples at the verge.
    internal static Vector3? RoadForward(TrackData track, Vector3 car)
    {
        if (track.Center == null || track.Center.Length < 4 || track.HalfWidth == null) return null;
        Vector2 position = new(car.X, car.Z);
        int nearest = track.NearestCenter(position, out _), count = track.Center.Length;
        float bestDistance = float.MaxValue, width = 0;
        Vector2 direction = Vector2.Zero;
        foreach (int start in new[] { (nearest - 1 + count) % count, nearest })
        {
            int end = (start + 1) % count;
            Vector2 a = track.Center[start], segment = track.Center[end] - a;
            if (segment.LengthSquared() < 0.0001f) continue;
            float u = Mathf.Clamp((position - a).Dot(segment) / segment.LengthSquared(), 0, 1);
            float distance = position.DistanceSquaredTo(a + segment * u);
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            width = Mathf.Lerp(track.HalfWidth[start], track.HalfWidth[end], u);
            direction = track.CenterDir(start).Lerp(track.CenterDir(end), u).Normalized();
        }
        return bestDistance <= width * width && direction.LengthSquared() > 0.01f
            ? new Vector3(direction.X, 0, direction.Y) : null;
    }

    public (Vector3 eye, Vector3 target) Frame(Vector3 car, Vector3 velocity, Vector3 heading,
        float dt, Func<float, float, float> ground, Vector3? roadForward = null)
    {
        Vector3 motion = new(velocity.X, 0, velocity.Z);
        float speed = motion.Length();
        Vector3 forward = new(heading.X, 0, heading.Z);
        if (forward.LengthSquared() < 0.01f) forward = Vector3.Forward;
        bool onRoad = roadForward.HasValue;
        if (onRoad) forward = roadForward.Value;
        float yaw = MathF.Atan2(forward.X, forward.Z);
        // Off road, at walking speed retain the car's orientation; at speed face the travel direction,
        // including reverse and sideways slides. Turning is limited to avoid sudden camera swings.
        if (!onRoad && speed > 2f)
            yaw = Mathf.LerpAngle(yaw, MathF.Atan2(motion.X, motion.Z), Mathf.Clamp((speed - 2f) / 8f, 0, 1));
        bool snap = !_ready || car.DistanceTo(_lastCar) > 35f;
        float blend = snap ? 1f : 1f - MathF.Exp(-3f * dt);
        _yaw = snap ? yaw : Mathf.RotateToward(_yaw, yaw, 1.5f * dt);
        Vector3 direction = new(MathF.Sin(_yaw), 0, MathF.Cos(_yaw));
        Vector3 right = new(-direction.Z, 0, direction.X);
        float fast = Mathf.Clamp(speed / 40f, 0, 1);
        Vector3 offset = -direction * Mathf.Lerp(22f, 28f, fast) + right * 4f + Vector3.Up * Mathf.Lerp(11f, 14f, fast);
        // Smooth the dolly relative to the car, so acceleration cannot leave the player behind.
        _offset = _offset.Lerp(offset, blend);
        // Steering and lateral slides must not swing the look target away from the road either.
        Vector3 lookAhead = onRoad ? forward * Math.Min(speed, 20f) : motion.LimitLength(20f);
        _lead = _lead.Lerp(lookAhead * 0.35f, blend);
        Vector3 eye = car + _offset;
        // Correct AFTER smoothing: both the eye and its sightline must clear changing terrain.
        Vector3 anchor = car + Vector3.Up * 1f;
        for (int i = 2; i <= 16; i++)
        {
            float u = i / 16f;
            Vector3 point = anchor.Lerp(eye, u);
            eye.Y += Math.Max(0f, (ground(point.X, point.Z) + 1.5f - point.Y) / u);
        }
        _lastCar = car;
        _ready = true;
        return (eye, car + Vector3.Up * 0.8f + _lead);
    }
}
