using System;
using CCCPRacing.Camera;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Race;

public partial class RaceScene
{
    readonly GameplayRearCamera _gameplayCamera = new();
    readonly GameplayBonnetFeel _bonnetFeel = new();      // the bonnet view's shake, sway, roll and pitch (they grow with the speed)
    GameplayView _gameplayView;     // Choices never leak into another track or personnel file.
    CameraDiscoveries _cameraDiscoveries;
    PartyPondDuck _cameraDuck;
    Node3D _cameraSecretModels;
    bool _gameplayCameraActive;
    GameplayView _gameplayLastView;
    string GameplayCameraName => _gameplayView switch { GameplayView.Cinematic => "CINEMATIC CAMERA", GameplayView.Bonnet => "BONNET CAMERA", _ => "TOP-DOWN CAMERA" };

    void CycleGameplayCamera() => _gameplayView = _cameraDiscoveries?.Next(_track.Id, _gameplayView) ?? GameplayView.TopDown;
    bool HasDiscoveredCamera => _cameraDiscoveries?.HasAlternative(_track.Id) == true;
    bool HasUndiscoveredCameras
    {
        get
        {
            if (_cameraDiscoveries == null) return false;
            if (_track.Id == "ilyich" && !_cameraDiscoveries.Has(_track.Id, GameplayView.Bonnet)) return true;
            foreach (var secret in CameraSecrets.Layout(_track.Id))
                if (!_cameraDiscoveries.Has(_track.Id, secret.View)) return true;
            return false;
        }
    }
    void LoadCameraDiscoveries()
    {
        _cameraDiscoveries = new CameraDiscoveries(FileDir) { AllOpen = () => CheatUnlock };
        _gameplayView = GameplayView.TopDown;
        _cameraDuck = FindChild("PondMallard", true, false) as PartyPondDuck;
        _cameraSecretModels = FindChild("CameraSecrets", true, false) as Node3D;
        UpdateCameraSecretModels();
    }

    void CameraDiscoveryTick(Vector3 before)
    {
        if (_state != State.Racing || InSchool || Replaying || _rescue.Active || FateHasCar
            || _cameraDiscoveries == null || _tourCapture || _cinematicTour) return;
        if (_track.Id == "ilyich" && _cameraDuck?.Active == true
            && !_cameraDiscoveries.Has(_track.Id, GameplayView.Bonnet)
            && CameraDiscoveries.Touches(before, _car.Pos, _car.Rot, _cameraDuck.WorldPosition, PartyPondDuck.Radius))
        {
            DiscoverCamera(GameplayView.Bonnet, "QUACK! BONNET CAMERA FOUND");
        }
        foreach (var secret in CameraSecrets.Layout(_track.Id))
        {
            var p = secret.Position;
            if (!_cameraDiscoveries.Has(_track.Id, secret.View)
                && CameraDiscoveries.Touches(before, _car.Pos, _car.Rot, new Vector3(p.X, _track.MeshHeight(p.X, p.Y) + 0.6f, p.Y), 0.4f))
                DiscoverCamera(secret.View, secret.View == GameplayView.Bonnet ? "BONNET CAMERA FOUND" : "CINEMATIC CAMERA FOUND");
        }
    }
    void DiscoverCamera(GameplayView view, string message)
    {
        if (!_cameraDiscoveries.Discover(_track.Id, view)) return;
        UpdateCameraSecretModels();
        _hud.ShowAlways(message, "C / LB: CHANGE CAMERA", 5f, UI.Hud.Gold);
    }
    void UpdateCameraSecretModels()
    {
        _cameraDuck?.SetCameraVisible(!_cameraDiscoveries.Has(_track.Id, GameplayView.Bonnet));
        if (_cameraSecretModels == null) return;
        foreach (var child in _cameraSecretModels.GetChildren())
            if (child is Node3D model && System.Enum.TryParse<GameplayView>(model.Name, out var view))
                model.Visible = !_cameraDiscoveries.Has(_track.Id, view);
    }
    float _gameplayOldFov, _gameplayOldNear, _gameplayOldFar;
    float _rearFov = RearFovStanding;
    /// <summary>The follow-behind view's lens: narrower standing, wider at speed (a wide field of view makes the roadside race past faster), a smooth step over 0..45 m/s, eased in time.</summary>
    const float RearFovStanding = 64f, RearFovFast = 76f;

    // Schools and recordings own their camera. Scripted failures, towing and plan endings do too.
    bool GameplayCameraAllowed => (_state is State.Countdown or State.Racing or State.Paused or State.Finished or State.Failed || ViewerMode && _state == State.Title)
        && !InSchool && !PresentationActive && !_tourCapture && !_cinematicTour
        && !PlanHasCar && !FateHasCar && !FuelHasCar
        && !_followTank && !_followJet && !_followKozlik && !_followBus && !_followBabushka
        && !_followSlurry && !_followTractor && _lookAt == null;

    void GameplayCameraInput()
    {
        if (_lanLeaveAsk || !HasDiscoveredCamera || !GameplayCameraAllowed || !Input.IsActionJustPressed("camera")) return;
        CycleGameplayCamera();
        _hud.ShowAlways(GameplayCameraName, null, 1.5f, _gameplayView != GameplayView.TopDown ? UI.Hud.Gold : UI.Hud.Paper);
    }

    // Run before ALL camera owners, so a scripted shot never inherits cinematic projection.
    void PrepareGameplayCamera()
    {
        if (_cameraDiscoveries?.Has(_track.Id, _gameplayView) != true) _gameplayView = GameplayView.TopDown;
        if (!_gameplayCameraActive || _gameplayView != GameplayView.TopDown && GameplayCameraAllowed) return;
        _gameplayCameraActive = false;
        _gameplayCamera.Reset(); _bonnetFeel.Reset();
        _cam.Fov = _gameplayOldFov;
        _cam.Near = _gameplayOldNear;
        _cam.Far = _gameplayOldFar;
        _cam.Snap(_car.Pos);
    }

    bool GameplayCamera(float dt)
    {
        if (_gameplayView == GameplayView.TopDown || !GameplayCameraAllowed) return false;
        if (!_gameplayCameraActive)
        {
            _gameplayOldFov = _cam.Fov;
            _gameplayOldNear = _cam.Near;
            _gameplayOldFar = _cam.Far;
            _gameplayCameraActive = true;
        }
        else if (_state == State.Paused && _gameplayLastView == _gameplayView) return true;
        if (_gameplayLastView != _gameplayView) { _gameplayCamera.Reset(); _bonnetFeel.Reset(); _rearFov = RearFovStanding; }
        _gameplayLastView = _gameplayView;
        if (_gameplayView == GameplayView.Bonnet)
        {
            var bonnet = GameplayBonnetCamera.Frame(_car.Pos, _car.Rot);
            _cam.Fov = 75f;
            _cam.Near = 0.06f;
            _cam.Far = 600f;
            if (_cam.ShakeEnabled)
            {
                var felt = _bonnetFeel.Apply(bonnet.eye, bonnet.target, _car.Rot, _car.Vel, dt, _cam.Rumble);
                _cam.Pose(felt.eye, felt.target, _car.Pos, dt, felt.roll);
            }
            else _cam.Pose(bonnet.eye, bonnet.target, _car.Pos, dt);
            return true;
        }
        var frame = _gameplayCamera.Frame(_car.Pos, _car.Vel, _car.Forward, dt, _track.MeshHeight, _cam.Rumble);
        float rearSpeed = new Vector2(_car.Vel.X, _car.Vel.Z).Length();
        float rearSp = Math.Clamp(rearSpeed / 45f, 0f, 1f); rearSp = rearSp * rearSp * (3f - 2f * rearSp);
        _rearFov = Mathf.Lerp(_rearFov, Mathf.Lerp(RearFovStanding, RearFovFast, rearSp), 1f - MathF.Exp(-2.5f * Math.Clamp(dt, 0f, 0.05f)));
        _cam.Fov = _rearFov;      // (a wide lens that widens with the speed: the roadside races past faster in a close chase)
        _cam.Near = 0.2f;
        _cam.Far = 600f;
        _cam.Pose(frame.eye, frame.target, _car.Pos, dt, frame.roll);
        return true;
    }
}
