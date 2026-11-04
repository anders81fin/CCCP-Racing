using System;
using CCCPRacing.Race;
using CCCPRacing.Track;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Tests;

// --norm-card-tests: the Socialist Rally's target card model on the production norms of all four tracks and the three fuels: the current row, the next DISTINCT medal row, the medal labels, the allowance, the
// passed step, the fulfilled plan; and the card's layout at the supported pixel sizes. The numbers come from NormPlan/TrackDef, nothing is hard-coded.
public partial class PhysicsTest
{
    void NormCardTest()
    {
        GD.Print("Norm card tests");
        var sb = new System.Text.StringBuilder();
        foreach (var track in new[] { "ilyich", "siberia", "atomgrad", "bonus" })
        foreach (FuelGrade fuel in Enum.GetValues(typeof(FuelGrade)))
        {
            var def = TrackDef.Load($"res://tracks/{track}.json").Norms;
            var ladder = NormPlan.LadderFor(def, fuel); NormPlan.UseTrack(def); NormPlan.UseFuel(fuel);
            int n = ladder.Length, bz = NormPlan.BronzeAt, sv = NormPlan.SilverAt; string tag = $"{track}/{fuel}";
            sb.Append($"{tag}: bronze row {bz} < {LapTimer.Format(ladder[bz - 1])}, silver row {sv} < {LapTimer.Format(ladder[sv - 1])}, gold row {n} < {LapTimer.Format(ladder[n - 1])}; ");
            float[] passedT = new float[n], passedL = new float[n];
            bool allOk = true; string bad = "";
            for (int step = 0; step < n; step++)
            {
                var v = NormCardModel.Build(ladder, 0f, passedT, passedL, step, false, bz, sv);
                bool ok = !v.AllMet && v.Current.Step == step + 1 && v.Current.Time == ladder[step] && v.Current.Medal == NormCardModel.MedalName(step + 1, bz, sv, n);
                // the next milestone: strictly after the current row, the first of bronze/silver/gold beyond it, never a repeat of the current row
                int want = step + 1 < bz ? bz : step + 1 < sv ? sv : step + 1 < n ? n : 0;
                ok &= want == 0 ? v.NextMilestone == null : v.NextMilestone != null && v.NextMilestone.Step == want && v.NextMilestone.Time == ladder[want - 1] && v.NextMilestone.Medal != "" && v.NextMilestone.Step != v.Current.Step;
                ok &= (step == 0) == (v.LastPassed == null);
                if (!ok) { allOk = false; bad += $" step {step}"; }
            }
            Check(allOk, $"{tag}: current row, medal label and the next distinct medal row for every step of {n}{bad}");
            Check(NormCardModel.Build(ladder, 0f, passedT, passedL, n - 1, true, bz, sv).AllMet && NormCardModel.Build(ladder, 0f, passedT, passedL, n, true, bz, sv).Current == null, $"{tag}: a fulfilled plan says all targets met");
            // the allowance of the climate: on the current and the next row, never on a passed row (its own target is kept)
            var w = NormCardModel.Build(ladder, 3.5f, new float[n], new float[n], 1, false, bz, sv);
            Check(MathF.Abs(w.Current.Time - (ladder[1] + 3.5f)) < 1e-4f && (w.NextMilestone == null || MathF.Abs(w.NextMilestone.Time - (ladder[w.NextMilestone.Step - 1] + 3.5f)) < 1e-4f), $"{tag}: the climate's allowance is on the current and next rows");
            var pt = new float[n]; pt[0] = ladder[0] + 2f; var pl = new float[n]; pl[0] = ladder[0] - 1.234f;
            var p = NormCardModel.Build(ladder, 3.5f, pt, pl, 1, false, bz, sv);
            Check(p.LastPassed != null && p.LastPassed.Passed && MathF.Abs(p.LastPassed.Time - pt[0]) < 1e-4f && p.LastPassed.Status.StartsWith("PASSED ") && p.LastPassed.Status.Contains(LapTimer.Format(pl[0])), $"{tag}: a passed step keeps its own target and shows its lap time");
        }
        GD.Print("Norm card rows (from the production data): " + sb);
        // the lap panel with norms (NORM row first): the long case (norm 10/10, rocket fuel, climate, wrecks) fits inside its panel
        {
            var lap = new Hud { PlanLaps = 5, PlanLeft = 1, PlanLastLap = true, PlanStep = 9, PlanSteps = 10, PlanTarget = 125.5f, PlanFuel = "ROCKET FUEL", PlanClimate = 12f, Wrecks = 2 };
            float lastRow = Hud.PlanLapsLeftY + 3 * 9, extras = 54 + 2 * 9;
            Check(lastRow + 7 <= 4 + lap.LapPanelHeight - 1 && extras + 7 <= 4 + lap.LapPanelHeight, $"lap panel: LAST (y {lastRow}) and the third extra row (y {extras}) are inside the panel ({4 + lap.LapPanelHeight})");
            Check(lap.LapPanelHeight == 53 + 27, $"lap panel height {lap.LapPanelHeight} = base + three extra rows");
            Check(Hud.PlanLapsLeftY == 8 + 10 && Hud.PlanRowShift == 9, "NORM at y 8, laps left at y 18, TIME/BEST/LAST one row (9 px) lower than without norms");
            var plain = new Hud { PlanLaps = 0 };
            Check(plain.LapPanelHeight == 44, "other modes: the panel height is unchanged (44)");
            lap.Free(); plain.Free();
        }
        // layout at the supported sizes: inside the screen, clear of the left aid, every text inside the card
        foreach (var size in new[] { new Vector2(480, 270), new Vector2(456, 256), new Vector2(512, 288) })
        {
            var hud = new Hud { PlanLaps = 5, PlanLeft = 5, PlanTarget = 95f, PlanFuel = "ROCKET FUEL", NormCard = NormCardModel.Build(NormPlan.Ladder, 0f, new float[NormPlan.Steps], new float[NormPlan.Steps], NormPlan.BronzeAt - 1, false, NormPlan.BronzeAt, NormPlan.SilverAt) };
            var r = hud.NormCardRect(size);
            bool inside = r.Position.X >= 0 && r.Position.Y == 4 && r.End.X <= size.X && r.End.Y <= 70;
            Check(inside && r.Position.X > 4 + hud.LapPanelWidth + 4, $"{size.X}x{size.Y}: the card is top right on the left aid's top edge, clear of the left aid ({4 + hud.LapPanelWidth} px) (card {r})");
            float curW = 28 + PixelFont.Measure("1:00.00", 2), smallW = 28 + PixelFont.Measure("CURRENT BRONZE"), rowW = 6 + PixelFont.Measure(" 7  < 1:00.00") + 4 + PixelFont.Measure("SILVER"), passW = 6 + PixelFont.Measure("10 X PASSED 1:00.00");
            Check(curW <= r.Size.X - 4 && smallW <= r.Size.X - 4 && rowW <= r.Size.X - 3 && passW <= r.Size.X - 4, $"{size.X}x{size.Y}: every card text fits inside it (time {curW}, status {smallW}, next {rowW}, passed {passW} of {r.Size.X})");
            hud.Free();
        }
        // the earned medal's star rests under the card: its top (with the landing thump, radius 7.5 x 1.6) stays clear of the card's bottom, in every card state, at every supported size, and never below the speedo
        foreach (var size in new[] { new Vector2(480, 270), new Vector2(456, 256), new Vector2(512, 288) })
        {
            var states = new (string, NormCardView, bool)[]
            {
                ("current+next", NormCardModel.Build(NormPlan.Ladder, 0f, new float[NormPlan.Steps], new float[NormPlan.Steps], 0, false, NormPlan.BronzeAt, NormPlan.SilverAt), false),
                ("current+next with the progression row", NormCardModel.Build(NormPlan.Ladder, 0f, new float[NormPlan.Steps], new float[NormPlan.Steps], 2, false, NormPlan.BronzeAt, NormPlan.SilverAt), true),
                ("last norm (no next row)", NormCardModel.Build(NormPlan.Ladder, 0f, new float[NormPlan.Steps], new float[NormPlan.Steps], NormPlan.Steps - 1, false, NormPlan.BronzeAt, NormPlan.SilverAt), false),
                ("all targets met", NormCardModel.Build(NormPlan.Ladder, 0f, new float[NormPlan.Steps], new float[NormPlan.Steps], NormPlan.Steps, true, NormPlan.BronzeAt, NormPlan.SilverAt), false),
            };
            foreach (var (name, view, flash) in states)
            {
                var hud = new Hud { NormCard = view };
                if (flash) hud.ReactToNorm(true);      // (the progression flash adds its row)
                float bottom = hud.NormCardRect(size).End.Y, y = hud.MedalCornerTarget(size);
                Check(y - 7.5f * 1.6f >= bottom + 1f && y + 7.5f * 1.6f < size.Y - 40, $"{size.X}x{size.Y}: the star ({y - 12:0.#}..{y + 12:0.#}) is under the {name} card (bottom {bottom}) and above the speedo");
                hud.Free();
            }
            var none = new Hud();
            Check(none.MedalCornerTarget(size) == 33f, $"{size.X}x{size.Y}: with no card (every other mode) the star stays where it was (y 33)");
            none.Free();
        }
        GD.Print($"Norm card tests: {(_failures == 0 ? "all ok" : _failures + " FAIL")}");
    }
}
