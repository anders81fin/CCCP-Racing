using System.Collections.Generic;
using CCCPRacing.Race;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Tests;

// --pause-rows-tests: the Party Meeting's rows are one list of ids (RaceScene.PauseRowList): the lines the Hud draws, its mouse hit test and the actions all read it. For every combination of
// NORMS, REPLAY and the pad's two label sets the order is the owner's (REPLAY first, then NORMS, DRIVING SCHOOL, SETTINGS, CONTINUE, RESTART RACE, EXIT RACE) and every line carries the label of its id. Everything else
// (map, comments, camera, edges, softening, radio, sounds, rumble) lives under SETTINGS.
public partial class PhysicsTest
{
    void PauseRowsTest()
    {
        GD.Print("Party Meeting row mapping tests");
        int combos = 0, bad = 0;
        foreach (bool norms in new[] { false, true })
        foreach (bool replay in new[] { false, true })
        foreach (bool padLabels in new[] { false, true })
        {
            var ids = RaceScene.PauseRowList(norms, replay);
            var hud = new Hud { PauseHasNorms = norms, PauseReplayAvailable = true, Pad = padLabels, PauseRowIds = ids.ToArray() };
            var lines = hud.PauseLines(out string replayLine);
            var want = new List<(PauseRowId Id, string Word)> { };
            if (replay) want.Add((PauseRowId.Replay, "REPLAY"));
            if (norms) want.Add((PauseRowId.Norms, "NORMS"));
            want.AddRange(new[] { (PauseRowId.Instructions, "DRIVING SCHOOL"), (PauseRowId.Settings, "SETTINGS"), (PauseRowId.Continue, "CONTINUE"), (PauseRowId.Restart, "RESTART"), (PauseRowId.Exit, "EXIT RACE") });
            bool ok = ids.Count == want.Count && lines.Count == want.Count && (!replay || ids[0] == PauseRowId.Replay && ReferenceEquals(lines[0], replayLine)) && (replay || replayLine == null);
            for (int i = 0; ok && i < want.Count; i++) ok = ids[i] == want[i].Id && lines[i].Contains(want[i].Word);
            foreach (var gone in new[] { "MAP", "COMMENTS", "RADIO", "SONG", "LUXURIES", "BALANCE", "RUMBLE", "VISUAL", "SOUND", "KEYS" }) foreach (var l in lines) ok &= !l.Contains(gone);
            combos++; if (!ok) { bad++; Check(false, $"pause rows: norms {norms} replay {replay} padLabels {padLabels}: [{string.Join(" | ", lines)}]"); }
            hud.Free();      // (an unattached Control owns a canvas item: freed here, or the run ends with leaked RIDs)
        }
        Check(bad == 0, $"every row of the Party Meeting is in the owner's order and carries the label of its id, in all {combos} combinations of norms, replay and label set");
        GD.Print($"Party Meeting row mapping tests: {(_failures == 0 ? "all ok" : _failures + " FAIL")}");
    }
}
