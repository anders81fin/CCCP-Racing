using Godot;

namespace CCCPRacing.Camera;

/// <summary>
/// The player's choice of the top-down camera's look-ahead (SETTINGS > VISUALS > TOP-DOWN LEAD): DIRECTED (the default: the look-ahead of
/// ChaseCamera.Lead.cs) or CLASSIC (the original). Remembered in settings.cfg [camera] lead = "classic" | "directed"; no entry is directed, any other value is classic. The viewing angle never depends on it.
/// </summary>
public static class CameraStyleSetting
{
    public const string Section = "camera", Key = "lead", Classic = "classic", Directed = "directed";

    public static string Name(bool directed) => directed ? Directed : Classic;
    public static string Label(bool directed) => directed ? "DIRECTED" : "CLASSIC";

    public static bool Load(ConfigFile cfg) => cfg.GetValue(Section, Key, Directed).AsString() == Directed;

    public static void Save(string path, bool directed)
    {
        var cfg = new ConfigFile();
        cfg.Load(path);      // keep the other sections
        cfg.SetValue(Section, Key, Name(directed));
        cfg.Save(path);
    }

    /// <summary>What the camera uses: the setting, unless the development flag --camera-classic is on the command line (it forces classic).</summary>
    public static bool Effective(bool settingDirected, bool classicFlag) => settingDirected && !classicFlag;
}
