using System;
using System.Collections;
using System.IO;
using System.Linq;
using CCCPRacing.Replay;
using Godot;

namespace CCCPRacing.Race;

public partial class RaceScene
{
    IEnumerable VwRecordingDelete()
    {
        foreach (var x in VwWaitReady()) yield return x;
        var archive = LapArchiveBox;
        // Never run destructive diagnostic steps against an existing player archive.
        if (VlArg("--data-dir") == null || archive.List().Count != 0)
        { VwCheck(false, "delete UI requires an explicitly isolated empty test profile"); yield break; }
        var fixture = LapArchive.For(Path.Combine(VlArg("--vw-fx") ?? "/tmp/cccp-lc-fx", "ilyich"));
        var info = fixture.List().First(i => i.Openable && i.Retained);
        var source = fixture.Open(info.Id); var original = source.GhostBytes.ToArray();
        string keptDir = Path.Combine(LapArchiveDir, "lap-delete-ui-kept"); Directory.CreateDirectory(keptDir);
        foreach (var file in Directory.GetFiles(info.Dir)) File.Copy(file, Path.Combine(keptDir, Path.GetFileName(file)));
        File.WriteAllText(Path.Combine(keptDir, "meta.json"), (source.Meta with { Id = "lap-delete-ui-kept", Retained = true }).ToJson());
        archive.Refresh(); archive.Flush(TimeSpan.FromSeconds(15));
        var recent = archive.Submit(new LapCapture { GhostBytes = source.GhostBytes.ToArray(), GhostFingerprint = source.Meta.GhostFingerprint,
            TrackId = "ilyich", Mode = "rally", Set = 0, LapSeconds = source.Meta.LapSeconds,
            Profile = _file.ToString(), TrackSignature = source.Meta.TrackSignature });
        var neighbour = archive.Submit(new LapCapture { GhostBytes = source.GhostBytes.ToArray(), GhostFingerprint = source.Meta.GhostFingerprint,
            TrackId = "ilyich", Mode = "rally", Set = 0, LapSeconds = source.Meta.LapSeconds,
            Profile = _file.ToString(), TrackSignature = source.Meta.TrackSignature });
        archive.Flush(TimeSpan.FromSeconds(15)); _lapArchiveUsed = archive;
        EnterRecordings();
        foreach (var x in VrUntil(() => _hud.ReplayList.Rows.Length == 3, 600, "three recordings")) yield return x;
        _hud.ReplayList.Selected = _replayInfos.FindIndex(i => i.Id == "lap-delete-ui-kept");
        foreach (var x in VrTap(Key.Delete)) yield return x;
        VwCheck(_replayDeleteId == "lap-delete-ui-kept" && !_replayDeleteConfirm, "keyboard DELETE opens a stable target, default CANCEL");
        foreach (var x in VrShot("delete-kept-confirm")) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 5; i++) yield return null;
        VwCheck(_replayDeleteId == null && Directory.Exists(keptDir) && _replayOpenTask == null && _replayViewer == null,
            "default Enter cancels without deleting or leaking WATCH");
        foreach (var x in VrPad(JoyButton.X)) yield return x;
        foreach (var x in VrPad(JoyButton.DpadDown)) yield return x;
        foreach (var x in VrPad(JoyButton.B)) yield return x;
        VwCheck(_replayDeleteId == null && Directory.Exists(keptDir), "pad X opens and B cancels the delete card");

        archive.WorkerHook = s => { if (s.StartsWith("delete:")) throw new IOException("test delete refusal"); };
        foreach (var x in VrTap(Key.Delete)) yield return x;
        foreach (var x in VrTap(Key.Down)) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        foreach (var x in VrUntil(() => _replayDeleteTask == null, 600, "delete failure")) yield return x;
        VwCheck(_hud.ReplayList.Notice.StartsWith("NOT DELETED:") && Directory.Exists(keptDir) && archive.Open("lap-delete-ui-kept") != null,
            "a delete failure remains visible and the kept recording stays usable");
        archive.WorkerHook = null;
        foreach (var x in VrUntil(() => _replayListTask == null, 600, "refresh after refusal")) yield return x;
        _hud.ReplayList.Selected = _replayInfos.FindIndex(i => i.Id == "lap-delete-ui-kept");
        foreach (var x in VrPad(JoyButton.X)) yield return x;
        // A refreshed/reordered list must not change what the confirmation refers to.
        _hud.ReplayList.Selected = _replayInfos.FindIndex(i => i.Id == recent.Id);
        foreach (var x in VrPad(JoyButton.DpadDown)) yield return x;
        foreach (var x in VrPad(JoyButton.A)) yield return x;
        foreach (var x in VrUntil(() => _replayDeleteTask == null && _hud.ReplayList.Rows.Length == 2, 600, "kept deleted")) yield return x;
        VwCheck(!Directory.Exists(keptDir) && archive.List().Count == 2 && archive.List().Any(i => i.Id == recent.Id),
            "pad confirmation deletes the original target only, despite selection changes");
        CloseRecordings();
        _state = State.Paused; _run = RunMode.Training;
        OpenReplayList();
        foreach (var x in VrUntil(() => _hud.ReplayList.Rows.Length == 2, 600, "Party Meeting list")) yield return x;
        _hud.ReplayList.Selected = _replayInfos.FindIndex(i => i.Id == recent.Id);
        foreach (var x in VrPad(JoyButton.X)) yield return x;
        VwCheck(_replayDeleteId == recent.Id && !_replayDeleteConfirm, "Party Meeting pad X also opens default CANCEL");
        foreach (var x in VrPad(JoyButton.B)) yield return x;
        foreach (var x in VrTap(Key.Delete)) yield return x;
        foreach (var x in VrTap(Key.Down)) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        foreach (var x in VrUntil(() => _replayDeleteTask == null && _hud.ReplayList.Rows.Length == 1, 600, "session deleted")) yield return x;
        VwCheck(archive.List().Single().Id == neighbour.Id && _state == State.Paused && _replayOpenTask == null && _replayViewer == null,
            "THIS SESSION deletion does not leak WATCH into the remaining valid row or resume the race");
        foreach (var x in VrTap(Key.Delete)) yield return x;
        foreach (var x in VrTap(Key.Down)) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        foreach (var x in VrUntil(() => _replayDeleteTask == null && _hud.ReplayList.Rows.Length == 0, 600, "last session deleted")) yield return x;
        VwCheck(archive.List().Count == 0 && _hud.ReplayList.Selected == 0, "last deletion leaves a valid empty list");
        foreach (var x in VrShot("delete-empty-list")) yield return x;
        VwCheck(fixture.Open(info.Id).GhostBytes.Span.SequenceEqual(original), "the original shared recording is unchanged");
    }
}
