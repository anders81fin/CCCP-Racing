using System;
using System.Linq;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    void IlyichEdgesTest()
    {
        var t = TrackBuilder.Build(TrackDef.Load("res://tracks/ilyich.json"));
        var layout = Props.IlyichEdgeTrees(t).ToArray();
        Check(layout.SequenceEqual(Props.IlyichEdgeTrees(t)), "Ilyich edge tree layout is deterministic");
        int inside = 0;
        bool clear = true, solid = true;
        foreach (var tree in layout)
        {
            int index = t.Props.FindIndex(p => p.Type == tree.Kind &&
                new Vector2(p.Position.X, p.Position.Z).DistanceTo(tree.Position) < 0.01f);
            if (index < 0) continue;
            inside++;
            int ci = t.NearestCenter(tree.Position, out float distance);
            clear &= distance >= t.HalfWidth[ci] + 11.9f;
            solid &= t.PropObstacle.TryGetValue(index, out int obstacle) &&
                t.Obstacles[obstacle].Center.DistanceTo(tree.Position) < 0.01f;
        }
        Check(inside >= 10 && clear && solid, "Edge trees stand clear of racing lines and retain normal collisions");
        string signature = TrackSignature.Compute(t);
        var heights = (float[])t.Heights.Clone();
        var surfaces = (SurfaceType[])t.Surf.Clone();
        int obstacleCount = t.Obstacles.Count;
        var scenery = Props.IlyichEdgeScenery(t);
        Check(signature == TrackSignature.Compute(t) && heights.SequenceEqual(t.Heights)
            && surfaces.SequenceEqual(t.Surf) && t.Obstacles.Count == obstacleCount,
            "Backyards, reeds and outer woods do not alter driving ground or collisions");
        Check(scenery.GetChildCount() <= 20, "Edge scenery stays within twenty static batches");
        Check(scenery.FindChild("Northern hay shelter", false, false) != null
            && scenery.GetChildren().Count(n => n.Name.ToString().StartsWith("Northern field ")) == 4,
            "Northern edge includes four farm parcels and their hay shelter");
        bool finite = true;
        foreach (Node3D batch in scenery.GetChildren())
        foreach (MeshInstance3D mesh in batch.GetChildren())
        {
            var bounds = mesh.Mesh.GetAabb();
            finite &= bounds.Position.IsFinite() && bounds.Size.IsFinite();
        }
        Check(finite, "All edge scenery batches have finite bounds");
        scenery.Free();
        var backdrop = WorldBackdrop.Build(t, Enumerable.Repeat(1f, t.N * t.N).ToArray(), TileAtlas.Build());
        var ground = backdrop.GetNode<MeshInstance3D>("OuterTerrain").Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        bool onGround = true;
        for (int i = 0; i + 2 < ground.Length; i += 291)
        {
            Vector3 mid = (ground[i] + ground[i + 1] + ground[i + 2]) / 3;
            onGround &= MathF.Abs(WorldBackdrop.SurfaceHeightAt(t, new Vector2(mid.X, mid.Z)) - mid.Y) < 0.01f;
        }
        Check(onGround, "Field overlays follow rendered backdrop triangles rather than floating or sinking");
        backdrop.Free();
        foreach (string id in new[] { "siberia", "atomgrad", "bonus" })
        {
            var other = new TrackData { Id = id };
            var empty = Props.IlyichEdgeScenery(other);
            Check(empty.GetChildCount() == 0 && !Props.IlyichEdgeTrees(other).Any(), $"{id}: Ilyich additions stay disabled");
            empty.Free();
        }
    }
}
