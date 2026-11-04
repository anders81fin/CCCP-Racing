using System;
using System.Collections.Generic;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Race;

public partial class RaceScene
{
    // Godot SmoothStep interpolates from/to by a 0..1 weight; normalize timed intervals first.
    static float SiberiaProgress(float from, float to, float time)
        => Mathf.SmoothStep(0, 1, Math.Clamp((time - from) / (to - from), 0, 1));

    Vector3 SiberiaActivity(string id, Vector2 fallback)
    {
        var plan = _track.SiberiaActivities;
        if (plan != null)
        {
            foreach (var prop in plan.Props)
                if (prop.Id == id) return Ground(prop.Position, 1.5f);
            foreach (var worker in plan.Workers)
                if (worker.Id == id) return Ground(worker.A.Lerp(worker.B, .5f), 1.5f);
        }
        return Ground(fallback, 1.5f);
    }

    Beat SiberiaDeparture()
    {
        int jet = -1;
        Vector3 from = Vector3.Zero, previous = Vector3.Zero, focus = Vector3.Zero;
        return B("", () =>
        {
            // Keep the aircraft already rolling: resetting sorties here cuts off its takeoff.
            jet = _jets?.NextJet ?? -1;
            from = _scScenicTarget;
            previous = _scScenicOffset;
            focus = Ground(new(233, 45), 3);
            _scScenic = () =>
            {
                if (_sc.Sim < 7 && _jets?.JetPosition(jet) is Vector3 p)
                {
                    p.X = Math.Clamp(p.X, 65f, 248f);
                    p.Z = Math.Clamp(p.Z, 38f, 70f);
                    p.Y = Math.Min(p.Y, _track.MeshHeight(p.X, p.Z) + 18f);
                    focus = p;
                }
                return SiberiaScenicView(from.Lerp(focus, SiberiaProgress(0, 1.2f, _sc.Sim)),
                    previous.Lerp(new(0, 55, 32), SiberiaProgress(0, 4f, _sc.Sim)));
            };
        }, (t, _) =>
        {
            if (t > 1.2f && string.IsNullOrEmpty(_sc.Caption)) _sc.Caption = "THE AIR FORCE IS READY.";
            if (!_sc.Once && (_jets?.JetVelocity(jet)?.Length() ?? 0) > 10)
            {
                _sc.Once = true;
                ShotSoon("winter_takeoff", 1f);
            }
            if (_sc.Once && t > 2.5f) _sc.Caption = "FULL AFTERBURNER. FOR THE PARTY.";
            return t >= 12;
        }, max: 13);
    }

    // A shallow glide, followed by a small dolly. Captions wait for the subject, without a pullback.
    Beat SiberiaVisit(string caption, Func<Vector3> subject, Vector3 offset, float duration, string name,
        Action setup = null, Action<float> action = null)
    {
        Vector3 from = Vector3.Zero, previous = Vector3.Zero;
        float travel = 0;
        return B("", () =>
        {
            BeginEnvironmentTour();
            from = _scScenic != null ? _scScenicTarget : _car.Pos;
            previous = _scScenic != null ? _scScenicOffset : _cam.Offset;
            setup?.Invoke();
            travel = Math.Clamp(from.DistanceTo(subject()) / 32f, 3f, 7f);
            if (SchoolDbg) GD.Print($"school: scenic {name} subject {subject()}, view {SiberiaScenicView(subject(), offset)}");
            _scScenic = () =>
            {
                float arrival = SiberiaProgress(0, travel, _sc.Sim);
                float dolly = SiberiaProgress(travel, duration, _sc.Sim);
                return SiberiaScenicView(from.Lerp(subject(), arrival),
                    previous.Lerp(offset + new Vector3(dolly * 3f, 0, 0), arrival));
            };
            ShotSoon(name, duration - 1);
        }, (t, _) =>
        {
            if (t >= travel + .6f && string.IsNullOrEmpty(_sc.Caption)) _sc.Caption = caption;
            action?.Invoke(t - travel - .6f);
            return t >= duration;
        }, max: duration + 1);
    }

    // Keep the far corners over the authored map while retaining an oblique view of lights/buildings.
    (Vector3 target, Vector3 offset) SiberiaScenicView(Vector3 target, Vector3 offset)
    {
        float size = _track.WorldSize;
        target.X = Math.Clamp(target.X, 30f, size - 30f);
        target.Z = Math.Clamp(target.Z, 14f, size - 14f);
        // Edge subjects are viewed from the border towards the interior, keeping them in frame.
        float east = SiberiaProgress(size - 60, size - 30, target.X);
        float west = 1 - SiberiaProgress(30, 60, target.X);
        offset = offset.Lerp(new(Math.Max(32f, Math.Abs(offset.X)), offset.Y, 8f), east);
        offset = offset.Lerp(new(-Math.Max(32f, Math.Abs(offset.X)), offset.Y, 8f), west);
        float north = 1 - SiberiaProgress(14, 32, target.Z);
        offset.Z = Mathf.Lerp(offset.Z, -Math.Max(32f, Math.Abs(offset.Z)), north);
        target.Y = Math.Max(target.Y, _track.MeshHeight(target.X, target.Z) + 1.5f);
        float tan = MathF.Tan(Mathf.DegToRad(_cam.Fov / 2));
        float aspect = GetViewport().GetVisibleRect().Size.Aspect();
        Basis basis = Basis.LookingAt(-offset.Normalized(), Vector3.Up);
        float groundY = _track.MeshHeight(target.X, target.Z);
        // The northern hangars reach the map edge: include their roof silhouettes against the
        // authored snowy backdrop, rather than cropping the airbase out of its own wide shot.
        float northEdge = target.Z < 80 && offset.Y > 60 ? -32f : 8f;
        float fit = 1f;
        foreach (float x in new[] { -1f, 1f })
        foreach (float y in new[] { -1f, 1f })
        {
            Vector3 ray = basis * new Vector3(x * tan * aspect, y * tan, -1);
            if (ray.Y >= -.001f) continue;
            Vector3 origin = target + ray * ((groundY - target.Y) / ray.Y);
            Vector3 span = offset - ray * (offset.Y / ray.Y);
            // Solve each corner's intersection continuously: no discrete zoom steps as the camera glides.
            if (span.X > .001f) fit = Math.Min(fit, (size - 8 - origin.X) / span.X);
            else if (span.X < -.001f) fit = Math.Min(fit, (8 - origin.X) / span.X);
            if (span.Z > .001f) fit = Math.Min(fit, (size - 8 - origin.Z) / span.Z);
            else if (span.Z < -.001f) fit = Math.Min(fit, (northEdge - origin.Z) / span.Z);
        }
        offset *= Math.Clamp(fit, .01f, 1f);
        // Clear forest and hillside terrain along the camera's sightline, rather than flying through it.
        float raise = 0;
        for (int i = 1; i <= 10; i++)
        {
            float u = i / 10f;
            Vector3 p = target + offset * u;
            raise = Math.Max(raise, (_track.MeshHeight(p.X, p.Z) + 3f - p.Y) / u);
        }
        offset.Y += raise;
        return (target, offset);
    }

    Beat SiberiaNightPhase(string caption, float from, float to, int phase)
    {
        float duration = phase == 2 ? 12f : 7f;
        Vector3 previousTarget = Vector3.Zero, previousOffset = Vector3.Zero;
        return B(phase == 0 ? "" : caption, () =>
        {
            BeginEnvironmentTour();
            previousTarget = _scScenic != null ? _scScenicTarget : _car.Pos;
            previousOffset = _scScenic != null ? _scScenicOffset : _cam.Offset;
            _scLightClock = from;
            _scScenic = () =>
            {
                // Hold the whole camp through sunset, dusk and night; keep its searchlights in view.
                // Dawn stays on the airbase after the separate, wide night takeoff shot.
                Vector3 target = phase < 3 ? Ground(new(125, 218), 4) : Ground(new(165, 42), 4);
                Vector3 offset = phase < 3 ? new(0, 65, 65) : new(0, 110, 55);
                float orbit = phase < 3 ? (phase * 7f + _sc.Sim) / 26f * .06f : _sc.Sim / duration * .025f;
                var view = SiberiaScenicView(target, offset.Rotated(Vector3.Up, -orbit));
                float arrival = phase == 0 ? SiberiaProgress(0, 4f, _sc.Sim) : 1;
                return (previousTarget.Lerp(view.target, arrival), previousOffset.Lerp(view.offset, arrival));
            };
            ShotSoon("winter_light_" + phase, duration - 1);
        }, (t, _) =>
        {
            _scLightClock = Mathf.Lerp(from, to, SiberiaProgress(0, 5f, t));
            if (phase == 0 && t > 4) _sc.Caption = caption;
            return t >= duration;
        }, max: duration + 1);
    }

    List<Beat> SiberiaEnvironmentBeats(string id) => id switch
    {
        "siberia_valley" => new()
        {
            SiberiaVisit("WELCOME TO SIBERIA.", () => _track.StartPos.Lerp(_car.Pos,
                    SiberiaProgress(9, 11, _sc.Total - _scEnvironmentStart)),
                new(-12, 26, 31), 14, "winter_start", action: t =>
                { if (t > 4) _sc.Caption = "SOME FRESH AIR FROM THE PARTY."; }),
        },
        "siberia_forestry" => new()
        {
            SiberiaVisit("THE PLAN NEEDS MORE TIMBER.", () => SiberiaActivity("forestry-trestle", new(260, 250)),
                new(40, 40, 8), 14, "winter_forestry", action: t =>
                { if (t > 3) _sc.Caption = "THE FOREST WAS NOT CONSULTED."; }),
        },
        "siberia_radar" => new()
        {
            // Replace the old eight-second forestry hold with a glide over the hillside.
            // The following radar/traffic shot retains its original time in the live lap.
            SiberiaVisit("THE ROAD UP IS YOUR PROBLEM.", () => Ground(new(275, 220), 3),
                new(32, 32, 8), 8, "winter_hills"),
            SiberiaVisit("THE RADAR WATCHES THE SKY.", () =>
                (KolkhozProp("radar", new(270, 156)) + Vector3.Up * 5).Lerp(
                    Ground(new(233, 45), 3), SiberiaProgress(8, 16, _sc.Sim)),
                new(32, 32, 8), 16, "winter_radar", action: t =>
                { if (t > 5) _sc.Caption = "THE ROAD UP IS YOUR PROBLEM."; }),
        },
        "siberia_base" => new() { SiberiaDeparture() },
        "siberia_wreck" => new()
        {
            SiberiaVisit("THE TIMBER DELIVERY IS DELAYED.", () => Ground(new(195, 246), 1),
                new(-24, 29, 35), 13, "winter_wreck", action: t =>
                { if (t > 4) _sc.Caption = "THE PAPERWORK BLAMES THE DRIVER."; }),
        },
        "siberia_camp" => new()
        {
            SiberiaVisit("LABOUR IS A MATTER OF HONOUR.", () => Ground(_track.Center[1210], 2),
                new(-12, 28, 36), 15, "winter_camp", setup: () => _column?.CueSchoolCamp(), action: t =>
                { if (t > 5) _sc.Caption = "LEAVING REQUIRES AUTHORISATION."; }),
            SiberiaVisit("EVEN HERE, SOMEONE FIXES CARS.", () => Ground(_drunks.MechanicPosition, 1.5f),
                new(-8, 24, 30), 7, "winter_mechanic", action: t =>
                { if (t >= 0) _sc.Arrow = () => Ground(_drunks.MechanicPosition, 3f); }),
        },
        "siberia_night" => new()
        {
            SiberiaNightPhase("THE LIGHT CHANGES AS YOU LAP.", 1, 3, 0),
            SiberiaNightPhase("LAPS 5-6: DUSK. LIGHTS ON.", 3, 5, 1),
            SiberiaNightPhase("LAPS 7-10: THE WATCH CONTINUES.", 5, 8, 2),
            SiberiaVisit("THE AIRBASE NEVER SLEEPS.", () => Ground(new(165, 42), 4),
                new(0, 110, 55), 26, "winter_night_airbase", setup: () =>
                {
                    _scLightClock = 8;
                    _ploughs?.CueSchoolRunwayPass();
                    _jets?.CueSchoolSortie();
                }, action: t =>
                {
                    if (!_sc.Once && (_jets is { FlyingJet: >= 0 } || (_jets?.JetVelocity(_jets.NextJet)?.Length() ?? 0) > 10))
                    {
                        _sc.Once = true;
                        ShotSoon("winter_night_takeoff", 1f);
                    }
                    if (_sc.Once) _sc.Caption = "NIGHT SHIFT. FULL AFTERBURNER.";
                }),
            SiberiaNightPhase("LAPS 11-12: DAWN. DAY RETURNS.", 8, 11.8f, 3),
        },
        _ => new(),
    };
}
