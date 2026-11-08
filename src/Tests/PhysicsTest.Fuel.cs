using System;
using CCCPRacing.Car;
using CCCPRacing.Race;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    /// <summary>
    /// Fuel: the tank's model (idle, flat out, economical), laps per tank for the autopilot at two
    /// aggressions, the depot's fill, the babushkas' canister rules and the dry-tank tow's cost.
    /// </summary>
    void FuelTest()
    {
        GD.Print("Fuel");
        var p = new CarParams();
        var tank = new FuelTank(p) { Enabled = true };
        Check(tank.Litres == p.TankLitres && tank.Fraction == 1f && !tank.Low, $"a full tank: {p.TankLitres:F0} l");

        // The model: idle drain, flat out at the redline, part throttle at low revs.
        float idle = FuelTank.FlowFor(p, 0f, p.IdleRpm), full = FuelTank.FlowFor(p, 1f, p.Redline), eco = FuelTank.FlowFor(p, 0.5f, 3500f);
        GD.Print($"  flow: idle {idle * 60:F2} l/min, flat out at the redline {full:F3} l/s, half throttle at 3500 rpm {eco:F3} l/s");
        Check(idle > 0f && idle < 0.05f && full > 10f * idle && eco < full * 0.4f, "idle drinks a little, flat out most, gentle driving far less");
        tank.Refill();
        float burnt = 0;
        for (int i = 0; i < 600; i++) burnt += tank.Burn(0.1f, 0f, p.IdleRpm);
        Check(Math.Abs(burnt - idle * 60f) < 1e-3f, $"a minute at idle: {burnt:F2} l");
        tank.Rate = 2f;
        Check(Math.Abs(tank.Burn(1f, 1f, p.Redline) - 2f * full) < 1e-4f, "--fuel-rate=2 doubles it");
        tank.Rate = 1f;
        Check(Math.Abs(tank.Burn(1f, 1f, p.Redline, FuelTank.KeysRate) - FuelTank.KeysRate * full) < 1e-4f
              && Math.Abs(tank.Burn(1f, 1f, p.Redline, FuelTank.BoostRate) - 2f * full) < 1e-4f,
            "keyboard throttle drinks 0.8 of it (all or nothing on a key), OCTANE BOOSTER twice as much");
        tank.Refill(0.02f);
        Check(tank.Sputtering && tank.Low && !tank.Empty, "2 %: low and sputtering");
        for (int i = 0; i < 100; i++) tank.Burn(0.1f, 1f, p.Redline);
        Check(tank.Empty && tank.Litres == 0f && tank.Burn(1f, 1f, p.Redline) == 0f, "runs dry and stays at 0");
        var off = new FuelTank(p) { Enabled = false };
        Check(off.Burn(10f, 1f, p.Redline) == 0f && !off.Empty && !off.Low, "Stakhanov, LAN, the title: no consumption");

        // Laps per tank for the autopilot, flat out (1.0) and economical (0.8).
        var track = TrackBuilder.Build(TrackDef.Load("res://tracks/bonus.json"));
        float perLap10 = LitresPerLap(track, 1f, out float lap10), perLap08 = LitresPerLap(track, 0.8f, out float lap08);
        float tanks10 = p.TankLitres / perLap10, tanks08 = p.TankLitres / perLap08;
        GD.Print($"  autopilot 1.0: {perLap10:F1} l/lap (lap {lap10:F1} s), {tanks10:F2} laps per tank; 0.8: {perLap08:F1} l/lap (lap {lap08:F1} s), {tanks08:F2} laps per tank ({(tanks08 / tanks10 - 1f) * 100f:F0} % more)");
        // (Until 0.56 the computer driver turned over once a lap at (56,130) and lay idle on its roof:
        // 6.9 laps a tank and 28 % more driven gently. Steadied in the air it laps clean.)
        // (The engine's fuel use was halved on 5 October with the day and night: the empty road's 13 laps a tank are about 3-5 in the full rally world.)
        // 2 November: FuelFull 2.22 -> 1.6 (the throttle down was too thirsty) and the owner's steering (0.8 / 70 / 0.46) that changes how the computer driver
        // takes the bends: 15.4 laps a tank flat out (3.2 l a lap; 11.4 with the old FuelFull and the new steering, 12.9 with both old), and the economical
        // drive (0.8 of the speed) only 13 % longer (it was 60-100 % longer before the steering: the computer driver at 0.8 no longer saves much).
        Check(tanks10 is >= 10.5f and <= 13f, "flat out on the empty road: a tank lasts 10.5-13 laps (8 November: the petrol car 175 Nm, 11.8 laps; was 14.5-17)");
        Check(tanks08 / tanks10 is >= 0.85f and <= 1.4f, "driven economically: a little longer (the computer driver at 0.8 of the speed; the idle drain is small)");

        // The depot: 6 l/s inside the zone at a standstill, a full 50 l tank in 8-9 s, nothing when moving or outside.
        float fill = 0;
        tank.Refill(0f);
        for (float t = 0; t < 10f && tank.Fraction < 1f; t += 0.1f)
        {
            if (FuelDepot.Fills(new Vector2(3f, 2f), Vector2.Zero, 0.5f)) tank.Fill(FuelDepot.FillRate * 0.1f);
            fill = t + 0.1f;
        }
        Check(tank.Fraction == 1f && fill is > 8f and < 9f, $"the depot fills an empty tank in {fill:F1} s");
        Check(!FuelDepot.Fills(new Vector2(3f, 2f), Vector2.Zero, 1.2f), "rolling at 4.3 km/h: no fuel");
        Check(!FuelDepot.Fills(new Vector2(FuelDepot.ZoneRadius + 0.2f, 0f), Vector2.Zero, 0f), "outside the zone: no fuel");

        // The babushkas' canisters: pass within her reach at any speed and she tosses it in.
        Check(Babushkas.JudgePass(2f) == Babushkas.Pass.Catch, "2 m from her: the can is in the car");
        Check(Babushkas.JudgePass(-4.3f) == Babushkas.Pass.Catch, "either side, 4.3 m off (in her lane): caught");
        Check(Babushkas.JudgePass(Babushkas.PassReach + 0.1f) == Babushkas.Pass.Missed, "out of her reach: nothing");
        float gap = Babushkas.PassReach - CarSim.CollisionRadius - Babushkas.BodyRadius;
        Check(gap > 2f && gap < 3.5f, $"her reach leaves {gap:F1} m between her and the car's body circles: a catch is never a hit");
        Check(Babushkas.Sees(new Vector2(0, 30), Vector2.Zero, new Vector2(0, 15)), "a car coming at her 30 m off: she holds the can out");
        Check(!Babushkas.Sees(new Vector2(0, -10), Vector2.Zero, new Vector2(0, 15)) && !Babushkas.Sees(new Vector2(0, 100), Vector2.Zero, new Vector2(0, 15)),
            "a car going away, or far off: she does not");
        tank.Refill(0.5f);
        float before = tank.Litres;
        tank.Fill(Babushkas.CanisterLitres);
        Check(Math.Abs(tank.Litres - before - 20f) < 1e-3f, "a canister: +20 l");
        tank.Fill(Babushkas.CanisterLitres);
        Check(tank.Fraction == 1f, "never past full");
        Check(Babushkas.CanRespawn.min >= 60f && Babushkas.CanRespawn.max <= 90f, "she gets a new can after 60-90 s");

        // Running dry costs a lap of the norm's window (and the towed lap is void).
        var plan = new NormPlan();
        plan.Reset(true, 2);
        Check(!plan.Penalize(RaceScene.TowLaps) && plan.Left == NormPlan.WindowLaps - 1 && plan.OnLap(50f, false) == NormPlan.Outcome.Continue
            && plan.Left == NormPlan.WindowLaps - 2, "towed to the depot: -1 lap, and the void lap uses one more");
    }

    /// <summary>Autopilot laps on the bonus track: litres per flying lap (laps 2-3) and the lap time.</summary>
    static float LitresPerLap(TrackData track, float aggression, out float lapTime)
    {
        var car = new CarSim(new CarParams(), track) { Obstacles = track.Obstacles, WorldMax = track.WorldSize - 4 };
        car.Reset(track.StartPos, track.StartYaw);
        var tank = new FuelTank(car.P) { Enabled = true, Litres = 1e6f };
        var ap = new AutoPilot(track) { Aggression = aggression };
        var timer = new LapTimer(track);
        float used = 0, lapSum = 0;
        int laps = 0;
        timer.LapCompleted += (t, _) =>
        {
            if (++laps is 2 or 3) lapSum += t;
            if (laps == 1) used = 0;
        };
        timer.Start();
        for (float t = 0; t < 400f && laps < 3; t += Dt)
        {
            ap.Drive(car, Dt);
            car.Step(Dt);
            if (car.UpsideDownTime > 1.5f)
            {
                if (OS.HasEnvironment("FUELFLIPS")) GD.Print($"  fuel lap test: the car turned over at ({car.Pos.X:F0},{car.Pos.Z:F0}), lap {laps + 1}, {t:F1} s");
                int i = track.NearestCenter(new Vector2(car.Pos.X, car.Pos.Z), out _);
                car.Reset(track.CenterPos(i) + Vector3.Up * 0.7f, TrackData.YawFor(track.CenterDir(i)));
            }
            used += tank.Burn(Dt, car.Throttle, car.Rpm);
            timer.Update(Dt, new Vector2(car.Pos.X, car.Pos.Z));
        }
        lapTime = lapSum / 2f;
        return used / 2f;
    }
}
