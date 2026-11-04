using System;
using System.Collections.Generic;
using CCCPRacing.Track;
using CCCPRacing.Audio;
using Godot;

namespace CCCPRacing.Race;

public partial class RaceScene
{
    bool _scEnvironment;
    float _scEnvironmentStart;
    float? _scLightClock;
    Func<(Vector3 target, Vector3 offset)> _scScenic;
    Vector3 _scScenicTarget, _scScenicOffset;
    PartyRowboat _scBoat;
    KolkhozWorkers _scWorkers;
    float _scEngineVolume;
    RadioMusic _scMusic;

    void BeginEnvironmentTour()
    {
        if (_scEnvironment) return;
        ScFresh();
        ApplyWorld(_trackId == "siberia"
            ? Actors.Drunks | Actors.Details | Actors.Ploughs | Actors.Column | Actors.Jets
            : Actors.Tractor | Actors.Slurry | Actors.Bus | Actors.Drunks);
        _car.Reset(_track.StartPos, _track.StartYaw);
        _car.Neutral = false;
        ScStop();
        _cam.Snap(_car.Pos);
        _scBoat = FindChild("PartyOfficialRowing", true, false) as PartyRowboat;
        _scWorkers = FindChild("KolkhozWorkers", true, false) as KolkhozWorkers;
        if (_scWorkers != null) _scWorkers.ScenicActive = true;
        _scEngineVolume = _snd.Volume;
        _snd.Volume = 0;
        _carRadio.Suspend(true);
        if (_scMusic == null)
        {
            _scMusic = new RadioMusic
            {
                Name = "EnvironmentMusic", Subtle = true, VolumeDb = -6f,
                TrackPath = _trackId == "siberia" ? "res://assets/music/radio/Moskowaltz.mp3"
                    : "res://assets/music/radio/Kevät saapuu Moskovaan.mp3",
            };
            AddChild(_scMusic);
        }
        _scMusic.VolumeDb = -6f;
        _scMusic.Play(0f, immediate: true); // Preserve the recording's opening note from the title card.
        _scEnvironmentStart = _sc.Total;
        _scEnvironment = true;
        _sc.ShowDelay = 0;
    }

    void EndEnvironmentTour()
    {
        _scScenic = null;
        if (!_scEnvironment) return;
        _scEnvironment = false;
        _scMusic.Suspend(false);
        _scMusic.Stop();
        if (_scWorkers != null) _scWorkers.ScenicActive = false;
        _scWorkers = null;
        // Returning to the title resumes its theme, not the car's station.
        _carRadio.Suspend(_state == State.Title);
        _snd.Volume = _scEngineVolume;
    }

    void EnvironmentMusic(float dt, bool paused)
    {
        if (!_scEnvironment) return;
        _scMusic.Suspend(paused);
        bool shot = _sc.Current.step.Id == "kolkhoz_dacha" && _sc.BeatIndex == 1 && _scBoat != null
            && (_scBoat.NextShotIn < 0.65f || _scBoat.SinceShot < 1.2f);
        shot |= _trackId == "siberia" && _sc.Current.step.Id is ("siberia_base" or "siberia_night") && _sc.Once;
        _scMusic.VolumeDb = Mathf.MoveToward(_scMusic.VolumeDb, shot ? -20f : -6f, (shot ? 30f : 7f) * dt);
    }

    Vector3 KolkhozProp(string type, Vector2 near)
    {
        Vector3 best = Ground(near, 2);
        float distance = float.MaxValue;
        foreach (var p in _track.Props)
        {
            if (p.Type != type) continue;
            float d = new Vector2(p.Position.X, p.Position.Z).DistanceSquaredTo(near);
            if (d < distance) { best = p.Position + Vector3.Up * 2; distance = d; }
        }
        return best;
    }

    BusStop KolkhozVillageStop()
    {
        Vector3 church = KolkhozProp("church", new(49, 191));
        BusStop best = _track.BusStops[0];
        foreach (var stop in _track.BusStops)
            if (stop.Position.DistanceSquaredTo(new(church.X, church.Z)) < best.Position.DistanceSquaredTo(new(church.X, church.Z))) best = stop;
        return best;
    }

    /// <summary>Travel between shots by pulling back, then dolly/orbit around the live subject.</summary>
    Beat ScenicShot(string caption, Func<Vector3> subject, Vector3 offsetFrom, Vector3 offsetTo,
        float duration, string name, Action setup = null, Action<float> action = null, bool captionOnArrival = false, bool directTravel = false, bool gentleTravel = false)
    {
        Vector3 from = Vector3.Zero, previousOffset = Vector3.Zero;
        float travel = 2;
        return B(captionOnArrival ? "" : caption, () =>
        {
            BeginEnvironmentTour();
            from = _scScenic != null ? _scScenicTarget : _car.Pos;
            previousOffset = _scScenic != null ? _scScenicOffset : _cam.Offset;
            setup?.Invoke();
            float distance = from.DistanceTo(subject());
            travel = Math.Clamp(distance / 45f, 1.2f, 3.2f);
            _scScenic = () =>
            {
                float t = _sc.Sim;
                if (gentleTravel)
                {
                    // Spend the entire clip on a shallow glide without a pullback or an orbit.
                    float glide = SiberiaProgress(0, duration, t);
                    return (from.Lerp(subject(), glide), previousOffset.Lerp(offsetTo, glide));
                }
                float arriving = directTravel ? Math.Clamp(t / travel, 0, 1) : SiberiaProgress(0, travel, t);
                float dolly = SiberiaProgress(travel, duration, t);
                Vector3 wantedOffset = offsetFrom.Lerp(offsetTo, dolly);
                float pullback = directTravel ? 0 : MathF.Sin(MathF.PI * Math.Clamp(t / travel, 0, 1)) * Math.Min(55, distance * 0.22f);
                Vector3 offset = previousOffset.Lerp(wantedOffset, arriving) + new Vector3(0, pullback, pullback * 0.55f);
                return (from.Lerp(subject(), arriving), offset);
            };
            ShotSoon(name, Math.Max(travel + 1, duration - 1));
        }, (t, _) =>
        {
            // The camera follows the cinematic target with smoothing: let it settle after travel.
            if (captionOnArrival && t >= travel + 0.8f && string.IsNullOrEmpty(_sc.Caption))
            {
                _sc.Caption = caption;
                if (SchoolDbg) GD.Print($"school: {name} caption after camera arrival at {t:F1} s (travel {travel:F1} s)");
            }
            action?.Invoke(t);
            return t >= duration;
        }, max: duration + 1);
    }

    (Vector3 target, Vector3 offset) KolkhozOverview(float progress = 0, float finalTurn = 0)
    {
        float size = _track.WorldSize;
        // Look west from the course towards the village, at a 32-degree elevation.
        Vector3 village = KolkhozProp("church", new(49, 191));
        Vector3 target = Ground(new(village.X + 45f, village.Z + 6f), 18);
        // Orbit towards the illuminated dacha, then finish looking southeast during dawn.
        target = target.Lerp(Ground(PartyDacha.Site + new Vector2(-20, 0), 6), Mathf.SmoothStep(0, 1, progress));
        float elevation = Mathf.DegToRad(32f);
        Vector3 direction = new Vector3(MathF.Cos(elevation), MathF.Sin(elevation), 0)
            .Rotated(Vector3.Up, -MathF.PI * progress - Mathf.Pi * 0.25f * finalTurn);
        Basis view = Basis.LookingAt(-direction, Vector3.Up);
        float aspect = GetViewport().GetVisibleRect().Size.Aspect();
        float tan = MathF.Tan(Mathf.DegToRad(_cam.Fov * 0.5f));
        float distance = 150;
        // Keep every screen corner over terrain, including the wider far edge of the view.
        // Leave room for hills and trees at the map border rather than exposing the void.
        for (int pass = 0; pass < 80; pass++)
        {
            bool inside = true;
            Vector3 eye = target + direction * distance;
            foreach (float x in new[] { -1f, 1f })
            foreach (float y in new[] { -1f, 1f })
            {
                Vector3 ray = view * new Vector3(x * tan * aspect, y * tan, -1);
                Vector3 ground = eye + ray * ((target.Y - eye.Y) / ray.Y);
                inside &= ground.X >= 10 && ground.Z >= 10 && ground.X <= size - 10 && ground.Z <= size - 10;
            }
            if (inside) break;
            distance *= 0.97f;
        }
        return (target, direction * distance);
    }

    Beat KolkhozDayPhase(string caption, float from, float to, string name)
    {
        // Double the orbit's timeline after the arrival, preserving the full village-to-dacha arc.
        float elapsed = name switch { "sunset" => 10.8f, "dusk" => 20.8f, "night" => 30.8f, "dawn" => 40.8f, _ => 0 };
        float duration = name == "day" ? 10.8f : 10f;
        return B(caption, () =>
        {
            BeginEnvironmentTour();
            Vector3 startTarget = _scScenic != null ? _scScenicTarget : _car.Pos;
            Vector3 startOffset = _scScenic != null ? _scScenicOffset : _cam.Offset;
            _scLightClock = from;
            _scScenic = () =>
            {
                float progress = Math.Clamp((elapsed + _sc.Sim - 3.2f) / 37.6f, 0, 1);
                float finalTurn = name == "dawn" ? SiberiaProgress(0, duration, _sc.Sim) : 0;
                var view = KolkhozOverview(progress, finalTurn);
                float arrival = name == "day" ? SiberiaProgress(0, 3.2f, _sc.Sim) : 1;
                return (startTarget.Lerp(view.target, arrival), startOffset.Lerp(view.offset, arrival));
            };
            ShotSoon(name, duration - 1);
        }, (t, _) =>
        {
            _scLightClock = Mathf.Lerp(from, to, SiberiaProgress(0, 6, t));
            return t >= duration;
        }, max: duration + 1);
    }

    List<Beat> KolkhozVillageBeats()
    {
        Vector3 startTarget = Vector3.Zero, startEye = Vector3.Zero;
        Vector3 church = KolkhozProp("church", new(49, 191));
        Vector3 endEye = church + new Vector3(10, 19, 25);
        // One fourteen-second path crosses the caption boundary at full travel speed.
        // Its southern control points keep the camera approaching the church from the same side.
        static Vector3 Curve(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float u)
        {
            float v = 1 - u;
            return a * (v * v * v) + b * (3 * v * v * u) + c * (3 * v * u * u) + d * (u * u * u);
        }
        Beat Approach(bool grain) => B(grain ? "THE CHURCH NOW STORES GRAIN." : "", () =>
        {
            BeginEnvironmentTour();
            if (!grain)
            {
                startTarget = _scScenic != null ? _scScenicTarget : _car.Pos;
                startEye = startTarget + (_scScenic != null ? _scScenicOffset : _cam.Offset);
            }
            _scScenic = () =>
            {
                float u = SiberiaProgress(0, 14, _sc.Sim + (grain ? 7 : 0));
                Vector3 target = Curve(startTarget, church + Vector3.Back * 6, church + Vector3.Back * 3, church, u);
                Vector3 eye = Curve(startEye, Ground(new(125, 270), 38), Ground(new(48, 255), 30), endEye, u);
                return (target, eye - target);
            };
            ShotSoon(grain ? "grain_store" : "village", 6);
        }, (t, _) =>
        {
            if (!grain && t >= 5f) _sc.Caption = "WELCOME TO THE VILLAGE.";
            if (grain && t > 4) _sc.Caption = "THE HARVEST IS NOW SACRED.";
            return t >= 7;
        }, max: 8);

        return new()
        {
            Approach(false),
            Approach(true),
            ScenicShot("THE BUS RUNS ON A TIMETABLE.", () => Ground(KolkhozVillageStop().Position, 1),
                new(-8, 25, 30), new(7, 20, 26), 10, "bus_stop",
                () => _bus?.Warp(SchoolScript.Wrap(_track, KolkhozVillageStop().Index + 35)),
                t => { if (t > 5) _sc.Caption = "KEPT IN A DIFFERENT TIME ZONE."; }),
            B("ALL ABOARD. EVENTUALLY.", () =>
            {
                Vector3 target = _scScenicTarget, offset = _scScenicOffset;
                _scScenic = () => (target, offset);
                ShotSoon("bus", 4);
            }, (t, _) => { ScStop(); return t >= 6; }),
        };
    }

    List<Beat> KolkhozBeats(string id) => id switch
    {
        "kolkhoz_farm" => new()
        {
            ScenicShot("OUR FINISH LINE IS A BARN.", () => KolkhozProp("startbarn", new(81, 60)).Lerp(_car.Pos,
                    SiberiaProgress(9, 11, _sc.Total - _scEnvironmentStart)),
                new(-22, 15, 12), new(0, 31, 23), 14, "barn"),
            ScenicShot("BARN. SILO. FIVE-YEAR PLAN.", () => Ground(new(148, 37), 3),
                new(-14, 28, 32), new(-8, 26, 32), 8, "grain_yard", gentleTravel: true),
            ScenicShot("THEY DO THE HEAVY LIFTING.", () => Ground(new(151, 38), 1.5f),
                new(-8, 26, 32), new(-6, 26, 32), 6, "loaders", gentleTravel: true),
        },
        "kolkhoz_dacha" => new()
        {
            ScenicShot("THE PARTY HOSTS A DEAR FRIEND.", () => Ground(PartyDacha.Site, 3),
                new(-18, 27, 33), new(-12, 20, 27), 12, "dacha", setup: () => _scLightClock = 2.5f,
                action: t => { if (t > 8) _sc.Caption = "A VIP FROM OUR LITTLE BROTHER."; }, captionOnArrival: true),
            ScenicShot("FRIENDSHIP. COOPERATION.", () => _scBoat != null
                ? _scBoat.BoatPosition.Lerp(_scBoat.DuckPosition, 0.5f) + Vector3.Up : Ground(new(220, 210), 1),
                new(-9, 22, 26), new(8, 18, 23), 12, "hunt", () =>
                {
                    _scBoat?.CueSchoolShot(6.5f);
                    _sc.T1 = _scBoat?.ShotsFired ?? 0;
                }, t =>
                {
                    if (t > 3.2f && !_sc.Once) _sc.Caption = "MUTUAL ASSISTANCE. MORE VODKA.";
                    if (_scBoat == null || _scBoat.ShotsFired <= _sc.T1 || _sc.Once) return;
                    _sc.Once = true;
                    _sc.Caption = "A WARNING SHOT. TO THE FISH.";
                    ShotSoon("miss", 0.28f);
                    if (SchoolDbg) GD.Print($"school: kolkhoz hunt miss shown, music {_scMusic.VolumeDb:F1} dB, car speed {_car.Speed:F2}");
                }, captionOnArrival: true),
            ScenicShot("THE DUCK HAS IMMUNITY.", () => _scBoat?.DuckPosition ?? Ground(new(220, 216), 1),
                new(7, 13, 17), new(-3, 11, 14), 4, "duck"),
        },
        "kolkhoz_fields" => new()
        {
            ScenicShot("OUR FIELDS. COLLECTIVE GLORY.", () => Ground(new(153, 111), 1),
                new(-10, 38, 42), new(10, 29, 33), 7, "fields",
                setup: () => _scLightClock = null, captionOnArrival: true, directTravel: true),
            ScenicShot("HARD WORK. SAME PAY.", () => Ground(new(126, 112), 1.4f),
                new(10, 29, 33), new(8, 29, 33), 6, "field_workers", gentleTravel: true),
            ScenicShot("THE WORKERS FISH AFTER WORK.", () => Ground(new(244, 222), 1.2f),
                new(0, 16, -30), new(0, 14, -26), 7, "fishermen", setup: () => _scLightClock = null, captionOnArrival: true),
            ScenicShot("THE TRACTORIST HAD HIS 300 GRAMS.", () => Ground(_tractor?.Position2D ?? new(205, 74), 1.5f),
                new(-13, 21, 25), new(9, 16, 21), 8, "plough"),
            ScenicShot("ORGANIC PERFUME. STATE APPROVED.", () => Ground(_slurry?.Position2D ?? new(168, 222), 1.5f),
                new(-13, 23, 29), new(10, 18, 25), 8, "slurry"),
        },
        "kolkhoz_village" => KolkhozVillageBeats(),
        "kolkhoz_daynight" => new()
        {
            KolkhozDayPhase("THE LIGHT FOLLOWS YOUR LAPS.", 1, 1, "day"),
            KolkhozDayPhase("LAPS 3-4: SUNSET.", 1, 3, "sunset"),
            KolkhozDayPhase("LAPS 5-6: DUSK. LIGHTS ON.", 3, 5, "dusk"),
            KolkhozDayPhase("LAPS 7-10: NIGHT.", 5, 8, "night"),
            KolkhozDayPhase("LAPS 11-12: DAWN. DAY RETURNS.", 8, 11.8f, "dawn"),
        },
        _ => new(),
    };
}
