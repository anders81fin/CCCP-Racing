using System;
using Godot;

namespace CCCPRacing.Camera;

/// <summary>
/// The ROAD-ALIGNED gameplay view: an eye high above and a little behind the car looking down at a slant (about 50 degrees, the car low in the picture), turned so that the screen's up is the way the road runs (not a fixed compass direction like the overhead views).
/// The heading is the road's direction where the car is on the road, the travel direction (or the nose) elsewhere, and it eases round (a corner turns the picture slowly, a spin never whips it). The view
/// looks well ahead of the car, more at speed, so the road to come is the larger part of the picture.
/// </summary>
internal sealed class GameplayRoadCamera
{
    const float Height = 34f, Back = 20f, Ahead = 8.5f, TurnRate = 2.2f, MaxTurn = 1.4f, Omega = 2.5f, LeadRate = 3f, LeadPerSpeed = 0.14f, MaxLead = 5.6f;
    bool _ready;
    float _yaw;
    Vector3 _off, _offVel, _lastCar, _lastVel;      // (the view's centre relative to the car, and its velocity relative to the car's)
    float _lead;

    public void Reset() => _ready = false;

    /// <param name="road">the road's direction at the car (flat, unit) or null off the road</param>
    public (Vector3 eye, Vector3 target) Frame(Vector3 car, Vector3 velocity, Vector3 heading, Vector3? road, float dt, Func<float, float, float> ground)
    {
        dt = Math.Clamp(dt, 0f, 0.05f);
        Vector3 motion = new(velocity.X, 0, velocity.Z);
        float speed = motion.Length();
        Vector3 nose = new(heading.X, 0, heading.Z);
        Vector3 want = road ?? (speed > 3f ? motion / speed : nose.LengthSquared() > 0.01f ? nose.Normalized() : Vector3.Forward);
        // Reversing along the road keeps the road's direction (up the screen stays the way the road runs): only the off-road heading is the travel.
        float target = MathF.Atan2(want.X, want.Z);
        bool snap = !_ready || car.DistanceTo(_lastCar) > 35f;
        if (snap) _yaw = target;
        else
        {
            float step = Mathf.AngleDifference(_yaw, target) * (1f - MathF.Exp(-TurnRate * dt));
            _yaw += Math.Clamp(step, -MaxTurn * dt, MaxTurn * dt);
        }
        Vector3 dir = new(MathF.Sin(_yaw), 0, MathF.Cos(_yaw));
        // The eye hangs a little behind the car (so the car stands low in the picture) and looks Ahead of it, a little more at speed: a slanted view over the road to come, not straight down.
        float lead = Math.Clamp(speed * LeadPerSpeed, 0f, MaxLead);
        _lead = snap ? lead : Mathf.Lerp(_lead, lead, 1f - MathF.Exp(-LeadRate * dt));
        // The view's centre hangs on a soft spring in the car's frame: a steady speed costs it nothing, but the car's acceleration, braking and cornering leave it behind, so the car moves about in the picture
        // (it pulls ahead in a launch, slides forward under braking, swings to the outside in a corner) instead of being bolted to one spot.
        Vector3 prevVel = _lastVel; _lastVel = velocity;
        if (snap) { _off = Vector3.Zero; _offVel = Vector3.Zero; }
        else
        {
            Vector3 aCar = (new Vector3(velocity.X, 0, velocity.Z) - new Vector3(prevVel.X, 0, prevVel.Z)) / Math.Max(dt, 1e-4f);
            aCar = aCar.LimitLength(30f);
            int steps = Math.Max(1, (int)MathF.Ceiling(dt / (1f / 120f)));
            float h = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                _offVel += (-Omega * Omega * _off - 2f * Omega * _offVel - aCar) * h;
                _off += _offVel * h;
            }
        }
        Vector3 centre = car + _off;
        _lastCar = car;
        _ready = true;
        Vector3 eye = centre - dir * Back + Vector3.Up * Height;
        if (ground != null) eye.Y = Math.Max(eye.Y, ground(eye.X, eye.Z) + 6f);
        return (eye, centre + dir * (Ahead + _lead));
    }
}
