using System;
using System.Collections;
using System.Linq;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

public partial class RaceScene
{
    IEnumerable VwStakhanovBoard()
    {
        foreach (var x in VwWaitReady()) yield return x;
        bool racing = false;
        foreach (var x in VwStakhanovGo(() => this, _vwDrv, r => racing = r)) yield return x;
        VwCheck(racing, "Stakhanov is running after the production clutch countdown");
        IEnumerable Settle() { for (int i = 0; i < 8; i++) yield return null; }
        bool Times(float best, float last, bool invalid = false) => _hud.BestIsSession && Math.Abs(_hud.SessionBestLap - best) < .001f && Math.Abs(_hud.LastLap - last) < .001f && _hud.LastLapVoid == invalid;
        VwCheck(Times(-1, -1), "a fresh Stakhanov session has no BEST or LAST");
        _boards[1].ReplaceEntries(Enumerable.Range(1, 10).Select(i => new Leaderboard.Entry("FAST PETROL", 30 + i, "2026-11-07", 100)));
        _timer.BestLap = 20f;
        _timer.CompleteLapForTest(70f);
        foreach (var x in Settle()) yield return x;
        VwCheck(Times(70, 70) && _timer.BestLap == 20, "PETROL session time is independent of the persistent record");
        _timer.CompleteLapForTest(75f);
        foreach (var x in Settle()) yield return x;
        VwCheck(Times(70, 75), "PETROL keeps its best and most recent lap separately");
        foreach (var x in VrTap(Key.Key1)) yield return x;
        foreach (var x in Settle()) yield return x;
        VwCheck(Times(-1, -1), "switching to unused DIESEL empties its session display");
        _timer.CompleteLapForTest(80f); // This lap began on petrol: highest-fuel policy.
        foreach (var x in Settle()) yield return x;
        VwCheck(Times(-1, -1), "a mixed PETROL/DIESEL lap is not credited to DIESEL");
        _timer.CompleteLapForTest(90f);
        foreach (var x in Settle()) yield return x;
        VwCheck(Times(90, 90), "a complete DIESEL lap belongs to DIESEL");
        var diesel = _boards[1].Entries.SingleOrDefault(e => e.Power == 50 && e.Time == 90);
        foreach (var x in VrUntil(() => _boardRetains.Count == 0, 60 * 20, "the fuel board's recording is retained")) yield return x;
        var kept = diesel?.RecordingId == null ? null : LeaderboardLapArchive.List().FirstOrDefault(e => e.Id == diesel.RecordingId);
        var reloaded = Leaderboard.Load(_board.Path, capacity: Leaderboard.StakhanovSize, fuelPartitions: true);
        VwCheck(diesel?.RecordingId != null && kept is { Retained: true, Openable: true } && reloaded.Entries.Any(e => e.Power == 50 && e.RecordingId == diesel.RecordingId),
            "a DIESEL lap slower than all ten PETROL results still gets its persistent board row and openable retained recording");
        _timer.Void(); _timer.CompleteLapForTest(60f);
        foreach (var x in Settle()) yield return x;
        VwCheck(Times(90, 60, true), "a void DIESEL lap updates LAST with VOID but cannot improve BEST");
        _timer.Unranked = true; _timer.CompleteLapForTest(55f); _timer.Unranked = false;
        foreach (var x in Settle()) yield return x;
        VwCheck(Times(90, 55), "an unranked DIESEL lap cannot improve BEST");
        foreach (var x in VrTap(Key.Key2)) yield return x;
        foreach (var x in Settle()) yield return x;
        VwCheck(Times(70, 80), "returning to PETROL restores its own BEST and LAST");
        // An isolated board fixture, rendered by the same production provider.
        _boards[1].ReplaceEntries(Enumerable.Range(1, 10).Select(i => new Leaderboard.Entry("DRIVER" + i, 30 + i, "2026-11-07", 100)));
        foreach (var x in Settle()) yield return x;
        VwCheck(_hud.StakhanovMiniBoard && _hud.StakhanovBoardRows.Count == 10 && _hud.StakhanovBoardRows[9].Time == 40, "the live PETROL mini board receives ranks 1 through 10");
        foreach (var x in VrShot("stakhanov-hud-petrol")) yield return x;
        foreach (var x in VrTap(Key.Escape)) yield return x;
        foreach (var x in Settle()) yield return x;
        VwCheck(_state == State.Paused && _hud.PauseRowIds.Contains(PauseRowId.Leaderboards), "PARTY MEETING offers LEADERBOARDS");
        for (int i = 0; i < 12 && _hud.PauseSelection != PauseRowIndex(PauseRowId.Leaderboards); i++)
            foreach (var x in VrTap(Key.Up)) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        foreach (var x in Settle()) yield return x;
        string track = _trackId;
        float lapClock = _lapClock;
        var camera = _gameplayView;
        bool map = _minimapOn;
        VwCheck(_hud.StakhanovBoardOpen && _hud.StakhanovBoardFuel == "PETROL" && _hud.StakhanovBoardRows.Count == 10, "the board opens at the driving fuel and current track");
        foreach (var x in VrShot("stakhanov-party-leaderboards")) yield return x;
        foreach (var x in VrTap(Key.Left)) yield return x;
        foreach (var x in VrTap(Key.W)) yield return x;
        foreach (var x in VrTap(Key.Key4)) yield return x;
        foreach (var x in VrTap(Key.M)) yield return x;
        foreach (var x in VrTap(Key.C)) yield return x;
        foreach (var x in VrTap(Key.R)) yield return x;
        foreach (var x in Settle()) yield return x;
        VwCheck(_hud.StakhanovBoardFuel == "DIESEL" && _hud.StakhanovBoardRows.Count == 0 && _power == 100 && _trackId == track && _state == State.Paused && _lapClock == lapClock,
            "browsing DIESEL is read-only: no car fuel, track, clock or pause change; gameplay shortcuts are consumed");
        VwCheck(_gameplayView == camera && _minimapOn == map, "the board consumes camera and map shortcuts without changing settings");
        foreach (var x in VrShot("stakhanov-party-diesel-empty")) yield return x;
        foreach (var x in VrPad(JoyButton.DpadRight)) yield return x;
        foreach (var x in Settle()) yield return x;
        VwCheck(_hud.StakhanovBoardFuel == "PETROL" && _power == 100, "controller browses the board without changing the car");
        foreach (var x in VrPad(JoyButton.B)) yield return x;
        foreach (var x in Settle()) yield return x;
        VwCheck(!_hud.StakhanovBoardOpen && _state == State.Paused, "B returns to PARTY MEETING and keeps the drive paused");
        foreach (var x in VrTap(Key.Enter)) yield return x;
        foreach (var x in Settle()) yield return x;
        VwCheck(_hud.StakhanovBoardOpen, "Enter reopens the selected LEADERBOARDS row");
        foreach (var x in VrTap(Key.Escape)) yield return x;
        foreach (var x in Settle()) yield return x;
        VwCheck(!_hud.StakhanovBoardOpen && _state == State.Paused, "Esc closes only the board, without resuming the drive");
        foreach (var x in VrTap(Key.Enter)) yield return x;
        foreach (var x in Settle()) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        foreach (var x in Settle()) yield return x;
        VwCheck(!_hud.StakhanovBoardOpen && _state == State.Paused, "Enter closes only the board, without reopening it or resuming");
        foreach (var x in VrTap(Key.Escape)) yield return x;
        foreach (var x in Settle()) yield return x;
        VwCheck(_state == State.Racing && Times(70, 80), "resume preserves the per-fuel session times");
        EnterCountdown();
        foreach (var x in Settle()) yield return x;
        VwCheck(Times(-1, -1) && _boards[1].Entries.Count == 10, "restart clears session times and retains the leaderboard");
    }
}
