using System;
using System.Collections;
using System.Linq;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

public partial class RaceScene
{
    // Called by the existing menus scenario, on its isolated run after its original checks.
    IEnumerable VwPartySwitches()
    {
        // The previous viewer closes behind an input-release barrier; wait for
        // that barrier before sending the first pause press.
        for (int i = 0; i < 10; i++) yield return null;
        IEnumerable Mouse(Rect2 cell, bool on)
        {
            bool pass = _vwDrv.PassForeignMotion;
            _vwDrv.PassForeignMotion = true;
            var pt = cell.Position + new Vector2(cell.Size.X * (on ? .75f : .25f), cell.Size.Y / 2);
            var physical = pt * VwPixelFrame().PixelScale;
            Input.WarpMouse(physical);
            for (int i = 0; i < 6; i++) yield return null;
            _vwDrv.SetControl(true);
            Input.ParseInputEvent(new InputEventMouseMotion { Position = physical, GlobalPosition = physical, Device = ViewerWindowedDriver.Sentinel });
            yield return null; yield return null;
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = physical, GlobalPosition = physical });
            yield return null; yield return null;
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = physical, GlobalPosition = physical });
            yield return null; yield return null;
            _vwDrv.SetControl(false); _vwDrv.PassForeignMotion = pass;
        }
        IEnumerable SettingsTo(SettingsRowId id)
        {
            for (int i = 0; i < 16 && _hud.SettingsRowIds[_hud.SettingsSelection] != id; i++)
                foreach (var x in VrPad(JoyButton.DpadDown)) yield return x;
            VwCheck(_hud.SettingsRowIds[_hud.SettingsSelection] == id, "switch test selected " + id);
        }
        foreach (var x in VrPad(JoyButton.Start)) yield return x;
        for (int i = 0; i < 16 && _hud.PauseSelection != PauseRowIndex(PauseRowId.Settings); i++)
            foreach (var x in VrPad(JoyButton.DpadDown)) yield return x;
        foreach (var x in VrPad(JoyButton.A)) yield return x;
        VwCheck(_state == State.Paused && _hud.SettingsOpen, "switch test enters SETTINGS through PARTY MEETING");
        float clock = _lapClock;

        foreach (var x in SettingsTo(SettingsRowId.Rumble)) yield return x;
        bool rumble = _rumble.Enabled;
        foreach (bool on in new[] { false, false, true, true })
        {
            foreach (var x in VrPad(on ? JoyButton.DpadRight : JoyButton.DpadLeft)) yield return x;
            VwCheck(_rumble.Enabled == on && _state == State.Paused, "RUMBLE arrow sets " + on + " without toggling an already matching state");
        }
        foreach (var x in VrShot("party-switches-settings")) yield return x;
        foreach (var x in VrPad(rumble ? JoyButton.DpadRight : JoyButton.DpadLeft)) yield return x;
        foreach (var x in SettingsTo(SettingsRowId.Visuals)) yield return x;
        foreach (var x in VrPad(JoyButton.A)) yield return x;
        var ids = _hud.VisualRowIds.Where(id => _hud.VisualToggleValue(id) != null).ToArray();
        VwCheck(ids.Length == 6, "Rally VISUALS exposes all six boolean switches");
        foreach (var id in ids)
        {
            foreach (var x in VwVisualTo(id)) yield return x;
            bool original = _hud.VisualToggleValue(id).Value;
            foreach (bool on in new[] { false, false, true, true })
            {
                foreach (var x in VrTap(on ? Key.Right : Key.Left)) yield return x;
                VwCheck(_hud.VisualToggleValue(id) == on, id + " arrow explicitly sets " + on);
            }
            int row = Array.IndexOf(_hud.VisualRowIds, id);
            var cell = Hud.OnOffRowRect(_hud.VisualRect, _hud.VisualRect.Position.Y + 38 + row * 16);
            foreach (var x in Mouse(cell, false)) yield return x;
            VwCheck(_hud.VisualToggleValue(id) == false, id + " actual mouse OFF cell sets OFF");
            foreach (var x in Mouse(cell, true)) yield return x;
            VwCheck(_hud.VisualToggleValue(id) == true, id + " actual mouse ON cell sets ON");
            foreach (var x in VrTap(original ? Key.Right : Key.Left)) yield return x;
        }
        foreach (var x in VrShot("party-switches-visuals")) yield return x;
        VwCheck(_hud.VisualToggleValue(VisualRowId.Rendering) == null && _hud.VisualToggleValue(VisualRowId.Edges) == null, "rendering modes and MSAA remain multi-value selectors");
        foreach (var x in VrTap(Key.Escape)) yield return x;
        // Keyboard now, so RUMBLE may be hidden again; SOUNDS remains the first row.
        for (int i = 0; i < 16 && _hud.SettingsRowIds[_hud.SettingsSelection] != SettingsRowId.Sounds; i++) foreach (var x in VrTap(Key.Up)) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        bool radio = _carRadio.On;
        foreach (bool on in new[] { false, false, true, true })
        {
            foreach (var x in VrTap(on ? Key.Right : Key.Left)) yield return x;
            VwCheck(_carRadio.On == on && _hud.MixerLeadRows == (on ? 2 : 1), "RADIO sets " + on + " with the correct station-row visibility");
        }
        var radioCell = Hud.OnOffRowRect(_hud.MixerRect, _hud.MixerRect.Position.Y + 38);
        foreach (var x in Mouse(radioCell, false)) yield return x;
        VwCheck(!_carRadio.On, "RADIO mouse OFF works");
        radioCell = Hud.OnOffRowRect(_hud.MixerRect, _hud.MixerRect.Position.Y + 38); // the centered panel moves when STATION disappears
        foreach (var x in Mouse(radioCell, true)) yield return x;
        VwCheck(_carRadio.On, "RADIO mouse ON works");
        foreach (var x in VrShot("party-switches-sounds")) yield return x;
        foreach (var x in VrTap(radio ? Key.Right : Key.Left)) yield return x;
        foreach (var x in VrTap(Key.Escape)) yield return x;

        // LAB is last: enabling it intentionally makes this disposable run unranked.
        foreach (var x in SettingsTo(SettingsRowId.Lab)) yield return x;
        bool lab = _labMode;
        foreach (bool on in new[] { false, false, true, true })
        {
            foreach (var x in VrPad(on ? JoyButton.DpadRight : JoyButton.DpadLeft)) yield return x;
            VwCheck(_labMode == on, "LAB MODE sets " + on + " without a double toggle");
        }
        foreach (var x in VrShot("party-switches-lab-on")) yield return x;
        foreach (var x in VrPad(lab ? JoyButton.DpadRight : JoyButton.DpadLeft)) yield return x;
        var cfg = new ConfigFile(); cfg.Load(_records.Dir + "settings.cfg");
        VwCheck(cfg.GetValue("lab", "enabled", !lab).AsBool() == lab, "LAB MODE preference is saved and restored");
        VwCheck(_state == State.Paused && _lapClock == clock, "all switches leave the drive paused and its clock untouched");
        foreach (var x in VrPad(JoyButton.B)) yield return x;
        foreach (var x in VrPad(JoyButton.B)) yield return x;
        VwCheck(_state == State.Racing, "normal back chain resumes from PARTY MEETING");
    }
}
