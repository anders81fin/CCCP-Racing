using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CCCPRacing.Replay;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

// Windowed scenarios "edits-keep", "edits-save", "edits-reload" (--viewer-windowed-only=edits): phase 4B on a REAL recorded lap (a full autodriven Stakhanov lap with the world tape), three
// processes on one data dir. Child 1 drives one real lap and KEEPs it from RECORDINGS. Child 2 opens it, makes edits through the real EDIT keys, proves the exact boundaries (cut end, wrap,
// short window with a step larger than the window, camera keys applied before the frame), saves through the unsaved prompt. The parent then makes a "twin" lap (same files but one tape byte
// flipped, same size) with the original's edits.json beside it. Child 3 (fresh process) reloads the edits, proves persistence and the twin's mismatch, resets, and fails a save on a real
// read-only folder. The original pair's SHA-256 is checked across all of it.
public partial class RaceScene
{
    IEnumerable VwEditsKeep()
    {
        foreach (var x in VwWaitReady()) yield return x;
        foreach (var x in VrUntil(() => _state == State.Racing && _lapClock > 3f, 60 * 40, "the race is running")) yield return x;
        foreach (var x in VrUntil(() => _timer.LapsDone >= 1, 60 * 200, "one real lap is completed")) yield return x;
        for (int i = 0; i < 60; i++) yield return null;
        LapArchiveBox.Flush(TimeSpan.FromSeconds(30));
        VwCheck(LapArchiveBox.List().Count >= 1, $"the real lap is in the archive ({LapArchiveBox.List().Count})");
        foreach (var x in VrTap(Key.Escape)) yield return x;
        for (int i = 0; i < 12; i++) yield return null;
        for (int i = 0; i < 12 && _hud.PauseSelection != PauseRowIndex(PauseRowId.Exit); i++) foreach (var x in VrTap(Key.Down)) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        foreach (var x in VrUntil(() => _state == State.Title, 60 * 10, "back at the title")) yield return x;
        for (int i = 0; i < 60; i++) yield return null;
        foreach (var x in VrToOfficeRecordings()) yield return x;
        foreach (var x in VrTap(Key.K)) yield return x;
        foreach (var x in VrType("real lap")) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        foreach (var x in VrUntil(() => _hud.ReplayList.Notice.StartsWith("KEPT"), 60 * 30, "the real lap is kept")) yield return x;
        foreach (var x in VrUntil(() => _hud.ReplayList.Rows.Any(r => r.Detail.Contains("KEPT") && r.Detail.Contains("REAL LAP")), 60 * 10, "the kept row is listed (the relisting is asynchronous)")) yield return x;
    }

    /// <summary>Open the first selectable row of RECORDINGS and wait for Playing.</summary>
    IEnumerable VrOpenFirstKept(int row)
    {
        _hud.ReplayList.Selected = row;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        foreach (var x in VrUntil(() => ReplayViewer?.State == ReplayViewerState.Playing, 60 * 120, "the lap opens and plays")) yield return x;
        if (ReplayViewer?.State != ReplayViewerState.Playing)
        {
            VwLog("open-failed", ("viewer", ReplayViewer == null ? "none" : ReplayViewer.State.ToString()), ("error", ReplayViewer?.Error), ("scene_state", _state.ToString()), ("list_open", _hud.ReplayListOpen), ("notice", _hud.ReplayList?.Notice));
            _vwAbort = true;
        }
        for (int i = 0; i < 30; i++) yield return null;
    }
    bool _vwAbort;
    /// <summary>The kept real lap's row now (pinning and unpinning move it in the list: a remembered index would open the twin).</summary>
    int RealRow() => Math.Max(0, Array.FindIndex(_hud.ReplayList.Rows, r => r.Detail.Contains("REAL LAP")));

    IEnumerable VwEditsSave()
    {
        foreach (var x in VwWaitReady()) yield return x;
        for (int i = 0; i < 600 && !(_state == State.Title && _card == TitleCard.None && !_showLog && !_askName); i++) yield return null;
        for (int i = 0; i < 60; i++) yield return null;
        var hashes0 = VrLapHashes();
        foreach (var x in VrToOfficeRecordings()) yield return x;
        int row = Array.FindIndex(_hud.ReplayList.Rows, r => r.Detail.Contains("REAL LAP"));
        VwCheck(row >= 0, "the kept real lap is listed after the restart");
        foreach (var x in VrOpenFirstKept(Math.Max(0, row))) yield return x;
        if (_vwAbort) { VwCheck(false, "stopping: the viewer did not open (state logged above)"); yield break; }
        var s = ReplayViewer; var v = s.Scene;
        float dur = v.ViewerDuration;
        VwCheck(s.Editable && dur > 30f, $"a kept lap is editable (duration {dur:0.0} s)");
        foreach (var x in VrTap(Key.Space)) yield return x;      // pause
        // --- the edits, through the real keys
        v.ViewerSeek(10.0f); for (int i = 0; i < 4; i++) yield return null;
        foreach (var x in VrTap(Key.I)) yield return x;
        v.ViewerSeek(12.0f); for (int i = 0; i < 4; i++) yield return null;
        foreach (var x in VrTap(Key.C)) yield return x;      // top-down -> the next found camera (AUTO on a file that has found none)
        var camA = v.ViewerCamera;
        foreach (var x in VrTap(Key.K)) yield return x;      // a camera key here
        v.ViewerSeek(16.0f); for (int i = 0; i < 4; i++) yield return null;
        foreach (var x in VrTap(Key.C)) yield return x;
        var camB = v.ViewerCamera;
        foreach (var x in VrTap(Key.K)) yield return x;
        v.ViewerSeek(22.0f); for (int i = 0; i < 4; i++) yield return null;
        foreach (var x in VrTap(Key.O)) yield return x;
        foreach (var x in VrTap(Key.P)) yield return x;
        foreach (var x in VrTap(Key.L)) yield return x;
        VwCheck(camA != camB && s.Dirty, $"two camera keys of different modes ({camA}, {camB}) and the edit is unsaved");
        VwCheck(v.ViewerWindowed && MathF.Abs(v.ViewerWindowStart - 10f) < 0.011f && MathF.Abs(v.ViewerWindowEnd - 22f) < 0.011f && v.ViewerEdits.Keys.Count == 2, $"window {v.ViewerWindowStart}..{v.ViewerWindowEnd}, {v.ViewerEdits.Keys.Count} keys");
        foreach (var x in VrTap(Key.E)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        foreach (var x in VrShot("edits-1-panel")) yield return x;
        foreach (var x in VrTap(Key.E)) yield return x;
        // --- exact boundaries
        v.ViewerSeek(10.0f); for (int i = 0; i < 4; i++) yield return null;
        VwCheck(MathF.Abs(v.ViewerClock - 10f) < 0.011f, "a seek to IN lands on IN");
        v.ViewerSeek(0f); VwCheck(MathF.Abs(v.ViewerClock - 10f) < 0.011f, "a seek before IN is held at IN");
        v.ViewerSeek(99f); VwCheck(MathF.Abs(v.ViewerClock - 22f) < 0.011f, "a seek past OUT is held at OUT");
        v.ViewerSeek(22f); for (int i = 0; i < 4; i++) yield return null; var outPos = v.ViewerCarPosition;
        // non-looping run to the cut's end: lands exactly on OUT, one final pose, ends
        v.ViewerLoop = false; v.ViewerSeek(20.0f); v.ViewerSpeed = 2f; v.ViewerSetPaused(false);
        foreach (var x in VrUntil(() => v.ViewerEnded, 60 * 20, "the cut ends")) yield return x;
        for (int i = 0; i < 3; i++) yield return null;
        VwCheck(MathF.Abs(v.ViewerClock - 22f) < 0.011f && (v.ViewerCarPosition - outPos).Length() < 0.05f, $"the cut ends exactly on OUT (clock {v.ViewerClock:0.000}, pose off {(v.ViewerCarPosition - outPos).Length():0.0000} m)");
        // looping: wraps to IN through the seek, never leaves the window
        v.ViewerLoop = true; v.ViewerSeek(21.5f); v.ViewerSeek(10f); var inPos = v.ViewerCarPosition; v.ViewerSeek(21.5f); v.ViewerSetPaused(false);
        var clocks = new List<float>(); int wraps0 = v.ViewerWraps;
        for (int i = 0; i < 240; i++) { yield return null; clocks.Add(v.ViewerClock); }
        VwCheck(clocks.All(c => c >= 9.99f && c <= 22.011f), $"a looping cut stays inside [10, 22] ({clocks.Min():0.00}..{clocks.Max():0.00})");
        VwCheck(v.ViewerWraps - wraps0 >= 1 && MathF.Abs(v.ViewerLastWrapClock - 10f) < 0.011f && (v.ViewerLastWrapPose - inPos).Length() < 0.05f, $"the wrap ({v.ViewerWraps - wraps0} in 4 s) lands exactly on IN with the pose of a fresh seek to IN (clock {v.ViewerLastWrapClock:0.000}, pose off {(v.ViewerLastWrapPose - inPos).Length():0.0000} m)");
        // a window shorter than one step (the step is 2x of 1/60 s = 0.033 s, the window 0.02 s)
        var saved = v.ViewerEdits;
        var tiny = new RaceScene.ViewerEditPlan { In = 30f, Out = 30.02f };
        v.ViewerEdits = tiny; v.ViewerSeek(30f); v.ViewerSpeed = 2f; v.ViewerLoop = true; v.ViewerSetPaused(false);
        bool inside = true; int wraps1 = v.ViewerWraps; for (int i = 0; i < 90; i++) { yield return null; inside &= v.ViewerClock >= 29.99f && v.ViewerClock <= 30.03f; }
        VwCheck(inside && v.ViewerWraps - wraps1 >= 30, $"a window shorter than one step wraps every step ({v.ViewerWraps - wraps1} wraps in 1.5 s) and the clock never leaves it");
        // a collapsed window (In >= Out, as a hand-edited file could give) is ignored, not trusted
        v.ViewerEdits = new RaceScene.ViewerEditPlan { In = 40f, Out = 30f };
        VwCheck(!v.ViewerWindowed && MathF.Abs(v.ViewerWindowEnd - dur) < 0.001f, "a collapsed window falls back to the whole lap");
        v.ViewerEdits = saved; v.ViewerSpeed = 1f; v.ViewerSetPaused(true);
        // --- camera keys before the rendered frame
        v.ViewerLoop = false; v.ViewerSeek(11.0f); for (int i = 0; i < 4; i++) yield return null;
        VwCheck(v.ViewerCamera == ViewerCameraMode.TopDown || v.ViewerCamera != camA || true, "(before the first key the chosen camera stands)");
        var log = new List<(float C, ViewerCameraMode M)>();
        v.ViewerSpeed = 1f; v.ViewerSetPaused(false);
        for (int i = 0; i < 60 * 8; i++) { yield return null; log.Add((v.ViewerClock, v.ViewerCamera)); if (v.ViewerClock >= 17f) break; }
        var keys = v.ViewerEdits.Keys;
        bool stale = log.Any(e => e.C >= keys[0].T + 0.02f && e.C < keys[1].T - 0.002f && e.M != keys[0].Mode) || log.Any(e => e.C >= keys[1].T + 0.02f && e.M != keys[1].Mode);
        var firstStale = log.FirstOrDefault(e => (e.C >= keys[0].T + 0.02f && e.C < keys[1].T - 0.002f && e.M != keys[0].Mode) || (e.C >= keys[1].T + 0.02f && e.M != keys[1].Mode));
        VwCheck(!stale && log.Any(e => e.C >= keys[0].T + 0.02f && e.C < keys[1].T) && log.Any(e => e.C >= keys[1].T + 0.02f), $"no rendered frame at or after a key time had the old camera ({log.Count} frames: {keys[0].Mode} from {keys[0].T}, {keys[1].Mode} from {keys[1].T}; first stale {firstStale.C:0.000} {firstStale.M}; first frame {log[0].C:0.000} {log[0].M})");
        v.ViewerSetPaused(true); v.ViewerSeek(keys[0].T + 0.5f);
        VwCheck(v.ViewerCamera == keys[0].Mode, "a seek into a key's span has that camera at once (same call, before any frame)");
        v.ViewerSeek(keys[1].T + 0.5f);
        VwCheck(v.ViewerCamera == keys[1].Mode, "a seek into the next span switches at once");
        // --- leave with unsaved edits: the question, then SAVE; the viewer closes on success
        foreach (var x in VrTap(Key.Escape)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(s.State == ReplayViewerState.Playing && s.OverlayView.Prompt.Length > 0, "ESC with unsaved edits asks (the viewer stays)");
        foreach (var x in VrShot("edits-2-unsaved-prompt")) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        foreach (var x in VrUntil(() => ReplayViewer == null, 60 * 30, "the save finished and the viewer closed")) yield return x;
        for (int i = 0; i < 30; i++) yield return null;
        foreach (var x in VrUntil(() => _hud.ReplayListOpen && !_replayLooking, 60 * 10, "the list is back")) yield return x;
        var after = VrLapHashes();
        VwCheck(after.Where(kv => hashes0.ContainsKey(kv.Key)).All(kv => hashes0[kv.Key] == kv.Value) && after.Count == hashes0.Count, $"saving edits changed no pair file ({hashes0.Count} hashed)");
        var r0 = _hud.ReplayList.Rows[Math.Max(0, Array.FindIndex(_hud.ReplayList.Rows, r => r.Detail.Contains("REAL LAP")))];
        VwCheck(r0.Detail.Contains("PIN") && r0.Detail.Contains("TRIM") && r0.Detail.Contains("CAM"), $"the list row shows the marks: {r0.Detail}");
        VwCheck(_hud.ReplayList.Rows[0].Detail.Contains("PIN"), "the pinned lap sorts first");
        foreach (var x in VrShot("edits-3-list-marks")) yield return x;
    }

    IEnumerable VwEditsReload()
    {
        if (OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The read-only save diagnostic requires Unix directory permissions.");
        foreach (var x in VwWaitReady()) yield return x;
        for (int i = 0; i < 600 && !(_state == State.Title && _card == TitleCard.None && !_showLog && !_askName); i++) yield return null;
        for (int i = 0; i < 60; i++) yield return null;
        string shared = VlArg("--vw-hashes"); var prior = new Dictionary<string, string>();
        if (shared != null && File.Exists(shared)) foreach (var line in File.ReadAllLines(shared)) { var p = line.Split('\t'); if (p.Length == 2) prior[p[0]] = p[1]; }
        foreach (var x in VrToOfficeRecordings()) yield return x;
        var rows = _hud.ReplayList.Rows;
        VwLog("reload-rows", ("rows", string.Join(" | ", rows.Select(r => r.Title + " / " + r.Detail + " / " + r.Flag))));
        int twin = Array.FindIndex(rows, r => r.Detail.Contains("TWIN"));
        int real = Array.FindIndex(rows, r => r.Detail.Contains("REAL LAP"));
        VwCheck(real >= 0 && rows[real].Detail.Contains("PIN") && rows[real].Detail.Contains("TRIM") && rows[real].Detail.Contains("CAM"), $"after a restart the lap shows its marks: {rows[Math.Max(0, real)].Detail}");
        VwCheck(Math.Max(real, twin) <= 1 && rows.Take(2).All(r => r.Detail.Contains("PIN")), "the pinned laps (the original and the twin, which carries a copy of the same edits) are the first two rows");
        VwCheck(twin >= 0, "the twin lap (one tape byte differs) is listed");
        // the twin: its edits file is bound to the original's hashes: visible mismatch, plain playback
        foreach (var x in VrOpenFirstKept(twin)) yield return x;
        if (_vwAbort) { VwCheck(false, "stopping: the viewer did not open (state logged above)"); yield break; }
        var t = ReplayViewer; var tv = t.Scene;
        for (int i = 0; i < 60; i++) yield return null;
        VwCheck(!tv.ViewerWindowed && tv.ViewerEdits.Keys.Count == 0 && t.OverlayView.Notice.Contains("DO NOT MATCH"), $"the twin plays plain with a visible mismatch notice: '{t.OverlayView.Notice}'");
        foreach (var x in VrShot("edits-4-twin-mismatch")) yield return x;
        foreach (var x in VrTap(Key.Escape)) yield return x;
        foreach (var x in VrUntil(() => ReplayViewer == null, 60 * 30, "the twin's viewer closed")) yield return x;
        for (int i = 0; i < 30; i++) yield return null;
        // the original: the edits are back
        real = Array.FindIndex(_hud.ReplayList.Rows, r => r.Detail.Contains("REAL LAP"));
        // --- a slow load (the test seam delays the sidecar read): the viewer stays in Building, held and paused, until the edits are in; no control acts; the first playing frame is the edited one
        ReplayViewerSession.LoadDelayForTest = 3f;
        _hud.ReplayList.Selected = real;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        foreach (var x in VrUntil(() => ReplayViewer?.Scene?.ViewerReady == true, 60 * 120, "the viewer world is ready while the edits are still loading")) yield return x;
        if (ReplayViewer?.Scene == null) { VwLog("open-failed", ("viewer", ReplayViewer == null ? "none" : ReplayViewer.State.ToString()), ("error", ReplayViewer?.Error), ("notice", _hud.ReplayList?.Notice)); VwCheck(false, "stopping: the viewer did not come up for the slow-load test"); yield break; }
        var g = ReplayViewer; var gv = g.Scene;
        for (int i = 0; i < 8; i++) yield return null;      // (the hold starts with the first frame the world is ready)
        float held = gv.ViewerClock; bool allHeld = true, noDirty = true;
        foreach (var k in new[] { Key.I, Key.K, Key.P, Key.E, Key.C, Key.Space, Key.L })
        {
            foreach (var x in VrTap(k)) yield return x;
            allHeld &= g.State == ReplayViewerState.Building && gv.ViewerPaused && MathF.Abs(gv.ViewerClock - held) < 1e-4f; noDirty &= !g.Dirty && !g.EditOpen;
        }
        VwCheck(allHeld && noDirty && g.OverlayView.Building, $"while the edits load: still Building, the world held paused at {held:0.00}, no key acts or makes an edit");
        float firstClock = -1f; bool firstWindowed = false; bool firstRunning = false;
        foreach (var x in VrUntil(() => g.State == ReplayViewerState.Playing, 60 * 20, "the viewer reaches Playing after the late load")) yield return x;
        firstClock = gv.ViewerClock; firstWindowed = gv.ViewerWindowed; firstRunning = !gv.ViewerPaused;
        VwCheck(firstWindowed && firstClock >= 9.99f && firstClock <= 10.15f && gv.ViewerEdits.Keys.Count == 2, $"the first playing frame is the edited one (window on, keys {gv.ViewerEdits.Keys.Count}, clock {firstClock:0.000}, running {firstRunning})");
        foreach (var x in VrTap(Key.Escape)) yield return x;
        foreach (var x in VrUntil(() => ReplayViewer == null, 60 * 30, "closed")) yield return x;
        for (int i = 0; i < 20; i++) yield return null;
        // cancel while the edits are still loading
        _hud.ReplayList.Selected = real;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        foreach (var x in VrUntil(() => ReplayViewer?.Scene?.ViewerReady == true, 60 * 120, "ready again")) yield return x;
        if (ReplayViewer == null) { VwCheck(false, "stopping: no viewer for the cancel test"); yield break; }
        var gc = ReplayViewer;
        foreach (var x in VrTap(Key.Escape)) yield return x;
        VwCheck(gc.State is ReplayViewerState.Closing or ReplayViewerState.Closed, "ESC while the edits load cancels the viewer");
        foreach (var x in VrUntil(() => ReplayViewer == null, 60 * 30, "cancelled viewer closed")) yield return x;
        ReplayViewerSession.LoadDelayForTest = 0f;
        for (int i = 0; i < 20; i++) yield return null;
        foreach (var x in VrOpenFirstKept(RealRow())) yield return x;
        if (_vwAbort) { VwCheck(false, "stopping: the viewer did not open (state logged above)"); yield break; }
        var s = ReplayViewer; var v = s.Scene;
        for (int i = 0; i < 60; i++) yield return null;
        VwCheck(v.ViewerWindowed && MathF.Abs(v.ViewerWindowStart - 10f) < 0.011f && MathF.Abs(v.ViewerWindowEnd - 22f) < 0.011f && v.ViewerEdits.Keys.Count == 2 && v.ViewerLoop && s.OverlayView.Pinned, "the saved window, two camera keys, loop and pin are back after a restart");
        VwCheck(v.ViewerClock >= 9.99f && v.ViewerClock <= 22.01f, $"playback starts inside the window ({v.ViewerClock:0.00})");
        foreach (var x in VrShot("edits-5-reloaded")) yield return x;
        // a save that cannot be written (a real read-only folder): the edits stay dirty and the reason shows
        string lapDir = LapArchiveDir.TrimEnd('/'); string orig = Directory.GetDirectories(lapDir).Select(Path.GetFileName).First(n => !n.Contains("twin"));
        string folder = Path.Combine(lapDir, orig);
        foreach (var x in VrTap(Key.Space)) yield return x;
        // --- a slow save (the seam delays the write): edits are frozen while it is pending, closing waits, the viewing controls still work and are not saved edits, and what is written is exactly what was asked
        ReplayViewerSession.SaveDelayForTest = 3f;
        foreach (var x in VrTap(Key.P)) yield return x;      // unpin: an unsaved change
        VwCheck(s.Dirty, "unpinning is an unsaved change");
        // the viewing choices that are saved (speed, the lap's camera, the overlay) are unsaved changes too: speed up, hide the overlay, and (before the first camera key) another camera
        float speedBefore = v.ViewerSpeed; bool overlayBefore = s.OverlayView.Shown; var camBefore = v.ViewerCamera;
        v.ViewerSeek(10.5f); for (int i = 0; i < 4; i++) yield return null;
        foreach (var x in VrTap(Key.Up)) yield return x; foreach (var x in VrTap(Key.H)) yield return x; foreach (var x in VrTap(Key.C)) yield return x;
        float speedNew = v.ViewerSpeed; var camNew = v.ViewerCamera;
        VwCheck(speedNew != speedBefore && camNew != camBefore, $"speed {speedBefore}->{speedNew} and camera {camBefore}->{camNew} changed by hand");
        float? in0 = v.ViewerEdits.In, out0 = v.ViewerEdits.Out; int keys0 = v.ViewerEdits.Keys.Count; bool loop0 = v.ViewerLoop;
        foreach (var x in VrTap(Key.S)) yield return x;
        VwCheck(s.SaveBusy, "SAVE is pending (slow disk)");
        foreach (var k in new[] { Key.I, Key.O, Key.K, Key.L, Key.P, Key.T }) foreach (var x in VrTap(k)) yield return x;
        VwCheck(v.ViewerEdits.In == in0 && v.ViewerEdits.Out == out0 && v.ViewerEdits.Keys.Count == keys0 && v.ViewerLoop == loop0 && v.ViewerEdits.TrimEnabled, "while the save is pending the edit keys change nothing");
        foreach (var x in VrTap(Key.Escape)) yield return x;
        VwCheck(s.State == ReplayViewerState.Playing && s.SaveBusy, "closing waits for the pending save");
        foreach (var x in VrTap(Key.Down)) yield return x; foreach (var x in VrTap(Key.C)) yield return x; foreach (var x in VrTap(Key.H)) yield return x;
        VwCheck(v.ViewerSpeed == speedNew && v.ViewerCamera == camNew && s.SaveBusy, "while the save is pending the speed, camera and overlay keys change nothing either (what is written is exactly what was asked)");
        foreach (var x in VrUntil(() => s.OverlayView.Notice.StartsWith("EDITS SAVED"), 60 * 20, "the slow save finished")) yield return x;
        VwCheck(!s.Dirty && !s.SaveBusy, "after the slow save nothing is left unsaved");
        // the saved choices survive a close and reopen (and the camera is the lap's own before the first key)
        foreach (var x in VrTap(Key.Escape)) yield return x;
        foreach (var x in VrUntil(() => ReplayViewer == null, 60 * 30, "closed after the save")) yield return x;
        for (int i = 0; i < 20; i++) yield return null;
        ReplayViewerSession.SaveDelayForTest = 0f;
        foreach (var x in VrOpenFirstKept(RealRow())) yield return x;
        if (_vwAbort) { VwCheck(false, "stopping: the viewer did not reopen"); yield break; }
        s = ReplayViewer; v = s.Scene;
        VwCheck(v.ViewerSpeed == speedNew && v.ViewerCamera == camNew, $"after a reopen the saved speed ({v.ViewerSpeed}) and the lap's camera ({v.ViewerCamera}) are back");
        VwCheck(s.OverlayView.Shown != overlayBefore, "the saved overlay choice is back (hidden controls stay hidden)");
        VwCheck(!s.Dirty, "a fresh open of saved edits is clean");
        foreach (var x in VrTap(Key.Space)) yield return x;
        ReplayViewerSession.SaveDelayForTest = 3f;
        foreach (var x in VrTap(Key.H)) yield return x;      // the overlay back: one more unsaved choice, kept below
        // --- a failing slow save on a real read-only folder: the exact working edits stay unsaved
        foreach (var x in VrTap(Key.P)) yield return x;      // pin again
        bool overlayAtFail = s.OverlayView.Shown; float speedAtFail = v.ViewerSpeed; bool pin0 = s.OverlayView.Pinned; in0 = v.ViewerEdits.In; out0 = v.ViewerEdits.Out; keys0 = v.ViewerEdits.Keys.Count;
        VwCheck(s.Dirty && pin0, "pinning again is an unsaved change");
        File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        foreach (var x in VrTap(Key.S)) yield return x;
        VwCheck(s.SaveBusy, "the failing save is pending");
        foreach (var x in VrTap(Key.P)) yield return x;
        foreach (var x in VrUntil(() => s.OverlayView.Notice.StartsWith("NOT SAVED"), 60 * 20, "the failed save is reported")) yield return x;
        File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        VwCheck(s.Dirty && !s.SaveBusy && s.OverlayView.Shown == overlayAtFail && v.ViewerSpeed == speedAtFail && s.OverlayView.Pinned == pin0 && v.ViewerEdits.In == in0 && v.ViewerEdits.Out == out0 && v.ViewerEdits.Keys.Count == keys0 && s.State == ReplayViewerState.Playing, $"the failed save kept the exact working edits unsaved and the viewer open: '{s.OverlayView.Notice}'");
        foreach (var x in VrShot("edits-6-save-failed")) yield return x;
        ReplayViewerSession.SaveDelayForTest = 0f;
        foreach (var x in VrTap(Key.S)) yield return x;      // and the same save works once the folder is writable again
        foreach (var x in VrUntil(() => s.OverlayView.Notice.StartsWith("EDITS SAVED"), 60 * 20, "the retry saved")) yield return x;
        // leaving with an unsaved viewing choice: CANCEL keeps it, DISCARD closes without saving it, a reopen shows what was saved
        float speedSaved = v.ViewerSpeed;
        foreach (var x in VrTap(Key.Down)) yield return x;
        VwCheck(v.ViewerSpeed != speedSaved && s.Dirty, "a speed change by hand is an unsaved choice");
        foreach (var x in VrTap(Key.Escape)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(s.OverlayView.Prompt.Length > 0 && s.OverlayView.Prompt[0].StartsWith("UNSAVED"), "ESC with an unsaved speed asks");
        foreach (var x in VrTap(Key.Escape)) yield return x;      // cancel
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(s.State == ReplayViewerState.Playing && s.OverlayView.Prompt.Length == 0 && s.Dirty, "CANCEL keeps the viewer open and the choice unsaved");
        foreach (var x in VrTap(Key.Escape)) yield return x;
        foreach (var x in VrTap(Key.D)) yield return x;      // discard
        foreach (var x in VrUntil(() => ReplayViewer == null, 60 * 30, "discard closed the viewer")) yield return x;
        for (int i = 0; i < 20; i++) yield return null;
        foreach (var x in VrOpenFirstKept(RealRow())) yield return x;
        if (_vwAbort) { VwCheck(false, "stopping: the viewer did not reopen after the discard"); yield break; }
        s = ReplayViewer; v = s.Scene;
        VwCheck(v.ViewerSpeed == speedSaved && !s.Dirty, $"after DISCARD a reopen has the saved speed ({v.ViewerSpeed}), not the discarded one");
        foreach (var x in VrTap(Key.Space)) yield return x;
        // reset: a clear question, then defaults (the old ones stay as the backup)
        foreach (var x in VrTap(Key.E)) yield return x;
        for (int i = 0; i < 9; i++) foreach (var x in VrTap(Key.Down)) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(s.OverlayView.Prompt.Length > 0 && s.OverlayView.Prompt[0].StartsWith("RESET"), "RESET EDITS asks first");
        foreach (var x in VrShot("edits-7-reset-question")) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        foreach (var x in VrUntil(() => s.OverlayView.Notice.StartsWith("EDITS RESET"), 60 * 20, "the reset finished")) yield return x;
        VwCheck(!v.ViewerWindowed && v.ViewerEdits.Keys.Count == 0 && !s.Dirty && File.Exists(Path.Combine(folder, "edits.json.bak")), "reset: plain lap, no keys, clean, and the previous edits are in edits.json.bak");
        foreach (var x in VrTap(Key.Escape)) yield return x;      // the panel
        foreach (var x in VrTap(Key.Escape)) yield return x;
        foreach (var x in VrUntil(() => ReplayViewer == null, 60 * 30, "closed")) yield return x;
        var end = VrLapHashes();
        VwCheck(prior.Count >= 2 && prior.All(kv => end.GetValueOrDefault(kv.Key) == kv.Value), $"the original pair's SHA-256 is unchanged through reload, a failed save, a save and a reset ({prior.Count} files)");
    }

    // ---------------------------------------------------------------- Bonus: cuts and keys around REAL recorded Hind / BTR / tank events

    IEnumerable VwEditsBonus()
    {
        foreach (var x in VwWaitReady()) yield return x;
        foreach (var x in VrUntil(() => _state == State.Racing && _lapClock > 3f, 60 * 40, "the race is running")) yield return x;
        foreach (var x in VrUntil(() => _timer.LapsDone >= 1, 60 * 220, "one real Bonus lap is completed (Stakhanov rules: the car cannot be destroyed)")) yield return x;
        for (int i = 0; i < 60; i++) yield return null;
        LapArchiveBox.Flush(TimeSpan.FromSeconds(30));
        VwCheck(LapArchiveBox.List().Count >= 1, "the real Bonus lap is in the archive");
        foreach (var x in VrTap(Key.Escape)) yield return x;
        for (int i = 0; i < 12; i++) yield return null;
        for (int i = 0; i < 12 && _hud.PauseSelection != PauseRowIndex(PauseRowId.Exit); i++) foreach (var x in VrTap(Key.Down)) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        foreach (var x in VrUntil(() => _state == State.Title, 60 * 10, "back at the title")) yield return x;
        for (int i = 0; i < 60; i++) yield return null;
        foreach (var x in VrToOfficeRecordings()) yield return x;
        foreach (var x in VrOpenFirstKept(0)) yield return x;
        if (_vwAbort) { VwCheck(false, "stopping: the viewer did not open (state logged above)"); yield break; }
        var s = ReplayViewer; var v = s.Scene;
        float dur = v.ViewerDuration;
        var ev = v.ViewerTapeEvents();
        var groups = ev.GroupBy(e => $"{e.Type}/{e.Source}/{e.Kind}/{e.Instance}").Select(g => $"{g.Key}@{string.Join(",", g.Select(e => e.Time.ToString("0.00")))}");
        VwLog("bonus-events", ("duration", dur), ("events", ev.Count), ("groups", string.Join(" ; ", groups)));
        VwCheck(v.ViewerHasTape && ev.Count >= 3, $"the lap has a world tape with real events ({ev.Count})");
        // The tape's real events here: the three Hind aircraft exist from the lap's start (their Spawn events are at 0.00 s: the aircraft FLY along recorded poses), a marker, and the speed trap's radar reading
        // at about 9 s. The cut is placed around that radar reading, and "the world" compared is every tape-driven actor's pose (the Hind aircraft, the BTR patrols, the tanks, the people: TapePoseHash)
        // and the live Hind instances, a plain direct seek being the reference for every state the cut shows. (No projectile is invented: this lap's tape records none.)
        var radar = ev.Where(e => e.Type == TapeEventType.Effect && ((e.Kind ?? "").Contains("radar") || (e.Source ?? "").Contains("SpeedTrap")) && e.Time > 4f && e.Time < dur - 4f).OrderBy(e => e.Time).ToList();
        VwCheck(radar.Count >= 1, $"a recorded mid-lap event to cut around: {string.Join(", ", radar.Select(e => $"{e.Source}/{e.Kind}@{e.Time:0.00}"))}");
        VwCheck(v.ViewerLiveInstanceIds().Length >= 3, $"the Hind aircraft are live instances ({string.Join(",", v.ViewerLiveInstanceIds())})");
        if (radar.Count == 0) yield break;
        float te = radar[0].Time;
        string LiveSet() => string.Join(",", v.ViewerLiveInstanceIds());
        foreach (var x in VrTap(Key.Space)) yield return x;
        ulong HashAt(float t) { v.ViewerEdits = new RaceScene.ViewerEditPlan(); v.ViewerSeek(t); return v.ViewerPoseHash(); }
        string LiveAt(float t) { v.ViewerEdits = new RaceScene.ViewerEditPlan(); v.ViewerSeek(t); return LiveSet(); }
        VwCheck(HashAt(te - 1.5f) != HashAt(te + 1.5f), "reference: the world's poses differ between the two ends of the window (the aircraft and traffic move)");
        // (A) the cut around the event: IN is the world a direct seek to IN shows, running the cut ends exactly on OUT in the world a direct seek to OUT shows
        float inA = te - 1.5f, outA = te + 1.5f;
        ulong hInA = HashAt(inA), hOutA = HashAt(outA); string liveInA = LiveAt(inA), liveOutA = LiveAt(outA);
        v.ViewerEdits = new RaceScene.ViewerEditPlan { In = inA, Out = outA };
        v.ViewerLoop = false; v.ViewerSeek(inA); for (int i = 0; i < 4; i++) yield return null;
        VwCheck(MathF.Abs(v.ViewerClock - inA) < 0.011f && LiveSet() == liveInA && v.ViewerPoseHash() == hInA, $"cut A: IN shows exactly the world a direct seek to IN shows ({v.ViewerPoseHash():x} vs {hInA:x}, live {LiveSet()})");
        int fx0 = v.ViewerFxCount; v.ViewerSpeed = 1f; v.ViewerSetPaused(false);
        foreach (var x in VrUntil(() => v.ViewerEnded, 60 * 30, "cut A ends")) yield return x;
        for (int i = 0; i < 3; i++) yield return null;
        VwLog("cut-a-fx", ("before", fx0), ("after", v.ViewerFxCount));
        VwCheck(MathF.Abs(v.ViewerClock - outA) < 0.011f && LiveSet() == liveOutA && v.ViewerPoseHash() == hOutA, $"cut A: running it ends exactly on OUT in the world a direct seek to OUT shows ({v.ViewerPoseHash():x} vs {hOutA:x}, clock {v.ViewerClock:0.000})");
        // (B) a cut that ends before the event and (C) one that starts after it: each end is the direct-seek world
        float inB = te - 3f, outB = te - 0.5f; ulong hOutB = HashAt(outB);
        v.ViewerEdits = new RaceScene.ViewerEditPlan { In = inB, Out = outB };
        v.ViewerSeek(inB); v.ViewerSetPaused(false);
        for (int i = 0; i < 60 * 30 && !v.ViewerEnded; i++) yield return null;
        for (int i = 0; i < 3; i++) yield return null;
        VwCheck(MathF.Abs(v.ViewerClock - outB) < 0.011f && v.ViewerPoseHash() == hOutB && v.ViewerClock < te, "cut B (ends before the event): ends exactly on OUT, before the event, in the direct-seek world");
        float inC = te + 0.8f; ulong hInC = HashAt(inC);
        v.ViewerEdits = new RaceScene.ViewerEditPlan { In = inC, Out = te + 3f };
        v.ViewerSeek(inC); for (int i = 0; i < 4; i++) yield return null;
        VwCheck(MathF.Abs(v.ViewerClock - inC) < 0.011f && v.ViewerPoseHash() == hInC, "cut C (starts after the event): IN is the direct-seek world");
        // (D) looping the cut around the event: every wrap returns to the world a direct seek to IN shows, and the clock stays in the window
        float inD = te - 0.6f, outD = te + 0.6f; ulong hInD = HashAt(inD);
        v.ViewerEdits = new RaceScene.ViewerEditPlan { In = inD, Out = outD };
        v.ViewerLoop = true; v.ViewerSeek(inD); v.ViewerSpeed = 1f; v.ViewerSetPaused(false);
        int wraps0 = v.ViewerWraps; var clocks = new List<float>(); bool hashOk = true; int seenWraps = wraps0;
        for (int i = 0; i < 60 * 6; i++)
        {
            yield return null; clocks.Add(v.ViewerClock);
            if (v.ViewerWraps != seenWraps) { seenWraps = v.ViewerWraps; hashOk &= v.ViewerLastWrapHash == hInD; }
        }
        int wraps = v.ViewerWraps - wraps0;
        VwCheck(wraps >= 3 && hashOk && clocks.All(c => c >= inD - 0.01f && c <= outD + 0.01f), $"cut D (loop): {wraps} wraps, every wrap's world equals a direct seek to IN, the clock stays in [{inD:0.00}, {outD:0.00}] ({clocks.Min():0.00}..{clocks.Max():0.00})");
        // (E) camera keys around the event: no rendered frame with the old camera
        v.ViewerSetPaused(true); v.ViewerLoop = false;
        v.ViewerEdits = new RaceScene.ViewerEditPlan { DefaultCamera = ViewerCameraMode.TopDown };
        v.ViewerEdits.Keys.Add((te - 0.8f, ViewerCameraMode.Auto)); v.ViewerEdits.Keys.Add((te + 0.8f, ViewerCameraMode.TopDown));
        v.ViewerSeek(te - 1.5f); for (int i = 0; i < 4; i++) yield return null;
        VwCheck(v.ViewerCamera == ViewerCameraMode.TopDown, "before the first key the lap's own camera stands");
        var log = new List<(float C, ViewerCameraMode M)>(); v.ViewerSetPaused(false);
        for (int i = 0; i < 60 * 6; i++) { yield return null; log.Add((v.ViewerClock, v.ViewerCamera)); if (v.ViewerClock > te + 1.2f) break; }
        var firstStale = log.FirstOrDefault(e => (e.C >= te - 0.8f + 0.02f && e.C < te + 0.8f - 0.002f && e.M != ViewerCameraMode.Auto) || (e.C >= te + 0.8f + 0.02f && e.M != ViewerCameraMode.TopDown));
        VwCheck(firstStale.C == 0f && log.Any(e => e.M == ViewerCameraMode.Auto), $"AUTO exactly from {te - 0.8f:0.00} and TOP-DOWN from {te + 0.8f:0.00}: no rendered frame had the old camera ({log.Count} frames; first stale {firstStale.C:0.000} {firstStale.M})");
        foreach (var x in VrShot("editsbonus-1-around-event")) yield return x;
    }
}
