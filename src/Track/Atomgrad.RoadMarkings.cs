using System;
using Godot;

namespace CCCPRacing.Track;

public static partial class Atomgrad
{
    // Faded paint is actual ground-following geometry: potholes and standing water interrupt it.
    public static Node3D RoadMarkings(TrackData track)
    {
        var root = new Node3D { Name = "WornRoadMarkings" };
        if (track.Id != "atomgrad") return root;
        var mesh = new SurfaceTool(); mesh.Begin(Mesh.PrimitiveType.Triangles);
        int count = 0;
        Vector2 Point(float s, float lateral)
        {
            int i = (int)s % track.Center.Length, next = (i + 1) % track.Center.Length;
            float f = s - MathF.Floor(s);
            var forward = track.CenterDir(i).Lerp(track.CenterDir(next), f).Normalized();
            return track.Center[i].Lerp(track.Center[next], f) + new Vector2(-forward.Y, forward.X) * lateral;
        }
        bool Concrete(Vector2 p)
        {
            int x = (int)(p.X / track.Tile), z = (int)(p.Y / track.Tile);
            return x >= 0 && z >= 0 && x < track.N && z < track.N
                && track.TileSurface(x, z) == SurfaceType.Concrete;
        }
        void Cell(float s, float length, float lateral, float width, int seed)
        {
            var centre = Point(s + length / 2, lateral + width / 2);
            float wear = Noise.Hash01((int)(s * 2), (int)(lateral * 13), seed);
            if (wear < .28f || Noise.Hash01((int)(s / 3), seed, 1986) < .1f) return;
            var a = Point(s, lateral); var b = Point(s, lateral + width);
            var c = Point(s + length, lateral + width); var d = Point(s + length, lateral);
            if (!Concrete(centre) || !Concrete(a) || !Concrete(b) || !Concrete(c) || !Concrete(d)) return;
            float shade = .78f + wear * .12f;
            var paint = new Color(shade, shade * .97f, shade * .84f);
            void Vertex(Vector2 p)
            {
                mesh.SetNormal(Vector3.Up); mesh.SetColor(paint);
                mesh.AddVertex(new Vector3(p.X, track.MeshHeight(p.X, p.Y) + .04f, p.Y));
            }
            Vertex(a); Vertex(b); Vertex(c); Vertex(a); Vertex(c); Vertex(d); count++;
        }
        var crossings = new int[3];
        var sites = new[] { new Vector2(44, 104), new Vector2(80, 127), new Vector2(82, 180) };
        for (int i = 0; i < sites.Length; i++) crossings[i] = track.NearestCenter(sites[i], out _);
        foreach (var span in new[] { (285, 440), (440, 585), (605, 895) })
        for (float s = span.Item1; s < span.Item2; s += .5f)
        {
            int i = (int)s;
            if (track.CenterSurf[i] != SurfaceType.Concrete) continue;
            bool crossing = Array.Exists(crossings, at => Math.Abs(s - at) < 5);
            if (crossing) continue;
            float turn = Math.Abs(track.CenterDir(i - 5).AngleTo(track.CenterDir(i + 5)));
            // Sparse central dashes through town; no stripes wrapping around the square corners.
            if (turn > .32f) continue;
            if ((int)s % 12 < 5) Cell(s, .45f, -.13f, .26f, 101);
            if (span.Item1 >= 605 && turn > .12f) continue;
            foreach (int side in new[] { -1, 1 })
            {
                float lateral = side * (track.HalfWidth[i] - .65f) - .11f;
                Cell(s, .45f, lateral, .22f, side + 105);
            }
        }
        foreach (int at in crossings)
        {
            float half = track.HalfWidth[at] - .8f;
            for (float lateral = -half; lateral < half; lateral += 1.25f)
            for (float s = at - 1.5f; s < at + 1.5f; s += .5f)
            for (float across = lateral; across < Math.Min(lateral + .7f, half); across += .2f)
                Cell(s, .45f, across, Math.Min(.18f, half - across), 301 + at);
        }
        if (count == 0) return root;
        mesh.Index();
        root.AddChild(new MeshInstance3D { Name = "FadedPaint", Mesh = mesh.Commit(),
            MaterialOverride = new StandardMaterial3D { VertexColorUseAsAlbedo = true,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled, Roughness = 1,
                SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        return root;
    }
}
