using System;
using Godot;

namespace CCCPRacing.Camera;

/// <summary>
/// The life of the BONNET view, which stays bolted to the car (the lens sits on the bonnet's edge and never lags in position): a small shake, a slow pan and tilt sway, a roll into a corner and a
/// dip for braking, all growing with the speed (almost nothing standing, the most at a high speed). The roll and the pitch follow the car's accelerations through a soft delay, as a head would.
/// All of it is rotation and a hair of movement: far less than the chase camera has, since the lens is so close. Off with the camera shake setting (the caller).
/// </summary>
internal sealed class GameplayBonnetFeel
{
    bool _ready;
    float _clock, _roll, _pitch, _aLat, _aLong;
    Vector3 _lastVel, _lastEye;

    public void Reset() => _ready = false;

    /// <param name="eye">the bonnet view's lens</param><param name="target">where it looks (GameplayBonnetCamera.Frame)</param>
    /// <param name="body">the car's orientation</param><param name="velocity">the car's velocity</param><param name="rumble">the surface's rumble in metres (ChaseCamera.Rumble)</param>
    public (Vector3 eye, Vector3 target, float roll) Apply(Vector3 eye, Vector3 target, Basis body, Vector3 velocity, float dt, float rumble = 0f)
    {
        dt = Math.Clamp(dt, 0f, 0.05f);
        bool snap = !_ready || eye.DistanceTo(_lastEye) > 35f;
        float speed = new Vector2(velocity.X, velocity.Z).Length();
        float sp = Math.Clamp(speed / 45f, 0f, 1f);
        sp = sp * sp * (3f - 2f * sp);
        float lively = Mathf.Lerp(0.12f, 1f, sp);

        if (!snap && dt > 0)
        {
            Vector3 a = (velocity - _lastVel) / dt;
            a.Y = 0;
            float k = 1f - MathF.Exp(-6f * dt);
            _aLong = Mathf.Lerp(_aLong, Math.Clamp(a.Dot(-body.Z), -25f, 25f), k);
            _aLat = Mathf.Lerp(_aLat, Math.Clamp(-a.Dot(body.X), -25f, 25f), k);      // + : pulled to the car's left
        }
        else { _aLong = _aLat = 0; _roll = _pitch = 0; _clock = 0; }
        _lastVel = velocity; _lastEye = eye; _ready = true;
        _clock += dt;

        float kp = snap ? 1f : 1f - MathF.Exp(-5f * dt);
        _pitch = Mathf.Lerp(_pitch, Math.Clamp(_aLong * 0.0025f, -0.03f, 0.03f) * lively, kp);
        _roll = Mathf.Lerp(_roll, Math.Clamp(_aLat * 0.004f, -0.06f, 0.06f) * lively, kp);

        // The pan and the tilt (a slow sway) and the shake (two quick smooth wobbles, not per-frame noise), all with the speed; the ground's rumble adds to the shake.
        float sway = 0.004f * sp, tremor = 0.0006f + 0.0035f * sp + 0.15f * rumble;
        float pan = sway * MathF.Sin(_clock * 1.7f + 0.4f) + tremor * (MathF.Sin(_clock * 57f) + 0.6f * MathF.Sin(_clock * 91f + 1.2f));
        float tilt = _pitch + 0.75f * sway * MathF.Sin(_clock * 2.3f + 1.1f) + tremor * (MathF.Sin(_clock * 63f + 0.7f) + 0.6f * MathF.Sin(_clock * 107f + 2.1f));
        Vector3 dir = (target - eye).Normalized();
        dir = new Basis(body.Y.Normalized(), pan) * new Basis(body.X.Normalized(), tilt) * dir;
        // A hair of movement for the shake (the lens stays on the bonnet).
        Vector3 jitter = (body.X * MathF.Sin(_clock * 71f) + body.Y * MathF.Sin(_clock * 83f + 0.7f)) * (0.0015f * lively + 0.4f * rumble * 0.01f);
        return (eye + jitter, eye + jitter + dir * (target - eye).Length(), _roll);
    }
}
