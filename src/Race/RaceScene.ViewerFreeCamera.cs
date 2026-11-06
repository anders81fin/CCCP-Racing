using System;
using CCCPRacing.Camera;
using CCCPRacing.Replay;
using Godot;

namespace CCCPRacing.Race;

public partial class RaceScene
{
    ReplayFreeCamera _viewerFreeCamera;
    LapFreeCamera _viewerSavedFree;
    bool _viewerFreeControl;
    float _viewerFreeStart;
    float _viewerFreeBaseFov, _viewerFreeBaseNear, _viewerFreeBaseFar;
    float _viewerFreeBaseEngine, _viewerFreeBaseHorn;      // the car's own sounds at full: the free camera hears them as a microphone at the lens would
    public bool ViewerFreeCameraActive => _viewerFreeCamera != null;
    public bool ViewerFreeCameraControlled => _viewerFreeControl;
    public ReplayFreeCamera ViewerFreeRig => _viewerFreeCamera;
    public Transform3D ViewerCameraPose => _cam.GlobalTransform;
    public float ViewerCameraFov => _cam.Fov;

    public void ViewerBeginFreeCamera()
    {
        if (!ViewerMode || !ViewerReady) return;
        EnsureFreeCamera();
        ViewerFreeCameraPose();
        if (_viewerSavedFree?.Motion != null)
        {
            _viewerFreeCamera.SetFollow(_viewerSavedFree.Follow, _car.Pos);
            _viewerFreeCamera.SetLook(_viewerSavedFree.Look, _car.Pos);
        }
        _viewerSavedFree = null; _viewerFreeControl = true;
    }
    void EnsureFreeCamera()
    {
        if (ViewerFreeCameraActive) return;
        var pose = _cam.GlobalTransform; float fov = _cam.Fov, near = _cam.Near, far = _cam.Far;
        ViewerLeaveDirector(); ViewerLeaveGameplayCamera();
        _viewerFreeBaseFov = _cam.Fov; _viewerFreeBaseNear = _cam.Near; _viewerFreeBaseFar = _cam.Far;
        _viewerFreeBaseEngine = _snd.Volume; _viewerFreeBaseHorn = _horn.Volume;
        _cam.Fov = fov; _cam.Near = near; _cam.Far = far;
        _viewerFreeCamera = new ReplayFreeCamera(pose, _car.Pos);
    }
    void ClearFreeCamera()
    {
        if (!ViewerFreeCameraActive) return;
        _viewerFreeCamera = null; _viewerSavedFree = null; _viewerFreeControl = false;
        _cam.Fov = _viewerFreeBaseFov; _cam.Near = _viewerFreeBaseNear; _cam.Far = _viewerFreeBaseFar;
        _snd.Volume = _viewerFreeBaseEngine; _horn.Volume = _viewerFreeBaseHorn;
        _cam.Snap(_car.Pos);
        _viewerCamDirty = true;
    }
    /// <summary>Leaves the free camera for a following camera: <paramref name="back"/> (the one in view when the player took the free camera), top-down by default. Its own lens comes back with it.</summary>
    public void ViewerEndFreeCamera(ViewerCameraMode? back = null)
    {
        if (!ViewerFreeCameraActive) return;
        ClearFreeCamera();
        var mode = back ?? ViewerCameraMode.TopDown;
        _viewerCamera = mode;
        _gameplayView = mode switch { ViewerCameraMode.Bonnet => GameplayView.Bonnet, ViewerCameraMode.Cinematic => GameplayView.Cinematic, _ => GameplayView.TopDown };
        _replayCamera.Reset(); _gameplayCamera.Reset();
        _cam.Snap(_car.Pos); _viewerCamDirty = true;
        if (mode != ViewerCameraMode.Auto) ViewerGameplayCameraFrame(1f / 60f);      // (Auto's director takes its first step in the next ViewerFrame, paused or not)
    }
    public LapFreeCamera ViewerCaptureFreeCamera()
    {
        if (_viewerFreeCamera == null) return null;
        ViewerFreeCameraPose();
        return _viewerFreeCamera.Capture(_car.Pos, _cam.Fov, _cam.Near, _cam.Far);
    }
    void ViewerConstrainFreeCamera()
    {
        // Use the canonical movie aspect so window resizing, seeks and MP4 export
        // cannot change a floor-limited shot's position.
        const float aspect = 16f / 9f;
        float tangent = Mathf.Tan(Mathf.DegToRad(_cam.Fov) * .5f);
        float clearance = Mathf.Max(.35f, _cam.Near * Mathf.Sqrt(1 + tangent * tangent * (1 + aspect * aspect)) + .1f);
        _viewerFreeCamera.ConstrainGround(_track.MeshHeight, clearance);
    }
    public void ViewerReviewCameraKeys()
    {
        ViewerEndFreeCamera();
        ViewerApplyCameraKeys(force: true);
        if (ViewerFreeCameraActive) ViewerFreeCameraPose();
    }
    /// <summary>The free camera in hand goes to a saved shot's place and lens (Tab between keys while flying); the player keeps control.</summary>
    public void ViewerPlaceFreeKey(LapFreeCamera shot)
    {
        if (_viewerFreeCamera == null || shot == null) return;
        _viewerFreeCamera.Restore(shot, _car.Pos);
        _cam.Fov = shot.Fov; _cam.Near = shot.Near; _cam.Far = shot.Far;
        ViewerFreeCameraPose();
    }
    public void ViewerPrepareExportCamera(float clock)
    {
        ViewerApplyCameraKeys(force: false, keyClock: clock);
        if (ViewerFreeCameraActive) ViewerFreeCameraPose(clock);
    }
    void ViewerSelectFreeKey(LapFreeCamera shot, float start)
    {
        EnsureFreeCamera(); _viewerFreeStart = start; _viewerSavedFree = shot; _viewerFreeControl = false;
    }
    void ViewerFreeCameraPose(float? clock = null)
    {
        if (_viewerFreeCamera == null) return;
        if (_viewerSavedFree != null && !_viewerFreeControl)
        {
            var frame = ReplayCameraMotion.Evaluate(_viewerSavedFree, _viewerFreeStart, clock ?? _replayClock, TitleGhost.Sample);
            if (_viewerSavedFree.Motion == null) _viewerFreeCamera.Restore(_viewerSavedFree, _car.Pos);
            else _viewerFreeCamera.Restore(_viewerSavedFree with { X = frame.Pose.Origin.X, Y = frame.Pose.Origin.Y, Z = frame.Pose.Origin.Z, Follow = false,
                Yaw = frame.Pose.Basis.GetEuler().Y, Pitch = frame.Pose.Basis.GetEuler().X, Look = _viewerSavedFree.Look }, _car.Pos);
            _cam.Fov = frame.Fov; _cam.Near = _viewerSavedFree.Near; _cam.Far = _viewerSavedFree.Far;
        }
        else _viewerFreeCamera.UpdateTarget(_car.Pos);
        ViewerConstrainFreeCamera();
        _cam.GlobalTransform = new Transform3D(_viewerFreeCamera.Basis, _viewerFreeCamera.Position);
        _cam.ListenAt(_viewerFreeCamera.Position, -_viewerFreeCamera.Basis.Z);      // the microphone is at the lens, not at the car
        // The car's own sounds are not positional: the microphone at the lens hears them as far as the car is (6 dB for each doubling of the distance, nothing at all
        // beyond 300 m), as if the car were any other sound source in the world.
        float away = _viewerFreeCamera.Position.DistanceTo(_car.Pos + Vector3.Up * 0.7f);
        float hear = away >= 300f ? 0f : Mathf.Clamp(8f / Math.Max(away, 8f), 0f, 1f) * Mathf.Clamp((300f - away) / 60f, 0f, 1f);
        _snd.Volume = _viewerFreeBaseEngine * hear;
        _horn.Volume = _viewerFreeBaseHorn * hear;
    }
}
