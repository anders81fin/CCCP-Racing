using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CCCPRacing.Track;

/// <summary>
/// The water's surface at TrackData.WaterLevel, clipped to the authored pond and river footprint
/// and to the rendered terrain below the waterline.
/// </summary>
public static class WaterMesh
{
    public static readonly Color Colour = new(0.30f, 0.44f, 0.50f, 0.62f);

    // Polluted green-grey water still catches the sky; rust is carried in uneven surface streaks.
    public static Color ColourFor(TrackData t) => t.Id == "atomgrad"
        ? new Color(0.45f, 0.55f, 0.53f, 0.82f) : Colour;

    public static Node3D Build(TrackData t)
    {
        Node3D result = null;
        foreach (var step in BuildSteps(t)) result = step ?? result;
        return result;
    }

    /// <summary>The same build in slices (a null between them: a frame may go by while a loading card runs); the last item is the water.
    /// A build abandoned between slices frees the half-made water node.</summary>
    public static IEnumerable<Node3D> BuildSteps(TrackData t)
    {
        var root = new Node3D { Name = "Water" };
        bool handedOver = false;
        try
        {
        if (float.IsNaN(t.WaterLevel)) { handedOver = true; yield return root; yield break; }
        float y = t.WaterLevel;
        var built = new WaterSurfaceMeshes();
        foreach (var _ in BuildWaterSurfacesSteps(t, y, built)) yield return null;
        ArrayMesh surface = built.Base, flowSurface = built.Flow;
        yield return null;
        var waterMaterial = new StandardMaterial3D
        {
            AlbedoColor = ColourFor(t),
            AlbedoTexture = t.Id == "atomgrad" ? IndustrialTexture() : null,
            Uv1Scale = t.Id == "atomgrad" ? new Vector3(0.25f, 0.25f, 1) : Vector3.One,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Linear,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        if (surface != null)
            root.AddChild(new MeshInstance3D
            {
                Mesh = surface,
                MaterialOverride = waterMaterial,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        // The base water sheet stays calm across both ponds and rivers. Add the animated glints
        // only to river tiles, with UVs measured along each authored centre line so the current
        // follows bends and flows in the point order (source to downstream).
        if (flowSurface != null)
        {
            var flowMaterial = new StandardMaterial3D
            {
                AlbedoColor = t.Id == "atomgrad"
                    ? new Color(0.84f, 0.87f, 0.76f, 0.62f)
                    : new Color(0.78f, 0.9f, 1f, 0.46f),
                AlbedoTexture = FlowTexture(t.Id == "atomgrad"),
                TextureFilter = BaseMaterial3D.TextureFilterEnum.Linear,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            };
            root.AddChild(new MeshInstance3D
            {
                Name = "Moving current",
                Mesh = flowSurface,
                Position = new Vector3(0, 0.025f, 0),
                MaterialOverride = flowMaterial,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
            root.AddChild(new WaterFlow(flowMaterial));
        }
        yield return null;
        AddFordStones(root, t);
        yield return null;
        AddFordPlants(root, t);
        AddPondReeds(root, t);
        handedOver = true;
        yield return root;
        }
        finally { if (!handedOver) root.Free(); }
    }

    /// <summary>The finished water meshes (an iterator cannot return them): the calm sheet and the animated current.</summary>
    sealed class WaterSurfaceMeshes { public ArrayMesh Base, Flow; }

    const float FlowUvScale = 0.12f;
    const int WaterEdgeSubdivisions = 6;

    readonly struct WaterVertex
    {
        public readonly Vector2 P;
        public readonly float Height;
        public readonly float Edge;
        public WaterVertex(Vector2 p, float height, float edge) { P = p; Height = height; Edge = edge; }
        public WaterVertex Lerp(WaterVertex other, float f) => new(P.Lerp(other.P, f), Mathf.Lerp(Height, other.Height, f), Mathf.Lerp(Edge, other.Edge, f));
    }

    /// <summary>
    /// Tessellate the exact terrain triangles near authored water, then clip each small triangle
    /// against both the authored shoreline and the rendered ground at the waterline. This prevents
    /// the water plane crossing raised banks or floating over low ground outside a pond or river.
    /// </summary>
    static IEnumerable<int> BuildWaterSurfacesSteps(TrackData t, float waterLevel, WaterSurfaceMeshes result)
    {
        var slice = System.Diagnostics.Stopwatch.StartNew();
        if (t.Water == null) yield break;
        var baseTool = new SurfaceTool(); baseTool.Begin(Mesh.PrimitiveType.Triangles); baseTool.SetNormal(Vector3.Up);
        var flowTool = new SurfaceTool(); flowTool.Begin(Mesh.PrimitiveType.Triangles); flowTool.SetNormal(Vector3.Up);
        var pathOffsets = new Dictionary<RiverDef, float[]>();
        foreach (var river in t.Water.Rivers)
        {
            if (river.Points == null || river.Points.Length < 2) continue;
            var offsets = new float[river.Points.Length - 1];
            for (int i = 1; i < river.Points.Length - 1; i++)
            {
                Vector2 a = new(river.Points[i - 1][0], river.Points[i - 1][1]);
                Vector2 b = new(river.Points[i][0], river.Points[i][1]);
                offsets[i] = offsets[i - 1] + a.DistanceTo(b);
            }
            pathOffsets.Add(river, offsets);
        }

        if (!WaterBounds(t.Water, out float minX, out float minZ, out float maxX, out float maxZ)) yield break;
        int minTileX = Math.Clamp((int)MathF.Floor(minX / t.Tile), 0, t.N - 1);
        int minTileZ = Math.Clamp((int)MathF.Floor(minZ / t.Tile), 0, t.N - 1);
        int maxTileX = Math.Clamp((int)MathF.Floor(maxX / t.Tile), 0, t.N - 1);
        int maxTileZ = Math.Clamp((int)MathF.Floor(maxZ / t.Tile), 0, t.N - 1);
        bool hasBase = false, hasFlow = false;
        float tileDiagonal = t.Tile * MathF.Sqrt(2f);
        for (int z = minTileZ; z <= maxTileZ; z++)
        {
        if (slice.ElapsedMilliseconds > 10) { yield return 0; slice.Restart(); }
        for (int x = minTileX; x <= maxTileX; x++)
        {
            Vector2 v00p = new(x * t.Tile, z * t.Tile), v10p = new((x + 1) * t.Tile, z * t.Tile);
            Vector2 v01p = new(x * t.Tile, (z + 1) * t.Tile), v11p = new((x + 1) * t.Tile, (z + 1) * t.Tile);
            float h00 = t.VertexHeight(x, z), h10 = t.VertexHeight(x + 1, z);
            float h01 = t.VertexHeight(x, z + 1), h11 = t.VertexHeight(x + 1, z + 1);
            WaterVertex v00 = Sample(t, v00p, h00), v10 = Sample(t, v10p, h10);
            WaterVertex v01 = Sample(t, v01p, h01), v11 = Sample(t, v11p, h11);
            if (v00.Edge > tileDiagonal && v10.Edge > tileDiagonal && v01.Edge > tileDiagonal && v11.Edge > tileDiagonal)
                continue; // The authored water edge cannot reach any point in this tile.

            bool fullySubmerged = v00.Edge <= -tileDiagonal && v10.Edge <= -tileDiagonal
                && v01.Edge <= -tileDiagonal && v11.Edge <= -tileDiagonal
                && h00 <= waterLevel && h10 <= waterLevel && h01 <= waterLevel && h11 <= waterLevel;
            if (fullySubmerged)
            {
                EmitTriangle(baseTool, v00p, v10p, v11p, waterLevel, false, default, 0f);
                EmitTriangle(baseTool, v00p, v11p, v01p, waterLevel, false, default, 0f);
                hasBase = true;

                if (TryFastRiverTile(t.Water, new Vector2((x + 0.5f) * t.Tile, (z + 0.5f) * t.Tile),
                        v00p, v10p, v01p, v11p, tileDiagonal, pathOffsets,
                        out RiverDef river, out int segment, out float pathOffset))
                {
                    Vector2 start = new(river.Points[segment][0], river.Points[segment][1]);
                    Vector2 end = new(river.Points[segment + 1][0], river.Points[segment + 1][1]);
                    var path = (start, (end - start).Normalized());
                    EmitTriangle(flowTool, v00p, v10p, v11p, waterLevel, true, path, pathOffset);
                    EmitTriangle(flowTool, v00p, v11p, v01p, waterLevel, true, path, pathOffset);
                    hasFlow = true;
                }
                else if (MayContainCurrent(t.Water, new Vector2((x + 0.5f) * t.Tile, (z + 0.5f) * t.Tile), tileDiagonal))
                {
                    // Only the current mask needs fine triangles around the river/pond transition.
                    SubdivideTriangle(v00, v10, v11, WaterEdgeSubdivisions, baseTool, flowTool, t, waterLevel, pathOffsets,
                        false, ref hasBase, ref hasFlow);
                    SubdivideTriangle(v00, v11, v01, WaterEdgeSubdivisions, baseTool, flowTool, t, waterLevel, pathOffsets,
                        false, ref hasBase, ref hasFlow);
                }
            }
            else
            {
                SubdivideTriangle(v00, v10, v11, WaterEdgeSubdivisions, baseTool, flowTool, t, waterLevel, pathOffsets,
                    true, ref hasBase, ref hasFlow);
                SubdivideTriangle(v00, v11, v01, WaterEdgeSubdivisions, baseTool, flowTool, t, waterLevel, pathOffsets,
                    true, ref hasBase, ref hasFlow);
            }
        }
        }
        yield return 0;      // (each commit below is one native call)
        result.Base = hasBase ? baseTool.Commit() : null;
        yield return 0;
        result.Flow = hasFlow ? flowTool.Commit() : null;
    }

    static WaterVertex Sample(TrackData t, Vector2 p, float height) => new(p, height, WaterDistance(t.Water, p));

    static void SubdivideTriangle(WaterVertex a, WaterVertex b, WaterVertex c, int divisions,
        SurfaceTool baseTool, SurfaceTool flowTool, TrackData t, float waterLevel, Dictionary<RiverDef, float[]> pathOffsets,
        bool emitBase, ref bool hasBase, ref bool hasFlow)
    {
        var rows = new WaterVertex[divisions + 1][];
        for (int i = 0; i <= divisions; i++)
            rows[i] = new WaterVertex[divisions - i + 1];
        // Fill barycentric samples directly. The source triangle is planar, while the authored
        // distance is re-evaluated at every sample to follow curved river banks closely.
        for (int i = 0; i <= divisions; i++)
        for (int j = 0; j <= divisions - i; j++)
        {
            float u = i / (float)divisions, v = j / (float)divisions;
            Vector2 p = a.P + (b.P - a.P) * u + (c.P - a.P) * v;
            float h = a.Height + (b.Height - a.Height) * u + (c.Height - a.Height) * v;
            rows[i][j] = Sample(t, p, h);
        }
        for (int i = 0; i < divisions; i++)
        for (int j = 0; j < divisions - i; j++)
        {
            ClipAndEmit(rows[i][j], rows[i + 1][j], rows[i][j + 1], baseTool, flowTool, t, waterLevel, pathOffsets,
                emitBase, ref hasBase, ref hasFlow);
            if (i + j < divisions - 1)
                ClipAndEmit(rows[i + 1][j], rows[i + 1][j + 1], rows[i][j + 1], baseTool, flowTool, t, waterLevel, pathOffsets,
                    emitBase, ref hasBase, ref hasFlow);
        }
    }

    static void ClipAndEmit(WaterVertex a, WaterVertex b, WaterVertex c, SurfaceTool baseTool,
        SurfaceTool flowTool, TrackData t, float waterLevel, Dictionary<RiverDef, float[]> pathOffsets,
        bool emitBase, ref bool hasBase, ref bool hasFlow)
    {
        var polygon = new List<WaterVertex> { a, b, c };
        polygon = Clip(polygon, 0f, true);
        polygon = Clip(polygon, waterLevel, false);
        if (polygon.Count < 3) return;
        if (emitBase)
        {
            EmitPolygon(baseTool, polygon, waterLevel, false, null, 0f);
            hasBase = true;
        }

        Vector2 center = Vector2.Zero;
        foreach (var vertex in polygon) center += vertex.P;
        center /= polygon.Count;
        if (!TryNearestRiver(t.Water, center, out RiverDef river, out int segment, out float riverEdgeDistance) || riverEdgeDistance > 0f) return;
        foreach (var pond in t.Water.Ponds)
            if (center.DistanceTo(new Vector2(pond.X, pond.Z)) < pond.R) return;
        if (!pathOffsets.TryGetValue(river, out var offsets)) return;
        Vector2 start = new(river.Points[segment][0], river.Points[segment][1]);
        Vector2 end = new(river.Points[segment + 1][0], river.Points[segment + 1][1]);
        Vector2 dir = (end - start).Normalized();
        EmitPolygon(flowTool, polygon, waterLevel, true, (start, dir), offsets[segment]);
        hasFlow = true;
    }

    static List<WaterVertex> Clip(List<WaterVertex> input, float boundary, bool waterSide)
    {
        var output = new List<WaterVertex>(input.Count + 1);
        if (input.Count == 0) return output;
        float Value(WaterVertex v) => waterSide ? v.Edge : v.Height;
        bool Inside(WaterVertex v) => waterSide ? v.Edge <= boundary : v.Height <= boundary;
        WaterVertex previous = input[^1];
        bool previousInside = Inside(previous);
        foreach (var current in input)
        {
            bool currentInside = Inside(current);
            if (currentInside != previousInside)
            {
                float f = (boundary - Value(previous)) / (Value(current) - Value(previous));
                output.Add(previous.Lerp(current, f));
            }
            if (currentInside) output.Add(current);
            previous = current;
            previousInside = currentInside;
        }
        return output;
    }

    static void EmitPolygon(SurfaceTool tool, List<WaterVertex> polygon, float waterLevel, bool flow,
        (Vector2 start, Vector2 dir)? path, float pathOffset)
    {
        WaterVertex root = polygon[0];
        for (int i = 1; i + 1 < polygon.Count; i++)
        {
            AddWaterVertex(tool, root.P, waterLevel, flow, path, pathOffset);
            AddWaterVertex(tool, polygon[i].P, waterLevel, flow, path, pathOffset);
            AddWaterVertex(tool, polygon[i + 1].P, waterLevel, flow, path, pathOffset);
        }
    }

    static void AddWaterVertex(SurfaceTool tool, Vector2 p, float y, bool flow, (Vector2 start, Vector2 dir)? path, float pathOffset)
    {
        if (flow && path.HasValue)
        {
            Vector2 delta = p - path.Value.start;
            tool.SetUV(new Vector2((pathOffset + delta.Dot(path.Value.dir)) * FlowUvScale, path.Value.dir.Cross(delta) * FlowUvScale));
        }
        else tool.SetUV(p * 0.11f);
        tool.SetNormal(Vector3.Up);
        tool.AddVertex(new Vector3(p.X, y, p.Y));
    }

    static void EmitTriangle(SurfaceTool tool, Vector2 a, Vector2 b, Vector2 c, float y, bool flow,
        (Vector2 start, Vector2 dir)? path, float pathOffset)
    {
        AddWaterVertex(tool, a, y, flow, path, pathOffset);
        AddWaterVertex(tool, b, y, flow, path, pathOffset);
        AddWaterVertex(tool, c, y, flow, path, pathOffset);
    }

    static bool MayContainCurrent(WaterDef water, Vector2 center, float diagonal)
    {
        if (!TryNearestRiver(water, center, out _, out _, out float riverEdge) || riverEdge > diagonal) return false;
        foreach (var pond in water.Ponds)
            if (center.DistanceTo(new Vector2(pond.X, pond.Z)) + diagonal < pond.R) return false;
        return true;
    }

    static bool TryFastRiverTile(WaterDef water, Vector2 center, Vector2 v00, Vector2 v10, Vector2 v01, Vector2 v11,
        float diagonal, Dictionary<RiverDef, float[]> pathOffsets, out RiverDef river, out int segment, out float pathOffset)
    {
        pathOffset = 0f;
        if (!TryNearestRiver(water, center, out river, out segment, out float edge) || edge > -diagonal) return false;
        foreach (Vector2 p in new[] { v00, v10, v01, v11 })
        {
            if (!TryNearestRiver(water, p, out RiverDef cornerRiver, out int cornerSegment, out float cornerEdge)
                || cornerRiver != river || cornerSegment != segment || cornerEdge > -diagonal)
            {
                river = null; segment = -1; return false;
            }
        }
        foreach (var pond in water.Ponds)
            if (center.DistanceTo(new Vector2(pond.X, pond.Z)) <= pond.R + diagonal)
            {
                river = null; segment = -1; return false;
            }
        if (!pathOffsets.TryGetValue(river, out float[] offsets)) { river = null; segment = -1; return false; }
        pathOffset = offsets[segment];
        return true;
    }

    static bool WaterBounds(WaterDef water, out float minX, out float minZ, out float maxX, out float maxZ)
    {
        minX = minZ = float.PositiveInfinity; maxX = maxZ = float.NegativeInfinity;
        foreach (var pond in water.Ponds)
        {
            minX = Math.Min(minX, pond.X - pond.R); maxX = Math.Max(maxX, pond.X + pond.R);
            minZ = Math.Min(minZ, pond.Z - pond.R); maxZ = Math.Max(maxZ, pond.Z + pond.R);
        }
        foreach (var river in water.Rivers)
        {
            if (river.Points == null) continue;
            float radius = river.MaxWidth * 0.5f;
            foreach (var point in river.Points)
            {
                minX = Math.Min(minX, point[0] - radius); maxX = Math.Max(maxX, point[0] + radius);
                minZ = Math.Min(minZ, point[1] - radius); maxZ = Math.Max(maxZ, point[1] + radius);
            }
        }
        return !float.IsPositiveInfinity(minX);
    }

    static float WaterDistance(WaterDef water, Vector2 p)
    {
        float best = float.PositiveInfinity;
        foreach (var river in water.Rivers)
        {
            if (river.Points == null || river.Points.Length < 2) continue;
            best = Math.Min(best, river.EdgeDistance(p));
        }
        foreach (var pond in water.Ponds)
            best = Math.Min(best, p.DistanceTo(new Vector2(pond.X, pond.Z)) - pond.R);
        return best;
    }

    static bool TryNearestRiver(WaterDef water, Vector2 p, out RiverDef nearestRiver, out int nearestSegment, out float edgeDistance)
    {
        nearestRiver = null;
        nearestSegment = -1;
        edgeDistance = float.PositiveInfinity;
        foreach (var river in water.Rivers)
        {
            if (river.Points == null || river.Points.Length < 2) continue;
            for (int i = 0; i + 1 < river.Points.Length; i++)
            {
                Vector2 a = new(river.Points[i][0], river.Points[i][1]);
                Vector2 b = new(river.Points[i + 1][0], river.Points[i + 1][1]);
                Vector2 ab = b - a;
                float lengthSquared = ab.LengthSquared();
                if (lengthSquared < 0.0001f) continue;
                float u = Math.Clamp((p - a).Dot(ab) / lengthSquared, 0f, 1f);
                float distance = p.DistanceTo(a + ab * u) - river.HalfWidth(i, u);
                if (distance >= edgeDistance) continue;
                edgeDistance = distance;
                nearestRiver = river;
                nearestSegment = i;
            }
        }
        if (nearestRiver != null) edgeDistance = nearestRiver.EdgeDistance(p);
        return nearestRiver != null;
    }

    static ImageTexture _industrialTexture;

    // Seamless low-frequency patches plus stretched surface streaks; shared with the off-map river.
    internal static ImageTexture IndustrialTexture()
    {
        if (_industrialTexture != null) return _industrialTexture;
        const int size = 128;
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float u = x * Mathf.Tau / size, v = y * Mathf.Tau / size;
            float patches = 0.5f + 0.22f * MathF.Sin(u + 0.8f * MathF.Sin(v))
                + 0.16f * MathF.Sin(2 * v - u) + 0.1f * MathF.Sin(3 * u + 2 * v);
            var color = new Color(0.48f, 0.65f, 0.61f).Lerp(new Color(0.95f, 0.98f, 0.87f), patches);
            float rust = Mathf.SmoothStep(0.55f, 0.95f, MathF.Sin(2 * v + MathF.Sin(u))) * 0.45f;
            color = color.Lerp(new Color(0.95f, 0.72f, 0.46f), rust);
            float streak = MathF.Sin(11 * v + 0.7f * MathF.Sin(2 * u)) * 0.035f;
            image.SetPixel(x, y, new Color(color.R + streak, color.G + streak, color.B + streak));
        }
        return _industrialTexture = ImageTexture.CreateFromImage(image);
    }

    /// <summary>A seamless, hand-painted pattern of narrow broken glints; WaterFlow scrolls it downstream.</summary>
    static ImageTexture FlowTexture(bool industrial = false)
    {
        const int size = 64;
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        image.Fill(new Color(1f, 1f, 1f, 0f));
        var rng = new Random(1922);
        for (int streak = 0; streak < (industrial ? 44 : 34); streak++)
        {
            int x = rng.Next(size), y = rng.Next(size), length = industrial ? rng.Next(4, 12) : rng.Next(3, 10);
            int drift = industrial ? 0 : rng.Next(-1, 2);
            for (int k = 0; k < length; k++)
            {
                int px = (x + k) % size, py = (y + k * drift) % size;
                if (py < 0) py += size;
                image.SetPixel(px, py, new Color(1f, 1f, 1f, industrial ? 0.55f : 0.32f));
                if (k % 3 == 0) image.SetPixel(px, (py + 1) % size, new Color(1f, 1f, 1f, industrial ? 0.16f : 0.1f));
            }
        }
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>Exposed low-poly stones among the shallow ford gravel, batched into one draw call.</summary>
    static void AddFordStones(Node3D root, TrackData t)
    {
        if (t.FordIndex < 0) return;
        var points = new List<Vector2>();
        int n = t.N;
        Vector2 ford = t.Center[t.FordIndex];
        for (int z = 0; z < n; z++)
        for (int x = 0; x < n; x++)
        {
            if (t.Surf[z * n + x] != SurfaceType.Shallow) continue;
            var p = new Vector2((x + 0.5f) * t.Tile, (z + 0.5f) * t.Tile);
            if (p.DistanceTo(ford) > 22f) continue;
            uint h = Hash(x, z);
            if (h % 2 == 0) points.Add(p + new Vector2(((h >> 8) % 100) / 100f - 0.5f, ((h >> 16) % 100) / 100f - 0.5f) * t.Tile * 0.55f);
        }
        if (points.Count == 0) return;
        var mesh = new SphereMesh { Radius = 1f, Height = 1.45f, RadialSegments = 7, Rings = 3 };
        mesh.Material = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.39f, 0.38f, 0.34f), Roughness = 0.94f,
            SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
        };
        var multi = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh, InstanceCount = points.Count };
        for (int i = 0; i < points.Count; i++)
        {
            uint h = Hash((int)(points[i].X * 10), (int)(points[i].Y * 10));
            var scale = new Vector3(0.25f + (h % 100) / 300f, 0.13f + ((h >> 8) % 100) / 750f, 0.22f + ((h >> 16) % 100) / 380f);
            var basis = Basis.Identity.Rotated(Vector3.Up, ((h >> 4) % 628) / 100f).Scaled(scale);
            multi.SetInstanceTransform(i, new Transform3D(basis, new Vector3(points[i].X, t.WaterLevel - 0.04f, points[i].Y)));
        }
        root.AddChild(new MultiMeshInstance3D { Name = "Ford stones", Multimesh = multi, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
    }

    internal static bool DeepFordPlantSite(TrackData t, Vector2 p)
    {
        if(t.Id!="ilyich" || t.FordIndex<0 || p.DistanceTo(t.Center[t.FordIndex])>8) return false;
        return t.WaterLevel-t.MeshHeight(p.X,p.Y)>=.4f
            && t.Water.Rivers.Any(r=>r.EdgeDistance(p)<-.4f);
    }

    /// <summary>Water lilies on the quiet shallows and small reed beds just beyond the ford banks.</summary>
    static void AddFordPlants(Node3D root, TrackData t)
    {
        if (t.FordIndex < 0 || t.Winter) return;
        int n = t.N;
        Vector2 ford = t.Center[t.FordIndex];
        var rng = new Random(1922);
        var padCandidates = new List<Vector2>();
        for (int z = 0; z < n; z++)
        for (int x = 0; x < n; x++)
        {
            var surface = t.Surf[z * n + x];
            if (surface is not (SurfaceType.Water or SurfaceType.Shallow)) continue;
            var p = new Vector2((x + 0.5f) * t.Tile, (z + 0.5f) * t.Tile);
            if (p.DistanceTo(ford) > 23f) continue;
            int ci = t.NearestCenter(p, out float roadDistance);
            if (ci >= 0 && roadDistance < t.HalfWidth[ci] + 2.5f && !DeepFordPlantSite(t,p)) continue;
            if (!DeepFordPlantSite(t,p) && rng.NextDouble() > (surface == SurfaceType.Shallow ? 0.52 : 0.32)) continue;
            padCandidates.Add(p);
        }
        for (int i = padCandidates.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (padCandidates[i], padCandidates[j]) = (padCandidates[j], padCandidates[i]);
        }
        var pads = new List<Vector2>();
        foreach (var p in padCandidates.OrderByDescending(p=>DeepFordPlantSite(t,p)))
        {
            if (pads.Count >= 18) break;
            if (pads.Any(q => p.DistanceTo(q) < 2.4f)) continue;
            var candidate = p + new Vector2((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f) * 0.8f;
            int ci=t.NearestCenter(candidate,out float rd);
            if(rd<t.HalfWidth[ci]+2.5f && !DeepFordPlantSite(t,candidate)) candidate=p;
            pads.Add(candidate);
        }
        AddLilyPads(root, t, pads, rng);

        // Shore candidates: a dry cell immediately beside the water, outside the road's shoulder.
        var shores = new List<Vector2>();
        for (int z = 1; z < n - 1; z++)
        for (int x = 1; x < n - 1; x++)
        {
            var surface = t.Surf[z * n + x];
            if (surface is not (SurfaceType.Grass or SurfaceType.Mud or SurfaceType.Dirt)) continue;
            var p = new Vector2((x + 0.5f) * t.Tile, (z + 0.5f) * t.Tile);
            if (p.DistanceTo(ford) > 30f) continue;
            int ci = t.NearestCenter(p, out float roadDistance);
            if (ci >= 0 && roadDistance < t.HalfWidth[ci] + 2.1f) continue;
            bool besideWater = false;
            for (int dz = -1; dz <= 1 && !besideWater; dz++)
            for (int dx = -1; dx <= 1 && !besideWater; dx++)
                besideWater = t.Surf[(z + dz) * n + x + dx] is SurfaceType.Water or SurfaceType.Shallow;
            if (besideWater) shores.Add(p);
        }
        // Sample authored banks directly as well: around a ford the road can leave only a few
        // dry grid cells beside the water, so a tile-only scatter often makes reeds too sparse.
        if (t.Water != null)
            foreach (var river in t.Water.Rivers)
            for (int seg = 0; seg + 1 < river.Points.Length; seg++)
            {
                Vector2 a = new(river.Points[seg][0], river.Points[seg][1]);
                Vector2 b = new(river.Points[seg + 1][0], river.Points[seg + 1][1]);
                int steps = Math.Max(1, (int)MathF.Ceiling(a.DistanceTo(b) / 8f));
                Vector2 side = new(-(b - a).Normalized().Y, (b - a).Normalized().X);
                for (int k = 0; k <= steps; k++)
                {
                    Vector2 mid = a.Lerp(b, k / (float)steps);
                    if (mid.DistanceTo(ford) < 8f || mid.DistanceTo(ford) > 38f) continue;
                    foreach (float sign in new[] { -1f, 1f })
                    foreach (float extra in new[] { 1.5f, 2.5f, 3.5f })
                    {
                        Vector2 p = mid + side * sign * (river.HalfWidth(seg, k / (float)steps) + extra);
                        int x = (int)(p.X / t.Tile), z = (int)(p.Y / t.Tile);
                        if (x < 1 || z < 1 || x >= n - 1 || z >= n - 1) continue;
                        if (t.Surf[z * n + x] is SurfaceType.Water or SurfaceType.Shallow) continue;
                        int ci = t.NearestCenter(p, out float roadDistance);
                        if (ci >= 0 && roadDistance < t.HalfWidth[ci] + 2.1f) continue;
                        shores.Add(p);
                        break;
                    }
                }
            }
        // Pick separated clumps so the shoreline reads as scattered reeds, not a continuous hedge.
        var clumps = new List<Vector2>();
        while (shores.Count > 0 && clumps.Count < 28)
        {
            int pick = rng.Next(shores.Count);
            Vector2 p = shores[pick]; shores.RemoveAt(pick);
            if (clumps.Any(q => p.DistanceTo(q) < 2.8f)) continue;
            clumps.Add(p);
        }
        // A few in-water clumps on the deep side, never on the fast shallow bar.
        for(float z=ford.Y-7;z<=ford.Y+7;z+=1)
        for(float x=ford.X-7;x<=ford.X+7;x+=1)
        {
            var p=new Vector2(x,z);
            if(!DeepFordPlantSite(t,p)) continue;
            float edge=t.Water.Rivers.Min(r=>r.EdgeDistance(p));
            if(edge < -1.6f || clumps.Any(q=>p.DistanceTo(q)<2.5f)) continue;
            clumps.Add(p);
        }
        AddReeds(root, t, clumps, rng);
    }

    internal static List<Vector2> PondReedSites(TrackData t) => PondReedSites(t,new Random(192221));

    static List<Vector2> PondReedSites(TrackData t, Random rng)
    {
        var clumps=new List<Vector2>();
        if(t.Id!="ilyich" || t.Winter || t.Water==null) return clumps;
        foreach(var pond in t.Water.Ponds)
        {
            var centre=new Vector2(pond.X,pond.Z);
            for(int i=0;i<60;i++)
            {
                // Short beds separated by open shoreline, using the existing river reed kit.
                if(Noise.Hash01(i/3,0,192221)<.38f) continue;
                float angle=i*MathF.Tau/60 + ((float)rng.NextDouble()-.5f)*.05f;
                float radius=pond.R-.6f + ((float)rng.NextDouble()-.5f)*.9f;
                var p=centre+new Vector2(MathF.Cos(angle),MathF.Sin(angle))*radius;
                if(p.Y<centre.Y && Math.Abs(p.X-centre.X)<5) continue; // pier and landing
                if(t.Water.Rivers.Any(r=>r.EdgeDistance(p)<1)) continue; // open outlet
                int ci=t.NearestCenter(p,out float road);
                if(road<t.HalfWidth[ci]+3 || clumps.Any(q=>p.DistanceTo(q)<2.2f)) continue;
                clumps.Add(p);
            }
        }
        return clumps;
    }

    static void AddPondReeds(Node3D root, TrackData t)
    {
        var rng=new Random(192221);
        var clumps=PondReedSites(t,rng);
        if(clumps.Count==0)return;
        var bed=new Node3D {Name="Pond shoreline reeds"};root.AddChild(bed);
        AddReeds(bed,t,clumps,rng);
    }

    static void AddLilyPads(Node3D root, TrackData t, List<Vector2> pads, Random rng)
    {
        if (pads.Count == 0) return;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        ReadOnlySpan<Vector2> outline = new Vector2[]
        {
            new(0.02f,-0.40f), new(0.14f,-0.32f), new(0.23f,-0.14f), new(0.22f,0.10f),
            new(0.14f,0.30f), new(0.02f,0.42f), new(-0.10f,0.31f), new(-0.20f,0.13f),
            new(-0.22f,-0.10f), new(-0.12f,-0.30f),
        };
        foreach (var p in pads)
        {
            float yaw = (float)rng.NextDouble() * MathF.Tau, c = MathF.Cos(yaw), s = MathF.Sin(yaw);
            float scale = 0.75f + (float)rng.NextDouble() * 0.65f;
            float y = t.WaterLevel + 0.055f;
            Color leaf = new(0.22f + (float)rng.NextDouble() * 0.08f, 0.34f + (float)rng.NextDouble() * 0.1f, 0.17f, 1f);
            var center = new Vector3(p.X, y, p.Y);
            for (int i = 0; i < outline.Length; i++)
            {
                Vector2 a = outline[i] * scale, b = outline[(i + 1) % outline.Length] * scale;
                Vector3 va = new(p.X + a.X * c - a.Y * s, y, p.Y + a.X * s + a.Y * c);
                Vector3 vb = new(p.X + b.X * c - b.Y * s, y, p.Y + b.X * s + b.Y * c);
                // Reverse the outline winding in XZ so the flat leaf faces up.
                st.SetColor(leaf); st.SetNormal(Vector3.Up); st.AddVertex(center);
                st.SetColor(leaf.Lightened(0.12f)); st.SetNormal(Vector3.Up); st.AddVertex(vb);
                st.SetColor(leaf); st.SetNormal(Vector3.Up); st.AddVertex(va);
            }
        }
        var mat = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.PerVertex,
            Roughness = 0.95f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        root.AddChild(new MeshInstance3D
        {
            Name = "Water lilies", Mesh = st.Commit(), MaterialOverride = mat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    static void AddReeds(Node3D root, TrackData t, List<Vector2> clumps, Random rng)
    {
        if (clumps.Count == 0) return;
        var stemMesh = new CylinderMesh { TopRadius = 0.022f, BottomRadius = 0.045f, Height = 1f, RadialSegments = 5, Rings = 1 };
        stemMesh.Material = new StandardMaterial3D { AlbedoColor = new Color(0.43f, 0.39f, 0.2f), Roughness = 1f };
        var leafMesh = new BoxMesh { Size = new Vector3(0.09f, 0.62f, 0.035f) };
        leafMesh.Material = new StandardMaterial3D { AlbedoColor = new Color(0.34f, 0.46f, 0.17f), Roughness = 1f };
        var headMesh = new SphereMesh { Radius = 0.08f, Height = 0.22f, RadialSegments = 6, Rings = 3 };
        headMesh.Material = new StandardMaterial3D { AlbedoColor = new Color(0.52f, 0.43f, 0.22f), Roughness = 1f };
        var stems = new List<Transform3D>();
        var leaves = new List<Transform3D>();
        var heads = new List<Transform3D>();
        foreach (Vector2 clump in clumps)
        {
            float ground = t.MeshHeight(clump.X, clump.Y);
            for (int i = 0; i < 6; i++)
            {
                float h = 1.35f + (float)rng.NextDouble() * 0.9f;
                float yaw = (float)rng.NextDouble() * MathF.Tau;
                Vector2 offset = new((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f);
                Vector3 baseAt = new(clump.X + offset.X * 0.8f, ground, clump.Y + offset.Y * 0.8f);
                float lean = ((float)rng.NextDouble() - 0.5f) * 0.28f;
                Basis stemBasis = Basis.Identity.Rotated(Vector3.Forward, lean).Scaled(new Vector3(1, h, 1));
                stems.Add(new Transform3D(stemBasis, baseAt + Vector3.Up * (h * 0.5f)));
                heads.Add(new Transform3D(Basis.Identity, baseAt + Vector3.Up * (h - 0.02f)));
                for (int k = 0; k < 2; k++)
                {
                    float angle = yaw + (k == 0 ? 0 : MathF.PI * 0.72f);
                    var leafBasis = Basis.Identity.Rotated(Vector3.Up, angle).Rotated(Vector3.Forward, lean + 0.2f)
                        .Scaled(new Vector3(1f, 0.8f + (float)rng.NextDouble() * 0.6f, 1f));
                    Vector3 leafAt = baseAt + Vector3.Up * (h * (0.42f + 0.1f * k));
                    leaves.Add(new Transform3D(leafBasis, leafAt));
                }
            }
        }
        var stemMulti = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = stemMesh, InstanceCount = stems.Count };
        for (int i = 0; i < stems.Count; i++) stemMulti.SetInstanceTransform(i, stems[i]);
        var leafMulti = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = leafMesh, InstanceCount = leaves.Count };
        for (int i = 0; i < leaves.Count; i++) leafMulti.SetInstanceTransform(i, leaves[i]);
        var headMulti = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = headMesh, InstanceCount = heads.Count };
        for (int i = 0; i < heads.Count; i++) headMulti.SetInstanceTransform(i, heads[i]);
        root.AddChild(new MultiMeshInstance3D { Name = "Reed stalks", Multimesh = stemMulti, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        root.AddChild(new MultiMeshInstance3D { Name = "Reed leaves", Multimesh = leafMulti, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        root.AddChild(new MultiMeshInstance3D { Name = "Reed seed heads", Multimesh = headMulti, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
    }

    static uint Hash(int x, int z)
    {
        uint h = unchecked((uint)(x * 374761393 + z * 668265263));
        h = (h ^ (h >> 13)) * 1274126177u;
        return h ^ (h >> 16);
    }
}

/// <summary>Slow downstream drift in the ford and river; uses a standard material so day/night can tint it.</summary>
public partial class WaterFlow : Node3D
{
    readonly StandardMaterial3D _material;
    float _time;

    public WaterFlow(StandardMaterial3D material) => _material = material;

    public override void _Process(double delta)
    {
        _time += (float)delta;
        // U follows downstream distance; V stays across the river, without sideways drift.
        _material.Uv1Offset = new Vector3(-_time * 0.025f, 0, 0);
    }
}
