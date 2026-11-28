using System;
using System.Collections;
using System.IO;
using System.Linq;
using CCCPRacing.Audio;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

// Windowed scenario "title-settings" (--viewer-windowed-only=titlesettings): the owner's main-menu order of 2026-11-19 in the real game on a fresh temporary profile: the first launch stands on SINGLE PLAYER > SOCIALIST RALLY with
// its goal line; the top row has no SETTINGS (the owner's order of 2026-11-28: SETTINGS is only in the Party Meeting); Esc goes back one screen at a time. And the rally's card chain: Esc from the track card is one step back to the fuel, with the choices kept.
public partial class RaceScene
{
    IEnumerable VwTitleSettings()
    {
        foreach (var x in VwWaitReady()) yield return x;
        for (int i = 0; i < 600 && !(_state == State.Title && _card == TitleCard.None && !_showLog && !_askName); i++) yield return null;
        for (int i = 0; i < 90; i++) yield return null;
        VwCheck(_state == State.Title && _menuParent == null && _menu == MenuItem.Single, $"a first launch: the top row stands on SINGLE PLAYER ({_menu})");
        VwCheck(TopItems.SequenceEqual(new[] { MenuItem.Single, MenuItem.Multi, MenuItem.Office }) && _hud.MenuLabels.Length == 3 && !_hud.MenuLabels.Contains("SETTINGS"), "the top row: SINGLE PLAYER, MULTIPLAYER, PARTY OFFICE (no SETTINGS: it is only in the Party Meeting)");
        foreach (var x in VrShot("titlesettings-top")) yield return x;

        // ---- SINGLE PLAYER > SOCIALIST RALLY with its goal line
        foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_menuParent == MenuItem.Single && _menu == MenuItem.Race, "SINGLE PLAYER opens on SOCIALIST RALLY");
        VwCheck(_hud.MenuHint is var (ht, hl) && hl.Contains("BEAT LAP NORMS TO EARN MEDALS.") && hl.Length >= 4, "the rally's hint has the goal line and keeps its jokes");
        foreach (var x in VrShot("titlesettings-rally-hint")) yield return x;

        // ---- the card chain: Esc is one step back, the choices kept
        foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_card == TitleCard.RallyFuel && _rallyFuelFirst, "the rally asks for the fuel first");
        int fuelSel = _cardSel;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_carTourOpen && _card == TitleCard.RallyFuel && _ctThenChoose, "a fuel chosen for the first time on the profile plays its INFO before the track card");
        foreach (var x in VrTap(Key.Escape)) yield return x;      // ESC SKIP
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_card == TitleCard.Track && _rallyFuelPicked && !_carTourOpen, "then the track card");
        foreach (var x in VrTap(Key.Right)) yield return x;
        int trackSel = _cardSel;
        foreach (var x in VrTap(Key.Escape)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_card == TitleCard.RallyFuel && _cardSel == fuelSel && _rallyFuelFirst && _menuParent == MenuItem.Single, $"Esc on the track card: ONE step back, the fuel card with the fuel chosen selected ({_cardSel} == {fuelSel})");
        foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_card == TitleCard.Track && _cardSel == trackSel, $"forward again: the track card is on the track it was left on ({_cardSel} == {trackSel})");
        foreach (var x in VrTap(Key.Escape)) yield return x;
        foreach (var x in VrTap(Key.Escape)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_card == TitleCard.None && _menuParent == MenuItem.Single && _menu == MenuItem.Race, "Esc twice: the fuel card, then the menu's SOCIALIST RALLY row");

        // ---- the menu hides to show the background video: Space (keys) or RB (pad), back with the same key or any pad button
        VwCheck(_state == State.Title && _card == TitleCard.None && !_titleMenuHidden, "the menu is up");
        foreach (var x in VrShot("titlesettings-hint")) yield return x;
        foreach (var x in VrTap(Key.Space)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_titleMenuHidden && _hud.TitleMenuHidden, "Space hides the menu");
        // the title has no poster colour filter any more; the bottom line stays with the menu hidden
        VwCheck(_saturation > 0.99f, $"the world is in its own colour (saturation {_saturation:0.00}): the poster filter is off");
        foreach (var x in VrShot("titlesettings-hidden")) yield return x;
        VwCheck(_saturation > 0.99f, "the hidden menu's world has no poster filter");
        foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(!_titleMenuHidden && _card == TitleCard.None, "Enter on the hidden menu only brings it back (nothing starts)");
        VwCheck(_saturation > 0.99f, "and it stays in colour when the menu is back (no poster filter)");
        foreach (var x in VrTap(Key.Space)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_titleMenuHidden, "Space hides it again");
        foreach (var x in VrTap(Key.Space)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(!_titleMenuHidden, "Space again shows it");
        foreach (var x in VrTap(Key.Space)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        foreach (var x in VrTap(Key.Escape)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(!_titleMenuHidden && _resignOut < 0, "Esc on the hidden menu brings it back, no resignation question");
        PadButton(JoyButton.RightShoulder, true, false); PadButton(JoyButton.RightShoulder, false, false);
        for (int i = 0; i < 8; i++) yield return null;
        VwCheck(_titleMenuHidden, "the pad's RB hides the menu");
        PadButton(JoyButton.A, true, false); PadButton(JoyButton.A, false, false);
        for (int i = 0; i < 8; i++) yield return null;
        VwCheck(!_titleMenuHidden && _card == TitleCard.None, "any pad button (A) brings it back and starts nothing");

        // ---- over a mode's card too: the menu is in a submenu here (SINGLE PLAYER) and a card opens over it
        VwCheck(_menuParent == MenuItem.Single, "the hide tests above ran in a submenu (SINGLE PLAYER's rows)");
        foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_card == TitleCard.RallyFuel, "the fuel card is open");
        foreach (var x in VrTap(Key.Space)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_titleMenuHidden && _card == TitleCard.RallyFuel && _hud.TitleMenuHidden, "Space hides the menu and its card too");
        foreach (var x in VrShot("titlesettings-card-hidden")) yield return x;
        foreach (var x in VrTap(Key.Left)) yield return x;
        VwCheck(_titleMenuHidden, "while it is hidden the card takes no keys");
        foreach (var x in VrTap(Key.Space)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(!_titleMenuHidden && _card == TitleCard.RallyFuel, "Space shows them again, the card where it was");
        foreach (var x in VrTap(Key.Escape)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_card == TitleCard.None && _menuParent == MenuItem.Single, "Esc closes the card");
        // PARTY OFFICE's row too
        foreach (var x in VrTap(Key.Escape)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_menuParent == null, "Esc: the top row");
        _menu = MenuItem.Office; foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_menuParent == MenuItem.Office, "PARTY OFFICE's row is open");
        foreach (var x in VrTap(Key.Space)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_titleMenuHidden, "Space hides PARTY OFFICE's row too");
        foreach (var x in VrTap(Key.Space)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(!_titleMenuHidden && _menuParent == MenuItem.Office, "and shows it again");

        VwCardTextWidths();      // (last: it opens medals on this temporary profile)
    }

    /// <summary>Every title card's texts fit the narrowest screen the game is drawn on (1366x768 is 456 px wide): the card is as wide as its longest text plus 40 px and never wider than the
    /// screen less 8 px, and it does not wrap, so a longer text would be cut off. Every card, every highlighted row, the keyboard's and the pad's words, the track card of the rally, the laps and
    /// the LAN, locked and open (HudSchool.cs draws the card).</summary>
    void VwCardTextWidths()
    {
        const int Screen = 456, Room = Screen - 48, KeysRoom = Screen - 8 - 16;      // (the text room inside the card; the key line's room when the leaderboards are offered)
        var saved = (_card, _cardSel, _padMode, _trackFor, _hud.Pad, _savedRallyFuel);
        int cases = 0, worst = 0; string worstText = "";
        var over = new System.Collections.Generic.List<string>();
        void Look(string what, string text, int limit)
        {
            if (string.IsNullOrEmpty(text)) return;
            int w = PixelFont.Measure(text); cases++;
            if (w > worst) { worst = w; worstText = $"{what}: {text}"; }
            if (w > limit && over.Count < 8) over.Add($"{what} ({w} px of {limit}): {text}");
        }
        void Sweep(string state)
        {
            foreach (TitleCard c in Enum.GetValues(typeof(TitleCard)))
            {
                if (c == TitleCard.None) continue;
                foreach (var forWhat in c == TitleCard.Track ? new[] { MenuItem.Race, MenuItem.Training, MenuItem.Lan } : new[] { MenuItem.Race })
                foreach (bool pad in new[] { false, true })
                foreach (var resumeFuel in c == TitleCard.RallyResume ? new[] { FuelGrade.Diesel, FuelGrade.Petrol, FuelGrade.Rocket, FuelGrade.Sputnik } : new[] { FuelGrade.Diesel })
                {
                    _card = c; _trackFor = forWhat; _padMode = pad; _hud.Pad = pad; _savedRallyFuel = resumeFuel;
                    for (int sel = 0; sel < CardCount; sel++)
                    {
                        _cardSel = sel; CardHud();
                        string what = $"{state} {c}{(c == TitleCard.Track ? "/" + forWhat : "")} row {sel}{(pad ? " pad" : "")}";
                        Look(what + " title", _hud.CardTitle, Room);
                        Look(what + " sub", _hud.CardSub, Room);
                        Look(what + " note", _hud.CardNote, Room);
                        Look(what + " keys", _hud.CardBoards ? _hud.CardKeysLine() : null, KeysRoom);
                        int opts = 0; foreach (var o in _hud.CardOptions) opts += PixelFont.Measure(o) + 28;      // (the smallest letters and gaps the card falls back to)
                        if (opts > Room && over.Count < 8) over.Add($"{what} options ({opts} px of {Room})");
                    }
                }
            }
        }
        Sweep("fresh");
        foreach (var t in FuelTracks(FuelGrade.Petrol)) _campaign.Award(t, NormPlan.Medal.Silver, FuelGrade.Diesel);
        foreach (var t in FuelTracks(FuelGrade.Rocket)) _campaign.Award(t, NormPlan.Medal.Silver, FuelGrade.Petrol);
        foreach (var t in Tracks) { _campaign.PresentLaps(t.id, false); _campaign.PresentLaps(t.id, true); }
        Sweep("medals");
        (_card, _cardSel, _padMode, _trackFor, _hud.Pad, _savedRallyFuel) = saved; CardHud();
        VwLog("card-text-widths", ("texts", cases), ("widest_px", worst), ("widest", worstText));
        VwCheck(cases > 60 && over.Count == 0, $"every title card text fits the narrowest screen ({cases} texts, widest {worst} px of {Room}: {worstText}){(over.Count > 0 ? "; TOO WIDE: " + string.Join(" | ", over) : "")}");
    }

}
