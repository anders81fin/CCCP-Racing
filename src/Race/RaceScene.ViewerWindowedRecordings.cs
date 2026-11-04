using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using CCCPRacing.Replay;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

// Windowed scenarios "recordings-keep" and "recordings-restart" (--viewer-windowed-only=recordings): two game children that share one data dir (the parent hashes the kept pairs between them,
// and puts a damaged lap folder beside them). Child 1 drives Stakhanov laps, goes back to the title, opens PARTY OFFICE > RECORDINGS by keys, KEEPs with a name, cancels another name, and
// KEEPs again and quits at once. Child 2 (a fresh process, the title) lists what was kept, compares the SHA-256 of the pairs, watches one from the Office, checks the title came back
// exactly, then the pad (D-pad, A, Y, the name wheel, B), a failing KEEP and a cancel. The PARTY MEETING's own list and its partial-lap resume are the replay-ui scenario's.
public partial class RaceScene
{
    IEnumerable VrType(string text)
    {
        _vwDrv.SetControl(true);
        foreach (char c in text)
        {
            var k = char.IsLetter(c) ? (Key)char.ToUpperInvariant(c) : c == ' ' ? Key.Space : (Key)c;
            Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Unicode = c, Pressed = true, Device = 0 }); yield return null;
            Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Unicode = c, Pressed = false, Device = 0 }); yield return null;
        }
        _vwDrv.SetControl(false);
    }

    /// <summary>Whole-file SHA-256 of every file of the kept laps (relative to the lap folder).</summary>
    Dictionary<string, string> VrLapHashes()
    {
        var d = new Dictionary<string, string>();
        string dir = LapArchiveDir.TrimEnd('/');
        if (!Directory.Exists(dir)) return d;
        foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            if (Path.GetFileName(f) is "ghost.bin" or "tape.bin") d[Path.GetRelativePath(dir, f)] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f)));
        return d;
    }

    IEnumerable VrToOfficeRecordings()
    {
        VwCheck(_state == State.Title, "the title is up");
        for (int i = 0; i < 6 && _menuParent != null; i++) foreach (var x in VrTap(Key.Escape)) yield return x;
        for (int i = 0; i < 6 && _menu != MenuItem.Office; i++) foreach (var x in VrTap(Key.Right)) yield return x;
        VwCheck(_menu == MenuItem.Office, "the cursor is on PARTY OFFICE");
        foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        for (int i = 0; i < 6 && _menu != MenuItem.Recordings; i++) foreach (var x in VrTap(Key.Right)) yield return x;
        VwCheck(_menuParent == MenuItem.Office && _menu == MenuItem.Recordings && _hud.MenuLabels.Contains("RECORDINGS"), "PARTY OFFICE has RECORDINGS");
        foreach (var x in VrTap(Key.Enter)) yield return x;
        foreach (var x in VrUntil(() => _recordingsOpen && _hud.ReplayListOpen && !_replayLooking, 60 * 10, "the RECORDINGS list is up")) yield return x;
    }

    IEnumerable VwRecordingsKeep()
    {
        foreach (var x in VwWaitReady()) yield return x;
        foreach (var x in VrUntil(() => _state == State.Racing && _lapClock > 3f, 60 * 40, "the race is running")) yield return x;
        VwCheck(_run == RunMode.Training, "Stakhanov laps");
        foreach (float t in new[] { 65.5f, 71.25f, 60.75f })
        {
            for (int i = 0; i < 150; i++) yield return null;      // (the recorder needs some of the lap)
            _timer.CompleteLapForTest(t);
        }
        for (int i = 0; i < 30; i++) yield return null;
        LapArchiveBox.Flush(TimeSpan.FromSeconds(20));
        var infos = LapArchiveBox.List();
        VwCheck(infos.Count == 3 && infos.All(i => !i.Retained), $"three laps are in the archive this session, none kept ({infos.Count})");
        // out of the race by the Party Meeting's own rows
        foreach (var x in VrTap(Key.Escape)) yield return x;
        for (int i = 0; i < 12; i++) yield return null;
        for (int i = 0; i < 12 && _hud.PauseSelection != PauseRowIndex(PauseRowId.Exit); i++) foreach (var x in VrTap(Key.Down)) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        foreach (var x in VrUntil(() => _state == State.Title, 60 * 10, "back at the title")) yield return x;
        for (int i = 0; i < 60; i++) yield return null;
        foreach (var x in VrToOfficeRecordings()) yield return x;
        var l = _hud.ReplayList;
        VwCheck(l.Heading == "RECORDINGS" && l.Rows.Length == 3 && l.Rows.All(r => r.Detail.Contains("THIS SESSION") && !r.Detail.Contains("KEPT")), $"RECORDINGS lists the three laps as THIS SESSION ({l.Rows.Length})");
        foreach (var x in VrShot("rec-1-session")) yield return x;
        // KEEP with a typed name
        foreach (var x in VrTap(Key.K)) yield return x;
        VwCheck(_keepNameOpen, "K opens the name field");
        foreach (var x in VrShot("rec-2-name-field")) yield return x;
        foreach (var x in VrType("my best 1")) yield return x;
        VwCheck(_keepName == "MY BEST 1", $"typed text is cleaned to '{_keepName}'");
        foreach (var x in VrTap(Key.Enter)) yield return x;
        VwCheck(!_keepNameOpen && _keepTask != null || _hud.ReplayList.Notice.StartsWith("KEPT") || _hud.ReplayList.Notice.StartsWith("SAVING"), "Enter starts the save");
        foreach (var x in VrUntil(() => _hud.ReplayList.Notice.StartsWith("KEPT"), 60 * 20, "the notice says KEPT")) yield return x;
        for (int i = 0; i < 30; i++) yield return null;
        VwCheck(_hud.ReplayList.Rows.Count(r => r.Detail.Contains("KEPT") && r.Detail.Contains("MY BEST 1")) == 1, "the kept row shows KEPT and its name");
        foreach (var x in VrShot("rec-3-kept")) yield return x;
        // a cancelled name saves nothing
        var before = VrLapHashes();
        _hud.ReplayList.Selected = 1;
        foreach (var x in VrTap(Key.K)) yield return x;
        foreach (var x in VrType("never")) yield return x;
        foreach (var x in VrTap(Key.Escape)) yield return x;
        for (int i = 0; i < 20; i++) yield return null;
        VwCheck(!_keepNameOpen && _keepTask == null && _recordingsOpen && _hud.ReplayList.Notice.StartsWith("KEEP CANCELLED"), "Esc cancels the name: nothing saved, still in RECORDINGS");
        VwCheck(VrLapHashes().Count == before.Count, "a cancelled KEEP wrote no files");
        // an empty name gets the default; a name that is already used is allowed
        _hud.ReplayList.Selected = 1;
        foreach (var x in VrTap(Key.K)) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        foreach (var x in VrUntil(() => _hud.ReplayList.Notice.StartsWith("KEPT"), 60 * 20, "an empty name is kept under the default name")) yield return x;
        VwCheck(_hud.ReplayList.Notice.Contains("ILYICH STAK "), $"default name: {_hud.ReplayList.Notice}");
        _hud.ReplayList.Selected = 2;
        foreach (var x in VrTap(Key.K)) yield return x;
        foreach (var x in VrType("my best 1")) yield return x;
        // the last KEEP and the quit right behind it (the scene's exit waits for the worker)
        // Enter and the quit in the same frame: the save is in the worker's hands when the scene's exit comes
        VwCheck(_keepNameOpen && _keepName == "MY BEST 1", "the third name (the same as the first) is typed");
        CommitKeep();
        VwCheck(_keepTask != null && !_keepTask.IsCompleted || _keepTask != null, "the third KEEP (a duplicate name) is handed to the worker");
        VwLog("quit-right-after-keep", ("pending", _keepTask != null), ("completed", _keepTask?.IsCompleted));
        GetTree().Quit();
    }

    IEnumerable VwRecordingsRestart()
    {
        foreach (var x in VwWaitReady()) yield return x;
        for (int i = 0; i < 600 && !(_state == State.Title && _card == TitleCard.None && !_showLog && !_askName); i++) yield return null;
        for (int i = 0; i < 60; i++) yield return null;
        string shared = VlArg("--vw-hashes");
        var prior = new Dictionary<string, string>();
        if (shared != null && File.Exists(shared)) foreach (var line in File.ReadAllLines(shared)) { var p = line.Split('\t'); if (p.Length == 2) prior[p[0]] = p[1]; }
        var filesBefore = VrFiles();
        foreach (var x in VrToOfficeRecordings()) yield return x;
        var rows = _hud.ReplayList.Rows;
        VwLog("restart-rows", ("rows", string.Join(" | ", rows.Select(r => r.Title + " / " + r.Detail + " / " + r.Flag))));
        VwCheck(rows.Count(r => r.Detail.Contains("KEPT")) >= 3 && rows.All(r => !r.Detail.Contains("THIS SESSION")), $"after a restart the kept laps are listed as KEPT and no session laps ({rows.Length} rows)");
        VwCheck(rows.Any(r => r.Detail.Contains("MY BEST 1")), "the kept names are there");
        VwCheck(rows.Any(r => r.Flag == "DAMAGED" && !r.Selectable), "the damaged folder shows DAMAGED and cannot be opened");
        var now = VrLapHashes();
        bool same = prior.Count > 0 && prior.All(kv => now.GetValueOrDefault(kv.Key) == kv.Value);
        VwCheck(same, $"the kept pairs' SHA-256 is unchanged since the first process ({prior.Count} files compared)");
        foreach (var x in VrShot("rec-4-restart-list")) yield return x;
        // watch one from the Office; the title must come back exactly
        int sel = Array.FindIndex(rows, r => r.Selectable);
        _hud.ReplayList.Selected = Math.Max(0, sel);
        var snap = (_menuParent, _menu, _rallyPhase, _lapsPhase, _rallyFuel, _trackId, _lastPlayed, _hud.ReplayList.Selected, _state);
        bool sfxBefore = Audio.AudioMix.SfxMuted;
        var audio = new List<(Node, bool)>(); foreach (var n in FindChildren("*", "", true, false)) if (n is AudioStreamPlayer a) audio.Add((a, a.StreamPaused)); else if (n is AudioStreamPlayer3D a3) audio.Add((a3, a3.StreamPaused));
        foreach (var x in VrTap(Key.Enter)) yield return x;
        foreach (var x in VrUntil(() => ReplayViewer?.State == ReplayViewerState.Playing, 60 * 120, "the lap opens and plays")) yield return x;
        for (int i = 0; i < 60; i++) yield return null;
        VwCheck(_state == State.Title && ReplayViewer != null, "the viewer runs over the running title (no drive)");
        VwCheck(sfxBefore && !Audio.AudioMix.SfxMuted, $"the title mutes the sound-effect bus; the viewer opened from it has the bus OPEN (was muted {sfxBefore}, now muted {Audio.AudioMix.SfxMuted})");
        foreach (var x in VrShot("rec-5-viewer")) yield return x;
        foreach (var x in VrTap(Key.Escape)) yield return x;
        foreach (var x in VrUntil(() => ReplayViewer == null, 60 * 30, "the viewer closed")) yield return x;
        for (int i = 0; i < 20; i++) yield return null;
        var snap2 = (_menuParent, _menu, _rallyPhase, _lapsPhase, _rallyFuel, _trackId, _lastPlayed, _hud.ReplayList.Selected, _state);
        VwCheck(snap.Equals(snap2), $"the title came back as it was: {snap2}");
        VwCheck(_recordingsOpen && _hud.ReplayListOpen && _menuParent == MenuItem.Office && _menu == MenuItem.Recordings && _card == TitleCard.None, "back in RECORDINGS under PARTY OFFICE, no card");
        VwCheck(_state == State.Title && _countdown <= 0 && _hud.Mode == HudMode.Title, "no countdown or race started");
        VwCheck(Audio.AudioMix.SfxMuted == sfxBefore, "the title's sound-effect mute is given back after the viewer");
        bool audioSame = true; foreach (var (n, was) in audio) if (IsInstanceValid(n)) audioSame &= (n is AudioStreamPlayer a ? a.StreamPaused : ((AudioStreamPlayer3D)n).StreamPaused) == was;
        VwCheck(audioSame, $"every audio player is paused exactly as before ({audio.Count} players)");
        VwCheck(VrLapHashes().Where(kv => prior.ContainsKey(kv.Key)).All(kv => prior[kv.Key] == kv.Value), "watching changed no kept file");
        foreach (var x in VrShot("rec-6-back-from-viewer")) yield return x;
        // the pad: D-pad moves, Y opens the name wheel, Up/Down turn the letter, A adds one, Start keeps (a rename of this kept lap), B cancels
        _padMode = true;
        foreach (var x in VrPad(JoyButton.DpadDown)) yield return x;
        foreach (var x in VrPad(JoyButton.Y)) yield return x;
        VwCheck(_keepNameOpen, "pad Y opens the name field");
        for (int i = 0; i < 30 && _keepName.Length > 0; i++) foreach (var x in VrPad(JoyButton.B)) yield return x;
        VwCheck(_keepNameOpen && _keepName.Length == 0, "pad B deletes the letters");
        foreach (var x in VrPad(JoyButton.DpadUp)) yield return x;
        foreach (var x in VrPad(JoyButton.DpadUp)) yield return x;
        foreach (var x in VrPad(JoyButton.A)) yield return x;
        VwCheck(_keepName.Length == 2, $"the wheel gave '{_keepName}'");
        foreach (var x in VrShot("rec-7-pad-wheel")) yield return x;
        foreach (var x in VrPad(JoyButton.Start)) yield return x;
        foreach (var x in VrUntil(() => _hud.ReplayList.Notice.StartsWith("RENAMED"), 60 * 20, "the pad's rename saved")) yield return x;
        string renamed = _keepTaskName;
        foreach (var x in VrUntil(() => _hud.ReplayList.Rows.Any(r => r.Detail.EndsWith(renamed)), 60 * 10, $"the listing shows the new name {renamed}")) yield return x;
        VwCheck(renamed.Length == 2 && _hud.ReplayList.Rows.Count(r => r.Detail.EndsWith(renamed)) == 1, $"exactly one row carries the new name '{renamed}'");
        foreach (var x in VrPad(JoyButton.B)) yield return x;
        for (int i = 0; i < 12; i++) yield return null;
        VwCheck(!_recordingsOpen && _menuParent == MenuItem.Office && _menu == MenuItem.Recordings && _state == State.Title, "pad B leaves RECORDINGS for PARTY OFFICE, on RECORDINGS");
        // a failing KEEP is visible and leaves the pair usable
        foreach (var x in VrTap(Key.Enter)) yield return x;
        foreach (var x in VrUntil(() => _recordingsOpen && !_replayLooking, 60 * 10, "RECORDINGS again")) yield return x;
        var h0 = VrLapHashes();
        KeepFailForTest = "DISK FULL (TEST)";
        foreach (var x in VrTap(Key.K)) yield return x;
        foreach (var x in VrType("x")) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        foreach (var x in VrUntil(() => _hud.ReplayList.Notice.StartsWith("NOT KEPT"), 60 * 10, "the failure is shown")) yield return x;
        KeepFailForTest = null;
        VwCheck(_hud.ReplayList.NoticeIsError && _hud.ReplayList.Notice.Contains("DISK FULL"), $"the failure says why: {_hud.ReplayList.Notice}");
        foreach (var x in VrShot("rec-8-keep-failed")) yield return x;
        VwCheck(VrLapHashes().All(kv => h0.GetValueOrDefault(kv.Key) == kv.Value), "after the failed KEEP every kept file is unchanged");
        // opening still works after the failure
        foreach (var x in VrTap(Key.Enter)) yield return x;
        foreach (var x in VrUntil(() => ReplayViewer?.State == ReplayViewerState.Playing, 60 * 120, "the lap still opens after the failed KEEP")) yield return x;
        foreach (var x in VrTap(Key.Escape)) yield return x;
        foreach (var x in VrUntil(() => ReplayViewer == null, 60 * 30, "closed")) yield return x;
        var endHashes = VrLapHashes();
        VwCheck(prior.Count >= 6 && prior.All(kv => endHashes.GetValueOrDefault(kv.Key) == kv.Value), $"at the end every kept pair (all {prior.Count} files, the repeated, duplicate-named and renamed ones too) has the SHA-256 it had after the first process, no extra pair files appeared ({endHashes.Count} now)");
        VwCheck(endHashes.Count == prior.Count, "no pair file was added or lost by rename, cancel, failure or watching");
        var filesAfter = VrFiles();
        string fd = string.Join(";", filesBefore.Keys.Union(filesAfter.Keys).Where(k => filesBefore.GetValueOrDefault(k) != filesAfter.GetValueOrDefault(k)).Take(8));
        VwLog("files", ("before", filesBefore.Count), ("after", filesAfter.Count), ("changed", fd));
        VwCheck(fd == "", $"Office, viewer and KEEP wrote no record, profile, campaign or settings file ({filesBefore.Count} files hashed; changed: {fd})");
    }
}
