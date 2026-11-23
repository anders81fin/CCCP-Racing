using System;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// The newly opened fuel's unlocking (the owner's order, 2026-11-23): leaving a run that opened PETROL, JET FUEL or SPUTNIK returns to CHOOSE FUEL (not the track card) with the new fuel selected, and
/// the padlock over the card bursts open (HudFuelUnlock.cs). Once per fuel; a fuel that was already presented before this order is old news. The car's own ceremony (RaceScene.FuelCeremony.cs) comes
/// afterwards, when the title is clear again.
/// </summary>
public partial class RaceScene
{
    FuelGrade? _fuelUnlock, _unlockFuel;
    Action _afterCeremonies;

    /// <summary>
    /// Opens the cards at once when no ceremony is due; otherwise the title shows the ceremony first (the new fuel's car, then the test pilots' programme) and the cards come when it is clear again, so
    /// that the car's showing and the padlock in the menu both happen, in that order.
    /// </summary>
    void AfterTitleCeremonies(Action open)
    {
        _afterCeremonies = null;
        if (CeremonyDue()) { _afterCeremonies = open; return; }
        open();
    }

    /// <summary>A ceremony is going to start over the title (the fuel's car, the programme's) or is up.</summary>
    bool CeremonyDue() => ViewerMode == false && !_previewMovie && InstructionTrack == null && _campaign != null
        && (FuelCeremonyActive || SputnikCeremonyActive || FuelCeremonyCandidate() != null);

    void AfterCeremoniesFrame()
    {
        if (_afterCeremonies == null) return;
        if (_state != State.Title) { _afterCeremonies = null; return; }
        if (!_ceremonyDue && CeremonyClear && !CeremonyDue()) { var open = _afterCeremonies; _afterCeremonies = null; open(); }
    }
    float _fuelUnlockAge;

    /// <summary>The fuel opened by the medals that has not had its unlocking yet.</summary>
    FuelGrade? FuelUnlockPending()
    {
        if (ViewerMode || _previewMovie || InstructionTrack != null || _campaign == null || !_campaign.FuelPresentationStarted || CheatUnlock) return null;
        foreach (var g in new[] { FuelGrade.Petrol, FuelGrade.Rocket, FuelGrade.Sputnik })
            if (FuelOpenByMedals(g) && !_campaign.FuelPresented(g) && !_campaign.FuelUnlockShown(g)) return g;
        return null;
    }

    /// <summary>On the fuel card just opened by a return from a run: the pending fuel selected and the padlock begins. True when it did.</summary>
    bool BeginFuelUnlock()
    {
        var wanted = _unlockFuel; _unlockFuel = null;
        if (wanted is not { } g || _card is not (TitleCard.RallyFuel or TitleCard.LapsFuel)) return false;
        _fuelUnlock = g; _fuelUnlockAge = 0;
        _cardSel = (int)g;
        _hud.BeginFuelUnlock((int)g, FuelGrades.Names[(int)g]);
        return true;
    }

    /// <summary>The padlock is up: the card takes no keys until it has opened (any key skips it once it has broken).</summary>
    bool FuelUnlockActive => _fuelUnlock != null && _state == State.Title && _card is TitleCard.RallyFuel or TitleCard.LapsFuel;

    void FuelUnlockFrame(float dt)
    {
        if (_fuelUnlock == null) return;
        if (!FuelUnlockActive) { EndFuelUnlock(false); return; }
        _fuelUnlockAge += Math.Min(dt, 0.1f);
        _hud.FuelUnlockAge = _fuelUnlockAge;
        _cardSel = (int)_fuelUnlock.Value;
        if (_fuelUnlockAge >= Hud.FuelUnlockEnd) EndFuelUnlock(true);
    }

    void EndFuelUnlock(bool seen)
    {
        if (_fuelUnlock is { } g && seen) _campaign.ShowFuelUnlock(g);
        _fuelUnlock = null; _hud.EndFuelUnlock();
        _overlayExitFrame = Engine.GetProcessFrames();
    }
}
