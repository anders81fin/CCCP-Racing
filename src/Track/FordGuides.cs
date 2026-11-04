using System;
using Godot;

namespace CCCPRacing.Track;

/// <summary>Quiet physical cues for Ilyich's shallow outside line, with no change to driving surfaces.</summary>
public static class FordGuides
{
    public static Node3D Build(TrackData t)
    {
        var root = new Node3D { Name = "Ford guides" };
        if (t.Id != "ilyich" || t.FordIndex < 0 || float.IsNaN(t.WaterLevel)) return root;

        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        int triangles = 0;
        void Tri(Vector3 a, Vector3 b, Vector3 c, Color color)
        {
            st.SetColor(color); st.SetNormal(Vector3.Up); st.AddVertex(a);
            st.SetColor(color); st.SetNormal(Vector3.Up); st.AddVertex(b);
            st.SetColor(color); st.SetNormal(Vector3.Up); st.AddVertex(c);
            triangles++;
        }
        Vector3 Bed(Vector2 p) => new(p.X, t.MeshHeight(p.X, p.Y) + 0.018f, p.Y);

        // Small broken patches lighten the real shallow gravel, leaving deep water untouched.
        Vector2 ford = t.Center[t.FordIndex];
        for (int z = 0; z < t.N; z++)
        for (int x = 0; x < t.N; x++)
        {
            var p = new Vector2((x + 0.5f) * t.Tile, (z + 0.5f) * t.Tile);
            if (p.DistanceSquaredTo(ford) > 18f * 18f || t.TileSurface(x, z) != SurfaceType.Shallow) continue;
            int ci = t.NearestCenter(p, out float distance);
            if (distance > t.HalfWidth[ci] - 0.3f) continue;
            float depth = t.WaterLevel - t.MeshHeight(p.X, p.Y);
            if (depth < 0.025f || depth > 0.22f) continue;
            float tint = Noise.Hash01(x, z, 731);
            var color = new Color(0.61f + tint * 0.08f, 0.56f + tint * 0.07f, 0.43f + tint * 0.06f);
            float radius = t.Tile * (0.4f + tint * 0.07f);
            var centre = Bed(p);
            for (int k = 0; k < 6; k++)
            {
                float a = k * MathF.Tau / 6f, b = (k + 1) * MathF.Tau / 6f;
                Vector2 q = p + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius;
                Vector2 r = p + new Vector2(MathF.Cos(b), MathF.Sin(b)) * radius;
                if (t.WaterLevel - t.MeshHeight(q.X, q.Y) > 0.24f ||
                    t.WaterLevel - t.MeshHeight(r.X, r.Y) > 0.24f) continue;
                Tri(centre, Bed(r), Bed(q), color);
            }
        }

        void Box(Vector3 size, Vector3 position, float yaw, Color color)
        {
            var arrays = DrunkTractor.PrimitiveArrays(Props.Box(size.X, size.Y, size.Z));
            var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
            var transform = new Transform3D(new Basis(Vector3.Up, yaw), position);
            for (int k = 0; k < indices.Length; k += 3)
                Tri(transform * vertices[indices[k]], transform * vertices[indices[k + 1]],
                    transform * vertices[indices[k + 2]], color);
        }

        int n = t.Center.Length, fi = t.FordIndex;
        float outside = t.CenterDir((fi - 8 + n) % n).Cross(t.CenterDir((fi + 8) % n)) >= 0 ? -1f : 1f;
        // Three modest white-tipped timber stakes trace the outside bank, clear of the road.
        foreach (int offset in new[] { -11, 0, 11 })
        {
            int ci = (fi + offset + n) % n;
            Vector2 direction = t.CenterDir(ci), right = new(-direction.Y, direction.X);
            Vector2 p = t.Center[ci] + right * outside * (t.HalfWidth[ci] + 1.6f);
            float ground = Math.Max(t.MeshHeight(p.X, p.Y), t.WaterLevel - 0.12f);
            var at = new Vector3(p.X, ground, p.Y);
            float yaw = TrackData.YawFor(direction);
            Box(new Vector3(0.16f, 1.15f, 0.16f), at + Vector3.Up * 0.475f, yaw, new Color(0.36f, 0.27f, 0.17f));
            Box(new Vector3(0.175f, 0.36f, 0.175f), at + Vector3.Up * 0.86f, yaw, new Color(0.88f, 0.85f, 0.7f));
        }

        if (triangles > 0)
        {
            var material = new StandardMaterial3D
            {
                VertexColorUseAsAlbedo = true, ShadingMode = BaseMaterial3D.ShadingModeEnum.PerVertex,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled, Roughness = 1f,
                SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
            };
            st.GenerateNormals();
            var mesh = st.Commit();
            mesh.SurfaceSetMaterial(0, material);
            root.AddChild(new MeshInstance3D { Mesh = mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        }
        return root;
    }
}
