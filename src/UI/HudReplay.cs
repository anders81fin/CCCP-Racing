using Godot;

namespace CCCPRacing.UI;

public partial class Hud
{
    // The REPLAY list of the Party Meeting (docs/work-orders/player-replay.md phase 3). Draw only: RaceScene fills ReplayList.
    public bool ReplayListOpen;
    public ReplayListView ReplayList = new();

    /// <summary>At most this many laps are visible at once; the window follows the selection.</summary>
    public const int ReplayListWindow = 6;
    const int ReplayRowHeight = 22;

    public void SetReplayListOpen(bool open)
    {
        ReplayListOpen = open;
        _menuTextClock = 0;
    }

    internal Rect2 ReplayListRect => ReplayListRectFor(Screen);

    internal static Rect2 ReplayListRectFor(Vector2 screen)
    {
        float width = Mathf.Min(320, screen.X - 12), height = 30 + ReplayListWindow * ReplayRowHeight + 14 + 18;
        return new Rect2(Mathf.Floor((screen.X - width) / 2), Mathf.Floor((screen.Y - height) / 2), width, height);
    }

    /// <summary>The first visible row: the window follows the selection, stateless.</summary>
    internal static int ReplayFirstRow(int count, int selected) =>
        Mathf.Clamp(selected - 3, 0, Mathf.Max(0, count - ReplayListWindow));

    internal static float ReplayRowY(Rect2 r, int slot) => r.Position.Y + 30 + slot * ReplayRowHeight;

    /// <summary>Mouse hit test: the index in ReplayList.Rows of the row under pos, or -1.</summary>
    public int ReplayRowAt(Vector2 pos)
    {
        var r = ReplayListRect;
        int count = ReplayList.Rows.Length;
        if (pos.X < r.Position.X + 4 || pos.X >= r.End.X - 8) return -1;
        float rel = pos.Y - (r.Position.Y + 30 - 2);
        if (rel < 0) return -1;
        int slot = (int)Mathf.Floor(rel / ReplayRowHeight);
        int row = ReplayFirstRow(count, ReplayList.Selected) + slot;
        return slot >= 0 && slot < ReplayListWindow && row < count ? row : -1;
    }

    /// <summary>Cuts a text to maxWidth pixels, ending in "." only when it had to be cut.</summary>
    internal static string ReplayFit(string text, float maxWidth)
    {
        if (PixelFont.Measure(text) <= maxWidth) return text;
        while (text.Length > 0 && PixelFont.Measure(text + ".") > maxWidth) text = text[..^1];
        return text.Length == 0 ? "" : text + ".";
    }

    /// <summary>The three texts of a row, fitted to a panel of the given width (the flag keeps its place, the title gives way).</summary>
    internal static void ReplayRowTexts(ReplayRowView row, float panelWidth, out string title, out string detail, out string flag)
    {
        float inner = panelWidth - 28;                    // 10 px left, 18 px right (room for the scroll bar)
        flag = ReplayFit(row.Flag ?? "", inner * 0.5f);
        float room = inner - (flag.Length > 0 ? PixelFont.Measure(flag) + 8 : 0);
        title = ReplayFit(row.Title ?? "", room);
        detail = ReplayFit(row.Detail ?? "", inner);
    }

    internal string ReplayFooter(float width)
    {
        bool keep = ReplayList.AllowKeep;
        string hint = keep ? P("UP/DOWN  ENTER WATCH  K KEEP  DEL DELETE  ESC", "D-PAD  A WATCH  Y KEEP  X DELETE  B BACK") : P("UP/DOWN  ENTER WATCH  DEL DELETE  ESC", "D-PAD  A WATCH  X DELETE  B BACK");
        if (PixelFont.Measure(hint) > width) hint = keep ? P("UP/DOWN  ENTER  K KEEP  DEL DELETE  ESC", "D-PAD  A  Y KEEP  X DELETE  B") : P("UP/DOWN  ENTER  DEL DELETE  ESC", "D-PAD  A  X DELETE  B");
        return hint;
    }

    public bool ReplayDeleteOpen, ReplayDeleteConfirm;
    public string ReplayDeleteTitle = "", ReplayDeleteDetail = "";
    internal Rect2 ReplayDeleteRect => new(Mathf.Floor((Screen.X - Mathf.Min(320, Screen.X - 8)) / 2), Mathf.Floor(Screen.Y / 2 - 56), Mathf.Min(320, Screen.X - 8), 112);

    public int ReplayDeleteRowAt(Vector2 p)
    {
        var r = ReplayDeleteRect;
        if (p.X < r.Position.X + 6 || p.X >= r.End.X - 6) return -1;
        if (p.Y >= r.Position.Y + 66 && p.Y < r.Position.Y + 79) return 0;
        if (p.Y >= r.Position.Y + 80 && p.Y < r.Position.Y + 93) return 1;
        return -1;
    }

    void DrawReplayDelete()
    {
        var r = ReplayDeleteRect; float cx = r.Position.X + r.Size.X / 2;
        Panel(r, 0.98f);
        DrawHeading("DELETE RECORDING?", cx, r.Position.Y + 7, _menuTextClock);
        PixelFont.DrawCentered(this, ReplayFit(ReplayDeleteTitle, r.Size.X - 12), cx, r.Position.Y + 27, Gold);
        PixelFont.DrawCentered(this, ReplayFit(ReplayDeleteDetail, r.Size.X - 12), cx, r.Position.Y + 39, Grey);
        PixelFont.DrawCentered(this, ReplayFit("RECORDING AND EDITS WILL BE REMOVED", r.Size.X - 12), cx, r.Position.Y + 52, Paper);
        foreach (int row in new[] { 0, 1 })
        {
            bool selected = ReplayDeleteConfirm == (row == 1);
            float y = r.Position.Y + 68 + row * 14;
            if (selected) DrawRect(new Rect2(r.Position.X + 6, y - 2, r.Size.X - 12, 12), new Color(Red, .55f));
            PixelFont.DrawCentered(this, (selected ? "> " : "") + (row == 0 ? "CANCEL" : "DELETE"), cx, y, selected ? Gold : Grey);
        }
        PixelFont.DrawCentered(this, P("UP/DOWN  ENTER CHOOSE  ESC CANCEL", "D-PAD  A CHOOSE  B CANCEL"), cx, r.End.Y - 12, Grey);
    }

    // KEEP's name field: over the list while the player types a name for the lap to keep.
    public bool KeepNameOpen;
    public string KeepName = "", KeepNameSub = "";
    public const int KeepNameMax = 24;

    void DrawKeepName()
    {
        var s = Screen;
        float cx = s.X / 2;
        float fw = KeepNameMax * 12 + 16, w = Mathf.Max(Mathf.Ceil(fw) + 20, Mathf.Min(s.X - 8, 270));
        w = Mathf.Min(w, s.X - 4);
        var r = new Rect2(Mathf.Floor(cx - w / 2), Mathf.Floor(s.Y * 0.5f - 44), w, 88);
        Panel(r, 0.96f);
        DrawHeading("NAME THE RECORDING", cx, r.Position.Y + 7, _menuTextClock, PixelFont.Measure("NAME THE RECORDING", 2) <= r.Size.X - 8 ? 2 : 1);
        PixelFont.DrawCentered(this, ReplayFit(KeepNameSub, r.Size.X - 8), cx, r.Position.Y + 26, Grey);
        float ffw = Mathf.Min(fw, r.Size.X - 8);
        var f = new Rect2(Mathf.Floor(cx - ffw / 2), r.Position.Y + 40, ffw, 22);
        DrawRect(f, new Color(0, 0, 0, 0.6f));
        DrawRect(f, new Color(Gold, 0.8f), false, 1f);
        // the field shows the last letters when the name is longer than the box
        string shown = KeepName; int fit = Mathf.Max(1, (int)((ffw - 16) / 12));
        if (shown.Length > fit) shown = shown[^fit..];
        DrawTextField(shown, f, fit);
        string hint = P("ENTER KEEP  ESC CANCEL  EMPTY = DEFAULT NAME", "START KEEP  B DELETE/CANCEL  UP/DOWN LETTER");
        if (PixelFont.Measure(hint) > r.Size.X - 6) hint = P("ENTER KEEP  ESC CANCEL", "START KEEP  B CANCEL");
        PixelFont.DrawCentered(this, hint, cx, r.End.Y - 12, Grey);
    }

    internal void DrawReplayList()
    {
        var r = ReplayListRect;
        var v = ReplayList;
        int count = v.Rows.Length;
        float cx = r.Position.X + r.Size.X / 2;
        Panel(r, 1f);
        DrawHeading(string.IsNullOrEmpty(v.Heading) ? "REPLAY" : v.Heading, Screen.X / 2, r.Position.Y + 8, _menuTextClock);
        float noticeY = r.End.Y - 30;
        if (count == 0)
        {
            bool hasNotice = !string.IsNullOrEmpty(v.Notice);
            string main = hasNotice ? v.Notice : "NO LAPS RECORDED YET";
            float cy = r.Position.Y + 30 + ReplayListWindow * ReplayRowHeight / 2 - 10;
            PixelFont.DrawCentered(this, ReplayFit(main, r.Size.X - 16), cx, cy, hasNotice && v.NoticeIsError ? Red : Paper);
            if (!hasNotice) PixelFont.DrawCentered(this, "DRIVE A LAP FIRST", cx, cy + 12, Grey);
        }
        else
        {
            int first = ReplayFirstRow(count, v.Selected);
            for (int slot = 0; slot < ReplayListWindow && first + slot < count; slot++)
            {
                int i = first + slot;
                var row = v.Rows[i];
                float y = ReplayRowY(r, slot);
                bool selected = i == v.Selected;
                ReplayRowTexts(row, r.Size.X, out string title, out string detail, out string flag);
                if (selected) DrawRect(new Rect2(r.Position.X + 4, y - 2, r.Size.X - 12, ReplayRowHeight - 2), new Color(Red, 0.55f));
                Color tc = !row.Selectable ? new Color(Grey, 0.7f) : selected ? Gold : Paper;
                float x = r.Position.X + 10;
                PixelFont.Draw(this, title, new Vector2(x, y), tc);
                PixelFont.Draw(this, detail, new Vector2(x, y + 10), row.Selectable ? Grey : new Color(Grey, 0.55f));
                if (flag.Length > 0) PixelFont.Draw(this, flag, new Vector2(r.End.X - 18 - PixelFont.Measure(flag), y), row.Selectable ? Red : new Color(Red, 0.7f));
            }
            // Scroll bar and the n/N counter.
            float top = r.Position.Y + 28, bar = ReplayListWindow * ReplayRowHeight;
            DrawRect(new Rect2(r.End.X - 8, top, 2, bar), new Color(Grey, 0.25f));
            if (count > ReplayListWindow)
            {
                float th = Mathf.Max(6, Mathf.Floor(bar * ReplayListWindow / count));
                float ty = top + Mathf.Floor((bar - th) * first / (count - ReplayListWindow));
                DrawRect(new Rect2(r.End.X - 8, ty, 2, th), Gold);
            }
            string n = $"{v.Selected + 1}/{count}";
            PixelFont.Draw(this, n, new Vector2(r.End.X - 8 - PixelFont.Measure(n), r.Position.Y + 8), Grey);
        }
        if (!string.IsNullOrEmpty(v.Notice) && count > 0)
            PixelFont.DrawCentered(this, ReplayFit(v.Notice, r.Size.X - 12), cx, noticeY, v.NoticeIsError ? Red : Grey);
        PixelFont.DrawCentered(this, ReplayFooter(r.Size.X - 8), cx, r.End.Y - 12, Grey);
    }
}
