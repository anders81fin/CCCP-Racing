using System;
using CCCPRacing.Car;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    void BrakingBalanceTest()
    {
        CarSim Rolling(SurfaceType surface, float speed, bool oldSteering = false)
        {
            var car = NewCar(new FlatGround { Surface = surface },
                oldSteering ? new CarParams { LockedBrakeSteering = 0 } : new CarParams());
            Run(car, 1);
            car.Gear = 3;
            car.Vel = car.Forward * (speed / 3.6f);
            foreach (var wheel in car.Wheels) wheel.SpinSpeed = car.ForwardSpeed / car.P.WheelRadius;
            return car;
        }
        (float distance, float time, float locked) Stop(SurfaceType surface, bool oldSteering = false)
        {
            var car = Rolling(surface, 100, oldSteering);
            float distance = 0, time = 0, locked = 0;
            while (car.Speed > 0.5f && time < 30)
            {
                car.SetControls(0f, 1f, 0f, false, false);
                float before = car.Speed;
                car.Step(Dt);
                distance += (before + car.Speed) * 0.5f * Dt;
                time += Dt;
                if (car.Wheels[0].Locked || car.Wheels[1].Locked) locked += Dt;
                CheckFinite(car);
            }
            Check(time < 30 && car.Up.Y > 0.8f, $"{surface}: stops upright");
            return (distance, time, locked / time);
        }
        void CheckFinite(CarSim car)
        {
            if (!float.IsFinite(car.Speed)) throw new Exception("Non-finite braking simulation");
        }
        float concreteStop = 0, gravelStop = 0, snowStop = 0, iceStop = 0;
        foreach (var surface in Enum.GetValues<SurfaceType>())
        {
            var info = Surfaces.Get(surface);
            var current = Stop(surface);
            var withoutAssist = Stop(surface, true);
            Check(Math.Abs(current.distance - withoutAssist.distance) < 0.05f,
                $"{surface}: steering assist does not change straight-line stopping distance");
            if (surface == SurfaceType.Concrete) concreteStop = current.distance;
            if (surface == SurfaceType.Gravel) gravelStop = current.distance;
            if (surface == SurfaceType.Snow) snowStop = current.distance;
            if (surface == SurfaceType.Ice) iceStop = current.distance;
            Surfaces.Info[(int)surface] = info with { BrakeBite = 1 };
            (float distance, float time, float locked) previous;
            try { previous = Stop(surface); }
            finally { Surfaces.Info[(int)surface] = info; }
            GD.Print($"BRAKE {surface}: 100-1.8 km/h {previous.distance:F1} -> {current.distance:F1} m, {current.time:F2} s, front lock {current.locked:P0}");
            if (info.BrakeBite > 1) Check(current.distance < previous.distance, $"{surface}: raised bite shortens stopping distance");
            float coastTurn = 0;
            foreach (float pedal in new[] { 0f, 0.5f, 1f })
            {
                float Turn(bool oldSteering)
                {
                    var car = Rolling(surface, 60, oldSteering);
                    var startForward = car.Forward;
                    float locked = 0;
                    Run(car, 0.7f, () =>
                    {
                        car.SetControls(0f, pedal, 0.4f, false, false);
                        if (car.Wheels[0].Locked || car.Wheels[1].Locked) locked += Dt;
                    });
                    float yaw = Mathf.RadToDeg(startForward.SignedAngleTo(car.Forward, Vector3.Up));
                    GD.Print($"STEER {surface}: brake {pedal:F1}, yaw {yaw:F1} deg, front lock {locked / 0.7f:P0}, end {car.Speed * 3.6f:F1} km/h");
                    Check(float.IsFinite(yaw) && car.Up.Y > 0.8f, $"{surface}: brake/steer stable at pedal {pedal}");
                    return -yaw;
                }
                float previousTurn = Turn(true), currentTurn = Turn(false);
                GD.Print($"TURN {surface}: brake {pedal:F1}, into steer {previousTurn:F1} -> {currentTurn:F1} deg");
                if (pedal == 0) coastTurn = currentTurn;
                if (pedal == 1)
                {
                    Check(currentTurn > previousTurn + 1 && currentTurn > 0,
                        $"{surface}: locked braking retains limited steering in the requested direction");
                    Check(currentTurn < coastTurn * 0.85f,
                        $"{surface}: full braking still understeers relative to coasting");
                }
            }
        }
        Check(concreteStop < gravelStop && gravelStop < snowStop && snowStop < iceStop,
            "braking preserves concrete/gravel/snow/ice surface differences");
        float wetness = Surfaces.Wetness;
        try
        {
            Surfaces.Wetness = 1;
            var wet = Stop(SurfaceType.Concrete);
            GD.Print($"BRAKE wet concrete: {wet.distance:F1} m vs dry {concreteStop:F1} m");
            Check(wet.distance > concreteStop * 1.15f, "rain still lengthens concrete braking distance");
        }
        finally { Surfaces.Wetness = wetness; }
        foreach (float speed in new[] { 30f, 100f, 150f })
        foreach (float steer in new[] { -0.4f, 0.4f })
        {
            var car = Rolling(SurfaceType.Gravel, speed);
            var heading = car.Forward;
            Run(car, 0.7f, () => car.SetControls(0f, 1f, steer, false, false));
            float yaw = heading.SignedAngleTo(car.Forward, Vector3.Up);
            Check(float.IsFinite(car.Speed) && car.Up.Y > 0.8f && yaw * steer < 0,
                $"gravel: stable brake steering both directions at {speed} km/h ({steer})");
        }
    }
}
