using System.IO;
using System.Linq;
using CCCPRacing.Net;
using CCCPRacing.Race;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    void StakhanovFuelBoardTests(string root)
    {
        var source = NewMachine(root, "fuel-source");
        var target = NewMachine(root, "fuel-target");
        var old = new Leaderboard(Leaderboard.StakhanovSize) { Path = source.BoardPath("ilyich", 1) };
        old.Insert("LEGACY", 19, "2026-11-01");
        old.Insert("OLD PETROL", 20, "2026-11-01", 100, recordingId: "lap-old-petrol");
        old.Save();
        string original = FileHash(old.Path);
        source.Open();
        var board = source.Live[1];
        Check(board.FuelPartitions && board.Entries.Count == 2 && board.Entries.Single(e => e.Power == 100).RecordingId == "lap-old-petrol", "fuel boards: old rows migrate with their actual fuel and recording link; unknown remains unclassified");
        for (int grade = 0; grade < Training.LapsCount; grade++)
            for (int row = 0; row < 12; row++) board.Insert("FUEL " + grade + " ROW " + row, 100 * (grade + 1) + row, "2026-11-07", Training.PercentForKey(grade + 1));
        Check(Enumerable.Range(0, 4).All(g => board.Entries.Count(e => e.Power == Training.PercentForKey(g + 1)) == 10) && board.Entries.Count == 41,
            "fuel boards: four independent full top tens coexist with the unknown legacy row");
        Check(board.Qualifies(55, 50) && !board.Qualifies(999, 50) && board.RankFor(305, 200) == 7,
            "fuel boards: qualification and ranks use the lap's fuel rather than the faster fuels");
        Check(board.SaveAtomic() == null && FileHash(old.Path + ".before-fuel-top10") == original, "fuel boards: first save preserves exact original bytes before upgrading the board");
        var back = source.Disk("ilyich", 1);
        Check(back.Entries.SequenceEqual(board.Entries), "fuel boards: save/load preserves every fuel row, marker and recording link");
        board.Insert("NEW DIESEL", 50, "2026-11-07", 50); board.Save();
        Check(FileHash(old.Path + ".before-fuel-top10") == original && board.Entries.Count(e => e.Power == 100) == 10, "fuel boards: later saves preserve the original backup and other fuels");
        var manifest = source.Store.BuildManifest(out _);
        var wire = LanBoardManifest.Read(manifest.ToBytes());
        Check(wire.RowCount == 41, "fuel boards: all forty fuel results plus legacy row survive the bounded LAN manifest");
        target.Store.Merge(wire, 2, new System.Collections.Generic.Dictionary<string, string> { ["ilyich"] = SigI });
        Check(target.Live[1].Entries.Count == 41 && Enumerable.Range(0, 4).All(g => target.Live[1].Entries.Count(e => e.Power == Training.PercentForKey(g + 1)) == 10),
            "fuel boards: consenting LAN receiver retains every independent top ten");
        target.Store.Merge(wire, 2, new System.Collections.Generic.Dictionary<string, string> { ["ilyich"] = SigI });
        Check(target.Disk("ilyich", 1).Entries.Count == 41, "fuel boards: repeated sync is idempotent and persists all fuel rows");
        var full = new LanBoardManifest();
        foreach (string track in LanBoardStore.AllTracks)
        {
            var sets = new System.Collections.Generic.List<(int, System.Collections.Generic.IReadOnlyList<LanBoardRow>)>();
            for (int set = 0; set < RecordStore.Sets; set++)
                sets.Add((set, Enumerable.Range(0, LanBoardManifest.Capacity(set)).Select(i => new LanBoardRow("TWELVE CHARS", 30 + i, "2026-11-07", set == 1 ? 100 : 0, false, false, false, "lap-" + new string('a', 124), 1, 1)).ToArray()));
            full.Tracks.Add(new LanBoardTrack(track, SigI, sets));
        }
        byte[] fullBytes = full.ToBytes();
        Check(fullBytes.Length <= LanBoardManifest.MaxBytes && LanBoardManifest.Read(fullBytes).RowCount == 248,
            "fuel boards: every track and set fits one bounded offer even with maximum-length recording ids");
    }
}
