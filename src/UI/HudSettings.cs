using Godot;

namespace CCCPRacing.UI;

public partial class Hud
{
    /// <summary>SETTINGS of the Party Meeting: SOUNDS and VISUALS (submenus), RUMBLE where there is a pad, back.</summary>
    public bool SettingsOpen;
    public bool LabModeOn;
    public int SettingsSelection;
    public SettingsRowId[] SettingsRowIds = { SettingsRowId.Sounds, SettingsRowId.Visuals, SettingsRowId.Keys, SettingsRowId.Back };

    public void SetSettingsOpen(bool open)
    {
        SettingsOpen = open;
        _menuTextClock = 0;
        if (open) SettingsSelection = 0;
    }

    internal Rect2 SettingsRect
    {
        get
        {
            float width = Mathf.Min(280, Screen.X - 12), height = 38 + SettingsRowIds.Length * 16 + 20;
            return new Rect2(Mathf.Floor((Screen.X - width) / 2), Mathf.Floor((Screen.Y - height) / 2), width, height);
        }
    }

    public int SettingsRowAt(Vector2 pos)
    {
        var r = SettingsRect;
        if (pos.X < r.Position.X + 4 || pos.X >= r.End.X - 4) return -1;
        int row = (int)Mathf.Floor((pos.Y - r.Position.Y - 36) / 16);
        return row >= 0 && row < SettingsRowIds.Length ? row : -1;
    }

    /// <summary>A row's left label and its right-aligned value (null: none).</summary>
    internal void SettingsRowTexts(SettingsRowId id, out string label, out string value)
    {
        value = null;
        switch (id)
        {
            case SettingsRowId.Sounds: label = "SOUNDS"; value = ">"; break;
            case SettingsRowId.Visuals: label = "VISUALS"; value = ">"; break;
            case SettingsRowId.Lab: label = "LAB MODE"; value = LabModeOn ? "ON" : "OFF"; break;
            case SettingsRowId.LabTune: label = "LAB TUNING"; value = ">"; break;
            case SettingsRowId.Rumble: label = "RUMBLE"; value = Rumble ? "ON" : "OFF"; break;
            case SettingsRowId.Keys: label = "KEYS"; break;
            default: label = "BACK"; break;
        }
    }

    internal bool? SettingsToggleValue(SettingsRowId id) => id switch
    {
        SettingsRowId.Lab => LabModeOn,
        SettingsRowId.Rumble => Rumble,
        _ => null,
    };

    internal bool? SettingsSwitchValueAt(int row, Vector2 p) => row >= 0 && row < SettingsRowIds.Length && SettingsToggleValue(SettingsRowIds[row]) != null
        ? OnOffValueAt(OnOffRowRect(SettingsRect, SettingsRect.Position.Y + 38 + row * 16), p) : null;

    /// <summary>A row's shortcut (keyboard or pad button), or null: drawn in its own column, left of the label column that every row's label starts in.</summary>
    internal string SettingsRowKey(SettingsRowId id) => id switch
    {
        SettingsRowId.Rumble => P("V", "RB"),
        SettingsRowId.Keys => P("K", "Y"),
        SettingsRowId.Back => P("ESC", "B"),
        _ => null,
    };

    /// <summary>The x where every row's label starts: after the shortcut column (the widest shortcut, ESC, and a gap), so KEYS, RUMBLE, BACK, SOUNDS and VISUALS all start together.</summary>
    internal float SettingsLabelX(Rect2 r) => r.Position.X + (r.Size.X < 240 ? 10 : 18) + PixelFont.Measure("ESC  ");

    internal void DrawSettings()
    {
        var r = SettingsRect;
        Panel(r, 1f);
        DrawHeading("SETTINGS", Screen.X / 2, r.Position.Y + 8, _menuTextClock,
            PixelFont.Measure("SETTINGS", 2) <= r.Size.X - 12 ? 2 : 1);
        float x = r.Position.X + (r.Size.X < 240 ? 10 : 18);
        for (int i = 0; i < SettingsRowIds.Length; i++)
        {
            float y = r.Position.Y + 38 + i * 16;
            bool selected = i == SettingsSelection;
            if (selected) DrawRect(new Rect2(r.Position.X + 4, y - 2, r.Size.X - 8, 12), new Color(Red, 0.55f));
            Color c = selected ? Gold : Paper;
            SettingsRowTexts(SettingsRowIds[i], out string label, out string value);
            string key = SettingsRowKey(SettingsRowIds[i]);
            if (key != null) PixelFont.Draw(this, key, new Vector2(x, y), selected ? Gold : Grey);
            PixelFont.Draw(this, label, new Vector2(SettingsLabelX(r), y), c);
            if (SettingsToggleValue(SettingsRowIds[i]) is bool on) DrawOnOffSwitch(OnOffRowRect(r, y).Position, on, selected);
            else if (value != null) PixelFont.Draw(this, value, new Vector2(r.End.X - 12 - PixelFont.Measure(value), y), c);
        }
        string hint = P("UP/DOWN SELECT  < > CHANGE  ENTER OK", "D-PAD SELECT/CHANGE  A SELECT");
        if (PixelFont.Measure(hint) > r.Size.X - 12) hint = P("UP/DOWN SELECT  < > CHANGE", "D-PAD SELECT / CHANGE");
        PixelFont.DrawCentered(this, hint, Screen.X / 2, r.End.Y - 12, Grey);
    }
}
