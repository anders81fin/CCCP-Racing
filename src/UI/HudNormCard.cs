using Godot;

namespace CCCPRacing.UI;

public partial class Hud
{
    /// <summary>The Socialist Rally's target card (top right, under the same top edge as the left aid); null: none (every other mode, school, LAN, viewer, title).</summary>
    public NormCardView NormCard;

    public const float NormCardWidth = 128f;
    static Color MedalColour(string medal) => medal switch { "BRONZE" => Bronze, "SILVER" => Silver, "GOLD" => Gold, _ => Gold };

    /// <summary>The card's rectangle on a screen of this size (the tests measure it).</summary>
    internal Rect2 NormCardRect(Vector2 screen) => new(screen.X - 4 - NormCardWidth, 4, NormCardWidth, NormCardHeight());

    /// <summary>The one predicate for the target card: drawn and laid out (the medal star's rest) only while the model is there and the player has not hidden it.</summary>
    internal bool NormCardShown => NormCard != null && NormsCardOn;

    float NormCardHeight() => NormCard == null ? 0 : NormCard.AllMet ? 30 : 13 + 3 + 25 + (NormCard.NextMilestone != null ? 10 : 0) + 4 + (NormCardFlash && NormCard.LastPassed != null ? 10 : 0);

    /// <summary>The progression flash: a short, nonblocking highlight when a norm was just beaten (the same moment as the paper burst; no sound of its own).</summary>
    bool NormCardFlash => _normAge < 2.5f;

    void DrawNormCard()
    {
        var card = NormCard;
        if (card == null) return;
        var s = Screen;
        var r = NormCardRect(s);
        bool flash = NormCardFlash;
        DrawRect(r, new Color(0, 0, 0, 0.55f));
        DrawRect(r, new Color(flash && (int)(_clock * 6) % 2 == 0 ? Gold : Red, 0.9f), false, 1f);
        // header: NORMS on the left, the emblem where it always was (star and CCCP, top right)
        PixelFont.Draw(this, "NORMS", new Vector2(r.Position.X + 5, r.Position.Y + 4), Paper);
        DrawStar(new Vector2(s.X - 12, 11), 1, Red);
        PixelFont.Draw(this, "CCCP", new Vector2(s.X - 44, 8), Red);
        float y = r.Position.Y + 17;
        if (card.AllMet)
        {
            PixelFont.Draw(this, "ALL TARGETS MET", new Vector2(r.Position.X + 6, y + 3), Green);
            return;
        }
        if (flash && card.LastPassed != null)
        {
            var p = card.LastPassed;
            PixelFont.Draw(this, $"{p.Step,2} X {p.Status}", new Vector2(r.Position.X + 6, y), Green);
            y += 10;
        }
        // CURRENT: the time is the prominent thing, the step before it, the medal it earns (if it is a medal row) after it
        var c = card.Current;
        DrawRect(new Rect2(r.Position.X + 2, y - 1, r.Size.X - 4, 25), new Color(Red, 0.35f));
        PixelFont.Draw(this, $"{c.Step,2}", new Vector2(r.Position.X + 5, y + 4), Gold);
        PixelFont.Draw(this, "<", new Vector2(r.Position.X + 20, y + 4), Paper);
        PixelFont.Draw(this, Race.LapTimer.Format(c.Time), new Vector2(r.Position.X + 28, y), Paper, 2);
        PixelFont.Draw(this, "CURRENT", new Vector2(r.Position.X + 28, y + 15), Gold);
        if (c.Medal != "") PixelFont.Draw(this, c.Medal, new Vector2(r.Position.X + 28 + PixelFont.Measure("CURRENT "), y + 15), MedalColour(c.Medal));
        y += 26;
        if (card.NextMilestone is { } m)
        {
            PixelFont.Draw(this, $"{m.Step,2}  < {Race.LapTimer.Format(m.Time)}", new Vector2(r.Position.X + 6, y), new Color(Paper, 0.85f));
            PixelFont.Draw(this, m.Medal, new Vector2(r.End.X - 3 - PixelFont.Measure(m.Medal), y), MedalColour(m.Medal));
        }
    }
}
