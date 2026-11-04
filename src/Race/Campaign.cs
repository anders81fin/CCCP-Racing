using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// The five-year plan's medals: the best medal won in the SOCIALIST RALLY on each track, in
/// campaign.cfg next to the records (a file of its own: the records and the KGB file are never
/// touched). A track that needs another's bronze opens once that is won.
/// </summary>
public class Campaign
{
    readonly string _path;
    readonly ConfigFile _cfg = new();

    public Campaign(string dir)
    {
        _path = dir == null ? null : dir + "campaign.cfg";
        if (_path != null && FileAccess.FileExists(_path)) _cfg.Load(_path);
    }

    /// <summary>The best medal on a track on any fuel: it is what opens the next track (a medal counts whatever the fuel).</summary>
    public NormPlan.Medal Best(string track) => (NormPlan.Medal)(int)_cfg.GetValue("medals", track, 0);

    /// <summary>The best medal on a track on this fuel. Medals won before the fuels existed are petrol's.</summary>
    public NormPlan.Medal BestOn(string track, FuelGrade fuel)
    {
        string key = $"{track}_{FuelGrades.Ids[(int)fuel]}";
        if (_cfg.HasSectionKey("medals_fuel", key)) return (NormPlan.Medal)(int)_cfg.GetValue("medals_fuel", key, 0);
        bool anyFuelMedal = false;
        for (int g = 0; g < FuelGrades.Count; g++) anyFuelMedal |= _cfg.HasSectionKey("medals_fuel", $"{track}_{FuelGrades.Ids[g]}");
        return fuel == FuelGrade.Petrol && !anyFuelMedal ? Best(track) : NormPlan.Medal.None;
    }

    /// <summary>Saves the medal for the fuel and, if it beats the best on any fuel, for the track: true when the track's best improves.</summary>
    public bool Award(string track, NormPlan.Medal medal, FuelGrade fuel)
    {
        // The medals from before the fuels become petrol's before the first fuel medal is written next to them.
        string petrolKey = $"{track}_{FuelGrades.Ids[(int)FuelGrade.Petrol]}";
        if (Best(track) > NormPlan.Medal.None && !_cfg.HasSectionKey("medals_fuel", petrolKey) && BestOn(track, FuelGrade.Petrol) == Best(track))
            _cfg.SetValue("medals_fuel", petrolKey, (int)Best(track));
        if (medal > BestOn(track, fuel))
        {
            _cfg.SetValue("medals_fuel", $"{track}_{FuelGrades.Ids[(int)fuel]}", (int)medal);
            if (_path != null) _cfg.Save(_path);
        }
        return Award(track, medal);
    }

    /// <summary>Saves the medal if it beats the best on file: true when it does (a new medal).</summary>
    public bool Award(string track, NormPlan.Medal medal)
    {
        if (medal <= Best(track)) return false;
        _cfg.SetValue("medals", track, (int)medal);
        if (_path != null) _cfg.Save(_path);
        return true;
    }

    public bool LapsPresented(string track, bool rally = false) =>
        (bool)_cfg.GetValue(rally ? "rally_presented" : "laps_presented", track, false);

    public void PresentLaps(string track, bool rally = false)
    {
        _cfg.SetValue(rally ? "rally_presented" : "laps_presented", track, true);
        if (_path != null) _cfg.Save(_path);
    }

    public static string Name(NormPlan.Medal m) => m switch
    {
        NormPlan.Medal.Bronze => "BRONZE", NormPlan.Medal.Silver => "SILVER", NormPlan.Medal.Gold => "GOLD", _ => "NONE",
    };
}
