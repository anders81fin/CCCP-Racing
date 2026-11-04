using System;

namespace CCCPRacing.Replay;

/// <summary>Version-3 editorial motion, evaluated from tape time. Null on legacy static shots.
/// Position, aim (LapFreeCamera.Look) and lens remain independent.</summary>
public sealed record LapCameraMotion
{
    public const string Fixed = "Fixed", World = "World", Side = "Side", Helicopter = "Helicopter";
    public string Tracking { get; init; } = Fixed;
    public float Duration { get; init; } = 6;
    public float Rise { get; init; }
    public float Dolly { get; init; }
    public float? EndFov { get; init; }

    public string Validate()
    {
        if (Tracking is not (Fixed or World or Side or Helicopter)) return "unknown camera tracking behavior";
        if (!float.IsFinite(Duration) || Duration < .5f || Duration > 120) return "camera duration must be 0.5..120 seconds";
        if (!float.IsFinite(Rise) || Math.Abs(Rise) > 200 || !float.IsFinite(Dolly) || Math.Abs(Dolly) > 200) return "camera movement must be within 200 metres";
        if (EndFov is float f && (!float.IsFinite(f) || f < 10 || f > 120)) return "camera end lens must be 10..120 degrees";
        return null;
    }
}
