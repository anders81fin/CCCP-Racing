using Godot;

namespace CCCPRacing.Net;

/// <summary>
/// RECORD LAN RACES? (RaceScene.LanRecord.cs): whether this machine records its own LAN races in the background.
/// Kept in the machine's settings.cfg ([lan] record_races, beside [input] rumble; --data-dir isolates it), the same
/// scope as the recordings folder. A missing key means the player has never answered: the LAN menu asks before
/// HOST GAME or JOIN GAME, and until then nothing is recorded. Local only: never sent, never part of a ballot.
/// </summary>
public static class LanRecordChoice
{
    static readonly System.Collections.Generic.Dictionary<string, bool> TemporaryAnswers = new();
    public const string Section = "lan", Key = "record_races", Flag = "--lan-record=";
    /// <summary>The question's preselected answer (the behaviour before the question existed).</summary>
    public const bool Suggested = true;

    /// <summary>The saved answer; null: never answered (or not a yes/no value).</summary>
    public static bool? Load(string settingsPath)
    {
        var cfg = new ConfigFile();
        if (cfg.Load(settingsPath) != Error.Ok || !cfg.HasSectionKey(Section, Key)) return null;
        var v = cfg.GetValue(Section, Key);
        return v.VariantType == Variant.Type.Bool ? (bool)v : null;
    }

    /// <summary>Saves the answer, keeping the file's other sections. An existing file that cannot be read is never overwritten.</summary>
    public static bool Save(string settingsPath, bool on)
    {
        var cfg = new ConfigFile();
        if (cfg.Load(settingsPath) != Error.Ok && FileAccess.FileExists(settingsPath)) return false;
        cfg.SetValue(Section, Key, on);
        return cfg.Save(settingsPath) == Error.Ok;
    }

    /// <summary>An unwritable settings file must not lose the explicit answer when joining reloads the track.</summary>
    static string SessionKey(string path) => System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath(path));
    public static bool? ForSession(string settingsPath) => TemporaryAnswers.TryGetValue(SessionKey(settingsPath), out bool answer) ? answer : Load(settingsPath);
    public static bool Apply(string settingsPath, bool on)
    {
        string key = SessionKey(settingsPath);
        if (Save(settingsPath, on)) { TemporaryAnswers.Remove(key); return true; }
        TemporaryAnswers[key] = on;
        return false;
    }

    /// <summary>Test flag --lan-record=yes|no: this run only, skips the question, never saved. null: no (valid) flag.</summary>
    public static bool? Override(string[] args)
    {
        bool? r = null;
        foreach (string a in args)
            if (a.StartsWith(Flag))
                r = a[Flag.Length..].ToLowerInvariant() switch { "yes" or "1" or "on" => true, "no" or "0" or "off" => false, _ => r };
        return r;
    }

    public static string Label(bool? saved) => saved switch { true => "YES", false => "NO", null => "ASK" };
}
