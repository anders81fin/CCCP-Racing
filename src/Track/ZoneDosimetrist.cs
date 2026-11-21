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
    Node3D _figure, _head, _probeArm, _meterArm, _wash;
    TrackData _t;
    readonly List<(int Index, Obstacle Was)> _tapeObstacles = new();
    /// <summary>The replay shows his tape poses (the figure, the head, the arms, the wash flag): he makes none of his own, and his obstacle is parked.</summary>
    public bool TapeDriven { get; private set; }
    /// <summary>A flag that is visible while the car is washed in the decontamination bay and stands at the car: a tape part, so the replay's emitters make the hose's spray over the car (TapeEmitters.Kind.Wash).</summary>
    public Node3D WashFlag => _wash;
    // The check (Inspect): where it is in its time and the car's place; the poses ease towards what that time asks (so a stop, a reset or a knock-over never snaps or sticks).
    float _elapsed = -1f, _total = 10f;
    Vector3 _car;
    float _turn, _probe, _meter, _yaw, _sweepPhase;
    Transform3D _rest;
    readonly Knockdown _down = new();
    int _obstacle = -1;

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
        d._head = d._figure.GetNode<Node3D>("Head"); d._probeArm = d._figure.GetNode<Node3D>("ProbeArm"); d._meterArm = d._figure.GetNode<Node3D>("MeterArm");
        d.AddChild(d._figure);
        d._wash = new Node3D { Name = "Wash", Visible = false };
        d.AddChild(d._wash);
        d._rest = d._figure.Transform;
        d._obstacle = t.Obstacles.Count;
        t.Obstacles.Add(new Obstacle(new Vector2(d._figure.Position.X, d._figure.Position.Z), 0.4f * Atomgrad.DosimetristScale, default, false, 0.5f, true, 2.5f));     // a man: 9 km/h off the car
        d._down.Bind(t, d._obstacle);
        // The world tape (docs/replay-tape.md): his figure (with the knock-over's tilt and the turn), head and both arms, and the wash flag (visible, at the car, while it is washed).
        Tape.Version(d, 1);
        Tape.Part(d, "root"); Tape.Part(d._figure, "figure"); Tape.Part(d._head, "head"); Tape.Part(d._probeArm, "probeArm"); Tape.Part(d._meterArm, "meterArm"); Tape.Part(d._wash, "wash");
        return d;
    }

    /// <summary>The place over his head for the speech bubble.</summary>
    public Vector3 HeadAt => _figure.Position + Vector3.Up * 4.1f;

    /// <summary>What knocks him over (Breakables.AddKnock).</summary>
    public IEnumerable<(int obstacle, Func<Vector3, bool> hit)> Knocks()
    {
        yield return (_obstacle, _down.Hit);
    }

    /// <summary>A new run: standing, his obstacle where it was.</summary>
    public void Reset()
    {
        _down.Reset();
        _elapsed = -1f; _turn = _probe = _meter = _yaw = 0f;      // (a new run: at rest at once)
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
        _figure.Transform = new Transform3D(_down.Tilt * new Basis(Vector3.Up, _yaw * _turn) * _rest.Basis, _rest.Origin);
        _probeArm.Rotation = new Vector3(0.6f * _probe, 0.45f * MathF.Sin(_sweepPhase) * _probe, 0);      // (down over the wing, along it and back)
        _meterArm.Rotation = new Vector3(-1.3f * _meter, 0, 0);                                           // (the meter lifted to his eyes)
        _head.Rotation = new Vector3(0.5f * _meter, -0.6f * _meter, 0);                                  // (a glance down at it)
    }

    public override void _Process(double delta)
    {
        if (TapeDriven) return;      // (the replay poses him)
        float dt = (float)delta;
        _down.Tick(dt);
        Vector3 toCar = _rest.Basis.Inverse() * (_car - _rest.Origin);
        float dist = new Vector2(toCar.X, toCar.Z).Length();
        bool active = _elapsed >= 0f && dist < Reach && !_down.Down;
        float e = _elapsed, end = _total;
        float turnT = active && e < end - 1.3f ? 1f : 0f;
        float probeT = active && e > 0.8f && e < end - 2.2f ? 1f : 0f;
        float meterT = active && (e > 2.6f && e < 3.9f || e > 5.6f && e < 6.9f) ? 1f : 0f;
        if (active) _yaw = Mathf.Clamp(MathF.Atan2(toCar.X, toCar.Z), -1.7f, 1.7f);      // (towards the car as it stands now, kept while he turns back)
        float k = 1f - MathF.Exp(-7f * dt);
        _turn = Mathf.Lerp(_turn, turnT, 1f - MathF.Exp(-4f * dt));
        _probe = Mathf.Lerp(_probe, probeT, k);
        _meter = Mathf.Lerp(_meter, meterT, k);
        if (probeT > 0f) _sweepPhase += dt * 2.4f;
        Pose();
    }
}
