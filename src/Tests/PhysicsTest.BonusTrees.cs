using System;
using System.Linq;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    /// <summary>
    /// The Poligon's cleared ground (BonusTrees.cs): every spruce gone, a few pines only at the outer rim and never about the start, the camp or the ridge, the birches kept; the obstacle ids unchanged (the removed trees'
    /// slots parked away, so a world tape's broken-tree ids still name the trees they did); the remaining trees' obstacles on the trees; the far trees mostly broadleaf; every other track exactly as it was.
    /// </summary>
    void BonusTreeTest()
    {
        var def = TrackDef.Load("res://tracks/bonus.json");
        bool was = BonusTrees.Enabled;
        BonusTrees.Enabled = false; var old = TrackBuilder.Build(def);
        BonusTrees.Enabled = true; var cleared = TrackBuilder.Build(TrackDef.Load("res://tracks/bonus.json"));
        BonusTrees.Enabled = was;
        bool Tree(PropInstance p) => p.Type is "birch" or "spruce" or "pine" or "redpine";
        int Count(TrackData t, string type) => t.Props.Count(p => p.Type == type);
        GD.Print($"bonus trees before: birch {Count(old, "birch")}, pine {Count(old, "pine")}, spruce {Count(old, "spruce")}; after: birch {Count(cleared, "birch")}, pine {Count(cleared, "pine")}, spruce {Count(cleared, "spruce")}; cleared {cleared.ClearedTrees.Count}");
        Check(Count(old, "spruce") > 20 && Count(old, "pine") > 20, "the old Poligon has its spruces and pines (the baseline)");
        Check(Count(cleared, "spruce") == 0, "no spruce is left on the Poligon");
        Check(Count(cleared, "birch") == Count(old, "birch"), $"every birch stays ({Count(cleared, "birch")})");
        int pines = Count(cleared, "pine");
        Check(pines >= 2 && pines <= 8, $"a few pines remain ({pines} of {Count(old, "pine")})");
        float Edge(Vector3 p) => MathF.Min(MathF.Min(p.X, p.Z), MathF.Min(cleared.WorldSize - p.X, cleared.WorldSize - p.Z));
        var zones = new BonusTrees(cleared);
        Check(cleared.Props.Where(p => p.Type == "pine").All(p => Edge(p.Position) <= BonusTrees.RimWidth), "the pines that remain stand at the outer rim");
        Check(cleared.Props.Where(p => p.Type == "pine").All(p => !zones.InFreeZone(new Vector2(p.Position.X, p.Position.Z))), "none of them is about the start straight, the camp or the ridge");
        var start = new Vector2(cleared.StartPos.X, cleared.StartPos.Z);
        Check(!cleared.Props.Any(p => p.Type is "pine" or "spruce" && new Vector2(p.Position.X, p.Position.Z).DistanceTo(start) < BonusTrees.StartFree) && !cleared.Props.Any(p => p.Type is "pine" or "spruce" && BonusTrees.InCampZone(new Vector2(p.Position.X, p.Position.Z))), "no conifer within 60 m of the start line or in the camp's zone");
        // every other prop is where it was (the builder drew the same numbers), only the trees that were left out are missing
        var oldOther = old.Props.Where(p => !Tree(p)).Select(p => (p.Type, MathF.Round(p.Position.X, 2), MathF.Round(p.Position.Z, 2))).ToList();
        var newOther = cleared.Props.Where(p => !Tree(p)).Select(p => (p.Type, MathF.Round(p.Position.X, 2), MathF.Round(p.Position.Z, 2))).ToList();
        Check(oldOther.SequenceEqual(newOther), $"everything that is not a tree is exactly where it was ({newOther.Count} props)");
        Check(old.Bushes.SequenceEqual(cleared.Bushes) && old.Tufts.SequenceEqual(cleared.Tufts), $"the bushes ({cleared.Bushes.Count}) and tufts ({cleared.Tufts.Count}) are where they were");
        // the obstacles: the ids are stable, the removed trees' slots parked away, the others on their trees
        Check(old.Obstacles.Count == cleared.Obstacles.Count, $"the obstacle list is as long as before ({cleared.Obstacles.Count}): the ids of the felled trees in an older tape still name the same trees");
        int parked = 0, same = 0, bad = 0;
        for (int i = 0; i < old.Obstacles.Count; i++)
        {
            var a = old.Obstacles[i]; var b = cleared.Obstacles[i];
            if (a.Equals(b)) same++; else if (b.Center.X < -9000f) parked++; else bad++;
        }
        Check(bad == 0 && parked == cleared.ClearedTrees.Count - 0 && same + parked == old.Obstacles.Count, $"an obstacle is either as it was ({same}) or parked away for a tree left out ({parked} of {cleared.ClearedTrees.Count}); none moved ({bad})");
        bool onTrees = true; int checkedTrees = 0;
        foreach (var (prop, obstacle) in cleared.PropObstacle)
        {
            var p = cleared.Props[prop]; if (!Tree(p)) continue;
            checkedTrees++; var o = cleared.Obstacles[obstacle];
            onTrees &= o.Radius > 0.12f && o.Center.DistanceTo(new Vector2(p.Position.X, p.Position.Z)) < 0.02f && old.Obstacles[obstacle].Equals(o);
        }
        Check(onTrees && checkedTrees == Count(cleared, "birch") + pines, $"the {checkedTrees} remaining trees each have their own obstacle, on the tree, the same as before");
        Check(cleared.ClearedTrees.All(c => cleared.Obstacles.All(o => o.Center.X < -9000f || o.Center.DistanceTo(c) > 0.05f || o.Radius < 0.2f)), "no obstacle is left where a tree was left out");
        // the cleared ground is grass underneath (grip unchanged) and is drawn as earth
        var map = new DryGrassMap(cleared); bool grass = true, earth = true;
        foreach (var c in cleared.ClearedTrees)
        {
            int ix = (int)(c.X / cleared.Tile), iz = (int)(c.Y / cleared.Tile);
            grass &= cleared.TileSurface(ix, iz) == old.TileSurface(ix, iz) && cleared.Sample(c.X, c.Y).Surface == old.Sample(c.X, c.Y).Surface;
            if (cleared.TileSurface(ix, iz) == SurfaceType.Grass) earth &= map.At(ix, iz, (int)SurfaceType.Grass) > 0.97f;
        }
        Check(grass, "the ground under the cleared trees has the surface it had (grip untouched)");
        Check(earth, "and its grass tiles are drawn as bare earth (dry amount 1)");
        // where the cleared ground is, near the start and the ridge (for the review's close-up pictures)
        var ridgePoint = PoligonDust.RidgeOf(cleared, PoligonDust.WindOf(cleared) ?? Vector2.Right) is int ri and >= 0 ? cleared.Center[ri] : Vector2.Zero;
        foreach (var (label, at) in new[] { ("start", start), ("ridge", ridgePoint) })
        {
            var near = cleared.ClearedTrees.Select(c => (c, d: c.DistanceTo(at))).OrderBy(x => x.d).Take(4);
            foreach (var (c, d) in near)
            {
                int ci = 0; float best = float.MaxValue; for (int i = 0; i < cleared.Center.Length; i++) { float dd = cleared.Center[i].DistanceTo(c); if (dd < best) { best = dd; ci = i; } }
                GD.Print($"cleared near {label}: spot ({c.X:0},{c.Y:0}) {d:0} m from it, nearest road index {ci}, {best:0.0} m from the road");
            }
        }
        // the far trees
        int far = 0, conifers = 0;
        for (float z = -110; z < cleared.WorldSize + 110; z += 9) for (float x = -110; x < cleared.WorldSize + 110; x += 9) { far++; if (CCCPRacing.Track.Noise.Hash01((int)x, (int)z, 138) > WorldBackdrop.ConiferThreshold(cleared)) conifers++; }
        BonusTrees.Enabled = false; float oldShare = (float)Enumerable.Range(0, 1).Select(_ => { int c2 = 0; for (float z = -110; z < cleared.WorldSize + 110; z += 9) for (float x = -110; x < cleared.WorldSize + 110; x += 9) if (CCCPRacing.Track.Noise.Hash01((int)x, (int)z, 138) > WorldBackdrop.ConiferThreshold(cleared)) c2++; return c2; }).First() / far; BonusTrees.Enabled = was;
        float share = (float)conifers / far;
        Check(share < 0.1f && oldShare > 0.35f, $"the far trees are broadleaf: conifers {share:P0} of the backdrop (were {oldShare:P0})");
        // every other track is exactly as it was: no clearing is made for it
        var ilyich = TrackBuilder.Build(TrackDef.Load("res://tracks/ilyich.json"));
        Check(ilyich.ClearedTrees.Count == 0 && WorldBackdrop.ConiferThreshold(ilyich) == 0.55f && ilyich.Props.Count(p => p.Type == "spruce") > 50, "Ilyich keeps all its trees and its backdrop");
    }
}
