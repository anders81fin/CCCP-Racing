using Godot;

namespace CCCPRacing.UI;

public partial class Hud
{
    internal const float OnOffSwitchWidth = 50, OnOffSwitchHeight = 10;

    internal static Rect2 OnOffRowRect(Rect2 panel, float y) =>
        new(panel.End.X - 12 - OnOffSwitchWidth, y - 1, OnOffSwitchWidth, OnOffSwitchHeight);

    internal static bool? OnOffValueAt(Rect2 rect, Vector2 p) => rect.HasPoint(p) ? p.X >= rect.GetCenter().X : null;

    Rect2 DrawOnOffSwitch(Vector2 at, bool on, bool focused)
    {
        var r = new Rect2(at, new Vector2(OnOffSwitchWidth, OnOffSwitchHeight));
        DrawRect(r, new Color(0, 0, 0, .6f));
        var active = new Rect2(r.Position + new Vector2(on ? r.Size.X / 2 : 0, 0), new Vector2(r.Size.X / 2, r.Size.Y));
        DrawRect(active, new Color(on ? Green : Grey, .35f));
        DrawRect(r, focused ? Gold : Grey, false, 1);
        DrawLine(new Vector2(r.GetCenter().X, r.Position.Y), new Vector2(r.GetCenter().X, r.End.Y), Grey);
        PixelFont.DrawCentered(this, "OFF", r.Position.X + r.Size.X / 4, r.Position.Y + 1, on ? Grey : Paper);
        PixelFont.DrawCentered(this, "ON", r.Position.X + r.Size.X * .75f, r.Position.Y + 1, on ? Green : Grey);
        return r;
    }
}
