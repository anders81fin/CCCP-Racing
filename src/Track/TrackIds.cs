namespace CCCPRacing.Track;

/// <summary>Canonical track IDs. The old bonus ID exists only for save migration; kolkhoz aliases now mean Ilyich.</summary>
public static class TrackIds
{
    /// <summary>False on the tracks whose tracks/ID.json says "fuel": false (Ilyich's Lamp and Atomgrad-4): no consumption, depot or cans; the LAN lobby cannot ask for fuel there. A test (PhysicsTest.RadiationPower) keeps this equal to the json.</summary>
    public static bool HasFuel(string id) => id is not ("ilyich" or "atomgrad");
    public static bool Known(string id) => id is "ilyich" or "siberia" or "atomgrad" or "bonus";
    public static string Label(string id) => id switch {"ilyich"=>"KOLKHOZ","siberia"=>"SIBERIA","atomgrad"=>"ATOMGRAD-4","bonus"=>"POLIGON 17",_=>"?"};
    public const string LegacyBonus = "kolkhoz";
    public static string FromCommandLine(string id) => id is "kolkhoz" or "kolhoosi" ? "ilyich" : id;
}
