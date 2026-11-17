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

    partial void CameraBoostProbe()
    {
        PowerSwitchProbe();
        if (!_camBoostProbeRead) { _camBoostProbeRead = true; _camBoostProbe = System.Array.IndexOf(CmdArgs(), "--camera-boost-probe") >= 0; }
        if (!_camBoostProbe || _state is State.Paused) return;
        var c = _cam.GlobalPosition; var f = _cam.Focus; var p = _car.Pos;
        string F(float v) => v.ToString("F3", CultureInfo.InvariantCulture);
        GD.Print($"camprobe {Engine.GetProcessFrames()} t={F(Replaying ? _replayClock : _lapClock)} boost={(_boostLeft > 0 ? 1 : 0)} replay={(Replaying ? 1 : 0)} cam={F(c.X)},{F(c.Z)} focus={F(f.X)},{F(f.Z)} car={F(p.X)},{F(p.Z)} speed={F(_car.Speed)} maxlead={(float.IsFinite(_cam.MaxLead) ? F(_cam.MaxLead) : "inf")}");
    }
}
