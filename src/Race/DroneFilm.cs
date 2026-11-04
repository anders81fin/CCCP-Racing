using System;
using System.Collections.Generic;

namespace CCCPRacing.Race;

/// <summary>A track's drone presentation (RaceScene.DroneTour.cs): its card, captions, cues and music, with the
/// flight data baked from the approved film (DroneTourData.*, made by tools/video/export_drone_data.py).
/// The tour on CHOOSE TRACK (T) flies it where the film is Ready.</summary>
sealed class DroneFilm
{
    public string Track;
    /// <summary>The baked flight path, one sample each SampleStep seconds: eyeX eyeZ eyeAGL targetX targetZ targetAGL fov lightClock.</summary>
    public float[][] Path = Array.Empty<float[]>();
    /// <summary>The lap chase drone's keys by road metre: metre azimuth dist height fov subjectX subjectZ blend.</summary>
    public float[][] LapKeys = Array.Empty<float[]>();
    /// <summary>When the flight passes each named waypoint, in seconds.</summary>
    public Dictionary<string, float> Marks = new();
    public float SampleStep = 0.5f;
    /// <summary>When the car starts (the path runs on a few seconds after it: the hand-over to the chase drone).</summary>
    public float LapAt;
    /// <summary>The black name card: big lines (scale 3) and explanation lines.</summary>
    public string[] CardBig = Array.Empty<string>(), CardSmall = Array.Empty<string>();
    public float CardEnd = 4.5f;
    /// <summary>The flight starts this long after the film does (the black card holds meanwhile): marks, captions, cues and the light run on film time = time - FilmDelay.</summary>
    public float FilmDelay;
    /// <summary>(waypoint mark, seconds after it, seconds shown, text), one line each.</summary>
    public (string mark, float after, float length, string text)[] Captions = Array.Empty<(string, float, float, string)>();
    /// <summary>(from road metre, to road metre, text): lines shown while the car drives the lap (the tracks' school tips).</summary>
    public (int from, int to, string text)[] LapCaptions = Array.Empty<(int, int, string)>();
    /// <summary>Actors run ahead on their own clocks before the film starts, so they are at work where the drone passes.</summary>
    public (string actor, float seconds)[] Prerolls = Array.Empty<(string, float)>();
    /// <summary>Things cued by film time (seconds), or in the lap by road metre.</summary>
    public (string name, float at)[] Cues = Array.Empty<(string, float)>();
    public (string name, int metre)[] LapCues = Array.Empty<(string, int)>();

    public bool Ready => Path.Length > 1 && LapKeys.Length > 1;

    public static DroneFilm For(string track) => track switch { "ilyich" => Kolkhoz, "siberia" => Siberia, "atomgrad" => Atomgrad, "bonus" => Bonus, _ => null };

    static readonly DroneFilm Kolkhoz = new()
    {
        Track = "ilyich",
        Path = DroneTourData.Kolkhoz.Path, LapKeys = DroneTourData.Kolkhoz.LapKeys, Marks = DroneTourData.Kolkhoz.Marks,
        SampleStep = DroneTourData.Kolkhoz.SampleStep, LapAt = DroneTourData.Kolkhoz.LapAt, CardEnd = DroneTourData.Kolkhoz.CardEnd, FilmDelay = DroneTourData.Kolkhoz.FilmDelay,
        CardBig = DroneTourData.Kolkhoz.CardBig, CardSmall = DroneTourData.Kolkhoz.CardSmall, Captions = DroneTourData.Kolkhoz.Captions, LapCaptions = DroneTourData.Kolkhoz.LapCaptions,
        // (the world runs 13.5 s more while the card holds, so the staging runs ahead 13.5 s less than it did)
        Prerolls = new[] { ("tractor", 23.5f), ("slurry", 30.5f) },
        Cues = new[] { ("hunt", 51f) },
    };

    static readonly DroneFilm Siberia = new()
    {
        Track = "siberia",
        Path = DroneTourData.Siberia.Path, LapKeys = DroneTourData.Siberia.LapKeys, Marks = DroneTourData.Siberia.Marks,
        SampleStep = DroneTourData.Siberia.SampleStep, LapAt = DroneTourData.Siberia.LapAt, CardEnd = DroneTourData.Siberia.CardEnd, FilmDelay = DroneTourData.Siberia.FilmDelay,
        CardBig = DroneTourData.Siberia.CardBig, CardSmall = DroneTourData.Siberia.CardSmall, Captions = DroneTourData.Siberia.Captions,
        // The volunteers set off from the middle of the camp 11 s before the film (so the head of the column is at the far gate
        // as the drone passes it); the against-plough comes down the ridge under the drone and the strip is clear when the MiK
        // is cued to overtake the drone on the runway (the cue time is the runway mark minus 12.8 s: 58.4 - 12.8).
        Cues = new[] { ("column", -11f), ("sortie", 45.6f) },
        LapCues = new[] { ("sortie", 540) },
    };

    static readonly DroneFilm Atomgrad = new()
    {
        Track = "atomgrad",
        Path = DroneTourData.Atomgrad.Path, LapKeys = DroneTourData.Atomgrad.LapKeys, Marks = DroneTourData.Atomgrad.Marks,
        SampleStep = DroneTourData.Atomgrad.SampleStep, LapAt = DroneTourData.Atomgrad.LapAt, CardEnd = DroneTourData.Atomgrad.CardEnd, FilmDelay = DroneTourData.Atomgrad.FilmDelay,
        CardBig = DroneTourData.Atomgrad.CardBig, CardSmall = DroneTourData.Atomgrad.CardSmall, Captions = DroneTourData.Atomgrad.Captions, LapCaptions = DroneTourData.Atomgrad.LapCaptions,
        // The goods train sets out for the crossing as the drone nears it and the catfish jumps as it passes the channel;
        // the lunokhod walks its own beat across the road.
        Prerolls = new[] { ("ploughs", 14.5f) },      // the mixers are at the plant (the sarcophagus) as the drone passes it
        Cues = new[] { ("train", 4f), ("fish", 60f) },
    };
    static readonly DroneFilm Bonus = new()
    {
        Track = "bonus",
        Path = DroneTourData.Bonus.Path, LapKeys = DroneTourData.Bonus.LapKeys, Marks = DroneTourData.Bonus.Marks,
        SampleStep = DroneTourData.Bonus.SampleStep, LapAt = DroneTourData.Bonus.LapAt, CardEnd = DroneTourData.Bonus.CardEnd, FilmDelay = DroneTourData.Bonus.FilmDelay,
        CardBig = DroneTourData.Bonus.CardBig, CardSmall = DroneTourData.Bonus.CardSmall,
        Captions = DroneTourData.Bonus.Captions, LapCaptions = DroneTourData.Bonus.LapCaptions,
    };

}
