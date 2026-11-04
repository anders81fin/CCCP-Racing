using System;
using CCCPRacing.Car;
using CCCPRacing.Race;
using CCCPRacing.Track;

namespace CCCPRacing.Replay;

public enum LapPlaybackError
{
    None,
    /// <summary>No lap, or its meta/ghost bytes are missing.</summary>
    NoSource,
    /// <summary>The ghost bytes are not a whole ghost file (truncated, damaged, unknown format); nothing can be played.</summary>
    GhostDamaged,
    /// <summary>The ghost's fingerprint is not the one the lap's meta records.</summary>
    GhostMismatch,
    /// <summary>The lap was recorded on another track layout than the one it is played on (only checked when the caller passes the expected track signature).</summary>
    TrackMismatch,
    /// <summary>The meta says the lap has a world tape but the source carries none.</summary>
    TapeMissing,
    /// <summary>The tape is not a tape file, is truncated, or its body cannot be played.</summary>
    TapeDamaged,
    /// <summary>The tape is a tape file of another format version (older or newer): it is not read.</summary>
    TapeLegacy,
    /// <summary>The tape was recorded against another ghost, track or record set than the lap's.</summary>
    TapePairMismatch,
}

/// <summary>A decoded and validated world tape of one lap, not yet bound to any scene. Immutable; <see cref="CreatePlayer"/> makes the playing object.</summary>
public sealed class LapTape
{
    internal LapTape(WorldTape tape, TapeBody body) { Tape = tape; Body = body; }
    public WorldTape Tape { get; }
    public TapeBody Body { get; }
    public int Samples => Tape.SampleCount;
    /// <summary>The length of the tape in seconds (samples at the tape rate, 15 Hz).</summary>
    public float Seconds => Tape.SampleCount / WorldTapeConst.TapeRate;
    public int Keyframes => Body.Keys.Count;

    /// <summary>The tape time of the keyframe a seek to <paramref name="clock"/> restores (the nearest at or before it).</summary>
    public float KeyframeBefore(float clock)
    {
        int i = Math.Clamp((int)MathF.Floor(clock * WorldTapeConst.TapeRate + 1e-3f), 0, Math.Max(0, Body.Samples.Count - 1));
        var kf = Body.Keys[0];
        foreach (var k in Body.Keys) if (k.Sample <= i) kf = k;
        return kf.Sample / WorldTapeConst.TapeRate;
    }

    /// <summary>
    /// A new, independent player of this tape against a viewer's own world: <paramref name="index"/> and <paramref name="sink"/> must belong to that
    /// world (the player covers an actor only when the index's structure equals the tape's). Main thread only (it reads nodes). The live scene's
    /// registry of tape slots is not involved. Call <see cref="WorldTapePlayer.Release"/> when the viewer closes.
    /// </summary>
    public WorldTapePlayer CreatePlayer(IWorldTapeIndex index, IWorldTapeSink sink) => new(Tape, index, sink, Body);
}

/// <summary>What <see cref="LapPlayback.Prepare"/> made. Everything in it is private to the caller: parsed from the source's bytes, shared with nothing.</summary>
public sealed class LapPlaybackResult
{
    /// <summary>The ghost, null only when <see cref="Error"/> is not None.</summary>
    public Ghost Ghost { get; init; }
    /// <summary>The world tape, null for a ghost-only lap and when <see cref="TapeError"/> is not None.</summary>
    public LapTape Tape { get; init; }
    /// <summary>Why no lap can be played at all (the ghost); None when the ghost is usable.</summary>
    public LapPlaybackError Error { get; init; }
    /// <summary>Why the tape cannot be used (the lap is then ghost only, the viewer decides whether to open it); None for a good tape and for a lap that has none.</summary>
    public LapPlaybackError TapeError { get; init; }
    public string Message { get; init; } = "";
    /// <summary>The lap's meta (copied from the source).</summary>
    public LapMeta Meta { get; init; }
    /// <summary>The ghost is usable (the lap can be shown, with or without a tape).</summary>
    public bool Ok => Error == LapPlaybackError.None && Ghost != null;
    /// <summary>The ghost is usable and a tape comes with it.</summary>
    public bool HasTape => Ok && Tape != null;
    /// <summary>Lap length in seconds: the ghost's (30 Hz samples); the tape covers the same lap on the 15 Hz grid.</summary>
    public float LapSeconds => Ghost?.Duration ?? 0f;
    /// <summary>The playing clock may jump anywhere in 0..LapSeconds: the ghost is absolute, and with a tape the player restores the nearest keyframe (<see cref="LapTape.KeyframeBefore"/>).</summary>
    public bool CanSeek => Ok && Ghost.Count > 0;
    /// <summary>The seek granularity of the tape: the world is exact at a keyframe and rebuilt from it by replaying the deltas in between. 0 without a tape.</summary>
    public float KeyframeSeconds => HasTape ? WorldTapeConst.KeyframeSeconds : 0f;
}

/// <summary>
/// The viewer's private playback inputs: a <see cref="Ghost"/> and a world tape made from a <see cref="LapSource"/>'s immutable bytes. Nothing here
/// touches the live scene's ghosts, best lap, tape registry or any record file; the source is only read.
///
/// <para><b>Threading.</b> <see cref="Prepare"/> uses managed code only (no Godot node or resource), so it may run on a worker thread; the result's
/// objects are the caller's alone. <see cref="LapTape.CreatePlayer"/> and every playing call are main thread.</para>
///
/// <para><b>What playback supports (checked against Ghost and WorldTapePlayer, not assumed):</b></para>
/// <list type="bullet">
/// <item>Forward play: <c>player.Apply(clock, dt)</c> and <c>ghost.Apply(clock, car, dt)</c> every frame.</item>
/// <item>Seek in either direction, any clock in 0..LapSeconds, without a rebuild. The tape player seeks by itself: <c>Apply</c> with a clock that goes backwards
///   or jumps more than <see cref="WorldTapeConst.SeekJump"/> forward calls <c>Seek</c> (nearest keyframe, every 5 s, then the deltas and events up to the clock; spawned
///   instances and effects are rebuilt); <c>Seek(clock)</c> may also be called explicitly. The ghost is absolute (pose, car state, damage), so any clock is valid; call
///   <c>ghost.Rewind()</c> before a jump so the hits between the two clocks are not replayed as bangs (Apply reports the hits passed since its last call).</item>
/// <item>Loop wrap: the clock goes back to 0, i.e. a backward jump: the tape player seeks to 0 by itself and the ghost needs <c>Rewind()</c> (the race scene does the same).
///   The tape's state at 0 is the lap start, not the end of the previous pass: a one-shot content (effects, breakables, spawns) is rebuilt, not carried over.</item>
/// <item>Speed: only the dt and the clock the caller supplies; the players know no time scale. A speed that makes a step longer than SeekJump seeks every frame.</item>
/// <item>A new <b>player</b> is needed when the viewer world is rebuilt (the player binds the index and sink at construction and caches nodes), after
///   <c>Release()</c> if the same world is reused (Release destroys what it made; the next Apply seeks), and for any other lap (a different source means a new Prepare).</item>
/// <item>A new <b>ghost</b> is not needed for seek or loop; only a different lap needs another Prepare.</item>
/// <item>Not supported: playing the tape beyond its last sample (the last state is held), playing it against a world whose actors differ from the tape's
///   (those actors are simply not covered and run live; <c>Covers</c> says which).</item>
/// </list>
/// </summary>
public static class LapPlayback
{
    /// <summary>
    /// Builds the ghost and the tape of <paramref name="src"/> for a viewer. Never throws. The ghost is validated against the meta's fingerprint; the tape is
    /// checked as <see cref="LapArchive.Open"/> does (decodes, validates, carries the ghost's fingerprint) and also against the meta's track and record set.
    /// A bad tape does not fail the lap: <see cref="LapPlaybackResult.TapeError"/> says why it is ghost only.
    /// </summary>
    /// <param name="ghostOnly">Only the ghost is read and checked (the tape is skipped, its fields stay empty): the quick answer to "can this lap be played at all".</param>
    /// <param name="expectedTrackSignature">The signature of the track the viewer is built on; when given, a lap recorded on another one is refused (TrackMismatch).</param>
    public static LapPlaybackResult Prepare(LapSource src, CarParams p, IGround ground, string expectedTrackSignature = null, bool ghostOnly = false)
    {
        LapMeta meta = src?.Meta;
        try
        {
            if (src == null || meta == null || src.GhostBytes.IsEmpty) return Fail(meta, LapPlaybackError.NoSource, "no lap data");
            if (expectedTrackSignature != null && meta.TrackSignature != expectedTrackSignature)
                return Fail(meta, LapPlaybackError.TrackMismatch, "the lap was recorded on another version of the track");
            var ghost = Ghost.FromBytes(src.GhostBytes.ToArray(), p, ground, out string why);
            if (ghost == null) return Fail(meta, LapPlaybackError.GhostDamaged, "the ghost data is damaged (" + why + ")");
            ulong fp = WorldTapeFingerprint.Of(ghost);
            if (fp != meta.GhostFingerprint) return Fail(meta, LapPlaybackError.GhostMismatch, "the ghost does not match the lap's record");

            if (ghostOnly) return new LapPlaybackResult { Ghost = ghost, Meta = meta };      // (a validation of the ghost alone: the tape is read when the lap is really prepared, and a bad tape never fails a lap)
            if (!src.HasTape)
                return new LapPlaybackResult { Ghost = ghost, Meta = meta, TapeError = meta.HasTape ? LapPlaybackError.TapeMissing : LapPlaybackError.None, Message = meta.HasTape ? "the world tape is missing, ghost only" : "" };
            var (tape, err, msg) = ReadTape(src.TapeBytes.ToArray(), meta, fp);
            return new LapPlaybackResult { Ghost = ghost, Tape = tape, Meta = meta, TapeError = err, Message = msg };
        }
        catch (Exception ex) { return Fail(meta, LapPlaybackError.GhostDamaged, $"{ex.GetType().Name}: {ex.Message}"); }
    }

    static LapPlaybackResult Fail(LapMeta meta, LapPlaybackError e, string msg) => new() { Error = e, Message = msg, Meta = meta };

    /// <summary>The tape of the bytes with the pairing checks, or the typed reason it cannot be played. Never throws.</summary>
    static (LapTape, LapPlaybackError, string) ReadTape(byte[] bytes, LapMeta meta, ulong ghostFingerprint)
    {
        try
        {
            if (bytes.Length < 8) return (null, LapPlaybackError.TapeDamaged, "the world tape is truncated, ghost only");
            uint magic = BitConverter.ToUInt32(bytes, 0); int version = BitConverter.ToInt32(bytes, 4);
            if (magic != WorldTapeConst.Magic) return (null, LapPlaybackError.TapeDamaged, "the world tape is not a tape file, ghost only");
            if (version != WorldTapeConst.FormatVersion) return (null, LapPlaybackError.TapeLegacy, $"the world tape is format {version}, this game plays {WorldTapeConst.FormatVersion}: ghost only");
            var t = WorldTapeCodec.Decode(bytes);
            if (t == null || t.SampleCount < 0 || !WorldTapeCodec.Validate(t)) return (null, LapPlaybackError.TapeDamaged, "the world tape is damaged, ghost only");
            if (t.GhostFingerprint != ghostFingerprint) return (null, LapPlaybackError.TapePairMismatch, "the world tape belongs to another ghost, ghost only");
            if (t.TrackId != meta.TrackId || t.Set != meta.Set) return (null, LapPlaybackError.TapePairMismatch, "the world tape was recorded on another track or record set, ghost only");
            var body = WorldTapePlayer.Validate(t);
            if (body == null) return (null, LapPlaybackError.TapeDamaged, "the world tape body cannot be played, ghost only");
            return (new LapTape(t, body), LapPlaybackError.None, "");
        }
        catch (Exception ex) { return (null, LapPlaybackError.TapeDamaged, $"the world tape cannot be read ({ex.Message}), ghost only"); }
    }
}
