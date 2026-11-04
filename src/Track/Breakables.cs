using System;
using System.Collections.Generic;
using CCCPRacing.Car;
using Godot;

namespace CCCPRacing.Track;

/// <summary>
/// Scenery that gives way (CarSim.BreakObstacle). Trees and telegraph poles take a really hard hit:
/// the car still bounces off, but the tree or pole topples in its direction of travel and a pole
/// pulls its wires down. Tank shells, tank hulls, jet crashes and missiles fell them easily (Blast,
/// Crush). A dry-stone wall crumbles under a hard hit and the car ploughs on through it, slower
/// (StoneWalls). A fallen trunk is not an obstacle, only a jolt when a wheel rolls over it.
/// Everything stands again on a restart.
/// </summary>
public partial class Breakables : Node3D
{
    /// <summary>Impact speeds (m/s, into the object) that break it.</summary>
    public const float SmallTreeBreak = 16f;    // 58 km/h: saplings and slim birches
    public const float PoleBreak = 18f;         // 65 km/h
    public const float BigTreeBreak = 24f;      // 86 km/h
    public const float WallBreak = 11f;         // 40 km/h
    /// <summary>Speed (m/s) the car loses along its travel going through a stone wall, and the kick it gets.</summary>
    public const float WallSpeedLoss = 9f;
    public const float WallJolt = 1.2f;         // m/s up
    public const float WallPitch = 1.0f;        // rad/s nose up
    /// <summary>How close a tank shell must land to fell a tree or pole (m); jet crashes and missiles.</summary>
    public const float ShellReach = 3f;
    public const float BlastReach = 4f;
    /// <summary>Trees smaller than this (Props size) are small.</summary>
    public const float SmallTreeSize = 0.9f;
    /// <summary>Share of the impact the car still takes (damage, sound) when the object breaks.</summary>
    public const float ImpactShare = 0.6f;

    const float FallTime = 1.1f;               // base to ground, ease-in
    const float BounceTime = 0.9f;             // the small bounce after it lands
    const float SmashReach = 0.5f;             // replay: how close the recorded car must be to what it hit
    const int VelHistory = 8;                  // replay: the approach speed is taken this many ticks back
    const float SmashDepth = 0.4f;             // replay: how deep inside a wall the car must be to have gone through
    const float SparkTime = 2.5f;              // a dropped power line keeps crackling this long

    public static float BreakSpeed(string type, float size) => size > 0 && size < SmallTreeSize ? SmallTreeBreak : BigTreeBreak;

    class Faller
    {
        public string Type;
        public int Obstacle;
        public Node3D Node;
        public Transform3D Orig;
        public float Top, Rest, Log;
        public readonly List<Span> Spans = new();
        public bool Down, Landed, Settled;
        public float T, EndAngle;
        public Vector3 Axis, Dir;
    }

    class Span
    {
        public MeshInstance3D Node;
        public Mesh Orig;
        public int A, B;
        public bool Down, Settled;
    }

    TrackData _t;
    TyreEffects _fx;
    StoneWalls _walls;
    readonly Dictionary<int, Faller> _byObstacle = new();
    readonly Dictionary<int, Faller> _byProp = new();
    readonly Dictionary<int, int> _wallByObstacle = new();      // obstacle index -> WallSegments index
    readonly List<int> _breakable = new();                      // every breakable obstacle, for the replay
    readonly List<Faller> _down = new();
    readonly List<Span> _spansDown = new();
    readonly Dictionary<int, Obstacle> _removed = new();
    readonly Dictionary<int, Func<Vector3, bool>> _knocks = new();      // people and signs that fall over (Knockdown)

    /// <summary>An actor's person or sign: hit, it falls over (true) and moves its obstacle out of the way itself.</summary>
    public void AddKnock(int obstacle, Func<Vector3, bool> hit) => _knocks[obstacle] = hit;
    readonly List<(Vector2 a, Vector2 b, float h)> _logs = new();
    readonly Queue<Vector3> _vels = new();
    Vector2 _prevFront, _prevRear;
    bool _havePrev;
    readonly RandomNumberGenerator _rng = new() { Seed = 1937 };

    /// <summary>What broke since the last Tick (for sound, camera and messages): "birch", "spruce", "pine", "pole", "wall" or null.</summary>
    public event Action<int,Vector3,Vector3> Broken;
    public string BrokeKind { get; private set; }
    public float CrackStrength { get; private set; }
    /// <summary>Hay bales knocked over so far, and where the last one stood (the lens's straw).</summary>
    public int HayKnocks { get; private set; }
    public Vector3 HayAt { get; private set; }
    /// <summary>People and signs knocked over since the last Tick (the militia, the liaison officers, the workers, the men at the stop, the drunks...), and the fastest car speed (m/s) that did it.</summary>
    public int KnockedThisTick { get; private set; }
    public float KnockSpeed { get; private set; }
    /// <summary>A fallen tree landing, or a wheel going over a trunk, 0..1.</summary>
    public float ThumpStrength { get; private set; }

    static readonly Color[] BirchLeaves = { new(0.36f, 0.52f, 0.24f), new(0.28f, 0.42f, 0.2f), new(0.45f, 0.58f, 0.26f) };
    static readonly Color[] Needles = { new(0.13f, 0.27f, 0.16f), new(0.17f, 0.33f, 0.19f), new(0.21f, 0.34f, 0.19f) };
    static readonly Color[] Splinters = { new(0.88f, 0.87f, 0.82f), new(0.75f, 0.64f, 0.46f), new(0.45f, 0.35f, 0.22f) };
    static readonly Color[] Sparks = { new(1f, 0.95f, 0.6f), new(1f, 0.75f, 0.3f), new(0.8f, 0.9f, 1f) };

    public void Init(TrackData t, TyreEffects fx, Node3D props, StoneWalls walls)
    {
        _t = t;
        _fx = fx;
        _walls = walls;
        foreach (var c in props.GetChildren())
        {
            if (c is not Node3D n || !n.HasMeta("top") || !n.HasMeta("prop")) continue;
            int prop = n.GetMeta("prop").AsInt32();
            if (!t.PropObstacle.TryGetValue(prop, out int obs)) continue;
            var f = new Faller
            {
                Type = t.Props[prop].Type,
                Obstacle = obs,
                Node = n,
                Orig = n.Transform,
                Top = n.GetMeta("top").AsSingle(),
                Rest = n.GetMeta("rest").AsSingle(),
                Log = n.GetMeta("log").AsSingle(),
            };
            _byObstacle[obs] = f;
            _byProp[prop] = f;
            _breakable.Add(obs);
        }
        if (props.GetNodeOrNull("PowerLines") is Node lines)
            foreach (var c in lines.GetChildren())
            {
                if (c is not MeshInstance3D mi || !mi.HasMeta("poles")) continue;
                var ab = mi.GetMeta("poles").AsVector2I();
                var s = new Span { Node = mi, Orig = mi.Mesh, A = ab.X, B = ab.Y };
                if (_byProp.TryGetValue(s.A, out var fa)) fa.Spans.Add(s);
                if (_byProp.TryGetValue(s.B, out var fb)) fb.Spans.Add(s);
            }
        for (int i = 0; i < t.WallSegments.Count; i++)
        {
            var ws = t.WallSegments[i];
            for (int k = ws.First; k < ws.First + ws.Count; k++)
            {
                _wallByObstacle[k] = i;
                _breakable.Add(k);
            }
        }
    }

    /// <summary>Raised by Reset: other breakable scenery (the guardrails) stands again too.</summary>
    public event Action Restarted;

    /// <summary>Everything stands again (race restart).</summary>
    public void Reset()
    {
        Restarted?.Invoke();
        foreach (var f in _down)
        {
            f.Node.Transform = f.Orig;
            f.Down = f.Landed = f.Settled = false;
            f.T = 0;
        }
        _down.Clear();
        foreach (var s in _spansDown)
        {
            s.Node.Mesh = s.Orig;
            s.Down = s.Settled = false;
        }
        _spansDown.Clear();
        foreach (var (i, o) in _removed) _t.Obstacles[i] = o;
        _removed.Clear();
        _logs.Clear();
        _walls?.Reset();
        _havePrev = false;
        _vels.Clear();
    }

    /// <summary>
    /// CarSim.BreakObstacle: obstacle i was hit at this speed by a car moving at vel. Breaks it and
    /// returns true if it is breakable (the threshold is the obstacle's BreakAt).
    /// </summary>
    public bool TryBreak(int i, float impact, Vector3 vel, Vector3 carPos)
    {
        if (_removed.ContainsKey(i)) return false;
        if (_knocks.TryGetValue(i, out var knock))
        {
            bool down = knock(vel);
            if (down) { KnockedThisTick++; KnockSpeed = Math.Max(KnockSpeed, new Vector2(vel.X, vel.Z).Length()); }
            return down;
        }
        if (_byObstacle.TryGetValue(i, out var f))
        {
            Fell(f, vel);
            BrokeKind = f.Type;
            if (f.Type == "hay") { HayKnocks++; HayAt = f.Node.GlobalPosition; }
        }
        else if (_wallByObstacle.TryGetValue(i, out int seg) && _walls != null)
        {
            Vector2 hit = _t.Obstacles[i].Center;
            BreakWall(seg, hit, vel);
            // ...and whatever else of the wall is in the car's way, so it doesn't stick in the gap.
            var p = new Vector2(carPos.X, carPos.Z);
            var dir = new Vector2(vel.X, vel.Z);
            if (dir.LengthSquared() > 0.01f)
            {
                dir = dir.Normalized();
                for (int s = 0; s < _t.WallSegments.Count; s++)
                {
                    var ws = _t.WallSegments[s];
                    for (int k = ws.First; k < ws.First + ws.Count; k++)
                    {
                        var o = _t.Obstacles[k];
                        Vector2 d = o.Center - hit;
                        if (d.LengthSquared() > 16f) continue;
                        Vector2 rel = o.Center - p;
                        float across = MathF.Abs(rel.X * dir.Y - rel.Y * dir.X);
                        if (across < CarSim.CollisionRadius + o.Radius - 0.05f && rel.Dot(dir) > -1f) { BreakWall(s, o.Center, vel); break; }
                    }
                }
            }
            BrokeKind = "wall";
        }
        else return false;
        CrackStrength = Math.Max(CrackStrength, Math.Clamp(impact / 15f, 0.5f, 1f));
        return true;
    }

    void BreakWall(int seg, Vector2 hit, Vector3 vel)
    {
        var ws = _t.WallSegments[seg];
        if (_removed.ContainsKey(ws.First)) return;
        for (int k = ws.First; k < ws.First + ws.Count; k++) Remove(k);
        _walls.Break(seg, hit, vel);
        if (!_replay) Broken?.Invoke(ws.First,vel,new Vector3(hit.X,_t.MeshHeight(hit.X,hit.Y),hit.Y));
    }

    // ---- the world tape's replay: what the recorded lap broke, and when (docs/replay-tape.md, phase 3) ----
    bool _replay;      // the replay's breaking is not news: no Broken event (it would be recorded, sent to a LAN...)

    /// <summary>Test: the base of the standing tree or pole nearest to <paramref name="from"/> (a blast there fells it).</summary>
    public Vector3? NearestStanding(Vector3 from)
    {
        Vector3? best = null; float bestD = float.MaxValue;
        foreach (var f in _byProp.Values)
        {
            if (f.Down) continue;
            float d = f.Orig.Origin.DistanceSquaredTo(from);
            if (d < bestD) { bestD = d; best = f.Orig.Origin; }
        }
        return best;
    }

    /// <summary>Test: how many trees and poles lie where they fell (their node is not where it stood).</summary>
    public int LyingCount { get { int n = 0; foreach (var f in _byObstacle.Values) if (f.Down && !f.Node.Transform.IsEqualApprox(f.Orig)) n++; return n; } }

    /// <summary>How many things are broken now (trees and poles down, wall segments crumbled).</summary>
    public int BrokenCount { get { int n = 0; foreach (var f in _byObstacle.Values) if (_removed.ContainsKey(f.Obstacle)) n++; for (int i = 0; i < _t.WallSegments.Count; i++) if (_removed.ContainsKey(_t.WallSegments[i].First)) n++; return n; } }

    /// <summary>The ids (as <see cref="Broken"/> reports them) of everything that is broken now: a lap that starts with some broken records them.</summary>
    public void BrokenIds(List<int> into)
    {
        into.Clear();
        foreach (var f in _byObstacle.Values) if (_removed.ContainsKey(f.Obstacle)) into.Add(f.Obstacle);
        for (int i = 0; i < _t.WallSegments.Count; i++) if (_removed.ContainsKey(_t.WallSegments[i].First)) into.Add(_t.WallSegments[i].First);
    }

    /// <summary>Replay: what was broken at <paramref name="id"/> breaks now, as it did then (the tree topples with its leaves, the wall crumbles). Nothing happens if it is broken or unknown.
    /// <paramref name="age"/> is how long ago it broke (the player fires an event up to one step late): the tree is that far into its fall, as a seek to the same clock would show it.</summary>
    public void ReplayBreak(int id, Vector3 vel, Vector3 at, float age = 0f)
    {
        _replay = true;
        try
        {
            if (_removed.ContainsKey(id)) return;
            if (_byObstacle.TryGetValue(id, out var f)) { Fell(f, vel); if (age > 0f) FallTo(f, age); }
            else if (_wallByObstacle.TryGetValue(id, out int seg) && _walls != null) BreakWall(seg, new Vector2(at.X, at.Z), vel);
        }
        finally { _replay = false; }
    }

    /// <summary>The tree or pole that has just started to fall is <paramref name="age"/> seconds into it: the angle, the landing (its log to bump over) and the settling of <see cref="Tick"/>, and its wires, as the ticks would have left them.</summary>
    void FallTo(Faller f, float age)
    {
        f.T = age;
        f.Node.Transform = new Transform3D(new Basis(f.Axis, FallAngle(age, f.EndAngle)) * f.Orig.Basis, f.Orig.Origin);
        if (age >= FallTime) Land(f);
        f.Settled = age > FallTime + BounceTime;
        foreach (var s in f.Spans) DropWires(s);
    }

    /// <summary>Test (the seek probe): where every felled tree or pole's model is now (obstacle id, transform, and how far it has turned from standing), in id order.</summary>
    public List<(int Id, Transform3D Pose, float Angle)> FallenPoses()
    {
        var l = new List<(int, Transform3D, float)>();
        foreach (var f in _byObstacle.Values) if (f.Down) l.Add((f.Obstacle, f.Node.Transform, (f.Orig.Basis.Inverse() * f.Node.Transform.Basis).GetRotationQuaternion().GetAngle()));
        l.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        return l;
    }

    /// <summary>The age (s) of the fall of a broken tree or pole, as far as the world knows it: how far into its fall or bounce it was (a settled one reports where it stopped counting, over two seconds); 100 for a wall or anything else.</summary>
    public float BreakAge(int id) => _byObstacle.TryGetValue(id, out var f) && f.Down ? f.T : 100f;

    /// <summary>
    /// Replay (a seek): the world shows exactly <paramref name="ids"/> broken, each as far into its fall as <paramref name="ages"/> says (seconds since it broke; missing: settled, lying where it ended up),
    /// <paramref name="how"/> says which way it went down and where it was hit, as the tape recorded, and everything else standing. Whatever the previous pass broke and is not in the set stands
    /// again. The fall of a tree is seekable (its angle, landing and wires follow the age, so a seek equals playing to the same clock); a wall that crumbled is not: it is rubble at once.
    /// </summary>
    public void ReplaySet(IReadOnlyCollection<int> ids, IReadOnlyDictionary<int, (Vector3 Vel, Vector3 At)> how, IReadOnlyDictionary<int, float> ages = null)
    {
        if (_t == null) return;
        _replay = true;
        var fx = _fx; _fx = null;      // (no sparks and leaves for something that fell long ago)
        try
        {
            Reset();
            bool walls = false;
            foreach (int id in ids)
            {
                if (_byObstacle.TryGetValue(id, out var f) && !f.Down)
                {
                    Fell(f, how != null && how.TryGetValue(id, out var h) ? h.Vel : f.Orig.Basis.Z * -3f);
                    FallTo(f, ages != null && ages.TryGetValue(id, out float a) ? Math.Max(0f, a) : FallTime + BounceTime + 1f);
                }
                else if (_wallByObstacle.TryGetValue(id, out int seg) && _walls != null) { var c = how != null && how.TryGetValue(id, out var h) ? new Vector2(h.At.X, h.At.Z) : _t.Obstacles[id].Center; BreakWall(seg, c, how != null && how.TryGetValue(id, out h) ? h.Vel : Vector3.Zero); walls = true; }
            }
            if (walls) _walls.Settle();
        }
        finally { _fx = fx; _replay = false; }
    }

    /// <summary>
    /// A shell, crash or missile going off at p: trees and poles within reach of it topple away
    /// from the blast. Returns how many fell.
    /// </summary>
    public int Blast(Vector3 p, float reach)
    {
        int n = 0;
        var c = new Vector2(p.X, p.Z);
        foreach (var f in _byProp.Values)
        {
            if (f.Down) continue;
            var o = _t.Obstacles[f.Obstacle];
            Vector2 d = o.Center - c;
            if (d.Length() > reach + o.Radius) continue;
            Vector2 away = d.LengthSquared() > 1e-4f ? d.Normalized() : Vector2.Right;
            Fell(f, new Vector3(away.X, 0, away.Y) * 8f);
            n++;
        }
        return n;
    }

    /// <summary>A moving hull (a tank) pushes over any tree or pole it touches. Returns how many fell.</summary>
    public int Crush(Obstacle hull)
    {
        int n = 0;
        foreach (var f in _byProp.Values)
        {
            if (f.Down) continue;
            var o = _t.Obstacles[f.Obstacle];
            Vector2 d = o.Center - hull.Center;
            float r = hull.Radius + o.Radius;
            if (d.LengthSquared() > r * r) continue;
            Vector2 dir = hull.Velocity.LengthSquared() > 0.25f ? hull.Velocity.Normalized() : d.Normalized();
            Fell(f, new Vector3(dir.X, 0, dir.Y) * 5f);
            n++;
        }
        return n;
    }

    /// <summary>Base of the standing tree nearest to the line of fire from a to b (short of b, clear of it) that suits (tests).</summary>
    public Vector2? TreeInLine(Vector2 a, Vector2 b, Func<Vector2, bool> suits)
    {
        Vector2? best = null;
        float bestD = 10f;
        Vector2 ab = b - a;
        foreach (var f in _byProp.Values)
        {
            if (f.Down || f.Type == "pole") continue;
            var p = new Vector2(f.Orig.Origin.X, f.Orig.Origin.Z);
            float t = (p - a).Dot(ab) / ab.LengthSquared();
            float d = p.DistanceTo(a + ab * t);
            if (t < 0f || t > 1.3f || p.DistanceTo(a) < 12f || p.DistanceTo(b) < 8f || d >= bestD || !suits(p)) continue;
            best = p;
            bestD = d;
        }
        return best;
    }

    void Remove(int i)
    {
        if (_removed.ContainsKey(i)) return;
        _removed[i] = _t.Obstacles[i];
        _t.Obstacles[i] = new Obstacle(new Vector2(-1e4f, -1e4f), 0.1f);
    }

    /// <summary>
    /// Replay (the title demo): the record lap stores only how hard each hit was. On a hit, whatever
    /// breakable thing the car is touching and was coming at fast enough (velocity a few ticks
    /// back) must have broken when the lap was driven, and did the recorded share of the blow.
    /// </summary>
    public void Smash(CarSim car, float impact)
    {
        _vels.Enqueue(car.Vel);
        if (_vels.Count > VelHistory) _vels.Dequeue();
        if (impact <= 0 && car.Speed < WallBreak) return;
        Vector3 v0 = _vels.Peek();
        foreach (float cz in CarSim.CollisionCircleZ)
        {
            Vector3 c3 = car.Pos + car.Rot * new Vector3(0, 0, cz);
            var c = new Vector2(c3.X, c3.Z);
            foreach (int i in _breakable)
            {
                var o = _t.Obstacles[i];
                Vector2 d = c - o.Center;
                float reach = CarSim.CollisionRadius + o.Radius;
                float d2 = d.LengthSquared();
                if (d2 > (reach + SmashReach) * (reach + SmashReach) || d2 < 1e-6f) continue;
                // A wall the car went through: it is right inside it (a wall it bounced off, never).
                if (o.Crumbles)
                {
                    if (d2 < (reach - SmashDepth) * (reach - SmashDepth)) TryBreak(i, WallBreak, car.Vel, car.Pos);
                    continue;
                }
                float vn = -new Vector3(d.X, 0, d.Y).Normalized().Dot(v0);
                if (vn >= o.BreakAt && impact >= o.BreakAt * ImpactShare * 0.8f) TryBreak(i, vn, v0, car.Pos);
            }
        }
    }

    /// <summary>The tree or pole starts to topple along the car's travel.</summary>
    void Fell(Faller f, Vector3 vel)
    {
        Remove(f.Obstacle);
        if (!_replay) Broken?.Invoke(f.Obstacle,vel,f.Orig.Origin);
        var hv = new Vector3(vel.X, 0, vel.Z);
        f.Dir = hv.LengthSquared() > 0.01f ? hv.Normalized() : f.Orig.Basis.Z;
        f.Axis = Vector3.Up.Cross(f.Dir).Normalized();
        Vector3 b = f.Orig.Origin;
        Vector3 tip = b + f.Dir * f.Top;
        // Lies down with its top resting on the ground there (a slope, the crown, the crossarm).
        float rise = _t.MeshHeight(tip.X, tip.Z) + f.Rest - b.Y;
        f.EndAngle = Math.Clamp(MathF.PI / 2f - MathF.Atan2(rise, f.Top), 1.0f, 1.8f);
        f.Down = true;
        f.T = 0;
        _down.Add(f);
        foreach (var s in f.Spans)
        {
            if (!s.Down) { s.Down = true; _spansDown.Add(s); }
            s.Settled = false;
        }
        if (_fx == null) return;

        Vector3 snap = b + Vector3.Up * 0.5f;
        for (int k = 0; k < 10; k++)
            _fx.Burst(snap, hv * 0.4f + new Vector3(_rng.RandfRange(-3, 3), _rng.RandfRange(1.5f, 4.5f), _rng.RandfRange(-3, 3)),
                f.Type == "birch" ? Splinters[_rng.RandiRange(0, 2)] : Splinters[_rng.RandiRange(1, 2)], 1f, 0.14f, 0.1f, _rng.RandfRange(0.8f, 1.5f), 14f, 0.8f, true);
        if (f.Type == "pole")
        {
            // The wires snap at the top.
            Vector3 top = f.Orig * new Vector3(0, 6.8f, 0);
            SparkBurst(top, 24);
        }
        else Leaves(f, f.Orig * new Vector3(0, f.Top, 0), hv * 0.3f, 22);
    }

    void Leaves(Faller f, Vector3 at, Vector3 vel, int n)
    {
        var cols = f.Type == "birch" ? BirchLeaves : Needles;
        for (int k = 0; k < n; k++)
            _fx.Burst(at + new Vector3(_rng.RandfRange(-1, 1), _rng.RandfRange(-0.8f, 0.8f), _rng.RandfRange(-1, 1)),
                vel + new Vector3(_rng.RandfRange(-3.5f, 3.5f), _rng.RandfRange(0.5f, 4f), _rng.RandfRange(-3.5f, 3.5f)),
                cols[_rng.RandiRange(0, cols.Length - 1)], 1f, 0.32f, 0.2f, _rng.RandfRange(1f, 1.8f), 4f, 1.6f, true);
    }

    void SparkBurst(Vector3 at, int n)
    {
        for (int k = 0; k < n; k++)
            _fx.Burst(at, new Vector3(_rng.RandfRange(-5, 5), _rng.RandfRange(0f, 6f), _rng.RandfRange(-5, 5)),
                Sparks[_rng.RandiRange(0, Sparks.Length - 1)], 1f, 0.16f, 0.04f, _rng.RandfRange(0.25f, 0.6f), 12f, 0.5f, false);
    }

    /// <summary>Angle from upright at time t: ease-in down, then a small bounce.</summary>
    static float FallAngle(float t, float end)
    {
        if (t < FallTime)
        {
            float u = t / FallTime;
            return end * u * u;
        }
        float b = t - FallTime;
        return end - 0.12f * MathF.Abs(MathF.Sin(b * MathF.PI / 0.3f)) * MathF.Exp(-b * 5f);
    }

    public void Tick(float dt, CarSim car, bool jolts)
    {
        BrokeKind = null;
        KnockedThisTick = 0;
        KnockSpeed = 0;
        CrackStrength = 0;
        ThumpStrength = 0;
        foreach (var f in _down)
        {
            if (f.Settled) continue;
            f.T += dt;
            float a = FallAngle(f.T, f.EndAngle);
            f.Node.Transform = new Transform3D(new Basis(f.Axis, a) * f.Orig.Basis, f.Orig.Origin);
            if (!f.Landed && f.T >= FallTime) Land(f);
            if (f.Type == "pole" && f.Landed && f.T < FallTime + SparkTime && _fx != null && _rng.Randf() < 0.25f)
                SparkBurst(f.Node.Transform * new Vector3(0, 6.7f, 0), _rng.RandiRange(1, 4));
            if (f.T > FallTime + BounceTime) f.Settled = true;
        }
        foreach (var s in _spansDown)
            if (!s.Settled) DropWires(s);
        _walls?.Tick(dt);
        if (car != null && jolts) Jolts(car);
        else _havePrev = false;
    }

    /// <summary>Crashes down: leaves and dust fly, the trunk becomes a log to bump over.</summary>
    void Land(Faller f)
    {
        f.Landed = true;
        Vector3 b = f.Orig.Origin;
        var b2 = new Vector2(b.X, b.Z);
        var d2 = new Vector2(f.Dir.X, f.Dir.Z);
        _logs.Add((b2 + d2 * 0.6f, b2 + d2 * f.Top * 0.9f, f.Log));
        ThumpStrength = Math.Max(ThumpStrength, Math.Clamp(f.Top / 9f, 0.35f, 1f));
        if (_fx == null) return;
        Vector3 top = f.Node.Transform * new Vector3(0, f.Top, 0);
        if (f.Type == "pole") SparkBurst(top, 30);
        else Leaves(f, top, Vector3.Zero, 30);
        for (int k = 0; k < 3; k++)
        {
            Vector3 at = b + f.Dir * f.Top * (0.3f + 0.3f * k);
            at.Y = _t.MeshHeight(at.X, at.Z) + 0.3f;
            _fx.Smoke(at, Vector3.Up * 0.5f, new Color(0.62f, 0.56f, 0.44f), 0.45f, 0.8f, 2.6f, 1.4f);
        }
    }

    /// <summary>Wires of a span with a broken pole: they go slack and sag down onto the grass.</summary>
    void DropWires(Span s)
    {
        _byProp.TryGetValue(s.A, out var fa);
        _byProp.TryGetValue(s.B, out var fb);
        float Progress(Faller f) => f is { Down: true } ? Math.Min(1f, f.T / FallTime) : 0f;
        float slack = Math.Max(Progress(fa), Progress(fb));
        Vector3 origin = s.Node.Position;
        Transform3D xa = fa?.Node.Transform ?? new Transform3D(new Basis(Vector3.Up, _t.Props[s.A].Rotation), _t.Props[s.A].Position);
        Transform3D xb = fb?.Node.Transform ?? new Transform3D(new Basis(Vector3.Up, _t.Props[s.B].Rotation), _t.Props[s.B].Position);
        float len = new Vector2(xa.Origin.X - xb.Origin.X, xa.Origin.Z - xb.Origin.Z).Length();
        const int Segs = 16;
        int wires = Props.WireCount(_t, s.A, s.B);
        var verts = new Vector3[wires * Segs * 2];
        int v = 0;
        for (int w = 0; w < wires; w++)
        {
            Vector3 from = xa * Props.WireAttachment(_t, s.A, w), to = xb * Props.WireAttachment(_t, s.B, w);
            float sag = Mathf.Lerp(Props.WireSag(s.A, w, len), len * 0.3f, slack);
            Vector3 prev = Vector3.Zero;
            for (int k = 0; k <= Segs; k++)
            {
                float f = k / (float)Segs;
                Vector3 cur = from.Lerp(to, f) - new Vector3(0, 4f * sag * f * (1 - f), 0);
                cur.Y = Math.Max(cur.Y, _t.MeshHeight(cur.X, cur.Z) + 0.05f);
                cur -= origin;
                if (k > 0) { verts[v++] = prev; verts[v++] = cur; }
                prev = cur;
            }
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, arrays);
        s.Node.Mesh = mesh;
        s.Settled = (fa == null || fa.Settled || !fa.Down) && (fb == null || fb.Settled || !fb.Down);
    }

    /// <summary>A wheel pair rolling over a fallen trunk: a kick up and a pitch, no collision.</summary>
    void Jolts(CarSim car)
    {
        Vector3 f3 = car.Pos + car.Rot * new Vector3(0, 0, -1.25f), r3 = car.Pos + car.Rot * new Vector3(0, 0, 1.25f);
        Vector2 front = new(f3.X, f3.Z), rear = new(r3.X, r3.Z);
        if (_havePrev && _logs.Count > 0 && front.DistanceSquaredTo(_prevFront) < 25f)
        {
            float speed = new Vector2(car.Vel.X, car.Vel.Z).Length();
            foreach (var (a, b, h) in _logs)
                for (int axle = 0; axle < 2; axle++)
                {
                    bool isFront = axle == 0;
                    if (!Crosses(isFront ? _prevFront : _prevRear, isFront ? front : rear, a, b)) continue;
                    float k = Math.Clamp(speed * h * 0.35f, 0.2f, 2.2f);
                    car.Vel = new Vector3(car.Vel.X * (1f - 0.1f * h), car.Vel.Y + k * 0.5f, car.Vel.Z * (1f - 0.1f * h));
                    car.AngVel += car.Rot * new Vector3(isFront ? k * 0.8f : -k * 0.6f, 0, _rng.RandfRange(-0.3f, 0.3f) * k);
                    ThumpStrength = Math.Max(ThumpStrength, Math.Clamp(0.3f + k * 0.3f, 0f, 1f));
                }
        }
        _prevFront = front;
        _prevRear = rear;
        _havePrev = true;
    }

    /// <summary>Whether segments p0-p1 and a-b cross.</summary>
    static bool Crosses(Vector2 p0, Vector2 p1, Vector2 a, Vector2 b)
    {
        static float Cross(Vector2 u, Vector2 v) => u.X * v.Y - u.Y * v.X;
        Vector2 r = p1 - p0, s = b - a;
        float den = Cross(r, s);
        if (MathF.Abs(den) < 1e-6f) return false;
        float t = Cross(a - p0, s) / den, u = Cross(a - p0, r) / den;
        return t >= 0 && t <= 1 && u >= 0 && u <= 1;
    }
}
