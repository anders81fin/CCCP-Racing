using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

// Windowed checks of CHOOSE TRACK's L LEADERBOARDS (RaceScene.TrackCardBoards.cs), run on the real title inside "rally-start" (fresh) and
// "stakhanov-start" (--viewer-windowed-only=rallystart,stakhanovstart). Fixture boards on the temporary profile name their own track, mode and fuel
// (S/A/B/I = SIBERIA/ATOMGRAD-4/POLIGON 17/KOLKHOZ, R/L = rally/laps, P/D/J/S/X = petrol/diesel/jet/sputnik/unknown legacy power), so a row from
// another track, mode or fuel on a scoped board is visible by name. The highlighted card is never the built track first; locked tracks may be read.
public partial class RaceScene
{
        /// <summary>The fixture rows (only ever on a /tmp test profile): other tracks on file, the built KOLKHOZ in memory.</summary>
    bool VtbFixtures()
    {
        string real = ProjectSettings.GlobalizePath(_records.Dir);
        bool temp = real.StartsWith("/tmp/cccp-win-") && _trackId == "ilyich";
        VwCheck(temp, $"the fixture boards go to the temporary test profile only ({real}, built {_trackId})");
        if (!temp) return false;
        void Board(string track, int set, params (string name, float time, int power)[] rows)
        {
            var b = new Leaderboard(set == 1 ? Leaderboard.StakhanovSize : Leaderboard.Size, set == 1) { Path = RecordStore.PathsOnly(_records.Dir, track).BoardForSet(set) };
            foreach (var (name, time, power) in rows) b.Insert(name, time, "2026-11-07", power);
            b.Save();
        }
        int P = Training.PercentForKey(2), D = Training.PercentForKey(1), S = Training.PercentForKey(4);
        // SIBERIA: the rally's PETROL has four rows (three fit), DIESEL one, JET FUEL none, SPUTNIK two;
        // the laps' PETROL twelve offered (ten fit), DIESEL two, JET FUEL none, SPUTNIK one, and two of an unknown legacy power (150 %) that belong to no fuel.
        Board("siberia", 0, ("SRP1", 61f, 0), ("SRP2", 62f, 0), ("SRP3", 63f, 0), ("SRP4", 64f, 0));
        Board("siberia", 2, ("SRD1", 70f, 0));
        Board("siberia", 4, ("SRS1", 55f, 0), ("SRS2", 56f, 0));
        Board("siberia", 1, Enumerable.Range(1, 12).Select(i => ($"SLP{i:00}", 80f + i, P))
            .Concat(new[] { ("SLD1", 90f, D), ("SLD2", 91f, D), ("SLS1", 75f, S), ("SLX1", 70f, 150), ("SLX2", 71f, 150) }).ToArray());
        // ATOMGRAD-4: rally rows only (its laps' boards must stay empty); POLIGON 17: laps rows only (its rally boards must stay empty).
        Board("atomgrad", 0, ("ARP1", 65f, 0));
        Board("bonus", 1, ("BLP1", 77f, P));
        // KOLKHOZ (built): rows in memory on the rally's PETROL and the laps' PETROL (the runs here complete no PETROL lap).
        _boards[0].Insert("IRP1", 66f, "2026-11-07"); _boards[0].Insert("IRP2", 67f, "2026-11-07");
        _boards[1].Insert("ILP1", 85f, "2026-11-07", P);
        _otherBoards.Clear();      // (read again from the files just written)
        return true;
    }

    static string VtbNames(IEnumerable<Leaderboard.Entry> rows) => string.Join(",", rows.Select(e => e.Name));

    /// <summary>The scoped board's groups as "FUEL:rows;...".</summary>
    string VtbShown() => string.Join(";", _hud.BoardFuels.Select(f => f.Fuel + ":" + VtbNames(f.Entries)));

    bool VtbOpen(int track, bool laps) => _showBoard && BoardScoped && _hud.Mode == HudMode.Board && _hud.BoardScoped && _hud.BoardStakhanov == laps
        && _hud.BoardTrack == Tracks[track].name && _hud.BoardSelected == -1 && _hud.BoardReplayNotice == "" && _card == TitleCard.Track && _cardSel == track;

    /// <summary>Back on the same CHOOSE TRACK, highlight and mode, nothing chosen or started, the saved files and the car's fuel untouched.</summary>
    bool VtbBack(int track, MenuItem mode, Dictionary<string, string> files, int power, string rallyFile) => !_showBoard && !_boardScoped && _hud.Mode == HudMode.Title
        && _card == TitleCard.Track && _cardSel == track && _trackFor == mode && _menu == mode && _menuParent == MenuItem.Single && VwNothingStarted()
        && !_tourOpen && _power == power && _lapsFuelLast == null && VwRallyFuelOnFile() == rallyFile && VwDiff(files, VrFiles()) == "" && _hud.CardTitle == "CHOOSE TRACK";

    IEnumerable VtbTo(int track, bool pad)
    {
        for (int i = 0; i < 6 && _cardSel != track; i++)
            foreach (var x in pad ? VrPad(_cardSel < track ? JoyButton.DpadRight : JoyButton.DpadLeft) : VrTap(_cardSel < track ? Key.Right : Key.Left)) yield return x;
        VwCheck(_card == TitleCard.Track && _cardSel == track, $"CHOOSE TRACK highlights {Tracks[track].name} (sel {_cardSel})");
    }

    /// <summary>On an open CHOOSE TRACK of this mode (the built KOLKHOZ highlighted): the L / X boards of other highlighted tracks and back; ends on KOLKHOZ.</summary>
    IEnumerable VtbTrackCardBoards(bool laps)
    {
        var mode = laps ? MenuItem.Training : MenuItem.Race;
        string tag = laps ? "stakhanov" : "rally";
        if (!VtbFixtures()) yield break;
        _padMode = false;
        for (int i = 0; i < 4; i++) yield return null;
        string keysKb = "< > CHOOSE   ENTER OK   T TOUR   L LEADERBOARDS   ESC BACK";
        VwCheck(_hud.CardBoards && _hud.CardTour && _hud.CardKeysDrawn == keysKb, $"{tag}: CHOOSE TRACK offers L LEADERBOARDS beside T TOUR ('{_hud.CardKeysDrawn}')");
        foreach (var x in VrShot($"trackcard-{tag}-footer-keys")) yield return x;
        var files = VrFiles(); int power = _power; string rallyFile = VwRallyFuelOnFile(); int officePage = _boardPage, officeTrack = _boardTrack;
        var fuelBefore = _rallyFuel;

        // 1. SIBERIA (not the built track; locked on a fresh profile): only its own mode's boards
        foreach (var x in VtbTo(1, false)) yield return x;
        bool locked = _hud.CardLocked != null && _hud.CardLocked[1];
        foreach (var x in VrTap(Key.L)) yield return x;
        for (int i = 0; i < 4; i++) yield return null;
        VwCheck(VtbOpen(1, laps) && _menu == mode && _trackFor == mode, $"{tag}: L opens SIBERIA's {(laps ? "STAKHANOV LAPS" : "SOCIALIST RALLY")} boards in place, the card behind it unchanged (locked {locked}, '{_hud.BoardTrack}', stakhanov {_hud.BoardStakhanov})");
        if (laps)
        {
            VwCheck(_hud.BoardFuel == "DIESEL" && VtbShown() == "DIESEL - TOP 10:SLD1,SLD2" && VtbNames(_hud.Board) == VtbNames(_hud.BoardFuels[0].Entries),
                $"stakhanov: always opens on SIBERIA's DIESEL board ({VtbShown()})");
            foreach (var x in VrShot("trackcard-stakhanov-siberia-diesel")) yield return x;
            var seen = new List<string>();
            foreach (var want in new[] { ("PETROL", string.Join(",", Enumerable.Range(1, 10).Select(i => $"SLP{i:00}"))), ("JET FUEL", ""), ("SPUTNIK", "SLS1"), ("DIESEL", "SLD1,SLD2") })
            {
                foreach (var x in VrTap(Key.Right)) yield return x;
                for (int i = 0; i < 2; i++) yield return null;
                seen.Add(VtbShown());
                VwCheck(_hud.BoardFuel == want.Item1 && VtbShown() == $"{want.Item1} - TOP 10:{want.Item2}" && VtbOpen(1, true),
                    $"stakhanov: Right browses to {want.Item1} on SIBERIA ({VtbShown()})");
                if (want.Item1 == "JET FUEL") foreach (var x in VrShot("trackcard-stakhanov-siberia-jet-empty")) yield return x;
            }
            VwCheck(_hud.BoardFuel == "DIESEL" && !seen.Any(s => s.Contains("SLX") || s.Contains("SR") || s.Contains("IL")), $"stakhanov: fuel order Diesel, Petrol, Jet Fuel, Sputnik wraps to Diesel ({string.Join(" | ", seen)})");
            foreach (var x in VrTap(Key.Left)) yield return x;
            VwCheck(_hud.BoardFuel == "SPUTNIK", $"stakhanov: Left wraps back to Sputnik ({_hud.BoardFuel})");
            foreach (var x in VrTap(Key.Right)) yield return x;
        }
        else
        {
            VwCheck(VtbShown() == "DIESEL:SRD1;PETROL:SRP1,SRP2,SRP3;JET FUEL:;SPUTNIK:SRS1,SRS2" && _hud.Board.Count == 0,
                $"rally: SIBERIA's four fuel boards, three places each, empty JET FUEL empty; no KOLKHOZ, laps or other track rows ({VtbShown()})");
            foreach (var x in VrShot("trackcard-rally-siberia")) yield return x;
        }
        // 2. nothing else reaches through: no other track or mode, row, replay, tour, fuel key or shortcut, no mouse row
        string shown = VtbShown(), fuel = _hud.BoardFuel;
        foreach (var k in new[] { Key.W, Key.S, Key.Pageup, Key.Pagedown, Key.Up, Key.Down, Key.T, Key.L, Key.Key1, Key.Key3, Key.R, Key.C, Key.M, Key.Tab, Key.Space })
            foreach (var x in VrTap(k)) yield return x;
        if (!laps) foreach (var k in new[] { Key.Left, Key.Right, Key.A, Key.D }) foreach (var x in VrTap(k)) yield return x;
        var size = GetViewport().GetVisibleRect().Size;
        foreach (var x in VrClick(new Vector2(size.X / 2, size.Y / 2))) yield return x;
        for (int i = 0; i < 10; i++) yield return null;
        VwCheck(VtbOpen(1, laps) && VtbShown() == shown && _hud.BoardFuel == fuel && _replayViewer == null && _boardReplayOpen == null && !_tourOpen && _power == power && VwDiff(files, VrFiles()) == "",
            $"{tag}: W/S, PageUp/Down, Up/Down, T, L, number keys, R/C/M/Tab/Space{(laps ? "" : ", Left/Right")} and a mouse click change nothing: same track, mode and fuel, no replay or tour, no file written");
        // 3. Esc: back to the same card
        foreach (var x in VrTap(Key.Escape)) yield return x;
        for (int i = 0; i < 10; i++) yield return null;
        VwCheck(VtbBack(1, mode, files, power, rallyFile) && (_hud.CardLocked?[1] ?? false) == locked, $"{tag}: Esc returns to CHOOSE TRACK on SIBERIA, {mode}, nothing chosen, started or saved ({_card}, sel {_cardSel}, {_trackFor}, files {VwDiff(files, VrFiles())})");

        // 4. the other mode's rows only: an empty board stays empty (no fallback to the mode that has rows)
        int other = laps ? 2 : 3;      // ATOMGRAD-4 has rally rows only, POLIGON 17 laps rows only
        foreach (var x in VtbTo(other, false)) yield return x;
        foreach (var x in VrTap(Key.L)) yield return x;
        for (int i = 0; i < 4; i++) yield return null;
        bool empty = VtbOpen(other, laps) && _hud.BoardFuels.All(f => f.Entries.Count == 0) && _hud.Board.Count == 0;
        if (laps)
            for (int f = 0; f < 4; f++) { foreach (var x in VrTap(Key.Right)) yield return x; empty &= VtbOpen(other, true) && _hud.BoardFuels.Single().Entries.Count == 0; }
        VwCheck(empty, $"{tag}: {Tracks[other].name}'s {(laps ? "laps" : "rally")} boards are empty on every fuel and stay so: no fallback to its {(laps ? "rally" : "laps")} rows ({VtbShown()})");
        foreach (var x in VrShot($"trackcard-{tag}-{Tracks[other].id}-empty")) yield return x;
        // 5. the pad: X opens, the d-pad browses (laps), B closes, a second X opens again, A closes; no key is left held
        _padMode = true;
        foreach (var x in VrPad(JoyButton.B)) yield return x;
        for (int i = 0; i < 10; i++) yield return null;
        VwCheck(VtbBack(other, mode, files, power, rallyFile) && _padKeys.Count == 0, $"{tag}: pad B returns to CHOOSE TRACK on {Tracks[other].name} (pad keys held {_padKeys.Count})");
        for (int i = 0; i < 4; i++) yield return null;
        VwCheck(_hud.CardKeysDrawn == "< > CHOOSE   A OK   " + (_hud.CardTour ? "Y TOUR   " : "") + "X LEADERBOARDS   B BACK", $"{tag}: the pad's card offers X LEADERBOARDS ('{_hud.CardKeysDrawn}')");
        foreach (var x in VrShot($"trackcard-{tag}-footer-pad")) yield return x;
        foreach (var x in VrPad(JoyButton.X)) yield return x;
        for (int i = 0; i < 4; i++) yield return null;
        VwCheck(VtbOpen(other, laps) && (!laps || _hud.BoardFuel == "DIESEL") && _padKeys.Count == 0, $"{tag}: pad X opens the highlighted {Tracks[other].name}'s boards (pad keys held {_padKeys.Count})");
        if (laps)
        {
            string f0 = _hud.BoardFuel;
            foreach (var x in VrPad(JoyButton.DpadRight)) yield return x;
            VwCheck(_hud.BoardFuel != f0 && VtbOpen(other, true), $"stakhanov: the d-pad browses the fuel ({f0} -> {_hud.BoardFuel})");
        }
        foreach (var k in new[] { JoyButton.Y, JoyButton.X, JoyButton.LeftShoulder, JoyButton.RightShoulder, JoyButton.DpadUp, JoyButton.DpadDown, JoyButton.Back })
            foreach (var x in VrPad(k)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(VtbOpen(other, laps) && !_tourOpen && _replayViewer == null && _padKeys.Count == 0 && _resignOut < 0f, $"{tag}: pad Y, X, LB/RB, up/down and Back change nothing (open {BoardScoped}, tour {_tourOpen}, resign {_resignOut})");
        foreach (var x in VrPad(JoyButton.B)) yield return x;
        for (int i = 0; i < 10; i++) yield return null;
        VwCheck(VtbBack(other, mode, files, power, rallyFile), $"{tag}: pad B closes it again");
        _padMode = false;

        // 6. the built KOLKHOZ: its own in-memory rows; Enter closes without choosing the track (it is open: a leaked Enter would go on to the next card)
        foreach (var x in VtbTo(0, false)) yield return x;
        foreach (var x in VrTap(Key.L)) yield return x;
        for (int i = 0; i < 4; i++) yield return null;
        VwCheck(VtbOpen(0, laps) && (laps ? VtbShown() == "DIESEL - TOP 10:" : VtbShown() == "DIESEL:;PETROL:IRP1,IRP2;JET FUEL:;SPUTNIK:"), $"{tag}: KOLKHOZ (built) shows its own {(laps ? "laps" : "rally")} rows only ({VtbShown()})");
        foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 20; i++) yield return null;
        VwCheck(VtbBack(0, mode, files, power, rallyFile), $"{tag}: Enter closes the boards back to CHOOSE TRACK on KOLKHOZ without choosing it ({_card}, sel {_cardSel})");
        // 7. T TOUR still opens (the built track: no rebuild) and Esc closes it; L works again after it
        foreach (var x in VrTap(Key.T)) yield return x;
        for (int i = 0; i < 20; i++) yield return null;
        VwCheck(_tourOpen && _card == TitleCard.Track && !_showBoard, $"{tag}: T still opens KOLKHOZ's tour (tour {_tourOpen})");
        foreach (var x in VrTap(Key.Escape)) yield return x;
        for (int i = 0; i < 20; i++) yield return null;
        VwCheck(!_tourOpen && _card == TitleCard.Track && _cardSel == 0, $"{tag}: Esc closes the tour back to the card (tour {_tourOpen}, {_card})");
        for (int i = 0; i < 30; i++) yield return null;
        foreach (var x in VrTap(Key.L)) yield return x;
        for (int i = 0; i < 4; i++) yield return null;
        VwCheck(VtbOpen(0, laps), $"{tag}: L opens the boards again after the tour");
        foreach (var x in VrTap(Key.Escape)) yield return x;
        for (int i = 0; i < 10; i++) yield return null;
        VwCheck(VtbBack(0, mode, files, power, rallyFile) && _rallyFuel == fuelBefore && _boardPage == officePage && _boardTrack == officeTrack,
            $"{tag}: back on KOLKHOZ; the rally fuel, the car's power and the office board's own page/track are untouched (page {_boardPage}, track {_boardTrack})");
    }
}
