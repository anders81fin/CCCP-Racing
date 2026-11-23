using System;
using System.Collections;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

// Windowed scenario "stakhanov-exit" (--viewer-windowed-only=stakhanovexit): leaving the STAKHANOV LAPS through the Party Meeting's EXIT RACE goes back to the laps' track card of the same fuel (the 2026-11-23 order; SINGLE PLAYER's STAKHANOV LAPS
// row under it), not to the main menu; forward the track card stands on the track just driven, Esc is one step back.
public partial class RaceScene
{
    IEnumerable VwStakhanovExit()
    {
        foreach (var x in VwWaitReady()) yield return x;
        foreach (var x in VrUntil(() => _state == State.Racing && _lapClock > 2f, 60 * 60, "the race is running")) yield return x;
        VwCheck(StakhanovRules && _lastPlayed == MenuItem.Training, "a Stakhanov laps drive");
        string track = _trackId;
        _lapsFuelLast = FuelGrade.Diesel;      // (what the menu's fuel card records when the laps start from it; this drive started from the command line)
        foreach (var x in VrTap(Key.Escape)) yield return x;
        VwCheck(_state == State.Paused, "the Party Meeting is up");
        foreach (var x in VrToRow(PauseRowIndex(PauseRowId.Exit))) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 60; i++) yield return null;
        int idx = Array.FindIndex(Tracks, t => t.id == track);
        VwCheck(_state == State.Title && _card == TitleCard.Track && _lapsFuelPicked && _menuParent == MenuItem.Single && _menu == MenuItem.Training, $"EXIT RACE leaves for the laps' track card ({_state}, {_card}, row {_menu})");
        VwCheck(_lapsFuelPick == FuelGrade.Diesel && _cardSel == idx, $"the fuel the laps started with is kept ({_lapsFuelPick}) and the card stands on the track just driven ({_cardSel} == {idx})");
        foreach (var x in VrShot("stakhanovexit-track")) yield return x;
        // Esc: one step back to the fuel (the same one selected), again Esc: the laps' row in the menu
        foreach (var x in VrTap(Key.Escape)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_card == TitleCard.LapsFuel, "Esc on the track card: back to the fuel");
        foreach (var x in VrTap(Key.Escape)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_card == TitleCard.None && _menuParent == MenuItem.Single && _menu == MenuItem.Training, "Esc again: STAKHANOV LAPS' row in SINGLE PLAYER");
    }
}
