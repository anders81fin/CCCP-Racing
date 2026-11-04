using System;
using Godot;

namespace CCCPRacing.Race;

/// <summary>What the parked car's driver does with the throttle during a shot (the engine revs freely: neutral, clutch down, handbrake on).</summary>
enum TourRev
{
    /// <summary>The engine ticks over.</summary>
    Idle,
    /// <summary>A blip or two.</summary>
    Blip,
    /// <summary>The throttle rises to the floor, holds, and is lifted a second before the shot ends.</summary>
    Sweep,
    /// <summary>Flat out for the first part of the shot, then off at once: the lift-off crackle, the turbo's blow-off.</summary>
    Lift,
}

/// <summary>
/// One shot of a car's tour: the lens moves from Eye0 to Eye1 while it looks from Look0 to Look1 (all in the car's own frame: x right, y up, z back, the nose at -2),
/// Dur seconds, with the caption and what the throttle does.
/// </summary>
sealed record CarShot(float Dur, Vector3 Eye0, Vector3 Eye1, Vector3 Look0, Vector3 Look1, float Fov, string Caption, TourRev Rev = TourRev.Idle, bool Specs = false);

/// <summary>
/// The tour of one fuel's car (CAR TOUR, T on the FUEL card): a black card, then shots of the car with a caption each, the engine
/// shown, and a card of its data at the end. A script is just this list; the tour itself is RaceScene.CarTour.cs. Captions keep to the pixel font
/// (letters, digits and . , : ! ? - + = / % " ( ) < > # *, no apostrophes).
/// </summary>
sealed class CarTourScript
{
    public string[] CardBig, CardSmall;
    public CarShot[] Shots;

    public float Duration
    {
        get { float t = 0; foreach (var s in Shots) t += s.Dur; return t; }
    }

    // The car's places, in its own frame.
    static readonly Vector3 Nose = new(0, -0.05f, -2.05f), Bonnet = new(0, 0.30f, -1.2f), Roof = new(0, 0.95f, -0.1f), Wing = new(0, 0.55f, 1.85f),
        FrontWheel = new(0.92f, -0.30f, -1.18f), RearWheel = new(0.92f, -0.30f, 1.13f), Side = new(0.9f, 0.0f, 0f), Exhaust = new(0.35f, -0.35f, 2.1f),
        Lamps = new(0, 0.40f, -1.9f), Middle = new(0, 0.1f, 0);

    static CarShot S(float dur, Vector3 eye0, Vector3 eye1, Vector3 look0, Vector3 look1, float fov, string caption, TourRev rev = TourRev.Idle, bool specs = false)
        => new(dur, eye0, eye1, look0, look1, fov, caption, rev, specs);

    /// <summary>A wide shot circling the car at a given distance and height, from one angle (degrees, 0 = the nose, 90 = the right side) to another.</summary>
    static CarShot Orbit(float dur, float deg0, float deg1, float dist, float height, string caption, float fov = 50f, TourRev rev = TourRev.Idle, bool specs = false)
    {
        Vector3 At(float deg) => new(MathF.Sin(Mathf.DegToRad(deg)) * dist, height, -MathF.Cos(Mathf.DegToRad(deg)) * dist);
        return S(dur, At(deg0), At(deg1), Middle, Middle, fov, caption, rev, specs);
    }

    public static CarTourScript For(FuelGrade g) => g switch
    {
        FuelGrade.Diesel => Diesel(),
        FuelGrade.Rocket => JetFuel(),
        FuelGrade.Sputnik => Sputnik(),
        _ => Petrol(),
    };

    static CarTourScript Diesel() => new()
    {
        CardBig = new[] { "THE DIESEL" },
        CardSmall = new[] { "A CAR FOR PEOPLE WHO ARE NOT IN A HURRY.", "THEY WILL ARRIVE. EVENTUALLY. THE SMOKE WILL ARRIVE FIRST." },
        Shots = new[]
        {
            Orbit(6, 40, 130, 7.5f, 1.4f, "THIS IS THE DIESEL. ARMY GREEN, LIKE THE UAZ OF THE DUTY OFFICER."),
            S(5, new Vector3(1.6f, 0.9f, -4.2f), new Vector3(-1.2f, 0.7f, -3.2f), Bonnet, Nose, 48, "THE BUMPERS ARE CHROME. THE MANNERS ARE NOT."),
            S(6, new Vector3(1.5f, -0.1f, 4.8f), new Vector3(0.9f, -0.2f, 3.2f), Exhaust, Exhaust, 42, "TWO PIPES. BLACK SMOKE ALL THE TIME. THE ENGINEER CALLS IT PROGRESS.", TourRev.Idle),
            S(6, new Vector3(2.6f, 0.9f, 4.6f), new Vector3(-2.0f, 1.0f, 4.2f), new Vector3(0, 0.4f, 1.7f), new Vector3(0, 0.4f, 1.7f), 45, "NO WING. A LITTLE LIP ON THE BOOT FOR AERODYNAMICS. OR FOR A SANDWICH."),
            S(7, new Vector3(2.4f, 0.1f, 3.6f), new Vector3(1.4f, 0.0f, 2.9f), Exhaust, Exhaust, 42, "400 NM LOW DOWN. IT DOES NOT REV. IT PUSHES. PRESS THE PEDAL.", TourRev.Sweep),
            S(5, new Vector3(2.6f, 0.2f, 4.2f), new Vector3(1.6f, 0.0f, 3.4f), Exhaust, Exhaust, 42, "AND LIFT. A KNOCK, A GRUNT, A FRESH CLOUD.", TourRev.Lift),
            Orbit(9, 130, 230, 7.0f, 1.6f, "VERDICT: SLOW, HONEST, FILTHY. THE POLITBURO IS PLEASED.", 52, TourRev.Blip, specs: true),
        },
    };

    static CarTourScript Petrol() => new()
    {
        CardBig = new[] { "THE PETROL" },
        CardSmall = new[] { "THE FACTORY RALLY CAR FROM TOLYATTI.", "NOT FAST. NOT SLOW. ABOVE ALL: AVAILABLE." },
        Shots = new[]
        {
            Orbit(6, 30, 120, 7.5f, 1.4f, "THIS IS THE PETROL. THE ONE EVERYBODY KNOWS. THE ONE THAT STARTS."),
            S(5, new Vector3(3.6f, 0.2f, -2.0f), new Vector3(3.0f, 0.1f, -0.6f), FrontWheel, FrontWheel, 40, "STEEL WHEELS WITH VENTS, DRILLED BY HAND. YOU CAN SEE WHICH HAND."),
            S(5, new Vector3(2.4f, 1.3f, 3.9f), new Vector3(-2.4f, 1.3f, 3.9f), Wing, Wing, 45, "A LOW WING. IT DOES NOTHING. IT LOOKS LIKE IT DOES NOTHING FAST."),
            S(6, new Vector3(0.0f, 2.6f, -0.9f), new Vector3(0.7f, 2.4f, 0.4f), Roof, new Vector3(0, 1.0f, 0.2f), 50, "ROOF RACK AND A SPARE WHEEL. IN CASE THE ROAD NEVER ARRIVES."),
            S(7, new Vector3(2.4f, 0.1f, 3.6f), new Vector3(1.4f, 0.0f, 2.9f), Exhaust, Exhaust, 42, "100% OF THE FACTORY POWER. A REAL ENGINE. LISTEN TO IT SING.", TourRev.Sweep),
            S(5, new Vector3(2.6f, 0.2f, 4.2f), new Vector3(1.6f, 0.0f, 3.4f), Exhaust, Exhaust, 42, "LIFT, AND IT POPS. THE FACTORY SAYS THAT IS NORMAL.", TourRev.Lift),
            Orbit(9, 120, 220, 7.0f, 1.6f, "VERDICT: THE REFERENCE. EVERYTHING ELSE IS AN OPINION.", 52, TourRev.Blip, specs: true),
        },
    };

    static CarTourScript JetFuel() => new()
    {
        CardBig = new[] { "THE JET FUEL" },
        CardSmall = new[] { "TAKE THE FACTORY CAR. TAKE A TURBO FROM THE AIRBASE.", "DO NOT TELL THE AIRBASE." },
        Shots = new[]
        {
            Orbit(6, 35, 125, 7.5f, 1.4f, "THIS IS THE JET FUEL. RED, LIKE A FLAG. LIGHT, LIKE A PROMISE."),
            S(5, new Vector3(1.8f, 0.2f, -4.6f), new Vector3(-1.6f, 0.1f, -3.6f), Nose, Nose, 46, "NO BUMPERS. THEY WERE REMOVED FOR WEIGHT, BY HAND, ONE EVENING."),
            S(5, new Vector3(0.0f, 2.8f, -1.0f), new Vector3(0.0f, 2.6f, 0.7f), Roof, Roof, 50, "THE RACK AND THE SPARE STAY. THE DRIVER IS AN OPTIMIST."),
            S(5, new Vector3(2.5f, 1.1f, 4.0f), new Vector3(-2.5f, 1.1f, 4.0f), Wing, Wing, 45, "A MIDDLE-SIZED WING FOR A MIDDLE-SIZED DISASTER."),
            S(7, new Vector3(2.4f, 0.1f, 3.6f), new Vector3(1.4f, 0.0f, 2.9f), Exhaust, Exhaust, 42, "175% POWER, 250% ON TURBO. HEAR THE WHISTLE? THAT IS THE AIRBASE CALLING.", TourRev.Sweep),
            S(6, new Vector3(2.6f, 0.2f, 4.2f), new Vector3(1.6f, 0.0f, 3.4f), Exhaust, Exhaust, 42, "LIFT OFF. THE BLOW-OFF, THEN THE BANGS. NEARLY EXPLOSIONS.", TourRev.Lift),
            Orbit(9, 125, 225, 7.0f, 1.6f, "VERDICT: FAST IN A STRAIGHT LINE. THE CORNERS ARE A MATTER OF FAITH.", 52, TourRev.Blip, specs: true),
        },
    };

    static CarTourScript Sputnik() => new()
    {
        CardBig = new[] { "THE SPUTNIK" },
        CardSmall = new[] { "BUILT IN A SHED BY MEN WHO HAD NEVER HEARD OF A SPEED LIMIT.", "THE FASTEST, WILDEST CAR IN THE KOLKHOZ." },
        Shots = new[]
        {
            Orbit(6, 35, 125, 7.5f, 1.4f, "THIS IS THE SPUTNIK. IT IS WHITE, BECAUSE THE SNOW WILL NOT NOTICE IT COMING."),
            S(5, new Vector3(1.6f, 0.9f, -4.3f), new Vector3(-1.4f, 0.8f, -3.3f), Lamps, Bonnet, 48, "FOUR LAMPS, LOUVRES IN THE BONNET, NO BUMPERS. WEIGHT IS THE ENEMY."),
            S(5, new Vector3(3.4f, 0.0f, -2.3f), new Vector3(3.0f, -0.1f, -0.5f), FrontWheel, FrontWheel, 40, "BRAKE COOLING SLATS ON EVERY WHEEL. THE BRAKES WILL NEED THEM."),
            S(5, new Vector3(3.8f, 0.0f, 1.8f), new Vector3(3.8f, 0.1f, -1.8f), Side, Side, 42, "SKIRTS. FOUR-WHEEL DRIVE: 40% FRONT, 60% REAR, 100% CCCP."),
            S(5, new Vector3(2.4f, 1.2f, 4.0f), new Vector3(-2.4f, 1.2f, 4.0f), Wing, Wing, 45, "THE WING IS HIGH AND BLACK. THE STAR IS RED. THE SPONSOR IS THE STATE."),
            S(4, new Vector3(1.8f, 2.6f, -1.4f), new Vector3(-1.0f, 2.2f, 0.9f), new Vector3(0, 1.1f, -0.1f), new Vector3(0, 1.1f, -0.1f), 50, "NO ROOF RACK, NO SPARE. A SCOOP INSTEAD. IF YOU PUNCTURE, YOU WALK."),
            S(6, new Vector3(-1.9f, 0.5f, 4.4f), new Vector3(1.4f, 0.4f, 3.6f), new Vector3(0, -0.3f, 2.1f), new Vector3(0.2f, -0.3f, 2.1f), 44, "FIVE CYLINDERS: AN ODD NUMBER, SO AN ODD EXHAUST. ONE FAT PIPE, FIVE SLITS, A SMALL 5.", TourRev.Blip),
            S(7, new Vector3(2.4f, 0.1f, 3.6f), new Vector3(1.4f, 0.0f, 2.9f), Exhaust, Exhaust, 42, "FIVE CYLINDERS, A TURBO, NO PLAN. LISTEN TO THAT WARBLE.", TourRev.Sweep),
            S(6, new Vector3(2.6f, 0.2f, 4.2f), new Vector3(1.6f, 0.0f, 3.4f), Exhaust, Exhaust, 42, "LIFT OFF: THE WASTEGATE SIGHS, THEN THE PIPE CRACKLES IN FIVES. ANTI-LAG, OR A SMALL WAR.", TourRev.Lift),
            Orbit(9, 125, 235, 7.0f, 1.6f, "VERDICT: NOT RECOMMENDED. BUY IT IMMEDIATELY.", 52, TourRev.Blip, specs: true),
        },
    };
}
