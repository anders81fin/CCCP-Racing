using System;
using System.Collections.Generic;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Track;

/// <summary>Low-poly kolkhoz scenery built from primitive meshes.</summary>
public static partial class Props
{
    static readonly Dictionary<Color, StandardMaterial3D> Materials = new();

    public static StandardMaterial3D Mat(Color c)
    {
        if (!Materials.TryGetValue(c, out var m))
        {
            m = new StandardMaterial3D
            {
                AlbedoColor = c,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.PerVertex,
                Roughness = 1f,
                SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
            };
            Materials[c] = m;
        }
        return m;
    }

    static readonly Color BirchBark = new(0.88f, 0.87f, 0.82f);
    static readonly Color BirchMark = new(0.28f, 0.27f, 0.25f);
    static readonly Color BirchLeaf = new(0.36f, 0.52f, 0.24f);
    static readonly Color BirchLeafDark = new(0.28f, 0.42f, 0.2f);
    static readonly Color AutumnLeaf = new(0.86f, 0.68f, 0.14f), AutumnLeafOrange = new(0.82f, 0.42f, 0.1f), AutumnLeafGreen = new(0.62f, 0.62f, 0.2f);
    static readonly Color Wood = new(0.33f, 0.24f, 0.16f);
    static readonly Color Timber = new(0.3f, 0.21f, 0.13f);
    static readonly Color BarnWall = new(0.46f, 0.2f, 0.14f);
    static readonly Color BarnPlank = new(0.55f, 0.27f, 0.19f);
    static readonly Color Roof = new(0.32f, 0.32f, 0.34f);
    static readonly Color Concrete = new(0.68f, 0.67f, 0.62f);
    static readonly Color SovietRed = new(0.72f, 0.1f, 0.08f);
    static readonly Color Gold = new(0.9f, 0.75f, 0.2f);
    internal static readonly Color TractorBlue = new(0.2f, 0.34f, 0.58f);
    internal static readonly Color Rubber = new(0.1f, 0.1f, 0.1f);
    static readonly Color Granite = new(0.3f, 0.3f, 0.32f);
    static readonly Color Bronze = new(0.34f, 0.4f, 0.34f);
    static readonly Color Hay = new(0.82f, 0.7f, 0.34f);
    static readonly Color SpruceLeaf = new(0.13f, 0.27f, 0.16f);
    static readonly Color SpruceLeafLight = new(0.17f, 0.33f, 0.19f);
    static readonly Color PineBark = new(0.56f, 0.3f, 0.18f);
    static readonly Color PineBarkLow = new(0.36f, 0.27f, 0.21f);
    static readonly Color PineLeaf = new(0.21f, 0.34f, 0.19f);
    static readonly Color RedNeedles = new(0.58f, 0.24f, 0.1f), RedNeedlesLight = new(0.7f, 0.36f, 0.14f);
    static readonly Color Wire = new(0.1f, 0.1f, 0.1f);

    /// <summary>Wire attachment points on a pole (local): both crossarm ends and the pole top.</summary>
    internal static readonly Vector3[] PoleWireAttach = { new(-0.82f, 6.7f, 0), new(0.82f, 6.7f, 0), new(0, 7.02f, 0) };

    static readonly Color Snow = new(0.95f, 0.96f, 0.99f);
    static readonly Color SnowShade = new(0.84f, 0.88f, 0.95f);
    static readonly Color BareTwigs = new(0.3f, 0.26f, 0.24f);

    public static Node3D Build(TrackData t)
    {
        Node3D result = null;
        foreach (var step in BuildSteps(t)) result = step ?? result;
        return result;
    }

    /// <summary>
    /// The same build in slices (a null between them: let a frame go by, so a loading card and the music keep running);
    /// the last item is the finished Props node. Build runs all the slices at once.
    /// </summary>
    public static System.Collections.Generic.IEnumerable<Node3D> BuildSteps(TrackData t)
    {
        bool winter = t.Winter, autumn = t.Autumn;
        var slice = System.Diagnostics.Stopwatch.StartNew();
        var root = new Node3D { Name = "Props" };
        bool handedOver = false;
        try {      // (a build abandoned between slices frees the half-made props)
        int i = 0;
        var propTime = OS.HasEnvironment("PROPTIME") ? new System.Collections.Generic.Dictionary<string, (long ticks, int n)>() : null;     // dev: where the build time goes
        var propClock = System.Diagnostics.Stopwatch.StartNew();
        foreach (var p in t.Props)
        {
            long propStart = propClock.ElapsedTicks;
            // Per-tree variety from the position; the size comes from placement (obstacle radius).
            int hx = (int)(p.Position.X * 10f), hz = (int)(p.Position.Z * 10f);
            float TreeSize(float lo, float hi) => p.Size > 0 ? p.Size : lo + (hi - lo) * Noise.Hash01(hx, hz, 5);
            Node3D n = p.Type switch
            {
                "birch" => Birch(TreeSize(0.8f, 1.2f), hx, hz, winter, autumn),
                "spruce" => Spruce(TreeSize(0.6f, 1.3f), hx, hz, winter),
                "pine" => Pine(TreeSize(0.7f, 1.3f), hx, hz, winter),
                "redpine" => Pine(TreeSize(0.7f, 1.3f), hx, hz, false, true),
                "turbinehall" => Atomgrad.TurbineHall(),
                "reactor" => Atomgrad.ReactorBlock(p.Text),
                "sarcophagus" => Atomgrad.Sarcophagus(),
                "stack" => Atomgrad.Stack(),
                "pipebridge" => Atomgrad.PipeBridge(float.Parse(p.Text ?? "16", System.Globalization.CultureInfo.InvariantCulture)),
                "apartments" => Atomgrad.Apartments(p.Text),
                "ferriswheel" => Atomgrad.FerrisWheel(),
                "culture" => Atomgrad.PalaceOfCulture(),
                "bumpercars" => Atomgrad.BumperCars(),
                "checkpoint" => Atomgrad.Checkpoint(),
                "radsign" => Atomgrad.RadSign(p.Text),
                "wash" => Atomgrad.WashStation(),
                "wreck" => Atomgrad.Wreck(int.Parse(p.Text ?? "0")),
                "railyard" => Atomgrad.RailYard(t, p.Position, p.Rotation),
                "rails" => Atomgrad.Rails(float.Parse(p.Text ?? "100", System.Globalization.CultureInfo.InvariantCulture), t, p.Position, p.Rotation),
                "crossing" => Atomgrad.CrossingSign(),
                "pole" => Pole(p.Text == "transformer"),
                "streetlamp" => VillagePower.StreetLamp(),
                "gaiservice" => new Node3D(),
                "powerconsumer" => new Node3D(),
                "powercabinet" => VillagePower.Cabinet(p.Text == "bonus main distribution"),
                "barn" => Barn(t.Id == "ilyich"),
                "silo" => t.Id == "bonus" ? Buildings.RuinedSilo() : Silo(t, p.Position),
                "bigbarn" => Village.BigBarn(t, p.Position, p.Rotation, t.Props.Exists(d => d.Type == "dormitory"
                    && Village.DormitoryJoinsBarn(p, d.Position, d.Rotation))),
                "dormitory" => Village.Dormitory(t, p.Position, p.Rotation, Num(p.Text, 0, 84f)),
                "tractor" => Tractor(),
                "statue" => Statue(t.Id == "ilyich"),
                "billboard" => Billboard(p.Text ?? "GLORY TO LABOUR!"),
                "hay" => HayBale(hx * 31 + hz),
                "startgate" => StartGate(p.Size, t.Winter),
                "startbarn" => Village.StartBarn(p.Size, Num(p.Text, 0, 22f), Num(p.Text, 1, 6f), p.Rotation, Num(p.Text, 2, 0.03f), LineRise(p.Text, 4), Num(p.Text, 3, 0f)),
                "church" => Village.Church(),
                "stonepost" => Village.StoneGatePost(),
                "partydacha" => PartyDacha.Building(t,p.Position,p.Rotation),
                "izba" => Village.Izba(hx * 31 + hz),
                "wheatfield" => Village.WheatField(Num(p.Text, 0, 24f), Num(p.Text, 1, 30f), hx * 31 + hz, new Vector2(p.Position.X, p.Position.Z), p.Rotation, KolkhozWorkers.Layout(t), t, p.Position.Y),
                "drunktractor" => new Node3D(),        // spawn point only; RaceScene drives the real one
                "hangar" => Airbase.Hangar(t.Id == "siberia"),
                "balalaika" => Airbase.Balalaika(),
                "hind" => HindSorties.BuildModel(),
                "helipad" => BonusAirbase.BuildPad(t, p.Position, p.Rotation),
                "fieldtent" => BonusFieldCamp.Tent(p.Text),
                "fieldtruck" => BonusFieldCamp.Truck(),
                "fieldnet" => BonusFieldCamp.Net(),
                "fieldfuel" => BonusFieldCamp.Fuel(),
                "fieldgenerator" => BonusFieldCamp.Generator(),
                "fieldperimeter" => BonusFieldCamp.Perimeter(t, p.Position, p.Rotation),
                "windsock" => Airbase.Windsock(),
                "busstop" => BusShelter.Build(),
                "barracks" => Buildings.Barracks(t.Id == "siberia"),
                "flagpole" => Buildings.FlagPole(),
                "ruinbarn" => Buildings.RuinedBarn(),
                "deadtree" => Buildings.DeadTree(0.8f + 0.5f * Noise.Hash01(i, 9, 3)),
                "radar" => Buildings.Radar(t.Id == "siberia"),
                "buk21" => new Node3D(), // dynamic launcher models belong to HindSorties
                "lorrywreck" => LogPiles.LorryWreck(),
                _ => null,
            };
            if (n == null) { GD.PushWarning($"Unknown prop {p.Type}"); i++; continue; }
            if (p.Type == "statue" && t.Id == "ilyich") n.Scale = Vector3.One * Village.MonumentScale;
            if (t.PowerAnchors.TryGetValue(i, out var anchor) && p.Type != "streetlamp")
                n.AddChild(VillagePower.ServiceMast(p.Type, anchor));
            if (t.CableEntries.TryGetValue(i, out var entry))
                n.AddChild(VillagePower.CableEntry(entry, p.Type == "pole"));
            n.SetMeta("prop", i++);
            // Buildings fade see-through while the car is behind them (SeeThrough).
            if (p.Type is "barn" or "bigbarn" or "dormitory" or "silo" or "church" or "izba" or "hangar" or "barracks" or "ruinbarn" or "radar"
                or "partydacha" or "turbinehall" or "reactor" or "sarcophagus" or "stack" or "apartments" or "ferriswheel" or "culture" or "wash")
            {
                n.Name = $"{p.Type}{i}";
                n.AddChild(new SeeThrough { Name = "SeeThrough" });
            }
            n.Position = p.Position;
            n.Rotation = new Vector3(p.Type is "birch" or "pine" or "redpine" ? Lean(hx, hz) : 0, p.Rotation, 0);
            root.AddChild(n);
            if (propTime != null && (propClock.ElapsedTicks - propStart) * 1000 / System.Diagnostics.Stopwatch.Frequency >= 30) GD.Print($"  slow prop #{i - 1} {p.Type}: {(propClock.ElapsedTicks - propStart) * 1000 / System.Diagnostics.Stopwatch.Frequency} ms");
            if (propTime != null) { var was = propTime.GetValueOrDefault(p.Type); propTime[p.Type] = (was.ticks + propClock.ElapsedTicks - propStart, was.n + 1); }
            if (slice.ElapsedMilliseconds > 10) { LoadProf.At(); yield return null; slice.Restart(); }
        }
        if (propTime != null)
            foreach (var kv in propTime) GD.Print($"  prop time {kv.Key,-14} x{kv.Value.n,-3} {kv.Value.ticks * 1000 / System.Diagnostics.Stopwatch.Frequency,5} ms");
        root.AddChild(Village.Yards(t));
        root.AddChild(Atomgrad.RoadMarkings(t));
        LoadProf.At(); yield return null;
        slice.Restart();
        root.AddChild(BonusRouteGuides.Build(t));
        LoadProf.At(); yield return null;
        slice.Restart();
        root.AddChild(MaintenanceHall.Build(t));
        LoadProf.At(); yield return null;
        slice.Restart();
        root.AddChild(Village.MonumentSquare(t));
        LoadProf.At(); yield return null;
        slice.Restart();
        root.AddChild(VillageLife.Build(t));
        LoadProf.At(); yield return null;
        slice.Restart();
        foreach (var part in IlyichEdgeSceneryParts(t)) { if (part == null) { LoadProf.At(); yield return null; slice.Restart(); } else root.AddChild(part); }
        LoadProf.At(); yield return null;
        slice.Restart();
        root.AddChild(GrainDelivery.Build(t));
        LoadProf.At(); yield return null;
        slice.Restart();
        foreach (var part in PartyDacha.GroundsSteps(t)) { if (part == null) { LoadProf.At(); yield return null; slice.Restart(); } else root.AddChild(part); }
        LoadProf.At(); yield return null;
        slice.Restart();
        root.AddChild(PartyRowboat.Build(t));
        LoadProf.At(); yield return null;
        slice.Restart();
        root.AddChild(PartyPondDuck.Build(t));
        LoadProf.At(); yield return null;
        slice.Restart();
        root.AddChild(CameraSecrets.Build(t));
        LoadProf.At(); yield return null;
        slice.Restart();
        root.AddChild(KolkhozWorkers.Build(t));
        LoadProf.At(); yield return null;
        slice.Restart();
        root.AddChild(FordGuides.Build(t));
        LoadProf.At(); yield return null;
        slice.Restart();
        root.AddChild(PowerLines(t));
        LoadProf.At(); yield return null;
        slice.Restart();
        if (t.Id == "siberia") root.AddChild(SiberiaRadar.BuildGrounds(t));
        handedOver = true;
        yield return root;
        } finally { if (!handedOver) root.Free(); }
    }

    /// <summary>The k-th number in a prop's "a;b" text (the start barn's length and line), or the fallback.</summary>
    /// <summary>The ground heights along a painted line (the entries of the text from k on, one per sample across the road), or null.</summary>
    static float[] LineRise(string text, int k)
    {
        var parts = (text ?? "").Split(';');
        if (parts.Length - k < 2) return null;
        var v = new float[parts.Length - k];
        for (int i = 0; i < v.Length; i++)
            if (!float.TryParse(parts[k + i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v[i])) return null;
        return v;
    }

    static float Num(string text, int k, float fallback)
    {
        var parts = (text ?? "").Split(';');
        return k < parts.Length && float.TryParse(parts[k], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : fallback;
    }

    /// <summary>A few trees lean a little (radians, about the local X axis after the yaw).</summary>
    static float Lean(int hx, int hz)
    {
        float u = Noise.Hash01(hx, hz, 17);
        return u < 0.7f ? 0f : Mathf.DegToRad((u - 0.7f) / 0.3f * 9f);
    }

    /// <summary>Car collision radius of a tree trunk of the given type and size.</summary>
    public static float TreeObstacleRadius(string type, float size) => type switch
    {
        "spruce" => 0.2f + 0.4f * size,           // the skirt of branches reaches the ground
        "pine" or "redpine" => 0.18f + 0.17f * size,
        _ => 0.21f + 0.14f * size,
    };

    /// <summary>
    /// Sagging wires between consecutive poles of a power line: one node per span (meta "poles" =
    /// the two prop indices) so a span can drop with its pole.
    /// </summary>
    static Node3D PowerLines(TrackData t)
    {
        var root = new Node3D { Name = "PowerLines" };
        var mat = new StandardMaterial3D { AlbedoColor = Wire, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        const int Segs = 8;
        foreach (var (a, b) in t.PowerSpans)
        {
            PropInstance pa = t.Props[a], pb = t.Props[b];
            var ba = new Basis(Vector3.Up, pa.Rotation);
            var bb = new Basis(Vector3.Up, pb.Rotation);
            float len = new Vector2(pa.Position.X - pb.Position.X, pa.Position.Z - pb.Position.Z).Length();
            int wires = WireCount(t, a, b);
            var verts = new Vector3[wires * Segs * 2];
            int v = 0;
            for (int w = 0; w < wires; w++)
            {
                Vector3 from = ba * WireAttachment(t, a, w);
                Vector3 to = pb.Position - pa.Position + bb * WireAttachment(t, b, w);
                float sag = WireSag(a, w, len);
                Vector3 prev = from;
                for (int k = 1; k <= Segs; k++)
                {
                    float f = k / (float)Segs;
                    Vector3 cur = from.Lerp(to, f) - new Vector3(0, 4f * sag * f * (1 - f), 0);
                    verts[v++] = prev;
                    verts[v++] = cur;
                    prev = cur;
                }
            }
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = verts;
            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, arrays);
            var mi = new MeshInstance3D
            {
                Mesh = mesh,
                MaterialOverride = mat,
                Position = pa.Position,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            mi.SetMeta("poles", new Vector2I(a, b));
            root.AddChild(mi);
        }
        return root;
    }

    internal static int WireCount(TrackData t, int a, int b) =>
        t.PowerAnchors.ContainsKey(a) || t.PowerAnchors.ContainsKey(b) ? 1 : PoleWireAttach.Length;

    internal static Vector3 WireAttachment(TrackData t, int prop, int wire) =>
        t.PowerAnchors.TryGetValue(prop, out var anchor) ? anchor : PoleWireAttach[wire];

    /// <summary>Sag (m) of wire w in the span starting at pole prop a.</summary>
    internal static float WireSag(int a, int w, float len) => len / 24f * (0.45f + 0.3f * Noise.Hash01(a, w, 23));

    /// <summary>
    /// Marks a prop that topples when broken (Breakables): height of its top (crown, crossarm) above
    /// the base, how high that top rests once it lies down, and how thick the fallen trunk is.
    /// </summary>
    static Node3D Fells(Node3D n, float top, float rest, float log)
    {
        n.SetMeta("top", top);
        n.SetMeta("rest", rest);
        n.SetMeta("log", log);
        return n;
    }

    /// <summary>The merged meshes of identical objects (the poles of a line, the bale variants), built once and shared: a new instance costs nothing.</summary>
    static readonly Dictionary<string, (Mesh mesh, Material material)> BatchCache = new();

    /// <summary>Lets go of the shared meshes (the scene leaves the tree: a quit must not find resources still held by a static).</summary>
    public static void ReleaseCache() => BatchCache.Clear();

    /// <summary>
    /// A merged mesh node for an object whose stationary parts never change: build(), which fills a node and merges it
    /// (DrunkTractor.BatchStatic), runs only the first time for this key; later calls return a new node on the same mesh and material.
    /// </summary>
    static MeshInstance3D BatchedCopy(string key, Func<Node3D> build)
    {
        if (!BatchCache.TryGetValue(key, out var shared))
        {
            var holder = build();
            foreach (var child in holder.GetChildren())
                if (child is MeshInstance3D { Mesh: not null } mi) { shared = (mi.Mesh, mi.MaterialOverride); break; }
            holder.Free();
            BatchCache[key] = shared;
        }
        return new MeshInstance3D { Mesh = shared.mesh, MaterialOverride = shared.material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
    }

    internal static MeshInstance3D Part(Mesh mesh, Color c, Vector3 pos, Vector3? rot = null)
    {
        return new MeshInstance3D
        {
            Mesh = mesh,
            MaterialOverride = Mat(c),
            Position = pos,
            RotationDegrees = rot ?? Vector3.Zero,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    internal static BoxMesh Box(float x, float y, float z) => new() { Size = new Vector3(x, y, z) };

    internal static CylinderMesh Cyl(float r, float h, int seg = 8, float top = -1) =>
        new() { TopRadius = top < 0 ? r : top, BottomRadius = r, Height = h, RadialSegments = seg, Rings = 1 };

    internal static SphereMesh Ball(float r, int seg = 6) =>
        new() { Radius = r, Height = r * 2, RadialSegments = seg, Rings = 3 };

    /// <summary>
    /// Birch of size 0.6 (slim sapling) .. 1.5 (big old tree); trunk height and crown size vary
    /// independently from the hash, with broken bark marks, forked branches and a fuller crown.
    /// </summary>
    static Node3D Birch(float size, int hx, int hz, bool winter = false, bool autumn = false)
    {
        var n = new Node3D();
        float h = 6f * size * (0.8f + 0.45f * Noise.Hash01(hx, hz, 11));       // trunk height
        float c = size * (0.7f + 0.6f * Noise.Hash01(hx, hz, 13));             // crown scale
        float r = 0.14f * MathF.Pow(size, 1.3f);                               // trunk radius
        n.AddChild(Part(Cyl(r, h, 5, r * 0.7f), BirchBark, new Vector3(0, h * 0.5f, 0)));
        int bands = h > 5f ? 4 : 3;
        for (int k = 0; k < bands; k++)
        {
            float a = k * 2.399f + Noise.Hash01(hx + k, hz, 29) * 0.45f;
            float y = h * (0.13f + k * 0.62f / bands);
            float markW = r * (0.9f + 0.65f * Noise.Hash01(hx, hz + k, 31));
            float markH = 0.06f + 0.06f * Noise.Hash01(hx + k, hz + k, 33);
            var at = new Vector3(MathF.Sin(a) * r * 0.82f, y, MathF.Cos(a) * r * 0.82f);
            n.AddChild(Part(Box(markW, markH, 0.025f), BirchMark, at, new Vector3(0, Mathf.RadToDeg(a), 0)));
        }
        if (!winter) n.AddChild(TreeBeams(BirchBranches(h, c, r, hx, hz), BirchBark));
        if (winter)
        {
            // Bare: a few dark twiggy limbs fanning up from the upper trunk, a lick of snow in the forks.
            for (int k = 0; k < 4; k++)
            {
                float a = k * 90f + 40f * Noise.Hash01(hx + k, hz, 17), len = 1.6f * c;
                var arm = new Node3D { Position = new Vector3(0, h * (0.62f + 0.07f * k), 0), RotationDegrees = new Vector3(0, a, 38f) };
                arm.AddChild(Part(Cyl(r * 0.45f, len, 4, r * 0.15f), BareTwigs, new Vector3(0, len * 0.5f, 0)));
                n.AddChild(arm);
            }
            n.AddChild(Part(Ball(0.9f * c), BareTwigs, new Vector3(0, h * 0.95f, 0)));
            n.AddChild(Part(Ball(0.5f * c), Snow, new Vector3(0.1f * c, h * 0.95f + 0.55f * c, 0)));
            return Fells(n, h * 0.9f, 0.6f * c, r * 2f);
        }
        // Autumn: gold, now and then orange, a few still half green; thinner crowns.
        float turn = Noise.Hash01(hx, hz, 19);
        Color leaf = autumn ? (turn < 0.2f ? AutumnLeafGreen : turn < 0.75f ? AutumnLeaf : AutumnLeafOrange) : BirchLeaf;
        Color dark = autumn ? leaf * 0.8f : BirchLeafDark;
        float thin = autumn ? 0.85f : 1f;
        n.AddChild(Part(Ball(1.7f * c * thin), dark, new Vector3(0.2f * c, h * 0.87f, 0.1f * c)));
        n.AddChild(Part(Ball(1.3f * c * thin), leaf, new Vector3(-0.3f * c, h * 0.87f + 1.2f * c, -0.2f * c)));
        var crown = Part(Ball(0.92f * c * thin), leaf, new Vector3(0.72f * c, h * 0.87f + 0.62f * c, -0.48f * c));
        crown.Scale = new Vector3(1.15f, 0.85f, 1f);
        n.AddChild(crown);
        return Fells(n, h * 0.87f, 1.2f * c, r * 2f);
    }

    /// <summary>Spruce: stacked low-poly cones down to the ground. Size 0.4 (sapling) .. 1.6.</summary>
    static Node3D Spruce(float size, int hx, int hz, bool winter = false)
    {
        var n = new Node3D();
        float h = 8.5f * size * (0.85f + 0.3f * Noise.Hash01(hx, hz, 11));
        float w = 2.1f * size * (0.8f + 0.35f * Noise.Hash01(hx, hz, 13));    // skirt radius
        n.AddChild(Part(Cyl(0.1f + 0.1f * size, h * 0.86f, 5), PineBarkLow, new Vector3(0, h * 0.43f, 0)));
        int tiers = size < 0.7f ? 2 : 3;
        n.AddChild(TreeBeams(SpruceBoughs(h, w, tiers, hx, hz), SpruceLeafLight));
        for (int k = 0; k < tiers; k++)
        {
            float f = k / (float)tiers;
            float ch = h * (tiers == 2 ? 0.62f : 0.46f);                       // overlapping cones
            float y0 = 0.25f * size + (h - ch - 0.25f * size) * (k / (float)(tiers - 1));
            float r = w * (1f - 0.62f * f);
            n.AddChild(Part(Cyl(w * (1f - 0.62f * f), ch, 7, 0f), k % 2 == 0 ? SpruceLeaf : SpruceLeafLight,
                new Vector3(0, y0 + ch * 0.5f, 0)));
            if (winter)
            {
                if (k == tiers - 1)
                {
                    // The crown keeps its snow cap.
                    float cover = 0.62f + 0.08f * Noise.Hash01(hx + k, hz, 19);
                    n.AddChild(Part(Cyl(r * cover * 1.04f, ch * cover, 7, 0f), Snow,
                        new Vector3(0, y0 + ch - ch * cover * 0.5f + 0.04f, 0), new Vector3(0, 25f * k, 0)));
                }
                else
                {
                    n.AddChild(SpruceSnowPatches(y0, ch, r, k, hx, hz));
                }
            }
        }
        return Fells(n, h * 0.6f, w * 0.45f, 0.2f + 0.2f * size);
    }

    /// <summary>Three broken snow loads on the broad lower boughs, leaving green gaps between them.</summary>
    static MeshInstance3D SpruceSnowPatches(float y0, float h, float radius, int tier, int hx, int hz)
    {
        const int around = 4, rows = 1;
        float phase = tier * 0.73f;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        for (int p = 0; p < 3; p++)
        {
            st.SetColor(p == 1 ? SnowShade : Snow);
            float center = phase + p * (MathF.Tau / 3f)
                + (Noise.Hash01(hx + tier * 13 + p, hz, 71) - 0.5f) * 0.55f;
            float span = 0.95f + 0.55f * Noise.Hash01(hx + tier * 17 + p, hz, 73);
            var v = new Vector3[rows + 1, around + 1];
            for (int x = 0; x <= around; x++)
            {
                float u = x / (float)around;
                float angle = center + (u - 0.5f) * span
                    + (Noise.Hash01(hx + tier * 31 + p * 11 + x, hz, 83) - 0.5f) * 0.14f;
                float low = 0.015f + 0.06f * Noise.Hash01(hx + tier * 19 + p + x, hz, 79);
                float high = 0.19f + 0.16f * Noise.Hash01(hx, hz + tier * 23 + p + x, 81);
                for (int row = 0; row <= rows; row++)
                {
                    float t = row / (float)rows;
                    float fraction = Mathf.Lerp(low, high, t);
                    float outer = 1.035f + fraction * 0.58f
                        + (Noise.Hash01(hx + tier * 47 + p * 17 + x, hz + row, 97) - 0.5f) * 0.1f;
                    float r = radius * (1f - fraction) * outer;
                    v[row, x] = new Vector3(MathF.Cos(angle) * r, y0 + h * fraction + 0.025f, MathF.Sin(angle) * r);
                }
            }
            for (int x = 0; x < around; x++)
            for (int row = 0; row < rows; row++)
            {
                SnowTriangle(st, v[row, x], v[row, x + 1], v[row + 1, x + 1]);
                SnowTriangle(st, v[row, x], v[row + 1, x + 1], v[row + 1, x]);
            }
        }
        var material = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.PerVertex,
            Roughness = 1f,
            SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        return new MeshInstance3D
        {
            Name = "LowerSpruceSnow",
            Mesh = st.Commit(),
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    static void SnowTriangle(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 normal = (b - a).Cross(c - a).Normalized();
        st.SetNormal(normal); st.AddVertex(a);
        st.SetNormal(normal); st.AddVertex(b);
        st.SetNormal(normal); st.AddVertex(c);
    }

    /// <summary>Broken limbs and branch whorls merged into one low-poly mesh per tree.</summary>
    static List<(Vector3 a, Vector3 b, float width)> BirchBranches(float h, float crown, float trunk, int hx, int hz)
    {
        var beams = new List<(Vector3, Vector3, float)>();
        for (int k = 0; k < 4; k++)
        {
            float a = k * (MathF.Tau / 4f) + Noise.Hash01(hx + k, hz, 37) * 0.6f;
            float y = h * (0.56f + 0.075f * k);
            float len = crown * (1.05f + 0.25f * Noise.Hash01(hx, hz + k, 41));
            Vector3 dir = new(MathF.Sin(a), 0, MathF.Cos(a));
            var from = Vector3.Up * y + dir * trunk * 0.45f;
            var to = from + dir * len + Vector3.Up * (crown * 0.45f);
            beams.Add((from, to, trunk * 0.24f));
        }
        return beams;
    }

    /// <summary>Short, down-swept bough tips that break the spruce's smooth cone silhouette.</summary>
    static List<(Vector3 a, Vector3 b, float width)> SpruceBoughs(float h, float crown, int tiers, int hx, int hz)
    {
        var beams = new List<(Vector3, Vector3, float)>();
        for (int k = 0; k < tiers; k++)
        {
            float f = k / (float)tiers;
            float ch = h * 0.46f;
            float y0 = 0.25f + (h - ch - 0.25f) * (k / (float)(tiers - 1));
            float radius = crown * (1f - 0.62f * f) * 0.38f;
            for (int b = 0; b < 3; b++)
            {
                float a = b * (MathF.Tau / 3f) + Noise.Hash01(hx + k, hz + b, 43) * 0.5f;
                Vector3 dir = new(MathF.Sin(a), 0, MathF.Cos(a));
                var from = Vector3.Up * (y0 + ch * 0.58f) + dir * radius * 0.08f;
                var to = from + dir * radius * 1.3f - Vector3.Up * (radius * 0.38f);
                beams.Add((from, to, radius * 0.12f));
            }
        }
        return beams;
    }

    /// <summary>Builds tapered four-sided branches into one mesh, keeping added tree detail inexpensive.</summary>
    static MeshInstance3D TreeBeams(List<(Vector3 a, Vector3 b, float width)> beams, Color color)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        void Face(Vector3 a, Vector3 b, Vector3 c)
        {
            st.AddVertex(a); st.AddVertex(b); st.AddVertex(c);
            st.AddVertex(c); st.AddVertex(b); st.AddVertex(a);
        }
        foreach (var (a, b, width) in beams)
        {
            Vector3 axis = (b - a).Normalized();
            Vector3 side = axis.Cross(Vector3.Up).Normalized() * width;
            if (side.LengthSquared() < 0.001f) side = Vector3.Right * width;
            Vector3 depth = axis.Cross(side).Normalized() * width * 0.7f;
            Vector3 endSide = side * 0.22f, endDepth = depth * 0.22f;
            Vector3[] baseV = { a - side - depth, a + side - depth, a + side + depth, a - side + depth };
            Vector3[] tipV = { b - endSide - endDepth, b + endSide - endDepth, b + endSide + endDepth, b - endSide + endDepth };
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                Face(baseV[i], baseV[j], tipV[j]);
                Face(baseV[i], tipV[j], tipV[i]);
            }
        }
        st.GenerateNormals();
        return new MeshInstance3D
        {
            Mesh = st.Commit(),
            MaterialOverride = Mat(color),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    /// <summary>Pine: tall bare reddish trunk with the crown high up. Size 0.5 .. 1.5.</summary>
    static Node3D Pine(float size, int hx, int hz, bool winter = false, bool red = false)
    {
        var n = new Node3D();
        float h = 9.5f * size * (0.85f + 0.3f * Noise.Hash01(hx, hz, 11));
        float c = size * (0.75f + 0.5f * Noise.Hash01(hx, hz, 13));
        float r = 0.15f * MathF.Pow(size, 1.2f);
        // Grey-brown lower trunk, the upper trunk and crown reddish.
        n.AddChild(Part(Cyl(r, h * 0.35f, 5, r * 0.9f), PineBarkLow, new Vector3(0, h * 0.175f, 0)));
        n.AddChild(Part(Cyl(r * 0.9f, h * 0.65f, 5, r * 0.5f), PineBark, new Vector3(0, h * 0.675f, 0)));
        // The red forest: needles killed rust-red, a few still brown-green.
        float dead = Noise.Hash01(hx, hz, 23);
        var crown = Part(Ball(1.5f * c), red ? (dead < 0.15f ? new Color(0.36f, 0.3f, 0.16f) : RedNeedles) : PineLeaf, new Vector3(0.3f * c, h * 0.88f, 0));
        crown.Scale = new Vector3(1f, 0.55f, 1f);
        n.AddChild(crown);
        var top = Part(Ball(1.1f * c), red ? RedNeedlesLight : SpruceLeafLight, new Vector3(-0.4f * c, h * 0.88f + 0.8f * c, 0.2f * c));
        top.Scale = new Vector3(1f, 0.6f, 1f);
        n.AddChild(top);
        if (winter)
        {
            // Snow on the crown: flattened white cushions on top of both balls.
            var s1 = Part(Ball(1.25f * c), Snow, new Vector3(0.3f * c, h * 0.88f + 0.35f * c, 0));
            s1.Scale = new Vector3(1f, 0.35f, 1f);
            n.AddChild(s1);
            var s2 = Part(Ball(0.9f * c), SnowShade, new Vector3(-0.4f * c, h * 0.88f + 1.05f * c, 0.2f * c));
            s2.Scale = new Vector3(1f, 0.35f, 1f);
            n.AddChild(s2);
        }
        return Fells(n, h * 0.88f, 0.8f * c, r * 2f);
    }

    /// <summary>
    /// A wooden power pole: creosote-stained foot, step bolts, a stencilled number plate, a crossarm with
    /// braces and pin insulators, an earth wire down the trunk and a capped top. The stationary parts are
    /// merged into one mesh (a line has dozens of them); the transformer stays its own node.
    /// </summary>
    static Node3D Pole(bool transformer)
    {
        var n = new Node3D();
        n.AddChild(BatchedCopy("pole", PoleBody));        // every pole of a line is the same mesh
        if (transformer) n.AddChild(VillagePower.Transformer());
        return Fells(n, 6.6f, 0.12f, 0.24f);
    }

    static Node3D PoleBody()
    {
        var solid = new Node3D();
        Color creosote = new(0.2f, 0.15f, 0.11f), steel = new(0.4f, 0.41f, 0.4f), plate = new(0.86f, 0.84f, 0.76f);
        solid.AddChild(Part(Cyl(0.12f, 7f, 5), Wood, new Vector3(0, 3.5f, 0)));
        solid.AddChild(Part(Cyl(0.135f, 1.3f, 5), creosote, new Vector3(0, 0.65f, 0)));                   // the foot, tarred against rot
        solid.AddChild(Part(Cyl(0.05f, 0.18f, 5, 0.0f), Wood, new Vector3(0, 7.09f, 0)));                // the capped top
        solid.AddChild(Part(Box(1.8f, 0.14f, 0.14f), Wood, new Vector3(0, 6.6f, 0)));                    // crossarm
        foreach (float side in new[] { -1f, 1f })
        {
            // A brace from the trunk up to each end of the arm, and a pin on each insulator.
            solid.AddChild(Part(Box(1.03f, 0.06f, 0.06f), Timber, new Vector3(side * 0.375f, 6.2f, 0), new Vector3(0, 0, side * 43f)));
        }
        foreach (var at in PoleWireAttach)
        {
            solid.AddChild(Part(Cyl(0.085f, 0.18f, 6), new Color(0.72f, 0.76f, 0.7f), at - Vector3.Up * 0.06f));
            solid.AddChild(Part(Cyl(0.03f, 0.1f, 4), steel, at - Vector3.Up * 0.2f));                       // the pin
        }
        // Climbing bolts alternate left and right up the trunk; the number plate and a warning sign sit on the lane side.
        for (int k = 0; k < 6; k++)
            solid.AddChild(Part(Box(0.22f, 0.04f, 0.04f), steel, new Vector3(k % 2 == 0 ? 0.1f : -0.1f, 2.2f + k * 0.55f, 0.1f)));
        solid.AddChild(Part(Box(0.22f, 0.3f, 0.03f), plate, new Vector3(0, 2.0f, 0.125f)));
        solid.AddChild(Part(Box(0.12f, 0.14f, 0.035f), new Color(0.15f, 0.15f, 0.15f), new Vector3(0, 2.0f, 0.13f)));      // stencilled digits
        solid.AddChild(Part(Box(0.035f, 6.2f, 0.035f), steel, new Vector3(-0.115f, 3.4f, 0)));          // the earth wire
        DrunkTractor.BatchStatic(solid, null);
        return solid;
    }

    static Node3D Barn(bool tractorBarn)
    {
        var n = new Node3D();
        if (tractorBarn)
        {
            // The working barn has a real opening in its road-facing wall.
            n.AddChild(Part(Box(8f, 4.5f, 0.25f), BarnWall, new Vector3(0, 2.0f, -6.38f)));
            foreach (float side in new[] { -1f, 1f })
            {
                n.AddChild(Part(Box(2.2f, 4.5f, 0.25f), BarnWall, new Vector3(side * 2.9f, 2.0f, 6.38f)));
                n.AddChild(Part(Box(0.24f, 4.5f, 12.75f), BarnWall, new Vector3(side * 3.88f, 2.0f, 0)));
            }
            n.AddChild(Part(Box(3.6f, 0.9f, 0.25f), BarnWall, new Vector3(0, 4.05f, 6.38f)));
        }
        else n.AddChild(Part(Box(8f, 4.5f, 13f), BarnWall, new Vector3(0, 2.0f, 0)));
        if (tractorBarn) n.AddChild(Village.TractorBarnRoof());
        else n.AddChild(Part(new PrismMesh { Size = new Vector3(8.8f, 3f, 13.6f) }, Roof, new Vector3(0, 5.75f, 0)));
        if (!tractorBarn) n.AddChild(Part(Box(3.2f, 3.4f, 0.1f), Wood, new Vector3(0, 1.7f, 6.52f)));
        // Whitewashed corner boards and exposed plank joints break up the broad red walls.
        Color trim = new(0.76f, 0.69f, 0.54f);
        foreach (float x in new[] { -4.06f, 4.06f })
        {
            foreach (float z in new[] { -6.52f, 6.52f })
                n.AddChild(Part(Box(0.2f, 4.45f, 0.2f), Timber, new Vector3(x, 2.05f, z)));
            for (float z = -4.8f; z <= 4.81f; z += 2.4f)
                n.AddChild(Part(Box(0.09f, 4.05f, 0.08f), BarnPlank, new Vector3(x, 2.05f, z)));
            foreach (float y in new[] { 0.65f, 1.7f, 2.75f, 3.8f })
                n.AddChild(Part(Box(0.08f, 0.1f, 12.8f), BarnPlank, new Vector3(x, y, 0)));
        }
        // Double-leaf door, framed and braced, with a small grain-cooperative sign above it.
        foreach (float x in new[] { -1.72f, 1.72f })
            n.AddChild(Part(Box(0.16f, 3.65f, 0.16f), trim, new Vector3(x, 1.8f, 6.63f)));
        n.AddChild(Part(Box(3.6f, 0.18f, 0.16f), trim, new Vector3(0, 3.48f, 6.63f)));
        if (tractorBarn)
        {
            n.AddChild(new BarnDoors(3.44f, 3.45f, 6.55f, Wood, BarnPlank));
            n.AddChild(NightGlow.Part(Box(0.42f, 0.13f, 0.26f), Wood,
                new Color(1f, 0.76f, 0.42f), 1.3f, new Vector3(0, 3.46f, 6.87f)));
            var porchLight = new OmniLight3D { Position = new Vector3(0, 3.25f, 7.05f),
                LightColor = new Color(1f, 0.76f, 0.44f), OmniRange = 8f, ShadowEnabled = false, LightEnergy = 0f };
            porchLight.SetMeta("night_energy", 1.5f);
            n.AddChild(porchLight);
        }
        else
        {
            n.AddChild(Part(Box(0.08f, 3.25f, 0.12f), Timber, new Vector3(0, 1.7f, 6.62f)));
            foreach (float angle in new[] { -43f, 43f })
                n.AddChild(Part(Box(0.14f, 3.8f, 0.1f), BarnPlank, new Vector3(0, 1.7f, 6.64f), new Vector3(0, 0, angle)));
        }
        n.AddChild(Buildings.TextPanel("КОЛХОЗ", 3.6f, 0.55f, new Vector3(0, 3.9f, 6.72f), SovietRed, trim));
        // Ventilation slots in the end walls and a ridge cap along the roof.
        foreach (float z in new[] { -6.63f, 6.63f })
        foreach (float x in new[] { -2.55f, 2.55f })
        {
            n.AddChild(Part(Box(0.75f, 0.34f, 0.08f), Wood, new Vector3(x, 4.05f, z)));
            n.AddChild(Part(Box(0.65f, 0.05f, 0.1f), trim, new Vector3(x, 4.05f, z + MathF.Sign(z) * 0.05f)));
        }
        if (!tractorBarn) n.AddChild(Part(Box(0.24f, 0.18f, 13.7f), Timber, new Vector3(0, 7.18f, 0)));
        // On the working barn the hatch belongs in the wooden gable, below the metal roof.
        if (tractorBarn)
        {
            n.AddChild(Part(Box(1.15f, 1.0f, 0.12f), Timber, new Vector3(0, 5.25f, 6.62f)));
            n.AddChild(Part(Box(0.95f, 0.78f, 0.14f), Wood, new Vector3(0, 5.25f, 6.64f)));
            n.AddChild(Part(Box(0.95f, 0.08f, 0.16f), trim, new Vector3(0, 5.25f, 6.67f)));
        }
        else
        {
            n.AddChild(Part(Box(1.25f, 0.75f, 0.08f), Wood, new Vector3(4.48f, 4.65f, -1.5f)));
            n.AddChild(Part(Box(1.05f, 0.08f, 0.1f), trim, new Vector3(4.54f, 4.65f, -1.5f)));
            n.AddChild(Part(Box(0.08f, 0.58f, 0.1f), trim, new Vector3(4.54f, 4.65f, -1.5f)));
        }
        return n;
    }

    static Node3D Silo(TrackData track, Vector3 origin)
    {
        var n = new Node3D();
        bool aged = track.Id == "ilyich";
        Color steel = aged ? new Color(0.30f, 0.26f, 0.21f) : Timber;
        Color red = aged ? new Color(0.46f, 0.19f, 0.14f) : SovietRed;
        if (aged) n.AddChild(Village.WeatheredSiloBody());
        else n.AddChild(Part(Cyl(2.4f, 12f, 10), Concrete, new Vector3(0, 6f, 0)));
        n.AddChild(Part(Cyl(2.5f, 2f, 10, 0.2f), Roof, new Vector3(0, 13f, 0)));
        if (!aged) n.AddChild(Part(Cyl(2.45f, 0.6f, 10), red, new Vector3(0, 9f, 0)));
        // Cast-concrete seams, a fill hatch and an old steel vent make the silo read at a glance.
        foreach (float y in new[] { 1.5f, 3.5f, 5.5f, 7.5f, 10.5f })
            n.AddChild(Part(Cyl(2.44f, 0.09f, 10), new Color(0.53f, 0.53f, 0.5f), new Vector3(0, y, 0)));
        n.AddChild(Part(Cyl(0.55f, 0.12f, 8), Concrete, new Vector3(0, 14.05f, 0)));
        n.AddChild(Part(Cyl(0.18f, 0.85f, 6, 0.13f), steel, new Vector3(0, 14.48f, 0)));
        n.AddChild(Part(Cyl(0.32f, 0.1f, 8), Roof, new Vector3(0, 14.93f, 0)));
        // A fixed ladder on the road-facing side, with a red grain-store plate beside it.
        foreach (var (x, z, angle) in new[] { (0.9f, 2.22f, 22f), (1.48f, 1.89f, 38f) })
            n.AddChild(Part(Box(0.1f, 8.4f, 0.1f), steel, new Vector3(x, 5.6f, z), new Vector3(0, angle, 0)));
        for (float y = 1.8f; y <= 9.41f; y += 0.95f)
            n.AddChild(Part(Box(0.72f, 0.08f, 0.1f), steel, new Vector3(1.2f, y, 2.08f), new Vector3(0, 30, 0)));
        n.AddChild(Buildings.TextPanel("ЗЕРНО", 1.7f, 0.55f, new Vector3(-0.2f, 8.05f, 2.43f), red, aged ? new Color(0.78f, 0.69f, 0.46f) : new Color(0.95f, 0.82f, 0.46f)));
        // Unloading pipe and shutoff wheel at the base.
        n.AddChild(Part(Box(0.18f, 1.35f, 0.18f), steel, new Vector3(-1.25f, 0.78f, 2.08f)));
        n.AddChild(Part(Box(0.85f, 0.16f, 0.16f), steel, new Vector3(-1.25f, 0.28f, 2.35f)));
        n.AddChild(Part(Cyl(0.22f, 0.08f, 8), red, new Vector3(-1.25f, 1.5f, 2.2f), new Vector3(90, 0, 0)));
        if (aged) n.AddChild(Village.SiloLoadingApron(track, origin));
        return n;
    }

    static Node3D Tractor()
    {
        var n = new Node3D();
        n.AddChild(Part(Box(1.2f, 1.1f, 2.6f), TractorBlue, new Vector3(0, 1.3f, -0.5f)));
        n.AddChild(Part(Box(1.5f, 1.4f, 1.3f), TractorBlue, new Vector3(0, 2.3f, 0.8f)));
        n.AddChild(Part(Box(1.3f, 0.7f, 1.1f), new Color(0.5f, 0.6f, 0.65f), new Vector3(0, 2.4f, 0.8f)));
        foreach (float s in new[] { -1f, 1f })
        {
            n.AddChild(Part(Cyl(0.85f, 0.45f, 10), Rubber, new Vector3(s * 0.95f, 0.85f, 0.9f), new Vector3(0, 0, 90)));
            n.AddChild(Part(Cyl(0.45f, 0.3f, 8), Rubber, new Vector3(s * 0.75f, 0.45f, -1.5f), new Vector3(0, 0, 90)));
        }
        n.AddChild(Part(Cyl(0.06f, 1.2f, 4), Rubber, new Vector3(0.4f, 2.3f, -1.2f)));
        return n;
    }

    /// <summary>
    /// The monument to Ilyich in the village: a stepped granite plinth with an inscription plate on both faces,
    /// a bronze figure in boots, flared coat, shoulders, head with a goatee and a cap in one hand, the other arm
    /// pointing the way (-Z, the road side); steps, a wreath and a little flower bed at the foot on that side.
    /// </summary>
    static Node3D Statue(bool detailed = false)
    {
        var n = new Node3D();
        var solid = new Node3D();
        Color granite = Granite, graniteLight = Granite.Lightened(0.12f), bronze = Bronze, bronzeDark = Bronze.Darkened(0.25f);
        // Plinth: base slab, mid slab, the block with a cornice, and two steps on the road side.
        solid.AddChild(Part(Box(3.4f, 0.3f, 3.4f), graniteLight, new Vector3(0, 0.15f, 0)));
        solid.AddChild(Part(Box(2.8f, 0.25f, 2.8f), granite, new Vector3(0, 0.425f, 0)));
        solid.AddChild(Part(Box(2.3f, 1.85f, 2.3f), granite, new Vector3(0, 1.475f, 0)));
        solid.AddChild(Part(Box(2.55f, 0.2f, 2.55f), graniteLight, new Vector3(0, 2.5f, 0)));
        solid.AddChild(Part(Box(2.0f, 0.14f, 0.7f), graniteLight, new Vector3(0, 0.07f, -2.05f)));
        solid.AddChild(Part(Box(1.6f, 0.14f, 0.5f), graniteLight, new Vector3(0, 0.21f, -1.9f)));
        foreach (float zz in new[] { -1.17f, 1.17f })
            solid.AddChild(Part(Box(2.0f, 0.22f, 0.04f), Gold, new Vector3(0, 2.2f, zz)));              // gilt bands above the plates
        if (detailed) AddIlyichFigure(solid);
        else
        {
            // The figure (front towards -Z): boots, legs, coat, belt, shoulders, head, goatee and cap.
            foreach (float x in new[] { -0.2f, 0.2f })
            {
                solid.AddChild(Part(Box(0.28f, 0.2f, 0.5f), bronzeDark, new Vector3(x, 2.7f, -0.05f)));
                solid.AddChild(Part(Box(0.24f, 1.15f, 0.26f), bronze, new Vector3(x, 3.4f, 0)));
            }
            solid.AddChild(Part(Cyl(0.5f, 1.4f, 8, 0.36f), bronze, new Vector3(0, 4.65f, 0)));            // the coat, flared at the hem
            solid.AddChild(Part(Box(0.9f, 0.1f, 0.5f), bronzeDark, new Vector3(0, 4.55f, 0)));            // belt
            solid.AddChild(Part(Box(1.0f, 0.3f, 0.55f), bronze, new Vector3(0, 5.42f, 0)));               // shoulders
            solid.AddChild(Part(Cyl(0.11f, 0.2f, 6), bronze, new Vector3(0, 5.68f, 0)));                  // neck
            solid.AddChild(Part(Ball(0.28f), bronze, new Vector3(0, 5.95f, 0)));
            solid.AddChild(Part(Box(0.13f, 0.17f, 0.1f), bronzeDark, new Vector3(0, 5.8f, -0.25f)));      // goatee
            solid.AddChild(Part(Box(0.2f, 0.05f, 0.12f), bronzeDark, new Vector3(0, 5.88f, -0.27f)));     // moustache
            solid.AddChild(Part(Box(0.22f, 1.15f, 0.22f), bronze, new Vector3(0.58f, 5.3f, -0.45f), new Vector3(-55, 0, -18)));   // the arm that points the way
            solid.AddChild(Part(Box(0.18f, 0.1f, 0.3f), bronze, new Vector3(0.75f, 5.74f, -0.98f)));      // its hand
            solid.AddChild(Part(Box(0.2f, 0.9f, 0.2f), bronze, new Vector3(-0.58f, 5.05f, -0.05f), new Vector3(0, 0, 12)));        // the other arm
            solid.AddChild(Part(Box(0.4f, 0.12f, 0.3f), bronzeDark, new Vector3(-0.66f, 4.58f, -0.05f))); // the cap in that hand
        }
        // The wreath at the foot of the plinth and a flower bed in front of the steps.
        solid.AddChild(Part(Cyl(0.55f, 0.1f, 10), new Color(0.2f, 0.4f, 0.2f), new Vector3(0, 0.62f, -1.43f), new Vector3(90, 0, 0)));
        solid.AddChild(Part(Cyl(0.3f, 0.11f, 10), new Color(0.8f, 0.1f, 0.08f), new Vector3(0, 0.62f, -1.44f), new Vector3(90, 0, 0)));
        solid.AddChild(Part(Box(3.2f, 0.18f, 0.9f), new Color(0.3f, 0.2f, 0.12f), new Vector3(0, 0.09f, -3.0f)));
        for (int k = 0; k < 9; k++)
            solid.AddChild(Part(Box(0.16f, 0.2f, 0.16f), k % 3 == 0 ? Gold : k % 3 == 1 ? SovietRed : new Color(0.9f, 0.9f, 0.85f),
                new Vector3(-1.3f + k * 0.325f, 0.3f, -3.0f + (k % 2 == 0 ? 0.15f : -0.15f))));
        DrunkTractor.BatchStatic(solid, null);
        n.AddChild(solid);
        foreach (float zz in new[] { -1f, 1f })
        {
            var plate = Buildings.TextPanel(detailed ? "LENIN" : "ИЛЬИЧ", detailed ? 1.95f : 1.7f, 0.5f, new Vector3(0, 1.4f, zz * 1.17f), new Color(0.08f, 0.08f, 0.09f), new Color(0.95f, 0.8f, 0.3f));
            if (zz < 0) plate.RotationDegrees = new Vector3(0, 180, 0);
            n.AddChild(plate);
        }
        return n;
    }

    /// <summary>
    /// A roadside slogan board, readable from both sides: a slab with a framed panel on each face, two stout posts with
    /// concrete feet and rear braces, a tin rain cap with a gilt star, and lamp arms over both faces (lit at night).
    /// </summary>
    static Node3D Billboard(string text)
    {
        var n = new Node3D();
        var solid = new Node3D();
        Color frame = Timber, tin = new(0.38f, 0.4f, 0.42f), back = new(0.27f, 0.2f, 0.14f), foot = new(0.6f, 0.6f, 0.56f);
        foreach (float x in new[] { -2.9f, 2.9f })
        {
            solid.AddChild(Part(Box(0.22f, 4.6f, 0.22f), Wood, new Vector3(x, 2.3f, 0)));
            solid.AddChild(Part(Box(0.3f, 0.08f, 0.3f), Timber, new Vector3(x, 4.64f, 0)));
            solid.AddChild(Part(Box(0.5f, 0.35f, 0.5f), foot, new Vector3(x, 0.14f, 0)));                // concrete foot
            solid.AddChild(Part(Box(0.1f, 0.1f, 2.1f), Timber, new Vector3(x, 0.9f, -0.85f), new Vector3(-45, 0, 0)));   // brace behind
        }
        solid.AddChild(Part(Box(6.2f, 0.12f, 0.12f), Timber, new Vector3(0, 1.6f, -0.1f)));              // cross-beam between the posts
        solid.AddChild(Part(Box(6.5f, 2.05f, 0.1f), back, new Vector3(0, 3.5f, 0)));                     // the slab between the two panels
        // The frame round the panels (each 6.4 x 2.0).
        solid.AddChild(Part(Box(6.8f, 0.16f, 0.3f), frame, new Vector3(0, 4.56f, 0)));
        solid.AddChild(Part(Box(6.8f, 0.16f, 0.3f), frame, new Vector3(0, 2.44f, 0)));
        foreach (float x in new[] { -3.32f, 3.32f })
            solid.AddChild(Part(Box(0.16f, 2.2f, 0.3f), frame, new Vector3(x, 3.5f, 0)));
        // Tin rain cap, a gilt star on it and a lamp arm over each face.
        solid.AddChild(Part(Box(7.0f, 0.08f, 0.7f), tin, new Vector3(0, 4.74f, 0)));
        solid.AddChild(Part(Box(0.4f, 0.4f, 0.06f), Gold, new Vector3(0, 5.0f, 0), new Vector3(0, 0, 45)));
        solid.AddChild(Part(Box(0.4f, 0.4f, 0.06f), Gold, new Vector3(0, 5.0f, 0)));
        foreach (float side in new[] { -1f, 1f })
        foreach (float x in new[] { -1.9f, 1.9f })
            solid.AddChild(Part(Box(0.06f, 0.06f, 0.9f), tin, new Vector3(x, 4.82f, side * 0.55f), new Vector3(-side * 15, 0, 0)));
        DrunkTractor.BatchStatic(solid, null);
        n.AddChild(solid);
        n.AddChild(Panel(text, 6.4f, 2f, new Vector3(0, 3.5f, 0.07f), SovietRed, Gold));
        var rear = Panel(text, 6.4f, 2f, new Vector3(0, 3.5f, -0.07f), SovietRed, Gold);
        rear.RotationDegrees = new Vector3(0, 180, 0);
        n.AddChild(rear);
        foreach (float side in new[] { -1f, 1f })       // the lamp heads over the panels: they glow at night
        foreach (float x in new[] { -1.9f, 1.9f })
            n.AddChild(NightGlow.Part(Box(0.4f, 0.14f, 0.3f), tin, new Color(1f, 0.82f, 0.5f), 1.4f, new Vector3(x, 4.7f, side * 1.0f)));
        return n;
    }

    /// <summary>
    /// The start and finish gantry of a track without a start barn: two steel legs on base plates with a lattice truss across,
    /// the START / FINISH banner in it, red pennants, a flagpole at each leg, two floodlights (they glow and light the line
    /// at night), a chequered line painted across the road under it, and a cap of snow in winter. Posts stand at +-half
    /// (the obstacles are there); the line is at local z 0 where the lap timer counts.
    /// </summary>
    static Node3D StartGate(float half, bool winter = false)
    {
        var n = new Node3D();
        var solid = new Node3D();
        Color steel = new(0.34f, 0.37f, 0.38f), steelLight = new(0.5f, 0.52f, 0.5f), concrete = new(0.62f, 0.61f, 0.57f), white = new(0.92f, 0.92f, 0.86f), black = new(0.1f, 0.1f, 0.1f);
        const float Top = 5.9f, Low = 5.05f;
        foreach (float s in new[] { -1f, 1f })
        {
            float x = s * half;
            solid.AddChild(Part(Box(0.7f, 0.25f, 0.7f), concrete, new Vector3(x, 0.125f, 0)));            // footing
            solid.AddChild(Part(Box(0.5f, 0.06f, 0.5f), steelLight, new Vector3(x, 0.28f, 0)));           // base plate
            foreach (float bx in new[] { -0.19f, 0.19f })
            foreach (float bz in new[] { -0.19f, 0.19f })
                solid.AddChild(Part(Cyl(0.035f, 0.1f, 5), steelLight, new Vector3(x + bx, 0.36f, bz)));   // anchor bolts
            solid.AddChild(Part(Cyl(0.17f, 5.9f, 8, 0.13f), steel, new Vector3(x, 3.25f, 0)));            // the leg, a little slimmer at the top
            solid.AddChild(Part(Box(0.4f, 0.12f, 0.4f), steelLight, new Vector3(x, Top + 0.1f, 0)));      // cap on the leg
            solid.AddChild(Part(Box(0.12f, 0.45f, 0.5f), steel, new Vector3(x, 4.5f, 0.3f)));             // a gusset to the front, where the truss joins
            solid.AddChild(Part(Box(0.9f, 0.04f, 0.9f), new Color(0.78f, 0.1f, 0.08f), new Vector3(x, 1.2f, 0)));   // red band round the foot of the leg, painted
            solid.AddChild(Part(Cyl(0.185f, 0.3f, 8), new Color(0.78f, 0.1f, 0.08f), new Vector3(x, 1.1f, 0)));
            solid.AddChild(Part(Cyl(0.185f, 0.3f, 8), white, new Vector3(x, 1.4f, 0)));
        }
        // The lattice truss: top and bottom chords, a zigzag between them and uprights every few metres.
        float len = half * 2f;
        solid.AddChild(Part(Box(len, 0.12f, 0.14f), steel, new Vector3(0, Top, 0)));
        solid.AddChild(Part(Box(len, 0.12f, 0.14f), steel, new Vector3(0, Low, 0)));
        int bays = Math.Max(4, (int)MathF.Round(len / 1.4f));
        float bay = len / bays, diag = MathF.Sqrt(bay * bay + (Top - Low) * (Top - Low));
        float tilt = Mathf.RadToDeg(MathF.Atan2(Top - Low, bay));
        for (int k = 0; k < bays; k++)
        {
            float cx = -half + bay * (k + 0.5f);
            solid.AddChild(Part(Box(diag, 0.07f, 0.07f), steel, new Vector3(cx, (Top + Low) / 2f, 0), new Vector3(0, 0, k % 2 == 0 ? tilt : -tilt)));
        }
        for (int k = 1; k < bays; k++)
            solid.AddChild(Part(Box(0.08f, Top - Low, 0.08f), steel, new Vector3(-half + bay * k, (Top + Low) / 2f, 0)));
        // Red pennants on a line under the top chord, and the two flagpoles on the leg caps.
        for (int k = 0; k < bays * 2; k++)
        {
            float px = -half + 0.3f + (len - 0.6f) * k / (bays * 2f - 1f);
            solid.AddChild(Part(new PrismMesh { Size = new Vector3(0.28f, 0.34f, 0.03f) }, k % 2 == 0 ? SovietRed : Gold, new Vector3(px, Top - 0.3f, 0.12f), new Vector3(0, 0, 180)));
        }
        foreach (float s in new[] { -1f, 1f })
        {
            solid.AddChild(Part(Cyl(0.04f, 2.6f, 5), new Color(0.7f, 0.7f, 0.7f), new Vector3(s * half, Top + 1.4f, 0)));
            solid.AddChild(Part(Ball(0.07f), Gold, new Vector3(s * half, Top + 2.75f, 0)));
            for (int k = 0; k < 4; k++)       // a waving red flag, in strips
                solid.AddChild(Part(Box(0.3f, 0.7f, 0.025f), k % 2 == 0 ? SovietRed : SovietRed * 0.85f,
                    new Vector3(s * half + s * (0.17f + k * 0.3f), Top + 2.2f, MathF.Sin(k * 1.6f) * 0.06f), new Vector3(0, MathF.Sin(k * 1.6f) * 14f, 0)));
        }
        // The line: a chequered strip across the road, two rows of squares (0.5 m), centred on z 0.
        int cells = Math.Max(2, (int)((len - 3.6f) / 0.5f));
        for (int k = 0; k < cells; k++)
        for (int r = 0; r < 2; r++)
            solid.AddChild(Part(Box(0.5f, 0.02f, 0.25f), (k + r) % 2 == 0 ? white : black, new Vector3(-(cells - 1) * 0.25f + k * 0.5f, 0.03f, (r - 0.5f) * 0.25f)));
        // Winter: snow on the chords and drifted at the feet.
        if (winter)
        {
            Color snow = new(0.95f, 0.96f, 0.99f);
            solid.AddChild(Part(Box(len - 0.4f, 0.1f, 0.18f), snow, new Vector3(0, Top + 0.1f, 0)));
            foreach (float s in new[] { -1f, 1f })
            {
                var drift = Part(Ball(0.6f, 6), snow, new Vector3(s * half, 0.1f, 0.3f));
                drift.Scale = new Vector3(1.1f, 0.4f, 0.9f);
                solid.AddChild(drift);
            }
        }
        DrunkTractor.BatchStatic(solid, null);
        n.AddChild(solid);
        // The banner in the middle bays (panel width = most of the span), double sided so both ways through read.
        float bw = len - 1.6f;
        n.AddChild(Panel("CTAPT  *  ФИHИШ", bw, 0.8f, new Vector3(0, (Top + Low) / 2f, 0.09f), SovietRed, Gold));
        var rear = Panel("CTAPT  *  ФИHИШ", bw, 0.8f, new Vector3(0, (Top + Low) / 2f, -0.09f), SovietRed, Gold);
        rear.RotationDegrees = new Vector3(0, 180, 0);
        n.AddChild(rear);
        // Two floodlights on the chord, glowing at night, and a light for the line.
        foreach (float s in new[] { -1f, 1f })
            n.AddChild(NightGlow.Part(Box(0.5f, 0.2f, 0.35f), steelLight, new Color(1f, 0.92f, 0.7f), 2f, new Vector3(s * (half * 0.5f), Top + 0.22f, 0.3f)));
        var lamp = new OmniLight3D { Position = new Vector3(0, 4.6f, 0.6f), LightColor = new Color(1f, 0.9f, 0.7f), OmniRange = 14f, ShadowEnabled = false, LightEnergy = 0f };
        lamp.SetMeta("night_energy", 1.3f);
        n.AddChild(lamp);
        return n;
    }

    /// <summary>A round bale on its side (axis along X), settled under its own weight: the rolled layers
    /// show on both ends, twine bands round it, loose straw sticking out and lying about its foot;
    /// the shade and the stray straw differ from bale to bale. One batched mesh.</summary>
    static Node3D HayBale(int seed)
    {
        // Five variants of the roll (shade, the spiral of the ends, the straw), shared by the bales of a field; each bale is turned its own way.
        int variant = (seed % 5 + 5) % 5;
        var n = new Node3D();
        n.AddChild(BatchedCopy("hay" + variant, () => HayBody(variant * 7919 + 17)));
        return Fells(n, 1.7f, 0.9f, 0f);     // knocked over by the car (Breakables): it tips, the car ploughs on
    }

    static Node3D HayBody(int seed)
    {
        var n = new Node3D();
        float Rand(int k) => Noise.Hash01(seed, k, 71);
        Color hay = Hay.Lerp(new Color(0.74f, 0.62f, 0.30f), Rand(1) * 0.7f);       // fresh or a little weathered
        Color dark = hay.Darkened(0.09f), light = hay.Lightened(0.05f);
        Color twine = new(0.80f, 0.76f, 0.60f);
        const float R = 0.9f, W = 1.3f, Y = 0.85f;
        void Add(Mesh mesh, Color c, Vector3 at, Vector3 rot) => n.AddChild(Part(mesh, c, at, rot));
        // The body, a touch flattened at the bottom (settled).
        var body = Part(Cyl(R, W, 14), hay, new Vector3(0, Y, 0), new Vector3(0, 0, 90));
        body.Scale = new Vector3(0.94f, 1f, 1f);
        n.AddChild(body);
        // Both ends: the rolled layers, thin and close, wandering off centre towards the core like a
        // spiral (soft shades, not a target).
        Vector2 core = new((Rand(2) - 0.5f) * 0.16f, (Rand(3) - 0.5f) * 0.16f);
        foreach (float side in new[] { -1f, 1f })
        {
            const int Rings = 8;
            for (int k = 0; k < Rings; k++)
            {
                float u = (k + 1f) / Rings;
                float r = R * 0.92f * (1f - u * 0.9f);
                Vector2 c = core * u + new Vector2(MathF.Cos(k * 2.1f), MathF.Sin(k * 2.1f)) * 0.012f * k;
                Color shade = k == Rings - 1 ? dark.Darkened(0.06f) : k % 2 == 0 ? dark : light;
                var ring = Part(Cyl(r, 0.02f, 12), shade, new Vector3(side * (W / 2 + 0.004f + k * 0.004f), Y + c.Y * 0.94f, c.X), new Vector3(0, 0, 90));
                ring.Scale = new Vector3(1f, 1f, 0.94f + 0.06f * u);     // flattened like the body, rounder at the core
                n.AddChild(ring);
            }
        }
        // Twine bands round the roll.
        foreach (float x in new[] { -0.42f, -0.14f, 0.14f, 0.42f })
        {
            var band = Part(Cyl(R + 0.012f, 0.035f, 14), twine, new Vector3(x, Y, 0), new Vector3(0, 0, 90));
            band.Scale = new Vector3(0.94f, 1f, 1f);
            n.AddChild(band);
        }
        // Loose straw sticking out of the roll, more at the ends.
        for (int k = 0; k < 7; k++)
        {
            float a = Rand(10 + k) * MathF.Tau;
            float x = (Rand(20 + k) - 0.5f) * W * 1.05f;
            float r = R * 0.98f;
            var at = new Vector3(x, Y + MathF.Sin(a) * r * 0.94f, MathF.Cos(a) * r);
            if (at.Y < 0.15f) continue;
            Add(Box(0.03f, 0.32f, 0.03f), light, at, new Vector3(Mathf.RadToDeg(-a) + 90f + (Rand(30 + k) - 0.5f) * 50f, (Rand(40 + k) - 0.5f) * 60f, 0));
        }
        // Stalks lying about its foot.
        for (int k = 0; k < 5; k++)
        {
            float a = Rand(50 + k) * MathF.Tau, d = 1.0f + Rand(60 + k) * 0.6f;
            Add(Box(0.45f, 0.02f, 0.04f), light, new Vector3(MathF.Cos(a) * d, 0.03f, MathF.Sin(a) * d), new Vector3(0, Rand(70 + k) * 180f, 0));
        }
        DrunkTractor.BatchStatic(n, null);
        return n;
    }

    /// <summary>Double-sided text panel. Panel spans local X, faces ±Z.</summary>
    static MeshInstance3D Panel(string text, float w, float h, Vector3 pos, Color bg, Color fg)
    {
        Image img = PixelFont.RenderImage(text, fg, bg, 3);
        var mat = new StandardMaterial3D
        {
            AlbedoTexture = ImageTexture.CreateFromImage(img),
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        var mi = new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(w, h) },
            MaterialOverride = mat,
            Position = pos,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        return mi;
    }
}
