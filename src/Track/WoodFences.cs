using System;
using System.Collections.Generic;
using CCCPRacing.Car;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Track;

/// <summary>
/// The rickety wooden post-and-rail fences, one breakable span per post: whatever drives into a
/// span (the car, the drunk tractorist) snaps the post and flings the rails, which tumble, bounce
/// and lie in the grass until the race restarts. Intact spans share one mesh, rebuilt on a break.
/// Hitting one costs the car a little speed and knocks its nose, but it never stops it.
/// </summary>
public partial class WoodFences : Node3D
{
    const float SpeedKeep = 0.9f;          // car keeps this share of its speed per span broken
    const float MaxDebris = 160;

    struct Piece
    {
        public Transform3D Xf;
        public Vector3 Size;
        public Color Color;
    }

    class Span
    {
        public Vector2 A, B;               // post at A, rails from A to B (B == A for the last post)
        public readonly List<Piece> Pieces = new();
        public bool Broken;
    }

    class Debris
    {
        public MeshInstance3D Node;
        public Vector3 Pos, Vel, AngVel;
        public Basis Rot;
        public float Half;                 // half the thinnest side: how low it can lie
        public bool Resting;
        public float SpawnTime; public int ReplaySteps;
    }

    TrackData _t;
    TyreEffects _fx;
    readonly List<Span> _spans = new();
    readonly List<Debris> _debris = new();
    MeshInstance3D _intact;
    StandardMaterial3D _mat;
    bool _dirty, _replaying;
    public event Action<int, Vector2, Vector3, ulong> TapeBroken;
    public event Action Restarted;
    public int SpanCount => _spans.Count;
    public int BrokenCount { get { int count = 0; foreach (var span in _spans) if (span.Broken) count++; return count; } }
    public int DebrisCount => _debris.Count;
    public List<Transform3D> DebrisPoses() => _debris.ConvertAll(d => new Transform3D(d.Rot, d.Pos));
    readonly RandomNumberGenerator _rng = new() { Seed = 1961 };

    /// <summary>Spans broken during the last Smash calls since Tick; for sound and messages.</summary>
    public event Action<int,Vector3> SpanBroken;
    public void BreakNetwork(int i,Vector3 velocity) {if(i>=0&&i<_spans.Count&&!_spans[i].Broken) Break(_spans[i],(_spans[i].A+_spans[i].B)*.5f,velocity);}
    public int BrokenThisTick { get; private set; }
    public float CrackStrength { get; private set; }

    static readonly Color Wood = new(0.62f, 0.5f, 0.34f);   // sun-bleached boards read against the grass
    static readonly Color[] Splinters = { new(0.62f, 0.5f, 0.34f), new(0.75f, 0.64f, 0.46f), new(0.45f, 0.35f, 0.22f) };

    public void Init(TrackData t, TyreEffects fx, StandardMaterial3D mat)
    {
        _t = t;
        _fx = fx;
        _mat = mat;
        var rng = new RandomNumberGenerator { Seed = 1953 };
        foreach (var run in t.Fences)
            if (run.Type != "stone") BuildRun(run, rng);
        _intact = new MeshInstance3D { Name = "Intact", MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(_intact);
        // Bump this salt when debris physics or the recorded state/timing meaning changes.
        // Geometry changes also invalidate only this actor's events (span IDs are local).
        const uint tapePhysicsVersion = 1;
        uint hash = 2166136261;
        hash = (hash ^ tapePhysicsVersion) * 16777619;
        void Mix(float v) { hash = (hash ^ (uint)BitConverter.SingleToInt32Bits(v)) * 16777619; }
        foreach (var span in _spans) foreach (var pc in span.Pieces)
        { foreach (var v in new[] { pc.Xf.Origin, pc.Xf.Basis.X, pc.Xf.Basis.Y, pc.Xf.Basis.Z, pc.Size }) { Mix(v.X); Mix(v.Y); Mix(v.Z); } }
        Tape.Version(this, unchecked((int)hash)); Tape.Part(this, "root");
        Rebuild();
    }

    void BuildRun(FenceRun run, RandomNumberGenerator rng)
    {
        for (int k = 0; k < run.Points.Count; k++)
        {
            Vector3 p = run.Points[k];
            var span = new Span { A = new Vector2(p.X, p.Z), B = new Vector2(p.X, p.Z) };
            // Posts lean a little, every which way.
            var tilt = new Basis(new Vector3(rng.RandfRange(-1, 1), 0, rng.RandfRange(-1, 1)).Normalized(), rng.RandfRange(0f, 0.12f));
            span.Pieces.Add(new Piece { Xf = new Transform3D(tilt, p + tilt * new Vector3(0, 0.6f, 0)), Size = new Vector3(0.22f, 1.25f, 0.22f), Color = Fences.Jit(Wood * 0.85f, rng, 0.05f) });
            if (k + 1 < run.Points.Count)
            {
                Vector3 q = run.Points[k + 1];
                span.B = new Vector2(q.X, q.Z);
                Vector3 d = q - p;
                float len = d.Length();
                var along = Basis.LookingAt(d.Normalized(), Vector3.Up);
                foreach (float h in new[] { 0.45f, 0.95f })
                {
                    if (rng.Randf() < (h < 0.5f ? 0.12f : 0.08f)) continue;              // missing plank
                    float sag = rng.RandfRange(-0.06f, 0.02f);
                    var b = along * new Basis(Vector3.Right, rng.RandfRange(-0.05f, 0.05f)); // warped
                    span.Pieces.Add(new Piece { Xf = new Transform3D(b, (p + q) * 0.5f + new Vector3(0, h + sag, 0)), Size = new Vector3(0.12f, 0.2f, len + 0.15f), Color = Fences.Jit(Wood * 1.08f, rng, 0.06f) });
                }
            }
            _spans.Add(span);
        }
    }

    void Rebuild()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        int n = 0;
        foreach (var s in _spans)
        {
            if (s.Broken) continue;
            foreach (var pc in s.Pieces) { Fences.AddBox(st, pc.Xf, pc.Size, pc.Color); n++; }
        }
        _intact.Mesh = n > 0 ? st.Commit() : null;
        _dirty = false;
    }

    /// <summary>Middle and direction of the fence span nearest to p (tests).</summary>
    public (Vector2 mid, Vector2 dir) NearestSpan(Vector2 p)
    {
        Span best = null;
        foreach (var s in _spans)
            if (s.A != s.B && (best == null || p.DistanceTo((s.A + s.B) * 0.5f) < p.DistanceTo((best.A + best.B) * 0.5f))) best = s;
        return best == null ? (p, Vector2.Right) : ((best.A + best.B) * 0.5f, (best.B - best.A).Normalized());
    }

    /// <summary>Everything stands again, the boards are gone (race restart).</summary>
    public void Reset()
    {
        _replaying = false;
        Restarted?.Invoke();
        bool any = false;
        foreach (var s in _spans) { any |= s.Broken; s.Broken = false; }
        foreach (var d in _debris) { RemoveChild(d.Node); d.Node.QueueFree(); }
        _debris.Clear();
        if (any) Rebuild();
    }

    /// <summary>The car ploughs through: its three body circles against every span nearby.</summary>
    public void Smash(CarSim car)
    {
        if (_replaying) return;
        float speed = new Vector2(car.Vel.X, car.Vel.Z).Length();
        if (speed < 0.8f) return;
        int broke = 0;
        foreach (float cz in new[] { -1.3f, 0f, 1.3f })
        {
            Vector3 c = car.Pos + car.Rot * new Vector3(0, 0, cz);
            broke += Smash(new Vector2(c.X, c.Z), 0.95f, car.Vel);
        }
        if (broke == 0) return;
        // Rotten wood gives way, but every span still takes a bite out of the speed and shoves
        // the nose a little. The Pyatyorka is lower than the top rail, so the body pitches too.
        float keep = MathF.Pow(SpeedKeep, broke);
        car.Vel = new Vector3(car.Vel.X * keep, car.Vel.Y, car.Vel.Z * keep);
        car.AngVel += car.Rot * new Vector3(_rng.RandfRange(-0.4f, 0.1f), _rng.RandfRange(-0.5f, 0.5f), _rng.RandfRange(-0.3f, 0.3f)) * broke;
        CrackStrength = Math.Max(CrackStrength, Math.Clamp(speed / 20f, 0.35f, 1f));
    }

    /// <summary>Breaks every intact span within radius of the point; returns how many.</summary>
    public int Smash(Vector2 p, float radius, Vector3 vel)
    {
        if (_replaying) return 0;
        int broke = 0;
        foreach (var s in _spans)
        {
            if (s.Broken) continue;
            if (DistToSegment(p, s.A, s.B) > radius + 0.12f) continue;
            Break(s, p, vel);
            broke++;
        }
        BrokenThisTick += broke;
        return broke;
    }

    static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float l2 = ab.LengthSquared();
        float t = l2 < 1e-6f ? 0 : Math.Clamp((p - a).Dot(ab) / l2, 0f, 1f);
        return p.DistanceTo(a + ab * t);
    }

    void Break(Span s, Vector2 hit, Vector3 vel, RandomNumberGenerator rng = null)
    {
        rng ??= _rng;
        ulong initialState = rng.State;
        s.Broken = true;
        if (!_replaying) SpanBroken?.Invoke(_spans.IndexOf(s),vel);
        _dirty = true;
        var hv = new Vector3(vel.X, 0, vel.Z);
        float speed = hv.Length();
        foreach (var pc in s.Pieces)
        {
            if (_debris.Count >= MaxDebris) { RemoveChild(_debris[0].Node); _debris[0].Node.QueueFree(); _debris.RemoveAt(0); }
            // Flung along with the car, up and a bit sideways; the faster, the further.
            Vector3 away = pc.Xf.Origin - new Vector3(hit.X, pc.Xf.Origin.Y, hit.Y);
            away = away.LengthSquared() > 1e-4f ? away.Normalized() : Vector3.Up;
            var d = new Debris
            {
                Pos = pc.Xf.Origin,
                Rot = pc.Xf.Basis,
                Vel = hv * rng.RandfRange(0.5f, 0.9f) + away * rng.RandfRange(1f, 3f) + Vector3.Up * rng.RandfRange(2f, 3f + speed * 0.2f),
                AngVel = new Vector3(rng.RandfRange(-1, 1), rng.RandfRange(-1, 1), rng.RandfRange(-1, 1)) * (4f + speed * 0.4f),
                Half = Math.Min(pc.Size.X, Math.Min(pc.Size.Y, pc.Size.Z)) * 0.5f,
                Node = new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = pc.Size },
                    MaterialOverride = new StandardMaterial3D { AlbedoColor = pc.Color, Roughness = 1f, SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled },
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                },
            };
            AddChild(d.Node);
            d.Node.GlobalTransform = pc.Xf;
            _debris.Add(d);

            // Splinters and a puff of old dust from the snapped wood.
            for (int k = 0; k < 6; k++)
                _fx.Burst(pc.Xf.Origin, hv * 0.5f + new Vector3(rng.RandfRange(-3, 3), rng.RandfRange(1.5f, 5f), rng.RandfRange(-3, 3)),
                    Splinters[rng.RandiRange(0, Splinters.Length - 1)], 1f, 0.16f, 0.12f, rng.RandfRange(0.8f, 1.6f), 14f, 0.8f, true);
        }
        Vector3 at = new(hit.X, _t.MeshHeight(hit.X, hit.Y) + 0.6f, hit.Y);
        _fx.Smoke(at, hv * 0.3f + Vector3.Up, new Color(0.7f, 0.63f, 0.5f), 0.45f, 0.6f, 2.2f, 1.2f);
        if (!_replaying) TapeBroken?.Invoke(_spans.IndexOf(s), hit, vel, initialState);
    }

    public void Tick(float dt)
    {
        BrokenThisTick = 0;
        CrackStrength = 0;
        foreach (var d in _debris) Step(d, dt);
    }

    void Step(Debris d, float dt)
    {
        if (d.Resting) return;
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
                // Clatter: bounce, lose most of the speed, tumble on.
                d.Vel = new Vector3(d.Vel.X * 0.5f, -d.Vel.Y * 0.3f, d.Vel.Z * 0.5f);
                d.AngVel *= 0.6f;
            }
            else
            {
                // Fall flat: the longest side lies along the ground.
                d.Vel = new Vector3(d.Vel.X * 0.8f, 0, d.Vel.Z * 0.8f);
                d.AngVel *= 0.8f;
                d.Rot = d.Rot.Slerp(LieFlat(d.Rot), Math.Min(1f, 8f * dt)).Orthonormalized();
                if (d.Vel.LengthSquared() < 0.05f && d.AngVel.LengthSquared() < 0.2f)
                {
                    d.Rot = LieFlat(d.Rot);
                    d.Resting = true;
                }
            }
        }
        d.Node.GlobalTransform = new Transform3D(d.Rot.Orthonormalized(), d.Pos);
    }

    /// <summary>Restore one recorded break without consuming the live fence RNG or sending collision/network callbacks.</summary>
    public void ReplayBreak(int id, Vector2 hit, Vector3 velocity, ulong randomState, float age, float eventTime = 0f)
    {
        if (id < 0 || id >= _spans.Count || _spans[id].Broken) return;
        _replaying = true;
        using var rng = new RandomNumberGenerator(); rng.State = randomState;
        var previous = new HashSet<Debris>(_debris);
        Break(_spans[id], hit, velocity, rng);
        foreach (var d in _debris) if (!previous.Contains(d)) { d.SpawnTime = eventTime; Advance(d, age); }
        Rebuild();
    }

    void Advance(Debris d, float age)
    {
        // Nearest physics grid point: subtracting accumulated float lap clocks can fall just below a whole tick.
        int steps = (int)Math.Clamp(Math.Round(Math.Max(0d, age) * 120d, MidpointRounding.AwayFromZero), 0d, int.MaxValue);
        while (d.ReplaySteps < steps && !d.Resting) { Step(d, 1f / 120f); d.ReplaySteps++; }
        d.ReplaySteps = steps;
    }

    public void ReplayUpdate(float clock)
    {
        _replaying = true;
        foreach (var d in _debris) Advance(d, clock - d.SpawnTime);
    }

    public void ReplayClear() { Reset(); _replaying = false; }

    /// <summary>Nearest orientation with the box's thinnest axis pointing up.</summary>
    internal static Basis LieFlat(Basis r)
    {
        r = r.Orthonormalized();
        // Whichever local axis is closest to vertical goes vertical; keep the heading.
        Vector3[] axes = { r.X, r.Y, r.Z };
        int up = 0;
        for (int i = 1; i < 3; i++) if (Math.Abs(axes[i].Y) > Math.Abs(axes[up].Y)) up = i;
        Vector3 from = axes[up] * Math.Sign(axes[up].Y == 0 ? 1 : axes[up].Y);
        Vector3 axis = from.Cross(Vector3.Up);
        if (axis.LengthSquared() < 1e-8f) return r;
        return (new Basis(axis.Normalized(), from.AngleTo(Vector3.Up)) * r).Orthonormalized();
    }

    public override void _Process(double delta)
    {
        if (_dirty) Rebuild();
    }
}
