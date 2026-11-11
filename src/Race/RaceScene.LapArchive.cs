using System.Diagnostics;
using System.IO;
using CCCPRacing.Replay;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// The player's automatic lap archive (docs/player-replay-design.md): every completed normal, record or void lap of a single-player Rally or
/// Stakhanov run is frozen at the finish line and handed to the archive's own worker. It never uses the record slots or their saves.
/// </summary>
public partial class RaceScene
{
    /// <summary>Single-player rally/Stakhanov racing only; not the school, LAN, trailer, tour or preview runs, and not a run that keeps no records (--no-record, the instruction player).</summary>
    // Test flags (the player-facing archive has no command-line switch): --no-lap-archive turns it off for an A/B run, --lap-archive-retain-all keeps every
    // recent lap on disk at the scene's exit so a test can inspect the files.
    static readonly bool LapArchiveOff = System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--no-lap-archive") >= 0;
    static readonly bool LapArchiveRetainAll = System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--lap-archive-retain-all") >= 0;

    bool LapArchiveEligible => !LapArchiveOff && _state == State.Racing && _run is RunMode.Rally or RunMode.Training && !InSchool && !InLanRace
        && !_tourCapture && !_cinematicTour && !_droneTour && !_previewMovie && !NoRecords;

    /// <summary>The active personnel file's lap folder (retained laps live here); user:// or the test data dir, globalised.</summary>
    string LapArchiveDir => WorldTapeFile.Real(FileDir + "laps");

    readonly System.Collections.Generic.Dictionary<string, LapArchive> _sessionLapArchives = new();
    readonly System.Collections.Generic.Dictionary<string, int> _sessionLapOrder = new();
    int _sessionLapSequence;
    LapArchive ReplayArchiveFor(string id) => _replayOrigin == ReplayOrigin.PartyMeeting
        && id != null && _sessionLapArchives.TryGetValue(id, out var archive) ? archive : LapArchiveBox;

    LapArchive LapArchiveBox => LapArchive.For(LapArchiveDir);

    /// <summary>The lap that just ended, before the recorders are reset. Cheap on the main thread: a ghost copy and the recorder snapshot; the rest runs on the archive's worker.</summary>
    void LapArchiveCapture(float time, LapKind kind, bool ranked, bool fake)
    {
        _lastBoardRecordingId = null;
        if (fake || !LapArchiveEligible || _rec.Count < 2) return;
        var sw = Stopwatch.StartNew();
        var copy = _rec.CopyWithTime(time);
        var cap = LapCapture.FromGhostCopy(copy, _tapeOn ? _tapeRec : null, _track.Id, RecordSet, _run == RunMode.Rally ? "rally" : "stakhanov", kind, ranked,
            copy.HasDayClock ? copy.DayClockAt(0) : -1, _file.ToString(), _records.Signature);
        cap.Fuels = LapFuelsText();
        cap.LapNumber = System.Math.Max(1, _timer.LapsDone);
        bool board = ranked && !_autodrive && _board.Qualifies(time, _lapPeak);
        var archive = board ? LeaderboardLapArchive : LapArchiveBox;
        if (board) _boardLapArchiveUsed = archive; else _lapArchiveUsed = archive;
        var res = archive.Submit(cap);
        if (res.Accepted) { _sessionLapArchives[res.Id] = archive; _sessionLapOrder[res.Id] = ++_sessionLapSequence; }
        if (board && res.Accepted)
        {
            _lastBoardRecordingId = res.Id;
            _boardRetains.Add((res.Id, archive.Retain(res.Id, $"LAP {cap.LapNumber} {DriverName} {LapTimer.Format(time)}")));
        }
        if (board && !res.Accepted) _hud?.Show("LEADERBOARD RECORDING NOT SAVED", res.Reason.ToUpperInvariant(), 5f, Hud.Red);
        GD.Print($"lap archive: {_track.Id} set {RecordSet} {kind} {time:F2} s: {(res.Accepted ? "kept " + res.Id : "refused: " + res.Reason)}, main thread {sw.Elapsed.TotalMilliseconds:F2} ms, tape {(_tapeOn ? "yes" : "no")}");
    }

    /// <summary>Messages of the archive's worker (failures included) reach the log from the main thread.</summary>
    void LapArchiveTick()
    {
        LeaderboardReplayTick();
        if (_lapArchiveUsed == null) return;
        foreach (var m in _lapArchiveUsed.DrainMessages()) GD.Print("lap archive: " + m);
    }
    LapArchive _lapArchiveUsed;

    /// <summary>The scene leaves the tree: a retain or compression in flight gets a bounded time to finish.</summary>
    void LapArchiveExit()
    {
        if (_boardLapArchiveUsed != null && !_boardLapArchiveUsed.Flush(System.TimeSpan.FromSeconds(10)))
            GD.PushWarning("Leaderboard recording save still pending on exit");
        if (_lapArchiveUsed == null) return;
        if (LapArchiveRetainAll)
        {
            _lapArchiveUsed.Flush(System.TimeSpan.FromSeconds(30));      // (the tapes are compressed first)
            foreach (var info in _lapArchiveUsed.List())
                if (!info.Retained) { var r = _lapArchiveUsed.Retain(info.Id, "test").GetAwaiter().GetResult(); GD.Print($"lap archive: retain {info.Id}: {(r.Ok ? "ok" : "FAILED " + r.Message)}"); }
        }
        if (!_lapArchiveUsed.Flush(System.TimeSpan.FromSeconds(10))) GD.PushWarning("lap archive: the worker did not finish within 10 s of the scene's exit");
        foreach (var m in _lapArchiveUsed.DrainMessages()) GD.Print("lap archive: " + m);
    }
}
