using System;
using Godot;
using CCCPRacing.Track;

namespace CCCPRacing.Race;

/// <summary>
/// The geometry behind the driving warnings about something that moves on or onto the road (the Ilyich bus, the drunk tractor): is it AHEAD of the car along the road it is on, in front of the car's nose within a range and a cone
/// that follows the road's own direction (not the car's heading in a slide), on the same stretch of road (not the other leg of a bend that happens to be near), and in view (not behind a crest or a building)?
/// Pure functions on the track's data, so the acceptance tests can put the car and the thing anywhere and ask. The owner's order of 2026-11-21: the bus 100 m, 120 degrees; the tractor 50 m, 140 degrees.
/// </summary>
public static class RoadWarnings
{
    public const float BusRange = 85f, BusHalfAngle = 60f;      // (100 m until 2026-11-23: the warning came a little too early)
    public const float TractorRange = 50f, TractorHalfAngle = 70f;
    /// <summary>Kozlik's bear patrol (the owner's addition of 2026-11-21): chosen from its own driving: it cruises at 15.5 m/s and slows to 8.5 in bends, a car comes up on it at 10 to 25 m/s, and a stop from 25 m/s with a second and a half to read takes about 80 m.</summary>
    public const float PatrolRange = 80f, PatrolHalfAngle = 60f, PatrolBodyRadius = 2.2f;
    const float NoseAhead = 2.3f, EyeHeight = 1.25f;

    /// <summary>Why a thing was or was not counted ahead (a test reads it; the game only uses Ahead).</summary>
    public readonly record struct Verdict(bool Ahead, string Why, float Distance, float AngleDegrees, float RoadArc, bool InView)
    {
        public override string ToString() => $"{(Ahead ? "AHEAD" : "no")} ({Why}) {Distance:0.#} m, {AngleDegrees:0} deg, road arc {RoadArc:0} m, in view {InView}";
    }

    /// <summary>The road's own direction at the car, turned the way the car goes along it (the car's heading decides the sign).</summary>
    public static Vector2 RoadForward(TrackData t, Vector2 carPos, Vector2 carHeading, out int index)
    {
        index = t.NearestCenter(carPos, out _);
        var dir = t.CenterDir(index);
        return dir.Dot(carHeading) < 0f ? -dir : dir;
    }

    /// <summary>Metres along the road, in the car's direction of travel, from road sample <paramref name="from"/> to <paramref name="to"/> (the loop's length if it is behind).</summary>
    public static float ArcForward(TrackData t, int from, int to, bool forwardIsIncreasing)
    {
        int n = t.Center.Length; float arc = 0f; int i = from;
        for (int guard = 0; i != to && guard < n; guard++)
        {
            int next = forwardIsIncreasing ? (i + 1) % n : (i - 1 + n) % n;
            arc += t.Center[i].DistanceTo(t.Center[next]); i = next;
        }
        return arc;
    }

    /// <summary>
    /// Is <paramref name="target"/> (its ground position; <paramref name="bodyHeights"/> the heights of the parts that must be seen, above its ground) ahead of the car within the range and the cone, on the road ahead
    /// (<paramref name="onRoad"/>: it stands on the carriageway) and in view?
    /// </summary>
    public static Verdict Ahead(TrackData t, Vector3 car, Vector2 carHeading, Vector2 target, Vector2 targetForward, float range, float halfAngleDegrees, bool requireSameRoad, Probe[] probes, Vector2 ignoreCircle = default, float ignoreRadius = 0f)
    {
        Vector2 carPos = new(car.X, car.Z);
        Vector2 fwd = RoadForward(t, carPos, carHeading, out int ci);
        Vector2 nose = carPos + carHeading.Normalized() * NoseAhead;
        Vector2 to = target - nose; float dist = to.Length();
        if (dist > range) return new(false, "too far", dist, 0, 0, false);
        float angle = dist < 0.01f ? 0f : Mathf.RadToDeg(MathF.Acos(Math.Clamp(fwd.Dot(to / dist), -1f, 1f)));
        if (angle > halfAngleDegrees) return new(false, angle > 90f ? "behind" : "outside the cone", dist, angle, 0, false);
        // The same stretch of road: the way along the road from the car to the thing is not much longer than the straight line (the other leg of a bend, a parallel road, is far by road).
        float arc = 0f;
        if (requireSameRoad)
        {
            int ti = t.NearestCenter(target, out _);
            bool inc = IncreasingIsForward(t, ci, fwd);
            arc = ArcForward(t, ci, ti, inc);
            if (arc > dist * 1.5f + 15f) return new(false, "another stretch of road", dist, angle, arc, false);
        }
        bool seen = InView(t, car, carHeading, target, targetForward, probes, ignoreCircle, ignoreRadius);
        return seen ? new(true, "ahead", dist, angle, arc, true) : new(false, "out of view", dist, angle, arc, false);
    }

    static bool IncreasingIsForward(TrackData t, int index, Vector2 forward)
    {
        int n = t.Center.Length;
        return (t.Center[(index + 3) % n] - t.Center[index]).Dot(forward) >= 0f;
    }

    /// <summary>A part of the thing that must be seen: <paramref name="Across"/> and <paramref name="Along"/> metres from its centre in its own frame (along its forward), <paramref name="Height"/> above the ground there.</summary>
    public readonly record struct Probe(float Across, float Along, float Height);

    /// <summary>The visible hull of the bus (about 2.5 m wide, 10 m long, 3 m high): the roof's and the lower body's corners and middle.</summary>
    public static readonly Probe[] BusProbes = { new(0, 0, 2.7f), new(1.2f, 4.5f, 2.6f), new(-1.2f, 4.5f, 2.6f), new(1.2f, -4.5f, 2.6f), new(-1.2f, -4.5f, 2.6f), new(1.25f, 0, 1.0f), new(-1.25f, 0, 1.0f), new(0, 4.9f, 1.4f), new(0, -4.9f, 1.4f) };
    /// <summary>The tractor's cab and bonnet, and its plough (3.5 m behind its origin, 2.4 m wide).</summary>
    public static readonly Probe[] TractorProbes = { new(0, 0, 2.2f), new(0, 1.2f, 1.2f), new(1.0f, 0, 1.0f), new(-1.0f, 0, 1.0f), new(0, -3.5f, 0.8f), new(1.2f, -3.5f, 0.6f), new(-1.2f, -3.5f, 0.6f) };
    /// <summary>Kozlik's UAZ with the bear on it.</summary>
    public static readonly Probe[] PatrolProbes = { new(0, 0, 2.2f), new(0.8f, 1.8f, 1.2f), new(-0.8f, 1.8f, 1.2f), new(0.8f, -1.8f, 1.2f), new(-0.8f, -1.8f, 1.2f) };

    /// <summary>How tall an obstacle circle stands, for the view: the track data has none, so it is told by what the circle is. 0: it does not block (a post or a trunk is thinner than a car's view needs: the probes around it see past).</summary>
    static float OccluderHeight(in Obstacle o)
    {
        if (o.Center.X < -9000f) return 0f;                   // (a parked-away slot)
        if (o.Velocity != default) return 2.8f;               // a moving vehicle (the bus, a tank, a patrol)
        if (o.Radius >= 1.3f) return 5f;                      // a building, a rock, a stopped vehicle
        if (o.Crumbles) return 1.3f;                          // a dry-stone wall
        if (o.Radius >= 0.35f) return 1.4f;                   // a bush, a bale, a drum
        return 0f;                                            // a post, a trunk, a sign: thin
    }

    /// <summary>
    /// What the driver sees: rays from the eye at the car's NOSE (where the range is measured from) to the parts of the thing. Terrain along a ray and obstacles that are TALLER than the ray at that point block it (a building, a
    /// moving vehicle between them, a crest); the thing is in view when any part is (partly hidden is still seen). Circles within <paramref name="ignoreRadius"/> of <paramref name="ignoreCentre"/> are the thing itself.
    /// </summary>
    public static bool InView(TrackData t, Vector3 car, Vector2 carHeading, Vector2 target, Vector2 targetForward, Probe[] probes, Vector2 ignoreCentre = default, float ignoreRadius = 0f)
    {
        Vector2 nose = new Vector2(car.X, car.Z) + carHeading.Normalized() * NoseAhead;
        float eyeY = t.Sample(nose.X, nose.Y).Height + EyeHeight;
        Vector2 side = new(-targetForward.Y, targetForward.X);
        foreach (var pr in probes)
        {
            Vector2 at = target + side * pr.Across + targetForward * pr.Along;
            if (RayClear(t, nose, eyeY, at, t.Sample(at.X, at.Y).Height + pr.Height, ignoreCentre, ignoreRadius)) return true;
        }
        return false;
    }

    /// <summary>One ray from (a, eyeY) to (b, endY): terrain, then the obstacles taller than the ray where it passes them.</summary>
    public static bool RayClear(TrackData t, Vector2 a, float eyeY, Vector2 b, float endY, Vector2 ignoreCentre = default, float ignoreRadius = 0f)
    {
        float dist = a.DistanceTo(b);
        if (dist < 1f) return true;
        for (float s = 3f; s < dist - 1.5f; s += 2f)
        {
            Vector2 p = a.Lerp(b, s / dist);
            if (t.Sample(p.X, p.Y).Height > Mathf.Lerp(eyeY, endY, s / dist) - 0.05f) return false;
        }
        Vector2 ab = b - a; float len2 = ab.LengthSquared();
        foreach (var o in t.Obstacles)
        {
            float h = OccluderHeight(o);
            if (h <= 0f) continue;
            if (ignoreRadius > 0f && o.Center.DistanceTo(ignoreCentre) < ignoreRadius) continue;
            float u = Mathf.Clamp((o.Center - a).Dot(ab) / len2, 0f, 1f);
            float along = u * MathF.Sqrt(len2);
            if (along < 2f || along > dist - 1.5f) continue;
            if (a.Lerp(b, u).DistanceTo(o.Center) >= o.Radius) continue;
            if (Mathf.Lerp(eyeY, endY, u) < t.Sample(o.Center.X, o.Center.Y).Height + h) return false;      // (the ray passes the circle lower than it is tall)
        }
        return true;
    }

    public enum TractorState { None, Crossing, Ahead }

    /// <summary>
    /// The tractor: AHEAD when its body or plough already stands on the carriageway (an obstacle there), CROSSING when it is on its way onto the road and the car would reach that place at about the same time;
    /// None for one that ploughs at the side, goes away or is not seen. <paramref name="carSpeed"/> and the tractor's pose are of this instant.
    /// </summary>
    public static TractorState Tractor(TrackData t, Vector3 car, Vector2 carHeading, float carSpeed, Vector2 pos, Vector2 forward, float speed, Vector2 ploughPos, out Verdict verdict)
    {
        verdict = new(false, "none", 0, 0, 0, false);
        Vector2 c2 = new(car.X, car.Z);
        if (pos.DistanceTo(c2) > TractorRange + 12f && ploughPos.DistanceTo(c2) > TractorRange + 12f) return TractorState.None;      // (far: nothing to work out)
        bool bodyOn = OnCarriageway(t, pos, 1.6f), ploughOn = OnCarriageway(t, ploughPos, 1.5f);
        // the cone and the range count from the part that matters (the one on the road, else the body); what must be seen is the hull and the plough (any part)
        Vector2 focus = ploughOn && !bodyOn ? ploughPos : pos;
        var v = Ahead(t, car, carHeading, focus, forward, TractorRange, TractorHalfAngle, requireSameRoad: false, TractorProbes, pos, 0f);
        verdict = v;
        if (!v.Ahead) return TractorState.None;
        // The same stretch of road: where the tractor meets the road is on the stretch ahead of the car, not on the other leg of a bend that is near by line and far by road.
        Vector2 fwdRoad = RoadForward(t, c2, carHeading, out int ci);
        int ri = t.NearestCenter(focus, out float lateral);
        float arc = ArcForward(t, ci, ri, IncreasingIsForward(t, ci, fwdRoad));
        float straight = c2.DistanceTo(focus);
        if (arc > straight * 1.5f + 15f) { verdict = v with { Ahead = false, Why = "another stretch of road", RoadArc = arc }; return TractorState.None; }
        verdict = v with { RoadArc = arc };
        if (bodyOn || ploughOn) return TractorState.Ahead;
        // on its way to the road: its heading brings it to the carriageway, and the car gets to that place within a few seconds of it
        if (speed < 0.5f) return TractorState.None;
        Vector2 outward = (pos - t.Center[ri]).Normalized();
        float towardRoad = -forward.Dot(outward);      // 1: straight at the road, -1: away from it
        if (towardRoad < 0.35f) return TractorState.None;
        float edge = t.RoadHalfWidth(ri, pos) + 1.5f;
        float gap = MathF.Max(0f, lateral - edge);
        float tractorTime = gap / MathF.Max(0.1f, speed * towardRoad);
        float along = (t.Center[ri] - c2).Dot(fwdRoad);
        float carTime = along / MathF.Max(2f, carSpeed);
        return along > -4f && MathF.Abs(carTime - tractorTime) <= 3.5f ? TractorState.Crossing : TractorState.None;
    }

    public enum PatrolState { None, Ahead, Joining }

    /// <summary>
    /// Kozlik's patrol (a UAZ with a bear): AHEAD when it is on the carriageway on the stretch of road ahead, in view, and the car gets up to it (it drives slower than the car, comes towards it, or stands on the driving line);
    /// JOINING when it waits or crawls at the roadside and is on its way onto the road so that it is on the car's line when the car gets there. A patrol that stands safely at the side, is hidden, behind, or seen across a
    /// bend on another leg of the road stays silent.
    /// </summary>
    public static PatrolState Patrol(TrackData t, Vector3 car, Vector2 carHeading, Vector2 carVel, Vector2 pos, Vector2 forward, float speed, out Verdict verdict)
    {
        verdict = Ahead(t, car, carHeading, pos, forward, PatrolRange, PatrolHalfAngle, false, PatrolProbes, pos, 4f);
        if (!verdict.Ahead) return PatrolState.None;
        Vector2 carPos = new(car.X, car.Z);
        Vector2 moverVel = forward * speed;
        Vector2 toMover = (pos - carPos).Normalized();
        float closing = (carVel - moverVel).Dot(toMover);      // m/s the gap shrinks
        bool onRoad = OnCarriageway(t, pos, PatrolBodyRadius);
        // the same stretch of road: by road the way to it is not much longer than straight
        int ci = t.NearestCenter(carPos, out _);
        Vector2 fwdRoad = RoadForward(t, carPos, carHeading, out _);
        int pi = t.NearestCenter(pos, out float lateralPatrol);
        float arc = ArcForward(t, ci, pi, IncreasingIsForward(t, ci, fwdRoad));
        float straight = carPos.DistanceTo(pos);
        if (arc > straight * 1.5f + 15f) { verdict = verdict with { Ahead = false, Why = "another stretch of road", RoadArc = arc }; return PatrolState.None; }
        if (onRoad)
        {
            // on the driving line (a lane's width of it) and getting closer, or standing there: a reason to ease off
            float lineOffset = MathF.Abs((pos - carPos).Dot(new Vector2(-fwdRoad.Y, fwdRoad.X)));
            if (lineOffset > 4.5f) { verdict = verdict with { Ahead = false, Why = "beside the driving line" }; return PatrolState.None; }
            if (closing > 1.5f || speed < 1.0f && closing > -0.5f) return PatrolState.Ahead;
            verdict = verdict with { Ahead = false, Why = "pulling away" };
            return PatrolState.None;
        }
        // at the roadside: only one that is on its way onto the road matters
        if (speed < 0.5f) { verdict = verdict with { Ahead = false, Why = "waiting at the side" }; return PatrolState.None; }
        Vector2 outward = (pos - t.Center[pi]).Normalized();
        float towardRoad = -forward.Dot(outward);
        if (towardRoad < 0.35f) { verdict = verdict with { Ahead = false, Why = "not coming onto the road" }; return PatrolState.None; }
        float gap = MathF.Max(0f, lateralPatrol - (t.RoadHalfWidth(pi, pos) + 1.5f));
        float patrolTime = gap / MathF.Max(0.1f, speed * towardRoad);
        float along = (t.Center[pi] - carPos).Dot(fwdRoad);
        float carTime = along / MathF.Max(2f, carVel.Length());
        if (along > -4f && MathF.Abs(carTime - patrolTime) <= 3.5f) return PatrolState.Joining;
        verdict = verdict with { Ahead = false, Why = "gets to the road at another time" };
        return PatrolState.None;
    }

    static bool OnCarriageway(TrackData t, Vector2 p, float radius)
    {
        int i = t.NearestCenter(p, out float lateral);
        return lateral - radius < t.RoadHalfWidth(i, p) + 0.5f;
    }
}

/// <summary>
/// Once per encounter, and again at the next one: a warning armed by the thing coming into the car's view ahead, spent when it is given, armed again when the encounter is over (the thing has been out of the cone for a
/// moment: passed, gone, away). The game says a warning was given only when the message was shown or queued, so a busy HUD does not eat a real encounter.
/// </summary>
public sealed class EncounterGate
{
    readonly float _overFor;
    bool _spent; float _quiet;
    public EncounterGate(float overForSeconds = 1.5f) { _overFor = overForSeconds; }

    /// <summary>True when the warning is due now. <paramref name="inEncounter"/>: the thing counts as ahead this frame.</summary>
    public bool Due(bool inEncounter, float dt)
    {
        if (inEncounter) { _quiet = 0f; return !_spent; }
        _quiet += dt;
        if (_quiet >= _overFor) _spent = false;
        return false;
    }

    /// <summary>The warning was shown (or queued): this encounter has had it.</summary>
    public void Spend() => _spent = true;
    public bool Spent => _spent;
}

/// <summary>The same for the tractor, whose warning (CROSSING or AHEAD, whichever it is when it is due) is given once per encounter: the first one really shown is enough, a change of state in the same encounter does not give a second.</summary>
public sealed class TractorGate
{
    float _none;
    bool _given;

    /// <summary>The state to warn about now (None: nothing). Call <see cref="Given"/> when the warning was shown or queued.</summary>
    public RoadWarnings.TractorState Due(RoadWarnings.TractorState now, float dt)
    {
        if (now == RoadWarnings.TractorState.None) { _none += dt; if (_none >= 1.5f) _given = false; return RoadWarnings.TractorState.None; }
        _none = 0f;
        return _given ? RoadWarnings.TractorState.None : now;
    }

    public void Given(RoadWarnings.TractorState s) { if (s != RoadWarnings.TractorState.None) _given = true; }
    /// <summary>This encounter has had its warning (a test reads it).</summary>
    public bool IsGiven => _given;
}
