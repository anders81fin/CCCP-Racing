using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    // --resource-criterion-tests: the steady-state criterion on synthetic series (pure arithmetic): it must pass flat and noisy-flat series, and reject every leak shape.
    void ResourceCriterionTests()
    {
        int fails = 0;
        void C(bool ok, string what) { GD.Print($"  [{(ok ? "ok" : "FAIL")}] {what}"); if (!ok) fails++; }
        var rng = new Random(7);
        IEnumerable<double> Flat(int n, double at, double noise) => Enumerable.Range(0, n).Select(_ => at + (rng.NextDouble() * 2 - 1) * noise);
        C(ResourceCriterion.Evaluate(Flat(8, 14066, 0).ToList()).Ok, "a perfectly flat settled series is steady");
        C(ResourceCriterion.Evaluate(Flat(8, 14066, 6).ToList()).Ok, "a flat series with +-6 noise is steady");
        foreach (var (per, n) in new[] { (1.0, 20), (2.0, 10), (3.0, 8), (5.0, 6), (25.0, 6) })
        {
            var leak = Enumerable.Range(0, n).Select(i => 14066 + per * i).ToList();
            var r = ResourceCriterion.Evaluate(leak);
            C(!r.Ok, $"a leak of {per} objects per cycle over {n} measured cycles is rejected ({r.Why})");
        }
        var step = Enumerable.Range(0, 8).Select(i => i < 4 ? 14066.0 : 15418.0).ToList();
        C(!ResourceCriterion.Evaluate(step).Ok, "a step of 1350 objects (a real, kept change) is rejected");
        var dipRaw = new double[] { 15418, 14066, 15421, 15413, 15418, 15418, 15595, 15418 };
        C(!ResourceCriterion.Evaluate(dipRaw).Ok, "the UNSETTLED raw series of the old flake (a 1350 dip, a +177 hiccup) is not steady either: it is judged only settled");
        C(!ResourceCriterion.Evaluate(new double[] { 1, 2, 3 }).Ok, "too few cycles are refused, not passed");
        foreach (var (bad, name) in new[] { (double.NaN, "NaN"), (double.PositiveInfinity, "+infinity"), (double.NegativeInfinity, "-infinity") })
        {
            var s = new double[] { 14066, 14066, 14066, bad, 14066, 14066 };
            var r = ResourceCriterion.Evaluate(s);
            C(!r.Ok && r.Why.Contains("not finite"), $"a series with one {name} sample is rejected explicitly ({r.Why})");
            var first = new double[] { bad, 14066, 14066, 14066, 14066 };
            C(!ResourceCriterion.Evaluate(first).Ok, $"{name} as the first sample is rejected too");
        }
        C(ResourceCriterion.Evaluate(new double[] { 14066, 14066, 14066, 14066 }).Ok, "control: the same finite series without the bad sample is steady");
        GD.Print(fails == 0 ? "Resource criterion tests: all ok" : $"Resource criterion tests: {fails} FAILED");
        GetTree().Quit(fails == 0 ? 0 : 1);
    }
}
