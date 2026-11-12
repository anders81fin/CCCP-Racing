namespace CCCPRacing.Race;

/// <summary>Who sets the car's controls in the Racing state once the early-exit drives (plan, wreck, LAN, school) did not take the tick. In priority order: the first one that applies wins.</summary>
enum CarControlSource
{
    ClutchKick, Donut, Pulse, FenceTest, CrashTest, CanisterTest, DepotHold, BabushkaCruise, Stomp,
    /// <summary>The autopilot (--autodrive, not a pad's auto drive).</summary>
    Autopilot,
    /// <summary>A pad drives, alone or with the keys, or the pad's auto drive.</summary>
    Pad,
    /// <summary>The keyboard: nothing above applies.</summary>
    Keys,
}

/// <summary>
/// The conditions the choice reads, each already reduced to a yes or no by the scene (RaceScene.CarControl.cs): the clutch-kick test is on, the dev test drives
/// (--donut, --pulse, --fence-test, --crash-test, --canister-test, --at-depot with the autopilot, a babushka hit to cruise to, --stomp), the autopilot is driving
/// (--autodrive and not a pad's auto drive), a pad is in use. A value type with no scene or Godot reference, so the choice is a pure function.
/// </summary>
readonly record struct RacingControlInputs(
    bool ClutchKick, bool Donut, bool Pulse, bool FenceTest, bool CrashTest, bool CanisterTest, bool DepotHold, bool BabushkaCruise, bool Stomp,
    bool Autopilot, bool Pad);

/// <summary>The priority of the Racing state's control sources, as a pure function (tested by --car-control-tests).</summary>
static class CarControlChoice
{
    internal static CarControlSource Racing(in RacingControlInputs i)
    {
        if (i.ClutchKick) return CarControlSource.ClutchKick;
        if (i.Donut) return CarControlSource.Donut;
        if (i.Pulse) return CarControlSource.Pulse;
        if (i.FenceTest) return CarControlSource.FenceTest;
        if (i.CrashTest) return CarControlSource.CrashTest;
        if (i.CanisterTest) return CarControlSource.CanisterTest;
        if (i.DepotHold) return CarControlSource.DepotHold;
        if (i.BabushkaCruise) return CarControlSource.BabushkaCruise;
        if (i.Stomp) return CarControlSource.Stomp;
        if (i.Autopilot) return CarControlSource.Autopilot;
        if (i.Pad) return CarControlSource.Pad;
        return CarControlSource.Keys;
    }
}
