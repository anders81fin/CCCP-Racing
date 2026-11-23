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
        /// <summary>The car as it was when the rally was saved (-1: not kept, as in the older saves): the engine's health 0..1, the dose as a share of the zone's limit, the fuel in litres.</summary>
        public float Health { get; init; } = -1f;
        public float Dose { get; init; } = -1f;
        public float Litres { get; init; } = -1f;
        /// <summary>The body's damage (CarDamage.SavedText), empty when the car was whole.</summary>
        public string Body { get; init; } = "";
        /// <summary>The revenges already under way, which a saved rally does not shake off: "jet" (the Balalaika's or the Hind's), "tanks", "slurry:N" (N laps left); comma separated, empty: none.</summary>
        public string Revenges { get; init; } = "";
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
        if (point.Health >= 0f) cfg.SetValue(track, "health", point.Health);
        if (point.Dose >= 0f) cfg.SetValue(track, "dose", point.Dose);
        if (point.Litres >= 0f) cfg.SetValue(track, "litres", point.Litres);
        if (!string.IsNullOrEmpty(point.Body)) cfg.SetValue(track, "body", point.Body);
        if (!string.IsNullOrEmpty(point.Revenges)) cfg.SetValue(track, "revenges", point.Revenges);
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
            float Car(string key, float max) { float v = (float)cfg.GetValue(track, key, -1f); return float.IsFinite(v) && v >= 0f && v <= max ? v : -1f; }
            p = p with { PassedTimes = times, PassedTargets = targets, Health = Car("health", 1f), Dose = Car("dose", 100f), Litres = Car("litres", 1000f), Body = CleanBody((string)cfg.GetValue(track, "body", "")), Revenges = CleanRevenges((string)cfg.GetValue(track, "revenges", "")) };
            return p.Step >= 0 && p.Step < NormPlan.Steps && p.Left >= 1 && p.Left <= NormPlan.MaxLaps
                && p.Used >= 0 && p.Wrecks >= 0 && float.IsFinite(p.RunTime) && p.RunTime >= 0
                && p.RunLaps >= 0 && p.DayOffset >= 0 && p.DayOffset < DayNight.CycleLaps ? p : null;
        }
        catch (InvalidCastException) { return null; }
    }

    /// <summary>The body text is read only when it is the size a body can be (a hand-edited or damaged file cannot make the game read a megabyte).</summary>
    public static string CleanBody(string text) => text != null && text.Length <= 600_000 ? text : "";

    /// <summary>Only the revenges the game knows, each once (a damaged file or a hand-edited one cannot start anything else).</summary>
    public static string CleanRevenges(string text)
    {
        var kept = new System.Collections.Generic.List<string>();
        foreach (var token in (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            bool slurry = token.StartsWith("slurry:") && int.TryParse(token["slurry:".Length..], out int n) && n >= 1 && n <= 9;
            if ((token is "jet" or "tanks" || slurry) && !kept.Contains(token)) kept.Add(token);
        }
        return string.Join(",", kept);
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
