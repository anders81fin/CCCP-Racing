using System;
using Godot;
using CCCPRacing.Replay;

namespace CCCPRacing.Camera;

/// <summary>Pure absolute evaluator shared by paused seeks, playback and export. Smoothing is a
/// bounded, causal convolution of recorded poses, never accumulated display-frame state.</summary>
public static class ReplayCameraMotion
{
    public readonly record struct Frame(Transform3D Pose, float Fov);
    static Vector3 Position(Func<float, Transform3D> sample, float t) => sample(Math.Max(0, t)).Origin;
    static Vector3 Smooth(Func<float, Transform3D> sample, float t, float window)
    {
        Vector3 sum = Vector3.Zero; float weight = 0;
        for (int i = 0; i <= 24; i++)
        {
            float w = 25 - i;
            sum += Position(sample, t - window * i / 24) * w; weight += w;
        }
        return sum / weight;
    }
    static Vector3 Direction(Func<float, Transform3D> sample, float t, float window, Vector3 fallback)
    {
        // Retain the last travelled direction through a stop. Bounded search (up to 20 s)
        // avoids a sudden reset to car-body yaw when velocity approaches zero.
        for (int i = 0; i <= 600; i++)
        {
            float at = i == 0 ? t : MathF.Floor(t * 30) / 30 - i / 30f;
            var d = Position(sample, at) - Position(sample, at - window); d.Y = 0;
            if (d.LengthSquared() > .04f) return d.Normalized();
            if (at <= 0) break;
        }
        return fallback;
    }
    public static Frame Evaluate(LapFreeCamera shot, float start, float clock, Func<float, Transform3D> sample)
    {
        var m = shot.Motion;
        var car = Position(sample, clock);
        var rig = new ReplayFreeCamera(Transform3D.Identity, car);
        if (m == null)
        { rig.Restore(shot, car); return new(new Transform3D(rig.Basis, rig.Position), shot.Fov); }
        float u = Mathf.Clamp((clock - start) / m.Duration, 0, 1); u = u * u * (3 - 2 * u);
        Vector3 eye = new(shot.X, shot.Y, shot.Z), anchor = Position(sample, start);
        if (m.Tracking == LapCameraMotion.World) eye += car - anchor;
        if (m.Tracking is LapCameraMotion.Side or LapCameraMotion.Helicopter)
        {
            float window = m.Tracking == LapCameraMotion.Helicopter ? 1.6f : .4f;
            var fallback = -sample(Math.Max(0, start)).Basis.Z; fallback.Y = 0;
            fallback = fallback.LengthSquared() > .001f ? fallback.Normalized() : Vector3.Forward;
            var a = Direction(sample, start, window, fallback);
            var b = Direction(sample, clock, window, a);
            float turn = Mathf.Atan2(a.Cross(b).Y, a.Dot(b));
            eye = anchor + (eye - anchor).Rotated(Vector3.Up, turn) + Smooth(sample, clock, window) - Smooth(sample, start, window);
        }
        // Positive dolly approaches the subject; stop before crossing it. Negative retreats.
        var toward = car - eye;
        float approach = Mathf.Min(m.Dolly * u, Mathf.Max(0, toward.Length() - 1));
        if (toward.LengthSquared() > .0001f) eye += toward.Normalized() * approach;
        eye += Vector3.Up * (m.Rise * u);
        rig.Restore(shot with { X = eye.X, Y = eye.Y, Z = eye.Z, Follow = false, Motion = null }, car);
        return new(new Transform3D(rig.Basis, rig.Position), Mathf.Lerp(shot.Fov, m.EndFov ?? shot.Fov, u));
    }
}
