using System;
using System.Collections;
using System.IO;
using System.Linq;
using CCCPRacing.Camera;
using CCCPRacing.Replay;
using Godot;

namespace CCCPRacing.Race;
public partial class RaceScene
{
    static bool FreeShotMatches(RaceScene v, LapFreeCamera shot)
    {
        float start = v.ViewerEdits.FreeKeys.Where(k => k.Value == shot).Select(k => k.Key).FirstOrDefault();
        var expected = ReplayCameraMotion.Evaluate(shot, start, v.ViewerClock, v.TitleGhost.Sample);
        return v.ViewerFreeCameraActive && v.ViewerCameraPose.Origin.DistanceTo(expected.Pose.Origin) < .001f
            && v.ViewerCameraPose.Basis.IsEqualApprox(expected.Pose.Basis) && Mathf.Abs(v.ViewerCameraFov - expected.Fov) < .001f;
    }
    IEnumerable VwFreeCameraKeys()
    {
        bool motionShots = VlArg("--camera-motion") == "1";
        foreach (var x in VwWaitReady()) yield return x;
        var originals = LapArchive.For(Path.Combine(VlArg("--vw-fx") ?? "/tmp/cccp-lc-fx", "ilyich"));
        var originalInfo = originals.List().First(i => i.Openable && i.Retained);
        string sourceHash = originals.Open(originalInfo.Id).Sha256();
        string root = Path.Combine(VlArg("--vw-media") ?? "media/free-camera-phase2", "fixture");
        string dir = Path.Combine(root, originalInfo.Id); Directory.CreateDirectory(dir);
        foreach (var file in new[] { "ghost.bin", "tape.bin", "meta.json" })
            if (File.Exists(Path.Combine(originalInfo.Dir, file))) File.Copy(Path.Combine(originalInfo.Dir, file), Path.Combine(dir, file), true);
        var archive = new LapArchive(root); var info = archive.List().Single(); var source = archive.Open(info.Id);
        var s = OpenReplayViewer(source, out string err); VwCheck(s != null, "free-key viewer opens: " + err);
        if (s == null) yield break;
        s.SetEditContext(archive, info); s.SetTitle("FREE CAMERA KEYS TEST");
        foreach (var x in VrUntil(() => s.State == ReplayViewerState.Playing, 7200, "ready")) yield return x;
        var v = s.Scene; v.ViewerSetPaused(true); v.ViewerSeek(5);
        foreach (var x in VrTap(Key.K)) yield return x;
        VwCheck(v.ViewerEdits.Keys.Count == 1 && v.ViewerEdits.FreeKeys.Count == 0, "preset camera points coexist with free-camera points");
        v.ViewerSeek(10);
        for (int i = 0; i < 4; i++) yield return null;
        foreach (var x in VrTap(Key.E)) yield return x;
        int freeRow = Array.FindIndex(s.EditRowTexts, r => r.StartsWith("FREE CAMERA"));
        foreach (var x in ToExportRow(s, freeRow)) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        foreach (var x in VrTap(Key.W, 15)) yield return x;
        foreach (var x in VrTap(Key.E, 10)) yield return x;
        foreach (var x in VrTap(Key.F)) yield return x;
        foreach (var x in VrTap(Key.G)) yield return x;
        if (motionShots) foreach (var x in VwConfigureMotion(s, v)) yield return x;
        var shotA = v.ViewerCaptureFreeCamera();
        if (motionShots) shotA = shotA with { Motion = s.CameraMotionDraft };
        foreach (var x in VrTap(Key.K)) yield return x;
        VwCheck(s.Dirty && v.ViewerEdits.FreeKeys.Count == 1 && v.ViewerEdits.FreeKeys[10f] == shotA, "K captures first pose with independent follow/look flags and marks edits unsaved");
        foreach (var x in VrTap(Key.Tab)) yield return x;
        VwCheck(s.FreeCameraActive && v.ViewerClock == 10f, "Tab with one key stays in the free camera at that key");
        foreach (var x in VrTap(Key.Escape)) yield return x;
        v.ViewerSeek(v.ViewerClock); for (int i = 0; i < 4; i++) yield return null;
        VwCheck(!s.FreeCameraActive && FreeShotMatches(v, shotA) && s.State == ReplayViewerState.Playing, "leaving the free camera returns to playback controls using the captured camera without closing");
        v.ViewerSeek(15f); for (int i = 0; i < 4; i++) yield return null;
        VwCheck(FreeShotMatches(v, shotA), "follow+look key evaluates at sought car pose");
        var pose15 = v.ViewerCameraPose;
        v.ViewerSeek(20f);
        foreach (var x in VrTap(Key.E)) yield return x;
        foreach (var x in ToExportRow(s, freeRow)) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        foreach (var x in VrTap(Key.F)) yield return x;
        foreach (var x in VrTap(Key.G)) yield return x;
        foreach (var x in VrTap(Key.D, 15)) yield return x;
        if (motionShots)
        {
            foreach (var x in VrTap(Key.B)) yield return x;
            for (int i = 0; i < 7; i++) foreach (var x in VrTap(Key.Down)) yield return x;
            foreach (var x in VrTap(Key.Enter)) yield return x;
            foreach (var x in VrTap(Key.Escape)) yield return x;
            VwCheck(s.CameraMotionDraft == null, "CLEAR MOTION restores legacy fixed-shot controls");
        }
        var shotB = v.ViewerCaptureFreeCamera();
        foreach (var x in VrTap(Key.K)) yield return x;
        foreach (var x in VrTap(Key.Escape)) yield return x;
        VwCheck(!shotB.Follow && !shotB.Look && v.ViewerEdits.FreeKeys.Count == 2, "second fixed camera key keeps independent flags");
        v.ViewerSeek(9.5f); for (int i = 0; i < 3; i++) yield return null;
        VwCheck(!v.ViewerFreeCameraActive && v.ViewerCamera == ViewerCameraMode.TopDown, "before the first point the normal default camera is restored");
        v.ViewerSeek(19.99f); for (int i = 0; i < 3; i++) yield return null;
        VwCheck(FreeShotMatches(v, shotA), "first shot holds until the exact cut");
        v.ViewerSeek(20f); for (int i = 0; i < 3; i++) yield return null;
        VwCheck(FreeShotMatches(v, shotB), "second shot applies as a hard cut with no interpolation");
        v.ViewerSeek(25f); for (int i = 0; i < 3; i++) yield return null;
        VwCheck(FreeShotMatches(v, shotB), "fixed camera stays at captured coordinates later in the lap");
        v.ViewerSeek(15f); for (int i = 0; i < 3; i++) yield return null;
        VwCheck(v.ViewerCameraPose.IsEqualApprox(pose15), "seeking backwards returns to the identical follow+look picture");
        // Tab / Shift+Tab step through the keys (5 preset, 10 shotA, 20 shotB) in the replay, round the ends
        v.ViewerSeek(12f); for (int i = 0; i < 3; i++) yield return null;
        foreach (var x in VrTap(Key.Tab)) yield return x;
        VwCheck(Mathf.Abs(v.ViewerClock - 20f) < .01f && FreeShotMatches(v, shotB), "Tab goes to the next key and shows its camera");
        foreach (var x in VrTap(Key.Tab)) yield return x;
        VwCheck(Mathf.Abs(v.ViewerClock - 5f) < .01f && !v.ViewerFreeCameraActive, "Tab wraps from the last key to the first (a preset key)");
        foreach (var x in VrTapShift(Key.Tab)) yield return x;
        VwCheck(Mathf.Abs(v.ViewerClock - 20f) < .01f && FreeShotMatches(v, shotB), "Shift+Tab wraps backwards from the first key to the last");
        foreach (var x in VrTapShift(Key.Tab)) yield return x;
        VwCheck(Mathf.Abs(v.ViewerClock - 10f) < .01f && FreeShotMatches(v, shotA), "Shift+Tab goes to the previous key");
        // in the free camera a free key puts the rig there and flying goes on; a preset key leaves the free camera
        foreach (var x in VrTap(Key.E)) yield return x;
        foreach (var x in ToExportRow(s, freeRow)) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        foreach (var x in VrTap(Key.W, 15)) yield return x;
        foreach (var x in VrTap(Key.Tab)) yield return x;
        var inHand = v.ViewerCaptureFreeCamera();
        VwCheck(s.FreeCameraActive && Mathf.Abs(v.ViewerClock - 20f) < .01f && new Vector3(inHand.X, inHand.Y, inHand.Z).DistanceTo(new Vector3(shotB.X, shotB.Y, shotB.Z)) < .01f && Mathf.Abs(inHand.Yaw - shotB.Yaw) < .001f,
            "Tab in the free camera puts the camera at the free key and keeps control");
        foreach (var x in VrTap(Key.Tab)) yield return x;
        VwCheck(!s.FreeCameraActive && Mathf.Abs(v.ViewerClock - 5f) < .01f, "Tab onto a preset key leaves the free camera");
        v.ViewerSeek(10f); foreach (var x in VrTap(Key.I)) yield return x;
        v.ViewerSeek(22f); foreach (var x in VrTap(Key.O)) yield return x;
        foreach (var x in VrTap(Key.L)) yield return x;
        int samples = 0, wrong = 0;
        void Sample() { samples++; if (!FreeShotMatches(v, v.ViewerClock < 20 - .0001f ? shotA : shotB)) wrong++; }
        RenderingServer.FramePostDraw += Sample;
        v.ViewerSeek(21.98f); foreach (var x in VrTap(Key.Space)) yield return x;
        for (int i = 0; i < 30; i++) yield return null;
        foreach (var x in VrTap(Key.Space)) yield return x;
        RenderingServer.FramePostDraw -= Sample;
        VwCheck(v.ViewerWraps > 0 && v.ViewerClock >= 10 && v.ViewerClock < 20 && samples > 10 && wrong == 0,
            $"loop and continuous playback use exact selected key on every sampled frame (samples {samples}, wrong {wrong})");
        foreach (var x in VrTap(Key.S)) yield return x;
        foreach (var x in VrUntil(() => !s.SaveBusy, 1800, "saved")) yield return x;
        VwCheck(!s.Dirty && archive.LoadEdits(info.Id).Result.Edits.CameraKeys.Count == 3, "SAVE persists both camera poses and clears dirty state");
        foreach (var x in VrShot("saved-free-keys")) yield return x;
        foreach (var x in VrTap(Key.E)) yield return x;
        int exportRow = Array.FindIndex(s.EditRowTexts, r => r.StartsWith("EXPORT"));
        foreach (var x in ToExportRow(s, exportRow)) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        VwCheck(s.ExportOpen && s.OverlayView.ExportRows.Length > 0, "MP4 export accepts the saved free-camera working context");
        foreach (var x in VrTap(Key.Escape)) yield return x;
        s.Close(); foreach (var x in VrUntil(() => s.State == ReplayViewerState.Closed, 1800, "closed")) yield return x;
        var fresh = new LapArchive(root); var again = fresh.Open(info.Id); var againInfo = fresh.List().Single();
        var s2 = OpenReplayViewer(again, out err); VwCheck(s2 != null, "fresh viewer opens persisted points: " + err);
        if (s2 == null) yield break;
        s2.SetEditContext(fresh, againInfo);
        foreach (var x in VrUntil(() => s2.State == ReplayViewerState.Playing, 7200, "fresh ready")) yield return x;
        var v2 = s2.Scene; v2.ViewerSetPaused(true);
        VwCheck(v2.ViewerClock >= 10 && v2.ViewerClock < 11 && FreeShotMatches(v2, shotA) && !s2.FreeCameraActive && !s2.Dirty,
            "saved trim and free camera are applied at reopen before editing controls take over");
        v2.ViewerSeek(21); for (int i = 0; i < 3; i++) yield return null;
        VwCheck(FreeShotMatches(v2, shotB), "fresh viewer restores the second fixed shot");
        foreach (var x in VrTap(Key.Delete)) yield return x;
        VwCheck(v2.ViewerEdits.Keys.Count == 3 && !s2.Dirty, "Del away from any key removes nothing");
        foreach (var x in VrTapShift(Key.Tab)) yield return x;
        VwCheck(Mathf.Abs(v2.ViewerClock - 20f) < .01f, "Shift+Tab from the reopened viewer finds the key before the clock");
        foreach (var x in VrTap(Key.Delete)) yield return x;
        for (int i = 0; i < 3; i++) yield return null;
        VwCheck(v2.ViewerEdits.Keys.Count == 2 && !v2.ViewerEdits.FreeKeys.ContainsKey(20f) && s2.Dirty && FreeShotMatches(v2, shotA),
            "Del removes just that key, marks edits unsaved, and the key before it takes over");
        foreach (var x in VrTap(Key.T)) yield return x; // disable trim, so this is a full-lap wrap with a gap before the first key
        bool sawStart = false; int wrongStart = 0;
        void SampleFull() { if (v2.ViewerClock < 2) { sawStart = true; if (v2.ViewerFreeCameraActive || v2.ViewerCamera != ViewerCameraMode.TopDown) wrongStart++; } }
        RenderingServer.FramePostDraw += SampleFull;
        v2.ViewerSeek(v2.ViewerDuration - .02f);
        foreach (var x in VrTap(Key.Space)) yield return x;
        for (int i = 0; i < 30; i++) yield return null;
        foreach (var x in VrTap(Key.Space)) yield return x;
        RenderingServer.FramePostDraw -= SampleFull;
        VwCheck(sawStart && wrongStart == 0, $"full-lap wrap restores default before the first key, without a stale free-camera frame (wrong {wrongStart})");
        v2.ViewerSeek(21f); for (int i = 0; i < 3; i++) yield return null;
        VwCheck(v2.ViewerFreeCameraActive, "saved shot active before clearing its points");
        foreach (var x in VrTap(Key.E)) yield return x;
        int clearRow = Array.FindIndex(s2.EditRowTexts, r => r.StartsWith("CLEAR CAMERA KEYS"));
        foreach (var x in ToExportRow(s2, clearRow)) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        VwCheck(!v2.ViewerFreeCameraActive && v2.ViewerEdits.Keys.Count == 0 && s2.Dirty, "clearing keys removes saved free view and marks edits unsaved");
        int editFree = Array.FindIndex(s2.EditRowTexts, r => r.StartsWith("FREE CAMERA"));
        foreach (var x in ToExportRow(s2, editFree)) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        foreach (var x in VrTap(Key.K)) yield return x;
        foreach (var x in VrTap(Key.Escape)) yield return x;
        v2.ViewerSeek(v2.ViewerClock); for (int i = 0; i < 3; i++) yield return null;
        VwCheck(v2.ViewerFreeCameraActive && s2.Dirty, "another working free key is active before reset");
        foreach (var x in VrTap(Key.E)) yield return x;
        int resetRow = Array.FindIndex(s2.EditRowTexts, r => r.StartsWith("RESET EDITS"));
        foreach (var x in ToExportRow(s2, resetRow)) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        foreach (var x in VrUntil(() => !s2.SaveBusy, 1800, "reset saved")) yield return x;
        VwCheck(!v2.ViewerFreeCameraActive && v2.ViewerEdits.Keys.Count == 0 && !s2.Dirty && !v2.ViewerLoop && v2.ViewerSpeed == 1f && v2.ViewerCamera == ViewerCameraMode.TopDown,
            "RESET EDITS clears active saved free camera immediately while paused");
        s2.Close(); foreach (var x in VrUntil(() => s2.State == ReplayViewerState.Closed, 1800, "final closed")) yield return x;
        VwCheck(fresh.Open(info.Id).Sha256() == sourceHash && originals.Open(originalInfo.Id).Sha256() == sourceHash, "original and cloned driving/world tape bytes remain unchanged");
    }
}
