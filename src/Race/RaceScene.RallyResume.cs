using System;
using System.Globalization;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

public partial class RaceScene
{
    string ResumeSignature => _records.Signature + ":" + string.Join(",",
        System.Array.ConvertAll(NormPlan.Ladder, t => t.ToString("R", CultureInfo.InvariantCulture)));
    bool _resumeLoaded, _resumeStale;

    void AskLeaveRally()
    {
        // Nothing to keep before the first norm is beaten; and on the window's last lap a resumed rally would have no lap left (the lap abandoned counts as used), so nothing is offered either
        if (!_norms.Enabled || _norms.Finished || _norms.Step == 0 || _norms.OnLastLap) { ReturnRallyToTrackSelect(); return; }
        _hud.RallyExitSelection = 0;
        _hud.RallyExitError = null;
        _hud.RallyExitRevenge = ActiveRevenges().Length > 0;
        _hud.SetRallyExitOpen(true);
    }

    bool RallyExitInput()
    {
        if (!_hud.RallyExitOpen) return false;
        if (Input.IsActionJustPressed("pause")) CloseRallyExit();
        else if (Input.IsActionJustPressed("accelerate")) _hud.RallyExitSelection = (_hud.RallyExitSelection + 2) % 3;
        else if (Input.IsActionJustPressed("brake")) _hud.RallyExitSelection = (_hud.RallyExitSelection + 1) % 3;
        else if (Input.IsActionJustPressed("start") && !Input.IsKeyPressed(Key.Alt)) ChooseRallyExit(_hud.RallyExitSelection);
        return true;
    }

    void CloseRallyExit()
    {
        _hud.SetRallyExitOpen(false);
        _overlayExitFrame = Engine.GetProcessFrames();
    }

    void ChooseRallyExit(int selection)
    {
        if (selection == 2) { CloseRallyExit(); return; }
        Error error = selection == 0
            ? RallyResume.Save(FileDir, _trackId, ResumeSignature, RallyCheckpointNow(), _rallyFuel)
            : RallyResume.Clear(FileDir, _trackId);
        if (error != Error.Ok)
        {
            _hud.RallyExitError = "SAVE FAILED - TRY AGAIN OR CANCEL";
            return;
        }
        CloseRallyExit();
        ReturnRallyToTrackSelect();
    }

    /// <summary>The rally as it stands: the norm, the laps, and the car (the engine's health, the dose, the fuel) so that a resumed rally goes on with the same car.</summary>
    RallyResume.Checkpoint RallyCheckpointNow() => new(_norms.Step, _norms.Left, _norms.Used, _wrecks, _runClock, _runLaps, _dayNight.LapOffset)
    {
        PassedTimes = _norms.PassedTimes, PassedTargets = _norms.PassedTargets,
        Health = _health.Enabled ? _health.Health : -1f,
        Dose = _rad != null && _doseOn && _rad.Limit > 0f ? _dose / _rad.Limit : -1f,
        Litres = _tank.Enabled ? _tank.Litres : -1f,
        Revenges = ActiveRevenges(),
        Body = _carVis?.Damage is { IsDamaged: true } body ? body.SavedText() : "",
    };

    void RestoreRallyRun()
    {
        _resumeLoaded = false;
        _hud.PauseNormsResumed = false;
        if (_newRallyTrack == _trackId) return;
        if (RallyResume.Load(FileDir, _trackId, ResumeSignature) is not { } point)
        {
            // A save that no longer fits (the norms or the profile changed after it was saved, or the file is damaged) cannot be
            // resumed: the run starts from the first norm, so the save is dropped and the player told, not offered again.
            if (RallyResume.Exists(FileDir, _trackId) && RallyResume.Clear(FileDir, _trackId) == Error.Ok) _resumeStale = true;
            return;
        }
        // The lap that was being driven when the rally was saved is used up: one lap less in the window (a save from before this rule, with one lap left, keeps it)
        _norms.Restore(point.Step, Math.Max(1, point.Left - 1), point.Used, point.PassedTimes, point.PassedTargets);
        if (point.Health >= 0f && _health.Enabled) { _health.Set(point.Health); ApplyTrainingPower(); }
        if (point.Dose >= 0f && _rad != null && _doseOn) _dose = point.Dose * _rad.Limit;
        if (point.Litres >= 0f && _tank.Enabled) _tank.Litres = Math.Min(point.Litres, _tank.Capacity);
        if (!string.IsNullOrEmpty(point.Body)) _carVis?.Damage?.RestoreSaved(point.Body);      // (the dents, the glass and the parts as they were)
        _resumeRevenges.Clear(); _resumeRevengeWait = 0;
        foreach (var token in point.Revenges.Split(',', StringSplitOptions.RemoveEmptyEntries)) _resumeRevenges.Add(token);      // (they begin again when the run does: RaceScene.Revenge.cs)
        _wrecks = point.Wrecks;
        _runClock = point.RunTime;
        _runLaps = point.RunLaps;
        _dayNight.LapOffset = point.DayOffset;
        _dnDraw = false;
        _timer.RestoreCompletedLaps(point.Used);
        _runMedal = NormPlan.MedalFor(point.Step);
        _fileAlert = true;
        _goAlert = null;
        _resumeLoaded = true;
        _hud.PauseNormsResumed = true;
    }
}
