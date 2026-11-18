using System;
using System.Linq;
using CCCPRacing.Car;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    // --tank-roam-test: the roaming tank exercise on Poligon: ten simulated minutes. The tanks must move about widely, never cross the range's north line (the base's height), never drive into fixed things,
    // never jump, fire often and spend part of the time across the road.
    void TankRoamTest()
    {
        var track = TrackBuilder.Build(TrackDef.Load("res://tracks/bonus.json"));
        var fx = new TyreEffects(); AddChild(fx); fx.Init(track);
        var tanks = new TankBattle(); AddChild(tanks);
        var tr = track.TankRange;
        int fixedCount = track.Obstacles.Count;
        var fixedObs = new (Vector2 c, float r)[fixedCount]; for (int i = 0; i < fixedCount; i++) fixedObs[i] = (track.Obstacles[i].Center, track.Obstacles[i].Radius);
        { var bins = new int[6]; foreach (var o in track.Obstacles) if (o.Center.X > tr.X0 && o.Center.X < tr.X1 && o.Center.Y > tr.Z0 && o.Center.Y < tr.Z1 && o.Radius > 0.15f) bins[Math.Min(5, (int)(o.Radius / 0.5f))]++; GD.Print($"TANKRANGE obstacles by radius (0.5 m bins, last: 2.5 m and up): {string.Join(", ", bins)}"); }
        tanks.Init(track, fx, new Rect2(tr.X0, tr.Z0, tr.X1 - tr.X0, tr.Z1 - tr.Z0), tr.Tanks);
        var hulls = tanks.HullObstacles.ToArray();
        int perTank = 3, n = hulls.Length / perTank;
        int shots = 0; tanks.Fired += _ => shots++;
        float closestTanks = 1e9f; int near15 = 0, nearFrames = 0; var deepNote = new string[n]; var maxStep = new float[n]; var prev = new Vector2[n]; var travelled = new float[n]; var minZ = new float[n]; var minX = new float[n]; var maxX = new float[n]; var onRoad = new int[n]; var jumps = new int[n]; var deep = new float[n];
        for (int k = 0; k < n; k++) { prev[k] = track.Obstacles[hulls[k * perTank + 1]].Center; minZ[k] = 1e9f; minX[k] = 1e9f; maxX[k] = -1e9f; }
        int hallFrames = 0; int frames = 60 * 60 * 10;
        for (int i = 0; i < frames; i++)
        {
            tanks.Tick(1f / 60f, null);
            for (int a = 0; a < n; a++) for (int b = a + 1; b < n; b++)
            {
                float dd = track.Obstacles[hulls[a * perTank + 1]].Center.DistanceTo(track.Obstacles[hulls[b * perTank + 1]].Center);
                if (i > 60) { closestTanks = MathF.Min(closestTanks, dd); nearFrames++; if (dd < 15f) near15++; }
            }
            for (int k = 0; k < n; k++)
            {
                Vector2 mid = track.Obstacles[hulls[k * perTank + 1]].Center;
                float step = i == 0 ? 0f : mid.DistanceTo(prev[k]); travelled[k] += step; maxStep[k] = MathF.Max(maxStep[k], step); if (step > 0.5f) { jumps[k]++; if (jumps[k] <= 3) GD.Print($"  tank {k} JUMP t={i / 60f:F1}s step {step:F1} m from {prev[k]} to {mid}"); } prev[k] = mid;
                if (track.MaintenanceHall != null && track.MaintenanceHall.Contains(mid, 0f)) hallFrames++;
                minZ[k] = MathF.Min(minZ[k], mid.Y); minX[k] = MathF.Min(minX[k], mid.X); maxX[k] = MathF.Max(maxX[k], mid.X);
                int ci = track.NearestCenter(mid, out float d); if (d < track.HalfWidth[ci] + 1f) onRoad[k]++;
                for (int s = 0; s < perTank; s++)
                {
                    Vector2 c = track.Obstacles[hulls[k * perTank + s]].Center;
                    for (int q = 0; q < fixedCount; q++)
                    {
                        if (fixedObs[q].r < 0.3f) continue;
                        float pen = fixedObs[q].r + 1.8f - c.DistanceTo(fixedObs[q].c);
                        if (pen > deep[k]) { deep[k] = pen; if (pen > 0.6f && deepNote[k] == null) deepNote[k] = $"t={i / 60f:F1}s slot {s} at {c} in obstacle {fixedObs[q].c} r={fixedObs[q].r:F2} (tank {track.Obstacles[hulls[k * perTank + 1]].Center})"; }
                    }
                }
            }
        }
        for (int k = 0; k < n; k++)
            GD.Print($"TANK {k}: travelled {travelled[k]:F0} m, x {minX[k]:F0}-{maxX[k]:F0}, north-most z {minZ[k]:F0}, {onRoad[k] / 60f:F0} s on the road, {jumps[k]} jumps (largest step {maxStep[k]:F2} m), deepest overlap {deep[k]:F2} m");
        for (int k = 0; k < n; k++) if (travelled[k] < 500f) { Vector2 at = prev[k]; foreach (var (c, r) in fixedObs) if (c.DistanceTo(at) < 9f && r > 0.15f) GD.Print($"  tank {k} at {at}: obstacle {c} r={r:F2} gap {c.DistanceTo(at) - r:F2}"); }
        for (int k = 0; k < n; k++) if (deepNote[k] != null) GD.Print($"  tank {k} first deep overlap {deepNote[k]}");
        GD.Print($"TANKS: closest two tanks {closestTanks:F1} m apart, within 15 m of each other {100f * near15 / Math.Max(1, nearFrames):F1} % of the pair-time");
        Check(closestTanks > 7f, $"no two tanks touch: the closest are {closestTanks:F1} m apart");
        Check(100f * near15 / Math.Max(1, nearFrames) < 6f, "the tanks keep their distance: within 15 m of each other under 6 % of the time");
        GD.Print($"TANKS: {shots} rounds in ten minutes");
        Check(n >= 4, "the roaming exercise has four tanks");
        Check(Enumerable.Range(0, n).All(k => minZ[k] >= tr.Z0 - 3f), $"no tank crosses the range's north line (z {tr.Z0}): the base's height is never reached");
        Check(Enumerable.Range(0, n).All(k => travelled[k] > 600f && maxX[k] - minX[k] > 120f), "every tank drives widely (over 600 m and across 120 m of the range in ten minutes)");
        Check(Enumerable.Range(0, n).All(k => jumps[k] == 0), "no tank jumps");
        Check(Enumerable.Range(0, n).All(k => deep[k] < 0.6f), "no tank hull is driven deep into a fixed thing");
        if (track.MaintenanceHall != null) Check(hallFrames == 0, $"no tank drives into the repair hall ({hallFrames} tank-frames inside)");
        Check(shots >= 40, $"the tanks fire often ({shots} rounds in ten minutes)");
        Check(onRoad.Sum() > 60 * 20, $"the tanks spend time on the road ({onRoad.Sum() / 60f:F0} tank-seconds), parked across it");
        tanks.Free(); fx.Free();
    }
}
