using System.Globalization;
using System.Linq;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// Development-only (not in the release build): <c>--camera-boost-probe</c> prints, every rendered frame, where the overhead camera, its view centre and the car are and whether the
/// OCTANE BOOSTER (or its replay) is on, so PhysicsTest.CameraBoost can measure the continuity of the real camera across the boost's start and end in a real run and in a Watch.
/// </summary>
public partial class RaceScene
{
    bool _camBoostProbe, _camBoostProbeRead;

    // --power-switch=T:P,T:P (Stakhanov laps): at lap clock T the real SetPower(P) runs, as the number keys do (the camera test of the power switch, PhysicsTest.CameraStakhanov).
    System.Collections.Generic.List<(float at, int pct)> _powerSwitches; int _powerSwitchNext;
    void PowerSwitchProbe()
    {
        if (_powerSwitches == null)
        {
            _powerSwitches = new();
            foreach (string a in CmdArgs()) if (a.StartsWith("--power-switch="))
                foreach (var part in a["--power-switch=".Length..].Split(','))
                { var x = part.Split(':'); _powerSwitches.Add((float.Parse(x[0], CultureInfo.InvariantCulture), int.Parse(x[1]))); }
        }
        if (_powerSwitchNext < _powerSwitches.Count && _state == State.Racing && _lapClock >= _powerSwitches[_powerSwitchNext].at)
        { SetPower(_powerSwitches[_powerSwitchNext].pct); GD.Print($"powerswitch t={_lapClock:F3} -> {_powerSwitches[_powerSwitchNext].pct} %"); _powerSwitchNext++; }
    }

    // --pause-probe=T: at lap clock T the game is paused as the Esc key does, then the camera's pose is printed every frame for 3 s (the view behind the PARTY MEETING must not move).
    float _ppAt = -1; int _ppFrames; bool _ppRead; Vector3 _ppLast;
    void PauseProbe()
    {
        if (!_ppRead) { _ppRead = true; foreach (string a in CmdArgs()) if (a.StartsWith("--pause-probe=")) _ppAt = float.Parse(a["--pause-probe=".Length..], CultureInfo.InvariantCulture); }
        if (_ppAt < 0) return;
        if (_ppFrames == 0 && _state == State.Racing && _lapClock >= _ppAt) { _state = State.Paused; _hud.Mode = CCCPRacing.UI.HudMode.Paused; _ppFrames = 1; _ppLast = _cam.GlobalPosition; return; }
        if (_ppFrames == 0) return;
        var c = _cam.GlobalPosition; var f = _cam.Focus;
        GD.Print($"pauseprobe n={_ppFrames} cam={c.X:F4},{c.Y:F4},{c.Z:F4} move={(c - _ppLast).Length():F5} car={_car.Pos.X:F3},{_car.Pos.Z:F3} vel={_car.Vel.Length():F2}");
        _ppLast = c;
        if (++_ppFrames > 180) CCCPRacing.Audio.AudioMix.Quit(this);
    }

    partial void CameraBoostProbe()
    {
        PauseProbe();
        PowerSwitchProbe();
        if (!_camBoostProbeRead) { _camBoostProbeRead = true; _camBoostProbe = System.Array.IndexOf(CmdArgs(), "--camera-boost-probe") >= 0; }
        if (!_camBoostProbe || _state is State.Paused) return;
        var c = _cam.GlobalPosition; var f = _cam.Focus; var p = _car.Pos;
        string F(float v) => v.ToString("F3", CultureInfo.InvariantCulture);
        GD.Print($"camprobe {Engine.GetProcessFrames()} t={F(Replaying ? _replayClock : _lapClock)} boost={(_boostLeft > 0 ? 1 : 0)} replay={(Replaying ? 1 : 0)} cam={F(c.X)},{F(c.Z)} focus={F(f.X)},{F(f.Z)} car={F(p.X)},{F(p.Z)} speed={F(_car.Speed)} maxlead={(float.IsFinite(_cam.MaxLead) ? F(_cam.MaxLead) : "inf")}");
    }
}
