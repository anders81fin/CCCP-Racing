using System;
using System.Collections.Generic;

namespace CCCPRacing.UI;

/// <summary>One line of the Socialist Rally's target card: the same table columns as the Party Meeting's NORMS AND MEDALS (step, time, status, medal).</summary>
public sealed record NormCardRow(int Step, float Time, string Status, string Medal, bool Passed);

/// <summary>What the target card shows: the step just passed (briefly, while the card flashes), the CURRENT pending step and the next medal milestone beyond it; or "all targets met".</summary>
public sealed class NormCardView
{
    public NormCardRow LastPassed;      // the step beaten before the current one (null at the first); drawn only while the progression flash runs
    public NormCardRow Current;         // null once the plan is fulfilled
    public NormCardRow NextMilestone;   // the next DISTINCT medal row beyond Current (null when Current is the last)
    public bool AllMet;
}

/// <summary>
/// The card's rows from the production norms (NormPlan: the ladder at the fuel's pace, the climate's allowance on every norm, the passed steps' own targets and times, the medals' steps). No rules of
/// its own: the same numbers and wording as DrawPauseNorms; RaceScene binds it every frame (RaceScene.NormCard.cs).
/// </summary>
public static class NormCardModel
{
    public static string MedalName(int row1, int bronzeAt, int silverAt, int steps) => row1 == steps ? "GOLD" : row1 == silverAt ? "SILVER" : row1 == bronzeAt ? "BRONZE" : "";

    /// <param name="ladder">The ladder in seconds at the fuel's pace, without the allowance.</param>
    /// <param name="passedTargets">The effective target each passed step was beaten against (its own allowance), 0 where none.</param>
    /// <param name="passedTimes">The lap time that beat each passed step, 0 where none.</param>
    public static NormCardView Build(float[] ladder, float allowance, float[] passedTargets, float[] passedTimes, int step, bool fulfilled, int bronzeAt, int silverAt)
    {
        int n = ladder.Length;
        var v = new NormCardView();
        if (fulfilled || step >= n) { v.AllMet = true; return v; }
        float TimeOf(int i) => i < step && i < passedTargets.Length && passedTargets[i] > 0 ? passedTargets[i] : ladder[i] + allowance;
        string Medal(int i) => MedalName(i + 1, bronzeAt, silverAt, n);
        v.Current = new NormCardRow(step + 1, TimeOf(step), "CURRENT", Medal(step), false);
        if (step > 0)
        {
            int p = step - 1;
            string time = p < passedTimes.Length && passedTimes[p] > 0 ? " " + Race.LapTimer.Format(passedTimes[p]) : "";
            v.LastPassed = new NormCardRow(p + 1, TimeOf(p), "PASSED" + time, Medal(p), true);
        }
        // the next medal row strictly beyond the current step: bronze, silver, gold in order (the current row, when it is itself a medal row, is not repeated)
        foreach (int row1 in new[] { bronzeAt, silverAt, n })
        {
            if (row1 - 1 > step) { v.NextMilestone = new NormCardRow(row1, ladder[row1 - 1] + allowance, "", MedalName(row1, bronzeAt, silverAt, n), false); break; }
        }
        return v;
    }
}
