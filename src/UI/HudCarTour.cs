using System;
using Godot;

namespace CCCPRacing.UI;

public partial class Hud
{
    /// <summary>A car's tour is up (T on the FUEL card): the track tour's full screen frame with the car's own captions, gauges and data card.</summary>
    public bool CarTour, CarTourLocked;
    public string CarTourCaption;
    /// <summary>The data card's rows, "LABEL|VALUE", and how much of it shows (0..1).</summary>
    public string[] CarSpecRows = Array.Empty<string>();
    public string CarSpecTitle = "";
    public float CarSpecAlpha;
    public float CarRpm, CarRedline, CarBoost;

    /// <summary>A caption of one or two lines: it breaks at the space nearest the middle when it does not fit a line.</summary>
    static string[] CaptionLines(string text, float width)
    {
        if (PixelFont.Measure(text, 1) <= width) return new[] { text };
        int mid = text.Length / 2, best = -1;
        for (int i = 0; i < text.Length; i++)
            if (text[i] == ' ' && (best < 0 || Math.Abs(i - mid) < Math.Abs(best - mid))) best = i;
        return best < 0 ? new[] { text } : new[] { text[..best], text[(best + 1)..] };
    }

    void DrawCarTourExtras(Rect2 r)
    {
        if (Clean) return;
        float cx = r.GetCenter().X;
        if (!string.IsNullOrEmpty(CarTourCaption) && CarSpecAlpha <= 0.01f)
        {
            var lines = CaptionLines(CarTourCaption, r.Size.X - 24);
            float y = r.End.Y - 22 - 11 * (lines.Length - 1);
            float w = 0;
            foreach (var l in lines) w = Math.Max(w, PixelFont.Measure(l, 1));
            DrawRect(new Rect2(Mathf.Floor(cx - w / 2 - 8), y - 5, Mathf.Ceil(w) + 16, 11 * lines.Length + 8), new Color(0, 0, 0, 0.62f));
            for (int i = 0; i < lines.Length; i++)
                PixelFont.DrawCentered(this, lines[i], cx, y + 11 * i, Gold, 1, new Color(0.3f, 0, 0));
        }
        // The gauges: the revs and the boost, small, at the bottom left (the dashboard of a car that stands still).
        if (CarRedline > 0)
        {
            float gx = Mathf.Floor(r.Position.X + 10), gy = Mathf.Floor(r.End.Y - 56);
            PixelFont.Draw(this, "RPM " + (int)(Mathf.Round(CarRpm / 50f) * 50f), new Vector2(gx, gy - 11), Paper, 1, new Color(0, 0, 0));
            DrawRect(new Rect2(gx, gy, 84, 6), new Color(0, 0, 0, 0.7f));
            float fill = Math.Clamp(CarRpm / CarRedline, 0f, 1f);
            DrawRect(new Rect2(gx + 1, gy + 1, 82 * fill, 4), fill > 0.9f ? Red : Gold);
            if (CarBoost > 0.02f)
            {
                PixelFont.Draw(this, "BOOST", new Vector2(gx, gy + 9), Paper, 1, new Color(0, 0, 0));
                DrawRect(new Rect2(gx, gy + 19, 84, 4), new Color(0, 0, 0, 0.7f));
                DrawRect(new Rect2(gx + 1, gy + 20, 82 * Math.Clamp(CarBoost, 0f, 1f), 2), Paper);
            }
        }
        // The data card.
        if (CarSpecAlpha > 0.01f && CarSpecRows.Length > 0)
        {
            float a = CarSpecAlpha;
            float w = Math.Min(r.Size.X - 40, 300), h = 28 + 15 * CarSpecRows.Length;
            var box = new Rect2(Mathf.Floor(cx - w / 2), Mathf.Floor(r.Position.Y + (r.Size.Y - h) / 2 - 14), Mathf.Floor(w), h);      // (whole pixels: the pixel font blurs on halves)
            DrawRect(box, new Color(0.05f, 0, 0, 0.84f * a));
            DrawRect(new Rect2(box.Position.X, box.Position.Y - 2, box.Size.X, 2), new Color(Gold, a));
            DrawRect(new Rect2(box.Position.X, box.End.Y, box.Size.X, 2), new Color(Gold, a));
            PixelFont.DrawCentered(this, CarSpecTitle, cx, box.Position.Y + 8, new Color(Gold, a), 2, new Color(0.3f, 0, 0, a));
            for (int i = 0; i < CarSpecRows.Length; i++)
            {
                string[] kv = CarSpecRows[i].Split('|');
                float y = box.Position.Y + 28 + 15 * i;
                PixelFont.Draw(this, kv[0], new Vector2(box.Position.X + 12, y), new Color(Grey, a), 1, new Color(0, 0, 0, a));
                PixelFont.Draw(this, kv.Length > 1 ? kv[1] : "", new Vector2(box.Position.X + 96, y), new Color(Paper, a), 1, new Color(0, 0, 0, a));
            }
            if (!string.IsNullOrEmpty(CarTourCaption))
                PixelFont.DrawCentered(this, CarTourCaption, cx, box.End.Y + 12, new Color(Gold, a), 1, new Color(0.3f, 0, 0, a));
        }
    }
}
