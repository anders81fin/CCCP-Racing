using System.IO;
using System.Linq;

namespace CCCPRacing.Tests;

// --tape-scene-tests, part 3 (T8): a lap recorded in THIS process is replayed on the title of the SAME scene (no reload). The old code stored the
// fresh slot without its validated body, so the player covered nothing until the next load. The child autodrives lap 1 (a record), and when the
// pair is saved (--tape-title-after-lap=1) goes to the title in the same scene. The test proves a lap was really saved (files, record line) first.
public partial class PhysicsTest
{
    void TapeScene3()
    {
        string dir = TapeSceneDir("same");
        try
        {
            var c = TapeGame(dir, new[] { "--start", "--autodrive", "--track=ilyich", "--tape-debug", "--tape-test-part", "--tape-title-after-lap=1", "--tape-replay-quit=30" }, 400);
            var l1 = c.Find("tape: lap 1 ");
            var (g, t, paired) = TapeSceneLoad(dir, "ilyich", 0);
            TapeCheck(!c.TimedOut && c.Exit == 0 && !c.Crashed, $"scene 10: the child ended cleanly (exit {c.Exit}, {c.Seconds:F0} s)");
            TapeCheck(l1 != null && l1.Contains("record True") && l1.Contains("recording True") && c.All("World tape: set 0 lap").Count() == 1 && c.Has("ghost+tape saved"), $"scene 10: PROOF a lap was saved: lap 1 is a recorded record and the pair save reported ({l1?.Trim()})");
            TapeCheck(File.Exists(TapeGhostFile(dir, "ilyich", 0)) && File.Exists(TapeTapeFile(dir, "ilyich", 0)) && g != null && t != null && paired, "scene 10: PROOF: ghost_ilyich.bin and tape_ilyich.bin exist on disk as a matching pair");
            TapeCheck(c.Has("title entered in the same scene #1 after lap 1: replaying True") && c.Has("saved tape in memory True") && !c.Has("scene build #2") && !c.Has("reloading the scene"), $"scene 10: the title was entered in the SAME scene without a reload ({c.Find("title entered")?.Trim()})");
            var cv = TapeKeyList(c.Find("from tape: covers ["), "covers");
            TapeCheck(cv.Contains("Tractor:DrunkTractor") && cv.Length >= 3 && !c.Has("runs live: no usable tape"), $"scene 10: the lap saved in this session covers its actors in the same scene (covers {cv.Length}: {string.Join(", ", cv)})");
            var s = TapeSummaryLine(c, "replay done"); var travel = c.Lines.LastOrDefault(l => l.StartsWith("tape: travel"));
            var bad = cv.Where(k => TapeCountOf(s, "skipped", k) <= 1000 || TapeCountOf(s, "live", k) != 0).ToArray();
            TapeCheck(s != null && cv.Length > 0 && bad.Length == 0, $"scene 10: the live tick of all {cv.Length} covered actors is skipped in the title replay, none ticks live (offenders [{string.Join(", ", bad)}]; tractor {TapeCountOf(s, "skipped", "Tractor:DrunkTractor")} skipped, {TapeCountOf(s, "live", "Tractor:DrunkTractor")} live)");
            TapeInfo($"scene 10: travel of the covered actors in the replay: {travel?.Trim()}");
            float moved = TapeNum(travel, "Tractor:DrunkTractor");
            TapeCheck(moved > 20f, $"scene 10: the covered tractor's pose moves during the title replay ({moved:F0} m)");
            var tp1 = c.Lines.FirstOrDefault(l => l.Contains("testpart basis error"));
            TapeCheck(tp1 != null && TapeNum(tp1, "error") < 0.01f && TapeNum(tp1, "over") > 200, $"scene 10: the scaled part follows the fresh tape ({tp1})");
        }
        finally { TapeSceneRm(dir); }
    }
}
