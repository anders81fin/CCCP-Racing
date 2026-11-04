using System;
using CCCPRacing.Car;
using CCCPRacing.Race;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    /// <summary>SCHOOLLAB=1: tries the driving school's demo sites and prints how each technique does.</summary>
    void SchoolLab()
    {
        var track = TrackBuilder.Build(TrackDef.Load("res://tracks/bonus.json"));
        CarSim New() => new(new CarParams(), track) { Obstacles = track.Obstacles, WorldMax = track.WorldSize - 4 };
        // Mud and gravel: speed after 1.5 / 3 / 6 s by throttle (analog, and key taps).
        foreach (int at in new[] { 372, 440, 150 })
            foreach (string thr in new[] { "1.0", "0.6", "0.4", "tap.25/.25", "tap.3/.2" })
            {
                var car = New();
                SchoolScript.SetRolling(car, track, SchoolScript.RoadPoint(track, at, 0), track.CenterDir(at), 0);
                string o = "";
                float spin = 0;
                for (float t = 0; t < 6f; t += Dt)
                {
                    float th;
                    if (thr.StartsWith("tap"))
                    {
                        var on = thr[3..].Split('/');
                        float a = float.Parse("0" + on[0], System.Globalization.CultureInfo.InvariantCulture), b = float.Parse("0" + on[1], System.Globalization.CultureInfo.InvariantCulture);
                        th = t % (a + b) < a ? 1f : 0f;
                    }
                    else th = float.Parse(thr, System.Globalization.CultureInfo.InvariantCulture);
                    car.SetControls(th, 0, SchoolScript.SteerRoad(car, track), false, false);
                    car.Step(Dt);
                    if (car.Wheels[2].Spinning) spin += Dt;
                    foreach (float m in new[] { 1.5f, 3f })
                        if (t < m && t + Dt >= m) o += $" {m}s {car.Speed * 3.6f:F0}";
                }
                GD.Print($"  lab launch {at} ({track.CenterSurf[at]}) throttle {thr}:{o} 6s {car.Speed * 3.6f:F0} km/h, spin {spin:F1} s");
            }
        // Through slippery bends: full throttle against gentle, from a crawl (off the line, worst side).
        foreach (int at in new[] { 372, 395, 432, 450, 490, 770 })
            foreach (float th in new[] { 1f, 0.5f, 0.35f })
            {
                var car = New();
                SchoolScript.SetRolling(car, track, SchoolScript.RoadPoint(track, at, 0), track.CenterDir(at), 15 / 3.6f);
                float maxOver = -99, yawRate = 0;
                int reached = at;
                float t = 0;
                for (; t < 6f; t += Dt)
                {
                    car.SetControls(th, 0, SchoolScript.SteerRoad(car, track), false, false);
                    car.Step(Dt);
                    maxOver = Math.Max(maxOver, SchoolScript.OffRoad(track, new Vector2(car.Pos.X, car.Pos.Z), out reached));
                    yawRate = Math.Max(yawRate, Math.Abs(car.AngVel.Y));
                }
                GD.Print($"  lab bendgas {at} ({track.CenterSurf[at]}) throttle {th:F2}: reached {reached} m, {car.Speed * 3.6f:F0} km/h, max over edge {maxOver:F1} m, max yaw rate {yawRate:F2}");
            }
        // Handbrake turn on the runway at 40 / 50 km/h.
        foreach (float v0 in new[] { 35f, 45f, 55f })
            foreach (float hold in new[] { 0.8f, 1.2f })
            {
                var car = New();
                SchoolScript.SetRolling(car, track, SchoolScript.RoadPoint(track, 45, 0), track.CenterDir(45), v0 / 3.6f);
                var chk = new HandbrakeTurnCheck();
                Verdict hv = Verdict.Pending;
                float t = 0;
                for (; t < 5f && hv == Verdict.Pending; t += Dt)
                {
                    bool pull = t > 0.3f && t < 0.3f + hold;
                    car.SetControls(0f, 0f, t > 0.3f && t < 0.3f + hold + 0.4f ? -1f : 0f, pull, false);
                    car.Step(Dt);
                    hv = chk.Feed(t, MathF.Atan2(car.Forward.X, car.Forward.Z), car.Speed * 3.6f, pull);
                }
                GD.Print($"  lab handbrake {v0} km/h hold {hold}: {hv} at {t:F2} s, best {chk.BestDeg:F0} deg, travelled to {track.NearestCenter(new Vector2(car.Pos.X, car.Pos.Z), out float off)} m, {off:F1} m off centre");
            }
        // Clutch launch on the runway: plain, and clutch dumped at 1.0 s.
        foreach (bool clutch in new[] { false, true })
        {
            var car = New();
            SchoolScript.SetRolling(car, track, SchoolScript.RoadPoint(track, 15, 0), track.CenterDir(15), 0);
            var chk = new ClutchLaunchCheck();
            Verdict cv = Verdict.Pending;
            float t = 0, at50 = -1;
            for (; t < 8f; t += Dt)
            {
                car.Clutch = clutch && t < 1.2f;
                car.SetControls(1f, 0, SchoolScript.SteerRoad(car, track), false, false);
                car.Step(Dt);
                if (cv == Verdict.Pending) cv = chk.Feed(Dt, car.Speed * 3.6f, car.Clutch, car.Rpm);
                if (at50 < 0 && car.Speed * 3.6f >= 50) at50 = t;
            }
            GD.Print($"  lab clutch {clutch}: {cv} {chk.Why} launch {chk.LaunchTime:F2} s, 50 km/h at {at50:F2} s");
        }
        for (int bp = -24; bp <= -8; bp += 2)
            foreach (float v0 in new[] { 80f, 85f, 90f })
                foreach (string how in new[] { "stomp", "pump" })
                {
                    var car = New();
                    int entry = 1008;
                    SchoolScript.SetRolling(car, track, SchoolScript.RoadPoint(track, entry - 45, 0), track.CenterDir(entry - 45), v0 / 3.6f);
                    float t = 0, t0 = -1, maxOver = -99;
                    bool released = false;
                    for (int k = 0; t < 10f; t += Dt, k++)
                    {
                        if (k % 2 == 1) { car.Step(Dt); continue; }     // the game sets the controls once per 1/120 s tick of two substeps
                        float over = SchoolScript.OffRoad(track, new Vector2(car.Pos.X, car.Pos.Z), out int idx);
                        int s = SchoolScript.Along(track, entry, idx);
                        if (s > -5) maxOver = Math.Max(maxOver, over);
                        float steer = SchoolScript.SteerRoad(car, track);
                        if (s < bp && t0 < 0) car.SetControls(car.ForwardSpeed * 3.6f < v0 ? 1f : 0f, 0, steer, false, false);
                        else
                        {
                            if (t0 < 0) t0 = t;
                            if (how == "stomp") car.SetControls(0, car.Speed * 3.6f > 4 ? 1f : 0f, steer > 0 ? 1f : -1f, false, false);
                            else if (!released && car.Speed * 3.6f > 38) car.SetControls(0, (t - t0) % 0.3f < 0.17f ? 1f : 0f, steer, false, false);
                            else { released = true; car.SetControls(car.ForwardSpeed * 3.6f < 35 ? 1f : 0f, 0, steer, false, false); }
                        }
                        car.Step(Dt);
                        if (s > 50) break;
                    }
                    SchoolScript.OffRoad(track, new Vector2(car.Pos.X, car.Pos.Z), out int end);
                    GD.Print($"  lab corner brake at {bp} from {v0} {how}: max over edge {maxOver:F1} m, ends at metre {end}, {car.Speed * 3.6f:F0} km/h, t {t:F1}");
                }
        foreach (int bend in new[] { 240, 1020 })
            foreach (float v0 in new[] { 70f, 85f })
                foreach (string how in new[] { "stomp", "pump", "none", "early" })
                {
                    var car = new CarSim(new CarParams(), track) { Obstacles = track.Obstacles, WorldMax = track.WorldSize - 4 };
                    int entry = bend - 12, start = entry - 45;
                    SchoolScript.SetRolling(car, track, SchoolScript.RoadPoint(track, start, 0), track.CenterDir(start), v0 / 3.6f);
                    var check = new PumpBrakeCheck(40);
                    Verdict v = Verdict.Pending;
                    float t = 0, brakeAt = -1;
                    for (; t < 12f && v == Verdict.Pending; t += Dt)
                    {
                        float over = SchoolScript.OffRoad(track, new Vector2(car.Pos.X, car.Pos.Z), out int idx);
                        int s = SchoolScript.Along(track, entry, idx);
                        float steer = SchoolScript.SteerRoad(car, track);
                        bool braking = s >= -22;
                        if (braking && brakeAt < 0) brakeAt = t;
                        float bt = t - brakeAt;
                        float brake = !braking ? 0 : how switch
                        {
                            "stomp" => 1f, "pump" => bt % 0.3f < 0.17f ? 1f : 0f, "early" => bt < 1.2f ? 1f : 0f, _ => 0f,
                        };
                        car.SetControls(!braking && car.ForwardSpeed * 3.6f < v0 ? 1f : 0f, brake, steer, false, false);
                        car.Step(Dt);
                        v = check.Feed(s, over, car.Speed * 3.6f);
                    }
                    GD.Print($"  lab bend {bend} from {v0} km/h {how,-5}: {v} {check.Why} maxOver {check.MaxOver:F1} min {check.MinKmh:F0} km/h t {t:F1}");
                }
        // Mud: full throttle against gentle.
        foreach (int at in new[] { 366, 372, 385 })
            foreach (float thr in new[] { 1f, 0.7f, 0.5f, 0.4f, 0.3f })
            {
                var car = new CarSim(new CarParams(), track) { Obstacles = track.Obstacles, WorldMax = track.WorldSize - 4 };
                SchoolScript.SetRolling(car, track, SchoolScript.RoadPoint(track, at, 0), track.CenterDir(at), 0);
                for (float t = 0; t < 4f; t += Dt)
                {
                    car.SetControls(thr, 0, SchoolScript.SteerRoad(car, track), false, false);
                    car.Step(Dt);
                }
                GD.Print($"  lab mud {at} throttle {thr:F1}: {car.Speed * 3.6f:F0} km/h after 4 s, surface {car.Wheels[2].Surface}");
            }
    }

    /// <summary>
    /// The driving school's checks fed made-up data, the courses' texts in the pixel font, and the
    /// [tutorial] section of settings.cfg round-tripped without touching the other sections.
    /// </summary>
    string _kickWhy;

    void SchoolTest()
    {
        GD.Print("Driving school");
        if (System.Environment.GetEnvironmentVariable("SCHOOLLAB") != null) SchoolLab();

        // Pump braking: on the road and slowed in the bend passes; off the road or too fast fails.
        var pb = new PumpBrakeCheck(40);
        Verdict v = Verdict.Pending;
        for (int s = -30; s <= 45 && v == Verdict.Pending; s++) v = pb.Feed(s, s < 0 ? -2f : 1.5f, s < 10 ? 80 - s * 4 : 38);
        Check(v == Verdict.Success && pb.MinKmh <= 45, $"pump brake: 1.5 m over the edge, slowed to {pb.MinKmh:F0} km/h in the bend: success");
        pb = new PumpBrakeCheck(40);
        v = Verdict.Pending;
        for (int s = -30; s <= 45 && v == Verdict.Pending; s++) v = pb.Feed(s, s < 5 ? -1f : (s - 5) * 0.8f, 60);
        Check(v == Verdict.Fail && pb.Why == "YOU LEFT THE ROAD", "pump brake: locked wheels go straight off the road: fail at once");
        pb = new PumpBrakeCheck(40);
        v = Verdict.Pending;
        for (int s = -30; s <= 45 && v == Verdict.Pending; s++) v = pb.Feed(s, -1f, 60);
        Check(v == Verdict.Fail && pb.Why.StartsWith("TOO FAST"), "pump brake: on the road but never slowed: fail at the exit");
        pb = new PumpBrakeCheck(40);
        Check(pb.Feed(-20, 8f, 80) == Verdict.Pending, "pump brake: off the road well before the bend does not count yet");

        // Clutch kick: the kick, then the whole bend in a held slide passes; a tail that grips, a spin,
        // stopping, the road left or no kick fail.
        Verdict Kick(Func<int, float> slip, Func<int, float> over = null, bool used = true, float kmh = 65)
        {
            var kc = new KickSlideCheck(92);
            Verdict r = Verdict.Pending;
            for (int s = -30; s <= 100 && r == Verdict.Pending; s++) r = kc.Feed(s, over?.Invoke(s) ?? -2f, used && s >= -10, slip(s), kmh, "USE THE CLUTCH IN THE BEND");
            _kickWhy = kc.Why;
            return r;
        }
        Check(Kick(s => s < -8 ? 0 : 22) == Verdict.Success, "clutch kick: 22 degrees round the whole bend: success");
        Check(Kick(s => s < -8 ? 0 : s < 30 ? 25 : 4) == Verdict.Fail && _kickWhy.StartsWith("THE TAIL GRIPPED"), "clutch kick: the tail grips after 30 m: fail at the bend's end");
        Check(Kick(s => s < -8 ? 0 : s < 40 ? 25 : 60) == Verdict.Fail && _kickWhy.StartsWith("TOO SIDEWAYS"), "clutch kick: 60 degrees (a spin): fail at once");
        Check(Kick(s => 22, kmh: 20) == Verdict.Fail && _kickWhy.StartsWith("KEEP IT GOING"), "clutch kick: crawling round: fail");
        Check(Kick(s => 22, s => s > 50 ? 4f : -2f) == Verdict.Fail && _kickWhy.StartsWith("CATCH IT"), "clutch kick: off the road in the slide: fail");
        Check(Kick(s => 22, used: false) == Verdict.Fail && _kickWhy == "USE THE CLUTCH IN THE BEND", "clutch kick: a slide without the kick: fail");

        // Handbrake turn: 180 degrees in 1.5 s with the handbrake passes; slow drift or no handbrake does not.
        var hb = new HandbrakeTurnCheck();
        v = Verdict.Pending;
        for (float t = 0; t < 2f && v == Verdict.Pending; t += 0.01f) v = hb.Feed(t, MathF.PI * Math.Min(1f, t / 1.5f) + (t > 1f ? MathF.Tau : 0), 30, t > 0.2f && t < 1f);
        Check(v == Verdict.Success, "handbrake: a spin with the handbrake (across the angle wrap): success");
        hb = new HandbrakeTurnCheck();
        v = Verdict.Pending;
        for (float t = 0; t < 10f && v == Verdict.Pending; t += 0.01f) v = hb.Feed(t, MathF.PI * t / 9f, 30, true);
        Check(v == Verdict.Pending, $"handbrake: a slow 180 over 9 s is not a handbrake turn (best {hb.BestDeg:F0} deg in 3 s)");
        hb = new HandbrakeTurnCheck();
        v = Verdict.Pending;
        for (float t = 0; t < 2f && v == Verdict.Pending; t += 0.01f) v = hb.Feed(t, MathF.PI * t, 30, false);
        Check(v == Verdict.Pending, "handbrake: the same spin without the handbrake does not count");
        hb = new HandbrakeTurnCheck();
        v = Verdict.Pending;
        for (float t = 0; t < 2f && v == Verdict.Pending; t += 0.01f) v = hb.Feed(t, MathF.PI * t, 90, true);
        Check(v == Verdict.Pending, "handbrake: not at 90 km/h (low speed only)");

        // Clutch launch: clutch in, revs, let go, 50 km/h in 2.4 s passes; no clutch or too slow fails.
        var cl = new ClutchLaunchCheck();
        v = Verdict.Pending;
        float kmh = 0;
        for (float t = 0; t < 6f && v == Verdict.Pending; t += 0.01f)
        {
            bool clutch = t < 1.5f;
            if (!clutch) kmh += 0.01f * 50f / 2.4f;
            v = cl.Feed(0.01f, kmh, clutch, clutch ? 6500 : 5000);
        }
        Check(v == Verdict.Success && Math.Abs(cl.LaunchTime - 2.4f) < 0.05f, $"clutch: 50 km/h {cl.LaunchTime:F2} s after letting go: success");
        cl = new ClutchLaunchCheck();
        v = Verdict.Pending;
        kmh = 0;
        for (float t = 0; t < 6f && v == Verdict.Pending; t += 0.01f) { kmh += 0.01f * 20f; v = cl.Feed(0.01f, kmh, false, 5000); }
        Check(v == Verdict.Fail && cl.Why == "HOLD THE CLUTCH FIRST", "clutch: driving off without it fails");
        cl = new ClutchLaunchCheck();
        v = Verdict.Pending;
        kmh = 0;
        for (float t = 0; t < 8f && v == Verdict.Pending; t += 0.01f)
        {
            bool clutch = t < 1f;
            if (!clutch) kmh += 0.01f * 10f;
            v = cl.Feed(0.01f, kmh, clutch, 6000);
        }
        Check(v == Verdict.Fail && cl.Why.StartsWith("TOO SLOW"), "clutch: 50 km/h not reached within 3 s: fail");
        cl = new ClutchLaunchCheck();
        v = Verdict.Pending;
        kmh = 0;
        for (float t = 0; t < 6f && v == Verdict.Pending; t += 0.01f) { kmh = Math.Min(14f, kmh + 0.01f * 4f); v = cl.Feed(0.01f, kmh, t > 2f, 900, 0f); }
        Check(v == Verdict.Pending, "clutch: rolling down the runway's slope (clutch or not, no throttle) is neither a launch nor a fail");
        cl = new ClutchLaunchCheck();
        Check(cl.Feed(0.01f, 0, true, 1200) == Verdict.Pending && cl.Feed(0.01f, 0, false, 1000) == Verdict.Pending && cl.Feed(0.01f, 2, false, 1500) == Verdict.Pending,
            "clutch: let go without revving is not a launch yet");

        // Texts: the pixel font draws every character, no apostrophes, short lines.
        bool drawable = true, shortLines = true;
        foreach (string s in SchoolScript.AllTexts())
        {
            foreach (char c in s) drawable &= c != '\'' && CCCPRacing.UI.PixelFont.CanDraw(c);
            shortLines &= s.Length <= 42;
        }
        Check(drawable, "school texts: all in the pixel font, no apostrophes");
        Check(shortLines, "school texts: at most 42 characters a line");
        Check(Array.TrueForAll(SchoolScript.Ilyich, x => x.step.Tell.Length <= 2)
            && Array.TrueForAll(SchoolScript.Siberia, x => x.step.Tell.Length <= 2), "school texts: at most two lines a card");
        var scenic = SchoolScript.IlyichEnvironment;
        Check(string.Join("|", SchoolScript.IlyichChapters) == "TRACK|DAMAGE|GAI RADAR"
            && scenic.Length == 5 && scenic[0].step.Id == "kolkhoz_farm" && scenic[1].step.Id == "kolkhoz_dacha"
            && scenic[2].step.Id == "kolkhoz_fields" && scenic[3].step.Id == "kolkhoz_village" && scenic[4].step.Id == "kolkhoz_daynight"
            && Array.TrueForAll(scenic, x => !x.step.Try && x.step.Keys.Length == 0),
            "preview: Ilyich environment is separate from its three instruction chapters");
        Check(SchoolScript.Ilyich[0].step.Id == "tour_first_bend" && SchoolScript.Ilyich[0].chapter == 0
            && !Array.Exists(SchoolScript.Ilyich, x => x.step.Id.StartsWith("kolkhoz_")),
            "school: Ilyich starts at track instruction with no environment steps");
        Check(SchoolScript.For(SchoolCourse.Rally, "bonus") == null
            && SchoolScript.Chapters(SchoolCourse.Rally, "bonus").Length == 0,
            "school: bonus has no course or instruction options");
        foreach (string track in new[] { "ilyich", "siberia" })
        {
            var course = SchoolScript.For(SchoolCourse.Rally, track);
            var names = SchoolScript.Chapters(SchoolCourse.Rally, track);
            bool valid = !Array.Exists(names, n => n.Contains("MISSION"))
                && !Array.Exists(course, x => x.step.Id is "norm" or "gulag" or "medal_bronze" or "medal_silver" or "medal_gold")
                && Array.TrueForAll(course, x => x.chapter >= 0 && x.chapter < names.Length);
            for (int i = 0; i < names.Length; i++) valid &= Array.Exists(course, x => x.chapter == i);
            Check(valid, $"school: {track} has no mission lessons and every remaining chapter is reachable");
        }
        var winter = SchoolScript.For(SchoolCourse.Rally, "siberia");
        Check(winter == SchoolScript.Siberia && SchoolScript.For(SchoolCourse.Driving, "siberia") == SchoolScript.Driving,
            "school: Siberia has its own rally course; the general driving course stays on Ilyich");
        var winterScenic = SchoolScript.SiberiaEnvironment;
        Check(string.Join("|", SchoolScript.SiberiaChapters) == "TRACK|WINTER ROAD|FROZEN RIVER|FUEL AND REPAIRS"
            && string.Join("|", Array.ConvertAll(winterScenic, x => x.step.Id)) == "siberia_valley|siberia_forestry|siberia_radar|siberia_base|siberia_wreck|siberia_camp|siberia_night"
            && Array.TrueForAll(winterScenic, x => !x.step.Try && x.step.Keys.Length == 0),
            "preview: Siberia environment is separate from its four instruction chapters");
        Check(Array.Find(winter, x => x.step.Id == "snow_tour_start").chapter == 0
            && Array.Find(winter, x => x.step.Id == "snow_bank").chapter == 1
            && Array.Find(winter, x => x.step.Id == "snow_ice").chapter == 2
            && Array.Find(winter, x => x.step.Id == "snow_fuel").chapter == 3,
            "school: the four winter driving chapters remain available as lessons 1-4");
        bool chapters = winter[0].step.Id == "snow_tour_start" && !Array.Exists(winter, x => x.step.Id.StartsWith("siberia_")) && winter.Length > 0 && winter[^1].step.Id != "recap";
        var ids = new System.Collections.Generic.HashSet<string>();
        foreach (var (chapter, step) in winter)
            chapters &= chapter >= 0 && chapter < SchoolScript.Chapters(SchoolCourse.Rally, "siberia").Length && ids.Add(step.Id);
        Check(chapters, "school: winter steps have unique ids, valid chapters and no closing recap (only the driving school has one)");

        // settings.cfg: [tutorial] round trip, the other sections kept.
        string dir = "user://test_school/";
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(dir));
        string path = dir + "settings.cfg";
        var cfg = new ConfigFile();
        cfg.SetValue("radio", "volume", 7);
        cfg.SetValue("input", "rumble", false);
        cfg.Save(path);
        var tf = new TutorialFile(dir);
        Check(!tf.RallySeen, "tutorial: a settings file without [tutorial]: nothing seen (YES first)");
        tf.MarkSeen(SchoolCourse.Rally);
        var back = new TutorialFile(dir);
        var raw = new ConfigFile();
        raw.Load(path);
        Check(back.RallySeen, "tutorial: rally_seen saved and loaded");
        Check((int)raw.GetValue("radio", "volume", 0) == 7 && !(bool)raw.GetValue("input", "rumble", true),
            "tutorial: the radio and rumble sections are kept");
        back.MarkKey("school_rally_ilyich");
        Check(!new TutorialFile(dir).SeenKey("school_rally_siberia"), "tutorial: seeing Ilyich does not skip Siberia instructions");
        back.MarkKey("school_rally_siberia");
        var both = new TutorialFile(dir);
        Check(both.RallySeen && both.SeenKey("school_rally_ilyich") && both.SeenKey("school_rally_siberia"), "tutorial: each track's school key saved");
        var none = new TutorialFile(dir + "missing/");
        Check(!none.RallySeen, "tutorial: no settings file: nothing seen");
        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(path));
        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(dir));
    }
}
