using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

// Windowed scenario "hud-toggles" (--viewer-windowed-only=hudtoggles): CAR INSTRUMENTS, LAP INFO and NORMS INFO in SETTINGS > VISUALS, chosen through the menu's own handler,
// remembered in settings.cfg [hud], the star's rest following the card, and the danger warnings staying when the instruments are hidden.
public partial class RaceScene
{
    IEnumerable VwHudToggles()
    {
        foreach (var x in VwWaitReady()) yield return x;
        foreach (var x in VrUntil(() => _state == State.Racing && _lapClock > 3f, 60 * 40, "the race is running")) yield return x;
        IEnumerable Settle() { for (int i = 0; i < 8; i++) yield return null; }
        string Cfg(string key) { var c = new ConfigFile(); c.Load(_records.Dir + "settings.cfg"); return c.GetValue("hud", key, "unset").ToString(); }
        void Choose(VisualRowId id) { SyncVisualHud(); int i = Array.IndexOf(_hud.VisualRowIds, id); VwCheck(i >= 0, $"row {id} is in the list"); if (i >= 0) ChooseVisual(i); }
        VwCheck(_hud.CarInstrumentsOn && _hud.LapInfoOn && _hud.NormsCardOn, "all three on by default");
        SyncVisualHud();
        VwCheck(_hud.VisualRowIds.Contains(VisualRowId.NormsCard), "the Socialist Rally lists NORMS INFO");
        foreach (var x in VrShot("hudtoggles-1-all-on")) yield return x;
        // the owner's row order, and every row's hit test lands on its own row
        var want = new List<VisualRowId> { VisualRowId.Comments, VisualRowId.LapInfo, VisualRowId.NormsCard, VisualRowId.Map, VisualRowId.CarInstruments };
        if (_hud.GameplayCameraName != null) want.Add(VisualRowId.Camera);
        want.AddRange(new[] { VisualRowId.Rendering, VisualRowId.Edges, VisualRowId.Softening, VisualRowId.Back });
        VwCheck(_hud.VisualRowIds.SequenceEqual(want), "VISUALS order: " + string.Join(", ", _hud.VisualRowIds));
        // the real way in: pause, SETTINGS, VISUALS (own-viewport shots; the pad shot shows the pad's hints, not a physical pad)
        foreach (var x in VrTap(Key.Escape)) yield return x;
        for (int i = 0; i < 12; i++) yield return null;
        for (int i = 0; i < 12 && _hud.PauseSelection != PauseRowIndex(PauseRowId.Settings); i++) foreach (var x in VrTap(Key.Down)) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 8; i++) yield return null;
        for (int i = 0; i < 12 && _hud.SettingsRowIds[_hud.SettingsSelection] != SettingsRowId.Visuals; i++) foreach (var x in VrTap(Key.Down)) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 8; i++) yield return null;
        VwCheck(_hud.VisualOpen, "VISUALS is open");
        var vr = _hud.VisualRect; bool hitOk = true;
        for (int i = 0; i < _hud.VisualRowIds.Length; i++) hitOk &= _hud.VisualRowAt(new Vector2(vr.Position.X + vr.Size.X / 2, vr.Position.Y + 36 + i * 16 + 8)) == i;
        VwCheck(hitOk, "each row's hit test lands on its own row");
        foreach (var x in VrShot("visuals-order-keyboard")) yield return x;
        _hud.Pad = true; for (int i = 0; i < 6; i++) yield return null;
        foreach (var x in VrShot("visuals-order-pad-hints")) yield return x;
        _hud.Pad = false;
        VwCheck(_hud.VisualRowIds[0] == VisualRowId.Comments && _hud.VisualRowIds[_hud.VisualRowIds.Length - 1] == VisualRowId.Back, "COMMENTS first, BACK last");
        foreach (var x in VrTap(Key.Escape)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        foreach (var x in VrTap(Key.Escape)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        foreach (var x in VrTap(Key.Escape)) yield return x;
        for (int i = 0; i < 8; i++) yield return null;
        VwCheck(_state == State.Racing, "back in the race");
        // LAP INFO with norms: the NORM row leads, laps left / TIME / BEST / LAST follow, the extra rows come below it
        foreach (var x in VrShot("lapinfo-1-normal")) yield return x;
        // the long case: norm 10/10 on its last lap, rocket fuel, a climate allowance and wrecks (every extra row), with the panel and its toggle
        var keepClimate = _norms.Climate; var keepFuel = _rallyFuel; int keepWrecks = _wrecks;
        _norms.Restore(9, 1, 40); _norms.Climate = () => 12f; _rallyFuel = FuelGrade.Rocket; _wrecks = 2;
        for (int i = 0; i < 20; i++) yield return null;
        VwCheck(_hud.PlanLaps > 0 && _hud.PlanLastLap && _hud.PlanClimate > 0 && _hud.PlanFuel != "" && _hud.Wrecks == 2, $"the long case is up (last lap {_hud.PlanLastLap}, climate {_hud.PlanClimate}, fuel '{_hud.PlanFuel}', wrecks {_hud.Wrecks})");
        foreach (var x in VrShot("lapinfo-2-long-norm10-climate")) yield return x;
        Choose(VisualRowId.LapInfo); for (int i = 0; i < 6; i++) yield return null;
        foreach (var x in VrShot("lapinfo-3-long-hidden")) yield return x;
        Choose(VisualRowId.LapInfo); for (int i = 0; i < 6; i++) yield return null;
        _norms.Restore(0, 5, 0); _norms.Climate = keepClimate; _rallyFuel = keepFuel; _wrecks = keepWrecks;
        for (int i = 0; i < 10; i++) yield return null;
        float starOn = _hud.MedalCornerTarget(GetViewport().GetVisibleRect().Size);

        Choose(VisualRowId.CarInstruments); foreach (var x in Settle()) yield return x;
        VwCheck(!_hud.CarInstrumentsOn && _hud.LapInfoOn && _hud.NormsCardOn && Cfg("car") == "false", "CAR INSTRUMENTS off alone, remembered");
        foreach (var x in VrShot("hudtoggles-2-no-instruments")) yield return x;
        // a danger still reaches the driver
        float engine = _health.Health;
        for (int i = 0; i < 20; i++) { _health.Set(0.2f); yield return null; }
        foreach (var x in VrShot("hudtoggles-2b-no-instruments-danger-a")) yield return x;
        for (int i = 0; i < 20; i++) { _health.Set(0.2f); yield return null; }      // (the warnings blink, a third of a second each way)
        foreach (var x in VrShot("hudtoggles-2c-no-instruments-danger-b")) yield return x;
        _health.Set(engine);
        Choose(VisualRowId.CarInstruments);

        Choose(VisualRowId.LapInfo); foreach (var x in Settle()) yield return x;
        VwCheck(_hud.CarInstrumentsOn && !_hud.LapInfoOn && _hud.NormsCardOn && Cfg("lap") == "false", "LAP INFO off alone, remembered");
        foreach (var x in VrShot("hudtoggles-3-no-lap-info")) yield return x;
        Choose(VisualRowId.LapInfo);

        Choose(VisualRowId.NormsCard); foreach (var x in Settle()) yield return x;
        VwCheck(_hud.CarInstrumentsOn && _hud.LapInfoOn && !_hud.NormsCardOn && !_hud.NormCardShown && Cfg("norms") == "false", "NORMS INFO off alone, remembered");
        float starOff = _hud.MedalCornerTarget(GetViewport().GetVisibleRect().Size);
        VwCheck(starOff == 33f && starOff < starOn, $"hidden card: the star rests at the old place ({starOff}, card on {starOn})");
        foreach (var x in VrShot("hudtoggles-4-no-norms-card")) yield return x;
        _hud.ResetMedalCelebration(); _hud.CelebrateMedal(NormPlan.Medal.Bronze, "");
        for (int i = 0; i < 60 * 4; i++) yield return null;
        foreach (var x in VrShot("hudtoggles-4b-star-without-card")) yield return x;
        _hud.ResetMedalCelebration();
        Choose(VisualRowId.NormsCard);
        VwCheck(_hud.NormCardShown && Cfg("norms") == "true", "NORMS INFO back on");

        // the rows are only where they apply
        var keep = _run; _run = RunMode.Training; SyncVisualHud();
        VwCheck(!_hud.VisualRowIds.Contains(VisualRowId.NormsCard) && _hud.VisualRowIds.Contains(VisualRowId.CarInstruments), "Stakhanov laps: no NORMS INFO row");
        _run = keep; SyncVisualHud();

        // all three off at once; then the pause menu, the minimap and the replay-free HUD still draw
        Choose(VisualRowId.CarInstruments); Choose(VisualRowId.LapInfo); Choose(VisualRowId.NormsCard); foreach (var x in Settle()) yield return x;
        foreach (var x in VrShot("hudtoggles-5-all-off")) yield return x;
        Choose(VisualRowId.CarInstruments); Choose(VisualRowId.LapInfo); Choose(VisualRowId.NormsCard); foreach (var x in Settle()) yield return x;
        VwCheck(_hud.CarInstrumentsOn && _hud.LapInfoOn && _hud.NormsCardOn, "everything back on (the player's settings.cfg ends as it began)");
    }
}
