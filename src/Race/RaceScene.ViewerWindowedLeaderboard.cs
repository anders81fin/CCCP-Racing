using System;
using System.Collections;
using System.Linq;
using CCCPRacing.Replay;
using Godot;

namespace CCCPRacing.Race;
public partial class RaceScene
{
    IEnumerable VwLeaderboardReplay()
    {
        foreach (var x in VwWaitReady()) yield return x;
        var state = _state; var run = _run; bool noRecord = _noRecord;
        _state = State.Racing; _run = RunMode.Rally; _noRecord = false; _autodrive = false;
        var board = _boards[RecordSet];
        VwCheck(board.Entries.Count == 0, "isolated profile starts with an empty board");
        _rec.Clear(); TapeBegin();
        Vector3 start = _car.Pos;
        for (int i = 0; i <= 120; i++)
        {
            float t = i / 30f; _car.Pos = start + Vector3.Forward * (i * .12f);
            _rec.Record(t + .00001f, _car, 0, 0, 0, 400, damage: _carVis.Damage);
            if (_tapeOn) _tapeRec.Record(t);
        }
        // An earlier run on the same track must never leak into this run's meeting.
        LapArchiveCapture(5, LapKind.Normal, true, false);
        string previousRunId = _lastBoardRecordingId;
        foreach (var x in VrUntil(() => _boardRetains.Count == 0, 1800, "previous run save")) yield return x;
        _sessionLapArchives.Clear();
        LapArchiveCapture(6, LapKind.Normal, true, false);
        foreach (var x in VrUntil(() => _boardRetains.Count == 0, 1800, "first session lap save")) yield return x;
        LapArchiveCapture(7, LapKind.Normal, true, false);
        foreach (var x in VrUntil(() => _boardRetains.Count == 0, 1800, "second session lap save")) yield return x;
        LapArchiveCapture(4, LapKind.Normal, true, false);
        string id = _lastBoardRecordingId;
        BoardLap(4, false);
        VwCheck(id != null && board.Entries[0].RecordingId == id, "production finish capture binds the exact lap to inserted result");
        _rec.Clear(); _state = state; _run = run; _noRecord = noRecord;
        foreach (var x in VrUntil(() => _boardRetains.Count == 0, 1800, "automatic save")) yield return x;
        var saved = LeaderboardLapArchive.List().Single(i => i.Id == id);
        VwCheck(saved.Retained && saved.Openable && !LapArchiveBox.List().Any(i => i.Id == id), "automatic recording is durable and excluded from normal replay lists");
        StartReplayListing();
        foreach (var x in VrUntil(() => { PollReplayListing(); return _replayListTask == null; }, 600, "session-only listing")) yield return x;
        VwCheck(_replayInfos.Count == 3 && _replayInfos.Any(i => i.Id == id) && !_replayInfos.Any(i => i.Id == previousRunId),
            "Party Meeting lists only this run, including its leaderboard lap, never a previous run on the same track");
        var source = LeaderboardLapArchive.Open(id); string hash = source.Sha256();
        VwCheck(Leaderboard.Load(board.Path).Entries[0].RecordingId == id, "row-to-recording association survives disk reload");
        var sessionEntries = _sessionLapArchives.ToArray();
        var hudMode = _hud.Mode;
        _state = State.Paused; _run = RunMode.Rally; _hud.Mode = UI.HudMode.Paused;
        OpenReplayList();
        foreach (var x in VrUntil(() => { PollReplayListing(); return !_replayLooking && _replayListTask == null; }, 600, "multiple-lap choice")) yield return x;
        VwCheck(_hud.ReplayListOpen && _replayInfos.Count == 3 && _replayViewer == null,
            "three session laps show the choice list without auto-opening a lap");
        CloseReplayList();
        _sessionLapArchives.Clear(); _sessionLapArchives[id] = LeaderboardLapArchive;
        OpenReplayList();
        foreach (var x in VrUntil(() => { PollReplayListing(); PollReplayOpen(); return _replayViewer?.State == ReplayViewerState.Playing; }, 7200, "single lap opens directly")) yield return x;
        VwCheck(_replayViewer?.Scene.ViewerSource.Lap.Sha256() == hash,
            "one session lap opens its exact recording without a confirmation selection");
        _replayViewer?.Close();
        foreach (var x in VrUntil(() => _replayViewer == null, 1800, "single-lap return")) yield return x;
        VwCheck(!_hud.ReplayListOpen && _state == State.Paused && _hud.Mode == UI.HudMode.Paused,
            "single-lap viewer returns directly to Party Meeting without resuming driving");
        foreach (var entry in sessionEntries) _sessionLapArchives[entry.Key] = entry.Value;
        _state = state; _run = run; _hud.Mode = hudMode;
        var originalArchive = LeaderboardLapArchive;
        UseFile(_file == 1 ? 2 : 1, touch: false);
        VwCheck(ReferenceEquals(originalArchive, LeaderboardLapArchive) && LeaderboardLapArchive.Open(id).Sha256() == hash,
            "shared leaderboard recording remains reachable after switching personnel file");
        board.Insert("LEGACY", 8); board.Save();
        EnterBoard();
        for (int i = 0; i < 4; i++) yield return null;
        foreach (var x in VrShot("leaderboard-replay")) yield return x;
        foreach (var x in VrPad(JoyButton.DpadDown)) yield return x;
        VwCheck(_boardReplaySelected == 1 && _padMode, "gamepad D-pad selects rows through the normal pad input path");
        foreach (var x in VrPad(JoyButton.DpadUp)) yield return x;
        foreach (var x in VrTap(Key.Down)) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        VwCheck(_showBoard && _replayViewer == null && BoardReplayStatus().Contains("NO RECORDING"), "legacy result visibly refuses replay without substituting a record ghost");
        foreach (var x in VrTap(Key.Up)) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        foreach (var x in VrUntil(() => _replayViewer?.State == ReplayViewerState.Playing, 7200, "leaderboard viewer")) yield return x;
        var session = _replayViewer;
        VwCheck(session != null && session.Scene.ViewerSource.Lap.Sha256() == hash, "ENTER opens the exact selected source pair");
        session?.Close();
        foreach (var x in VrUntil(() => _replayViewer == null, 1800, "return to board")) yield return x;
        VwCheck(_showBoard && _hud.Mode == UI.HudMode.Board && _boardReplaySelected == 0, "viewer exit restores the same leaderboard and selected result");
        VwCheck(LeaderboardLapArchive.Open(id).Sha256() == hash, "watching never changes original source bytes");
    }
}
