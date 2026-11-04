using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    /// <summary>
    /// The norms: a ladder of lap times with three laps each; a lap under the norm climbs (skipping
    /// steps it also beat), beating the last fulfils the plan, a used-up window fails it.
    /// </summary>
    void LapBudgetTest()
    {
        GD.Print("Norms (the plan)");
        const NormPlan.Outcome C = NormPlan.Outcome.Continue, N = NormPlan.Outcome.NewNorm,
            S = NormPlan.Outcome.Success, O = NormPlan.Outcome.OutOfLaps;
        var p = new NormPlan();
        p.Reset(true);
        Check(NormPlan.Steps == 10 && NormPlan.Ladder[0] == 75f && NormPlan.Ladder[6] == 60f && NormPlan.Ladder[9] == 52.5f, "ladder: 10 norms, 1:15 down to 52.5 s in 2.5 s steps (norm 7 is 1:00)");
        Check(p.Step == 0 && p.Left == 5 && p.Norm == 75f && !p.OnLastLap, "a rally starts at norm 1 (1:15.00) with five laps");

        // Ladder progress: a lap under the norm hands out the next one and a fresh window.
        Check(p.OnLap(76f, true) == C && p.Left == 4, "a slow lap uses a lap of the window");
        Check(p.OnLap(74.5f, true) == N && p.Step == 1 && p.Norm == 72.5f && p.Left == 8, "under 1:15 on the 2nd lap: norm 2 (1:12.50), five laps and the three saved (8)");
        Check(p.OnLap(72.5f, true) == C && p.Left == 7, "exactly the norm is not under it");
        Check(p.OnLap(72.4f, true) == N && p.Step == 2 && p.Left == 10, "under 1:12.50: norm 3 with five and six saved, capped at ten");

        // No skips: however fast, a lap beats one norm, the next comes with five laps.
        p.Reset(true);
        Check(p.OnLap(61f, true) == N && p.Step == 1 && p.Norm == 72.5f && p.Left == 9, "61.0 s from norm 1: only norm 1 beaten, next 1:12.50 with five laps and four saved (9)");
        for (int k = 0; k < 7; k++) p.OnLap(50f, true);
        Check(p.Step == 8 && p.Left == NormPlan.MaxLaps && !p.Finished, "a 50 s lap each time: one norm a lap, at norm 9 after eight laps");
        Check(p.OnLap(54f, true) == N && p.Step == 9 && p.Norm == 52.5f, "under norm 9: the Stakhanovite 0:52.50 next");
        Check(p.OnLap(52.4f, true) == S && p.Fulfilled && p.Finished, "52.4 s beats the Stakhanovite 0:52.50: PLAN FULFILLED");
        Check(p.OnLap(40f, true) == C && p.Used == 10, "after the end nothing counts");
        p.Reset(true);
        Check(p.OnLap(50f, true) == N && p.Step == 1 && !p.Fulfilled, "one lap under every norm: still only norm 1");

        // A window used up: out of laps (the gulag flow), void laps use laps and never climb.
        p.Reset(true, 5);
        Check(p.OnLap(45f, false) == C && p.Left == 4 && p.Step == 5, "a void lap uses a lap, however fast");
        p.OnLap(63f, true);
        p.OnLap(63f, true);
        Check(p.OnLap(63f, true) == C && p.OnLastLap, "four laps gone: the last lap for this norm");
        Check(p.OnLap(62.6f, true) == O && p.Finished && !p.Fulfilled && p.Left == 0, "the fifth misses 1:02.50: out of laps");

        // Penalties take laps from the current window; the window resets on a new norm.
        p.Reset(true);
        Check(!p.Penalize(2) && p.Left == 3, "destroyed car: -2 laps");
        Check(!p.Penalize(2) && p.Left == 1 && p.OnLastLap, "another: the new car drives the last one");
        Check(p.OnLap(74f, true) == N && p.Left == 5, "a new norm: a full window again, the penalty forgotten");
        Check(!p.Penalize(1) && p.Left == 4, "seized engine (or an empty-tank tow): -1 lap");
        p.Penalize(2);
        Check(p.Penalize(2) && p.Finished && !p.Fulfilled && p.OnLap(40f, true) == C, "two laps lost with two left: out of laps");
        p.Reset(true, 1, 1);
        Check(p.Penalize(1) && p.Finished, "tow on the last lap: out of laps");
        p.Reset(false);
        Check(!p.Penalize(2) && p.OnLap(40f, true) == C && p.Used == 0 && !p.Finished, "training, LAN, demo: no norms");
        for (int i = 0; i < 30; i++) p.OnLap(80f, true);
        Check(!p.Finished && !p.OnLastLap, "training: never out of laps");

        // Tests: --laps-left and --norm-step.
        p.Reset(true, 8, 1);
        Check(p.Step == 8 && p.Norm == 55f && p.OnLastLap, "--norm-step=9 --laps-left=1: norm 0:55.00 on its last lap");

        // The climate's allowance: on top of every norm, in the check too.
        p.Reset(true, 7);
        p.Climate = () => 3.5f;
        Check(p.Norm == 61f && p.LadderTime == 57.5f, "a 3.5 s allowance: norm 8 is 1:01.00");
        Check(p.OnLap(58f, true) == N && p.Step == 8 && p.Norm == 58.5f, "58 s under 61.0 (and 58.5): one norm only, norm 9 (0:58.50 with the allowance)");
        p.Climate = null;
    }
}
