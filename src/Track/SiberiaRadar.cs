using System;
using System.Collections.Generic;
using Godot;

namespace CCCPRacing.Track;

/// <summary>Snowbound radar outpost above the eastern switchbacks; shared access, clearance and fence layout.</summary>
public static class SiberiaRadar
{
    public static readonly Vector2 Site = new(270, 156);
    public static readonly Vector2 Guard = new(280, 151);
    public static readonly Vector2[] Drive = { new(276, 139), new(270, 144), new(270, 149), new(270, 150) };
    public static readonly Vector2[][] Boundary = {
        new[] { new Vector2(273, 149), new(277, 149), new(277, 163), new(263, 163), new(263, 149), new(267, 149) },
    };

    public static bool Reserved(Vector2 p) =>
        (p.X >= 261 && p.X <= 279 && p.Y >= 147 && p.Y <= 165)
        || Village.NearAccessPath(p, Drive, 5) || p.DistanceTo(Guard) < 4;

    public static void GradeAccess(TrackData t) { foreach (var _ in GradeAccessSteps(t)) { } }

    /// <summary>GradeAccess in slices: every item is a point where a frame may go by (a loading card keeps running).</summary>
    public static System.Collections.Generic.IEnumerable<int> GradeAccessSteps(TrackData t)
    {
        var slice = System.Diagnostics.Stopwatch.StartNew();
        var heights = new float[Drive.Length];
        for (int i = 0; i < Drive.Length; i++) heights[i] = t.MeshHeight(Drive[i].X, Drive[i].Y);
        for (int z = 0; z <= t.N; z++)
        {
        if (slice.ElapsedMilliseconds > 10) { yield return 0; slice.Restart(); }
        for (int x = 0; x <= t.N; x++)
        {
            var p = new Vector2(x * t.Tile, z * t.Tile);
            int ci = t.NearestCenter(p, out float roadDistance);
            if (roadDistance < t.HalfWidth[ci] + 0.5f) continue;
            float best = 5, height = 0;
            for (int i = 0; i + 1 < Drive.Length; i++)
            {
                var s = Drive[i + 1] - Drive[i];
                float f = Math.Clamp((p - Drive[i]).Dot(s) / s.LengthSquared(), 0, 1);
                float d = p.DistanceTo(Drive[i] + s * f);
                if (d < best) { best = d; height = Mathf.Lerp(heights[i], heights[i + 1], f); }
            }
            if (best < 5)
            {
                int k = z * (t.N + 1) + x;
                t.Heights[k] = Mathf.Lerp(t.Heights[k], height, 1 - Mathf.SmoothStep(2.5f, 5, best));
            }
        }
        }
    }

    public static void PaintSurfaces(TrackData t) { foreach (var _ in PaintSurfacesSteps(t)) { } }

    /// <summary>PaintSurfaces in slices (see GradeAccessSteps).</summary>
    public static System.Collections.Generic.IEnumerable<int> PaintSurfacesSteps(TrackData t)
    {
        var slice = System.Diagnostics.Stopwatch.StartNew();
        for (int z = 0; z < t.N; z++)
        {
        if (slice.ElapsedMilliseconds > 10) { yield return 0; slice.Restart(); }
        for (int x = 0; x < t.N; x++)
        {
            var p = new Vector2((x + 0.5f) * t.Tile, (z + 0.5f) * t.Tile);
            int ci = t.NearestCenter(p, out float d);
            if (d <= t.HalfWidth[ci]) continue;
            if (Village.NearAccessPath(p, Drive, 2.5f) || p.DistanceTo(Guard) < 3)
                t.Surf[z * t.N + x] = SurfaceType.Snow;
        }
        }
    }

    public static void Reserve(TrackData t, List<Vector2> occupied)
    {
        for (float z = 136; z <= 168; z += 2)
        for (float x = 258; x <= 284; x += 2)
        {
            var p = new Vector2(x, z);
            if (Reserved(p)) occupied.Add(p);
        }
        foreach (var p in FenceSamples()) t.Obstacles.Add(new Obstacle(p, 0.22f));
    }

    static IEnumerable<Vector2> FenceSamples()
    {
        foreach (var run in Boundary)
        for (int i = 0; i + 1 < run.Length; i++)
        {
            int count = (int)MathF.Ceiling(run[i].DistanceTo(run[i + 1]) / 1.2f);
            for (int j = 0; j <= count; j++) yield return run[i].Lerp(run[i + 1], j / (float)count);
        }
    }

    public static Node3D BuildGrounds(TrackData t)
    {
        var root = new Node3D { Name = "RadarGrounds" };
        var steel = new Color(0.35f, 0.38f, 0.36f);
        var posts = new Node3D();
        var lines = new List<Vector3>();
        Vector3 Ground(Vector2 p) => new(p.X, t.MeshHeight(p.X, p.Y), p.Y);
        foreach (var run in Boundary)
        for (int i = 0; i + 1 < run.Length; i++)
        {
            int count = (int)MathF.Ceiling(run[i].DistanceTo(run[i + 1]) / 3);
            for (int j = 0; j <= count; j++)
            {
                var p = Ground(run[i].Lerp(run[i + 1], j / (float)count));
                posts.AddChild(Props.Part(Props.Cyl(0.055f, 1.9f, 5), steel, p + Vector3.Up * 0.95f));
                // The post leans out at the top to carry two more strands of barbed wire (an outrigger, 45 degrees).
                Vector2 edge = run[i + (j == count ? 0 : 1)] - run[i];
                Vector3 outward = new Vector3(edge.Y, 0, -edge.X).Normalized() * 0.28f;
                posts.AddChild(Props.Part(Props.Box(0.05f, 0.05f, 0.45f), steel, p + Vector3.Up * 1.95f + outward * 0.5f,
                    new Vector3(0, Mathf.RadToDeg(MathF.Atan2(outward.X, outward.Z)), 0)));
                if (j == count) continue;
                var q = Ground(run[i].Lerp(run[i + 1], (j + 1) / (float)count));
                foreach (float h in new[] { 0.55f, 1.1f, 1.75f })
                { lines.Add(p + Vector3.Up * h); lines.Add(q + Vector3.Up * h); }
                foreach (float h in new[] { 2.0f, 2.1f })
                { lines.Add(p + Vector3.Up * h + outward); lines.Add(q + Vector3.Up * h + outward); }
            }
        }
        // The gate in the north run: two stout posts with caps, and the leaf folded back along the fence on the east side.
        foreach (var gx in new[] { 267f, 273f })
        {
            var g = Ground(new Vector2(gx, 149));
            posts.AddChild(Props.Part(Props.Cyl(0.11f, 2.4f, 6), steel, g + Vector3.Up * 1.2f));
            posts.AddChild(Props.Part(Props.Ball(0.13f, 5), steel * 1.2f, g + Vector3.Up * 2.45f));
        }
        var leaf = Ground(new Vector2(274.5f, 149.1f));
        posts.AddChild(Props.Part(Props.Box(2.8f, 0.07f, 0.07f), steel, leaf + Vector3.Up * 0.35f));
        posts.AddChild(Props.Part(Props.Box(2.8f, 0.07f, 0.07f), steel, leaf + Vector3.Up * 1.7f));
        for (int k = 0; k < 6; k++)
            posts.AddChild(Props.Part(Props.Box(0.06f, 1.4f, 0.06f), steel, leaf + new Vector3(-1.25f + k * 0.5f, 1.05f, 0)));
        DrunkTractor.BatchStatic(posts, null);
        root.AddChild(posts);
        // Warning plates on the east and west fences, readable from outside.
        foreach (var (at, yaw) in new[] { (new Vector2(277.1f, 156f), 90f), (new Vector2(262.9f, 156f), -90f), (new Vector2(277.1f, 152f), 90f) })
        {
            var plate = Buildings.TextPanel("ЗАПРЕТНАЯ ЗОНА", 2.4f, 0.45f, Ground(at) + Vector3.Up * 1.4f, new Color(0.78f, 0.1f, 0.08f), Colors.White);
            plate.RotationDegrees = new Vector3(0, yaw, 0);
            root.AddChild(plate);
        }
        var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = lines.ToArray();
        var mesh = new ArrayMesh(); mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, arrays);
        root.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = Props.Mat(steel), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        return root;
    }
}
