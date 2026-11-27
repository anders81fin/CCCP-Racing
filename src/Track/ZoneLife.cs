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

    /// <param name="staticObstacleCount">the obstacle slots [0, n) that are static scenery for the horses (production: the end of the static world, RaceScene.WorldBuild's actorObs); default: every slot there is now</param>
    public void Init(TrackData t, TyreEffects fx, ZoneDef def, int staticObstacleCount = -1)
    {
        _staticObstacles = staticObstacleCount < 0 ? t.Obstacles.Count : Math.Min(staticObstacleCount, t.Obstacles.Count);
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
        Tape.Version(this, _robot != null ? 4 : 3); Tape.Part(this, "root");
        if (_def.Catfish != null) { Tape.Part(_fish, "catfish"); Tape.Part(_fishRear, "catfishRear"); Tape.Part(_fishTail, "catfishTail"); }
        MarkHorseTape();
        if (_robot != null)
        {
            Tape.Part(_robot, "lunokhod"); Tape.Part(_dish, "dish"); Tape.Spin(_dish, Vector3.Up, () => _clock * 0.8f);
            Tape.Part(_robotProbe, "robotProbe"); Tape.Part(_surveyLamp, "robotSurveyLamp");
        }
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
        _fishRear.Rotation = Vector3.Zero; _fishTail.Rotation = Vector3.Zero;
        ResetHorses();
        _robotT = 0;
        _robotWait = 2f;
        _robotSurvey = false;
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

    Node3D _fish, _fishRear, _fishTail;
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
        // The rear body and the tail hang on pivots so the fish can bend: the rear half (a tapering body) turns at the middle of the fish, the tail at the end of it.
        _fishRear = new Node3D { Name = "CatfishRear", Position = new Vector3(0, 0, 0.55f) };
        _fish.AddChild(_fishRear);
        var rearBody = Props.Part(Props.Ball(0.4f, 8), skin, new Vector3(0, 0, 0.45f));
        rearBody.Scale = new Vector3(0.7f, 0.55f, 1.6f);
        _fishRear.AddChild(rearBody);
        _fishTail = new Node3D { Name = "CatfishTail", Position = new Vector3(0, 0, 0.95f) };
        _fishRear.AddChild(_fishTail);
        _fishTail.AddChild(Props.Part(Props.Box(0.06f, 0.7f, 0.6f), skin, new Vector3(0, 0, 0.25f)));          // the tail fin
        foreach (float s in new[] { -1f, 1f })
            _fish.AddChild(Props.Part(Props.Box(0.03f, 0.03f, 0.9f), skin, new Vector3(s * 0.25f, -0.1f, -1.4f), new Vector3(0, s * 25f, 0)));   // whiskers
    }

    const float FishTime = 1.3f;

    /// <summary>The bend of the rear body and the tail by the time in the jump (0..1): the tail beats hard in the first moments (the launch from the water), the body settles straight in the air,
    /// bends into a C over the top of the arc (so a short jump reads as a fish and not a floating log) and straightens for the dive. Returns the rear body's and the tail's yaw (rad).</summary>
    internal static (float rear, float tail) FishBend(float t)
    {
        float launch = Math.Clamp(1f - t / 0.22f, 0f, 1f);                       // the first fifth of the jump
        float beat = MathF.Sin(t * 62f) * launch;                                // the tail's strokes: about 2.5 fast beats
        float apex = MathF.Sin(MathF.PI * Math.Clamp((t - 0.25f) / 0.5f, 0f, 1f));      // the arch over the top
        float rear = 0.4f * beat + 0.55f * apex;
        float tail = 1.0f * beat + 0.45f * apex;
        return (rear, tail);
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
            LaunchSplash(_fishFrom, new Vector3(_fishTo.X - _fishFrom.X, 0, _fishTo.Z - _fishFrom.Z).Normalized());
            if (OS.HasEnvironment("ZONEDBG")) GD.Print($"zone: the catfish jumps at ({x:F0},{c.Z:F0}), t {_clock:F1}");
        }
        _fishT += dt / FishTime;
        float t = Math.Min(_fishT, 1f);
        var p = _fishFrom.Lerp(_fishTo, t) + Vector3.Up * 2.4f * 4f * t * (1f - t);
        var d = (_fishTo - _fishFrom).Normalized();
        _fish.GlobalPosition = p;
        _fish.Rotation = new Vector3(Mathf.Lerp(0.9f, -0.9f, t) * 0.9f, MathF.Atan2(d.X, d.Z) + MathF.PI, MathF.Sin(t * 12f) * 0.15f);
        var (rearBend, tailBend) = FishBend(t);
        _fishRear.Rotation = new Vector3(0, rearBend, 0); _fishTail.Rotation = new Vector3(0, tailBend, 0);
        if (_fishT >= 1f)
        {
            Splash(_fishTo);
            _fish.Visible = false;
            _fishRear.Rotation = Vector3.Zero; _fishTail.Rotation = Vector3.Zero;
            _fishT = -1;
            _fishNext = _rng.RandfRange(7f, 15f);
        }
    }

    /// <summary>The tape's effect host (null: not recording). The catfish's two splashes are events (docs/replay-tape.md): the launch and the dive, each made by the static functions below, live and in the replay.</summary>
    public ITapeFxHost TapeFx;
    const float SplashLife = 1.6f;

    void LaunchSplash(Vector3 at, Vector3 forward)
    {
        TapeFx?.Fx(new TapeEvent(0f, TapeEventType.Effect, Kind: TapeFxKind.CatfishLaunch, Pos: at, Dir: forward, Life: SplashLife));
        LaunchSplashFx(_fx, _rng, at, forward);
    }

    void Splash(Vector3 at)
    {
        TapeFx?.Fx(new TapeEvent(0f, TapeEventType.Effect, Kind: TapeFxKind.CatfishSplash, Pos: at, Life: SplashLife));
        SplashFx(_fx, _rng, at);
    }

    /// <summary>The launch from the water: the tail strikes the surface behind the fish (a fan of spray thrown back and out, a pale flat ring spreading on the surface) and the body breaks out in a heavy column.</summary>
    internal static void LaunchSplashFx(TyreEffects fx, RandomNumberGenerator rng, Vector3 at, Vector3 forward)
    {
        if (fx == null) return;
        Vector3 back = -forward, side = new Vector3(-forward.Z, 0, forward.X);
        var spray = new Color(0.82f, 0.88f, 0.94f);
        for (int k = 0; k < 40; k++)      // the tail's strike: thrown back and up, wide
        {
            float fan = rng.RandfRange(-1f, 1f);
            fx.Burst(at + back * 1.5f + Vector3.Up * 0.2f, back * rng.RandfRange(1.5f, 4.5f) + side * fan * 3.5f + Vector3.Up * rng.RandfRange(2.5f, 6.5f), spray, 0.85f, 0.35f, 0.18f, 1.0f, 12f, 0.3f, false);
        }
        for (int k = 0; k < 28; k++)      // the column the body lifts out of the channel
            fx.Burst(at + Vector3.Up * 0.2f, new Vector3(rng.RandfRange(-1.2f, 1.2f), rng.RandfRange(4f, 8f), rng.RandfRange(-1.2f, 1.2f)) + forward * 1.5f, spray, 0.9f, 0.4f, 0.2f, 1.1f, 12f, 0.3f, false);
        for (int k = 0; k < 26; k++)      // the pale ring spreading flat on the surface
        {
            float a = k * MathF.Tau / 26f;
            fx.Burst(at + back * 1.2f + Vector3.Up * 0.08f, new Vector3(MathF.Cos(a), 0, MathF.Sin(a)) * rng.RandfRange(2.2f, 3.2f), new Color(0.92f, 0.95f, 0.97f), 0.5f, 0.45f, 0.8f, 0.9f, 0f, 1.2f, false);
        }
    }

    /// <summary>The dive back in: a spray of drops.</summary>
    internal static void SplashFx(TyreEffects fx, RandomNumberGenerator rng, Vector3 at)
    {
        if (fx == null) return;
        for (int k = 0; k < 24; k++)
            fx.Burst(at + Vector3.Up * 0.3f, new Vector3(rng.RandfRange(-2.5f, 2.5f), rng.RandfRange(2f, 5f), rng.RandfRange(-2.5f, 2.5f)),
                new Color(0.75f, 0.82f, 0.9f), 0.8f, 0.3f, 0.2f, 0.9f, 12f, 0.3f, false);
    }

    // ------------------------------------------------------------ the lunokhod

    Node3D _robot, _dish, _robotProbe, _surveyLamp;
    bool _robotSurvey;
    const float SurveySeconds = 5f;
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
        _robotProbe = new Node3D { Name = "SurveyProbe", Position = new Vector3(.51f, .81f, -.85f) };
        _robot.AddChild(_robotProbe);
        var armColor = new Color(.92f, .63f, .12f);
        _robotProbe.AddChild(Props.Part(Props.Box(.11f, .11f, .74f), armColor, new Vector3(0, 0, -.37f)));
        _robotProbe.AddChild(Props.Part(Props.Box(.22f, .17f, .23f), new Color(.18f, .20f, .19f), new Vector3(0, 0, -.74f)));
        _surveyLamp = Props.Part(Props.Box(.24f, .12f, .24f), armColor, new Vector3(-.45f, 1.55f, -.20f));
        ((MeshInstance3D)_surveyLamp).MaterialOverride = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, .70f, .12f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        _robot.AddChild(_surveyLamp);
        var route = _def.Robot;
        Vector2 dock = new(route.X0, route.Z0), sample = new(route.X1, route.Z1);
        BuildRobotStation(dock, (sample - dock).Normalized(), false);
        BuildRobotStation(sample, (dock - sample).Normalized(), true);
        _robotObs = _t.Obstacles.Count;
        _t.Obstacles.Add(new Obstacle(new Vector2(-1e4f, -1e4f), 0.1f));
    }

    void BuildRobotStation(Vector2 at, Vector2 heading, bool survey)
    {
        var station = new Node3D
        {
            Name = survey ? "SurveyPoint" : "RobotServiceDock",
            Position = new Vector3(at.X, _t.MeshHeight(at.X, at.Y), at.Y),
            Rotation = new Vector3(0, TrackData.YawFor(heading), 0),
        };
        AddChild(station);
        var paint = new Color(.88f, .64f, .13f);
        float half = survey ? 1.5f : 1.85f;
        void Mark(float x, float z, float w, float length, Color color)
        {
            var offset = station.Basis * new Vector3(x, 0, z);
            float y = _t.MeshHeight(at.X + offset.X, at.Y + offset.Z) - station.Position.Y;
            station.AddChild(Props.Part(Props.Box(w, .035f, length), color, new Vector3(x, y + .023f, z)));
        }
        foreach (float side in new[] { -1f, 1f })
        {
            Mark(side * half, 0, .17f, 3.6f, paint);
            Mark(0, side * 1.72f, half * 2f, .17f, paint);
        }
        if (survey) Mark(-.64f, 1.63f, .8f, .8f, new Color(.27f, .30f, .29f));
        else
        {
            var steel = new Color(.36f, .43f, .38f);
            station.AddChild(Props.Part(Props.Box(2.75f, .88f, .65f), steel, new Vector3(0, .45f, 2.16f)));
            for (int i = -2; i <= 2; i++)
                station.AddChild(Props.Part(Props.Box(.23f, .14f, .038f), paint, new Vector3(i * .47f, .55f, 1.82f)));
        }
        var sign = Buildings.TextPanel(survey ? "ДОЗА 01" : "ПОСТ 01", 1.8f, .38f,
            survey ? new Vector3(0, .06f, 2.17f) : new Vector3(0, .59f, 1.80f), new Color(.16f, .18f, .16f), paint);
        sign.RotationDegrees = survey ? new Vector3(-90, 0, 0) : new Vector3(0, 180, 0);
        station.AddChild(sign);
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
        float probe = 0f;
        if (_robotSurvey)
        {
            float elapsed = SurveySeconds - _robotWait;
            probe = Mathf.SmoothStep(0f, .8f, elapsed) * (1f - Mathf.SmoothStep(4f, SurveySeconds, elapsed));
        }
        bool moving = _robotWait <= 0f && !(atEnd && carNear);
        if (_robotWait > 0f)
        {
            _robotWait = Math.Max(0f, _robotWait - dt);
            if (_robotWait == 0f) _robotSurvey = false;
        }
        else if (moving)
        {
            float end = out_ ? len : 2f * len;
            _robotT = Math.Min(end, _robotT + RobotSpeed * dt);
            if (_robotT >= end)
            {
                _robotSurvey = out_;
                _robotWait = out_ ? SurveySeconds : 3f;
                if (!out_) _robotT = 0f;
                moving = false;
            }
            at = out_ ? _robotT : _robotT == 0f ? 0f : 2f * len - _robotT;
        }

        bool faceSurvey = _robotSurvey || out_;
        Vector2 dir = (faceSurvey ? b - a : a - b).Normalized();
        Vector2 p = a.Lerp(b, Math.Clamp(at / len, 0f, 1f));
        _robot.GlobalPosition = new Vector3(p.X, _t.MeshHeight(p.X, p.Y), p.Y);
        _robot.Rotation = new Vector3(0, TrackData.YawFor(dir), 0);
        _dish.Rotation = new Vector3(0, _clock * 0.8f, 0);
        _robotProbe.Rotation = new Vector3(-.93f * probe, 0, 0);
        _surveyLamp.Visible = probe > .6f;
        _t.Obstacles[_robotObs] = new Obstacle(p, 1.5f, moving ? dir * RobotSpeed : Vector2.Zero, Height: ObstacleHeights.Robot);
    }

    // ------------------------------------------------------------ the train

    Node3D _train;
    readonly List<MeshInstance3D> _lamps = new();
    readonly List<int> _trainObs = new();
    DieselSound _trainSound;
    float _trainS, _trainWait, _trainSpeed;
    int _trainDir;
    const float TrainLen = 50f, TrainSpeed = 9f;
    const int TrainCircles = 24;      // along the 51 m consist, 2.2 m apart: the 1.7 m circles overlap, no gap

    void BuildTrain()
    {
        if (_def.Train == null) return;
        _train = new Node3D { Name = "Train" };
        AddChild(_train);
        BuildTrainBody(_train);
        for (int k = 0; k < TrainCircles; k++)      // (the 9 circles 6.25 m apart left 2.9 m of air between them: the car drove through the train, 2026-11-19)
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

    /// <summary>Whether a body (a circle at p) stands where the train runs: the train's own 1.7 m circles along x = Train.X, north to the buffer, with half a metre to spare.
    /// The GAI's autograph never holds a car here (RaceScene.SpeedTrap): the train keeps its timetable and stops for nothing.</summary>
    public bool OnRails(Vector2 p, float radius)
    {
        var tr = _def?.Train;
        if (tr == null || _train == null) return false;
        return MathF.Abs(p.X - tr.X) < 1.7f + radius + 0.5f && p.Y > tr.End - 2f - radius;
    }

    /// <summary>The crossing's lamps blink: the train is coming, or on it.</summary>
    public bool CrossingWarns => _train != null && _warned;

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
            float z = _train.GlobalPosition.Z - 11.9f + k * 2.2f;
            _t.Obstacles[_trainObs[k]] = new Obstacle(new Vector2(tr.X, z), 1.7f, v, Height: ObstacleHeights.Train);
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
