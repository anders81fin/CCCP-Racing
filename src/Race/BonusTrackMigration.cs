using System;
using Godot;
using CCCPRacing.Track;

namespace CCCPRacing.Race;

/// <summary>Preserve records and each personnel file when the fourth track is renamed to bonus.</summary>
public static class BonusTrackMigration
{
    public static void Migrate(string root)
    {
        // Keep old files if a new-name destination already exists; never overwrite either set.
        foreach (string file in DirAccess.GetFilesAt(root))
        foreach (string prefix in new[] { "ghost_", "leaderboard_", "track_", "records_" })
        {
            string oldPrefix = prefix + TrackIds.LegacyBonus;
            if (!file.StartsWith(oldPrefix + ".") && !file.StartsWith(oldPrefix + "_")) continue;
            string destination = root + prefix + "bonus" + file[oldPrefix.Length..];
            if (FileAccess.FileExists(destination)) continue;
            var error = DirAccess.RenameAbsolute(ProjectSettings.GlobalizePath(root + file), ProjectSettings.GlobalizePath(destination));
            if (error != Error.Ok) GD.PrintErr($"Bonus migration: {file}: {error}");
        }
        MigrateDirectory(root);
        for (int file = 1; file <= PlayerFiles.Count; file++) MigrateDirectory(PlayerFiles.Dir(root, file));
    }

    static void MigrateDirectory(string dir)
    {
        foreach (string file in new[] { "campaign.cfg", "speedtrap.cfg", "settings.cfg" })
        {
            var cfg = new ConfigFile();
            if (cfg.Load(dir + file) != Error.Ok) continue;
            bool changed = false;
            foreach (string section in cfg.GetSections())
            foreach (string key in cfg.GetSectionKeys(section))
            {
                string newKey = key == TrackIds.LegacyBonus ? "bonus"
                    : key == "school_rally_" + TrackIds.LegacyBonus ? "school_rally_bonus" : key;
                if (newKey == key) continue;
                var old = cfg.GetValue(section, key);
                if (!cfg.HasSectionKey(section, newKey)) cfg.SetValue(section, newKey, old);
                else if (old.VariantType == Variant.Type.Int && cfg.GetValue(section, newKey).VariantType == Variant.Type.Int)
                    cfg.SetValue(section, newKey, Math.Max(old.AsInt64(), cfg.GetValue(section, newKey).AsInt64()));
                else if (old.VariantType == Variant.Type.Float && cfg.GetValue(section, newKey).VariantType == Variant.Type.Float)
                    cfg.SetValue(section, newKey, Math.Max(old.AsDouble(), cfg.GetValue(section, newKey).AsDouble()));
                else if (old.VariantType == Variant.Type.Bool && cfg.GetValue(section, newKey).VariantType == Variant.Type.Bool)
                    cfg.SetValue(section, newKey, old.AsBool() || cfg.GetValue(section, newKey).AsBool());
                // Keep the legacy key as a fallback for older builds; game code uses only the canonical key.
                changed = true;
            }
            if (changed) cfg.Save(dir + file);
        }
    }
}
