using System;
using CCCPRacing.Car;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    // --scrub-rest-test: a car sliding broadside at walking pace with no drive slows down about as hard as its tyres grip (the arcade forgiveness of the scrub is off there), and the forgiveness is whole above 50 km/h.
    void ScrubRestTest()
    {
        GD.Print("Sliding sideways on concrete, gas and wheel off");
        (float decel, float lat) Run05(float v, float gas)
        {
            var car = NewCar(new FlatGround { Surface = SurfaceType.Concrete }); Run(car, 1f); car.Gear = 2;
            car.Vel = car.Forward.Rotated(Vector3.Up, Mathf.DegToRad(90f)) * v;
            for (float t = 0; t < 0.5f; t += Dt) { car.SetControls(gas, 0f, 0f, false, false); car.Step(Dt); }
            return ((v - car.Speed) / 0.5f, MathF.Abs(car.Vel.Dot(car.Right)));
        }
        var (d4, l4) = Run05(4f, 0f); var (d8, _) = Run05(8f, 0f); var (d20, _) = Run05(20f, 0f);
        GD.Print($"  mean deceleration over 0.5 s: 4 m/s {d4:F1}, 8 m/s {d8:F1}, 20 m/s {d20:F1} m/s2; sideways speed left at 4 m/s {l4:F2})");
        Check(d4 > 4f, $"4 m/s (14 km/h) broadside with no drive: the car brakes hard, {d4:F1} m/s² (the tyres' full grip about 9; it was 2.5)");
        Check(d8 > d20 && d20 < 4f, $"the forgiveness rises with speed: {d8:F1} m/s² at 8 m/s, {d20:F1} at 20 m/s (the arcade drift, was about 2.5)");
    }
}
