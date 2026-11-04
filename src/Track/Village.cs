using System;
using System.Collections.Generic;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Track;

/// <summary>
/// The village on ILYICH'S LAMP: the drive-through barn over the start and finish line, the old
/// church (now the kolkhoz grain store) and the log houses (izbas). All boxes and prisms like the
/// rest of the world; local +Z is the barn's way through and the house's face to the street.
/// </summary>
public static partial class Village
{
    static readonly Color Plank = new(0.45f, 0.21f, 0.14f);
    static readonly Color PlankDark = new(0.33f, 0.15f, 0.1f);
    static readonly Color Timber = new(0.3f, 0.21f, 0.13f);
    static readonly Color Tin = new(0.38f, 0.4f, 0.42f);
    static readonly Color Line = new(0.92f, 0.9f, 0.84f);
    static readonly Color Red = new(0.72f, 0.1f, 0.08f);
    static readonly Color Gold = new(1f, 0.85f, 0.3f);
    static readonly Color Lamp = new(1f, 0.85f, 0.5f);

    public const float BarnHeight = 5f;

    /// <summary>
    /// The start/finish barn: two long plank walls either side of the road (inner half-width
    /// half), open at both ends, gables and a roof that see-through fade while a car is under them
    /// (FadeUnder), the line painted inside line metres ahead of the middle, START and FINISH
    /// banners on the far loft and lamps that glow at night.
    /// </summary>
    public static Node3D StartBarn(float half, float length, float line, float yaw = 0, float stripeY = 0.03f, float[] rise = null, float reach = 0f)
    {
        var n = new Node3D();
        float wx = half + 0.25f;
        foreach (float s in new[] { -1f, 1f })
        {
            // A metre deeper than the road: the walls stand in the ditches beside it.
            n.AddChild(Props.Part(Props.Box(0.5f, BarnHeight + 1f, length), Plank, new Vector3(s * wx, (BarnHeight - 1f) / 2f, 0)));
            // Dark battens every 1.2 m and corner posts.
            for (float z = -length / 2f + 0.6f; z < length / 2f; z += 1.2f)
                n.AddChild(Props.Part(Props.Box(0.56f, BarnHeight, 0.1f), PlankDark, new Vector3(s * wx, BarnHeight / 2f, z)));
            foreach (float e in new[] { -1f, 1f })
                n.AddChild(Props.Part(Props.Box(0.7f, BarnHeight + 0.2f, 0.7f), Timber, new Vector3(s * wx, BarnHeight / 2f, e * length / 2f)));
        }
        // Hay along the inside of the walls, clear of the road.
        foreach (float s in new[] { -1f, 1f })
            for (float z = -length / 2f + 2f; z < length / 2f - 1f; z += 4.5f)
                if (MathF.Abs(z - line) > 3f)
                    n.AddChild(Props.Part(Props.Box(1.1f, 0.9f, 1.6f), new Color(0.82f, 0.7f, 0.34f), new Vector3(s * (wx - 0.9f), 0.45f, z)));
        // The line and the banner over it.
        if (rise == null || rise.Length < 2)
            n.AddChild(Props.Part(Props.Box(half * 2f - 1.6f, 0.02f, 0.5f), Line, new Vector3(0, stripeY, line)));
        else
        {
            // Pieces from sample to sample, each tilted to the ground between them: the line lies on the road and in the ditches, however it falls.
            for (int i = 0; i + 1 < rise.Length; i++)
            {
                float x0 = -reach + 2f * reach * i / (rise.Length - 1), x1 = -reach + 2f * reach * (i + 1) / (rise.Length - 1);
                float dx = x1 - x0, dh = rise[i + 1] - rise[i], len = MathF.Sqrt(dx * dx + dh * dh) + 0.02f;
                n.AddChild(Props.Part(Props.Box(len, 0.02f, 0.5f), Line, new Vector3((x0 + x1) / 2f, (rise[i] + rise[i + 1]) / 2f, line),
                    new Vector3(0, 0, Mathf.RadToDeg(MathF.Atan2(dh, dx)))));
            }
        }
        // Tilt the two banner panels toward the fixed elevated camera and hang them
        // between the timber trusses, which would obscure a single long line of text.
        float farSide = MathF.Sin(yaw) >= 0 ? 1f : -1f;
        foreach (var (text, z) in new[] { ("START", length / 8), ("FINISH", -length / 8) })
        {
            var banner = Buildings.TextPanel(text, length / 4 - 0.8f, 1.4f,
                new Vector3(farSide * (half - Math.Min(2.1f, half * 0.3f) - 0.65f), 4.6f, z), Red, Line);
            banner.RotationDegrees = new Vector3(-55, -farSide * 90, 0);
            n.AddChild(banner);
        }
        // Lamps under the roof (they glow after dark).
        foreach (float z in new[] { -length / 4f, length / 4f })
            n.AddChild(NightGlow.Part(Props.Ball(0.25f), Lamp, Lamp, 1.6f, new Vector3(0, BarnHeight - 0.4f, z)));

        // Roof and gables: their own materials, faded while a car is under them.
        var roof = new FadeUnder { Name = "BarnRoof" };
        float span = wx * 2f + 1.6f;
        roof.AddChild(roof.Part(new PrismMesh { Size = new Vector3(span, 3.4f, length + 1f) }, new Color(0.3f, 0.3f, 0.32f), new Vector3(0, BarnHeight + 1.7f, 0)));
        foreach (float e in new[] { -1f, 1f })
        {
            roof.AddChild(roof.Part(new PrismMesh { Size = new Vector3(wx * 2f + 0.5f, 3.4f, 0.3f) }, Plank, new Vector3(0, BarnHeight + 1.7f, e * (length / 2f))));
            roof.AddChild(roof.Part(Props.Box(wx * 2f + 0.5f, 0.6f, 0.4f), Timber, new Vector3(0, BarnHeight - 0.3f, e * (length / 2f))));
        }
        AddBarnDetails(n, roof, half, length, line);
        n.AddChild(roof);
        return n;
    }

    /// <summary>Car obstacles of the barn: its two walls (centre c, yaw along the road, inner half-width half).</summary>
    public static IEnumerable<Obstacle> BarnWalls(Vector2 c, float yaw, float half, float length)
    {
        Vector2 along = new(MathF.Sin(yaw), MathF.Cos(yaw)), across = new(along.Y, -along.X);
        const float R = 0.35f;
        foreach (float s in new[] { -1f, 1f })
            for (float z = -length / 2f; z <= length / 2f + 0.01f; z += R * 1.4f)
                yield return new Obstacle(c + across * s * (half + 0.25f) + along * z, R);
    }

    /// <summary>The old church: a whitewashed nave, an apse, the bell tower with a green onion dome
    /// (the cross long gone), and over the door the kolkhoz's word for it: grain store.</summary>
    public static Node3D Church()
    {
        var n = new Node3D();
        var white = new Color(0.86f, 0.84f, 0.77f);
        var shade = new Color(0.7f, 0.68f, 0.62f);
        var green = new Color(0.22f, 0.45f, 0.3f);
        // Nave along local Z (12 m), apse at -Z, tower at +Z (the door side).
        n.AddChild(Props.Part(Props.Box(7f, 6f, 10f), white, new Vector3(0, 3f, -1f)));
        n.AddChild(Props.Part(new PrismMesh { Size = new Vector3(7.6f, 2.6f, 10.4f) }, green, new Vector3(0, 7.3f, -1f)));
        n.AddChild(Props.Part(Props.Cyl(2.6f, 4.5f, 8), white, new Vector3(0, 2.25f, -6.2f)));
        n.AddChild(Props.Part(Props.Cyl(2.8f, 1.4f, 8, 0.4f), green, new Vector3(0, 5.2f, -6.2f)));
        // Central drum and dome over the nave.
        n.AddChild(Props.Part(Props.Cyl(1.5f, 2.2f, 8), white, new Vector3(0, 9.3f, -1f)));

        // Bell tower.
        n.AddChild(Props.Part(Props.Box(4f, 11f, 4f), white, new Vector3(0, 5.5f, 6f)));
        n.AddChild(Props.Part(Props.Box(4.2f, 0.4f, 4.2f), shade, new Vector3(0, 8.2f, 6f)));
        n.AddChild(Props.Part(Props.Cyl(1.1f, 1.2f, 8), white, new Vector3(0, 11.6f, 6f)));
        // Door and the grain-store sign.
        n.AddChild(Props.Part(Props.Box(1.8f, 2.6f, 0.1f), new Color(0.24f, 0.17f, 0.12f), new Vector3(0, 1.3f, 8.04f)));
        n.AddChild(Buildings.TextPanel("ЗЕРНОСКЛАД", 3.8f, 0.7f, new Vector3(0, 3.3f, 8.06f), Red, Gold));
        // The elevated race camera foreshortens a wall sign and the cornice hides its top.
        // Mount a larger plate on the camera-facing roof slope, just above its raised seams.
        var storeSign = Buildings.TextPanel("GRAIN STORE", 6.3f, 1.15f, new Vector3(2f, 7.4f, -1f), Red, Line);
        // A one-texel I stem can disappear when the roof plate is sampled at game resolution.
        // Give this sign a broad I with full-width serifs, keeping the shared UI font intact.
        var signImage = PixelFont.RenderImage("GRAIN STORE", Line, Red);
        int letterX = 2 + 3 * PixelFont.Advance;
        for (int y = 0; y < PixelFont.GlyphH; y++)
            for (int x = 0; x < PixelFont.GlyphW; x++)
                signImage.SetPixel(letterX + x, 2 + y,
                    y == 0 || y == PixelFont.GlyphH - 1 || x >= 1 && x <= 2 ? Line : Red);
        ((StandardMaterial3D)storeSign.MaterialOverride).AlbedoTexture = ImageTexture.CreateFromImage(signImage);
        storeSign.RotationDegrees = new Vector3(-Mathf.RadToDeg(MathF.Atan2(3.8f, 2.6f)), 90, 0);
        n.AddChild(storeSign);
        n.AddChild(ChurchDetails());
        return n;
    }

    /// <summary>A pair of capped dry-stone gateposts marking the grain loaders' opening.</summary>
    public static Node3D StoneGatePost()
    {
        var n = new Node3D { Name = "Churchyard gatepost" };
        var stone = new Color(0.54f, 0.53f, 0.48f);
        var cap = new Color(0.66f, 0.64f, 0.58f);
        n.AddChild(Props.Part(Props.Box(0.65f, 0.28f, 0.62f), stone, new Vector3(0, 0.14f, 0)));
        n.AddChild(Props.Part(Props.Box(0.55f, 0.28f, 0.52f), stone.Lightened(0.08f), new Vector3(0.02f, 0.42f, -0.02f)));
        n.AddChild(Props.Part(Props.Box(0.78f, 0.16f, 0.72f), cap, new Vector3(0, 0.64f, 0)));
        return n;
    }

    /// <summary>Worn sandy yard aprons and footpaths for the Ilyich village, plus a small
    /// grain-store loading area. Ground geometry follows the rendered terrain and has no collision.</summary>
    public static Node3D Yards(TrackData t)
    {
        var root = new Node3D { Name = "VillageYards" };
        if (t.Id != "ilyich") return root;

        var ground = new SurfaceTool();
        ground.Begin(Mesh.PrimitiveType.Triangles);
        var rng = new RandomNumberGenerator { Seed = 192210u };
        Color Dirt(float r, float g, float b) => new(ToLinear(r), ToLinear(g), ToLinear(b));
        float ToLinear(float c) => c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
        // The values describe the intended sRGB appearance. Vertex colors are linear, so convert
        // them here; raw .4-.5 values otherwise turn into pale concrete under the daylight rig.
        Color[] earth = { Dirt(0.43f, 0.33f, 0.22f), Dirt(0.46f, 0.36f, 0.24f), Dirt(0.4f, 0.31f, 0.21f), Dirt(0.49f, 0.38f, 0.25f) };
        Color[] gravel = { Dirt(0.41f, 0.39f, 0.33f), Dirt(0.46f, 0.43f, 0.36f), Dirt(0.38f, 0.37f, 0.32f), Dirt(0.49f, 0.46f, 0.38f) };

        void Triangle(Vector3 a, Vector3 b, Vector3 c, Color color)
        {
            ground.SetColor(color); ground.SetNormal(Vector3.Up); ground.AddVertex(a);
            ground.SetColor(color); ground.SetNormal(Vector3.Up); ground.AddVertex(b);
            ground.SetColor(color); ground.SetNormal(Vector3.Up); ground.AddVertex(c);
        }
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
        {
            Triangle(a, c, b, color);
            Triangle(a, d, c, color);
        }
        Color Tone(Color c, float factor) => new(c.R * factor, c.G * factor, c.B * factor, c.A);
        void MottledQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
        {
            Color ca = Tone(color, rng.RandfRange(0.91f, 1.09f));
            Color cb = Tone(color, rng.RandfRange(0.91f, 1.09f));
            Color cc = Tone(color, rng.RandfRange(0.91f, 1.09f));
            Color cd = Tone(color, rng.RandfRange(0.91f, 1.09f));
            ground.SetColor(ca); ground.SetNormal(Vector3.Up); ground.AddVertex(a);
            ground.SetColor(cc); ground.SetNormal(Vector3.Up); ground.AddVertex(c);
            ground.SetColor(cb); ground.SetNormal(Vector3.Up); ground.AddVertex(b);
            ground.SetColor(ca); ground.SetNormal(Vector3.Up); ground.AddVertex(a);
            ground.SetColor(cd); ground.SetNormal(Vector3.Up); ground.AddVertex(d);
            ground.SetColor(cc); ground.SetNormal(Vector3.Up); ground.AddVertex(c);
        }
        Vector3 Ground(Vector2 p, float lift = 0.025f) => new(p.X, t.MeshHeight(p.X, p.Y) + lift, p.Y);
        Vector2 World(PropInstance p, Vector2 local)
        {
            Vector3 offset = new Basis(Vector3.Up, p.Rotation) * new Vector3(local.X, 0, local.Y);
            return new Vector2(p.Position.X + offset.X, p.Position.Z + offset.Z);
        }

        // Oval, trampled dirt immediately beyond the house fronts. The surrounding grass is
        // left broad; narrow two-track paths carry the wear on toward the nearest road edge.
        void Apron(PropInstance house)
        {
            const float rx = 5.8f, rz = 3.25f;
            const float step = 0.8f;
            int houseX = (int)(house.Position.X * 10), houseZ = (int)(house.Position.Z * 10);
            int iz = 0;
            for (float z = -rz; z < rz; z += step, iz++)
            {
                int ix = 0;
                for (float x = -rx; x < rx; x += step, ix++)
                {
                    float cx = x + step * 0.5f, cz = z + step * 0.5f;
                    float edge = 0.94f + Noise.Hash01(houseX + ix, houseZ + iz, 82) * 0.2f;
                    if (cx * cx / (rx * rx) + cz * cz / (rz * rz) > edge) continue;
                    Vector2 Corner(int gx, int gz, float px, float pz)
                    {
                        float jx = (Noise.Hash01(houseX + gx, houseZ + gz, 79) - 0.5f) * 0.26f;
                        float jz = (Noise.Hash01(houseX + gx, houseZ + gz, 80) - 0.5f) * 0.26f;
                        return World(house, new Vector2(px + jx, 3.0f + pz + jz));
                    }
                    var a = Corner(ix, iz, x, z);
                    var b = Corner(ix + 1, iz, x + step, z);
                    var c = Corner(ix + 1, iz + 1, x + step, z + step);
                    var d = Corner(ix, iz + 1, x, z + step);
                    var col = earth[rng.RandiRange(0, earth.Length - 1)];
                    MottledQuad(Ground(a), Ground(b), Ground(c), Ground(d), col);
                }
            }

            Strip(HouseAccessPath(t, house), 1.55f, earth);
        }

        void Strip(IReadOnlyList<Vector2> pts, float width, Color[] palette)
        {
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                Vector2 d = pts[i + 1] - pts[i];
                if (d.LengthSquared() < 0.01f) continue;
                int slices = Math.Max(1, (int)MathF.Ceiling(d.Length() / 0.55f));
                Vector2 normal = new(-d.Y, d.X);
                normal /= d.Length();
                float previousWidth = i == 0 ? width * 0.64f : width * rng.RandfRange(0.72f, 1.24f);
                Vector2 previousCenter = pts[i];
                for (int s = 1; s <= slices; s++)
                {
                    float f = s / (float)slices;
                    float taper = i + 2 == pts.Count && s == slices ? 0.64f : 1f;
                    float nextWidth = width * taper * rng.RandfRange(0.72f, 1.24f);
                    float nextOffset = rng.RandfRange(-0.2f, 0.2f);
                    Vector2 center = pts[i].Lerp(pts[i + 1], f) + normal * nextOffset;
                    Vector2 a = previousCenter - normal * previousWidth * 0.5f;
                    Vector2 b = previousCenter + normal * previousWidth * 0.5f;
                    Vector2 c = center + normal * nextWidth * 0.5f;
                    Vector2 e = center - normal * nextWidth * 0.5f;
                    Color col = palette[rng.RandiRange(0, palette.Length - 1)];
                    MottledQuad(Ground(a), Ground(b), Ground(c), Ground(e), col);
                    previousCenter = center;
                    previousWidth = nextWidth;
                }
            }
        }

        foreach (var house in t.Props)
            if (house.Type == "izba") Apron(house);

        // Gate-to-door wheel track through the churchyard, aligned with the existing NW gateposts.
        var church = t.Props.Find(p => p.Type == "church");
        if (church != null)
        {
            var access = new List<Vector2> { new(46.2f, 183.8f), new(47.1f, 185.4f), new(48.1f, 187.2f), new(49f, 189.2f), new(49f, 191.2f) };
            Strip(access, 2.0f, gravel);
            // A modest compacted loading patch; its broken outline keeps it from reading as paving.
            for (int i = 0; i < 100; i++)
            {
                float a = rng.RandfRange(0, MathF.Tau), r = MathF.Sqrt(rng.Randf());
                Vector2 center = new(48.4f + MathF.Cos(a) * 3.3f * r, 188.4f + MathF.Sin(a) * 2.2f * r);
                float size = rng.RandfRange(0.22f, 0.72f);
                Vector2 u = new Vector2(MathF.Cos(a), MathF.Sin(a)) * size, v = new Vector2(-u.Y, u.X) * size * 0.7f;
                Quad(Ground(center - u - v), Ground(center + u - v), Ground(center + u + v), Ground(center - u + v), gravel[rng.RandiRange(0, gravel.Length - 1)]);
            }


        }

        ground.Index();
        root.AddChild(new MeshInstance3D { Name = "WornVillageGround", Mesh = ground.Commit(),
            MaterialOverride = new StandardMaterial3D { VertexColorUseAsAlbedo = true, ShadingMode = BaseMaterial3D.ShadingModeEnum.PerVertex, Roughness = 1f, SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled, CullMode = BaseMaterial3D.CullModeEnum.Disabled },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        return root;
    }

    // Broad architectural details read from the race camera. Bake them into one vertex-coloured
    // surface, including the domes, so decoration does not create hundreds of draw calls.
    static MeshInstance3D ChurchDetails()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var stone = new Color(0.92f, 0.89f, 0.79f);
        var baseStone = new Color(0.48f, 0.47f, 0.41f);
        var dark = new Color(0.12f, 0.14f, 0.14f);
        var green = new Color(0.22f, 0.45f, 0.3f);
        var seam = new Color(0.16f, 0.31f, 0.22f);
        var brick = new Color(0.52f, 0.29f, 0.19f);
        var board = new Color(0.45f, 0.32f, 0.2f);

        void Part(PrimitiveMesh mesh, Color color, Vector3 at, Vector3 rotation = default)
        {
            var xf = new Transform3D(Basis.FromEuler(rotation), at);
            var arrays = DrunkTractor.PrimitiveArrays(mesh);
            var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var normals = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
            foreach (int i in arrays[(int)Mesh.ArrayType.Index].AsInt32Array())
            {
                st.SetColor(color);
                st.SetNormal(xf.Basis * normals[i]);
                st.AddVertex(xf * vertices[i]);
            }
        }
        void Box(Vector3 size, Color color, Vector3 at) => Part(Props.Box(size.X, size.Y, size.Z), color, at);

        // Arched recess with chunky voussoirs; local +Z faces out of the wall.
        void Arch(Vector3 at, float yaw, float radius, float stem, bool boarded, bool closedUp = false)
        {
            var basis = new Basis(Vector3.Up, yaw);
            void Piece(PrimitiveMesh mesh, Color color, Vector3 offset, Vector3 rot = default)
            {
                // The only local rotations are about X (opening disc) or Z (arch stones).
                var local = Basis.FromEuler(rot);
                Part(mesh, color, at + basis * offset, (basis * local).GetEuler());
            }
            Piece(Props.Box(radius * 2, stem, 0.08f), dark, new Vector3(0, stem / 2, 0));
            Piece(Props.Cyl(radius, 0.08f, 12), dark, new Vector3(0, stem, 0.005f), new Vector3(MathF.PI / 2, 0, 0));
            foreach (float side in new[] { -1f, 1f })
                Piece(Props.Box(0.22f, stem, 0.2f), stone, new Vector3(side * (radius + 0.11f), stem / 2, 0.04f));
            for (int i = 0; i < 7; i++)
            {
                float a = (i + 0.5f) * MathF.PI / 7;
                Piece(Props.Box(0.22f, (radius + 0.11f) * MathF.PI / 7 + 0.035f, 0.2f), stone,
                    new Vector3((radius + 0.11f) * MathF.Cos(a), stem + (radius + 0.11f) * MathF.Sin(a), 0.04f), new Vector3(0, 0, a));
            }
            Piece(Props.Box(radius * 2 + 0.65f, 0.22f, 0.36f), baseStone, new Vector3(0, -0.1f, 0.08f));
            if (closedUp)
            {
                // Rough planks nailed across the whole opening, including its arched head.
                float width = (radius * 2 + 0.12f) / 4;
                for (int i = 0; i < 4; i++)
                {
                    float height = stem + radius + 0.12f + (i % 2) * 0.06f;
                    Piece(Props.Box(width - 0.012f, height, 0.14f), i % 2 == 0 ? board : board.Darkened(0.15f),
                        new Vector3((i - 1.5f) * width, height / 2 - 0.04f, 0.15f));
                }
                Piece(Props.Box(radius * 2 + 0.18f, 0.16f, 0.1f), board.Darkened(0.25f),
                    new Vector3(0, stem * 0.6f, 0.27f), new Vector3(0, 0, -0.18f));
            }
            else if (boarded)
                for (int i = 0; i < 3; i++)
                    Piece(Props.Box(radius * 2, 0.23f, 0.12f), board, new Vector3(0, 0.22f + i * 0.36f, 0.09f), new Vector3(0, 0, i == 1 ? 0.12f : -0.06f));
            else
                Piece(Props.Box(0.1f, stem + radius * 0.75f, 0.1f), baseStone, new Vector3(0, (stem + radius * 0.75f) / 2, 0.08f));
        }

        // Plinth and cornices break up the large plain walls.
        Box(new Vector3(7.18f, 0.65f, 10.16f), baseStone, new Vector3(0, 0.325f, -1));
        Box(new Vector3(7.35f, 0.24f, 10.25f), stone, new Vector3(0, 5.65f, -1));
        Box(new Vector3(7.55f, 0.18f, 10.35f), baseStone, new Vector3(0, 5.94f, -1));
        foreach (float side in new[] { -1f, 1f })
        {
            for (int i = 0; i < 4; i++)
            {
                float z = -5.6f + i * 3f;
                Box(new Vector3(0.2f, 4.85f, 0.38f), stone, new Vector3(side * 3.55f, 3.05f, z));
                Box(new Vector3(0.3f, 0.25f, 0.65f), stone, new Vector3(side * 3.57f, 5.35f, z));
            }
            for (int w = 0; w < 3; w++)
                Arch(new Vector3(side * 3.54f, 2.8f, -4f + w * 3f), side * MathF.PI / 2, 0.48f, 1.1f, w == 1);
            // Exposed masonry in broad, irregular patches near the damp base.
            for (int row = 0; row < 3; row++)
                for (int b = 0; b < 3 - row; b++)
                    Box(new Vector3(0.06f, 0.22f, 0.5f), brick,
                        new Vector3(side * 3.525f, 0.85f + row * 0.27f, -2.7f + b * 0.56f + row * 0.24f));
        }
        // Boarded windows follow the four exposed facets of the octagonal apse.
        // Place them on face centres (the apothem), rather than at the cylinder's corners.
        float apseFace = 2.6f * MathF.Cos(MathF.PI / 8) + 0.04f;
        for (int i = 0; i < 4; i++)
        {
            float yaw = Mathf.DegToRad(112.5f + i * 45f);
            Arch(new Vector3(apseFace * MathF.Sin(yaw), 2.1f, -6.2f + apseFace * MathF.Cos(yaw)),
                yaw, 0.43f, 0.95f, true, closedUp: true);
        }

        // Roof ridge and raised standing seams, visible from the elevated camera.
        Box(new Vector3(0.2f, 0.18f, 10.5f), seam, new Vector3(0, 8.62f, -1));
        float slope = MathF.Atan2(2.6f, 3.8f);
        foreach (float side in new[] { -1f, 1f })
            for (int i = 0; i < 6; i++)
                Part(Props.Box(4.62f, 0.07f, 0.09f), seam, new Vector3(side * 1.9f, 7.34f, -5.6f + i * 1.8f), new Vector3(0, 0, -side * slope));

        Box(new Vector3(4.16f, 0.65f, 4.16f), baseStone, new Vector3(0, 0.325f, 6));
        foreach (float y in new[] { 4.1f, 7.9f, 10.95f })
            Box(new Vector3(4.35f, 0.25f, 4.35f), stone, new Vector3(0, y, 6));
        foreach (float x in new[] { -1.9f, 1.9f })
            foreach (float z in new[] { 4.1f, 7.9f })
                Box(new Vector3(0.32f, 10.1f, 0.32f), stone, new Vector3(x, 5.6f, z));
        for (int i = 0; i < 4; i++)
        {
            float yaw = i * MathF.PI / 2;
            Arch(new Vector3(2.04f * MathF.Sin(yaw), 8.6f, 6 + 2.04f * MathF.Cos(yaw)), yaw, 0.62f, 1.05f, false);
        }
        // Heavy timber door with iron straps beneath the existing grain-store sign.
        Box(new Vector3(0.08f, 2.6f, 0.07f), dark, new Vector3(0, 1.3f, 8.12f));
        foreach (float y in new[] { 0.6f, 2.0f })
            Box(new Vector3(1.65f, 0.14f, 0.08f), baseStone, new Vector3(0, y, 8.13f));
        foreach (float x in new[] { -1.05f, 1.05f })
            Box(new Vector3(0.3f, 2.85f, 0.25f), stone, new Vector3(x, 1.425f, 8.08f));
        Box(new Vector3(2.4f, 0.25f, 0.3f), stone, new Vector3(0, 2.85f, 8.08f));

        // A conspicuous loading stack on the camera-facing side of the tower. Keep the new
        // storage props inside the existing 7 x 17 m church obstacle footprint.
        var sack = new Color(0.76f, 0.63f, 0.39f);
        var grain = new Color(0.88f, 0.69f, 0.27f);
        for (int i = 0; i < 6; i++)
            Box(new Vector3(1.45f, 0.14f, 0.4f), board, new Vector3(2.73f, 0.25f, 5.3f + i * 0.46f));
        foreach (float x in new[] { 2.2f, 3.25f })
            Box(new Vector3(0.16f, 0.2f, 2.8f), dark, new Vector3(x, 0.1f, 6.45f));
        for (int layer = 0; layer < 3; layer++)
            for (int row = 0; row < 3; row++)
                for (int col = 0; col < (layer == 2 ? 1 : 2); col++)
                {
                    var at = new Vector3(2.38f + col * 0.7f + (layer == 2 ? 0.25f : 0), 0.57f + layer * 0.47f, 5.55f + row * 0.84f);
                    Part(new SphereMesh { Radius = 0.37f, Height = 0.52f, RadialSegments = 8, Rings = 4 }, sack, at);
                    Part(Props.Cyl(0.09f, 0.12f, 6, 0.055f), board, at + new Vector3(0.1f, 0.26f, 0));
                }
        Part(Props.Cyl(0.52f, 0.18f, 10, 0.12f), grain, new Vector3(2.7f, 0.1f, 7.85f));
        for (int i = 0; i < 7; i++)
            Part(Props.Cyl(0.09f + (i % 3) * 0.025f, 0.025f, 5), grain,
                new Vector3(2.1f + (i % 3) * 0.45f, 0.025f, 7.9f + (i / 3) * 0.2f));

        // Faceted onion profiles: narrow neck, broad shoulder, then a drawn-out point.
        void Dome(float z, float bottom, float radius, float height, Color color)
        {
            float[] h = { 0, 0.12f, 0.36f, 0.58f, 0.78f, 1 };
            float[] r = { 0.52f, 0.85f, 1, 0.8f, 0.35f, 0.015f };
            for (int i = 0; i < h.Length - 1; i++)
                Part(Props.Cyl(radius * r[i], height * (h[i + 1] - h[i]), 12, radius * r[i + 1]), color,
                    new Vector3(0, bottom + height * (h[i] + h[i + 1]) / 2, z));
        }
        foreach (var (z, y, radius) in new[] { (-1f, 10.35f, 1.55f), (6f, 12.15f, 1.15f) })
            Part(Props.Cyl(radius, 0.22f, 12), stone, new Vector3(0, y, z));
        Dome(-1, 10.45f, 1.7f, 3.1f, green);
        Dome(6, 12.25f, 1.3f, 2.9f, new Color(0.45f, 0.28f, 0.16f));

        st.Index();
        var mesh = st.Commit();
        var material = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.PerVertex,
            Roughness = 1f,
            SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
        };
        return new MeshInstance3D { Name = "ChurchDetails", Mesh = mesh, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
    }

    /// <summary>A log house: dark logs, three windows in blue carved frames on the street side (+Z),
    /// a steep tin roof in the house's colour, a chimney; window lights at night.</summary>
    public static Node3D Izba(int seed)
    {
        var n = new Node3D();
        var rng = new RandomNumberGenerator { Seed = (ulong)(seed * 7919 + 13) };
        Color[] roofs = { new(0.25f, 0.42f, 0.3f), new(0.55f, 0.18f, 0.12f), Tin, new(0.24f, 0.33f, 0.5f) };
        Color roof = roofs[rng.RandiRange(0, roofs.Length - 1)];
        // Kolkhoz village houses share a log-built form, but differ in stain and
        // painted joinery: natural timber, faded ochre, sage and old red-brown.
        Color[][] stains =
        {
            new[] { new Color(0.48f, 0.3f, 0.17f), new Color(0.37f, 0.22f, 0.13f) },
            new[] { new Color(0.58f, 0.43f, 0.22f), new Color(0.46f, 0.32f, 0.17f) },
            new[] { new Color(0.37f, 0.43f, 0.31f), new Color(0.27f, 0.34f, 0.25f) },
            new[] { new Color(0.52f, 0.29f, 0.22f), new Color(0.39f, 0.21f, 0.17f) },
            new[] { new Color(0.53f, 0.49f, 0.38f), new Color(0.4f, 0.38f, 0.31f) },
        };
        var stain = stains[rng.RandiRange(0, stains.Length - 1)];
        Color logA = stain[0], logB = stain[1];
        Color[] trims = { new(0.87f, 0.78f, 0.55f), new(0.63f, 0.72f, 0.55f), new(0.77f, 0.65f, 0.42f), new(0.72f, 0.78f, 0.76f) };
        Color trim = trims[rng.RandiRange(0, trims.Length - 1)];
        Color fence = stain[1] * 0.8f + trim * 0.2f;
        // Batch the repeated trim, log ends and fence into one vertex-coloured mesh per house.
        // The windows that glow at night stay separate so their light material keeps working.
        var detailTool = new SurfaceTool();
        detailTool.Begin(Mesh.PrimitiveType.Triangles);
        void Detail(PrimitiveMesh mesh, Color color, Vector3 at, Vector3 rotationDegrees = default)
        {
            var radians = rotationDegrees * (Mathf.Pi / 180f);
            var xf = new Transform3D(Basis.FromEuler(radians), at);
            var arrays = DrunkTractor.PrimitiveArrays(mesh);
            var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var normals = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
            foreach (int index in arrays[(int)Mesh.ArrayType.Index].AsInt32Array())
            {
                detailTool.SetColor(color);
                detailTool.SetNormal(xf.Basis * normals[index]);
                detailTool.AddVertex(xf * vertices[index]);
            }
        }
        void DetailBox(float x, float y, float z, Color color, Vector3 at, Vector3 rotationDegrees = default) =>
            Detail(Props.Box(x, y, z), color, at, rotationDegrees);
        const float W = 7f, D = 6f, H = 3f;
        // Alternating log courses, with projecting round log ends at the corners.
        for (int k = 0; k < 6; k++)
        {
            float y = H / 12f + k * H / 6f;
            n.AddChild(Props.Part(Props.Box(W, H / 6f, D), k % 2 == 0 ? logA : logB, new Vector3(0, H / 12f + k * H / 6f, 0)));
            foreach (float z in new[] { -D / 2f + 0.08f, D / 2f - 0.08f })
            foreach (float side in new[] { -1f, 1f })
                Detail(Props.Cyl(0.16f, 0.72f, 7), k % 2 == 0 ? logB : logA,
                    new Vector3(side * (W / 2f + 0.03f), y, z), new Vector3(0, 0, 90));
        }
        // Roof edge boards and a raised ridge cap break up the broad roof silhouette.
        n.AddChild(Props.Part(new PrismMesh { Size = new Vector3(D + 1f, 2.6f, W + 0.8f) }, roof, new Vector3(0, H + 1.3f, 0), new Vector3(0, 90, 0)));
        foreach (float z in new[] { -D / 2f - 0.34f, D / 2f + 0.34f })
            DetailBox(W + 0.5f, 0.17f, 0.14f, trim, new Vector3(0, H + 0.05f, z));
        // The ridge runs along the house width (+/-X); keep its cap aligned with the roof peak.
        DetailBox(W + 0.5f, 0.16f, 0.2f, trim, new Vector3(0, H + 2.55f, 0));
        // Brick chimney with a projecting cap and dark flue opening.
        var brick = new Color(0.42f, 0.22f, 0.16f);
        DetailBox(0.58f, 1.45f, 0.58f, brick, new Vector3(1.6f, H + 2.0f, -0.8f));
        DetailBox(0.72f, 0.14f, 0.72f, trim, new Vector3(1.6f, H + 2.77f, -0.8f));
        DetailBox(0.32f, 0.04f, 0.32f, new Color(0.12f, 0.12f, 0.12f), new Vector3(1.6f, H + 2.86f, -0.8f));
        Color frame = trim;
        var glow = new Color(1f, 0.78f, 0.38f);
        for (int w = 0; w < 3; w++)
        {
            float x = -2.2f + w * 2.2f;
            n.AddChild(Props.Part(Props.Box(1.3f, 1.4f, 0.08f), frame, new Vector3(x, 1.6f, D / 2f + 0.03f)));
            var pane = Props.Box(0.9f, 1f, 0.06f);
            var at = new Vector3(x, 1.6f, D / 2f + 0.08f);
            n.AddChild(rng.Randf() < 0.5f ? NightGlow.Part(pane, new Color(0.15f, 0.17f, 0.2f), glow, 0.9f, at)
                : Props.Part(pane, new Color(0.15f, 0.17f, 0.2f), at));
            // Cross muntins, deep sill and carved lintel make the windows readable at distance.
            DetailBox(0.075f, 0.94f, 0.08f, frame * 0.78f, new Vector3(x, 1.6f, D / 2f + 0.13f));
            DetailBox(0.84f, 0.07f, 0.08f, frame * 0.78f, new Vector3(x, 1.6f, D / 2f + 0.13f));
            DetailBox(1.48f, 0.16f, 0.18f, trim, new Vector3(x, 0.83f, D / 2f + 0.08f));
            DetailBox(1.48f, 0.17f, 0.14f, frame, new Vector3(x, 2.38f, D / 2f + 0.08f));
            // Paired shutters sit beside each pane; one carries a simple diagonal brace.
            foreach (float side in new[] { -1f, 1f })
            {
                float shutterX = x + side * 0.78f;
                DetailBox(0.22f, 0.82f, 0.1f, fence, new Vector3(shutterX, 1.6f, D / 2f + 0.09f));
                DetailBox(0.27f, 0.055f, 0.11f, trim, new Vector3(shutterX, 1.34f, D / 2f + 0.15f), new Vector3(0, 0, side * 35f));
            }
        }
        // Side entry door, two worn steps and a small porch hood.
        DetailBox(0.12f, 2.15f, 1.2f, brick, new Vector3(W / 2f + 0.05f, 1.12f, 0.5f));
        DetailBox(0.14f, 2.3f, 1.34f, trim, new Vector3(W / 2f + 0.12f, 1.18f, 0.5f));
        DetailBox(0.05f, 1.98f, 1.02f, brick, new Vector3(W / 2f + 0.21f, 1.15f, 0.5f));
        Detail(Props.Ball(0.07f), Gold, new Vector3(W / 2f + 0.28f, 1.08f, 0.9f));
        DetailBox(1.5f, 0.18f, 1.75f, roof, new Vector3(W / 2f + 0.95f, 2.38f, 0.5f));
        DetailBox(1.2f, 0.12f, 1.35f, Timber, new Vector3(W / 2f + 0.75f, 0.12f, 0.5f));
        DetailBox(0.95f, 0.12f, 1.1f, Timber, new Vector3(W / 2f + 0.56f, 0.28f, 0.5f));
        // A low, individually stained picket fence along the street side.
        for (float x = -W / 2f; x <= W / 2f + 0.01f; x += 0.5f)
            if (MathF.Abs(x) > 1.1f)
                DetailBox(0.08f, 0.9f, 0.08f, fence, new Vector3(x, 0.45f, D / 2f + 2f));
        DetailBox(2.4f, 0.08f, 0.06f, fence, new Vector3(-2.3f, 0.7f, D / 2f + 2f));
        DetailBox(2.4f, 0.08f, 0.06f, fence, new Vector3(2.3f, 0.7f, D / 2f + 2f));
        detailTool.Index();
        var detailMesh = detailTool.Commit();
        var detailMaterial = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.PerVertex,
            Roughness = 1f,
            SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
        };
        n.AddChild(new MeshInstance3D { Name = "HouseDetails", Mesh = detailMesh, MaterialOverride = detailMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        return n;
    }

    /// <summary>A visible patch of ripening grain, generated as one static mesh for a low draw-call count.</summary>
    public static Node3D WheatField(float width, float depth, int seed, Vector2 centre = default, float yaw = 0, List<KolkhozWorkerSpec> workers = null, TrackData ground = null, float originHeight = 0)
    {
        var root = new Node3D { Name = "WheatField" };
        float stalkBase = 0;
        var rng = new RandomNumberGenerator { Seed = (ulong)(seed * 3571 + 1922) };
        var meshTool = new SurfaceTool();
        meshTool.Begin(Mesh.PrimitiveType.Triangles);
        var green = new Color(0.38f, 0.47f, 0.16f);
        var greenLight = new Color(0.55f, 0.57f, 0.19f);
        var grain = new Color(0.78f, 0.63f, 0.23f);
        var grainLight = new Color(0.93f, 0.77f, 0.34f);

        void Triangle(Vector3 a, Vector3 b, Vector3 c, Color color)
        {
            a.Y += stalkBase; b.Y += stalkBase; c.Y += stalkBase;
            var normal = (b - a).Cross(c - a).Normalized();
            meshTool.SetColor(color); meshTool.SetNormal(normal); meshTool.AddVertex(a);
            meshTool.SetColor(color); meshTool.SetNormal(normal); meshTool.AddVertex(b);
            meshTool.SetColor(color); meshTool.SetNormal(normal); meshTool.AddVertex(c);
        }
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
        {
            Triangle(a, b, c, color);
            Triangle(a, c, d, color);
        }

        // Dense planted rows, with a small amount of deterministic irregularity so
        // the crop reads as a field rather than a rigid grid from the race camera.
        int row = 0;
        for (float z = -depth / 2f + 0.35f; z < depth / 2f; z += 0.78f, row++)
        {
            float offset = row % 2 == 0 ? 0f : 0.36f;
            for (float x = -width / 2f + 0.35f + offset; x < width / 2f; x += 0.72f)
            {
                float px = x + rng.RandfRange(-0.12f, 0.12f);
                float pz = z + rng.RandfRange(-0.1f, 0.1f);
                Vector3 rotated = new Basis(Vector3.Up, yaw) * new Vector3(px, 0, pz);
                Vector2 world = centre + new Vector2(rotated.X, rotated.Z);
                if (workers != null && workers.Exists(w => w.Position.DistanceSquaredTo(world) < 1.4f * 1.4f)) continue;
                // A stalk stays upright, but its entire mesh starts at the actual terrain triangle.
                stalkBase = ground == null ? 0 : ground.MeshHeight(world.X, world.Y) - originHeight;
                float h = rng.RandfRange(1.0f, 1.5f);
                float leanX = rng.RandfRange(-0.12f, 0.12f);
                float leanZ = rng.RandfRange(-0.1f, 0.1f);
                float stemW = rng.RandfRange(0.035f, 0.06f);
                Color stem = rng.Randf() < 0.5f ? green : greenLight;

                // Two crossed, tapered stem ribbons remain visible from either side.
                Quad(new Vector3(px - stemW, 0, pz), new Vector3(px + stemW, 0, pz),
                    new Vector3(px + leanX, h - 0.12f, pz), new Vector3(px + leanX - stemW * 0.45f, h - 0.12f, pz), stem);
                Quad(new Vector3(px, 0, pz - stemW), new Vector3(px, 0, pz + stemW),
                    new Vector3(px + leanX, h - 0.12f, pz + leanZ + stemW * 0.45f),
                    new Vector3(px + leanX, h - 0.12f, pz + leanZ - stemW * 0.45f), stem);

                // A pair of narrow leaves, one rising and one drooping from each stalk.
                Triangle(new Vector3(px, h * 0.3f, pz), new Vector3(px - 0.2f, h * 0.48f, pz + 0.03f),
                    new Vector3(px - 0.03f, h * 0.4f, pz + 0.02f), green);
                Triangle(new Vector3(px, h * 0.48f, pz), new Vector3(px + 0.18f, h * 0.63f, pz - 0.04f),
                    new Vector3(px + 0.02f, h * 0.56f, pz - 0.02f), greenLight);

                // Ripening seed head: a short golden rachis with alternating kernels.
                float topX = px + leanX, topZ = pz + leanZ;
                Quad(new Vector3(topX - 0.035f, h - 0.26f, topZ), new Vector3(topX + 0.035f, h - 0.26f, topZ),
                    new Vector3(topX + 0.025f, h + 0.08f, topZ), new Vector3(topX - 0.025f, h + 0.08f, topZ), grain);
                for (int k = 0; k < 3; k++)
                {
                    float ky = h - 0.2f + k * 0.075f;
                    float side = k % 2 == 0 ? -1f : 1f;
                    Triangle(new Vector3(topX, ky + 0.03f, topZ),
                        new Vector3(topX + side * 0.085f, ky + 0.11f, topZ),
                        new Vector3(topX + side * 0.025f, ky - 0.015f, topZ), grainLight);
                    Triangle(new Vector3(topX, ky - 0.005f, topZ),
                        new Vector3(topX - side * 0.07f, ky + 0.06f, topZ),
                        new Vector3(topX - side * 0.02f, ky - 0.055f, topZ), grain);
                }
            }
        }

        meshTool.Index();
        var mesh = meshTool.Commit();
        var material = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.PerVertex,
            Roughness = 1f,
            SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        root.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        return root;
    }
}

/// <summary>
/// Parts that fade to see-through while a car is under them (a roof over the road): their own
/// materials, fading only when their geometry intersects the camera-to-car segment.
/// </summary>
public partial class FadeUnder : Node3D
{
    /// <summary>The player's car, set by the race every frame.</summary>
    public static Vector3 Car;
    /// <summary>Trailer-only option that keeps the world fully opaque in a replay capture.</summary>
    public static bool Off;
    readonly BuildingOcclusion _occlusion = new();
    readonly List<StandardMaterial3D> _mats = new();
    /// <summary>How far see-through (0 solid .. 1 at Faded); eased, so neither way is a hard switch.</summary>
    float _t, _alpha = 1f;
    const float Faded = 0.15f, FadeIn = 0.3f, FadeOut = 0.7f;

    public MeshInstance3D Part(Mesh mesh, Color c, Vector3 pos)
    {
        var m = new StandardMaterial3D { AlbedoColor = c, Transparency = BaseMaterial3D.TransparencyEnum.Alpha };
        _mats.Add(m);
        return new MeshInstance3D { Mesh = mesh, MaterialOverride = m, Position = pos, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
    }

    public override void _Ready() { Collect(this, Transform3D.Identity); AddToGroup("sight_occluder"); }

    public bool BlocksSight(Vector3 from, Vector3 to)
    {
        var inverse = GlobalTransform.AffineInverse();
        return _occlusion.Blocks(inverse * from, inverse * to);
    }

    void Collect(Node3D parent, Transform3D transform)
    {
        foreach (var child in parent.GetChildren())
        {
            if (child is not Node3D node) continue;
            var local = transform * node.Transform;
            if (node is MeshInstance3D { Mesh: not null } mesh) _occlusion.Add(mesh.Mesh, local);
            Collect(node, local);
        }
    }

    public override void _Process(double delta)
    {
        var camera = GetViewport()?.GetCamera3D();
        if (camera == null) return;
        var inverse = GlobalTransform.AffineInverse();
        bool blocks = !Off && _occlusion.Blocks(inverse * camera.GlobalPosition, inverse * (Car + Vector3.Up * 0.8f));
        float target = blocks ? 1f : 0f;
        _t = Mathf.MoveToward(_t, target, (float)delta / (target > _t ? FadeIn : FadeOut));
        float a = Mathf.Lerp(1f, Faded, Mathf.SmoothStep(0f, 1f, _t));
        if (a == _alpha) return;
        _alpha = a;
        foreach (var m in _mats) m.AlbedoColor = new Color(m.AlbedoColor, a);
    }
}
