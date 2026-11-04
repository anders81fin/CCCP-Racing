using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// The GAI's book: the fastest reading on each track's speed trap, the rally and Stakhanov laps
/// apart (500 % is another sport), in speedtrap.cfg next to the records (a file of its own).
/// </summary>
public class SpeedRecords
{
    readonly string _path;
    readonly ConfigFile _cfg = new();

    public SpeedRecords(string dir)
    {
        _path = dir == null ? null : dir + "speedtrap.cfg";
        if (_path != null && FileAccess.FileExists(_path)) _cfg.Load(_path);
    }

    public float Best(string mode, string track) => (float)_cfg.GetValue(mode, track, 0f);

    /// <summary>Writes the reading down if it beats the book: true when it does.</summary>
    public bool Offer(string mode, string track, float kmh)
    {
        if (kmh <= Best(mode, track)) return false;
        _cfg.SetValue(mode, track, kmh);
        if (_path != null) _cfg.Save(_path);
        return true;
    }
}
