using System;
using System.Linq;
using CCCPRacing.Race;
using CCCPRacing.Track;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    /// <summary>
    /// The driving warnings about the bus, the drunk tractor and Kozlik's patrol (RoadWarnings.cs), on Ilyich's real road: ahead within the range and the cone that follows the road, on the same stretch of road (not the other leg of
    /// a bend), in view (terrain, a building; not a tree), once per encounter and again at the next, a busy HUD not eating a real encounter, a queued situational message asked again when its turn comes.
    /// </summary>
    void RoadWarningsTest()
    {
        var t = TrackBuilder.Build(TrackDef.Load("res://tracks/ilyich.json"));
        int n = t.Center.Length;
        Vector3 CarAt(int i) { var c = t.Center[i]; return new Vector3(c.X, t.Sample(c.X, c.Y).Height, c.Y); }
        // a straight stretch of road, flat enough to see along: the car at i, the road clear in view 100 m ahead
        int s0 = -1;
        for (int i = 0; i < n && s0 < 0; i += 7)
        {
            var d0 = t.CenterDir(i); bool straight = true;
            for (int k = 10; k <= 130 && straight; k += 20) straight = t.CenterDir((i + k) % n).Dot(d0) > 0.998f;
            if (!straight) continue;
            var heading = d0; var car = CarAt(i);
            if (RoadWarnings.InView(t, car, heading, t.Center[(i + 100) % n], -heading, RoadWarnings.BusProbes) && RoadWarnings.InView(t, car, heading, t.Center[(i + 60) % n], -heading, RoadWarnings.BusProbes)) s0 = i;
        }
        Check(s0 >= 0, $"road warnings: a straight, open stretch of Ilyich's road was found (sample {s0})");
        if (s0 < 0) return;
        Vector3 car0 = CarAt(s0); Vector2 h0 = t.CenterDir(s0);
        RoadWarnings.Verdict Bus(Vector2 at, Vector2? ignore = null) => RoadWarnings.Ahead(t, car0, h0, at, -h0, RoadWarnings.BusRange, RoadWarnings.BusHalfAngle, true, RoadWarnings.BusProbes, ignore ?? at, 6f);
        Vector2 Ahead(float m) => t.Center[(s0 + (int)m) % n];

        // ---- the bus
        Check(Bus(Ahead(80)).Ahead, $"the bus 80 m ahead on the road counts ({Bus(Ahead(80))})");
        Check(!Bus(Ahead(120)).Ahead, $"the bus 120 m ahead is too far ({Bus(Ahead(120))})");
        Check(!Bus(t.Center[(s0 - 40 + n) % n]).Ahead, $"the bus behind the car does not ({Bus(t.Center[(s0 - 40 + n) % n])})");
        Vector2 nose = new Vector2(car0.X, car0.Z) + h0 * 2.3f, perp = new Vector2(-h0.Y, h0.X);
        Vector2 AtAngle(float deg, float dist) => nose + h0 * (dist * MathF.Cos(Mathf.DegToRad(deg))) + perp * (dist * MathF.Sin(Mathf.DegToRad(deg)));
        var inCone = RoadWarnings.Ahead(t, car0, h0, AtAngle(59f, 40f), -h0, 100f, 60f, false, RoadWarnings.BusProbes);
        var outCone = RoadWarnings.Ahead(t, car0, h0, AtAngle(61f, 40f), -h0, 100f, 60f, false, RoadWarnings.BusProbes);
        Check(inCone.Why != "outside the cone" && outCone.Why == "outside the cone", $"the cone is 60 degrees each way from the road's direction (59: {inCone.Why}, 61: {outCone.Why})");
        // a car sliding sideways: the cone follows the ROAD, not the car's heading
        var sliding = RoadWarnings.Ahead(t, car0, (h0 + perp * 1.4f).Normalized(), Ahead(60), -h0, 100f, 60f, true, RoadWarnings.BusProbes);
        Check(sliding.Ahead, $"a car pointing 54 degrees off the road in a slide still has the bus 60 m ahead on the road in its cone ({sliding})");
        // a building between them blocks the view, a tree does not
        int mid = (s0 + 40) % n;
        t.Obstacles.Add(new Obstacle(t.Center[mid] + perp * 0.2f, 6f));
        var behindBuilding = Bus(Ahead(80));
        t.Obstacles.RemoveAt(t.Obstacles.Count - 1);
        t.Obstacles.Add(new Obstacle(t.Center[mid], 0.5f));
        var behindTree = Bus(Ahead(80));
        t.Obstacles.RemoveAt(t.Obstacles.Count - 1);
        Check(!behindBuilding.Ahead && behindBuilding.Why == "out of view" && behindTree.Ahead, $"a building between blocks the view ({behindBuilding.Why}), a tree does not ({behindTree.Why})");
        // what really hides it: a vehicle in between hides it, one beside the line hides only part (still seen), a low wall does not hide a bus's roof
        t.Obstacles.Add(new Obstacle(t.Center[mid], 1.8f, new Vector2(3f, 0f)));
        var behindVehicle = Bus(Ahead(80));
        t.Obstacles.RemoveAt(t.Obstacles.Count - 1);
        t.Obstacles.Add(new Obstacle(t.Center[mid] + perp * 0.9f, 1.0f, new Vector2(3f, 0f)));
        var partlyHidden = Bus(Ahead(80));
        t.Obstacles.RemoveAt(t.Obstacles.Count - 1);
        t.Obstacles.Add(new Obstacle(t.Center[mid], 0.7f, default, false, 0f, true));
        var behindWall = Bus(Ahead(80));
        t.Obstacles.RemoveAt(t.Obstacles.Count - 1);
        Check(!behindVehicle.Ahead && behindVehicle.Why == "out of view" && partlyHidden.Ahead && behindWall.Ahead, $"a vehicle between hides the bus ({behindVehicle.Why}); a vehicle at the side hides part and the bus is still seen ({partlyHidden.Why}); a low wall does not hide the roof ({behindWall.Why})");
        // across a bend: near by the straight line, far by road
        int fi = -1, fj = -1;
        for (int i = 0; i < n && fi < 0; i += 11)
        {
            var d = t.CenterDir(i); var c = CarAt(i);
            for (int j = 0; j < n; j += 5)
            {
                float dist = (t.Center[j] - t.Center[i]).Length();
                if (dist < 40f || dist > 90f) continue;
                if (MathF.Acos(Math.Clamp(d.Dot((t.Center[j] - t.Center[i]).Normalized()), -1f, 1f)) > Mathf.DegToRad(40f)) continue;
                float arc = RoadWarnings.ArcForward(t, i, j, (t.Center[(i + 3) % n] - t.Center[i]).Dot(d) >= 0f);
                if (arc > dist * 1.5f + 15f && arc < n - 20) { fi = i; fj = j; break; }
            }
        }
        if (fi >= 0)
        {
            var across = RoadWarnings.Ahead(t, CarAt(fi), t.CenterDir(fi), t.Center[fj], -t.CenterDir(fi), RoadWarnings.BusRange, RoadWarnings.BusHalfAngle, true, RoadWarnings.BusProbes);
            Check(!across.Ahead && across.Why == "another stretch of road", $"a bus on the other leg of a bend, near by line and far by road, does not count ({across})");
        }
        else Check(false, "road warnings: no place with another leg of the road in the cone was found on Ilyich to test the bend");

        // ---- the drunk tractor
        Vector2 Out = perp;      // the side of the road
        float hw = t.RoadHalfWidth((s0 + 40) % n, Ahead(40));
        RoadWarnings.TractorState Tr(Vector2 pos, Vector2 forward, float speed, float carSpeed, out RoadWarnings.Verdict v)
            => RoadWarnings.Tractor(t, car0, h0, carSpeed, pos, forward, speed, pos - forward * 3.5f, out v);
        var onRoad = Tr(Ahead(40), -h0, 2f, 14f, out var v1);
        Check(onRoad == RoadWarnings.TractorState.Ahead, $"a tractor standing on the road 40 m ahead: TRACTOR AHEAD ({onRoad}, {v1})");
        Check(Tr(Ahead(70), -h0, 2f, 14f, out _) == RoadWarnings.TractorState.None, "a tractor on the road 70 m ahead is beyond its 50 m");
        Vector2 field = Ahead(35) + Out * (hw + 12f);
        Check(Tr(field, h0, 2.5f, 14f, out _) == RoadWarnings.TractorState.None, "a tractor ploughing along the road 12 m off it says nothing");
        Vector2 verge = Ahead(35) + Out * (hw + 9f);
        var coming = Tr(verge, -Out, 3f, 14f, out var v2);
        Check(coming == RoadWarnings.TractorState.Crossing, $"a tractor on its way onto the road as the car gets there: TRACTOR CROSSING ({coming}, {v2})");
        Check(Tr(verge, Out, 3f, 14f, out _) == RoadWarnings.TractorState.None, "the same tractor going away from the road says nothing");
        Check(Tr(verge, -Out, 3f, 3f, out _) == RoadWarnings.TractorState.None, "a tractor that reaches the road long before a slow car gets there says nothing yet");
        Check(Tr(Ahead(30) + Out * (hw + 4f), Out, 2f, 14f, out _) == RoadWarnings.TractorState.Ahead, "a tractor with its plough still on the carriageway counts as AHEAD");
        // a tractor in view only past a building
        t.Obstacles.Add(new Obstacle(t.Center[(s0 + 20) % n] + perp * 0.1f, 6f));
        var hidden = Tr(Ahead(40), -h0, 2f, 14f, out var v3);
        t.Obstacles.RemoveAt(t.Obstacles.Count - 1);
        Check(hidden == RoadWarnings.TractorState.None, $"a tractor behind a building is not warned about ({v3})");

        int gi = -1, gj = -1;      // (a nearer pair for the tractor's 50 m)
        for (int i = 0; i < n && gi < 0; i += 9)
        {
            var d = t.CenterDir(i);
            for (int j = 0; j < n; j += 3)
            {
                float dist = (t.Center[j] - t.Center[i]).Length();
                if (dist < 25f || dist > 46f) continue;
                if (MathF.Acos(Math.Clamp(d.Dot((t.Center[j] - t.Center[i]).Normalized()), -1f, 1f)) > Mathf.DegToRad(55f)) continue;
                float arc = RoadWarnings.ArcForward(t, i, j, (t.Center[(i + 3) % n] - t.Center[i]).Dot(d) >= 0f);
                if (arc > dist * 1.5f + 15f && arc < n - 20) { gi = i; gj = j; break; }
            }
        }
        if (gi >= 0)
        {
            var otherLeg = RoadWarnings.Tractor(t, CarAt(gi), t.CenterDir(gi), 14f, t.Center[gj], -t.CenterDir(gj), 2f, t.Center[gj] + t.CenterDir(gj) * 3.5f, out var v4);
            Check(otherLeg == RoadWarnings.TractorState.None && v4.Why == "another stretch of road", $"a tractor standing on the other leg of a bend, near by line and far by road, is not warned about ({otherLeg}, {v4})");
        }
        else Check(false, "road warnings: no pair of road legs within 25-46 m was found to test the tractor across a bend");

        // ---- Kozlik's patrol
        RoadWarnings.PatrolState Pa(Vector2 pos, Vector2 forward, float speed, Vector2 carVel, out RoadWarnings.Verdict v) => RoadWarnings.Patrol(t, car0, h0, carVel, pos, forward, speed, out v);
        Vector2 cv = h0 * 20f;
        Check(Pa(Ahead(50), h0, 0f, cv, out var p1) == RoadWarnings.PatrolState.Ahead, $"a patrol stopped on the driving line 50 m ahead: AHEAD ({p1})");
        Check(Pa(Ahead(60), -h0, 9f, cv, out var p2) == RoadWarnings.PatrolState.Ahead, $"a patrol coming towards the car on the road: AHEAD ({p2})");
        Check(Pa(Ahead(50), h0, 12f, cv, out var p3) == RoadWarnings.PatrolState.Ahead, $"a patrol ahead on the road, slower than the car, which gets up to it: AHEAD ({p3})");
        Check(Pa(Ahead(50), h0, 15.5f, h0 * 9f, out var p4) == RoadWarnings.PatrolState.None, $"a patrol that pulls away from a slower car says nothing ({p4})");
        Check(Pa(Ahead(50) + Out * (hw + 6f), -h0, 0f, cv, out var p5) == RoadWarnings.PatrolState.None, $"a patrol on guard at the side, stopped, says nothing ({p5})");
        Check(Pa(Ahead(50) + Out * (hw + 6f), -Out, 4f, cv, out var p6) == RoadWarnings.PatrolState.Joining, $"a patrol pulling out of its roadside spot onto the car's line: JOINING ({p6})");
        Check(Pa(t.Center[(s0 - 40 + n) % n], h0, 0f, cv, out _) == RoadWarnings.PatrolState.None, "a patrol behind the car says nothing");
        Check(Pa(Ahead(95), h0, 0f, cv, out _) == RoadWarnings.PatrolState.None, "a patrol beyond its 80 m says nothing");

        // ---- once per encounter, again at the next
        var gate = new EncounterGate();
        Check(gate.Due(true, 0.016f), "the gate: the warning is due when the encounter starts");
        Check(gate.Due(true, 0.016f), "the gate: and still due while it was not given (a busy HUD does not eat the encounter)");
        gate.Spend();
        Check(!gate.Due(true, 0.016f) && !gate.Due(true, 0.5f), "the gate: given, it is not due again in the same encounter");
        Check(!gate.Due(false, 0.5f) && !gate.Due(true, 0.016f), "the gate: a short gap out of view is the same encounter");
        Check(!gate.Due(false, 2f) && gate.Due(true, 0.016f), "the gate: after the encounter is over (out of it for 1.5 s), the next one is warned about");
        var tg = new TractorGate();
        Check(tg.Due(RoadWarnings.TractorState.Crossing, 0.016f) == RoadWarnings.TractorState.Crossing, "the tractor gate: CROSSING is due");
        Check(tg.Due(RoadWarnings.TractorState.Crossing, 0.016f) == RoadWarnings.TractorState.Crossing, "the tractor gate: and still due while it was not shown (a busy HUD)");
        tg.Given(RoadWarnings.TractorState.Crossing);
        Check(tg.Due(RoadWarnings.TractorState.Crossing, 0.016f) == RoadWarnings.TractorState.None, "the tractor gate: CROSSING is not repeated");
        Check(tg.Due(RoadWarnings.TractorState.Ahead, 0.016f) == RoadWarnings.TractorState.None, "the tractor gate: the state changing to AHEAD in the same encounter gives no second warning");
        tg.Due(RoadWarnings.TractorState.None, 0.5f);
        Check(tg.Due(RoadWarnings.TractorState.Ahead, 0.016f) == RoadWarnings.TractorState.None, "the tractor gate: a half second out of it is the same encounter");
        tg.Due(RoadWarnings.TractorState.None, 2f);
        Check(tg.Due(RoadWarnings.TractorState.Ahead, 0.016f) == RoadWarnings.TractorState.Ahead, "the tractor gate: a new encounter after it has gone is warned about (whichever state it is in then)");

        // ---- the HUD: a dropped warning says so, a queued situational message is asked again
        var hud = new Hud();
        hud.Alert("ALERT", null, 3f, Hud.Red, Prio.Important);
        bool dropped = !hud.Show("URGENT", null, 2.5f, Hud.Gold, Prio.Important, urgent: true);
        Check(dropped, "the HUD: an urgent warning dropped for a busy screen says it was dropped");
        bool valid = true;
        bool queued = hud.Show("QUEUED", null, 2.5f, Hud.Gold, Prio.Important, stillValid: () => valid);
        valid = false;
        for (float tt = 0; tt < 4f; tt += 0.1f) hud.TickMessagesForTest(0.1f);
        Check(queued && hud.CenterText != "QUEUED", "the HUD: a queued message whose situation has passed is not shown when its turn comes");
        var hud2 = new Hud();
        hud2.Alert("ALERT", null, 1f, Hud.Red, Prio.Important);
        hud2.Show("STILL TRUE", null, 2.5f, Hud.Gold, Prio.Important, stillValid: () => true);
        for (float tt = 0; tt < 1.5f; tt += 0.1f) hud2.TickMessagesForTest(0.1f);
        Check(hud2.CenterText == "STILL TRUE", "the HUD: a queued message still true is shown");
    }
}
