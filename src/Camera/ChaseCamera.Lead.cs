using System;
using Godot;

namespace CCCPRacing.Camera;

/// <summary>
/// The look-ahead experiment of the overhead drive camera (docs/reviews/camera-lookahead-experiment-20261113.md). The angle (Offset), the compass direction
/// and the lens never change; only where the view's centre lies ahead of the car does:
/// the car's position is followed on its own (PosRate), the direction of the look-ahead turns slowly and at a limited rate (DirRate, DirMaxTurn),
/// its amount follows the speed on its own response (LeadTime, MagRise, MagFall), and the result is limited so the car stays inside a screen-space margin
/// (MarginBottom/Top/Side) at any speed, braking, reversing or slide. <see cref="DirectedLead"/> = false (or <c>--camera-classic</c>) brings the old behaviour back unchanged.
/// </summary>
public partial class ChaseCamera
{
    /// <summary>Set from the chosen camera view (RaceScene.GameplayCamera: DIRECTED or CLASSIC, cycled with C / LB or the CAMERA row). True, the default: the directed look-ahead below. False: the classic one (velocity x LookAhead through one FollowRate lag).</summary>
    public static bool DirectedLead = true;

    /// <summary>Seconds of travel the view's centre is put ahead of the car (the car itself is followed closely, so this is the visible lead).</summary>
    public static float LeadTime = 0.70f;
    /// <summary>The car's own position follow (1/s): fine enough to smooth physics steps, quick enough that the car does not drift on screen.</summary>
    public const float PosRate = 10f;
    /// <summary>The look-ahead's direction follows the travel direction at this response (1/s), never faster than DirMaxTurn (rad/s): a corner moves the framing calmly.</summary>
    public const float DirRate = 1.7f, DirMaxTurn = 1.9f;
    /// <summary>The look-ahead's amount rises and falls at these responses (1/s); under DirMinSpeed (m/s) the direction is not updated, and the amount fades in over GateLow..GateHigh (m/s).</summary>
    public const float MagRise = 1.8f, MagFall = 2.6f, DirMinSpeed = 2f, GateLow = 1.5f, GateHigh = 6f;
    /// <summary>The car's share of the screen it may reach (normalised device coordinates, 1 = the edge): the bottom is held earlier, the top and sides have more room.</summary>
    public const float MarginBottom = 0.58f, MarginTop = 0.68f, MarginSide = 0.62f;
    /// <summary>The outer, hard margins: the soft limit above is followed with a finite response (no kink in the framing when it starts to bind), these never yield.</summary>
    public const float HardBottom = 0.82f, HardTop = 0.90f, HardSide = 0.84f;
    /// <summary>The soft limit is followed at these responses (1/s): tightening is quick, letting go slow.</summary>
    public const float ScaleTighten = 7f, ScaleRecover = 1.6f;
    /// <summary>The followed position never lags the car by more than this (m): at 380 km/h or after a jump the smoothing must not let the car run out of the view.</summary>
    public const float PosMaxLag = 5f;

    /// <summary>MaxLead switches between 8 m and unlimited at once (OCTANE BOOSTER or a Stakhanov rocket starts or ends); the limit that is applied to the view moves between them at this rate (m/s), so the
    /// framing never jumps. It is only the speed of the change: the screen margins above keep the car in view at any moment.</summary>
    public const float LeadCapRate = 12f, LeadCapAccel = 48f;

    Vector3 _pos;
    float _leadCap = float.PositiveInfinity, _capVel, _natPrev = float.NaN;      // the MaxLead actually applied, and the speed it moves at (m/s)
    Vector2 _leadDir = new(0, -1);
    float _leadMag, _leadScale = 1f;
    Vector3 _carNow;

    /// <summary>Where the view's centre lies relative to the car (m, ground plane): what the experiment sets.</summary>
    internal Vector2 LeadOffset => new(_focus.X - _carNow.X, _focus.Z - _carNow.Z);
    internal float LeadScale => _leadScale;
    internal Vector3 Focus => _focus;

    void ResetLead(Vector3 target)
    {
        _pos = target; _carNow = target; _leadMag = 0; _leadScale = 1f; _leadDir = new Vector2(0, -1); _leadCap = float.PositiveInfinity; _capVel = 0; _natPrev = float.NaN;
    }

    float ViewAspect()
    {
        var vp = IsInsideTree() ? GetViewport() : null;
        if (vp == null) return 16f / 9f;
        var size = vp.GetVisibleRect().Size;
        return size.Y > 1 ? size.X / size.Y : 16f / 9f;
    }

    /// <summary>The car's place on the screen (NDC, x right, y up, 1 = the edge) for a view centred at <paramref name="focus"/> with this eye offset: a plain pinhole of the camera's own fixed angle.</summary>
    internal static Vector2 ScreenOf(Vector3 car, Vector3 focus, Vector3 viewOffset, float fovDeg, float aspect)
    {
        Basis b = Basis.LookingAt(-viewOffset.Normalized(), Vector3.Up);
        Vector3 q = car - (focus + viewOffset);
        float depth = -q.Dot(b.Z), tanV = MathF.Tan(Mathf.DegToRad(fovDeg) * 0.5f);
        return new Vector2(q.Dot(b.X) / (depth * tanV * aspect), q.Dot(b.Y) / (depth * tanV));
    }

    /// <summary>The largest share (0..1) of the wanted lead that keeps the car inside the screen margins. Each margin is linear in that share, so it is solved exactly.</summary>
    float ScreenLimit(Vector3 target, Vector2 lead, float mBottom = MarginBottom, float mTop = MarginTop, float mSide = MarginSide)
    {
        Vector3 o = _viewOffset;
        Basis b = Basis.LookingAt(-o.Normalized(), Vector3.Up);
        float tanV = MathF.Tan(Mathf.DegToRad(Fov) * 0.5f), tanH = tanV * ViewAspect();
        Vector3 q0 = new Vector3(target.X - _pos.X, target.Y - _focus.Y, target.Z - _pos.Z) - o;      // the car from the eye with no lead
        Vector3 ld = new(lead.X, 0, lead.Y);
        float d0 = -q0.Dot(b.Z), dl = ld.Dot(b.Z);      // the depth is d0 + s * dl
        float s = 1f;
        void C(float a, float k) { if (a <= 0f) s = 0f; else if (k < 0f) s = MathF.Min(s, a / -k); }      // a + k s >= 0
        C(q0.Dot(b.Y) + mBottom * tanV * d0, -ld.Dot(b.Y) + mBottom * tanV * dl);
        C(mTop * tanV * d0 - q0.Dot(b.Y), mTop * tanV * dl + ld.Dot(b.Y));
        C(mSide * tanH * d0 - q0.Dot(b.X), mSide * tanH * dl + ld.Dot(b.X));
        C(q0.Dot(b.X) + mSide * tanH * d0, -ld.Dot(b.X) + mSide * tanH * dl);
        return Math.Clamp(s, 0f, 1f);
    }

    void RotateLead(Vector2 travel, float dt)
    {
        if (_leadMag < 0.75f) { _leadDir = travel; return; }      // (nothing to swing yet: a start from rest or after a reverse takes its direction at once)
        float cur = _leadDir.Angle(), delta = MathF.IEEERemainder(travel.Angle() - cur, MathF.Tau);
        float step = Math.Clamp(delta * (1f - MathF.Exp(-DirRate * dt)), -DirMaxTurn * dt, DirMaxTurn * dt);
        _leadDir = Vector2.FromAngle(cur + step);
    }

    /// <summary>MaxLead (the overdrive's 8 m) is a limit on the FINAL ground-plane vector from the car to the view's centre, whatever made it: the lead, the position lag, or a reversal that sets them against each other.
    /// <paramref name="cut"/>: a cut takes the limit at once; otherwise the applied limit moves towards MaxLead at LeadCapRate from where the view's centre is now, and is released only when it
    /// no longer binds. The lead state itself (_leadMag, _pos) is never touched, so the framing is the same as it would be without the limit once the limit goes.</summary>
    void LimitFocus(Vector3 target, float dt, bool cut = false)
    {
        var offset = new Vector2(_focus.X - target.X, _focus.Z - target.Z);
        float natural = offset.Length();
        if (cut) { _leadCap = MaxLead; _capVel = 0; }
        else if (float.IsFinite(MaxLead) || float.IsFinite(_leadCap))
        {
            // The applied limit chases MaxLead (or, when MaxLead is released, the view's own distance) with a bounded speed that itself builds up and dies away at LeadCapAccel: no jump, no kink.
            bool release = !float.IsFinite(MaxLead);
            // (it starts where the view's centre is, moving as the centre moves: the limit's first frame changes neither the position nor the velocity of the framing)
            if (!float.IsFinite(_leadCap)) { _leadCap = natural; _capVel = float.IsFinite(_natPrev) && dt > 0f ? Math.Clamp((natural - _natPrev) / dt, -LeadCapRate * 4f, LeadCapRate * 4f) : 0f; }
            float goal = release ? natural : MaxLead, dist = goal - _leadCap, sign = MathF.Sign(dist);
            float want = sign * MathF.Min(LeadCapRate, LeadCapAccel / LeadCapRate * MathF.Abs(dist));      // (a quick approach that fades out smoothly: the fade's own deceleration, LeadCapAccel / LeadCapRate x speed, never asks for more than LeadCapAccel, so it never stops with speed left)
            _capVel += Math.Clamp(want - _capVel, -LeadCapAccel * dt, LeadCapAccel * dt);
            float next = _leadCap + _capVel * dt;
            if (MathF.Sign(goal - next) != sign && sign != 0f) next = goal;
            _leadCap = next;
            if (release && _leadCap >= natural - 0.003f) { _leadCap = float.PositiveInfinity; _capVel = 0; }
        }
        _natPrev = natural;
        if (!float.IsFinite(_leadCap) || natural <= _leadCap) return;
        offset = offset.LimitLength(_leadCap);
        _focus.X = target.X + offset.X; _focus.Z = target.Z + offset.Y;
    }

    float LeadMagnitude(float speed) => LeadTime * speed * Mathf.SmoothStep(GateLow, GateHigh, speed);

    /// <summary>The overhead camera of a driven (or replayed) car: the directed look-ahead, or the classic one while the experiment is off.</summary>
    public void FollowDrive(Vector3 target, Vector3 velocity, float dt)
    {
        if (!DirectedLead) { Follow(target, velocity, dt); return; }
        if (velocity.LengthSquared() < 0.01f) _driveSetback = Vector3.Zero;
        _listener = target; _carNow = target;
        _viewOffset = _viewOffset.Lerp(ArmFor(_feelZoom), 1f - MathF.Exp(-2.5f * dt));
        float k = 1f - MathF.Exp(-PosRate * dt);
        // The follow of a moving car, solved exactly for a steady velocity over the frame: the same place at any frame rate.
        float catchUp = k / PosRate - dt * (1f - k);
        _pos.X += (target.X - _pos.X) * k - velocity.X * catchUp; _pos.Z += (target.Z - _pos.Z) * k - velocity.Z * catchUp;
        var lag = new Vector2(target.X - _pos.X, target.Z - _pos.Z);
        if (lag.Length() > PosMaxLag) { lag = lag.LimitLength(PosMaxLag); _pos.X = target.X - lag.X; _pos.Z = target.Z - lag.Y; }
        var v = new Vector2(velocity.X, velocity.Z); float speed = v.Length();
        if (speed > DirMinSpeed) RotateLead(v / speed, dt);
        float magTarget = LeadMagnitude(speed);
        _leadMag += (magTarget - _leadMag) * (1f - MathF.Exp(-(magTarget > _leadMag ? MagRise : MagFall) * dt));
        Vector2 lead = _leadDir * _leadMag;
        float limit = ScreenLimit(target, lead);
        _leadScale = Mathf.Lerp(_leadScale, limit, 1f - MathF.Exp(-(limit < _leadScale ? ScaleTighten : ScaleRecover) * dt));
        _leadScale = MathF.Min(_leadScale, ScreenLimit(target, lead, HardBottom, HardTop, HardSide));      // (the car never gets nearer the edge than the hard margins)
        _focus.X = _pos.X + lead.X * _leadScale; _focus.Z = _pos.Z + lead.Y * _leadScale;
        LimitFocus(target, dt);
        _focus.Y = Mathf.Lerp(_focus.Y, target.Y, 1f - MathF.Exp(-2f * dt));
        _shake = Math.Max(0, _shake - dt * 1.5f);
        Apply(dt);
    }

    /// <summary>A cut to the directed framing at the car's actual speed (no settling afterwards at constant speed).</summary>
    void SnapDirected(Vector3 target, Vector3 velocity)
    {
        ResetLead(target);
        // A car at a steady speed is followed with a lag of speed / PosRate: start there, so there is nothing to settle after a cut.
        var lag = new Vector2(velocity.X, velocity.Z) / PosRate; lag = lag.LimitLength(PosMaxLag);
        _pos = new Vector3(target.X - lag.X, target.Y, target.Z - lag.Y);
        var v = new Vector2(velocity.X, velocity.Z); float speed = v.Length();
        if (speed > DirMinSpeed) _leadDir = v / speed;
        _leadMag = LeadMagnitude(speed);
        Vector2 lead = _leadDir * _leadMag;
        _focus = target;
        _leadScale = ScreenLimit(target, lead);
        _focus = new Vector3(_pos.X + lead.X * _leadScale, target.Y, _pos.Z + lead.Y * _leadScale);
        LimitFocus(target, 0f, cut: true);
        Apply();
    }
}
