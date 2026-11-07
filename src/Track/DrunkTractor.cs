using System;
using System.Collections.Generic;
using CCCPRacing.Car;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Track;

/// <summary>
/// The kolkhoz MT-Agro with its plough down and a driver who has had his 300 grams.
/// Wanders from one random spot to the next, weaving, changing speed, dozing off and hiccuping
/// black smoke. Whatever the plough passes over becomes ploughed field: furrows you can see and
/// feel (low grip, drag), including stripes across the road. A moving obstacle for the car.
/// Everything resets when the race restarts.
/// </summary>
public partial class DrunkTractor : Node3D
{
    const float PloughBack = 3.5f;       // plough centre behind the tractor origin
    const float PloughWidth = 2.4f;
    const float Edge = 28f;              // stays this far from the map edge
    const int MaxSegments = 3000;

    TrackData _t;
    TyreEffects _fx;
    SurfaceType[] _origSurf;
    Vector2 _start;
    float _startYaw;

    Vector2 _pos, _goal;
    float _yaw, _speed, _targetSpeed;
    float _soot, _speedTimer, _napTimer, _hicTimer, _puffTimer, _clodAcc, _t0, _drift, _segDist;
    int _obsBody = -1, _obsPlough = -1;
    readonly List<(Vector2 p, float r)> _avoid = new();
    readonly RandomNumberGenerator _rng = new() { Seed = 1986 };
    readonly RandomNumberGenerator _departureRng = new() { Seed = 1942 };
    BarnDoors _barnDoor;
    float _departureDelay, _doorOpen;
    int _departureStage = 3; // 0 wait, 1 open, 2 leave, 3 plough normally

    public Node3D Model => _model;
    Node3D _model, _driver, _head;
    CCCPRacing.Audio.DieselSound _sound;

    // Furrows: rolling overlay mesh behind the plough.
    readonly Vector3[] _v = new Vector3[MaxSegments * 12];
    readonly Color[] _c = new Color[MaxSegments * 12];
    int _segHead, _segCount;
    bool _dirty;
    Vector2? _lastMark;
    ArrayMesh _furrowMesh;

    public Vector2 Position2D => _pos;
    public float Speed => _speed;
    public Vector2 Forward2D => Forward;
    public bool Dozing => _napTimer > 0;
    public bool HasDeparted => _departureStage == 3;
    public bool CrossingBarnRoad => _departureStage == 2;
    public int BodyObstacle => _obsBody;
    public int PloughObstacle => _obsPlough;

    public void ConfigureDeparture(BarnDoors door)
    {
        _barnDoor = door;
        Reset();
    }

    static readonly Color Soil = new(0.40f, 0.29f, 0.18f);
    static readonly Color Furrow = new(0.26f, 0.18f, 0.10f);

    public void Init(TrackData t, TyreEffects fx, Vector2 start, float yaw)
    {
        _t = t;
        _fx = fx;
        _start = start;
        _startYaw = yaw;
        _origSurf = (SurfaceType[])t.Surf.Clone();

        // Things a drunk still manages not to plough into.
        foreach (var o in t.Obstacles)
            _avoid.Add((o.Center, o.Radius));
        _obsBody = t.Obstacles.Count;
        t.Obstacles.Add(new Obstacle(start, 1.3f));
        _obsPlough = t.Obstacles.Count;
        t.Obstacles.Add(new Obstacle(start, 1.1f));

        BuildModel();
        _furrowMesh = new ArrayMesh();
        var furrows = new MeshInstance3D
        {
            Mesh = _furrowMesh,
            TopLevel = true,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                VertexColorUseAsAlbedo = true,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            },
        };
        AddChild(furrows);
        _sound = new CCCPRacing.Audio.DieselSound();
        _model.AddChild(_sound);
        Tape.Version(this, 1);
        Tape.Part(this, "root"); Tape.Part(_model, "model"); Tape.Part(_driver, "driver"); Tape.Part(_head, "head");
        Tape.Sound(_model, TapeSoundChannel.DieselSpeed, () => _sound.Speed, v => _sound.Speed = v);
        Tape.Sound(_model, TapeSoundChannel.DieselDozing, () => _sound.Dozing ? 1f : 0f, v => _sound.Dozing = v > 0.5f);
        Reset();
    }

    public void Reset()
    {
        Array.Copy(_origSurf, _t.Surf, _origSurf.Length);
        _pos = _start;
        _yaw = _startYaw;
        _speed = 0;
        _targetSpeed = 2.5f;
        _napTimer = 0;
        _hicTimer = 5f;
        _segCount = _segHead = 0;
        _lastMark = null;
        _dirty = true;
        PickGoal();
        _departureStage = _barnDoor == null ? 3 : 0;
        _departureDelay = _departureRng.RandfRange(16f, 29f);
        _doorOpen = 0;
        _barnDoor?.SetOpen(0);
        Place(0);
        // Departure can wait in the barn for many seconds: move collision circles now, not on its first drive tick.
        _t.Obstacles[_obsBody] = new Obstacle(_pos, 1.3f, Material: ImpactMaterial.Metal);
        _t.Obstacles[_obsPlough] = new Obstacle(_pos - Forward * PloughBack, 1.1f, Material: ImpactMaterial.Metal);
        _sound.Speed = 0;
    }

    // ------------------------------------------------------------ model

    void BuildModel()
    {
        _model = new Node3D { Name = "MT-Agro" };
        AddChild(_model);
        BuildMtAgro(_model, plough: true, out _driver, out _head);
    }

    /// <summary>The blue MT-Agro with its tractorist; the plough is optional (the rescue tractor has a tow rope instead).</summary>
    public static void BuildMtAgro(Node3D model, bool plough, out Node3D driver, out Node3D head, Color? bodyColor = null, bool batchStatic = false)
    {
        var blue = bodyColor ?? Props.TractorBlue;
        var rubber = Props.Rubber;
        var metal = new Color(0.3f, 0.3f, 0.32f);
        var steel = new Color(0.48f, 0.49f, 0.46f);
        var darkSteel = new Color(0.18f, 0.19f, 0.18f);
        var glass = new Color(0.25f, 0.48f, 0.55f);
        var lamp = new Color(0.96f, 0.82f, 0.48f);
        var red = new Color(0.68f, 0.12f, 0.08f);
        var blueHighlight = bodyColor.HasValue ? blue.Lightened(0.12f) : new Color(0.25f, 0.4f, 0.61f);
        var blueShade = bodyColor.HasValue ? blue.Darkened(0.12f) : new Color(0.18f, 0.3f, 0.44f);
        var archColor = bodyColor.HasValue ? blue.Lightened(0.07f) : new Color(0.24f, 0.4f, 0.6f);

        // Engine hood and lower chassis: bonnet forward (-Z), cab over the driven rear axle.
        model.AddChild(Props.Part(Props.Box(1.2f, 0.88f, 2.6f), blue, new Vector3(0, 1.38f, -0.5f)));
        model.AddChild(Props.Part(Props.Box(1.05f, 0.18f, 2.25f), blueHighlight, new Vector3(0, 1.9f, -0.48f)));
        model.AddChild(Props.Part(Props.Box(1.42f, 0.32f, 2.65f), darkSteel, new Vector3(0, 0.66f, -0.43f))); // chassis rails
        model.AddChild(Props.Part(Props.Box(1.46f, 0.16f, 1.25f), blue, new Vector3(0, 1.86f, 0.78f)));      // rear deck
        model.AddChild(Props.Part(Props.Box(1.05f, 0.38f, 0.56f), blue, new Vector3(0, 1.08f, 1.0f)));       // transmission case

        // Radiator grille, front lights, nose badge and a solid iron bumper.
        model.AddChild(Props.Part(Props.Box(1.08f, 0.64f, 0.08f), darkSteel, new Vector3(0, 1.35f, -1.84f)));
        model.AddChild(Props.Part(Props.Box(1.2f, 0.09f, 0.1f), blue, new Vector3(0, 1.73f, -1.86f)));
        for (int i = 0; i < 7; i++)
        {
            float x = -0.42f + i * 0.14f;
            model.AddChild(Props.Part(Props.Box(0.035f, 0.52f, 0.025f), steel, new Vector3(x, 1.34f, -1.9f)));
        }
        model.AddChild(Props.Part(Props.Box(0.2f, 0.14f, 0.035f), red, new Vector3(0, 1.78f, -1.91f)));
        foreach (float s in new[] { -1f, 1f })
        {
            model.AddChild(Props.Part(Props.Cyl(0.16f, 0.11f, 10), darkSteel, new Vector3(s * 0.48f, 1.59f, -1.9f), new Vector3(90, 0, 0)));
            model.AddChild(Props.Part(Props.Ball(0.115f, 8), lamp, new Vector3(s * 0.48f, 1.59f, -1.97f)));
            model.AddChild(Props.Part(Props.Box(0.12f, 0.18f, 0.1f), red, new Vector3(s * 0.59f, 1.0f, -1.84f)));
            model.AddChild(Props.Part(Props.Box(0.11f, 0.16f, 0.1f), red, new Vector3(s * 0.68f, 1.47f, 1.43f)));
        }
        model.AddChild(Props.Part(Props.Box(1.8f, 0.18f, 0.28f), steel, new Vector3(0, 0.58f, -1.98f)));
        model.AddChild(Props.Part(Props.Box(1.45f, 0.13f, 0.14f), darkSteel, new Vector3(0, 0.48f, -2.0f)));

        // Side engine louvers, access panels, fuel filler and a visible step on each side.
        foreach (float s in new[] { -1f, 1f })
        {
            model.AddChild(Props.Part(Props.Box(0.045f, 0.4f, 0.88f), blueShade, new Vector3(s * 0.615f, 1.4f, -0.65f)));
            for (int k = 0; k < 5; k++)
                model.AddChild(Props.Part(Props.Box(0.035f, 0.035f, 0.7f), steel, new Vector3(s * 0.65f, 1.28f + k * 0.065f, -0.65f)));
            model.AddChild(Props.Part(Props.Box(0.22f, 0.12f, 0.7f), steel, new Vector3(s * 0.98f, 0.53f, 0.15f)));
            model.AddChild(Props.Part(Props.Box(0.12f, 0.08f, 0.82f), darkSteel, new Vector3(s * 0.86f, 0.69f, 0.12f)));
            model.AddChild(Props.Part(Props.Cyl(0.12f, 0.1f, 8), steel, new Vector3(s * 0.43f, 1.9f, 0.66f)));
        }

        // Rear wheel arches and the heavy cab platform.
        foreach (float s in new[] { -1f, 1f })
        {
            model.AddChild(Props.Part(Props.Box(0.16f, 0.14f, 2.15f), blue, new Vector3(s * 1.04f, 1.74f, 0.88f)));
            model.AddChild(Props.Part(Props.Box(0.12f, 0.12f, 1.55f), archColor, new Vector3(s * 1.12f, 1.67f, 0.9f)));
        }

        // Road wheels with agricultural lugs, pressed rims, hubs and visible wheel nuts.
        AddWheel(model, -0.95f, 0.85f, 0.45f, 0.9f, true, rubber, darkSteel, steel);
        AddWheel(model,  0.95f, 0.85f, 0.45f, 0.9f, true, rubber, darkSteel, steel);
        AddWheel(model, -0.75f, 0.45f, 0.3f, -1.5f, false, rubber, darkSteel, steel);
        AddWheel(model,  0.75f, 0.45f, 0.3f, -1.5f, false, rubber, darkSteel, steel);

        // Enclosed cab frame, broad glazed panes, roof lip and mirrors.
        foreach (float sx in new[] { -0.7f, 0.7f })
        foreach (float sz in new[] { 0.2f, 1.4f })
            model.AddChild(Props.Part(Props.Box(0.1f, 1.3f, 0.1f), metal, new Vector3(sx, 2.6f, sz)));
        model.AddChild(Props.Part(Props.Box(1.3f, 0.72f, 0.045f), glass, new Vector3(0, 2.83f, 0.2f)));
        model.AddChild(Props.Part(Props.Box(1.3f, 0.68f, 0.045f), glass, new Vector3(0, 2.83f, 1.4f)));
        foreach (float s in new[] { -1f, 1f })
        {
            model.AddChild(Props.Part(Props.Box(0.045f, 0.68f, 0.96f), glass, new Vector3(s * 0.7f, 2.82f, 0.8f)));
            model.AddChild(Props.Part(Props.Cyl(0.035f, 0.58f, 6), metal, new Vector3(s * 0.83f, 2.55f, -0.02f), new Vector3(0, 0, 90)));
            model.AddChild(Props.Part(Props.Box(0.24f, 0.28f, 0.06f), darkSteel, new Vector3(s * 0.91f, 2.84f, -0.03f)));
        }
        model.AddChild(Props.Part(Props.Box(1.58f, 0.12f, 1.48f), blue, new Vector3(0, 3.3f, 0.8f)));
        model.AddChild(Props.Part(Props.Box(1.68f, 0.1f, 1.55f), metal, new Vector3(0, 3.23f, 0.8f)));
        foreach (float s in new[] { -1f, 1f })
        {
            model.AddChild(Props.Part(Props.Box(0.16f, 0.08f, 0.12f), lamp, new Vector3(s * 0.55f, 3.38f, 0.18f)));
            model.AddChild(Props.Part(Props.Box(0.1f, 0.08f, 0.12f), red, new Vector3(s * 0.55f, 3.38f, 1.42f)));
        }
        // Exhaust stack, muffler, seat, dashboard and steering wheel.
        model.AddChild(Props.Part(Props.Cyl(0.075f, 0.42f, 8), darkSteel, new Vector3(0.48f, 2.0f, -1.12f)));
        model.AddChild(Props.Part(Props.Cyl(0.12f, 0.62f, 8), metal, new Vector3(0.48f, 2.42f, -1.12f)));
        model.AddChild(Props.Part(Props.Cyl(0.085f, 0.12f, 8), darkSteel, new Vector3(0.48f, 2.79f, -1.12f)));
        model.AddChild(Props.Part(Props.Box(0.52f, 0.18f, 0.52f), new Color(0.22f, 0.19f, 0.16f), new Vector3(0, 2.12f, 1.05f)));
        model.AddChild(Props.Part(Props.Box(0.5f, 0.48f, 0.16f), new Color(0.22f, 0.19f, 0.16f), new Vector3(0, 2.36f, 1.25f)));
        model.AddChild(Props.Part(Props.Box(1.12f, 0.14f, 0.2f), darkSteel, new Vector3(0, 2.34f, 0.28f)));
        var steeringWheel = new TorusMesh { InnerRadius = 0.035f, OuterRadius = 0.22f, Rings = 8, RingSegments = 6 };
        model.AddChild(Props.Part(steeringWheel, darkSteel, new Vector3(0, 2.5f, 0.06f), new Vector3(22, 0, 0)));
        model.AddChild(Props.Part(Props.Cyl(0.035f, 0.12f, 8), darkSteel, new Vector3(0, 2.5f, 0.06f), new Vector3(90, 0, 0)));

        // Hydraulic lift, drawbar and rear three-point linkage.
        model.AddChild(Props.Part(Props.Box(0.18f, 0.2f, 0.9f), darkSteel, new Vector3(0, 0.62f, 1.63f)));
        foreach (float s in new[] { -1f, 1f })
        {
            model.AddChild(Props.Part(Props.Box(0.1f, 0.62f, 0.13f), steel, new Vector3(s * 0.48f, 0.73f, 1.78f), new Vector3(-24, 0, s * -9)));
            model.AddChild(Props.Part(Props.Box(0.1f, 0.16f, 0.52f), metal, new Vector3(s * 0.5f, 0.48f, 1.94f)));
        }

        // The tractorist: quilted jacket, ushanka, suspiciously red nose, bottle in hand.
        driver = new Node3D { Position = new Vector3(0, 2.0f, 1.0f) };
        model.AddChild(driver);
        var coat = new Color(0.25f, 0.28f, 0.2f);
        driver.AddChild(Props.Part(Props.Box(0.58f, 0.64f, 0.42f), coat, new Vector3(0, 0.28f, 0)));
        for (int k = 0; k < 3; k++)
            driver.AddChild(Props.Part(Props.Box(0.6f, 0.035f, 0.43f), new Color(0.19f, 0.22f, 0.17f), new Vector3(0, 0.12f + k * 0.15f, -0.018f)));
        foreach (float s in new[] { -1f, 1f })
        {
            driver.AddChild(Props.Part(Props.Cyl(0.12f, 0.42f, 6), coat, new Vector3(s * 0.31f, 0.38f, -0.08f), new Vector3(55, 0, s * 28)));
            driver.AddChild(Props.Part(Props.Ball(0.09f), new Color(0.82f, 0.62f, 0.5f), new Vector3(s * 0.39f, 0.27f, -0.3f)));
            driver.AddChild(Props.Part(Props.Box(0.19f, 0.28f, 0.28f), new Color(0.18f, 0.18f, 0.16f), new Vector3(s * 0.18f, -0.12f, 0.12f)));
        }
        head = new Node3D { Position = new Vector3(0, 0.75f, 0) };
        driver.AddChild(head);
        head.AddChild(Props.Part(Props.Ball(0.17f), new Color(0.85f, 0.65f, 0.55f), Vector3.Zero));
        head.AddChild(Props.Part(Props.Ball(0.2f), new Color(0.28f, 0.2f, 0.13f), new Vector3(0, 0.12f, 0.015f)));
        head.AddChild(Props.Part(Props.Box(0.5f, 0.08f, 0.44f), new Color(0.35f, 0.24f, 0.14f), new Vector3(0, 0.12f, -0.025f)));
        head.AddChild(Props.Part(Props.Box(0.4f, 0.07f, 0.12f), new Color(0.2f, 0.15f, 0.1f), new Vector3(0, 0.04f, -0.22f)));
        head.AddChild(Props.Part(Props.Ball(0.06f), new Color(0.9f, 0.15f, 0.1f), new Vector3(0, -0.02f, -0.17f)));
        driver.AddChild(Props.Part(Props.Cyl(0.06f, 0.32f, 6), new Color(0.2f, 0.45f, 0.25f), new Vector3(0.42f, 0.45f, -0.15f), new Vector3(18, 0, -22)));
        driver.AddChild(Props.Part(Props.Cyl(0.075f, 0.1f, 8), new Color(0.64f, 0.72f, 0.43f), new Vector3(0.42f, 0.62f, -0.15f)));

        if (!plough)
        {
            if (batchStatic) BatchStatic(model, driver);
            return;
        }
        // Plough: drawbar, cross beam and four angled shares, dug into the ground.
        model.AddChild(Props.Part(Props.Box(0.15f, 0.15f, 1.6f), metal, new Vector3(0, 0.7f, 2.3f)));
        model.AddChild(Props.Part(Props.Box(PloughWidth, 0.15f, 0.2f), metal, new Vector3(0, 0.55f, PloughBack)));
        for (int k = 0; k < 4; k++)
        {
            float x = -PloughWidth / 2f + 0.3f + k * (PloughWidth - 0.6f) / 3f;
            model.AddChild(Props.Part(Props.Box(0.1f, 0.55f, 0.6f), new Color(0.55f, 0.55f, 0.55f),
                new Vector3(x, 0.25f, PloughBack + 0.1f), new Vector3(0, 25, 0)));
            model.AddChild(Props.Part(Props.Box(0.12f, 0.08f, 0.28f), darkSteel,
                new Vector3(x, 0.08f, PloughBack + 0.3f), new Vector3(0, 25, 0)));
        }
        if (batchStatic) BatchStatic(model, driver);
    }

    /// <summary>Combines the stationary opaque tractor parts into one vertex-coloured surface.</summary>
    internal static void BatchStatic(Node3D model, Node3D driver)
    {
        foreach (var _ in BatchStaticSteps(model, driver)) { }
    }

    /// <summary>Test aid: true merges through SurfaceTool vertex by vertex (the reference the managed merge below must equal).</summary>
    internal static bool BatchReference;

    /// <summary>The managed merge welds the way SurfaceTool.Index does: equal position, normal and colour share one vertex, numbered in first-use order.</summary>
    readonly struct BatchSurface
    {
        public readonly Vector3[] Vertices, Normals; public readonly int[] Indices; public readonly Color[] Colors;
        public BatchSurface(Mesh mesh, int surface)
        {
            var arrays = mesh.SurfaceGetArrays(surface);
            Vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            Normals = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
            Indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
            var colorArray = arrays[(int)Mesh.ArrayType.Color];
            Colors = colorArray.VariantType == Variant.Type.PackedColorArray ? colorArray.AsColorArray() : Array.Empty<Color>();
        }
    }

    /// <summary>A vertex by the bits of its floats (as SurfaceTool compares them: -0 and 0 are two vertices).</summary>
    readonly struct WeldKey : IEquatable<WeldKey>
    {
        readonly int _a, _b, _c, _d, _e, _f, _g, _h, _i, _j;
        public WeldKey(Vector3 v, Vector3 n, Color c)
        {
            _a = Bits(v.X); _b = Bits(v.Y); _c = Bits(v.Z); _d = Bits(n.X); _e = Bits(n.Y); _f = Bits(n.Z);
            _g = Bits(c.R); _h = Bits(c.G); _i = Bits(c.B); _j = Bits(c.A);
        }
        public bool Equals(WeldKey o) => _a == o._a && _b == o._b && _c == o._c && _d == o._d && _e == o._e && _f == o._f && _g == o._g && _h == o._h && _i == o._i && _j == o._j;
        public override bool Equals(object obj) => obj is WeldKey k && Equals(k);
        public override int GetHashCode() => HashCode.Combine(HashCode.Combine(_a, _b, _c, _d, _e), HashCode.Combine(_f, _g, _h, _i, _j));
    }

    /// <summary>The arrays of each distinct primitive shape, read from the engine once (main thread only; a few hundred small entries).</summary>
    static readonly System.Collections.Generic.Dictionary<string, BatchSurface> PrimitiveShapes = new();

    static int Bits(float f) => BitConverter.SingleToInt32Bits(f);

    /// <summary>
    /// What PrimitiveMesh.GetMeshArrays gives (vertices, normals, indices), from the shape cache: the engine builds the mesh, uploads it and reads it back
    /// for every call (about 1 ms each on a windowed renderer) where a baked decoration asks for hundreds of boxes. Other meshes go to the engine.
    /// </summary>
    internal static Godot.Collections.Array PrimitiveArrays(PrimitiveMesh mesh)
    {
        string key = PrimitiveKey(mesh);
        if (key == null) return mesh.GetMeshArrays();
        if (!PrimitiveShapes.TryGetValue(key, out var src)) PrimitiveShapes[key] = src = new BatchSurface(mesh, 0);
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = src.Vertices;
        arrays[(int)Mesh.ArrayType.Normal] = src.Normals;
        arrays[(int)Mesh.ArrayType.Index] = src.Indices;
        return arrays;
    }

    /// <summary>The triangle corners (three per triangle, in index order) and bounds of a primitive mesh, from the shape cache and without making the engine build or read back the mesh; false for any other mesh.</summary>
    internal static bool TryPrimitiveFaces(Mesh mesh, out Vector3[] faces, out Aabb bounds)
    {
        faces = null; bounds = default;
        string key = PrimitiveKey(mesh);
        if (key == null) return false;
        if (!PrimitiveShapes.TryGetValue(key, out var src)) PrimitiveShapes[key] = src = new BatchSurface(mesh, 0);
        var idx = src.Indices;
        int n = idx.Length == 0 ? src.Vertices.Length : idx.Length;
        n -= n % 3;
        faces = new Vector3[n];
        for (int i = 0; i < n; i++) faces[i] = src.Vertices[idx.Length == 0 ? i : idx[i]];
        if (n > 0)
        {
            Vector3 lo = faces[0], hi = faces[0];
            foreach (var f in faces) { lo = lo.Min(f); hi = hi.Max(f); }
            bounds = new Aabb(lo, hi - lo);
        }
        return true;
    }

    /// <summary>A key naming a primitive mesh's whole geometry (every parameter that shapes it), or null for any other mesh.</summary>
    static string PrimitiveKey(Mesh mesh)
    {
        switch (mesh)
        {
            case BoxMesh b when !b.FlipFaces:
                return $"B{Bits(b.Size.X)},{Bits(b.Size.Y)},{Bits(b.Size.Z)},{b.SubdivideWidth},{b.SubdivideHeight},{b.SubdivideDepth}";
            case CylinderMesh c when !c.FlipFaces:
                return $"C{Bits(c.TopRadius)},{Bits(c.BottomRadius)},{Bits(c.Height)},{c.RadialSegments},{c.Rings},{c.CapTop},{c.CapBottom}";
            case SphereMesh s when !s.FlipFaces:
                return $"S{Bits(s.Radius)},{Bits(s.Height)},{s.RadialSegments},{s.Rings},{s.IsHemisphere}";
            default: return null;
        }
    }

    /// <summary>BatchStatic in slices: every item is a point where a frame may go by (a loading card keeps running). The vertices are gathered
    /// in managed arrays (one native call builds the surface, where a SurfaceTool costs three native calls per vertex) and the gathering is cut into time slices.</summary>
    internal static System.Collections.Generic.IEnumerable<int> BatchStaticSteps(Node3D model, Node3D driver)
    {
        var slice = System.Diagnostics.Stopwatch.StartNew(); var dbg = System.Diagnostics.Stopwatch.StartNew(); int dbgMiss = 0; long dbgMissTicks = 0;
        bool reference = BatchReference;
        var st = new SurfaceTool();
        if (reference) st.Begin(Mesh.PrimitiveType.Triangles);
        var outVerts = new System.Collections.Generic.List<Vector3>();
        var outNormals = new System.Collections.Generic.List<Vector3>();
        var outColors = new System.Collections.Generic.List<Color>();
        var outIndex = new System.Collections.Generic.List<int>();
        var weld = new System.Collections.Generic.Dictionary<WeldKey, int>();
        var cache = new System.Collections.Generic.Dictionary<(Mesh, int), BatchSurface>();
        var meshes = new System.Collections.Generic.List<(MeshInstance3D Instance, Transform3D Xf)>();
        void Collect(Node node, Transform3D parent)
        {
            if (node == driver) return;
            Transform3D xf = parent;
            if (node is Node3D n3d) xf *= n3d.Transform;
            if (node is MeshInstance3D mi) meshes.Add((mi, xf));
            foreach (Node child in node.GetChildren()) Collect(child, xf);
        }
        foreach (Node child in model.GetChildren()) Collect(child, Transform3D.Identity);
        foreach (var (instance, xf) in meshes)
        {
            if (instance.Mesh == null) continue;
            // A primitive mesh (box, cylinder, sphere) is read through its parameters: asking the mesh itself for its arrays would upload it to the
            // GPU and read it back (about 0.6 ms each on a windowed renderer), and a build has thousands of them but only a few hundred distinct shapes.
            string primKey = reference ? null : PrimitiveKey(instance.Mesh);      // (the SurfaceTool reference reads every mesh from the engine)
            int surfaces = primKey != null ? 1 : instance.Mesh.GetSurfaceCount();
            for (int surface = 0; surface < surfaces; surface++)
            {
                BatchSurface src;
                if (primKey != null)
                {
                    if (!PrimitiveShapes.TryGetValue(primKey, out src)) { long m0 = System.Diagnostics.Stopwatch.GetTimestamp(); PrimitiveShapes[primKey] = src = new BatchSurface(instance.Mesh, 0); dbgMiss++; dbgMissTicks += System.Diagnostics.Stopwatch.GetTimestamp() - m0; }
                }
                else if (!cache.TryGetValue((instance.Mesh, surface), out src)) cache[(instance.Mesh, surface)] = src = new BatchSurface(instance.Mesh, surface);
                var vertices = src.Vertices; var normals = src.Normals; var indices = src.Indices; var vertexColors = src.Colors;
                var mat = instance.GetSurfaceOverrideMaterial(surface) as StandardMaterial3D
                    ?? instance.MaterialOverride as StandardMaterial3D
                    ?? (primKey != null ? ((PrimitiveMesh)instance.Mesh).Material : instance.Mesh.SurfaceGetMaterial(surface)) as StandardMaterial3D;
                Color baseColor = (mat?.AlbedoColor ?? Colors.White).SrgbToLinear();
                int count = indices.Length == 0 ? vertices.Length : indices.Length;
                int[] weldOf = null;      // (a source vertex is transformed and welded once, however many triangles use it; numbered at its first use, as the per-corner walk numbers them)
                if (!reference) { weldOf = new int[vertices.Length]; Array.Fill(weldOf, -1); }
                for (int i = 0; i + 2 < count; i += 3)
                {
                    if ((i & 15) == 0 && slice.ElapsedMilliseconds > 10) { yield return 0; slice.Restart(); }
                    for (int k = 0; k < 3; k++)
                    {
                        int index = indices.Length == 0 ? i + k : indices[i + k];
                        if (reference) { BatchVertex(st, xf, vertices, normals, vertexColors, baseColor, index); continue; }
                        int w = weldOf[index];
                        if (w < 0)
                        {
                            var n = (xf.Basis * normals[index]).Normalized();
                            var c = baseColor * (vertexColors.Length > index ? vertexColors[index] : Colors.White);
                            var v = xf * vertices[index];
                            var wk = new WeldKey(v, n, c);
                            if (!weld.TryGetValue(wk, out w)) { w = outVerts.Count; weld[wk] = w; outVerts.Add(v); outNormals.Add(n); outColors.Add(c); }
                            weldOf[index] = w;
                        }
                        outIndex.Add(w);
                    }
                }
            }
        }
        yield return 0;
        if (OS.HasEnvironment("PROPTIME") && dbg.ElapsedMilliseconds > 25) GD.Print($"  batch {model.Name}: {meshes.Count} parts, {outVerts.Count} verts, gather {dbg.ElapsedMilliseconds} ms of which {dbgMiss} new shapes {dbgMissTicks * 1000 / System.Diagnostics.Stopwatch.Frequency} ms");
        Mesh merged;
        if (reference) { st.Index(); merged = st.Commit(); }
        else
        {
            var am = new ArrayMesh();
            if (outIndex.Count > 0)
            {
                var arrays = new Godot.Collections.Array();
                arrays.Resize((int)Mesh.ArrayType.Max);
                arrays[(int)Mesh.ArrayType.Vertex] = outVerts.ToArray();
                arrays[(int)Mesh.ArrayType.Normal] = outNormals.ToArray();
                arrays[(int)Mesh.ArrayType.Color] = outColors.ToArray();
                arrays[(int)Mesh.ArrayType.Index] = outIndex.ToArray();
                am.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            }
            merged = am;
        }
        yield return 0;
        foreach (Node child in model.GetChildren())
            if (child != driver) { model.RemoveChild(child); child.Free(); }
        model.AddChild(new MeshInstance3D
        {
            Name = "BatchedMTAgro",
            Mesh = merged,
            MaterialOverride = new StandardMaterial3D
            {
                VertexColorUseAsAlbedo = true,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.PerVertex,
                Roughness = 1f,
                SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    static void BatchVertex(SurfaceTool st, Transform3D xf, Vector3[] vertices, Vector3[] normals, Color[] vertexColors, Color baseColor, int index)
    {
        st.SetNormal((xf.Basis * normals[index]).Normalized());
        st.SetColor(baseColor * (vertexColors.Length > index ? vertexColors[index] : Colors.White));
        st.AddVertex(xf * vertices[index]);
    }

    static void AddWheel(Node3D model, float side, float radius, float width, float z, bool rear,
        Color rubber, Color darkSteel, Color steel)
    {
        float y = radius;
        int lugs = rear ? 14 : 10;
        model.AddChild(Props.Part(Props.Cyl(radius, width, 12), rubber, new Vector3(side, y, z), new Vector3(0, 0, 90)));
        for (int i = 0; i < lugs; i++)
        {
            float a = i * MathF.Tau / lugs;
            var pos = new Vector3(side, y + MathF.Cos(a) * radius * 0.93f, z + MathF.Sin(a) * radius * 0.93f);
            float lugWidth = rear ? 0.23f : 0.16f;
            model.AddChild(Props.Part(Props.Box(width * 0.92f, lugWidth, rear ? 0.27f : 0.19f), darkSteel,
                pos, new Vector3(Mathf.RadToDeg(a), 0, 0)));
        }
        foreach (float s in new[] { -1f, 1f })
        {
            float face = side + s * (width * 0.5f + 0.025f);
            model.AddChild(Props.Part(Props.Cyl(radius * (rear ? 0.48f : 0.54f), 0.08f, 10), steel,
                new Vector3(face, y, z), new Vector3(0, 0, 90)));
            float outer = face + s * 0.05f;
            model.AddChild(Props.Part(Props.Cyl(radius * 0.22f, 0.06f, 8), darkSteel,
                new Vector3(outer, y, z), new Vector3(0, 0, 90)));
            int bolts = rear ? 8 : 6;
            for (int b = 0; b < bolts; b++)
            {
                float a = b * MathF.Tau / bolts;
                model.AddChild(Props.Part(Props.Ball(0.035f), new Color(0.72f, 0.68f, 0.52f),
                    new Vector3(outer + s * 0.045f, y + MathF.Cos(a) * radius * 0.32f, z + MathF.Sin(a) * radius * 0.32f)));
            }
        }
    }

    // ------------------------------------------------------------ driving

    Vector2 Forward => new(-MathF.Sin(_yaw), -MathF.Cos(_yaw));

    void PickGoal()
    {
        float w = _t.WorldSize;
        _goal = new Vector2(_rng.RandfRange(Edge + 10, w - Edge - 10), _rng.RandfRange(Edge + 10, w - Edge - 10));
    }

    public void Tick(float dt, bool raceActive = true, SovietBus bus = null)
    {
        if (_departureStage != 3)
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
                // Let a bus already at the barn go by before committing to the crossing.
                if (bus != null && bus.Position2D.DistanceTo(new Vector2(_start.X, 51f)) < 18f) return;
                _departureStage = 2;
            }
            float nextSpeed = Mathf.MoveToward(_speed, 2.8f, 1.3f * dt);
            Vector2 next = _pos + Vector2.Down * (nextSpeed * dt);
            if (bus?.Blocks(next, 1.3f) == true || bus?.Blocks(next - Forward * PloughBack, 1.1f) == true
                || GulagTruck.BlocksAt(next, 1.3f) || GulagTruck.BlocksAt(next - Forward * PloughBack, 1.1f))
                nextSpeed = 0;
            else _pos = next;
            _speed = nextSpeed;
            _t0 += dt;
            Place(dt);
            _t.Obstacles[_obsBody] = new Obstacle(_pos, 1.3f, Material: ImpactMaterial.Metal);
            _t.Obstacles[_obsPlough] = new Obstacle(_pos - Forward * PloughBack, 1.1f, Material: ImpactMaterial.Metal);
            _sound.Speed = _speed;
            if (_pos.Y >= _start.Y + 26f)
            {
                _departureStage = 3;
                _goal = new Vector2(_start.X + 15f, 145f);
            }
            return;
        }
        _t0 += dt;

        // Throttle: wanders between speeds, sometimes dozes off at the wheel.
        _speedTimer -= dt;
        if (_speedTimer <= 0)
        {
            _speedTimer = _rng.RandfRange(2f, 6f);
            _targetSpeed = _rng.RandfRange(1.4f, 4.2f);
            if (_rng.Randf() < 0.12f) _napTimer = _rng.RandfRange(2f, 5f);
        }
        float target = _targetSpeed;
        if (_napTimer > 0) { _napTimer -= dt; target = 0; }
        _speed = Mathf.MoveToward(_speed, target, 1.2f * dt);

        // Where he means to go ...
        Vector2 toGoal = _goal - _pos;
        if (toGoal.Length() < 8f) { PickGoal(); toGoal = _goal - _pos; }
        Vector2 want = toGoal.Normalized();
        // ... what he still sees in time (barns, silos, the statue, trees) ...
        foreach (var (p, r) in _avoid)
        {
            Vector2 d = _pos - p;
            float range = r + (r > 1f ? 9f : 3f);
            float dist = d.Length();
            if (dist < range && dist > 0.01f) want += d / dist * (range - dist) / range * 2.5f;
        }
        float w = _t.WorldSize;
        if (_pos.X < Edge) want.X += 2f;
        if (_pos.X > w - Edge) want.X -= 2f;
        if (_pos.Y < Edge) want.Y += 2f;
        if (_pos.Y > w - Edge) want.Y -= 2f;

        // ... and how he actually steers: slow to react, weaving, drifting off to one side.
        float desiredYaw = MathF.Atan2(-want.X, -want.Y);
        float err = Mathf.AngleDifference(_yaw, desiredYaw);
        _drift = Math.Clamp(_drift * 0.995f + _rng.RandfRange(-1f, 1f) * dt * 1.5f, -0.5f, 0.5f);
        float weave = MathF.Sin(_t0 * 0.9f) * 0.45f + MathF.Sin(_t0 * 2.3f + 1f) * 0.2f;
        float steer = Math.Clamp(err * 0.6f, -0.5f, 0.5f) + weave + _drift;
        _yaw += steer * dt * Math.Min(1f, 0.2f + _speed * 0.35f);
        Vector2 proposed = _pos + Forward * _speed * dt;
        if (bus?.Blocks(proposed + Forward * 0.2f, 1.3f) == true
            || bus?.Blocks(proposed - Forward * PloughBack, 1.1f) == true
            || GulagTruck.BlocksAt(proposed + Forward * 0.2f, 1.3f) || GulagTruck.BlocksAt(proposed - Forward * PloughBack, 1.1f)) _speed = 0;
        else _pos = proposed;
        _pos = new Vector2(Math.Clamp(_pos.X, 12f, w - 12f), Math.Clamp(_pos.Y, 12f, w - 12f));

        Plough(dt);
        Exhaust(dt);
        Place(dt);

        _t.Obstacles[_obsBody] = new Obstacle(_pos + Forward * 0.2f, 1.3f, Material: ImpactMaterial.Metal);
        _t.Obstacles[_obsPlough] = new Obstacle(_pos - Forward * PloughBack, 1.1f, Material: ImpactMaterial.Metal);
        _sound.Speed = _speed;
        _sound.Dozing = Dozing;
    }

    void Place(float dt)
    {
        float y = _t.MeshHeight(_pos.X, _pos.Y, out Vector3 n);
        var b = new Basis(Vector3.Up, _yaw);
        Vector3 axis = Vector3.Up.Cross(n);
        if (axis.LengthSquared() > 1e-6f) b = new Basis(axis.Normalized(), Vector3.Up.AngleTo(n)) * b;
        // Loose front axle and a lot of vodka: the whole thing sways.
        b = b * new Basis(Vector3.Back, MathF.Sin(_t0 * 1.7f) * 0.04f * Math.Min(1f, _speed));
        _model.GlobalTransform = new Transform3D(b, new Vector3(_pos.X, y, _pos.Y));

        float nod = Dozing ? 0.5f : MathF.Sin(_t0 * 2.1f) * 0.25f;
        _head.Rotation = new Vector3(nod, MathF.Sin(_t0 * 0.7f) * 0.4f, MathF.Sin(_t0 * 1.3f) * 0.3f);
        _driver.Rotation = new Vector3(0, 0, MathF.Sin(_t0 * 1.1f) * 0.15f);
    }

    // ------------------------------------------------------------ plough

    void Plough(float dt)
    {
        if (_speed < 0.2f) return;
        Vector2 fwd = Forward, side = new(-fwd.Y, fwd.X);
        Vector2 c = _pos - fwd * PloughBack;

        // Physics: tiles under the shares become ploughed field.
        for (float o = -PloughWidth / 2f; o <= PloughWidth / 2f + 0.01f; o += 0.6f)
        {
            Vector2 p = c + side * o;
            int ix = (int)(p.X / _t.Tile), iz = (int)(p.Y / _t.Tile);
            if (ix >= 0 && iz >= 0 && ix < _t.N && iz < _t.N) _t.Surf[iz * _t.N + ix] = SurfaceType.Field;
        }

        // Clods flicked off the shares.
        _clodAcc += _speed * dt * 3f;
        while (_clodAcc >= 1f)
        {
            _clodAcc -= 1f;
            Vector2 p = c + side * _rng.RandfRange(-PloughWidth / 2f, PloughWidth / 2f);
            float gy = _t.MeshHeight(p.X, p.Y);
            Vector3 v = new Vector3(side.X, 0, side.Y) * _rng.RandfRange(0.5f, 1.8f) + Vector3.Up * _rng.RandfRange(1.5f, 3f);
            _fx.Clod(new Vector3(p.X, gy + 0.2f, p.Y), v, _rng.RandfRange(0.18f, 0.3f));
        }

        // Visual furrows: a strip of turned soil with dark furrow lines.
        if (_lastMark is not Vector2 last || last.DistanceTo(c) > 4f) { _lastMark = c; return; }
        if (last.DistanceTo(c) < 0.8f) return;
        Vector2 dir = (c - last).Normalized(), s = new(-dir.Y, dir.X);
        int o6 = _segHead * 12;
        AddQuad(o6, last, c, s, -PloughWidth / 2f, PloughWidth / 2f, 0.045f, Soil);
        AddQuad(o6 + 6, last, c, s, -0.12f, 0.12f, 0.055f, Furrow);
        _segHead = (_segHead + 1) % MaxSegments;
        _segCount = Math.Min(_segCount + 1, MaxSegments);
        // Second pair of furrow lines as their own segment slots keeps the arrays simple.
        int o7 = _segHead * 12;
        AddQuad(o7, last, c, s, -0.75f, -0.55f, 0.055f, Furrow);
        AddQuad(o7 + 6, last, c, s, 0.55f, 0.75f, 0.055f, Furrow);
        _segHead = (_segHead + 1) % MaxSegments;
        _segCount = Math.Min(_segCount + 1, MaxSegments);
        _lastMark = c;
        _dirty = true;
    }

    void AddQuad(int o, Vector2 a, Vector2 b, Vector2 side, float s0, float s1, float lift, Color col)
    {
        Vector3 G(Vector2 p) => new(p.X, _t.MeshHeight(p.X, p.Y) + lift, p.Y);
        Vector3 a0 = G(a + side * s0), a1 = G(a + side * s1), b0 = G(b + side * s0), b1 = G(b + side * s1);
        _v[o] = a0; _v[o + 1] = a1; _v[o + 2] = b1;
        _v[o + 3] = a0; _v[o + 4] = b1; _v[o + 5] = b0;
        for (int k = 0; k < 6; k++) _c[o + k] = col;
    }

    /// <summary>The stack (the model's own space) and the engine's size against the player's car (also the slurry and rescue tractors' pipe).</summary>
    internal static readonly Vector3 SootPipe = new(0.4f, 2.95f, -1.2f);
    internal const float SootPower = 1.2f;

    void Exhaust(float dt)
    {
        Vector3 pipe = _model.GlobalTransform * new Vector3(0.4f, 2.95f, -1.2f);
        if (!Dozing)      // the player's diesel smoke, a little bigger engine
        {
            Vector3 fwd = -_model.GlobalTransform.Basis.Z.Normalized();
            _fx.EngineSoot(pipe, -fwd, fwd * _speed, Mathf.Clamp(_speed / 4f, 0.25f, 1f), SootPower, ref _soot, dt);
        }
        _hicTimer -= dt;
        if (_hicTimer <= 0)
        {
            // Hic! A fat black cloud and a lurch.
            _hicTimer = _rng.RandfRange(4f, 10f);
            for (int k = 0; k < 4; k++)
                _fx.Smoke(pipe + Vector3.Up * k * 0.3f, Vector3.Up * 2f, new Color(0.08f, 0.08f, 0.08f), 0.8f, 0.6f, 2.4f, 1.6f);
            _yaw += _rng.RandfRange(-0.25f, 0.25f);
            _sound.Cough();
        }
    }

    public override void _Process(double delta)
    {
        if (!_dirty) return;
        _dirty = false;
        _furrowMesh.ClearSurfaces();
        if (_segCount == 0) return;
        int verts = _segCount * 12;
        var v = new Vector3[verts];
        var c = new Color[verts];
        Array.Copy(_v, v, verts);
        Array.Copy(_c, c, verts);
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = v;
        arrays[(int)Mesh.ArrayType.Color] = c;
        _furrowMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
    }
}
