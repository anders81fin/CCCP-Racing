using System;
using Godot;

namespace CCCPRacing.UI;

/// <summary>SHARE BOARDS (RaceScene.LanBoards.cs): the consent question, and the lobby's share line, progress and notices.</summary>
public partial class Hud
{
    public class LanBoardView
    {
        public bool On, Busy, Ask, NoteBad, Waiting;
        public string Progress, Note;
    }
    public readonly LanBoardView LanBoards = new();

    /// <summary>Height the board lines add to the lobby card.</summary>
    public const float LanBoardsLobbyHeight = 26;

    Rect2 LanBoardsAskRect()
    {
        float w = Math.Min(300, Screen.X - 8), h = 140;
        return new(Mathf.Floor((Screen.X - w) / 2), Mathf.Floor((Screen.Y - h) / 2), w, h);
    }

    /// <summary>The row under the mouse on the question: 0 YES, 1 NO, -1 none.</summary>
    public int LanBoardsAskRowAt(Vector2 p)
    {
        if (Mode != HudMode.Lan || Lan.Page != LanPage.Boards) return -1;
        var r = LanBoardsAskRect();
        if (!new Rect2(r.Position.X + 10, r.Position.Y + 85, r.Size.X - 20, 28).HasPoint(p)) return -1;
        return p.Y < r.Position.Y + 99 ? 0 : 1;
    }

    void DrawLanBoardsAsk()
    {
        var r = LanBoardsAskRect();
        Panel(r, 0.98f);
        float cx = r.GetCenter().X, y = r.Position.Y, inner = r.Size.X - 8;
        const string head = "SHARE BOARDS?";
        PixelFont.DrawCentered(this, head, cx, y + 7, Gold, PixelFont.Measure(head, 2) <= inner ? 2 : 1, new Color(0.35f, 0, 0));
        string[] what = { "YES SENDS YOUR BOARD TIMES,", "NAMES AND ATTACHED RECORDINGS", "TO COMRADES WHO ALSO SAY YES", "AND ADDS THEIRS TO YOUR BOARDS." };
        for (int i = 0; i < what.Length; i++) PixelFont.DrawCentered(this, ReplayFit(what[i], inner), cx, y + 27 + i * 9, Paper);
        PixelFont.DrawCentered(this, ReplayFit("THIS SESSION ONLY", inner), cx, y + 66, Grey);
        PixelFont.DrawCentered(this, ReplayFit("NO STOPS IT. MERGED ROWS STAY.", inner), cx, y + 75, Grey);
        MenuItem("YES, SHARE BOARDS", LanBoards.Ask, cx, y + 88);
        MenuItem("NO, DO NOT SHARE", !LanBoards.Ask, cx, y + 102);
        PixelFont.DrawCentered(this, P("UP/DOWN  ENTER CHOOSE", "D-PAD  A CHOOSE"), cx, y + 118, Grey);
        PixelFont.DrawCentered(this, P("ESC BACK", "B BACK"), cx, y + 128, Grey);
    }

    float _lanBoardsLineY = -1, _lanBoardsActionX = -1, _lanBoardsActionEnd = -1;
    /// <summary>Where the lobby's share line was last drawn (tests click it), -1 before.</summary>
    public float LanBoardsShareLineY => _lanBoardsLineY;

    /// <summary>The lobby's board line under the mouse: 0 the share line (B), 1 the progress / resync line (R), -1 none.</summary>
    public int LanBoardsLobbyLineAt(Vector2 p)
    {
        if (Mode != HudMode.Lan || Lan.Page != LanPage.Lobby || _lanBoardsLineY < 0) return -1;
        float x0 = Mathf.Floor(Screen.X / 2 - Math.Min(290, Screen.X - 8) / 2);
        if (p.X < x0 || p.X > x0 + Math.Min(290, Screen.X - 8)) return -1;
        if (p.Y >= _lanBoardsLineY - 2 && p.Y < _lanBoardsLineY + 9)
            return LanBoards.On && p.X >= _lanBoardsActionX && p.X <= _lanBoardsActionEnd ? 1 : 0;
        if (p.Y >= _lanBoardsLineY + 9 && p.Y < _lanBoardsLineY + 20) return 1;
        return -1;
    }

    /// <summary>Two lines in the lobby card: "B SHARE BOARDS: NO/YES  R RESYNC/CANCEL", then progress, the latest notice, or what YES means.</summary>
    void DrawLanLobbyBoards(Rect2 r, float y)
    {
        var b = LanBoards;
        _lanBoardsLineY = y;
        float cx = r.GetCenter().X, inner = r.Size.X - 8;
        string baseText = P("B", "Y") + (Screen.X<300 ? " BOARDS: " : " SHARE BOARDS: ") + (b.On ? "YES" : "NO");
        string action = b.On ? "   " + P("R", "X") + (b.Busy ? " CANCEL" : " RESYNC") : "";
        string share = baseText + action;
        float textLeft = cx - PixelFont.Measure(share)/2;
        _lanBoardsActionX = b.On ? textLeft + PixelFont.Measure(baseText + "   ") : -1;
        _lanBoardsActionEnd = b.On ? textLeft + PixelFont.Measure(share) : -1;
        PixelFont.DrawCentered(this, ReplayFit(share, inner), cx, y, b.On ? Green : Paper);
        string second = b.Progress ?? b.Note ?? (b.Waiting ? "BALLOT WAITS FOR BOARD SYNC" : b.On ? "SHARING TIMES, NAMES, RECORDINGS" : "TIMES, NAMES AND RECORDINGS");
        Color c = b.Progress != null ? Gold : b.Note != null ? (b.NoteBad ? Red : Paper) : Grey;
        PixelFont.DrawCentered(this, ReplayFit(second, inner), cx, y + 10, c);
    }

    /// <summary>The player row's board tag (SHARE / SYNC), drawn left of the right column; returns its width.</summary>
    float LanBoardsTag(LanRow p, float right, float y)
    {
        if (p.Boards == 0) return 0;
        string tag = p.Boards == 2 ? "SYNC" : "SHARE";
        float w = PixelFont.Measure(tag);
        PixelFont.Draw(this, tag, new Vector2(right - w, y), p.Boards == 2 ? Gold : Green);
        return w + 6;
    }
}
