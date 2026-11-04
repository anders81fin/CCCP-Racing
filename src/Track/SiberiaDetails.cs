using System;
using System.Collections.Generic;
using CCCPRacing.Car;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Track;

/// <summary>Small local winter work scenes. Layout is shared by forest reservations, collisions and rendering.</summary>
public partial class SiberiaDetails : Node3D
{
    public const float WorkerScale = 1.6f, BodyRadius = .64f;
    public record WorkerSpec(string Id, WinterJob Job, Vector2 A, Vector2 B, float Yaw,
        int Variant = 0, float Period = 20, float Phase = 0);
    public record PropSpec(string Id, Vector2 Position, float Yaw, Func<Node3D> Build);
    public record SolidSpec(string Id, Vector2 Position, float Radius);
    public record SawSpec(string Id, Vector2 Position, float Yaw, float Phase);

    public sealed class ActivityLayout
    {
        public readonly List<WorkerSpec> Workers = new();
        public readonly List<PropSpec> Props = new();
        public readonly List<SolidSpec> Solids = new();
        public readonly List<(Vector2 Center, float Radius)> Reservations = new();
        public readonly List<SawSpec> Saws = new();
        public void Reserve(Vector2 center, float radius) => Reservations.Add((center, radius));
        public void AddProp(string id, Vector2 position, float yaw, Func<Node3D> build,
            params (Vector2 Offset, float Radius)[] collisionCircles)
        {
            Props.Add(new(id, position, yaw, build));
            foreach (var (offset, radius) in collisionCircles)
            {
                var local = new Basis(Vector3.Up, yaw) * new Vector3(offset.X, 0, offset.Y);
                Solids.Add(new(id, position + new Vector2(local.X, local.Z), radius));
            }
        }
        public void AddSaw(string id, Vector2 center, float yaw, float phase = 0) => Saws.Add(new(id, center, yaw, phase));
    }

    sealed class Worker
    {
        public WorkerSpec Spec;
        public WinterWorkerModel Model;
        public int Obstacle;
        public float Time;
        public Vector2 Position;
    }
    readonly List<Worker> _workers = new();
    readonly List<(Node3D Model, Vector3 Origin, SawSpec Spec)> _saws = new();
    TrackData _track;
    BalalaikaSorties _jets;
    float _clock, _step;

    public static ActivityLayout Layout(TrackData t, TrackDef def)
    {
        var plan = new ActivityLayout();
        if (t.Id != "siberia") return plan;
        AddForestry(t, def, plan);
        AddCamp(t, def, plan);
        AddRadar(t, plan);
        AddAirbase(t, plan);
        return plan;
    }

    /// <summary>Called before random trees, once. Tree reservations cover full walking routes.</summary>
    public static void Reserve(TrackData t, TrackDef def, List<Vector2> occupied)
    {
        if (t.Id != "siberia") return;
        var plan = t.SiberiaActivities = Layout(t, def);
        foreach (var (center, radius) in plan.Reservations)
        for (float z = -radius; z <= radius; z += 2)
        for (float x = -radius; x <= radius; x += 2)
            if (x * x + z * z <= radius * radius) occupied.Add(center + new Vector2(x, z));
        foreach (var spec in plan.Workers)
        {
            int steps = Math.Max(1, (int)MathF.Ceiling(spec.A.DistanceTo(spec.B)));
            for (int i = 0; i <= steps; i++) occupied.Add(spec.A.Lerp(spec.B, i / (float)steps));
        }
        foreach (var solid in plan.Solids)
        {
            t.Obstacles.Add(new Obstacle(solid.Position, solid.Radius));
            occupied.Add(solid.Position);
        }
    }

    public static SiberiaDetails Build(TrackData t, BalalaikaSorties jets)
    {
        SiberiaDetails result = null;
        foreach (var step in BuildSteps(t, jets)) result = step ?? result;
        return result;
    }

    /// <summary>The same build in slices (a null between them: a frame may go by while a loading card runs); the last item is the details.
    /// A build abandoned between slices frees the half-made node.</summary>
    public static IEnumerable<SiberiaDetails> BuildSteps(TrackData t, BalalaikaSorties jets)
    {
        var root = new SiberiaDetails { Name = "SiberiaDetails", _track = t, _jets = jets };
        bool handedOver = false;
        try
        {
        var plan = t.SiberiaActivities;
        if (plan == null) { handedOver = true; yield return root; yield break; }
        foreach (var prop in plan.Props)
        {
            var model = prop.Build();
            model.Name = prop.Id;
            model.Position = Ground(t, prop.Position);
            model.Rotation = new Vector3(0, prop.Yaw, 0);
            root.AddChild(model);
            yield return null;
        }
        foreach (var spec in plan.Workers)
        {
            var model = WinterWorkerModel.Build(spec.Job, spec.Variant);
            model.Name = spec.Id;
            model.Scale = Vector3.One * WorkerScale;
            if (spec.Job == WinterJob.Saw && plan.Saws.Count > 0)
            {
                Vector3 strokeAxis = new Basis(Vector3.Up, plan.Saws[0].Yaw).X;
                model.SawStrokeDirection = MathF.Sign(new Basis(Vector3.Up, spec.Yaw).Z.Dot(strokeAxis));
            }
            root.AddChild(model);
            model.MarkTape(spec.Id + "/");
            int index = t.Obstacles.Count;
            t.Obstacles.Add(new Obstacle(spec.A, BodyRadius));
            root._workers.Add(new Worker { Spec = spec, Model = model, Obstacle = index });
            yield return null;
        }
        foreach (var saw in plan.Saws)
        {
            var model = SharedSaw();
            model.Name = saw.Id;
            var rest = Ground(t, saw.Position) + Vector3.Up * 1.45f;
            model.Position = rest;
            model.Rotation = new Vector3(0, saw.Yaw, 0);
            root.AddChild(model);
            Tape.Part(model, saw.Id + "/saw");
            root._saws.Add((model, rest, saw));
            yield return null;
        }
        if (root._workers.Count + root._saws.Count > 0) { Tape.Version(root, 1); Tape.Part(root, "root"); }      // the world tape: only when there is something to move
        root.Reset();
        handedOver = true;
        yield return root;
        }
        finally { if (!handedOver) root.Free(); }
    }

    public void Reset()
    {
        _clock = _step = 0;
        foreach (var worker in _workers) { worker.Time = worker.Spec.Phase; Place(worker, 0); }
        foreach (var (model, rest, spec) in _saws)
            model.Position = rest + model.Basis.X * (.20f * SawStroke(spec.Phase));
    }

    /// <summary>Empty-road modes hide people, while physical scene props remain visible.</summary>
    public void SetWorkersActive(bool active)
    {
        foreach (var worker in _workers) worker.Model.Visible = active;
    }

    /// <summary>Ping-pong journey with pauses at both ends, including the actual facing and gait state.</summary>
    public static (Vector2 Position, float Yaw, bool Walking) Route(WorkerSpec spec, float time)
    {
        if (spec.A.DistanceSquaredTo(spec.B) < .001f) return (spec.A, spec.Yaw, false);
        float phase = ((time / spec.Period) % 1 + 1) % 1;
        bool forward = phase < .5f;
        float progress = forward ? Math.Clamp((phase - .12f) / .28f, 0, 1) : 1 - Math.Clamp((phase - .62f) / .28f, 0, 1);
        bool walking = phase is > .12f and < .40f or > .62f and < .90f;
        float yaw = walking ? TrackData.YawFor((spec.B - spec.A) * (forward ? 1 : -1)) : spec.Yaw;
        return (spec.A.Lerp(spec.B, progress), yaw, walking);
    }

    public static float SawStroke(float time) => time % 10 < 7 ? MathF.Sin(time * MathF.Tau / 1.6f) : 0;

    public void Tick(float dt, CarSim car = null)
    {
        if (_workers.Count == 0) return;
        _clock += dt;
        _step += dt;
        if (_step < 1f / 12f) return;
        float step = _step;
        _step = 0;
        foreach (var worker in _workers)
        {
            var next = Route(worker.Spec, worker.Time + step);
            bool blocked = next.Walking && car != null && next.Position.DistanceTo(new Vector2(car.Pos.X, car.Pos.Z)) < 2.8f;
            if (!blocked) worker.Time += step;
            Place(worker, step, blocked);
        }
        foreach (var (model, rest, spec) in _saws)
            model.Position = rest + model.Basis.X * (.20f * SawStroke(_clock + spec.Phase));
    }

    void Place(Worker worker, float dt, bool blocked = false)
    {
        var (pos, yaw, walking) = Route(worker.Spec, worker.Time);
        Vector2 velocity = dt > 0 ? (pos - worker.Position) / dt : Vector2.Zero;
        worker.Position = pos;
        worker.Model.Position = Ground(_track, pos);
        worker.Model.Rotation = new Vector3(0, yaw, 0);
        worker.Model.Walking = walking && !blocked;
        float journey = ((worker.Time / worker.Spec.Period) % 1 + 1) % 1;
        worker.Model.Carrying = worker.Spec.A == worker.Spec.B || journey is >= .08f and < .46f;
        var signal = _jets?.GroundSignal() ?? (Moving: false, Waiting: false);
        worker.Model.Animate(worker.Spec.Job == WinterJob.Saw ? _clock + worker.Spec.Phase : worker.Time, signal.Moving, signal.Waiting);
        _track.Obstacles[worker.Obstacle] = new Obstacle(pos, BodyRadius, velocity);
    }

    static Vector3 Ground(TrackData t, Vector2 p) => new(p.X, t.MeshHeight(p.X, p.Y), p.Y);

    static Node3D SharedSaw()
    {
        var n = new Node3D();
        n.AddChild(Props.Part(Props.Box(2.25f, .16f, .045f), new Color(.7f, .72f, .68f), Vector3.Zero));
        for (int i = 0; i < 16; i++)
            n.AddChild(Props.Part(Props.Box(.07f, .07f, .05f), new Color(.45f, .48f, .46f), new Vector3(-1.03f + i * .137f, -.10f, 0), new Vector3(0, 0, 45)));
        foreach (float s in new[] { -1f, 1f })
            n.AddChild(Props.Part(Props.Box(.12f, .43f, .12f), new Color(.35f, .21f, .12f), new Vector3(s * 1.18f, .08f, 0)));
        DrunkTractor.BatchStatic(n, null);
        return n;
    }
}
