using System;
using System.IO;
using System.Linq;
using CCCPRacing.Race;
using CCCPRacing.Track;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    /// <summary>
    /// The medal jumps of the Socialist Rally (the owner's order, 2026-11-19): a ranked lap strictly under the bronze norm's time takes a run without its bronze to BronzeAt norms beaten (never more than
    /// the bronze), once the run has its bronze a lap strictly under the silver norm's time takes it to SilverAt, and after the silver the norms come one a lap. One real lap, one success's window.
    /// </summary>
    void NormJumpTest()
    {
        GD.Print("Norm jumps (medal jumps)");
        const NormPlan.Outcome C = NormPlan.Outcome.Continue, N = NormPlan.Outcome.NewNorm, S = NormPlan.Outcome.Success;
        NormPlan.UseTrack(null);      // (the default ladder 75, 72.5 ... 52.5; bronze after 4 norms: 1:07.50, silver after 7: 1:00.00)
        int bronze = NormPlan.BronzeAt, silver = NormPlan.SilverAt, n = NormPlan.Steps;
        float bronzeTime = NormPlan.Ladder[bronze - 1], silverTime = NormPlan.Ladder[silver - 1];
        Check(bronze == 4 && silver == 7 && n == 10 && bronzeTime == 67.5f && silverTime == 60f, "the default ladder: bronze after 4 norms (1:07.50), silver after 7 (1:00.00), 10 norms");
        var p = new NormPlan();

        // Five gold-time laps from the start: bronze, silver, norm 8, norm 9, gold.
        p.Reset(true);
        Check(p.OnLap(50f, true) == N && p.Step == bronze && p.Jumped == 4 && p.Used == 1 && NormPlan.MedalFor(p.Step) == NormPlan.Medal.Bronze, "1st gold-time lap: straight to the bronze (4 norms), not further");
        Check(p.OnLap(50f, true) == N && p.Step == silver && p.Jumped == 3 && p.Used == 2 && NormPlan.MedalFor(p.Step) == NormPlan.Medal.Silver, "2nd: the bronze is earned, so straight to the silver (7 norms)");
        Check(p.OnLap(50f, true) == N && p.Step == 8 && p.Jumped == 1 && p.Norm == 55f, "3rd: after the silver one norm a lap: norm 8 beaten, norm 9 (0:55.00) next");
        Check(p.OnLap(50f, true) == N && p.Step == 9 && p.Norm == 52.5f, "4th: norm 9 beaten, the Stakhanovite 0:52.50 next");
        Check(p.OnLap(50f, true) == S && p.Fulfilled && p.Finished && p.Used == 5, "5th: gold, the plan is fulfilled: five real laps");

        // A bronze-time lap from the start: the bronze only; the next lap under the silver time: the silver; between them ordinary climbing.
        p.Reset(true);
        Check(p.OnLap(67.4f, true) == N && p.Step == bronze, "67.4 s from norm 1 (under 1:07.50): the bronze");
        Check(p.OnLap(64f, true) == N && p.Step == bronze + 1 && p.Jumped == 1, "64 s: under norm 5 (1:05.00) but over the silver time: one norm");
        Check(p.OnLap(59.9f, true) == N && p.Step == silver && p.Jumped == 2, "59.9 s from norm 6: under the silver time 1:00.00: the silver (7 norms)");
        p.Reset(true, silver);
        Check(p.OnLap(59f, true) == C && p.Step == silver, "after the silver a lap over norm 8 (0:57.50) does not climb");
        Check(p.OnLap(57.4f, true) == N && p.Step == silver + 1 && p.Jumped == 1, "57.4 s beats norm 8: one step");

        // Exactly on a limit is not under it (the strict rule).
        p.Reset(true);
        Check(p.OnLap(bronzeTime, true) == N && p.Step == 1 && p.Jumped == 1, "exactly 1:07.50 does not jump: it beats norm 1 only (the strict under-the-time rule)");
        p.Reset(true, bronze);
        Check(p.OnLap(silverTime, true) == N && p.Step == bronze + 1 && p.Jumped == 1, "exactly 1:00.00 does not jump to the silver: it beats norm 5 only");
        p.Reset(true, bronze);
        Check(p.OnLap(silverTime - 0.01f, true) == N && p.Step == silver, "0.01 s under 1:00.00 does");

        // Void or unranked: nothing moves (it only uses a lap).
        p.Reset(true);
        Check(p.OnLap(50f, false) == C && p.Step == 0 && p.Left == 4 && p.Used == 1 && p.PassedTimes.All(t => t == 0), "a void or unranked gold-time lap does not jump: it uses a lap");
        p.Reset(true, bronze);
        Check(p.OnLap(50f, false) == C && p.Step == bronze && p.Left == 4, "a void lap does not jump to the silver either");
        p.Reset(true);
        Check(p.OnLap(0f, true) == C && p.Step == 0, "a lap without a time does not jump");

        // The climate's allowance is on the medal times too, in the check and in the record of the target.
        p.Reset(true); p.Climate = () => 3.5f;
        Check(p.OnLap(bronzeTime + 3.5f, true) == N && p.Step == 1, "with a 3.5 s allowance exactly 1:11.00 is not under the bronze time: one norm");
        p.Reset(true); p.Climate = () => 3.5f;
        Check(p.OnLap(bronzeTime + 3.4f, true) == N && p.Step == bronze && Math.Abs(p.PassedTargets[bronze - 1] - (bronzeTime + 3.5f)) < 1e-4f, "1:10.90 is under the 1:11.00 bronze time: the bronze, the target recorded with the allowance");
        p.Reset(true, bronze);
        Check(p.OnLap(silverTime + 3.4f, true) == N && p.Step == silver && Math.Abs(p.PassedTargets[silver - 1] - (silverTime + 3.5f)) < 1e-4f, "and 1:03.40 under 1:03.50: the silver");
        p.Climate = null;

        // The window of one success, one real lap: the laps saved come along, up to the ten, whatever number of norms the lap jumped.
        p.Reset(true, 0, 1);
        Check(p.OnLap(50f, true) == N && p.Step == bronze && p.Left == NormPlan.WindowLaps && p.Used == 1, "a jump from the last lap of the first norm: five laps (the saved ones were none), one lap used");
        p.Reset(true);
        p.OnLap(80f, true); p.OnLap(80f, true);
        Check(p.Left == 3, "two slow laps: three left");
        Check(p.OnLap(50f, true) == N && p.Step == bronze && p.Left == NormPlan.WindowLaps + 3 - 1 && p.Used == 3, "the jump gives one success's window: five and the two laps saved (7), not five per norm");
        p.Reset(true);
        Check(p.OnLap(50f, true) == N && p.Left == 9, "from a full window of five: the five and the four saved (9)");

        // Only the norm the lap landed on has a time; the skipped norms have none (no lap was driven for them).
        p.Reset(true);
        p.OnLap(50f, true);
        Check(p.PassedTimes[bronze - 1] == 50f && p.PassedTimes.Take(bronze - 1).All(t => t == 0) && p.PassedTargets.Take(bronze - 1).All(t => t == 0) && p.PassedTargets[bronze - 1] == bronzeTime,
            "after the bronze jump only norm 4 has the lap's time and target; norms 1-3 have none");
        p.OnLap(50f, true);
        Check(p.PassedTimes[silver - 1] == 50f && p.PassedTimes[bronze] == 0 && p.PassedTimes[bronze + 1] == 0 && p.Used == 2 && p.PassedTimes.Count(t => t > 0) == 2, "after the silver jump: two times in all (norms 4 and 7) for two real laps");

        // A medal from an earlier run never moves the steps: only this run's norms count.
        {
            string dir = Path.Combine(Path.GetTempPath(), "cccp-normjump-" + System.Environment.ProcessId) + "/";
            Directory.CreateDirectory(dir);
            try
            {
                var campaign = new Campaign(dir);
                Check(campaign.Award("ilyich", NormPlan.Medal.Gold, FuelGrade.Petrol) && campaign.Best("ilyich") == NormPlan.Medal.Gold, "the career has a gold on the track");
                p.Reset(true);
                Check(p.OnLap(50f, true) == N && p.Step == bronze && NormPlan.MedalFor(p.Step) == NormPlan.Medal.Bronze, "a new run still has to earn its bronze first: the gold-time lap gives the bronze");
                Check(!campaign.Award("ilyich", NormPlan.Medal.Bronze, FuelGrade.Petrol) && campaign.Best("ilyich") == NormPlan.Medal.Gold, "the run's bronze does not improve (or lower) the career gold");
                Check(p.OnLap(50f, true) == N && p.Step == silver && NormPlan.MedalFor(p.Step) == NormPlan.Medal.Silver, "and the silver comes on the run's second lap, as for a first-time driver");
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        // The medal of a jump counts like any: the bronze opens, the silver is the run's best; MedalFor of the step.
        Check(NormPlan.MedalFor(bronze) == NormPlan.Medal.Bronze && NormPlan.MedalFor(silver) == NormPlan.Medal.Silver && NormPlan.MedalFor(n) == NormPlan.Medal.Gold && NormPlan.MedalFor(bronze - 1) == NormPlan.Medal.None, "the medal of a step: 3 none, 4 bronze, 7 silver, 10 gold");

        // The track's own medal steps and the fuel's pace: the ladder of each track and fuel, the same rules.
        foreach (var track in new[] { "ilyich", "siberia", "atomgrad", "bonus" })
            foreach (FuelGrade fuel in Enum.GetValues(typeof(FuelGrade)))
            {
                var def = TrackDef.Load($"res://tracks/{track}.json").Norms;
                NormPlan.UseTrack(def); NormPlan.UseFuel(fuel);
                int bz = NormPlan.BronzeAt, sv = NormPlan.SilverAt; float[] ladder = NormPlan.Ladder;
                var q = new NormPlan(); q.Reset(true);
                float justUnderBronze = ladder[bz - 1] - 0.01f, justUnderSilver = ladder[sv - 1] - 0.01f;
                bool ok = q.OnLap(justUnderBronze, true) == N && q.Step == bz;
                ok &= q.OnLap(justUnderSilver, true) == N && q.Step == sv;
                q.Reset(true);
                ok &= q.OnLap(ladder[bz - 1], true) == N && q.Step == 1;      // (exactly on the bronze time: one norm)
                Check(ok, $"{track}/{fuel}: bronze after {bz} (under {LapTimer.Format(ladder[bz - 1])}), silver after {sv} (under {LapTimer.Format(ladder[sv - 1])}) on this track's ladder and fuel");
                NormPlan.UseTrack(null);
            }
        NormPlan.UseTrack(null);
    }
}
