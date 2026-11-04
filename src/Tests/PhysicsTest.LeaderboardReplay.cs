using System;
using System.IO;
using System.Linq;
using CCCPRacing.Race;
using CCCPRacing.Replay;

namespace CCCPRacing.Tests;
public partial class PhysicsTest
{
    void LeaderboardReplayTests()
    {
        string root = Path.Combine(Path.GetTempPath(), "cccp-board-replay-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var rig = new LapRig(this, 3, 2);
        try
        {
            var archive = new LapArchive(Path.Combine(root, "leaderboard-laps"));
            var normal = new LapArchive(Path.Combine(root, "laps"));
            var g = rig.Drive(4, 10, 25);
            var result = archive.Submit(LapCap(g, rig.Rec, "ilyich", 0));
            var kept = archive.Retain(result.Id, "LEADERBOARD").Result;
            Check(result.Accepted && kept.Ok, "board recording: production ghost/world pair is auto-retainable immediately after submission");
            var source = archive.Open(result.Id); string hash = source.Sha256();
            Check(source.HasTape && normal.List().Count == 0, "board recording: separate storage never appears in ordinary replay archive");
            string path = Path.Combine(root, "board.cfg");
            var board = new Leaderboard { Path = path };
            board.Insert("A", 4, recordingId: result.Id); board.Insert("B", 3, recordingId: "lap-second");
            board.Insert("OLD", 5); board.Save();
            var loaded = Leaderboard.Load(path);
            Check(loaded.Entries[0].RecordingId == "lap-second" && loaded.Entries[1].RecordingId == result.Id
                && loaded.Entries[2].RecordingId == null, "board recording: sorting and save/reload preserve exact associations; old row has no invented recording");
            foreach (var unsafeId in new[] { "../lap-file", "lap-../other", "/lap-absolute", "lap-back\\slash", "other" })
                Check(Leaderboard.CleanRecordingId(unsafeId) == null, "board recording: invalid archive reference refused");
            loaded.Insert("NEW", 2, recordingId: "lap-third"); loaded.Insert("BEST", 1, recordingId: "lap-best"); loaded.Save();
            Check(!loaded.Entries.Any(e => e.RecordingId == result.Id) && new LapArchive(Path.Combine(root, "leaderboard-laps")).Open(result.Id).Sha256() == hash,
                "board recording: displacement does not delete original recording bytes");
            Check(new Leaderboard(Leaderboard.StakhanovSize).Capacity == 10 && loaded.Capacity == 3, "board recording: rally and Stakhanov capacities unchanged");
        }
        finally { Directory.Delete(root, true); }
    }
}
