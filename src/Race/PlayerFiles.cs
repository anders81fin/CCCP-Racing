using System;
using System.Collections.Generic;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// The PERSONNEL FILES: five drivers, each with their own progress in profiles/N/ next to the records:
/// campaign.cfg (the medals and the tracks they open), speedtrap.cfg (the GAI readings) and a
/// settings.cfg of their own ([kgb], [tutorial], [driver] name, [file] played). The boards of honour,
/// the ghosts and the game's own settings (radio, map, rumble) stay shared in the records directory,
/// whose settings.cfg also keeps the file in use ([files] active). A file owns only these
/// files and its rally checkpoints: erasing one never touches the records.
/// </summary>
public static class PlayerFiles
{
    public const int Count = 5;
    static readonly string[] Own = { "campaign.cfg", "speedtrap.cfg", "settings.cfg", RallyResume.Filename };
    static readonly string[] OwnSections = { "kgb", "tutorial", "driver", "cameras" };

    /// <summary>File n's directory (1..Count) under the records directory root.</summary>
    public static string Dir(string root, int n) => $"{root}profiles/{n}/";

    /// <summary>The file in use (1 when none has been chosen).</summary>
    public static int Active(string root)
    {
        var cfg = new ConfigFile();
        if (cfg.Load(root + "settings.cfg") != Error.Ok) return 1;
        var v = cfg.GetValue("files", "active", 1);
        return v.VariantType == Variant.Type.Int ? Math.Clamp((int)v, 1, Count) : 1;
    }

    /// <summary>Makes file n the one in use (its directory made if new) and stamps it as played (unless just erased).</summary>
    public static void Activate(string root, int n, bool touch = true)
    {
        string path = root + "settings.cfg";
        var cfg = new ConfigFile();
        cfg.Load(path);       // keep the other sections
        cfg.SetValue("files", "active", n);
        cfg.Save(path);
        DirAccess.MakeDirRecursiveAbsolute(Dir(root, n));
        if (touch) Touch(root, n);
    }

    /// <summary>Stamps file n as played now.</summary>
    public static void Touch(string root, int n)
    {
        string path = Dir(root, n) + "settings.cfg";
        var cfg = new ConfigFile();
        cfg.Load(path);
        cfg.SetValue("file", "played", (long)Time.GetUnixTimeFromSystem());
        cfg.Save(path);
    }

    /// <summary>
    /// Once, before there are files: the progress so far becomes file 1 (copied: the old files in the
    /// records directory stay as they are).
    /// </summary>
    public static void Migrate(string root)
    {
        if (DirAccess.DirExistsAbsolute(root + "profiles")) return;
        string dir = Dir(root, 1);
        DirAccess.MakeDirRecursiveAbsolute(dir);
        foreach (string f in new[] { "campaign.cfg", "speedtrap.cfg" })
            if (FileAccess.FileExists(root + f)) DirAccess.CopyAbsolute(root + f, dir + f);
        var old = new ConfigFile();
        if (old.Load(root + "settings.cfg") != Error.Ok) return;
        var mine = new ConfigFile();
        foreach (string section in OwnSections)
            if (old.HasSection(section))
                foreach (string key in old.GetSectionKeys(section))
                    mine.SetValue(section, key, old.GetValue(section, key));
        mine.Save(dir + "settings.cfg");
    }

    /// <summary>What the files card shows of a file: in use at all, the name, each track's medal, last played.</summary>
    public readonly record struct Summary(bool Used, string Name, Dictionary<string, NormPlan.Medal> Medals, long Played, int Cameras = 0);

    public static Summary Read(string root, int n)
    {
        string dir = Dir(root, n);
        var medals = new Dictionary<string, NormPlan.Medal>();
        bool used = false;
        var camp = new ConfigFile();
        if (camp.Load(dir + "campaign.cfg") == Error.Ok)
        {
            used = true;
            if (camp.HasSection("medals"))
                foreach (string track in camp.GetSectionKeys("medals"))
                    medals[track] = (NormPlan.Medal)(int)camp.GetValue("medals", track, 0);
        }
        var cfg = new ConfigFile();
        string name = "";
        long played = 0;
        int cameras = 0;
        if (cfg.Load(dir + "settings.cfg") == Error.Ok)
        {
            name = Leaderboard.CleanName(cfg.GetValue("driver", "name", "").AsString());
            played = (long)cfg.GetValue("file", "played", 0L);
            if (cfg.HasSection("cameras"))
                foreach (string key in cfg.GetSectionKeys("cameras"))
                    if (cfg.GetValue("cameras", key).VariantType == Variant.Type.Bool && cfg.GetValue("cameras", key).AsBool()) cameras++;
            used |= name.Length > 0 || cfg.HasSection("tutorial") || cfg.HasSection("cameras");
        }
        return new Summary(used, name, medals, played, cameras);
    }

    /// <summary>Erases file n: its own files only (never the records).</summary>
    public static void Erase(string root, int n)
    {
        string dir = Dir(root, n);
        foreach (string f in Own)
            if (FileAccess.FileExists(dir + f)) DirAccess.RemoveAbsolute(dir + f);
    }
}
