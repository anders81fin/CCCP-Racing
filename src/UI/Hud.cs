using System;
using System.Collections.Generic;
using Godot;

namespace CCCPRacing.UI;

/// <summary>
/// Watch: replay focus, the record lap replay with only the blinking replay line over it. Board:
/// the leaderboard over the title replay (the driver's name is asked on the title before a run:
/// DrawNameAsk). Gulag: the game-over screen after a failed mission (HudGulag.cs). Plan: the PLAN
/// FULFILLED screen (HudPlan.cs). Changelog: the version history over the title replay
/// (HudChangelog.cs). Keys: the race HUD with the key card (HudKeys.cs). Intro: the TEST VERSION
/// card with the changelog at launch (HudIntro.cs). Lan: the LAN RACE screens (HudLan.cs), over
/// the title or, for the results, over the race.
/// </summary>
public enum HudMode { Title, Countdown, Race, Paused, Watch, Board, Gulag, Plan, Changelog, Keys, Intro, Lan, Card, Files, Lessons }

/// <summary>All 2D overlay drawing, in low-res viewport pixels with the bitmap font.</summary>
public partial class Hud : Control
{
    public static readonly Color Red = new(0.86f, 0.12f, 0.08f);
    public static readonly Color Gold = new(1f, 0.8f, 0.2f);
    public static readonly Color Paper = new(0.93f, 0.9f, 0.8f);
    public static readonly Color Grey = new(0.6f, 0.6f, 0.58f);
    public static readonly Color Green = new(0.5f, 0.8f, 0.4f);

    /// <summary>The two runs on the title menu (short forms for narrow windows); in-race texts say TrainingWord.</summary>
    public const string RaceLabel = "SOCIALIST RALLY", RaceShort = "RALLY";
    public const string TrainingLabel = "STAKHANOV LAPS", TrainingShort = "STAKHANOV", TrainingWord = "STAKHANOV";

    public HudMode Mode = HudMode.Title;
    /// <summary>One-off announcement on the title screen (records archived), null for none.</summary>
    public string TitleNotice;
    public bool TitleReplay;
    string _radioText;
    float _radioTime;
    public string TrackName = "";
    public float Countdown;
    public float LapTime, BestLap = -1, LastLap = -1;
    /// <summary>The Socialist Rally's top-left BEST is this session's best lap (-1 until a valid lap); BestLap stays the leaderboard record (title record line, record holder).</summary>
    public float SessionBestLap = -1;
    public bool BestIsSession;
    public bool LapVoid, LastLapVoid;
    public int Lap = 1;
    /// <summary>Training laps run: the race shows the power line.</summary>
    public bool Training;
    /// <summary>Title menu selection, an index into the row (-1: none).</summary>
    public int MenuSelection;
    /// <summary>The title menu's open row: its labels (short forms for narrow windows), the parent's
    /// name over it (null: the top row) and an item drawn dim (not ready yet, -1: none).</summary>
    public string[] MenuLabels = Array.Empty<string>(), MenuShort = Array.Empty<string>();
    public string MenuHeader;
    public int MenuDim = -1;
    /// <summary>The selected mode in a mode row: what it is, in the middle of the title (null: none).</summary>
    public (string title, string[] lines)? MenuHint;
    /// <summary>The built track's name for the title's record line.</summary>
    public string RecordTrack = "KOLKHOZ";
    /// <summary>Leaderboard entries, fastest first; BoardHighlight is the one added this session (-1: none).</summary>
    public IReadOnlyList<Race.Leaderboard.Entry> Board = Array.Empty<Race.Leaderboard.Entry>();
    public int BoardHighlight = -1;
    public int BoardSelected = -1;
    public string BoardReplayNotice = "";
    public record FuelBoard(string Fuel, IReadOnlyList<Race.Leaderboard.Entry> Entries, int Highlight, string Speed);
    public FuelBoard[] BoardFuels = Array.Empty<FuelBoard>();
    /// <summary>Board is the STAKHANOV LAPS one (a power column), else SOCIALIST RALLY.</summary>
    public bool BoardStakhanov;
    /// <summary>CHOOSE TRACK's read-only boards: one track and mode, no selection, replay or track keys (Stakhanov: one fuel's group, Left/Right the fuel).</summary>
    public bool BoardScoped;
    /// <summary>The rally board's fuel (PETROL, DIESEL, JET FUEL; empty on the Stakhanov board).</summary>
    public string BoardFuel = "";
    /// <summary>The GAI's record on the board's track and run ("GAI 128 KM/H"), or "".</summary>
    public string BoardSpeed = "";
    /// <summary>The board's track name (the title's LEADERBOARD: Up/Down shows the other tracks), or "" (the school's mock board).</summary>
    public string BoardTrack = "";
    /// <summary>Name of the record holder for the title's record line (null: unknown).</summary>
    public string RecordHolder;
    /// <summary>Name entry: the text typed so far, the lap and the place it takes.</summary>
    public string EntryName = "";
    /// <summary>The title asks the driver's name for this mode (YOUR NAME, TOVARISH?) in EntryName.</summary>
    public bool NameAsk;
    /// <summary>The name question's second line: what the name is for.</summary>
    public string NameAskSub = "";
    public float PowerScale = 1f;
    /// <summary>Stakhanov laps: the fuel name shown instead of POWER n % (empty: the percent).</summary>
    public string PowerLabel = "";
    public float SpeedKmh;
    public int Gear = 1;
    public float RpmFrac;
    /// <summary>Clutch pedal down: a small label over the speedo.</summary>
    public bool Clutch;
    /// <summary>Driving the course backwards: WRONG WAY! blinks over the race until it is put right.</summary>
    public bool WrongWay;
    public bool ShowDebug;
    /// <summary>Trailer capture (--clean-hud): no panels, title text or messages; speech bubbles,
    /// the lens splat and the full-screen pages (gulag, leaderboard) stay.</summary>
    public bool Clean;
    public readonly List<string> DebugLines = new();
    public float[] WheelLoads = new float[4];
    /// <summary>Speech bubbles anchored to screen points (babushkas yelling).</summary>
    public readonly List<(Vector2 pos, string text)> Bubbles = new();

    float _clock;
    Audio.TypewriterSound _typewriterSound;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        TextureFilter = TextureFilterEnum.Nearest;
        AddChild(_typewriterSound = new Audio.TypewriterSound());
        AddChild(_medalCheer = new Audio.MedalCheer());
        AddChild(_normCheer = new Audio.MedalCheer { Name = "NormCheer", Brief = true });
        AddChild(_normMissSound = new Audio.NormMissSound { Name = "NormMiss" });
    }

    /// <summary>Car radio: station/song title in a little panel at the top for a few seconds.</summary>
    public void ShowRadio(string text)
    {
        _radioText = text;
        _radioTime = 4f;
    }

    /// <summary>Comments (TAB): the jokes (Prio.Nice, HudMessages.cs). Off, they are dropped; everything that matters still shows.</summary>
    public bool Comments { get; private set; } = true;

    /// <summary>TAB: comments on or off; the confirmation shows either way.</summary>
    public void SetComments(bool on, bool confirm)
    {
        if (confirm) ShowAlways(on ? "COMMENTS ON" : "COMMENTS OFF", null, 1.5f, on ? Gold : Paper);
        Comments = on;
    }

    public override void _Process(double delta)
    {
        TickMenuText((float)delta);
        TickStartupBanner((float)delta);
        TickMedal((float)delta);
        TickNormFeedback((float)delta);
        TickGaugeWarnings((float)delta);
        PlaceMessage((float)delta);
        _clock += (float)delta;
        _radioTime -= (float)delta;
        if (!_lensTape) _lensTime += (float)delta;
        TickMessages((float)delta);
        QueueRedraw();
    }

    Vector2 Screen => GetViewportRect().Size;

    public override void _Draw()
    {
        _typewriterSound.BeginFrame();
        DrawHud();
        string context = LoadingTrack ? $"loading:{LoadingTrackReady}"
            : TrackPreview && PreviewCardAlpha > 0 && !PreviewMovie && !Clean ? $"tour:{_previewCardHeadingSeen}"
            : Clean || TrackPreview || StartupArchivePending ? null : Mode switch
        {
            HudMode.Title => $"title:{MenuHeader}:{MenuSelection}:{CardTitle}:{(CardTitle == null ? -1 : CardSelection)}:{NameAsk}:{ReplayListOpen}",
            HudMode.Files => $"files:{FilesSelection}:{FilesErase}:{NameAsk}",
            HudMode.Lessons => $"lessons:{LessonSelection}:{Lessons.Length}",
            HudMode.Board => $"board:{BoardTrack}:{BoardStakhanov}:{BoardFuel}",
            HudMode.Paused => "party-meeting",
            HudMode.Keys => "controls",
            _ => null,
        };
        _typewriterSound.EndFrame(context);
    }

    void DrawHud()
    {
        if (LoadingTrack) { DrawLoadingTrack(); return; }
        // The launch replay gets an uninterrupted video-only lead-in after the cover closes.
        if (StartupVideoOnly) return;
        if (TrackPreview) { DrawTrackPreview(); return; }
        if (Mode == HudMode.Watch)
        {
            if (!Clean) DrawTitle(focus: true);
            if (_lensTape && _lensTime < _lensDuration) DrawLensSplat();
            return;
        }
        if (!Clean && !School && Mode is HudMode.Race or HudMode.Paused) DrawLensFx();      // HudLens.cs: summer on the glass, under the panels
        switch (Mode)
        {
            case HudMode.Title: if (!Clean) DrawTitle(); break;
            case HudMode.Countdown: if (!Clean) { DrawRace(); DrawCountdown(); } break;
            case HudMode.Race: if (!Clean) DrawRace(); break;
            case HudMode.Paused: DrawRace(); break;
            case HudMode.Board: DrawBoard(); break;
            case HudMode.Files: DrawFiles(); break;
            case HudMode.Lessons: break;
            case HudMode.Gulag: DrawGulag(); break;
            case HudMode.Plan: DrawPlanFulfilled(); break;
            case HudMode.Changelog: DrawChangelog(); break;
            case HudMode.Intro: DrawIntro(); break;
            case HudMode.Keys: DrawRace(); break;
            case HudMode.Card: DrawRace(); break;
            case HudMode.Lan: DrawLan(); break;
        }
        bool inCar = Mode is HudMode.Countdown or HudMode.Race or HudMode.Paused or HudMode.Keys or HudMode.Card;
        if (inCar) DrawBubbles();
        if (inCar && _radioTime > 0 && _radioText != null && !Clean) DrawRadio();
        if ((inCar || _lensTape) && _lensTime < _lensDuration) DrawLensSplat();
        if (!Clean && inCar) DrawMessages();
        if (inCar)
        {
            DrawNormConfetti();
            DrawNormMiss();
            DrawMedalCelebration();
        }
        if (Mode == HudMode.Card && StopCard is var (title, lines, color)) DrawStopCard(title, lines, color);
        if (!Clean && School && Mode is HudMode.Race or HudMode.Countdown) DrawSchool();
        if (CardTitle != null && Mode == HudMode.Title) DrawCard();
        if (NameAsk && Mode is HudMode.Title or HudMode.Files) DrawNameAsk();
        if (ShowDebug) DrawDebug();
        if (Mode == HudMode.Keys) DrawKeys();
        if (Mode == HudMode.Paused) { if (LabOpen) { } else if (StakhanovBoardOpen) DrawStakhanovBoard(false); else if (RallyExitOpen) DrawRallyExit(); else if (PauseNormsOpen) DrawPauseNorms(); else if (VisualOpen) DrawVisual(); else if (MixerOpen) DrawMixer(); else if (SettingsOpen) DrawSettings(); else if (ReplayListOpen) DrawReplayList(); else DrawPause(); }
        if (LabRun && inCar && Mode != HudMode.Keys) DrawLabTag();
        if (LabOpen) DrawLab();
        if (Mode == HudMode.Title && ReplayListOpen) DrawReplayList();      // RECORDINGS in the Party Office (HudReplay.cs)
        if (KeepNameOpen && ReplayListOpen) DrawKeepName();
        if (ReplayDeleteOpen && ReplayListOpen) DrawReplayDelete();
        if (Mode == HudMode.Lessons) DrawLessons();
        DrawCapitalistLuxuriesNotice();
        DrawLanKeep();                    // HudLanKeep.cs: KEEP after a LAN race, under the leave question
        DrawLanLeave();
        DrawResign();                     // HudResign.cs: over everything, the black screen too
    }

    void Panel(Rect2 r, float alpha = 0.55f)
    {
        DrawRect(r, new Color(0.05f, 0.03f, 0.03f, alpha));
        DrawRect(r, new Color(Red, 0.9f), false, 1f);
    }

    void DrawStar(Vector2 c, int scale, Color col)
    {
        PixelFont.Draw(this, "*", c - new Vector2(2.5f * scale, 3.5f * scale), col, scale);
    }

    /// <summary>
    /// The title over the replay. In replay focus (focus) only the replay line is drawn, blinking,
    /// in the same place; otherwise it is a menu target: bright with markers when selected.
    /// </summary>
    /// <summary>The title banner's top (under its gold line), near the top of the screen: the middle stays free.</summary>
    const float BannerTop = 12;

    void DrawTitleBanner()
    {
        var s = Screen;
        float cx = s.X / 2;
        // Whole pixels: the band, its gold lines, the letters and the stars step down together.
        float by = BannerTop + Mathf.Round(StartupBannerOffset);
        // Red banner.
        DrawRect(new Rect2(0, by, s.X, 52), new Color(Red, 0.92f));
        DrawRect(new Rect2(0, by + 52, s.X, 2), Gold);
        DrawRect(new Rect2(0, by - 2, s.X, 2), Gold);
        string title = PixelFont.Faux("CCCP RACING");
        int scale = s.X >= 420 ? 5 : 4;
        PixelFont.DrawCentered(this, title, cx, by + 26 - 3.5f * scale, Gold, scale, new Color(0.3f, 0, 0));
        float tw = PixelFont.Measure(title, scale);
        DrawStar(new Vector2(cx - tw / 2 - 20, by + 26), 3, Gold);
        DrawStar(new Vector2(cx + tw / 2 + 20, by + 26), 3, Gold);

    }

    void DrawTitle(bool focus = false)
    {
        var s = Screen;
        float cx = s.X / 2;
        float by = BannerTop;
        // Between the banner and the menu is the replay car's space: over the record lap its centre
        // stays within 39-64 % of the screen height (the camera leads it), so the menu sits just
        // below that and the record line goes under the menu.
        float my = Mathf.Floor(s.Y * 0.64f + 14);
        float ry = my + 17;                 // record line
        if (focus)
        {
            if (TitleReplay && BestLap > 0 && (int)(_clock * 1.5f) % 2 == 0)
                PixelFont.DrawCentered(this, "> REPLAY OF THE RECORD LAP <", cx, ry + 11, Gold);
            return;
        }
        DrawRect(new Rect2(0, 0, s.X, s.Y), new Color(0, 0, 0, 0.25f));

        DrawTitleBanner();

        // A one-off announcement (records archived) right under the banner.
        if (TitleNotice != null)
        {
            float nw = PixelFont.Measure(TitleNotice);
            float ny = by + 62;
            DrawRect(new Rect2(Mathf.Floor(cx - nw / 2 - 6), ny - 3, Mathf.Ceil(nw) + 12, 13), new Color(Red, 0.92f));
            PixelFont.DrawCentered(this, TitleNotice, cx, ny, (int)(_clock * 2) % 2 == 0 ? Gold : Paper);
        }

        // A card before a run (the track, the Stakhanov laps' time and light): the mode's hint card
        // stays, the card goes under it; the menu row, the record and replay lines and notes give way.
        if (CardTitle != null)
        {
            _cardTop = by + 58;
            // Exactly where it stood over the mode row (the row's header line above it).
            float cardHh = MenuHeader == null ? 0 : 11;
            if (MenuHint is var (cardHintTitle, cardHintLines)) _cardTop = DrawMenuHint(cardHintTitle, cardHintLines, cx, by + 58, my - cardHh - 8, typed: false) + 4;   // the card is the active one: only it types
            DrawCredits(s, cx);
            return;
        }

        // Menu: the open row (SINGLE PLAYER | MULTIPLAYER | LEADERBOARD | CHANGELOG, or a mode row), compact.
        // Slots are as wide as the label; the gaps and the ends hold the selection markers, so nothing
        // shifts. The gap narrows before narrow windows get the short labels. A mode row has its
        // parent's name (and the way back) on a line of the panel above it.
        const float Mark = 2 * PixelFont.Advance;    // "> " and " <" on either side of the selected label
        string[] items = null;
        float total = 0, gap = 0;
        foreach (var labels in new[] { MenuLabels, MenuShort })
        {
            items = labels;
            foreach (float g in new[] { Mark + 8, Mark + 3 })
            {
                gap = g;
                total = 2 * Mark - gap;
                foreach (var item in items) total += PixelFont.Measure(item) + gap;
                if (total + 24 <= s.X) break;
            }
            if (total + 24 <= s.X) break;
        }
        string header = MenuHeader == null ? null : MenuHeader + "  -  " + P("ESC", "B") + " BACK";
        float pw = Math.Max(total, header == null ? 0 : PixelFont.Measure(header));
        float hh = header == null ? 0 : 11;
        Panel(new Rect2(Mathf.Floor(cx - pw / 2 - 8), my - 4 - hh, Mathf.Ceil(pw) + 16, 15 + hh), 0.6f);
        if (header != null) PixelFont.DrawCentered(this, header, cx, my - hh, Grey);
        float x = cx - total / 2 + Mark;
        for (int i = 0; i < items.Length; i++)
        {
            float w = PixelFont.Measure(items[i]);
            MenuItem(items[i], MenuSelection == i, x + w / 2, my, i == MenuDim);
            x += w + gap;
        }
        // A note: in the middle between the banner and the menu, or just over the row when a hint card has the middle.
        DrawMenuNote(cx, MenuHint == null ? Mathf.Floor((by + 56 + my - 4 - hh) / 2 - 12) : my - hh - 36);
        if (MenuHint is var (hintTitle, hintLines)) DrawMenuHint(hintTitle, hintLines, cx, by + 58, my - hh - 8);

        DrawCredits(s, cx);
    }

    /// <summary>The title's bottom line: the file in use and the version (the credits are in PARTY OFFICE).</summary>
    void DrawCredits(Vector2 s, float cx)
    {
        DrawRect(new Rect2(0, s.Y - 13, s.X, 13), new Color(0.03f, 0.02f, 0.02f, 0.6f));   // keeps the small print readable
        string version = "V. " + ((string)ProjectSettings.GetSetting("application/config/version", "0.1"));
        PixelFont.Draw(this, version, new Vector2(s.X - PixelFont.Measure(version) - 4, s.Y - 10), Gold);     // the banner has the top
        // Whose personnel file is in use, cut short before the version in a narrow window.
        string file = FileLabel;
        float room = s.X - PixelFont.Measure(version) - 16;
        while (file.Length > 0 && PixelFont.Measure(file) > room) file = file[..^1];
        if (file.Length > 0) PixelFont.Draw(this, file, new Vector2(4, s.Y - 10), Grey);
    }

    string _noteText, _noteSub;
    float _noteUntil = -1;

    /// <summary>A few words over the title menu (an item that is not ready yet).</summary>
    public void MenuNote(string text, string sub, float duration)
    {
        _noteText = text;
        _noteSub = sub;
        _noteUntil = _clock + duration;
    }

    /// <summary>The hint box between the banner (top) and the menu (bottom): the mode's name and a few lines.</summary>
    /// <summary>The selected mode's hint card, centred between top and bottom (not above top): its bottom edge.</summary>
    float DrawMenuHint(string title, string[] lines, float cx, float top, float bottom, bool typed = true)
    {
        // Menu cards leave one empty text row between the heading and the help.
        const int HelpTop = 34, RowPitch = 10;
        float w = PixelFont.Measure(title, 2);
        foreach (var l in lines) w = Math.Max(w, PixelFont.Measure(l));
        w += 20;
        float h = HelpTop + lines.Length * RowPitch + 2;
        float y = Mathf.Floor(Math.Max(top, (top + bottom) / 2 - h / 2));
        Panel(new Rect2(Mathf.Floor(cx - w / 2), y, Mathf.Ceil(w), h), 0.88f);
        // A new hint types out, unless another panel is the active one (a card under it): then it is just there.
        if (_hintTitle != title) { _hintTitle = title; _hintTextClock = typed ? 0 : 999f; }
        DrawHeading(title, cx, y + 5, _hintTextClock);
        for (int i = 0; i < lines.Length; i++) PixelFont.DrawCentered(this, lines[i], cx, y + HelpTop + i * RowPitch, Paper);
        return y + h;
    }

    void DrawMenuNote(float cx, float y)
    {
        if (_noteText == null || _clock > _noteUntil) return;
        float w = Math.Max(PixelFont.Measure(_noteText, 2), PixelFont.Measure(_noteSub)) + 16;
        Panel(new Rect2(Mathf.Floor(cx - w / 2), y - 4, Mathf.Ceil(w), 32), 0.85f);
        PixelFont.DrawCentered(this, _noteText, cx, y, Gold, 2, new Color(0.35f, 0, 0));
        PixelFont.DrawCentered(this, _noteSub, cx, y + 18, Paper);
    }

    /// <summary>
    /// The board of honour of one run (Left/Right shows the other): three places per fuel, the one added this
    /// session highlighted. Stakhanov rows have the power the lap was driven at.
    /// </summary>
    readonly List<(Rect2 Rect, int Row)> _boardHitRows = new();
    void DrawBoard()
    {
        _boardHitRows.Clear();
        var s = Screen;
        float cx = s.X / 2;
        DrawRect(new Rect2(0, 0, s.X, s.Y), new Color(0, 0, 0, 0.35f));
        // After the time a small keyboard or pad: how the lap was driven.
        const int RowH = 11, TimeX = 24 + 13 * 6 + 8, MarkX = TimeX + 7 * 6 + 4, PowerX = MarkX + InputMarkW + 8;
        int dateX = BoardStakhanov ? PowerX + 4 * 6 + 12 : PowerX + 13;    // the rally's gap: a moon (night) and a bolt (OCTANE BOOSTER)
        float rowW = dateX + 10 * 6;     // the rows, centred in a panel as wide for both boards
        bool fuels = (!BoardStakhanov || BoardScoped) && BoardFuels.Length > 0;      // (the scoped Stakhanov board: its one fuel as a group)
        int groups = fuels ? BoardFuels.Length : 1;
        int places = BoardStakhanov ? Race.Leaderboard.StakhanovSize : Race.Leaderboard.Size;
        float w = Math.Max(320, rowW + 16), h = 38 + groups * (places * RowH + (fuels ? 13 : 0)) + 40;
        var r = new Rect2(Mathf.Floor(cx - w / 2), Mathf.Floor(Math.Max(4, (s.Y - h) / 2 - 6)), w, h);
        Panel(r, 0.85f);
        DrawHeading("BOARD OF HONOUR", cx, r.Position.Y + 7, _menuTextClock);
        string run = BoardStakhanov ? TrainingLabel : RaceLabel;
        string sub = BoardTrack == "" ? run + " - TOP " + places : BoardTrack + " - " + run;
        PixelFont.DrawCentered(this, sub + (fuels || BoardSpeed == "" ? "" : " - " + BoardSpeed), cx, r.Position.Y + 25, BoardTrack == "" ? Grey : Paper);
        float y = r.Position.Y + 38;
        float x0 = Mathf.Floor(cx - rowW / 2);
        for (int group = 0; group < groups; group++)
        {
            var entries = fuels ? BoardFuels[group].Entries : Board;
            int highlight = fuels ? BoardFuels[group].Highlight : BoardHighlight;
            if (fuels)
            {
                PixelFont.Draw(this, BoardFuels[group].Fuel, new Vector2(x0, y), Gold);
                PixelFont.Draw(this, BoardFuels[group].Speed, new Vector2(x0 + TimeX, y), Grey);
                y += 13;
            }
            for (int i = 0; i < places; i++, y += RowH)
            {
                _boardHitRows.Add((new Rect2(r.Position.X + 3, y - 2, w - 6, RowH), group * places + i));
                bool selected = BoardSelected == group * places + i;
                bool hl = i == highlight && i < entries.Count;
                if (selected) DrawRect(new Rect2(r.Position.X + 3, y - 2, w - 6, RowH), new Color(Gold, .22f));
                if (selected) PixelFont.Draw(this, ">", new Vector2(x0 - 8, y), Gold);
                if (hl) DrawRect(new Rect2(r.Position.X + 4, y - 2, w - 8, RowH), new Color(Red, 0.55f));
                Color c = hl ? Gold : i == 0 && entries.Count > 0 ? Paper : i < entries.Count ? new Color(0.85f, 0.83f, 0.76f) : Grey;
                PixelFont.Draw(this, $"{i + 1}.".PadLeft(3), new Vector2(x0, y), c);
                if (i < entries.Count)
                {
                    var e = entries[i];
                    PixelFont.Draw(this, e.Name, new Vector2(x0 + 24, y), c);
                    PixelFont.Draw(this, Race.LapTimer.Format(e.Time), new Vector2(x0 + TimeX, y), c);
                    DrawInputMark(new Vector2(x0 + MarkX, y), e.Pad, hl ? Gold : e.Pad ? new Color(Paper, 0.8f) : new Color(Grey, 0.7f));
                    if (e.Night) DrawMoonMark(new Vector2(x0 + MarkX + InputMarkW + 4, y), hl ? Gold : Moon);
                    if (e.Boost) DrawMark(BoltMark, new Vector2(x0 + MarkX + InputMarkW + 11, y), hl ? Paper : Bolt);
                    if (BoardStakhanov)
                        PixelFont.Draw(this, Race.Training.FuelShort(e.Power).PadLeft(4), new Vector2(x0 + PowerX, y), c);
                    PixelFont.Draw(this, string.IsNullOrEmpty(e.Date) ? "" : e.Date, new Vector2(x0 + dateX, y), hl ? Gold : Grey);
                }
                else PixelFont.Draw(this, "-", new Vector2(x0 + 24, y), Grey);
            }
        }
        if (BoardScoped)
        {
            if (BoardStakhanov) PixelFont.DrawCentered(this, Pad ? "D-PAD < > FUEL" : "< > FUEL", cx, r.End.Y - 22, Gold);
            PixelFont.DrawCentered(this, Pad ? "B BACK" : "ESC BACK", cx, r.End.Y - 12, Grey);
            return;
        }
        PixelFont.DrawCentered(this, Pad ? "D-PAD SELECT/BOARD  LB/RB TRACK" : "< > BOARD   W/S TRACK   UP/DOWN SELECT", cx, r.End.Y - 32, Grey);
        PixelFont.DrawCentered(this, string.IsNullOrEmpty(BoardReplayNotice) ? "ENTER REPLAY" : BoardReplayNotice, cx, r.End.Y - 22, Gold);
        PixelFont.DrawCentered(this, Pad ? "B BACK" : "ESC BACK", cx, r.End.Y - 12, Grey);
    }

    public int BoardRowAt(Vector2 at)
    {
        foreach (var hit in _boardHitRows) if (hit.Rect.HasPoint(at)) return hit.Row;
        return -1;
    }

    /// <summary>The title's name question before a run: the board of honour's name for its laps.</summary>
    void DrawNameAsk()
    {
        var s = Screen;
        float cx = s.X / 2;
        string sub = NameAskSub;
        float w = Math.Max(260, Mathf.Ceil(PixelFont.Measure(sub)) + 20);
        var r = new Rect2(Mathf.Floor(cx - w / 2), Mathf.Floor(s.Y * 0.35f), w, 84);      // between the title band and the menu row
        Panel(r, 0.92f);
        DrawHeading("YOUR NAME, TOVARISH?", cx, r.Position.Y + 7, _nameTextClock);
        PixelFont.DrawCentered(this, sub, cx, r.Position.Y + 25, Grey);
        float fw = Race.Leaderboard.MaxName * 12 + 16;
        var f = new Rect2(Mathf.Floor(cx - fw / 2), r.Position.Y + 38, fw, 22);
        DrawRect(f, new Color(0, 0, 0, 0.6f));
        DrawRect(f, new Color(Gold, 0.8f), false, 1f);
        DrawTextField(EntryName, f, Race.Leaderboard.MaxName);
        PixelFont.DrawCentered(this, P("ENTER OK  BACKSPACE DELETE  ESC BACK", WheelKeys), cx, r.End.Y - 12, Grey);
    }

    static readonly Color Moon = new(0.72f, 0.8f, 1f);
    static readonly string[] MoonMark = { "..##.", ".##..", "##...", "##...", ".##..", "..##." };

    /// <summary>A small crescent (5x6), top-left at a text line's top: a lap driven after dark.</summary>
    void DrawMoonMark(Vector2 at, Color c) => DrawMark(MoonMark, at, c);

    /// <summary>A lightning bolt (5x6): OCTANE BOOSTER ran during the lap.</summary>
    static readonly string[] BoltMark = { "...##", "..##.", ".####", "..##.", ".##..", ".#..." };
    static readonly Color Bolt = new(1f, 0.8f, 0.2f);

    void DrawMark(string[] mark, Vector2 at, Color c)
    {
        at = new Vector2(Mathf.Floor(at.X), Mathf.Floor(at.Y) + 1);
        for (int r = 0; r < mark.Length; r++)
            for (int x = 0; x < mark[r].Length; x++)
                if (mark[r][x] == '#') DrawRect(new Rect2(at.X + x, at.Y + r, 1, 1), c);
    }

    void MenuItem(string text, bool selected, float cx, float y, bool dim = false)
    {
        if (selected) text = "> " + text + " <";
        Color c = selected ? Gold : Grey;
        PixelFont.DrawCentered(this, text, cx, y, dim ? new Color(c, 0.4f) : c);
    }

    void DrawRace()
    {
        if (SchoolScenic) return;
        var s = Screen;

        // Lap panel, top-left (a LAN race has its own).
        if (LanLaps > 0) DrawLanPanel();
        else if (!HideLapPanel && LapInfoOn)
        {
        Panel(new Rect2(4, 4, LapPanelWidth, LapPanelHeight));
        if (Training)
        {
            // Training laps: power level as text and four plan blocks of 50 % each (two = 100 %, full at 200 %).
            // Red below the plan, gold on it, green above it; the golden 200 % and more flashes.
            int pct = (int)MathF.Round(PowerScale * 100);
            Color c = pct < 100 ? Red : pct == 100 ? Gold : Race.Training.Hero(pct) ? ((int)(_clock * 4) % 2 == 0 ? Gold : Paper) : Green;
            PixelFont.Draw(this, PowerLabel != "" ? PowerLabel : $"POWER {pct}%", new Vector2(9, 45), c);
            for (int i = 0; i < 4; i++)
                DrawRect(new Rect2(80 + i * 6, 46, 4, 5), i < MathF.Round(PowerScale * 2) ? c : new Color(Grey, 0.25f));
        }
        float shift = PlanLaps > 0 ? PlanRowShift : 0;      // (the NORM row leads the panel)
        if (!DrawPlanLines()) PixelFont.Draw(this, $"LAP {Lap}", new Vector2(9, 8), Gold);
        // A void lap (R) swaps the label for a red VOID; the time keeps running.
        PixelFont.Draw(this, (LapVoid ? "VOID " : "TIME ") + Race.LapTimer.Format(LapTime), new Vector2(9, 18 + shift), LapVoid ? Red : Paper);
        PixelFont.Draw(this, "BEST " + Race.LapTimer.Format(BestIsSession ? SessionBestLap : BestLap), new Vector2(9, 27 + shift), Grey);
        PixelFont.Draw(this, "LAST " + (LastLapVoid ? "VOID" : Race.LapTimer.Format(LastLap)), new Vector2(9, 36 + shift), LastLapVoid ? Red : Grey);
        }

        DrawMinimap();     // bottom-left (HudMinimap.cs)

        // Top-right emblem.
        if (LanLaps > 0)
        {
            if (Lan.Page == LanPage.None) DrawLanStandings();
        }
        else if (NormCardShown) DrawNormCard();      // the Socialist Rally's target card carries the emblem in its header (HudNormCard.cs)
        else if (StakhanovMiniBoard) DrawStakhanovBoard(true);
        else
        {
            DrawStar(new Vector2(s.X - 12, 11), 1, Red);
            PixelFont.Draw(this, "CCCP", new Vector2(s.X - 44, 8), Red);
        }

        // Speedo, bottom-right.
        var r = new Rect2(s.X - 92, s.Y - 40, 88, 36);
        if (Clutch) PixelFont.Draw(this, "CLUTCH", new Vector2(r.End.X - PixelFont.Measure("CLUTCH") - 1, r.Position.Y - 9), Gold);
        if (CarInstrumentsOn || LanLaps > 0)
        {
        Panel(r);
        string spd = ((int)MathF.Round(SpeedKmh)).ToString().PadLeft(3);
        PixelFont.Draw(this, spd, new Vector2(r.Position.X + 5, r.Position.Y + 5), Paper, 2);
        PixelFont.Draw(this, "KM/H", new Vector2(r.Position.X + 43, r.Position.Y + 12), Grey);
        PixelFont.Draw(this, Gear == 0 ? "N" : Gear < 0 ? "R" : Gear.ToString(), new Vector2(r.Position.X + 72, r.Position.Y + 5), Gold, 2);
        // Tachometer: 14 segments, last three red.
        int segs = 14;
        int lit = (int)MathF.Round(Math.Clamp(RpmFrac, 0, 1) * segs);
        for (int i = 0; i < segs; i++)
        {
            Color c = i >= segs - 3 ? Red : i >= segs - 6 ? Gold : new Color(0.5f, 0.8f, 0.4f);
            if (i >= lit) c = new Color(c, 0.18f);
            DrawRect(new Rect2(r.Position.X + 5 + i * 5.7f, r.Position.Y + 26, 4, 5), c);
        }
        DrawEngineGauge(r);
        }
        else DrawHiddenGaugeWarnings(r);
        if (WrongWay && (int)(_clock * 2.5f) % 2 == 0)
        {
            float cx = Screen.X / 2f, y = Screen.Y * 0.22f;
            var box = new Rect2(cx - 92, y - 6, 184, 40);
            DrawRect(box, new Color(0, 0, 0, 0.7f));
            DrawRect(box, Red, false, 2f);
            PixelFont.DrawCentered(this, "WRONG WAY!", cx, y, Red, 3, new Color(0.25f, 0, 0));
            PixelFont.DrawCentered(this, "TURN AROUND, TOVARISH", cx, y + 24, Paper);
        }
        if (CarInstrumentsOn || LanLaps > 0) { DrawFuelGauge(r); DrawDoseGauge(r); DrawWarningLights(); }
        DrawLanTags();
        DrawRevenge();
        DrawWreckCountdown();
    }

    public bool StartRevving;
    public bool StartClutchPrompt, FalseStart;
    public bool StartClutchWaiting;
    public bool LanGridWaiting;
    public int LanGridReady;

    void DrawCountdown()
    {
        if (StartRevving) return;
        var s = Screen;
        if (StartClutchWaiting)
        {
            DrawRect(new Rect2(Vector2.Zero, s), new Color(0, 0, 0, 0.55f));
            float w = Math.Min(s.X - 16, 344), y = MathF.Floor(s.Y / 2 - 46);
            Panel(new Rect2(MathF.Floor((s.X - w) / 2), y, w, 92), 0.98f);
            PixelFont.DrawCentered(this, "CLUTCH START", s.X / 2, y + 9, Gold, 2);
            PixelFont.DrawCentered(this, "PRESS AND HOLD CLUTCH - " + P("SHIFT", "LB"), s.X / 2, y + 32, Paper);
            PixelFont.DrawCentered(this, "KEEP IT HELD. REV UP - " + P("UP / W", "RT"), s.X / 2, y + 45, Paper);
            PixelFont.DrawCentered(this, "RELEASE CLUTCH ONLY ON GREEN", s.X / 2, y + 58, Gold);
            PixelFont.DrawCentered(this, "HOLD CLUTCH TO RESUME", s.X / 2, y + 76, Grey);
            return;
        }
        if (LanGridWaiting)
        {
            PixelFont.DrawCentered(this,"WAITING FOR COMRADES",s.X/2,s.Y*.28f,Gold,2);
            PixelFont.DrawCentered(this,$"CLUTCHES READY: {LanGridReady}/{LanCount}",s.X/2,s.Y*.28f+24,Paper);
            PixelFont.DrawCentered(this,"KEEP CLUTCH HELD - REV UP",s.X/2,s.Y*.28f+37,Gold);
            return;
        }
        int n = (int)MathF.Ceiling(Countdown);
        string txt = n > 0 ? n.ToString() : "ПОЕХАЛИ!";
        Color c = n > 0 ? Red : Gold;
        // Lights.
        for (int i = 0; i < 3; i++)
        {
            bool on = n <= 0 || i < 4 - n;
            Color lc = n <= 0 ? new Color(0.3f, 0.9f, 0.3f) : Red;
            DrawRect(new Rect2(s.X / 2 - 27 + i * 20, s.Y * 0.28f - 22, 14, 14), on ? lc : new Color(0.2f, 0.05f, 0.05f));
        }
        PixelFont.DrawCentered(this, txt, s.X / 2, s.Y * 0.28f, c, n > 0 ? 4 : 3);
        if (StartClutchPrompt)
        {
            PixelFont.DrawCentered(this, FalseStart ? "FALSE START - LAP VOID" : "HOLD CLUTCH - " + P("SHIFT", "LB"),
                s.X / 2, s.Y * 0.28f + 34, FalseStart ? Red : Gold);
            PixelFont.DrawCentered(this, FalseStart ? "COMPLETE THIS LAP TO TRY AGAIN" : "REV UP - RELEASE ON GREEN",
                s.X / 2, s.Y * 0.28f + 46, Paper);
        }
    }

    public int PauseSelection;
    /// <summary>The car radio for the pause menu's rows: on, volume 0..10.</summary>
    public bool RadioOn;
    public int RadioVolume;
    public int RadioStation, RadioStationCount;
    public string AntialiasingName = "OFF", SofteningName = "OFF";
    /// <summary>The REPLAY row of the pause menu (after TAB/COMMENTS): shown, and whether any lap can be watched.</summary>
    public bool PauseReplayShown, PauseReplayAvailable;
    /// <summary>The Party Meeting's rows in order (RaceScene supplies them; the labels come from the ids).</summary>
    public PauseRowId[] PauseRowIds = { PauseRowId.Instructions, PauseRowId.Settings, PauseRowId.Continue, PauseRowId.Restart, PauseRowId.Exit };

    /// <summary>The pause menu's lines, top to bottom: one source for the drawing, the mouse hit test and the rows' tests (RaceScene maps a selected row to its action by the same order).</summary>
    public List<string> PauseLines(out string replayLine)
    {
        // One block, left-aligned so the keys and the actions form columns; the pad's buttons after
        // pad input. Which rows exist and in what order is the controller's (PauseRowIds).
        var lines = new List<string>();
        replayLine = null;
        foreach (var id in PauseRowIds)
        {
            string line = id switch
            {
                PauseRowId.Replay => "       " + (PauseReplayAvailable ? "REPLAY" : "REPLAY - NO LAPS"),
                PauseRowId.Norms => "       NORMS AND MEDALS",
                PauseRowId.Leaderboards => "       LEADERBOARDS",
                PauseRowId.Continue => P("ESC    ", "BACK   ") + "CONTINUE",
                PauseRowId.Restart => P("R      ", "X      ") + "RESTART RACE",
                PauseRowId.Exit => "       EXIT RACE",
                PauseRowId.Instructions => P("I      ", "       ") + "DRIVING SCHOOL",
                _ => "       SETTINGS",
            };
            if (id == PauseRowId.Replay) replayLine = line;
            lines.Add(line);
        }
        return lines;
    }

    void DrawPause()
    {
        var s = Screen;
        var lines = PauseLines(out string replayLine);
        float h = 52 + lines.Count * 10 + (PadLost ? 12 : 0);
        var r = new Rect2(s.X / 2 - 110, Mathf.Floor(s.Y / 2 - h / 2), 220, h);
        Panel(r, 0.85f);
        DrawHeading("PARTY MEETING", s.X / 2, r.Position.Y + 8, _menuTextClock);
        float y0 = r.Position.Y + 30;
        if (PadLost)
        {
            if ((int)(_clock * 2) % 2 == 0) PixelFont.DrawCentered(this, "CONTROLLER DISCONNECTED", s.X / 2, y0, Red);
            y0 += 12;
        }
        float x = Mathf.Floor(s.X / 2 - PixelFont.Measure("       EXIT RACE") / 2f);
        for (int i = 0; i < lines.Count; i++)
        {
            float y = y0 + i * 10;
            bool selected = i == PauseSelection;
            if (selected) DrawRect(new Rect2(r.Position.X + 4, y - 2, r.Size.X - 8, 10), new Color(Red, 0.55f));
            PixelFont.Draw(this, lines[i], new Vector2(x, y), selected ? Gold : ReferenceEquals(lines[i], replayLine) && !PauseReplayAvailable ? Grey : Paper);
        }
        PixelFont.DrawCentered(this, P("UP/DOWN CHOOSE  ENTER SELECT", "D-PAD CHOOSE  A SELECT"), s.X / 2, r.End.Y - 12, Grey);
    }

    void DrawRadio()
    {
        var s = Screen;
        string text = _radioText.ToUpperInvariant();
        float w = PixelFont.Measure(text) + 16;
        var r = new Rect2(Mathf.Floor(s.X / 2 - w / 2), 4, w, 13);
        Panel(r, 0.75f);
        // Little dial glyph: a pip that slides while the text is up.
        DrawRect(new Rect2(r.Position.X + 3, r.Position.Y + 5, 6, 3), new Color(Gold, 0.5f));
        DrawRect(new Rect2(r.Position.X + 3 + ((int)(_clock * 8) % 5), r.Position.Y + 4, 1, 5), Red);
        PixelFont.Draw(this, text, r.Position + new Vector2(12, 3), Gold);
    }

    void DrawBubbles()
    {
        var screen = Screen;
        foreach (var (pos, text) in Bubbles)
        {
            // Only bubbles that are on screen (points near the camera plane project to huge values).
            if (!float.IsFinite(pos.X) || !float.IsFinite(pos.Y)) continue;
            if (pos.X < -60 || pos.Y < -20 || pos.X > screen.X + 60 || pos.Y > screen.Y + 20) continue;
            float w = PixelFont.Measure(text) + 6, h = 11;
            var r = new Rect2(Mathf.Floor(pos.X - w / 2), Mathf.Floor(pos.Y - h - 4), w, h);
            DrawRect(r, new Color(1f, 0.97f, 0.88f));
            DrawRect(r, new Color(0.15f, 0.05f, 0.05f), false, 1f);
            // Tail pointing down at her head.
            var tip = new Vector2(Mathf.Floor(pos.X), Mathf.Floor(pos.Y));
            // Pixel tail, row by row (no polygon triangulation needed).
            for (int k = 0; k < 4; k++)
                DrawRect(new Rect2(tip.X - (3 - k), tip.Y - 4 + k, (3 - k) * 2 + 1, 1), new Color(1f, 0.97f, 0.88f));
            PixelFont.Draw(this, text, r.Position + new Vector2(3, 2), new Color(0.75f, 0.08f, 0.05f), 1, new Color(0, 0, 0, 0));
        }
    }

    /// <summary>The car on the screen (null: none to keep clear of) and how fast it moves down it (px/s, up negative).</summary>
    public Vector2? CarOnScreen;
    public float CarScreenDy;
    const float MessageGap = 22f;       // from the car's centre to the comment's near edge
    bool _messageBelow;
    float _messageY = float.NaN;
    Message _messageFor;      // the comment its side and place were chosen for

    /// <summary>A comment's lines: the text (one or two letter sizes) and its second line.</summary>
    float MessageHeight(Message m, int scale) => 7 * scale + (m.Sub != null ? 5 + 7 : 0);

    /// <summary>
    /// Where the comment goes. When it appears: above the car while it drives down the screen, below
    /// it while it drives up, on the other side where that one has no room. From then on it stays
    /// put on that side for as long as it is shown; it moves only to keep its gap when the car comes
    /// towards it (further away, never across the car).
    /// </summary>
    void PlaceMessage(float dt)
    {
        if (_center is not { } m || CarOnScreen is not Vector2 car) { _messageY = float.NaN; _messageFor = null; return; }
        var s = Screen;
        int scale = PixelFont.Measure(m.Text, 2) < s.X - 16 ? 2 : 1;
        float h = MessageHeight(m, scale), top = 24, bottom = s.Y - 34 - h;
        float above = car.Y - MessageGap - h, below = car.Y + MessageGap;
        if (!ReferenceEquals(m, _messageFor) || float.IsNaN(_messageY))
        {
            // A new comment: pick its side and place once.
            _messageFor = m;
            if (CarScreenDy > 6f) _messageBelow = false;
            else if (CarScreenDy < -6f) _messageBelow = true;
            if (!_messageBelow && above < top) _messageBelow = true;
            else if (_messageBelow && below > bottom) _messageBelow = false;
            _messageY = Math.Clamp(_messageBelow ? below : above, top, Math.Max(top, bottom));
            return;
        }
        // Shown: the side never changes; only pushed further off when the car closes in.
        float keep = _messageBelow ? Math.Max(_messageY, below) : Math.Min(_messageY, above);
        keep = Math.Clamp(keep, top, Math.Max(top, bottom));
        _messageY = Mathf.Lerp(_messageY, keep, 1f - MathF.Exp(-10f * dt));
    }

    void DrawMessages()
    {
        var s = Screen;
        if (_center is { } m)
        {
            if (NormMissActive && m.Prio > Prio.Critical) return;
            if (m.Time < 0.6f && (int)(m.Time * 10) % 2 == 1) return; // blink in
            int scale = PixelFont.Measure(m.Text, 2) < s.X - 16 ? 2 : 1;
            float y = float.IsNaN(_messageY) ? Mathf.Floor(s.Y * 0.36f) : Mathf.Floor(_messageY);
            PixelFont.DrawCentered(this, m.Text, s.X / 2, y, m.Color, scale, new Color(0.35f, 0, 0));
            if (m.Sub != null)
                PixelFont.DrawCentered(this, m.Sub, s.X / 2, y + 7 * scale + 5, Paper);
        }
    }

    // ------------------------------------------------------------ babushka on the lens

    float _lensTime = 99f, _lensDuration;
    Color _lensScarf;
    bool _lensMilitary;
    int _lensSeed;
    bool _lensTape;
    public readonly record struct LensSplatState(Color Scarf, float Duration, float Age, bool Military, int Seed);
    public LensSplatState CurrentLensSplat => new(_lensScarf, _lensDuration, _lensTime, _lensMilitary, _lensSeed);
    public bool TapeLensActive => _lensTape && _lensTime < _lensDuration;

    public void RestoreTapeLensEvent(Race.TapeEvent? e, float age)
    {
        if (e is not Race.TapeEvent hit) { ClearTapeLensSplat(); return; }
        RestoreTapeLensSplat(new LensSplatState(new Color(hit.Dir.X, hit.Dir.Y, hit.Dir.Z), hit.Life, age, hit.Param > .5f, hit.Instance));
    }

    /// <summary>A babushka smacked into the camera: her squashed face on the cracked glass.</summary>
    public void LensSplat(Color scarf, float duration, bool military=false, int? seed=null)
    {
        _lensScarf = scarf;_lensMilitary=military;
        _lensDuration = duration;
        _lensTime = 0;
        _lensTape = false;
        _lensSeed = seed ?? _lensSeed + 1;
    }

    public void RestoreTapeLensSplat(LensSplatState state)
    {
        _lensScarf = state.Scarf; _lensDuration = state.Duration;
        _lensTime = state.Age; _lensMilitary = state.Military; _lensSeed = state.Seed;
        _lensTape = true;
        QueueRedraw();
    }

    public void ClearTapeLensSplat()
    {
        if (!_lensTape) return;
        _lensTape = false; _lensTime = 99f;
        QueueRedraw();
    }

    // Squashed against the glass from the outside: headscarf, eyes squeezed shut (> <),
    // flattened nose, mouth open in an O. S scarf, s scarf shade, K skin, k pressed skin,
    // N nose, D dark, M mouth, W teeth.
    static readonly string[] LensFace =
    {
        ".......SSSSSSSSSS.......",
        ".....SSSSSSSSSSSSSS.....",
        "...SSSSSSSSSSSSSSSSSS...",
        "..SSSSSsssssssssSSSSSS..",
        ".SSSSssKKKKKKKKKKssSSSS.",
        ".SSSsKKKKKKKKKKKKKKsSSS.",
        "SSSsKKDDKKKKKKKKDDKKsSSS",
        "SSSsKKKDDKKKKKKDDKKKsSSS",
        "SSSsKKDDKKKKKKKKDDKKsSSS",
        "SSSsKkkKKKKNNKKKKkkKsSSS",
        "SSSsKkkKKKNNNNKKKkkKsSSS",
        "SSSsKKKKKNNNNNNKKKKKsSSS",
        ".SSsKKKKKKNNNNKKKKKKsSS.",
        ".SSsKKKKKDDDDDDKKKKKsSS.",
        "..SsKKKKDMMWWMMDKKKKsS..",
        "..SSsKKKDMMMMMMDKKKsSS..",
        "...SSsKKKDDDDDDKKKsSS...",
        "....SSssKKKKKKKKssSS....",
        "......SSSSSSSSSSSS......",
        ".........SSSSSS.........",
        "........SSS..SSS........",
        ".......SSS....SSS.......",
    };

    static readonly string[] MilitaryLensFace = {
        "......SSSSSSSSSSSS......", "....SSSSSSSSSSSSSSSS....",
        "...SSSSSSSSSSSSSSSSSS...", "..SSSSSsssssssssSSSSSS..",
        ".SSSSssssssssssssssSSSS.", ".ssssssssssssssssssssss.",
        "...sKKDDKKKKKKKKDDKKs...", "...sKKKDDKKKKKKDDKKKs...",
        "...sKKDDKKKKKKKKDDKKs...", "...sKkkKKKKNNKKKKkkKs...",
        "...sKkkKKKNNNNKKKkkKs...", "...sKKKKKNNNNNNKKKKKs...",
        "...sKKKKKKNNNNKKKKKKs...", "...sKKKKKDDDDDDKKKKKs...",
        "....KKKKDMMWWMMDKKKK....", "....KKKKDMMMMMMDKKKK....",
        ".....KKKKDDDDDDKKKK.....", "......KKKKKKKKKKKK......",
        ".......KKKKKKKKKK.......", "........KKKKKKKK........",
        ".........SSSSSS.........", "........SSSSSSSS........"
    };

    // Palm pressed flat on the glass, fingers splayed; C is the coat sleeve.
    static readonly string[] LensHand =
    {
        "H..H..H.",
        "H.HH.HH.",
        "HHHHHHH.",
        "HHHHHHH.H",
        "HHHHHHHHH",
        "HHHHHHHH.",
        ".HHHHHH..",
        "..CCCC...",
        "..CCCC...",
    };

    void DrawSprite(string[] rows, Vector2 at, float cell, bool mirror, Func<char, Color?> palette, float stretchY = 1f)
    {
        // Dark outline first, so she reads against grass and road alike.
        for (int r = 0; r < rows.Length; r++)
            for (int c = 0; c < rows[r].Length; c++)
            {
                if (palette(rows[r][c]) is not Color col) continue;
                float x = mirror ? at.X + (9 - 1 - c) * cell : at.X + c * cell;
                DrawRect(new Rect2(Mathf.Floor(x) - 1, Mathf.Floor(at.Y + r * cell * stretchY) - 1, Mathf.Ceil(cell) + 2, Mathf.Ceil(cell * stretchY) + 2), new Color(0.12f, 0.04f, 0.03f, col.A));
            }
        for (int r = 0; r < rows.Length; r++)
        {
            string row = rows[r];
            for (int c = 0; c < row.Length; c++)
            {
                if (palette(row[c]) is not Color col) continue;
                float x = mirror ? at.X + (9 - 1 - c) * cell : at.X + c * cell;
                DrawRect(new Rect2(Mathf.Floor(x), Mathf.Floor(at.Y + r * cell * stretchY), Mathf.Ceil(cell), Mathf.Ceil(cell * stretchY)), col);
            }
        }
    }

    void DrawLensSplat()
    {
        var s = Screen;
        float t = _lensTime, d = _lensDuration;
        float fade = Math.Clamp((d - t) / 0.35f, 0f, 1f);
        var rng = new RandomNumberGenerator { Seed = (ulong)(7 + _lensSeed) };
        Vector2 hit = new(Mathf.Floor(s.X / 2 + rng.RandfRange(-40, 40)), Mathf.Floor(s.Y * 0.64f + rng.RandfRange(-8, 8)));

        // White flash on impact.
        if (t < 0.1f) DrawRect(new Rect2(0, 0, s.X, s.Y), new Color(1, 1, 1, 0.7f * (1f - t / 0.1f)));

        // Cracked glass: jagged rays from the impact with side branches, and a broken ring.
        var glass = new Color(0.92f, 0.96f, 1f, 0.85f * fade);
        var glassDim = new Color(0.8f, 0.88f, 0.95f, 0.5f * fade);
        int rays = 11;
        for (int i = 0; i < rays; i++)
        {
            float ang = i * MathF.Tau / rays + rng.RandfRange(-0.2f, 0.2f);
            float len = rng.RandfRange(0.35f, 0.7f) * s.Y;
            Vector2 p = hit;
            int segs = 5;
            for (int k = 0; k < segs; k++)
            {
                ang += rng.RandfRange(-0.3f, 0.3f);
                Vector2 q = p + Vector2.FromAngle(ang) * len / segs;
                DrawLine(p, q, glass, 1f);
                if (k > 0 && rng.Randf() < 0.5f)
                {
                    float b = ang + (rng.Randf() < 0.5f ? -1 : 1) * rng.RandfRange(0.5f, 1.1f);
                    DrawLine(p, p + Vector2.FromAngle(b) * rng.RandfRange(6f, 18f), glassDim, 1f);
                }
                p = q;
            }
        }
        for (int ring = 0; ring < 2; ring++)
        {
            float rr = 26f + ring * 30f;
            int n = 14;
            for (int i = 0; i < n; i++)
            {
                if (rng.Randf() < 0.3f) continue;
                float a0 = i * MathF.Tau / n, a1 = (i + 1) * MathF.Tau / n;
                Vector2 p0 = hit + Vector2.FromAngle(a0) * (rr + rng.RandfRange(-4, 4));
                Vector2 p1 = hit + Vector2.FromAngle(a1) * (rr + rng.RandfRange(-4, 4));
                DrawLine(p0, p1, ring == 0 ? glass : glassDim, 1f);
            }
        }

        // Her: slams in slightly oversized, sticks, then slowly slides down the glass leaving a smear.
        float cell = Mathf.Floor(Math.Max(3f, s.Y / 52f));
        float squash = t < 0.12f ? 1.25f - t / 0.12f * 0.25f : 1f;
        float c = cell * squash;
        float slide = t < 0.55f ? 0f : 120f * (t - 0.55f) * (t - 0.55f);
        float stretch = 1f + Math.Min(0.35f, slide / 300f);
        float w = 24 * c, h = LensFace.Length * c * stretch;
        Vector2 at = new(Mathf.Floor(hit.X - w / 2), Mathf.Floor(hit.Y - h * 0.45f + slide));
        float alpha = fade;
        if (slide > 1f)
            DrawRect(new Rect2(at.X + 5 * c, hit.Y - h * 0.45f + 4 * c, 14 * c, slide), new Color(0.86f, 0.68f, 0.58f, 0.22f * alpha));
        Color scarf = _lensScarf, shade = _lensScarf.Darkened(0.35f);
        DrawSprite(_lensMilitary?MilitaryLensFace:LensFace, at, c, false, ch => ch switch
        {
            'S' => new Color(scarf, alpha),
            's' => new Color(shade, alpha),
            'K' => new Color(0.93f, 0.72f, 0.62f, alpha),
            'k' => new Color(0.98f, 0.58f, 0.55f, alpha),
            'N' => new Color(1f, 0.8f, 0.72f, alpha),
            'D' => new Color(0.25f, 0.08f, 0.06f, alpha),
            'M' => new Color(0.55f, 0.1f, 0.1f, alpha),
            'W' => new Color(0.95f, 0.93f, 0.85f, alpha),
            _ => null,
        }, stretch);
        // Hands either side of the face, a little higher, sliding down with her.
        Func<char, Color?> hand = ch => ch switch
        {
            'H' => new Color(0.93f, 0.72f, 0.62f, alpha),
            'C' => new Color(0.3f, 0.28f, 0.25f, alpha),
            _ => null,
        };
        DrawSprite(LensHand, new Vector2(at.X - 9 * c, at.Y + 3 * c), c, false, hand, stretch);
        DrawSprite(LensHand, new Vector2(at.X + w, at.Y + 3 * c), c, true, hand, stretch);
        // Glare on the glass over her.
        var glare = new Color(1, 1, 1, 0.35f * fade);
        for (int i = 0; i < 3; i++)
            DrawLine(hit + new Vector2(-w * 0.45f + i * 5, -h * 0.2f + i * 2), hit + new Vector2(-w * 0.2f + i * 5, -h * 0.45f + i * 2), glare, 2f);
    }

    void DrawDebug()
    {
        var s = Screen;
        float y = 54;
        Panel(new Rect2(4, y - 3, 170, DebugLines.Count * 9 + 38), 0.7f);
        foreach (var line in DebugLines)
        {
            PixelFont.Draw(this, line, new Vector2(8, y), new Color(0.6f, 1f, 0.6f));
            y += 9;
        }
        // Wheel load bars in car layout: FL FR / RL RR.
        for (int i = 0; i < 4; i++)
        {
            float x = 12 + (i % 2) * 24, yy = y + 2 + (i / 2) * 14;
            float frac = Math.Clamp(WheelLoads[i] / 6000f, 0, 1);
            DrawRect(new Rect2(x, yy, 18, 10), new Color(0.2f, 0.2f, 0.2f));
            DrawRect(new Rect2(x, yy + 10 * (1 - frac), 18, 10 * frac), frac <= 0.001f ? Red : new Color(0.5f, 0.8f, 0.4f));
        }
        PixelFont.Draw(this, "WHEEL LOADS", new Vector2(64, y + 10), Grey);
        _ = s;
    }
}
