using System;
using System.Collections.Generic;
using CCCPRacing.Car;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Track;

/// <summary>
/// The zone checkpoint's dosimetrist (ATOMGRAD-4): a figure of his own beside the road at the start (Atomgrad.DosimetristAt, in the checkpoint's
/// frame), standing on the ground there. Like the GAI inspector and the other people the car knocks him over instead of stopping
/// against him: he tips away from the car, lies a while and gets up again (Knockdown); his obstacle is soft (the car ploughs on).
/// RaceScene gives him his speech bubble while he checks the car (DoseShouts) and resets him with a new run.
/// </summary>
public partial class ZoneDosimetrist : Node3D
{
    Node3D _figure, _body, _head, _probeArm, _meterArm, _probeElbow, _meterElbow, _hipL, _hipR, _kneeL, _kneeR, _wash;
    TrackData _t;
    readonly List<(int Index, Obstacle Was)> _tapeObstacles = new();
    /// <summary>The replay shows his tape poses (the figure, the head, the arms, the wash flag): he makes none of his own, and his obstacle is parked.</summary>
    public bool TapeDriven { get; private set; }
    /// <summary>A flag that is visible while the car is washed in the decontamination bay and stands at the car: a tape part, so the replay's emitters make the hose's spray over the car (TapeEmitters.Kind.Wash).</summary>
    public Node3D WashFlag => _wash;
    // The check (Inspect): where it is in its time and the car's place; the poses ease towards what that time asks (so a stop, a reset or a knock-over never snaps or sticks).
    float _elapsed = -1f, _total = 10f;
    Vector3 _car;
    float _turn, _probe, _meter, _yaw, _step, _lean, _sweep;
    Transform3D _rest;
    readonly Knockdown _down = new();
    int _obstacle = -1;
    bool _serviceWalking;
    float _serviceYaw;
    public Vector2 Home => new(_rest.Origin.X, _rest.Origin.Z);

    public void PoseServiceWalk(Vector2 at, Vector2 facing, float distance, bool measuring = false, bool ordering = false)
    {
        if (TapeDriven) return;
        if (!_serviceWalking)
        {
            _down.Reset();
            _serviceYaw = Mathf.Atan2(_rest.Basis.Z.X, _rest.Basis.Z.Z);
            _serviceWalking = true;
        }
        if (facing.LengthSquared() > .0001f)
            _serviceYaw = Mathf.LerpAngle(_serviceYaw, Mathf.Atan2(facing.X, facing.Y), .18f);
        float stride = Mathf.Sin(distance * Mathf.Tau / 1.65f);
        float bend = measuring ? .22f + .07f * stride : 0f;
        _figure.Transform = new Transform3D(new Basis(Vector3.Up, _serviceYaw) * Basis.FromScale(_rest.Basis.Scale),
            new Vector3(at.X, _t.MeshHeight(at.X, at.Y) + .045f * Math.Abs(stride), at.Y));
        _hipL.Rotation = new Vector3(.31f * stride, 0, 0);
        _hipR.Rotation = new Vector3(-.31f * stride, 0, 0);
        _kneeL.Rotation = new Vector3(.21f * Math.Max(0f, -stride), 0, 0);
        _kneeR.Rotation = new Vector3(.21f * Math.Max(0f, stride), 0, 0);
        _body.Position = new Vector3(0, .86f, 0);
        _body.Rotation = new Vector3(bend, 0, .05f * stride);
        _probeArm.Rotation = new Vector3(ordering ? -1.35f : measuring ? -.92f + .14f * stride : -.17f * stride,
            ordering ? .8f : measuring ? .25f * stride : 0, 0);
        _probeElbow.Rotation = new Vector3(measuring ? -.57f : -.28f, 0, 0);
        _meterArm.Rotation = new Vector3(measuring ? -.55f : .17f * stride, 0, 0);
        _meterElbow.Rotation = new Vector3(measuring ? -.7f : -.23f, 0, 0);
        _head.Rotation = new Vector3(measuring ? .12f : 0, 0, 0);
        if (_obstacle >= 0) _t.Obstacles[_obstacle] = _t.Obstacles[_obstacle] with { Center = at };
    }

    public void EndServiceWalk()
    {
        if (!_serviceWalking) return;
        _serviceWalking = false;
        if (_obstacle >= 0) _t.Obstacles[_obstacle] = _t.Obstacles[_obstacle] with { Center = Home };
        Pose();
    }

    /// <summary>Where he stands: the checkpoint's frame point put on the ground.</summary>
    public static Vector3 WorldAt(TrackData t, PropInstance checkpoint)
    {
        Vector3 w = checkpoint.Position + new Basis(Vector3.Up, checkpoint.Rotation) * Atomgrad.DosimetristAt;
        return new Vector3(w.X, t.MeshHeight(w.X, w.Z), w.Z);
    }

    /// <summary>The first checkpoint's dosimetrist; null if the track has none.</summary>
    public static ZoneDosimetrist Build(TrackData t)
    {
        var pr = t.Props.Find(p => p.Type == "checkpoint");
        if (pr == null) return null;
        var d = new ZoneDosimetrist { Name = "Dosimetrist" };
        d._figure = Atomgrad.Dosimetrist(Vector3.Zero);
        d._figure.Position = WorldAt(t, pr);
        d._figure.Rotation = new Vector3(0, pr.Rotation, 0);
        d._t = t;
        d._body = d._figure.GetNode<Node3D>("Torso");
        d._head = d._body.GetNode<Node3D>("Head");
        d._probeArm = d._body.GetNode<Node3D>("ProbeArm");
        d._meterArm = d._body.GetNode<Node3D>("MeterArm");
        d._probeElbow = d._probeArm.GetNode<Node3D>("Elbow");
        d._meterElbow = d._meterArm.GetNode<Node3D>("Elbow");
        d._hipL = d._figure.GetNode<Node3D>("HipL");
        d._hipR = d._figure.GetNode<Node3D>("HipR");
        d._kneeL = d._hipL.GetNode<Node3D>("Knee");
        d._kneeR = d._hipR.GetNode<Node3D>("Knee");
        d.AddChild(d._figure);
        d._wash = new Node3D { Name = "Wash", Visible = false };
        d.AddChild(d._wash);
        d._rest = d._figure.Transform;
        d._obstacle = t.Obstacles.Count;
        t.Obstacles.Add(new Obstacle(new Vector2(d._figure.Position.X, d._figure.Position.Z), 0.4f * Atomgrad.DosimetristScale, default, false, 0.5f, true, 2.5f));     // a man: 9 km/h off the car
        d._down.Bind(t, d._obstacle);
        // The world tape (docs/replay-tape.md): his figure (with the knock-over's tilt and the turn), head and both arms, and the wash flag (visible, at the car, while it is washed).
        Tape.Version(d, 2);
        Tape.Part(d, "root"); Tape.Part(d._figure, "figure"); Tape.Part(d._head, "head"); Tape.Part(d._probeArm, "probeArm"); Tape.Part(d._meterArm, "meterArm"); Tape.Part(d._wash, "wash");
        Tape.Part(d._body, "torso");
        Tape.Part(d._probeElbow, "probeElbow"); Tape.Part(d._meterElbow, "meterElbow");
        Tape.Part(d._hipL, "hipL"); Tape.Part(d._hipR, "hipR");
        Tape.Part(d._kneeL, "kneeL"); Tape.Part(d._kneeR, "kneeR");
        d.Pose();
        return d;
    }

    /// <summary>The place over his head for the speech bubble.</summary>
    public Vector3 HeadAt => _head.GlobalPosition + Vector3.Up * 0.7f;

    /// <summary>What knocks him over (Breakables.AddKnock).</summary>
    public IEnumerable<(int obstacle, Func<Vector3, bool> hit)> Knocks()
    {
        yield return (_obstacle, velocity => !_serviceWalking && _down.Hit(velocity));
    }

    /// <summary>A new run: standing, his obstacle where it was.</summary>
    public void Reset()
    {
        _down.Reset();
        EndServiceWalk();
        _elapsed = -1f; _turn = _probe = _meter = _yaw = _step = _lean = _sweep = 0f;      // (a new run: at rest at once)
        _wash.Visible = false;
        Pose();
    }

    /// <summary>
    /// The dosimetric check, from the car stopped at the line (elapsed seconds into the check of total seconds, car the car's place; elapsed below 0 = no check). He turns to face the car where it
    /// really stands, sweeps the probe along its wing, glances twice at the meter, and in the last moments turns back and lowers the probe to his resting pose. The check ending, a restart, the car
    /// being taken away (a rescue: too far to reach) or his own knock-over all bring him back to rest; he never points at a car that is not there.
    /// </summary>
    public void Inspect(float elapsed, float total, Vector3 car)
    {
        _elapsed = elapsed; _total = Math.Max(1f, total); _car = car;
    }

    /// <summary>The car is washed in the bay now (or not): the flag stands at the car for the replay's spray (the spray itself is WashSprayFx, the same live and in the replay).</summary>
    public void SetWash(bool washing, Vector3 carAt)
    {
        if (TapeDriven) return;
        _wash.Visible = washing;
        if (washing) _wash.GlobalPosition = carAt;
    }

    /// <summary>The hose over the car in the decontamination bay: a drop or two a step from above the car (about 40 a second), as live play makes them and the replay's emitters do from the flag.</summary>
    public static void WashSprayFx(TyreEffects fx, RandomNumberGenerator rng, Vector3 carAt, float dt)
    {
        if (fx == null || rng.Randf() >= dt * 40f) return;
        fx.Burst(carAt + new Vector3(rng.RandfRange(-1.5f, 1.5f), 3.2f, rng.RandfRange(-2f, 2f)), Vector3.Down * 3f + new Vector3(rng.RandfRange(-1f, 1f), 0, rng.RandfRange(-1f, 1f)),
            new Color(0.75f, 0.85f, 1f), 0.6f, 0.25f, 0.6f, 0.7f, 6f, 0.6f, false);
    }

    /// <summary>The replay shows (or stops showing) his tape poses: his obstacle is parked while it does, and he stands at rest when it ends.</summary>
    public void SetTapeDriven(bool on)
    {
        if (TapeDriven == on) return;
        if (!on)
        {
            foreach (var (index, was) in _tapeObstacles) _t.Obstacles[index] = was;
            _tapeObstacles.Clear();
            TapeDriven = false;
            Reset();
            return;
        }
        Reset();
        _tapeObstacles.Add((_obstacle, _t.Obstacles[_obstacle]));
        _t.Obstacles[_obstacle] = new Obstacle(new Vector2(-1e4f, -1e4f), .1f);
        TapeDriven = true;
    }

    /// <summary>The probe is within reach of the car only this close (m).</summary>
    const float Reach = 14f;

    void Pose()
    {
        var facing = new Basis(Vector3.Up, _yaw * _turn) * _rest.Basis;
        var advance = _down.Down ? Vector3.Zero : facing.Z.Normalized() * (.31f * _step);
        _figure.Transform = new Transform3D(_down.Tilt * facing, _rest.Origin + advance);
        _hipL.Rotation = new Vector3(-.29f * _step, 0, 0);
        _hipR.Rotation = new Vector3(.19f * _step, 0, 0);
        _kneeL.Rotation = new Vector3(.13f * _step + .20f * _lean, 0, 0);
        _kneeR.Rotation = new Vector3(.15f * _lean, 0, 0);
        _body.Position = new Vector3(.04f * _step, .86f - .075f * _lean, 0);
        _body.Rotation = new Vector3(.33f * _lean, .13f * _probe, -.08f * _step);
        _probeArm.Rotation = new Vector3(-.88f * _probe, _sweep, -.16f * _probe);
        _probeElbow.Rotation = new Vector3(-.28f - .55f * _probe, 0, 0);
        _meterArm.Rotation = new Vector3(-.65f * _meter, 0, .14f * _meter);
        _meterElbow.Rotation = new Vector3(-.23f - .74f * _meter, 0, 0);
        _head.Rotation = new Vector3(.22f * _meter - .08f * _lean, .23f * _turn - .49f * _meter, 0);
    }

    public override void _Process(double delta)
    {
        if (_serviceWalking && OS.HasEnvironment("DOSEDBG")) GD.Print($"dosimetrist at {_figure.Position.X:F1},{_figure.Position.Z:F1} home {Home.X:F1},{Home.Y:F1}");
        if (TapeDriven || _serviceWalking) return;      // (walking round the stopped car: RaceScene poses him, the check's rest pose here would put him back at his post every frame)
        float dt = (float)delta;
        _down.Tick(dt);
        Vector3 toCar = _rest.Basis.Inverse() * (_car - _rest.Origin);
        bool active = _elapsed >= 0f && new Vector2(toCar.X, toCar.Z).Length() < Reach && !_down.Down;
        float t = _elapsed / _total;
        float k = 1f - MathF.Exp(-9f * dt);
        if (active) _yaw = Mathf.Clamp(MathF.Atan2(toCar.X, toCar.Z), -1.7f, 1.7f);
        float Phase(float begin, float peak, float hold, float end) => active
            ? Mathf.SmoothStep(begin, peak, t) * (1f - Mathf.SmoothStep(hold, end, t)) : 0f;
        _turn = Mathf.Lerp(_turn, Phase(0f, .14f, .79f, .97f), k);
        _step = Mathf.Lerp(_step, Phase(.10f, .25f, .74f, .91f), k);
        _lean = Mathf.Lerp(_lean, Phase(.19f, .32f, .52f, .70f), k);
        _probe = Mathf.Lerp(_probe, Phase(.24f, .36f, .51f, .65f), k);
        _sweep = Mathf.Lerp(_sweep, _probe * Mathf.Lerp(-.46f, .46f, Mathf.SmoothStep(.35f, .53f, t)), k);
        _meter = Mathf.Lerp(_meter, Phase(.53f, .63f, .74f, .84f), k);
        Pose();
    }
}
