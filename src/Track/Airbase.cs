using System;
using System.Collections.Generic;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Track;

/// <summary>
/// The highway strip on the main straight (markings, edge lights) and the airbase props:
/// an arched hardened aircraft shelter, a silver Balalaika and a windsock.
/// </summary>
public static class Airbase
{
    /// <summary>Shared positions for runway light meshes and underground electrical terminals.</summary>
    public static IEnumerable<Vector2> EdgeLights(TrackData t)
    {
        if (t.Runway == null) yield break;
        int n = t.Center.Length, from = (int)t.Runway.From;
        int len = ((int)(t.Runway.To - t.Runway.From) % n + n) % n;
        float hw = t.HalfWidth[(from % n + n) % n];
        for (int k = 0; k <= len; k += 20)
        foreach (float s in new[] { -1f, 1f })
        {
            int i = ((from + k) % n + n) % n;
            var d = t.CenterDir(i);
            yield return t.Center[i] + new Vector2(-d.Y, d.X) * (s * (hw + 0.6f));
        }
    }
    static readonly Color Paint = new(0.93f, 0.93f, 0.9f);
    static readonly Color Silver = new(0.74f, 0.75f, 0.77f);
    static readonly Color DarkMetal = new(0.25f, 0.25f, 0.27f);
    static readonly Color Glass = new(0.2f, 0.28f, 0.33f);
    static readonly Color Red = new(0.8f, 0.1f, 0.08f);

    // ------------------------------------------------------------ runway markings

    public static Node3D BuildRunway(TrackData t)
    {
        var root = new Node3D { Name = "Runway" };
        if (t.Runway == null) return root;
        var rw = t.Runway;
        int n = t.Center.Length;
        int len = ((int)(rw.To - rw.From) % n + n) % n;

        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        Vector2 P(int i, float lateral)
        {
            i = ((i % n) + n) % n;
            Vector2 d = t.CenterDir(i);
            return t.Center[i] + new Vector2(-d.Y, d.X) * lateral;
        }

        // Strip along the road between two distances at a lateral band.
        void Band(int a, int b, float l0, float l1, Color c)
        {
            for (int i = a; i < b; i += 2)
            {
                int j = Math.Min(i + 2, b);
                Quad(st, t, P(i, l0), P(i, l1), P(j, l1), P(j, l0), c);
            }
        }

        int from = (int)rw.From;
        float hw = t.HalfWidth[from % n];
        // Edge lines and centreline dashes.
        Band(from, from + len, hw - 1.0f, hw - 0.7f, Paint);
        Band(from, from + len, -hw + 0.7f, -hw + 1.0f, Paint);
        for (int k = 14; k < len - 14; k += 15) Band(from + k, from + k + 6, -0.25f, 0.25f, Paint);
        // Threshold "piano keys" at both ends.
        foreach (int start in new[] { from + 2, from + len - 10 })
            for (float l = -hw + 1.6f; l < hw - 1.5f; l += 1.3f)
                if (Math.Abs(l) > 0.9f) Band(start, start + 8, l, l + 0.7f, Paint);

        st.SetMaterial(new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        });
        root.AddChild(new MeshInstance3D { Mesh = st.Commit(), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });

        // Runway designators, readable to a pilot rolling in from each end.
        if (rw.Numbers is { Length: 2 })
        {
            root.AddChild(Designator(t, rw.Numbers[0], from + 13, +1));
            root.AddChild(Designator(t, rw.Numbers[1], from + len - 13, -1));
        }

        // Edge lights: short posts with yellow caps (decoration only).
        foreach (Vector2 p in EdgeLights(t))
            {
                float y = t.MeshHeight(p.X, p.Y);
                root.AddChild(Props.Part(Props.Box(0.12f, 0.35f, 0.12f), DarkMetal, new Vector3(p.X, y + 0.17f, p.Y)));
                var cap = new MeshInstance3D
                {
                    Mesh = Props.Box(0.22f, 0.12f, 0.22f),
                    Position = new Vector3(p.X, y + 0.4f, p.Y),
                    MaterialOverride = NightGlow.Keep(new StandardMaterial3D { AlbedoColor = new Color(1f, 0.85f, 0.3f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded }),   // lit: they shine at night
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                };
                root.AddChild(cap);
                NightGlow.Lamps.Add(cap);
            }
        return root;
    }

    static MeshInstance3D Designator(TrackData t, string text, int i, int travelSign)
    {
        const float W = 5f, H = 6.5f;
        int n = t.Center.Length;
        i = ((i % n) + n) % n;
        Vector2 c = t.Center[i];
        Vector2 d = t.CenterDir(i) * travelSign;     // the top of the text points the way the pilot is rolling
        Vector2 r = new(-d.Y, d.X);
        // Painted like the bands: every font pixel is a vertex-coloured quad draped over the road.
        // (A textured flat quad sank into the crowned strip and flickered, and its texture
        // sparkled with stray colours on the Vulkan renderer.)
        var img = PixelFont.RenderImage(text, Colors.White, new Color(0, 0, 0, 0), 0);
        int iw = img.GetWidth(), ih = img.GetHeight();
        Vector2 At(float x, float y) => c + r * ((x / iw - 0.5f) * W) + d * ((0.5f - y / ih) * H);
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        for (int y = 0; y < ih; y++)
        for (int x = 0; x < iw; x++)
            if (img.GetPixel(x, y).A > 0.5f)
                Quad(st, t, At(x, y), At(x + 1, y), At(x + 1, y + 1), At(x, y + 1), Paint);
        st.SetMaterial(new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        });
        return new MeshInstance3D { Mesh = st.Commit(), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
    }

    static void Quad(SurfaceTool st, TrackData t, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color col)
    {
        Vector3 G(Vector2 p) => new(p.X, t.MeshHeight(p.X, p.Y) + 0.04f, p.Y);
        Vector3 A = G(a), B = G(b), C = G(c), D = G(d);
        foreach (var v in new[] { A, B, C, A, C, D })
        {
            st.SetColor(col);
            st.AddVertex(v);
        }
    }

    // ------------------------------------------------------------ hangar

    /// <summary>Arched hardened aircraft shelter, camouflaged, open towards +Z.</summary>
    public static Node3D Hangar(bool winter = false)
    {
        const float R = 9f, Half = 11f, Wall = 0.8f;
        const int Seg = 14, ZSeg = 6;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var rng = new RandomNumberGenerator { Seed = 21 };
        Color[] camo = { new(0.36f, 0.4f, 0.28f), new(0.45f, 0.42f, 0.32f), new(0.3f, 0.33f, 0.26f), new(0.5f, 0.5f, 0.46f) };
        var interior = new Color(0.12f, 0.12f, 0.12f);
        var concrete = new Color(0.55f, 0.55f, 0.5f);

        Vector3 Arc(float r, int k, float z)
        {
            float a = MathF.PI * k / Seg;
            return new Vector3(MathF.Cos(a) * r, MathF.Sin(a) * r, z);
        }
        void Tri(Vector3 a, Vector3 b, Vector3 c, Color col, Vector3 n)
        {
            foreach (var v in new[] { a, b, c }) { st.SetColor(col); st.SetNormal(n); st.AddVertex(v); }
        }

        for (int k = 0; k < Seg; k++)
        for (int zi = 0; zi < ZSeg; zi++)
        {
            float z0 = -Half + 2 * Half * zi / ZSeg, z1 = -Half + 2 * Half * (zi + 1) / ZSeg;
            Color c = camo[rng.RandiRange(0, camo.Length - 1)];
            Vector3 n = (Arc(1, k, 0) + Arc(1, k + 1, 0)).Normalized();
            Vector3 a = Arc(R, k, z0), b = Arc(R, k + 1, z0), cc = Arc(R, k + 1, z1), d = Arc(R, k, z1);
            Tri(a, b, cc, c, n); Tri(a, cc, d, c, n);
            // Inside of the vault, dark.
            Vector3 ai = Arc(R - Wall, k, z0), bi = Arc(R - Wall, k + 1, z0), ci = Arc(R - Wall, k + 1, z1), di = Arc(R - Wall, k, z1);
            Tri(ai, bi, ci, interior, -n); Tri(ai, ci, di, interior, -n);
        }
        for (int k = 0; k < Seg; k++)
        {
            // Front ring (concrete edge of the arch) and closed back wall.
            Vector3 o0 = Arc(R, k, Half), o1 = Arc(R, k + 1, Half), i0 = Arc(R - Wall, k, Half), i1 = Arc(R - Wall, k + 1, Half);
            Tri(o0, o1, i1, concrete, Vector3.Back); Tri(o0, i1, i0, concrete, Vector3.Back);
            Vector3 b0 = Arc(R, k, -Half), b1 = Arc(R, k + 1, -Half);
            Tri(new Vector3(0, 0, -Half), b0, b1, camo[0], Vector3.Forward);
            Tri(new Vector3(0, 0, -Half + 0.1f), Arc(R - Wall, k, -Half + 0.1f), Arc(R - Wall, k + 1, -Half + 0.1f), interior, Vector3.Back);
        }
        // Dark floor inside so the open mouth reads as deep.
        Tri(new Vector3(-R + Wall, 0.03f, -Half), new Vector3(R - Wall, 0.03f, -Half), new Vector3(R - Wall, 0.03f, Half), interior, Vector3.Up);
        Tri(new Vector3(-R + Wall, 0.03f, -Half), new Vector3(R - Wall, 0.03f, Half), new Vector3(-R + Wall, 0.03f, Half), interior, Vector3.Up);
        // Red star over the entrance.
        Star(st, new Vector3(0, R - Wall * 0.5f - 1.2f, Half + 0.05f), Vector3.Back, Vector3.Up, 1.1f, Red);

        st.SetMaterial(LitVertexMaterial());
        var n0 = new Node3D();
        n0.AddChild(new MeshInstance3D { Mesh = st.Commit(), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        SiberiaModelDetails.Hangar(n0, winter);
        return n0;
    }

    // ------------------------------------------------------------ Balalaika

    /// <summary>Silver MiK-21 (a made-up delta-wing fighter), nose towards -Z, ~14 m long, 7 m span, on its gear, a missile on each wingtip rail.</summary>
    public static Node3D Balalaika()
    {
        var n = new Node3D();
        const float Y = 1.6f;   // fuselage axis height
        // Fuselage, intake with shock cone, jet pipe.
        n.AddChild(Props.Part(Props.Cyl(0.62f, 11f, 10), Silver, new Vector3(0, Y, 0.3f), new Vector3(90, 0, 0)));
        n.AddChild(Props.Part(Props.Cyl(0.55f, 0.3f, 10), DarkMetal, new Vector3(0, Y, -5.25f), new Vector3(90, 0, 0)));
        n.AddChild(Props.Part(Props.Cyl(0.32f, 1.1f, 8, 0.0f), Silver, new Vector3(0, Y, -5.7f), new Vector3(-90, 0, 0)));
        n.AddChild(Props.Part(Props.Cyl(0.5f, 0.5f, 10, 0.45f), DarkMetal, new Vector3(0, Y, 6.0f), new Vector3(90, 0, 0)));
        // Canopy and dorsal spine.
        var canopy = Props.Part(Props.Ball(0.42f, 8), Glass, new Vector3(0, Y + 0.55f, -2.7f));
        canopy.Scale = new Vector3(0.85f, 0.75f, 1.9f);
        n.AddChild(canopy);
        n.AddChild(Props.Part(Props.Box(0.32f, 0.3f, 5.2f), Silver, new Vector3(0, Y + 0.55f, 0.6f)));
        // Landing gear (own node so flying jets can retract it).
        var gear = new Node3D { Name = "Gear" };
        n.AddChild(gear);
        foreach (var (x, z) in new[] { (0f, -3.3f), (-1.25f, 0.9f), (1.25f, 0.9f) })
        {
            gear.AddChild(Props.Part(Props.Box(0.12f, Y - 0.3f, 0.12f), DarkMetal, new Vector3(x, (Y - 0.3f) / 2f + 0.3f, z)));
            gear.AddChild(Props.Part(Props.Cyl(0.32f, 0.2f, 8), new Color(0.12f, 0.12f, 0.12f), new Vector3(x, 0.32f, z), new Vector3(0, 0, 90)));
        }

        // Wingtip launch rails, each with an  missile (MissileL / MissileR).
        foreach (float s in new[] { -1f, 1f })
        {
            var rail = Missile.Rail();
            rail.Name = s < 0 ? "MissileL" : "MissileR";
            rail.Position = new Vector3(s * 3.85f, Y - 0.62f, 2.9f);   // just outboard of the tip so it shows from above
            n.AddChild(rail);
            n.AddChild(Props.Part(Props.Box(0.4f, 0.1f, 0.9f), Silver, new Vector3(s * 3.7f, Y - 0.32f, 3.2f)));   // tip fairing onto the rail
        }

        // Delta wings, tail fin, tailplanes and red stars as one flat-shaded mesh.
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var wingCol = Silver * 0.93f;
        foreach (float s in new[] { -1f, 1f })
        {
            TriFlat(st, new Vector3(s * 0.55f, Y - 0.2f, -1.2f), new Vector3(s * 0.55f, Y - 0.2f, 3.8f), new Vector3(s * 3.6f, Y - 0.3f, 3.5f), wingCol);
            TriFlat(st, new Vector3(s * 0.5f, Y, 4.3f), new Vector3(s * 0.5f, Y, 5.9f), new Vector3(s * 2.0f, Y - 0.05f, 5.7f), wingCol);
            Star(st, new Vector3(s * 2.3f, Y - 0.22f, 2.6f), Vector3.Up, Vector3.Forward, 0.45f, Red);
        }
        // Swept fin.
        Vector3 f0 = new(0, Y + 0.5f, 2.4f), f1 = new(0, Y + 0.5f, 5.8f), f2 = new(0, Y + 2.5f, 6.0f), f3 = new(0, Y + 2.5f, 5.0f);
        TriFlat(st, f0, f1, f2, Silver);
        TriFlat(st, f0, f2, f3, Silver);
        foreach (float s in new[] { -1f, 1f })
            Star(st, new Vector3(s * 0.02f, Y + 1.5f, 4.7f), new Vector3(s, 0, 0), Vector3.Up, 0.5f, Red);
        st.SetMaterial(LitVertexMaterial());
        n.AddChild(new MeshInstance3D { Mesh = st.Commit(), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        SiberiaModelDetails.Jet(n, gear);
        JetLights(n, gear);
        return n;
    }

    /// <summary>
    /// The lights a fighter has to be seen and to see by at night: steady red (port, -X) and green
    /// (starboard) at the wingtips, a white tail light, two red anti-collision beacons (back and belly)
    /// that flash twice every 1.2 s, and a landing light on the nose gear lighting the ground ahead. All glow only
    /// after dark (NightGlow); by day they are small painted lenses.
    /// </summary>
    static void JetLights(Node3D n, Node3D gear)
    {
        const float Y = 1.6f;
        var green = new Color(0.1f, 0.9f, 0.25f);
        n.AddChild(NightGlow.Part(Props.Ball(0.13f, 6), new Color(0.65f, 0.08f, 0.06f), new Color(1f, 0.1f, 0.05f), 3f, new Vector3(-3.62f, Y - 0.3f, 3.55f)));
        n.AddChild(NightGlow.Part(Props.Ball(0.13f, 6), new Color(0.08f, 0.5f, 0.15f), green, 3f, new Vector3(3.62f, Y - 0.3f, 3.55f)));
        n.AddChild(NightGlow.Part(Props.Ball(0.1f, 6), new Color(0.8f, 0.8f, 0.75f), new Color(1f, 0.97f, 0.9f), 3f, new Vector3(0, Y + 2.5f, 6.1f)));
        var beacons = new JetBeacons { Name = "Beacons" };
        foreach (float y in new[] { Y + 0.82f, Y - 0.68f })
            beacons.Add(NightGlow.Part(Props.Ball(0.17f, 6), new Color(0.7f, 0.08f, 0.06f), new Color(1f, 0.08f, 0.04f), 4f, new Vector3(0, y, 1.6f)));
        n.AddChild(beacons);
        // The landing light on the nose gear (it goes with the gear when that is drawn up).
        gear.AddChild(NightGlow.Part(Props.Ball(0.11f, 6), new Color(0.8f, 0.8f, 0.7f), new Color(1f, 0.95f, 0.8f), 3f, new Vector3(0, 0.72f, -3.35f)));
        var lamp = new SpotLight3D
        {
            Position = new Vector3(0, 0.72f, -3.45f), RotationDegrees = new Vector3(-7f, 0, 0), LightColor = new Color(1f, 0.95f, 0.82f),
            SpotRange = 46f, SpotAngle = 20f, SpotAttenuation = 0.6f, ShadowEnabled = false, LightSpecular = 0f, LightEnergy = 0f, Visible = false,
        };
        lamp.SetMeta("night_energy", 2.6f);
        gear.AddChild(lamp);
    }

    // ------------------------------------------------------------ windsock

    public static Node3D Windsock()
    {
        var n = new Node3D();
        n.AddChild(Props.Part(Props.Cyl(0.08f, 6f, 6), new Color(0.85f, 0.85f, 0.85f), new Vector3(0, 3f, 0)));
        // Sock blowing towards +X, sagging a little, orange/white bands.
        for (int k = 0; k < 4; k++)
        {
            float r0 = 0.45f - k * 0.08f;
            var band = Props.Part(Props.Cyl(r0 - 0.08f, 0.55f, 8, r0), k % 2 == 0 ? new Color(1f, 0.45f, 0.1f) : Colors.White,
                new Vector3(0.35f + k * 0.55f, 5.8f - k * 0.08f, 0), new Vector3(0, 0, 90 - 6));
            n.AddChild(band);
        }
        return n;
    }

    // ------------------------------------------------------------ helpers

    static StandardMaterial3D LitVertexMaterial() => new()
    {
        VertexColorUseAsAlbedo = true,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.PerVertex,
        Roughness = 0.8f,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    static void TriFlat(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Color col)
    {
        Vector3 nrm = (b - a).Cross(c - a).Normalized();
        if (nrm.Y < 0) nrm = -nrm;
        foreach (var v in new[] { a, b, c }) { st.SetColor(col); st.SetNormal(nrm); st.AddVertex(v); }
    }

    /// <summary>Five-pointed star in the plane with normal <paramref name="n"/>, "up" along <paramref name="up"/>.</summary>
    static void Star(SurfaceTool st, Vector3 c, Vector3 n, Vector3 up, float r, Color col)
    {
        Vector3 right = up.Cross(n).Normalized();
        Vector3 u = n.Cross(right).Normalized();
        Vector3 offset = n.Normalized() * 0.01f;
        for (int k = 0; k < 10; k++)
        {
            float a0 = MathF.PI / 2f + k * MathF.PI / 5f, a1 = a0 + MathF.PI / 5f;
            float r0 = k % 2 == 0 ? r : r * 0.4f, r1 = k % 2 == 0 ? r * 0.4f : r;
            Vector3 p0 = c + offset + (right * MathF.Cos(a0) + u * MathF.Sin(a0)) * r0;
            Vector3 p1 = c + offset + (right * MathF.Cos(a1) + u * MathF.Sin(a1)) * r1;
            foreach (var v in new[] { c + offset, p0, p1 }) { st.SetColor(col); st.SetNormal(n); st.AddVertex(v); }
        }
    }
}

/// <summary>A fighter's anti-collision beacons: two quick flashes every 1.2 s (dark by day too).</summary>
public partial class JetBeacons : Node3D
{
    readonly System.Collections.Generic.List<MeshInstance3D> _lamps = new();
    float _t;

    public void Add(MeshInstance3D lamp)
    {
        _lamps.Add(lamp);
        AddChild(lamp);
    }

    public override void _Process(double delta)
    {
        _t = (_t + (float)delta) % 1.2f;
        bool on = _t < 0.09f || _t is > 0.24f and < 0.33f;
        foreach (var l in _lamps) l.Visible = on;
    }
}
