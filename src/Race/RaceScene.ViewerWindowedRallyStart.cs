using System;
using System.Collections;
using System.Linq;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

// Windowed scenario "rally-start" (--viewer-windowed-only=rallystart): the real title menu to a Socialist Rally, fresh and resumed (a saved rally on a temporary profile): no NORMS AND MEDALS table before the countdown, the
// countdown then the race follow, the saved rally's progress is restored and its save consumed; the table stays in the Party Meeting by hand.
public partial class RaceScene
{
    IEnumerable VwRallyStart(bool resume)
    {
        foreach (var x in VwWaitReady()) yield return x;
        for (int i = 0; i < 600 && !(_state == State.Title && _card == TitleCard.None && !_showLog && !_askName); i++) yield return null;
        for (int i = 0; i < 90; i++) yield return null;
        VwCheck(_state == State.Title && _menuParent == null, "the title's top row is up");
        string dir = FileDir; string track = _trackId;
        if (resume)
        {
            var point = new RallyResume.Checkpoint(3, 2, 9, 1, 123.5f, 9, 6);
            VwCheck(RallyResume.Save(dir, track, ResumeSignature, point, FuelGrade.Petrol) == Error.Ok && RallyResume.Exists(dir, track), "a saved rally (step 3, 2 laps left) is on the temporary profile");
        }
        for (int i = 0; i < 6 && _menu != MenuItem.Single; i++) foreach (var x in VrTap(Key.Left)) yield return x;
        VwCheck(_menu == MenuItem.Single, "the cursor is on SINGLE PLAYER");
        foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_menuParent == MenuItem.Single && _menu == MenuItem.Race, "the SOCIALIST RALLY is selected");
        foreach (var x in VrTap(Key.Enter)) yield return x;      // the track card
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_card == TitleCard.Track, "the track card");
        foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_card == (resume ? TitleCard.RallyResume : TitleCard.RallyFuel), $"the next card is {(resume ? "SAVED RALLY" : "FUEL")} (and no time of day card at all) ({_card})");
        foreach (var x in VrTap(Key.Enter)) yield return x;      // CONTINUE (resume) / the chosen fuel
        bool modal = false, countdown = false, racing = false; int frames = 0;
        int readyFor = 0, controlOff = -1; bool clutchHeld = false;
        for (; frames < 60 * 90 && !racing; frames++)
        {
            yield return null;
            // the loading card waits for a key when the track is ready (TRACK READY - PRESS ANY KEY): the script presses it itself, marked as its own input (held for a few frames), so nobody has to help it along
            if (TrackLoadingScreen.Active is { IsReady: true } && ++readyFor == 30)
            {
                _vwDrv.SetControl(true); controlOff = frames + 5;
                Input.ParseInputEvent(VwKey(Key.Enter, true, 0)); Input.ParseInputEvent(VwKey(Key.Enter, false, 0));
                VwLog("press-any-key", ("frame", frames));
            }
            // the start waits for the driver's clutch (HOLD CLUTCH - SHIFT: the countdown does not run on its own for a human driver): the script holds it from the countdown on and lets go at GO, like a player, again marked as its own input
            if (_state == State.Countdown && !clutchHeld)
            {
                clutchHeld = true; _vwDrv.SetControl(true); controlOff = frames + 5;
                Input.ParseInputEvent(VwKey(Key.Shift, true, 0));
                VwLog("clutch-held", ("frame", frames));
            }
            else if (_state == State.Racing && clutchHeld)
            {
                clutchHeld = false; _vwDrv.SetControl(true); controlOff = frames + 5;
                Input.ParseInputEvent(VwKey(Key.Shift, false, 0));
            }
            if (controlOff >= 0 && frames >= controlOff) { _vwDrv.SetControl(false); controlOff = -1; }
            modal |= _hud.PauseNormsOpen;
            if (_state == State.Countdown) countdown = true;
            if (_state == State.Racing && countdown) racing = true;
        }
        VwLog("rally-start", ("resume", resume), ("frames", frames), ("modal_seen", modal), ("countdown_seen", countdown), ("racing", racing), ("norm_step", _norms.Step), ("norm_left", _norms.Left), ("used", _norms.Used));
        VwCheck(!modal, "no NORMS AND MEDALS table was shown before the countdown");
        VwCheck(countdown && racing, $"the countdown ran and the race started ({frames} frames)");
        if (resume)
        {
            VwCheck(_norms.Step == 3 && _norms.Left == 2 && _norms.Used == 9, $"the saved rally's progress is restored (step {_norms.Step}, {_norms.Left} laps left, {_norms.Used} used)");
            VwCheck(!RallyResume.Exists(dir, track), "the saved rally was consumed at the start (as before)");
        }
        else VwCheck(_norms.Step == 0 && _norms.Left == NormPlan.WindowLaps, "a fresh rally starts at norm 1 with its five laps");
        for (int i = 0; i < 60; i++) yield return null;
        foreach (var x in VrShot(resume ? "rallystart-resumed-race" : "rallystart-fresh-race")) yield return x;
        // the table stays in the Party Meeting by hand: NORMS AND MEDALS opens, ESC closes it and the race is still paused
        foreach (var x in VrTap(Key.Escape)) yield return x;
        for (int i = 0; i < 12; i++) yield return null;
        VwCheck(_state == State.Paused && _hud.PauseRowIds.Contains(PauseRowId.Norms), "the Party Meeting has NORMS AND MEDALS");
        for (int i = 0; i < 12 && _hud.PauseSelection != PauseRowIndex(PauseRowId.Norms); i++) foreach (var x in VrTap(Key.Up)) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 12; i++) yield return null;
        VwCheck(_hud.PauseNormsOpen && _state == State.Paused, "the norms table opens by hand");
        foreach (var x in VrShot(resume ? "rallystart-resumed-table" : "rallystart-fresh-table")) yield return x;
        foreach (var x in VrTap(Key.Escape)) yield return x;
        for (int i = 0; i < 8; i++) yield return null;
        VwCheck(!_hud.PauseNormsOpen && _state == State.Paused, "ESC closes the table back to the Party Meeting, the race still paused");
        foreach (var x in VrTap(Key.Escape)) yield return x;
        for (int i = 0; i < 8; i++) yield return null;
        VwCheck(_state == State.Racing, "ESC in the Party Meeting continues the race");
        // where the "foreign" input counter came from: the events that arrived that were not marked as this script's (kind and count)
        VwLog("foreign-breakdown", ("events", string.Join(" ", _vwDrv.Arrivals.Where(a => !a.Sentinel).GroupBy(a => a.What).OrderByDescending(g => g.Count()).Take(8).Select(g => $"{g.Key}x{g.Count()}"))), ("frames_span", _vwDrv.Arrivals.Count == 0 ? "-" : $"{_vwDrv.Arrivals.Min(a => (long)a.Frame)}-{_vwDrv.Arrivals.Max(a => (long)a.Frame)}"));
    }
}
