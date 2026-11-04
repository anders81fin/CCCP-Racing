using System;
using System.Collections.Generic;
using Godot;

namespace CCCPRacing.Race;

public enum GameplayView { TopDown, Cinematic, Bonnet }

/// <summary>Per-track camera discoveries belong to the active personnel file, not the shared settings.</summary>
public sealed class CameraDiscoveries
{
    readonly string _path;
    readonly HashSet<string> _owned = new(StringComparer.Ordinal);
    public CameraDiscoveries(string directory)
    {
        _path = directory == null ? null : directory + "settings.cfg";
        if (_path == null) return;
        var cfg = new ConfigFile();
        if (cfg.Load(_path) != Error.Ok || !cfg.HasSection("cameras")) return;
        foreach (string key in cfg.GetSectionKeys("cameras"))
            if (cfg.GetValue("cameras", key).VariantType == Variant.Type.Bool && cfg.GetValue("cameras", key).AsBool()) _owned.Add(key);
    }
    static string Key(string track, GameplayView view) => track + "/" + view.ToString().ToLowerInvariant();
    /// <summary>The Konami code (RaceScene.Cheat.cs): every camera of every track counts as found. Nothing is written; the real discoveries stay as they were.</summary>
    public Func<bool> AllOpen { get; set; }
    public bool Has(string track, GameplayView view) => view == GameplayView.TopDown || AllOpen?.Invoke() == true || _owned.Contains(Key(track, view));
    public bool Discover(string track, GameplayView view)
    {
        if (view == GameplayView.TopDown || Has(track, view)) return false;
        if (_path != null)
        {
            // Reload before writing: the driver's name, tutorial and KGB sections share this file.
            var cfg = new ConfigFile(); cfg.Load(_path);
            cfg.SetValue("cameras", Key(track, view), true);
            if (cfg.Save(_path) != Error.Ok) return false;
        }
        return _owned.Add(Key(track, view));
    }
    public bool HasAlternative(string track) => Has(track, GameplayView.Cinematic) || Has(track, GameplayView.Bonnet);
    public GameplayView Next(string track, GameplayView current)
    {
        for (int step = 1; step <= 3; step++)
        {
            var candidate = (GameplayView)(((int)current + step) % 3);
            if (Has(track, candidate)) return candidate;
        }
        return GameplayView.TopDown;
    }

    // A swept car footprint catches fast passes without allowing a jump overhead or a rescue teleport.
    public static bool Touches(Vector3 before, Vector3 after, Basis body, Vector3 target, float radius)
    {
        if (before.DistanceTo(after) > 35f) before = after;
        Vector3 motion = after - before;
        float t = motion.LengthSquared() < 0.0001f ? 1f : Mathf.Clamp((target - before).Dot(motion) / motion.LengthSquared(), 0, 1);
        Vector3 relative = body.Inverse() * (target - before.Lerp(after, t));
        return MathF.Abs(relative.Y) < 1.4f && MathF.Abs(relative.X) < 0.95f + radius && MathF.Abs(relative.Z) < 2.15f + radius;
    }
}
