using System;
using CCCPRacing.Car;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    // --yaw-low-test: a car coasting at walking pace with the wheel at full lock does not swing its tail out (the yaw assist rises with speed from 1/s at 22 km/h to its full value at 50 km/h).
    void YawLowTest()
    {
        GD.Print("Yaw assist at walking pace, no drive");
        float RearSlip(float v, float gas)
        {
            var car = NewCar(new FlatGround { Surface = SurfaceType.Concrete }); Run(car, 1f); car.Gear = 2; car.Vel = car.Forward * v;
            float worst = 0f;
            for (float t = 0; t < 2f; t += Dt)
            {
                car.SetControls(gas, 0f, 1f, false, false); car.Step(Dt);
                float rl = 0; foreach (var w in car.Wheels) if (!w.Front) rl += MathF.Abs(w.SlipLat) / 2f;
                worst = MathF.Max(worst, rl);
            }
            return worst;
        }
        // With the whole assist the rear slid 0.16 / 0.84 / 3.12 m/s at 3 / 5 / 7 m/s; the coasting assist (YawAssistRest 3, rising from 22 km/h) keeps it clearly lower.
        foreach (var (v, whole) in new[] { (3f, 0.16f), (5f, 0.84f), (7f, 3.12f) })
        {
            float coast = RearSlip(v, 0f);
            GD.Print($"  {v:F0} m/s coasting at full lock: rear tyres slide sideways at most {coast:F2} m/s (with the whole assist {whole:F2})");
            Check(coast < whole * 0.75f, $"{v:F0} m/s, no gas, full lock: the tail slides out clearly less than with the whole assist ({coast:F2} against {whole:F2} m/s)");
        }
        // The Scandinavian flick (right, then at once left, the gas off) starts only above 50 km/h: none at 10 and 13 m/s, one at 20 m/s.
        foreach (var (v, wantFlick) in new[] { (10f, false), (13f, false), (20f, true) })
        {
            var car = NewCar(new FlatGround { Surface = SurfaceType.Concrete }); Run(car, 1f); car.Gear = 3; car.Vel = car.Forward * v;
            for (float t = 0; t < 1f; t += Dt) { car.SetControls(0f, 0f, t < 0.3f ? 1f : -1f, false, false); car.Step(Dt); }
            Check((car.Flicks > 0) == wantFlick, $"{v:F0} m/s ({v * 3.6f:F0} km/h): the flick {(wantFlick ? "fires" : "does not fire")} ({car.Flicks})");
        }
    }
}
