using System;
using System.Collections.Generic;
using CCCPRacing.Car;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Track;

/// <summary>
/// Two KGB officers at the start line, one on each verge, in greatcoats and enormous peaked caps.
/// The clerk (left of the road) writes down every lap on his clipboard; the supervisor (right)
/// stands with his hands behind his back, smokes, follows the car with his eyes and shakes or nods
/// his head. Both are solid: a KGB officer does not move for anybody.
/// </summary>
public partial class KgbOfficers : Node3D
{
    const float SizeScale = 1.75f;               // cartoon-sized, a touch bigger than the babushkas
    const float Radius = 0.45f * SizeScale;      // obstacle circle
    const float VergeGap = 2.6f;                 // metres beyond the road edge
    const float BeforeLine = 4.5f;               // metres before the line, towards the grid
    const float WatchRange = 60f;
    const float ScribbleTime = 2.2f;
    const float LookUpTime = 0.6f;
    const float BubbleTime = 3f;

    static readonly Color Coat = new(0.2f, 0.23f, 0.2f);
    static readonly Color CoatDark = new(0.13f, 0.15f, 0.13f);
    static readonly Color Crown = new(0.5f, 0.5f, 0.36f);
    static readonly Color Band = new(0.12f, 0.24f, 0.78f);
    static readonly Color Piping = new(0.8f, 0.1f, 0.08f);
    static readonly Color Gold = new(0.92f, 0.76f, 0.2f);
    static readonly Color Black = new(0.06f, 0.06f, 0.07f);
    static readonly Color Skin = new(0.88f, 0.66f, 0.56f);
    static readonly Color Nose = new(0.86f, 0.44f, 0.4f);
    static readonly Color Leather = new(0.3f, 0.19f, 0.1f);
    static readonly Color Paper = new(0.95f, 0.94f, 0.88f);
    static readonly Color GreyCoat = new(0.42f, 0.42f, 0.4f);
    static readonly Color GreyDark = new(0.28f, 0.28f, 0.27f);
    static readonly Color Astrakhan = new(0.33f, 0.31f, 0.3f);
    static readonly Color Folder = new(0.55f, 0.12f, 0.1f);
    const float LiaisonBack = 3.2f;              // metres further from the line than the supervisor

    class Officer
    {
        public Vector2 Pos;
        public float Yaw, RestYaw;
        public Node3D Root, Body, Head, ArmL, ArmR;
        public readonly Knockdown Down = new();     // hit by the car: falls over, gets up again
        public int Obstacle;
    }

    TrackData _t;
    TyreEffects _fx;
    Officer _clerk, _boss, _liaison;
    Vector2 _line, _dir;
    float _half, _time;
    readonly RandomNumberGenerator _rng = new() { Seed = 1917 };
    readonly bool _debug = OS.HasEnvironment("KGBDBG");

    // Clerk state: looking up at the car, then scribbling; otherwise idle with the stopwatch.
    float _lookUp, _scribble, _watchCd = 4f, _watchTime, _bubble;
    string _bubbleText;
    // Line crossing check (works for the race car and the title replay alike).
    float _prevAlong = float.NaN;
    Vector2 _prevCar;
    float _sinceLog = 99f;
    // Supervisor state.
    float _gestureCd = 5f, _gesture, _smokeCd = 1f, _bossBubble;
    bool _nod;
    float _bossHead;
    string _bossText = "ХОРОШО.";
    // Liaison officer: his bubble and the finger wagging while he speaks.
    float _liaisonBubble, _liaisonWag;
    string _liaisonText;

    /// <summary>Laps the clerk has written down since the last reset.</summary>
    public int LapsLogged { get; private set; }
    /// <summary>The clerk is writing right now (for tests).</summary>
    public bool Scribbling => _scribble > 0;
    /// <summary>Supervisor body yaw minus his rest yaw, radians (for tests).</summary>
    public float SupervisorTurn => Mathf.AngleDifference(_boss.RestYaw, _boss.Yaw);
    public Vector2 ClerkPosition => _clerk.Pos;
    public Vector2 SupervisorPosition => _boss.Pos;
    public Vector2 LiaisonPosition => _liaison.Pos;
    /// <summary>The liaison officer's collision circle in the track's obstacle list.</summary>
    public int LiaisonObstacle { get; private set; }
    /// <summary>The liaison officer's figure (hidden with the norms).</summary>
    public Node3D Liaison => _liaison.Root;

    public void Init(TrackData t, TyreEffects fx)
    {
        _t = t;
        _fx = fx;
        _line = t.Center[0];
        _dir = t.CenterDir(0);
        _half = t.HalfWidth[0];
        Vector2 right = new(-_dir.Y, _dir.X);    // TrackBuilder's +side: the driver's right
        Vector2 at = _line - _dir * BeforeLine;
        float off = _half + VergeGap;

        _clerk = Build(at - right * off, clerk: true);
        _boss = Build(at + right * off, clerk: false);
        _liaison = BuildLiaison(at - _dir * LiaisonBack + right * off);
        _clerk.RestYaw = TrackData.YawFor(right);          // all face the road
        _boss.RestYaw = _liaison.RestYaw = TrackData.YawFor(-right);
        LiaisonObstacle = t.Obstacles.Count + 2;
        foreach (var o in new[] { _clerk, _boss, _liaison })
        {
            o.Yaw = o.RestYaw;
            o.Obstacle = t.Obstacles.Count;
            t.Obstacles.Add(new Obstacle(o.Pos, Radius, default, false, 0.5f, true, 2.5f, Material: ImpactMaterial.Soft));     // knocked over, not a wall
            o.Down.Bind(t, o.Obstacle);
        }
        if (_debug) GD.Print($"KGB clerk at {_clerk.Pos}, supervisor at {_boss.Pos}");
        // The world tape: the three officers (the liaison is also a WorldActor of its own, but only this actor has a root mark, so only this one is covered).
        Tape.Version(this, 1); Tape.Part(this, "root");
        foreach (var (o, who) in new[] { (_clerk, "clerk"), (_boss, "boss"), (_liaison, "liaison") })
        {
            Tape.Part(o.Root, who + "/figure"); Tape.Part(o.Head, who + "/head"); Tape.Part(o.ArmL, who + "/armL"); Tape.Part(o.ArmR, who + "/armR");
        }
        Reset();
    }

    public void Reset()
    {
        _lookUp = _scribble = _bubble = _bossBubble = _gesture = _liaisonBubble = _liaisonWag = 0;
        _watchCd = 4f;
        _prevAlong = float.NaN;
        _sinceLog = 99f;
        LapsLogged = 0;
        foreach (var o in new[] { _clerk, _boss, _liaison }) { o.Yaw = o.RestYaw; o.Down.Reset(); }
        Pose(0);
    }

    /// <summary>The officers: what knocks them over (Breakables.AddKnock).</summary>
    public IEnumerable<(int obstacle, Func<Vector3, bool> hit)> Knocks()
    {
        foreach (var o in new[] { _clerk, _boss, _liaison }) yield return (o.Obstacle, o.Down.Hit);
    }

    /// <summary>
    /// The clerk records a lap: he looks up and scribbles, and with a time the HUD shows it in a
    /// bubble ("0:57.12 ЗАПИСАНО"). A record gets an approving nod from the supervisor.
    /// </summary>
    public void LogLap(float time = -1, bool record = false)
    {
        LapsLogged++;
        _sinceLog = 0;
        StartWriting();
        if (time > 0)
        {
            _bubbleText = $"{LapTimer.Format(time)} ЗАПИСАНО";
            _bubble = BubbleTime;
        }
        if (record)
        {
            _nod = true;
            _gesture = 1.6f;
            _bossBubble = 2.5f;
            _bossText = "ХОРОШО.";
        }
        if (_debug) GD.Print($"KGB: lap logged ({LapsLogged}) {(time > 0 ? LapTimer.Format(time) : "")}");
    }

    /// <summary>A wreck goes on file: the clerk looks up, scribbles and says so ("ЗАПИСАНО.").</summary>
    public void LogWreck()
    {
        StartWriting();
        _bubbleText = "ЗАПИСАНО.";
        _bubble = BubbleTime;
        if (_debug) GD.Print("KGB: wreck logged");
    }

    /// <summary>
    /// The supervisor has his say in a bubble (e.g. the laps left in the plan); with a gesture he
    /// nods (approve) or shakes his head, otherwise he just says it.
    /// </summary>
    public void Say(string text, bool? approve = null, float duration = 3f)
    {
        _bossText = text;
        _bossBubble = duration;
        if (approve is bool nod)
        {
            _nod = nod;
            _gesture = 1.6f;
            _gestureCd = 6f;
        }
    }

    /// <summary>The liaison officer announces a norm (or the laps left) in his bubble, wagging a finger.</summary>
    public void SayLiaison(string text, float duration = 3.5f)
    {
        _liaisonText = text;
        _liaisonBubble = duration;
        _liaisonWag = Math.Min(duration, 2f);
    }

    void StartWriting()
    {
        _lookUp = LookUpTime;
        _scribble = ScribbleTime;
        _watchTime = 0;
    }

    /// <summary>What the officers say, anchored above their caps (for the HUD bubbles).</summary>
    public IEnumerable<(Vector3 pos, string text)> ActiveShouts()
    {
        if (_bubble > 0 && _bubbleText != null) yield return (Top(_clerk), _bubbleText);
        if (_bossBubble > 0 && _bossText != null) yield return (Top(_boss), _bossText);
        if (_liaisonBubble > 0 && _liaisonText != null && _liaison.Root.Visible) yield return (Top(_liaison) + Vector3.Up * 1.9f, _liaisonText);   // over the supervisor's bubble
    }

    Vector3 Top(Officer o) => new(o.Pos.X, _t.MeshHeight(o.Pos.X, o.Pos.Y) + 2.05f * SizeScale, o.Pos.Y);

    public void Tick(float dt, CarSim car)
    {
        _time += dt;
        _sinceLog += dt;
        _bubble -= dt;
        _bossBubble -= dt;
        _liaisonBubble -= dt;
        _liaisonWag -= dt;
        var carPos = new Vector2(car.Pos.X, car.Pos.Z);
        CheckLine(carPos);
        TickClerk(dt, carPos);
        TickBoss(dt, car, carPos);
        // The liaison officer turns a little towards a car close by, and back to the road.
        var li = _liaison;
        float lw = carPos.DistanceTo(li.Pos) < 30f ? TurnToward(li, carPos, 0.6f) : li.RestYaw;
        li.Yaw = Mathf.LerpAngle(li.Yaw, lw, Math.Min(1f, dt * 2f));
        Pose(dt);
        if (_debug && (int)(_time * 2) != (int)((_time - dt) * 2))
            GD.Print($"KGB {_time:F1} s: car {carPos.DistanceTo(_boss.Pos):F0} m from the supervisor, turned {Mathf.RadToDeg(SupervisorTurn):F0} deg, head {Mathf.RadToDeg(_boss.Head.Rotation.Y):F0}; clerk {(_lookUp > 0 ? "looking up" : _scribble > 0 ? "writing" : _watchTime > 0 ? "stopwatch" : "tapping")}");
    }

    void CheckLine(Vector2 carPos)
    {
        Vector2 rel = carPos - _line;
        float along = rel.Dot(_dir);
        float across = Math.Abs(rel.Dot(new Vector2(-_dir.Y, _dir.X)));
        bool jumped = carPos.DistanceTo(_prevCar) > 8f;       // reset, rescue or replay loop
        if (!jumped && !float.IsNaN(_prevAlong) && _prevAlong < 0 && along >= 0 && across < _half + 4f && _sinceLog > 1.5f)
        {
            // The race calls LogLap with the time on a lap; this covers the start, the replay car
            // and anything else crossing: he looks up and notes it anyway.
            StartWriting();
            if (_debug) GD.Print($"KGB: line crossed at {_time:F2} s");
        }
        _prevAlong = along;
        _prevCar = carPos;
    }

    void TickClerk(float dt, Vector2 carPos)
    {
        var o = _clerk;
        if (_lookUp > 0) _lookUp -= dt;
        else if (_scribble > 0) _scribble -= dt;
        else
        {
            // Idle: a glance at the stopwatch now and then.
            _watchCd -= dt;
            if (_watchTime > 0) _watchTime -= dt;
            else if (_watchCd <= 0) { _watchTime = 1.4f; _watchCd = _rng.RandfRange(3f, 6f); }
        }
        // He turns a little towards the car while looking up, otherwise faces the road.
        float want = o.RestYaw;
        if (_lookUp > 0 && carPos.DistanceTo(o.Pos) < WatchRange)
            want = TurnToward(o, carPos, 0.7f);
        o.Yaw = Mathf.LerpAngle(o.Yaw, want, Math.Min(1f, dt * 5f));
    }

    void TickBoss(float dt, CarSim car, Vector2 carPos)
    {
        var o = _boss;
        float d = carPos.DistanceTo(o.Pos);
        bool watching = d < WatchRange && d > 0.5f;
        float want = watching ? TurnToward(o, carPos, 1.5f) : o.RestYaw;
        o.Yaw = Mathf.LerpAngle(o.Yaw, want, Math.Min(1f, dt * 2.5f));
        // The head keeps his eyes on the car past what the body turns.
        float headWant = watching ? Math.Clamp(Mathf.AngleDifference(o.Yaw, TrackData.YawFor((carPos - o.Pos).Normalized())), -0.9f, 0.9f) : 0f;
        _bossHead = Mathf.Lerp(_bossHead, headWant, Math.Min(1f, dt * 6f));

        // Occasional disapproving shake (or, rarely, a nod); a fast pass close by always earns a shake.
        _gestureCd -= dt;
        if (_gesture > 0) _gesture -= dt;
        else if (watching && d < 15f && car.Speed > 15f && _gestureCd < 3f)
        {
            _nod = false; _gesture = 1.4f; _gestureCd = 6f;
        }
        else if (_gestureCd <= 0)
        {
            _nod = _rng.Randf() < 0.3f; _gesture = 1.4f; _gestureCd = _rng.RandfRange(5f, 9f);
        }

        // Papirosa smoke from the corner of his mouth.
        _smokeCd -= dt;
        if (_smokeCd <= 0 && _fx != null && IsInsideTree())
        {
            _smokeCd = _rng.RandfRange(0.9f, 1.8f);
            Vector3 mouth = o.Head.GlobalTransform * new Vector3(0.12f, 0.1f, -0.3f);
            _fx.Smoke(mouth, new Vector3(_rng.RandfRange(-0.2f, 0.2f), 0.5f, _rng.RandfRange(-0.2f, 0.2f)),
                new Color(0.82f, 0.82f, 0.8f), 0.45f, 0.18f, 0.9f, 1.8f);
        }
    }

    /// <summary>Yaw towards the car, but the body turns at most maxTurn from rest (the head does the rest).</summary>
    static float TurnToward(Officer o, Vector2 target, float maxTurn)
    {
        float yaw = TrackData.YawFor((target - o.Pos).Normalized());
        float diff = Math.Clamp(Mathf.AngleDifference(o.RestYaw, yaw), -maxTurn, maxTurn);
        return o.RestYaw + diff;
    }

    // ------------------------------------------------------------ posing

    void Pose(float dt)
    {
        foreach (var o in new[] { _clerk, _boss, _liaison })
        {
            float y = _t.MeshHeight(o.Pos.X, o.Pos.Y);     // this node sits at the origin
            o.Down.Tick(dt);
            o.Root.Transform = new Transform3D(o.Down.Tilt * new Basis(Vector3.Up, o.Yaw).Scaled(Vector3.One * SizeScale), new Vector3(o.Pos.X, y, o.Pos.Y));
        }

        // Clerk: clipboard held in the left hand; the right writes, taps, or holds up the stopwatch.
        var c = _clerk;
        c.ArmL.Rotation = new Vector3(1.05f, 0, 0.35f);
        if (_lookUp > 0)
        {
            c.Head.Rotation = new Vector3(0.15f, 0, 0);          // chin up: who is this?
            c.ArmR.Rotation = new Vector3(0.9f, 0, -0.3f);
        }
        else if (_scribble > 0)
        {
            float w = _time * 22f;
            c.Head.Rotation = new Vector3(-0.45f, MathF.Sin(w * 0.15f) * 0.08f, 0);
            c.ArmR.Rotation = new Vector3(1.0f + MathF.Sin(w) * 0.1f, MathF.Sin(w * 0.37f) * 0.2f, -0.45f + MathF.Cos(w) * 0.06f);
        }
        else if (_watchTime > 0)
        {
            c.Head.Rotation = new Vector3(-0.3f, 0.35f, 0);
            c.ArmR.Rotation = new Vector3(1.55f, 0, -0.1f);
        }
        else
        {
            // Tapping the pencil on the board.
            float tap = MathF.Max(0, MathF.Sin(_time * 7f));
            c.Head.Rotation = new Vector3(-0.2f, 0, 0);
            c.ArmR.Rotation = new Vector3(0.95f + tap * 0.12f, 0, -0.42f);
        }

        // Supervisor: hands clasped behind his back, the head follows past the body's turn.
        var b = _boss;
        b.ArmL.Rotation = new Vector3(-0.45f, 0, -0.3f);
        b.ArmR.Rotation = new Vector3(-0.45f, 0, 0.3f);
        float g = _gesture > 0 ? MathF.Sin(_time * 14f) * Math.Min(1f, _gesture) : 0f;
        b.Head.Rotation = _nod ? new Vector3(-Math.Abs(g) * 0.3f, _bossHead, 0) : new Vector3(0, _bossHead + g * 0.35f, 0);

        // Liaison officer: the folder under the left arm, the right index finger up and wagging while he speaks.
        var l = _liaison;
        l.ArmL.Rotation = new Vector3(0.5f, 0, 0.25f);
        float wag = _liaisonWag > 0 ? MathF.Sin(_time * 16f) * 0.25f : 0f;
        l.ArmR.Rotation = _liaisonWag > 0 ? new Vector3(2.5f, wag, -0.15f + wag) : new Vector3(0.1f, 0, -0.12f);
        l.Head.Rotation = new Vector3(_liaisonWag > 0 ? 0.1f : -0.05f, 0, 0);
    }

    // ------------------------------------------------------------ models

    /// <summary>The Party's liaison officer: grey coat, red armband, astrakhan hat, spectacles, the norms in a red folder.</summary>
    Officer BuildLiaison(Vector2 pos)
    {
        var o = new Officer { Pos = pos };
        o.Root = new Node3D { Name = "LiaisonOfficer" };
        AddChild(o.Root);
        o.Body = new Node3D();
        o.Root.AddChild(o.Body);
        var body = o.Body;
        foreach (float s in new[] { -1f, 1f })
            body.AddChild(Props.Part(Props.Box(0.14f, 0.16f, 0.28f), Black, new Vector3(s * 0.1f, 0.08f, -0.04f)));
        body.AddChild(Props.Part(Props.Box(0.12f, 0.2f, 0.12f), GreyDark, new Vector3(-0.1f, 0.26f, 0)));   // trousers under the coat
        body.AddChild(Props.Part(Props.Box(0.12f, 0.2f, 0.12f), GreyDark, new Vector3(0.1f, 0.26f, 0)));
        body.AddChild(Props.Part(Props.Cyl(0.3f, 0.76f, 8, 0.27f), GreyCoat, new Vector3(0, 0.72f, 0)));
        body.AddChild(Props.Part(Props.Box(0.56f, 0.14f, 0.32f), GreyCoat, new Vector3(0, 1.08f, 0)));
        body.AddChild(Props.Part(Props.Box(0.26f, 0.12f, 0.08f), GreyDark, new Vector3(0, 1.12f, -0.14f)));   // collar
        body.AddChild(Props.Part(Props.Box(0.08f, 0.12f, 0.02f), Piping, new Vector3(0, 1.1f, -0.185f)));       // red tie
        for (int k = 0; k < 3; k++)
            body.AddChild(Props.Part(Props.Box(0.05f, 0.05f, 0.03f), Black, new Vector3(0, 0.55f + k * 0.17f, -0.3f + k * 0.01f)));
        body.AddChild(Props.Part(Props.Box(0.06f, 0.06f, 0.03f), Piping, new Vector3(-0.13f, 0.98f, -0.27f)));  // Party badge
        o.Head = new Node3D { Position = new Vector3(0, 1.18f, 0) };
        body.AddChild(o.Head);
        var h = o.Head;
        h.AddChild(Props.Part(Props.Ball(0.22f, 8), Skin, new Vector3(0, 0.2f, 0)));
        h.AddChild(Props.Part(Props.Ball(0.06f), Nose, new Vector3(0, 0.18f, -0.22f)));
        h.AddChild(Props.Part(Props.Box(0.34f, 0.03f, 0.03f), Black, new Vector3(0, 0.27f, -0.2f)));           // spectacles
        h.AddChild(Props.Part(Props.Box(0.1f, 0.08f, 0.02f), new Color(0.75f, 0.85f, 0.9f), new Vector3(-0.08f, 0.26f, -0.215f)));
        h.AddChild(Props.Part(Props.Box(0.1f, 0.08f, 0.02f), new Color(0.75f, 0.85f, 0.9f), new Vector3(0.08f, 0.26f, -0.215f)));
        h.AddChild(Props.Part(Props.Cyl(0.24f, 0.2f, 10, 0.25f), Astrakhan, new Vector3(0, 0.44f, 0)));         // pirozhok hat
        h.AddChild(Props.Part(Props.Box(0.44f, 0.04f, 0.26f), GreyDark, new Vector3(0, 0.55f, 0)));
        o.ArmL = new Node3D { Position = new Vector3(-0.31f, 1.06f, 0) };
        o.ArmR = new Node3D { Position = new Vector3(0.31f, 1.06f, 0) };
        body.AddChild(o.ArmL);
        body.AddChild(o.ArmR);
        foreach (var arm in new[] { o.ArmL, o.ArmR })
        {
            arm.AddChild(Props.Part(Props.Box(0.14f, 0.5f, 0.15f), GreyCoat, new Vector3(0, -0.25f, 0)));
            arm.AddChild(Props.Part(Props.Ball(0.075f), Skin, new Vector3(0, -0.54f, 0)));
        }
        // The red armband on the left sleeve, the folder with the norms held against the chest.
        o.ArmL.AddChild(Props.Part(Props.Box(0.17f, 0.12f, 0.18f), Piping, new Vector3(0, -0.14f, 0)));
        o.ArmL.AddChild(Props.Part(Props.Box(0.36f, 0.46f, 0.05f), Folder, new Vector3(0.18f, -0.5f, -0.14f), new Vector3(0, 0, 10)));
        o.ArmL.AddChild(Props.Part(Props.Box(0.3f, 0.4f, 0.02f), Paper, new Vector3(0.18f, -0.5f, -0.17f), new Vector3(0, 0, 10)));
        o.ArmR.AddChild(Props.Part(Props.Box(0.035f, 0.14f, 0.035f), Skin, new Vector3(0, -0.66f, 0)));        // the finger
        return o;
    }

    Officer Build(Vector2 pos, bool clerk)
    {
        var o = new Officer { Pos = pos };
        o.Root = new Node3D { Name = clerk ? "KgbClerk" : "KgbSupervisor" };
        AddChild(o.Root);
        o.Body = new Node3D();
        o.Root.AddChild(o.Body);
        var body = o.Body;

        // Boots and a long greatcoat flaring towards the hem.
        foreach (float s in new[] { -1f, 1f })
        {
            body.AddChild(Props.Part(Props.Box(0.15f, 0.24f, 0.3f), Black, new Vector3(s * 0.11f, 0.12f, -0.04f)));
        }
        body.AddChild(Props.Part(Props.Cyl(0.33f, 0.88f, 8, 0.25f), Coat, new Vector3(0, 0.66f, 0)));
        body.AddChild(Props.Part(Props.Cyl(0.27f, 0.07f, 8), Leather, new Vector3(0, 0.82f, 0)));            // belt
        body.AddChild(Props.Part(Props.Box(0.08f, 0.06f, 0.04f), Gold, new Vector3(0, 0.82f, -0.27f)));      // buckle
        for (int k = 0; k < 3; k++)
            body.AddChild(Props.Part(Props.Box(0.05f, 0.05f, 0.04f), Gold, new Vector3(0.08f, 0.5f + k * 0.18f, -0.3f + k * 0.015f)));
        // Broad shoulders, gold epaulettes, a red collar tab.
        body.AddChild(Props.Part(Props.Box(0.66f, 0.16f, 0.34f), Coat, new Vector3(0, 1.08f, 0)));
        body.AddChild(Props.Part(Props.Box(0.2f, 0.04f, 0.2f), Gold, new Vector3(-0.24f, 1.17f, 0)));
        body.AddChild(Props.Part(Props.Box(0.2f, 0.04f, 0.2f), Gold, new Vector3(0.24f, 1.17f, 0)));
        body.AddChild(Props.Part(Props.Box(0.24f, 0.1f, 0.06f), CoatDark, new Vector3(0, 1.17f, -0.15f)));
        body.AddChild(Props.Part(Props.Box(0.06f, 0.06f, 0.02f), Piping, new Vector3(-0.08f, 1.17f, -0.185f)));
        body.AddChild(Props.Part(Props.Box(0.06f, 0.06f, 0.02f), Piping, new Vector3(0.08f, 1.17f, -0.185f)));

        // A big head under an even bigger cap.
        o.Head = new Node3D { Position = new Vector3(0, 1.2f, 0) };
        body.AddChild(o.Head);
        var h = o.Head;
        h.AddChild(Props.Part(Props.Ball(0.24f, 8), Skin, new Vector3(0, 0.22f, 0)));
        h.AddChild(Props.Part(Props.Ball(0.07f), Nose, new Vector3(0, 0.2f, -0.24f)));
        h.AddChild(Props.Part(Props.Box(0.3f, 0.06f, 0.06f), Black, new Vector3(0, 0.12f, -0.21f), new Vector3(0, 0, 0)));   // moustache
        h.AddChild(Props.Part(Props.Box(0.08f, 0.1f, 0.05f), Black, new Vector3(-0.14f, 0.08f, -0.2f), new Vector3(0, 0, -20)));
        h.AddChild(Props.Part(Props.Box(0.08f, 0.1f, 0.05f), Black, new Vector3(0.14f, 0.08f, -0.2f), new Vector3(0, 0, 20)));
        h.AddChild(Props.Part(Props.Box(0.36f, 0.07f, 0.05f), Black, new Vector3(0, 0.29f, -0.2f)));        // sunglasses
        // Furazhka: royal-blue band, huge khaki crown with red piping raised at the front, black
        // visor, gold badge. The hat is the point.
        var cap = new Node3D { Position = new Vector3(0, 0.38f, 0), RotationDegrees = new Vector3(10, 0, 0) };
        h.AddChild(cap);
        cap.AddChild(Props.Part(Props.Cyl(0.25f, 0.16f, 10), Band, new Vector3(0, 0.06f, 0)));
        cap.AddChild(Props.Part(Props.Cyl(0.26f, 0.16f, 12, 0.58f), Crown, new Vector3(0, 0.22f, 0)));
        cap.AddChild(Props.Part(Props.Cyl(0.59f, 0.04f, 12), Piping, new Vector3(0, 0.29f, 0)));
        cap.AddChild(Props.Part(Props.Cyl(0.56f, 0.05f, 12), Crown, new Vector3(0, 0.32f, 0)));
        cap.AddChild(Props.Part(Props.Box(0.44f, 0.03f, 0.24f), Black, new Vector3(0, 0f, -0.3f), new Vector3(-22, 0, 0)));   // visor
        cap.AddChild(Props.Part(Props.Box(0.2f, 0.18f, 0.04f), Gold, new Vector3(0, 0.2f, -0.44f), new Vector3(15, 0, 0)));   // badge
        cap.AddChild(Props.Part(Props.Box(0.09f, 0.09f, 0.03f), Piping, new Vector3(0, 0.2f, -0.465f), new Vector3(15, 0, 0)));

        // Arms on shoulder pivots.
        o.ArmL = new Node3D { Position = new Vector3(-0.34f, 1.08f, 0) };
        o.ArmR = new Node3D { Position = new Vector3(0.34f, 1.08f, 0) };
        body.AddChild(o.ArmL);
        body.AddChild(o.ArmR);
        foreach (var arm in new[] { o.ArmL, o.ArmR })
        {
            arm.AddChild(Props.Part(Props.Box(0.15f, 0.52f, 0.16f), Coat, new Vector3(0, -0.26f, 0)));
            arm.AddChild(Props.Part(Props.Ball(0.08f), Skin, new Vector3(0, -0.56f, 0)));
        }

        if (clerk)
        {
            // Clipboard in the left hand (tilted up towards his face), pencil and stopwatch in the right.
            var board = new Node3D { Position = new Vector3(0.2f, -0.62f, -0.2f), RotationDegrees = new Vector3(-48, 0, -15) };
            o.ArmL.AddChild(board);
            board.AddChild(Props.Part(Props.Box(0.44f, 0.04f, 0.56f), Leather, Vector3.Zero));
            board.AddChild(Props.Part(Props.Box(0.38f, 0.02f, 0.46f), Paper, new Vector3(0, 0.03f, 0.03f)));
            for (int k = 0; k < 4; k++)      // columns of lap times
                board.AddChild(Props.Part(Props.Box(0.26f, 0.01f, 0.025f), new Color(0.3f, 0.3f, 0.45f), new Vector3(-0.02f, 0.045f, -0.1f + k * 0.08f)));
            board.AddChild(Props.Part(Props.Box(0.14f, 0.04f, 0.06f), new Color(0.7f, 0.7f, 0.72f), new Vector3(0, 0.05f, -0.26f)));
            o.ArmR.AddChild(Props.Part(Props.Box(0.03f, 0.2f, 0.03f), new Color(0.95f, 0.8f, 0.2f), new Vector3(0, -0.66f, -0.02f), new Vector3(30, 0, 0)));
            o.ArmR.AddChild(Props.Part(Props.Cyl(0.06f, 0.03f, 8), new Color(0.78f, 0.78f, 0.8f), new Vector3(0.05f, -0.58f, 0.06f), new Vector3(90, 0, 0)));
        }
        else
        {
            // Papirosa in the corner of the mouth, a medal row on the chest.
            h.AddChild(Props.Part(Props.Box(0.03f, 0.03f, 0.16f), Paper, new Vector3(0.1f, 0.08f, -0.3f), new Vector3(10, -20, 0)));
            body.AddChild(Props.Part(Props.Box(0.2f, 0.06f, 0.03f), Piping, new Vector3(-0.12f, 0.98f, -0.28f)));
            body.AddChild(Props.Part(Props.Box(0.06f, 0.08f, 0.03f), Gold, new Vector3(-0.17f, 0.9f, -0.285f)));
            body.AddChild(Props.Part(Props.Box(0.06f, 0.08f, 0.03f), Gold, new Vector3(-0.07f, 0.9f, -0.285f)));
        }
        return o;
    }
}
