using System;
using System.Collections.Generic;
using CCCPRacing.Audio;
using CCCPRacing.Car;
using CCCPRacing.Race;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Track;

/// <summary>Soviet concrete bus shelter with a mosaic back wall, bench and the yellow "А" sign.
/// Open side (and bench) face -Z, i.e. the road.</summary>
public static class BusShelter
{
    public static Node3D Build()
    {
        var n = new Node3D();
        // A sun-faded warm concrete contrasts with the grey roadside paving;
        // the teal roof and oxide-red fascia echo the varied painted/mosaic stops.
        var concrete = new Color(0.76f, 0.67f, 0.5f);
        var concreteLight = new Color(0.9f, 0.8f, 0.61f);
        var concreteDark = new Color(0.43f, 0.34f, 0.25f);
        var roof = new Color(0.2f, 0.39f, 0.38f);
        var roofLight = new Color(0.31f, 0.53f, 0.49f);
        var oxideRed = new Color(0.62f, 0.16f, 0.11f);
        var steel = new Color(0.22f, 0.24f, 0.23f);
        var wood = new Color(0.38f, 0.25f, 0.15f);
        // Precast concrete shell: deeper corner pilasters and a visible panel seam
        // make the shelter read as a heavy roadside structure at racing distance.
        n.AddChild(Props.Part(Props.Box(4.2f, 2.6f, 0.25f), concrete, new Vector3(0, 1.3f, 0.7f)));
        n.AddChild(Props.Part(Props.Box(0.25f, 2.6f, 1.6f), concrete, new Vector3(-2.0f, 1.3f, 0f)));
        n.AddChild(Props.Part(Props.Box(0.25f, 2.6f, 1.6f), concrete, new Vector3(2.0f, 1.3f, 0f)));
        foreach (float x in new[] { -1.94f, 1.94f })
            n.AddChild(Props.Part(Props.Box(0.34f, 2.68f, 0.34f), concreteLight, new Vector3(x, 1.34f, 0.66f)));
        n.AddChild(Props.Part(Props.Box(4.35f, 0.08f, 0.07f), concreteDark, new Vector3(0, 0.14f, 0.56f)));
        // Broad slab roof, with fascia, underside beams, drainage lip and shallow ribs.
        n.AddChild(Props.Part(Props.Box(4.8f, 0.2f, 2.3f), roof, new Vector3(0, 2.7f, -0.1f)));
        n.AddChild(Props.Part(Props.Box(4.92f, 0.22f, 0.16f), oxideRed, new Vector3(0, 2.65f, -1.18f)));
        n.AddChild(Props.Part(Props.Box(4.86f, 0.14f, 0.12f), roofLight, new Vector3(0, 2.61f, 1.04f)));
        for (int i = 0; i < 7; i++)
            n.AddChild(Props.Part(Props.Box(0.055f, 0.035f, 1.9f), roofLight,
                new Vector3(-2.05f + i * 0.68f, 2.595f, -0.08f)));
        foreach (float x in new[] { -1.35f, 0f, 1.35f })
            n.AddChild(Props.Part(Props.Box(0.12f, 0.12f, 1.45f), concreteDark, new Vector3(x, 2.48f, 0.12f)));
        // Mosaic: bold stripes and a sun on the inside of the back wall.
        Color[] tiles = { new(0.8f, 0.15f, 0.1f), new(0.95f, 0.75f, 0.2f), new(0.2f, 0.4f, 0.7f), new(0.9f, 0.9f, 0.85f) };
        for (int k = 0; k < 7; k++)
            n.AddChild(Props.Part(Props.Box(0.5f, 1.4f - (k % 3) * 0.3f, 0.05f), tiles[k % tiles.Length],
                new Vector3(-1.6f + k * 0.53f, 1.5f, 0.56f)));
        n.AddChild(Props.Part(Props.Cyl(0.35f, 0.06f, 10), tiles[1], new Vector3(0, 2.2f, 0.54f), new Vector3(90, 0, 0)));
        // Bench: separate worn slats, iron end frames, feet and back rail.
        for (int i = 0; i < 4; i++)
            n.AddChild(Props.Part(Props.Box(3.2f, 0.065f, 0.105f), i % 2 == 0 ? wood : new Color(0.45f, 0.31f, 0.19f),
                new Vector3(0, 0.53f, 0.18f + i * 0.11f)));
        for (int side = -1; side <= 1; side += 2)
        {
            float x = side * 1.3f;
            n.AddChild(Props.Part(Props.Box(0.09f, 0.44f, 0.07f), steel, new Vector3(x, 0.28f, 0.35f)));
            n.AddChild(Props.Part(Props.Box(0.09f, 0.08f, 0.56f), steel, new Vector3(x, 0.49f, 0.35f)));
            n.AddChild(Props.Part(Props.Box(0.16f, 0.07f, 0.65f), concreteDark, new Vector3(x, 0.06f, 0.35f)));
        }
        n.AddChild(Props.Part(Props.Box(3.05f, 0.09f, 0.09f), wood, new Vector3(0, 0.9f, 0.59f)));
        // Timetable case on the back wall, with a red route marker and paper inset.
        n.AddChild(Props.Part(Props.Box(0.86f, 0.82f, 0.1f), steel, new Vector3(-1.35f, 1.64f, 0.51f)));
        n.AddChild(Props.Part(Props.Box(0.7f, 0.66f, 0.035f), new Color(0.83f, 0.79f, 0.64f), new Vector3(-1.35f, 1.64f, 0.445f)));
        n.AddChild(Props.Part(Props.Box(0.74f, 0.13f, 0.04f), new Color(0.65f, 0.12f, 0.08f), new Vector3(-1.35f, 2.0f, 0.42f)));
        for (int i = 0; i < 4; i++)
            n.AddChild(Props.Part(Props.Box(0.48f - (i % 2) * 0.1f, 0.025f, 0.018f), concreteDark,
                new Vector3(-1.35f, 1.78f - i * 0.12f, 0.414f)));
        // Bus-stop sign: pole and a yellow plate with a black "А".
        n.AddChild(Props.Part(Props.Cyl(0.07f, 2.95f, 8), steel, new Vector3(2.8f, 1.48f, -0.8f)));
        n.AddChild(Props.Part(Props.Box(0.12f, 0.12f, 0.12f), concreteDark, new Vector3(2.8f, 0.06f, -0.8f)));
        n.AddChild(Props.Part(Props.Box(0.72f, 0.82f, 0.1f), concreteDark, new Vector3(2.8f, 2.6f, -0.8f)));
        var img = PixelFont.RenderImage("А", new Color(0.05f, 0.05f, 0.05f), new Color(0.95f, 0.8f, 0.15f), 2);
        n.AddChild(new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(0.6f, 0.7f) },
            Position = new Vector3(2.8f, 2.6f, -0.86f),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoTexture = ImageTexture.CreateFromImage(img),
                TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        return n;
    }
}

/// <summary>
/// Men waiting for a bus that they will certainly miss. They sway, take a pull from the bottle
/// every so often and cheer when you blast past ("УРА!"). One of them has given up and lies on
/// the bench. Hit one and he doesn't even notice.
/// </summary>
public partial class DrunkMen : Node3D
{
    const float SizeScale = 1.6f;
    const float Radius = 0.42f * SizeScale;
    static readonly string[] Cheers = { "УРА!", "НА ЗДОРОВЬЕ!", "ДАВАЙ!" };

    class Man
    {
        public Node3D Root, Body, Head, ArmR, LegL, LegR, Bottle;
        public Vector2 Pos;
        public float Yaw, Phase, SwayRate, DrinkTimer, Drinking, Cheer;
        public string Shout;
        public bool Lying;
        public int Obstacle = -1;
        public readonly Knockdown Down = new();     // hit by the car: falls over, gets up again
    }

    TrackData _t;
    readonly List<Man> _men = new();
    Man _mech;                          // the one asleep on the bench: the village's only mechanic
    Transform3D _shelter;
    float _shelterYaw;
    readonly RandomNumberGenerator _rng = new() { Seed = 1980 };
    float _clock;
    public bool HitThisTick { get; private set; }

    static readonly Color[] Coats = { new(0.35f, 0.33f, 0.3f), new(0.25f, 0.28f, 0.35f), new(0.4f, 0.3f, 0.22f) };
    static readonly Color Skin = new(0.86f, 0.66f, 0.56f);

    public void Init(TrackData t)
    {
        _t = t;
        Tape.Version(this, 1); Tape.Part(this, "root");
        bool someoneLying = false;
        foreach (var stop in t.BusStops)
        {
            var shelter = new Transform3D(new Basis(Vector3.Up, stop.Yaw), new Vector3(stop.Position.X, 0, stop.Position.Y));
            for (int k = 0; k < stop.Drunks; k++)
            {
                var m = new Man { Phase = _rng.RandfRange(0, 10), SwayRate = _rng.RandfRange(0.6f, 1.1f), DrinkTimer = _rng.RandfRange(3f, 10f) };
                if (!someoneLying && k == 0)
                {
                    // Asleep on the bench.
                    m.Lying = true;
                    someoneLying = true;
                    Vector3 p = shelter * new Vector3(0, 0, 0.35f);
                    m.Pos = new Vector2(p.X, p.Z);
                    m.Yaw = stop.Yaw + MathF.PI / 2f;
                    _mech = m;
                    _shelter = shelter;
                    _shelterYaw = stop.Yaw;
                }
                else
                {
                    Vector3 p = shelter * new Vector3(-2.0f + k * 1.9f, 0, -1.9f - _rng.RandfRange(0f, 0.7f));
                    m.Pos = new Vector2(p.X, p.Z);
                    m.Yaw = stop.Yaw + _rng.RandfRange(-0.6f, 0.6f);
                    m.Obstacle = t.Obstacles.Count;
                    t.Obstacles.Add(new Obstacle(m.Pos, Radius, default, false, 0.5f, true, 2.5f, Material: ImpactMaterial.Soft));     // knocked over, not a wall
                    m.Down.Bind(t, m.Obstacle);
                }
                Build(m);
                _men.Add(m);
            }
        }
        // No bus stop (the labour camp): just the mechanic, asleep on the bench by a barrack door.
        if (_mech == null && t.MechanicPost is { } post)
        {
            var m = new Man { Lying = true, Phase = _rng.RandfRange(0, 10), SwayRate = 0.8f, DrinkTimer = 8f };
            _shelter = post;
            _shelterYaw = post.Basis.GetEuler().Y;
            Vector3 p = post * new Vector3(0, 0, 0.35f);
            m.Pos = new Vector2(p.X, p.Z);
            m.Yaw = _shelterYaw + MathF.PI / 2f;
            _mech = m;
            Build(m);
            _men.Add(m);
        }
        if (AircraftMechanic) { ResetMechanic(); BuildCot(); }
        if (MechanicPostPosition is Vector2 post0)
        {
            // On the bonus track the symbol hangs over the mechanic's cot, not over the service spot.
            Vector2 service = AircraftMechanic ? LyingPos() : post0;
            AddChild(new MechanicMarker
            {
                Position = new Vector3(service.X, t.MeshHeight(service.X, service.Y) + 8.4f, service.Y),
            });
        }
    }

    /// <summary>A drunk's model, the nodes to pose (the bus's passengers are the same men).</summary>
    public class Figure { public Node3D Root, Body, Head, ArmR, LegL, LegR, Bottle; }

    /// <summary>The men waiting at the stop: what knocks them over (Breakables.AddKnock).</summary>
    public IEnumerable<(int obstacle, Func<Vector3, bool> hit)> Knocks()
    {
        foreach (var m in _men)
            if (m.Obstacle >= 0) yield return (m.Obstacle, m.Down.Hit);
    }

    public static Color RandomCoat(RandomNumberGenerator rng) => Coats[rng.RandiRange(0, Coats.Length - 1)];

    /// <summary>Builds a drunk (not yet in the tree), cartoon-sized like the others.</summary>
    public static Figure BuildFigure(Color coat)
    {
        var f = new Figure { Root = new Node3D { Scale = Vector3.One * SizeScale }, Body = new Node3D() };
        f.Root.AddChild(f.Body);
        var trousers = new Color(0.18f, 0.18f, 0.22f);
        // Legs hang from hip joints so the mechanic can walk.
        f.LegL = new Node3D { Position = new Vector3(-0.1f, 0.75f, 0) };
        f.LegR = new Node3D { Position = new Vector3(0.1f, 0.75f, 0) };
        f.Body.AddChild(f.LegL);
        f.Body.AddChild(f.LegR);
        f.LegL.AddChild(Props.Part(Props.Box(0.15f, 0.75f, 0.18f), trousers, new Vector3(0, -0.37f, 0)));
        f.LegR.AddChild(Props.Part(Props.Box(0.15f, 0.75f, 0.18f), trousers, new Vector3(0, -0.37f, 0)));
        f.Body.AddChild(Props.Part(Props.Box(0.48f, 0.62f, 0.3f), coat, new Vector3(0, 1.05f, 0)));
        f.Head = new Node3D { Position = new Vector3(0, 1.5f, 0) };
        f.Body.AddChild(f.Head);
        f.Head.AddChild(Props.Part(Props.Ball(0.14f), Skin, Vector3.Zero));
        f.Head.AddChild(Props.Part(Props.Ball(0.05f), new Color(0.9f, 0.15f, 0.1f), new Vector3(0, -0.02f, -0.14f)));       // nose
        f.Head.AddChild(Props.Part(Props.Box(0.32f, 0.07f, 0.34f), new Color(0.22f, 0.22f, 0.2f), new Vector3(0, 0.12f, 0)));  // flat cap
        f.Head.AddChild(Props.Part(Props.Box(0.26f, 0.03f, 0.12f), new Color(0.22f, 0.22f, 0.2f), new Vector3(0, 0.09f, -0.2f)));
        f.ArmR = new Node3D { Position = new Vector3(0.3f, 1.3f, 0) };
        f.Body.AddChild(f.ArmR);
        f.ArmR.AddChild(Props.Part(Props.Box(0.12f, 0.5f, 0.12f), coat, new Vector3(0, -0.25f, 0)));
        f.Bottle = Props.Part(Props.Cyl(0.05f, 0.3f, 6), new Color(0.2f, 0.5f, 0.25f), new Vector3(0, -0.55f, -0.08f), new Vector3(90, 0, 0));      // gripped across the fist: 90 degrees to the arm, not an extension of it
        f.ArmR.AddChild(f.Bottle);
        f.Body.AddChild(Props.Part(Props.Box(0.12f, 0.5f, 0.12f), coat, new Vector3(-0.3f, 1.05f, 0)));
        return f;
    }

    void Build(Man m)
    {
        var f = BuildFigure(RandomCoat(_rng));
        (m.Root, m.Body, m.Head, m.ArmR, m.LegL, m.LegR, m.Bottle) = (f.Root, f.Body, f.Head, f.ArmR, f.LegL, f.LegR, f.Bottle);
        AddChild(m.Root);
        if (m.Lying)
        {
            m.Body.Rotation = new Vector3(0, 0, MathF.PI / 2f);
            m.Body.Position = new Vector3(0.75f, 0.62f, 0);
        }
        // The world tape records the figure, limbs and bottle visibility. The
        // hammer is a fixed arm attachment, restored from that same taped arm
        // and the mutually-exclusive bottle visibility (RestoreMechanicTapeTool).
        string n = $"{_men.Count + 1}/";
        Tape.Part(m.Root, n + "figure"); Tape.Part(m.Body, n + "body"); Tape.Part(m.Head, n + "head"); Tape.Part(m.ArmR, n + "armR");
        Tape.Part(m.LegL, n + "legL"); Tape.Part(m.LegR, n + "legR"); Tape.Part(m.Bottle, n + "bottle");
    }

    public IEnumerable<(Vector3 pos, string text)> ActiveShouts()
    {
        foreach (var m in _men)
            if (m.Cheer > 0) yield return (m.Root.GlobalPosition + Vector3.Up * 2.3f * SizeScale, m.Shout);
            else if (m.Lying && (int)(_clock * 0.5f) % 3 == 0) yield return (m.Root.GlobalPosition + Vector3.Up * 1.6f * SizeScale, "Z Z Z");
    }

    public void Tick(float dt, CarSim car, float impact)
    {
        _clock += dt;
        HitThisTick = false;
        var carPos = new Vector2(car.Pos.X, car.Pos.Z);
        foreach (var m in _men)
        {
            float d = m.Pos.DistanceTo(carPos);
            if (m == _mech) { m.Cheer -= dt; Place(m); continue; }
            if (!m.Lying && d < Radius + 1.2f && impact > 1f) HitThisTick = true;
            if (!m.Lying && d < 14f && car.Speed > 11f)
            {
                if (m.Cheer <= 0)
                {
                    m.Shout = Cheers[_rng.RandiRange(0, Cheers.Length - 1)];
                    if (OS.HasEnvironment("DRUNKDBG")) GD.Print($"drunk cheers '{m.Shout}' at {_clock:F1} s, car {car.Speed * 3.6f:F0} km/h, {d:F1} m away");
                }
                m.Cheer = 2.5f;
            }
            m.Cheer -= dt;
            m.Down.Tick(dt);
            m.Phase += dt * m.SwayRate;
            m.DrinkTimer -= dt;
            if (m.DrinkTimer <= 0) { m.Drinking = 1.4f; m.DrinkTimer = _rng.RandfRange(6f, 12f); }
            m.Drinking -= dt;
            Place(m);
        }
    }

    void Place(Man m)
    {
        if (m == _mech && _mechPhase != Mech.Asleep) { PlaceMechanic(m); return; }
        float y = _t.MeshHeight(m.Pos.X, m.Pos.Y);
        m.Root.GlobalTransform = new Transform3D(m.Down.Tilt * new Basis(Vector3.Up, m.Yaw).Scaled(Vector3.One * SizeScale), new Vector3(m.Pos.X, y, m.Pos.Y));
        if (m.Lying)
        {
            m.Head.Rotation = new Vector3(0, 0, MathF.Sin(_clock * 0.8f) * 0.1f);
            return;
        }
        // Swaying from the ankles, a little stagger in a slow circle.
        float s1 = MathF.Sin(m.Phase * 1.3f), s2 = MathF.Sin(m.Phase * 0.9f + 1.7f);
        m.Body.Rotation = new Vector3(s2 * 0.12f, 0, s1 * 0.16f);
        if (m.Cheer > 0)
        {
            m.ArmR.Rotation = new Vector3(2.8f + MathF.Sin(_clock * 14f) * 0.25f, 0, 0.3f);   // bottle raised high
            m.Head.Rotation = new Vector3(-0.3f, 0, 0);
        }
        else if (m.Drinking > 0)
        {
            m.ArmR.Rotation = new Vector3(2.2f, 0, -0.5f);                                     // to the mouth
            m.Head.Rotation = new Vector3(-0.6f, 0, 0);
        }
        else
        {
            m.ArmR.Rotation = new Vector3(0.3f + s1 * 0.2f, 0, 0);
            m.Head.Rotation = new Vector3(0.2f, s2 * 0.3f, s1 * 0.2f);
        }
    }
}

/// <summary>
/// A PAZIK on the kolkhoz service, running the course the wrong way round at its own pace:
/// slow in the bends, stopping at both bus stops, braking and honking if a car is in its lane.
/// A heavy moving obstacle; impacts use its velocity. Honk at it head-on and it takes the ditch
/// (SovietBus.Ditch.cs).
/// </summary>
public partial class SovietBus : Node3D
{
    const float Length = 8f, Width = 2.5f;
    internal static readonly Vector3 SootPipe = new(-0.9f, 0.4f, Length / 2f + 0.1f);      // the rear pipe (model space)
    internal const float SootPower = 1.8f;      // a bus's big engine against the player's car
    const float Cruise = 8.5f, BendSpeed = 4f;

    TrackData _t;
    TyreEffects _fx;
    float _s, _speed, _stopTimer, _honkCd, _puff, _soot;
    int _lastStop = -1;
    readonly int[] _obs = new int[5];
    Node3D _model, _doorL, _doorR;
    DieselSound _sound;
    DrunkTractor _trafficTractor;
    SlurryTractor _trafficSlurry;
    public Vector2 Position2D { get; private set; }
    public Vector2 Forward2D { get; private set; }
    public float Speed => _speed;
    /// <summary>The bus in service (front at -Z); a burnt-out one is left behind and a spare gets a new model.</summary>
    public Node3D Model => _model;
    float _startS;

    /// <param name="wood">the wooden fences, which it simply flattens when it takes the ditch</param>
    /// <param name="staticObstacles">obstacles [0, this) are the track's fixed ones (buildings, walls, trees, poles)</param>
    public void Init(TrackData t, TyreEffects fx, float startS, WoodFences wood, int staticObstacles)
    {
        _t = t;
        _fx = fx;
        _startS = startS;
        _wood = wood;
        _staticObs = staticObstacles;
        for (int k = 0; k < _obs.Length; k++)
        {
            _obs[k] = t.Obstacles.Count;
            t.Obstacles.Add(new Obstacle(Vector2.Zero, 1.3f));
        }
        for (int k = 0; k < _wreckObs.Length; k++)
        {
            _wreckObs[k] = t.Obstacles.Count;
            t.Obstacles.Add(new Obstacle(Parked, 0.1f));
        }
        BuildModel();
        _sound = new DieselSound { BaseRate = 10f, RatePerSpeed = 2.5f, Body = 0.04f, Loudness = 3f, Reach = 110f };
        AddChild(_sound);     // not on the model: a burnt-out model is left behind as the wreck
        // The world tape: the in-service model (it moves, the Bus node does not), its doors and bumper, the engine sound node (its position follows the model) and speed.
        Tape.Version(this, 2); Tape.Part(this, "root"); Tape.Part(_model, "model"); Tape.Part(_doorL, "doorL"); Tape.Part(_doorR, "doorR");
        Tape.Part(_bumper, "bumper"); Tape.Part(_sound, "sound");
        Tape.Sound(_model, TapeSoundChannel.DieselSpeed, () => _sound.Speed, v => _sound.Speed = v);
        // For the bus DieselDozing carries the engine pause state (ignition through the spare departure).
        Tape.Sound(_model, TapeSoundChannel.DieselDozing, () => _sound.StreamPaused ? 1f : 0f, v => _sound.StreamPaused = v > 0.5f);
        if (Debug) GD.Print($"bus: route of {t.Center.Length} m, starts at metre {startS:F0}");
        Reset();
    }

    public void Reset()
    {
        ResetDitch();
        _s = _startS;
        _speed = Cruise;
        _stopTimer = 0;
        _lastStop = -1;
        if (!TapeDriven) TestPlace();
        Tick(0, null);
    }

    /// <summary>The driving school: the bus back on its route at metre s, cruising (the spare still leaves from the timetable's start).</summary>
    public void Warp(float s)
    {
        ResetDitch();
        _s = s;
        _speed = Cruise;
        _stopTimer = 0;
        _lastStop = -1;
        Tick(0, null);
    }

    void BuildModel()
    {
        _model = BuildModelBody(out _doorL, out _doorR, out _bumper);
        AddChild(_model);
    }

    // The same body is used by the native bus, its wreck and the tape's wreck.
    static Node3D BuildModelBody(out Node3D _doorL, out Node3D _doorR, out Node3D _bumper)
    {
        var _model = new Node3D { Name = "PAZIK" };
        var cream = new Color(0.92f, 0.86f, 0.66f);
        var red = new Color(0.72f, 0.16f, 0.1f);
        var glass = new Color(0.2f, 0.26f, 0.3f);
        var dark = new Color(0.1f, 0.1f, 0.1f);
        // Lower body red, window band, cream upper body, pale rounded roof. Front at -Z.
        _model.AddChild(Props.Part(Props.Box(Width, 1.0f, Length), red, new Vector3(0, 0.95f, 0)));
        _model.AddChild(Props.Part(Props.Box(Width - 0.02f, 0.85f, Length - 0.3f), glass, new Vector3(0, 1.88f, 0.1f)));
        _model.AddChild(Props.Part(Props.Box(Width, 0.45f, Length), cream, new Vector3(0, 2.52f, 0)));
        _model.AddChild(Props.Part(Props.Box(Width - 0.3f, 0.2f, Length - 0.4f), cream * 1.05f, new Vector3(0, 2.84f, 0)));
        for (float z = -2.6f; z <= 3.2f; z += 1.45f)   // window pillars
        {
            _model.AddChild(Props.Part(Props.Box(Width + 0.02f, 0.85f, 0.12f), cream, new Vector3(0, 1.88f, z)));
        }
        // PAZ-672 details: cream window frames, red waist stripe, side marker lamps and drip rails.
        var trim = new Color(0.96f, 0.91f, 0.76f);
        var chrome = new Color(0.58f, 0.59f, 0.55f);
        var amber = new Color(0.92f, 0.58f, 0.16f);
        var tail = new Color(0.72f, 0.08f, 0.05f);
        foreach (float x in new[] { -Width / 2f - 0.025f, Width / 2f + 0.025f })
        {
            for (float z = -3.25f; z <= 3.25f; z += 1.45f)
                _model.AddChild(Props.Part(Props.Box(0.055f, 0.92f, 0.07f), trim, new Vector3(x, 1.88f, z)));
            _model.AddChild(Props.Part(Props.Box(0.055f, 0.07f, 6.9f), red.Darkened(0.08f), new Vector3(x, 1.25f, 0)));
            _model.AddChild(Props.Part(Props.Box(0.08f, 0.045f, 7.0f), trim, new Vector3(x, 2.98f, 0)));
            foreach (float z in new[] { -3.2f, 3.25f })
                _model.AddChild(Props.Part(Props.Box(0.07f, 0.18f, 0.22f), amber, new Vector3(x, 1.25f, z)));
        }
        // Roof luggage rails, twin ventilation hatches and four small clearance lamps.
        foreach (float x in new[] { -0.88f, 0.88f })
        {
            _model.AddChild(Props.Part(Props.Box(0.045f, 0.12f, 6.4f), chrome, new Vector3(x, 3.0f, 0)));
            foreach (float z in new[] { -3.3f, 3.3f })
                _model.AddChild(Props.Part(Props.Box(0.18f, 0.08f, 0.12f), amber, new Vector3(x * 1.18f, 2.86f, z)));
        }
        foreach (float z in new[] { -1.1f, 1.1f })
        {
            _model.AddChild(Props.Part(Props.Box(0.65f, 0.08f, 0.8f), new Color(0.82f, 0.77f, 0.62f), new Vector3(0, 3.0f, z)));
            _model.AddChild(Props.Part(Props.Box(0.68f, 0.035f, 0.08f), chrome, new Vector3(0, 3.05f, z - 0.3f)));
        }
        // Front: windscreen, destination board, headlights, bumper.
        _model.AddChild(Props.Part(Props.Box(Width - 0.3f, 0.9f, 0.05f), glass, new Vector3(0, 1.85f, -Length / 2f - 0.01f)));
        _model.AddChild(Props.Part(Props.Box(0.055f, 0.82f, 0.06f), trim, new Vector3(0, 1.85f, -Length / 2f - 0.055f))); // split windscreen
        foreach (float x in new[] { -0.55f, 0.55f })
        {
            _model.AddChild(Props.Part(Props.Box(0.035f, 0.035f, 0.48f), dark, new Vector3(x, 1.47f, -Length / 2f - 0.07f), new Vector3(-12, 0, 0))); // wiper
            _model.AddChild(Props.Part(Props.Cyl(0.22f, 0.07f, 12), chrome, new Vector3(x * 1.65f, 0.88f, -Length / 2f - 0.07f), new Vector3(90, 0, 0)));
            _model.AddChild(Props.Part(Props.Cyl(0.14f, 0.075f, 10), new Color(1f, 0.85f, 0.52f), new Vector3(x * 1.65f, 0.88f, -Length / 2f - 0.12f), new Vector3(90, 0, 0)));
        }
        // Pressed-metal grille, towing eye, rear lamps and registration plate.
        _model.AddChild(Props.Part(Props.Box(0.9f, 0.38f, 0.045f), new Color(0.38f, 0.38f, 0.34f), new Vector3(0, 1.22f, -Length / 2f - 0.035f)));
        for (float y = 1.08f; y <= 1.36f; y += 0.07f)
            _model.AddChild(Props.Part(Props.Box(0.78f, 0.025f, 0.04f), chrome, new Vector3(0, y, -Length / 2f - 0.07f)));
        _model.AddChild(Props.Part(Props.Box(0.5f, 0.18f, 0.04f), trim, new Vector3(0, 0.62f, -Length / 2f - 0.08f)));
        foreach (float x in new[] { -0.88f, 0.88f })
        {
            _model.AddChild(Props.Part(Props.Box(0.24f, 0.42f, 0.05f), tail, new Vector3(x, 0.9f, Length / 2f + 0.025f)));
            _model.AddChild(Props.Part(Props.Box(0.28f, 0.12f, 0.06f), chrome, new Vector3(x, 0.44f, Length / 2f + 0.05f)));
        }
        var sign = PixelFont.RenderImage("КОЛХОЗ 7", new Color(1f, 0.85f, 0.3f), new Color(0.08f, 0.08f, 0.08f), 2);
        _model.AddChild(new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(1.8f, 0.35f) },
            Position = new Vector3(0, 2.5f, -Length / 2f - 0.03f),
            RotationDegrees = new Vector3(0, 180, 0),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoTexture = ImageTexture.CreateFromImage(sign),
                TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        foreach (float x in new[] { -0.85f, 0.85f })
            _model.AddChild(Props.Part(Props.Box(0.3f, 0.2f, 0.05f), new Color(1f, 0.95f, 0.75f), new Vector3(x, 0.85f, -Length / 2f - 0.02f)));
        _bumper = Props.Part(Props.Box(Width + 0.1f, 0.2f, 0.15f), new Color(0.6f, 0.6f, 0.6f), new Vector3(0, 0.45f, -Length / 2f - 0.05f));
        _model.AddChild(_bumper);
        foreach (float x in new[] { -0.95f, 0.95f })
        {
            var arm = Props.Part(Props.Box(0.035f, 0.035f, 0.48f), chrome, new Vector3(x, 2.1f, -3.45f), new Vector3(0, -12 * Math.Sign(x), 0));
            _model.AddChild(arm);
            _model.AddChild(Props.Part(Props.Box(0.32f, 0.22f, 0.12f), dark, new Vector3(x * 1.18f, 2.12f, -3.66f)));
        }
        // Doors on the right-hand (kerb) side, open when stopped.
        _doorL = new Node3D { Position = new Vector3(Width / 2f + 0.01f, 1.4f, -2.8f) };
        _doorR = new Node3D { Position = new Vector3(Width / 2f + 0.01f, 1.4f, 1.9f) };
        _model.AddChild(_doorL);
        _model.AddChild(_doorR);
        _doorL.AddChild(Props.Part(Props.Box(0.05f, 1.9f, 0.9f), dark, Vector3.Zero));
        _doorR.AddChild(Props.Part(Props.Box(0.05f, 1.9f, 0.9f), dark, Vector3.Zero));
        // Wheels.
        foreach (float z in new[] { -2.4f, 2.4f })
        foreach (float x in new[] { -1f, 1f })
        {
            _model.AddChild(Props.Part(Props.Cyl(0.5f, 0.3f, 10), dark, new Vector3(x * 1.1f, 0.5f, z), new Vector3(0, 0, 90)));
            _model.AddChild(Props.Part(Props.Cyl(0.25f, 0.035f, 12), chrome, new Vector3(x * 1.27f, 0.5f, z), new Vector3(0, 0, 90)));
            _model.AddChild(Props.Part(Props.Cyl(0.095f, 0.04f, 10), new Color(0.18f, 0.2f, 0.2f), new Vector3(x * 1.3f, 0.5f, z), new Vector3(0, 0, 90)));
        }
        return _model;
    }

    /// <summary>Advance along the course (backwards) and update pose/obstacles. car may be null; horn: the car is honking.</summary>
    public void Tick(float dt, CarSim car, bool horn = false, DrunkTractor tractor = null, SlurryTractor slurry = null)
    {
        if (TapeDriven) return;
        _trafficTractor = tractor;
        _trafficSlurry = slurry;
        if (DitchTick(dt, car, horn)) return;
        int n = _t.Center.Length;
        int i = (((int)_s) % n + n) % n;

        // Speed: slow for bends ahead (in the bus's direction), stop at bus stops.
        float turn = 0;
        Vector2 d0 = -_t.CenterDir(i);
        for (int k = 5; k < 35; k += 5)
            turn = Math.Max(turn, MathF.Abs(d0.AngleTo(-_t.CenterDir(((i - k) % n + n) % n))));
        float target = Mathf.Lerp(Cruise, BendSpeed, Math.Clamp(turn / 1.0f, 0, 1));

        foreach (var stop in _t.BusStops)
        {
            int ahead = ((i - stop.Index) % n + n) % n;         // metres until the stop (moving backwards)
            if (stop.Index != _lastStop && ahead < 25)
            {
                target = Math.Min(target, Math.Max(0f, (ahead - 1f) * 0.45f));
                if (ahead <= 2 && _stopTimer <= 0) { _stopTimer = 6f; _lastStop = stop.Index; }
            }
        }
        if (_stopTimer > 0) { _stopTimer -= dt; target = 0; }

        // Don't mow down the player: brake and honk if a car sits in our lane ahead.
        if (car != null)
        {
            Vector2 rel = new Vector2(car.Pos.X, car.Pos.Z) - Position2D;
            float ahead = rel.Dot(Forward2D), lat = rel.Dot(new Vector2(-Forward2D.Y, Forward2D.X));
            _honkCd -= dt;
            if (ahead > 3f && ahead < 18f && MathF.Abs(lat) < 2.6f)
            {
                target = 0;
                if (_honkCd <= 0) { _sound.Honk(); _honkCd = 2.5f; }
            }
        }

        // Yield early to farm traffic. A tractor just leaving a barn is still off the lane,
        // so reserve its crossing before its collision circles reach the road.
        float trafficAhead = float.PositiveInfinity;
        void Check(Vector2 p, float radius, bool crossing)
        {
            if (crossing)
            {
                if (MathF.Abs(p.Y - Position2D.Y) > 35f) return;
                p = new Vector2(p.X, Position2D.Y);
            }
            Vector2 rel = p - Position2D;
            float ahead = rel.Dot(Forward2D);
            float side = MathF.Abs(rel.Cross(Forward2D));
            if (ahead > 0 && ahead < 42f && side < Width / 2f + radius + (crossing ? 4f : 1.3f))
                trafficAhead = Math.Min(trafficAhead, ahead - radius);
        }
        if (tractor != null)
        {
            Check(_t.Obstacles[tractor.BodyObstacle].Center, 1.3f, false);
            Check(_t.Obstacles[tractor.PloughObstacle].Center, 1.1f, false);
            if (tractor.CrossingBarnRoad) Check(tractor.Position2D, 2f, true);
        }
        if (slurry != null)
        {
            Check(_t.Obstacles[slurry.BodyObstacle].Center, 1.3f, false);
            Check(_t.Obstacles[slurry.TankObstacle].Center, 1.5f, false);
            if (slurry.CrossingBarnRoad) Check(slurry.Position2D, 2f, true);
        }
        // ...and to the gulag truck when it is out on the road.
        foreach (var c in GulagTruck.Body) Check(c, GulagTruck.BodyRadius, false);
        if (trafficAhead < 42f)
            target = Math.Min(target, Math.Max(0f, (trafficAhead - 9f) * 0.45f));

        _speed = Mathf.MoveToward(_speed, target, (target < _speed ? 3.5f : 1f) * dt);
        float previousS = _s;
        _s = ((_s - _speed * dt) % n + n) % n;

        // Pose: on our side of the road (left of the racing direction), following the ground.
        RoutePose(_s, out Vector2 pos, out Vector2 fwd);
        if (TrafficBlocks(pos, fwd, tractor, slurry))
        {
            // The route is scripted, but the body may never step through another vehicle.
            // Stop at the last free pose even when braking started too late.
            float lo = 0f, hi = 1f;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                float mid = (lo + hi) * 0.5f;
                float s = ((previousS - _speed * dt * mid) % n + n) % n;
                RoutePose(s, out Vector2 testPos, out Vector2 testFwd);
                if (TrafficBlocks(testPos, testFwd, tractor, slurry)) hi = mid;
                else lo = mid;
            }
            _s = ((previousS - _speed * dt * lo) % n + n) % n;
            RoutePose(_s, out pos, out fwd);
            _speed = 0;
        }
        Place(pos, fwd, fwd * _speed, dt, 0, 0);
        if (horn) Hear(car);
    }

    /// <summary>Whether a tractor body would overlap the bus (including its burnt wreck).</summary>
    public bool Blocks(Vector2 pos, float radius)
    {
        foreach (int slot in _obs)
        {
            var o = _t.Obstacles[slot];
            if (pos.DistanceSquaredTo(o.Center) < MathF.Pow(radius + o.Radius + 0.08f, 2)) return true;
        }
        foreach (int slot in _wreckObs)
        {
            var o = _t.Obstacles[slot];
            if (pos.DistanceSquaredTo(o.Center) < MathF.Pow(radius + o.Radius + 0.08f, 2)) return true;
        }
        return false;
    }

    bool TrafficBlocks(Vector2 pos, Vector2 fwd, DrunkTractor tractor, SlurryTractor slurry)
    {
        bool Hits(int slot)
        {
            var o = _t.Obstacles[slot];
            for (int k = 0; k < _obs.Length; k++)
                if ((pos + fwd * (-3.2f + k * 1.6f)).DistanceSquaredTo(o.Center)
                    < MathF.Pow(BodyR + o.Radius + 0.08f, 2)) return true;
            return false;
        }
        bool HitsTruck()
        {
            for (int k = 0; k < _obs.Length; k++)
                if (GulagTruck.BlocksAt(pos + fwd * (-3.2f + k * 1.6f), BodyR)) return true;
            return false;
        }
        return (tractor != null && (Hits(tractor.BodyObstacle) || Hits(tractor.PloughObstacle)))
            || (slurry != null && (Hits(slurry.BodyObstacle) || Hits(slurry.TankObstacle)))
            || HitsTruck();
    }

    /// <summary>Where the timetable puts the bus at route metre s: its lane, facing against the race.</summary>
    void RoutePose(float s, out Vector2 pos, out Vector2 fwd)
    {
        int n = _t.Center.Length;
        int i = (((int)s) % n + n) % n;
        float f = s - MathF.Floor(s);
        Vector2 c = _t.Center[i].Lerp(_t.Center[(i + 1) % n], f);
        fwd = -_t.CenterDir(i);
        Vector2 right = new(-fwd.Y, fwd.X);
        pos = c + right * (_t.HalfWidth[i] * LaneOffset);
    }

    /// <summary>
    /// Model on the ground at pos facing fwd (plus extra roll/pitch in radians: the ditch),
    /// doors, collision circles moving with it (vel), engine sound and exhaust.
    /// </summary>
    void Place(Vector2 pos, Vector2 fwd, Vector2 vel, float dt, float roll, float pitch)
    {
        Position2D = pos;
        Forward2D = fwd;
        Vector2 right = new(-fwd.Y, fwd.X);
        float hF = _t.MeshHeight(pos.X + fwd.X * 2.4f, pos.Y + fwd.Y * 2.4f);
        float hB = _t.MeshHeight(pos.X - fwd.X * 2.4f, pos.Y - fwd.Y * 2.4f);
        float hR = _t.MeshHeight(pos.X + right.X * 1.1f, pos.Y + right.Y * 1.1f);
        float hL = _t.MeshHeight(pos.X - right.X * 1.1f, pos.Y - right.Y * 1.1f);
        var b = new Basis(Vector3.Up, TrackData.YawFor(fwd))
              * new Basis(Vector3.Right, MathF.Atan2(hF - hB, 4.8f) + pitch)
              * new Basis(Vector3.Back, MathF.Atan2(hR - hL, 2.2f) * 0.8f + roll);
        _model.GlobalTransform = new Transform3D(b, new Vector3(pos.X, (hF + hB) * 0.5f, pos.Y));
        if (_sound != null) _sound.Position = _model.Position;

        bool open = _stopTimer > 0.5f;
        _doorL.Position = new Vector3(Width / 2f + (open ? 0.15f : 0.01f), 1.4f, open ? -3.3f : -2.8f);
        _doorR.Position = new Vector3(Width / 2f + (open ? 0.15f : 0.01f), 1.4f, open ? 2.4f : 1.9f);

        for (int k = 0; k < _obs.Length; k++)
            _t.Obstacles[_obs[k]] = new Obstacle(pos + fwd * (-3.2f + k * 1.6f), 1.3f, vel, Material: ImpactMaterial.Metal);

        if (dt > 0)
        {
            float speed = vel.Length();
            _sound.Speed = speed;
            Vector3 pipe = _model.GlobalTransform * SootPipe;
            // the player's diesel smoke, a bus's big engine (the pipe is at the rear)
            _fx.EngineSoot(pipe, new Vector3(-fwd.X, 0, -fwd.Y), new Vector3(vel.X, 0, vel.Y), Mathf.Clamp(speed / 8f, 0.25f, 1f), SootPower, ref _soot, dt);
        }
    }
}
