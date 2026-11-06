using System;
using System.Buffers.Binary;
using CCCPRacing.Race;

namespace CCCPRacing.Replay;

/// <summary>
/// The lap, frozen on the main thread: plain bytes and a copied recording, no Godot object, nothing shared with the live ghost or recorder.
/// The archive takes ownership of the arrays at Submit; a capture can be submitted once.
/// </summary>
public sealed class LapCapture
{
    /// <summary>The ghost file's bytes (made on the calling thread), or null when <see cref="OwnedGhost"/> is given and the archive makes them on its worker.</summary>
    public byte[] GhostBytes { get; init; }
    /// <summary>A copy of the lap's ghost that nothing else touches from now on (the archive reads it on its worker): see <see cref="FromGhostCopy"/>.</summary>
    public Ghost OwnedGhost { get; init; }
    public ulong GhostFingerprint { get; init; }
    /// <summary>The copied recording, or null for a ghost-only lap.</summary>
    public WorldTapeRecorder.Recording Recording { get; init; }
    public string TrackId { get; init; } = "";
    public int Set { get; init; }
    public string Mode { get; init; } = "";
    public float LapSeconds { get; init; }
    public int LapNumber { get; set; }
    public LapKind Kind { get; init; }
    public bool Ranked { get; init; }
    public float DayClock { get; init; } = -1;
    public string Profile { get; init; } = "";
    public string TrackSignature { get; init; } = "";
    public int Layout { get; init; } = RecordStore.Layout;
    /// <summary>See <see cref="LapMeta.Fuels"/>.</summary>
    public string Fuels { get; set; } = "";
    public DateTime RecordedUtc { get; init; } = DateTime.UtcNow;
    internal int Used;

    /// <summary>
    /// The main-thread part of a capture, from the live objects: ghost.ToBytes(), its fingerprint and recorder.Snapshot() (the recorder is only read, never finished).
    /// <paramref name="lapGhost"/> is the lap's copy (rec.CopyWithTime(time)); LapSeconds is its LapTime. <paramref name="recorder"/> may be null (no world tape).
    /// </summary>
    public static LapCapture FromLive(Ghost lapGhost, WorldTapeRecorder recorder, string trackId, int set, string mode, LapKind kind, bool ranked, float dayClock, string profile, string trackSignature, DateTime? recordedUtc = null)
        => new()
        {
            GhostBytes = lapGhost.ToBytes(), GhostFingerprint = WorldTapeFingerprint.Of(lapGhost), Recording = recorder?.Snapshot(),
            TrackId = trackId, Set = set, Mode = mode, LapSeconds = lapGhost.LapTime, Kind = kind, Ranked = ranked, DayClock = dayClock, Profile = profile, TrackSignature = trackSignature,
            RecordedUtc = recordedUtc ?? DateTime.UtcNow,
        };

    /// <summary>
    /// The cheap main-thread capture: only the recorder snapshot (about 1 ms) and the hand-over of <paramref name="ownedCopy"/>; ghost.ToBytes (the damage compression, 160 ms on a 120 s lap) and the fingerprint
    /// run on the archive's worker. <paramref name="ownedCopy"/> must be a ghost copy made for the archive (rec.CopyWithTime(time)) that the caller never uses again: the worker only reads it, and the damage
    /// states it shares by reference are immutable records captured with fresh arrays.
    /// </summary>
    public static LapCapture FromGhostCopy(Ghost ownedCopy, WorldTapeRecorder recorder, string trackId, int set, string mode, LapKind kind, bool ranked, float dayClock, string profile, string trackSignature, DateTime? recordedUtc = null)
        => new()
        {
            OwnedGhost = ownedCopy, Recording = recorder?.Snapshot(),
            TrackId = trackId, Set = set, Mode = mode, LapSeconds = ownedCopy.LapTime, Kind = kind, Ranked = ranked, DayClock = dayClock, Profile = profile, TrackSignature = trackSignature,
            RecordedUtc = recordedUtc ?? DateTime.UtcNow,
        };
}

/// <summary>Reads what the archive needs from a ghost file's bytes without a Ghost object (so it works on any thread and on a file that was never loaded).</summary>
public static class LapGhostBytes
{
    const uint MagicV3 = 0x33434343, MagicV4 = 0x34434343;      // "CCC3" / "CCC4", the formats Ghost.ToBytes writes
    const int Header = 12, SampleBytes = 142;                    // magic, lap time, count; then 142 bytes per sample (pose, car state, 4 wheels, day clock)

    /// <summary>The ghost's <see cref="WorldTapeFingerprint"/>, computed from the file bytes exactly as Of(ghost) does after a load; null when the bytes are not a whole V3/V4 ghost file.</summary>
    public static ulong? Fingerprint(ReadOnlySpan<byte> b, out int samples, out float lapTime)
    {
        samples = 0; lapTime = -1;
        if (b.Length < Header) return null;
        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(b);
        if (magic != MagicV3 && magic != MagicV4) return null;
        lapTime = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(b[4..]));
        uint n = BinaryPrimitives.ReadUInt32LittleEndian(b[8..]);
        if (n == 0 || n > 100000) return null;
        long body = Header + (long)n * SampleBytes;
        if (b.Length < body) return null;
        if (magic == MagicV3) { if (b.Length != body) return null; }
        else if (b.Length < body + 4 || BinaryPrimitives.ReadInt32LittleEndian(b[(int)body..]) != b.Length - body - 4) return null;
        samples = (int)n;
        var h = TapeHash.Start().Add((int)n).Add(lapTime);
        for (int i = 0; i < n; i++)
        {
            int o = Header + i * SampleBytes;
            for (int k = 0; k < 7; k++) h = h.Add(BinaryPrimitives.ReadUInt32LittleEndian(b[(o + 4 * k)..]));      // position x y z, rotation x y z w, as stored
        }
        return h.Value;
    }
}
