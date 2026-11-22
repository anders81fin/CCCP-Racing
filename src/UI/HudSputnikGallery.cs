using System;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.UI;

/// <summary>
/// The Sputnik gallery of a personnel file (KGB FILES > PERSONNEL > G, RaceScene.SputnikGallery.cs): four pages that show what the driver of that file has earned in the SPUTNIK test pilots' programme, as the ceremonies showed it:
/// the licence, the order (the highest class), the champion's pennant and the chief test pilot's car (the 3D view with the car's livery, the page's text drawn here). A page not earned yet is a locked card that says what it takes.
/// The cards are the ceremonies' own drawings (HudSputnik.cs), fully revealed and without the fanfare.
/// </summary>
public partial class Hud
{
    public bool GalleryOn;
    public int GalleryPage, GalleryPages = 4;
    public float GalleryAge;
    public bool[] GalleryEarned = new bool[4];      // licence, order, pennant, car
    public SputnikAward GalleryOrderClass = SputnikAward.Third;
    public string GalleryTrackLine = "";

    static readonly string[] GalleryNames = { "TEST PILOT'S LICENCE", "ORDER OF SPUTNIK", "RALLY CHAMPION'S PENNANT", "THE CHIEF TEST PILOT'S CAR" };
    static readonly string[] GalleryHow =
    {
        "SILVER OR BETTER ON ILYICH, ATOMGRAD-4 AND SIBERIA, ON SPUTNIK",
        "SILVER OR BETTER ON ALL FOUR TRACKS, ON SPUTNIK. GOLD ON MORE TRACKS, A HIGHER CLASS",
        "GOLD ON ILYICH, ATOMGRAD-4 AND SIBERIA, ON SPUTNIK",
        "GOLD ON ALL FOUR TRACKS, ON SPUTNIK: THE MECHANIC PAINTS YOUR NAME ON THE CAR",
    };

    /// <summary>The page is the car: the 3D view carries it, the HUD draws only the bands and the text.</summary>
    public bool GalleryOnCar => GalleryOn && GalleryPage == 3 && GalleryEarned[3];

    void DrawGallery()
    {
        float cx = Screen.X / 2;
        int page = Math.Clamp(GalleryPage, 0, 3);
        var panel = new Rect2(6, 6, Screen.X - 12, Screen.Y - 12);
        bool earned = GalleryEarned[page];
        var accent = !earned ? Grey : page switch { 0 => SpSilver, 2 => Red, _ => Gold };
        string name = SpName.Length == 0 ? "COMRADE" : SpName;
        string foot = P("LEFT/RIGHT PAGE   ESC BACK", "D-PAD PAGE   B BACK");
        if (GalleryOnCar)
        {
            float topBand = 34, bottomBand = Math.Min(Screen.Y * 0.28f, 74);
            DrawRect(new Rect2(0, 0, Screen.X, topBand), new Color(0.02f, 0.01f, 0, 0.82f));
            DrawRect(new Rect2(0, Screen.Y - bottomBand, Screen.X, bottomBand), new Color(0.02f, 0.01f, 0, 0.82f));
            DrawRect(new Rect2(0, topBand, Screen.X, 1), new Color(Gold, 0.8f)); DrawRect(new Rect2(0, Screen.Y - bottomBand - 1, Screen.X, 1), new Color(Gold, 0.8f));
            DrawRect(panel, Gold, false, 2);
            PixelFont.DrawCentered(this, "CHIEF TEST PILOT", cx, panel.Position.Y + 12, Gold, PixelFont.Measure("CHIEF TEST PILOT", 2) < panel.Size.X - 24 ? 2 : 1, HeadingShadow);
            float by = Screen.Y - bottomBand;
            SpLine("COMRADE " + name + ", THE CAR IS YOURS", cx, by + 8, Gold, panel.Size.X - 24);
            PixelFont.DrawCentered(this, "THE PIT MECHANIC PAINTED IT. ALL FOUR TRACKS ON GOLD, ON SPUTNIK", cx, by + 22, Paper);
            PixelFont.DrawCentered(this, $"{page + 1}/{GalleryPages}   " + foot, cx, panel.End.Y - 12, Grey);
            return;
        }
        DrawRect(new Rect2(Vector2.Zero, Screen), new Color(0.03f, 0.02f, 0.02f, 0.97f));
        DrawRect(panel, accent, false, 2);
        DrawRect(new Rect2(panel.Position + Vector2.One * 5, panel.Size - Vector2.One * 10), new Color(accent, 0.4f), false, 1);
        SpAge = 20f + _clock;      // (everything revealed, the pennant still waves)
        if (earned)
        {
            SpAward = page switch { 0 => SputnikAward.TestPilot, 1 => GalleryOrderClass, 2 => SputnikAward.Champion, _ => SputnikAward.Chief };
            switch (page)
            {
                case 0: DrawSpLicence(panel); break;
                case 1: DrawSpOrder(panel); break;
                case 2: DrawSpPennant(panel); break;
                default: DrawSpChief(panel); break;
            }
        }
        else
        {
            PixelFont.DrawCentered(this, GalleryNames[page], cx, panel.Position.Y + 12, Grey, PixelFont.Measure(GalleryNames[page], 2) < panel.Size.X - 24 ? 2 : 1, HeadingShadow);
            var card = new Rect2(panel.Position.X + 40, panel.Position.Y + 44, panel.Size.X - 80, panel.Size.Y * 0.5f);
            DrawRect(card, new Color(0.08f, 0.07f, 0.06f)); DrawRect(card, new Color(Grey, 0.7f), false, 1);
            var c = card.GetCenter();
            DrawRect(new Rect2(c.X - 7, c.Y - 4, 14, 11), Grey); DrawArc(c + new Vector2(0, -4), 6, MathF.PI, MathF.Tau, 12, Grey, 2);      // a padlock
            PixelFont.DrawCentered(this, "NOT EARNED YET", cx, c.Y + 16, Paper);
            float y = card.End.Y + 10;
            foreach (var part in SpWrap(GalleryHow[page], panel.Size.X - 48)) { PixelFont.DrawCentered(this, part, cx, y, Gold); y += 10; }
            if (GalleryTrackLine.Length > 0) PixelFont.DrawCentered(this, GalleryTrackLine, cx, y + 4, Grey);
        }
        PixelFont.DrawCentered(this, $"{page + 1}/{GalleryPages}   " + foot, cx, panel.End.Y - 12, Grey);
    }
}
