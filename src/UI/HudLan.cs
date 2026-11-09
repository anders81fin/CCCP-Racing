using System;
using System.Collections.Generic;
using Godot;

namespace CCCPRacing.UI;

/// <summary>The LAN RACE pages (menu, join, lobby, the secret ballot and its count, results) and the LAN race HUD.</summary>
public enum LanPage { None, Menu, Name, Join, Ip, Connecting, Message, Lobby, Ballot, Reveal, Results, Loading, SessionResults, SessionDecision, SessionFinal, Record, Boards }

public partial class Hud
{
    public const string LanLabel = "LAN / INTERNET", LanShort = "LAN";

    public class LanRow
    {
        public string Name = "", Note;
        public Color Color;
        public int Ping = -1;
        public bool Host, You, Voted;
        /// <summary>SHARE BOARDS: 0 not sharing, 1 sharing, 2 syncing now (HudLanBoards.cs).</summary>
        public int Boards;
    }

    public readonly record struct LanStandingRow(int Rank, string Name, Color Color, bool You, bool Finished, bool Dnf, int Lap);

    /// <summary>What the LAN pages show; RaceScene.Lan fills it every frame.</summary>
    public class LanView
    {
        public LanPage Page;
        public int Sel;
        public string Text = "", Title = "", Message = "", Sub, Track;
        public bool FuelAvailable = true;
        public readonly List<string> Items = new();
        public readonly List<LanRow> Players = new();
        public bool IsHost, Voted, Damage = true, Fuel, Obstacles = true;
        public int Laps, Power, Choice;
        /// <summary>The fuels the ballot offers (a bit per fuel grade; the comrade with the most opened decides).</summary>
        public int FuelMask = 15;
        public float BallotLeft = -1;
        public string Address;
        // The count: ballots (name, colour, percent), the tally and the drum, the grid.
        public float T;
        public LanBallotShow Count;
        public readonly List<(int rank, string name, Color color, float time, float best, bool pad)> Results = new();
        public readonly List<(int rank,string name,Color color,int wins,bool connected,bool you)> SessionScores=new();
        public string WinnerName;
        public float ResultsAge;
        public bool WinnerYou;
        public string[] TrackOptions=Array.Empty<string>();
        public int TrackChoice;
        public int SessionScroll;
    }

    public class LanBallotShow
    {
        public readonly List<(string name, Color color, int pct)> Ballots = new();
        public readonly SortedDictionary<int, int> Counts = new();
        public readonly List<int> Tied = new();
        public readonly List<(string name, Color color)> Grid = new();
        public int Winner, Seed, Power;
        public string Track;
        public bool TrackDraw;
        public bool FuelAvailable=true;
        public bool Damage, Fuel, Obstacles = true;
        public readonly List<bool?> ObstacleVotes = new();
        /// <summary>Nobody submitted a ballot: everything was drawn by lot, no majority to claim.</summary>
        public bool NoVotes;
        /// <summary>One per ballot row; null: NOT VOTED (the row's pct is LanBallot.Abstain).</summary>
        public readonly List<(bool? damage, bool? fuel)> RuleVotes = new();
        public Net.LanBallot.Timeline Times;
    }

    public readonly LanView Lan = new();

    // The LAN race HUD: LAP n/m, POS p/n, race time, name tags and arrows, the leave question.
    public int LanLaps, LanLap, LanPos, LanCount;
    public float LanTime, LanLast = -1, LanBest = -1, LanDnfIn = -1;
    public bool LanDone, LanDnf, LanLeaveAsk;
    public bool LanLeaveSelected, LanLeaveHost;
    public readonly List<(Vector2 pos, string name, Color color, bool onScreen)> LanTags = new();
    public readonly List<LanStandingRow> LanStandings = new();

    public void PlayLanWinnerCheer() { _medalCheer?.Stop(); _medalCheer?.Play(); }
    public void StopLanWinnerCheer() => _medalCheer?.Stop();

    static string Laps(int n) => n == 1 ? "1 LAP" : $"{n} LAPS";

    Rect2 LanPanel(float w, float h)
    {
        var s = Screen;
        var r = new Rect2(Mathf.Floor(s.X / 2 - w / 2), Mathf.Floor(Math.Max(4, (s.Y - h) / 2 - 4)), w, h);
        Panel(r, 0.86f);
        return r;
    }

    void LanHeading(Rect2 r, string title, string sub)
    {
        float cx = r.Position.X + r.Size.X / 2;
        PixelFont.DrawCentered(this, title, cx, r.Position.Y + 7, Gold, 2, new Color(0.35f, 0, 0));
        if (sub != null) PixelFont.DrawCentered(this, sub, cx, r.Position.Y + 25, Grey);
    }

    void LanFooter(Rect2 r, string text, bool blink = false)
    {
        if (blink && (int)(_clock * 2) % 2 != 0) return;
        PixelFont.DrawCentered(this, text, r.Position.X + r.Size.X / 2, r.End.Y - 12, blink ? Gold : Grey);
    }

    void DrawLan()
    {
        var v = Lan;
        if (v.Page is LanPage.SessionResults or LanPage.SessionDecision or LanPage.SessionFinal) { DrawRace(); DrawLanSessionResults(); return; }
        if (v.Page == LanPage.Results) { DrawRace(); DrawLanResults(); return; }
        var s = Screen;
        DrawRect(new Rect2(0, 0, s.X, s.Y), new Color(0, 0, 0, 0.3f));
        switch (v.Page)
        {
            case LanPage.Menu: DrawLanMenu(); break;
            case LanPage.Record: DrawLanRecord(); break;
            case LanPage.Boards: DrawLanBoardsAsk(); break;
            case LanPage.Name: DrawLanField("YOUR NAME, TOVARISH?", "FOR THE COMRADES ON THE NETWORK", Race.Leaderboard.MaxName, P("ENTER OK  ESC BACK", WheelKeys)); break;
            case LanPage.Ip: DrawLanField("ENTER IP ADDRESS", "OF THE HOST'S MACHINE", 15, P("ENTER CONNECT  ESC BACK", WheelKeys)); break;
            case LanPage.Join: DrawLanJoin(); break;
            case LanPage.Connecting: DrawLanNote(v.Title, v.Message, Gold, P("ESC", "B") + " CANCEL", false); break;
            case LanPage.Message: DrawLanNote(v.Title, v.Message, Red, AnyKey, true); break;
            case LanPage.Loading: DrawLanNote(v.Title,v.Message,Gold,P("ESC","B")+" LEAVE",false);break;
            case LanPage.Lobby: DrawLanLobby(); break;
            case LanPage.Ballot: DrawLanBallot(); break;
            case LanPage.Reveal: DrawLanReveal(); break;
        }
    }

    void DrawLanMenu()
    {
        var v = Lan;
        string[] help = {
            "USE ZEROTIER OR PORTHOLE (ON STEAM)",
            "TO CREATE A VIRTUAL LAN.",
            $"HOST FIREWALL: ALLOW UDP {Net.LanSession.Port}.",
            $"JOINERS: ALLOW UDP {Net.LanDiscovery.Port} (GAME LIST)."
        };
        var r = LanPanel(250, 40 + v.Items.Count * 13 + 22 + 21 + help.Length * 9);
        LanHeading(r, LanLabel, "COMRADE: " + v.Text + " / " + v.Track);
        float cx = r.Position.X + r.Size.X / 2;
        for (int i = 0; i < v.Items.Count; i++) MenuItem(v.Items[i], v.Sel == i, cx, r.Position.Y + 42 + i * 13);
        float y = r.Position.Y + 48 + v.Items.Count * 13;
        PixelFont.DrawCentered(this, "PLAY OVER THE INTERNET", cx, y, Gold);
        for (int i = 0; i < help.Length; i++)
            PixelFont.DrawCentered(this, help[i], cx, y + 13 + i * 9, Grey);
        LanFooter(r, P("ENTER SELECT  ESC BACK", "A SELECT  B BACK"));
    }

    /// <summary>A text field page (the name, the IP), like the name entry of the board.</summary>
    void DrawLanField(string title, string sub, int max, string keys)
    {
        var v = Lan;
        float fw = max * 12 + 16;
        var r = LanPanel(Math.Max(260, fw + 30), 92);
        LanHeading(r, title, sub);
        float cx = r.Position.X + r.Size.X / 2;
        var f = new Rect2(Mathf.Floor(cx - fw / 2), r.Position.Y + 38, fw, 22);
        DrawRect(f, new Color(0, 0, 0, 0.6f));
        DrawRect(f, new Color(Gold, 0.8f), false, 1f);
        DrawTextField(v.Text, f, max);
        LanFooter(r, keys);
    }

    void DrawLanNote(string title, string message, Color color, string keys, bool blink)
    {
        float w = Math.Max(Math.Max(240, PixelFont.Measure(message) + 30), PixelFont.Measure(title, 2) + 30);
        var r = LanPanel(w, 70);
        float cx = r.Position.X + r.Size.X / 2;
        PixelFont.DrawCentered(this, title, cx, r.Position.Y + 8, color, 2, new Color(0.35f, 0, 0));
        PixelFont.DrawCentered(this, message, cx, r.Position.Y + 30, Paper);
        LanFooter(r, keys, blink);
    }

    void DrawLanJoin()
    {
        var v = Lan;
        int rows = Math.Max(1, v.Items.Count);
        var r = LanPanel(300, 40 + (rows + 1) * 12 + 24);
        LanHeading(r, "JOIN GAME", v.Sub ?? "GAMES ON THIS NETWORK");
        float cx = r.Position.X + r.Size.X / 2, y = r.Position.Y + 42;
        if (v.Items.Count <= 1)
        {
            PixelFont.DrawCentered(this, (int)(_clock * 2) % 2 == 0 ? "SEARCHING FOR COMRADES..." : "SEARCHING FOR COMRADES", cx, y, Grey);
            y += 12;
        }
        for (int i = 0; i < v.Items.Count; i++, y += 12) MenuItem(v.Items[i], v.Sel == i, cx, y);
        LanFooter(r, P("ENTER JOIN  ESC BACK", "A JOIN  B BACK"));
    }

    /// <summary>Colour swatch, name, host/you marks, then the right column (ping or VOTED).</summary>
    void LanPlayerRows(Rect2 r, float y, bool ballot)
    {
        float x0 = r.Position.X + 16, x1 = r.End.X - 16;
        foreach (var p in Lan.Players)
        {
            DrawRect(new Rect2(x0, y, 9, 7), p.Color);
            DrawRect(new Rect2(x0, y, 9, 7), new Color(0, 0, 0, 0.6f), false, 1f);
            string right = ballot ? (p.Voted ? "VOTED" : "THINKING...") : p.Note ?? (p.Ping < 0 ? "-" : $"{p.Ping} MS");
            string suffix=p.You?" - YOU":"", name=p.Name;
            float tagW = ballot ? 0 : LanBoardsTag(p, x1 - PixelFont.Measure(right) - 6, y);      // SHARE / SYNC (HudLanBoards.cs)
            float room=x1-PixelFont.Measure(right)-6-tagW-(x0+15);
            while(name.Length>0 && PixelFont.Measure(name+suffix)>room) name=name[..^1];
            PixelFont.Draw(this,name+suffix,new Vector2(x0+15,y),p.You?Gold:Paper);
            PixelFont.Draw(this, right, new Vector2(x1 - PixelFont.Measure(right), y), ballot ? (p.Voted ? Green : Grey) : Grey);
            y += 11;
        }
    }

    void DrawLanLobby()
    {
        var v = Lan;
        var r = LanPanel(Math.Min(290,Screen.X-8), 44 + Math.Max(4,v.Players.Count) * 11 + 50 + LanBoardsLobbyHeight);
        LanHeading(r, "THE LOBBY", null);      // leave the former track subtitle row empty
        PixelFont.DrawCentered(this,v.Address!=null?"IP: "+v.Address:v.Sub,r.GetCenter().X,r.Position.Y+33,Grey);
        LanPlayerRows(r, r.Position.Y + 44, false);
        float cx = r.Position.X + r.Size.X / 2, y = r.Position.Y + 40 + Math.Max(4,v.Players.Count) * 11 + 6;
        string laps = v.IsHost ? $"LAPS  < {v.Laps} >" : $"LAPS  {v.Laps}";
        PixelFont.DrawCentered(this, laps, cx, y, v.IsHost && !LanBoards.Focused ? Gold : Paper);
        string go = v.IsHost ?  (v.Players.Count >= 2 ? P("ENTER", "A") + " - TO THE BALLOT BOX" : "WAITING FOR COMRADES...") : "WAITING FOR THE HOST...";
        if (v.IsHost && v.Players.Count >= 2 || (int)(_clock * 2) % 2 == 0)
            PixelFont.DrawCentered(this, go, cx, y + 14, v.IsHost && v.Players.Count >= 2 ? Gold : Grey);
        DrawLanLobbyBoards(r, y + 28);      // SHARE BOARDS (HudLanBoards.cs)
        LanFooter(r, P("UP/DOWN SELECT  < > CHANGE  ", "D-PAD SELECT/CHANGE  ") + P("ESC", "B") + (v.IsHost ? " CLOSE" : " LEAVE"));
    }

    const float ChoicePitch = 14;

    /// <summary>
    /// One ballot row: every option visible in cells across [x0, x0+width), wrapping to
    /// <paramref name="columns"/> per line (a short last line is centred). The chosen cell is boxed:
    /// gold when this row has the cursor, grey when not, green once the ballot is in. Returns the height used.
    /// </summary>
    float DrawLanChoices(string[] options, int chosen, bool focus, float x0, float width, int columns, float y)
    {
        var v = Lan;
        columns = Math.Max(1, columns);
        float cell = Math.Min(width / columns, 90);  // two tracks keep fuel-sized cells, not half the card
        for (int i = 0; i < options.Length; i++)
        {
            int line = i / columns, inLine = Math.Min(columns, options.Length - line * columns);
            float x = x0 + (width - cell * inLine) / 2 + cell * (i % columns), fy = y + line * ChoicePitch;
            bool sel = i == chosen;
            if (sel)
            {
                var b = new Rect2(Mathf.Floor(x + 1), fy - 3, Mathf.Floor(cell - 2), 12);
                DrawRect(b, new Color(v.Voted ? Green : Gold, 0.25f));
                DrawRect(b, v.Voted ? Green : focus ? Gold : Grey, false, 1f);
            }
            Color c = !sel ? Grey : v.Voted ? Green : focus ? Gold : Paper;
            PixelFont.DrawCentered(this, options[i], x + cell / 2, fy, c);
        }
        return (options.Length + columns - 1) / columns * ChoicePitch;
    }

    void DrawLanBallot()
    {
        var v = Lan;
        bool narrow = Screen.X < 300;
        float w = Math.Min(370, Screen.X - 8), inner = w - 16;
        // Rows top to bottom: track, fuel, damage, fuel consumption, moving obstacles. Row ids in v.Sel: 3 track, 0 fuel, 1 damage, 2 fuel consumption, 4 moving obstacles.
        int tracks = v.TrackOptions.Length;
        int trackCols = narrow ? Math.Min(2, tracks) : Math.Max(1, tracks);
        // Only the fuels on offer (the comrade with the most opened decides): the cards keep their key numbers.
        var offered = new List<int>();
        for (int g = 0; g < Net.LanRace.FuelCount; g++) if ((v.FuelMask >> g & 1) != 0) offered.Add(g);
        if (offered.Count == 0) offered.Add(0);
        int fuelCols = narrow ? Math.Min(2, offered.Count) : offered.Count;
        int trackLines = tracks > 1 ? (tracks + trackCols - 1) / trackCols : tracks == 1 ? 1 : 0;
        int fuelLines = (offered.Count + fuelCols - 1) / fuelCols;
        float choices = (trackLines + fuelLines + 3) * ChoicePitch + (trackLines > 0 ? 4 : 0) + 4;
        var r = LanPanel(w, 42 + choices + 13 + Math.Max(4, v.Players.Count) * 11 + 34);
        LanHeading(r, "SECRET BALLOT", Screen.X<330?"VOTE TOGETHER":"CHOOSE THE TRACK AND RULES FOR ALL");
        float cx = r.Position.X + r.Size.X / 2, x0 = r.Position.X + 8, y = r.Position.Y + 42;
        if (tracks > 1)
        {
            y += DrawLanChoices(v.TrackOptions, Math.Clamp(v.TrackChoice, 0, tracks - 1), v.Sel == 3, x0, inner, trackCols, y) + 4;
        }
        else if (tracks == 1)
        {
            PixelFont.DrawCentered(this, "TRACK: " + v.TrackOptions[0], cx, y, Paper);
            y += ChoicePitch + 4;
        }
        var fuels = new string[offered.Count];
        for (int i = 0; i < fuels.Length; i++) fuels[i] = $"{offered[i] + 1} {Race.FuelGrades.Names[offered[i]]}";
        y += DrawLanChoices(fuels, Math.Max(0, offered.IndexOf(v.Choice - 1)), v.Sel == 0, x0, inner, fuelCols, y) + 4;
        // DAMAGE and FUEL CONSUMPTION: a right-aligned label, then YES / NO in the same cells.
        const float yesNo = 34, gap = 8;
        float labelW = PixelFont.Measure("FUEL CONSUMPTION"), gx = Mathf.Floor(cx - (labelW + gap + 2 * yesNo) / 2);
        string[] yn = { "YES", "NO" };
        foreach (int row in new[]{1,2,4})
        {
            if (row == 2 && !v.FuelAvailable) { PixelFont.DrawCentered(this,"NO FUEL ON THIS TRACK",cx,y,Grey); y += ChoicePitch; continue; }
            bool on = row == 1 ? v.Damage : row == 4 ? v.Obstacles : v.Fuel, focus = v.Sel == row && !v.Voted;
            string label = row == 1 ? "DAMAGE" : row == 4 ? "MOVING OBSTACLES" : "FUEL CONSUMPTION";
            PixelFont.Draw(this, label, new Vector2(gx + labelW - PixelFont.Measure(label), y), focus ? Gold : Paper);
            DrawLanChoices(yn, on ? 0 : 1, v.Sel == row, gx + labelW + gap, 2 * yesNo, 2, y);
            y += ChoicePitch;
        }
        PixelFont.DrawCentered(this, v.Voted ? "YOUR BALLOT IS IN THE BOX" : P(Screen.X<330?"ARROWS CHANGE  ENTER VOTE":"UP/DOWN ROW  < > CHANGE  ENTER VOTE", "D-PAD CHANGE  A VOTE"), cx, y, v.Voted ? Green : Paper);
        LanPlayerRows(r, y + 13, true);
        if (v.BallotLeft >= 0)
            LanFooter(r, BallotFooter(v.BallotLeft, Screen.X<330));
    }

    /// <summary>The deadline: an unsubmitted ballot is NOT VOTED, there are no default votes.</summary>
    internal static string BallotFooter(float left, bool compact)
    {
        int s = (int)MathF.Ceiling(left);
        return compact ? $"{s} S - NO BALLOT = NOT VOTED" : $"{s} S - NO BALLOT BY THE DEADLINE = NOT VOTED";
    }

    /// <summary>
    /// The count, on every screen at once: the ballots one by one, the tally, the lottery drum
    /// among the tied powers, THE PEOPLE HAVE CHOSEN, and the grid drawn by lot.
    /// </summary>
    void DrawLanReveal()
    {
        var v = Lan;
        var c = v.Count;
        if (c == null) return;
        float t = v.T;
        var tl = c.Times;
        var r = LanPanel(Math.Min(370,Screen.X-8), 238);
        bool compact=r.Size.X<300;
        float cx = r.Position.X + r.Size.X / 2;
        LanHeading(r, "THE COUNT", t >= tl.Announce && t < tl.Grid ? null : (c.TrackDraw?"TRACK DRAW: ":"TRACK: ")+c.Track);
        float y = r.Position.Y + 40;
        if (t < tl.Announce)
        {
            for (int i = 0; i < c.Ballots.Count; i++, y += 11)
            {
                if (t < Net.LanBallot.Step * (i + 1)) continue;
                var (name, col, pct) = c.Ballots[i];
                DrawRect(new Rect2(r.Position.X + (compact?12:40), y, 9, 7), col);
                var rules = c.RuleVotes[i];
                bool voted = Net.LanBallot.Voted(pct) && rules.damage is bool && rules.fuel is bool;
                string p = !voted ? "NOT VOTED" : compact?$"{Race.Training.FuelShort(pct)} D{(rules.damage==true?"+":"-")} F{(rules.fuel==true?"+":"-")}":$"{Race.Training.FuelShort(pct)} D:{(rules.damage==true ? "ON" : "OFF")} F:{(rules.fuel==true ? "ON" : "OFF")}";
                string obstacle = i<c.ObstacleVotes.Count && c.ObstacleVotes[i] is bool o ? (compact ? (o?" O+":" O-") : (o?" O:ON":" O:OFF")) : "";
                if (voted) p += obstacle;
                float nx=r.Position.X+(compact?27:55),px=r.End.X-(compact?12:40)-PixelFont.Measure(p);
                while(name.Length>0 && PixelFont.Measure(name)>px-nx-6)name=name[..^1];
                PixelFont.Draw(this,name,new Vector2(nx,y),Paper);
                PixelFont.Draw(this, p, new Vector2(px, y), voted ? Gold : Grey);
            }
            y = r.Position.Y + 40 + Math.Max(4,c.Ballots.Count) * 11 + 6;
            if (t >= tl.Tally)
            {
                var parts = new List<string>();
                foreach (var (pct, n) in c.Counts) parts.Add($"{Race.Training.FuelShort(pct)}: {n}");
                int notVoted = c.Ballots.FindAll(b => !Net.LanBallot.Voted(b.pct)).Count;
                if (notVoted > 0) parts.Add($"NOT VOTED: {notVoted}");
                for (int i = 0; i < parts.Count; i += 2)
                {
                    PixelFont.DrawCentered(this, string.Join("   ", parts.GetRange(i, Math.Min(2, parts.Count-i))), cx, y, Paper);
                    y += 10;
                }
                if (c.NoVotes)
                    PixelFont.DrawCentered(this, compact ? "NO VOTES! THE DRUM DECIDES" : "NO VOTES CAST! THE LOTTERY DRUM DECIDES", cx, y, Red);
                else if (c.Tied.Count > 1)
                    PixelFont.DrawCentered(this, "A TIE! THE LOTTERY DRUM DECIDES", cx, y, Red);
            }
            if (c.Tied.Count > 1 && t >= tl.Drum) DrawDrum(c, t - tl.Drum, cx, y + 16);
        }
        else if (t < tl.Grid)
        {
            float a = t - tl.Announce;
            if (a > 0.1f || (int)(a * 20) % 2 == 0)
            {
                // No ballot at all: no majority to announce, the lot chose.
                PixelFont.DrawCentered(this, c.NoVotes ? "NO VOTES CAST" : "THE PEOPLE HAVE CHOSEN", cx, y + 20, Paper, compact?1:2, new Color(0.35f, 0, 0));
                string choice = Race.Training.FuelName(c.Power) + " IN " + c.Track;
                int choiceScale = compact ? 1 : 2;
                PixelFont.DrawCentered(this, choice, cx, y + 44, Gold, choiceScale, new Color(0.35f, 0, 0));
                if (a > 0.8f) PixelFont.DrawCentered(this, c.NoVotes ? "ALL DRAWN BY LOT" : "99.8 % APPROVAL RATE", cx, y + 84, c.NoVotes ? Paper : Green, compact?1:2);
                if (a > 1.4f) PixelFont.DrawCentered(this, Race.FuelGrades.CardLine((int)Net.LanRace.Grade(c.Power)), cx, y + 108, Grey);
                PixelFont.DrawCentered(this, RuleSummary(c,compact), cx, y + 126, Gold);
                PixelFont.DrawCentered(this, ObstacleSummary(c,compact), cx, y + 138, Gold);
            }
        }
        else
        {
            PixelFont.DrawCentered(this, "GRID DRAWN BY LOT", cx, y + 6, Gold, compact?1:2, new Color(0.35f, 0, 0));
            y += 32;
            for (int i = 0; i < c.Grid.Count; i++, y += 13)
            {
                if (t - tl.Grid < 0.35f * (i + 1)) continue;
                var (name, col) = c.Grid[i];
                string row = $"ROW {i / 2 + 1}";
                float pad=compact?12:60, nx=r.Position.X+pad+35,rx=r.End.X-pad-PixelFont.Measure(row);
                while(name.Length>0 && PixelFont.Measure(name)>rx-nx-6)name=name[..^1];
                PixelFont.Draw(this, $"{i + 1}.", new Vector2(r.Position.X + pad, y), Grey);
                DrawRect(new Rect2(r.Position.X + pad+20, y, 9, 7), col);
                PixelFont.Draw(this, name, new Vector2(nx, y), Paper);
                PixelFont.Draw(this, row, new Vector2(rx, y), Grey);
            }
            PixelFont.DrawCentered(this, $"{Laps(v.Laps)} / {Race.Training.FuelName(c.Power)}", cx, r.End.Y - 49, Paper);
            PixelFont.DrawCentered(this, RuleSummary(c,compact), cx, r.End.Y - 36, Gold);
            PixelFont.DrawCentered(this, ObstacleSummary(c,compact), cx, r.End.Y - 25, Gold);
        }
        LanFooter(r, P("ESC", "B") + " LEAVE");
    }

    /// <summary>The rule results; a tie among the submitted votes is a (DRAW), NOT VOTED rows never count as NO.</summary>
    static string ObstacleSummary(LanBallotShow c,bool compact)
    {
        int yes=c.ObstacleVotes.FindAll(v=>v==true).Count, no=c.ObstacleVotes.FindAll(v=>v==false).Count;
        return (compact?"OBSTACLES ":"MOVING OBSTACLES ")+(c.Obstacles?"ON":"OFF")+(!c.NoVotes && yes==no && c.ObstacleVotes.Count>0?" (DRAW)":"");
    }

    static string RuleSummary(LanBallotShow c,bool compact=false)
    {
        string Rule(string label, bool result, Func<(bool? damage, bool? fuel), bool?> pick)
        {
            int on = c.RuleVotes.FindAll(v => pick(v) == true).Count, off = c.RuleVotes.FindAll(v => pick(v) == false).Count;
            return $"{label} {(result ? "ON" : "OFF")}{(!c.NoVotes && on == off ? " (DRAW)" : "")}";
        }
        string s = Rule(compact?"DMG":"DAMAGE", c.Damage, v => v.damage) + " / " + (c.FuelAvailable?Rule("FUEL", c.Fuel, v => v.fuel):compact?"NO FUEL":"NO FUEL ON THIS TRACK");
        return c.NoVotes ? s + (compact ? " - DRAWN" : " - DRAWN BY LOT") : s;
    }

    /// <summary>The lottery drum: a reel of the tied powers spinning down onto the winner.</summary>
    void DrawDrum(LanBallotShow c, float t, float cx, float y)
    {
        int n = c.Tied.Count;
        float p = Net.LanBallot.DrumPosition(n, c.Winner, c.Seed, t);
        const float H = 22, W = 110;
        var box = new Rect2(Mathf.Floor(cx - W / 2), y, W, H * 2);
        DrawRect(box, new Color(0.08f, 0.02f, 0.02f, 0.95f));
        float mid = box.Position.Y + H - 7;
        float frac = p - MathF.Floor(p);
        // The reel rolls downwards: the next option comes in from above.
        for (int k = -1; k <= 1; k++)
        {
            float yy = mid + (frac - k) * H;
            if (yy < box.Position.Y + 1 || yy > box.End.Y - 15) continue;
            int idx = Net.LanBallot.DrumIndex(n, MathF.Floor(p) + k);
            bool done = t >= Net.LanBallot.DrumSpin;
            PixelFont.DrawCentered(this, Race.Training.FuelName(c.Tied[idx]), cx, Mathf.Floor(yy), done ? Gold : Paper, 2);
        }
        DrawRect(box, Gold, false, 1f);
        DrawRect(new Rect2(box.Position.X - 6, mid + 3, 4, 7), Red);
        DrawRect(new Rect2(box.End.X + 2, mid + 3, 4, 7), Red);
    }

    void DrawLanResults()
    {
        var v = Lan;
        if (v.WinnerName != null) DrawLanResultsConfetti(v.ResultsAge);
        var r = LanPanel(Math.Min(344,Screen.X-8), 172 + Math.Max(0,v.Results.Count-4)*12);
        bool compact=r.Size.X<300;
        LanHeading(r, "RACE RESULTS", $"{Laps(v.Laps)} / {Race.Training.FuelName(v.Power)}");
        if (v.WinnerName != null)
        {
            var band = new Rect2(r.Position.X + 8, r.Position.Y + 38, r.Size.X - 16, 40);
            DrawRect(band, new Color(0.48f, 0.06f, 0.04f, 0.96f));
            DrawRect(band, Gold, false, 1f);
            DrawStar(new Vector2(band.Position.X + 14, band.Position.Y + 16), 4, Gold);
            DrawStar(new Vector2(band.End.X - 14, band.Position.Y + 16), 4, Gold);
            PixelFont.DrawCentered(this, compact?(v.WinnerYou?"YOU WIN":"RACE WINNER"):v.WinnerYou ? "YOU WIN - GLORY TO THE WORKER" : "GLORY TO THE WINNER", r.Position.X + r.Size.X / 2, band.Position.Y + 3, Gold);
            PixelFont.DrawCentered(this, v.WinnerName, r.Position.X + r.Size.X / 2, band.Position.Y + 12, Paper,PixelFont.Measure(v.WinnerName)*2>band.Size.X-32?1:2, new Color(0.2f, 0, 0));
        }
        if(compact)
        {
            float y0=r.Position.Y+84,left=r.Position.X+12,right=r.End.X-12;
            PixelFont.Draw(this,"NAME",new Vector2(left+28,y0),Grey);
            PixelFont.Draw(this,"TIME",new Vector2(right-PixelFont.Measure("TIME"),y0),Grey);y0+=12;
            foreach(var row in v.Results)
            {
                string time=row.time<0?"DNF":Race.LapTimer.Format(row.time),name=row.name;
                float tx=right-PixelFont.Measure(time);
                while(name.Length>0 && PixelFont.Measure(name)>tx-left-34)name=name[..^1];
                PixelFont.Draw(this,row.time<0?"-":$"{row.rank}.",new Vector2(left,y0),Paper);
                DrawRect(new Rect2(left+15,y0,9,7),row.color);
                PixelFont.Draw(this,name,new Vector2(left+28,y0),Paper);
                PixelFont.Draw(this,time,new Vector2(tx,y0),row.time<0?Red:Paper);y0+=12;
            }
            LanFooter(r,P("ENTER STANDINGS  ESC LEAVE","A STANDINGS  B LEAVE"));return;
        }
        float x0 = r.Position.X + 14, y = r.Position.Y + 84;
        PixelFont.Draw(this, "NAME", new Vector2(x0 + 34, y), Grey);
        PixelFont.Draw(this, "TIME", new Vector2(x0 + 150, y), Grey);
        PixelFont.Draw(this, "BEST LAP", new Vector2(x0 + 210, y), Grey);
        PixelFont.Draw(this, "CAR", new Vector2(r.End.X - 14 - PixelFont.Measure("CAR"), y), Grey);
        y += 12;
        foreach (var (rank, name, col, time, best, pad) in v.Results)
        {
            bool dnf = time < 0;
            PixelFont.Draw(this, dnf ? "-" : $"{rank}.", new Vector2(x0, y), rank == 1 && !dnf ? Gold : Paper);
            DrawRect(new Rect2(x0 + 20, y, 9, 7), col);
            PixelFont.Draw(this, name, new Vector2(x0 + 34, y), rank == 1 && !dnf ? Gold : Paper);
            PixelFont.Draw(this, dnf ? "DNF" : Race.LapTimer.Format(time), new Vector2(x0 + 150, y), dnf ? Red : Paper);
            PixelFont.Draw(this, Race.LapTimer.Format(best), new Vector2(x0 + 210, y), Grey);
            if (!dnf) DrawInputMark(new Vector2(x0 + 260, y), pad, pad ? new Color(Paper, 0.8f) : new Color(Grey, 0.7f));
            string pw = Race.Training.FuelShort(v.Power);
            PixelFont.Draw(this, pw, new Vector2(r.End.X - 14 - PixelFont.Measure(pw), y), Grey);
            y += 12;
        }
        LanFooter(r, P("ENTER SESSION STANDINGS  ESC LEAVE", "A SESSION STANDINGS  B LEAVE"));
    }

    void DrawLanSessionResults()
    {
        var v=Lan;
        if(v.Page==LanPage.SessionFinal && v.SessionScores.Exists(s=>s.wins>0)) DrawLanResultsConfetti(v.ResultsAge);
        var r=LanPanel(Math.Min(344,Screen.X-8),Math.Min(232,88+v.SessionScores.Count*12));
        if(v.Page==LanPage.SessionDecision)
        {
            if(r.Size.X<300) PixelFont.DrawCentered(this,"ANOTHER RACE?",r.GetCenter().X,r.Position.Y+10,Gold);
            else LanHeading(r,"ANOTHER RACE, TOVARISH?","THE HOST DECIDES FOR THE SESSION");
            string[] choices={"CONTINUE RACING","END SESSION"};
            for(int i=0;i<choices.Length;i++)
                PixelFont.DrawCentered(this,(v.Sel==i?"> ":"  ")+choices[i],r.Position.X+r.Size.X/2,r.Position.Y+44+i*18,v.Sel==i?Gold:Paper);
            LanFooter(r,P("ENTER SELECT  ESC LEAVE","A SELECT  B LEAVE"));return;
        }
        bool final=v.Page==LanPage.SessionFinal;
        if(r.Size.X<300) PixelFont.DrawCentered(this,final?"SESSION COMPLETE":"SESSION STANDINGS",r.GetCenter().X,r.Position.Y+10,Gold);
        else LanHeading(r,final?"SESSION COMPLETE":"SESSION STANDINGS",final?null:"VICTORIES ACROSS ALL TRACKS");
        if(final)
        {
            var winners=v.SessionScores.FindAll(s=>s.wins>0 && s.rank==1);
            string title=winners.Count==0?"NO WINNER":winners.Count==1?"SESSION WINNER":"JOINT SESSION WINNERS";
            PixelFont.DrawCentered(this,title,r.Position.X+r.Size.X/2,r.Position.Y+30,Gold);
        }
        float y=r.Position.Y+(v.Page==LanPage.SessionFinal?44:38);
        PixelFont.Draw(this,"NAME",new Vector2(r.Position.X+50,y),Grey);
        PixelFont.Draw(this,"WINS",new Vector2(r.End.X-40,y),Grey);
        y+=12;
        foreach(var s in System.Linq.Enumerable.Skip(v.SessionScores,v.SessionScroll))
        {
            if(y>r.End.Y-25)break;
            if(s.you) DrawRect(new Rect2(r.Position.X+8,y-2,r.Size.X-16,11),new Color(Gold,.14f));
            PixelFont.Draw(this,$"{s.rank}.",new Vector2(r.Position.X+14,y),s.rank==1?Gold:Paper);
            DrawRect(new Rect2(r.Position.X+34,y,9,7),s.color);
            string suffix=s.connected?"":" (LEFT)", name=s.name;
            while(name.Length>0 && PixelFont.Measure(name+suffix)>r.Size.X-100) name=name[..^1];
            name+=suffix;
            PixelFont.Draw(this,name,new Vector2(r.Position.X+50,y),final && s.rank==1 && s.wins>0?Gold:s.connected?Paper:Grey);
            PixelFont.Draw(this,s.wins.ToString(),new Vector2(r.End.X-28,y),Gold);
            y+=12;
        }
        LanFooter(r,v.Page==LanPage.SessionFinal?P("ENTER LEAVE SESSION","A LEAVE SESSION"):
            v.IsHost?P("ENTER HOST DECISION  ESC LEAVE","A HOST DECISION  B LEAVE"):P("WAIT FOR HOST  ESC LEAVE","WAIT FOR HOST  B LEAVE"));
    }

    void DrawLanResultsConfetti(float age)
    {
        if (age >= 3f) return;
        float fade = 1 - Mathf.SmoothStep(2.2f, 3f, age);
        for (int i = 0; i < 48; i++)
        {
            float delay = Track.Noise.Hash01(i, 1, 1945) * 0.22f;
            float t = age - delay;
            if (t < 0) continue;
            float x = Track.Noise.Hash01(i, 2, 1945) * Screen.X + MathF.Sin(t * 5 + i) * 5;
            float y = -5 + (25 + 35 * Track.Noise.Hash01(i, 3, 1945)) * t + 20 * t * t;
            if (y >= Screen.Y) continue;
            DrawRect(new Rect2(Mathf.Floor(x), Mathf.Floor(y), 2, 3), new Color(i % 3 == 0 ? Red : i % 3 == 1 ? Gold : Paper, fade));
        }
    }

    void DrawLanStandings()
    {
        if (LanStandings.Count == 0) return;
        const float width = 154, rowHeight = 11;
        var r = new Rect2(Screen.X - width - 4, 4, width, 18 + LanStandings.Count * rowHeight);
        Panel(r, 0.83f);
        PixelFont.Draw(this, "RACE ORDER", r.Position + new Vector2(7, 5), Gold);
        for (int i = 0; i < LanStandings.Count; i++)
        {
            var row = LanStandings[i];
            float y = r.Position.Y + 17 + i * rowHeight;
            if (row.You) DrawRect(new Rect2(r.Position.X + 3, y - 2, width - 6, 10), new Color(Gold, 0.16f));
            PixelFont.Draw(this, $"{row.Rank}.", new Vector2(r.Position.X + 7, y), row.Rank == 1 ? Gold : Grey);
            DrawRect(new Rect2(r.Position.X + 21, y, 6, 6), row.Color);
            string name = row.Name.Length > 12 ? row.Name[..12] : row.Name;
            PixelFont.Draw(this, name, new Vector2(r.Position.X + 31, y), row.Dnf ? Grey : row.You ? Gold : Paper);
            string state = row.Dnf ? "DNF" : row.Finished ? "FIN" : $"L{row.Lap}/{LanLaps}";
            PixelFont.Draw(this, state, new Vector2(r.End.X - 7 - PixelFont.Measure(state), y), row.Dnf ? Red : row.Finished ? Gold : Grey);
        }
    }

    /// <summary>The LAN lap panel: LAP n/m, POS p/n, race time, last and best lap, the power.</summary>
    void DrawLanPanel()
    {
        Panel(new Rect2(4, 4, 104, LanDnfIn >= 0 && !LanDone ? 62 : 53));
        PixelFont.Draw(this, LanDnf ? "DNF" : LanDone ? "FINISHED" : $"LAP {Math.Min(LanLap, LanLaps)}/{LanLaps}", new Vector2(9, 8), LanDnf ? Red : Gold);
        string pos = $"POS {LanPos}/{LanCount}";
        PixelFont.Draw(this, pos, new Vector2(104 - PixelFont.Measure(pos), 8), LanPos == 1 ? Gold : Paper);
        PixelFont.Draw(this, "TIME " + Race.LapTimer.Format(LanTime), new Vector2(9, 18), Paper);
        PixelFont.Draw(this, "BEST " + Race.LapTimer.Format(LanBest), new Vector2(9, 27), Grey);
        PixelFont.Draw(this, "LAST " + Race.LapTimer.Format(LanLast), new Vector2(9, 36), Grey);
        int pct = (int)MathF.Round(PowerScale * 100);
        PixelFont.Draw(this, $"POWER {pct}%", new Vector2(9, 45), pct < 100 ? Red : pct == 100 ? Gold : Green);
        if (LanDnfIn >= 0 && !LanDone)
            PixelFont.Draw(this, $"DNF IN {(int)MathF.Ceiling(LanDnfIn)} S", new Vector2(9, 54), (int)(_clock * 3) % 2 == 0 ? Red : Paper);
    }

    /// <summary>A name over each comrade's car in its colour; off screen, an arrow at the edge towards it.</summary>
    void DrawLanTags()
    {
        var s = Screen;
        Vector2 c = s / 2;
        const float Margin = 10f;
        var placed = new List<Rect2>();
        foreach (var (p, name, col, on) in LanTags)
        {
            if (!float.IsFinite(p.X) || !float.IsFinite(p.Y)) continue;
            bool inView = on && p.X > Margin && p.X < s.X - Margin && p.Y > Margin && p.Y < s.Y - Margin;
            if (inView)
            {
                // Cars side by side: a tag that would cover another goes up a line.
                float w = PixelFont.Measure(name);
                var tag = new Rect2(Mathf.Floor(p.X - w / 2 - 2), Mathf.Floor(p.Y - 12), w + 4, 10);
                for (int k = 0; k < 4 && placed.Exists(o => o.Intersects(tag)); k++) tag.Position -= new Vector2(0, 10);
                placed.Add(tag);
                DrawRect(tag, new Color(0.05f, 0.03f, 0.03f, 0.55f));
                PixelFont.Draw(this, name, tag.Position + new Vector2(2, 1), col);
                continue;
            }
            Vector2 v = p - c;
            if (v.LengthSquared() < 1f) v = Vector2.Up;
            Vector2 n = v.Normalized();
            float t = Math.Min((c.X - Margin) / Math.Max(Math.Abs(v.X), 1e-3f), (c.Y - Margin) / Math.Max(Math.Abs(v.Y), 1e-3f));
            Vector2 tip = c + v * t, side = new(-n.Y, n.X), back = tip - n * 8f;
            DrawColoredPolygon(new[] { tip + n * 1.5f, back + side * 6f - n * 1.5f, back - side * 6f - n * 1.5f }, new Color(0.1f, 0.03f, 0.03f));
            DrawColoredPolygon(new[] { tip.Floor(), (back + side * 4.5f).Floor(), (back - side * 4.5f).Floor() }, col);
            // The name beside the arrow, kept on screen.
            float w2 = PixelFont.Measure(name);
            Vector2 at = back - n * 6f;
            float lx = Math.Clamp(at.X - w2 / 2, 2, s.X - w2 - 2), ly = Math.Clamp(at.Y - 3, 2, s.Y - 9);
            PixelFont.Draw(this, name, new Vector2(Mathf.Floor(lx), Mathf.Floor(ly)), col);
        }
    }

    Rect2 LanLeaveRect()
    {
        float w=Math.Min(330,Screen.X-12),h=w<290?122:94;
        return new(Mathf.Floor((Screen.X-w)/2),Mathf.Floor((Screen.Y-h)/2),w,h);
    }

    public int LanLeaveRowAt(Vector2 p)
    {
        var r=LanLeaveRect();
        if(r.Size.X<290)
        {
            if(!new Rect2(r.Position.X+10,r.Position.Y+50,r.Size.X-20,36).HasPoint(p)) return -1;
            return p.Y<r.Position.Y+68?0:1;
        }
        if(!new Rect2(r.Position.X+10,r.Position.Y+46,r.Size.X-20,22).HasPoint(p)) return -1;
        return p.X<r.GetCenter().X?0:1;
    }

    /// <summary>One modal above every LAN page. The shared race and ballot keep advancing below it.</summary>
    void DrawLanLeave()
    {
        if (!LanLeaveAsk) return;
        DrawRect(new Rect2(Vector2.Zero,Screen),new Color(0,0,0,.55f));
        var r=LanLeaveRect();Panel(r,.98f);
        float cx=r.GetCenter().X;
        if(r.Size.X<290)
        {
            PixelFont.DrawCentered(this,LanLeaveHost?"CLOSE LAN GAME?":"LEAVE LAN GAME?",cx,r.Position.Y+8,Gold);
            PixelFont.DrawCentered(this,LanLeaveHost?"THIS ENDS THE GAME":"OTHER PLAYERS CAN",cx,r.Position.Y+25,Paper);
            PixelFont.DrawCentered(this,LanLeaveHost?"FOR EVERYONE.":"KEEP RACING.",cx,r.Position.Y+35,Paper);
            MenuItem("STAY IN GAME",!LanLeaveSelected,cx,r.Position.Y+54);
            MenuItem(LanLeaveHost?"CLOSE GAME":"LEAVE GAME",LanLeaveSelected,cx,r.Position.Y+72);
            PixelFont.DrawCentered(this,P("UP/DOWN SELECT","D-PAD SELECT"),cx,r.Position.Y+96,Grey);
            PixelFont.DrawCentered(this,P("ENTER OK  ESC BACK","A OK  B BACK"),cx,r.Position.Y+107,Grey);
            return;
        }
        PixelFont.DrawCentered(this,LanLeaveHost?"CLOSE LAN GAME?":"LEAVE LAN GAME?",cx,r.Position.Y+8,Gold,2);
        PixelFont.DrawCentered(this,LanLeaveHost?"THIS ENDS THE GAME FOR EVERYONE.":"OTHER PLAYERS CAN KEEP RACING.",cx,r.Position.Y+30,Paper);
        MenuItem("STAY IN GAME",!LanLeaveSelected,cx-80,r.Position.Y+52);
        MenuItem(LanLeaveHost?"CLOSE GAME":"LEAVE GAME",LanLeaveSelected,cx+80,r.Position.Y+52);
        PixelFont.DrawCentered(this,P("ARROWS SELECT  ENTER OK  ESC BACK","D-PAD SELECT  A OK  B BACK"),cx,r.Position.Y+78,Grey);
    }
}
