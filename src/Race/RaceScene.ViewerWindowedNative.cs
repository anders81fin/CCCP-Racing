using System;
using System.Collections;
using System.IO;
using System.Linq;
using CCCPRacing.Replay;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

// Windowed scenario "native-render" (--viewer-windowed-only=native): SETTINGS > VISUALS > RENDERING < PIXEL / NATIVE > through the real Main.tscn / PixelFrame.
// Measures the physical render targets (not only the setting), the logical 2D size, Camera3D projection, the real pointer path, settings.cfg, the world going on
// across the switch, a window resize / fullscreen, a full-screen presentation (the track tour) and the solo replay viewer. Shots: the game viewport and the window.
public partial class RaceScene
{
    SubViewport VnGame => GetViewport() as SubViewport;
    SubViewport VnOverlay => VwField(VwPixelFrame(), "_overlay") as SubViewport;

    void VnLogTargets(string when)
    {
        var f = VwPixelFrame(); var vp = VnGame; var tex = vp.GetTexture();
        VwLog("targets", ("when", when), ("native", PixelFrame.Native), ("window", DisplayServer.WindowGetSize()), ("root", GetTree().Root.GetVisibleRect().Size), ("scale", f.PixelScale), ("render_scale", f.RenderScale),
            ("vp_size", vp.Size), ("vp_override", vp.Size2DOverride), ("vp_stretch", vp.Size2DOverrideStretch), ("vp_visible", vp.GetVisibleRect().Size), ("tex", tex.GetSize()),
            ("overlay", VnOverlay.Size), ("overlay_override", VnOverlay.Size2DOverride), ("rect", ((TextureRect)VwField(f, "_rect")).Size), ("hud_screen", _hud.GetViewportRect().Size));
    }

    /// <summary>The PixelFrame contract for the current window: logical = ceil(window / scale); PIXEL renders the logical size, NATIVE logical × scale with the 2D kept logical; shown 1:1 either way.</summary>
    void VnCheckTargets(string when, bool native)
    {
        VnLogTargets(when);
        var f = VwPixelFrame(); var vp = VnGame; int s = f.PixelScale;
        Vector2 root = GetTree().Root.GetVisibleRect().Size;
        var logical = new Vector2I((int)MathF.Ceiling(root.X / s), (int)MathF.Ceiling(root.Y / s));
        var img = vp.GetTexture().GetImage();
        var want = native ? logical * s : logical;
        VwCheck(PixelFrame.Native == native && f.RenderScale == (native ? s : 1), $"{when}: setting {(native ? "NATIVE" : "PIXEL")} in effect (render scale {f.RenderScale}, pixel scale {s})");
        VwCheck(vp.Size == want && img.GetWidth() == want.X && img.GetHeight() == want.Y, $"{when}: game framebuffer {vp.Size}, read back {img.GetWidth()}x{img.GetHeight()}, expected {want} (window {root})");
        VwCheck((Vector2I)vp.GetVisibleRect().Size == logical && (Vector2I)_hud.GetViewportRect().Size == logical, $"{when}: 2D / HUD stays logical {logical} (visible {vp.GetVisibleRect().Size})");
        VwCheck(vp.Size2DOverride == (native ? logical : Vector2I.Zero) && vp.Size2DOverrideStretch == native, $"{when}: 2D override {vp.Size2DOverride} stretch {vp.Size2DOverrideStretch}");
        var rect = (TextureRect)VwField(f, "_rect");
        VwCheck((Vector2I)rect.Size == logical * s, $"{when}: shown at {rect.Size} = logical × {s} (1:1 for NATIVE's {vp.Size})");
    }

    Image VnWindowShot(string name)
    {
        RenderingServer.ForceDraw(false);
        var img = GetTree().Root.GetTexture().GetImage();
        string path = VwMediaPath(name);
        if (path != null) img.SavePng(path);
        VwLog("shot", ("name", name), ("size", $"{img.GetWidth()}x{img.GetHeight()}"));
        return img;
    }

    IEnumerable VnSwitch() { SyncVisualHud(); int i = Array.IndexOf(_hud.VisualRowIds, VisualRowId.Rendering); VwCheck(i >= 0, "RENDERING row present"); if (i >= 0) ChooseVisual(i); yield return null; yield return null; }

    string VnCfg() { var c = new ConfigFile(); c.Load(_records.Dir + "settings.cfg"); return c.GetValue("video", "rendering", "unset").ToString(); }

    IEnumerable VwNativeRender()
    {
        foreach (var x in VwWaitReady()) yield return x;
        foreach (var x in VrUntil(() => _state == State.Racing && _lapClock > 4f, 60 * 40, "the race is running")) yield return x;
        VwCheck(!PixelFrame.Native && VnCfg() == "unset", "fresh data: RENDERING starts PIXEL, nothing saved");
        VnCheckTargets("pixel-start", false);

        // ---- the world goes on across the switch (no reset, no reload, no hitch in the race clock)
        float lap0 = _lapClock; var pos0 = _car.Pos; ulong phys0 = Engine.GetPhysicsFrames(); var state0 = _state; int laps0 = _timer.LapsDone;
        foreach (var x in VnSwitch()) yield return x;
        VwCheck(PixelFrame.Native && _state == state0 && _timer.LapsDone == laps0 && _lapClock > lap0 && _lapClock - lap0 < 0.2f && _car.Pos.DistanceTo(pos0) < 5f && Engine.GetPhysicsFrames() > phys0,
            $"switch to NATIVE mid-race: state {_state}, lap clock {lap0:0.000} -> {_lapClock:0.000}, car moved {_car.Pos.DistanceTo(pos0):0.00} m, physics frames on");
        VnCheckTargets("native-race", true);
        VwCheck(VnCfg() == "1", "NATIVE saved as [video] rendering=1 (" + VnCfg() + ")");
        foreach (var x in VnSwitch()) yield return x;
        VnCheckTargets("pixel-again", false);
        VwCheck(VnCfg() == "0", "PIXEL saved as [video] rendering=0");

        // ---- comparable frames: the race frozen (process disabled) and the same frame in both modes; Camera3D projection the same in logical pixels
        var keepMode = ProcessMode; ProcessMode = ProcessModeEnum.Disabled;
        for (int i = 0; i < 4; i++) yield return null;
        var carPx = _cam.UnprojectPosition(_car.Pos); var frozen = _car.Pos;
        VwGrab(VnGame, "pixel-gameplay-viewport"); VnWindowShot("pixel-gameplay-window");
        foreach (var x in VnSwitch()) yield return x;
        for (int i = 0; i < 3; i++) yield return null;
        var carNx = _cam.UnprojectPosition(_car.Pos);
        VwGrab(VnGame, "native-gameplay-viewport"); VnWindowShot("native-gameplay-window");
        VwCheck(_car.Pos == frozen && carPx.DistanceTo(carNx) < 0.01f, $"Camera3D.UnprojectPosition stays in logical pixels: car at {carPx} PIXEL, {carNx} NATIVE");
        ProcessMode = keepMode;
        for (int i = 0; i < 4; i++) yield return null;

        // ---- the real menu path: Esc, SETTINGS, VISUALS, RENDERING (keys); VISUALS shot in both modes
        foreach (var x in VrTap(Key.Escape)) yield return x;
        for (int i = 0; i < 12; i++) yield return null;
        for (int i = 0; i < 12 && _hud.PauseSelection != PauseRowIndex(PauseRowId.Settings); i++) foreach (var x in VrTap(Key.Down)) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 8; i++) yield return null;
        for (int i = 0; i < 12 && _hud.SettingsRowIds[_hud.SettingsSelection] != SettingsRowId.Visuals; i++) foreach (var x in VrTap(Key.Down)) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 8; i++) yield return null;
        VwCheck(_hud.VisualOpen && _state == State.Paused, "VISUALS open over the paused race");
        foreach (var x in VwVisualTo(VisualRowId.Rendering)) yield return x;
        _hud.VisualRowTexts(VisualRowId.Rendering, out string label, out string value);
        VwCheck(label == "RENDERING" && value == "< NATIVE >", $"the row reads {label} {value}");
        for (int i = 0; i < 45; i++) yield return null;
        VwGrab(VnGame, "native-visuals-viewport"); VnWindowShot("native-visuals-window");
        var r = _hud.VisualRect;
        VwCheck(r.Position.Y >= 0 && r.End.Y <= _hud.GetViewportRect().Size.Y, $"VISUALS panel {r} fits the logical screen {_hud.GetViewportRect().Size} with {_hud.VisualRowIds.Length} rows");
        float paused = _lapClock;
        foreach (var x in VrTap(Key.Left)) yield return x;
        VwCheck(!PixelFrame.Native && _hud.VisualOpen && _lapClock == paused, "LEFT on RENDERING: PIXEL, the menu stays open, the paused race clock unchanged");
        VnCheckTargets("pixel-menu", false);
        for (int i = 0; i < 45; i++) yield return null;
        VwGrab(VnGame, "pixel-visuals-viewport"); VnWindowShot("pixel-visuals-window");
        foreach (var x in VrTap(Key.Enter)) yield return x;
        VwCheck(PixelFrame.Native, "ENTER on RENDERING: NATIVE");

        // ---- the real pointer: warp the system cursor onto the RENDERING row, a motion and a click as the window delivers them (PixelFrame forwards them; RawInput reads the
        // HUD's own mouse position). PIXEL is the pre-change baseline (its viewport is assigned nothing new): NATIVE must behave the same, in the same coordinate space.
        int row = Array.IndexOf(_hud.VisualRowIds, VisualRowId.Rendering);
        var seen = new System.Collections.Generic.Dictionary<bool, (Vector2 hud, int rowAt, bool clicked, Transform2D xf, int rootRow)>();
        bool oldMotion = _vwDrv.PassForeignMotion;
        _vwDrv.PassForeignMotion = true;      // this probe deliberately needs the window motion; SetControl only admits keys/buttons
        foreach (bool native in new[] { false, true })
        {
            if (PixelFrame.Native != native) foreach (var x in VnSwitch()) yield return x;      // (each pass measures the mode it names, whatever the click before did)
            int s = VwPixelFrame().PixelScale;
            var logicalPt = new Vector2(r.Position.X + r.Size.X / 2, r.Position.Y + 36 + row * 16 + 8);
            Input.WarpMouse(logicalPt * s);
            for (int i = 0; i < 6; i++) yield return null;
            var warpOnly = _hud.GetLocalMousePosition();
            _vwDrv.SetControl(true);
            Input.ParseInputEvent(new InputEventMouseMotion { Position = logicalPt * s, GlobalPosition = logicalPt * s, Device = ViewerWindowedDriver.Sentinel });      // (the driver keeps unmarked motion away from the game)
            yield return null; yield return null;
            _vwDrv.SetControl(false);
            var hudMouse = _hud.GetLocalMousePosition(); var rootMouse = GetTree().Root.GetMousePosition();
            var xf = _hud.GetGlobalTransformWithCanvas(); int rootRow = _hud.VisualRowAt(xf.AffineInverse() * (rootMouse / s));
            VwLog("pointer", ("native", native), ("warp_physical", logicalPt * s), ("root_mouse", rootMouse), ("hud_mouse_warp", warpOnly), ("hud_mouse", hudMouse), ("vp_mouse", VnGame.GetMousePosition()),
                ("frame_mouse", VwPixelFrame().GetLocalMousePosition()), ("frame_root_mouse", VwPixelFrame().GetViewport().GetMousePosition()), ("frame_xf", VwPixelFrame().GetGlobalTransformWithCanvas()), ("frame_processing", VwPixelFrame().IsProcessing()),
                ("row_at", _hud.VisualRowAt(hudMouse)), ("root_div_scale_row", rootRow), ("hud_xform", xf), ("want_row", row));
            if (rootMouse.DistanceTo(logicalPt * s) >= 1.5f) VwFinding("pointer-warp", $"the compositor did not move the cursor (root mouse {rootMouse}, asked {logicalPt * s}) in {(native ? "NATIVE" : "PIXEL")}");
            bool before = PixelFrame.Native;
            _vwDrv.SetControl(true);
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = rootMouse, GlobalPosition = rootMouse });
            yield return null;
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = rootMouse, GlobalPosition = rootMouse });
            yield return null; yield return null;
            _vwDrv.SetControl(false);
            bool clicked = PixelFrame.Native != before;
            seen[native] = (hudMouse, _hud.VisualRowAt(hudMouse), clicked, xf, rootRow);
            if (clicked) foreach (var x in VnSwitch()) yield return x;
        }
        var (pp, nn) = (seen[false], seen[true]);
        _vwDrv.PassForeignMotion = oldMotion;
        VwCheck(pp.clicked && nn.clicked && pp.rowAt == row && nn.rowAt == row, "window mouse reaches RENDERING and changes the setting in both PIXEL and NATIVE");
        VwCheck(pp.hud == nn.hud && pp.rowAt == nn.rowAt && pp.clicked == nn.clicked, $"pointer parity: HUD mouse {pp.hud} / {nn.hud}, row {pp.rowAt} / {nn.rowAt}, click switched {pp.clicked} / {nn.clicked} (PIXEL / NATIVE)");
        VwCheck(pp.xf.IsEqualApprox(nn.xf) && pp.rootRow == row && nn.rootRow == row, $"the HUD's canvas transform is the same in both modes and window mouse / scale lands on RENDERING ({pp.rootRow} / {nn.rootRow})");
        if (!pp.clicked && pp.hud == Vector2.Zero)
            VwFinding("pointer-preexisting", $"PIXEL and NATIVE alike: the HUD mouse inside PixelFrame's game SubViewport reads {pp.hud} with the cursor on a row (window {GetTree().Root.GetMousePosition()}), so a click picks no row; nothing pushes input into that viewport (pre-existing, not changed by RENDERING)");
        if (!PixelFrame.Native) foreach (var x in VnSwitch()) yield return x;
        for (int i = 0; i < 3; i++) foreach (var x in VrTap(Key.Escape)) yield return x;
        for (int i = 0; i < 8; i++) yield return null;
        VwCheck(_state == State.Racing && PixelFrame.Native, "back in the race, NATIVE");

        // ---- window resize and fullscreen: the frame refits (NATIVE), and PIXEL after it
        var root0 = GetTree().Root.GetVisibleRect().Size;
        // (the compositor owns a floating window's size: resize this child's own window by its pid, to a size the scale does not divide, so logical × scale overhangs it)
        static string Hypr(int w, int h)
        {
            // (.NET Process, not OS.Execute: Godot's execute dropped the Lua string quotes)
            var psi = new System.Diagnostics.ProcessStartInfo("hyprctl") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            psi.ArgumentList.Add("dispatch"); psi.ArgumentList.Add($"hl.dsp.window.resize({{ x = {w}, y = {h}, relative = false, window = \"pid:{System.Environment.ProcessId}\" }})");
            try { using var q = System.Diagnostics.Process.Start(psi); string o = q.StandardOutput.ReadToEnd() + q.StandardError.ReadToEnd(); q.WaitForExit(5000); return $"exit {q.ExitCode} {o.Trim()}"; }
            catch (Exception e) { return "failed " + e.Message; }
        }
        VwLog("hyprctl-resize", ("result", Hypr(821, 461)));
        for (int i = 0; i < 40; i++) yield return null;
        var rootR = GetTree().Root.GetVisibleRect().Size;
        VwCheck(rootR != root0, $"the window was resized: {root0} -> {rootR}");
        VnCheckTargets($"native-resized-{rootR.X}x{rootR.Y}", true);
        VnWindowShot("native-resized-window");
        VwLog("hyprctl-resize", ("result", Hypr(768, 432)));
        for (int i = 0; i < 40; i++) yield return null;
        DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
        for (int i = 0; i < 45; i++) yield return null;
        VnCheckTargets($"native-fullscreen-{DisplayServer.WindowGetSize().X}x{DisplayServer.WindowGetSize().Y}", true);
        VnWindowShot("native-fullscreen-window");
        foreach (var x in VnSwitch()) yield return x;
        VnCheckTargets("pixel-fullscreen", false);
        foreach (var x in VnSwitch()) yield return x;
        DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
        for (int i = 0; i < 40; i++) yield return null;
        VnCheckTargets($"native-back-{DisplayServer.WindowGetSize().X}x{DisplayServer.WindowGetSize().Y}", true);

        // ---- the solo replay viewer: its own viewport mirrors the frame's, follows a switch while open
        var fx = LapArchive.For(Path.Combine(VlArg("--vw-fx") ?? "/tmp/cccp-lc-fx", "ilyich"));
        var info = fx.List().FirstOrDefault(i => i.Openable);
        VwCheck(info != null, "a fixture lap to view");
        if (info != null)
        {
            var sess = OpenReplayViewer(fx.Open(info.Id), out string err); VwCheck(sess != null, "viewer opens: " + err);
            if (sess != null)
            {
                foreach (var x in VrUntil(() => sess.State == ReplayViewerState.Playing, 7200, "viewer ready")) yield return x;
                for (int i = 0; i < 20; i++) yield return null;
                var vvp = sess.Scene.GetViewport() as SubViewport; var game = VnGame;
                VwLog("viewer-targets", ("native", PixelFrame.Native), ("viewer_vp", vvp.Size), ("viewer_override", vvp.Size2DOverride), ("viewer_visible", vvp.GetVisibleRect().Size), ("game_vp", game.Size), ("tex", vvp.GetTexture().GetSize()));
                VwCheck(vvp.Size == game.Size && vvp.Size2DOverride == game.Size2DOverride && vvp.Size2DOverrideStretch && vvp.GetVisibleRect().Size == game.GetVisibleRect().Size,
                    $"NATIVE viewer: framebuffer {vvp.Size} = the frame's, 2D {vvp.GetVisibleRect().Size}");
                VwGrab(vvp, "native-viewer-world"); VnWindowShot("native-viewer-window");
                var vcar = sess.Scene._cam.UnprojectPosition(sess.Scene._car.Pos);
                PixelFrame.Native = false;      // (the viewer has no VISUALS of its own: the static is what the menu row sets)
                for (int i = 0; i < 6; i++) yield return null;
                VwLog("viewer-targets", ("native", PixelFrame.Native), ("viewer_vp", vvp.Size), ("viewer_override", vvp.Size2DOverride), ("game_vp", game.Size));
                VwCheck(vvp.Size == game.Size && vvp.Size == (Vector2I)game.GetVisibleRect().Size && vvp.Size2DOverride == Vector2I.Zero, $"PIXEL while open: the viewer follows at {vvp.Size}");
                VwGrab(vvp, "pixel-viewer-world");
                PixelFrame.Native = true;
                for (int i = 0; i < 6; i++) yield return null;
                VwCheck(vvp.Size == game.Size && vvp.Size2DOverrideStretch && sess.State == ReplayViewerState.Playing, $"NATIVE again while open: {vvp.Size}, still playing");
                VwLog("viewer-car", ("logical", vcar));
                sess.Close();
                foreach (var x in VrUntil(() => sess.State == ReplayViewerState.Closed, 600, "viewer closed")) yield return x;
                for (int i = 0; i < 10; i++) yield return null;
                VnCheckTargets("native-after-viewer", true);
            }
        }

        // ---- a full-screen presentation: the track tour on the title (the HUD moves into PixelFrame's overlay)
        EnterTitle(); OpenMenu(MenuItem.Single, MenuItem.Race); OpenTrackCard(MenuItem.Race);
        _cardSel = Array.FindIndex(Tracks, t => t.id == _trackId); OpenTour();
        foreach (var x in VrUntil(() => PresentationActive && VwField(VwPixelFrame(), "_previewLayout") is true, 600, "the tour presentation is laid out")) yield return x;
        for (int i = 0; i < 20; i++) yield return null;
        foreach (var x in VrTap(Key.Space)) yield return x;      // (a drone film's black card waits for a key)
        for (int i = 0; i < 120; i++) yield return null;
        VnLogTargets("native-tour");
        var ov = VnOverlay; var gv = VnGame; int sc = VwPixelFrame().PixelScale;
        VwCheck(PresentationFullscreen && gv.Size == (Vector2I)gv.GetVisibleRect().Size * sc && ov.Size == (Vector2I)ov.GetVisibleRect().Size * sc && ov.Size2DOverrideStretch,
            $"NATIVE tour: world {gv.Size} and overlay {ov.Size} at ×{sc}, overlay 2D {ov.GetVisibleRect().Size}");
        VnWindowShot("native-tour-window");
        foreach (var x in VnSwitch()) yield return x;
        for (int i = 0; i < 4; i++) yield return null;
        VwCheck(gv.Size == (Vector2I)gv.GetVisibleRect().Size && ov.Size == (Vector2I)ov.GetVisibleRect().Size && ov.Size2DOverride == Vector2I.Zero, $"PIXEL tour: world {gv.Size}, overlay {ov.Size} logical");
        VnWindowShot("pixel-tour-window");
        VwCheck(VnCfg() == "0", "settings.cfg follows the last switch (0)");
    }
}
