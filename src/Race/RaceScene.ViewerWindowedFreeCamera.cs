using System;
using System.Collections;
using System.IO;
using System.Linq;
using CCCPRacing.Replay;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;
public partial class RaceScene
{
    IEnumerable VwFreeCamera()
    {
        foreach (var x in VwWaitReady()) yield return x;
        var fx = LapArchive.For(Path.Combine(VlArg("--vw-fx") ?? "/tmp/cccp-lc-fx", "ilyich"));
        var originalInfo = fx.List().First(i => i.Openable && i.Retained); var original = fx.Open(originalInfo.Id); string hash = original.Sha256();
        string cloneRoot = Path.Combine(VlArg("--vw-media") ?? "media/free-camera-phase1-20261029", "fixture");
        string cloneDir = Path.Combine(cloneRoot, originalInfo.Id); Directory.CreateDirectory(cloneDir);
        foreach (var file in new[] { "ghost.bin", "tape.bin", "meta.json" })
            if (File.Exists(Path.Combine(originalInfo.Dir, file))) File.Copy(Path.Combine(originalInfo.Dir, file), Path.Combine(cloneDir, file), true);
        var archive = new LapArchive(cloneRoot); var info = archive.List().Single(); var source = archive.Open(info.Id);
        VwCheck(source.Sha256() == hash && !File.Exists(Path.Combine(cloneDir, "edits.json")), "isolated whole-lap clone has unchanged tape bytes and no pre-existing trim");
        var s = OpenReplayViewer(source, out string err); VwCheck(s != null, "free camera viewer opens: " + err);
        if (s == null) yield break;
        s.SetEditContext(archive, info); s.SetTitle("FREE CAMERA TEST");
        foreach (var x in VrUntil(() => s.State == ReplayViewerState.Playing, 7200, "viewer ready")) yield return x;
        GetWindow().GrabFocus();
        foreach (var x in VrUntil(() => GetWindow().HasFocus(), 600, "test window focused")) yield return x;
        var v = s.Scene; v.ViewerSetPaused(true); v.ViewerSeek(10f);
        for (int i = 0; i < 4; i++) yield return null;
        float clock = v.ViewerClock, fov = v.ViewerCameraFov; var normal = v.ViewerCameraPose;
        var oldMouse = Input.MouseMode;
        foreach (var x in VwDirectFreeCamera(s, v, archive, info, cloneDir)) yield return x;
        v.ViewerSetPaused(true); v.ViewerSeek(10f); v.ViewerCamera = ViewerCameraMode.TopDown;
        for (int i = 0; i < 4; i++) yield return null;
        clock = v.ViewerClock; fov = v.ViewerCameraFov; normal = v.ViewerCameraPose;
        foreach (var x in VrTap(Key.V)) yield return x;
        int row = Array.FindIndex(s.EditRowTexts, r => r.StartsWith("FREE CAMERA"));
        VwCheck(row >= 0, "free camera is offered in EDIT/EXPORT");
        foreach (var x in ToExportRow(s, row)) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        VwCheck(s.FreeCameraActive && !s.EditOpen && v.ViewerCameraPose.IsEqualApprox(normal) && v.ViewerCameraFov == fov, "enter preserves current pose and lens without zoom");
        if (!s.FreeCameraActive)
        {
            VwLog("free-entry-state", ("editOpen", s.EditOpen), ("selected", s.OverlayView.EditSelected), ("rows", string.Join(",", s.EditRowTexts)), ("state", s.State));
            foreach (var arrival in _vwDrv.Arrivals.TakeLast(24)) VwLog("entry-input", ("event", arrival.What), ("sentinel", arrival.Sentinel));
            yield break;
        }
        Vector3 p = v.ViewerFreeRig.Position;
        foreach (var x in VrTap(Key.W, 30)) yield return x;
        VwCheck(v.ViewerFreeRig.Position.DistanceTo(p) > .5f && Mathf.Abs(v.ViewerFreeRig.Position.Y - p.Y) < .001f && v.ViewerClock == clock,
            "W moves camera horizontally while replay is paused");
        p = v.ViewerFreeRig.Position;
        foreach (var x in VrTap(Key.E, 20)) yield return x;
        VwCheck(v.ViewerFreeRig.Position.Y > p.Y + .5f && !s.EditOpen && !s.ExportOpen && !s.Dirty, "E raises camera and does not open edit/export or dirty saved choices");
        v.ViewerFreeRig.Move(Vector3.Down, .1f, 100000);
        for (int i = 0; i < 4; i++) yield return null;
        VwCheck(v.ViewerCameraPose.Origin.Y >= v._track.MeshHeight(v.ViewerCameraPose.Origin.X, v.ViewerCameraPose.Origin.Z) + .34f,
            "production free camera cannot descend underneath rendered terrain");
        var groundShot = v.ViewerCaptureFreeCamera();
        VwCheck(groundShot.Y >= v._track.MeshHeight(groundShot.X, groundShot.Z) + .34f, "captured camera key also stays above ground");
        v.ViewerFreeRig.Move(Vector3.Up, .1f, 200);
        for (int i = 0; i < 4; i++) yield return null;
        var pose = v.ViewerCameraPose;
        foreach (var x in VrTap(Key.Left, 20)) yield return x;
        float backClock = v.ViewerClock;
        VwCheck(backClock < clock && v.ViewerPaused && s.FreeCameraActive && v.ViewerCameraPose.IsEqualApprox(pose),
            "free-camera LEFT seeks backward while preserving paused state and fixed camera placement");
        foreach (var x in VrTap(Key.Right, 20)) yield return x;
        VwCheck(v.ViewerClock > backClock && s.FreeCameraActive && v.ViewerPaused,
            "free-camera RIGHT seeks forward without leaving camera controls");
        float here = v.ViewerClock, initialSpeed = v.ViewerSpeed;
        foreach (var x in VrTap(Key.Up)) yield return x;
        VwCheck(v.ViewerSpeed > initialSpeed && s.FreeCameraActive && v.ViewerClock == here && v.ViewerPaused,
            "UP increases free-camera playback speed without seeking or resuming");
        foreach (var x in VrTap(Key.Down)) yield return x;
        VwCheck(v.ViewerSpeed == initialSpeed && s.FreeCameraActive, "DOWN restores the previous playback speed inside free camera");
        foreach (var x in VrTap(Key.Comma)) yield return x;
        VwCheck(Mathf.Abs(v.ViewerClock - (here - .1f)) < .001f && v.ViewerPaused && s.FreeCameraActive && v.ViewerSpeed == initialSpeed,
            $"free-camera COMMA steps one frame back while paused (frame stepping since 2026-10-30; clock {here} -> {v.ViewerClock})");
        here = v.ViewerClock;
        foreach (var x in VrTap(Key.B)) yield return x;
        foreach (var x in VrTap(Key.Right, 40)) yield return x;
        foreach (var x in VrTap(Key.Down)) yield return x;
        VwCheck(s.CameraMotionOpen && v.ViewerClock == here && v.ViewerSpeed == initialSpeed, "motion card owns arrows without seeking or changing playback speed");
        foreach (var x in VrTap(Key.Up)) yield return x;
        _vwDrv.SetControl(true);
        Input.ParseInputEvent(VwKey(Key.Right, true, 0));
        for (int i = 0; i < 4; i++) yield return null;
        foreach (var x in VrTap(Key.Escape)) yield return x;
        for (int i = 0; i < 40; i++) yield return null;
        VwCheck(!s.CameraMotionOpen && s.FreeCameraActive && v.ViewerClock == here,
            "closing motion card with RIGHT held waits for release before seeking");
        Input.ParseInputEvent(VwKey(Key.Right, false, 0));
        for (int i = 0; i < 4; i++) yield return null;
        _vwDrv.SetControl(false);
        v.ViewerSeek(clock);
        for (int i = 0; i < 4; i++) yield return null;
        foreach (var x in VwFreeScreenshot(s, v)) yield return x;
        GetWindow().GrabFocus();
        foreach (var x in VrUntil(() => GetWindow().HasFocus(), 600, "mouse test window focused")) yield return x;
        for (int i = 0; i < 2; i++) yield return null;
        VwCheck(Input.MouseMode == Input.MouseModeEnum.Captured, "free camera captures mouse automatically, including after motion card closes");
        Input.ParseInputEvent(new InputEventMouseMotion { Relative = new Vector2(80, 25), Device = ViewerWindowedDriver.Sentinel }); yield return null; yield return null;
        VwCheck(!v.ViewerCameraPose.Basis.IsEqualApprox(pose.Basis), "mouse motion turns the camera with no button held");
        _vwDrv.SetControl(true);
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Device = 0 }); yield return null;
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false, Device = 0 }); yield return null;
        VwCheck(Input.MouseMode == Input.MouseModeEnum.Captured, "right mouse button no longer gates or releases look");
        _vwDrv.SetControl(false);
        foreach (var x in VrTap(Key.G)) yield return x;
        p = v.ViewerFreeRig.Position; v.ViewerSeek(12f); for (int i = 0; i < 4; i++) yield return null;
        VwCheck(v.ViewerFreeRig.Position == p && (-v.ViewerCameraPose.Basis.Z).Dot((v.ViewerCarPosition - p).Normalized()) > .9998f,
            $"fixed camera tracks car with aim only after seek (follow {v.ViewerFreeRig.Follow}, look {v.ViewerFreeRig.Look}, delta {v.ViewerFreeRig.Position.DistanceTo(p)}, dot {(-v.ViewerCameraPose.Basis.Z).Dot((v.ViewerCarPosition - p).Normalized())})");
        foreach (var x in VrTap(Key.F)) yield return x;
        Vector3 offset = v.ViewerFreeRig.Position - v.ViewerCarPosition;
        foreach (var x in VrTap(Key.Right)) yield return x;
        float followedClock = v.ViewerClock;
        VwCheck(followedClock > 12f && s.FreeCameraActive, "arrow seek works with follow and look-at enabled");
        for (int i = 0; i < 4; i++) yield return null;
        VwCheck((v.ViewerFreeRig.Position - v.ViewerCarPosition).DistanceTo(offset) < .001f && v.ViewerFreeRig.Follow && v.ViewerFreeRig.Look,
            $"position follow and look-at are independently enabled and follow seeks (follow {v.ViewerFreeRig.Follow}, look {v.ViewerFreeRig.Look}, delta {(v.ViewerFreeRig.Position - v.ViewerCarPosition).DistanceTo(offset)})");
        foreach (var x in VrShot("free-camera")) yield return x;
        foreach (var x in VrTap(Key.Space)) yield return x;
        for (int i = 0; i < 20; i++) yield return null;
        bool sampled = false, follows = false; float sampleClock = 0;
        void Sample() { sampleClock = v.ViewerClock; follows = !v.ViewerPaused && (v.ViewerFreeRig.Position - v.ViewerCarPosition).DistanceTo(offset) < .01f; sampled = true; }
        RenderingServer.FramePostDraw += Sample;
        for (int i = 0; i < 5 && !sampled; i++) yield return null;
        RenderingServer.FramePostDraw -= Sample;
        VwCheck(sampled && follows && sampleClock > 15f, $"free camera and follow also work during playback (clock {sampleClock}, sampled {sampled}, follows {follows})");
        float playingClock = v.ViewerClock;
        foreach (var x in VrTap(Key.Left)) yield return x;
        VwCheck(!v.ViewerPaused && s.FreeCameraActive && v.ViewerClock < playingClock
            && (v.ViewerFreeRig.Position - v.ViewerCarPosition).DistanceTo(offset) < .01f,
            "seeking during playback keeps playback running and preserves camera follow offset");
        foreach (var x in VrTap(Key.Space)) yield return x;
        foreach (var x in VrTap(Key.R)) yield return x;
        VwCheck(Input.MouseMode == oldMouse, "leaving free camera restores original cursor mode");
        VwCheck(!s.FreeCameraActive && s.State == ReplayViewerState.Playing && v.ViewerCamera == ViewerCameraMode.TopDown && v.ViewerCameraFov == fov && !s.Dirty,
            "R returns to standard top-down without closing, zooming or saving camera edits");
        foreach (var x in VrTap(Key.Right)) yield return x;
        VwCheck(v.ViewerClock > 16f, "fresh replay seek works after camera mode exits");
        foreach (var x in VrTap(Key.V)) yield return x;
        foreach (var x in ToExportRow(s, row)) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        GetWindow().GrabFocus();
        foreach (var x in VrUntil(() => GetWindow().HasFocus(), 600, "exit test window focused")) yield return x;
        _vwDrv.SetControl(true);
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Device = 0 }); yield return null;
        GetWindow().EmitSignal(Window.SignalName.FocusExited); yield return null;
        VwCheck(Input.MouseMode == oldMouse, "focus loss releases mouse capture");
        GetWindow().EmitSignal(Window.SignalName.FocusEntered); yield return null;
        VwCheck(Input.MouseMode == Input.MouseModeEnum.Captured, "returning focus automatically resumes free-camera mouse look");
        GetWindow().EmitSignal(Window.SignalName.FocusExited); yield return null;
        _vwDrv.SetControl(false);
        foreach (var x in VrTap(Key.R)) yield return x;
        GetWindow().EmitSignal(Window.SignalName.FocusEntered); yield return null;
        _vwDrv.SetControl(true);
        VwCheck(!s.FreeCameraActive && Input.MouseMode == oldMouse, "R from an Edit-entered free camera returns too and keeps the cursor free");
        foreach (var x in VrTapCtrl(Key.S)) yield return x;
        VwCheck(!s.FreeCameraActive, "CTRL+S is the direct SAVE and does not fly the camera");
        foreach (var x in VrUntil(() => !s.SaveBusy, 1800, "CTRL+S save finished")) yield return x;
        VwCheck(!s.Dirty && File.Exists(Path.Combine(cloneDir, "edits.json")), "CTRL+S saved the clone's (unchanged) viewing choices");
        _vwDrv.SetControl(true);
        s.Close(); yield return null;
        VwCheck(Input.MouseMode == oldMouse, "forced viewer close releases mouse capture");
        foreach (var g in _vwDrv.Arrivals.Where(a => !a.Sentinel).GroupBy(a => string.Join(":", a.What.Split(':').Take(2))).OrderByDescending(g => g.Count()).Take(12))
            VwLog("device0-input", ("note", "includes this test's own taps injected as device 0"), ("kind", g.Key), ("count", g.Count()), ("first_frame", g.First().Frame), ("last_frame", g.Last().Frame));
        _vwDrv.SetControl(false);
        foreach (var x in VrUntil(() => s.State == ReplayViewerState.Closed, 1800, "closed")) yield return x;
        VwCheck(fx.Open(originalInfo.Id).Sha256() == hash && archive.Open(info.Id).Sha256() == hash, "original driving and world tapes unchanged");

    /// <summary>The owner's direct free camera (2026-11-05): W/A/S/D or an ordinary mouse motion in the plain viewer enter it from the camera in view, R returns to that camera.
    /// Gated by EDIT/EXPORT, prompts and focus; a pointer warp after R (or after focus/cards) does not re-enter; flying never keys, dirties or saves.</summary>
    IEnumerable VwDirectFreeCamera(ReplayViewerSession s, RaceScene v, LapArchive archive, LapEntryInfo info, string cloneDir)
    {
        string edits = Path.Combine(cloneDir, "edits.json");
        bool Untouched() => !s.Dirty && v.ViewerEdits.Keys.Count == 0 && !File.Exists(edits) && !s.SaveBusy;
        IEnumerable Motion(Vector2 rel) { Input.ParseInputEvent(new InputEventMouseMotion { Relative = rel, Device = ViewerWindowedDriver.Sentinel }); yield return null; yield return null; }
        var size = new Vector2(480, 270);
        bool padView = s.OverlayView.PadMode; s.OverlayView.PadMode = false;      // (the keyboard help; a real pad's event may have switched the overlay to pad hints)
        var hints = ReplayOverlay.Layout(s.OverlayView, size).Texts.Select(t => t.Text).ToList();
        s.OverlayView.PadMode = padView;
        VwCheck(hints.Contains("WASD/MOUSE FREE CAMERA"), "plain viewer help names the WASD/MOUSE free camera [" + string.Join(",", hints) + "]");
        float clock = v.ViewerClock, fov = v.ViewerCameraFov, speed = v.ViewerSpeed; var normal = v.ViewerCameraPose; var mouse0 = Input.MouseMode;
        VwCheck(v.ViewerPaused && v.ViewerCamera == ViewerCameraMode.TopDown && !s.FreeCameraActive && Untouched(), "direct: starts paused in the plain top-down viewer with no edits");

        // gating: the EDIT panel, a prompt, the export card and an unfocused window own W/A/S/D and the mouse
        foreach (var x in VrTap(Key.V)) yield return x;
        int sel = s.OverlayView.EditSelected;
        foreach (var x in VrTap(Key.W, 6)) yield return x;
        foreach (var x in Motion(new Vector2(70, 20))) yield return x;
        VwCheck(s.EditOpen && !s.FreeCameraActive && s.OverlayView.EditSelected == sel && v.ViewerCameraPose.IsEqualApprox(normal), "direct: W and the mouse do nothing while EDIT is open");
        int reset = Array.FindIndex(s.EditRowTexts, r => r.StartsWith("RESET EDITS"));
        foreach (var x in ToExportRow(s, reset)) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        bool prompt = s.OverlayView.Prompt.Length > 0;
        foreach (var x in VrTap(Key.D, 6)) yield return x;
        foreach (var x in Motion(new Vector2(70, 20))) yield return x;
        VwCheck(prompt && !s.FreeCameraActive && s.OverlayView.Prompt.Length > 0 && Untouched(), "direct: a modal prompt keeps D and the mouse");
        foreach (var x in VrTap(Key.Escape)) yield return x;      // cancel the question
        int export = Array.FindIndex(s.EditRowTexts, r => r.StartsWith("EXPORT"));
        if (!s.EditOpen) foreach (var x in VrTap(Key.V)) yield return x;
        foreach (var x in ToExportRow(s, export)) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        bool exportOpen = s.ExportOpen;
        foreach (var x in VrTap(Key.S, 6)) yield return x;
        foreach (var x in Motion(new Vector2(70, 20))) yield return x;
        VwCheck(exportOpen && s.ExportOpen && !s.FreeCameraActive && Untouched(), "direct: the export card keeps S and the mouse");
        for (int i = 0; i < 3 && (s.ExportOpen || s.EditOpen); i++) foreach (var x in VrTap(Key.Escape)) yield return x;
        VwCheck(!s.ExportOpen && !s.EditOpen && s.State == ReplayViewerState.Playing, "direct: ESC closes the card and panel without leaving the viewer");
        foreach (var x in Motion(new Vector2(70, 20))) yield return x;
        VwCheck(!s.FreeCameraActive && s.DirectMouseQuiet, "direct: the motion right after a card closes is ignored (quiet time)");
        s.FocusOverrideForTest = false;
        foreach (var x in VrUntil(() => !s.DirectMouseQuiet, 120, "quiet time over (unfocused)")) yield return x;
        foreach (var x in VrTap(Key.A, 6)) yield return x;
        foreach (var x in Motion(new Vector2(70, 20))) yield return x;
        VwCheck(!s.FreeCameraActive && v.ViewerCameraPose.IsEqualApprox(normal), "direct: an unfocused window ignores A and the mouse");
        s.FocusOverrideForTest = null; yield return null;
        foreach (var x in Motion(new Vector2(70, 20))) yield return x;
        VwCheck(!s.FreeCameraActive && s.DirectMouseQuiet, "direct: focus coming back starts a quiet time, so its first motion is not taken");
        foreach (var x in VrUntil(() => !s.DirectMouseQuiet, 120, "quiet time over (focus back)")) yield return x;

        // W enters at once from the camera in view, keeps paused time, flies, and changes no edit
        v.ViewerSeek(clock); for (int i = 0; i < 4; i++) yield return null;
        normal = v.ViewerCameraPose; fov = v.ViewerCameraFov;
        int entries = s.DirectFreeEntries;
        _vwDrv.SetControl(true);
        Input.ParseInputEvent(VwKey(Key.W, true, 0)); yield return null; yield return null;
        bool entered = s.FreeCameraActive;
        for (int i = 0; i < 28; i++) yield return null;
        Input.ParseInputEvent(VwKey(Key.W, false, 0)); yield return null; yield return null;
        _vwDrv.SetControl(false);
        VwCheck(entered && s.DirectFreeEntries == entries + 1 && s.FreeEntryPose.IsEqualApprox(normal) && s.FreeEntryFov == fov,
            $"direct: W enters the free camera from the top-down pose and lens (entered {entered}, fov {s.FreeEntryFov} vs {fov})");
        if (!entered) yield break;
        VwCheck(v.ViewerFreeRig.Position.DistanceTo(normal.Origin) > .5f && Mathf.Abs(v.ViewerFreeRig.Position.Y - normal.Origin.Y) < .001f,
            $"direct: the initiating W press already flies the camera forward (moved {v.ViewerFreeRig.Position.DistanceTo(normal.Origin):0.00} m)");
        VwCheck(v.ViewerPaused && v.ViewerClock == clock && v.ViewerSpeed == speed && Untouched() && !s.EditOpen, "direct: entering and flying keep paused time and speed and change no edit");
        VwCheck(Input.MouseMode == Input.MouseModeEnum.Captured, "direct: the mouse is captured for looking in the focused window");
        foreach (var x in VwShotDirect("direct-free-wasd")) yield return x;
        // seek and speed inside
        foreach (var x in VrTap(Key.Right)) yield return x;
        VwCheck(v.ViewerClock > clock && s.FreeCameraActive && v.ViewerPaused, "direct: RIGHT still seeks inside the direct free camera");
        foreach (var x in VrTap(Key.Up)) yield return x;
        VwCheck(v.ViewerSpeed > speed && s.FreeCameraActive, "direct: UP still changes speed inside the direct free camera");
        foreach (var x in VrTap(Key.Down)) yield return x;
        // R returns to the tracking camera with its own lens; the pointer's release/warp does not fly back in
        float here = v.ViewerClock;
        foreach (var x in VrTap(Key.R)) yield return x;
        VwCheck(!s.FreeCameraActive && v.ViewerCamera == ViewerCameraMode.TopDown && v.ViewerCameraFov == fov && v.ViewerPaused && v.ViewerClock == here && Input.MouseMode == mouse0 && Untouched(),
            "direct: R returns to the top-down follow camera, its lens and the free cursor, still paused at the same time, nothing edited");
        foreach (var x in Motion(new Vector2(90, 30))) yield return x;
        VwCheck(!s.FreeCameraActive, "direct: a motion right after R (the release's warp) does not re-enter");
        _vwDrv.PassForeignMotion = true; int passed = _vwDrv.ForeignMotionPassed;
        var win = GetWindow(); Input.WarpMouse((Vector2)(win.Size / 2) + new Vector2(40, 10)); yield return null; yield return null; yield return null;
        _vwDrv.PassForeignMotion = false;
        VwCheck(!s.FreeCameraActive, $"direct: a real pointer warp right after R does not re-enter ({_vwDrv.ForeignMotionPassed - passed} real motion events reached the viewer)");
        foreach (var x in VrUntil(() => !s.DirectMouseQuiet, 120, "quiet time after R")) yield return x;
        for (int i = 0; i < 4; i++) yield return null;
        VwCheck(!s.FreeCameraActive, "direct: with no further motion the follow camera stays");
        foreach (var x in VrTap(Key.Left)) yield return x;
        VwCheck(v.ViewerClock < here && !s.FreeCameraActive, "direct: plain-viewer seek works after R");

        // an ordinary mouse motion enters at once and its own motion turns the camera
        v.ViewerSeek(clock); for (int i = 0; i < 4; i++) yield return null;
        normal = v.ViewerCameraPose;
        foreach (var x in Motion(new Vector2(80, 25))) yield return x;
        VwCheck(s.FreeCameraActive && s.DirectFreeEntries == entries + 2 && s.FreeEntryPose.IsEqualApprox(normal) && s.FreeEntryFov == fov,
            "direct: an ordinary mouse motion enters the free camera from the pose and lens in view, no button held");
        if (!s.FreeCameraActive) yield break;
        VwCheck(v.ViewerCameraPose.Origin.IsEqualApprox(normal.Origin) && !v.ViewerCameraPose.Basis.IsEqualApprox(normal.Basis), "direct: the entering motion already turns the camera in place");
        var turned = v.ViewerCameraPose.Basis;
        foreach (var x in Motion(new Vector2(-40, 0))) yield return x;
        VwCheck(!v.ViewerCameraPose.Basis.IsEqualApprox(turned) && Input.MouseMode == Input.MouseModeEnum.Captured, "direct: further motion keeps turning with the captured mouse");
        foreach (var x in VrTap(Key.Escape)) yield return x;
        VwCheck(!s.FreeCameraActive && s.State == ReplayViewerState.Playing && v.ViewerCamera == ViewerCameraMode.TopDown && Untouched(), "direct: ESC also returns to follow and keeps the viewer open");
        foreach (var x in VrUntil(() => !s.DirectMouseQuiet, 120, "quiet time after ESC")) yield return x;

        // playing is kept; the camera that was in view (AUTO here) is the one R gives back
        foreach (var x in VrTap(Key.Space)) yield return x;
        VwCheck(!v.ViewerPaused, "direct: playback runs");
        foreach (var x in VrTap(Key.D, 10)) yield return x;
        float c1 = v.ViewerClock; for (int i = 0; i < 10; i++) yield return null;
        VwCheck(s.FreeCameraActive && !v.ViewerPaused && v.ViewerClock > c1, "direct: D enters during playback and playback keeps running");
        foreach (var x in VrTap(Key.R)) yield return x;
        VwCheck(!s.FreeCameraActive && !v.ViewerPaused && v.ViewerCamera == ViewerCameraMode.TopDown, "direct: R during playback returns to follow without pausing");
        foreach (var x in VrTap(Key.C)) yield return x;
        var cam = v.ViewerCamera;
        for (int i = 0; i < 20; i++) yield return null;
        foreach (var x in VrTap(Key.S, 6)) yield return x;
        VwCheck(s.FreeCameraActive && s.FreeReturnCamera == cam, $"direct: S enters from the {cam} camera");
        foreach (var x in VrTap(Key.R)) yield return x;
        for (int i = 0; i < 4; i++) yield return null;
        VwCheck(!s.FreeCameraActive && v.ViewerCamera == cam && !v.ViewerPaused, $"direct: R restores the {cam} camera that was in view, not a different follow mode (now {v.ViewerCamera})");
        foreach (var x in VrTap(Key.Space)) yield return x;
        for (int i = 0; i < 4 && v.ViewerCamera != ViewerCameraMode.TopDown; i++) foreach (var x in VrTap(Key.C)) yield return x;      // (the lap's chosen camera back: C is a saved viewing choice, not part of this check)
        VwCheck(v.ViewerPaused && v.ViewerCamera == ViewerCameraMode.TopDown && Untouched(), "direct: pause again; the whole direct round left no camera key, dirty edit or saved sidecar");
        foreach (var x in VrUntil(() => !s.DirectMouseQuiet, 120, "quiet time at end")) yield return x;

        // a saved free-camera key in view: W takes it over from its own pose, R gives the keyed shot back (not a follow mode); the key is then removed again
        v.ViewerSeek(clock); for (int i = 0; i < 4; i++) yield return null;
        foreach (var x in VrTap(Key.W, 10)) yield return x;
        foreach (var x in VrTap(Key.K)) yield return x;
        var keyed = v.ViewerCaptureFreeCamera();
        foreach (var x in VrTap(Key.R)) yield return x;
        VwCheck(!s.FreeCameraActive && !v.ViewerFreeCameraActive && v.ViewerCamera == ViewerCameraMode.TopDown, "direct: R after adding a key returns to the top-down camera that was in view");
        v.ViewerSeek(v.ViewerClock); for (int i = 0; i < 4; i++) yield return null;
        var keyedPose = v.ViewerCameraPose;
        VwCheck(v.ViewerFreeCameraActive && v.ViewerEdits.FreeKeys.Count == 1 && keyedPose.Origin.DistanceTo(new Vector3(keyed.X, keyed.Y, keyed.Z)) < .01f,
            "direct: a seek shows the saved free key at the clock");
        foreach (var x in VrUntil(() => !s.DirectMouseQuiet, 120, "quiet time before keyed entry")) yield return x;
        foreach (var x in Motion(new Vector2(60, 0))) yield return x;
        VwCheck(s.FreeCameraActive && s.FreeReturnKeyed && s.FreeEntryPose.Origin.IsEqualApprox(keyedPose.Origin), "direct: the mouse takes over the keyed shot from its own place");
        foreach (var x in VrTap(Key.D, 10)) yield return x;
        foreach (var x in VrTap(Key.R)) yield return x;
        for (int i = 0; i < 4; i++) yield return null;
        VwCheck(!s.FreeCameraActive && v.ViewerFreeCameraActive && v.ViewerCameraPose.Origin.DistanceTo(keyedPose.Origin) < .01f && v.ViewerEdits.FreeKeys.Count == 1,
            "direct: R after flying away returns to the keyed shot and flying did not change the key");
        foreach (var x in VrTap(Key.Delete)) yield return x;
        for (int i = 0; i < 4; i++) yield return null;
        VwCheck(v.ViewerEdits.Keys.Count == 0 && !s.Dirty && !v.ViewerFreeCameraActive && v.ViewerCamera == ViewerCameraMode.TopDown, "direct: DEL removes the test key and the lap's top-down camera is back, nothing unsaved");
        foreach (var x in VrUntil(() => !s.DirectMouseQuiet, 120, "quiet time after keyed round")) yield return x;
    }

    IEnumerable VwShotDirect(string name) { yield return null; VwGrab(GetViewport(), name); VwLog("shot", ("name", name)); yield return null; }
}
}
