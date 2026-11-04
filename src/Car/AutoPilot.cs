using System;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Car;

/// <summary>Simple centre-line follower. Drives the attract-mode demo and the headless track test.</summary>
public class AutoPilot
{
    readonly TrackData _t;
    int _idx = -1;
    public float Aggression = 1f;
    /// <summary>Test: how many times the lateral grip the corner speeds may use (the norm calibration drives nearer the human pace).</summary>
    public float CornerGrip = 1f;
    float _stuckTime, _reverseTime, _traction = 1f, _side, _sideHold;
    float _crossAt = float.MaxValue, _oncomingAt = float.MaxValue, _moverSpeed = -1f;      // road metres to something crossing the road ahead (MoverAhead)

    public AutoPilot(TrackData t) => _t = t;

    public void Drive(CarSim car, float dt)
    {
        int n = _t.Center.Length;
        var p = new Vector2(car.Pos.X, car.Pos.Z);
        if (_idx < 0) _idx = _t.NearestCenter(p, out _);
        else
        {
            int best = _idx;
            float bestD = float.MaxValue;
            for (int k = -10; k < 60; k++)
            {
                int i = ((_idx + k) % n + n) % n;
                float d = _t.Center[i].DistanceSquaredTo(p);
                if (d < bestD) { bestD = d; best = i; }
            }
            _idx = best;
            if (bestD > 30f * 30f) _idx = _t.NearestCenter(p, out _);
        }

        float speed = car.Speed;
        int ti = (_idx + 5 + (int)(speed * 0.45f)) % n;
        Vector2 target = _t.Center[ti];
        // Something big moving on the road ahead (a snowplough, the bus, a tractor): drive round it on
        // the far side, slowly, instead of into it and backing off for ever.
        float shift = MoverAhead(p, out float moverDist, out bool moverMid);
        // Something slow ahead going our way, and traffic coming the other way: no overtaking, follow it.
        bool follow = shift != 0 && _moverSpeed >= 0f && _oncomingAt < 110f && _oncomingAt > moverDist;
        if (follow) shift = 0;
        // Once a side is chosen keep to it while the thing is near: switching at the last moment
        // (it swerves, the road bends) drives straight into it.
        _sideHold -= dt;
        if (shift != 0 && _sideHold > 0 && _side != 0 && moverDist < 35f) shift = _side;
        else if (shift != 0) { _side = shift; _sideHold = 1.5f; }
        if (shift != 0) target += new Vector2(-_t.CenterDir(ti).Y, _t.CenterDir(ti).X) * shift * _t.HalfWidth[ti];
        Vector2 fwd = new Vector2(car.Forward.X, car.Forward.Z).Normalized();
        Vector2 right = new Vector2(car.Right.X, car.Right.Z).Normalized();
        Vector2 to = (target - p).Normalized();
        float err = MathF.Atan2(to.Dot(right), to.Dot(fwd));
        float steer = Math.Clamp(err * 2.2f, -1f, 1f);

        // Slow down for what's coming: largest heading change within the braking horizon.
        float maxTurn = 0, maxCurvature = 0;
        Vector2 d0 = _t.CenterDir(_idx);
        int horizon = 12 + (int)(speed * 1.6f);
        for (int k = 6; k < horizon; k += 3)
        {
            Vector2 dk = _t.CenterDir((_idx + k) % n);
            float turn = MathF.Abs(d0.AngleTo(dk));
            maxTurn = Math.Max(maxTurn, turn);
            maxCurvature = Math.Max(maxCurvature, turn / k);
        }
        float grip = Surfaces.Get(_t.CenterSurf[(_idx + 10) % n]).Grip;
        float targetSpeed = Mathf.Lerp(34f, 10f, Math.Clamp(maxTurn / 1.3f, 0, 1)) * MathF.Sqrt(grip) * Aggression;
        // Aggression can use the straights; corner speed must still fit the available grip.
        float cornerLimit = MathF.Sqrt(grip * 9.81f * 0.8f * CornerGrip / Math.Max(maxCurvature, 0.0001f));
        targetSpeed = Math.Min(targetSpeed, cornerLimit);
        // Slow behind it only when it fills the middle of the road; beside it, drive on past.
        if (shift != 0 && moverDist < 30f && moverMid) targetSpeed = Math.Min(targetSpeed, moverDist < 18f ? 5f : 9f);
        // ...braking in time for it: no faster than could still stop 12 m behind it (on a wet road too).
        if (follow) targetSpeed = Math.Min(targetSpeed, _moverSpeed + MathF.Sqrt(2f * 4f * Math.Max(0f, moverDist - 12f)));
        // A train or the lunokhod across the road ahead: stop short of it.
        if (_crossAt < 40f) targetSpeed = Math.Min(targetSpeed, Math.Max(0f, (_crossAt - 9f) * 0.6f));

        // Stuck against a birch or in a ditch: back off like a real comrade.
        if (_reverseTime > 0)
        {
            _reverseTime -= dt;
            car.SetControls(false, true, -steer, false);
            return;
        }
        _stuckTime = speed < 1.5f ? _stuckTime + dt : 0;
        if (_stuckTime > 2f)
        {
            _stuckTime = 0;
            _reverseTime = 1.5f;
        }

        // Anticipate the stronger engine's pickup rather than flooring it right up to the target.
        float projectedSpeed = speed + car.DriveAcceleration * 0.18f;
        bool up = projectedSpeed < targetSpeed && (Math.Abs(err) < 1.2f || speed < 4f);
        bool down = speed > targetSpeed + 2f;
        car.SetControls(up, down, steer, false);

        // Comrade's right foot: ease off while the rear wheels spin.
        bool spin = car.Wheels[2].Spinning || car.Wheels[3].Spinning;
        _traction = spin ? Math.Max(0.35f, _traction - 3f * dt) : Math.Min(1f, _traction + 1.5f * dt);
        car.ThrottleTarget *= _traction * Math.Clamp((targetSpeed - projectedSpeed) / 3f, 0, 1);

        // No ABS: pump the brake when the front wheels lock so it can still steer.
        if (car.Brake > 0 && (car.Wheels[0].Locked || car.Wheels[1].Locked)) car.Brake = 0.3f;
    }

    /// <summary>
    /// The side to keep to (a share of the half-width, + right of the road's direction) for the
    /// nearest big moving obstacle within 45 m ahead (80 m when it comes the other way) and on the road: away from it, right if it is
    /// in the middle. 0: none. dist: how far ahead it is.
    /// </summary>
    float MoverAhead(Vector2 p, out float dist, out bool mid)
    {
        mid = false;
        _crossAt = _oncomingAt = float.MaxValue;
        _moverSpeed = -1f;
        int n = _t.Center.Length;
        dist = float.MaxValue;
        float lat = 0;
        foreach (var o in _t.Obstacles)
        {
            if (o.Radius < 0.9f || o.Velocity.LengthSquared() < 0.25f || (o.Center - p).LengthSquared() > 85f * 85f) continue;
            // Something coming the other way closes twice as fast: look further for it.
            int reach = o.Velocity.Dot(_t.CenterDir(_idx)) < 0f ? 80 : 45;
            // Where it is along the road (metres ahead of the car, round the bends) and across it.
            int k = -1;
            float best = float.MaxValue;
            for (int a = -2; a <= reach; a++)
            {
                int i = ((_idx + a) % n + n) % n;
                float d2 = _t.Center[i].DistanceSquaredTo(o.Center);
                if (d2 < best) { best = d2; k = a; }
            }
            int c = ((_idx + k) % n + n) % n;
            Vector2 d = _t.CenterDir(c);
            float l = (o.Center - _t.Center[c]).Dot(new Vector2(-d.Y, d.X));
            if (best > 15f * 15f) continue;
            // Crossing the road (a train, the lunokhod): no side to pass it on; wait for it.
            if (MathF.Abs(o.Velocity.Normalized().Dot(d)) < 0.5f)
            {
                if (MathF.Abs(l) < _t.HalfWidth[c] + o.Radius + 4f && k < _crossAt) _crossAt = k;
                continue;
            }
            if (MathF.Abs(l) > _t.HalfWidth[c] + o.Radius) continue;
            bool oncoming = o.Velocity.Dot(d) < 0f;
            if (oncoming && k < _oncomingAt) _oncomingAt = k;
            if (k >= dist) continue;
            _moverSpeed = oncoming ? -1f : o.Velocity.Length();
            dist = k;
            lat = l;
        }
        if (dist == float.MaxValue) return 0;
        mid = MathF.Abs(lat) < 1.2f;
        return MathF.Abs(lat) < 0.5f ? 0.7f : -MathF.Sign(lat) * 0.7f;
    }
}
