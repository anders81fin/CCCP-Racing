using System;
using Godot;
using CCCPRacing.Camera;

namespace CCCPRacing.Race;

// Capture-only camera moves. Car, lighting and actors still come from the tape.
public partial class RaceScene
{
    string _tapeShootSiberiaDetail;

    bool SiberiaTapeDetailCamera(float dt)
    {
        if (_tapeShootSiberiaDetail == "siberia-jet-hero" && SiberiaTapeJetCamera(dt)) return true;
        if (_tapeShootSiberiaDetail == "siberia-hairpin-overhead")
        {
            var overheadEye = new Vector3(187, 56, 222);
            _cam.Fov = 60; _cam.Near = .2f; _cam.Far = 650;
            // Pose updates the car listener; use north as camera up for the
            // exact vertical view, where world-up would be collinear.
            _cam.Pose(overheadEye, new Vector3(187, 0, 221.99f), _car.Pos, dt);
            _cam.GlobalTransform = new Transform3D(Basis.LookingAt(Vector3.Down, Vector3.Forward), overheadEye);
            return true;
        }
        if (_tapeShootSiberiaDetail == "siberia-inner-bank")
        {
            // Fixed tripod on the inner bank beside the apex, away from camp.
            // Pan with the car as it approaches, rotates and exits the bend.
            var bank = new Vector3(196, _track.MeshHeight(196, 236) + 8, 236);
            _cam.Fov = 65; _cam.Near = .2f; _cam.Far = 650;
            _cam.Pose(bank, _car.Pos + Vector3.Up * .6f, _car.Pos, dt);
            return true;
        }
        if (_tapeShootSiberiaDetail == "siberia-bonnet")
        {
            var frame = GameplayBonnetCamera.Frame(_car.Pos, _car.Rot);
            _cam.Fov = 75; _cam.Near = .06f; _cam.Far = 650;
            _cam.Pose(frame.eye, frame.target, _car.Pos, dt);
            return true;
        }
        float u = Mathf.Clamp((_replayClock - Math.Max(0, _tapeReplayStart)) / Math.Max(1, _tapeReplayQuit), 0, 1);
        Vector3 car = _car.Pos + Vector3.Up;
        Vector3 future = TitleGhost.Sample(Math.Min(TitleGhost.Duration, _replayClock + .3f)).Origin + Vector3.Up;
        Vector3 target = car.Lerp(future, .25f);
        Vector3 offset = _tapeShootSiberiaDetail switch
        {
            "siberia-ridge" => new(Mathf.Lerp(-32, -24, u), Mathf.Lerp(21, 31, u), Mathf.Lerp(21, 13, u)),
            "siberia-runway" => new(Mathf.Lerp(18, 10, u), Mathf.Lerp(13, 18, u), 29),
            "siberia-descent" => new(Mathf.Lerp(30, 23, u), Mathf.Lerp(19, 27, u), 23),
            "siberia-airbase-wide" => new(Mathf.Lerp(-28, -22, u), Mathf.Lerp(18, 22, u), 52),
            _ => new(Mathf.Lerp(-29, -20, u), Mathf.Lerp(23, 20, u), 25),
        };
        Vector3 eye = car + offset;
        if (_tapeShootSiberiaDetail == "siberia-airbase-wide")
            target = car + new Vector3(12, 8, -16);
        if (_tapeShootSiberiaDetail == "siberia-outer-crane")
        {
            // East bank, outside the tightening left turn after the frozen river.
            // A stationary crane base lets the car enter, slide wide and fell
            // the tree without the camera travelling into the collision.
            eye = new Vector3(248, Mathf.Lerp(16, 35, u), 235);
            target = car;
        }
        if (_tapeShootSiberiaDetail == "siberia-front-follow")
        {
            // Keep the lead rig's heading stable through an impact instead of
            // rotating it with the damaged/spinning body.
            float start = Math.Max(0, _tapeReplayStart);
            Vector3 heading = TitleGhost.Sample(Math.Min(TitleGhost.Duration, start + 1)).Origin
                - TitleGhost.Sample(start).Origin;
            heading.Y = 0;
            heading = heading.LengthSquared() > .01f ? heading.Normalized() : -_car.Rot.Z;
            Vector3 side = heading.Cross(Vector3.Up).Normalized();
            eye = car + heading * 10 + side * 3 + Vector3.Up * 2.5f;
            target = car;
        }
        for (int i = 2; i <= 20; i++)
        {
            float along = i / 20f;
            Vector3 p = target.Lerp(eye, along);
            eye.Y += Math.Max(0, (_track.MeshHeight(p.X, p.Z) + 2 - p.Y) / along);
        }
        _cam.Fov = _tapeShootSiberiaDetail is "siberia-runway" or "siberia-front-follow" ? 64 : 59;
        if (_tapeShootSiberiaDetail == "siberia-airbase-wide") _cam.Fov = 68;
        _cam.Near = .2f; _cam.Far = 650;
        _cam.Pose(eye, target, _car.Pos, dt);
        return true;
    }
}
