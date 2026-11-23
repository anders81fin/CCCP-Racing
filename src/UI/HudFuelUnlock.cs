using System;
using Godot;

namespace CCCPRacing.UI;

/// <summary>
/// The fuel's unlocking (the owner's order, 2026-11-23): when the run that opened a new fuel is left, the CHOOSE FUEL card comes up on that fuel, and a padlock over the card shakes, cracks and bursts
/// open (brass fanfare, rays, shards, a flash across the card) before the card is given back. Modelled on the track's opening (HudLapsReward.cs), grander. Driven by RaceScene.FuelUnlock.cs.
/// </summary>
public partial class Hud
{
    /// <summary>The option that is being unlocked (-1: none) and the seconds since it began.</summary>
    public int FuelUnlockIndex = -1;
    public float FuelUnlockAge;
    public string FuelUnlockName = "";
    public const float FuelUnlockBreak = 2.3f, FuelUnlockEnd = 5.4f;
    Rect2 _fuelUnlockOption;
    bool _fuelUnlockBroken;

    public void BeginFuelUnlock(int index, string name)
    {
        FuelUnlockIndex = index; FuelUnlockName = name; FuelUnlockAge = 0; _fuelUnlockBroken = false;
        GoldRewardFanfare(false);
    }

    public void EndFuelUnlock() { FuelUnlockIndex = -1; }

    /// <summary>Called from DrawCard with the card's frame, once the options are laid out. The padlock is drawn big over the middle of the screen, under a dimmed picture, so that nothing of the menu is in its way.</summary>
    void DrawFuelUnlock(Rect2 card, float optionY)
    {
        if (FuelUnlockIndex < 0) return;
        float age = FuelUnlockAge, cx = Screen.X / 2;
        if (!_fuelUnlockBroken && age >= FuelUnlockBreak) { _fuelUnlockBroken = true; GoldRewardFanfare(true); }
        const float S = 2f;
        var centre = new Vector2(cx, 112);
        var iron = new Color(0.62f, 0.63f, 0.68f);
        var rust = new Color(0.30f, 0.28f, 0.28f);
        float top = 58;
        float dim = age < FuelUnlockBreak ? 0.82f : 0.82f * (1 - Mathf.Clamp((age - FuelUnlockBreak - 1.5f) / 1.8f, 0, 1));
        DrawRect(new Rect2(0, top, Screen.X, Screen.Y - top), new Color(0.02f, 0.02f, 0.02f, dim));
        if (age < FuelUnlockBreak)
        {
            DrawRect(_fuelUnlockOption, new Color(0.04f, 0.04f, 0.04f, 0.85f));      // (the unlit option, dimmed under the picture)
            float t = age / FuelUnlockBreak;
            float amp = 0.5f + 4.5f * t * t;
            var shake = new Vector2(MathF.Sin(age * 47f) * amp, MathF.Cos(age * 61f) * amp * 0.5f);
            if (t > 0.25f && (age * 9f) % 1f < 0.14f) DrawRect(new Rect2(centre.X - 34, centre.Y - 40, 68, 76), new Color(Gold, 0.08f + 0.22f * t));
            DrawPadlock(centre + shake, S, 0f, 0f, iron, rust, t);
            PixelFont.DrawCentered(this, FuelUnlockName + " IS SEALED", cx, centre.Y + 32, Grey, 1);
            return;
        }
        float b = age - FuelUnlockBreak;
        float flash = 1 - Mathf.Clamp(b / 0.5f, 0, 1);
        if (flash > 0) DrawRect(new Rect2(0, 0, Screen.X, Screen.Y), new Color(1f, 0.93f, 0.7f, flash * 0.8f));
        float rayFade = 1 - Mathf.Clamp((b - 0.5f) / 2.4f, 0, 1);
        for (int i = 0; i < 20; i++)
        {
            float angle = i * MathF.Tau / 20 + b * 0.3f;
            var dir = Vector2.FromAngle(angle);
            float len = 30 + 120 * (1 - MathF.Exp(-b * 2.2f));
            DrawLine(centre + dir * 14, centre + dir * len, new Color(i % 2 == 0 ? Gold : Silver, 0.6f * rayFade), 2f);
        }
        DrawPadlock(centre, S, b, 1f, iron, rust, 1f);
        for (int i = 0; i < 48; i++)
        {
            var dir = Vector2.FromAngle(i * MathF.Tau / 48 + (i % 3) * 0.07f);
            float speed = 50 + (i % 5) * 26;
            var at = centre + dir * speed * b + new Vector2(0, 90 * b * b);
            float fade = 1 - Mathf.Clamp(b / 1.8f, 0, 1);
            if (fade > 0) DrawRect(new Rect2(at, new Vector2(2 + i % 3, 2 + i % 2)), new Color(i % 3 == 0 ? Silver : Gold, fade));
        }
        float textFade = 1 - Mathf.Clamp((b - 1.1f) / 0.8f, 0, 1);      // (the words go before the picture comes back, so they never lie over the card's own)
        if (textFade > 0) DrawTypewriter(FuelUnlockName + " UNLOCKED", cx, centre.Y + 32, textFade < 1 ? new Color(Gold, textFade) : Gold, Math.Max(0, b - 0.3f), HeadingRate, 2, textFade < 1 ? null : HeadingShadow);
        // the option lights up, a gold glint running along the card
        float pulse = 0.5f + 0.5f * MathF.Sin(b * 8f);
        float glow = Mathf.Clamp(0.55f - b * 0.15f, 0.1f, 0.55f) * (0.6f + 0.4f * pulse) * Mathf.Clamp(b / 0.8f, 0, 1);
        DrawRect(_fuelUnlockOption, new Color(Gold, glow));
        if (b > 1.6f && b < 2.6f)
        {
            float x = Mathf.Lerp(card.Position.X, card.End.X, (b - 1.6f));
            DrawRect(new Rect2(x, card.Position.Y, 5, card.Size.Y), new Color(Gold, 0.8f * (1 - (b - 1.6f))));
        }
    }

    /// <summary>The padlock, 14 x S high: crack lines before the break (cracks 0..1), the halves and the shackle flung apart after it (open 1, b seconds since).</summary>
    void DrawPadlock(Vector2 c, float S, float b, float open, Color iron, Color rust, float cracks)
    {
        float fall = open * b;
        var shackleAt = c + new Vector2(open * 10 * S * Math.Min(b, 1.2f), (-8 - open * (40 * b - 90 * b * b * 0.5f)) * S);
        float rot = open * b * 3f;
        DrawArc(shackleAt, 8 * S, MathF.PI + rot, MathF.Tau + rot, 20, rust, 4f * S);
        DrawArc(shackleAt, 8 * S, MathF.PI + rot, MathF.Tau + rot, 20, iron, 2f * S);
        var left = new Rect2(c.X - 12 * S - open * 18 * S * Math.Min(b, 1.5f), c.Y - 8 * S + fall * fall * 30 * S, 12 * S, 16 * S);
        var right = new Rect2(c.X + open * 18 * S * Math.Min(b, 1.5f), c.Y - 8 * S + fall * fall * 36 * S, 12 * S, 16 * S);
        foreach (var half in new[] { left, right })
        {
            DrawRect(half, rust);
            DrawRect(new Rect2(half.Position + new Vector2(S, S), half.Size - new Vector2(2 * S, 2 * S)), iron);
            DrawRect(new Rect2(half.Position + new Vector2(S, S), new Vector2(half.Size.X - 2 * S, S)), new Color(1, 1, 1, 0.25f));
        }
        if (open < 0.5f)
        {
            DrawRect(new Rect2(c.X - 1.5f * S, c.Y - 2 * S, 3 * S, 6 * S), rust);      // the keyhole
            int n = (int)(cracks * 7);
            for (int i = 0; i < n; i++)
            {
                float y = c.Y - 7 * S + i * 2.4f * S;
                DrawLine(new Vector2(c.X + (i % 2 == 0 ? -5 : 4) * S, y), new Vector2(c.X + (i % 2 == 0 ? 4 : -4) * S, y + 2.4f * S), new Color(Gold, 0.95f), 1.5f);
            }
        }
    }
}
