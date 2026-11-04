using System;
using Godot;

namespace CCCPRacing.UI;

/// <summary>
/// Summer on the lens (Ilyich's Lamp, driven by RaceScene.LensFx): a poplar tuft stuck on the glass,
/// a faint film of dust at the edges, a fly walking about or a bug splatted, a few straws stuck, and
/// the low sun's flare. All small, mostly rare, drawn under the HUD panels and away from the car.
/// </summary>
public partial class Hud
{
    /// <summary>Dust film 0..1 (0.18 opacity at the edges when full), flare strength 0..1 with the sun's screen direction.</summary>
    public float LensDust, LensFlare;
    public Vector2 LensFlareDir = new(-1, 0);

    readonly RandomNumberGenerator _lr = new() { Seed = 1957 };
    float _fluffT = 99, _fluffD, _flyT = 99, _flyD, _bugT = 99;
    Vector2 _fluffAt, _flyAt, _flyTo, _bugAt;
    int _fluffSeed;
    readonly float[] _strawT = new float[3] { 99, 99, 99 };
    readonly float[] _strawD = new float[3];
    readonly Vector2[] _strawAt = new Vector2[3];
    readonly float[] _strawAng = new float[3];
    readonly float[] _strawLen = new float[3];

    /// <summary>Nothing on the glass (a new run).</summary>
    public void LensReset()
    {
        LensDust = LensFlare = 0;
        _fluffT = _flyT = _bugT = 99;
        for (int i = 0; i < 3; i++) _strawT[i] = 99;
    }

    /// <summary>Something is on the lens now (the draw is worth doing).</summary>
    bool LensAny => LensDust > 0.01f || LensFlare > 0.01f || _fluffT < _fluffD || _flyT < _flyD || _bugT < BugLife
        || _strawT[0] < _strawD[0] || _strawT[1] < _strawD[1] || _strawT[2] < _strawD[2];

    const float BugLife = 3.2f;

    /// <summary>A spot near a screen edge, clear of the middle where the car is.</summary>
    Vector2 EdgeSpot()
    {
        var s = Screen;
        // The side bands between the HUD panels (lap panel top left, map bottom left, gauges bottom right).
        bool left = _lr.Randf() < 0.5f;
        float x = left ? _lr.RandfRange(10, 64) : _lr.RandfRange(s.X - 64, s.X - 10);
        float y = left ? _lr.RandfRange(62, s.Y - 78) : _lr.RandfRange(28, s.Y - 54);
        return new Vector2(MathF.Floor(x), MathF.Floor(y));
    }

    /// <summary>A poplar tuft sticks to the glass for this many seconds (the wind of speed blows it off sooner).</summary>
    public void LensFluff(float duration) { _fluffT = 0; _fluffD = duration; _fluffAt = EdgeSpot(); _fluffSeed++; }

    /// <summary>A fly lands near an edge and walks a little.</summary>
    public void LensFly(float duration)
    {
        _flyT = 0; _flyD = duration; _flyAt = EdgeSpot();
        _flyTo = _flyAt + Vector2.FromAngle(_lr.RandfRange(0, MathF.Tau)) * _lr.RandfRange(10, 22);
    }

    /// <summary>A bug is splatted.</summary>
    public void LensBug() { _bugT = 0; _bugAt = EdgeSpot(); }

    /// <summary>Straws stick to the glass.</summary>
    public void LensStraw(int count, float duration)
    {
        for (int i = 0, n = 0; i < 3 && n < count; i++)
            if (_strawT[i] >= _strawD[i])
            {
                n++;
                _strawT[i] = 0; _strawD[i] = duration * _lr.RandfRange(0.8f, 1.3f);
                _strawAt[i] = EdgeSpot() + new Vector2(_lr.RandfRange(-14, 14), _lr.RandfRange(-14, 14));
                _strawAng[i] = _lr.RandfRange(-1.2f, 1.2f); _strawLen[i] = _lr.RandfRange(6, 10);
            }
    }

    /// <summary>The lens's clocks. kmh blows the tuft off sooner.</summary>
    public void LensAdvance(float dt, float kmh)
    {
        if (_fluffT < _fluffD) _fluffT += dt * (1f + Math.Clamp(kmh, 0, 150) / 45f);
        if (_flyT < _flyD) _flyT += dt;
        if (_bugT < BugLife) _bugT += dt;
        for (int i = 0; i < 3; i++) if (_strawT[i] < _strawD[i]) _strawT[i] += dt;
        _lensTremble += dt;
    }
    float _lensTremble;

    static uint Hash(uint x)
    {
        x ^= x >> 16; x *= 0x7feb352d; x ^= x >> 15; x *= 0x846ca68b; x ^= x >> 16;
        return x;
    }
    static float Hash01(uint x) => (Hash(x) & 0xffff) / 65535f;

    void LensPx(float x, float y, Color c, float w = 1, float h = 1) => DrawRect(new Rect2(MathF.Floor(x), MathF.Floor(y), w, h), c);

    void LensDisc(Vector2 c, float r, Color col, float step = 2f)
    {
        for (float y = -r; y < r; y += step)
        {
            float w = MathF.Sqrt(Math.Max(0, r * r - y * y));
            DrawRect(new Rect2(MathF.Floor(c.X - w), MathF.Floor(c.Y + y), MathF.Ceiling(w * 2), step), col);
        }
    }

    void DrawLensFx()
    {
        if (!LensAny) return;
        if (LensFlare > 0.01f) DrawFlare();
        if (LensDust > 0.01f) DrawDust();
        if (_fluffT < _fluffD) DrawFluff();
        if (_flyT < _flyD) DrawFly();
        if (_bugT < BugLife) DrawBug();
        for (int i = 0; i < 3; i++) if (_strawT[i] < _strawD[i]) DrawStraw(i);
    }

    // ------------------------------------------------------------ dust

    static readonly float[] DustBands = { 1f, 0.7f, 0.45f, 0.24f, 0.1f };

    void DrawDust()
    {
        var s = Screen;
        const float edge = 0.18f, band = 8f;
        for (int i = 0; i < DustBands.Length; i++)
        {
            var c = new Color(0.8f, 0.72f, 0.6f, edge * DustBands[i] * LensDust);
            float o = i * band;
            DrawRect(new Rect2(o, o, s.X - 2 * o, band), c);
            DrawRect(new Rect2(o, s.Y - o - band, s.X - 2 * o, band), c);
            DrawRect(new Rect2(o, o + band, band, s.Y - 2 * o - 2 * band), c);
            DrawRect(new Rect2(s.X - o - band, o + band, band, s.Y - 2 * o - 2 * band), c);
        }
        // The corners gather more (nested squares, at most about 18% + a bit more).
        for (int k = 0; k < 3; k++)
        {
            var c = new Color(0.78f, 0.68f, 0.55f, 0.025f * LensDust);
            float z = 14 + k * 12;
            DrawRect(new Rect2(0, 0, z, z), c);
            DrawRect(new Rect2(s.X - z, 0, z, z), c);
            DrawRect(new Rect2(0, s.Y - z, z, z), c);
            DrawRect(new Rect2(s.X - z, s.Y - z, z, z), c);
        }
        // Specks of grit, more of them as it builds.
        int n = (int)(LensDust * 26);
        for (int k = 0; k < n; k++)
        {
            float along = Hash01((uint)k * 7 + 1), depth = Hash01((uint)k * 7 + 2) * 40f;
            float x, y;
            switch (k & 3)
            {
                case 0: x = along * s.X; y = depth; break;
                case 1: x = along * s.X; y = s.Y - depth - 1; break;
                case 2: x = depth; y = along * s.Y; break;
                default: x = s.X - depth - 1; y = along * s.Y; break;
            }
            bool big = Hash((uint)k * 13u + 5) % 5 == 0;
            LensPx(x, y, new Color(0.52f, 0.46f, 0.38f, 0.4f), big ? 2 : 1, 1);
        }
    }

    // ------------------------------------------------------------ poplar tuft

    void DrawFluff()
    {
        float t = _fluffT, d = _fluffD;
        float a = Math.Clamp(t / 0.25f, 0f, 1f) * Math.Clamp((d - t) / 0.35f, 0f, 1f);
        float away = t > d - 0.35f ? (t - (d - 0.35f)) / 0.35f : 0f;                 // blown off sideways at the end
        uint frame = (uint)(_lensTremble * 14f);
        float shake = 0.7f + (d - t < 1f ? 1f : 0f);
        int jx = (int)MathF.Round((Hash01(frame * 3 + 1) - 0.5f) * 2 * shake), jy = (int)MathF.Round((Hash01(frame * 3 + 2) - 0.5f) * 2 * shake);
        float side = _fluffAt.X < Screen.X / 2 ? -1 : 1;
        Vector2 p = _fluffAt + new Vector2(jx + side * away * away * 60f, jy - away * 12f);
        uint seed = (uint)_fluffSeed * 977u;
        for (int y = -6; y <= 6; y++)
            for (int x = -6; x <= 6; x++)
            {
                float r = MathF.Sqrt(x * x + y * y * 1.25f);
                float rag = Hash01(seed + (uint)((x + 8) * 31 + (y + 8)));
                float al;
                if (r < 2.5f) al = 0.8f;
                else if (r < 4f) al = rag > 0.2f ? 0.5f : 0f;
                else if (r < 5.6f) al = rag > 0.6f ? 0.28f : 0f;
                else al = 0;
                if (al <= 0) continue;
                LensPx(p.X + x, p.Y + y, new Color(1f, 1f, 0.97f, al * a));
            }
        // a few loose fibres
        for (int f = 0; f < 5; f++)
        {
            float ang = Hash01(seed + 900u + (uint)f) * MathF.Tau;
            var q = p + Vector2.FromAngle(ang) * (6 + Hash01(seed + 950u + (uint)f) * 3);
            LensPx(q.X, q.Y, new Color(1f, 1f, 0.95f, 0.45f * a));
        }
    }

    // ------------------------------------------------------------ fly

    void DrawFly()
    {
        float t = _flyT, d = _flyD;
        float land = 0.25f, takeoff = d - 0.45f;
        Vector2 p;
        if (t < land) p = _flyAt - Vector2.FromAngle(0) * 0 + new Vector2(0, (1f - t / land) * -14f);       // drops in from above
        else if (t < takeoff)
        {
            // walks in short stretches with pauses
            float w = (t - land) / (takeoff - land);
            float steps = MathF.Floor(w * 5f), frac = w * 5f - steps;
            float prog = (steps + Mathf.SmoothStep(0.3f, 0.9f, frac)) / 5f;
            p = _flyAt.Lerp(_flyTo, prog);
        }
        else
        {
            float k = (t - takeoff) / 0.45f;
            var dir = (_flyTo - _flyAt).Normalized();
            p = _flyTo + (dir * 0.6f + new Vector2(0, -0.8f)).Normalized() * k * k * 70f;
        }
        bool flying = t < land || t >= takeoff;
        var dark = new Color(0.1f, 0.08f, 0.07f);
        var wing = new Color(0.82f, 0.85f, 0.9f, flying ? 0.35f : 0.55f);
        int px = (int)MathF.Floor(p.X), py = (int)MathF.Floor(p.Y);
        bool flap = (int)(t * 30) % 2 == 0;
        // body 2x3, head 1, wings, legs
        LensPx(px, py, dark, 2, 3);
        LensPx(px, py - 1, new Color(0.3f, 0.18f, 0.14f), 2, 1);
        LensPx(px - (flap || flying ? 2 : 1), py, wing, 2, 2);
        LensPx(px + 2, py, wing, 2, 2);
        if (!flying)
        {
            bool alt = (int)(t * 8) % 2 == 0;
            var leg = new Color(0.12f, 0.1f, 0.09f, 0.9f);
            LensPx(px - 1, py + (alt ? 0 : 1), leg); LensPx(px + 2, py + (alt ? 1 : 0), leg);
            LensPx(px - 1, py + 2, leg); LensPx(px + 2, py + 2, leg);
        }
    }

    // ------------------------------------------------------------ bug

    void DrawBug()
    {
        float t = _bugT;
        float a = t < 1.2f ? 1f : Math.Clamp(1f - (t - 1.2f) / (BugLife - 1.2f), 0f, 1f);
        float p = MathF.Floor(_bugAt.X), q = MathF.Floor(_bugAt.Y);
        var halo = new Color(0.66f, 0.7f, 0.22f, 0.45f * a);
        var smear = new Color(0.7f, 0.76f, 0.24f, 0.8f * a);
        var core = new Color(0.9f, 0.88f, 0.4f, 0.9f * a);
        var dark = new Color(0.32f, 0.27f, 0.08f, 0.95f * a);
        LensPx(p - 3, q - 1, halo, 8, 4);
        LensPx(p - 2, q - 2, smear, 6, 5);
        LensPx(p - 1, q - 1, core, 4, 3);
        LensPx(p - 1, q, dark, 2, 1);
        LensPx(p + 4, q - 3, smear, 1, 1);
        LensPx(p - 4, q + 2, smear, 1, 1);
        LensPx(p + 3, q + 3, halo, 2, 1);
        // a slow drip of the smear
        float drip = Math.Min(7f, t * 3f);
        LensPx(p, q + 3, new Color(0.62f, 0.68f, 0.2f, 0.6f * a), 1, MathF.Floor(drip));
    }

    // ------------------------------------------------------------ straw

    void DrawStraw(int i)
    {
        float t = _strawT[i], d = _strawD[i];
        float a = Math.Clamp(t / 0.1f, 0f, 1f) * Math.Clamp((d - t) / 0.4f, 0f, 1f);
        var dir = Vector2.FromAngle(_strawAng[i]);
        var a0 = _strawAt[i].Floor();
        var b0 = (_strawAt[i] + dir * _strawLen[i]).Floor();
        DrawLine(a0 + new Vector2(0, 1), b0 + new Vector2(0, 1), new Color(0.42f, 0.3f, 0.08f, 0.8f * a), 1f);
        DrawLine(a0, b0, new Color(0.95f, 0.8f, 0.34f, a), 1f);
    }

    // ------------------------------------------------------------ low sun

    void DrawFlare()
    {
        var s = Screen;
        var c = s / 2;
        var dir = LensFlareDir.Normalized();
        float tx = MathF.Abs(dir.X) < 0.001f ? 1e6f : (s.X / 2) / MathF.Abs(dir.X);
        float ty = MathF.Abs(dir.Y) < 0.001f ? 1e6f : (s.Y / 2) / MathF.Abs(dir.Y);
        var sun = c + dir * MathF.Min(tx, ty);
        float f = LensFlare;
        // the glow from the edge: four stepped discs, 0.12 at the very core
        LensDisc(sun, 84, new Color(1f, 0.82f, 0.45f, 0.03f * f), 3);
        LensDisc(sun, 60, new Color(1f, 0.8f, 0.42f, 0.03f * f), 3);
        LensDisc(sun, 38, new Color(1f, 0.82f, 0.5f, 0.03f * f), 2);
        LensDisc(sun, 20, new Color(1f, 0.9f, 0.65f, 0.03f * f), 2);
        // ghosts along the line through the middle
        LensDisc(sun + (c - sun) * 0.62f, 9, new Color(1f, 0.78f, 0.4f, 0.05f * f), 2);
        LensDisc(sun + (c - sun) * 1.45f, 5, new Color(0.9f, 0.85f, 0.6f, 0.06f * f), 1);
        LensDisc(sun + (c - sun) * 1.8f, 13, new Color(1f, 0.7f, 0.35f, 0.04f * f), 2);
        LensDisc(sun + (c - sun) * 1.8f, 7, new Color(1f, 0.72f, 0.38f, 0.03f * f), 2);
    }
}
