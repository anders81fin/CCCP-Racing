using CCCPRacing.Audio;
using System;
using System.Collections.Generic;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// The minimap (Hud.DrawMinimap): the track's centre line scaled into a box bottom-left, north up,
/// the car, the ghost and a LAN race's comrades on it. In a run only (not the title, the school or
/// the end screens). M (a pad's R3) turns it on and off, kept in settings.cfg ([hud] minimap, on by default).
/// </summary>
public partial class RaceScene
{
    bool _minimapOn = true;
    Vector2 _mapMin, _mapSpan;

    /// <summary>After the track is built: its road as the map's line, and the setting.</summary>
    void InitMinimap()
    {
        var c = _track.Center;
        Vector2 lo = c[0], hi = c[0];
        foreach (var p in c) { lo = lo.Min(p); hi = hi.Max(p); }
        if (_drunksAll?.MechanicPostPosition is Vector2 service)
        {
            lo = lo.Min(service - Vector2.One * 4f);
            hi = hi.Max(service + Vector2.One * 4f);
        }
        if (_fuelOnTrack && _depot != null)
        {
            lo = lo.Min(_depot.Zone - Vector2.One * 4f);
            hi = hi.Max(_depot.Zone + Vector2.One * 4f);
        }
        if (_rad != null)
        {
            var wash = new Vector2(_rad.WashX, _rad.WashZ);
            lo = lo.Min(wash - Vector2.One * 4f);
            hi = hi.Max(wash + Vector2.One * 4f);
        }
        float margin = 6f;
        _mapMin = lo - new Vector2(margin, margin);
        _mapSpan = (hi - lo) + new Vector2(2 * margin, 2 * margin);
        var pts = new List<Vector2>();
        for (int i = 0; i < c.Length; i += 4) pts.Add(MapPoint(c[i]));
        _hud.MinimapPath = pts.ToArray();
        _hud.MinimapAspect = _mapSpan.X / _mapSpan.Y;
        _hud.MinimapMechanic = _drunksAll?.MechanicPostPosition is Vector2 post ? MapPoint(post) : null;
        _hud.MinimapStart = MapPoint(c[0]);
        _hud.MinimapStartDir = _track.CenterDir(0);
        var cfg = new ConfigFile();
        if (cfg.Load(_records.Dir + "settings.cfg") == Error.Ok && cfg.GetValue("hud", "minimap", true) is { VariantType: Variant.Type.Bool } v)
            _minimapOn = (bool)v;
        if (cfg.GetValue("hud", "car", true) is { VariantType: Variant.Type.Bool } hc) _hud.CarInstrumentsOn = (bool)hc;
        if (cfg.GetValue("hud", "lap", true) is { VariantType: Variant.Type.Bool } hl) _hud.LapInfoOn = (bool)hl;
        if (cfg.GetValue("hud", "norms", true) is { VariantType: Variant.Type.Bool } hn) _hud.NormsCardOn = (bool)hn;
        if (InstructionTrack == null) { LoadAntialiasing(cfg); SoundBalance.Load(cfg); }
        LabInit(cfg, Array.IndexOf(CmdArgs(), "--lab") >= 0);      // RaceScene.Lab.cs
    }

    Vector2 MapPoint(Vector2 world) => (world - _mapMin) / _mapSpan;

    /// <summary>M (R3): the map on or off, remembered.</summary>
    void ToggleMinimap()
    {
        _minimapOn = !_minimapOn;
        _hud.ShowAlways(_minimapOn ? "MAP ON" : "MAP OFF", null, 1.5f, _minimapOn ? Hud.Gold : Hud.Paper);
        string path = _records.Dir + "settings.cfg";
        var cfg = new ConfigFile();
        cfg.Load(path);       // keep the other sections
        cfg.SetValue("hud", "minimap", _minimapOn);
        cfg.Save(path);
    }

    /// <summary>A VISUALS HUD choice, remembered in settings.cfg [hud] next to the map.</summary>
    void SetHudChoice(string key, bool on)
    {
        string path = _records.Dir + "settings.cfg";
        var cfg = new ConfigFile();
        cfg.Load(path);
        cfg.SetValue("hud", key, on);
        cfg.Save(path);
    }

    /// <summary>Per frame: what the map shows now.</summary>
    void MinimapHud()
    {
        _hud.MapOn = _minimapOn;
        _hud.Minimap = _minimapOn && !InSchool && !_gulag && !_planScreen && !Replaying
                       && _state is State.Countdown or State.Racing or State.Paused or State.Failed or State.Finished;
        if (!_hud.Minimap) return;
        _hud.MinimapWash = _rad != null ? MapPoint(new Vector2(_rad.WashX, _rad.WashZ)) : null;
        _hud.MinimapFuel = _fuelOnTrack && _depot != null
            && (InLanRace ? _lanReveal?.Fuel == true : _depot.Visible) ? MapPoint(_depot.Zone) : null;
        _hud.MinimapCar = MapPoint(new Vector2(_car.Pos.X, _car.Pos.Z));
        _hud.MinimapCarColor = InLanRace ? CCCPRacing.Net.LanRace.TagFor(_net.Me?.Slot ?? 0) : Hud.Gold;
        _hud.MinimapCarDir = new Vector2(_car.Forward.X, _car.Forward.Z);
        _hud.MinimapGhost = _ghostVis.Visible ? MapPoint(new Vector2(_ghostCar.Pos.X, _ghostCar.Pos.Z)) : null;
        _hud.MinimapOthers.Clear();
        if (InLanRace)
            foreach (var rc in _remote.Values) _hud.MinimapOthers.Add((MapPoint(new Vector2(rc.Puppet.Pos.X, rc.Puppet.Pos.Z)),CCCPRacing.Net.LanRace.TagFor(rc.Slot)));
    }
}
