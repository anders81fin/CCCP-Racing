using System;
using CCCPRacing.Car;
using CCCPRacing.Race;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    void SiberiaTerrainTest()
    {
        var def = TrackDef.Load("res://tracks/siberia.json");
        var track = TrackBuilder.Build(def);
        var oldDef = TrackDef.Load("res://tracks/siberia.json");
        oldDef.Water.ValleyDepth = 0;
        oldDef.Features.Clear();
        var original = TrackBuilder.Build(oldDef);
        float runway = 0, grade = 0, mismatch = 0;
        int steepest = 0;
        for (int i = 0; i < track.Center.Length; i++)
        {
            float slope = Math.Abs(track.CenterH[(i + 1) % track.Center.Length] - track.CenterH[i]);
            if (slope > grade) { grade = slope; steepest = i; }
            if (i >= 630 && i <= 790) runway = Math.Max(runway, Math.Abs(track.CenterH[i] - original.CenterH[i]));
            var p = track.Center[i];
            mismatch = Math.Max(mismatch, Math.Abs(track.CenterH[i] - track.MeshHeight(p.X, p.Y)));
        }
        Check(runway < .01f, $"runway retains original grade: change {runway:F3} m");
        GD.Print($"  steepest point {track.Center[steepest]}, original grade {Math.Abs(original.CenterH[steepest + 1] - original.CenterH[steepest]) * 100:F1}%");
        Check(grade < .22f, $"road has gentle transitions: max {grade * 100:F1}% at {steepest} m");
        Check(mismatch < .3f, $"road profile matches collision terrain: max error {mismatch:F3} m");
        foreach (var p in new[] { new Vector2(90, 142), new Vector2(130, 162), new Vector2(165, 186) })
        {
            float ice = track.MeshHeight(p.X, p.Y);
            var normal = new Vector2(-.5f, .866f);
            foreach (int side in new[] { -1, 1 })
            {
                var bank = p + normal * (side * 38f);
                float rise = track.MeshHeight(bank.X, bank.Y) - ice;
                Check(rise > 1.4f, $"river below surrounding bank at {p}, side {side}: {rise:F2} m");
            }
        }
        float previous = float.PositiveInfinity, upstream = 0;
        var river = def.Water.Rivers[0];
        for (int j = 0; j + 1 < river.Points.Length; j++)
        {
            var a = new Vector2(river.Points[j][0], river.Points[j][1]);
            var b = new Vector2(river.Points[j + 1][0], river.Points[j + 1][1]);
            for (int k = 0; k < 20; k++)
            {
                var p = a.Lerp(b, k / 20f);
                if (p.X < 35 || p.Y > 285) continue; // separate map-edge blending
                float h = track.MeshHeight(p.X, p.Y);
                upstream = Math.Max(upstream, h - previous);
                previous = h;
            }
        }
        Check(upstream < .15f, $"ice flows downstream without ledges: max uphill step {upstream:F3} m");
        foreach (float aggression in new[] { 1f, 1.15f })
        {
            var car = new CarSim(CarParams.ForTrack(def), track)
            { Obstacles = track.Obstacles, Banks = track.Banks, WorldMax = track.WorldSize - 4 };
            car.Reset(track.StartPos, track.StartYaw);
            var driver = new AutoPilot(track) { Aggression = aggression };
            var timer = new LapTimer(track); timer.Start();
            float minUp = 1, maxAir = 0, elapsed = 0;
            for (; elapsed < 180 && timer.LapsDone == 0; elapsed += Dt)
            {
                driver.Drive(car, Dt); car.Step(Dt);
                timer.Update(Dt, new Vector2(car.Pos.X, car.Pos.Z));
                minUp = Math.Min(minUp, car.Up.Y); maxAir = Math.Max(maxAir, car.AirTime);
            }
            Check(timer.LapsDone > 0 && minUp > .7f,
                $"Siberia lap pace {aggression:F2}: {elapsed:F1}s, upright {minUp:F2}, max air {maxAir:F2}s");
        }
    }
}
