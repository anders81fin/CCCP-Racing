using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CCCPRacing.Net;
using CCCPRacing.Race;
using CCCPRacing.Replay;

namespace CCCPRacing.Tests;

/// <summary>
/// --lan-board-tests: SHARE BOARDS (docs/lan-leaderboard-sync.md). The merge rules on real board files, the recording import on real
/// ghost/world-tape pairs, and the consent/relay/transfer protocol between in-process sessions through the production receive handlers.
/// Everything lives in fresh temporary folders; nothing of the player's records is read.
/// </summary>
public partial class PhysicsTest
{
    const string SigI = "1111111111111111", SigS = "2222222222222222", SigA = "3333333333333333", SigOld = "4444444444444444";

    sealed class BoardMachine
    {
        public string Dir; public Leaderboard[] Live = new Leaderboard[RecordStore.Sets]; public LanBoardStore Store; public LapArchive Laps; public int Changed;
        public string BoardPath(string track, int set) => RecordStore.PathsOnly(Dir, track).BoardForSet(set);
        public Leaderboard Disk(string track, int set) => Leaderboard.Load(BoardPath(track, set), -1, LanBoardManifest.Capacity(set));
        public void Mark(string track, string sig, int layout = RecordStore.Layout)
        {
            if (sig != null) File.WriteAllText(Dir + $"track_{track}.sig", sig + "\n");
            if (layout > 0) File.WriteAllText(Dir + $"records_{track}.ver", layout + "\n");
        }
        public void Row(string track, int set, string name, float time, string date = "2026-11-01", int power = 0, string rec = null)
        {
            var b = track == "ilyich" && Live[set] != null ? Live[set] : Disk(track, set);
            b.Insert(name, time, date, power, recordingId: rec); b.Save();
        }
        public void Open()
        {
            for (int set = 0; set < RecordStore.Sets; set++) Live[set] = Disk("ilyich", set);
            Laps = new LapArchive(Path.Combine(Dir, "leaderboard-laps"));
            Store = new LanBoardStore(Dir, "ilyich", SigI, set => Live[set], () => Changed++, Laps, t => t switch { "ilyich" => SigI, "siberia" => SigS, "atomgrad" => SigA, "bonus" => "5555555555555555", _ => null });
        }
    }

    BoardMachine NewMachine(string root, string name)
    {
        var m = new BoardMachine { Dir = Path.Combine(root, name) + "/" };
        Directory.CreateDirectory(m.Dir);
        m.Mark("ilyich", SigI);
        // private files the sync must never touch: a personal-best ghost slot, campaign and settings
        File.WriteAllBytes(m.Dir + "ghost_ilyich.bin", Encoding.UTF8.GetBytes("not a real ghost " + name));
        File.WriteAllText(m.Dir + "settings.cfg", "[lan]\nrecord_races=true\n");
        Directory.CreateDirectory(m.Dir + "profiles/1"); File.WriteAllText(m.Dir + "profiles/1/campaign.cfg", "[medals]\nilyich=2\n");
        m.Open();
        return m;
    }

    /// <summary>A real leaderboard recording (ghost + world tape) of this lap time in the machine's board archive, retained.</summary>
    string BoardRecording(BoardMachine m, LapRig rig, float seconds, string track = "ilyich", int set = 0, string sig = SigI)
    {
        var g = rig.Drive(seconds, 10, 20);
        var r = m.Laps.Submit(LapCap(g, rig.Rec, track, set, sig: sig));
        var k = m.Laps.Retain(r.Id, "BOARD").Result;
        if (!r.Accepted || !k.Ok) throw new InvalidOperationException("fixture recording not kept: " + r.Reason + " " + k.Message);
        return r.Id;
    }

    static string TreeHash(string dir, params string[] skip)
    {
        if (!Directory.Exists(dir)) return "none";
        var sb = new StringBuilder();
        foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
        {
            string rel = Path.GetRelativePath(dir, f);
            if (skip.Any(s => rel.StartsWith(s, StringComparison.Ordinal))) continue;
            sb.Append(rel).Append('=').Append(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(f)))).Append('\n');
        }
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    static string FileHash(string path) => File.Exists(path) ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))) : "none";

    sealed class SpyStore : ILanBoardStore
    {
        public readonly ILanBoardStore Inner; public int Builds, Merges, Stages, Commits, Reads;
        public SpyStore(ILanBoardStore inner) { Inner = inner; }
        public LanBoardManifest BuildManifest(out string note) { Builds++; return Inner.BuildManifest(out note); }
        public LanBoardMergeResult Merge(LanBoardManifest offer, long origin, IReadOnlyDictionary<string, string> known) { Merges++; return Inner.Merge(offer, origin, known); }
        public bool StillWanted(LanBoardWant w) => Inner.StillWanted(w);
        public Task<byte[]> ReadPayload(string id) { Reads++; return Inner.ReadPayload(id); }
        public Task<LanBoardStage> Stage(LanBoardWant w, byte[] p) { Stages++; return Inner.Stage(w, p); }
        public string Commit(LanBoardStage s) { Commits++; return Inner.Commit(s); }
        public void Discard(LanBoardStage s) => Inner.Discard(s);
    }

    void LanBoardTests()
    {
        string root = Path.Combine(Path.GetTempPath(), "cccp-lan-boards-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        BoardTestLog($"LAN boards: test root {root}");
        using var rig = new LapRig(this, 3, 2);
        try
        {
            LanBoardMergeRules(root);
            LanBoardStoreTests(root, rig);
            LanBoardImportTests(root, rig);
            LanBoardProtocolTests(root, rig);
            LanBoardWireBounds();
            Check(LanSession.Protocol == 15 && LanReplayHeaderOk(15) && LanReplayHeaderOk(14) && LanReplayHeaderOk(11) && !LanReplayHeaderOk(16),
                "LAN boards: live protocol 15; stored LAN replays of protocols 11-15 still pass the header check, 16 does not");
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    static void BoardTestLog(string s) => Godot.GD.Print(s);

    static bool LanReplayHeaderOk(int protocol)
    {
        try { LanReplayFile.Validate(new LanReplayFile.Header("ilyich", "0123456789abcdef", "0.853", protocol, 5, 1, new[] { new LanReplayFile.Player(1, "HOST", 0) }, 100)); return true; }
        catch (InvalidDataException) { return false; }
    }

    // ---------------------------------------------------------------- Leaderboard: keys, merge, ties, capacity, links, atomic save

    void LanBoardMergeRules(string root)
    {
        string path = Path.Combine(root, "rules.cfg");
        var b = new Leaderboard { Path = path, LastName = "LOCAL DRIVER" };
        b.Insert("ALPHA", 10f, "2026-11-01", recordingId: "lap-local-a"); b.Insert("BRAVO", 12f, "2026-11-02");
        Check(Leaderboard.RowKey("alpha", 10.0001f, "2026-11-01", 0, false, false, false) == Leaderboard.RowKey(b.Entries[0]), "LAN boards: a row's key is the cleaned name, the time to the millisecond, the date and markers");
        Check(b.MergeRow("ALPHA", 10.0002f, "2026-11-01", 0, false, false, false) == -1 && b.Entries.Count == 2, "LAN boards: the same result again is not a second row (idempotent)");
        Check(b.MergeRow("CHARLIE", 10f, "2026-11-03", 0, false, false, false) == 2 && b.Entries[0].Name == "ALPHA" && b.Entries[1].Name == "CHARLIE",
            "LAN boards: an equal time is placed by its row key");
        var other = new Leaderboard(); other.Insert("CHARLIE", 10f, "2026-11-03"); other.MergeRow("ALPHA", 10f, "2026-11-01", 0, false, false, false);
        Check(other.Entries.Select(e => e.Name).SequenceEqual(new[] { "ALPHA", "CHARLIE" }), "LAN boards: tied rows end in the same order whichever machine merged which (same top three everywhere)");
        Check(b.Entries[1].Date == "2026-11-03" && b.Entries[1].RecordingId == null, "LAN boards: a merged row keeps its own date and never brings a recording link");
        Check(b.MergeRow("DELTA", 13f, "2026-11-04", 0, false, false, false) == 0 && b.Entries.Count == 3 && b.Entries[2].Name == "BRAVO",
            "LAN boards: a slower result than a full board's last does not get on it (capacity kept)");
        Check(b.MergeRow("ECHO", 9f, "", 0, false, false, false) == 1 && b.Entries.Count == 3 && b.Entries.All(e => e.Name != "BRAVO"),
            "LAN boards: a faster result pushes the slowest off a full board, as a solo lap would");
        Check(b.Entries[1].RecordingId == "lap-local-a", "LAN boards: the local row's recording link survives the merge");
        Check(!b.AttachRecording(Leaderboard.RowKey(b.Entries[1]), "lap-other") && b.Entries[1].RecordingId == "lap-local-a", "LAN boards: an existing recording link is never replaced");
        Check(b.AttachRecording(Leaderboard.RowKey(b.Entries[0]), "lap-new") && b.Entries[0].RecordingId == "lap-new", "LAN boards: a verified recording links to a row that had none");
        Check(!b.AttachRecording(Leaderboard.RowKey(b.Entries[2]), "../escape"), "LAN boards: an unsafe recording id is never linked");
        Check(b.SaveAtomic() == null, "LAN boards: the verified board save works");
        var back = Leaderboard.Load(path);
        Check(back.LastName == "LOCAL DRIVER" && back.Entries.Select(Leaderboard.RowKey).SequenceEqual(b.Entries.Select(Leaderboard.RowKey))
            && back.Entries.Select(e => e.RecordingId).SequenceEqual(b.Entries.Select(e => e.RecordingId)), "LAN boards: the saved board reads back exactly (rows, links, the local last name)");
        string before = FileHash(path);
        b.MergeRow("FOXTROT", 8f, "", 0, false, false, false);
        foreach (string fail in new[] { "before-write", "before-replace" })
        {
            Leaderboard.FailSaveForTest = fail;
            Check(b.SaveAtomic() != null && FileHash(path) == before && !File.Exists(path + ".lan.tmp"), $"LAN boards: a save failing {fail} leaves the old board file whole and no temporary file");
        }
        var stak = new Leaderboard(Leaderboard.StakhanovSize);
        for (int i = 0; i < 12; i++) stak.MergeRow("S" + i, 30 + i, "", 100 + i, false, false, false);
        Check(stak.Entries.Count == 10 && stak.Entries[^1].Name == "S9", "LAN boards: the Stakhanov board keeps its ten");
    }

    // ---------------------------------------------------------------- the store: offers, merges, signatures, layouts, private files

    void LanBoardStoreTests(string root, LapRig rig)
    {
        string dir = Path.Combine(root, "store");
        var a = NewMachine(dir, "a"); var b = NewMachine(dir, "b");
        // A: rally petrol with a recording, Stakhanov with power, Siberia diesel (saved signature), Atomgrad on an old shape
        string recA = BoardRecording(a, rig, 4f);
        a.Row("ilyich", 0, "ALPHA", 4f, rec: recA); a.Row("ilyich", 0, "ALPHA TWO", 6f);
        a.Row("ilyich", 1, "ALPHA", 30f, power: 140);
        a.Mark("siberia", SigS); a.Row("siberia", 2, "SIB A", 70f);
        a.Mark("atomgrad", SigOld); a.Row("atomgrad", 3, "ATOM OLD", 80f);
        a.Mark("bonus", SigS, layout: 1); a.Row("bonus", 4, "BONUS OLDLAYOUT", 90f);
        // B: the same ALPHA lap without its recording, a faster rally row, Atomgrad current, Bonus records without a signature
        b.Row("ilyich", 0, "ALPHA", 4f); b.Row("ilyich", 0, "BRAVO", 3.5f, rec: "lap-20260101000000-ilyich-s0-beef");
        b.Mark("atomgrad", SigA); b.Row("atomgrad", 3, "ATOM B", 81f);
        b.Row("bonus", 4, "BONUS B", 95f);
        File.Delete(b.Dir + "records_bonus.ver");
        string ghostB = FileHash(b.Dir + "ghost_ilyich.bin"), settingsB = FileHash(b.Dir + "settings.cfg"), campaignB = FileHash(b.Dir + "profiles/1/campaign.cfg");
        string sibBoardBefore = FileHash(b.BoardPath("siberia", 2)), bonusBoardB = FileHash(b.BoardPath("bonus", 4)), atomBoardB = FileHash(b.BoardPath("atomgrad", 3));

        var offer = a.Store.BuildManifest(out string note);
        var ilyich = offer.Tracks.FirstOrDefault(t => t.Track == "ilyich");
        Check(note == null && offer.BuiltTrack == "ilyich" && offer.BuiltSignature == SigI && ilyich != null && ilyich.Signature == SigI,
            "LAN boards: the offer names the built track and its current signature");
        Check(offer.Tracks.Any(t => t.Track == "siberia" && t.Signature == SigS) && offer.Tracks.Any(t => t.Track == "atomgrad" && t.Signature == SigOld) && offer.Tracks.All(t => t.Track != "bonus"),
            "LAN boards: other tracks are offered on their saved signature; a track on an old record layout is not offered");
        var rowA = ilyich.Sets.Single(s => s.Set == 0).Rows.Single(r => r.Name == "ALPHA");
        Check(rowA.RecordingId == recA && rowA.Fingerprint != 0 && rowA.RecordingBytes > 0 && ilyich.Sets.Single(s => s.Set == 1).Rows[0].Power == 140,
            "LAN boards: a row's verified recording is offered by id, fingerprint and size; Stakhanov power travels with the row");
        var bytes = offer.ToBytes();
        var back = LanBoardManifest.Read(bytes);
        Check(back.RowCount == offer.RowCount && back.RecordingCount == 1 && bytes.Length < 4096, $"LAN boards: the offer survives the wire format ({bytes.Length} bytes for {offer.RowCount} rows)");

        var sibOnly = new LanBoardManifest(); sibOnly.Tracks.AddRange(back.Tracks.Where(t => t.Track == "siberia"));
        var notCurrent = new LanBoardManifest(); notCurrent.Tracks.Add(new LanBoardTrack("siberia",SigOld,sibOnly.Tracks[0].Sets));
        var unconfirmed = b.Store.Merge(notCurrent, 2, new Dictionary<string, string> { ["ilyich"] = SigI });
        Check(unconfirmed.Skipped.Any(x => x.StartsWith("siberia")) && unconfirmed.RowsAdded == 0 && !File.Exists(b.BoardPath("siberia", 2)) && !File.Exists(b.Dir + "track_siberia.sig"),
            "LAN boards: a missing track's boards are checked against current local geometry, not merely the offered saved signature");
        var known = new Dictionary<string, string> { ["ilyich"] = SigI, ["atomgrad"] = SigA, ["siberia"] = SigS };
        var res = b.Store.Merge(back, 2, known);
        var bI = b.Live[0];
        Check(res.RowsAdded == 3 && bI.Entries.Select(e => e.Name).SequenceEqual(new[] { "BRAVO", "ALPHA", "ALPHA TWO" }) && bI.Entries[0].RecordingId != null,
            $"LAN boards: rows merge into the live board by time; the local recording link stays (+{res.RowsAdded})");
        Check(res.Wants.Count == 1 && res.Wants[0].RecordingId == recA && res.Wants[0].RowKey == Leaderboard.RowKey(bI.Entries[1]),
            "LAN boards: the local row without a recording keeps its place and wants the comrade's recording (it is not dropped)");
        Check(b.Live[1].Entries.Count == 1 && b.Live[1].Entries[0].Power == 140 && b.Disk("ilyich", 1).Entries.Count == 1, "LAN boards: the Stakhanov set is merged and saved");
        Check(b.Disk("siberia", 2).Entries.Single().Name == "SIB A" && File.ReadAllText(b.Dir + "track_siberia.sig").Trim() == SigS && File.ReadAllText(b.Dir + "records_siberia.ver").Trim() == "2",
            "LAN boards: a track with no records here takes the comrade's boards and their signature and layout");
        Check(res.Skipped.Any(s => s.StartsWith("atomgrad")) && FileHash(b.BoardPath("atomgrad", 3)) == atomBoardB && File.ReadAllText(b.Dir + "track_atomgrad.sig").Trim() == SigA,
            "LAN boards: boards on an old track shape are not merged and the saved signature is never changed");
        Check(FileHash(b.BoardPath("bonus", 4)) == bonusBoardB && !File.Exists(b.Dir + "track_bonus.sig"), "LAN boards: records without a signature are never marked current");
        Check(FileHash(b.Dir + "ghost_ilyich.bin") == ghostB && FileHash(b.Dir + "settings.cfg") == settingsB && FileHash(b.Dir + "profiles/1/campaign.cfg") == campaignB && sibBoardBefore == "none",
            "LAN boards: ghosts, settings and campaign are untouched");
        string backup = Directory.GetFiles(Path.Combine(b.Dir, "lan-board-backups"), "*", SearchOption.AllDirectories).FirstOrDefault(f => f.EndsWith("leaderboard_ilyich.cfg")) ?? "";
        Check(backup != "" && Leaderboard.Load(backup).Entries.Count == 2, "LAN boards: the board's previous file is copied to the session's backup before its first change");
        Check(b.Changed > 0 && b.Disk("ilyich", 0).Entries.Select(Leaderboard.RowKey).SequenceEqual(bI.Entries.Select(Leaderboard.RowKey)), "LAN boards: the live board and its file agree; the other boards' cache is told");
        string hashAfter = TreeHash(b.Dir);
        var again = b.Store.Merge(LanBoardManifest.Read(bytes), 2, known);
        Check(again.RowsAdded == 0 && again.Duplicates >= 4 && TreeHash(b.Dir) == hashAfter && again.Wants.Count == 1, "LAN boards: the same offer again changes nothing (no duplicate rows, no new backup)");

        // a failed save rolls the live board back
        int rowsBefore = b.Live[3].Entries.Count;
        Leaderboard.FailSaveForTest = "before-replace";
        var a3 = new LanBoardManifest { BuiltTrack = "ilyich", BuiltSignature = SigI };
        a3.Tracks.Add(new LanBoardTrack("ilyich", SigI, new List<(int, IReadOnlyList<LanBoardRow>)> { (3, new List<LanBoardRow> { new("ROCKET", 5f, "", 0, false, false, false) }) }));
        var failed = b.Store.Merge(a3, 2, known);
        Check(failed.Error != null && b.Live[3].Entries.Count == rowsBefore && !File.Exists(b.BoardPath("ilyich", 3)), "LAN boards: a merge whose save fails is undone in memory too (a later solo save cannot write it)");

        // a recording still being saved on the giver's machine is not offered, and says so
        var g = rig.Drive(7f, 5, 5);
        var pending = a.Laps.Submit(LapCap(g, rig.Rec, "ilyich", 2, sig: SigI));
        a.Row("ilyich", 2, "PENDING", 7f, rec: pending.Id);
        var off2 = a.Store.BuildManifest(out string note2);
        var prow = off2.Tracks.Single(t => t.Track == "ilyich").Sets.Single(s => s.Set == 2).Rows.Single();
        Check(prow.RecordingId == null && off2.Pending == 1 && note2 != null, "LAN boards: a recording still being saved is offered without its link, with a visible note");
    }

    // ---------------------------------------------------------------- recordings: verify, stage, commit, collisions, tampering, rollback

    void LanBoardImportTests(string root, LapRig rig)
    {
        string dir = Path.Combine(root, "import");
        var a = NewMachine(dir, "a"); var b = NewMachine(dir, "b");
        string recA = BoardRecording(a, rig, 4.5f);
        a.Row("ilyich", 0, "ALPHA", 4.5f, rec: recA);
        string aTree = TreeHash(a.Dir);
        var offer = a.Store.BuildManifest(out _);
        var res = b.Store.Merge(offer, 2, new Dictionary<string, string>());
        var want = res.Wants.Single();
        var payload = a.Store.ReadPayload(recA).Result;
        var (pid, pmeta, pghost, ptape) = LanBoardPayload.Unpack(payload);
        var sentMeta = Encoding.UTF8.GetString(pmeta);
        Check(pid == recA && ptape != null && !sentMeta.Contains("profile-1") && sentMeta.Contains("\"Profile\": \"\"") && payload.Length <= want.Bytes,
            $"LAN boards: the shared recording carries the exact ghost and tape and a meta.json without the local profile ({payload.Length / 1024} KB)");
        var src = a.Laps.Open(recA); string hash = src.Sha256();
        string lapsDir = Path.Combine(b.Dir, "leaderboard-laps");

        // tampering: each refused, and nothing appears in the archive or staging
        string Tamper(Func<byte[], byte[]> change, LanBoardWant w = null)
        {
            string before = TreeHash(lapsDir);
            var st = b.Store.Stage(w ?? want, change((byte[])payload.Clone())).Result;
            if (st.Ok) b.Store.Discard(st);
            bool clean = TreeHash(lapsDir) == before && (!Directory.Exists(b.Laps.IncomingDir) || Directory.GetDirectories(b.Laps.IncomingDir).Length == 0);
            return st.Ok ? "ACCEPTED" : clean ? st.Error : "DIRTY: " + st.Error;
        }
        // (the ghost fingerprint covers the stored positions; the tape header names the ghost it belongs to; neither format has a checksum of its own)
        int ghostStart = payload.Length - (ptape.Length + 4) - pghost.Length, tapeStart = payload.Length - ptape.Length;
        string r1 = Tamper(p => { p[ghostStart + 12 + 2] ^= 0x55; return p; });
        Check(r1 != "ACCEPTED" && !r1.StartsWith("DIRTY"), $"LAN boards: a changed ghost position is refused ({r1})");
        string r2 = Tamper(p => { p[tapeStart + 4 + 4 + 1 + "ilyich".Length + 12 + 1] ^= 0x55; return p; });
        Check(r2 != "ACCEPTED" && !r2.StartsWith("DIRTY"), $"LAN boards: a tape that names another ghost is refused ({r2})");
        string r2b = Tamper(_ => LanBoardPayload.Pack(recA, pmeta, pghost, ptape[..(ptape.Length - 40)]));
        Check(r2b != "ACCEPTED" && !r2b.StartsWith("DIRTY"), $"LAN boards: a cut tape body is refused ({r2b})");
        string r3 = Tamper(p => p[..(p.Length - 10)]);
        Check(r3 != "ACCEPTED" && !r3.StartsWith("DIRTY"), $"LAN boards: a truncated recording is refused ({r3})");
        foreach (var (what, w) in new[] { ("set", want with { Set = 1 }), ("track", want with { Track = "siberia" }), ("time", want with { Time = want.Time + .5f }),
            ("signature", want with { Signature = SigOld }), ("fingerprint", want with { Fingerprint = want.Fingerprint ^ 1 }), ("id", want with { RecordingId = "lap-other" }) })
        {
            string r = Tamper(p => p, w);
            Check(r != "ACCEPTED" && !r.StartsWith("DIRTY"), $"LAN boards: a recording that is not the row's ({what}) is refused ({r})");
        }
        var forged = LanBoardPayload.Pack("lap-x", Encoding.UTF8.GetBytes(sentMeta), pghost, ptape);
        Check(Tamper(_ => forged) != "ACCEPTED", "LAN boards: a meta.json of another lap id is refused");
        bool unsafeRefused = false;
        try { LanBoardPayload.Unpack(LanBoardPayload.Pack("../../etc", pmeta, pghost, ptape)); } catch (InvalidDataException) { unsafeRefused = true; }
        Check(unsafeRefused, "LAN boards: a path-like recording id is refused before anything is written");

        // failure rollback at both steps
        LapArchive.FailImportForTest = "after-stage";
        var f1 = b.Store.Stage(want, payload).Result;
        Check(!f1.Ok && Directory.GetDirectories(b.Laps.IncomingDir).Length == 0 && !Directory.Exists(Path.Combine(lapsDir, recA)), "LAN boards: a staging failure removes its partial folder");
        var s2 = b.Store.Stage(want, payload).Result;
        LapArchive.FailImportForTest = "before-move";
        Check(s2.Ok && b.Store.Commit(s2) != null && Directory.GetDirectories(b.Laps.IncomingDir).Length == 0 && !Directory.Exists(Path.Combine(lapsDir, recA)) && b.Live[0].Entries[0].RecordingId == null,
            "LAN boards: a commit failing before its move leaves no folder and no link");

        // the real import
        var st = b.Store.Stage(want, payload).Result;
        Check(st.Ok && st.LocalId == recA, "LAN boards: a verified recording is staged under its own id");
        Check(b.Store.Commit(st) == null && b.Live[0].Entries[0].RecordingId == recA && b.Disk("ilyich", 0).Entries[0].RecordingId == recA, "LAN boards: the commit links the row, in memory and on disk");
        var files = Directory.GetFiles(Path.Combine(lapsDir, recA)).Select(Path.GetFileName).OrderBy(x => x).ToArray();
        Check(files.SequenceEqual(new[] { "ghost.bin", "meta.json", "records.ver", "tape.bin", "track.sig" }), "LAN boards: exactly the archive's files are written (no edits or other sidecars) " + string.Join(",", files));
        var reloaded = new LapArchive(lapsDir).Open(recA);
        Check(reloaded != null && reloaded.Sha256() == hash && reloaded.HasTape && reloaded.Meta.Profile == "" && reloaded.Meta.Name.StartsWith("LAN ALPHA"),
            "LAN boards: after a reload the imported recording opens with the comrade's exact ghost and tape, without their profile");
        var prep = LapPlayback.Prepare(reloaded, new CCCPRacing.Car.CarParams(), null, SigI, ghostOnly: true);
        Check(prep.Ok, "LAN boards: the imported recording passes the viewer's own preparation on the right signature (" + prep.Message + ")");
        Check(TreeHash(a.Dir) == aTree, "LAN boards: sending a recording never changes the giver's files");
        var again = b.Store.Merge(offer, 2, new Dictionary<string, string>());
        Check(again.Wants.Count == 0 && again.RowsAdded == 0, "LAN boards: once imported, the same offer wants nothing");

        // the same lap already here under another row: linked without a transfer
        var c = NewMachine(dir, "c");
        string recC = BoardRecording(c, rig, 5.5f);
        c.Row("ilyich", 0, "CHARLIE", 5.5f, rec: recC);
        var offerC = c.Store.BuildManifest(out _);
        var payloadC = c.Store.ReadPayload(recC).Result;
        var resB = b.Store.Merge(offerC, 3, new Dictionary<string, string>());
        var stC = b.Store.Stage(resB.Wants.Single(), payloadC).Result; b.Store.Commit(stC);
        b.Live[0].Insert("CHARLIE COPY", 5.5f, "2026-11-01"); b.Live[0].Save();
        var dupOffer = new LanBoardManifest(); var row = offerC.Tracks[0].Sets[0].Rows[0] with { Name = "CHARLIE COPY", Date = "2026-11-01" };
        dupOffer.Tracks.Add(new LanBoardTrack("ilyich", SigI, new List<(int, IReadOnlyList<LanBoardRow>)> { (0, new List<LanBoardRow> { row }) }));
        var linked = b.Store.Merge(dupOffer, 3, new Dictionary<string, string>());
        Check(linked.LinkedLocal == 1 && linked.Wants.Count == 0 && Directory.GetDirectories(lapsDir).Length == 2, "LAN boards: a recording already kept here is linked without fetching it again");

        // an id clash with a different local lap: a fresh local id, the original folder untouched
        var d = NewMachine(dir, "d");
        string recD = BoardRecording(d, rig, 6.5f);
        d.Row("ilyich", 0, "DELTA", 6.5f, rec: recD);
        var e = NewMachine(dir, "e");
        string mine = BoardRecording(e, rig, 8f);
        string clashDir = Path.Combine(e.Dir, "leaderboard-laps", recD);
        Directory.Move(Path.Combine(e.Dir, "leaderboard-laps", mine), clashDir);
        var meta = File.ReadAllText(Path.Combine(clashDir, "meta.json")).Replace(mine, recD);
        File.WriteAllText(Path.Combine(clashDir, "meta.json"), meta);
        e.Open();
        string clashHash = TreeHash(clashDir);
        var resE = e.Store.Merge(d.Store.BuildManifest(out _), 4, new Dictionary<string, string>());
        var stE = e.Store.Stage(resE.Wants.Single(), d.Store.ReadPayload(recD).Result).Result;
        Check(stE.Ok && stE.LocalId != recD && Leaderboard.CleanRecordingId(stE.LocalId) == stE.LocalId && e.Store.Commit(stE) == null,
            $"LAN boards: an id already taken by another lap gets a fresh local id ({stE.LocalId})");
        Check(TreeHash(clashDir) == clashHash && e.Live[0].Entries.Single(x => x.Name == "DELTA").RecordingId == stE.LocalId
            && new LapArchive(Path.Combine(e.Dir, "leaderboard-laps")).Open(stE.LocalId).Sha256() == d.Laps.Open(recD).Sha256(),
            "LAN boards: the clashing original stays byte for byte; the row links the remapped import");
    }

    // ---------------------------------------------------------------- the protocol: consent, relay, barrier, withdraw, cancel, late join, leave, resync

    void LanBoardProtocolTests(string root, LapRig rig)
    {
        string dir = Path.Combine(root, "net");
        BoardMachine h = NewMachine(dir, "host"), c2 = NewMachine(dir, "c2"), c3 = NewMachine(dir, "c3"), c4 = NewMachine(dir, "c4");
        string r2a = BoardRecording(c2, rig, 4f), r2b = BoardRecording(c2, rig, 5f, set: 1);
        c2.Row("ilyich", 0, "TWO", 4f, rec: r2a); c2.Row("ilyich", 1, "TWO", 5f, power: 120, rec: r2b);
        string r3 = BoardRecording(c3, rig, 4.25f);
        c3.Row("ilyich", 0, "THREE", 4.25f, rec: r3);
        string rh = BoardRecording(h, rig, 3.75f);
        h.Row("ilyich", 0, "HOST", 3.75f, rec: rh);
        string hTree = TreeHash(h.Dir), c4Tree = TreeHash(c4.Dir);

        var net = new LanSession.BoardNet("HOST", "C2", "C3");
        var spy = new SpyStore(h.Store);
        net.Host.BoardStore = spy; net.Peers[2].BoardStore = c2.Store; net.Peers[3].BoardStore = c3.Store;
        net.Pump();
        Check(net.Packets == 0 && TreeHash(h.Dir) == hTree, "LAN boards: with nobody saying YES nothing is sent and nothing changes");

        // C2 YES alone: its offer waits on the host, nobody receives it
        net.Peers[2].SetBoardShare(true); net.Pump();
        Check(c3.Live[0].Entries.All(e => e.Name != "TWO") && spy.Merges == 0 && net.Host.BoardSettled(1), "LAN boards: one comrade's YES alone shares with nobody");
        Check(net.Host.Players[2].BoardShare && !net.Host.Players[3].BoardShare, "LAN boards: the host knows who said YES");

        // C3 YES too, the host stays NO: the host relays offers and chunks, merges and writes nothing
        net.Peers[3].SetBoardShare(true);
        bool sawChunk = false; int blockedAtBarrier = 0;
        net.Pump(until: () =>
        {
            if (net.Host.BoardChunksRelayed > 0 && !sawChunk) { sawChunk = true; if (!net.Host.StartBallot()) blockedAtBarrier++; }
            return false;
        });
        Check(sawChunk && blockedAtBarrier == 1 && net.Host.State == LanSession.Phase.Lobby, "LAN boards: the host's ballot does not open while a recording is on its way");
        Check(c3.Live[0].Entries.Any(e => e.Name == "TWO" && e.RecordingId == r2a) && c3.Live[1].Entries.Any(e => e.Name == "TWO" && e.RecordingId == r2b)
            && c2.Live[0].Entries.Any(e => e.Name == "THREE" && e.RecordingId == r3), "LAN boards: two sharing comrades get each other's rows and recordings through a host that does not share");
        Check(spy.Merges == 0 && spy.Stages == 0 && spy.Commits == 0 && spy.Builds == 0 && TreeHash(h.Dir) == hTree && net.Host.BoardChunksRelayed > 0,
            $"LAN boards: the NO host relayed {net.Host.BoardChunksRelayed} chunks and its records are unchanged");
        Check(net.MaxPacket <= LanSession.BoardChunkBytes + 128 || net.MaxPacket <= LanBoardManifest.MaxBytes + 64, $"LAN boards: no packet above the chunk or offer limit (largest {net.MaxPacket} B)");
        Check(new LapArchive(Path.Combine(c3.Dir, "leaderboard-laps")).Open(r2a).Sha256() == c2.Laps.Open(r2a).Sha256(), "LAN boards: the relayed recording arrives byte for byte");
        Check(!net.Host.BoardSyncPending && net.Host.BoardSettled(2), "LAN boards: when the sync is done the barrier opens");

        // resync: no duplicates, nothing new on disk
        string c3Tree = TreeHash(c3.Dir), c2Tree = TreeHash(c2.Dir);
        int imported = net.Peers[3].BoardRecordingsImported;
        Check(net.Peers[3].ResyncBoards(), "LAN boards: an idle sharing comrade can resync");
        net.Pump();
        Check(TreeHash(c3.Dir) == c3Tree && TreeHash(c2.Dir) == c2Tree && net.Peers[3].BoardRecordingsImported == imported, "LAN boards: a resync of the same boards adds no row and no recording");

        // the host says YES: it merges both, and both get the host's
        net.Host.SetBoardShare(true); net.Pump();
        Check(h.Live[0].Entries.Any(e => e.Name == "TWO" && e.RecordingId == r2a) && h.Live[0].Entries.Any(e => e.Name == "THREE" && e.RecordingId == r3)
            && c2.Live[0].Entries.Any(e => e.Name == "HOST" && e.RecordingId == rh) && c3.Live[0].Entries.Any(e => e.Name == "HOST" && e.RecordingId == rh),
            "LAN boards: a host that says YES shares and merges like any comrade");
        Check(h.Live[0].Entries.Count == 3 && h.Live[0].Entries.Select(e => e.Time).SequenceEqual(new[] { 3.75f, 4f, 4.25f }), "LAN boards: the merged board is the top three by time");

        // late join: C4 joins, says YES and receives the stored offers
        var p4 = net.Add(4, "C4"); p4.BoardStore = c4.Store; net.Host.Players[4] = new LanPlayer { Id = 4, Name = "C4", Slot = 3 };
        net.Pump();
        Check(TreeHash(c4.Dir) == c4Tree, "LAN boards: a comrade who joins and says nothing gets nothing");
        p4.SetBoardShare(true); net.Pump();
        Check(c4.Live[0].Entries.Count == 3 && c4.Live[0].Entries.All(e => e.RecordingId != null), "LAN boards: a late comrade who says YES gets the boards and their recordings");

        // withdraw in the middle of a transfer: it stops, nothing half-kept, the barrier opens
        // a long lap (many chunks, so the transfer spans many frames) on the Stakhanov board, which has room for it
        string r2c = BoardRecording(c2, rig, 40f, set: 1);
        c2.Row("ilyich", 1, "TWO LONG", 40f, power: 110, rec: r2c);
        string c4Before = TreeHash(c4.Dir, "leaderboard_");
        net.Peers[2].ResyncBoards();
        bool withdrew = false; int chunksBefore = p4.BoardChunksIn;
        net.Pump(until: () =>
        {
            if (!withdrew && p4.BoardChunksIn > chunksBefore && p4.BoardIncomingTid >= 0) { withdrew = true; p4.SetBoardShare(false); }
            return false;
        });
        Check(withdrew && !p4.BoardBusyLocal && !Directory.Exists(Path.Combine(c4.Dir, "leaderboard-laps", r2c))
            && (!Directory.Exists(c4.Laps.IncomingDir) || Directory.GetDirectories(c4.Laps.IncomingDir).Length == 0), "LAN boards: NO in the middle of a transfer stops it and keeps nothing half-made");
        Check(c4.Live[1].Entries.Any(e => e.Name == "TWO LONG" && e.RecordingId == null) && !net.Host.Players[4].BoardShare && net.Peers[2].BoardServing == 0,
            "LAN boards: rows merged before the NO stay (without the unfinished recording); the giver stopped serving");
        Check(!net.Host.BoardSyncPending, "LAN boards: after a withdrawal the ballot is free again");

        // cancel (still YES), then a leave in the middle
        p4.SetBoardShare(true);
        bool cancelled = false;
        int before2 = p4.BoardChunksIn;
        net.Pump(until: () => { if (!cancelled && p4.BoardChunksIn > before2 && p4.BoardIncomingTid >= 0) { cancelled = true; p4.CancelBoardSync(); } return false; });
        Check(cancelled && p4.BoardShareMine && !p4.BoardBusyLocal && !net.Host.BoardSyncPending && !Directory.Exists(Path.Combine(c4.Dir, "leaderboard-laps", r2c)), "LAN boards: CANCEL stops the sync and stays YES");
        p4.ResyncBoards();
        bool left = false;
        int before3 = p4.BoardChunksIn;
        net.Pump(until: () => { if (!left && p4.BoardChunksIn > before3 && p4.BoardIncomingOrigin == 2) { left = true; net.Leave(2); } return false; });
        Check(left && !p4.BoardBusyLocal && !net.Host.BoardSyncPending && p4.BoardNoteBad, "LAN boards: a comrade leaving mid-transfer fails that recording visibly and frees the ballot");

        // no transfer outside the lobby: chunks that arrive during the race are counted and dropped
        int outside = p4.BoardChunksOutsideLobby;
        p4.BoardFixtureState(LanSession.Phase.Race);
        p4.OnPacket(1, LanSession.BoardChunkFixture(3, 1234, 0, 0, 1, 10, new byte[10]));      // (host to client: the long is the origin)
        Check(p4.BoardChunksOutsideLobby == outside + 1 && p4.BoardIncomingTid < 0, "LAN boards: a chunk outside the lobby is dropped and counted");
        p4.BoardFixtureState(LanSession.Phase.Lobby);
        net.Host.BoardFixtureState(LanSession.Phase.Ballot);
        int relayed = net.Host.BoardChunksRelayed;
        net.Host.OnPacket(3, LanSession.BoardChunkFixture(4, 1, 0, 0, 1, 10, new byte[10]));
        Check(net.Host.BoardChunksRelayed == relayed && net.Wire.Count == 0, "LAN boards: the host relays no chunk once the ballot is open");
        net.Host.BoardFixtureState(LanSession.Phase.Lobby);
        // a forged chunk from a comrade the transfer is not with is ignored
        int blocked = p4.BoardMessagesBlocked;
        p4.OnPacket(1, LanSession.BoardChunkFixture(3, 99, 0, 0, 1, 10, new byte[10]));
        Check(p4.BoardMessagesBlocked == blocked + 1, "LAN boards: a chunk of no transfer of this machine (wrong sender / id) is ignored");

        // the session closes: consent is forgotten
        p4.BoardFixtureClose();
        Check(!p4.BoardShareMine && !p4.BoardBusyLocal, "LAN boards: closing the session forgets YES");
    }

    void LanBoardWireBounds()
    {
        var m = new LanBoardManifest();
        m.Tracks.Add(new LanBoardTrack("ilyich", SigI, new List<(int, IReadOnlyList<LanBoardRow>)> { (0, new List<LanBoardRow> { new("OK", 10f, "2026-11-01", 0, false, false, false) }) }));
        byte[] good = m.ToBytes();
        Check(LanBoardManifest.Read(good).RowCount == 1, "LAN boards: a valid offer reads");
        bool Bad(Action<LanBoardManifest> change)
        {
            var x = new LanBoardManifest(); change(x);
            try { LanBoardManifest.Read(x.ToBytes()); return false; } catch (InvalidDataException) { return true; } catch (EndOfStreamException) { return true; }
        }
        List<(int, IReadOnlyList<LanBoardRow>)> One(int set, LanBoardRow r, int n = 1) => new() { (set, Enumerable.Repeat(r, n).ToList()) };
        var ok = new LanBoardRow("OK", 10f, "", 0, false, false, false);
        Check(Bad(x => x.Tracks.Add(new LanBoardTrack("moscow", SigI, One(0, ok)))), "LAN boards: an unknown track is refused");
        Check(Bad(x => x.Tracks.Add(new LanBoardTrack("ilyich", "nothex", One(0, ok)))), "LAN boards: a malformed signature is refused");
        Check(Bad(x => x.Tracks.Add(new LanBoardTrack("ilyich", SigI, One(0, ok with { Time = float.NaN })))) && Bad(x => x.Tracks.Add(new LanBoardTrack("ilyich", SigI, One(0, ok with { Time = -1 }))))
            && Bad(x => x.Tracks.Add(new LanBoardTrack("ilyich", SigI, One(0, ok with { Time = 99999 })))), "LAN boards: NaN, negative and absurd times are refused");
        Check(Bad(x => x.Tracks.Add(new LanBoardTrack("ilyich", SigI, One(0, ok, 4)))), "LAN boards: more rows than the board holds are refused");
        Check(Bad(x => x.Tracks.Add(new LanBoardTrack("ilyich", SigI, One(5, ok)))), "LAN boards: a record set that does not exist is refused");
        Check(Bad(x => x.Tracks.Add(new LanBoardTrack("ilyich", SigI, One(0, ok with { Name = "lower" })))) && Bad(x => x.Tracks.Add(new LanBoardTrack("ilyich", SigI, One(0, ok with { Date = "yesterday" })))),
            "LAN boards: uncleaned names and bad dates are refused");
        Check(Bad(x => x.Tracks.Add(new LanBoardTrack("ilyich", SigI, One(0, ok with { RecordingId = "../lap", Fingerprint = 1, RecordingBytes = 10 })))), "LAN boards: a path-like recording id is refused");
        Check(Bad(x => x.Tracks.Add(new LanBoardTrack("ilyich", SigI, One(0, ok with { RecordingId = "lap-a", Fingerprint = 1, RecordingBytes = LanBoardPayload.MaxBytes + 1 })))), "LAN boards: an oversize recording is refused");
        Check(Bad(x => x.Tracks.Add(new LanBoardTrack("ilyich", SigI, One(0, ok with { Power = 50 })))), "LAN boards: power on a rally board is refused");
        Check(Bad(x => { for (int i = 0; i < 2; i++) x.Tracks.Add(new LanBoardTrack("ilyich", SigI, One(0, ok))); }), "LAN boards: a track twice is refused");
        bool big = false; try { LanBoardManifest.Read(new byte[LanBoardManifest.MaxBytes + 1]); } catch (InvalidDataException) { big = true; }
        Check(big, "LAN boards: an offer over 32 KB is refused unread");
        bool trailing = false; try { LanBoardManifest.Read(good.Append((byte)0).ToArray()); } catch (InvalidDataException) { trailing = true; }
        Check(trailing, "LAN boards: trailing bytes are refused");
    }
}

/// <summary>
/// --lan-board-seed=ROOT (tools/net/lan_board_smoke.py): three isolated data roots (host, client1, client2) with different boards on the
/// real current Kolkhoz and Siberia signatures and real ~1.6 MB leaderboard recordings. --lan-board-verify=ROOT prints one
/// "LANBOARDVERIFY {json}" line per root: every board row, whether its recording opens and passes the viewer's preparation, and a hash
/// of the record files (boards, signatures, layouts, ghosts, tapes, the recordings archive, staging and backups).
/// </summary>
public partial class PhysicsTest
{
    static readonly string[] SeedRoots = { "host", "client1", "client2" };

    void LanBoardSeed(string root)
    {
        string sigI = Track.TrackSignature.Compute(Track.TrackBuilder.Build(Track.TrackDef.Load("res://tracks/ilyich.json")));
        string sigS = Track.TrackSignature.Compute(Track.TrackBuilder.Build(Track.TrackDef.Load("res://tracks/siberia.json")));
        using var rig = new LapRig(this, 3, 2);
        foreach (string name in SeedRoots)
        {
            var m = new BoardMachine { Dir = Path.Combine(root, name) + "/" };
            if (Directory.Exists(m.Dir)) throw new IOException("seed root exists: " + m.Dir);
            Directory.CreateDirectory(m.Dir);
            m.Mark("ilyich", sigI);
            m.Open();
            string Rec(float t, int set = 0) => BoardRecordingSig(m, rig, t, set, sigI);
            switch (name)
            {
                case "host":
                    m.Row("ilyich", 0, "HOST A", 41f, "2026-11-01", rec: Rec(41f));
                    m.Row("ilyich", 1, "HOST A", 52f, "2026-11-01", power: 120, rec: Rec(52f, 1));
                    m.Mark("siberia", sigS); m.Row("siberia", 2, "HOST SIB", 80f, "2026-11-02");
                    var g = rig.Drive(44f, 5, 5); File.WriteAllBytes(m.Dir + "ghost_ilyich.bin", g.ToBytes());      // a personal-best ghost slot (must stay untouched)
                    break;
                case "client1":
                    m.Row("ilyich", 0, "CLIENT ONE", 40.5f, "2026-11-03", rec: Rec(40.5f));
                    m.Row("ilyich", 0, "SHARED ROW", 43f, "2026-11-03");
                    m.Row("ilyich", 3, "C1 ROCKET", 39f, "2026-11-03", rec: Rec(39f, 3));
                    m.Mark("atomgrad", "0123456789abcdef"); m.Row("atomgrad", 0, "C1 ATOM OLD", 90f, "2026-11-03");
                    break;
                case "client2":
                    m.Row("ilyich", 0, "CLIENT TWO", 42f, "2026-11-04", rec: Rec(42f));
                    m.Row("ilyich", 0, "SHARED ROW", 43f, "2026-11-03");
                    m.Row("ilyich", 4, "C2 SPUTNIK", 38f, "2026-11-04", rec: Rec(38f, 4));
                    break;
            }
            File.WriteAllText(m.Dir + "settings.cfg", "[driver]\nname=\"" + name.ToUpperInvariant() + "\"\n");
            BoardTestLog($"LANBOARDSEED {name} {m.Dir} sig {sigI}");
        }
        BoardTestLog("LANBOARDSEED done");
    }

    string BoardRecordingSig(BoardMachine m, LapRig rig, float seconds, int set, string sig) => BoardRecording(m, rig, seconds, "ilyich", set, sig);

    void LanBoardVerify(string root)
    {
        foreach (string name in SeedRoots)
        {
            string dir = Path.Combine(root, name) + "/";
            if (!Directory.Exists(dir)) continue;
            var laps = new LapArchive(Path.Combine(dir, "leaderboard-laps"));
            string sigI = File.Exists(dir + "track_ilyich.sig") ? File.ReadAllText(dir + "track_ilyich.sig").Trim() : "";
            var rows = new List<string>();
            int linked = 0, open = 0, prepared = 0;
            foreach (string track in LanBoardStore.AllTracks)
                for (int set = 0; set < RecordStore.Sets; set++)
                {
                    string path = RecordStore.PathsOnly(dir, track).BoardForSet(set);
                    if (!File.Exists(path)) continue;
                    foreach (var e in Leaderboard.Load(path, -1, LanBoardManifest.Capacity(set)).Entries)
                    {
                        string state = "none";
                        if (e.RecordingId != null)
                        {
                            linked++;
                            var src = laps.Open(e.RecordingId, out string why);
                            if (src == null) state = "BROKEN:" + why;
                            else
                            {
                                open++;
                                bool ok = src.Meta.TrackId == track && src.Meta.Set == set && Math.Abs(src.Meta.LapSeconds - e.Time) <= .001f;
                                var prep = LapPlayback.Prepare(src, new CCCPRacing.Car.CarParams(), null, track == "ilyich" ? sigI : null, ghostOnly: true);
                                if (ok && prep.Ok) prepared++;
                                state = (ok && prep.Ok ? "ok:" : "MISMATCH:") + src.Sha256()[..12];
                            }
                        }
                        rows.Add($"{track}/{set}/{e.Name}/{e.Time:F3}/{e.Date}/{e.Power}/{state}");
                    }
                }
            string records = TreeHash(dir, "settings.cfg", "profiles", "speedtrap", "cameras", "recordings", "logs", "lan", "replays", "laps" + Path.DirectorySeparatorChar, "shader_cache", "vulkan");
            int dirs = Directory.Exists(Path.Combine(dir, "leaderboard-laps")) ? Directory.GetDirectories(Path.Combine(dir, "leaderboard-laps")).Length : 0;
            int staging = Directory.Exists(laps.IncomingDir) ? Directory.GetDirectories(laps.IncomingDir).Length : 0;
            int backups = Directory.Exists(dir + "lan-board-backups") ? Directory.GetFiles(dir + "lan-board-backups", "*", SearchOption.AllDirectories).Length : 0;
            string ghost = FileHash(dir + "ghost_ilyich.bin");
            string json = System.Text.Json.JsonSerializer.Serialize(new { root = name, rows, linked, open, prepared, recordings = dirs, staging, backups, records, ghost });
            BoardTestLog("LANBOARDVERIFY " + json);
        }
    }
}
