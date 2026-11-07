using System.Linq;
using Godot;

namespace CCCPRacing.Race;

public partial class RaceScene
{
    FuelGrade _stakhanovBoardFuel;
    bool StakhanovBoardAvailable => _run == RunMode.Training && _state != State.Title && !InLanRace && !ViewerMode;

    void OpenStakhanovBoard()
    {
        if (!StakhanovBoardAvailable) return;
        _stakhanovBoardFuel = Training.GradeOf(_power) ?? FuelGrade.Petrol;
        _hud.StakhanovBoardOpen = true;
        StakhanovBoardHud();
    }

    void StakhanovBoardHud()
    {
        _hud.StakhanovMiniBoard = StakhanovBoardAvailable;
        if (!_hud.StakhanovMiniBoard) { _hud.StakhanovBoardOpen = false; return; }
        var grade = _hud.StakhanovBoardOpen ? _stakhanovBoardFuel : Training.GradeOf(_power);
        _hud.StakhanovBoardTrack = TrackName(_trackId);
        _hud.StakhanovBoardFuel = grade is FuelGrade g ? FuelGrades.Names[(int)g] : Training.FuelName(_power);
        int power = grade is FuelGrade f ? Training.PercentForKey((int)f + 1) : _power;
        _hud.StakhanovBoardRows = _boards[1].Entries.Where(e => e.Power == power).Take(Leaderboard.StakhanovSize).ToArray();
    }

    bool StakhanovBoardRawInput(InputEvent e)
    {
        if (!_hud.StakhanovBoardOpen) return false;
        if (PadRawInput(e)) return true;
        if (e is InputEventKey { Pressed: true, Echo: false } k)
        {
            if (k.Keycode is Key.Escape or Key.Enter or Key.KpEnter)
            {
                _hud.StakhanovBoardOpen = false;
                _overlayExitFrame = Engine.GetProcessFrames();
            }
            else if (k.Keycode is Key.Left or Key.Right)
            {
                _stakhanovBoardFuel = (FuelGrade)(((int)_stakhanovBoardFuel + (k.Keycode == Key.Left ? 3 : 1)) % Training.LapsCount);
                StakhanovBoardHud();
            }
        }
        // No gameplay shortcuts, track changes or mouse hit-through while browsing.
        return true;
    }
}
