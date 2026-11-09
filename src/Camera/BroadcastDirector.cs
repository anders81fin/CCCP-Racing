using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using CCCPRacing.Track;

namespace CCCPRacing.Camera;

/// <summary>
/// The replay's automatic broadcast: a rally TV director that cuts a lap the way a WRC highlights film does. The whole lap is planned
/// once from the recorded path (where it jumps, slides, wades, turns hard and runs flat out), then every frame is a pure function of
/// the replay clock, so a seek, a pause and the movie export all see the same picture. Cameras: the game's own top-down view (home),
/// the bonnet, a close chase, a low spoiler camera behind the car, a helicopter circling it, and the fixed cameras of the
/// stages: a roadside telephoto on the outside of a corner (it pans with the car and zooms to hold its size), a head-on camera down
/// the road, a wide low fly-by, a camera at the lip of a jump and one on the bank of the ford. Cuts come on the action, never
/// twice the same kind in a row, and no shot is shorter than about two seconds.
/// </summary>
internal sealed class BroadcastDirector
{
    internal enum Kind { TopDown, Bonnet, Chase, Spoiler, Helicopter, Roadside, HeadOn, Fly, Jump, Splash }
    internal readonly record struct Segment(float Start, float End, Kind Kind, Vector3 Eye, int Seed, string Why);
    internal readonly record struct View(Kind Kind, int Index, Vector3 Eye, Vector3 Target, float Fov, float Near);

    public const float MinShot = 2.0f, SampleStep = 0.1f;

    TrackData _track;
    Func<float, Transform3D> _sample;
    float _duration, _maxSpeed;
    object _key;
    readonly List<Segment> _plan = new();

    internal IReadOnlyList<Segment> Plan => _plan;
    internal bool Planned(object key) => _key != null && Equals(_key, key);
    internal void Reset() { _key = null; _plan.Clear(); }

    static bool Fixed(Kind k) => k is Kind.Roadside or Kind.HeadOn or Kind.Fly or Kind.Jump or Kind.Splash;

    // ---------------------------------------------------------------- what stands in the way
    // The taiga is scenery only (no obstacle circles) and a snow bank is not terrain: both hide the car from a low camera, so the director looks for them itself.
    const float CrownRadius = 1.7f, TreeHeight = 8f, CellSize = 8f;
    Dictionary<(int, int), List<int>> _forest;

    void IndexForest()
    {
        _forest = new();
        for (int i = 0; i < _track.Taiga.Count; i++)
        {
            var p = _track.Taiga[i].Pos;
            var key = ((int)MathF.Floor(p.X / CellSize), (int)MathF.Floor(p.Z / CellSize));
            if (!_forest.TryGetValue(key, out var list)) _forest[key] = list = new List<int>();
            list.Add(i);
        }
    }

    /// <summary>The point is inside the crown of a taiga tree (a cylinder of the tree's size).</summary>
    bool InForest(Vector3 p, float margin = .5f)
    {
        if (_forest == null || _forest.Count == 0) return false;
        int cx = (int)MathF.Floor(p.X / CellSize), cz = (int)MathF.Floor(p.Z / CellSize);
        for (int dx = -1; dx <= 1; dx++)
        for (int dz = -1; dz <= 1; dz++)
        {
            if (!_forest.TryGetValue((cx + dx, cz + dz), out var list)) continue;
            foreach (int i in list)
            {
                var (pos, scale, _, _) = _track.Taiga[i];
                float r = CrownRadius * scale + margin;
                if ((pos.X - p.X) * (pos.X - p.X) + (pos.Z - p.Z) * (pos.Z - p.Z) < r * r && p.Y < pos.Y + TreeHeight * scale) return true;
            }
        }
        return false;
    }

    /// <summary>The point lies in a snow bank (below its crest).</summary>
    bool InBank(Vector3 p, float margin = .35f)
    {
        if (_track.Banks == null) return false;
        var q = new Vector2(p.X, p.Z);
        int hint = _track.NearestCenter(q, out float d);
        if (d > _track.Banks.Inner(hint) + Snowbanks.Width + 2f) return false;
        return _track.Banks.Query(q, .15f, ref hint, out _, out float pen, out float top) && p.Y < top + margin;
    }

    /// <summary>The camera at eye sees the target: nothing solid, no hill, no tree crown and no snow bank between.</summary>
    internal bool Visible(Vector3 eye, Vector3 target)
    {
        if (!ReplayCameraDirector.ClearView(_track, eye, target)) return false;
        var aim = target + Vector3.Up;
        float length = eye.DistanceTo(aim);
        int steps = Math.Max(4, (int)(length / 1.5f));
        for (int i = 1; i < steps; i++)
        {
            var p = aim.Lerp(eye, i / (float)steps);
            if (InBank(p) || InForest(p)) return false;
        }
        return true;
    }

    // ---------------------------------------------------------------- the recorded path
    Vector3 P(float t) => _sample(Math.Clamp(t, 0, _duration)).Origin;
    Basis B(float t) => _sample(Math.Clamp(t, 0, _duration)).Basis;
    float Speed(float t) { var d = P(t + .15f) - P(t - .15f); d.Y = 0; return d.Length() / .3f; }
    Vector3 Smooth(float t, float window)
    {
        Vector3 sum = Vector3.Zero; float w = 0;
        for (int i = 0; i <= 6; i++) { float k = 7 - i; sum += P(t - window * i / 6f) * k; w += k; }
        return sum / w;
    }
    /// <summary>The direction of travel (it holds the last one through a stop; the body's front when it never moved).</summary>
    Vector3 Heading(float t, float window = .4f)
    {
        for (int i = 0; i < 40; i++)
        {
            float at = t - i * .25f;
            var d = P(at + window / 2) - P(at - window / 2); d.Y = 0;
            if (d.LengthSquared() > .04f) return d.Normalized();
            if (at <= 0) break;
        }
        var f = -B(t).Z; f.Y = 0;
        return f.LengthSquared() > .001f ? f.Normalized() : Vector3.Forward;
    }
    static Vector3 Right(Vector3 forward) => forward.Cross(Vector3.Up).Normalized();
    float Ground(Vector3 p) => WorldBackdrop.HeightAt(_track, new Vector2(p.X, p.Z));

    // ---------------------------------------------------------------- the plan
    internal void Build(TrackData track, Func<float, Transform3D> sample, float duration, object key)
    {
        _track = track; _sample = sample; _duration = Math.Max(0, duration); _key = key;
        _plan.Clear();
        IndexForest();
        if (_duration < 2 * MinShot) { _plan.Add(new Segment(0, Math.Max(_duration, .1f), Kind.TopDown, default, 0, "short")); return; }
        int n = (int)(_duration / SampleStep) + 1;
        var speed = new float[n]; var height = new float[n]; var slip = new float[n]; var wet = new bool[n]; var turn = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i * SampleStep, v = Speed(t);
            speed[i] = v; _maxSpeed = Math.Max(_maxSpeed, v);
            var p = P(t); height[i] = p.Y - Ground(p);
            wet[i] = _track.Sample(p.X, p.Z).WaterDepth > .08f;
            var vel = P(t + .15f) - P(t - .15f); vel.Y = 0;
            var front = -B(t).Z; front.Y = 0;
            slip[i] = v > 6f && front.LengthSquared() > .001f ? Math.Abs(Mathf.Atan2(front.Cross(vel).Y, front.Dot(vel))) : 0;
            var a = Heading(t - 1f); var b = Heading(t + 1f);
            turn[i] = v > 6f ? a.Cross(b).Y >= 0 ? Mathf.Acos(Math.Clamp(a.Dot(b), -1f, 1f)) : -Mathf.Acos(Math.Clamp(a.Dot(b), -1f, 1f)) : 0;
        }
        var sortedHeights = height.OrderBy(h => h).ToArray();
        float rest = sortedHeights[sortedHeights.Length / 2];

        // Events, with how much each deserves a camera of its own.
        var events = new List<(float T, int Priority, Func<Segment?> Make)>();
        foreach (var (a, b) in Spans(n, i => height[i] > rest + .7f && speed[i] > 6f, 3))
        {
            float t0 = a * SampleStep, t1 = b * SampleStep;
            events.Add((t0, 10, () => JumpShot(t0, t1)));
        }
        foreach (var (a, b) in Spans(n, i => wet[i] && speed[i] > 3f, 4))
        {
            float t0 = a * SampleStep, t1 = b * SampleStep;
            events.Add((t0, 9, () => SplashShot(t0, t1)));
        }
        foreach (var (a, b) in Spans(n, i => slip[i] > .32f, 7))
        {
            float tm = (a + b) / 2f * SampleStep;
            events.Add((tm, 8, () => AnchoredShot(Kind.Roadside, tm, "slide", 0, 7f)));
        }
        int cornerCount = 0;
        for (int i = 2; i < n - 2; i++)      // the hard corners, one per local peak of the turn within two seconds
        {
            float w = Math.Abs(turn[i]);
            if (w < 1.0f) continue;
            bool peak = true;
            for (int k = Math.Max(0, i - 25); k <= Math.Min(n - 1, i + 25) && peak; k++) if (Math.Abs(turn[k]) > w + 1e-4f || (Math.Abs(turn[k]) == w && k < i)) peak = false;
            if (!peak) continue;
            float tc = i * SampleStep; float sign = turn[i];
            int cornerNo = cornerCount++;
            Kind cornerKind = (cornerNo % 4) switch { 1 => Kind.HeadOn, 3 => Kind.Fly, _ => Kind.Roadside };      // (Roadside, head-on, Roadside, fly-by)
            events.Add((tc, w > 1.8f ? 7 : 6, () => AnchoredShot(cornerKind, tc, w > 1.8f ? "hairpin" : "corner", sign, cornerKind == Kind.Fly ? 3.6f : 10f) ?? AnchoredShot(Kind.Roadside, tc, w > 1.8f ? "hairpin" : "corner", sign, 10f)));
        }
        foreach (var e in events.OrderByDescending(e => e.Priority).ThenBy(e => e.T))
        {
            if (e.Make() is not Segment s) continue;
            Claim(s, e.T);
        }
        FillGaps(speed);
    }

    /// <summary>Contiguous runs of samples where the condition holds for at least minSamples.</summary>
    static IEnumerable<(int A, int B)> Spans(int n, Func<int, bool> on, int minSamples)
    {
        int start = -1;
        for (int i = 0; i <= n; i++)
        {
            bool now = i < n && on(i);
            if (now && start < 0) start = i;
            else if (!now && start >= 0) { if (i - start >= minSamples) yield return (start, i - 1); start = -1; }
        }
    }

    /// <summary>Takes the free stretch of time around the key moment, if the shot still gets its two seconds.</summary>
    void Claim(Segment s, float key)
    {
        float a = s.Start, b = s.End;
        foreach (var o in _plan)
        {
            if (o.End <= a || o.Start >= b) continue;
            if (o.Start <= key && key < o.End) return;      // the moment is already covered by a better shot
            if (o.End <= key) a = Math.Max(a, o.End); else b = Math.Min(b, o.Start);
        }
        if (b - a < MinShot) return;
        _plan.Add(s with { Start = a, End = b });
        _plan.Sort((x, y) => x.Start.CompareTo(y.Start));
    }

    void FillGaps(float[] speed)
    {
        var filled = new List<Segment>();
        float cursor = 0;
        var events = _plan.ToList();
        int piece = 0;
        Kind previous = Kind.Jump;
        void Gap(float a, float b, int nextEvent)
        {
            float length = b - a;
            if (length <= .01f) return;
            if (length < MinShot)
            {
                // Too short for a shot of its own: the shot before it runs on, or the one after it begins earlier.
                if (filled.Count > 0) filled[^1] = filled[^1] with { End = b };
                else if (nextEvent >= 0 && nextEvent < events.Count) events[nextEvent] = events[nextEvent] with { Start = a };
                return;
            }
            int count = Math.Max(1, (int)Math.Round(length / 5f));
            float step = length / count;
            for (int i = 0; i < count; i++)
            {
                float s0 = a + i * step, s1 = s0 + step;
                int si = Math.Clamp((int)((s0 + s1) / 2 / SampleStep), 0, speed.Length - 1);
                bool fast = _maxSpeed > 1 && speed[si] > .8f * _maxSpeed;
                Segment? seg = null;
                if (s0 > .5f && piece % 3 == 1)
                {
                    // A fixed camera ahead, the car passing it in the middle of the piece.
                    var kind = fast ? (piece % 2 == 0 ? Kind.Fly : Kind.HeadOn) : (piece % 2 == 0 ? Kind.HeadOn : Kind.Roadside);
                    if (kind != previous && AnchoredShot(kind, (s0 + s1) / 2, "stage", 0, kind == Kind.Fly ? 3.6f : 6f) is Segment q
                        && Math.Min(q.End, s1) - Math.Max(q.Start, s0) >= MinShot)
                        seg = q with { Start = s0, End = s1 };
                }
                seg ??= new Segment(s0, s1, BaseKind(piece, fast, s0 < .5f, previous), default, piece, fast ? "flat out" : "cover");
                piece++;
                previous = seg.Value.Kind;
                filled.Add(seg.Value);
            }
        }
        for (int e = 0; e < events.Count; e++)
        {
            Gap(cursor, events[e].Start, e);
            filled.Add(events[e]); previous = events[e].Kind; cursor = events[e].End;
        }
        Gap(cursor, _duration, -1);
        _plan.Clear(); _plan.AddRange(filled.OrderBy(s => s.Start));
        // A fixed camera whose shot grew (a short gap went to it) must still see the car all through: if not, a moving camera takes the shot.
        for (int i = 0; i < _plan.Count; i++)
        {
            var seg = _plan[i];
            if (!Fixed(seg.Kind) || ClearAcross(seg.Eye, seg.Start, seg.End)) continue;
            Kind before = i > 0 ? _plan[i - 1].Kind : Kind.Jump, after = i + 1 < _plan.Count ? _plan[i + 1].Kind : Kind.Jump;
            Kind pick = BaseKind(i, true, seg.Start < .5f, before);
            if (pick == after) pick = BaseKind(i + 1, true, false, pick);
            _plan[i] = seg with { Kind = pick, Eye = default, Why = seg.Why + " (moved)" };
        }
        // The last shot runs to the very end of the clip.
        if (_plan.Count > 0) { var last = _plan[^1]; _plan[^1] = last with { End = Math.Max(last.End, _duration) }; }
    }

    static Kind BaseKind(int piece, bool fast, bool opening, Kind previous)
    {
        if (opening) return Kind.Chase;
        Kind[] calm = { Kind.TopDown, Kind.Chase, Kind.Bonnet, Kind.Helicopter, Kind.TopDown, Kind.Spoiler };
        Kind[] quick = { Kind.Bonnet, Kind.Spoiler, Kind.TopDown, Kind.Chase, Kind.Helicopter, Kind.Spoiler };
        var table = fast ? quick : calm;
        for (int i = 0; i < table.Length; i++)
        {
            var k = table[(piece + i) % table.Length];
            if (k != previous) return k;
        }
        return Kind.TopDown;
    }

    // ---------------------------------------------------------------- fixed cameras
    /// <summary>
    /// A camera stood by the road for the moment tc: on the outside of the corner (sign: + left turn) a few metres off the edge, ahead of
    /// the car so that it comes at the camera, passes it and leaves. Null when no clear spot is found.
    /// </summary>
    Segment? AnchoredShot(Kind kind, float tc, string why, float sign, float stand)
    {
        var heading = Heading(tc);
        int at = _track.NearestCenter(new Vector2(P(tc).X, P(tc).Z), out _);
        float speed = Math.Max(5f, Speed(tc));
        int sideFirst = sign > 0 ? 1 : sign < 0 ? -1 : (Math.Abs((int)(tc * 7)) % 2 == 0 ? 1 : -1);      // (+1 the right hand side: the outside of a left turn)
        float ahead = kind switch { Kind.Fly => speed * 1.3f, Kind.HeadOn => speed * 2.2f, _ => why is "corner" or "hairpin" ? 0f : speed * .8f };
        float height = kind switch { Kind.Fly => .5f, Kind.HeadOn => .9f, _ => 1.5f };
        foreach (float lift in new[] { 0f, 2.5f, 5.5f })      // (over a bank or a tree line: the camera climbs a mound)
        foreach (float extra in new[] { 0f, 4f, 9f })
        foreach (int side in new[] { sideFirst, -sideFirst })
        {
            var onRoad = P(tc) + heading * ahead;
            var right = Right(heading);
            int ci = _track.NearestCenter(new Vector2(onRoad.X, onRoad.Z), out _);
            float half = _track.HalfWidth[ci];
            var eye = onRoad + right * side * (half + stand + extra) * (kind == Kind.HeadOn ? .5f : 1f);
            eye.Y = Ground(eye) + height + lift;
            if (!SpotOk(eye, tc, kind)) continue;
            // The car's closest approach, and the shot's time around it.
            float best = float.MaxValue, tStar = tc;
            for (float t = Math.Max(0, tc - 4f); t <= Math.Min(_duration, tc + 4f); t += .1f)
            {
                float d = P(t).DistanceTo(eye);
                if (d < best) { best = d; tStar = t; }
            }
            float lead = Math.Clamp(45f / speed, 1.2f, 3.2f), tail = kind == Kind.Fly ? 1.0f : 1.6f;
            if (kind is Kind.Roadside) { lead = Math.Clamp(38f / speed, 1.3f, 2.8f); tail = 2.2f; }
            float start = Math.Max(0, tStar - lead), end = Math.Min(_duration, tStar + tail);
            if (end - start < MinShot) { start = Math.Max(0, end - MinShot); end = Math.Min(_duration, start + MinShot); }
            if (!ClearAcross(eye, start, end)) continue;
            return new Segment(start, end, kind, eye, (int)(tc * 10), why);
        }
        return null;
    }

    Segment? JumpShot(float t0, float t1)
    {
        var heading = Heading(t0);
        float flight = Math.Max(.4f, t1 - t0);
        var mid = P((t0 + t1) / 2);
        var right = Right(heading);
        foreach (float lift in new[] { 0f, 2.5f, 5.5f })
        foreach (float off in new[] { 9f, 14f, 6f })
        foreach (int side in new[] { 1, -1 })
        {
            var eye = mid + right * side * off;
            eye.Y = Ground(eye) + 1.1f + lift;
            if (!SpotOk(eye, t0, Kind.Jump)) continue;
            float start = Math.Max(0, t0 - 1.1f), end = Math.Min(_duration, t1 + 1.4f);
            if (!ClearAcross(eye, start, end)) continue;
            return new Segment(start, end, Kind.Jump, eye, (int)(t0 * 10), $"jump {flight:0.0} s");
        }
        return null;
    }

    Segment? SplashShot(float t0, float t1)
    {
        var heading = Heading(t0);
        var right = Right(heading);
        var entry = P(t0) + heading * 6f;
        foreach (float lift in new[] { 0f, 2.5f, 5.5f })
        foreach (float off in new[] { 7f, 11f, 5f })
        foreach (int side in new[] { 1, -1 })
        {
            var eye = entry + right * side * off;
            eye.Y = Ground(eye) + 1.0f + lift;
            if (!SpotOk(eye, t0, Kind.Splash)) continue;
            float start = Math.Max(0, t0 - 1.5f), end = Math.Min(_duration, t1 + 1.0f);
            if (!ClearAcross(eye, start, end)) continue;
            return new Segment(start, end, Kind.Splash, eye, (int)(t0 * 10), "ford");
        }
        return null;
    }

    /// <summary>A standing place: dry, above the ground, not in a solid thing and in sight of the car at the moment.</summary>
    bool SpotOk(Vector3 eye, float t, Kind kind)
    {
        if (eye.X < 4 || eye.Z < 4 || eye.X > _track.N * _track.Tile - 4 || eye.Z > _track.N * _track.Tile - 4) return false;
        if (_track.Sample(eye.X, eye.Z).WaterDepth > 0f) return false;
        var e2 = new Vector2(eye.X, eye.Z);
        foreach (var o in _track.Obstacles) if (o.Center.DistanceTo(e2) < o.Radius + 1.2f) return false;
        int ci = _track.NearestCenter(e2, out float d);
        if (d < _track.HalfWidth[ci] + .8f && kind != Kind.HeadOn) return false;      // (not on the road itself)
        if (InForest(eye, 1.0f) || InBank(eye, .8f)) return false;      // (not inside a tree or a snow bank)
        return Visible(eye, P(t));
    }

    /// <summary>The car stays in sight of the camera through the shot (checked every half second).</summary>
    bool ClearAcross(Vector3 eye, float a, float b)
    {
        for (float t = Math.Max(0, a); t <= Math.Min(_duration, b); t += .5f)
            if (P(t).DistanceTo(eye) > 70f || !Visible(eye, P(t))) return false;
        return true;
    }

    // ---------------------------------------------------------------- the frame
    internal View Frame(float clock)
    {
        if (_plan.Count == 0) return new View(Kind.TopDown, 0, default, default, 45, .5f);
        int index = _plan.FindLastIndex(s => s.Start <= clock);
        if (index < 0) index = 0;
        var seg = _plan[index];
        var car = P(clock);
        var heading = Heading(clock, .5f);
        var right = Right(heading);
        float speed = Speed(clock);
        Vector3 Clamp(Vector3 eye, float clearance) { eye.Y = Math.Max(eye.Y, Ground(eye) + clearance); return eye; }
        switch (seg.Kind)
        {
            case Kind.Bonnet:
            {
                var v = GameplayBonnetCamera.Frame(car, B(clock));
                return new View(Kind.Bonnet, index, v.eye, v.target, 75, .06f);
            }
            case Kind.Chase:
            {
                var eye = Clamp(Smooth(clock, .3f) - heading * 8f + Vector3.Up * 2.5f, 1f);
                return new View(Kind.Chase, index, eye, car + heading * 5f + Vector3.Up * .9f, 60, .3f);
            }
            case Kind.Spoiler:
            {
                var eye = Clamp(Smooth(clock, .15f) - heading * 3.4f + right * .9f + Vector3.Up * .9f, .6f);
                float fov = Math.Clamp(66f + speed * .45f, 66f, 86f);
                return new View(Kind.Spoiler, index, eye, car + heading * 10f + Vector3.Up * .5f, fov, .15f);
            }
            case Kind.Helicopter:
            {
                float angle = seg.Seed * 1.9f + (clock - seg.Start) * .2f;
                var offset = new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle)) * 34f;
                var eye = Clamp(Smooth(clock, .8f) + offset + Vector3.Up * (21f + 4f * MathF.Sin(angle * 1.3f)), 9f);
                return new View(Kind.Helicopter, index, eye, car + heading * 4f, 52, .5f);
            }
            case Kind.TopDown:
                return new View(Kind.TopDown, index, default, default, 45, .5f);
            default:
            {
                // The fixed camera: its operator pans a moment behind the car and zooms to hold it at a steady size.
                float lag = seg.Kind is Kind.Fly ? .1f : seg.Kind is Kind.Jump ? .3f : .25f;
                var aim = Smooth(clock, lag) + Vector3.Up * .75f;
                float distance = Math.Max(2f, seg.Eye.DistanceTo(aim));
                float frame = seg.Kind switch { Kind.Roadside => 5.4f, Kind.HeadOn => 4.6f, Kind.Jump => 7.5f, Kind.Splash => 6f, _ => 3f };
                float fov = Mathf.RadToDeg(2f * MathF.Atan(frame / distance));
                fov = seg.Kind is Kind.Fly ? Math.Clamp(Math.Max(fov, 62f), 62f, 78f) : Math.Clamp(fov, 14f, 58f);
                return new View(seg.Kind, index, seg.Eye, aim, fov, seg.Kind is Kind.Fly ? .12f : .3f);
            }
        }
    }
}
