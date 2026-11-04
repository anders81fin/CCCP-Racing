using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Race;

/// <summary>Each track's one-take "drone" presentation: a prelude flown along a baked camera path, the light
/// running from day to night and back, then the instructions' autopilot lap under a smoothed chase drone.
/// It is the track's tour on CHOOSE TRACK (T; the data is DroneTourData, made by tools/video/export_drone_data.py
/// from the approved film) and a capture mode: --drone-tour --track=ilyich, with --drone-path=CSV (tools/video/
/// bake_drone_path.py), --drone-lap=keys and --drone-log=FILE to override the data and record the camera.</summary>
public partial class RaceScene
{
    bool _droneTour;
    readonly List<(ServiceMarker marker, bool visible)> _droneMarkers = new();
    float _droneT = -1f;
    // In the game the first black card waits for a key (or a minute) before the flight starts (RaceScene.TrackPreview.cs).
    bool _droneWait;
    float _droneWaitT, _droneDelay;
    ulong _droneReleaseFrame;
    const float DroneWaitMax = 60f;
    float _droneIntroEnd, _dronePathEnd;
    float[][] _dronePath = Array.Empty<float[]>();        // per 1/60 s: eyeX eyeZ eyeAgl tgtX tgtZ tgtAgl fov clock
    float[][] _droneKeys = Array.Empty<float[]>();        // by road metre: metre azimuth dist height fov subjX subjZ blend
    float _droneStartT = -1f;
    bool _droneCamReady;
    Vector3 _droneEye, _droneTarget, _droneEyeVel, _droneTargetVel, _droneEyeOffset, _droneTargetOffset;
    float _droneFov = 58f, _droneLapEnd = -1f;
    StreamWriter _droneLog;
    int _droneLastSecond = -1;
    const float DronePathHz = 60f;
    float[] _droneEyeAlt = Array.Empty<float>(), _droneTargetAlt = Array.Empty<float>();   // absolute heights per path row
    float _droneCarY, _droneCarYVel, _droneBaseY, _droneBaseYVel, _droneLift, _droneLiftVel, _droneYaw, _dronePitch, _droneYawVel, _dronePitchVel;
    bool _droneAnglesReady;
    Vector3 _droneLogTarget, _droneLogEye, _droneCarPrev, _droneCarSpeed;

    static string DroneArg(string name) =>
        Array.Find(OS.GetCmdlineUserArgs(), a => a.StartsWith(name + "="))?[(name.Length + 1)..];

    static float[][] DroneRows(string path)
    {
        var rows = new List<float[]>();
        foreach (string line in File.ReadAllLines(path))
        {
            if (line.Length == 0 || line[0] == '#') continue;
            var parts = line.Split(new[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var row = new float[parts.Length];
            for (int i = 0; i < parts.Length; i++) row[i] = float.Parse(parts[i], CultureInfo.InvariantCulture);
            rows.Add(row);
        }
        return rows.ToArray();
    }

    void DroneLogLine(string line) { _droneLog?.WriteLine(line); }

    /// <summary>--drone-tour: the film as a capture (the HUD off, the world as the school builds it).</summary>
    void ConfigureDroneTour()
    {
        _cinematicTour = false;
        _hud.Visible = false;
        _scMarkNode.Visible = false;
        _sc.Beats = new() { DroneBeat() };
        _sc.BeatIndex = -1;
        _sc.NextBeat();
    }

    /// <summary>The tour's one beat (also the capture's).</summary>
    Beat DroneBeat() => B("", DroneSetup, DroneDrive, max: 600f);

    DroneFilm _film;

    /// <summary>The flight's data: the files given on the command line, else the game's own (DroneFilm, DroneTourData).</summary>
    void LoadDroneData()
    {
        _film = DroneFilm.For(_trackId) ?? throw new InvalidOperationException("no drone film for " + _trackId);
        string pathFile = DroneArg("--drone-path"), lapFile = DroneArg("--drone-lap"), logFile = DroneArg("--drone-log");
        if (logFile != null && _droneLog == null) _droneLog = new StreamWriter(logFile) { AutoFlush = true };
        _dronePath = pathFile != null ? DroneRows(pathFile) : DroneTourPath(_film);
        BuildDroneAltitudes();
        _droneKeys = lapFile != null ? DroneRows(lapFile) : _film.LapKeys;
        _dronePathEnd = (_dronePath.Length - 1) / DronePathHz;
        // The car starts at --drone-lap-at (the path runs on a few seconds: the hand-over to the chase drone).
        _droneIntroEnd = DroneFloat("--drone-lap-at", _film.LapAt);
    }

    static float DroneFloat(string arg, float fallback) =>
        DroneArg(arg) is string v ? float.Parse(v, CultureInfo.InvariantCulture) : fallback;

    /// <summary>The baked samples (every SampleStep s) back to 60 Hz rows: Catmull-Rom between them.</summary>
    static float[][] DroneTourPath(DroneFilm film)
    {
        var src = film.Path;
        if (src.Length < 2) return Array.Empty<float[]>();
        int frames = (int)MathF.Round((src.Length - 1) * film.SampleStep * DronePathHz) + 1;
        var rows = new float[frames][];
        for (int f = 0; f < frames; f++)
        {
            float x = Math.Min(f / DronePathHz / film.SampleStep, src.Length - 1);
            int i = Math.Min((int)x, src.Length - 2);
            float u = x - i;
            float[] p0 = src[Math.Max(i - 1, 0)], p1 = src[i], p2 = src[i + 1], p3 = src[Math.Min(i + 2, src.Length - 1)];
            var row = new float[p1.Length];
            for (int c = 0; c < row.Length; c++)
                row[c] = 0.5f * (2 * p1[c] + (-p0[c] + p2[c]) * u + (2 * p0[c] - 5 * p1[c] + 4 * p2[c] - p3[c]) * u * u
                    + (-p0[c] + 3 * p1[c] - 3 * p2[c] + p3[c]) * u * u * u);
            rows[f] = row;
        }
        return rows;
    }

    /// <summary>Who is in the world while the drone flies, and for the lap (the ploughs and the column would hold the car up).</summary>
    Actors DroneFlightWorld() => _trackId switch
    {
        "siberia" => Actors.School | Actors.Details | Actors.Jets | Actors.Ploughs | Actors.Column,
        "bonus" => Actors.School | Actors.Jets | Actors.Tanks | Actors.Btr | Actors.Kozlik | Actors.Details,
        "atomgrad" => Actors.Babushkas | Actors.Zone | Actors.Ploughs,       // the train, the lunokhod, the catfish and the mixers (no depot or mechanic icons)
        _ => Actors.School | Actors.Tractor | Actors.Slurry | Actors.Details,
    };
    Actors DroneLapWorld() => _trackId switch
    {
        "bonus" => Actors.School | Actors.Jets | Actors.Details,
        "siberia" => Actors.School | Actors.Details | Actors.Jets,
        "atomgrad" => Actors.Babushkas,                                       // a train or a mixer would hold the lap up
        _ => DroneFlightWorld(),
    };

    /// <summary>The beat's setup: the world as the film needs it, the actors staged ahead, the camera reset.</summary>
    void DroneSetup()
    {
        LoadDroneData();
        // The track's tour has the environment up already (music, silent engine, the car in its barn); a capture builds it here.
        bool preview = _scEnvironment;
        _droneDelay = preview ? 0f : _film.FilmDelay;      // a capture holds the card for FilmDelay seconds; the game held it already (until a key)
        if (!preview) ScFresh();
        _droneTour = true;
        ApplyWorld(DroneFlightWorld());
        foreach (var (marker, visible) in _droneMarkers) marker.Visible = visible;
        _droneMarkers.Clear();
        if (_trackId == "bonus")
            foreach (Node child in FindChildren("*", "", true, false))
                if (child is ServiceMarker marker)
                { _droneMarkers.Add((marker, marker.Visible)); marker.Hide(); }
        _tour = TrackTour.For(_trackId, _track.Center.Length);
        if (_tour.FromStartPos) { _car.Reset(_track.StartPos, _track.StartYaw); _cam.Snap(_car.Pos); }
        else ScRoll(0, 0, 0);
        _car.Neutral = false;
        _scRoad = 0;
        _tourHalf = false;
        _tourSlowed.Clear();
        _tourT0.Clear();
        _ap.Aggression = 1f;
        _cam.ShakeEnabled = false;
        _droneT = 0;
        _droneCamReady = _droneAnglesReady = false;
        _droneLapEnd = -1f;
        _droneStartT = -1f;
        _droneCued = new bool[_film.Cues.Length];
        _droneLapCued = new bool[_film.LapCues.Length];
        _scLightClock = ClockAt(0);
        _hud.PreviewCardBig = _film.CardBig;
        _hud.PreviewCardSmall = _film.CardSmall;
        _scWorkers = FindChild("KolkhozWorkers", true, false) as KolkhozWorkers;
        if (_scWorkers != null) _scWorkers.ScenicActive = true;
        _scBoat = FindChild("PartyOfficialRowing", true, false) as PartyRowboat;
        DroneStage(preview);
        _droneStopAfter = -1f;
        string stop = DroneArg("--drone-stop-after");
        if (stop != null) _droneStopAfter = float.Parse(stop, CultureInfo.InvariantCulture);
        DroneLogLine($"DRONE BEGIN frame={Engine.GetProcessFrames()} track={_trackId} start=({_track.StartPos.X:F2},{_track.StartPos.Y:F2},{_track.StartPos.Z:F2}) yaw={_track.StartYaw:F3} introEnd={_droneIntroEnd:F3} lap={_track.Center.Length}");
        if (!preview) GD.Print($"DRONE BEGIN frame={Engine.GetProcessFrames()} introEnd={_droneIntroEnd:F2}");
    }

    /// <summary>The actors' staging: the kolkhoz vehicles run on the ordinary race clocks (the school and the title hold them),
    /// and each film's actors are run ahead so they are at work where the drone passes.</summary>
    void DroneStage(bool inGame)
    {
        _droneTicks.Clear();
        if (_trackId == "ilyich")
            foreach (var actor in _actors)
            {
                if (actor.Who is not (Actors.Tractor or Actors.Slurry)) continue;
                _droneTicks.Add((actor, actor.Tick));
                if (actor.Who == Actors.Tractor) actor.Tick = (dt, _) => _tractorAll.Tick(dt, true, _bus);
                else actor.Tick = (dt, _) => _slurryAll.Tick(dt, true, _bus);
            }
        // A capture's world also runs while its card holds (FilmDelay), so its prerolls are that much shorter; in the game
        // the flight starts the moment the card is released, so the staging has to cover the card's hold too.
        float extra = inGame ? _film.FilmDelay : 0f;
        foreach (var (actor, seconds) in _film.Prerolls)
            Preroll(DroneFloat("--drone-preroll-" + actor, seconds) + extra, DroneActorTick(actor));
        // Cues before the film starts (negative film time) happen now, and their actors run on for as long as they would have.
        if (inGame)
            for (int c = 0; c < _film.Cues.Length; c++)
            {
                var (name, at) = _film.Cues[c];
                if (at >= 0) continue;
                _droneCued[c] = true;
                DroneCue(name);
                Preroll(-at, DroneActorTick(name));
            }
    }

    Action<float> DroneActorTick(string actor) => actor switch
    {
        "tractor" => dt => _tractorAll?.Tick(dt, true, null),
        "slurry" => dt => _slurryAll?.Tick(dt, true, null),
        "ploughs" => dt => _ploughsAll?.Tick(dt, null),
        "column" => dt => _columnAll?.Tick(dt, null),
        _ => _ => { },
    };

    // ------------------------------------------------------------ the waiting card (the game's tour)

    /// <summary>The tour opens on its black card, which waits: nothing is built or started until it is released.</summary>
    void StartDroneCardWait()
    {
        _film = DroneFilm.For(_trackId);
        _hud.PreviewCardBig = _film.CardBig;
        _hud.PreviewCardSmall = _film.CardSmall;
        _droneWait = true;
        _droneWaitT = 0;
    }

    /// <summary>The player pressed a key (or a minute has gone): the world is set up fresh and the flight starts.</summary>
    void ReleaseDroneCard()
    {
        if (!_droneWait) return;
        _droneWait = false;
        _droneReleaseFrame = Engine.GetProcessFrames();
        BeginEnvironmentTour();           // the fresh world, the music and the silent engine
        _sc.PreviewFirst();               // the flight's beat: DroneSetup
        PreviewLog();
    }

    /// <summary>Per frame while the card waits: a minute is long enough.</summary>
    void DroneCardTick(float dt)
    {
        if (!_droneWait) return;
        _droneWaitT += dt;
        if (_droneWaitT >= DroneWaitMax) ReleaseDroneCard();
    }

    /// <summary>Any key, mouse or pad button starts the flight (Esc still goes back: TourInput). True: the event was the card's.</summary>
    bool DroneCardInput(InputEvent e)
    {
        if (!_droneWait) return false;
        if (e is InputEventKey { Pressed: true, Keycode: Key.Escape }) return false;
        bool press = e is InputEventKey { Pressed: true, Echo: false } or InputEventMouseButton { Pressed: true }
            or InputEventJoypadButton { Pressed: true };
        if (press && _droneWaitT > 0.25f) ReleaseDroneCard();      // (not the very press that opened the tour)
        return press;
    }

    readonly List<(WorldActor actor, Action<float, bool> tick)> _droneTicks = new();
    bool[] _droneCued = Array.Empty<bool>(), _droneLapCued = Array.Empty<bool>();

    /// <summary>The tour is over or closed: the actors' own clocks back, the car at its start.</summary>
    void StopDroneTour()
    {
        if (!_droneTour) return;
        _droneTour = false;
        foreach (var (actor, tick) in _droneTicks) actor.Tick = tick;
        _droneTicks.Clear();
        foreach (var (marker, visible) in _droneMarkers) marker.Visible = visible;
        _droneMarkers.Clear();
        _car.Reset(_track.StartPos, _track.StartYaw);
        _car.Neutral = false;
    }

    const float DroneEngineVolume = 0.8f;       // the rally's own engine level (School.cs)
    float _droneStopAfter = -1f;

    static void Preroll(float seconds, Action<float> tick)
    {
        for (float age = 0; age < seconds; age += 1f / 60f) tick(1f / 60f);
    }

    /// <summary>A film's cue (DroneFilm.Cues, LapCues).</summary>
    void DroneCue(string name)
    {
        switch (name)
        {
            case "hunt": _scBoat?.CueSchoolShot(0.6f); break;      // the duck hunt's shot, for the drone's pass over the pond
            case "column": _columnAll?.CueSchoolCamp(); break;     // the volunteers set off from the middle of the camp
            case "train": _zoneAll?.CueSchoolTrain(); break;       // the goods train sets out for the level crossing
            case "fish": _zoneAll?.CueSchoolFish(); break;         // the catfish jumps as the drone passes the channel
            case "sortie":                                         // a MiK taxis out and takes off
                if (_jets != null) { _jets.Ploughs = null; _jets.CueSchoolSortie(); }
                break;
        }
    }

    /// <summary>The light clock: the film's day-to-night timelapse (and back) over the prelude, from the path file's last column.</summary>
    float ClockAt(float t)
    {
        if (_dronePath.Length == 0) return 1f;
        float f = Math.Clamp(t * DronePathHz, 0, _dronePath.Length - 1);
        int i = (int)f;
        int j = Math.Min(i + 1, _dronePath.Length - 1);
        return Mathf.Lerp(_dronePath[i][7], _dronePath[j][7], f - i);
    }

    /// <summary>The school's tips for the lap, by the road metre the car is at.</summary>
    string _droneLastCaption = "";

    string DroneLapCaptionAt(int metre)
    {
        foreach (var (from, to, text) in _film.LapCaptions)
            if (metre >= from && metre < to) return text;
        return "";
    }

    string DroneCaptionAt(float t)
    {
        foreach (var (mark, after, length, text) in _film.Captions)
        {
            float from = _film.Marks[mark] + after;
            if (t >= from && t < from + length) return text;
        }
        return "";
    }

    /// <summary>The black name card at the start (1 = opaque), fading out into the flight; 0 once it is gone.</summary>
    public float DroneCardAlpha => _film == null ? 0f : _droneWait ? 1f : !_droneTour ? 0f
        : _droneDelay == 0f ? Mathf.Clamp(1f - _droneT / 0.8f, 0f, 1f)      // in the game: held until a key, it fades as the flight starts
        : _droneT < _film.CardEnd - 0.8f ? 1f : Mathf.Clamp((_film.CardEnd - _droneT) / 0.8f, 0f, 1f);
    public bool DroneCardWaiting => _droneWait;

    bool DroneDrive(float t, float dt)
    {
        _droneT = t;
        float ft = t - _droneDelay;      // film time: the flight starts when the black card has held long enough
        int i = SchoolScript.Follow(_track, CarXZ, ref _scRoad);
        int lap = _track.Center.Length;
        if (i > lap * 2 / 5 && i < lap * 3 / 5) _tourHalf = true;
        int m = TourMetre(i);
        bool prelude = ft < _droneIntroEnd;
        _sc.Caption = DroneCaptionAt(ft);
        if (_sc.Caption == "" && !prelude && _droneStartT >= 0) _sc.Caption = DroneLapCaptionAt(m);
        if (_sc.Caption != _droneLastCaption) { _droneLastCaption = _sc.Caption; DroneLogLine($"DRONE CAPTION t={t:F2} metre={m} {_sc.Caption}"); if (OS.HasEnvironment("DRONEDBG")) GD.Print($"DRONE CAPTION t={t:F2} metre={m} {_sc.Caption}"); }
        _scLightClock = ClockAt(ft);
        // The player's engine stays silent until the car moves; then it fades in.
        _snd.Volume = prelude ? 0f : Mathf.MoveToward(_snd.Volume, DroneEngineVolume, 4f * dt);
        for (int c = 0; c < _film.Cues.Length; c++)
            if (!_droneCued[c] && ft >= DroneFloat("--drone-cue-" + _film.Cues[c].name, _film.Cues[c].at))
            {
                _droneCued[c] = true;
                DroneCue(_film.Cues[c].name);
            }
        if (prelude) ScStop();
        else
        {
            if (_droneStartT < 0)
            {
                _droneStartT = t;
                ApplyWorld(DroneLapWorld());      // (the ploughs and the column would hold the lap up)
                _car.SetControls(false, false, 0f, false);
                DroneLogLine($"DRONE MARK lap_start t={t:F3}");
            }
            _car.Clutch = false;
            TourDriveAt(m, t - _droneStartT, dt);
            for (int c = 0; c < _film.LapCues.Length; c++)
                if (!_droneLapCued[c] && m >= _film.LapCues[c].metre && m < _film.LapCues[c].metre + 300)
                {
                    _droneLapCued[c] = true;
                    DroneCue(_film.LapCues[c].name);
                }
            if (_droneLapEnd < 0 && _tourHalf && m >= lap - 12)
            {
                _droneLapEnd = t;
                DroneLogLine($"DRONE MARK lap_end t={t:F3}");
            }
        }
        DroneLog(t, m);
        // The lap's last seconds: the drone has the barn and the finish before the take ends.
        if (_droneStopAfter >= 0 && t >= _droneStopAfter) return true;
        return _droneLapEnd >= 0 && t >= _droneLapEnd + 5f;
    }

    void DroneLog(float t, int metre)
    {
        if (_droneLog == null) return;
        _droneLog.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"CAM {t:F4} {_droneLogEye.X:F3} {_droneLogEye.Y:F3} {_droneLogEye.Z:F3} {_droneLogTarget.X:F3} {_droneLogTarget.Y:F3} {_droneLogTarget.Z:F3} {_droneFov:F2} {_car.Pos.X:F2} {_car.Pos.Z:F2} {metre} {Kmh:F1} {(_scLightClock ?? 0):F3} {_droneLift:F2} {_droneCarY:F2} {_car.Pos.Y:F2}"));
        int second = (int)t;
        if (second == _droneLastSecond) return;
        _droneLastSecond = second;
        Vector3 boat = _scBoat?.BoatPosition ?? Vector3.Zero, duck = _scBoat?.DuckPosition ?? Vector3.Zero;
        Vector2 tractor = _tractorAll?.Position2D ?? Vector2.Zero, slurry = _slurryAll?.Position2D ?? Vector2.Zero;
        _droneLog.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"ACT {t:F1} tractor {tractor.X:F1} {tractor.Y:F1} slurry {slurry.X:F1} {slurry.Y:F1} bus 0 0 boat {boat.X:F1} {boat.Z:F1} duck {duck.X:F1} {duck.Z:F1} shots {_scBoat?.ShotsFired ?? 0}"));
        if (_ploughsAll != null)
        {
            string ploughs = string.Join(" ", System.Linq.Enumerable.Select(_ploughsAll.Positions, p => $"{p.X:F1} {p.Y:F1}"));
            // every jet that exists, as id:x,y,z (a cued sortie replaces the jets, so the ids move on)
            string jets = _jetsAll == null ? "" : string.Join(" ", System.Linq.Enumerable.Select(
                System.Linq.Enumerable.Where(System.Linq.Enumerable.Range(0, 24), k => _jetsAll.JetPosition(k) != null),
                k => { var jp = _jetsAll.JetPosition(k)!.Value; return $"{k}:{jp.X:F1},{jp.Y:F1},{jp.Z:F1}"; }));
            _droneLog.WriteLine(string.Create(CultureInfo.InvariantCulture, $"SIB {t:F1} ploughs {ploughs} jets {jets}"));
        }
    }

    /// <summary>The lap keys' row at a road metre: Catmull-Rom between the neighbouring keys (the azimuth unwrapped).</summary>
    float[] DroneKey(float metre)
    {
        var k = _droneKeys;
        if (k.Length == 0) return new float[] { metre, 200f, 26f, 14f, 58f, 0f, 0f, 0f };
        if (metre <= k[0][0]) return k[0];
        if (metre >= k[^1][0]) return k[^1];
        int b = 0;
        while (b + 2 < k.Length && metre >= k[b + 1][0]) b++;
        float[] p0 = k[Math.Max(b - 1, 0)], p1 = k[b], p2 = k[b + 1], p3 = k[Math.Min(b + 2, k.Length - 1)];
        float u = (metre - p1[0]) / (p2[0] - p1[0]);
        var row = new float[p1.Length];
        for (int c = 1; c < row.Length; c++)
        {
            float a0 = p0[c], a1 = p1[c], a2 = p2[c], a3 = p3[c];
            if (c == 1)
            {
                // Unwrap the compass angle so a key crossing 0/360 turns the short way.
                a0 = a1 + Mathf.Wrap(a0 - a1 + 180f, 0, 360) - 180f;
                a2 = a1 + Mathf.Wrap(a2 - a1 + 180f, 0, 360) - 180f;
                a3 = a2 + Mathf.Wrap(a3 - a2 + 180f, 0, 360) - 180f;
            }
            row[c] = 0.5f * (2 * a1 + (-a0 + a2) * u + (2 * a0 - 5 * a1 + 4 * a2 - a3) * u * u + (-a0 + 3 * a1 - 3 * a2 + a3) * u * u * u);
        }
        row[0] = metre;
        return row;
    }


    /// <summary>Gaussian low-pass of a 60 Hz series (sigma in seconds), the ends held by odd reflection.</summary>
    static float[] DroneSmooth(float[] a, float sigma)
    {
        int n = (int)(sigma * DronePathHz * 3), len = a.Length;
        var k = new float[2 * n + 1];
        float sum = 0;
        for (int i = -n; i <= n; i++) sum += k[i + n] = MathF.Exp(-0.5f * i * i / (sigma * DronePathHz * sigma * DronePathHz));
        var o = new float[len];
        for (int i = 0; i < len; i++)
        {
            float v = 0;
            for (int j = -n; j <= n; j++)
            {
                int m = i + j;
                float x = m < 0 ? 2 * a[0] - a[Math.Min(-m, len - 1)] : m >= len ? 2 * a[len - 1] - a[Math.Max(2 * (len - 1) - m, 0)] : a[m];
                v += x * k[j + n];
            }
            o[i] = v / sum;
        }
        return o;
    }

    /// <summary>The path's heights as a gimbal drone flies them: the ground-relative heights low-passed along the
    /// way, then kept above the ground (a smoothed margin), so the camera does not bob with every bump.</summary>
    void BuildDroneAltitudes()
    {
        int n = _dronePath.Length;
        float[] Build(int xc, int zc, int hc, float margin)
        {
            var ground = new float[n];
            var alt = new float[n];
            for (int i = 0; i < n; i++)
            {
                ground[i] = _track.MeshHeight(_dronePath[i][xc], _dronePath[i][zc]);
                alt[i] = ground[i] + _dronePath[i][hc];
            }
            alt = DroneSmooth(alt, 3.0f);
            for (int pass = 0; pass < 3; pass++)
            {
                for (int i = 0; i < n; i++) alt[i] = Math.Max(alt[i], ground[i] + margin);
                alt = DroneSmooth(alt, 1.5f);
            }
            for (int i = 0; i < n; i++) alt[i] = Math.Max(alt[i], ground[i] + margin * 0.6f);
            return alt;
        }
        _droneEyeAlt = Build(0, 1, 2, 5.5f);
        _droneTargetAlt = Build(3, 4, 5, 0.5f);
    }

    /// <summary>A critically damped scalar (the same spring as DroneDamp).</summary>
    static float DroneDampF(float current, float want, ref float velocity, float smoothTime, float dt)
    {
        float omega = 2f / smoothTime, x = omega * dt;
        float exp = 1f / (1f + x + 0.48f * x * x + 0.235f * x * x * x);
        float change = current - want;
        float temp = (velocity + omega * change) * dt;
        velocity = (velocity - omega * temp) * exp;
        return want + (change + temp) * exp;
    }

    /// <summary>Critically damped follow (a spring with no overshoot) that keeps its velocity: the hand-over from
    /// the flown path to the chase drone starts at the path's own speed, so the camera never dips or stops.</summary>
    static Vector3 DroneDamp(Vector3 current, Vector3 want, ref Vector3 velocity, float smoothTime, float dt)
    {
        float omega = 2f / smoothTime, x = omega * dt;
        float exp = 1f / (1f + x + 0.48f * x * x + 0.235f * x * x * x);
        Vector3 change = current - want;
        Vector3 temp = (velocity + omega * change) * dt;
        velocity = (velocity - omega * temp) * exp;
        return want + (change + temp) * exp;
    }

    /// <summary>The baked path's pose at t (eye, target, fov); false once the path has run out.</summary>
    bool DronePathPose(float t, out Vector3 eye, out Vector3 target, out float fov)
    {
        eye = target = Vector3.Zero;
        fov = 56f;
        if (_dronePath.Length == 0 || t > _dronePathEnd) return false;
        float f = Math.Clamp(t * DronePathHz, 0, _dronePath.Length - 1);
        int i = (int)f;
        int j = Math.Min(i + 1, _dronePath.Length - 1);
        float u = f - i;
        float[] a = _dronePath[i], b = _dronePath[j];
        float L(int c) => Mathf.Lerp(a[c], b[c], u);
        float AltAt(float[] alt) => Mathf.Lerp(alt[i], alt[j], u);
        eye = new Vector3(L(0), AltAt(_droneEyeAlt), L(1));
        target = new Vector3(L(3), AltAt(_droneTargetAlt), L(4));
        fov = L(6);
        return true;
    }

    void DroneTourCamera(float dt)
    {
        float t = Math.Max(_droneT - _droneDelay, 0);      // film time (the camera waits at the path's start while the card holds)
        bool onPath = DronePathPose(t, out Vector3 pathEye, out Vector3 pathTarget, out float pathFov);
        Vector3 eye, target;
        float fov;
        if (onPath && t < _droneIntroEnd)
        {
            // Flown straight off the baked path; the chase offsets wait at zero.
            eye = _droneEye = pathEye;
            target = _droneTarget = pathTarget;
            fov = _droneFov = pathFov;
            _droneEyeOffset = _droneTargetOffset = _droneEyeVel = _droneTargetVel = Vector3.Zero;
            _droneCamReady = true;
        }
        else
        {
            int lap = _track.Center.Length;
            int metre = TourMetre(SchoolScript.Follow(_track, CarXZ, ref _scRoad));
            if (!_tourHalf && metre > lap * 4 / 5) metre = 0;      // the start inside the barn, just short of the line
            var key = DroneKey(metre);
            // The gimbal does not ride the car's bumps and jumps. The look-at height follows a smoothed car height;
            // the drone's own altitude climbs ahead of rising ground (the ground the car and the drone's spot
            // will cross in the next seconds) and settles gently, so it never has to be lifted over a crest.
            Vector3 carVel = dt > 0 && _droneCamReady ? (_car.Pos - _droneCarPrev) / dt : Vector3.Zero;
            _droneCarSpeed = _droneCarSpeed.Lerp(carVel, 1f - MathF.Exp(-6f * dt));
            _droneCarPrev = _car.Pos;
            if (!_droneCamReady) { _droneCarY = _droneBaseY = _car.Pos.Y; _droneCarYVel = _droneBaseYVel = 0; }
            _droneCarY = DroneDampF(_droneCarY, _car.Pos.Y, ref _droneCarYVel, 0.7f, dt);
            Vector3 car = new(_car.Pos.X, _droneCarY + 1f, _car.Pos.Z);
            Vector3 subject = key[7] > 0 ? new Vector3(key[5], _track.MeshHeight(key[5], key[6]) + 2, key[6]) : car;
            Vector3 wantTarget = car + (subject - car) * key[7];
            float az = Mathf.DegToRad(key[1]);
            Vector3 offset = new(MathF.Cos(az) * key[2], key[3], MathF.Sin(az) * key[2]);
            float groundAhead = _car.Pos.Y;
            for (int secs = 0; secs <= 3; secs++)
            {
                Vector3 ahead = _car.Pos + _droneCarSpeed * secs;
                Vector3 spot = ahead + new Vector3(offset.X, 0, offset.Z);
                groundAhead = Math.Max(groundAhead, _track.MeshHeight(ahead.X, ahead.Z));
                groundAhead = Math.Max(groundAhead, _track.MeshHeight(spot.X, spot.Z) + 4f - key[3]);
            }
            _droneBaseY = DroneDampF(_droneBaseY, groundAhead, ref _droneBaseYVel, groundAhead > _droneBaseY ? 1.0f : 2.0f, dt);
            Vector3 wantEye = wantTarget + new Vector3(offset.X, 0, offset.Z);
            wantEye.Y = _droneBaseY + 1f + key[3];
            float wantFov = key[4];
            if (_dronePath.Length > 0)
            {
                // The path goes on in a straight line past its end. The chase is a smoothed offset from that
                // reference, so the hand-over starts at zero offset and zero extra speed: the drone keeps its
                // flying speed instead of a spring slowing it first (a spring on a moving target lags by speed x time).
                float tc = Math.Min(t, _dronePathEnd);
                DronePathPose(tc, out Vector3 refEye, out Vector3 refTarget, out float refFov);
                if (t > _dronePathEnd)
                {
                    DronePathPose(Math.Max(_dronePathEnd - 0.1f, 0f), out Vector3 e0, out Vector3 t0, out _);
                    refEye += (refEye - e0) / 0.1f * (t - _dronePathEnd);
                    refTarget += (refTarget - t0) / 0.1f * (t - _dronePathEnd);
                }
                float w = Mathf.SmoothStep(0, 1, Math.Clamp((t - _droneIntroEnd - DroneBlendDelay) / DroneBlendLength, 0, 1));
                _droneEyeOffset = DroneDamp(_droneEyeOffset, (wantEye - refEye) * w, ref _droneEyeVel, DroneEyeTime, dt);
                _droneTargetOffset = DroneDamp(_droneTargetOffset, (wantTarget - refTarget) * w, ref _droneTargetVel, DroneTargetTime, dt);
                _droneEye = refEye + _droneEyeOffset;
                _droneTarget = refTarget + _droneTargetOffset;
                _droneFov = Mathf.Lerp(_droneFov, Mathf.Lerp(refFov, wantFov, w), 1f - MathF.Exp(-1.2f * dt));
            }
            else
            {
                if (!_droneCamReady) { _droneEye = wantEye; _droneTarget = wantTarget; _droneCamReady = true; _droneFov = wantFov; }
                _droneEye = DroneDamp(_droneEye, wantEye, ref _droneEyeVel, DroneEyeTime, dt);
                _droneTarget = DroneDamp(_droneTarget, wantTarget, ref _droneTargetVel, DroneTargetTime, dt);
                _droneFov = Mathf.Lerp(_droneFov, wantFov, 1f - MathF.Exp(-1.2f * dt));
            }
            eye = _droneEye;
            target = _droneTarget;
            fov = _droneFov;
            // Clear the sightline over crests and the ground under the drone: the lift needed is smoothed,
            // rising a little faster than it falls (on the output, not the smoothed state).
            float need = 0;
            for (int s = 2; s <= 16; s++)
            {
                float v = s / 16f;
                Vector3 p = car.Lerp(eye, v);
                need = Math.Max(need, (_track.MeshHeight(p.X, p.Z) + 2.5f - p.Y) / v);
            }
            _droneLift = DroneDampF(_droneLift, need, ref _droneLiftVel, need > _droneLift ? 0.9f : 2.0f, dt);
            eye.Y += Math.Max(_droneLift, 0f);
        }
        // A gimbal: the look direction's yaw and pitch each follow their wanted angle through a soft spring,
        // and the horizon never rolls.
        Vector3 look = target - eye;
        float wantYaw = MathF.Atan2(look.X, look.Z), wantPitch = MathF.Atan2(look.Y, MathF.Sqrt(look.X * look.X + look.Z * look.Z));
        if (!_droneAnglesReady) { _droneYaw = wantYaw; _dronePitch = wantPitch; _droneYawVel = _dronePitchVel = 0; _droneAnglesReady = true; }
        _droneYaw = DroneDampF(_droneYaw, _droneYaw + Mathf.AngleDifference(_droneYaw, wantYaw), ref _droneYawVel, DroneYawTime, dt);
        _dronePitch = DroneDampF(_dronePitch, wantPitch, ref _dronePitchVel, DronePitchTime, dt);
        Vector3 dir = new(MathF.Sin(_droneYaw) * MathF.Cos(_dronePitch), MathF.Sin(_dronePitch), MathF.Cos(_droneYaw) * MathF.Cos(_dronePitch));
        _droneLogTarget = eye + dir * 40f;
        _droneLogEye = eye;
        _cam.Scenic(_car.Pos, eye - _car.Pos, dt);
        _cam.Fov = fov;
        _cam.Near = 0.2f;
        _cam.Far = 600f;
        _cam.GlobalTransform = new Transform3D(Basis.LookingAt(dir, Vector3.Up), eye);
        _cam.ListenAt(eye, dir);      // the sounds of the flight (a MiK on its take-off run, tractors) are heard from the lens, not from the parked car
    }

    const float DroneBlendDelay = 0.2f, DroneBlendLength = 3.0f, DroneEyeTime = 1.0f, DroneTargetTime = 0.55f, DroneYawTime = 0.4f, DronePitchTime = 0.7f;
}
