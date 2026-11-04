using System;
using Godot;

namespace CCCPRacing.Track;

/// <summary>Builds the ground mesh: one textured quad per tile, flat shaded with baked sunlight.</summary>
public static class TerrainMesh
{
    /// <summary>Afternoon sun, fairly low (~37°) so slopes and crests read from the high camera.</summary>
    public static readonly Vector3 SunDir = new Vector3(-0.6f, 0.62f, 0.5f).Normalized();
    /// <summary>Brightness of ground in the shadow of a hill or crest.</summary>
    const float HillShadow = 0.58f;

    public static MeshInstance3D Build(TrackData t)
    {
        MeshInstance3D result = null;
        foreach (var step in BuildSteps(t)) result = step ?? result;
        return result;
    }

    /// <summary>The same build in slices (a null between them: a frame may go by while a loading card runs); the last item is the terrain.</summary>
    public static System.Collections.Generic.IEnumerable<MeshInstance3D> BuildSteps(TrackData t)
    {
        var slice = System.Diagnostics.Stopwatch.StartNew();
        int n = t.N;
        int triCount = n * n * 2;
        var verts = new Vector3[triCount * 3];
        var uvs = new Vector2[triCount * 3];
        var cols = new Color[triCount * 3];
        var nrms = new Vector3[triCount * 3];
        var uv2s = new Vector2[triCount * 3];      // x: the tile's tint without relief or hill shadow (the lit night material)
        int k = 0;
        const float cellU = 1f / Surfaces.Count, cellV = 1f / TileAtlas.Variants;
        const float inset = 0.5f / 16f;

        var shade = new float[n * n];
        foreach (var _ in CastShadowsSteps(t, shade)) { LoadProf.At(); yield return null; slice.Restart(); }
        LoadProf.At(); yield return null;
        slice.Restart();

        for (int iz = 0; iz < n; iz++)
        {
        if (slice.ElapsedMilliseconds > 10) { LoadProf.At(); yield return null; slice.Restart(); }
        for (int ix = 0; ix < n; ix++)
        {
            float x0 = ix * t.Tile, z0 = iz * t.Tile, x1 = x0 + t.Tile, z1 = z0 + t.Tile;
            var v00 = new Vector3(x0, t.VertexHeight(ix, iz), z0);
            var v10 = new Vector3(x1, t.VertexHeight(ix + 1, iz), z0);
            var v01 = new Vector3(x0, t.VertexHeight(ix, iz + 1), z1);
            var v11 = new Vector3(x1, t.VertexHeight(ix + 1, iz + 1), z1);

            int s = (int)t.Surf[iz * n + ix];
            // The dacha's finely textured, feathered yard supplies its own visible surface.
            // Keep grass beneath its soft edges while retaining gravel in the physics map.
            if (t.Id == "ilyich" && s == (int)SurfaceType.Gravel
                && x0 >= 198 && x0 <= 290 && z0 >= 160 && z0 <= 194)
            {
                var at = new Vector2(x0 + t.Tile * 0.5f, z0 + t.Tile * 0.5f);
                int road = t.NearestCenter(at, out float distance);
                if (distance >= t.HalfWidth[road] && (PartyDacha.InGrounds(at) || PartyDacha.OnDrive(at)))
                    s = (int)SurfaceType.Grass;
            }
            int var = t.Variant[iz * n + ix];
            float u0 = s * cellU + inset * cellU, u1 = (s + 1) * cellU - inset * cellU;
            float w0 = var * cellV + inset * cellV, w1 = (var + 1) * cellV - inset * cellV;
            var t00 = new Vector2(u0, w0); var t10 = new Vector2(u1, w0);
            var t01 = new Vector2(u0, w1); var t11 = new Vector2(u1, w1);

            float plain = 0.96f + 0.08f * Noise.Hash01(ix, iz, 99), tint = plain * shade[iz * n + ix];
            // Bonus north rim: expose the existing steep faces visually, keeping the
            // terrain vertices, surfaces and tyre grip exactly as authored.
            float rock = NorthRock(t, s, (v00 + v10 + v01 + v11) * 0.25f,
                Math.Max(Math.Abs(v10.Y-v00.Y), Math.Abs(v11.Y-v01.Y)) / t.Tile,
                Math.Max(Math.Abs(v01.Y-v00.Y), Math.Abs(v11.Y-v10.Y)) / t.Tile);
            for (int j = k; j < k + 6; j++) uv2s[j] = new Vector2(plain, rock);
            Tri(verts, uvs, cols, nrms, ref k, v00, v10, v11, t00, t10, t11, tint);
            Tri(verts, uvs, cols, nrms, ref k, v00, v11, v01, t00, t11, t01, tint);
        }
        }

        ImageTexture atlas = null;
        foreach (var step in TileAtlas.BuildSteps(t.Autumn)) { if (step != null) atlas = step; else { yield return null; slice.Restart(); } }
        var mat = new StandardMaterial3D { AlbedoTexture = atlas };    // only carries the atlas for the backdrop
        // The day's ground is the lit shader with no lit part: the same unshaded baked colours, but the texture
        // lookup is held inside its tile, so MSAA's extrapolated edge pixels never reach a neighbouring tile.
        var dayMaterial = NightMaterial(mat.AlbedoTexture);
        // Mobile selects at most eight spotlights per mesh. A map-sized AABB makes
        // distant vehicle/search lights compete with the player's headlights everywhere.
        // Small independent instances give each patch of ground its own local light list.
        var terrain = new MeshInstance3D { Name = "Terrain" };
        bool handedOver = false;
        try {      // (a build abandoned between slices frees the half-made terrain)
        int chunkTiles = Math.Max(1, (int)MathF.Floor(32f / t.Tile));
        for (int iz = 0; iz < n; iz += chunkTiles)
        for (int ix = 0; ix < n; ix += chunkTiles)
        {
            int width = Math.Min(chunkTiles, n - ix), depth = Math.Min(chunkTiles, n - iz);
            int count = width * depth * 6;
            var cv = new Vector3[count]; var cuv = new Vector2[count];
            var cc = new Color[count]; var cn = new Vector3[count]; var cuv2 = new Vector2[count];
            bool first = ix == 0 && iz == 0;
            Vector3 offset = first ? Vector3.Zero : new Vector3((ix + width / 2f) * t.Tile, 0, (iz + depth / 2f) * t.Tile);
            for (int row = 0; row < depth; row++)
            {
                int start = ((iz + row) * n + ix) * 6, dst = row * width * 6, length = width * 6;
                Array.Copy(verts, start, cv, dst, length); Array.Copy(uvs, start, cuv, dst, length);
                Array.Copy(cols, start, cc, dst, length); Array.Copy(nrms, start, cn, dst, length);
                Array.Copy(uv2s, start, cuv2, dst, length);
            }
            for (int vertex = 0; vertex < count; vertex++) cv[vertex] -= offset;
            using var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = cv;
            arrays[(int)Mesh.ArrayType.TexUV] = cuv;
            arrays[(int)Mesh.ArrayType.Color] = cc;
            arrays[(int)Mesh.ArrayType.Normal] = cn;
            arrays[(int)Mesh.ArrayType.TexUV2] = cuv2;
            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            mesh.SurfaceSetMaterial(0, dayMaterial);
            if (first) terrain.Mesh = mesh;
            else terrain.AddChild(new MeshInstance3D { Name = $"TerrainChunk{ix}_{iz}", Mesh = mesh, Position = offset });
            if (slice.ElapsedMilliseconds > 10) { LoadProf.At(); yield return null; slice.Restart(); }
        }
        // ILYICH'S LAMP's coarse tile map leaves a visibly square edge beside the narrow road.
        // This render-only apron softens that edge without changing the ground samples or grip.
        if (t.Id == "ilyich")
        {
            MeshInstance3D verges = null;
            foreach (var step in BuildIlyichVergesSteps(t)) { if (step != null) verges = step; else { LoadProf.At(); yield return null; slice.Restart(); } }
            terrain.AddChild(verges);
        }
        LoadProf.At(); yield return null;
        slice.Restart();
        WorldBackdrop backdrop = null;
        foreach (var step in WorldBackdrop.BuildSteps(t, shade, mat.AlbedoTexture)) { if (step != null) backdrop = step; else { LoadProf.At(); yield return null; slice.Restart(); } }
        terrain.AddChild(backdrop);
        handedOver = true;
        yield return terrain;
        } finally { if (!handedOver) terrain.Free(); }
    }

    /// <summary>Switch all terrain patches together, leaving verges and the backdrop on their own materials.</summary>
    public static void SetLightingMaterial(MeshInstance3D terrain, Material material)
    {
        if (terrain.MaterialOverride == material) return;
        terrain.MaterialOverride = material;
        foreach (var child in terrain.GetChildren())
            if (child is MeshInstance3D mesh && mesh.Name.ToString().StartsWith("TerrainChunk", StringComparison.Ordinal))
                mesh.MaterialOverride = material;
    }

    static float NorthRock(TrackData t, int surface, Vector3 p, float dx, float dz)
    {
        if(t.Id!="bonus" || p.Z>110 || surface!=(int)SurfaceType.Grass && surface!=(int)SurfaceType.DeadGrass && surface!=(int)SurfaceType.Mud) return 0;
        float slope=MathF.Sqrt(dx*dx+dz*dz);
        if(slope<.26f) return 0;
        int road=t.NearestCenter(new Vector2(p.X,p.Z),out float distance);
        float roadside=SmoothStep(t.HalfWidth[road]+2,t.HalfWidth[road]+6,distance);
        return SmoothStep(.26f,.72f,slope)*(1-SmoothStep(65,110,p.Z))*roadside;
    }

    readonly struct VergeVertex
    {
        public readonly Vector3 Position;
        public readonly Vector2 UV;
        public readonly float Edge;
        public VergeVertex(Vector3 position, Vector2 uv, float edge) { Position = position; UV = uv; Edge = edge; }
    }

    /// <summary>
    /// A low-opacity dirt texture clipped to the terrain's own triangles. Its mesh shares every
    /// underlying grid plane, so it cannot float across the stepped tile edge or cover the road.
    /// </summary>
    static System.Collections.Generic.IEnumerable<MeshInstance3D> BuildIlyichVergesSteps(TrackData t)
    {
        var slice = System.Diagnostics.Stopwatch.StartNew();
        var vertices = new System.Collections.Generic.List<Vector3>();
        var uvs = new System.Collections.Generic.List<Vector2>();
        var colors = new System.Collections.Generic.List<Color>();
        var normals = new System.Collections.Generic.List<Vector3>();
        bool BuildingNear(Vector2 p)
        {
            foreach (var prop in t.Props)
            {
                if (prop.Type is not ("barn" or "bigbarn" or "startbarn" or "church" or "izba" or "silo" or "hangar" or "barracks" or "ruinbarn" or "radar")) continue;
                float radius = prop.Type switch { "startbarn" => 12f, "hangar" => 15f, "bigbarn" => 11f, "church" or "silo" => 7f, _ => 6f };
                if (p.DistanceSquaredTo(new Vector2(prop.Position.X, prop.Position.Z)) < radius * radius) return true;
            }
            return false;
        }
        bool Eligible(SurfaceType surface) => surface is SurfaceType.Grass or SurfaceType.DeadGrass or SurfaceType.Dirt or SurfaceType.Mud;
        // Cache the road-edge distance at the terrain grid vertices. Rasterize the narrow road
        // neighborhood from centerline samples instead of searching the whole line per vertex.
        int vertexWidth = t.N + 1;
        var edgeAt = new float[vertexWidth * vertexWidth];
        Array.Fill(edgeAt, float.PositiveInfinity);
        for (int ci = 0; ci < t.Center.Length; ci++)
        {
            Vector2 c = t.Center[ci];
            float width = t.HalfWidth[ci], reach = width + 8f;
            int minX = Math.Max(0, (int)MathF.Floor((c.X - reach) / t.Tile));
            int maxX = Math.Min(t.N, (int)MathF.Ceiling((c.X + reach) / t.Tile));
            int minZ = Math.Max(0, (int)MathF.Floor((c.Y - reach) / t.Tile));
            int maxZ = Math.Min(t.N, (int)MathF.Ceiling((c.Y + reach) / t.Tile));
            for (int vz = minZ; vz <= maxZ; vz++)
            for (int vx = minX; vx <= maxX; vx++)
            {
                float distance = new Vector2(vx * t.Tile - c.X, vz * t.Tile - c.Y).Length();
                if (distance > reach) continue;
                int index = vz * vertexWidth + vx;
                edgeAt[index] = Math.Min(edgeAt[index], distance - t.RoadHalfWidth(ci,new Vector2(vx*t.Tile,vz*t.Tile)));
            }
        }
        float OuterLimit(int ix, int iz)
        {
            // Broad, slowly varying scallops make the outer edge look worn instead of geometric.
            float noise = Noise.Hash01(ix / 2, iz / 2, 615);
            return 3.8f + (noise - 0.5f) * 1.4f;
        }
        VergeVertex At(float x, float z, float edge, float localX, float localZ)
        {
            // Tile-local UVs select one variant from the 64x16 strip and span it across this cell.
            float u = Mathf.Lerp(0.25f, 0.5f, localX);
            float v = localZ;
            return new VergeVertex(new Vector3(x, t.MeshHeight(x, z) + 0.045f, z), new Vector2(u, v), edge);
        }
        VergeVertex Mix(VergeVertex a, VergeVertex b, float f) => new(
            a.Position.Lerp(b.Position, f), a.UV.Lerp(b.UV, f), Mathf.Lerp(a.Edge, b.Edge, f));
        System.Collections.Generic.List<VergeVertex> Clip(System.Collections.Generic.List<VergeVertex> input, float boundary, bool above)
        {
            var output = new System.Collections.Generic.List<VergeVertex>(5);
            if (input.Count == 0) return output;
            VergeVertex previous = input[^1];
            bool previousInside = above ? previous.Edge >= boundary : previous.Edge <= boundary;
            foreach (var current in input)
            {
                bool currentInside = above ? current.Edge >= boundary : current.Edge <= boundary;
                if (currentInside != previousInside)
                {
                    float f = (boundary - previous.Edge) / (current.Edge - previous.Edge);
                    output.Add(Mix(previous, current, f));
                }
                if (currentInside) output.Add(current);
                previous = current;
                previousInside = currentInside;
            }
            return output;
        }
        float Fade(float edge) => 0.18f * SmoothStep(0.25f, 0.85f, edge) * (1f - SmoothStep(3.15f, 4.6f, edge));
        void AddPolygon(System.Collections.Generic.List<VergeVertex> polygon)
        {
            if (polygon.Count < 3) return;
            VergeVertex a = polygon[0];
            for (int k = 1; k + 1 < polygon.Count; k++)
            {
                VergeVertex b = polygon[k], c = polygon[k + 1];
                Vector3 face = (b.Position - a.Position).Cross(c.Position - a.Position);
                if (face.LengthSquared() < 1e-8f) continue;
                Vector3 normal = face.Normalized();
                if (normal.Y < 0) normal = -normal;
                foreach (var q in new[] { a, b, c })
                {
                    vertices.Add(q.Position); uvs.Add(q.UV);
                    float alpha = Fade(q.Edge);
                    colors.Add(new Color(1f, 1f, 1f, alpha));
                    normals.Add(normal);
                }
            }
        }
        void PaintTriangle(VergeVertex a, VergeVertex b, VergeVertex c, float outer)
        {
            var polygon = new System.Collections.Generic.List<VergeVertex> { a, b, c };
            polygon = Clip(polygon, 0.25f, true);
            polygon = Clip(polygon, outer, false);
            AddPolygon(polygon);
        }

        for (int iz = 0; iz < t.N; iz++)
        {
        if (slice.ElapsedMilliseconds > 10) { yield return null; slice.Restart(); }
        for (int ix = 0; ix < t.N; ix++)
        {
            SurfaceType surface = t.Surf[iz * t.N + ix];
            if (!Eligible(surface)) continue;
            float x0 = ix * t.Tile, z0 = iz * t.Tile, x1 = x0 + t.Tile, z1 = z0 + t.Tile;
            Vector2 center = new((x0 + x1) * 0.5f, (z0 + z1) * 0.5f);
            if (BuildingNear(center)) continue;
            int v00i = iz * vertexWidth + ix, v10i = v00i + 1;
            int v01i = v00i + vertexWidth, v11i = v01i + 1;
            if (float.IsPositiveInfinity(edgeAt[v00i]) && float.IsPositiveInfinity(edgeAt[v10i])
                && float.IsPositiveInfinity(edgeAt[v01i]) && float.IsPositiveInfinity(edgeAt[v11i])) continue;
            float outer = OuterLimit(ix, iz);
            float EdgeAt(int index) => float.IsPositiveInfinity(edgeAt[index]) ? 100f : edgeAt[index];
            VergeVertex v00 = At(x0, z0, EdgeAt(v00i), 0f, 0f);
            VergeVertex v10 = At(x1, z0, EdgeAt(v10i), 1f, 0f);
            VergeVertex v01 = At(x0, z1, EdgeAt(v01i), 0f, 1f);
            VergeVertex v11 = At(x1, z1, EdgeAt(v11i), 1f, 1f);
            // Match TerrainMesh's exact (00)-(11) diagonal and vertex heights.
            PaintTriangle(v00, v10, v11, outer);
            PaintTriangle(v00, v11, v01, outer);
        }
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = colors.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
        var vergeMesh = new ArrayMesh();
        if (vertices.Count > 0) vergeMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        var vergeMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoTexture = TileAtlas.Strip(SurfaceType.Gravel, 614),
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        if (vertices.Count > 0) vergeMesh.SurfaceSetMaterial(0, vergeMat);
        yield return new MeshInstance3D { Mesh = vergeMesh, Name = "RoadVerges", CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
    }

    static float SmoothStep(float a, float b, float x)
    {
        float t = Math.Clamp((x - a) / (b - a), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    /// <summary>
    /// The terrain after sunset (Race.DayNight): the baked day light as emission, times baked_tint
    /// (white, lit_mix 0: exactly the unshaded day material), plus the plain tiles lit for real by
    /// lit_mix, so the moving sun, the moon, headlights and lamps light the ground.
    /// </summary>
    public static ShaderMaterial NightMaterial(Texture2D atlas)
    {
        var mat = new ShaderMaterial { Shader = new Shader { Code = NightShader } };
        mat.SetShaderParameter("atlas", atlas);
        mat.SetShaderParameter("cells", new Vector2(Surfaces.Count, TileAtlas.Variants));
        return mat;
    }

    const string NightShader = @"
shader_type spatial;
render_mode cull_disabled, specular_disabled, diffuse_lambert;
uniform sampler2D atlas : source_color, filter_nearest;
uniform vec3 baked_tint = vec3(1.0);
uniform float lit_mix = 0.0;
uniform vec2 cells = vec2(16.0, 4.0);
const float INSET = 0.03125;       // half a texel of the 16 px tile (TerrainMesh)
varying flat vec2 cell;
varying vec3 terrain_world;
void vertex() {
    cell = floor(UV * cells);
    terrain_world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}
void fragment() {
	// A tile's UVs span exactly its inset cell; an edge pixel's extrapolated UV is held inside it (MSAA).
	vec2 uv = clamp(UV, (cell + INSET) / cells, (cell + 1.0 - INSET) / cells);
	vec3 tex = texture(atlas, uv).rgb;
    if (UV2.y > 0.001) {
        // Coherent mineral beds, thin horizontal seams and staggered fractures.
        // World-space coordinates keep the layers continuous across terrain chunks.
        vec3 p = terrain_world;
        float bed = p.y * 1.15 + sin(p.x * 0.045) * 0.7 + sin(p.z * 0.08) * 0.3;
        float layer = floor(bed);
        float mineral = fract(sin(layer * 73.17) * 43758.5453);
        vec3 stone = mix(vec3(0.22,0.25,0.25), vec3(0.39,0.38,0.34), mineral);
        float seam = 1.0-smoothstep(0.025,0.085,min(fract(bed),1.0-fract(bed)));
        float crack = abs(fract((p.x+p.z*0.23)*0.21 + floor(bed*0.25)*0.37)-0.5);
        stone *= 1.0 - seam*0.23 - (1.0-smoothstep(0.008,0.025,crack))*0.27;
        float grain = fract(sin(dot(floor(p.xz*7.0)+floor(p.y*9.0),vec2(127.1,311.7)))*43758.5453);
        stone *= 0.94 + grain*0.12;
        tex = mix(tex,stone,UV2.y);
    }
	EMISSION = tex * COLOR.rgb * baked_tint;
	ALBEDO = tex * UV2.x * lit_mix;
	ROUGHNESS = 1.0;
	METALLIC = 0.0;
	SPECULAR = 0.0;
}
";

    /// <summary>
    /// Hills cast shadows: from each cell centre, march towards the sun over the height grid; if the
    /// ground rises above the sun ray the cell lies in shadow (a soft edge from the last metre of
    /// clearance). Baked once, so it costs nothing while racing.
    /// </summary>
    static System.Collections.Generic.IEnumerable<int> CastShadowsSteps(TrackData t, float[] shade)
    {
        var slice = System.Diagnostics.Stopwatch.StartNew();
        int n = t.N;
        var dir = new Vector2(SunDir.X, SunDir.Z);
        float rise = SunDir.Y / dir.Length();          // metres up per metre towards the sun
        dir = dir.Normalized();
        float step = t.Tile * 0.75f, reach = 90f;
        for (int iz = 0; iz < n; iz++)
        {
        if (slice.ElapsedMilliseconds > 10) { yield return 0; slice.Restart(); }
        for (int ix = 0; ix < n; ix++)
        {
            float x = (ix + 0.5f) * t.Tile, z = (iz + 0.5f) * t.Tile;
            float h0 = t.MeshHeight(x, z) + 0.2f, clear = float.MaxValue;
            for (float d = step; d < reach; d += step)
            {
                float px = x + dir.X * d, pz = z + dir.Y * d;
                if (px < 0 || pz < 0 || px >= n * t.Tile || pz >= n * t.Tile) break;
                clear = Math.Min(clear, h0 + d * rise - t.MeshHeight(px, pz));
                if (clear < -1f) break;
            }
            float k = Math.Clamp((clear + 1f) / 1.5f, 0f, 1f);    // 0 deep in shadow .. 1 lit
            shade[iz * n + ix] = Mathf.Lerp(HillShadow, 1f, k);
        }
        }
    }

    static void Tri(Vector3[] v, Vector2[] uv, Color[] c, Vector3[] nrm, ref int k,
        Vector3 a, Vector3 b, Vector3 d, Vector2 ta, Vector2 tb, Vector2 td, float tint)
    {
        Vector3 n = (d - a).Cross(b - a).Normalized();
        if (n.Y < 0) n = -n;
        nrm[k] = nrm[k + 1] = nrm[k + 2] = n;
        // Relief shading, exaggerated around flat ground so crests, dips and jump ramps read
        // from the high camera: flat = 0.92, every few degrees of slope shows.
        float lit = Math.Clamp(0.92f + 3.4f * (n.Dot(SunDir) - SunDir.Y), 0.35f, 1.2f) * tint;
        var col = new Color(lit, lit, lit);
        v[k] = a; uv[k] = ta; c[k++] = col;
        v[k] = b; uv[k] = tb; c[k++] = col;
        v[k] = d; uv[k] = td; c[k++] = col;
    }
}

/// <summary>Procedural 16×16 pixel tiles, one column per surface, a few variants each.</summary>
public static class TileAtlas
{
    public const int Size = 16, Variants = 4;

    public static ImageTexture Build(bool autumn = false)
    {
        ImageTexture result = null;
        foreach (var step in BuildSteps(autumn)) result = step ?? result;
        return result;
    }

    /// <summary>The same atlas in slices (a null between them: a frame may go by while a loading card runs); the last item is the texture.</summary>
    public static System.Collections.Generic.IEnumerable<ImageTexture> BuildSteps(bool autumn = false)
    {
        var slice = System.Diagnostics.Stopwatch.StartNew();
        var img = Image.CreateEmpty(Size * Surfaces.Count, Size * Variants, false, Image.Format.Rgb8);
        var rng = new RandomNumberGenerator { Seed = 1961 };
        for (int s = 0; s < Surfaces.Count; s++)
        for (int v = 0; v < Variants; v++)
        {
            Paint(img, (SurfaceType)s, v, s * Size, v * Size, rng, autumn);
            if (slice.ElapsedMilliseconds > 10) { yield return null; slice.Restart(); }
        }
        yield return null;
        yield return ImageTexture.CreateFromImage(img);
    }

    /// <summary>One surface's tiles side by side (all four variants, 64 x 16 px), for meshes that want the ground's own pixels (the snow banks).</summary>
    public static ImageTexture Strip(SurfaceType s, ulong seed)
    {
        var img = Image.CreateEmpty(Size * Variants, Size, false, Image.Format.Rgb8);
        var rng = new RandomNumberGenerator { Seed = seed };
        for (int v = 0; v < Variants; v++) Paint(img, s, v, v * Size, 0, rng);
        return ImageTexture.CreateFromImage(img);
    }

    static Color C(int r, int g, int b) => Color.Color8((byte)r, (byte)g, (byte)b);

    static Color Jitter(Color c, RandomNumberGenerator rng, float amt)
    {
        float d = rng.RandfRange(-amt, amt);
        return new Color(c.R + d, c.G + d, c.B + d);
    }

    static void Paint(Image img, SurfaceType s, int variant, int ox, int oy, RandomNumberGenerator rng, bool autumn = false)
    {
        void P(int x, int y, Color c) => img.SetPixel(ox + (x & 15), oy + (y & 15), c);

        switch (s)
        {
            case SurfaceType.Grass when autumn:
                // Olive-brown, going over, strewn with yellow birch leaves and a few rusty ones.
                for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float r = rng.Randf();
                    Color c = r < 0.16f ? C(70, 72, 36) : r < 0.32f ? C(110, 104, 52) : r < 0.38f ? C(196, 156, 40) : r < 0.41f ? C(160, 82, 30) : C(88, 88, 44);
                    P(x, y, c);
                }
                break;

            case SurfaceType.Grass:
                for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float r = rng.Randf();
                    Color c = r < 0.15f ? C(58, 88, 38) : r < 0.3f ? C(96, 128, 56) : r < 0.33f ? C(150, 140, 80) : C(76, 108, 46);
                    P(x, y, c);
                }
                break;

            case SurfaceType.Concrete:
            case SurfaceType.Pothole:
                for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                    P(x, y, Jitter(C(132, 130, 122), rng, 0.035f));
                for (int i = 0; i < Size; i++) { P(i, 0, C(92, 90, 86)); P(0, i, C(92, 90, 86)); }
                if (s == SurfaceType.Concrete && variant >= 2)
                {
                    // Crack.
                    int cx = rng.RandiRange(3, 12), cy = 1;
                    while (cy < 15) { P(cx, cy, C(70, 68, 64)); cy++; cx += rng.RandiRange(-1, 1); }
                }
                if (s == SurfaceType.Concrete && variant == 1)
                    for (int i = 0; i < 9; i++) P(rng.RandiRange(4, 11), rng.RandiRange(4, 11), C(88, 84, 80)); // oil stain
                if (s == SurfaceType.Pothole)
                {
                    // Matches Pothole.Height: ~0.72 m radius at 8 px/m, dark flat bottom, pale broken rim.
                    for (int y = 0; y < Size; y++)
                    for (int x = 0; x < Size; x++)
                    {
                        float d = new Vector2(x - 7.5f, (y - 7.5f) * 1.15f).Length() + rng.Randf() * 1.0f;
                        if (d < 4f) P(x, y, C(46, 40, 34));
                        else if (d < 6f) P(x, y, C(76, 70, 62));
                        else if (d < 7f && rng.Randf() < 0.5f) P(x, y, C(160, 156, 146));
                    }
                }
                break;

            case SurfaceType.Gravel:
                for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float r = rng.Randf();
                    Color c = r < 0.2f ? C(178, 160, 128) : r < 0.35f ? C(118, 104, 82) : C(152, 134, 104);
                    P(x, y, c);
                }
                break;

            case SurfaceType.Mud:
                for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                    P(x, y, Jitter(C(90, 66, 42), rng, 0.03f));
                if (variant < 2)
                    for (int y = 0; y < Size; y++) { P(4, y, C(66, 48, 30)); P(11, y, C(66, 48, 30)); P(5, y, C(106, 82, 56)); } // ruts
                for (int i = 0; i < 6; i++) P(rng.RandiRange(0, 15), rng.RandiRange(0, 15), C(120, 96, 70));
                break;

            case SurfaceType.Puddle:
            {
                // Water lying in the road around it: variant 0 mud, 1 gravel, 2 and 3 dirt (TrackBuilder
                // picks it from the road), with a thin band of wet, darker ground round the edge.
                SurfaceType ground = variant switch { 1 => SurfaceType.Gravel, 0 => SurfaceType.Mud, _ => SurfaceType.Dirt };
                Paint(img, ground, 2, ox, oy, rng);                 // mud variant 2 has no ruts
                for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float d = new Vector2(x - 7.5f, (y - 7.5f) * 1.4f).Length() + rng.Randf();
                    if (d < 6.5f) P(x, y, C(88, 100, 106));
                    else if (d < 7.6f) P(x, y, img.GetPixel(ox + x, oy + y).Darkened(0.28f));
                }
                P(5, 6, C(170, 180, 184)); P(6, 6, C(170, 180, 184)); P(9, 9, C(140, 150, 156));
                break;
            }

            case SurfaceType.Dirt:
                for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float r = rng.Randf();
                    P(x, y, r < 0.12f ? C(106, 84, 56) : r < 0.2f ? C(150, 124, 90) : C(128, 102, 70));
                }
                break;

            case SurfaceType.Field:
                for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    bool furrow = (x % 4) < 2;
                    Color c = furrow ? C(96, 70, 44) : C(122, 94, 62);
                    P(x, y, Jitter(c, rng, 0.03f));
                }
                if (variant == 3)
                    for (int i = 0; i < 8; i++) P(rng.RandiRange(0, 15), rng.RandiRange(0, 15), C(170, 150, 80)); // straw
                break;

            case SurfaceType.DeadGrass:
                // Withered, grey-brown, bare patches of dust.
                for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float r = rng.Randf();
                    P(x, y, r < 0.18f ? C(96, 88, 70) : r < 0.3f ? C(138, 128, 104) : r < 0.36f ? C(70, 66, 58) : C(116, 108, 86));
                }
                break;

            case SurfaceType.Water:
                // The river's bed under the water (WaterMesh lies over it): dark silt, a few stones, weed.
                for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                    P(x, y, Jitter(C(62, 60, 44), rng, 0.05f));
                for (int i = 0; i < 7; i++) P(rng.RandiRange(0, 15), rng.RandiRange(0, 15), C(96, 94, 82));
                for (int i = 0; i < 5; i++) P(rng.RandiRange(0, 15), rng.RandiRange(0, 15), C(52, 74, 40));
                break;

            case SurfaceType.Shallow:
                // The ford's gravel bar, wet: gravel a shade darker, with silt in the hollows.
                Paint(img, SurfaceType.Gravel, 2, ox, oy, rng);
                for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                    P(x, y, img.GetPixel(ox + x, oy + y).Darkened(0.22f));
                for (int i = 0; i < 6; i++) P(rng.RandiRange(0, 15), rng.RandiRange(0, 15), C(70, 66, 50));
                break;

            case SurfaceType.Ice:
                // Blue-grey glare ice: smooth, white cracks, frozen bubbles, a pale polished streak.
                for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                    P(x, y, Jitter(C(150, 178, 196), rng, 0.02f));
                if (variant != 1)
                {
                    int cx = rng.RandiRange(1, 14), cy = 0;
                    while (cy < 16) { P(cx, cy, C(222, 234, 242)); cy++; cx += rng.RandiRange(-1, 1); }
                }
                if (variant >= 2)
                    for (int x = 0; x < Size; x++) P(x, (x / 3 + variant * 4) & 15, C(186, 208, 222));   // a streak of polish
                for (int i = 0; i < 4; i++) P(rng.RandiRange(0, 15), rng.RandiRange(0, 15), C(118, 146, 166));   // bubbles
                break;

            case SurfaceType.Snow:
                // Packed snow on the road: nearly white, a touch greyer and bluer than the snow off
                // it, compacted flecks, now and then a grain of sand. The wheel tracks are a mesh
                // of their own along the road (SnowTracks), so they follow it whichever way it runs.
                for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float r = rng.Randf();
                    P(x, y, r < 0.14f ? C(214, 219, 228) : r < 0.2f ? C(244, 246, 250) : C(229, 232, 238));
                }
                for (int i = 0; i < 2 + variant; i++) P(rng.RandiRange(0, 15), rng.RandiRange(0, 15), C(168, 158, 142));   // sand
                break;

            case SurfaceType.DeepSnow:
                // Untouched snow: bright, faint blue hollows, the odd twig or grass stalk.
                for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float r = rng.Randf();
                    P(x, y, r < 0.12f ? C(222, 230, 242) : r < 0.16f ? C(255, 255, 255) : C(240, 244, 250));
                }
                if (variant == 3)
                    for (int i = 0; i < 3; i++) P(rng.RandiRange(0, 15), rng.RandiRange(0, 15), C(120, 110, 80));
                break;

            case SurfaceType.Runway:
                // Big smooth slabs, pale; only the odd black streak of tyre rubber from landings.
                for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                    P(x, y, Jitter(C(158, 157, 150), rng, 0.02f));
                if (variant == 0) for (int i = 0; i < Size; i++) P(i, 0, C(128, 127, 122));
                if (variant == 2)
                    for (int x = 2; x < 14; x++) { P(x, 7, C(95, 94, 92)); P(x, 8, C(110, 108, 105)); }
                break;
        }
    }
}
