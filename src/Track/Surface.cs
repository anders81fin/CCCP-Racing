using Godot;

namespace CCCPRacing.Track;

public enum SurfaceType : byte
{
    Grass,
    Concrete,
    Gravel,
    Mud,
    Puddle,
    Pothole,
    Dirt,
    Field,
    Runway,      // military highway strip: smooth, well-kept concrete (no potholes, no slab joints)
    DeadGrass,   // withered ground around the radar station
    Water,       // rivers and ponds deeper than a hand: drags hard, the car wades
    Shallow,     // the ford's gravel bar and the river's edge: a splash
    Ice,         // the frozen river, black ice on the winter road: almost no grip, no noise
    Snow,        // the packed snow of a winter road: loose, a little better than mud
    DeepSnow,    // the winter ground off the road: the car ploughs, slow and slippery
}

/// <summary>How a surface feels under the tyres.</summary>
public readonly record struct SurfaceInfo(
    string Name,
    float Grip,          // friction multiplier
    float Rolling,       // rolling resistance, fraction of wheel load
    float BumpAmp,       // micro-bump amplitude in metres (physics only)
    bool Loose,          // throws gravel/mud, crunchy sound instead of squeal
    float WetDrag = 0f,  // drag per wheel ∝ speed² (water, deep mud), N per (m/s)² at normal load
    float Aquaplane = 0f,  // speed (m/s) at which grip has halved; 0 = grip not speed dependent
    float WetLoss = 0f,
    float DriveBite = 1f,  // longitudinal grip peak, separate from side grip
    float SpinGrip = 0.76f, // grip left in excessive wheelspin, relative to the peak
    float PeakDriveSlip = 0.12f,
    float BrakeBite = 1f); // braking grip relative to the surface's base friction

public static class Surfaces
{
    public const int Count = 15;

    public static readonly SurfaceInfo[] Info =
    {
        new("grass",    0.55f, 0.080f, 0.035f, true, WetDrag: 0.7f, WetLoss: 0.15f),
        new("concrete", 1.00f, 0.015f, 0.000f, false, WetLoss: 0.3f, DriveBite: 1.08f, SpinGrip: 0.70f, PeakDriveSlip: 0.07f, BrakeBite: 1.35f),
        new("gravel",   0.80f, 0.020f, 0.020f, true, WetLoss: 0.12f, SpinGrip: 0.78f, PeakDriveSlip: 0.13f, BrakeBite: 1.15f),
        new("mud",      0.42f, 0.060f, 0.045f, true, WetDrag: 1.2f, WetLoss: 0.08f, SpinGrip: 0.72f, PeakDriveSlip: 0.22f),
        new("puddle",   0.85f, 0.015f, 0.000f, true, WetDrag: 7f, Aquaplane: 17f),
        new("pothole",  0.90f, 0.020f, 0.000f, false, WetLoss: 0.3f, BrakeBite: 1.35f),
        new("dirt",     0.75f, 0.022f, 0.025f, true, WetLoss: 0.18f, BrakeBite: 1.10f),
        new("field",    0.50f, 0.120f, 0.000f, true, WetDrag: 0.45f, WetLoss: 0.12f),
        new("runway",   1.00f, 0.012f, 0.000f, false, WetLoss: 0.3f, DriveBite: 1.15f, SpinGrip: 0.72f, PeakDriveSlip: 0.065f, BrakeBite: 1.35f),
        new("deadgrass",0.55f, 0.080f, 0.035f, true, WetDrag: 0.7f, WetLoss: 0.15f),
        new("water",    0.55f, 0.040f, 0.000f, true, WetDrag: 9f, Aquaplane: 12f),
        new("shallow",  0.75f, 0.022f, 0.010f, true, WetDrag: 1.6f),
        new("ice",      0.36f, 0.010f, 0.000f, false, SpinGrip: 0.70f, PeakDriveSlip: 0.20f),
        new("snow",     0.60f, 0.025f, 0.015f, true, PeakDriveSlip: 0.16f, BrakeBite: 1.10f),
        new("deepsnow", 0.40f, 0.090f, 0.030f, true, WetDrag: 0.8f),
    };

    public static SurfaceInfo Get(SurfaceType s) => Info[(int)s];

    /// <summary>How wet the world is, 0 dry .. 1 soaked (the rain: Race.Weather sets it per frame; 0 off its tracks and in the tests).</summary>
    public static float Wetness;

    /// <summary>Grip at a given sliding/rolling speed: water lifts the tyre as speed rises.</summary>
    public static float GripAt(this SurfaceInfo si, float speed)
    {
        float grip = si.Grip * (1f - si.WetLoss * Wetness);
        if (si.Aquaplane <= 0f) return grip;
        float k = speed / si.Aquaplane;
        return grip / (1f + k * k);
    }

    public static SurfaceType Parse(string name)
    {
        for (int i = 0; i < Count; i++)
            if (Info[i].Name == name) return (SurfaceType)i;
        GD.PushWarning($"Unknown surface '{name}', using concrete");
        return SurfaceType.Concrete;
    }
}
