using System;
using Godot;

namespace CCCPRacing.UI;

/// <summary>Quitting from the title (RaceScene.Resign.cs): the resignation window, the shot, the dark.</summary>
public partial class Hud
{
    /// <summary>Seconds since the resignation was received (-1: none).</summary>
    public float ResignOut = -1f;
    /// <summary>When all but the stain fades to black: just after the shot (ResignClick + HammerSound.ShotAt + 0.15, RaceScene.Resign.cs).</summary>
    public const float ResignFadeAt = 2.45f, ResignFadeLen = 0.5f;
    /// <summary>The stain fades after the words are gone (RaceScene.ResignQuit waits for it).</summary>
    public const float ResignBloodFadeAt = ResignFadeAt + ResignFadeLen, ResignBloodFadeLen = 0.8f;

    /// <summary>
    /// A small pixel splatter as on a wall, in dark red: a blotch where the shot hit, drops thrown out
    /// round it (the far ones as short streaks along their flight) in about a tenth of a second, and
    /// a few drips that run slowly down. Always the same pattern (a fixed seed); fade 1..0 takes it away.
    /// </summary>
    void DrawBlood(Vector2 at, float since, float fade, float spread = 1f)
    {
        if (fade <= 0) return;
        var rng = new Random(1937);
        float fly = Mathf.Clamp(since / 0.1f, 0, 1);
        fly = 1 - (1 - fly) * (1 - fly);        // fast first, then settling
        Color Tone(float shade, float alpha) => new(shade, 0.02f, 0.03f, alpha * fade);
        // The blotch.
        foreach (var (dx, dy) in new[] { (0, 0), (-1, 0), (1, 0), (0, -1), (0, 1), (-1, -1), (1, 1), (2, 0), (-2, 1), (0, 2) })
            DrawRect(new Rect2(at.X + dx, at.Y + dy, 1, 1), Tone(0.5f, 0.9f * Mathf.Clamp(fly * 4, 0, 1)));
        // The spray: most of it thrown sideways and up, little straight down.
        for (int i = 0; i < 38; i++)
        {
            float ang = (float)(rng.NextDouble() * Math.Tau);
            float dist = (4 + (float)Math.Pow(rng.NextDouble(), 1.5) * 32) * spread;
            float shade = 0.38f + (float)rng.NextDouble() * 0.3f;
            var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            var p = at + dir * dist * fly;
            int len = dist > 18 && rng.NextDouble() < 0.6 ? 2 : 1;      // a far drop smears along its way
            for (int k = 0; k < len; k++)
            {
                var q = p - dir * k;
                DrawRect(new Rect2(Mathf.Floor(q.X), Mathf.Floor(q.Y), 1, 1), Tone(shade, 0.85f));
            }
            if (dist < 12 && rng.NextDouble() < 0.3) DrawRect(new Rect2(Mathf.Floor(p.X), Mathf.Floor(p.Y), 2, 2), Tone(shade, 0.85f));
        }
        // Drips that run down the wall from the blotch.
        for (int i = 0; i < 4; i++)
        {
            float dx = Mathf.Round((float)(rng.NextDouble() * 10 - 5)), run = 3 + (float)rng.NextDouble() * 5;
            int len = 1 + (int)Mathf.Min(since * run, 16);
            DrawRect(new Rect2(Mathf.Floor(at.X + dx), Mathf.Floor(at.Y + 2), 1, len), Tone(0.42f, 0.75f));
        }
    }

    void DrawResign()
    {
        if (ResignOut < 0) return;
        var s = Screen;
        float cx = s.X / 2;
        const string L1 = "THE PARTY HAS RECEIVED", L2 = "YOUR RESIGNATION.";
        float shotAt = Race.RaceScene.ResignClick + Audio.HammerSound.ShotAt;
        bool shot = ResignOut >= shotAt;
        // After the shot all but the stain goes to the dark: the dimming of the title, then the window and its words.
        float gone = Mathf.Clamp((ResignOut - ResignFadeAt) / ResignFadeLen, 0, 1);
        DrawRect(new Rect2(0, 0, s.X, s.Y), new Color(0, 0, 0, Mathf.Lerp(0.45f * Mathf.Clamp(ResignOut / 0.4f, 0, 1), 1f, gone)));
        float w = PixelFont.Measure(L1, 2) + 28, h = 58;
        var r = new Rect2(Mathf.Floor(cx - w / 2), Mathf.Floor(s.Y / 2 - h / 2), w, h);
        float a = (1f - gone) * Mathf.Clamp(ResignOut / 0.25f, 0, 1);
        if (a > 0)
        {
            Panel(r, 0.92f * a);
            var shadow = new Color(0.35f, 0, 0, a);
            PixelFont.DrawCentered(this, L1, cx, r.Position.Y + 12, new Color(Gold, a), 2, shadow);
            // The shot takes the full stop off the end of the text: the stain is left in its place.
            float left = Mathf.Floor(cx - PixelFont.Measure(L2, 2) / 2f);
            PixelFont.Draw(this, shot ? L2[..^1] : L2, new Vector2(left, r.Position.Y + 32), new Color(Gold, a), 2, shadow);
        }
        // The stain stays in the dark after the words have faded, and goes last.
        if (shot)
        {
            float dotX = Mathf.Floor(cx - PixelFont.Measure(L2, 2) / 2f) + (L2.Length - 1) * PixelFont.Advance * 2 + 4;
            DrawBlood(new Vector2(dotX, r.Position.Y + 32 + 12), ResignOut - shotAt,
                1f - Mathf.Clamp((ResignOut - ResignBloodFadeAt) / ResignBloodFadeLen, 0, 1), 1.5f);
        }
    }
}
