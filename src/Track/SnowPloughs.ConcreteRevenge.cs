using System;
using System.Collections.Generic;
using CCCPRacing.Audio;
using CCCPRacing.Car;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Track;

public partial class SnowPloughs
{
    const float PourStart = .65f;
    static readonly Color WetConcreteColor = new(.43f, .41f, .37f);
    readonly List<Node3D> _concretePiles = new();
    readonly Dictionary<Vector2I, List<(Vector2 From, Vector2 To)>> _concreteGrid = new();
    Plough _revengeMixer;
    Node3D _concreteChute, _concreteStream;
    Vector3 _restChute;
    float _unloadTime = -1f;
    int _concreteLapsLeft;
    Vector3? _concreteTail;
    float _concreteTailYaw;
    MeshInstance3D _fallingConcrete;

    public bool ConcreteRevengePending { get; private set; }
    public ITapeFxHost ConcreteTape;

    public bool ArmConcreteRevenge()
    {
        if (_revengeMixer == null || _tapeDriven || _concreteLapsLeft > 0 || ConcreteRevengePending) return false;
        return ConcreteRevengePending = true;
    }

    void InitConcreteRevenge()
    {
        if (_t.Id != "atomgrad") return;
        _revengeMixer = _ploughs.FindLast(p => p.Mixer);
        if (_revengeMixer == null) return;
        _concreteChute = _revengeMixer.Model.GetNode<Node3D>("MixingChute");
        _restChute = _concreteChute.RotationDegrees;
        _concreteStream = new MeshInstance3D { Position = new Vector3(0, -.04f, 1.03f) };
        var flow = new ShaderMaterial { Shader = new Shader { Code = """
            shader_type spatial;
            void fragment() {
                float folds = sin(UV.y * 29.0 - TIME * 17.0 + sin(UV.x * 16.0));
                ALBEDO = vec3(0.45, 0.43, 0.39) + vec3(0.045) * folds;
                ROUGHNESS = 0.43;
            }
            """ } };
        _concreteStream.AddChild(new MeshInstance3D
        {
            Position = new Vector3(0, .092f, -1.03f),
            Mesh = new BoxMesh { Size = new Vector3(.36f, .035f, 1.12f) },
            MaterialOverride = flow,
        });
        _fallingConcrete = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = .155f, BottomRadius = .125f, Height = 1f, RadialSegments = 9 },
            MaterialOverride = flow,
        };
        _concreteStream.AddChild(_fallingConcrete);
        _concreteStream.Name = "WetConcreteStream";
        _concreteChute.AddChild(_concreteStream);
        _concreteStream.Visible = false;
        Tape.Version(this, 3);
        Tape.Part(_concreteChute, "concreteChute");
        Tape.Part(_concreteStream, "concreteStream");
        _t.ConcreteDragAt = ConcreteResistance;
    }

    static Vector2I ConcreteCell(Vector2 position) => new(Mathf.FloorToInt(position.X / 4f), Mathf.FloorToInt(position.Y / 4f));

    float ConcreteResistance(Vector2 position)
    {
        if (!_concreteGrid.TryGetValue(ConcreteCell(position), out var nearby)) return 0f;
        float resistance = 0f;
        foreach (var (a, b) in nearby)
        {
            float distance = position.DistanceTo(Geometry2D.GetClosestPointToSegment(position, a, b));
            resistance = Math.Max(resistance, 12f * (1f - Mathf.SmoothStep(.48f, 1.18f, distance)));
        }
        return resistance;
    }

    void AddConcreteResistance(Vector3 from, Vector3 to)
    {
        Vector2 a = new(from.X, from.Z), b = new(to.X, to.Z);
        var reach = Vector2.One * 1.18f;
        var min = ConcreteCell(a.Min(b) - reach);
        var max = ConcreteCell(a.Max(b) + reach);
        for (int z = min.Y; z <= max.Y; z++)
            for (int x = min.X; x <= max.X; x++)
            {
                Vector2I cell = new(x, z);
                if (!_concreteGrid.TryGetValue(cell, out var segments)) _concreteGrid[cell] = segments = new();
                segments.Add((a, b));
            }
    }

    /// <summary>The laps the mixer driver's revenge still has (3 when it starts, the last one with the concrete lying on the road: 1), 0 when it is not on.</summary>
    public int ConcreteLapsLeft => _concreteLapsLeft;

    public bool ConcreteRevengeLapDone()
    {
        if (_concreteLapsLeft <= 0) return false;
        if (--_concreteLapsLeft == 0)
        {
            ClearConcreteRevenge();
            return true;
        }
        if (_concreteLapsLeft == 1)
        {
            _concreteStream.Visible = false;
            _concreteChute.RotationDegrees = _restChute;
            _concreteTail = null;
        }
        return false;
    }

    internal static Node3D MakeConcretePile(Vector3 at, float yaw)
    {
        var pile = new Node3D { Name = "WetConcretePile", Position = at, Rotation = new Vector3(0, yaw, 0) };
        pile.AddChild(Props.Part(Props.Ball(.70f, 8), WetConcreteColor, new Vector3(0, .02f, 0)));
        ((Node3D)pile.GetChild(0)).Scale = new Vector3(1.48f, .30f, 1.1f);
        pile.AddChild(Props.Part(Props.Ball(.53f, 8), WetConcreteColor.Lightened(.14f), new Vector3(.29f, .03f, .20f)));
        ((Node3D)pile.GetChild(1)).Scale = new Vector3(1.2f, .23f, 1f);
        return pile;
    }

    // Keep the stream between the chute's actual lip and the terrain, including in a posed replay.
    public override void _Process(double delta)
    {
        if (_concreteStream?.Visible != true) return;
        Vector3 lip = _concreteChute.ToGlobal(new Vector3(0, .09f, .57f));
        Vector3 landing = ConcreteLanding(lip);
        Vector3 axis = lip - landing;
        if (axis.LengthSquared() < .001f) return;
        Vector3 up = axis.Normalized(), right = up.Cross(Vector3.Forward).Normalized();
        _fallingConcrete.GlobalTransform = new Transform3D(
            new Basis(right, axis, right.Cross(up)), (lip + landing) * .5f);
    }

    Vector3 ConcreteLanding(Vector3 lip)
    {
        var p = lip + _concreteChute.GlobalBasis.Z.Normalized() * .27f;
        return new Vector3(p.X, _t.MeshHeight(p.X, p.Z) + .045f, p.Z);
    }

    internal static Node3D MakeConcreteStrip(TrackData track, Vector3 from, Vector3 to, float startYaw, float endYaw)
    {
        var mesh = new SurfaceTool();
        mesh.Begin(Mesh.PrimitiveType.Triangles);
        var startSide = new Basis(Vector3.Up, startYaw).X;
        var endSide = new Basis(Vector3.Up, endYaw).X;
        Vector3 Edge(Vector3 centre, Vector3 side, float offset)
        {
            var p = centre + side * offset;
            p.Y = track.MeshHeight(p.X, p.Z) + .035f + .075f * (1f - Math.Abs(offset) / .62f);
            return p;
        }
        float[] offsets = { -.62f, -.34f, .14f, .62f };
        for (int i = 0; i < offsets.Length - 1; i++)
        {
            var a = Edge(from, startSide, offsets[i]);
            var b = Edge(from, startSide, offsets[i + 1]);
            var c = Edge(to, endSide, offsets[i + 1]);
            var d = Edge(to, endSide, offsets[i]);
            mesh.SetColor(i == 1 ? WetConcreteColor.Lightened(.12f) : WetConcreteColor);
            foreach (var v in new[] { a, b, c, a, c, d })
            {
                mesh.SetNormal(Vector3.Up);
                mesh.AddVertex(v);
            }
        }
        return new MeshInstance3D
        {
            Name = "WetConcreteStrip",
            Mesh = mesh.Commit(),
            MaterialOverride = new StandardMaterial3D
            {
                VertexColorUseAsAlbedo = true, Roughness = .46f,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            },
        };
    }

    void ClearConcreteRevenge()
    {
        if (_revengeMixer == null) return;
        foreach (var node in _concretePiles)
        {
            RemoveChild(node);
            node.QueueFree();
        }
        _concretePiles.Clear();
        _concreteGrid.Clear();
        _concreteLapsLeft = 0;
        ConcreteRevengePending = false;
        _unloadTime = -1f;
        _concreteTail = null;
        _concreteChute.RotationDegrees = _restChute;
        _concreteStream.Visible = false;
    }

    void ConcreteRevengeTick(float dt, CarSim car)
    {
        if (_revengeMixer is not { } mixer) return;
        if (ConcreteRevengePending && car != null)
        {
            Vector2 behind = mixer.Pos - new Vector2(car.Pos.X, car.Pos.Z);
            float gap = behind.Dot(mixer.Fwd);
            if (gap is > 24f and < 66f && behind.LengthSquared() < 68f * 68f)
            {
                ConcreteRevengePending = false;
                _unloadTime = 0f;
                _concreteLapsLeft = 3;
                _concreteTail = null;
                _concreteChute.AddChild(new ClangSound { Strength = .85f });
                mixer.Sound.Honk(.55f);
                ConcreteWarning?.Invoke();
            }
        }
        if (_concreteLapsLeft < 2) return;
        _unloadTime += dt;
        float down = Mathf.SmoothStep(0f, .55f, _unloadTime);
        _concreteChute.RotationDegrees = _restChute.Lerp(new Vector3(42f, 0f, 0f), down);
        _concreteStream.Visible = _unloadTime >= PourStart && mixer.Speed > .5f;
        if (_concreteStream.Visible)
        {
            var at = ConcreteLanding(_concreteChute.ToGlobal(new Vector3(0, .09f, .57f)));
            float yaw = TrackData.YawFor(mixer.Fwd);
            if (_concreteTail is not Vector3 from)
            {
                _concreteTail = at;
                _concreteTailYaw = yaw;
            }
            else if (from.DistanceSquaredTo(at) >= .65f * .65f)
            {
                // The concrete is not poured over a railway (its rails and ballast stay clear): no strip, no drag and no tape event for the stretch that touches it; the tail goes on.
                const float RailReach = 2.4f;
                if (_t.OnRailway(new Vector2(from.X, from.Z), RailReach) || _t.OnRailway(new Vector2(at.X, at.Z), RailReach) || _t.OnRailway(new Vector2((from.X + at.X) / 2f, (from.Z + at.Z) / 2f), RailReach))
                {
                    _concreteTail = at;
                    _concreteTailYaw = yaw;
                    return;
                }
                var strip = MakeConcreteStrip(_t, from, at, _concreteTailYaw, yaw);
                AddChild(strip);
                _concretePiles.Add(strip);
                AddConcreteResistance(from, at);
                ConcreteTape?.Fx(new TapeEvent(0f, TapeEventType.Effect, Kind: TapeFxKind.MixerConcreteStrip,
                    Pos: at, Dir: from, Param: _concreteTailYaw, Velocity: new Vector3(yaw, 0, 0), Life: float.MaxValue));
                _concreteTail = at;
                _concreteTailYaw = yaw;
            }
        }
    }

    public event Action ConcreteWarning;
}
