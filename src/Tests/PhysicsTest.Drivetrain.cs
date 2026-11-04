using System;
using CCCPRacing.Car;
using CCCPRacing.Race;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    void AirborneEngineTest()
    {
        GD.Print("Jump: free-rev the driven axle, hold the gear, lift off and land under load");
        var car = NewCar(new FlatGround());
        Run(car, 1);
        car.Gear = 3;
        car.Vel = car.Forward * (60f / 3.6f);
        Run(car, 0.2f, () => car.SetControls(true, false, 0, false));
        car.Pos += Vector3.Up * 8;
        car.Vel += Vector3.Up * 2;
        ulong shifts = car.PowerUpshifts;
        Run(car, 0.85f, () => car.SetControls(true, false, 0, false));
        GD.Print($"  airborne rpm {car.Rpm:F0}, gear {car.Gear}, rear spin {car.Wheels[2].SpinSpeed:F0} rad/s");
        Check(car.AirTime > 0.6f && car.Rpm > car.P.Redline - 400 && car.Limiter,
            "full throttle in flight reaches the limiter regardless of road speed");
        Check(car.Gear == 3 && car.PowerUpshifts == shifts, "airborne limiter does not shift the gearbox");
        float high = car.Rpm;
        Run(car, 0.3f, () => car.SetControls(false, false, 0, false));
        Check(car.AirTime > 0 && car.Rpm < high - 800, "lifting off in flight drops engine RPM");
        Run(car, 1.7f, () => car.SetControls(false, false, 0, false));
        Check(car.AirTime == 0 && car.Up.Y > 0.9f && float.IsFinite(car.Rpm), "lands and restores a stable loaded engine");
    }

    void RallyGearUsageTest()
    {
        GD.Print("Ilyich: automatic rally gearbox usage over two minutes");
        var def = TrackDef.Load("res://tracks/ilyich.json");
        var track = TrackBuilder.Build(def);
        var car = new CarSim(CarParams.ForTrack(def), track) { Obstacles = track.Obstacles, WorldMax = track.WorldSize - 4 };
        car.Reset(track.StartPos, track.StartYaw);
        var driver = new AutoPilot(track);
        var timer = new LapTimer(track);
        timer.Start();
        float[] seconds = new float[car.P.Gears.Length + 1];
        float top = 0;
        for (float t = 0; t < 120; t += Dt)
        {
            driver.Drive(car, Dt);
            car.Step(Dt);
            timer.Update(Dt, new Vector2(car.Pos.X, car.Pos.Z));
            if (car.Gear > 0) seconds[car.Gear] += Dt;
            top = Math.Max(top, car.Speed * 3.6f);
        }
        for (int i = 1; i < seconds.Length; i++) GD.Print($"  gear {i}: {seconds[i]:F1} s");
        GD.Print($"  peak {top:F0} km/h, completed laps {timer.LapsDone}");
        Check(seconds[1] > 1 && seconds[2] > 1 && seconds[3] > 1 && seconds[4] > 1,
            "Ilyich driving uses first through fourth gear");
        Check(timer.LapsDone > 0, "the rally gearing still completes an Ilyich lap");
    }
}
