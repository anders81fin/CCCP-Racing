using Godot;

namespace CCCPRacing.UI;

public partial class Hud
{
    /// <summary>The SOUNDS mixer of the pause menu: open, the selected row, the three levels (0..10).</summary>
    public bool MixerOpen;
    public int MixerSelection;
    public int[] MixerLevels = { 5, 5, 5, 5, 8 };

    /// <summary>RADIO on/off, STATION (only while the radio is on), the five levels, then RESET and BACK.</summary>
    public int MixerLeadRows => 1 + (RadioOn ? 1 : 0);
    public int MixerRowCount => MixerLeadRows + 5 + 2;

    public void SetMixerOpen(bool open)
    {
        MixerOpen = open;
        _menuTextClock = 0;
        if (open) MixerSelection = 0;
    }

    internal Rect2 MixerRect
    {
        get
        {
            float width = Mathf.Min(280, Screen.X - 12), height = 38 + MixerRowCount * 16 + 20;
            return new Rect2(Mathf.Floor((Screen.X - width) / 2), Mathf.Floor((Screen.Y - height) / 2), width, height);
        }
    }

    public int MixerRowAt(Vector2 pos)
    {
        var r = MixerRect;
        if (pos.X < r.Position.X + 4 || pos.X >= r.End.X - 4) return -1;
        int row = (int)Mathf.Floor((pos.Y - r.Position.Y - 36) / 16);
        return row >= 0 && row < MixerRowCount ? row : -1;
    }

    void DrawMixer()
    {
        var r = MixerRect;
        Panel(r, 1f);
        DrawHeading("SOUNDS", Screen.X / 2, r.Position.Y + 8, _menuTextClock,
            PixelFont.Measure("SOUNDS", 2) <= r.Size.X - 12 ? 2 : 1);
        string[] names = { "ENGINE AND GEARBOX", "TYRES AND ROAD", "SURROUNDINGS", "ALERTS", "RADIO VOLUME" };
        float x = r.Position.X + (r.Size.X < 240 ? 10 : 18);
        int lead = MixerLeadRows, count = MixerRowCount;
        for (int i = 0; i < count; i++)
        {
            float y = r.Position.Y + 38 + i * 16;
            bool selected = i == MixerSelection;
            if (selected) DrawRect(new Rect2(r.Position.X + 4, y - 2, r.Size.X - 8, 12), new Color(Red, 0.55f));
            Color c = selected ? Gold : Paper;
            int k = i - lead;
            if (i < lead)
            {
                string value = i == 0 ? (RadioOn ? "ON" : "OFF") : RadioStation + " / " + RadioStationCount;
                PixelFont.Draw(this, i == 0 ? "RADIO" : "STATION", new Vector2(x, y), c);
                PixelFont.Draw(this, value, new Vector2(r.End.X - 12 - PixelFont.Measure(value), y), c);
            }
            else if (k < 5)
            {
                int level = MixerLevels[k];
                PixelFont.Draw(this, names[k], new Vector2(x, y), c);
                string bar = level == 0 ? "MUTE" : new string('#', level) + new string('-', 10 - level);
                PixelFont.Draw(this, bar, new Vector2(r.End.X - 12 - PixelFont.Measure(bar), y), c);
            }
            else PixelFont.Draw(this, k == 5 ? "RESET TO DEFAULT" : P("ESC  BACK", "B    BACK"), new Vector2(x, y), c);
        }
        string hint = P("UP/DOWN CHOOSE  LEFT/RIGHT CHANGE", "D-PAD CHOOSE / CHANGE  A SELECT");
        if (PixelFont.Measure(hint) > r.Size.X - 12) hint = P("UP/DOWN SELECT  < > CHANGE", "D-PAD SELECT / CHANGE");
        PixelFont.DrawCentered(this, hint, Screen.X / 2, r.End.Y - 12, Grey);
    }
}
