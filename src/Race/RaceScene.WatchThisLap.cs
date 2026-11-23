using System.Linq;
using CCCPRacing.Replay;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// WATCH CURRENT LAP (the owner's order, 2026-11-23): the Party Meeting's row for the lap that is being driven. The recording so far goes into the lap archive as a void lap (never a record, never the
/// board's) and opens in the replay viewer like any other lap. There is only ever one such lap: a new look replaces the old one, and the lap's real ending (or the run's) removes it. WATCH LAST LAP stays
/// the last lap that ended; this one is not noted as that.
/// </summary>
public partial class RaceScene
{
    string _partialLapId;

    /// <summary>The row shows in a Party Meeting of a single-player rally or Stakhanov drive, once the lap has some length to show.</summary>
    bool ThisLapAvailable => ReplayRowShown && _state == State.Paused && _lapClock > 2f && _rec.Count >= 2 && _watchTask == null && _replayViewer == null;

    /// <summary>The previous look at a lap in progress is out of the archive and the lists.</summary>
    void DropPartialLap()
    {
        if (_partialLapId == null) return;
        string id = _partialLapId; _partialLapId = null;
        if (_sessionLapArchives.TryGetValue(id, out var archive))
        {
            // a lap the player chose to KEEP in the viewer stays (it is in the recordings list); only an unkept one is thrown away
            bool kept = archive.List().Any(i => i.Id == id && (i.Retained || i.Meta.Retained));
            if (!kept) _ = archive.Delete(id);
            _sessionLapArchives.Remove(id);
        }
        _sessionLapOrder.Remove(id);
    }

    void WatchThisLap()
    {
        if (!ThisLapAvailable) return;
        DropPartialLap();
        LapArchiveCapture(_lapClock, LapKind.Void, false, false, _timer.LapsDone + 1, partial: true);
        if (_partialLapId == null) { WatchNotice("CANNOT BE SAVED", true); return; }
        WatchLapId(_partialLapId, _timer.LapsDone + 1, WatchFrom.PartyMeeting);
    }
}
