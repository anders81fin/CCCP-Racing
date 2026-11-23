using System;
using System.Collections.Generic;
using Godot;

namespace CCCPRacing.Track;

/// <summary>
/// The Poligon's first static vegetation trial (the owner's order, 2026-11-23): at the start straight low grey-green shrubs on one side and a group of green birches opposite (the faded-grass look of that
/// verge is the terrain's own drawing: TerrainMesh.DryGrass; the soft patch overlay of the first version was dropped on the owner's review); on the open gravel ridge sparse, irregular clumps of one bent straw-pale hay tuft that leans the way the windsock blows even when
/// the air is still, and a few of the track's own green bushes on the sheltered side below. Nothing here makes an obstacle, and the crest itself stays open.
///
/// Replay and tape: all of it is static scenery that never moves, changes or breaks (docs/replay-tape.md names such scenery as needing no registration), built once with the world from the track's own data and
/// a fixed seed, so a drive, the title replay and the viewer see the same thing and a seek has nothing to restore.
/// </summary>
public partial class PoligonVegetation : Node3D
{
    /// <summary>Master switch (a test turns the trial off for the "before" pictures).</summary>
    public static bool Enabled = true;

    public int Shrubs { get; private set; }
    public int Birches { get; private set; }
    public int HayTufts { get; private set; }
    public int LeeBushes { get; private set; }
    public int ClearedHay { get; private set; }
    public Vector2 Wind { get; }
    public readonly List<Vector3> TuftPositions = new(), ShrubPositions = new(), BirchPositions = new(), BushPositions = new();
    /// <summary>The way the hay leans (unit, ground plane): the windsock's.</summary>
    public Vector2 Lean => Wind;
    public int StartIndex { get; private set; }
    public int StartSide { get; private set; }

    readonly TrackData _t;
    readonly RandomNumberGenerator _rng = new() { Seed = 1917 };

    static float Hash(int k) { uint h = (uint)k * 2654435761u; h ^= h >> 15; h *= 2246822519u; h ^= h >> 13; h *= 3266489917u; h ^= h >> 16; return (h & 0xFFFF) / 65535f; }

    /// <summary>The trial for the Poligon: needs the camp's windsock (the hay's lean) and the dust's ridge; null otherwise (nothing is added to any other track).</summary>
    public static PoligonVegetation TryCreate(TrackData t, int ridgeIndex)
    {
        if (t == null || t.Id != "bonus" || PoligonDust.WindOf(t) is not { } wind || ridgeIndex < 0) return null;
        var v = new PoligonVegetation(t, wind) { Name = "PoligonVegetation" };
        v.BuildStart();
        v.BuildRidge(ridgeIndex);
        v.BuildCleared();
        return v;
    }

    PoligonVegetation(TrackData t, Vector2 wind) { _t = t; Wind = wind; }

    public void Apply() => Visible = Enabled;

    // ------------------------------------------------------------ site rules

    bool Ground(Vector2 p, params SurfaceType[] allowed)
    {
        if (p.X < 14f || p.Y < 14f || p.X > _t.WorldSize - 14f || p.Y > _t.WorldSize - 14f) return false;
        var s = _t.TileSurface((int)(p.X / _t.Tile), (int)(p.Y / _t.Tile));
        return Array.IndexOf(allowed, s) >= 0;
    }

    float RoadGap(Vector2 p)
    {
        float best = float.MaxValue;
        for (int i = 0; i < _t.Center.Length; i++)
        {
            float d = _t.Center[i].DistanceTo(p) - _t.HalfWidth[i];
            if (d < best) best = d;
        }
        return best;
    }

    bool Clear(Vector2 p, float r)
    {
        foreach (var o in _t.Obstacles) if (o.Center.DistanceTo(p) < o.Radius + r) return false;
        foreach (var pr in _t.Props) if (new Vector2(pr.Position.X, pr.Position.Z).DistanceTo(p) < 2.2f + r) return false;
        foreach (var pad in _t.Pads) if (p.X > pad.X0 - r && p.X < pad.X1 + r && p.Y > pad.Z0 - r && p.Y < pad.Z1 + r) return false;
        return true;
    }

    // ------------------------------------------------------------ the start straight

    void BuildStart()
    {
        int n = _t.Center.Length, si = 0; float sd = float.MaxValue;
        var start = new Vector2(_t.StartPos.X, _t.StartPos.Z);
        for (int i = 0; i < n; i++) { float d = _t.Center[i].DistanceSquaredTo(start); if (d < sd) { sd = d; si = i; } }
        StartIndex = si;
        Vector2 dir = _t.CenterDir(si), nrm = new(-dir.Y, dir.X);
        // the side with the more unbroken grass is the one to break up
        int greenLeft = 0, greenRight = 0;
        for (int k = -10; k <= 25; k += 3)
            for (float off = 4f; off <= 16f; off += 3f)
            {
                int i = ((si + k) % n + n) % n; Vector2 c = _t.Center[i], d = _t.CenterDir(i), q = new(-d.Y, d.X);
                if (Ground(c + q * (_t.HalfWidth[i] + off), SurfaceType.Grass)) greenLeft++;
                if (Ground(c - q * (_t.HalfWidth[i] + off), SurfaceType.Grass)) greenRight++;
            }
        int side = greenLeft >= greenRight ? 1 : -1; StartSide = side;

        // low grey-green shrubs behind the start verge, in three loose groups
        var shrubs = new List<Transform3D>(); var shrubCols = new List<Color>();
        Color[] grey = { new(0.4f, 0.52f, 0.36f), new(0.36f, 0.48f, 0.34f), new(0.46f, 0.56f, 0.4f), new(0.42f, 0.5f, 0.38f) };
        for (int g = 0; g < 3; g++)
        {
            int i = ((si - 6 + g * 13 + (int)(Hash(g + 90) * 4f)) % n + n) % n;
            Vector2 c = _t.Center[i], d = _t.CenterDir(i), q = new Vector2(-d.Y, d.X) * side;
            Vector2 gc = c + q * (_t.HalfWidth[i] + 12f + 5f * Hash(g + 91));
            int count = 4 + (int)(Hash(g + 92) * 3f);
            for (int s = 0; s < count; s++)
            {
                Vector2 p = gc + new Vector2((Hash(g * 31 + s * 3 + 1) - 0.5f) * 5.2f, (Hash(g * 31 + s * 3 + 2) - 0.5f) * 4.4f);
                if (!Ground(p, SurfaceType.Grass, SurfaceType.Field) || !Clear(p, 1f)) continue;
                float size = 0.8f + 0.5f * Hash(g * 31 + s * 3 + 3);
                var pos = new Vector3(p.X, _t.MeshHeight(p.X, p.Y), p.Y);
                shrubs.Add(new Transform3D(new Basis(Vector3.Up, Hash(s + g * 7) * MathF.Tau).Scaled(new Vector3(size * 1.25f, size * 0.8f, size * 1.25f)), pos));
                shrubCols.Add(grey[(g + s) % grey.Length] * (0.92f + 0.16f * Hash(s * 5 + g)));
                ShrubPositions.Add(pos);
            }
        }
        Shrubs = shrubs.Count;
        if (shrubs.Count > 0) AddChild(Vegetation.Multi(Vegetation.BushMesh(), shrubs, shrubCols, "StartShrubs"));

        // a group of green birches on the other side (only to look at: no obstacle is made)
        var grove = new Node3D { Name = "StartBirches" }; AddChild(grove);
        for (int k = 0; k < 10; k++)
        {
            int i = ((si + 2 + k * 2 + (int)(Hash(k + 120) * 3f)) % n + n) % n;
            Vector2 c = _t.Center[i], d = _t.CenterDir(i), q = new Vector2(-d.Y, d.X) * -side;
            Vector2 p = c + q * (_t.HalfWidth[i] + 10f + 6f * Hash(k + 121));
            if (!Ground(p, SurfaceType.Grass, SurfaceType.Field) || !Clear(p, 2.2f) || RoadGap(p) < 8f) continue;
            float size = 0.85f + 0.4f * Hash(k + 122);
            var tree = Props.DecorBirch(size, (int)p.X, (int)p.Y);
            var pos = new Vector3(p.X, _t.MeshHeight(p.X, p.Y), p.Y);
            tree.Position = pos; tree.RotationDegrees = new Vector3(0, Hash(k + 123) * 360f, 0);
            grove.AddChild(tree); BirchPositions.Add(pos); Birches++;
        }
    }

    // ------------------------------------------------------------ the ridge

    void BuildRidge(int ridge)
    {
        int n = _t.Center.Length;
        var tufts = new List<Transform3D>(); var tuftCols = new List<Color>();
        float yaw = MathF.Atan2(-Wind.Y, Wind.X);      // the tuft's local +X is the way it leans: the wind's
        for (int c = 0; c < 15; c++)
        {
            if (Hash(c * 17 + 5) < 0.28f) continue;      // gaps: the groups are sparse and irregular
            int i = ((ridge - 46 + c * 6 + (int)(Hash(c * 17 + 6) * 5f)) % n + n) % n;
            Vector2 cc = _t.Center[i], d = _t.CenterDir(i), q = new Vector2(-d.Y, d.X) * (Hash(c * 17 + 7) < 0.5f ? 1f : -1f);
            float off = 0.9f + 6.0f * MathF.Pow(Hash(c * 17 + 8), 1.6f);
            Vector2 gc = cc + q * (_t.HalfWidth[i] + off);
            int count = 3 + (int)(Hash(c * 17 + 9) * 5f);
            for (int s = 0; s < count; s++)
            {
                Vector2 p = gc + new Vector2((Hash(c * 41 + s * 3 + 1) - 0.5f) * 2.4f, (Hash(c * 41 + s * 3 + 2) - 0.5f) * 2.4f);
                if (!Ground(p, SurfaceType.Grass, SurfaceType.DeadGrass, SurfaceType.Field, SurfaceType.Dirt) || !Clear(p, 0.5f) || RoadGap(p) < 0.8f) continue;
                float size = 1.35f + 0.8f * Hash(c * 41 + s * 3 + 3);
                var pos = new Vector3(p.X, _t.MeshHeight(p.X, p.Y), p.Y);
                float jitter = (Hash(c * 59 + s) - 0.5f) * 0.42f;      // a little either side of the wind's line
                tufts.Add(new Transform3D(new Basis(Vector3.Up, yaw + jitter).Scaled(Vector3.One * size), pos));
                tuftCols.Add(new Color(0.95f, 0.84f, 0.52f) * (0.88f + 0.2f * Hash(c * 67 + s)));      // oljenvaalea
                TuftPositions.Add(pos);
            }
        }
        HayTufts = tufts.Count;
        if (tufts.Count > 0) AddChild(Vegetation.Multi(HayMesh(), tufts, tuftCols, "RidgeHay"));

        // below the crest on the sheltered side, the track's own green bushes
        var bushes = new List<Transform3D>(); var bushCols = new List<Color>();
        Color[] greens = { new(0.28f, 0.45f, 0.2f), new(0.34f, 0.52f, 0.22f), new(0.24f, 0.38f, 0.18f) };
        float crest = _t.CenterH[ridge];
        for (int b = 0; b < 10; b++)
        {
            int i = ((ridge - 30 + b * 6 + (int)(Hash(b + 200) * 4f)) % n + n) % n;
            Vector2 cc = _t.Center[i], d = _t.CenterDir(i), q = new(-d.Y, d.X);
            if (q.Dot(Wind) < 0) q = -q;      // the lee side: the wind blows away from the crest there
            Vector2 p = cc + q * (_t.HalfWidth[i] + 6f + 9f * Hash(b + 201));
            if (!Ground(p, SurfaceType.Grass, SurfaceType.Field) || !Clear(p, 1f) || RoadGap(p) < 4f) continue;
            if (_t.MeshHeight(p.X, p.Y) > crest - 0.6f) continue;      // below the crest
            float size = 0.8f + 0.6f * Hash(b + 202);
            var pos = new Vector3(p.X, _t.MeshHeight(p.X, p.Y), p.Y);
            bushes.Add(new Transform3D(new Basis(Vector3.Up, Hash(b + 203) * MathF.Tau).Scaled(new Vector3(size, size * 0.85f, size)), pos));
            bushCols.Add(greens[b % greens.Length]); BushPositions.Add(pos);
        }
        LeeBushes = bushes.Count;
        if (bushes.Count > 0) AddChild(Vegetation.Multi(Vegetation.BushMesh(), bushes, bushCols, "RidgeLeeBushes"));
    }

    /// <summary>Hay on the ground where a tree was left out (BonusTrees): a few tufts round each spot, leaning like the ridge's.</summary>
    void BuildCleared()
    {
        var tufts = new List<Transform3D>(); var cols = new List<Color>(); float yaw = MathF.Atan2(-Wind.Y, Wind.X);
        for (int c = 0; c < _t.ClearedTrees.Count; c++)
        {
            Vector2 centre = _t.ClearedTrees[c]; int count = 3 + (int)(Hash(c * 13 + 1) * 3f);
            for (int s = 0; s < count; s++)
            {
                Vector2 p = centre + new Vector2((Hash(c * 71 + s * 3 + 1) - 0.5f) * 4.2f, (Hash(c * 71 + s * 3 + 2) - 0.5f) * 4.2f);
                if (!Ground(p, SurfaceType.Grass, SurfaceType.DeadGrass, SurfaceType.Field, SurfaceType.Dirt) || !Clear(p, 0.5f)) continue;
                var pos = new Vector3(p.X, _t.MeshHeight(p.X, p.Y), p.Y);
                tufts.Add(new Transform3D(new Basis(Vector3.Up, yaw + (Hash(c * 79 + s) - 0.5f) * 0.42f).Scaled(Vector3.One * (1.1f + 0.7f * Hash(c * 83 + s))), pos));
                cols.Add(new Color(0.95f, 0.84f, 0.52f) * (0.88f + 0.2f * Hash(c * 89 + s)));
            }
        }
        ClearedHay = tufts.Count;
        if (tufts.Count > 0) AddChild(Vegetation.Multi(HayMesh(), tufts, cols, "ClearedHay"));
    }

    /// <summary>One bent tuft of dry hay: a fan of thin blades that rise and bend towards local +X (the wind's way), dark at the root and pale at the tips.</summary>
    static Mesh HayMesh()
    {
        var st = new SurfaceTool(); st.Begin(Mesh.PrimitiveType.Triangles);
        const int Blades = 16, Segs = 4;
        for (int k = 0; k < Blades; k++)
        {
            float ang = k * 2.399963f;      // the golden angle: an even, unpatterned fan
            float rad = 0.04f + 0.17f * ((k * 7 % 11) / 10f);
            Vector3 root = new(MathF.Cos(ang) * rad, 0, MathF.Sin(ang) * rad);
            float h = 0.7f + 0.5f * (((k * 5) % 7) / 6f), bend = 0.62f * h * (0.8f + 0.4f * ((k * 3 % 5) / 4f));
            Vector3 side = new Vector3(-MathF.Sin(ang), 0, MathF.Cos(ang)) * 0.06f;
            Vector3 prev = root; float prevT = 0f;
            for (int s = 1; s <= Segs; s++)
            {
                float t = s / (float)Segs;
                var pt = root + new Vector3(bend * t * t, h * t, 0f) + new Vector3(0, 0, (k % 3 - 1) * 0.04f * t);
                float w0 = 1f - prevT * 0.8f, w1 = 1f - t * 0.8f;
                Color c0 = new Color(0.55f, 0.5f, 0.36f).Lerp(Colors.White, prevT), c1 = new Color(0.55f, 0.5f, 0.36f).Lerp(Colors.White, t);
                void V(Vector3 v, Color c) { st.SetColor(c); st.SetNormal(Vector3.Up); st.AddVertex(v); }
                V(prev - side * w0, c0); V(prev + side * w0, c0); V(pt + side * w1, c1);
                V(prev - side * w0, c0); V(pt + side * w1, c1); V(pt - side * w1, c1);
                prev = pt; prevT = t;
            }
        }
        return st.Commit();
    }
}
