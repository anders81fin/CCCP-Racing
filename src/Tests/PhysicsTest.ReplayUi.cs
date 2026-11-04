using System;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    static readonly Vector2[] ReplayUiSizes = { new(480, 270), new(456, 256), new(512, 288) };

    static ReplayListView ReplayUiList(int rows, int selected, string notice = "", bool error = false)
    {
        var v = new ReplayListView { Selected = selected, Notice = notice, NoticeIsError = error };
        v.Rows = new ReplayRowView[rows];
        for (int i = 0; i < rows; i++)
            v.Rows[i] = new ReplayRowView
            {
                Title = i % 2 == 0 ? "SIBERIA STAKHANOV RALLY  12:34.56" : "ILYICH RALLY  1:23.45",
                Detail = i % 2 == 0 ? "RECORD  14:30  KEPT  GOLD OKTAN PETROL TANK" : "RECORD  14:30  KEPT",
                Flag = i % 3 == 0 ? "NO WORLD TAPE" : i % 3 == 1 ? "PREPARING" : "",
                Selectable = i % 4 != 3,
            };
        return v;
    }

    /// <summary>The replay screens (UI/HudReplay.cs, UI/ReplayOverlay.cs): everything fits at three screen sizes, hit tests match the drawing, the draw paths run.</summary>
    async void ReplayUiTests()
    {
        try { await ReplayUiTestsBody(); }
        catch (Exception e) { GD.PrintErr(e); _failures++; }
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    async System.Threading.Tasks.Task ReplayUiTestsBody()
    {
        GD.Print("Replay UI: list, overlay, pause row");
        foreach (var size in ReplayUiSizes)
        {
            string sz = $"{size.X}x{size.Y}";
            // --- list ---
            var r = Hud.ReplayListRectFor(size);
            Check(r.Position.X >= 0 && r.Position.Y >= 0 && r.End.X <= size.X && r.End.Y <= size.Y, $"{sz}: list panel inside the screen");
            bool fit = true;
            foreach (var row in ReplayUiList(8, 0).Rows)
            {
                Hud.ReplayRowTexts(row, r.Size.X, out string t, out string d, out string f);
                float inner = r.Size.X - 28;
                fit &= PixelFont.Measure(t) + (f.Length > 0 ? PixelFont.Measure(f) + 8 : 0) <= inner && PixelFont.Measure(d) <= inner;
            }
            Check(fit, $"{sz}: row title + flag and detail fit the list panel");
            Hud.ReplayRowTexts(new ReplayRowView { Title = "SHORT", Detail = "SMALL", Flag = "" }, r.Size.X, out string st, out _, out _);
            Check(st == "SHORT", $"{sz}: short names are not cut");
            Hud.ReplayRowTexts(new ReplayRowView { Title = new string('W', 80), Detail = "X", Flag = "NO WORLD TAPE" }, r.Size.X, out st, out _, out _);
            Check(st.EndsWith("."), $"{sz}: a long title is cut with a trailing dot");
            Check(PixelFont.Measure("UP/DOWN CHOOSE  ENTER WATCH  ESC BACK") <= r.Size.X - 8 && PixelFont.Measure("D-PAD CHOOSE  A WATCH  B BACK") <= r.Size.X - 8, $"{sz}: list footers fit");
            Check(PixelFont.Measure("NO LAPS RECORDED YET") <= r.Size.X - 16 && r.End.Y - 30 >= Hud.ReplayRowY(r, Hud.ReplayListWindow) - 4, $"{sz}: empty state fits, notice line sits under the rows");
            Check(Hud.ReplayFirstRow(20, 19) == 14 && Hud.ReplayFirstRow(20, 0) == 0 && Hud.ReplayFirstRow(3, 2) == 0 && Hud.ReplayFirstRow(0, 0) == 0, $"{sz}: the window follows the selection");

            // --- overlay ---
            var views = new (string Name, ReplayOverlayView V)[]
            {
                ("building", new() { Building = true, BuildProgress = 0.6f }),
                ("building pad", new() { Building = true, PadMode = true, BuildProgress = 5f }),
                ("shown", new() { Title = "SIBERIA STAKHANOV  12:34.56  RECORD  KEPT", Clock = 123.4f, Duration = 754.56f, Speed = 0.25f, Camera = "GAMEPLAY" }),
                ("paused", new() { Title = "ILYICH RALLY 1:23.45", Clock = 12.3f, Duration = 83.45f, Paused = true, Speed = 0.5f, Camera = "GAMEPLAY" }),
                ("pad", new() { Title = "ILYICH RALLY 1:23.45", Clock = 12.3f, Duration = 83.45f, Paused = true, PadMode = true, Camera = "BONNET" }),
                ("ended long", new() { Title = "SIBERIA STAKHANOV  12:34.56  RECORD  KEPT", Clock = 754.56f, Duration = 754.56f, Ended = true, Speed = 2f, Camera = "GAMEPLAY", Notice = "NO WORLD TAPE: CAR ONLY, NO TRAFFIC OR ANIMALS" }),
                ("notice pad", new() { PadMode = true, Clock = 1, Duration = 2, Notice = "NO WORLD TAPE: CAR ONLY, NO TRAFFIC OR ANIMALS" }),
                ("hidden", new() { Shown = false, Notice = "NO WORLD TAPE: CAR ONLY" }),
                ("hidden pad", new() { Shown = false, PadMode = true }),
                ("zero duration", new() { Duration = 0, Clock = 5 }),
                ("edit marks", new() { Title = "ILYICH STAKHANOV  1:23.45  RECORD  KEPT", Clock = 30f, Duration = 83.45f, Editable = true, Dirty = true, Pinned = true, WindowIn = 12.3f, WindowOut = 61f, KeyTimes = new[] { 20f, 40f }, Speed = 1f, Camera = "BONNET" }),
                ("edit panel", new() { Title = "ILYICH RALLY 1:23.45", Clock = 30f, Duration = 83.45f, Paused = true, Editable = true, EditOpen = true, EditRows = new[] { "SET IN HERE  0:12.3", "SET OUT HERE  OPEN", "CLEAR TRIM", "TRIM ON", "ADD CAMERA KEY  (2)", "CLEAR CAMERA KEYS", "LOOP OFF", "PIN OFF", "SAVE", "RESET EDITS", "CLOSE" }, EditSelected = 8, WindowIn = 12.3f }),
                ("edit panel pad", new() { Title = "SIBERIA STAKHANOV  12:34.56  RECORD  KEPT", Clock = 30f, Duration = 754.56f, PadMode = true, Editable = true, EditOpen = true, EditRows = new[] { "SET IN HERE  0:12.3", "SET OUT HERE  OPEN", "CLEAR TRIM", "TRIM ON", "ADD CAMERA KEY  (2)", "CLEAR CAMERA KEYS", "LOOP OFF", "PIN OFF", "SAVE", "RESET EDITS", "CLOSE" }, EditSelected = 10 }),
                ("free camera", new() { Title = "ILYICH FREE CAMERA", FreeCamera = true, FreeFollow = true, FreeLook = true, Camera = "FREE", Paused = true, Clock = 20, Duration = 60 }),
                ("free camera fixed", new() { Title = "SIBERIA FREE CAMERA", FreeCamera = true, Camera = "FREE", Clock = 20, Duration = 60 }),
                ("prompt unsaved", new() { Clock = 30f, Duration = 83.45f, Editable = true, Dirty = true, Prompt = new[] { "UNSAVED EDITS", "ENTER SAVE  D DISCARD  ESC CANCEL" } }),
                ("prompt keep first pad", new() { Clock = 30f, Duration = 83.45f, PadMode = true, Editable = true, Prompt = new[] { "KEEP THIS LAP FIRST", "EDITS BELONG TO A KEPT LAP", "A BACK TO THE LIST TO KEEP  B CANCEL" } }),
            };
            foreach (var (name, v) in views)
            {
                var l = ReplayOverlay.Layout(v, size);
                bool inside = true; string bad = "";
                var screen = new Rect2(Vector2.Zero, size);
                foreach (var t in l.Texts)
                {
                    var b = t.Bounds;
                    float w = PixelFont.Measure(t.Text, t.Scale) + 1, h = 7 * t.Scale + 1;
                    bool ok = screen.Encloses(b) && t.X >= b.Position.X + 1 && t.X + w <= b.End.X - 1 && t.Y >= b.Position.Y + 1 && t.Y + h <= b.End.Y - 1;
                    if (!ok) { inside = false; bad += $" [{t.Text} @{t.X},{t.Y} in {b}]"; }
                }
                Check(inside, $"{sz}: overlay '{name}' texts fit their panels{bad}");
                var panels = new System.Collections.Generic.List<Rect2>();
                foreach (var f in l.Fills) if (!f.Filled && f.Color.A > 0.89f && f.Color.A < 0.91f && f.Rect.Size.Y > 8) panels.Add(f.Rect);
                bool overlap = false;
                for (int i = 0; i < panels.Count; i++)
                    for (int j = i + 1; j < panels.Count; j++)
                        if (panels[i].Intersects(panels[j])) overlap = true;
                Check(!overlap, $"{sz}: overlay '{name}' panels do not overlap");
                bool hasState = l.Texts.Exists(t => t.Text is "PAUSED" or "PLAYING" or "END OF LAP");
                Check(hasState == (v.Shown && !v.Building), $"{sz}: overlay '{name}' state word only when the controls are shown");
            }
            var sh = ReplayOverlay.Layout(views[2].V, size);
            Check(sh.Texts.Exists(t => t.Text == "SPEED 0.25X") && sh.Texts.Exists(t => t.Text == "CAMERA GAMEPLAY") && sh.Texts.Exists(t => t.Text == "PLAYING"), $"{sz}: overlay shows state, speed and camera");
            Check(ReplayOverlay.FormatTime(83.45f) == "1:23.4" && ReplayOverlay.FormatTime(-1) == "0:00.0" && ReplayOverlay.SpeedText(1f) == "SPEED 1X" && ReplayOverlay.SpeedText(0.5f) == "SPEED 0.5X", $"{sz}: time and speed formats");
            var dim = sh.Texts.Find(t => t.Text == ", . STEP");
            var lit = ReplayOverlay.Layout(views[3].V, size).Texts.Find(t => t.Text == ", . STEP");
            Check(dim.Color.A < lit.Color.A, $"{sz}: the step hint is dimmed while playing");
            Check(sh.Texts.Exists(t => t.Text == "SPACE PLAY/PAUSE") && sh.Texts.Exists(t => t.Text == "ESC BACK"), $"{sz}: keyboard hints all drawn");

            // --- pause menu, everything shown ---
            int count = 8;          // replay, norms, continue, restart, exit, keys, instructions, settings
            float ph = 52 + count * 10 + 12;
            Check(ph <= 256 && ph <= size.Y, $"{sz}: pause menu with every row is {ph} px high");
            float lx = Mathf.Floor(size.X / 2 - PixelFont.Measure("       EXIT RACE") / 2f);
            foreach (string line in new[] { "       REPLAY", "       REPLAY - NO LAPS", "       NORMS AND MEDALS", "       SETTINGS" })
                Check(lx + PixelFont.Measure(line) <= size.X / 2 + 110 - 2, $"{sz}: pause line '{line.Trim()}' fits the 220 px panel");
        }

        // --- in the tree: hit tests and the draw paths ---
        foreach (var size in ReplayUiSizes)
        {
            string sz = $"{size.X}x{size.Y}";
            var vp = new SubViewport { Size = new Vector2I((int)size.X, (int)size.Y), RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
            AddChild(vp);
            var hud = new Hud { Mode = HudMode.Paused };
            vp.AddChild(hud);
            var overlay = new ReplayOverlay();
            vp.AddChild(overlay);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            foreach (bool pad in new[] { false, true })
            {
                hud.Pad = pad;
                string footer = hud.ReplayFooter(hud.ReplayListRect.Size.X - 8);
                Check(footer.Contains("DELETE") && PixelFont.Measure(footer) <= hud.ReplayListRect.Size.X - 8,
                    $"{sz}: replay deletion hint fits ({(pad ? "pad" : "keyboard")})");
            }
            var dr = hud.ReplayDeleteRect;
            Check(new Rect2(Vector2.Zero, size).Encloses(dr)
                && hud.ReplayDeleteRowAt(new Vector2(size.X / 2, dr.Position.Y + 72)) == 0
                && hud.ReplayDeleteRowAt(new Vector2(size.X / 2, dr.Position.Y + 86)) == 1
                && hud.ReplayDeleteRowAt(new Vector2(size.X / 2, dr.Position.Y + 52)) == -1,
                $"{sz}: delete card fits and only its two button rows are clickable");

            foreach (int n in new[] { 0, 1, 3, 6, 7, 25 })
                foreach (int sel in new[] { 0, n / 2, Math.Max(0, n - 1) })
                {
                    hud.ReplayList = ReplayUiList(n, sel);
                    var r = hud.ReplayListRect;
                    int first = Hud.ReplayFirstRow(n, sel);
                    bool ok = true;
                    for (int slot = 0; slot < Hud.ReplayListWindow; slot++)
                    {
                        int expect = first + slot < n ? first + slot : -1;
                        ok &= hud.ReplayRowAt(new Vector2(r.Position.X + r.Size.X / 2, Hud.ReplayRowY(r, slot) + 4)) == expect;
                        ok &= hud.ReplayRowAt(new Vector2(r.Position.X + r.Size.X / 2, Hud.ReplayRowY(r, slot) + 15)) == expect;
                    }
                    ok &= hud.ReplayRowAt(new Vector2(r.Position.X + r.Size.X / 2, r.Position.Y + 5)) == -1;
                    ok &= hud.ReplayRowAt(new Vector2(r.Position.X - 2, Hud.ReplayRowY(r, 0) + 4)) == -1;
                    ok &= hud.ReplayRowAt(new Vector2(r.Position.X + r.Size.X / 2, r.End.Y - 2)) == -1;
                    Check(ok, $"{sz}: ReplayRowAt maps the drawn rows (n={n}, selected={sel})");
                }

            // The pause menu's row hit test follows the added row.
            hud.ReplayListOpen = false;
            hud.PauseHasNorms = false; hud.PauseReplayShown = false;
            float TopOff(int cnt) => Mathf.Floor(size.Y / 2 - (52 + cnt * 10) / 2f) + 28;
            int rows0 = hud.PauseRowIds.Length;
            float yLast0 = TopOff(rows0) + (rows0 - 1) * 10 + 4;
            Check(hud.PauseRowAt(new Vector2(size.X / 2, yLast0)) == rows0 - 1 && hud.PauseRowAt(new Vector2(size.X / 2, yLast0 + 10)) == -1, $"{sz}: pause rows without REPLAY");
            hud.PauseRowIds = new[] { PauseRowId.Replay, PauseRowId.Norms, PauseRowId.Instructions, PauseRowId.Settings, PauseRowId.Continue, PauseRowId.Restart, PauseRowId.Exit };
            float yLast1 = TopOff(7) + 6 * 10 + 4;
            Check(hud.PauseRowAt(new Vector2(size.X / 2, yLast1)) == 6 && hud.PauseRowAt(new Vector2(size.X / 2, yLast1 + 10)) == -1, $"{sz}: pause rows with REPLAY count it");

            // Draw every screen once.
            int draws0 = ReplayOverlay.DrawCount;
            var states = new Action[]
            {
                () => { hud.ReplayListOpen = true; hud.ReplayList = ReplayUiList(0, 0); },
                () => hud.ReplayList = ReplayUiList(0, 0, "NO LAPS RECORDED YET", true),
                () => hud.ReplayList = ReplayUiList(25, 12, "NO WORLD TAPE FOR THIS LAP - ONLY THE CAR IS SHOWN", true),
                () => { hud.Pad = true; hud.ReplayList = ReplayUiList(6, 5, "OPENING..."); },
                () => { hud.ReplayDeleteOpen = true; hud.ReplayDeleteTitle = "A KEPT RECORDING WITH A LONG NAME"; hud.ReplayDeleteDetail = "KEPT ILYICH RALLY 1:23.45"; hud.ReplayDeleteConfirm = false; },
                () => { hud.ReplayDeleteConfirm = true; hud.Pad = false; },
                () => { hud.ReplayDeleteOpen = false; hud.ReplayListOpen = false; hud.PauseHasNorms = true; hud.PadConnected = true; hud.PadLost = true; hud.RadioOn = true; hud.RadioStationCount = 12; hud.RadioStation = 12; hud.PauseReplayAvailable = false; hud.PauseSelection = 7; },
                () => { hud.PauseReplayAvailable = true; hud.PauseSelection = 0; hud.Pad = false; },
                () => overlay.View = new ReplayOverlayView { Building = true, BuildProgress = 0.4f },
                () => overlay.View = new ReplayOverlayView { Title = "SIBERIA STAKHANOV  12:34.56  RECORD  KEPT", Clock = 30, Duration = 120, Notice = "NO WORLD TAPE: CAR ONLY" },
                () => overlay.View = new ReplayOverlayView { Shown = false, PadMode = true },
            };
            foreach (var set in states)
            {
                set();
                hud.QueueRedraw();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            Check(ReplayOverlay.DrawCount > draws0, $"{sz}: the draw paths ran without exceptions ({ReplayOverlay.DrawCount - draws0} overlay draws)");
            vp.GetParent()?.RemoveChild(vp); vp.Free();      // (at once: a queued free never runs before the test process quits and shows as leaked instances)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        // The audio mixer thread releases stopped playbacks a few mix steps after their player is freed; the process must not quit before that.
        await ToSignal(GetTree().CreateTimer(0.4), SceneTreeTimer.SignalName.Timeout);
        GD.Print(_failures == 0 ? "Replay UI: all ok" : $"Replay UI: {_failures} FAILED");
    }
}
