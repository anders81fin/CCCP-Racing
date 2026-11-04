using System;
using System.Collections;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

// Windowed scenario "norm-card" (--viewer-windowed-only=normcard): the Socialist Rally's target card in the real game on each track: the card equals the production norms, sits top right on the left aid's top edge,
// shows the progression highlight after a beaten norm, and is absent in every other mode.
public partial class RaceScene
{
    IEnumerable VwNormCard()
    {
        foreach (var x in VwWaitReady()) yield return x;
        foreach (var x in VrUntil(() => _state == State.Racing && _lapClock > 3f, 60 * 40, "the race is running")) yield return x;
        string track = _trackId;
        for (int i = 0; i < 60; i++) yield return null;
        var card = _hud.NormCard;
        VwCheck(card != null && card.Current != null, "the card is up in the Socialist Rally");
        if (card?.Current == null) yield break;
        int step = _norms.Step;
        VwCheck(card.Current.Step == step + 1 && MathF.Abs(card.Current.Time - _norms.Norm) < 1e-4f, $"{track}: the card's current row is the production norm (row {card.Current.Step}, {LapTimer.Format(card.Current.Time)} = {LapTimer.Format(_norms.Norm)})");
        if (card.NextMilestone is { } nm)
            VwCheck(nm.Step == NormPlan.BronzeAt && MathF.Abs(nm.Time - (NormPlan.Ladder[NormPlan.BronzeAt - 1] + _norms.Allowance)) < 1e-4f && nm.Medal == "BRONZE", $"{track}: the next milestone is BRONZE at row {nm.Step}, {LapTimer.Format(nm.Time)} (ladder + allowance)");
        VwLog("card", ("track", track), ("fuel", _rallyFuel), ("current", $"{card.Current.Step} < {LapTimer.Format(card.Current.Time)}"), ("next", card.NextMilestone == null ? "-" : $"{card.NextMilestone.Step} < {LapTimer.Format(card.NextMilestone.Time)} {card.NextMilestone.Medal}"), ("bronze_at", NormPlan.BronzeAt), ("silver_at", NormPlan.SilverAt), ("steps", NormPlan.Steps));
        foreach (var x in VrShot($"normcard-{track}-start")) yield return x;
        // the other modes: no card (the predicate, with the mode flipped for a frame)
        var keepRun = _run;
        foreach (var (name, mode) in new[] { ("Stakhanov laps", RunMode.Training), ("school", RunMode.School), ("LAN", RunMode.Lan) })
        {
            _run = mode; NormCardHud();
            VwCheck(_hud.NormCard == null, $"{track}: no target card in {name}");
        }
        _run = keepRun; NormCardHud();
        VwCheck(_hud.NormCard != null, $"{track}: and the card is back in the Socialist Rally");
        { var vs = new RaceScene { ViewerMode = true }; vs.NormCardHud(); VwCheck(vs._hud == null, "a viewer scene has no card (no hud of its own)"); vs.Free(); }
        // a beaten norm: the production OnLap moves the step, the paper burst's moment flashes the card: the passed row, the new current, the next medal row
        float lapTime = _norms.Norm - 1.234f;
        var outcome = _norms.OnLap(lapTime, true);
        _hud.ReactToNorm(true);
        NormCardHud();
        for (int i = 0; i < 20; i++) yield return null;
        var after = _hud.NormCard;
        VwCheck(outcome == NormPlan.Outcome.NewNorm && after.Current.Step == step + 2 && after.LastPassed != null && after.LastPassed.Step == step + 1 && after.LastPassed.Status.Contains(LapTimer.Format(lapTime)), $"{track}: beating norm {step + 1} moves the card to row {after.Current.Step} and keeps the passed row for the flash");
        foreach (var x in VrShot($"normcard-{track}-passed")) yield return x;
        // the plan fulfilled through the production OnLap (every remaining norm beaten): the card reads all targets met
        for (int guard = 0; guard < 20 && !_norms.Finished; guard++) _norms.OnLap(_norms.Norm - 0.5f, true);
        NormCardHud();
        for (int i = 0; i < 90; i++) yield return null;      // (the progression flash is over by now)
        VwCheck(_norms.Fulfilled && _hud.NormCard != null && _hud.NormCard.AllMet, $"{track}: a fulfilled plan reads all targets met");
        foreach (var x in VrShot($"normcard-{track}-all-met")) yield return x;
    }
}
