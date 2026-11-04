using System;
using CCCPRacing.Car;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    /// <summary>SLIDEPROBE=1: the slide techniques at 60 km/h on dirt, 1.6 s from the turn-in: heading change, peak slip angle, speed lost.</summary>
    void ProbeSlides()
    {
        foreach (var s in new[] { SurfaceType.Dirt, SurfaceType.Gravel, SurfaceType.Concrete })
        {
            GD.Print($"== {s}");
            Probe(s, "steer only", (c, t) => c.SetControls(true, false, 1f, false));
            Probe(s, "steer, no gas", (c, t) => c.SetControls(false, false, 1f, false));
            Probe(s, "locked brake + steer", (c, t) => c.SetControls(false, true, 1f, false));
            Probe(s, "pumping + steer", (c, t) => c.SetControls(false, (t % 0.3f) < 0.15f, 1f, false));
            Probe(s, "handbrake 0.5 s + steer", (c, t) => c.SetControls(t > 0.5f, false, 1f, t < 0.5f));
            Probe(s, "clutch kick + steer", (c, t) => { c.Clutch = t < 0.35f; c.SetControls(true, false, 1f, false); });
            Probe(s, "away 0.45 s, in (gas on)", (c, t) => c.SetControls(true, false, t < 0.45f ? -1f : 1f, false));
            Probe(s, "flick (away 0.45 s, lift, in)", (c, t) => c.SetControls(t > 0.7f, false, t < 0.45f ? -1f : 1f, false));
        }
        // A keyboard driver through S-bends on the throttle: full lock one way, then the other.
        foreach (float half in new[] { 0.4f, 0.6f, 0.9f })
        {
            var car = NewCar(new FlatGround { Surface = SurfaceType.Dirt });
            Run(car, 1f);
            car.Gear = 2;
            car.Vel = car.Forward * (65f / 3.6f);
            float peakSlip = 0, t = 0;
            for (; t < 6f; t += Dt)
            {
                car.SetControls(true, false, (t % (2 * half)) < half ? 1f : -1f, false);
                car.Step(Dt);
                var hv = new Vector2(car.Vel.X, car.Vel.Z);
                if (hv.Length() > 2f) peakSlip = Math.Max(peakSlip, Mathf.RadToDeg(MathF.Abs(new Vector2(car.Forward.X, car.Forward.Z).AngleTo(hv))));
            }
            GD.Print($"  keyboard S-bends, {half:F1} s a side, throttle on: {car.Flicks} flicks in 6 s, peak slip {peakSlip:F0}°, {car.Speed * 3.6f:F0} km/h, spun {car.Spun}");
        }
        GetTree().Quit();
    }

    /// <summary>
    /// The slide techniques the school teaches stay distinct (60 km/h into a full-lock bend on dirt):
    /// locked brakes retain limited steering, pumping turns more; the handbrake and a clutch kick swing the car well
    /// past steering alone, the kick keeping its speed; the flick swings it past the same steering
    /// done with the throttle on.
    /// </summary>
    void SlideTechniquesTest()
    {
        GD.Print("Slide techniques (60 km/h, dirt, full lock, 1.6 s: heading change)");
        var s = SurfaceType.Dirt;
        var steer = Probe(s, "steer only", (c, t) => c.SetControls(true, false, 1f, false));
        var locked = Probe(s, "locked brake + steer", (c, t) => c.SetControls(false, true, 1f, false));
        var pump = Probe(s, "pumping + steer", (c, t) => c.SetControls(false, (t % 0.3f) < 0.15f, 1f, false));
        var hand = Probe(s, "handbrake 0.5 s + steer", (c, t) => c.SetControls(t > 0.5f, false, 1f, t < 0.5f));
        var kick = Probe(s, "clutch kick + steer", (c, t) => { c.Clutch = t < 0.35f; c.SetControls(true, false, 1f, false); });
        var away = Probe(s, "away 0.45 s, in (gas on)", (c, t) => c.SetControls(true, false, t < 0.45f ? -1f : 1f, false));
        // The same on the throttle with no flick at all: the steering's turn alone (measured 29° before the flick).
        const float steerAway0 = 29f;
        var flick = Probe(s, "flick (away 0.45 s, lift, in)", (c, t) => c.SetControls(t > 0.7f, false, t < 0.45f ? -1f : 1f, false));
        Check(locked.heading > 0 && locked.heading < steer.heading * 0.5f && pump.heading > locked.heading + 35f,
            "locked brakes retain limited steering; pumping restores substantially more turn");
        Check(hand.heading > steer.heading + 25f && kick.heading > steer.heading + 25f, "the handbrake and a clutch kick swing the car well past steering alone");
        Check(kick.speed > hand.speed + 4f, "a clutch kick keeps its speed (a power slide), the handbrake scrubs it");
        Check(flick.heading > away.heading + 15f,      // (+22 with the steering defaults of 2026-11-02, +25 before: the car steers so well that the pendulum adds less)
             "the Scandinavian flick with the throttle lifted swings the car into the bend (a pendulum)");
        Check(away.heading > steerAway0 + 10f, "...and on the throttle it still swings, less");
    }

    (float heading, float speed) Probe(SurfaceType s, string name, Action<CarSim, float> input)
    {
        var car = NewCar(new FlatGround { Surface = s });
        Run(car, 1f);
        car.Gear = 2;
        car.Vel = car.Forward * (60f / 3.6f);
        foreach (var w in car.Wheels) w.SpinSpeed = car.Vel.Length() / car.P.WheelRadius;
        for (float t = 0; t < 0.4f; t += Dt) { car.SetControls(true, false, 0f, false); car.Step(Dt); }   // settle at speed, straight
        Vector3 f0 = car.Forward;
        float v0 = car.Speed, peakSlip = 0, peakYaw = 0;
        for (float t = 0; t < 1.6f; t += Dt)
        {
            input(car, t);
            car.Step(Dt);
            var hv = new Vector2(car.Vel.X, car.Vel.Z);
            var hf = new Vector2(car.Forward.X, car.Forward.Z);
            if (hv.Length() > 2f) peakSlip = Math.Max(peakSlip, Mathf.RadToDeg(MathF.Abs(hf.AngleTo(hv))));
            peakYaw = Math.Max(peakYaw, MathF.Abs(car.AngVel.Y));
        }
        car.Clutch = false;
        float turned = Mathf.RadToDeg(new Vector2(f0.X, f0.Z).AngleTo(new Vector2(car.Forward.X, car.Forward.Z)));
        var vdir = new Vector2(car.Vel.X, car.Vel.Z);
        float path = Mathf.RadToDeg(new Vector2(f0.X, f0.Z).AngleTo(vdir));
        GD.Print($"  {name,-30} heading {MathF.Abs(turned),5:F0}°  path {MathF.Abs(path),5:F0}°  peak slip {peakSlip,4:F0}°  peak yaw {Mathf.RadToDeg(peakYaw),4:F0}°/s  speed {v0 * 3.6f:F0} -> {car.Speed * 3.6f:F0} km/h");
        return (MathF.Abs(turned), car.Speed * 3.6f);
    }
}
