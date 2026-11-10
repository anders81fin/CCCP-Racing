using System;
using System.Collections.Generic;
using CCCPRacing.Car;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Race;

/// <summary>The rally schools for Ilyich, Siberia and Atomgrad, and the title's car-control course on Ilyich.
/// Stakhanov laps have none.</summary>
public enum SchoolCourse { Rally, Driving }

/// <summary>A control drawn on the school's cards: keycaps after key input, the pad's buttons after pad input.</summary>
public enum SchoolKey { Drive, Gas, Brake, Steer, Handbrake, Clutch, Horn, Rescue }

/// <summary>What a try's success check says this tick.</summary>
public enum Verdict { Pending, Success, Fail }

/// <summary>
/// One step of a lesson: SHOW (a demo, RaceScene.SchoolSteps.cs), TELL (at most two short lines, the
/// pad's wording where it names a key, and the controls pictured), and TRY (Hint on screen while the
/// player drives it) when it has one.
/// </summary>
public record SchoolStep(string Id, string[] Tell, SchoolKey[] Keys, string Hint = null, string[] PadTell = null, string PadHint = null)
{
    public bool Try => Hint != null;
}

/// <summary>
/// The driving school's content: the lessons of each course, their texts (English, the pixel
/// font's charset, no apostrophes) and the success checks of the tries. The checks are plain
/// classes fed a tick at a time, so the headless tests can drive them with made-up data.
/// </summary>
public static class SchoolScript
{
    public const int MaxTries = 3;
    public const float TryTimeout = 20f;

    public static readonly string[] IlyichChapters = { "TRACK", "DAMAGE", "GAI RADAR" };
    public static readonly string[] SiberiaChapters = { "TRACK", "WINTER ROAD", "FROZEN RIVER", "FUEL", "REPAIRS", "GRANDMA" };
    public static readonly string[] AtomgradChapters = { "TRACK", "RADIATION", "FUEL", "REPAIRS" };
    public static readonly string[] DrivingChapters = { "CLUTCH START", "STEERING", "BRAKING", "DRIFTING", "RESCUE" };   // the YOU ARE READY recap closes RESCUE (only the driving school has one)


    static readonly SchoolKey[] None = Array.Empty<SchoolKey>();

    /// <summary>
    /// ILYICH'S LAMP rally school: a lap with driving tips (the first bend, the rollers and the jump,
    /// the stream, the long bends, the church hairpin, the hill to the barn; measured with
    /// ILYICHLAB=rollers / ford),
    /// a separate GAI radar and patrol demonstration, and the damage (a
    /// head-on with the bus, the horn for the mechanic with a smoking engine). Car control is the
    /// DRIVING SCHOOL's (Driving).
    /// </summary>
    public static readonly (int chapter, SchoolStep step)[] IlyichEnvironment =
    {
        (0, new("kolkhoz_farm", new[] { "THE KOLKHOZ: GLORY TO LABOUR!", "EVEN THE FINISH LINE HAS A ROOF." }, None)),
        (0, new("kolkhoz_dacha", new[] { "A JUNIOR PARTNER VISITS.", "DO NOT DISTURB THE DUCK HUNT." }, None)),
        (0, new("kolkhoz_fields", new[] { "EVERYONE WORKS FOR THE HARVEST.", "SOME CONTRIBUTE FROM A BOTTLE." }, None)),
        (0, new("kolkhoz_village", new[] { "A GRAIN STORE WITH A BELL TOWER.", "THE BUS WILL COME. EVENTUALLY." }, None)),
        (0, new("kolkhoz_daynight", new[] { "DAY, SUNSET, DUSK, NIGHT, DAWN.", "THE LIGHT CHANGES AS YOU LAP." }, None)),
    };

    /// <summary>The first track's tour on CHOOSE TRACK: one drone flight and lap (RaceScene.DroneTour.cs); its captions are drawn there.</summary>
    public static readonly (int chapter, SchoolStep step)[] IlyichDrone =
    {
        (0, new("kolkhoz_drone", System.Array.Empty<string>(), None)),
    };

    /// <summary>SIBERIA's tour once its drone film has been baked (DroneFilm.Siberia).</summary>
    public static readonly (int chapter, SchoolStep step)[] SiberiaDrone =
    {
        (0, new("siberia_drone", System.Array.Empty<string>(), None)),
    };

    /// <summary>ATOMGRAD-4's tour once its drone film has been baked (DroneFilm.Atomgrad).</summary>
    public static readonly (int chapter, SchoolStep step)[] AtomgradDrone =
    {
        (0, new("atomgrad_drone", System.Array.Empty<string>(), None)),
    };

    /// <summary>POLIGON 17: the field camp, live exercise and driving lap.</summary>
    static readonly string[] BonusDroneChapters = { "POLIGON 17" };
    public static readonly (int chapter, SchoolStep step)[] BonusDrone =
    {
        (0, new("bonus_drone", System.Array.Empty<string>(), None)),
    };

    public static (int chapter, SchoolStep step)[] DroneFor(string track) => track switch
    {
        "ilyich" => IlyichDrone, "siberia" => SiberiaDrone,
        "atomgrad" => AtomgradDrone, "bonus" => BonusDrone,
        _ => System.Array.Empty<(int, SchoolStep)>(),
    };

    public static readonly (int chapter, SchoolStep step)[] Ilyich =
    {
        (0, new("tour_first_bend", new[] { "THE FIRST BEND TURNS RIGHT.", "THEN A FAST RUN TO THE JUMP." }, None)),
        (0, new("tour_jump", new[] { "THE JUMP: LINE UP EARLY.", "LAND DOWNHILL. BRAKE FOR STREAM." }, None)),
        (0, new("tour_ford", new[] { "STREAM: KEEP LEFT ON THE BAR.", "THE DEEP INSIDE LINE IS SLOW." }, None)),
        (0, new("tour_bends", new[] { "LONG BENDS: A CLUTCH KICK,", "THEN HOLD THE SLIDE ON THE GAS." }, new[] { SchoolKey.Clutch, SchoolKey.Gas })),
        (0, new("tour_hairpin", new[] { "THE GRAIN STORE HAIRPIN:", "STEER AND PULL THE HANDBRAKE." }, new[] { SchoolKey.Steer, SchoolKey.Handbrake })),
        (0, new("tour_finish", new[] { "UP THE HILL: A TIGHT LINE.", "RUN WIDE AND YOU MISS THE BARN!" }, None)),
        (1, new("crash", new[] { "CRASHES HURT THE ENGINE.", "AT 0 %: GAME OVER. THE GULAG!" }, None)),
        (1, new("headlights", new[] { "BROKEN LAMPS MEAN LESS LIGHT.", "THE MECHANIC FIXES THEM TOO." }, None)),
        (1, new("mechanic", new[] { "THE MECHANIC SLEEPS AT THE STOP.", "HONK HIM AWAKE. PARK: HE FIXES." }, new[] { SchoolKey.Horn }, "HONK NEAR THE SLEEPING MAN")),
        (2, new("tour_gai", new[] { "THE GAI RADAR. LIMIT: 90 KM/H.", "A PATROL MAY BE WAITING AHEAD." }, None)),
        (2, new("tour_officer", new[] { "THE MILITIAMAN READS YOUR SPEED.", "LOOK FOR HIS SPEECH BUBBLE." }, None)),
        (2, new("gai_ambush", new[] { "THE DUTY OFFICER MAY WAIT HERE.", "HE ALSO PATROLS THE TRACK." }, None)),
        (2, new("gai_chase", new[] { "IF HE IS WAITING: OVER 90?", "HE CHASES YOU WITH SIRENS ON." }, None)),
    };


    /// <summary>
    /// The DRIVING SCHOOL (the title's SINGLE PLAYER row) on ILYICH'S LAMP: the clutch start on the
    /// straight, steering up, across and down the screen (left and right are the car's), locked brakes
    /// against pumping in the S-bends, the clutch kick and the Scandinavian flick (both in the left-hander at 500 m),
    /// the handbrake in the church hairpin, R shown in a hollow by the road, the recap.
    /// </summary>
    public static readonly (int chapter, SchoolStep step)[] Driving =
    {
        (0, new("clutch", new[] { "CLUTCH IN. REV TO 5000 AND HOLD.", "LET GO, GAS FLAT: A ROCKET START!" }, new[] { SchoolKey.Clutch, SchoolKey.Gas }, "CLUTCH IN, HOLD 5000 RPM, LET GO")),
        (1, new("steer", new[] { "STEER AS IF YOU SIT IN THE CAR.", "COMING DOWN, RIGHT GOES LEFT!" }, new[] { SchoolKey.Steer }, "DRIVE DOWN: STEER AS THE DRIVER")),
        (2, new("brake", new[] { "LOCKED WHEELS GO STRAIGHT ON.", "TAP THE BRAKE: THE CAR TURNS!" }, new[] { SchoolKey.Brake, SchoolKey.Steer }, "TAP THE BRAKE. TAP. TAP.")),
        (3, new("kick", new[] { "IN THE BEND: CLUTCH IN AND OUT!", "THE TAIL SLIDES. KEEP GAS ON." }, new[] { SchoolKey.Clutch, SchoolKey.Gas, SchoolKey.Steer }, "IN THE BEND: A CLUTCH KICK!")),
        (3, new("flick", new[] { "STEER AWAY, LIFT OFF, STEER IN:", "THE SCANDINAVIAN FLICK!" }, new[] { SchoolKey.Steer, SchoolKey.Gas }, "AWAY, LIFT OFF, IN: FLICK IT!")),
        (3, new("hairpin", new[] { "GRAIN STORE HAIRPIN: STEER AND", "PULL THE HANDBRAKE. IT SWINGS!" }, new[] { SchoolKey.Steer, SchoolKey.Handbrake }, "HAIRPIN: STEER + HANDBRAKE")),
        (4, new("rescue", new[] { "STUCK? PRESS R.", "THE LAP IS LOST." }, new[] { SchoolKey.Rescue }, null,
            new[] { "STUCK? PRESS B.", "THE LAP IS LOST." })),      // shown only: no try
        (4, new("recap", new[] { "YOU ARE READY, TOVARISH!" }, None)),
    };

    /// <summary>Siberia teaches its own hazards, at their actual places on the winter road.</summary>
    public static readonly (int chapter, SchoolStep step)[] SiberiaEnvironment =
    {
        (0, new("siberia_valley", new[] { "WELCOME TO SIBERIA.", "SOME FRESH AIR FROM THE PARTY." }, None)),
        (0, new("siberia_forestry", new[] { "THE PLAN NEEDS MORE TIMBER.", "THE FOREST WAS NOT CONSULTED." }, None)),
        (0, new("siberia_radar", new[] { "THE RADAR WATCHES THE SKY.", "THE ROAD UP IS YOUR PROBLEM." }, None)),
        (0, new("siberia_base", new[] { "THE AIR FORCE IS READY.", "FULL AFTERBURNER. FOR THE PARTY." }, None)),
        (0, new("siberia_wreck", new[] { "THE TIMBER DELIVERY IS DELAYED.", "THE PAPERWORK BLAMES THE DRIVER." }, None)),
        (0, new("siberia_camp", new[] { "LABOUR IS A MATTER OF HONOUR.", "LEAVING REQUIRES AUTHORISATION." }, None)),
        (0, new("siberia_night", new[] { "THE LIGHT CHANGES AS YOU LAP.", "THE WATCH WILL CONTINUE." }, None)),
    };

    public static readonly (int chapter, SchoolStep step)[] Siberia =
    {
        (0, new("snow_tour_start", new[] { "THE VALLEY: ICE ON THE STRAIGHT.", "A FAST LEFT UP TO THE HILLS." }, None)),
        (0, new("snow_tour_hills", new[] { "SWITCHBACKS CLIMB THE EAST HILL.", "THE RADAR STANDS ABOVE THEM." }, None)),
        (0, new("snow_tour_base", new[] { "WEST ALONG THE AIRBASE RUNWAY.", "FUEL ON THE RIGHT. GAI: 90." }, None)),
        (0, new("snow_tour_river", new[] { "DOWN THE WEST SLOPE TO THE ICE.", "BRAKE EARLY FOR THE EXIT BEND." }, new[] { SchoolKey.Brake })),
        (0, new("snow_tour_camp", new[] { "THE ROAD GOES THROUGH THE CAMP.", "FIRST RIGHT BARRACK: MECHANIC." }, None)),
        (0, new("snow_tour_home", new[] { "LEFT OUT OF THE CAMP, THEN HOME.", "A FULL LAP OF SIBERIA!" }, None)),
        (1, new("snow_bank", new[] { "BRUSH THE BANK: IT TURNS YOU IN.", "HIT IT SQUARE AND YOU STOP." }, new[] { SchoolKey.Steer })),
        (1, new("snow_hairpin", new[] { "SWITCHBACKS: STEER + HANDBRAKE.", "A SHORT PULL, THEN GAS ON." }, new[] { SchoolKey.Brake, SchoolKey.Steer, SchoolKey.Handbrake, SchoolKey.Gas })),
        (1, new("snow_plough", new[] { "PLOUGHS RUN BOTH WAYS.", "SLOW DOWN. GIVE THE BLADE ROOM." }, new[] { SchoolKey.Brake, SchoolKey.Steer })),
        (1, new("snow_runway", new[] { "THE RUNWAY HAS MORE GRIP.", "GAI AT THE FAR END: LIMIT 90." }, None)),
        (2, new("snow_ice", new[] { "ICE: BRAKE EARLY, STEER GENTLY.", "LOCKED WHEELS SLIDE STRAIGHT." }, new[] { SchoolKey.Brake, SchoolKey.Steer })),
        (2, new("snow_exit", new[] { "ICE EXIT: A HARD-TO-READ BEND.", "BRAKE EARLY. DO NOT RUN WIDE!" }, new[] { SchoolKey.Brake, SchoolKey.Steer })),
        (3, new("snow_fuel", new[] { "IN SIBERIA, THE CAR USES FUEL.", "HARD GAS AND HIGH REVS USE MORE." }, new[] { SchoolKey.Gas })),
        (3, new("snow_tank", new[] { "THE FUEL TANK HOLDS 50 LITRES.", "A LIGHT FOOT SAVES FUEL." }, None)),
        (3, new("snow_low", new[] { "FUEL LOW: THE GAUGE BLINKS.", "NEAR EMPTY, THE ENGINE SPUTTERS." }, None)),
        (3, new("snow_empty", new[] { "EMPTY: ENGINE DIES. COAST ON.", "A CAN OR THE DEPOT CAN SAVE YOU." }, new[] { SchoolKey.Steer })),
        (3, new("snow_tow", new[] { "STOPPED: TOWED TO THE DEPOT.", "THE LAP IS VOID. -1 LAP OF NORM." }, None)),
        (3, new("snow_last_try", new[] { "NO LAPS LEFT IN THE NORM?", "THE PLAN FAILS. THE GULAG!" }, None)),
        (3, new("snow_refuel", new[] { "TWO WAYS TO REFUEL ON A LAP:", "BABUSHKA CANS OR A DEPOT STOP." }, None)),
        (3, new("can", new[] { "BABUSHKAS HAVE JERRYCANS.", "DRIVE PAST HER, IN HER LANE." }, new[] { SchoolKey.Steer })),
        (3, new("depot", new[] { "FUEL: RUNWAY END, ON THE RIGHT.", "STOP BY THE TANK TO FILL UP." }, new[] { SchoolKey.Brake })),
        (3, new("golden", new[] { "OCTANE BOOSTER: GOLD CAN, 3.5X POWER.", "THE BOOST LASTS 20 SECONDS." }, None)),
        (4, new("snow_mechanic", new[] { "CAMP: FIRST BARRACK, RIGHT SIDE.", "HONK. PARK NEAR HIM FOR REPAIRS." }, new[] { SchoolKey.Horn, SchoolKey.Brake })),
        (5, new("snow_grandma", new[] { "ONE GRANNY HAS A PILOT GRANDSON.", "SHE LOOKS LIKE ANY OTHER GRANNY." }, new[] { SchoolKey.Horn, SchoolKey.Brake })),
        (5, new("snow_revenge", new[] { "HIT HER: HER GRANDSON TAKES OFF.", "HIS JET HUNTS YOU WITH MISSILES." }, None)),
    };

    /// <summary>Atomgrad-4: the track, the Zone's dose and decontamination, then fuel and the mechanic.</summary>
    public static readonly (int chapter, SchoolStep step)[] Atomgrad =
    {
        (0, new("atom_tour_forest", new[] { "THE RED FOREST: THE ZONE IS HOT.", "KEEP TO THE ROAD AND WATCH THE METER." }, None)),
        (0, new("atom_tour_plant", new[] { "THE POWER PLANT AND THE VEHICLE GRAVEYARD.", "THE PAPERWORK SAYS: DO NOT TOUCH." }, None)),
        (0, new("atom_tour_channel", new[] { "THE COOLING CHANNEL: WET CONCRETE.", "A LONG STRAIGHT, THEN BRAKE FOR TOWN." }, None)),
        (0, new("atom_tour_town", new[] { "ATOMGRAD-4: TIGHT CITY CORNERS.", "THE SQUARE AND THE FERRIS WHEEL." }, None)),
        (0, new("atom_tour_wash", new[] { "THE BIRCH AVENUE LEADS TO THE CHECKPOINT.", "THE WASH STATION IS JUST BEFORE IT." }, None)),
        (1, new("atom_rad_sources", new[] { "THE DOSIMETER CLICKS IN THE ZONE.", "THE RED FOREST AND BLOCK 4 ARE HOT." }, None)),
        (1, new("atom_rad_dose", new[] { "THE GAUGE SHOWS YOUR DOSE IN MSV.", "OFF THE ROAD: THREE TIMES THE DOSE." }, None)),
        (1, new("atom_rad_limit", new[] { "OVER 20 MSV: THE DOSIMETRIST STOPS YOU.", "THE CHECK AT THE LINE COSTS TIME." }, None)),
        (1, new("atom_rad_wash", new[] { "WASH AT THE BIRCH AVENUE STATION.", "CROSS THE BAY SLOWLY TO LOSE DOSE." }, None)),
        (2, new("atom_fuel", new[] { "THROTTLE AND HIGH REVS USE FUEL.", "THE GAUGE BLINKS BEFORE THE TANK IS EMPTY." }, None)),
        (2, new("atom_empty", new[] { "EMPTY: THE ENGINE DIES. COAST ON.", "A DEPOT STOP CAN GET YOU MOVING." }, None)),
        (2, new("atom_refuel", new[] { "THE DEPOT IS ON THE LEFT, PAST", "THE FOREST. STOP IN ITS BAY." }, None)),
        (3, new("atom_repairs", new[] { "THE MECHANIC SLEEPS BY THE BUS STOP.", "HONK, THEN PARK BESIDE HIM TO REPAIR." }, None)),
    };

    /// <summary>The course on a track (each track teaches what is new on it); null: no school there yet.</summary>
    public static (int chapter, SchoolStep step)[] For(SchoolCourse c, string track = "bonus") =>
        c == SchoolCourse.Driving ? Driving : track switch { "ilyich" => Ilyich, "siberia" => Siberia, "atomgrad" when System.Array.IndexOf(Godot.OS.GetCmdlineUserArgs(), "--drone-tour") >= 0 => AtomgradDrone, "atomgrad" => Atomgrad, "bonus" when System.Array.IndexOf(Godot.OS.GetCmdlineUserArgs(), "--drone-tour") >= 0 => BonusDrone, _ => null };
    public static string[] Chapters(SchoolCourse c, string track = "bonus") =>
        c == SchoolCourse.Driving ? DrivingChapters : track switch { "ilyich" => IlyichChapters, "siberia" => SiberiaChapters, "atomgrad" => AtomgradChapters, "bonus" when System.Array.IndexOf(Godot.OS.GetCmdlineUserArgs(), "--drone-tour") >= 0 => BonusDroneChapters, _ => Array.Empty<string>() };

    /// <summary>The course has the player try its steps: none now (the schools show and tell; the tries'
    /// checks stay, tested, should a course want them back).</summary>
    public static bool HasTries(SchoolCourse c) => false;

    /// <summary>The DRIVING SCHOOL's track (its lessons are scripted on its bends).</summary>
    public const string DrivingTrack = "ilyich";

    /// <summary>The recap card: the controls and what they do.</summary>
    public static (SchoolKey key, string what)[] Recap(SchoolCourse c) => c == SchoolCourse.Driving
        ? new[]
        {
            (SchoolKey.Steer, "DRIVE"), (SchoolKey.Gas, "ACCELERATE"), (SchoolKey.Brake, "BRAKE"),
            (SchoolKey.Handbrake, "HANDBRAKE"),
            (SchoolKey.Clutch, "CLUTCH"), (SchoolKey.Horn, "HORN"), (SchoolKey.Rescue, "BACK ON THE ROAD"),
        }
        : new[]
        {
            (SchoolKey.Steer, "DRIVE"), (SchoolKey.Gas, "ACCELERATE"), (SchoolKey.Brake, "BRAKE"),
            (SchoolKey.Handbrake, "HANDBRAKE"),
            (SchoolKey.Clutch, "CLUTCH"), (SchoolKey.Horn, "HORN"), (SchoolKey.Rescue, "BACK ON THE ROAD"),
        };

    /// <summary>Every text the school shows can be drawn: pixel-font characters only (tests).</summary>
    public static IEnumerable<string> AllTexts()
    {
        foreach (var list in new[] { Ilyich, Siberia, Atomgrad, Driving, IlyichEnvironment, IlyichDrone, SiberiaDrone, AtomgradDrone, BonusDrone, SiberiaEnvironment })
            foreach (var (_, s) in list)
            {
                foreach (var t in s.Tell) yield return t;
                if (s.PadTell != null) foreach (var t in s.PadTell) yield return t;
                if (s.Hint != null) yield return s.Hint;
                if (s.PadHint != null) yield return s.PadHint;
            }
        foreach (var t in IlyichChapters) yield return t;
        foreach (var t in SiberiaChapters) yield return t;
        foreach (var t in AtomgradChapters) yield return t;
        foreach (var t in DrivingChapters) yield return t;
    }

    // ------------------------------------------------------------ road helpers

    /// <summary>Road metre i (wrapped).</summary>
    public static int Wrap(TrackData t, int i) => (i % t.Center.Length + t.Center.Length) % t.Center.Length;

    /// <summary>A point lateral m to the right of road metre i (right of the race direction).</summary>
    public static Vector2 RoadPoint(TrackData t, int i, float lateral)
    {
        i = Wrap(t, i);
        Vector2 d = t.CenterDir(i);
        return t.Center[i] + new Vector2(-d.Y, d.X) * lateral;
    }

    /// <summary>Signed road metres from a to b along the race direction (-n/2..n/2).</summary>
    public static int Along(TrackData t, int a, int b)
    {
        int n = t.Center.Length, d = ((b - a) % n + n) % n;
        return d > n / 2 ? d - n : d;
    }

    /// <summary>The car at from, heading dir at speed (m/s), in the gear for it, wheels turning (as Babushkas.AimPass does).</summary>
    public static void SetRolling(CarSim car, TrackData t, Vector2 from, Vector2 dir, float speed)
    {
        dir = dir.Normalized();
        car.Reset(new Vector3(from.X, t.MeshHeight(from.X, from.Y) + 0.55f, from.Y), TrackData.YawFor(dir));
        // Along the ground's slope: a flat start on a descent (the runway's) would leave the road and drop onto it.
        float rise = (t.MeshHeight(from.X + dir.X * 2f, from.Y + dir.Y * 2f) - t.MeshHeight(from.X - dir.X * 2f, from.Y - dir.Y * 2f)) / 4f;
        car.Vel = new Vector3(dir.X, rise, dir.Y) * speed;
        // ...and the car lies along it too, or the front wheels hang in the air (descent) or dig in (climb).
        if (MathF.Abs(rise) > 1e-3f)
        {
            Vector3 axis = car.Right.Normalized();
            float pitch = MathF.Atan(rise);
            car.Rot = new Basis(axis, pitch) * car.Rot;
            if (car.Forward.Y * rise < 0f) car.Rot = new Basis(axis, -2f * pitch) * car.Rot;
        }
        var p = car.P;
        for (int g = 1; g <= p.Gears.Length; g++)
        {
            car.Gear = g;
            float rpm = speed / p.WheelRadius * p.Gears[g - 1] * p.FinalDrive * 60f / MathF.Tau;
            if (rpm < p.ShiftUp * 0.85f) break;
        }
        foreach (var w in car.Wheels) w.SpinSpeed = speed / p.WheelRadius;
    }

    /// <summary>Steering towards a point (pure pursuit), -1..1.</summary>
    public static float SteerTo(CarSim car, Vector2 target, float gain = 2.2f)
    {
        Vector2 p = new(car.Pos.X, car.Pos.Z), fwd = new Vector2(car.Forward.X, car.Forward.Z).Normalized(), right = new Vector2(car.Right.X, car.Right.Z).Normalized();
        Vector2 to = (target - p).Normalized();
        return Math.Clamp(MathF.Atan2(to.Dot(right), to.Dot(fwd)) * gain, -1f, 1f);
    }

    /// <summary>Steering that follows the road lateral m right of the centre line, looking ahead with the speed.</summary>
    public static float SteerRoad(CarSim car, TrackData t, float lateral = 0f)
    {
        Vector2 p = new(car.Pos.X, car.Pos.Z);
        int i = t.NearestCenter(p, out _);
        return SteerTo(car, RoadPoint(t, i + 6 + (int)(car.Speed * 0.45f), lateral));
    }

    /// <summary>The road metre nearest to p, searched from hint (-8..+12 m: called every tick, so a hairpin's
    /// other leg or a nearby stretch never takes it); hint follows.</summary>
    public static int Follow(TrackData t, Vector2 p, ref int hint)
    {
        int n = t.Center.Length, best = hint;
        float bestD = float.MaxValue;
        for (int a = -8; a <= 12; a++)
        {
            int i = Wrap(t, hint + a);
            float d = t.Center[i].DistanceSquaredTo(p);
            if (d < bestD) { bestD = d; best = i; }
        }
        if (bestD > 40f * 40f) best = t.NearestCenter(p, out _);
        hint = best;
        return best;
    }

    /// <summary>
    /// Steering through a slide (the school's demos): it aims the car's path, not its nose, at the
    /// road ahead, and steers into the slide by the slip angle (counter-steer), so a car swung
    /// round by a slide technique is caught and driven on. hint: the road metre, followed.
    /// </summary>
    public static float SlideSteer(CarSim car, TrackData t, ref int hint, float lateral = 0f, float counter = 0.9f)
    {
        Vector2 p = new(car.Pos.X, car.Pos.Z);
        int i = Follow(t, p, ref hint);
        Vector2 target = RoadPoint(t, i + 7 + (int)(car.Speed * 0.5f), lateral);
        Vector2 fwd = new Vector2(car.Forward.X, car.Forward.Z).Normalized(), right = new Vector2(car.Right.X, car.Right.Z).Normalized();
        Vector2 vel = new(car.Vel.X, car.Vel.Z);
        Vector2 dir = vel.Length() > 2f ? vel.Normalized() : fwd;
        Vector2 to = (target - p).Normalized();
        // The path's error (in the car's frame: + to the right) and the slip (+: the path runs right of the nose).
        float pathErr = MathF.Atan2(to.Dot(right), to.Dot(fwd)) - MathF.Atan2(dir.Dot(right), dir.Dot(fwd));
        float slip = MathF.Atan2(dir.Dot(right), dir.Dot(fwd));
        return Math.Clamp(pathErr * 2.0f + slip * counter, -1f, 1f);
    }

    /// <summary>Metres beyond the road edge (negative: on the road) and the road metre of a point.</summary>
    public static float OffRoad(TrackData t, Vector2 p, out int index)
    {
        index = t.NearestCenter(p, out float d);
        return d - t.HalfWidth[index];
    }
}

/// <summary>
/// The pump-braking try: through a bend (road metres Entry..Exit), the car must stay within the
/// road edge plus Tolerance and get below SlowKmh somewhere in it. Leaving the road (a locked
/// front end goes straight on) fails at once; out of the bend too fast fails too.
/// </summary>
public class PumpBrakeCheck
{
    public float Length, Tolerance = 3f, SlowKmh = 45f;
    public float MaxOver { get; private set; } = float.NegativeInfinity;
    public float MinKmh { get; private set; } = float.MaxValue;
    public string Why { get; private set; }

    public PumpBrakeCheck(float length) => Length = length;

    /// <param name="s">road metres past the bend's entry (negative before it)</param>
    /// <param name="over">metres beyond the road edge (negative: on the road)</param>
    public Verdict Feed(float s, float over, float kmh)
    {
        if (s >= -5f) MaxOver = Math.Max(MaxOver, over);
        if (over > Tolerance && s >= -5f) { Why = "YOU LEFT THE ROAD"; return Verdict.Fail; }
        if (s >= 0 && s <= Length) MinKmh = Math.Min(MinKmh, kmh);
        if (s <= Length) return Verdict.Pending;
        if (MinKmh <= SlowKmh) return Verdict.Success;
        Why = "TOO FAST. TAP THE BRAKE";
        return Verdict.Fail;
    }
}

/// <summary>
/// The clutch kick's try: the kick breaks the tail loose, then the bend (road metres 0..Length) is
/// driven round in a held slide: on the road (plus Tolerance) all through it, never more sideways
/// than MaxSlip (a spin), never slower than MinKmh, and sliding (MinSlip or more, the tail out of
/// the bend) for at least Share of it. A tail that grips again is caught at the bend's end.
/// </summary>
public class KickSlideCheck
{
    public float Length, Tolerance = 3f, Before = 25f, MinSlip = 10f, MaxSlip = 50f, MinKmh = 30f, Share = 0.5f;
    public string Why { get; private set; }
    public int Slid { get; private set; }      // metres of the bend driven in the slide
    public float PeakSlip { get; private set; }
    bool _used;
    int _last = int.MinValue;

    public KickSlideCheck(float length) => Length = length;

    /// <param name="s">road metres past the bend's entry</param>
    /// <param name="over">metres beyond the road edge</param>
    /// <param name="used">the kick has been used (this run)</param>
    /// <param name="slip">the slip angle in degrees, + with the tail out of the bend</param>
    /// <param name="missing">why, when the kick has not come by 10 m into the bend</param>
    public Verdict Feed(float s, float over, bool used, float slip, float kmh, string missing)
    {
        if (s >= -Before && used) _used = true;
        if (over > Tolerance && s >= -10f) { Why = _used ? "CATCH IT! STEER INTO THE SLIDE" : "YOU LEFT THE ROAD"; return Verdict.Fail; }
        if (s >= 0 && s <= Length)
        {
            if (!_used)
            {
                if (s <= 10f) return Verdict.Pending;
                Why = missing;
                return Verdict.Fail;
            }
            PeakSlip = Math.Max(PeakSlip, slip);
            if (MathF.Abs(slip) > MaxSlip) { Why = "TOO SIDEWAYS! EASE OFF THE GAS"; return Verdict.Fail; }
            if (kmh < MinKmh) { Why = "KEEP IT GOING: GAS ON"; return Verdict.Fail; }
            int m = (int)s;
            if (m != _last && m < Length) { _last = m; if (slip >= MinSlip) Slid++; }
        }
        if (s <= Length) return Verdict.Pending;
        if (Slid >= Share * Length) return Verdict.Success;
        Why = "THE TAIL GRIPPED: KEEP IT SLIDING";
        return Verdict.Fail;
    }
}

/// <summary>
/// A slide technique's try through a bend (road metres Entry..Exit): the technique must be used in
/// it (from a little before), the car must stay within the road edge plus Tolerance, and get out
/// of the bend's end. Leaving the road fails at once; out of the bend without the technique fails.
/// </summary>
public class SlideCheck
{
    public float Length, Tolerance = 3f, Before = 25f;
    public string Why { get; private set; }
    bool _used;

    public SlideCheck(float length) => Length = length;

    /// <param name="s">road metres past the bend's entry</param>
    /// <param name="over">metres beyond the road edge</param>
    /// <param name="used">the technique has been used (this run)</param>
    public Verdict Feed(float s, float over, bool used, string missing)
    {
        if (s >= -Before && used) _used = true;
        if (over > Tolerance && s >= -10f) { Why = _used ? "CATCH IT! STEER INTO THE SLIDE" : "YOU LEFT THE ROAD"; return Verdict.Fail; }
        if (s <= Length) return Verdict.Pending;
        if (_used) return Verdict.Success;
        Why = missing;
        return Verdict.Fail;
    }
}

/// <summary>
/// The handbrake try: the car turns more than TurnDeg within Window seconds, slower than MaxKmh,
/// with the handbrake pulled during it.
/// </summary>
public class HandbrakeTurnCheck
{
    public float Window = 3f, TurnDeg = 120f, MaxKmh = 60f;
    readonly List<(float t, float yaw, bool hb)> _s = new();
    float _total, _prevYaw = float.NaN;
    public float BestDeg { get; private set; }

    /// <param name="yaw">heading in radians (any wrap)</param>
    public Verdict Feed(float t, float yaw, float kmh, bool handbrake)
    {
        if (!float.IsNaN(_prevYaw)) _total += Mathf.AngleDifference(_prevYaw, yaw);
        _prevYaw = yaw;
        if (kmh > MaxKmh) { _s.Clear(); return Verdict.Pending; }
        _s.Add((t, _total, handbrake));
        while (_s.Count > 0 && _s[0].t < t - Window) _s.RemoveAt(0);
        bool hb = false;
        float turn = 0;
        foreach (var x in _s)
        {
            hb |= x.hb;
            turn = Math.Max(turn, Math.Abs(Mathf.RadToDeg(_total - x.yaw)));
        }
        BestDeg = Math.Max(BestDeg, hb ? turn : 0);
        return hb && turn >= TurnDeg ? Verdict.Success : Verdict.Pending;
    }
}

/// <summary>
/// The clutch launch try: from (nearly) a standstill, the clutch held while the engine revs past
/// RevRpm, then let go: RaceKmh within Window seconds of letting go. Driving off on the throttle
/// without the clutch fails; rolling a little (the runway slopes) is no launch yet.
/// </summary>
public class ClutchLaunchCheck
{
    public float RevRpm = 4000f, RaceKmh = 50f, Window = 3.5f, CreepKmh = 15f;     // (3.5 s: a dirt start takes ~3 s)
    bool _revved, _held, _launched;
    float _since;
    public string Why { get; private set; }
    public float LaunchTime { get; private set; } = -1;

    public Verdict Feed(float dt, float kmh, bool clutch, float rpm, float throttle = 1f)
    {
        if (!_launched)
        {
            if (clutch && kmh < CreepKmh) { _held = true; _revved |= rpm >= RevRpm; return Verdict.Pending; }
            if (!_held)
            {
                if (kmh > 8f && throttle > 0.3f) { Why = "HOLD THE CLUTCH FIRST"; return Verdict.Fail; }
                return Verdict.Pending;
            }
            if (!_revved) { _held = false; return Verdict.Pending; }     // let go without revving: hold it again
            _launched = true;
            _since = 0;
        }
        _since += dt;
        if (kmh >= RaceKmh) { LaunchTime = _since; return Verdict.Success; }
        if (_since > Window) { Why = "TOO SLOW. MORE REVS!"; return Verdict.Fail; }
        return Verdict.Pending;
    }
}

/// <summary>
/// Whether the driver has been through each course's school (to the end, skips included): the
/// INSTRUCTIONS? card then offers NO first. Kept in settings.cfg ([tutorial] rally_seen; an old stakhanov_seen is left alone).
/// </summary>
public class TutorialFile
{
    const string Section = "tutorial";

    readonly string _path;
    public bool RallySeen { get; private set; }

    /// <summary>The file in dir (RecordStore.Dir); null: kept in memory only (tests).</summary>
    public TutorialFile(string dir)
    {
        if (dir == null) return;
        _path = dir + "settings.cfg";
        var cfg = new ConfigFile();
        if (cfg.Load(_path) != Error.Ok) return;
        RallySeen = Read(cfg, "rally_seen");
    }

    static bool Read(ConfigFile cfg, string key)
    {
        var v = cfg.GetValue(Section, key, false);
        return v.VariantType == Variant.Type.Bool && (bool)v;
    }

    public bool Seen(SchoolCourse c) => RallySeen;

    readonly HashSet<string> _seen = new();

    /// <summary>Any one-time explanation by key (e.g. "gai_chase_ilyich"): seen already?</summary>
    public bool SeenKey(string key)
    {
        if (_seen.Contains(key)) return true;
        if (_path == null) return false;
        var cfg = new ConfigFile();
        if (cfg.Load(_path) != Error.Ok || !Read(cfg, key)) return false;
        _seen.Add(key);
        return true;
    }

    /// <summary>The explanation by key has been shown: remembered (the other sections stay as they are).</summary>
    public void MarkKey(string key)
    {
        if (!_seen.Add(key) || _path == null) return;
        var cfg = new ConfigFile();
        cfg.Load(_path);
        cfg.SetValue(Section, key, true);
        cfg.Save(_path);
    }

    /// <summary>The course was gone through to the end: remembered (the other sections stay as they are).</summary>
    public void MarkSeen(SchoolCourse c)
    {
        if (Seen(c)) return;
        RallySeen = true;
        if (_path == null) return;
        var cfg = new ConfigFile();
        cfg.Load(_path);      // keep the other sections (radio, rumble, the KGB file)
        cfg.SetValue(Section, "rally_seen", true);
        cfg.Save(_path);
    }
}
