using System;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.UI;

/// <summary>The key card: on K (a pad: START, then Y);
/// after pad input it shows the pad's buttons.</summary>
public partial class Hud
{
    public bool LoadingTrack, LoadingTrackReady;
    /// <summary>The loading card's bar, 0..1 (an estimate by build checkpoints; 1 only when ready).</summary>
    public float LoadingProgress;
    /// <summary>The bar belongs to the normal single-player card only: a LAN card keeps its old layout (set by TrackLoadingScreen, never by the LAN override).</summary>
    public bool LoadingBarShown;
    string LoadingTrackHeading => LoadingTrackReady ? "TRACK READY" : "LOADING TRACK...";
    // While loading, the heading types itself out again and again (a short pause with the full line each round); "TRACK READY" types once.
    float LoadingHeadingElapsed => LoadingTrackReady ? _loadingHeadingClock
        : _loadingHeadingClock % (LoadingTrackHeading.Length / HeadingRate + .7f);
    public bool LoadingTrackHeadingComplete => _loadingHeadingClock * HeadingRate >= LoadingTrackHeading.Length;

    void DrawLoadingTrack()
    {
        var s = Screen;
        var controls = SchoolScript.Recap(SchoolCourse.Driving);
        int rows = (controls.Length + 2) / 3;
        const float HeadingTop = 9, HeadingHeight = 14, FooterGap = 8, BottomPadding = 8, CaptionBottom = 39;
        float FooterHeight = LoadingBarShown ? 19 : 7;
        float fixedWithoutRows = HeadingTop + HeadingHeight + CaptionBottom + FooterGap + FooterHeight + BottomPadding;
        float cellHeight = Mathf.Clamp(Mathf.Floor((s.Y - 8 - fixedWithoutRows - 8) / Math.Max(1, rows - 1)), 42, 50);
        float gridHeight = (rows - 1) * cellHeight + CaptionBottom;
        float fixedHeight = HeadingTop + HeadingHeight + gridHeight + FooterGap + FooterHeight + BottomPadding;
        float headingGap = Mathf.Clamp(s.Y - 8 - fixedHeight, 8, 24);
        float w = Math.Min(s.X - 8, 442), h = fixedHeight + headingGap;
        float cx = s.X / 2;
        var r = new Rect2(Mathf.Floor(cx - w / 2), Mathf.Floor((s.Y - h) / 2), w, h);
        Panel(r, 0.94f);
        DrawHeading(LoadingTrackHeading, cx, r.Position.Y + HeadingTop, LoadingHeadingElapsed);
        float gridTop = r.Position.Y + HeadingTop + HeadingHeight + headingGap;
        DrawControlGrid(controls, cx, gridTop, w, cellHeight);
        // The bar in the style of LOADING REPLAY (ReplayOverlay): grey trough, gold fill, red frame. An estimate by build checkpoints; no number.
        float textY = gridTop + gridHeight + FooterGap;
        if (LoadingBarShown)
        {
            float barW = Math.Min(w - 24, 240);
            var bar = new Rect2(Mathf.Floor(cx - barW / 2), gridTop + gridHeight + FooterGap, barW, 6);
            float p = Mathf.Clamp(float.IsFinite(LoadingProgress) ? LoadingProgress : 0f, 0f, 1f);
            DrawRect(bar, new Color(Grey, 0.3f));
            DrawRect(new Rect2(bar.Position, new Vector2(Mathf.Floor(bar.Size.X * p), bar.Size.Y)), Gold);
            DrawRect(bar, new Color(Red, 0.9f), false);
            textY = bar.End.Y + 5;
        }
        if (LoadingTrackReady && (int)(_clock * 2) % 2 == 0)
            PixelFont.DrawCentered(this, "PRESS ANY KEY, TOVARISH", cx, textY, Gold);
    }

    static readonly (string key, string action)[] KeyCard =
    {
        ("1 - 4", "DIESEL / PETROL / JET / SPUTNIK"),
        ("ALT+ENTER", "WINDOW / FULLSCREEN"),
    };

    static readonly (string key, string action)[] PadCard =
    {
        ("D-PAD UP/DOWN", "POWER + / - (STAKHANOV)"),
        ("START", "PAUSE"),
        ("START, Y", "THIS CARD"),
    };

    /// <summary>The recap's control pictures followed by the remaining shortcuts.</summary>
    void DrawKeys()
    {
        var s = Screen;
        float cx = s.X / 2;
        const int RowH = 10, KeyCol = 16 * PixelFont.Advance;
        int actionW = 0;
        var card = Pad ? PadCard : KeyCard;
        var controls = SchoolScript.Recap(SchoolCourse.Driving);
        int gridHeight = ((controls.Length + 2) / 3) * 50;
        foreach (var (_, action) in card) actionW = Math.Max(actionW, PixelFont.Measure(action));
        float w = Math.Min(s.X - 8, Math.Max(442, KeyCol + actionW + 36));
        float h = 34 + gridHeight + card.Length * RowH + 18;
        var r = new Rect2(Mathf.Floor(cx - w / 2), Mathf.Floor((s.Y - h) / 2), w, h);
        Panel(r, 0.88f);
        DrawHeading("CONTROLS, TOVARISH", cx, r.Position.Y + 7, _menuTextClock);
        DrawControlGrid(controls, cx, r.Position.Y + 31, w);
        float x = Mathf.Floor(cx - (KeyCol + actionW) / 2f), y = r.Position.Y + 31 + gridHeight;
        foreach (var (key, action) in card)
        {
            PixelFont.Draw(this, key, new Vector2(x, y), Gold);
            PixelFont.Draw(this, action, new Vector2(x + KeyCol, y), Paper);
            y += RowH;
        }
        if ((int)(_clock * 2) % 2 == 0)
            PixelFont.DrawCentered(this, AnyKey, cx, r.End.Y - 13, Gold);
    }
}
