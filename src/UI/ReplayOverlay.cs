using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;

namespace CCCPRacing.UI;

/// <summary>
/// The viewer's overlay of the player replay (docs/work-orders/player-replay.md phase 3). Draw only: RaceScene fills View, this reads it.
///
/// Controls (the hints the overlay prints; the viewer itself lives in RaceScene):
///   Action          Keyboard        Pad
///   play / pause    SPACE           A
///   seek back/fwd   LEFT / RIGHT    D-PAD LEFT / RIGHT
///   speed           UP / DOWN       D-PAD UP / DOWN   (0.25X 0.5X 1X 2X)
///   step (paused)   , and .         LB / RB
///   camera          C               Y
///   free camera     W A S D / MOUSE (EDIT > FREE CAMERA)   R or ESC back to the camera that was in view
///   hide/show UI    H               X
///   back            ESC             B
///   cancel loading  ESC             B
/// While the world builds only LOADING REPLAY, the progress bar and the cancel hint are drawn. With the overlay hidden only a one-line
/// "H SHOW CONTROLS" remains.
/// </summary>
public partial class ReplayOverlay : Control
{
    static readonly Color Red = new(0.86f, 0.12f, 0.08f);
    static readonly Color Gold = new(1f, 0.8f, 0.2f);
    static readonly Color Paper = new(0.93f, 0.9f, 0.8f);
    static readonly Color Grey = new(0.6f, 0.6f, 0.58f);

    ReplayOverlayView _view = new();

    public ReplayOverlayView View
    {
        get => _view;
        set { _view = value ?? new(); QueueRedraw(); }
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    Texture2D _backdrop;
    /// <summary>The frozen still of the live game shown behind the loading panel while the viewer builds (the session captures it once and drops it when the viewer is ready or closed).</summary>
    public Texture2D Backdrop { get => _backdrop; set { _backdrop = value; QueueRedraw(); } }

    internal static int DrawCount;        // the smoke test checks that _Draw really ran

    public override void _Draw()
    {
        DrawCount++;
        var size = GetViewportRect().Size;
        var layout = Layout(_view, size);
        if (_view.Building && _backdrop != null)
        {
            DrawTextureRect(_backdrop, new Rect2(Vector2.Zero, size), false);      // (captured from the same viewport: the same size and framing, no crop or zoom)
            DrawRect(new Rect2(Vector2.Zero, size), new Color(0, 0, 0, 0.45f));      // the scrim under the panel
        }
        foreach (var f in layout.Fills) { if (f.Filled) DrawRect(f.Rect, f.Color); else DrawRect(f.Rect, f.Color, false, 1f); }
        foreach (var t in layout.Texts)
            PixelFont.Draw(this, t.Text, new Vector2(t.X, t.Y), t.Color, t.Scale);
    }

    // --- layout: one pure function, shared by the draw and the tests ---

    internal readonly record struct Fill(Rect2 Rect, Color Color, bool Filled);
    /// <summary>A text at (X, Y) (its left edge), inside Bounds (the panel or the screen it must not leave).</summary>
    internal readonly record struct Label(string Text, float X, float Y, Color Color, int Scale, Rect2 Bounds);

    internal sealed class LayoutResult
    {
        public readonly List<Fill> Fills = new();
        public readonly List<Label> Texts = new();
    }

    static string Fit(string s, float width) => Hud.ReplayFit(s, width);

    static void Panel(LayoutResult l, Rect2 r, float alpha = 0.8f)
    {
        l.Fills.Add(new Fill(r, new Color(0.05f, 0.03f, 0.03f, alpha), true));
        l.Fills.Add(new Fill(r, new Color(Red, 0.9f), false));
    }

    internal static string FormatTime(float t)
    {
        int tenths = (int)(Math.Max(0f, t) * 10f);
        return $"{tenths / 600}:{tenths / 10 % 60:00}.{tenths % 10}";
    }

    internal static string SpeedText(float speed) => "SPEED " + speed.ToString("0.##", CultureInfo.InvariantCulture) + "X";

    /// <summary>The hint groups, each a key and its action, in order; Step is the one that only matters when paused.</summary>
    internal static (string Text, bool PausedOnly)[] HintGroups(bool pad, bool editable = false) => (pad
        ? new[] { ("A PLAY/PAUSE", false), ("D-PAD SEEK/SPEED", false), ("LB/RB STEP", true), ("Y CAMERA", false), ("X HIDE", false), ("BACK EDIT/EXPORT", false), ("B BACK", false) }
        : new[] { ("SPACE PLAY/PAUSE", false), ("< > SEEK", false), ("UP/DOWN SPEED", false), (", . STEP", true), ("C CAMERA", false), ("WASD/MOUSE FREE CAMERA", false), ("H HIDE", false), ("V EDIT/EXPORT", false), ("ESC BACK", false) }).ToArray();      // (EDIT/EXPORT shows for every lap: a lap that is not kept offers EXPORT and the way to keep it)

    internal static LayoutResult Layout(ReplayOverlayView v, Vector2 size)
    {
        var l = new LayoutResult();
        var screen = new Rect2(Vector2.Zero, size);
        float W = size.X, H = size.Y;
        const float M = 6;
        string hintCancel = v.PadMode ? "B CANCEL" : "ESC CANCEL";

        if (v.Building)
        {
            float w = Mathf.Min(240, W - 12), h = 62;
            var r = new Rect2(Mathf.Floor((W - w) / 2), Mathf.Floor((H - h) / 2), w, h);
            Panel(l, r, 0.9f);
            int scale = PixelFont.Measure("LOADING REPLAY", 2) <= w - 12 ? 2 : 1;
            l.Texts.Add(new Label("LOADING REPLAY", Mathf.Floor(r.GetCenter().X - PixelFont.Measure("LOADING REPLAY", scale) / 2f), r.Position.Y + 8, Gold, scale, r));
            var bar = new Rect2(r.Position.X + 12, r.Position.Y + 30, w - 24, 6);
            l.Fills.Add(new Fill(bar, new Color(Grey, 0.3f), true));
            float p = Mathf.Clamp(float.IsFinite(v.BuildProgress) ? v.BuildProgress : 0f, 0f, 1f);
            l.Fills.Add(new Fill(new Rect2(bar.Position, new Vector2(Mathf.Floor(bar.Size.X * p), bar.Size.Y)), Gold, true));
            l.Fills.Add(new Fill(bar, new Color(Red, 0.9f), false));
            l.Texts.Add(new Label(hintCancel, Mathf.Floor(r.GetCenter().X - PixelFont.Measure(hintCancel) / 2f), r.End.Y - 16, Grey, 1, r));
            return l;
        }

        // The hint lines (wrapped), needed first for the height of the bottom block.
        var lines = new List<List<(string Text, bool PausedOnly)>> { new() };
        float lineW = 0, maxW = W - 2 * M - 12;
        if (v.Shown)
            foreach (var g in v.FreeCamera && v.PadMode ? new (string Text, bool PausedOnly)[] { ("L-STICK MOVE", false), ("R-STICK LOOK", false), ("LT/RT HEIGHT", false), ("L3 FAST", false), ("A PLAY/PAUSE", false), ("D-PAD SEEK/SPEED", false), ("LB/RB STEP", true), ("X ADD KEY", false), ("Y FOLLOW", false), ("R3 LOOK AT CAR", false), ("START MOTION / LENS", false), ("B FOLLOW CAMERA", false) } : v.FreeCamera ? new (string Text, bool PausedOnly)[] { ("WASD MOVE", false), ("Q/E HEIGHT", false), ("V EDIT/EXPORT", false), ("MOUSE LOOK", false), ("SHIFT FAST / CTRL SLOW", false), ("SPACE PLAY/PAUSE", false), ("< > SEEK", false), ("UP/DOWN SPEED", false), (", . STEP", true), ("H HIDE", false), ("F12 SCREENSHOT", false), ("B MOTION / LENS", false), ("K ADD KEY", false), ("TAB/SHIFT+TAB STEP KEYS", false), ("DEL REMOVE KEY", false), ("R/ESC FOLLOW CAMERA", false) } : v.KeyTimes.Length > 0 && !v.PadMode ? HintGroups(v.PadMode, v.Editable).Append(("TAB CAMERA KEYS", false)).Select(h => (Text: h.Item1, PausedOnly: h.Item2)).ToArray() : HintGroups(v.PadMode, v.Editable))
            {
                float gw = PixelFont.Measure(g.Text), add = lines[^1].Count == 0 ? gw : gw + 12;
                if (lines[^1].Count > 0 && lineW + add > maxW) { lines.Add(new()); lineW = 0; add = gw; }
                lines[^1].Add(g);
                lineW += add;
            }
        float blockH = 38 + lines.Count * 10 + 3;
        float blockTop = v.Shown ? H - M - blockH : H - M - 8;

        // The transient notice, above the timeline.
        if (!string.IsNullOrEmpty(v.Notice))
        {
            string n = Fit(v.Notice, W - 2 * M - 12);
            float nw = PixelFont.Measure(n) + 12;
            var nr = new Rect2(Mathf.Floor((W - nw) / 2), blockTop - 17, nw, 13);
            Panel(l, nr, 0.85f);
            l.Texts.Add(new Label(n, nr.Position.X + 6, nr.Position.Y + 3, Gold, 1, nr));
        }

        if (!v.Shown)
        {
            string hint = v.PadMode ? "X SHOW CONTROLS" : "H SHOW CONTROLS";
            l.Texts.Add(new Label(hint, M, H - M - 8, Grey, 1, screen));
            return l;
        }

        // Title, top left.
        if (!string.IsNullOrEmpty(v.Title))
        {
            string t = Fit(v.Title + (v.Dirty ? " *" : "") + (v.Pinned ? " PIN" : ""), W - 2 * M - 10);
            var tr = new Rect2(M, M, PixelFont.Measure(t) + 10, 13);
            Panel(l, tr, 0.75f);
            l.Texts.Add(new Label(t, tr.Position.X + 5, tr.Position.Y + 3, Gold, 1, tr));
        }

        if (v.FreeCamera)
        {
            var text = new[] { "FREE CAMERA", (v.PadMode ? "Y" : "F") + " FOLLOW CAR " + (v.FreeFollow ? "ON" : "OFF"), (v.PadMode ? "R3" : "G") + " LOOK AT CAR " + (v.FreeLook ? "ON" : "OFF") };
            float fw = text.Max(t => PixelFont.Measure(t)) + 12;
            var fr = new Rect2(M, M + 17, Mathf.Min(fw, W - 2 * M), 38);
            Panel(l, fr, .85f);
            for (int i = 0; i < text.Length; i++) l.Texts.Add(new Label(Fit(text[i], fr.Size.X - 12), fr.Position.X + 6, fr.Position.Y + 5 + 10 * i, i == 0 ? Gold : Paper, 1, fr));
        }

        if (v.FreeCamera)
        {
            int heading = (int)Mathf.Round(v.FreeHeading) % 360;
            var info = new[] { $"X {v.FreePosition.X:0.0}  Y {v.FreePosition.Y:0.0}  Z {v.FreePosition.Z:0.0}", $"HEADING {heading} DEG  PITCH {Mathf.Round(v.FreePitch):0} DEG" };
            float iw = Mathf.Min(info.Max(t => PixelFont.Measure(t)) + 12, W - 2 * M);
            var ir = new Rect2(W - M - iw, M, iw, 28);
            Panel(l, ir, .85f);
            for (int i = 0; i < info.Length; i++) l.Texts.Add(new Label(Fit(info[i], ir.Size.X - 12), ir.Position.X + 6, ir.Position.Y + 5 + 10 * i, Paper, 1, ir));
        }

        // The bottom block.
        var br = new Rect2(M, blockTop, W - 2 * M, blockH);
        Panel(l, br, 0.85f);
        float x0 = br.Position.X + 6, x1 = br.End.X - 6;
        string state = v.Ended ? "END OF LAP" : v.Paused ? "PAUSED" : "PLAYING";
        l.Texts.Add(new Label(state, x0, br.Position.Y + 4, v.Ended ? Red : v.Paused ? Gold : Paper, 1, br));
        string speed = SpeedText(v.Speed);
        float stateEnd = x0 + PixelFont.Measure(state) + 12;
        string cam = "CAMERA " + (v.Camera ?? "");
        float speedW = PixelFont.Measure(speed);
        cam = Fit(cam, Mathf.Max(0, x1 - stateEnd - speedW - 12));
        float cx = x1 - PixelFont.Measure(cam);
        l.Texts.Add(new Label(cam, cx, br.Position.Y + 4, Paper, 1, br));
        l.Texts.Add(new Label(speed, cx - 12 - speedW, br.Position.Y + 4, Paper, 1, br));

        // The timeline.
        var tl = new Rect2(x0, br.Position.Y + 17, x1 - x0, 6);
        float dur = v.Duration > 0.001f && float.IsFinite(v.Duration) ? v.Duration : 1f;
        float frac = Mathf.Clamp(float.IsFinite(v.Clock) ? v.Clock / dur : 0f, 0f, 1f);
        l.Fills.Add(new Fill(tl, new Color(Grey, 0.3f), true));
        l.Fills.Add(new Fill(new Rect2(tl.Position, new Vector2(Mathf.Floor(tl.Size.X * frac), tl.Size.Y)), new Color(Red, 0.9f), true));
        if (v.Duration > 0.001f && tl.Size.X * 10f / dur >= 4f)
            for (float s = 10f; s < dur; s += 10f)
                l.Fills.Add(new Fill(new Rect2(tl.Position.X + Mathf.Floor(tl.Size.X * s / dur), tl.End.Y, 1, 2), new Color(Grey, 0.8f), true));
        // the cut window: what is outside it is dimmed (a reversible choice, the lap is whole), IN and OUT are gold ticks, camera keys are paper ticks above the line
        bool hasIn = v.WindowIn >= 0 && float.IsFinite(v.WindowIn), hasOut = v.WindowOut >= 0 && float.IsFinite(v.WindowOut);
        if ((hasIn || hasOut) && v.Duration > 0.001f)
        {
            float a = hasIn ? Mathf.Clamp(v.WindowIn / dur, 0, 1) : 0f, b = hasOut ? Mathf.Clamp(v.WindowOut / dur, 0, 1) : 1f;
            if (v.TrimOn)
            {
                if (a > 0) l.Fills.Add(new Fill(new Rect2(tl.Position, new Vector2(Mathf.Floor(tl.Size.X * a), tl.Size.Y)), new Color(0, 0, 0, 0.6f), true));
                if (b < 1) l.Fills.Add(new Fill(new Rect2(tl.Position.X + Mathf.Floor(tl.Size.X * b), tl.Position.Y, tl.Size.X - Mathf.Floor(tl.Size.X * b), tl.Size.Y), new Color(0, 0, 0, 0.6f), true));
            }
            if (hasIn) l.Fills.Add(new Fill(new Rect2(tl.Position.X + Mathf.Floor(tl.Size.X * a) - 1, tl.Position.Y - 2, 2, tl.Size.Y + 4), Gold, true));
            if (hasOut) l.Fills.Add(new Fill(new Rect2(tl.Position.X + Mathf.Floor(tl.Size.X * b) - 1, tl.Position.Y - 2, 2, tl.Size.Y + 4), Gold, true));
        }
        foreach (float kt in v.KeyTimes)
            if (kt >= 0 && float.IsFinite(kt) && v.Duration > 0.001f) l.Fills.Add(new Fill(new Rect2(tl.Position.X + Mathf.Floor(tl.Size.X * Mathf.Clamp(kt / dur, 0, 1)), tl.Position.Y - 4, 1, 3), Paper, true));
        l.Fills.Add(new Fill(tl, new Color(Red, 0.9f), false));
        float px = Mathf.Clamp(tl.Position.X + Mathf.Floor(tl.Size.X * frac), tl.Position.X + 1, tl.End.X - 2);
        l.Fills.Add(new Fill(new Rect2(px - 1, tl.Position.Y - 3, 3, tl.Size.Y + 6), Gold, true));

        string times = FormatTime(v.Clock) + " / " + FormatTime(v.Duration);
        l.Texts.Add(new Label(times, x0, br.Position.Y + 27, Paper, 1, br));

        // The hints.
        for (int i = 0; i < lines.Count; i++)
        {
            float x = x0, y = br.Position.Y + 38 + i * 10;
            foreach (var g in lines[i])
            {
                l.Texts.Add(new Label(g.Text, x, y, g.PausedOnly && !v.Paused ? new Color(Grey, 0.45f) : Grey, 1, br));
                x += PixelFont.Measure(g.Text) + 12;
            }
        }
        // The EDIT panel (left, under the title), and a modal question over everything.
        if (v.EditOpen && v.EditRows.Length > 0)
        {
            float pw = 0; foreach (var r0 in v.EditRows) pw = Mathf.Max(pw, PixelFont.Measure(r0));
            pw = Mathf.Min(pw + 22, W - 2 * M);
            float rowH = 9, ph = 13 + v.EditRows.Length * rowH + 4;
            var pr = new Rect2(M, M + 17, pw, Mathf.Min(ph, blockTop - (M + 17) - 4));
            Panel(l, pr, 0.9f);
            l.Texts.Add(new Label("EDIT", pr.Position.X + 6, pr.Position.Y + 3, Gold, 1, pr));
            int fit = Mathf.Max(1, (int)((pr.Size.Y - 13 - 3) / rowH));
            int first = Mathf.Clamp(v.EditSelected - fit + 1, 0, Mathf.Max(0, v.EditRows.Length - fit));
            for (int i = 0; i < fit && first + i < v.EditRows.Length; i++)
            {
                bool sel = first + i == v.EditSelected;
                float y = pr.Position.Y + 13 + i * rowH;
                if (sel) l.Fills.Add(new Fill(new Rect2(pr.Position.X + 2, y - 1, pr.Size.X - 4, rowH), new Color(Red, 0.55f), true));
                l.Texts.Add(new Label(Fit(v.EditRows[first + i], pr.Size.X - 12), pr.Position.X + 6, y, sel ? Gold : Paper, 1, pr));
            }
        }
        if (v.ExportRows.Length > 0 || v.ExportLines.Length > 0)
        {
            float ew = PixelFont.Measure(v.ExportTitle, 1) + 24;
            foreach (var t in v.ExportRows) ew = Mathf.Max(ew, PixelFont.Measure(t) + 24);
            foreach (var t in v.ExportLines) ew = Mathf.Max(ew, PixelFont.Measure(t) + 24);
            ew = Mathf.Min(Mathf.Max(ew, 200), W - 2 * M);
            int nLines = v.ExportRows.Length + v.ExportLines.Length;
            float eh = 24 + nLines * 10 + (v.ExportFraction >= 0 ? 12 : 0);
            var er = new Rect2(Mathf.Floor((W - ew) / 2), Mathf.Max(M, Mathf.Floor((H - eh) / 2)), ew, Mathf.Min(eh, H - 2 * M));
            Panel(l, er, 0.96f);
            l.Texts.Add(new Label(Fit(v.ExportTitle, er.Size.X - 12), Mathf.Floor(er.GetCenter().X - PixelFont.Measure(Fit(v.ExportTitle, er.Size.X - 12)) / 2f), er.Position.Y + 5, Gold, 1, er));
            float y = er.Position.Y + 17;
            for (int i = 0; i < v.ExportRows.Length; i++, y += 10)
            {
                bool sel = i == v.ExportSelected;
                if (sel) l.Fills.Add(new Fill(new Rect2(er.Position.X + 2, y - 1, er.Size.X - 4, 10), new Color(Red, 0.55f), true));
                l.Texts.Add(new Label(Fit(v.ExportRows[i], er.Size.X - 12), er.Position.X + 6, y, sel ? Gold : Paper, 1, er));
            }
            foreach (var t in v.ExportLines)
            {
                string f = Fit(t, er.Size.X - 12);
                l.Texts.Add(new Label(f, Mathf.Floor(er.GetCenter().X - PixelFont.Measure(f) / 2f), y, Paper, 1, er)); y += 10;
            }
            if (v.ExportFraction >= 0)
            {
                var bar = new Rect2(er.Position.X + 8, y + 2, er.Size.X - 16, 5);
                l.Fills.Add(new Fill(bar, new Color(Grey, 0.3f), true));
                l.Fills.Add(new Fill(new Rect2(bar.Position, new Vector2(Mathf.Floor(bar.Size.X * Mathf.Clamp(v.ExportFraction, 0, 1)), bar.Size.Y)), Gold, true));
                l.Fills.Add(new Fill(bar, new Color(Red, 0.9f), false));
            }
        }
        if (v.Prompt.Length > 0)
        {
            float qw = 0; foreach (var q in v.Prompt) qw = Mathf.Max(qw, PixelFont.Measure(q));
            qw = Mathf.Min(qw + 24, W - 2 * M);
            float qh = 14 + v.Prompt.Length * 11;
            var qr = new Rect2(Mathf.Floor((W - qw) / 2), Mathf.Floor((H - qh) / 2), qw, qh);
            Panel(l, qr, 0.96f);
            for (int i = 0; i < v.Prompt.Length; i++)
                l.Texts.Add(new Label(Fit(v.Prompt[i], qr.Size.X - 8), Mathf.Floor(qr.GetCenter().X - PixelFont.Measure(Fit(v.Prompt[i], qr.Size.X - 8)) / 2f), qr.Position.Y + 7 + i * 11, i == 0 ? Gold : Paper, 1, qr));
        }
        return l;
    }
}
