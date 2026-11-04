using System;
using CCCPRacing.Audio;
using CCCPRacing.Car;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Track;

// The Hinds' effects (docs/replay-tape.md, phase 3): the visuals live here as static functions, so live play and the title replay (the tape's sink)
// make exactly the same thing from the same numbers. The live code calls them and tells the world tape (TapeFx) what it made.
public partial class HindSorties
{
    /// <summary>The world tape's view of the effects made here and by the battery (null: nothing records). Set by the race.</summary>
    public ITapeFxHost TapeFx;

    /// <summary>The tracer of a round is on the tape once its flight is over: it left <paramref name="flight"/> seconds ago and flew that long (a round that hit the car stopped early).</summary>
    void TapeRound(Round r, float flight)
    {
        if (TapeFx == null) return;
        TapeFx.Fx(new TapeEvent(flight, TapeEventType.Projectile, Kind: TapeFxKind.HindTracer, Projectile: TapeProjectile.Tracer, Pos: r.Origin,
            Velocity: (r.Target - r.Origin) / Math.Max(.0001f, r.Flight), Dir: r.Kick, Life: flight));
    }

    /// <summary>The flash at the gun (a short orange puff).</summary>
    internal static void MuzzleFx(TyreEffects fx, Vector3 muzzle, Vector3 kick) => fx.Burst(muzzle, kick, new Color(1, .8f, .3f), 1, .65f, .05f, .1f, 0, 0, false);

    /// <summary>A cannon round striking the ground: sparks, a tall dust jet, a low cloud and the strike sound under <paramref name="parent"/>.</summary>
    internal static void ImpactFx(TyreEffects fx, RandomNumberGenerator rng, Vector3 at, bool hard, Node parent)
    {
        Color colour = hard ? new Color(1, .75f, .35f) : new Color(.43f, .33f, .19f);
        for (int k = 0; k < 9; k++)
            fx.Burst(at, new Vector3(rng.RandfRange(-6, 6), rng.RandfRange(5, 14), rng.RandfRange(-6, 6)), colour, 1, .32f, .12f, .8f, 12, 0, false);
        Color dust = hard ? new Color(.53f, .52f, .48f) : new Color(.46f, .36f, .23f);
        // A tall impact jet and rolling low cloud leave a readable, persistent strafing line.
        fx.Smoke(at + Vector3.Up * .3f, Vector3.Up * 4.5f, dust, .9f, 1.1f, 4.5f, 2.7f);
        for (int k = 0; k < 3; k++)
            fx.Smoke(at + new Vector3(rng.RandfRange(-1, 1), .2f, rng.RandfRange(-1, 1)),
                new Vector3(rng.RandfRange(-2.5f, 2.5f), rng.RandfRange(.7f, 2), rng.RandfRange(-2.5f, 2.5f)),
                dust, .65f, .8f, 3.8f, 3.5f);
        parent.AddChild(new CannonStrikeSound { Position = at });
    }

    /// <summary>One puff of rotor wash dust at a lifting or landing aircraft (<paramref name="at"/> is already out on its circle).</summary>
    internal static void WashFx(TyreEffects fx, Vector3 at, Vector3 outward) =>
        fx.Smoke(at, outward * 5 + Vector3.Up, new Color(.57f, .52f, .38f), .3f, 1.4f, 3, 2);

    /// <summary>One puff of the smoke and fire trail of a falling aircraft and its wreck.</summary>
    internal static void TrailFx(TyreEffects fx, Vector3 pos)
    {
        fx.Smoke(pos + Vector3.Up * 1.5f, Vector3.Up * 3, new Color(.12f, .12f, .11f), .9f, 2, 5, 3);
        fx.Burst(pos + Vector3.Up * 1.2f, Vector3.Up * 2, new Color(1, .35f, .05f), 1, 1.4f, .4f, .6f, 0, 0, false);
    }

    /// <summary>An aircraft hit in the air (a collision or the battery): the boom (under <paramref name="sound"/>), four pieces thrown off (under <paramref name="parent"/>, handed to <paramref name="piece"/>: node, velocity, spin) and a cloud of debris.</summary>
    internal static void FallFx(TyreEffects fx, RandomNumberGenerator rng, Node sound, Node parent, Vector3 pos, float side, Vector3 velocity, Action<Node3D, Vector3, Vector3> piece)
    {
        sound.AddChild(new ExplosionSound { Position = pos, Reach = 650, Loudness = 0 });
        for (int i = 0; i < 4; i++)
        {
            var node = Props.Part(Props.Box(i % 2 == 0 ? 2.5f : .6f, .12f, i % 2 == 0 ? 1.2f : 1.8f),
                new Color(.28f, .3f, .22f), pos + new Vector3(side * (i + 1), 2, i - 2));
            parent.AddChild(node);
            piece(node, velocity * .6f + new Vector3(side * rng.RandfRange(5, 16), rng.RandfRange(3, 12), rng.RandfRange(-8, 8)),
                new Vector3(rng.RandfRange(-4, 4), rng.RandfRange(-3, 3), rng.RandfRange(-5, 5)));
        }
        for (int i = 0; i < 18; i++)
            fx.Burst(pos + Vector3.Up * 2, new Vector3(rng.RandfRange(-15, 15), rng.RandfRange(-3, 12), rng.RandfRange(-15, 15)),
                new Color(.45f, .43f, .35f), 1, .7f, 1.5f, 2.5f, 12, 0, false);
    }

    /// <summary>A wreck coming to rest: the boom and the fireball, scar and smoke (a node of its own that frees itself).</summary>
    internal static GroundImpactEffects ExplodeFx(TrackData track, Node sound, Node parent, Vector3 at)
    {
        sound.AddChild(new ExplosionSound { Position = at, Reach = 1200, Loudness = 2 });
        var detail = GroundImpactEffects.Create(track, at, true);
        parent.AddChild(detail);
        return detail;
    }

    /// <summary>The tracer's glow (live play and the replay both use it).</summary>
    internal static StandardMaterial3D TracerMaterial() => NightGlow.Keep(new StandardMaterial3D { AlbedoColor = new Color(1, .82f, .24f),
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, EmissionEnabled = true,
        Emission = new Color(1, .5f, .08f), EmissionEnergyMultiplier = 2 });
}
