using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CCCPRacing.Lab;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// LAB: sliders on the live parameters of the car (CarLab), while driving or in the paused menu. LAB MODE (SETTINGS) makes the NEXT run a lab run: it writes no
/// record, ghost, lap, tape or ranking (a lab run is unranked and has no records), and carries the LAB mark. Without LAB MODE every run starts from the car's own
/// defaults. F2 (pad Y) opens the panel over the race and slows the game; SETTINGS > LAB TUNING opens it over the paused game.
/// Keys: PgUp/PgDn row, Home/End adjust, , and . group, Insert step (or run the action row), Delete resets the row; in the menu also the arrows and Enter.
/// </summary>
public partial class RaceScene
{
    CarLab _lab;
    bool _labMode, _labRun, _labOpen, _labLive, _labOwnsTime, _labDirty;
    int _labGroup, _labRow;
    LabStep _labStep = LabStep.Normal;
    float _labSlow = 0.25f, _labSaveIn;
    LabParam _labSlowParam;
    List<(LabParam P, string Action)> _labItems = new();

    /// <summary>A lab run: no record of any kind is written (a test, a replay viewer and LAN are never lab runs).</summary>
    public bool LabRun => _labRun;
    internal bool LabOpenNow => _labOpen;
    internal bool LabLiveNow => _labLive;
    internal CarLab Lab => _lab;
    internal bool NoRecords => _noRecord || _labRun;

    string LabCfgPath => _records.Dir + "lab.cfg";
    string LabGroupName => _labGroup == 0 ? "LAB" : _lab.Groups[_labGroup - 1];
    int LabGroupCount => 1 + _lab.Groups.Count;

    void LabInit(ConfigFile settings, bool forceOn)
    {
        if (ViewerMode || InstructionTrack != null || _params == null || _car == null) return;
        _lab = new CarLab(_params, _car.RefreshGeometry);
        _labSlowParam = new LabParam
        {
            Group = "LAB", Name = "SlowMotion", Label = "SLOW MOTION", Doc = "How slowly the game runs while the panel is open over the race. 1 is full speed.",
            Default = 0.25f, Min = 0.05f, Max = 1f, Get = () => _labSlow, Set = v => _labSlow = v,
        };
        _labMode = forceOn || settings.GetValue("lab", "enabled", false).AsBool();
        var lc = new ConfigFile();
        if (lc.Load(LabCfgPath) == Error.Ok)
        {
            if (lc.GetValue("lab", "slow", 0.25f).VariantType == Variant.Type.Float) _labSlowParam.Assign((float)lc.GetValue("lab", "slow", 0.25f).AsDouble());
            if (_labMode) _lab.Load(lc);
        }
        LabBuildItems();
    }

    void LabSaveNow()
    {
        if (_lab == null) return;
        var lc = new ConfigFile();
        _lab.Save(lc);
        foreach (var (p, value) in _labPreset)      // (the diesel's preset is not the owner's setting: it must not come back on petrol's next run)
            if (lc.HasSectionKey(CarLab.Section, p.Name) && MathF.Abs(p.Value - value) < 1e-4f * MathF.Max(1f, MathF.Abs(value))) lc.EraseSectionKey(CarLab.Section, p.Name);
        lc.SetValue("lab", "slow", _labSlow);
        lc.Save(LabCfgPath);
        _labDirty = false;
    }

    void LabSetMode(bool on)
    {
        if (_lab == null || _labMode == on) return;
        _labMode = on;
        string path = _records.Dir + "settings.cfg";
        var cfg = new ConfigFile();
        cfg.Load(path);
        cfg.SetValue("lab", "enabled", on);
        cfg.Save(path);
        if (on)
        {
            // Applies to the run in progress: it turns unranked at once (a lab run never writes records).
            var lc = new ConfigFile();
            if (lc.Load(LabCfgPath) == Error.Ok) _lab.Load(lc);
            LabRunStarts();
        }
        _hud.ShowAlways(on ? "LAB MODE ON" : "LAB MODE OFF", on ? (_labRun ? "THIS RUN IS NOW A LAB RUN" : "THE NEXT RUN IS A LAB RUN") : "THE NEXT RUN IS A NORMAL RUN", 2f, on ? Hud.Gold : Hud.Paper);
    }

    /// <summary>At every start and restart: the run is a lab run or a normal one. A normal one drives the car's own defaults.</summary>
    void LabRunStarts()
    {
        if (_lab == null) return;
        _labRun = _labMode && !InLanRace && !InSchool && !ViewerMode;
        _timer.Unranked = _labRun;
        if (!_labRun) { LabClose(); _lab.ResetAll(); _labPreset.Clear(); _engineFuelApplied = null; }
        LabApplyFuelPreset();
        _hud.LabRun = _labRun;
    }

    /// <summary>
    /// What a real diesel would be (docs/lab-mode.md, "The diesel preset"): a lower redline with taller gears, the torque low and wide instead of high and peaky. A lab run
    /// on DIESEL starts with these on the sliders (the fuel's 50 % power is not applied then: the torque curve is the character), unless a slider is already changed.
    /// Petrol and rocket fuel use the car's own defaults; so does a normal run.
    /// </summary>
    internal static readonly (string Name, float Value)[] DieselPreset = FuelGrades.DieselEngine;
    readonly List<(LabParam P, float Value)> _labPreset = new();
    FuelGrade? _labPresetFuel;
    /// <summary>The SPUTNIK's preset: the four-wheel drive (the engine is the jet fuel's, 175 % and the turbo, which a lab run gets as in a normal run).</summary>
    internal static readonly (string Name, float Value)[] JetPreset = { ("MaxTorque", FuelGrades.JetTorque) };
    internal static readonly (string Name, float Value)[] SputnikPreset = {
        ("MaxTorque", FuelGrades.SputnikTorque), ("Mass", FuelGrades.SputnikMass), ("DriveFront", FuelGrades.SputnikDriveFront), ("CenterLock", FuelGrades.SputnikCenterLock),
        ("TurboSpool", FuelGrades.SputnikTurboSpool), ("TurboKick", FuelGrades.SputnikTurboKick),
    };
    bool LabDieselPreset => _labRun && _run is RunMode.Rally or RunMode.Training && EngineFuel is FuelGrade.Diesel or FuelGrade.Rocket or FuelGrade.Sputnik;

    /// <summary>At the start of a run: the diesel's preset goes on the sliders (those still at their defaults) and comes off again when the fuel is another.</summary>
    void LabApplyFuelPreset()
    {
        if (_lab == null) return;
        bool want = LabDieselPreset;
        FuelGrade fuel = EngineFuel;
        // The fuel changed since the last lab run: the sliders start from that fuel's normal-run profile, whatever the last fuel left on them.
        if (_labRun && _labPresetFuel != null && _labPresetFuel != fuel) { _lab.ResetAll(); _labPreset.Clear(); }
        _labPresetFuel = _labRun ? fuel : null;
        if (want)
        {
            bool first = _labPreset.Count == 0;
            foreach (var (name, value) in fuel == FuelGrade.Sputnik ? SputnikPreset : fuel == FuelGrade.Rocket ? JetPreset : DieselPreset)
            {
                var p = _lab.Params.FirstOrDefault(x => x.Name == name);
                if (p == null) continue;
                bool mine = _labPreset.Any(x => x.P == p);
                if (mine) { if (!p.IsChanged) p.Assign(value); continue; }      // (RESET ALL took it off: a new run puts it back)
                if (p.IsChanged) continue;      // (a slider the owner has moved keeps the owner's value)
                p.Assign(value);
                _labPreset.Add((p, value));
            }
            if (first && _labPreset.Count > 0) _hud.ShowAlways(fuel == FuelGrade.Sputnik ? "SPUTNIK PRESET" : fuel == FuelGrade.Rocket ? "JET FUEL PRESET" : "DIESEL PRESET", fuel == FuelGrade.Sputnik ? "200 NM, 900 KG, 4WD 40-60, CENTRE LOCK, GROUP B TURBO" : fuel == FuelGrade.Rocket ? "200 NM, 175% POWER, TURBO" : "5000 RPM, 200 NM LOW AND WIDE, 1050 KG, FOUR CLOSE GEARS", 3f, Hud.Gold);
        }
        else if (!want && _labPreset.Count > 0)
        {
            foreach (var (p, value) in _labPreset) if (MathF.Abs(p.Value - value) < 1e-4f * MathF.Max(1f, MathF.Abs(value))) p.Reset();
            _labPreset.Clear();
        }
    }

    // ------------------------------------------------------------------ the panel

    void LabBuildItems()
    {
        _labItems = new();
        if (_labGroup == 0)
        {
            _labItems.Add((_labSlowParam, null));
            _labItems.Add((null, "RESET ALL"));
            _labItems.Add((null, "COPY DIFF"));
        }
        else foreach (var p in _lab.InGroup(_lab.Groups[_labGroup - 1])) _labItems.Add((p, null));
        _labRow = Math.Clamp(_labRow, 0, Math.Max(0, _labItems.Count - 1));
    }

    bool LabCanOpenLive => _lab != null && _labMode && _labRun && _state is State.Racing or State.Countdown && !_hud.SettingsOpen;

    void LabOpen(bool live)
    {
        if (_lab == null) return;
        _labOpen = true; _labLive = live;
        LabBuildItems();
        _hud.LabOpen = true;
    }

    void LabClose()
    {
        if (!_labOpen) return;
        _labOpen = false; _hud.LabOpen = false;
        if (_labDirty) LabSaveNow();
        _overlayExitFrame = Engine.GetProcessFrames();      // (the Esc or button that closed it does nothing else)
    }

    void LabToggleLive()
    {
        if (_labOpen) { LabClose(); return; }
        if (_lab == null || !_labMode) return;
        if (!_labRun) { _hud.ShowAlways("NOT A LAB RUN", "LAB MODE STARTS WITH THE NEXT RUN", 2.5f, Hud.Red); return; }
        if (LabCanOpenLive) LabOpen(true);
    }

    /// <summary>Per frame: the panel's content for the Hud, the slow motion, the saving of a changed value.</summary>
    void LabFrame(float dt)
    {
        if (_lab == null) return;
        if (_labOpen && _labLive && !LabCanOpenLive) LabClose();
        bool slow = _labOpen && _labLive;
        if (slow) { Engine.TimeScale = _labSlow; _labOwnsTime = true; }
        else if (_labOwnsTime) { Engine.TimeScale = 1; _labOwnsTime = false; }
        if (_labDirty && (_labSaveIn -= dt) <= 0) LabSaveNow();
        _hud.LabRun = _labRun;
        if (!_labOpen) return;
        var rows = new LabRowView[_labItems.Count];
        for (int i = 0; i < rows.Length; i++)
        {
            var (p, action) = _labItems[i];
            rows[i] = p != null
                ? new LabRowView { Label = p.Label, Value = p.Text, Fraction = p.Fraction, Changed = p.IsChanged }
                : new LabRowView { Label = action, Value = action == "RESET ALL" ? $"{_lab.ChangedCount} CHANGED" : null, Fraction = -1f };
        }
        _hud.LabRows = rows;
        _hud.LabSelected = _labRow;
        _hud.LabGroup = LabGroupName; _hud.LabGroupIndex = _labGroup; _hud.LabGroupCount = LabGroupCount;
        _hud.LabTitle = _labLive ? $"LAB  SLOW {LabParam.Format(_labSlow)}X  {_labStep.ToString().ToUpperInvariant()}" : $"LAB  PAUSED  {_labStep.ToString().ToUpperInvariant()}";
        var sel = _labItems.Count > 0 ? _labItems[_labRow] : default;
        _hud.LabDoc = sel.P != null ? (sel.P.DefaultText is var d ? (sel.P.Doc.Length > 0 ? sel.P.Doc + " " : "") + "DEFAULT " + d : "")
            : sel.Action == "RESET ALL" ? "Puts every parameter back to the car's own value." : sel.Action == "COPY DIFF" ? "Copies what differs from the defaults to the clipboard and writes lab-diff.txt." : "";
        _hud.LabHint = _padMode
            ? (_labLive ? "D-PAD ROW/ADJUST  LB/RB GROUP  A STEP  X RESET  Y CLOSE" : "D-PAD ROW/ADJUST  LB/RB GROUP  A STEP  X RESET  B BACK")
            : (_labLive ? "PGUP/PGDN ROW  HOME/END ADJUST  , . GROUP  INS STEP  DEL RESET  F2 CLOSE" : "ARROWS ROW/ADJUST  , . GROUP  ENTER STEP  DEL RESET  ESC BACK");
    }

    void LabMove(int rowDelta)
    {
        if (_labItems.Count == 0) return;
        _labRow = (_labRow + rowDelta + _labItems.Count) % _labItems.Count;
    }

    void LabGroupMove(int d)
    {
        _labGroup = (_labGroup + d + LabGroupCount) % LabGroupCount;
        _labRow = 0;
        LabBuildItems();
    }

    void LabAdjust(int dir)
    {
        if (_labItems.Count == 0) return;
        var (p, _) = _labItems[_labRow];
        if (p == null) return;
        p.Nudge(dir, _labStep);
        _labDirty = true; _labSaveIn = 0.6f;
    }

    void LabActivate()
    {
        if (_labItems.Count == 0) return;
        var (p, action) = _labItems[_labRow];
        if (p != null) { _labStep = (LabStep)(((int)_labStep + 1) % 3); return; }
        if (action == "RESET ALL") { _lab.ResetAll(); _labDirty = true; _labSaveIn = 0f; _hud.ShowAlways("ALL BACK TO DEFAULT", null, 1.5f, Hud.Paper); }
        else if (action == "COPY DIFF") LabCopyDiff();
    }

    void LabResetRow()
    {
        if (_labItems.Count == 0 || _labItems[_labRow].P is not { } p) return;
        p.Reset(); _labDirty = true; _labSaveIn = 0.6f;
    }

    void LabCopyDiff()
    {
        string diff = _lab.Diff();
        DisplayServer.ClipboardSet(diff);
        using var f = Godot.FileAccess.Open(_records.Dir + "lab-diff.txt", Godot.FileAccess.ModeFlags.Write);
        bool saved = f != null;
        f?.StoreString(diff);
        _hud.ShowAlways("DIFF COPIED", saved ? "ALSO IN LAB-DIFF.TXT" : null, 2f, Hud.Gold);
    }

    /// <summary>The raw key events of the panel. Over the race only the panel's own keys are taken (the driving keys go on); over the paused menu every key is.</summary>
    bool LabKeyInput(InputEventKey k)
    {
        if (_lab == null || !k.Pressed) return false;
        var c = k.PhysicalKeycode;
        if (!_labOpen)
        {
            if (c == Key.F2 && !k.Echo && _state is not (State.Title or State.Paused)) { LabToggleLive(); return true; }
            return false;
        }
        if (!k.Echo && (c == Key.F2 || c == Key.Escape)) { LabClose(); return true; }
        bool menu = !_labLive;
        switch (c)
        {
            case Key.Pageup or Key.Up when c == Key.Pageup || menu: case Key.W when menu: LabMove(-1); return true;
            case Key.Pagedown or Key.Down when c == Key.Pagedown || menu: case Key.S when menu: LabMove(1); return true;
            case Key.Home or Key.Left when c == Key.Home || menu: case Key.A when menu: LabAdjust(-1); return true;
            case Key.End or Key.Right when c == Key.End || menu: case Key.D when menu: LabAdjust(1); return true;
            case Key.Comma when !k.Echo: LabGroupMove(-1); return true;
            case Key.Period when !k.Echo: LabGroupMove(1); return true;
            case Key.Insert when !k.Echo: LabActivate(); return true;
            case Key.Enter or Key.KpEnter when menu && !k.Echo: LabActivate(); return true;
            case Key.Delete when !k.Echo: LabResetRow(); return true;
        }
        return menu;
    }

    /// <summary>The pad in the panel: the d-pad etc. become the panel's keys. Key.None: not the panel's.</summary>
    Key LabPadKey(JoyButton b, bool racing)
    {
        if (_lab == null) return Key.None;
        if (!_labOpen)
            return racing && b == JoyButton.Y && _labMode && _state is not (State.Title or State.Paused) ? Key.F2 : Key.None;
        if (b is JoyButton.Y or JoyButton.Start or JoyButton.Back && racing) return Key.F2;
        return b switch
        {
            JoyButton.DpadUp => racing ? Key.Pageup : Key.Up,
            JoyButton.DpadDown => racing ? Key.Pagedown : Key.Down,
            JoyButton.DpadLeft => racing ? Key.Home : Key.Left,
            JoyButton.DpadRight => racing ? Key.End : Key.Right,
            JoyButton.LeftShoulder => Key.Comma,
            JoyButton.RightShoulder => Key.Period,
            JoyButton.A => racing ? Key.Insert : Key.Enter,
            JoyButton.X => Key.Delete,
            JoyButton.B or JoyButton.Y or JoyButton.Start or JoyButton.Back => Key.Escape,
            _ => Key.None,
        };
    }

    void LabExit()
    {
        if (_labOwnsTime) { Engine.TimeScale = 1; _labOwnsTime = false; }
        if (_labDirty) LabSaveNow();
    }
}
