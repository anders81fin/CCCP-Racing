using System;
using System.Collections.Generic;
using System.Text;
using Godot;

namespace CCCPRacing.UI;

/// <summary>One line of the LAB panel: a slider (Fraction 0..1) or an action (Fraction below 0).</summary>
public struct LabRowView
{
    public string Label, Value;
    public float Fraction;
    public bool Changed;
}

/// <summary>
/// The LAB panel (RaceScene.Lab.cs fills it): sliders for the live parameters of an object, in groups, over the race (slowed down) or over the paused menu.
/// It takes the right side of the screen and leaves the road visible.
/// </summary>
public partial class Hud
{
    public bool LabOpen, LabRun;
    public string LabTitle = "", LabGroup = "", LabDoc = "", LabHint = "", LabStepName = "";
    public int LabGroupIndex, LabGroupCount, LabSelected;
    public LabRowView[] LabRows = Array.Empty<LabRowView>();

    internal static string LabClean(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s.ToUpperInvariant()) sb.Append(PixelFont.CanDraw(c) ? c : '?');
        return sb.ToString();
    }

    internal static List<string> LabWrap(string text, int chars, int maxLines)
    {
        var lines = new List<string>();
        var cur = new StringBuilder();
        foreach (var word in LabClean(text).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (cur.Length > 0 && cur.Length + 1 + word.Length > chars) { lines.Add(cur.ToString()); cur.Clear(); }
            if (cur.Length > 0) cur.Append(' ');
            cur.Append(word);
        }
        if (cur.Length > 0) lines.Add(cur.ToString());
        if (lines.Count > maxLines)
        {
            lines.RemoveRange(maxLines, lines.Count - maxLines);
            string last = lines[maxLines - 1];
            lines[maxLines - 1] = (last.Length > chars - 3 ? last[..(chars - 3)] : last) + "...";
        }
        return lines;
    }

    /// <summary>The small LAB mark of a run that writes no records.</summary>
    void DrawLabTag()
    {
        float w = PixelFont.Measure("LAB") + 8;
        var r = new Rect2(Mathf.Floor(Screen.X / 2 - w / 2), 2, w, 11);
        DrawRect(r, new Color(Red, 0.85f));
        PixelFont.DrawCentered(this, "LAB", Screen.X / 2, 4, Paper);
    }

    internal void DrawLab()
    {
        var s = Screen;
        float w = Mathf.Min(214, s.X * 0.48f), x = s.X - w - 4, top = 4, h = s.Y - 8;
        Panel(new Rect2(x, top, w, h), 0.82f);
        int chars = (int)((w - 12) / PixelFont.Advance);
        PixelFont.Draw(this, LabClean(LabTitle), new Vector2(x + 6, top + 5), Gold);
        string g = LabClean($"< {LabGroup} >  {LabGroupIndex + 1}/{LabGroupCount}");
        PixelFont.Draw(this, g, new Vector2(x + 6, top + 16), Paper);
        var hint = LabWrap(LabHint, chars, 3);
        var doc = LabWrap(LabDoc, chars, 4);
        float footer = (hint.Count + doc.Count) * PixelFont.LineHeight + 12;
        float rowsTop = top + 28, rowH = 10;
        int visible = Math.Max(3, (int)((h - 28 - footer) / rowH));
        int first = Math.Clamp(LabSelected - visible / 2, 0, Math.Max(0, LabRows.Length - visible));
        for (int i = first; i < LabRows.Length && i < first + visible; i++)
        {
            var row = LabRows[i];
            float y = rowsTop + (i - first) * rowH;
            bool sel = i == LabSelected;
            if (row.Fraction >= 0)
                DrawRect(new Rect2(x + 4, y - 1, Mathf.Floor((w - 8) * row.Fraction), rowH - 1), new Color(Red, sel ? 0.65f : 0.32f));
            else if (sel)
                DrawRect(new Rect2(x + 4, y - 1, w - 8, rowH - 1), new Color(Red, 0.55f));
            if (sel) DrawRect(new Rect2(x + 4, y - 1, w - 8, rowH - 1), new Color(Gold, 0.9f), false, 1f);
            Color c = row.Changed ? Gold : sel ? Paper : Grey;
            PixelFont.Draw(this, LabClean(row.Label), new Vector2(x + 7, y), c);
            if (row.Value != null)
            {
                string v = LabClean(row.Value);
                PixelFont.Draw(this, v, new Vector2(x + w - 7 - PixelFont.Measure(v), y), c);
            }
        }
        if (first > 0) PixelFont.Draw(this, "<", new Vector2(x + w - 12, top + 16), Grey);
        float fy = top + h - footer + 4;
        DrawRect(new Rect2(x + 4, fy - 3, w - 8, 1), new Color(Red, 0.7f));
        foreach (var line in doc) { PixelFont.Draw(this, line, new Vector2(x + 6, fy), Paper); fy += PixelFont.LineHeight; }
        foreach (var line in hint) { PixelFont.Draw(this, line, new Vector2(x + 6, fy), Grey); fy += PixelFont.LineHeight; }
    }
}
