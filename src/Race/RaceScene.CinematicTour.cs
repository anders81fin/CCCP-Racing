using System;
using Godot;

namespace CCCPRacing.Race;

/// <summary>Opt-in movie photography of the instructions' unchanged autopilot lap (--cinematic-tour).
/// Road-based cuts keep the jump, slides and landmarks in the shot, independently of render speed.</summary>
public partial class RaceScene
{
    bool _cinematicTour;
    int _cinematicShot = -1;
    Vector3 _cinematicEye, _cinematicTarget;

    readonly record struct LapShot(int From, int To, float Back, float Side, float Height,
        float EndBack, float EndSide, float EndHeight, float Fov = 58f, float Lead = 3f);

    static readonly LapShot[] IlyichLapShots =
    {
        new(0, 40, 18, -13, 16, 13, -8, 8, 55),               // crane down from the barn
        new(40, 100, 12, -5, 3.8f, 9, -4, 3, 67),             // low chase
        new(100, 195, -15, 13, 8, -10, 18, 10, 60),            // first bend, front three-quarter
        new(195, 280, 5, -10, 4, -7, -13, 5, 66),             // the run to the jump
        new(280, 350, 8, 23, 10, -7, 26, 7, 56),              // jump in profile
        new(350, 435, -14, -14, 7, -9, -12, 5, 62),            // ford approach and spray
        new(435, 558, 17, 12, 10, -8, 20, 12, 56),             // orbit the clutch kick
        new(558, 715, 13, -7, 5, 11, -10, 6, 65),             // chase into the second slide
        new(715, 834, -16, 24, 17, 15, 22, 15, 55),            // church hairpin, sweeping overhead
        new(834, 930, 20, -19, 19, 14, -11, 10, 59),           // descend over the climb
        new(930, 1200, 16, 16, 18, 4, 9, 23, 54),             // finish roof and barn reveal
    };

    static readonly LapShot[] SiberiaLapShots =
    {
        new(0, 100, 24, -18, 19, 14, -8, 6, 58),              // descend into the valley
        new(100, 270, 12, -5, 4, 9, -4, 3.5f, 67),            // ice straight, low chase
        new(270, 435, -14, 17, 12, 8, 22, 15, 58),             // switchback orbit
        new(435, 610, 21, -22, 24, 12, -14, 13, 58),           // radar ridge reveal
        new(610, 720, -20, 7, 5, -15, 5, 3.5f, 66),            // facing the car on the runway
        new(720, 820, 10, -12, 5, -8, -15, 7, 64),             // lateral runway dolly
        new(820, 1035, 18, 14, 13, 12, 7, 5, 62),             // downhill pursuit
        new(1035, 1160, -16, -22, 14, 12, -19, 12, 58),        // frozen river and braking bend
        new(1160, 1250, 18, 21, 20, -8, 17, 16, 56),          // above the barracks
        new(1250, 1700, 20, -14, 12, 12, -8, 7, 62),           // home along the snowy road
    };

    void CinematicTourCamera(float dt)
    {
        var shots = _trackId == "ilyich" ? IlyichLapShots : SiberiaLapShots;
        int metre = TourMetre(_scRoad);
        // The Ilyich car starts just before the line, inside the barn.
        if (!_tourHalf && metre > _track.Center.Length * 4 / 5) metre = 0;
        int index = Array.FindIndex(shots, s => metre >= s.From && metre < s.To);
        if (index < 0) index = shots.Length - 1;
        var shot = shots[index];
        float u = Math.Clamp((metre - shot.From) / (float)(shot.To - shot.From), 0, 1);
        u = u * u * (3 - 2 * u);
        Vector2 dir = _track.CenterDir(SchoolScript.Wrap(_track, _scRoad + 5));
        Vector3 forward = new(dir.X, 0, dir.Y), right = new(-dir.Y, 0, dir.X);
        Vector3 target = _car.Pos + Vector3.Up * 0.65f + forward * shot.Lead;
        Vector3 eye = _car.Pos - forward * Mathf.Lerp(shot.Back, shot.EndBack, u)
            + right * Mathf.Lerp(shot.Side, shot.EndSide, u)
            + Vector3.Up * Mathf.Lerp(shot.Height, shot.EndHeight, u);
        // Keep dolly paths above hills; elevated village shots clear building roofs.
        eye.Y = Math.Max(eye.Y, _track.MeshHeight(eye.X, eye.Z) + 2.8f);
        bool cut = index != _cinematicShot;
        float blend = cut ? 1 : 1 - MathF.Exp(-10f * dt);
        _cinematicEye = _cinematicEye.Lerp(eye, blend);
        _cinematicTarget = _cinematicTarget.Lerp(target, blend);
        _cinematicShot = index;
        _cam.ShakeEnabled = false;
        // Scenic updates the car's spatial-audio listener before the movie's precise camera pose.
        _cam.Scenic(_car.Pos, _cinematicEye - _car.Pos, dt);
        _cam.Fov = shot.Fov;
        _cam.Near = 0.2f;
        _cam.Far = 600f;
        _cam.GlobalTransform = new Transform3D(Basis.LookingAt(_cinematicTarget - _cinematicEye, Vector3.Up), _cinematicEye);
        _scLightClock = _trackId == "ilyich" ? 2.7f : 1.2f;
    }
}
