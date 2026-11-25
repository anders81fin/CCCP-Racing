using System;
using System.Collections;
using System.IO;
using System.Linq;
using CCCPRacing.Audio;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

// Windowed scenario "title-settings" (--viewer-windowed-only=titlesettings): the owner's main-menu order of 2026-11-19 in the real game on a fresh temporary profile: the first launch stands on SINGLE PLAYER > SOCIALIST RALLY with
// its goal line, SETTINGS is in the top row before any drive, its SOUNDS shows RADIO as a passive ON and a STATION row of six whose sixth, the menu's theme "HE KATSOIVAT LANTEEN", is the default; stepping it tunes the
// menu's music and is kept; Esc goes back one screen at a time. And the rally's card chain: Esc from the track card is one step back to the fuel, with the choices kept.
public partial class RaceScene
{
    IEnumerable VwTitleSettings()
    {
        foreach (var x in VwWaitReady()) yield return x;
        for (int i = 0; i < 600 && !(_state == State.Title && _card == TitleCard.None && !_showLog && !_askName); i++) yield return null;
        for (int i = 0; i < 90; i++) yield return null;
        VwCheck(_state == State.Title && _menuParent == null && _menu == MenuItem.Single, $"a first launch: the top row stands on SINGLE PLAYER ({_menu})");
        VwCheck(TopItems.SequenceEqual(new[] { MenuItem.Single, MenuItem.Multi, MenuItem.Settings, MenuItem.Office }) && _hud.MenuLabels.Length == 4, "the top row: SINGLE PLAYER, MULTIPLAYER, SETTINGS, PARTY OFFICE");
        foreach (var x in VrShot("titlesettings-top")) yield return x;

        // ---- SETTINGS before a drive
        foreach (var x in VrTap(Key.Right)) yield return x; foreach (var x in VrTap(Key.Right)) yield return x;
        VwCheck(_menu == MenuItem.Settings, "the cursor is on SETTINGS");
        foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(TitleSettingsOpen && _hud.SettingsOpen && _hud.SettingsRowIds.Contains(SettingsRowId.Sounds) && _hud.SettingsRowIds.Contains(SettingsRowId.Visuals) && !_hud.SettingsRowIds.Contains(SettingsRowId.LabTune), "SETTINGS opens over the title with the shared rows (SOUNDS, VISUALS), no run-only rows");
        foreach (var x in VrShot("titlesettings-settings")) yield return x;
        // LAB MODE is shown OFF and passive in the main menu
        {
            int labRow = Array.IndexOf(_hud.SettingsRowIds, SettingsRowId.Lab);
            VwCheck(labRow >= 0, "the SETTINGS rows have LAB MODE");
            if (labRow >= 0)
            {
                bool labWas = _labMode; int selWas = _hud.SettingsSelection;
                _hud.SettingsSelection = labRow;
                _hud.SettingsRowTexts(SettingsRowId.Lab, out _, out string labValue);
                foreach (var x in VrTap(Key.Enter)) yield return x; foreach (var x in VrTap(Key.Right)) yield return x; foreach (var x in VrTap(Key.Left)) yield return x;
                VwCheck(_hud.SettingsTitleMode && labValue == "OFF" && _hud.SettingsToggleValue(SettingsRowId.Lab) == null && _labMode == labWas, $"LAB MODE in the main menu: shown OFF ({labValue}), no switch, Enter and the arrows change nothing");
                foreach (var x in VrShot("titlesettings-lab-passive")) yield return x;
                _hud.SettingsSelection = selWas;
            }
        }
        foreach (var x in VrTap(Key.Space)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_titleMenuHidden && _hud.SettingsOpen, "Space hides the menu over the SETTINGS card (it stays open underneath)");
        foreach (var x in VrTap(Key.Space)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(!_titleMenuHidden && _hud.SettingsOpen, "and shows it again");
        // VISUALS: the HUD rows of a run are passive in the main menu; rendering, edges and softening stay active
        _hud.SettingsSelection = Array.IndexOf(_hud.SettingsRowIds, SettingsRowId.Visuals);
        foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_hud.VisualOpen && _hud.VisualTitleMode, "VISUALS opens from the main menu");
        foreach (var id in new[] { VisualRowId.Comments, VisualRowId.LapInfo, VisualRowId.Map, VisualRowId.CarInstruments })
        {
            int row = Array.IndexOf(_hud.VisualRowIds, id);
            if (row < 0) { VwCheck(false, $"VISUALS has the row {id}"); continue; }
            bool before = _hud.VisualToggleValue(id) == true;
            _hud.VisualSelection = row;
            foreach (var x in VrTap(Key.Enter)) yield return x; foreach (var x in VrTap(Key.Right)) yield return x; foreach (var x in VrTap(Key.Left)) yield return x;
            VwCheck(_hud.VisualRowPassive(id) && (_hud.VisualToggleValue(id) == true) == before, $"{id} is passive in the main menu: its state ({before}) does not change");
        }
        foreach (var id in new[] { VisualRowId.Rendering, VisualRowId.Edges, VisualRowId.Softening })
            VwCheck(!_hud.VisualRowPassive(id) && Array.IndexOf(_hud.VisualRowIds, id) >= 0, $"{id} stays active in the main menu");
        foreach (var x in VrShot("titlesettings-visuals-passive")) yield return x;
        foreach (var x in VrTap(Key.Escape)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        _hud.SettingsSelection = Array.IndexOf(_hud.SettingsRowIds, SettingsRowId.Sounds);
        foreach (var x in VrTap(Key.Enter)) yield return x;      // SOUNDS
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_hud.MixerOpen && _hud.MixerTitleMode && _hud.MixerLeadRows == 2, "SOUNDS from the main menu: the RADIO row and the STATION row");
        int count = _carRadio.StationCount;      // (the five songs and the theme as the sixth: the car's radio has it too)
        string theme = "HE KATSOIVAT LANTEEN";
        VwCheck(_hud.TitleStationCount == count && count == 6 && _hud.TitleStationNumber == 6 && _hud.TitleStationName == theme, $"six stations, the sixth ('{_hud.TitleStationName}', {_hud.TitleStationNumber} / {_hud.TitleStationCount}) is the default");
        foreach (var x in VrShot("titlesettings-sounds")) yield return x;
        // Space hides the menu over SETTINGS' screens too (the video can be watched while the music is set), back with Space
        foreach (var x in VrTap(Key.Space)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_titleMenuHidden && _hud.MixerOpen && _hud.TitleMenuHidden, "Space hides the menu over SOUNDS (the screen stays open underneath)");
        foreach (var x in VrTap(Key.Right)) yield return x;
        VwCheck(_titleMenuHidden, "while it is hidden the screen takes no keys");
        foreach (var x in VrTap(Key.Space)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(!_titleMenuHidden && _hud.MixerOpen, "Space shows SOUNDS again");
        // RADIO VOLUME is the menu's music level here
        {
            int volRow = _hud.MixerLeadRows + SoundBalance.Count;
            _hud.MixerSelection = volRow;
            int v0 = TitleVolume; float db0 = _radio.VolumeDb; int carVol = _carRadio.Volume;
            foreach (var x in VrTap(Key.Left)) yield return x;
            for (int i = 0; i < 4; i++) yield return null;
            VwCheck(TitleVolume == Math.Max(0, v0 - 1) && _carRadio.Volume == carVol && _radio.VolumeDb < db0 + 0.01f && (v0 == 0 || _radio.VolumeDb < db0), $"RADIO VOLUME in the main menu lowers the menu's music level ({v0} -> {TitleVolume}, {db0:F1} -> {_radio.VolumeDb:F1} dB), not the car's radio ({carVol} == {_carRadio.Volume})");
            string cfgV = File.Exists(_records.Dir + "settings.cfg") ? File.ReadAllText(_records.Dir + "settings.cfg") : "";
            VwCheck(cfgV.Contains("title_volume"), "the level is kept in settings.cfg [radio] title_volume");
            foreach (var x in VrTap(Key.Right)) yield return x;
            VwCheck(TitleVolume == v0, "Right puts it back");
            _hud.MixerSelection = 0;      // (back on RADIO for the passive check below)
        }
        // RADIO is passive here
        bool radioWas = _carRadio.On;
        foreach (var x in VrTap(Key.Enter)) yield return x; foreach (var x in VrTap(Key.Left)) yield return x; foreach (var x in VrTap(Key.Right)) yield return x;
        VwCheck(_carRadio.On == radioWas && _hud.MixerTitleMode, "RADIO cannot be changed in the main menu (Enter, Left, Right do nothing)");
        // STATION: down to row 1, Left tunes the fifth station
        foreach (var x in VrTap(Key.Down)) yield return x;
        VwCheck(_hud.MixerSelection == 1, "the cursor is on STATION");
        foreach (var x in VrTap(Key.Left)) yield return x;
        for (int i = 0; i < 20; i++) yield return null;
        string fifth = Ascii(_carRadio.StationName(4));
        VwCheck(_carRadio.StationCount == 6 && _carRadio.StationName(_carRadio.StationCount - 1) == "He katsoivat länteen", $"the car's radio has the theme as its sixth station too ({_carRadio.StationCount} stations, last '{(_carRadio.StationCount > 0 ? _carRadio.StationName(_carRadio.StationCount - 1) : "-")}')");
        VwCheck(_hud.TitleStationNumber == 5 && _hud.TitleStationName == fifth && _titleTuned == fifth, $"Left: station 5 ({_hud.TitleStationName}), the menu's music tuned to it ({_titleTuned})");
        string cfgPath = _records.Dir + "settings.cfg";
        string cfg = File.Exists(cfgPath) ? File.ReadAllText(cfgPath) : "";
        VwCheck(cfg.Contains("title_station"), "the choice is kept in settings.cfg [radio] title_station");
        foreach (var x in VrTap(Key.Right)) yield return x;
        for (int i = 0; i < 20; i++) yield return null;
        VwCheck(_hud.TitleStationNumber == 6 && _titleTuned == theme, "Right: back on the theme, tuned");
        // the balance rows play their demonstrations over the menu's video
        foreach (var x in VrTap(Key.Down)) yield return x;      // ENGINE AND GEARBOX (row 2: after RADIO and STATION)
        for (int i = 0; i < 30; i++) yield return null;
        float mix0 = _mixClock;
        foreach (var x in VrUntil(() => _mixClock > mix0 + 0.5f, 60 * 12, "the demonstration clock runs")) yield return x;
        VwCheck(_hud.MixerSelection == 2 && _mixClock > mix0 + 0.5f && _titleMixerSounds, $"ENGINE AND GEARBOX in the main menu: the demonstration runs (selection {_hud.MixerSelection}, clock {mix0:F2} -> {_mixClock:F2} s, sounds on {_titleMixerSounds})");
        int lvl0 = Audio.SoundBalance.Levels[0];
        foreach (var x in VrTap(Key.Right)) yield return x;
        VwCheck(Audio.SoundBalance.Levels[0] == Math.Min(Audio.SoundBalance.Max, lvl0 + 1), $"Right raises the level ({lvl0} -> {Audio.SoundBalance.Levels[0]}, selection {_hud.MixerSelection}, lead {_hud.MixerLeadRows}) and it is saved");
        foreach (var x in VrTap(Key.Left)) yield return x;
        // one screen back at a time
        foreach (var x in VrTap(Key.Escape)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(!_hud.MixerOpen && _hud.SettingsOpen, "Esc from SOUNDS: back on SETTINGS");
        for (int i = 0; i < 10; i++) yield return null;
        VwCheck(!_titleMixerSounds, "closing SOUNDS hands the menu's own sound back");
        foreach (var x in VrTap(Key.Escape)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(!_hud.SettingsOpen && _state == State.Title && _menu == MenuItem.Settings && _menuParent == null, "Esc from SETTINGS: the main menu, still on SETTINGS");

        // ---- SINGLE PLAYER > SOCIALIST RALLY with its goal line
        foreach (var x in VrTap(Key.Left)) yield return x; foreach (var x in VrTap(Key.Left)) yield return x;
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
        VwCheck(_card == TitleCard.Track && _rallyFuelPicked, "then the track card");
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

        // ---- SETTINGS and its screens step back with the pad's B (and Start, VIEW) too, as in the Party Meeting
        foreach (var x in VrTap(Key.Escape)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        _menu = MenuItem.Settings;
        PadButton(JoyButton.A, true, false); PadButton(JoyButton.A, false, false);
        for (int i = 0; i < 8; i++) yield return null;
        VwCheck(TitleSettingsOpen && _hud.SettingsOpen, "the pad's A opens SETTINGS over the title");
        foreach (var b in new[] { JoyButton.B, JoyButton.Start, JoyButton.Back })
        {
            _hud.SettingsSelection = Array.IndexOf(_hud.SettingsRowIds, SettingsRowId.Sounds);
            PadButton(JoyButton.A, true, false); PadButton(JoyButton.A, false, false);
            for (int i = 0; i < 8; i++) yield return null;
            VwCheck(_hud.MixerOpen, $"{b}: the pad's A opens SOUNDS");
            PadButton(b, true, false); PadButton(b, false, false);
            for (int i = 0; i < 8; i++) yield return null;
            VwCheck(!_hud.MixerOpen && _hud.SettingsOpen, $"the pad's {b} from SOUNDS: back on SETTINGS");
            _hud.SettingsSelection = Array.IndexOf(_hud.SettingsRowIds, SettingsRowId.Visuals);
            PadButton(JoyButton.A, true, false); PadButton(JoyButton.A, false, false);
            for (int i = 0; i < 8; i++) yield return null;
            VwCheck(_hud.VisualOpen, $"{b}: the pad's A opens VISUALS");
            PadButton(b, true, false); PadButton(b, false, false);
            for (int i = 0; i < 8; i++) yield return null;
            VwCheck(!_hud.VisualOpen && _hud.SettingsOpen, $"the pad's {b} from VISUALS: back on SETTINGS");
        }
        PadButton(JoyButton.B, true, false); PadButton(JoyButton.B, false, false);
        for (int i = 0; i < 8; i++) yield return null;
        VwCheck(!TitleSettingsOpen && _state == State.Title && _menu == MenuItem.Settings && _menuParent == null, "the pad's B from SETTINGS: the main menu, still on SETTINGS");

        // ---- a press in the first frame of a new screen is read (a Steam Deck draws a new screen's first frame for two seconds, and everything pressed meanwhile lands in the frame after it)
        // SETTINGS is open and the pad's context still says "something else" (the last frame's): the screen appeared after the pad code looked.
        PadButton(JoyButton.A, true, false); PadButton(JoyButton.A, false, false);
        for (int i = 0; i < 8; i++) yield return null;
        VwCheck(TitleSettingsOpen && _hud.SettingsOpen, "SETTINGS is open again");
        _hud.SettingsSelection = Array.IndexOf(_hud.SettingsRowIds, SettingsRowId.Sounds);
        _padCtx = PadCtx.Race;
        PadButton(JoyButton.A, true, false);      // (held: the key stays down while the game looks)
        for (int i = 0; i < 4; i++) yield return null;
        VwCheck(_hud.MixerOpen, "the pad's A in the first frame of a screen opens SOUNDS: the press is not let go of by the context catching up");
        PadButton(JoyButton.A, false, false);
        for (int i = 0; i < 6; i++) yield return null;
        PadButton(JoyButton.B, true, false); PadButton(JoyButton.B, false, false);
        for (int i = 0; i < 8; i++) yield return null;
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
