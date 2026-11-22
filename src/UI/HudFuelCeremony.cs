using System;
using Godot;
using CCCPRacing.Race;

namespace CCCPRacing.UI;

// The ceremony of a newly opened fuel (the owner's order of 2026-11-22): bigger than the opening of a track and than a gold on a single track, because it changes the car and every norm. A full-screen card over the title that
// stays until a key is pressed (RaceScene.FuelCeremony.cs): the fuel's name large in its colour, the rays, the engine's own numbers, and what opened it.
public partial class Hud
{
    public string FuelCeremonyName, FuelCeremonyKeyLine, FuelCeremonySpec;
    public float FuelCeremonyAge;
    public Color FuelCeremonyColor = Colors.White;
    public bool FuelCeremonyCanClose;

    public bool FuelCeremonyOn => FuelCeremonyName != null;

    public void BeginFuelCeremony(FuelGrade g)
    {
        FuelCeremonyName = FuelGrades.Names[(int)g];
        FuelCeremonyColor = g switch { FuelGrade.Petrol => Gold, FuelGrade.Rocket => Red, _ => Paper };
        FuelCeremonyKeyLine = g switch
        {
            FuelGrade.Petrol => "SILVER ON THE FIRST THREE TRACKS ON DIESEL",
            FuelGrade.Rocket => "SILVER ON ALL FOUR TRACKS ON PETROL",
            _ => "SILVER ON ALL FOUR TRACKS ON JET FUEL",
        };
        FuelCeremonySpec = FuelGrades.CardLine((int)g);
        FuelCeremonyAge = 0; FuelCeremonyCanClose = false;
        GoldRewardFanfare(false);
    }

    public void EndFuelCeremony() => FuelCeremonyName = null;

    /// <summary>Called every frame while it is on: the fanfare's second part when the name arrives.</summary>
    public void FuelCeremonyTick(float dt)
    {
        float before = FuelCeremonyAge;
        FuelCeremonyAge += dt;
        if (before < 2.6f && FuelCeremonyAge >= 2.6f) GoldRewardFanfare(true);
        FuelCeremonyCanClose = FuelCeremonyAge >= 4f;
    }

    void DrawFuelCeremony()
    {
        float age = FuelCeremonyAge, cx = Screen.X / 2;
        Color c = FuelCeremonyColor;
        bool named = age >= 2.6f, ready = FuelCeremonyCanClose;
        float fade = Mathf.SmoothStep(0, 1, Math.Clamp(age / 1.0f, 0f, 1f));
        var panel = new Rect2(6, 6, Screen.X - 12, Screen.Y - 12);
        // The new car is in the middle of the screen (the world's own car, circled by the camera); the dark is only a band at the top and the bottom for the text, and a veil that clears in the first second.
        DrawRect(new Rect2(Vector2.Zero, Screen), new Color(0.02f, 0.01f, 0, 0.85f * (1 - fade)));
        float topBand = 34, bottomBand = Math.Min(Screen.Y * 0.36f, 84);
        DrawRect(new Rect2(0, 0, Screen.X, topBand), new Color(0.02f, 0.01f, 0, 0.82f));
        DrawRect(new Rect2(0, Screen.Y - bottomBand, Screen.X, bottomBand), new Color(0.02f, 0.01f, 0, 0.82f));
        DrawRect(new Rect2(0, topBand, Screen.X, 1), new Color(c, 0.8f));
        DrawRect(new Rect2(0, Screen.Y - bottomBand - 1, Screen.X, 1), new Color(c, 0.8f));
        DrawRect(panel, c, false, 2);
        DrawRect(new Rect2(panel.Position + Vector2.One * 5, panel.Size - Vector2.One * 10), new Color(c, 0.45f), false, 1);
        float top = panel.Position.Y;
        DrawTypewriter("NEW FUEL AUTHORIZED", cx, top + 12, Gold, age - 0.3f, RewardTypewriterRate, PixelFont.Measure("NEW FUEL AUTHORIZED", 2) < panel.Size.X - 24 ? 2 : 1, HeadingShadow);
        float by = Screen.Y - bottomBand;
        int nameScale = PixelFont.Measure(FuelCeremonyName, 3) < panel.Size.X - 30 ? 3 : 2;
        if (named) PixelFont.DrawCentered(this, FuelCeremonyName, cx, by + 6, c, nameScale, HeadingShadow);
        else DrawTypewriter("???", cx, by + 6, Grey, 0, RewardTypewriterRate, nameScale, HeadingShadow);
        if (named)
        {
            PixelFont.DrawCentered(this, FuelCeremonySpec, cx, by + 6 + 7 * nameScale + 7, Paper);
            PixelFont.DrawCentered(this, FuelCeremonyKeyLine, cx, by + 6 + 7 * nameScale + 17, Gold);
        }
        if (ready && (int)(_clock * 2) % 2 == 0) PixelFont.DrawCentered(this, AnyKey, cx, panel.End.Y - 12, Paper);
        // sparks fly up the two sides once the name has arrived
        if (named)
        {
            float burst = age - 2.6f;
            for (int i = 0; i < 56; i++)
            {
                float hash = Track.Noise.Hash01(i, 7, 1981), t = burst - hash * 0.3f;
                if (t < 0) continue;
                bool left = (i & 1) == 0;
                float x = (left ? panel.Position.X + 14 : panel.End.X - 14) + (left ? 1 : -1) * (6 + hash * 24) * t;
                float y = by - 4 - (40 + hash * 90) * t + 30 * t * t;
                if (y < topBand) continue;
                Vector2 at = new(x, y), direction = Vector2.FromAngle(i + t * 5);
                DrawLine(at, at + direction * 4, new Color(i % 3 == 0 ? Paper : c, 1 - Mathf.SmoothStep(0.9f, 1.8f, t)), 2);
            }
        }
    }
}
