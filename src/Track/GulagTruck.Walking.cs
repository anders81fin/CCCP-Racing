using System;
using System.Collections.Generic;
using Godot;

namespace CCCPRacing.Track;

public partial class GulagTruck
{
    // Inflated footprints: a pedestrian's body must clear the vehicle, not just his feet.
    internal readonly record struct WalkBlock(Transform3D Frame, Vector2 Min, Vector2 Max)
    {
        public Vector3 Local(Vector3 p) => Frame.AffineInverse() * p;
        public bool Contains(Vector3 p)
        {
            p = Local(p);
            return p.X > Min.X && p.X < Max.X && p.Z > Min.Y && p.Z < Max.Y;
        }
        public Vector3 OutsidePoint(Vector3 p)
        {
            Vector3 q = Local(p);
            if (!Contains(p)) return p;
            float[] gaps = { q.X - Min.X, Max.X - q.X, q.Z - Min.Y, Max.Y - q.Z };
            int best = 0;
            for (int i = 1; i < 4; i++) if (gaps[i] < gaps[best]) best = i;
            if (best == 0) q.X = Min.X - 0.03f;
            if (best == 1) q.X = Max.X + 0.03f;
            if (best == 2) q.Z = Min.Y - 0.03f;
            if (best == 3) q.Z = Max.Y + 0.03f;
            return Frame * q;
        }
        public bool Cuts(Vector3 a, Vector3 b)
        {
            a = Local(a); b = Local(b);
            float lo = 0f, hi = 1f;
            // Clip against the open interior; touching a routing corner is safe.
            for (int axis = 0; axis < 2; axis++)
            {
                float start = axis == 0 ? a.X : a.Z, delta = axis == 0 ? b.X - a.X : b.Z - a.Z;
                float min = (axis == 0 ? Min.X : Min.Y) + 0.001f;
                float max = (axis == 0 ? Max.X : Max.Y) - 0.001f;
                if (Math.Abs(delta) < 0.00001f) { if (start <= min || start >= max) return false; }
                else
                {
                    float t0 = (min - start) / delta, t1 = (max - start) / delta;
                    lo = Math.Max(lo, Math.Min(t0, t1)); hi = Math.Min(hi, Math.Max(t0, t1));
                    if (hi <= lo) return false;
                }
            }
            return hi > lo;
        }
    }

    WalkBlock _carBlock;
    Vector3[] _outRoute, _backRoute, _driverRoute;
    WalkBlock TruckBlock => new(_model.GlobalTransform,
        new(-1.25f * TruckScale - 0.65f, -3.5f * TruckScale - 0.65f),
        new(1.25f * TruckScale + 0.65f, 4.1f * TruckScale + 0.65f));

    WalkBlock[] WalkBlocks(bool driver)
    {
        if (!driver || !_driver.Visible) return new[] { TruckBlock, _carBlock };
        return new[] { TruckBlock, _carBlock,
            new WalkBlock(new Transform3D(Basis.Identity, _driver.GlobalPosition), new(-1.1f, -1.1f), new(1.1f, 1.1f)) };
    }

    // A tiny visibility graph around both rotated vehicles and the player. Every segment is
    // checked, including the final approach: pushing only the endpoint outside is insufficient.
    internal static Vector3[] WalkingRoute(Vector3 from, Vector3 to, WalkBlock[] blocks)
    {
        for (int pass = 0; pass < blocks.Length + 1; pass++)
            foreach (var block in blocks) to = block.OutsidePoint(to);
        var nodes = new List<Vector3> { from, to };
        foreach (var block in blocks)
            foreach (float x in new[] { block.Min.X - 0.03f, block.Max.X + 0.03f })
                foreach (float z in new[] { block.Min.Y - 0.03f, block.Max.Y + 0.03f })
                {
                    Vector3 p = block.Frame * new Vector3(x, 0, z);
                    if (Array.TrueForAll(blocks, b => !b.Contains(p))) nodes.Add(p);
                }
        float[] costs = new float[nodes.Count]; Array.Fill(costs, float.PositiveInfinity); costs[0] = 0;
        int[] previous = new int[nodes.Count]; Array.Fill(previous, -1);
        bool[] done = new bool[nodes.Count];
        for (int n = 0; n < nodes.Count; n++)
        {
            int at = -1;
            for (int i = 0; i < nodes.Count; i++) if (!done[i] && (at < 0 || costs[i] < costs[at])) at = i;
            if (at < 0 || float.IsPositiveInfinity(costs[at])) break;
            if (at == 1) break;
            done[at] = true;
            for (int i = 1; i < nodes.Count; i++)
            {
                if (done[i] || !Array.TrueForAll(blocks, b => !b.Cuts(nodes[at], nodes[i]))) continue;
                float cost = costs[at] + ((nodes[i] - nodes[at]) with { Y = 0 }).Length();
                if (cost < costs[i]) { costs[i] = cost; previous[i] = at; }
            }
        }
        if (previous[1] < 0) return new[] { from }; // Wait safely if there is no clear route.
        var route = new List<Vector3>();
        for (int at = 1; at >= 0; at = previous[at]) route.Add(nodes[at]);
        route.Reverse();
        return route.ToArray();
    }
}
