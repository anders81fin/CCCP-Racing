using System;
using System.Collections.Generic;
using Godot;

namespace CCCPRacing.Track;

public static partial class Village
{
    // One continuous surface prevents the drive, turning area and footpath fighting at overlaps.
    static MeshInstance3D KolkhozFrontage(TrackData t, Vector3 origin, Basis basis, List<Vector2[]> paths)
    {
        float Distance(Vector2 p, Vector2[] path)
        {
            float distance = float.MaxValue;
            for (int i = 0; i + 1 < path.Length; i++)
            {
                var v = path[i + 1] - path[i];
                float u = Math.Clamp((p - path[i]).Dot(v) / v.LengthSquared(), 0, 1);
                distance = Math.Min(distance, p.DistanceTo(path[i] + v * u));
            }
            return distance;
        }
        float Coverage(Vector2 p)
        {
            float waviness = MathF.Sin(p.X * 1.13f + p.Y * 0.71f) * 0.10f
                + MathF.Sin(p.Y * 2.3f - p.X * 0.43f) * 0.06f;
            float result = 0;
            for (int i = 0; i < paths.Count; i++)
            {
                bool drive = i == paths.Count - 1 && paths.Count > 4;
                float inner = drive ? 2.0f : 0.65f, outer = drive ? 2.8f : 1.25f;
                float distance = Distance(p, paths[i]);
                // Wider worn landings at the foot of the three staircases.
                if (i < 3) distance = Math.Min(distance, Math.Max(0, p.DistanceTo(paths[i][0]) - 0.9f));
                result = Math.Max(result, 1 - Mathf.SmoothStep(inner, outer, distance + waviness));
            }
            return result;
        }
        Vector2 low = paths[0][0], high = low;
        foreach (var path in paths)
        foreach (var point in path) { low = low.Min(point); high = high.Max(point); }
        low -= Vector2.One * 4; high += Vector2.One * 4;
        low = (low / t.Tile).Floor() * t.Tile;
        var mesh = new SurfaceTool(); mesh.Begin(Mesh.PrimitiveType.Triangles);
        void Vertex(Vector2 p)
        {
            float tone = 0.94f + Noise.Hash01((int)(p.X * 4), (int)(p.Y * 4), 291) * 0.12f;
            mesh.SetColor(new Color(tone, tone, tone, Coverage(p)));
            mesh.SetUV(p / 3f); mesh.SetNormal(Vector3.Up);
            mesh.AddVertex(basis.Inverse() * (new Vector3(p.X, t.MeshHeight(p.X, p.Y) + 0.025f, p.Y) - origin));
        }
        // Subdivide the actual terrain triangles; no quad bridges a terrain diagonal.
        int divisions = (int)MathF.Ceiling(t.Tile / 0.5f);
        for (float z = low.Y; z <= high.Y; z += t.Tile)
        for (float x = low.X; x <= high.X; x += t.Tile)
        {
            var tileOrigin = new Vector2(x, z);
            // Match TerrainMesh's visible road tiles exactly: stop at their shared edge,
            // with no inset or alpha fade that would expose grass between the surfaces.
            var centre = tileOrigin + Vector2.One * (t.Tile * 0.5f);
            int road = t.NearestCenter(centre, out float roadDistance);
            if (roadDistance < t.HalfWidth[road]) continue;
            void Subdivide(Vector2 a, Vector2 b, Vector2 c)
            {
                Vector2 P(int i, int j) => a + (b - a) * (i / (float)divisions) + (c - a) * (j / (float)divisions);
                void Tri(Vector2 p, Vector2 q, Vector2 r)
                {
                    if (Math.Max(Coverage(p), Math.Max(Coverage(q), Coverage(r))) < 0.001f) return;
                    Vertex(p); Vertex(r); Vertex(q);
                }
                for (int i = 0; i < divisions; i++)
                for (int j = 0; j < divisions - i; j++)
                {
                    Tri(P(i, j), P(i + 1, j), P(i, j + 1));
                    if (i + j + 1 < divisions) Tri(P(i + 1, j), P(i + 1, j + 1), P(i, j + 1));
                }
            }
            Subdivide(tileOrigin, tileOrigin + new Vector2(t.Tile, 0), tileOrigin + new Vector2(t.Tile, t.Tile));
            Subdivide(tileOrigin, tileOrigin + new Vector2(t.Tile, t.Tile), tileOrigin + new Vector2(0, t.Tile));
        }
        // Fine packed earth and gravel, in the same warm palette as the village approaches.
        var image = Image.CreateEmpty(64, 64, false, Image.Format.Rgb8);
        for (int y = 0; y < 64; y++)
        for (int x = 0; x < 64; x++)
        {
            float n = Noise.Hash01(x, y, 923), fleck = Noise.Hash01(x, y, 924);
            var color = new Color(0.40f, 0.34f, 0.25f) * (0.85f + n * 0.30f);
            if (fleck > 0.90f) color = new Color(0.49f, 0.46f, 0.37f) * (0.9f + n * 0.2f);
            image.SetPixel(x, y, color);
        }
        return new MeshInstance3D
        {
            Name = "KolkhozFrontagePaths", Mesh = mesh.Commit(),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoTexture = ImageTexture.CreateFromImage(image), VertexColorUseAsAlbedo = true,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled, Roughness = 1,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }
}
