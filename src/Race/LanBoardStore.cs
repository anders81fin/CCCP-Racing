using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CCCPRacing.Net;
using CCCPRacing.Replay;
using CCCPRacing.Track;
using Godot;
using IOFile = System.IO.File;

namespace CCCPRacing.Race;

/// <summary>
/// The records side of SHARE BOARDS (docs/lan-leaderboard-sync.md): builds this machine's offer from the boards of all four tracks and
/// five record sets, merges a comrade's offer into them, and brings verified recordings into the leaderboard recordings archive.
/// Only boards (leaderboard_*.cfg) and that archive change; ghosts, tapes, campaign and settings are never touched. A board is merged
/// only when both sides saved it on the same track signature and record layout. Each board file is copied once per session before
/// its first change, and saved through a verified temporary file. The built track's boards are the race scene's own instances, so a
/// later solo lap saves on top of the merged results; the other tracks' cached boards are dropped after every change.
/// </summary>
public sealed class LanBoardStore : ILanBoardStore
{
    public static readonly string[] AllTracks = { "ilyich", "siberia", "atomgrad", "bonus" };
    public readonly string Dir, BuiltTrack, BuiltSignature;
    readonly Func<int, Leaderboard> _live;
    readonly Action _changed;
    readonly Func<string,string> _freshSignature;
    static readonly Dictionary<string,string> FreshSignatures = new(StringComparer.Ordinal);
    public readonly LapArchive Archive;
    /// <summary>Names the backup folder of this session (lan-board-backups/&lt;stamp&gt;/).</summary>
    public Func<string> BackupStamp = () => DateTime.Now.ToString("yyyyMMdd-HHmmss");
    readonly HashSet<string> _backedUp = new();

    /// <param name="dir">The records directory ("user://" or a --data-dir).</param>
    /// <param name="live">The race scene's board of a record set of the built track.</param>
    /// <param name="changed">Called after any board changed: drop cached boards of other tracks.</param>
    public LanBoardStore(string dir, string builtTrack, string builtSignature, Func<int, Leaderboard> live, Action changed, LapArchive archive, Func<string,string> freshSignature = null)
    {
        Dir = string.IsNullOrEmpty(dir) ? "user://" : dir.EndsWith('/') ? dir : dir + "/";
        BuiltTrack = builtTrack; BuiltSignature = builtSignature; _live = live; _changed = changed; Archive = archive;
        _freshSignature = freshSignature ?? FreshSignatureFromGame;
    }
    bool _cleaned;      // a previous session's leftover staging folders are removed before the first import (never on a machine that does not share)

    static string Real(string p) => ProjectSettings.GlobalizePath(p);

    // Only a consenting receiver starting a missing track's boards needs this. Cache the
    // actual shipped geometry once, in the lobby, rather than requiring someone to have
    // already raced every track. No imported old signature is labelled as current.
    static string FreshSignatureFromGame(string track)
    {
        if(!TrackIds.Known(track)) return null;
        lock(FreshSignatures) {
            if(FreshSignatures.TryGetValue(track,out var saved))return saved;
            var watch=System.Diagnostics.Stopwatch.StartNew();
            var sig=TrackSignature.Compute(TrackBuilder.Build(TrackDef.Load("res://tracks/"+track+".json")));
            FreshSignatures[track]=sig;
            GD.Print($"LAN boards: verified {track} geometry in {watch.Elapsed.TotalMilliseconds:F0} ms (cached)");
            return sig;
        }
    }


    /// <summary>A board of any track: the built one's live instance, else read from its file (an old record lap with no board file still shows as TOVARISH, as the race scene would show it).</summary>
    Leaderboard BoardOf(string track, int set)
    {
        if (track == BuiltTrack && _live != null) return _live(set);
        var paths = RecordStore.PathsOnly(Dir, track);
        string path = paths.BoardForSet(set);
        float seed = -1;
        if (!Godot.FileAccess.FileExists(path) && IOFile.Exists(Real(paths.GhostForSet(set))))
            try { LapGhostBytes.Fingerprint(IOFile.ReadAllBytes(Real(paths.GhostForSet(set))), out _, out seed); } catch { seed = -1; }
        return Leaderboard.Load(path, seed, LanBoardManifest.Capacity(set));
    }

    // ------------------------------------------------------------ the offer

    public LanBoardManifest BuildManifest(out string note)
    {
        note = null;
        var m = new LanBoardManifest { BuiltTrack = BuiltTrack ?? "", BuiltSignature = TrackSignature.LooksValid(BuiltSignature) ? BuiltSignature : "" };
        if (m.BuiltSignature.Length == 0) m.BuiltTrack = "";
        var infos = Archive.List().ToDictionary(i => i.Id);
        int pending = 0;
        foreach (string track in AllTracks)
        {
            var paths = RecordStore.PathsOnly(Dir, track);
            bool built = track == BuiltTrack;
            string sig = built ? m.BuiltSignature : paths.SavedSignatureOrNull();
            if (string.IsNullOrEmpty(sig)) continue;
            if (!built && paths.StoredLayout() != RecordStore.Layout) continue;
            if (built && paths.SavedSignatureOrNull() is string saved && saved != sig) continue;
            var sets = new List<(int, IReadOnlyList<LanBoardRow>)>();
            for (int set = 0; set < RecordStore.Sets; set++)
            {
                var board = BoardOf(track, set);
                if (board.Entries.Count == 0) continue;
                var rows = new List<LanBoardRow>();
                foreach (var e in board.Entries.Take(LanBoardManifest.Capacity(set)))
                {
                    string name = Leaderboard.CleanName(e.Name);
                    if (name.Length == 0 || !float.IsFinite(e.Time) || e.Time <= 0 || e.Time > LanBoardManifest.MaxTime) continue;
                    int power = set == 1 ? Math.Clamp(e.Power, 0, 2000) : 0;
                    string date = e.Date ?? "";
                    if (!(date.Length == 0 || date.Length == 10 && date[4] == '-' && date[7] == '-')) date = "";
                    var row = new LanBoardRow(name, e.Time, date, power, e.Pad, e.Night, e.Boost);
                    if (e.RecordingId != null && infos.TryGetValue(e.RecordingId, out var info))
                    {
                        if (!info.Retained || info.InMemory) pending++;      // the board's own retain is still running: offered next time
                        else if (info.Openable && info.State == LapEntryState.Ready && info.Meta.TrackId == track && info.Meta.Set == set
                            && Math.Abs(info.Meta.LapSeconds - e.Time) <= .001f && info.Meta.TrackSignature == sig && info.Meta.Layout == RecordStore.Layout)
                        {
                            long bytes = info.SizeBytes + LanBoardPayload.MaxMeta + 512;
                            if (bytes <= LanBoardPayload.MaxBytes) row = row with { RecordingId = e.RecordingId, Fingerprint = info.Meta.GhostFingerprint, RecordingBytes = (int)bytes };
                        }
                    }
                    rows.Add(row);
                }
                if (rows.Count > 0) sets.Add((set, rows));
            }
            if (sets.Count > 0) m.Tracks.Add(new LanBoardTrack(track, sig, sets));
        }
        m.Pending = pending;
        if (pending > 0) note = $"{pending} OF YOUR RECORDINGS ARE STILL SAVING - RESYNC LATER";
        return m;
    }

    // ------------------------------------------------------------ merging an offer

    public LanBoardMergeResult Merge(LanBoardManifest offer, long origin, IReadOnlyDictionary<string, string> knownCurrent)
    {
        var res = new LanBoardMergeResult();
        bool any = false;
        foreach (var t in offer.Tracks)
        {
            var paths = RecordStore.PathsOnly(Dir, t.Track);
            bool built = t.Track == BuiltTrack, fresh = false;
            if (!built && Godot.FileAccess.FileExists(paths.LayoutPath) && paths.StoredLayout()!=RecordStore.Layout)
            { res.Skipped.Add($"{t.Track}: old record layout here"); continue; }
            string reference;
            if (built) reference = BuiltSignature;
            else if (paths.SavedSignatureOrNull() is string saved)
            {
                if (paths.StoredLayout() != RecordStore.Layout) { res.Skipped.Add($"{t.Track}: old record layout here"); continue; }
                reference = saved;
            }
            else if (paths.HasAnyRecordFile()) { res.Skipped.Add($"{t.Track}: records here have no signature"); continue; }
            else {
                string currentShape=_freshSignature(t.Track);
                if(currentShape!=t.Signature) {res.Skipped.Add($"{t.Track}: saved shape is not the current track");continue;}
                reference=currentShape;fresh=true;
            }
            if (knownCurrent != null && knownCurrent.TryGetValue(t.Track, out var current) && current != reference) { res.Skipped.Add($"{t.Track}: old track shape"); continue; }
            if (t.Signature != reference) { res.Skipped.Add($"{t.Track}: signature mismatch"); continue; }
            bool trackChanged = false;
            foreach (var (set, rows) in t.Sets)
            {
                var board = BoardOf(t.Track, set);
                if (board.Capacity != LanBoardManifest.Capacity(set)) { res.Skipped.Add($"{t.Track} set {set}: capacity"); continue; }
                var before = board.Entries.ToList();
                int added = 0, linked = 0;
                foreach (var r in rows)
                {
                    int rank = board.MergeRow(r.Name, r.Time, r.Date, r.Power, r.Pad, r.Night, r.Boost);
                    if (rank > 0) added++; else if (rank < 0) res.Duplicates++;
                }
                // recordings: rows on the board without one get it from here if this exact lap is already kept, else it is fetched
                foreach (var r in rows)
                {
                    if (r.RecordingId == null) continue;
                    var e = board.Find(r.Key);
                    if (e == null || e.RecordingId != null) continue;
                    if (Archive.FindRecording(t.Track, set, e.Time, r.Fingerprint) is string have) { if (board.AttachRecording(r.Key, have)) linked++; continue; }
                    res.Wants.Add(new LanBoardWant(origin, t.Track, set, t.Signature, r.Key, e.Time, r.RecordingId, r.Fingerprint, r.RecordingBytes));
                }
                if (added == 0 && linked == 0 && before.SequenceEqual(board.Entries)) continue;
                // removed rows were pushed off by faster ones; nothing else may change
                string err = Persist(board, before);
                if (err != null) { res.Error = err; res.Wants.RemoveAll(w => w.Track == t.Track && w.Set == set); continue; }
                res.RowsAdded += added; res.LinkedLocal += linked; res.BoardsChanged++; trackChanged = any = true;
            }
            if (trackChanged && (fresh || built)) MarkTrack(paths, reference);
        }
        if (any) _changed?.Invoke();
        return res;
    }

    /// <summary>Backup (once per session per file), then the verified save. On failure the board goes back to what its file holds.</summary>
    string Persist(Leaderboard board, List<Leaderboard.Entry> before)
    {
        string err = Backup(board.Path) ?? board.SaveAtomic();
        if (err != null) Restore(board, before);
        return err;
    }

    static void Restore(Leaderboard board, List<Leaderboard.Entry> before)
    {
        // back to the exact rows it had (the live instance must not keep unsaved rows a later solo save would write)
        board.ReplaceEntries(before);
    }

    string Backup(string path)
    {
        try
        {
            string real = Real(path);
            if (!IOFile.Exists(real) || !_backedUp.Add(real)) return null;
            string dir = Path.Combine(Real(Dir), "lan-board-backups", BackupStamp());
            Directory.CreateDirectory(dir);
            string to = Path.Combine(dir, Path.GetFileName(real));
            if (!IOFile.Exists(to)) IOFile.Copy(real, to);
            return null;
        }
        catch (Exception e) { _backedUp.Remove(Real(path)); return "could not back up " + Path.GetFileName(path) + ": " + e.Message; }
    }

    /// <summary>
    /// A track whose boards were just written on a signature that was not saved yet (the built track, or a track with no record files at
    /// all here): its signature and record layout files are written so the boards count as made on that shape. An existing file is never
    /// changed, so old records are never marked current; if the shape was stale, the next build archives them as always (never deletes).
    /// </summary>
    static void MarkTrack(RecordStore paths, string signature)
    {
        try
        {
            string sig = Real(paths.SignaturePath), layout = Real(paths.LayoutPath);
            if (!IOFile.Exists(layout)) IOFile.WriteAllText(layout, RecordStore.Layout + "\n");
            if (!IOFile.Exists(sig)) IOFile.WriteAllText(sig, signature + "\n");
        }
        catch (Exception e) { GD.PushWarning("LAN boards: could not mark " + paths.TrackId + ": " + e.Message); }
    }

    // ------------------------------------------------------------ recordings

    public bool StillWanted(LanBoardWant w)
    {
        var e = BoardOf(w.Track, w.Set).Find(w.RowKey);
        return e != null && e.RecordingId == null;
    }

    public Task<byte[]> ReadPayload(string recordingId)
    {
        string id = Leaderboard.CleanRecordingId(recordingId) ?? throw new InvalidDataException("bad recording id");
        var archive = Archive;
        return Task.Run(() =>
        {
            var info = archive.List().FirstOrDefault(i => i.Id == id);
            if (info == null || !info.Retained || info.InMemory) throw new InvalidDataException("the recording is not saved here");
            var src = archive.Open(id, out string reason) ?? throw new InvalidDataException(reason ?? "cannot open");
            var m = src.Meta;
            var meta = new LapMeta
            {
                Id = id, TrackId = m.TrackId, Set = m.Set, Mode = m.Mode, LapSeconds = m.LapSeconds, LapNumber = m.LapNumber, Kind = m.Kind, Ranked = m.Ranked, RecordedUtc = m.RecordedUtc,
                DayClock = m.DayClock, HasTape = src.HasTape, GhostFingerprint = m.GhostFingerprint, Profile = "", TrackSignature = m.TrackSignature, Layout = m.Layout,
                Name = "", Retained = false, TotalBytes = src.GhostBytes.Length + src.TapeBytes.Length, Fuels = m.Fuels,
            };
            return LanBoardPayload.Pack(id, Encoding.UTF8.GetBytes(meta.ToJson()), src.GhostBytes.Span, src.HasTape ? src.TapeBytes.Span : ReadOnlySpan<byte>.Empty);
        });
    }

    public Task<LanBoardStage> Stage(LanBoardWant want, byte[] payload)
    {
        var archive = Archive;
        bool clean = !_cleaned; _cleaned = true;
        return Task.Run(() =>
        {
            if (clean) archive.CleanIncoming();
            var stage = new LanBoardStage { Want = want };
            try
            {
                var (id, meta, ghost, tape) = LanBoardPayload.Unpack(payload);
                if (id != want.RecordingId) { stage.Error = "another recording came"; return stage; }
                string name = want.RowKey.Split('|')[0];
                var s = archive.StageImport(id, meta, ghost, tape, new LapArchive.ImportExpect(want.Track, want.Set, want.Time, want.Signature, want.Fingerprint, $"LAN {name} {LapTimer.Format(want.Time)}"));
                stage.Error = s.Error; stage.StageDir = s.StageDir; stage.LocalId = s.LocalId; stage.ExistingId = s.ExistingId; stage.Archived = s;
            }
            catch (Exception e) { stage.Error = e.Message; }
            return stage;
        });
    }

    public string Commit(LanBoardStage stage)
    {
        string id = stage.ExistingId;
        if (id == null)
        {
            if (stage.Archived is not LapArchive.ImportStage s) return "nothing staged";
            string err = Archive.CommitImport(s);
            if (err != null) return err;
            id = s.LocalId;
        }
        var board = BoardOf(stage.Want.Track, stage.Want.Set);
        var before = board.Entries.ToList();
        if (!board.AttachRecording(stage.Want.RowKey, id)) return null;      // the row was linked meanwhile: the recording stays kept, unlinked
        string saveErr = Persist(board, before);
        if (saveErr == null) _changed?.Invoke();
        return saveErr;
    }

    public void Discard(LanBoardStage stage) { if (stage?.StageDir != null) Archive.DiscardImport(stage.StageDir); }
}
