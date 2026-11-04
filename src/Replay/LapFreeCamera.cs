using System;

namespace CCCPRacing.Replay;

/// <summary>Camera-key framing (v2 static; v3 optional motion), independent of the original driving/world tape.
/// Follow uses the captured world-space offset; fixed shots use XYZ. Aim tracking is independent.</summary>
public sealed record LapFreeCamera
{
    public float X { get; init; }
    public float Y { get; init; }
    public float Z { get; init; }
    public float OffsetX { get; init; }
    public float OffsetY { get; init; }
    public float OffsetZ { get; init; }
    public float Yaw { get; init; }
    public float Pitch { get; init; }
    public float Fov { get; init; } = 45f;
    public float Near { get; init; } = .5f;
    public float Far { get; init; } = 260f;
    public bool Follow { get; init; }
    public bool Look { get; init; }
    public LapCameraMotion Motion { get; init; }

    public string Validate()
    {
        foreach (float f in new[] { X, Y, Z, OffsetX, OffsetY, OffsetZ, Yaw, Pitch, Fov, Near, Far })
            if (!float.IsFinite(f)) return "free camera has a non-finite value";
        if (Math.Abs(Yaw) > MathF.PI + .001f || Math.Abs(Pitch) > 89.01f * MathF.PI / 180f) return "free camera angle is out of range";
        if (Fov <= 0 || Fov >= 179 || Near <= 0 || Far <= Near) return "free camera lens is out of range";
        return Motion?.Validate();
    }
}
