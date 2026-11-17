using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Godot;

namespace CCCPRacing.Tests;

/// <summary>
/// The overhead camera across the OCTANE BOOSTER's start and end in the real game (golden can caught by the autopilot, then the saved lap in Watch): the view centre's lead (focus - car)
/// must not jump when the boost's 8 m limit on it starts or ends (ChaseCamera.LimitFocus / LeadCapRate). Flag --camera-boost-scene-tests.
/// </summary>
public partial class PhysicsTest
{
    sealed record BoostFrame(long N, float T, bool Boost, bool Replay, Vector2 Cam, Vector2 Focus, Vector2 Car, float Speed, string MaxLead);

    static List<BoostFrame> BoostFrames(IEnumerable<string> lines)
    {
        var rx = new Regex(@"camprobe (\d+) t=(\S+) boost=(\d) replay=(\d) cam=(\S+),(\S+) focus=(\S+),(\S+) car=(\S+),(\S+) speed=(\S+) maxlead=(\S+)");
        float N(string s) => float.Parse(s, CultureInfo.InvariantCulture);
        var list = new List<BoostFrame>();
        foreach (var l in lines)
        {
            var m = rx.Match(l); if (!m.Success) continue;
            var g = m.Groups;
            list.Add(new BoostFrame(long.Parse(g[1].Value), N(g[2].Value), g[3].Value == "1", g[4].Value == "1", new(N(g[5].Value), N(g[6].Value)), new(N(g[7].Value), N(g[8].Value)), new(N(g[9].Value), N(g[10].Value)), N(g[11].Value), g[12].Value));
        }
        return list;
    }

    /// <summary>The view centre's lead (focus - car), frame to frame: its step (m) and the change of its velocity (m/s) for every frame, over consecutive process frames only.</summary>
    static (float step, float vel)[] BoostLeadMotion(List<BoostFrame> f)
    {
        var r = new (float, float)[f.Count];
        for (int i = 2; i < f.Count; i++)
        {
            if (f[i].N - f[i - 2].N != 2) continue;
            var l0 = f[i - 2].Focus - f[i - 2].Car; var l1 = f[i - 1].Focus - f[i - 1].Car; var l2 = f[i].Focus - f[i].Car;
            float dt = MathF.Max(1e-3f, f[i].T - f[i - 1].T); if (f[i].T - f[i - 1].T > 0.05f || f[i].T <= f[i - 1].T) continue;
            r[i] = ((l2 - l1).Length(), ((l2 - l1) - (l1 - l0)).Length() / dt);
        }
        return r;
    }

    /// <summary>Checks one run: for every start and end of the boost, the lead's step and velocity change within 1.2 s of the switch against the most the same run has anywhere else (3 s away from any switch).</summary>
    void BoostRunChecks(string what, List<BoostFrame> f, int wantSwitches, bool byLimit = false)
    {
        var sw = new List<int>();
        for (int i = 1; i < f.Count; i++) if ((byLimit ? f[i].MaxLead != f[i - 1].MaxLead : f[i].Boost != f[i - 1].Boost) && f[i].Replay == f[i - 1].Replay) sw.Add(i);
        Check(sw.Count >= wantSwitches, $"{what}: the limit switches in the real run ({sw.Count} switches, {f.Count} camera frames)");
        if (sw.Count < wantSwitches) return;
        var m = BoostLeadMotion(f);
        bool Near(int i, float secs) => sw.Any(s => MathF.Abs(f[i].T - f[s].T) < secs);
        float baseStep = 0, baseVel = 0;
        for (int i = 2; i < f.Count; i++) if (!Near(i, 3f) && Near(i, 14f) && f[i].T > f[sw[0]].T - 8f) { baseStep = MathF.Max(baseStep, m[i].step); baseVel = MathF.Max(baseVel, m[i].vel); }
        foreach (int s in sw)
        {
            float step = 0, vel = 0; float lead = (f[s].Focus - f[s].Car).Length();
            for (int i = 2; i < f.Count; i++) if (MathF.Abs(f[i].T - f[s].T) < 1.2f) { step = MathF.Max(step, m[i].step); vel = MathF.Max(vel, m[i].vel); }
            var a = f[Math.Max(0, s - 30)]; var b = f[Math.Min(f.Count - 1, s + 30)];
            float turn = MathF.Abs(MathF.IEEERemainder((b.Car - f[s].Car).Angle() - (f[s].Car - a.Car).Angle(), MathF.Tau)) / MathF.Max(0.1f, b.T - a.T);
            GD.Print($"  {what}: {(byLimit ? "power switch (MaxLead " + f[s - 1].MaxLead + " -> " + f[s].MaxLead + ")" : "boost " + (f[s].Boost ? "starts" : "ends"))} at {f[s].T:F2} s, {f[s].Speed * 3.6f:F0} km/h, car turning {turn:F2} rad/s, lead {lead:F1} m, MaxLead {f[s].MaxLead}: lead step {step:F3} m/frame (rest of the run at most {baseStep:F3}), velocity change {vel:F1} m/s per frame (rest {baseVel:F1})");
            Check(step <= baseStep + 0.3f && vel <= baseVel + 8f, $"{what}: at the {(byLimit ? "power switch" : f[s].Boost ? "boost start" : "boost end")} ({f[s].T:F1} s) the view centre does not jump: step {step:F3} m (rest of the run {baseStep:F3} + 0.3), velocity change {vel:F1} m/s (rest {baseVel:F1} + 8); a hard 8 m switch would jump by lead - 8 in one frame");
        }
    }

    void CameraBoostSceneTests()
    {
        string dir = ProjectSettings.GlobalizePath("res://media/camera-boost-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff"));
        Directory.CreateDirectory(dir); string laps = dir + "/laps"; Directory.CreateDirectory(laps);
        var record = TapeGame(dir + "/data", new[] { "--track=siberia", "--start", "--autodrive", "--ap-speed=1.0", "--no-record", "--golden-test", "--canister-test=110", "--canister-miss=2", "--canister-test-resume",
            "--camera-boost-probe", "--tape-debug", "--tape-session-laps=" + laps, "--tape-quit-laps=1" }, 300, env: new() { ["FUELDBG"] = "1" });
        Check(record.Exit == 0 && !record.TimedOut && !record.Crashed && record.Has("fuel: OCTANE BOOSTER") && record.Has("fuel: boost over"), "camera boost: the real game catches the golden can, boosts and the boost runs out");
        var live = BoostFrames(record.Lines).Where(x => !x.Replay).ToList();
        BoostRunChecks("camera boost (live)", live, 2);
        var lap = Directory.GetDirectories(laps, "lap-*").OrderBy(x => x).FirstOrDefault();
        if (lap == null || live.Count == 0) { Check(false, "camera boost: a saved lap with the boost exists"); return; }
        float t0 = live.First(x => x.Boost).T, t1 = live.Last(x => x.Boost).T;
        var watch = TapeGame(lap, new[] { "--track=siberia", "--watch", "--tape-shoot", "--tape-camera=overview", "--camera-boost-probe", "--tape-debug", "--tape-replay-start=" + MathF.Max(0, t0 - 6f).ToString(CultureInfo.InvariantCulture), "--tape-replay-quit=" + (t1 - t0 + 14f).ToString(CultureInfo.InvariantCulture) }, 200);
        Check(watch.Exit == 0 && !watch.TimedOut && !watch.Crashed && !watch.Has("ERROR:"), "camera boost: Watch replays the saved boost lap from before the boost and exits cleanly");
        BoostRunChecks("camera boost (Watch)", BoostFrames(watch.Lines).Where(x => x.Replay).ToList(), 2);
        GD.Print("CAMERA BOOST TEST OUTPUT " + dir);
    }

    /// <summary>
    /// Stakhanov laps: the power is switched on the move (the real SetPower, as the number keys do) from 100 % to 350 % (the overdrive's MaxLead 8 m starts) and back to 100 % (it ends), in a real
    /// autopilot run on Siberia; the overhead camera's view centre must not jump at either switch. Flag --camera-stakhanov-scene-tests (the Stakhanov half of the OCTANE BOOSTER fix, which switches the same limit).
    /// </summary>
    void CameraStakhanovSceneTests()
    {
        string dir = ProjectSettings.GlobalizePath("res://media/camera-stakhanov-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff"));
        Directory.CreateDirectory(dir);
        foreach (var (track, seq) in new[] { ("siberia", "20:350,34:100") })
        {
            var run = TapeGame(dir + "/" + track, new[] { "--track=" + track, "--start", "--training", "--power=100", "--unlock-all", "--autodrive", "--no-record", "--camera-boost-probe", "--power-switch=" + seq, "--tape-quit-at=" + (seq.Split(',')[1].Split(':')[0] == "18" ? "30" : "46") }, 300);
            Check(run.Exit == 0 && !run.TimedOut && !run.Crashed && !run.Has("ERROR:") && run.All("powerswitch").Count() == 2, $"camera stakhanov ({track}): the real game runs the autopilot and the two real power switches ({seq}) happen");
            BoostRunChecks($"camera stakhanov ({track})", BoostFrames(run.Lines).Where(x => !x.Replay).ToList(), 2, byLimit: true);
        }
        GD.Print("CAMERA STAKHANOV TEST OUTPUT " + dir);
    }
}
