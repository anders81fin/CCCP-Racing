using CCCPRacing.Camera;

namespace CCCPRacing.Race;

public partial class RaceScene
{
    readonly ReplayCameraDirector _replayCamera = new();
    bool _replayCameraActive;
    ReplayCameraDirector.Shot? _replayLastShot;
    float _replayOldFov, _replayOldNear, _replayOldFar;
    float _demoCameraClock;
    bool TitleCameraAllowed => _state == State.Title && !PresentationActive;

    void PrepareReplayCamera()
    {
        if (!_replayCameraActive || TitleCameraAllowed) return;
        _cam.Fov = _replayOldFov; _cam.Near = _replayOldNear; _cam.Far = _replayOldFar;
        _cam.Snap(_car.Pos);
        _replayCameraActive = false;
        _replayCamera.Reset();
        _tapePlanLastShot = -1;
    }

    bool ReplayCamera(float dt)
    {
        if (!TitleCameraAllowed) return false;
        if (!_replayCameraActive)
        {
            _replayOldFov = _cam.Fov; _replayOldNear = _cam.Near; _replayOldFar = _cam.Far;
            _replayCameraActive = true;
            _replayCamera.Reset();
            _replayLastShot = null;
        }
        if (_tapeShoot && _tapeCameraSequencePath != null && Replaying)
            return TapeSequenceCamera(dt);
        if (_tapeShoot && _tapeShootTrailerDetail != null && Replaying)
            return TrailerTapeDetailCamera(dt);
        if (_tapeShoot && _tapeShootSiberiaDetail != null && Replaying && _trackId == "siberia")
            return SiberiaTapeDetailCamera(dt);
        if (_tapeShoot && _tapeShootIlyichDetail != null && Replaying)
            return IlyichTapeDetailCamera(dt);
        if (_tapeShoot && (_tapeShootIlyichPlanV2 || _tapeShootIlyichPlanV3) && Replaying)
            return IlyichTapePlanV2Camera(dt);
        if (_tapeShoot && _tapeShootIlyichPlan && Replaying)
            return IlyichTapePlanCamera(dt);
        if (_tapeShoot && _tapeShootOverview && Replaying)
        {
            // A captured span can hold one camera across the water crossing or
            // another encounter, regardless of the title director's cut schedule.
            if (_replayLastShot != ReplayCameraDirector.Shot.Overview) _cam.SnapDriving(_car.Pos, _car.Vel);
            _replayLastShot = ReplayCameraDirector.Shot.Overview;
            _cam.Fov = _replayOldFov; _cam.Near = _replayOldNear; _cam.Far = _replayOldFar;
            CameraFeel(dt, replayDriving: true);
            _cam.Follow(_car.Pos, _car.Vel, dt);
            return true;
        }
        Godot.Vector3 future;
        float clock;
        if (Replaying)
        {
            future = TitleGhost.Sample(System.Math.Min(TitleGhost.Duration, _replayClock + 2f)).Origin;
            clock = _replayClock;
        }
        else
        {
            _demoCameraClock += dt;
            clock = _demoCameraClock;
            int at = _track.NearestCenter(new(_car.Pos.X, _car.Pos.Z), out _);
            int ahead = (at + (int)System.Math.Max(18, _car.Speed * 2)) % _track.Center.Length;
            future = _track.CenterPos(ahead) + Godot.Vector3.Up * .7f;
        }
        var view = _replayCamera.Frame(_track, _car.Pos, _car.Vel, _car.Rot, future, clock, dt);
        bool cut = _replayLastShot != view.Shot;
        _replayLastShot = view.Shot;
        if (view.Shot == ReplayCameraDirector.Shot.Overview)
        {
            _cam.Fov = _replayOldFov; _cam.Near = _replayOldNear; _cam.Far = _replayOldFar;
            if (cut) _cam.SnapDriving(_car.Pos, _car.Vel);
            CameraFeel(dt, replayDriving: true);
            _cam.Follow(_car.Pos, _car.Vel, dt);
            return true;
        }
        _cam.Fov = view.Fov; _cam.Near = view.Near; _cam.Far = 600;
        _cam.Pose(view.Eye, view.Target, _car.Pos, dt);
        return true;
    }
}
