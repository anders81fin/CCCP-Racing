using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Godot;

namespace CCCPRacing.Tests;

// --tape-scene-tests, part 2 (T1b): the phase-1 actors of every track family are marked, so one real lap per family is recorded in a headless child
// game (its own temporary --data-dir) and the numbers are judged: how many actors and parts the index holds, the tape's size, the cost per sample,
// the finish-line cost on the main thread (under 50 ms), and that a second process (another seed) covers exactly the actors that were recorded. One trace comparison
// on the Bonus track (the busiest world): recording on and off leave the simulation bit for bit alike.
public partial class PhysicsTest
{
    static readonly string[] TapeFamilies = { "ilyich", "siberia", "atomgrad", "bonus" };

    /// <summary>The keys of "group [a=1, b=2]" in a log line (the "=n" is dropped).</summary>
    static string[] TapeKeyList(string line, string group)
    {
        var g = Regex.Match(line ?? "", Regex.Escape(group) + @" \[([^\]]*)\]");
        return !g.Success || g.Groups[1].Value.Trim().Length == 0 ? Array.Empty<string>() : g.Groups[1].Value.Split(", ").Select(k => k.Contains('=') ? k[..k.IndexOf('=')] : k).ToArray();
    }

    static string TapeBracket(string line, string group) => Regex.Match(line ?? "", Regex.Escape(group) + @" \[([^\]]*)\]").Groups[1].Value;

    void TapeScene2()
    {
        foreach (var track in TapeFamilies) TapeScene2Track(track);
        TapeScene2Trace();
    }

    void TapeScene2Track(string track)
    {
        string dir = TapeSceneDir("f" + track);
        try
        {
            var c = TapeGame(dir, new[] { "--start", "--autodrive", "--track=" + track, "--tape-debug", "--tape-quit-laps=1" }, 600);
            var (g, t, paired) = TapeSceneLoad(dir, track, 0);
            TapeCheck(!c.TimedOut && c.Exit == 0 && !c.Crashed && g != null && t != null && paired, $"scene 9 ({track}): the lap was recorded and saved as a matching pair ({c.Seconds:F0} s)");
            var build = c.Find("scene build #1"); var idx = c.Find("tape: index actors [");
            var cost = c.Find("record cost of lap 1"); var sv = c.Find("World tape: set 0 lap");
            var actors = TapeKeyList(idx, "index actors");
            int parts = (int)TapeNum(build, "index");
            long size = File.Exists(TapeTapeFile(dir, track, 0)) ? new FileInfo(TapeTapeFile(dir, track, 0)).Length : 0;
            TapeInfo($"scene 9 ({track}): index {parts} parts, {actors.Length} actors [{TapeBracket(idx, "index actors")}], not indexed [{TapeBracket(idx, "unmarked")}]");
            TapeInfo($"scene 9 ({track}): tape file {size / 1024} KB; {c.Find("tape: lap 1 ")?.Trim()}");
            TapeInfo($"scene 9 ({track}): {cost?.Trim()}");
            TapeInfo($"scene 9 ({track}): {c.Find("counters of lap 1")?.Trim()}");
            TapeInfo($"scene 9 ({track}): {sv?.Trim()}");
            TapeCheck(actors.Length >= 2 && parts >= 10, $"scene 9 ({track}): the index holds {actors.Length} actors with {parts} parts");
            // cost: the provisional budget of docs/replay-tape.md section 10 (avg 0.5 ms, p99 1.5 ms per sample), hard fail at twice that
            if (cost != null)
            {
                float avg = TapeNum(cost, "avg"), p99 = TapeNum(cost, "p99");
                TapeCheck(avg < 1.0f && p99 < 3.0f, $"scene 9 ({track}): the recording cost per sample is within twice the budget (avg {avg:F3} ms, p99 {p99:F3} ms)");
            }
            else TapeCheck(false, $"scene 9 ({track}): the record cost line was printed");
            long main = sv != null ? (long)TapeNum(sv, "main thread") : long.MaxValue;
            TapeCheck(sv != null && main < 50 && sv.Contains("(on the worker)"), $"scene 9 ({track}): the finish line costs the main thread {main} ms (Finish and handing the pair save to a worker; under 50 ms): {sv?.Trim()}");
            // a second process, another seed: every recorded actor is covered again, so the structure does not depend on the run
            var r = TapeGame(dir, new[] { "--track=" + track, "--tape-debug", "--tape-replay-quit=12" }, 240, seed: "7");
            var covers = TapeKeyList(r.Find("from tape: covers ["), "covers");
            bool all = actors.Length > 0 && actors.All(covers.Contains) && covers.All(actors.Contains);
            TapeCheck(r.Exit == 0 && !r.Crashed && all, $"scene 9 ({track}): a fresh process with another seed covers exactly the {actors.Length} recorded actors (covers {covers.Length}, missing [{string.Join(", ", actors.Except(covers))}])");
            var s = TapeSummaryLine(r, "replay done");
            int skippedTicks = s == null ? 0 : actors.Count(a => TapeCountOf(s, "skipped", a) > 100), liveTicks = s == null ? 0 : actors.Count(a => TapeCountOf(s, "live", a) > 0);
            TapeCheck(s != null && skippedTicks == actors.Length && liveTicks == 0, $"scene 9 ({track}): the live tick of all {actors.Length} covered actors is skipped in the replay (skipped {skippedTicks}, still ticking {liveTicks})");
        }
        finally { TapeSceneRm(dir); }
    }

    // ---- the Bonus track (the busiest): the car and world trace is identical with recording on and off over a lap boundary ----
    void TapeScene2Trace()
    {
        var tr = new string[2]; var dirs = new[] { TapeSceneDir("bon"), TapeSceneDir("boff") };
        try
        {
            for (int i = 0; i < 2; i++)
            {
                tr[i] = dirs[i] + ".trace.txt";
                var c = TapeGame(dirs[i], new[] { "--start", "--autodrive", "--track=bonus" }, 600, tapeOff: i == 1, env: new() { ["WORLDTRACE"] = tr[i], ["WORLDTRACE_FRAMES"] = "30000" });
                TapeCheck(!c.TimedOut && c.Exit == 0 && File.Exists(tr[i]) && File.ReadLines(tr[i]).Last() == "END", $"scene 9 trace: the {(i == 0 ? "recording" : "CCCP_TAPE=off")} Bonus trace run completed ({c.Seconds:F0} s)");
                bool pair = File.Exists(TapeTapeFile(dirs[i], "bonus", 0));
                TapeCheck(File.Exists(TapeGhostFile(dirs[i], "bonus", 0)) && pair == (i == 0), $"scene 9 trace: the lap boundary saved a ghost {(i == 0 ? "and a tape" : "and no tape")} inside the Bonus trace");
            }
            if (File.Exists(tr[0]) && File.Exists(tr[1]))
                TapeCheck(File.ReadAllBytes(tr[0]).SequenceEqual(File.ReadAllBytes(tr[1])), $"scene 9 trace: the Bonus car and world trace ({File.ReadLines(tr[0]).Count() - 1} steps) is identical with recording on and off");
        }
        finally { foreach (var d in dirs) TapeSceneRm(d); foreach (var f in tr) try { if (f != null) File.Delete(f); } catch { } }
    }
}
