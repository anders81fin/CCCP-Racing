using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CCCPRacing.Car;
using CCCPRacing.Race;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    void TrackGearboxAnalysis()
    {
        string filter = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--gearbox-track="))?.Split('=')[1];
        foreach (string id in new[] { "ilyich", "siberia", "atomgrad", "bonus" })
        {
            if (filter != null && filter != id) continue;
            var def = TrackDef.Load($"res://tracks/{id}.json");
            var track = TrackBuilder.Build(def);
            float longest = 0, grade = 0;
            int n = track.Center.Length;
            for (int i = 0; i < n; i++)
            {
                Vector2 heading = track.CenterDir(i);
                float straight = 0;
                for (int k = 1; k < Math.Min(n, 600); k++)
                {
                    int next = (i + k) % n;
                    if (Math.Abs(heading.AngleTo(track.CenterDir(next))) > Mathf.DegToRad(12)) break;
                    straight += track.Center[next].DistanceTo(track.Center[(next + n - 1) % n]);
                }
                longest = Math.Max(longest, straight);
                int ahead = (i + 10) % n;
                grade = Math.Max(grade, Math.Abs(track.CenterH[ahead] - track.CenterH[i])
                    / Math.Max(1, track.Center[ahead].DistanceTo(track.Center[i])) * 100);
            }
            GD.Print($"GEARBOX {id}: {track.Length:F0} m, longest straight (<12 deg) {longest:F0} m, max 10 m grade {grade:F1}%");
            GD.Print("  surfaces: " + string.Join(", ", track.CenterSurf.GroupBy(s => s).Select(g => $"{g.Key} {100f * g.Count() / n:F0}%")));
            var setup = CarParams.ForTrack(def);
            Check(setup.Gears.Length == 5, $"{id}: five forward gears");
            GD.Print("  limiter speeds: " + string.Join(" / ", setup.Gears.Select(g =>
                (setup.Redline * MathF.Tau * setup.WheelRadius * 0.06f / (g * setup.FinalDrive)).ToString("F1", CultureInfo.InvariantCulture))));
            foreach (float aggression in new[] { 1f, 1.2f })
            {
                var car = new CarSim(CarParams.ForTrack(def), track) { Obstacles = track.Obstacles, Banks = track.Banks, WorldMax = track.WorldSize - 4 };
                car.Reset(track.StartPos, track.StartYaw);
                var driver = new AutoPilot(track) { Aggression = aggression };
                var timer = new LapTimer(track);
                timer.Start();
                var speeds = new List<float>();
                var gears = new float[6];
                int shifts = 0, lastGear = 1, resets = 0;
                bool tipping = false;
                float recentImpact = 0;
                for (float t = 0; t < 180; t += Dt)
                {
                    driver.Drive(car, Dt);
                    car.Step(Dt);
                    timer.Update(Dt, new Vector2(car.Pos.X, car.Pos.Z));
                    recentImpact = Math.Max(car.LastImpact, Math.Max(0, recentImpact - Dt * 10));
                    if (car.Up.Y < 0.65f && !tipping)
                    {
                        int tipAt = track.NearestCenter(new Vector2(car.Pos.X, car.Pos.Z), out float distance);
                        GD.Print($"  TIP {id} pace {aggression:F1} t={t:F1}: road {tipAt}, outside {distance - track.HalfWidth[tipAt]:F1} m, speed {car.Speed * 3.6f:F0}, air {car.AirTime:F2}, roll {RollDeg(car):F0}, nose {car.Forward.Y:F2}, impact {recentImpact:F1}");
                        tipping = true;
                    }
                    if (car.Up.Y > 0.95f) tipping = false;
                    speeds.Add(car.Speed * 3.6f);
                    if (car.Gear > 0) gears[car.Gear] += Dt;
                    if (car.Gear != lastGear) { shifts++; lastGear = car.Gear; }
                    if (car.UpsideDownTime > 1.5f)
                    {
                        int nearest = track.NearestCenter(new Vector2(car.Pos.X, car.Pos.Z), out _);
                        car.Reset(track.CenterPos(nearest) + Vector3.Up * 0.7f, TrackData.YawFor(track.CenterDir(nearest)));
                        resets++;
                    }
                }
                speeds.Sort();
                GD.Print($"  pace {aggression:F1}: laps {timer.LapsDone}, top {speeds[^1]:F1}, p25/50/75/90 {speeds[speeds.Count / 4]:F0}/{speeds[speeds.Count / 2]:F0}/{speeds[speeds.Count * 3 / 4]:F0}/{speeds[speeds.Count * 9 / 10]:F0} km/h, shifts {shifts}, resets {resets}");
                GD.Print("  gear seconds: " + string.Join(" / ", Enumerable.Range(1, 5).Select(g => $"{g}:{gears[g]:F1}")));
                Check(gears[1] > 0.5f && gears[2] > 0.5f && gears[3] > 1 && gears[4] > 1,
                    $"{id} pace {aggression:F1} uses first through fourth");
                if (id != "ilyich" && aggression > 1)
                    Check(gears[5] > 1, $"{id} fast straights use fifth");
                Check(resets == 0, $"{id} pace {aggression:F1} stays upright");
                Check(timer.LapsDone > 0, $"{id} pace {aggression:F1} completes a lap");
            }
        }
    }
}
