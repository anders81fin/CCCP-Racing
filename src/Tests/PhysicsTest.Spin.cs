using System;
using CCCPRacing.Car;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    /// <summary>
    /// A car spun round off the road and rolling backwards in a forward gear: the brake (key or pad
    /// pedal) brakes it as hard as it brakes a car rolling forwards. It used to select reverse at
    /// once and drive the car on backwards at full throttle.
    /// </summary>
    void SpinTest()
    {
        GD.Print("Spun round off the road");
        float fwd = Roll(SurfaceType.Grass, 1f, car => car.SetControls(false, true, 0f, false)).dist;
        var (back, gear) = Roll(SurfaceType.Grass, -1f, car => car.SetControls(false, true, 0f, false));
        var (pad, padGear) = Roll(SurfaceType.Grass, -1f, car => car.SetControls(0f, 1f, 0f, false, true));
        float coast = Roll(SurfaceType.Grass, -1f, car => car.SetControls(false, false, 0f, false)).dist;
        GD.Print($"  90 km/h on grass to walking pace: braking forwards {fwd:F0} m, backwards {back:F0} m (pad {pad:F0} m), rolling backwards {coast:F0} m");
        Check(gear > 0 && padGear > 0 && back < fwd * 1.15f && pad < fwd * 1.15f,
            "rolling backwards, the brake brakes (no reverse gear at speed), as hard as forwards");
        float concrete = Roll(SurfaceType.Concrete, 1f, car => car.SetControls(false, false, 0f, false)).dist;
        float field = Roll(SurfaceType.Field, 1f, car => car.SetControls(false, false, 0f, false)).dist;
        GD.Print($"  coasting from 90 km/h: concrete {concrete:F0} m (or more), grass {coast:F0} m, field {field:F0} m");
        Check(field < 220f, "the field (rolling 0.12) stops a coasting car well short of the road (was 295 m)");
        float grassFwd = Roll(SurfaceType.Grass, 1f, car => car.SetControls(false, false, 0f, false)).dist;
        Check(grassFwd < 280f && coast < grassFwd * 0.8f, $"grass stops a coasting car short of the road ({grassFwd:F0} m, was 358 m), tail first sooner: the tyres dig in ({coast:F0} m)");

        // Tail first on the gas or coasting, the car swaps ends (the front wheels flop round), losing speed sideways.
        foreach (var (s, name) in new[] { (SurfaceType.Concrete, "concrete"), (SurfaceType.Grass, "grass") })
            foreach (bool gas in new[] { true, false })
            {
                var (noseAt, low) = Swap(s, car => car.SetControls(gas, false, 0f, false));
                GD.Print($"  tail first at 90 km/h on {name}, {(gas ? "on the gas" : "coasting")}: nose first after {noseAt:F1} s, down to {low * 3.6f:F0} km/h");
                Check(noseAt > 0.6f && noseAt < 2.5f && low < 20f, $"...swaps ends in 0.6-2.5 s and loses at least 18 km/h on {name}");
            }
        var (braked, _) = Swap(SurfaceType.Grass, car => car.SetControls(false, true, 0f, false));
        Check(braked < 0, "on the brakes (front wheels locked) it slides on tail first");
    }

    /// <summary>Tail first at 25 m/s, 5° off straight, 3rd gear: when it is going nose first (-1: not in 4 s) and its lowest speed till then.</summary>
    (float noseAt, float low) Swap(SurfaceType s, Action<CarSim> input)
    {
        var car = NewCar(new FlatGround { Surface = s });
        Run(car, 1f);
        car.Gear = 3;
        car.Vel = car.Forward.Rotated(Vector3.Up, 0.08f) * -25f;
        float low = 25f;
        for (float t = 0; t < 4f && car.Speed > 1.5f; t += Dt)
        {
            input(car);
            car.Step(Dt);
            low = Math.Min(low, car.Speed);
            if (car.ForwardSpeed > 0) return (t, low);
        }
        return (-1f, low);
    }

    /// <summary>The car at 25 m/s along (dir 1) or against (dir -1) its nose in 3rd gear: the distance to walking pace (at most 400 m) and the gear then.</summary>
    (float dist, int gear) Roll(SurfaceType s, float dir, Action<CarSim> input)
    {
        var car = NewCar(new FlatGround { Surface = s });
        Run(car, 1f);
        car.Gear = 3;
        car.Vel = car.Forward * 25f * dir;
        Vector3 from = car.Pos;
        // Below 1 m/s the brake key selects reverse, as it should at a stop: measure to 1.5 m/s.
        for (float t = 0; t < 30f && car.Speed > 1.5f && car.Pos.DistanceTo(from) < 400f; t += Dt)
        {
            input(car);
            car.Step(Dt);
        }
        return (new Vector2(car.Pos.X - from.X, car.Pos.Z - from.Z).Length(), car.Gear);
    }
}
