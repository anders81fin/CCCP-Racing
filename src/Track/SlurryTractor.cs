using System;
using CCCPRacing.Race;
using Godot;
using CCCPRacing.Audio;

namespace CCCPRacing.Track;

/// <summary>A kolkhoz tanker working the two fields across the S-bend. One bounded trail mesh.</summary>
public partial class SlurryTractor : Node3D
{
    const float Speed = 2.8f, TowLength = 4.5f;
    TrackData _track;
    Node3D _tractor, _tank, _head;
    DieselSound _sound;
    ArrayMesh _trailMesh;
    MultiMeshInstance3D _spray;
    int _bodySlot, _tankSlot;
    float _angle, _clock, _renderClock, _depositDistance, _lateOff;
    Vector2 _pos, _trailer, _previousNozzle;
    bool _wasField;
    readonly RandomNumberGenerator _departureRng = new() { Seed = 1985 };
    BarnDoors _barnDoor;
    Vector2 _barnCenter;
    float _departureDelay, _doorOpen;
    int _departureStage = 4; // 0 wait, 1 open, 2 leave, 3 reach field, 4 spread
    public Node3D Model => _tractor;
    public Vector2 Position2D => _pos;
    public bool Spraying { get; private set; }
    public SlurryTrail Trail { get; } = new();
    public bool HasDeparted => _departureStage == 4;
    public bool CrossingBarnRoad => _departureStage == 2 || (_departureStage == 3 && _pos.Y < 62f);
    public int BodyObstacle => _bodySlot;
    public int TankObstacle => _tankSlot;

    public void ConfigureDeparture(BarnDoors door, Vector2 barnCenter)
    {
        _barnDoor = door;
        _barnCenter = barnCenter;
        Reset();
    }

    // Elliptical farm circuit: wide turns and two crossings of the field S-bend.
    public static Vector2 Route(float angle) => new(168f + 32f * MathF.Cos(angle), 222f + 20f * MathF.Sin(angle));
    public static bool NearRoute(Vector2 p, float margin)
    {
        for (int i = 0; i < 96; i++)
            if (p.DistanceSquaredTo(Route(i * MathF.Tau / 96)) < margin * margin) return true;
        return false;
    }

    public void Init(TrackData t)
    {
        _track = t;
        t.Slurry = Trail;
        _bodySlot = t.Obstacles.Count;
        t.Obstacles.Add(new Obstacle(Vector2.Zero, 1.3f));
        _tankSlot = t.Obstacles.Count;
        t.Obstacles.Add(new Obstacle(Vector2.Zero, 1.5f));
        _tractor = new Node3D { Name = "GreenMTAgro" };
        AddChild(_tractor);
        DrunkTractor.BuildMtAgro(_tractor, false, out _, out _head, new Color(0.30f, 0.39f, 0.22f), true);
        // A pale replacement roof distinguishes this working tractor from the blue plough tractor.
        _tractor.AddChild(Props.Part(Props.Box(1.60f, 0.08f, 1.50f), new Color(0.70f, 0.69f, 0.56f), new Vector3(0, 3.39f, 0.8f)));
        _sound = new DieselSound { Loudness = -1f };
        _tractor.AddChild(_sound);
        _tank = SlurryTanker.Build();
        AddChild(_tank);
        _trailMesh = new ArrayMesh();
        AddChild(new MeshInstance3D
        {
            Name = "SlurryPatches", Mesh = _trailMesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D
            {
                VertexColorUseAsAlbedo = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel,
                Roughness = 0.20f, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            },
        });
        _spray = new MultiMeshInstance3D
        {
            Name = "SlurryFan",
            Multimesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                Mesh = Props.Box(0.10f, 0.08f, 0.20f), InstanceCount = 24,
            },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.30f, 0.22f, 0.09f), Roughness = 0.3f },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        _tank.AddChild(_spray);
        // The world tape: tractor, driver's head and tanker. (The spray's drops are redrawn each tick and the trail is live state: not recorded.)
        Tape.Version(this, 1); Tape.Part(this, "root"); Tape.Part(_tractor, "tractor"); Tape.Part(_head, "head"); Tape.Part(_tank, "tank");
        Tape.Sound(_tractor, TapeSoundChannel.DieselSpeed, () => _sound.Speed, v => _sound.Speed = v);
        Reset();
    }

    public void Reset()
    {
        _angle = -0.6f; _clock = _renderClock = _depositDistance = _lateOff = 0;
        _pos = Route(_angle);
        Vector2 forward = new(-32f * MathF.Sin(_angle), 20f * MathF.Cos(_angle));
        _trailer = _pos - forward.Normalized() * TowLength;
        _departureStage = _barnDoor == null ? 4 : 0;
        _departureDelay = _departureRng.RandfRange(46f, 63f);
        _doorOpen = 0;
        _barnDoor?.SetOpen(0);
        if (_barnDoor != null)
        {
            _pos = _barnCenter;
            _trailer = _pos - Vector2.Down * TowLength;
        }
        Trail.Clear(); _trailMesh.ClearSurfaces();
        _wasField = false; Spraying = false; _spray.Visible = false;
        Place(0, _barnDoor == null ? null : Vector2.Down);
        _previousNozzle = Nozzle();
    }

    /// <summary>The effects (the player's diesel smoke from the stack); null: none.</summary>
    public CCCPRacing.Car.TyreEffects Fx;
    float _soot, _sootDt;

    public void Tick(float dt, bool raceActive = true, SovietBus bus = null)
    {
        _sootDt = dt;
        if (_departureStage != 4)
        {
            if (!raceActive) return;
            if (_departureStage == 0)
            {
                _departureDelay -= dt;
                if (_departureDelay > 0) return;
                _departureStage = 1;
            }
            if (_departureStage == 1)
            {
                _doorOpen = Mathf.MoveToward(_doorOpen, 1f, dt / 2f);
                _barnDoor.SetOpen(_doorOpen);
                if (_doorOpen < 1f) return;
                if (bus != null && bus.Position2D.DistanceTo(new Vector2(_barnCenter.X, 50f)) < 18f) return;
                _departureStage = 2;
            }
            Vector2 forward = _departureStage == 2 ? Vector2.Down : (Route(-0.6f) - _pos).Normalized();
            float speed = _departureStage == 2 ? 2.9f : 4.1f;
            Vector2 next = _pos + forward * speed * dt;
            Vector2 departureHitch = next - forward * 1.9f;
            Vector2 tow = _trailer - departureHitch;
            Vector2 departureTrailer = departureHitch + (tow.LengthSquared() > 0.001f ? tow.Normalized() : -forward) * (TowLength - 1.9f);
            if (bus?.Blocks(next, 1.3f) == true || bus?.Blocks(departureTrailer, 1.5f) == true
                || GulagTruck.BlocksAt(next, 1.3f) || GulagTruck.BlocksAt(departureTrailer, 1.5f))
                speed = 0;
            else { _pos = next; _trailer = departureTrailer; }
            Place(speed, forward);
            if (_departureStage == 2 && _pos.Y >= _barnCenter.Y + 22f) _departureStage = 3;
            if (_departureStage == 3 && _pos.DistanceTo(Route(-0.6f)) < 1.5f)
            {
                _pos = Route(-0.6f);
                _angle = -0.6f;
                _departureStage = 4;
                _previousNozzle = Nozzle();
            }
            return;
        }
        _clock += dt;
        Trail.Tick(dt);
        float tangentLength = new Vector2(32f * MathF.Sin(_angle), 20f * MathF.Cos(_angle)).Length();
        float nextAngle = (_angle + Speed * dt / tangentLength) % MathF.Tau;
        Vector2 nextPos = Route(nextAngle);
        Vector2 hitch = nextPos - new Vector2(-32f * MathF.Sin(nextAngle), 20f * MathF.Cos(nextAngle)).Normalized() * 1.9f;
        Vector2 nextTrailer = hitch + (_trailer - hitch).Normalized() * (TowLength - 1.9f);
        bool blocked = bus?.Blocks(nextPos, 1.3f) == true || bus?.Blocks(nextTrailer, 1.5f) == true
            || GulagTruck.BlocksAt(nextPos, 1.3f) || GulagTruck.BlocksAt(nextTrailer, 1.5f);
        if (!blocked) { _angle = nextAngle; _pos = nextPos; _trailer = nextTrailer; }
        Place(blocked ? 0 : Speed);
        int ci = _track.NearestCenter(_pos, out float roadDistance);
        bool field = roadDistance > _track.HalfWidth[ci] + 2f;
        if (_wasField && !field) _lateOff = 5f; // Careless driver closes the valve late.
        _lateOff = Math.Max(0, _lateOff - dt);
        _wasField = field;
        // Short runs of spreading; the late shutoff leaves a readable tail on the road.
        Spraying = (field && _clock % 14f < 5f) || _lateOff > 0;
        _spray.Visible = Spraying;
        if (Spraying)
            for (int i = 0; i < _spray.Multimesh.InstanceCount; i++)
            {
                float flight = (_clock * 1.8f + i * 0.137f) % 1f;
                float side = (i % 8 - 3.5f) / 3.5f;
                var drop = new Vector3(side * flight * 2f, 0.70f + 0.4f * flight - 1.05f * flight * flight, 2.05f + flight * 1.8f);
                _spray.Multimesh.SetInstanceTransform(i, new Transform3D(Basis.Identity, drop));
            }
        Vector2 nozzle = Nozzle();
        _depositDistance += nozzle.DistanceTo(_previousNozzle);
        _previousNozzle = nozzle;
        if (Spraying && _depositDistance >= 0.8f)
        {
            _depositDistance = 0;
            var surface = _track.TileSurface((int)(nozzle.X / _track.Tile), (int)(nozzle.Y / _track.Tile));
            if (surface is not (SurfaceType.Water or SurfaceType.Shallow)) Trail.Deposit(nozzle);
        }
        if (!Spraying) _depositDistance = 0.8f;
        _renderClock += dt;
        if (_renderClock >= 0.1f) { _renderClock %= 0.1f; DrawTrail(); }
    }

    Vector2 Nozzle() => _trailer + (_trailer - _pos).Normalized() * 3.1f;
    void Place(float speed, Vector2? travel = null)
    {
        Vector2 forward = travel ?? new Vector2(-32f * MathF.Sin(_angle), 20f * MathF.Cos(_angle));
        Ground(_tractor, _pos, forward);
        Ground(_tank, _trailer, _pos - _trailer);
        _head.Rotation = new Vector3(0, 0.10f * MathF.Sin(_clock), 0);
        _track.Obstacles[_bodySlot] = new Obstacle(_pos, 1.3f, forward.Normalized() * speed, Material: ImpactMaterial.Metal);
        _track.Obstacles[_tankSlot] = new Obstacle(_trailer, 1.5f, forward.Normalized() * speed, Material: ImpactMaterial.Metal);
        _sound.Speed = speed;
        if (Fx != null && _sootDt > 0)
        {
            Vector3 pipe = _tractor.GlobalTransform * new Vector3(0.4f, 2.95f, -1.2f);
            Vector3 fwd = -_tractor.GlobalTransform.Basis.Z.Normalized();
            Fx.EngineSoot(pipe, -fwd, fwd * speed, Mathf.Clamp(speed / 2.8f, 0.25f, 1f), 1.2f, ref _soot, _sootDt);      // the player's diesel smoke, as the other tractors'
        }
    }
    void Ground(Node3D node, Vector2 p, Vector2 forward)
    {
        float y = _track.MeshHeight(p.X, p.Y, out var normal);
        Vector3 f = new(forward.X, 0, forward.Y);
        f = (f - normal * f.Dot(normal)).Normalized();
        node.Transform = new Transform3D(Basis.LookingAt(f, normal), new Vector3(p.X, y, p.Y));
    }

    void DrawTrail()
    {
        _trailMesh.ClearSurfaces();
        if (Trail.Patches.Count == 0) return;
        var st = new SurfaceTool(); st.Begin(Mesh.PrimitiveType.Triangles);
        Vector3 GroundPoint(Vector2 p) => new(p.X, _track.Sample(p.X, p.Y).Height + 0.035f, p.Y);
        foreach (var patch in Trail.Patches)
        {
            var color = new Color(0.29f, 0.21f, 0.09f, Trail.Strength(patch) * 0.82f).SrgbToLinear();
            for (int i = 0; i < 12; i++)
            {
                Vector2 Rim(int j)
                {
                    float a = j * MathF.Tau / 12f;
                    float radius = SlurryTrail.Radius * (0.94f + 0.06f * MathF.Sin(j * 2.3f + patch.Born));
                    return patch.Center + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius;
                }
                st.SetColor(color); st.SetNormal(Vector3.Up);
                st.AddVertex(GroundPoint(patch.Center)); st.AddVertex(GroundPoint(Rim(i + 1))); st.AddVertex(GroundPoint(Rim(i)));
            }
        }
        st.Commit(_trailMesh);
    }
}
