using System;
using System.Collections.Generic;
using Godot;
using CCCPRacing.Race;

namespace CCCPRacing.Track;

/// <summary>
/// The GAI post (ГАИ, the Soviet traffic police) at the end of the longest straight: a striped
/// booth on legs, the posted limit 40 m before it, and the inspector at the roadside with a
/// radar gun and his striped baton. A car crossing the radar's line gets measured: a flash, the
/// reading in a bubble over his head, the gun raised. The race (RaceScene.SpeedTrap.cs) keeps the
/// records and has the comments. Above RadarMax the reading is ??? and he shakes the gun.
/// </summary>
public partial class SpeedTrap : Node3D
{
    public const float RadarMax = 300f;          // km/h: the Soviet radar's end of the scale
    const float Edge = 2.4f, BoothOut = 9f, SignBefore = 40f, Bubble = 3f;    // the booth beyond the ditch and any roadside fence
    const float SizeScale = 1.6f;               // cartoon-sized like the other people, so he reads from the high camera

    static readonly Color Uniform = new(0.3f, 0.36f, 0.46f), UniformDark = new(0.22f, 0.26f, 0.34f);
    static readonly Color Skin = new(0.88f, 0.68f, 0.55f), Black = new(0.08f, 0.08f, 0.08f);
    static readonly Color White = new(0.92f, 0.92f, 0.9f), Red = new(0.78f, 0.1f, 0.08f), Blue = new(0.2f, 0.32f, 0.62f);

    TrackData _t;
    Vector2 _line, _dir, _right, _officer;
    /// <summary>The radar's line across the road, the racing direction there, towards the inspector's side, the road's half-width.</summary>
    public Vector2 Line => _line;
    public Vector2 Dir => _dir;
    public Vector2 Side => _right;
    public float Half => _half;
    /// <summary>The radar's road metre and side (+1 right of the racing direction, -1 left).</summary>
    public int At { get; private set; }
    public float SideSign { get; private set; } = 1f;
    float _half, _prevAlong = float.NaN, _bubble, _flash, _shake;
    string _text;
    Node3D _figure, _head, _gunArm, _sign;
    Transform3D _figureRest, _signRest;
    // Hit by the car they fall over (the inspector gets up again, the sign stays down) and it ploughs on.
    readonly Knockdown _officerDown = new(), _signDown = new(stays: true);
    int _officerObstacle, _signObstacle;
    OmniLight3D _flashLight;

    public float Limit { get; private set; }
    /// <summary>Where the inspector stands (for the bubble and the tests).</summary>
    public Vector2 OfficerPosition => _officer;
    /// <summary>The booth itself, for camera framing and landmark markers.</summary>
    public Vector3 BoothPosition { get; private set; }
    /// <summary>Keep the measured speed visible while a school card freezes the scene.</summary>
    public bool HoldReading;

    /// <summary>Shared by the booth and its electrical service, so the cable stays on the wall.</summary>
    public static Transform3D BoothTransform(TrackData t, SpeedTrapDef def)
    {
        int i = ((int)def.At % t.Center.Length + t.Center.Length) % t.Center.Length;
        Vector2 dir = t.CenterDir(i);
        Vector2 side = new Vector2(-dir.Y, dir.X) * (def.Side == "left" ? -1f : 1f);
        Vector2 p = t.Center[i] + side * (t.HalfWidth[i] + BoothOut) + dir * 3f;
        return new Transform3D(new Basis(Vector3.Up, TrackData.YawFor(-side)),
            new Vector3(p.X, t.MeshHeight(p.X, p.Y), p.Y));
    }

    public void Init(TrackData t, SpeedTrapDef def)
    {
        Name = "SpeedTrap";
        _t = t;
        Limit = def.Limit;
        int n = t.Center.Length, i = ((int)def.At % n + n) % n;
        _line = t.Center[i];
        _dir = t.CenterDir(i);
        float side = def.Side == "left" ? -1f : 1f;
        At = i;
        SideSign = side;
        _right = new Vector2(-_dir.Y, _dir.X) * side;          // towards the inspector's side of the road
        _half = t.HalfWidth[i];
        _officer = _line + _right * (_half + Edge);
        var boothTransform = BoothTransform(t, def);
        BoothPosition = boothTransform.Origin;
        Vector2 booth = new(boothTransform.Origin.X, boothTransform.Origin.Z);
        int si = ((int)(def.At - SignBefore) % n + n) % n;
        Vector2 sign = t.Center[si] + _right * (t.HalfWidth[si] + 1.6f);

        _figure = Inspector(out _gunArm, out _head);
        _figure.Name = "Inspector";
        _figure.Position = new Vector3(_officer.X, t.MeshHeight(_officer.X, _officer.Y), _officer.Y);
        _figure.Rotation = new Vector3(0, TrackData.YawFor(-_right), 0);    // from the verge towards the road
        _figure.Scale = Vector3.One * SizeScale;
        AddChild(_figure);
        var b = Booth();
        b.Transform = boothTransform;                                  // the window towards the road
        AddChild(b);
        var s = LimitSign((int)def.Limit);
        s.Name = "LimitSign";
        s.Position = new Vector3(sign.X, t.MeshHeight(sign.X, sign.Y), sign.Y);
        s.Rotation = new Vector3(0, TrackData.YawFor(-_dir), 0);             // faces the oncoming car
        AddChild(s);
        _flashLight = new OmniLight3D { Name = "RadarFlash", LightColor = new Color(0.85f, 0.92f, 1f), LightEnergy = 6f, OmniRange = 14f, ShadowEnabled = false, Visible = false };
        _flashLight.Position = _figure.Position + Vector3.Up * 1.6f;
        AddChild(_flashLight);
        _sign = s;
        _figureRest = _figure.Transform;
        _signRest = s.Transform;
        _officerObstacle = t.Obstacles.Count;
        t.Obstacles.Add(new Obstacle(_officer, 0.35f * SizeScale, default, false, 0.5f, true, 2.5f));      // a man: 9 km/h off the car
        t.Obstacles.Add(new Obstacle(booth, 1.2f));
        _signObstacle = t.Obstacles.Count;
        t.Obstacles.Add(new Obstacle(sign, 0.12f, default, false, 0.5f, true, 0.8f));                     // a thin pole
        _officerDown.Bind(t, _officerObstacle);
        _signDown.Bind(t, _signObstacle);
        ResetGaze();
        Tape.Version(this, 1);
        Tape.Part(this, "root"); Tape.Part(_figure, "inspector"); Tape.Part(_head, "head");
        Tape.Part(_gunArm, "radarArm"); Tape.Part(_sign, "sign"); Tape.Part(_flashLight, "flash");
    }

    /// <summary>The inspector and the sign: what knocks them over (Breakables.AddKnock).</summary>
    public IEnumerable<(int obstacle, Func<Vector3, bool> hit)> Knocks()
    {
        yield return (_officerObstacle, _officerDown.Hit);
        yield return (_signObstacle, _signDown.Hit);
    }


    /// <summary>
    /// Per frame with the car: true once as it crosses the radar's line going the right way,
    /// within 6 m of the road; kmh is the reading (NaN above RadarMax).
    /// </summary>
    public bool Check(Vector3 car, Vector3 vel, out float kmh)
    {
        kmh = 0f;
        if (TapeDriven) return false;
        Vector2 p = new(car.X, car.Z);
        float along = (p - _line).Dot(_dir);
        float lateral = MathF.Abs((p - _line).Dot(_right));
        bool crossed = !float.IsNaN(_prevAlong) && _prevAlong < 0f && along >= 0f && along < 8f && lateral < _half + 6f;
        _prevAlong = MathF.Abs(along) < 40f ? along : float.NaN;
        if (!crossed || new Vector2(vel.X, vel.Z).Dot(_dir) <= 0f) return false;
        kmh = new Vector2(vel.X, vel.Z).Length() * 3.6f;
        if (kmh > RadarMax) kmh = float.NaN;
        _text = float.IsNaN(kmh) ? "???" : $"{kmh:0} КМ/Ч";
        _bubble = Bubble;
        _flash = 0.12f;
        _shake = float.IsNaN(kmh) ? 1.5f : 0f;
        TapeFx?.Fx(new TapeEvent(0, TapeEventType.Effect, Kind: TapeFxKind.RadarReading,
            Param: float.IsNaN(kmh) ? -1 : kmh, Pos: _figure.Position + Vector3.Up * 2f * SizeScale, Life: Bubble));
        return true;
    }

    /// <summary>A new run: no half-measured crossing carried over.</summary>
    public void Reset()
    {
        _prevAlong = float.NaN;
        _bubble = _shake = 0f;
        _officerDown.Reset();
        _signDown.Reset();
        ResetGaze();
        _flash = 0;
        if (_figure != null) { _figure.Transform = _figureRest; _sign.Transform = _signRest; _gunArm.Rotation = Vector3.Zero; _flashLight.Visible = false; }
    }

    public IEnumerable<(Vector3 pos, string text)> ActiveShouts()
    {
        if (_bubble > 0f && _text != null) yield return (_figure.Position + Vector3.Up * 2f * SizeScale, _text);
    }

    public void Tick(float delta, Vector3? car)
    {
        if (TapeDriven) return;
        float dt = HoldReading ? 0f : delta;
        _bubble -= dt;
        _flash -= dt;
        _shake -= dt;
        _flashLight.Visible = _flash > 0f;
        _flashLight.LightEnergy = 6f;
        // The gun comes up while he reads it out, and shakes when the needle went off the scale.
        float raise = _bubble > 0f ? 1f : 0f;
        float shake = _shake > 0f ? MathF.Sin(_shake * 40f) * 12f : 0f;
        _gunArm.RotationDegrees = new Vector3(-80f * raise + shake, 0, 0);
        // Knocked over by the car: he falls and gets up again, the sign stays down.
        _officerDown.Tick(delta);
        _signDown.Tick(delta);
        GazeTick(dt, car);
        var body = new Basis(Vector3.Up, _bodyYaw) * Basis.FromScale(_figureRest.Basis.Scale);
        _figure.Transform = new Transform3D(_officerDown.Tilt * body, _figureRest.Origin);
        _sign.Transform = new Transform3D(_signDown.Tilt * _signRest.Basis, _signRest.Origin);
    }

    /// <summary>The inspector: grey-blue uniform, white belt and sling, a cap with a red band; the
    /// radar gun in the right hand (an arm that lifts), the striped baton in the left. Front: -Z.</summary>
    static Node3D Inspector(out Node3D gunArm, out Node3D head)
    {
        var n = new Node3D();
        foreach (float s in new[] { -1f, 1f })
        {
            n.AddChild(Props.Part(Props.Box(0.15f, 0.24f, 0.3f), Black, new Vector3(s * 0.11f, 0.12f, -0.04f)));
            n.AddChild(Props.Part(Props.Box(0.16f, 0.5f, 0.18f), UniformDark, new Vector3(s * 0.11f, 0.48f, 0)));
        }
        n.AddChild(Props.Part(Props.Box(0.5f, 0.55f, 0.3f), Uniform, new Vector3(0, 0.98f, 0)));
        n.AddChild(Props.Part(Props.Box(0.52f, 0.07f, 0.32f), White, new Vector3(0, 0.78f, 0)));                       // belt
        n.AddChild(Props.Part(Props.Box(0.07f, 0.6f, 0.02f), White, new Vector3(0.05f, 1f, -0.16f), new Vector3(0, 0, 35)));   // sling
        head = new Node3D { Name = "Head", Position = new Vector3(0, 1.46f, 0) };
        n.AddChild(head);
        head.AddChild(Props.Part(Props.Ball(0.22f, 8), Skin, Vector3.Zero));
        head.AddChild(Props.Part(Props.Box(0.24f, 0.05f, 0.05f), Black, new Vector3(0, -.08f, -0.2f))); // moustache
        head.AddChild(Props.Part(Props.Cyl(0.23f, 0.12f, 10), Red, new Vector3(0, .20f, 0)));
        head.AddChild(Props.Part(Props.Cyl(0.24f, 0.1f, 10, 0.34f), Uniform, new Vector3(0, .30f, 0)));
        head.AddChild(Props.Part(Props.Box(0.32f, 0.03f, 0.18f), Black, new Vector3(0, .18f, -0.24f), new Vector3(-20, 0, 0)));
        // Left arm down with the baton.
        n.AddChild(Props.Part(Props.Box(0.12f, 0.5f, 0.12f), Uniform, new Vector3(-0.32f, 0.98f, 0)));
        for (int k = 0; k < 4; k++)
            n.AddChild(Props.Part(Props.Box(0.06f, 0.12f, 0.06f), k % 2 == 0 ? White : Black, new Vector3(-0.34f, 0.66f - k * 0.12f, -0.04f)));
        // Right arm on a shoulder pivot with the radar gun.
        gunArm = new Node3D { Name = "RadarArm", Position = new Vector3(0.32f, 1.2f, 0) };
        gunArm.AddChild(Props.Part(Props.Box(0.12f, 0.5f, 0.12f), Uniform, new Vector3(0, -0.22f, 0)));
        gunArm.AddChild(Props.Part(Props.Box(0.16f, 0.16f, 0.34f), Black, new Vector3(0, -0.5f, -0.1f)));
        gunArm.AddChild(Props.Part(Props.Cyl(0.08f, 0.16f, 8), new Color(0.4f, 0.4f, 0.42f), new Vector3(0, -0.56f, -0.28f), new Vector3(90, 0, 0)));
        n.AddChild(gunArm);
        return n;
    }

    /// <summary>The GAI booth: a little glass-sided box on four legs, white with a blue band, ГАИ on top. Window: -Z.</summary>
    static Node3D Booth()
    {
        var n = new Node3D();
        foreach (float x in new[] { -0.7f, 0.7f })
        foreach (float z in new[] { -0.7f, 0.7f })
            n.AddChild(Props.Part(Props.Box(0.12f, 1.2f, 0.12f), new Color(0.35f, 0.35f, 0.37f), new Vector3(x, 0.6f, z)));
        n.AddChild(Props.Part(Props.Box(1.8f, 2f, 1.8f), White, new Vector3(0, 2.2f, 0)));
        n.AddChild(Props.Part(Props.Box(1.84f, 0.3f, 1.84f), Blue, new Vector3(0, 1.6f, 0)));
        n.AddChild(Props.Part(Props.Box(1.5f, 0.8f, 0.04f), new Color(0.3f, 0.4f, 0.45f), new Vector3(0, 2.6f, -0.92f)));
        n.AddChild(Props.Part(Props.Box(2f, 0.2f, 2f), Blue, new Vector3(0, 3.3f, 0)));
        n.AddChild(Buildings.TextPanel("ГАИ", 1.6f, 0.6f, new Vector3(0, 3.75f, -0.5f), Blue, White));
        return n;
    }

    /// <summary>The posted limit: a white disc with a red ring on a pole, the number on it. Face: +Z.</summary>
    static Node3D LimitSign(int limit)
    {
        var n = new Node3D();
        n.AddChild(Props.Part(Props.Cyl(0.05f, 2.2f, 6), new Color(0.6f, 0.6f, 0.6f), new Vector3(0, 1.1f, 0)));
        n.AddChild(Props.Part(Props.Cyl(0.42f, 0.04f, 12), Red, new Vector3(0, 2.3f, 0), new Vector3(90, 0, 0)));
        n.AddChild(Props.Part(Props.Cyl(0.32f, 0.05f, 12), White, new Vector3(0, 2.3f, 0), new Vector3(90, 0, 0)));
        n.AddChild(Buildings.TextPanel(limit.ToString(), 0.42f, 0.3f, new Vector3(0, 2.3f, 0.04f), White, Black));
        return n;
    }
}
