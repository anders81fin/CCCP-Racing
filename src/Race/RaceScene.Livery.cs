using System;
using System.Linq;
using CCCPRacing.Car;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// The chief test pilot's livery on the car (SputnikProgram.cs, CarVisual.Livery.cs): the name the mechanic painted in the ceremony (campaign.cfg [sputnik_livery]) and the four gold stars, on the player's own Sputnik in every later run and
/// in the replays of those laps. A replay is of the car it was recorded in: the livery is part of the lap's metadata (LapMeta.Livery, "NAME|STARS"), so a lap from before the paint, or from a profile that never had it, shows a plain car, and
/// a lap of another driver (an imported one, a LAN board's) shows that driver's. The tape's world actors are not involved.
/// </summary>
public partial class RaceScene
{
    (string Name, int Stars)? _viewerLivery;
    /// <summary>The ceremony paints the car gradually: while set, this is the livery shown (name, stars, reveal 0..1).</summary>
    (string Name, int Stars, float Reveal)? _liveryPaint;

    internal static (string Name, int Stars)? ParseLiveryForTest(string text) => ParseLivery(text);
    static (string Name, int Stars)? ParseLivery(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var parts = text.Split('|');
        if (parts.Length != 2 || !int.TryParse(parts[1], out int stars) || stars < 1 || stars > 4) return null;
        string name = Leaderboard.CleanName(parts[0]);
        return name.Length == 0 ? null : (name, stars);
    }

    /// <summary>The livery of the lap being recorded: the painted name and four stars when the car on this lap is the Sputnik (a rally on it, or a Stakhanov lap that used it), else nothing.</summary>
    string LiveryText()
    {
        string name = _campaign.SputnikPaintedName;
        if (name.Length == 0) return "";
        bool sputnik = _run == RunMode.Rally ? _rallyFuel == FuelGrade.Sputnik : _lapFuels.Any(f => f.Grade == (int)FuelGrade.Sputnik);
        return sputnik ? name + "|4" : "";
    }

    /// <summary>Per frame: the livery the car in view should wear, put on (or taken off) the visual; cheap, the visual ignores a repeat.</summary>
    void ApplyLivery()
    {
        if (_carVis == null) return;
        if (_liveryPaint is { } p) { _carVis.SetLivery(p.Name, p.Stars, p.Reveal); return; }
        if (GalleryCarActive) { _carVis.SetLivery(_galleryPainted, 4); return; }      // (RaceScene.SputnikGallery.cs: that driver's own painted name)
        (string Name, int Stars)? want = null;
        if (CarTourActive || FuelCeremonyActive) want = null;      // (the paint scene sets _liveryPaint)
        else if (ViewerMode) { if (EngineFuel == FuelGrade.Sputnik) want = _viewerLivery; }
        else if (Replaying || _run == RunMode.Lan || _state == State.Title) want = null;      // (the title's replay and the menu's demo car: not the player's own run)
        else if (EngineFuel == FuelGrade.Sputnik && _campaign.SputnikPaintedName.Length > 0) want = (_campaign.SputnikPaintedName, 4);
        if (want is { } w) _carVis.SetLivery(w.Name, w.Stars); else _carVis.SetLivery("", 0);
    }
}
