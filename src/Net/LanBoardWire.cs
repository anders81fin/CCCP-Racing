using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CCCPRacing.Race;
using CCCPRacing.Track;

namespace CCCPRacing.Net;

/// <summary>One board row as another machine offers it (docs/lan-leaderboard-sync.md). A recording is offered only by its id, ghost fingerprint and size.</summary>
public sealed record LanBoardRow(string Name, float Time, string Date, int Power, bool Pad, bool Night, bool Boost, string RecordingId = null, ulong Fingerprint = 0, int RecordingBytes = 0)
{
    public string Key => Leaderboard.RowKey(Name, Time, Date, Power, Pad, Night, Boost);
}

/// <summary>A track's offered boards: the signature its records were saved on, then the rows of each record set.</summary>
public sealed record LanBoardTrack(string Track, string Signature, IReadOnlyList<(int Set, IReadOnlyList<LanBoardRow> Rows)> Sets);

/// <summary>
/// What a sharing comrade offers: every board of every track whose records carry a saved signature (times and names, and the ids of
/// the verified recordings attached to them), plus the track this machine has built and its current signature. Bounded wire format.
/// </summary>
public sealed class LanBoardManifest
{
    public const int MaxBytes = 32 * 1024, MaxTracks = 4;
    public const float MaxTime = 3600;
    public string BuiltTrack = "", BuiltSignature = "";
    /// <summary>Recordings not offered because they were still being saved on this machine.</summary>
    public int Pending;
    public readonly List<LanBoardTrack> Tracks = new();

    public int RowCount => Tracks.Sum(t => t.Sets.Sum(s => s.Rows.Count));
    public int RecordingCount => Tracks.Sum(t => t.Sets.Sum(s => s.Rows.Count(r => r.RecordingId != null)));

    public static int Capacity(int set) => set == 1 ? Leaderboard.StakhanovSize : Leaderboard.Size;

    public void Write(BinaryWriter w)
    {
        w.Write((byte)1);
        w.Write(BuiltTrack ?? ""); w.Write(BuiltSignature ?? "");
        w.Write((ushort)Math.Clamp(Pending, 0, ushort.MaxValue));
        w.Write((byte)Tracks.Count);
        foreach (var t in Tracks)
        {
            w.Write(t.Track); w.Write(t.Signature);
            w.Write((byte)t.Sets.Count);
            foreach (var (set, rows) in t.Sets)
            {
                w.Write((byte)set); w.Write((byte)rows.Count);
                foreach (var r in rows)
                {
                    w.Write(r.Name); w.Write(r.Time); w.Write(r.Date ?? ""); w.Write((ushort)Math.Clamp(r.Power, 0, ushort.MaxValue));
                    w.Write((byte)((r.Pad ? 1 : 0) | (r.Night ? 2 : 0) | (r.Boost ? 4 : 0) | (r.RecordingId != null ? 8 : 0)));
                    if (r.RecordingId != null) { w.Write(r.RecordingId); w.Write(r.Fingerprint); w.Write(r.RecordingBytes); }
                }
            }
        }
    }

    public byte[] ToBytes()
    {
        using var ms = new MemoryStream(); using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, true)) Write(w);
        return ms.ToArray();
    }

    static string ReadShort(BinaryReader r, int max)
    {
        string s = r.ReadString();
        if (s.Length > max) throw new InvalidDataException("board text too long");
        return s;
    }

    static bool IsDate(string d) => d.Length == 0 || d.Length == 10 && d[4] == '-' && d[7] == '-' && d.Where((c, i) => i != 4 && i != 7).All(char.IsAsciiDigit);

    /// <summary>Reads and checks a whole manifest; any violation throws InvalidDataException (nothing is half-read into the caller's state).</summary>
    public static LanBoardManifest Read(byte[] bytes)
    {
        if (bytes == null || bytes.Length > MaxBytes) throw new InvalidDataException("board offer size");
        using var r = new BinaryReader(new MemoryStream(bytes));
        var m = Read(r);
        if (r.BaseStream.Position != r.BaseStream.Length) throw new InvalidDataException("board offer trailing bytes");
        return m;
    }

    public static LanBoardManifest Read(BinaryReader r)
    {
        if (r.ReadByte() != 1) throw new InvalidDataException("board offer version");
        var m = new LanBoardManifest { BuiltTrack = ReadShort(r, 16), BuiltSignature = ReadShort(r, 16) };
        if (m.BuiltTrack.Length > 0 && (!TrackIds.Known(m.BuiltTrack) || !TrackSignature.LooksValid(m.BuiltSignature))) throw new InvalidDataException("board offer built track");
        m.Pending = r.ReadUInt16();
        int tn = r.ReadByte();
        if (tn > MaxTracks) throw new InvalidDataException("board offer track count");
        var seenTracks = new HashSet<string>();
        for (int i = 0; i < tn; i++)
        {
            string track = ReadShort(r, 16), sig = ReadShort(r, 16);
            if (!TrackIds.Known(track) || !seenTracks.Add(track) || !TrackSignature.LooksValid(sig)) throw new InvalidDataException("board offer track");
            int sn = r.ReadByte();
            if (sn > RecordStore.Sets) throw new InvalidDataException("board offer set count");
            var sets = new List<(int, IReadOnlyList<LanBoardRow>)>(); var seenSets = new HashSet<int>();
            for (int s = 0; s < sn; s++)
            {
                int set = r.ReadByte(), rn = r.ReadByte();
                if (set >= RecordStore.Sets || !seenSets.Add(set) || rn > Capacity(set)) throw new InvalidDataException("board offer set");
                var rows = new List<LanBoardRow>(rn);
                for (int k = 0; k < rn; k++)
                {
                    string name = ReadShort(r, 64); float time = r.ReadSingle(); string date = ReadShort(r, 10); int power = r.ReadUInt16(); int flags = r.ReadByte();
                    if (Leaderboard.CleanName(name) != name || name.Length == 0) throw new InvalidDataException("board offer name");
                    if (!float.IsFinite(time) || time <= 0 || time > MaxTime) throw new InvalidDataException("board offer time");
                    if (!IsDate(date)) throw new InvalidDataException("board offer date");
                    if (set != 1 ? power != 0 : power > 2000) throw new InvalidDataException("board offer power");
                    if ((flags & ~15) != 0) throw new InvalidDataException("board offer flags");
                    string id = null; ulong fp = 0; int size = 0;
                    if ((flags & 8) != 0)
                    {
                        id = ReadShort(r, 128); fp = r.ReadUInt64(); size = r.ReadInt32();
                        if (Leaderboard.CleanRecordingId(id) != id || size <= 0 || size > LanBoardPayload.MaxBytes) throw new InvalidDataException("board offer recording");
                    }
                    rows.Add(new LanBoardRow(name, time, date, power, (flags & 1) != 0, (flags & 2) != 0, (flags & 4) != 0, id, fp, size));
                }
                sets.Add((set, rows));
            }
            m.Tracks.Add(new LanBoardTrack(track, sig, sets));
        }
        return m;
    }
}

/// <summary>
/// One recording on the wire: a whitelisted meta.json (no profile, no paths, no edits) and the exact ghost.bin / tape.bin bytes.
/// Only ever produced from a verified <see cref="CCCPRacing.Replay.LapSource"/>; the receiver verifies everything again.
/// </summary>
public static class LanBoardPayload
{
    const uint Magic = 0x52424343;      // "CCBR"
    public const int MaxBytes = 12 << 20, MaxMeta = 16 * 1024;

    public static byte[] Pack(string id, byte[] meta, ReadOnlySpan<byte> ghost, ReadOnlySpan<byte> tape)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, true))
        {
            w.Write(Magic); w.Write((byte)1); w.Write(id);
            w.Write(meta.Length); w.Write(meta);
            w.Write(ghost.Length); w.Write(ghost);
            w.Write(tape.Length); w.Write(tape);
        }
        if (ms.Length > MaxBytes) throw new InvalidDataException($"recording too large to share ({ms.Length / 1048576.0:F1} MB)");
        return ms.ToArray();
    }

    /// <summary>Splits a payload; throws InvalidDataException on any size or framing problem. The tape is null when there is none.</summary>
    public static (string Id, byte[] Meta, byte[] Ghost, byte[] Tape) Unpack(byte[] bytes)
    {
        if (bytes == null || bytes.Length > MaxBytes) throw new InvalidDataException("recording size");
        using var r = new BinaryReader(new MemoryStream(bytes));
        if (r.ReadUInt32() != Magic || r.ReadByte() != 1) throw new InvalidDataException("not a shared recording");
        string id = r.ReadString();
        if (Leaderboard.CleanRecordingId(id) != id) throw new InvalidDataException("recording id");
        byte[] Part(int max)
        {
            int n = r.ReadInt32();
            if (n < 0 || n > max || n > r.BaseStream.Length - r.BaseStream.Position) throw new InvalidDataException("recording part size");
            return r.ReadBytes(n);
        }
        var meta = Part(MaxMeta); var ghost = Part(MaxBytes); var tape = Part(MaxBytes);
        if (r.BaseStream.Position != r.BaseStream.Length) throw new InvalidDataException("recording trailing bytes");
        if (meta.Length == 0 || ghost.Length == 0) throw new InvalidDataException("recording parts missing");
        return (id, meta, ghost, tape.Length == 0 ? null : tape);
    }
}

/// <summary>A recording this machine wants from a comrade: which row of which board it belongs to, and what it must turn out to be.</summary>
public sealed record LanBoardWant(long Origin, string Track, int Set, string Signature, string RowKey, float Time, string RecordingId, ulong Fingerprint, int Bytes);

/// <summary>What a merge did: rows put on boards, boards skipped and why, and the recordings still to fetch.</summary>
public sealed class LanBoardMergeResult
{
    public int RowsAdded, BoardsChanged, LinkedLocal, Duplicates;
    public readonly List<string> Skipped = new();
    public readonly List<LanBoardWant> Wants = new();
    public string Error;
}

/// <summary>A recording verified and written to a staging folder on the worker; the main thread commits it into the archive or discards it.</summary>
public sealed class LanBoardStage
{
    public LanBoardWant Want;
    public string StageDir, LocalId, Error;
    public bool Ok => Error == null && StageDir != null;
    /// <summary>The same recording was already here (same fingerprint, track, set and time): nothing was staged; the row links to this id.</summary>
    public string ExistingId;
    /// <summary>The archive's own staging record (LapArchive.ImportStage).</summary>
    public object Archived;
}

/// <summary>The machine's records as LAN board sharing sees them (implemented by LanBoardStore for the running RaceScene).</summary>
public interface ILanBoardStore
{
    /// <summary>The offer of this machine's boards (main thread). Note: a short visible explanation (e.g. recordings still saving), or null.</summary>
    LanBoardManifest BuildManifest(out string note);
    /// <summary>Merges a comrade's offer into the boards (main thread, small files); returns what changed and the recordings to fetch.</summary>
    LanBoardMergeResult Merge(LanBoardManifest offer, long origin, IReadOnlyDictionary<string, string> knownCurrent);
    /// <summary>The want's row is still on its board without a recording (so fetching it is still worth it).</summary>
    bool StillWanted(LanBoardWant want);
    /// <summary>Reads and verifies an offered recording on a worker: the payload, or an exception.</summary>
    System.Threading.Tasks.Task<byte[]> ReadPayload(string recordingId);
    /// <summary>Verifies a received payload against the want and writes it to staging, on a worker.</summary>
    System.Threading.Tasks.Task<LanBoardStage> Stage(LanBoardWant want, byte[] payload);
    /// <summary>Main thread: moves a staged recording into the archive and links its row. Returns the error, null on success.</summary>
    string Commit(LanBoardStage stage);
    /// <summary>Removes a staged recording that will not be committed.</summary>
    void Discard(LanBoardStage stage);
}
