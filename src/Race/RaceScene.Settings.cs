using System;
using System.Collections.Generic;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// SETTINGS of the Party Meeting: SOUNDS, VISUALS, RUMBLE (where there is a pad, as before), KEYS and back. SOUNDS (RaceScene.SoundMixer.cs) and VISUALS (RaceScene.VisualSettings.cs) open over it and give it back at their row;
/// back from SETTINGS lands on the main menu at its SETTINGS row. ESC and the pad's B go one step back everywhere, and CONTINUE only from the main menu. Rows are found by id (SettingsRowId).
/// </summary>
public partial class RaceScene
{
    static void ApplyOnOff(bool current, bool? requested, Action toggle)
    {
        if (requested is not bool value || value != current) toggle();
    }

    List<SettingsRowId> SettingsRowsNow
    {
        get
        {
            var l = new List<SettingsRowId> { SettingsRowId.Sounds, SettingsRowId.Visuals };
            if (_lab != null) l.Add(SettingsRowId.Lab);
            if (_lab != null && _labMode && _labRun) l.Add(SettingsRowId.LabTune);
            if (_hud.PadConnected) l.Add(SettingsRowId.Rumble);
            l.Add(SettingsRowId.Keys); l.Add(SettingsRowId.Back);
            return l;
        }
    }

    void SyncSettingsHud()
    {
        _hud.LabModeOn = _labMode;
        _hud.SettingsRowIds = SettingsRowsNow.ToArray();
        if (_hud.SettingsOpen) _hud.SettingsSelection = Math.Clamp(_hud.SettingsSelection, 0, _hud.SettingsRowIds.Length - 1);
    }

    bool SettingsInput(bool alt)
    {
        if (!_hud.SettingsOpen) return false;
        int n = _hud.SettingsRowIds.Length;
        if (Input.IsActionJustPressed("pause")) CloseSettings();
        else if (Input.IsActionJustPressed("accelerate")) _hud.SettingsSelection = (_hud.SettingsSelection + n - 1) % n;
        else if (Input.IsActionJustPressed("brake")) _hud.SettingsSelection = (_hud.SettingsSelection + 1) % n;
        else if (Input.IsActionJustPressed("rumble")) ToggleRumble();
        else if (Input.IsActionJustPressed("keys")) ShowKeys();
        else if (Input.IsActionJustPressed("steer_left")) ChooseSettings(_hud.SettingsSelection, false);
        else if (Input.IsActionJustPressed("steer_right")) ChooseSettings(_hud.SettingsSelection, true);
        else if (Input.IsActionJustPressed("start") && !alt) ChooseSettings(_hud.SettingsSelection);
        return true;
    }

    void OpenSettings()
    {
        _hud.SetSettingsOpen(true);
        SyncSettingsHud();
    }

    void CloseSettings()
    {
        _hud.SetSettingsOpen(false);
        _overlayExitFrame = Engine.GetProcessFrames();
    }

    void ChooseSettings(int row, bool? toggleValue = null)
    {
        var rows = _hud.SettingsRowIds;
        if (row < 0 || row >= rows.Length) return;
        _hud.SettingsSelection = row;
        switch (rows[row])
        {
            case SettingsRowId.Sounds: OpenMixer(); break;
            case SettingsRowId.Visuals: OpenVisual(); break;
            case SettingsRowId.Lab: ApplyOnOff(_labMode, toggleValue, () => LabSetMode(!_labMode)); SyncSettingsHud(); break;
            case SettingsRowId.LabTune: LabOpen(false); break;
            case SettingsRowId.Rumble: ApplyOnOff(_rumble.Enabled, toggleValue, ToggleRumble); break;
            case SettingsRowId.Keys: ShowKeys(); break;      // (the help card; any key closes it and SETTINGS is there at its KEYS row, the race still paused)
            case SettingsRowId.Back: CloseSettings(); break;
        }
    }
}
