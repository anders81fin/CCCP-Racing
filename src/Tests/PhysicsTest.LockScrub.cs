using System;
using CCCPRacing.Car;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    // --lock-scrub-test: the turned front tyres scrub the speed off a coasting car, steplessly with the steering angle (none straight, all of it at full lock), and not on the gas.
    void LockScrubTest()
    {
        GD.Print("Steering lock scrub on concrete");
        float Stop(float v, float steer, float gas)
        {
            var c = NewCar(new FlatGround { Surface = SurfaceType.Concrete }); Run(c, 1f); c.Gear = 2; c.Vel = c.Forward * v;
            float t = 0f; for (; t < 20f && c.Speed > 0.1f; t += Dt) { c.SetControls(gas, 0f, steer, false, false); c.Step(Dt); }
            return t;
        }
        float s0 = Stop(4f, 0f, 0f), s25 = Stop(4f, 0.25f, 0f), s50 = Stop(4f, 0.5f, 0f), s100 = Stop(4f, 1f, 0f);
        GD.Print($"  coasting from 4 m/s to a stop at steering 0 / 25 / 50 / 100 %: {s0:F1} / {s25:F1} / {s50:F1} / {s100:F1} s");
        Check(s0 > s25 && s25 > s50 && s50 > s100 && s0 > s100 * 4f, "the lock scrub grows steplessly with the steering angle (0, 25, 50, 100 %)");
        Check(Stop(3f, 1f, 0f) < 2.5f && Stop(6f, 1f, 0f) < 4.5f, "from 3 and 6 m/s the held full lock stops the coasting car within 2.5 and 4.5 s");
        var gasCar = NewCar(new FlatGround { Surface = SurfaceType.Concrete }); Run(gasCar, 1f); gasCar.Gear = 2; gasCar.Vel = gasCar.Forward * 4f;
        for (float t = 0; t < 3f; t += Dt) { gasCar.SetControls(true, false, 1f, false); gasCar.Step(Dt); }
        Check(gasCar.Speed > 2.5f, $"on the gas the held lock does not stop the car ({gasCar.Speed:F1} m/s after 3 s)");
    }
}
