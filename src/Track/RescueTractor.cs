using System;
using CCCPRacing.Audio;
using CCCPRacing.Car;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Track;

/// <summary>
/// When the Pyatyorka ends up on its roof or side, the kolkhoz sends a MT-Agro: it comes down the
/// road behind you flat out (for a tractor), honks, stops, the tractorist has a sip, the tow rope
/// goes on and the car is yanked back onto its wheels. Then he reverses off and vanishes. The time
/// all this takes is the penalty. RaceScene hands the car over while Active and gets it back upright.
/// A car with an empty tank (CallTow) gets the rope on its tail instead: the tractor reverses off
/// dragging it along for a few seconds (JustTowed), and the race puts it down at the fuel depot.
/// </summary>
public partial class RescueTractor : Node3D
{
    enum Phase { Idle, Approach, Sip, Pull, Tow, Leave }

    const float ApproachFrom = 28f;      // metres back along the road
    const float StopAt = 6.5f;           // tractor origin to car
    const float TopSpeed = 9f;
    const float PullTime = 1.5f;
    const float TowTime = 3.5f;          // s of dragging before the cut to the depot
    const float TowSpeed = 2.8f;

    TrackData _t;
    TyreEffects _fx;
    public Node3D Model => _model;
    Node3D _model, _driver, _head;
    MeshInstance3D _rope;
    DieselSound _sound;
    readonly RandomNumberGenerator _rng = new() { Seed = 1980 };

    Phase _phase, _dbgPhase;
    Vector2 _pos;
    float _yaw, _speed, _timer, _t0, _puff, _soot, _travel;      // (_travel: metres driven, the wheels turn by it)
    readonly System.Collections.Generic.List<DrunkTractor.TractorWheel> _wheels = new();
    Quaternion _carFrom;
    Vector3 _carFromPos, _carToPos;
    float _carYaw, _ropeLen;
    bool _tow;
    int _idx, _carIdx;      // the road sample the tractor is at, and the car's
    bool _road;            // on the way in it keeps to the road until the car is near
    const float RoadAhead = 8f, RoadLeaveCar = 14f;

    public bool Active => _phase is Phase.Approach or Phase.Sip or Phase.Pull or Phase.Tow;
    /// <summary>True for one Tick after the car lands back on its wheels.</summary>
    public bool JustRighted { get; private set; }
    /// <summary>True for one Tick when a tow (CallTow) is done: the race takes the car to the depot.</summary>
    public bool JustTowed { get; private set; }
    /// <summary>The tractor is on its way to tow, or towing.</summary>
    public bool Towing => _tow && Active;
    public bool Arriving => _phase == Phase.Approach;

    public void Init(TrackData t, TyreEffects fx)
    {
        _t = t;
        _fx = fx;
        _model = new Node3D { Name = "RescueMtAgro", Visible = false };
        AddChild(_model);
        DrunkTractor.BuildMtAgro(_model, plough: false, out _driver, out _head, null, false, _wheels);
        // Tow hook on the front weight.
        _model.AddChild(Props.Part(Props.Box(0.5f, 0.2f, 0.25f), new Color(0.3f, 0.3f, 0.32f), new Vector3(0, 0.7f, -1.95f)));
        _rope = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.06f, 0.06f, 1f) },
            MaterialOverride = Props.Mat(new Color(0.55f, 0.45f, 0.25f)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            TopLevel = true,
            Visible = false,
        };
        AddChild(_rope);
        _sound = new DieselSound();
        _model.AddChild(_sound);
        // Keep this version-1 structure compatible with existing tractor tapes.
        // The rope's unscaled endpoints are a separate actor, not children of this index entry.
        Tape.Version(this, 2); Tape.Part(this, "root"); Tape.Part(_model, "model"); Tape.Part(_driver, "driver"); Tape.Part(_head, "head");
        DrunkTractor.TapeWheels(_wheels, () => _travel);      // (version 2: the four wheels turn, their angles are spinners)
        Tape.Sound(_model, TapeSoundChannel.DieselSpeed, () => _sound.Speed, v => _sound.Speed = v);
        Tape.Sound(_model, TapeSoundChannel.DieselDozing, () => _sound.Dozing ? 1f : 0f, v => _sound.Dozing = v > 0.5f);
        InitTapeRope();
    }

    public void Reset()
    {
        _phase = Phase.Idle;
        _model.Visible = false;
        _rope.Visible = false;
        _ropeState.Visible = false;
        _sound.Speed = 0;
        _sound.Dozing = true;
        JustRighted = false;
        _tow = false;
    }

    /// <summary>Send the tractor for the car at its current spot.</summary>
    public void Call(CarSim car)
    {
        Vector2 c = new(car.Pos.X, car.Pos.Z);
        int i = _t.NearestCenter(c, out _);
        int n = _t.Center.Length;
        // Start back along the road; the road is where nothing is in the way.
        int back = (i - (int)ApproachFrom + n) % n;
        _pos = _t.Center[back];
        _idx = back; _carIdx = i; _road = true;
        if (_pos.DistanceTo(c) < ApproachFrom * 0.6f) { _pos = c - _t.CenterDir(i) * ApproachFrom; _road = false; }
        _yaw = TrackData.YawFor((c - _pos).Normalized());
        _speed = TopSpeed * 0.8f;
        _phase = Phase.Approach;
        _timer = 0;
        _model.Visible = true;
        _sound.Dozing = false;
        _sound.Honk();
        _tow = false;
        Place(0);
    }

    /// <summary>Send the tractor to tow the car away (an empty tank): it comes as for a rescue, then drags it.</summary>
    public void CallTow(CarSim car)
    {
        Call(car);
        _tow = true;
    }

    /// <summary>Advance the rescue; while Active it also poses the car.</summary>
    public void Tick(float dt, CarSim car)
    {
        if (TapeDriven || TapeRopeDriven) return;
        JustRighted = JustTowed = false;
        if (_phase == Phase.Idle) return;
        _t0 += dt;
        _timer += dt;
        Vector2 c = new(car.Pos.X, car.Pos.Z);
        Vector2 fwd = new(-MathF.Sin(_yaw), -MathF.Cos(_yaw));

        switch (_phase)
        {
            case Phase.Approach:
            {
                Vector2 to = c - _pos;
                float dist = to.Length();
                // Weaves a little on the way; the drink.
                Vector2 aim = c;
                if (_road && dist > RoadLeaveCar) aim = RoadTarget();
                else _road = false;
                Vector2 toAim = aim - _pos;
                float want = TrackData.YawFor(toAim / Math.Max(toAim.Length(), 0.01f)) + MathF.Sin(_t0 * 1.9f) * 0.12f * Math.Min(1f, (dist - StopAt) / 10f);
                _yaw += Math.Clamp(Mathf.AngleDifference(_yaw, want), -(_road ? 2.2f : 1.5f) * dt, (_road ? 2.2f : 1.5f) * dt);
                float target = Math.Clamp((dist - StopAt) * 1.6f, 1.5f, TopSpeed);   // crawls the last metres, never stalls
                _speed = Mathf.MoveToward(_speed, target, (target < _speed ? 9f : 4f) * dt);
                _pos += fwd * _speed * dt;
                if (dist - StopAt < 0.3f || _timer > 12f)
                {
                    _speed = 0;
                    _phase = Phase.Sip;
                    _timer = 0;
                    _sound.Honk();
                }
                break;
            }
            case Phase.Sip:
                // A sip for courage, a hiccup, rope on.
                if (_timer > 0.5f && _timer - dt <= 0.5f) Hiccup();
                if (_timer > 0.9f && _tow)
                {
                    _phase = Phase.Tow;
                    _timer = 0;
                    _idx = _t.NearestCenter(_pos, out _);
                    Vector3 hk = _model.GlobalTransform * new Vector3(0, 0.7f, -2.05f);
                    _ropeLen = Math.Max(2.5f, new Vector2(hk.X, hk.Z).DistanceTo(c) - 0.3f);
                    _carYaw = MathF.Atan2(-car.Forward.X, -car.Forward.Z);
                }
                else if (_timer > 0.9f)
                {
                    _phase = Phase.Pull;
                    _timer = 0;
                    _carFrom = car.Rot.GetRotationQuaternion();
                    _carFromPos = car.Pos;
                    // Keep the heading the car had, just the right way up.
                    Vector3 f = car.Forward;
                    if (new Vector2(f.X, f.Z).LengthSquared() < 0.01f) f = car.Up * -1f;
                    _carYaw = MathF.Atan2(-f.X, -f.Z);
                    // Dragged half a metre toward the tractor as it rolls over.
                    Vector2 toward = (_pos - c).Normalized() * 0.6f;
                    Vector2 end = c + toward;
                    _carToPos = new Vector3(end.X, _t.MeshHeight(end.X, end.Y) + 0.75f, end.Y);
                }
                break;
            case Phase.Pull:
            {
                // Reverses away while the rope rolls the car over, one heave.
                _speed = -1.2f;
                _pos += fwd * _speed * dt;
                float u = Math.Min(1f, _timer / PullTime);
                float e = u * u * (3 - 2 * u);
                var to = new Basis(Vector3.Up, _carYaw).GetRotationQuaternion();
                car.Rot = new Basis(_carFrom.Slerp(to, e));
                car.Pos = _carFromPos.Lerp(_carToPos, e) + Vector3.Up * MathF.Sin(e * MathF.PI) * 1.1f;
                car.Vel = Vector3.Zero;
                car.AngVel = Vector3.Zero;
                if (u >= 1f)
                {
                    car.Reset(_carToPos, _carYaw);
                    JustRighted = true;
                    _phase = Phase.Leave;
                    _timer = 0;
                    _rope.Visible = false;
                    Hiccup();
                }
                break;
            }
            case Phase.Tow:
            {
                // Reverses off with the car on the rope, its tail to the tractor, sliding along behind.
                _speed = Mathf.MoveToward(_speed, -TowSpeed, 2f * dt);
                ReverseAlongRoad(dt, 1.4f);
                fwd = new(-MathF.Sin(_yaw), -MathF.Cos(_yaw));
                _pos += fwd * _speed * dt;
                Vector3 hk = _model.GlobalTransform * new Vector3(0, 0.7f, -2.05f);
                Vector2 h2 = new(hk.X, hk.Z), away = c - h2;
                float d = away.Length();
                Vector2 dir = d > 0.01f ? away / d : -fwd;
                if (d > _ropeLen) c = h2 + dir * _ropeLen;
                _carYaw = Mathf.LerpAngle(_carYaw, TrackData.YawFor(dir), Math.Min(1f, dt * 2.5f));
                car.Pos = new Vector3(c.X, _t.MeshHeight(c.X, c.Y) + 0.55f, c.Y);
                car.Rot = new Basis(Vector3.Up, _carYaw);
                car.Vel = Vector3.Zero;
                car.AngVel = Vector3.Zero;
                if (_timer < TowTime) break;
                Reset();
                JustTowed = true;
                break;
            }
            case Phase.Leave:
                // Off he goes, reversing away, and he is gone.
                _speed = Mathf.MoveToward(_speed, -3.5f, 2f * dt);
                if (_timer < dt * 1.5f) _idx = _t.NearestCenter(_pos, out _);
                ReverseAlongRoad(dt, 1.2f);
                fwd = new(-MathF.Sin(_yaw), -MathF.Cos(_yaw));
                _pos += fwd * _speed * dt;
                if (_timer > 5f) Reset();
                break;
        }

        if (OS.HasEnvironment("RESCUEDBG") && _phase != _dbgPhase) { GD.Print($"{Time.GetTicksMsec() / 1000f:F2}s rescue {_dbgPhase} -> {_phase}, car up {car.Up.Y:F2} at ({car.Pos.X:F1},{car.Pos.Y:F1},{car.Pos.Z:F1}) tractor ({_pos.X:F1},{_pos.Y:F1})"); _dbgPhase = _phase; }
        if (_phase == Phase.Idle) return;
        Place(dt);
        Rope(car);
        Exhaust(dt);
        _sound.Speed = Math.Abs(_speed) + (_phase == Phase.Pull ? 3f : 0f);
    }

    /// <summary>The next stretch of the road to drive on towards the car: the sample a few metres ahead of the tractor's own.</summary>
    Vector2 RoadTarget()
    {
        int n = _t.Center.Length;
        while (_pos.DistanceTo(_t.Center[_idx]) < 6f && _idx != _carIdx) _idx = (_idx + 1) % n;
        if ((_carIdx - _idx + n) % n > n / 2) _road = false;      // run past it (a loop seam, a car behind): straight at it
        return _t.Center[(_idx + (int)RoadAhead) % n];
    }

    /// <summary>Backing away keeps to the road too: the tractor faces forward along it and reverses back the way it came.</summary>
    void ReverseAlongRoad(float dt, float turnRate)
    {
        int n = _t.Center.Length;
        int best = _idx; float bd = float.MaxValue;
        for (int k = -4; k <= 4; k++)
        {
            int j = ((_idx + k) % n + n) % n;
            float d = _pos.DistanceTo(_t.Center[j]);
            if (d < bd) { bd = d; best = j; }
        }
        _idx = best;
        Vector2 target = _t.Center[((_idx - (int)RoadAhead) % n + n) % n];
        Vector2 move = target - _pos;
        if (move.LengthSquared() < 0.01f) return;
        float want = TrackData.YawFor(-move.Normalized());      // faces the way it came from: forward along the road
        _yaw += Math.Clamp(Mathf.AngleDifference(_yaw, want), -turnRate * dt, turnRate * dt);
    }

    void Hiccup()
    {
        _sound.Cough();
        Vector3 pipe = _model.GlobalTransform * new Vector3(0.4f, 2.95f, -1.2f);
        for (int k = 0; k < 4; k++)
            _fx.Smoke(pipe + Vector3.Up * k * 0.3f, Vector3.Up * 2f, new Color(0.08f, 0.08f, 0.08f), 0.8f, 0.6f, 2.4f, 1.6f);
    }

    void Exhaust(float dt)
    {
        Vector3 pipe = _model.GlobalTransform * new Vector3(0.4f, 2.95f, -1.2f);
        Vector3 fwd = -_model.GlobalTransform.Basis.Z.Normalized();
        // the player's diesel smoke, bigger for a tractor; straining at the tow it works at full throttle
        _fx.EngineSoot(pipe, -fwd, fwd * _speed, _phase == Phase.Pull ? 1f : Mathf.Clamp(_speed / 4f, 0.25f, 1f), 1.4f, ref _soot, dt);
    }

    void Place(float dt)
    {
        _travel += _speed * dt;
        DrunkTractor.RollWheels(_wheels, _travel);
        float y = _t.MeshHeight(_pos.X, _pos.Y, out Vector3 n);
        var b = new Basis(Vector3.Up, _yaw);
        Vector3 axis = Vector3.Up.Cross(n);
        if (axis.LengthSquared() > 1e-6f) b = new Basis(axis.Normalized(), Vector3.Up.AngleTo(n)) * b;
        b = b * new Basis(Vector3.Back, MathF.Sin(_t0 * 1.7f) * 0.04f * Math.Min(1f, Math.Abs(_speed)));
        _model.GlobalTransform = new Transform3D(b, new Vector3(_pos.X, y, _pos.Y));
        float nod = _phase == Phase.Sip ? -0.6f : MathF.Sin(_t0 * 2.1f) * 0.25f;   // head back: bottle up
        _head.Rotation = new Vector3(nod, MathF.Sin(_t0 * 0.7f) * 0.4f, MathF.Sin(_t0 * 1.3f) * 0.3f);
        _driver.Rotation = new Vector3(0, 0, MathF.Sin(_t0 * 1.1f) * 0.15f);
    }

    void Rope(CarSim car)
    {
        if (TapeRopeDriven) return;
        bool on = _phase is Phase.Pull or Phase.Tow || (_phase == Phase.Sip && _timer > 0.7f);
        _ropeState.Visible = on;
        _rope.Visible = on;
        if (!on) return;
        Vector3 hook = _model.GlobalTransform * new Vector3(0, 0.7f, -2.05f);
        // Tied round the sill on the tractor's side, so the pull rolls the car toward it; a tow takes the tail.
        Vector3 side = car.Rot.X.Dot(hook - car.Pos) > 0 ? Vector3.Right : Vector3.Left;
        Vector3 tie = car.Pos + car.Rot * (_tow ? new Vector3(0, -0.1f, 2.05f) : side * 0.85f);
        _ropeHook.GlobalPosition = hook;
        _ropeTie.GlobalPosition = tie;
        DrawRope(hook, tie);
    }

    void DrawRope(Vector3 hook, Vector3 tie)
    {
        Vector3 d = tie - hook;
        float len = d.Length();
        if (len < 0.05f) { _rope.Visible = false; return; }
        _rope.GlobalTransform = new Transform3D(Basis.LookingAt(d / len, Mathf.Abs(d.Y / len) > 0.99f ? Vector3.Right : Vector3.Up) * Basis.FromScale(new Vector3(1, 1, len)), (hook + tie) * 0.5f);
    }
}
