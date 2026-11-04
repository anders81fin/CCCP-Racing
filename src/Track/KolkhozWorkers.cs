using System.Collections.Generic;
using Godot;

namespace CCCPRacing.Track;

public enum KolkhozJob { Reap, Pitchfork, CarrySack, Load, Foreman, Rest, Fish }

public readonly record struct KolkhozWorkerSpec(Vector2 Position, float Yaw, KolkhozJob Job, int Variant = 0, float Phase = 0);

/// <summary>Small work crews on Ilyich: one animation clock for the entire workforce.</summary>
public partial class KolkhozWorkers : Node3D
{
    // Match the cartoon scale used by Babushkas and DrunkMen.
    public const float SizeScale = 1.6f;
    public const float BodyRadius = 0.4f * SizeScale;

    readonly List<(KolkhozWorkerModel Model, KolkhozWorkerSpec Spec)> _crew = new();
    // Hit by the car a worker falls over and gets up again (Knockdown): his rest pose, his obstacle.
    readonly List<(Knockdown Down, Transform3D Pose, int Obstacle)> _down = new();
    float _clock, _step;
    Vector3 _prevCar;
    bool _hasPrevCar;
    float _carKmh;

    /// <summary>The scenic school camera overlooks the whole kolkhoz, so do not cull its workers by distance.</summary>
    public bool ScenicActive { get; set; }

    public static List<KolkhozWorkerSpec> Layout(TrackData t)
    {
        var all = new List<KolkhozWorkerSpec>();
        if (t.Id != "ilyich") return all;
        all.AddRange(FieldCrew(t));
        all.AddRange(LoadingCrew(t));
        all.AddRange(FishingCrew(t));
        return all;
    }

    static IEnumerable<KolkhozWorkerSpec> FishingCrew(TrackData t)
    {
        // Two quiet fishing spots on each side of the ford, on both banks.
        // Keep the road and the whole crossing apron clear.
        yield return new(new Vector2(241.3f, 225), 2.4f, KolkhozJob.Fish, 0, 0);
        yield return new(new Vector2(232.5f, 233.6f), -0.74f, KolkhozJob.Fish, 2, 1.7f);
        yield return new(new Vector2(279, 261), 2.4f, KolkhozJob.Fish, 4, 3.4f);
        yield return new(new Vector2(267.5f, 267.6f), -0.74f, KolkhozJob.Fish, 2, 5.1f);
    }

    public static KolkhozWorkers Build(TrackData t)
    {
        var root = new KolkhozWorkers { Name = "KolkhozWorkers" };
        foreach (var spec in Layout(t))
        {
            var model = KolkhozWorkerModel.Build(spec.Job, spec.Variant, (t.WaterLevel - t.MeshHeight(spec.Position.X, spec.Position.Y)) / SizeScale, root._crew.Count);
            model.Scale = Vector3.One * SizeScale;
            model.Position = new Vector3(spec.Position.X, t.MeshHeight(spec.Position.X, spec.Position.Y), spec.Position.Y);
            model.Rotation = new Vector3(0, spec.Yaw, 0);
            model.Animate(spec.Phase);
            root.AddChild(model);
            root._crew.Add((model, spec));
            int obstacle = root._crew.Count - 1 < t.WorkerObstacles.Count ? t.WorkerObstacles[root._crew.Count - 1] : -1;
            var down = new Knockdown();
            if (obstacle >= 0) down.Bind(t, obstacle);
            root._down.Add((down, model.Transform, obstacle));
        }
        if (root._crew.Count == 0) root.SetProcess(false);
        return root;
    }

    /// <summary>The workers: what knocks them over (Breakables.AddKnock).</summary>
    public IEnumerable<(int obstacle, System.Func<Vector3, bool> hit)> Knocks()
    {
        foreach (var d in _down)
            if (d.Obstacle >= 0) yield return (d.Obstacle, d.Down.Hit);
    }

    /// <summary>A new run: everyone standing.</summary>
    public void ResetKnocks()
    {
        foreach (var d in _down) d.Down.Reset();
    }

    public override void _Process(double delta)
    {
        for (int i = 0; i < _down.Count; i++)
        {
            var d = _down[i];
            if (!d.Down.Down) { if (_crew[i].Model.Transform != d.Pose) _crew[i].Model.Transform = d.Pose; continue; }
            d.Down.Tick((float)delta);
            // A walker has moved off his obstacle's centre: he tips over about where he stands.
            Vector3 pivot = d.Pose.Basis * _crew[i].Model.Offset;
            Basis tilt = d.Down.Tilt;
            _crew[i].Model.Transform = new Transform3D(tilt * d.Pose.Basis, d.Pose.Origin + pivot - tilt * pivot);
        }
        _clock += (float)delta;
        _step += (float)delta;

        // The car's speed from its movement (a jump, like a restart, is not speed).
        Vector3 car = SeeThrough.Car;
        if (delta > 0)
        {
            var move = car - _prevCar;
            move.Y = 0;
            float ms = _hasPrevCar && move.Length() < 15f ? move.Length() / (float)delta : 0f;
            _carKmh = Mathf.Lerp(_carKmh, ms * 3.6f, 0.3f);
        }
        _prevCar = car;
        _hasPrevCar = true;

        if (_step < 1f / 24f) return;
        _step %= 1f / 24f;
        var camera = GetViewport().GetCamera3D();
        for (int i = 0; i < _crew.Count; i++)
        {
            var (model, spec) = _crew[i];
            if (!ScenicActive && camera != null && model.GlobalPosition.DistanceSquaredTo(camera.GlobalPosition) > 90f * 90f) continue;
            if (_down[i].Down.Down) continue;        // lying there: no walking on
            Vector3 to = car - _down[i].Pose.Origin;
            to.Y = 0;
            float dist = to.Length();
            float bearing = 0;
            if (dist < 30f)
            {
                // The car in the worker's own frame (he faces -Z at yaw 0, turned by his yaw).
                Vector3 local = new Basis(Vector3.Up, spec.Yaw).Inverse() * to;
                bearing = Mathf.Atan2(-local.X, -local.Z);
            }
            model.Animate(_clock + spec.Phase, dist, bearing, _carKmh);
        }
    }
}
