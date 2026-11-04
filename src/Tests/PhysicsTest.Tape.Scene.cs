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

// --tape-scene-tests: the world tape in the REAL game. Each case starts ONE headless child Godot at a time (the game itself, autodriven,
// with its own temporary --data-dir and its own XDG_DATA_HOME, so the user's saves, ghosts and boards are never touched) and judges its
// log lines (RaceScene.Tape.cs prints them under --tape-debug) and the files it leaves. Linux only (the child's output goes to a file
// through /bin/sh, so no reader thread is needed).
public partial class PhysicsTest
{
    sealed record TapeChild(int Exit, bool TimedOut, string[] Lines, double Seconds)
    {
        public string Find(string text) => Lines.FirstOrDefault(l => l.Contains(text));
        public bool Has(string text) => Find(text) != null;
        public IEnumerable<string> All(string text) => Lines.Where(l => l.Contains(text));
        public bool Crashed => Lines.Any(l => l.Contains("Unhandled exception") || l.Contains("System.NullReference") || l.Contains("System.InvalidOperation") || l.Contains("System.IndexOutOfRange") || l.Contains("System.ArgumentOutOfRange") || l.Contains("System.InvalidCast"));
    }

    static string TapeProjectDir => ProjectSettings.GlobalizePath("res://");

    /// <summary>One game child to its end (or killed at the timeout). <paramref name="onTick"/> runs on this thread every 100 ms while it lives.</summary>
    TapeChild TapeGame(string dir, string[] args, int timeoutSec, bool tapeOff = false, Dictionary<string, string> env = null, Action onTick = null, string seed = "1")
    {
        string log = dir + ".log";
        var psi = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
        foreach (var s in new[] { "-c", "exec \"$0\" \"$@\" > \"$TAPE_OUT\" 2>&1", OS.GetExecutablePath(), "--headless", "--fixed-fps", "60", "--path", TapeProjectDir, "--" }) psi.ArgumentList.Add(s);
        foreach (var a in args.Concat(new[] { "--no-intro", "--babushka-seed=" + seed, "--rng-seed=" + seed, "--data-dir=" + dir })) psi.ArgumentList.Add(a);
        psi.Environment["TAPE_OUT"] = log;
        psi.Environment["XDG_DATA_HOME"] = dir + ".xdg";
        if (tapeOff) psi.Environment["CCCP_TAPE"] = "off"; else psi.Environment.Remove("CCCP_TAPE");
        if (env != null) foreach (var (k, v) in env) psi.Environment[k] = v;
        Directory.CreateDirectory(dir);
        var sw = Stopwatch.StartNew();
        using var p = Process.Start(psi);
        bool timedOut = false;
        while (!p.WaitForExit(100))
        {
            onTick?.Invoke();
            if (sw.Elapsed.TotalSeconds > timeoutSec) { timedOut = true; TapeKeepTimeout(dir, log, p.Id, args, timeoutSec, sw.Elapsed.TotalSeconds); try { p.Kill(true); } catch { } p.WaitForExit(); break; }      // (only this runner's own child, by its PID)
        }
        var lines = File.Exists(log) ? File.ReadAllLines(log) : Array.Empty<string>();
        return new TapeChild(p.ExitCode, timedOut, lines, sw.Elapsed.TotalSeconds);
    }

    /// <summary>
    /// The child's own timeout fired (the timeout is not loosened and nothing is abandoned: the child is killed by PID after this): keep what explains it. The full log (it holds the
    /// "WAIT DIAGNOSTIC" lines with the worker's phase, the gate and the main thread's stack) and a state dump (the child's threads and what they wait in, the data dir's files with sizes
    /// and times) go to /tmp/cccp-tape-timeouts/ and survive the cleanup of the case.
    /// </summary>
    static void TapeKeepTimeout(string dir, string log, int pid, string[] args, int timeoutSec, double seconds)
    {
        try
        {
            string keep = Path.Combine(Path.GetTempPath(), "cccp-tape-timeouts", Path.GetFileName(dir));
            Directory.CreateDirectory(keep);
            var d = new System.Text.StringBuilder();
            d.AppendLine($"child pid {pid} timed out after {seconds:F1} s (limit {timeoutSec} s) at {DateTime.Now:O}; args: {string.Join(' ', args)}");
            string proc = $"/proc/{pid}";
            foreach (var f in new[] { "status", "wchan", "stat" }) try { d.AppendLine($"--- {proc}/{f}"); d.AppendLine(File.ReadAllText($"{proc}/{f}")); } catch (Exception ex) { d.AppendLine("(" + ex.Message + ")"); }
            try
            {
                d.AppendLine("--- threads (tid, comm, state, wchan)");
                foreach (var t in Directory.GetDirectories($"{proc}/task").OrderBy(x => x))
                {
                    string Rd(string n) { try { return File.ReadAllText(Path.Combine(t, n)).Trim(); } catch { return "?"; } }
                    var st = Rd("stat"); int i = st.LastIndexOf(')');
                    d.AppendLine($"{Path.GetFileName(t)} {Rd("comm")} state {(i > 0 && i + 2 < st.Length ? st[i + 2] : '?')} wchan {Rd("wchan")}");
                }
            }
            catch (Exception ex) { d.AppendLine("(" + ex.Message + ")"); }
            try
            {
                d.AppendLine("--- data dir files (name, bytes, modified)");
                foreach (var f in Directory.GetFiles(dir).OrderBy(x => x)) { var fi = new FileInfo(f); d.AppendLine($"{fi.Name} {fi.Length} {fi.LastWriteTime:HH:mm:ss.fff}"); }
            }
            catch (Exception ex) { d.AppendLine("(" + ex.Message + ")"); }
            File.WriteAllText(Path.Combine(keep, "state.txt"), d.ToString());
            if (File.Exists(log)) File.Copy(log, Path.Combine(keep, "child.log"), true);
            GD.Print($"TIMEOUT of a scene child: its full log and a state dump are kept in {keep}");
        }
        catch (Exception ex) { GD.Print("could not keep the timeout state: " + ex.Message); }
    }

    static string TapeSceneDir(string name)
    {
        var d = Path.Combine(Path.GetTempPath(), $"cccp-tape-tests-scene-{name}-{Guid.NewGuid():N}");
        return d;
    }

    static void TapeSceneRm(string dir)
    {
        foreach (var d in new[] { dir, dir + ".xdg" }) try { if (d.Contains("cccp-tape-tests-scene-") && Directory.Exists(d)) Directory.Delete(d, true); } catch { }
        try { File.Delete(dir + ".log"); } catch { }
    }

    static void TapeCopyDir(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)));
    }

    static readonly string[] TapeSetNames = { "", "_stakhanov", "_diesel", "_rocket" };
    static string TapeGhostFile(string dir, string track, int set) => Path.Combine(dir, $"ghost_{track}{TapeSetNames[set]}.bin");
    static string TapeTapeFile(string dir, string track, int set) => Path.Combine(dir, $"tape_{track}{TapeSetNames[set]}.bin");

    /// <summary>The ghost and tape of a set read back from disk: (ghost, tape, whether the tape belongs to the ghost).</summary>
    (Ghost G, WorldTape T, bool Paired) TapeSceneLoad(string dir, string track, int set)
    {
        var gp = TapeGhostFile(dir, track, set); var tp = TapeTapeFile(dir, track, set);
        var g = File.Exists(gp) ? Ghost.Load(gp, TapeParams, null) : null;
        var t = File.Exists(tp) ? WorldTapeCodec.Decode(File.ReadAllBytes(tp)) : null;
        return (g, t, g != null && t != null && t.GhostFingerprint == WorldTapeFingerprint.Of(g));
    }

    static float TapeNum(string line, string key) { var m = Regex.Match(line ?? "", Regex.Escape(key) + @"\s*=?\s*(-?\d+(\.\d+)?)"); return m.Success ? float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : float.NaN; }
    static int TapeCountOf(string line, string group, string key)
    {
        var g = Regex.Match(line ?? "", Regex.Escape(group) + @" \[([^\]]*)\]");
        if (!g.Success) return 0;
        var m = Regex.Match(g.Groups[1].Value, @"(^|,)" + Regex.Escape(key) + @"=(\d+)");
        return m.Success ? int.Parse(m.Groups[2].Value) : 0;
    }
    static string TapeSummaryLine(TapeChild c, string why) => c.Lines.LastOrDefault(l => l.Contains($"summary ({why})"));

    void TapeSceneTests()
    {
        GD.Print("World tape scene tests (real game, one headless child at a time)");
        if (!OperatingSystem.IsLinux()) { Skip("tape scene: the child runs go through /bin/sh (Linux only)"); return; }
        string a = TapeSceneDir("a");
        string only = OS.GetCmdlineUserArgs().FirstOrDefault(x => x.StartsWith("--tape-scene-only="))?["--tape-scene-only=".Length..];      // e.g. "scene 10": run just that case (it needs the data of 1+7 for some)
        void Case(string name, Action act) { if (only == null || name == only) TapeRun(name, act); }
        try
        {
            Case("scene 1+7", () => TapeSceneRecordAndReload(a));
            Case("scene 1b", TapeSceneTraceEquality);
            Case("scene 2", TapeSceneSets);
            Case("scene forced failures", TapeSceneForcedFailures);
            Case("scene 3", () => TapeSceneLegacyAndCorrupt(a));
            Case("scene 4", () => TapeSceneLoop(a));
            Case("scene 5", () => TapeSceneSuppression(a));
            Case("scene 6", () => TapeSceneTrip(a));
            Case("scene 8", TapeSceneBonus);
            Case("scene 8b", TapeSceneBonusSurvivable);     // the same Bonus world with the engine health off (--training): see TapeSceneBonusSurvivable
            Case("scene session", TapeSceneTrailerSession);
            Case("scene 10", TapeScene3);      // PhysicsTest.Tape.Scene3.cs: a lap saved in this process replays in the same scene (no reload)
            Case("scene 9", TapeScene2);      // PhysicsTest.Tape.Scene2.cs: one lap per track family, the actors covered, costs, the Bonus trace
            Case("scene 11", TapeScene4);     // PhysicsTest.Tape.Scene4.cs: phase 2, the Hinds and missiles as dynamic instances (needs no data of the others)
            Case("scene 12", TapeScene5);     // PhysicsTest.Tape.Scene5.cs: phase 3, effects, projectiles and breakables (needs no data of the others)
            Case("scene 13", TapeScene6);     // PhysicsTest.Tape.Scene6.cs: T9, the jets and guided missiles, the battery body, the emitters, the collision policy, the inventory
            Case("scene 14", TapeScene7);     // PhysicsTest.Tape.Scene7.cs: T10, the async pair save held and faulted deterministically (the worker gate), the wait diagnostics
        }
        finally { TapeSceneRm(a); }
        GD.Print(_failures == 0 ? "World tape scene tests: all passed" : $"World tape scene tests: {_failures} FAILED");
    }

    // ---- 1 (record-only saving) and 7 (a second lap in the scene, a scene reload, a scaled part) in one run: two laps, then an in-process reload and the title replay ----
    void TapeSceneRecordAndReload(string dir)
    {
        byte[] gSnap = null, tSnap = null;
        var gp = TapeGhostFile(dir, "ilyich", 0); var tp = TapeTapeFile(dir, "ilyich", 0);
        var c = TapeGame(dir, new[] { "--start", "--autodrive", "--track=ilyich", "--tape-debug", "--tape-test-part", "--tape-lap-speeds=1,0.7", "--tape-reload-after-lap=2", "--tape-replay-quit=12" }, 400, onTick: () =>
        {
            if (gSnap == null && File.Exists(gp) && File.Exists(tp)) { try { gSnap = File.ReadAllBytes(gp); tSnap = File.ReadAllBytes(tp); } catch { gSnap = tSnap = null; } }
        });
        TapeCheck(!c.TimedOut && c.Exit == 0 && !c.Crashed, $"scene 1: the two-lap run ended cleanly (exit {c.Exit}, {c.Seconds:F0} s)");
        var l1 = c.Lines.FirstOrDefault(l => l.StartsWith("tape: lap 1 ")); var l2 = c.Lines.FirstOrDefault(l => l.StartsWith("tape: lap 2 "));
        TapeCheck(l1 != null && l1.Contains("record True") && l1.Contains("recording True") && TapeNum(l1, "samples") > 300, $"scene 1: lap 1 is a record and was recorded ({l1})");
        TapeCheck(l2 != null && l2.Contains("record False") && l2.Contains("recording True") && TapeNum(l2, "samples") > 300, $"scene 1: lap 2 is NOT a record and was recorded all the same ({l2})");
        float lap1 = float.Parse(Regex.Match(l1 ?? "tape: lap 1 0 s", @"lap 1 (\d+\.\d+)").Groups[1].Value, CultureInfo.InvariantCulture), lap2 = float.Parse(Regex.Match(l2 ?? "tape: lap 2 0 s", @"lap 2 (\d+\.\d+)").Groups[1].Value, CultureInfo.InvariantCulture);
        TapeCheck(lap2 > lap1, $"scene 1: the second lap ({lap2:F2} s) is slower than the first ({lap1:F2} s)");
        TapeCheck(c.All("World tape: set 0 lap").Count() == 1 && c.Has("ghost+tape saved"), "scene 1: exactly one pair was saved (the record), not two");
        TapeCheck(gSnap != null && tSnap != null && File.ReadAllBytes(gp).SequenceEqual(gSnap) && File.ReadAllBytes(tp).SequenceEqual(tSnap), "scene 1: after the slower lap 2 the ghost and the tape are unchanged byte for byte");
        var (g, t, paired) = TapeSceneLoad(dir, "ilyich", 0);
        TapeCheck(g != null && t != null && paired && t.Set == 0 && t.TrackId == "ilyich" && t.SampleCount > 300 && Math.Abs(t.LapTime - g.LapTime) < 1e-3f, $"scene 1: the pair on disk matches by fingerprint (ghost {g?.LapTime:F2} s, tape set {t?.Set} track {t?.TrackId} samples {t?.SampleCount})");
        TapeCheck(File.Exists(Path.Combine(dir, "track_ilyich.sig")), "scene 1: the track signature was written with the pair");
        // 7: the second lap in the same scene and a reload
        TapeCheck(TapeKeyList(c.Find("against the actors of scene #1 before lap 2 ends"), "the saved tape covers").Contains("Tractor:DrunkTractor"), "scene 7: the lap-1 tape still covers the tractor (among the other marked actors) in the same scene at lap 2");
        TapeCheck(c.Has("scene build #2") && TapeKeyList(c.Find("replay of set 0 from tape: covers ["), "covers").Contains("Tractor:DrunkTractor"), "scene 7: after an in-process scene reload (fresh actors, fresh index) the tape recorded in the first scene covers the tractor again");
        var tp1 = c.Lines.FirstOrDefault(l => l.Contains("testpart basis error"));
        TapeCheck(tp1 != null && TapeNum(tp1, "error") < 0.01f && TapeNum(tp1, "over") > 200 && tp1.Contains("scale (1.60, 1.00, 0.50)"), $"scene 7: the non-uniformly scaled part replays as R*S within 0.01 of its recorded basis, scale kept ({tp1})");
        // 8: the cost at the finish line
        var sv = c.Find("World tape: set 0 lap");
        if (sv != null) TapeInfo($"scene 8 (Ilyich): {sv.Trim()}");
        TapeCheck(sv != null, "scene 8: the finish-line cost was printed");
        var un = c.Find("tape: lap 1 ");
        TapeInfo($"scene 1: recorder on Ilyich: {un}");
    }

    // ---- 1b: the simulation is the same with recording on and off (one rally lap boundary inside the trace) ----
    void TapeSceneTraceEquality()
    {
        var tr = new string[2]; var dirs = new[] { TapeSceneDir("on"), TapeSceneDir("off") };
        try
        {
            for (int i = 0; i < 2; i++)
            {
                tr[i] = dirs[i] + ".trace.txt";
                var c = TapeGame(dirs[i], new[] { "--start", "--autodrive", "--track=ilyich" }, 300, tapeOff: i == 1, env: new() { ["WORLDTRACE"] = tr[i], ["WORLDTRACE_FRAMES"] = "24000" });
                TapeCheck(!c.TimedOut && c.Exit == 0 && File.Exists(tr[i]) && File.ReadLines(tr[i]).Last() == "END", $"scene 1b: the {(i == 0 ? "recording" : "CCCP_TAPE=off")} trace run completed ({c.Seconds:F0} s)");
                bool pair = File.Exists(TapeTapeFile(dirs[i], "ilyich", 0));
                TapeCheck(File.Exists(TapeGhostFile(dirs[i], "ilyich", 0)) && pair == (i == 0), $"scene 1b: the lap boundary saved a ghost {(i == 0 ? "and a tape" : "and no tape")} inside the trace");
            }
            if (File.Exists(tr[0]) && File.Exists(tr[1]))
            {
                bool same = File.ReadAllBytes(tr[0]).SequenceEqual(File.ReadAllBytes(tr[1]));
                TapeCheck(same, $"scene 1b: the car and world trace ({File.ReadLines(tr[0]).Count() - 1} steps) is identical with recording on and off");
            }
        }
        finally { foreach (var d in dirs) TapeSceneRm(d); foreach (var f in tr) try { if (f != null) File.Delete(f); } catch { } }
    }

    // ---- 2: every record set's pair under the right names, and the title replay of each plays from its tape ----
    void TapeSceneSets()
    {
        var modes = new[] { new string[0], new[] { "--training" }, new[] { "--grade=diesel" }, new[] { "--grade=rocket" } };
        for (int set = 0; set < 4; set++)
        {
            string dir = TapeSceneDir("set" + set);
            try
            {
                var c = TapeGame(dir, new[] { "--start", "--autodrive", "--track=ilyich", "--tape-debug", "--tape-quit-laps=1" }.Concat(modes[set]).ToArray(), 300);
                var (g, t, paired) = TapeSceneLoad(dir, "ilyich", set);
                TapeCheck(!c.TimedOut && c.Exit == 0 && !c.Crashed && g != null && t != null && paired && t.Set == set, $"scene 2: set {set} ({new[] { "petrol rally", "Stakhanov", "diesel rally", "rocket rally" }[set]}) wrote ghost_ilyich{TapeSetNames[set]}.bin and tape_ilyich{TapeSetNames[set]}.bin, a matching pair (tape set {t?.Set}, {c.Seconds:F0} s)");
                var others = Enumerable.Range(0, 4).Where(s => s != set).Count(s => File.Exists(TapeGhostFile(dir, "ilyich", s)) || File.Exists(TapeTapeFile(dir, "ilyich", s)));
                TapeCheck(others == 0, $"scene 2: set {set} touched no other set's files");
                var sv = c.Find($"World tape: set {set} lap");
                TapeCheck(sv != null && sv.Contains("ghost+tape saved"), $"scene 2: set {set} reported {sv?.Trim()}");
                var r = TapeGame(dir, new[] { "--track=ilyich", "--tape-debug", "--tape-replay-quit=5" }, 120);
                TapeCheck(r.Exit == 0 && !r.Crashed && TapeKeyList(r.Find($"replay of set {set} from tape: covers ["), "covers").Contains("Tractor:DrunkTractor"), $"scene 2: the title replay of set {set} plays from its tape");
                var roots = r.Find("covered roots visible");
                TapeInfo($"scene 2: set {set} title replay: {r.Find("replay of set")?.Split("live [")[0].Trim()} {roots?.Substring(roots.IndexOf("covered roots"))}");
            }
            finally { TapeSceneRm(dir); }
        }
    }

    // ---- the pair save's partial results in the real game: a failing tape replacement, a refused ghost ----
    void TapeSceneForcedFailures()
    {
        string dir = TapeSceneDir("fail");
        var gp = TapeGhostFile(dir, "ilyich", 0); var tp = TapeTapeFile(dir, "ilyich", 0);
        try
        {
            var c1 = TapeGame(dir, new[] { "--start", "--autodrive", "--track=ilyich", "--tape-debug", "--tape-quit-laps=1", "--ap-speed=0.8" }, 300);
            var (g1, t1, ok1) = TapeSceneLoad(dir, "ilyich", 0);
            TapeCheck(g1 != null && t1 != null && ok1, $"scene fail: setup, a slow first record ({g1?.LapTime:F2} s) with its pair");
            var gb = File.ReadAllBytes(gp); var tb = File.ReadAllBytes(tp);
            // The ghost replacement fails: GhostSaved is false, nothing changed.
            var c2 = TapeGame(dir, new[] { "--start", "--autodrive", "--track=ilyich", "--tape-debug", "--tape-quit-laps=1", "--tape-fail=ghost" }, 300);
            TapeCheck(c2.Has("record True") && c2.Has("the pair save was refused") && !c2.Has("ghost+tape saved"), "scene fail: a faster lap whose ghost replacement fails is reported as refused");
            TapeCheck(File.ReadAllBytes(gp).SequenceEqual(gb) && File.ReadAllBytes(tp).SequenceEqual(tb) && Directory.GetFiles(dir, "*.tmp").Length == 0, "scene fail: a refused save leaves the old ghost and tape byte for byte, and no .tmp");
            // The tape replacement fails after the ghost was replaced: the new ghost IS the record.
            var c3 = TapeGame(dir, new[] { "--start", "--autodrive", "--track=ilyich", "--tape-debug", "--tape-quit-laps=1", "--tape-fail=tape" }, 300);
            var (g3, t3, ok3) = TapeSceneLoad(dir, "ilyich", 0);
            TapeCheck(c3.Has("the new ghost is the record, the tape could not be saved") && c3.Has("ghost saved, tape not"), "scene fail: a failing tape replacement is reported as 'ghost saved, tape not'");
            TapeCheck(g3 != null && g3.LapTime < g1.LapTime - 1f && File.ReadAllBytes(tp).SequenceEqual(tb) && !ok3, $"scene fail: the new, faster ghost ({g3?.LapTime:F2} s < {g1?.LapTime:F2} s) is on disk, the old tape stays and no longer matches it by fingerprint");
            TapeCheck(File.ReadAllText(Path.Combine(dir, "track_ilyich.sig")).Trim().Length > 0 && Directory.GetFiles(dir, "*.tmp").Length == 0, "scene fail: the signature was written for the new ghost and no .tmp remains");
            var r = TapeGame(dir, new[] { "--track=ilyich", "--tape-debug", "--tape-replay-quit=6" }, 120);
            TapeCheck(r.Exit == 0 && !r.Crashed && !r.Has("from tape") && r.Has("belongs to another ghost") && TapeSummaryLine(r, "replay done") is string s && s.Contains("skipped []") && TapeCountOf(s, "live", "Tractor:DrunkTractor") > 100, "scene fail: a fresh game drops the old tape by its fingerprint: the replay runs live, nothing skipped, the tractor ticks");
        }
        finally { TapeSceneRm(dir); }
    }

    // ---- 3: a ghost with no tape, and tapes that are corrupt in different ways: the replay runs live (or the actor alone is uncovered) ----
    void TapeSceneLegacyAndCorrupt(string src)
    {
        var tpName = Path.GetFileName(TapeTapeFile(src, "ilyich", 0));
        var variants = new List<(string Name, Action<string> Make, Func<TapeChild, bool> Expect, string What)>();
        variants.Add(("legacy ghost, no tape", d => File.Delete(Path.Combine(d, tpName)), c => !c.Has("from tape"), "runs live"));
        variants.Add(("truncated body", d => { var f = Path.Combine(d, tpName); var b = File.ReadAllBytes(f); File.WriteAllBytes(f, b[..(b.Length * 6 / 10)]); }, c => !c.Has("from tape"), "runs live"));
        variants.Add(("flipped bytes in the body", d => { var f = Path.Combine(d, tpName); var b = File.ReadAllBytes(f); for (int i = b.Length - 400; i < b.Length - 300; i++) b[i] ^= 0xA5; File.WriteAllBytes(f, b); }, c => !c.Has("from tape"), "runs live"));
        variants.Add(("flipped bytes in the middle of the file", d => { var f = Path.Combine(d, tpName); var b = File.ReadAllBytes(f); for (int i = b.Length / 2; i < b.Length / 2 + 60; i++) b[i] ^= 0xFF; File.WriteAllBytes(f, b); }, c => !c.Has("from tape"), "runs live"));
        variants.Add(("garbage file", d => File.WriteAllBytes(Path.Combine(d, tpName), Enumerable.Range(0, 5000).Select(i => (byte)(i * 31 + 7)).ToArray()), c => !c.Has("from tape"), "runs live"));
        variants.Add(("another ghost's fingerprint", d => { var f = Path.Combine(d, tpName); var t = WorldTapeCodec.Decode(File.ReadAllBytes(f)); t.GhostFingerprint ^= 1; File.WriteAllBytes(f, WorldTapeCodec.Encode(t)); }, c => !c.Has("from tape") && c.Has("belongs to another ghost"), "runs live"));
        variants.Add(("a body that disagrees with its header", d => { var f = Path.Combine(d, tpName); var t = WorldTapeCodec.Decode(File.ReadAllBytes(f)); t.SampleCount += 5; File.WriteAllBytes(f, WorldTapeCodec.Encode(t)); }, c => !c.Has("from tape") && (c.Has("corrupt or inconsistent body") || c.Has("unreadable, ignored")), "runs live"));
        variants.Add(("the tractor's structure hash differs", d => { var f = Path.Combine(d, tpName); var t = WorldTapeCodec.Decode(File.ReadAllBytes(f)); t.ActorStructure["Tractor:DrunkTractor"].Hash ^= 1; File.WriteAllBytes(f, WorldTapeCodec.Encode(t)); },
            c => TapeKeyList(c.Find("from tape: covers ["), "covers") is { Length: > 0 } cv && !cv.Contains("Tractor:DrunkTractor") && TapeSummaryLine(c, "replay done") is string s && TapeCountOf(s, "skipped", "Tractor:DrunkTractor") == 0 && TapeCountOf(s, "live", "Tractor:DrunkTractor") > 100, "only the tractor is uncovered and ticks live"));
        variants.Add(("the tractor's rest scale is tampered in the header (hash kept)", d =>
        {
            var f = Path.Combine(d, tpName); var t = WorldTapeCodec.Decode(File.ReadAllBytes(f)); var st = t.ActorStructure["Tractor:DrunkTractor"];
            foreach (var k in st.RestPoses.Keys.ToArray()) st.RestPoses[k] = new Transform3D(st.RestPoses[k].Basis.Scaled(new Vector3(3, 3, 3)), st.RestPoses[k].Origin);
            File.WriteAllBytes(f, WorldTapeCodec.Encode(t));
        }, c => TapeKeyList(c.Find("from tape: covers ["), "covers").Contains("Tractor:DrunkTractor") && TapeNum(c.Find("testpart basis error"), "error") < 0.01f && c.Find("testpart basis error").Contains("scale (1.60, 1.00, 0.50)"), "the live rest scale is used, not the header's"));
        foreach (var (name, make, expect, what) in variants)
        {
            string d = TapeSceneDir("v");
            try
            {
                TapeCopyDir(src, d); make(d);
                var c = TapeGame(d, new[] { "--track=ilyich", "--tape-debug", "--tape-test-part", "--tape-replay-quit=5" }, 120);
                var s = TapeSummaryLine(c, "replay done");
                bool notSuppressed = what != "runs live" || (s != null && s.Contains("skipped []") && TapeCountOf(s, "live", "Tractor:DrunkTractor") > 100);
                TapeCheck(c.Exit == 0 && !c.TimedOut && !c.Crashed && s != null && notSuppressed && expect(c), $"scene 3: {name}: {what}, no exception{(c.Crashed ? " (CRASHED)" : "")}{(s == null ? " (no summary)" : "")}");
            }
            finally { TapeSceneRm(d); }
        }
    }

    // ---- 4: the replay wraps; Seek at the start and at every wrap; the poses at a wrap are the poses at the start ----
    void TapeSceneLoop(string dir)
    {
        var c = TapeGame(dir, new[] { "--track=ilyich", "--tape-debug", "--tape-test-part", "--tape-replay-quit=135" }, 300);
        var seeks = c.All("tape: seek #").Select(l => Regex.Match(l, @"seek #(\d+) at ([\d.]+) poses ([0-9a-f]+)")).Where(m => m.Success).ToList();
        TapeCheck(c.Exit == 0 && !c.Crashed && seeks.Count == 3, $"scene 4: Seek was called at the start and at both wraps of a 135 s replay ({seeks.Count} calls)");
        TapeCheck(seeks.Count == 3 && seeks.All(m => m.Groups[2].Value == seeks[0].Groups[2].Value), $"scene 4: every Seek is at the start position ({string.Join(", ", seeks.Select(m => m.Groups[2].Value))} s)");
        TapeCheck(seeks.Count == 3 && seeks.All(m => m.Groups[3].Value == seeks[0].Groups[3].Value), $"scene 4: the poses after each wrap equal the poses at the start ({string.Join(", ", seeks.Select(m => m.Groups[3].Value))})");
        var tp1 = c.Find("testpart basis error");
        TapeCheck(tp1 != null && TapeNum(tp1, "error") < 0.01f, $"scene 4: the scaled part follows the tape across the wraps ({tp1})");
        var s = TapeSummaryLine(c, "replay done");
        TapeCheck(s != null && s.Contains("seeks 3") && TapeCountOf(s, "skipped", "Tractor:DrunkTractor") > 15000, $"scene 4: the tractor's tick was skipped through all wraps");
    }

    // ---- 5: a covered actor's live tick is NOT called in the replay, an uncovered one still ticks ----
    void TapeSceneSuppression(string dir)
    {
        var c = TapeGame(dir, new[] { "--track=ilyich", "--tape-debug", "--tape-test-part", "--tape-replay-quit=45" }, 200);
        var s = TapeSummaryLine(c, "replay done"); var travel2 = c.Lines.LastOrDefault(l => l.StartsWith("tape: travel"));
        int skipped = TapeCountOf(s, "skipped", "Tractor:DrunkTractor"), liveT = TapeCountOf(s, "live", "Tractor:DrunkTractor"), jets = TapeCountOf(s, "live", "Jets:BalalaikaSorties"), busSk = TapeCountOf(s, "skipped", "Bus:Bus"), kgbSk = TapeCountOf(s, "skipped", "None:KgbOfficers"), babSk = TapeCountOf(s, "skipped", "Babushkas:Babushkas");
        TapeCheck(c.Exit == 0 && skipped > 4000 && liveT == 0, $"scene 5: the covered tractor's live tick was skipped {skipped} times and called {liveT} times in the replay");
        TapeCheck(jets == skipped && busSk == skipped && kgbSk == skipped && babSk == skipped && TapeCountOf(s, "live", "Bus:Bus") + TapeCountOf(s, "live", "None:KgbOfficers") + TapeCountOf(s, "live", "Babushkas:Babushkas") == 0, $"scene 5: the uncovered jets tick every step ({jets}); the covered bus, KGB and babushkas are skipped like the tractor ({busSk} bus, {kgbSk} KGB, {babSk} babushkas, none live)");
        float tractor = TapeNum(travel2, "Tractor:DrunkTractor"), busGo = TapeNum(travel2, "Bus:Bus");
        TapeCheck(tractor > 20f, $"scene 5: the covered tractor's root moved {tractor:F0} m in the replay (posed by the tape)");
        TapeInfo($"scene 5: the uncovered bus moved {busGo:F0} m in the same 45 s ({travel2?.Trim()})");
    }

    // ---- 6: leave the title for a race (and by a preview) and come back ----
    // NOTE: the "race" trip only reaches the COUNTDOWN (PrepareRun, as pressing RACE does); no lap is driven or saved. It tests leaving the replay and
    // coming back, never a completed race: the same-session lap is scene 10 (PhysicsTest.Tape.Scene3.cs).
    void TapeSceneTrip(string dir)
    {
        foreach (bool preview in new[] { false, true })
        {
            var c = TapeGame(dir, new[] { "--track=ilyich", "--tape-debug", "--tape-test-part", "--tape-title-trip=4,10" + (preview ? ",preview" : ""), "--tape-replay-quit=12" }, 200);
            string label = preview ? "a track preview" : "a race";
            var before = TapeSummaryLine(c, "before the trip"); var mid = TapeSummaryLine(c, "end of the race part"); var end = TapeSummaryLine(c, "replay done");
            int s0 = TapeCountOf(before, "skipped", "Tractor:DrunkTractor"), s1 = TapeCountOf(mid, "skipped", "Tractor:DrunkTractor"), s2 = TapeCountOf(end, "skipped", "Tractor:DrunkTractor");
            int l1 = TapeCountOf(mid, "live", "Tractor:DrunkTractor"), l2 = TapeCountOf(end, "live", "Tractor:DrunkTractor");
            TapeCheck(c.Exit == 0 && !c.Crashed && before != null && mid != null && end != null, $"scene 6 ({label}): the trip ran to its end ({c.Seconds:F0} s)");
            TapeCheck(s0 > 300 && s1 - s0 <= (preview ? 10 : 0), $"scene 6 ({label}): outside the replay no tick is suppressed (skipped {s0} before, {s1} after the away part)");
            TapeCheck(l1 > 100 && l2 == l1, $"scene 6 ({label}): the tractor ticks live while away ({l1} ticks) and not again in the replay after the return (still {l2})");
            TapeCheck(s2 > s1 + 300 && c.Has("trip: back on the title, replaying True"), $"scene 6 ({label}): back on the title the replay skips the tractor's tick again ({s1} -> {s2})");
            TapeCheck(c.Has("released: covered parts back at rest"), $"scene 6 ({label}): the covered parts were given back at rest when the replay ended");
            if (!preview)
            {
                TapeCheck(c.Has("trip: race started, state Countdown"), "scene 6 (a race): the trip only reached the Countdown (no lap was driven or saved; see scene 10 for that)");
                // The rest is the pose the world was built in. After a start the live actors place themselves anew (a babushka's Reset draws a new place and phase), so a part
                // standing off its build-time rest is no proof of a stale tape pose: what must hold is that no actor but those whose own Reset moves them is off, and that
                // the tractor (whose Reset restores its build pose) is not.
                var off = c.Find("covered parts off rest");
                var by = TapeKeyList(off, "by actor");
                TapeCheck(off != null && !by.Contains("Tractor:DrunkTractor") && by.All(k => k == "Babushkas:Babushkas"), $"scene 6 (a race): after the race started only the babushkas (their Reset draws new places) stand off their build-time rest ({TapeBracket(off, "by actor")})");
            }
            else TapeCheck(c.Has("trip: preview opened") && c.Has("released: covered parts back at rest") && l1 > 100, "scene 6 (a track preview): the preview (no PlaceAtStart) ended the replay: parts released within a few ticks, the tractor ticks live");
            var seeks = c.All("tape: seek #").Select(l => Regex.Match(l, @"poses ([0-9a-f]+)")).ToList();
            TapeCheck(seeks.Count == 2 && seeks[0].Groups[1].Value == seeks[1].Groups[1].Value, $"scene 6 ({label}): the replay re-seeks on return to the same poses ({string.Join(", ", seeks.Select(m => m.Groups[1].Value))})");
        }
    }

    // ---- 8: the busiest track (bonus: the BTR is marked): the cost at the finish line, and the replay covers the BTR ----
    void TapeSceneTrailerSession()
    {
        string dir = TapeSceneDir("trailer-session");
        string laps = Path.Combine(dir, "laps");
        try
        {
            var c = TapeGame(dir, new[] { "--start", "--autodrive", "--track=siberia", "--no-record",
                "--void-at=8", "--tape-debug", "--tape-session-laps=" + laps, "--tape-quit-laps=2" }, 500);
            TapeCheck(c.Exit == 0 && !c.TimedOut && !c.Crashed, "session: two real Siberia laps finish, including the rescued first lap");
            for (int i = 1; i <= 2; i++)
            {
                string lap = Path.Combine(laps, $"lap-{i:D3}");
                var (g, t, paired) = TapeSceneLoad(lap, "siberia", 0);
                TapeCheck(paired && g.Duration > 20 && t.SampleCount > 100,
                    $"session: lap {i} saves a loadable matching car/world-tape pair");
                TapeCheck(g?.HasDamage == true, $"session: lap {i} includes original car damage state");
                string meta = Path.Combine(lap, "session-lap.json");
                bool? ranked = File.Exists(meta)
                    ? System.Text.Json.JsonDocument.Parse(File.ReadAllText(meta)).RootElement.GetProperty("ranked").GetBoolean() : null;
                TapeCheck(ranked == (i == 2), $"session: lap {i} records its correct ranked/void status ({ranked})");
            }
            TapeCheck(!File.Exists(TapeGhostFile(dir, "siberia", 0)) && !File.Exists(TapeTapeFile(dir, "siberia", 0)),
                "session: filming does not create ordinary record files");
            var r = TapeGame(Path.Combine(laps, "lap-001"), new[] { "--track=siberia", "--tape-debug",
                "--tape-wrap-every=2", "--tape-replay-quit=6" }, 180);
            TapeCheck(r.Exit == 0 && !r.Crashed && r.Has("from tape: covers") && r.All("tape: seek #").Count() >= 2,
                "session: the rescued lap replays from its saved pair with seeks/loops and clean exit");
        }
        finally { TapeSceneRm(dir); }
    }

    void TapeSceneBonus() => TapeSceneBonusRun("scene 8", 0, new[] { "--start", "--autodrive", "--track=bonus", "--tape-debug", "--tape-quit-laps=1" });

    /// <summary>
    /// scene 8b: the same record/save/load/replay assertions on the SAME Bonus world (Hind sorties, BTR patrols, tank battle, babushkas), with a survivable
    /// fixture: <c>--training</c> (Stakhanov laps, tape set 1, files *_stakhanov). Training turns the engine health off (EngineHealth.Enabled is false, so a Hind cannon
    /// hit or a crash cannot destroy the car; the actors still fly, shoot and drive). Existing flag, nothing added to the game. It does NOT prove the original
    /// scene 8 path (the rally rules with a mortal bot, where the autodriven car is destroyed by the Hind at lap clock ~48 s: that stays a real failure of scene 8).
    /// </summary>
    void TapeSceneBonusSurvivable() => TapeSceneBonusRun("scene 8b", 1, new[] { "--start", "--autodrive", "--track=bonus", "--training", "--tape-debug", "--tape-quit-laps=1" });

    void TapeSceneBonusRun(string label, int set, string[] childArgs)
    {
        string dir = TapeSceneDir("bonus");
        try
        {
            var c = TapeGame(dir, childArgs, 500, env: new() { ["FORCEBOOT"] = "1" });
            // The child ends itself (exit 3) with this line when the autodriven car meets a terminal Failed state before the lap is done: report that real failure, nothing else.
            var failLine = c.Lines.FirstOrDefault(l => l.StartsWith("AUTODRIVE FAILED:"));
            if (c.Exit == 3 && failLine != null)
            {
                TapeCheck(false, $"{label}: the autodriven bonus run did not finish its lap; the child exited 3 after \"{failLine.Trim()}\" ({c.Seconds:F0} s)");
                Skip($"{label}: NOT RUN because of that failure: matching pair, finish-line cost, replay coverage (BTR patrols, tanks, Hinds), BTR live tick skipped, replay seeks and loops (no finished lap to record)");
                return;
            }
            var (g, t, paired) = TapeSceneLoad(dir, "bonus", set);
            TapeCheck(!c.TimedOut && c.Exit == 0 && !c.Crashed && g != null && t != null && paired, $"{label}: the bonus track's lap wrote a matching pair ({c.Seconds:F0} s)");
            var sv = c.Find($"World tape: set {set} lap");
            long mainMs = sv != null ? (long)TapeNum(sv, "main thread") : long.MaxValue;
            TapeInfo($"{label} (Bonus, BTR marked): {sv?.Trim()}; tape file {(t != null ? new FileInfo(TapeTapeFile(dir, "bonus", set)).Length / 1024 : 0)} KB");
            TapeCheck(sv != null && mainMs < 50, $"{label}: the finish line costs the main thread {mainMs} ms on the Bonus track (the pair save runs on a worker; under 50 ms), and the quit that follows at once still left the matching pair on disk");
            TapeInfo($"{label} (Bonus): {c.Find("tape: lap 1 ")?.Trim()}");
            var r = TapeGame(dir, new[] { "--track=bonus", "--tape-debug", "--tape-wrap-every=4", "--tape-replay-quit=20" }.Concat(set == 1 ? new[] { "--training" } : Array.Empty<string>()).ToArray(), 200, env: new() { ["FORCEBOOT"] = "1" });
            var cov = r.Find($"replay of set {set} from tape");
            TapeCheck(r.Exit == 0 && !r.Crashed && cov != null && cov.Contains("BtrPatrols") && cov.Contains("TankBattle") && cov.Contains("HindSorties"), $"{label}: the bonus replay covers the BTR patrols, tanks and Hinds after the sliced build ({cov?.Split("live [")[0].Trim()})");
            var sm = TapeSummaryLine(r, "replay done");
            TapeCheck(sm != null && TapeCountOf(sm, "skipped", "Btr:BtrPatrols") > 1000 && TapeCountOf(sm, "live", "Btr:BtrPatrols") == 0, $"{label}: the BTR's live tick is skipped in the bonus replay");
            int seeks = r.All("tape: seek #").Count();
            TapeCheck(seeks >= 3, $"{label}: the bonus replay seeks through repeated loops ({seeks} seeks)");
        }
        finally { TapeSceneRm(dir); }
    }
}
