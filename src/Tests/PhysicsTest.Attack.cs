using System;
using CCCPRacing.Car;
using CCCPRacing.Camera;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    void MaximumAttackTest()
    {
        CarParams Setup(bool old) => old ? new CarParams
        {
            MaxTorque = 230, DriveGrip = 1.45f, ThrottleRise = 4,
            ThrottleProgression = 0, TorqueCurveFalloff = 0.55f,
        } : new CarParams();
        (float acceleration, float pitch, float throttle) Exit(bool old, int gear, float speed, float pedal, SurfaceType surface)
        {
            var car = NewCar(new FlatGround { Surface = surface }, Setup(old));
            Run(car, 1);
            car.Gear = gear;
            car.Vel = car.Forward * (speed / 3.6f);
            foreach (var wheel in car.Wheels) wheel.SpinSpeed = car.ForwardSpeed / car.P.WheelRadius;
            car.Rpm = speed / 3.6f / car.P.WheelRadius * car.P.Gears[gear - 1] * car.P.FinalDrive * 60 / MathF.Tau;
            float before = car.Speed, pitch = 0;
            Run(car, 0.35f, () =>
            {
                car.SetControls(pedal, 0f, 0f, false, false);
                pitch = Math.Max(pitch, Mathf.RadToDeg(MathF.Asin(Math.Clamp(car.Forward.Y, -1, 1))));
            });
            return ((car.Speed - before) / 0.35f, pitch, car.Throttle);
        }
        foreach (var surface in new[] { SurfaceType.Gravel, SurfaceType.Concrete })
        foreach (int gear in new[] { 2, 3 })
        {
            float speed = gear == 2 ? 35 : 60;
            var before = Exit(true, gear, speed, 1, surface);
            var after = Exit(false, gear, speed, 1, surface);
            GD.Print($"  ATTACK {surface} gear {gear} at {speed:F0}: {before.acceleration:F2} -> {after.acceleration:F2} m/s2, pitch {before.pitch:F2} -> {after.pitch:F2} deg");
            Check(after.acceleration > before.acceleration * 1.25f, $"{surface} gear {gear}: at least 25% stronger full-throttle pickup");
            Check(after.pitch > before.pitch && after.pitch < 12, $"{surface} gear {gear}: real suspension load pitches the nose up, within bounds");
        }
        var partial = Exit(false, 3, 60, 0.4f, SurfaceType.Gravel);
        var full = Exit(false, 3, 60, 1, SurfaceType.Gravel);
        Check(partial.acceleration > 0 && partial.acceleration < full.acceleration * 0.65f,
            "partial throttle remains measured below the attack at full pedal");
        var response = NewCar(new FlatGround());
        Run(response, 1);
        Run(response, 0.075f, () => response.SetControls(true, false, 0, false));
        Check(response.Throttle > 0.95f, "full-throttle response in 75 ms");
        Run(response, Dt, () => response.SetControls(false, false, 0, false));
        Check(response.Throttle == 0, "lifting off releases the throttle immediately");

        var camera = new ChaseCamera();
        for (int i = 0; i < 120; i++) camera.Acceleration(new Vector3(0, 0, -20), 0, 1f / 60);
        Check(camera.DriveSetback.Length() < 0.001f, "steady speed alone does not trigger an acceleration camera kick");
        for (int i = 0; i < 120; i++) camera.Acceleration(new Vector3(0, 0, -20), 12, 1f / 60);
        Check(camera.DriveSetback.Z > 0.8f && camera.DriveSetback.Length() <= 1.35f,
            "camera gives bounded rearward lag under actual acceleration");
        camera.ShakeEnabled = false;
        for (int i = 0; i < 120; i++) camera.Acceleration(new Vector3(0, 0, -20), 12, 1f / 60);
        Check(camera.DriveSetback.Length() < 0.001f, "disabled camera shake also disables acceleration lag");
        camera.Free();
    }
}
