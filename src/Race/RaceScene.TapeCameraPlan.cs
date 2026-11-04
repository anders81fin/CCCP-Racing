using System;
using Godot;
using CCCPRacing.Camera;

namespace CCCPRacing.Race;

/// <summary>
/// Opt-in Ilyich tape photography. Cut marks come from the take's road
/// positions, and every eye/target is a function of the same replay clock as
/// the ghost and world tape. There is no camera state to drift on seek.
/// </summary>
public partial class RaceScene
{
    bool _tapeShootIlyichPlan;
    bool _tapeShootIlyichPlanV2;
    bool _tapeShootIlyichPlanV3;
    string _tapeShootIlyichDetail;
    float[] _tapeCameraMarks = Array.Empty<float>();
    int _tapePlanLastShot = -1;
    static readonly string[] IlyichTapeShotNames =
        { "farm", "east-bend", "ford", "gravel-bend", "church", "statue", "village" };
    static readonly string[] IlyichTapeV2ShotNames =
        { "farm", "silo-bonnet", "first-bend-overview", "far-bank-low", "mud-bend-crane", "church", "bus-stop-overview" };
    static readonly string[] IlyichTapeV3ShotNames =
        { "farm", "silo-bonnet", "first-bend-overview", "far-bank-low", "mud-bend-crane",
          "church-approach", "church-inner-hairpin", "church-exit", "bus-stop-overview" };

    bool IlyichTapeDetailCamera(float dt)
    {
        Vector3 car = (_tapeShootIlyichDetail == "ilyich-sweep" ? TapeDroneAnchor(.24f) : _car.Pos) + Vector3.Up;
        Vector3 next = TitleGhost.Sample(Math.Min(TitleGhost.Duration, _replayClock + .25f)).Origin + Vector3.Up;
        Vector3 target = car.Lerp(next, .15f);
        Vector3 eye = _tapeShootIlyichDetail switch
        {
            "ilyich-impact" => Ground(new Vector2(78f, 268f), 5.5f),
            "ilyich-ford" => Ground(new Vector2(266f, 261f), 5.2f),
            "ilyich-east" => car + new Vector3(-22f, 13f, -14f),
            _ => car + new Vector3(-23f, 16f + Mathf.Clamp((_replayClock - 22f) * 1.5f, 0f, 9f), 22f),
        };
        for (int i = 2; i <= 16; i++)
        {
            float along = i / 16f;
            Vector3 p = target.Lerp(eye, along);
            eye.Y += Math.Max(0f, (_track.MeshHeight(p.X, p.Z) + 1.4f - p.Y) / along);
        }
        _cam.Fov = 59f; _cam.Near = .2f; _cam.Far = 600f;
        _cam.Pose(eye, target, _car.Pos, dt);
        return true;
    }

    bool IlyichTapePlanV2Camera(float dt)
    {
        float clock = _replayClock;
        int shot = 0;
        while (shot < _tapeCameraMarks.Length && clock >= _tapeCameraMarks[shot]) shot++;
        float from = shot == 0 ? 0f : _tapeCameraMarks[shot - 1];
        float to = shot == _tapeCameraMarks.Length ? TitleGhost.Duration : _tapeCameraMarks[shot];
        float u = Mathf.Clamp((clock - from) / Math.Max(.01f, to - from), 0f, 1f);
        if (shot != _tapePlanLastShot)
        {
            _tapePlanLastShot = shot;
            string[] names = _tapeShootIlyichPlanV3 ? IlyichTapeV3ShotNames : IlyichTapeV2ShotNames;
            GD.Print($"tape camera: {names[shot]} begins at {clock:F3} s");
        }

        if (shot == 2 || shot == (_tapeShootIlyichPlanV3 ? 8 : 6))
        {
            _cam.Fov = _replayOldFov; _cam.Near = _replayOldNear; _cam.Far = _replayOldFar;
            if (clock - from < 1f / 30f + .001f) _cam.SnapDriving(_car.Pos, _car.Vel);
            CameraFeel(dt, replayDriving: true);
            _cam.Follow(_car.Pos, _car.Vel, dt);
            return true;
        }
        if (shot == 1)
        {
            var bonnet = GameplayBonnetCamera.Frame(_car.Pos, _car.Rot);
            _cam.Fov = 75f; _cam.Near = .06f; _cam.Far = 600f;
            _cam.Pose(bonnet.eye, bonnet.target, _car.Pos, dt);
            return true;
        }

        Vector3 car = _car.Pos + Vector3.Up;
        Vector3 next = TitleGhost.Sample(Math.Min(TitleGhost.Duration, clock + .35f)).Origin + Vector3.Up;
        Vector3 target = car.Lerp(next, .32f), eye;
        float fov;
        switch (shot)
        {
            case 0: // The original opening until the silo.
                eye = car + new Vector3(Mathf.Lerp(-27f, -18f, u), Mathf.Lerp(21f, 29f, u), Mathf.Lerp(28f, 22f, u));
                target += new Vector3(0, 0, -6f);
                fov = 60f;
                break;
            case 3: // Fixed position on the far riverbank near the road edge.
                eye = Ground(new Vector2(251f, 258f), 2.1f);
                target = car.Lerp(next, .10f);
                fov = 60f;
                break;
            case 4: // Crane on the outside of the bend after the mud, rising through both turns.
                eye = Ground(new Vector2(Mathf.Lerp(179f, 122f, u), Mathf.Lerp(277f, 252f, u)),
                    Mathf.Lerp(13f, 38f, u));
                target = target.Lerp(Ground(new Vector2(156f, 220f), 1f), .10f);
                fov = 62f;
                break;
            case 6 when _tapeShootIlyichPlanV3: // Fixed point inside the church hairpin; pan, do not move.
                eye = Ground(new Vector2(68f, 194f), 3.1f);
                target = car.Lerp(next, .10f);
                fov = 64f;
                break;
            default: // Keep the existing church view until the bus stop.
                eye = car + new Vector3(Mathf.Lerp(24f, 17f, u), Mathf.Lerp(17f, 24f, u), Mathf.Lerp(28f, 23f, u));
                target = target.Lerp(Ground(new Vector2(57, 192), 3f), .18f);
                fov = 58f;
                break;
        }
        if (shot != 3 && (!_tapeShootIlyichPlanV3 || shot != 6))
        {
            for (int i = 2; i <= 16; i++)
            {
                float along = i / 16f;
                Vector3 p = target.Lerp(eye, along);
                eye.Y += Math.Max(0f, (_track.MeshHeight(p.X, p.Z) + 2.1f - p.Y) / along);
            }
        }
        _cam.Fov = fov; _cam.Near = .2f; _cam.Far = 600f;
        _cam.Pose(eye, target, _car.Pos, dt);
        return true;
    }

    bool IlyichTapePlanCamera(float dt)
    {
        float clock = _replayClock;
        int shot = 0;
        while (shot < _tapeCameraMarks.Length && clock >= _tapeCameraMarks[shot]) shot++;
        float from = shot == 0 ? 0f : _tapeCameraMarks[shot - 1];
        float to = shot == _tapeCameraMarks.Length ? TitleGhost.Duration : _tapeCameraMarks[shot];
        float u = Mathf.Clamp((clock - from) / Math.Max(0.01f, to - from), 0f, 1f);
        if (shot != _tapePlanLastShot)
        {
            _tapePlanLastShot = shot;
            GD.Print($"tape camera: {IlyichTapeShotNames[shot]} begins at {clock:F3} s");
        }

        Vector3 car = _car.Pos + Vector3.Up;
        Vector3 next = TitleGhost.Sample(Math.Min(TitleGhost.Duration, clock + 0.38f)).Origin + Vector3.Up;
        Vector3 target = car.Lerp(next, 0.42f), eye;
        float fov;
        switch (shot)
        {
            case 0: // Red barns, bus and tractor: a rising dolly following the start straight.
                eye = car + new Vector3(Mathf.Lerp(-27f, -18f, u), Mathf.Lerp(21f, 29f, u), Mathf.Lerp(28f, 22f, u));
                target += new Vector3(0, 0, -6f);
                fov = 60f;
                break;
            case 1: // East ridge: one front/side angle through the bend, no bonnet cut.
                eye = car + new Vector3(Mathf.Lerp(-25f, -19f, u), Mathf.Lerp(15f, 23f, u), Mathf.Lerp(-13f, -8f, u));
                fov = 58f;
                break;
            case 2: // Stay on the south bank from approach to exit of the ford.
                eye = car + new Vector3(Mathf.Lerp(-5f, 12f, u), 19f, 31f);
                target = target.Lerp(Ground(new Vector2(256, 247), 1f), 0.12f);
                fov = 60f;
                break;
            case 3: // A broad sideways gravel sweep with the dust trail in frame.
                eye = car + new Vector3(Mathf.Lerp(-25f, -17f, u), Mathf.Lerp(19f, 16f, u), Mathf.Lerp(23f, 16f, u));
                fov = 60f;
                break;
            case 4: // The road curls towards the grain-store church.
                eye = car + new Vector3(Mathf.Lerp(24f, 17f, u), Mathf.Lerp(17f, 24f, u), Mathf.Lerp(28f, 23f, u));
                target = target.Lerp(Ground(new Vector2(57, 192), 3f), 0.18f);
                fov = 58f;
                break;
            case 5: // A deliberate, slow orbit about the monument; car and statue share depth.
                Vector3 statue = Ground(new Vector2(61, 163), 3f);
                float angle = Mathf.Lerp(0.65f, 2.2f, u);
                eye = statue + new Vector3(37f * MathF.Cos(angle), Mathf.Lerp(23f, 27f, u), 37f * MathF.Sin(angle));
                target = target.Lerp(statue, 0.35f);
                fov = 61f;
                break;
            default: // Village street and return to the barn.
                eye = car + new Vector3(Mathf.Lerp(-24f, -18f, u), Mathf.Lerp(18f, 22f, u), 20f);
                fov = 59f;
                break;
        }

        // The sightline as well as the eye must clear the terrain. This is a
        // clock-local correction, so seeking and shooting a later span agree.
        for (int i = 2; i <= 16; i++)
        {
            float along = i / 16f;
            Vector3 p = target.Lerp(eye, along);
            eye.Y += Math.Max(0f, (_track.MeshHeight(p.X, p.Z) + 2.1f - p.Y) / along);
        }
        _cam.Fov = fov; _cam.Near = .2f; _cam.Far = 600f;
        _cam.Pose(eye, target, _car.Pos, dt);
        return true;
    }
}
