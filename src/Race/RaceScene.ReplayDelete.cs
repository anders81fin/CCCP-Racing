using System;
using System.Linq;
using System.Threading.Tasks;
using CCCPRacing.Replay;
using Godot;

namespace CCCPRacing.Race;

public partial class RaceScene
{
    string _replayDeleteId, _replayDeleteName;
    bool _replayDeleteConfirm;
    ulong _replayDeleteInputFrame;
    LapArchive _replayDeleteArchive;
    Task<LapDeleteResult> _replayDeleteTask;
    string _replayDeletingId;

    void BeginReplayDelete()
    {
        if (_keepTask != null || _replayDeleteTask != null || _replayOpenTask != null)
        { ReplayNotice("RECORDING IS BUSY - PLEASE WAIT", true); return; }
        int row = _hud.ReplayList.Selected;
        if (row < 0 || row >= _replayInfos.Count) { ReplayNotice("NO RECORDING TO DELETE", true); return; }
        var info = _replayInfos[row];
        _replayDeleteId = info.Id; _replayDeleteArchive = ReplayArchiveFor(info.Id);
        _replayDeleteName = string.IsNullOrEmpty(info.Meta.Name) ? RowOf(info).Title : info.Meta.Name;
        _replayDeleteConfirm = false;
        _hud.ReplayDeleteTitle = _replayDeleteName;
        _hud.ReplayDeleteDetail = KeepStateText(info) + "  " + RowOf(info).Title;
        _replayDeleteInputFrame = Engine.GetProcessFrames();
    }

    void CancelReplayDelete()
    {
        _replayDeleteId = null;
        _replayDeleteInputFrame = _overlayExitFrame = Engine.GetProcessFrames();
    }

    // Modal raw input is shared by the Party Meeting and Office, including mapped pad keys.
    bool ReplayDeleteRawInput(InputEvent e)
    {
        if (_replayDeleteId != null)
        {
            if (Engine.GetProcessFrames() <= _replayDeleteInputFrame + 1) return true;
            if (e is InputEventKey { Pressed: true } k)
            {
                switch (k.Keycode)
                {
                    case Key.Up or Key.Down or Key.Left or Key.Right or Key.W or Key.S:
                        _replayDeleteConfirm = !_replayDeleteConfirm; break;
                    case Key.Escape when !k.Echo: CancelReplayDelete(); break;
                    case Key.Enter or Key.KpEnter when !k.Echo && !k.AltPressed:
                        if (_replayDeleteConfirm) CommitReplayDelete(); else CancelReplayDelete(); break;
                }
            }
            else if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            {
                int row = _hud.ReplayDeleteRowAt(_hud.GetLocalMousePosition());
                if (row == 0) CancelReplayDelete(); else if (row == 1) CommitReplayDelete();
            }
            return true;
        }
        if (_replayDeleteTask != null) { _replayDeleteInputFrame = Engine.GetProcessFrames(); return true; }
        if (Engine.GetProcessFrames() <= _replayDeleteInputFrame + 1) return true;
        if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Delete, CtrlPressed: false, AltPressed: false }
            && Engine.GetProcessFrames() > _overlayFrame + 1)
        { BeginReplayDelete(); return true; }
        return false;
    }

    void CommitReplayDelete()
    {
        if (!ReferenceEquals(_replayDeleteArchive, ReplayArchiveFor(_replayDeleteId ?? _replayDeletingId)))
        { CancelReplayDelete(); ReplayNotice("THE PERSONNEL FILE HAS CHANGED", true); return; }
        _replayDeletingId = _replayDeleteId;
        CancelReplayDelete();
        _lapArchiveUsed = _replayDeleteArchive;
        _replayDeleteTask = _replayDeleteArchive.Delete(_replayDeletingId);
        ReplayNotice("DELETING THE RECORDING...", false);
    }

    void ReplayDeleteTick()
    {
        if (_replayDeleteTask is not { IsCompleted: true } task) return;
        _replayDeleteInputFrame = _overlayExitFrame = Engine.GetProcessFrames();
        _replayDeleteTask = null;
        var result = task.IsCompletedSuccessfully ? task.Result : new LapDeleteResult(false, task.Exception?.GetBaseException().Message ?? "unknown error");
        if (_hud.ReplayListOpen && ReferenceEquals(_replayDeleteArchive, ReplayArchiveFor(_replayDeleteId ?? _replayDeletingId)))
        {
            _replayListTask = null; // discard any listing begun before deletion
            if (result.Ok)
            {
                _replayInfos.RemoveAll(i => i.Id == _replayDeletingId);
                _replayKnownLaps = _replayInfos.Count;
                BuildReplayRows(true);
            }
            ReplayNotice(result.Ok ? "DELETED: " + _replayDeleteName : ("NOT DELETED: " + result.Message).ToUpperInvariant(), !result.Ok);
            _replayDeleteArchive.Refresh();
            StartReplayListing();
        }
        GD.Print("recording delete: " + _replayDeletingId + ": " + (result.Ok ? "ok" : result.Message));
        _replayDeleteArchive = null; _replayDeletingId = null;
    }
}
