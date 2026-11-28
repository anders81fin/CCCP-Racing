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
        VwCheck(_hud.MenuLabels.SequenceEqual(new[] { "SINGLE PLAYER", "MULTIPLAYER", "PARTY OFFICE" }), $"top row (SETTINGS is only in the Party Meeting since 2026-11-28): {string.Join(" | ", _hud.MenuLabels)}");
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
        // KGB FILES is the archive itself: Enter opens it, with no row of its own
        foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 8; i++) yield return null;
        VwCheck(_showFiles && _hud.Mode == HudMode.Files && _hud.Files.Length == PlayerFiles.Count && !_filesAtLaunch && _menu == MenuItem.Files && _menuParent == MenuItem.Office, $"Enter on KGB FILES opens the archive directly (files {_hud.Files?.Length}, on {_menu}, parent {_menuParent})");
        VwCheck(_hud.MenuHint is ("KGB FILES", _), "the hint box was KGB FILES' own");
        foreach (var x in VrShot("title-kgb-files")) yield return x;
        var size = GetViewport().GetVisibleRect().Size;
        foreach (var x in VrShot("title-personnel")) yield return x;
        foreach (var x in VrTap(Key.Down)) yield return x;
        VwCheck(_showFiles && _filesSel == fileBefore % PlayerFiles.Count, $"Down moves in the files ({_filesSel})");
        foreach (var x in VrClick(new Vector2(size.X / 2, size.Y / 2))) yield return x;
        VwCheck(_showFiles && !_askName && _file == fileBefore, "mouse: a click in PERSONNEL neither closes it nor takes a file into use");
        // the recordings count replaces the camera count on a row, and R looks into the selected driver's recordings and comes back
        for (int i = 0; i < 120 && _fileRecordingsTask != null; i++) yield return null;
        VwCheck(_hud.Files.All(f => f.Recordings >= 0) && _hud.Files.Length == PlayerFiles.Count, $"every file's recordings are counted ({string.Join(",", _hud.Files.Select(f => f.Recordings))})");
        _filesSel = _file - 1; FilesHud();
        VwCheck(_hud.Files[_file - 1].Recordings == 0, "this fresh profile has no recordings");
        foreach (var x in VrTap(Key.R)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_showFiles && _hud.Mode == HudMode.Files && !_recordingsOpen && _recFileView == 0, "R on a driver without recordings says so and the archive stays (the recordings list is not opened)");
        foreach (var x in VrShot("title-files-r-none")) yield return x;
        foreach (var x in VrTap(Key.Escape)) yield return x;
        for (int i = 0; i < 8; i++) yield return null;
        VwCheck(!_showFiles && _menuParent == MenuItem.Office && _menu == MenuItem.Files && VwCalm() && _file == fileBefore, $"ESC from the archive returns to PARTY OFFICE on KGB FILES, the file in use unchanged (parent {_menuParent}, on {_menu})");
        // KGB FILES has no LEADERBOARDS row any more (CHOOSE TRACK and the Party Meeting have them); the office board itself is as it was (opened here by the code the other paths use)
        VwCheck(!_hud.MenuLabels.Contains("LEADERBOARDS") && _hud.MenuLabels.SequenceEqual(new[] { "KGB FILES", "CREDITS", "RESIGN" }), "PARTY OFFICE has no LEADERBOARDS row (KGB FILES, CREDITS, RESIGN)");
        EnterBoard();
        for (int i = 0; i < 8; i++) yield return null;
        VwCheck(_showBoard && _hud.Mode == HudMode.Board, "the office board of honour opens");
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
        VwCheck(!_showBoard && _hud.Mode == HudMode.Title, $"ESC closes the office board ({_menu})");
        _menu = MenuItem.Files; _menuParent = MenuItem.Office;      // (the board was opened from outside the row: the row stands where it was)
        // (CREDITS next: KGB FILES has no row any more, so PARTY OFFICE is where the cursor already stands)
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
        VwCheck(_showFiles && _hud.Mode == HudMode.Files, "pad: A on KGB FILES opens the archive directly");
        foreach (var x in VrPad(JoyButton.B)) yield return x;
        for (int i = 0; i < 8; i++) yield return null;
        VwCheck(!_showFiles && _menuParent == MenuItem.Office && _menu == MenuItem.Files && VwCalm() && _file == fileBefore, $"pad: B closes the archive back to PARTY OFFICE on KGB FILES (parent {_menuParent}, on {_menu})");
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
        // the Back shortcut from the top row: PARTY OFFICE with RESIGN selected (Back there would resign: not pressed), then B up again
        foreach (var x in VrPad(JoyButton.Back)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_menuParent == MenuItem.Office && _menu == MenuItem.Quit && VwCalm(), $"pad: Back on the top row opens PARTY OFFICE with RESIGN selected, nothing resigned (on {_menu})");
        foreach (var x in VrPad(JoyButton.B)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_menuParent == null && _menu == MenuItem.Office && VwCalm() && _file == fileBefore, "pad: B back to the top row; the personnel file in use never changed");
        _padMode = false;
        // Esc on the top row: PARTY OFFICE opens with RESIGN selected (no resignation), a second Esc goes back to the top row
        foreach (var x in VrTap(Key.Escape)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_menuParent == MenuItem.Office && _menu == MenuItem.Quit && VwCalm(), $"Esc on the top row opens PARTY OFFICE with RESIGN selected, nothing resigned (on {_menu}, resign {_resignOut})");
        foreach (var x in VrTap(Key.Escape)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_menuParent == null && _menu == MenuItem.Office && VwCalm(), "a second Esc goes back to the top row");
        for (int i = 0; i < 6 && _menu != MenuItem.Multi; i++) foreach (var x in VrTap(Key.Left)) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_menuParent == null && _lanPage != LanPage.None, $"MULTIPLAYER goes straight to the LAN race's start (page {_lanPage}, parent {_menuParent})");
        foreach (var x in VrShot("title-multiplayer")) yield return x;
        foreach (var x in VrTap(Key.Escape)) yield return x;
        VwCheck(_menuParent == null && _menu == MenuItem.Multi && VwCalm(), "ESC from the LAN start returns to MULTIPLAYER on the top row");
        VwCheck(true, $"(the title survived: state {_state}, resign asked {_resignOut})");
    }
}
