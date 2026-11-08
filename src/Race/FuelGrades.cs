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
    /// <summary>The line on the FUEL card.</summary>
    public static readonly string[] Lines =
    {
        "A TRACTOR ENGINE. TORQUE AT THE BOTTOM, 5000 RPM", "100% POWER. FACTORY SPECIFICATION", "175% POWER, 250% ON TURBO. BAIKONUR SURPLUS", "175% POWER, 250% ON TURBO, FOUR-WHEEL DRIVE",
    };

    /// <summary>The line of the FUEL card before the norms: the power of petrol and rocket fuel; the diesel is an engine of its own.</summary>
    public static string CardLine(int grade) => grade == (int)FuelGrade.Diesel ? "200 NM, 1050 KG, 5000 RPM, ECONOMICAL" : grade == (int)FuelGrade.Rocket ? "175% POWER, 250% ON TURBO" : grade == (int)FuelGrade.Sputnik ? "TOP SECRET: AWD PROTOTYPE" : "THE STANDARD";

    /// <summary>
    /// The diesel's engine, a diesel and not a petrol engine at half the power (tried out in LAB, 2 November): a lower redline with four close gears (a lower top speed, 8 November: the hills before the speed; was five taller gears at the same top speed
    /// at 5000 rpm), the torque low and wide instead of high and peaky, and more of it. Petrol and rocket fuel use the car's own defaults.
    /// </summary>
    public static readonly (string Name, float Value)[] DieselEngine =
    {
        ("Redline", 5000f), ("ShiftUp", 4600f), ("ShiftDown", 2200f), ("PeakRpm", 2600f), ("TorqueCurveFalloff", 0.82f), ("MaxTorque", 200f), ("Mass", 1050f), ("FinalDrive", 4.6f), ("FuelRevKnee", 3200f),
    };

    /// <summary>The SPUTNIK's own mass (kg), lighter than the other cars. Jet fuel and Sputnik keep the petrol car's torque: the 175 % power and the turbo are applied on top of it.</summary>
    public const float SputnikMass = 900f;

    /// <summary>The engine's power as the physics gets it (percent): the diesel's character is its torque curve, so it has the whole of it.</summary>
    public static int PhysicsPercent(FuelGrade g) => g switch { FuelGrade.Diesel => 100, FuelGrade.Rocket or FuelGrade.Sputnik => 175, _ => Percent[(int)g] };

    /// <summary>
    /// The rocket fuel's engine in the rally: 75 % more power than petrol before the turbo comes in (PhysicsPercent 175) and 150 % more at full boost (2.5 times petrol):
    /// the turbo's gain on the 1.75.
    /// </summary>
    public const float RocketTurboGain = 2.5f / 1.75f - 1f;

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
        p.Gears = GearsFor(g);
        p.DriveFront = DriveFrontFor(g);
        p.CenterLock = g == FuelGrade.Sputnik ? SputnikCenterLock : 0f;
        p.TurboSpool = g == FuelGrade.Sputnik ? SputnikTurboSpool : defaults.TurboSpool;
        p.TurboKick = g == FuelGrade.Sputnik ? SputnikTurboKick : 0f;
    }

    /// <summary>The same body equipment for solo, LAN and recorded cars; paint belongs to the caller.</summary>
    public static void ApplyVisual(CarVisual visual, FuelGrade grade)
    {
        visual.Wing = grade switch { FuelGrade.Diesel => 3, FuelGrade.Rocket => 1, FuelGrade.Sputnik => 2, _ => 0 };
        visual.Equip(bumpers: grade is FuelGrade.Diesel or FuelGrade.Petrol, roofRack: grade != FuelGrade.Sputnik, kit: grade == FuelGrade.Sputnik);
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
