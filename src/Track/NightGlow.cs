using System.Collections.Generic;
using Godot;

namespace CCCPRacing.Track;

/// <summary>
/// What the rally's night (Race.DayNight) needs from the world builders: parts that light up after
/// dark (lit windows, beacons: emissive, dark by day) and unshaded materials that are lights
/// themselves and must not dim with the world (flames, runway lights). By day nothing here changes a pixel.
/// </summary>
public static class NightGlow
{
    /// <summary>Material meta: how much an unshaded material keeps its day colour at night (1: all, a light; 0.4: dust).</summary>
    public const string KeepMeta = "night_keep";
    public const string EnergyMeta = "night_glow_energy";

    /// <summary>Emissive materials and their full-night emission energy; the night scales them all from 0 (day).</summary>
    public static readonly List<(StandardMaterial3D mat, float energy)> Glows = new();

    /// <summary>How far the night has come (0 day .. 1 full), set by Race.DayNight every time it changes: parts made later (a jet taking off) start at it.</summary>
    public static float Level;

    /// <summary>Small lamps (the runway's edge lights) that the night swells, so they read as dots of light.</summary>
    public static readonly List<Node3D> Lamps = new();

    /// <summary>The registries and the level as they were (Snapshot): what the replay viewer's session owner puts back after a second world has built and left.</summary>
    public sealed class State
    {
        internal (StandardMaterial3D mat, float energy)[] Glows;
        internal Node3D[] Lamps;
        internal float Level;
    }

    /// <summary>
    /// The live world's glows, lamps and level. Every world build clears the three (RaceScene.TrackToBuild) and a second world fills them with
    /// its own, so a session owner opening one takes this first and calls Restore after the nested scene has left the tree, then DayNight.ForceReapply.
    /// </summary>
    public static State Snapshot() => new() { Glows = Glows.ToArray(), Lamps = Lamps.ToArray(), Level = Level };

    /// <summary>Puts a Snapshot back; the nested world's entries are dropped (they are the only thing that held its materials).</summary>
    public static void Restore(State s)
    {
        Glows.Clear(); Glows.AddRange(s.Glows);
        Lamps.Clear(); Lamps.AddRange(s.Lamps);
        Level = s.Level;
    }

    /// <summary>
    /// A part as Props.Part would make it (vertex-lit, matt), with its own material that glows in
    /// glowColor at night. By day it is exactly the plain part.
    /// </summary>
    public static MeshInstance3D Part(Mesh mesh, Color color, Color glowColor, float energy, Vector3 pos)
    {
        var mat = new StandardMaterial3D
        {
            AlbedoColor = color,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.PerVertex,
            Roughness = 1f,
            SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
            EmissionEnabled = true,
            Emission = glowColor,
            EmissionEnergyMultiplier = energy * Level,
        };
        mat.SetMeta(EnergyMeta,energy);
        Glows.Add((mat, energy));
        return new MeshInstance3D { Mesh = mesh, MaterialOverride = mat, Position = pos, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
    }

    /// <summary>Marks an unshaded material as a light: at night it keeps (this much of) its day colour.</summary>
    public static T Keep<T>(T mat, float amount = 1f) where T : Material
    {
        mat.SetMeta(KeepMeta, amount);
        return mat;
    }
}
