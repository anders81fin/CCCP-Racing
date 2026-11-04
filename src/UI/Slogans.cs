using System;

namespace CCCPRacing.UI;

/// <summary>The Ministry of Motorsport's approved vocabulary.</summary>
public static class Slogans
{
    static readonly Random Rng = new();

    public static readonly string[] Lap =
    {
        "GLORIOUS LAP, TOVARISH!",
        "THE PARTY IS PLEASED",
        "NORM FULFILLED 112%",
        "LAP DELIVERED ON SCHEDULE",
        "THE KOLKHOZ APPLAUDS",
    };

    public static readonly string[] Record =
    {
        "HERO OF SOCIALIST LABOUR!",
        "NEW FIVE-YEAR RECORD!",
        "ORDER OF LENIN FOR YOU!",
    };

    public static readonly string[] Grass =
    {
        "KOLKHOZ FIELD IS PEOPLE'S PROPERTY!",
        "THE HARVEST, COMRADE!",
        "AGRONOMIST IS WATCHING",
        "THIS IS NOT A TRACTOR",
    };

    public static readonly string[] Snow =
    {
        "THE SNOW IS PEOPLE'S PROPERTY TOO!",
        "GENERAL WINTER IS WATCHING",
        "THIS IS NOT A SNOWPLOUGH",
        "SIBERIA HAS ROOM FOR EVERYONE",
    };

    public static readonly string[] Fence =
    {
        "KOLKHOZ FENCE DESTROYED!",
        "SABOTAGE OF COLLECTIVE PROPERTY!",
        "THE COWS ARE FREE NOW",
        "FIREWOOD FOR WINTER, THANK YOU",
    };

    public static readonly string[] Air =
    {
        "SUSPENSION: NYET!",
        "COSMONAUT CANDIDATE!",
        "THE COSMONAUT IS PROUD",
    };

    public static readonly string[] Crash =
    {
        "PEOPLE'S PROPERTY DAMAGED!",
        "REPORT TO THE CHAIRMAN",
    };

    /// <summary>Only for crashes that really were a birch.</summary>
    public static readonly string[] BirchCrash =
    {
        "THE BIRCH WON",
        "THE BIRCH IS UNDAMAGED",
    };

    /// <summary>The car knocked a birch down.</summary>
    public static readonly string[] BirchDown =
    {
        "THE BIRCH LOST, FOR ONCE",
        "TIMBER!",
    };

    public static readonly string[] TreeDown =
    {
        "TIMBER!",
        "FIREWOOD PLAN FULFILLED",
    };

    public static readonly string[] PoleDown =
    {
        "COMMUNISM MINUS ELECTRIFICATION",
        "THE VILLAGE IS IN THE DARK",
    };

    public static readonly string[] WallDown =
    {
        "THAT WALL SURVIVED THE TSAR",
        "NOW IT IS A PILE OF STONES",
    };

    public static readonly string[] Slow =
    {
        "BREAD QUEUE MOVES FASTER",
        "IS THIS THE 5-YEAR PLAN?",
    };

    public static readonly string[] Flip =
    {
        "ON YOUR ROOF, TOVARISH!",
        "WHEELS UP! NOT IN THE PLAN",
        "THE SKY IS BELOW YOU, TOVARISH",
    };

    public const string Shortcut = "COMRADE, THIS IS NOT IN THE 5-YEAR PLAN";

    public static string Pick(string[] list) => list[Rng.Next(list.Length)];
}
