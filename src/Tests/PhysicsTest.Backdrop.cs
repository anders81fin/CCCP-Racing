using System;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    void WorldBackdropTest()
    {
        GD.Print("Outer landscape: seamless heights, bounded static geometry and day/night tint");
        foreach (string id in new[] { "ilyich", "siberia", "atomgrad", "bonus" })
        {
            var t = TrackBuilder.Build(TrackDef.Load($"res://tracks/{id}.json"));
            var heights = (float[])t.Heights.Clone();
            int obstacles = t.Obstacles.Count;
            string signature = TrackSignature.Compute(t);
            var terrain = TerrainMesh.Build(t);
            // Local bounds prevent distant lights consuming the terrain's per-mesh light budget.
            var patches = new System.Collections.Generic.List<MeshInstance3D> { terrain };
            foreach (var child in terrain.GetChildren())
                if (child is MeshInstance3D patch && patch.Name.ToString().StartsWith("TerrainChunk")) patches.Add(patch);
            int terrainVertices = 0;
            bool bounded = true, groundMatches = true;
            foreach (var patch in patches)
            {
                var bounds = patch.Mesh.GetAabb();
                bounded &= bounds.Size.X <= 32.001f && bounds.Size.Z <= 32.001f;
                var points = patch.Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                terrainVertices += points.Length;
                foreach (var point in points)
                {
                    Vector3 world = point + patch.Position;
                    groundMatches &= MathF.Abs(world.Y - t.MeshHeight(world.X, world.Z)) < 0.01f;
                }
            }
            Check(bounded && groundMatches && terrainVertices == t.N * t.N * 6,
                $"{id}: complete terrain retains driving heights with local light bounds");
            var day = terrain.Mesh.SurfaceGetMaterial(0);
            var night = TerrainMesh.NightMaterial(((ShaderMaterial)day).GetShaderParameter("atlas").As<Texture2D>());
            TerrainMesh.SetLightingMaterial(terrain, night);
            bool allNight = patches.TrueForAll(patch => patch.MaterialOverride == night);
            TerrainMesh.SetLightingMaterial(terrain, null);
            Check(allNight && patches.TrueForAll(patch => patch.MaterialOverride == null && patch.Mesh.SurfaceGetMaterial(0) == day),
                $"{id}: every terrain patch switches to night lighting and back to day");
            var backdrop = terrain.GetNode<WorldBackdrop>("WorldBackdrop");
            var ground = backdrop.GetNode<MeshInstance3D>("OuterTerrain");
            var vertices = ground.Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            if (id == "siberia")
            {
                var uv = ground.Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.TexUV].AsVector2Array();
                foreach (int end in new[] { 0, 1 })
                {
                    var river = t.Water.Rivers[0];
                    int e = end == 0 ? 0 : river.Points.Length - 1, next = end == 0 ? 1 : e - 1;
                    var origin = new Vector2(river.Points[e][0], river.Points[e][1]);
                    var direction = (origin - new Vector2(river.Points[next][0], river.Points[next][1])).Normalized();
                    bool iceContinues = true, banks = true;
                    foreach (float distance in new[] { 4f, 16f, 48f, 100f, 160f })
                    {
                        var p = origin + direction * distance;
                        float nearest = float.MaxValue;
                        SurfaceType surface = SurfaceType.DeepSnow;
                        for (int j = 0; j < vertices.Length; j += 3)
                        {
                            Vector3 centre = (vertices[j] + vertices[j + 1] + vertices[j + 2]) / 3;
                            float d = p.DistanceSquaredTo(new Vector2(centre.X, centre.Z));
                            if (d >= nearest) continue;
                            nearest = d; surface = (SurfaceType)(int)uv[j].X;
                        }
                        iceContinues &= nearest < 16 && surface == SurfaceType.Ice;
                        var side = new Vector2(-direction.Y, direction.X) * 38;
                        banks &= WorldBackdrop.HeightAt(t, p + side) > WorldBackdrop.HeightAt(t, p) + 1
                            && WorldBackdrop.HeightAt(t, p - side) > WorldBackdrop.HeightAt(t, p) + 1;
                    }
                    Check(iceContinues && banks, $"Siberia river end {end}: ice and raised banks continue 160 m beyond the map");
                }
            }
            bool valid = true, stitched = true, outside = true;
            foreach (var p in vertices)
            {
                valid &= float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);
                outside &= !(p.X > 0 && p.X < t.WorldSize && p.Z > 0 && p.Z < t.WorldSize);
                if (WorldBackdrop.OutsideDistance(t, new Vector2(p.X, p.Z)) == 0)
                    stitched &= MathF.Abs(p.Y - t.MeshHeight(p.X, p.Z)) < 0.001f;
            }
            bool upward = true;
            for (int i = 0; i < vertices.Length; i += 3)
                upward &= (vertices[i + 2] - vertices[i]).Cross(vertices[i + 1] - vertices[i]).Y >= -0.001f;
            GD.Print($"  {id}: {vertices.Length / 3} outer terrain triangles, {backdrop.GetChildCount()} static meshes");
            Check(valid && stitched && outside && upward, $"{id}: outer terrain meets the complete border without covering the playable grid");
            Check(ground.Mesh.GetAabb().Position.X <= -WorldBackdrop.Extent && ground.Mesh.GetAabb().End.X >= t.WorldSize + WorldBackdrop.Extent,
                $"{id}: landscape covers both near and distant views outside the map");
            bool untouched = t.Obstacles.Count == obstacles && TrackSignature.Compute(t) == signature;
            for (int i = 0; i < heights.Length; i++) untouched &= heights[i] == t.Heights[i];
            Check(untouched, $"{id}: background does not change driving heights, obstacles or track records");
            backdrop.SetLook(new Color(0.12f, 0.15f, 0.20f), new Color(0.02f, 0.03f, 0.05f), 1);
            bool tinted = true;
            foreach (var child in backdrop.GetChildren())
                if (child is MeshInstance3D mesh && mesh.Name != "OuterRiver")
                    tinted &= ((ShaderMaterial)mesh.MaterialOverride).GetShaderParameter("day_tint").AsVector3().X < 0.13f
                        && ((ShaderMaterial)mesh.MaterialOverride).GetShaderParameter("lit_mix").AsSingle() == 1;
            Check(backdrop.GetChildCount() is >= 2 and <= 3 && vertices.Length / 3 <= 45000 && tinted,
                $"{id}: two or three static meshes, bounded terrain triangles, and night tint on both");
            terrain.Free();
        }
    }
}
