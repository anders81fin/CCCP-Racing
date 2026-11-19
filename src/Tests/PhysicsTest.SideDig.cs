using System;
using CCCPRacing.Car;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    // --side-dig-test: off the road a car sliding broadside (no gas) brakes about as hard as on it (the tyres dig in), nothing changes on the road, and rolling straight on grass is not slowed by it.
    void SideDigTest()
    {
        GD.Print("Sliding sideways on soft ground");
        float Broadside(SurfaceType s, float v)
        {
            var car = NewCar(new FlatGround { Surface = s }); Run(car, 1f); car.Gear = 2;
            car.Vel = car.Forward.Rotated(Vector3.Up, Mathf.DegToRad(90f)) * v;
            for (float t = 0; t < 0.5f; t += Dt) { car.SetControls(0f, 0f, 0f, false, false); car.Step(Dt); }
            return (v - car.Speed) / 0.5f;
        }
        float Straight(SurfaceType s)
        {
            var car = NewCar(new FlatGround { Surface = s }); Run(car, 1f); car.Gear = 2; car.Vel = car.Forward * 10f;
            float t = 0f; for (; t < 30f && car.Speed > 5f; t += Dt) { car.SetControls(0f, 0f, 0f, false, false); car.Step(Dt); }
            return t;
        }
        float road = Broadside(SurfaceType.Concrete, 8f), grass = Broadside(SurfaceType.Grass, 8f), field = Broadside(SurfaceType.Field, 8f), mud = Broadside(SurfaceType.Mud, 8f);
        GD.Print($"  broadside at 8 m/s, deceleration over 0.5 s: concrete {road:F1}, grass {grass:F1}, field {field:F1}, mud {mud:F1} m/s2; straight coasting 10 to 5 m/s on grass {Straight(SurfaceType.Grass):F1} s");
        Check(grass > 6.5f && field > 6.5f && mud > 5.5f, $"broadside on grass, field and mud the car brakes hard ({grass:F1}, {field:F1}, {mud:F1} m/s²; was 4.4, 4.1)");
        Check(MathF.Abs(road - 6.6f) < 0.6f, $"on concrete nothing changes ({road:F1} m/s², was 6.6)");
        Check(Straight(SurfaceType.Grass) > 3.4f && Straight(SurfaceType.Grass) < 4.4f, "rolling straight on grass the dig does not act (10 to 5 m/s in about 3.9 s)");
    }
}
