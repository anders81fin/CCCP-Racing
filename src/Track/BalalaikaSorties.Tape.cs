using System;
using System.Collections.Generic;
using CCCPRacing.Audio;
using CCCPRacing.Car;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Track;

// The world tape's side of the Balalaika sorties (docs/replay-tape.md, section 16): each jet is a dynamic instance of kind "jet", built by the same
// BuildJet in play and in the replay and marked by one MarkTape (root, gear, the two rails, the sound and a state). A guided missile is an instance of
// kind "missile" (Missile.Tape.cs). The crash is an effect event (jet-crash) made by the very CrashFx the live crash calls. What burns every tick (the
// afterburner's exhaust and flame, the trail of a diving jet, a wreck's smoke) is NOT an event per puff: the replay's own emitters make it from the
// recorded afterburner, state and wreck age, with a random generator of their own (a seek starts them afresh), which keeps the tape small.
public partial class BalalaikaSorties
{
    /// <summary>A burning wreck's life (s): the live wreck node, its smoke and its fire.</summary>
    public const float WreckLife = 12f;
    /// <summary>The scorch stays until the world is reset (or eight are down): the crash event's life, for a replay seeking late in the lap.</summary>
    public const float CrashLife = 600f;
    /// <summary>The fireball's longest particle (s): a crash restored later than this has no particles to make.</summary>
    public const float CrashParticleLife = 8f;

    bool _tapeDriven;
    /// <summary>The title replay's tape shows the jets and their wrecks: nothing is spawned, flown or fired here (setting it resets the sorties, so no live jet stays; clearing it puts the sorties back to their start).</summary>
    public bool TapeDriven { get => _tapeDriven; set { if (_tapeDriven == value) return; _tapeDriven = value; Reset(); } }

    /// <summary>The jet with its flame and its sound, as play and the replay both build it.</summary>
    internal static Node3D BuildJet(out MeshInstance3D flame, out JetSound sound)
    {
        var model = Airbase.Balalaika();
        flame = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.42f, Height = 3f, RadialSegments = 8, Rings = 1 },   // wide at the nozzle
            MaterialOverride = NightGlow.Keep(new StandardMaterial3D
            {
                AlbedoColor = new Color(1f, 0.6f, 0.15f, 0.85f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            }),
            Position = new Vector3(0, 1.6f, 7.8f),
            RotationDegrees = new Vector3(90, 0, 0),
            Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        model.AddChild(flame);
        sound = new JetSound();
        model.AddChild(sound);
        return model;
    }

    /// <summary>The tape marks of a jet: its kind, the parts that move or hide (the gear, the rails' missiles) and its sounds and state (0 flying or on the ground, 2 diving into the ground). The flame is no part: it rescales every tick, the afterburner channel drives it.</summary>
    internal static void MarkTape(Node3D model, JetSound sound, Func<int> state, Action<int> setState = null)
    {
        model.SetMeta(WorldTapeConst.KindMeta, "jet");
        Tape.Part(model, "root");
        Tape.Part(model.GetNode<Node3D>("Gear"), "gear");
        Tape.Part(model.GetNode<Node3D>("MissileL/Missile"), "railL");
        Tape.Part(model.GetNode<Node3D>("MissileR/Missile"), "railR");
        Tape.Sound(model, TapeSoundChannel.JetLevel, () => sound.Level, v => sound.Level = v);
        Tape.Sound(model, TapeSoundChannel.JetAfterburner, () => sound.Afterburner, v => sound.Afterburner = v);
        Tape.Sound(model, TapeSoundChannel.AircraftState, () => state() / 3f, v => setState?.Invoke((int)MathF.Round(v * 3f)));
    }

    /// <summary>The jet of a tape ("jet"): the play model, marked alike, nothing simulated; its flame, exhaust and dive trail follow the recorded channels.</summary>
    public static Node3D BuildReplayJet(TyreEffects fx)
    {
        var model = BuildJet(out var flame, out var sound);
        var rfx = new JetReplayFx { Name = "TapeState", Model = model, Flame = flame, Sound = sound, Fx = fx };
        model.AddChild(rfx);
        MarkTape(model, sound, () => rfx.State, k => rfx.State = k);
        return model;
    }

    /// <summary>The afterburner flames of the jets the tape shows (position of the nozzle, how hard it burns), for the night's afterburner lights.</summary>
    IEnumerable<(Vector3 nozzle, float burn)> ReplayBurners()
    {
        foreach (var c in GetChildren())
            if (c is Node3D m && m.HasMeta(WorldTapeConst.KindMeta) && m.GetNodeOrNull<JetReplayFx>("TapeState") is { } f && f.Flame.Visible)
                yield return (f.Flame.GlobalPosition, f.Sound.Afterburner);
    }

    /// <summary>The burning wreck of a replayed crash: the same smoke and fire as the live wreck makes, for <see cref="WreckLife"/> seconds, then the wreck is gone.</summary>
    internal static void MakeReplayWreck(TyreEffects fx, Node3D wreck, Vector3 p, float age, int seed)
    {
        if (age >= WreckLife) { wreck.Free(); return; }
        var w = new JetWreckFx { Fx = fx, Pos = p, Life = WreckLife };
        w.Seed(seed);
        wreck.AddChild(w);
        w.PreRoll(age);
    }
}

/// <summary>The state of a replayed jet that the pose cannot carry: the afterburner's flame and exhaust and the smoke of a diving jet, made every physics step by the live code's own formulas (BalalaikaSorties.Place, Exhaust, Dive).</summary>
public partial class JetReplayFx : Node
{
    public int State;
    public Node3D Model;
    public MeshInstance3D Flame;
    public JetSound Sound;
    public TyreEffects Fx;
    readonly RandomNumberGenerator _rng = new() { Seed = 1958 };

    public override void _PhysicsProcess(double delta)
    {
        float ab = Sound.Afterburner;
        Flame.Visible = ab > 0.05f;
        if (Flame.Visible) Flame.Scale = new Vector3(1f, (0.8f + 0.4f * _rng.Randf()) * ab, 1f);
        if (Fx == null || !Model.IsInsideTree()) return;
        Fx.BeginReplay(0);
        try
        {
            if (ab >= 0.05f)
            {
                Vector3 tail = Model.GlobalTransform * new Vector3(0, 1.6f, 6.6f), back = Model.GlobalTransform.Basis.Z;
                for (int k = 0; k < 2; k++)
                    Fx.Burst(tail + back * _rng.RandfRange(0.5f, 2.5f), back * 12f, _rng.Randf() < 0.5f ? new Color(1f, 0.85f, 0.4f) : new Color(1f, 0.45f, 0.1f),
                        1f, 0.7f, 0.2f, 0.18f, 0f, 2f, false);
                if (_rng.Randf() < 0.4f)
                    Fx.Smoke(tail + back * 3f, Vector3.Up * 0.5f, new Color(0.45f, 0.45f, 0.45f), 0.35f, 0.8f, 3f, 1.8f);
            }
            if (State == 2)
            {
                Vector3 at = Model.GlobalPosition + Vector3.Up * 1.6f;
                Fx.Smoke(at, Vector3.Up * 0.5f, new Color(0.08f, 0.08f, 0.08f), 0.75f, 0.8f, 3f, 2.5f);
                if (_rng.Randf() < 0.5f)
                    Fx.Burst(at, Vector3.Zero, new Color(1f, 0.55f, 0.1f), 1f, 0.9f, 0.3f, 0.25f, 0f, 1f, false);
            }
        }
        finally { Fx.EndReplay(); }
    }
}

/// <summary>A replayed jet's burning wreck (a child of the wreck node): the live wreck's smoke and fire per step (BalalaikaSorties.Tick), then it frees the wreck.</summary>
public partial class JetWreckFx : Node
{
    public TyreEffects Fx;
    public Vector3 Pos;
    public float Life;
    readonly RandomNumberGenerator _rng = new();

    public void Seed(int seed) => _rng.Seed = (ulong)(uint)seed;

    /// <summary>The wreck has burned for <paramref name="age"/> seconds already: its particles are made as they would have been (each as old as it would be now).</summary>
    public void PreRoll(float age)
    {
        const float Step = 1f / 30f;
        for (float t = 0; t < age; t += Step)
        {
            float dt = Math.Min(Step, age - t);
            Fx.BeginReplay(age - t);
            try { Emit(dt); } finally { Fx.EndReplay(); }
        }
    }

    void Emit(float dt)
    {
        Life -= dt;
        if (_rng.Randf() < dt * 14f)
            Fx.Smoke(Pos + Vector3.Up * 0.8f, Vector3.Up * _rng.RandfRange(2f, 4f) + new Vector3(_rng.RandfRange(-0.5f, 0.5f), 0, _rng.RandfRange(-0.5f, 0.5f)),
                new Color(0.1f, 0.1f, 0.1f), 0.7f, 1.2f, 4.5f, 3.5f);
        if (Life > 3f && _rng.Randf() < dt * 20f)
            Fx.Burst(Pos + new Vector3(_rng.RandfRange(-1.5f, 1.5f), 0.5f, _rng.RandfRange(-1.5f, 1.5f)), Vector3.Up * _rng.RandfRange(1.5f, 3f),
                new Color(1f, _rng.RandfRange(0.35f, 0.7f), 0.1f), 1f, 0.9f, 0.2f, 0.5f, -2f, 1f, false);
    }

    public override void _PhysicsProcess(double delta)
    {
        Fx.BeginReplay(0);
        try { Emit((float)delta); } finally { Fx.EndReplay(); }
        if (Life <= 0) GetParent()?.QueueFree();
    }
}
