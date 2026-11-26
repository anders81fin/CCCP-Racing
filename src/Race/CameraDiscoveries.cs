using System;
using System.Collections.Generic;
using Godot;

namespace CCCPRacing.Race;

/// <summary>TopDown is the directed overhead camera (the default); Classic the original overhead one. Both are always there; Cinematic (follow behind: CinematicClose, Cinematic = medium and CinematicFar, all three opened by one find) and Bonnet are found on the track.</summary>
public enum GameplayView { TopDown, Cinematic, Bonnet, Classic, CinematicClose, CinematicFar, RoadAligned }

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
    static string Key(string track, GameplayView view) => track + "/" + (view is GameplayView.CinematicClose or GameplayView.CinematicFar ? GameplayView.Cinematic : view).ToString().ToLowerInvariant();
    public static bool IsFollowBehind(GameplayView view) => view is GameplayView.Cinematic or GameplayView.CinematicClose or GameplayView.CinematicFar;
    /// <summary>The Konami code (RaceScene.Cheat.cs): every camera of every track counts as found. Nothing is written; the real discoveries stay as they were.</summary>
    public Func<bool> AllOpen { get; set; }
    public static bool IsOverhead(GameplayView view) => view is GameplayView.TopDown or GameplayView.Classic;
    /// <summary>Always there, nothing to find: the two overhead views, and the road-aligned one above the car on tracks that do not hide it (the four tracks hide it; custom tracks have it open).</summary>
    public static bool AlwaysOpen(string track, GameplayView view) => IsOverhead(view) || view == GameplayView.RoadAligned && !Track.CameraSecrets.HidesRoadAligned(track);
    /// <summary>The cycle: directed, classic, the found bonnet, the found follow-behind (close, medium, far) and last the road-aligned view (found on the four tracks, always there on custom tracks).</summary>
    static readonly GameplayView[] Order = { GameplayView.TopDown, GameplayView.Classic, GameplayView.Bonnet, GameplayView.CinematicClose, GameplayView.Cinematic, GameplayView.CinematicFar, GameplayView.RoadAligned };
    public bool Has(string track, GameplayView view) => AlwaysOpen(track, view) || AllOpen?.Invoke() == true || _owned.Contains(Key(track, view));
    public bool Discover(string track, GameplayView view)
    {
        if (AlwaysOpen(track, view) || Has(track, view)) return false;
        if (_path != null)
        {
            // Reload before writing: the driver's name, tutorial and KGB sections share this file.
            var cfg = new ConfigFile(); cfg.Load(_path);
            cfg.SetValue("cameras", Key(track, view), true);
            if (cfg.Save(_path) != Error.Ok) return false;
        }
        return _owned.Add(Key(track, view));
    }
    public bool HasAlternative(string track) => Has(track, GameplayView.Cinematic) || Has(track, GameplayView.Bonnet)
        || !AlwaysOpen(track, GameplayView.RoadAligned) && Has(track, GameplayView.RoadAligned);
    public GameplayView Next(string track, GameplayView current) => Step(track, current, 1);
    /// <summary>The next (step 1) or previous (-1) view this file has on the track, round the cycle.</summary>
    public GameplayView Step(string track, GameplayView current, int step)
    {
        int at = Array.IndexOf(Order, current);
        for (int n = 1; n <= Order.Length; n++)
        {
            var candidate = Order[((at + step * n) % Order.Length + Order.Length) % Order.Length];
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
