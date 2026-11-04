using System;
using System.Collections.Generic;
using Godot;

namespace CCCPRacing.Track;

/// <summary>
/// The ploughed snow piled high along both sides of a winter road (TrackDef.Snowbanks). Not a row of
/// obstacles: a bank follows the centre line, its inner foot Gap beyond the road's edge and Width
/// across, so CarSim can ask where a point lies against it (Query) and the car meets one smooth
/// wall. Gaps (Except, in metres along the road, per side) leave the road open, and so does the
/// ice (a road on a frozen river has no banks). Towards a gap the bank tapers down to the ground
/// over TaperLength metres, lower and narrower, the wall with it.
/// </summary>
public class Snowbanks
{
    /// <summary>The bank's base across (m): the inner face rises over the first 0.9 m, the back falls to nothing.</summary>
    public const float Width = 2.6f;
    /// <summary>Metres over which a bank sinks to the ground at a gap.</summary>
    public const float TaperLength = 6f;

    readonly TrackData _t;
    public readonly float Height, Gap;
    readonly bool[] _openLeft, _openRight;
    readonly bool[] _foldLeft, _foldRight;      // rows of the mesh left out where the bank's back would fold over itself (the ridge runs on across)
    readonly float[] _taperLeft, _taperRight;
    readonly float[] _wideLeft, _wideRight;      // extra gap per sample (Widen), eased

    public Snowbanks(TrackData t, SnowbankDef def)
    {
        _t = t;
        Height = def.Height;
        Gap = def.Gap;
        int n = t.Center.Length;
        _openLeft = new bool[n];
        _openRight = new bool[n];
        foreach (var e in def.Except)
        {
            int from = (int)MathF.Floor(e.From), to = (int)MathF.Ceiling(e.To);
            if (to < from) to += n;
            for (int i = from; i <= to; i++)
            {
                int k = ((i % n) + n) % n;
                if (e.Side != "right") _openLeft[k] = true;
                if (e.Side != "left") _openRight[k] = true;
            }
        }
        // The ice: open a few metres beyond it either way.
        for (int i = 0; i < n; i++)
            if (t.CenterSurf[i] == SurfaceType.Ice)
                for (int k = -3; k <= 3; k++) { int j = ((i + k) % n + n) % n; _openLeft[j] = _openRight[j] = true; }
        _wideLeft = new float[n];
        _wideRight = new float[n];
        foreach (var e in def.Widen)
        {
            int from = (int)MathF.Floor(e.From), to = (int)MathF.Ceiling(e.To);
            if (to < from) to += n;
            for (int i = from; i <= to; i++)
            {
                int k = ((i % n) + n) % n;
                if (e.Side != "right") _wideLeft[k] = Math.Max(_wideLeft[k], e.By);
                if (e.Side != "left") _wideRight[k] = Math.Max(_wideRight[k], e.By);
            }
        }
        _wideLeft = Ease(_wideLeft);
        _wideRight = Ease(_wideRight);
        _foldLeft = Folds(true);
        _foldRight = Folds(false);
        _taperLeft = Tapers(_openLeft);
        _taperRight = Tapers(_openRight);
    }

    /// <summary>
    /// On the inside of a tight bend the bank's outer edge passes the centre of curvature, where the two legs of the bend meet:
    /// the mesh rows there run back over each other and crumple. They are marked (a margin of a sample either way) so the
    /// mesh leaves them out and joins the rows on either side: one ridge across the apex, the physics wall unchanged.
    /// </summary>
    bool[] Folds(bool left)
    {
        int n = _t.Center.Length;
        var raw = new bool[n];
        for (int i = 0; i < n; i++)
        {
            raw[i] = InsideCurvature(i, left) * Inner(i, left) > 0.8f;      // the bank's foot itself lies beyond the centre of curvature: the two legs meet here
        }
        var fold = new bool[n];
        for (int i = 0; i < n; i++)
            if (raw[i])
                for (int k = -1; k <= 2; k++) fold[((i + k) % n + n) % n] = true;
        return fold;
    }

    /// <summary>The mesh has no row at sample i on that side: the bank's back would fold there.</summary>
    public bool Folded(int i, bool left) => left ? _foldLeft[i] : _foldRight[i];

    /// <summary>A step in the extra gap becomes a slope over about TaperLength * 2 metres (a box blur round the loop, twice).</summary>
    static float[] Ease(float[] a)
    {
        int n = a.Length, r = (int)TaperLength;
        for (int pass = 0; pass < 2; pass++)
        {
            var b = new float[n];
            for (int i = 0; i < n; i++)
            {
                float s = 0;
                for (int k = -r; k <= r; k++) s += a[((i + k) % n + n) % n];
                b[i] = s / (2 * r + 1);
            }
            a = b;
        }
        return a;
    }

    /// <summary>0..1 per sample: how much of the bank stands there (0 in a gap and at its edge, 1 TaperLength beyond).</summary>
    static float[] Tapers(bool[] open)
    {
        int n = open.Length, reach = (int)TaperLength + 1;
        var tp = new float[n];
        for (int i = 0; i < n; i++)
        {
            int d = reach;
            for (int k = 0; k <= reach && d == reach; k++)
                if (open[((i + k) % n + n) % n] || open[((i - k) % n + n) % n]) d = k;
            tp[i] = SmoothStep(1f, TaperLength, d);
        }
        return tp;
    }

    static float SmoothStep(float e0, float e1, float x)
    {
        float u = Math.Clamp((x - e0) / (e1 - e0), 0f, 1f);
        return u * u * (3f - 2f * u);
    }

    /// <summary>How much of the bank stands at sample i on that side (0 none .. 1 the full bank).</summary>
    public float Taper(int i, bool left) => left ? _taperLeft[i] : _taperRight[i];

    /// <summary>Distance from the centre line to the bank's inner foot at sample i on that side.</summary>
    public float Inner(int i, bool left) => _t.HalfWidth[i] + Gap + (left ? _wideLeft[i] : _wideRight[i]);

    /// <summary>...on the side where it stands furthest out.</summary>
    public float Inner(int i) => Math.Max(Inner(i, true), Inner(i, false));

    /// <summary>How sharply the road turns towards that side at sample i (1/m; 0 when it turns away or runs straight): the bank stands on the inside of that bend.</summary>
    public float InsideCurvature(int i, bool left)
    {
        int n = _t.Center.Length;
        int a = ((i - 2) % n + n) % n, b = (i + 2) % n;
        Vector2 da = _t.CenterDir(a), db = _t.CenterDir(b);
        float turn = MathF.Atan2(da.X * db.Y - da.Y * db.X, da.Dot(db));      // + turns right of the heading, - left (x right, z down)
        float toward = left ? -turn : turn;
        if (toward <= 0f) return 0f;
        float len = (_t.Center[b] - _t.Center[a]).Length();
        return len < 1e-3f ? 0f : toward / len;
    }

    /// <summary>The bank is there on that side of sample i (left: the left of the driving direction).</summary>
    public bool Closed(int i, bool left) => !(left ? _openLeft[i] : _openRight[i]);

    /// <summary>
    /// A circle of radius r at p against the banks. hint: the centre-line sample found last time for
    /// this body (-1: search the whole road), updated. True when it overlaps a bank: push it out by
    /// pen along normal (horizontal, x/z); top is the bank's crest height there.
    /// </summary>
    public bool Query(Vector2 p, float r, ref int hint, out Vector2 normal, out float pen, out float top)
    {
        normal = Vector2.Zero;
        pen = 0;
        top = 0;
        var c = _t.Center;
        int n = c.Length;
        int best = -1;
        float bestD = float.MaxValue, bestT = 0, bestLat = 0;
        int from = hint < 0 ? 0 : hint - 25, to = hint < 0 ? n - 1 : hint + 25;
        for (int k = from; k <= to; k++)
        {
            int i = ((k % n) + n) % n, j = (i + 1) % n;
            Vector2 a = c[i], seg = c[j] - a;
            float len2 = seg.LengthSquared();
            if (len2 < 1e-6f) continue;
            float u = Math.Clamp((p - a).Dot(seg) / len2, 0f, 1f);
            Vector2 q = a + seg * u, d = p - q;
            float dist = d.LengthSquared();
            if (dist >= bestD) continue;
            bestD = dist;
            best = i;
            bestT = u;
            // Left of the driving direction is positive (x right, z down: left = (z, -x) turned).
            bestLat = (seg.X * d.Y - seg.Y * d.X) / MathF.Sqrt(len2) * -1f;
        }
        if (best < 0) return false;
        if (hint >= 0 && bestD > 30f * 30f) { hint = -1; return Query(p, r, ref hint, out normal, out pen, out top); }   // moved far (a tow, a reset)
        hint = best;
        bool left = bestLat > 0;
        if (!Closed(best, left)) return false;
        int nx = (best + 1) % n;
        float taper = Mathf.Lerp(Taper(best, left), Taper(nx, left), bestT);
        if (taper < 0.05f) return false;
        float inner = Mathf.Lerp(Inner(best, left), Inner(nx, left), bestT);
        float lat = Math.Abs(bestLat);
        if (lat > inner + Width + r) return false;
        Vector2 dir = (c[nx] - c[best]).Normalized();
        Vector2 leftN = new(dir.Y, -dir.X);         // points to the left of the driving direction
        Vector2 outward = left ? leftN : -leftN;
        if (lat - inner < Width * 0.5f)
        {
            pen = lat + r - inner;                  // against the inner face: pushed back onto the road
            normal = -outward;
        }
        else
        {
            pen = inner + Width - (lat - r);        // stuck out on the far side: pushed away from the road
            normal = outward;
        }
        top = Mathf.Lerp(_t.CenterH[best], _t.CenterH[nx], bestT) + Height * taper;
        return pen > 0;
    }
}

/// <summary>
/// The banks' mesh: a rough ridge of ploughed snow on each closed side, laid on the ground. The
/// inner face wanders a little (±20 cm) round where the physics has it; everything behind it is
/// irregular: low swells, big drift heaps, a crest and back that wander, every vertex nudged. Flat-shaded facets in the ground's
/// own deep-snow pixels, so the bank reads as the same snow heaped up.
/// </summary>
public partial class SnowbankMesh : MeshInstance3D
{
    // Cross-section: (offset beyond the inner foot, share of the height). The inner face is steep.
    static readonly (float o, float h)[] Profile =
        { (-0.25f, 0f), (0.25f, 0.55f), (0.6f, 0.88f), (1.0f, 1f), (1.5f, 0.92f), (2.0f, 0.68f), (2.5f, 0.32f), (Snowbanks.Width, 0f) };

    public static SnowbankMesh Build(TrackData t)
    {
        var banks = t.Banks;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        st.SetSmoothGroup(uint.MaxValue);          // flat facets, like the terrain
        int n = t.Center.Length;
        const int Step = 1;
        foreach (bool left in new[] { true, false })
        {
            Vector3[] prev = null;
            var joints = Joints(t, banks, left, out var handled);      // where the bank crosses itself on the inside of a tight bend: sample p -> (the sample the ridge comes back on, the joint row)
            int skipTo = -1;
            for (int i = 0; i <= n; i += Step)
            {
                if (i <= skipTo) continue;
                int k0 = i % n;
                // A gap's edge sample still gets a row, flat on the ground: the bank's end is closed.
                bool here = banks.Closed(k0, left) || banks.Closed((k0 + 1) % n, left) || banks.Closed((k0 - 1 + n) % n, left);
                if (!here) { prev = null; continue; }
                var row = Row(t, banks, k0, left, banks.Closed(k0, left) ? banks.Taper(k0, left) : 0f);
                if (prev != null)
                    for (int k = 0; k + 1 < Profile.Length; k++)
                    {
                        float shade = 0.94f + 0.06f * Noise.Hash01(k0 * 7 + k, left ? 1 : 2, 1942);
                        Color col = k <= 1 ? new Color(0.86f * shade, 0.9f * shade, 0.98f * shade)       // the cut face, a little blue
                            : k <= 3 ? new Color(shade, shade, shade) : new Color(0.96f * shade, 0.97f * shade, shade);
                        Vector3 a0 = prev[k], a1 = prev[k + 1], b0 = row[k], b1 = row[k + 1];
                        if (left) Quad(st, a0, a1, b1, b0, col);
                        else Quad(st, a0, b0, b1, a1, col);
                    }
                prev = row;
                if (joints.TryGetValue(i, out var jt))
                {
                    // The ridge crosses itself here: the loop between is cut away and the two arms meet in one joint row.
                    for (int k = 0; k + 1 < Profile.Length; k++)
                    {
                        Color col = k <= 1 ? new Color(0.86f, 0.9f, 0.98f) : new Color(0.97f, 0.97f, 0.97f);
                        if (left) Quad(st, prev[k], prev[k + 1], jt.row[k + 1], jt.row[k], col);
                        else Quad(st, prev[k], jt.row[k], jt.row[k + 1], prev[k + 1], col);
                    }
                    prev = jt.row;
                    skipTo = jt.to;
                }
            }
        }
        st.GenerateNormals();
        var mat = new StandardMaterial3D
        {
            AlbedoTexture = TileAtlas.Strip(SurfaceType.DeepSnow, 1943),
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            VertexColorUseAsAlbedo = true,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.PerVertex,
            Roughness = 1f,
            SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        st.SetMaterial(mat);
        return new SnowbankMesh { Name = "Snowbanks", Mesh = st.Commit() };
    }


    /// <summary>
    /// Where the bank crosses itself on the inside of a tight bend (its two legs meet): for each run of folded samples the crest
    /// line is searched for the first pair of its segments that cross. The loop between them is cut away, and for every line of the
    /// profile the joint row sits where that line's two arms cross, so the two banks run into one another cleanly.
    /// </summary>
    static Dictionary<int, (int to, Vector3[] row)> Joints(TrackData t, Snowbanks banks, bool left, out HashSet<int> handled)
    {
        handled = new HashSet<int>();
        var result = new Dictionary<int, (int to, Vector3[] row)>();
        int n = t.Center.Length;
        const int Crest = 3;
        int i = 0;
        while (i < n)
        {
            if (!(banks.Closed(i, left) && banks.Folded(i, left))) { i++; continue; }
            int first = i;
            while (i < n && banks.Closed(i % n, left) && banks.Folded(i % n, left)) i++;
            int a = Math.Max(0, first - 3), b = Math.Min(n - 1, i + 2);
            var rows = new Dictionary<int, Vector3[]>();
            Vector3[] R(int x) { if (!rows.TryGetValue(x, out var r)) rows[x] = r = Row(t, banks, x % n, left, banks.Closed(x % n, left) ? banks.Taper(x % n, left) : 0f); return r; }
            bool done = false;
            for (int p = a; p < b - 1 && !done; p++)
                for (int q = p + 2; q < b && !done; q++)
                    if (Cross(R(p)[Crest], R(p + 1)[Crest], R(q)[Crest], R(q + 1)[Crest], out _, out _))
                    {
                        var joint = new Vector3[Profile.Length];
                        for (int k = 0; k < Profile.Length; k++)
                        {
                            if (Cross(R(p)[k], R(p + 1)[k], R(q)[k], R(q + 1)[k], out float tt, out float uu, 0.5f))
                            {
                                Vector3 pa = R(p)[k].Lerp(R(p + 1)[k], tt), pb = R(q)[k].Lerp(R(q + 1)[k], uu);
                                joint[k] = pa.Lerp(pb, 0.5f);
                            }
                            else joint[k] = R(p + 1)[k].Lerp(R(q)[k], 0.5f);
                        }
                        result[p] = (q, joint);
                        for (int x = a; x <= b; x++) handled.Add(x % n);      // this run is cut at its crossing, not bridged
                        done = true;
                    }
        }
        return result;
    }

    /// <summary>Whether segments a0-a1 and b0-b1 cross on the map (x, z); t and u are the positions along them. slack lets the lines cross a little beyond their ends.</summary>
    static bool Cross(Vector3 a0, Vector3 a1, Vector3 b0, Vector3 b1, out float t, out float u, float slack = 0f)
    {
        t = u = 0f;
        float rx = a1.X - a0.X, rz = a1.Z - a0.Z, sx = b1.X - b0.X, sz = b1.Z - b0.Z;
        float den = rx * sz - rz * sx;
        if (MathF.Abs(den) < 1e-6f) return false;
        t = ((b0.X - a0.X) * sz - (b0.Z - a0.Z) * sx) / den;
        u = ((b0.X - a0.X) * rz - (b0.Z - a0.Z) * rx) / den;
        return t >= -slack && t <= 1f + slack && u >= -slack && u <= 1f + slack;
    }

    static Vector3[] Row(TrackData t, Snowbanks banks, int i, bool left, float taper)
    {
        Vector2 c = t.Center[i], dir = t.CenterDir(i);
        Vector2 side = left ? new Vector2(dir.Y, -dir.X) : new Vector2(-dir.Y, dir.X);
        int seed = left ? 1941 : 1945;
        // Low swells every ~10 m, now and then a drift heap half again as high and wider.
        float N01(float x, float z, int sd) => Noise.Value(x, z, sd) * 0.5f + 0.5f;
        float swell = 0.8f + 0.45f * N01(i / 9f, 0.5f, seed);
        float heap = Math.Clamp((N01(i / 23f, 4.5f, seed + 1) - 0.62f) / 0.25f, 0f, 1f);
        float tall = swell * (1f + 0.55f * heap) * taper;
        float wide = (1f + 0.35f * N01(i / 13f, 8.5f, seed + 2) + 0.5f * heap) * (0.35f + 0.65f * taper);   // narrower as it sinks
        // The cut face wanders too, about 20 cm either way of where the car meets it: the plough's
        // blade never ran straight.
        float face = 0.2f * Noise.Value(i / 4f, 12.5f, seed + 4);
        // On the inside of a bend the back of the bank must stay short of the centre of curvature, or the rows fold over each other:
        // squeeze everything behind the cut face to fit (the face itself stays where the physics has it).
        float squeeze = 1f;
        float kappa = banks.InsideCurvature(i, left);
        if (kappa > 1e-3f)
        {
            float room = 0.8f / kappa - banks.Inner(i, left);      // metres from the inner foot to 80 % of the radius
            squeeze = Math.Clamp((room - 0.25f) / (Snowbanks.Width * 1.8f), 0.12f, 1f);
        }
        var row = new Vector3[Profile.Length];
        for (int k = 0; k < Profile.Length; k++)
        {
            float jit = Noise.Hash01(i, k, seed + 3) - 0.5f;
            float o = Profile[k].o, h = Profile[k].h;
            if (k >= 2) o = 0.6f + (o - 0.6f) * wide + 0.35f * jit;     // the crest and back wander
            else o += face + 0.12f * jit;
            if (k > 0 && k < Profile.Length - 1) h *= 1f + 0.3f * jit;   // every vertex nudged up or down
            if (k > 0 && squeeze < 1f) o = 0.25f + (o - 0.25f) * squeeze;
            Vector2 p = c + side * (banks.Inner(i, left) + o);
            float ground = t.MeshHeight(p.X, p.Y);
            if (k == 0)
            {
                // The cut face starts at the road's level so the road meets it with no step, but only where the bank stands at the road's edge
                // and only a little: a bank pushed out (Widen) or over ground that falls away (the runway's embankment) rests on the ground.
                float extra = banks.Inner(i, left) - t.HalfWidth[i] - banks.Gap;
                float near = Math.Clamp(1f - extra, 0f, 1f);
                ground += Math.Min(Math.Max(0f, t.CenterH[i] - 0.05f - ground), 0.25f) * near;
            }
            row[k] = new Vector3(p.X, ground + banks.Height * h * tall, p.Y);
        }
        return row;
    }

    static void Quad(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col)
    {
        st.SetColor(col);
        // The ground's pixels: 8 px a metre, the four tile variants side by side along x.
        void V(Vector3 p) { st.SetUV(new Vector2(p.X / 8f, p.Z / 2f)); st.AddVertex(p); }
        V(a); V(b); V(c);
        V(a); V(c); V(d);
    }
}

/// <summary>
/// Snow poles along a winter road: a thin orange-red pole every Spacing metres on both sides,
/// standing in the bank's inner face and showing a metre above it, a yellow reflective band at the
/// top (white vanished against the snow). Only where the bank stands full height (not at its gaps, on the ice or tapering). Not solid.
/// The band is a glossy shaded yellow that glows faintly after dark (NightGlow), brighter in the headlights.
/// </summary>
public partial class SnowPoles : Node3D
{
    // A size up from life (a real one is ~5 cm thick), or the chase camera sees only dots.
    const float Spacing = 25f, Height = 2.7f, Thick = 0.16f, Band = 0.42f, In = 0.3f;
    /// <summary>The bands' faint glow at full night (the night scales it up from nothing at dusk).</summary>
    const float GlowAtNight = 0.35f;

    public static SnowPoles Build(TrackData t)
    {
        var banks = t.Banks;
        var poles = new SnowPoles { Name = "SnowPoles" };
        var at = new List<Transform3D>();
        int n = t.Center.Length;
        foreach (bool left in new[] { true, false })
            for (float s = left ? 0f : Spacing * 0.5f; s < n; s += Spacing)
            {
                int i = (int)s % n;
                if (!banks.Closed(i, left) || banks.Taper(i, left) < 0.95f) continue;
                Vector2 c = t.Center[i], dir = t.CenterDir(i);
                Vector2 side = left ? new Vector2(dir.Y, -dir.X) : new Vector2(-dir.Y, dir.X);
                Vector2 p = c + side * (banks.Inner(i, left) + In);
                // Each leans a little, one way or another, as the plough left it.
                float lx = (Noise.Hash01(i, left ? 3 : 4, 1950) - 0.5f) * 0.12f, lz = (Noise.Hash01(i, left ? 5 : 6, 1950) - 0.5f) * 0.12f;
                var b = new Basis(Vector3.Up, TrackData.YawFor(dir)) * new Basis(Vector3.Right, lx) * new Basis(Vector3.Back, lz);
                at.Add(new Transform3D(b, new Vector3(p.X, t.MeshHeight(p.X, p.Y), p.Y)));
            }
        poles.AddChild(Many(new BoxMesh { Size = new Vector3(Thick, Height, Thick) }, Height / 2f, Props.Mat(new Color(0.9f, 0.32f, 0.08f)), at));
        var shine = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 0.86f, 0.12f), Roughness = 0.12f, MetallicSpecular = 1f,
            EmissionEnabled = true, Emission = new Color(1f, 0.8f, 0.2f), EmissionEnergyMultiplier = 0f,
        };
        NightGlow.Glows.Add((shine, GlowAtNight));
        poles.AddChild(Many(new BoxMesh { Size = new Vector3(Thick * 1.25f, Band, Thick * 1.25f) }, Height - Band / 2f - 0.06f, shine, at));
        if (OS.HasEnvironment("POLEDBG")) GD.Print($"snow poles: {at.Count}");
        return poles;
    }

    static MultiMeshInstance3D Many(Mesh mesh, float y, Material mat, List<Transform3D> at)
    {
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh, InstanceCount = at.Count };
        for (int k = 0; k < at.Count; k++) mm.SetInstanceTransform(k, at[k] * new Transform3D(Basis.Identity, new Vector3(0, y, 0)));
        return new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
    }
}
