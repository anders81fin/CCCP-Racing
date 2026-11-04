using System;
using System.Collections.Generic;
using Godot;

namespace CCCPRacing.Track;

/// <summary>
/// Small signs of life in the yards of the Ilyich village: washing on lines beside three izbas by the road
/// swaying in the wind, a hen house with a flock that flees the car, and a tethered donkey that
/// looks up when a car passes and kicks at a fast one. Decoration only: nothing here is an obstacle
/// (all of it stands well clear of the road, the lane and the house access paths). Every spot is given
/// in the house's own frame (+X along the house front, +Z out to the lane, -Z the back yard).
/// </summary>
public partial class VillageLife : Node3D
{
    /// <summary>Everything here at the people's size (the workers and guards are 1.6 of a real person).</summary>
    const float Size = 1.6f;
    // Houses (by position) nearest the race road, with a washing line in a side yard (side -1/+1 in the
    // house frame; not where the hens or the donkey are), running from beside the back corner out
    // towards the road: 6-13 m from its edge, seen across as the car passes.
    static readonly (float x, float z, float side)[] LineHouses = { (15, 240, -1), (15, 150, -1), (60, 120, 1) };
    const float LineX = 6.8f, LineZ0 = -1.5f, LineZ1 = 8f, PostHeight = 2.1f * Size;

    // The hens' yard: the front corner of the izba at 15,150 (house frame), the coop in the middle.
    static readonly (float x, float z) HenHouseAt = (15, 150);
    static readonly Vector2 CoopAt = new(8f, 10f);
    static readonly Vector2 YardMin = new(5.2f, 7.4f), YardMax = new(10.8f, 12.6f);
    const float CoopClear = 1.25f * Size, HenFleeDist = 9f;
    /// <summary>The coop's collision circle (m): its box and roof are about 2.6 x 2.1 m at this size.</summary>
    const float CoopObstacleRadius = 1.35f;

    // The donkey: left front corner of the izba at 15,118; peg at the corner, grazing along the front.
    static readonly (float x, float z) DonkeyHouseAt = (15, 118);
    static readonly Vector2 PegAt = new(-3.9f, 3.3f), DonkeyAt = new(-5.3f, 6.4f);
    const float DonkeyAlertDist = 12f, KickSpeed = 40f / 3.6f, CullDist = 90f;

    TrackData _t;
    float _clock, _speed, _wind;
    Vector3 _prevCar;
    bool _prevValid;
    Camera3D _cam;

    readonly List<(Node3D Pivot, float Rot, float Amp, float Rate, float Phase, float Lean)> _clothes = new();
    readonly List<Vector3> _lineCentres = new();
    readonly List<Hen> _hens = new();
    Vector3 _yardCentre;
    PropInstance _henHouse;
    Donkey _donkey;

    static PropInstance House(TrackData t, (float x, float z, float side) at) => House(t, (at.x, at.z));
    static PropInstance House(TrackData t, (float x, float z) at) =>
        t.Props.Find(p => p.Type == "izba" && MathF.Abs(p.Position.X - at.x) < 0.5f && MathF.Abs(p.Position.Z - at.z) < 0.5f);

    /// <summary>House-frame point to world (X, Z).</summary>
    static Vector2 W(PropInstance h, float lx, float lz)
    {
        Vector3 o = new Basis(Vector3.Up, h.Rotation) * new Vector3(lx, 0, lz);
        return new Vector2(h.Position.X + o.X, h.Position.Z + o.Z);
    }

    static Vector2 W(PropInstance h, Vector2 l) => W(h, l.X, l.Y);

    /// <summary>Spots the trees and bushes must keep off (the track builder's occupied list).</summary>
    public static void Reserve(TrackData t, List<Vector2> occupied)
    {
        if (t.Id != "ilyich") return;
        foreach (var at in LineHouses)
        {
            var h = House(t, at);
            if (h == null) continue;
            for (float z = LineZ0 - 0.5f; z <= LineZ1 + 0.5f; z += 1.5f) occupied.Add(W(h, at.side * LineX, z));
        }
        var hh = House(t, HenHouseAt);
        if (hh != null)
            for (float x = YardMin.X; x <= YardMax.X; x += 1.8f)
                for (float z = YardMin.Y; z <= YardMax.Y; z += 1.8f) occupied.Add(W(hh, x, z));
        var dh = House(t, DonkeyHouseAt);
        if (dh != null) { occupied.Add(W(dh, PegAt)); occupied.Add(W(dh, DonkeyAt)); occupied.Add(W(dh, DonkeyAt.X - 1f, DonkeyAt.Y)); }
    }

    public static VillageLife Build(TrackData t)
    {
        var root = new VillageLife { Name = "VillageLife", _t = t };
        if (t.Id != "ilyich") { root.SetProcess(false); return root; }
        foreach (var at in LineHouses)
        {
            var h = House(t, at);
            if (h != null) root.BuildWashingLine(h, at.side, root._lineCentres.Count);
        }
        var hh = House(t, HenHouseAt);
        if (hh != null) root.BuildHenYard(hh);
        var dh = House(t, DonkeyHouseAt);
        if (dh != null) root.BuildDonkey(dh);
        return root;
    }

    // ---------------------------------------------------------------- helpers

    float Ground(Vector2 p) => _t.MeshHeight(p.X, p.Y);

    static Node3D Batched(Action<Node3D> fill)
    {
        var c = new Node3D();
        fill(c);
        DrunkTractor.BatchStatic(c, null);
        return c;
    }

    static MeshInstance3D P(Mesh mesh, Color c, Vector3 pos, Vector3? rotDeg = null, Vector3? scale = null)
    {
        var m = Props.Part(mesh, c, pos, rotDeg);
        if (scale.HasValue) m.Scale = scale.Value;
        return m;
    }

    /// <summary>A thin straight bar between two points.</summary>
    static MeshInstance3D Bar(Vector3 a, Vector3 b, float thick, Color c)
    {
        Vector3 d = b - a;
        var m = Props.Part(Props.Box(thick, thick, d.Length()), c, Vector3.Zero);
        m.Transform = new Transform3D(Basis.LookingAt(d.Normalized(), MathF.Abs(d.Normalized().Y) > 0.99f ? Vector3.Right : Vector3.Up), (a + b) * 0.5f);
        return m;
    }

    bool Near(Vector3 p) => _cam == null || p.DistanceSquaredTo(_cam.GlobalPosition) < CullDist * CullDist;

    // ---------------------------------------------------------------- washing lines

    static readonly Color Wood = new(0.36f, 0.26f, 0.17f);
    static readonly Color Rope = new(0.78f, 0.74f, 0.64f);

    void BuildWashingLine(PropInstance h, float side, int index)
    {
        var a = W(h, side * LineX, LineZ0); var b = W(h, side * LineX, LineZ1);
        float yaw = MathF.Atan2(-(b - a).Y, (b - a).X);      // the clothes' plane along the line
        float ya = Ground(a) + PostHeight, yb = Ground(b) + PostHeight;
        var ta = new Vector3(a.X, ya, a.Y); var tb = new Vector3(b.X, yb, b.Y);
        const float Sag = 0.28f * Size;
        Vector3 Line(float u) => ta.Lerp(tb, u) + Vector3.Down * (4f * Sag * u * (1 - u));
        var fixedParts = Batched(n =>
        {
            foreach (var (p, g) in new[] { (ta, Ground(a)), (tb, Ground(b)) })
            {
                n.AddChild(Bar(new Vector3(p.X, g, p.Z), p, 0.09f * Size, Wood));
                n.AddChild(P(Props.Box(0.5f * Size, 0.07f * Size, 0.07f * Size), Wood, p - Vector3.Up * 0.12f * Size, new Vector3(0, Mathf.RadToDeg(h.Rotation), 0)));
            }
            for (int i = 0; i < 8; i++) n.AddChild(Bar(Line(i / 8f), Line((i + 1) / 8f), 0.014f * Size, Rope));
        });
        AddChild(fixedParts);

        // The washing: shirts, a sheet, trousers, wool socks and a red headscarf, spread along the line.
        var kinds = index switch
        {
            0 => new[] { 'S', 'T', 'H', 'S', 'W', 'R', 'S' },
            1 => new[] { 'H', 'S', 'T', 'W', 'T', 'R', 'S', 'S' },
            _ => new[] { 'S', 'W', 'T', 'H', 'R', 'S' },
        };
        float len = LineZ1 - LineZ0;
        float total = 0; foreach (var k in kinds) total += ClothWidth(k) * Size;
        float gap = MathF.Max(0.08f, (len - 0.9f - total) / (kinds.Length - 1));
        float cursor = 0.45f;
        var rng = new RandomNumberGenerator { Seed = (ulong)(7000 + index * 31) };
        foreach (var k in kinds)
        {
            float w = ClothWidth(k) * Size, u = (cursor + w * 0.5f) / len;
            cursor += w + gap;
            var pivot = new Node3D { Position = Line(u), Rotation = new Vector3(0, yaw, 0), Scale = Vector3.One * Size };
            pivot.AddChild(Batched(n => BuildCloth(n, k, rng)));
            AddChild(pivot);
            _clothes.Add((pivot, yaw, rng.RandfRange(0.07f, 0.16f), rng.RandfRange(1.7f, 3.1f), rng.RandfRange(0, MathF.Tau), rng.RandfRange(0.04f, 0.1f)));
        }
        _lineCentres.Add(Line(0.5f));
    }

    static float ClothWidth(char k) => k switch { 'W' => 1.3f, 'S' => 0.7f, 'T' => 0.4f, 'H' => 0.4f, _ => 0.28f };

    static readonly Color[] ShirtColours =
    {
        new(0.86f, 0.84f, 0.78f), new(0.34f, 0.46f, 0.66f), new(0.62f, 0.64f, 0.5f), new(0.8f, 0.72f, 0.5f),
    };

    static void BuildCloth(Node3D n, char kind, RandomNumberGenerator rng)
    {
        switch (kind)
        {
            case 'S':      // shirt, arms hanging
            {
                var c = ShirtColours[rng.RandiRange(0, ShirtColours.Length - 1)];
                n.AddChild(P(Props.Box(0.5f, 0.64f, 0.03f), c, new Vector3(0, -0.32f, 0)));
                foreach (float s in new[] { -1f, 1f })
                    n.AddChild(P(Props.Box(0.12f, 0.36f, 0.03f), c.Darkened(0.06f), new Vector3(s * 0.31f, -0.2f, 0), new Vector3(0, 0, s * 12f)));
                break;
            }
            case 'W':      // sheet
                n.AddChild(P(Props.Box(1.3f, 0.95f, 0.02f), new Color(0.9f, 0.88f, 0.82f), new Vector3(0, -0.475f, 0)));
                n.AddChild(P(Props.Box(1.3f, 0.06f, 0.025f), new Color(0.7f, 0.74f, 0.82f), new Vector3(0, -0.9f, 0)));
                break;
            case 'T':      // trousers, hung by the waist
            {
                var c = rng.Randf() < 0.5f ? new Color(0.24f, 0.28f, 0.38f) : new Color(0.3f, 0.29f, 0.26f);
                n.AddChild(P(Props.Box(0.4f, 0.12f, 0.03f), c, new Vector3(0, -0.06f, 0)));
                foreach (float s in new[] { -1f, 1f })
                    n.AddChild(P(Props.Box(0.18f, 0.78f, 0.03f), c.Darkened(0.05f), new Vector3(s * 0.1f, -0.5f, 0)));
                break;
            }
            case 'H':      // red headscarf, a diamond hung by its corner
                n.AddChild(P(Props.Box(0.3f, 0.3f, 0.02f), new Color(0.78f, 0.1f, 0.09f), new Vector3(0, -0.21f, 0), new Vector3(0, 0, 45)));
                break;
            default:       // a pair of wool socks
                foreach (float s in new[] { -1f, 1f })
                {
                    n.AddChild(P(Props.Box(0.09f, 0.28f, 0.035f), new Color(0.7f, 0.66f, 0.58f), new Vector3(s * 0.07f, -0.14f, 0)));
                    n.AddChild(P(Props.Box(0.095f, 0.05f, 0.04f), new Color(0.55f, 0.2f, 0.16f), new Vector3(s * 0.07f, -0.04f, 0)));
                    n.AddChild(P(Props.Box(0.09f, 0.08f, 0.04f), new Color(0.42f, 0.36f, 0.3f), new Vector3(s * 0.07f, -0.3f, 0.02f)));
                }
                break;
        }
    }

    // ---------------------------------------------------------------- hens

    sealed class Hen
    {
        public Node3D Root, Tilt, Head, WingL, WingR;
        public Vector2 Pos, Target, FleeDir;
        public float Heading, Timer, Phase, Hop;
        public int Mode;     // 0 pecking about, 1 strolling, 2 fleeing, 3 scared, standing
    }

    void BuildHenYard(PropInstance h)
    {
        _henHouse = h;
        // The coop: a plank box on short legs with a gabled tin roof, a ramp to its door, a nesting box.
        Vector2 cw = W(h, CoopAt);
        var coop = Batched(n =>
        {
            var plank = new Color(0.55f, 0.4f, 0.25f); var dark = new Color(0.3f, 0.2f, 0.12f);
            foreach (float sx in new[] { -1f, 1f }) foreach (float sz in new[] { -1f, 1f })
                n.AddChild(P(Props.Box(0.1f, 0.45f, 0.1f), dark, new Vector3(sx * 0.55f, 0.225f, sz * 0.4f)));
            n.AddChild(P(Props.Box(1.3f, 0.8f, 1.0f), plank, new Vector3(0, 0.85f, 0)));
            for (float x = -0.5f; x <= 0.51f; x += 0.25f) n.AddChild(P(Props.Box(0.04f, 0.8f, 1.03f), dark, new Vector3(x, 0.85f, 0)));
            n.AddChild(P(new PrismMesh { Size = new Vector3(1.6f, 0.45f, 1.3f) }, new Color(0.35f, 0.38f, 0.4f), new Vector3(0, 1.475f, 0)));
            n.AddChild(P(Props.Box(0.34f, 0.36f, 0.05f), new Color(0.07f, 0.05f, 0.04f), new Vector3(0.3f, 0.72f, 0.5f)));
            n.AddChild(P(Props.Box(0.32f, 0.04f, 1.0f), dark.Lightened(0.15f), new Vector3(0.3f, 0.25f, 0.95f), new Vector3(26.6f, 0, 0)));
            for (float z = 0.7f; z < 1.3f; z += 0.2f)
                n.AddChild(P(Props.Box(0.32f, 0.03f, 0.04f), dark, new Vector3(0.3f, 0.24f - (z - 0.7f) * 0.5f + 0.18f, z), new Vector3(26.6f, 0, 0)));
            n.AddChild(P(Props.Box(0.35f, 0.3f, 0.8f), plank.Darkened(0.1f), new Vector3(-0.85f, 0.95f, 0)));
            n.AddChild(P(Props.Box(0.42f, 0.04f, 0.9f), dark, new Vector3(-0.85f, 1.12f, 0), new Vector3(0, 0, -8)));
        });
        _t.Obstacles.Add(new Obstacle(cw, CoopObstacleRadius));      // solid: the car cannot drive through the coop (the hens flee round it)
        coop.Position = new Vector3(cw.X, Ground(cw), cw.Y);
        coop.Rotation = new Vector3(0, h.Rotation, 0);
        coop.Scale = Vector3.One * Size;
        AddChild(coop);

        var rng = new RandomNumberGenerator { Seed = 5150 };
        Color[] colours =
        {
            new(0.9f, 0.88f, 0.82f), new(0.55f, 0.3f, 0.16f), new(0.86f, 0.84f, 0.78f), new(0.32f, 0.23f, 0.17f),
            new(0.68f, 0.4f, 0.2f), new(0.92f, 0.9f, 0.86f), new(0.5f, 0.28f, 0.15f),
        };
        for (int i = 0; i < 7; i++)
        {
            var hen = MakeHen(colours[i], rng.Randf() * 10f);
            for (int tries = 0; tries < 20; tries++)
            {
                hen.Pos = new Vector2(rng.RandfRange(YardMin.X, YardMax.X), rng.RandfRange(YardMin.Y, YardMax.Y));
                if (hen.Pos.DistanceTo(CoopAt) > CoopClear + 0.2f) break;
            }
            hen.Target = hen.Pos;
            hen.Heading = rng.RandfRange(0, MathF.Tau);
            hen.Timer = rng.RandfRange(0.5f, 4f);
            _hens.Add(hen);
            AddChild(hen.Root);
            PlaceHen(hen);
        }
        _yardCentre = new Vector3(cw.X, Ground(cw), cw.Y);
    }

    static Hen MakeHen(Color body, float phase)
    {
        var hen = new Hen { Root = new Node3D(), Tilt = new Node3D { Position = new Vector3(0, 0.15f, 0) }, Phase = phase };
        var leg = new Color(0.85f, 0.62f, 0.2f);
        hen.Tilt.AddChild(Batched(n =>
        {
            n.AddChild(P(Props.Ball(0.14f), body, new Vector3(0, 0.1f, 0), null, new Vector3(0.85f, 0.85f, 1.3f)));
            n.AddChild(P(Props.Box(0.04f, 0.15f, 0.13f), body.Darkened(0.1f), new Vector3(0, 0.2f, -0.2f), new Vector3(-30, 0, 0)));
            foreach (float s in new[] { -1f, 1f })
            {
                n.AddChild(P(Props.Box(0.022f, 0.14f, 0.022f), leg, new Vector3(s * 0.05f, -0.07f, 0.02f)));
                n.AddChild(P(Props.Box(0.05f, 0.015f, 0.07f), leg, new Vector3(s * 0.05f, -0.145f, 0.04f)));
            }
        }));
        hen.Head = new Node3D { Position = new Vector3(0, 0.17f, 0.15f) };
        hen.Head.AddChild(Batched(n =>
        {
            n.AddChild(P(Props.Box(0.05f, 0.13f, 0.05f), body, new Vector3(0, 0.05f, 0)));
            n.AddChild(P(Props.Ball(0.04f), body.Lightened(0.05f), new Vector3(0, 0.13f, 0.02f)));
            n.AddChild(P(Props.Box(0.014f, 0.04f, 0.05f), new Color(0.82f, 0.08f, 0.06f), new Vector3(0, 0.185f, 0.01f)));
            n.AddChild(P(Props.Box(0.018f, 0.03f, 0.015f), new Color(0.82f, 0.08f, 0.06f), new Vector3(0, 0.09f, 0.055f)));
            n.AddChild(P(Props.Box(0.02f, 0.015f, 0.04f), new Color(0.95f, 0.78f, 0.1f), new Vector3(0, 0.125f, 0.075f)));
        }));
        hen.Tilt.AddChild(hen.Head);
        foreach (float s in new[] { -1f, 1f })
        {
            var wing = new Node3D { Position = new Vector3(s * 0.115f, 0.18f, 0.0f) };
            wing.AddChild(Batched(n => n.AddChild(P(Props.Box(0.015f, 0.1f, 0.2f), body.Darkened(0.12f), new Vector3(s * 0.01f, -0.05f, -0.02f)))));
            hen.Tilt.AddChild(wing);
            if (s < 0) hen.WingL = wing; else hen.WingR = wing;
        }
        hen.Root.AddChild(hen.Tilt);
        hen.Root.Scale = Vector3.One * Size;
        return hen;
    }

    void PlaceHen(Hen h)
    {
        var w = W(_henHouse, h.Pos);
        h.Root.Position = new Vector3(w.X, Ground(w) + h.Hop, w.Y);
        h.Root.Rotation = new Vector3(0, _henHouse.Rotation + h.Heading, 0);
    }

    static Vector2 ClampYard(Vector2 p)
    {
        p = new Vector2(Mathf.Clamp(p.X, YardMin.X, YardMax.X), Mathf.Clamp(p.Y, YardMin.Y, YardMax.Y));
        Vector2 d = p - CoopAt;
        if (d.Length() < CoopClear) p = CoopAt + (d.LengthSquared() < 0.0001f ? Vector2.Right : d.Normalized()) * CoopClear;
        return new Vector2(Mathf.Clamp(p.X, YardMin.X, YardMax.X), Mathf.Clamp(p.Y, YardMin.Y, YardMax.Y));
    }

    void TickHens(float dt, Vector3 car, bool moving)
    {
        // The car in the yard's own frame.
        Vector3 rel = new Basis(Vector3.Up, -_henHouse.Rotation) * (car - _henHouse.Position);
        var carLocal = new Vector2(rel.X, rel.Z);
        foreach (var h in _hens)
        {
            float carDist = h.Pos.DistanceTo(carLocal);
            if (h.Mode != 2 && carDist < HenFleeDist && moving)
            {
                h.Mode = 2;
                h.Timer = 0.75f + (float)GD.Randf() * 0.55f;
                Vector2 away = (h.Pos - carLocal).Normalized();
                away = away.Rotated((float)GD.Randf() * 1.2f - 0.6f);
                h.FleeDir = away;
                h.Heading = MathF.Atan2(away.X, away.Y);
            }
            h.Phase += dt;
            float peck = 0, tilt = 0, bob = 0, flap = 0, headUp = 0;
            switch (h.Mode)
            {
                case 2:
                    h.Pos = ClampYard(h.Pos + h.FleeDir * 4.2f * dt);
                    h.Hop = 0.13f * Size * MathF.Abs(MathF.Sin(h.Phase * 11f));
                    flap = 0.9f + 0.5f * MathF.Sin(h.Phase * 38f);
                    headUp = 1f;
                    h.Timer -= dt;
                    if (h.Timer <= 0) { h.Mode = 3; h.Timer = 2.2f + (float)GD.Randf() * 2.5f; h.Hop = 0; }
                    break;
                case 3:
                    headUp = 1f;
                    h.Timer -= dt;
                    if (h.Timer <= 0) { h.Mode = 0; h.Timer = 1f; }
                    break;
                case 1:
                {
                    Vector2 d = h.Target - h.Pos;
                    if (d.Length() < 0.1f) { h.Mode = 0; h.Timer = 2f + (float)GD.Randf() * 4f; break; }
                    h.Heading = MathF.Atan2(d.X, d.Y);
                    h.Pos = ClampYard(h.Pos + d.Normalized() * 0.4f * dt);
                    bob = MathF.Sin(h.Phase * 9f);
                    break;
                }
                default:
                {
                    // Peck in bursts of a few jabs with a look-up between.
                    float cycle = MathF.Sin(h.Phase * 0.9f + h.Timer);
                    float jab = cycle > -0.25f ? MathF.Pow(MathF.Abs(MathF.Sin(h.Phase * 7f)), 2f) : 0f;
                    peck = jab;
                    tilt = jab * 0.6f;
                    h.Timer -= dt;
                    if (h.Timer <= 0)
                    {
                        h.Mode = 1;
                        h.Target = ClampYard(new Vector2(h.Pos.X + (float)GD.Randf() * 4f - 2f, h.Pos.Y + (float)GD.Randf() * 3f - 1.5f));
                    }
                    break;
                }
            }
            h.Tilt.Rotation = new Vector3(tilt, 0, 0);
            h.Head.Rotation = new Vector3(headUp > 0 ? -0.25f : peck * 0.7f, 0, 0);
            h.Head.Position = new Vector3(0, 0.17f, 0.15f + 0.025f * bob);
            h.WingL.Rotation = new Vector3(0, 0, -flap);
            h.WingR.Rotation = new Vector3(0, 0, flap);
            PlaceHen(h);
        }
    }

    // ---------------------------------------------------------------- donkey

    sealed class Donkey
    {
        public PropInstance House;
        public Node3D Root, Neck, EarL, EarR, Tail, Hind;
        public float Raise, Kick = -1f, Cooldown, TailT = 2f, TailKick, EarT = 1.5f, EarKick, EarSide = 1f, Chew, Phase;
        public bool Armed = true;
    }

    void BuildDonkey(PropInstance h)
    {
        var d = new Donkey { House = h };
        Vector2 pos = W(h, DonkeyAt);
        var grey = new Color(0.46f, 0.4f, 0.35f);
        var pale = new Color(0.8f, 0.76f, 0.68f);
        var dark = new Color(0.24f, 0.2f, 0.18f);
        d.Root = new Node3D { Position = new Vector3(pos.X, Ground(pos), pos.Y), Rotation = new Vector3(0, h.Rotation - MathF.PI / 2f, 0), Scale = Vector3.One * Size };
        d.Root.AddChild(Batched(n =>
        {
            n.AddChild(P(Props.Box(0.42f, 0.46f, 1.1f), grey, new Vector3(0, 1.05f, 0)));
            n.AddChild(P(Props.Box(0.34f, 0.14f, 0.85f), pale, new Vector3(0, 0.85f, 0.02f)));
            n.AddChild(P(Props.Box(0.06f, 0.05f, 1.0f), dark, new Vector3(0, 1.3f, 0)));        // the dorsal stripe
            n.AddChild(P(Props.Box(0.5f, 0.06f, 0.05f), dark, new Vector3(0, 1.3f, 0.22f)));    // and the shoulder cross
            foreach (float s in new[] { -1f, 1f })
            {
                n.AddChild(P(Props.Box(0.1f, 0.8f, 0.11f), grey.Darkened(0.04f), new Vector3(s * 0.14f, 0.4f, 0.42f)));
                n.AddChild(P(Props.Box(0.11f, 0.07f, 0.13f), dark, new Vector3(s * 0.14f, 0.035f, 0.43f)));
            }
        }));
        // Neck and head hang from the shoulder: pitched down to graze, up when something passes.
        d.Neck = new Node3D { Position = new Vector3(0, 1.2f, 0.5f) };
        d.Neck.AddChild(Batched(n =>
        {
            n.AddChild(P(Props.Box(0.2f, 0.24f, 0.5f), grey, new Vector3(0, 0, 0.22f)));
            n.AddChild(P(Props.Box(0.06f, 0.1f, 0.46f), dark, new Vector3(0, 0.15f, 0.2f)));                              // mane
            n.AddChild(P(Props.Box(0.2f, 0.22f, 0.4f), grey.Lightened(0.03f), new Vector3(0, -0.08f, 0.6f), new Vector3(20, 0, 0)));
            n.AddChild(P(Props.Box(0.17f, 0.17f, 0.16f), pale, new Vector3(0, -0.17f, 0.78f), new Vector3(20, 0, 0)));     // pale muzzle
            n.AddChild(P(Props.Box(0.03f, 0.03f, 0.03f), dark, new Vector3(0.1f, 0.0f, 0.65f)));                          // eyes
            n.AddChild(P(Props.Box(0.03f, 0.03f, 0.03f), dark, new Vector3(-0.1f, 0.0f, 0.65f)));
        }));
        foreach (float s in new[] { -1f, 1f })
        {
            var ear = new Node3D { Position = new Vector3(s * 0.07f, 0.05f, 0.5f), Rotation = new Vector3(0, 0, -s * 0.4f) };
            ear.AddChild(Batched(n =>
            {
                n.AddChild(P(Props.Box(0.06f, 0.3f, 0.04f), grey.Darkened(0.05f), new Vector3(0, 0.15f, 0)));
                n.AddChild(P(Props.Box(0.03f, 0.2f, 0.045f), dark, new Vector3(0, 0.2f, 0)));
            }));
            d.Neck.AddChild(ear);
            if (s < 0) d.EarR = ear; else d.EarL = ear;
        }
        d.Root.AddChild(d.Neck);
        d.Tail = new Node3D { Position = new Vector3(0, 1.22f, -0.55f) };
        d.Tail.AddChild(Batched(n =>
        {
            n.AddChild(P(Props.Box(0.05f, 0.5f, 0.05f), grey, new Vector3(0, -0.25f, -0.02f)));
            n.AddChild(P(Props.Box(0.09f, 0.18f, 0.09f), dark, new Vector3(0, -0.55f, -0.02f)));
        }));
        d.Root.AddChild(d.Tail);
        d.Hind = new Node3D { Position = new Vector3(0, 0.88f, -0.48f) };
        d.Hind.AddChild(Batched(n =>
        {
            foreach (float s in new[] { -1f, 1f })
            {
                n.AddChild(P(Props.Box(0.11f, 0.8f, 0.13f), grey.Darkened(0.04f), new Vector3(s * 0.14f, -0.42f, 0)));
                n.AddChild(P(Props.Box(0.12f, 0.07f, 0.14f), dark, new Vector3(s * 0.14f, -0.85f, 0.01f)));
            }
        }));
        d.Root.AddChild(d.Hind);
        AddChild(d.Root);

        // Peg at the izba corner and the rope sagging across the grass to the halter.
        Vector2 peg = W(h, PegAt), collar = W(h, DonkeyAt.X - 0.65f * Size, DonkeyAt.Y);
        float pegY = Ground(peg), collarY = Ground(collar);
        var a = new Vector3(peg.X, pegY + 0.42f * Size, peg.Y); var b = new Vector3(collar.X, collarY + 0.95f * Size, collar.Y);
        AddChild(Batched(n =>
        {
            n.AddChild(Bar(new Vector3(peg.X, pegY, peg.Y), a + Vector3.Up * 0.04f, 0.07f * Size, Wood));
            const int Seg = 7;
            Vector3 Pt(float u)
            {
                var p = a.Lerp(b, u);
                float sag = 4f * 0.5f * u * (1 - u);
                p.Y = Mathf.Max(p.Y - sag, Ground(new Vector2(p.X, p.Z)) + 0.04f);
                return p;
            }
            for (int i = 0; i < Seg; i++) n.AddChild(Bar(Pt(i / (float)Seg), Pt((i + 1) / (float)Seg), 0.03f * Size, Rope.Darkened(0.2f)));
        }));
        _donkey = d;
    }

    void TickDonkey(float dt, Vector3 car, float speed)
    {
        var d = _donkey;
        d.Phase += dt;
        float dist = new Vector2(car.X - d.Root.Position.X, car.Z - d.Root.Position.Z).Length();
        bool alert = dist < DonkeyAlertDist;
        d.Raise += ((alert ? 1f : 0f) - d.Raise) * MathF.Min(1f, dt * (alert ? 5f : 1.2f));
        d.Cooldown -= dt;
        if (dist > 25f) d.Armed = true;
        if (alert && speed > KickSpeed && d.Armed && d.Kick < 0 && d.Cooldown <= 0)
        {
            d.Kick = 0; d.Armed = false; d.Cooldown = 12f;
        }
        float kickAngle = 0;
        if (d.Kick >= 0)
        {
            d.Kick += dt;
            float u = d.Kick / 0.7f;
            if (u >= 1f) d.Kick = -1f; else kickAngle = 0.95f * MathF.Pow(MathF.Sin(MathF.PI * u), 0.7f);
        }
        d.Hind.Rotation = new Vector3(kickAngle, 0, 0);
        // The whole rear lifts a little with the kick.
        d.Root.Rotation = new Vector3(-kickAngle * 0.07f, d.House.Rotation - MathF.PI / 2f, 0);

        // Grazing: head low and chewing; alert: neck up, ears forward.
        d.Chew += dt * 5f;
        float graze = 1.1f + 0.04f * MathF.Sin(d.Chew), up = -0.6f;
        d.Neck.Rotation = new Vector3(Mathf.Lerp(graze, up, d.Raise), 0, 0);

        d.TailT -= dt;
        if (d.TailT <= 0) { d.TailKick = 1f; d.TailT = 1.5f + (float)GD.Randf() * 3f; }
        d.TailKick = MathF.Max(0, d.TailKick - dt * 1.4f);
        d.Tail.Rotation = new Vector3(0.12f + 0.15f * MathF.Sin(d.Phase * 0.8f), 0.9f * d.TailKick * MathF.Sin(d.Phase * 22f), 0);

        d.EarT -= dt;
        if (d.EarT <= 0) { d.EarKick = 1f; d.EarSide = GD.Randf() < 0.5f ? -1f : 1f; d.EarT = 1f + (float)GD.Randf() * 3f; }
        d.EarKick = MathF.Max(0, d.EarKick - dt * 2.5f);
        float flick = 0.35f * d.EarKick * MathF.Sin(d.Phase * 25f);
        float spread = Mathf.Lerp(0.4f, 0.2f, d.Raise);
        d.EarL.Rotation = new Vector3(0.1f * d.Raise, 0, -spread + (d.EarSide > 0 ? flick : 0));
        d.EarR.Rotation = new Vector3(0.1f * d.Raise, 0, spread + (d.EarSide < 0 ? flick : 0));
    }

    // ---------------------------------------------------------------- the clock

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _clock += dt;
        _cam = GetViewport().GetCamera3D();

        // The car's speed from how far it moved this frame (a jump is a reset, not speed).
        Vector3 car = SeeThrough.Car;
        if (_prevValid && dt > 0)
        {
            float v = new Vector2(car.X - _prevCar.X, car.Z - _prevCar.Z).Length() / dt;
            if (v > 90f) v = 0;
            _speed += (v - _speed) * MathF.Min(1f, dt * 8f);
        }
        _prevCar = car; _prevValid = true;

        _wind = 0.55f + 0.25f * MathF.Sin(_clock * 0.37f) + 0.2f * MathF.Sin(_clock * 1.13f + 1f);
        for (int i = 0; i < _clothes.Count; i++)
        {
            var c = _clothes[i];
            if (!Near(c.Pivot.GlobalPosition)) continue;
            float swing = c.Lean * _wind + c.Amp * _wind * MathF.Sin(_clock * c.Rate + c.Phase);
            c.Pivot.Rotation = new Vector3(swing, c.Rot + 0.12f * _wind * MathF.Sin(_clock * c.Rate * 0.7f + c.Phase * 2f), 0);
        }
        if (_hens.Count > 0 && Near(_yardCentre)) TickHens(dt, car, _speed > 1.5f);
        if (_donkey != null && Near(_donkey.Root.GlobalPosition)) TickDonkey(dt, car, _speed);
    }
}
