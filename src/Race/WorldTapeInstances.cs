using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// What an actor that creates and frees models during a lap tells the world tape (docs/replay-tape.md, sections 7 and 9). The model
/// carries <c>tape_kind</c> (kind, or "kind/argument") and its marked parts; <see cref="Born"/> is called once it is built and in the tree,
/// <see cref="Gone"/> just before it is freed (the index must let go of a node before it is queued for freeing). Both only touch the tape
/// bookkeeping, never the simulation, and both are no-ops while nothing records.
/// </summary>
public interface ITapeInstanceHost
{
    void Born(Node3D model);
    void Gone(Node3D model);
}

/// <summary>
/// What an actor that makes one-shot effects tells the world tape (docs/replay-tape.md, section 7 and 15): the event describes the effect
/// (Effect or Projectile, Kind, Pos, Dir, Velocity, Param, Life; <c>Source</c> is filled in by the host) and <c>Time</c> is how many seconds AGO
/// it began (0: now; a projectile that is told when it ends says how long it flew). It only reads the world, never changes it, and is a
/// cheap no-op while nothing records. The actor calls it where it spawns the live effect, so a replay can spawn the same one.
/// </summary>
public interface ITapeFxHost
{
    void Fx(in TapeEvent e);
}

/// <summary>The kinds of the world tape's effect and projectile events (phase 3): the actor names one where it spawns the live effect, the replay's sink switches on it.</summary>
public static class TapeFxKind
{
    public const string HindTracer = "hind-tracer", HindImpact = "hind-impact", RotorWash = "rotor-wash", HindTrail = "hind-trail", HindFall = "hind-fall",
        HindDebris = "hind-debris", HindCrash = "hind-crash", RunWarning = "run-warning", BukLaunch = "buk-launch", BukTrail = "buk-trail", BukBurst = "buk-burst",
        TankBlast = "tank-blast", TankShell = "tank-shell", TankImpact = "tank-impact", TankRicochet = "tank-ricochet", BtrTracer = "btr-tracer", Marker = "tape-fx",
        BusFire = "bus-fire", WoodBreak = "wood-break", Breakable = "breakable", JetCrash = "jet-crash", MissileBurst = "missile-burst", BabushkaLaunch = "babushka-launch", CanisterBurst = "canister-burst",
        BabushkaLens = "babushka-lens", MechanicWhack = "mechanic-whack", RadarReading = "radar-reading";
}
