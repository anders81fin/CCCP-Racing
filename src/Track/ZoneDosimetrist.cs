using System;
using System.Collections.Generic;
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
    Node3D _figure;
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
        d.AddChild(d._figure);
        d._rest = d._figure.Transform;
        d._obstacle = t.Obstacles.Count;
        t.Obstacles.Add(new Obstacle(new Vector2(d._figure.Position.X, d._figure.Position.Z), 0.4f * Atomgrad.DosimetristScale, default, false, 0.5f, true, 2.5f));     // a man: 9 km/h off the car
        d._down.Bind(t, d._obstacle);
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
    public void Reset() => _down.Reset();

    public override void _Process(double delta)
    {
        _down.Tick((float)delta);
        _figure.Transform = new Transform3D(_down.Tilt * _rest.Basis, _rest.Origin);
    }
}
