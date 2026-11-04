using System;
using Godot;
using CCCPRacing.Track;

namespace CCCPRacing.Camera;

/// <summary>Quiet broadcast-style coverage of the title's saved lap, independent of camera discoveries.</summary>
internal sealed class ReplayCameraDirector
{
    internal enum Shot { Overview, Follow, Trackside, Bonnet }
    internal readonly record struct View(Shot Shot, Vector3 Eye, Vector3 Target, float Fov, float Near);
    readonly GameplayCinematicCamera _follow = new();
    float _start = -1, _lastClock = -1;
    int _slot = -1;
    bool _fallback;
    Vector3 _stand, _lastCar;
    float _encounterUntil = -1, _encounterCooldown = -1;
    Shot _highlightShot = Shot.Bonnet, _previousWanted;

    internal void Reset()
    { _start = _lastClock = _encounterUntil = _encounterCooldown = -1; _slot = -1; _follow.Reset(); }

    internal static bool ClosePass(TrackData track, Vector3 car, Vector3 velocity)
    {
        Vector2 motion = new(velocity.X, velocity.Z);
        if (motion.LengthSquared() < 64) return false;
        foreach (var other in track.Obstacles)
        {
            if (other.Radius < .6f || other.Velocity.LengthSquared() < 6.25f) continue;
            Vector2 relative = other.Center - new Vector2(car.X, car.Z), speed = other.Velocity - motion;
            if (relative.Dot(motion) < 0 || relative.LengthSquared() > 10000 || speed.LengthSquared() < 25) continue;
            float time = -relative.Dot(speed) / speed.LengthSquared();
            if (time < 0 || time > 1.7f) continue;
            if ((relative + speed * time).Length() < other.Radius + 3.5f) return true;
        }
        return false;
    }

    internal static bool TerrainHighlight(TrackData track, Vector3 car)
    {
        int at = track.NearestCenter(new Vector2(car.X, car.Z), out float distance), n = track.Center.Length;
        if (distance > track.HalfWidth[at] + 2) return false;
        float low = float.PositiveInfinity, high = float.NegativeInfinity, grade = 0;
        for (int k = -10; k <= 35; k++)
        {
            int i = (at + k + n) % n;
            float height = track.CenterH[i];
            low = Math.Min(low, height); high = Math.Max(high, height);
            grade = Math.Max(grade, Math.Abs(height - track.CenterH[(i + 1) % n]));
        }
        return high - low >= 3f && grade >= .11f;
    }

    internal static bool ClearView(TrackData track, Vector3 eye, Vector3 car)
    {
        Vector3 target = car + Vector3.Up;
        for (int i = 1; i <= 16; i++)
        {
            Vector3 p = target.Lerp(eye, i / 16f);
            if (p.Y < WorldBackdrop.HeightAt(track, new Vector2(p.X, p.Z)) + 1f) return false;
        }
        Vector2 a = new(eye.X, eye.Z), b = new(target.X, target.Z), ab = b - a;
        foreach (var obstacle in track.Obstacles)
        {
            float u = Math.Clamp((obstacle.Center - a).Dot(ab) / Math.Max(.01f, ab.LengthSquared()), 0, 1);
            if (u < .92f && obstacle.Center.DistanceTo(a + ab * u) < obstacle.Radius + 1.5f) return false;
        }
        return true;
    }

    internal View Frame(TrackData track, Vector3 car, Vector3 velocity, Basis body,
        Vector3 future, float clock, float dt)
    {
        if (_start < 0 || clock < _lastClock || car.DistanceTo(_lastCar) > 35)
        { Reset(); _start = clock; }
        _lastClock = clock; _lastCar = car;
        // 40 s: 24 s of the game's top-down view, 10 s bonnet, 4 s trackside, 2 s follow.
        float phase = (clock - _start) % 40f;
        int slot = phase < 10 ? 0 : phase < 16 ? 1 : phase < 24 ? 2 : phase < 28 ? 3
            : phase < 34 ? 4 : phase < 38 ? 5 : 6;
        Shot shot = slot switch { 1 or 5 => Shot.Bonnet, 3 => Shot.Trackside, 6 => Shot.Follow, _ => Shot.Overview };
        Vector3? road = GameplayCinematicCamera.RoadForward(track, car);
        Vector3 heading = road ?? new Vector3(-body.Z.X, 0, -body.Z.Z).Normalized();
        var follow = _follow.Frame(car, velocity, -body.Z, dt,
            (x, z) => WorldBackdrop.HeightAt(track, new Vector2(x, z)), road);
        // Keep the speed and body motion through corners, hills and slides. Only an overturned,
        // stopped or backwards-moving car needs an exterior view.
        bool bonnet = body.Y.Y > .65f && velocity.Length() > 3 && velocity.Normalized().Dot(-body.Z) > .15f;
        bool closePass = bonnet && ClosePass(track, car, velocity);
        if (bonnet && clock - _start >= 3 && clock >= _encounterCooldown
            && (closePass || TerrainHighlight(track, car)))
        {
            _encounterUntil = clock + 3.5f;
            _encounterCooldown = clock + 30f; // short highlights preserve top-down as the main view
            _highlightShot = closePass ? Shot.Bonnet : Shot.Trackside;
            _fallback = false;
        }
        bool highlight = bonnet && clock < _encounterUntil;
        if (highlight && closePass) _highlightShot = Shot.Bonnet;
        if (highlight && shot != Shot.Trackside) shot = _highlightShot;
        if (slot != _slot || shot != _previousWanted)
        {
            _slot = slot; _previousWanted = shot; _fallback = false;
            if (shot == Shot.Trackside)
            {
                Vector3 right = new(-heading.Z, 0, heading.X);
                _fallback = true;
                foreach (int side in new[] { 1, -1 })
                {
                    var eye = future + right * (side * 28) + Vector3.Up * 14;
                    eye.Y = Math.Max(eye.Y, WorldBackdrop.HeightAt(track, new Vector2(eye.X, eye.Z)) + 8);
                    if (!ClearView(track, eye, car) || !ClearView(track, eye, future)) continue;
                    _stand = eye; _fallback = false; break;
                }
            }
        }
        if (shot == Shot.Trackside && !_fallback)
        {
            if (ClearView(track, _stand, car))
                return new(Shot.Trackside, _stand, car + Vector3.Up * .8f, 55, .3f);
            _fallback = true; // do not flicker back into this shot when the obstruction passes
        }
        if (highlight && shot == Shot.Trackside && _fallback)
        {
            var view = GameplayBonnetCamera.Frame(car, body);
            return new(Shot.Bonnet, view.eye, view.target, 75, .06f);
        }
        if (shot == Shot.Bonnet && !bonnet) _fallback = true;
        if (shot == Shot.Bonnet && !_fallback)
        {
            var view = GameplayBonnetCamera.Frame(car, body);
            return new(Shot.Bonnet, view.eye, view.target, 75, .06f);
        }
        if (shot != Shot.Follow)
        {
            Vector3 lead = new Vector3(velocity.X, 0, velocity.Z).LimitLength(20) * .25f;
            return new(Shot.Overview, car + new Vector3(0, 36, 28), car + lead, 45, .5f);
        }
        return new(Shot.Follow, follow.eye, follow.target, 60, .3f);
    }
}
