using System;
using CCCPRacing.Race;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    /// <summary>
    /// PACELAB=1: the co-driver's bends on every track, and a car driven twice round the centre line at 30 m/s:
    /// each bend must be called exactly once a lap, ahead of the bend.
    /// </summary>
    void PaceLab()
    {
        foreach (string id in new[] { "ilyich", "siberia", "atomgrad", "bonus" })
        {
            var t = TrackBuilder.Build(TrackDef.Load($"res://tracks/{id}.json"));
            var notes = new PaceNotes();
            int[] bySeverity = new int[5];
            foreach (var b in PaceNotes.Find(t)) bySeverity[b.Severity]++;
            int n = t.Center.Length, calls = 0, early = 0;
            for (int lap = 0; lap < 2; lap++)
                for (int i = 0; i < n; i++)
                    if (notes.Tick(t, t.Center[i], 30f, out var bend))
                    {
                        calls++;
                        int dist = (bend.Start - i + n) % n;
                        if (dist < 8 || dist > 150) early++;
                    }
            Check(notes.Bends.Count > 0 && calls == 2 * notes.Bends.Count && early == 0, $"pace notes {id}: {notes.Bends.Count} bends (medium {bySeverity[2]}, sharp {bySeverity[3]}, hairpin {bySeverity[4]}), {calls} calls in 2 laps, {early} at a silly distance");
        }
    }
}
