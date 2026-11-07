using System;
using System.Linq;
using CCCPRacing.Audio;
using CCCPRacing.Car;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Track;

// The world tape's side of the Hind exercise (docs/replay-tape.md, phase 2): each aircraft is a dynamic instance of kind "hind/<number>",
// built by the same BuildAircraft as in play. In the title replay the tape drives the aircraft (TapeDriven): nothing here departs, flies, fires
// or lands, the crew and the battery go on, and the replay's own aircraft are made by BuildReplayAircraft.
public partial class HindSorties
{
    bool _tapeDriven;
    /// <summary>The title replay's tape shows the aircraft: the sorties neither spawn nor fly (setting it resets the exercise, so no live aircraft stays; clearing it puts the parked ones back).</summary>
    public bool TapeDriven { get => _tapeDriven; set { if (_tapeDriven == value) return; _tapeDriven = value; Reset(); } }

    /// <summary>What goes on while the tape shows the aircraft: the crew, the ground circles of nothing (the battery's dish and loader are posed by the tape).</summary>
    public int CrewTicks;      // (tests) how often the crew and battery ticked without aircraft
    void TickBesidesAircraft(float dt, CarSim car)
    {
        CrewTicks++;
        _life?.Tick(dt, car, null, _battery?.Alert ?? false);
        // (the battery's dish, loader truck and crane are static parts of this actor: the tape poses them, so the battery's own steps would only fight the poses; with no aircraft it fires nothing)
        UpdateGroundObstacles();
    }

    /// <summary>The aircraft model with its two sounds, as play and the replay both build it.</summary>
    internal static Node3D BuildAircraft(string number, out RotorSound engine, out HindGunSound gun)
    {
        var model = BuildModel(number);
        engine = new RotorSound { Level = 0 }; model.AddChild(engine);
        gun = new HindGunSound { Position = new Vector3(0, 1.25f, -6.5f) }; model.AddChild(gun);
        return model;
    }

    /// <summary>0 powered, 1 landing light on, 2 unpowered (falling), 3 wreck: the lights and the squashed wreck of <see cref="Place"/> and the wreck step, as one recorded state.</summary>
    static int AirState(Aircraft p) => p.Phase == Phase.Wreck ? 3 : p.Phase == Phase.Falling ? 2 : p.Phase is Phase.Spool or Phase.Lift or Phase.Climb or Phase.Descend ? 1 : 0;

    /// <summary>The tape marks of an aircraft: its kind, its moving parts (rotors as registered spinners), and its sounds and state.</summary>
    internal static void MarkTape(Node3D model, string number, Func<double> rotor, Func<double> tail, RotorSound engine, HindGunSound gun, MeshInstance3D flash, Func<int> state, Action<int> setState = null)
    {
        model.SetMeta(WorldTapeConst.KindMeta, "hind/" + number);
        Tape.Part(model, "root");
        var r = model.GetNode<Node3D>("Rotor"); Tape.Part(r, "rotor"); Tape.Spin(r, Vector3.Up, () => (float)rotor());
        var t = model.GetNode<Node3D>("TailRotor"); Tape.Part(t, "tail"); Tape.Spin(t, Vector3.Right, () => (float)tail());
        var gear = model.GetNode<Node3D>("Gear"); Tape.Part(gear, "gear");
        int k = 0; foreach (var leg in gear.GetChildren()) if (leg is Node3D l) Tape.Part(l, "leg" + k++);
        var mount = model.GetNode<Node3D>("Gun"); Tape.Part(mount, "gun");
        Tape.Part(model.GetNode<Node3D>("Beacons"), "beacons");
        Tape.Sound(model, TapeSoundChannel.RotorLevel, () => engine.Level, v => { engine.Level = v; engine.StreamPaused = v < .004f; });
        Tape.Sound(mount, TapeSoundChannel.GunFiring, () => gun.Firing ? 1f : 0f, v => { gun.Firing = v > .5f; flash.Visible = v > .5f; });
        Tape.Sound(model, TapeSoundChannel.AircraftState, () => state() / 3f, v => setState?.Invoke((int)MathF.Round(v * 3f)));
    }

    /// <summary>The aircraft of a tape ("hind/<c>number</c>" without the kind): the play model, marked alike, nothing simulated.</summary>
    public static Node3D BuildReplayAircraft(string number, TyreEffects effects = null)
    {
        var model = BuildAircraft(number, out var engine, out var gun);
        var fx = new HindReplayFx { Name = "TapeState", Fx = effects, Model = model, Lights = model.GetNode<MilitaryLights>("NightLights") };
        model.AddChild(fx);
        MarkTape(model, number, () => 0, () => 0, engine, gun, model.GetNode<MeshInstance3D>("Gun/Flash"), () => fx.State, k => fx.State = k);
        return model;
    }
}

public partial class HindSorties
{
    /// <summary>Put a replay aircraft (made by <see cref="BuildReplayAircraft"/>) back to its freshly built state, so a seek can keep it: the recorded
    /// channels (rotor level, gun, state) and the visible flags the tape writes are returned to what the factory made; poses come from the tape.</summary>
    public static bool ResetReplayAircraft(Node3D model)
    {
        var fx = model.GetNodeOrNull<HindReplayFx>("TapeState");
        var engine = model.GetChildren().OfType<RotorSound>().FirstOrDefault();
        var gun = model.GetChildren().OfType<HindGunSound>().FirstOrDefault();
        var flash = model.GetNodeOrNull<MeshInstance3D>("Gun/Flash");
        if (fx == null || engine == null || gun == null || flash == null) return false;
        fx.State = 0; model.Scale = Vector3.One; model.Visible = true;
        engine.ResetState();
        gun.Firing = false; flash.Visible = false;
        return true;
    }
}

/// <summary>The state of a replayed aircraft that the pose cannot carry: its lights and the wreck's squashed height (applied each frame, after the poses).</summary>
public partial class HindReplayFx : Node
{
    public int State;
    public Node3D Model;
    public MilitaryLights Lights;
    public TyreEffects Fx;
    Vector3 _prev;
    bool _has;
    readonly float[] _soot = new float[2];

    /// <summary>The player's diesel smoke from both exhausts, as the live aircraft (HindSorties.Tick) makes it: while powered (not falling or wrecked) and moving; the speed is the pose's movement (a seek's jump is not speed).</summary>
    public override void _PhysicsProcess(double delta)
    {
        if (Fx == null || Model == null || !Model.IsInsideTree() || delta <= 0) return;
        Vector3 pos = Model.GlobalPosition, vel = _has ? (pos - _prev) / (float)delta : Vector3.Zero;
        _prev = pos; _has = true;
        if (State >= 2 || !Model.IsVisibleInTree() || vel.Length() < HindSorties.SootMinSpeed || vel.Length() > 120f) return;
        Fx.BeginReplay(0);
        try
        {
            for (int i = 0; i < 2; i++)
                Fx.EngineSoot(Model.GlobalTransform * HindSorties.SootPipes[i], Model.GlobalTransform.Basis.Z, vel, 0.6f, HindSorties.SootPower, ref _soot[i], (float)delta, kerosene: true);
        }
        finally { Fx.EndReplay(); }
    }

    public override void _Process(double delta)
    {
        Lights.Powered = State < 2; Lights.BeamAllowed = State == 1;
        var s = State == 3 ? new Vector3(1, .55f, 1) : Vector3.One;
        if (Model.Scale != s) Model.Scale = s;
    }
}
