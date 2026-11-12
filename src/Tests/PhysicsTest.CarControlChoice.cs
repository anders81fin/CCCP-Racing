using System;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    /// <summary>
    /// The Racing state's control priority (CarControlChoice, flag --car-control-tests). Not a copy of the table: every one of the 2048 combinations of the 11 conditions is
    /// checked against properties. The condition order of RacingControlInputs is the priority order and CarControlSource has the same order, so "the first condition that
    /// holds wins" is read here as "the lowest set bit", an independent formulation of the scene's if-chain.
    /// </summary>
    void CarControlChoiceTests()
    {
        GD.Print("Car control choice (priority of the Racing state's control sources)");
        const int n = 11;
        var sources = Enum.GetValues<CarControlSource>();
        Check(sources.Length == n + 1 && (int)CarControlSource.Keys == n && (int)CarControlSource.ClutchKick == 0, "the sources are the 11 conditions in priority order, then the keyboard");

        static RacingControlInputs From(int mask) => new(
            (mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0, (mask & 8) != 0, (mask & 16) != 0, (mask & 32) != 0,
            (mask & 64) != 0, (mask & 128) != 0, (mask & 256) != 0, (mask & 512) != 0, (mask & 1024) != 0);

        int wrongFirst = 0, wrongLower = 0, wrongHigher = 0;
        for (int mask = 0; mask < 1 << n; mask++)
        {
            var got = CarControlChoice.Racing(From(mask));
            int expect = mask == 0 ? n : System.Numerics.BitOperations.TrailingZeroCount((uint)mask);
            if ((int)got != expect) wrongFirst++;
            for (int j = 0; j < n; j++)
            {
                int flipped = mask ^ (1 << j);
                var other = CarControlChoice.Racing(From(flipped));
                if (j > expect && other != got) wrongLower++;                                   // a condition below the winner's priority never changes the choice
                if (j < expect && (mask & (1 << j)) == 0 && (int)other != j) wrongHigher++;    // switching on a higher-priority condition always takes over
            }
        }
        Check(wrongFirst == 0, $"all 2048 combinations: the highest-priority condition that holds is chosen, none: the keyboard ({wrongFirst} wrong)");
        Check(wrongLower == 0, $"a lower-priority condition never changes the choice ({wrongLower} wrong)");
        Check(wrongHigher == 0, $"a higher-priority condition switched on always takes over ({wrongHigher} wrong)");

        // The combinations the game meets, named.
        Check(CarControlChoice.Racing(default) == CarControlSource.Keys, "no condition: the keyboard");
        Check(CarControlChoice.Racing(new RacingControlInputs(false, false, false, false, false, false, false, false, false, true, true)) == CarControlSource.Autopilot,
            "--autodrive with a pad in use: the autopilot drives (the pad branch is after it)");
        Check(CarControlChoice.Racing(new RacingControlInputs(false, false, false, false, false, false, false, false, false, false, true)) == CarControlSource.Pad, "a pad alone: the pad");
        Check(CarControlChoice.Racing(new RacingControlInputs(true, true, true, true, true, true, true, true, true, true, true)) == CarControlSource.ClutchKick, "the clutch-kick test beats everything");
        Check(CarControlChoice.Racing(new RacingControlInputs(false, false, false, false, false, false, true, false, false, true, false)) == CarControlSource.DepotHold,
            "--at-depot stands the car still before the autopilot may drive");
    }
}
