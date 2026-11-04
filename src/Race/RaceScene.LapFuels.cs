using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CCCPRacing.Race;

/// <summary>
/// Stakhanov laps change the fuel (the car) with the number keys in the middle of a lap. The lap remembers which fuel it had when (LapMeta.Fuels, "seconds:grade" pairs), so the replay
/// viewer shows the car of the moment: its wing, kit, paint and engine voice. Rally laps need none (the record set tells the fuel); a lap from before this has none and shows as petrol.
/// </summary>
public partial class RaceScene
{
    readonly List<(float T, int Grade)> _lapFuels = new();
    (float T, int Grade)[] _viewerFuels;

    /// <summary>A lap starts (or restarts): its fuel is the one in use.</summary>
    void LapFuelsReset()
    {
        _lapFuels.Clear();
        if (_run == RunMode.Training && Training.GradeOf(_power) is FuelGrade g) _lapFuels.Add((0f, (int)g));
    }

    /// <summary>The fuel has just changed (SetPower).</summary>
    void LapFuelsNote()
    {
        if (_run != RunMode.Training || Training.GradeOf(_power) is not FuelGrade g) return;
        float t = MathF.Max(0f, _lapClock);
        if (_lapFuels.Count > 0 && _lapFuels[^1].Grade == (int)g) return;
        if (_lapFuels.Count > 0 && _lapFuels[^1].T >= t) _lapFuels.RemoveAt(_lapFuels.Count - 1);      // (a change before the lap began replaces the first)
        _lapFuels.Add((t, (int)g));
    }

    string LapFuelsText() => string.Join(",", _lapFuels.Select(f => f.T.ToString("0.##", CultureInfo.InvariantCulture) + ":" + f.Grade));

    static (float T, int Grade)[] ParseLapFuels(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var list = new List<(float, int)>();
        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split(':');
            if (kv.Length == 2 && float.TryParse(kv[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float t) && int.TryParse(kv[1], out int g) && g >= 0 && g < FuelGrades.Count) list.Add((t, g));
        }
        return list.Count == 0 ? null : list.ToArray();
    }

    /// <summary>The viewer's fuel at the replay clock, or null when the lap has no fuel list (the record set tells, as before).</summary>
    FuelGrade? ViewerFuelNow()
    {
        if (!ViewerMode || _viewerFuels == null) return null;
        int at = 0;
        for (int i = 0; i < _viewerFuels.Length; i++) if (_viewerFuels[i].T <= _replayClock) at = i;
        return (FuelGrade)_viewerFuels[at].Grade;
    }
}
