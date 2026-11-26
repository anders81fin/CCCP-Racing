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
    /// <summary>
    /// --gai-stop-traffic-tests (the owner's order of 2026-11-26): during the GAI's stop nothing drives through the held car or the Kozlik behind it (a ghost: solid only to the traffic).
    /// The snow plough and the BTR never move deeper into a car or the ghost Kozlik, even one that turns up inside their braking distance or slides into their lane late; the gulag truck
    /// stops short of the ghost Kozlik and goes on once it has gone, and its own arrested car never stalls it. Atomgrad: the autograph never holds a car or the Kozlik on the rails, and the
    /// rails test covers every place the train's circles reach.
    /// </summary>
    void GaiStopTrafficTest()
    {
        MoverBlockers.Clear(); MoverBlockers.ClearGhosts();
        const float dt = 1f / 60f;
        var ghostOwner = new object();

        // The deepest overlap (m) of the circles [from, from+count) of the track's obstacles with the blockers (0 or less: clear).
        static float Depth(TrackData t, int from, int count, IEnumerable<GulagTruck.Solid> blockers)
        {
            float worst = -99f;
            for (int i = from; i < from + count; i++)
            {
                var o = t.Obstacles[i];
                if (o.Center.X < -9000f) continue;
                foreach (var b in blockers) worst = Math.Max(worst, o.Radius + b.Radius - o.Center.DistanceTo(b.Centre));
            }
            return worst;
        }
        static MoverBlockers.Circle[] KozlikCircles(Vector2 middle, Vector2 fwd) =>
            Enumerable.Range(-1, 3).Select(k => new MoverBlockers.Circle(middle + fwd * (k * KozlikPatrol.HalfLength), KozlikPatrol.BodyRadius, Vector2.Zero)).ToArray();

        // ---- the snow plough (Siberia) and the BTR (Poligon): the last overlap check ----
        // kind: "car" (the held car), "kozlik" (the ghost behind it), "late" (the ghost sliding into the lane from the side, inside the braking distance)
        foreach (string kind in new[] { "car", "kozlik", "late" })
        {
            // the plough
            {
                var track = TrackBuilder.Build(TrackDef.Load("res://tracks/siberia.json"));
                var ploughs = new SnowPloughs(); AddChild(ploughs);
                int first = track.Obstacles.Count;
                ploughs.Init(track, null, new List<PloughDef> { new() { Start = 420 } });
                int count = track.Obstacles.Count - first;
                for (int i = 0; i < 60 * 4; i++) ploughs.Tick(dt, null);
                var model = ploughs.GetChild<Node3D>(0);
                Vector2 fwd = new Vector2(-model.GlobalBasis.Z.X, -model.GlobalBasis.Z.Z).Normalized();
                int front = first; for (int i = first; i < first + count; i++) if (track.Obstacles[i].Center.Dot(fwd) > track.Obstacles[front].Center.Dot(fwd)) front = i;
                var f = track.Obstacles[front];
                float speed0 = f.Velocity.Length();
                Vector2 right = new(-fwd.Y, fwd.X);
                var car = new CarSim(new CarParams(), track);
                Vector2 Target(float r) => f.Center + fwd * (f.Radius + r + 0.4f);
                float growth = 0f, deepest = -99f; Vector2 lastFront = f.Center;
                for (int i = 0; i < 60 * 4; i++)
                {
                    MoverBlockers.Clear(); MoverBlockers.ClearGhosts();
                    if (kind == "car")
                    {
                        Vector2 at = Target(CarSim.CollisionRadius + 1.3f);
                        car.Reset(new Vector3(at.X, track.MeshHeight(at.X, at.Y) + .5f, at.Y), TrackData.YawFor(fwd));
                        MoverBlockers.AddCar(car);
                    }
                    else
                    {
                        Vector2 at = Target(KozlikPatrol.HalfLength + KozlikPatrol.BodyRadius);
                        if (kind == "late") at += right * Math.Max(0f, 7f - 4f * i * dt);      // from 7 m aside into the lane at 4 m/s
                        MoverBlockers.SetGhost(ghostOwner, KozlikCircles(at, fwd));
                    }
                    var blockers = GulagTruck.Solids.ToList();
                    float before = Depth(track, first, count, blockers);
                    ploughs.Tick(dt, kind == "car" ? car : null);
                    float after = Depth(track, first, count, blockers);
                    if (after > 0f) growth = Math.Max(growth, after - Math.Max(0f, before));
                    deepest = Math.Max(deepest, after);
                }
                float stoppedSpeed = track.Obstacles[front].Velocity.Length();
                MoverBlockers.Clear(); MoverBlockers.ClearGhosts();
                Vector2 held = track.Obstacles[front].Center;
                for (int i = 0; i < 60 * 6; i++) ploughs.Tick(dt, null);
                float onwards = track.Obstacles[front].Center.DistanceTo(held);
                Check(speed0 > 8f, $"plough/{kind}: it was driving ({speed0:F1} m/s) when the blocker turned up {0.4f:F1} m off its blade");
                Check(growth < 0.002f, $"plough/{kind}: it never moves deeper into the {(kind == "car" ? "car" : "Kozlik")} (worst growth in one tick {growth:F3} m, deepest overlap {deepest:F2} m)");
                Check(stoppedSpeed < 0.05f && onwards > 5f, $"plough/{kind}: it stood still in front of it ({stoppedSpeed:F2} m/s) and drove on once it had gone ({onwards:F1} m)");
                RemoveChild(ploughs); ploughs.Free();
            }
            // the BTR
            {
                var track = TrackBuilder.Build(TrackDef.Load("res://tracks/bonus.json"));
                var fx = new TyreEffects(); AddChild(fx); fx.Init(track);
                int first = track.Obstacles.Count;
                var btr = new BtrPatrols(); AddChild(btr); btr.Init(track, fx);
                float speed0 = 0f;
                for (int i = 0; i < 60 * 40 && speed0 < 2.5f; i++) { btr.Tick(dt); speed0 = track.Obstacles[first].Velocity.Length(); }
                Vector2 c0 = track.Obstacles[first].Center, c2 = track.Obstacles[first + 2].Center, fwd = (c2 - c0).Normalized(), right = new(-fwd.Y, fwd.X);
                var car = new CarSim(new CarParams(), track);
                float growth = 0f, deepest = -99f;
                for (int i = 0; i < 60 * 4; i++)
                {
                    MoverBlockers.Clear(); MoverBlockers.ClearGhosts();
                    Vector2 frontNow = c2;
                    if (kind == "car")
                    {
                        Vector2 at = frontNow + fwd * (1.45f + CarSim.CollisionRadius + 1.3f + 0.3f);
                        car.Reset(new Vector3(at.X, track.MeshHeight(at.X, at.Y) + .5f, at.Y), TrackData.YawFor(fwd));
                        MoverBlockers.AddCar(car);
                    }
                    else
                    {
                        Vector2 at = frontNow + fwd * (1.45f + KozlikPatrol.HalfLength + KozlikPatrol.BodyRadius + 0.3f);
                        if (kind == "late") at += right * Math.Max(0f, 6f - 4f * i * dt);
                        MoverBlockers.SetGhost(ghostOwner, KozlikCircles(at, fwd));
                    }
                    var blockers = GulagTruck.Solids.ToList();
                    float before = Depth(track, first, 3, blockers);
                    btr.Tick(dt);
                    float after = Depth(track, first, 3, blockers);
                    if (after > 0f) growth = Math.Max(growth, after - Math.Max(0f, before));
                    deepest = Math.Max(deepest, after);
                }
                float stoppedSpeed = track.Obstacles[first].Velocity.Length();
                MoverBlockers.Clear(); MoverBlockers.ClearGhosts();
                Vector2 held = track.Obstacles[first].Center;
                for (int i = 0; i < 60 * 6; i++) btr.Tick(dt);
                float onwards = track.Obstacles[first].Center.DistanceTo(held);
                Check(speed0 > 2.5f, $"BTR/{kind}: it was rolling ({speed0:F1} m/s) when the blocker turned up 0.3 m off its bow");
                Check(growth < 0.002f, $"BTR/{kind}: it never moves deeper into the {(kind == "car" ? "car" : "Kozlik")} (worst growth in one tick {growth:F3} m, deepest overlap {deepest:F2} m)");
                Check(stoppedSpeed < 0.05f && onwards > 3f, $"BTR/{kind}: it stood still in front of it ({stoppedSpeed:F2} m/s) and drove on once it had gone ({onwards:F1} m)");
                RemoveChild(btr); btr.Free(); RemoveChild(fx); fx.Free();
            }
        }

        // ---- the gulag truck: the ghost Kozlik in its lane; its own arrested car (registered as in the race) never stalls it ----
        {
            var track = TrackBuilder.Build(TrackDef.Load("res://tracks/bonus.json"));
            var fx = new TyreEffects(); fx.Init(track); AddChild(fx);
            const int road = 300;
            var car = new CarSim(new CarParams(), track);
            SchoolScript.SetRolling(car, track, SchoolScript.RoadPoint(track, road, 0), track.CenterDir(road), 0);
            var truck = new GulagTruck(); AddChild(truck); truck.Init(track, fx); truck.Call(car);
            for (int i = 0; i < 48; i++) { MoverBlockers.Clear(); MoverBlockers.AddCar(car); truck.Tick(dt, car); }
            Vector2 fwd = new Vector2(truck.Velocity.X, truck.Velocity.Z).Normalized();
            Vector2 mid = GulagTruck.Body.ElementAt(1);
            Vector2 kozlik = mid + fwd * 16f;
            float speed0 = truck.Velocity.Length(), deepest = -99f;
            for (int i = 0; i < 60 * 6; i++)
            {
                MoverBlockers.Clear(); MoverBlockers.AddCar(car); MoverBlockers.SetGhost(ghostOwner, KozlikCircles(kozlik, fwd));
                truck.Tick(dt, car);
                foreach (var b in GulagTruck.Body.ToList()) foreach (var c in MoverBlockers.Ghosts.SelectMany(g => g.Value)) deepest = Math.Max(deepest, GulagTruck.BodyRadius + c.Radius - b.DistanceTo(c.Centre));
            }
            float waiting = truck.Velocity.Length();
            bool approaching = truck.State == GulagTruck.Phase.Approach;
            MoverBlockers.ClearGhosts();
            bool boarded = false;
            for (int i = 0; i < 60 * 30 && !boarded; i++) { MoverBlockers.Clear(); MoverBlockers.AddCar(car); truck.Tick(dt, car); boarded = truck.State != GulagTruck.Phase.Approach; }
            Check(speed0 > 6f, $"gulag truck: it was coming ({speed0:F1} m/s) when it met the Kozlik 16 m ahead");
            Check(deepest < 0f && waiting < 0.05f && approaching, $"gulag truck: it stops short of the ghost Kozlik and waits (deepest overlap {deepest:F2} m, {waiting:F2} m/s, still approaching {approaching})");
            Check(boarded, "gulag truck: with the Kozlik gone it reaches the car and the boarding starts, though the arrested car is among the cars it yields to");
            MoverBlockers.Clear();
            RemoveChild(truck); truck.Free(); RemoveChild(fx); fx.Free();
        }

        // ---- Atomgrad: the autograph is never held on the rails ----
        {
            var def = TrackDef.Load("res://tracks/atomgrad.json");
            var track = TrackBuilder.Build(def);
            var fx = new TyreEffects(); AddChild(fx); fx.Init(track);
            int first = track.Obstacles.Count;
            var zone = new ZoneLife(); AddChild(zone); zone.Init(track, fx, def.Zone);
            var tr = def.Zone.Train;
            var trainCircles = Enumerable.Range(first, track.Obstacles.Count - first).Where(i => MathF.Abs(track.Obstacles[i].Center.X - tr.X) < 0.01f && MathF.Abs(track.Obstacles[i].Radius - 1.7f) < 0.01f).ToList();
            Check(trainCircles.Count == 24, $"atomgrad: the train's circles are found ({trainCircles.Count})");
            Vector2 cross = new(tr.X, tr.Cross), east = new(1, 0);
            Check(RaceScene.AutographOnRails(zone, cross, cross - east * 6.5f, east), "autograph on the rails: a car caught on the crossing is not held there");
            Check(RaceScene.AutographOnRails(zone, cross + east * 8f, cross + east * 1.5f, east), "autograph on the rails: nor is it held while the Kozlik behind it is still on the rails");
            Check(!RaceScene.AutographOnRails(zone, cross + east * 13f, cross + east * 6.5f, east) && !RaceScene.AutographOnRails(zone, cross - east * 13f, cross - east * 6.5f, -east),
                "autograph on the rails: clear of the line, both ways, the car is held as everywhere else");
            Check(!RaceScene.AutographOnRails(null, cross, cross, east), "autograph on the rails: a track without a train holds the car as before");
            // Over the train's whole timetable (out, back and the waits): wherever a car's collision circle is said to be off the rails, no train circle ever reaches it, and the train does run over the crossing.
            var reached = new List<Vector2>();
            float nearest = 99f;
            for (int i = 0; i < 60 * 150; i++)
            {
                zone.Tick(dt, null);
                if (i % 6 != 0) continue;
                foreach (int k in trainCircles) { var c = track.Obstacles[k].Center; reached.Add(c); nearest = Math.Min(nearest, c.DistanceTo(cross)); }
            }
            int leaks = 0; Vector2 leak = default;
            float minZ = reached.Min(c => c.Y), maxZ = reached.Max(c => c.Y);
            for (float x = tr.X - 8f; x <= tr.X + 8f; x += 0.25f)
            for (float z = minZ - 4f; z <= maxZ + 4f; z += 1f)
            {
                var p = new Vector2(x, z);
                if (zone.OnRails(p, CarSim.CollisionRadius)) continue;
                foreach (var c in reached) if (c.DistanceTo(p) < 1.7f + CarSim.CollisionRadius) { leaks++; leak = p; break; }
            }
            Check(nearest < 1.2f, $"atomgrad: the train does run over the level crossing (its circles come {nearest:F2} m from the crossing's middle): a car held there would be in its way");
            Check(leaks == 0, $"atomgrad: the rails test covers every place the train reaches (z {minZ:F0}..{maxZ:F0}; {leaks} uncovered places{(leaks > 0 ? $", e.g. {leak}" : "")})");
            RemoveChild(zone); zone.Free(); RemoveChild(fx); fx.Free();
        }

        // ---- Atomgrad: a real rear-end fine right past the level crossing, the train coming; nobody at the wheel, or the brake kept on (the car stops close: he must back off) ----
        // The car stands 8 m past the line; the officer rams it from behind (the real collision), so he stops with his UAZ on the rails. With the race's rails rule (AutographRailsTick,
        // the clock running only while both are clear, the car held only then) nothing is ever on the train's way when it passes, and the autograph is signed in the end.
        foreach (string how in new[] { "nobody at the wheel", "brake kept on", "stopped close" })
        {
            bool braking = how != "nobody at the wheel", stoppedClose = how == "stopped close";
            var def = TrackDef.Load("res://tracks/atomgrad.json");
            var track = TrackBuilder.Build(def);
            var fx = new TyreEffects(); AddChild(fx); fx.Init(track);
            var zone = new ZoneLife(); AddChild(zone); zone.Init(track, fx, def.Zone);
            var tr = def.Zone.Train;
            var trainCircles = Enumerable.Range(0, track.Obstacles.Count).Where(i => MathF.Abs(track.Obstacles[i].Center.X - tr.X) < 0.01f && MathF.Abs(track.Obstacles[i].Radius - 1.7f) < 0.01f).ToList();
            var patrol = new KozlikPatrol(); AddChild(patrol); patrol.Init(track, fx, null, null, 0);
            var car = new CarSim(new CarParams(), track) { Obstacles = track.Obstacles };
            int cross = track.NearestCenter(new Vector2(tr.X, tr.Cross), out _), n = track.Center.Length;
            int carAt = (cross + 8) % n;
            SchoolScript.SetRolling(car, track, SchoolScript.RoadPoint(track, carAt, 0f), track.CenterDir(carAt), 0f);
            bool caught = false, signed = false, kozlikWasOnRails = false, backedOff = false;
            if (stoppedClose) { patrol.TestStartAutographOnRoad(carAt - 6.5f, 0f); caught = true; }      // the shove has died away with both stopped, his UAZ at the usual 6.5 m behind: on the rails
            else patrol.TestStartChaseOnRoad(carAt - 15, 0f, 0f, 9.5f);      // (the game's own RamDelta)
            patrol.Caught += () => caught = true;
            zone.CueSchoolTrain();
            float clock = 0f, worstTrainKozlik = -99f, worstTrainCar = -99f, trainNearest = 99f; int railsTicks = 0;
            for (int i = 0; i < 60 * 45 && !(signed && i > 60 * 30); i++)
            {
                zone.Tick(dt, car);
                patrol.WaitAtCrossing = RaceScene.KozlikWaitsAtCrossing(zone, patrol);      // (the race's GaiChaseTick, every tick)
                bool onRails = caught && !signed && RaceScene.AutographRailsTick(zone, patrol, car);
                if (caught && !signed && !onRails) { clock += dt; if (clock >= 6f) { patrol.EndAutograph(); signed = true; } }
                bool held = !caught || caught && !signed && !onRails;      // (before the ram the car stands braked, as in the ram tests)
                if (held) { car.SetControls(false, false, 0f, true); car.Brake = 1f; car.Neutral = true; }
                else if (braking) { car.SetControls(false, false, 0f, true); car.Brake = 1f; car.Neutral = true; }      // the player keeps the brake on
                else { car.SetControls(false, false, 0f, false); car.Brake = 0f; car.Neutral = true; }      // nobody at the wheel
                for (int sub = 0; sub < 2; sub++) car.Step(dt / 2f);
                patrol.Tick(dt, car, car.LastImpact);
                bool kOn = RaceScene.KozlikOnRails(zone, patrol.Position2D, patrol.Forward2D);
                if (caught && !signed && kOn) { kozlikWasOnRails = true; railsTicks++; }
                if (kozlikWasOnRails && !kOn) backedOff = true;
                Vector2 carC = new(car.Pos.X, car.Pos.Z), carF = new(car.Forward.X, car.Forward.Z);
                foreach (int k in trainCircles)
                {
                    var t = track.Obstacles[k];
                    trainNearest = Math.Min(trainNearest, t.Center.DistanceTo(new Vector2(tr.X, tr.Cross)));
                    for (int j = -1; j <= 1; j++)
                    {
                        float dk = t.Radius + KozlikPatrol.BodyRadius - t.Center.DistanceTo(patrol.Position2D + patrol.Forward2D * (j * KozlikPatrol.HalfLength));
                        if (dk > worstTrainKozlik && dk > 0f && OS.HasEnvironment("RAILSDBG")) GD.Print($"  train into the Kozlik {dk:F2} m at {i * dt:F1} s, signed {signed}, Kozlik at {patrol.Position2D}, {patrol.Speed:F1} m/s");
                        worstTrainKozlik = Math.Max(worstTrainKozlik, dk);
                        worstTrainCar = Math.Max(worstTrainCar, t.Radius + CarSim.CollisionRadius - t.Center.DistanceTo(carC + carF * (j * 1.3f)));
                    }
                }
            }
            Check(caught && kozlikWasOnRails, $"rails fine ({how}): {(stoppedClose ? "stopped for the autograph" : "the officer's ram caught the car")} 8 m past the line, his UAZ on the rails (caught {caught}, on the rails {railsTicks} ticks)");
            Check(backedOff, $"rails fine ({how}): he is off the rails before the train comes, by himself");
            Check(trainNearest < 1.2f, $"rails fine ({how}): the train did cross the road meanwhile ({trainNearest:F2} m from the crossing's middle)");
            Check(worstTrainKozlik < 0f && worstTrainCar < 0f, $"rails fine ({how}): the train never reached the Kozlik ({worstTrainKozlik:F2} m) or the car ({worstTrainCar:F2} m)");
            Check(signed, $"rails fine ({how}): the autograph is signed in the end ({clock:F1} s of the 6 counted)");
            RemoveChild(patrol); patrol.Free(); RemoveChild(zone); zone.Free(); RemoveChild(fx); fx.Free();
        }
        MoverBlockers.Clear(); MoverBlockers.ClearGhosts();
    }
}
