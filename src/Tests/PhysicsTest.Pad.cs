using System;
using System.Globalization;
using CCCPRacing.Car;
using CCCPRacing.Race;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    /// <summary>Game pads: the dead zones and the curve, analog throttle and brake, reverse on LT, the key path unchanged, the board's input mark.</summary>
    void PadTest()
    {
        GD.Print("Game pad");
        PadMappingTest();
        PadKeysUnchangedTest();
        PadThrottleTest();
        PadBrakeTest();
        PadReverseTest();
        PadSteerRateTest();
        PadBoardTest();
    }

    void PadMappingTest()
    {
        Check(DriverInput.Pedal(0.03f) == 0 && DriverInput.Pedal(0.05f) == 0 && DriverInput.Pedal(1f) == 1f
              && Math.Abs(DriverInput.Pedal(0.525f) - 0.5f) < 1e-4f, "triggers: nothing under 0.05, then rescaled to 0..1 (0.525 -> 0.50)");
        float half = DriverInput.Steering(0.56f, 0), diag = DriverInput.Steering(0.1f, 0.9f);
        GD.Print($"  stick X 0.10 -> {DriverInput.Steering(0.1f, 0):F3}, 0.56 -> {half:F3}, 1.0 -> {DriverInput.Steering(1f, 0):F3}; (0.1, 0.9) -> {diag:F3}; (0, 0.9) -> {DriverInput.Steering(0, 0.9f):F3}");
        Check(DriverInput.Steering(0.1f, 0) == 0 && DriverInput.Steering(0.05f, 0.08f) == 0, "stick: nothing inside the radial dead zone (0.12)");
        Check(DriverInput.Steering(1f, 0) == 1f && DriverInput.Steering(-1f, 0) == -1f && DriverInput.Steering(0.8f, 0.8f) <= 1f, "stick: full travel is full lock (and never more on a diagonal)");
        Check(Math.Abs(half - MathF.Pow(0.5f, DriverInput.SteerCurve)) < 1e-4f && half < 0.35f, $"stick: half travel steers {half:F2} (the curve keeps small corrections small)");
        Check(DriverInput.Steering(0, 0.9f) == 0 && Math.Abs(diag) < 0.05f, "stick: pushed up or down it does not steer");
        bool inverse = true;
        foreach (float v in new[] { 0f, 0.01f, 0.3f, 0.5f, 0.99f, 1f })
        {
            inverse &= Math.Abs(DriverInput.Pedal(DriverInput.PedalRaw(v)) - v) < 1e-4f;
            inverse &= Math.Abs(DriverInput.Steering(DriverInput.SteeringRaw(v), 0) - v) < 1e-4f && Math.Abs(DriverInput.Steering(DriverInput.SteeringRaw(-v), 0) + v) < 1e-4f;
        }
        Check(inverse, "the raw values --pad-autodrive puts in the pad map back to the autopilot's");
    }

    /// <summary>A fixed drive on the key path (accelerate, brake, reverse, steer, handbrake); the same numbers as before the pads.</summary>
    static string KeyPathScenario()
    {
        var car = NewCar(new FlatGround());
        Run(car, 1f);
        float t = 0;
        Run(car, 7f, () =>
        {
            if (t < 3f) car.SetControls(true, false, t > 1.5f ? 0.6f : 0f, false);
            else if (t < 4.5f) car.SetControls(false, true, -1f, false);
            else if (t < 5.5f) car.SetControls(false, true, 0.3f, false);
            else if (t < 6.2f) car.SetControls(true, false, -1f, t > 5.9f);
            else car.SetControls(false, false, 0f, true);
            t += Dt;
        });
        return string.Format(CultureInfo.InvariantCulture, "{0:R} {1:R} {2:R} {3:R} {4:R} {5}", car.Pos.X, car.Pos.Z, car.Vel.X, car.Vel.Z, car.SteerAngle, car.Gear);
    }

    void PadKeysUnchangedTest()
    {
        // Recorded with this same scenario on the build before the pads (2407302), re-recorded when the
        // brake key went on braking a car still sliding sideways instead of selecting reverse (was
        // "18.815245 -17.87721 0.0050686593 0.018612128 0 1"), and again when the rebound damping and
        // the bump stop were made to hold a landing (was "18.671495 -17.94478 0.004878455 0.01837525 0 1"),
        // and again on 17 October when the rally car got its driven-wheel slip, harder acceleration and
        // powered-cornering assist (was "19.165956 -18.8093 0.005722409 0.011414798 0 1"; the car now
        // pulls out of the first bend much faster and is still rolling at the end). The pad changes do not
        // touch this path: it only calls CarSim.
        // 23 October: automatic N at rest intentionally removes the old idle
        // driveline load (previous: "25.031275 -57.996452 -3.6164894 -2.4945974 0 1").
        // With the owner's final 1200 rpm idle. IdleTest separately verifies N,
        // launch/reverse and rolling engine braking.
        // 28 October: the handbrake is a rear brake against the patch's motion that the engine can overpower
        // (previously "25.252047 -58.439014 -3.65101 -2.5115836 0 1"). The scenario presses the handbrake for
        // the last 0.3 s of throttle and afterwards; the same drive without the key is unchanged bit for bit
        // (HandbrakeThrottleTest checks "22.970406 -58.57387 -7.083739 -4.427338 0 1" recorded on 2b2fde0).
        // 2 November: the owner's steering (MaxSteer 0.8, SteerSpeedFalloff 70, FastSteerAssist 0.46; before: 0.7, 50, 0.25) turns the same scenario
        // a little tighter and further (previously "25.386398 -57.631176 -3.06181 -1.9607791 0 1").
        // 2 November: the petrol car's power +25 % (MaxTorque 310 -> 387.5): previously "26.37102 -56.936016 -2.8184862 -0.68292654 0 1" (with the new steering).
        const string Before = "26.109898 -56.97671 -3.2694411 -0.7999047 0 1";
        string now = KeyPathScenario();
        GD.Print($"  key path: {now}");
        Check(now == Before, "keyboard trajectory retains the current idle/handling baseline (bit for bit)");
    }

    /// <summary>0-100 km/h on flat concrete with the throttle pedal held at this much.</summary>
    static float ZeroToHundred(float throttle)
    {
        var car = NewCar(new FlatGround());
        Run(car, 1f);
        float t = 0;
        while (t < 30f && car.ForwardSpeed * 3.6f < 100f)
        {
            car.SetControls(throttle, 0f, 0f, false, true);
            car.Step(Dt);
            t += Dt;
        }
        return t;
    }

    void PadThrottleTest()
    {
        float full = ZeroToHundred(1f), half = ZeroToHundred(0.5f);
        GD.Print($"  0-100 km/h: RT 1.0 {full:F2} s, RT 0.5 {half:F2} s");
        Check(half > full * 1.3f && half < 30f, "half throttle accelerates clearly slower than full, and still gets there");
        var kb = NewCar(new FlatGround());
        var pad = NewCar(new FlatGround());
        Run(kb, 1f);
        Run(pad, 1f);
        Run(kb, 3f, () => kb.SetControls(true, false, 0f, false));
        Run(pad, 3f, () => pad.SetControls(1f, 0f, 0f, false, true));
        Check(kb.Pos == pad.Pos && kb.Vel == pad.Vel, "full RT drives exactly like the accelerate key");
    }

    /// <summary>Braking from 100 km/h with the brake pedal at this much: distance and how much of it the fronts were locked.</summary>
    static (float dist, float locked) PadStop(float brake)
    {
        var car = NewCar(new FlatGround());
        Run(car, 1f);
        car.Vel = car.Forward * (100f / 3.6f);
        Vector3 p0 = car.Pos;
        int steps = 0, locked = 0;
        Run(car, 12f, () =>
        {
            car.SetControls(0f, car.ForwardSpeed > 0.3f ? brake : 0f, 0f, false, true);
            if (car.Speed > 3f)
            {
                steps++;
                if (car.Wheels[0].Locked || car.Wheels[1].Locked) locked++;
            }
        });
        return (car.Pos.DistanceTo(p0), locked / (float)Math.Max(1, steps));
    }

    void PadBrakeTest()
    {
        var full = PadStop(1f);
        var half = PadStop(0.5f);
        var light = PadStop(0.3f);
        GD.Print($"  100-0 km/h: LT 1.0 {full.dist:F1} m (fronts locked {full.locked * 100:F0}%), LT 0.5 {half.dist:F1} m ({half.locked * 100:F0}%), LT 0.3 {light.dist:F1} m ({light.locked * 100:F0}%)");
        Check(half.dist > full.dist && light.dist > half.dist, "a lighter brake pedal stops longer");
        Check(half.locked < 0.05f && light.locked == 0, "half brake does not lock the wheels");
    }

    void PadReverseTest()
    {
        var car = NewCar(new FlatGround());
        Run(car, 1f);
        Run(car, 0.2f, () => car.SetControls(0f, 0.05f, 0f, false, true));
        bool held = car.Gear == 1 && car.Brake > 0;
        Run(car, 2f, () => car.SetControls(0f, 0.6f, 0f, false, true));
        bool backwards = car.Gear == -1 && car.ForwardSpeed < -1f;
        float backSpeed = car.ForwardSpeed;
        Run(car, 3f, () => car.SetControls(0.8f, 0f, 0f, false, true));
        GD.Print($"  LT 0.6 from a standstill: {backSpeed * 3.6f:F1} km/h after 2 s; then RT: gear {car.Gear}, {car.ForwardSpeed * 3.6f:F1} km/h");
        Check(held, "a touch of LT at a standstill only holds the car");
        Check(backwards, "LT at a standstill selects reverse and drives backwards");
        Check(car.Gear >= 1 && car.ForwardSpeed > 1f, "RT brakes the reverse and then drives forward again (in 3 s the stronger pickup is already in second)");
    }

    void PadSteerRateTest()
    {
        // From straight to half lock at 20 m/s: the stick gets there faster than a key's ramp.
        float Reach(bool pad)
        {
            var car = NewCar(new FlatGround());
            Run(car, 1f);
            car.Vel = car.Forward * 20f;
            float t = 0, target = car.P.MaxSteer / (1f + 20f / car.P.SteerSpeedFalloff) * 0.5f;
            while (t < 2f && car.SteerAngle < target * 0.95f)
            {
                if (pad) car.SetControls(1f, 0f, 0.5f, false, true);
                else car.SetControls(true, false, 0.5f, false);
                car.Step(Dt);
                t += Dt;
            }
            return t;
        }
        float keys = Reach(false), stick = Reach(true);
        GD.Print($"  half lock at 72 km/h: keys {keys * 1000:F0} ms, stick {stick * 1000:F0} ms");
        // (the rates are 30 and 12 rad/s, a ratio of 0.4; the steps of 1/240 s make 3 against 6 of them at this lock: 0.5, which "< 0.5" fails on a tie)
        Check(stick < keys * 0.6f, "the stick steers faster than the keys' rate");
    }

    void PadBoardTest()
    {
        string path = "user://test_pad_leaderboard.cfg";
        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(path));
        // A board as the game wrote it before the pads (no input key), the Stakhanov kind with powers.
        const string Old = "[driver]\n\nlast_name=\"ANTTI\"\n\n[entry_0]\n\nname=\"ANTTI\"\ntime=47.211178\ndate=\"2026-09-28\"\npower=100\n\n"
            + "[entry_1]\n\nname=\"TOVARISH\"\ntime=50.4452\ndate=\"2026-09-28\"\npower=350\n\n[entry_2]\n\nname=\"ANTTI\"\ntime=62.2894\ndate=\"2026-09-28\"\npower=40\n";
        using (var f = FileAccess.Open(path, FileAccess.ModeFlags.Write)) f.StoreString(Old);
        var b = Leaderboard.Load(path, capacity: Leaderboard.StakhanovSize);
        bool kb = b.Entries.Count == 3;
        foreach (var e in b.Entries) kb &= !e.Pad;
        Check(kb, "an old board without input keys: every lap is a keyboard lap");
        b.Save();
        string back = FileAccess.GetFileAsString(path);
        Check(back == Old, "load and save of an old board gives the same file byte for byte");

        b.Insert("PADDY", 49f, "2026-09-29", 200, pad: true);
        b.Save();
        string text = FileAccess.GetFileAsString(path);
        var l = Leaderboard.Load(path, capacity: Leaderboard.StakhanovSize);
        bool rest = l.Entries.Count == 4 && l.Entries[1].Name == "PADDY" && l.Entries[1].Pad && l.Entries[1].Power == 200;
        rest &= l.Entries[0] == b.Entries[0] && l.Entries[2] == b.Entries[2] && l.Entries[3] == b.Entries[3] && !l.Entries[0].Pad && !l.Entries[3].Pad;
        Check(text.Contains("input=\"pad\"") && text.Split("input=").Length == 2, "a pad lap saves input=\"pad\" (keyboard laps no key)");
        Check(rest && l.LastName == "ANTTI", "the pad lap loads back as a pad lap; the other laps and fields are unchanged");
        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(path));

        // PADBOARDS=<dir of COPIES of real boards>: each leaderboard_*.cfg there loads and saves unchanged.
        string dir = OS.GetEnvironment("PADBOARDS");
        if (dir == "") return;
        foreach (string f in DirAccess.GetFilesAt(dir))
        {
            if (!f.StartsWith("leaderboard_") || !f.EndsWith(".cfg")) continue;
            string p = dir.TrimEnd('/') + "/" + f, before = FileAccess.GetFileAsString(p);
            var board = Leaderboard.Load(p, capacity: Leaderboard.StakhanovSize);
            board.Save();
            Check(FileAccess.GetFileAsString(p) == before, $"copy of {f}: {board.Entries.Count} laps load and save byte for byte");
        }
    }
}
