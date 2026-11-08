using System;
using System.Collections.Generic;
using CCCPRacing.Audio;
using CCCPRacing.Car;
using CCCPRacing.Race;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Track;

/// <summary>
/// Two 1980s Soviet main battle tanks on a live-fire exercise, one on each side of the road:
/// drive to a firing position, stop, lay the gun, fire one or two rounds from standstill,
/// then relocate (sometimes backing out first). Legendary inaccuracy, mostly in range: short
/// rounds land on and around the road between them, so the danger is driving through the line
/// of fire. Dirt fountains, small craters, a shove for a Pyatyorka that happens to be nearby. A
/// direct hit only makes sparks: Soviet steel. Tanks also cross the road between firing positions, clearing the carriageway before stopping.
/// </summary>
public partial class TankBattle : Node3D
{
    public BonusExerciseControl Exercise;
    const float CruiseMin = 5f, CruiseMax = 8f;   // 18-29 km/h between firing positions
    const float Accel = 1.1f, Brake = 1.5f;       // m/s², 41 tonnes
    const float ReverseSpeed = 2.2f;
    const float PivotRate = 0.3f;                 // rad/s, turning on the spot
    const float DriveTurnRate = 0.2f;             // rad/s, steering under way
    const float TraverseRate = 0.42f;             // turret, rad/s (24°/s)
    const float ReloadMin = 7f, ReloadMax = 10f;
    const float SpeedRef = 8f;                    // dust, squat
    const float ShellSpeed = 95f;
    const float RoadKeepOff = 9f;                 // hull centre stays this far beyond the road edge (ditch + half a hull)
    const float PathClear = 3f;                   // straight legs keep this clear of trees, poles and walls
    const int ObstacleSlotsPerTank = 3;

    enum Phase { Stand, Drive, Reverse }

    class Tank
    {
        public Node3D Hull, Turret, Barrel;
        public readonly List<Node3D> Wheels=new();
        public readonly List<Node3D> Belts=new();
        public float Soot, PreviousSpeed, PreviousYaw, PitchMotion, RollMotion, MotionTime;
        public float LeftTravel,RightTravel,TraverseVelocity,FlashLeft;
        public MeshInstance3D Flash;
        public DieselSound Sound;
        public Vector2 Pos;
        public float Yaw, TurretYaw, Speed, ReloadLeft, Recoil;
        public int ObsBase;
        public int Side;                  // 0: inside the road's U, 1: outside it
        public Phase Phase;
        public int At = -1;               // firing position it holds or heads for
        public readonly List<int> Route = new();
        public Vector2 LegStart;          // where the current straight leg began
        public float Cruise, Settle, Linger, ReverseLeft, Stuck;
        public bool Crossing, AcrossRoad;
        public Vector2 CrossHome, CrossGoal;
        public float MoveAfter, CrossingCooldown;
        public int Rounds;                // still to fire from this position
        public float AimErr;              // turret error to the target, rad
    }

    class Shell
    {
        public Vector3 Pos, Vel;
        public Vector3 Origin, Vel0;      // the world tape: where and how it left the gun
        public float Life;
        public Tank Owner;
        public bool Chase;            // fired at the car (revenge)
        public int No;
    }

    TrackData _t;
    TyreEffects _fx;
    Rect2 _area;
    bool _encounterUsed;
    readonly List<Tank> _tanks = new();
    readonly List<Shell> _shells = new();
    readonly List<(Vector2 p, float r)> _avoid = new();
    readonly RandomNumberGenerator _rng = new() { Seed = 1983 };
    Node3D _craters;
    // Firing positions per side and which pairs a straight leg joins (clear of the road and trees).
    readonly List<Vector2>[] _spots = { new(), new() };
    bool[][,] _legs;

    /// <summary>Test (--shell-tree): asked (shooter, target tank) at each round; a point sends that round there.</summary>
    public Func<Vector2, Vector2, Vector2?> TestAim;

    /// <summary>Collision circles of the hulls (indices into TrackData.Obstacles): they push trees over.</summary>
    public IEnumerable<int> HullObstacles
    {
        get
        {
            foreach (var k in _tanks)
                for (int s = 0; s < ObstacleSlotsPerTank; s++) yield return k.ObsBase + s;
        }
    }

    /// <summary>(impact point, distance to the car) for every shell landing, and direct hits.</summary>
    public event Action<Vector3, float> ShellLanded;
    /// <summary>The world tape's view of the effects made here (null: nothing records). Set by the race.</summary>
    public ITapeFxHost TapeFx;

    public void Init(TrackData t, TyreEffects fx, Rect2 area, int count)
    {
        foreach (var _ in InitSteps(t, fx, area, count)) { }
    }

    // Same initialization order and tape identities; only the loading caller yields frames.
    public IEnumerable<int> InitSteps(TrackData t, TyreEffects fx, Rect2 area, int count)
    {
        _t = t;
        _fx = fx;
        _area = area;
        // Buildings get a wide berth, trees/fences/poles a narrow one (they're in the table as small circles).
        foreach (var o in t.Obstacles)
            if (o.Radius > 0.15f && area.Grow(20f).HasPoint(o.Center)) _avoid.Add((o.Center, o.Radius));
        InitSpots(t.TankRange);
        yield return 0;
        _craters = new Node3D { Name = "ShellCraters" };
        AddChild(_craters);
        Tape.Version(this, 1); Tape.Part(this, "root");
        string[] numbers = { "217", "342", "105", "631" };
        for (int i = 0; i < count; i++)
        {
            var tank = new Tank { ObsBase = t.Obstacles.Count, Side = i % 2 };
            for (int k = 0; k < ObstacleSlotsPerTank; k++) t.Obstacles.Add(new Obstacle(new Vector2(-1e4f, -1e4f), 0.1f));
            Build(tank, numbers[i % numbers.Length]);
            MarkTape(tank, i + 1);
            _tanks.Add(tank);
            yield return 0;
        }
        Reset();
        yield return 0;
    }

    /// <summary>Firing positions from the track file (positionsA inside the U, positionsB outside);
    /// without them, three spots down each half of the range.</summary>
    void InitSpots(TankRangeDef def)
    {
        for (int s = 0; s < 2; s++)
        {
            var list = s == 0 ? def?.PositionsA : def?.PositionsB;
            if (list != null)
                foreach (var p in list) _spots[s].Add(new Vector2(p[0], p[1]));
            if (_spots[s].Count == 0)
                for (int i = 0; i < 3; i++)
                    _spots[s].Add(new Vector2(Mathf.Lerp(_area.Position.X, _area.End.X, s == 0 ? 0.2f : 0.8f),
                                              Mathf.Lerp(_area.Position.Y, _area.End.Y, 0.25f + i * 0.25f)));
        }
        _legs = new bool[2][,];
        for (int s = 0; s < 2; s++)
        {
            int n = _spots[s].Count;
            _legs[s] = new bool[n, n];
            for (int a = 0; a < n; a++)
            {
                if (RoadEdge(_spots[s][a]) < RoadKeepOff + 3f) GD.PushWarning($"tank position {_spots[s][a]} is close to the road");
                for (int b = a + 1; b < n; b++)
                    _legs[s][a, b] = _legs[s][b, a] = LegClear(_spots[s][a], _spots[s][b]);
            }
        }
    }

    public void Reset()
    {
        foreach (var c in _craters.GetChildren()) c.QueueFree();
        _shells.Clear();
        _chasing = false;
        _shotsLeft = _shellsOut = 0;
        _revengeCd = 0;
        _encounterUsed = false;
        for (int i = 0; i < _tanks.Count; i++)
        {
            var k = _tanks[i];
            var spots = _spots[k.Side];
            k.At = i < 2 ? PickSpot(k, -1, null) : _rng.RandiRange(0, spots.Count - 1);
            k.Route.Clear();
            k.Crossing = k.AcrossRoad = false;
            k.CrossingCooldown = 0;
            k.Pos = k.LegStart = spots[k.At];
            var foe = Foe(k);
            k.Yaw = TrackData.YawFor((foe != null ? foe.Pos : _area.GetCenter()) - k.Pos) + _rng.RandfRange(-0.4f, 0.4f);
            k.TurretYaw = 0;
            k.Speed = k.PreviousSpeed = k.LeftTravel = k.RightTravel = k.PitchMotion = k.RollMotion = k.Recoil = k.FlashLeft = k.TraverseVelocity = 0;
            k.PreviousYaw=k.Yaw;
            k.Barrel.Rotation=Vector3.Zero;
            k.ReloadLeft = _rng.RandfRange(2f, 6f);
            Hold(k);
            k.Settle = 0;
            Place(k, 0);
        }
        _stat = new();
    }

    Tank Foe(Tank k)
    {
        foreach (var o in _tanks)
            if (o != k && o.Side != k.Side && o.At >= 0) return o;
        return null;
    }

    /// <summary>Where the other side's tank is, or is heading.</summary>
    Vector2? FoeSpot(Tank k)
    {
        var o = Foe(k);
        if (o == null) return null;
        int goal = o.Route.Count > 0 ? o.Route[^1] : o.At;
        return _spots[o.Side][goal];
    }

    /// <summary>Next firing position: another one of its own side, preferring 90-130 m to the enemy
    /// and one a straight leg from here reaches.</summary>
    int PickSpot(Tank k, int current, Vector2? from)
    {
        var spots = _spots[k.Side];
        var foe = FoeSpot(k);
        int best = current;
        float bestScore = float.MaxValue;
        for (int i = 0; i < spots.Count; i++)
        {
            if (i == current) continue;
            float score = _rng.RandfRange(0f, 12f);
            if (foe is Vector2 f)
            {
                float d = spots[i].DistanceTo(f);
                score += d < 90f ? 90f - d : d > 130f ? d - 130f : 0f;
            }
            if (from is Vector2 a && !LegClear(a, spots[i])) score += 1000f;
            if (score < bestScore) { bestScore = score; best = i; }
        }
        return best < 0 ? 0 : best;
    }

    // ------------------------------------------------------------ model

    void Build(Tank k, string number)
    {
        var olive = new Color(0.32f, 0.36f, 0.22f);
        var dark = new Color(0.12f, 0.12f, 0.11f);
        var steel = new Color(0.28f, 0.3f, 0.2f);
        k.Hull = new Node3D { Name = "T-72" };
        AddChild(k.Hull);
        // Hull: low slab with a sloped glacis at the front (-Z).
        k.Hull.AddChild(Props.Part(Props.Box(3.4f, 0.9f, 6.6f), olive, new Vector3(0, 1.0f, 0.2f)));
        k.Hull.AddChild(Props.Part(Props.Box(3.4f, 0.5f, 1.4f), olive * 0.92f, new Vector3(0, 0.95f, -3.4f), new Vector3(-28, 0, 0)));
        // Tracks, road wheels and side skirts.
        foreach (float s in new[] { -1f, 1f })
        {
            k.Hull.AddChild(Props.Part(Props.Box(0.7f, 0.8f, 6.9f), dark, new Vector3(s * 1.55f, 0.45f, 0)));
        }
        // Fuel drums on the back deck.
        foreach (float s in new[] { -0.6f, 0.6f })
            k.Hull.AddChild(Props.Part(Props.Cyl(0.3f, 0.9f, 8), new Color(0.25f, 0.28f, 0.18f), new Vector3(s, 1.3f, 3.5f), new Vector3(0, 0, 90)));

        // Turret: squashed dome, cupola, long 125 mm gun.
        k.Turret = new Node3D { Position = new Vector3(0, 1.45f, 0.1f) };
        k.Hull.AddChild(k.Turret);
        var dome = Props.Part(Props.Ball(1.35f, 10), olive * 1.05f, new Vector3(0, 0.25f, 0.2f));
        dome.Scale = new Vector3(1f, 0.42f, 1.05f);
        k.Turret.AddChild(dome);
        k.Barrel = new Node3D { Position = new Vector3(0, 0.3f, -1.1f) };
        k.Turret.AddChild(k.Barrel);
        k.Barrel.AddChild(Props.Part(Props.Cyl(0.11f, 5.2f, 6), olive * 0.9f, new Vector3(0, 0, -2.6f), new Vector3(90, 0, 0)));
        k.Barrel.AddChild(Props.Part(Props.Cyl(0.17f, 1.2f, 6), olive * 0.8f, new Vector3(0, 0, -2.2f), new Vector3(90, 0, 0)));  // thermal sleeve
        BuildDetails(k, olive, dark, steel);
        // Three opaque surfaces per tank: hull, rotating turret and recoiling gun.
        // Tactical glyphs are added afterwards as opaque unshaded geometry.
        DrunkTractor.BatchStatic(k.Barrel, null);
        DrunkTractor.BatchStatic(k.Turret, k.Barrel);
        DrunkTractor.BatchStatic(k.Hull, k.Turret);
        BuildRunningGear(k,olive,dark,steel);
        MilitaryLights.Vehicle(k.Hull,new(0,1.22f,-4.08f),new(0,1.25f,3.55f),1.1f,28);
        // Solid glyph pixels avoid the small alpha-scissor texture's corrupted
        // fragments at close/grazing free-camera angles. Placement/size stay the same.
        using var img = PixelFont.RenderImage(number, Colors.White, Colors.Transparent, 1);
        var glyph = new SurfaceTool(); glyph.Begin(Mesh.PrimitiveType.Triangles);
        glyph.SetNormal(Vector3.Back);
        for (int y = 0; y < img.GetHeight(); y++)
        for (int x = 0; x < img.GetWidth(); x++)
        {
            if (img.GetPixel(x,y).A < .5f) continue;
            float left = (x / (float)img.GetWidth() - .5f) * 1.2f;
            float right = ((x + 1f) / img.GetWidth() - .5f) * 1.2f;
            float top = (.5f - y / (float)img.GetHeight()) * .45f;
            float bottom = (.5f - (y + 1f) / img.GetHeight()) * .45f;
            var tl = new Vector3(left,top,0); var tr = new Vector3(right,top,0);
            var bl = new Vector3(left,bottom,0); var br = new Vector3(right,bottom,0);
            foreach (var v in new[] { tl,tr,bl,tr,br,bl }) glyph.AddVertex(v);
        }
        var markMesh = glyph.Commit();
        var markMaterial = new StandardMaterial3D { AlbedoColor = new Color(.95f,.95f,.9f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
        foreach (float s in new[] { -1f, 1f })
            k.Turret.AddChild(new MeshInstance3D { Mesh = markMesh,
                Position = new Vector3(s * 1.365f, .3f, .35f), RotationDegrees = new Vector3(0,s * 90,0),
                MaterialOverride = markMaterial, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });

        k.Sound = new DieselSound { Voice=DieselSound.EngineVoice.Tank, BaseRate=50, RatePerSpeed=5, Body=.025f, Loudness=3, Reach=380, Position=new(0,1.3f,2.2f) };
        k.Hull.AddChild(k.Sound);
    }


    /// <summary>The world tape parts of one tank: hull (it moves, the TankBattle node does not), turret, barrel (recoil), muzzle flash, the road wheels (spinners) and the engine sound. The tread belts are left out: their position is a 0.289 m sawtooth that aliases at 15 Hz.</summary>
    static void MarkTape(Tank k, int no)
    {
        string n = $"{no}/";
        Tape.Part(k.Hull, n + "hull"); Tape.Part(k.Turret, n + "turret"); Tape.Part(k.Barrel, n + "barrel");
        if (k.Flash != null) Tape.Part(k.Flash, n + "flash");
        for (int i = 0; i < k.Wheels.Count; i++)
        {
            bool left = i < 6;
            Tape.Part(k.Wheels[i], n + k.Wheels[i].Name);
            Tape.Spin(k.Wheels[i], Vector3.Right, left ? () => -k.LeftTravel / .43f : () => -k.RightTravel / .43f);
        }
        Tape.Sound(k.Hull, TapeSoundChannel.DieselSpeed, () => k.Sound.Speed, v => k.Sound.Speed = v);
    }

    // ------------------------------------------------------------ update

    public void Tick(float dt, CarSim car)
    {
        var carPos = car != null ? car.Pos : new Vector3(-1e4f, 0, -1e4f);
        _car = car;
        // Rearm only after leaving the exercise, never while waiting for a tank to pass.
        if (car != null && !_area.Grow(60f).HasPoint(new Vector2(car.Pos.X, car.Pos.Z)) &&
            !_tanks.Exists(k => k.Crossing || k.AcrossRoad))
            _encounterUsed = false;
        if (car != null && dt > 0)
        {
            // What the gunners make of the car's motion: a slow, smoothed estimate.
            Vector2 v = new(car.Vel.X, car.Vel.Z);
            _carVelEst = _carVelEst.Lerp(v, Math.Min(1f, dt / 0.8f));
            _lastCar = new Vector2(car.Pos.X, car.Pos.Z);
        }
        foreach (var k in _tanks)
        {
            float yaw0 = k.Yaw, turret0 = k.TurretYaw;
            Drive(k, dt);
            if (_chasing) AimAtCar(k, dt);
            else Aim(k, dt);
            Place(k, dt);
            if (TankDbg && dt > 0) Measure(k, dt, yaw0, turret0);
        }
        if (_chasing) RevengeFire(dt);
        UpdateShells(dt, carPos);
        // Obstacles: three circles along each hull, moving with it.
        foreach (var k in _tanks)
        {
            Vector2 f = new(-MathF.Sin(k.Yaw), -MathF.Cos(k.Yaw));
            for (int s = 0; s < ObstacleSlotsPerTank; s++)
                _t.Obstacles[k.ObsBase + s] = new Obstacle(k.Pos + f * (-2.2f + s * 2.2f), 1.8f, f * k.Speed, Material: ImpactMaterial.Metal);
        }
        if (TankDbg) Report(dt);
        Exercise?.GroundBusy(_shells.Count>0 || _tanks.Exists(k=>k.Crossing || k.AcrossRoad));
    }

    /// <summary>Stop here and fire two or three rounds once the gun is laid.</summary>
    void Hold(Tank k)
    {
        k.Phase = Phase.Stand;
        k.Rounds = _rng.RandiRange(1, 2);
        k.MoveAfter = _rng.RandfRange(9f, 15f);
        k.Settle = _rng.RandfRange(1.5f, 2.5f);
        k.Linger = _rng.RandfRange(1f, 3f);
    }

    /// <summary>Shoot and scoot: sometimes back out along the way it came, then on to another position.</summary>
    void Relocate(Tank k)
    {
        if (k.AcrossRoad)
        {
            k.CrossGoal = k.CrossHome;
            k.Crossing = true;
            k.AcrossRoad = false;
            k.Phase = Phase.Drive;
            return;
        }
        if (k.CrossingCooldown <= 0 && TryCrossRoad(k)) return;
        float leg = k.LegStart.DistanceTo(k.Pos);
        if (k.Phase == Phase.Stand && leg > 9f && _rng.Randf() < 0.35f)
        {
            k.Phase = Phase.Reverse;
            k.ReverseLeft = Math.Min(_rng.RandfRange(5f, 10f), leg - 4f);
            return;
        }
        var spots = _spots[k.Side];
        int next = PickSpot(k, k.At, k.Pos);
        k.Route.Clear();
        if (LegClear(k.Pos, spots[next])) k.Route.Add(next);
        else
        {
            // Along the known legs from the position it holds (it stands on it, or on the leg that led there).
            var via = Path(k.Side, k.At, next);
            if (via == null) { Hold(k); return; }
            if (k.Pos.DistanceTo(spots[k.At]) > 1.5f) k.Route.Add(k.At);
            k.Route.AddRange(via);
        }
        k.At = next;
        k.LegStart = k.Pos;
        k.Cruise = _rng.RandfRange(CruiseMin, CruiseMax);
        k.Stuck = 0;
        k.Phase = Phase.Drive;
    }

    // A short, nearly perpendicular crossing, with both stopping points beyond the ditch.
    // Reserve it until the tank returns so the pair cannot meet nose to nose on the road.
    bool TryCrossRoad(Tank k)
    {
        k.CrossingCooldown = 0.5f;
        if(Exercise!=null && !Exercise.GroundAllowed) return false;
        if (_car == null || _chasing || _encounterUsed) return false;
        foreach (var other in _tanks)
            if (other != k && (other.Crossing || other.AcrossRoad)) return false;
        int index = _t.NearestCenter(k.Pos, out float distance);
        Vector2 centre = _t.Center[index];
        Vector2 normal = (k.Pos - centre).Normalized();
        if (distance < _t.HalfWidth[index] + RoadKeepOff || distance > 65f ||
            MathF.Abs(normal.Dot(_t.CenterDir(index))) > 0.25f) return false;
        Vector2 goal = centre - normal * (_t.HalfWidth[index] + RoadKeepOff + 5f);
        if (!_area.Grow(20f).HasPoint(goal) || RoadEdge(goal) < RoadKeepOff + 2f) return false;
        Vector2 direction = (goal - k.Pos).Normalized();
        int samples = (int)MathF.Ceiling(k.Pos.DistanceTo(goal));
        bool entered = false, exited = false;
        for (int i = 0; i <= samples; i++)
        {
            Vector2 p = k.Pos.Lerp(goal, i / (float)samples);
            foreach (var (c, r) in _avoid)
                if (p.DistanceTo(c) < r + PathClear) return false;
            if (RoadEdge(p) < 4f)
            {
                if (exited) return false;
                entered = true;
                int road = _t.NearestCenter(p, out _);
                if (MathF.Abs(direction.Dot(_t.CenterDir(road))) > 0.4f) return false;
            }
            else if (entered) exited = true;
        }
        if (!entered || !exited) return false;
        // Time the encounter along the actual road, including its bend. Straight-line
        // distance would trigger on the wrong arm of the U and for departing cars.
        int carIndex = _t.NearestCenter(new Vector2(_car.Pos.X, _car.Pos.Z), out float offRoad);
        Vector2 velocity = new(_car.Vel.X, _car.Vel.Z);
        float alongSpeed = velocity.Dot(_t.CenterDir(carIndex));
        if (offRoad > _t.HalfWidth[carIndex] + 8f || MathF.Abs(alongSpeed) < 3f) return false;
        int step = alongSpeed > 0 ? 1 : -1;
        float roadDistance = 0;
        int cursor = carIndex;
        for (int n = 0; cursor != index && n < _t.Center.Length; n++)
        {
            int next = (cursor + step + _t.Center.Length) % _t.Center.Length;
            roadDistance += _t.Center[cursor].DistanceTo(_t.Center[next]);
            cursor = next;
            if (roadDistance > 500f) return false;
        }
        float turnTime = MathF.Abs(Mathf.AngleDifference(k.Yaw, TrackData.YawFor(direction))) / PivotRate;
        float entryDistance = distance - _t.HalfWidth[index] - 2f;
        float entryTime = TravelTime(entryDistance);
        float arrival = roadDistance / MathF.Abs(alongSpeed);
        if (!EncounterWindow(arrival, turnTime + entryTime, roadDistance)) return false;
        k.CrossHome = k.Pos;
        k.CrossGoal = goal;
        k.Crossing = true;
        k.Route.Clear();
        k.Cruise = CruiseMax;
        k.Phase = Phase.Drive;
        _encounterUsed = true;
        return true;
    }

    // Allow a visible warning before the hull occupies the road. Refuse late triggers.
    internal static bool EncounterWindow(float arrival, float entry, float distance) =>
        distance >= 35f && arrival >= entry + 1.5f && arrival <= entry + 4f;

    internal bool EncounterStarted => _encounterUsed;

    static float TravelTime(float distance)
    {
        float accelerating = CruiseMax * CruiseMax / (2f * Accel);
        return distance <= accelerating ? MathF.Sqrt(2f * Math.Max(0, distance) / Accel)
            : CruiseMax / Accel + (distance - accelerating) / CruiseMax;
    }

    /// <summary>Shortest chain of clear legs from one position to another (excluding the start).</summary>
    List<int> Path(int side, int from, int to)
    {
        int n = _spots[side].Count;
        var dist = new float[n];
        var prev = new int[n];
        var done = new bool[n];
        for (int i = 0; i < n; i++) { dist[i] = float.MaxValue; prev[i] = -1; }
        dist[from] = 0;
        for (int it = 0; it < n; it++)
        {
            int u = -1;
            for (int i = 0; i < n; i++) if (!done[i] && (u < 0 || dist[i] < dist[u])) u = i;
            if (u < 0 || dist[u] == float.MaxValue) break;
            done[u] = true;
            for (int v = 0; v < n; v++)
                if (_legs[side][u, v] && dist[u] + _spots[side][u].DistanceTo(_spots[side][v]) < dist[v])
                {
                    dist[v] = dist[u] + _spots[side][u].DistanceTo(_spots[side][v]);
                    prev[v] = u;
                }
        }
        if (dist[to] == float.MaxValue) return null;
        var path = new List<int>();
        for (int v = to; v != from; v = prev[v]) path.Insert(0, v);
        return path;
    }

    void Drive(Tank k, float dt)
    {
        k.CrossingCooldown -= dt;
        float targetSpeed = 0f, turn = 0f;
        switch (k.Phase)
        {
            case Phase.Stand:
                // Check while holding an emplacement, rather than only at the random
                // end of a firing cycle. Committed crossings still finish during revenge.
                if (!_chasing && !k.AcrossRoad && k.CrossingCooldown <= 0 &&
                    MathF.Abs(k.Speed) < 0.05f && TryCrossRoad(k)) break;
                k.MoveAfter -= dt;
                if (!_chasing && (k.Rounds <= 0 || k.MoveAfter <= 0) && MathF.Abs(k.Speed) < 0.05f)
                {
                    k.Linger -= dt;
                    if (k.Linger <= 0) Relocate(k);
                }
                break;
            case Phase.Reverse:
                if (_chasing) { k.Phase = Phase.Stand; break; }
                k.ReverseLeft -= MathF.Abs(k.Speed) * dt;
                // Ease off so it stops where it meant to.
                targetSpeed = -Math.Min(ReverseSpeed, MathF.Sqrt(2f * Brake * Math.Max(0f, k.ReverseLeft)));
                if (k.ReverseLeft <= 0.05f && MathF.Abs(k.Speed) < 0.05f) Relocate(k);
                break;
            case Phase.Drive:
                if (_chasing && !k.Crossing) { k.Phase = Phase.Stand; break; }
                if (!k.Crossing && k.Route.Count == 0) { Hold(k); break; }
                Vector2 goal = k.Crossing ? k.CrossGoal : _spots[k.Side][k.Route[0]], to = goal - k.Pos;
                float left = to.Length();
                if (left < 1.2f)
                {
                    if (k.Crossing)
                    {
                        k.Crossing = false;
                        k.AcrossRoad = goal.DistanceTo(k.CrossHome) > 2f;
                        k.CrossingCooldown = _rng.RandfRange(25f, 45f);
                        Hold(k);
                        break;
                    }
                    k.LegStart = goal;
                    k.Route.RemoveAt(0);
                    if (k.Route.Count == 0 && MathF.Abs(k.Speed) < 0.3f) Hold(k);
                    break;
                }
                float err = Mathf.AngleDifference(k.Yaw, TrackData.YawFor(to));
                if (MathF.Abs(err) > 0.25f)
                {
                    // Pointing the wrong way: stop, then pivot on the spot.
                    if (MathF.Abs(k.Speed) < 0.2f) turn = Math.Clamp(err * 1.2f, -PivotRate, PivotRate);
                }
                else
                {
                    turn = Math.Clamp(err * 1.2f, -DriveTurnRate, DriveTurnRate);
                    // Only this leg's end counts when more legs follow: it stops at every position.
                    targetSpeed = Math.Min(k.Cruise, MathF.Sqrt(2f * Brake * Math.Max(0f, left - 1f)));
                    targetSpeed *= 1f - Math.Min(0.6f, MathF.Abs(err) * 2f);
                }
                break;
        }
        k.Yaw += turn * dt;
        float rate = MathF.Abs(targetSpeed) > MathF.Abs(k.Speed) && MathF.Sign(targetSpeed) != -MathF.Sign(k.Speed) ? Accel : Brake;
        k.Speed = Mathf.MoveToward(k.Speed, targetSpeed, rate * dt);
        Vector2 f = new(-MathF.Sin(k.Yaw), -MathF.Cos(k.Yaw));
        Vector2 next = k.Pos + f * k.Speed * dt;
        // Ordinary patrol legs stay off the road; committed crossings continue to the far shoulder.
        if (!k.Crossing && k.Speed != 0 && RoadEdge(next) < RoadKeepOff && RoadEdge(next) < RoadEdge(k.Pos))
        {
            k.Speed = 0;
            k.Stuck += dt;
            if (k.Stuck > 3f && k.Phase != Phase.Stand) { k.Route.Clear(); Hold(k); }
        }
        else k.Pos = next;

        // Dust off the tracks, diesel smoke from the back.
        float speed = MathF.Abs(k.Speed);
        if (_rng.Randf() < dt * 30f * (speed / SpeedRef))
        {
            Vector2 back = k.Pos - f * 3.2f * MathF.Sign(k.Speed) + new Vector2(-f.Y, f.X) * _rng.RandfRange(-1.6f, 1.6f);
            DustFx(_fx, _t, back);
        }
        {
            // The player's diesel smoke from the exhaust at the back, a tank's big engine (TapeEmitters makes the same in a replay).
            Vector3 fwd3 = -k.Hull.GlobalTransform.Basis.Z.Normalized();
            _fx.EngineSoot(k.Hull.GlobalTransform * SootPipe, -fwd3, fwd3 * k.Speed, Mathf.Clamp(speed / SpeedRef, 0.25f, 1f), SootPower, ref k.Soot, dt);
        }
        k.Sound.Speed = speed + MathF.Abs(turn) * 6f;
    }

    /// <summary>One puff of dust off a tank's tracks at <paramref name="back"/> (live play and the title replay's emitters).</summary>
    internal static void DustFx(TyreEffects fx, TrackData t, Vector2 back) =>
        fx.Smoke(new Vector3(back.X, t.MeshHeight(back.X, back.Y) + 0.3f, back.Y), Vector3.Up * 0.6f, new Color(0.55f, 0.46f, 0.34f), 0.12f, 0.38f, 1.8f, 1f);
    /// <summary>One puff of diesel smoke at the exhaust (live play and the replay).</summary>
    internal static readonly Vector3 SootPipe = new(1.2f, 1.3f, 3.3f);
    internal const float SootPower = 3f;
    internal static void ExhaustFx(TyreEffects fx, Vector3 ex) => fx.Smoke(ex, Vector3.Up * 1.5f, new Color(0.1f, 0.1f, 0.1f), 0.6f, 0.6f, 2.2f, 1.6f);
    /// <summary>The tanks' hulls, for the replay's dust emitters.</summary>
    internal IEnumerable<Node3D> TapeHulls() { foreach (var k in _tanks) yield return k.Hull; }
    /// <summary>Distance a tank's dust trails behind it, and the speed at which it is thickest (m/s): the live constants, for the replay.</summary>
    internal const float DustSpeedRef = SpeedRef;

    /// <summary>Distance from the road edge (negative on the road).</summary>
    float RoadEdge(Vector2 p)
    {
        int i = _t.NearestCenter(p, out float d);
        return d - _t.HalfWidth[i];
    }

    /// <summary>A straight leg that keeps off the road and clear of trees, poles and walls.</summary>
    bool LegClear(Vector2 a, Vector2 b)
    {
        int n = Math.Max(1, (int)a.DistanceTo(b));
        for (int i = 0; i <= n; i++)
        {
            Vector2 p = a.Lerp(b, i / (float)n);
            if (RoadEdge(p) < RoadKeepOff) return false;
            foreach (var (c, r) in _avoid)
                if (p.DistanceTo(c) < r + PathClear) return false;
        }
        return true;
    }

    /// <summary>Turret onto the other side's tank; fires only standing still with the gun laid.</summary>
    void Aim(Tank k, float dt)
    {

        k.ReloadLeft -= dt;
        var target = Foe(k);
        if (target == null) return;
        // During a relocation the barrel travels over the hull rather than tracking across the road.
        Traverse(k, k.Phase != Phase.Stand ? k.Yaw : TrackData.YawFor(target.Pos - k.Pos), dt);
        if (k.Phase != Phase.Stand || MathF.Abs(k.Speed) > 0.01f) return;
        if(Exercise!=null && !Exercise.GroundAllowed) return;
        k.Settle -= dt;
        if (k.Rounds > 0 && k.Settle <= 0 && k.ReloadLeft <= 0 && MathF.Abs(k.AimErr) < 0.01f)
        {
            k.ReloadLeft = _rng.RandfRange(ReloadMin, ReloadMax);
            k.Rounds--;
            Fire(k, target);
        }
    }

    /// <summary>Turret towards a world yaw at the traverse rate; remembers the error.</summary>
    void Traverse(Tank k, float worldYaw, float dt)
    {
        float wantLocal = Mathf.AngleDifference(k.Yaw, worldYaw);
        k.AimErr = Mathf.AngleDifference(k.TurretYaw, wantLocal);
        float desired=Math.Clamp(k.AimErr*2,-TraverseRate,TraverseRate);
        k.TraverseVelocity=Mathf.MoveToward(k.TraverseVelocity,desired,dt*.7f);
        float step=k.TraverseVelocity*dt;
        if(MathF.Abs(step)>MathF.Abs(k.AimErr)) {step=k.AimErr;k.TraverseVelocity=0;}
        k.TurretYaw+=step;
    }

    void Fire(Tank k, Tank target)
    {
        Vector3 muzzle = k.Barrel.GlobalTransform * new Vector3(0, 0, -5.3f);
        // Legendary Soviet gunnery, mostly in range: short or long along the line of fire,
        // a few metres sideways, now and then close enough to ring the other tank's armour.
        Vector2 dir = (target.Pos - k.Pos).Normalized(), side = new(-dir.Y, dir.X);
        Vector3 aim3;
        if (_rng.Randf() < 0.08f)
        {
            Vector2 aim = target.Pos + dir * _rng.RandfRange(-1.5f, 1.5f) + side * _rng.RandfRange(-1f, 1f);
            aim3 = new(aim.X, _t.MeshHeight(aim.X, aim.Y) + 1f, aim.Y);
        }
        else
        {
            // Into the ground at the aim point. A long round passes beside the target, not through it.
            float along = _rng.RandfRange(-35f, 25f);
            if (MathF.Abs(along) < 5f) along += along < 0 ? -5f : 5f;
            float across = along > 0 ? (_rng.Randf() < 0.5f ? -1f : 1f) * _rng.RandfRange(2.8f, 5f) : _rng.RandfRange(-4f, 4f);
            Vector2 aim = target.Pos + dir * along + side * across;
            aim3 = new(aim.X, _t.MeshHeight(aim.X, aim.Y) - 0.3f, aim.Y);
        }
        if (TestAim?.Invoke(k.Pos, target.Pos) is Vector2 ta) aim3 = new(ta.X, _t.MeshHeight(ta.X, ta.Y) - 0.3f, ta.Y);
        _stat.Shots++;
        Launch(k, muzzle, aim3, false, 0);
    }

    /// <summary>A tank fired: the muzzle (the night's muzzle flash).</summary>
    public event Action<Vector3> Fired;

    void Launch(Tank k, Vector3 muzzle, Vector3 aim3, bool chase, int no)
    {
        if(MaintenanceHall.BlocksFire(_t,new(muzzle.X,muzzle.Z),new(aim3.X,aim3.Z))) return;
        Vector3 local=k.Turret.ToLocal(aim3)-k.Barrel.Position;
        float pitch=Math.Clamp(MathF.Atan2(local.Y,new Vector2(local.X,local.Z).Length()),-.1f,.25f);
        k.Barrel.Rotation=new(pitch,0,0);
        muzzle=k.Barrel.GlobalTransform*new Vector3(0,0,-5.3f);
        Fired?.Invoke(muzzle);
        Vector3 dir = (aim3 - muzzle).Normalized();
        var shell = new Shell { Pos = muzzle, Vel = dir * ShellSpeed + Vector3.Up * 2f, Life = 3f, Owner = k, Chase = chase, No = no };
        shell.Origin = muzzle; shell.Vel0 = shell.Vel;
        _shells.Add(shell);

        // Muzzle blast, smoke ring, recoil, boom.
        Vector3 fwd = -k.Barrel.GlobalTransform.Basis.Z;
        BlastFx(_fx, _t, _rng, this, muzzle, fwd);
        TapeFx?.Fx(new TapeEvent(0f, TapeEventType.Effect, Kind: TapeFxKind.TankBlast, Effect: TapeEffect.MuzzleBlast, Pos: muzzle, Dir: fwd, Life: 2.8f));
        k.Recoil = 1f;
        k.FlashLeft=.065f;
    }

    /// <summary>The cannon's blast: fire, a smoke ring, gas and dust under the muzzle, and the boom (live play and the tape's replay).</summary>
    internal static void BlastFx(TyreEffects fx, TrackData t, RandomNumberGenerator rng, Node parent, Vector3 muzzle, Vector3 fwd)
    {
        for (int i = 0; i < 14; i++)
            fx.Burst(muzzle, fwd * rng.RandfRange(8f, 20f) + new Vector3(rng.RandfRange(-3, 3), rng.RandfRange(-1, 3), rng.RandfRange(-3, 3)),
                rng.Randf() < 0.5f ? new Color(1f, 0.85f, 0.4f) : new Color(1f, 0.5f, 0.1f), 1f, 1.1f, 0.3f, 0.2f, 0f, 3f, false);
        for (int i = 0; i < 6; i++)
            fx.Smoke(muzzle + fwd * i * 0.8f, fwd * 3f + Vector3.Up, new Color(0.7f, 0.68f, 0.62f), 0.6f, 1f, 3.2f, 2f);
        // Propellant gases erupt forward and sideways; the pressure lifts dust beneath the muzzle.
        for(int i=0;i<14;i++)
        {
            float a=i*MathF.Tau/14;
            Vector3 radial=new Vector3(MathF.Cos(a),.2f,MathF.Sin(a));
            fx.Smoke(muzzle+fwd*.5f,radial*7+fwd*5,new Color(.65f,.61f,.5f),.85f,.6f,4.5f,1.6f);
            Vector3 ground=new Vector3(muzzle.X,t.MeshHeight(muzzle.X,muzzle.Z)+.15f,muzzle.Z);
            fx.Smoke(ground+radial,radial*5+Vector3.Up,new Color(.43f,.36f,.24f),.7f,1,4.8f,2.8f);
        }
        parent.AddChild(new ExplosionSound { Position = muzzle, Loudness = 8f, Reach = 900f, ReferenceDistance = 160f, Cannon = true });
    }

    /// <summary>The glow of a shell in flight: one spark at its position and one half-way back (each step in live play, each frame of the replay).</summary>
    internal static void TracerFx(TyreEffects fx, Vector3 pos, Vector3 prev)
    {
        fx.Burst(pos, Vector3.Zero, new Color(1f, 0.9f, 0.5f), 1f, 0.45f, 0.1f, 0.12f, 0f, 0f, false);
        fx.Burst(prev.Lerp(pos, 0.5f), Vector3.Zero, new Color(1f, 0.7f, 0.3f), 0.8f, 0.35f, 0.1f, 0.1f, 0f, 0f, false);
    }

    /// <summary>The shell's flight is over (it struck a tank, the car or the ground, or ran out): it is on the tape as a ballistic event.</summary>
    void TapeShell(Shell s)
    {
        if (TapeFx == null) return;
        float flown = Math.Max(0.0001f, 3f - s.Life);
        TapeFx.Fx(new TapeEvent(flown, TapeEventType.Projectile, Kind: TapeFxKind.TankShell, Projectile: TapeProjectile.Shell, Pos: s.Origin, Velocity: s.Vel0, Gravity: 4f, Life: flown));
    }

    void UpdateShells(float dt, Vector3 carPos)
    {
        for (int i = _shells.Count - 1; i >= 0; i--)
        {
            var s = _shells[i];
            Vector3 prev = s.Pos;
            s.Vel += Vector3.Down * 4f * dt;
            s.Pos += s.Vel * dt;
            s.Life -= dt;
            // Tracer.
            TracerFx(_fx, s.Pos, prev);

            Tank hit = null;
            foreach (var k in _tanks)
                if (k != s.Owner && new Vector2(s.Pos.X, s.Pos.Z).DistanceTo(k.Pos) < 2.2f && s.Pos.Y < _t.MeshHeight(k.Pos.X, k.Pos.Y) + 2.5f) hit = k;
            float ground = _t.MeshHeight(s.Pos.X, s.Pos.Z);
            if (hit != null)
            {
                TapeShell(s);
                Ricochet(s.Pos, hit);
                if (TankDbg && !s.Chase) Landed(s.Pos, true);
                ChaseShellDone(s, -1);
                _shells.RemoveAt(i);
                continue;
            }
            if (HitsCar(prev, s.Pos))
            {
                // Straight into the Pyatyorka: it goes off on the body, not on the ground behind it.
                TapeShell(s);
                Impact(s.Pos, carPos);
                ChaseShellDone(s, s.Pos.DistanceTo(carPos));
                _shells.RemoveAt(i);
            }
            else if (s.Pos.Y <= ground || s.Life <= 0)
            {
                var p = new Vector3(s.Pos.X, ground, s.Pos.Z);
                TapeShell(s);
                Impact(p, carPos);
                if (TankDbg && !s.Chase) Landed(p, false);
                ChaseShellDone(s, new Vector2(p.X - carPos.X, p.Z - carPos.Z).Length());
                _shells.RemoveAt(i);
            }
        }
    }

    void Impact(Vector3 p, Vector3 carPos)
    {
        _craters.AddChild(GroundImpactEffects.Create(_t,p));
        if (_craters.GetChildCount() > 40) _craters.GetChild(0).QueueFree();
        AddChild(new ExplosionSound { Position = p, Loudness = 3f, Reach = 650f, ReferenceDistance = 90f });
        TapeFx?.Fx(new TapeEvent(0f, TapeEventType.Effect, Kind: TapeFxKind.TankImpact, Effect: TapeEffect.ShellImpact, Pos: p, Life: 35f));
        ShellLanded?.Invoke(p, new Vector2(p.X - carPos.X, p.Z - carPos.Z).Length());
    }

    void Ricochet(Vector3 p, Tank k)
    {
        // Direct hit: sparks everywhere, a clang, the tank carries on regardless.
        RicochetFx(_fx, _rng, this, p);
        TapeFx?.Fx(new TapeEvent(0f, TapeEventType.Effect, Kind: TapeFxKind.TankRicochet, Effect: TapeEffect.ShellImpact, Pos: p, Life: .4f));
    }

    internal static void RicochetFx(TyreEffects fx, RandomNumberGenerator rng, Node parent, Vector3 p)
    {
        for (int i = 0; i < 30; i++)
            fx.Burst(p, new Vector3(rng.RandfRange(-10, 10), rng.RandfRange(2, 12), rng.RandfRange(-10, 10)),
                new Color(1f, 0.95f, 0.6f), 1f, 0.25f, 0.1f, 0.4f, 12f, 0.5f, false);
        parent.AddChild(new ExplosionSound { Position = p, Loudness = -1f, Reach = 500f });
    }

    void Place(Tank k, float dt)
    {
        Vector2 f = new(-MathF.Sin(k.Yaw), -MathF.Cos(k.Yaw)), r = new(-f.Y, f.X);
        float hF = _t.MeshHeight(k.Pos.X + f.X * 3f, k.Pos.Y + f.Y * 3f);
        float hB = _t.MeshHeight(k.Pos.X - f.X * 3f, k.Pos.Y - f.Y * 3f);
        float hR = _t.MeshHeight(k.Pos.X + r.X * 1.6f, k.Pos.Y + r.Y * 1.6f);
        float hL = _t.MeshHeight(k.Pos.X - r.X * 1.6f, k.Pos.Y - r.Y * 1.6f);
        // Suspension responds to acceleration and track motion, never to wall-clock time at rest.
        if(dt>0)
        {
            float turn=Mathf.AngleDifference(k.PreviousYaw,k.Yaw)/dt;
            float acceleration=(k.Speed-k.PreviousSpeed)/dt;
            k.PitchMotion=Mathf.Lerp(k.PitchMotion,Math.Clamp(acceleration*.025f,-.065f,.065f),1-MathF.Exp(-dt*4));
            k.RollMotion=Mathf.Lerp(k.RollMotion,Math.Clamp(-turn*k.Speed*.02f,-.05f,.05f),1-MathF.Exp(-dt*3));
            k.LeftTravel+=(k.Speed-turn*1.6f)*dt;
            k.RightTravel+=(k.Speed+turn*1.6f)*dt;
            k.MotionTime+=MathF.Abs(k.Speed)*dt;
            k.Recoil=Mathf.MoveToward(k.Recoil,0,dt*1.8f);
            k.FlashLeft=Math.Max(0,k.FlashLeft-dt);
        }
        k.PreviousSpeed=k.Speed;k.PreviousYaw=k.Yaw;
        float terrainPitch=MathF.Atan2(hF-hB,6f),terrainRoll=MathF.Atan2(hR-hL,3.2f);
        var b = new Basis(Vector3.Up, k.Yaw)
              * new Basis(Vector3.Right, terrainPitch+k.PitchMotion-k.Recoil*.035f)
              * new Basis(Vector3.Back, terrainRoll+k.RollMotion);
        k.Hull.GlobalTransform = new Transform3D(b, new Vector3(k.Pos.X, (hF + hB) * 0.5f, k.Pos.Y));
        k.Turret.Rotation = new Vector3(0, k.TurretYaw, 0);
        k.Barrel.Position = new Vector3(0, 0.3f, -1.1f + k.Recoil*k.Recoil*.42f);
        for(int i=0;i<k.Wheels.Count;i++)
        {
            var wheel=k.Wheels[i];float travel=i<6?k.LeftTravel:k.RightTravel;
            wheel.Rotation=new Vector3(-travel/.43f,0,0);
        }
        for(int run=0;run<k.Belts.Count;run++)
        {
            float travel=run<2?k.LeftTravel:k.RightTravel;
            k.Belts[run].Position=new Vector3(0,0,(run%2==0?-1:1)*Mathf.PosMod(travel,.289f));
        }
        if(k.Flash!=null) k.Flash.Visible=k.FlashLeft>0;

    }


    public Vector2? FirstTank => _tanks.Count > 0 ? _tanks[0].Pos : null;

    // ------------------------------------------------------------ revenge (the tank driver's grandmother)

    public const int ChaseShots = 10;
    const float ChaseRange = 220f;           // opens fire inside this, with a clear line of fire
    const float ChaseLead = 0.7f;            // how much of the car's motion the gunner allows for (standing, he tracks well)
    static readonly bool RevengeDbg = OS.HasEnvironment("REVENGEDBG");

    CarSim _car;
    Vector2 _carVelEst, _lastCar;
    bool _chasing;
    int _shotsLeft, _shellsOut, _turn;
    float _revengeCd;

    /// <summary>(shot number 1..10) a tank fires at the car.</summary>
    public event Action<int> ChaseShot;
    /// <summary>The last of the ten shells has landed: back to the exercise.</summary>
    public event Action ChaseOver;

    /// <summary>The revenge is on: both tanks hold their positions and shoot at the car.</summary>
    public bool Chasing => _chasing;
    /// <summary>Shots the revenge still has to fire.</summary>
    public int ShotsLeft => _shotsLeft;
    /// <summary>Distance from the nearest tank to the car, m (for tests).</summary>
    public float NearestToCar
    {
        get
        {
            float best = float.MaxValue;
            foreach (var k in _tanks) best = Math.Min(best, k.Pos.DistanceTo(_lastCar));
            return best;
        }
    }

    /// <summary>The tank driver's grandmother has been run over: both tanks stop where they are,
    /// turn their guns on the car and fire ten shells at it between them. False if one is already on.</summary>
    public bool StartChase()
    {
        if (_chasing || _tanks.Count == 0) return false;
        _chasing = true;
        _shotsLeft = ChaseShots;
        _shellsOut = 0;
        _revengeCd = _rng.RandfRange(1f, 2f);
        foreach (var k in _tanks) k.ReloadLeft = Math.Min(k.ReloadLeft, _rng.RandfRange(1f, 3f));
        if (RevengeDbg) GD.Print($"revenge tanks: on, nearest tank {NearestToCar:F0} m from the car");
        return true;
    }

    /// <summary>Calls the revenge off at once (shells in flight still land); the tanks go back to the exercise.</summary>
    public void EndRevenge()
    {
        if (!_chasing) return;
        _chasing = false;
        _shotsLeft = _shellsOut = 0;
        foreach (var k in _tanks) if (k.Phase == Phase.Stand) Hold(k);
        if (RevengeDbg) GD.Print("revenge tanks: called off");
    }

    /// <summary>Turret tracks where the car will be.</summary>
    void AimAtCar(Tank k, float dt)
    {

        k.ReloadLeft -= dt;
        Traverse(k, TrackData.YawFor(CarAimPoint(k) - k.Pos), dt);
    }

    Vector2 CarAimPoint(Tank k) => _lastCar + _carVelEst * (k.Pos.DistanceTo(_lastCar) / ShellSpeed) * ChaseLead;

    /// <summary>The two tanks take turns: a shell at the car every 4-5 s from whichever is loaded,
    /// laid, standing and has the car in range over the terrain.</summary>
    void RevengeFire(float dt)
    {
        _revengeCd -= dt;
        if(Exercise!=null && !Exercise.GroundAllowed) return;
        if (_car == null || _shotsLeft <= 0 || _revengeCd > 0) return;
        if (BonusCampLayout.Inside(_lastCar)) return;      // (the car is inside the base: the tanks hold their fire, the shots wait until it is out again)
        for (int j = 0; j < _tanks.Count; j++)
        {
            int i = (_turn + j) % _tanks.Count;
            var k = _tanks[i];
            float dist = k.Pos.DistanceTo(_lastCar);
            if (k.ReloadLeft > 0 || MathF.Abs(k.Speed) > 0.05f || MathF.Abs(k.AimErr) > 0.02f || dist > ChaseRange || !LineOfFire(k)) continue;
            _turn = i + 1;
            _revengeCd = _rng.RandfRange(4f, 5f);
            k.ReloadLeft = _rng.RandfRange(ReloadMin, ReloadMax);
            // Mostly a range error, growing with distance; the gunner is no marksman, but he is motivated.
            Vector2 aimAt = CarAimPoint(k), dir = (aimAt - k.Pos).Normalized(), side = new(-dir.Y, dir.X);
            float along = (_rng.Randf() + _rng.Randf() - 1f) * (2f + dist * 0.05f);
            float across = (_rng.Randf() + _rng.Randf() - 1f) * (1f + dist * 0.015f);
            Vector2 aim = aimAt + dir * along + side * across;
            Vector3 muzzle = k.Barrel.GlobalTransform * new Vector3(0, 0, -5.3f);
            Vector3 aim3 = new(aim.X, _t.MeshHeight(aim.X, aim.Y) + 0.1f, aim.Y);   // low: through the car or into the ground at it
            _shotsLeft--;
            _shellsOut++;
            int no = ChaseShots - _shotsLeft;
            Launch(k, muzzle, aim3, true, no);
            if (RevengeDbg)
                GD.Print($"revenge tanks: shot {no}/{ChaseShots} from tank {i} at {dist:F0} m, car {_car.Speed * 3.6f:F0} km/h, aim {aim.DistanceTo(_lastCar):F1} m off the car");
            ChaseShot?.Invoke(no);
            return;
        }
    }

    /// <summary>No hill between the gun and the car (a height check every 4 m along the line).</summary>
    bool LineOfFire(Tank k)
    {
        Vector3 a = new(k.Pos.X, _t.MeshHeight(k.Pos.X, k.Pos.Y) + 2.4f, k.Pos.Y);
        Vector3 b = _car.Pos + Vector3.Up * 0.5f;
        int n = Math.Max(2, (int)(a.DistanceTo(b) / 4f));
        for (int i = 1; i < n; i++)
        {
            Vector3 p = a.Lerp(b, i / (float)n);
            if (_t.MeshHeight(p.X, p.Z) > p.Y - 0.3f) return false;
        }
        return true;
    }

    /// <summary>A shell's segment this step passes through the car's body (three circles along it, below the roof).</summary>
    bool HitsCar(Vector3 a, Vector3 b)
    {
        if (_car == null) return false;
        for (int ci = -1; ci <= 1; ci++)
        {
            Vector3 c = _car.Pos + _car.Rot * new Vector3(0, 0.2f, ci * 1.3f);
            Vector3 ab = b - a;
            float u = ab.LengthSquared() > 1e-6f ? Math.Clamp((c - a).Dot(ab) / ab.LengthSquared(), 0f, 1f) : 0f;
            Vector3 q = a + ab * u;
            if (new Vector2(q.X - c.X, q.Z - c.Z).Length() < 1.0f && MathF.Abs(q.Y - c.Y) < 0.9f) return true;
        }
        return false;
    }


    void ChaseShellDone(Shell s, float distToCar)
    {
        if (!s.Chase || !_chasing) return;
        _shellsOut--;
        if (RevengeDbg) GD.Print($"revenge tanks: shell {s.No} lands {(distToCar < 0 ? "on the other tank" : $"{distToCar:F1} m from the car")}");
        if (_shotsLeft <= 0 && _shellsOut <= 0)
        {
            _chasing = false;
            foreach (var k in _tanks) if (k.Phase == Phase.Stand) Hold(k);
            if (RevengeDbg) GD.Print("revenge tanks: ten shots fired, back to the exercise");
            ChaseOver?.Invoke();
        }
    }

    // ------------------------------------------------------------ TANKDBG: exercise statistics

    static readonly bool TankDbg = OS.HasEnvironment("TANKDBG");

    class Stats
    {
        public float Time, NextReport = 30f, DistMin = float.MaxValue, DistSum, MaxTraverse, MaxTurn, MaxSpeed, MinEdge = float.MaxValue, LandEdgeSum;
        public int Shots, Landed, OnRoad, Within10, TankHits;
        public readonly float[] Corridor = new float[4], Moving = new float[4];
    }
    Stats _stat = new();

    void Measure(Tank k, float dt, float yaw0, float turret0)
    {
        int i = _tanks.IndexOf(k);
        if (i > 3) return;
        _stat.MaxTurn = Math.Max(_stat.MaxTurn, MathF.Abs(Mathf.AngleDifference(yaw0, k.Yaw)) / dt);
        _stat.MaxTraverse = Math.Max(_stat.MaxTraverse, MathF.Abs(Mathf.AngleDifference(turret0, k.TurretYaw)) / dt);
        _stat.MaxSpeed = Math.Max(_stat.MaxSpeed, MathF.Abs(k.Speed));
        float edge = RoadEdge(k.Pos);
        _stat.MinEdge = Math.Min(_stat.MinEdge, edge);
        if (edge < 6f) _stat.Corridor[i] += dt;              // road, ditch and a metre
        if (MathF.Abs(k.Speed) > 0.05f || yaw0 != k.Yaw) _stat.Moving[i] += dt;
    }

    void Landed(Vector3 p, bool tank)
    {
        if (tank) { _stat.TankHits++; return; }
        float edge = RoadEdge(new Vector2(p.X, p.Z));
        _stat.Landed++;
        _stat.LandEdgeSum += Math.Max(0f, edge);
        if (edge < 0) _stat.OnRoad++;
        else if (edge < 10f) _stat.Within10++;
    }

    void Report(float dt)
    {
        _stat.Time += dt;
        if (_tanks.Count >= 2)
        {
            float d = _tanks[0].Pos.DistanceTo(_tanks[1].Pos);
            _stat.DistMin = Math.Min(_stat.DistMin, d);
            _stat.DistSum += d * dt;
        }
        if (_stat.Time < _stat.NextReport) return;
        _stat.NextReport += 30f;
        float T = _stat.Time;
        GD.Print($"TANKDBG {T:F0} s: distance min {_stat.DistMin:F0} mean {_stat.DistSum / T:F0} m | in road corridor " +
                 $"{100 * _stat.Corridor[0] / T:F1} % / {100 * _stat.Corridor[1] / T:F1} %, min road-edge distance {_stat.MinEdge:F1} m | moving " +
                 $"{100 * _stat.Moving[0] / T:F0} % / {100 * _stat.Moving[1] / T:F0} % | shots {_stat.Shots}, landed {_stat.Landed} " +
                 $"(on the road {_stat.OnRoad}, within 10 m of it {_stat.Within10}, mean {(_stat.Landed > 0 ? _stat.LandEdgeSum / _stat.Landed : 0):F1} m from the edge), on a tank {_stat.TankHits} | " +
                 $"max traverse {_stat.MaxTraverse:F2} rad/s, max hull turn {_stat.MaxTurn:F2} rad/s, max speed {_stat.MaxSpeed:F1} m/s");
    }
}
