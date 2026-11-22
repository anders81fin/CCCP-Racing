using System;
using System.Collections.Generic;
using System.Linq;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.UI;

/// <summary>
/// The ceremonies of the SPUTNIK test pilots' programme (SputnikProgram.cs; RaceScene.SputnikCeremony.cs decides, this draws): a card for the main recognition earned (the licence, the order with its ribbon and gold stars, the champion's
/// pennant, the chief test pilot's finale), then the grant letter that lists everything granted at once, then, for the chief test pilot, the pit mechanic painting the name on the car in the 3D view with the text only drawn here.
/// Pixel art from rectangles, polygons and the game's own font; every text is measured against the screen so a long name fits.
/// </summary>
public partial class Hud
{
    public bool SpOn;
    public SputnikAward SpAward;
    public SputnikAward[] SpNew = Array.Empty<SputnikAward>(), SpOld = Array.Empty<SputnikAward>();
    public string SpName = "";
    /// <summary>The Sputnik medal and the name of each of the programme's four tracks, SputnikStanding.AllTracks order.</summary>
    public NormPlan.Medal[] SpMedals = new NormPlan.Medal[4];
    public string[] SpTrackNames = { "KOLKHOZ", "ATOMGRAD-4", "SIBERIA", "POLIGON 17" };
    public int SpPage;      // 0 the main card, 1 the grant letter
    public float SpAge;
    public bool SpCanClose;
    // the chief test pilot's painting (the 3D view under the HUD)
    public bool SpPaintOn;
    public float SpPaintAge, SpPaintReveal;
    public bool SpPaintDone;

    static readonly Color SpPaper = new(0.90f, 0.86f, 0.72f), SpInk = new(0.14f, 0.10f, 0.08f), SpStampRed = new(0.72f, 0.10f, 0.10f), SpSilver = new(0.82f, 0.85f, 0.9f);

    public void SpBegin(SputnikAward main, SputnikAward[] fresh, SputnikAward[] old, string name, NormPlan.Medal[] medals, string[] trackNames)
    {
        SpOn = true; SpAward = main; SpNew = fresh; SpOld = old; SpName = name; SpMedals = medals; SpTrackNames = trackNames;
        SpPage = 0; SpAge = 0; SpCanClose = false; SpPaintOn = false; SpPaintDone = false;
        GoldRewardFanfare(false);
    }

    public void SpPage1()
    {
        SpPage = 1; SpAge = 0; SpCanClose = false;
        GoldRewardFanfare(true);
    }

    public void SpEnd() { SpOn = false; SpPaintOn = false; }

    public void SpTick(float dt)
    {
        float before = SpAge;
        SpAge += dt;
        if (SpPage == 0 && before < 2.8f && SpAge >= 2.8f) GoldRewardFanfare(true);
        SpCanClose = SpAge >= (SpPage == 0 ? 3.4f : 1.5f);
    }

    Color SpAccent(SputnikAward a) => a switch { SputnikAward.TestPilot => SpSilver, SputnikAward.Champion => Red, _ => Gold };

    /// <summary>The routing from DrawHud: the cards, or the painting's text over the 3D view.</summary>
    void DrawSputnik()
    {
        if (SpPaintOn) { DrawSputnikPaint(); return; }
        float cx = Screen.X / 2;
        var accent = SpAccent(SpAward);
        DrawRect(new Rect2(Vector2.Zero, Screen), new Color(0.03f, 0.02f, 0.02f, 0.97f));
        var panel = new Rect2(6, 6, Screen.X - 12, Screen.Y - 12);
        DrawRect(panel, accent, false, 2);
        DrawRect(new Rect2(panel.Position + Vector2.One * 5, panel.Size - Vector2.One * 10), new Color(accent, 0.4f), false, 1);
        if (SpPage == 1) { DrawSputnikLetter(panel, accent); return; }
        switch (SpAward)
        {
            case SputnikAward.TestPilot: DrawSpLicence(panel); break;
            case SputnikAward.Champion: DrawSpPennant(panel); break;
            case SputnikAward.Chief: DrawSpChief(panel); break;
            default: DrawSpOrder(panel); break;
        }
        if (SpCanClose && (int)(_clock * 2) % 2 == 0) PixelFont.DrawCentered(this, AnyKey, cx, panel.End.Y - 14, Paper);
    }

    // ---------------------------------------------------------------- shared pieces

    void SpHeading(Rect2 panel, string text, Color c, float y)
    {
        float cx = Screen.X / 2;
        int scale = PixelFont.Measure(text, 2) < panel.Size.X - 24 ? 2 : 1;
        DrawTypewriter(text, cx, y, c, SpAge - 0.3f, RewardTypewriterRate, scale, HeadingShadow);
    }

    /// <summary>A line that fits: the font at scale 1, cut from the middle with dots if even that is too wide.</summary>
    void SpLine(string text, float cx, float y, Color c, float maxW, int scale = 1, bool onPaper = false)
    {
        if (PixelFont.Measure(text, scale) > maxW) scale = 1;
        while (PixelFont.Measure(text, 1) > maxW && text.Length > 6) text = text[..(text.Length - 2)].TrimEnd() + ".";
        PixelFont.DrawCentered(this, text, cx, y, c, scale, onPaper ? new Color(0, 0, 0, 0) : null);
    }

    /// <summary>A satellite pin: the ball with four antennae.</summary>
    void SpPin(Vector2 c, float r, Color col)
    {
        DrawCircle(c, r, col);
        DrawCircle(c, r * 0.55f, col.Darkened(0.35f));
        DrawCircle(c + new Vector2(-r * 0.2f, -r * 0.25f), r * 0.28f, col.Lightened(0.35f));
        for (int i = 0; i < 4; i++)
        {
            Vector2 d = Vector2.FromAngle(MathF.PI / 4 + i * MathF.PI / 2);
            DrawLine(c + d * r * 0.9f, c + d * r * 2.1f, col, 1.5f);
            DrawCircle(c + d * r * 2.1f, 1.2f, col);
        }
    }

    /// <summary>A rubber stamp: a ring and the track's name; a gold one for a gold, a silver one for a silver, grey when there is nothing yet.</summary>
    void SpStamp(Vector2 c, float r, string name, NormPlan.Medal medal, float reveal, bool onPaper = false)
    {
        if (reveal <= 0) return;
        var col = medal >= NormPlan.Medal.Gold ? Gold : medal >= NormPlan.Medal.Silver ? SpStampRed : Grey;
        float a = Math.Min(1f, reveal * 3f);
        var colour = new Color(col, a);
        float rr = r * (1 + 0.5f * MathF.Exp(-reveal * 6) * MathF.Cos(reveal * 18));
        DrawArc(c, rr, 0, MathF.Tau, 40, colour, 2);
        DrawArc(c, rr - 3.5f, 0, MathF.Tau, 40, new Color(col, a * 0.6f), 1);
        string label = name.Length > 8 ? name[..8] : name;
        PixelFont.DrawCentered(this, label, c.X, c.Y - 3.5f, colour, 1, onPaper ? new Color(0, 0, 0, 0) : null);
        if (medal >= NormPlan.Medal.Gold) StarShape(c + new Vector2(0, -rr * 0.55f), 3.2f, colour);
    }

    /// <summary>The three (four) stamps in a row, one by one.</summary>
    void SpStamps(Rect2 area, int count, float startAge, bool onPaper = false)
    {
        float w = area.Size.X / count, r = Math.Min(w * 0.42f, 27f);
        for (int i = 0; i < count; i++)
            SpStamp(new Vector2(area.Position.X + w * (i + 0.5f), area.Position.Y + area.Size.Y / 2), r, SpTrackNames[i], SpMedals[i], SpAge - startAge - i * 0.45f, onPaper);
    }

    /// <summary>The order's ribbon: red with gold edge stripes, hanging from the star, and gold stars on it.</summary>
    void SpRibbon(Vector2 top, float w, float h, int stars, float starReveal)
    {
        var r = new Rect2(top.X - w / 2, top.Y, w, h);
        DrawRect(r, new Color(0.62f, 0.07f, 0.08f));
        DrawRect(new Rect2(r.Position.X + 3, r.Position.Y, 2, h), Gold); DrawRect(new Rect2(r.End.X - 5, r.Position.Y, 2, h), Gold);
        DrawRect(r, new Color(Gold, 0.9f), false, 1);
        DrawColoredPolygon(new[] { new Vector2(r.Position.X, r.End.Y), new Vector2(r.GetCenter().X, r.End.Y - 7), new Vector2(r.End.X, r.End.Y), new Vector2(r.End.X, r.End.Y + 5), new Vector2(r.GetCenter().X, r.End.Y - 2), new Vector2(r.Position.X, r.End.Y + 5) }, new Color(0.62f, 0.07f, 0.08f));
        for (int i = 0; i < stars; i++)
        {
            float t = starReveal - i * 0.3f;
            if (t <= 0) continue;
            float sx = top.X + (i - (stars - 1) / 2f) * 9.5f, sy = top.Y + h * 0.82f;
            StarShape(new Vector2(sx, sy), 3.6f * (1 + 0.6f * MathF.Exp(-t * 6) * MathF.Cos(t * 14)), new Color(Gold, Math.Min(1f, t * 4)));
        }
    }

    // ---------------------------------------------------------------- 1: the test pilot's licence

    void DrawSpLicence(Rect2 panel)
    {
        float cx = Screen.X / 2, top = panel.Position.Y;
        SpHeading(panel, "SPUTNIK TEST PILOT", SpSilver, top + 12);
        float w = Math.Min(Screen.X - 40, 300), h = Math.Min(panel.Size.Y * 0.56f, 128);
        var card = new Rect2(cx - w / 2, top + 34, w, h);
        float arrive = Math.Clamp(SpAge / 0.7f, 0f, 1f);
        card.Position += new Vector2(0, (1 - arrive) * 40);
        DrawRect(card, SpPaper);
        DrawRect(card, SpInk, false, 1);
        DrawRect(new Rect2(card.Position, new Vector2(card.Size.X, 13)), new Color(0.16f, 0.2f, 0.34f));
        PixelFont.Draw(this, "CCCP / SPUTNIK TEST PILOT'S LICENCE", card.Position + new Vector2(6, 3), SpPaper, 1, new Color(0, 0, 0, 0));
        // the photograph: a pilot in a leather helmet and goggles
        var photo = new Rect2(card.Position.X + 8, card.Position.Y + 20, 40, 46);
        DrawRect(photo, new Color(0.45f, 0.55f, 0.68f));
        DrawCircle(photo.GetCenter() + new Vector2(0, 4), 12, new Color(0.87f, 0.66f, 0.52f));
        DrawRect(new Rect2(photo.GetCenter().X - 13, photo.Position.Y + 8, 26, 10), new Color(0.35f, 0.22f, 0.12f));
        DrawRect(new Rect2(photo.GetCenter().X - 10, photo.Position.Y + 16, 20, 5), new Color(0.15f, 0.15f, 0.18f));
        DrawRect(new Rect2(photo.GetCenter().X - 14, photo.End.Y - 8, 28, 8), new Color(0.2f, 0.28f, 0.16f));
        DrawRect(photo, SpInk, false, 1);
        float tx = photo.End.X + 10, maxW = card.End.X - tx - 6;
        PixelFont.Draw(this, "NAME", new Vector2(tx, card.Position.Y + 20), new Color(0.4f, 0.35f, 0.3f), 1, new Color(0, 0, 0, 0));
        string name = SpName.Length == 0 ? "COMRADE" : SpName;
        int ns = PixelFont.Measure(name, 2) <= maxW ? 2 : 1;
        PixelFont.Draw(this, name, new Vector2(tx, card.Position.Y + 30), SpInk, ns, new Color(0, 0, 0, 0));
        PixelFont.Draw(this, "CLASS", new Vector2(tx, card.Position.Y + 48 + (ns - 1) * 7), new Color(0.4f, 0.35f, 0.3f), 1, new Color(0, 0, 0, 0));
        PixelFont.Draw(this, "SPUTNIK / ROAD USE", new Vector2(tx, card.Position.Y + 57 + (ns - 1) * 7), SpInk, 1, new Color(0, 0, 0, 0));
        SpPin(new Vector2(card.End.X - 22, card.Position.Y + 32), 7.5f, SpSilver);
        SpStamps(new Rect2(card.Position.X + 8, card.Position.Y + 72, card.Size.X - 16, card.Size.Y - 78), 3, 1.0f, true);
        float y = card.End.Y + 14;
        if (SpAge > 2.6f)
        {
            PixelFont.DrawCentered(this, "CLEARED FOR FLIGHT.", cx, y, Gold, 1, HeadingShadow);
            PixelFont.DrawCentered(this, "ROAD USE PERMITTED.", cx, y + 10, Paper);
        }
    }

    // ---------------------------------------------------------------- 2-4: the order, third to first class

    void DrawSpOrder(Rect2 panel)
    {
        float cx = Screen.X / 2, top = panel.Position.Y;
        SpHeading(panel, "ORDER OF SPUTNIK", Gold, top + 12);
        int stars = SputnikStanding.Stars(SpAward);
        string cls = SpAward switch { SputnikAward.Third => "THIRD CLASS", SputnikAward.Second => "SECOND CLASS", _ => "FIRST CLASS" };
        float arrive = 1 + 0.5f * MathF.Exp(-SpAge * 5) * MathF.Cos(SpAge * 13);
        Vector2 centre = new(cx - Math.Min(Screen.X * 0.2f, 90), top + panel.Size.Y * 0.42f);
        float radius = Math.Min(24, panel.Size.Y * 0.11f);
        SpRibbon(centre + new Vector2(0, -radius * 1.5f), 34, panel.Size.Y * 0.26f, stars, SpAge - 1.0f);
        for (int i = 0; i < 20; i++)
        {
            float a = i * MathF.Tau / 20 + SpAge * 0.08f;
            DrawLine(centre + Vector2.FromAngle(a) * radius * 1.35f, centre + Vector2.FromAngle(a) * radius * (i % 2 == 0 ? 2.0f : 1.7f), new Color(Gold, 0.35f), 1);
        }
        StarShape(centre + new Vector2(0, radius * 0.05f), radius * 1.1f * arrive, new Color(0.16f, 0.07f, 0.06f));
        StarShape(centre, radius * arrive, Gold);
        SpPin(centre, radius * 0.38f, SpSilver);
        float rx = centre.X + radius * 3.4f, rw = panel.End.X - rx - 12;
        float y = top + 36;
        PixelFont.Draw(this, cls, new Vector2(rx, y), Gold, 2, HeadingShadow); y += 22;
        var lines = new List<(string, Color)>();
        lines.Add(("COMRADE " + (SpName.Length == 0 ? "PILOT" : SpName), Paper));
        lines.Add(("SILVER OR BETTER ON ALL FOUR TRACKS", Grey));
        if (SpAward >= SputnikAward.Second) lines.Add(("GOLD: " + string.Join(", ", SputnikStanding.AllTracks.Select((t, i) => (t, i)).Where(p => SpMedals[p.i] >= NormPlan.Medal.Gold).Select(p => SpTrackNames[p.i])), Gold));
        if (SpAward == SputnikAward.First)
        {
            var silver = SputnikStanding.AllTracks.Select((t, i) => (t, i)).Where(p => SpMedals[p.i] == NormPlan.Medal.Silver).Select(p => SpTrackNames[p.i]).ToArray();
            if (silver.Length > 0) lines.Add(("STILL SILVER: " + string.Join(", ", silver), SpSilver));
        }
        foreach (var (text, col) in lines)
        {
            foreach (var part in SpWrap(text, rw)) { PixelFont.Draw(this, part, new Vector2(rx, y), col); y += 10; }
            y += 2;
        }
        // the stamps of the four tracks along the bottom (the fourth is Poligon's: the order wants all four)
        SpStamps(new Rect2(panel.Position.X + 16, top + panel.Size.Y * 0.68f, panel.Size.X - 32, panel.Size.Y * 0.22f), 4, 1.4f);
    }

    /// <summary>Breaks a line at spaces to the width (scale 1).</summary>
    IEnumerable<string> SpWrap(string text, float width)
    {
        var line = "";
        foreach (var word in text.Split(' '))
        {
            string next = line.Length == 0 ? word : line + " " + word;
            if (PixelFont.Measure(next) > width && line.Length > 0) { yield return line; line = word; }
            else line = next;
        }
        if (line.Length > 0) yield return line;
    }

    // ---------------------------------------------------------------- 5: the champion's pennant

    void DrawSpPennant(Rect2 panel)
    {
        float cx = Screen.X / 2, top = panel.Position.Y;
        SpHeading(panel, "SPUTNIK RALLY CHAMPION", Gold, top + 12);
        float poleX = cx - Math.Min(Screen.X * 0.32f, 130), poleTop = top + 34, poleBottom = panel.End.Y - 40;
        DrawLine(new Vector2(poleX, poleTop - 6), new Vector2(poleX, poleBottom), new Color(0.75f, 0.7f, 0.6f), 3);
        DrawCircle(new Vector2(poleX, poleTop - 8), 3.5f, Gold);
        float rise = Math.Clamp((SpAge - 0.4f) / 1.2f, 0f, 1f);
        float fx = poleX + 2, fy = poleTop + (1 - rise) * (poleBottom - poleTop - 60), fw = (panel.End.X - 14) - fx, fh = Math.Min(86, panel.Size.Y * 0.38f);
        // the swallow-tailed pennant, waving a little: columns of quads, the tail notched at the free end
        const int N = 24;
        var red = new Color(0.72f, 0.08f, 0.08f);
        float Wave(float t) => MathF.Sin(t * 6 - SpAge * 3) * 3 * t;
        var topEdge = new List<Vector2>(); var botEdge = new List<Vector2>();
        for (int i = 0; i < N; i++)
        {
            float t0 = i / (float)N, t1 = (i + 1) / (float)N;
            float x0 = fx + fw * t0, x1 = fx + fw * t1, y0 = fy + Wave(t0), y1 = fy + Wave(t1);
            float notch0 = t0 > 0.84f ? (t0 - 0.84f) / 0.16f : 0f, notch1 = t1 > 0.84f ? (t1 - 0.84f) / 0.16f : 0f;      // the V cut into the free end
            float mid0 = fh * 0.5f * notch0, mid1 = fh * 0.5f * notch1;
            DrawColoredPolygon(new[] { new Vector2(x0, y0), new Vector2(x1, y1), new Vector2(x1, y1 + fh * 0.5f - mid1), new Vector2(x0, y0 + fh * 0.5f - mid0) }, red);
            DrawColoredPolygon(new[] { new Vector2(x0, y0 + fh * 0.5f + mid0), new Vector2(x1, y1 + fh * 0.5f + mid1), new Vector2(x1, y1 + fh), new Vector2(x0, y0 + fh) }, red);
            topEdge.Add(new Vector2(x0, y0)); botEdge.Add(new Vector2(x0, y0 + fh));
        }
        topEdge.Add(new Vector2(fx + fw, fy + Wave(1f))); botEdge.Add(new Vector2(fx + fw, fy + Wave(1f) + fh));
        var notchTip = new Vector2(fx + fw * 0.84f, fy + Wave(0.84f) + fh * 0.5f);
        DrawPolyline(topEdge.ToArray(), Gold, 2);
        DrawPolyline(botEdge.ToArray(), Gold, 2);
        DrawLine(topEdge[^1], notchTip, Gold, 2); DrawLine(botEdge[^1], notchTip, Gold, 2);
        DrawLine(topEdge[0], botEdge[0], Gold, 2);
        // the hammer-and-star motif: a gold star at the hoist
        StarShape(new Vector2(fx + 18, fy + 18), 9, Gold);
        string name = SpName.Length == 0 ? "COMRADE" : SpName;
        float maxW = fw - 56;
        int ns = PixelFont.Measure(name, 2) <= maxW ? 2 : 1;
        float nx = fx + 38 + (maxW - PixelFont.Measure(name, ns)) / 2;
        float wave = MathF.Sin(0.4f * 6 - SpAge * 3) * 3;
        PixelFont.Draw(this, name, new Vector2(nx, fy + fh * 0.30f + wave), Gold, ns, new Color(0.3f, 0, 0));
        string ro = "ILYICH · ATOMGRAD-4 · SIBERIA";
        int rs = PixelFont.Measure(ro) <= fw - 46 ? 1 : 1;
        PixelFont.Draw(this, ro, new Vector2(fx + 38 + (maxW - PixelFont.Measure(ro, rs)) / 2, fy + fh * 0.66f + wave), Paper, rs);
        if (SpAge > 2.4f)
        {
            PixelFont.DrawCentered(this, "ALL THREE FIRST TRACKS ON GOLD, ON SPUTNIK", cx, panel.End.Y - 36, Gold, 1, HeadingShadow);
        }
    }

    // ---------------------------------------------------------------- 6: the chief test pilot

    void DrawSpChief(Rect2 panel)
    {
        float cx = Screen.X / 2, top = panel.Position.Y;
        SpHeading(panel, "CHIEF TEST PILOT", Gold, top + 12);
        Vector2 centre = new(cx, top + panel.Size.Y * 0.38f);
        float radius = Math.Min(30, panel.Size.Y * 0.14f);
        float arrive = 1 + 0.6f * MathF.Exp(-SpAge * 4) * MathF.Cos(SpAge * 12);
        for (int ring = 0; ring < 2; ring++)
            for (int i = 0; i < 28; i++)
            {
                float a = i * MathF.Tau / 28 + SpAge * (ring == 0 ? 0.09f : -0.06f);
                float len = radius * (ring == 0 ? 2.5f : 1.9f);
                DrawColoredPolygon(new[] { centre + Vector2.FromAngle(a - 0.03f) * radius * 1.1f, centre + Vector2.FromAngle(a) * len, centre + Vector2.FromAngle(a + 0.03f) * radius * 1.1f }, new Color(Gold, ring == 0 ? 0.30f : 0.18f));
            }
        DrawMedalWreath(centre, radius * arrive, Gold);
        SpPin(centre, radius * 0.4f, SpSilver);
        // four gold stars over the badge, one at a time
        for (int i = 0; i < 4; i++)
        {
            float t = SpAge - 1.0f - i * 0.45f;
            if (t > 0) StarShape(new Vector2(cx + (i - 1.5f) * 22, centre.Y - radius * 1.9f), 7f * (1 + 0.6f * MathF.Exp(-t * 6) * MathF.Cos(t * 14)), new Color(Gold, Math.Min(1f, t * 4)));
        }
        SpStamps(new Rect2(panel.Position.X + 16, top + panel.Size.Y * 0.62f, panel.Size.X - 32, panel.Size.Y * 0.2f), 4, 2.2f);
        if (SpAge > 3.6f)
        {
            string name = "COMRADE " + (SpName.Length == 0 ? "PILOT" : SpName);
            SpLine(name, cx, panel.End.Y - 38, Gold, panel.Size.X - 24);
            PixelFont.DrawCentered(this, "ALL FOUR TRACKS ON GOLD, ON SPUTNIK", cx, panel.End.Y - 27, Paper);
        }
    }

    // ---------------------------------------------------------------- the grant letter: everything granted at once

    void DrawSputnikLetter(Rect2 panel, Color accent)
    {
        float cx = Screen.X / 2, top = panel.Position.Y;
        PixelFont.DrawCentered(this, "GRANT LETTER", cx, top + 12, Gold, PixelFont.Measure("GRANT LETTER", 2) < panel.Size.X - 24 ? 2 : 1, HeadingShadow);
        PixelFont.DrawCentered(this, "SPUTNIK TEST PILOTS' PROGRAMME", cx, top + 30, Grey);
        float w = panel.Size.X - 40, y = top + 46;
        var sheet = new Rect2(panel.Position.X + 20, y - 6, w, panel.Size.Y - 82);
        DrawRect(sheet, SpPaper); DrawRect(sheet, SpInk, false, 1);
        string who = "ISSUED TO COMRADE " + (SpName.Length == 0 ? "PILOT" : SpName);
        SpLine(who, cx, y, SpInk, w - 12, 1, true);
        y += 14;
        float rowGap = Math.Min(15, (sheet.End.Y - y - 6) / 6f);
        foreach (var a in SputnikStanding.Rank.Reverse())
        {
            bool fresh = SpNew.Contains(a), old = SpOld.Contains(a);
            if (!fresh && !old) continue;
            string line = (fresh ? "NEW  " : "     ") + SputnikStanding.Title(a);
            int stars = SputnikStanding.Stars(a);
            var col = fresh ? new Color(0.45f, 0.07f, 0.07f) : new Color(0.4f, 0.36f, 0.3f);
            float lx = sheet.Position.X + 8;
            if (PixelFont.Measure(line) > w - 48) line = line.Replace("ORDER OF SPUTNIK - ", "ORDER, ");
            PixelFont.Draw(this, line, new Vector2(lx, y), col, 1, new Color(0, 0, 0, 0));
            for (int s = 0; s < stars; s++) StarShape(new Vector2(sheet.End.X - 10 - s * 9, y + 3.5f), 3.2f, fresh ? new Color(0.78f, 0.55f, 0.05f) : new Color(0.6f, 0.55f, 0.45f));
            y += rowGap;
        }
        if (SpCanClose && (int)(_clock * 2) % 2 == 0) PixelFont.DrawCentered(this, AnyKey, cx, panel.End.Y - 14, Paper);
    }

    // ---------------------------------------------------------------- the chief test pilot's finale: the mechanic paints the car

    void DrawSputnikPaint()
    {
        float cx = Screen.X / 2;
        var panel = new Rect2(6, 6, Screen.X - 12, Screen.Y - 12);
        float topBand = 34, bottomBand = Math.Min(Screen.Y * 0.30f, 78);
        float veil = 1f - Mathf.SmoothStep(0, 1, Math.Clamp(SpPaintAge / 1.0f, 0f, 1f));
        DrawRect(new Rect2(Vector2.Zero, Screen), new Color(0.02f, 0.01f, 0, 0.9f * veil));
        DrawRect(new Rect2(0, 0, Screen.X, topBand), new Color(0.02f, 0.01f, 0, 0.82f));
        DrawRect(new Rect2(0, Screen.Y - bottomBand, Screen.X, bottomBand), new Color(0.02f, 0.01f, 0, 0.82f));
        DrawRect(new Rect2(0, topBand, Screen.X, 1), new Color(Gold, 0.8f));
        DrawRect(new Rect2(0, Screen.Y - bottomBand - 1, Screen.X, 1), new Color(Gold, 0.8f));
        DrawRect(panel, Gold, false, 2);
        DrawRect(new Rect2(panel.Position + Vector2.One * 5, panel.Size - Vector2.One * 10), new Color(Gold, 0.4f), false, 1);
        string head = SpPaintDone ? "THE CAR IS YOURS" : "THE PIT MECHANIC IS AT WORK";
        PixelFont.DrawCentered(this, head, cx, panel.Position.Y + 12, Gold, PixelFont.Measure(head, 2) < panel.Size.X - 24 ? 2 : 1, HeadingShadow);
        float by = Screen.Y - bottomBand;
        string name = "COMRADE " + (SpName.Length == 0 ? "PILOT" : SpName) + ", THE CAR IS YOURS";
        if (SpPaintDone)
        {
            int scale = PixelFont.Measure(name, 2) < panel.Size.X - 24 ? 2 : 1;
            if (scale == 1 && PixelFont.Measure(name, 1) > panel.Size.X - 16)
            {
                PixelFont.DrawCentered(this, "COMRADE " + (SpName.Length == 0 ? "PILOT" : SpName) + ",", cx, by + 8, Gold, 1, HeadingShadow);
                PixelFont.DrawCentered(this, "THE CAR IS YOURS", cx, by + 19, Gold, 1, HeadingShadow);
            }
            else PixelFont.DrawCentered(this, name, cx, by + 10, Gold, scale, HeadingShadow);
            PixelFont.DrawCentered(this, "CHIEF TEST PILOT OF THE SPUTNIK PROGRAMME", cx, by + 36, Paper);
            if (SpPaintAge > SpPaintDoneAt + 1.2f && (int)(_clock * 2) % 2 == 0) PixelFont.DrawCentered(this, AnyKey, cx, panel.End.Y - 12, Paper);
        }
        else PixelFont.DrawCentered(this, "NAME AND FOUR STARS, BY HAND", cx, by + 12, Paper);
    }

    public float SpPaintDoneAt = 9.5f;
}
