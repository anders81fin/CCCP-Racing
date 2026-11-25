using System;
using System.Collections;
using System.IO;
using System.Linq;
using CCCPRacing.Track;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

// Windowed scenario "custom-tracks" (--viewer-windowed-only=customtracks): SINGLE PLAYER > CUSTOM TRACKS on a fresh temporary profile. The row and its hint, the empty list and its texts (the card's keys, Esc back), then
// three tracks written into the profile's tracks folder: one that does not validate (Enter says why and starts nothing), one that validates but does not build (the world is rebuilt on the first track and the list
// comes back on that track with the reason), and a good one (Enter starts free laps on it at once: DIESEL, daylight, no fuel or time card; EXIT RACE leaves for the list on the track just driven).
public partial class RaceScene
{
    /// <summary>The custom track id the test makes fail to build (static: it survives the scene reload).</summary>
    static string VwFailBuildOf;

    partial void CustomBuildHook(string id)
    {
        if (id == VwFailBuildOf || Array.IndexOf(OS.GetCmdlineUserArgs(), "--vw-fail-build=" + id) >= 0) throw new InvalidOperationException("test: the road could not be built");      // (--vw-fail-build=ID: a child process started on a failing track)
    }

    IEnumerable VwCustomTracks()
    {
        foreach (var x in VwWaitReady()) yield return x;
        for (int i = 0; i < 600 && !(_state == State.Title && _card == TitleCard.None && !_showLog && !_askName); i++) yield return null;
        for (int i = 0; i < 90; i++) yield return null;
        // the scenario writes tracks, a board and a signature into the profile: it runs only on the harness's own fresh temporary one (VwChild: /tmp/cccp-win-N.xdg); on any other it FAILS, it does not pass silently
        bool temporary = OS.GetUserDataDir().StartsWith("/tmp/") && OS.GetUserDataDir().Contains("/cccp-win-");
        VwCheck(temporary, $"the scenario runs on the harness's temporary profile, the only one it may write into ({OS.GetUserDataDir()})");
        if (!temporary) yield break;
        VwCheck(!Directory.Exists(ProjectSettings.GlobalizePath(TrackSource.CustomRoot)) || Directory.GetDirectories(ProjectSettings.GlobalizePath(TrackSource.CustomRoot)).Length == 0, "the profile has no custom tracks yet");
        _padMode = false;
        // ---- the row
        for (int i = 0; i < 6 && _menuParent != null; i++) foreach (var x in VrTap(Key.Escape)) yield return x;
        for (int i = 0; i < 6 && _menu != MenuItem.Single; i++) foreach (var x in VrTap(Key.Left)) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        for (int i = 0; i < 6 && _menu != MenuItem.Custom; i++) foreach (var x in VrTap(Key.Right)) yield return x;
        for (int i = 0; i < 8; i++) yield return null;
        VwCheck(_menu == MenuItem.Custom && _menuParent == MenuItem.Single && _hud.MenuLabels.SequenceEqual(new[] { "DRIVING SCHOOL", "SOCIALIST RALLY", "STAKHANOV LAPS", "CUSTOM TRACKS" }), $"SINGLE PLAYER has a fourth row, CUSTOM TRACKS ({string.Join(" | ", _hud.MenuLabels)})");
        VwCheck(_hud.MenuHint is var (hintTitle, hintLines) && hintTitle == "CUSTOM TRACKS" && hintLines.All(l => l.All(PixelFont.CanDraw) && l.Length <= 46), "its hint says what it is, in characters the font draws");
        foreach (var x in VrShot("customtracks-row")) yield return x;
        // ---- the empty list
        foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 8; i++) yield return null;
        VwCheck(_card == TitleCard.Custom && _hud.CardTitle == "CHOOSE TRACK" && _hud.CustomRows is { Length: 0 }, $"Enter opens the list, empty ({_card}, rows {_hud.CustomRows?.Length})");
        VwCheck(_hud.CustomEmpty.Length == 4 && _hud.CustomEmpty.All(l => l.Length <= 42 && l.All(PixelFont.CanDraw)), "the empty list says where the tracks go, in lines of at most 42 drawable characters");
        for (int i = 0; i < 4; i++) yield return null;
        VwCheck(_hud.CustomKeysDrawn == _hud.CustomKeysLine(true) && PixelFont.Measure(_hud.CustomKeysDrawn) <= 408, $"the card's keys line is drawn and fits ('{_hud.CustomKeysDrawn}')");
        foreach (var x in VrShot("customtracks-empty")) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 4; i++) yield return null;
        VwCheck(_card == TitleCard.Custom && VwNothingStarted(), "Enter on the empty list does nothing");
        foreach (var x in VrTap(Key.Escape)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_card == TitleCard.None && _menu == MenuItem.Custom && _menuParent == MenuItem.Single, "Esc: back to the CUSTOM TRACKS row");
        // ---- three tracks in the folder
        string root = ProjectSettings.GlobalizePath(TrackSource.CustomRoot);
        string oval = File.ReadAllText(ProjectSettings.GlobalizePath("res://docs/public/examples/oval/track.json"));
        foreach (var (folder, text) in new[] { ("broken-one", "{ nope"), ("fails-build", oval.Replace("OVAL EXAMPLE", "FAILS BUILD")), ("oval-test", oval.Replace("OVAL EXAMPLE", "OVAL TEST")) })
        { Directory.CreateDirectory(Path.Combine(root, folder)); File.WriteAllText(Path.Combine(root, folder, "track.json"), text); }
        foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 8; i++) yield return null;
        VwCheck(_card == TitleCard.Custom && _hud.CustomRows.SequenceEqual(new[] { "BROKEN-ONE", "FAILS BUILD", "OVAL TEST" }) && _hud.CustomBad.SequenceEqual(new[] { true, false, false }), $"the list reads the folder again: three tracks, the broken one marked ({string.Join(" | ", _hud.CustomRows ?? Array.Empty<string>())}; bad {string.Join(",", _hud.CustomBad ?? Array.Empty<bool>())})");
        VwCheck(_hud.CardSub != null && _hud.CardSub.StartsWith("ERROR: ") && _hud.CardSub.Length <= 42 && _hud.CardSub.All(PixelFont.CanDraw), $"the broken track's line says why ('{_hud.CardSub}')");
        VwCheck(_hud.CardNote == "KEYS 1-4 SWITCH FUEL WHILE DRIVING" && PixelFont.Measure(_hud.CardNote) <= 408, $"the list says that keys 1-4 switch the fuel while driving ('{_hud.CardNote}')");
        for (int i = 0; i < 4; i++) yield return null;
        VwCheck(_hud.CustomKeysDrawn == _hud.CustomKeysLine(false) && PixelFont.Measure(_hud.CustomKeysDrawn) <= 408, "the keys line of the full list fits");
        foreach (var x in VrShot("customtracks-list")) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_card == TitleCard.Custom && VwNothingStarted() && _hud.MenuNoteNow.text == "CANNOT START THIS TRACK", $"Enter on the broken track starts nothing and says so ('{_hud.MenuNoteNow.text}')");
        foreach (var x in VrTap(Key.Down)) yield return x;
        for (int i = 0; i < 4; i++) yield return null;
        VwCheck(_customSel == 1 && _hud.CustomSelection == 1 && _hud.CardSub == "NO LAP YET - FREE LAPS, NO MEDALS", $"Down: the next track, no lap on it yet ('{_hud.CardSub}')");
        foreach (var x in VrTap(Key.Up)) yield return x;
        foreach (var x in VrPad(JoyButton.DpadDown)) yield return x;
        for (int i = 0; i < 4; i++) yield return null;
        VwCheck(_customSel == 1, "the d-pad moves the list too");
        // a lap on the board of the good track (a human's lap would be put there by the name entry): the list then shows it
        {
            // (with the track's signature on file, as the first recorded lap leaves it: a board without one is taken for an old shape and archived)
            string sig = TrackSignature.Compute(TrackBuilder.Build(TrackSource.Load("custom-oval-test")));
            new RecordStore(_records.Dir, "custom-oval-test", sig).WriteSignature();
            var lb = Leaderboard.Load(new RecordStore(_records.Dir, "custom-oval-test", null).BoardForSet(1), capacity: Leaderboard.StakhanovSize, fuelPartitions: true);
            lb.Insert("TEST DRIVER", 41.23f, power: Training.PercentForKey((int)FuelGrade.Diesel + 1));
            lb.Insert("OTHER FUEL", 39.5f, power: Training.PercentForKey((int)FuelGrade.Petrol + 1));      // (another fuel's lap: not the DIESEL best)
            lb.Save();
        }
        // ---- a track that validates but does not build: the world is rebuilt on the first track and the list comes back with the reason
        VwFailBuildOf = "custom-fails-build";
        var tree = GetTree(); var old = this; var drv = _vwDrv;
        foreach (var x in VrTap(Key.Enter)) yield return x;      // (from here on this instance is freed by the reloads: statics, locals and the fresh scene only)
        RaceScene back = null;
        for (int i = 0; i < 60 * 90 && back == null; i++)
        {
            yield return null;
            var f = VwFindLive(tree.Root);
            if (f != null && !ReferenceEquals(f, old) && f._card == TitleCard.Custom) { back = f; back._vwDrv = drv; }
        }
        VwFailBuildOf = null;
        VwCheck(back != null, "after the failed build the list is up again on a rebuilt world");
        if (back == null) yield break;
        foreach (var x in back.VwCustomTracksAfterFailure(tree, drv)) yield return x;
    }

    /// <summary>The scene that came back after a custom track failed to build (its own viewport, its own fields).</summary>
    IEnumerable VwCustomTracksAfterFailure(SceneTree tree, ViewerWindowedDriver drv)
    {
        for (int i = 0; i < 10; i++) yield return null;
        VwCheck(_trackId == "ilyich" && _card == TitleCard.Custom && _customSel == 1 && _menuParent == MenuItem.Single && _menu == MenuItem.Custom, $"the world is the first track's, the list stands on the track that failed ({_trackId}, {_card}, row {_customSel})");
        VwCheck(_hud.MenuNoteNow.text == "COULD NOT LOAD THIS TRACK" && _hud.MenuNoteNow.sub.Contains("COULD NOT BE BUILT") && _hud.MenuNoteNow.sub.Length <= 42, $"a red note says it could not be loaded, and why ('{_hud.MenuNoteNow.text}' / '{_hud.MenuNoteNow.sub}')");
        foreach (var x in VrShot("customtracks-load-error")) yield return x;
        // ---- the good track starts free laps at once
        foreach (var x in VrTap(Key.Down)) yield return x;
        for (int i = 0; i < 4; i++) yield return null;
        VwCheck(_customSel == 2 && _hud.CustomRows[2] == "OVAL TEST", "the good track is selected");
        VwCheck(_hud.CustomTimes[2] == "0:41.23" && _hud.CustomTimes[1] == "" && _hud.CardSub == "BEST LAP 0:41.23 - FREE LAPS, NO MEDALS", $"the list shows the best DIESEL lap of the good track, not another fuel's ('{_hud.CustomTimes[2]}', '{_hud.CardSub}')");
        foreach (var x in VrShot("customtracks-best")) yield return x;
        var old = this;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_card == TitleCard.None, $"no fuel card and no time card: the run goes on at once ({_card})");
        RaceScene fresh = null;
        RaceScene Who() { var f = VwFindLive(tree.Root); if (f != null && !ReferenceEquals(f, old) && fresh == null) { fresh = f; fresh._vwDrv = drv; } return fresh; }
        bool racing = false;
        foreach (var x in VwStakhanovGo(Who, drv, r => racing = r)) yield return x;
        VwCheck(fresh != null && racing, $"the scene was rebuilt on the custom track and the laps are racing (fresh {fresh != null}, racing {racing})");
        if (fresh == null) yield break;
        foreach (var x in fresh.VwCustomTracksRacing()) yield return x;
    }

    IEnumerable VwCustomTracksRacing()
    {
        for (int i = 0; i < 30; i++) yield return null;
        VwCheck(_trackId == "custom-oval-test" && _run == RunMode.Training && TrackName(_trackId) == "OVAL TEST", $"free laps on the custom track, with its own name for the boards ({_trackId}, {_run}, '{TrackName(_trackId)}')");
        VwCheck(VwEngineIs(FuelGrade.Diesel) && !_track.Id.StartsWith("ilyich"), $"DIESEL, as the list starts them ({VwEngineNow()})");
        VwCheck(_dayNight.LapOffset == DayNight.FirstLap(DayNight.Phase.Day) - 1, $"in daylight (offset {_dayNight.LapOffset})");
        foreach (var x in VrShot("customtracks-racing")) yield return x;
        // ---- keys 1-4 and the d-pad switch the fuel as in the Stakhanov laps, with the fuels the rally has opened (the medals of the campaign, not of this track)
        VwCheck(VwEngineIs(FuelGrade.Diesel), $"the run starts on DIESEL ({VwEngineNow()})");
        foreach (var x in VrTap(Key.Key2)) yield return x;
        for (int i = 0; i < 10; i++) yield return null;
        VwCheck(VwEngineIs(FuelGrade.Diesel), $"key 2 with PETROL still closed: refused, DIESEL stays ({VwEngineNow()})");
        foreach (var t in FuelTracks(FuelGrade.Petrol)) _campaign.Award(t, NormPlan.Medal.Silver, FuelGrade.Diesel);      // (silver on the three first tracks in the rally on DIESEL opens PETROL; the same on PETROL opens JET FUEL)
        foreach (var t in FuelTracks(FuelGrade.Rocket)) _campaign.Award(t, NormPlan.Medal.Silver, FuelGrade.Petrol);
        foreach (FuelGrade g in new[] { FuelGrade.Diesel, FuelGrade.Petrol, FuelGrade.Rocket, FuelGrade.Sputnik }) { _campaign.PresentFuel(g); _campaign.ShowFuelUnlock(g); }      // (the opening ceremonies of the new fuels and tracks are seen: EXIT RACE then goes straight to the list; a ceremony comes first otherwise)
        foreach (var t in Tracks) { _campaign.PresentLaps(t.id, false); _campaign.PresentLaps(t.id, true); }
        VwCheck(!FuelLocked(FuelGrade.Petrol) && !FuelLocked(FuelGrade.Rocket) && FuelLocked(FuelGrade.Sputnik), "the rally's medals open PETROL and JET FUEL, SPUTNIK stays closed");
        foreach (var x in VrTap(Key.Key2)) yield return x;
        for (int i = 0; i < 10; i++) yield return null;
        VwCheck(_state == State.Racing && VwEngineIs(FuelGrade.Petrol) && _trackId == "custom-oval-test", $"key 2 on the custom track: PETROL's engine ({VwEngineNow()})");
        foreach (var x in VrTap(Key.Key3)) yield return x;
        for (int i = 0; i < 10; i++) yield return null;
        VwCheck(VwEngineIs(FuelGrade.Rocket), $"key 3: JET FUEL's engine ({VwEngineNow()})");
        foreach (var x in VrTap(Key.Key4)) yield return x;
        for (int i = 0; i < 10; i++) yield return null;
        VwCheck(VwEngineIs(FuelGrade.Rocket), $"key 4: the closed SPUTNIK is refused, JET FUEL stays ({VwEngineNow()})");
        foreach (var x in VrPad(JoyButton.DpadDown)) yield return x;
        for (int i = 0; i < 10; i++) yield return null;
        VwCheck(VwEngineIs(FuelGrade.Petrol), $"pad D-pad down: one step down to PETROL ({VwEngineNow()})");
        foreach (var x in VrTap(Key.Key1)) yield return x;
        for (int i = 0; i < 10; i++) yield return null;
        VwCheck(VwEngineIs(FuelGrade.Diesel), $"key 1: back on DIESEL ({VwEngineNow()})");
        // ---- F4 while driving: the Stakhanov board of this track, under the track's own name, with the lap that is on file
        foreach (var x in VrTap(Key.F4)) yield return x;
        for (int i = 0; i < 12; i++) yield return null;
        VwCheck(_hud.StakhanovBoardOpen && _hud.StakhanovBoardTrack == "OVAL TEST" && _boards[1].Entries.Count == 2, $"F4: the Stakhanov board opens for OVAL TEST with its laps ('{_hud.StakhanovBoardTrack}', {_boards[1].Entries.Count} laps)");
        foreach (var x in VrShot("customtracks-board")) yield return x;
        foreach (var x in VrTap(Key.Escape)) yield return x;      // (the board closes, the Party Meeting is up: the F-key contract)
        for (int i = 0; i < 12; i++) yield return null;
        VwCheck(!_hud.StakhanovBoardOpen && _state == State.Paused, "Esc closes the board: the Party Meeting is up");
        // ---- EXIT RACE leaves for the list, on the track just driven
        foreach (var x in VrToRow(PauseRowIndex(PauseRowId.Exit))) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 60; i++) yield return null;
        VwCheck(_state == State.Title && _card == TitleCard.Custom && _hud.CustomRows[_customSel] == "OVAL TEST" && _menu == MenuItem.Custom && _menuParent == MenuItem.Single, $"EXIT RACE leaves for the CUSTOM TRACKS list on the track just driven ({_state}, {_card}, row {_customSel})");
        foreach (var x in VrShot("customtracks-exit")) yield return x;
        foreach (var x in VrTap(Key.Escape)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_card == TitleCard.None && _menu == MenuItem.Custom, "Esc: the CUSTOM TRACKS row");
    }
}
