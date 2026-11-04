using System;
using Godot;

namespace CCCPRacing.Race;

/// <summary>Opt-in, uncut trailer originals: real rally physics under a continuous landscape dolly.
/// --trailer-v2=kolkhoz|farm|siberia with the matching --track. Ordinary play is unaffected.</summary>
public partial class RaceScene
{
    bool _trailerV2, _trailerV2CameraReady, _trailerV2JetCued;
    bool _trailerV2Farm;
    float _trailerV2Turn = -1f, _trailerV2Time;
    int _trailerV2LastLog = -1, _trailerV2Jet = -1;
    Vector3 _trailerV2Eye, _trailerV2Target;
    /// <summary>Road metre at which the MiG's sortie is cued. Trailer v3 (--trailer-jet-at=M) moves it
    /// earlier so the jet is off the runway before the car reaches it; v2 used 560.</summary>
    int _trailerV2JetAt = 560;

    void ConfigureTrailerV2()
    {
        _trailerV2 = true;
        _trailerV2Farm = Array.Exists(CmdArgs(), a => a == "--trailer-v2=farm");
        string jetAt = Array.Find(CmdArgs(), a => a.StartsWith("--trailer-jet-at="));
        if (jetAt != null) _trailerV2JetAt = int.Parse(jetAt["--trailer-jet-at=".Length..]);
        _cinematicTour = false;
        _hud.Visible = false;
        _scMarkNode.Visible = false;
        _sc.Beats = new() { B("", () =>
        {
            ScFresh();
            bool winter = _trackId == "siberia";
            ApplyWorld(winter ? Actors.Details | Actors.Jets : Actors.Tractor | Actors.Slurry | Actors.Bus | Actors.Drunks);
            // Begin already charging towards a proven handbrake bend; no parked lead-in.
            _scRoad = winter ? 295 : _trailerV2Farm ? 0 : 744;
            ScRoll(_scRoad, 0, winter ? 42 : _trailerV2Farm ? 90 : 66);
            _ap.Aggression = 1.18f;
            _trailerV2Time = 0;
            _trailerV2Turn = -1;
            _trailerV2LastLog = -1;
            _trailerV2CameraReady = _trailerV2JetCued = false;
            _scLightClock = winter ? 1.2f : 2.65f;
            _cam.ShakeEnabled = false;
            if (_trailerV2Farm)
            {
                // Stage the ordinary departure before the car arrives; no teleported vehicle.
                for (float age = 0; age < 70f && !_slurryAll.CrossingBarnRoad; age += 1f / 60f)
                    _slurryAll.Tick(1f / 60f, true);
                for (float age = 0; age < 3.5f; age += 1f / 60f) _slurryAll.Tick(1f / 60f, true);
            }
            // The school state normally holds farm departures. Only these capture actors run
            // their ordinary race clocks, allowing the real doors and traffic logic to play.
            foreach (var actor in _actors)
            {
                if (actor.Who == Actors.Tractor)
                    actor.Tick = (dt, _) => _tractorAll.Tick(dt, true, _bus);
                else if (actor.Who == Actors.Slurry)
                    actor.Tick = (dt, _) => _slurryAll.Tick(dt, true, _bus);
            }
            GD.Print($"TRAILER_V2 BEGIN {_trackId} frame={Engine.GetProcessFrames()}");
        }, TrailerV2Drive, max: 47f) };
        _sc.BeatIndex = -1;
        _sc.NextBeat();
    }

    bool TrailerV2Drive(float t, float dt)
    {
        _trailerV2Time = t;
        int metre = SchoolScript.Follow(_track, CarXZ, ref _scRoad);
        bool winter = _trackId == "siberia";
        if (winter && metre < 348)
        {
            // The established Siberia lesson: short handbrake pull, then catch on throttle.
            if (metre < 311 && _trailerV2Turn < 0) ScCruise(38f, SlideSteerNow);
            else
            {
                if (_trailerV2Turn < 0) _trailerV2Turn = t;
                float u = t - _trailerV2Turn;
                _car.SetControls(u > .45f ? 1f : 0f, Kmh > 44 ? .3f : 0f,
                    SlideSteerNow, u < .25f, false);
            }
        }
        else if (!winter && metre >= 744 && metre < 834)
            SlideDrive("hairpin", 804, metre < 778 ? 66f : 50f, t, ref _trailerV2Turn);
        else
        {
            _car.Clutch = false;
            _ap.Drive(_car, dt);
            // The tested Ilyich hill and barn run are full-throttle stretches.
            if (!winter && metre >= 834 && metre < 930) ScCruise(110f, SlideSteerNow);
            else if (!winter && metre >= 985) ScCruise(110f, SlideSteerNow);
        }
        // Cue the actual taxi/runway/takeoff sequence while the car climbs past the radar.
        if (winter && !_trailerV2JetCued && metre >= _trailerV2JetAt)
        {
            _trailerV2JetCued = true;
            _jets.Ploughs = null;
            _jets.CueSchoolSortie();
            _trailerV2Jet = _jets.NextJet;
            GD.Print($"TRAILER_V2 SORTIE t={t:F3} metre={metre}");
        }
        if (winter) _scLightClock = Mathf.Lerp(1.2f, 4.65f, SiberiaProgress(6f, 35f, t));
        if ((int)t != _trailerV2LastLog)
        {
            _trailerV2LastLog = (int)t;
            GD.Print($"TRAILER_V2 SAMPLE t={t:F3} frame={Engine.GetProcessFrames()} metre={metre} kmh={Kmh:F1} clock={_scLightClock:F2}");
        }
        return t >= (_trailerV2Farm ? 12f : 44f);
    }

    void TrailerV2Camera(float dt)
    {
        float t = _trailerV2Time;
        bool winter = _trackId == "siberia";
        Vector3 car = _car.Pos + Vector3.Up;
        Vector3 subject;
        Vector3 offset;
        if (winter)
        {
            // East-side travelling crane: the switchbacks, radar, then the runway unfold
            // beyond the car. The subject remains near enough for the rally to read.
            float runway = SiberiaProgress(435, 635, _scRoad);
            subject = Ground(new Vector2(270, 156), 5).Lerp(Ground(new Vector2(220, 48), 4), runway);
            if (_trailerV2JetCued && _jets.JetPosition(_trailerV2Jet) is Vector3 jet)
                subject = subject.Lerp(jet, .25f * runway);
            offset = new Vector3(37f - 14f * runway, 32f + 12f * runway, 22f - 3f * runway);
            offset = offset.Rotated(Vector3.Up, -.15f + .007f * t);
        }
        else
        {
            float barn = SiberiaProgress(835, 970, _scRoad);
            // Follow the village hairpin uphill to the joined barns and dormitory.
            subject = Ground(new Vector2(54, 183), 3).Lerp(Ground(new Vector2(146, 39), 4), barn);
            if (_scRoad < 300) subject = Ground(_track.Center[SchoolScript.Wrap(_track, _scRoad + 38)], 2);
            offset = new Vector3(-26f + 10f * MathF.Sin(t * .04f), 28f + 6f * MathF.Sin(t * .035f), 30f);
            offset = offset.Rotated(Vector3.Up, -.003f * t);
            if (_trailerV2Farm)
            {
                subject = Ground(new Vector2(169, 32), 3);
                offset = new Vector3(10 - t, 24 + t * .5f, 31);
            }
        }
        // Compose the landscape and car together rather than locking on the bonnet.
        Vector3 target = car + (subject - car).LimitLength(35f) * .40f;
        Vector3 eye = target + offset;
        float blend = _trailerV2CameraReady ? 1f - MathF.Exp(-2.5f * dt) : 1f;
        _trailerV2Target = _trailerV2Target.Lerp(target, blend);
        _trailerV2Eye = _trailerV2Eye.Lerp(eye, blend);
        // Clear the sightline over hill crests and roofs without terrain clipping.
        for (int i = 2; i <= 16; i++)
        {
            float u = i / 16f;
            Vector3 p = car.Lerp(_trailerV2Eye, u);
            _trailerV2Eye.Y += Math.Max(0, (_track.MeshHeight(p.X, p.Z) + 2.5f - p.Y) / u);
        }
        _trailerV2CameraReady = true;
        _cam.Scenic(_car.Pos, _trailerV2Eye - _car.Pos, dt);
        _cam.Fov = 58f;
        _cam.Near = .2f;
        _cam.Far = 600;
        _cam.GlobalTransform = new Transform3D(Basis.LookingAt(_trailerV2Target - _trailerV2Eye, Vector3.Up), _trailerV2Eye);
    }
}
