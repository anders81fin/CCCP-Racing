using System;
using System.Collections.Generic;
using System.Linq;
using CCCPRacing.Car;
using CCCPRacing.Race;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    const float RamDt = 1f / 60f;

    /// <summary>What a run of the duty officer behind a real, stepped car saw: the tick of the fine, the ticks of his contacts, the car's numbers at the contact.</summary>
    sealed class RamResult
    {
        public int CaughtTick = -1, CaughtCount, FirstContactTick = -1, ObstacleTick = -1;
        public readonly List<(int Tick, CarSim.ObstacleContact Contact)> Contacts = new();
        public float SpeedBefore, SpeedAfter, ImpactAtContact, PatrolGapAtObstacle = -1f, PatrolBehindSecond;
        public float MinSep = 1e9f;
        public bool WasBehind, CarPassedPatrol, AutographAtEnd, FinePassed;
        public float MinAhead = 1e9f;      // the closest he came behind the car along the road (m): the old fine's distance was 4.5
        public int ContactsAfterCaught, GhostObstacles;
        public Vector2 CarAt;
    }

    /// <summary>
    /// The game's own order of a physics tick: the car steps (two substeps, its collision response against the track's obstacles, which hold the officer's three circles) and then the officer ticks with what the
    /// car felt. `carSpeed` rolls the car down the road (0: stands, brake held); `gap` metres behind it the officer starts at `patrolSpeed`, `ramDelta` m/s faster than the car; the car is `carLat` off the centre line.
    /// </summary>
    RamResult RamRun(string id, float carSpeed, float carLat, float gap, float ramDelta, int ticks, float patrolLat = float.NaN, bool stepCar = true, float obstacleAhead = 0f, int hold = -1)
    {
        var track = TrackBuilder.Build(TrackDef.Load($"res://tracks/{id}.json"));
        var fx = new TyreEffects(); fx.Init(track); AddChild(fx);
        var patrol = new KozlikPatrol(); AddChild(patrol);
        patrol.Init(track, fx, null, null, 0);
        var car = new CarSim(new CarParams(), track) { Obstacles = track.Obstacles };
        int At = RamStraight(track);
        SchoolScript.SetRolling(car, track, SchoolScript.RoadPoint(track, At, carLat), track.CenterDir(At), carSpeed);
        if (obstacleAhead > 0f)
        {
            Vector2 dir = track.CenterDir(At); Vector2 spot = SchoolScript.RoadPoint(track, At, carLat) + dir * obstacleAhead;
            track.Obstacles.Add(new Obstacle(spot, 1.5f));
        }
        patrol.TestStartChaseOnRoad(At - gap, float.IsNaN(patrolLat) ? carLat : patrolLat, carSpeed, ramDelta);
        var res = new RamResult(); int tick = 0;
        if (OS.HasEnvironment("RAMDBG")) GD.Print($"RAM spacing {track.Center[At].DistanceTo(track.Center[At + 1]):F3} m, hw {track.HalfWidth[At]:F2}, n {track.Center.Length}");
        patrol.Caught += () => { res.CaughtCount++; if (res.CaughtTick < 0) res.CaughtTick = tick; };
        long seen = car.ContactSerial;
        float lastSpeedForward = car.Speed;
        for (tick = 0; tick < ticks; tick++)
        {
            float before = car.Vel.Dot(car.Forward), maxImpact = 0f;
            if (stepCar)
            {
                if (carSpeed <= 0f || (hold >= 0 && tick >= hold)) { car.SetControls(false, false, 0f, true); car.Brake = 1f; car.Neutral = true; }
                for (int sub = 0; sub < 2; sub++) { car.Step(RamDt / 2f); maxImpact = Math.Max(maxImpact, car.LastImpact); }
            }
            if (res.ObstacleTick < 0 && obstacleAhead > 0f && maxImpact > 3f && !car.Contacts.Any(c => c.Serial > seen && c.Source != 0))
            { res.ObstacleTick = tick; res.PatrolGapAtObstacle = patrol.Position2D.DistanceTo(new Vector2(car.Pos.X, car.Pos.Z)); }
            foreach (var c in car.Contacts.Where(c => c.Serial > seen && c.Source == patrol.Source))
            {
                res.Contacts.Add((tick, c));
                if (res.FirstContactTick < 0) { res.FirstContactTick = tick; res.SpeedBefore = before; res.ImpactAtContact = maxImpact; }
                if (res.CaughtTick >= 0) res.ContactsAfterCaught++;
            }
            seen = car.ContactSerial;
            patrol.Tick(RamDt, car, maxImpact);
            if (res.FirstContactTick == tick && res.SpeedAfter == 0f) res.SpeedAfter = car.Vel.Dot(car.Forward);
            var carPos = new Vector2(car.Pos.X, car.Pos.Z);
            float ahead = (carPos - patrol.Position2D).Dot(track.CenterDir(At));
            res.MinSep = Math.Min(res.MinSep, (carPos - patrol.Position2D).Length());
            if (ahead > 0f) res.MinAhead = Math.Min(res.MinAhead, ahead);
            if (tick > 5 && ahead > 3f && res.CarPassedPatrol == false && res.WasBehind) res.CarPassedPatrol = true;
            if (ahead < -3f) res.WasBehind = true;
            if (OS.HasEnvironment("RAMDBG") && tick % 15 == 0) GD.Print($"RAM t{tick} patrol {patrol.Position2D} spd {patrol.Speed:F2} car {carPos} v {car.Speed:F2} sep {(patrol.Position2D - carPos).Length():F2} ahead {(carPos - patrol.Position2D).Dot(track.CenterDir(At)):F2} gap {patrol.TestGap:F2} s {patrol.TestS:F2} steer {patrol.TestSteer:F3} lat {patrol.TestLat:F2} guide {patrol.TestGuide}");
        }
        res.AutographAtEnd = patrol.TestAutograph; res.FinePassed = patrol.LastWasPass;
        res.CarAt = new Vector2(car.Pos.X, car.Pos.Z);
        foreach (int o in patrol.TestObstacleIndices) if (track.Obstacles[o].Center.X < -1000f) res.GhostObstacles++;
        patrol.Reset();      // (lets go of the ghost circles he has handed to the traffic: they are static)
        RemoveChild(patrol); patrol.Free(); RemoveChild(fx); fx.Free();
        return res;
    }

    /// <summary>The middle of the straightest 120 m of the road (a car that is only rolled, not steered, must not leave it; the officer's cornering would also cap his speed in a bend): a road metre.</summary>
    static int RamStraight(TrackData track)
    {
        int n = track.Center.Length, best = 100; float bestTurn = float.MaxValue;
        for (int i = 100; i < n - 200; i += 5)
        {
            float turn = 0f; Vector2 d0 = track.CenterDir(i);
            for (int k = 5; k <= 120; k += 5) turn = Math.Max(turn, MathF.Abs(d0.AngleTo(track.CenterDir(i + k))));
            if (turn < bestTurn) { bestTurn = turn; best = i + 60; }
        }
        return best;
    }

    /// <summary>
    /// The duty officer's fine is a real rear-end collision. His three obstacle circles carry his id and part, the car's collision response notes every contact with them (the response itself, no extra shove),
    /// and the fine begins on the tick that his FRONT strikes the car's REAR while he drives into it: not on the distance, not at a side-swipe, not at a head-on, not at another obstacle's blow, and not twice.
    /// </summary>
    void KozlikRamTest()
    {
        float mass = new CarParams().Mass;

        // 1. A hard rear-end: the car stands, he comes in 9.5 m/s faster than it. The fine is on the tick of the first contact, the response is the game's own, the car is thrown forward.
        var hard = RamRun("bonus", 0f, -3f, 14f, 9.5f, 60 * 12, hold: 0);
        var hc = hard.Contacts.Count > 0 ? hard.Contacts[0].Contact : default;
        float expectedJ = 1.25f * hc.Closing * mass;
        Check(hard.CaughtTick >= 0 && hard.CaughtCount == 1 && hard.CaughtTick == hard.FirstContactTick && hc.Part == KozlikPatrol.FrontPart && hc.CarCircle == CarSim.RearCircle && hc.Closing > 4f,
            $"Kozlik ram: a hard rear-end is a front-to-rear contact (closing {hc.Closing:F1} m/s) and the fine begins on its very tick ({hard.CaughtTick} = {hard.FirstContactTick})");
        Check(Math.Abs(hc.Impulse - expectedJ) < .12f * expectedJ && hard.SpeedAfter - hard.SpeedBefore > .8f * hc.Impulse / mass && hard.SpeedAfter - hard.SpeedBefore < 1.2f * hc.Impulse / mass,
            $"Kozlik ram: the impulse is the game's collision response ({hc.Impulse:F0} N s against {expectedJ:F0}) and the car's speed rises by it ({hard.SpeedBefore:F2} to {hard.SpeedAfter:F2} m/s, {hc.Impulse / mass:F2} expected)");
        Check(hard.ImpactAtContact > 4f && hard.CaughtTick > 20, $"Kozlik ram: a violent hit is allowed and is the car's ordinary impact ({hard.ImpactAtContact:F1} m/s), not a fine from afar (tick {hard.CaughtTick})");

        // 2. A light rear-end: he closes only 2 m/s faster; the same fine, a much smaller impulse.
        var light = RamRun("bonus", 0f, -3f, 9f, 2f, 60 * 12, hold: 0);
        var lc = light.Contacts.Count > 0 ? light.Contacts[0].Contact : default;
        Check(light.CaughtTick >= 0 && light.CaughtTick == light.FirstContactTick && lc.Closing > .5f && lc.Closing < 3.5f && lc.Impulse > 0f && lc.Impulse < .5f * hc.Impulse,
            $"Kozlik ram: a light rear-end is fined too, on its tick ({light.CaughtTick}), with a small impulse ({lc.Impulse:F0} N s at {lc.Closing:F1} m/s against the hard one's {hc.Impulse:F0})");

        // 3. A car rolling at 12 m/s, rammed from behind.
        var rolling = RamRun("bonus", 12f, -3f, 12f, 9.5f, 60 * 4);
        Check(rolling.CaughtTick >= 0 && rolling.CaughtTick == rolling.FirstContactTick, $"Kozlik ram: a rolling car is fined on the tick of the contact ({rolling.CaughtTick}, contact {rolling.FirstContactTick})");

        // 4. Passing by: the car drives past him on the far side of where he can steer (5 m clear): no contact, no fine; and a car that stands 5 m aside while he comes up beside it is no fine either.
        var hwTrack = TrackBuilder.Build(TrackDef.Load("res://tracks/bonus.json")); float hw = hwTrack.HalfWidth[RamStraight(hwTrack)];
        var pass = RamRun("bonus", 15f, .8f * hw + 5f, -30f, 9.5f, 60 * 5);
        Check(pass.CarPassedPatrol && pass.Contacts.Count == 0 && pass.CaughtCount == 0, $"Kozlik ram: a car driving past him 5 m clear is no contact and no fine (passed {pass.CarPassedPatrol}, contacts {pass.Contacts.Count}, fines {pass.CaughtCount})");
        // 4b. He passes the car: a car rolling slowly 5 m aside (out of his reach), he comes in 9.5 m/s faster and cannot stop in time: his centre goes past the car's without a touch, and that is the fine, on that tick.
        var overtake = RamRun("bonus", 3f, .8f * hw + 5f, 14f, 9.5f, 60 * 6);
        Check(overtake.CaughtCount == 1 && overtake.Contacts.Count == 0 && overtake.FinePassed && overtake.CaughtTick > 0,
            $"Kozlik ram: he drives past the car without a touch: the fine is a pass (fines {overtake.CaughtCount}, pass {overtake.FinePassed}, contacts {overtake.Contacts.Count}, tick {overtake.CaughtTick})");
        Check(hard.CaughtCount == 1 && !hard.FinePassed, "Kozlik ram: a rear-end is not marked as a pass");
        var aside = RamRun("bonus", 0f, .8f * hw + 5f, 14f, 9.5f, 60 * 8, hold: 0);
        Check(aside.Contacts.Count == 0 && aside.CaughtCount == 0 && !aside.AutographAtEnd && aside.MinAhead < 4.6f,
            $"Kozlik ram: close is not enough: he came to {aside.MinAhead:F1} m behind a car 5 m aside (the old fine's distance was 4.5) and nothing happened");

        // 5. A side-swipe: he can only steer to 0.8 of the road and the car stands further out, so that his front scrapes along its rear corner and side (the contact normal is more across than along). A contact, never a fine.
        int touched = 0, fined = 0; float strongest = 0f, minSep = 1e9f;
        foreach (float off in new[] { 0.5f, 0.7f, 0.9f })
        {
            var side = RamRun("bonus", 0f, .8f * hw + off, 14f, 9.5f, 60 * 8, hold: 0);
            if (side.Contacts.Count > 0) { touched++; strongest = Math.Max(strongest, side.Contacts.Max(c => c.Contact.Closing)); }
            fined += side.CaughtCount; minSep = Math.Min(minSep, side.MinSep);
        }
        Check(touched > 0 && fined == 0, $"Kozlik ram: side-swipes ({touched} of 3 touched, min sep {minSep:F2} m, the strongest {strongest:F1} m/s) are never a fine ({fined})");

        // 6. Another obstacle's blow: the car runs into a bollard ahead (8 m/s) with him close behind; the fine is not at that blow but later, at his own rear-end.
        var other = RamRun("bonus", 8f, -3f, 9f, 9.5f, 60 * 12, obstacleAhead: 12f);
        Check(other.ObstacleTick >= 0 && other.CaughtTick > other.ObstacleTick && other.CaughtTick == other.FirstContactTick && other.PatrolGapAtObstacle < 14f,
            $"Kozlik ram: a blow of another obstacle (tick {other.ObstacleTick}, he was {other.PatrolGapAtObstacle:F1} m away) is no fine; his own rear-end is (tick {other.CaughtTick})");

        // 7. Not by distance, not in a replay: the car is placed on his line and never stepped (as a replay's car), he drives through where it stands: no contact is ever reported, no fine.
        var ghostCar = RamRun("bonus", 0f, -3f, 14f, 9.5f, 60 * 8, stepCar: false);
        Check(ghostCar.Contacts.Count == 0 && ghostCar.CaughtCount == 0 && ghostCar.MinAhead < 4.6f, $"Kozlik ram: closeness alone is never a fine (a car that does not collide, as in a replay, is not fined, though he came to {ghostCar.MinAhead:F1} m)");

        // 8. After the fine: once. He is not solid to the car any more (no second and third blow while the car is held and he signs), his circles are out of the way.
        Check(hard.CaughtCount == 1 && hard.ContactsAfterCaught == 0 && hard.AutographAtEnd && hard.GhostObstacles == 3,
            $"Kozlik ram: one fine, no further blows during the autograph ({hard.ContactsAfterCaught}), his circles are not solid ({hard.GhostObstacles}/3), the autograph is on {hard.AutographAtEnd}");

        // 9. Which contact counts: the classification itself, on synthetic contacts.
        CarSim.ObstacleContact C(int part, int circle, float closing, Vector3 local, Vector2 world) => new(1, 1, part, 0, circle, closing, 1f, local, world);
        Vector3 behind = new(0, 0, -1); Vector2 fwd = new(0, 1);      // (the car's nose is -Z: struck from behind, the normal points forward)
        Check(KozlikPatrol.IsRearRam(C(2, 2, 3f, behind, fwd), fwd, 8f), "Kozlik ram: front circle on the car's rear circle, driving into it, is the fine");
        Check(!KozlikPatrol.IsRearRam(C(1, 2, 3f, behind, fwd), fwd, 8f) && !KozlikPatrol.IsRearRam(C(0, 2, 3f, behind, fwd), fwd, 8f), "Kozlik ram: his middle or rear circle is not his front");
        Check(!KozlikPatrol.IsRearRam(C(2, 0, 3f, behind, fwd), fwd, 8f) && !KozlikPatrol.IsRearRam(C(2, 1, 3f, behind, fwd), fwd, 8f), "Kozlik ram: the car's nose or middle circle is not its rear");
        Check(!KozlikPatrol.IsRearRam(C(2, 2, 3f, new Vector3(-1, 0, 0), new Vector2(-1, 0)), fwd, 8f) && !KozlikPatrol.IsRearRam(C(2, 2, 3f, new Vector3(0, 0, 1), -fwd), fwd, 8f), "Kozlik ram: a side or a head-on is not a rear-end");
        Check(!KozlikPatrol.IsRearRam(C(2, 2, 3f, behind, new Vector2(1, 0)), fwd, 8f), "Kozlik ram: his nose must point at the car");
        Check(!KozlikPatrol.IsRearRam(C(2, 2, .1f, behind, fwd), fwd, 8f) && !KozlikPatrol.IsRearRam(C(2, 2, 3f, behind, fwd), fwd, .1f) && !KozlikPatrol.IsRearRam(C(2, 2, 3f, behind, fwd), fwd, -4f), "Kozlik ram: a graze, a stand-still officer or a reversing one is not ramming");
    }
}
