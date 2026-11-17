using System;
using System.Collections.Generic;
using Godot;

namespace CCCPRacing.Track;

public static partial class PartyDacha
{
    // One continuous surface prevents the drive, turning area and footpath fighting at overlaps.
    static IEnumerable<MeshInstance3D> YardSurfaceSteps(TrackData t)
    {
        var slice = System.Diagnostics.Stopwatch.StartNew();
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
        var walk = Walk[..4];
        float Coverage(Vector2 p)
        {
            // Gentle, irregular margins instead of ruler-straight strips and a perfect disk.
            float waviness = MathF.Sin(p.X * 1.13f + p.Y * 0.71f) * 0.12f
                + MathF.Sin(p.Y * 2.3f - p.X * 0.43f) * 0.07f;
            float drive = 1 - Mathf.SmoothStep(1.75f, 2.75f, Distance(p, Drive) + waviness);
            float turning = 1 - Mathf.SmoothStep(CourtR - 0.9f, CourtR + 0.3f, p.DistanceTo(CourtC));
            float foot = 1 - Mathf.SmoothStep(0.40f, 1.0f, Distance(p, walk) + waviness);
            float park = InParking(p, 0.4f) ? 1f : 0f;
            park = Math.Max(park, HouseRing(p));      // the paved walk right round the log house
            park = Math.Max(park, 1 - Mathf.SmoothStep(0.55f, 1.0f, Distance(p, BoothPath)));
            park = Math.Max(park, 1 - Mathf.SmoothStep(2.2f, 2.9f, Distance(p, ParkLink)));
            park = Math.Max(park, 1 - Mathf.SmoothStep(2.0f, 2.8f, Distance(p, PorchPath)));      // ...and from the court to the porch      // the link from the court to the car park      // the short paved path to the guard booth's door
            return Math.Max(Math.Max(drive, Math.Max(turning, foot)), park);
        }
        var mesh = new SurfaceTool(); mesh.Begin(Mesh.PrimitiveType.Triangles);
        void Vertex(Vector2 p)
        {
            float tone = 0.94f + Noise.Hash01((int)(p.X * 4), (int)(p.Y * 4), 291) * 0.12f;
            mesh.SetColor(new Color(tone, tone, tone, Coverage(p)));
            mesh.SetUV(p / 3f); mesh.SetNormal(Vector3.Up);
            mesh.AddVertex(new Vector3(p.X, t.MeshHeight(p.X, p.Y) + 0.035f, p.Y));
        }
        // Subdivide the actual terrain triangles; no quad bridges a terrain diagonal.
        int divisions = (int)MathF.Ceiling(t.Tile / 0.5f);
        for (float z = 160; z < 200; z += t.Tile)
        for (float x = 198; x < 292; x += t.Tile)
        {
            if (slice.ElapsedMilliseconds > 10) { yield return null; slice.Restart(); }
            var origin = new Vector2(x, z);
            // Match TerrainMesh's visible road tiles exactly: stop at their shared edge,
            // with no inset or alpha fade that would expose grass between the surfaces.
            var centre = origin + Vector2.One * (t.Tile * 0.5f);
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
            Subdivide(origin, origin + new Vector2(t.Tile, 0), origin + new Vector2(t.Tile, t.Tile));
            Subdivide(origin, origin + new Vector2(t.Tile, t.Tile), origin + new Vector2(0, t.Tile));
        }
        // Paving: running-bond granite setts, 0.75 x 0.37 m (16 x 8 px of 3 m / 64 px), each cut a slightly different grey or warm tone, a dark joint and a few specks.
        var image = Image.CreateEmpty(64, 64, false, Image.Format.Rgb8);
        yield return null;
        for (int y = 0; y < 64; y++)
        for (int x = 0; x < 64; x++)
        {
            int row = y / 8, off = (row % 2) * 8, bx = ((x + off) % 64) / 16, by = row;
            bool joint = (y % 8 == 0) || ((x + off) % 16 == 0);
            float tone = 0.8f + Noise.Hash01(bx, by, 931) * 0.28f;
            float warm = Noise.Hash01(bx, by, 932);
            var color = warm > 0.55f ? new Color(0.58f, 0.54f, 0.47f) : new Color(0.5f, 0.5f, 0.52f);
            color *= tone * (0.94f + Noise.Hash01(x, y, 933) * 0.12f);
            if (joint) color = new Color(0.2f, 0.19f, 0.17f);
            image.SetPixel(x, y, color);
        }
        yield return null;
        yield return new MeshInstance3D
        {
            Name = "PavedYard", Mesh = mesh.Commit(),
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

    /// <summary>The paved walk round the log house: the band between the footprint (with the veranda) and a rectangle 2.5 m out from it (south: up to the pond's edge), soft at both edges.</summary>
    static float HouseRing(Vector2 p)
    {
        float Out(Vector2 q, float hx, float zMin, float zMax) { float dx = Math.Max(0f, Math.Abs(q.X - Site.X) - hx), dz = Math.Max(0f, Math.Max(zMin - q.Y, q.Y - zMax)); return MathF.Sqrt(dx * dx + dz * dz); }
        float outer = Out(p, 12.4f, Site.Y - 9.3f, Site.Y + 10f), inner = Out(p, 9.9f, Site.Y - 6.8f, Site.Y + 9.6f);
        return (1f - Mathf.SmoothStep(0f, 0.5f, outer)) * Mathf.SmoothStep(0f, 0.45f, inner);
    }
}
