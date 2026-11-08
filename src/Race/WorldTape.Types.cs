using System;
using System.Collections.Generic;
using Godot;

namespace CCCPRacing.Race;

// Shared types and interfaces of the world tape (docs/replay-tape.md, section 9). Compiled, behaviour-free:
// T1 (recorder), T2 (storage), T3 (player) and T4 (events) build on exactly these, so parallel work cannot drift apart.

/// <summary>Constants every part of the tape agrees on.</summary>
public static class WorldTapeConst
{
    /// <summary>A tape sample is taken every second ghost sample (the ghost grid is 30 Hz): 15 Hz.</summary>
    public const int TapeStride = 2;
    public const float TapeRate = Ghost.SampleRate / TapeStride;
    /// <summary>A full keyframe at lap start and then every this many tape samples (5 s).</summary>
    public const int KeyframeSamples = 75;
    public const float KeyframeSeconds = KeyframeSamples / TapeRate;
    /// <summary>Sound state is sampled this often (changed-only).</summary>
    public const float SoundRate = 10f;
    /// <summary>A clock jump bigger than this (or any step backwards) is a Seek, not a frame step.</summary>
    public const float SeekJump = 2f;
    /// <summary>Node metadata: the node is a tape part of its actor (the value is the part name).</summary>
    public const string PartMeta = "tape_part";
    /// <summary>Node metadata: the spawned model is a tape instance of this kind (the value is the kind).</summary>
    public const string KindMeta = "tape_kind";
    /// <summary>Node metadata: the actor's own structure version (an int; bump it when its builder changes meaning).</summary>
    public const string VersionMeta = "tape_ver";
    public const uint Magic = 0x31544343;     // "CCT1"
    public const int FormatVersion = 1;
}

/// <summary>A recorded part: its actor and its name within the actor ("root", "turret", "wheel_fl", "2/wheel_fl"...).</summary>
public readonly record struct TapePartId(string ActorKey, string Part)
{
    public override string ToString() => $"{ActorKey}/{Part}";
}

/// <summary>A part's pose at one instant. Local transform, as the node holds it.</summary>
public readonly record struct TapePose(Transform3D Local, bool Visible);

/// <summary>A sound node's recorded property. One node can carry several (a diesel has Speed and Dozing).</summary>
public enum TapeSoundChannel : byte
{
    DieselSpeed, DieselDozing, RotorLevel, JetLevel, JetAfterburner, GunFiring, AircraftState,
    // Appended IDs; existing channels and their wire ranges stay unchanged.
    FuelPouring, FuelLevel, FuelEnvelope,
    PlayerBoostLeft, PlayerPower, PlayerOverdrive, PlayerGolden,
    /// <summary>The slurry tractor's spray: 0 off, 1 spreading, 2 spreading in the grandmother's revenge (the replay shows the fan and keeps the revenge's mark).</summary>
    SlurrySpray,
}

/// <summary>A recorded sound value: the part (or the actor root) that owns the sound node, and which property.</summary>
public readonly record struct TapeSoundKey(TapePartId Part, TapeSoundChannel Channel);

/// <summary>
/// How each sound property becomes a byte and back (linear, clamped). The ranges are part of the file format:
/// changing one needs a new <see cref="WorldTapeConst.FormatVersion"/>.
/// </summary>
public static class TapeSoundRange
{
    public static (float Min, float Max) Of(TapeSoundChannel c) => c switch
    {
        TapeSoundChannel.DieselSpeed => (0f, 40f),          // m/s; the fastest rally vehicle is about 35
        TapeSoundChannel.DieselDozing => (0f, 1f),
        TapeSoundChannel.RotorLevel => (0f, 1f),
        TapeSoundChannel.JetLevel => (0f, 1f),
        TapeSoundChannel.JetAfterburner => (0f, 1f),
        TapeSoundChannel.GunFiring => (0f, 1f),
        TapeSoundChannel.AircraftState => (0f, 1f),         // k / 3: 0 powered, 1 landing light on, 2 unpowered (falling), 3 wreck (squashed)
        TapeSoundChannel.PlayerBoostLeft => (0f, 20f),
        TapeSoundChannel.PlayerPower => (0f, 5f),
        TapeSoundChannel.PlayerOverdrive => (0f, 1f),
        TapeSoundChannel.PlayerGolden => (0f, 1f),
        TapeSoundChannel.FuelPouring => (0f, 1f),
        TapeSoundChannel.FuelLevel => (0f, 1f),
        TapeSoundChannel.FuelEnvelope => (0f, 1f),
        TapeSoundChannel.SlurrySpray => (0f, 2f),
        _ => (0f, 1f),
    };
    public static byte ToByte(TapeSoundChannel c, float v) { var (lo, hi) = Of(c); return (byte)Math.Clamp((int)MathF.Round((v - lo) / (hi - lo) * 255f), 0, 255); }
    public static float FromByte(TapeSoundChannel c, byte b) { var (lo, hi) = Of(c); return lo + b / 255f * (hi - lo); }
}

/// <summary>
/// A part that turns continuously (a rotor, a wheel). The owner publishes its real, unwrapped angle (radians, growing
/// without limit) about <see cref="Axis"/> in the part's local frame; the recorder stores that, never a guess made from
/// sparse poses. Registered with <c>Tape.Spinner(node, spinner)</c> by the actor's builder.
/// </summary>
public interface ITapeSpinner
{
    Vector3 Axis { get; }
    float Angle { get; }
}

/// <summary>
/// What makes an actor's recorded parts mean the same thing later (section 4.3): the actor's own structure version
/// (bumped by hand when its builder changes in a way a part list cannot show), its part list, the hierarchy of every
/// part (its parent path from the actor root), and the rest pose of every part, local and relative to the actor root.
/// </summary>
public sealed class TapeActorStructure
{
    public int Version;
    public uint Hash;                                          // FNV-1a over Version, sorted part ids, class names, parent paths and quantised rest poses
    public Dictionary<string, string> ParentPaths = new();     // by part name: child indices and class names from the actor root, e.g. "0:Node3D/3:Node3D"
    public Dictionary<string, Transform3D> RestPoses = new(); // by part name, local
    public Dictionary<string, Transform3D> RestInRoot = new(); // by part name, relative to the actor root
}

public enum TapeEventType : byte { Spawn, Despawn, Projectile, Effect }

/// <summary>What a one-shot effect is (the sink maps each to the same function live play calls).</summary>
public enum TapeEffect : byte
{
    MuzzleBlast, ShellImpact, Explosion, CannonBurst, JetCrash, BreakableBroken, BabushkaLaunch, Smoke,
    /// <summary>Phase 3: the Hind run-in warning lamps (Pos and Dir are the two ends).</summary>
    RunWarning,
    /// <summary>Phase 3: a piece of a collided aircraft, thrown and falling by the same formula as live play (Pos, Velocity, Dir = spin, Param = which).</summary>
    Debris,
    /// <summary>Phase 3: a missile leaving its rack (the sound and the smoke ring).</summary>
    Launch,
    /// <summary>Phase 3: a marker, nothing to show: the lap was recorded by a build that records the breakables (the replay then does not guess them from the car).</summary>
    Marker,
}

/// <summary>What a projectile is (parametric: start, velocity, gravity, life; no per-frame samples).</summary>
public enum TapeProjectile : byte { Shell, Missile, Tracer }

/// <summary>
/// One entry of the event log. Which fields are used depends on <see cref="Type"/>:
/// Spawn (Kind, Parent, Instance), Despawn (Instance), Projectile (Projectile, Pos, Velocity, Gravity, Life, Instance),
/// Effect (Effect, Pos, Dir, Param, Life). <see cref="Source"/> is the actor key the event came from: when that actor is
/// not covered (its structure no longer matches), its events are dropped with its poses.
/// Guided missiles are NOT projectile events: they are dynamic instances (Spawn plus sampled poses).
/// </summary>
public readonly record struct TapeEvent(
    float Time, TapeEventType Type,
    string Source = null, string Kind = null, string Parent = null, int Instance = 0,
    TapeEffect Effect = default, TapeProjectile Projectile = default,
    Vector3 Pos = default, Vector3 Dir = default, Vector3 Velocity = default,
    float Gravity = 0f, float Param = 0f, float Life = 0f)
{
    public float End => Time + Life;
}

/// <summary>Everything that exists at one instant: the state a seek restores.</summary>
public sealed class TapeKeyframe
{
    public int Sample;                                         // tape sample index
    public Dictionary<TapePartId, TapePose> Poses = new();
    public Dictionary<TapeSoundKey, byte> Sounds = new();
    public List<TapeEvent> LiveInstances = new();              // Spawn events of instances not yet despawned
    public List<TapeEvent> LiveEffects = new();                // events with Life still running
    public HashSet<int> BrokenBreakables = new();              // obstacle ids
}

/// <summary>The recorded tape in memory. <see cref="Payload"/> is opaque to everyone but the codec.</summary>
public sealed class WorldTape
{
    public string TrackId;
    public int Set;
    public float LapTime;
    public int SampleCount;                                    // tape samples
    public ulong GhostFingerprint;                             // of the ghost this was recorded with (section 8)
    public Dictionary<string, TapeActorStructure> ActorStructure = new();    // per actor (section 4.3)
    public byte[] Payload = Array.Empty<byte>();
}

/// <summary>The parts of the world that can be recorded and posed, found by <see cref="TapePartId"/> (T1 implements it).</summary>
public interface IWorldTapeIndex
{
    int Count { get; }
    TapePartId IdOf(int part);
    Node3D NodeOf(int part);
    bool TryFind(TapePartId id, out int part);
    IEnumerable<string> ActorKeys { get; }
    /// <summary>The actor's version, part list and rest poses as they are now (section 4.3).</summary>
    TapeActorStructure StructureOf(string actorKey);
    /// <summary>Registers a dynamic instance's parts (Spawn); returns the first part number.</summary>
    int AddInstance(string actorKey, int instance, Node3D root);
    void RemoveInstance(int instance);
}

public interface IWorldTapeRecorder
{
    /// <summary>Lap start: bind the index, write the initial keyframe.</summary>
    void Begin(IWorldTapeIndex index);
    /// <summary>Every physics tick of a lap; samples on the tape grid; a keyframe every <see cref="WorldTapeConst.KeyframeSamples"/>.</summary>
    void Record(float lapClock);
    void Event(in TapeEvent e);
    void Clear();
    /// <summary>
    /// The safety net (section 4.4): how many samples saw a part whose rotation jumped by more than 25 degrees and that is not a
    /// registered spinner. The phase-1 actors must keep this at 0.
    /// </summary>
    int UnregisteredSpinners { get; }
    /// <summary>Samples that saw a part whose scale left its rest scale by more than 1 % (section 4.1): the tape cannot reproduce that. The phase-1 actors must keep this at 0.</summary>
    int UnscaledChanges { get; }
    /// <summary>Compress. Called once, after the finish line, off the hot path.</summary>
    WorldTape Finish(float lapTime, ulong ghostFingerprint);
}

public interface IWorldTapePlayer
{
    /// <summary>Frame by frame; a jump (backwards, or more than <see cref="WorldTapeConst.SeekJump"/> forward) calls <see cref="Seek"/>.</summary>
    void Apply(float lapClock, float dt);
    /// <summary>Destroy what the player created, restore the nearest keyframe, replay deltas and events up to the clock.</summary>
    void Seek(float lapClock);
    /// <summary>True for the actors the tape covers (their live tick is skipped in the replay).</summary>
    bool Covers(string actorKey);
}

/// <summary>What the player needs from the game (RaceScene implements it): spawn models, remove them, fire effects.</summary>
public interface IWorldTapeSink
{
    Node3D Spawn(string kind, string parentActorKey, int instance);
    void Despawn(int instance);
    /// <summary>
    /// Fire an effect or start a ballistic projectile. <paramref name="age"/> is how long it has already been going when this
    /// is called (0 in normal play, the time since e.Time after a seek): the sink must restore the effect AS IF it had run for
    /// that long (smoke emitters pre-rolled, a fire already burning), not start a fresh shorter one. Remaining life = e.Life - age.
    /// </summary>
    void Effect(in TapeEvent e, float age);
    /// <summary>Seek step 1: destroy every effect and projectile this sink started for the tape (spawned instances go through <see cref="Despawn"/>).</summary>
    void ClearEffects();
    /// <summary>Seek step 2: the set of broken breakables (obstacle ids) at the restored instant; the sink makes the world match it (phase 3).</summary>
    void SetBrokenBreakables(IReadOnlyCollection<int> obstacleIds);
    /// <summary>Seek step 2 with the clock: the same, and a breakable that broke at tape time t is shown <c>clock - t</c> seconds into its fall (what playing to the clock shows). A sink that has no fall to show keeps the default.</summary>
    void SetBrokenBreakables(IReadOnlyCollection<int> obstacleIds, float clock) => SetBrokenBreakables(obstacleIds);
}

/// <summary>
/// An optional extra of a sink: a seek may keep an instance it already shows when the tape wants the same instance (id, kind, parent) again,
/// instead of freeing the model and building it anew. The sink answers per kind (a kind with state the tape cannot restore is not recyclable)
/// and returns the instance to its freshly built state; the player then poses it from the keyframe like a new one.
/// </summary>
public interface IWorldTapeSinkRecycle
{
    bool CanRecycle(string kind);
    /// <summary>Reset the live instance to a new one's state; false if it is gone or not the same kind/parent (the player then spawns anew).</summary>
    bool Recycle(int instance, string kind, string parentActorKey);
}

/// <summary>File operations of the pair save. The implementation must do every replacement through this object, so tests can inject failures and probe the final path between operations.</summary>
public interface ITapeFileOps
{
    void Step(string name);                       // called between operations ("tmp-written", "verified", "before-replace", "after-replace", "ghost-replaced", "tape-replaced"...)
    void Replace(string tmp, string final);       // atomic replace of an existing file (File.Replace(tmp, final, null)); throws when unavailable
    void Move(string tmp, string final);          // the target does not exist
    void Warn(string message);                    // one warning per refused save
}

/// <summary>
/// What a pair save managed. The record is the ghost: when <see cref="GhostSaved"/> is true the new ghost is in place and the lap IS the
/// record even if the tape could not be replaced (the old tape is then dropped by its fingerprint). There is deliberately NO conversion to
/// bool: callers read <see cref="GhostSaved"/> and <see cref="TapeSaved"/> (or <see cref="Whole"/> where "both" is really what they mean),
/// so the old all-or-nothing reading cannot come back by accident.
/// </summary>
public readonly record struct TapePairResult(bool GhostSaved, bool TapeSaved)
{
    public bool Whole => GhostSaved && TapeSaved;
    public bool Refused => !GhostSaved;
    public override string ToString() => GhostSaved ? (TapeSaved ? "ghost+tape saved" : "ghost saved, tape not") : "refused";
}

/// <summary>FNV-1a, 64 bit: the hash the tape uses for fingerprints.</summary>
public struct TapeHash
{
    ulong _h;
    public static TapeHash Start() => new() { _h = 14695981039346656037UL };
    public TapeHash Add(uint v) { _h = (_h ^ (v & 0xFF)) * 1099511628211UL; _h = (_h ^ ((v >> 8) & 0xFF)) * 1099511628211UL; _h = (_h ^ ((v >> 16) & 0xFF)) * 1099511628211UL; _h = (_h ^ (v >> 24)) * 1099511628211UL; return this; }
    public TapeHash Add(float v) => Add(BitConverter.SingleToUInt32Bits(v));
    public TapeHash Add(int v) => Add((uint)v);
    public ulong Value => _h;
}

/// <summary>
/// The ghost's fingerprint (section 8): what ties a tape to the record car it was made with. The ghost file format is
/// not changed; the tape stores this and the loader recomputes it.
/// </summary>
public static class WorldTapeFingerprint
{
    public static ulong Of(Ghost ghost)
    {
        // Every stored sample, raw (no interpolation), so the value survives Save and Load bit for bit.
        var h = TapeHash.Start().Add(ghost.Count).Add(ghost.LapTime);
        for (int i = 0; i < ghost.Count; i++)
        {
            var p = ghost.PositionAt(i);
            var q = ghost.RotationAt(i);
            h = h.Add(p.X).Add(p.Y).Add(p.Z).Add(q.X).Add(q.Y).Add(q.Z).Add(q.W);
        }
        return h.Value;
    }
}
