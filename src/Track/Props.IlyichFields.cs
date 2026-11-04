using System.Collections.Generic;
using System;
using Godot;

namespace CCCPRacing.Track;

public static partial class Props
{
    /// <summary>In slices: every item is a point where a frame may go by (a loading card keeps running).</summary>
    static IEnumerable<int> IlyichNorthernFields(TrackData t, Node3D root, List<Node3D> pending)
    {
        var slice = System.Diagnostics.Stopwatch.StartNew();
        Vector3 Ground(Vector2 p, float lift = 0) => new(p.X, WorldBackdrop.SurfaceHeightAt(t, p) + lift, p.Y);
        var fields = new (Vector2 Centre, Vector2 HalfSize, bool Harvested)[]
        {
            (new(44, 7), new(27, 11), false),
            (new(238, 12), new(30, 10), true),
            (new(93, -28), new(31, 14), true),
            (new(224, -30), new(26, 13), false),
        };
        for (int index = 0; index < fields.Length; index++)
        {
            var (centre, half, harvested) = fields[index];
            var field = new Node3D { Name = "Northern field " + index }; pending.Add(field);
            // Four-sided patch corners are on 45-degree rays; compensate so its extent is half.
            foreach (var _ in PatchSteps(field, t, centre, half * MathF.Sqrt(2),
                harvested ? new Color(0.43f, 0.35f, 0.19f) : new Color(0.32f, 0.27f, 0.16f), 4)) { yield return 0; slice.Restart(); }
            yield return 0; slice.Restart();
            // Ground-following parallel furrows and short stubble make these real parcels at racing scale.
            var rows = new SurfaceTool();
            rows.Begin(Mesh.PrimitiveType.Triangles);
            void Tri(Vector3 a, Vector3 b, Vector3 c, Color colour)
            {
                rows.SetColor(colour); rows.SetNormal(Vector3.Up);
                rows.AddVertex(a); rows.AddVertex(b); rows.AddVertex(c);
            }
            for (float z = -half.Y + 1; z < half.Y - 0.5f; z += 1.7f)
            {
            if (slice.ElapsedMilliseconds > 10) { yield return 0; slice.Restart(); }
            for (float x = -half.X + 0.5f; x < half.X - 0.5f; x += 1.4f)
            {
                Vector2 p = centre + new Vector2(x, z);
                float end = Math.Min(1.4f, half.X - x);
                var colour = harvested ? new Color(0.60f, 0.49f, 0.26f) : new Color(0.21f, 0.29f, 0.12f);
                Vector3 a = Ground(p + new Vector2(0, -0.13f), 0.06f);
                Vector3 b = Ground(p + new Vector2(end, -0.13f), 0.06f);
                Vector3 c = Ground(p + new Vector2(end, 0.13f), 0.06f);
                Vector3 d = Ground(p + new Vector2(0, 0.13f), 0.06f);
                Tri(a, c, b, colour); Tri(a, d, c, colour);
                if (Noise.Hash01((int)(x * 10), (int)(z * 10), 920 + index) > 0.35f)
                {
                    Vector3 stem = Ground(p + new Vector2(0.4f, 0), 0.07f);
                    float height = harvested ? 0.35f : 0.65f;
                    Tri(stem + new Vector3(-0.12f, 0, 0), stem + Vector3.Up * height,
                        stem + new Vector3(0.12f, 0, 0), colour.Lightened(0.1f));
                }
            }
            }
            rows.SetMaterial(new StandardMaterial3D { VertexColorUseAsAlbedo = true, CullMode = BaseMaterial3D.CullModeEnum.Disabled });
            yield return 0; slice.Restart();
            field.AddChild(new MeshInstance3D { Mesh = rows.Commit() });
            yield return 0; slice.Restart();
            foreach (var _ in DrunkTractor.BatchStaticSteps(field, null)) { yield return 0; slice.Restart(); }
            yield return 0; slice.Restart();
            root.AddChild(field);
        }

        // Beyond the northern barn yard, an open hay shelter gives the fields a destination.
        // It stands outside the playable grid, so it is backdrop rather than a new driving obstacle.
        var shelter = new Node3D { Name = "Northern hay shelter", Position = Ground(new Vector2(151, -16)) }; pending.Add(shelter);
        foreach (float x in new[] { -6f, 0f, 6f })
        foreach (float z in new[] { -4f, 4f })
            shelter.AddChild(Part(Box(0.32f, 4.4f, 0.32f), Timber, new Vector3(x, 2.2f, z)));
        foreach (float z in new[] { -4f, 4f })
            shelter.AddChild(Part(Box(12.6f, 0.28f, 0.28f), Wood, new Vector3(0, 4.2f, z)));
        shelter.AddChild(Part(new PrismMesh { Size = new Vector3(13.8f, 2.6f, 9.8f) }, new Color(0.43f, 0.40f, 0.34f), new Vector3(0, 5.5f, 0)));
        for (int i = 0; i < 8; i++)
        {
            var bale = HayBale(1922 + i);
            bale.Position = new Vector3(-4.5f + i % 4 * 3, 0, -1.5f + i / 4 * 3);
            shelter.AddChild(bale);
        }
        foreach (var _ in DrunkTractor.BatchStaticSteps(shelter, null)) { yield return 0; slice.Restart(); }
        root.AddChild(shelter);
    }
}
