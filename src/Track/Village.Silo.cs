using System;
using Godot;

namespace CCCPRacing.Track;

public static partial class Village
{
    /// <summary>Ten concrete facets, with construction courses, runoff and damp at the foot.</summary>
    public static MeshInstance3D WeatheredSiloBody()
    {
        using var surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);
        Vector3 Corner(int i) => new(2.4f * MathF.Sin(i * MathF.Tau / 10), 0,
            2.4f * MathF.Cos(i * MathF.Tau / 10));
        Color Tone(int column, int row)
        {
            float y = (row + 0.5f) * 0.5f;
            float variation = Noise.Hash01(column / 4, row / 4, 318) * 0.045f;
            Color color = new(0.59f + variation, 0.58f + variation, 0.53f + variation);
            float dampHeight = 0.65f + Noise.Hash01(column, 0, 319) * 0.65f;
            if (y < dampHeight) color = color.Lerp(new Color(0.32f, 0.34f, 0.25f), 0.65f);
            // Broken runs begin beneath upper joints, rather than covering every face equally.
            float streak = Noise.Hash01(column, 0, 320);
            if (streak > 0.66f && y > 2f + streak * 4f && y < 10.6f)
                color = color.Darkened(0.12f + (streak - 0.66f) * 0.22f);
            if ((column == 7 || column == 8) && row >= 7 && row <= 9)
                color = new Color(0.52f, 0.51f, 0.46f); // a repaired concrete patch
            if (y > 8.7f && y < 9.3f)
                color = Noise.Hash01(column, row, 321) > 0.83f ? color
                    : new Color(0.46f, 0.20f, 0.15f); // worn paint, interrupted by exposed concrete
            return color.SrgbToLinear();
        }
        for (int face = 0; face < 10; face++)
        {
            Vector3 a = Corner(face), b = Corner(face + 1);
            Vector3 normal = ((a + b) / 2).Normalized();
            for (int col = 0; col < 4; col++)
            for (int row = 0; row < 24; row++)
            {
                Vector3 left = a.Lerp(b, col / 4f), right = a.Lerp(b, (col + 1) / 4f);
                Vector3 p = left + Vector3.Up * (row * 0.5f), q = right + Vector3.Up * (row * 0.5f);
                surface.SetNormal(normal);
                surface.SetColor(Tone(face * 4 + col, row));
                surface.AddVertex(p); surface.AddVertex(q + Vector3.Up * 0.5f); surface.AddVertex(q);
                surface.AddVertex(p); surface.AddVertex(p + Vector3.Up * 0.5f); surface.AddVertex(q + Vector3.Up * 0.5f);
            }
        }
        return new MeshInstance3D
        {
            Name = "WeatheredConcrete", Mesh = surface.Commit(),
            MaterialOverride = new StandardMaterial3D
            {
                VertexColorUseAsAlbedo = true, Roughness = 1,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.PerVertex,
                SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    public static MeshInstance3D SiloLoadingApron(TrackData track, Vector3 origin)
    {
        using var surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);
        // Small terrain-following worn apron, with a dusty patch below the discharge pipe.
        float tile = track.Tile;
        float x0 = MathF.Floor((origin.X - 4) / tile) * tile;
        float z0 = MathF.Floor((origin.Z + 1) / tile) * tile;
        Vector2 centre = new(origin.X - 0.7f, origin.Z + 3.1f);
        float Coverage(Vector2 p) => 1 - Mathf.SmoothStep(1.8f, 3.1f,
            p.DistanceTo(centre) + MathF.Sin(p.X * 2.1f + p.Y) * 0.12f);
        void Vertex(Vector2 p)
        {
            float dust = 1 - Mathf.SmoothStep(0.35f, 1.5f, p.DistanceTo(centre + new Vector2(-0.55f, -0.5f)));
            Color c = new Color(0.38f, 0.31f, 0.21f).Lerp(new Color(0.58f, 0.47f, 0.27f), dust * 0.6f).SrgbToLinear();
            c.A = Coverage(p);
            surface.SetColor(c);
            surface.SetNormal(Vector3.Up);
            surface.AddVertex(new Vector3(p.X, track.MeshHeight(p.X, p.Y) + 0.035f, p.Y) - origin);
        }
        int divisions = (int)MathF.Ceiling(tile / 0.5f);
        for (float z = z0; z <= origin.Z + 7; z += tile)
        for (float x = x0; x <= origin.X + 4; x += tile)
        {
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
            Vector2 at = new(x, z);
            Subdivide(at, at + new Vector2(tile, 0), at + new Vector2(tile, tile));
            Subdivide(at, at + new Vector2(tile, tile), at + new Vector2(0, tile));
        }
        return new MeshInstance3D
        {
            Name = "GrainDustApron", Mesh = surface.Commit(),
            MaterialOverride = new StandardMaterial3D
            {
                VertexColorUseAsAlbedo = true, Roughness = 1,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }
}
