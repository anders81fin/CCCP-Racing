using System;
using CCCPRacing.Audio;
using CCCPRacing.Car;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Track;

/// <summary>
/// Heat-seeking air-to-air missile in the  mould. Leaves the rail at the carrier's
/// velocity plus a kick, accelerates to an arcade top speed, and steers at a limited turn rate
/// towards where the target will be (lead pursuit, proportional-navigation-ish). It is no sniper:
/// it homes on a point a few metres off the target, drawn at launch and wandering in flight, so
/// most bursts are near misses. The last few metres it flies blind, straight on: a target that
/// turns hard as it closes in makes it overshoot, and once the target is too far off its nose
/// the seeker loses lock. The proximity fuse goes off at the closest
/// approach inside <see cref="FuseRadius"/>; otherwise on the ground or when its time runs out.
/// Driven by its owner via <see cref="Tick"/>.
/// </summary>
public partial class Missile : Node3D
{
    public const float Boost = 8f;           // m/s off the rail on top of the carrier's velocity
    public const float Accel = 18f;          // m/s² while the motor burns
    public const float MaxSpeed = 45f;       // a faster carrier's speed bleeds off down to this
    const float Bleed = 30f;                 // m/s² above MaxSpeed
    public const float TurnRate = 0.9f;      // rad/s
    const float TerminalTurnRate = 2.5f;     // rad/s once blind, homing on the remembered course
    const float SteerGain = 8f;              // turn rate (rad/s) per rad of heading error, up to TurnRate
    public const float BurnTime = 10f;
    public const float LifeTime = 16f;       // self-destruct: reaches ~600 m
    public const float FuseRadius = EngineHealth.MissileReach;
    const float SeekerLimit = 1.0f;          // rad off the nose: lock lost (once it has been acquired)
    const float HardTurnRate = 1.1f;         // rad/s: the target's course swinging this fast (handbrake, a hard jink)...
    const float HardTurnTime = 0.15f;        // ...for this long, 45-150 m out, and the seeker loses it
    const float HardTurnRange = 150f;
    const float BlindRange = 45f;            // m: too close to follow a jink any more, it flies on to where the target was heading
    // Aim point: this far off the target (m) at launch, in a random direction, mostly level, plus a
    // slow wander of up to Wander m in flight.
    const float AimMin = 1f, AimMax = 6f, Wander = 1f;
    const float DropTime = 0.25f;            // falls clear of the rail before steering
    // Aims at the intercept point, but on a lagging estimate of the target's velocity; inside
    // BlindRange it no longer steers, so a car turning hard a second before impact makes it miss.
    const float LeadFactor = 1f, TargetVelLag = 0.1f, TargetAccLag = 0.2f, MaxTargetAcc = 12f;

    static readonly bool Dbg = OS.HasEnvironment("MISSILEDBG");
    static int _serial;
    /// <summary>Test (--missile-seed): shifts every missile's random draws.</summary>
    public static int SeedBase;

    /// <summary>(position, distance to the target) when the warhead goes off — ground, fuse or timeout.</summary>
    public event Action<Vector3, float> Exploded;

    public Vector3 Pos { get; private set; }
    public Vector3 Vel { get; private set; }
    public float Age { get; private set; }
    /// <summary>Closest the missile has come to its target so far.</summary>
    public float MinDist { get; private set; } = float.MaxValue;
    public bool Alive { get; private set; } = true;
    public bool Locked { get; private set; } = true;
    /// <summary>Where its target was on the last tick.</summary>
    public Vector3 TargetPos => _lastTarget;

    TrackData _t;
    TyreEffects _fx;
    Func<Vector3> _target;
    Vector3 _lastTarget, _targetVel, _targetAcc, _aim, _wf, _wp, _blindPos, _blindVel, _blindAcc;
    float _blindAt = -1f;
    MeshInstance3D _flame;
    MissileSound _sound;
    bool _acquired;
    float _turnRate, _hardTurn;
    float _nextLog, _los, _maxLos;
    int _id;
    readonly RandomNumberGenerator _rng = new();

    /// <summary>Fire a missile from <paramref name="from"/> (nose along its -Z) and add it under <paramref name="parent"/>.</summary>
    public static Missile Launch(Node parent, TrackData t, TyreEffects fx, Transform3D from, Vector3 carrierVel, Func<Vector3> target)
    {
        var m = new Missile { _t = t, _fx = fx, _target = target, _id = ++_serial };
        m._rng.Seed = (ulong)(7919 * (m._id + 1000 * SeedBase));
        float a = m._rng.RandfRange(0f, MathF.Tau), u = m._rng.Randf(), r = AimMin + (AimMax - AimMin) * u * u * u * u;   // most draws close
        m._aim = new Vector3(MathF.Cos(a) * r, m._rng.RandfRange(0f, 0.6f), MathF.Sin(a) * r);
        m._wf = new Vector3(m._rng.RandfRange(0.5f, 1.1f), m._rng.RandfRange(0.5f, 1.1f), m._rng.RandfRange(0.5f, 1.1f));
        m._wp = new Vector3(m._rng.RandfRange(0f, MathF.Tau), m._rng.RandfRange(0f, MathF.Tau), m._rng.RandfRange(0f, MathF.Tau));
        Dress(m, out m._flame, out m._sound);
        MarkTape(m, () => m._flame.Visible ? 1 : 0);
        parent.AddChild(m);

        Vector3 fwd = -from.Basis.Z.Normalized();
        m.Pos = from.Origin;
        m.Vel = carrierVel + fwd * Boost + Vector3.Down * 2f;
        m._lastTarget = target();
        m.GlobalTransform = new Transform3D(from.Basis.Orthonormalized(), m.Pos);
        m.UpdateGain();
        if (Dbg) GD.Print($"missile #{m._id} launch at ({m.Pos.X:F0},{m.Pos.Y:F0},{m.Pos.Z:F0}) v={m.Vel.Length():F1} m/s, target {m.Pos.DistanceTo(m._lastTarget):F0} m, aim {r:F1} m off");
        return m;
    }

    /// <summary>Advances flight, trail and fuse. Returns false once it has exploded (the node frees itself).</summary>
    public bool Tick(float dt)
    {
        if (!Alive) return false;
        Age += dt;
        Vector3 prev = Pos;

        // Target motion, smoothed, for the lead point. It steers for the aim point off the target.
        Vector3 tp = _target(), prevTp = _lastTarget;
        if (dt > 0)
        {
            Vector3 v0 = _targetVel;
            _targetVel = _targetVel.Lerp((tp - _lastTarget) / dt, Math.Min(1f, dt / TargetVelLag));
            _targetAcc = _targetAcc.Lerp((_targetVel - v0) / dt, Math.Min(1f, dt / TargetAccLag)).LimitLength(MaxTargetAcc);
            // How fast the target's course swings (horizontal), smoothed.
            Vector2 h0 = new(v0.X, v0.Z), h1 = new(_targetVel.X, _targetVel.Z);
            float swing = h0.Length() > 6f && h1.Length() > 6f ? MathF.Abs(h0.AngleTo(h1)) / dt : 0f;
            _turnRate += (swing - _turnRate) * Math.Min(1f, dt / 0.1f);
        }
        _lastTarget = tp;
        Vector3 ap = tp + _aim + new Vector3(MathF.Sin(Age * _wf.X + _wp.X) * Wander, MathF.Sin(Age * _wf.Y + _wp.Y) * Wander * 0.2f,
            MathF.Sin(Age * _wf.Z + _wp.Z) * Wander);
        Vector3 av = _targetVel;
        // Close in, the seeker is blind: it flies on to where the target was going when it got there.
        if (_blindAt < 0 && Age > DropTime && Pos.DistanceTo(tp) < BlindRange)
        {
            _blindAt = Age; _blindPos = ap; _blindVel = _targetVel + _targetAcc * TargetVelLag; _blindAcc = _targetAcc;
            if (Dbg) GD.Print($"missile #{_id} blind at {Pos.DistanceTo(tp):F0} m, aim point {ap.DistanceTo(tp):F1} m off, heading error {(Vel.Normalized().AngleTo((ap - Pos).Normalized())):F2} rad");
        }
        if (_blindAt >= 0)
        {
            float bt = Age - _blindAt;
            ap = _blindPos + _blindVel * bt + _blindAcc * (0.5f * bt * bt);
            av = _blindVel + _blindAcc * bt;
        }

        float speed = Vel.Length();
        Vector3 dir = speed > 0.1f ? Vel / speed : -GlobalTransform.Basis.Z;
        float turn = 0;
        if (Age > DropTime && Locked)
        {
            float dist = Pos.DistanceTo(ap);
            float tgo = Math.Clamp(dist / Math.Max(speed, 30f), 0f, 3f) * LeadFactor;
            Vector3 want = (ap + av * tgo - Pos).Normalized();
            float off = dir.AngleTo((ap - Pos).Normalized());
            if (off < 0.5f) _acquired = true;
            Vector3 r = tp - Pos, vrel = _targetVel - Vel;
            _los = r.Cross(vrel).Length() / Math.Max(r.LengthSquared(), 1f);
            if (_acquired) _maxLos = Math.Max(_maxLos, _los);
            // A hard turn while it closes in breaks the lock (not once it is blind and committed).
            float range = Pos.DistanceTo(tp);
            _hardTurn = _blindAt < 0 && range < HardTurnRange && _turnRate > HardTurnRate ? _hardTurn + dt : 0f;
            if (_acquired && (off > SeekerLimit || _hardTurn >= HardTurnTime))
            {
                Locked = false;
                if (Dbg) GD.Print($"missile #{_id} lost lock at {Age:F2} s, {dist:F0} m, min {MinDist:F1} m{(_hardTurn >= HardTurnTime ? $" (hard turn {_turnRate:F2} rad/s)" : "")}");
            }
            else
            {
                // Heading and climb angle steered separately so a missile turning round to a target
                // behind and below swings round level instead of looping into the ground.
                float h = MathF.Atan2(dir.X, dir.Z), g = MathF.Asin(Math.Clamp(dir.Y, -1f, 1f));
                float hw = MathF.Atan2(want.X, want.Z), gw = MathF.Asin(Math.Clamp(want.Y, -1f, 1f));
                float yawErr = Mathf.AngleDifference(h, hw);
                if (Math.Abs(yawErr) > 0.6f) gw = Math.Max(gw, -0.05f);
                float alt = Pos.Y - _t.MeshHeight(Pos.X, Pos.Z);
                float hd = new Vector2(ap.X - Pos.X, ap.Z - Pos.Z).Length();
                gw = Math.Max(gw, -MathF.Atan2(Math.Max(alt - 0.5f, 0f), Math.Max(hd, 1f)) - 0.05f);   // no diving into the ground short of it
                float rate = _blindAt >= 0 ? TerminalTurnRate : TurnRate;
                float yr = Math.Clamp(yawErr * SteerGain, -rate, rate), pr = Math.Clamp((gw - g) * SteerGain, -rate, rate);
                turn = MathF.Sqrt(yr * yr + pr * pr);
                if (turn > rate) { yr *= rate / turn; pr *= rate / turn; turn = rate; }
                h += yr * dt;
                g = Math.Clamp(g + pr * dt, -1.4f, 1.4f);
                dir = new Vector3(MathF.Cos(g) * MathF.Sin(h), MathF.Sin(g), MathF.Cos(g) * MathF.Cos(h));
            }
        }

        bool burning = Age < BurnTime;
        if (Age < DropTime) Vel += Vector3.Down * 6f * dt;           // falling off the rail
        else if (speed > MaxSpeed) Vel = dir * Math.Max(MaxSpeed, speed - Bleed * dt)
            + (burning ? Vector3.Zero : Vector3.Down * 9.8f * dt);                  // shedding the carrier's speed
        else if (burning) Vel = dir * Math.Min(MaxSpeed, speed + Accel * dt);
        else
        {
            // Coasting: drag and gravity bend the nose down.
            Vel = dir * Math.Max(20f, speed - 10f * dt) + Vector3.Down * 9.8f * dt;
        }
        Pos += Vel * dt;

        // Orient along the flight path, rolling slowly.
        Vector3 f = Vel.Normalized();
        Vector3 up = Math.Abs(f.Y) > 0.98f ? Vector3.Forward : Vector3.Up;
        var b = Basis.LookingAt(f, up) * new Basis(Vector3.Back, Age * 3f);
        GlobalTransform = new Transform3D(b, Pos);
        UpdateGain();

        // Trail and flame.
        _flame.Visible = burning && Age > 0.05f;
        if (_flame.Visible) _flame.Scale = new Vector3(1f, 0.8f + 0.4f * _rng.Randf(), 1f);
        if (burning)
        {
            Vector3 tail = Pos - f * 1.9f;
            float seg = prev.DistanceTo(Pos);
            int puffs = Math.Max(1, (int)(seg / 1.2f));
            for (int k = 0; k < puffs; k++)
            {
                Vector3 p = tail - Vel * dt * (k / (float)puffs);
                _fx.Smoke(p, new Vector3(_rng.RandfRange(-0.4f, 0.4f), _rng.RandfRange(0.1f, 0.6f), _rng.RandfRange(-0.4f, 0.4f)),
                    new Color(0.93f, 0.93f, 0.9f), 0.75f, 0.45f, 2.6f, _rng.RandfRange(2.5f, 3.5f));
            }
            _fx.Burst(tail - f * 0.3f, -f * 8f, _rng.Randf() < 0.5f ? new Color(1f, 0.95f, 0.6f) : new Color(1f, 0.6f, 0.15f),
                1f, 0.55f, 0.15f, 0.12f, 0f, 2f, false);
        }

        // Fuse: closest approach to the target (not the aim point) on this step, relative to it.
        Vector3 r0 = prev - prevTp, rel = Pos - tp - r0;
        float u = rel.LengthSquared() > 1e-6f ? Math.Clamp(-r0.Dot(rel) / rel.LengthSquared(), 0f, 1f) : 1f;
        Vector3 closest = prev + (Pos - prev) * u;
        float d = (r0 + rel * u).Length();
        MinDist = Math.Min(MinDist, d);

        if (Dbg && Age >= _nextLog)
        {
            _nextLog += 0.5f;
            GD.Print($"missile #{_id} t={Age:F1} v={Vel.Length():F1} turn={turn:F2} dist={d:F1} alt={Pos.Y - _t.MeshHeight(Pos.X, Pos.Z):F1} los={_los:F2}{(Locked ? "" : " (no lock)")}");
        }

        // Inside the fuse radius it goes off once the distance stops shrinking (closest approach).
        if (Age > DropTime && d < FuseRadius && u < 1f) { Explode(closest, d, "proximity"); return false; }
        float ground = _t.MeshHeight(Pos.X, Pos.Z);
        if (Pos.Y <= ground + 0.1f) { Explode(new Vector3(Pos.X, ground, Pos.Z), Pos.DistanceTo(tp), "ground"); return false; }
        if (Age > LifeTime) { Explode(Pos, Pos.DistanceTo(tp), "self-destruct"); return false; }
        return true;
    }

    /// <summary>Removes it silently, no explosion (owners being reset).</summary>
    public void Remove()
    {
        Alive = false;
        QueueFree();
    }

    void UpdateGain()
    {
        var cam = GetViewport()?.GetCamera3D();
        if (cam == null || _sound == null) return;
        float d = cam.GlobalPosition.DistanceTo(Pos);
        _sound.Gain = Math.Clamp(40f / (d + 10f), 0.05f, 1f);
    }

    void Explode(Vector3 p, float distToTarget, string why)
    {
        Alive = false;
        if (Dbg) GD.Print($"missile #{_id} {why} at {Age:F2} s ({p.X:F0},{p.Y:F0},{p.Z:F0}), target {distToTarget:F1} m, min {MinDist:F1} m, max LOS rate {_maxLos:F2} rad/s");
        var host = GetParent();
        TapeFx?.Fx(new TapeEvent(0f, TapeEventType.Effect, Kind: TapeFxKind.MissileBurst, Effect: TapeEffect.Explosion, Pos: p, Life: BurstLife));
        ExplodeFx(_fx, _rng, host, host, p);
        Exploded?.Invoke(p, distToTarget);
        QueueFree();
    }

    /// <summary>The warhead goes off: smoke, fireball and debris (<paramref name="fx"/>), the flash (under <paramref name="light"/>) and the bang (under <paramref name="sound"/>); a null parent makes neither. Live play and the title replay both make it here, with the live code's order of draws.</summary>
    internal static void ExplodeFx(TyreEffects fx, RandomNumberGenerator rng, Node light, Node sound, Vector3 p)
    {
        // Smoke first, fireball drawn over it, then debris.
        for (int k = 0; k < 18; k++)
        {
            var at = p + new Vector3(rng.RandfRange(-1.5f, 1.5f), rng.RandfRange(0.5f, 3f), rng.RandfRange(-1.5f, 1.5f));
            var up = new Vector3(rng.RandfRange(-1.5f, 1.5f), rng.RandfRange(2f, 5f), rng.RandfRange(-1.5f, 1.5f));
            fx.Smoke(at, up, new Color(0.12f, 0.12f, 0.12f), 0.8f, 1.5f, 5f, rng.RandfRange(2f, 4f));
        }
        for (int k = 0; k < 40; k++)
        {
            Vector3 v = new Vector3(rng.RandfRange(-1, 1), rng.RandfRange(0f, 1.2f), rng.RandfRange(-1, 1)) * rng.RandfRange(3f, 12f);
            var hot = rng.Randf() < 0.5f ? new Color(1f, 0.85f, 0.35f) : new Color(1f, 0.45f, 0.1f);
            float life = rng.RandfRange(2f, 3.5f), size = rng.RandfRange(0.3f, 0.7f);
            fx.Burst(p + Vector3.Up * 0.8f, v, hot, 1f, life, 0.4f, size, -2f, 2.5f, false);
        }
        for (int k = 0; k < 16; k++)
        {
            var v = new Vector3(rng.RandfRange(-10, 10), rng.RandfRange(5, 14), rng.RandfRange(-10, 10));
            fx.Burst(p + Vector3.Up * 0.5f, v, new Color(0.25f, 0.25f, 0.27f), 1f, 0.3f, 0.25f, 3f, 14f, 0.2f, true);
        }
        if (light != null)
        {
            var flash = new OmniLight3D { LightColor = new Color(1f, 0.75f, 0.35f), LightEnergy = 5f, OmniRange = 30f, Position = p + Vector3.Up * 2f };
            light.AddChild(flash);
            var tw = flash.CreateTween();
            tw.TweenProperty(flash, "light_energy", 0f, 0.4f);
            tw.TweenCallback(Callable.From(flash.QueueFree));
        }
        sound?.AddChild(new ExplosionSound { Position = p, Loudness = 0f });
    }

    /// <summary>The body, the flame and the sound of a missile under <paramref name="host"/>, as play and the replay both build them.</summary>
    internal static void Dress(Node3D host, out MeshInstance3D flame, out MissileSound sound)
    {
        host.AddChild(Model());
        flame = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.03f, BottomRadius = 0.2f, Height = 1.4f, RadialSegments = 6, Rings = 1 },
            MaterialOverride = NightGlow.Keep(new StandardMaterial3D
            {
                AlbedoColor = new Color(1f, 0.9f, 0.55f, 0.95f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            }),
            Position = new Vector3(0, 0, 2.35f),
            RotationDegrees = new Vector3(90, 0, 0),   // wide end at the nozzle
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        host.AddChild(flame);
        sound = new MissileSound { BurnTime = BurnTime };
        host.AddChild(sound);
    }

    // ------------------------------------------------------------ model

    static readonly Color Body = new(0.9f, 0.9f, 0.88f);
    static readonly Color Fin = new(0.7f, 0.71f, 0.72f);
    static readonly Color Seeker = new(0.12f, 0.13f, 0.15f);

    /// <summary>The missile itself, nose towards -Z, ~3.8 m long (a size up on the real 2.8 m so it reads from the chase camera).</summary>
    public static Node3D Model()
    {
        var n = new Node3D { Name = "Missile" };
        n.AddChild(Props.Part(Props.Cyl(0.16f, 3.0f, 8), Body, new Vector3(0, 0, 0.1f), new Vector3(90, 0, 0)));
        n.AddChild(Props.Part(Props.Cyl(0.16f, 0.55f, 8, 0.04f), Seeker, new Vector3(0, 0, -1.67f), new Vector3(-90, 0, 0)));
        n.AddChild(Props.Part(Props.Cyl(0.13f, 0.2f, 8), Seeker, new Vector3(0, 0, 1.68f), new Vector3(90, 0, 0)));   // nozzle
        // Canards up front, tail fins at the back, cruciform.
        n.AddChild(Props.Part(Props.Box(0.75f, 0.04f, 0.22f), Fin, new Vector3(0, 0, -1.05f)));
        n.AddChild(Props.Part(Props.Box(0.04f, 0.75f, 0.22f), Fin, new Vector3(0, 0, -1.05f)));
        n.AddChild(Props.Part(Props.Box(1.15f, 0.04f, 0.5f), Fin, new Vector3(0, 0, 1.3f)));
        n.AddChild(Props.Part(Props.Box(0.04f, 1.15f, 0.5f), Fin, new Vector3(0, 0, 1.3f)));
        return n;
    }

    /// <summary>Wingtip launch rail with a missile under it (named "Missile", hidden once fired).
    /// Origin is at the missile's axis.</summary>
    public static Node3D Rail()
    {
        var n = new Node3D();
        n.AddChild(Props.Part(Props.Box(0.12f, 0.16f, 2.2f), new Color(0.55f, 0.56f, 0.58f), new Vector3(0, 0.26f, 0.1f)));
        n.AddChild(Model());
        return n;
    }
}
