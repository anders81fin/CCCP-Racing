using System;
using System.Collections;
using System.Linq;
using CCCPRacing.Replay;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

// Windowed scenario "watch-lap" (--viewer-windowed-only=watchlap): the owner's acceptance case of 2026-11-19, in the real game through the production paths: a record lap is driven (captured through LapArchiveCapture,
// the way a lap that reaches the line is), then a SLOWER ordinary lap; F3 and the Party Meeting's WATCH LAST LAP both open exactly the slower lap (its id, time and kind), never the record; closing the viewer keeps the car, the
// clocks, the norm and the attempts; the Party Meeting comes back on the WATCH LAST LAP row; the hint, the rows for 0/1/2 laps and a card's WATCH LAP are checked on the way.
public partial class RaceScene
{
    IEnumerable VwWatchLap()
    {
        foreach (var x in VwWaitReady()) yield return x;
        foreach (var x in VrUntil(() => _state == State.Racing && _lapClock > 3f, 60 * 60, "the race is running")) yield return x;
        VwCheck(_norms.Enabled && ReplayRowShown, "a single-player Socialist Rally with the lap archive");

        // ---- 0 laps: the Party Meeting has no replay row, and F3 does nothing
        VwCheck(_sessionLapOrder.Count == 0 && _lastLapId == null, "no lap yet in this run");
        VwCheck(!PauseRowsNow.Contains(PauseRowId.WatchLastLap) && !PauseRowsNow.Contains(PauseRowId.BrowseLaps), "0 laps: no WATCH LAST LAP and no BROWSE LAPS row");
        foreach (var x in VrTap(Key.F3)) yield return x;
        for (int i = 0; i < 20; i++) yield return null;
        VwCheck(ReplayViewer == null && _watchTask == null, "0 laps: F3 opens nothing");

        // ---- 1 lap: a record lap, through the production capture
        LapArchiveCapture(41.20f, LapKind.Record, true, false);
        string recordId = _lastLapId;
        for (int i = 0; i < 90; i++) yield return null;      // (the worker prepares the tape)
        VwCheck(recordId != null && _sessionLapOrder.Count == 1 && _sessionLapArchives.ContainsKey(recordId), "the record lap is the run's first lap, in its archive");
        VwCheck(PauseRowsNow.SequenceEqual(PauseRowList(true, 1, StakhanovBoardAvailable)) && PauseRowsNow.Contains(PauseRowId.WatchLastLap) && !PauseRowsNow.Contains(PauseRowId.BrowseLaps), "1 lap: WATCH LAST LAP, no BROWSE LAPS");
        VwCheck(_hud.WatchHint != null && _hud.WatchHint.StartsWith("F3  WATCH LAP"), $"the hint after the lap: '{_hud.WatchHint}'");
        foreach (var x in VrShot("watchlap-hint")) yield return x;

        // ---- 2 laps: then a slower ordinary lap
        LapArchiveCapture(47.55f, LapKind.Normal, true, false);
        string slowId = _lastLapId;
        for (int i = 0; i < 90; i++) yield return null;
        VwCheck(slowId != null && slowId != recordId && _sessionLapOrder.Count == 2, "the slower lap is the run's second lap, a different id");
        VwCheck(PauseRowsNow.Contains(PauseRowId.WatchLastLap) && PauseRowsNow.Contains(PauseRowId.BrowseLaps) && PauseRowsNow[0] == PauseRowId.Continue, "2 laps: CONTINUE first, then WATCH LAST LAP and BROWSE LAPS");

        // ---- F3 opens the slower lap, never the record
        var normsBefore = (_norms.Step, _norms.Left, _norms.Used); float lapClock = _lapClock, clock = _clock;
        foreach (var x in VrTap(Key.F3)) yield return x;
        foreach (var x in VrUntil(() => ReplayViewer is { State: ReplayViewerState.Playing }, 60 * 30, "the viewer plays")) yield return x;
        var v = ReplayViewer;
        VwCheck(v != null && v.LapId == slowId && Math.Abs(v.LapSeconds - 47.55f) < 0.01f && v.LapKindOf == LapKind.Normal, $"F3 opened the slower ordinary lap ({v?.LapId} {v?.LapSeconds:F2} s {v?.LapKindOf}), not the record {recordId}");
        VwCheck(v != null && v.Scene != null && v.Scene.ViewerCamera == ViewerCameraMode.Auto, "in the AUTO camera");
        var carPos = _car.Pos; float lapAtOpen = _lapClock;
        for (int i = 0; i < 120; i++) yield return null;
        VwCheck(_car.Pos.DistanceTo(carPos) < 1e-4f && Math.Abs(_lapClock - lapAtOpen) < 1e-4f, "while the viewer is up the live car and the lap clock stand still");
        foreach (var x in VrShot("watchlap-viewer")) yield return x;
        foreach (var x in VrTap(Key.Escape)) yield return x;
        foreach (var x in VrUntil(() => ReplayViewer == null && _state == State.Racing, 60 * 20, "the viewer closed, back in the drive")) yield return x;
        VwCheck(_state == State.Racing && (_norms.Step, _norms.Left, _norms.Used) == normsBefore && _lapClock >= lapAtOpen && _lapClock - lapAtOpen < 1.5f, $"closing gives the drive back: racing, the norm and the attempts unchanged {normsBefore} -> {(_norms.Step, _norms.Left, _norms.Used)}, lap clock {lapAtOpen:F2} -> {_lapClock:F2}");

        // ---- WATCH LAST LAP in the Party Meeting: the same lap, back on the same row
        foreach (var x in VrTap(Key.Escape)) yield return x;
        VwCheck(_state == State.Paused && _hud.PauseSelection == 0 && _hud.PauseRowIds[0] == PauseRowId.Continue, "the Party Meeting opens on CONTINUE, the top row");
        int watchRow = PauseRowIndex(PauseRowId.WatchLastLap);
        foreach (var x in VrToRow(watchRow)) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        foreach (var x in VrUntil(() => ReplayViewer is { State: ReplayViewerState.Playing }, 60 * 30, "the viewer plays")) yield return x;
        v = ReplayViewer;
        VwCheck(v != null && v.LapId == slowId && Math.Abs(v.LapSeconds - 47.55f) < 0.01f && v.LapKindOf == LapKind.Normal, $"WATCH LAST LAP opened the slower lap too ({v?.LapId} {v?.LapSeconds:F2} s)");
        foreach (var x in VrTap(Key.Escape)) yield return x;
        foreach (var x in VrUntil(() => ReplayViewer == null && _state == State.Paused, 60 * 20, "the viewer closed, back in the meeting")) yield return x;
        VwCheck(_state == State.Paused && _hud.PauseSelection == watchRow && (_norms.Step, _norms.Left, _norms.Used) == normsBefore, $"back in the Party Meeting on the WATCH LAST LAP row ({_hud.PauseSelection} == {watchRow}), the norm and the attempts unchanged");
        // BROWSE LAPS lists both laps; the record is not what WATCH LAST LAP follows
        foreach (var x in VrToRow(PauseRowIndex(PauseRowId.BrowseLaps))) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 60; i++) yield return null;
        VwCheck(_hud.ReplayListOpen && _replayInfos.Count == 2, $"BROWSE LAPS lists the run's two laps ({_replayInfos.Count})");
        foreach (var x in VrTap(Key.Escape)) yield return x;
        for (int i = 0; i < 10; i++) yield return null;

        // ---- a stop card offers WATCH LAP, CONTINUE staying the default
        foreach (var x in VrToRow(0)) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;      // CONTINUE
        for (int i = 0; i < 10; i++) yield return null;
        VwCheck(_state == State.Racing, "CONTINUE (row 0) resumes the drive");
        _stopQueue.Clear();
        bool keepAuto = _autodrive; _autodrive = false;      // (the test drive closes cards by itself: this one is held for the check)
        _stopCard = new StopCard("test_watch", "A CARD", new[] { "THE RACE IS STOPPED UNDER IT" }, Hud.Gold, "", "");
        _cardAge = 5f; _state = State.Paused; _hud.Mode = HudMode.Card; _hud.StopCard = (_stopCard.Title, _stopCard.Lines, _stopCard.Color); _hud.StopCardReady = true;
        for (int i = 0; i < 10; i++) yield return null;
        VwCheck(_hud.StopCardWatch != null && _hud.StopCardWatch.StartsWith("F3  WATCH LAP"), $"the stop card offers '{_hud.StopCardWatch}'");
        foreach (var x in VrShot("watchlap-card")) yield return x;
        foreach (var x in VrTap(Key.F3)) yield return x;
        foreach (var x in VrUntil(() => ReplayViewer is { State: ReplayViewerState.Playing }, 60 * 30, "the viewer plays from the card")) yield return x;
        VwCheck(ReplayViewer?.LapId == slowId && _stopCard != null, "F3 on the card opens the lap and the card is still there");
        foreach (var x in VrTap(Key.Escape)) yield return x;
        foreach (var x in VrUntil(() => ReplayViewer == null, 60 * 20, "the viewer closed")) yield return x;
        VwCheck(_stopCard != null && _state == State.Paused && _hud.Mode == HudMode.Card, "back on the very same card");
        foreach (var x in VrTap(Key.Enter)) yield return x;      // any other key: CONTINUE
        for (int i = 0; i < 10; i++) yield return null;
        VwCheck(_stopCard == null && _state == State.Racing, "any other key closes the card and continues");
        _autodrive = keepAuto;
    }
}
