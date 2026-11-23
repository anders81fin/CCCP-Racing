using System;
using Godot;

namespace CCCPRacing.Track;

/// <summary>
/// The Poligon's trees (the owner's order, 2026-11-23): a dry, windy training ground, not a forest. Every spruce of the playable area is left out; the pines stay only as a few, sparse ones at the outer rim
/// of the ground and never about the start straight, the camp or the open gravel ridge; the birches (and the other trees the builder places) stay. The builder still draws every random number it drew before and still reserves the
/// spot, so everything it places after the trees lands where it did; a tree that is left out only does not exist. Its obstacle is NOT removed from the list but parked away (the way the Hinds' spare slots are,
/// at -10000), so that the ids of every other obstacle stay as they were: the broken-tree ids of an older world tape (section 19 of docs/replay-tape.md) still name the same trees, and an id of a tree that is
/// gone names nothing (Breakables.ReplaySet ignores it). Where a tree was left out the ground is shown as earth and hay (TerrainMesh's dry grass, PoligonVegetation's hay); nothing about the ground's surface
/// or grip changes.
/// </summary>
public sealed class BonusTrees
{
    /// <summary>Radius round the start line, round the camp and round the ridge kept free of pines (metres).</summary>
    public const float StartFree = 60f, CampMargin = 30f, RidgeFree = 62f;
    /// <summary>How near the map's edge a pine may stand (metres), and the share of those that do.</summary>
    public const float RimWidth = 30f, RimShare = 0.4f;

    /// <summary>Master switch: the cleared ground (default) or the trees as they were (the review's before pictures: --poligon-old-trees, and the test that builds both worlds).</summary>
    public static bool Enabled = Array.IndexOf(OS.GetCmdlineUserArgs(), "--poligon-old-trees") < 0;

    readonly TrackData _t;
    readonly Vector2 _start, _ridge;
    readonly bool _hasRidge;
    public int Spruces, Pines, KeptPines, Birches, Cleared;

    public BonusTrees(TrackData t)
    {
        _t = t;
        _start = new Vector2(t.StartPos.X, t.StartPos.Z);
        if (PoligonDust.WindOf(t) is { } wind && PoligonDust.RidgeOf(t, wind) is int r and >= 0) { _ridge = t.Center[r]; _hasRidge = true; }
    }

    /// <summary>The camp and the strip in front of it.</summary>
    public static bool InCampZone(Vector2 p)
    {
        var b = BonusCampLayout.ProtectedBounds;
        return p.X > b.Position.X - CampMargin && p.X < b.End.X + CampMargin && p.Y > b.Position.Y - CampMargin && p.Y < b.End.Y + CampMargin;
    }

    /// <summary>The start straight's surroundings, the camp's front and the open ridge: no pines there, rim or not.</summary>
    public bool InFreeZone(Vector2 p) => p.DistanceTo(_start) < StartFree || InCampZone(p) || (_hasRidge && p.DistanceTo(_ridge) < RidgeFree);

    /// <summary>True for a pine that stands at the outer rim (a handful of them, spread by a hash of the place).</summary>
    public bool RimPine(Vector2 p)
    {
        float edge = MathF.Min(MathF.Min(p.X, p.Y), MathF.Min(_t.WorldSize - p.X, _t.WorldSize - p.Y));
        return edge <= RimWidth && Noise.Hash01((int)MathF.Floor(p.X), (int)MathF.Floor(p.Y), 5101) < RimShare && !InFreeZone(p);
    }

    /// <summary>Should the builder leave this tree out? (Counts what it sees.)</summary>
    public bool Drops(string kind, Vector2 pos)
    {
        switch (kind)
        {
            case "spruce": Spruces++; Cleared++; return true;
            case "pine": Pines++; if (RimPine(pos)) { KeptPines++; return false; } Cleared++; return true;
            case "birch": Birches++; return false;
            default: return false;
        }
    }

    /// <summary>Leaves the tree out and keeps every id as it was: the spot stays reserved, the obstacle slot is parked away, the place is recorded for the ground.</summary>
    public void LeaveOut(string kind, Vector2 pos)
    {
        _t.ClearedTrees.Add(pos);
        if (kind is "birch" or "spruce" or "pine" or "redpine") _t.Obstacles.Add(new Obstacle(new Vector2(-10000f, -10000f), 0.1f));
    }
}
