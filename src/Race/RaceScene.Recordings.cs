using System;
using System.Linq;
using System.Threading.Tasks;
using CCCPRacing.Replay;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// PARTY OFFICE > KGB FILES > RECORDINGS and KEEP (docs/work-orders/player-recordings-phase4.md, A). The same list as the Party Meeting's REPLAY (RaceScene.ReplayMenu.cs) over the title,
/// for the laps of this game session ("THIS SESSION", in memory) and the ones kept on disk ("KEPT", the personnel file's lap folder, there after a restart). It owns no drive:
/// the title scene itself is the viewer's session owner and gets the title back as it was (the KGB FILES row on RECORDINGS, the title replay and the music). KEEP asks for a name and writes
/// the whole pair through the archive's own Retain (its worker, tmp-verify-replace). Explicit DELETE uses a separate confirmation and archive worker operation. Names are set at KEEP (and by KEEP on a kept lap: a rename);
/// labels, pins and cuts are the later editing phase.
/// </summary>
public partial class RaceScene
{
    public enum ReplayOrigin { PartyMeeting, Office }

    /// <summary>Where the open list (and the viewer opened from it) belongs. Everything that assumed the Party Meeting branches on this.</summary>
    ReplayOrigin _replayOrigin = ReplayOrigin.PartyMeeting;

    /// <summary>RECORDINGS is open over the title (the Office origin).</summary>
    bool _recordingsOpen;

    // KEEP: the name field and the save in flight
    bool _keepNameOpen;
    string _keepName = "";
    string _keepTargetId;
    bool _keepRename;
    Task<RetainResult> _keepTask;
    string _keepTaskName = "";
    LapArchive _keepArchive;      // the archive (the personnel file) the save in flight belongs to, and which lap, and whether it is a rename: a task keeps its own identity
    string _keepTaskId;
    bool _keepTaskRename;

    /// <summary>Test seam: the next KEEP ends in this error instead of writing (proves the visible failure and that the previous pair stays usable).</summary>
    internal static string KeepFailForTest;

    const int KeepNameMax = Hud.KeepNameMax;      // 24

    /// <summary>The "THIS SESSION" label: recent laps live in this game's memory only; a lap that was kept is "KEPT" and is on disk.</summary>
    static string KeepStateText(LapEntryInfo i) => i.Retained ? "KEPT" : "THIS SESSION";

    // ------------------------------------------------------------------ the Office list

    void EnterRecordings()
    {
        if (_state != State.Title || ViewerMode) return;
        _replayOrigin = ReplayOrigin.Office;
        _recordingsOpen = true;
        _replayKnownLaps = -1; _replayInfos = new();      // (the active personnel file's archive, looked at afresh: a switch of file never shows the other file's laps)
        _replayListTask = null;
        _hud.ReplayList = new ReplayListView { Heading = "RECORDINGS", AllowKeep = true, Notice = "LOOKING AT THE ARCHIVE..." };
        _replayLooking = true;
        _hud.SetReplayListOpen(true);
        _overlayFrame = Engine.GetProcessFrames();
        StartReplayListing();
    }

    void CloseRecordings()
    {
        _recordingsOpen = false;
        _keepNameOpen = false;
        CancelReplayDelete();
        _hud.SetReplayListOpen(false);
        _overlayExitFrame = Engine.GetProcessFrames();
        _replayOrigin = ReplayOrigin.PartyMeeting;
    }

    /// <summary>Every raw event while a list may be open: the name field first, K for KEEP in both origins, and the whole Office list. True when the event was consumed.</summary>
    bool RecordingsRawInput(InputEvent e)
    {
        if (!_hud.ReplayListOpen) return false;
        bool office = _recordingsOpen;
        if (_replayViewer is { State: not ReplayViewerState.Closed }) return false;
        if (_keepNameOpen)
        {
            if (e is InputEventKey { Pressed: true } key) KeepNameKey(key);
            return true;
        }
        if (ReplayDeleteRawInput(e)) return true;
        if (_replayOpenTask != null)
        {
            // opening: Esc (or B) cancels, anything else waits (the Party Meeting's own polled path does the same for its list)
            if (office && e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }) { _replayOpenId = null; ReplayNotice("OPENING CANCELLED", false); _replayOpenTask = null; }
            return office;
        }
        if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.K, AltPressed: false, CtrlPressed: false } && Engine.GetProcessFrames() > _overlayFrame + 1)
        {
            BeginKeep();
            return true;
        }
        if (!office || Engine.GetProcessFrames() <= _overlayFrame + 1) return office;
        var l = _hud.ReplayList; int n = l.Rows.Length;
        if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
        {
            if (_hud.ReplayRowAt(_hud.GetLocalMousePosition()) is >= 0 and var row) { l.Selected = row; ChooseReplayRow(row); }
            return true;
        }
        if (e is not InputEventKey { Pressed: true } k) return true;
        switch (k.Keycode)
        {
            case Key.Escape: if (!k.Echo) CloseRecordings(); break;
            case Key.Up or Key.W: if (n > 0) l.Selected = (l.Selected + n - 1) % n; break;
            case Key.Down or Key.S: if (n > 0) l.Selected = (l.Selected + 1) % n; break;
            case Key.Enter or Key.KpEnter: if (n > 0 && !k.Echo && !k.AltPressed) ChooseReplayRow(l.Selected); break;
        }
        return true;
    }

    /// <summary>Per frame: the Office list's listing and opening results (the Party Meeting polls its own), and a save's outcome in either.</summary>
    void RecordingsTick()
    {
        if (ViewerMode) return;
        if (_recordingsOpen) { PollReplayListing(); if (_replayOpenTask != null) PollReplayOpen(); }
        KeepTick();
        ReplayDeleteTick();
    }

    // ------------------------------------------------------------------ KEEP

    /// <summary>K (or the pad's Y) on the selected lap: ask for a name, then save the pair. Refuses visibly when it cannot.</summary>
    void BeginKeep()
    {
        var l = _hud.ReplayList;
        if (_keepTask != null) { ReplayNotice("STILL SAVING A LAP - WAIT", true); return; }
        if (l.Rows.Length == 0) { ReplayNotice("NO LAP TO KEEP", true); return; }
        if (l.Selected < 0 || l.Selected >= _replayInfos.Count) return;
        var info = _replayInfos[l.Selected];
        if (info.State == LapEntryState.Incompatible) { ReplayNotice("THIS LAP IS FROM ANOTHER VERSION - CANNOT KEEP", true); return; }
        if (!info.Openable) { ReplayNotice(info.State == LapEntryState.Failed ? "THIS LAP IS DAMAGED - CANNOT KEEP" : "THIS LAP IS STILL BEING PREPARED", true); return; }
        _keepTargetId = info.Id;
        _keepRename = info.Retained;
        _keepName = info.Retained ? info.Meta.Name ?? "" : "";
        _keepNameOpen = true;
        _hud.KeepNameSub = (info.Retained ? "RENAME " : "") + RowOf(info).Title;
    }

    /// <summary>The name field's typing (keyboard, and the pad's wheel through its Up/Down/Enter keys): allowed characters, Backspace, Enter keeps, Esc cancels.</summary>
    void KeepNameKey(InputEventKey k)
    {
        switch (k.Keycode)
        {
            case Key.Enter or Key.KpEnter:
                if (k.Echo || k.AltPressed) return;
                CommitKeep();
                return;
            case Key.Escape:
                if (k.Echo) return;
                _keepNameOpen = false; _keepTargetId = null;
                _overlayExitFrame = Engine.GetProcessFrames();
                ReplayNotice("KEEP CANCELLED - NOTHING WAS SAVED", false);
                return;
            case Key.Backspace:
                if (_keepName.Length > 0) _keepName = _keepName[..^1];
                return;
        }
        if (k.CtrlPressed || k.AltPressed || k.MetaPressed || k.Unicode <= 0 || k.Unicode > char.MaxValue) return;
        char c = char.ToUpperInvariant((char)k.Unicode);
        if (_keepName.Length >= KeepNameMax || !Leaderboard.IsNameChar(c)) return;
        if (c == ' ' && (_keepName.Length == 0 || _keepName[^1] == ' ')) return;
        _keepName += c;
    }

    /// <summary>Uppercase, allowed characters, single spaces, at most KeepNameMax long (the leaderboard's characters, a longer limit).</summary>
    internal static string CleanKeepName(string s)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c0 in s ?? "")
        {
            char c = char.ToUpperInvariant(c0);
            if (!Leaderboard.IsNameChar(c) || (c == ' ' && (sb.Length == 0 || sb[^1] == ' '))) continue;
            sb.Append(c);
        }
        string r = sb.ToString().Trim();
        return r.Length > KeepNameMax ? r[..KeepNameMax].TrimEnd() : r;
    }

    /// <summary>An empty name identifies the numbered lap, track and time ("LAP 3 ILYICH 1-23-45"); legacy laps keep the earlier track/mode/time label. A name already used by another kept lap is allowed (every recording has its own id); the list shows the time beside it.</summary>
    string DefaultKeepName(LapEntryInfo info)
    {
        int cs = (int)MathF.Round(info.Meta.LapSeconds * 100);      // ("1:23.45" would lose its punctuation in a name: 1-23-45)
        if (info.Meta.LapNumber > 0) return CleanKeepName($"LAP {info.Meta.LapNumber} {info.Meta.TrackId} {cs / 6000}-{cs / 100 % 60:00}-{cs % 100:00}");
        return CleanKeepName($"{info.Meta.TrackId} {(info.Meta.Mode == "stakhanov" ? "STAK" : "RALLY")} {cs / 6000}-{cs / 100 % 60:00}-{cs % 100:00}");
    }

    void CommitKeep()
    {
        var info = _replayInfos.FirstOrDefault(i => i.Id == _keepTargetId);
        _keepNameOpen = false;
        _overlayExitFrame = Engine.GetProcessFrames();
        if (info == null) { ReplayNotice("THE LAP IS GONE FROM THE LIST", true); return; }
        string name = CleanKeepName(_keepName);
        if (name.Length == 0) name = DefaultKeepName(info);
        _keepTaskName = name;
        var archive = ReplayArchiveFor(info.Id); string id = info.Id;
        _lapArchiveUsed = archive;      // (the scene's exit waits for the worker: a KEEP followed by a quit still lands on disk)
        _keepArchive = archive; _keepTaskId = id; _keepTaskRename = _keepRename;
        if (!string.IsNullOrEmpty(KeepFailForTest)) { var fail = KeepFailForTest; _keepTask = Task.FromResult(new RetainResult(false, fail, null)); }
        else _keepTask = archive.Retain(id, name);
        ReplayNotice(_keepRename ? "SAVING THE NEW NAME..." : "SAVING THE RECORDING...", false);
    }

    /// <summary>Per frame while a save is in flight: the notice becomes the outcome, and the list is looked at again.</summary>
    void KeepTick()
    {
        if (_keepTask is not { IsCompleted: true } t) return;
        _keepTask = null;
        RetainResult r;
        if (t.IsFaulted) r = new RetainResult(false, t.Exception?.GetBaseException().Message ?? "unknown error", null);
        else r = t.Result;
        // the outcome belongs to the archive the save was started in: another personnel file's list (a switch of file while it ran) neither gets its notice nor is mistaken for it
        bool sameFile = ReferenceEquals(_keepArchive, ReplayArchiveFor(_keepTaskId));
        if (_hud.ReplayListOpen && sameFile)
        {
            if (r.Ok) ReplayNotice(_keepTaskRename ? "RENAMED: " + _keepTaskName : "KEPT: " + _keepTaskName, false);
            else ReplayNotice(("NOT KEPT: " + (r.Message ?? "UNKNOWN ERROR")).ToUpperInvariant(), true);
            StartReplayListing();
        }
        else if (!r.Ok) GD.PushWarning("KEEP: " + r.Message);
        GD.Print($"keep: {(_keepTaskRename ? "rename" : "keep")} '{_keepTaskName}' of {_keepTaskId}: {(r.Ok ? "ok " + r.Dir : "FAILED " + r.Message)}{(sameFile ? "" : " (another personnel file is active now: no notice)")}");
        _keepArchive = null; _keepTaskId = null;
    }
}
