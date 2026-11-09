using System;
using Godot;

namespace CCCPRacing.Camera;

/// <summary>
/// The CINEMATIC gameplay view: a close chase camera behind the car, low and wide, so the car's place among the others and the line it takes can be read, a slide's angle above all.
/// The camera is not nailed to the car. Its yaw follows the nose, with a tenth of the way to the travel direction, through an exponential lag (a slide turns the camera round hard like a rubber band, a little late, and a hint of the slide angle stays in the picture); its position hangs on a
/// critically damped spring, so a corner or a launch leaves it a moment behind; the look point trails a little; a lateral acceleration rolls the picture, an acceleration or braking pitches it
/// and the speed, a slide and the ground add a fine shake. Collision kicks and the plain shake are ChaseCamera's (Pose).
/// </summary>
internal sealed class GameplayRearCamera
{
    /// <summary>The yaw's lag (1/s: 2.4 is a time constant of 0.4 s), the spring's natural frequency (rad/s; the delay in a corner is about 1/ω) and the look point's rate.</summary>
    const float YawRate = 2.7f, OmegaH = 7f, OmegaV = 11f, LookRate = 6f;
    bool _ready;
    float _yaw, _roll, _pitch, _aLat, _aLong, _clock;
    Vector3 _rel, _relVel, _look, _lastCar, _lastVel;      // (the eye relative to the car, and its velocity relative to the car's)

    public void Reset() => _ready = false;

    /// <param name="car">car position</param><param name="velocity">car velocity</param><param name="heading">the way the nose points</param>
    /// <param name="ground">the ground height at x, z</param><param name="rumble">the surface's rumble in metres (ChaseCamera: 0 on roads)</param>
    public (Vector3 eye, Vector3 target, float roll) Frame(Vector3 car, Vector3 velocity, Vector3 heading, float dt, Func<float, float, float> ground, float rumble = 0f)
    {
        dt = Math.Clamp(dt, 0f, 0.05f);
        Vector3 motion = new(velocity.X, 0, velocity.Z);
        float speed = motion.Length();
        Vector3 nose = new(heading.X, 0, heading.Z);
        if (nose.LengthSquared() < 0.01f) nose = Vector3.Forward;
        nose = nose.Normalized();
        float headYaw = MathF.Atan2(nose.X, nose.Z);
        bool snap = !_ready || car.DistanceTo(_lastCar) > 35f;

        // The yaw: the nose, with a tenth of the way to the travel direction at speed. In a slide the camera swings round with the car like a rubber band (a little late: YawRate), yet a
        // hint of the slide angle stays in the picture. Reversing, or crawling, it is the nose.
        float yawTarget = headYaw;
        bool reversing = speed > 1.5f && motion.Normalized().Dot(nose) < -0.2f;
        if (speed > 3f && !reversing)
            yawTarget = Mathf.LerpAngle(headYaw, MathF.Atan2(motion.X, motion.Z), 0.12f * Math.Clamp((speed - 3f) / 9f, 0f, 1f));
        if (snap) _yaw = yawTarget;
        else
        {
            float step = Mathf.AngleDifference(_yaw, yawTarget) * (1f - MathF.Exp(-YawRate * dt));
            _yaw += Math.Clamp(step, -4.5f * dt, 4.5f * dt);      // (never a sudden swing)
        }
        Vector3 dir = new(MathF.Sin(_yaw), 0, MathF.Cos(_yaw));
        Vector3 right = new(-dir.Z, 0, dir.X);

        // The accelerations (smoothed): along the nose and sideways, for the roll and the pitch.
        if (!snap && dt > 0)
        {
            Vector3 a = (new Vector3(velocity.X, 0, velocity.Z) - new Vector3(_lastVel.X, 0, _lastVel.Z)) / dt;
            Vector3 noseRight = new(-nose.Z, 0, nose.X);
            float k = 1f - MathF.Exp(-6f * dt);
            _aLong = Mathf.Lerp(_aLong, Math.Clamp(a.Dot(nose), -25f, 25f), k);
            _aLat = Mathf.Lerp(_aLat, Math.Clamp(-a.Dot(noseRight), -25f, 25f), k);      // + : the car is pulled to its left
        }
        else _aLong = _aLat = 0;
        _lastVel = velocity;

        float fast = Math.Clamp(speed / 40f, 0f, 1f);
        // The camera's life follows the speed: standing it is nearly still, at full speed everything is at its largest (a smooth step from 0 to 45 m/s).
        float sp = Math.Clamp(speed / 45f, 0f, 1f);
        sp = sp * sp * (3f - 2f * sp);
        float lively = Mathf.Lerp(0.12f, 1f, sp);      // (the roll and the pitch keep a trace at a standstill, so a launch still shows)
        float distance = Mathf.Lerp(6.5f, 9f, fast), height = Mathf.Lerp(2.3f, 3.1f, fast);
        Vector3 ideal = -dir * distance + Vector3.Up * height;      // relative to the car
        if (snap) { _rel = ideal; _relVel = Vector3.Zero; _look = car + Vector3.Up; _roll = _pitch = 0; _clock = 0; }
        else
        {
            // A critically damped spring on the eye, in the car's frame: a steady speed costs it nothing, but the car's acceleration and the turning of the ideal point leave the eye behind.
            // (The car's acceleration pushes the eye the other way, as it would a camera on an arm.) Stepped finely so a slow frame cannot throw it.
            Vector3 aCar = (new Vector3(velocity.X, 0, velocity.Z) - new Vector3(_lastVel.X, 0, _lastVel.Z)) / Math.Max(dt, 1e-4f);
            aCar = aCar.LimitLength(30f);
            int steps = Math.Max(1, (int)MathF.Ceiling(dt / (1f / 120f)));
            float h = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                Vector3 off = _rel - ideal;
                Vector3 acc = new Vector3(-OmegaH * OmegaH * off.X - 2f * OmegaH * _relVel.X - aCar.X, -OmegaV * OmegaV * off.Y - 2f * OmegaV * _relVel.Y, -OmegaH * OmegaH * off.Z - 2f * OmegaH * _relVel.Z - aCar.Z);
                _relVel += acc * h;
                _rel += _relVel * h;
            }
        }
        _clock += dt;

        // The look point trails the car a little, ahead of it along the travel.
        Vector3 lead = speed > 1f ? motion.Normalized() * Math.Clamp(speed * 0.1f, 0f, 3.5f) : Vector3.Zero;
        Vector3 look = car + Vector3.Up * 1.0f + lead;
        _look = snap ? look : _look.Lerp(look, 1f - MathF.Exp(-LookRate * dt));

        // Pitch (braking dips the view, a launch lifts it) and roll (it leans into a corner), both soft.
        float pitchTarget = Math.Clamp(_aLong * 0.0035f, -0.04f, 0.04f) * lively;
        float rollTarget = Math.Clamp(_aLat * 0.0055f, -0.09f, 0.09f) * lively;
        float kp = snap ? 1f : 1f - MathF.Exp(-5f * dt);
        _pitch = Mathf.Lerp(_pitch, pitchTarget, kp);
        _roll = Mathf.Lerp(_roll, rollTarget, kp);
        // The pan and the tilt: a slow sway of where the lens points that grows with the speed (nothing standing), on top of the pitch and the roll.
        Vector3 sway = (right * MathF.Sin(_clock * 1.7f + 0.4f) * 0.9f + Vector3.Up * MathF.Sin(_clock * 2.3f + 1.1f) * 0.6f) * (0.012f * sp * distance);
        Vector3 target = _look + Vector3.Up * (MathF.Tan(_pitch) * distance) + sway;

        // The shake: the speed, a slide and the ground. Two quick smooth wobbles (not per-frame noise) across the picture.
        float slide = speed > 5f ? Math.Clamp(MathF.Acos(Math.Clamp(motion.Normalized().Dot(nose), -1f, 1f)) / 0.5f, 0f, 1f) : 0f;
        float amp = 0.004f + 0.034f * sp + 0.03f * slide * sp + 0.6f * rumble;      // (the ground's rumble already grows with the speed: CameraFeel)
        Vector3 up = Vector3.Up;
        Vector3 wobble = right * (MathF.Sin(_clock * 57f) + 0.6f * MathF.Sin(_clock * 91f + 1.2f)) * amp + up * (MathF.Sin(_clock * 63f + 0.7f) + 0.6f * MathF.Sin(_clock * 107f + 2.1f)) * amp;

        Vector3 eye = car + _rel + wobble;
        // Correct AFTER the spring: the eye and its sightline must clear changing terrain.
        Vector3 anchor = car + Vector3.Up * 1f;
        if (ground != null)
        {
            eye.Y = Math.Max(eye.Y, ground(eye.X, eye.Z) + 0.9f);
            for (int i = 2; i <= 16; i++)
            {
                float u = i / 16f;
                Vector3 point = anchor.Lerp(eye, u);
                eye.Y += Math.Max(0f, (ground(point.X, point.Z) + 1.0f - point.Y) / u);
            }
        }
        _lastCar = car;
        _ready = true;
        return (eye, target, _roll);
    }
}
