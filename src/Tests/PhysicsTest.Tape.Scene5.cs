using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Tests;

// Phase 3 of the world tape (T4c): the effects, projectiles and breakables, in the real game. --tape-scene-tests, scene 12. One headless child at a time.
//   Bonus (the busiest world): two autodriven laps, the second a record that starts with the first one's effects going; the title replays it in the SAME scene (no reload),
//     then in windows chosen from what the tape holds (replay-only children on copies of the pair): the sink made exactly the effects the tape has in each window, pass after
//     pass, with the ages restored at every wrap; leaving the replay for a race or a preview leaves no effect behind; foreign and corrupt tapes fall back to live. One lap
//     with a scene reload in between proves the same on a fresh scene.
//   Ilyich (trees): a lap fells a tree at a known second (--tape-blast); the replayed world's tree lies only after that second, and stands again at every wrap; the second lap
//     starts with the first one's tree already down; a replay that starts after the second shows both lying.
// Every variant first proves that a lap was really saved (the pair is on disk and matches).
public partial class PhysicsTest
{
    sealed record TapeFxSeek(int N, float At, int Nodes, int Particles, int Projectiles, int Pieces, int Broken, int Lying)
    {
        public (int, int, int, int, int, int) State => (Nodes, Particles, Projectiles, Pieces, Broken, Lying);
    }

    static List<TapeFxSeek> TapeFxSeeks(TapeChild c)
    {
        var list = new List<TapeFxSeek>();
        foreach (var l in c.All("tape: seek fx #"))
        {
            var m = Regex.Match(l, @"seek fx #(\d+) at (-?[\d.]+): nodes (\d+) particles (\d+) projectiles (\d+) pieces (\d+) broken (\d+) lying (\d+)");
            if (m.Success) list.Add(new TapeFxSeek(int.Parse(m.Groups[1].Value), float.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups[3].Value), int.Parse(m.Groups[4].Value), int.Parse(m.Groups[5].Value), int.Parse(m.Groups[6].Value), int.Parse(m.Groups[7].Value), int.Parse(m.Groups[8].Value)));
        }
        return list;
    }

    static Dictionary<string, int> TapeFxMadeOf(TapeChild c, string why)
    {
        var l = c.Lines.LastOrDefault(x => x.StartsWith($"tape: fx ({why})"));
        var d = new Dictionary<string, int>();
        var m = Regex.Match(l ?? "", @"made \[([^\]]*)\]");
        if (m.Success && m.Groups[1].Value.Length > 0) foreach (var kv in m.Groups[1].Value.Split(',')) { var p = kv.Split('='); d[p[0]] = int.Parse(p[1]); }
        return d;
    }

    static bool TapeIsFx(TapeEvent e) => e.Type is TapeEventType.Effect or TapeEventType.Projectile && e.Kind != null && e.Kind != TapeFxKind.Marker;
    static string TapeKinds(IEnumerable<TapeEvent> ev) => string.Join(", ", ev.Where(TapeIsFx).GroupBy(e => e.Kind).OrderBy(g => g.Key).Select(g => $"{g.Key}={g.Count()}"));

    /// <summary>
    /// How many times the player can have asked the sink for effects of a kind in one pass of a replay window [t0, t0 + w): the ones going at t0 (restored by the seek, with their age)
    /// and the ones that begin inside the window (fired as the replay plays). The ends of the window and of an effect are blurred by two physics steps either way.
    /// </summary>
    static (int Low, int High) TapeFxBounds(IEnumerable<TapeEvent> ev, string kind, float t0, float w)
    {
        const float eps = 2f / 60f; int low = 0, high = 0;
        foreach (var e in ev.Where(e => TapeIsFx(e) && e.Kind == kind))
        {
            bool goingAtStart = e.Time <= t0 && e.End >= t0, goingAtStartSure = e.Time <= t0 - eps && e.End >= t0 + eps;
            bool begins = e.Time > t0 && e.Time <= t0 + w, beginsSure = e.Time > t0 + eps && e.Time <= t0 + w - eps;
            if (goingAtStartSure || beginsSure) low++;
            if (goingAtStart || begins || (e.Time <= t0 + eps && e.End >= t0 - eps) || (e.Time <= t0 + w + eps && e.Time > t0 - eps)) high++;
        }
        return (low, high);
    }

    void TapeScene5()
    {
        TapeScene5Bonus();
        TapeScene5Breakables();
    }

    // ================================================================ Bonus: the Hinds, the battery, the tanks, the BTRs

    void TapeScene5Check(TapeChild c, WorldTape tape, float t0, float w, string label, int minPasses)
    {
        var ev = WorldTapeCodec.ReadBody(tape.Payload).Events;
        var seeks = TapeFxSeeks(c); var made = TapeFxMadeOf(c, "replay done");
        int passes = Math.Max(0, seeks.Count - 1);
        TapeCheck(passes >= minPasses && seeks.All(s => Math.Abs(s.At - t0) < 0.02f), $"{label}: the replay of [{t0:F1} s, {t0 + w:F1} s) started and wrapped {passes} times, every seek at {t0:F1} s");
        var kinds = ev.Where(TapeIsFx).Select(e => e.Kind).Distinct().OrderBy(k => k).ToList();
        var bad = new List<string>(); var seen = new List<string>();
        foreach (var k in kinds)
        {
            var (low, high) = TapeFxBounds(ev, k, t0, w); int got = made.GetValueOrDefault(k);
            seen.Add($"{k}: made {got} for {low}..{high} per pass");
            if (got < passes * low || got > (passes + 1) * high) bad.Add($"{k} {got} not in [{passes * low}, {(passes + 1) * high}]");
        }
        foreach (var k in made.Keys.Where(k => !kinds.Contains(k))) bad.Add($"{k} made {made[k]} but the tape has none");
        TapeCheck(bad.Count == 0 && made.Count > 0, $"{label}: the sink made exactly the effects the tape has in the window, pass after pass ({string.Join("; ", seen)}){(bad.Count == 0 ? "" : " BAD: " + string.Join("; ", bad))}");
        // the restored state at every wrap is the same: nothing multiplies and nothing is left from the pass before
        TapeCheck(seeks.Count >= 2 && seeks.All(s => s.State == seeks[0].State), $"{label}: effect nodes, particles, projectiles and pieces restored at every one of {seeks.Count} seeks are identical (no leak): {(seeks.Count > 0 ? seeks[0].State.ToString() : "none")}");
    }

    void TapeScene5Bonus()
    {
        string a = TapeSceneDir("fx5a"), rel = TapeSceneDir("fx5rel");
        try
        {
            var lapArgs = new[] { "--start", "--autodrive", "--track=bonus", "--ap-speed=0.8", "--tape-lap-speeds=0,1", "--tape-debug", "--tape-trace-alloc" };
            // ---- A: two laps (the second a record that starts with the first one's effects going), then the title replay in the SAME scene
            var c = TapeGame(a, lapArgs.Concat(new[] { "--tape-title-after-lap=2", "--tape-replay-start=40", "--tape-wrap-every=6", "--tape-replay-quit=66" }).ToArray(), 1500);
            var l1 = c.Find("tape: lap 1 "); var l2 = c.Find("tape: lap 2 ");
            TapeCheck(!c.TimedOut && c.Exit == 0 && !c.Crashed, $"scene 12 (same scene): the child ended cleanly (exit {c.Exit}, {c.Seconds:F0} s)");
            var (g, t, paired) = TapeSceneLoad(a, "bonus", 0);
            float lap2 = float.Parse(Regex.Match(l2 ?? "tape: lap 2 0 s", @"lap 2 (\d+\.\d+)").Groups[1].Value, CultureInfo.InvariantCulture);
            TapeCheck(l1 != null && l2 != null && l1.Contains("record True") && l2.Contains("record True") && l2.Contains("recording True") && c.All("ghost+tape saved").Count() == 2 && g != null && t != null && paired && Math.Abs(t.LapTime - lap2) < 0.02f,
                $"scene 12 (same scene): PROOF a lap was saved: lap 1 and the faster lap 2 are records, two pair saves reported, ghost_bonus.bin and tape_bonus.bin on disk are lap 2's matching pair (tape {t?.LapTime:F2} s, lap 2 {lap2:F2} s)");
            if (t == null) return;
            var body = WorldTapeCodec.ReadBody(t.Payload); var ev = body.Events;
            TapeInfo($"scene 12 (Bonus): tape file {new FileInfo(TapeTapeFile(a, "bonus", 0)).Length / 1024} KB, {ev.Count} events, effect events [{TapeKinds(ev)}]");

            // (7) the Hind sortie's effects are on the tape again: the expected kinds, with their sources
            int N(string k) => ev.Count(e => TapeIsFx(e) && e.Kind == k);
            TapeCheck(ev.Any(e => e.Kind == TapeFxKind.Marker) && N(TapeFxKind.HindTracer) > 20 && N(TapeFxKind.HindImpact) > 5 && N(TapeFxKind.RotorWash) > 5 && N(TapeFxKind.RunWarning) >= 1,
                $"scene 12 (same scene): the tape holds the Hinds' missing effects again: tracers {N(TapeFxKind.HindTracer)}, impacts {N(TapeFxKind.HindImpact)}, rotor wash {N(TapeFxKind.RotorWash)}, run-in warnings {N(TapeFxKind.RunWarning)}");
            TapeInfo($"scene 12 (Bonus): shoot-down and wreck: fall {N(TapeFxKind.HindFall)}, smoke trail {N(TapeFxKind.HindTrail)}, crash {N(TapeFxKind.HindCrash)}; battery: launches {N(TapeFxKind.BukLaunch)}, trail {N(TapeFxKind.BukTrail)}, bursts {N(TapeFxKind.BukBurst)}; tanks: blasts {N(TapeFxKind.TankBlast)}, shells {N(TapeFxKind.TankShell)}, impacts {N(TapeFxKind.TankImpact)}, ricochets {N(TapeFxKind.TankRicochet)}; BTR tracers {N(TapeFxKind.BtrTracer)}");
            var hindKinds = new[] { TapeFxKind.HindTracer, TapeFxKind.HindImpact, TapeFxKind.RotorWash, TapeFxKind.HindTrail, TapeFxKind.HindFall, TapeFxKind.HindCrash, TapeFxKind.RunWarning, TapeFxKind.BukLaunch, TapeFxKind.BukTrail, TapeFxKind.BukBurst };
            TapeCheck(ev.Where(e => TapeIsFx(e) && hindKinds.Contains(e.Kind)).All(e => e.Source == "Jets:HindSorties") && ev.Where(e => TapeIsFx(e) && e.Kind.StartsWith("tank-")).All(e => e.Source == "Tanks:TankBattle") && ev.Where(e => TapeIsFx(e) && e.Kind == TapeFxKind.BtrTracer).All(e => e.Source == "Btr:BtrPatrols"),
                "scene 12 (same scene): every effect event names the actor it came from (so an actor that does not match the tape drops its events with its poses)");
            TapeCheck(ev.Where(e => TapeIsFx(e) && e.Kind != TapeFxKind.Breakable).All(e => float.IsFinite(e.Time) && e.Life > 0) && ev.Where(e => e.Type == TapeEventType.Projectile).All(e => e.Life > 0 && e.Life < 6), "scene 12 (same scene): every effect has a life and a finite time; every projectile flew a positive, short time");

            // (2) a lap that STARTS with effects going records them: events at or before 0 that are still going, all of them in the first keyframe
            var fx2 = c.Find("fx of lap 2:");
            int carried = (int)TapeNum(Regex.Match(fx2 ?? "", @"(\d+) effects carried over").Value, "");
            var going0 = ev.Where(e => TapeIsFx(e) && e.Time <= 0f && e.Life > 0 && e.End > 0f).ToList();
            var kf0 = body.Keys[0].LiveEffects;
            TapeCheck(carried >= 1 && going0.Count(e => e.Time < 0f) >= carried && kf0.Count == going0.Count && going0.All(e => kf0.Contains(e)),
                $"scene 12 (same scene): lap 2 started with {carried} of lap 1's effects going: they are on the tape at negative times and all {going0.Count} are in its first keyframe ({kf0.Count}); ages [{string.Join(", ", going0.Where(e => e.Time < 0).Take(6).Select(e => $"{e.Kind} {-e.Time:F1} s"))}]");
            TapeCheck(ev.Count(e => e.Kind == TapeFxKind.Marker) == 1, "scene 12 (same scene): the tape carries its marker (a replay of it does not guess the breakables from the car)");

            // (5) the recording cost, and where the garbage comes from: the recorder's own code allocates nothing per sample; the spawn-time costs are the event blocks of the first lap
            var cost = c.Find("record cost of lap 2"); var cost1 = c.Find("record cost of lap 1"); var fx1 = c.Find("fx of lap 1:");
            TapeInfo($"scene 12 (Bonus): {cost1?.Trim()}");
            TapeInfo($"scene 12 (Bonus): {cost?.Trim()}");
            TapeInfo($"scene 12 (Bonus): {fx1?.Trim()}");
            TapeInfo($"scene 12 (Bonus): {fx2?.Trim()}");
            foreach (var (lapNo, line) in new[] { (1, cost1), (2, cost) })
            {
                var ph = Regex.Match(line ?? "", @"sound ticks (\d+), part samples (\d+), keyframes (\d+)"); long steady = (long)TapeNum(Regex.Match(line ?? "", @"allocated (\d+) bytes in the").Value, "allocated");
                var again = Regex.Matches(line ?? "", @"three more reads (\d+) B").Select(m => int.Parse(m.Groups[1].Value)).ToList();
                TapeCheck(line != null && ph.Success && ph.Groups[1].Value == "0" && ph.Groups[3].Value == "0" && again.All(x => x == 0) && steady <= 70000 && line.Contains("a gen0 collection ran inside 0 "),
                    $"scene 12 (same scene): lap {lapNo}: the recorder's own sound ticks and keyframes allocate nothing; {steady} bytes in all come from {again.Count} one-off reads of Godot nodes that allocate nothing when read again, and no collection ran in a window");
            }
            TapeCheck(cost != null && TapeNum(cost, "avg") < 1.0f && TapeNum(cost, "p99") < 3.0f, $"scene 12 (same scene): the record cost per sample with the effect hooks is within twice the budget (avg {TapeNum(cost, "avg"):F3} ms, p99 {TapeNum(cost, "p99"):F3} ms)");
            long hook2 = (long)TapeNum(Regex.Match(fx2 ?? "", @"hook itself allocated (\d+) bytes").Value, "allocated"), hook1 = (long)TapeNum(Regex.Match(fx1 ?? "", @"hook itself allocated (\d+) bytes").Value, "allocated");
            TapeCheck(fx1 != null && fx2 != null && hook2 <= 4096 && hook1 < 400000, $"scene 12 (same scene): the effect hooks' spawn-time allocation is the event blocks of the first lap ({hook1} bytes) and next to nothing from the second ({hook2} bytes)");
            var sv = c.Lines.Where(x => x.Contains("World tape: set 0 lap")).LastOrDefault();
            TapeCheck(sv != null && TapeNum(sv, "main thread") < 50, $"scene 12 (same scene): the finish line costs the main thread {TapeNum(sv, "main thread")} ms with the effect events (under 50 ms; the pair save took {TapeNum(sv, "pair save")} ms on a worker)");

            // the title replays the fresh tape in the same scene: the sink made effects
            TapeCheck(c.Has("title entered in the same scene #1 after lap 2: replaying True") && !c.Has("scene build #2"), "scene 12 (same scene): the title replays the fresh tape in the same scene");
            var madeA = TapeFxMadeOf(c, "replay done"); var seeksA = TapeFxSeeks(c);
            TapeCheck(madeA.Count > 0 && seeksA.Count >= 8 && seeksA.All(s => s.State == seeksA[0].State), $"scene 12 (same scene): the replay [40 s, 46 s) made [{string.Join(", ", madeA.Select(kv => kv.Key + "=" + kv.Value))}] and restored the same state at each of {seeksA.Count} seeks ({(seeksA.Count > 0 ? seeksA[0].State.ToString() : "none")})");
            var exit = c.Find("scene exit fx:");
            TapeCheck(exit != null && TapeNum(exit, "nodes") == 0 && TapeNum(exit, "particles") == 0 && TapeNum(exit, "projectiles") == 0 && TapeNum(exit, "pieces") == 0, $"scene 12 (same scene): leaving the scene took every effect the replay made ({exit?.Trim()})");

            // ---- C: replay-only windows chosen from the tape: exactly the tape's effects, pass after pass
            float first(string kind) => ev.Where(e => TapeIsFx(e) && e.Kind == kind).Select(e => e.Time).DefaultIfEmpty(-1f).Min();
            var windows = new List<(string Name, float T0, float W)> { ("the first Hind strafing run", Math.Max(2f, MathF.Floor(first(TapeFxKind.HindImpact)) - 3f), 9f) };
            if (N(TapeFxKind.HindFall) > 0) windows.Add(("a shoot-down and its debris", MathF.Floor(first(TapeFxKind.HindFall)) + 1.5f, 8f));
            if (N(TapeFxKind.TankShell) > 0) windows.Add(("tank fire", Math.Max(2f, MathF.Floor(first(TapeFxKind.TankShell)) - 2f), 8f));
            if (N(TapeFxKind.BukTrail) > 0) windows.Add(("a battery missile", Math.Max(2f, MathF.Floor(first(TapeFxKind.BukLaunch)) - 1f), 8f));
            foreach (var (name, t0w, w) in windows)
            {
                float t0 = Math.Max(1f, Math.Min(t0w, t.LapTime - w - 2f));      // (a window must lie inside the lap, or the ghost's end wraps it early)
                string d = TapeSceneDir("fx5w");
                try
                {
                    TapeCopyDir(a, d);
                    var r = TapeGame(d, new[] { "--track=bonus", "--tape-debug", $"--tape-replay-start={t0.ToString(CultureInfo.InvariantCulture)}", $"--tape-wrap-every={w.ToString(CultureInfo.InvariantCulture)}", $"--tape-replay-quit={(4.5f * w).ToString(CultureInfo.InvariantCulture)}" }, 400);
                    TapeCheck(r.Exit == 0 && !r.Crashed && r.Has("from tape: covers") && TapeKeyList(r.Find("from tape: covers ["), "covers").Contains("Jets:HindSorties"), $"scene 12 (window {name}): the replay ran from the tape and covers the Hind sorties ({r.Seconds:F0} s)");
                    TapeScene5Check(r, t, t0, w, $"scene 12 (window {name})", 3);
                    if (name.StartsWith("a shoot-down"))
                    {
                        var s0 = TapeFxSeeks(r).FirstOrDefault(); var m = TapeFxMadeOf(r, "replay done");
                        TapeCheck(s0 != null && s0.Pieces == 4 && m.GetValueOrDefault(TapeFxKind.HindFall) > 0, $"scene 12 (window {name}): a seek 1.5 s after the shoot-down restores its four pieces of debris (pieces {s0?.Pieces}, fall made {m.GetValueOrDefault(TapeFxKind.HindFall)})");
                    }
                    if (name == "tank fire")
                    {
                        var m = TapeFxMadeOf(r, "replay done");
                        TapeCheck(m.GetValueOrDefault(TapeFxKind.TankShell) > 0 && m.GetValueOrDefault(TapeFxKind.TankBlast) > 0, $"scene 12 (window {name}): the tank's blasts and its shell in flight were made ({m.GetValueOrDefault(TapeFxKind.TankBlast)} blasts, {m.GetValueOrDefault(TapeFxKind.TankShell)} shells)");
                    }
                }
                finally { TapeSceneRm(d); }
            }

            // (3) leaving the replay for a race or a preview leaves no effect behind; coming back restores the same
            float tripT0 = Math.Max(2f, first(TapeFxKind.HindImpact) + 0.5f);      // (an impact is going: there is something to leave behind)
            foreach (bool preview in new[] { false, true })
            {
                string d = TapeSceneDir("fx5t");
                try
                {
                    TapeCopyDir(a, d);
                    var r = TapeGame(d, new[] { "--track=bonus", "--tape-debug", $"--tape-replay-start={tripT0.ToString(CultureInfo.InvariantCulture)}", "--tape-title-trip=4,10" + (preview ? ",preview" : ""), "--tape-replay-quit=12" }, 300);
                    string label = $"scene 12 ({(preview ? "preview" : "race")} trip)";
                    var seeks = TapeFxSeeks(r); var before = TapeFxMadeOf(r, "before the trip"); var rel2 = r.Find("released fx:"); var ex = r.Find("scene exit fx:");
                    TapeCheck(r.Exit == 0 && !r.Crashed && seeks.Count == 2 && seeks[0].State == seeks[1].State, $"{label}: the replay seeks at the start and again on return to the same effects ({seeks.Count} seeks, state {seeks.FirstOrDefault()?.State})");
                    TapeCheck(before.Count > 0 && seeks[0].Nodes + seeks[0].Particles + seeks[0].Projectiles > 0, $"{label}: there were effects to leave behind (made before the trip [{string.Join(", ", before.Select(kv => kv.Key + "=" + kv.Value))}])");
                    TapeCheck(rel2 != null && TapeNum(rel2, "nodes") == 0 && TapeNum(rel2, "particles") == 0 && TapeNum(rel2, "projectiles") == 0 && TapeNum(rel2, "pieces") == 0 && TapeNum(rel2, "broken") == 0, $"{label}: leaving the replay for gameplay took away every effect node, particle, projectile and piece it made ({rel2?.Trim()})");
                    TapeCheck(ex != null && TapeNum(ex, "nodes") == 0 && TapeNum(ex, "particles") == 0 && TapeNum(ex, "projectiles") == 0, $"{label}: and the scene's exit left none ({ex?.Trim()})");
                }
                finally { TapeSceneRm(d); }
            }

            // (6) corrupt and foreign tapes fall back to live: no effect is made from them
            var tpName = Path.GetFileName(TapeTapeFile(a, "bonus", 0));
            var variants = new List<(string Name, Action<string> Make, Func<TapeChild, bool> Expect)>
            {
                ("truncated body", d => { var f = Path.Combine(d, tpName); var b = File.ReadAllBytes(f); File.WriteAllBytes(f, b[..(b.Length * 6 / 10)]); }, r => !r.Has("from tape")),
                ("garbage file", d => File.WriteAllBytes(Path.Combine(d, tpName), Enumerable.Range(0, 5000).Select(i => (byte)(i * 31 + 7)).ToArray()), r => !r.Has("from tape")),
                ("another ghost's fingerprint", d => { var f = Path.Combine(d, tpName); var tt = WorldTapeCodec.Decode(File.ReadAllBytes(f)); tt.GhostFingerprint ^= 1; File.WriteAllBytes(f, WorldTapeCodec.Encode(tt)); }, r => !r.Has("from tape") && r.Has("belongs to another ghost")),
                ("the Hind actor's structure differs", d => { var f = Path.Combine(d, tpName); var tt = WorldTapeCodec.Decode(File.ReadAllBytes(f)); tt.ActorStructure["Jets:HindSorties"].Hash ^= 1; File.WriteAllBytes(f, WorldTapeCodec.Encode(tt)); }, r => r.Has("from tape") && !TapeKeyList(r.Find("from tape: covers ["), "covers").Contains("Jets:HindSorties")),
            };
            foreach (var (name, make, expect) in variants)
            {
                string d = TapeSceneDir("fx5v");
                try
                {
                    TapeCopyDir(a, d); make(d);
                    var r = TapeGame(d, new[] { "--track=bonus", "--tape-debug", $"--tape-replay-start={tripT0.ToString(CultureInfo.InvariantCulture)}", "--tape-wrap-every=9", "--tape-replay-quit=14" }, 300, env: new() { ["HINDDBG"] = "1" });
                    var m = TapeFxMadeOf(r, "replay done");
                    bool noHindFx = m.Keys.All(k => !hindKinds.Contains(k));
                    TapeCheck(r.Exit == 0 && !r.TimedOut && !r.Crashed && expect(r) && noHindFx && r.Lines.Count(x => x.StartsWith("HIND ")) > 0, $"scene 12 (foreign tape: {name}): replays without exception, makes no Hind effect (made [{string.Join(", ", m.Select(kv => kv.Key + "=" + kv.Value))}]) and the sorties run live{(r.Crashed ? " (CRASHED)" : "")}");
                }
                finally { TapeSceneRm(d); }
            }

            // ---- B: record lap 1, reload the scene, replay in the fresh one (new index, new actors, a new sink)
            var cb = TapeGame(rel, new[] { "--start", "--autodrive", "--track=bonus", "--tape-debug", "--tape-reload-after-lap=1", "--tape-replay-start=40", "--tape-wrap-every=6", "--tape-replay-quit=30" }, 900);
            var (gb, tb, pb) = TapeSceneLoad(rel, "bonus", 0);
            TapeCheck(!cb.TimedOut && cb.Exit == 0 && !cb.Crashed && gb != null && tb != null && pb && cb.Has("ghost+tape saved") && cb.Has("reloading the scene"), $"scene 12 (reload): PROOF a lap was saved in scene #1 (ghost+tape pair on disk, matching), then the scene was reloaded ({cb.Seconds:F0} s)");
            TapeCheck(cb.Has("scene build #2") && TapeKeyList(cb.Find("from tape: covers ["), "covers").Contains("Jets:HindSorties"), "scene 12 (reload): the fresh scene #2 covers the Hind sorties from the tape of scene #1");
            TapeInfo($"scene 12 (reload): {cb.Find("record cost of lap 1")?.Trim()}");
            if (tb != null)
            {
                TapeInfo($"scene 12 (reload): effect events [{TapeKinds(WorldTapeCodec.ReadBody(tb.Payload).Events)}]");
                TapeScene5Check(cb, tb, 40f, 6f, "scene 12 (reload)", 4);
                // the BTR patrol's tracers (a lap at the autopilot's own speed has them): a window around the first volley
                var bev = WorldTapeCodec.ReadBody(tb.Payload).Events.Where(e => TapeIsFx(e) && e.Kind == TapeFxKind.BtrTracer).ToList();
                TapeCheck(bev.Count > 0 && bev.All(e => e.Source == "Btr:BtrPatrols"), $"scene 12 (reload): the lap's tape holds the BTR patrol's tracers ({bev.Count})");
                if (bev.Count > 0)
                {
                    float bt0 = Math.Max(1f, Math.Min(MathF.Floor(bev.Min(e => e.Time)) - 2f, tb.LapTime - 12f)); string d = TapeSceneDir("fx5btr");
                    try
                    {
                        TapeCopyDir(rel, d);
                        var r = TapeGame(d, new[] { "--track=bonus", "--tape-debug", $"--tape-replay-start={bt0.ToString(CultureInfo.InvariantCulture)}", "--tape-wrap-every=8", "--tape-replay-quit=36" }, 300);
                        TapeCheck(r.Exit == 0 && !r.Crashed && r.Has("from tape: covers"), $"scene 12 (window the BTR's volley): the replay ran from the tape ({r.Seconds:F0} s)");
                        TapeScene5Check(r, tb, bt0, 8f, "scene 12 (window the BTR's volley)", 3);
                        var m = TapeFxMadeOf(r, "replay done");
                        TapeCheck(m.GetValueOrDefault(TapeFxKind.BtrTracer) > 0, $"scene 12 (window the BTR's volley): the BTR's tracers were made ({m.GetValueOrDefault(TapeFxKind.BtrTracer)})");
                    }
                    finally { TapeSceneRm(d); }
                }
            }
        }
        finally { TapeSceneRm(a); TapeSceneRm(rel); }
    }

    // ================================================================ Ilyich: a tree falls at a known second

    void TapeScene5Breakables()
    {
        string a = TapeSceneDir("fx5b"), rel = TapeSceneDir("fx5brel");
        try
        {
            // ---- same scene: lap 1 fells a tree at 15 s, the record lap 2 at 25 s (and starts with lap 1's tree down); the replay window [12 s, 28 s) wraps
            var c = TapeGame(a, new[] { "--start", "--autodrive", "--track=ilyich", "--ap-speed=0.8", "--tape-lap-speeds=0,1", "--tape-debug", "--tape-blast=15,25", "--tape-title-after-lap=2", "--tape-replay-start=12", "--tape-wrap-every=16", "--tape-replay-quit=70" }, 900);
            var (g, t, paired) = TapeSceneLoad(a, "ilyich", 0);
            var l2 = c.Find("tape: lap 2 ");
            float lap2 = float.Parse(Regex.Match(l2 ?? "tape: lap 2 0 s", @"lap 2 (\d+\.\d+)").Groups[1].Value, CultureInfo.InvariantCulture);
            TapeCheck(!c.TimedOut && c.Exit == 0 && !c.Crashed && g != null && t != null && paired && c.All("ghost+tape saved").Count() == 2 && Math.Abs(t.LapTime - lap2) < 0.02f, $"scene 12 (trees, same scene): PROOF a lap was saved: two pair saves, the pair of lap 2 on disk matches ({c.Seconds:F0} s, tape {t?.LapTime:F2} s)");
            var blasts = c.All("tape: blast: lap").ToList();
            var felled = blasts.Select(l => (int)TapeNum(l, "felled")).ToList();
            TapeCheck(blasts.Count >= 2 && felled.Take(2).All(n => n >= 1), $"scene 12 (trees, same scene): the blasts felled trees on purpose, one in each lap and again after a void lap at the start line ({blasts.Count} blasts: {string.Join(" | ", blasts.Take(3).Select(l => l.Trim()))})");
            if (t == null || blasts.Count < 2) return;
            var body = WorldTapeCodec.ReadBody(t.Payload); var ev = body.Events;
            var brk = ev.Where(e => e.Type == TapeEventType.Effect && e.Effect == TapeEffect.BreakableBroken).ToList();
            var atStart = brk.Where(e => e.Time <= 0f).Select(e => (int)e.Param).ToList(); var during = brk.Where(e => e.Time > 1f).ToList();
            float a2 = during.Count > 0 ? during.Min(e => e.Time) : -1f;
            var fx2 = c.Find("fx of lap 2:");
            TapeCheck(atStart.Count >= 1 && atStart.Count == (int)TapeNum(Regex.Match(fx2 ?? "", @"(\d+) breakables").Value, "") && during.Count >= 1 && a2 > 24.5f && a2 < 26f,
                $"scene 12 (trees, same scene): lap 2's tape has lap 1's {atStart.Count} broken tree(s) at 0 and its own {during.Count} at {a2:F2} s (the blast was at 25)");
            bool keysOk = body.Keys.Count > 6;
            foreach (var k in body.Keys)
            {
                float tk = k.Sample / WorldTapeConst.TapeRate; var want = brk.Where(e => e.Time <= tk).Select(e => (int)e.Param).ToHashSet();
                keysOk &= k.Broken.ToHashSet().SetEquals(want);
            }
            TapeCheck(keysOk && body.Keys[0].Broken.ToHashSet().SetEquals(atStart) && body.Keys.Last().Broken.Count == atStart.Count + during.Count, $"scene 12 (trees, same scene): every keyframe holds the trees broken by its time (first {body.Keys[0].Broken.Count}, last {body.Keys.Last().Broken.Count} of {body.Keys.Count} keyframes)");
            TapeCheck(ev.Count(e => e.Kind == TapeFxKind.Marker) == 1, "scene 12 (trees, same scene): the tape carries its marker, so the replay does not guess the breakables from the car");
            TapeScene5Trees(c, atStart.Count, during.Count, a2, 12f, "scene 12 (trees, same scene)", 3);

            // ---- a replay that starts AFTER the blast: both trees are down from the first frame and stay down through the wraps
            string d = TapeSceneDir("fx5bl");
            try
            {
                TapeCopyDir(a, d);
                var r = TapeGame(d, new[] { "--track=ilyich", "--tape-debug", "--tape-replay-start=40", "--tape-wrap-every=8", "--tape-replay-quit=30" }, 300);
                var seeks = TapeFxSeeks(r);
                int both = atStart.Count + during.Count;
                TapeCheck(r.Exit == 0 && !r.Crashed && seeks.Count >= 3 && seeks.All(s => s.Broken == both && s.Lying == both), $"scene 12 (trees, start after the blast): a seek at 40 s shows both blasts' trees down and lying, at every wrap ({string.Join(", ", seeks.Select(s => $"{s.Broken}/{s.Lying}"))} of {both})");
            }
            finally { TapeSceneRm(d); }

            // ---- a seek in the middle of a fall shows what playing shows: the replay starts a second before the tree of lap 2 falls (at a2) and, when playing reaches a2 + 0.2, 0.7, 1.3 and 3 s
            // (the fall, the landing and bounce, settled), seeks to that same clock and ticks the scenery as a wrap does; the felled trees' real model transforms are logged both ways
            string dp = TapeSceneDir("fx5bp");
            try
            {
                TapeCopyDir(a, dp);
                float[] at = { a2 + 0.2f, a2 + 0.7f, a2 + 1.3f, a2 + 3f };
                string probe = string.Join(",", at.Select(x => x.ToString("F3", CultureInfo.InvariantCulture)));
                var r = TapeGame(dp, new[] { "--track=ilyich", "--tape-debug", $"--tape-replay-start={(a2 - 1f).ToString(CultureInfo.InvariantCulture)}", $"--tape-seek-probe={probe}", "--tape-replay-quit=7" }, 300);
                var pr = r.All("seek probe #").Select(l => Regex.Match(l, @"probe #(\d+) at ([\d.]+): fallen (\d+)/(\d+) worst model difference ([\d.]+) \[(.*)\]")).Where(m => m.Success).ToList();
                var angles = pr.Select(m => Regex.Matches(m.Groups[6].Value, @"playing ([\d.]+) rad, seek ([\d.]+) rad").Select(x => (Play: float.Parse(x.Groups[1].Value, CultureInfo.InvariantCulture), Seek: float.Parse(x.Groups[2].Value, CultureInfo.InvariantCulture))).ToList()).ToList();
                float worst = pr.Count == 0 ? 99f : pr.Max(m => float.Parse(m.Groups[5].Value, CultureInfo.InvariantCulture));
                TapeCheck(r.Exit == 0 && !r.Crashed && pr.Count == 4 && pr.All(m => m.Groups[3].Value == m.Groups[4].Value && int.Parse(m.Groups[3].Value) >= atStart.Count + 1) && worst < 2e-3f,
                    $"scene 12 (trees, seek probe): at four clocks in the fall (a + 0.2, 0.7, 1.3, 3 s) a seek to the clock gives the felled trees the same model transforms as playing to it (worst difference {worst:F5}; {string.Join(" | ", pr.Select(m => $"a+{float.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) - a2:F1}: {m.Groups[6].Value}"))})");
                TapeCheck(angles.Count == 4 && angles[0].Count > 0 && angles[0].Min(x => x.Seek) < 0.2f && angles[0].Min(x => x.Seek) > 0.001f && angles[3].Any(x => x.Seek > 0.9f),
                    $"scene 12 (trees, seek probe): the seek at a + 0.2 s shows a tree that is hardly down (and not the settled one), at a + 3 s a lying one ({string.Join(" / ", angles.Select(l => string.Join(",", l.Select(x => x.Seek.ToString("F2")))))} rad)");
            }
            finally { TapeSceneRm(dp); }
        }
        finally { TapeSceneRm(a); }

        // ---- reload: lap 1 fells a tree at 15 s, the scene is reloaded, the fresh scene replays [8 s, 20 s): nothing is broken at the start
        try
        {
            var cb = TapeGame(rel, new[] { "--start", "--autodrive", "--track=ilyich", "--tape-debug", "--tape-blast=15", "--tape-reload-after-lap=1", "--tape-replay-start=8", "--tape-wrap-every=12", "--tape-replay-quit=50" }, 600);
            var (gb, tb, pb) = TapeSceneLoad(rel, "ilyich", 0);
            TapeCheck(!cb.TimedOut && cb.Exit == 0 && !cb.Crashed && gb != null && tb != null && pb && cb.Has("ghost+tape saved") && cb.Has("reloading the scene") && cb.Has("scene build #2"), $"scene 12 (trees, reload): PROOF a lap was saved in scene #1 (matching pair on disk), then the scene was reloaded ({cb.Seconds:F0} s)");
            if (tb == null) return;
            var body = WorldTapeCodec.ReadBody(tb.Payload);
            var brk = body.Events.Where(e => e.Type == TapeEventType.Effect && e.Effect == TapeEffect.BreakableBroken).ToList();
            var atStart = brk.Where(e => e.Time <= 0f).ToList(); var during = brk.Where(e => e.Time > 1f).ToList();      // (a void lap at the start line can have felled one before the lap that counts)
            float a1 = during.Count > 0 ? during.Min(e => e.Time) : -1f;
            TapeCheck(during.Count >= 1 && a1 > 14.5f && a1 < 16f && body.Keys[0].Broken.Count == atStart.Count, $"scene 12 (trees, reload): the tape has {during.Count} tree(s) breaking at {a1:F2} s and {atStart.Count} already broken in its first keyframe ({body.Keys[0].Broken.Count})");
            TapeScene5Trees(cb, atStart.Count, during.Count, a1, 8f, "scene 12 (trees, reload)", 3);
        }
        finally { TapeSceneRm(rel); }
    }

    /// <summary>The replayed world's trees: <paramref name="before"/> are down when each pass starts, <paramref name="n"/> more fall at <paramref name="a"/> and stand again at the wrap.</summary>
    void TapeScene5Trees(TapeChild c, int before, int n, float a, float t0, string label, int minPasses)
    {
        var seeks = TapeFxSeeks(c);
        var lines = c.All("tape: replay broken").Select(l => Regex.Match(l, @"replay broken (\d+) lying (\d+) at ([\d.]+)")).Where(m => m.Success)
            .Select(m => (Broken: int.Parse(m.Groups[1].Value), Lying: int.Parse(m.Groups[2].Value), At: float.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture))).ToList();
        TapeCheck(seeks.Count >= minPasses + 1 && seeks.All(s => Math.Abs(s.At - t0) < 0.02f && s.Broken == before && s.Lying == before), $"{label}: every seek (the start and each of {seeks.Count - 1} wraps) shows exactly the {before} tree(s) broken before the pass, lying, and no tree of the pass before ({string.Join(", ", seeks.Select(s => $"{s.Broken}/{s.Lying}"))})");
        var early = lines.Where(l => l.Broken > before && l.At < a - 0.05f).ToList();
        var fall = lines.Where(l => l.Broken == before + n && l.At >= a - 0.05f && l.At < a + 0.5f).ToList();
        var lies = lines.Where(l => l.Lying == before + n && l.At >= a && l.At < a + 3f).ToList();
        var wrap = lines.Where(l => l.Broken == before && l.At < a - 1f && l.At > t0 - 0.5f).ToList();
        TapeCheck(early.Count == 0, $"{label}: no tree of the pass is broken before a = {a:F2} s (early lines {early.Count})");
        TapeCheck(fall.Count >= minPasses, $"{label}: in each pass the tree breaks at a = {a:F2} s ({fall.Count} log lines show {before + n} broken between {a - 0.05f:F2} and {a + 0.5f:F2} s)");
        TapeCheck(lies.Count >= minPasses, $"{label}: and is visibly lying within 3 s of it ({lies.Count} passes)");
        TapeCheck(wrap.Count >= minPasses, $"{label}: and stands again at each wrap ({wrap.Count} returns to {before} broken)");
        var made = TapeFxMadeOf(c, "replay done");
        TapeCheck(made.GetValueOrDefault(TapeFxKind.Breakable) >= minPasses * n, $"{label}: the sink broke them as the tape said ({made.GetValueOrDefault(TapeFxKind.Breakable)} breaks over {minPasses}+ passes of {n})");
    }
}
