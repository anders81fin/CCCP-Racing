using System;
using System.Collections.Generic;
using Godot;

namespace CCCPRacing.Audio;

/// <summary>
/// The Pyatyorka's radio. Stations are the mp3 files in assets/music/radio (sorted by name);
/// Backspace switches the set on/off, the pause menu selects stations, 0 / + turn the volume
/// down / up (11 levels, saved in settings.cfg next to the records).
/// Same cheap-radio sound as the title music; station changes sweep through static.
/// When a song ends the radio moves on to the next station.
/// Near the radar station the set picks up interference: from ~60 m the hiss and crackle grow,
/// the station fades and drops out, a heterodyne whistle warbles, and every time the dish sweeps
/// past the car a harsh buzz burst tears through.
/// </summary>
public partial class CarRadio : Node
{
    public const string Folder = "res://assets/music/radio";

    readonly List<(string name, string path)> _stations = new();
    RadioMusic _radio;
    int _current = -1;
    bool _on;

    /// <summary>Volume levels 0..10, 3 dB apart; level 8 is the original loudness, 0 is mute.</summary>
    public const int MaxVolume = 10, DefaultVolume = 8;
    const float DefaultDb = -3f, StepDb = 3f;
    int _volume = DefaultVolume;
    string _settingsPath;

    /// <summary>Raised with the text to flash on screen (song title, "RADIO OFF").</summary>
    public event Action<string> Announce;

    /// <summary>Car position, fed every frame by the race (radar interference).</summary>
    public Vector3 CarPos;
    /// <summary>Stakhanov laps: no radar interference, the radio plays clean.</summary>
    public bool NoRadar;
    /// <summary>Radar interference starts this far from the station and is full inside StrongRange.</summary>
    public const float FarRange = 60f, StrongRange = 20f;
    /// <summary>Beam half-width (rad) in which the sweeping dish buzzes the radio.</summary>
    const float BeamHalfWidth = 0.3f;
    static readonly bool Debug = OS.GetEnvironment("CCCP_RADAR_DEBUG") != "";
    float _dbgT;

    public int StationCount => _stations.Count;
    public int StationNumber => _current + 1;

    /// <summary>Station i (0-based): its song's name, and the stream to play (the title screen can tune to them too).</summary>
    public string StationName(int i) => _stations[i].name;
    public AudioStream StationStream(int i) => ResourceLoader.Load<AudioStream>(_stations[i].path);
    public bool On => _on;
    public int Volume => _volume;

    public override void _Ready()
    {
        _radio = new RadioMusic { TrackPath = "", LoopSongs = false, VolumeDb = VolumeToDb(_volume) };
        AddChild(_radio);
        _radio.SongStarted += name => Announce?.Invoke($"RADIO {_current + 1}: {name}");
        _radio.SongFinished += () => { if (_on && _stations.Count > 0) Select((_current + 1) % _stations.Count); };
        LoadStations();
    }

    /// <summary>The stations in order: RADIO 1 first.</summary>
    static readonly string[] Order =
    {
        "Kadonneen jäsenkirjan jäljillä", "Moskowaltz", "Soita sitä haitaria, Dmitri!", "Kevät saapuu Moskovaan", "Unknown Party Classic No. 2 (Censored)",
    };

    void LoadStations()
    {
        var dir = DirAccess.Open(Folder);
        if (dir == null) return;
        var files = new List<string>();
        foreach (string f in dir.GetFiles())
        {
            // Exported builds list "x.mp3.import"; the editor lists "x.mp3".
            string name = f.EndsWith(".import") ? f[..^".import".Length] : f;
            if (name.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) && !files.Contains(name)) files.Add(name);
        }
        // The stations' numbers stay put whatever the songs are called; anything new comes after, A-Z.
        files.Sort((x, y) =>
        {
            int ix = Array.IndexOf(Order, x[..^4]), iy = Array.IndexOf(Order, y[..^4]);
            if (ix < 0) ix = int.MaxValue;
            if (iy < 0) iy = int.MaxValue;
            return ix != iy ? ix.CompareTo(iy) : StringComparer.OrdinalIgnoreCase.Compare(x, y);
        });
        foreach (string f in files) _stations.Add((f[..^4], Folder + "/" + f));
    }

    public void Toggle()
    {
        if (_stations.Count == 0) { Announce?.Invoke("RADIO: NO STATIONS"); return; }
        if (_on)
        {
            _on = false;
            _radio.PowerOff();
            Announce?.Invoke("RADIO OFF");
        }
        else
        {
            _on = true;
            Select(Math.Max(0, _current));
        }
    }

    /// <summary>Switch on (current station, or the first) or off without the "RADIO OFF" text
    /// unless announce is set. Used for the default at the start of a run.</summary>
    public void SetOn(bool on, bool announce)
    {
        if (on == _on || _stations.Count == 0) return;
        if (on) { Select(Math.Max(0, _current)); return; }
        _on = false;
        _radio.PowerOff();
        if (announce) Announce?.Invoke("RADIO OFF");
    }

    bool _previewing;

    /// <summary>The mixer's RADIO row: the music plays for a moment even with the radio off (the volume can be set by ear);
    /// it does not turn the radio on and stops again when the row is left.</summary>
    public void SetPreview(bool on)
    {
        if (on == _previewing || _stations.Count == 0) return;
        _previewing = on;
        if (_on) return;
        if (on) _radio.Tune(ResourceLoader.Load<AudioStream>(_stations[Math.Max(0, _current)].path), _stations[Math.Max(0, _current)].name);
        else _radio.Stop();
    }

    /// <summary>Browse stations in either direction, wrapping and switching the radio on.</summary>
    public void StepStation(int step)
    {
        int count = _stations.Count;
        if (count == 0) return;
        int current = _current < 0 ? (step < 0 ? 0 : -1) : _current;
        Select(((current + step) % count + count) % count);
    }

    /// <summary>Volume one step up (+1) or down (-1), announced and saved.</summary>
    public void StepVolume(int step)
    {
        int v = Math.Clamp(_volume + step, 0, MaxVolume);
        if (v != _volume)
        {
            _volume = v;
            _radio.VolumeDb = VolumeToDb(v);
            SaveVolume();
        }
        Announce?.Invoke(v == 0 ? "RADIO VOLUME: MUTE" : "RADIO VOLUME " + new string('#', v) + new string('-', MaxVolume - v));
    }

    static float VolumeToDb(int v) => v <= 0 ? -80f : DefaultDb + (v - DefaultVolume) * StepDb;

    /// <summary>Read the saved volume from settings.cfg in the data directory (RecordStore.Dir).
    /// Missing or invalid: the default level.</summary>
    public void LoadVolume(string dir)
    {
        _settingsPath = dir + "settings.cfg";
        var cfg = new ConfigFile();
        int v = DefaultVolume;
        if (cfg.Load(_settingsPath) == Error.Ok)
        {
            var val = cfg.GetValue("radio", "volume", DefaultVolume);
            if (val.VariantType == Variant.Type.Int && (int)val >= 0 && (int)val <= MaxVolume) v = (int)val;
        }
        _volume = v;
        if (_radio != null) _radio.VolumeDb = VolumeToDb(v);
    }

    void SaveVolume()
    {
        if (_settingsPath == null) return;
        var cfg = new ConfigFile();
        cfg.Load(_settingsPath);      // keep any other sections
        cfg.SetValue("radio", "volume", _volume);
        cfg.Save(_settingsPath);
    }

    /// <summary>Tune to a station (0-based). Switches the radio on if it was off.</summary>
    public void Select(int index)
    {
        if (index < 0 || index >= _stations.Count) return;
        _on = true;
        _current = index;
        var stream = ResourceLoader.Load<AudioStream>(_stations[index].path);
        _radio.Tune(stream, _stations[index].name);
    }

    public override void _Process(double delta)
    {
        float interference = 0f, sweep = 0f, best = float.MaxValue;
        var world = Track.WorldScope.Of(this);      // (a second world's radars, the replay viewer's, are not heard here)
        foreach (var head in Track.RadarHead.Heads)
        {
            if (head.World != world) continue;
            var hp = head.GlobalPosition;
            var to = new Vector2(CarPos.X - hp.X, CarPos.Z - hp.Z);
            float d = to.Length();
            if (d >= best) continue;
            best = d;
            // Smoothstep from FarRange in to StrongRange.
            float x = Math.Clamp((FarRange - d) / (FarRange - StrongRange), 0f, 1f);
            interference = x * x * (3f - 2f * x);
            float off = d > 1f ? MathF.Abs(head.Beam.AngleTo(to / d)) : 0f;
            sweep = Math.Clamp(1f - off / BeamHalfWidth, 0f, 1f);
        }
        if (NoRadar) interference = sweep = 0f;
        _radio.Interference = interference;
        _radio.Sweep = interference > 0f ? sweep : 0f;
        if (Debug && (_dbgT += (float)delta) >= 0.25f)
        {
            _dbgT = 0;
            GD.Print($"RADAR t={Time.GetTicksMsec() / 1000f:F2} d={best:F0} interference={interference:F2} sweep={sweep:F2}");
        }
    }

    /// <summary>Title screen plays its own music: the car radio waits and resumes afterwards.</summary>
    public void Suspend(bool on) => _radio.Suspend(on);
}
