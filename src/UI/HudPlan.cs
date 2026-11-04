using System;
using Godot;

namespace CCCPRacing.UI;

/// <summary>The norms in the race HUD (LAPS 2, NORM 3/9 0:55.00) and the PLAN FULFILLED screen.</summary>
public partial class Hud
{
    /// <summary>Laps in a norm's window; 0 hides the norms (training, LAN, demo).</summary>
    public int PlanLaps;
    /// <summary>Laps left for the current norm, its step (0-based) of how many, and its time with the allowance.</summary>
    public int PlanLeft, PlanStep, PlanSteps = 9;
    public float PlanTarget = -1;
    /// <summary>The climate's allowance in the norm (a line of its own when above zero).</summary>
    public float PlanClimate;
    /// <summary>The rally's fuel when it is not petrol (a line of its own under the norm; empty: none).</summary>
    public string PlanFuel = "";
    /// <summary>The lap being driven is the window's last: LAPS blinks red.</summary>
    public bool PlanLastLap;
    /// <summary>PLAN FULFILLED screen: laps driven, the norm the run started at (0-based), the lap that did it.</summary>
    public int PlanUsed;
    public float PlanLapTime = -1;
    /// <summary>PLAN FULFILLED screen: the medal line and the next track's sign.</summary>
    public string PlanMedal, PlanNext = "NEXT: KOLKHOZ NO.8";
    /// <summary>PLAN FULFILLED screen: the track's name over the title (null: none).</summary>
    public string PlanTrack;

    /// <summary>Rows the norms add under the lap panel's usual five (the climate, wrecks).</summary>
    int PlanExtraRows => PlanLaps <= 0 ? 0 : (PlanFuel != "" ? 1 : 0) + (PlanClimate > 0.005f ? 1 : 0) + (Wrecks > 0 ? 1 : 0);

    static readonly Color Banner = new(0.62f, 0.06f, 0.04f);

    /// <summary>The norm row's label (NORM 3/10 ), the time follows it.</summary>
    string PlanNormLabel => $"NORM {PlanStep + 1}/{PlanSteps} ";

    /// <summary>The lap panel's width: 104 px, wider when the norm row needs it (NORM 10/10 1:18.50).</summary>
    /// <summary>The lap panel's height: the five rows (a sixth with the training power line) and the plan's extra rows.</summary>
    internal float LapPanelHeight => (Training || PlanLaps > 0 ? 53 : 44) + 9 * PlanExtraRows;

    internal float LapPanelWidth => PlanLaps <= 0 ? 104 : Math.Max(104, Mathf.Ceil(PixelFont.Measure(PlanNormLabel) + 1 + PixelFont.Measure(Race.LapTimer.Format(PlanTarget))) + 10);

    /// <summary>With norms the panel starts with the NORM row; everything else (laps left, TIME, BEST, LAST) sits one row lower than without. Shared by the draw code so no row keeps a stale y.</summary>
    internal const float PlanRowShift = 9;
    internal const float PlanLapsLeftY = 18;

    /// <summary>Lap panel with norms: NORM n/10 and its time on the first row, then the laps left (LAST LAP! blinking on the last). False without norms.</summary>
    bool DrawPlanLines()
    {
        if (PlanLaps <= 0) return false;
        bool blink = (int)(_clock * 3) % 2 == 0;
        string norm = PlanNormLabel;
        PixelFont.Draw(this, norm, new Vector2(9, 8), Red);
        PixelFont.Draw(this, Race.LapTimer.Format(PlanTarget), new Vector2(9 + PixelFont.Measure(norm) + 1, 8), Gold);
        if (PlanLastLap) PixelFont.Draw(this, "LAST LAP!", new Vector2(9, PlanLapsLeftY), blink ? Red : Gold);
        else PixelFont.Draw(this, $"{PlanLeft} LAPS LEFT", new Vector2(9, PlanLapsLeftY), Gold);
        float y = 54;
        if (PlanFuel != "") { PixelFont.Draw(this, PlanFuel, new Vector2(9, y), Gold); y += 9; }
        if (PlanClimate > 0.005f) { PixelFont.Draw(this, $"(CLIMATE +{PlanClimate:0.0})", new Vector2(9, y), Grey); y += 9; }
        if (Wrecks > 0) PixelFont.Draw(this, $"WRECKS: {Wrecks}", new Vector2(9, y), Red);
        return true;
    }

    /// <summary>A filled five-pointed star, point up: five tips around a pentagon.</summary>
    void StarShape(Vector2 c, float r, Color col)
    {
        var outer = new Vector2[5];
        var inner = new Vector2[5];
        for (int i = 0; i < 5; i++)
        {
            float a = -MathF.PI / 2 + i * MathF.Tau / 5;
            outer[i] = c + Vector2.FromAngle(a) * r;
            inner[i] = c + Vector2.FromAngle(a + MathF.PI / 5) * r * 0.4f;
        }
        DrawColoredPolygon(inner, col);
        for (int i = 0; i < 5; i++)
            DrawColoredPolygon(new[] { outer[i], inner[i], inner[(i + 4) % 5] }, col);
    }

    /// <summary>The plan is fulfilled: a sunburst, the order of the Red Banner of Labour, the stats and the next kolkhoz (under construction).</summary>
    void DrawPlanFulfilled()
    {
        var s = Screen;
        float cx = s.X / 2;
        DrawRect(new Rect2(0, 0, s.X, s.Y), Banner);

        // Sunburst rays turning slowly behind the medal.
        var medal = new Vector2(Mathf.Floor(cx), Mathf.Floor(s.Y * 0.2f) + 4);
        const int Rays = 16;
        for (int i = 0; i < Rays; i++)
        {
            float a0 = _clock * 0.08f + i * MathF.Tau / Rays, a1 = a0 + MathF.Tau / Rays * 0.5f;
            float len = s.X;
            DrawColoredPolygon(new[] { medal, medal + Vector2.FromAngle(a0) * len, medal + Vector2.FromAngle(a1) * len },
                new Color(0.78f, 0.12f, 0.06f));
        }
        // Gold bunting top and bottom.
        DrawRect(new Rect2(0, 4, s.X, 2), Gold);
        DrawRect(new Rect2(0, s.Y - 6, s.X, 2), Gold);

        // The medal: ribbon, gold disc, red star.
        float mr = Math.Max(14, Mathf.Floor(s.Y * 0.07f));
        float ribbonTop = 6, ribbonW = Mathf.Floor(mr * 1.3f);
        DrawRect(new Rect2(medal.X - ribbonW / 2, ribbonTop, ribbonW, medal.Y - ribbonTop), Red);
        DrawRect(new Rect2(medal.X - ribbonW / 2 + 3, ribbonTop, 2, medal.Y - ribbonTop), Gold);
        DrawRect(new Rect2(medal.X + ribbonW / 2 - 5, ribbonTop, 2, medal.Y - ribbonTop), Gold);
        DrawCircle(medal, mr + 2, new Color(0.45f, 0.3f, 0.05f));
        DrawCircle(medal, mr, Gold);
        DrawCircle(medal, mr - 3, new Color(0.95f, 0.72f, 0.15f));
        StarShape(medal, mr * 0.78f, new Color(0.45f, 0.05f, 0.03f));
        StarShape(medal - new Vector2(0, 1), mr * 0.7f, Red);

        float y = medal.Y + mr + 10;
        if (PlanTrack != null)
        {
            PixelFont.DrawCentered(this, PlanTrack, cx, y - 2, Paper, 2, new Color(0.3f, 0, 0));
            y += 18;
        }
        int big = PixelFont.Measure("PLAN FULFILLED!", 4) < s.X - 20 ? 4 : 3;
        PixelFont.DrawCentered(this, "PLAN FULFILLED!", cx, y, Gold, big, new Color(0.3f, 0, 0));
        y += 7 * big + 6;
        PixelFont.DrawCentered(this, "ACCORDING TO PLAN", cx, y, Paper, 2, new Color(0.3f, 0, 0));
        y += 22;

        // The report to the ministry.
        int rows = 3 + (EndWrecks > 0 ? 1 : 0) + (PlanMedal != null ? 1 : 0) + (PlanFuel != "" ? 1 : 0);
        var r = new Rect2(Mathf.Floor(cx - 110), y, 220, 8 + rows * 10);
        Panel(r, 0.75f);
        float ry = r.Position.Y + 5;
        Row("LAPS DRIVEN", PlanUsed.ToString(), ref ry);
        if (EndWrecks > 0) Row("WRECKS", EndWrecks.ToString(), ref ry);
        if (PlanFuel != "") Row("FUEL", PlanFuel, ref ry);
        Row("STAKHANOVITE NORM", Race.LapTimer.Format(PlanTarget), ref ry);
        Row("YOUR LAP", Race.LapTimer.Format(PlanLapTime), ref ry);
        if (PlanMedal != null) Row("MEDAL", PlanMedal, ref ry);

        // Next kolkhoz: hazard stripes and the sign.
        float by = r.End.Y + 8;
        const float Bh = 15;
        DrawRect(new Rect2(0, by, s.X, Bh), new Color(0.1f, 0.08f, 0.05f));
        for (float x = -Bh; x < s.X; x += 16)
            DrawColoredPolygon(new[] { new Vector2(x, by + Bh), new Vector2(x + Bh, by), new Vector2(x + Bh + 8, by), new Vector2(x + 8, by + Bh) }, Gold);
        string Next = PlanNext;
        float nw = PixelFont.Measure(Next) + 12;
        DrawRect(new Rect2(Mathf.Floor(cx - nw / 2), by + 2, Mathf.Ceil(nw), Bh - 4), new Color(0.1f, 0.08f, 0.05f));
        PixelFont.DrawCentered(this, Next, cx, by + 4, Gold);

        if ((int)(_clock * 2) % 2 == 0)
            PixelFont.DrawCentered(this, AnyKey, cx, Math.Min(by + Bh + 8, s.Y - 17), Paper);

        void Row(string label, string value, ref float yy)
        {
            PixelFont.Draw(this, label, new Vector2(r.Position.X + 10, yy), Grey);
            PixelFont.Draw(this, value, new Vector2(r.End.X - 10 - PixelFont.Measure(value), yy), Paper);
            yy += 10;
        }
    }
}
