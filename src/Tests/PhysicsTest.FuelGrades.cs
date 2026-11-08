using System;
using CCCPRacing.Car;
using System.Linq;
using CCCPRacing.Race;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    /// <summary>FUELLAB=1: the rally's fuels: power, OCTANE BOOSTER, the norms at each pace, record files, saved rallies and medals.</summary>
    void FuelGradesTest()
    {
        GD.Print("Fuel grades: power, norms, records, resume and medals");
        Check(FuelGrades.Percent.SequenceEqual(new[] { 50, 100, 200, 200 }) && Training.Count == 3 && FuelGrades.Count == 4, "diesel 50, petrol 100, jet fuel 200 percent; the Sputnik has the jet fuel's; the LAN keeps three");
        Check(Training.LapsCount == 4 && Training.PercentForKey(4) == Training.SputnikPercent && Training.GradeOf(201) == FuelGrade.Sputnik && Training.GradeOf(200) == FuelGrade.Rocket
            && Training.FuelShort(201) == "SPUT" && Training.FuelName(201) == "SPUTNIK" && Training.Overdrive(201) == 0f && !Training.Valid(201), "Stakhanov laps offer the Sputnik as the fourth fuel (201), the LAN does not");
        Check(Enumerable.Range(0, 3).Select(i => FuelGrades.BoostPercent((FuelGrade)i)).SequenceEqual(new[] { 175, 350, 500 }),
            "OCTANE BOOSTER is 3.5 times the fuel's power, to the physics' 500");
        Check(FuelGrades.Parse("rocket") == FuelGrade.Rocket && FuelGrades.Parse("nonsense") == FuelGrade.Petrol, "fuel names parse, petrol otherwise");

        var run = new PowerRun[3];
        for (int i = 0; i < 3; i++)
        {
            run[i] = MeasurePower(FuelGrades.PhysicsPercent((FuelGrade)i), (FuelGrade)i);
            GD.Print($"  {FuelGrades.Names[i],-11} {FuelGrades.PhysicsPercent((FuelGrade)i),3} %: top {run[i].Top:F1} km/h, 0-100 {run[i].T100:F2} s, launch wheelspin {run[i].SpinShare * 100:F0}%");
        }
        Check(run[1].T100 >= run[2].T100, "rocket fuel: never a slower 0-100 than petrol");

        // The SPUTNIK: the jet fuel's engine on all four wheels.
        var sp = new CarParams(); FuelGrades.ApplyEngine(sp, FuelGrade.Sputnik);
        var jp = new CarParams(); FuelGrades.ApplyEngine(jp, FuelGrade.Rocket);
        Check(Math.Abs(sp.DriveFront - 0.4f) < 1e-6f && jp.DriveFront == 0f && FuelGrades.PhysicsPercent(FuelGrade.Sputnik) == 175
              && FuelGrades.TurboGainFor(FuelGrade.Sputnik) == FuelGrades.RocketTurboGain, "the Sputnik: 40 % of the force to the front, the jet fuel's 175 % and turbo");
        var jet = MeasurePower(175, FuelGrade.Rocket);
        var spu = MeasurePower(175, FuelGrade.Sputnik);
        GD.Print($"  jet fuel 0-100 {jet.T100:F2} s, wheelspin {jet.SpinShare * 100:F0}%; Sputnik 0-100 {spu.T100:F2} s, wheelspin {spu.SpinShare * 100:F0}%");
        Check(spu.T100 > 0 && spu.T100 <= jet.T100 + 0.01f && spu.Top > 100f, "the Sputnik pulls away no slower than the jet fuel");
        {
            // Loose ground, where the drive axle spins: the four wheels pull harder than two.
            float Dist(FuelGrade g)
            {
                var car = PowerCar(175, SurfaceType.Gravel, g);
                float p0 = car.Pos.Z, t = 0;
                Run(car, 5f, () => { car.SetControls(true, false, 0, false); t += Dt; });
                return Math.Abs(car.Pos.Z - p0);
            }
            float dj = Dist(FuelGrade.Rocket), ds = Dist(FuelGrade.Sputnik);
            GD.Print($"  gravel, 5 s from rest: jet fuel {dj:F1} m, Sputnik {ds:F1} m");
            Check(ds > dj * 1.03f, "on gravel the four-wheel drive gets further in 5 s");
            var c4 = PowerCar(175, SurfaceType.Gravel, FuelGrade.Sputnik);
            Run(c4, 3f, () => c4.SetControls(true, false, 0, false));
            Check(c4.Wheels[0].ForceLong > 0 && c4.Wheels[1].ForceLong > 0 && c4.Wheels[2].ForceLong > 0, "all four wheels push the car forward");
        }

        foreach (string id in new[] { "ilyich", "siberia", "atomgrad", "bonus" })
        {
            var def = TrackDef.Load($"res://tracks/{id}.json").Norms;
            var petrol = NormPlan.LadderFor(def, FuelGrade.Petrol);
            var diesel = NormPlan.LadderFor(def, FuelGrade.Diesel);
            var rocket = NormPlan.LadderFor(def, FuelGrade.Rocket);
            var sputnik = NormPlan.LadderFor(def, FuelGrade.Sputnik);
            Check(petrol.Length == diesel.Length && petrol.Length == rocket.Length && petrol.Length == sputnik.Length, $"{id}: every fuel has the same number of norms");
            Check(Enumerable.Range(0, petrol.Length).All(i => diesel[i] > petrol[i] && petrol[i] > rocket[i] && rocket[i] > sputnik[i]),
                $"{id}: each better fuel is tighter on every norm (diesel is the reference)");
            Check(def.Ladder.SequenceEqual(petrol), $"{id}: the stored petrol ladder is petrol's");
            Check(diesel.Zip(diesel.Skip(1)).All(p => p.First > p.Second) && sputnik.Zip(sputnik.Skip(1)).All(p => p.First > p.Second), $"{id}: the ladders still descend");
            Check(diesel.Concat(sputnik).All(t => System.MathF.Abs(t * 10f - System.MathF.Round(t * 10f)) < 1e-3f), $"{id}: norms are in tenths of a second");
        }

        var rs = new RecordStore("user://test_fuel_records/", "ilyich", "sig");
        var paths = Enumerable.Range(0, RecordStore.Sets).SelectMany(s => new[] { rs.GhostForSet(s), rs.BoardForSet(s) }).ToList();
        Check(paths.Distinct().Count() == paths.Count, "every record set has its own ghost and board file");
        Check(rs.GhostForSet(0) == rs.GhostPath && rs.BoardForSet(0) == rs.BoardPath && rs.GhostForSet(1) == rs.StakhanovGhostPath,
            "petrol keeps the rally's original files, Stakhanov its own");
        Check(RecordStore.SetOf(FuelGrade.Petrol) == 0 && RecordStore.SetOf(FuelGrade.Diesel) == 2 && RecordStore.SetOf(FuelGrade.Rocket) == 3 && RecordStore.SetOf(FuelGrade.Sputnik) == 4, "record sets by fuel");

        string dir = "user://test_fuel_resume/";
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(dir));
        var point = new RallyResume.Checkpoint(3, 2, 9, 1, 123.5f, 9, 6) { PassedTimes = new[] { 74f, 71f, 69f }, PassedTargets = new[] { 75f, 72.5f, 70f } };
        RallyResume.Save(dir, "ilyich", "sig", point, FuelGrade.Rocket);
        Check(RallyResume.FuelOf(dir, "ilyich") == FuelGrade.Rocket, "a saved rally remembers its fuel");
        RallyResume.Save(dir, "siberia", "sig", point);
        Check(RallyResume.FuelOf(dir, "siberia") == FuelGrade.Petrol, "a rally saved without a fuel was petrol's");
        RallyResume.Clear(dir, "ilyich");
        RallyResume.Clear(dir, "siberia");

        string cdir = "user://test_fuel_campaign/";
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(cdir));
        var cfg = new ConfigFile();
        cfg.SetValue("medals", "ilyich", (int)NormPlan.Medal.Silver);      // earned before the fuels existed
        cfg.Save(cdir + "campaign.cfg");
        var campaign = new Campaign(cdir);
        Check(campaign.BestOn("ilyich", FuelGrade.Petrol) == NormPlan.Medal.Silver && campaign.BestOn("ilyich", FuelGrade.Rocket) == NormPlan.Medal.None,
            "medals from before the fuels are petrol's");
        Check(campaign.Award("ilyich", NormPlan.Medal.Gold, FuelGrade.Rocket) && campaign.Best("ilyich") == NormPlan.Medal.Gold, "a rocket fuel gold counts for the track");
        Check(campaign.BestOn("ilyich", FuelGrade.Rocket) == NormPlan.Medal.Gold && campaign.BestOn("ilyich", FuelGrade.Petrol) == NormPlan.Medal.Silver,
            "...and is kept apart per fuel (the old silver stays petrol's)");
        Check(!campaign.Award("ilyich", NormPlan.Medal.Bronze, FuelGrade.Diesel) && campaign.BestOn("ilyich", FuelGrade.Diesel) == NormPlan.Medal.Bronze,
            "a lower medal on another fuel is kept for that fuel, the track's best stays");
    }
}
