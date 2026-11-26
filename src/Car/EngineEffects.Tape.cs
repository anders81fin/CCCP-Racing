using System;
using CCCPRacing.Race;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Car;

/// <summary>
/// The world tape's side of the engine effects (docs/replay-tape.md section 22). The engine is a small actor of its own (a root and one sound channel, like the player's power state): the channel is
/// <see cref="TapeLevel"/> (the smoke of a sick engine, the steam of a seized one, a burning wreck); the seizure and the explosion are effect events that make the same oil, steam, fire and black smoke
/// as the live ones (<see cref="SeizeFx"/>, <see cref="ExplodeFx"/>) with the replay's own generator, and leave the same oil slick and burnt paint, as old as the tape's clock says.
/// The replay never draws from the live generator (<c>_rng</c>).
/// </summary>
public partial class EngineEffects
{
    readonly RandomNumberGenerator _replayRng = new() { Seed = 1953 };
    float _replayLevel, _replaySmokeAcc, _replayFireAcc;

    void MarkTape()
    {
        Tape.Version(this, 1); Tape.Part(this, "root");
        Tape.Sound(this, TapeSoundChannel.EngineFx, () => TapeLevel, v => _replayLevel = v);
    }

    /// <summary>The tape shows the engine's smoke and fire; leaving it, the live engine's own go on.</summary>
    public bool TapeDriven { get; private set; }
    public void SetTapeDriven(bool on) { if (TapeDriven == on) return; TapeDriven = on; if (!on) ReplayClear(); }

    /// <summary>A seizure of the tape, <paramref name="age"/> s ago (called while the replay's effects are being made: they come out as old as that): oil, steam and the slick.</summary>
    public void ReplaySeize(Vector3 pos, Basis rot, Vector3 vel, float age, RandomNumberGenerator rng)
    {
        SeizeFx(_fx, rng, pos, rot, vel);
        ReplaySlick(pos + rot * Sump, 0.8f, 4.2f, age);
    }

    /// <summary>A direct hit of the tape: the fireball, and the burnt body and the burning pool (and the fire the level channel brings on).</summary>
    public void ReplayExplode(Vector3 pos, CarVisual vis, float age, RandomNumberGenerator rng)
    {
        ExplodeFx(_fx, rng, pos);
        ReplaySlick(pos, 3f, 6f, age);
        if (vis != null) { SetOverlay(vis, _burnt); _blackened = vis; }
    }

    /// <summary>A tank's crush of the tape: the dust, and the car's body pressed flat (the dust is restored by age through the tagged particles).</summary>
    public void ReplayCrush(Vector3 pos, CarVisual vis, float age, RandomNumberGenerator rng)
    {
        CrushFx(_fx, rng, pos);
        if (vis != null) { vis.Flatten = CrushedFlatten; _flattened = vis; }
    }

    void ReplaySlick(Vector3 at, float size, float target, float age)
    {
        _slickPos = new Vector3(at.X, _track.MeshHeight(at.X, at.Z) + 0.08f, at.Z);
        _slickTarget = target;
        _slickSize = Math.Min(target, size + age * 1.2f);      // (the live tick grows it 1.2 m a second)
        _slick.Visible = true;
        SizeSlick();
    }

    /// <summary>The level of the tape at the car's place (the replay's frame): the same emission the live Tick makes, with the replay's generator and clocks.</summary>
    public void ReplayTick(float dt, Transform3D car, Vector3 vel)
    {
        if (!TapeDriven || _replayLevel <= 0.001f) { _replaySmokeAcc = _replayFireAcc = 0; return; }
        Vector3 bonnet = car.Origin + car.Basis * Bonnet;
        if (_replayLevel >= 0.95f)
        {
            _replayFireAcc += dt * 70f;
            while (_replayFireAcc >= 1f)
            {
                _replayFireAcc -= 1f;
                var rng = _replayRng;
                Vector3 p = car.Origin + Vector3.Up * 0.4f + car.Basis * new Vector3(rng.RandfRange(-0.7f, 0.7f), 0.2f, rng.RandfRange(-1.7f, 1.3f));
                _fx.Burst(p, vel * 0.9f + new Vector3(rng.RandfRange(-0.6f, 0.6f), rng.RandfRange(2.5f, 5f), rng.RandfRange(-0.6f, 0.6f)),
                    FireCols[rng.RandiRange(0, 2)], 1f, rng.RandfRange(0.9f, 1.5f), 0.2f, rng.RandfRange(0.35f, 0.7f), -2f, 1.5f, false);
                if (rng.Randf() < 0.4f)
                    _fx.Smoke(p + Vector3.Up * 1.2f, vel * 0.5f + new Vector3(rng.RandfRange(-0.4f, 0.4f), rng.RandfRange(3f, 4.5f), rng.RandfRange(-0.4f, 0.4f)),
                        new Color(0.05f, 0.05f, 0.05f), 0.85f, 1.2f, 5f, rng.RandfRange(2.5f, 4f));
            }
        }
        else if (_replayLevel > 0.55f)
        {
            _replaySmokeAcc += dt * 18f * ((_replayLevel - 0.6f) / 0.3f);
            while (_replaySmokeAcc >= 1f) { _replaySmokeAcc -= 1f; SteamFx(_fx, _replayRng, car.Origin, car.Basis, 1); }
        }
        else
        {
            float k = Math.Clamp((_replayLevel - 0.02f) / 0.48f, 0f, 1f);
            var rng = _replayRng;
            _replaySmokeAcc += dt * (22f + 26f * k);
            while (_replaySmokeAcc >= 1f)
            {
                _replaySmokeAcc -= 1f;
                float grey = 0.4f - 0.3f * k;
                _fx.Smoke(bonnet + RandIn(rng, 0.4f, 0.1f, 0.4f), vel * 0.6f + new Vector3(rng.RandfRange(-0.5f, 0.5f), 2.4f + k, rng.RandfRange(-0.5f, 0.5f)),
                    new Color(grey, grey, grey * 0.95f), 0.75f + 0.2f * k, 0.9f + 0.3f * k, 2.6f + 1.6f * k, 1.2f + 0.8f * k);
            }
        }
    }

    /// <summary>A seek or the end of the replay: no slick, no burnt paint, no level (the live engine's state is not touched).</summary>
    public void ReplayClear()
    {
        _replayLevel = _replaySmokeAcc = _replayFireAcc = 0;
        _slick.Visible = false; _slickSize = _slickTarget = 0;
        if (_blackened != null) { SetOverlay(_blackened, null); _blackened = null; }
        if (_flattened != null) { _flattened.Flatten = 1f; _flattened = null; }
    }
}
