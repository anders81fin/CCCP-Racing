using System;
using System.Collections.Generic;
using CCCPRacing.Race;
using CCCPRacing.Car;
using Godot;

namespace CCCPRacing.Track;

/// <summary>
/// Standing figures the car knocks over (Knockdown): each falls away from the car, lies a while and
/// gets up. RaceScene registers Knocks() with Breakables and resets them at a run's start. A figure
/// may walk about (Move: e.g. DachaPatrol), its obstacle going with it.
/// </summary>
public partial class KnockGroup : Node3D
{
    readonly List<(Node3D Node, Knockdown Down, Transform3D Pose, int Obstacle)> _figures = new();
    TrackData _t;
    readonly List<(int Index, Obstacle Was)> _tapeObstacles = new();
    /// <summary>The replay shows the figures' tape poses (place, turn, the knock-over's tilt, the legs): none of their own, and their obstacles are parked.</summary>
    public bool TapeDriven { get; private set; }

    /// <summary>A figure (already a child, posed) and its obstacle in t.Obstacles.</summary>
    public void Add(Node3D figure, TrackData t, int obstacle)
    {
        _t = t;
        var down = new Knockdown();
        if (obstacle >= 0) down.Bind(t, obstacle);
        Tape.Part(figure, $"{_figures.Count}/figure");      // the world tape (the group's owner marks the group's root, and what walks marks the legs)
        _figures.Add((figure, down, figure.Transform, obstacle));
    }

    public int Count => _figures.Count;
    public Node3D Figure(int i) => _figures[i].Node;
    /// <summary>Knocked over (lying or getting up): it does not walk on until it stands again.</summary>
    public bool IsDown(int i) => _figures[i].Down.Down;

    /// <summary>A standing figure's new place and facing; its obstacle follows.</summary>
    public void Move(int i, Transform3D pose)
    {
        var f = _figures[i];
        if (f.Down.Down) return;
        _figures[i] = (f.Node, f.Down, pose, f.Obstacle);
        f.Node.Transform = pose;
        if (f.Obstacle >= 0)
            _t.Obstacles[f.Obstacle] = _t.Obstacles[f.Obstacle] with { Center = new Vector2(pose.Origin.X, pose.Origin.Z) };
    }

    public IEnumerable<(int obstacle, Func<Vector3, bool> hit)> Knocks()
    {
        foreach (var f in _figures)
            if (f.Obstacle >= 0) yield return (f.Obstacle, f.Down.Hit);
    }

    public void ResetKnocks()
    {
        foreach (var f in _figures)
        {
            f.Down.Reset();
            f.Node.Transform = f.Pose;
        }
    }

    /// <summary>The replay's tape poses the figures (true), or lets go: they stand at their rest places again and their obstacles come back.</summary>
    public void SetTapeDriven(bool on)
    {
        if (TapeDriven == on) return;
        if (!on)
        {
            foreach (var (index, was) in _tapeObstacles) _t.Obstacles[index] = was;
            _tapeObstacles.Clear();
            TapeDriven = false;
            ResetKnocks();
            return;
        }
        ResetKnocks();
        foreach (var f in _figures)
            if (f.Obstacle >= 0) { _tapeObstacles.Add((f.Obstacle, _t.Obstacles[f.Obstacle])); _t.Obstacles[f.Obstacle] = new Obstacle(new Vector2(-1e4f, -1e4f), .1f); }
        TapeDriven = true;
    }

    public override void _Process(double delta)
    {
        if (TapeDriven) return;
        foreach (var f in _figures)
        {
            if (!f.Down.Down) continue;
            f.Down.Tick((float)delta);
            f.Node.Transform = new Transform3D(f.Down.Tilt * f.Pose.Basis, f.Pose.Origin);
        }
    }
}
