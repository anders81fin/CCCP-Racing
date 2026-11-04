using System;
using Godot;

namespace CCCPRacing.Track;

public partial class KozlikPatrol
{
    const float Wheelbase = 2.4f * ModelScale;
    const float MaxSteer = 0.61f; // 35 degrees: finite turning circle, including at walking speed.
    float _steer, _driveDt, _previousSpeed;
    bool _reverse;
    Vector2 _guidePosition, _guideForward, _velocity;
    float SteeringLookAhead => Math.Clamp(3.5f + _speed * 0.4f, 4f, 14f);

    /// <summary>Only the wheels move the car. Lane/path coordinates are steering targets, never poses.</summary>
    void AdvanceVehicle(float dt)
    {
        if (dt <= 0) return;
        if (_mode == Mode.Guard) { _velocity = Vector2.Zero; return; }
        Vector2 to = _guidePosition - Position2D;
        float distance = to.Length();
        // A tight parking approach can require backing up. Stop before selecting the other
        // direction; neither changing gear nor steering may rotate a stationary vehicle.
        bool parking = _mode is Mode.ToGuard or Mode.Back || _mode == Mode.Chase && _chaseOut;
        float ahead = distance > 0.1f ? Forward2D.Dot(to / distance) : 0;
        bool wantReverse = parking && (ahead < -0.3f || _reverse && ahead < 0.3f);
        bool changeGear = wantReverse != _reverse;
        if (changeGear && _previousSpeed < 0.15f) { _reverse = wantReverse; changeGear = false; }
        float curvature = distance > 0.1f ? 2f * Forward2D.Cross(to) / (distance * distance) : 0;
        float wantSteer = Math.Clamp(MathF.Atan(Wheelbase * curvature), -MaxSteer, MaxSteer);
        _steer = Mathf.MoveToward(_steer, wantSteer, 1.4f * dt);
        curvature = MathF.Tan(_steer) / Wheelbase;
        float cornerSpeed = MathF.Sqrt(5.5f / Math.Max(0.0001f, Math.Abs(curvature)));
        _speed = Mathf.MoveToward(_previousSpeed, changeGear ? 0 : Math.Min(_speed, _reverse ? Math.Min(2.5f, cornerSpeed) : cornerSpeed), ChaseBrake * dt);
        float signedSpeed = _reverse ? -_speed : _speed;
        float turn = curvature * signedSpeed * dt;
        Vector2 before = Position2D;
        Position2D += Forward2D.Rotated(turn * 0.5f) * (signedSpeed * dt);
        Forward2D = Forward2D.Rotated(turn).Normalized();
        _velocity = (Position2D - before) / dt;
        if (_mode is Mode.Road or Mode.Autograph || _mode == Mode.Chase && !_chaseOut)
            _s = RoadProgress(Position2D);
    }

    float RoadProgress(Vector2 position)
    {
        int n = _t.Center.Length, start = (int)_s;
        float best = float.MaxValue, result = _s;
        for (int a = -20; a <= 20; a++)
        {
            int i = ((start + a) % n + n) % n;
            Vector2 p = _t.Center[i], d = _t.Center[(i + 1) % n] - p;
            float f = d.LengthSquared() > 0.0001f ? Math.Clamp((position - p).Dot(d) / d.LengthSquared(), 0, 1) : 0;
            float error = position.DistanceSquaredTo(p + d * f);
            if (error < best) { best = error; result = (i + f) % n; }
        }
        return result;
    }

    float PathProgress(Vector2 position)
    {
        float best = float.MaxValue, result = _ps;
        for (int i = 1; i < _path.Length; i++)
        {
            if (_pathLen[i] < _ps - 5 || _pathLen[i - 1] > _ps + 20) continue;
            Vector2 a = _path[i - 1], d = _path[i] - a;
            float f = d.LengthSquared() > 0.0001f ? Math.Clamp((position - a).Dot(d) / d.LengthSquared(), 0, 1) : 0;
            float error = position.DistanceSquaredTo(a + d * f);
            if (error < best) { best = error; result = Mathf.Lerp(_pathLen[i - 1], _pathLen[i], f); }
        }
        return result;
    }
}
