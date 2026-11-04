using System;
using System.Collections;
using System.IO;
using System.Linq;
using CCCPRacing.Replay;
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
        foreach (var x in VrTap(Key.E)) yield return x;
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
        VwCheck(v.ViewerClock == here, "free-camera seek does not enable unrelated frame-step shortcuts");
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
        foreach (var x in VrTap(Key.E)) yield return x;
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
        s.Close(); yield return null;
        VwCheck(Input.MouseMode == oldMouse, "forced viewer close releases mouse capture");
        _vwDrv.SetControl(false);
        foreach (var x in VrUntil(() => s.State == ReplayViewerState.Closed, 1800, "closed")) yield return x;
        VwCheck(fx.Open(originalInfo.Id).Sha256() == hash && archive.Open(info.Id).Sha256() == hash, "original driving and world tapes unchanged");
    }
}
