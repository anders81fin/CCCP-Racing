using System;
using System.Collections.Generic;
using Godot;

namespace CCCPRacing.UI;

/// <summary>
/// Title menu CHANGELOG: res://CHANGELOG.md over the title replay, the three newest versions one after
/// another, word-wrapped to the panel. CREDITS shows res://CREDITS.md in the same panel. Up/Down scroll
/// a long text; RaceScene sends every other key back.
/// </summary>
public partial class Hud
{
    public const string ChangelogPath = "res://CHANGELOG.md", CreditsPath = "res://CREDITS.md";
    /// <summary>How many versions the in-game changelog shows (the file keeps them all).</summary>
    const int ChangelogVersions = 3;
    bool _credits;
    List<(string text, bool heading)> _creditsText;

    // (text, heading) per source line from the first "## " on; blank lines separate versions.
    List<(string text, bool heading)> _log;
    int _logScroll, _logMaxScroll;

    /// <summary>Opens the log at the top (newest version).</summary>
    public void ResetChangelog(bool credits = false)
    {
        _logScroll = 0;
        _credits = credits;
    }

    /// <summary>Scrolls by this many rows, within the log.</summary>
    public void ScrollChangelog(int rows) => _logScroll = Math.Clamp(_logScroll + rows, 0, _logMaxScroll);

    static List<(string, bool)> LoadChangelog()
    {
        var lines = new List<(string, bool)>();
        string src = FileAccess.FileExists(ChangelogPath) ? FileAccess.GetFileAsString(ChangelogPath) : "";
        bool started = false;
        int versions = 0;
        foreach (string raw in src.Split('\n'))
        {
            string l = raw.TrimEnd();
            if (l.StartsWith("## "))
            {
                if (++versions > ChangelogVersions) break;
                started = true;
                lines.Add((l[3..], true));
            }
            else if (started) lines.Add((l, false));
        }
        if (lines.Count == 0) lines.Add(("THE CHANGELOG HAS BEEN CLASSIFIED.", true));
        return lines;
    }

    /// <summary>The credits from CREDITS.md (upper case: the pixel font's letters).</summary>
    static List<(string, bool)> LoadCredits()
    {
        var lines = new List<(string, bool)> { ("CCCP RACING", true) };      // the file's blank line after its title follows
        string src = FileAccess.FileExists(CreditsPath) ? FileAccess.GetFileAsString(CreditsPath) : "";
        // The file wraps its paragraphs by hand: join them again so the panel wraps them to its width.
        string para = null;
        void Flush() { if (para != null) lines.Add((para, false)); para = null; }
        foreach (string raw in src.Split('\n'))
        {
            string l = raw.Trim();
            if (l.StartsWith("# ")) continue;          // the file's own title: the panel has one
            if (l.Length == 0) { Flush(); lines.Add(("", false)); continue; }
            l = l.ToUpperInvariant().Replace(';', ',');      // the pixel font has no semicolon
            if (l.StartsWith("- ") || para == null) { Flush(); para = l; }
            else para += " " + l;
        }
        Flush();
        return lines;
    }

    /// <summary>Splits a line into rows of at most this many characters; list items indent their continuation rows.</summary>
    static IEnumerable<string> Wrap(string text, int cols)
    {
        string indent = text.StartsWith("- ") ? "  " : "";
        string row = "";
        foreach (string word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (row.Length > 0 && row.Length + 1 + word.Length > cols)
            {
                yield return row;
                row = indent + word;
            }
            else row = row.Length == 0 ? word : row + " " + word;
        }
        yield return row;
    }

    void DrawChangelog()
    {
        var r = LogPanel();
        float cx = Screen.X / 2;
        PixelFont.DrawCentered(this, _credits ? "CREDITS" : "CHANGELOG", cx, r.Position.Y + 7, Gold, 2, new Color(0.35f, 0, 0));
        DrawLogRows(r, r.Position.Y + 28, r.End.Y - 16);
        string hint = _logMaxScroll > 0 ? "UP/DOWN SCROLL  -  ANY OTHER " + P("KEY", "BUTTON") + ", TOVARISH" : AnyKey;
        if ((int)(_clock * 2) % 2 == 0)
            PixelFont.DrawCentered(this, hint, cx, r.End.Y - 12, Gold);
    }

    /// <summary>Dims the screen and draws the tall panel of the changelog, or one height tall in the middle
    /// (the test-version card's).</summary>
    Rect2 LogPanel(float height = -1)
    {
        var s = Screen;
        DrawRect(new Rect2(0, 0, s.X, s.Y), new Color(0, 0, 0, 0.35f));
        float w = Math.Min(s.X - 16, 440), h = height > 0 ? Math.Min(height, s.Y - 16) : s.Y - 16;
        var r = new Rect2(Mathf.Floor(s.X / 2 - w / 2), Mathf.Floor(s.Y / 2 - h / 2), Mathf.Floor(w), h);
        Panel(r, 0.88f);
        return r;
    }

    /// <summary>The log's rows from top to bottom in panel r, scrolled, with a scroll bar when it is longer.</summary>
    void DrawLogRows(Rect2 r, float top, float bottom)
    {
        _log ??= LoadChangelog();
        _creditsText ??= LoadCredits();
        var source = _credits ? _creditsText : _log;
        const int RowH = 10;
        float x0 = r.Position.X + 12;
        int cols = (int)((r.Size.X - 32) / PixelFont.Advance);
        var rows = new List<(string text, bool heading)>();
        foreach (var (text, heading) in source)
        {
            if (text.Length == 0) rows.Add(("", false));
            else foreach (string row in Wrap(text, cols)) rows.Add((row, heading));
        }
        int visible = Math.Max(1, (int)((bottom - top) / RowH));
        _logMaxScroll = Math.Max(0, rows.Count - visible);
        _logScroll = Math.Clamp(_logScroll, 0, _logMaxScroll);

        for (int i = 0; i < visible && _logScroll + i < rows.Count; i++)
        {
            var (text, heading) = rows[_logScroll + i];
            float y = top + i * RowH;
            if (heading)
            {
                DrawRect(new Rect2(r.Position.X + 4, y - 2, r.Size.X - 8, RowH), new Color(Red, 0.55f));
                PixelFont.Draw(this, _credits ? text : "V" + text, new Vector2(x0, y), Gold);
            }
            else PixelFont.Draw(this, text, new Vector2(x0, y), Paper);
        }

        // Scroll bar on the right when the log is longer than the panel.
        if (_logMaxScroll > 0)
        {
            float trackH = bottom - top, barH = Math.Max(8, trackH * visible / rows.Count);
            float barY = top + (trackH - barH) * _logScroll / _logMaxScroll;
            DrawRect(new Rect2(r.End.X - 8, top, 3, trackH), new Color(Grey, 0.25f));
            DrawRect(new Rect2(r.End.X - 8, barY, 3, barH), Gold);
        }
    }
}
