using System;
using System.Collections.Generic;

namespace CCCPRacing.Race;

/// <summary>
/// Progress of the normal single-player track load (the TrackLoadingScreen bar). The world build offers <c>LoadingFrame</c> checkpoints; every call
/// while the card is up is one step (counted whether or not the call really gave a frame, so the count does not depend on the machine's speed).
/// The total is a measured estimate per track, so the bar is approximate: it never shows more than <see cref="Cap"/> until the world is ready, and the card sets 1.0 itself at ready.
/// </summary>
public static class LoadProgress
{
    public const float Cap = 0.97f;

    /// <summary>
    /// Checkpoints of a whole normal build per track. To refresh after changing a builder: run
    /// <c>LOADSTEPS=1 godot-mono --headless --path . res://scenes/tests/PhysicsTest.tscn -- --viewer-windowed-tests --viewer-windowed-only=loadbar</c>
    /// (needs a display); each child log prints "LOADSTEPS ID n" when its build ends. Copy the numbers here. A track missing from the table uses the fallback curve.
    /// </summary>
    static readonly Dictionary<string, int> ExpectedSteps = new() { ["ilyich"] = 780, ["siberia"] = 340, ["atomgrad"] = 225, ["bonus"] = 255 };
    // Measured 2026-10-27 on the windowed game over the real menu route (two runs each, the count varies a little from run to run):
    // ilyich 745/768, siberia 331/321, atomgrad 214/216, bonus 240/247; the values above are rounded up so the bar rarely waits at the cap.
    // (Headless with FORCEBOOT counts more: 930/391/278/282. The windowed numbers are the ones a player has.)

    /// <summary>The bar for a track after this many checkpoints: 0..Cap, never decreasing in steps.</summary>
    public static float Fraction(string track, int steps)
    {
        if (steps <= 0) return 0f;
        if (ExpectedSteps.TryGetValue(track ?? "", out int total) && total > 0)
            return Math.Min(Cap, Cap * steps / total);
        return Cap * 0.9f * (1f - MathF.Exp(-steps / 120f));      // unknown track: a slow saturating curve, always below the cap
    }
}
