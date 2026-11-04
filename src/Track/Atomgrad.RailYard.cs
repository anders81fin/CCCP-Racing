using System;
using Godot;

namespace CCCPRacing.Track;

public static partial class Atomgrad
{
    // Local coordinates: the running rail is X=0, north is -Z; the road is beyond the south gate.
    static readonly (Vector2 a, Vector2 b)[] YardFence = {
        (new(-10, -29), new(23, -29)), (new(-10, -29), new(-10, 31)),
        (new(23, -29), new(23, 31)),
        (new(-10, 31), new(-2.2f, 31)), (new(2.2f, 31), new(12, 31)),
        (new(19, 31), new(23, 31))
    };

    public static Node3D RailYard(TrackData t, Vector3 at, float yaw)
    {
        var root = new Node3D { Name = "PlantRailYard" };
        var solid = new Node3D();
        var basis = new Basis(Vector3.Up, yaw);
        float Ground(float x, float z)
        {
            var w = at + basis * new Vector3(x, 0, z);
            return t.MeshHeight(w.X, w.Z) - at.Y;
        }
        void Box(float w, float h, float l, Color color, float x, float y, float z) =>
            Add(solid, Props.Box(w, h, l), color, x, y, z);
        // Cracked service apron, with a narrow gravel drive leading out to the road.
        for (int x = 5; x < 23; x += 3)
        for (int z = -19; z < 31; z += 3)
            Box(2.96f, 0.08f, 2.96f, (x + z) % 3 == 0 ? ConcreteStain : ConcreteDark,
                x + 1.5f, Ground(x + 1.5f, z + 1.5f) + 0.025f, z + 1.5f);
        for (float z = 32.5f; z < 44; z += 3)
            Box(6.6f, 0.06f, 3.04f, new Color(0.35f, 0.33f, 0.28f), 15.5f, Ground(15.5f, z) + 0.025f, z);

        // Small brick-and-corrugated-steel stores building, front facing the crane and gate.
        float floor = Math.Max(Math.Max(Ground(9, -16), Ground(19, -16)), Math.Max(Ground(9, 6), Ground(19, 6)));
        Box(10.6f, 0.55f, 22.6f, ConcreteStain, 14, floor + 0.05f, -5);
        Box(10, 4.8f, 22, new Color(0.44f, 0.36f, 0.28f), 14, floor + 2.65f, -5);
        Box(10.8f, 0.2f, 22.8f, new Color(0.29f, 0.33f, 0.32f), 14, floor + 5.16f, -5);
        Box(0.18f, 0.35f, 23, RustDark, 8.65f, floor + 5.13f, -5);
        Box(0.18f, 0.35f, 23, RustDark, 19.35f, floor + 5.13f, -5);
        for (int k = 0; k < 12; k++)
            Box(10.8f, 0.045f, 0.09f, Steel, 14, floor + 5.28f, -15.5f + k * 1.9f);
        // Sliding door onto the south apron, and a rail-side loading door onto the platform.
        Box(4.6f, 3.3f, 0.12f, RustDark, 14, floor + 1.94f, 6.07f);
        for (int k = 0; k < 12; k++) Box(4.4f, 0.055f, 0.15f, Rust, 14, floor + 0.45f + k * 0.26f, 6.15f);
        Box(0.12f, 3.1f, 4.8f, RustDark, 8.94f, floor + 1.88f, -2);
        for (int k = 0; k < 10; k++) Box(0.15f, 0.05f, 4.6f, Rust, 8.86f, floor + 0.55f + k * 0.28f, -2);
        Box(5.2f, 0.1f, 0.18f, Steel, 14, floor + 3.7f, 6.18f);
        Box(0.3f, 3.5f, 0.12f, ConcreteDark, 16.6f, floor + 2, 6.1f);
        foreach (float z in new[] { -12f, 3f })
        {
            Box(0.1f, 1f, 2.3f, Black, 8.92f, floor + 3.95f, z);
            Box(0.12f, 0.86f, 2.1f, Window, 8.85f, floor + 3.95f, z);
            Box(0.15f, 1f, 0.06f, Steel, 8.78f, floor + 3.95f, z);
        }
        root.AddChild(Buildings.TextPanel("АЭС-4 · СКЛАД", 7.5f, 0.7f,
            new Vector3(14, floor + 4.35f, 6.2f), White, new Color(0.18f, 0.25f, 0.22f)));
        // Raised loading platform beside the track; its edge stays clear of the wagons.
        for (float z = -14; z <= 19; z += 3)
        {
            float h = Ground(5.5f, z);
            Box(4.8f, 0.9f, 2.96f, ConcreteDark, 5.5f, h + 0.45f, z);
            Box(4.95f, 0.12f, 2.99f, Concrete, 5.5f, h + 0.96f, z);
            Box(0.08f, 0.14f, 2.9f, Yellow.Darkened(0.3f), 3.05f, h + 0.9f, z);
        }
        // Gantry straddles the rail and loading platform. All legs meet the same level top beam.
        float beamY = Math.Max(Math.Max(Ground(-4, 11), Ground(9, 11)), Math.Max(Ground(-4, 19), Ground(9, 19))) + 8.4f;
        foreach (float x in new[] { -4f, 9f })
        foreach (float z in new[] { 11f, 19f })
        {
            float ground = Ground(x, z), height = beamY - ground;
            Box(0.9f, 0.3f, 0.9f, Concrete, x, ground + 0.15f, z);
            Box(0.32f, height, 0.32f, Rust, x, ground + height / 2, z);
            Rod(solid, new Vector3(x, ground + 1.3f, z), new Vector3(x, beamY - 0.4f, z == 11 ? 19 : 11), 0.11f, RustDark);
        }
        foreach (float z in new[] { 11f, 19f }) Box(13.8f, 0.6f, 0.35f, Yellow.Darkened(0.28f), 2.5f, beamY, z);
        foreach (float x in new[] { -4f, 9f }) Box(0.4f, 0.4f, 8.5f, RustDark, x, beamY + 0.2f, 15);
        Box(0.7f, 0.4f, 8.1f, Steel, 4.3f, beamY + 0.2f, 15);
        Box(1.2f, 0.6f, 1.2f, RustDark, 4.3f, beamY - 0.45f, 15);
        float hookY = Ground(4.3f, 15) + 3.8f;
        Rod(solid, new Vector3(4.15f, beamY - 0.7f, 15), new Vector3(4.15f, hookY, 15), 0.035f, Black);
        Rod(solid, new Vector3(4.45f, beamY - 0.7f, 15), new Vector3(4.45f, hookY, 15), 0.035f, Black);
        Box(0.5f, 0.55f, 0.3f, Yellow.Darkened(0.2f), 4.3f, hookY, 15);
        Rod(solid, new Vector3(4.3f, hookY - 0.2f, 15), new Vector3(4.3f, hookY - 0.65f, 15), 0.12f, Steel);
        Rod(solid, new Vector3(4.3f, hookY - 0.65f, 15), new Vector3(4.6f, hookY - 0.65f, 15), 0.12f, Steel);
        // Palletised stores, cable drums and stacked pipe sections await unloading.
        foreach (var p in new[] { new Vector2(14, 15), new Vector2(18, 21), new Vector2(12, 25) })
        {
            float h = Ground(p.X, p.Y);
            Box(2.2f, 0.18f, 1.8f, RustDark, p.X, h + 0.09f, p.Y);
            Box(1.9f, 1.3f, 1.5f, new Color(0.46f, 0.36f, 0.22f), p.X, h + 0.83f, p.Y);
            foreach (float s in new[] { -0.65f, 0.65f }) Box(0.09f, 1.35f, 1.55f, Steel, p.X + s, h + 0.83f, p.Y);
        }
        for (int k = 0; k < 4; k++)
            Add(solid, Props.Cyl(0.3f, 4f, 12), RustDark, 17 + k * 0.65f, Ground(18, -23) + 0.3f, -23, 90, 0, 0);
        foreach (float x in new[] { 11f, 14f })
        {
            float h = Ground(x, -23);
            Add(solid, Props.Cyl(0.75f, 0.75f, 12), Black, x, h + 0.8f, -23, 90, 0, 0);
            foreach (float z in new[] { -23.45f, -22.55f })
                Add(solid, Props.Cyl(0.9f, 0.09f, 12), new Color(0.41f, 0.29f, 0.18f), x, h + 0.8f, z, 90, 0, 0);
        }
        // Fencing is open across the railway and at the separate road entrance.
        foreach (var fence in YardFence)
        {
            int spans = (int)MathF.Ceiling(fence.a.DistanceTo(fence.b) / 3f);
            for (int k = 0; k <= spans; k++)
            {
                var p = fence.a.Lerp(fence.b, k / (float)spans);
                float h = Ground(p.X, p.Y);
                Box(0.1f, 2.35f, 0.1f, RustDark, p.X, h + 1.175f, p.Y);
                if (k == spans) continue;
                var q = fence.a.Lerp(fence.b, (k + 1) / (float)spans);
                float qh = Ground(q.X, q.Y);
                foreach (float y in new[] { 0.35f, 1.05f, 1.75f, 2.2f })
                    Rod(solid, new Vector3(p.X, h + y, p.Y), new Vector3(q.X, qh + y, q.Y), 0.035f, Steel);
                for (int wire = 1; wire <= 5; wire++)
                {
                    var v = p.Lerp(q, wire / 6f); float vh = Ground(v.X, v.Y);
                    Box(0.022f, 1.85f, 0.022f, Steel, v.X, vh + 1.25f, v.Y);
                }
            }
        }
        // Entrance gate folded against the east fence, leaving a seven metre service lane.
        Rod(solid, new Vector3(23, Ground(23, 24) + 1.2f, 24), new Vector3(23, Ground(23, 30) + 1.2f, 30), 0.08f, Rust);
        root.AddChild(Buildings.TextPanel("АЭС-4 · ГРУЗОВОЙ ДВОР", 8.7f, 0.6f,
            new Vector3(7, Ground(7, 31) + 2.55f, 31.08f), White, new Color(0.18f, 0.25f, 0.22f)));
        // Buffer stop is beyond the locomotive's stopping point, on the rail centre line.
        float bufferY = Ground(0, -27);
        foreach (float x in new[] { -0.76f, 0.76f })
        {
            Rod(solid, new Vector3(x, bufferY + 0.25f, -28.3f), new Vector3(x, bufferY + 1.3f, -27), 0.2f, RustDark);
            Box(0.25f, 0.9f, 0.25f, Rust, x, bufferY + 0.7f, -27);
        }
        Box(2.35f, 0.38f, 0.3f, White.Darkened(0.15f), 0, bufferY + 1.15f, -27);
        foreach (float x in new[] { -0.85f, -0.28f, 0.28f, 0.85f })
            Box(0.28f, 0.39f, 0.035f, Red, x, bufferY + 1.15f, -26.83f);
        Merge(root, solid);
        var lamp = NightGlow.Part(Props.Box(0.55f, 0.15f, 0.4f), new Color(0.7f, 0.7f, 0.59f), new Color(1f, 0.86f, 0.6f), 1.6f,
            new Vector3(14, floor + 3.85f, 6.4f));
        root.AddChild(lamp);
        return root;
    }

    public static void RailYardObstacles(TrackData t, Vector2 at, float yaw)
    {
        Vector2 ax = new(MathF.Cos(yaw), -MathF.Sin(yaw)), az = new(MathF.Sin(yaw), MathF.Cos(yaw));
        Vector2 World(Vector2 local) => at + ax * local.X + az * local.Y;
        void Row(Vector2 a, Vector2 b, float radius)
        {
            int steps = Math.Max(1, (int)MathF.Ceiling(a.DistanceTo(b) / (radius * 1.5f)));
            for (int i = 0; i <= steps; i++) t.Obstacles.Add(new Obstacle(World(a.Lerp(b, i / (float)steps)), radius));
        }
        // Building, platform edge, crane legs, stores and buffer; leave the rail corridor open.
        for (float z = -12; z <= 2; z += 4) t.Obstacles.Add(new Obstacle(World(new Vector2(14, z)), 5f));
        foreach (float x in new[] { 4f, 5.6f, 7.2f }) Row(new(x, -14.6f), new(x, 19.6f), 0.9f);
        foreach (float x in new[] { -4f, 9f })
        foreach (float z in new[] { 11f, 19f }) t.Obstacles.Add(new Obstacle(World(new(x, z)), 0.5f));
        foreach (var p in new[] { new Vector2(14, 15), new Vector2(18, 21), new Vector2(12, 25) })
            t.Obstacles.Add(new Obstacle(World(p), 1.15f));
        Row(new(-1, -27), new(1, -27), 0.45f);
        foreach (var fence in YardFence) Row(fence.a, fence.b, 0.2f);
    }
}
