using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CCCPRacing.Replay;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// REPLAY in the Party Meeting (docs/work-orders/player-replay.md phase 3): the pause menu's row, the list of the archive's laps (recent and kept) and the hand-over to the
/// viewer session. The race stays suspended in State.Paused throughout; the viewer freezes the scene (ReplayViewerSession.Suspend) and gives it back exactly, and the player
/// returns to this list. Only CONTINUE in the menu resumes physics. The list is read and the chosen lap opened on a worker thread (managed code only): a first listing scans the
/// folder of kept laps, and opening waits for a lap's tape to be compressed.
/// </summary>
public partial class RaceScene
{
    /// <summary>The row exists in a single-player rally or Stakhanov Party Meeting only (the archive's own eligibility; not the school, LAN, a demonstration, a viewer).</summary>
    bool ReplayRowShown => !ViewerMode && InstructionTrack == null && _run is RunMode.Rally or RunMode.Training && !InSchool && !InLanRace && !_trailerCapture && !_cinematicTour && !_previewMovie;

    int PauseReplayRow => PauseRowIndex(PauseRowId.Replay);

    List<LapEntryInfo> _replayInfos = new();
    Task<List<LapEntryInfo>> _replayListTask;
    Task<(LapSource Src, string Reason)> _replayOpenTask;
    string _replayOpenId;
    int _replayKnownLaps = -1;      // for the menu row: the archive had this many laps at the last listing (-1: not looked yet)
    float _replayListAge;
    ulong _replayLastSync;
    bool _replayAutoSingle, _replayDirect;
    bool _replayLooking;      // the list shows its "looking" line until the first listing arrives

    /// <summary>Kept in step with the menu every frame: whether the row shows and whether it has any lap to open.</summary>
    void SyncPauseReplayRow()
    {
        _hud.PauseReplayShown = ReplayRowShown;
        if (!ReplayRowShown) return;
        ulong frame = Engine.GetProcessFrames();
        if (frame - _replayLastSync > 2) _replayListAge = 99f;      // (the menu has just come up again: look at the archive afresh)
        _replayLastSync = frame;
        _replayListAge += (float)GetProcessDeltaTime();
        if (_replayListTask == null && (_replayKnownLaps < 0 || _replayListAge > 1.5f)) StartReplayListing();      // (a lap finished since, a tape now ready: a cheap look at what the archive holds)
        PollReplayListing();
        _hud.PauseReplayAvailable = _replayKnownLaps > 0;
    }

    void StartReplayListing()
    {
        var archive = LapArchiveBox;
        bool office = _replayOrigin == ReplayOrigin.Office;
        var session = _sessionLapArchives.ToArray();
        _replayListTask = Task.Run(() => office ? archive.List().ToList() :
            session.Select(x => x.Value).Distinct().SelectMany(a => a.List())
                .Where(i => session.Any(x => x.Key == i.Id)).ToList());
        _replayListAge = 0;
    }

    void PollReplayListing()
    {
        if (_replayListTask is not { IsCompleted: true } t) return;
        _replayListTask = null;
        if (t.IsFaulted) { _replayKnownLaps = 0; _replayInfos = new(); GD.PushWarning("Replay: listing the laps failed: " + t.Exception?.GetBaseException().Message); }
        else
        {
            // Only laps captured in this run belong to the meeting, including its record laps.
            _replayInfos = t.Result.Where(i => _replayOrigin == ReplayOrigin.Office || _sessionLapArchives.ContainsKey(i.Id))
                .OrderByDescending(i => i.Pinned).ToList();
            _replayKnownLaps = _replayInfos.Count;
        }
        if (_hud.ReplayListOpen)
        {
            BuildReplayRows(keepSelection: true);
            if (_replayAutoSingle)
            {
                _replayAutoSingle = false;
                if (_replayOrigin == ReplayOrigin.PartyMeeting && _replayInfos.Count == 1 && _replayInfos[0].Openable)
                { _replayDirect = true; ChooseReplayRow(0); }
            }
        }
    }

    // ------------------------------------------------------------------ the list

    void OpenReplayList()
    {
        if (_state != State.Paused || !ReplayRowShown) return;      // (only the Party Meeting of a drive that can be resumed; a finished or failed run has no such meeting)
        _replayOrigin = ReplayOrigin.PartyMeeting;
        _replayAutoSingle = true; _replayDirect = false;
        _hud.SetReplayListOpen(true);
        _replayLooking = true;
        _hud.ReplayList = new ReplayListView { Notice = "LOOKING AT THE ARCHIVE..." };
        StartReplayListing();      // (a fresh look: laps finished since, tapes now ready)
    }

    void CloseReplayList()
    {
        _replayAutoSingle = false; _replayDirect = false;
        CancelReplayDelete();
        _hud.SetReplayListOpen(false);
        _overlayExitFrame = Engine.GetProcessFrames();
    }

    static string LapTimeText(float s) { int m = (int)(s / 60f); return $"{m}:{s - m * 60f:00.00}"; }

    ReplayRowView RowOf(LapEntryInfo i)
    {
        var m = i.Meta;
        string mode = m.Mode == "stakhanov" ? "STAKHANOV" : "RALLY";
        string kind = m.Kind switch { LapKind.Record => "RECORD", LapKind.Void => "VOID", _ => "" };
        // the light the lap started in: the day's phase of the laps driven then (the clock counts laps, not hours); a plain-day run recorded none
        string clockText = m.DayClock >= 0 ? DayNight.PhaseForLap((int)MathF.Floor(m.DayClock) + 1).ToString().ToUpperInvariant() : "";
        string detail = string.Join("  ", new[] { kind, clockText, KeepStateText(i), i.Pinned ? "PIN" : "", i.HasTrim ? "TRIM" : "", i.HasCameraKeys ? "CAM" : "", i.EditsState is LapEditsState.Unreadable or LapEditsState.Mismatch or LapEditsState.NewerVersion ? "EDITS?" : "", i.Retained && !string.IsNullOrEmpty(m.Name) ? m.Name : "" }.Where(x => x != ""));
        string flag = "";
        bool selectable = i.Openable;
        if (i.State == LapEntryState.Incompatible) { flag = "OLD VERSION"; selectable = false; }
        else if (i.State == LapEntryState.Failed && !i.Openable) { flag = "DAMAGED"; selectable = false; }
        else if (i.State == LapEntryState.Pending && !i.Openable) { flag = "PREPARING"; selectable = false; }
        else if (!m.HasTape) flag = "CAR ONLY";
        return new ReplayRowView { Title = $"{m.TrackId.ToUpperInvariant()} {mode}  {LapTimeText(m.LapSeconds)}", Detail = detail, Flag = flag, Selectable = selectable };
    }

    void BuildReplayRows(bool keepSelection)
    {
        var old = _hud.ReplayList;
        var rows = _replayInfos.Select(RowOf).ToArray();
        string notice = keepSelection && old != null && !_replayLooking ? old.Notice : "";
        _replayLooking = false;
        bool err = keepSelection && old != null && old.NoticeIsError;
        if (rows.Length == 0 && notice == "") notice = "NO LAPS RECORDED YET. DRIVE A LAP FIRST.";      // (the same words in the Office)
        _hud.ReplayList = new ReplayListView { Heading = _replayOrigin == ReplayOrigin.Office ? "RECORDINGS" : "REPLAY", Rows = rows, Selected = Math.Clamp(keepSelection && old != null ? old.Selected : 0, 0, Math.Max(0, rows.Length - 1)), Notice = notice, NoticeIsError = err };
    }

    void ReplayNotice(string text, bool error)
    {
        var l = _hud.ReplayList; l.Notice = text; l.NoticeIsError = error;
    }

    /// <summary>The list screen's input (polled while the Party Meeting is up). True while the list is open.</summary>
    bool ReplayMenuInput(bool alt)
    {
        if (!_hud.ReplayListOpen) return false;
        if (_replayOrigin != ReplayOrigin.PartyMeeting) return false;
        if (_keepNameOpen || _replayDeleteId != null || _replayDeleteTask != null || Engine.GetProcessFrames() <= _replayDeleteInputFrame + 1) return true;
        PollReplayListing();
        if (_replayOpenTask != null) { PollReplayOpen(); if (Input.IsActionJustPressed("pause")) { _replayOpenId = null; ReplayNotice("OPENING CANCELLED", false); _replayOpenTask = null; } return true; }
        var l = _hud.ReplayList; int n = l.Rows.Length;
        if (Input.IsActionJustPressed("pause")) CloseReplayList();
        else if (n > 0 && Input.IsActionJustPressed("accelerate")) l.Selected = (l.Selected + n - 1) % n;
        else if (n > 0 && Input.IsActionJustPressed("brake")) l.Selected = (l.Selected + 1) % n;
        else if (n > 0 && Input.IsActionJustPressed("start") && !alt) ChooseReplayRow(l.Selected);
        return true;
    }

    void ChooseReplayRow(int row)
    {
        if (row < 0 || row >= _replayInfos.Count || _replayOpenTask != null) return;
        var info = _replayInfos[row];
        if (!info.Openable) { ReplayNotice(info.State == LapEntryState.Incompatible ? "THIS LAP IS FROM ANOTHER VERSION" : info.State == LapEntryState.Failed ? "THIS LAP IS DAMAGED" : "THIS LAP IS STILL BEING PREPARED", true); return; }
        _replayOpenId = info.Id;
        var archive = ReplayArchiveFor(info.Id); string id = info.Id;
        _replayOpenTask = Task.Run(() => { var src = archive.Open(id, out string reason); return (src, reason); });
        ReplayNotice("OPENING...", false);
    }

    void PollReplayOpen()
    {
        if (_replayOpenTask is not { IsCompleted: true } t) return;
        _replayOpenTask = null;
        string id = _replayOpenId; _replayOpenId = null;
        if (t.IsFaulted) { ReplayNotice("COULD NOT OPEN THE LAP", true); GD.PushWarning("Replay: opening failed: " + t.Exception?.GetBaseException().Message); return; }
        var (src, reason) = t.Result;
        if (src == null) { ReplayNotice(("CANNOT OPEN: " + (reason ?? "UNKNOWN")).ToUpperInvariant(), true); return; }
        // The call comes from inside this scene's own frame (PauseInput): the viewer's suspend hides the hud and stops the scene, so it starts after the frame, never in the middle of it.
        var info = _replayInfos.FirstOrDefault(i => i.Id == id);
        Callable.From(() => StartReplayViewer(src, info)).CallDeferred();
    }

    /// <summary>Hand over to the viewer: only from the Party Meeting. The race is left in State.Paused; the session suspends everything else and gives it back.</summary>
    void StartReplayViewer(LapSource src, LapEntryInfo info)
    {
        bool here = _replayOrigin == ReplayOrigin.Office ? _state == State.Title && _recordingsOpen : _state == State.Paused;      // the Party Meeting's drive is suspended, the Office's title is simply running
        if (!here || !_hud.ReplayListOpen || _replayViewer != null) { ReplayNotice("THE MEETING HAS MOVED ON", true); return; }
        var session = OpenReplayViewer(src, out string error);
        if (session == null) { ReplayNotice(("CANNOT WATCH: " + error).ToUpperInvariant(), true); return; }
        session.SetEditContext(ReplayArchiveFor(src.Meta.Id), info);      // (RaceScene.ReplayViewerEdits.cs: a kept lap can be edited)
        session.SetTitle(info != null ? RowOf(info).Title + (info.Meta.Kind == LapKind.Record ? " RECORD" : info.Meta.Kind == LapKind.Void ? " VOID" : "") : "");
        ReplayNotice("", false);
        session.Restored += () =>
        {
            // back in the same Party Meeting (or the Office), at the same list
            if (!IsInstanceValid(this) || !_hud.ReplayListOpen) return;
            if (_replayDirect && !session.EndedByError) { CloseReplayList(); return; }
            _replayDirect = false;
            StartReplayListing();      // (a save or reset may have changed the marks)
            if (session.EndNotice != "") ReplayNotice(session.EndNotice, session.EndedByError);
        };
    }
}
