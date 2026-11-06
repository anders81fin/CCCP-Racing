using System;
using Godot;

namespace CCCPRacing.UI;

/// <summary>RECORD LAN RACES? (RaceScene.LanRecord.cs): YES / NO before this machine first hosts or joins, and from the LAN menu.</summary>
public partial class Hud
{
    Rect2 LanRecordRect()
    {
        float w = Math.Min(300, Screen.X - 8), h = 122;
        return new(Mathf.Floor((Screen.X - w) / 2), Mathf.Floor((Screen.Y - h) / 2), w, h);
    }

    /// <summary>The row under the mouse on the question: 0 YES, 1 NO, -1 none.</summary>
    public int LanRecordRowAt(Vector2 p)
    {
        if (Mode != HudMode.Lan || Lan.Page != LanPage.Record) return -1;
        var r = LanRecordRect();
        if (!new Rect2(r.Position.X + 10, r.Position.Y + 67, r.Size.X - 20, 28).HasPoint(p)) return -1;
        return p.Y < r.Position.Y + 81 ? 0 : 1;
    }

    void DrawLanRecord()
    {
        var r = LanRecordRect();
        Panel(r, 0.98f);
        float cx = r.GetCenter().X, y = r.Position.Y, inner = r.Size.X - 8;
        const string head = "RECORD LAN RACES?";
        PixelFont.DrawCentered(this, head, cx, y + 7, Gold, PixelFont.Measure(head, 2) <= inner ? 2 : 1, new Color(0.35f, 0, 0));
        PixelFont.DrawCentered(this, ReplayFit("RECORD EVERY LAN RACE ON THIS", inner), cx, y + 27, Paper);
        PixelFont.DrawCentered(this, ReplayFit("MACHINE, TO KEEP AFTERWARDS?", inner), cx, y + 36, Paper);
        PixelFont.DrawCentered(this, ReplayFit("YOUR CHOICE ONLY: NOT SHARED", inner), cx, y + 48, Grey);
        PixelFont.DrawCentered(this, ReplayFit("CHANGE IT LATER IN THE LAN MENU", inner), cx, y + 57, Grey);
        MenuItem("YES, RECORD", Lan.Sel == 0, cx, y + 70);
        MenuItem("NO, DO NOT RECORD", Lan.Sel != 0, cx, y + 84);
        PixelFont.DrawCentered(this, P("UP/DOWN  ENTER CHOOSE", "D-PAD  A CHOOSE"), cx, y + 100, Grey);
        PixelFont.DrawCentered(this, P("ESC BACK", "B BACK"), cx, y + 110, Grey);
    }
}
