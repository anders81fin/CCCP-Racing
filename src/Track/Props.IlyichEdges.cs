using System;
using System.Collections.Generic;
using Godot;

namespace CCCPRacing.Track;

public static partial class Props
{
    internal record EdgeTree(Vector2 Position, string Kind, float Size, float Yaw);

    // One layout on both sides of the playable boundary; in-map trunks use ordinary tree collisions.
    internal static IEnumerable<EdgeTree> IlyichEdgeTrees(TrackData t)
    {
        if (t.Id != "ilyich") yield break;
        var rng = new RandomNumberGenerator { Seed = 192207UL };
        var groves = new (Vector2 Centre, Vector2 Radius, int Count, bool Pine)[]
        {
            (new(322, 76), new(18, 22), 17, true),
            (new(328, 132), new(23, 27), 24, true),
            (new(326, 195), new(18, 21), 19, true),
            (new(268, 316), new(18, 16), 12, false),
            (new(210, 324), new(22, 13), 14, false),
            (new(126, 322), new(23, 14), 14, false),
            (new(-18, 92), new(16, 18), 12, false),
            (new(-22, 170), new(12, 21), 13, false),
            (new(-18, 263), new(17, 23), 15, false),
            (new(43, -14), new(32, 6), 16, false),
            (new(238, -9), new(30, 5), 15, false),
            (new(284, 20), new(9, 16), 9, false),
        };
        foreach (var grove in groves)
        for (int i = 0; i < grove.Count; i++)
        {
            float angle = rng.RandfRange(0, MathF.Tau), radius = MathF.Sqrt(rng.Randf());
            Vector2 p = grove.Centre + new Vector2(MathF.Cos(angle) * grove.Radius.X, MathF.Sin(angle) * grove.Radius.Y) * radius;
            // Keep open windows between groves, rather than drawing a wall around the map.
            yield return new(p, grove.Pine && rng.Randf() < 0.7f ? "pine" : "birch",
                rng.RandfRange(0.65f, 1.15f), rng.RandfRange(0, MathF.Tau));
        }
    }

    internal static Node3D IlyichEdgeScenery(TrackData t)
    {
        Node3D result = null;
        foreach (var step in IlyichEdgeSceneryParts(t)) result = step ?? result;
        return result;
    }

    /// <summary>The same scenery in slices (null: let a frame go by while a loading card runs); the last item is the finished node.</summary>
    internal static IEnumerable<Node3D> IlyichEdgeSceneryParts(TrackData t)
    {
        var root = new Node3D { Name = "IlyichEdgeScenery" };
        var slice = System.Diagnostics.Stopwatch.StartNew();
        if (t.Id != "ilyich") { yield return root; yield break; }
        // A build abandoned between slices (a cancelled replay viewer) frees what it still holds: the root and the groups not yet in it.
        var pending = new List<Node3D>();
        bool handedOver = false;
        try
        {
        Vector3 Ground(Vector2 p, float lift = 0) => new(p.X, WorldBackdrop.SurfaceHeightAt(t, p) + lift, p.Y);

        // Small batches retain local bounds for lighting and culling.
        var outerTrees = new Dictionary<string, Node3D>();
        foreach (var tree in IlyichEdgeTrees(t))
        {
            if (WorldBackdrop.OutsideDistance(t, tree.Position) <= 0) continue;
            string sector = tree.Position.Y < 0 ? "Northern shelterbelts" : tree.Position.X < 0 ? "Village woods"
                : tree.Position.Y > t.WorldSize ? "Meadow woods" : "Ridge woods";
            if (!outerTrees.TryGetValue(sector, out var grove))
                { outerTrees[sector] = grove = new Node3D { Name = sector }; pending.Add(grove); }
            int x = (int)(tree.Position.X * 10), z = (int)(tree.Position.Y * 10);
            var model = tree.Kind == "pine" ? Pine(tree.Size, x, z) : Birch(tree.Size, x, z);
            model.Position = Ground(tree.Position);
            model.Rotation = new Vector3(0, tree.Yaw, 0);
            grove.AddChild(model);
            // Small groves: batching a whole sector at once is one long stall, and small batches cull better anyway.
            if (grove.GetChildCount() >= 50) { foreach (var _ in DrunkTractor.BatchStaticSteps(grove, null)) { LoadProf.At(); yield return null; slice.Restart(); } root.AddChild(grove); outerTrees.Remove(sector); LoadProf.At(); yield return null; slice.Restart(); }
            else if (slice.ElapsedMilliseconds > 10) { LoadProf.At(); yield return null; slice.Restart(); }
        }
        foreach (var grove in outerTrees.Values) { foreach (var _ in DrunkTractor.BatchStaticSteps(grove, null)) { LoadProf.At(); yield return null; slice.Restart(); } root.AddChild(grove); LoadProf.At(); yield return null; slice.Restart(); }
        foreach (var _ in IlyichNorthernFields(t, root, pending)) { LoadProf.At(); yield return null; slice.Restart(); }
        LoadProf.At(); yield return null;
        slice.Restart();

        // The village already has front-yard life; the western edge now reads as working back yards.
        foreach (float z in new[] { 118f, 150f, 186f, 214f, 240f })
        {
            var yard = new Node3D { Name = "Kitchen garden " + z }; pending.Add(yard);
            Vector2 centre = new(0, z);
            foreach (var _ in PatchSteps(yard, t, centre, new Vector2(6, 8), new Color(0.29f, 0.23f, 0.13f), 4)) { LoadProf.At(); yield return null; slice.Restart(); }
            for (int row = 0; row < 5; row++)
            {
                float x = -4.3f + row * 2.05f;
                for (int k = 0; k < 7; k++)
                {
                    Vector2 p = centre + new Vector2(x, -5.7f + k * 1.8f);
                    float h = Noise.Hash01(row, k + (int)z, 611);
                    var leaf = Part(Ball(0.42f + h * 0.16f), new Color(0.28f + h * 0.08f, 0.40f + h * 0.10f, 0.18f), Ground(p, 0.22f));
                    leaf.Scale = new Vector3(1, 0.65f, 1);
                    yard.AddChild(leaf);
                }
            }
            // A low, incomplete fence leaves the garden connected to the house's back door.
            for (int k = 0; k < 6; k++)
            {
                Vector2 p = new(-7.4f, z - 8 + k * 3.2f);
                yard.AddChild(Part(Box(0.16f, 1.15f, 0.16f), Wood, Ground(p, 0.55f)));
                if (k < 5 && k != 2)
                    foreach (float y in new[] { 0.35f, 0.78f })
                        yard.AddChild(Part(Box(0.08f, 0.12f, 3.2f), Timber, Ground(p + new Vector2(0, 1.6f), y)));
            }
            var shed = new Node3D { Position = Ground(new Vector2(-12, z + 6)) };
            shed.AddChild(Part(Box(4.2f, 2.7f, 5.2f), new Color(0.33f, 0.29f, 0.22f), new Vector3(0, 1.35f, 0)));
            shed.AddChild(Part(new PrismMesh { Size = new Vector3(4.8f, 1.7f, 5.8f) }, Roof, new Vector3(0, 3.5f, 0)));
            shed.AddChild(Part(Box(1.4f, 2.1f, 0.06f), new Color(0.16f, 0.14f, 0.10f), new Vector3(0, 1.05f, 2.64f)));
            for (int board = 0; board < 9; board++)
                shed.AddChild(Part(Box(0.06f, 2.6f, 0.06f), Timber, new Vector3(-1.9f + board * 0.47f, 1.35f, 2.65f)));
            yard.AddChild(shed);
            if (slice.ElapsedMilliseconds > 10) { LoadProf.At(); yield return null; slice.Restart(); }
            for (int row = 0; row < 3; row++)
            for (int k = 0; k < 5 - row; k++)
                yard.AddChild(Part(Cyl(0.23f, 2.4f, 6), new Color(0.48f, 0.35f, 0.20f),
                    Ground(new Vector2(5.1f + row * 0.2f, z + 5.5f + k * 0.48f), 0.25f + row * 0.43f), new Vector3(0, 0, 90)));
            for (int k = 0; k < 3; k++)
            {
                Vector2 p = new(-22 + k % 2 * 4, z - 8 + k * 7);
                Vector3 ground = Ground(p);
                yard.AddChild(Part(Cyl(0.2f, 2.8f, 6), Timber, ground + Vector3.Up * 1.4f));
                var crown = Part(Ball(2.1f, 7), new Color(0.25f, 0.39f, 0.16f), ground + Vector3.Up * 3.2f);
                crown.Scale = new Vector3(1.1f, 0.8f, 1);
                yard.AddChild(crown);
                for (int apple = 0; apple < 7; apple++)
                {
                    float angle = apple * 2.399f;
                    yard.AddChild(Part(Ball(0.14f, 5), new Color(0.67f, 0.26f, 0.10f),
                        ground + new Vector3(MathF.Cos(angle) * 1.9f, 3.1f + (apple % 3 - 1) * 0.6f, MathF.Sin(angle) * 1.7f)));
                }
            }
            foreach (var _ in DrunkTractor.BatchStaticSteps(yard, null)) { LoadProf.At(); yield return null; slice.Restart(); } root.AddChild(yard);
        }

        // Broken sandy openings under the eastern groves: large shapes, not a uniform grass strip.
        var ridge = new Node3D { Name = "Ridge clearings" }; pending.Add(ridge);
        foreach (var p in new[] { new Vector2(316, 101), new Vector2(332, 161), new Vector2(316, 220) })
            foreach (var _ in PatchSteps(ridge, t, p, new Vector2(7, 10), new Color(0.39f, 0.39f, 0.23f), 11)) { LoadProf.At(); yield return null; slice.Restart(); }
        foreach (var _ in DrunkTractor.BatchStaticSteps(ridge, null)) { LoadProf.At(); yield return null; slice.Restart(); } root.AddChild(ridge);
        LoadProf.At(); yield return null; slice.Restart();

        var banks = new Node3D { Name = "Lower river meadow" }; pending.Add(banks);
        var rng = new RandomNumberGenerator { Seed = 192208UL };
        if (t.Water != null)
        foreach (var river in t.Water.Rivers)
        for (int seg = 0; seg + 1 < river.Points.Length; seg++)
        {
            Vector2 a = new(river.Points[seg][0], river.Points[seg][1]);
            Vector2 b = new(river.Points[seg + 1][0], river.Points[seg + 1][1]);
            // Scatter the existing river-meadow kit along the whole authored river.
            Vector2 normal = new(-(b - a).Normalized().Y, (b - a).Normalized().X);
            for (float u = 0; u < 1; u += 0.5f)
            foreach (float side in new[] { -1f, 1f })
            {
                if (slice.ElapsedMilliseconds > 10) { LoadProf.At(); yield return null; slice.Restart(); }
                Vector2 mid = a.Lerp(b, u);
                if (a.Y < 275 && Noise.Hash01((int)(mid.X*3), (int)(mid.Y*3)+(side>0?19:0), 192208) < .38f) continue;
                Vector2 p = mid + normal * side * (river.HalfWidth(seg, u) + 2.4f);
                for (int step=0; step<4 && river.EdgeDistance(p)<.8f; step++) p += normal * side * .6f;
                if (river.EdgeDistance(p)<.8f || WorldBackdrop.SurfaceHeightAt(t,p)<t.WaterLevel+.04f) continue;
                int ci = t.NearestCenter(p, out float distance);
                if (distance < t.HalfWidth[ci] + 6) continue;
                foreach (var _ in PatchSteps(banks, t, p + normal * side * 1.5f, new Vector2(3.5f, 4.5f), new Color(0.28f, 0.36f, 0.17f), 7)) { LoadProf.At(); yield return null; slice.Restart(); }
                for (int k = 0; k < 7; k++)
                {
                    Vector2 q = p + new Vector2(rng.RandfRange(-1.5f, 1.5f), rng.RandfRange(-1.5f, 1.5f));
                    int qi=t.NearestCenter(q,out float qd);
                    if (qd<t.HalfWidth[qi]+4 || river.EdgeDistance(q)<.4f
                        || WorldBackdrop.SurfaceHeightAt(t,q)<t.WaterLevel+.02f) continue;
                    float height = rng.RandfRange(1.1f, 2.1f);
                    banks.AddChild(Part(Cyl(0.045f, height, 4), new Color(0.40f, 0.44f, 0.19f), Ground(q, height / 2)));
                    banks.AddChild(Part(Cyl(0.11f, 0.35f, 5), new Color(0.32f, 0.20f, 0.10f), Ground(q, height)));
                    banks.AddChild(Part(Box(0.14f, height * 0.7f, 0.035f), new Color(0.40f, 0.48f, 0.21f), Ground(q, height * 0.55f), new Vector3(0, rng.RandfRange(0, 180), 25)));
                }
            }
        }
        LoadProf.At(); yield return null; slice.Restart();
        foreach (var _ in DrunkTractor.BatchStaticSteps(banks, null)) { LoadProf.At(); yield return null; slice.Restart(); } root.AddChild(banks);
        // Match the river's dark bank vegetation to the existing pond reed groups.
        var pondBanks=new Node3D {Name="Pond shoreline meadow"};pending.Add(pondBanks);
        foreach(var site in WaterMesh.PondReedSites(t))
        {
            var outward=(site-PartyRowboat.PondCentre).Normalized();
            var centre=site+outward*2.8f;
            if(centre.Y<205 && Math.Abs(centre.X-220)<7) continue;
            if(PartyDacha.OnDrive(centre,3) || Village.NearAccessPath(centre,PartyDacha.Walk,6)) continue;
            foreach(var _ in PatchSteps(pondBanks,t,centre,new Vector2(3.5f,4.5f),new Color(.20f,.29f,.12f),7,true))
                {LoadProf.At();yield return null;slice.Restart();}
        }
        foreach(var _ in DrunkTractor.BatchStaticSteps(pondBanks,null))
            {LoadProf.At();yield return null;slice.Restart();}
        root.AddChild(pondBanks);
        handedOver = true;
        yield return root;
        }
        finally
        {
            if (!handedOver)
            {
                foreach (var n in pending) if (GodotObject.IsInstanceValid(n) && n.GetParent() == null) n.Free();
                root.Free();
            }
        }
    }

    // Ground-following polygons stay purely visual: driving heights and surface types are untouched.
    static void Patch(Node3D root, TrackData t, Vector2 centre, Vector2 radius, Color colour, int sides)
    {
        foreach (var _ in PatchSteps(root, t, centre, radius, colour, sides)) { }
    }

    /// <summary>The same patch in slices: every item is a point where a frame may go by.</summary>
    static IEnumerable<int> PatchSteps(Node3D root, TrackData t, Vector2 centre, Vector2 radius, Color colour, int sides, bool levelPondBank=false)
    {
        var slice = System.Diagnostics.Stopwatch.StartNew();
        var mesh = new SurfaceTool();
        mesh.Begin(Mesh.PrimitiveType.Triangles);
        Vector3 Ground(Vector2 p) => new(p.X, WorldBackdrop.SurfaceHeightAt(t, p) + (levelPondBank ? .07f : .035f), p.Y);
        void Tri(Vector2 a, Vector2 b, Vector2 c)
        {
            if (t.Water != null)
                foreach (var river in t.Water.Rivers)
                    if (river.EdgeDistance(a) < 0.2f || river.EdgeDistance(b) < 0.2f || river.EdgeDistance(c) < 0.2f) return;
            if(t.Water!=null)foreach(var pond in t.Water.Ponds)
            {
                var centre=new Vector2(pond.X,pond.Z);
                if(a.DistanceTo(centre)<pond.R+.2f || b.DistanceTo(centre)<pond.R+.2f || c.DistanceTo(centre)<pond.R+.2f) return;
            }
            if (!float.IsNaN(t.WaterLevel) && (WorldBackdrop.SurfaceHeightAt(t, a) < t.WaterLevel-(levelPondBank ? .08f : 0)
                || WorldBackdrop.SurfaceHeightAt(t, b) < t.WaterLevel-(levelPondBank ? .08f : 0) || WorldBackdrop.SurfaceHeightAt(t, c) < t.WaterLevel-(levelPondBank ? .08f : 0))) return;
            Vector2 mid = (a + b + c) / 3;
            float tint = 0.76f + Noise.Hash01((int)(mid.X * 4), (int)(mid.Y * 4), 728) * 0.42f;
            mesh.SetColor(new Color(tint, tint, tint));
            mesh.SetNormal(Vector3.Up);
            mesh.AddVertex(Ground(a)); mesh.AddVertex(Ground(b)); mesh.AddVertex(Ground(c));
        }
        for (int i = 0; i < sides; i++)
        {
            float a = i * MathF.Tau / sides + MathF.PI / 4, b = (i + 1) * MathF.Tau / sides + MathF.PI / 4;
            float Radius(int index) => sides == 4 ? 1 : 0.68f + Noise.Hash01((int)centre.X + index % sides, (int)centre.Y, 729) * 0.38f;
            Vector2 A = centre + new Vector2(MathF.Cos(a) * radius.X, MathF.Sin(a) * radius.Y) * Radius(i);
            Vector2 B = centre + new Vector2(MathF.Cos(b) * radius.X, MathF.Sin(b) * radius.Y) * Radius(i + 1);
            int steps = Math.Max(1, (int)MathF.Ceiling(A.DistanceTo(B) / 0.8f));
            int rings = Math.Max(1, (int)MathF.Ceiling(Math.Max(radius.X, radius.Y) / 0.8f));
            for (int k = 0; k < steps; k++)
            {
            if (slice.ElapsedMilliseconds > 10) { yield return 0; slice.Restart(); }
            for (int ring = 0; ring < rings; ring++)
            {
                Vector2 left = A.Lerp(B, k / (float)steps), right = A.Lerp(B, (k + 1f) / steps);
                Vector2 a0 = centre.Lerp(left, ring / (float)rings), a1 = centre.Lerp(left, (ring + 1f) / rings);
                Vector2 b0 = centre.Lerp(right, ring / (float)rings), b1 = centre.Lerp(right, (ring + 1f) / rings);
                Tri(a0, b1, a1);
                if (ring > 0) Tri(a0, b0, b1);
            }
            }
        }
        mesh.SetMaterial(Mat(colour));
        root.AddChild(new MeshInstance3D { Mesh = mesh.Commit(), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
    }
}
