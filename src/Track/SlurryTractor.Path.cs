using System;
using System.Collections.Generic;
using Godot;

namespace CCCPRacing.Track;

public partial class SlurryTractor
{
    List<Vector2> _path; int _pathAt; float _pathRetry;

    /// <summary>The room a tractor and its tanker need beyond an obstacle's own radius (m): the tanker's circle (1.5) + its swing inside a sharp corner (about 1.0) + a hand's breadth. No path is ever planned with less.</summary>
    internal const float MinClearance = 2.7f;

    /// <summary>
    /// The way to a goal across open country: false when there is no path with the room the vehicle needs (it then stands still and tries again in two seconds: never a straight line through fixed things), else the unit direction to the next waypoint of a
    /// path planned once around every fixed obstacle, water and the hedge.
    /// </summary>
    bool PathForward(Vector2 goal, float dt, out Vector2 direction)
    {
        direction = Vector2.Zero;
        if (_path == null)
        {
            _pathRetry -= dt; if (_pathRetry > 0f) return false;
            _path = Plan(_track, _pos, goal, _bodySlot, _tankSlot);
            if (_path == null) { _pathRetry = 2f; return false; }
            _pathAt = 0;
        }
        while (_pathAt < _path.Count - 1 && _pos.DistanceTo(_path[_pathAt]) < 2.2f) _pathAt++;
        Vector2 to = _path[Math.Min(_pathAt, _path.Count - 1)] - _pos;
        direction = to.LengthSquared() > 0.01f ? to.Normalized() : (goal - _pos).Normalized();
        return true;
    }

    /// <summary>A* over a 2.5 m grid (see TryPlan) with the clearance widest first, never below <see cref="MinClearance"/>; null when there is no way.</summary>
    internal static List<Vector2> Plan(TrackData t, Vector2 from, Vector2 to, int skipA = -1, int skipB = -1)
    {
        foreach (float clearance in new[] { 3.6f, 3.2f, MinClearance })
        {
            var path = TryPlan(t, from, to, clearance, skipA, skipB);
            if (path != null) return path;
        }
        return null;
    }

    static List<Vector2> TryPlan(TrackData track, Vector2 from, Vector2 to, float clearance, int skipA, int skipB)
    {
        const float Cell = 2.5f;
        int n = (int)(track.WorldSize / Cell) + 1;
        var blocked = new bool[n * n];
        void Mark(Vector2 c, float radius)
        {
            int x0 = Math.Max(0, (int)((c.X - radius) / Cell)), x1 = Math.Min(n - 1, (int)((c.X + radius) / Cell)), z0 = Math.Max(0, (int)((c.Y - radius) / Cell)), z1 = Math.Min(n - 1, (int)((c.Y + radius) / Cell));
            for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++)
                if (new Vector2((x + 0.5f) * Cell, (z + 0.5f) * Cell).DistanceTo(c) < radius + Cell * 0.5f) blocked[z * n + x] = true;
        }
        for (int i = 0; i < track.Obstacles.Count; i++)
        {
            if (i == skipA || i == skipB) continue;
            var o = track.Obstacles[i];
            if (o.Center.X < -9000f || o.Radius < 0.1f) continue;
            Mark(o.Center, o.Radius + clearance);
        }
        for (int z = 0; z < n; z++) for (int x = 0; x < n; x++)
        {
            var c = new Vector2((x + 0.5f) * Cell, (z + 0.5f) * Cell);
            if (c.X < 14f || c.Y < 14f || c.X > track.WorldSize - 14f || c.Y > track.WorldSize - 14f) { blocked[z * n + x] = true; continue; }
            if (track.Sample(c.X, c.Y).Surface is SurfaceType.Water or SurfaceType.Shallow) blocked[z * n + x] = true;
        }
        // Clear round the start and the goal (the barn, the circuit's own point).
        foreach (var e in new[] { from, to })
        {
            int ex = (int)(e.X / Cell), ez = (int)(e.Y / Cell);
            for (int dz = -3; dz <= 3; dz++) for (int dx = -3; dx <= 3; dx++) { int x = ex + dx, z = ez + dz; if (x >= 0 && z >= 0 && x < n && z < n) blocked[z * n + x] = false; }
        }
        int start = Math.Clamp((int)(from.Y / Cell), 0, n - 1) * n + Math.Clamp((int)(from.X / Cell), 0, n - 1), goal = Math.Clamp((int)(to.Y / Cell), 0, n - 1) * n + Math.Clamp((int)(to.X / Cell), 0, n - 1);
        var g = new float[n * n]; Array.Fill(g, float.MaxValue); var parent = new int[n * n]; Array.Fill(parent, -1);
        var open = new PriorityQueue<int, float>(); g[start] = 0; open.Enqueue(start, 0);
        float H(int c) { float dx = Math.Abs(c % n - goal % n), dz = Math.Abs(c / n - goal / n); return (dx + dz + (1.4142f - 2f) * Math.Min(dx, dz)) * Cell; }
        bool found = false;
        while (open.TryDequeue(out int cur, out _))
        {
            if (cur == goal) { found = true; break; }
            int cx = cur % n, cz = cur / n;
            for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dz == 0) continue;
                int x = cx + dx, z = cz + dz; if (x < 0 || z < 0 || x >= n || z >= n) continue;
                int nb = z * n + x; if (blocked[nb]) continue;
                if (dx != 0 && dz != 0 && (blocked[cz * n + x] || blocked[z * n + cx])) continue;      // no squeezing through a diagonal gap
                float step = (dx != 0 && dz != 0 ? 1.4142f : 1f) * Cell, ng = g[cur] + step;
                if (ng < g[nb]) { g[nb] = ng; parent[nb] = cur; open.Enqueue(nb, ng + H(nb)); }
            }
        }
        if (!found) return null;
        var cells = new List<Vector2>();
        for (int c = goal; c != -1; c = parent[c]) cells.Add(new Vector2((c % n + 0.5f) * Cell, (c / n + 0.5f) * Cell));
        cells.Reverse();
        // String pulling: from each waypoint the farthest later one with a clear straight line.
        bool Clear(Vector2 a, Vector2 b)
        {
            int steps = (int)(a.DistanceTo(b) / (Cell * 0.5f)) + 1;
            for (int s = 0; s <= steps; s++) { var p = a.Lerp(b, s / (float)steps); if (blocked[Math.Clamp((int)(p.Y / Cell), 0, n - 1) * n + Math.Clamp((int)(p.X / Cell), 0, n - 1)]) return false; }
            return true;
        }
        var result = new List<Vector2> { from }; int at = 0;
        while (at < cells.Count - 1)
        {
            int far = cells.Count - 1;
            while (far > at + 1 && !Clear(cells[at], cells[far])) far--;
            result.Add(cells[far]); at = far;
        }
        result[^1] = to;
        return result;
    }
}
