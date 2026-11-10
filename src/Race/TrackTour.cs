using System;
using Godot;

namespace CCCPRacing.Race;

/// <summary>Where a tour points: a road metre (metres right of the centre line), a place on the map, or the mechanic.</summary>
readonly record struct TourSpot(int Metre = -1, float Lateral = 0f, Vector2? At = null, bool Mechanic = false, float Up = 1f);

/// <summary>What the tour's driver does over a stretch of road (else the autopilot drives).</summary>
enum TourDrive
{
    /// <summary>A slide lesson's technique (Tech: kick, hairpin) into the bend at Entry, at Kmh (ApproachKmh until BrakeAt); in place of the autopilot.</summary>
    Technique,
    /// <summary>Kmh held on the road's line, over the autopilot.</summary>
    Cruise,
    /// <summary>The autopilot's pedals, the wheel on a line Lateral metres right of the centre line.</summary>
    Line,
    /// <summary>Pumped brakes down to Kmh, then crawling at CrawlKmh (ice).</summary>
    PumpBrake,
}

/// <summary>One driving instruction over road metres [From, To).</summary>
sealed record TourZone(int From, int To, TourDrive Kind, float Kmh = 0f, string Tech = null, int Entry = 0,
    float ApproachKmh = 0f, int BrakeAt = int.MinValue, float Lateral = 0f, float CrawlKmh = 0f, float SlowMo = 0f);

/// <summary>A landmark pointed out over road metres [From, To): the camera frames Look with the car, the arrow is on Arrow
/// (only while well in the frame, if InFrame), a screenshot (--school-shots) as it comes up.</summary>
sealed record TourLandmark(int From, int To, TourSpot? Look, TourSpot? Arrow, string Shot, bool InFrame = false);

/// <summary>The caption over road metres [From, To).</summary>
sealed record TourCaption(int From, int To, string Text);

/// <summary>A part of the lap, a step of the school (Id): from where the last ended to road metre To (-1: over the line),
/// its caption where no other is (Captions) and its landmarks.</summary>
sealed record TourPart(string Id, int To, string Caption, TourCaption[] Captions = null, TourLandmark[] Landmarks = null);

/// <summary>
/// A track's lap with tips, the first lesson of its rally school (THE TRACK): the autopilot round the lap
/// from the start, the parts told as they come, the landmarks pointed out, the techniques shown where the
/// track asks for them (in slow motion). Road metres past the line on the way home count on from the lap's
/// length. RaceScene.TrackTour.cs drives it; a new track's tour is a new entry here.
/// </summary>
sealed class TrackTour
{
    public TourPart[] Parts;
    public TourZone[] Zones = Array.Empty<TourZone>();
    /// <summary>The car starts in the start building (StartPos) rather than on road metre 0.</summary>
    public bool FromStartPos;
    /// <summary>The airbase crew and its kind stay in the school's world for the tour.</summary>
    public bool Details;
    public float Speed = 1.5f, MaxTime = 70f;
    /// <summary>A part ends within this many metres past its To.</summary>
    public int EndSlack = 200;

    public static TrackTour For(string trackId, int lap) => trackId switch
    {
        "ilyich" => Ilyich(lap),
        "siberia" => Siberia(),
        "atomgrad" => Atomgrad(),
        "bonus" => Bonus(),
        _ => null,
    };

    public TourPart Part(string id) => Array.Find(Parts, p => p.Id == id);

    static TrackTour Bonus() => new()
    {
        FromStartPos = true, MaxTime = 120f, Speed = 1f,
        Parts = new[] { new TourPart("bonus_tour_lap", -1, "POLIGON 17 - THE EXERCISE IS LIVE") },
    };

    const int Open = int.MaxValue;

    /// <summary>ATOMGRAD-4: checkpoint, red forest, plant, cooling channel, town and the wash bay.</summary>
    static TrackTour Atomgrad() => new()
    {
        MaxTime = 115f,
        Speed = 1.2f,
        EndSlack = 120,
        Parts = new TourPart[]
        {
            new("atom_tour_forest", 205, "THE RED FOREST: THE ZONE IS HOT.",
                new[] { new TourCaption(0, 75, "THE CHECKPOINT: INTO THE ZONE."), new TourCaption(75, Open, "THE RED FOREST. KEEP TO THE ROAD.") },
                new[] { new TourLandmark(75, 205, new TourSpot(145), new TourSpot(145), "red_forest", true) }),
            new("atom_tour_plant", 360, "THE POWER PLANT AND THE VEHICLE GRAVEYARD.",
                Landmarks: new[] { new TourLandmark(205, 360, new TourSpot(292), new TourSpot(292), "vehicle_graveyard", true) }),
            new("atom_tour_channel", 575, "THE COOLING CHANNEL: WET CONCRETE.",
                new[] { new TourCaption(360, 445, "ROUND THE TURBINE HALL."), new TourCaption(445, Open, "FLAT OUT BESIDE THE COOLING CHANNEL.") },
                new[] { new TourLandmark(360, 445, new TourSpot(407), new TourSpot(407), "turbine_hall", true), new TourLandmark(445, 575, new TourSpot(505), new TourSpot(505), "cooling_channel", true) }),
            new("atom_tour_town", 790, "ATOMGRAD-4: TIGHT CITY CORNERS.",
                new[] { new TourCaption(575, 635, "BRAKE FOR THE BRIDGE INTO TOWN."), new TourCaption(635, Open, "THE SQUARE AND THE FERRIS WHEEL.") },
                new[] { new TourLandmark(650, 790, new TourSpot(716), new TourSpot(716), "ferris_wheel", true) }),
            new("atom_tour_wash", -1, "THE BIRCH AVENUE LEADS TO THE CHECKPOINT.",
                new[] { new TourCaption(790, 850, "THE WASH STATION: CROSS SLOWLY."), new TourCaption(850, Open, "BACK TO THE CHECKPOINT. OUT OF THE ZONE.") },
                new[] { new TourLandmark(790, 850, new TourSpot(At: new Vector2(68, 231)), new TourSpot(At: new Vector2(68, 231)), "wash_station", true) }),
        }
    };

    /// <summary>
    /// ILYICH'S LAMP (ILYICHLAB): out of the barn, the first bend (the GAI radar), the run to the one big jump
    /// (takeoff at 251 m) flat out, the ford on the gravel bar on the left (82 km/h kept against 62 inside), a clutch kick in both
    /// long bends at the bottom (500, 625 m: the slide held mid-road), the church hairpin on the handbrake
    /// after a brisk approach and late braking, up the hill to the barn on a tight line.
    /// </summary>
    static TrackTour Ilyich(int lap)
    {
        static TourLandmark Focus(int from, int to, int at, string shot = null, float lateral = 0f) =>
            new(from, to, new TourSpot(at, lateral), new TourSpot(at, lateral), shot ?? $"landmark_{at}", InFrame: true);
        return new TrackTour
        {
            FromStartPos = true,
            MaxTime = 70f,
            Parts = new TourPart[]
            {
                new("tour_first_bend", 170, "THE START: OUT OF THE BARN.",
                    new[] { new TourCaption(100, 211, "THE FIRST BEND. TURN RIGHT.") },
                    new[] { Focus(100, 211, 180, "first_bend") }),
                new("tour_jump", 360, "THE JUMP: LINE UP, FLAT OUT.",
                    new[] { new TourCaption(300, Open, "LAND DOWNHILL. BRAKE FOR THE STREAM.") },
                    new[] { Focus(170, 290, 251), Focus(300, 361, 335) }),
                new("tour_ford", 435, "DOWN TO THE STREAM. KEEP LEFT.",
                    new[] { new TourCaption(400, 435, "ACROSS ON THE GRAVEL BAR.") },
                    new[] { Focus(360, 436, 400, lateral: -3f) }),
                new("tour_bends", 600, "THE FIELD BENDS: CLUTCH KICK.",
                    new[] { new TourCaption(435, 475, "THE MUD HAIRPIN.") },
                    new[] { Focus(400, 475, 440), Focus(475, 525, 500), Focus(525, 601, 560) }),
                new("tour_hairpin", 850, "THE GRAIN STORE HAIRPIN.",
                    new[] { new TourCaption(0, 744, "THE FIELD BENDS. TO THE VILLAGE.") },
                    new[] { Focus(600, 691, 660), Focus(744, 851, 804) }),
                new("tour_finish", -1, "UP THE HILL, HOME TO THE BARN.",
                    new[] { new TourCaption(975, Open, "THE BARN: THROUGH TO THE FINISH.") },
                    new[] { Focus(900, 975, 960), Focus(lap - 65, Open, lap - 5) }),
            },
            Zones = new TourZone[]
            {
                new(195, 305, TourDrive.Cruise, Kmh: 120f),                                         // the run to the jump flat out
                new(305, 345, TourDrive.Cruise, Kmh: 95f),                                          // ...eased off after the landing (the jump carries speed: the stream needs it gone)
                new(330, 345, TourDrive.Cruise, Kmh: 95f, SlowMo: 2.5f),                            // ...and the jump
                new(345, 365, TourDrive.Cruise, Kmh: 80f),                                          // the landing: down to the speed for the stream
                new(365, 435, TourDrive.Line, Lateral: -3f),                                        // the ford on the bar
                new(475, 558, TourDrive.Technique, Kmh: 70f, Tech: "kick", Entry: 500, SlowMo: 2f), // the first long bend
                new(600, 715, TourDrive.Technique, Kmh: 70f, Tech: "kick", Entry: 625, SlowMo: 2f), // the second
                new(744, 834, TourDrive.Technique, Kmh: 50f, Tech: "hairpin", Entry: 804,           // the church hairpin:
                    ApproachKmh: 66f, BrakeAt: 778, SlowMo: 2f),                                     // brisk, then late on the brakes
                new(834, 930, TourDrive.Cruise, Kmh: 110f),                                         // up the hill
                new(930, 985, TourDrive.Cruise, Kmh: 70f),                                          // the last bend
                new(985, lap, TourDrive.Cruise, Kmh: 110f),                                         // through the barn, to the line
            },
        };
    }

    /// <summary>SIBERIA: the valley, the switchbacks under the radar, the airbase runway, down to the frozen
    /// river, the ice exit braked early (the bend hides its shape, the timber wreck waits wide), through the camp
    /// past the mechanic's barrack, home.</summary>
    static TrackTour Siberia() => new()
    {
        Details = true,
        MaxTime = 90f,
        EndSlack = 100,
        Parts = new TourPart[]
        {
            new("snow_tour_start", 270, "THE VALLEY. UP TO THE HILLS."),
            new("snow_tour_hills", 610, "SWITCHBACKS, UP PAST THE RADAR.",
                new[] { new TourCaption(435, 470, "THE RADAR ABOVE THE SWITCHBACKS.") },
                new[] { new TourLandmark(435, 470, new TourSpot(At: new Vector2(270, 156)), new TourSpot(At: new Vector2(275, 156), Up: 5f), "radar") }),
            new("snow_tour_base", 820, "THE AIRBASE. WEST ON THE RUNWAY."),
            new("snow_tour_river", 1070, "DOWN THE WEST SLOPE TO THE RIVER.",
                new[] { new TourCaption(1035, 1090, "BRAKE EARLY. ICE EXIT IS TRICKY.") }),
            new("snow_tour_camp", 1250, "THROUGH THE CAMP.",
                new[]
                {
                    new TourCaption(1035, 1090, "BRAKE EARLY. ICE EXIT IS TRICKY."),
                    new TourCaption(1090, 1160, "DO NOT RUN WIDE INTO THE WRECK."),
                    new TourCaption(1172, 1215, "FIRST RIGHT BARRACK: MECHANIC."),
                },
                new[] { new TourLandmark(1172, 1215, null, new TourSpot(Mechanic: true, Up: 2.6f), "camp") }),
            new("snow_tour_home", -1, "LEFT OUT OF THE CAMP. HOME AGAIN."),
        },
        Zones = new TourZone[]
        {
            new(1035, 1160, TourDrive.PumpBrake, Kmh: 32f, CrawlKmh: 28f),     // the ice exit
        },
    };
}
