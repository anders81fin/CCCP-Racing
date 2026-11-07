using System;

namespace CCCPRacing.Race;

public partial class RaceScene
{
    // A session is one start/restart. Mixed-fuel laps use their highest fuel,
    // exactly as the persistent Stakhanov board does.
    readonly float[] _fuelSessionBest = { -1f, -1f, -1f, -1f };
    readonly float[] _fuelSessionLast = { -1f, -1f, -1f, -1f };
    readonly bool[] _fuelSessionVoid = new bool[Training.LapsCount];

    void StakhanovSessionReset()
    {
        Array.Fill(_fuelSessionBest, -1f);
        Array.Fill(_fuelSessionLast, -1f);
        Array.Clear(_fuelSessionVoid);
    }

    void StakhanovSessionLap(float time, bool ranked, bool isVoid, bool fake)
    {
        if (_run != RunMode.Training || InLanRace || ViewerMode || fake ||
            !float.IsFinite(time) || time <= 0 || Training.GradeOf(_lapPeak) is not FuelGrade grade) return;
        int i = (int)grade;
        _fuelSessionLast[i] = time;
        _fuelSessionVoid[i] = isVoid;
        if (ranked && !isVoid && (_fuelSessionBest[i] < 0 || time < _fuelSessionBest[i])) _fuelSessionBest[i] = time;
    }

    void StakhanovSessionHud()
    {
        if (_run != RunMode.Training || _state == State.Title || InLanRace || ViewerMode ||
            Training.GradeOf(_power) is not FuelGrade grade) return;
        int i = (int)grade;
        _hud.BestIsSession = true;
        _hud.SessionBestLap = _fuelSessionBest[i];
        _hud.LastLap = _fuelSessionLast[i];
        _hud.LastLapVoid = _fuelSessionVoid[i];
    }
}
