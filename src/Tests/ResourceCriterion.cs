using System;
using System.Collections.Generic;
using System.Linq;

namespace CCCPRacing.Tests;

/// <summary>
/// The steady-state criterion for repeated open/close cycles (gate R). It is judged on a GC-SETTLED series (the test collects before it samples: never the game), after a warm-up
/// that the caller drops, and it asks three things of the measured cycles: the least-squares slope (objects per cycle), the total drift that slope implies over the window, and the spread
/// (max - min). It does NOT use the lower of two halves or any other shortcut that a single low sample can satisfy. Defaults: slope at most 0.5 per cycle, drift at most 15 objects over the
/// window, spread at most 60. A leak of 2 objects per cycle over 10 measured cycles drifts 18 and is rejected; the control tests (and an injected leak in the real cycle series) prove it.
/// </summary>
public static class ResourceCriterion
{
    public const double SlopeMax = 0.5, DriftMax = 15, SpreadMax = 60;

    public readonly record struct Result(bool Ok, double Slope, double Drift, double Spread, string Why);

    public static Result Evaluate(IReadOnlyList<double> series, double slopeMax = SlopeMax, double driftMax = DriftMax, double spreadMax = SpreadMax)
    {
        int n = series.Count;
        if (n < 4) return new Result(false, 0, 0, 0, $"only {n} measured cycles (need at least 4)");
        for (int i = 0; i < n; i++)      // (comparisons against NaN are all false: a non-finite sample would otherwise slip through every bound)
            if (!double.IsFinite(series[i])) return new Result(false, double.NaN, double.NaN, double.NaN, $"sample {i} is not finite ({series[i]}): the telemetry is broken, not steady");
        double mx = (n - 1) / 2.0, my = series.Average(), sxy = 0, sxx = 0;
        for (int i = 0; i < n; i++) { sxy += (i - mx) * (series[i] - my); sxx += (i - mx) * (i - mx); }
        double slope = sxy / sxx, drift = slope * (n - 1), spread = series.Max() - series.Min();
        var bad = new List<string>();
        if (slope > slopeMax) bad.Add($"slope {slope:0.00}/cycle > {slopeMax}");
        if (drift > driftMax) bad.Add($"drift {drift:0.0} > {driftMax}");
        if (spread > spreadMax) bad.Add($"spread {spread:0} > {spreadMax}");
        return new Result(bad.Count == 0, slope, drift, spread, bad.Count == 0 ? "steady" : string.Join("; ", bad));
    }

    /// <summary>The criterion over a series with its first <paramref name="warmup"/> cycles dropped.</summary>
    public static Result AfterWarmup(IReadOnlyList<double> series, int warmup) => Evaluate(series.Skip(warmup).ToList());
}
