using System;
using Godot;

namespace CCCPRacing.Audio;

/// <summary>
/// The sound mixer of the PARTY MEETING (SOUND BALANCE): three levels 0..10 (5 = as the game was tuned, 2.5 dB a step, 0 = mute)
/// for the machinery (engine, gearbox, exhaust, brakes, horn), the road (tyres, the ground, stones, wind, bumps)
/// the surroundings (the bus, tractors, siren, weather, aircraft, explosions) and the alerts (the dosimeter, the missile warning,
/// the co-driver). The radio's volume is in the mixer too but stays the car radio's own (CarRadio). Kept in settings.cfg ([sound]).
/// </summary>
public static class SoundBalance
{
    public const int Engine = 0, Road = 1, World = 2, Alerts = 3, Count = 4, Max = 10, Default = 5;
    public static readonly string[] Names = { "ENGINE AND GEARBOX", "TYRES AND ROAD", "SURROUNDINGS", "ALERTS" };
    public static readonly string[] Keys = { "engine", "road", "world", "alerts" };
    public static readonly int[] Levels = { Default, Default, Default, Default };

    /// <summary>The engine voice's and the road voice's gains (linear), read by <see cref="EngineSound"/> every sample.</summary>
    public static float EngineGain = 1f, RoadGain = 1f;

    public static float Db(int level) => level <= 0 ? -80f : (level - Default) * 2.5f;

    static float Gain(int level) => level <= 0 ? 0f : MathF.Pow(10f, Db(level) / 20f);

    /// <summary>The surroundings go through their buses (the Diesel, Jet and Boom mix groups).</summary>
    public static void Apply()
    {
        EngineGain = Gain(Levels[Engine]);
        RoadGain = Gain(Levels[Road]);
        float world = Db(Levels[World]);
        AudioMix.SetTrim(AudioMix.Diesel, world);
        AudioMix.SetTrim(AudioMix.Jet, world);
        AudioMix.SetTrim(AudioMix.Boom, world);
        AudioMix.SetTrim(AudioMix.Warning, Db(Levels[Alerts]));      // the dosimeter, the missile warning, the co-driver
    }

    public static void Step(int group, int step)
    {
        Levels[group] = Math.Clamp(Levels[group] + step, 0, Max);
        Apply();
    }

    public static void Reset()
    {
        for (int i = 0; i < Count; i++) Levels[i] = Default;
        Apply();
    }

    public static void Load(ConfigFile cfg)
    {
        for (int i = 0; i < Count; i++)
        {
            var v = cfg.GetValue("sound", Keys[i], Default);
            Levels[i] = v.VariantType == Variant.Type.Int ? Math.Clamp((int)v, 0, Max) : Default;
        }
        Apply();
    }

    public static void Save(string path)
    {
        var cfg = new ConfigFile();
        cfg.Load(path);      // keep the other sections
        for (int i = 0; i < Count; i++) cfg.SetValue("sound", Keys[i], Levels[i]);
        cfg.Save(path);
    }
}
