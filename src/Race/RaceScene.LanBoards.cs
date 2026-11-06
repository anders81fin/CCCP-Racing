using System;
using System.Linq;
using CCCPRacing.Net;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// SHARE BOARDS in the LAN lobby (LanSession.Boards.cs, LanBoardStore, docs/lan-leaderboard-sync.md). Every comrade decides alone:
/// B (pad Y) asks SHARE BOARDS? (YES shares this machine's board times, names and attached recordings with comrades who also say YES,
/// and merges theirs here); B again says NO (withdraws: transfers stop, rows already merged stay). R (pad X) resyncs, or cancels a
/// running sync. The mouse clicks the same two lines. Nothing is saved: the answer starts NO in every session.
/// Test flags: --lan-board-share=yes|no (answer on reaching the lobby), --lan-board-share-delay=S, --lan-board-expect=N (host: the
/// automatic ballot waits until N comrades share and every sync is done), --lan-board-withdraw-after=N (withdraw after N chunks in).
/// </summary>
public partial class RaceScene
{
    LanBoardStore _lanBoardStore;
    bool? _lanBoardFlag;
    float _lanBoardFlagDelay = 0.5f;
    double _lanBoardFlagAt = -1;
    bool _lanBoardFlagDone;
    int _lanBoardExpect, _lanBoardWithdrawAfter = -1;
    bool _lanBoardAskYes;
    string _lanBoardHostNote; double _lanBoardHostNoteAt = -1;

    bool LanBoardsAllowed => InstructionTrack == null && !ViewerMode && !_labRun && LanPlaybackSource == null && _records != null && _boards[0] != null;

    void LanBoardsArgs(string a)
    {
        if (a.StartsWith("--lan-board-share=")) _lanBoardFlag = a.EndsWith("=yes") || a.EndsWith("=1");
        else if (a.StartsWith("--lan-board-share-delay=")) _lanBoardFlagDelay = float.Parse(a["--lan-board-share-delay=".Length..], System.Globalization.CultureInfo.InvariantCulture);
        else if (a.StartsWith("--lan-board-expect=")) _lanBoardExpect = int.Parse(a["--lan-board-expect=".Length..]);
        else if (a == "--lan-board-ui-probe") _lanBoardProbe = true;
        else if (a.StartsWith("--lan-board-withdraw-after=")) _lanBoardWithdrawAfter = int.Parse(a["--lan-board-withdraw-after=".Length..]);
    }

    /// <summary>Per frame (LanFrame): the store of this scene is the session's while the scene lives; test flags.</summary>
    void LanBoardsFrame()
    {
        if (_net == null) return;
        LanBoardsProbeTick();      // --lan-board-ui-probe (RaceScene.LanBoardsTest.cs)
        if (LanBoardsAllowed && _net.Active && _net.BoardStore == null)
        {
            _lanBoardStore ??= new LanBoardStore(_records.Dir, _track.Id, _records.Signature, set => _boards[set], () => { _otherBoards.Clear(); _replayTracks = null; }, LeaderboardLapArchive);
            _lanBoardStore.BackupStamp = () => _net.BoardSessionStamp;
            _net.BoardStore = _lanBoardStore;
        }
        if (_lanBoardFlag is bool on && !_lanBoardFlagDone && _net.Active && _net.InLobby && _net.State == LanSession.Phase.Lobby && _lanPage == LanPage.Lobby)
        {
            if (_lanBoardFlagAt < 0) _lanBoardFlagAt = LanSession.Now + _lanBoardFlagDelay;
            else if (LanSession.Now >= _lanBoardFlagAt) { _lanBoardFlagDone = true; if (on) _net.SetBoardShare(true); GD.Print($"LAN boards: test flag answered {(on ? "YES" : "NO")}"); }
        }
        if (_lanBoardWithdrawAfter >= 0 && _net.BoardShareMine && _net.BoardChunksIn >= _lanBoardWithdrawAfter)
        { _lanBoardWithdrawAfter = -1; GD.Print($"LAN boards: test withdraw after {_net.BoardChunksIn} chunks"); _net.SetBoardShare(false); }
    }

    void LanBoardsDetach()
    {
        if (_net != null && _lanBoardStore != null && ReferenceEquals(_net.BoardStore, _lanBoardStore)) _net.BoardStore = null;
    }

    /// <summary>
    /// Host automatic ballot (tests): with --lan-board-expect=N it starts asking for the ballot as soon as N comrades share and have
    /// offered, so the production barrier in StartBallot (not this test gate) is what holds it until the sync is done.
    /// </summary>
    bool LanBoardsReadyForAutostart => _lanBoardExpect <= 0 || _net.BoardSharersOffered(_lanBoardExpect);

    /// <summary>The lobby's board keys: B / pad Y share (question) or withdraw, R / pad X resync or cancel. True: taken.</summary>
    bool LanBoardsLobbyKey(Key key, bool first)
    {
        if (!first || _net.State != LanSession.Phase.Lobby) return key is Key.B or Key.R;
        if (key == Key.B) { LanBoardsToggle(); return true; }
        if (key == Key.R) { LanBoardsAction(); return true; }
        return false;
    }

    void LanBoardsToggle()
    {
        if (_net.BoardShareMine) { _net.SetBoardShare(false); GD.Print("LAN boards: NO (withdrawn) from the lobby"); }
        else { _lanBoardAskYes = false; SetLanPage(LanPage.Boards); GD.Print("LAN boards: question open"); }
    }

    void LanBoardsAction()
    {
        if (!_net.BoardShareMine) return;
        if (_net.BoardBusyLocal) { _net.CancelBoardSync(); GD.Print("LAN boards: cancel from the lobby"); }
        else if (_net.ResyncBoards()) GD.Print("LAN boards: resync from the lobby");
    }

    /// <summary>SHARE BOARDS? : Up/Left YES, Down/Right NO, Enter answers, Esc backs out (stays NO).</summary>
    void LanBoardsAskKey(bool up, bool down, bool enter, bool esc)
    {
        if (esc) { SetLanPage(LanPage.Lobby); return; }
        if (up) _lanBoardAskYes = true;
        else if (down) _lanBoardAskYes = false;
        else if (enter) LanBoardsAnswer(_lanBoardAskYes);
    }

    void LanBoardsAnswer(bool yes)
    {
        SetLanPage(LanPage.Lobby);
        if (!yes) { GD.Print("LAN boards: answered NO"); return; }
        if (!_net.SetBoardShare(true)) LanBoardsHostSay("NOT NOW: ONLY IN THE LOBBY");
        GD.Print("LAN boards: answered YES");
    }

    /// <summary>The mouse on the question (rows) and on the lobby's two board lines. True: a mouse event on these pages (always taken).</summary>
    bool LanBoardsMouse(InputEvent e)
    {
        if (e is not InputEventMouse) return false;
        LanBoardsMouseAt(_hud.GetLocalMousePosition(), e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }, e is InputEventMouseMotion);
        return true;
    }

    /// <summary>The mouse at a HUD position (480x270 pixels): hover selects on the question, a click answers or works the lobby lines.</summary>
    void LanBoardsMouseAt(Vector2 at, bool click, bool hover)
    {
        if (_lanPage == LanPage.Boards)
        {
            int row = _hud.LanBoardsAskRowAt(at);
            if (row >= 0 && hover) _lanBoardAskYes = row == 0;
            else if (row >= 0 && click) LanBoardsAnswer(row == 0);
            return;
        }
        if (_lanPage == LanPage.Lobby && click && _net.State == LanSession.Phase.Lobby)
        {
            int line = _hud.LanBoardsLobbyLineAt(at);
            if (line == 0) LanBoardsToggle();
            else if (line == 1) LanBoardsAction();
        }
    }

    void LanBoardsHostSay(string s) { _lanBoardHostNote = s; _lanBoardHostNoteAt = LanSession.Now; }

    /// <summary>Host Enter in the lobby: the ballot, unless a comrade's board sync still runs (then it says whose).</summary>
    void LanBoardsStartBallot()
    {
        if (_net.StartBallot()) return;
        if (_net.BoardSyncPending) LanBoardsHostSay("BALLOT WAITS FOR BOARD SYNC: " + _net.BoardPendingNames);
    }

    /// <summary>What the lobby shows of SHARE BOARDS (HudLanBoards.cs).</summary>
    void LanBoardsHud()
    {
        var b = _hud.LanBoards;
        b.Ask = _lanBoardAskYes;
        b.On = _net.BoardShareMine; b.Busy = _net.BoardBusyLocal;
        var (done, total, failed, bytes, of) = _net.BoardProgress;
        b.Progress = b.Busy && total > 0 ? $"RECORDINGS {Math.Min(done + 1, total)}/{total}" + (of > 0 ? $"  {bytes / 1048576.0:F1}/{of / 1048576.0:F1} MB" : "") : null;
        double now = LanSession.Now;
        string note = _net.BoardNoteAt >= 0 && now - _net.BoardNoteAt < 8 ? _net.BoardNote : null;
        bool bad = note != null && _net.BoardNoteBad;
        if (_lanBoardHostNoteAt >= 0 && now - _lanBoardHostNoteAt < 5 && (_net.BoardSyncPending || !_net.IsHost)) { note = _lanBoardHostNote; bad = true; }
        b.Note = note; b.NoteBad = bad;
        b.Waiting = _net.IsHost && _net.BoardSyncPending;
    }
}
