using Godot;

namespace CCCPRacing.Race;

public partial class RaceScene
{
    /// <summary>The title menu's memory between launches: the game mode played last (settings.cfg [menu] last), so a returning player finds the menu on it; a first launch has none (SINGLE PLAYER, SOCIALIST RALLY).</summary>
    static class MenuMemory
    {
        const string Section = "menu", Key = "last";

        static string Id(MenuItem mode) => mode switch { MenuItem.Race => "rally", MenuItem.Training => "stakhanov", MenuItem.School => "school", MenuItem.Lan => "lan", _ => "" };
        static MenuItem? Parse(string id) => id switch { "rally" => MenuItem.Race, "stakhanov" => MenuItem.Training, "school" => MenuItem.School, "lan" => MenuItem.Lan, _ => null };

        /// <summary>The mode saved, or the fallback (a first launch, an unknown value).</summary>
        public static MenuItem Load(string dir, MenuItem fallback)
        {
            var cfg = new ConfigFile();
            if (cfg.Load(dir + "settings.cfg") != Error.Ok || !cfg.HasSectionKey(Section, Key)) return fallback;
            return cfg.GetValue(Section, Key).VariantType == Variant.Type.String && Parse((string)cfg.GetValue(Section, Key)) is { } m ? m : fallback;
        }

        public static void Save(string dir, MenuItem mode)
        {
            if (Id(mode) == "") return;
            var cfg = new ConfigFile();
            cfg.Load(dir + "settings.cfg");      // keep the other sections
            cfg.SetValue(Section, Key, Id(mode));
            cfg.Save(dir + "settings.cfg");
        }
    }
}
