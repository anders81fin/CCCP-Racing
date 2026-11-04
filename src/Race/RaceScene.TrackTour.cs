using System;
using System.Collections.Generic;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// The rally schools' lap with tips (TrackTour: each track's parts, landmarks and driving as data): one
/// beat per part, the autopilot round the lap, the part's captions and landmarks by road metre, the
/// techniques where the track asks for them.
/// </summary>
public partial class RaceScene
{
    TrackTour _tour;
    bool _tourHalf;                                        // past the middle of the lap: metres on from the line count on from the lap's length
    readonly Dictionary<TourZone, float> _tourT0 = new();   // a technique's start in this part (its slow motion)
    readonly HashSet<TourZone> _tourSlowed = new();        // a stretch's slow motion, once a tour
    TourLandmark _tourShown;

    /// <summary>A tour part's beat, if this step is one of the track's tour.</summary>
    List<Beat> TourBeats(string id)
    {
        _tour ??= TrackTour.For(_trackId, _track.Center.Length);
        if (_tour?.Part(id) is not TourPart part) return null;
        return new() { TourPartBeat(part, Array.IndexOf(_tour.Parts, part) == 0) };
    }

    /// <summary>The road metre along the tour: past half the lap, metres beyond the line count on.</summary>
    int TourMetre(int i) => _tourHalf && i < _track.Center.Length * 2 / 5 ? i + _track.Center.Length : i;

    Vector3? TourAt(TourSpot? spot)
    {
        if (spot is not TourSpot s) return null;
        if (s.Mechanic) return _drunks == null ? null : Ground(_drunks.MechanicPosition, s.Up);
        if (s.At is Vector2 at) return Ground(at, s.Up);
        return Ground(SchoolScript.RoadPoint(_track, SchoolScript.Wrap(_track, s.Metre), s.Lateral), s.Up);
    }

    static T InStretch<T>(T[] list, int m) where T : class
    {
        if (list != null)
            foreach (var x in list)
                if (x switch { TourCaption c => m >= c.From && m < c.To, TourLandmark l => m >= l.From && m < l.To, _ => false }) return x;
        return null;
    }

    static string TourCaption(TourPart part, int m) => InStretch(part.Captions, m)?.Text ?? part.Caption;

    Beat TourPartBeat(TourPart part, bool first)
    {
        var tour = _tour;
        TourLandmark Landmark() => InStretch(part.Landmarks, TourMetre(_scRoad));
        return B(part.Caption, () =>
        {
            if (first)
            {
                ScFresh();
                if (tour.FromStartPos)
                {
                    _car.Reset(_track.StartPos, _track.StartYaw);
                    _cam.Snap(_car.Pos);
                }
                else ScRoll(0, 0, 0);
                _scRoad = 0;
                if (tour.Details) ApplyWorld(Actors.School | Actors.Details);
                _tourHalf = false;
                _tourSlowed.Clear();
                ShotSoon("start_straight", 0.6f);
            }
            else
            {
                // A knock on the way is no lesson here: the engine whole again for each part.
                _health.Set(1f);
                ApplyTrainingPower();
                _carVis.Damage?.Reset();
            }
            _tourT0.Clear();
            _tourShown = null;
            _scLook = () => TourAt(Landmark()?.Look);
            // The caption where the car is, already over the pause before it drives.
            int road = _scRoad;
            _sc.Caption = TourCaption(part, TourMetre(SchoolScript.Follow(_track, CarXZ, ref road)));
        }, (t, dt) =>
        {
            int i = SchoolScript.Follow(_track, CarXZ, ref _scRoad);
            int lap = _track.Center.Length;
            if (i > lap * 2 / 5 && i < lap * 3 / 5) _tourHalf = true;     // passing the middle (a start in the barn is just short of the line)
            int m = TourMetre(i);
            _sc.Caption = TourCaption(part, m);
            var shown = Landmark();
            if (shown != null && shown != _tourShown) ShotSoon(shown.Shot, 0.6f);
            _tourShown = shown;
            TourDriveAt(m, t, dt);
            if (SchoolDbg && i != _scDbgS && i % 5 == 0) { _scDbgS = i; GD.Print($"school: tour metre {i} {Kmh:F0} km/h lat {LateralNow()} up {_car.Up.Y:F2} slip {SlipDeg(BendSign(i)):F0} last {_car.LastTechnique}"); }
            int to = part.To < 0 ? _track.Center.Length : part.To;
            return m >= to && m < to + tour.EndSlack;
        }, SchoolBox.None, () =>
        {
            var l = Landmark();
            return TourAt(l?.Arrow) is Vector3 p && (!l.InFrame || WellInFrame(p)) ? p : null;
        }, tour.Speed, tour.MaxTime);
    }

    /// <summary>The tour's driving at road metre m: a technique in its bend, else the autopilot and what the stretch asks over it.</summary>
    void TourDriveAt(int m, float t, float dt)
    {
        foreach (var z in _tour.Zones)
            if (z.Kind == TourDrive.Technique && m >= z.From && m < z.To)
            {
                float t0 = _tourT0.TryGetValue(z, out float s) ? s : -1f;
                bool was = t0 >= 0;
                float kmh = z.BrakeAt != int.MinValue && m < z.BrakeAt ? z.ApproachKmh : z.Kmh;
                SlideDrive(z.Tech, z.Entry, kmh, t, ref t0);
                _tourT0[z] = t0;
                if (!was && t0 >= 0 && z.SlowMo > 0) SlowMo(z.SlowMo);
                return;
            }
        _ap.Drive(_car, dt);
        foreach (var z in _tour.Zones)
        {
            if (z.Kind == TourDrive.Technique || m < z.From || m >= z.To) continue;
            switch (z.Kind)
            {
                case TourDrive.Cruise:
                    ScCruise(z.Kmh, SlideSteerNow);
                    break;
                case TourDrive.Line:
                    _car.SetControls(_car.Throttle, _car.Brake, SchoolScript.SlideSteer(_car, _track, ref _scRoad, z.Lateral), false, false);
                    break;
                case TourDrive.PumpBrake:
                    if (Kmh > z.Kmh) _car.SetControls(0f, t % 0.32f < 0.20f ? 0.65f : 0f, SlideSteerNow, false, false);
                    else ScCruise(z.CrawlKmh, SlideSteerNow);
                    break;
            }
            if (z.SlowMo > 0 && _tourSlowed.Add(z)) SlowMo(z.SlowMo);
        }
    }
}
