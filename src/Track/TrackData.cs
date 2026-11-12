using System;
using System.Collections.Generic;
using Godot;

namespace CCCPRacing.Track;

public struct GroundSample
{
    public float Height;
    public Vector3 Normal;         // of the rendered mesh
    public Vector2 Slope;          // dh/dx, dh/dz of the small bumps (pothole walls, slab joints)
    public SurfaceType Surface;
    public float SlurryWetness;
    public float OilFilm;
    /// <summary>The strip of verge just outside the road's edge (dirt, or ploughed snow in winter).</summary>
    public bool Verge;
    /// <summary>How far the water stands over the bed here (m): rivers and ponds; 0 where there is none (a synthetic ground says 0 too). The car wades by it, flying or not.</summary>
    public float WaterDepth;
}

/// <summary>Anything the car can drive on. Kept as an interface so physics tests can use synthetic ground.</summary>
public interface IGround
{
    GroundSample Sample(float x, float z);
}

/// <summary>
/// Collision circle in XZ. Velocity is for moving obstacles (bus), so impacts are relative.
/// BreakAt: impact speed (m/s) at which it gives way (trees, poles, stone walls, see Breakables); 0 never.
/// Crumbles: when it gives way the car goes on through (stone walls) instead of bouncing off.
/// Loss: the speed (m/s) a crumbling obstacle takes off the car going through; below 0 a stone wall's.
/// </summary>
/// <summary>Acoustic contact type; Unknown is the safe fallback for old recordings
/// and obstacles without an authored material. This does not affect physics.</summary>
public enum ImpactMaterial : byte { Unknown, Metal, Wood, Stone, Snow, Ground, Soft }

public readonly record struct Obstacle(Vector2 Center, float Radius, Vector2 Velocity = default, bool Birch = false, float BreakAt = 0, bool Crumbles = false, float Loss = -1f,
    ImpactMaterial Material = ImpactMaterial.Unknown);

/// <summary>About 2 m of dry-stone wall: points Seg..Seg+1 of fence run Run, owning obstacles [First, First + Count).</summary>
public readonly record struct WallSegment(int Run, int Seg, int First, int Count);

/// <summary>A bus stop beside the road: centre-line index, shelter position/facing, how many drunks wait.</summary>
public record BusStop(int Index, Vector2 Position, float Yaw, int Drunks);

/// <summary>A fence polyline on the ground (world points, heights already applied).</summary>
public record FenceRun(string Type, List<Vector3> Points);

public record PropInstance(string Type, Vector3 Position, float Rotation, string Text, float Size = 0);

/// <summary>Buried connection between prop terminals. Route includes both endpoints in XZ; never rendered as a wire.</summary>
public record PowerCable(int A, int B, Vector2[] Route);

/// <summary>Generated, runtime form of a track: height grid, surface grid, centre line and props.</summary>
public class TrackData : IGround
{
    public SlurryTrail Slurry;
    public string Id;
    public string Name;
    /// <summary>A winter track (TrackDef.Ground "deepsnow"): conifers in snow, bare birches, snowfall.</summary>
    public bool Winter;
    /// <summary>An autumn track (TrackDef.Season "autumn"): yellow birches, brown grass strewn with leaves.</summary>
    public bool Autumn;
    /// <summary>The zone (TrackDef.Radiation): the bear wears a gas mask.</summary>
    public bool Zone;
    public int N;                 // tiles per side
    public float Tile;            // metres per tile
    public float[] Heights;       // (N+1)*(N+1) vertex heights
    public SurfaceType[] Surf;    // N*N tile surfaces
    public bool[] Verge;          // N*N: the tile is the verge strip beside the road (a little drag, CarParams.VergeDrag)
    public byte[] Variant;        // N*N texture variant 0..3

    public Vector2[] Center;      // centre line samples, ~1 m apart, closed loop
    public float[] CenterH;
    public readonly List<RoadLandform> RoadLandforms = new();
    public float[] HalfWidth;
    public float[] RoadLeftInset, RoadRightInset; // optional worn edges inside the nominal road width
    public SurfaceType[] CenterSurf;
    public float Length;
    public MaintenanceHallSite MaintenanceHall;

    public readonly List<PropInstance> Props = new();
    public readonly List<FenceRun> Fences = new();
    /// <summary>The winter road's snow banks (null: a summer track).</summary>
    public Snowbanks Banks;
    /// <summary>Stacks of logs by the road (LogPiles).</summary>
    public readonly List<LogPiles.Pile> LogPiles = new();
    /// <summary>Authored Siberian work scenes shared by reservations, static collision and runtime actors.</summary>
    public SiberiaDetails.ActivityLayout SiberiaActivities;
    /// <summary>Where a mechanic sleeps on a bench by a door when there is no bus stop (the labour camp): like a shelter, -Z faces the road.</summary>
    public Transform3D? MechanicPost;
    /// <summary>The winter forest beyond reach (TaigaMesh): ground position, size, yaw, kind (0, 1 spruce, 2 pine).</summary>
    public readonly List<(Vector3 Pos, float Scale, float Yaw, int Kind)> Taiga = new();
    /// <summary>Power line spans: prop indices of poles, or a pole and a building service attachment.</summary>
    public readonly List<(int A, int B)> PowerSpans = new();
    public readonly List<PowerCable> PowerCables = new();
    public readonly HashSet<int> PowerSources = new();
    /// <summary>Local entry point of a buried building service (separate from aerial attachments).</summary>
    public readonly Dictionary<int, Vector3> CableEntries = new();
    /// <summary>Local service attachment on each supplied building or lamp.</summary>
    public readonly Dictionary<int, Vector3> PowerAnchors = new();
    /// <summary>Obstacle index of each breakable prop (trees, poles), by prop index.</summary>
    public readonly Dictionary<int, int> PropObstacle = new();
    public readonly List<WallSegment> WallSegments = new();
    /// <summary>The kolkhoz workers' obstacles, in KolkhozWorkers.Layout order (they fall over when hit).</summary>
    public readonly List<int> WorkerObstacles = new();
    /// <summary>The dacha's security men's obstacles, in PartyDacha.Guards order.</summary>
    public readonly List<int> DachaGuardObstacles = new();
    public int BabushkaCount;
    public readonly List<PadDef> Pads = new();
    public RunwayDef Runway;
    public readonly List<BusStop> BusStops = new();
    public TankRangeDef TankRange;
    /// <summary>The water line of the ponds and rivers (NaN: no water); the tiles under it are Water or Shallow.</summary>
    public float WaterLevel = float.NaN;
    /// <summary>Authored river paths and widths used by shoreline vegetation.</summary>
    public WaterDef Water;
    public MeadowDef Meadow;
    public BackdropDef Backdrop;
    /// <summary>The ford: the centre-line index where the river crosses the road (-1: none).</summary>
    public int FordIndex = -1;
    public readonly List<BlightDef> Blights = new();
    /// <summary>Scattered vegetation: position, size, yaw, dead or alive.</summary>
    public readonly List<(Vector3 pos, float size, float yaw, bool dead)> Bushes = new();
    public readonly List<(Vector3 pos, float size, float yaw)> Tufts = new();

    public bool InBlight(Vector2 p)
    {
        foreach (var b in Blights)
            if (p.DistanceTo(new Vector2(b.X, b.Z)) < b.R) return true;
        return false;
    }
    public readonly List<Obstacle> Obstacles = new();

    public Vector3 StartPos;
    /// <summary>Half the start/finish line's width between its posts (the gate's or the barn's walls): a lap ends only through it.</summary>
    public float LineHalf = 10f;
    public float StartYaw;

    public float WorldSize => N * Tile;

    public float VertexHeight(int ix, int iz) => Heights[iz * (N + 1) + ix];

    public SurfaceType TileSurface(int ix, int iz)
    {
        ix = Math.Clamp(ix, 0, N - 1);
        iz = Math.Clamp(iz, 0, N - 1);
        return Surf[iz * N + ix];
    }

    /// <summary>Height of the rendered mesh (no micro bumps). Triangles split along the (0,0)-(1,1) diagonal.</summary>
    public float MeshHeight(float x, float z, out Vector3 normal)
    {
        float gx = Math.Clamp(x / Tile, 0f, N - 0.0001f);
        float gz = Math.Clamp(z / Tile, 0f, N - 0.0001f);
        int ix = (int)gx, iz = (int)gz;
        float fx = gx - ix, fz = gz - iz;
        float h00 = VertexHeight(ix, iz), h10 = VertexHeight(ix + 1, iz);
        float h01 = VertexHeight(ix, iz + 1), h11 = VertexHeight(ix + 1, iz + 1);
        if (fx >= fz)
        {
            normal = new Vector3(-(h10 - h00), Tile, -(h11 - h10)).Normalized();
            return h00 + fx * (h10 - h00) + fz * (h11 - h10);
        }
        normal = new Vector3(-(h11 - h01), Tile, -(h01 - h00)).Normalized();
        return h00 + fz * (h01 - h00) + fx * (h11 - h01);
    }

    public float MeshHeight(float x, float z) => MeshHeight(x, z, out _);

    public GroundSample Sample(float x, float z)
    {
        float h = MeshHeight(x, z, out Vector3 n);
        int ix = (int)MathF.Floor(x / Tile), iz = (int)MathF.Floor(z / Tile);
        SurfaceType s = TileSurface(ix, iz);
        float b = MicroBump(x, z, s, ix, iz);
        Vector2 slope = Vector2.Zero;
        if (b != 0f || s is SurfaceType.Pothole or SurfaceType.Concrete)
        {
            const float e = 0.05f;
            slope = new Vector2(
                (MicroBump(x + e, z, s, ix, iz) - MicroBump(x - e, z, s, ix, iz)) / (2 * e),
                (MicroBump(x, z + e, s, ix, iz) - MicroBump(x, z - e, s, ix, iz)) / (2 * e));
        }
        float wet = s is SurfaceType.Water or SurfaceType.Shallow && !float.IsNaN(WaterLevel) ? MathF.Max(0f, WaterLevel - (h + b)) : 0f;
        return new GroundSample { Height = h + b, WaterDepth = wet, Normal = n, Slope = slope, Surface = s, SlurryWetness = Slurry?.Wetness(new Vector2(x, z)) ?? 0, OilFilm=MaintenanceHall?.Oil(new(x,z)) ?? 0,
            Verge = Verge != null && Verge[Math.Clamp(iz, 0, N - 1) * N + Math.Clamp(ix, 0, N - 1)] };
    }

    /// <summary>Small-scale bumps the mesh is too coarse to show but the suspension feels.</summary>
    float MicroBump(float x, float z, SurfaceType s, int ix, int iz)
    {
        switch (s)
        {
            case SurfaceType.Concrete:
            {
                // Slab joints every 4 m: a V-shaped gap between slightly tilted slabs.
                float jx = MathF.Abs(x / 4f - MathF.Round(x / 4f)) * 4f;
                float jz = MathF.Abs(z / 4f - MathF.Round(z / 4f)) * 4f;
                float j = MathF.Min(jx, jz);
                return j < 0.2f ? -0.04f * (1f - j / 0.2f) : 0f;
            }
            case SurfaceType.Pothole:
                return Pothole.Height(x - (ix + 0.5f) * Tile, z - (iz + 0.5f) * Tile, ix, iz);
            case SurfaceType.Puddle:
                return -0.08f;
            case SurfaceType.Field:
                return 0.07f * MathF.Sin(x * MathF.Tau / 1.6f);   // furrows along Z
            default:
            {
                float amp = Surfaces.Get(s).BumpAmp;
                return amp == 0f ? 0f : amp * Noise.Value(x * 0.9f, z * 0.9f, 77);
            }
        }
    }

    public int NearestCenter(Vector2 p, out float dist)
    {
        int best = 0;
        float bestD = float.MaxValue;
        for (int i = 0; i < Center.Length; i++)
        {
            float d = Center[i].DistanceSquaredTo(p);
            if (d < bestD) { bestD = d; best = i; }
        }
        dist = MathF.Sqrt(bestD);
        return best;
    }

    public float RoadLandformHeight(int centre, float distance)
    {
        if(centre<0 || RoadLandforms.Count==0)return 0;
        float height=0;
        foreach(var form in RoadLandforms)
        {
            float inner=HalfWidth[centre]+3;
            float u=Math.Clamp((distance-inner)/Math.Max(1,form.Width-inner),0,1);
            height+=form.Lift[centre]*(1-u*u*(3-2*u));
        }
        return height;
    }

    /// <summary>Road edge shared by terrain grading, grip surfaces and the verge mesh.</summary>
    public float RoadHalfWidth(int i, Vector2 p)
    {
        if (RoadLeftInset == null) return HalfWidth[i];
        var direction = CenterDir(i);
        float side = (p - Center[i]).Dot(new Vector2(-direction.Y, direction.X));
        return HalfWidth[i] - (side < 0 ? RoadLeftInset[i] : RoadRightInset[i]);
    }

    public Vector2 CenterDir(int i)
    {
        int n = Center.Length;
        return (Center[(i + 2) % n] - Center[(i - 2 + n) % n]).Normalized();
    }

    /// <summary>Yaw for a body whose forward is -Z, facing the given XZ direction.</summary>
    public static float YawFor(Vector2 dir) => MathF.Atan2(-dir.X, -dir.Y);

    public Vector3 CenterPos(int i)
    {
        Vector2 c = Center[((i % Center.Length) + Center.Length) % Center.Length];
        return new Vector3(c.X, MeshHeight(c.X, c.Y), c.Y);
    }
}

/// <summary>
/// Shape of a pothole relative to its tile centre (metres, negative = hole): a flat broken
/// bottom, steep walls and a crumbled rim. Size and depth vary per tile.
/// </summary>
public static class Pothole
{
    public static float Radius(int ix, int iz) => 0.6f + 0.25f * Noise.Hash01(ix, iz, 404);
    public static float Depth(int ix, int iz) => 0.14f + 0.14f * Noise.Hash01(ix, iz, 405);

    public static float Height(float dx, float dz, int ix, int iz)
    {
        float radius = Radius(ix, iz), depth = Depth(ix, iz);
        float r = new Vector2(dx, dz * 1.15f).Length() / radius;
        if (r < 0.65f) return -depth;
        if (r < 1f)
        {
            float k = (r - 0.65f) / 0.35f;
            return -depth * (1f - k * k * (3 - 2 * k));
        }
        if (r < 1.2f) return 0.025f * MathF.Sin((r - 1f) / 0.2f * MathF.PI);
        return 0f;
    }
}

/// <summary>Deterministic hash-based value noise; no engine dependency.</summary>
public static class Noise
{
    static float Hash(int x, int z, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + z * 668265263 + seed * 1442695041);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0xFFFFFF * 2f - 1f;
        }
    }

    public static float Hash01(int x, int z, int seed) => Hash(x, z, seed) * 0.5f + 0.5f;

    /// <summary>Smooth value noise in [-1, 1].</summary>
    public static float Value(float x, float z, int seed)
    {
        int ix = (int)MathF.Floor(x), iz = (int)MathF.Floor(z);
        float fx = x - ix, fz = z - iz;
        fx = fx * fx * (3 - 2 * fx);
        fz = fz * fz * (3 - 2 * fz);
        float a = Hash(ix, iz, seed), b = Hash(ix + 1, iz, seed);
        float c = Hash(ix, iz + 1, seed), d = Hash(ix + 1, iz + 1, seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fz);
    }

    public static float Fbm(float x, float z, int seed, int octaves = 4)
    {
        float sum = 0, amp = 1, norm = 0;
        for (int o = 0; o < octaves; o++)
        {
            sum += Value(x, z, seed + o * 31) * amp;
            norm += amp;
            amp *= 0.5f;
            x *= 2.03f;
            z *= 2.03f;
        }
        return sum / norm;
    }
}

/// <summary>A static transverse landform carrying an authored road profile into both verges.</summary>
public sealed record RoadLandform(float[] Lift,float Width);
