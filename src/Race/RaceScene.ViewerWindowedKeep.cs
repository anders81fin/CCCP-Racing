using System;
using System.Collections;
using System.IO;
using System.Linq;
using CCCPRacing.Replay;
using Godot;

namespace CCCPRacing.Race;

public partial class RaceScene
{
    IEnumerable VwViewerKeep()
    {
        foreach (var x in VwWaitReady()) yield return x;
        if (VlArg("--data-dir") == null || LapArchiveBox.List().Count != 0)
        { VwCheck(false, "KEEP scenario requires isolated empty profile"); yield break; }
        var fixture = LapArchive.For(Path.Combine(VlArg("--vw-fx") ?? "/tmp/cccp-lc-fx", "ilyich"));
        var original = fixture.Open(fixture.List().First(i => i.Openable && i.Retained).Id);
        string originalHash = original.Sha256(); var a = LapArchiveBox;
        var sub = a.Submit(new LapCapture { GhostBytes = original.GhostBytes.ToArray(), GhostFingerprint = original.Meta.GhostFingerprint,
            TrackId = "ilyich", Set = 0, Mode = "rally", LapSeconds = original.Meta.LapSeconds, Profile = _file.ToString(), TrackSignature = original.Meta.TrackSignature });
        a.Flush(TimeSpan.FromSeconds(15));
        var info = a.List().Single(); var source = a.Open(sub.Id); string hash = source.Sha256();
        var s = OpenReplayViewer(source, out string err); VwCheck(s != null, "unkept lap opens: " + err);
        if (s == null) yield break;
        s.SetEditContext(a, info); s.SetTitle("VIEWER KEEP TEST");
        foreach (var x in VrUntil(() => s.State == ReplayViewerState.Playing, 7200, "viewer ready")) yield return x;
        var v = s.Scene; v.ViewerSetPaused(true); v.ViewerSeek(5f);
        foreach (var x in VrTap(Key.V)) yield return x;
        int keep = Array.FindIndex(s.EditRowTexts, t => t.StartsWith("KEEP RECORDING"));
        VwCheck(keep >= 0 && !s.Editable, "unkept viewer has KEEP RECORDING");
        foreach (var x in ToExportRow(s, keep)) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        VwCheck(s.KeepOpen, "KEEP opens name card inside the viewer");
        foreach (var x in VrShot("viewer-keep-name")) yield return x;
        float t = v.ViewerClock;
        foreach (var x in VrTap(Key.Right, 30)) yield return x;
        foreach (var x in VrTap(Key.Comma)) yield return x;
        VwCheck(MathF.Abs(v.ViewerClock - t) < .001f && v.ViewerPaused, "name card consumes held seek and frame-step keys");
        foreach (var x in VrTap(Key.Escape)) yield return x;
        VwCheck(!s.KeepOpen && !a.List().Single().Retained && s.State == ReplayViewerState.Playing && v.ViewerClock == t,
            "cancel leaves recording unkept and viewer at same paused frame");
        foreach (var x in VrTap(Key.V)) yield return x;
        foreach (var x in ToExportRow(s, keep)) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        ReplayViewerSession.ViewerKeepFailForTest = "test disk refusal";
        foreach (var x in VrTap(Key.Enter)) yield return x;
        foreach (var x in VrUntil(() => !s.KeepBusy, 300, "keep refusal")) yield return x;
        VwCheck(s.KeepOpen && !s.Editable && !a.List().Single().Retained && s.OverlayView.Notice.StartsWith("NOT KEPT:"), "failed KEEP is visible, retryable and does not close or promote the lap");
        ReplayViewerSession.ViewerKeepFailForTest = null;
        foreach (var x in VrPad(JoyButton.DpadUp)) yield return x;
        ReplayViewerSession.ViewerKeepDelayForTest = 2f;
        foreach (var x in VrPad(JoyButton.Start)) yield return x;
        foreach (var x in VrTap(Key.Escape)) yield return x;
        foreach (var x in VrTap(Key.Right, 30)) yield return x;
        VwCheck(s.KeepBusy && s.State == ReplayViewerState.Playing && v.ViewerClock == t && v.ViewerPaused,
            "pending KEEP consumes exit and held seek until its outcome");
        foreach (var x in VrUntil(() => s.Editable && !s.KeepBusy, 1800, "kept inside viewer")) yield return x;
        VwCheck(!s.KeepOpen && s.State == ReplayViewerState.Playing && ReferenceEquals(v, s.Scene) && v.ViewerClock == t && v.ViewerPaused,
            "successful pad KEEP promotes same viewer in place without seeking or unpausing");
        ReplayViewerSession.ViewerKeepDelayForTest = 0;
        var kept = a.List().Single();
        VwCheck(kept.Retained && kept.Meta.Name == "A" && a.Open(sub.Id).Sha256() == hash, "pad name and complete source persist unchanged under original lap id");
        foreach (var x in VrTap(Key.I)) yield return x;
        v.ViewerSeek(8f); foreach (var x in VrTap(Key.O)) yield return x;
        foreach (var x in VrTapCtrl(Key.S)) yield return x;
        foreach (var x in VrUntil(() => !s.SaveBusy, 1800, "edits saved after KEEP")) yield return x;
        var edits = a.LoadEdits(sub.Id).Result;
        VwCheck(edits.Edits?.In == 5f && edits.Edits.Out == 8f && !s.Dirty, "newly kept viewer can save edits without reopening");
        foreach (var x in VrTap(Key.P)) yield return x;
        VwCheck(s.Dirty, "unsaved pin before rename");
        foreach (var x in VrTap(Key.V)) yield return x;
        int rename = Array.FindIndex(s.EditRowTexts, r => r.StartsWith("RENAME RECORDING"));
        VwCheck(rename >= 0, "kept viewer offers RENAME RECORDING");
        foreach (var x in ToExportRow(s, rename)) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        foreach (var x in VrTap(Key.Backspace)) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        foreach (var x in VrUntil(() => !s.KeepOpen && !s.KeepBusy, 1800, "renamed")) yield return x;
        VwCheck(a.List().Single().Meta.Name.StartsWith("ILYICH") && a.Open(sub.Id).Sha256() == hash && a.LoadEdits(sub.Id).Result.Edits?.In == 5f,
            "empty rename uses default name and preserves source plus saved edits");
        VwCheck(s.Dirty && s.OverlayView.Pinned && v.ViewerEdits.In == 5f && v.ViewerEdits.Out == 8f,
            "rename preserves unsaved pin and current working trim");
        foreach (var x in VrTapCtrl(Key.S)) yield return x;
        foreach (var x in VrUntil(() => !s.SaveBusy, 1800, "pin saved")) yield return x;
        v.ViewerSeek(6f); v.ViewerSetPaused(false);
        foreach (var x in VrTap(Key.V)) yield return x;
        foreach (var x in ToExportRow(s, rename)) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        float playingClock = v.ViewerClock;
        foreach (var x in VrTap(Key.Right, 30)) yield return x;
        VwCheck(v.ViewerPaused && v.ViewerClock == playingClock, "opening name card freezes a playing replay");
        foreach (var x in VrTap(Key.Escape)) yield return x;
        VwCheck(!v.ViewerPaused && s.State == ReplayViewerState.Playing, "cancel restores the prior playing state");
        v.ViewerSetPaused(true);
        foreach (var x in VrShot("viewer-kept")) yield return x;
        s.Close(); foreach (var x in VrUntil(() => s.State == ReplayViewerState.Closed, 1800, "viewer closed")) yield return x;
        var fresh = new LapArchive(a.List().Single().Dir is string dir ? Path.GetDirectoryName(dir) : "");
        VwCheck(fresh.List().Single().Retained && fresh.Open(sub.Id).Sha256() == hash, "kept lap opens from a fresh archive instance");
        VwCheck(original.Sha256() == originalHash, "shared original fixture unchanged");
    }
}
