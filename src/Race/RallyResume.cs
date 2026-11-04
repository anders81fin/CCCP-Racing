using System;
using Godot;

namespace CCCPRacing.Race;

/// <summary>One explicit, single-use rally checkpoint per track and personnel file.</summary>
public static class RallyResume
{
    public const string Filename = "rally-resume.cfg";
    public record Checkpoint(int Step, int Left, int Used, int Wrecks, float RunTime, int RunLaps, int DayOffset)
    {
        public float[] PassedTargets { get; init; } = Array.Empty<float>();
        public float[] PassedTimes { get; init; } = Array.Empty<float>();
    }

    public static Error Save(string dir, string track, string signature, Checkpoint point, FuelGrade fuel = FuelGrade.Petrol)
    {
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(dir));
        var cfg = new ConfigFile();
        cfg.Load(dir + Filename);
        cfg.SetValue(track, "signature", signature);
        cfg.SetValue(track, "fuel", FuelGrades.Ids[(int)fuel]);
        cfg.SetValue(track, "step", point.Step);
        cfg.SetValue(track, "left", point.Left);
        cfg.SetValue(track, "used", point.Used);
        cfg.SetValue(track, "wrecks", point.Wrecks);
        cfg.SetValue(track, "time", point.RunTime);
        cfg.SetValue(track, "laps", point.RunLaps);
        cfg.SetValue(track, "day", point.DayOffset);
        for (int i = 0; i < NormPlan.Steps; i++)
        {
            cfg.SetValue(track, $"passed_{i}", i < point.PassedTimes.Length ? point.PassedTimes[i] : 0f);
            cfg.SetValue(track, $"target_{i}", i < point.PassedTargets.Length ? point.PassedTargets[i] : 0f);
        }
        return cfg.Save(dir + Filename);
    }

    public static Checkpoint Load(string dir, string track, string signature)
    {
        var cfg = new ConfigFile();
        if (cfg.Load(dir + Filename) != Error.Ok || !cfg.HasSection(track)) return null;
        try
        {
            if ((string)cfg.GetValue(track, "signature", "") != signature) return null;
            int Int(string key) => (int)cfg.GetValue(track, key, -1);
            var p = new Checkpoint(Int("step"), Int("left"), Int("used"), Int("wrecks"),
                (float)cfg.GetValue(track, "time", -1f), Int("laps"), Int("day"));
            var times = new float[NormPlan.Steps];
            var targets = new float[NormPlan.Steps];
            for (int i = 0; i < times.Length; i++)
            {
                float target = (float)cfg.GetValue(track, $"target_{i}", 0f);
                if (i < p.Step && float.IsFinite(target) && target > 0) targets[i] = target;
                float t = (float)cfg.GetValue(track, $"passed_{i}", 0f);
                if (i < p.Step && float.IsFinite(t) && t > 0) times[i] = t;
            }
            p = p with { PassedTimes = times, PassedTargets = targets };
            return p.Step >= 0 && p.Step < NormPlan.Steps && p.Left >= 1 && p.Left <= NormPlan.MaxLaps
                && p.Used >= 0 && p.Wrecks >= 0 && float.IsFinite(p.RunTime) && p.RunTime >= 0
                && p.RunLaps >= 0 && p.DayOffset >= 0 && p.DayOffset < DayNight.CycleLaps ? p : null;
        }
        catch (InvalidCastException) { return null; }
    }

    /// <summary>The fuel a saved rally was driven on (petrol: older saves).</summary>
    public static FuelGrade FuelOf(string dir, string track)
    {
        var cfg = new ConfigFile();
        return cfg.Load(dir + Filename) == Error.Ok ? FuelGrades.Parse((string)cfg.GetValue(track, "fuel", "petrol")) : FuelGrade.Petrol;
    }

    public static bool Exists(string dir, string track)
    {
        var cfg = new ConfigFile();
        return cfg.Load(dir + Filename) == Error.Ok && cfg.HasSection(track);
    }

    public static Error Clear(string dir, string track)
    {
        var cfg = new ConfigFile();
        if (cfg.Load(dir + Filename) != Error.Ok || !cfg.HasSection(track)) return Error.Ok;
        cfg.EraseSection(track);
        return cfg.Save(dir + Filename);
    }
}
