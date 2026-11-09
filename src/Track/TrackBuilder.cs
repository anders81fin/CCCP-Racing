using System;
using System.Collections.Generic;
using Godot;

namespace CCCPRacing.Track;

/// <summary>Turns a TrackDef into TrackData: rolling kolkhoz terrain with a road carved along a closed spline.</summary>
public static partial class TrackBuilder
{
    const float EdgeMargin = 34f;     // hills rise near map edges to hide the end of the world
    const float BlendOuter = 7f;      // distance beyond road edge where terrain is back to natural

    public static TrackData Build(TrackDef def)
    {
        TrackData result = null;
        foreach (var step in BuildSteps(def)) result = step ?? result;
        return result;
    }

    /// <summary>The same build in slices (a null between them: a frame may go by while a loading card runs); the last item is the finished track.</summary>
    public static IEnumerable<TrackData> BuildSteps(TrackDef def)
    {
        var slice = System.Diagnostics.Stopwatch.StartNew();
        // Restart after resuming each checkpoint: the caller may spend a frame away from
        // this iterator, and that wait must not count against the next slice's work budget.
        BonusCampLayout.Apply(def);
        var t = new TrackData
        {
            Id = def.Id,
            Name = def.Name,
            N = def.Size,
            Tile = def.TileSize,
            Water = def.Water,
            Meadow = def.Meadow,
        };
        int n = t.N, nv = n + 1;
        float world = t.WorldSize;

        // 1. Natural terrain.
        var baseH = new float[nv * nv];
        for (int iz = 0; iz < nv; iz++)
        {
        if (slice.ElapsedMilliseconds > 10) { LoadProf.At(); yield return null; slice.Restart(); }
        for (int ix = 0; ix < nv; ix++)
        {
            float x = ix * t.Tile, z = iz * t.Tile;
            baseH[iz * nv + ix] = NaturalHeight(def, x, z, world);
        }
        }

        LoadProf.At(); yield return null;
        slice.Restart();
        // 2. Centre line.
        BuildCenterLine(def, t, out float[] lift);
        if(def.ReverseFrom>=0) Reorient(def,t,lift);
        ShapeRoadEdges(def,t);

        LoadProf.At(); yield return null;
        slice.Restart();
        // 3. Road height: terrain under the centre line, smoothed hard so the road flows, plus authored lift.
        int cn = t.Center.Length;
        var roadH = new float[cn];
        for (int i = 0; i < cn; i++)
            roadH[i] = NaturalHeight(def, t.Center[i].X, t.Center[i].Y, world);
        for (int pass = 0; pass < 3; pass++) roadH = BoxBlur(roadH, def.RoadSmoothing);
        for (int i = 0; i < cn; i++) roadH[i] += lift[i];
        for (int pass = 0; pass < 2; pass++) roadH = BoxBlur(roadH, 3);
        // Authored crests, dips and jumps go on last so they keep their shape.
        foreach (var f in def.Features) ApplyFeature(roadH, f);
        GradeFrozenValleyRoad(def, t, roadH);
        t.CenterH = roadH;
        foreach(var feature in def.Features)
            if(feature.TerrainWidth>0)
            {
                var profile=new float[cn];ApplyFeature(profile,feature);
                t.RoadLandforms.Add(new RoadLandform(profile,feature.TerrainWidth));
            }
        MaintenanceHall.Prepare(t,def.MaintenanceHall);

        LoadProf.At(); yield return null;
        slice.Restart();
        float landformReach=30;
        foreach(var form in t.RoadLandforms)landformReach=Math.Max(landformReach,form.Width);
        // 4. Carve road and ditches into the vertex grid.
        t.Heights = new float[nv * nv];
        var grid = new CenterGrid(t.Center, 8f);
        for (int iz = 0; iz < nv; iz++)
        {
        if (slice.ElapsedMilliseconds > 10) { LoadProf.At(); yield return null; slice.Restart(); }
        for (int ix = 0; ix < nv; ix++)
        {
            var p = new Vector2(ix * t.Tile, iz * t.Tile);
            int ci = grid.Nearest(p, landformReach, out float d);
            float h = baseH[iz * nv + ix]+t.RoadLandformHeight(ci,d);
            if (ci >= 0)
            {
                float hw = t.RoadHalfWidth(ci,p);
                float blend = 1f - SmoothStep(hw + 0.5f, hw + BlendOuter, d);
                h = Mathf.Lerp(h, roadH[ci], blend);
                // Drainage ditch beside the road: the Soviet engineer's gift to your suspension.
                if (d > hw + 1.2f && d < hw + 5f)
                    h -= 0.45f * MathF.Sin((d - hw - 1.2f) / 3.8f * MathF.PI);
            }
            t.Heights[iz * nv + ix] = h;
        }
        }

        foreach(var _ in ShapeMeadowSteps(def,t,grid)) {LoadProf.At();yield return null;slice.Restart();}

        // Bus stops: shelter beyond the verge, on a small paved pad so it doesn't sit in the ditch.
        var pads = new List<PadDef>(def.Pads);
        foreach (var b in def.BusStops)
        {
            int bi = ((int)b.At % cn + cn) % cn;
            Vector2 dir = t.CenterDir(bi), right = new(-dir.Y, dir.X);
            float sign = b.Side == "left" ? -1f : 1f;
            Vector2 pos = t.Center[bi] + right * sign * (t.HalfWidth[bi] + 3.4f);
            t.BusStops.Add(new BusStop(bi, pos, TrackData.YawFor(-right * sign), b.Drunks));
            pads.Add(new PadDef { X0 = pos.X - 3.5f, Z0 = pos.Y - 3.5f, X1 = pos.X + 3.5f, Z1 = pos.Y + 3.5f, Surface = "concrete", Height = roadH[bi] });
        }

        // A fuel depot out on the lap (TrackDef.Depot) stands on a level concrete pad: the stopping
        // zone and the tank (FuelDepot puts them: the zone Off metres out, the tank 4.6 m further).
        if (def.Depot is { } dp)
        {
            int di = (dp.At % cn + cn) % cn;
            var site = FuelDepot.Layout(t, dp);
            Vector2 zone = site.Zone, tank = site.Tank;
            Vector2 lo = zone.Min(tank) - new Vector2(4f, 4f), hi = zone.Max(tank) + new Vector2(4f, 4f);
            pads.Add(new PadDef { X0 = lo.X, Z0 = lo.Y, X1 = hi.X, Z1 = hi.Y, Surface = "concrete", Height = roadH[di] });
        }

        LoadProf.At(); yield return null;
        slice.Restart();
        // 4b. Pads (airfield apron, hangar floor, bus stops, a depot): flatten with a 6 m soft edge.
        var padHeights = new List<float>();
        foreach (var pad in pads)
        {
            var mid = new Vector2((pad.X0 + pad.X1) / 2f, (pad.Z0 + pad.Z1) / 2f);
            float ph = pad.Height ?? (pad.Ground ? t.MeshHeight(mid.X, mid.Y) : roadH[t.NearestCenter(mid, out _)]);
            if (t.Id == "bonus" && mid.X >= 72 && mid.X <= 214 && mid.Y >= 78 && mid.Y <= 125)
                ph = BonusAirbase.BaseHeight(t);
            padHeights.Add(ph);
            const float Soft = 6f;
            for (int iz = 0; iz < nv; iz++)
            {
            if (slice.ElapsedMilliseconds > 10) { LoadProf.At(); yield return null; slice.Restart(); }
            for (int ix = 0; ix < nv; ix++)
            {
                float x = ix * t.Tile, z = iz * t.Tile;
                float dx = Math.Max(0, Math.Max(pad.X0 - x, x - pad.X1));
                float dz = Math.Max(0, Math.Max(pad.Z0 - z, z - pad.Z1));
                float d = MathF.Sqrt(dx * dx + dz * dz);
                if (d >= Soft) continue;
                // Never reshape the road itself (bus stops sit on crests and dips): pads only
                // flatten the ground beside it.
                int rc = grid.Nearest(new Vector2(x, z), 12f, out float rd);
                if (rc >= 0 && rd < t.HalfWidth[rc] + 1.5f) continue;
                float w = 1f - SmoothStep(0f, Soft, d);
                t.Heights[iz * nv + ix] = Mathf.Lerp(t.Heights[iz * nv + ix], ph, w);
            }
            }
        }

        foreach (var prop in def.Props)
            if (prop.Type == "dormitory")
            {
                float length = float.TryParse(prop.Text, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float parsed) ? parsed : 84f;
                Village.GradeDormitory(t, new Vector2(prop.X, prop.Z), Mathf.DegToRad(prop.Rot), length);
            }
        LoadProf.At(); yield return null; slice.Restart();
        if (t.Id == "siberia") foreach (var _ in SiberiaRadar.GradeAccessSteps(t)) { LoadProf.At(); yield return null; slice.Restart(); }
        LoadProf.At(); yield return null; slice.Restart();
        foreach (var _ in BonusAirbase.GradeSteps(t)) { LoadProf.At(); yield return null; slice.Restart(); }
        LoadProf.At(); yield return null; slice.Restart();
        foreach (var _ in CarveWaterSteps(def, t, grid, roadH)) { LoadProf.At(); yield return null; slice.Restart(); }     // TrackBuilder.Water.cs: ponds, rivers, the ford
        LoadProf.At(); yield return null; slice.Restart();
        if (t.Id == "ilyich")
        {
            foreach (var _ in PartyDacha.GradeGroundsSteps(t)) { LoadProf.At(); yield return null; slice.Restart(); }
            LoadProf.At(); yield return null; slice.Restart();
            foreach (var _ in PartyDacha.GradeAccessSteps(t)) { LoadProf.At(); yield return null; slice.Restart(); }
            LoadProf.At(); yield return null; slice.Restart();
        }

        foreach (var _ in MaintenanceHall.GradeSteps(t)) { LoadProf.At(); yield return null; slice.Restart(); }

        LoadProf.At(); yield return null;
        slice.Restart();
        // 5. Tile surfaces.
        t.Surf = new SurfaceType[n * n];
        t.Variant = new byte[n * n];
        t.Verge = new bool[n * n];
        SurfaceType ground = Surfaces.Parse(def.Ground);
        bool winter = ground == SurfaceType.DeepSnow;
        t.Winter = winter;
        t.Autumn = !winter && def.Season == "autumn";
        t.Zone = def.Radiation != null;
        for (int iz = 0; iz < n; iz++)
        {
        if (slice.ElapsedMilliseconds > 10) { LoadProf.At(); yield return null; slice.Restart(); }
        for (int ix = 0; ix < n; ix++)
        {
            var p = new Vector2((ix + 0.5f) * t.Tile, (iz + 0.5f) * t.Tile);
            int ci = grid.Nearest(p, 30f, out float d);
            SurfaceType s = ground;
            float edgeWidth = ci >= 0 ? t.RoadHalfWidth(ci,p) : 0;
            if (ci >= 0 && d < edgeWidth)
            {
                s = t.CenterSurf[ci];
            }
            else if (ci >= 0 && d < edgeWidth + 1.6f)
            {
                s = winter ? SurfaceType.Snow : SurfaceType.Dirt;    // the verge: ploughed snow in winter
                t.Verge[iz * n + ix] = true;
            }
            else
            {
                foreach (var f in def.Fields)
                    if (p.X >= f.X0 && p.X <= f.X1 && p.Y >= f.Z0 && p.Y <= f.Z1)
                        s = Surfaces.Parse(f.Surface);
                if (s == SurfaceType.Grass && !winter && Noise.Value(p.X / 9f, p.Y / 9f, def.Seed + 9) > 0.62f)
                    s = SurfaceType.Mud;
            }
            t.Surf[iz * n + ix] = s;
            t.Variant[iz * n + ix] = (byte)(Noise.Hash01(ix, iz, def.Seed + 11) * 3.999f);
        }
        }

        // Pads get their own surface, except where the road runs through them.
        foreach (var pad in pads)
        {
            var ps = Surfaces.Parse(pad.Surface);
            for (int iz = 0; iz < n; iz++)
            {
            if (slice.ElapsedMilliseconds > 10) { LoadProf.At(); yield return null; slice.Restart(); }
            for (int ix = 0; ix < n; ix++)
            {
                float x = (ix + 0.5f) * t.Tile, z = (iz + 0.5f) * t.Tile;
                if (x < pad.X0 || x > pad.X1 || z < pad.Z0 || z > pad.Z1) continue;
                var cur = t.Surf[iz * n + ix];
                int roadIndex = grid.Nearest(new Vector2(x, z), 12f, out float roadDistance);
                if (cur is SurfaceType.Concrete or SurfaceType.Runway or SurfaceType.Gravel or SurfaceType.Mud or SurfaceType.Dirt
                        or SurfaceType.Snow or SurfaceType.Ice
                    && roadIndex >= 0 && roadDistance < (pad.ConnectRoad ? t.HalfWidth[roadIndex] : 8f)) continue;
                t.Surf[iz * n + ix] = ps;
                t.Verge[iz * n + ix] = false;
            }
            }
        }
        t.Pads.AddRange(pads);

        // Blight around the radar: everything green withers (the road stays road).
        t.Blights.AddRange(def.Blights);
        for (int iz = 0; iz < n; iz++)
        {
        if (slice.ElapsedMilliseconds > 10) { LoadProf.At(); yield return null; slice.Restart(); }
        for (int ix = 0; ix < n; ix++)
        {
            var cur = t.Surf[iz * n + ix];
            if (cur is SurfaceType.Grass or SurfaceType.Mud or SurfaceType.Field && t.InBlight(new Vector2((ix + 0.5f) * t.Tile, (iz + 0.5f) * t.Tile)))
                t.Surf[iz * n + ix] = SurfaceType.DeadGrass;
        }
        }

        foreach (var _ in WaterSurfacesSteps(def, t)) { LoadProf.At(); yield return null; slice.Restart(); }
        LoadProf.At(); yield return null; slice.Restart();
        foreach (var _ in PartyDacha.PaintSurfacesSteps(t)) { LoadProf.At(); yield return null; slice.Restart(); }
        LoadProf.At(); yield return null; slice.Restart();
        foreach (var _ in BonusAirbase.PaintSteps(t)) { LoadProf.At(); yield return null; slice.Restart(); }
        LoadProf.At(); yield return null; slice.Restart();
        foreach (var _ in MaintenanceHall.PaintSteps(t)) { LoadProf.At(); yield return null; slice.Restart(); }
        LoadProf.At(); yield return null; slice.Restart();
        if (t.Id == "siberia") foreach (var _ in SiberiaRadar.PaintSurfacesSteps(t)) { LoadProf.At(); yield return null; slice.Restart(); }
        LoadProf.At(); yield return null;
        slice.Restart();
        PlaceHazards(def, t);
        LoadProf.At(); yield return null;
        slice.Restart();

        // The front bumper (2.11 m ahead of the centre) waits 40 cm behind the line.
        Vector2 start = t.Center[0] - t.CenterDir(0) * 2.51f;
        t.StartPos = new Vector3(start.X, t.MeshHeight(start.X, start.Y) + 0.7f, start.Y);
        t.StartYaw = TrackData.YawFor(t.CenterDir(0));

        if (def.Snowbanks != null) t.Banks = new Snowbanks(t, def.Snowbanks);
        t.BabushkaCount = def.Babushkas;
        t.Runway = def.Runway;
        t.TankRange = def.TankRange;

        // 7. Props.
        LoadProf.At(); yield return null;
        slice.Restart();
        foreach (var _ in PlacePropsSteps(def, t, grid)) { LoadProf.At(); yield return null; slice.Restart(); }
        yield return t;
    }

    /// <summary>Adds one feature's height profile to the road (1 sample = 1 m).</summary>
    static void ApplyFeature(float[] roadH, FeatureDef f)
    {
        int n = roadH.Length;
        switch (f.Type)
        {
            case "crest":
            case "dip":
            {
                float sign = f.Type == "dip" ? -1f : 1f;
                int half = (int)(f.Length / 2);
                for (int k = -half; k <= half; k++)
                {
                    float u = k / (float)half;
                    roadH[((int)f.At + k + n) % n] += sign * f.Height * 0.5f * (1f + MathF.Cos(u * MathF.PI));
                }
                break;
            }
            case "level":
            {
                // Runway: a straight, even grade (least-squares fit of the stretch), so no humps
                // but no cliff at the ends either; eased in/out over 20 m.
                int len = Math.Max(2, (int)f.Length), from = (int)f.At;
                float sx = 0, sy = 0, sxx = 0, sxy = 0;
                for (int k = 0; k < len; k++)
                {
                    float y = roadH[(from + k) % n];
                    sx += k; sy += y; sxx += k * k; sxy += k * y;
                }
                float slope = (len * sxy - sx * sy) / (len * sxx - sx * sx);
                float icpt = (sy - slope * sx) / len;
                const int Ease = 35;
                for (int k = -Ease; k < len + Ease; k++)
                {
                    float w = k < 0 ? 1f + k / (float)Ease : k >= len ? 1f - (k - len + 1) / (float)Ease : 1f;
                    w = Math.Clamp(w, 0f, 1f);
                    w = w * w * (3 - 2 * w);
                    int i = ((from + k) % n + n) % n;
                    float lineH = icpt + slope * Math.Clamp(k, 0, len - 1);
                    roadH[i] = Mathf.Lerp(roadH[i], lineH, w);
                }
                break;
            }
            case "jump":
            {
                // Run-up: u² gets steeper toward the lip (the launch). Then a smooth fall-away.
                int up = (int)f.Length, down = Math.Max(2, (int)f.Drop);
                for (int k = 0; k <= up; k++)
                {
                    float u = k / (float)up;
                    roadH[((int)f.At - up + k + n) % n] += f.Height * u * u;
                }
                for (int k = 1; k <= down; k++)
                {
                    float v = k / (float)down;
                    roadH[((int)f.At + k) % n] += f.Height * (1f - v * v * (3 - 2 * v));
                }
                break;
            }
            default:
                GD.PushWarning($"Unknown track feature '{f.Type}'");
                break;
        }
    }

    /// <summary>
    /// Potholes and puddles walked out along the road with a random gap per surface and a random
    /// spot across the width, never closer than MinHazardGap to each other: spread out, not
    /// clumped like per-tile dice rolls would make them.
    /// </summary>
    static void PlaceHazards(TrackDef def, TrackData t)
    {
        const float MinHazardGap = 4f;
        var rng = new RandomNumberGenerator { Seed = (ulong)(def.Seed + 33) };
        var placed = new List<Vector2>();
        int n = t.Center.Length;
        float next = 12f;                        // leave the start line clean
        while (next < n - 10)
        {
            int i = (int)next;
            SurfaceType road = t.CenterSurf[i];
            // Mean gap along the road (m); the gaps themselves are irregular: now and then a cluster
            // of two or three close together, otherwise exponential, so some stretches are clean.
            (SurfaceType hazard, float meanGap) = road switch
            {
                SurfaceType.Concrete => (SurfaceType.Pothole, 9.5f),
                SurfaceType.Mud => (SurfaceType.Puddle, 5f),
                SurfaceType.Gravel or SurfaceType.Dirt => (SurfaceType.Puddle, 12f),
                SurfaceType.Snow => (SurfaceType.Ice, 14f),          // black ice under the snow
                _ => (road, 30f),
            };
            if (hazard == road) { next += meanGap; continue; }
            const float ClusterChance = 0.3f, ClusterGap = 5f, FloorGap = 2f;
            float spread = (meanGap - ClusterChance * ClusterGap) / (1f - ClusterChance) - FloorGap;
            next += rng.Randf() < ClusterChance
                ? rng.RandfRange(4f, 6f)
                : FloorGap - spread * MathF.Log(1f - rng.Randf() * 0.999f);

            Vector2 dir = t.CenterDir(i), side = new(-dir.Y, dir.X);
            float lateral = rng.RandfRange(-(t.HalfWidth[i] - 1.2f), t.HalfWidth[i] - 1.2f);
            Vector2 p = t.Center[i] + side * lateral;
            int ix = (int)(p.X / t.Tile), iz = (int)(p.Y / t.Tile);
            if (ix < 0 || iz < 0 || ix >= t.N || iz >= t.N || t.Surf[iz * t.N + ix] != road) continue;
            Vector2 c = new((ix + 0.5f) * t.Tile, (iz + 0.5f) * t.Tile);
            if(t.MaintenanceHall?.Contains(c,2) ?? false) continue;
            bool crowded = false;
            foreach (var q in placed) if (q.DistanceTo(c) < MinHazardGap) { crowded = true; break; }
            if (crowded) continue;
            t.Surf[iz * t.N + ix] = hazard;
            // A puddle tile is painted over the road it lies in (TileAtlas: variant = the ground around it).
            if (hazard == SurfaceType.Puddle)
                t.Variant[iz * t.N + ix] = (byte)(road switch { SurfaceType.Gravel => 1, SurfaceType.Dirt => 2, _ => 0 });
            placed.Add(c);
        }
    }

    static float NaturalHeight(TrackDef def, float x, float z, float world)
    {
        float hills = HillsHeight(def, x, z, world, out float calm);
        float noise = def.HillAmp * Noise.Fbm(x / def.HillScale, z / def.HillScale, def.Seed, 4);
        float h = noise * (1f - calm) + hills;
        float e = MathF.Min(MathF.Min(x, z), MathF.Min(world - x, world - z));
        if (e < EdgeMargin)
        {
            float k = 1f - e / EdgeMargin;
            if (def.EdgeRim is float rim)
            {
                // Rise toward a rim of about this height (a little noise left in), so the edge
                // never outgrows the big hills; under them the wall gives way entirely and the
                // hills themselves are the skyline.
                float target = Math.Max(h, rim + 0.4f * noise);
                h = Mathf.Lerp(h, target, k * k * (1f - SmoothStep(0f, EdgeHillFade, hills)));
            }
            else h += k * k * 16f;
        }
        foreach (var plateau in def.Plateaus)
        {
            float dx = Math.Max(0, Math.Max(plateau.X0 - x, x - plateau.X1));
            float dz = Math.Max(0, Math.Max(plateau.Z0 - z, z - plateau.Z1));
            float weight = 1f - SmoothStep(0, Math.Max(1f, plateau.Blend), MathF.Sqrt(dx * dx + dz * dz));
            h = Mathf.Lerp(h, plateau.Height, weight);
        }
        if (def.Water is { Frozen: true, ValleyDepth: > 0 } water)
        {
            float distance = float.MaxValue;
            foreach (var river in water.Rivers)
                distance = Math.Min(distance, river.EdgeDistance(new Vector2(x, z)));
            h -= water.ValleyDepth * (1f - SmoothStep(0, Math.Max(1f, water.ValleyBankWidth), distance));
        }
        return h;
    }

    const float EdgeHillFade = 6f;    // hill height at which the edge wall has faded out completely

    /// <summary>
    /// Sum of the authored landforms: a (1-u²)² bell of the distance to the hill's point or ridge
    /// line. calm = how much of the noise to smooth away here (strongest hill wins). With a
    /// BackRadius the side facing away from the map centre falls off over that distance instead,
    /// blended by direction so there is no seam.
    /// </summary>
    static float HillsHeight(TrackDef def, float x, float z, float world, out float calm)
    {
        float sum = 0;
        calm = 0;
        var p = new Vector2(x, z);
        var centre = new Vector2(world, world) * 0.5f;
        foreach (var hill in def.Hills)
        {
            var a = new Vector2(hill.X, hill.Z);
            var b = new Vector2(hill.X1 ?? hill.X, hill.Z1 ?? hill.Z);
            Vector2 ab = b - a;
            float len2 = ab.LengthSquared();
            float t = len2 > 1e-6f ? Math.Clamp((p - a).Dot(ab) / len2, 0f, 1f) : 0f;
            Vector2 q = a + ab * t;
            float radius = hill.Radius;
            if (hill.BackRadius is float back && p.DistanceSquaredTo(q) > 1e-6f)
            {
                float cos = (p - q).Normalized().Dot((centre - q).Normalized());
                radius = Mathf.Lerp(hill.Radius, back, SmoothStep(0.3f, -0.3f, cos));
            }
            float u = p.DistanceTo(q) / radius;
            if (u >= 1f) continue;
            float w = 1f - u * u;
            sum += hill.Height * w * w;
            calm = MathF.Max(calm, hill.Calm * w * w);
        }
        return sum;
    }

    /// <summary>Moves sample zero to an authored distance and reverses the closed road loop.</summary>
    static void Reorient(TrackDef def,TrackData t,float[] lift)
    {
        int n=t.Center.Length, start=((def.ReverseFrom%n)+n)%n;
        int Map(float old) => ((start-(int)MathF.Round(old)%n)%n+n)%n;
        var center=new Vector2[n];var widths=new float[n];var surfaces=new SurfaceType[n];var heights=new float[n];
        for(int i=0;i<n;i++)
        {
            int source=(start-i+n)%n;
            center[i]=t.Center[source];widths[i]=t.HalfWidth[source];surfaces[i]=t.CenterSurf[source];heights[i]=lift[source];
        }
        t.Center=center;t.HalfWidth=widths;t.CenterSurf=surfaces;
        Array.Copy(heights,lift,n);
        foreach(var feature in def.Features)
        {
            feature.At=feature.Type=="level"?Map(feature.At+feature.Length):Map(feature.At);
            if(feature.Type=="jump") (feature.Length,feature.Drop)=(feature.Drop,feature.Length);
        }
        foreach(var fence in def.Fences)
        {
            float from=fence.From,to=fence.To;fence.From=Map(to);fence.To=Map(from);
            if(fence.Side is "left" or "right") fence.Side=fence.Side=="left"?"right":"left";
            if(fence.NearTo>fence.NearFrom)
            { float nearFrom=fence.NearFrom;fence.NearFrom=Map(fence.NearTo);fence.NearTo=Map(nearFrom); }
        }
        if(def.SpeedTrap!=null)
        { def.SpeedTrap.At=Map(def.SpeedTrap.At);def.SpeedTrap.Side=def.SpeedTrap.Side=="left"?"right":"left"; }
        if(def.Depot!=null)
        { def.Depot.At=Map(def.Depot.At);def.Depot.Side=def.Depot.Side=="left"?"right":"left"; }
        if(def.Runway!=null)
        {
            float from=def.Runway.From,to=def.Runway.To;
            def.Runway.From=Map(to);def.Runway.To=Map(from);
            if(def.Runway.TakeoffFrom.HasValue) def.Runway.TakeoffFrom=Map(def.Runway.TakeoffFrom.Value);
            if(def.Runway.TakeoffTo.HasValue) def.Runway.TakeoffTo=Map(def.Runway.TakeoffTo.Value);
        }
    }

    static void BuildCenterLine(TrackDef def, TrackData t, out float[] liftOut)
    {
        var pts = def.Points;
        int m = pts.Count;
        var center = new List<Vector2>();
        var width = new List<float>();
        var surf = new List<SurfaceType>();
        var lift = new List<float>();
        for (int i = 0; i < m; i++)
        {
            TrackPoint p0 = pts[(i - 1 + m) % m], p1 = pts[i], p2 = pts[(i + 1) % m], p3 = pts[(i + 2) % m];
            Vector2 a = new(p0.X, p0.Z), b = new(p1.X, p1.Z), c = new(p2.X, p2.Z), d = new(p3.X, p3.Z);
            int steps = Math.Max(2, (int)MathF.Ceiling(b.DistanceTo(c) * 1.1f));
            SurfaceType s = Surfaces.Parse(p1.Surface);
            for (int k = 0; k < steps; k++)
            {
                float u = k / (float)steps;
                center.Add(CatmullRom(a, b, c, d, u));
                width.Add(Mathf.Lerp(p1.Width, p2.Width, u) * 0.5f);
                surf.Add(s);
                lift.Add(Mathf.Lerp(p1.Lift, p2.Lift, u));
            }
        }

        // Resample to uniform 1 m spacing so indices map to distance.
        var cum = new float[center.Count + 1];
        for (int i = 0; i < center.Count; i++)
            cum[i + 1] = cum[i] + center[i].DistanceTo(center[(i + 1) % center.Count]);
        float total = cum[center.Count];
        int count = (int)MathF.Floor(total);
        t.Center = new Vector2[count];
        t.HalfWidth = new float[count];
        t.CenterSurf = new SurfaceType[count];
        liftOut = new float[count];
        int j = 0;
        for (int i = 0; i < count; i++)
        {
            float target = i * total / count;
            while (cum[j + 1] < target) j++;
            float u = (target - cum[j]) / MathF.Max(1e-5f, cum[j + 1] - cum[j]);
            int j1 = (j + 1) % center.Count;
            t.Center[i] = center[j].Lerp(center[j1], u);
            t.HalfWidth[i] = Mathf.Lerp(width[j], width[j1], u);
            t.CenterSurf[i] = surf[j];
            liftOut[i] = Mathf.Lerp(lift[j], lift[j1], u);
        }
        foreach (var span in def.SurfaceSpans)
        {
            SurfaceType s = Surfaces.Parse(span.Surface);
            for (int i = Math.Max(0, (int)span.From); i < Math.Min(count, (int)span.To); i++) t.CenterSurf[i] = s;
        }
        t.Length = total;
        foreach(var bay in def.WidthSpans)
            for(int i=Math.Max(0,(int)bay.From);i<Math.Min(count,(int)bay.To);i++)
            {
                float weight=Math.Clamp(Math.Min(i-bay.From,bay.To-i)/Math.Max(1,bay.Taper),0,1);
                weight=weight*weight*(3-2*weight);
                t.HalfWidth[i]=Mathf.Lerp(t.HalfWidth[i],Math.Max(t.HalfWidth[i],bay.Width*.5f),weight);
            }
    }

    static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float u)
    {
        float u2 = u * u, u3 = u2 * u;
        return 0.5f * (2f * p1 + (-p0 + p2) * u + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u2 + (-p0 + 3f * p1 - 3f * p2 + p3) * u3);
    }

    static float[] BoxBlur(float[] src, int radius)
    {
        int n = src.Length;
        var dst = new float[n];
        float sum = 0;
        for (int k = -radius; k <= radius; k++) sum += src[((k % n) + n) % n];
        for (int i = 0; i < n; i++)
        {
            dst[i] = sum / (2 * radius + 1);
            sum += src[(i + radius + 1) % n] - src[((i - radius) % n + n) % n];
        }
        return dst;
    }

    static float SmoothStep(float e0, float e1, float x)
    {
        float k = Math.Clamp((x - e0) / (e1 - e0), 0f, 1f);
        return k * k * (3 - 2 * k);
    }

    // ---------------------------------------------------------------- props

    /// <summary>PlaceProps in slices: every item is a point where a frame may go by (a loading card keeps running).</summary>
    static IEnumerable<int> PlacePropsSteps(TrackDef def, TrackData t, CenterGrid grid)
    {
        var slice = System.Diagnostics.Stopwatch.StartNew();
        var occupied = new List<Vector2>();

        foreach (var p in def.Props)
            AddProp(t, p.Type, new Vector2(p.X, p.Z), Mathf.DegToRad(p.Rot), p.Text, occupied);

        if (t.Id == "bonus")
        {
            int helipadIndex = 0;
            foreach (var prop in t.Props)
                if (prop.Type == "helipad" && ++helipadIndex == 2)
                {
                    // Service the middle pad from its front quarter, outside the driving strip.
                    var basis = new Basis(Vector3.Up, prop.Rotation);
                    Vector3 work = prop.Position + basis * new Vector3(2.2f, 0, -3.8f);
                    var facing = new Basis(Vector3.Up, prop.Rotation + MathF.PI / 2);
                    t.MechanicPost = new Transform3D(facing, work - facing * new Vector3(0, 0, .35f));
                    occupied.Add(new Vector2(work.X, work.Z));
                    break;
                }
        }

        // Start/finish: a drive-through barn over the line, or the banner across the road at centre sample 0.
        if (def.StartBarn is { } sb)
        {
            // The barn uses local +Z for its entrance side, while YawFor points local -Z down
            // the road. Keep the painted line anchored to the timer's exact sample-0 crossing.
            float line = sb.Length / 2f - sb.Before;
            Vector2 dir = t.CenterDir(0), c = t.Center[0] + dir * line;
            float yaw = TrackData.YawFor(dir), half = t.HalfWidth[0] + sb.Room;
            float barnBase = t.MeshHeight(c.X, c.Y);
            // The stripe is local +Z * line from this origin, which lands exactly at Center[0].
            // The barn may sit on a slight grade, so its origin's height is not the road height under the stripe.
            float stripeY = t.MeshHeight(t.Center[0].X, t.Center[0].Y) + 0.06f - barnBase;
            // The ground under the painted line is not level across the road (a crown, the ditches by the walls): the heights of the ground along the line, so that the stripe follows it
            // instead of hanging in the air near the walls.
            var across = new Vector2(MathF.Cos(yaw), -MathF.Sin(yaw));
            var rise = new System.Text.StringBuilder();
            const int LineSamples = 13;
            float reach = Math.Min(half - 0.8f, t.HalfWidth[0] - 0.05f);      // the paint stops at the road's edge (the ground by the walls is grass)
            rise.Append(';').Append(reach.ToString(System.Globalization.CultureInfo.InvariantCulture));
            for (int i = 0; i < LineSamples; i++)
            {
                float u = -reach + 2f * reach * i / (LineSamples - 1);
                Vector2 at = t.Center[0] + across * u;
                rise.Append(';').Append((t.MeshHeight(at.X, at.Y) + 0.06f - barnBase).ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            t.LineHalf = half;
            t.Props.Add(new PropInstance("startbarn", new Vector3(c.X, t.MeshHeight(c.X, c.Y), c.Y), yaw,
                $"{sb.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)};{line.ToString(System.Globalization.CultureInfo.InvariantCulture)};{stripeY.ToString(System.Globalization.CultureInfo.InvariantCulture)}{rise}", half));
            foreach (var o in Village.BarnWalls(c, yaw, half, sb.Length))
            {
                t.Obstacles.Add(o);
                occupied.Add(o.Center);
            }
        }
        else
        {
            Vector2 c = t.Center[0], dir = t.CenterDir(0), side = new(-dir.Y, dir.X);
            float half = t.HalfWidth[0] + 1.8f;
            t.LineHalf = half;
            float y = t.CenterH[0];
            t.Props.Add(new PropInstance("startgate", new Vector3(c.X, y, c.Y), TrackData.YawFor(dir), null, half));
            t.Obstacles.Add(new Obstacle(c + side * half, 0.25f));
            t.Obstacles.Add(new Obstacle(c - side * half, 0.25f));
            occupied.Add(c + side * half);
            occupied.Add(c - side * half);
        }

        foreach (var b in t.BusStops)
            AddProp(t, "busstop", b.Position, b.Yaw, null, occupied);

        int firstDeliveryObstacle = t.Obstacles.Count;
        GrainDelivery.AddObstacles(t);
        for (int i = firstDeliveryObstacle; i < t.Obstacles.Count; i++)
            occupied.Add(t.Obstacles[i].Center);

        if (t.Id == "ilyich")
        {
            AddProp(t, "partydacha", PartyDacha.Site, 0, null, occupied);
            foreach (var solid in PartyDacha.SolidFootprints())
            {
                t.Obstacles.Add(new Obstacle(solid.At, solid.Radius));
                occupied.Add(solid.At);
            }
            foreach (var guard in PartyDacha.Guards)
            {
                t.DachaGuardObstacles.Add(t.Obstacles.Count);
                t.Obstacles.Add(new Obstacle(guard.At, KolkhozWorkers.BodyRadius, default, false, 0.5f, true, 2.5f, Material: ImpactMaterial.Soft));     // knocked over, not a wall (the estate is kept clear already)
            }
            // Keep ornamental grounds and the complete access corridor free of random vegetation.
            for (float z = 162; z <= 193; z += 2)
            for (float x = 200; x <= 248; x += 2) occupied.Add(new Vector2(x, z));
            for (int i = 0; i + 1 < PartyDacha.Drive.Length; i++)
            {
                var a = PartyDacha.Drive[i]; var b = PartyDacha.Drive[i + 1];
                for (float d = 0; d <= a.DistanceTo(b); d += 1.5f)
                    occupied.Add(a.Lerp(b, d / a.DistanceTo(b)));
            }
            // Lantern posts round the dacha's pond, for the dark.
            foreach (var site in PartyDacha.PondLampSites(t)) AddProp(t, "pondlamp", site, 0, null, occupied);
        }
        if (t.Id == "bonus")
        {
            // Reserve the service bay, kitchen, combine and battery loading lane.
            foreach (var site in new[] { new Vector2(88,43),new Vector2(171,40),BonusAirbase.AbandonedCombine,
                new Vector2(300,175),BonusExerciseLife.RadioOperatorPost })
                for(int x=-5;x<=5;x+=2) for(int z=-5;z<=5;z+=2) occupied.Add(site+new Vector2(x,z));
            t.Obstacles.Add(new Obstacle(BonusAirbase.AbandonedCombine,3));
            t.Obstacles.Add(new Obstacle(new Vector2(169,38),1.6f));
            t.Obstacles.Add(new Obstacle(new Vector2(88,43),1));
        }
        foreach (var prop in t.Props)
            if (prop.Type == "buk21")
                for (int x = -12; x <= 13; x += 2)
                    for (int z = -9; z <= 17; z += 2)
                        occupied.Add(new Vector2(prop.Position.X + x, prop.Position.Z + z));
        MaintenanceHall.Reserve(t,occupied);
        VillageLife.Reserve(t, occupied);
        yield return 0; slice.Restart();
        foreach (var step in PlaceFencesSteps(def, t, grid, occupied)) { yield return step; slice.Restart(); }
        yield return 0; slice.Restart();
        if (t.Id == "siberia") SiberiaRadar.Reserve(t, occupied);
        SiberiaDetails.Reserve(t, def, occupied);
        yield return 0; slice.Restart();

        foreach (var worker in KolkhozWorkers.Layout(t))
        {
            t.WorkerObstacles.Add(t.Obstacles.Count);
            t.Obstacles.Add(new Obstacle(worker.Position, KolkhozWorkers.BodyRadius, default, false, 0.5f, true, 2.5f, Material: ImpactMaterial.Soft));     // knocked over, not a wall
            occupied.Add(worker.Position);
        }

        if (t.Id == "ilyich")
            for (int i = 0; i < 96; i++) occupied.Add(SlurryTractor.Route(i * MathF.Tau / 96));

        // Secret cameras must remain reachable rather than spawning inside a tree or a bush.
        foreach (var secret in CameraSecrets.Layout(t.Id))
            for (float x = -3; x <= 3; x += 3)
            for (float z = -3; z <= 3; z += 3) occupied.Add(secret.Position + new Vector2(x, z));

        if (t.Id == "bonus")
        {
            foreach(var berm in BonusAirbase.Berms)
                for(float x=-14;x<=14;x+=2) for(float z=-14;z<=14;z+=2)
                    if(BonusAirbase.BermHeight(berm+new Vector2(x,z))>.01f) occupied.Add(berm+new Vector2(x,z));
            for (int i = 1; i < BonusAirbase.Bypass.Length; i++)
            {
                Vector2 a = BonusAirbase.Bypass[i-1], b = BonusAirbase.Bypass[i];
                int steps = (int)MathF.Ceiling(a.DistanceTo(b));
                for (int k = 0; k <= steps; k++)
                    foreach (float offset in new[] { -3f, 0f, 3f })
                        occupied.Add(a.Lerp(b, k / (float)steps) + (b-a).Normalized().Orthogonal() * offset);
            }
        }

        yield return 0; slice.Restart();
        if (t.Id == "ilyich") PlaceVillagePower(t, def.SpeedTrap, occupied);
        else if (t.Id == "siberia") PlaceSiberiaPower(def, t, occupied);
        else if (t.Id == "atomgrad") PlaceAtomgradPower(def, t, occupied);
        else if (t.Id == "bonus") PlaceBonusPower(def, t, occupied);
        else
        {
            // Telegraph poles along the road, always on the same (outer) side, every ~24 m.
            int cn = t.Center.Length;
            int buildings = t.Props.Count;          // props placed so far (wires must clear them)
            var poles = new List<(int Sample, int Prop)>();
            for (int i = 0; i < cn; i += 24)
            {
                Vector2 dir = t.CenterDir(i);
                Vector2 side = new(-dir.Y, dir.X);
                Vector2 pos = t.Center[i] + side * (t.HalfWidth[i] + 6.5f);
                grid.Nearest(pos, 40f, out float d);
                if (d < t.HalfWidth[i] + 5f || !FreeSpot(pos, occupied, 3f) || !InsideMap(t, pos, 20f) || InPad(t, pos, 3f) || NearWater(t, pos, 3f)) continue;
                poles.Add((i, t.Props.Count));
                AddProp(t, "pole", pos, TrackData.YawFor(dir), null, occupied);
            }
            PlacePowerLines(t, poles, cn, buildings, occupied);
        }

        yield return 0; slice.Restart();
        // Birch groves: clustered, never on the road.
        var rng = new RandomNumberGenerator { Seed = (ulong)def.Seed };
        int placed = 0, tries = 0;
        while (placed < def.Birches && tries++ < def.Birches * 40)
        {
            if (slice.ElapsedMilliseconds > 10) { yield return 0; slice.Restart(); }
            // Pick a grove centre from noise, then scatter around it.
            Vector2 pos = new(rng.RandfRange(20f, t.WorldSize - 20f), rng.RandfRange(20f, t.WorldSize - 20f));
            if (Noise.Value(pos.X / 35f, pos.Y / 35f, def.Seed + 21) < 0.05f) continue;
            grid.Nearest(pos, 40f, out float d);
            if (d < 13f || !FreeSpot(pos, occupied, 4f) || InPad(t, pos, 6f)) continue;
            var s = t.TileSurface((int)(pos.X / t.Tile), (int)(pos.Y / t.Tile));
            if (s == SurfaceType.Field || s == SurfaceType.Mud || NearWater(t, pos, 3f)) continue;
            float size = 0.6f + 0.9f * MathF.Pow(rng.Randf(), 1.4f);
            AddProp(t, t.InBlight(pos) ? "deadtree" : TreeKind(def, pos, rng), pos, rng.RandfRange(0, MathF.Tau), null, occupied, size);
            placed++;
        }

        yield return 0; slice.Restart();
        // A few extra dead trees inside each blight.
        foreach (var b in t.Blights)
            for (int k = 0, tries2 = 0; k < 7 && tries2 < 200; tries2++)
            {
                float a = rng.RandfRange(0, MathF.Tau), r = rng.RandfRange(6f, b.R - 2f);
                var pos = new Vector2(b.X + MathF.Cos(a) * r, b.Z + MathF.Sin(a) * r);
                grid.Nearest(pos, 40f, out float d);
                if (d < 10f || !FreeSpot(pos, occupied, 4f) || !InsideMap(t, pos, 8f)) continue;
                AddProp(t, "deadtree", pos, rng.RandfRange(0, MathF.Tau), null, occupied);
                k++;
            }

        // The red forest (ATOMGRAD-4): a dense stand of rust-red pines round the road through it.
        if (def.RedForest is { } rf)
            for (int k = 0, tries3 = 0; k < rf.Count && tries3 < rf.Count * 30; tries3++)
            {
                float a = rng.RandfRange(0, MathF.Tau), r = rf.R * MathF.Sqrt(rng.Randf());
                var pos = new Vector2(rf.X + MathF.Cos(a) * r, rf.Z + MathF.Sin(a) * r);
                int ci = grid.Nearest(pos, 40f, out float d);
                if (ci >= 0 && d < t.HalfWidth[ci] + 4f || !FreeSpot(pos, occupied, 3.2f) || !InsideMap(t, pos, 8f)) continue;
                AddProp(t, "redpine", pos, rng.RandfRange(0, MathF.Tau), null, occupied, 0.7f + 0.6f * rng.Randf());
                k++;
            }

        yield return 0; slice.Restart();
        foreach (var step in ScatterVegetationSteps(def, t, grid, occupied, rng)) { yield return step; slice.Restart(); }
        yield return 0; slice.Restart();
        if (t.Id == "ilyich")
            foreach (var tree in Props.IlyichEdgeTrees(t))
            {
                if (slice.ElapsedMilliseconds > 10) { yield return 0; slice.Restart(); }
                Vector2 p = tree.Position;
                if (!InsideMap(t, p, 1f) || !FreeSpot(p, occupied, 4f) || NearWater(t, p, 3f)) continue;
                int ci = grid.Nearest(p, 40f, out float distance);
                if (ci >= 0 && distance < t.HalfWidth[ci] + 12f) continue;
                if (t.TileSurface((int)(p.X / t.Tile), (int)(p.Y / t.Tile)) != SurfaceType.Grass) continue;
                AddProp(t, tree.Kind, p, tree.Yaw, null, occupied, tree.Size);
            }
        yield return 0; slice.Restart();
        PlaceLogPiles(def, t, occupied);
        yield return 0; slice.Restart();
        PlaceTaiga(def, t, grid, occupied);      // TrackBuilder.Taiga.cs: winter only
    }

    /// <summary>
    /// Wires between consecutive poles of the same line. A span is left out where a pole is
    /// missing, the gap is long, the line turns sharply, or a building or hill is in the way.
    /// </summary>
    static void PlacePowerLines(TrackData t, List<(int Sample, int Prop)> poles, int cn, int buildings, List<Vector2> occupied)
    {
        bool Blocked(Vector3 a, Vector3 b)
        {
            Vector2 a2 = new(a.X, a.Z), b2 = new(b.X, b.Z);
            float len = a2.DistanceTo(b2);
            if (len < 8f || len > 36f) return true;
            for (int k = 0; k < buildings; k++)
            {
                var pr = t.Props[k];
                if (pr.Type is "startgate" or "busstop" or "drunktractor") continue;
                Vector2 p = new(pr.Position.X, pr.Position.Z);
                if (Geometry2D.GetClosestPointToSegment(p, a2, b2).DistanceTo(p) < 7f) return true;
            }
            for (float f = 0.1f; f < 1f; f += 0.1f)
            {
                Vector3 m = a.Lerp(b, f);
                if (t.MeshHeight(m.X, m.Z) > m.Y + 6.7f - 0.8f - 3f) return true;      // wire under 3 m above ground
            }
            return false;
        }

        for (int k = 0; k < poles.Count; k++)
        {
            var (s0, p0) = poles[k];
            var (s1, p1) = poles[(k + 1) % poles.Count];
            int gap = ((s1 - s0) % cn + cn) % cn;
            if (poles.Count < 2 || gap == 0 || gap > 30) continue;           // a pole is missing between them
            PropInstance a = t.Props[p0], b = t.Props[p1];
            if (MathF.Abs(Mathf.AngleDifference(a.Rotation, b.Rotation)) > Mathf.DegToRad(55f)) continue;
            if (Blocked(a.Position, b.Position)) continue;
            t.PowerSpans.Add((p0, p1));
            // Keep the wire corridor clear of trees.
            Vector2 a2 = new(a.Position.X, a.Position.Z), b2 = new(b.Position.X, b.Position.Z);
            for (float f = 0.2f; f < 0.9f; f += 0.2f) occupied.Add(a2.Lerp(b2, f));
        }
    }

    /// <summary>
    /// Birch or conifer for a tree at pos: conifers in stands where a coarse noise is high
    /// (~40 % of the trees), pines on their own patches among the spruces, a few strays.
    /// </summary>
    static string TreeKind(TrackDef def, Vector2 pos, RandomNumberGenerator rng)
    {
        float stand = Noise.Value(pos.X / 48f, pos.Y / 48f, def.Seed + 77) + rng.RandfRange(-0.12f, 0.12f);
        // The taiga: spruce and pine, a bare birch here and there.
        if (def.Ground == "deepsnow") return stand < -0.55f ? "birch" : Noise.Value(pos.X / 26f, pos.Y / 26f, def.Seed + 78) > 0.25f ? "pine" : "spruce";
        if (stand < ConiferThreshold) return "birch";
        return Noise.Value(pos.X / 26f, pos.Y / 26f, def.Seed + 78) > 0.05f ? "pine" : "spruce";
    }

    const float ConiferThreshold = 0.1f;

    /// <summary>Bushes (clumped by noise) and grass tufts over the grass; withered inside blights.</summary>
    static IEnumerable<int> ScatterVegetationSteps(TrackDef def, TrackData t, CenterGrid grid, List<Vector2> occupied, RandomNumberGenerator rng)
    {
        var slice = System.Diagnostics.Stopwatch.StartNew();
        bool Ok(Vector2 p, float roadGap, float propGap)
        {
            if (!InsideMap(t, p, 12f) || InPad(t, p, 2f)) return false;
            var s = t.TileSurface((int)(p.X / t.Tile), (int)(p.Y / t.Tile));
            if (s is not (SurfaceType.Grass or SurfaceType.DeadGrass) || NearWater(t, p, 1.5f)) return false;
            int ci = grid.Nearest(p, 30f, out float d);
            if (ci >= 0 && d < t.HalfWidth[ci] + roadGap) return false;
            return FreeSpot(p, occupied, propGap);
        }

        int placed = 0;
        for (int tries = 0; placed < def.Bushes && tries < def.Bushes * 30; tries++)
        {
            if (slice.ElapsedMilliseconds > 10) { yield return 0; slice.Restart(); }
            var p = new Vector2(rng.RandfRange(12f, t.WorldSize - 12f), rng.RandfRange(12f, t.WorldSize - 12f));
            // Clumps: dense where the noise is high, near groves and along field edges.
            if (Noise.Value(p.X / 25f, p.Y / 25f, def.Seed + 55) < rng.RandfRange(-0.2f, 0.6f)) continue;
            if (!Ok(p, 3.5f, 1.8f)) continue;
            t.Bushes.Add((new Vector3(p.X, t.MeshHeight(p.X, p.Y), p.Y), rng.RandfRange(0.6f, 1.5f), rng.RandfRange(0, MathF.Tau), t.InBlight(p)));
            placed++;
        }
        placed = 0;
        for (int tries = 0; placed < def.Tufts && tries < def.Tufts * 20; tries++)
        {
            if (slice.ElapsedMilliseconds > 10) { yield return 0; slice.Restart(); }
            var p = new Vector2(rng.RandfRange(12f, t.WorldSize - 12f), rng.RandfRange(12f, t.WorldSize - 12f));
            if (!Ok(p, 1.8f, 0.8f)) continue;
            t.Tufts.Add((new Vector3(p.X, t.MeshHeight(p.X, p.Y), p.Y), rng.RandfRange(0.5f, 1.1f), rng.RandfRange(0, MathF.Tau)));
            placed++;
        }
    }

    /// <summary>
    /// Fences beside the ditch for the authored stretches. Gaps are left where another piece of
    /// road or a prop is too close, so a fence never blocks the track.
    /// </summary>
    static IEnumerable<int> PlaceFencesSteps(TrackDef def, TrackData t, CenterGrid grid, List<Vector2> occupied)
    {
        var slice = System.Diagnostics.Stopwatch.StartNew();
        int n = t.Center.Length;
        var housePaths = new List<List<Vector2>>();
        if (t.Id == "ilyich")
            foreach (var house in t.Props)
                if (house.Type == "izba") housePaths.Add(Village.HouseAccessPath(t, house));
        foreach (var f in def.Fences)
        {
            float sign = f.Side == "left" ? -1f : 1f;
            float step = f.Type == "stone" ? 2f : 3f;
            float len = ((f.To - f.From) % n + n) % n;
            var pts = new List<Vector3>();
            void Flush()
            {
                if (pts.Count >= 2) t.Fences.Add(new FenceRun(f.Type, pts));
                pts = new List<Vector3>();
            }
            for (float d = 0; d <= len; d += step)
            {
                if (slice.ElapsedMilliseconds > 10) { yield return 0; slice.Restart(); }
                int i = ((int)(f.From + d) % n + n) % n;
                Vector2 dir = t.CenterDir(i), side = new Vector2(-dir.Y, dir.X) * sign;
                float offset = f.OffsetAt(f.From + d);
                Vector2 p = t.Center[i] + side * (t.HalfWidth[i] + offset);
                int ci = grid.Nearest(p, 30f, out float dn);
                bool roadClose = ci >= 0 && dn < t.HalfWidth[ci] + Math.Min(3.5f, offset - 0.3f);     // another road (its own is offset out)
                // End the run on either side of a house approach. Its collision circles are
                // generated only after this split, so the visible gate is genuinely passable.
                bool access = housePaths.Exists(path => Village.NearAccessPath(p, path, 2f));
                if (BonusAirbase.Reserved(t, p, 2f) || (t.Id == "bonus" && BonusAirbase.OnStand(p,2f)) || access || (t.Id == "ilyich" && PartyDacha.OnDrive(p, 2f)) || roadClose || !FreeSpot(p, occupied, 2.5f) || !InsideMap(t, p, 10f)) { Flush(); continue; }
                pts.Add(new Vector3(p.X, t.MeshHeight(p.X, p.Y), p.Y));
            }
            Flush();
        }

        // Collision: stone walls are a chain of small circles (wooden fences break instead, see
        // WoodFences); occupancy so trees and poles keep clear of both.
        // Each ~2 m stone segment owns its circles (the shared end circle goes to the next one),
        // so a hard hit can knock just that segment down (Breakables).
        for (int r = 0; r < t.Fences.Count; r++)
        {
            var run = t.Fences[r];
            for (int k = 0; k + 1 < run.Points.Count; k++)
            {
                var a = new Vector2(run.Points[k].X, run.Points[k].Z);
                var b = new Vector2(run.Points[k + 1].X, run.Points[k + 1].Z);
                occupied.Add(a);
                if (run.Type != "stone") continue;
                int steps = Math.Max(1, (int)MathF.Ceiling(a.DistanceTo(b) / 0.9f));
                int first = t.Obstacles.Count, last = k + 2 == run.Points.Count ? steps : steps - 1;
                for (int j = 0; j <= last; j++) t.Obstacles.Add(new Obstacle(a.Lerp(b, j / (float)steps), 0.45f, default, false, Breakables.WallBreak, true, Material: ImpactMaterial.Stone));
                t.WallSegments.Add(new WallSegment(r, k, first, t.Obstacles.Count - first));
            }
        }
    }

    static bool InPad(TrackData t, Vector2 p, float margin)
    {
        foreach (var pad in t.Pads)
            if (p.X > pad.X0 - margin && p.X < pad.X1 + margin && p.Y > pad.Z0 - margin && p.Y < pad.Z1 + margin) return true;
        return false;
    }

    static bool InsideMap(TrackData t, Vector2 p, float margin) =>
        p.X > margin && p.Y > margin && p.X < t.WorldSize - margin && p.Y < t.WorldSize - margin;

    static bool FreeSpot(Vector2 p, List<Vector2> occupied, float r)
    {
        foreach (var o in occupied)
            if (o.DistanceSquaredTo(p) < r * r) return false;
        return true;
    }

    static void AddProp(TrackData t, string type, Vector2 p, float rot, string text, List<Vector2> occupied, float size = 0)
    {
        int materialStart = t.Obstacles.Count;
        float y = t.MeshHeight(p.X, p.Y);
        t.Props.Add(new PropInstance(type, new Vector3(p.X, y, p.Y), rot, text, size));
        occupied.Add(p);
        if (type is "birch" or "spruce" or "pine" or "redpine" or "pole" or "hay") t.PropObstacle[t.Props.Count - 1] = t.Obstacles.Count;
        Vector2 ax = new(MathF.Cos(rot), -MathF.Sin(rot)), az = new(MathF.Sin(rot), MathF.Cos(rot));    // local X and Z
        switch (type)
        {
            case "birch": t.Obstacles.Add(new Obstacle(p, size > 0 ? Props.TreeObstacleRadius(type, size) : 0.35f, default, true, Breakables.BreakSpeed(type, size))); break;
            case "spruce" or "pine" or "redpine": t.Obstacles.Add(new Obstacle(p, Props.TreeObstacleRadius(type, size > 0 ? size : 1f), default, false, Breakables.BreakSpeed(type, size))); break;
            case "streetlamp": t.Obstacles.Add(new Obstacle(p, 0.16f)); break;
            case "pondlamp": t.Obstacles.Add(new Obstacle(p, 0.14f)); break;
            case "powercabinet": t.Obstacles.Add(new Obstacle(p, 0.65f)); break;
            case "pole": t.Obstacles.Add(new Obstacle(p, 0.2f, default, false, Breakables.PoleBreak)); break;
            case "hay": t.Obstacles.Add(new Obstacle(p, 0.9f, default, false, 2f, true, 5f)); break;    // gives way: it tips over, the car ploughs on 18 km/h slower
            case "statue":
                t.Obstacles.Add(new Obstacle(p, 1.7f * (t.Id == "ilyich" ? Village.MonumentScale : 1f)));
                if (t.Id == "ilyich")
                    // Reserve both the square and its straight approach before planting vegetation.
                    for (float x = -7; x <= 7; x += 2)
                        for (float z = -Village.MonumentApproachLength; z <= 5; z += 2)
                            if (z >= -8 || MathF.Abs(x) <= 2)
                                occupied.Add(p + ax * x + az * z);
                break;
            case "silo": t.Obstacles.Add(new Obstacle(p, 2.6f)); break;
            case "barn":
                if (t.Id != "ilyich") { AddBox(t, p, rot, 8f, 13f); break; }
                AddBox(t, p - az * 6.4f, rot, 8f, 0.3f);
                foreach (float side in new[] { -1f, 1f })
                {
                    AddBox(t, p + ax * side * 3.9f, rot, 0.3f, 13f);
                    AddBox(t, p + ax * side * 2.9f + az * 6.4f, rot, 2.2f, 0.3f);
                }
                break;
            case "bigbarn":
                AddBox(t, p - az * 7.9f, rot, 10f, 0.3f);
                foreach (float side in new[] { -1f, 1f })
                {
                    AddBox(t, p + ax * side * 4.9f, rot, 0.3f, 16f);
                    AddBox(t, p + ax * side * 3.8f + az * 7.9f, rot, 2.4f, 0.3f);
                }
                break;
            case "dormitory":
            {
                // A row of circles along local +X (the wing runs from its west end), the ground under
                // the whole footprint kept free of trees.
                float length = float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float l) ? l : 84f;
                float r = Village.DormDepth / 2;
                int k = Math.Max(1, (int)MathF.Ceiling(length / (r * 1.5f)));
                for (int i = 0; i <= k; i++)
                    t.Obstacles.Add(new Obstacle(p + ax * Mathf.Lerp(r, length - r, i / (float)k), r));
                for (float x = 0; x <= length; x += 3f)
                    for (float z = -r; z <= r; z += 2f) occupied.Add(p + ax * x + az * z);
                foreach (float fraction in new[] { 0.2f, 0.5f, 0.8f })
                {
                    AddBox(t, p + ax * (length * fraction) + az * (r + 1.5f), rot, 3.7f, 3f);
                    // Keep bushes, grass and trees off the stairs and their approach.
                    for (float x = -2f; x <= 2f; x += 1f)
                        for (float z = r; z <= r + 4f; z += 1f)
                            occupied.Add(p + ax * (length * fraction + x) + az * z);
                }
                if (t.Id == "ilyich")
                {
                    // Reserve the frontage and every road-to-door approach before scattering plants.
                    for (float x = 7; x <= length - 2; x += 1f)
                        for (float z = r + 2; z <= r + 5; z += 1f)
                            occupied.Add(p + ax * x + az * z);
                    foreach (float fraction in new[] { 0.2f, 0.5f, 0.8f })
                    {
                        Vector2 door = p + ax * (length * fraction) + az * (r + 2);
                        int road = t.NearestCenter(door, out _);
                        Vector2 end = t.Center[road];
                        int steps = Math.Max(1, (int)MathF.Ceiling(door.DistanceTo(end)));
                        for (int step = 0; step <= steps; step++)
                            foreach (float side in new[] { -1f, 0f, 1f })
                                occupied.Add(door.Lerp(end, step / (float)steps) + ax * side);
                    }
                }
                break;
            }
            case "church": AddBox(t, p, rot, 7f, 17f); break;
            case "stonepost": t.Obstacles.Add(new Obstacle(p, 0.42f)); break;
            case "izba": AddBox(t, p, rot, 7f, 6f); break;
            case "lorrywreck": AddBox(t, p, rot, 2.6f, 10.5f); break;
            case "tractor": AddBox(t, p, rot, 2.2f, 4f); break;
            case "hangar": AddBox(t, p, rot, 18.5f, 22f); break;
            case "fieldtent": AddBox(t, p, rot, 6.2f, 8.8f); break;
            case "fieldtruck": AddBox(t, p, rot, 2.8f, 8.6f); break;
            case "fieldfuel": AddBox(t, p, rot, 7.2f, 7f); break;
            case "fieldgenerator": AddBox(t, p, rot, 2.8f, 2.3f); break;
            case "fieldperimeter":
                // Use the exact same road-cleared bag placements as the visible perimeter.
                for (float x = -64; x <= 78; x += 2.5f)
                    for (float z = -31; z <= 28; z += 2.5f)
                        occupied.Add(p + ax * x + az * z);
                foreach (var bag in BonusFieldCamp.PerimeterBags())
                    t.Obstacles.Add(new Obstacle(p + ax * bag.Center.X + az * bag.Center.Y, 1.05f));
                foreach (float x in new[] { -BonusFieldCamp.GateHalfWidth, BonusFieldCamp.GateHalfWidth })
                    t.Obstacles.Add(new Obstacle(p + ax * x + az * BonusFieldCamp.GateZ, 0.8f));
                foreach (Vector2 tower in BonusFieldCamp.PerimeterTowers)
                    t.Obstacles.Add(new Obstacle(p + ax * tower.X + az * tower.Y, 1.4f));
                break;
            case "fieldnet":
                foreach(float nx in new[]{-6f,6f}) foreach(float nz in new[]{-6f,6f})
                    t.Obstacles.Add(new Obstacle(p + ax * nx + az * nz, .10f));
                break;
            case "windsock": t.Obstacles.Add(new Obstacle(p, 0.15f)); break;
            case "flagpole": t.Obstacles.Add(new Obstacle(p, 0.15f)); break;
            case "deadtree": t.Obstacles.Add(new Obstacle(p, 0.3f)); break;
            case "ruinbarn": AddBox(t, p, rot, 8f, 13f); break;
            case "barracks": AddBox(t, p, rot + MathF.PI / 2f, 11f, 31f); break;
            case "radar": t.Obstacles.Add(new Obstacle(p, 4.3f)); break;
            case "busstop": AddBox(t, p, rot + MathF.PI / 2f, 1.4f, 4.2f); break;
            case "hind":
            {
                Vector2 along = new(MathF.Sin(rot), MathF.Cos(rot)), across = new(along.Y, -along.X);
                for (float z = -6; z <= 6; z += 2) t.Obstacles.Add(new Obstacle(p + along * z, 1));
                for (float x = -3.5f; x <= 3.5f; x += 1.75f) t.Obstacles.Add(new Obstacle(p + across * x, 1.1f));
                break;
            }
            case "balalaika":
            {
                // Fuselage along local Z plus a circle over each wing.
                Vector2 along = new(MathF.Sin(rot), MathF.Cos(rot)), across = new(along.Y, -along.X);
                for (float z = -5.5f; z <= 5.5f; z += 1.5f) t.Obstacles.Add(new Obstacle(p + along * z, 0.8f));
                t.Obstacles.Add(new Obstacle(p + along * 2.4f + across * 2.2f, 1.3f));
                t.Obstacles.Add(new Obstacle(p + along * 2.4f - across * 2.2f, 1.3f));
                break;
            }
            // ATOMGRAD-4 (Track.Atomgrad).
            case "turbinehall": AddBox(t, p, rot + MathF.PI / 2f, 22f, 81f); break;
            case "reactor": AddBox(t, p + az * 4f, rot, 26f, 36f); break;
            case "sarcophagus": AddBox(t, p - az * 8f, rot, 31f, 50f); break;
            case "stack": t.Obstacles.Add(new Obstacle(p, 4.6f)); break;
            case "pipebridge":
            {
                float span = float.Parse(text ?? "16", System.Globalization.CultureInfo.InvariantCulture);
                t.Obstacles.Add(new Obstacle(p + ax * span / 2f, 1.3f));
                t.Obstacles.Add(new Obstacle(p - ax * span / 2f, 1.3f));
                break;
            }
            case "apartments": AddBox(t, p, rot + MathF.PI / 2f, 12f, 50f); break;
            case "ferriswheel": t.Obstacles.Add(new Obstacle(p + ax * 6f, 2.6f)); t.Obstacles.Add(new Obstacle(p - ax * 6f, 2.6f)); break;
            case "culture": AddBox(t, p, rot + MathF.PI / 2f, 16f, 26f); break;
            case "bumpercars": AddBox(t, p, rot + MathF.PI / 2f, 10f, 14f); break;
            case "checkpoint":
                t.Obstacles.Add(new Obstacle(p, 2.1f));
                t.Obstacles.Add(new Obstacle(p + ax * 3f, 0.35f));
                t.Obstacles.Add(new Obstacle(p - ax * 3.5f, 0.15f));
                break;
            case "radsign" or "crossing": t.Obstacles.Add(new Obstacle(p, 0.15f)); break;
            case "wash":
                AddBox(t, p + ax * 4f, rot, 0.8f, 16f);
                AddBox(t, p - ax * 4f, rot, 0.8f, 16f);
                AddBox(t, p + ax * 7.2f + az * 3f, rot, 5f, 6f);
                break;
            case "railyard":
                Atomgrad.RailYardObstacles(t, p, rot);
                // Reserve the entire working apron and rail approach from procedural trees/poles.
                for (float x = -10; x <= 23; x += 3)
                for (float z = -29; z <= 44; z += 3) occupied.Add(p + ax * x + az * z);
                break;
            case "wreck": AddBox(t, p, rot, 2.7f, text == "1" ? 10f : 8f); break;
            case "billboard":
                t.Obstacles.Add(new Obstacle(p + ax * 2.6f, 0.2f));
                t.Obstacles.Add(new Obstacle(p - ax * 2.6f, 0.2f));
                break;
        }
        ImpactMaterial material = type switch
        {
            "birch" or "spruce" or "pine" or "redpine" or "deadtree" or "pole" or "izba" => ImpactMaterial.Wood,
            "church" or "stonepost" or "statue" or "stack" or "apartments" or "culture" or "reactor" or "sarcophagus" => ImpactMaterial.Stone,
            "hay" or "fieldtent" => ImpactMaterial.Soft,
            "streetlamp" or "pondlamp" or "powercabinet" or "silo" or "lorrywreck" or "tractor" or "hangar" or "fieldtruck"
                or "fieldfuel" or "fieldgenerator" or "windsock" or "flagpole" or "radar" or "wreck" or "pipebridge"
                or "ferriswheel" or "radsign" or "crossing" => ImpactMaterial.Metal,
            _ => ImpactMaterial.Unknown,
        };
        for (int i = materialStart; i < t.Obstacles.Count; i++) t.Obstacles[i] = t.Obstacles[i] with { Material = material };
    }

    /// <summary>Approximates a rotated box (sizeX across, sizeZ along) with a row of circles.</summary>
    static void AddBox(TrackData t, Vector2 c, float rot, float sizeX, float sizeZ)
    {
        // Local +Z rotated by yaw 'rot' (Godot rotation around Y): (sin, cos).
        Vector2 along = new(MathF.Sin(rot), MathF.Cos(rot));
        float r = sizeX * 0.5f;
        int k = Math.Max(1, (int)MathF.Ceiling(sizeZ / sizeX));
        for (int i = 0; i < k; i++)
        {
            float o = k == 1 ? 0 : Mathf.Lerp(-sizeZ * 0.5f + r, sizeZ * 0.5f - r, i / (float)(k - 1));
            t.Obstacles.Add(new Obstacle(c + along * o, r));
        }
    }
}

/// <summary>Bucketed centre-line samples for fast nearest queries during generation.</summary>
class CenterGrid
{
    readonly Vector2[] _pts;
    readonly float _cell;
    readonly Dictionary<(int, int), List<int>> _buckets = new();

    public CenterGrid(Vector2[] pts, float cell)
    {
        _pts = pts;
        _cell = cell;
        for (int i = 0; i < pts.Length; i++)
        {
            var key = ((int)MathF.Floor(pts[i].X / cell), (int)MathF.Floor(pts[i].Y / cell));
            if (!_buckets.TryGetValue(key, out var list)) _buckets[key] = list = new List<int>();
            list.Add(i);
        }
    }

    /// <summary>Nearest sample within maxDist, or -1 (dist = +inf).</summary>
    public int Nearest(Vector2 p, float maxDist, out float dist)
    {
        int r = (int)MathF.Ceiling(maxDist / _cell);
        int cx = (int)MathF.Floor(p.X / _cell), cz = (int)MathF.Floor(p.Y / _cell);
        int best = -1;
        float bestD = maxDist * maxDist;
        for (int dz = -r; dz <= r; dz++)
        for (int dx = -r; dx <= r; dx++)
        {
            if (!_buckets.TryGetValue((cx + dx, cz + dz), out var list)) continue;
            foreach (int i in list)
            {
                float d = _pts[i].DistanceSquaredTo(p);
                if (d < bestD) { bestD = d; best = i; }
            }
        }
        dist = best >= 0 ? MathF.Sqrt(bestD) : float.PositiveInfinity;
        return best;
    }
}
