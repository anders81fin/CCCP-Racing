using System;
using System.Collections.Generic;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.UI;

/// <summary>A HUD element the driving school points at with a pulsing box.</summary>
public enum SchoolBox { None, Norm, Fuel, Engine, Speedo, Dose }

/// <summary>
/// The KOLKHOZ DRIVING SCHOOL over the race HUD (RaceScene.School.cs): the lesson's title, the
/// demo's caption, the TELL card with the instructor (the KGB supervisor) and the controls drawn
/// as keycaps or pad buttons (whichever was used last), the try's hint line, the banners (YOUR
/// TURN!, AGAIN!, WELL DONE), a bouncing arrow at a thing in the world, a pulsing box on a gauge,
/// the recap card and the leave question. On the title: the INSTRUCTIONS? and track cards.
/// </summary>
public partial class Hud
{
    public bool School;
    public int SchoolChapterCount;
    /// <summary>The top line: which lesson of how many, and what to do (WATCH / YOUR TURN).</summary>
    public string SchoolTag;
    /// <summary>The demo's words at the bottom of the screen (null: none).</summary>
    public string SchoolCaption;
    public string SchoolSubtitle;
    /// <summary>The TELL card (null: none) and the controls on it.</summary>
    public string[] SchoolTell;
    public bool SchoolTellAtTop;
    public bool SchoolScenic;
    public SchoolKey[] SchoolTellKeys = Array.Empty<SchoolKey>();
    /// <summary>A try: its hint and controls, and which try of MaxTries.</summary>
    public string SchoolHint;
    public SchoolKey[] SchoolHintKeys = Array.Empty<SchoolKey>();
    public int SchoolTry;
    /// <summary>The instructor's bubble on the praise banner.</summary>
    public string SchoolBubble;
    /// <summary>Screen point the arrow points down at (null: none).</summary>
    public Vector2? SchoolArrow;
    public SchoolBox SchoolHighlight;
    /// <summary>The steering lesson: where the car's own left and right are on the screen (null: no tags), and the key held (-1 left, 1 right, 0 none).</summary>
    public Vector2? SchoolLeftTag, SchoolRightTag;
    public int SchoolSteerKey;
    /// <summary>The arrow keys under the caption, the held one lit (the steering demo).</summary>
    public bool SchoolSteerKeys;
    /// <summary>The slide lessons' road markings: the words over each strip (screen point, text, colour, the car is on it).</summary>
    public readonly List<(Vector2 at, string text, Color col, bool lit)> SchoolMarks = new();
    public bool SchoolAsk, HideLapPanel;
    /// <summary>The recap card's controls (null: not shown) and its way out.</summary>
    public (SchoolKey key, string what)[] SchoolRecap;
    public string SchoolRecapGo;
    /// <summary>A choice card over the title (null: none): its heading, a line under it, the options and the one selected.</summary>
    public string CardTitle, CardSub;
    /// <summary>The highlighted track has a scenic tour (T / Y opens it).</summary>
    public bool CardTour;
    public string[] CardOptions = Array.Empty<string>();
    public int CardSelection;
    /// <summary>Options not open yet (a locked track): drawn in black and white. Null: all open.</summary>
    public bool[] CardLocked;

    string _banner, _bannerSub;
    Color _bannerColor;
    float _bannerFrom, _bannerUntil;

    static readonly Color Cap = new(0.16f, 0.14f, 0.13f), CapLit = new(0.42f, 0.28f, 0.04f), CapEdge = new(0.05f, 0.03f, 0.03f);
    static readonly Color PadA = new(0.3f, 0.75f, 0.3f), PadB = new(0.85f, 0.2f, 0.15f), PadX = new(0.25f, 0.45f, 0.9f), PadY = new(0.95f, 0.8f, 0.2f);
    static readonly Color Dim = new(0.35f, 0.33f, 0.31f);

    /// <summary>A big line across the upper screen (YOUR TURN!, AGAIN!, WELL DONE, TOVARISH!).</summary>
    public void SchoolBanner(string text, string sub, float duration, Color color)
    {
        _banner = text;
        _bannerSub = sub;
        _bannerColor = color;
        _bannerFrom = _clock;
        _bannerUntil = _clock + duration;
    }

    public void ClearSchoolBanner() => _banner = null;

    Rect2 SpeedoRect => new(Screen.X - 92, Screen.Y - 40, 88, 36);

    Rect2 BoxRect(SchoolBox b)
    {
        var sp = SpeedoRect;
        return b switch
        {
            SchoolBox.Norm => new Rect2(4, 4, LapPanelWidth, LapPanelHeight),
            SchoolBox.Speedo => sp,
            SchoolBox.Engine => new Rect2(sp.Position.X - 50, sp.Position.Y, 46, sp.Size.Y),
            SchoolBox.Fuel => new Rect2(sp.Position.X - (EngineHealth >= 0 ? 50 : 0) - 40, sp.Position.Y, 36, sp.Size.Y),
            SchoolBox.Dose => new Rect2(sp.Position.X - (EngineHealth >= 0 ? 50 : 0) - (Fuel >= 0 ? 40 : 0) - 40, sp.Position.Y, 36, sp.Size.Y),
            _ => default,
        };
    }

    void DrawSchool()
    {
        if (SchoolHighlight != SchoolBox.None) DrawHighlight(BoxRect(SchoolHighlight));
        if (SchoolArrow is Vector2 a) DrawWorldArrow(a);
        foreach (var m in SchoolMarks) MarkLabel(m.at, m.text, m.col, m.lit);
        if (SchoolLeftTag is Vector2 lt) SideTag(lt, "LEFT", SchoolSteerKey < 0);
        if (SchoolRightTag is Vector2 rt) SideTag(rt, "RIGHT", SchoolSteerKey > 0);
        var s = Screen;
        float cx = s.X / 2;
        if (SchoolTag != null && SchoolTell == null && SchoolRecap == null)
        {
            float w = PixelFont.Measure(SchoolTag) + 12;
            var r = new Rect2(Mathf.Floor(cx - w / 2), 4, w, 13);
            Panel(r, 0.75f);
            PixelFont.DrawCentered(this, SchoolTag, cx, r.Position.Y + 3, Gold);
        }
        if (SchoolCaption != null && SchoolTell == null)
            DrawLessonCaption(SchoolCaption, new Rect2(Vector2.Zero, s));
        if (SchoolSubtitle != null)
        {
            string[] lines = SchoolSubtitle.Split(" / ");
            float y = Mathf.Floor(s.Y * 0.36f - (lines.Length - 1) * 14f);
            foreach (string line in lines)
            {
                int scale = PixelFont.Measure(line, 3) <= s.X - 16 ? 3 : 2;
                PixelFont.DrawCentered(this, line, cx, y, Gold, scale, new Color(0.35f, 0, 0));
                y += 28f;
            }
        }
        if (SchoolSteerKeys && SchoolCaption != null && SchoolTell == null && !Pad)
            Arrows(new Vector2(Mathf.Floor(cx - 23), Mathf.Floor(s.Y - 64)), false, false, SchoolSteerKey < 0, SchoolSteerKey > 0);
        if (SchoolHint != null && SchoolTell == null) DrawHint();
        DrawSchoolBanner();
        if (SchoolTell != null) DrawTell();
        if (SchoolRecap != null) DrawRecap();
        if (SchoolAsk) DrawLeaveAsk();
        if (SchoolChapterCount > 1) PixelFont.Draw(this, $"PRESS 1-{SchoolChapterCount} TO JUMP TO A LESSON", new Vector2(8, s.Y - 10), Grey);
    }

    /// <summary>A pulsing box round a HUD element, gold and red by turns.</summary>
    void DrawHighlight(Rect2 r)
    {
        if (r.Size == Vector2.Zero) return;
        float grow = 2f + MathF.Abs(MathF.Sin(_clock * 6f)) * 2f;
        var rr = r.Grow(Mathf.Floor(grow));
        Color c = (int)(_clock * 4) % 2 == 0 ? Gold : Red;
        DrawRect(rr.Grow(1), new Color(0, 0, 0, 0.6f), false, 1f);
        DrawRect(rr, c, false, 2f);
    }

    /// <summary>A bouncing arrow pointing down at a screen point; off screen it waits at the edge, pointing there.</summary>
    void DrawWorldArrow(Vector2 p)
    {
        var s = Screen;
        const float M = 14f;
        if (!float.IsFinite(p.X) || !float.IsFinite(p.Y)) return;
        bool inView = p.X > M && p.X < s.X - M && p.Y > M + 20 && p.Y < s.Y - M;
        Color c = (int)(_clock * 3) % 2 == 0 ? Gold : new Color(1f, 0.95f, 0.6f);
        if (inView)
        {
            float bounce = Mathf.Floor(MathF.Abs(MathF.Sin(_clock * 5f)) * 6f);
            Vector2 tip = new(Mathf.Floor(p.X), Mathf.Floor(p.Y) - 3 - bounce);
            // Outline, then the head and the shaft.
            for (int k = 0; k < 6; k++) DrawRect(new Rect2(tip.X - k - 1, tip.Y - k - 1, 2 * k + 3, 1), CapEdge);
            DrawRect(new Rect2(tip.X - 3, tip.Y - 16, 7, 11), CapEdge);
            for (int k = 0; k < 5; k++) DrawRect(new Rect2(tip.X - k, tip.Y - k, 2 * k + 1, 1), c);
            DrawRect(new Rect2(tip.X - 2, tip.Y - 15, 5, 11), c);
            return;
        }
        Vector2 ctr = s / 2, v = p - ctr;
        if (v.LengthSquared() < 1f) v = Vector2.Down;
        float t = Math.Min((ctr.X - M) / Math.Max(Math.Abs(v.X), 1e-3f), (ctr.Y - M) / Math.Max(Math.Abs(v.Y), 1e-3f));
        Vector2 n = v.Normalized(), e = ctr + v * t, side = new(-n.Y, n.X), back = e - n * 10f;
        DrawColoredPolygon(new[] { e + n * 1.5f, back + side * 7f - n * 1.5f, back - side * 7f - n * 1.5f }, CapEdge);
        DrawColoredPolygon(new[] { e, back + side * 5.5f, back - side * 5.5f }, c);
    }

    void DrawSchoolBanner()
    {
        if (_banner == null || _clock > _bannerUntil) return;
        var s = Screen;
        float age = _clock - _bannerFrom;
        if (age < 0.4f && (int)(age * 10) % 2 == 1) return;
        int sc = PixelFont.Measure(_banner, 3) < s.X - 70 ? 3 : 2;
        float y = Mathf.Floor(s.Y * 0.24f);
        float w = PixelFont.Measure(_banner, sc);
        PixelFont.DrawCentered(this, _banner, s.X / 2, y, _bannerColor, sc, new Color(0.35f, 0, 0));
        if (_bannerSub != null) PixelFont.DrawCentered(this, _bannerSub, s.X / 2, y + 7 * sc + 6, Paper);
        if (SchoolBubble != null) Instructor(new Vector2(Mathf.Floor(s.X / 2 - w / 2 - 42), y - 14), SchoolBubble);
    }

    // Shared by track instructions and the live menu video, in the video rectangle's coordinates.
    void DrawLessonCaption(string text, Rect2 area)
    {
        int scale = PixelFont.Measure(text, 2) < area.Size.X - 16 ? 2 : 1;
        float inset = area.Size == Screen ? 84 : Math.Min(84, Mathf.Floor(area.Size.Y * 0.31f));
        PixelFont.DrawCentered(this, text, area.GetCenter().X, Mathf.Floor(area.End.Y - inset),
            Gold, scale, new Color(0.35f, 0, 0));
    }

    void DrawLessonHeading(Rect2 area, string title, string subtitle = null)
    {
        float by = area.Position.Y + Mathf.Floor(area.Size.Y * 0.3f);
        float h = subtitle != null ? 58 : 44, cx = area.GetCenter().X;
        DrawRect(new Rect2(area.Position.X, by, area.Size.X, h), new Color(Red, 0.92f));
        DrawRect(new Rect2(area.Position.X, by - 2, area.Size.X, 2), Gold);
        DrawRect(new Rect2(area.Position.X, by + h, area.Size.X, 2), Gold);
        if (subtitle != null) PixelFont.DrawCentered(this, subtitle, cx, by + 7, Paper);
        int scale = PixelFont.Measure(title, 3) <= area.Size.X - 16 ? 3
            : PixelFont.Measure(title, 2) <= area.Size.X - 16 ? 2 : 1;
        float ty = by + (subtitle != null ? 24 : (h - 7 * scale) / 2);
        PixelFont.DrawCentered(this, title, cx, ty, Gold, scale, new Color(0.3f, 0, 0));
        float tw = PixelFont.Measure(title, scale);
        if (tw <= area.Size.X - 48)
        {
            DrawStar(new Vector2(cx - tw / 2 - 16, ty + 10), 2, Gold);
            DrawStar(new Vector2(cx + tw / 2 + 16, ty + 10), 2, Gold);
        }
    }

    /// <summary>The rally's stop card (RaceScene.Cards): title, lines; set while the game stands under it.</summary>
    public (string title, string[] lines, Color color)? StopCard;
    /// <summary>The card takes a key now (after RaceScene.CardMinShow): only then does it say PRESS ANY KEY.</summary>
    public bool StopCardReady;

    /// <summary>The stop card: the instructor, a big title, the lines (the first in gold), any key goes on.</summary>
    void DrawStopCard(string title, string[] lines, Color color)
    {
        var s = Screen;
        float cx = s.X / 2;
        int tsc = PixelFont.Measure(title, 2) <= s.X - 80 ? 2 : 1;
        float textW = PixelFont.Measure(title, tsc);
        foreach (var l in lines) textW = Math.Max(textW, PixelFont.Measure(l));
        float w = Math.Min(s.X - 8, textW + 76), h = 26 + 7 * tsc + lines.Length * 11 + 18;
        if (NormMissCard) h += 30;
        var r = new Rect2(Mathf.Floor(cx - w / 2), Mathf.Floor(s.Y * 0.48f - h / 2), w, h);
        DrawRect(new Rect2(0, 0, s.X, s.Y), new Color(0, 0, 0, 0.35f));
        Panel(r, 0.98f);
        DrawRect(new Rect2(r.Position.X, r.Position.Y - 2, r.Size.X, 2), Gold);
        Instructor(r.Position + new Vector2(8, 14), null);
        float tx = r.Position.X + 52 + (r.Size.X - 58) / 2;
        float y = r.Position.Y + 9;
        PixelFont.DrawCentered(this, title, tx, y, color, tsc, new Color(0.35f, 0, 0));
        y += 7 * tsc + 9;
        if (NormMissCard)
        {
            DrawNormMissStamp(new Vector2(tx, y + 7), _clock - _normMissCardStarted);
            y += 30;
        }
        for (int i = 0; i < lines.Length; i++, y += 11)
            PixelFont.DrawCentered(this, lines[i], tx, y, i == 0 ? Gold : Paper);
        string go = P("PRESS ANY KEY", "PRESS ANY BUTTON");
        if (StopCardReady && (int)(_clock * 2) % 2 == 0) PixelFont.Draw(this, go, new Vector2(r.End.X - PixelFont.Measure(go) - 6, r.End.Y - 11), Grey);
    }

    /// <summary>The TELL card: the instructor, one or two big lines, the controls, the way on.</summary>
    void DrawTell() => DrawLessonTell(new Rect2(Vector2.Zero, Screen), SchoolTell,
        SchoolTellKeys, SchoolTag, SchoolTellAtTop, true);

    void DrawLessonTell(Rect2 area, string[] lines, SchoolKey[] controls, string tag = null,
        bool atTop = false, bool prompt = false)
    {
        float cx = area.GetCenter().X;
        bool keys = controls.Length > 0;
        float textW = 0;
        foreach (var l in lines) textW = Math.Max(textW, PixelFont.Measure(l, 2));
        bool compact = area.Size.X < 272;
        float textInset = area.Size.X < 248 ? 24 : compact ? 42 : 52;
        float w = Math.Min(area.Size.X - (compact ? 8 : 12), Math.Max(textW + 70, 260)), h = 22 + lines.Length * 19 + (keys ? 36 : 0) + 14;
        var r = new Rect2(Mathf.Floor(cx - w / 2), area.Position.Y + Mathf.Floor(atTop ? 8f : area.Size.Y * 0.62f - h / 2), w, h);
        Panel(r, 0.9f);
        if (tag != null) PixelFont.Draw(this, tag, r.Position + new Vector2(6, 5), Grey);
        Instructor(r.Position + new Vector2(8, 20), null, area.Size.X < 248 ? 1 : 2);
        float tx = r.Position.X + textInset + (r.Size.X - textInset - 6) / 2;
        float y = r.Position.Y + 20;
        for (int i = 0; i < lines.Length; i++, y += 19)
        {
            string l = lines[i];
            int sc = PixelFont.Measure(l, 2) <= r.Size.X - 60 ? 2 : 1;
            PixelFont.DrawCentered(this, l, tx, y + (sc == 1 ? 4 : 0), i == 0 ? Gold : Paper, sc, new Color(0.35f, 0, 0));
        }
        if (keys) DrawKeyRow(controls, tx, y + 2);
        string go = P("ENTER GO ON   ESC LEAVE", "A GO ON   START LEAVE");
        if (prompt && (int)(_clock * 2) % 2 == 0) PixelFont.Draw(this, go, new Vector2(r.End.X - PixelFont.Measure(go) - 6, r.End.Y - 11), Grey);
    }

    /// <summary>The try's hint over the gauges: the controls and a few words, which try, how to skip.</summary>
    void DrawHint()
    {
        var s = Screen;
        float kw = KeyRowWidth(SchoolHintKeys);
        string sub = $"TRY {SchoolTry} OF {SchoolScript.MaxTries}   " + P("ENTER SKIP", "X SKIP");
        float tw = Math.Max(PixelFont.Measure(SchoolHint), PixelFont.Measure(sub));
        float w = kw + tw + (kw > 0 ? 22 : 12), h = 36;
        var r = new Rect2(Mathf.Floor(s.X / 2 - w / 2), Mathf.Floor(s.Y - 90), w, h);
        Panel(r, 0.8f);
        if (kw > 0) DrawKeyRow(SchoolHintKeys, r.Position.X + 6 + kw / 2, r.Position.Y + 4);
        float x = r.Position.X + (kw > 0 ? kw + 16 : 6);
        PixelFont.Draw(this, SchoolHint, new Vector2(x, r.Position.Y + 8), Gold);
        PixelFont.Draw(this, sub, new Vector2(x, r.Position.Y + 21), Grey);
    }

    void DrawLeaveAsk()
    {
        var s = Screen;
        var r = new Rect2(Mathf.Floor(s.X / 2 - 125), Mathf.Floor(s.Y / 2 - 26), 250, 50);
        Panel(r, 0.92f);
        PixelFont.DrawCentered(this, "LEAVE THE SCHOOL?", s.X / 2, r.Position.Y + 9, Gold, 2, new Color(0.35f, 0, 0));
        PixelFont.DrawCentered(this, P("ENTER = YES   ESC = NO", "A = YES   B = NO"), s.X / 2, r.Position.Y + 33, Paper);
    }

    /// <summary>The last card: each control drawn separately with its name.</summary>
    void DrawRecap()
    {
        var s = Screen;
        float cx = s.X / 2;
        const float CellW = 142, CellH = 50;
        int rows = (SchoolRecap.Length + 2) / 3;
        float w = Math.Min(s.X - 8, CellW * 3 + 16), h = 34 + CellH * rows + 22;
        var r = new Rect2(Mathf.Floor(cx - w / 2), Mathf.Floor((s.Y - h) / 2), w, h);
        Panel(r, 0.92f);
        PixelFont.DrawCentered(this, "YOU ARE READY, TOVARISH!", cx, r.Position.Y + 9, Gold, 2, new Color(0.35f, 0, 0));
        DrawControlGrid(SchoolRecap, cx, r.Position.Y + 32, w, CellH);
        if ((int)(_clock * 2) % 2 == 0) PixelFont.DrawCentered(this, SchoolRecapGo, cx, r.End.Y - 14, Gold);
    }

    void DrawControlGrid((SchoolKey key, string what)[] controls, float cx, float top, float width, float cellHeight = 50)
    {
        float cw = (width - 16) / 3;
        for (int i = 0; i < controls.Length; i++)
        {
            var (key, what) = controls[i];
            int inRow = Math.Min(3, controls.Length - i / 3 * 3);           // a short last row is centred
            float x = cx + (i % 3 - (inRow - 1) / 2f) * cw, y = top + (i / 3) * cellHeight;
            DrawKeyRow(new[] { key }, x, y);
            PixelFont.DrawCentered(this, what, x, y + 32, Paper);
        }
    }

    /// <summary>A choice over the title: INSTRUCTIONS, TOVARISH? YES / NO, or the track card.</summary>
    /// <summary>The card's top: under the mode's hint card (DrawTitle sets it).</summary>
    float _cardTop;

    /// <summary>
    /// The title's card before a run (the track, the Stakhanov laps' time of day and light): under the
    /// selected mode's hint card, above the credits line. Where that room is short the card is compact:
    /// a small heading, small names.
    /// </summary>
    void DrawCard()
    {
        var s = Screen;
        float cx = s.X / 2;
        float top = Mathf.Floor(_cardTop), room = s.Y - 14 - top;
        bool compact = room < 82;
        // Two-size letters while they fit; the track card's four names go down to the small ones.
        int scale = compact ? 1 : 2, pad = compact ? 28 : 40;
        float Opts() { float o = 0; foreach (var n in CardOptions) o += PixelFont.Measure(n, scale) + pad; return o; }
        float opts = Opts();
        if (opts > s.X - 48 && scale == 2) { scale = 1; pad = 28; opts = Opts(); }
        int titleScale = compact ? 1 : 2;
        float w = Math.Min(s.X - 8, Math.Max(Math.Max(PixelFont.Measure(CardTitle, titleScale), opts), PixelFont.Measure(CardSub ?? "")) + 40);
        float h = compact ? Math.Min(56, room) : 82;
        var r = new Rect2(Mathf.Floor(cx - w / 2), top, w, h);
        Panel(r, 0.94f);
        float ty = compact ? 5 : 9, sy = compact ? 15 : 27, oy = compact ? 25 : 40, oh = compact ? 17 : 20;
        DrawHeading(CardTitle, cx, r.Position.Y + ty, _cardTextClock, titleScale);
        if (CardSub != null) PixelFont.DrawCentered(this, CardSub, cx, r.Position.Y + sy, Grey);
        float x = cx - opts / 2;
        for (int i = 0; i < CardOptions.Length; i++)
        {
            float ow = PixelFont.Measure(CardOptions[i], scale) + pad;
            bool sel = i == CardSelection, locked = CardLocked != null && i < CardLocked.Length && CardLocked[i];
            if (sel) DrawRect(new Rect2(x + 4, r.Position.Y + oy, ow - 8, oh), locked ? new Color(0.3f, 0.3f, 0.3f, 0.8f) : new Color(Red, 0.7f));
            Color c = locked ? (sel ? new Color(0.88f, 0.88f, 0.88f) : new Color(0.42f, 0.42f, 0.42f)) : sel ? Gold : Grey;
            PixelFont.DrawCentered(this, CardOptions[i], x + ow / 2, r.Position.Y + oy + (scale == 2 ? 3 : Mathf.Floor((oh - 7) / 2f)), c, scale);
            x += ow;
        }
        DrawLapsReward(r, oy, compact);
        if (LapsRewardSource != null) return;
        string keys = CardTour ? P("< > CHOOSE   ENTER OK   T TOUR   ESC BACK", "< > CHOOSE   A OK   Y TOUR   B BACK")
            : P("< > CHOOSE   ENTER OK   ESC BACK", "< > CHOOSE   A OK   B BACK");
        float keysY = r.End.Y - (compact ? 11 : 13);
        PixelFont.DrawCentered(this, keys, cx, keysY, Grey);
        if (CardTour)
        {
            string tour = P("T TOUR", "Y TOUR");
            float tourX = Mathf.Floor(cx - PixelFont.Measure(keys) / 2f)
                + keys.IndexOf(tour, StringComparison.Ordinal) * PixelFont.Advance;
            PixelFont.Draw(this, tour, new Vector2(tourX, keysY), Gold);
        }
    }

    // ------------------------------------------------------------ the instructor

    // The KGB supervisor from the start line, head and shoulders: C cap, R band, G gold, V visor,
    // K skin, k shade, E eyes, M moustache, U uniform, T collar tabs.
    static readonly string[] Officer =
    {
        "...CCCCCCCCCC...",
        "..CCCCCCCCCCCC..",
        ".CCCCCCGGCCCCCC.",
        ".CCCCCCGGCCCCCC.",
        ".RRRRRRRRRRRRRR.",
        "VVVVVVVVVVVVVVVV",
        "...KKKKKKKKKK...",
        "...KEEKKKKEEK...",
        "...KKKKKKKKKK...",
        "...KKKKkkKKKK...",
        "...KMMMMMMMMK...",
        "...KKKKKKKKKK...",
        "....KKKKKKKK....",
        "......KKKK......",
        "..UUTUUUUUUTUU..",
        ".UUUTUUUUUUTUUU.",
        "UUUUUUUGUUUUUUUU",
    };

    /// <summary>The instructor's head (32x34), top-left at at; a speech bubble to his right when he says something.</summary>
    void Instructor(Vector2 at, string says, int scale = 2)
    {
        DrawSprite(Officer, at, scale, false, ch => ch switch
        {
            'C' => new Color(0.3f, 0.36f, 0.22f), 'R' => new Color(0.75f, 0.12f, 0.08f), 'G' => Gold, 'V' => new Color(0.08f, 0.07f, 0.06f),
            'K' => new Color(0.9f, 0.7f, 0.6f), 'k' => new Color(0.78f, 0.55f, 0.47f), 'E' => new Color(0.12f, 0.06f, 0.05f),
            'M' => new Color(0.3f, 0.18f, 0.1f), 'U' => new Color(0.33f, 0.38f, 0.24f), 'T' => new Color(0.75f, 0.12f, 0.08f),
            _ => null,
        });
        if (says == null) return;
        float w = PixelFont.Measure(says) + 8;
        var b = new Rect2(Mathf.Floor(at.X + 30), Mathf.Floor(at.Y - 14), w, 12);
        var paper = new Color(1f, 0.97f, 0.88f);
        DrawRect(b, paper);
        DrawRect(b, new Color(0.15f, 0.05f, 0.05f), false, 1f);
        for (int k = 0; k < 4; k++) DrawRect(new Rect2(b.Position.X + 3 + k, b.End.Y + k, 4 - k, 1), paper);
        PixelFont.Draw(this, says, b.Position + new Vector2(4, 3), new Color(0.75f, 0.08f, 0.05f), 1, new Color(0, 0, 0, 0));
    }

    // ------------------------------------------------------------ keycaps and pad buttons

    const float KeyH = 28;

    const int ArrowUp = 1, ArrowDown = 2, ArrowLeft = 4, ArrowRight = 8;

    static int ArrowMask(SchoolKey k) => k switch
    {
        SchoolKey.Drive => 15, SchoolKey.Gas => ArrowUp, SchoolKey.Brake => ArrowDown, SchoolKey.Steer => ArrowLeft | ArrowRight, _ => 0,
    };

    /// <summary>What to draw: on the keys, all the arrow keys asked for share one cluster (brake + steer: down, left and right lit).</summary>
    List<(SchoolKey key, int arrows)> KeyItems(SchoolKey[] keys)
    {
        var items = new List<(SchoolKey, int)>();
        int at = -1;
        foreach (var k in keys)
        {
            int m = Pad ? 0 : ArrowMask(k);
            if (m == 0) { items.Add((k, 0)); continue; }
            if (at < 0) { at = items.Count; items.Add((k, m)); }
            else items[at] = (items[at].Item1, items[at].Item2 | m);
        }
        return items;
    }

    float KeyRowWidth(SchoolKey[] keys)
    {
        var items = KeyItems(keys);
        if (items.Count == 0) return 0;
        float w = 0;
        foreach (var (k, _) in items) w += KeyWidth(k);
        return w + (items.Count - 1) * 16;
    }

    /// <summary>The controls side by side with a + between them, centred on cx, top at y (28 high).</summary>
    void DrawKeyRow(SchoolKey[] keys, float cx, float y)
    {
        float x = Mathf.Floor(cx - KeyRowWidth(keys) / 2);
        var items = KeyItems(keys);
        for (int i = 0; i < items.Count; i++)
        {
            if (i > 0)
            {
                PixelFont.Draw(this, "+", new Vector2(x + 5, y + KeyH / 2 - 4), Gold);
                x += 16;
            }
            var (k, m) = items[i];
            if (m != 0) Arrows(new Vector2(x, y), (m & ArrowUp) != 0, (m & ArrowDown) != 0, (m & ArrowLeft) != 0, (m & ArrowRight) != 0);
            else DrawControl(k, new Vector2(x, y));
            x += KeyWidth(k);
        }
    }

    float KeyWidth(SchoolKey k) => Pad
        ? k switch
        {
            SchoolKey.Drive => 30 + 4 + 20 + 4 + 20, SchoolKey.Gas or SchoolKey.Brake => 20, SchoolKey.Steer => 30, SchoolKey.Clutch => 26,
            _ => 28,
        }
        : k switch
        {
            SchoolKey.Handbrake => 60, SchoolKey.Clutch => 36, SchoolKey.Horn => 30, SchoolKey.Rescue => 16,
            _ => 46,
        };

    void DrawControl(SchoolKey k, Vector2 at)
    {
        float mid = at.Y + KeyH / 2;
        if (!Pad)
        {
            switch (k)
            {
                case SchoolKey.Drive: Arrows(at, true, true, true, true); break;
                case SchoolKey.Gas: Arrows(at, true, false, false, false); break;
                case SchoolKey.Brake: Arrows(at, false, true, false, false); break;
                case SchoolKey.Steer: Arrows(at, false, false, true, true); break;
                case SchoolKey.Handbrake: KeyCap(new Rect2(at.X, mid - 7, 60, 14), "SPACE", true); break;
                case SchoolKey.Clutch: KeyCap(new Rect2(at.X, mid - 7, 36, 14), "SHIFT", true); break;
                case SchoolKey.Horn: KeyCap(new Rect2(at.X, mid - 7, 30, 14), "CTRL", true); break;
                case SchoolKey.Rescue: KeyCap(new Rect2(at.X, mid - 7, 16, 14), "R", true); break;
            }
            return;
        }
        switch (k)
        {
            case SchoolKey.Drive:
                Stick(at);
                Trigger(new Vector2(at.X + 34, at.Y), "RT", true);
                Trigger(new Vector2(at.X + 58, at.Y), "LT", true);
                break;
            case SchoolKey.Gas: Trigger(at, "RT", true); break;
            case SchoolKey.Brake: Trigger(at, "LT", true); break;
            case SchoolKey.Steer: Stick(at); break;
            case SchoolKey.Handbrake: FaceButtons(at, 'A'); break;
            case SchoolKey.Horn: FaceButtons(at, 'Y'); break;
            case SchoolKey.Rescue: FaceButtons(at, 'B'); break;
            case SchoolKey.Clutch: Bumper(new Vector2(at.X, mid - 5), "LB"); break;
        }
    }

    /// <summary>A road marking's words: a dark plate edged in its colour, filled with it while the car is on the strip.</summary>
    void MarkLabel(Vector2 p, string text, Color col, bool lit)
    {
        if (!float.IsFinite(p.X) || !float.IsFinite(p.Y)) return;
        float w = PixelFont.Measure(text) + 10;
        var r = new Rect2(Mathf.Floor(p.X - w / 2), Mathf.Floor(p.Y - 8), w, 15);
        DrawRect(r, lit ? new Color(col * 0.55f, 0.95f) : new Color(0.05f, 0.04f, 0.04f, 0.8f));
        DrawRect(r, col, false, lit ? 2f : 1f);
        PixelFont.DrawCentered(this, text, p.X, r.Position.Y + 4, lit ? Paper : col);
    }

    /// <summary>The car's LEFT or RIGHT beside it: a keycap, gold while that way is steered.</summary>
    void SideTag(Vector2 p, string label, bool lit)
    {
        if (!float.IsFinite(p.X) || !float.IsFinite(p.Y)) return;
        float w = PixelFont.Measure(label) + 8;
        KeyCap(new Rect2(p.X - w / 2, p.Y - 7, w, 14), label, lit);
    }

    /// <summary>A keycap: a dark top with a light rim and its label, gold when it is the one to press.</summary>
    void KeyCap(Rect2 r, string label, bool lit)
    {
        r = new Rect2(r.Position.Floor(), r.Size);
        DrawRect(new Rect2(r.Position.X, r.Position.Y + 2, r.Size.X, r.Size.Y), CapEdge);          // its side, under it
        DrawRect(r, lit ? CapLit : Cap);
        DrawRect(r, lit ? Gold : new Color(Paper, 0.6f), false, 1f);
        if (label.Length > 0) PixelFont.DrawCentered(this, label, r.Position.X + r.Size.X / 2, r.Position.Y + 3, lit ? Gold : Paper);
    }

    /// <summary>The four arrow keys in their inverted T; the ones to press lit, each with a pixel arrow.</summary>
    void Arrows(Vector2 at, bool up, bool down, bool left, bool right)
    {
        const float W = 14, H = 12;
        var u = new Rect2(at.X + 16, at.Y, W, H);
        var l = new Rect2(at.X, at.Y + 15, W, H);
        var d = new Rect2(at.X + 16, at.Y + 15, W, H);
        var r = new Rect2(at.X + 32, at.Y + 15, W, H);
        foreach (var (rect, lit, dir) in new[] { (u, up, 0), (l, left, 3), (d, down, 2), (r, right, 1) })
        {
            KeyCap(rect, "", lit);
            Color c = lit ? Gold : Paper;
            float cx = Mathf.Floor(rect.Position.X + W / 2), cy = Mathf.Floor(rect.Position.Y + H / 2);
            for (int k = 0; k < 4; k++)
                switch (dir)
                {
                    case 0: DrawRect(new Rect2(cx - k, cy - 2 + k, 2 * k + 1, 1), c); break;
                    case 2: DrawRect(new Rect2(cx - k, cy + 1 - k, 2 * k + 1, 1), c); break;
                    case 3: DrawRect(new Rect2(cx - 2 + k, cy - k, 1, 2 * k + 1), c); break;
                    case 1: DrawRect(new Rect2(cx + 1 - k, cy - k, 1, 2 * k + 1), c); break;
                }
        }
    }

    /// <summary>A, B, X, Y in their diamond; the one to press big and in its colour, the others dim.</summary>
    void FaceButtons(Vector2 at, char lit)
    {
        Vector2 c = at + new Vector2(14, 14);
        foreach (var (ch, off, col) in new[] { ('Y', new Vector2(0, -9), PadY), ('X', new Vector2(-9, 0), PadX), ('B', new Vector2(9, 0), PadB), ('A', new Vector2(0, 9), PadA) })
        {
            bool on = ch == lit;
            DrawCircle(c + off, on ? 6.5f : 4.5f, CapEdge);
            DrawCircle(c + off, on ? 5.5f : 3.5f, on ? col : Dim);
            if (on) PixelFont.Draw(this, ch.ToString(), (c + off - new Vector2(2, 3)).Floor(), CapEdge, 1, new Color(0, 0, 0, 0));
        }
    }

    /// <summary>A trigger (RT, LT): a tall rounded pad with its name.</summary>
    void Trigger(Vector2 at, string label, bool lit)
    {
        var r = new Rect2(at.X, at.Y + 6, 20, 20);
        DrawCircle(new Vector2(at.X + 10, at.Y + 8), 10.5f, CapEdge);
        DrawCircle(new Vector2(at.X + 10, at.Y + 8), 9.5f, lit ? CapLit : Cap);
        DrawRect(new Rect2(r.Position.X, r.Position.Y + 2, r.Size.X, r.Size.Y - 2), lit ? CapLit : Cap);
        DrawRect(new Rect2(r.Position.X, r.Position.Y + 2, r.Size.X, r.Size.Y - 2), lit ? Gold : new Color(Paper, 0.6f), false, 1f);
        PixelFont.DrawCentered(this, label, at.X + 10, at.Y + 12, lit ? Gold : Paper);
    }

    /// <summary>A shoulder button (LB): long and flat.</summary>
    void Bumper(Vector2 at, string label) => KeyCap(new Rect2(at.X, at.Y, 26, 11), label, true);

    /// <summary>The left stick, with arrows either side: steer.</summary>
    void Stick(Vector2 at)
    {
        Vector2 c = at + new Vector2(15, 14);
        DrawCircle(c, 11f, CapEdge);
        DrawCircle(c, 10f, Cap);
        DrawCircle(c, 6f, CapLit);
        DrawCircle(c, 4f, Gold);
        for (int k = 0; k < 3; k++)
        {
            DrawRect(new Rect2(c.X - 14 + k, c.Y - k, 1, 2 * k + 1), Gold);
            DrawRect(new Rect2(c.X + 14 - k, c.Y - k, 1, 2 * k + 1), Gold);
        }
    }
}
