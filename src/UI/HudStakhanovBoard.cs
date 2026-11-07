using System;
using System.Collections.Generic;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.UI;

public partial class Hud
{
    public bool StakhanovMiniBoard, StakhanovBoardOpen;
    public string StakhanovBoardTrack = "", StakhanovBoardFuel = "";
    public IReadOnlyList<Leaderboard.Entry> StakhanovBoardRows = Array.Empty<Leaderboard.Entry>();

    void DrawStakhanovBoard(bool mini)
    {
        var s = Screen;
        float w = mini ? 156 : 326, h = mini ? 53 : 183;
        var r = new Rect2(mini ? s.X - w - 4 : Mathf.Floor((s.X - w) / 2), mini ? 4 : Mathf.Floor((s.Y - h) / 2), w, h);
        Panel(r, mini ? .85f : .96f);
        PixelFont.DrawCentered(this, mini ? StakhanovBoardFuel + " - TOP 10" : "LEADERBOARDS", r.GetCenter().X, r.Position.Y + (mini ? 4 : 6), Gold);
        if (!mini) PixelFont.DrawCentered(this, StakhanovBoardTrack + " - " + StakhanovBoardFuel, r.GetCenter().X, r.Position.Y + 22, Paper);
        int count = mini ? 4 : Leaderboard.StakhanovSize;
        float y = r.Position.Y + (mini ? 14 : 40);
        for (int row = 0; row < count; row++, y += mini ? 9 : 11)
        {
            int i = mini && row == 3 ? 9 : row;
            var e = i < StakhanovBoardRows.Count ? StakhanovBoardRows[i] : null;
            var c = i == 0 ? Paper : Grey;
            PixelFont.Draw(this, $"{i + 1,2}.", new Vector2(r.Position.X + (mini ? 5 : 6), y), c);
            string name = e?.Name ?? "-";
            // The mini panel leaves room for the full twelve-letter driver name.
            PixelFont.Draw(this, name, new Vector2(r.Position.X + (mini ? 24 : 28), y), c);
            PixelFont.Draw(this, e == null ? "--:--.--" : LapTimer.Format(e.Time), new Vector2(r.End.X - 54, y), c);
        }
        if (!mini)
        {
            PixelFont.DrawCentered(this, P("< > FUEL   ESC BACK", "D-PAD LEFT/RIGHT FUEL   B BACK"), r.GetCenter().X, r.End.Y - 19, Paper);
            PixelFont.DrawCentered(this, "CURRENT TRACK", r.GetCenter().X, r.End.Y - 9, Grey);
        }
    }
}
