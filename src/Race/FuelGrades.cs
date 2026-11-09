using System;
using System.Reflection;
using CCCPRacing.Car;
using Godot;

namespace CCCPRacing.Race;

/// <summary>The SOCIALIST RALLY's fuel: how much power the Pyatyorka has (and so how the norms are set) and how thirsty it is.</summary>
public enum FuelGrade { Diesel, Petrol, Rocket, Sputnik }

public static class FuelGrades
{
    public const int Count = 4;
    public static readonly string[] Names = { "DIESEL", "PETROL", "JET FUEL", "SPUTNIK" };
    public static readonly string[] Ids = { "diesel", "petrol", "rocket", "sputnik" };
    /// <summary>The engine's power in percent of the factory's. 100 is the game as it has always been.</summary>
    public static readonly int[] Percent = { 50, 100, 200, 200 };      // Sputnik shares jet fuel power; the ballot uses a separate stable ID.
    /// <summary>The tank's consumption factor by fuel (against the tuned base rate; 1 = the tuned one): the diesel sips, the Sputnik drinks.</summary>
    public static readonly float[] Burn = { 0.5f, 0.8f, 1.0f, 1.2f };
    /// <summary>What a car of this fuel is, in the real units and from the engine itself (ApplyEngine), so no text can go stale: peak torque in Nm (the power factor in it), the same on full turbo, mass in kg, the redline and the number of gears.</summary>
    public static (int Nm, int TurboNm, int Kg, int Rpm, int Gears) Spec(FuelGrade g)
    {
        var p = new CarParams(); ApplyEngine(p, g);
        float nm = p.MaxTorque * PhysicsPercent(g) / 100f;
        return ((int)MathF.Round(nm), (int)MathF.Round(nm * (1f + TurboGainFor(g))), (int)MathF.Round(p.Mass), (int)MathF.Round(p.Redline), p.Gears.Length);
    }

    /// <summary>The engine's cylinders: the Diesel a tractor four, the Petrol and the Jet fuel the Pyatyorka's four, the Sputnik the five of its warble.</summary>
    public static int Cylinders(FuelGrade g) => g == FuelGrade.Sputnik ? 5 : 4;

    /// <summary>The line of the FUEL card before the norms: gears, torque, revs and mass, nothing else (the Jet fuel's turbo torque beside its torque); the Sputnik is top secret.</summary>
    public static string CardLine(int grade)
    {
        var g = (FuelGrade)Math.Clamp(grade, 0, Count - 1);
        var s = Spec(g);
        return g switch
        {
            FuelGrade.Rocket => $"{s.Gears} GEAR {s.Nm} NM ({s.TurboNm} NM TURBO) {s.Rpm} RPM {s.Kg} KG",
            FuelGrade.Sputnik => "TOP SECRET: AWD PROTOTYPE",
            _ => $"{s.Gears} GEAR {s.Nm} NM {s.Rpm} RPM {s.Kg} KG",
        };
    }
    /// <summary>
    /// The diesel's engine, a diesel and not a petrol engine at half the power (tried out in LAB, 2 November): a lower redline with four close gears (a lower top speed, 8 November: the hills before the speed; was five taller gears at the same top speed
    /// at 5000 rpm), the torque low and wide instead of high and peaky, and more of it. Petrol and rocket fuel use the car's own defaults.
    /// </summary>
    public static readonly (string Name, float Value)[] DieselEngine =
    {
        ("Redline", 5000f), ("ShiftUp", 4600f), ("ShiftDown", 2200f), ("PeakRpm", 2600f), ("TorqueCurveFalloff", 0.82f), ("MaxTorque", 200f), ("Mass", 1050f), ("FinalDrive", 4.6f), ("FuelRevKnee", 3200f),
    };

    /// <summary>The SPUTNIK's own mass (kg), lighter than the other cars. Jet fuel and Sputnik keep the petrol car's torque: the 140 % power and the turbo are applied on top of it.</summary>
    public const float SputnikMass = 900f;
    /// <summary>The jet fuel's mass (kg): a little lighter than the Petrol's 1050 (no bumpers, no rack worth speaking of).</summary>
    public const float JetMass = 1000f;

    /// <summary>The engine's power as the physics gets it (percent): the diesel's character is its torque curve, so it has the whole of it.</summary>
    public static int PhysicsPercent(FuelGrade g) => g switch { FuelGrade.Diesel => 100, FuelGrade.Rocket or FuelGrade.Sputnik => 140, _ => Percent[(int)g] };

    /// <summary>
    /// The rocket fuel's engine in the rally: 40 % more power than petrol before the turbo comes in (PhysicsPercent 140) and 100 % more at full boost (2 times petrol):
    /// the turbo's gain on the 1.75.
    /// </summary>
    public const float RocketTurboGain = 2.0f / 1.4f - 1f;

    /// <summary>The turbo's gain for the car on this fuel (the rocket fuel's, otherwise none).</summary>
    public static float TurboGainFor(FuelGrade g) => g is FuelGrade.Rocket or FuelGrade.Sputnik ? RocketTurboGain : 0f;

    /// <summary>
    /// The gears of each fuel (the diesel four, the others five), found by a sweep of the first gear and the spacing against the 0-100, the 80-140 and the 400 m (PhysicsTest --gear-sweep, 8 November). The top gear and
    /// the redline stay as they were, so every fuel has the same top speed; only the gears between them differ. Petrol keeps the car's own gears (the sweep gained 1 % there).
    /// </summary>
    public static float[] GearsFor(FuelGrade g) => g switch
    {
        FuelGrade.Diesel => new[] { 4.60f, 2.63f, 1.64f, 1.18f },
        FuelGrade.Rocket or FuelGrade.Sputnik => new[] { 3.60f, 2.44f, 1.75f, 1.36f, 1.18f },
        _ => (float[])new CarParams().Gears.Clone(),
    };

    /// <summary>The car's engine for this fuel: the diesel's, or the car's own defaults for any other.</summary>
    public static void ApplyEngine(CarParams p, FuelGrade g)
    {
        var defaults = new CarParams();
        foreach (var (name, value) in DieselEngine)
        {
            var prop = typeof(CarParams).GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            prop.SetValue(p, g == FuelGrade.Diesel ? value : prop.GetValue(defaults));
        }
        if (g == FuelGrade.Sputnik) p.Mass = SputnikMass;
        else if (g == FuelGrade.Rocket) p.Mass = JetMass;
        p.Gears = GearsFor(g);
        p.DriveFront = DriveFrontFor(g);
        p.CenterLock = g == FuelGrade.Sputnik ? SputnikCenterLock : 0f;
        p.TurboSpool = g == FuelGrade.Sputnik ? SputnikTurboSpool : defaults.TurboSpool;
        p.TurboKick = g == FuelGrade.Sputnik ? SputnikTurboKick : 0f;
    }

    /// <summary>The same body equipment for solo, LAN and recorded cars; paint belongs to the caller.</summary>
    /// <summary>
    /// The tail pipes of each car, as x in the car's frame (the rear is +z, the pipes stand at y -0.40): the Diesel one pipe, the Petrol the twin pipes of the body (both on the right), the
    /// Jet fuel four (two on each side of the rear) and the Sputnik its one fat pipe (the kit's). The model's pipes, the smoke and the backfire flames all come from these.
    /// </summary>
    public static float[] PipeXs(FuelGrade g) => g switch
    {
        FuelGrade.Diesel => new[] { 0.40f },
        FuelGrade.Rocket => new[] { -0.47f, -0.33f, 0.33f, 0.47f },
        FuelGrade.Sputnik => new[] { 0.40f },
        _ => new[] { 0.33f, 0.47f },
    };

    /// <summary>
    /// The idle shudder of each engine's character (cosmetic, the body only): the strength of each beat and how fast the beats come. The Sputnik's five-cylinder is the reference (unequal, quick);
    /// the Diesel's four is slow and lumpy, the Petrol's and the Jet fuel's four even and fine.
    /// </summary>
    public static float[] IdleBeats(FuelGrade g) => g switch
    {
        FuelGrade.Sputnik => new[] { 1f, .45f, .85f, .35f, .65f },
        FuelGrade.Diesel => new[] { 1f, .55f, .8f, .55f },
        _ => new[] { .8f, .6f, .8f, .6f },
    };
    public static double IdleRate(FuelGrade g) => g switch { FuelGrade.Sputnik => 8.0, FuelGrade.Diesel => 6.5, _ => 9.0 };
    /// <summary>How strongly the car shudders at idle against the Sputnik's: the others have half of it.</summary>
    public static float IdleShudder(FuelGrade g) => g == FuelGrade.Sputnik ? 1f : 0.5f;

    public static void ApplyVisual(CarVisual visual, FuelGrade grade)
    {
        visual.SetIdleCharacter(IdleBeats(grade), IdleRate(grade));
        visual.Wing = grade switch { FuelGrade.Diesel => 3, FuelGrade.Rocket => 1, FuelGrade.Sputnik => 2, _ => 0 };
        visual.Equip(bumpers: grade is FuelGrade.Diesel or FuelGrade.Petrol, roofRack: grade != FuelGrade.Sputnik, kit: grade == FuelGrade.Sputnik,
            pipes: grade switch { FuelGrade.Diesel => CarDamage.PipeSet.Single, FuelGrade.Rocket => CarDamage.PipeSet.Quad, _ => CarDamage.PipeSet.Twin });      // (the Sputnik's kit hides them all and has its own pipe)
    }

    /// <summary>The SPUTNIK is a Group B Quattro: a viscous centre lock (up to 35 points of the engine move to the axle that grips), a turbo that spools in 0.4 s but holds its power back and then surges.</summary>
    public const float SputnikCenterLock = 0.35f, SputnikTurboSpool = 0.4f, SputnikTurboKick = 0.8f;

    /// <summary>The SPUTNIK drives all four wheels: 40 % of the engine to the front, 60 % to the rear.</summary>
    public const float SputnikDriveFront = 0.4f;
    public static float DriveFrontFor(FuelGrade g) => g == FuelGrade.Sputnik ? SputnikDriveFront : 0f;

    public static int PercentOf(FuelGrade g) => Percent[(int)g];

    /// <summary>OCTANE BOOSTER multiplies the fuel's power by 3.5 (the physics stops at 500%): 175, 350 and 500.</summary>
    public static int BoostPercent(FuelGrade g) => Math.Min(500, (int)MathF.Round(PercentOf(g) * 3.5f));

    public static FuelGrade Parse(string id) => Array.IndexOf(Ids, (id ?? "").ToLowerInvariant()) is var i and >= 0 ? (FuelGrade)i : FuelGrade.Petrol;

    /// <summary>The fuel last chosen on the card (settings.cfg [rally] fuel); petrol at first.</summary>
    public static FuelGrade Load(string dir)
    {
        var cfg = new ConfigFile();
        return cfg.Load(dir + "settings.cfg") == Error.Ok ? Parse((string)cfg.GetValue("rally", "fuel", "petrol")) : FuelGrade.Petrol;
    }

    public static void Save(string dir, FuelGrade g)
    {
        var cfg = new ConfigFile();
        cfg.Load(dir + "settings.cfg");      // keep the other sections
        cfg.SetValue("rally", "fuel", Ids[(int)g]);
        cfg.Save(dir + "settings.cfg");
    }
}
