using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CCCPRacing.Export;
using CCCPRacing.Replay;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

// Windowed scenario "export-ui" (--viewer-windowed-only=exportui, --vw-fx=DIR with DIR/TRACK retained fixture laps): the MP4 export through the REAL viewer UI on a kept lap with UNSAVED edits
// (a cut and a camera key). The card, a real 4-second export of THE CUT, progress and result screens, a cancelled second export, and proof that nothing was saved or discarded.
public partial class RaceScene
{
    /// <summary>Move the EDIT panel's cursor to a row from wherever it is (the panel keeps no assumption of row 0).</summary>
    IEnumerable ToExportRow(ReplayViewerSession s, int row)
    {
        int n = s.EditRowTexts.Length;
        for (int i = 0; i < n && s.OverlayView.EditSelected != row; i++) foreach (var x in VrTap(Key.Down)) yield return x;
    }

    IEnumerable VwExportUi()
    {
        bool freeShots = VlArg("--export-free") == "1";
        ExportUiController.KeepDiagnosticsForTest = freeShots;
        foreach (var x in VwWaitReady()) yield return x;
        for (int i = 0; i < 600 && !(_state == State.Title && _card == TitleCard.None && !_showLog && !_askName); i++) yield return null;
        for (int i = 0; i < 60; i++) yield return null;
        if (VlArg("--vw-native") == "1")
        {
            // VISUALS > RENDERING NATIVE in the player's game and on disk: the exporter child must still render its fixed pixel movie
            StepRendering(); for (int i = 0; i < 4; i++) yield return null;
            var native = new ConfigFile(); native.Parse(ExportSettingsSnapshot());
            VwCheck(PixelFrame.Native && native.GetValue("video", "rendering", 0).AsInt32() == 1 && (GetViewport() as SubViewport)?.Size2DOverrideStretch == true,
                $"NATIVE before the export: the game renders at {(GetViewport() as SubViewport)?.Size}, the child's settings say rendering=1");
        }
        string fx = VlArg("--vw-fx") ?? "/tmp/cccp-lc-fx", track = VlArg("--vw-track") ?? "ilyich";
        var jobsBefore = (VlArg("--export-jobs-dir") is string j0 && Directory.Exists(j0) ? Directory.GetDirectories(j0) : Array.Empty<string>()).ToHashSet();      // (orphans of runs that were killed stay: only job folders made by this run are judged)
        var filesBefore = (VlArg("--export-dir") is string d0 && Directory.Exists(d0) ? Directory.GetFiles(d0) : Array.Empty<string>()).ToHashSet();      // (earlier runs' outputs stay: only what this run makes is judged)
        // the SHARED fixture is never edited: the scenario works on a unique clone (pair + meta + layout copied byte for byte into this run's own folder, no sidecar)
        var shared = LapArchive.For(Path.Combine(fx, track)); var sinfo = shared.List().FirstOrDefault(i => i.Openable && i.Retained);
        VwCheck(sinfo != null, "a kept fixture lap exists");
        if (sinfo == null) yield break;
        string cloneRoot = Path.Combine(VlArg("--export-dir") ?? Path.GetTempPath(), "fixture-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"), track);
        string cloneDir = Path.Combine(cloneRoot, Path.GetFileName(sinfo.Dir)); Directory.CreateDirectory(cloneDir);
        foreach (var f in new[] { "ghost.bin", "tape.bin", "meta.json", "records.ver" }) if (File.Exists(Path.Combine(sinfo.Dir, f))) File.Copy(Path.Combine(sinfo.Dir, f), Path.Combine(cloneDir, f));
        string H(string f) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(f)));
        VwCheck(new[] { "ghost.bin", "tape.bin" }.All(f => H(Path.Combine(sinfo.Dir, f)) == H(Path.Combine(cloneDir, f))) && !File.Exists(Path.Combine(cloneDir, "edits.json")), $"the clone's pair is byte-identical to the shared fixture's and has no sidecar ({Path.GetFileName(cloneDir)})");
        var archive = LapArchive.For(cloneRoot); var info = archive.List().FirstOrDefault(i => i.Openable && i.Retained);
        VwCheck(info != null && info.Dir == cloneDir, "the clone opens as a kept lap");
        if (info == null) yield break;
        // the export reads the CURRENT sound balance, not the disk's: a changed level that was never saved shows up in the snapshot the child gets
        int old0 = Audio.SoundBalance.Levels[0]; Audio.SoundBalance.Levels[0] = old0 == 0 ? 3 : 0;
        string snap = ExportSettingsSnapshot(); var snapCfg = new ConfigFile(); snapCfg.Parse(snap);
        VwCheck(snapCfg.GetValue("sound", Audio.SoundBalance.Keys[0], -1).AsInt32() == Audio.SoundBalance.Levels[0], $"the export snapshot carries the live sound level {Audio.SoundBalance.Levels[0]} (the disk has {old0})");
        VwCheck(!snap.Contains("[leaderboard") && !snap.Contains("lap"), "the snapshot is the settings file only");
        Audio.SoundBalance.Levels[0] = old0;
        var before = Directory.GetFiles(info.Dir).ToDictionary(f => Path.GetFileName(f), f => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(f))));
        var lap = archive.Open(info.Id);
        var s = OpenReplayViewer(lap, out string err);
        VwCheck(s != null, "the viewer opened (" + err + ")");
        if (s == null) yield break;
        s.SetEditContext(archive, info); s.SetTitle("EXPORT UI TEST");
        foreach (var x in VrUntil(() => s.State == ReplayViewerState.Playing, 60 * 120, "playing")) yield return x;
        var v = s.Scene;
        foreach (var x in VrTap(Key.Space)) yield return x;
        // unsaved edits: a cut 10..14 and a camera key
        v.ViewerSeek(10f); for (int i = 0; i < 4; i++) yield return null; foreach (var x in VrTap(Key.I)) yield return x;
        v.ViewerSeek(14f); for (int i = 0; i < 4; i++) yield return null; foreach (var x in VrTap(Key.O)) yield return x;
        v.ViewerSeek(12f); for (int i = 0; i < 4; i++) yield return null; foreach (var x in VrTap(Key.C)) yield return x; foreach (var x in VrTap(Key.K)) yield return x;
        VwCheck(s.Dirty && v.ViewerEdits.Keys.Count == 1, "unsaved edits: a cut and one camera key");
        if (freeShots)
        {
            foreach (var x in VrTap(Key.T)) yield return x; // temporarily disable cut to place a key before IN
            v.ViewerSeek(9f);
            foreach (var x in VrTap(Key.V)) yield return x;
            int freeRow = Array.FindIndex(s.EditRowTexts, r => r.StartsWith("FREE CAMERA"));
            foreach (var x in ToExportRow(s, freeRow)) yield return x;
            foreach (var x in VrTap(Key.Enter)) yield return x;
            foreach (var x in VrTap(Key.W, 15)) yield return x;
            foreach (var x in VrTap(Key.V, 10)) yield return x;
            if (!v.ViewerFreeRig.Follow) foreach (var x in VrTap(Key.F)) yield return x;
            if (!v.ViewerFreeRig.Look) foreach (var x in VrTap(Key.G)) yield return x;
            if (VlArg("--camera-motion") == "1") foreach (var x in VwConfigureMotion(s, v)) yield return x;
            foreach (var x in VrTap(Key.K)) yield return x;
            foreach (var x in VrTap(Key.Escape)) yield return x;
            v.ViewerSeek(12f);
            foreach (var x in VrTap(Key.V)) yield return x;
            foreach (var x in ToExportRow(s, freeRow)) yield return x;
            foreach (var x in VrTap(Key.Enter)) yield return x;
            if (v.ViewerFreeRig.Follow) foreach (var x in VrTap(Key.F)) yield return x;
            if (v.ViewerFreeRig.Look) foreach (var x in VrTap(Key.G)) yield return x;
            foreach (var x in VrTap(Key.D, 30)) yield return x;
            foreach (var x in VrTap(Key.K)) yield return x;
            foreach (var x in VrTap(Key.Escape)) yield return x;
            foreach (var x in VrTap(Key.T)) yield return x;
            VwCheck(s.Dirty && v.ViewerEdits.FreeKeys.Count == 2 && v.ViewerEdits.FreeKeys[9f].Follow && !v.ViewerEdits.FreeKeys[12f].Follow,
                "unsaved free follow/look key precedes IN, fixed shot replaces the normal key at the internal cut");
            foreach (float at in new[] { 10f, 12f, 10f + 239f / 60f })
            {
                v.ViewerSeek(at); for (int i = 0; i < 4; i++) yield return null;
                string label = at == 10f ? "in" : at == 12f ? "cut" : "last";
                var image = v.GetViewport().GetTexture().GetImage();
                image.SavePng(Path.Combine(VlArg("--vw-media"), "reference-" + label + ".png"));
                VwLog("free-reference", ("label", label), ("clock", at), ("size", image.GetSize()));
            }
            v.ViewerSeek(10f); v.ViewerEndFreeCamera();
            for (int i = 0; i < 4; i++) yield return null;
            v.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(VlArg("--vw-media"), "reference-wrong-topdown.png"));
            v.ViewerSeek(10f);
        }
        // E opens the panel: EXPORT is a row of it
        foreach (var x in VrTap(Key.V)) yield return x;
        var rows = s.EditRowTexts;
        int exportRow = Array.FindIndex(rows, r => r.StartsWith("EXPORT"));
        VwCheck(exportRow >= 0, "the EDIT panel lists EXPORT MP4... (" + string.Join(" | ", rows) + ")");
        foreach (var x in VrShot("export-1-edit-panel")) yield return x;
        var ov = s.OverlayView;
        foreach (var x in ToExportRow(s, exportRow)) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(s.ExportOpen && ov.ExportRows.Length >= 7, "the export card is up: " + string.Join(" | ", ov.ExportRows));
        float modalClock = v.ViewerClock, modalSpeed = v.ViewerSpeed;
        bool modalPaused = v.ViewerPaused;
        foreach (var key in new[] { Key.Right, Key.Left, Key.Down, Key.Right, Key.Left, Key.Up, Key.Comma, Key.Period })
        {
            foreach (var x in VrTap(key, 40)) yield return x;
            VwCheck(MathF.Abs(v.ViewerClock - modalClock) < 0.001f && v.ViewerSpeed == modalSpeed && v.ViewerPaused == modalPaused,
                $"export owns held {key}: replay clock/speed/pause unchanged");
        }
        foreach (var button in new[] { JoyButton.DpadRight, JoyButton.DpadLeft, JoyButton.LeftShoulder, JoyButton.RightShoulder })
        {
            foreach (var x in VrPad(button, 40)) yield return x;
            VwCheck(MathF.Abs(v.ViewerClock - modalClock) < 0.001f && v.ViewerPaused == modalPaused,
                $"export owns held pad {button}: no seek or step");
        }
        _vwDrv.SetControl(true);
        Input.ParseInputEvent(VwKey(Key.Right, true, 0));
        for (int i = 0; i < 4; i++) yield return null;
        foreach (var x in VrTap(Key.Escape)) yield return x;
        for (int i = 0; i < 40; i++) yield return null;
        VwCheck(!s.ExportOpen && MathF.Abs(v.ViewerClock - modalClock) < 0.001f, "closing the card while RIGHT is held does not leak a seek");
        Input.ParseInputEvent(VwKey(Key.Right, false, 0));
        for (int i = 0; i < 4; i++) yield return null;
        foreach (var x in VrTap(Key.Right)) yield return x;
        VwCheck(v.ViewerClock > modalClock + 0.1f, "after release a fresh RIGHT press seeks normally");
        v.ViewerSeek(modalClock);v.ViewerSetPaused(modalPaused);_vwDrv.SetControl(false);
        if (VlArg("--export-input-only") == "1") { s.Close(); yield break; }
        foreach (var x in VrTap(Key.V)) yield return x;
        foreach (var x in ToExportRow(s, exportRow)) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        if (ov.ExportRows[0].Contains("THE CUT")) foreach (var x in VrTap(Key.Left)) yield return x;
        foreach (var x in VrShot("export-2-card-whole")) yield return x;
        foreach (var x in VrTap(Key.Right)) yield return x;      // SPAN: THE CUT
        VwCheck(ov.ExportRows[0].Contains("THE CUT"), "SPAN offers THE CUT because a cut is on: " + ov.ExportRows[0]);
        foreach (var x in VrTap(Key.Down)) yield return x; foreach (var x in VrTap(Key.Right)) yield return x; foreach (var x in VrTap(Key.Right)) yield return x;      // SIZE: 540P
        VwCheck(ov.ExportRows[1].Contains("540"), "SIZE 540P: " + ov.ExportRows[1]);
        VwCheck(ov.ExportRows[4].Contains("240 FRAMES"), "LENGTH shows 4 s = 240 frames: " + ov.ExportRows[4]);
        foreach (var x in VrShot("export-3-card-cut")) yield return x;
        for (int i = 0; i < 4; i++) foreach (var x in VrTap(Key.Down)) yield return x;      // START EXPORT
        foreach (var x in VrTap(Key.Enter)) yield return x;
        bool sawBar = false; var wall0 = System.Diagnostics.Stopwatch.StartNew();
        VwCheck(s.ExportOpen, "START EXPORT began the export");
        while (s.ExportOpen && wall0.Elapsed.TotalSeconds < 600)
        {
            yield return null;
            if (!sawBar && ov.ExportFraction > 0.2f) { sawBar = true; foreach (var x in VrShot("export-4-progress")) yield return x; }
            if (ov.ExportRows.Length == 0 && ov.ExportLines.Length > 0 && ov.ExportTitle.StartsWith("EXPORT DONE")) break;
        }
        VwCheck(sawBar, "the progress bar showed during the export");
        VwCheck(ov.ExportTitle.StartsWith("EXPORT DONE"), $"the result card says DONE: {ov.ExportTitle} / {string.Join(" | ", ov.ExportLines)}");
        foreach (var x in VrShot("export-5-done")) yield return x;
        string outPath = ov.ExportLines.Skip(1).FirstOrDefault(l => l.Contains("export-ui")) ;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(!s.ExportOpen, "Enter closes the result card");
        string destDir = VlArg("--export-dir");
        var mp4 = destDir == null ? null : Directory.GetFiles(destDir, "EXPORT-UI-TEST*.mp4").Where(f => !filesBefore.Contains(f)).OrderBy(f => File.GetLastWriteTimeUtc(f)).LastOrDefault();
        VwCheck(mp4 != null && new FileInfo(mp4).Length > 20_000, "the MP4 exists: " + mp4);
        VwLog("export-ui-output", ("path", mp4));
        if (freeShots && mp4 != null)
        {
            string diag = Path.Combine(Path.GetDirectoryName(mp4), Path.GetFileNameWithoutExtension(mp4) + ".diag");
            var plan = ExportPlan.FromJson(File.ReadAllText(Path.Combine(diag, "plan.json")));
            var poses = File.ReadAllLines(Path.Combine(diag, "free-camera-poses.jsonl"));
            VwCheck(plan.Request.CameraKeys.Count == 2 && plan.Request.CameraKeys[0].T < plan.Request.InClock
                && plan.Request.CameraKeys.All(k => k.Mode == "Free" && k.Free != null) && poses.Length == 240,
                "the real UI export plan preserves both payloads, preceding key and every frame pose");
        }
        // nothing was saved or discarded: the edits are still unsaved, still there, the sidecar absent, the lap unchanged
        VwCheck(s.Dirty && v.ViewerEdits.Keys.Count == (freeShots ? 2 : 1) && v.ViewerWindowed && !File.Exists(Path.Combine(info.Dir, "edits.json")), $"the export neither saved nor discarded the unsaved edits (dirty {s.Dirty}, keys {v.ViewerEdits.Keys.Count}, windowed {v.ViewerWindowed} {v.ViewerEdits.In}..{v.ViewerEdits.Out} trim {v.ViewerEdits.TrimEnabled}, sidecar {File.Exists(Path.Combine(info.Dir, "edits.json"))})");
        var after = Directory.GetFiles(info.Dir).ToDictionary(f => Path.GetFileName(f), f => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(f))));
        VwCheck(before.Count == after.Count && before.All(kv => after.GetValueOrDefault(kv.Key) == kv.Value), "the original lap files are byte-identical");
        // a second export, cancelled through the UI (ESC asks, Enter confirms)
        foreach (var x in VrTap(Key.V)) yield return x;
        foreach (var x in ToExportRow(s, exportRow)) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        VwCheck(s.ExportOpen, "the export card opened for the second export (the selection was moved to the EXPORT row, not assumed)");
        if (!s.ExportOpen) { s.Close(); yield break; }
        for (int i = 0; i < 5; i++) foreach (var x in VrTap(Key.Down)) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;      // START (whole lap, 1080p: long enough to cancel)
        var wall = System.Diagnostics.Stopwatch.StartNew();
        while (ov.ExportFraction < 0.02f && wall.Elapsed.TotalSeconds < 240 && s.ExportOpen) yield return null;
        VwCheck(ov.ExportFraction >= 0.02f, $"the second export is rendering ({ov.ExportFraction:0.00} after {wall.Elapsed.TotalSeconds:0} s)");
        foreach (var x in VrTap(Key.Escape)) yield return x;
        VwCheck(ov.ExportLines.Any(l => l.StartsWith("CANCEL THE EXPORT")), "ESC during the export asks first");
        foreach (var x in VrShot("export-6-cancel-question")) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        wall.Restart(); while (!ov.ExportTitle.StartsWith("EXPORT CANCELLED") && wall.Elapsed.TotalSeconds < 90) yield return null;
        VwCheck(ov.ExportTitle.StartsWith("EXPORT CANCELLED"), "the export was cancelled: " + ov.ExportTitle);
        foreach (var x in VrTap(Key.Enter)) yield return x;
        var made = destDir == null ? Array.Empty<string>() : Directory.GetFiles(destDir, "*").Where(f => !filesBefore.Contains(f)).ToArray();
        VwCheck(made.Length == 1 && made[0] == mp4, $"this run made only the finished file, nothing from the cancelled run (made: {string.Join(", ", made.Select(Path.GetFileName))}; finished: {Path.GetFileName(mp4)})");
        VwCheck(!Directory.GetFiles(destDir, "*.part*").Any(f => !filesBefore.Contains(f)) && !Directory.GetFiles(destDir, ".*").Any(f => !filesBefore.Contains(f)), "no .part file was left by this run");
        var jobsDir = VlArg("--export-jobs-dir");
        var lastJob = s.LastExportJob; if (lastJob != null) { var wj = System.Diagnostics.Stopwatch.StartNew(); while (!lastJob.Done.IsCompleted && wj.Elapsed.TotalSeconds < 30) yield return null; }
        VwCheck(lastJob != null && lastJob.Done.IsCompleted, "the cancelled job's own task completed");
        VwCheck(lastJob != null && jobsDir != null && !Directory.Exists(Path.Combine(jobsDir, lastJob.JobId)), $"this job's own folder ({lastJob?.JobId}) is gone");
        var jobsNow = jobsDir != null && Directory.Exists(jobsDir) ? Directory.GetDirectories(jobsDir).Where(d => !jobsBefore.Contains(d)).Select(Path.GetFileName).ToArray() : Array.Empty<string>();
        VwCheck(jobsNow.Length == 0, $"no job folder of this run remains once the job's task is complete (new folders: {string.Join(", ", jobsNow)})");
        ExportUiController.KeepDiagnosticsForTest = false;
        s.Close();
    }
}
