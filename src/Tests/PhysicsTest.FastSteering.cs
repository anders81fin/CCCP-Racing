using System;
using CCCPRacing.Car;
using CCCPRacing.Race;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    void FastSteeringTest()
    {
        (float path, float roll, float up, float speed, bool finite) Drive(
            SurfaceType surface, float speed, int power, float steer, bool enabled, int scenario, bool wet)
        {
            float savedWetness = Surfaces.Wetness;
            try
            {
                Surfaces.Wetness = wet ? 1 : 0;
                var ground = new FlatGround { Surface = surface };
                var car = NewCar(ground, new CarParams { FastSteerAssist = enabled ? 0.25f : 0 });
                Run(car, 1);
                car.PowerScale = power / 100f;
                car.Overdrive = Training.Overdrive(power);
                car.Gear = 4;
                car.Vel = car.Forward * (speed / 3.6f);
                car.Rpm = speed / 3.6f / car.P.WheelRadius * car.P.Gears[3] * car.P.FinalDrive * 60 / MathF.Tau;
                foreach (var wheel in car.Wheels) wheel.SpinSpeed = car.ForwardSpeed / car.P.WheelRadius;
                Vector3 heading = car.Forward;
                float roll = 0;
                bool finite = true, launched = false;
                float duration = scenario == 0 ? 0.6f : 2.5f;
                for (float t = 0; t < duration; t += Dt)
                {
                    float input = scenario == 0 || t < 0.6f ? steer : t < 1.2f ? -steer : 0;
                    car.SetControls(scenario == 0 ? 0f : 1f, 0f, input, false, true);
                    if (scenario == 2 && t >= 0.3f && !launched)
                    {
                        car.Pos += Vector3.Up * 0.7f;
                        car.Vel += Vector3.Up * 1.5f;
                        launched = true;
                    }
                    if (scenario == 3 && t >= 0.4f)
                        ground.Surface = surface switch
                        {
                            SurfaceType.Concrete => SurfaceType.Gravel,
                            SurfaceType.Gravel => SurfaceType.Concrete,
                            SurfaceType.Snow => SurfaceType.Ice,
                            SurfaceType.Ice => SurfaceType.Snow,
                            _ => SurfaceType.Mud,
                        };
                    car.Step(Dt);
                    finite &= float.IsFinite(car.Speed + car.Pos.Y + car.AngVel.Length());
                    roll = Math.Max(roll, Math.Abs(RollDeg(car)));
                }
                Vector3 velocity = new(car.Vel.X, 0, car.Vel.Z);
                float path = -Math.Sign(steer) * Mathf.RadToDeg(heading.SignedAngleTo(velocity.Normalized(), Vector3.Up));
                return (path, roll, car.Up.Y, car.Speed, finite);
            }
            finally { Surfaces.Wetness = savedWetness; }
        }
        foreach (var surface in new[] { SurfaceType.Concrete, SurfaceType.Gravel, SurfaceType.Dirt, SurfaceType.Snow, SurfaceType.Ice })
        foreach (float speed in new[] { 40f, 80f, 120f, 160f })
        foreach (float steer in new[] { -1f, -0.2f, 0.2f, 1f })
        {
            var before = Drive(surface, speed, 100, steer, false, 0, false);
            var after = Drive(surface, speed, 100, steer, true, 0, false);
            GD.Print($"FASTSTEER {surface} {speed} input {steer}: path {before.path:F2}->{after.path:F2}, roll {before.roll:F1}->{after.roll:F1}");
            Check(after.finite && after.up > 0.8f, $"{surface} {speed} input {steer}: stable full/partial pad steering");
            if (speed <= 50 || Math.Abs(steer) < 0.45f)
                Check(Math.Abs(after.path - before.path) < 0.001f, "small corrections and low speed remain unchanged");
            else
                Check(after.path > before.path, "large steering input improves actual path turning at speed");
        }
        foreach (var surface in new[] { SurfaceType.Concrete, SurfaceType.Gravel, SurfaceType.Dirt, SurfaceType.Snow, SurfaceType.Ice })
        foreach (int power in new[] { 100, 200, 350, 500 })
        foreach (float speed in new[] { 80f, 120f, 160f })
        foreach (int scenario in new[] { 1, 2, 3 })
        foreach (bool wet in new[] { false, true })
        {
            var before = Drive(surface, speed, power, 1, false, scenario, wet);
            var after = Drive(surface, speed, power, 1, true, scenario, wet);
            string label = $"{surface} {power}% {speed} km/h scenario {scenario} wet {wet}";
            GD.Print($"STRESS {label}: roll {before.roll:F1}->{after.roll:F1}, up {before.up:F2}->{after.up:F2}");
            Check(after.finite, label + ": finite");
            Check(after.up > 0.7f || before.up <= 0.7f, label + ": no new rollover");
            Check(after.roll < 75f || before.roll >= 75f, label + ": no new severe transient roll");
        }
    }
}
