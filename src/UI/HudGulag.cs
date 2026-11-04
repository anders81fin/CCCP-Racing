using System;
using Godot;

namespace CCCPRacing.UI;

/// <summary>Engine health gauge and the wreck countdown in the race HUD, and the GULAG game-over screen.</summary>
public partial class Hud
{
    /// <summary>Engine health 0..1 beside the speedo; negative hides it (training, demo).</summary>
    public float EngineHealth = -1f;
    /// <summary>GULAG screen: why, and how far the driver got.</summary>
    public string GulagReason = "";
    /// <summary>A bronze rally medal can unlock the next track for STAKHANOV LAPS.</summary>
    public string GulagAfter = "";
    public int GulagLaps;
    public float GulagRunTime, GulagLastLap = -1;
    /// <summary>Seconds until the new Pyatyorka while the wreck stands; 0 hides it.</summary>
    public int WreckCountdown;
    /// <summary>Wrecks in this run: a line in the plan panel (0 hides it).</summary>
    public int Wrecks;
    /// <summary>Wrecks for the end screens' stats (GULAG and PLAN FULFILLED; 0 leaves the row out).</summary>
    public int EndWrecks;

    static readonly Color Healthy = new(0.5f, 0.8f, 0.4f);

    /// <summary>ENGINE box left of the speedo: percent and eight segments, red and blinking at 25 %.</summary>
    void DrawEngineGauge(Rect2 speedo)
    {
        if (EngineHealth < 0) return;
        var r = new Rect2(speedo.Position.X - 50, speedo.Position.Y, 46, speedo.Size.Y);
        Panel(r);
        float h = Math.Clamp(EngineHealth, 0f, 1f);
        bool dead = h <= 0f, low = h <= 0.25f;
        Color c = low ? Red : h <= 0.5f ? Gold : Healthy;
        PixelFont.DrawCentered(this, "ENGINE", r.Position.X + r.Size.X / 2, r.Position.Y + 4, Grey);
        string pct = dead ? "DEAD" : $"{MathF.Ceiling(h * 100):0}%";
        if (!low || dead || MathF.Sin(_engineWarningPhase) >= 0)
            PixelFont.DrawCentered(this, pct, r.Position.X + r.Size.X / 2, r.Position.Y + 14, low ? Red : Paper);
        const int Segs = 8;
        int lit = (int)MathF.Ceiling(h * Segs);
        for (int i = 0; i < Segs; i++)
            DrawRect(new Rect2(r.Position.X + 4 + i * 5, r.Position.Y + 26, 4, 5), i < lit ? c : new Color(c, 0.18f));
    }

    /// <summary>NEW PYATYORKA IN n, big, below the messages.</summary>
    void DrawWreckCountdown()
    {
        if (WreckCountdown <= 0) return;
        var s = Screen;
        string text = $"NEW PYATYORKA IN {WreckCountdown}";
        int scale = PixelFont.Measure(text, 2) < s.X - 16 ? 2 : 1;
        PixelFont.DrawCentered(this, text, s.X / 2, Mathf.Floor(s.Y * 0.62f), Gold, scale, new Color(0.35f, 0, 0));
    }

    /// <summary>Game over: barbed wire, watchtowers, snow, the sentence and the stats.</summary>
    void DrawGulag()
    {
        var s = Screen;
        float cx = s.X / 2;
        DrawRect(new Rect2(0, 0, s.X, s.Y), new Color(0.05f, 0.06f, 0.08f, 0.94f));

        // Kolyma snow, drifting.
        var rng = new RandomNumberGenerator { Seed = 1937 };
        for (int i = 0; i < 90; i++)
        {
            float x = rng.RandfRange(0, s.X), speed = rng.RandfRange(8, 22), y0 = rng.RandfRange(0, s.Y);
            float y = (y0 + _clock * speed) % s.Y;
            x = (x + MathF.Sin(_clock * 0.8f + i) * 6f + s.X) % s.X;
            DrawRect(new Rect2(Mathf.Floor(x), Mathf.Floor(y), 1, 1), new Color(0.9f, 0.92f, 0.95f, rng.RandfRange(0.4f, 0.9f)));
        }

        // Watchtowers in the corners.
        Tower(18, s.Y - 30);
        Tower(s.X - 18, s.Y - 30);
        // Barbed wire top and bottom.
        Wire(14);
        Wire(s.Y - 20);

        float y1 = Mathf.Floor(s.Y * 0.1f) + 6;
        int big = s.X >= 400 ? 8 : 6;
        PixelFont.DrawCentered(this, "GULAG", cx, y1, Red, big, new Color(0.25f, 0, 0));
        float y2 = y1 + 7 * big + 12;
        string reason = GulagReason ?? "";
        int rs = PixelFont.Measure(reason, 2) < s.X - 40 ? 2 : 1;
        PixelFont.DrawCentered(this, reason, cx, y2, Paper, rs, new Color(0.3f, 0, 0));
        y2 += 7 * rs + 8;
        string after = string.IsNullOrEmpty(GulagAfter) ? "10 YEARS IN KOLYMA" : GulagAfter;
        int afterScale = PixelFont.Measure(after, 2) < s.X - 24 ? 2 : 1;
        PixelFont.DrawCentered(this, after, cx, y2, Gold, afterScale, new Color(0.3f, 0, 0));

        // The case file.
        float py = y2 + 24;
        var r = new Rect2(Mathf.Floor(cx - 105), py, 210, EndWrecks > 0 ? 50 : 40);
        Panel(r, 0.7f);
        Row("LAPS COMPLETED", GulagLaps.ToString(), r.Position.Y + 6);
        Row("TIME IN THE RACE", FormatLong(GulagRunTime), r.Position.Y + 16);
        Row("LAST LAP", GulagLastLap > 0 ? Race.LapTimer.Format(GulagLastLap) : "-", r.Position.Y + 26);
        if (EndWrecks > 0) Row("WRECKS", EndWrecks.ToString(), r.Position.Y + 36);

        if ((int)(_clock * 2) % 2 == 0)
            PixelFont.DrawCentered(this, AnyKey, cx, Math.Min(r.End.Y + 12, s.Y - 34), Gold);

        void Row(string label, string value, float y)
        {
            PixelFont.Draw(this, label, new Vector2(r.Position.X + 10, y), Grey);
            PixelFont.Draw(this, value, new Vector2(r.End.X - 10 - PixelFont.Measure(value), y), Paper);
        }
    }

    static string FormatLong(float t)
    {
        int m = (int)(t / 60);
        return $"{m}:{t - m * 60:00.0}";
    }

    void Wire(float y)
    {
        var s = Screen;
        var wire = new Color(0.55f, 0.55f, 0.58f);
        DrawRect(new Rect2(0, y, s.X, 1), wire);
        DrawRect(new Rect2(0, y + 4, s.X, 1), wire);
        for (float x = 4; x < s.X; x += 12)
        {
            // Barbs: little crosses on each strand.
            DrawRect(new Rect2(x - 1, y - 1, 1, 1), wire);
            DrawRect(new Rect2(x + 1, y + 1, 1, 1), wire);
            DrawRect(new Rect2(x + 5, y + 3, 1, 1), wire);
            DrawRect(new Rect2(x + 7, y + 5, 1, 1), wire);
        }
        for (float x = 30; x < s.X; x += 60)
            DrawRect(new Rect2(x, y - 3, 2, 11), new Color(0.3f, 0.24f, 0.18f));
    }

    void Tower(float cx, float ground)
    {
        var wood = new Color(0.22f, 0.18f, 0.14f);
        DrawRect(new Rect2(cx - 9, ground - 40, 2, 40), wood);
        DrawRect(new Rect2(cx + 7, ground - 40, 2, 40), wood);
        DrawRect(new Rect2(cx - 11, ground - 52, 22, 12), wood);
        DrawRect(new Rect2(cx - 8, ground - 49, 16, 5), new Color(0.9f, 0.85f, 0.5f, 0.8f));    // lit window
        DrawRect(new Rect2(cx - 13, ground - 55, 26, 3), new Color(0.3f, 0.26f, 0.22f));
        // Searchlight beam sweeping.
        float a = MathF.Sin(_clock * 0.7f + cx) * 0.5f + (cx < Screen.X / 2 ? 0.6f : MathF.PI - 0.6f);
        var from = new Vector2(cx, ground - 47);
        DrawLine(from, from + Vector2.FromAngle(a) * 80f, new Color(1f, 0.95f, 0.7f, 0.12f), 6f);
    }
}
