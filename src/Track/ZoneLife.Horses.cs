using System;
using System.Collections.Generic;
using CCCPRacing.Car;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Track;

/// <summary>
/// The Przewalski's horses of ATOMGRAD-4: a walk and a trot with real legs (hip and knee per leg, tail, nod), the body following the ground (pitch and roll from four height samples),
/// and a body capsule (tail tip to muzzle) that is kept clear of every static obstacle circle (stone walls, the freight yard's fence rows, trees, buildings) and every fence polyline.
/// </summary>
public partial class ZoneLife
{
    /// <summary>The body capsule on the ground plane, in world metres (the horse is scaled 1.3): the tail tip 1.15 m behind the centre, the muzzle 1.95 m ahead, 0.42 m of half width plus the slack.</summary>
    internal const float HorseTail = 1.15f, HorseMuzzle = 1.95f, HorseBody = 0.42f, HorseSlack = 0.12f;
    static readonly string[] LegNames = { "FL", "FR", "RL", "RR" };

    class Horse
    {
        public Node3D Node, Head, Tail;
        public readonly Node3D[] Hip = new Node3D[4], Shin = new Node3D[4];
        public Vector2 Home, Pos, Graze, Face = new(0, 1);
        public float Run, Phase, Change, Speed, Gait, Move, Blend, Cycle, Pitch, Roll, HeadX;      // Gait: the speed the legs show (it follows Speed, so a sudden stop does not snap them)
        public int Obs;
    }

    readonly List<Horse> _horses = new();
    readonly RandomNumberGenerator _horseRng = new() { Seed = 4242 };
    // Static collision, indexed once: the obstacle circles that existed before the horses (read live: a knocked-down wall segment no longer blocks) and the non-stone fence segments.
    readonly Dictionary<long, List<int>> _horseObsGrid = new();
    readonly Dictionary<long, List<int>> _horseFenceGrid = new();
    readonly List<(Vector2 a, Vector2 b)> _horseFences = new();
    float _horseMaxRadius;
    int _staticObstacles;
    /// <summary>Development switch of the tests: a horse that ignores fences (the old behaviour), to prove the checks can fail.</summary>
    internal bool HorseAvoidance = true;
    static readonly float[] WalkPhase = { 0.25f, 0.75f, 0f, 0.5f }, TrotPhase = { 0f, 0.5f, 0.5f, 0f };      // FL FR RL RR
    static readonly float[] SteerAngles = { 0f, 0.4f, -0.4f, 0.8f, -0.8f, 1.3f, -1.3f, 1.9f, -1.9f };
    const float HorseCell = 8f;

    static long Cell(int cx, int cy) => ((long)cx << 32) ^ (uint)cy;
    static int CellOf(float v) => (int)MathF.Floor(v / HorseCell);

    void BuildHorses()
    {
        if (_def.Horses == null) return;
        IndexHorseObstacles();
        var dun = new Color(0.72f, 0.56f, 0.34f);
        var dark = new Color(0.2f, 0.15f, 0.1f);
        var muzzle = new Color(0.86f, 0.8f, 0.68f);
        for (int k = 0; k < _def.Horses.Count; k++)
        {
            float a = k * MathF.Tau / _def.Horses.Count;
            var home = new Vector2(_def.Horses.X + MathF.Cos(a) * 5f, _def.Horses.Z + MathF.Sin(a) * 4f);
            var n = new Node3D { Name = "Horse" + k, Scale = Vector3.One * 1.3f };
            AddChild(n);
            var h = new Horse { Node = n, Home = home, Pos = home, Graze = home, Phase = k * 1.7f, Obs = _t.Obstacles.Count };
            n.AddChild(Props.Part(Props.Box(0.55f, 0.6f, 1.5f), dun, new Vector3(0, 1.15f, 0)));      // the barrel
            for (int i = 0; i < 4; i++)
            {
                // Hip at the top of the upper leg, the knee 0.46 m below it; both are named tape parts.
                var hip = new Node3D { Name = "Leg" + LegNames[i], Position = new Vector3(i % 2 == 0 ? -0.2f : 0.2f, 0.88f, i < 2 ? -0.55f : 0.55f) };
                n.AddChild(hip);
                hip.AddChild(Props.Part(Props.Box(0.15f, 0.46f, 0.15f), dun, new Vector3(0, -0.23f, 0)));
                var knee = new Node3D { Name = "Shin" + LegNames[i], Position = new Vector3(0, -0.46f, 0) };
                hip.AddChild(knee);
                knee.AddChild(Props.Part(Props.Box(0.11f, 0.4f, 0.11f), dark, new Vector3(0, -0.2f, 0)));
                knee.AddChild(Props.Part(Props.Box(0.14f, 0.06f, 0.16f), dark, new Vector3(0, -0.41f, 0.01f)));   // the hoof
                h.Hip[i] = hip; h.Shin[i] = knee;
            }
            var tail = new Node3D { Name = "Tail", Position = new Vector3(0, 1.4f, 0.75f) };
            n.AddChild(tail);
            tail.AddChild(Props.Part(Props.Box(0.08f, 0.6f, 0.08f), dark, new Vector3(0, -0.28f, 0)));
            h.Tail = tail;
            var head = new Node3D { Position = new Vector3(0, 1.35f, -0.7f) };
            n.AddChild(head);
            head.AddChild(Props.Part(Props.Box(0.3f, 0.75f, 0.32f), dun, new Vector3(0, 0.2f, -0.1f), new Vector3(-35, 0, 0)));     // the neck
            head.AddChild(Props.Part(Props.Box(0.26f, 0.28f, 0.55f), dun, new Vector3(0, 0.5f, -0.45f)));                         // the head
            head.AddChild(Props.Part(Props.Box(0.22f, 0.22f, 0.18f), muzzle, new Vector3(0, 0.45f, -0.75f)));                     // the pale muzzle
            head.AddChild(Props.Part(Props.Box(0.08f, 0.22f, 0.7f), dark, new Vector3(0, 0.5f, 0.05f), new Vector3(-35, 0, 0)));     // the upright mane
            h.Head = head;
            // The side silhouette (added last, so the registered parts keep their places in the hierarchy and old tapes still fit): a raised wither over the shoulders, a deep chest in front of the barrel,
            // and a rounded, sloping croup with a muscled thigh on each side, so the shoulder, the chest and the hindquarters read from a distance as a horse and not a box on legs.
            n.AddChild(Props.Part(Props.Box(0.34f, 0.22f, 0.46f), dun, new Vector3(0, 1.53f, -0.36f)));                              // the wither
            n.AddChild(Props.Part(Props.Box(0.46f, 0.62f, 0.34f), dun, new Vector3(0, 1.04f, -0.8f)));                              // the chest
            n.AddChild(Props.Part(Props.Box(0.52f, 0.5f, 0.56f), dun, new Vector3(0, 1.3f, 0.6f), new Vector3(10, 0, 0)));           // the croup, sloping down to the tail
            foreach (float sx in new[] { -0.2f, 0.2f })
                n.AddChild(Props.Part(Props.Box(0.12f, 0.52f, 0.42f), dun * 0.9f, new Vector3(sx, 1.0f, 0.62f)));                    // the thigh
            _t.Obstacles.Add(new Obstacle(home, 0.8f));
            _horses.Add(h);
        }
    }

    /// <summary>The tape parts of the horses (docs/replay-tape.md): the horse, its head and tail, the four hips and knees; their poses are sampled, none of it is a continuous spinner.</summary>
    void MarkHorseTape()
    {
        for (int i = 0; i < _horses.Count; i++)
        {
            var h = _horses[i];
            Tape.Part(h.Node, $"{i + 1}/horse"); Tape.Part(h.Head, $"{i + 1}/horseHead"); Tape.Part(h.Tail, $"{i + 1}/tail");
            for (int l = 0; l < 4; l++) { Tape.Part(h.Hip[l], $"{i + 1}/leg{LegNames[l]}"); Tape.Part(h.Shin[l], $"{i + 1}/shin{LegNames[l]}"); }
        }
    }

    void IndexHorseObstacles()
    {
        int count = _staticObstacles;      // only the static world: the actors' slots (babushkas, bus, ploughs...) move and come after it
        for (int i = 0; i < count; i++)
        {
            var o = _t.Obstacles[i];
            if (o.Radius <= 0 || o.Velocity != default) continue;
            long key = Cell(CellOf(o.Center.X), CellOf(o.Center.Y));
            if (!_horseObsGrid.TryGetValue(key, out var list)) _horseObsGrid[key] = list = new List<int>();
            list.Add(i);
            _horseMaxRadius = MathF.Max(_horseMaxRadius, o.Radius);
        }
        foreach (var run in _t.Fences)
        {
            if (run.Type == "stone") continue;      // a stone wall already has its circles
            for (int k = 0; k + 1 < run.Points.Count; k++)
            {
                var a = new Vector2(run.Points[k].X, run.Points[k].Z); var b = new Vector2(run.Points[k + 1].X, run.Points[k + 1].Z);
                int idx = _horseFences.Count; _horseFences.Add((a, b));
                int steps = Math.Max(1, (int)MathF.Ceiling(a.DistanceTo(b) / (HorseCell * 0.5f)));
                for (int s = 0; s <= steps; s++)
                {
                    var p = a.Lerp(b, s / (float)steps);
                    long key = Cell(CellOf(p.X), CellOf(p.Y));
                    if (!_horseFenceGrid.TryGetValue(key, out var list)) _horseFenceGrid[key] = list = new List<int>();
                    if (!list.Contains(idx)) list.Add(idx);
                }
            }
        }
    }

    static float DistPointSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a; float l2 = ab.LengthSquared();
        float t = l2 < 1e-9f ? 0f : Math.Clamp((p - a).Dot(ab) / l2, 0f, 1f);
        return p.DistanceTo(a + ab * t);
    }

    static bool SegmentsCross(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        float Cr(Vector2 p, Vector2 q, Vector2 r) => (q.X - p.X) * (r.Y - p.Y) - (q.Y - p.Y) * (r.X - p.X);
        return Cr(a, b, c) * Cr(a, b, d) < 0 && Cr(c, d, a) * Cr(c, d, b) < 0;
    }

    static float DistSegmentSegment(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        => SegmentsCross(a, b, c, d) ? 0f : MathF.Min(MathF.Min(DistPointSegment(a, c, d), DistPointSegment(b, c, d)), MathF.Min(DistPointSegment(c, a, b), DistPointSegment(d, a, b)));

    /// <summary>The clearance (m, negative: overlapping) of the horse body capsule at this place and heading from static circles and fence polylines.</summary>
    internal float HorseClearance(Vector2 pos, Vector2 face)
    {
        Vector2 a = pos - face * HorseTail, b = pos + face * HorseMuzzle;
        float best = float.MaxValue;
        float reach = HorseBody + _horseMaxRadius + 0.5f;
        int x0 = CellOf(MathF.Min(a.X, b.X) - reach), x1 = CellOf(MathF.Max(a.X, b.X) + reach);
        int y0 = CellOf(MathF.Min(a.Y, b.Y) - reach), y1 = CellOf(MathF.Max(a.Y, b.Y) + reach);
        for (int cx = x0; cx <= x1; cx++)
        for (int cy = y0; cy <= y1; cy++)
        {
            if (_horseObsGrid.TryGetValue(Cell(cx, cy), out var list))
                foreach (int i in list)
                {
                    var o = _t.Obstacles[i];
                    if (o.Radius <= 0) continue;
                    best = MathF.Min(best, DistPointSegment(o.Center, a, b) - o.Radius - HorseBody);
                }
            if (_horseFenceGrid.TryGetValue(Cell(cx, cy), out var fl))
                foreach (int i in fl) best = MathF.Min(best, DistSegmentSegment(a, b, _horseFences[i].a, _horseFences[i].b) - 0.1f - HorseBody);
        }
        return best;
    }

    bool HorseFree(Vector2 pos, Vector2 face) => HorseClearance(pos, face) >= HorseSlack;

    bool HorsePathFree(Vector2 from, Vector2 to)
    {
        var d = to - from; float len = d.Length();
        if (len < 0.01f) return HorseFree(to, Vector2.Zero.Equals(d) ? new Vector2(0, 1) : d.Normalized());
        var dir = d / len;
        for (float s = 0.7f; s < len + 0.7f; s += 0.7f) if (!HorseFree(from + dir * MathF.Min(s, len), dir)) return false;
        return true;
    }

    static Vector2 TurnToward(Vector2 face, Vector2 dir, float maxAngle)
    {
        if (dir.LengthSquared() < 1e-6f) return face;
        float d = MathF.Atan2(face.Cross(dir), face.Dot(dir));
        return face.Rotated(Math.Clamp(d, -maxAngle, maxAngle)).Normalized();
    }

    static float TurnRate(float speed) => Mathf.Lerp(1.4f, 4.2f, Math.Clamp(speed / 6f, 0f, 1f));

    /// <summary>
    /// Is the move from (<paramref name="from"/>, <paramref name="fromFace"/>) to (<paramref name="to"/>, <paramref name="toFace"/>) clear all the way, turning included? The body is tested every 0.3 m of travel
    /// and every ~0.15 rad of turn along the move, so neither a big step nor a turn on the spot can carry the muzzle or tail through a fence. A pose that already overlaps (a spawn) may only get clearer, never worse.
    /// </summary>
    bool HorseSweepFree(Vector2 from, Vector2 fromFace, Vector2 to, Vector2 toFace)
    {
        float c0 = HorseClearance(from, fromFace);
        float need = MathF.Min(HorseSlack, c0);
        float turn = MathF.Abs(MathF.Atan2(fromFace.Cross(toFace), fromFace.Dot(toFace)));
        int n = Math.Max(1, Math.Max((int)MathF.Ceiling(from.DistanceTo(to) / 0.3f), (int)MathF.Ceiling(turn / 0.15f)));
        for (int i = 1; i <= n; i++)
        {
            float f = i / (float)n;
            var face = fromFace.Lerp(toFace, f); face = face.LengthSquared() < 1e-6f ? toFace : face.Normalized();
            if (HorseClearance(from.Lerp(to, f), face) < need) return false;
        }
        return true;
    }

    /// <summary>The wanted heading, or a clear one next to it (turning at the horse's own turn rate, looking 0.35 m ahead of the step); zero when nothing is clear.</summary>
    Vector2 SteerClear(Horse h, Vector2 want, float dt)
    {
        if (!HorseAvoidance) return want;
        float side = want.Cross(h.Home - h.Pos) >= 0 ? 1f : -1f;
        float lead = h.Speed * dt + 0.35f, turn = dt * TurnRate(h.Speed);
        foreach (float angle in SteerAngles)
        {
            var cand = want.Rotated(angle * side);
            var face = TurnToward(h.Face, cand, turn);
            if (HorseSweepFree(h.Pos, h.Face, h.Pos + face * lead, face)) return cand;
        }
        return Vector2.Zero;
    }

    Vector2 Separation(Horse h)
    {
        var push = Vector2.Zero;
        foreach (var o in _horses)
        {
            if (o == h) continue;
            var d = h.Pos - o.Pos; float l = d.Length();
            if (l < 2.2f && l > 0.01f) push += d / l * (2.2f - l);
        }
        return push;
    }

    /// <summary>The same start every time: home, a fixed grazing phase, the first heading that is clear of the fences, a standing gait; the horse RNG restarts.</summary>
    void ResetHorses()
    {
        _horseRng.Seed = 4242;
        for (int k = 0; k < _horses.Count; k++)
        {
            var h = _horses[k];
            h.Pos = h.Home; h.Graze = h.Home; h.Run = 0; h.Change = 0; h.Speed = 0; h.Gait = 0; h.Move = 0; h.Blend = 0; h.Cycle = 0; h.Phase = k * 1.7f;
            h.Face = new Vector2(0, 1);
            for (int i = 0; i < 8; i++)
            {
                var f = new Vector2(0, 1).Rotated(i * MathF.PI / 4f);
                if (HorseFree(h.Home, f)) { h.Face = f; break; }
            }
        }
    }

    void TickHorses(float dt, CarSim car)
    {
        var cp = car != null ? new Vector2(car.Pos.X, car.Pos.Z) : new Vector2(-1e4f, -1e4f);
        foreach (var h in _horses)
        {
            h.Phase += dt;
            Vector2 want = h.Face; float target = 0f;
            Vector2 away = h.Pos - cp;
            if (away.Length() < 20f && car != null && car.Speed > 3f)
            {
                // Off at a trot, away from the car, never far from home, not into a neighbour.
                h.Run = 1.5f;
                want = (away.Normalized() + (h.Home - h.Pos) * 0.04f + Separation(h) * 0.5f).Normalized();
                target = 6f;
            }
            else if (h.Run > 0) { h.Run -= dt; target = 3f; }
            else
            {
                // Grazing: a few steps now and then towards a new tuft that can be reached without crossing a fence.
                h.Change -= dt;
                if (h.Change <= 0)
                {
                    h.Change = _horseRng.RandfRange(3f, 7f);
                    var spot = h.Pos;
                    for (int tries = 0; tries < 6; tries++)
                    {
                        var c = h.Home + new Vector2(_horseRng.RandfRange(-6f, 6f), _horseRng.RandfRange(-5f, 5f));
                        if (HorsePathFree(h.Pos, c)) { spot = c; break; }
                    }
                    h.Graze = spot;
                }
                Vector2 to = h.Graze - h.Pos;
                if (to.Length() > 0.5f) { want = to.Normalized(); target = 0.8f; }
            }
            if (target > 0f)
            {
                want = SteerClear(h, want, dt);
                if (want == Vector2.Zero) target = 0f;
            }
            Vector2 oldPos = h.Pos, oldFace = h.Face;      // the last pose that was accepted
            h.Face = TurnToward(h.Face, target > 0f ? want : Vector2.Zero, dt * TurnRate(h.Speed));
            float align = target > 0f ? MathF.Max(0f, h.Face.Dot(want)) : 0f;
            float wanted = target * (0.25f + 0.75f * align);
            h.Speed = Mathf.MoveToward(h.Speed, wanted, (wanted > h.Speed ? 7f : 10f) * dt);
            Vector2 next = h.Pos + h.Face * h.Speed * dt;
            // The whole move (step and turn) from the last accepted pose must be clear; if not the horse stays exactly there.
            if (HorseAvoidance && (h.Speed > 0f || h.Face != oldFace) && !HorseSweepFree(oldPos, oldFace, next, h.Face)) { h.Speed = 0f; h.Face = oldFace; next = oldPos; }
            h.Pos = next;
            Pose(h, dt);
            _t.Obstacles[h.Obs] = new Obstacle(h.Pos + h.Face * 0.4f, 0.8f, h.Face * h.Speed);
        }
    }

    /// <summary>Upper leg 0.46, lower leg and hoof to the sole 0.44: the whole straight leg 0.90 from the hip.</summary>
    const float MaxHalfStep = 0.34f;
    const float UpperLeg = 0.46f, LowerLeg = 0.44f, LegLength = UpperLeg + LowerLeg;

    /// <summary>The ground a stride covers per cycle (m): a pony's short stride, longer the faster it goes.</summary>
    static float Stride(float v) => 1.1f + 0.22f * v;

    /// <summary>A planar two-bone solution for the sole at (fwd, down) from the hip: the hip's angle (+ forward) and how far the knee folds back (never negative); a leg that cannot reach is straight.</summary>
    internal static void LegIk(float fwd, float down, out float hip, out float knee)
    {
        float d = MathF.Sqrt(fwd * fwd + down * down);
        float alpha = MathF.Atan2(fwd, down);
        if (d >= LegLength - 1e-4f) { hip = alpha; knee = 0f; return; }
        float cosKnee = Math.Clamp((UpperLeg * UpperLeg + LowerLeg * LowerLeg - d * d) / (2f * UpperLeg * LowerLeg), -1f, 1f);
        float cosHip = Math.Clamp((UpperLeg * UpperLeg + d * d - LowerLeg * LowerLeg) / (2f * UpperLeg * d), -1f, 1f);
        hip = alpha + MathF.Acos(cosHip);
        knee = MathF.PI - MathF.Acos(cosKnee);
    }

    /// <summary>The gait and the body pose from the horse's speed, its cycle and the ground under it.</summary>
    void Pose(Horse h, float dt)
    {
        h.Gait = dt <= 0f ? h.Speed : Mathf.MoveToward(h.Gait, h.Speed, 14f * dt);
        float v = h.Gait;
        float stride = Stride(v);
        if (v > 0f) h.Cycle += v * dt / stride;      // cycles: a longer stride the faster it goes
        // Walk/trot blend and the legs' amplitude have their own, slower response: a stop or a change of gait moves the legs smoothly instead of re-timing them in a few ticks.
        float gtTarget = Mathf.SmoothStep(1.5f, 4f, v), moveTarget = v > 0.15f ? 1f : 0f;
        h.Blend = dt <= 0f ? gtTarget : Mathf.MoveToward(h.Blend, gtTarget, dt * 2.5f);
        h.Move = dt <= 0f ? moveTarget : Mathf.MoveToward(h.Move, moveTarget, dt * 5f);
        float gt = h.Blend, move = h.Move;
        // The legs by foot placement, not by swinging angles: each hoof is put on the ground plane under the body and carried back at exactly the body's speed through its stance (no sliding, no floating
        // at the ends of the swing), then lifted and brought forward. The stance travel is half a stride, so the body is lowered a little while moving (the legs are nearly straight: a long step
        // needs a bent leg) and a two-bone solution gives the hip and the knee (the knee only folds back, as before; standing, the legs are straight and the angles zero).
        float dm = MathF.Min(stride / 5.2f, MaxHalfStep) * move;      // half the stance travel in the horse's own units (the horse is scaled 1.3); a flat-out run covers less than the body does (a little slide) rather than crouching more
        float lift = (0.05f + 0.06f * gt) * move;
        // The sole's farthest reach from the hip stays 4 % short of the straight leg (a leg right at its full length turns its hip angle violently for a hair's breadth of reach): the body's drop follows from that.
        float reach = LegLength * (1f - 0.04f * MathF.Min(1f, dm / 0.08f));
        float crouch = LegLength - MathF.Sqrt(MathF.Max(0f, reach * reach - dm * dm));
        float dStance = LegLength - crouch;
        for (int i = 0; i < 4; i++)
        {
            float u = h.Cycle + Mathf.Lerp(WalkPhase[i], TrotPhase[i], gt); u -= MathF.Floor(u);
            float fwd, up = 0f;
            if (u >= 0.25f && u < 0.75f) fwd = dm * (1f - 4f * (u - 0.25f));      // stance: backwards, evenly
            else
            {
                float sw = ((u < 0.25f ? u + 1f : u) - 0.75f) * 2f;      // swing 0..1
                fwd = -dm + 2f * dm * sw;      // (the same shape as the stance, so the legs of a pair move exactly half a cycle apart)
                up = lift * MathF.Sin(MathF.PI * sw);
            }
            LegIk(fwd, dStance - up, out float hipAngle, out float knee);
            h.Hip[i].Rotation = new Vector3(hipAngle, 0, 0);
            h.Shin[i].Rotation = new Vector3(-knee, 0, 0);
        }
        bool grazing = h.Speed < 1f && h.Run <= 0f;
        float headX = grazing ? -0.9f + MathF.Sin(h.Phase * 1.3f) * 0.1f : 0.15f + 0.08f * move * MathF.Sin(MathF.Tau * h.Cycle * 2f);
        h.HeadX = dt <= 0f ? headX : Mathf.Lerp(h.HeadX, headX, 1f - MathF.Exp(-dt * 4f));
        h.Head.Rotation = new Vector3(h.HeadX, 0, 0);
        h.Tail.Rotation = new Vector3(Mathf.Lerp(0.3f, 0.9f, gt) + 0.08f * MathF.Sin(h.Phase * 2.1f), 0, 0.25f * MathF.Sin(h.Phase * 3f + h.Cycle * 5f));

        Vector2 f = h.Face, r = new(-f.Y, f.X);
        float hF = _t.MeshHeight(h.Pos.X + f.X * 0.9f, h.Pos.Y + f.Y * 0.9f), hB = _t.MeshHeight(h.Pos.X - f.X * 0.9f, h.Pos.Y - f.Y * 0.9f);
        float hR = _t.MeshHeight(h.Pos.X + r.X * 0.4f, h.Pos.Y + r.Y * 0.4f), hL = _t.MeshHeight(h.Pos.X - r.X * 0.4f, h.Pos.Y - r.Y * 0.4f);
        float pitch = Math.Clamp(MathF.Atan2(hF - hB, 1.8f), -0.45f, 0.45f), roll = Math.Clamp(MathF.Atan2(hR - hL, 0.8f), -0.45f, 0.45f);
        float k = dt <= 0f ? 1f : 1f - MathF.Exp(-dt * 8f);
        h.Pitch = Mathf.Lerp(h.Pitch, pitch, k); h.Roll = Mathf.Lerp(h.Roll, roll, k);
        h.Node.GlobalPosition = new Vector3(h.Pos.X, (hF + hB) * 0.5f - crouch * 1.3f, h.Pos.Y);
        h.Node.Quaternion = new Quaternion(Vector3.Up, MathF.Atan2(-f.X, -f.Y)) * new Quaternion(Vector3.Right, h.Pitch) * new Quaternion(Vector3.Back, h.Roll);
    }
}
