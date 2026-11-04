using System;
using CCCPRacing.Car;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    void PowerCornerTest()
    {
        foreach (var surface in new[] { SurfaceType.Concrete, SurfaceType.Gravel, SurfaceType.Dirt, SurfaceType.Snow, SurfaceType.Ice })
        foreach (float speed in new[] { 40f, 60f, 90f })
        foreach (float pedal in new[] { 0f, 0.4f, 1f })
        foreach (bool holdSpeed in new[] { false, true })
        {
            (float body, float path, float speed, float front, float pitch) Turn(int version)
            {
                var setup = version == 0 ? new CarParams
                {
                    MaxTorque = 230, DriveGrip = 1.45f, ThrottleRise = 4,
                    ThrottleProgression = 0, TorqueCurveFalloff = 0.55f, PowerFrontGripRecovery = 0,
                } : new CarParams { PowerFrontGripRecovery = version == 1 ? 0 : 0.65f };
                var car = NewCar(new FlatGround { Surface = surface }, setup);
                Run(car, 1);
                car.Gear = speed < 50 ? 2 : speed < 80 ? 3 : 4;
                car.Vel = car.Forward * (speed / 3.6f);
                car.Rpm = speed / 3.6f / setup.WheelRadius * setup.Gears[car.Gear - 1] * setup.FinalDrive * 60 / MathF.Tau;
                foreach (var w in car.Wheels) w.SpinSpeed = car.ForwardSpeed / setup.WheelRadius;
                var heading = car.Forward;
                float front = 0, pitch = 0, count = 0;
                for (float t = 0; t < 1; t += Dt)
                {
                    car.SetControls(pedal, 0f, 0.4f, false, false);
                    car.Step(Dt);
                    // Diagnostic governor only: preserve heading and vertical motion while
                    // removing speed gain/loss to isolate load/force effects at equal speed.
                    if (holdSpeed)
                    {
                        Vector3 horizontal = new(car.Vel.X, 0, car.Vel.Z);
                        horizontal = horizontal.Normalized() * (speed / 3.6f);
                        car.Vel = new(horizontal.X, car.Vel.Y, horizontal.Z);
                    }
                    front += car.Wheels[0].Load + car.Wheels[1].Load;
                    pitch += Mathf.RadToDeg(MathF.Asin(Math.Clamp(car.Forward.Y, -1, 1)));
                    count++;
                }
                Vector3 path = new(car.Vel.X, 0, car.Vel.Z);
                Check(float.IsFinite(car.Speed) && car.Up.Y > 0.8f, $"{surface} {speed} pedal {pedal} version {version} held {holdSpeed}: stable");
                return (-Mathf.RadToDeg(heading.SignedAngleTo(car.Forward, Vector3.Up)),
                    -Mathf.RadToDeg(heading.SignedAngleTo(path.Normalized(), Vector3.Up)),
                    car.Speed * 3.6f, front / count, pitch / count);
            }
            var before = Turn(0);
            var attack = Turn(1);
            var after = Turn(2);
            GD.Print($"CORNER {surface} {speed:F0} pedal {pedal:F1} held {holdSpeed}: old/attack/fixed body {before.body:F1}/{attack.body:F1}/{after.body:F1}, path {before.path:F1}/{attack.path:F1}/{after.path:F1} deg, speed {before.speed:F1}/{attack.speed:F1}/{after.speed:F1}, front {before.front:F0}/{attack.front:F0}/{after.front:F0} N, pitch {before.pitch:F1}/{attack.pitch:F1}/{after.pitch:F1}");
            if (pedal == 0)
            {
                Check(Math.Abs(before.path - after.path) < 0.1f, $"{surface} {speed}: coasting effectively unchanged by engine tuning");
                Check(Math.Abs(attack.path - after.path) < 0.001f, $"{surface} {speed}: recovery leaves coasting unchanged");
            }
            if (pedal == 1)
            {
                Check(after.path > attack.path, $"{surface} {speed}: front recovery improves powered path turning");
                if (holdSpeed)
                    Check(after.path >= before.path * 0.95f && after.path < before.path * 1.25f,
                        $"{surface} {speed}: equal-speed turning recovers without excessive gain over the old setup");
            }
        }
    }
}
