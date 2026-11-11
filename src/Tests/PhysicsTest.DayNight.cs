using System;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    /// <summary>Day and night: phases by lap, the allowance of the lap being driven, smooth light, the board's night mark.</summary>
    void DayNightTest()
    {
        GD.Print("Day and night");
        string phases = "";
        for (int lap = 1; lap <= 24; lap++) phases += "dSuNa"[(int)DayNight.PhaseForLap(lap)];
        Check(phases == "ddSSuuNNNNaaddSSuuNNNNaa" && DayNight.PhaseForLap(25) == DayNight.Phase.Day && DayNight.PhaseForLap(39) == DayNight.Phase.Sunset,
            $"laps 1-2 day, 3-4 sunset, 5-6 dusk, 7-10 night, 11-12 dawn, then the next day ({phases})");
        foreach (var p in Enum.GetValues<DayNight.Phase>())
            Check(DayNight.PhaseForLap(DayNight.FirstLap(p)) == p && (p == DayNight.Phase.Day || DayNight.PhaseForLap(DayNight.FirstLap(p) - 1) != p),
                $"{p} starts at lap {DayNight.FirstLap(p)}");
        Check(DayNight.AllowanceFor(DayNight.Phase.Day) == 0 && DayNight.AllowanceFor(DayNight.Phase.Sunset) == 0.5f && DayNight.AllowanceFor(DayNight.Phase.Dusk) == 1f
              && DayNight.AllowanceFor(DayNight.Phase.Night) == 2f && DayNight.AllowanceFor(DayNight.Phase.Dawn) == 0.75f, "allowance: day 0, sunset +0.5, dusk +1.0, night +2.0, dawn +0.75 s");
        Check(!DayNight.IsNight(DayNight.Phase.Sunset) && DayNight.IsNight(DayNight.Phase.Dusk) && DayNight.IsNight(DayNight.Phase.Night) && !DayNight.IsNight(DayNight.Phase.Dawn),
            "night laps: those started at dusk or at night");

        // The allowance is the lap's, from its start: it holds all the way round and changes at the line.
        var dn = new DayNight();
        bool held = true;
        dn.LapOffset = 0;
        foreach (int done in new[] { 1, 2, 4, 5, 10, 11, 12, 13 })
        {
            float first = -1;
            for (float f = 0; f <= 1f; f += 0.1f)
            {
                DayNightState(dn, done, f);
                if (first < 0) first = dn.ClimateAllowance;
                held &= dn.ClimateAllowance == first && first == DayNight.AllowanceFor(DayNight.PhaseForLap(done + 1));
            }
        }
        DayNightState(dn, 3, 0.99f);
        float before = dn.ClimateAllowance;
        DayNightState(dn, 4, 0f);
        Check(held && before == 0.5f && dn.ClimateAllowance == 1f, "the allowance holds through a lap (the phase at its start) and steps at the line");
        DayNightState(dn, 4, 0.5f);
        Check(dn.ClimateAllowance == 1f, "lap 5 (dusk) half way round: +1.0 s");
        dn.Free();

        // The light: exactly the day to the end of lap 2, then smooth (no step bigger than a small one per 1/200 lap).
        var d0 = DayNight.LookAt(0);
        bool plain = true;
        foreach (float x in new[] { 2f, 12f, 12.5f, 14f, 24f })
        {
            var d = DayNight.LookAt(x);
            plain &= DayNight.IsDay(x) && d.SunEnergy == d0.SunEnergy && d.SunAz == 0 && d.SunElev == d0.SunElev && d.Baked == d0.Baked && d.LitMix == 0 && d.Unlit == Colors.White;
        }
        Check(plain && !DayNight.IsDay(2.01f) && !DayNight.IsDay(11.99f) && !DayNight.IsDay(14.01f), "laps 1-2 of every day: the plain day (the dawn ends in it exactly)");
        float worst = 0, worstAt = 0;
        var prev = DayNight.LookAt(0);
        for (float x = 0.005f; x <= 25f; x += 0.005f)
        {
            var l = DayNight.LookAt(x);
            float step = Math.Max(Math.Abs(l.SunEnergy - prev.SunEnergy), Math.Max(Math.Abs(l.LitMix - prev.LitMix),
                Math.Max(Diff(l.Sky, prev.Sky), Math.Max(Diff(l.Ambient, prev.Ambient), Math.Max(Diff(l.Baked, prev.Baked), Diff(l.Unlit, prev.Unlit))))));
            step = Math.Max(step, Math.Abs(DayNight.BeamsAt(x) - DayNight.BeamsAt(x - 0.005f)));
            // The sun swaps to the moon only while dark (the jump in direction is unseen).
            if (Math.Abs(l.SunElev - prev.SunElev) > 1f || Math.Abs(l.SunAz - prev.SunAz) > 1f)
                step = Math.Max(step, Math.Max(l.SunEnergy, prev.SunEnergy) > 0.02f ? 1f : 0f);
            if (step > worst) { worst = step; worstAt = x; }
            prev = l;
        }
        GD.Print($"  biggest step per 1/200 lap: {worst:F4} at {worstAt:F3} laps");
        Check(worst < 0.03f, "the light changes smoothly within and across laps (no pop at the line)");
        Check(DayNight.BeamsAt(3.99f) == 0 && DayNight.BeamsAt(4.3f) == 1 && DayNight.BeamsAt(10.9f) == 1 && DayNight.BeamsAt(11.5f) == 0
              && DayNight.BeamsAt(15.99f) == 0 && DayNight.BeamsAt(16.3f) == 1,
            "headlights: off in the sunset, on from early in the dusk to the dawn, again the next day");
        Check(DayNight.LookAt(8f).Sky.Luminance < 0.08f && DayNight.LookAt(8f).Ambient.Luminance > 0.1f, "night: a dark sky, but moonlight enough to see the road edges");

        DayNightBoardTest();
    }

    static float Diff(Color a, Color b) => Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));

    /// <summary>Drives a bare DayNight's lap bookkeeping (no scene: the rendering is not touched while inactive).</summary>
    static void DayNightState(DayNight dn, int done, float frac) => dn.SetLapsForTest(done, frac);

    void DayNightBoardTest()
    {
        string path = "user://test_night_leaderboard.cfg";
        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(path));
        // A rally board as the game wrote it before the night (no night key), one pad lap.
        const string Old = "[driver]\n\nlast_name=\"ANTTI\"\n\n[entry_0]\n\nname=\"ANTTI\"\ntime=47.211178\ndate=\"2026-09-28\"\n\n"
            + "[entry_1]\n\nname=\"TOVARISH\"\ntime=50.4452\ndate=\"2026-09-28\"\ninput=\"pad\"\n";
        using (var f = FileAccess.Open(path, FileAccess.ModeFlags.Write)) f.StoreString(Old);
        var b = Leaderboard.Load(path);
        bool day = b.Entries.Count == 2;
        foreach (var e in b.Entries) day &= !e.Night;
        Check(day, "an old board without night keys: every lap is a day lap");
        b.Save();
        Check(FileAccess.GetFileAsString(path) == Old, "load and save of an old board gives the same file byte for byte");
        b.Insert("NOCHNOY", 49f, "2026-09-29", 0, pad: false, night: true);
        b.Save();
        string text = FileAccess.GetFileAsString(path);
        var l = Leaderboard.Load(path);
        bool ok = l.Entries.Count == 3 && l.Entries[1].Name == "NOCHNOY" && l.Entries[1].Night && !l.Entries[1].Pad
                  && l.Entries[0] == b.Entries[0] && l.Entries[2] == b.Entries[2] && !l.Entries[0].Night && l.Entries[2].Pad && !l.Entries[2].Night;
        Check(text.Contains("night=true") && text.Split("night=").Length == 2, "a night lap saves night=true (day laps no key)");
        Check(ok, "the night lap loads back as a night lap; the other laps and fields are unchanged");
        l.Insert("OKTAN", 47f, "2026-09-29", 0, pad: false, night: false, boost: true);
        l.Save();
        text = FileAccess.GetFileAsString(path);
        var lb = Leaderboard.Load(path);
        Check(text.Split("boost=").Length == 2 && lb.Entries[0].Name == "OKTAN" && lb.Entries[0].Boost && !lb.Entries[1].Boost && lb.Entries[2].Night,
            "an OCTANE BOOSTER lap saves boost=true and loads back marked; the others keep no boost key");
        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(path));
    }
}
