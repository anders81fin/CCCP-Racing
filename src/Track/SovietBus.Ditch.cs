using System;
using System.Collections.Generic;
using CCCPRacing.Audio;
using CCCPRacing.Car;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Track;

/// <summary>
/// The bus and the car's horn. Honk at the Pazik head-on (within <see cref="HearRange"/>, the car
/// in front of it, closing) and the driver yanks it to his right, off the road into the ditch:
/// a scripted swerve of ~1.5 s, stopping tilted, a puff of black smoke, one long blare. After
/// ~8 s he reverses back onto the road and the timetable resumes. The swerve path is checked
/// against the track's fixed obstacles (wooden fences just break): a light knock stops it dented,
/// a hard one (≥ <see cref="FireSpeed"/>) sets it on fire. A burning bus stays as a blackened,
/// smouldering wreck for the rest of the run, its drunk passengers stumble out, and a minute
/// later the kolkhoz sends a spare bus from the depot. A new run resets it all.
/// </summary>
public partial class SovietBus
{
    enum Act { Route, Swerve, Ditched, Reverse, Gone }

    const float LaneOffset = 0.45f;             // our lane: this far out of the half width
    /// <summary>How far the driver hears the car's horn (m).</summary>
    public const float HearRange = 80f;
    const float HearCone = 0.866f;              // cos 30°: the car ahead of the bus
    const float SwerveTime = 1.5f, DitchTime = 8f, ReverseTime = 3.5f;
    /// <summary>Hitting something solid at this speed (m/s) or more sets the bus on fire.</summary>
    public const float FireSpeed = 4f;
    const float SpareDelay = 60f;
    const float DitchRoll = 0.22f, DitchPitch = 0.07f;   // ~12.5° over to the right, nose ~4° down
    const float BodyR = 1.3f;                   // the collision circles
    const float BlazeTime = 45f, FadeTime = 30f, Smoulder = 0.3f;
    static readonly Vector2 Parked = new(-1e4f, -1e4f);
    static readonly Color[] FireCols = { new(1f, 0.92f, 0.5f), new(1f, 0.6f, 0.15f), new(1f, 0.35f, 0.05f) };
    static readonly bool Debug = OS.HasEnvironment("BUSDBG");

    WoodFences _wood;
    int _staticObs;
    readonly int[] _wreckObs = new int[5];
    Node3D _bumper;
    static readonly Vector3 BumperPos = new(0, 0.45f, -Length / 2f - 0.05f);
    readonly RandomNumberGenerator _rng = new();

    Act _act;
    float _actT;
    // The swerve path: from the route pose at _swS0, forward _swLen and right _swLat (a parabola).
    float _swS0, _swLen, _swLat, _swU, _revFrom, _roll, _pitch, _revRoll, _revPitch;
    Vector2 _swP0, _swF0, _swR0;
    readonly List<int> _near = new();

    // The fire.
    Node3D _wreck;
    Transform3D _wreckXf;
    Vector2 _wreckPos, _wreckFwd;
    StandardMaterial3D _burnt;
    FireSound _crackle;
    float _burnT, _fireAcc, _smokeAcc, _spareIn = -1;
    float _dbgT;                                // game time for the debug log
    class Passenger
    {
        public DrunkMen.Figure F;
        public Vector2 Pos, Target;
        public float Delay, Phase, Wait, Yaw, Shout;
        public bool Out;
    }
    readonly List<Passenger> _pax = new();

    /// <summary>The driver has heard the horn and is taking the ditch.</summary>
    public event Action Swerved;
    /// <summary>The bus hit something hard on the way and is burning.</summary>
    public event Action CaughtFire;
    /// <summary>A minute after the fire the spare bus leaves the depot.</summary>
    public event Action SpareSent;
    /// <summary>Wooden fence spans the bus flattened this step (for the crack sound).</summary>
    public int FencesBrokenThisTick { get; private set; }
    /// <summary>Test (--bus-swerve-into=fire|dent|ditch): every run starts with the bus swerving where that happens.</summary>
    public string TestInto;
    /// <summary>What the camera should look at: the wreck while it is the only bus.</summary>
    public Vector2 Focus2D => _wreck != null && _act == Act.Gone ? _wreckPos : Position2D;

    /// <summary>
    /// Distance (m) from p to the burning wreck's body, and the nearest point on it; +inf when
    /// nothing burns.
    /// </summary>
    public float FireDistance(Vector2 p, out Vector2 closest)
    {
        closest = p;
        if (_wreck == null) return float.PositiveInfinity;
        Vector2 right = new(-_wreckFwd.Y, _wreckFwd.X);
        Vector2 rel = p - _wreckPos;
        float along = Math.Clamp(rel.Dot(_wreckFwd), -Length / 2f, Length / 2f);
        float across = Math.Clamp(rel.Dot(right), -Width / 2f, Width / 2f);
        closest = _wreckPos + _wreckFwd * along + right * across;
        return p.DistanceTo(closest);
    }

    /// <summary>Test: the driver hears a horn that is not there.</summary>
    public void ForceSwerve()
    {
        if (_act == Act.Route) StartSwerve();
    }

    /// <summary>The passengers of the burnt bus, yelling (for the HUD).</summary>
    public IEnumerable<(Vector3 pos, string text)> ActiveShouts()
    {
        if (TapeDriven)
        {
            foreach (var child in GetChildren())
            {
                if (child is not Node3D passenger || passenger.IsQueuedForDeletion() || !passenger.Visible ||
                    !passenger.HasMeta(WorldTapeConst.KindMeta) ||
                    !((string)passenger.GetMeta(WorldTapeConst.KindMeta)).StartsWith("bus-passenger/", StringComparison.Ordinal)) continue;
                var shout = passenger.GetNodeOrNull<Node3D>("TapeShout");
                if (shout != null && shout.Visible) yield return (shout.GlobalPosition, "ПОЖАР!");
            }
            yield break;
        }
        foreach (var p in _pax)
            if (p.Out && p.Shout > 0) yield return (p.F.Root.GlobalPosition + Vector3.Up * 3.7f, "ПОЖАР!");
    }

    void ResetDitch()
    {
        _act = Act.Route;
        _actT = 0;
        _roll = _pitch = 0;
        _spareIn = -1;
        if (_wreck != null) _wreck.QueueFree();
        _wreck = null;
        foreach (var p in _pax) { TapeHost?.Gone(p.F.Root); p.F.Root.QueueFree(); }
        _pax.Clear();
        _crackle?.QueueFree();
        _crackle = null;
        _model.Visible = true;
        if (_sound != null) _sound.StreamPaused = false;
        _bumper.Position = BumperPos;
        _bumper.Rotation = Vector3.Zero;
        foreach (int k in _wreckObs) _t.Obstacles[k] = new Obstacle(Parked, 0.1f);
        TapeReset?.Invoke();
    }

    /// <summary>The route part of the tick is skipped while this returns true (the bus is off it).</summary>
    bool DitchTick(float dt, CarSim car, bool horn)
    {
        FencesBrokenThisTick = 0;
        _dbgT += dt;
        if (_wreck != null && dt > 0) Burn(dt);
        switch (_act)
        {
            case Act.Swerve: SwerveTick(dt); return true;
            case Act.Ditched:
                _actT += dt;
                Place(Position2D, Forward2D, Vector2.Zero, dt, _roll, _pitch);
                if (_actT >= DitchTime)
                {
                    _act = Act.Reverse;
                    _actT = 0;
                    (_revFrom, _revRoll, _revPitch) = (_swU, _roll, _pitch);
                    if (Debug) GD.Print($"bus: reversing out of the ditch at {_dbgT:F1} s");
                }
                return true;
            case Act.Reverse: ReverseTick(dt); return true;
            case Act.Gone:
                if ((_spareIn -= dt) > 0) return true;
                // The spare leaves the depot at the usual start of the route.
                _spareIn = -1;
                _act = Act.Route;
                _s = _startS;
                _speed = 0;
                _stopTimer = 0;
                _lastStop = -1;
                _model.Visible = true;
                _sound.StreamPaused = false;
                if (Debug) GD.Print($"bus: the spare bus sets off at {_dbgT:F1} s");
                SpareSent?.Invoke();
                return false;
        }
        return false;
    }

    // ------------------------------------------------------------ the swerve

    /// <summary>The horn: a car ahead, in the ±30° cone, closing, within earshot.</summary>
    void Hear(CarSim car)
    {
        if (car == null || _act != Act.Route || _speed < 1.5f) return;
        Vector2 rel = new Vector2(car.Pos.X, car.Pos.Z) - Position2D;
        float d = rel.Length();
        if (d > HearRange || d < 2f) return;
        Vector2 to = rel / d;
        if (to.Dot(Forward2D) < HearCone) return;
        float closing = (Forward2D * _speed - new Vector2(car.Vel.X, car.Vel.Z)).Dot(to);
        if (closing < 2f) return;
        if (Debug) GD.Print($"bus: hears the horn, car {d:F0} m ahead, closing {closing * 3.6f:F0} km/h");
        StartSwerve();
    }

    void StartSwerve()
    {
        SetupSwerve(_s, _speed);
        _act = Act.Swerve;
        _actT = 0;
        _swU = 0;
        _roll = _pitch = 0;
        _stopTimer = 0;
        _sound.Honk(1.3f);
        Swerved?.Invoke();
    }

    /// <summary>The path from route metre s at speed v: off the road to the right, clear of it.</summary>
    void SetupSwerve(float s, float v)
    {
        _swS0 = s;
        RoutePose(s, out _swP0, out _swF0);
        _swR0 = new Vector2(-_swF0.Y, _swF0.X);
        int n = _t.Center.Length;
        float hw = _t.HalfWidth[(((int)s) % n + n) % n];
        _swLat = hw * (1f - LaneOffset) + Width / 2f + 2f;
        _swLen = Math.Max(5f, v * SwerveTime / 1.6f);
        Vector2 mid = _swP0 + _swF0 * (_swLen * 0.5f) + _swR0 * (_swLat * 0.5f);
        _near.Clear();
        for (int j = 0; j < _staticObs; j++)
            if (_t.Obstacles[j].Center.DistanceTo(mid) < 16f + _t.Obstacles[j].Radius) _near.Add(j);
    }

    void SwervePose(float u, out Vector2 pos, out Vector2 fwd)
    {
        pos = _swP0 + _swF0 * (_swLen * u) + _swR0 * (_swLat * u * u);
        fwd = (_swF0 * _swLen + _swR0 * (2f * _swLat * u)).Normalized();
    }

    /// <summary>Hard over, braking all the way: path progress after t seconds.</summary>
    static float SwerveU(float t) => 1f - MathF.Pow(1f - Math.Min(1f, t / SwerveTime), 1.6f);

    /// <summary>The body circles against the fixed obstacles near the path; impact = closing speed.</summary>
    bool Collide(Vector2 pos, Vector2 fwd, Vector2 vel, out float impact)
    {
        impact = 0;
        bool hit = false;
        for (int k = 0; k < 5; k++)
        {
            Vector2 c = pos + fwd * (-3.2f + k * 1.6f);
            foreach (int j in _near)
            {
                var o = _t.Obstacles[j];
                float d = c.DistanceTo(o.Center);
                if (d >= o.Radius + BodyR) continue;
                Vector2 nrm = d > 1e-3f ? (o.Center - c) / d : fwd;
                float vn = vel.Dot(nrm);
                if (vn < 0.3f) continue;         // grazing past, or already moving away
                hit = true;
                impact = Math.Max(impact, vn);
            }
        }
        return hit;
    }

    void SwerveTick(float dt)
    {
        if (dt <= 0) { Place(Position2D, Forward2D, Vector2.Zero, 0, _roll, _pitch); return; }
        _actT += dt;
        float u = SwerveU(_actT);
        SwervePose(u, out Vector2 pos, out Vector2 fwd);
        Vector2 vel = (pos - Position2D) / dt;
        if (TrafficBlocks(pos, fwd, _trafficTractor, _trafficSlurry))
        {
            _act = Act.Ditched;
            _actT = 0;
            Place(Position2D, Forward2D, Vector2.Zero, dt, _roll, _pitch);
            return;
        }
        if (Collide(pos, fwd, vel, out float impact))
        {
            // Stopped dead against it, where it was a moment ago.
            _act = Act.Ditched;
            _actT = 0;
            Place(Position2D, Forward2D, Vector2.Zero, dt, _roll, _pitch);
            if (Debug) GD.Print($"bus: hits something at {impact * 3.6f:F0} km/h ({impact:F1} m/s) at {_dbgT:F1} s{(impact >= FireSpeed ? " - FIRE" : " - dented")}");
            if (impact >= FireSpeed && _wreck == null) Ignite();
            else
            {
                Dent();
                BlackPuff();
            }
            return;
        }
        if (_wood != null)
            for (int k = 0; k < 5; k++)
                FencesBrokenThisTick += _wood.Smash(pos + fwd * (-3.2f + k * 1.6f), BodyR, new Vector3(vel.X, 0, vel.Y));
        _swU = u;
        float tk = Math.Clamp((u - 0.55f) / 0.45f, 0f, 1f);
        tk = tk * tk * (3f - 2f * tk);
        _roll = -DitchRoll * tk;
        _pitch = -DitchPitch * tk;
        Place(pos, fwd, vel, dt, _roll, _pitch);
        if (_actT < SwerveTime) return;
        _act = Act.Ditched;
        _actT = 0;
        Place(pos, fwd, Vector2.Zero, dt, _roll, _pitch);
        BlackPuff();
        if (Debug) GD.Print($"bus: in the ditch at {_dbgT:F1} s, {_swLat:F1} m right of its lane");
    }

    void ReverseTick(float dt)
    {
        if (dt <= 0) return;
        float previousT = _actT;
        _actT += dt;
        float k = Math.Min(1f, _actT / ReverseTime);
        float u = _revFrom * (1f - k * k * (3f - 2f * k));
        SwervePose(u, out Vector2 pos, out Vector2 fwd);
        if (TrafficBlocks(pos, fwd, _trafficTractor, _trafficSlurry))
        {
            _actT = previousT;
            Place(Position2D, Forward2D, Vector2.Zero, dt, _roll, _pitch);
            return;
        }
        float f = _revFrom > 1e-3f ? u / _revFrom : 0f;
        _roll = _revRoll * f;
        _pitch = _revPitch * f;
        Place(pos, fwd, (pos - Position2D) / dt, dt, _roll, _pitch);
        if (k < 1f) return;
        // Back in its lane: the timetable resumes from where it left the road.
        _act = Act.Route;
        _s = _swS0;
        _speed = 0;
        if (Debug) GD.Print($"bus: back on the road at {_dbgT:F1} s");
    }

    /// <summary>A crumpled bumper hanging askew.</summary>
    void Dent()
    {
        _bumper.Position = BumperPos + new Vector3(0.1f, -0.08f, 0.3f);
        _bumper.Rotation = new Vector3(0, 0.25f, 0.12f);
    }

    /// <summary>The diesel chokes as it stalls: a cloud of black smoke.</summary>
    void BlackPuff()
    {
        Vector3 pipe = _model.GlobalTransform * new Vector3(-0.9f, 0.4f, Length / 2f + 0.1f);
        for (int i = 0; i < 12; i++)
            _fx.Smoke(pipe + new Vector3(_rng.RandfRange(-0.4f, 0.4f), _rng.RandfRange(0f, 0.6f), _rng.RandfRange(-0.4f, 0.4f)),
                new Vector3(_rng.RandfRange(-1f, 1f), _rng.RandfRange(1.5f, 3f), _rng.RandfRange(-1f, 1f)),
                new Color(0.06f, 0.06f, 0.06f), 0.85f, 0.8f, 3.2f, _rng.RandfRange(1.5f, 2.5f));
    }

    // ------------------------------------------------------------ the fire

    /// <summary>
    /// The wreck is this bus's model, left where it stopped; a new model (hidden until the spare
    /// sets off) takes its place, and the collision circles move to the wreck's own slots.
    /// </summary>
    void Ignite()
    {
        // The in-service model is never freed (the world tape holds its nodes for the life of the scene): the wreck is a fresh copy of
        // the body, put where the bus stands with its doors as they are; the in-service model hides below and waits for the spare.
        var (live, liveL, liveR, liveB) = (_model, _doorL, _doorR, _bumper);
        BuildModel();
        _wreck = _model;
        _wreck.GlobalTransform = live.GlobalTransform;
        _doorL.Position = liveL.Position; _doorR.Position = liveR.Position;
        _wreck.Name = "PAZIK wreck";
        _wreckXf = _wreck.GlobalTransform;
        _wreckPos = Position2D;
        _wreckFwd = Forward2D;
        Dent();
        _burnt = CreateBurntOverlay(_wreck);
        for (int k = 0; k < _wreckObs.Length; k++)
            _t.Obstacles[_wreckObs[k]] = new Obstacle(_wreckPos + _wreckFwd * (-3.2f + k * 1.6f), BodyR, Material: ImpactMaterial.Metal);
        foreach (int k in _obs) _t.Obstacles[k] = new Obstacle(Parked, 0.1f);

        (_model, _doorL, _doorR, _bumper) = (live, liveL, liveR, liveB);
        _model.Visible = false;
        _sound.StreamPaused = true;
        Position2D = Parked;

        _crackle = new FireSound { Position = _wreckXf.Origin + Vector3.Up * 1.5f };
        AddChild(_crackle);
        EmitIgnition(_fx, _rng, _wreckXf);

        int n = _rng.RandiRange(2, 3);
        for (int i = 0; i < n; i++)
        {
            Color coat = DrunkMen.RandomCoat(_rng);
            var p = new Passenger { F = DrunkMen.BuildFigure(coat), Delay = 1.2f + i * 1.5f, Phase = _rng.RandfRange(0, 10) };
            MarkPassenger(p.F, coat.ToHtml(false));
            p.F.Root.Visible = false;
            AddChild(p.F.Root);
            TapeHost?.Born(p.F.Root);
            _pax.Add(p);
        }
        _burnT = 0;
        _spareIn = SpareDelay;
        _act = Act.Gone;
        // Dir is the wreck's Euler rotation; Velocity holds the left door and Param the right door's Z.
        // Both doors share X/Y. The event remains alive with the wreck for the rest of the run.
        TapeFx?.Fx(new TapeEvent(0, TapeEventType.Effect, Kind: TapeFxKind.BusFire, Effect: TapeEffect.Explosion,
            Pos: _wreckXf.Origin, Dir: _wreckXf.Basis.GetEuler(), Velocity: liveL.Position, Param: liveR.Position.Z, Life: float.MaxValue));
        CaughtFire?.Invoke();
    }

    static void SetOverlay(Node n, Material m)
    {
        if (n is GeometryInstance3D g) g.MaterialOverlay = m;
        foreach (Node c in n.GetChildren()) SetOverlay(c, m);
    }

    /// <summary>Flames out of the windows and roof, a column of black smoke, the body charring; dying down to a smoulder.</summary>
    void Burn(float dt)
    {
        _burnT += dt;
        SetFireAge(_burnt, _crackle, _burnT);
        EmitFire(_fx, _rng, _wreckXf, _burnT, dt, ref _fireAcc, ref _smokeAcc);
        foreach (var p in _pax) Stumble(p, dt);
    }

    /// <summary>A passenger: out of the door one after another, then wandering about near the wreck.</summary>
    void Stumble(Passenger p, float dt)
    {
        if (!p.Out)
        {
            if ((p.Delay -= dt) > 0) return;
            Vector3 door = _wreckXf * new Vector3(Width / 2f + 0.5f, 0, -2.8f + 4.7f * (_pax.IndexOf(p) % 2));
            p.Pos = new Vector2(door.X, door.Z);
            p.Target = WanderSpot(p.Pos);
            p.Out = true;
            p.Shout = 4f;
            p.F.Root.Visible = true;
        }
        p.Shout -= dt;
        Vector2 to = p.Target - p.Pos;
        bool walking = to.Length() > 0.3f;
        if (walking)
        {
            // Unsteady: the heading wobbles as he goes.
            Vector2 dir = to.Normalized().Rotated(MathF.Sin(p.Phase * 0.8f) * 0.5f);
            p.Pos += dir * 0.9f * dt;
            p.Yaw = Mathf.LerpAngle(p.Yaw, TrackData.YawFor(dir), Math.Min(1f, dt * 4f));
            p.Phase += dt * 5f;
        }
        else if ((p.Wait -= dt) <= 0)
        {
            p.Target = WanderSpot(_wreckPos);
            p.Wait = _rng.RandfRange(2f, 5f);
        }
        else p.Phase += dt * 1.5f;
        float y = _t.MeshHeight(p.Pos.X, p.Pos.Y);
        p.F.Root.Position = new Vector3(p.Pos.X, y, p.Pos.Y);
        p.F.Root.Rotation = new Vector3(0, p.Yaw, 0);
        float s = MathF.Sin(p.Phase);
        p.F.Body.Rotation = new Vector3(MathF.Sin(p.Phase * 0.7f) * 0.12f, 0, MathF.Sin(p.Phase * 0.45f) * 0.2f);
        p.F.LegL.Rotation = new Vector3(walking ? s * 0.5f : 0, 0, 0);
        p.F.LegR.Rotation = new Vector3(walking ? -s * 0.5f : 0, 0, 0);
        // Arm up waving the bottle while he yells, otherwise it swings (or goes to the mouth).
        p.F.ArmR.Rotation = p.Shout > 0 ? new Vector3(2.8f + MathF.Sin(_burnT * 14f) * 0.25f, 0, 0.3f)
                          : new Vector3(walking ? s * 0.4f : 2.2f * Math.Max(0, MathF.Sin(p.Phase * 0.3f)), 0, 0);
        p.F.Root.GetNode<Node3D>("TapeShout").Visible = p.Shout > 0;
    }

    /// <summary>A walkable spot 5-10 m from the wreck (or from where he is), clear of walls and trees.</summary>
    Vector2 WanderSpot(Vector2 from)
    {
        for (int tries = 0; tries < 10; tries++)
        {
            float a = _rng.RandfRange(0, MathF.Tau), r = _rng.RandfRange(5f, 10f);
            Vector2 p = _wreckPos + new Vector2(MathF.Cos(a), MathF.Sin(a)) * r;
            if (MathF.Abs(_t.MeshHeight(p.X, p.Y) - _t.MeshHeight(from.X, from.Y)) > 2f) continue;
            bool blocked = false;
            foreach (int j in _near)
                if (p.DistanceTo(_t.Obstacles[j].Center) < _t.Obstacles[j].Radius + 1f) { blocked = true; break; }
            if (!blocked && FireDistance(p, out _) > 3f) return p;
        }
        return from;
    }

    // ------------------------------------------------------------ test

    /// <summary>
    /// --bus-swerve-into: put the bus on the first stretch of its route (from its start, the way
    /// it runs) where a swerve at cruising speed ends as asked, and swerve at once.
    /// </summary>
    void TestPlace()
    {
        if (TestInto == null) return;
        int n = _t.Center.Length;
        for (int k = 0; k < n; k++)
        {
            float s = ((_startS - k) % n + n) % n;
            SetupSwerve(s, Cruise);
            float impact = 0, u0 = 0;
            Vector2 prev = _swP0;
            for (float t = 1f / 60f; t <= SwerveTime + 1e-4f; t += 1f / 60f)
            {
                float u = SwerveU(t);
                SwervePose(u, out Vector2 pos, out Vector2 fwd);
                if (Collide(pos, fwd, (pos - prev) * 60f, out impact)) break;
                prev = pos;
                u0 = u;
            }
            bool ok = TestInto switch
            {
                "fire" => impact >= FireSpeed + 0.5f,
                "dent" => impact > 0.5f && impact < FireSpeed - 0.5f,
                _ => impact == 0 && u0 > 0.99f,
            };
            if (!ok) continue;
            if (Debug) GD.Print($"bus test: swerve into {TestInto} from route metre {s:F0} (impact {impact:F1} m/s)");
            _s = s;
            _speed = Cruise;
            RoutePose(s, out Vector2 p0, out Vector2 f0);
            Place(p0, f0, f0 * _speed, 0, 0, 0);
            StartSwerve();
            return;
        }
        GD.Print($"bus test: no place on the route to swerve into {TestInto}");
    }
}
