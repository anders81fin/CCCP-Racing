using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CCCPRacing.Car;
using CCCPRacing.Net;
using CCCPRacing.Race;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    /// <summary>LAN RACE: the secret ballot, the standings, remote car snapshots and laps from the grid.</summary>
    void LanTest()
    {
        GD.Print("LAN race");
        LanBallotTest();
        LanRankingTest();
        LanSnapshotTest();
        LanAuthorityTest();
        LanGridLapsTest();
    }

    void LanBallotTest()
    {
        var tied = LanBallot.Tally(new[] { 50, 50, 200 }, out var counts);
        Check(tied.SequenceEqual(new[] { 50 }) && counts[50] == 2 && counts[200] == 1, "ballot: the majority wins outright (50, 50, 200 -> 50)");
        tied = LanBallot.Tally(new[] { 200, 50 }, out _);
        Check(tied.SequenceEqual(new[] { 50, 200 }), "ballot: a 2-way tie goes to the drum with the tied powers only");
        tied = LanBallot.Tally(new[] { 200, 50, 100 }, out _);
        Check(tied.SequenceEqual(new[] { 50, 100, 200 }), "ballot: a 3-way tie, all three on the drum");
        tied = LanBallot.Tally(new[] { 50, 200, 50, 200 }, out _);
        Check(tied.SequenceEqual(new[] { 50, 200 }), "ballot: 2 + 2 votes, the drum has just those two");
        tied = LanBallot.Tally(new[] { 200, 200, 50, LanBallot.NoVote }, out counts);
        Check(tied.SequenceEqual(new[] { 200 }) && counts[100] == 1, "ballot: no vote counts as 100 %, 200 wins 2-1-1");

        Check(LanBallot.Rule(new[] { true, true, false }, 1, 1), "rules: ON majority wins");
        Check(!LanBallot.Rule(new[] { false, true, false }, 1, 1), "rules: OFF majority wins");
        Check(LanBallot.Rule(new[] { true, true }, 1, 1) && !LanBallot.Rule(new[] { false, false }, 1, 1), "rules: unanimous votes need no lottery");
        int damageOn = 0, fuelOn = 0, different = 0;
        bool reproducible = true;
        for (int seed = 0; seed < 10000; seed++)
        {
            bool damage = LanBallot.Rule(new[] { true, false }, seed, 0x44414d);
            bool fuel = LanBallot.Rule(new[] { false, true }, seed, 0x465545);
            damageOn += damage ? 1 : 0; fuelOn += fuel ? 1 : 0; different += damage != fuel ? 1 : 0;
            reproducible &= damage == LanBallot.Rule(new[] { false, true }, seed, 0x44414d);
        }
        Check(reproducible, "rules: all machines reproduce the same tied result regardless of ballot order");
        Check(Math.Abs(damageOn - 5000) < 200 && Math.Abs(fuelOn - 5000) < 200 && Math.Abs(different - 5000) < 200,
            "rules: tied damage and fuel votes have independent, balanced draws");

        // The draw: equal chances among the tied, over many seeds.
        bool uniform = true;
        string spread = "";
        foreach (int n in new[] { 2, 3, 4 })
        {
            var hits = new int[n];
            const int Seeds = 60000;
            for (int s = 0; s < Seeds; s++) hits[LanBallot.Draw(n, s * 7919 + 13)]++;
            float worst = hits.Max(h => Math.Abs(h / (float)Seeds * n - 1f));
            uniform &= worst < 0.03f;
            spread += $" {n}: {string.Join("/", hits)}";
        }
        Check(uniform, $"drum draw: each tied power within 3 % of an equal share ({spread.Trim()})");

        // The drum stops on the drawn winner on every screen: position is a pure function of the seed and time.
        bool stops = true, monotonic = true;
        for (int s = 0; s < 200; s++)
            for (int n = 2; n <= 4; n++)
            {
                int w = LanBallot.Draw(n, s);
                stops &= LanBallot.DrumIndex(n, LanBallot.DrumPosition(n, w, s, LanBallot.DrumSpin)) == w
                         && LanBallot.DrumIndex(n, LanBallot.DrumPosition(n, w, s, LanBallot.DrumSpin + 5f)) == w;
                float prev = -1;
                for (float t = 0; t <= LanBallot.DrumSpin; t += 0.05f)
                {
                    float p = LanBallot.DrumPosition(n, w, s, t);
                    monotonic &= p >= prev;
                    prev = p;
                }
            }
        Check(stops && monotonic, "drum: spins forward only, slows down and stops on the winner");
        float p0 = LanBallot.DrumPosition(2, 1, 42, 0.1f), p1 = LanBallot.DrumPosition(2, 1, 42, 2.9f) - LanBallot.DrumPosition(2, 1, 42, 2.8f);
        Check(p0 > 0.5f && p1 < 0.15f, $"drum: fast at first ({p0:F2} options in 0.1 s), crawling at the end ({p1:F3} in the last 0.1 s)");

        var ids = new long[] { 1, 88, 12345, 7 };
        var g1 = LanBallot.Shuffle(ids, 99);
        Check(g1.OrderBy(x => x).SequenceEqual(ids.OrderBy(x => x)) && g1.SequenceEqual(LanBallot.Shuffle(ids, 99)), "grid by lot: a permutation, the same from the same seed");
        var firsts = new int[4];
        for (int s = 0; s < 8000; s++) firsts[Array.IndexOf(ids, LanBallot.Shuffle(ids, s)[0])]++;
        Check(firsts.All(f => Math.Abs(f / 2000f - 1f) < 0.08f), $"grid by lot: everyone gets the pole equally often ({string.Join("/", firsts)})");

        var tl = new LanBallot.Timeline(3, true);
        Check(tl.Tally > 3 * LanBallot.Step && tl.Drum > tl.Tally && tl.Announce >= tl.Drum + LanBallot.DrumSpin && tl.End > tl.Grid,
            $"count timeline: ballots, tally {tl.Tally:F1} s, drum {tl.Drum:F1} s, chosen {tl.Announce:F1} s, grid {tl.Grid:F1} s, end {tl.End:F1} s");
    }

    void LanRankingTest()
    {
        const int N = 1234, G = LapTimer.GateStep;
        float Score(int laps, int gate, int prog) => LanRace.Score(laps, gate, prog, N, G);
        // Along a lap: monotonic from the grid, over the line, through every gate, into lap 2.
        var t = new LapTimer(new TrackData { Center = Enumerable.Range(0, N).Select(i => new Vector2(i, 0)).ToArray(), HalfWidth = Enumerable.Repeat(4f, N).ToArray() });
        Check(t.GateCount == N / G, $"{t.GateCount} gates on a {N} m lap");
        Check(Score(0, 1, N - 20) < Score(0, 1, N - 14) && Score(0, 1, N - 14) < Score(0, 1, 5) && Score(0, 1, 19) < Score(0, 2, 20),
            "score: row 2 < pole row < past the line < the next gate");
        Check(Score(0, t.GateCount, N - 3) < Score(1, 1, 2) && Score(1, 1, 2) < Score(1, 2, 21), "score: over the line into lap 2 counts up");
        Check(Score(2, 1, 5) > Score(1, t.GateCount, N - 1), "score: a lap ahead beats the end of the lap before");

        var r = LanRace.Rank(new[]
        {
            new LanRace.Standing { Id = 1, Score = Score(1, 10, 190) },
            new LanRace.Standing { Id = 2, Finished = true, FinishTime = 130.5f },
            new LanRace.Standing { Id = 3, Score = Score(1, 30, 590) },
            new LanRace.Standing { Id = 4, Finished = true, FinishTime = 128.2f },
            new LanRace.Standing { Id = 5, Dnf = true, Score = Score(2, 5, 90) },
        });
        Check(r.Select(s => s.Id).SequenceEqual(new long[] { 4, 2, 3, 1, 5 }), $"ranking: finishers by time, then the rest by progress, DNF last ({string.Join(" ", r.Select(s => s.Id))})");
    }

    void LanSnapshotTest()
    {
        var buf = new SnapshotBuffer();
        Snapshot S(float t, float x, float vx) => new() { Time = t, Pos = new Vector3(x, 0, 0), Frame = new Ghost.Frame { Vel = new Vector3(vx, 0, 0), Rpm = 1000 + t * 1000 } };
        // A car doing 30 m/s along X, sampled at 30 Hz; one sample arrives late (out of order).
        for (int i = 0; i <= 10; i++)
            if (i != 5) buf.Add(S(i / 30f, i, 30));
        buf.Add(S(5 / 30f, 5, 30));
        buf.Add(S(5 / 30f, 99, 30));     // a duplicate: ignored
        buf.Pose(4.5f / 30f, out var mid, out _);
        Check(Math.Abs(mid.X - 4.5f) < 1e-3f, $"snapshots: interpolated between two samples (x {mid.X:F3}, want 4.5; late sample slotted in)");
        buf.Pose(10 / 30f + 0.1f, out var ex, out _);
        Check(Math.Abs(ex.X - (10 + 3f)) < 1e-3f, $"snapshots: past the newest, extrapolated along the velocity (x {ex.X:F2}, want 13)");
        buf.Pose(10 / 30f + 2f, out var held, out _);
        Check(Math.Abs(held.X - (10 + 30 * SnapshotBuffer.MaxExtrapolate)) < 1e-3f, $"snapshots: extrapolation stops after {SnapshotBuffer.MaxExtrapolate * 1000:0} ms, then holds (x {held.X:F2})");
        buf.Pose(-1f, out var early, out _);
        Check(early.X == 0, "snapshots: before the first, the first");
        var qa = new Basis(Vector3.Up, 0).GetRotationQuaternion();
        var qb = new Basis(Vector3.Up, 1f).GetRotationQuaternion();
        var rb = new SnapshotBuffer();
        rb.Add(new Snapshot { Time = 0, Rot = qa });
        rb.Add(new Snapshot { Time = 1, Rot = qb });
        rb.Pose(0.5f, out _, out var rq);
        Check(Math.Abs(rq.GetEuler().Y - 0.5f) < 1e-3f, $"snapshots: rotation slerped ({rq.GetEuler().Y:F3} rad, want 0.5)");
        for (int i = 11; i < 200; i++) buf.Add(S(i / 30f, i, 30));
        Check(buf.Count <= SnapshotBuffer.Keep * 30 + 3, $"snapshots: old ones forgotten ({buf.Count} kept)");

        // The wire format carries everything the puppet needs.
        var track = new FlatGround();
        var car = new CarSim(new CarParams(), track);
        car.Reset(new Vector3(3, 0.6f, -7), 0.7f);
        car.SetControls(true, false, 0.4f, false);
        Run(car, 0.8f);
        var snap = Snapshot.Of(car, 12.345f, 6.5f, 2, 17, 345, true);
        snap.WorldLightProgress=2.75f;
        snap.FuelPickupSerial=3;
        snap.GridReadyCount=LanSession.MaxPlayers;
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, true)) snap.Write(w);
        int size = (int)ms.Length;
        ms.Position = 0;
        var back = Snapshot.Read(new BinaryReader(ms));
        bool same = back.Time == snap.Time && back.Pos == snap.Pos && back.Rot.IsEqualApprox(snap.Rot) && back.Frame.Vel == snap.Frame.Vel
                    && back.Frame.Rpm == snap.Frame.Rpm && back.Frame.Gear == snap.Frame.Gear && back.Frame.Steer == snap.Frame.Steer && back.Frame.Impact == 6.5f
                    && back.Laps == 2 && back.NextGate == 17 && back.Progress == 345 && back.Finished
                    && back.WorldLightProgress == 2.75f && back.FuelPickupSerial == 3 && back.GridReadyCount == LanSession.MaxPlayers
                    && Enumerable.Range(0, 4).All(k => back.Wheels[k].Length == snap.Wheels[k].Length && back.Wheels[k].Contact == snap.Wheels[k].Contact && back.Wheels[k].Surface == snap.Wheels[k].Surface);
        Check(same, $"snapshot: survives the wire ({size} bytes)");
        // The puppet posed from it: body, revs and wheels as sent.
        var pb = new SnapshotBuffer();
        pb.Add(back);
        var puppet = new CarSim(new CarParams(), track);
        float applied = float.NegativeInfinity;
        float impact = pb.Apply(12.345f, puppet, 1 / 120f, ref applied);
        Check(puppet.Pos == car.Pos && Math.Abs(puppet.Rpm - car.Rpm) < 1e-3f && puppet.Gear == car.Gear && impact == 6.5f
              && Math.Abs(puppet.Wheels[0].VisualLength - car.Wheels[0].VisualLength) < 1e-5f, "snapshot: the puppet takes the pose, the revs, the gear, the springs and the hit");
    }

    /// <summary>LapTimer from the 3x2 grid: no car gets a lap for free, none needs an extra one.</summary>
    void LanGridLapsTest()
    {
        var track = TrackBuilder.Build(TrackDef.Load("res://tracks/bonus.json"));
        int n = track.Center.Length;
        var widths = Enumerable.Range(0, LanSession.MaxPlayers).Select(g => track.HalfWidth[LanRace.GridCenter(track, g)]).ToArray();
        Check(widths.All(w => w >= LanRace.Column + 1.2f), $"grid: the road is wide enough for two abreast (half widths {string.Join(", ", widths.Select(w => w.ToString("F1")))} m, columns at {LanRace.Column} m)");
        LanRace.GridPose(track, 0, out var pole, out float poleYaw);
        Vector2 forward=track.CenterDir(0), pole2=new(pole.X,pole.Z);
        float behind=(track.Center[0]-pole2).Dot(forward);
        Vector3 facing=new Basis(Vector3.Up,poleYaw)*Vector3.Forward;
        float aligned=facing.Dot(new Vector3(forward.X,0,forward.Y));
        // Single-player now starts 2.51 m behind the line; LAN deliberately keeps 14 m.
        Check(Math.Abs(behind-LanRace.PoleLine)<1 && aligned>.99f,
            $"grid: pole row waits {LanRace.PoleLine} m before the line facing forward (behind {behind:F2}, alignment {aligned:F4})");
        bool allGood = true;
        string report = "";
        for (int g = 0; g < LanSession.MaxPlayers; g++)
        {
            LanRace.GridPose(track, g, out var pos, out _);
            var timer = new LapTimer(track);
            var laps = new List<float>();
            float travelled = 0;
            timer.LapCompleted += (_, _) => laps.Add(travelled);
            timer.VoidLapCompleted += _ => laps.Add(travelled);
            timer.Start();
            // Drive the grid column's line (offset from the centre) at 1 m a step for three laps and a bit.
            int start = LanRace.GridCenter(track, g);
            float side = g % 2 == 0 ? -LanRace.Column : LanRace.Column;
            Vector2 first = default;
            for (int k = 0; k <= 3 * n + 40; k++)
            {
                int i = (start + k) % n;
                Vector2 dir = track.CenterDir(i);
                Vector2 p = track.Center[i] + new Vector2(-dir.Y, dir.X) * side;
                if (k == 0) first = p;
                travelled = k;
                timer.Update(0.03f, p);
            }
            float lead = LanRace.PoleLine + g / 2 * LanRace.RowGap;     // m from the slot to the line
            bool ok = laps.Count == 3 && first.DistanceTo(new Vector2(pos.X, pos.Z)) < 0.01f;
            for (int l = 0; ok && l < 3; l++) ok = Math.Abs(laps[l] - (lead + (l + 1) * n)) <= 2f;
            allGood &= ok;
            report += $" slot {g + 1}: {string.Join("/", laps.Select(x => (x - lead).ToString("F0")))}";
        }
        Check(allGood, $"grid: every slot completes lap k at the line after k full laps, not at the start (m past the line:{report}, lap {n} m)");
    }
}
