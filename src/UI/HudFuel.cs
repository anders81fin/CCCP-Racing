using System;
using Godot;

namespace CCCPRacing.UI;

/// <summary>The rally's fuel gauge, left of the ENGINE box: litres and six segments, red and blinking when low.</summary>
public partial class Hud
{
    /// <summary>Tank 0..1; negative hides the gauge (Stakhanov, LAN, the title).</summary>
    public float Fuel = -1f;
    public float FuelLitres;
    /// <summary>Fuel is pouring in at the depot: REFUELLING over the gauge.</summary>
    public bool Refuelling;
    /// <summary>Seconds of OCTANE BOOSTER left (0: none): counted down over the gauge.</summary>
    public float FuelBoost;

    static readonly Color FuelAmber = new(0.95f, 0.62f, 0.15f);

    void DrawFuelGauge(Rect2 speedo)
    {
        if (Fuel < 0) return;
        float x = speedo.Position.X - (EngineHealth >= 0 ? 50 : 0) - 40;
        var r = new Rect2(x, speedo.Position.Y, 36, speedo.Size.Y);
        Panel(r);
        float f = Math.Clamp(Fuel, 0f, 1f);
        bool low = f < 0.2f, blink = f <= 0.5f ? MathF.Sin(_fuelWarningPhase) >= 0 : (int)(_clock * 3) % 2 == 0;
        Color c = low ? Red : f < 0.4f ? FuelAmber : Healthy;
        float cx = r.Position.X + r.Size.X / 2;
        // A little jerrycan before the word.
        var can = new Vector2(r.Position.X + 3, r.Position.Y + 4);
        DrawRect(new Rect2(can.X, can.Y + 1, 4, 6), low && blink ? Red : Grey);
        DrawRect(new Rect2(can.X + 1, can.Y, 2, 1), low && blink ? Red : Grey);
        PixelFont.Draw(this, "FUEL", new Vector2(r.Position.X + 9, r.Position.Y + 4), Grey);
        string l = f <= 0f ? "EMPTY" : $"{MathF.Ceiling(FuelLitres):0}L";
        if (!low || f <= 0f || blink)
            PixelFont.DrawCentered(this, l, cx, r.Position.Y + 14, low ? Red : Paper);
        const int Segs = 6;
        int lit = (int)MathF.Ceiling(f * Segs);
        for (int i = 0; i < Segs; i++)
            DrawRect(new Rect2(r.Position.X + 3 + i * 5, r.Position.Y + 26, 4, 5), i < lit && (!low || blink) ? c : new Color(c, 0.18f));
        if (Refuelling) PixelFont.Draw(this, "REFUELLING", new Vector2(r.Position.X, r.Position.Y - 9), blink ? Gold : Paper);
        else if (FuelBoost > 0) PixelFont.Draw(this, $"OCTANE BOOSTER  {MathF.Ceiling(FuelBoost):0}", new Vector2(r.Position.X, r.Position.Y - 9), FuelBoost < 4f && blink ? Red : Gold);
    }
}
