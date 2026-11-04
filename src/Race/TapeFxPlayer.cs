using System;
using System.Collections.Generic;
using CCCPRacing.Audio;
using CCCPRacing.Car;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// The game side of the world tape's effects (docs/replay-tape.md, phase 3): in the title replay it makes the effects, projectiles and broken breakables of a
/// tape's events with the very functions live play uses (the actors' static <c>...Fx</c> methods), restored by age (a smoke is already billowing, a fire already
/// burning, a flash that ended is skipped), and takes them all away on <see cref="Clear"/>. It is purely visual: it reads the track and the clock and makes nodes
/// and particles; it never touches an actor, the car or the simulation, and it has a random generator of its own. Tyre-effect particles it makes are tagged
/// (<see cref="TyreEffects.BeginReplay"/>) so they can be taken away and counted; everything else it makes hangs under one root node.
/// </summary>
public sealed partial class TapeFxPlayer
{
    sealed class Proj { public TapeEvent E; public Node3D Node; public Vector3 Last; public bool Placed; }
    sealed class Piece { public Node3D Node; public Vector3 Vel, Spin; public float Life = 20f; public bool Falling = true; }

    readonly TyreEffects _fx;
    readonly TrackData _track;
    readonly Breakables _scenery;
    readonly Node3D _root, _mute;
    readonly RandomNumberGenerator _rng = new();
    readonly List<Proj> _proj = new();
    readonly List<Piece> _pieces = new();
    Mesh _hindTracerMesh;
    StandardMaterial3D _hindTracerMaterial;
    IReadOnlyDictionary<int, (Vector3 Vel, Vector3 At)> _how;
    IReadOnlyDictionary<int, float> _brokeAt;      // when each breakable broke (a negative time: it was broken when the lap began)
    float _last = float.NaN;
    /// <summary>What the replay has made, by kind, since this player exists (the tests count them).</summary>
    public readonly SortedDictionary<string, int> Made = new();
    public WoodFences Wood;
    public bool WoodReplay;
    public int ProjectilesFlown { get; private set; }
    /// <summary>What the game itself makes of a hit it did not simulate (the babushka's thump and shake, the burst can's splash): the race sets it.</summary>
    public Action<TapeEvent, float> Feel;
    public Action<TapeEvent?, float> Lens;
    public Action<TapeEvent?, float> Radar;
    TapeEvent? _lensHit;
    TapeEvent? _radarReading;

    public TapeFxPlayer(TyreEffects fx, TrackData track, Breakables scenery, Node3D root)
    {
        _fx = fx; _track = track; _scenery = scenery; _root = root;
        _mute = new Node3D { Name = "TapeFxMute" };      // (never in the tree: the sounds of effects that are over are made here and freed at once)
    }

    /// <summary>Effect nodes alive now: the root's children.</summary>
    public int Nodes => _root.GetChildCount();
    public int Particles => _fx.ReplayParticles;
    public int Projectiles => _proj.Count;
    public int PiecesAlive => _pieces.Count;

    /// <summary>The way each breakable of the tape went down (direction and place), for the ones a seek shows already lying.</summary>
    public void SetBreakInfo(IEnumerable<TapeEvent> events)
    {
        var d = new Dictionary<int, (Vector3, Vector3)>(); var at = new Dictionary<int, float>();
        foreach (var e in events) if (e.Type == TapeEventType.Effect && e.Effect == TapeEffect.BreakableBroken) { d[(int)e.Param] = (e.Dir, e.Pos); at[(int)e.Param] = e.Time; }
        _how = d; _brokeAt = at;
    }

    void Count(string kind) { Made[kind] = Made.GetValueOrDefault(kind) + 1; }

    Node SoundParent(float age) => age < .3f ? _root : _mute;

    /// <summary>The player is done (the scene leaves the tree): its mute node, which is never in the tree, goes.</summary>
    public void Free() { Clear(); if (GodotObject.IsInstanceValid(_mute)) _mute.Free(); }

    void FlushMute()
    {
        foreach (var c in _mute.GetChildren()) { _mute.RemoveChild(c); c.Free(); }
    }

    void Seed(in TapeEvent e)
    {
        ulong h = 14695981039346656037UL;
        foreach (int v in new[] { BitConverter.SingleToInt32Bits(e.Time), BitConverter.SingleToInt32Bits(e.Pos.X), BitConverter.SingleToInt32Bits(e.Pos.Z), BitConverter.SingleToInt32Bits(e.Param) })
            h = (h ^ (uint)v) * 1099511628211UL;
        _rng.Seed = h;
    }

    /// <summary>The sink's Effect: the event as it began <paramref name="age"/> seconds ago.</summary>
    public void Effect(in TapeEvent e, float age)
    {
        if (e.Kind == TapeFxKind.Marker) return;
        if (e.Life > 0 && age > e.Life) return;
        if (e.Type == TapeEventType.Projectile) { Launch(e, age); return; }
        Seed(e);
        Count(e.Kind ?? e.Effect.ToString());
        _fx.BeginReplay(age);
        try
        {
            switch (e.Kind)
            {
                case TapeFxKind.BusFire: BusFire(e, age); break;
                case TapeFxKind.WoodBreak:
                    if (WoodReplay && ulong.TryParse(e.Parent, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out ulong state))
                    {
                        Wood?.ReplayBreak((int)e.Param, new Vector2(e.Pos.X, e.Pos.Z), e.Velocity, state, age, e.Time);
                        if (age < .1f) Feel?.Invoke(e, age);
                    }
                    break;
                case TapeFxKind.HindImpact: HindSorties.ImpactFx(_fx, _rng, e.Pos, e.Param > .5f, SoundParent(age)); break;
                case TapeFxKind.RotorWash: HindSorties.WashFx(_fx, e.Pos, e.Dir); break;
                case TapeFxKind.HindTrail: HindSorties.TrailFx(_fx, e.Pos); break;
                case TapeFxKind.HindFall: Fall(e, age); break;
                case TapeFxKind.HindCrash: Crash(e, age); break;
                case TapeFxKind.JetCrash: JetCrash(e, age); break;
                case TapeFxKind.MissileBurst: Missile.ExplodeFx(_fx, _rng, age < .4f ? _root : _mute, SoundParent(age), e.Pos); break;
                case TapeFxKind.BabushkaLaunch: if (age < .3f) Feel?.Invoke(e, age); break;      // (the thump and the shake: only for a hit that is happening now)
                case TapeFxKind.RadarReading:
                    if (_radarReading == null || e.Time >= _radarReading.Value.Time)
                    { _radarReading = e; Radar?.Invoke(e, age); }
                    break;
                case TapeFxKind.BabushkaLens:
                    if (_lensHit == null || e.Time >= _lensHit.Value.Time)
                    {
                        bool sameImpact = _lensHit is TapeEvent previous && Math.Abs(previous.Time - e.Time) < .00001f;
                        _lensHit = e; Lens?.Invoke(e, age);
                        if (age <= .00001f && !sameImpact) Feel?.Invoke(e, age);
                    }
                    break;
                case TapeFxKind.MechanicWhack:
                    _rng.Seed = unchecked((uint)e.Instance);
                    DrunkMen.WhackParticles(_fx, _rng, e.Pos, e.Param > .5f);
                    if (age < .8f) _root.AddChild(new ClangSound { Strength = e.Param > .5f ? 1 : .6f, StartAge = age });
                    if (age <= .00001f) Feel?.Invoke(e, age);
                    break;
                case TapeFxKind.CanisterBurst: Feel?.Invoke(e, age); break;      // (the splash restores by age through the tagged particles; its thump the host skips when old)
                case TapeFxKind.RunWarning: Warning(e, age); break;
                case TapeFxKind.BukLaunch: Buk21Battery.LaunchFx(_fx, _track, SoundParent(age), e.Pos, e.Dir); break;
                case TapeFxKind.BukTrail: Buk21Battery.TrailFx(_fx, e.Pos); break;
                case TapeFxKind.BukBurst: Buk21Battery.BurstFx(_fx, _rng, SoundParent(age), e.Pos, e.Param > .5f); break;
                case TapeFxKind.TankBlast: TankBattle.BlastFx(_fx, _track, _rng, SoundParent(age), e.Pos, e.Dir); break;
                case TapeFxKind.TankImpact: Crater(e, age); break;
                case TapeFxKind.TankRicochet: TankBattle.RicochetFx(_fx, _rng, SoundParent(age), e.Pos); break;
                case TapeFxKind.Breakable: _scenery?.ReplayBreak((int)e.Param, e.Dir, e.Pos, age); break;
                default:
                    if (e.Effect == TapeEffect.BreakableBroken) _scenery?.ReplayBreak((int)e.Param, e.Dir, e.Pos, age);
                    break;
            }
        }
        finally { _fx.EndReplay(); FlushMute(); }
    }

    void Fall(in TapeEvent e, float age)
    {
        HindSorties.FallFx(_fx, _rng, SoundParent(age), _root, e.Pos, e.Param, e.Velocity, (node, vel, spin) =>
        {
            var p = new Piece { Node = node, Vel = vel, Spin = spin };
            for (float t = 0; t < age && p.Life > 0; t += 1f / 60f) StepPiece(p, Math.Min(1f / 60f, age - t));
            if (p.Life <= 0) { node.GetParent()?.RemoveChild(node); node.QueueFree(); } else _pieces.Add(p);
        });
    }

    /// <summary>The same step as the live remnants of an aircraft (HindSorties.Tick).</summary>
    void StepPiece(Piece p, float dt)
    {
        p.Life -= dt;
        if (p.Life <= 0 || !p.Falling) return;
        p.Vel += Vector3.Down * 14 * dt;
        p.Node.Position += p.Vel * dt;
        p.Node.Rotation += p.Spin * dt;
        Vector3 at = p.Node.Position;
        float ground = _track.MeshHeight(at.X, at.Z);
        if (at.Y < ground + .2f) { p.Node.Position = new Vector3(at.X, ground + .2f, at.Z); p.Falling = false; }
    }

    void Crash(in TapeEvent e, float age)
    {
        var d = HindSorties.ExplodeFx(_track, SoundParent(age), _root, e.Pos);
        d.PreRoll(age);
    }

    /// <summary>A jet hits the ground: the live crash (BalalaikaSorties.CrashFx) as it would look <paramref name="age"/> seconds on: the particles only while they live, the flash and the boom only while they last, the scorch for good, the wreck while it burns.</summary>
    void JetCrash(in TapeEvent e, float age)
    {
        BalalaikaSorties.CrashFx(age < BalalaikaSorties.CrashParticleLife ? _fx : null, _rng, age < .6f ? _root : _mute, SoundParent(age), e.Pos, out var scorch, out var wreck);
        _root.AddChild(scorch);
        _root.AddChild(wreck);
        BalalaikaSorties.MakeReplayWreck(_fx, wreck, e.Pos, age, BitConverter.SingleToInt32Bits(e.Time) ^ BitConverter.SingleToInt32Bits(e.Pos.X));
    }

    void Crater(in TapeEvent e, float age)
    {
        var d = GroundImpactEffects.Create(_track, e.Pos);
        _root.AddChild(d);
        d.PreRoll(age);
        SoundParent(age).AddChild(new ExplosionSound { Position = e.Pos, Loudness = 3f, Reach = 650f, ReferenceDistance = 90f });
    }

    void Warning(in TapeEvent e, float age)
    {
        var w = new HindRunWarning();
        _root.AddChild(w);
        w.Init(_track, new Vector2(e.Pos.X, e.Pos.Z), new Vector2(e.Dir.X, e.Dir.Z));
        w.SetAge(age);
    }

    // ---- projectiles: parametric, placed by the clock (no per-frame samples) ----

    void Launch(in TapeEvent e, float age)
    {
        Count(e.Kind ?? "projectile");
        var pr = new Proj { E = e };
        switch (e.Kind)
        {
            case TapeFxKind.HindTracer:
                _hindTracerMesh ??= Props.Box(.16f, .16f, 1);
                _hindTracerMaterial ??= HindSorties.TracerMaterial();
                pr.Node = new MeshInstance3D { Mesh = _hindTracerMesh, MaterialOverride = _hindTracerMaterial, Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
                _root.AddChild(pr.Node);
                if (age < .1f) { _fx.BeginReplay(age); HindSorties.MuzzleFx(_fx, e.Pos, e.Dir); _fx.EndReplay(); }
                break;
            case TapeFxKind.BtrTracer:
                pr.Node = BtrPatrols.TracerNode();
                _root.AddChild(pr.Node);
                pr.Node.Position = e.Pos; pr.Node.LookAtFromPosition(e.Pos, e.Pos + e.Velocity);
                break;
        }
        _proj.Add(pr); ProjectilesFlown++;
        Place(pr, age, false);
    }

    static Vector3 PosAt(in TapeEvent e, float t) => e.Pos + e.Velocity * t + new Vector3(0, -.5f * e.Gravity * t * t, 0);

    void Place(Proj p, float t, bool stepping)
    {
        var e = p.E; Vector3 pos = PosAt(e, t);
        switch (e.Kind)
        {
            case TapeFxKind.HindTracer:
            {
                Vector3 dir = e.Velocity.Normalized();
                float length = Math.Min(6.5f, e.Velocity.Length() * t);
                p.Node.Visible = length > .02f;
                p.Node.Transform = new Transform3D(Basis.LookingAt(dir, MathF.Abs(dir.Y) > .98f ? Vector3.Right : Vector3.Up).Scaled(new Vector3(1, 1, Math.Max(.02f, length))), pos - dir * length * .5f);
                break;
            }
            case TapeFxKind.BtrTracer: p.Node.Position = pos; break;
            case TapeFxKind.TankShell:
                if (stepping && p.Placed) { _fx.BeginReplay(0); TankBattle.TracerFx(_fx, pos, p.Last); _fx.EndReplay(); }
                break;
        }
        p.Last = pos; p.Placed = true;
    }

    /// <summary>Once per step of the replay, after the player has applied the clock: the projectiles are where their formula puts them, the debris falls on.</summary>
    public void Update(float clock)
    {
        if (_radarReading is TapeEvent reading)
        {
            float age = Math.Max(0, clock - reading.Time);
            if (age >= reading.Life) { _radarReading = null; Radar?.Invoke(null, 0); }
            else Radar?.Invoke(reading, age);
        }
        if (_lensHit is TapeEvent lens)
        {
            float age = Math.Max(0, clock - lens.Time);
            if (age >= lens.Life) { _lensHit = null; Lens?.Invoke(null, 0); }
            else Lens?.Invoke(lens, age);
        }
        UpdateBusFires(clock);
        if (WoodReplay) Wood?.ReplayUpdate(clock);
        float dt = float.IsNaN(_last) ? 0 : Math.Clamp(clock - _last, 0, .1f);
        _last = clock;
        for (int i = _proj.Count - 1; i >= 0; i--)
        {
            var p = _proj[i]; float t = clock - p.E.Time;
            if (t >= p.E.Life)
            {
                if (p.E.Kind == TapeFxKind.BtrTracer) { _fx.BeginReplay(0); BtrPatrols.TracerEndFx(_fx, PosAt(p.E, p.E.Life)); _fx.EndReplay(); }
                if (p.Node != null) { p.Node.GetParent()?.RemoveChild(p.Node); p.Node.QueueFree(); }
                _proj.RemoveAt(i); continue;
            }
            Place(p, Math.Max(0, t), true);
        }
        if (dt > 0)
            for (int i = _pieces.Count - 1; i >= 0; i--)
            {
                var p = _pieces[i]; StepPiece(p, dt);
                if (p.Life <= 0) { p.Node.GetParent()?.RemoveChild(p.Node); p.Node.QueueFree(); _pieces.RemoveAt(i); }
            }
    }

    /// <summary>Seek step 1: every effect, projectile and piece the replay made goes (and the particles it tagged); nothing it did is left to multiply.</summary>
    public void Clear()
    {
        _radarReading = null; Radar?.Invoke(null, 0);
        _lensHit = null; Lens?.Invoke(null, 0);
        _busFires.Clear();
        if (WoodReplay) Wood?.ReplayClear();
        foreach (var c in _root.GetChildren()) { _root.RemoveChild(c); c.QueueFree(); }
        _proj.Clear(); _pieces.Clear();
        _fx.ClearReplay();
        _last = float.NaN;
    }

    /// <summary>Seek step 2: the world shows exactly these breakables broken, each as far into its fall as the tape's clock says (a tree that broke 0.2 s ago is not yet half down, one that broke 3 s ago lies).</summary>
    public void SetBroken(IReadOnlyCollection<int> ids, float clock = float.PositiveInfinity)
    {
        Dictionary<int, float> ages = null;
        if (_brokeAt != null && float.IsFinite(clock))
        {
            ages = new Dictionary<int, float>();
            foreach (int id in ids) if (_brokeAt.TryGetValue(id, out float t)) ages[id] = Math.Max(0f, clock - t);
        }
        _scenery?.ReplaySet(ids, _how, ages);
    }
}
