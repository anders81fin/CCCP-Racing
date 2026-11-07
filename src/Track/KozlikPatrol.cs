using System;
using System.Collections.Generic;
using CCCPRacing.Audio;
using CCCPRacing.Car;
using CCCPRacing.UI;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Track;

/// <summary>
/// An open Kozlik on guard duty: a driver in a huge furazhka and, on the back seat, a big brown
/// bear, the duty officer of the area, in an even more enormous one. It drives the course in the racing direction on the right half of the road, slows for
/// bends and now and then pulls off to guard the radar station or the hangar for a while
/// (the bear looks around, the driver smokes). It waits for the Balalaikas rather than blocking the
/// runway. A moving obstacle with velocity, like the bus. Everything resets on restart.
/// The GAI post's ambush (the rally): the Kozlik waits a while just past the radar at every pass;
/// a car caught speeding there has it after it (<see cref="StartChase"/>), siren wailing and blue
/// beacon flashing. On the car's tail for a moment, both stop and the bear asks for an autograph
/// for his wife (<see cref="Caught"/>, then <see cref="EndAutograph"/>); left far behind or after
/// half a minute, he gives up and shakes his paw (<see cref="GaveUp"/>).
/// </summary>
public partial class KozlikPatrol : Node3D
{
    const float ModelScale = 1.05f;                   // modest stylisation; comparable with the player rally car
    const float Length = 4.0f * ModelScale, Width = 1.95f * ModelScale;
    const float Cruise = 15.5f, BendSpeed = 8.5f, DetourSpeed = 6.5f;
    const float Lane = 0.45f, LaneAside = 0.72f; // lateral offset (share of half width) normal / giving way to a Balalaika
    const float ObsRadius = Width * .5f;
    const float GroundHalfTrack = .78f * ModelScale;

    enum Mode { Road, ToGuard, Guard, Back, Chase, Autograph }

    // The chase: flat out on the straights, quicker than on patrol through the bends, closing in to
    // TailGap metres behind the car; caught after CatchTime there, given up after GiveUpTime or GiveUpGap.
    const float ChaseBrake = 9f;
    const float ChaseMax = 40f, ChaseBend = 13f, ChaseOut = 12f, TailGap = 6.5f, CatchTime = 1.5f, GiveUpTime = 30f, GiveUpGap = 170f;

    class GuardSpot
    {
        public string Name;
        public Vector2 Spot, Face;
        public int Leave, Rejoin, Passes, Parity;
    }

    TrackData _t;
    TyreEffects _fx;
    public Node3D Model => _model;
    internal static readonly Vector3 SootPipe = new(0.6f, 0.4f, Length / 2f + 0.05f);
    BalalaikaSorties _jets;
    public HindSorties StrikeAircraft { get; set; }
    SovietBus _bus;
    readonly List<GuardSpot> _spots = new();
    readonly int[] _obs = new int[3];
    readonly RandomNumberGenerator _rng = new() { Seed = 1972 };
    int _rwFrom = -1;

    Mode _mode;
    float _s, _lat, _speed, _guardTimer, _clock, _puff, _soot, _dbg, _drink, _drinkCd, _wave, _waveCd, _hic, _hicCd, _angry, _smoke, _wheelSpin;
    GuardSpot _cur, _trapSpot;
    float _chaseT, _caughtT, _askT, _pause;
    bool _chaseOut;
    int _chaseJoin, _carK = -1;
    float _gapToCar = float.MaxValue;     // in the chase: road metres to the car
    Node3D _beacon;
    MeshInstance3D _beaconL, _beaconR;
    OmniLight3D _beaconLight;
    SirenSound _siren;
    Vector2[] _path;
    float[] _pathLen;
    float _ps;
    bool _waiting;

    Node3D _model, _body, _driverHead, _driverArm, _bear, _bearHead, _bearArmR, _bearArmL, _bottle;
    const float BottleRest = 1.2f;       // left arm angle holding the bottle up in front of him
    readonly List<Node3D> _wheels = new();
    DieselSound _sound;

    /// <summary>Road start position in centre-line metres (test hook: set before Reset).</summary>
    public float StartS;
    /// <summary>Test hook: guard at every pass instead of every other one.</summary>
    public bool AlwaysGuard;
    /// <summary>Test hook: drive the centre line (straight through a car parked on it).</summary>
    public bool CentreLine;
    public Vector2 Position2D { get; private set; }
    public Vector2 Forward2D { get; private set; }
    public float Speed => _speed;
    public bool Guarding => _mode == Mode.Guard;
    /// <summary>The Kozlik's body, front at -Z, scaled by ModelScale (the night hangs its lamps on it).</summary>
    public Node3D Body => _body;
    /// <summary>Waiting in the ambush beside the GAI post.</summary>
    public bool AtTrap => _mode == Mode.Guard && _cur != null && _cur == _trapSpot;
    /// <summary>After the car, or stopped behind it for the autograph.</summary>
    public bool Chasing => _mode is Mode.Chase or Mode.Autograph;
    /// <summary>On the car's tail long enough: both stop for the autograph.</summary>
    public event Action Caught;
    /// <summary>Left behind or out of time: the chase is over.</summary>
    public event Action GaveUp;
    /// <summary>The car ran into us last tick (for commentary).</summary>
    public bool HitThisTick { get; private set; }

    float _warnCd, _hitCd;

    public void Init(TrackData t, TyreEffects fx, BalalaikaSorties jets, SovietBus bus, float startS)
    {
        _t = t;
        _fx = fx;
        _jets = jets;
        _bus = bus;
        StartS = startS;
        int n = t.Center.Length;
        if (t.Runway != null) _rwFrom = ((int)t.Runway.From % n + n) % n;

        foreach (var p in t.Props)
        {
            var at = new Vector2(p.Position.X, p.Position.Z);
            if (p.Type == "radar")
            {
                // Park on the shoulder between the road and the radar fence, facing along the road.
                int ci = t.NearestCenter(at, out float d);
                Vector2 toRoad = (t.Center[ci] - at).Normalized();
                var spot = t.Id == "siberia" ? SiberiaRadar.Guard : at + toRoad * Math.Min(8.5f, d - t.HalfWidth[ci] - 1.5f);
                AddSpot("radar", spot, t.CenterDir(ci), 1);
            }
            else if (p.Type == "hangar")
            {
                // Beside the hangar mouth, off the Balalaikas' taxi line, looking across the apron.
                Vector2 outDir = new(MathF.Sin(p.Rotation), MathF.Cos(p.Rotation)), side = new(outDir.Y, -outDir.X);
                AddSpot("hangar", at + outDir * 19f - side * 14f, side, 0);
            }
        }
        for (int k = 0; k < _obs.Length; k++)
        {
            _obs[k] = t.Obstacles.Count;
            t.Obstacles.Add(new Obstacle(new Vector2(-1e4f, -1e4f), 0.1f));
        }
        BuildModel();
        _sound = new DieselSound { Voice=DieselSound.EngineVoice.Petrol, BaseRate=24, RatePerSpeed=3.3f, Body=.1f, Loudness=1, Reach=180, Position=new(0,.8f,-1.3f) };
        _model.AddChild(_sound);
        MarkTape();
        Reset();
    }

    /// <summary>The world tape parts: the model (it moves, the Kozlik node does not), body, crew, wheels (spinners), beacon lamps.</summary>
    void MarkTape()
    {
        Tape.Version(this, 2); Tape.Part(this, "root"); Tape.Part(_model, "model"); Tape.Part(_body, "body");
        Tape.Part(_driverHead, "driverHead"); Tape.Part(_driverArm, "driverArm");
        Tape.Part(_bear, "bear"); Tape.Part(_bearHead, "bearHead"); Tape.Part(_bearArmR, "bearArmR"); Tape.Part(_bearArmL, "bearArmL"); Tape.Part(_bottle, "bottle");
        Tape.Part(_beacon, "beacon"); Tape.Part(_beaconL, "beaconL"); Tape.Part(_beaconR, "beaconR"); Tape.Part(_beaconLight, "beaconLight");
        for (int i = 0; i < _wheels.Count; i++) { Tape.Part(_wheels[i], "wheel" + i); Tape.Spin(_wheels[i], Vector3.Right, () => _wheelSpin); }
        Tape.Sound(_model, TapeSoundChannel.DieselSpeed, () => _sound.Speed, v => _sound.Speed = v);
    }

    void AddSpot(string name, Vector2 spot, Vector2 face, int parity)
    {
        int n = _t.Center.Length;
        int ci = _t.NearestCenter(spot, out _);
        var g = new GuardSpot
        {
            Name = name, Spot = spot, Face = face.Normalized(), Parity = parity,
            Leave = ((ci - 25) % n + n) % n,
            // The Siberia radar fence sits inside a tight serpentine: rejoin beside the bay,
            // rather than cutting through the enclosure towards a point 30 m further west.
            Rejoin = (ci + (name == "radar" && _t.Id == "siberia" ? 0 : 30)) % n,
        };
        _spots.Add(g);
        if (OS.HasEnvironment("KOZLIKDBG"))
            GD.Print($"Kozlik guard spot {name} at ({spot.X:F1},{spot.Y:F1}), leave {g.Leave} m, rejoin {g.Rejoin} m, clearance {StaticClearance(Leg(g, true)):F1} / {StaticClearance(Leg(g, false)):F1} m");
    }

    /// <summary>
    /// The GAI post's ambush: a spot beside the road 28 m of road past the radar (at: its road metre,
    /// side: +1 right of the racing direction, -1 left), visited at every pass and kept longer than the others.
    /// </summary>
    public void AddTrapSpot(int at, float side)
    {
        int n = _t.Center.Length, k = (at + 28) % n;
        Vector2 d = _t.CenterDir(k);
        AddSpot("gai", _t.Center[k] + new Vector2(-d.Y, d.X) * side * (_t.HalfWidth[k] + 4.5f), d, 0);
        _trapSpot = _spots[^1];
    }

    public void Reset()
    {
        int n = _t.Center.Length;
        _s = ((StartS % n) + n) % n;
        _mode = Mode.Road;
        _steer = 0;
        _reverse = false;
        _driveDt = 0;
        _speed = Cruise;
        _lat = CentreLine ? 0 : Lane * _t.HalfWidth[(int)_s % n];
        _cur = null;
        _path = null;
        _waiting = false;
        _angry = _drink = _wave = _hic = _smoke = 0;
        _waveCd = 6f;
        _hicCd = 4f;
        _drinkCd = 11f;
        _warnCd = _hitCd = 0;
        _chaseT = _caughtT = _askT = _pause = 0;
        _chaseOut = false;
        Siren(false);
        foreach (var g in _spots) g.Passes = 0;
        PlaceOnRoad(n);
        Position2D = _guidePosition;
        Forward2D = _guideForward;
        _velocity = Forward2D * _speed;
        Tick(0, null, 0);
    }

    // ------------------------------------------------------------ model

    void BuildModel()
    {
        _model = new Node3D { Name = "Kozlik" };
        AddChild(_model);
        _body = new Node3D { Scale = Vector3.One * ModelScale };
        _model.AddChild(_body);
        var olive = new Color(0.33f, 0.37f, 0.2f);
        var oliveDark = new Color(0.25f, 0.28f, 0.15f);
        var black = new Color(0.08f, 0.08f, 0.08f);
        var seat = new Color(0.2f, 0.15f, 0.1f);
        var chrome = new Color(0.55f, 0.55f, 0.52f);
        var b = _body;
        // Boxy body, front at -Z: lower tub, raised bonnet, open cabin with low side walls.
        b.AddChild(Props.Part(Props.Box(1.7f, 0.4f, 3.9f), olive, new Vector3(0, 0.75f, 0)));
        b.AddChild(Props.Part(Props.Box(1.7f, 0.32f, 1.4f), olive, new Vector3(0, 1.1f, -1.25f)));      // bonnet
        b.AddChild(Props.Part(Props.Box(1.5f, 0.02f, 2.5f), oliveDark * 0.7f, new Vector3(0, 0.96f, 0.65f)));  // cabin floor
        foreach (float s in new[] { -1f, 1f })
        {
            b.AddChild(Props.Part(Props.Box(0.1f, 0.34f, 2.55f), olive, new Vector3(s * 0.8f, 1.1f, 0.67f)));   // cabin sides
            b.AddChild(Props.Part(Props.Box(0.3f, 0.1f, 1.0f), oliveDark, new Vector3(s * 0.82f, 1.02f, -1.2f)));  // front wings
            b.AddChild(Props.Part(Props.Box(0.3f, 0.1f, 1.0f), oliveDark, new Vector3(s * 0.82f, 1.02f, 1.2f)));   // rear wings
        }
        b.AddChild(Props.Part(Props.Box(1.7f, 0.34f, 0.1f), olive, new Vector3(0, 1.1f, 1.9f)));        // tailgate
        // Front: grille slots, round headlights, bumper. Red star on the bonnet.
        b.AddChild(Props.Part(Props.Box(1.1f, 0.55f, 0.05f), black, new Vector3(0, 0.95f, -1.96f)));
        for (int k = -3; k <= 3; k++)
            b.AddChild(Props.Part(Props.Box(0.06f, 0.5f, 0.05f), oliveDark, new Vector3(k * 0.14f, 0.95f, -1.99f)));
        foreach (float s in new[] { -1f, 1f })
        {
            b.AddChild(Props.Part(Props.Cyl(0.15f, 0.08f, 10), chrome, new Vector3(s * 0.66f, 1.02f, -1.97f), new Vector3(90, 0, 0)));
            b.AddChild(Props.Part(Props.Cyl(0.11f, 0.1f, 10), new Color(1f, 0.95f, 0.7f), new Vector3(s * 0.66f, 1.02f, -1.99f), new Vector3(90, 0, 0)));
            b.AddChild(Props.Part(Props.Box(0.14f, 0.12f, 0.05f), new Color(0.7f, 0.08f, 0.05f), new Vector3(s * 0.65f, 0.85f, 1.97f)));   // tail lights
        }
        b.AddChild(Props.Part(Props.Box(1.95f, 0.14f, 0.14f), black, new Vector3(0, 0.6f, -2.02f)));
        b.AddChild(Props.Part(Props.Box(1.95f, 0.14f, 0.14f), black, new Vector3(0, 0.6f, 2.0f)));
        var red = new Color(0.8f, 0.1f, 0.08f);
        for (int k = 0; k < 5; k++)   // five-pointed star from thin rays
            b.AddChild(Props.Part(Props.Box(0.08f, 0.02f, 0.26f), red, new Vector3(0, 1.265f, -1.2f), new Vector3(0, k * 72f, 0)));
        // Windscreen frame (folded up), dashboard, steering wheel.
        var screen = new Node3D { Position = new Vector3(0, 1.26f, -0.55f), RotationDegrees = new Vector3(-12, 0, 0) };
        b.AddChild(screen);
        foreach (float s in new[] { -1f, 1f })
            screen.AddChild(Props.Part(Props.Box(0.07f, 0.6f, 0.07f), oliveDark, new Vector3(s * 0.8f, 0.3f, 0)));
        screen.AddChild(Props.Part(Props.Box(1.67f, 0.08f, 0.08f), oliveDark, new Vector3(0, 0.6f, 0)));
        screen.AddChild(Props.Part(Props.Box(1.55f, 0.5f, 0.02f), new Color(0.55f, 0.66f, 0.72f), new Vector3(0, 0.32f, 0)));
        b.AddChild(Props.Part(Props.Box(1.5f, 0.2f, 0.15f), black, new Vector3(0, 1.2f, -0.45f)));
        b.AddChild(Props.Part(Props.Cyl(0.18f, 0.04f, 10), black, new Vector3(-0.4f, 1.38f, -0.25f), new Vector3(60, 0, 0)));
        // Seats: two up front, a bench at the back for the bear.
        foreach (float s in new[] { -1f, 1f })
        {
            b.AddChild(Props.Part(Props.Box(0.55f, 0.12f, 0.5f), seat, new Vector3(s * 0.4f, 1.02f, 0.1f)));
            b.AddChild(Props.Part(Props.Box(0.55f, 0.5f, 0.12f), seat, new Vector3(s * 0.4f, 1.3f, 0.38f)));
        }
        b.AddChild(Props.Part(Props.Box(1.5f, 0.12f, 0.55f), seat, new Vector3(0, 1.02f, 1.25f)));
        b.AddChild(Props.Part(Props.Box(1.5f, 0.45f, 0.12f), seat, new Vector3(0, 1.28f, 1.62f)));
        // Folded canvas top behind the bench, whip antenna, spare wheel on the tailgate.
        b.AddChild(Props.Part(Props.Box(1.6f, 0.2f, 0.32f), new Color(0.4f, 0.4f, 0.28f), new Vector3(0, 1.35f, 1.8f)));
        b.AddChild(Props.Part(Props.Cyl(0.015f, 1.8f, 4), black, new Vector3(0.78f, 2.1f, 1.8f)));
        b.AddChild(Props.Part(Props.Cyl(0.42f, 0.26f, 10), black, new Vector3(0.25f, 1.05f, 2.12f), new Vector3(90, 0, 0)));
        b.AddChild(Props.Part(Props.Cyl(0.22f, 0.28f, 8), olive, new Vector3(0.25f, 1.05f, 2.13f), new Vector3(90, 0, 0)));
        // Big off-road tyres (spin with speed).
        foreach (float z in new[] { -1.2f, 1.2f })
        foreach (float s in new[] { -1f, 1f })
        {
            var w = new Node3D { Position = new Vector3(s * 0.78f, 0.44f, z) };
            b.AddChild(w);
            w.AddChild(Props.Part(Props.Cyl(0.44f, 0.34f, 8), black, Vector3.Zero, new Vector3(0, 0, 90)));
            w.AddChild(Props.Part(Props.Cyl(0.2f, 0.36f, 6), olive, Vector3.Zero, new Vector3(0, 0, 90)));
            _wheels.Add(w);
        }

        BuildDriver();
        BuildBear();
        BuildBeacon();
    }

    /// <summary>Two big blue lamps on the ends of the windscreen frame (either side of the crew, so the
    /// chase camera sees them past the bear's cap), flashing in turn in the chase; a blue light around them at night.</summary>
    void BuildBeacon()
    {
        _beacon = new Node3D { Position = new Vector3(0, 1.93f, -0.62f), Visible = false };
        _body.AddChild(_beacon);
        _beacon.AddChild(Props.Part(Props.Box(1.8f, 0.06f, 0.2f), new Color(0.1f, 0.1f, 0.12f), Vector3.Zero));
        MeshInstance3D Lamp(float x) => new()
        {
            Mesh = Props.Box(0.42f, 0.34f, 0.34f),
            Position = new Vector3(x, 0.2f, 0),
            MaterialOverride = NightGlow.Keep(new StandardMaterial3D { AlbedoColor = new Color(0.25f, 0.45f, 1f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded }),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        _beaconL = Lamp(-0.72f);
        _beaconR = Lamp(0.72f);
        _beacon.AddChild(_beaconL);
        _beacon.AddChild(_beaconR);
        _beaconLight = new OmniLight3D { LightColor = new Color(0.3f, 0.5f, 1f), OmniRange = 12f, LightEnergy = 1.6f, ShadowEnabled = false, Visible = false, Position = new Vector3(0, 0.6f, 0) };
        _beacon.AddChild(_beaconLight);
        _siren = new SirenSound { Loudness = -2f };
        _model.AddChild(_siren);
    }

    void Siren(bool on)
    {
        if (_beacon == null) return;
        _beacon.Visible = on;
        _beaconLight.Visible = on;
        _siren.On = on;
    }

    void BuildDriver()
    {
        var tunic = new Color(0.36f, 0.38f, 0.24f);
        var capGreen = new Color(0.44f, 0.52f, 0.3f);
        var skin = new Color(0.86f, 0.66f, 0.56f);
        // Left-hand drive: the driver sits on the -X seat.
        var d = new Node3D { Position = new Vector3(-0.4f, 1.08f, 0.1f) };
        _body.AddChild(d);
        d.AddChild(Props.Part(Props.Box(0.5f, 0.55f, 0.32f), tunic, new Vector3(0, 0.28f, 0)));
        d.AddChild(Props.Part(Props.Box(0.54f, 0.08f, 0.3f), new Color(0.75f, 0.1f, 0.08f), new Vector3(0, 0.52f, 0)));   // shoulder boards
        d.AddChild(Props.Part(Props.Box(0.12f, 0.12f, 0.45f), tunic, new Vector3(0.2f, 0.42f, -0.25f)));   // right arm on the wheel
        _driverArm = new Node3D { Position = new Vector3(-0.28f, 0.5f, -0.05f) };                          // left arm: wheel / cigarette
        d.AddChild(_driverArm);
        _driverArm.AddChild(Props.Part(Props.Box(0.12f, 0.12f, 0.45f), tunic, new Vector3(0, 0, -0.2f)));
        _driverArm.AddChild(Props.Part(Props.Box(0.03f, 0.03f, 0.12f), new Color(0.95f, 0.95f, 0.9f), new Vector3(0, 0.05f, -0.45f)));  // papirosa
        _driverHead = new Node3D { Position = new Vector3(0, 0.72f, 0) };
        d.AddChild(_driverHead);
        _driverHead.AddChild(Props.Part(Props.Ball(0.16f), skin, Vector3.Zero));
        _driverHead.AddChild(Props.Part(Props.Box(0.2f, 0.04f, 0.04f), new Color(0.2f, 0.14f, 0.1f), new Vector3(0, -0.06f, -0.15f)));   // moustache
        // The furazhka: an aerodrome-sized crown, red band, black visor, gold badge.
        _driverHead.AddChild(Props.Part(Props.Cyl(0.2f, 0.12f, 12), new Color(0.85f, 0.1f, 0.08f), new Vector3(0, 0.15f, 0)));
        _driverHead.AddChild(Props.Part(Props.Cyl(0.22f, 0.14f, 12, 0.44f), capGreen, new Vector3(0, 0.28f, 0)));
        _driverHead.AddChild(Props.Part(Props.Box(0.3f, 0.03f, 0.18f), new Color(0.05f, 0.05f, 0.05f), new Vector3(0, 0.1f, -0.24f), new Vector3(-15, 0, 0)));
        _driverHead.AddChild(Props.Part(Props.Box(0.1f, 0.1f, 0.03f), new Color(0.95f, 0.78f, 0.2f), new Vector3(0, 0.16f, -0.22f)));
    }

    void BuildBear()
    {
        var fur = new Color(0.4f, 0.25f, 0.13f);
        var furLight = new Color(0.58f, 0.42f, 0.26f);
        var dark = new Color(0.06f, 0.05f, 0.04f);
        // Sits upright on the back bench, bigger than the driver.
        _bear = new Node3D { Position = new Vector3(0.1f, 1.05f, 1.2f), Scale = Vector3.One * 1.15f };
        _body.AddChild(_bear);
        var torso = Props.Part(Props.Ball(0.55f, 8), fur, new Vector3(0, 0.55f, 0));
        torso.Scale = new Vector3(1f, 1.2f, 0.85f);
        _bear.AddChild(torso);
        _bear.AddChild(Props.Part(Props.Ball(0.36f, 8), furLight, new Vector3(0, 0.45f, -0.25f)));     // belly
        foreach (float s in new[] { -1f, 1f })
        {
            _bear.AddChild(Props.Part(Props.Ball(0.24f), fur, new Vector3(s * 0.28f, 0.1f, -0.45f)));  // thighs forward
            _bear.AddChild(Props.Part(Props.Ball(0.14f), furLight, new Vector3(s * 0.3f, 0.02f, -0.72f)));  // soles
        }
        _bearHead = new Node3D { Position = new Vector3(0, 1.45f, -0.05f) };
        _bear.AddChild(_bearHead);
        _bearHead.AddChild(Props.Part(Props.Ball(0.34f, 8), fur, Vector3.Zero));
        foreach (float s in new[] { -1f, 1f })
        {
            _bearHead.AddChild(Props.Part(Props.Ball(0.12f), fur, new Vector3(s * 0.25f, 0.26f, 0.02f)));      // round ears
            _bearHead.AddChild(Props.Part(Props.Ball(0.06f), furLight, new Vector3(s * 0.25f, 0.27f, -0.06f)));
            _bearHead.AddChild(Props.Part(Props.Ball(0.045f), dark, new Vector3(s * 0.13f, 0.08f, -0.29f)));    // eyes
        }
        _bearHead.AddChild(Props.Part(Props.Ball(0.16f), furLight, new Vector3(0, -0.07f, -0.3f)));   // snout
        if (_t.Zone)
        {
            // In the zone the duty officer wears his gas mask: a grey rubber face over the snout,
            // two round glass eyes, the filter can hanging under the chin.
            var rubber = new Color(0.28f, 0.3f, 0.27f);
            _bearHead.AddChild(Props.Part(Props.Ball(0.3f, 8), rubber, new Vector3(0, -0.04f, -0.2f)));
            foreach (float sx in new[] { -1f, 1f })
            {
                _bearHead.AddChild(Props.Part(Props.Cyl(0.09f, 0.06f, 10), new Color(0.12f, 0.12f, 0.12f), new Vector3(sx * 0.13f, 0.06f, -0.44f), new Vector3(90, 0, 0)));
                _bearHead.AddChild(Props.Part(Props.Cyl(0.065f, 0.07f, 10), new Color(0.55f, 0.68f, 0.74f), new Vector3(sx * 0.13f, 0.06f, -0.46f), new Vector3(90, 0, 0)));
            }
            _bearHead.AddChild(Props.Part(Props.Cyl(0.05f, 0.16f, 6), rubber, new Vector3(0, -0.2f, -0.46f), new Vector3(60, 0, 0)));   // the hose
            _bearHead.AddChild(Props.Part(Props.Cyl(0.1f, 0.2f, 10), new Color(0.36f, 0.4f, 0.3f), new Vector3(0, -0.36f, -0.5f)));        // the filter can
        }
        else
            _bearHead.AddChild(Props.Part(Props.Ball(0.08f), new Color(0.78f, 0.12f, 0.1f), new Vector3(0, -0.02f, -0.45f)));   // red drinker's nose
        // The bear is the duty officer of the area: a tall furazhka that leaves no doubt how important
        // he is, but narrow enough that his ears and face still show. Red band, gold oak-leaf braid on
        // the visor, a big gold badge. It turns with his head.
        var capKhaki = new Color(0.46f, 0.47f, 0.32f);
        var capRed = new Color(0.72f, 0.08f, 0.07f);
        var gold = new Color(0.95f, 0.76f, 0.2f);
        var cap = new Node3D { Position = new Vector3(0, 0.36f, 0.04f), RotationDegrees = new Vector3(-6, 0, 0) };
        _bearHead.AddChild(cap);
        cap.AddChild(Props.Part(Props.Cyl(0.27f, 0.2f, 12), capRed, new Vector3(0, 0.08f, 0)));                  // band, between the ears
        cap.AddChild(Props.Part(Props.Cyl(0.29f, 0.36f, 14, 0.62f), capKhaki, new Vector3(0, 0.34f, 0)));       // tall crown flaring out
        cap.AddChild(Props.Part(Props.Cyl(0.63f, 0.05f, 14), capRed, new Vector3(0, 0.52f, 0)));               // piping
        cap.AddChild(Props.Part(Props.Cyl(0.6f, 0.07f, 14), capKhaki, new Vector3(0, 0.56f, 0)));              // top
        cap.AddChild(Props.Part(Props.Box(0.5f, 0.04f, 0.26f), dark, new Vector3(0, 0f, -0.34f), new Vector3(-24, 0, 0)));   // visor
        cap.AddChild(Props.Part(Props.Box(0.44f, 0.05f, 0.06f), gold, new Vector3(0, 0.03f, -0.3f), new Vector3(-24, 0, 0)));   // oak-leaf braid
        cap.AddChild(Props.Part(Props.Box(0.3f, 0.26f, 0.05f), gold, new Vector3(0, 0.36f, -0.52f), new Vector3(20, 0, 0)));    // badge
        cap.AddChild(Props.Part(Props.Box(0.12f, 0.12f, 0.04f), capRed, new Vector3(0, 0.36f, -0.55f), new Vector3(20, 0, 0))); // star on it
        _bearArmR = new Node3D { Position = new Vector3(0.45f, 0.95f, -0.05f) };
        _bearArmL = new Node3D { Position = new Vector3(-0.45f, 0.95f, -0.05f) };
        _bear.AddChild(_bearArmR);
        _bear.AddChild(_bearArmL);
        foreach (var arm in new[] { _bearArmR, _bearArmL })
        {
            arm.AddChild(Props.Part(Props.Box(0.26f, 0.6f, 0.26f), fur, new Vector3(0, -0.3f, 0)));
            arm.AddChild(Props.Part(Props.Ball(0.16f), fur, new Vector3(0, -0.62f, 0)));
        }
        // A big clear bottle of vodka with a red label, held up in front of him so nobody misses it.
        _bottle = new Node3D { Position = new Vector3(0, -0.64f, -0.08f), Rotation = new Vector3(-BottleRest, 0, 0) };
        _bearArmL.AddChild(_bottle);
        _bottle.AddChild(Props.Part(Props.Cyl(0.12f, 0.46f, 8), new Color(0.86f, 0.92f, 0.96f), new Vector3(0, 0.2f, 0)));
        _bottle.AddChild(Props.Part(Props.Cyl(0.125f, 0.16f, 8), new Color(0.85f, 0.1f, 0.08f), new Vector3(0, 0.2f, 0)));   // label
        _bottle.AddChild(Props.Part(Props.Cyl(0.05f, 0.18f, 6), new Color(0.86f, 0.92f, 0.96f), new Vector3(0, 0.52f, 0)));  // neck
        _bottle.AddChild(Props.Part(Props.Cyl(0.055f, 0.05f, 6), new Color(0.85f, 0.1f, 0.08f), new Vector3(0, 0.63f, 0)));  // cap
        _bearArmR.AddChild(Props.Part(Props.Box(0.3f, 0.16f, 0.3f), capRed, new Vector3(0, -0.2f, 0)));   // duty officer's armband
    }

    /// <summary>The bear hiccups now and then (a speech bubble over his head).</summary>
    public IEnumerable<(Vector3 pos, string text)> ActiveShouts()
    {
        if (_bearHead == null || !_model.Visible) yield break;
        if (_mode == Mode.Autograph && _askT > 0.6f)
            yield return (_bearHead.GlobalPosition + Vector3.Up * 1.6f, _askT < 4.2f ? "АВТОГРАФ ДЛЯ ЖЕНЫ?" : "СПАСИБО!");
        else if (_hic > 0 && _mode != Mode.Chase)
            yield return (_bearHead.GlobalPosition + Vector3.Up * 1.6f, "ИК!");
    }

    // ------------------------------------------------------------ driving

    /// <summary>Advance and update pose/obstacles. car may be null; impact is the car's last hit strength.</summary>
    public void Tick(float dt, CarSim car, float impact)
    {
        _clock += dt;
        _driveDt = dt;
        _previousSpeed = _speed;
        int n = _t.Center.Length;
        switch (_mode)
        {
            case Mode.Road: DriveRoad(dt, n); break;
            case Mode.ToGuard: DrivePath(dt, true); break;
            case Mode.Guard:
                _speed = 0;
                _guardTimer -= dt;
                if (_guardTimer <= 0)
                {
                    var leg = Leg(_cur, false);
                    _waiting = PathBlocked(leg);
                    if (!_waiting) { SetPath(leg); SetMode(Mode.Back); }
                }
                break;
            case Mode.Back: DrivePath(dt, false); break;
            case Mode.Chase: DriveChase(dt, n, car); break;
            case Mode.Autograph: DriveAutograph(dt, n, car); break;
        }
        // Other traffic on the road: slow down behind it, stop short of it (the bus, tractors, ploughs, a tank, a column).
        float trafficCap = TrafficCap();
        if (_speed > trafficCap) _speed = Mathf.MoveToward(_speed, trafficCap, ChaseBrake * dt);
        AdvanceVehicle(dt);
        if (KozlikDbg) CheckOverlap();
        if (_beacon != null && _beacon.Visible)
        {
            // Two flashes a second, left and right lamps in turn.
            float ph = (_clock * 2f) % 1f;
            _beaconL.Visible = ph < 0.4f;
            _beaconR.Visible = ph >= 0.5f && ph < 0.9f;
            _beaconLight.Visible = _beaconL.Visible || _beaconR.Visible;
        }

        // Pose on the ground, pitched and rolled with it.
        Vector2 pos = Position2D, fwd = Forward2D, right = new(-fwd.Y, fwd.X);
        float hF = _t.MeshHeight(pos.X + fwd.X * (Wheelbase*.5f), pos.Y + fwd.Y * (Wheelbase*.5f));
        float hB = _t.MeshHeight(pos.X - fwd.X * (Wheelbase*.5f), pos.Y - fwd.Y * (Wheelbase*.5f));
        float hR = _t.MeshHeight(pos.X + right.X * GroundHalfTrack, pos.Y + right.Y * GroundHalfTrack);
        float hL = _t.MeshHeight(pos.X - right.X * GroundHalfTrack, pos.Y - right.Y * GroundHalfTrack);
        var basis = new Basis(Vector3.Up, TrackData.YawFor(fwd))
                  * new Basis(Vector3.Right, MathF.Atan2(hF - hB, Wheelbase))
                  * new Basis(Vector3.Back, MathF.Atan2(hR - hL, GroundHalfTrack*2) * 0.8f);
        _model.GlobalTransform = new Transform3D(basis, new Vector3(pos.X, (hF + hB) * 0.5f, pos.Y));
        _body.Position = new Vector3(0, MathF.Sin(_clock * 11f) * 0.025f * Math.Min(1f, _speed / 8f), 0);   // bouncing on the leaf springs
        _wheelSpin -= (_reverse ? -_speed : _speed) * dt / (0.44f * ModelScale);
        foreach (var w in _wheels) w.Rotation = new Vector3(_wheelSpin, w.Position.Z < 0 ? -_steer : 0, 0);

        Vector2 vel = _velocity;
        // On the car's tail (or stopped behind it for the autograph) the Kozlik is not solid: when the
        // car stops dead (a jump's landing, a spin) it must not shunt it on.
        bool ghost = Chasing && _gapToCar < 12f;
        for (int k = 0; k < _obs.Length; k++)
            _t.Obstacles[_obs[k]] = ghost ? new Obstacle(new Vector2(-1e4f, -1e4f), 0.1f) : new Obstacle(pos + fwd * (k - 1) * (Wheelbase*.5f), ObsRadius, vel, Material: ImpactMaterial.Metal);

        HitThisTick = false;
        if (car != null && dt > 0 && impact > 2f && pos.DistanceTo(new Vector2(car.Pos.X, car.Pos.Z)) < Length * 0.5f + 3f)
        {
            HitThisTick = true;
            _angry = 3f;
            if (OS.HasEnvironment("KOZLIKDBG")) GD.Print($"Kozlik hit by the car at {_clock:F1} s, impact {impact:F1}, car {car.Speed * 3.6f:F0} km/h");
        }
        Animate(dt, car);

        if (dt > 0)
        {
            _sound.Speed = _speed;
            Vector3 pipe = _model.GlobalTransform * SootPipe;
            Vector3 fwd3 = -_model.GlobalTransform.Basis.Z.Normalized();
            _fx.EngineSoot(pipe, -fwd3, fwd3 * _speed, Mathf.Clamp(_speed / 10f, 0.25f, 1f), 1f, ref _soot, dt);      // the player's diesel smoke
            _dbg -= dt;
            if (_dbg <= 0 && OS.HasEnvironment("KOZLIKDBG"))
            {
                _dbg = 2f;
                GD.Print($"Kozlik {_mode}{(_waiting ? " (waiting)" : "")} s={_s:F0} pos=({pos.X:F0},{pos.Y:F0}) {_speed * 3.6f:F0} km/h lat={_lat:F1}{(_cur != null ? " at " + _cur.Name : "")}{(_beacon is { Visible: true } ? $" BEACON L{(_beaconL.Visible ? 1 : 0)}R{(_beaconR.Visible ? 1 : 0)}" : "")}");
            }
        }
    }

    void SetMode(Mode m)
    {
        _mode = m;
        if (OS.HasEnvironment("KOZLIKDBG")) GD.Print($"Kozlik -> {m}{(_cur != null ? " (" + _cur.Name + ")" : "")} at {_clock:F1} s");
    }

    void DriveRoad(float dt, int n)
    {
        int i = (int)_s % n;
        if (_pause > 0)
        {
            // After the autograph: stand aside a moment and let the car get away first.
            _pause -= dt;
            _speed = 0;
            _lat = Mathf.MoveToward(_lat, LaneAside * _t.HalfWidth[i], 1.5f * dt);
            PlaceOnRoad(n);
            return;
        }
        // Slow for the bends ahead.
        float turn = 0;
        Vector2 d0 = _t.CenterDir(i);
        for (int k = 5; k < 40; k += 5)
            turn = Math.Max(turn, MathF.Abs(d0.AngleTo(_t.CenterDir((i + k) % n))));
        float target = Mathf.Lerp(Cruise, BendSpeed, Math.Clamp(turn / 0.9f, 0, 1));

        // The next guard spot that is due: slow down for the turn-off.
        GuardSpot due = null;
        float dueDist = float.MaxValue;
        foreach (var g in _spots)
        {
            float d = ((g.Leave - _s) % n + n) % n;
            if (d < dueDist && d < 40f && Due(g)) { due = g; dueDist = d; }
        }
        if (due != null) target = Math.Min(target, MathF.Sqrt(DetourSpeed * DetourSpeed + 2f * 3f * dueDist));

        // Runway: wait at its threshold while a Balalaika is rolling or taxiing across; on it, keep well right.
        bool busy = false;
        _waiting = false;
        if ((_jets != null || StrikeAircraft != null) && _rwFrom >= 0)
        {
            float toRw = ((_rwFrom - _s) % n + n) % n;
            if ((_jets?.RunwayBusyAt(_t.Center[(_rwFrom + 12) % n]) ?? false) || (_jets?.JetOnGroundNear(_t.Center[_rwFrom], 18f) ?? false)
                || (StrikeAircraft?.RunwayBusyAt(_t.Center[(_rwFrom + 12) % n]) ?? false) || (StrikeAircraft?.JetOnGroundNear(_t.Center[_rwFrom], 18f) ?? false))
            {
                busy = true;
                if (toRw < 30f && toRw > 1f)
                {
                    target = Math.Min(target, Math.Max(0f, (toRw - 4f) * 0.6f));
                    _waiting = true;
                }
            }
        }
        float laneTarget = CentreLine ? 0 : (busy ? LaneAside : Lane) * _t.HalfWidth[i];
        _lat = Mathf.MoveToward(_lat, laneTarget, 1.2f * dt);

        _speed = Mathf.MoveToward(_speed, target, (target < _speed ? 4f : 2.2f) * dt);
        float prev = _s;
        float moved = _speed * dt;
        _s = (_s + moved) % n;

        PlaceOnRoad(n);

        // Crossing a turn-off point: every other pass (per spot) pull off and stand guard.
        if (dt <= 0) return;
        foreach (var g in _spots)
        {
            float d = ((g.Leave - prev) % n + n) % n;
            if (d > moved) continue;
            bool go = Due(g);
            g.Passes++;
            if (!go) continue;
            _cur = g;
            var leg = Leg(g, true);
            if (PathBlocked(leg)) { _cur = null; continue; }   // traffic in the way: next time
            SetPath(leg);
            SetMode(Mode.ToGuard);
            break;
        }
    }

    bool Due(GuardSpot g) => !CentreLine && (AlwaysGuard || g == _trapSpot || (g.Passes + g.Parity) % 2 == 0);

    /// <summary>Position and heading from the road metre and the lateral offset.</summary>
    void PlaceOnRoad(int n)
    {
        float aim = (_s + (_driveDt > 0 ? SteeringLookAhead : 0)) % n;
        int i = (int)aim % n;
        float f = aim - MathF.Floor(aim);
        Vector2 c = _t.Center[i].Lerp(_t.Center[(i + 1) % n], f);
        Vector2 fwd = _t.CenterDir(i).Lerp(_t.CenterDir((i + 1) % n), f).Normalized();
        _guidePosition = c + new Vector2(-fwd.Y, fwd.X) * _lat;
        _guideForward = fwd;
    }

    // ------------------------------------------------------------ the GAI chase

    /// <summary>Test (--gai-chase): standing in the GAI ambush from the start, for a long while.</summary>
    public void GuardAtTrap()
    {
        if (_trapSpot == null) return;
        _cur = _trapSpot;
        Position2D = _trapSpot.Spot;
        Forward2D = _trapSpot.Face;
        _speed = 0;
        _velocity = Vector2.Zero;
        _reverse = false;
        _steer = 0;
        _guardTimer = 1e4f;
        SetMode(Mode.Guard);
        Tick(0, null, 0);
    }

    /// <summary>A car caught speeding past the ambush: out of the spot and after it.</summary>
    public void StartChase()
    {
        if (_mode != Mode.Guard || _cur == null) return;
        // A short dash onto the road just ahead of the spot, behind the car that has passed it.
        int n = _t.Center.Length;
        _chaseJoin = (_t.NearestCenter(_cur.Spot, out _) + 10) % n;
        Vector2 rd = _t.CenterDir(_chaseJoin);
        SetPath(Hermite(_cur.Spot, _cur.Face, _t.Center[_chaseJoin] + new Vector2(-rd.Y, rd.X) * (Lane * _t.HalfWidth[_chaseJoin]), rd));
        _carK = -1;
        _chaseOut = true;
        _chaseT = _caughtT = 0;
        _waiting = false;
        Siren(true);
        SetMode(Mode.Chase);
    }

    /// <summary>The race no longer has the car to chase (wrecked, towed, the run over): back on patrol.</summary>
    public void StopChase()
    {
        if (!Chasing) return;
        EndChase();
    }

    /// <summary>The autograph is signed: stand aside, then back on patrol.</summary>
    public void EndAutograph()
    {
        if (_mode != Mode.Autograph) return;
        _pause = 4f;
        EndChase();
    }

    void EndChase()
    {
        Siren(false);
        if (_chaseOut)
        {
            _s = _t.NearestCenter(Position2D, out _);
            _lat = Lane * _t.HalfWidth[(int)_s];
        }
        _chaseOut = false;
        _cur = null;
        _path = null;
        _gapToCar = float.MaxValue;
        SetMode(Mode.Road);
    }

    /// <summary>The car's road metre (followed from the last one, so a hairpin's other leg never
    /// steals it) and its offset from the centre line.</summary>
    (int k, float lat) CarOnRoad(CarSim car)
    {
        var p = new Vector2(car.Pos.X, car.Pos.Z);
        int n = _t.Center.Length, k = _carK;
        if (k < 0) k = _t.NearestCenter(p, out _);
        else
        {
            float best = float.MaxValue;
            for (int a = -15; a <= 40; a++)
            {
                int i = ((_carK + a) % n + n) % n;
                float d2 = _t.Center[i].DistanceSquaredTo(p);
                if (d2 < best) { best = d2; k = i; }
            }
            if (best > 30f * 30f) k = _t.NearestCenter(p, out _);   // thrown off the road (or towed): look again
        }
        _carK = k;
        Vector2 d = _t.CenterDir(k);
        return (k, (p - _t.Center[k]).Dot(new Vector2(-d.Y, d.X)));
    }

    void DriveChase(float dt, int n, CarSim car)
    {
        _chaseT += dt;
        if (car == null) { EndChase(); return; }
        if (_chaseOut)
        {
            // Out of the ambush onto the road, in a hurry, once the car is past where we join it.
            float total = _pathLen[^1];
            var (ck0, _) = CarOnRoad(car);
            float past = ((ck0 - _chaseJoin) % n + n) % n;
            bool go = past > 4f && past < n * 0.5f;
            _speed = Mathf.MoveToward(_speed, go || _ps > 0f ? ChaseOut : 0f, 5f * dt);
            _ps = PathProgress(Position2D);
            _guidePosition = PathAt(Math.Min(total, _ps + SteeringLookAhead));
            _guideForward = PathDir(_ps);
            if (_ps < total - 0.05f || Position2D.DistanceTo(_path[^1]) > 1.2f) return;
            _s = _chaseJoin;
            _lat = Lane * _t.HalfWidth[_chaseJoin];
            _chaseOut = false;
            _cur = null;
        }
        var (ck, clat) = CarOnRoad(car);
        float gap = ((ck - _s) % n + n) % n;                  // road metres to the car ahead
        if (gap > n * 0.5f) gap -= n;                        // ...or behind us
        _gapToCar = MathF.Abs(gap);
        int i = (int)_s % n;
        float turn = 0;
        Vector2 d0 = _t.CenterDir(i);
        for (int k = 5; k < 40; k += 5)
            turn = Math.Max(turn, MathF.Abs(d0.AngleTo(_t.CenterDir((i + k) % n))));
        float target = Mathf.Lerp(ChaseMax, ChaseBend, Math.Clamp(turn / 0.9f, 0, 1));
        // Closing in: never faster than could still stop TailGap behind the car if it stopped dead
        // (it brakes, spins, lands from a jump), and no closer than that.
        float carSpeed = car.Speed;
        float room = Math.Max(0f, gap - TailGap);
        target = Math.Min(target, carSpeed + MathF.Sqrt(2f * ChaseBrake * room) * 0.8f);
        if (gap < TailGap) target = Math.Max(0f, carSpeed - 2f);
        if (gap < 0f) target = 0f;                           // the car is behind us (spun, thrown back): wait for it
        _speed = Mathf.MoveToward(_speed, target, (target < _speed ? ChaseBrake : 4f) * dt);
        _s = (_s + _speed * dt) % n;
        // Right behind the car, across the road as it is.
        float hw = _t.HalfWidth[i];
        _lat = Mathf.MoveToward(_lat, gap < 40f ? Math.Clamp(clat, -0.8f * hw, 0.8f * hw) : Lane * hw, 3f * dt);
        PlaceOnRoad(n);

        bool onTail = MathF.Abs(gap) < TailGap + 2.5f && MathF.Abs(clat - _lat) < 2.2f;
        _caughtT = onTail ? _caughtT + dt : Math.Max(0f, _caughtT - dt);
        if (_caughtT >= CatchTime)
        {
            _askT = 0;
            SetMode(Mode.Autograph);
            Caught?.Invoke();
            return;
        }
        if (_chaseT > GiveUpTime || gap > GiveUpGap)
        {
            if (OS.HasEnvironment("KOZLIKDBG")) GD.Print($"Kozlik gives up after {_chaseT:F1} s, {gap:F0} m behind the car (s={_s:F0}, car at {ck})");
            _angry = 3f;
            EndChase();
            GaveUp?.Invoke();
        }
    }

    /// <summary>Stopped right behind the stopped car while the bear asks for his autograph.</summary>
    void DriveAutograph(float dt, int n, CarSim car)
    {
        _askT += dt;
        float gap = TailGap;
        if (car != null)
        {
            var (ck, _) = CarOnRoad(car);
            gap = ((ck - _s) % n + n) % n;
            if (gap > n * 0.5f) gap -= n;
        }
        _gapToCar = MathF.Abs(gap);
        float target = Math.Max(0f, Math.Min(car?.Speed ?? 0f, (gap - TailGap) * 0.9f));
        _speed = Mathf.MoveToward(_speed, target, 8f * dt);
        _s = (_s + _speed * dt) % n;
        PlaceOnRoad(n);
    }

    void DrivePath(float dt, bool toGuard)
    {
        float total = _pathLen[^1];
        _ps = PathProgress(Position2D);
        float remain = total - _ps;
        // Speed: gentle over the grass, slow for sharp curvature, stop on the spot.
        Vector2 dNow = PathDir(_ps), dAhead = PathDir(Math.Min(total, _ps + 8f));
        float target = Mathf.Lerp(DetourSpeed, 3.5f, Math.Clamp(MathF.Abs(dNow.AngleTo(dAhead)) / 1.2f, 0, 1));
        if (toGuard)
        {
            float remaining = Math.Max(remain, Position2D.DistanceTo(_path[^1]));
            target = Math.Min(target, MathF.Sqrt(2f * 2.5f * Math.Max(0f, remaining - 0.3f)));
        }
        else if (remain < 25f) target = Math.Max(target, BendSpeed * (1f - remain / 25f) + target * remain / 25f);
        _speed = Mathf.MoveToward(_speed, target, (target < _speed ? 3.5f : 2f) * dt);
        _guidePosition = PathAt(Math.Min(total, _ps + SteeringLookAhead));
        _guideForward = PathDir(_ps);

        if (toGuard)
        {
            float distance = Position2D.DistanceTo(_path[^1]);
            if (distance > 0.65f || _speed > 0.15f) return;
        }
        else if (_ps < total - 0.05f || Position2D.DistanceTo(_path[^1]) > 1.2f) return;
        if (toGuard)
        {
            _guardTimer = _cur == _trapSpot ? _rng.RandfRange(35f, 55f) : _rng.RandfRange(10f, 18f);
            _speed = 0;
            SetMode(Mode.Guard);
        }
        else
        {
            int n = _t.Center.Length;
            _s = _cur.Rejoin;
            _lat = Lane * _t.HalfWidth[_cur.Rejoin % n];
            _cur = null;
            SetMode(Mode.Road);
        }
    }

    // ------------------------------------------------------------ detour paths

    /// <summary>The leg from the turn-off to the spot (out) or from the spot back onto the road.</summary>
    Vector2[] Leg(GuardSpot g, bool outward)
    {
        int n = _t.Center.Length;
        int ri = outward ? g.Leave : g.Rejoin;
        Vector2 rd = _t.CenterDir(ri);
        Vector2 rp = _t.Center[ri] + new Vector2(-rd.Y, rd.X) * (Lane * _t.HalfWidth[ri % n]);
        return outward ? Hermite(rp, rd, g.Spot, g.Face) : Hermite(g.Spot, g.Face, rp, rd);
    }

    static Vector2[] Hermite(Vector2 p0, Vector2 d0, Vector2 p1, Vector2 d1)
    {
        float k = p0.DistanceTo(p1) * 0.9f;
        Vector2 m0 = d0 * k, m1 = d1 * k;
        var pts = new Vector2[61];
        for (int j = 0; j <= 60; j++)
        {
            float t = j / 60f, t2 = t * t, t3 = t2 * t;
            pts[j] = (2 * t3 - 3 * t2 + 1) * p0 + (t3 - 2 * t2 + t) * m0 + (-2 * t3 + 3 * t2) * p1 + (t3 - t2) * m1;
        }
        return pts;
    }

    void SetPath(Vector2[] pts)
    {
        // A parking stop may finish at a different heading, particularly after reversing.
        // Begin the return curve at the actual axle pose rather than an assumed guard heading.
        Vector2 first = (pts[1] - pts[0]).Normalized(), last = (pts[^1] - pts[^2]).Normalized();
        if (Position2D.DistanceTo(pts[0]) > 0.3f || Forward2D.Dot(first) < 0.95f)
            pts = Hermite(Position2D, Forward2D, pts[^1], last);
        if (_speed < 0.15f) _reverse = false;
        _path = pts;
        _pathLen = new float[pts.Length];
        for (int j = 1; j < pts.Length; j++) _pathLen[j] = _pathLen[j - 1] + pts[j].DistanceTo(pts[j - 1]);
        _ps = 0;
    }

    Vector2 PathAt(float d)
    {
        int r = Array.BinarySearch(_pathLen, d);
        int j = Math.Max(1, r < 0 ? ~r : r);
        if (j >= _path.Length) return _path[^1];
        float seg = _pathLen[j] - _pathLen[j - 1];
        return _path[j - 1].Lerp(_path[j], seg > 1e-4f ? (d - _pathLen[j - 1]) / seg : 1f);
    }

    Vector2 PathDir(float d)
    {
        float total = _pathLen[^1];
        Vector2 a = PathAt(Math.Clamp(d - 0.8f, 0, total)), b = PathAt(Math.Clamp(d + 0.8f, 0, total));
        return (b - a).LengthSquared() > 1e-6f ? (b - a).Normalized() : Forward2D;
    }

    static readonly bool KozlikDbg = OS.HasEnvironment("KOZLIKDBG"), NoCap = OS.HasEnvironment("KOZLIKNOCAP");      // (test: the old behaviour, no slowing for traffic)
    float _overlapAt = -9;
    int _overlaps;

    /// <summary>Debug (KOZLIKDBG): the Kozlik drove into another moving vehicle, reported once a second.</summary>
    void CheckOverlap()
    {
        if (_clock - _overlapAt < 1f || _speed < 1f) return;      // (standing still it is not driving through anything)
        for (int i = 0; i < _t.Obstacles.Count; i++)
        {
            var o = _t.Obstacles[i];
            if (o.Velocity.LengthSquared() < Track.TrafficCap.MinSpeed * Track.TrafficCap.MinSpeed || o.Radius < 0.6f || o.Center.X < -9000f || Array.IndexOf(_obs, i) >= 0) continue;
            float d = o.Center.DistanceTo(Position2D);
            if (d < ObsRadius + o.Radius - 0.3f)
            {
                _overlapAt = _clock;
                _overlaps++;
                GD.Print($"KOZLIK OVERLAP #{_overlaps} at {_clock:F1} s ({_mode}): mover at ({o.Center.X:F0},{o.Center.Y:F0}) r={o.Radius:F1} v={o.Velocity.Length():F1} m/s, gap {d:F1} m, Kozlik {_speed * 3.6f:F0} km/h");
                return;
            }
        }
    }

        /// <summary>The speed the Kozlik may keep for the vehicles moving ahead of it (TrafficCap).</summary>
    float TrafficCap()
    {
        if (_mode == Mode.Guard || NoCap) return float.MaxValue;
        return Track.TrafficCap.Cap(_t, Position2D, Forward2D, _speed, ObsRadius, i => i >= _obs[0] && i < _obs[0] + _obs.Length);
    }

    /// <summary>A Balalaika on the ground near the leg or rolling on the runway it joins, or the bus on it.</summary>
    bool PathBlocked(Vector2[] pts)
    {
        for (int j = 0; j < pts.Length; j += 4)
        {
            if (_jets != null && (_jets.JetOnGroundNear(pts[j], 16f) || _jets.RunwayBusyAt(pts[j]))) return true;
            if (StrikeAircraft != null && (StrikeAircraft.JetOnGroundNear(pts[j], 16f) || StrikeAircraft.RunwayBusyAt(pts[j]))) return true;
            if (_bus != null && _bus.Position2D.DistanceTo(pts[j]) < 14f) return true;
        }
        return false;
    }

    /// <summary>Debug: closest approach of a leg to the static obstacles (trees, buildings).</summary>
    float StaticClearance(Vector2[] pts)
    {
        float best = float.MaxValue;
        foreach (var p in pts)
            foreach (var o in _t.Obstacles)
                best = Math.Min(best, p.DistanceTo(o.Center) - o.Radius);
        return best;
    }

    // ------------------------------------------------------------ crew

    void Animate(float dt, CarSim car)
    {
        bool guard = _mode == Mode.Guard;
        _angry -= dt;
        // Bear's head: looks around (more on guard), follows a car that comes close.
        float look = guard ? MathF.Sin(_clock * 0.9f) * 1.3f + MathF.Sin(_clock * 2.3f) * 0.3f : MathF.Sin(_clock * 0.5f) * 0.6f;
        float nod = MathF.Sin(_clock * 1.7f) * 0.08f;
        if (car != null)
        {
            Vector3 lc = _model.GlobalTransform.AffineInverse() * car.Pos;
            Vector3 rel = lc - new Vector3(_bear.Position.X, 0, _bear.Position.Z) * ModelScale;
            if (rel.Length() < 25f) look = Math.Clamp(MathF.Atan2(-rel.X, -rel.Z), -2.2f, 2.2f);
        }
        float yaw = Mathf.LerpAngle(_bearHead.Rotation.Y, look, Math.Min(1f, dt * 3f));
        // He is drunk: the whole bear sways on the bench and the head lolls.
        _bear.Rotation = new Vector3(MathF.Sin(_clock * 0.9f) * 0.05f, 0, MathF.Sin(_clock * 1.3f) * 0.1f + MathF.Sin(_clock * 0.47f) * 0.06f);
        _bearHead.Rotation = new Vector3(_angry > 0 ? 0.25f : nod + 0.1f, yaw, MathF.Sin(_clock * 1.1f) * 0.22f);

        // Arms: a pull from the bottle now and then, a sloppy drunken wave, both paws up when rammed.
        _drinkCd -= dt;
        _waveCd -= dt;
        _hicCd -= dt;
        if (_drinkCd <= 0) { _drink = 1.6f; _drinkCd = _rng.RandfRange(8f, 14f); }
        if (_waveCd <= 0 && _drink <= 0) { _wave = 2.6f; _waveCd = _rng.RandfRange(8f, 14f); }
        if (_hicCd <= 0) { _hic = 1.1f; _hicCd = _rng.RandfRange(5f, 10f); }
        _drink -= dt;
        _wave -= dt;
        _hic -= dt;
        if (_angry > 0)
        {
            float shake = MathF.Sin(_clock * 16f) * 0.3f;
            _bearArmR.Rotation = new Vector3(2.9f + shake, 0, -0.35f);
            _bearArmL.Rotation = new Vector3(2.9f - shake, 0, 0.35f);
        }
        else
        {
            bool ask = _mode == Mode.Autograph;   // waving his notebook at the driver
            _bearArmR.Rotation = _wave > 0 || ask ? new Vector3(2.6f, 0, -0.3f + MathF.Sin(_clock * (ask ? 3f : 5f)) * 0.55f) : new Vector3(0.5f, 0, 0.15f);
            _bearArmL.Rotation = _drink > 0 ? new Vector3(2.3f, 0, -0.55f) : new Vector3(BottleRest, 0, -0.15f);
            if (_drink > 0) _bearHead.Rotation = new Vector3(-0.5f, 0.3f, 0);
        }

        // Driver: eyes on the road; on guard he leans back and smokes.
        _driverHead.Rotation = guard ? new Vector3(0, MathF.Sin(_clock * 0.4f) * 0.6f, 0) : new Vector3(0, MathF.Sin(_clock * 0.3f) * 0.15f, 0);
        bool puff = guard && (_clock % 5f) < 1.4f;
        _driverArm.Rotation = puff ? new Vector3(1.0f, -0.9f, 0) : guard ? new Vector3(-0.6f, 0, 0) : Vector3.Zero;
        if (guard && dt > 0)
        {
            _smoke -= dt;
            if (_smoke <= 0)
            {
                _smoke = puff ? 0.25f : 0.8f;
                Vector3 mouth = _driverHead.GlobalPosition + _model.GlobalBasis * new Vector3(0, -0.05f, -0.25f);
                _fx.Smoke(mouth, Vector3.Up * 0.5f, new Color(0.85f, 0.85f, 0.82f), puff ? 0.5f : 0.25f, 0.12f, 0.6f, 1.6f);
            }
        }
    }

    // ------------------------------------------------------------ commentary

    /// <summary>The political officer on the bear (racing only).</summary>
    public void Commentary(float dt, CarSim car, Hud hud)
    {
        if (Chasing) return;     // the race says what the chase is
        _warnCd -= dt;
        _hitCd -= dt;
        Vector2 cp = new(car.Pos.X, car.Pos.Z);
        float d = Position2D.DistanceTo(cp);
        if (HitThisTick && _hitCd <= 0)
        {
            hud.Show("THE BEAR IS NOT AMUSED.", "KOZLIK ON GUARD DUTY", 2.5f, Hud.Red, Prio.Nice);
            _hitCd = 8f;
            _warnCd = 25f;
        }
        else if (d < 30f && car.Speed > 8f && _warnCd <= 0)
        {
            hud.Show(Guarding ? "THE BEAR IS ON GUARD DUTY" : "THE BEAR HAS A DRIVING LICENCE", "KEEP DISTANCE FROM THE BEAR", 2.5f, Hud.Gold, Prio.Important, urgent: true, plain: "BEAR AHEAD!", plainSub: "KEEP DISTANCE FROM THE BEAR");
            _warnCd = 40f;
            if (OS.HasEnvironment("KOZLIKDBG")) GD.Print($"Kozlik warning at {_clock:F1} s, car {d:F0} m away");
        }
    }
}
