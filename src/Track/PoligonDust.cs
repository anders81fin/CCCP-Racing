using System;
using System.Collections.Generic;
using Godot;

namespace CCCPRacing.Track;

/// <summary>
/// The Poligon's wind (the owner's trial, 2026-11-23): now and then a thin, low streak of dust crosses the road from the shoulder of one open gravel ridge, blowing the way the camp's windsock points. It stops
/// where the ground turns to mud.
///
/// Replay and tape: the streak is a closed-form function of ONE clock (<see cref="Update"/>'s argument: the lap clock in a drive, the recording's own clock in the title replay and the viewer, a seek or a loop
/// included). Nothing is spawned, nothing is kept between frames and the live simulation has nothing to suppress, so the picture at a clock is the same in a drive and in its replay and a seek can leave no stray
/// state; the tape therefore carries no event for it (a deliberate exclusion, stated in the completion report). The scene frees it with the world on a track change.
/// </summary>
public partial class PoligonDust : Node3D
{
    /// <summary>Master switch (a test turns it off for the "before" pictures).</summary>
    public static bool Enabled = true;

    // The schedule: a gust every ~17 s, each one a loose cloud of PuffsPerGust puffs born over its first few seconds (more of them at the front), each puff with its own length, speed, drift sideways and life, so the
    // gust spreads and breaks up as it crosses the road instead of a set of equal parallel streaks (the owner's review, 2026-11-23).
    public const float First = 5f, Period = 17f, Jitter = 5f, BirthSpan = 3.2f, MaxLife = 6.6f, GustSpan = BirthSpan + MaxLife;
    public const int PuffsPerGust = 14;
    public const float ReachMud = 0.6f;

    readonly TrackData _t;
    readonly Vector2 _wind;                 // unit, the way the windsock blows
    readonly Vector2 _origin;               // the shoulder the gust starts from
    readonly float _crestH;
    readonly List<MeshInstance3D> _panels = new();
    readonly StandardMaterial3D _material;
    static readonly Color DustColor = new(0.93f, 0.86f, 0.72f);
    public int RidgeIndex { get; }
    public Vector2 Wind => _wind;
    public Vector2 Origin => _origin;

    /// <summary>The way the camp's windsock blows (unit, ground plane), null when the track has none: the dust and the hay both follow it.</summary>
    public static Vector2? WindOf(TrackData t)
    {
        if (t == null) return null;
        foreach (var p in t.Props) if (p.Type == "windsock") { float a = Mathf.DegToRad(p.Rotation); return new Vector2(MathF.Cos(a), -MathF.Sin(a)).Normalized(); }
        return null;
    }

    /// <summary>The open gravel ridge the dust and the hay belong to: the highest ~50 m gravel stretch where the wind crosses the road (the centre sample's index), -1 when the track has none.</summary>
    public static int RidgeOf(TrackData t, Vector2 wind)
    {
        if (t?.CenterSurf == null || t.Center.Length < 100) return -1;
        int n = t.Center.Length, best = -1; float bestH = float.MinValue;
        for (int i = 0; i < n; i++)
        {
            bool gravel = true;
            for (int k = -25; k <= 25 && gravel; k += 5) gravel = t.CenterSurf[((i + k) % n + n) % n] == SurfaceType.Gravel;      // (an open stretch of gravel ~50 m long)
            // the wind must cross the road there (within ~30 degrees of square), so that the streak crosses it from the shoulder instead of running down it
            if (gravel && MathF.Abs(t.CenterDir(i).Dot(wind.Normalized())) < 0.5f && t.CenterH[i] > bestH) { bestH = t.CenterH[i]; best = i; }
        }
        return best;
    }

    /// <summary>The effect for a track whose camp has a windsock and which has an open gravel ridge; null otherwise (no dust, nothing else changes).</summary>
    public static PoligonDust TryCreate(TrackData t)
    {
        Vector2? wind = WindOf(t);      // (the sock blows towards its local +X)
        if (wind == null) return null;
        int best = RidgeOf(t, wind.Value);
        if (best < 0) return null;
        return new PoligonDust(t, best, wind.Value.Normalized());
    }

    PoligonDust(TrackData t, int ridge, Vector2 wind)
    {
        _t = t; RidgeIndex = ridge; _wind = wind; _crestH = t.CenterH[ridge];
        Name = "PoligonDust";
        // the shoulder on the upwind side: the road's normal that points against the wind
        Vector2 c = t.Center[ridge], d = t.CenterDir(ridge), nrm = new(-d.Y, d.X);
        if (nrm.Dot(wind) > 0) nrm = -nrm;
        _origin = c + nrm * (t.HalfWidth[ridge] + 0.8f);
        var mat = _material = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha, AlbedoColor = new Color(0.78f, 0.7f, 0.55f), AlbedoTexture = MakeTexture(),
            CullMode = BaseMaterial3D.CullModeEnum.Disabled, Roughness = 1f, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,      // (dust scatters the light from every side: it is tinted by the scene's light each frame instead, see Update)
            BlendMode = BaseMaterial3D.BlendModeEnum.Mix,
        };
        for (int i = 0; i < PuffsPerGust * 2; i++)
        {
            var m = new MeshInstance3D { Mesh = new QuadMesh { Size = new Vector2(1, 1) }, MaterialOverride = mat, Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            AddChild(m); _panels.Add(m);
        }
    }

    /// <summary>A soft, wispy puff: a long ellipse that thins to nothing along both axes (no hard edge anywhere), broken up a little by low-frequency noise.</summary>
    static ImageTexture MakeTexture()
    {
        const int W = 128, H = 48;
        var img = Image.CreateEmpty(W, H, false, Image.Format.Rgba8);
        var rng = new RandomNumberGenerator { Seed = 1917 };
        var noise = new float[W / 12 + 2, H / 12 + 2];
        for (int x = 0; x < noise.GetLength(0); x++) for (int y = 0; y < noise.GetLength(1); y++) noise[x, y] = rng.Randf();
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float u = (x + 0.5f) / W * 2f - 1f, v = (y + 0.5f) / H * 2f - 1f;
                float body = MathF.Exp(-(u * u * 2.6f + v * v * 3.4f));      // a gaussian blob: soft on every side
                float fx = x / 12f, fy = y / 12f; int ix = (int)fx, iy = (int)fy; float tx = fx - ix, ty = fy - iy;
                float nz = Mathf.Lerp(Mathf.Lerp(noise[ix, iy], noise[ix + 1, iy], tx), Mathf.Lerp(noise[ix, iy + 1], noise[ix + 1, iy + 1], tx), ty);
                float a = Math.Clamp(body * (0.55f + 0.7f * nz) - 0.03f, 0f, 1f);
                img.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        return ImageTexture.CreateFromImage(img);
    }

    // ------------------------------------------------------------ the schedule: a pure function of the clock

    static float Hash(int k) { uint h = (uint)k * 2654435761u; h ^= h >> 15; h *= 2246822519u; h ^= h >> 13; h *= 3266489917u; h ^= h >> 16; return (h & 0xFFFF) / 65535f; }

    /// <summary>The start of gust k (seconds on the clock).</summary>
    public static float GustStart(int k) => First + k * Period + (Hash(k) - 0.5f) * 2f * Jitter;

    /// <summary>One puff of a gust: everything about it is a function of (gust, index).</summary>
    public readonly record struct Puff(int Gust, int Index, float Age, float Life, float Speed, float Lateral, float Length, float Width, float Height, float Density, float Phase, float Yaw);

    /// <summary>Puff i of gust k at the clock; null while it is not alive. Pure.</summary>
    public static Puff? PuffAt(float clock, int k, int i)
    {
        int seed = k * 977 + i * 61;
        float h1 = Hash(seed + 1), h2 = Hash(seed + 2), h3 = Hash(seed + 3), h4 = Hash(seed + 4), h5 = Hash(seed + 5), h6 = Hash(seed + 6), h7 = Hash(seed + 7), h8 = Hash(seed + 8);
        float strength = 0.62f + 0.38f * Hash(k * 7919 + 3);      // some gusts are lighter than others
        float birth = GustStart(k) + BirthSpan * MathF.Pow(h1, 1.7f);      // bunched at the gust's front, a few late stragglers
        float life = 3.2f + (MaxLife - 3.2f) * h2;
        float age = clock - birth;
        if (age < 0f || age >= life) return null;
        float speed = 2.2f + 2.9f * h3 + 0.25f * (birth - GustStart(k));      // the later ones run a little faster: the gust spreads and thins
        float lateral = (h4 + h5 - 1f) * 8.5f;      // along the road, mostly near the middle of the gust
        float length = 1.8f + 4.4f * h6 * h6 + 1.2f * h7;
        float width = 0.9f + 1.7f * h7;      // soft patches, not strokes
        float height = 0.3f + 0.45f * h8;
        float density = (0.3f + 0.7f * h8 * h3) * strength;
        return new Puff(k, i, age, life, speed, lateral, length, width, height, density, h5 * MathF.Tau, (h8 - 0.5f) * 0.9f);      // each puff a little off the wind's line: no set of parallel strokes
    }

    /// <summary>The puffs alive at the clock (a gust never reaches into the next one: Period is longer than GustSpan). Pure: the same answer for the same clock, whatever came before.</summary>
    public static IEnumerable<Puff> Alive(float clock)
    {
        if (clock < 0f) yield break;
        int k0 = Math.Max(0, (int)MathF.Floor((clock - First - Jitter - GustSpan) / Period));
        for (int k = k0; GustStart(k) - Jitter <= clock + 0.01f && k < k0 + 6; k++)
            for (int i = 0; i < PuffsPerGust; i++)
                if (PuffAt(clock, k, i) is { } p) yield return p;
    }

    /// <summary>The picture at this clock (seconds): every panel is placed, faded or hidden from the clock alone.</summary>
    public void Update(float clock, Color light)
    {
        _material.AlbedoColor = new Color(DustColor.R * light.R, DustColor.G * light.G, DustColor.B * light.B);
        int used = 0;
        if (Enabled && clock >= 0f)
        {
            Vector2 road = _t.CenterDir(RidgeIndex);
            foreach (var p in Alive(clock))
            {
                if (used + 1 >= _panels.Count) break;
                float u = p.Age / p.Life;
                float env = Smooth(u / 0.16f) * (1f - Smooth((u - 0.42f) / 0.58f));      // in quickly, out slowly: the puff thins as it goes
                float wobble = MathF.Sin(p.Age * 1.3f + p.Phase) * 0.5f;
                var at = _origin + _wind * (p.Speed * p.Age) + road * (p.Lateral + wobble);
                float alpha = Math.Clamp(env * p.Density * 0.7f * (1f - MudShare(at)), 0f, 1f);
                float gy = _t.Sample(at.X, at.Y).Height;
                float length = p.Length * (1f + 1.1f * u), width = p.Width * (1f + 1.2f * u);
                PlacePanel(_panels[used++], at, gy, length, p.Height * (0.8f + 0.9f * u), alpha * 0.18f, true, width, p.Yaw);      // (the upright panel only a little: seen edge-on it is a stroke)
                PlacePanel(_panels[used++], at, gy, length, width, alpha * 0.95f, false, width, p.Yaw);
            }
        }
        for (int i = used; i < _panels.Count; i++) _panels[i].Visible = false;
    }

    static float Smooth(float x) { x = Math.Clamp(x, 0f, 1f); return x * x * (3f - 2f * x); }

    void PlacePanel(MeshInstance3D m, Vector2 at, float groundY, float length, float breadth, float alpha, bool vertical, float width = 0f, float yawJitter = 0f)
    {
        if (alpha < 0.01f) { m.Visible = false; return; }
        m.Visible = true;
        m.Transparency = 1f - alpha;
        float yaw = MathF.Atan2(-_wind.Y, _wind.X) + yawJitter;      // the quad's local X along the wind (a puff a little to either side of it)
        var basis = new Basis(Vector3.Up, yaw);
        if (vertical) { m.Scale = Vector3.One; m.GlobalTransform = new Transform3D(basis.Scaled(new Vector3(length, breadth, 1f)), new Vector3(at.X, groundY + 0.12f + breadth * 0.5f, at.Y)); }
        else { var flatBasis = basis * new Basis(Vector3.Right, -MathF.PI / 2f); m.GlobalTransform = new Transform3D(flatBasis.Scaled(new Vector3(length, breadth, 1f)), new Vector3(at.X, groundY + 0.16f, at.Y)); }
    }

    public float MudShareAt(Vector2 at) => MudShare(at);
    // read-outs for the tests
    public int VisiblePanels { get { int n = 0; foreach (var m in _panels) if (m.Visible) n++; return n; } }
    public Transform3D FirstPanelPose { get { foreach (var m in _panels) if (m.Visible) return m.GlobalTransform; return default; } }

    /// <summary>0 on dry ground, 1 on mud (sampled along the panel): the dust stops where the mud begins.</summary>
    float MudShare(Vector2 at)
    {
        int mud = 0;
        for (int i = -1; i <= 1; i++)
        {
            var p = at + _wind * (i * 1.8f);
            if (_t.Sample(p.X, p.Y).Surface == SurfaceType.Mud) mud++;
        }
        return mud / 3f;
    }
}
