using System;
using System.Collections.Generic;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// VISUALS (under SETTINGS): the map, the comments, the camera (where there is one to choose; found cameras only, as before), edges and softening. Edges and softening alone stand behind the
/// CAPITALIST LUXURIES ENABLED notice (RaceScene.cs sets the flag from them); the map, the comments and the camera never do. It opens over SETTINGS and gives it back at the same row. Rows are found by id (VisualRowId).
/// </summary>
public partial class RaceScene
{
    List<VisualRowId> VisualRowsNow
    {
        get
        {
            var l = new List<VisualRowId> { VisualRowId.Comments, VisualRowId.LapInfo };
            if (_run == RunMode.Rally && _norms.Enabled && !InLanRace) l.Add(VisualRowId.NormsCard);
            l.Add(VisualRowId.Map); l.Add(VisualRowId.CarInstruments);
            if (_hud.GameplayCameraName != null) l.Add(VisualRowId.Camera);
            l.Add(VisualRowId.Edges); l.Add(VisualRowId.Softening); l.Add(VisualRowId.Back);
            return l;
        }
    }

    void SyncVisualHud()
    {
        _hud.VisualRowIds = VisualRowsNow.ToArray();
        _hud.VisualCameraName = _hud.GameplayCameraName == null ? null : _hud.GameplayCameraName.Replace(" CAMERA", "");
        if (_hud.VisualOpen) _hud.VisualSelection = Math.Clamp(_hud.VisualSelection, 0, _hud.VisualRowIds.Length - 1);
    }

    bool VisualInput(bool alt)
    {
        if (!_hud.VisualOpen) return false;
        if (_hud.CameraHintOpen)
        {
            if (Input.IsActionJustPressed("pause") || Input.IsActionJustPressed("start") && !alt) CloseCameraHint();
            return true;
        }
        int n = _hud.VisualRowIds.Length;
        if (Input.IsActionJustPressed("pause")) CloseVisual();
        else if (Input.IsActionJustPressed("accelerate")) _hud.VisualSelection = (_hud.VisualSelection + n - 1) % n;
        else if (Input.IsActionJustPressed("brake")) _hud.VisualSelection = (_hud.VisualSelection + 1) % n;
        else if (Input.IsActionJustPressed("steer_left")) ChooseVisual(_hud.VisualSelection, -1);
        else if (Input.IsActionJustPressed("steer_right") || Input.IsActionJustPressed("start") && !alt) ChooseVisual(_hud.VisualSelection);
        return true;
    }

    void OpenVisual()
    {
        _hud.SetVisualOpen(true);
        SyncVisualHud();
    }

    void CloseVisual()
    {
        _hud.SetVisualOpen(false);      // (SETTINGS is still open underneath, at its VISUALS row)
        _overlayExitFrame = Engine.GetProcessFrames();
    }

    void CloseCameraHint()
    {
        _hud.SetCameraHintOpen(false);
        _overlayExitFrame = Engine.GetProcessFrames();
    }

    void ChooseVisual(int row, int step = 1)
    {
        var rows = _hud.VisualRowIds;
        if (row < 0 || row >= rows.Length) return;
        _hud.VisualSelection = row;
        switch (rows[row])
        {
            case VisualRowId.Map: ToggleMinimap(); break;
            case VisualRowId.Comments: _hud.SetComments(!_hud.Comments, confirm: true); break;
            case VisualRowId.Camera: ChooseCamera(step); break;
            case VisualRowId.Edges: StepAntialiasing(step); _hud.AntialiasingName = AaNames[_aaMode]; break;
            case VisualRowId.Softening: StepSoftening(step); _hud.SofteningName = SoftNames[_softLevel]; break;
            case VisualRowId.CarInstruments: SetHudChoice("car", _hud.CarInstrumentsOn = !_hud.CarInstrumentsOn); break;
            case VisualRowId.LapInfo: SetHudChoice("lap", _hud.LapInfoOn = !_hud.LapInfoOn); break;
            case VisualRowId.NormsCard: SetHudChoice("norms", _hud.NormsCardOn = !_hud.NormsCardOn); break;
            case VisualRowId.Back: CloseVisual(); break;
        }
    }

    /// <summary>The camera row: the next (or previous) camera this personnel file has found on this track; with none found the hidden-cameras hint. Found cameras only: the unlocks stay discoveries.</summary>
    void ChooseCamera(int step)
    {
        if (_hud.CameraUnlocksMissing && !_hud.CameraSomeFound)
        {
            _hud.SetCameraHintOpen(true);
            return;
        }
        for (int n = 1; n <= 3; n++)
        {
            var candidate = (GameplayView)(((int)_gameplayView + step * n + 6) % 3);
            if (_cameraDiscoveries?.Has(_track.Id, candidate) != true) continue;
            _gameplayView = candidate;
            _hud.GameplayCameraName = GameplayCameraName;
            break;
        }
        if (_hud.CameraUnlocksMissing) _hud.SetCameraHintOpen(true);
        SyncVisualHud();
    }
}
