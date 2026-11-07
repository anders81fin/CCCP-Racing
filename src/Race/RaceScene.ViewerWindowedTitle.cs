using System;
using System.Collections;
using System.Linq;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

// Windowed scenario "title-menu" (--viewer-windowed-only=titlemenu): PARTY OFFICE on the real title through the real Main.tscn/PixelFrame with injected keyboard, pad and mouse events:
// PARTY OFFICE (KGB FILES, CREDITS, RESIGN) and its KGB FILES row (PERSONNEL, LEADERBOARDS, RECORDINGS), each screen's way back to its own row and item, the back chain
// KGB FILES -> PARTY OFFICE -> top row, the pad's B one level at a time and its Back shortcut, and that nothing in it takes a file into use, leaves the title or hands in a resignation.
public partial class RaceScene
{
    bool VwFilesRow() => _menuParent == MenuItem.Files && _hud.MenuLabels.SequenceEqual(new[] { "PERSONNEL", "LEADERBOARDS", "RECORDINGS" }) && _hud.MenuHeader == "KGB FILES";

    bool VwCalm() => _state == State.Title && _resignOut <= 0f && !_askName && !_showFiles && !_showBoard && !_showLog && !_recordingsOpen;

    IEnumerable VrClick(Vector2 at)
    {
        _vwDrv.SetControl(true);
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, GlobalPosition = at }); yield return null; yield return null;
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at, GlobalPosition = at }); yield return null; yield return null;
        _vwDrv.SetControl(false);
    }

    IEnumerable VwTitleMenu()
    {
        foreach (var x in VwWaitReady()) yield return x;
        for (int i = 0; i < 600 && !(_state == State.Title && _card == TitleCard.None && !_showLog && !_askName); i++) yield return null;
        for (int i = 0; i < 60; i++) yield return null;
        VwCheck(_state == State.Title && _menuParent == null, $"the title's top row is up ({_state}, parent {_menuParent})");
        VwCheck(_hud.MenuLabels.SequenceEqual(new[] { "SINGLE PLAYER", "MULTIPLAYER", "PARTY OFFICE" }), $"top row unchanged: {string.Join(" | ", _hud.MenuLabels)}");
        int fileBefore = _file;
        // to PARTY OFFICE
        for (int i = 0; i < 6 && _menu != MenuItem.Office; i++) foreach (var x in VrTap(Key.Right)) yield return x;
        VwCheck(_menu == MenuItem.Office, "the cursor is on PARTY OFFICE");
        foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_menuParent == MenuItem.Office && _menu == MenuItem.Files && _hud.MenuLabels.SequenceEqual(new[] { "KGB FILES", "CREDITS", "RESIGN" }) && _hud.MenuHeader == "PARTY OFFICE",
            $"PARTY OFFICE row on KGB FILES: {string.Join(" | ", _hud.MenuLabels)} (header {_hud.MenuHeader}, on {_menu})");
        VwCheck(_hud.MenuShort.SequenceEqual(new[] { "FILES", "CREDITS", "RESIGN" }), $"PARTY OFFICE short labels: {string.Join(" | ", _hud.MenuShort)}");
        foreach (var x in VrShot("title-office")) yield return x;
        // KGB FILES: its own row
        foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(VwFilesRow() && _menu == MenuItem.Personnel && VwCalm(), $"Enter on KGB FILES opens its row on PERSONNEL: {string.Join(" | ", _hud.MenuLabels)} (header {_hud.MenuHeader}, on {_menu})");
        VwCheck(_hud.MenuShort.SequenceEqual(new[] { "PERSONNEL", "TOP 10", "REC" }), $"KGB FILES short labels: {string.Join(" | ", _hud.MenuShort)}");
        VwCheck(_hud.MenuHint is ("PERSONNEL", _), $"the hint box is PERSONNEL's ({_hud.MenuHint?.title})");
        foreach (var x in VrShot("title-kgb-files")) yield return x;
        // the mouse has no title-row binding: a click on the row changes nothing
        var size = GetViewport().GetVisibleRect().Size;
        foreach (var x in VrClick(new Vector2(size.X / 2, size.Y - 30))) yield return x;
        for (int i = 0; i < 4; i++) yield return null;
        VwCheck(VwFilesRow() && _menu == MenuItem.Personnel && VwCalm(), $"mouse: a click on the KGB FILES row leaves it as it was (on {_menu}, parent {_menuParent})");
        // PERSONNEL: the five profiles, Esc back to KGB FILES on PERSONNEL
        foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 8; i++) yield return null;
        VwCheck(_showFiles && _hud.Mode == HudMode.Files && _hud.Files.Length == PlayerFiles.Count && !_filesAtLaunch, $"PERSONNEL opens the {PlayerFiles.Count} personnel files ({_hud.Files?.Length})");
        foreach (var x in VrShot("title-personnel")) yield return x;
        foreach (var x in VrTap(Key.Down)) yield return x;
        VwCheck(_showFiles && _filesSel == fileBefore % PlayerFiles.Count, $"Down moves in the files ({_filesSel})");
        foreach (var x in VrClick(new Vector2(size.X / 2, size.Y / 2))) yield return x;
        VwCheck(_showFiles && !_askName && _file == fileBefore, "mouse: a click in PERSONNEL neither closes it nor takes a file into use");
        foreach (var x in VrTap(Key.Escape)) yield return x;
        for (int i = 0; i < 8; i++) yield return null;
        VwCheck(VwFilesRow() && _menu == MenuItem.Personnel && VwCalm() && _file == fileBefore, $"ESC from PERSONNEL returns to KGB FILES on PERSONNEL, the file in use unchanged (parent {_menuParent}, on {_menu}, file {_file})");
        // LEADERBOARDS and back
        foreach (var x in VrTap(Key.Right)) yield return x;
        VwCheck(_menu == MenuItem.Board && _hud.MenuHint is ("LEADERBOARDS", _), $"Right reaches LEADERBOARDS ({_menu})");
        foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 8; i++) yield return null;
        VwCheck(_showBoard && _hud.Mode == HudMode.Board, "LEADERBOARDS opens the board of honour");
        // the office board stays the unrestricted one (not CHOOSE TRACK's scoped view): the built track, a selected row, Left/Right the run, W/S the track
        int built = Array.FindIndex(Tracks, t => t.id == _trackId), page0 = _boardPage;
        VwCheck(!_boardScoped && !_hud.BoardScoped && _boardTrack == built && _hud.BoardSelected == 0 && _hud.BoardReplayNotice != "", $"the office board is unrestricted: built track {_hud.BoardTrack}, row selected, replay line '{_hud.BoardReplayNotice}'");
        foreach (var x in VrTap(Key.Right)) yield return x;
        VwCheck(_showBoard && _boardPage != page0 && _hud.BoardStakhanov == (_boardPage == 1), $"office board: Right switches the run (page {page0} -> {_boardPage})");
        foreach (var x in VrTap(Key.S)) yield return x;
        VwCheck(_showBoard && _boardTrack == (built + 1) % Tracks.Length && _hud.BoardTrack == Tracks[_boardTrack].name, $"office board: S goes to the next track ({_hud.BoardTrack})");
        foreach (var x in VrTap(Key.W)) yield return x;
        foreach (var x in VrTap(Key.Left)) yield return x;
        VwCheck(_showBoard && _boardTrack == built && _boardPage == page0, $"office board: W and Left go back (track {_boardTrack}, page {_boardPage})");
        foreach (var x in VrShot("title-leaderboards")) yield return x;
        foreach (var x in VrTap(Key.Escape)) yield return x;
        for (int i = 0; i < 8; i++) yield return null;
        VwCheck(VwFilesRow() && _menu == MenuItem.Board && VwCalm(), $"ESC from the board returns to KGB FILES on LEADERBOARDS (parent {_menuParent}, on {_menu})");
        // RECORDINGS and back
        foreach (var x in VrTap(Key.Right)) yield return x;
        VwCheck(_menu == MenuItem.Recordings, $"Right reaches RECORDINGS ({_menu})");
        foreach (var x in VrTap(Key.Right)) yield return x;
        VwCheck(_menu == MenuItem.Recordings && _menuParent == MenuItem.Files, "the KGB FILES row ends at RECORDINGS (no CREDITS in it)");
        foreach (var x in VrTap(Key.Enter)) yield return x;
        foreach (var x in VrUntil(() => _recordingsOpen && _hud.ReplayListOpen && !_replayLooking, 60 * 10, "RECORDINGS opens its list")) yield return x;
        foreach (var x in VrTap(Key.Escape)) yield return x;
        for (int i = 0; i < 8; i++) yield return null;
        VwCheck(VwFilesRow() && _menu == MenuItem.Recordings && VwCalm(), $"ESC from RECORDINGS returns to KGB FILES on RECORDINGS (parent {_menuParent}, on {_menu})");
        // up: KGB FILES -> PARTY OFFICE on KGB FILES -> CREDITS and back -> top row
        foreach (var x in VrTap(Key.Escape)) yield return x;
        for (int i = 0; i < 4; i++) yield return null;
        VwCheck(_menuParent == MenuItem.Office && _menu == MenuItem.Files && _hud.MenuHeader == "PARTY OFFICE" && VwCalm(), $"ESC in KGB FILES goes to PARTY OFFICE on KGB FILES (parent {_menuParent}, on {_menu})");
        for (int i = 0; i < 6 && _menu != MenuItem.Credits; i++) foreach (var x in VrTap(Key.Right)) yield return x;
        VwCheck(_menu == MenuItem.Credits, "the cursor is on CREDITS");
        foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 8; i++) yield return null;
        VwCheck(_showLog && _menu == MenuItem.Credits, "CREDITS opens");
        foreach (var x in VrShot("title-credits")) yield return x;
        foreach (var x in VrTap(Key.Escape)) yield return x;
        for (int i = 0; i < 8; i++) yield return null;
        VwCheck(!_showLog && _menuParent == MenuItem.Office && _menu == MenuItem.Credits, "ESC from the credits returns to PARTY OFFICE at CREDITS");
        foreach (var x in VrTap(Key.Escape)) yield return x;
        for (int i = 0; i < 4; i++) yield return null;
        VwCheck(_menuParent == null && _menu == MenuItem.Office && VwCalm(), "ESC in PARTY OFFICE goes to the top row on PARTY OFFICE");
        // the pad: A opens, B goes back one level at a time
        _padMode = true;
        foreach (var x in VrPad(JoyButton.A)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_menuParent == MenuItem.Office && _menu == MenuItem.Files, $"pad: A opens PARTY OFFICE on KGB FILES (on {_menu})");
        foreach (var x in VrPad(JoyButton.A)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(VwFilesRow() && _menu == MenuItem.Personnel, $"pad: A opens KGB FILES on PERSONNEL (on {_menu})");
        foreach (var x in VrPad(JoyButton.A)) yield return x;
        for (int i = 0; i < 8; i++) yield return null;
        VwCheck(_showFiles && !_askName, "pad: A opens PERSONNEL");
        foreach (var x in VrPad(JoyButton.B)) yield return x;
        for (int i = 0; i < 8; i++) yield return null;
        VwCheck(VwFilesRow() && _menu == MenuItem.Personnel && VwCalm() && _file == fileBefore, $"pad: B closes PERSONNEL back to KGB FILES on PERSONNEL (parent {_menuParent}, on {_menu})");
        foreach (var x in VrPad(JoyButton.DpadRight)) yield return x;
        VwCheck(_menu == MenuItem.Board, "pad: D-pad right reaches LEADERBOARDS");
        foreach (var x in VrPad(JoyButton.A)) yield return x;
        for (int i = 0; i < 8; i++) yield return null;
        VwCheck(_showBoard, "pad: A opens the board");
        foreach (var x in VrPad(JoyButton.B)) yield return x;
        for (int i = 0; i < 8; i++) yield return null;
        VwCheck(VwFilesRow() && _menu == MenuItem.Board && VwCalm(), $"pad: B closes the board back to KGB FILES on LEADERBOARDS (parent {_menuParent}, on {_menu})");
        foreach (var x in VrPad(JoyButton.DpadRight)) yield return x;
        foreach (var x in VrPad(JoyButton.A)) yield return x;
        foreach (var x in VrUntil(() => _recordingsOpen && _hud.ReplayListOpen && !_replayLooking, 60 * 10, "pad: A opens RECORDINGS")) yield return x;
        foreach (var x in VrPad(JoyButton.B)) yield return x;
        for (int i = 0; i < 8; i++) yield return null;
        VwCheck(VwFilesRow() && _menu == MenuItem.Recordings && VwCalm(), $"pad: B closes RECORDINGS back to KGB FILES on RECORDINGS (parent {_menuParent}, on {_menu})");
        // the pad's Back shortcut from the KGB FILES row: PARTY OFFICE on KGB FILES, no resignation
        foreach (var x in VrPad(JoyButton.Back)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_menuParent == MenuItem.Office && _menu == MenuItem.Files && VwCalm(), $"pad: Back in KGB FILES goes to PARTY OFFICE on KGB FILES, no resignation (parent {_menuParent}, on {_menu}, resign {_resignOut})");
        foreach (var x in VrPad(JoyButton.DpadRight)) yield return x;
        foreach (var x in VrPad(JoyButton.A)) yield return x;
        for (int i = 0; i < 8; i++) yield return null;
        VwCheck(_showLog, "pad: A opens the credits");
        foreach (var x in VrPad(JoyButton.B)) yield return x;
        for (int i = 0; i < 8; i++) yield return null;
        VwCheck(!_showLog && _menuParent == MenuItem.Office && _menu == MenuItem.Credits, "pad: B closes the credits back to PARTY OFFICE at CREDITS");
        foreach (var x in VrPad(JoyButton.B)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_menuParent == null && _menu == MenuItem.Office && VwCalm(), "pad: B goes up to the top row, no resignation");
        // the Back shortcut from the top row: PARTY OFFICE on KGB FILES (Back there would resign: not pressed), then B up again
        foreach (var x in VrPad(JoyButton.Back)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_menuParent == MenuItem.Office && _menu == MenuItem.Files && VwCalm(), $"pad: Back on the top row opens PARTY OFFICE on KGB FILES (on {_menu})");
        foreach (var x in VrPad(JoyButton.B)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_menuParent == null && _menu == MenuItem.Office && VwCalm() && _file == fileBefore, "pad: B back to the top row; the personnel file in use never changed");
        _padMode = false;
        for (int i = 0; i < 6 && _menu != MenuItem.Multi; i++) foreach (var x in VrTap(Key.Left)) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_menuParent == MenuItem.Multi && _hud.MenuLabels.SequenceEqual(new[] { "LAN / INTERNET" }), "MULTIPLAYER displays LAN / INTERNET");
        foreach (var x in VrShot("title-multiplayer")) yield return x;
        foreach (var x in VrTap(Key.Escape)) yield return x;
        VwCheck(_menuParent == null && _menu == MenuItem.Multi && VwCalm(), "ESC from LAN / INTERNET returns to MULTIPLAYER on the top row");
        VwCheck(true, $"(the title survived: state {_state}, resign asked {_resignOut})");
    }
}
