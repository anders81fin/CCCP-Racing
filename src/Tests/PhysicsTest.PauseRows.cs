using System.Collections.Generic;
using CCCPRacing.Race;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Tests;

// --pause-rows-tests: the Party Meeting's rows are one list of ids (RaceScene.PauseRowList): the lines the Hud draws, its mouse hit test and the actions all read it. For every combination of
// NORMS, the number of this run's laps (0: no replay row, 1: WATCH LAST LAP, 2+: WATCH LAST LAP and BROWSE LAPS), the Stakhanov board and the pad's two label sets the order is the owner's order of 2026-11-19
// (CONTINUE first and the default, then the replay rows, LEADERBOARDS / NORMS AND MEDALS, DRIVING SCHOOL, SETTINGS, then RESTART RACE and EXIT RACE) and every line carries the label of its id. Everything else
// (map, comments, camera, edges, softening, radio, sounds, rumble) lives under SETTINGS.
public partial class PhysicsTest
{
    void PauseRowsTest()
    {
        GD.Print("Party Meeting row mapping tests");
        int combos = 0, bad = 0;
        foreach (bool norms in new[] { false, true })
        foreach (int laps in new[] { 0, 1, 2, 5 })
        foreach (bool board in new[] { false, true })
        foreach (bool padLabels in new[] { false, true })
        {
            var ids = RaceScene.PauseRowList(norms, laps, board);
            var hud = new Hud { PauseHasNorms = norms, PauseReplayAvailable = true, Pad = padLabels, PauseRowIds = ids.ToArray() };
            var lines = hud.PauseLines(out string watchLine);
            var want = new List<(PauseRowId Id, string Word)> { (PauseRowId.Continue, "CONTINUE") };
            if (laps >= 1) want.Add((PauseRowId.WatchLastLap, "WATCH LAST LAP"));
            if (laps >= 2) want.Add((PauseRowId.BrowseLaps, "BROWSE LAPS"));
            if (board) want.Add((PauseRowId.Leaderboards, "LEADERBOARDS"));
            if (norms) want.Add((PauseRowId.Norms, "NORMS"));
            want.AddRange(new[] { (PauseRowId.Instructions, "DRIVING SCHOOL"), (PauseRowId.Settings, "SETTINGS"), (PauseRowId.Restart, "RESTART"), (PauseRowId.Exit, "EXIT RACE") });
            bool ok = ids.Count == want.Count && lines.Count == want.Count && watchLine == null;      // (no watch line object unless the last lap has a state to show)
            ok &= !ids.Contains(PauseRowId.BrowseLaps) || ids.Contains(PauseRowId.WatchLastLap);      // (BROWSE LAPS never without WATCH LAST LAP)
            for (int i = 0; ok && i < want.Count; i++) ok = ids[i] == want[i].Id && lines[i].Contains(want[i].Word);
            ok &= ids[0] == PauseRowId.Continue && ids[^1] == PauseRowId.Exit && ids[^2] == PauseRowId.Restart;
            foreach (var gone in new[] { "MAP", "COMMENTS", "RADIO", "SONG", "LUXURIES", "BALANCE", "RUMBLE", "VISUAL", "SOUND", "KEYS", "REPLAY" }) foreach (var l in lines) ok &= !l.Contains(gone);
            combos++; if (!ok) { bad++; Check(false, $"pause rows: norms {norms} laps {laps} board {board} padLabels {padLabels}: [{string.Join(" | ", lines)}]"); }
            hud.Free();      // (an unattached Control owns a canvas item: freed here, or the run ends with leaked RIDs)
        }
        {
            // the last lap's state replaces nothing: it is the WATCH LAST LAP row's own tail, greyed (never an older lap's)
            var hud = new Hud { PauseRowIds = RaceScene.PauseRowList(true, 2).ToArray(), PauseWatchState = "PREPARING" };
            var lines = hud.PauseLines(out string watchLine);
            Check(watchLine != null && ReferenceEquals(watchLine, lines[1]) && lines[1].Contains("WATCH LAST LAP - PREPARING") && lines[2].Contains("BROWSE LAPS") && !lines[2].Contains("PREPARING"), "a last lap still being prepared: WATCH LAST LAP says so, BROWSE LAPS stays plain");
            hud.Free();
        }
        Check(bad == 0, $"every row of the Party Meeting is in the owner's order and carries the label of its id, in all {combos} combinations of norms, laps (0, 1, 2, 5), board and label set");
        GD.Print($"Party Meeting row mapping tests: {(_failures == 0 ? "all ok" : _failures + " FAIL")}");
    }
}
