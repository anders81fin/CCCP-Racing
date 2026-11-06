using System;

namespace CCCPRacing.Race;

/// <summary>
/// Stakhanov laps: the driver picks the engine power with keys 1-4 (diesel, petrol, jet fuel, SPUTNIK; LAN offers the same four through its ballot), 100 % at the start.
/// Every valid lap is ranked on the Stakhanov board at the highest power used during it.
/// </summary>
public static class Training
{
    public const int DefaultPercent = 100, HeroPercent = 200;

    /// <summary>Power in whole percent for keys 1..3 (legacy power validation; fuel selection uses LapsLevels).</summary>
    static readonly int[] Levels = { 50, 100, 200 };      // Legacy power IDs; Sputnik uses its distinct 201 ID in LapsLevels.
    public const int Count = 3;
    /// <summary>The Sputnik's power value on the Stakhanov laps: the jet fuel's 200 plus one, so that it stays its own fuel (board column, lap peak, engine) while it has the jet fuel's power.</summary>
    public const int SputnikPercent = 201;
    static readonly int[] LapsLevels = { 50, 100, 200, SputnikPercent };
    /// <summary>The fuels the Stakhanov laps offer (keys 1-4); the LAN ballot offers the same four.</summary>
    public const int LapsCount = 4;
    public static bool Valid(int percent) => Array.IndexOf(Levels, percent) >= 0;
    public static string FuelName(int percent) => Array.IndexOf(LapsLevels, percent) is var i and >= 0 ? FuelGrades.Names[i] : "POWER";

    /// <summary>Four letters for the Stakhanov board's power column: the fuel the lap was driven on (other powers, LAN, as a percent).</summary>
    public static string FuelShort(int percent) => Array.IndexOf(LapsLevels, percent) switch { 0 => "DIES", 1 => "PETR", 2 => "ROCK", 3 => "SPUT", _ => percent > 0 ? percent + "%" : "-" };

    /// <summary>The fuel of a Stakhanov power (50, 100, 200 %, the Sputnik's 201), null for any other.</summary>
    public static FuelGrade? GradeOf(int percent) => Array.IndexOf(LapsLevels, percent) is var i and >= 0 ? (FuelGrade)i : null;

    /// <summary>Power in whole percent for number key 1..4 (also used by the LAN ballot).</summary>
    public static int PercentForKey(int key) => LapsLevels[key - 1];

    /// <summary>CarSim.Overdrive for a power: 0 up to 200 %, 0.5 at 350 %, 1 at 500 % (linear).</summary>
    public static float Overdrive(int percent) => percent == SputnikPercent ? 0f : Math.Clamp((percent - 200) / 300f, 0f, 1f);

    /// <summary>The plan overfulfilled twice over (200 % and up): the Hero of Socialist Labour.</summary>
    public static bool Hero(int percent) => percent >= HeroPercent;

    /// <summary>The line under "POWER N%" when a key picks a power.</summary>
    public static string Joke(int percent) => percent switch
    {
        <= 50 => "SAVING FUEL FOR THE PEOPLE",
        <= 75 => "A MODEST CONTRIBUTION",
        <= 100 => "FACTORY SPECIFICATION",
        <= 125 => "THE NORM IS EXCEEDED",
        <= 150 => "THE FOREMAN NODS",
        <= 175 => "THE BRIGADE IS PROUD OF YOU",
        <= 200 => "HERO OF SOCIALIST LABOUR",
        <= 350 => "THE PLAN IS OVERFULFILLED",
        _ => "FIVE-YEAR PLAN IN ONE LAP",
    };
}
