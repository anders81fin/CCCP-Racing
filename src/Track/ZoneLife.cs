using System;
using System.Collections.Generic;
using CCCPRacing.Audio;
using CCCPRacing.Car;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Track;

/// <summary>
/// The life of ATOMGRAD-4's zone (TrackDef.Zone): the giant catfish of the cooling channel jumping
/// now and then by the straight (scenery), a herd of Przewalski's horses grazing by the start
/// straight that trot off from a car (solid, moving), the lunokhod robot crossing the road at the
/// plant back and forth (solid, moving; it waits at the road's edge while a car comes), and the
/// goods train over the level crossing (solid, moving; the crossing's lamps blink, it whistles).
/// Everything resets on restart; RaceScene takes it out of the empty world (Actors.Zone).
/// </summary>
public partial class ZoneLife : Node3D
{
    TrackData _t;
    TyreEffects _fx;
    ZoneDef _def;
    readonly RandomNumberGenerator _rng = new() { Seed = 1986 };
    float _clock;

    public void Init(TrackData t, TyreEffects fx, ZoneDef def)
    {
        _t = t;
        _fx = fx;
        _def = def;
        BuildCatfish();
        BuildHorses();
        BuildRobot();
        BuildTrain();
        MarkTape();
        Reset();
    }

    /// <summary>The world tape parts: the catfish (it shows and hides), each horse and its head, the lunokhod and its dish (a spinner), the train with each vehicle's own pose and its engine sound, the crossing lamps (they show and hide).</summary>
    void MarkTape()
    {
        Tape.Version(this, 1); Tape.Part(this, "root");
        if (_def.Catfish != null) Tape.Part(_fish, "catfish");
        for (int i = 0; i < _horses.Count; i++) { Tape.Part(_horses[i].Node, $"{i + 1}/horse"); Tape.Part(_horses[i].Head, $"{i + 1}/horseHead"); }
        if (_robot != null) { Tape.Part(_robot, "lunokhod"); Tape.Part(_dish, "dish"); Tape.Spin(_dish, Vector3.Up, () => _clock * 0.8f); }
        if (_train == null) return;
        Tape.Part(_train, "train");
        int v = 0;
        foreach (var child in _train.GetChildren())
            if (child is Node3D vehicle && vehicle.HasMeta("rail_center")) Tape.Part(vehicle, "vehicle" + v++);
        for (int i = 0; i < _lamps.Count; i++) Tape.Part(_lamps[i], "lamp" + i);
        Tape.Sound(_train, TapeSoundChannel.DieselSpeed, () => _trainSound.Speed, s => _trainSound.Speed = s);
    }

    public void Reset()
    {
        _clock = 0;
        _fishT = -1;
        _fishNext = 4f;
        foreach (var h in _horses) { h.Pos = h.Home; h.Run = 0; }
        _robotT = 0;
        _robotWait = 0;
        _trainS = 0;
        _trainSpeed = 0;
        _trainWait = _def.Train != null ? 6f : 1e9f;
        _trainDir = 1;
        Tick(0, null);
    }

    /// <summary>The drone tour's cues: the train sets out now, the catfish jumps now.</summary>
    public void CueSchoolTrain() { _trainS = 0; _trainSpeed = 0; _trainDir = 1; _trainWait = 0.1f; }
    public void CueSchoolFish() { if (_fishT < 0) _fishNext = 0.1f; }

    public void Tick(float dt, CarSim car)
    {
        _clock += dt;
        TickCatfish(dt);
        TickHorses(dt, car);
        TickRobot(dt, car);
        TickTrain(dt, car);
    }

    // ------------------------------------------------------------ the catfish

    Node3D _fish;
    float _fishT = -1, _fishNext;
    Vector3 _fishFrom, _fishTo;

    void BuildCatfish()
    {
        _fish = new Node3D { Name = "Catfish", Visible = false };
        AddChild(_fish);
        var skin = new Color(0.28f, 0.27f, 0.22f);
        var belly = new Color(0.62f, 0.6f, 0.5f);
        var body = Props.Part(Props.Ball(0.55f, 8), skin, Vector3.Zero);
        body.Scale = new Vector3(0.8f, 0.6f, 2.2f);
        _fish.AddChild(body);
        var under = Props.Part(Props.Ball(0.45f, 8), belly, new Vector3(0, -0.15f, -0.2f));
        under.Scale = new Vector3(0.8f, 0.5f, 1.8f);
        _fish.AddChild(under);
        _fish.AddChild(Props.Part(Props.Box(0.06f, 0.7f, 0.6f), skin, new Vector3(0, 0, 1.35f)));          // the tail
        foreach (float s in new[] { -1f, 1f })
            _fish.AddChild(Props.Part(Props.Box(0.03f, 0.03f, 0.9f), skin, new Vector3(s * 0.25f, -0.1f, -1.4f), new Vector3(0, s * 25f, 0)));   // whiskers
    }

    void TickCatfish(float dt)
    {
        if (_def.Catfish == null) return;
        var c = _def.Catfish;
        if (_fishT < 0)
        {
            _fishNext -= dt;
            if (_fishNext > 0) return;
            // Up out of the channel at a random place along it, a fat arc, back in.
            float x = _rng.RandfRange(c.X0, c.X1);
            float level = float.IsNaN(_t.WaterLevel) ? 0f : _t.WaterLevel;
            float dir = _rng.Randf() < 0.5f ? -1f : 1f;
            _fishFrom = new Vector3(x - dir * 2f, level - 0.3f, c.Z);
            _fishTo = new Vector3(x + dir * 2f, level - 0.3f, c.Z + _rng.RandfRange(-1f, 1f));
            _fishT = 0;
            _fish.Visible = true;
            Splash(_fishFrom);
            if (OS.HasEnvironment("ZONEDBG")) GD.Print($"zone: the catfish jumps at ({x:F0},{c.Z:F0}), t {_clock:F1}");
        }
        _fishT += dt / 1.3f;
        float t = Math.Min(_fishT, 1f);
        var p = _fishFrom.Lerp(_fishTo, t) + Vector3.Up * 2.4f * 4f * t * (1f - t);
        var d = (_fishTo - _fishFrom).Normalized();
        _fish.GlobalPosition = p;
        _fish.Rotation = new Vector3(Mathf.Lerp(0.9f, -0.9f, t) * 0.9f, MathF.Atan2(d.X, d.Z) + MathF.PI, MathF.Sin(t * 12f) * 0.15f);
        if (_fishT >= 1f)
        {
            Splash(_fishTo);
            _fish.Visible = false;
            _fishT = -1;
            _fishNext = _rng.RandfRange(7f, 15f);
        }
    }

    void Splash(Vector3 at)
    {
        if (_fx == null) return;
        for (int k = 0; k < 24; k++)
            _fx.Burst(at + Vector3.Up * 0.3f, new Vector3(_rng.RandfRange(-2.5f, 2.5f), _rng.RandfRange(2f, 5f), _rng.RandfRange(-2.5f, 2.5f)),
                new Color(0.75f, 0.82f, 0.9f), 0.8f, 0.3f, 0.2f, 0.9f, 12f, 0.3f, false);
    }

    // ------------------------------------------------------------ the horses

    class Horse
    {
        public Node3D Node, Head;
        public Vector2 Home, Pos, Graze, Face = new(0, 1);
        public float Run, Phase, Change;
        public int Obs;
    }

    readonly List<Horse> _horses = new();

    void BuildHorses()
    {
        if (_def.Horses == null) return;
        var dun = new Color(0.72f, 0.56f, 0.34f);
        var dark = new Color(0.2f, 0.15f, 0.1f);
        var muzzle = new Color(0.86f, 0.8f, 0.68f);
        for (int k = 0; k < _def.Horses.Count; k++)
        {
            float a = k * MathF.Tau / _def.Horses.Count;
            var home = new Vector2(_def.Horses.X + MathF.Cos(a) * 5f, _def.Horses.Z + MathF.Sin(a) * 4f);
            var n = new Node3D { Name = "Horse" + k, Scale = Vector3.One * 1.3f };
            AddChild(n);
            n.AddChild(Props.Part(Props.Box(0.55f, 0.6f, 1.5f), dun, new Vector3(0, 1.15f, 0)));      // the barrel
            foreach (float x in new[] { -0.2f, 0.2f })
            foreach (float z in new[] { -0.55f, 0.55f })
                n.AddChild(Props.Part(Props.Box(0.13f, 0.85f, 0.13f), dark, new Vector3(x, 0.45f, z)));
            n.AddChild(Props.Part(Props.Box(0.08f, 0.6f, 0.08f), dark, new Vector3(0, 1.05f, 0.82f), new Vector3(30, 0, 0)));   // the tail
            var head = new Node3D { Position = new Vector3(0, 1.35f, -0.7f) };
            n.AddChild(head);
            head.AddChild(Props.Part(Props.Box(0.3f, 0.75f, 0.32f), dun, new Vector3(0, 0.2f, -0.1f), new Vector3(-35, 0, 0)));     // the neck
            head.AddChild(Props.Part(Props.Box(0.26f, 0.28f, 0.55f), dun, new Vector3(0, 0.5f, -0.45f)));                         // the head
            head.AddChild(Props.Part(Props.Box(0.22f, 0.22f, 0.18f), muzzle, new Vector3(0, 0.45f, -0.75f)));                     // the pale muzzle
            head.AddChild(Props.Part(Props.Box(0.08f, 0.22f, 0.7f), dark, new Vector3(0, 0.5f, 0.05f), new Vector3(-35, 0, 0)));     // the upright mane
            var h = new Horse { Node = n, Head = head, Home = home, Pos = home, Graze = home, Phase = k * 1.7f, Obs = _t.Obstacles.Count };
            _t.Obstacles.Add(new Obstacle(home, 0.8f));
            _horses.Add(h);
        }
    }

    void TickHorses(float dt, CarSim car)
    {
        var cp = car != null ? new Vector2(car.Pos.X, car.Pos.Z) : new Vector2(-1e4f, -1e4f);
        foreach (var h in _horses)
        {
            h.Phase += dt;
            Vector2 away = h.Pos - cp;
            float d = away.Length();
            Vector2 vel = Vector2.Zero;
            if (d < 20f && car != null && car.Speed > 3f)
            {
                // Off at a trot, away from the car, never far from home.
                h.Run = 1.5f;
                Vector2 dir = (away.Normalized() + (h.Home - h.Pos) * 0.04f).Normalized();
                vel = dir * 6f;
            }
            else if (h.Run > 0) { h.Run -= dt; vel = h.Face * 3f; }
            else
            {
                // Grazing: a few steps now and then towards a new tuft, home never far.
                h.Change -= dt;
                if (h.Change <= 0) { h.Change = _rng.RandfRange(3f, 7f); h.Graze = h.Home + new Vector2(_rng.RandfRange(-6f, 6f), _rng.RandfRange(-5f, 5f)); }
                Vector2 to = h.Graze - h.Pos;
                if (to.Length() > 0.5f) vel = to.Normalized() * 0.8f;
            }
            h.Pos += vel * dt;
            if (vel.LengthSquared() > 0.01f) h.Face = h.Face.Lerp(vel.Normalized(), Math.Min(1f, dt * 4f)).Normalized();
            float y = _t.MeshHeight(h.Pos.X, h.Pos.Y);
            h.Node.GlobalPosition = new Vector3(h.Pos.X, y, h.Pos.Y);
            h.Node.Rotation = new Vector3(0, MathF.Atan2(-h.Face.X, -h.Face.Y), 0);
            bool grazing = vel.LengthSquared() < 1f;
            h.Head.Rotation = new Vector3(grazing ? -0.9f + MathF.Sin(h.Phase * 1.3f) * 0.1f : 0.15f, 0, 0);
            h.Node.Position += Vector3.Up * (vel.LengthSquared() > 4f ? MathF.Abs(MathF.Sin(h.Phase * 9f)) * 0.12f : 0f);
            _t.Obstacles[h.Obs] = new Obstacle(h.Pos, 0.8f, vel);
        }
    }

    // ------------------------------------------------------------ the lunokhod

    Node3D _robot, _dish;
    float _robotT, _robotWait;
    int _robotObs = -1;
    const float RobotSpeed = 1.6f;

    void BuildRobot()
    {
        if (_def.Robot == null) return;
        _robot = new Node3D { Name = "Lunokhod", Scale = Vector3.One * 1.3f };
        AddChild(_robot);
        var tub = new Color(0.72f, 0.72f, 0.68f);
        _robot.AddChild(Props.Part(Props.Cyl(1f, 0.9f, 12, 0.8f), tub, new Vector3(0, 0.95f, 0)));          // the pressurised tub
        _robot.AddChild(Props.Part(Props.Cyl(1.15f, 0.08f, 12), new Color(0.2f, 0.22f, 0.3f), new Vector3(0, 1.45f, 0)));   // the lid with its solar cells
        foreach (float x in new[] { -0.95f, 0.95f })
            for (int k = 0; k < 4; k++)
                _robot.AddChild(Props.Part(Props.Cyl(0.3f, 0.18f, 10), new Color(0.3f, 0.3f, 0.3f), new Vector3(x, 0.3f, -0.9f + k * 0.6f), new Vector3(0, 0, 90)));
        _dish = new Node3D { Position = new Vector3(0.4f, 1.6f, 0.3f) };
        _robot.AddChild(_dish);
        _dish.AddChild(Props.Part(Props.Cyl(0.03f, 0.6f, 4), new Color(0.6f, 0.6f, 0.6f), new Vector3(0, 0.3f, 0)));
        _dish.AddChild(Props.Part(Props.Cyl(0.32f, 0.06f, 10, 0.05f), new Color(0.85f, 0.85f, 0.8f), new Vector3(0, 0.62f, 0), new Vector3(30, 0, 0)));
        _robot.AddChild(Props.Part(Props.Box(0.3f, 0.2f, 0.15f), new Color(0.1f, 0.1f, 0.1f), new Vector3(0, 1.1f, -0.85f)));   // the camera eyes
        _robot.AddChild(Buildings.TextPanel("СССР", 0.9f, 0.25f, new Vector3(0, 1.0f, 0.82f), new Color(0.78f, 0.1f, 0.08f), new Color(1f, 0.85f, 0.3f)));
        _robotObs = _t.Obstacles.Count;
        _t.Obstacles.Add(new Obstacle(new Vector2(-1e4f, -1e4f), 0.1f));
    }

    void TickRobot(float dt, CarSim car)
    {
        if (_robot == null) return;
        var r = _def.Robot;
        Vector2 a = new(r.X0, r.Z0), b = new(r.X1, r.Z1);
        float len = a.DistanceTo(b);
        // Back and forth across the road; at either end it rests, and it will not set out onto the
        // road while a car is coming up to the crossing (the plant's safety rules).
        float leg = _robotT % (2f * len);
        bool out_ = leg < len;
        float at = out_ ? leg : 2f * len - leg;
        bool atEnd = at < 0.3f || at > len - 0.3f;
        bool carNear = car != null && new Vector2(car.Pos.X, car.Pos.Z).DistanceTo(a.Lerp(b, 0.5f)) < 90f;
        if (_robotWait > 0) _robotWait -= dt;
        else if (atEnd && carNear) { }
        else
        {
            _robotT += RobotSpeed * dt;
            float leg2 = _robotT % (2f * len);
            if ((leg2 < len) != out_) _robotWait = 3f;       // rests at the end
        }
        Vector2 dir = (out_ ? b - a : a - b).Normalized();
        Vector2 p = a.Lerp(b, Math.Clamp(at / len, 0f, 1f));
        _robot.GlobalPosition = new Vector3(p.X, _t.MeshHeight(p.X, p.Y), p.Y);
        _robot.Rotation = new Vector3(0, MathF.Atan2(-dir.X, -dir.Y), 0);
        _dish.Rotation = new Vector3(0, _clock * 0.8f, 0);
        bool moving = _robotWait <= 0 && !(atEnd && carNear);
        _t.Obstacles[_robotObs] = new Obstacle(p, 1.5f, moving ? dir * RobotSpeed : Vector2.Zero);
    }

    // ------------------------------------------------------------ the train

    Node3D _train;
    readonly List<MeshInstance3D> _lamps = new();
    readonly List<int> _trainObs = new();
    DieselSound _trainSound;
    float _trainS, _trainWait, _trainSpeed;
    int _trainDir;
    const float TrainLen = 50f, TrainSpeed = 9f;

    void BuildTrain()
    {
        if (_def.Train == null) return;
        _train = new Node3D { Name = "Train" };
        AddChild(_train);
        BuildTrainBody(_train);
        for (int k = 0; k < 9; k++)
        {
            _trainObs.Add(_t.Obstacles.Count);
            _t.Obstacles.Add(new Obstacle(new Vector2(-1e4f, -1e4f), 0.1f));
        }
        _trainSound = new DieselSound { BaseRate = 9f, RatePerSpeed = 1.6f, Body = 0.05f, Loudness = 6f, Reach = 180f, AirHorn = true };
        _train.AddChild(_trainSound);
        // The crossing's warning lamps blink while it comes (the props' lamps are static: these sit over them).
        foreach (var p in _t.Props)
            if (p.Type == "crossing")
                foreach (float x in new[] { -0.3f, 0.3f })
                {
                    var lamp = new MeshInstance3D
                    {
                        Mesh = Props.Cyl(0.12f, 0.025f, 16),
                        MaterialOverride = NightGlow.Keep(new StandardMaterial3D { AlbedoColor = new Color(1f, 0.15f, 0.08f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded }),
                        Position = p.Position + new Basis(Vector3.Up, p.Rotation) * new Vector3(x, 1.8f, 0.215f),
                        Rotation = new Vector3(Mathf.Pi / 2f, p.Rotation, 0),
                        Visible = false,
                        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    };
                    AddChild(lamp);
                    _lamps.Add(lamp);
                }
    }

    /// <summary>The train runs on the rails along Z at x = Train.X, from off the map's south edge up
    /// to the siding's buffer (Train.End) and back, then waits a while; its front is the loco.</summary>
    void TickTrain(float dt, CarSim car)
    {
        if (_train == null) return;
        var tr = _def.Train;
        float south = tr.South + TrainLen, north = tr.End;
        float span = south - north;
        if (_trainWait > 0)
        {
            _trainSpeed = 0;
            _trainWait = Math.Max(0, _trainWait - dt);
            if (_trainWait == 0) _trainSound.Honk(1.4f);
        }
        else
        {
            const float acceleration = 1.8f;
            float remaining = Math.Max(0, span - _trainS);
            float target = Math.Min(TrainSpeed, MathF.Sqrt(2f * acceleration * remaining));
            _trainSpeed = Mathf.MoveToward(_trainSpeed, target, acceleration * dt);
            _trainS = Math.Min(span, _trainS + _trainSpeed * dt);
            if (span - _trainS < 0.025f)
            {
                _trainS = 0;
                _trainSpeed = 0;
                _trainDir = -_trainDir;
                _trainWait = _trainDir > 0 ? _rng.RandfRange(18f, 30f) : 8f;
            }
        }
        _trainSound.Speed = _trainSpeed;
        // The locomotive stays at the north end of the consist, including while reversing.
        float nose = _trainDir > 0 ? south - _trainS : north + _trainS;
        _train.GlobalPosition = new Vector3(tr.X, _t.MeshHeight(tr.X, Math.Clamp(nose, tr.End, tr.Cross)), nose + 13f);
        _train.Rotation = Vector3.Zero;
        // Each vehicle follows its own two bogies instead of sharing the locomotive's height.
        foreach (var child in _train.GetChildren())
        {
            if (child is not Node3D vehicle || !vehicle.HasMeta("rail_center")) continue;
            float centre = vehicle.GetMeta("rail_center").AsSingle();
            float halfbase = vehicle.GetMeta("rail_halfbase").AsSingle();
            float z = _train.GlobalPosition.Z + centre;
            float h0 = _t.MeshHeight(tr.X, z - halfbase), h1 = _t.MeshHeight(tr.X, z + halfbase);
            var basis = new Basis(Vector3.Right, -MathF.Atan2(h1 - h0, halfbase * 2));
            vehicle.Transform = new Transform3D(basis,
                new Vector3(0, (h0 + h1) / 2 - _train.GlobalPosition.Y, centre) - basis * new Vector3(0, 0, centre));
        }
        Vector2 v = new(0, _trainDir > 0 ? -_trainSpeed : _trainSpeed);
        for (int k = 0; k < _trainObs.Count; k++)
        {
            float z = _train.GlobalPosition.Z - 11.5f + k * 6.25f;
            _t.Obstacles[_trainObs[k]] = new Obstacle(new Vector2(tr.X, z), 1.7f, v);
        }
        // The crossing: lamps blink and the horn blows while the train is within 70 m of it.
        float toCross = MathF.Abs(_train.GlobalPosition.Z - tr.Cross);
        float rear = _train.GlobalPosition.Z + 38.6f;
        bool occupiesCrossing = nose <= tr.Cross + 3f && rear >= tr.Cross - 3f;
        bool warn = occupiesCrossing || (toCross < 70f + TrainLen / 2f && _trainWait <= 0);
        bool on = warn && (_clock * 2.5f) % 1f < 0.5f;
        for (int i = 0; i < _lamps.Count; i++) _lamps[i].Visible = warn && (on ^ (i % 2 == 1));
        if (warn && !_warned) { _trainSound.Honk(1.2f); if (OS.HasEnvironment("ZONEDBG")) GD.Print($"zone: train at the crossing, {(_trainDir > 0 ? "north" : "south")}bound, t {_clock:F1}"); }
        _warned = warn;
    }

    bool _warned;
}
