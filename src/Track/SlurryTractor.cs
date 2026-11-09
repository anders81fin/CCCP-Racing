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
    float _travel;      // metres driven: the wheels turn by it
    readonly System.Collections.Generic.List<DrunkTractor.TractorWheel> _wheels = new();
    float _angle, _clock, _renderClock, _depositDistance, _lateOff;
    Vector2 _pos, _trailer, _previousNozzle;
    bool _wasField;
    readonly RandomNumberGenerator _departureRng = new() { Seed = 1985 };
    BarnDoors _barnDoor;
    Vector2 _barnCenter;
    float _departureDelay, _doorOpen;
    int _departureStage = 4; // 0 wait, 1 open, 2 leave, 3 reach field, 4 spread, 5 revenge: drive to the road, 6 revenge: spread along the road
    // The driver's grandmother (a babushka the car has run over): the tractor leaves its fields, follows the road in the lap's direction spreading slurry for RevengeLaps laps.
    public const int RevengeLaps = 3;
    float _roadAt, _roadLat = RoadLane, _blockedFor;
    /// <summary>The revenge drives in its own lane, on the right of its direction: the bus (which drives the other way, on its right) passes it instead of meeting it nose to nose on the centre line. The share of the road's half width.</summary>
    const float RoadLane = 0.45f, VergeLane = 0.8f;
    Vector2 RoadPoint(Vector2 centre, int i, float lat) { Vector2 d = _track.CenterDir(i); return centre + new Vector2(-d.Y, d.X) * (_track.HalfWidth[i] * lat); }
    int _revengeLapsLeft;
    Vector2 _roadTarget;
    /// <summary>The revenge is on: the tractor is on its way to the road or spreading along it.</summary>
    public bool Avenging => _departureStage is 5 or 6;
    /// <summary>The tractor's revenge laps still to be driven by the car.</summary>
    public int RevengeLapsLeft => _revengeLapsLeft;
    /// <summary>The revenge is over: the tractor drives back to its fields.</summary>
    public event Action RevengeOver;
    /// <summary>The car finished a lap of the revenge and laps are left (how many).</summary>
    public event Action<int> RevengeLapPassed;
    /// <summary>The tractor starts on the road (the spreading itself comes with the first tick).</summary>
    public event Action RevengeOnTheRoad;
    public Node3D Model => _tractor;
    public Vector2 Position2D => _pos;
    public bool Spraying { get; private set; }
    public SlurryTrail Trail { get; } = new();
    /// <summary>The world tape's host for the patches this tractor lays.</summary>
    public ITapeFxHost TapeFx;
    int _tapeSpray;
    /// <summary>The title replay's tape shows the tractor, its spray and its patches: it lays and moves none of its own.</summary>
    public bool TapeDriven
    {
        get => _tapeDriven;
        set { if (_tapeDriven == value) return; _tapeDriven = value; if (value) { Trail.Clear(); _trailMesh?.ClearSurfaces(); Spraying = false; _spray.Visible = false; _tapeSpray = 0; } }
    }
    bool _tapeDriven;
    /// <summary>The replay lays a patch the tape recorded (the sink's effect, restored by age on a seek).</summary>
    public void ReplayPatch(Vector2 at, bool revenge, float age) => Trail.DepositAged(at, revenge, age);
    /// <summary>A seek or the end of the replay: the patches the tape laid go.</summary>
    public void ReplayClear() { Trail.Clear(); _trailMesh?.ClearSurfaces(); }
    public bool HasDeparted => _departureStage == 4;
    public bool CrossingBarnRoad => _departureStage == 2 || (_departureStage == 3 && _pos.Y < 62f);

    /// <summary>The driver's grandmother has been run over: the tractor drives to the road and spreads slurry along it for <see cref="RevengeLaps"/> laps. False if it is already on it or still in the barn.</summary>
    public bool StartRevenge()
    {
        if (_departureStage != 4) return false;
        _revengeLapsLeft = RevengeLaps;
        int k = _track.NearestCenter(_pos, out _);
        _roadAt = k;
        _roadTarget = RoadPoint(_track.Center[k], k, RoadLane);
        _roadLat = RoadLane; _blockedFor = 0f;
        _departureStage = 5;
        return true;
    }

    /// <summary>The car finished a lap: the third one ends the revenge.</summary>
    public void RevengeLapDone()
    {
        if (!Avenging || _revengeLapsLeft <= 0) return;
        if (--_revengeLapsLeft > 0) { RevengeLapPassed?.Invoke(_revengeLapsLeft); return; }
        _departureStage = 3;      // back to the fields (the departure's last leg)
        Spraying = false; _spray.Visible = false; _depositDistance = 0.8f;
        RevengeOver?.Invoke();
    }
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
        DrunkTractor.BuildMtAgro(_tractor, false, out _, out _head, new Color(0.30f, 0.39f, 0.22f), true, _wheels);
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
        Tape.Version(this, 3); Tape.Part(this, "root"); Tape.Part(_tractor, "tractor"); Tape.Part(_head, "head"); Tape.Part(_tank, "tank");
        DrunkTractor.TapeWheels(_wheels, () => _travel);      // (version 3: the four wheels turn, their angles are spinners)
        // Version 2: the spray (0 off, 1 spreading, 2 spreading in the revenge) is a sound channel; each patch is a 'slurry-patch' effect event (TapeFx), so the replay shows both.
        Tape.Sound(_tractor, TapeSoundChannel.SlurrySpray, () => Spraying ? (Avenging ? 2f : 1f) : 0f, v => _tapeSpray = (int)MathF.Round(v));
        Tape.Sound(_tractor, TapeSoundChannel.DieselSpeed, () => _sound.Speed, v => _sound.Speed = v);
        Reset();
    }

    public void Reset()
    {
        _angle = -0.6f; _clock = _renderClock = _depositDistance = _lateOff = _travel = 0;
        _revengeLapsLeft = 0;
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
        if (_tapeDriven)
        {
            _clock += dt;
            Trail.Tick(dt);
            Spraying = _tapeSpray > 0;
            SprayStep(dt, _tapeSpray == 2, deposit: false);
            return;
        }
        if (Avenging) { RevengeTick(dt, raceActive, bus); return; }
        if (_departureStage != 4)
        {
            if (!raceActive) return;
            Trail.Tick(dt);      // (the revenge's strip keeps drying while the tractor drives home)
            _renderClock += dt;
            if (_renderClock >= 0.1f) { _renderClock %= 0.1f; DrawTrail(); }
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
        SprayStep(dt, false);
    }

    /// <summary>The spray's drops, a patch under the nozzle every 0.8 m (every metre, bigger and longer lasting, in the revenge) and the trail's drawing.</summary>
    void SprayStep(float dt, bool revenge, bool deposit = true)
    {
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
        if (deposit && Spraying && _depositDistance >= (revenge ? 1.0f : 0.8f))
        {
            _depositDistance = 0;
            var surface = _track.TileSurface((int)(nozzle.X / _track.Tile), (int)(nozzle.Y / _track.Tile));
            if (surface is not (SurfaceType.Water or SurfaceType.Shallow))
            {
                Trail.Deposit(nozzle, revenge);
                TapeFx?.Fx(new TapeEvent(0f, TapeEventType.Effect, Kind: TapeFxKind.SlurryPatch, Effect: TapeEffect.Smoke, Pos: new Vector3(nozzle.X, 0, nozzle.Y), Param: revenge ? 1f : 0f,
                    Life: revenge ? SlurryTrail.RevengeLifetime : SlurryTrail.Lifetime));
            }
        }
        if (!Spraying) _depositDistance = 0.8f;
        _renderClock += dt;
        if (_renderClock >= 0.1f) { _renderClock %= 0.1f; DrawTrail(); }
    }


    /// <summary>The revenge: on the way to the road (5), then along the centre line in the lap's direction, spreading all the time (6).</summary>
    void RevengeTick(float dt, bool raceActive, SovietBus bus)
    {
        if (!raceActive) return;
        _clock += dt;
        Trail.Tick(dt);
        Vector2 forward; float speed;
        if (_departureStage == 5)
        {
            forward = (_roadTarget - _pos).Normalized();
            speed = 4.1f;
            Move(forward, speed, dt, bus, ref speed);
            if (_pos.DistanceTo(_roadTarget) < 1.5f) { _departureStage = 6; RevengeOnTheRoad?.Invoke(); }
            Place(speed, forward);
            Spraying = false;
            SprayStep(dt, true);
        }
        else
        {
            int n = _track.Center.Length;
            int i0 = (int)_roadAt % n, i1 = (i0 + 1) % n;
            Vector2 a = _track.Center[i0], b = _track.Center[i1];
            forward = (b - a).Normalized();
            speed = 3.2f;
            float step = speed * dt / Math.Max(0.2f, a.DistanceTo(b));
            float nextAt = (_roadAt + step) % n;
            int j0 = (int)nextAt % n, j1 = (j0 + 1) % n;
            Vector2 nextPos = RoadPoint(_track.Center[j0].Lerp(_track.Center[j1], nextAt - (int)nextAt), j0, _roadLat);
            Vector2 hitch = nextPos - forward * 1.9f;
            Vector2 tow = _trailer - hitch;
            Vector2 nextTrailer = hitch + (tow.LengthSquared() > 0.001f ? tow.Normalized() : -forward) * (TowLength - 1.9f);
            bool blocked = bus?.Blocks(nextPos, 1.3f) == true || bus?.Blocks(nextTrailer, 1.5f) == true
                || GulagTruck.BlocksAt(nextPos, 1.3f) || GulagTruck.BlocksAt(nextTrailer, 1.5f);
            if (!blocked) { _roadAt = nextAt; _pos = nextPos; _trailer = nextTrailer; _blockedFor = 0f; }
            else { speed = 0; _blockedFor += dt; }
            // Held up for a while (a bus or a lorry in the way): pull over to the verge so the road is free, and back to the lane when it moves on.
            _roadLat = Mathf.MoveToward(_roadLat, _blockedFor > 3f ? VergeLane : RoadLane, 0.25f * dt);
            if (speed == 0)
            {
                // Standing: the sideways move still happens (where it is free), so the pull-over is smooth and not a jump when it moves on.
                Vector2 stay = RoadPoint(_track.Center[i0].Lerp(_track.Center[i1], _roadAt - (int)_roadAt), i0, _roadLat);
                Vector2 stayHitch = stay - forward * 1.9f, stayTow = _trailer - stayHitch;
                Vector2 stayTrailer = stayHitch + (stayTow.LengthSquared() > 0.001f ? stayTow.Normalized() : -forward) * (TowLength - 1.9f);
                if (bus?.Blocks(stay, 1.3f) != true && bus?.Blocks(stayTrailer, 1.5f) != true && !GulagTruck.BlocksAt(stay, 1.3f) && !GulagTruck.BlocksAt(stayTrailer, 1.5f)) { _pos = stay; _trailer = stayTrailer; }
            }
            Place(speed, forward);
            Spraying = speed > 0;
            SprayStep(dt, true);
        }
    }

    /// <summary>A straight move toward a target (the way to the road), held back by a bus or the lorry in the way.</summary>
    void Move(Vector2 forward, float wanted, float dt, SovietBus bus, ref float speed)
    {
        Vector2 next = _pos + forward * wanted * dt;
        Vector2 hitch = next - forward * 1.9f;
        Vector2 tow = _trailer - hitch;
        Vector2 nextTrailer = hitch + (tow.LengthSquared() > 0.001f ? tow.Normalized() : -forward) * (TowLength - 1.9f);
        if (bus?.Blocks(next, 1.3f) == true || bus?.Blocks(nextTrailer, 1.5f) == true || GulagTruck.BlocksAt(next, 1.3f) || GulagTruck.BlocksAt(nextTrailer, 1.5f)) { speed = 0; return; }
        _pos = next; _trailer = nextTrailer;
    }

    Vector2 Nozzle() => _trailer + (_trailer - _pos).Normalized() * 3.1f;
    void Place(float speed, Vector2? travel = null)
    {
        _travel += speed * _sootDt;
        DrunkTractor.RollWheels(_wheels, _travel);
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
                    float radius = patch.Radius * (0.94f + 0.06f * MathF.Sin(j * 2.3f + patch.Born));
                    return patch.Center + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius;
                }
                st.SetColor(color); st.SetNormal(Vector3.Up);
                st.AddVertex(GroundPoint(patch.Center)); st.AddVertex(GroundPoint(Rim(i + 1))); st.AddVertex(GroundPoint(Rim(i)));
            }
        }
        st.Commit(_trailMesh);
    }
}
