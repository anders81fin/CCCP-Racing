using System;
using System.Collections.Generic;
using CCCPRacing.Audio;
using CCCPRacing.Car;
using Godot;

namespace CCCPRacing.Track;

/// <summary>
/// Mission failed: an olive ZIL-131 with a canvas-covered bed, soldiers in ushankas on the benches
/// under the tarp, comes down the road, overtakes the car and stops on the road ahead of it, its
/// tail to the car. An officer in a peaked cap gets out of the passenger door and orders the driver
/// out at gunpoint. The driver refuses; one pistol shot breaks a headlamp before he surrenders.
/// The driver, in blue overalls and a white rally helmet,
/// walks with his hands up round to the back of the truck, the officer behind him with the pistol,
/// the tailgate drops (В КУЗОВ!) and he climbs into the dark bed from behind. The officer walks back
/// to the cab, the door shuts and the truck drives off to Kolyma.
/// Kinematic: it follows a path along the road centre line, so it never gets stuck.
/// </summary>
public partial class GulagTruck : Node3D
{
    public enum Phase { Idle, Approach, Board, Leave, Gone }

    const float TruckScale = 1.25f;            // exaggerated to read from the chase camera
    const float ApproachFrom = 46f;       // metres of road behind the car
    const float Beside = 3.4f;            // lateral gap car centre - truck centre while it overtakes
    const float StopAhead = 16f;          // road metres from the car to the truck's centre when it stops (its tail ~8 m off the car's nose)
    // On the road it keeps to its own (right) lane like the ploughs, and never drives through other
    // traffic: it brakes behind any vehicle in its way (YieldSpeed).
    const float LaneShare = 0.42f, HalfWidth = 1.2f * TruckScale, Nose = 3.4f * TruckScale, Gap = 2.5f;
    Vector2[] _seenAt;
    float[] _movedAt;
    /// <summary>The truck out on the roads (approaching, boarding, leaving); null: none.</summary>
    static GulagTruck _onRoad;
    /// <summary>The truck that was on the road when this one took its place (another world's: the replay viewer's second scene); back when this one leaves the tree.</summary>
    GulagTruck _shadowed;

    public override void _ExitTree()
    {
        // Static pointer: never left dangling into a freed world, and a truck it shadowed (the live one, while a viewer's ends) takes it back.
        if (_onRoad == this) _onRoad = IsInstanceValid(_shadowed) && _shadowed.IsInsideTree() ? _shadowed : null;
        _shadowed = null;
    }
    /// <summary>Its body for other traffic: three circles along it (centre offsets from the middle, radius).</summary>
    static readonly float[] BodyAt = { -2.6f * TruckScale / 1.25f, 0f, 2.6f * TruckScale / 1.25f };
    const float BodyR = 1.55f;

    /// <summary>Whether a body (a circle at pos) would overlap the truck: other vehicles stop short of it.</summary>
    public static bool BlocksAt(Vector2 pos, float radius)
    {
        if (_onRoad is not { Active: true } g) return false;
        foreach (var c in g.BodyCentres())
            if (pos.DistanceSquaredTo(c) < MathF.Pow(radius + BodyR + 0.1f, 2)) return true;
        return false;
    }

    /// <summary>The truck's body circles where it stands now (empty when it is not on the road).</summary>
    public static IEnumerable<Vector2> Body => _onRoad is { Active: true } g ? g.BodyCentres() : Array.Empty<Vector2>();
    public const float BodyRadius = BodyR;

    IEnumerable<Vector2> BodyCentres()
    {
        var fwd = new Vector2(-MathF.Sin(_yaw), -MathF.Cos(_yaw));
        foreach (float a in BodyAt) yield return _pos + fwd * a;
    }
    const float TopSpeed = 12f;
    const float WalkSpeed = 2.2f;          // the driver, hands up; the officer keeps up with him
    const float LeaveFor = 5.5f;           // seconds of driving away before it is Gone

    static readonly Color Olive = new(0.33f, 0.37f, 0.2f);
    static readonly Color OliveDark = new(0.25f, 0.28f, 0.15f);
    static readonly Color Greatcoat = new(0.36f, 0.34f, 0.24f);
    static readonly Color Fur = new(0.42f, 0.36f, 0.28f);
    static readonly Color Skin = new(0.86f, 0.66f, 0.56f);
    static readonly Color Black = new(0.08f, 0.08f, 0.08f);
    static readonly Color Glass = new(0.5f, 0.6f, 0.65f);
    static readonly Color Red = new(0.8f, 0.1f, 0.08f);

    TrackData _t;
    TyreEffects _fx;
    Node3D _model, _tailgate, _driver, _legL, _legR, _armL, _armR, _sergeant, _sergeantArm;
    // The officer: out of the passenger door with a pistol, back into the cab at the end.
    Node3D _door, _officer, _offLegL, _offLegR, _gunArm;
    enum Step { DoorOpen, OfficerOut, Order, WarningShot, Surrender, March, Climb, OfficerBack, DoorClose }
    Step _step;
    float _stepT, _shoutT, _marchTime;
    CarSim _arrestCar;
    Node3D _muzzleFlash, _bulletTrace;
    float _shotAge = 10;
    public int WarningShots { get; private set; }
    public bool Refusing => _phase == Phase.Board && _step == Step.Order && _stepT > 1.2f;
    public bool PrisonerOutside => _driver.Visible;
    public event Action<CarSim> WarningShot;
    public Vector3 AimTarget => _step == Step.WarningShot ? _arrestCar.Pos + _arrestCar.Rot * new Vector3(-0.585f, 0.03f, -1.955f)
        : _driver.Visible ? _driver.GlobalPosition + Vector3.Up * 1.65f : _seatedPlayer;
    string _shout;
    Vector3 _seatedPlayer;
    Vector3 _cabSide, _doorOut, _corner, _guard, _drvFrom, _rear, _rearGuard, _offFrom;
    readonly List<Node3D> _soldiers = new();
    DieselSound _sound;
    readonly RandomNumberGenerator _rng = new() { Seed = 1937 };

    readonly List<Vector2> _path = new();
    float _s, _pathLen, _speed, _yaw, _timer, _t0, _puff, _soot;
    Vector2 _pos;
    Vector3 _walkFrom, _walkTo;
    int _carIndex;
    Phase _phase, _dbgPhase;

    public Phase State => _phase;
    public bool Active => _phase is Phase.Approach or Phase.Board or Phase.Leave;
    public bool Gone => _phase == Phase.Gone;
    public bool Leaving => _phase == Phase.Leave;
    public bool Boarding => _phase == Phase.Board;
    public float LeaveTime => _phase == Phase.Leave ? _timer : 0f;
    public Vector3 Position3D => _model.GlobalPosition;
    public Vector3 Velocity => new Vector3(-MathF.Sin(_yaw), 0, -MathF.Cos(_yaw)) * _speed;
    /// <summary>The truck's model (for the night's headlamps, Race.DayNight); visible only while it is out.</summary>
    public Node3D Model => _model;
    /// <summary>Its headlamps and the beam's origin in the model's frame (Race.DayNight lights them after dark).</summary>
    public static readonly Vector3[] Headlamps = { new Vector3(-0.95f, 1.55f, -3.32f) * TruckScale, new Vector3(0.95f, 1.55f, -3.32f) * TruckScale };
    public static readonly Vector3 BeamAt = new Vector3(0, 1.6f, -3.5f) * TruckScale;

    public void Init(TrackData t, TyreEffects fx)
    {
        _t = t;
        _fx = fx;
        _model = new Node3D { Name = "Zil131", Visible = false };
        AddChild(_model);
        var body = new Node3D { Scale = Vector3.One * TruckScale };
        _model.AddChild(body);
        BuildTruck(body);
        BuildTruckDetails(body);
        _driver = BuildDriver();
        AddChild(_driver);
        _driver.Visible = false;
        _officer = BuildOfficer();
        AddChild(_officer);
        _officer.Visible = false;
        _muzzleFlash = Props.Part(Props.Ball(0.22f, 6), new Color(1, 0.8f, 0.18f), new Vector3(0, -0.80f, -0.03f));
        ((StandardMaterial3D)((MeshInstance3D)_muzzleFlash).MaterialOverride).ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        _gunArm.AddChild(_muzzleFlash);
        _muzzleFlash.Visible = false;
        _bulletTrace = Props.Part(Props.Box(0.025f, 0.025f, 1), new Color(1, 0.85f, 0.4f), Vector3.Zero);
        ((StandardMaterial3D)((MeshInstance3D)_bulletTrace).MaterialOverride).ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        AddChild(_bulletTrace);
        _bulletTrace.Visible = false;
        _sound = new DieselSound { BaseRate = 11f, RatePerSpeed = 3f, Body = 0.05f };
        _model.AddChild(_sound);
    }

    public void Reset()
    {
        _phase = Phase.Idle;
        _muzzleFlash.Visible = _bulletTrace.Visible = false;
        WarningShots = 0;
        _model.Visible = false;
        _driver.Visible = false;
        if (_driver.GetParent() != this) _driver.Reparent(this, false);
        _officer.Visible = false;
        _door.Rotation = Vector3.Zero;
        _shoutT = 0;
        _sound.Speed = 0;
        _sound.Dozing = true;
        _speed = 0;
    }

    /// <summary>Send the truck for the car where it stands.</summary>
    public void Call(CarSim car)
    {
        _arrestCar = car;
        WarningShots = 0;
        _shotAge = 10;
        Vector2 c = new(car.Pos.X, car.Pos.Z);
        int n = _t.Center.Length;
        _carIndex = _t.NearestCenter(c, out _);
        Vector2 dir = _t.CenterDir(_carIndex);
        Vector2 right = new(-dir.Y, dir.X);
        // Park on the side away from the chase camera (it looks north), so the tall truck never
        // hides the car; off the road if need be, a ZIL-131 goes anywhere.
        Vector2 lane = right * (right.Y > 0 ? -Beside : Beside);

        _path.Clear();
        for (int k = (int)ApproachFrom; k >= 16; k -= 3)
            _path.Add(InLane((_carIndex - k + n) % n));
        // Off the road past the car, if the ground there is free of anything solid (the labour camp's huts, towers and wire, a building,
        // a wreck): a ZIL-131 goes anywhere, but not over a barracks. Otherwise it keeps to its lane and stops on the road short of the car.
        Vector2 detourA = c + lane - dir * 9f, detourB = c + lane + dir * 4f, backOn = InLane((_carIndex + 10) % n);
        if (DetourClear(_path[^1], detourA, detourB, backOn))
        {
            _path.Add(detourA);      // past the car on that side...
            _path.Add(detourB);
            for (int k = 10; k <= (int)StopAhead; k += 3)      // ...and back onto the road ahead of it
                _path.Add(InLane((_carIndex + k) % n));
        }
        else
            for (int k = -13; k <= (int)StopAhead; k += 3) _path.Add(InLane((_carIndex + k + n) % n));
        _path.Add(InLane((_carIndex + (int)StopAhead) % n));
        _seenAt = null;
        if (_onRoad != this) _shadowed = _onRoad;
        _onRoad = this;
        PathLength();
        _s = 0;
        _pos = _path[0];
        _yaw = TrackData.YawFor((_path[1] - _path[0]).Normalized());
        _speed = TopSpeed * 0.8f;
        _phase = Phase.Approach;
        _timer = _t0 = 0;
        _model.Visible = true;
        _tailgate.Rotation = Vector3.Zero;
        _door.Rotation = Vector3.Zero;
        _officer.Visible = false;
        _sound.Dozing = false;
        _sound.Honk();
        Place();
    }

    /// <summary>The off-road detour (from the last road point through a and b to the road again) keeps clear of every solid, standing obstacle: the truck's body is about 1.6 m across.</summary>
    bool DetourClear(Vector2 from, Vector2 a, Vector2 b, Vector2 backOn)
    {
        var line = new[] { from, a, b, backOn };
        foreach (var o in _t.Obstacles)
        {
            if (o.Radius < .9f || o.Velocity != Vector2.Zero || o.Center.X < -1000f) continue;
            for (int i = 0; i + 1 < line.Length; i++)
            {
                var seg = line[i + 1] - line[i];
                float u = Math.Clamp((o.Center - line[i]).Dot(seg) / Math.Max(.01f, seg.LengthSquared()), 0f, 1f);
                if (o.Center.DistanceTo(line[i] + seg * u) < o.Radius + HalfWidth + 1.3f) return false;
            }
        }
        return true;
    }

    public void Tick(float dt, CarSim car)
    {
        if (_phase is Phase.Idle or Phase.Gone) return;
        _shotAge += dt;
        _muzzleFlash.Visible = _shotAge < 0.10f;
        _bulletTrace.Visible = _shotAge < 0.055f;
        _t0 += dt;
        _timer += dt;
        switch (_phase)
        {
            case Phase.Approach:
            {
                float left = _pathLen - _s;
                float target = Math.Clamp(MathF.Sqrt(2f * 3f * Math.Max(0, left)), 0.8f, TopSpeed);
                float yield = YieldSpeed();
                if (yield < target) { target = yield; _timer -= dt; }      // waiting for traffic is not the give-up time
                _speed = Mathf.MoveToward(_speed, target, (yield < _speed ? 9f : 5f) * dt);
                Drive(dt);
                if (left < 0.1f || _timer > 20f)
                {
                    _speed = 0;
                    _phase = Phase.Board;
                    _timer = 0;
                    _sound.Honk();
                    Place();
                    StartBoarding(car);
                }
                break;
            }
            case Phase.Board:
                Board(dt);
                break;
            case Phase.Leave:
                float go = Math.Min(TopSpeed, YieldSpeed());
                _speed = Mathf.MoveToward(_speed, go, (go < _speed ? 9f : 2.5f) * dt);
                Drive(dt);
                if (_timer > LeaveFor || _s >= _pathLen) { _phase = Phase.Gone; _sound.Speed = 0; }
                break;
        }
        if (OS.HasEnvironment("GULAGDBG") && _phase != _dbgPhase) { GD.Print($"{Time.GetTicksMsec() / 1000f:F2}s truck {_dbgPhase} -> {_phase} at ({_pos.X:F1},{_pos.Y:F1}), car ({car.Pos.X:F1},{car.Pos.Z:F1})"); _dbgPhase = _phase; }
        if (_phase == Phase.Gone) return;
        Place();
        Animate(dt);
        Exhaust(dt);
        _sound.Speed = Math.Abs(_speed);
    }

    /// <summary>The truck has stopped ahead of the car: where everybody will walk.</summary>
    void StartBoarding(CarSim car)
    {
        var m = _model.GlobalTransform;
        float ts = TruckScale;
        _cabSide = Ground(m * new Vector3(1.0f * ts, 0, -1.0f * ts));
        _doorOut = Ground(m * new Vector3(2.1f * ts, 0, -1.0f * ts));
        _rear = Ground(m * new Vector3(0, 0, 4.9f * ts));                  // a step behind the tailgate
        _corner = Ground(m * new Vector3(2.1f * ts, 0, 5.3f * ts));         // round the right-hand rear corner, never through the truck
        _rearGuard = Ground(m * new Vector3(2.4f * ts, 0, 6.2f * ts));      // behind and to the side: the pistol on him
        Vector3 forward = (car.Rot.Z with { Y = 0 }).Normalized();
        _carBlock = new WalkBlock(new Transform3D(Basis.LookingAt(-forward, Vector3.Up), car.Pos),
            new(-1.5f, -2.7f), new(1.5f, 2.7f));
        // The driver's door (left: a Pyatyorka is left-hand drive).
        _drvFrom = Ground(car.Pos - car.Rot.X * 1.7f - car.Rot.Z * 0.2f);
        _seatedPlayer = car.Pos - car.Rot.X * 0.4f - car.Rot.Z * 0.2f + Vector3.Up * 0.45f;
        // The officer stands off the driver's door, between it and the truck.
        Vector3 toTruck = (_rear - _drvFrom) with { Y = 0 };
        _guard = Ground(_drvFrom + (toTruck.LengthSquared() > 0.01f ? toTruck.Normalized() : -car.Rot.Z) * 3.4f - car.Rot.X * 1.5f);
        _guard = Ground(m * Outside(m.AffineInverse() * _guard));
        var blocks = WalkBlocks(false);
        foreach (var block in blocks) _guard = Ground(block.OutsidePoint(_guard));
        _outRoute = WalkingRoute(_doorOut, _guard, blocks);
        _driverRoute = WalkingRoute(_drvFrom, _rear, blocks);
        _step = Step.DoorOpen;
        _stepT = 0;
        _walkFrom = _drvFrom;
        _walkTo = _rear;
        _driver.Visible = false;
        _armL.Rotation = new Vector3(2.4f, 0, -0.22f);
        _armR.Rotation = new Vector3(2.4f, 0, 0.22f);       // hands up
    }

    void Next(Step s)
    {
        _step = s;
        _stepT = 0;
        if (s == Step.March) _marchTime = 0;
    }

    void Shout(string text, float time = 2.2f)
    {
        _shout = text;
        _shoutT = time;
    }

    /// <summary>The officer's orders, over his cap.</summary>
    public IEnumerable<(Vector3 pos, string text)> ActiveShouts()
    {
        if (_phase == Phase.Board && _step == Step.Order && _stepT > 1.2f)
            yield return (_seatedPlayer + Vector3.Up * 2.5f, "НЕТ!");
        if (_shoutT > 0 && _officer.Visible) yield return (_officer.GlobalPosition + Vector3.Up * 3.6f, _shout);
    }

    /// <summary>A point in the truck's frame (metres) pushed out of its footprint (and a step round it) to the right-hand side.</summary>
    static Vector3 Outside(Vector3 local)
    {
        const float halfW = 1.25f * TruckScale + 0.9f, back = 4.1f * TruckScale + 0.9f, front = -3.5f * TruckScale - 0.9f;
        if (local.Z > front && local.Z < back && MathF.Abs(local.X) < halfW) local.X = halfW;
        return local with { Y = 0 };
    }

    /// <summary>Walks who along the points at speed (as Walk, leg by leg). True when at the last one.</summary>
    bool WalkPath(Node3D who, Node3D legL, Node3D legR, Vector3[] pts, float t, float speed, Vector3? face = null)
    {
        for (int i = 1; i < pts.Length; i++)
        {
            float leg = pts[i - 1].DistanceTo(pts[i]) / speed;
            if (t <= leg || i == pts.Length - 1) return Walk(who, legL, legR, pts[i - 1], pts[i], t, speed, face) && i == pts.Length - 1;
            t -= leg;
        }
        return false;
    }

    /// <summary>The officer keeps about three metres off the driver's right and behind him (never in the truck), the pistol on him. True when in place.</summary>
    bool Escort(float dt)
    {
        var m = _model.GlobalTransform;
        Vector3 dl = m.AffineInverse() * _driver.GlobalPosition;
        Vector3 want = Ground(m * Outside(dl + new Vector3(2.6f, 0, 1.6f)));
        Vector3 at = _officer.GlobalPosition;
        var blocks = WalkBlocks(true);
        var route = WalkingRoute(at, want, blocks);
        Vector3 next = route.Length > 1 ? route[1] : at;
        Vector3 d = (next - at) with { Y = 0 };
        float step = Math.Min(d.Length(), WalkSpeed * 1.4f * dt);
        if (step > 0.001f) _officer.GlobalPosition = Ground(at + d.Normalized() * step);
        Face(_officer, _driver.GlobalPosition, at);
        float swing = step > 0.01f ? MathF.Sin(_t0 * 9f) * 0.5f : 0f;
        _offLegL.Rotation = new Vector3(swing, 0, 0);
        _offLegR.Rotation = new Vector3(-swing, 0, 0);
        return ((want - _officer.GlobalPosition) with { Y = 0 }).Length() < 0.4f;
    }

    /// <summary>Walks who from a to b at speed: placed, facing the way (or face, if given), legs swinging. True when there.</summary>
    bool Walk(Node3D who, Node3D legL, Node3D legR, Vector3 a, Vector3 b, float t, float speed, Vector3? face = null)
    {
        float d = a.DistanceTo(b);
        float u = d < 0.01f ? 1f : Math.Clamp(t * speed / d, 0f, 1f);
        who.GlobalPosition = Ground(a.Lerp(b, u));
        Face(who, face ?? b, a);
        float swing = u < 1f ? MathF.Sin(t * 9f) * 0.5f : 0f;
        legL.Rotation = new Vector3(swing, 0, 0);
        legR.Rotation = new Vector3(-swing, 0, 0);
        return u >= 1f;
    }

    static void Face(Node3D who, Vector3 target, Vector3 fallbackFrom)
    {
        Vector3 d = (target - who.GlobalPosition) with { Y = 0 };
        if (d.LengthSquared() < 0.01f) d = (target - fallbackFrom) with { Y = 0 };
        if (d.LengthSquared() > 0.0001f) who.GlobalRotation = new Vector3(0, MathF.Atan2(-d.X, -d.Z), 0);
    }

    /// <summary>The arm and barrel track the player's chest, including while walking and climbing.</summary>
    void Aim(bool up)
    {
        if (!up) { _gunArm.Rotation = new Vector3(0.15f, 0, 0); return; }
        Vector3 target = AimTarget;
        Face(_officer, target, _guard);
        Vector3 direction = _officer.GlobalBasis.Inverse() * (target - _gunArm.GlobalPosition);
        if (direction.LengthSquared() > 0.0001f)
            _gunArm.Basis = Basis.LookingAt(direction.Normalized(), Vector3.Up)
                * Basis.FromEuler(new Vector3(MathF.PI / 2f, 0, 0));
    }

    /// <summary>The arrest at gunpoint (see the class summary), step by step.</summary>
    void Board(float dt)
    {
        _stepT += dt;
        _shoutT -= dt;
        float tail = 0f;
        Vector3 driverAt = _driver.Visible ? _driver.GlobalPosition : _drvFrom;
        switch (_step)
        {
            case Step.DoorOpen:
                _door.Rotation = new Vector3(0, 1.25f * Math.Clamp(_stepT / 0.5f, 0f, 1f), 0);
                if (_stepT > 0.6f) { _officer.GlobalPosition = _cabSide; _officer.Visible = true; Next(Step.OfficerOut); }
                break;
            case Step.OfficerOut:
            {
                // Down from the cab, along the truck's right-hand side and round its back to stand by the car's door.
                float exitTime = _cabSide.DistanceTo(_doorOut) / (WalkSpeed * 1.3f);
                bool arrived = false;
                if (_stepT < exitTime) Walk(_officer, _offLegL, _offLegR, _cabSide, _doorOut, _stepT, WalkSpeed * 1.3f);
                else arrived = WalkPath(_officer, _offLegL, _offLegR, _outRoute, _stepT - exitTime, WalkSpeed * 1.3f);
                if (arrived)
                {
                    Face(_officer, _drvFrom, _doorOut);
                    Shout("ВЫХОДИ!");
                    Next(Step.Order);
                }
                Aim(_stepT > 1f);
                break;
            }
            case Step.Order:
                Face(_officer, _drvFrom, _guard);
                Aim(true);
                if (_stepT > 3.0f) { Shout("ПОСЛЕДНИЙ РАЗ!", 1.5f); Next(Step.WarningShot); Aim(true); }
                break;
            case Step.WarningShot:
                Aim(true);
                if (_stepT >= 0.55f && _stepT - dt < 0.55f)
                {
                    _shotAge = 0;
                    WarningShots++;
                    _muzzleFlash.Visible = _bulletTrace.Visible = true;
                    Vector3 muzzle = _muzzleFlash.GlobalPosition;
                    Vector3 line = AimTarget - muzzle;
                    _bulletTrace.GlobalTransform = new Transform3D(Basis.LookingAt(line.Normalized(), Vector3.Up).Scaled(new Vector3(1, 1, line.Length())), muzzle + line * 0.5f);
                    var report = new PistolSound(); AddChild(report); report.GlobalPosition = muzzle; report.Play();
                    WarningShot?.Invoke(_arrestCar);
                    if (OS.HasEnvironment("GULAGDBG")) GD.Print("gulag: warning shot breaks the left headlamp; driver still seated");
                }
                if (_stepT > 1.5f) { Shout("ВЫХОДИ!"); Next(Step.Surrender); Aim(true); }
                break;
            case Step.Surrender:
                if (_stepT > 0.7f && !_driver.Visible)
                {
                    _driver.Visible = true;
                    _driver.GlobalPosition = _drvFrom;
                    Face(_driver, _rear, _drvFrom);
                    Shout("РУКИ ВВЕРХ!");
                }
                Aim(true);
                if (_stepT > 2.0f) Next(Step.March);
                break;
            case Step.March:
            {
                // The driver round to the tailgate, hands up; the officer a few steps behind, pistol on him.
                Vector3 previous = _driver.GlobalPosition;
                _marchTime += dt;
                bool there = WalkPath(_driver, _legL, _legR, _driverRoute, _marchTime, WalkSpeed);
                // Let the escort clear the way instead of marching through him. The clock for
                // this walk pauses too, so resuming cannot teleport the prisoner forward.
                if (((_driver.GlobalPosition - _officer.GlobalPosition) with { Y = 0 }).Length() < 1.7f)
                {
                    _driver.GlobalPosition = previous;
                    _marchTime -= dt;
                    there = false;
                }
                bool placed = Escort(dt);
                Aim(true);
                if (there && (placed || _stepT > _drvFrom.DistanceTo(_rear) / WalkSpeed + 2f)) { Face(_driver, _driver.GlobalPosition + (_rear - _drvFrom), _drvFrom); Shout("В КУЗОВ!"); Next(Step.Climb); }
                break;
            }
            case Step.Climb:
            {
                Face(_officer, driverAt, _rearGuard);
                tail = Math.Clamp(_stepT / 0.6f, 0f, 1f);
                float t = _stepT - 0.6f;
                if (t >= 0 && t < 0.9f)
                {
                    // Up over the tailgate from behind, the sergeant's hand on his collar.
                    if (_driver.GetParent() != _model) _driver.Reparent(_model, true);
                    float u = t / 0.9f;
                    // Ducking under the canvas (he is taller than the opening), then into the dark.
                    Vector3 from = _model.ToLocal(_rear), to = new(0, 1.62f * TruckScale, 3.4f * TruckScale);
                    _driver.Position = from.Lerp(to, u) + Vector3.Up * MathF.Sin(u * MathF.PI) * 0.35f;
                    _driver.Rotation = new Vector3(-0.7f * Math.Clamp(u * 2f, 0f, 1f), 0, 0);
                    _legL.Rotation = _legR.Rotation = new Vector3(-0.6f * MathF.Sin(u * MathF.PI), 0, 0);
                }
                else if (t >= 0.9f)
                {
                    _driver.Visible = false;
                    tail = 1f - Math.Clamp((t - 0.9f) / 0.5f, 0f, 1f);
                    if (t > 1.5f) Next(Step.OfficerBack);
                }
                Aim(_driver.Visible);
                _sergeantArm.Rotation = new Vector3(tail * -1.2f + MathF.Sin(_t0 * 6f) * 0.3f * tail, 0, 0);
                break;
            }
            case Step.OfficerBack:
                Aim(false);
                if (_stepT <= dt)
                {
                    _offFrom = _officer.GlobalPosition;
                    var route = new List<Vector3>(WalkingRoute(_offFrom, _doorOut, WalkBlocks(false)));
                    if (route.Count > 1) route.Add(_cabSide); // Only boarding the cab may enter its footprint.
                    _backRoute = route.ToArray();
                }
                if (WalkPath(_officer, _offLegL, _offLegR, _backRoute, _stepT, WalkSpeed * 1.3f))
                {
                    _officer.Visible = false;
                    Next(Step.DoorClose);
                }
                break;
            case Step.DoorClose:
                _door.Rotation = new Vector3(0, 1.25f * (1f - Math.Clamp(_stepT / 0.4f, 0f, 1f)), 0);
                if (_stepT > 0.6f)
                {
                    _phase = Phase.Leave;
                    _timer = 0;
                    _sound.Honk();
                    // Carry on down the road, out of sight.
                    int n = _t.Center.Length;
                    _path.Clear();
                    _path.Add(_pos);
                    for (int k = (int)StopAhead + 6; k < (int)StopAhead + 130; k += 3) _path.Add(_t.Center[(_carIndex + k) % n]);
                    PathLength();
                    _s = 0;
                }
                break;
        }
        if (_step != Step.Climb) _sergeantArm.Rotation = Vector3.Zero;
        _tailgate.Rotation = new Vector3(tail * 1.55f, 0, 0);
    }

    /// <summary>A road point moved into the truck's own lane (the right of the way it drives).</summary>
    Vector2 InLane(int i)
    {
        Vector2 dir = _t.CenterDir(i);
        return _t.Center[i] + new Vector2(-dir.Y, dir.X) * (_t.HalfWidth[i] * LaneShare);
    }

    /// <summary>
    /// The speed that keeps the truck behind traffic: any vehicle (a big obstacle that has moved in
    /// the last 20 s: tractors, the bus, ploughs, the column) in a corridor the truck's width ahead
    /// of it brakes it to a stop a few metres short. Trees, posts and buildings never move: ignored.
    /// </summary>
    float YieldSpeed()
    {
        var obs = _t.Obstacles;
        if (_seenAt == null || _seenAt.Length != obs.Count)
        {
            _seenAt = new Vector2[obs.Count];
            _movedAt = new float[obs.Count];
            for (int i = 0; i < obs.Count; i++) { _seenAt[i] = obs[i].Center; _movedAt[i] = -100f; }
        }
        var fwd = new Vector2(-MathF.Sin(_yaw), -MathF.Cos(_yaw));
        float cap = TopSpeed;
        for (int i = 0; i < obs.Count; i++)
        {
            var o = obs[i];
            if (o.Center.DistanceSquaredTo(_seenAt[i]) > 0.0004f) { _seenAt[i] = o.Center; _movedAt[i] = _t0; }
            if (o.Radius < 1f || o.Center.X < -1000f) continue;               // people, posts, bales; parked-away slots
            if (o.Velocity == Vector2.Zero && _t0 - _movedAt[i] > 20f) continue;
            Vector2 rel = o.Center - _pos;
            float along = rel.Dot(fwd);
            if (along < 0f || along > Nose + 18f) continue;
            if (MathF.Abs(rel.X * fwd.Y - rel.Y * fwd.X) > HalfWidth + o.Radius) continue;
            float room = along - Nose - o.Radius - Gap;
            cap = Math.Min(cap, room <= 0f ? 0f : MathF.Sqrt(2f * 4f * room));
        }
        return cap;
    }

    void PathLength()
    {
        _pathLen = 0;
        for (int i = 1; i < _path.Count; i++) _pathLen += _path[i].DistanceTo(_path[i - 1]);
    }

    /// <summary>Along the path; the heading chases a point a few metres ahead so corners are round.</summary>
    void Drive(float dt)
    {
        _s = Math.Min(_pathLen, _s + _speed * dt);
        Vector2 at = PathAt(_s), ahead = PathAt(_s + 4f);
        _pos = at;
        Vector2 d = ahead - at;
        if (d.LengthSquared() > 0.01f)
        {
            float want = TrackData.YawFor(d.Normalized());
            _yaw += Math.Clamp(Mathf.AngleDifference(_yaw, want), -1.4f * dt, 1.4f * dt);
        }
    }

    Vector2 PathAt(float s)
    {
        for (int i = 1; i < _path.Count; i++)
        {
            float l = _path[i].DistanceTo(_path[i - 1]);
            if (s <= l) return _path[i - 1].Lerp(_path[i], l > 0 ? s / l : 0);
            s -= l;
        }
        Vector2 last = _path[^1], prev = _path.Count > 1 ? _path[^2] : last;
        return last + (last - prev).Normalized() * s;
    }

    Vector3 Ground(Vector3 p) => new(p.X, _t.MeshHeight(p.X, p.Z), p.Z);

    void Place()
    {
        float y = _t.MeshHeight(_pos.X, _pos.Y, out Vector3 n);
        var b = new Basis(Vector3.Up, _yaw);
        Vector3 axis = Vector3.Up.Cross(n);
        if (axis.LengthSquared() > 1e-6f) b = new Basis(axis.Normalized(), Vector3.Up.AngleTo(n)) * b;
        b = b * new Basis(Vector3.Back, MathF.Sin(_t0 * 2.3f) * 0.02f * Math.Min(1f, Math.Abs(_speed)));
        _model.GlobalTransform = new Transform3D(b, new Vector3(_pos.X, y, _pos.Y));
    }

    /// <summary>Soldiers bob with the bumps; the prisoner hangs his head.</summary>
    void Animate(float dt)
    {
        float bump = Math.Min(1f, Math.Abs(_speed) / 6f);
        for (int i = 0; i < _soldiers.Count; i++)
            _soldiers[i].Position = _soldiers[i].Position with { Y = 1.62f + MathF.Abs(MathF.Sin(_t0 * 7f + i * 1.7f)) * 0.06f * bump };
        _ = dt;
    }

    void Exhaust(float dt)
    {
        Vector3 pipe = _model.GlobalTransform * (new Vector3(1.05f, 3.05f, -0.4f) * TruckScale);
        Vector3 fwd = -_model.GlobalTransform.Basis.Z.Normalized();
        // the player's diesel smoke, a lorry's big engine
        _fx.EngineSoot(pipe, -fwd, fwd * _speed, Mathf.Clamp(_speed / 8f, 0.25f, 1f), 2.2f, ref _soot, dt);
    }

    // ------------------------------------------------------------ model

    void BuildTruck(Node3D n)
    {
        // Frame, bumper, wheels: three axles.
        n.AddChild(Props.Part(Props.Box(1.3f, 0.35f, 7.2f), Black, new Vector3(0, 0.95f, 0.3f)));
        n.AddChild(Props.Part(Props.Box(2.5f, 0.28f, 0.25f), OliveDark, new Vector3(0, 1.0f, -3.45f)));
        foreach (float z in new[] { -2.3f, 1.3f, 2.75f })
        foreach (float s in new[] { -1f, 1f })
        {
            n.AddChild(Props.Part(Props.Cyl(0.58f, 0.42f, 10), Props.Rubber, new Vector3(s * 1.05f, 0.58f, z), new Vector3(0, 0, 90)));
            n.AddChild(Props.Part(Props.Cyl(0.3f, 0.44f, 8), OliveDark, new Vector3(s * 1.05f, 0.58f, z), new Vector3(0, 0, 90)));
        }
        // Long bonnet, grille, wings, headlights.
        n.AddChild(Props.Part(Props.Box(1.5f, 0.85f, 1.6f), Olive, new Vector3(0, 1.62f, -2.55f)));
        n.AddChild(Props.Part(Props.Box(1.3f, 0.7f, 0.08f), Black, new Vector3(0, 1.55f, -3.37f)));
        for (int k = 0; k < 5; k++)
            n.AddChild(Props.Part(Props.Box(0.06f, 0.6f, 0.04f), OliveDark, new Vector3(-0.5f + k * 0.25f, 1.55f, -3.42f)));
        foreach (float s in new[] { -1f, 1f })
        {
            n.AddChild(Props.Part(Props.Box(0.55f, 0.18f, 1.5f), Olive, new Vector3(s * 1.0f, 1.38f, -2.45f), new Vector3(8, 0, 0)));
            n.AddChild(Props.Part(Props.Cyl(0.14f, 0.1f, 8), new Color(1f, 0.95f, 0.7f), new Vector3(s * 0.95f, 1.55f, -3.25f), new Vector3(90, 0, 0)));
        }
        // Cab with the red star on the doors.
        n.AddChild(Props.Part(Props.Box(2.3f, 1.35f, 1.55f), Olive, new Vector3(0, 2.2f, -1.0f)));
        n.AddChild(Props.Part(Props.Box(2.36f, 0.08f, 1.6f), OliveDark, new Vector3(0, 2.9f, -1.0f)));
        n.AddChild(Props.Part(Props.Box(1.9f, 0.55f, 0.06f), Glass, new Vector3(0, 2.5f, -1.79f)));
        foreach (float s in new[] { -1f, 1f })
        {
            n.AddChild(Props.Part(Props.Box(0.08f, 0.24f, 0.08f), Black, new Vector3(s * 1.35f, 2.5f, -1.7f)));     // mirror
            if (s > 0) continue;
            n.AddChild(Props.Part(Props.Box(0.04f, 0.45f, 0.8f), Glass, new Vector3(s * 1.16f, 2.52f, -1.15f)));
            n.AddChild(Props.Part(Props.Box(0.04f, 0.32f, 0.32f), Red, new Vector3(s * 1.17f, 1.95f, -0.95f), new Vector3(45, 0, 0)));
        }
        // The passenger door (right), hinged at its front edge: the officer's way out and back in.
        _door = new Node3D { Position = new Vector3(1.17f, 1.55f, -1.72f) };
        n.AddChild(_door);
        _door.AddChild(Props.Part(Props.Box(0.06f, 1.3f, 0.95f), Olive, new Vector3(0.02f, 0.62f, 0.47f)));
        _door.AddChild(Props.Part(Props.Box(0.04f, 0.45f, 0.8f), Glass, new Vector3(0.05f, 0.97f, 0.45f)));
        _door.AddChild(Props.Part(Props.Box(0.04f, 0.32f, 0.32f), Red, new Vector3(0.06f, 0.4f, 0.77f), new Vector3(45, 0, 0)));
        // Spare wheel behind the cab, exhaust stack.
        n.AddChild(Props.Part(Props.Cyl(0.55f, 0.35f, 10), Props.Rubber, new Vector3(0, 2.2f, -0.05f), new Vector3(90, 0, 0)));
        n.AddChild(Props.Part(Props.Cyl(0.07f, 1.4f, 6), Black, new Vector3(1.05f, 2.4f, -0.4f)));

        // Open bed: planked floor, drop sides, benches.
        n.AddChild(Props.Part(Props.Box(2.5f, 0.14f, 4.2f), OliveDark, new Vector3(0, 1.5f, 1.95f)));
        n.AddChild(Props.Part(Props.Box(2.5f, 0.6f, 0.1f), Olive, new Vector3(0, 1.87f, -0.12f)));
        foreach (float s in new[] { -1f, 1f })
        {
            n.AddChild(Props.Part(Props.Box(0.1f, 0.6f, 4.2f), Olive, new Vector3(s * 1.2f, 1.87f, 1.95f)));
            n.AddChild(Props.Part(Props.Box(0.12f, 0.04f, 4.2f), OliveDark, new Vector3(s * 1.2f, 1.72f, 1.95f)));
            n.AddChild(Props.Part(Props.Box(0.4f, 0.1f, 3.6f), new Color(0.45f, 0.34f, 0.2f), new Vector3(s * 0.9f, 1.9f, 2f)));
        }
        // Canvas cover over the bed on its hoops, open at the back: a dark hole to be hauled into.
        // Tall enough for the sergeant standing at the tailgate.
        var canvas = new Color(0.2f, 0.22f, 0.14f);
        var hoop = new Color(0.14f, 0.15f, 0.1f);
        n.AddChild(Props.Part(Props.Box(2.44f, 1.5f, 0.06f), canvas, new Vector3(0, 2.9f, -0.12f)));          // front
        foreach (float s in new[] { -1f, 1f })
        {
            n.AddChild(Props.Part(Props.Box(0.06f, 1.32f, 4.2f), canvas, new Vector3(s * 1.22f, 2.83f, 1.95f)));   // sides
            n.AddChild(Props.Part(Props.Box(0.34f, 0.06f, 4.2f), canvas, new Vector3(s * 1.1f, 3.56f, 1.95f), new Vector3(0, 0, s * 35f)));   // shoulders
        }
        n.AddChild(Props.Part(Props.Box(2.0f, 0.06f, 4.2f), canvas, new Vector3(0, 3.66f, 1.95f)));            // roof
        n.AddChild(Props.Part(Props.Box(1.9f, 0.02f, 4.1f), Black, new Vector3(0, 3.6f, 1.95f)));               // the dark underside
        for (float z = 0.2f; z < 4.1f; z += 0.95f)
            n.AddChild(Props.Part(Props.Box(2.1f, 0.03f, 0.06f), hoop, new Vector3(0, 3.7f, z)));             // hoops showing through

        // Tailgate on its hinge at the bed floor.
        _tailgate = new Node3D { Position = new Vector3(0, 1.57f, 4.05f) };
        n.AddChild(_tailgate);
        _tailgate.AddChild(Props.Part(Props.Box(2.5f, 0.6f, 0.1f), Olive, new Vector3(0, 0.3f, 0)));
        _tailgate.AddChild(Props.Part(Props.Box(0.5f, 0.3f, 0.02f), new Color(0.9f, 0.9f, 0.85f), new Vector3(0, 0.3f, 0.06f)));   // number plate

        // Soldiers facing each other on the benches; a sergeant at the tailgate.
        float[] zs = { 0.6f, 1.6f, 2.6f };
        foreach (float s in new[] { -1f, 1f })
            foreach (float z in zs)
            {
                var sol = Soldier(seated: true);
                sol.Position = new Vector3(s * 0.9f, 1.62f, z + (s > 0 ? 0.3f : 0f));
                sol.Rotation = new Vector3(0, s > 0 ? MathF.PI / 2f : -MathF.PI / 2f, 0);
                n.AddChild(sol);
                _soldiers.Add(sol);
            }
        _sergeant = Soldier(seated: false);
        _sergeant.Position = new Vector3(0.5f, 1.57f, 3.6f);
        _sergeant.Rotation = new Vector3(0, MathF.PI, 0);
        n.AddChild(_sergeant);
        _sergeantArm = new Node3D { Position = new Vector3(0.3f, 1.25f, 0) };
        _sergeant.AddChild(_sergeantArm);
        _sergeantArm.AddChild(Props.Part(Props.Box(0.13f, 0.55f, 0.13f), Greatcoat, new Vector3(0, -0.27f, 0)));
    }

    static Node3D Soldier(bool seated)
    {
        var s = new Node3D();
        float hip = seated ? 0.35f : 0.8f;
        if (seated)
            s.AddChild(Props.Part(Props.Box(0.42f, 0.15f, 0.45f), Greatcoat, new Vector3(0, 0.35f, -0.2f)));
        else
        {
            s.AddChild(Props.Part(Props.Box(0.16f, 0.8f, 0.2f), Black, new Vector3(-0.11f, 0.4f, 0)));      // boots and legs
            s.AddChild(Props.Part(Props.Box(0.16f, 0.8f, 0.2f), Black, new Vector3(0.11f, 0.4f, 0)));
        }
        s.AddChild(Props.Part(Props.Box(0.5f, 0.68f, 0.34f), Greatcoat, new Vector3(0, hip + 0.32f, 0)));
        s.AddChild(Props.Part(Props.Box(0.52f, 0.06f, 0.36f), new Color(0.3f, 0.2f, 0.12f), new Vector3(0, hip + 0.1f, 0)));   // belt
        s.AddChild(Props.Part(Props.Box(0.12f, 0.5f, 0.12f), Greatcoat, new Vector3(-0.3f, hip + 0.35f, 0)));
        if (seated) s.AddChild(Props.Part(Props.Box(0.12f, 0.5f, 0.12f), Greatcoat, new Vector3(0.3f, hip + 0.35f, 0)));
        // Head in an ushanka: fur crown, ear flaps, red star.
        float hy = hip + 0.82f;
        s.AddChild(Props.Part(Props.Ball(0.15f), Skin, new Vector3(0, hy, 0)));
        s.AddChild(Props.Part(Props.Box(0.36f, 0.2f, 0.36f), Fur, new Vector3(0, hy + 0.14f, 0)));
        s.AddChild(Props.Part(Props.Box(0.4f, 0.07f, 0.4f), Fur * 0.85f, new Vector3(0, hy + 0.06f, 0)));
        s.AddChild(Props.Part(Props.Box(0.06f, 0.2f, 0.2f), Fur, new Vector3(-0.19f, hy - 0.02f, 0)));
        s.AddChild(Props.Part(Props.Box(0.06f, 0.2f, 0.2f), Fur, new Vector3(0.19f, hy - 0.02f, 0)));
        s.AddChild(Props.Part(Props.Box(0.08f, 0.08f, 0.03f), Red, new Vector3(0, hy + 0.14f, -0.19f), new Vector3(0, 0, 45)));
        // Rifle held upright.
        s.AddChild(Props.Part(Props.Box(0.06f, 0.95f, 0.07f), new Color(0.3f, 0.2f, 0.12f), new Vector3(seated ? 0.05f : -0.32f, hip + 0.3f, -0.3f)));
        s.Scale = Vector3.One * 1.15f;
        return s;
    }

    Node3D BuildDriver()
    {
        // The rally driver: blue overalls with a white stripe, a white full-face rally helmet with a
        // dark visor and a red band; arms on shoulder pivots (up at gunpoint).
        var d = new Node3D { Name = "Driver", Scale = Vector3.One * 1.6f };
        var suit = new Color(0.15f, 0.25f, 0.6f);
        var white = new Color(0.96f, 0.96f, 0.94f);
        _legL = new Node3D { Position = new Vector3(-0.1f, 0.75f, 0) };
        _legR = new Node3D { Position = new Vector3(0.1f, 0.75f, 0) };
        d.AddChild(_legL);
        d.AddChild(_legR);
        foreach (var leg in new[] { _legL, _legR })
        {
            leg.AddChild(Props.Part(Props.Box(0.15f, 0.7f, 0.18f), suit, new Vector3(0, -0.35f, 0)));
            leg.AddChild(Props.Part(Props.Box(0.15f, 0.08f, 0.24f), Black, new Vector3(0, -0.71f, -0.03f)));     // boots
        }
        d.AddChild(Props.Part(Props.Box(0.44f, 0.6f, 0.26f), suit, new Vector3(0, 1.05f, 0)));
        d.AddChild(Props.Part(Props.Box(0.08f, 0.5f, 0.02f), white, new Vector3(-0.1f, 1.08f, -0.135f)));   // stripe
        d.AddChild(Props.Part(Props.Box(0.46f, 0.05f, 0.28f), Black, new Vector3(0, 0.78f, 0)));            // belt
        _armL = new Node3D { Position = new Vector3(-0.27f, 1.3f, 0) };
        _armR = new Node3D { Position = new Vector3(0.27f, 1.3f, 0) };
        d.AddChild(_armL);
        d.AddChild(_armR);
        foreach (var arm in new[] { _armL, _armR })
        {
            arm.AddChild(Props.Part(Props.Box(0.14f, 0.27f, 0.15f), suit, new Vector3(0, -0.135f, 0)));
            arm.AddChild(Props.Part(Props.Box(0.07f, 0.2f, 0.016f), white, new Vector3(0, -0.13f, -0.083f)));
            var elbow = new Node3D { Position = new(0, -0.27f, 0), Rotation = new(0.7f, 0, 0) };
            arm.AddChild(elbow);
            elbow.AddChild(Props.Part(Props.Box(0.12f, 0.23f, 0.13f), suit, new(0, -0.115f, 0)));
            elbow.AddChild(Props.Part(Props.Box(0.125f, 0.055f, 0.14f), white, new(0, -0.22f, 0)));
            elbow.AddChild(Props.Part(Props.Box(0.11f, 0.12f, 0.08f), Black, new(0, -0.29f, 0)));
            elbow.AddChild(Props.Part(Props.Ball(0.035f, 6), Black, new(0.065f, -0.27f, -0.015f)));         // gloves
        }
        // Helmet: a white full-face shell, a wide dark visor, the chin bar, a thin blue stripe over the crown.
        d.AddChild(Props.Part(Props.Ball(0.22f, 8), white, new Vector3(0, 1.62f, 0)));

        d.AddChild(Props.Part(Props.Box(0.28f, 0.09f, 0.1f), white, new Vector3(0, 1.46f, -0.15f)));
        d.AddChild(Props.Part(Props.Box(0.07f, 0.03f, 0.4f), suit, new Vector3(0, 1.815f, 0.02f)));
        BuildDriverDetails(d);
        return d;
    }

    /// <summary>The officer: greatcoat, belt with a holster, a peaked cap with a red band; the right arm holds the pistol.</summary>
    Node3D BuildOfficer()
    {
        var o = new Node3D { Name = "Officer", Scale = Vector3.One * 1.6f };
        var coat = new Color(0.3f, 0.32f, 0.22f);
        var crown = new Color(0.36f, 0.38f, 0.26f);
        _offLegL = new Node3D { Position = new Vector3(-0.1f, 0.5f, 0) };
        _offLegR = new Node3D { Position = new Vector3(0.1f, 0.5f, 0) };
        o.AddChild(_offLegL);
        o.AddChild(_offLegR);
        foreach (var leg in new[] { _offLegL, _offLegR })
            leg.AddChild(Props.Part(Props.Box(0.15f, 0.5f, 0.2f), Black, new Vector3(0, -0.25f, 0)));          // boots
        o.AddChild(Props.Part(Props.Cyl(0.27f, 0.75f, 8, 0.22f), coat, new Vector3(0, 0.82f, 0)));           // greatcoat
        o.AddChild(Props.Part(Props.Box(0.5f, 0.42f, 0.3f), coat, new Vector3(0, 1.32f, 0)));
        o.AddChild(Props.Part(Props.Cyl(0.24f, 0.06f, 8), new Color(0.3f, 0.19f, 0.1f), new Vector3(0, 1.12f, 0)));   // belt
        o.AddChild(Props.Part(Props.Box(0.1f, 0.16f, 0.12f), new Color(0.3f, 0.19f, 0.1f), new Vector3(0.25f, 1.02f, 0.05f)));   // holster
        foreach (float sx in new[] { -1f, 1f })
            o.AddChild(Props.Part(Props.Box(0.16f, 0.03f, 0.18f), new Color(0.92f, 0.76f, 0.2f), new Vector3(sx * 0.2f, 1.54f, 0)));   // epaulettes
        var armL = new Node3D { Position = new Vector3(-0.31f, 1.48f, 0), Rotation = new Vector3(0, 0, -0.15f) };
        o.AddChild(armL);
        armL.AddChild(Props.Part(Props.Box(0.13f, 0.5f, 0.14f), coat, new Vector3(0, -0.25f, 0)));
        armL.AddChild(Props.Part(Props.Ball(0.07f), Skin, new Vector3(0, -0.53f, 0)));
        _gunArm = new Node3D { Name = "GunArm", Position = new Vector3(0.31f, 1.48f, 0) };
        o.AddChild(_gunArm);
        _gunArm.AddChild(Props.Part(Props.Box(0.13f, 0.5f, 0.14f), coat, new Vector3(0, -0.25f, 0)));
        _gunArm.AddChild(Props.Part(Props.Ball(0.07f), Skin, new Vector3(0, -0.53f, 0)));
        _gunArm.AddChild(Props.Part(Props.Box(0.06f, 0.22f, 0.12f), Black, new Vector3(0, -0.66f, -0.03f)));   // the Makarov
        // A big head under a peaked cap: khaki crown, red band, black visor, a red star.
        o.AddChild(Props.Part(Props.Ball(0.19f, 8), Skin, new Vector3(0, 1.75f, 0)));
        o.AddChild(Props.Part(Props.Box(0.24f, 0.05f, 0.05f), Black, new Vector3(0, 1.69f, -0.17f)));         // moustache
        o.AddChild(Props.Part(Props.Cyl(0.2f, 0.1f, 10), Red, new Vector3(0, 1.9f, 0)));
        o.AddChild(Props.Part(Props.Cyl(0.2f, 0.1f, 12, 0.36f), crown, new Vector3(0, 2.0f, 0)));
        o.AddChild(Props.Part(Props.Box(0.32f, 0.03f, 0.18f), Black, new Vector3(0, 1.86f, -0.24f), new Vector3(-20, 0, 0)));   // visor
        o.AddChild(Props.Part(Props.Box(0.08f, 0.08f, 0.03f), Red, new Vector3(0, 1.96f, -0.21f), new Vector3(0, 0, 45)));     // star
        return o;
    }
}
