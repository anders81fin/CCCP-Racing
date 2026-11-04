using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Tests;

// T9 of the world tape, in the real game. --tape-scene-tests, scene 13. One headless child at a time; every variant first proves that a lap was really saved.
//   Siberia (the airbase): the jets (BalalaikaSorties: kind "jet") and the guided missiles (kind "missile", made to fly by --missile-test-repeat --missile-dry: every flying jet ahead of
//     the car fires both at it, without hurting it) are dynamic instances; their crash and burst are effect events. A lap that starts with a jet alive; the title replays it in the SAME
//     scene (no reload), then after a reload; windows chosen from the tape (a missile alive, a fresh crash, a burnt-out wreck); the same lap with recording off (the world trace, with the
//     jets' own state, is identical); a trip into a race and back (instances and effects leave and come back; the replay's car never asks for a collision circle; the circles of the
//     actors the tape poses are parked and put back); a foreign tape (the jets run live).
//   Bonus: the Hind actor's version 3 (the battery's dishes and the loader truck are posed by the tape) and the emitters of the tanks' dust and the BTRs' dust.
//   Babushka launch: the hit's effect event is recorded at the live site.
//   Inventory: every actor of every track, covered or not, and why.
public partial class PhysicsTest
{
    const string JetsKey = "Jets:BalalaikaSorties";

    static int TapeGroupInt(string line, string pattern) { var m = Regex.Match(line ?? "", pattern); return m.Success ? int.Parse(m.Groups[1].Value) : -1; }

    /// <summary>The instances a tape has, with their kinds, from its Spawn events.</summary>
    sealed record WorldTapeBodyView(List<TapeEvent> Events, (int Id, string Kind)[] Kinds);
    static WorldTapeBodyView TapeView(WorldTape t)
    {
        var body = WorldTapeCodec.ReadBody(t.Payload);
        return new WorldTapeBodyView(body.Events, body.Events.Where(e => e.Type == TapeEventType.Spawn).Select(e => (e.Instance, e.Kind)).Distinct().ToArray());
    }

    static string[] TapeScene6LapArgs(string track) => new[] { "--start", "--autodrive", "--track=" + track, "--ap-speed=0.8", "--tape-debug", "--tape-pos-every=2", "--tape-count-queries" };
    static string[] TapeScene6JetArgs => new[] { "--missile-test-repeat", "--missile-dry" };

    void TapeScene6()
    {
        TapeScene6Jets();
        TapeScene6Bonus();
        TapeScene6Babushka();
        TapeScene6Inventory();
    }

    /// <summary>Instance seeks of a replay of the jets: the first one (at <paramref name="t0"/>) and every later one restore the instances the tape has alive then.</summary>
    void TapeScene6Replay(TapeChild c, WorldTape t, int lap, float t0, string label, int minSeeks)
    {
        var body = WorldTapeCodec.ReadBody(t.Payload); var view = TapeView(t);
        var seeks = TapeSeekLines(c);
        var want = TapeLiveAt(body.Events, t0); var first = seeks.FirstOrDefault();
        bool setOk = first != null && Math.Abs(first.At - t0) < 0.01f && first.Inst.Select(i => i.Id).OrderBy(i => i).SequenceEqual(want.Keys.OrderBy(i => i)) && first.Inst.All(i => Math.Abs(i.Spawn - want[i.Id]) < 0.006f);
        string kinds = string.Join(",", want.Keys.OrderBy(i => i).Select(i => view.Kinds.FirstOrDefault(k => k.Id == i).Kind));
        TapeCheck(setOk && want.Count >= 1, $"{label}: the first seek ({t0:F2} s) restores exactly the {want.Count} instances the tape has alive then [{kinds}], each with its spawn time (ids {string.Join(",", want.Keys.OrderBy(k => k))} vs {string.Join(",", first?.Inst.Select(i => i.Id).OrderBy(i => i).ToArray() ?? new int[0])})");
        // where they stand: the recorded lap logged its instances at every 2 s of the lap (--tape-pos-every=2)
        var rec = c.All("pos-at ").Select(l => Regex.Match(l, @"pos-at ([\d.]+) lap (\d+): \[(.*)\]")).Where(m => m.Success && int.Parse(m.Groups[2].Value) == lap && Math.Abs(float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) - t0) < 0.01f).LastOrDefault();
        float worst = 0; int compared = 0;
        if (rec != null && first != null)
            foreach (var i in first.Inst)
            {
                var m = Regex.Match(rec.Groups[3].Value, $@"(?:^|,){i.Id}=\((-?[\d.]+),(-?[\d.]+),(-?[\d.]+)\)");
                if (m.Success && i.P is Vector3 p) { worst = MathF.Max(worst, p.DistanceTo(new Vector3(float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), float.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture), float.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture)))); compared++; }
            }
        TapeCheck(compared >= 1 && worst < 0.03f, $"{label}: those instances stand where the recording had them at {t0:F2} s (worst {worst * 100:F1} cm over {compared} instances; the log rounds to 1 cm)");
        TapeCheck(seeks.Count >= minSeeks && seeks.All(s => s.Hash == seeks[0].Hash && s.Inst.Count == seeks[0].Inst.Count && s.Inst.Zip(seeks[0].Inst).All(z => z.First.Id == z.Second.Id && z.First.P == z.Second.P)), $"{label}: {seeks.Count} seeks (the start and every wrap) restore the same instances at the same poses");
        TapeCheck(seeks.Skip(1).All(s => s.Nodes == s.Inst.Count), $"{label}: at every seek after the first the jets and missiles in the tree are exactly the restored instances (no leak: {string.Join(",", seeks.Skip(1).Take(14).Select(s => s.Nodes))} for {seeks.Skip(1).FirstOrDefault()?.Inst.Count})");
        var inst = c.Lines.LastOrDefault(l => l.StartsWith("tape: replay instances (replay done)"));
        int made = (int)TapeNum(inst, "made"), removed = (int)TapeNum(inst, "removed"), live = (int)TapeNum(inst, "live");
        int inTree = Regex.Matches(Regex.Match(inst ?? "", @"in the tree \[([^\]]*)\]").Groups[1].Value, @"=(\d+)").Sum(x => int.Parse(x.Groups[1].Value));
        TapeCheck(inst != null && made > 0 && made - removed == live && inTree == live && inst.Contains("live actors' own [0]"), $"{label}: at the end made {made} - removed {removed} = live {live} = the nodes in the tree {inTree}, and no live jet or missile of the actor was registered during the replay ({inst?.Trim()})");
        var exit = c.Find("scene exit: replay instances");
        TapeCheck(exit != null && exit.Contains("0 left") && (int)TapeNum(exit, "instances") >= made, $"{label}: leaving the scene despawned every replay-owned instance ({exit?.Trim()})");
        var sum = TapeSummaryLine(c, "replay done");
        TapeCheck(TapeCountOf(sum, "skipped", JetsKey) > 300 && TapeCountOf(sum, "live", JetsKey) == 0, $"{label}: the jets' actor is not simulated in the replay ({TapeCountOf(sum, "skipped", JetsKey)} steps skipped, {TapeCountOf(sum, "live", JetsKey)} live)");
    }

    void TapeScene6Jets()
    {
        string a = TapeSceneDir("jt"), off = TapeSceneDir("jtoff"), rel = TapeSceneDir("jtrel");
        string trA = a + ".trace.txt", trOff = off + ".trace.txt";
        const float S = 30f;
        try
        {
            var lapArgs = TapeScene6LapArgs("siberia").Concat(TapeScene6JetArgs).ToArray();
            var env = new Dictionary<string, string> { ["WORLDTRACE"] = trA, ["WORLDTRACE_FRAMES"] = "100000" };
            // ---- A: one lap (it starts with the jet the void lap before it left in the air), then the title replay in the SAME scene
            var c = TapeGame(a, lapArgs.Concat(new[] { "--tape-title-after-lap=1", $"--tape-replay-start={S.ToString(CultureInfo.InvariantCulture)}", "--tape-wrap-every=8", "--tape-replay-quit=48" }).ToArray(), 1500, env: env);
            var l1 = c.Find("tape: lap 1 ");
            TapeCheck(!c.TimedOut && c.Exit == 0 && !c.Crashed, $"scene 13 (same scene): the child ended cleanly (exit {c.Exit}, {c.Seconds:F0} s)");
            var (g, t, paired) = TapeSceneLoad(a, "siberia", 0);
            float lap1 = float.Parse(Regex.Match(l1 ?? "tape: lap 1 0 s", @"lap 1 (\d+\.\d+)").Groups[1].Value, CultureInfo.InvariantCulture);
            TapeCheck(l1 != null && l1.Contains("record True") && l1.Contains("recording True") && c.All("ghost+tape saved").Count() == 1 && g != null && t != null && paired && Math.Abs(t.LapTime - lap1) < 0.02f,
                $"scene 13 (same scene): PROOF a lap was saved: lap 1 is a record and was recorded, one pair save reported, ghost_siberia.bin and tape_siberia.bin on disk are its matching pair (tape {t?.LapTime:F2} s, lap {lap1:F2} s)");
            if (t == null) return;
            var body = WorldTapeCodec.ReadBody(t.Payload); var ev = body.Events; var view = TapeView(t);
            int N(string k) => ev.Count(e => TapeIsFx(e) && e.Kind == k);
            int jets = view.Kinds.Count(k => k.Kind == "jet"), missiles = view.Kinds.Count(k => k.Kind == "missile");
            TapeInfo($"scene 13 (Siberia): tape file {new FileInfo(TapeTapeFile(a, "siberia", 0)).Length / 1024} KB, {ev.Count} events, instances: {jets} jets, {missiles} missiles; effects [{TapeKinds(ev)}]");
            TapeCheck(jets >= 3 && missiles >= 2 && ev.Where(e => e.Type == TapeEventType.Spawn).All(e => e.Parent == JetsKey && e.Kind is "jet" or "missile") && ev.Count(e => e.Type == TapeEventType.Despawn) >= jets + missiles - 3,
                $"scene 13 (same scene): the tape holds the lap's {jets} jets and {missiles} guided missiles as instances of the jets' actor, spawned and despawned during the lap ({ev.Count(e => e.Type == TapeEventType.Spawn)} spawns, {ev.Count(e => e.Type == TapeEventType.Despawn)} despawns)");
            TapeCheck(N(TapeFxKind.JetCrash) >= 1 && N(TapeFxKind.MissileBurst) >= 1 && ev.Where(e => TapeIsFx(e) && e.Kind is TapeFxKind.JetCrash or TapeFxKind.MissileBurst).All(e => e.Source == JetsKey && e.Life > 0),
                $"scene 13 (same scene): the tape holds the jets' crashes ({N(TapeFxKind.JetCrash)}) and the missiles' bursts ({N(TapeFxKind.MissileBurst)}) as effect events of the jets' actor, with a life");
            // a lap that starts with a jet in the air (the void lap before it)
            var i1 = c.Find("instances of lap 1: ");
            string startIds = Regex.Match(i1 ?? "", @"alive at the start \d+ \[([\d,]*)\]").Groups[1].Value;
            var zero = ev.Where(e => e.Type == TapeEventType.Spawn && e.Time == 0f).Select(e => e.Instance).OrderBy(x => x).ToList();
            var kf0 = body.Keys[0].LiveInstances.Select(e => e.Instance).OrderBy(x => x).ToList();
            TapeCheck(startIds.Length > 0 && zero.SequenceEqual(startIds.Split(',').Select(int.Parse).OrderBy(x => x)) && kf0.SequenceEqual(zero), $"scene 13 (same scene): the lap started with {zero.Count} instance(s) the void lap left alive: they are spawned at 0 and in its first keyframe (events at 0 [{string.Join(",", zero)}], keyframe 0 [{string.Join(",", kf0)}])");
            TapeCheck(ev.Where(e => e.Type is TapeEventType.Spawn or TapeEventType.Despawn).All(e => e.Time < 1e-6f || MathF.Abs(e.Time * WorldTapeConst.TapeRate - MathF.Round(e.Time * WorldTapeConst.TapeRate)) < 1e-3f), "scene 13 (same scene): spawns and despawns are events on the sample grid");
            var outside = c.Find("outside the tape");
            TapeCheck(outside != null && outside.Contains("gulag truck is idle") && outside.Contains("mission stands"), $"scene 13 (same scene): the gulag truck stands idle through a lap that becomes a record ({outside?.Trim()})");
            // the cost: the finish line on the main thread
            var cost = c.Find("record cost of lap 1"); var fin = c.Lines.LastOrDefault(x => x.Contains("World tape: set 0 lap"));
            TapeInfo($"scene 13 (Siberia): {cost?.Trim()}; {fin?.Trim()}");
            TapeCheck(cost != null && TapeNum(cost, "avg") < 1.0f && TapeNum(cost, "p99") < 3.0f, $"scene 13 (same scene): the recording cost per sample with the jets in the index is within twice the budget (avg {TapeNum(cost, "avg"):F3} ms, p99 {TapeNum(cost, "p99"):F3} ms)");
            TapeCheck(fin != null && TapeNum(fin, "main thread") < 50, $"scene 13 (same scene): the finish line costs the main thread {TapeNum(fin, "main thread")} ms (under 50 ms): {fin?.Trim()}");
            // the title replays the fresh tape in the same scene
            TapeCheck(c.Has("title entered in the same scene #1 after lap 1: replaying True") && !c.Has("scene build #2") && TapeKeyList(c.Find("from tape: covers ["), "covers").Contains(JetsKey), $"scene 13 (same scene): the title replays the fresh tape in the same scene and covers the jets' actor ({c.Find("from tape: covers [")?.Split("live [")[0].Trim()})");
            TapeScene6Replay(c, t, 1, S, "scene 13 (same scene)", 5);
            // the replay's own checks of the effects the jets made
            var madeA = TapeFxMadeOf(c, "replay done");
            TapeCheck(madeA.GetValueOrDefault(TapeFxKind.JetCrash) >= 1, $"scene 13 (same scene): the replay made the jets' crashes the tape holds at or before {S:F0} s again, scorch and wreck included ([{string.Join(", ", madeA.Select(kv => kv.Key + "=" + kv.Value))}])");
            var em = c.Find("emitters (replay done)");
            TapeInfo($"scene 13 (Siberia): {em?.Trim()}");
            TapeCheck(em != null && em.Contains("plough-spray"), $"scene 13 (same scene): the replay's emitters threw the covered ploughs' snow ({em?.Trim()})");
            // ---- the collision policy: the replay's car never asks for a collision circle (A's replay is 48 s of replay among the laps)
            var q = c.Find("car obstacle queries (replay done)");
            TapeCheck(q != null && (int)TapeNum(q, "in the replay") == 0 && (int)TapeNum(q, "everywhere") > 100000, $"scene 13 (same scene): CarSim read the collision circles {TapeNum(q, "everywhere")} times in the two laps and 0 times in the replay: the replay's car is posed by the ghost and never stepped ({q?.Trim()})");
            var park = c.All("circles of covered actors parked").LastOrDefault();
            TapeCheck(TapeGroupInt(park, @"parked: (\d+) of") > 0 && TapeGroupInt(park, @"\((\d+) were on the map\)") > 0, $"scene 13 (same scene): the circles of the actors the tape poses were frozen on the map and are parked while it poses them ({park?.Trim()})");
            // the effects and instance restore are the same at each wrap
            TapeScene5Check(c, t, S, 8f, "scene 13 (same scene)", 4);

            // ---- A': the same lap with recording off: the world trace (it hashes the jets' and missiles' own state) and the lap time are identical
            var envOff = new Dictionary<string, string> { ["WORLDTRACE"] = trOff, ["WORLDTRACE_FRAMES"] = "100000" };
            var o = TapeGame(off, lapArgs.Concat(new[] { "--tape-quit-laps=1" }).ToArray(), 1500, tapeOff: true, env: envOff);
            TapeCheck(!o.TimedOut && o.Exit == 0 && !o.Crashed && !File.Exists(TapeTapeFile(off, "siberia", 0)) && File.Exists(TapeGhostFile(off, "siberia", 0)), $"scene 13 (recording off): the lap ran with CCCP_TAPE=off, a ghost and no tape ({o.Seconds:F0} s)");
            var lapOff = Regex.Match(o.Find("tape: lap 1 ") ?? "", @"lap 1 (\d+\.\d+)").Groups[1].Value;
            TapeCheck(lapOff.Length > 0 && lapOff == lap1.ToString("F2", CultureInfo.InvariantCulture), $"scene 13 (recording off): the lap time is identical ({lap1:F2} vs {lapOff})");
            if (File.Exists(trA) && File.Exists(trOff))
            {
                var ta = File.ReadAllLines(trA); var tb = File.ReadAllLines(trOff);
                int seenRacing = -1, cut = ta.Length;
                for (int k = 0; k < ta.Length; k++) { var st = ta[k].Split(' ', 3); if (st.Length > 2 && st[1] == "Racing") seenRacing = k; else if (seenRacing >= 0 && st.Length > 2 && st[1].StartsWith("Title")) { cut = k; break; } }
                int n = Math.Min(cut, tb.Length); bool same = n > 8000 && cut >= tb.Length - 3 && ta.Take(n).SequenceEqual(tb.Take(n));      // (the off run quits on the lap-done step; the on run waits for the worker before it enters the title, so it has a few more racing steps)
                TapeCheck(same, $"scene 13 (recording off): the car and world trace (with every jet's, missile's and wreck's state in each step's hash) is identical with recording on and off for the {n} steps of a Siberia lap with its sorties (off ran {tb.Length} steps)");
            }
            else TapeCheck(false, "scene 13 (recording off): both trace files exist");
            TapeSceneRm(off);

            // ---- C: replay-only windows chosen from the tape (copies of A's directory)
            var missileLife = ev.Where(e => e.Type == TapeEventType.Spawn && e.Kind == "missile").Select(e => (Spawn: e.Time, Id: e.Instance)).Where(m => m.Spawn > 8f && m.Spawn < t.LapTime - 14f).OrderBy(m => m.Spawn).ToList();
            var crashes = ev.Where(e => TapeIsFx(e) && e.Kind == TapeFxKind.JetCrash && e.Time > 4f && e.Time < t.LapTime - 20f).OrderBy(e => e.Time).ToList();
            var windows = new List<(string Name, float T0, float W)>();
            if (missileLife.Count > 0) windows.Add(("a guided missile in flight", MathF.Ceiling(missileLife[0].Spawn * 15f) / 15f + 0.6f, 6f));
            if (crashes.Count > 0) windows.Add(("a fresh crash", MathF.Floor(crashes[0].Time) + 1f, 6f));
            if (crashes.Count > 0) windows.Add(("a wreck that has burnt out", MathF.Min(MathF.Floor(crashes[0].Time) + 14f, t.LapTime - 10f), 6f));
            foreach (var (name, t0w, w) in windows)
            {
                float t0 = MathF.Round(Math.Max(1f, Math.Min(t0w, t.LapTime - w - 2f)) * 15f) / 15f; string d = TapeSceneDir("jtw");
                try
                {
                    TapeCopyDir(a, d);
                    var r = TapeGame(d, new[] { "--track=siberia", "--tape-debug", $"--tape-replay-start={t0.ToString(CultureInfo.InvariantCulture)}", $"--tape-wrap-every={w.ToString(CultureInfo.InvariantCulture)}", $"--tape-replay-quit={(4.5f * w).ToString(CultureInfo.InvariantCulture)}" }, 400);
                    TapeCheck(r.Exit == 0 && !r.Crashed && r.Has("from tape: covers") && TapeKeyList(r.Find("from tape: covers ["), "covers").Contains(JetsKey), $"scene 13 (window {name}): the replay ran from the tape and covers the jets ({r.Seconds:F0} s)");
                    TapeScene5Check(r, t, t0, w, $"scene 13 (window {name})", 3);
                    var s0 = TapeSeekLines(r).FirstOrDefault(); var fx0 = TapeFxSeeks(r).FirstOrDefault();
                    var alive = TapeLiveAt(ev, t0);
                    string kindsNow = string.Join(",", alive.Keys.OrderBy(i => i).Select(i => view.Kinds.FirstOrDefault(k => k.Id == i).Kind));
                    if (name.StartsWith("a guided missile"))
                        TapeCheck(s0 != null && alive.Keys.Any(i => view.Kinds.Any(k => k.Id == i && k.Kind == "missile")) && s0.Inst.Select(i => i.Id).OrderBy(i => i).SequenceEqual(alive.Keys.OrderBy(i => i)), $"scene 13 (window {name}): the seek at {t0:F2} s restored a missile in the air with the others alive then [{kindsNow}]");
                    else
                    {
                        int crashed = crashes.Count(e => e.Time <= t0);
                        TapeCheck(fx0 != null && fx0.Nodes >= crashed && crashed >= 1, $"scene 13 (window {name}): the seek at {t0:F2} s restored the {crashed} crash(es) before it: scorches stay (and the wreck while it burns): {fx0?.Nodes} effect nodes");
                    }
                }
                finally { TapeSceneRm(d); }
            }

            // ---- D: trips into a race and a preview and back; the circles of the covered actors are parked while the replay runs and put back
            foreach (bool preview in new[] { false, true })
            {
                string d = TapeSceneDir("jtt");
                try
                {
                    TapeCopyDir(a, d);
                    var r = TapeGame(d, new[] { "--track=siberia", "--tape-debug", "--tape-count-queries", $"--tape-replay-start={S.ToString(CultureInfo.InvariantCulture)}", "--tape-title-trip=4,10" + (preview ? ",preview" : ""), "--tape-replay-quit=12" }, 300);
                    string label = $"scene 13 ({(preview ? "preview" : "race")} trip)";
                    var seeks = TapeSeekLines(r); var rel2 = r.Find("released: covered parts back at rest");
                    TapeCheck(r.Exit == 0 && !r.Crashed && seeks.Count == 2 && seeks[0].Hash == seeks[1].Hash && seeks[0].Inst.Select(i => i.Id).SequenceEqual(seeks[1].Inst.Select(i => i.Id)), $"{label}: the replay seeks at the start and again on return, to the same instances ({seeks.Count} seeks, {seeks.FirstOrDefault()?.Inst.Count} instances)");
                    var m = Regex.Match(rel2 ?? "", @"replay instances (\d+) made, (\d+) removed, (\d+) left");
                    TapeCheck(m.Success && m.Groups[1].Value == m.Groups[2].Value && m.Groups[3].Value == "0", $"{label}: leaving the replay despawned every replay-owned jet and missile ({rel2?.Trim()})");
                    var parked = r.All("circles of covered actors parked").ToList(); var restored = r.All("circles of covered actors restored").ToList();
                    TapeCheck(parked.Count == 2 && restored.Count >= 1 && TapeGroupInt(restored[0], @"restored: (\d+) of") == TapeGroupInt(parked[0], @"parked: (\d+) of") && TapeGroupInt(restored[0], @"restored: (\d+) of") > 0, $"{label}: the parked circles of the covered actors are all put back when the replay is left, and parked again on return ({parked.FirstOrDefault()?.Trim()}; {restored.FirstOrDefault()?.Trim()})");
                    var qs = r.Find("car obstacle queries (replay done)");
                    TapeCheck(qs != null && (int)TapeNum(qs, "in the replay") == 0 && (int)TapeNum(qs, "everywhere") > (preview ? -1 : 1000), $"{label}: {(preview ? "(a preview drives no car)" : "the live race in between read the circles (so the counter works)")} and the replay never did ({qs?.Trim()})");
                }
                finally { TapeSceneRm(d); }
            }

            // ---- E: a foreign tape: the jets' actor differs, so they fly live in the replay (and nothing of theirs is made from the tape)
            string dv = TapeSceneDir("jtv");
            try
            {
                TapeCopyDir(a, dv);
                var f = Path.Combine(dv, Path.GetFileName(TapeTapeFile(a, "siberia", 0)));
                var tt = WorldTapeCodec.Decode(File.ReadAllBytes(f)); tt.ActorStructure[JetsKey].Hash ^= 1; File.WriteAllBytes(f, WorldTapeCodec.Encode(tt));
                var r = TapeGame(dv, new[] { "--track=siberia", "--tape-debug", "--tape-replay-start=30", "--tape-replay-quit=20" }, 300);
                var sumF = TapeSummaryLine(r, "replay done"); var instF = r.Lines.LastOrDefault(x => x.StartsWith("tape: replay instances (replay done)"));
                var madeF = TapeFxMadeOf(r, "replay done");
                TapeCheck(r.Exit == 0 && !r.Crashed && r.Has("from tape") && !TapeKeyList(r.Find("from tape: covers ["), "covers").Contains(JetsKey) && TapeCountOf(sumF, "live", JetsKey) > 300 && (int)TapeNum(instF, "made") == 0 && !madeF.ContainsKey(TapeFxKind.JetCrash) && !madeF.ContainsKey(TapeFxKind.MissileBurst),
                    $"scene 13 (foreign tape: the jets' structure differs): replays without exception, the jets' actor runs live ({TapeCountOf(sumF, "live", JetsKey)} steps), no jet, missile or crash is made from the tape (made [{string.Join(", ", madeF.Select(kv => kv.Key + "=" + kv.Value))}])");
            }
            finally { TapeSceneRm(dv); }

            // ---- B: record, reload the scene, replay in the fresh one (new index, new actors, a new sink)
            var cb = TapeGame(rel, lapArgs.Concat(new[] { "--tape-reload-after-lap=1", $"--tape-replay-start={S.ToString(CultureInfo.InvariantCulture)}", "--tape-wrap-every=8", "--tape-replay-quit=32" }).ToArray(), 1500);
            var (gb, tb2, pb) = TapeSceneLoad(rel, "siberia", 0);
            TapeCheck(!cb.TimedOut && cb.Exit == 0 && !cb.Crashed && gb != null && tb2 != null && pb && cb.Has("ghost+tape saved") && cb.Has("reloading the scene"), $"scene 13 (reload): PROOF a lap was saved in scene #1 (ghost+tape pair on disk, matching), then the scene was reloaded ({cb.Seconds:F0} s)");
            TapeCheck(cb.Has("scene build #2") && TapeKeyList(cb.Find("from tape: covers ["), "covers").Contains(JetsKey), "scene 13 (reload): the fresh scene #2 (fresh actors, fresh index) covers the jets from the tape of scene #1");
            if (tb2 != null)
            {
                TapeScene6Replay(cb, tb2, 1, S, "scene 13 (reload)", 3);
                TapeScene5Check(cb, tb2, S, 8f, "scene 13 (reload)", 3);
            }
        }
        finally { TapeSceneRm(a); TapeSceneRm(off); TapeSceneRm(rel); foreach (var f in new[] { trA, trOff }) try { File.Delete(f); } catch { } }
    }

    // ================================================================ Bonus: the battery body, the emitters

    void TapeScene6Bonus()
    {
        string a = TapeSceneDir("jb");
        try
        {
            var c = TapeGame(a, TapeScene6LapArgs("bonus").Concat(new[] { "--tape-title-after-lap=1", "--tape-replay-start=40", "--tape-wrap-every=8", "--tape-replay-quit=40" }).ToArray(), 1500);
            var (g, t, paired) = TapeSceneLoad(a, "bonus", 0);
            TapeCheck(!c.TimedOut && c.Exit == 0 && !c.Crashed && g != null && t != null && paired && c.All("ghost+tape saved").Count() == 1, $"scene 13 (Bonus): PROOF a lap was saved: the matching pair is on disk ({c.Seconds:F0} s, tape {t?.LapTime:F2} s)");
            if (t == null) return;
            var hind = t.ActorStructure.GetValueOrDefault("Jets:HindSorties");
            var body = WorldTapeCodec.ReadBody(t.Payload);
            var parts = body.Parts.Where(p => p.ActorKey == "Jets:HindSorties").Select(p => p.Part).ToList();
            TapeCheck(hind != null && hind.Version == 3 && new[] { "l0/radar", "l1/radar", "loader", "crane", "hoist", "lifted", "wheel0", "wheel5" }.All(parts.Contains), $"scene 13 (Bonus): the Hind actor is at version {hind?.Version} and holds the battery's dishes and the loader truck as parts (loader, crane, hoist, lifted round, 6 wheels, 2 dishes: {parts.Count(p => p.StartsWith("l") || p is "loader" or "crane" or "hoist" or "lifted" || p.StartsWith("wheel"))} of {parts.Count} parts)");
            var counters = c.Find("counters of lap 1");
            TapeCheck(counters != null && !TapeBracket(counters, "unregistered spins by part").Contains("HindSorties/l0/radar") && !TapeBracket(counters, "unregistered spins by part").Contains("HindSorties/wheel"), $"scene 13 (Bonus): the dishes and the loader's wheels are registered spinners (no unregistered spin of them: [{TapeBracket(counters, "unregistered spins by part")}])");
            var fin = c.Lines.LastOrDefault(x => x.Contains("World tape: set 0 lap"));
            TapeInfo($"scene 13 (Bonus): {c.Find("record cost of lap 1")?.Trim()}; {fin?.Trim()}; tape file {new FileInfo(TapeTapeFile(a, "bonus", 0)).Length / 1024} KB");
            TapeCheck(fin != null && TapeNum(fin, "main thread") < 50, $"scene 13 (Bonus): the finish line costs the main thread {TapeNum(fin, "main thread")} ms (under 50 ms): {fin?.Trim()}");
            var em = c.Find("emitters (replay done)");
            var emMade = Regex.Matches(em ?? "", @"(tank-dust|tank-exhaust|btr-dust)=(\d+)").ToDictionary(m => m.Groups[1].Value, m => int.Parse(m.Groups[2].Value));
            TapeCheck(em != null && emMade.GetValueOrDefault("tank-dust") + emMade.GetValueOrDefault("tank-exhaust") > 0 && emMade.GetValueOrDefault("btr-dust") > 0, $"scene 13 (Bonus): the replay's emitters made the covered tanks' track dust and exhaust and the BTRs' dust ({em?.Trim()})");
            var sum = TapeSummaryLine(c, "replay done");
            TapeCheck(TapeCountOf(sum, "skipped", "Jets:HindSorties") > 300, "scene 13 (Bonus): the Hind actor's aircraft are posed by the tape (skipped live)");
            var q = c.Find("car obstacle queries (replay done)");
            TapeCheck(q != null && (int)TapeNum(q, "in the replay") == 0, $"scene 13 (Bonus): the replay's car reads no collision circle here either ({q?.Trim()})");
            // the tank-crush test must not fell trees from a frozen hull in a replay of a tape that knows the breakables: the replay broke exactly the trees the tape has
            var seeks = TapeFxSeeks(c); var ev = body.Events;
            int brokenBy = ev.Count(e => e.Type == TapeEventType.Effect && e.Effect == TapeEffect.BreakableBroken && e.Time <= 40f);
            TapeCheck(seeks.Count > 0 && seeks.All(s => s.Broken == seeks[0].Broken) && seeks[0].Broken == brokenBy, $"scene 13 (Bonus): the replay shows exactly the {brokenBy} breakables the tape has broken by 40 s, none more from a frozen tank hull at every wrap ({string.Join(",", seeks.Select(s => s.Broken))})");
        }
        finally { TapeSceneRm(a); }
    }

    // ================================================================ the babushka launch

    void TapeScene6Babushka()
    {
        string d = TapeSceneDir("jbab");
        try
        {
            var c = TapeGame(d, new[] { "--start", "--autodrive", "--track=ilyich", "--babushka-hit=40", "--tape-debug", "--tape-quit-at=10" }, 200);
            var launch = c.All("fx event babushka-launch").ToList(); var can = c.All("fx event canister-burst").ToList();
            TapeCheck(c.Exit == 0 && !c.Crashed && launch.Count >= 1 && launch.All(l => l.Contains("recording True") && l.Contains("Babushkas:Babushkas")), $"scene 13 (babushka launch): the live hit of a babushka is recorded at the live site ({launch.Count} launch event(s) while a lap was recorded, from Babushkas:Babushkas; {can.Count} burst can(s))");
            TapeInfo($"scene 13 (babushka launch): {launch.FirstOrDefault()?.Trim()}; {can.FirstOrDefault()?.Trim()}");
        }
        finally { TapeSceneRm(d); }
    }

    // ================================================================ the inventory

    sealed record TapeInv(string Actor, string Track, bool Covered, string Reason, string Status = null);

    void TapeScene6Inventory()
    {
        // The actors the world lists (RaceScene.World.cs ListActors) are read from the real game: each track's child prints the ones the tape indexes and the ones it does not.
        // Everything else that moves in a lap and is not in that list is a row of its own, with the reason (checked against the live lists: it is in neither).
        var expectMarked = new Dictionary<string, string[]>
        {
            ["ilyich"] = new[] { "None:WoodFences", "Tractor:DrunkTractor", "Slurry:SlurryTractor", "Bus:Bus", "Kozlik:Kozlik", "None:KgbOfficers", "Babushkas:Babushkas", "Drunks:DrunkMen", "None:RescueTractor" },
            ["siberia"] = new[] { "None:WoodFences", "Ploughs:SnowPloughs", "Column:PrisonerColumn", "Kozlik:Kozlik", "Jets:BalalaikaSorties", "Details:SiberiaDetails", "None:KgbOfficers", "Babushkas:Babushkas", "Drunks:DrunkMen", "None:RescueTractor" },
            ["atomgrad"] = new[] { "None:WoodFences", "Bus:Bus", "Ploughs:SnowPloughs", "Zone:ZoneLife", "Kozlik:Kozlik", "None:KgbOfficers", "Babushkas:Babushkas", "Drunks:DrunkMen", "None:RescueTractor" },
            ["bonus"] = new[] { "None:WoodFences", "Kozlik:Kozlik", "Jets:HindSorties", "None:KgbOfficers", "None:CampGate", "Btr:BtrPatrols", "Tanks:TankBattle", "Babushkas:Troopers", "Drunks:DrunkMen", "None:RescueTractor" },
        };
        var unmarkedWhy = new Dictionary<string, string>
        {
            ["Liaison:LiaisonOfficer"] = "recorded under KgbOfficers' tree (one index entry, not two)",
            ["Jets:BalalaikaSorties"] = "no hangar or runway on this track: the actor is built and does nothing",
            ["Details:SiberiaDetails"] = "no airbase crew plan on this track: the actor is empty",
        };
        var rows = new List<TapeInv>();
        foreach (var track in TapeFamilies)
        {
            string d = TapeSceneDir("inv" + track);
            try
            {
                var c = TapeGame(d, new[] { "--start", "--autodrive", "--track=" + track, "--tape-debug", "--tape-quit-at=2" }, 200);
                var idx = c.Find("tape: index actors ["); var marked = TapeKeyList(idx, "index actors"); var unmarked = TapeKeyList(idx, "unmarked");
                var want = expectMarked[track].Concat(new[] { "None:SpeedTrap", "Depot:FuelDepot", "None:RescueTowRope" }).ToArray();
                TapeCheck(c.Exit == 0 && idx != null && marked.OrderBy(x => x).SequenceEqual(want.OrderBy(x => x)), $"scene 13 (inventory {track}): the tape indexes exactly [{string.Join(", ", want)}] (got [{string.Join(", ", marked)}])");
                var missing = unmarked.Where(u => !unmarkedWhy.ContainsKey(u)).ToList();
                TapeCheck(missing.Count == 0, $"scene 13 (inventory {track}): every actor the tape does not index has a stated reason (without: [{string.Join(", ", missing)}])");
                foreach (var m in marked) rows.Add(new TapeInv(m.Split(':')[1], track, true, "indexed: marked parts" + (m.EndsWith("HindSorties") ? " + instances (Hinds, battery missiles) + the battery's dishes and loader truck" : m.EndsWith("BalalaikaSorties") ? " + instances (jets, guided missiles)" : "")));
                foreach (var u in unmarked) rows.Add(new TapeInv(u.Split(':')[1], track, false, unmarkedWhy[u]));
            }
            finally { TapeSceneRm(d); }
        }
        int workers = KolkhozWorkersCount();
        var outside = new (string Actor, string Track, bool Covered, string Reason, string[] Absent)[]
        {
            ("GulagTruck", "all", false, "idle through every lap that can become a record: it is called only when the mission has failed (RaceScene.Fate), when the lap is lost; the lap report logs it idle", new[] { "GulagTruck" }),
            ("RescueTractor", "all", true, "indexed (None:RescueTractor): root, model, driver, head and engine sound; None:RescueTowRope separately records unscaled endpoints and visibility, reconstructing the stretched rope without changing legacy tractor compatibility", Array.Empty<string>()),
            ($"KolkhozWorkers ({workers} workers)", "ilyich", false, "each worker is a skeleton animated from its own clock in _Process at 24 Hz (13 moving nodes: body, head, arms, hips, knees, feet, held work, rod, line) and knocked over by the live car; it is not a WorldActor, so a covered _Process cannot be skipped; wiring means ~" + (workers * 13) + " parts and a replay flag in the model", new[] { "KolkhozWorkers" }),
            ("VillageLife", "ilyich", false, "ambient washing, a flock that flees the car and a donkey that kicks, all driven from _Process by the live car's position; decoration with no collision, no WorldActor, nothing to skip", new[] { "VillageLife" }),
            ("PartyDacha (building, yard)", "ilyich", false, "n/a (static): the building and the yard never move, so there is nothing to record", Array.Empty<string>()),
            ("PartyRowboat, PartyPondDuck, DachaPatrol", "ilyich", false, "ambient movers in _Process (the rowboat's hunt, the duck, three guards on a beat knocked over by the live car); not WorldActors; ~dozens of animated nodes", new[] { "PartyRowboat", "DachaPatrol" }),
            ("ZoneDosimetrist", "atomgrad", false, "built after the actor list and the tape index (RaceScene.cs, near the speed trap); building it earlier shifts the obstacle slots the tape's breakable ids and the world trace use, and the index must be made before anything moves; his _Process also writes his pose every frame", new[] { "Dosimetrist", "ZoneDosimetrist" }),
            ("Buk21Battery body", "bonus", true, "indexed as parts of the Hind actor (version 3): both launchers' dishes (registered spinners), the loader truck's pose, crane, hoist, lifted round and six wheels; the turrets, racks and rounds were already; the replay no longer steps the battery", Array.Empty<string>()),
            ("Bus fire (burning wreck)", "ilyich, atomgrad", true, "persistent bus-fire event restores the wreck, charring, age-driven flame/smoke and crackle; sampled dynamic passengers and engine pause; version 2 suppresses native simulation", Array.Empty<string>()),
            ("WoodFences (breaking fences)", "ilyich", true, "indexed under None:WoodFences: geometry-versioned break events, retained broken spans and deterministic debris restored by age; compatible replay skips live fence collisions", Array.Empty<string>()),
            ("GAI chase effects (Kozlik's exhaust, the bear's breath)", "ilyich, siberia, atomgrad, bonus", false, "the patrol's poses, beacon and engine are indexed (Kozlik:Kozlik); its per-step exhaust smoke and the bear's breath puffs are emitted from KozlikPatrol.Tick and are not made by the replay", new[] { "KozlikChase" }),
            ("Hind landing-light beams and exhaust", "bonus", false, "the lights follow the recorded AircraftState (powered, beam allowed); the beams' own shading and the rotor exhaust are per-step and not made by the replay", new[] { "HindBeams" }),
            ("BonusExerciseLife (marshal, battery crew, cook)", "bonus", false, "ticks live in the replay by design (the Hind actor's crew), animated from the replay car; its workers' skeletons are not recorded", new[] { "ExerciseCrew" }),
        };
        foreach (var o in outside) rows.Add(new TapeInv(o.Actor, o.Track, o.Covered, o.Reason, o.Reason.StartsWith("n/a (static)") ? "n/a (static)" : null));
        // none of the rows outside the list may be one of the lists' keys (the claim "not an actor" is checked by the unmarked/marked sets above: they are exact)
        var all = expectMarked.Values.SelectMany(v => v).Concat(unmarkedWhy.Keys).ToList();
        foreach (var o in outside) TapeCheck(o.Absent.All(n => !all.Any(k => k.EndsWith(":" + n))), $"scene 13 (inventory): {o.Actor} is not among the actors the world lists, as the row says");
        TapeInfo("scene 13 (inventory): actor | track | covered | reason");
        foreach (var r in rows.OrderBy(r => r.Track).ThenBy(r => r.Actor)) TapeInfo($"scene 13 (inventory): {r.Actor,-44} | {r.Track,-8} | {r.Status ?? (r.Covered ? "COVERED" : "not covered"),-12} | {r.Reason}");
        TapeCheck(rows.All(r => r.Covered || r.Reason.Length > 20), $"scene 13 (inventory): the table has {rows.Count} rows ({rows.Count(r => r.Covered)} covered, {rows.Count(r => !r.Covered && r.Status == null)} not covered, {rows.Count(r => r.Status != null)} static) and every row that is not covered states its reason");
    }

    int KolkhozWorkersCount() => CCCPRacing.Track.KolkhozWorkers.Layout(CCCPRacing.Track.TrackBuilder.Build(CCCPRacing.Track.TrackDef.Load("res://tracks/ilyich.json"))).Count;
}
