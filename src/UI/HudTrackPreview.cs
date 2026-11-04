using System;
using Godot;
using CCCPRacing.Race;

namespace CCCPRacing.UI;

public partial class Hud
{
    public bool TrackPreview, PreviewFullscreen, PreviewFocused, PreviewMovie;
    public string PreviewTitle, PreviewSubtitle, PreviewCaption, PreviewLoading;
    public Texture2D PreviewTexture;
    public Vector2? PreviewArrow;
    public string[] PreviewTell;
    /// <summary>The first track's drone tour opens with a black card naming the track and where the name comes from (0 = off, 1 = opaque).</summary>
    public float PreviewCardAlpha;
    /// <summary>The card waits for a key (the game's tour): it says so, and Enter no longer races.</summary>
    public bool PreviewCardWaiting;
    /// <summary>The card's big lines (scale 3) and explanation lines (set by the tour: DroneFilm).</summary>
    public string[] PreviewCardBig = Array.Empty<string>(), PreviewCardSmall = Array.Empty<string>();

    public static Rect2 PreviewRect(Vector2 size)
    {
        // Leave more of the title banner visible and reserve the bottom for track selection.
        float h = Mathf.Floor(size.Y * 0.54f);
        float w = Math.Min(size.X - 20, Mathf.Floor(h * 16 / 9));
        float y = Math.Max(8, Mathf.Floor(size.Y - 57 - 20 - h));
        return new Rect2(Mathf.Floor((size.X - w) / 2), y, w, h);
    }

    void DrawTrackPreview()
    {
        Vector2 s = Screen;
        var r = PreviewFullscreen ? new Rect2(Vector2.Zero, s) : PreviewRect(s);
        if (!PreviewFullscreen)
        {
            DrawRect(new Rect2(Vector2.Zero, s), Colors.Black);
            DrawTitleBanner();
            if (PreviewTexture != null) DrawTextureRect(PreviewTexture, r, false);
            DrawRect(r.Grow(1), PreviewFocused ? Gold : Grey, false, 1);
            float y = Math.Max(r.End.Y + 14, s.Y - 57), cx = s.X / 2;
            PixelFont.DrawCentered(this, CardTitle, cx, y, PreviewFocused ? Grey : Gold);
            // The selected name stays readable even in the narrowest supported windows.
            PixelFont.DrawCentered(this, "< " + CardOptions[CardSelection] + " >", cx, y + 13,
                CardLocked[CardSelection] ? Grey : Paper, 2);
            PixelFont.DrawCentered(this, CardSub, cx, y + 32, Grey);
            PixelFont.DrawCentered(this, PreviewFocused ? "ENTER FULL SCREEN   DOWN TRACKS   ESC BACK"
                : "< > TRACK   ENTER SELECT   UP VIDEO   ESC BACK", cx, s.Y - 12, Grey);
        }
        if (PreviewLoading != null)
        {
            DrawRect(r, new Color(0, 0, 0, 0.8f));
            PixelFont.DrawCentered(this, "LOADING " + PreviewLoading + "...", r.GetCenter().X, r.GetCenter().Y - 4, Gold);
            return;
        }
        if (Clean) return;
        if (PreviewArrow is Vector2 at && new Rect2(Vector2.Zero, r.Size).HasPoint(at))
            DrawWorldArrow(r.Position + at);
        if (PreviewTitle != null) DrawLessonHeading(r, PreviewTitle, PreviewSubtitle);
        else if (PreviewTell is { Length: > 0 })
            DrawLessonTell(r, PreviewTell, Array.Empty<SchoolKey>());
        else if (!string.IsNullOrEmpty(PreviewCaption)) DrawLessonCaption(PreviewCaption, r);
        if (CarTour) DrawCarTourExtras(r);
        if (PreviewCardAlpha > 0) DrawPreviewCard(r);
        if (PreviewFullscreen && !PreviewMovie)
            PixelFont.DrawCentered(this, PreviewCardWaiting ? P("ESC BACK", "B BACK")
                : CarTour ? (CarTourLocked ? P("ESC BACK   THIS FUEL IS LOCKED", "B BACK   THIS FUEL IS LOCKED") : P("ESC BACK   ENTER RACE ON THIS FUEL", "B BACK   A RACE ON THIS FUEL"))
                : P("ESC BACK   ENTER RACE HERE", "B BACK   A RACE HERE"), s.X / 2, 7, Grey);
    }

    void DrawPreviewCard(Rect2 r)
    {
        float a = PreviewCardAlpha, f = r.Size.Y / 270f, cx = r.GetCenter().X;
        DrawRect(r, new Color(0, 0, 0, a));
        // A longer story starts higher and sets its lines closer (the film's encoder lays it out the same way).
        bool longText = PreviewCardSmall.Length > 4;
        int big0 = longText ? 36 : 62, small0 = longText ? 112 : 150, step = longText ? 15 : 16;
        float elapsed = _previewCardHeadingClock;
        for (int i = 0; i < PreviewCardBig.Length; i++)
        {
            float y = r.Position.Y + Mathf.Floor((big0 + 30 * i) * f);
            if (PreviewMovie)
                PixelFont.DrawCentered(this, PreviewCardBig[i], cx, y, new Color(Gold, a), 3, new Color(0.3f, 0, 0, a));
            else
                DrawTypewriter(PreviewCardBig[i], cx, y, new Color(Gold, a), elapsed, HeadingRate, 3, new Color(0.3f, 0, 0, a));
            elapsed -= PreviewCardBig[i].Length / HeadingRate;
        }
        for (int i = 0; i < PreviewCardSmall.Length; i++)
            PixelFont.DrawCentered(this, PreviewCardSmall[i], cx, r.Position.Y + Mathf.Floor((small0 + step * i) * f), new Color(Paper, a), 1, new Color(0.3f, 0, 0, a));
        if (PreviewCardWaiting && Time.GetTicksMsec() / 600 % 2 == 0)
            PixelFont.DrawCentered(this, P("PRESS ANY KEY", "PRESS ANY BUTTON"), cx, r.Position.Y + Mathf.Floor(252 * f), new Color(Gold, a), 1, new Color(0.3f, 0, 0, a));
    }
}
