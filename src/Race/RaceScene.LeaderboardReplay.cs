using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CCCPRacing.Replay;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;
public partial class RaceScene
{
    // Boards are shared between personnel files; their separate archive has the same scope.
    // These laps belong to result rows, never KEEP/THIS SESSION lists.
    LapArchive LeaderboardLapArchive => LapArchive.For(WorldTapeFile.Real(_records.Dir + "leaderboard-laps"));
    LapArchive _boardLapArchiveUsed;
    string _lastBoardRecordingId;
    readonly List<(string Id, Task<RetainResult> Work)> _boardRetains = new();
    int _boardReplaySelected;
    string _boardReplayNotice;
    ulong _boardReplayNoticeUntil;
    Task<(LapSource Source, string Reason)> _boardReplayOpen;
    string _boardReplayOpenTrack, _boardReplayOpenId;
    static readonly int[] RallyBoardSets = { 0, 2, 3, 4 };

    (Leaderboard.Entry Entry, int Set) BoardReplayRow()
    {
        string track = Tracks[_boardTrack].id;
        int capacity = _boardPage == 1 ? Leaderboard.StakhanovSize : Leaderboard.Size;
        int set = _boardPage == 1 ? 1 : RallyBoardSets[Math.Clamp(_boardReplaySelected / capacity, 0, 3)];
        var entries = BoardFor(track, set);
        int index = _boardReplaySelected % capacity;
        return (index >= 0 && index < entries.Count ? entries[index] : null, set);
    }
    string BoardReplayStatus()
    {
        if (_boardReplayOpen != null) return "OPENING RECORDING...";
        if (Time.GetTicksMsec() < _boardReplayNoticeUntil) return _boardReplayNotice;
        return BoardReplayRow().Entry?.RecordingId != null ? (_padMode ? "A REPLAY" : "ENTER REPLAY") : "NO RECORDING FOR THIS RESULT";
    }
    void BoardReplayNotice(string text)
    { _boardReplayNotice = text; _boardReplayNoticeUntil = Time.GetTicksMsec() + 5000; }

    bool LeaderboardReplayInput(InputEvent e)
    {
        // Keep the common stick/D-pad repeat and device-selection path. It emits
        // synthetic keys back here; only shoulder track switching is board-specific.
        if (e is InputEventJoypadMotion || e is InputEventJoypadButton { ButtonIndex:
            JoyButton.DpadUp or JoyButton.DpadDown or JoyButton.DpadLeft or JoyButton.DpadRight or JoyButton.A or JoyButton.B or JoyButton.Start }) return false;
        if (Engine.GetProcessFrames() <= _overlayFrame) return true;
        if (e is InputEventJoypadButton { Pressed: true } pad) PadSeen(pad.Device, true);
        else if (e is InputEventKey { Pressed: true } physical && physical.Device != PadKeyDevice) _padMode = false;
        int rows = _boardPage == 1 ? Leaderboard.StakhanovSize : Leaderboard.Size * 4;
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
        {
            int row = _hud.BoardRowAt(_hud.GetLocalMousePosition());
            if (row >= 0 && row < rows) { _boardReplaySelected = row; _boardReplayNoticeUntil = 0; OpenBoardReplay(); }
            return true;
        }
        Key key = e switch
        {
            InputEventKey { Pressed: true } k => k.Keycode,
            InputEventJoypadButton { Pressed: true } b => b.ButtonIndex switch {
                JoyButton.DpadUp => Key.Up, JoyButton.DpadDown => Key.Down,
                JoyButton.DpadLeft => Key.Left, JoyButton.DpadRight => Key.Right,
                JoyButton.LeftShoulder => Key.Pageup, JoyButton.RightShoulder => Key.Pagedown,
                JoyButton.A => Key.Enter, JoyButton.B => Key.Escape, _ => Key.None },
            _ => Key.None,
        };
        switch (key)
        {
            case Key.Up: _boardReplayNoticeUntil = 0; _boardReplaySelected = (_boardReplaySelected + rows - 1) % rows; break;
            case Key.Down: _boardReplayNoticeUntil = 0; _boardReplaySelected = (_boardReplaySelected + 1) % rows; break;
            case Key.Left or Key.Right or Key.A or Key.D:
                _boardReplayNoticeUntil = 0;
                _boardPage = _boardPage == 1 ? 0 : 1; _boardReplaySelected = 0; break;
            case Key.Pageup or Key.Pagedown or Key.W or Key.S:
                _boardReplayNoticeUntil = 0;
                int direction = key is Key.Pageup or Key.W ? -1 : 1;
                _boardTrack = (_boardTrack + Tracks.Length + direction) % Tracks.Length;
                _boardPage = BoardPageWithLaps(Tracks[_boardTrack].id, _boardPage); _boardReplaySelected = 0; break;
            case Key.Enter or Key.KpEnter:
                if (e is not InputEventKey { Echo: true }) OpenBoardReplay(); break;
            case Key.Escape or Key.Backspace:
                _showBoard = false; _overlayExitFrame = Engine.GetProcessFrames(); _hud.Mode = HudMode.Title; break;
        }
        return true;
    }
    void OpenBoardReplay()
    {
        if (_boardReplayOpen != null) return;
        var (entry, set) = BoardReplayRow();
        if (entry?.RecordingId == null) { BoardReplayNotice("NO RECORDING FOR THIS RESULT"); return; }
        string id = entry.RecordingId, track = Tracks[_boardTrack].id;
        _boardReplayOpenId = id; _boardReplayOpenTrack = track;
        var archive = LeaderboardLapArchive;
        _boardReplayOpen = Task.Run(() =>
        {
            var source = archive.Open(id, out string reason);
            if (source != null && (source.Meta.TrackId != track || source.Meta.Set != set || Math.Abs(source.Meta.LapSeconds - entry.Time) > .001f))
                return ((LapSource)null, "RECORDING DOES NOT MATCH THIS RESULT");
            return (source, reason);
        });
    }
    void LeaderboardReplayTick()
    {
        if (_boardLapArchiveUsed != null)
            foreach (var message in _boardLapArchiveUsed.DrainMessages()) GD.Print("leaderboard recording: " + message);
        for (int i = _boardRetains.Count - 1; i >= 0; i--)
        {
            var work = _boardRetains[i]; if (!work.Work.IsCompleted) continue;
            _boardRetains.RemoveAt(i);
            if (work.Work.IsFaulted || !work.Work.Result.Ok)
            {
                string why = work.Work.IsFaulted ? work.Work.Exception.GetBaseException().Message : work.Work.Result.Message;
                GD.PushWarning("Leaderboard recording " + work.Id + " could not be saved: " + why);
                _hud?.Show("RECORDING NOT SAVED", why.ToUpperInvariant(), 5f, Hud.Red);
            }
        }
        if (_boardReplayOpen is not { IsCompleted: true } task) return;
        _boardReplayOpen = null;
        if (!_showBoard || _boardScoped || Tracks[_boardTrack].id != _boardReplayOpenTrack || BoardReplayRow().Entry?.RecordingId != _boardReplayOpenId) return;
        if (task.IsFaulted) { BoardReplayNotice("COULD NOT OPEN RECORDING"); return; }
        var (source, reason) = task.Result;
        if (source == null) { BoardReplayNotice((reason ?? "RECORDING NOT AVAILABLE").ToUpperInvariant()); return; }
        _boardReplayNoticeUntil = 0;
        var archive = LeaderboardLapArchive;
        var info = archive.List().FirstOrDefault(x => x.Id == _boardReplayOpenId);
        Callable.From(() =>
        {
            if (!_showBoard || _boardScoped || Tracks[_boardTrack].id != source.Meta.TrackId || BoardReplayRow().Entry?.RecordingId != source.Meta.Id) return;
            var session = OpenReplayViewer(source, out string error);
            if (session == null) { BoardReplayNotice((error ?? "CANNOT OPEN RECORDING").ToUpperInvariant()); return; }
            session.SetEditContext(archive, info);
            session.SetTitle("LEADERBOARD " + LapTimer.Format(source.Meta.LapSeconds));
        }).CallDeferred();
    }
}
