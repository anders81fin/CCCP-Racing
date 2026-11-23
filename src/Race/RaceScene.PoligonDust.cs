using System;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Race;

public partial class RaceScene
{
    PoligonDust _poligonDust;
    PoligonVegetation _poligonVeg;

    /// <summary>
    /// The one clock the Poligon's dust reads: the lap clock in a drive, the recording's own clock in the title replay and the viewer (so a seek, a loop and the replay's end need no state to restore). The
    /// lap clock and the ghost's time start together (every _rec.Clear is paired with _lapClock = 0), so the picture at a recorded time is the picture that was seen then.
    /// </summary>
    float AmbientClock => Replaying || ViewerMode ? _replayClock : _lapClock + DustTestOffset;

    /// <summary>Test flag --poligon-dust-at=SECONDS: the live drive's dust clock runs this far ahead of the lap clock (a review drive meets a gust at the ridge). 0 in play; the replay never uses it.</summary>
    static readonly float DustTestOffset = ReadDustOffset();
    static float ReadDustOffset()
    {
        foreach (string a in OS.GetCmdlineUserArgs())
            if (a.StartsWith("--poligon-dust-at=") && float.TryParse(a["--poligon-dust-at=".Length..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v)) return v;
        return 0f;
    }

    void PoligonDustFrame()
    {
        if (_poligonDust == null) return;
        // the dust takes the colour of the light there is: the sun's and the sky's (pale and warm by day, nearly black at night)
        Color sun = _sunLight != null ? _sunLight.LightColor * _sunLight.LightEnergy : Colors.White, amb = _env != null ? _env.AmbientLightColor * _env.AmbientLightEnergy : Colors.White;
        var light = new Color(Math.Clamp(sun.R * 0.5f + amb.R * 0.75f, 0f, 1f), Math.Clamp(sun.G * 0.5f + amb.G * 0.75f, 0f, 1f), Math.Clamp(sun.B * 0.5f + amb.B * 0.75f, 0f, 1f));
        _poligonDust.Update(AmbientClock, light);
    }
}
