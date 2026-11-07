using System;
using Godot;

namespace CCCPRacing.UI;

/// <summary>DRIVING SCHOOL's lessons as a tree (RaceScene.SchoolMenu.cs): courses folded (+) or open (-), chapters under them.</summary>
public partial class Hud
{
    /// <summary>A line of the tree: its text (a course with + or -), and whether it is a course.</summary>
    public readonly record struct LessonLine(string Text, bool Header);

    public LessonLine[] Lessons = Array.Empty<LessonLine>();
    public int LessonSelection;

    void DrawLessons()
    {
        var s = Screen;
        float cx = s.X / 2;
        DrawRect(new Rect2(0, 0, s.X, s.Y), new Color(0, 0, 0, 0.35f));
        const int RowH = 12;
        int rows = Math.Min(Lessons.Length, Math.Max(4, (int)((s.Y - 110) / RowH)));
        int top = Math.Clamp(LessonSelection - rows / 2, 0, Math.Max(0, Lessons.Length - rows));
        float w = 340, h = 40 + rows * RowH + 26;
        var r = new Rect2(Mathf.Floor(cx - w / 2), Mathf.Floor((s.Y - h) / 2), w, h);
        Panel(r, 0.88f);
        DrawHeading("DRIVING SCHOOL", cx, r.Position.Y + 7, _menuTextClock);
        float x0 = r.Position.X + 20, y = r.Position.Y + 40;
        for (int i = top; i < top + rows; i++, y += RowH)
        {
            var l = Lessons[i];
            bool sel = i == LessonSelection;
            if (sel) DrawRect(new Rect2(r.Position.X + 4, y - 3, w - 8, RowH - 1), new Color(Red, 0.55f));
            PixelFont.Draw(this, l.Text, new Vector2(x0 + (l.Header ? 0 : 18), y), sel ? Gold : l.Header ? Paper : Grey);
        }
        if (top > 0) PixelFont.DrawCentered(this, "^", r.End.X - 12, r.Position.Y + 40, Grey);
        if (top + rows < Lessons.Length) PixelFont.DrawCentered(this, "v", r.End.X - 12, y - RowH, Grey);
        PixelFont.DrawCentered(this, P("UP/DOWN CHOOSE  < > CLOSE/OPEN  ENTER WATCH  ESC BACK", "D-PAD CHOOSE  < > CLOSE/OPEN  A WATCH  B BACK"),
            cx, r.End.Y - 13, Grey);
    }
}
