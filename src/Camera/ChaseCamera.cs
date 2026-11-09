using System;
using CCCPRacing.Car;
using Godot;

namespace CCCPRacing.Camera;

/// <summary>Rally-Sport style camera: fixed compass direction, tilted down, gently tracking the car
/// with a little look-ahead. Never rotates with the car, so driving "down" the screen is a skill.</summary>
public partial class ChaseCamera : Camera3D
{
    [Export] public Vector3 Offset = new(0, 31f, 23f);
    [Export] public float FollowRate = 4f;
    [Export] public float LookAhead = 0.45f;
    /// <summary>How far (m) the view may run ahead of the car. Unlimited, except in Stakhanov
    /// overdrive: at 300+ km/h the look-ahead would put the car off the bottom of the screen.</summary>
    public float MaxLead = float.PositiveInfinity;

    public bool ShakeEnabled { get; set; } = true;
    /// <summary>The surface's rumble now (m): the gameplay cameras of the car add it to their own shake.</summary>
    internal float Rumble => ShakeEnabled ? _rumble : 0f;

    Vector3 _focus;
    float _shake, _blast;
    CollisionResponse _collision;
    float _collisionAge = 10;
    Vector3? _collisionSource;
    Vector3 _driveSetback;
    internal Vector3 DriveSetback => _driveSetback;
    // The drive's feel (Feel): pull back with speed, the road's rumble, a dip on landing.
    float _feelZoom = 1f, _rumble, _rumbleClock, _dip, _dipVel;
    /// <summary>How far back the view pulls at full speed (share of its distance) and in the air, and how
    /// much closer it comes with the car standing still.</summary>
    const float SpeedPull = 0.25f, AirPull = 0.05f, StillPush = 0.13f;
    Vector3 _viewOffset;
    Vector3? _listener;
    AudioListener3D _ear;

    /// <summary>Where 3D sounds are heard from: the followed point (the car), facing like the camera
    /// so left/right panning matches the screen. Hearing from the camera 39 m up and back would
    /// make everything near the car sound distant.</summary>
    public Vector3 EarPosition => _ear?.GlobalPosition ?? GlobalPosition;
    readonly RandomNumberGenerator _rng = new();

    public override void _Ready()
    {
        _viewOffset = Offset;
        Fov = 45f;
        Near = 0.5f;
        Far = 260f;
        Current = true;
        _ear = new AudioListener3D { TopLevel = true };
        AddChild(_ear);
        _ear.MakeCurrent();
    }

    public void Snap(Vector3 target)
    {
        _focus = target;
        _viewOffset = Offset;
        _listener = null;
        _driveSetback = Vector3.Zero;
        Apply();
    }

    static float DrivingPull(float kmh, bool airborne) => kmh < 0 ? 1f
        : 1f + SpeedPull * Mathf.SmoothStep(0f, 1f, Math.Clamp((kmh - 35f) / 85f, 0f, 1f))
            - StillPush * (1f - Mathf.SmoothStep(0f, 1f, Math.Clamp(kmh / 35f, 0f, 1f))) + (airborne ? AirPull : 0f);

    /// <summary>A cut to normal driving framing starts at its actual speed distance,
    /// rather than visibly zooming out from the title menu's neutral camera arm.</summary>
    public void SnapDriving(Vector3 target, Vector3 velocity)
    {
        Snap(target);
        _dip = _dipVel = _rumble = _blast = 0;
        _feelZoom = DrivingPull(velocity.Length() * 3.6f, false);
        _viewOffset = Offset * _feelZoom;
        Vector3 lead = new(velocity.X * LookAhead, 0, velocity.Z * LookAhead);
        if (float.IsFinite(MaxLead)) lead = lead.LimitLength(MaxLead);
        _focus = target + lead;
        Apply();
    }

    /// <summary>A pressure wave hits the view: a hard slam in towards the car and a sharp, quickly dying jolt.</summary>
    public void Blast(float strength)
    {
        if (!ShakeEnabled) return;
        _blast = Math.Max(_blast, strength);
        _dipVel += strength * 26f;
    }

    public void Shake(float amount) { if (ShakeEnabled) _shake = Math.Max(_shake, amount); }

    /// <summary>Energy-scaled contact kick shared by top-down, bonnet, cinematic
    /// gameplay and tape cameras. Repeated contact cannot restart the whole tail.</summary>
    public void Collision(float normalSpeed, float massKg, Vector3? source = null)
    {
        if (!ShakeEnabled) return;
        var hit = CollisionResponse.Of(normalSpeed, massKg);
        if (hit.EnergyJoules <= 0 || (_collisionAge < .085f && hit.EnergyJoules < _collision.EnergyJoules * 1.4f)) return;
        _collision = hit; _collisionAge = 0; _collisionSource = source;
    }

    public void ClearCollision() { _collision = default; _collisionAge = 10; _collisionSource = null; }

    public void RestoreCollision(float speed, float mass, float age, Vector3 source)
    {
        ClearCollision(); Collision(speed, mass, source); _collisionAge = Math.Max(0, age);
    }

    Transform3D CollisionPose(Transform3D frame, float dt)
    {
        if (!ShakeEnabled) { ClearCollision(); return frame; }
        float t = _collisionAge, duration = _collision.Duration;
        _collisionAge += Math.Max(0, dt);
        if (_collision.EnergyJoules <= 0 || t >= duration) return frame;
        float proximity = _collisionSource is Vector3 source ? Math.Min(1, 35f / Math.Max(35, frame.Origin.DistanceTo(source))) : 1;
        float envelope = MathF.Exp(-4.5f * t / duration) * (1 - t / duration) * proximity;
        float travel = _collision.CameraTravel * envelope;
        // Keep a bonnet lens in the cockpit instead of throwing it through the body.
        if (Near < .1f) travel *= .23f;
        Vector3 offset = new Vector3(MathF.Sin(t * 69 + .35f), -MathF.Cos(t * 48), MathF.Cos(t * 37) * .4f) * travel;
        float angle = Mathf.DegToRad(_collision.CameraAngle) * envelope;
        var rotation = new Vector3(MathF.Cos(t * 43), MathF.Sin(t * 31) * .2f, MathF.Sin(t * 57 + .3f) * .45f) * angle;
        return new Transform3D(frame.Basis * Basis.FromEuler(rotation), frame.Origin + frame.Basis * offset);
    }

    /// <summary>Bounded camera lag from real acceleration; no shake or zoom pulse on a pedal press alone.</summary>
    public void Acceleration(Vector3 velocity, float acceleration, float dt)
    {
        Vector3 travel = new(velocity.X, 0, velocity.Z);
        float pull = ShakeEnabled ? Math.Clamp((acceleration - 2) / 8, 0, 1.35f) : 0;
        Vector3 wanted = travel.LengthSquared() > 0.25f ? -travel.Normalized() * pull : Vector3.Zero;
        _driveSetback = _driveSetback.Lerp(wanted, 1 - MathF.Exp(-10 * Math.Min(dt, 0.1f)));
    }

    /// <summary>
    /// The drive's feel, every frame (kmh &lt; 0: not driving, the view settles to its plain distance). Slow
    /// or standing, the view comes a little closer (slowly: a corner's braking hardly moves it); speed in
    /// km/h pulls it back (quicker, yet never pumping with the throttle); rumble (m) is a
    /// fine, smooth shake for the surface under the wheels; landing (s in the air) dips the view in
    /// towards the car and lets it spring back. Angle and compass direction never change.
    /// </summary>
    public void Feel(float kmh, float rumble, bool airborne, float landing, float dt)
    {
        if (!ShakeEnabled) { rumble = 0; landing = 0; }
        float pull = DrivingPull(kmh, airborne);
        _feelZoom = Mathf.Lerp(_feelZoom, pull, 1f - MathF.Exp(-(pull > _feelZoom ? 1.3f : 0.4f) * dt));
        _rumble = Mathf.Lerp(_rumble, rumble, 1f - MathF.Exp(-6f * dt));
        _rumbleClock += dt;
        _blast *= MathF.Exp(-6f * dt);
        if (landing > 0) _dipVel += Math.Min(1.6f, landing) * 9f;      // in towards the car
        // A stiff, well damped spring: one dip and a small rebound.
        _dipVel += (-60f * _dip - 9f * _dipVel) * dt;
        _dip += _dipVel * dt;
    }

    public void Follow(Vector3 target, Vector3 velocity, float dt)
        => FollowView(target, velocity, dt, _feelZoom, target);

    /// <summary>Frame the car and a teaching subject together, leaving room for models and captions.</summary>
    public void FramePair(Vector3 car, Vector3 subject, float dt)
    {
        Vector3 center = car.Lerp(subject, 0.5f);
        float aspect = GetViewport().GetVisibleRect().Size.Aspect();
        float zoom = PairZoom(car, subject, center, Offset, Fov, aspect);
        FollowView(center, Vector3.Zero, dt, zoom, car);
    }

    /// <summary>A scenic shot with its own compass angle and dolly distance; hearing follows the scene.</summary>
    public void Scenic(Vector3 subject, Vector3 offset, float dt)
        => FollowView(subject, Vector3.Zero, dt, 1f, subject, offset);

    /// <summary>The drone film's microphone: the ears go to the lens (after Scenic, which puts them at its subject, the car),
    /// so a MiK taking off beside the drone is heard where it is.</summary>
    public void ListenAt(Vector3 position, Vector3 forward)
    {
        if (_ear == null) return;
        var flat = new Vector3(forward.X, 0, forward.Z);
        if (flat.LengthSquared() < 1e-4f) flat = -GlobalTransform.Basis.Z;
        _ear.GlobalTransform = new Transform3D(Basis.LookingAt(flat.Normalized(), Vector3.Up), position);
    }

    internal static float PairZoom(Vector3 car, Vector3 subject, Vector3 center, Vector3 offset, float fov, float aspect)
    {
        Basis view = Basis.LookingAt(-offset.Normalized(), Vector3.Up);
        float tanV = MathF.Tan(Mathf.DegToRad(fov) * 0.5f);
        float distance = offset.Length();
        foreach (Vector3 point in new[] { car, subject })
        {
            Vector3 p = view.Inverse() * (point - center);
            // Five metres around each anchor includes a car, person or landmark entrance.
            distance = Math.Max(distance, p.Z + (Math.Abs(p.X) + 5f) / (tanV * aspect * 0.78f));
            distance = Math.Max(distance, p.Z + (Math.Abs(p.Y) + 5f) / (tanV * 0.62f));
        }
        return distance / offset.Length();
    }

    void FollowView(Vector3 target, Vector3 velocity, float dt, float zoom, Vector3 listener, Vector3? scenicOffset = null)
    {
        if (scenicOffset.HasValue || velocity.LengthSquared() < 0.01f) _driveSetback = Vector3.Zero;
        _listener = listener;
        _viewOffset = _viewOffset.Lerp(scenicOffset ?? Offset * zoom, 1f - MathF.Exp(-2.5f * dt));
        Vector3 want = target + new Vector3(velocity.X, 0, velocity.Z) * LookAhead;
        _focus = _focus.Lerp(want, 1f - MathF.Exp(-FollowRate * dt));
        var lead = new Vector2(_focus.X - target.X, _focus.Z - target.Z);
        if (lead.Length() > MaxLead)
        {
            lead = lead.LimitLength(MaxLead);
            _focus = new Vector3(target.X + lead.X, _focus.Y, target.Z + lead.Y);
        }
        _focus.Y = Mathf.Lerp(_focus.Y, target.Y, 1f - MathF.Exp(-2f * dt));
        _shake = Math.Max(0, _shake - dt * 1.5f);
        Apply(dt);
    }

    /// <summary>Gameplay photography keeps the same car listener and shake as the standard view.</summary>
    public void Pose(Vector3 eye, Vector3 target, Vector3 car, float dt, float roll = 0f)
    {
        if (!ShakeEnabled) _shake = 0;
        _shake = Math.Max(0, _shake - dt * 1.5f);
        Vector3 jitter = _shake > 0 ? new Vector3(_rng.RandfRange(-1, 1), _rng.RandfRange(-1, 1), 0) * _shake * 0.4f : Vector3.Zero;
        Basis view = Basis.LookingAt(target - eye, Vector3.Up);
        if (roll != 0f && ShakeEnabled) view *= new Basis(Vector3.Back, roll);      // (about the lens's own axis: + leans the picture to the left)
        GlobalTransform = CollisionPose(new Transform3D(view, eye + jitter), dt);
        Vector3 facing = new(target.X - eye.X, 0, target.Z - eye.Z);
        if (_ear != null) _ear.GlobalTransform = new Transform3D(Basis.LookingAt(facing.Normalized(), Vector3.Up), car + Vector3.Up * 1.5f);
    }

    void Apply(float dt = 0)
    {
        if (!ShakeEnabled) _shake = 0;
        Vector3 jitter = _shake > 0 ? new Vector3(_rng.RandfRange(-1, 1), _rng.RandfRange(-1, 1), 0) * _shake * 0.4f : Vector3.Zero;
        if (_blast > .01f) jitter += new Vector3(_rng.RandfRange(-1, 1), _rng.RandfRange(-1, 1), 0) * _blast * 1.6f;
        // The road's rumble: two quick, smooth wobbles across the view (not per-frame noise), and the dip.
        float t = _rumbleClock;
        Vector3 rumble = new Vector3(MathF.Sin(t * 71f) + 0.6f * MathF.Sin(t * 113f + 1.3f), MathF.Sin(t * 83f + 0.7f) + 0.6f * MathF.Sin(t * 131f + 2.1f), 0) * (_rumble * 0.6f);
        Vector3 eye = _focus + _viewOffset + (ShakeEnabled ? _driveSetback : Vector3.Zero) + jitter + Basis.LookingAt(-_viewOffset.Normalized(), Vector3.Up) * rumble
            - _viewOffset.Normalized() * _dip;
        Far = Math.Max(260f, _viewOffset.Length() + 200f); // High scenic views must still render the ground and distant landmarks.
        GlobalTransform = CollisionPose(new Transform3D(Basis.LookingAt(-_viewOffset.Normalized(), Vector3.Up), eye), dt);
        if (_ear != null) _ear.GlobalTransform = new Transform3D(Basis.LookingAt(-new Vector3(_viewOffset.X, 0, _viewOffset.Z).Normalized(), Vector3.Up), (_listener ?? _focus) + Vector3.Up * 1.5f);
    }
}
