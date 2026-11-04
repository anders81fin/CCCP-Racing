using System.Globalization;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

public partial class RaceScene
{
    string ResumeSignature => _records.Signature + ":" + string.Join(",",
        System.Array.ConvertAll(NormPlan.Ladder, t => t.ToString("R", CultureInfo.InvariantCulture)));
    bool _resumeLoaded;

    void AskLeaveRally()
    {
        if (!_norms.Enabled || _norms.Finished || _norms.Step == 0) { ReturnRallyToTrackSelect(); return; }
        _hud.RallyExitSelection = 0;
        _hud.RallyExitError = null;
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
            ? RallyResume.Save(FileDir, _trackId, ResumeSignature, new RallyResume.Checkpoint(_norms.Step, _norms.Left, _norms.Used,
                _wrecks, _runClock, _runLaps, _dayNight.LapOffset) { PassedTimes = _norms.PassedTimes, PassedTargets = _norms.PassedTargets }, _rallyFuel)
            : RallyResume.Clear(FileDir, _trackId);
        if (error != Error.Ok)
        {
            _hud.RallyExitError = "SAVE FAILED - TRY AGAIN OR CANCEL";
            return;
        }
        CloseRallyExit();
        ReturnRallyToTrackSelect();
    }

    void RestoreRallyRun()
    {
        _resumeLoaded = false;
        _hud.PauseNormsResumed = false;
        if (_newRallyTrack == _trackId) return;
        if (RallyResume.Load(FileDir, _trackId, ResumeSignature) is not { } point) return;
        _norms.Restore(point.Step, point.Left, point.Used, point.PassedTimes, point.PassedTargets);
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
