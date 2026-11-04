using System;
using System.Collections.Generic;
using Godot;

namespace CCCPRacing.UI;

/// <summary>
/// Revenge alerts: a banner of their own above the usual message line (so a fence or a crash
/// comment cannot wipe out "THAT WAS THE PILOT'S MOTHER!"), and small top-right indicators
/// while a revenge is on.
/// </summary>
public partial class Hud
{
    /// <summary>"MIK: PERSONAL" while the pilot's revenge is on, else null.</summary>
    public string RevengeJet;
    /// <summary>"TANKS: N SHOTS LEFT" while the tanks chase the car, else null.</summary>
    public string RevengeTanks;
    /// <summary>Screen points of missiles flying at the car (far off the edge when behind the camera).</summary>
    public readonly List<Vector2> MissileMarks = new();

    void DrawRevenge()
    {
        var s = Screen;
        float y = 20;
        foreach (var text in new[] { RevengeJet, RevengeTanks })
        {
            if (text == null) continue;
            float w = PixelFont.Measure(text) + 8;
            var r = new Rect2(Mathf.Floor(s.X - w - 4), y, w, 12);
            Panel(r, 0.75f);
            PixelFont.Draw(this, text, r.Position + new Vector2(4, 3), (int)(_clock * 2) % 2 == 0 ? Red : Gold);
            y += 14;
        }

        DrawMissileArrows();
        if (_top is not { } al) return;
        float age = al.Time;
        if (age < 0.6f && (int)(age * 10) % 2 == 1) return;   // blink in
        // Big when it fits, but not over the lap panel once the WRECKS line has made it taller.
        float w2 = PixelFont.Measure(al.Text, 2);
        int scale = w2 < s.X - 16 && (PlanExtraRows == 0 || s.X / 2 - w2 / 2 > 112) ? 2 : 1;
        float ay = Mathf.Floor(s.Y * 0.2f);
        PixelFont.DrawCentered(this, al.Text, s.X / 2, ay, al.Color, scale, new Color(0.35f, 0, 0));
        if (al.Sub != null) PixelFont.DrawCentered(this, al.Sub, s.X / 2, ay + 7 * scale + 5, Paper);
    }

    /// <summary>A blinking arrow per missile: at the screen edge towards it when it is off screen,
    /// just short of it when it is in view.</summary>
    void DrawMissileArrows()
    {
        var s = Screen;
        Vector2 c = s / 2;
        const float margin = 10f;
        Color col = (int)(_clock * 8) % 2 == 0 ? Red : Gold;
        foreach (var p in MissileMarks)
        {
            if (!float.IsFinite(p.X) || !float.IsFinite(p.Y)) continue;
            Vector2 v = p - c;
            if (v.LengthSquared() < 1f) v = Vector2.Up;
            Vector2 n = v.Normalized(), tip;
            bool inView = p.X > margin && p.X < s.X - margin && p.Y > margin && p.Y < s.Y - margin;
            if (inView) tip = p - n * 6f;
            else
            {
                // Where the line from the centre towards it leaves the screen, inset a little.
                float t = Math.Min((c.X - margin) / Math.Max(Math.Abs(v.X), 1e-3f), (c.Y - margin) / Math.Max(Math.Abs(v.Y), 1e-3f));
                tip = c + v * t;
            }
            Vector2 side = new(-n.Y, n.X), back = tip - n * 8f;
            var tri = new[] { tip.Floor(), (back + side * 4.5f).Floor(), (back - side * 4.5f).Floor() };
            DrawColoredPolygon(new[] { tip + n * 1.5f, back + side * 6f - n * 1.5f, back - side * 6f - n * 1.5f }, new Color(0.35f, 0, 0));
            DrawColoredPolygon(tri, col);
        }
    }
}
