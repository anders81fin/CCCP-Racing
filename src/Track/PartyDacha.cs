using System;
using System.Collections.Generic;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Track;

/// <summary>The party's lakeside rest home, with one shared layout for surfaces, access and collision.</summary>
public static partial class PartyDacha
{
    public static readonly Vector2 Site = new(220, 176);
    public static readonly Vector2 Gate = new(256, 176);
    /// <summary>The paved car park: five diagonal bays (3 m wide, 5.4 m deep) along ParkN, centred on ParkC; the official cars stand in the second and third.</summary>
    public static readonly Vector2 ParkC = new(248.5f, 194f);
    public static readonly Vector2 ParkN = new(1f, 0f), ParkA = new(0f, 1f);       // (along the row; towards the rear, away from the court)
    public static readonly Vector2[] ParkLink = { new(246f, 183.2f), new(246f, 191f) };      // the paved link from the court's south side to the car park
    public static Vector2 Bay(int i) => ParkC + ParkN * 3f * (i - 1.5f);
    public static readonly Vector2 Car = Bay(1);
    public static readonly Vector2 FinnishCar = Bay(2);
    /// <summary>Inside the paved car park (an oriented rectangle 16 m by 7 m), grown by margin.</summary>
    public static bool InParking(Vector2 p, float margin = 0f) { var d = p - ParkC; return MathF.Abs(d.Dot(ParkN)) < 6.2f + margin && MathF.Abs(d.Dot(ParkA)) < 3.5f + margin; }
    public const float CarYaw = 0f; // Cars stand nose-in facing north, towards the court they came from.
    public const float HalfWidth = 9f, HalfDepth = 6f;
    public static readonly Vector2[] Drive = { new(286, 176), Gate, new(239, 176) };       // straight: from the road to the gate and on to the court
    public static readonly Vector2[] Walk = { new(239, 176), new(233, 183), new(233, 187), new(220, 187), new(220, 195) };
    // The hedge: an irregular loop, not a box: its north face on the field's edge, then round the pond at 6-8 m and away from the roads, leaving the fishermen at the river outside it; open where the river leaves.
    public static readonly Vector2[][] Boundary = {
        new[] { new Vector2(227.4f, 225f), new Vector2(220, 227), new Vector2(214.3f, 226.2f), new Vector2(209, 224.1f), new Vector2(205.4f, 219.6f), new Vector2(202.3f, 215.2f), new Vector2(200.2f, 210.3f), new Vector2(198, 205), new Vector2(192, 201), new Vector2(186, 196), new Vector2(183.5f, 190), new Vector2(183.5f, 176), new Vector2(185, 166), new Vector2(189.5f, 161.8f), new Vector2(250, 161.8f), new Vector2(256, 165), new Vector2(256, 172) },       // (south-west it keeps clear of the slurry tractor's circuit)
        new[] { new Vector2(256, 180), new Vector2(256, 185), new Vector2(258.2f, 189), new Vector2(258.2f, 198), new Vector2(255, 207.5f), new Vector2(248.5f, 214.5f), new Vector2(241.5f, 219f), new Vector2(236.2f, 220.8f) },       // (the east side bulges out round the car park; both ends meet the river's banks across from each other)
    };
    static readonly Vector2[] Lot = { new(189.5f, 161.8f), new(250, 161.8f), new(256, 165), new(256, 185), new(258.2f, 189), new(258.2f, 198), new(255, 207.5f), new(248.5f, 214.5f), new(241.5f, 219f), new(236.2f, 220.8f), new(227.4f, 225f), new(220, 227), new(214.3f, 226.2f), new(209, 224.1f), new(205.4f, 219.6f), new(202.3f, 215.2f), new(200.2f, 210.3f), new(198, 205), new(192, 201), new(186, 196), new(183.5f, 190), new(183.5f, 176), new(185, 166) };

    const float HedgeStep = 1.0f;
    public static bool InGrounds(Vector2 p) => Geometry2D.IsPointInPolygon(p, Lot);
    public static bool OnDrive(Vector2 p, float extra = 0) => Village.NearAccessPath(p, Drive, 2.2f + extra);
    public static bool Reserved(Vector2 p) => InGrounds(p) || OnDrive(p, 3);

    /// <summary>A level lakeside estate, matched to the actual water line after the pond is carved.</summary>
    public static void GradeGrounds(TrackData t) { foreach (var _ in GradeGroundsSteps(t)) { } }

    /// <summary>GradeGrounds in slices: every item is a point where a frame may go by (a loading card keeps running).</summary>
    public static IEnumerable<int> GradeGroundsSteps(TrackData t)
    {
        if (float.IsNaN(t.WaterLevel)) yield break;
        var slice = System.Diagnostics.Stopwatch.StartNew();
        for (int z = 0; z <= t.N; z++)
        {
        if (slice.ElapsedMilliseconds > 10) { yield return 0; slice.Restart(); }
        for (int x = 0; x <= t.N; x++)
        {
            var p = new Vector2(x * t.Tile, z * t.Tile);
            float dx = Math.Max(0, Math.Max(200 - p.X, p.X - 262));
            float dz = Math.Max(0, Math.Max(162 - p.Y, p.Y - 200));
            float distance = MathF.Sqrt(dx * dx + dz * dz);
            if (distance >= 8) continue;
            bool inPond = false;
            foreach (var pond in t.Water.Ponds)
                inPond |= p.DistanceTo(new(pond.X, pond.Z)) < pond.R;
            if (inPond) continue; // Preserve the carved bed and the shoreline.
            int road = t.NearestCenter(p, out float roadDistance);
            if (roadDistance < t.HalfWidth[road] + 1.5f) continue;
            int index = z * (t.N + 1) + x;
            t.Heights[index] = Mathf.Lerp(t.Heights[index], t.WaterLevel, 1 - Mathf.SmoothStep(0, 8, distance));
        }
        }
    }

    public static void GradeAccess(TrackData t) { foreach (var _ in GradeAccessSteps(t)) { } }

    /// <summary>GradeAccess in slices (see GradeGroundsSteps).</summary>
    public static IEnumerable<int> GradeAccessSteps(TrackData t)
    {
        var slice = System.Diagnostics.Stopwatch.StartNew();
        var heights = new float[Drive.Length];
        float entryHeight = t.MeshHeight(Drive[0].X, Drive[0].Y);
        float length = 0;
        length = Drive[0].DistanceTo(Drive[1]);      // the drive reaches the lake's level at the gate and stays level through the court
        float travelled = 0;
        for (int i = 0; i < Drive.Length; i++)
        {
            if (i > 0) travelled += Drive[i - 1].DistanceTo(Drive[i]);
            heights[i] = Mathf.Lerp(entryHeight, t.WaterLevel, Math.Clamp(travelled / length, 0, 1));
        }
        for (int z = 0; z <= t.N; z++)
        {
        if (slice.ElapsedMilliseconds > 10) { yield return 0; slice.Restart(); }
        for (int x = 0; x <= t.N; x++)
        {
            var p = new Vector2(x * t.Tile, z * t.Tile);
            int ci = t.NearestCenter(p, out float roadDistance);
            if (roadDistance < t.HalfWidth[ci]) continue;
            float best = 5, height = 0;
            for (int i = 0; i + 1 < Drive.Length; i++)
            {
                var segment = Drive[i + 1] - Drive[i];
                float u = Math.Clamp((p - Drive[i]).Dot(segment) / segment.LengthSquared(), 0, 1);
                float d = p.DistanceTo(Drive[i] + segment * u);
                if (d < best) { best = d; height = Mathf.Lerp(heights[i], heights[i + 1], u); }
            }
            if (best >= 5) continue;
            float blend = 1 - Mathf.SmoothStep(2.5f, 5, best);
            int index = z * (t.N + 1) + x;
            t.Heights[index] = Mathf.Lerp(t.Heights[index], height, blend);
        }
        }
    }

    public static void PaintSurfaces(TrackData t) { foreach (var _ in PaintSurfacesSteps(t)) { } }

    /// <summary>PaintSurfaces in slices (see GradeGroundsSteps).</summary>
    public static IEnumerable<int> PaintSurfacesSteps(TrackData t)
    {
        if (t.Id != "ilyich") yield break;
        var slice = System.Diagnostics.Stopwatch.StartNew();
        for (int z = 0; z < t.N; z++)
        {
        if (slice.ElapsedMilliseconds > 10) { yield return 0; slice.Restart(); }
        for (int x = 0; x < t.N; x++)
        {
            var p = new Vector2((x + 0.5f) * t.Tile, (z + 0.5f) * t.Tile);
            int ci = t.NearestCenter(p, out float d);
            if (d < t.HalfWidth[ci]) continue;
            if (t.Surf[z * t.N + x] is SurfaceType.Water or SurfaceType.Shallow) continue;
            bool turning = InCourt(p);
            if (InParking(p) || turning) t.Surf[z * t.N + x] = SurfaceType.Concrete;      // the car park and the court: paved
            else if (OnDrive(p) || turning) t.Surf[z * t.N + x] = SurfaceType.Gravel;
            else if (InGrounds(p)) t.Surf[z * t.N + x] = SurfaceType.Grass;
            else if (p.X > 250f && p.X < 283f && p.Y > 156f && p.Y < 200f && t.Surf[z * t.N + x] is SurfaceType.Mud or SurfaceType.Dirt) t.Surf[z * t.N + x] = SurfaceType.Grass;      // the mud patches beside the entrance drive: lawn
        }
        }
    }

    /// <summary>The plainclothes security: where each stands and faces (their pose is their index).</summary>
    public static readonly (Vector2 At, float Yaw)[] Guards = { (new(242.5f, 173.7f), Mathf.Pi), (new(226f, 187f), -0.35f), (new(207f, 170f), 0.8f) };

    public static IEnumerable<(Vector2 At, float Radius)> SolidFootprints()
    {
        // A grid of circles covers the log palace's complete rectangular footprint.
        for (float x = -7.5f; x <= 7.5f; x += 3)
        for (float z = -4.5f; z <= 4.5f; z += 3)
            yield return (Site + new Vector2(x, z), 2.12f);
        foreach (float x in new[] { -9.3f, -5.7f, 5.7f, 9.3f })
            yield return (Site + new Vector2(x, 9.15f), 0.24f);
        foreach (float cz in new[] { -2.85f, -1.45f, 1.45f, 2.85f }) yield return (Site + new Vector2(13.0f, cz), 0.3f);       // the porch's columns
        foreach (float cz in new[] { -3.2f, -1.6f, 0f, 1.6f, 3.2f }) yield return (Site + new Vector2(14.3f, cz), 0.8f);        // its steps and urns
        yield return (Site + new Vector2(0, 10.25f), 1.75f);
        var carAxis = new Basis(Vector3.Up, CarYaw) * new Vector3(0, 0, 1.25f);
        var carOffset = new Vector2(carAxis.X, carAxis.Z);
        foreach (var solid in CourtSolids()) yield return solid;
        foreach (var parked in new[] { Bay(0), Car, FinnishCar, Bay(3) })
        {
            yield return (parked - carOffset, 1.1f);
            yield return (parked + carOffset, 1.1f);
        }
        yield return (Gate + new Vector2(0, -4), 0.65f);
        yield return (Gate + new Vector2(0, 4), 0.65f);
        foreach (var run in Boundary)
            for (int i = 0; i + 1 < run.Length; i++)
            {
                int count = (int)MathF.Ceiling(run[i].DistanceTo(run[i + 1]) / HedgeStep);
                for (int j = 0; j <= count; j++) yield return (run[i].Lerp(run[i + 1], j / (float)count), 0.62f);
            }
    }

    public static Node3D Grounds(TrackData t)
    {
        Node3D result = null;
        foreach (var step in GroundsSteps(t)) result = step ?? result;
        return result;
    }

    /// <summary>The same build in slices (a null between them: a frame may go by while a loading card runs); the last item is the grounds.
    /// A build abandoned between slices frees the half-made grounds.</summary>
    public static IEnumerable<Node3D> GroundsSteps(TrackData t)
    {
        var root = new Node3D { Name = "PartyDachaGrounds" };
        bool handedOver = false;
        try
        {
        if (t.Id != "ilyich") { handedOver = true; yield return root; yield break; }
        var slice = System.Diagnostics.Stopwatch.StartNew();
        MeshInstance3D yard = null;
        foreach (var step in YardSurfaceSteps(t)) { if (step != null) yard = step; else { yield return null; slice.Restart(); } }
        root.AddChild(yard);
        var objects = new Node3D(); root.AddChild(objects);
        void Box(Vector3 size, Vector3 at, Color col, Vector3 rotation = default) => objects.AddChild(Props.Part(Props.Box(size.X, size.Y, size.Z), col, at, rotation));
        Vector3 At(Vector2 p, float y = 0) => new(p.X, t.MeshHeight(p.X, p.Y) + y, p.Y);
        var cream = new Color(0.78f, 0.75f, 0.59f); var green = new Color(0.20f, 0.29f, 0.21f);
        // The boundary is a clipped thuja hedge, swept along each run as one continuous leafy surface (PartyDacha.Hedge.cs); not batched with the small things.
        var hedges = new Node3D { Name = "ThujaHedges" }; root.AddChild(hedges);
        foreach (var run in Boundary) hedges.AddChild(HedgeRun(t, run));
        foreach (float z in new[] { -4f, 4f })
        {
            Vector3 at = At(Gate + new Vector2(0, z));
            Box(new Vector3(1.2f, 2.7f, 1.2f), at + Vector3.Up * 1.35f, new Color(0.58f, 0.55f, 0.44f));
            Box(new Vector3(1.5f, 0.18f, 1.5f), at + Vector3.Up * 2.75f, cream);
            objects.AddChild(Star(at + new Vector3(0.64f, 2.1f, 0), 0.40f, new Vector3(0, 90, 0)));
        }
        // The car park: white bay lines between the bays, a low concrete wheel stop at the head of each bay, a granite kerb along the back, a kerbed edge on the lawn side.
        for (int i = 0; i <= 4; i++)
        {
            Vector2 lc = ParkC + ParkN * (3f * (i - 2));
            Box(new Vector3(0.14f, 0.03f, 5.3f), At(lc, 0.04f), new Color(0.92f, 0.92f, 0.86f), new Vector3(0, Mathf.RadToDeg(CarYaw), 0));
        }
        for (int i = 0; i < 4; i++)
            Box(new Vector3(1.5f, 0.14f, 0.24f), At(Bay(i) - ParkA * 2.2f, 0.07f), new Color(0.6f, 0.6f, 0.57f), new Vector3(0, Mathf.RadToDeg(CarYaw) + 90f, 0));
        for (float k = -6.2f; k <= 6.3f; k += 0.9f)
        {
            Box(new Vector3(0.85f, 0.2f, 0.3f), At(ParkC + ParkN * k + ParkA * 3.55f, 0.1f), new Color(0.5f, 0.5f, 0.48f), new Vector3(0, Mathf.RadToDeg(CarYaw) + 90f, 0));
        }
        // The white boom is raised beside the northern gatepost, leaving a clear car-width opening.
        Box(new Vector3(0.16f, 4.6f, 0.16f), At(Gate + new Vector2(0.5f, -3.8f), 3.5f), cream, new Vector3(12, 0, 0));
        objects.AddChild(OfficialCar(At(Car), yaw: CarYaw));
        objects.AddChild(OfficialCar(At(FinnishCar), finnishFlags: true, yaw: CarYaw));
        objects.AddChild(OfficialCar(At(Bay(0)), yaw: CarYaw));
        objects.AddChild(OfficialCar(At(Bay(3)), yaw: CarYaw));      // (four cars fill all four bays)
        BuildCourt(objects, (p, y) => At(p, y));

        Node3D Agent(Vector2 p, float yaw, int pose)
        {
            var agent = new Node3D { Name = "PlainclothesSecurity", Position = At(p), Rotation = new Vector3(0, yaw, 0), Scale = Vector3.One * KolkhozWorkers.SizeScale };
            Color black = new(0.035f, 0.043f, 0.047f), dark = new(0.075f, 0.085f, 0.088f);
            Color skin = new(0.62f, 0.43f, 0.33f), shirt = new(0.70f, 0.68f, 0.58f);
            void Part(Vector3 size, Vector3 at, Color color, Vector3 rot = default) =>
                agent.AddChild(Props.Part(Props.Box(size.X, size.Y, size.Z), color, at, rot));
            Part(new(0.58f, 0.67f, 0.34f), new(0, 1.08f, 0), black);
            Part(new(0.20f, 0.20f, 0.06f), new(0, 1.43f, -0.19f), shirt);
            Part(new(0.075f, 0.31f, 0.045f), new(0, 1.24f, -0.21f), new Color(0.45f, 0.06f, 0.045f));
            Part(new(0.28f, 0.38f, 0.28f), new(-0.17f, 0.39f, 0), black);
            Part(new(0.28f, 0.38f, 0.28f), new(0.17f, 0.39f, 0), dark);
            Part(new(0.16f, 0.16f, 0.15f), new(0, 1.58f, 0), skin);
            Part(new(0.39f, 0.16f, 0.36f), new(0, 1.72f, 0.01f), black);
            Part(new(0.47f, 0.09f, 0.40f), new(0, 1.79f, -0.005f), black);
            if (pose == 0)
            {
                Part(new(0.14f, 0.48f, 0.16f), new(-0.37f, 1.03f, -0.02f), black, new(-10, 0, -8));
                Part(new(0.14f, 0.48f, 0.16f), new(0.37f, 1.03f, 0.06f), black, new(12, 0, 10));
                Part(new(0.14f, 0.13f, 0.14f), new(0.37f, 0.79f, 0.07f), skin);
            }
            else if (pose == 1)
            {
                Part(new(0.14f, 0.50f, 0.16f), new(-0.36f, 1.18f, -0.06f), black, new(-30, 0, -14));
                Part(new(0.14f, 0.50f, 0.16f), new(0.34f, 1.14f, -0.05f), black, new(-24, 0, 12));
                Part(new(0.12f, 0.14f, 0.12f), new(0, 0.08f, -0.07f), new(0.12f, 0.12f, 0.12f));
            }
            else
            {
                Part(new(0.14f, 0.45f, 0.16f), new(-0.36f, 1.04f, 0.02f), black, new(8, 0, -10));
                Part(new(0.14f, 0.45f, 0.16f), new(0.36f, 1.10f, -0.02f), black, new(-18, 0, 14));
                Part(new(0.10f, 0.16f, 0.10f), new(0.35f, 0.84f, -0.11f), skin);
            }
            return agent;
        }

        // Gate, pond and garden posts keep overlapping sightlines across the estate. They stay out of
        // the batched mesh: hit by the car each falls over and gets up (KnockGroup, Guards' obstacles);
        // the pond and garden men walk their beats (DachaPatrol).
        var guards = new KnockGroup { Name = "DachaGuards" };
        Tape.Version(guards, 1); Tape.Part(guards, "root");      // the world tape: the three security men (place, turn, tilt, legs)
        objects.AddChild(guards);
        for (int i = 0; i < Guards.Length; i++)
        {
            var agent = Agent(Guards[i].At, Guards[i].Yaw, i);
            guards.AddChild(agent);
            guards.Add(agent, t, i < t.DachaGuardObstacles.Count ? t.DachaGuardObstacles[i] : -1);
        }
        guards.AddChild(new DachaPatrol(t, guards));      // the gate post, the pond's shore and the garden's beat
        // The footbridge reaches from dry land to a broad landing on the pond.
        float deckY = t.WaterLevel + 0.45f;
        for (float z = 187; z <= 196; z += 0.45f)
            Box(new Vector3(2.5f, 0.15f, 0.41f), new Vector3(220, deckY, z), new Color(0.49f, 0.35f, 0.20f));
        for (float x = 217.5f; x <= 222.5f; x += 0.45f)
            Box(new Vector3(0.41f, 0.15f, 2.5f), new Vector3(x, deckY, 197), new Color(0.49f, 0.35f, 0.20f));
        foreach (float z in new[] { 190f, 194f, 198f })
        foreach (float x in new[] { 218.9f, 221.1f })
        {
            float bottom = t.MeshHeight(x, z), top = deckY + 0.3f;
            Box(new Vector3(0.18f, top - bottom, 0.18f), new Vector3(x, (bottom + top) / 2, z), green);
        }
        yield return null; slice.Restart();
        foreach (var _ in DrunkTractor.BatchStaticSteps(objects, guards)) { yield return null; slice.Restart(); }
        handedOver = true;
        yield return root;
        }
        finally { if (!handedOver) root.Free(); }
    }

    /// <summary>The same lantern posts in pairs along the entrance drive (both its edges, every 9 m from the road to the gate and in to the turning circle).</summary>
    public static IEnumerable<Vector2> DriveLampSites(TrackData t)
    {
        if (t.Id != "ilyich") yield break;
        float next = 4f;
        for (int i = 0; i + 1 < Drive.Length; i++)
        {
            Vector2 a = Drive[i], b = Drive[i + 1], dir = (b - a).Normalized(), n = new(-dir.Y, dir.X);
            float len = a.DistanceTo(b);
            for (; next < len - 0.5f; next += 9f)
            {
                Vector2 c = a + dir * next;
                if (c.DistanceTo(Gate) < 3.5f) continue;                      // the gate posts' own ground
                foreach (float side in new[] { -1f, 1f })
                {
                    Vector2 p = c + n * side * 3.5f;
                    int road = t.NearestCenter(p, out float roadDistance);
                    if (roadDistance < t.HalfWidth[road] + 2.5f) continue;
                    if (t.Sample(p.X, p.Y) is var g && (g.WaterDepth > 0f || g.Surface is SurfaceType.Water or SurfaceType.Shallow)) continue;
                    yield return p;
                }
            }
            next -= len;
        }
    }

    /// <summary>Where the lantern posts stand on the pond's bank: round it, dry ground, off the estate, the drive and the road.</summary>
    public static IEnumerable<Vector2> PondLampSites(TrackData t)
    {
        if (t.Id != "ilyich" || float.IsNaN(t.WaterLevel) || t.Water?.Ponds == null) yield break;
        foreach (var pond in t.Water.Ponds)
        {
            var centre = new Vector2(pond.X, pond.Z);
            for (int k = 0; k < 8; k++)
            {
                float angle = (k + 0.5f) * MathF.Tau / 8f;
                for (float r = pond.R + 2f; r <= pond.R + 7f; r += 1.5f)
                {
                    var p = centre + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * r;
                    if (OnDrive(p, 3)) break;
                    int road = t.NearestCenter(p, out float roadDistance);
                    if (roadDistance < t.HalfWidth[road] + 3f) break;
                    if (t.Sample(p.X, p.Y) is var g && (g.WaterDepth > 0f || g.Surface is SurfaceType.Water or SurfaceType.Shallow)) continue;      // still wet: a little further out
                    yield return p;
                    break;
                }
            }
        }
    }
}
