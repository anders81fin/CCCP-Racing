using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Tests;

// T10 of the world tape, in the real game. --tape-scene-tests, scene 14: the ASYNC pair save, deterministically. The child holds the pair-save worker at a named point
// (--tape-hold-worker=POINT, the first save only; src/Race/WorldTapePairJob.cs) and the production wait releases it the moment the main thread starts to wait for it
// ("main waits at <site>" in the gate trace): nothing here depends on how fast a worker is. Every case first proves a lap was really saved and that the gate was reached.
//   control: the first lap alone, no hold: its ghost and tape bytes (the reference of the held runs, which must be byte for byte the same)
//   a: held after the snapshot while the next lap is recorded in the reused buffers (a slower lap 2: not a record), released at lap 2's report
//   b: the scene's exit while the worker has replaced the ghost and not the tape
//   c1: a scene reload while the save is pending, then the replay of the new scene plays the saved tape; c2: the title's replay (no polling) waits for the slot
//   d: a faster lap 2 finishes while the save of lap 1 is held: serialised in order, the final files are lap 2's pair
//   e: a ghost replacement that fails and a tape replacement that fails, through the worker: the applied result is what is on disk
//   f: a worker exception is surfaced (type, phase, held state) and the next lap saves
//   g: a hold released by a timer (6.5 s): the exit's wait logs its diagnostics and still ends with the pair on disk
//   h: the runner's own timeout keeps the child's log and a state dump (the dump function, on a child of its own that is killed by its PID)
public partial class PhysicsTest
{
    static string[] TapeScene7Args(params string[] extra) => new[] { "--start", "--autodrive", "--track=ilyich", "--tape-debug" }.Concat(extra).ToArray();
    static float TapeScene7Lap(TapeChild c, int n) => float.Parse(Regex.Match(c.Lines.FirstOrDefault(l => l.StartsWith($"tape: lap {n} ")) ?? $"tape: lap {n} 0 s", $@"lap {n} (\d+\.\d+)").Groups[1].Value, CultureInfo.InvariantCulture);
    static string TapeScene7Trace(TapeChild c) => c.Lines.LastOrDefault(l => l.StartsWith("World tape: gate trace [")) ?? "";
    static bool TapeScene7Order(string trace, params string[] parts) { int at = -1; foreach (var p in parts) { int i = trace.IndexOf(p, at + 1, StringComparison.Ordinal); if (i < 0) return false; at = i; } return true; }
    static string[] TapeScene7Tmp(string dir) => Directory.Exists(dir) ? Directory.GetFiles(dir, "*.tmp") : Array.Empty<string>();

    /// <summary>The pair in <paramref name="dir"/> is the control's lap: the ghost byte for byte (the simulation is deterministic) and a tape that decodes to the same lap (same ghost fingerprint, lap time and sample count). The tape bytes themselves are not
    /// run-to-run reproducible in the real game (the actors' animation counters differ by a few entries between two identical runs, measured), so their byte-for-byte proof is the unit test, tape 23.</summary>
    bool TapeScene7SameLap(string dir, byte[] ghostBytes, WorldTape t0)
    {
        var gp = TapeGhostFile(dir, "ilyich", 0); var tp = TapeTapeFile(dir, "ilyich", 0);
        if (!File.Exists(gp) || !File.Exists(tp) || !File.ReadAllBytes(gp).SequenceEqual(ghostBytes)) return false;
        var t = WorldTapeCodec.Decode(File.ReadAllBytes(tp));
        return t != null && t.GhostFingerprint == t0.GhostFingerprint && MathF.Abs(t.LapTime - t0.LapTime) < 1e-4f && t.SampleCount == t0.SampleCount;
    }

    void TapeScene7()
    {
        TapeScene7Hold();
        TapeScene7Order_();
        TapeScene7Failures();
        TapeScene7Diagnostics();
    }

    /// <summary>A run that must have ended cleanly, whose worker was held and let go by the main thread's wait at <paramref name="site"/>.</summary>
    void TapeScene7Held(TapeChild c, string label, string hold, string site)
    {
        string trace = TapeScene7Trace(c);
        TapeCheck(!c.TimedOut && c.Exit == 0 && !c.Crashed, $"scene 14 {label}: the child ended cleanly (exit {c.Exit}, {c.Seconds:F0} s)");
        TapeCheck(c.Has("test gate on the worker of this save") && trace.Contains("main waits at " + site) && TapeScene7Order(trace, "held at " + hold, "released: main waits at " + site, "passed " + hold), $"scene 14 {label}: the worker was held at '{hold}' and the main thread's wait at '{site}' let it go ({trace.Trim()})");
    }

    void TapeScene7Hold()
    {
        string ctl = TapeSceneDir("a0"), a = TapeSceneDir("a"), b = TapeSceneDir("b"), c1 = TapeSceneDir("c1"), c2 = TapeSceneDir("c2");
        try
        {
            // ---- control: lap 1 alone, no hold
            var k = TapeGame(ctl, TapeScene7Args("--tape-lap-speeds=1,0.7", "--tape-quit-laps=1"), 400);
            var (g0, t0, ok0) = TapeSceneLoad(ctl, "ilyich", 0);
            TapeCheck(!k.TimedOut && k.Exit == 0 && g0 != null && t0 != null && ok0 && k.All("ghost+tape saved").Count() == 1, $"scene 14 control: PROOF a lap was saved: one pair save reported, ghost and tape on disk match (lap {TapeScene7Lap(k, 1):F2} s, {k.Seconds:F0} s)");
            if (g0 == null || t0 == null) return;
            byte[] gb = File.ReadAllBytes(TapeGhostFile(ctl, "ilyich", 0)), tb = File.ReadAllBytes(TapeTapeFile(ctl, "ilyich", 0));

            // ---- a: held after the snapshot; lap 2 (slower: not a record) is recorded in the reused buffers meanwhile
            var ca = TapeGame(a, TapeScene7Args("--tape-lap-speeds=1,0.7", "--tape-quit-laps=2", "--tape-hold-worker=start"), 700);
            TapeScene7Held(ca, "a", "start", "the lap report");
            var l1 = ca.Lines.FirstOrDefault(l => l.StartsWith("tape: lap 1 ")); var l2 = ca.Lines.FirstOrDefault(l => l.StartsWith("tape: lap 2 "));
            TapeCheck(l1 != null && l1.Contains("record True") && l2 != null && l2.Contains("record False") && l2.Contains("recording True") && TapeNum(l2, "samples") > 300, $"scene 14 a: lap 1 is the record, lap 2 is slower and WAS recorded in the reused buffers while the worker was held ({l2?.Trim()})");
            TapeCheck(ca.All("ghost+tape saved").Count() == 1 && ca.Has("the main thread waited") && TapeScene7Order(TapeScene7Trace(ca), "main waits at the lap report"), "scene 14 a: one pair save, applied at lap 2's report, which was the first time the main thread waited (the earlier laps never blocked on it)");
            TapeCheck(TapeScene7SameLap(a, gb, t0), "scene 14 a: the ghost saved by the held worker is lap 1's byte for byte and the tape decodes to lap 1 (fingerprint, lap time, samples), as in the run that was not held (the recorder was reused for lap 2 before the worker ran)");
            TapeCheck(TapeScene7Tmp(a).Length == 0, "scene 14 a: no tmp file is left");
            TapeSceneRm(a);

            // ---- b: the exit while the ghost is replaced and the tape not
            var cb = TapeGame(b, TapeScene7Args("--tape-lap-speeds=1,0.7", "--tape-quit-laps=1", "--tape-hold-worker=ghost-replaced"), 400);
            TapeScene7Held(cb, "b", "ghost-replaced", "the scene's exit");
            TapeCheck(TapeScene7SameLap(b, gb, t0) && TapeScene7Tmp(b).Length == 0, "scene 14 b: the exit waited: after it the pair is whole on disk, byte for byte lap 1's, and no tmp or partial file is left");
            TapeSceneRm(b);

            // ---- c1: a scene reload while the save is pending, then the replay of the new scene
            var cc = TapeGame(c1, TapeScene7Args("--tape-lap-speeds=1,0.7", "--tape-reload-after-lap=1", "--tape-replay-quit=12", "--tape-hold-worker=tmp-written"), 500);
            TapeScene7Held(cc, "c1 (reload)", "tmp-written", "the scene's exit");
            TapeCheck(cc.Has("scene build #2") && TapeKeyList(cc.Find("replay of set 0 from tape: covers ["), "covers").Contains("Tractor:DrunkTractor"), $"scene 14 c1: the reloaded scene waited for the slot and replays the new tape ({cc.Find("replay of set 0")?.Split("live [")[0].Trim()})");
            TapeCheck(TapeScene7SameLap(c1, gb, t0) && TapeScene7Tmp(c1).Length == 0, "scene 14 c1: the files are lap 1's pair, byte for byte, no tmp");
            TapeSceneRm(c1);

            // ---- c2: the title's replay does not poll, it waits
            var cd = TapeGame(c2, TapeScene7Args("--tape-lap-speeds=1,0.7", "--tape-title-after-lap=1", "--tape-title-nopoll", "--tape-replay-quit=12", "--tape-hold-worker=tmp-written"), 500);
            TapeScene7Held(cd, "c2 (title)", "tmp-written", "the title's replay");
            TapeCheck(cd.Has("title entered in the same scene #1 after lap 1: replaying True") && cd.Has("saved tape in memory True") && !cd.Has("scene build #2") && TapeKeyList(cd.Find("from tape: covers ["), "covers").Contains("Tractor:DrunkTractor"), $"scene 14 c2: the title entered in the same scene, waited for the pending save and replays the NEW tape ({cd.Find("title entered")?.Trim()})");
            TapeCheck(TapeScene7SameLap(c2, gb, t0) && TapeScene7Tmp(c2).Length == 0, "scene 14 c2: the tape on disk is lap 1's, no tmp");
        }
        finally { foreach (var d in new[] { ctl, a, b, c1, c2 }) TapeSceneRm(d); }
    }

    // ---- d: a lap 2 that is a record finishes while the save of lap 1 is held ----
    void TapeScene7Order_()
    {
        string d = TapeSceneDir("d");
        try
        {
            var c = TapeGame(d, TapeScene7Args("--ap-speed=0.7", "--tape-lap-speeds=0.7,1", "--tape-quit-laps=2", "--tape-hold-worker=ghost-replaced"), 700);
            string trace = TapeScene7Trace(c);
            TapeCheck(!c.TimedOut && c.Exit == 0 && !c.Crashed && c.Has("test gate on the worker of this save") && TapeScene7Order(trace, "held at ghost-replaced", "main waits at", "passed ghost-replaced"), $"scene 14 d: the first save was held with the ghost replaced; the main thread's wait let it go ({trace.Trim()})");
            var l1 = c.Lines.FirstOrDefault(l => l.StartsWith("tape: lap 1 ")); var l2 = c.Lines.FirstOrDefault(l => l.StartsWith("tape: lap 2 "));
            float t1 = TapeScene7Lap(c, 1), t2 = TapeScene7Lap(c, 2);
            TapeCheck(l1 != null && l1.Contains("record True") && l2 != null && l2.Contains("record True") && t2 < t1, $"scene 14 d: lap 2 ({t2:F2} s) is faster than lap 1 ({t1:F2} s): both laps are records");
            var saves = c.Lines.Where(l => l.StartsWith("World tape: set 0 lap ")).ToList();
            TapeCheck(saves.Count == 2 && saves[0].Contains($"lap {t1:F2} s") && saves[1].Contains($"lap {t2:F2} s") && saves.All(s => s.Contains("ghost+tape saved")), $"scene 14 d: both pair saves were applied, in order, lap 1 then lap 2 ({string.Join(" | ", saves.Select(s => s.Split(',')[0]))})");
            var (g, t, paired) = TapeSceneLoad(d, "ilyich", 0);
            TapeCheck(g != null && t != null && paired && Math.Abs(g.LapTime - t2) < 0.02f && Math.Abs(t.LapTime - t2) < 0.02f && TapeScene7Tmp(d).Length == 0, $"scene 14 d: the final files are the second pair, never an interleaving: the ghost ({g?.LapTime:F2} s) and the tape ({t?.LapTime:F2} s) are lap 2 and match by fingerprint, no tmp");
        }
        finally { TapeSceneRm(d); }
    }

    // ---- e: failures through the worker; f: a worker exception ----
    void TapeScene7Failures()
    {
        string e = TapeSceneDir("e"), f = TapeSceneDir("f");
        var gp = TapeGhostFile(e, "ilyich", 0); var tp = TapeTapeFile(e, "ilyich", 0); string sig = Path.Combine(e, "track_ilyich.sig");
        try
        {
            var c1 = TapeGame(e, TapeScene7Args("--ap-speed=0.7", "--tape-quit-laps=1"), 400);
            var (g1, t1, ok1) = TapeSceneLoad(e, "ilyich", 0);
            TapeCheck(g1 != null && t1 != null && ok1 && File.Exists(sig), $"scene 14 e: setup, a slow first record ({g1?.LapTime:F2} s) with its pair and signature");
            if (g1 == null || t1 == null) return;
            byte[] gb = File.ReadAllBytes(gp), tb = File.ReadAllBytes(tp), sb = File.ReadAllBytes(sig);
            // the ghost replacement fails, the worker held at 'verified'
            var c2 = TapeGame(e, TapeScene7Args("--tape-quit-laps=1", "--tape-fail=ghost", "--tape-hold-worker=verified"), 400);
            TapeScene7Held(c2, "e1 (ghost fails)", "verified", "the scene's exit");
            TapeCheck(c2.Has("record True") && c2.Has("the pair save was refused") && !c2.Has("ghost+tape saved") && c2.Has("save refused"), "scene 14 e1: the refused save came back from the worker: applied as refused, with the worker's warning");
            TapeCheck(File.ReadAllBytes(gp).SequenceEqual(gb) && File.ReadAllBytes(tp).SequenceEqual(tb) && TapeScene7Tmp(e).Length == 0 && File.ReadAllBytes(sig).SequenceEqual(sb), "scene 14 e1: the old ghost and tape are byte for byte as they were, the signature untouched, no tmp");
            // the tape replacement fails after the ghost was replaced: the new ghost is the record
            var c3 = TapeGame(e, TapeScene7Args("--tape-quit-laps=1", "--tape-fail=tape", "--tape-hold-worker=tmp-written"), 400);
            TapeScene7Held(c3, "e2 (tape fails)", "tmp-written", "the scene's exit");
            var (g3, t3, ok3) = TapeSceneLoad(e, "ilyich", 0);
            TapeCheck(c3.Has("the new ghost is the record, the tape could not be saved") && c3.Has("ghost saved, tape not") && !c3.Has("the pair save was refused"), "scene 14 e2: the result applied on the main thread is 'ghost saved, tape not' (GhostSaved without TapeSaved)");
            TapeCheck(g3 != null && g3.LapTime < g1.LapTime - 1f && File.ReadAllBytes(tp).SequenceEqual(tb) && !ok3 && TapeScene7Tmp(e).Length == 0, $"scene 14 e2: disk agrees: the new faster ghost ({g3?.LapTime:F2} s < {g1.LapTime:F2} s) is there, the old tape stays and no longer matches, no tmp");
            TapeCheck(File.ReadAllBytes(sig).Length > 0, "scene 14 e2: the signature is there and non-empty after the ghost-only save, as in the synchronous contract");

            // ---- f: the worker throws after it is released; lap 2 saves normally
            var cf = TapeGame(f, TapeScene7Args("--ap-speed=0.7", "--tape-lap-speeds=0.7,1", "--tape-quit-laps=2", "--tape-hold-worker=start", "--tape-worker-throw=start"), 700);
            string trace = TapeScene7Trace(cf);
            TapeCheck(!cf.TimedOut && cf.Exit == 0 && !cf.Crashed && trace.Contains("main waits at") && TapeScene7Order(trace, "held at start", "passed start", "fault at start"), $"scene 14 f: the child ended cleanly although the worker threw ({cf.Seconds:F0} s; {trace.Trim()})");
            var w = cf.Lines.FirstOrDefault(l => l.Contains("save failed on the worker"));
            TapeCheck(w != null && w.Contains("InvalidOperationException") && w.Contains("phase 'start'") && w.Contains("held at start") && w.Contains("released at start"), $"scene 14 f: the exception is surfaced with its type, the phase and the held state ({w?.Trim()})");
            var saves = cf.Lines.Where(l => l.StartsWith("World tape: set 0 lap ")).ToList();
            TapeCheck(cf.Has("the pair save was refused") && saves.Count == 2 && saves[0].Contains(": refused,") && saves[1].Contains("ghost+tape saved"), $"scene 14 f: lap 1's result was applied as refused and the slot was free: lap 2 saved its pair ({string.Join(" | ", saves.Select(s => s.Split(',')[0] + "," + s.Split(',')[1]))})");
            var (gf, tf, okf) = TapeSceneLoad(f, "ilyich", 0);
            TapeCheck(gf != null && tf != null && okf && Math.Abs(gf.LapTime - TapeScene7Lap(cf, 2)) < 0.02f && TapeScene7Tmp(f).Length == 0, $"scene 14 f: the pair on disk is lap 2's ({gf?.LapTime:F2} s), no tmp");
        }
        finally { TapeSceneRm(e); TapeSceneRm(f); }
    }

    // ---- g: a long wait logs its diagnostics; h: the runner keeps the state of a timed-out child ----
    void TapeScene7Diagnostics()
    {
        string g = TapeSceneDir("g"), h = TapeSceneDir("h");
        try
        {
            var c = TapeGame(g, TapeScene7Args("--tape-lap-speeds=1,0.7", "--tape-quit-laps=1", "--tape-hold-worker=compressed", "--tape-hold-release-ms=6500"), 400);
            var diag = c.Lines.Where(l => l.Contains("WAIT DIAGNOSTIC")).ToList();
            TapeCheck(!c.TimedOut && c.Exit == 0 && !c.Crashed, $"scene 14 g: the child ended cleanly ({c.Seconds:F0} s)");
            TapeCheck(diag.Count == 1 && diag[0].Contains("#1") && diag[0].Contains("at the scene's exit") && diag[0].Contains("task Running") && diag[0].Contains("worker phase 'compressed'") && diag[0].Contains("held at compressed") && Regex.IsMatch(diag[0], @"waited 5\d\d\d ms"), $"scene 14 g: a wait of about 6.5 s logged exactly one diagnostic, after 5 s, naming the site, the task status, the worker phase and the held gate ({diag.FirstOrDefault()?.Trim()})");
            int at = Array.FindIndex(c.Lines, l => l.Contains("WAIT DIAGNOSTIC"));
            var stack = at >= 0 ? c.Lines.Skip(at + 1).Take(40).ToList() : new List<string>();
            TapeCheck(stack.Any(l => l.Contains("TapePairSaves.Join")) && stack.Any(l => l.Contains("RaceScene.TapeExit")), "scene 14 g: and the diagnostic carries the main thread's managed stack (TapePairSaves.Join under RaceScene.TapeExit)");
            var (gh, th, ok) = TapeSceneLoad(g, "ilyich", 0);
            TapeCheck(gh != null && th != null && ok && c.Has("the main thread waited") && TapeScene7Trace(c).Contains("main waits at the scene's exit") && TapeScene7Order(TapeScene7Trace(c), "held at compressed", "passed compressed"), "scene 14 g: the wait did not give up: when the timer let the worker go the pair was saved whole");

            // ---- h: the runner's timeout path keeps the child's full log and a state dump (the function the timeout calls, on a child that is then killed by its PID)
            Directory.CreateDirectory(h);
            var psi = new ProcessStartInfo("/bin/sleep", "60") { UseShellExecute = false };
            using var p = Process.Start(psi);
            string fakeLog = h + ".log"; File.WriteAllText(fakeLog, "line one\nWAIT DIAGNOSTIC #1 stand-in\n"); File.WriteAllText(Path.Combine(h, "ghost_x.bin"), "abc");
            TapeKeepTimeout(h, fakeLog, p.Id, new[] { "--demo" }, 7, 7.5);
            bool aliveBefore = !p.HasExited;
            try { p.Kill(true); p.WaitForExit(); } catch { }
            string keep = Path.Combine(Path.GetTempPath(), "cccp-tape-timeouts", Path.GetFileName(h));
            string st = File.Exists(Path.Combine(keep, "state.txt")) ? File.ReadAllText(Path.Combine(keep, "state.txt")) : "";
            TapeCheck(aliveBefore && st.Contains($"child pid {p.Id} timed out") && st.Contains("--- threads") && st.Contains("sleep") && st.Contains("ghost_x.bin 3") && File.Exists(Path.Combine(keep, "child.log")) && File.ReadAllText(Path.Combine(keep, "child.log")).Contains("WAIT DIAGNOSTIC #1 stand-in"), "scene 14 h: a timed-out child's full log and a state dump (its threads and what they wait in, the data dir's files) are kept in /tmp/cccp-tape-timeouts, and only that child, by its PID, was touched");
            try { Directory.Delete(keep, true); } catch { }
        }
        finally { TapeSceneRm(g); TapeSceneRm(h); }
    }
}
