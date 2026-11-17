namespace CCCPRacing.Track;

/// <summary>Canonical track IDs. The old bonus ID exists only for save migration; kolkhoz aliases now mean Ilyich.</summary>
public static class TrackIds
{
    public static bool Known(string id) => id is "ilyich" or "siberia" or "atomgrad" or "bonus";
    public static string Label(string id) => id switch {"ilyich"=>"KOLKHOZ","siberia"=>"SIBERIA","atomgrad"=>"ATOMGRAD-4","bonus"=>"POLIGON 17",_=>"?"};
    public const string LegacyBonus = "kolkhoz";
    public static string FromCommandLine(string id) => id is "kolkhoz" or "kolhoosi" ? "ilyich" : id;
}
