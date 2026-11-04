using System;
using System.Collections;
using System.Linq;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

// Windowed scenario "title-menu" (--viewer-windowed-only=titlemenu): PARTY OFFICE's ABOUT THE GAME row on the real title through the real Main.tscn/PixelFrame with injected keyboard and pad events: the third row
// (CHANGELOG, CREDITS, BACK), the back chain CHANGELOG/CREDITS -> ABOUT THE GAME -> PARTY OFFICE -> top row, and that nothing in it leaves the title or hands in a resignation.
public partial class RaceScene
{
    IEnumerable VwTitleMenu()
    {
        foreach (var x in VwWaitReady()) yield return x;
        for (int i = 0; i < 600 && !(_state == State.Title && _card == TitleCard.None && !_showLog && !_askName); i++) yield return null;
        for (int i = 0; i < 60; i++) yield return null;
        VwCheck(_state == State.Title && _menuParent == null, $"the title's top row is up ({_state}, parent {_menuParent})");
        // to PARTY OFFICE
        for (int i = 0; i < 6 && _menu != MenuItem.Office; i++) foreach (var x in VrTap(Key.Right)) yield return x;
        VwCheck(_menu == MenuItem.Office, "the cursor is on PARTY OFFICE");
        foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_menuParent == MenuItem.Office && _hud.MenuLabels.SequenceEqual(new[] { "KGB FILES", "LEADERBOARD", "RECORDINGS", "CREDITS", "RESIGN" }), $"PARTY OFFICE row: {string.Join(" | ", _hud.MenuLabels)}");
        foreach (var x in VrShot("title-office")) yield return x;
        // CREDITS and back
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
        VwCheck(_menuParent == null && _menu == MenuItem.Office && _state == State.Title && _resignOut <= 0f && !_askName, "ESC in PARTY OFFICE goes to the top row on PARTY OFFICE");
        // the pad: A opens, B goes back one level at a time
        _padMode = true;
        foreach (var x in VrPad(JoyButton.A)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_menuParent == MenuItem.Office, "pad: A opens PARTY OFFICE");
        for (int i = 0; i < 6 && _menu != MenuItem.Credits; i++) foreach (var x in VrPad(JoyButton.DpadRight)) yield return x;
        VwCheck(_menu == MenuItem.Credits, "pad: D-pad right reaches CREDITS");
        foreach (var x in VrPad(JoyButton.A)) yield return x;
        for (int i = 0; i < 8; i++) yield return null;
        VwCheck(_showLog, "pad: A opens the credits");
        foreach (var x in VrPad(JoyButton.B)) yield return x;
        for (int i = 0; i < 8; i++) yield return null;
        VwCheck(!_showLog && _menuParent == MenuItem.Office, "pad: B closes the credits back to PARTY OFFICE");
        foreach (var x in VrPad(JoyButton.B)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_menuParent == null && _state == State.Title && _resignOut <= 0f, "pad: B goes up to the top row, no resignation");
        VwCheck(true, $"(the title survived: state {_state}, resign asked {_resignOut})");
    }
}
