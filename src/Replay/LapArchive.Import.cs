using System;
using System.IO;
using System.Linq;
using System.Text;
using CCCPRacing.Race;

namespace CCCPRacing.Replay;

/// <summary>
/// LAN board sharing (docs/lan-leaderboard-sync.md): a comrade's leaderboard recording comes in as its meta.json, ghost.bin and tape.bin
/// bytes. It is verified exactly as <see cref="Open"/> verifies a retained lap, written to a staging folder beside the archive with the
/// retain's write discipline, and then moved in as one new retained lap. Nothing already in the archive is ever replaced: a clash of ids
/// gets a fresh local id.
/// </summary>
public sealed partial class LapArchive
{
    /// <summary>The staging folder (same parent, so the final move is a rename): "&lt;archive&gt;-incoming".</summary>
    public string IncomingDir => _dir + "-incoming";

    /// <summary>What an incoming recording must turn out to be (from the board row it belongs to).</summary>
    public readonly record struct ImportExpect(string TrackId, int Set, float LapSeconds, string TrackSignature, ulong GhostFingerprint, string Name);

    /// <summary>A verified recording in staging, or the reason it was refused. <see cref="ExistingId"/>: the archive already has this exact lap (nothing staged).</summary>
    public sealed record ImportStage(string StageDir, string LocalId, LapMeta Meta, string Error, string ExistingId = null)
    {
        public bool Ok => Error == null && StageDir != null;
    }

    /// <summary>A retained, readable lap that is this recording (same ghost fingerprint, track, set and time), or null.</summary>
    public string FindRecording(string trackId, int set, float lapSeconds, ulong fingerprint)
    {
        foreach (var i in List())
            if (i.Retained && i.Openable && i.State == LapEntryState.Ready && i.Meta.GhostFingerprint == fingerprint && i.Meta.TrackId == trackId
                && i.Meta.Set == set && Math.Abs(i.Meta.LapSeconds - lapSeconds) <= .001f)
            {
                // A readable metadata file alone does not prove the local source still exists intact.
                var source=Open(i.Id,out _);
                if(source!=null && source.Warning==null && (!i.Meta.HasTape || source.HasTape)) return i.Id;
            }
        return null;
    }

    /// <summary>
    /// Worker: verifies the parts against each other and against <paramref name="expect"/>, and writes them to a new staging folder
    /// (ghost.bin, tape.bin, track.sig, records.ver, meta.json last). Never touches the archive folder itself. Never throws.
    /// </summary>
    public ImportStage StageImport(string offeredId, byte[] metaJson, byte[] ghost, byte[] tape, ImportExpect expect)
    {
        string stage = null;
        try
        {
            string id = Leaderboard.CleanRecordingId(offeredId);
            if (id == null) return Refused("the recording id is not valid");
            if (metaJson == null || metaJson.Length > 16 * 1024) return Refused("meta.json is missing or too large");
            if (!LapMeta.TryParse(Encoding.UTF8.GetString(metaJson), out var m, out string err)) return Refused("meta.json unreadable: " + err);
            if (m.Version < 1 || m.Version > LapArchiveLimits.MetaVersion) return Refused($"meta version {m.Version}, this game reads {LapArchiveLimits.MetaVersion}");
            if (m.Layout != RecordStore.Layout) return Refused($"record layout {m.Layout}, this game uses {RecordStore.Layout}");
            if (m.Id != id) return Refused("meta.json belongs to another lap id");
            if (m.TrackId != expect.TrackId || m.Set != expect.Set) return Refused("the recording is of another track or record set");
            if (!float.IsFinite(m.LapSeconds) || Math.Abs(m.LapSeconds - expect.LapSeconds) > .001f) return Refused("the recording's lap time does not match the result");
            if (m.TrackSignature != expect.TrackSignature) return Refused("the recording was made on another track shape");
            if (ghost == null || ghost.Length == 0) return Refused("ghost.bin is missing");
            var fp = LapGhostBytes.Fingerprint(ghost, out _, out float ghostTime);
            if (fp == null) return Refused("ghost.bin is damaged");
            if (fp.Value != m.GhostFingerprint || fp.Value != expect.GhostFingerprint) return Refused("ghost.bin does not match meta.json");
            if (Math.Abs(ghostTime - m.LapSeconds) > .001f) return Refused("ghost.bin's lap time does not match meta.json");
            if (m.HasTape)
            {
                if (tape == null || tape.Length == 0) return Refused("tape.bin is missing");
                var t = WorldTapeCodec.Decode(tape);
                if (t == null || t.SampleCount < 0 || !WorldTapeCodec.Validate(t)) return Refused("tape.bin is damaged");
                if (t.GhostFingerprint != fp.Value) return Refused("tape.bin belongs to another ghost");
            }
            else if (tape != null && tape.Length > 0) return Refused("a tape came with a ghost-only lap");
            if (FindRecording(m.TrackId, m.Set, m.LapSeconds, fp.Value) is string have) return new ImportStage(null, have, m, null, have);

            string local = id;
            lock (_lock)
                for (int n = 0; Find(local) != null || Directory.Exists(Path.Combine(_dir, local)); n++)
                    local = $"{id[..Math.Min(id.Length, 100)]}-l{Rng.Next(0x10000):x4}";
            string name = CleanName(expect.Name);
            var final = new LapMeta
            {
                Id = local, TrackId = m.TrackId, Set = m.Set, Mode = Bound(m.Mode, 32), LapSeconds = m.LapSeconds, LapNumber = Math.Clamp(m.LapNumber,0,1000000), Kind = m.Kind, Ranked = m.Ranked,
                RecordedUtc = m.RecordedUtc, DayClock = m.DayClock, HasTape = m.HasTape, GhostFingerprint = fp.Value, Profile = "", TrackSignature = m.TrackSignature,
                Layout = m.Layout, Name = name, Retained = true, TotalBytes = ghost.Length + (m.HasTape ? tape.Length : 0), Fuels = Bound(m.Fuels, 512),
            };
            Directory.CreateDirectory(IncomingDir);
            stage = Path.Combine(IncomingDir, "stage-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);
            var ops = new BufferedOps(_ops);
            SaveFile(ops, Path.Combine(stage, "ghost.bin"), ghost);
            if (m.HasTape) SaveFile(ops, Path.Combine(stage, "tape.bin"), tape);
            SaveFile(ops, Path.Combine(stage, "track.sig"), Encoding.UTF8.GetBytes(final.TrackSignature + "\n"));
            SaveFile(ops, Path.Combine(stage, "records.ver"), Encoding.UTF8.GetBytes(final.Layout + "\n"));
            SaveFile(ops, Path.Combine(stage, "meta.json"), Encoding.UTF8.GetBytes(final.ToJson()));      // last, as in a retain
            if (FailImportForTest == "after-stage") { FailImportForTest = null; throw new IOException("test: after-stage"); }
            return new ImportStage(stage, local, final, null);
        }
        catch (Exception ex)
        {
            DiscardImport(stage);
            return Refused($"{ex.GetType().Name}: {ex.Message}");
        }

        static ImportStage Refused(string why) => new(null, null, null, why);
        static string Bound(string s, int max) { s ??= ""; s = new string(s.Where(c => !char.IsControl(c)).ToArray()); return s.Length > max ? s[..max] : s; }
    }

    /// <summary>Test seam: "after-stage" fails a stage after its files were written (cleanup), "before-move" fails a commit before the rename.</summary>
    internal static string FailImportForTest;

    /// <summary>
    /// Main thread: moves a staged recording into the archive under its local id (a rename that cannot replace anything), then lists it.
    /// Returns the error, null on success. A failed commit removes the staging folder.
    /// </summary>
    public string CommitImport(ImportStage s)
    {
        if (s == null || !s.Ok) return s?.Error ?? "nothing staged";
        string final = Path.Combine(_dir, s.LocalId);
        try
        {
            if (FailImportForTest == "before-move") { FailImportForTest = null; throw new IOException("test: before-move"); }
            Directory.CreateDirectory(_dir);
            lock (_lock)
            {
                if (Find(s.LocalId) != null || Directory.Exists(final)) throw new IOException("the recording id is taken");
                Directory.Move(s.StageDir, final);
                long size = new FileInfo(Path.Combine(final, "ghost.bin")).Length + (s.Meta.HasTape ? new FileInfo(Path.Combine(final, "tape.bin")).Length : 0);
                var e = new Entry { Dir = final, Retained = true, Meta = s.Meta, State = LapEntryState.Ready, Claim = 1, Size = size };
                e.Done.Set();
                _disk[s.LocalId] = e;
            }
            Say($"LAP ARCHIVE: imported {s.LocalId} from a comrade ({s.Meta.LapSeconds:F2} s, {s.Meta.TrackId} set {s.Meta.Set})");
            return null;
        }
        catch (Exception ex)
        {
            DiscardImport(s.StageDir);
            return ex.Message;
        }
    }

    /// <summary>Removes a staging folder (only one inside <see cref="IncomingDir"/>).</summary>
    public void DiscardImport(string stageDir)
    {
        try
        {
            if (string.IsNullOrEmpty(stageDir)) return;
            string full = Path.GetFullPath(stageDir), root = Path.GetFullPath(IncomingDir) + Path.DirectorySeparatorChar;
            if (full.StartsWith(root, StringComparison.Ordinal) && Directory.Exists(full)) Directory.Delete(full, true);
        }
        catch (Exception ex) { Say($"LAP ARCHIVE: could not remove the staging folder {stageDir}: {ex.Message}"); }
    }

    /// <summary>Removes what an interrupted session left in the staging folder (never anything in the archive itself).</summary>
    public void CleanIncoming()
    {
        try
        {
            if (!Directory.Exists(IncomingDir)) return;
            foreach (var d in Directory.GetDirectories(IncomingDir, "stage-*")) DiscardImport(d);
        }
        catch (Exception ex) { Say($"LAP ARCHIVE: could not clean {IncomingDir}: {ex.Message}"); }
    }
}
