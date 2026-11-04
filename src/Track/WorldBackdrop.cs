using System;
using Godot;

namespace CCCPRacing.Track;

/// <summary>Static scenery outside the physics grid: stitched terrain, sparse woods and a fading horizon.</summary>
public partial class WorldBackdrop : Node3D
{
    public const float Extent = 224f;
    readonly ShaderMaterial _ground = new(), _scenery = new();
    ShaderMaterial _water;
    Color _waterColour = WaterMesh.Colour;

    public static float OutsideDistance(TrackData t, Vector2 p) => Math.Max(0,
        Math.Max(Math.Max(-p.X, p.X - t.WorldSize), Math.Max(-p.Y, p.Y - t.WorldSize)));

    public static float HeightAt(TrackData t, Vector2 p)
    {
        float size = t.WorldSize, d = OutsideDistance(t, p);
        var edge = p.Clamp(Vector2.Zero, Vector2.One * size);
        float h = t.MeshHeight(edge.X, edge.Y);
        if (d == 0) return h;
        var inward = (edge - p).Normalized();
        var sample = (edge + inward * 6).Clamp(Vector2.Zero, Vector2.One * size);
        float slope = Math.Clamp((h - t.MeshHeight(sample.X, sample.Y)) / 6, -0.22f, 0.22f);
        float wave = Noise.Fbm(p.X / 90, p.Y / 90, t.Winter ? 1980 : 1922, 3);
        float rolling = t.Meadow?.HeightAt(p.X,p.Y) ?? 7 + wave * (t.Winter ? 9 : 6);
        float continued = h + slope * Math.Min(d, 12) * (1 - Mathf.SmoothStep(12, 70, d));
        float height = Mathf.Lerp(continued, rolling, Mathf.SmoothStep(0, 100, d));
        if (RiverAt(t, p, out float riverDistance, out float width, out float depth, out float level))
        {
            // A wide, gentle bed: the banks at the map edge are eased to match (TrackBuilder.EaseBanksAtEdge).
            float carve = (1 - Mathf.SmoothStep(width * 0.5f, width * 0.5f + 30, riverDistance)) * Mathf.SmoothStep(0, 4, d);
            height = Mathf.Lerp(height, Math.Min(height, level - depth), carve);
        }
        return height;
    }

    /// <summary>The actual triangle height of the backdrop, for ground-following details and field overlays.</summary>
    public static float SurfaceHeightAt(TrackData t, Vector2 p)
    {
        if (OutsideDistance(t, p) == 0) return t.MeshHeight(p.X, p.Y);
        Vector2 origin = new(MathF.Floor(p.X / 8) * 8, MathF.Floor(p.Y / 8) * 8);
        float distance = OutsideDistance(t, origin + Vector2.One * 4);
        float step = distance <= 16 ? 2 : distance <= 48 ? 4 : 8;
        origin += new Vector2(MathF.Floor((p.X - origin.X) / step), MathF.Floor((p.Y - origin.Y) / step)) * step;
        while (step > 4 && RiverAt(t, origin + Vector2.One * (step / 2), out float nearRiver, out float width, out _)
            && nearRiver < width + 18)
        {
            step /= 2;
            origin += new Vector2(MathF.Floor((p.X - origin.X) / step), MathF.Floor((p.Y - origin.Y) / step)) * step;
        }
        Vector2 uv = (p - origin) / step;
        float a = HeightAt(t, origin), b = HeightAt(t, origin + new Vector2(step, 0));
        float c = HeightAt(t, origin + Vector2.One * step), d = HeightAt(t, origin + new Vector2(0, step));
        return uv.X >= uv.Y ? a + (b - a) * uv.X + (c - b) * uv.Y
            : a + (c - d) * uv.X + (d - a) * uv.Y;
    }

    public static WorldBackdrop Build(TrackData t, float[] shade, Texture2D atlas)
    {
        WorldBackdrop result = null;
        foreach (var step in BuildSteps(t, shade, atlas)) result = step ?? result;
        return result;
    }

    /// <summary>The same build in slices (a null between them: a frame may go by while a loading card runs); the last item is the backdrop.
    /// A build abandoned between slices frees the half-made backdrop.</summary>
    public static System.Collections.Generic.IEnumerable<WorldBackdrop> BuildSteps(TrackData t, float[] shade, Texture2D atlas)
    {
        var root = new WorldBackdrop { Name = "WorldBackdrop", _waterColour = WaterMesh.ColourFor(t) };
        bool handedOver = false;
        try
        {
            var shader = new Shader { Code = ShaderCode };
            root._ground.Shader = root._scenery.Shader = shader;
            root._ground.SetShaderParameter("atlas", atlas);
            root._ground.SetShaderParameter("surface_count", (float)Surfaces.Count);
            root._ground.SetShaderParameter("tile_size", t.Tile);
            root._scenery.SetShaderParameter("textured", false);
            root.SetLook(Colors.White, new Color(0.62f, 0.64f, 0.62f));
            MeshInstance3D part = null;
            foreach (var step in BuildGroundSteps(t, shade, root._ground)) { if (step != null) part = step; else yield return null; }
            root.AddChild(part);
            yield return null;
            foreach (var step in BuildScenerySteps(t, root._scenery)) { if (step != null) part = step; else yield return null; }
            root.AddChild(part);
            yield return null;
            var water = root.BuildWater(t);
            if (water != null) root.AddChild(water);
            handedOver = true;
            yield return root;
        }
        finally { if (!handedOver) root.Free(); }
    }

    public void SetLook(Color tint, Color sky, float litMix = 0, Color? unlit = null)
    {
        foreach (var material in new[] { _ground, _scenery })
        {
            material.SetShaderParameter("day_tint", new Vector3(tint.R, tint.G, tint.B));
            material.SetShaderParameter("horizon", sky);
            material.SetShaderParameter("lit_mix", litMix);
        }
        if (_water != null)
        {
            _water.SetShaderParameter("water_color", _waterColour * (unlit ?? tint));
            _water.SetShaderParameter("horizon", sky);
        }
    }

    static System.Collections.Generic.IEnumerable<MeshInstance3D> BuildGroundSteps(TrackData t, float[] shade, ShaderMaterial material)
    {
        var slice = System.Diagnostics.Stopwatch.StartNew();
        var mesh = new SurfaceTool(); mesh.Begin(Mesh.PrimitiveType.Triangles);
        float size = t.WorldSize;
        void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            var centre = (a + b + c + d) * 0.25f;
            if ((a.DistanceTo(b) > 4 || a.DistanceTo(d) > 4)
                && RiverAt(t, centre, out float nearRiver, out float riverWidth, out _)
                && nearRiver < riverWidth + 18)
            {
                var ab = (a + b) * 0.5f; var bc = (b + c) * 0.5f;
                var cd = (c + d) * 0.5f; var da = (d + a) * 0.5f;
                Quad(a, ab, centre, da); Quad(ab, b, bc, centre);
                Quad(centre, bc, c, cd); Quad(da, centre, cd, d); return;
            }
            var edge = centre.Clamp(Vector2.Zero, Vector2.One * (size - 0.001f));
            int ix = (int)(edge.X / t.Tile), iz = (int)(edge.Y / t.Tile);
            float distance = OutsideDistance(t, centre);
            var surface = t.TileSurface(ix, iz);
            // Preserve the actual border palette first; further out, let fields and woods vary.
            if (distance > 14 && surface is not (SurfaceType.Water or SurfaceType.Shallow))
            {
                if (t.Winter) surface = SurfaceType.DeepSnow;
                else if (t.Id is "ilyich" or "bonus" && Noise.Hash01((int)MathF.Floor(centre.X / 65), (int)MathF.Floor(centre.Y / 55), 928) > 0.45f)
                    surface = SurfaceType.Field;
                else surface = SurfaceType.Grass;
            }
            if (RiverAt(t, centre, out float rd, out float rw, out _) && rd < rw * 0.5f)
                surface = t.Water.Frozen ? SurfaceType.Ice : SurfaceType.Water;
            int variant = t.Variant[iz * t.N + ix];
            float plain = 0.96f + 0.08f * Noise.Hash01(ix, iz, 99);
            float shadow = Mathf.Lerp(shade[iz * t.N + ix], 1, Mathf.SmoothStep(0, 12, distance));
            Vector3 At(Vector2 p) => new(p.X, HeightAt(t, p), p.Y);
            void Tri(Vector2 pa, Vector2 pb, Vector2 pc)
            {
                var va = At(pa); var vb = At(pb); var vc = At(pc);
                var normal = (vc - va).Cross(vb - va).Normalized();
                if (normal.Y < 0) { normal = -normal; (pb, pc) = (pc, pb); }
                float relief = Mathf.Lerp(3.4f, 1.1f, Mathf.SmoothStep(0, 18, distance));
                float light = Math.Clamp(0.92f + relief * (normal.Dot(TerrainMesh.SunDir) - TerrainMesh.SunDir.Y), 0.35f, 1.2f) * plain * shadow;
                foreach (var p in new[] { pa, pb, pc })
                {
                    mesh.SetNormal(normal); mesh.SetColor(new Color(light, light, light));
                    mesh.SetUV(new Vector2((int)surface, variant));
                    mesh.SetUV2(new Vector2(plain, Mathf.SmoothStep(100, 200, OutsideDistance(t, p))));
                    mesh.AddVertex(At(p));
                }
            }
            Tri(a, b, c); Tri(a, c, d);
        }
        // A regular world grid keeps fields rectangular and avoids radial fans at the corners.
        // Refine the first strip to the original two-metre edge vertices; coarsen farther out.
        for (float z = -Extent; z < size + Extent; z += 8)
        for (float x = -Extent; x < size + Extent; x += 8)
        {
            if (x >= 0 && z >= 0 && x + 8 <= size && z + 8 <= size) continue;
            if (slice.ElapsedMilliseconds > 10) { yield return null; slice.Restart(); }
            float distance = OutsideDistance(t, new Vector2(x + 4, z + 4));
            int divisions = distance <= 16 ? 4 : distance <= 48 ? 2 : 1;
            float step = 8f / divisions;
            for (int iz = 0; iz < divisions; iz++)
            for (int ix = 0; ix < divisions; ix++)
            {
                var a = new Vector2(x + ix * step, z + iz * step);
                Quad(a, a + new Vector2(step, 0), a + Vector2.One * step, a + new Vector2(0, step));
            }
        }
        yield return null;      // (the commit below is one native call)
        yield return new MeshInstance3D { Name = "OuterTerrain", Mesh = mesh.Commit(), MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
    }

    static System.Collections.Generic.IEnumerable<MeshInstance3D> BuildScenerySteps(TrackData t, ShaderMaterial material)
    {
        var slice = System.Diagnostics.Stopwatch.StartNew();
        var mesh = new SurfaceTool(); mesh.Begin(Mesh.PrimitiveType.Triangles);
        void Tri(Vector3 a, Vector3 b, Vector3 c, Color color)
        {
            var normal = (c - a).Cross(b - a).Normalized(); if (normal.Y < 0) normal = -normal;
            float light = Math.Clamp(0.70f + normal.Dot(TerrainMesh.SunDir) * 0.4f, 0.45f, 1.1f);
            mesh.SetNormal(normal); mesh.SetColor(color.SrgbToLinear() * light);
            foreach (var p in new[] { a, b, c })
            {
                mesh.SetUV2(new Vector2(1, Mathf.SmoothStep(100, 200, OutsideDistance(t, new Vector2(p.X, p.Z)))));
                mesh.AddVertex(p);
            }
        }
        void Cone(Vector3 at, float radius, float height, Color color, float topRadius = 0)
        {
            for (int i = 0; i < 6; i++)
            {
                float a = i * MathF.Tau / 6, b = (i + 1) * MathF.Tau / 6;
                var da = new Vector3(MathF.Cos(a), 0, MathF.Sin(a)); var db = new Vector3(MathF.Cos(b), 0, MathF.Sin(b));
                var tipA = at + Vector3.Up * height + da * topRadius; var tipB = at + Vector3.Up * height + db * topRadius;
                Tri(at + da * radius, at + db * radius, tipB, color);
                if (topRadius > 0) Tri(at + da * radius, tipB, tipA, color);
            }
        }
        for (float z = -110; z < t.WorldSize + 110; z += 9)
        for (float x = -110; x < t.WorldSize + 110; x += 9)
        {
            if (slice.ElapsedMilliseconds > 10) { yield return null; slice.Restart(); }
            var p = new Vector2(x + Noise.Hash01((int)x, (int)z, 134) * 6, z + Noise.Hash01((int)x, (int)z, 135) * 6);
            float distance = OutsideDistance(t, p);
            if (distance < 10 || distance > 110) continue;
            float cluster = Noise.Value(p.X / 38, p.Y / 38, 928);
            if (cluster < (t.Winter ? 0.30f : 0.48f) || Noise.Hash01((int)x, (int)z, 136) < 0.18f) continue;
            var edge = p.Clamp(Vector2.Zero, Vector2.One * (t.WorldSize - 0.001f));
            if (t.TileSurface((int)(edge.X / t.Tile), (int)(edge.Y / t.Tile)) is SurfaceType.Water or SurfaceType.Shallow) continue;
            if (RiverAt(t, p, out float riverDistance, out float riverWidth, out _) && riverDistance < riverWidth * 0.5f + 7) continue;
            float h = 5 + Noise.Hash01((int)x, (int)z, 137) * 5;
            var at = new Vector3(p.X, HeightAt(t, p), p.Y);
            Cone(at, 0.20f, h * 0.7f, new Color(0.27f, 0.20f, 0.13f), 0.10f);
            bool conifer = t.Winter || Noise.Hash01((int)x, (int)z, 138) > 0.55f;
            if (conifer)
            {
                for (int tier = 0; tier < 3; tier++)
                {
                    float radius = h * (0.26f - tier * 0.055f), height = h * 0.5f;
                    var bottom = at + Vector3.Up * (h * 0.12f + tier * h * 0.22f);
                    Cone(bottom, radius, height, new Color(0.075f, 0.18f, 0.10f));
                    if (t.Winter) Cone(bottom + Vector3.Up * height * 0.20f, radius * 0.82f, height * 0.82f, new Color(0.86f, 0.90f, 0.96f));
                }
            }
            else
            {
                var color = t.Autumn ? new Color(0.57f, 0.45f, 0.12f) : new Color(0.23f, 0.39f, 0.14f);
                Cone(at + Vector3.Up * h * 0.36f, h * 0.28f, h * 0.36f, color, h * 0.20f);
                Cone(at + Vector3.Up * h * 0.72f, h * 0.20f, h * 0.28f, color.Lightened(0.06f));
            }
        }
        if (t.Zone)
        {
            // Low, distant industrial blocks; silhouettes stay outside the playable grid.
            for (int i = 0; i < 10; i++)
            {
                var p = new Vector2(t.WorldSize + 65 + i % 3 * 12, 40 + i * 24);
                var at = new Vector3(p.X, HeightAt(t, p), p.Y);
                float width = 7 + i % 3 * 3, height = 7 + i % 4 * 4;
                Vector3 a = at + new Vector3(-width, 0, -5), b = at + new Vector3(width, 0, -5),
                    c = at + new Vector3(width, 0, 5), d = at + new Vector3(-width, 0, 5), up = Vector3.Up * height;
                var color = new Color(0.36f, 0.37f, 0.34f);
                Tri(a, b, b + up, color); Tri(a, b + up, a + up, color);
                Tri(b, c, c + up, color); Tri(b, c + up, b + up, color);
                Tri(c, d, d + up, color); Tri(c, d + up, c + up, color);
                Tri(d, a, a + up, color); Tri(d, a + up, d + up, color);
                Tri(a + up, b + up, c + up, color); Tri(a + up, c + up, d + up, color);
            }
        }
        yield return null;      // (the commit below is one native call)
        yield return new MeshInstance3D { Name = "OuterWoods", Mesh = mesh.Commit(), MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
    }

    const string ShaderCode = @"
shader_type spatial;
render_mode cull_disabled, specular_disabled, diffuse_lambert;
uniform sampler2D atlas : source_color, filter_nearest;
uniform bool textured = true;
uniform float surface_count = 16.0;
uniform float tile_size = 2.0;
uniform vec3 day_tint = vec3(1.0);
uniform float lit_mix = 0.0;
uniform vec4 horizon : source_color = vec4(0.62, 0.64, 0.62, 1.0);
varying vec3 world_position;
void vertex() { world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; }
void fragment() {
    vec3 tex = vec3(1.0);
    vec3 color = COLOR.rgb;
    if (textured) {
        vec2 pixel_uv = mix(vec2(0.03125), vec2(0.96875), fract(world_position.xz / tile_size));
        tex = texture(atlas, (UV + pixel_uv) / vec2(surface_count, 4.0)).rgb;
        color *= tex;
    }
    float fade = max(UV2.y, smoothstep(175.0, 245.0, length(VERTEX)));
    EMISSION = mix(color * day_tint, horizon.rgb, fade);
    ALBEDO = (textured ? tex * UV2.x : COLOR.rgb) * lit_mix * (1.0 - fade);
    ROUGHNESS = 1.0;
    SPECULAR = 0.0;
}
";
}
