using System;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using CCCPRacing.Race;

namespace CCCPRacing.Replay;

public enum LapKind { Normal, Record, Void }

public enum LapEntryState
{
    /// <summary>The ghost is ready; the world tape is still being compressed on the worker (Open waits for just this).</summary>
    Pending,
    /// <summary>Usable. A ghost-only lap (no world tape) is Ready too; its meta says HasTape = false.</summary>
    Ready,
    /// <summary>A worker step failed or the files are damaged; <see cref="LapEntryInfo.Message"/> says why. A recent lap with its ghost in memory is still openable.</summary>
    Failed,
    /// <summary>Saved by another record layout or a newer archive version: listed, never opened.</summary>
    Incompatible,
}

/// <summary>The one place for the memory policy. Sizes are from the design doc's measured production laps (ghost+tape together 1.30, 3.49, 2.62 and 3.40 MB; about 29 KB per second of lap).</summary>
public static class LapArchiveLimits
{
    /// <summary>Recent (not retained) laps kept in memory: the ten most recent (it was five; the owner found that too few).</summary>
    public const int MaxRecent = 10;
    /// <summary>
    /// Byte ceiling over all recent laps in memory (ghost bytes + tape bytes, or the raw recording while the tape is still being compressed).
    /// Ten laps of the longest measured track are about 35 MB; the ceiling leaves room for a pending raw recording and for laps twice as long as any track today.
    /// </summary>
    public const long MaxRecentBytes = 64L << 20;
    /// <summary>
    /// The largest single lap (ghost bytes + raw recording) that is archived. Set from the in-memory size of a real-size fixture
    /// (lap-archive-tests prints it) with a wide margin over any track today; a lap over it is refused visibly.
    /// </summary>
    public const long MaxLapBytes = 16L << 20;
    /// <summary>
    /// Process-wide byte cap over the recent laps of ALL shared archives together (one archive per profile folder, they live for the whole process, so a profile switch must not multiply the memory).
    /// Enforced when a capture is admitted: the oldest evictable recent lap of any archive goes first.
    /// </summary>
    public const long MaxProcessBytes = 96L << 20;
    /// <summary>Retains that may wait at once per archive (a lap with a retain waiting cannot be evicted, so waiting retains pin memory).</summary>
    public const int MaxPendingRetains = 2;
    /// <summary>Bytes the waiting retains may pin together (their laps' accounted size); a Retain beyond it is refused visibly. See the design doc for the resulting hard bound.</summary>
    public const long MaxPendingRetainBytes = 12L << 20;
    /// <summary>meta.json version written and the newest one this build understands.</summary>
    public const int MetaVersion = 1;
}

/// <summary>Everything about a lap that is not the recording itself. Saved as meta.json beside the pair.</summary>
public sealed record LapMeta
{
    public int Version { get; init; } = LapArchiveLimits.MetaVersion;
    public string Id { get; init; } = "";
    public string TrackId { get; init; } = "";
    /// <summary>Record set (RecordStore.SetOf): 0 rally petrol, 1 Stakhanov, 2 diesel, 3 rocket.</summary>
    public int Set { get; init; }
    /// <summary>Human label of the mode: the Rally fuel grade or "stakhanov".</summary>
    public string Mode { get; init; } = "";
    public float LapSeconds { get; init; }
    /// <summary>Completed lap number shown by the run; zero in recordings made before lap numbering.</summary>
    public int LapNumber { get; init; }
    public LapKind Kind { get; init; }
    public bool Ranked { get; init; }
    public DateTime RecordedUtc { get; init; }
    /// <summary>Day-night clock at the lap start, -1 for the plain day.</summary>
    public float DayClock { get; init; } = -1;
    public bool HasTape { get; init; }
    public ulong GhostFingerprint { get; init; }
    public string Profile { get; init; } = "";
    public string TrackSignature { get; init; } = "";
    /// <summary>RecordStore.Layout at the time of capture.</summary>
    public int Layout { get; init; }
    public string Name { get; init; } = "";
    public bool Retained { get; init; }
    public long TotalBytes { get; init; }
    /// <summary>Stakhanov laps: the fuel in use through the lap as "seconds:grade" pairs (RaceScene.LapFuels); empty for a rally lap (the set tells) and for laps from before it was kept.</summary>
    public string Fuels { get; init; } = "";

    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>Tolerant: unknown members are ignored and missing ones keep their defaults. False with a reason when the text is not a usable meta.</summary>
    public static bool TryParse(string json, out LapMeta meta, out string error)
    {
        meta = null; error = null;
        try
        {
            meta = JsonSerializer.Deserialize<LapMeta>(json, Options);
            if (meta == null) { error = "empty"; return false; }
            return true;
        }
        catch (Exception ex) { error = ex.Message; meta = null; return false; }
    }
}

/// <summary>An immutable lap: the metadata and the exact bytes of ghost.bin and tape.bin (the tape is null for a ghost-only lap).</summary>
public sealed class LapSource
{
    readonly byte[] _ghost, _tape;
    public LapMeta Meta { get; }
    /// <summary>Set when the lap opens in a reduced form (for example a damaged tape was dropped and the lap is ghost only).</summary>
    public string Warning { get; }
    public ReadOnlyMemory<byte> GhostBytes => _ghost;
    public ReadOnlyMemory<byte> TapeBytes => _tape;
    public bool HasTape => _tape != null;

    internal LapSource(LapMeta meta, byte[] ghost, byte[] tape, string warning = null)
    { Meta = meta; _ghost = ghost; _tape = tape; Warning = warning; }

    /// <summary>SHA-256 of the sources (ghost bytes, then tape bytes, each length-prefixed), lower-case hex. Metadata is not part of it.</summary>
    public string Sha256()
    {
        using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        h.AppendData(BitConverter.GetBytes((long)_ghost.Length)); h.AppendData(_ghost);
        h.AppendData(BitConverter.GetBytes(_tape == null ? -1L : _tape.Length)); if (_tape != null) h.AppendData(_tape);
        return Convert.ToHexString(h.GetHashAndReset()).ToLowerInvariant();
    }
}

public sealed record LapEntryInfo(LapMeta Meta, LapEntryState State, string Message, bool Retained, bool InMemory, long SizeBytes, bool Openable, string Dir,
    bool Pinned = false, bool HasTrim = false, bool HasCameraKeys = false, LapEditsState EditsState = LapEditsState.None)
{
    public string Id => Meta.Id;
}

public readonly record struct LapSubmitResult(bool Accepted, string Id, string Reason);

public readonly record struct RetainResult(bool Ok, string Message, string Dir);
