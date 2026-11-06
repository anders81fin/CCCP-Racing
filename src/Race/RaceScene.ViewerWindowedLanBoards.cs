using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CCCPRacing.Net;
using CCCPRacing.Replay;
using Godot;

namespace CCCPRacing.Race;
public partial class RaceScene
{
    /// <summary>
    /// --viewer-windowed=lan-board-import (isolated --data-dir): a comrade's real lap of this track (its own archive in a temporary folder)
    /// comes in through SHARE BOARDS' production store (merge, verified payload, staging, commit), survives a reload from disk, and opens
    /// from the real leaderboard with ENTER in the existing viewer, byte for byte. Nothing new is added to the viewer.
    /// </summary>
    IEnumerable VwLanBoardImport()
    {
        foreach (var x in VwWaitReady()) yield return x;
        var state = _state; var run = _run;
        _state = State.Racing; _run = RunMode.Rally; _autodrive = false;
        _rec.Clear(); TapeBegin();
        Vector3 start = _car.Pos;
        for (int i = 0; i <= 120; i++)
        {
            float t = i / 30f; _car.Pos = start + Vector3.Forward * (i * .12f);
            _rec.Record(t + .00001f, _car, 0, 0, 0, 400, damage: _carVis.Damage);
            if (_tapeOn) _tapeRec.Record(t);
        }
        const float lapTime = 4.2f;
        var copy = _rec.CopyWithTime(lapTime);
        var cap = LapCapture.FromGhostCopy(copy, _tapeOn ? _tapeRec : null, _track.Id, 0, "rally", LapKind.Normal, true, -1, "comrade-profile", _records.Signature);
        _rec.Clear(); _state = state; _run = run;
        string comradeDir = Path.Combine(Path.GetTempPath(), "cccp-vw-comrade-" + Guid.NewGuid().ToString("N"));
        var comrade = new LapArchive(Path.Combine(comradeDir, "leaderboard-laps"));
        var sub = comrade.Submit(cap);
        var keep = comrade.Retain(sub.Id, "COMRADE");
        foreach (var x in VrUntil(() => keep.IsCompleted, 1800, "comrade retain")) yield return x;
        VwCheck(sub.Accepted && keep.Result.Ok, "the comrade's lap is a real retained leaderboard recording (ghost + world tape)");
        var info = comrade.List().Single(i => i.Id == sub.Id);
        string hash = comrade.Open(sub.Id).Sha256();

        var offer = new LanBoardManifest { BuiltTrack = _track.Id, BuiltSignature = _records.Signature };
        offer.Tracks.Add(new LanBoardTrack(_track.Id, _records.Signature, new List<(int, IReadOnlyList<LanBoardRow>)>
            { (0, new List<LanBoardRow> { new("COMRADE", lapTime, "2026-11-06", 0, false, false, false, sub.Id, info.Meta.GhostFingerprint, (int)(info.SizeBytes + LanBoardPayload.MaxMeta + 512)) }) }));
        var offerBytes = offer.ToBytes();
        var store = new LanBoardStore(_records.Dir, _track.Id, _records.Signature, set => _boards[set], () => _otherBoards.Clear(), LeaderboardLapArchive);
        var res = store.Merge(LanBoardManifest.Read(offerBytes), 2, new Dictionary<string, string> { [_track.Id] = _records.Signature });
        VwCheck(res.RowsAdded == 1 && res.Wants.Count == 1 && _boards[0].Entries[0].Name == "COMRADE" && _boards[0].Entries[0].RecordingId == null,
            "the comrade's row is merged into the live board, first without a recording link");
        var giver = new LanBoardStore(comradeDir, null, null, null, null, comrade);
        var read = giver.ReadPayload(sub.Id);
        foreach (var x in VrUntil(() => read.IsCompleted, 1800, "payload")) yield return x;
        var stage = store.Stage(res.Wants[0], read.Result);
        foreach (var x in VrUntil(() => stage.IsCompleted, 1800, "stage")) yield return x;
        string err = store.Commit(stage.Result);
        string local = stage.Result.LocalId;
        VwCheck(err == null && _boards[0].Entries[0].RecordingId == local, "the verified recording is committed and linked to the row (" + (err ?? local) + ")");
        VwCheck(Leaderboard.Load(_boards[0].Path).Entries[0].RecordingId == local
            && new LapArchive(WorldTapeFile.Real(_records.Dir + "leaderboard-laps")).Open(local)?.Sha256() == hash,
            "after a reload from disk the row and the imported recording are the comrade's exact source");
        _boards[0].ReplaceEntries(Leaderboard.Load(_boards[0].Path).Entries);      // (as a scene reload would read it)
        EnterBoard();
        for (int i = 0; i < 4; i++) yield return null;
        foreach (var x in VrShot("lan-board-import-board")) yield return x;
        _boardReplaySelected = 0;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        foreach (var x in VrUntil(() => _replayViewer?.State == ReplayViewerState.Playing, 7200, "viewer of the imported row")) yield return x;
        var session = _replayViewer;
        VwCheck(session != null && session.Scene.ViewerSource.Lap.Sha256() == hash, "ENTER on the imported row opens the comrade's exact recording in the existing viewer");
        foreach (var x in VrShot("lan-board-import-viewer")) yield return x;
        session?.Close();
        foreach (var x in VrUntil(() => _replayViewer == null, 1800, "return to board")) yield return x;
        VwCheck(_showBoard && _boardReplaySelected == 0, "the viewer returns to the same board row");
        try { Directory.Delete(comradeDir, true); } catch { }
    }
}
