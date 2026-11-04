using System.Linq;
using CCCPRacing.Car;
using CCCPRacing.Race;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    /// <summary>
    /// FUELLAPS=track[,track...]: the computer driver's lap time (the mean of its laps 2 and 3, on the empty road) on each fuel's car: the diesel's engine
    /// (FuelGrades.ApplyEngine) at its full power, the old diesel (the petrol engine at 50 %), petrol and rocket fuel (200 %). The norms' factors follow the ratios.
    /// </summary>
    void FuelLapsLab()
    {
        { var r = MeasurePower(125); var b = MeasurePower(100); GD.Print($"  petrol 125 %: top {r.Top:F1} km/h, 0-100 {r.T100:F2} s, wheelspin {r.SpinShare * 100:F0}%   (100 %: top {b.Top:F1}, 0-100 {b.T100:F2} s, wheelspin {b.SpinShare * 100:F0}%)"); }
        foreach (string id in OS.GetEnvironment("FUELLAPS").Split(',', System.StringSplitOptions.RemoveEmptyEntries))
        {
            var def = TrackDef.Load($"res://tracks/{id}.json");
            var track = TrackBuilder.Build(def);
            float petrol = AutoLap(track, FuelGrade.Petrol, 100, false, out float lp), diesel = AutoLap(track, FuelGrade.Diesel, 100, true, out float ld), old = AutoLap(track, FuelGrade.Diesel, 50, false, out float lo), rocket = AutoLap(track, FuelGrade.Rocket, FuelGrades.PhysicsPercent(FuelGrade.Rocket), false, out float lr);
            { float jet = AutoLap(track, FuelGrade.Rocket, 175, true, out float lj), spu = AutoLap(track, FuelGrade.Sputnik, 175, true, out float ls); GD.Print($"  sputnik {id}: jet fuel {jet:F2} s, Sputnik {spu:F2} s ({spu / jet:F3} of jet fuel; {spu / petrol:F3} of petrol), litres {lj:F2} / {ls:F2}"); }
            float p125 = AutoLap(track, FuelGrade.Petrol, 125, false, out float l125);
            GD.Print($"  petrol at 125 % power: {p125:F2} s ({p125 / petrol:F3} of petrol), {l125:F2} l a lap ({l125 / lp:F2} of petrol)");
            GD.Print($"  litres a lap: petrol {lp:F2}, diesel {ld:F2} ({ld / lp:F2} of petrol), old diesel {lo:F2}, rocket {lr:F2} ({lr / lp:F2})");
            GD.Print($"fuellaps {id}: petrol {petrol:F2} s, diesel {diesel:F2} s ({diesel / petrol:F3}), old diesel {old:F2} s ({old / petrol:F3}), rocket {rocket:F2} s ({rocket / petrol:F3}); the norms' factors now {NormPlan.FactorFor(def.Norms, FuelGrade.Diesel):F2} / {NormPlan.FactorFor(def.Norms, FuelGrade.Rocket):F2}");
        }
    }

    /// <summary>TURBOLAB: full throttle on the rocket fuel's car from a standstill: the revs, the gear and the turbo's boost every half second.</summary>
    void TurboLab()
    {
        var car = PowerCar(FuelGrades.PhysicsPercent(FuelGrade.Rocket), SurfaceType.Concrete, FuelGrade.Rocket);
        float t = 0, next = 0;
        Run(car, 12f, () =>
        {
            car.SetControls(true, false, 0, false);
            t += Dt;
            if (t >= next) { GD.Print($"turbolab {t:F1} s: {car.Speed * 3.6f:5.1f} km/h, gear {car.Gear}, {car.Rpm:F0} rpm, boost {car.Boost:F2}"); next += 0.5f; }
        });
    }

    static float AutoLap(TrackData track, FuelGrade fuel, int percent, bool engine) => AutoLap(track, fuel, percent, engine, out _);

    static float AutoLap(TrackData track, FuelGrade fuel, int percent, bool engine, out float litres)
    {
        var p = new CarParams();
        if (engine) FuelGrades.ApplyEngine(p, fuel);
        var car = new CarSim(p, track) { Obstacles = track.Obstacles, WorldMax = track.WorldSize - 4, PowerScale = percent / 100f, TurboGain = FuelGrades.TurboGainFor(fuel) };
        car.Reset(track.StartPos, track.StartYaw);
        var ap = new AutoPilot(track) { Aggression = 1f };
        var timer = new LapTimer(track);
        var tank = new FuelTank(p) { Enabled = true, Litres = 1e6f, Rate = FuelGrades.Burn[(int)fuel] };
        float sum = 0, used = 0; int laps = 0;
        timer.LapCompleted += (t, _) => { if (++laps is 2 or 3) sum += t; if (laps == 1) used = 0; };
        timer.Start();
        for (float t = 0; t < 500f && laps < 3; t += Dt)
        {
            ap.Drive(car, Dt);
            car.Step(Dt);
            if (car.UpsideDownTime > 1.5f)
            {
                int i = track.NearestCenter(new Vector2(car.Pos.X, car.Pos.Z), out _);
                car.Reset(track.CenterPos(i) + Vector3.Up * 0.7f, TrackData.YawFor(track.CenterDir(i)));
            }
            used += tank.Burn(Dt, car.Throttle, car.Rpm);
            timer.Update(Dt, new Vector2(car.Pos.X, car.Pos.Z));
        }
        litres = used / 2f;
        return laps >= 3 ? sum / 2f : float.NaN;
    }
}
