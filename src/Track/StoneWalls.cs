using System;
using System.Collections.Generic;
using CCCPRacing.Car;
using Godot;

namespace CCCPRacing.Track;

/// <summary>
/// The low dry-stone walls, one MultiMesh of stones in ~2 m segments (TrackData.WallSegments). A hard
/// hit knocks a segment down (Breakables): its stones and a few hidden bits of rubble fly a little,
/// bounce and lie in the grass until the race restarts. Loose stones are not obstacles.
/// </summary>
public partial class StoneWalls : Node3D
{
    const int RubblePerSegment = 3;
    static readonly Color Grey = new(0.55f, 0.54f, 0.5f);

    struct Stone
    {
        public Transform3D Xf;             // unscaled
        public Vector3 Size;
    }

    class Segment
    {
        public int First, Count;           // stones [First, First + Count), the last RubblePerSegment hidden rubble
        public bool Broken;
    }

    class Loose
    {
        public int Index;
        public Vector3 Pos, Vel, AngVel, Size;
        public Basis Rot;
        public float Half;
        public bool Resting;
    }

    TrackData _t;
    TyreEffects _fx;
    MultiMesh _mm;
    readonly List<Stone> _stones = new();
    readonly List<Segment> _segs = new();
    readonly List<Loose> _loose = new();
    readonly RandomNumberGenerator _rng = new() { Seed = 1956 };

    public void Init(TrackData t, TyreEffects fx, StandardMaterial3D mat)
    {
        _t = t;
        _fx = fx;
        var rng = new RandomNumberGenerator { Seed = 1953 };        // the walls look as they always did
        var rubble = new RandomNumberGenerator { Seed = 1917 };
        var cols = new List<Color>();
        foreach (var run in t.Fences)
            if (run.Type == "stone")
                for (int k = 0; k + 1 < run.Points.Count; k++)
                {
                    var seg = new Segment { First = _stones.Count };
                    BuildSegment(run.Points[k], run.Points[k + 1], rng, cols);
                    for (int r = 0; r < RubblePerSegment; r++)
                    {
                        float s = rubble.RandfRange(0.16f, 0.3f);
                        _stones.Add(new Stone { Xf = Transform3D.Identity, Size = new Vector3(s * rubble.RandfRange(0.9f, 1.4f), s * 0.8f, s) });
                        cols.Add(Fences.Jit(Grey * 0.9f, rubble, 0.08f));
                    }
                    seg.Count = _stones.Count - seg.First;
                    _segs.Add(seg);
                }
        if (_segs.Count != t.WallSegments.Count) GD.PushWarning($"stone walls: {_segs.Count} segments, {t.WallSegments.Count} in the track");
        if (_stones.Count == 0) return;

        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        Fences.AddBox(st, Transform3D.Identity, Vector3.One, Colors.White);   // the instance colour shows through
        _mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = st.Commit(), InstanceCount = _stones.Count };
        for (int i = 0; i < _stones.Count; i++) _mm.SetInstanceColor(i, cols[i]);
        foreach (var seg in _segs) Stand(seg);
        AddChild(new MultiMeshInstance3D { Name = "Stones", Multimesh = _mm, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
    }

    /// <summary>Stones between two wall points: a row of big ones, a smaller one on top most of the time.</summary>
    void BuildSegment(Vector3 p, Vector3 q, RandomNumberGenerator rng, List<Color> cols)
    {
        Vector3 d = q - p;
        float len = d.Length();
        Vector3 dir = d / len;
        var along = Basis.LookingAt(dir, Vector3.Up);
        float x = 0;
        while (x < len)
        {
            float sl = rng.RandfRange(0.4f, 0.75f);
            Vector3 c = p + dir * Math.Min(len, x + sl * 0.5f);
            c.Y = _t.MeshHeight(c.X, c.Z);
            float h = rng.RandfRange(0.35f, 0.55f);
            var b = along * new Basis(Vector3.Up, rng.RandfRange(-0.15f, 0.15f));
            _stones.Add(new Stone { Xf = new Transform3D(b, c + new Vector3(0, h * 0.5f - 0.05f, 0)), Size = new Vector3(rng.RandfRange(0.5f, 0.65f), h, sl) });
            cols.Add(Fences.Jit(Grey, rng, 0.08f));
            if (rng.Randf() < 0.75f)
            {
                float h2 = rng.RandfRange(0.18f, 0.3f);
                _stones.Add(new Stone
                {
                    Xf = new Transform3D(along * new Basis(Vector3.Up, rng.RandfRange(-0.4f, 0.4f)), c + new Vector3(rng.RandfRange(-0.08f, 0.08f), h - 0.05f + h2 * 0.5f, 0)),
                    Size = new Vector3(rng.RandfRange(0.35f, 0.5f), h2, sl * rng.RandfRange(0.6f, 0.9f)),
                });
                cols.Add(Fences.Jit(Grey * 0.95f, rng, 0.08f));
            }
            x += sl * 0.92f;
        }
    }

    static Transform3D Scaled(Basis rot, Vector3 size, Vector3 pos) => new(rot * Basis.FromScale(size), pos);

    /// <summary>The segment stands as built, its rubble hidden.</summary>
    void Stand(Segment seg)
    {
        for (int i = seg.First; i < seg.First + seg.Count; i++)
        {
            bool rubble = i >= seg.First + seg.Count - RubblePerSegment;
            var s = _stones[i];
            _mm.SetInstanceTransform(i, rubble ? new Transform3D(Basis.FromScale(Vector3.Zero), s.Xf.Origin) : Scaled(s.Xf.Basis, s.Size, s.Xf.Origin));
        }
    }

    /// <summary>Everything stands again, the loose stones are gone (race restart).</summary>
    public void Reset()
    {
        foreach (var seg in _segs)
            if (seg.Broken) { seg.Broken = false; Stand(seg); }
        _loose.Clear();
    }

    /// <summary>Knocks segment i (index into TrackData.WallSegments) down, hit at the point by something moving at vel.</summary>
    public void Break(int i, Vector2 hit, Vector3 vel)
    {
        var seg = _segs[i];
        if (seg.Broken) return;
        seg.Broken = true;
        var hv = new Vector3(vel.X, 0, vel.Z);
        float speed = hv.Length();
        Vector3 fwd = speed > 0.1f ? hv / speed : Vector3.Forward;
        int wall = seg.Count - RubblePerSegment;
        for (int k = 0; k < seg.Count; k++)
        {
            int idx = seg.First + k;
            var s = _stones[idx];
            Vector3 pos = s.Xf.Origin;
            if (k >= wall)
            {
                // Rubble: chips from inside the wall, somewhere along the segment.
                var from = _stones[seg.First + _rng.RandiRange(0, Math.Max(0, wall - 1))];
                pos = from.Xf.Origin + new Vector3(_rng.RandfRange(-0.2f, 0.2f), 0.1f, _rng.RandfRange(-0.2f, 0.2f));
            }
            Vector3 away = pos - new Vector3(hit.X, pos.Y, hit.Y);
            away = away.LengthSquared() > 1e-4f ? away.Normalized() : fwd;
            _loose.Add(new Loose
            {
                Index = idx,
                Pos = pos,
                Rot = s.Xf.Basis,
                Size = s.Size,
                Half = Math.Min(s.Size.X, Math.Min(s.Size.Y, s.Size.Z)) * 0.5f,
                // Heavy: pushed along with the car, a little up and apart, not far.
                Vel = hv * _rng.RandfRange(0.25f, 0.55f) + away * _rng.RandfRange(0.5f, 2f) + Vector3.Up * _rng.RandfRange(1.2f, 2.5f + speed * 0.1f),
                AngVel = new Vector3(_rng.RandfRange(-1, 1), _rng.RandfRange(-1, 1), _rng.RandfRange(-1, 1)) * (2f + speed * 0.25f),
            });
        }
        if (_fx == null) return;
        Vector3 at = new(hit.X, _t.MeshHeight(hit.X, hit.Y) + 0.4f, hit.Y);
        _fx.Smoke(at, hv * 0.2f + Vector3.Up * 0.6f, new Color(0.62f, 0.6f, 0.55f), 0.5f, 0.7f, 2.6f, 1.4f);
        _fx.Smoke(at + fwd * 0.8f, hv * 0.3f + Vector3.Up * 0.8f, new Color(0.55f, 0.5f, 0.42f), 0.4f, 0.6f, 2.2f, 1.2f);
        for (int k = 0; k < 10; k++)
            _fx.Burst(at, hv * 0.4f + new Vector3(_rng.RandfRange(-3, 3), _rng.RandfRange(1.5f, 4.5f), _rng.RandfRange(-3, 3)),
                Fences.Jit(Grey, _rng, 0.1f), 1f, 0.14f, 0.1f, _rng.RandfRange(0.8f, 1.4f), 14f, 0.6f, true);
    }

    /// <summary>Replay: every loose stone has come to rest (a seek shows a wall that fell a while ago).</summary>
    public void Settle()
    {
        for (int i = 0; i < 900 && _loose.Exists(d => !d.Resting); i++) Tick(1f / 60f);
    }

    public void Tick(float dt)
    {
        foreach (var d in _loose)
        {
            if (d.Resting) continue;
            d.Vel.Y -= 9.81f * dt;
            d.Pos += d.Vel * dt;
            float w = d.AngVel.Length();
            if (w > 1e-4f) d.Rot = (new Basis(d.AngVel / w, w * dt) * d.Rot).Orthonormalized();
            float g = _t.MeshHeight(d.Pos.X, d.Pos.Z) + d.Half;
            if (d.Pos.Y < g)
            {
                d.Pos.Y = g;
                if (d.Vel.Y < -1.5f)
                {
                    // Stones don't bounce much: a thud, a short hop, most of the speed gone.
                    d.Vel = new Vector3(d.Vel.X * 0.4f, -d.Vel.Y * 0.2f, d.Vel.Z * 0.4f);
                    d.AngVel *= 0.5f;
                }
                else
                {
                    d.Vel = new Vector3(d.Vel.X * 0.7f, 0, d.Vel.Z * 0.7f);
                    d.AngVel *= 0.7f;
                    d.Rot = d.Rot.Slerp(WoodFences.LieFlat(d.Rot), Math.Min(1f, 8f * dt)).Orthonormalized();
                    if (d.Vel.LengthSquared() < 0.05f && d.AngVel.LengthSquared() < 0.2f)
                    {
                        d.Rot = WoodFences.LieFlat(d.Rot);
                        d.Resting = true;
                    }
                }
            }
            _mm.SetInstanceTransform(d.Index, Scaled(d.Rot, d.Size, d.Pos));
        }
    }
}
