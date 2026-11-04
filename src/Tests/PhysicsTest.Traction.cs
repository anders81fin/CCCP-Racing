using System;
using CCCPRacing.Car;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    void TractionBiteTest()
    {
        GD.Print("Traction: surface bite, wheel overspeed and recovery under load");
        float runway = 0, gravel = 0, mud = 0;
        foreach (var surface in new[] { SurfaceType.Runway, SurfaceType.Concrete, SurfaceType.Gravel, SurfaceType.Dirt, SurfaceType.Mud, SurfaceType.Snow, SurfaceType.Ice })
        {
            var car = NewCar(new FlatGround { Surface = surface });
            Run(car, 1);
            float spin = 0, t100 = -1, t = 0;
            Run(car, 10, () =>
            {
                car.SetControls(true, false, 0, false);
                t += Dt;
                if (car.Wheels[2].Spinning || car.Wheels[3].Spinning) spin += Dt;
                if (t100 < 0 && car.Speed * 3.6f >= 100) t100 = t;
            });
            GD.Print($"  {surface}: 10 s {car.Speed * 3.6f:F1} km/h, 0-100 {t100:F2}, spin {spin:F2} s, gear {car.Gear}, rpm {car.Rpm:F0}");
            Check(float.IsFinite(car.Rpm + car.Vel.Length()) && car.Up.Y > 0.9f, $"{surface}: finite and upright");
            if (surface == SurfaceType.Runway) runway = car.Speed;
            if (surface == SurfaceType.Gravel) gravel = car.Speed;
            if (surface == SurfaceType.Mud) mud = car.Speed;
        }
        Check(runway > gravel && gravel > mud, "hard road bites harder than gravel, mud is distinctly slower");
        float ExitAcceleration(float angle)
        {
            var exit = NewCar(new FlatGround { Surface = SurfaceType.Gravel });
            Run(exit, 1);
            exit.Gear = 3;
            exit.Rpm = 4800;
            exit.Throttle = 1;
            exit.Vel = exit.Forward.Rotated(Vector3.Up, Mathf.DegToRad(angle)) * (50 / 3.6f);
            foreach (var wheel in exit.Wheels) wheel.SpinSpeed = exit.ForwardSpeed / exit.P.WheelRadius;
            float initial = exit.Speed;
            Run(exit, 0.3f, () => exit.SetControls(true, false, 0, false));
            return (exit.Speed - initial) / 0.3f;
        }
        float straight = ExitAcceleration(0), sideways = ExitAcceleration(25);
        GD.Print($"  gravel exit: straight {straight:F2} m/s2, sliding {sideways:F2} m/s2");
        Check(straight > sideways + 0.5f, "straightening the car frees a clear increase in acceleration");
        var ground = new FlatGround { Surface = SurfaceType.Gravel };
        var recovery = NewCar(ground);
        Run(recovery, 1);
        recovery.PowerScale = 2;
        recovery.Gear = 1;
        GD.Print($"  recovery setup: power {recovery.PowerScale:F1}");
        recovery.Vel = recovery.Forward * (20 / 3.6f);
        Run(recovery, 0.5f, () => recovery.SetControls(true, false, 0, false));
        float looseRpm = recovery.Rpm, looseSlip = recovery.Wheels[2].SlipLong;
        Run(recovery, 0.7f, () => recovery.SetControls(0.2f, 0f, 0f, false, false));
        GD.Print($"  throttle recovery: slip {looseSlip:F2} -> {recovery.Wheels[2].SlipLong:F2}, rpm {looseRpm:F0} -> {recovery.Rpm:F0}");
        Check(looseSlip > 0.1f && recovery.Wheels[2].SlipLong < 0.08f, "metering throttle restores rolling traction");
        Check(recovery.Rpm < looseRpm, "regained tyre load brings the engine revs down");
        Check(Math.Abs(recovery.Wheels[2].SpinSpeed * recovery.P.WheelRadius - recovery.ForwardSpeed) < 1,
            "recovered wheel rotation follows road speed");
    }
}
