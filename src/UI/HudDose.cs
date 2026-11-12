using System;
using Godot;

namespace CCCPRacing.UI;

/// <summary>The rally's dose gauge (ATOMGRAD-4), left of the FUEL box: mSv and six segments, red and blinking over the limit.</summary>
public partial class Hud
{
    /// <summary>Dose as a share of the limit; negative hides the gauge (no radiation, not the rally).</summary>
    public float Dose = -1f;
    public float DoseMsv, DoseRate;
    /// <summary>The radiation power on top of the engine (0..0.15): a hint beside the gauge while it adds power.</summary>
    public float RadBoost;
    /// <summary>The wash or the dosimetrist's check is on: WASHING over the gauge.</summary>
    public bool Washing;

    void DrawDoseGauge(Rect2 speedo)
    {
        if (Dose < 0) return;
        float x = speedo.Position.X - (EngineHealth >= 0 ? 50 : 0) - (Fuel >= 0 ? 40 : 0) - 40;
        var r = new Rect2(x, speedo.Position.Y, 36, speedo.Size.Y);
        Panel(r);
        float f = Math.Clamp(Dose, 0f, 1.2f);
        bool over = f >= 1f, blink = (int)(_clock * 3) % 2 == 0;
        Color c = over ? Red : f >= 0.7f ? FuelAmber : Healthy;
        float cx = r.Position.X + r.Size.X / 2;
        // A little trefoil before the word: a dot and three blades.
        var t = new Vector2(r.Position.X + 5, r.Position.Y + 7);
        Color tc = over && blink ? Red : DoseRate > 0.2f && blink ? Gold : Grey;
        DrawRect(new Rect2(t.X - 0.5f, t.Y - 0.5f, 1, 1), tc);
        DrawRect(new Rect2(t.X - 1, t.Y - 3, 2, 2), tc);
        DrawRect(new Rect2(t.X - 3, t.Y + 1, 2, 2), tc);
        DrawRect(new Rect2(t.X + 1, t.Y + 1, 2, 2), tc);
        PixelFont.Draw(this, "DOSE", new Vector2(r.Position.X + 9, r.Position.Y + 4), Grey);
        string l = $"{DoseMsv:0}MSV";
        if (!over || blink) PixelFont.DrawCentered(this, l, cx, r.Position.Y + 14, over ? Red : Paper);
        const int Segs = 6;
        int lit = (int)MathF.Ceiling(Math.Min(f, 1f) * Segs);
        for (int i = 0; i < Segs; i++)
            DrawRect(new Rect2(r.Position.X + 3 + i * 5, r.Position.Y + 26, 4, 5), i < lit && (!over || blink) ? c : new Color(c, 0.18f));
        if (Washing) PixelFont.Draw(this, "WASHING", new Vector2(r.Position.X, r.Position.Y - 9), blink ? Gold : Paper);
        else if (RadBoost >= 0.005f) PixelFont.Draw(this, $"RAD +{MathF.Round(RadBoost * 100):0}%", new Vector2(r.Position.X, r.Position.Y - 9), Gold);
    }
}
