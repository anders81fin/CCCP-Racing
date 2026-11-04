using System;
using System.Collections.Generic;
using Godot;

namespace CCCPRacing.Track;

/// <summary>
/// The labour camp the road runs through (TrackDef.Gulag, metres along the road): a gate over the
/// road at either end (two log watchtowers, a beam with the camp's slogan tilted towards the
/// driver), log barracks under snow either side of the road, a barbed-wire fence round it all with
/// a tower at each far corner. The road through the camp has no snow banks, so the yard can be
/// driven into: towers, barracks and the wire are obstacles (added to the track's list).
/// </summary>
public static class GulagCamp
{
    static readonly Color Log = new(0.3f, 0.22f, 0.15f), LogDark = new(0.22f, 0.16f, 0.11f);
    static readonly Color Snow = new(0.95f, 0.96f, 0.99f), Window = new(0.08f, 0.08f, 0.1f);
    static readonly Color Post = new(0.26f, 0.2f, 0.15f), Wire = new(0.12f, 0.12f, 0.12f);
    static readonly Color Red = new(0.72f, 0.1f, 0.08f), Gold = new(1f, 0.85f, 0.3f);
    const string Slogan = "ТРУД - ДЕЛО ЧЕСТИ";
    const float HutLat = 13f, FenceLat = 24f;

    public readonly record struct HutSite(int Offset, float Side, int Index, Vector2 Position, float Yaw);

    static Vector2 At(TrackData t, int i, float lat)
    {
        var d = t.CenterDir(i);
        return t.Center[i] + new Vector2(d.Y, -d.X) * lat;
    }

    static float Clear(TrackData t, int i) => (t.Banks?.Inner(i) ?? t.HalfWidth[i] + 1f) + Snowbanks.Width + 0.8f;

    static bool Free(TrackData t, GulagDef def, Vector2 p, float margin)
    {
        int n = t.Center.Length, from = ((int)def.From % n + n) % n;
        int len = ((int)(def.To - def.From) % n + n) % n;
        int ci = t.NearestCenter(p, out float d);
        return ((ci - from) % n + n) % n <= len || d > t.HalfWidth[ci] + Snowbanks.Width + 1f + margin;
    }

    /// <summary>The same accepted hut sites feed both geometry and buried electrical services.</summary>
    public static IEnumerable<HutSite> Huts(TrackData t, GulagDef def)
    {
        int n = t.Center.Length, len = ((int)(def.To - def.From) % n + n) % n;
        for (int k = 14; k < len - 10; k += 22)
        foreach (float s in new[] { -1f, 1f })
        {
            int i = (((int)def.From + k) % n + n) % n;
            var p = At(t, i, s * HutLat);
            if (Free(t, def, p, 6)) yield return new(k, s, i, p, TrackData.YawFor(t.CenterDir(i)));
        }
    }

    public static IEnumerable<Vector2> TowerSites(TrackData t, GulagDef def)
    {
        int n = t.Center.Length;
        foreach (int index in new[] { (int)def.From, (int)def.To })
        {
            int i = (index % n + n) % n;
            foreach (float s in new[] { -1f, 1f })
            {
                yield return At(t, i, s * Clear(t, i));
                var corner = At(t, i, s * FenceLat);
                if (Free(t, def, corner, 2)) yield return corner;
            }
        }
    }

    public static Node3D Build(TrackData t, GulagDef def, List<Obstacle> obstacles)
    {
        void Block(Vector3 p, float r) => obstacles.Add(new Obstacle(new Vector2(p.X, p.Z), r));
        var root = new Node3D { Name = "GulagCamp" };
        int n = t.Center.Length;
        int from = ((int)def.From % n + n) % n, to = ((int)def.To % n + n) % n;
        int len = ((to - from) % n + n) % n;
        int I(int k) => ((from + k) % n + n) % n;
        float Clear(int i) => GulagCamp.Clear(t, i);

        // Where a point is free of the rest of the road (not the camp's own stretch).
        bool Free(Vector2 p, float margin)
        {
            return GulagCamp.Free(t, def, p, margin);
        }
        Vector2 At(int i, float lat)
        {
            return GulagCamp.At(t, i, lat);
        }
        Vector3 Ground(Vector2 p) => new(p.X, t.MeshHeight(p.X, p.Y), p.Y);
        float Yaw(int i) => TrackData.YawFor(t.CenterDir(i));

        // The two gates.
        foreach (int i in new[] { from, to })
        {
            float half = Clear(i);
            var gate = new Node3D { Position = Ground(t.Center[i]), Rotation = new Vector3(0, Yaw(i), 0) };
            foreach (float s in new[] { -1f, 1f })
            {
                var tower = Tower(5.5f);
                Vector2 p = At(i, -s * half);          // local +x is the right of the road, At's + the left
                tower.Position = new Vector3(s * half, t.MeshHeight(p.X, p.Y) - gate.Position.Y, 0);
                gate.AddChild(tower);
                int inside = I(i == from ? 9 : len - 9);
                Vector3 aim = Ground(t.Center[inside]);
                CampSearchlight.Attach(tower, Ground(p), Yaw(i), 5.5f, aim, aim, 0);
                Block(new Vector3(p.X, 0, p.Y), 1.6f);
            }
            gate.AddChild(Props.Part(Props.Box(2f * half, 0.35f, 0.35f), LogDark, new Vector3(0, 5.6f, 0)));
            var banner = Buildings.TextPanel(Slogan, Math.Min(2f * half - 3f, 11f), 1.3f, new Vector3(0, 4.7f, 0), Red, Gold);
            banner.RotationDegrees = new Vector3(-40f, 0, 0);      // tilted up towards the driver coming in
            gate.AddChild(banner);
            root.AddChild(gate);
        }

        // Barracks either side, parallel to the road, where they fit. The first one gets a door to
        // the road and a bench by it: the camp's mechanic sleeps there (DrunkMen).
        bool post = false;
        foreach (var site in Huts(t, def))
            {
                int i = site.Index, k = site.Offset;
                float s = site.Side;
                Vector2 p = site.Position;
                var hut = Barrack(Noise.Hash01(k, (int)s, 1937), t.Id == "siberia" && site.Offset == 36 && site.Side < 0);
                hut.Position = Ground(p);
                hut.Rotation = new Vector3(0, Yaw(i), 0);
                root.AddChild(hut);
                // Its walls: a row of circles down its length (the hut's long axis is local z).
                float hl = 7.5f + 2.5f * Noise.Hash01(k, (int)s, 1937);
                Vector3 along = hut.Basis * Vector3.Back;
                for (float z = -hl + 2.5f; z <= hl - 2.5f + 0.01f; z += 2.5f) Block(hut.Position + along * z, 3.2f);
                if (!post)
                {
                    post = true;
                    // The hut faces along the road (local -Z forward, +x its right): on the left of
                    // the road (s > 0) its +x wall faces the road, on the right its -x wall.
                    float wall = s > 0 ? 1f : -1f;
                    hut.AddChild(Props.Part(Props.Box(0.08f, 1.9f, 1.1f), LogDark, new Vector3(wall * 3.03f, 0.95f, 0)));          // the door
                    hut.AddChild(Props.Part(Props.Box(0.5f, 0.1f, 2.4f), Log, new Vector3(wall * 3.4f, 0.48f, 2.2f)));           // the bench
                    foreach (float bz in new[] { 1.3f, 3.1f })
                        hut.AddChild(Props.Part(Props.Box(0.4f, 0.45f, 0.12f), LogDark, new Vector3(wall * 3.4f, 0.22f, bz)));
                    // A shelter's frame: the bench at local (0, 0, 0.35), its open side (-Z) to the road.
                    Vector3 bench = hut.Transform * new Vector3(wall * 3.4f, 0, 2.2f);
                    Vector3 toRoad = (hut.Basis * new Vector3(wall, 0, 0)).Normalized();
                    float yaw = MathF.Atan2(-toRoad.X, -toRoad.Z);                     // local -Z faces the road
                    var basis = new Basis(Vector3.Up, yaw);
                    t.MechanicPost = new Transform3D(basis, bench - basis * new Vector3(0, 0, 0.35f));
                }
            }

        // The wire: along both sides at FenceLat, and in to each gate's towers; a tower at each far corner.
        foreach (float s in new[] { -1f, 1f })
        {
            var line = new List<Vector3>();
            line.Add(Ground(At(from, s * Clear(from))));
            for (int k = 0; k <= len; k += 4) line.Add(Ground(At(I(k), s * FenceLat)));
            line.Add(Ground(At(to, s * Clear(to))));
            root.AddChild(WireFence(line, p => Free(new Vector2(p.X, p.Z), 1f)));
            // The wire stops a car: small circles every metre and a half along the kept spans.
            for (int k = 0; k + 1 < line.Count; k++)
            {
                if (!Free(new Vector2(line[k].X, line[k].Z), 1f) || !Free(new Vector2(line[k + 1].X, line[k + 1].Z), 1f)) continue;
                float L = line[k].DistanceTo(line[k + 1]);
                for (float d = 0; d < L; d += 1.5f) Block(line[k].Lerp(line[k + 1], d / L), 0.35f);
            }
            foreach (int i in new[] { from, to })
            {
                Vector2 c = At(i, s * FenceLat);
                if (!Free(c, 2f)) continue;
                var tower = Tower(6.5f);
                tower.Position = Ground(c);
                tower.Rotation = new Vector3(0, Yaw(i), 0);
                root.AddChild(tower);
                int near = I(i == from ? 11 : len - 11), far = I(i == from ? 27 : len - 27);
                CampSearchlight.Attach(tower, tower.Position, Yaw(i), 6.5f,
                    Ground(At(near, s * 20f)), Ground(At(far, s * 17f)), s > 0 ? 9f : 23f);
                Block(tower.Position, 1.6f);
            }
        }
        return root;
    }

    /// <summary>A log watchtower: four legs, a cabin with a dark window band, a snowy pyramid roof.</summary>
    static Node3D Tower(float legs)
    {
        var n = new Node3D();
        foreach (var (x, z) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
        {
            var leg = Props.Part(Props.Cyl(0.13f, legs + 0.3f, 5), Log, new Vector3(x * 1.05f, legs * 0.5f, z * 1.05f));
            leg.RotationDegrees = new Vector3(z * 3f, 0, -x * 3f);      // splayed a little
            n.AddChild(leg);
        }
        n.AddChild(Props.Part(Props.Box(2.7f, 0.2f, 2.7f), LogDark, new Vector3(0, legs, 0)));
        n.AddChild(Props.Part(Props.Box(2.5f, 1.0f, 2.5f), Log, new Vector3(0, legs + 0.6f, 0)));
        n.AddChild(Props.Part(Props.Box(2.52f, 0.45f, 2.52f), Window, new Vector3(0, legs + 1.35f, 0)));
        foreach (var (x, z) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
            n.AddChild(Props.Part(Props.Box(0.14f, 0.5f, 0.14f), Log, new Vector3(x * 1.2f, legs + 1.35f, z * 1.2f)));
        var roof = Props.Part(Props.Cyl(2.3f, 1.3f, 4, 0f), Snow, new Vector3(0, legs + 2.25f, 0));
        roof.RotationDegrees = new Vector3(0, 45f, 0);
        n.AddChild(roof);
        SiberiaModelDetails.CampTower(n, legs);
        return n;
    }

    /// <summary>A long low log barrack, snow thick on its roof, a row of small windows, a stovepipe.</summary>
    static Node3D Barrack(float h01, bool woodYard = false)
    {
        var n = new Node3D();
        float L = 15f + 5f * h01, D = 6f, H = 2.4f;
        n.AddChild(Props.Part(Props.Box(D, H, L), Log, new Vector3(0, H / 2f, 0)));
        for (int k = 0; k < 4; k++)
            n.AddChild(Props.Part(Props.Box(D + 0.08f, 0.1f, L + 0.08f), LogDark, new Vector3(0, 0.4f + k * 0.55f, 0)));
        foreach (float s in new[] { -1f, 1f })
            for (float z = -L / 2f + 2f; z < L / 2f - 1f; z += 3f)
            {
                var mesh = Props.Box(0.06f, 0.55f, 0.8f);
                var at = new Vector3(s * (D / 2f + 0.02f), 1.5f, z);
                bool warm = woodYard && s < 0 && MathF.Abs(z - (-L / 2f + 5f)) < .1f;
                n.AddChild(warm ? NightGlow.Part(mesh, Window, new Color(1f, .72f, .30f), 1.1f, at) : Props.Part(mesh, Window, at));
            }
        // The roof: a gable, then snow lying on it a hand thick and over the eaves.
        n.AddChild(Props.Part(new PrismMesh { Size = new Vector3(D + 0.8f, 1.6f, L + 0.6f) }, LogDark, new Vector3(0, H + 0.8f, 0)));
        n.AddChild(Props.Part(new PrismMesh { Size = new Vector3(D + 1.1f, 1.75f, L + 0.8f) }, Snow, new Vector3(0, H + 0.95f, 0)));
        n.AddChild(Props.Part(Props.Cyl(0.13f, 1.2f, 5), Wire, new Vector3(D * 0.2f, H + 1.9f, L * 0.25f)));
        SiberiaModelDetails.CampHut(n, L);
        return n;
    }

    /// <summary>Barbed wire on leaning posts every 4 m, four strands; a span is left out where keep says no.</summary>
    static Node3D WireFence(List<Vector3> pts, Func<Vector3, bool> keep)
    {
        var n = new Node3D { Name = "Wire" };
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Lines);
        st.SetColor(Wire);
        for (int k = 0; k < pts.Count; k++)
        {
            Vector3 p = pts[k];
            if (!keep(p)) continue;
            n.AddChild(Props.Part(Props.Cyl(0.07f, 2.6f, 4), Post, p + Vector3.Up * 1.25f));
            if (k + 1 >= pts.Count || !keep(pts[k + 1])) continue;
            Vector3 q = pts[k + 1];
            for (int w = 0; w < 4; w++)
            {
                float y = 0.5f + w * 0.6f;
                st.AddVertex(p + Vector3.Up * y);
                st.AddVertex(q + Vector3.Up * (y - 0.06f));
            }
        }
        var mat = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true };
        st.SetMaterial(mat);
        n.AddChild(new MeshInstance3D { Mesh = st.Commit(), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        return n;
    }
}
