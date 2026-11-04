using System;
using CCCPRacing.Car;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    /// <summary>
    /// Breakable scenery (Breakables): a pole hit hard enough breaks but the car bounces off just as
    /// from an unbreakable one; a softer hit never asks; a stone wall hit hard enough lets the car
    /// through, a fixed amount slower; a softer one stops it.
    /// </summary>
    void BreakablesTest()
    {
        GD.Print("Breakable scenery");
        var gone = new Obstacle(new Vector2(-1e4f, -1e4f), 0.1f);

        // Head-on at v m/s into what make() puts 20 m ahead: speed 0.2 s after the hit, whether it
        // broke, and whether the car is still going forwards.
        float SpeedAfter(Func<CarSim> make, float v, out bool broke, out bool onward)
        {
            var car = make();
            Run(car, 0.3f);
            car.Vel = car.Forward * v;
            float hitAt = -1, t = 0;
            while (t < 3f && (hitAt < 0 || t - hitAt < 0.2f))
            {
                car.Step(Dt);
                t += Dt;
                if (hitAt < 0 && car.LastImpact > 1f) hitAt = t;
            }
            broke = _broke;
            onward = car.Vel.Dot(car.Forward) > 0.5f;
            return new Vector2(car.Vel.X, car.Vel.Z).Length();
        }

        CarSim PoleCar(bool breakable)
        {
            _broke = false;
            var car = NewCar(new FlatGround());
            var obs = new[] { new Obstacle(new Vector2(0, -20f), 0.2f, default, false, Breakables.PoleBreak) };
            car.Obstacles = obs;
            if (breakable) car.BreakObstacle = (i, impact, vel, pos) => { obs[i] = gone; return _broke = true; };
            return car;
        }

        float hard = Breakables.PoleBreak + 4f;
        float withBreak = SpeedAfter(() => PoleCar(true), hard, out bool broke, out _);
        float without = SpeedAfter(() => PoleCar(false), hard, out _, out _);
        GD.Print($"  pole at {hard * 3.6f:F0} km/h: {without * 3.6f:F1} km/h after the hit unbreakable, {withBreak * 3.6f:F1} km/h breaking it");
        Check(broke && Math.Abs(withBreak - without) < 0.01f, "a breaking pole stops the car exactly like an unbreakable one");
        SpeedAfter(() => PoleCar(true), Breakables.PoleBreak - 3f, out bool soft, out _);
        Check(!soft, "a softer hit does not break a pole");

        CarSim WallCar()
        {
            _broke = false;
            var car = NewCar(new FlatGround());
            var obs = new Obstacle[11];
            for (int k = 0; k < obs.Length; k++) obs[k] = new Obstacle(new Vector2(-4.5f + k * 0.9f, -20f), 0.45f, default, false, Breakables.WallBreak, true);
            car.Obstacles = obs;
            // As Breakables does: the hit segment and whatever else is in the car's way goes.
            car.BreakObstacle = (i, impact, vel, pos) =>
            {
                for (int k = 0; k < obs.Length; k++)
                    if (Math.Abs(obs[k].Center.X - pos.X) < 1.5f) obs[k] = gone;
                return _broke = true;
            };
            return car;
        }

        string speeds = "";
        bool through = true;
        foreach (float v in new[] { 13f, 20f, 25f })
        {
            float outV = SpeedAfter(WallCar, v, out bool b, out bool onward);
            speeds += $" {v * 3.6f:F0}->{outV * 3.6f:F0}";
            through &= b && onward && Math.Abs(outV - (v - Breakables.WallSpeedLoss)) < 1.5f;
        }
        GD.Print($"  stone wall, km/h in->out:{speeds}");
        Check(through, $"a hard hit goes through a stone wall about {Breakables.WallSpeedLoss:F0} m/s slower");
        SpeedAfter(WallCar, Breakables.WallBreak - 2f, out bool wallSoft, out bool onSoft);
        Check(!wallSoft && !onSoft, "a softer hit stops at the stone wall");
    }

    bool _broke;
}
