using System;
using Godot;

namespace CCCPRacing.Track;

public static partial class Village
{
    /// <summary>Depth of the kolkhoz dormitory (local Z); its obstacles follow it.</summary>
    public const float DormDepth = 9f;
    static readonly Color DormPlinth = new(0.5f, 0.49f, 0.46f);
    static readonly Color DormGlass = new(0.13f, 0.15f, 0.17f);
    static readonly Color DormBrick = new(0.45f, 0.2f, 0.14f);

    /// <summary>Grade the barrack's footprint to its road-side slope instead of raising it above the hillside.</summary>
    public static void GradeDormitory(TrackData t, Vector2 origin, float rot, float length)
    {
        var ax = new Vector2(MathF.Cos(rot), -MathF.Sin(rot));
        var az = new Vector2(MathF.Sin(rot), MathF.Cos(rot));
        Vector2 front = origin + az * (DormDepth / 2 + 2f);
        float start = t.MeshHeight(front.X, front.Y);
        Vector2 end = front + ax * length;
        float slope = (t.MeshHeight(end.X, end.Y) - start) / length;
        int nv = t.N + 1;
        for (int iz = 0; iz < nv; iz++)
        for (int ix = 0; ix < nv; ix++)
        {
            Vector2 world = new(ix * t.Tile, iz * t.Tile), delta = world - origin;
            float x = delta.Dot(ax), z = delta.Dot(az);
            float dx = MathF.Max(0, MathF.Max(-1f - x, x - length - 1f));
            float dz = MathF.Max(0, MathF.Max(-DormDepth / 2 - 1f - z, z - DormDepth / 2 - 3.5f));
            float distance = MathF.Sqrt(dx * dx + dz * dz);
            if (distance >= 4f) continue;
            int road = t.NearestCenter(world, out float roadDistance);
            if (roadDistance < t.HalfWidth[road] + 1.5f) continue;
            float weight = 1f - Mathf.SmoothStep(0, 1, distance / 4f);
            t.Heights[iz * nv + ix] = Mathf.Lerp(t.Heights[iz * nv + ix], start + slope * x, weight);
        }
    }

    /// <summary>
    /// The kolkhoz dormitory: one massive, absurdly long barrack running from the big barn's side along
    /// local +X (length metres), in the barn's weathered red planks under a single long gable roof. Its
    /// floor rises evenly with the ground (one straight slope, never stepped) on a concrete plinth that
    /// fills down to the grass. A row of small square windows, three big double doors, ventilation cupolas
    /// and chimneys on the ridge, a red slogan banner and the name board on the road side (+Z).
    /// </summary>
    public static Node3D Dormitory(TrackData t, Vector3 origin, float rot, float length)
    {
        var n = new Node3D();
        const float H = 3.9f, D = DormDepth, Hd = D / 2, Eave = 0.6f;
        const float Ridge = H + (Hd + Eave) * 0.62f;                       // about 32 degrees
        var basis = new Basis(Vector3.Up, rot);
        var barn = t.Props.Find(p => p.Type == "bigbarn" && DormitoryJoinsBarn(p, origin, rot));
        float Ground(float lx, float lz)
        {
            Vector3 w = origin + basis * new Vector3(lx, 0, lz);
            return t.MeshHeight(w.X, w.Z) - origin.Y;
        }
        float High(float x) => MathF.Max(Ground(x, -Hd), MathF.Max(Ground(x, 0), Ground(x, Hd)));

        // The floor: a straight line from end to end, lifted until it clears the ground everywhere.
        float slope = (High(length) - High(0)) / length, lift = float.MinValue;
        for (float x = 0; x <= length + 0.01f; x += 2f) lift = MathF.Max(lift, High(x) - slope * x);
        lift += 0.35f;
        float Floor(float x) => lift + slope * x;

        // The plinth, in 4 m blocks from the floor down below the lowest ground.
        for (float x = 0; x < length - 0.01f; x += 4f)
        {
            float w = MathF.Min(4f, length - x), cx = x + w / 2;
            float lo = MathF.Min(MathF.Min(Ground(x, -Hd), Ground(x + w, -Hd)), MathF.Min(Ground(x, Hd), Ground(x + w, Hd))) - 0.6f;
            float top = MathF.Max(Floor(x), Floor(x + w)) + 0.05f;
            n.AddChild(Props.Part(Props.Box(w + 0.02f, top - lo, D + 0.4f), DormPlinth, new Vector3(cx, (top + lo) / 2, 0)));
        }

        // Everything above the plinth is built level in a body tilted to the floor's slope.
        var body = new Node3D { Position = new Vector3(0, lift, 0), Rotation = new Vector3(0, 0, MathF.Atan(slope)) };
        n.AddChild(body);
        float len = length * MathF.Sqrt(1 + slope * slope);
        void B(float x, float y, float z, Color c, Vector3 at, Vector3? r = null) => body.AddChild(Props.Part(Props.Box(x, y, z), c, at, r));
        // Small details (seams, windows, posts, doors) merged into one mesh: hundreds of boxes, one draw.
        var detail = new Node3D();
        body.AddChild(detail);
        void Dt(float x, float y, float z, Color c, Vector3 at) => detail.AddChild(Props.Part(Props.Box(x, y, z), c, at));

        B(len, H, D, BigWall, new Vector3(len / 2, H / 2, 0));
        body.AddChild(Props.Part(new PrismMesh { Size = new Vector3(D, Ridge - H, len) }, BigWall, new Vector3(len / 2, H + (Ridge - H) / 2, 0), new Vector3(0, 90, 0)));
        float roofA = Mathf.RadToDeg(MathF.Atan2(Ridge - H, Hd + Eave));
        float roofLen = MathF.Sqrt((Hd + Eave) * (Hd + Eave) + (Ridge - H) * (Ridge - H)) + 0.12f;
        foreach (float side in new[] { -1f, 1f })
        {
            if (barn == null)
                B(len + 0.8f, 0.2f, roofLen, BigRoof, new Vector3(len / 2, (H + Ridge) / 2 + 0.12f, side * (Hd + Eave) / 2), new Vector3(side * roofA, 0, 0));
            B(len + 0.8f, 0.22f, 0.2f, BigTrim, new Vector3(len / 2, H - 0.12f, side * (Hd + Eave - 0.05f)));
            B(len - 0.2f, 0.35f, 0.1f, BigTrim, new Vector3(len / 2, 0.18f, side * (Hd + 0.03f)));
            // Vertical plank seams over the long walls.
            for (float x = 0.9f; x < len - 0.5f; x += 1.2f)
                Dt(0.08f, H - 0.5f, 0.08f, BigPlank, new Vector3(x, H / 2 + 0.1f, side * (Hd + 0.03f)));
        }
        if (barn == null)
            B(len + 1f, 0.24f, 0.45f, BigRoof.Darkened(0.3f), new Vector3(len / 2, Ridge + 0.12f, 0));
        else
            n.AddChild(DormitoryRoofJoin(basis.Inverse() * (barn.Position - origin), length, lift, slope, Ridge));
        // Dark posts at the corners and every 12 m: the frame of one long building.
        for (float x = 0.12f; x <= len; x += (len - 0.24f) / MathF.Round(len / 12f))
            foreach (float z in new[] { -Hd, Hd })
                Dt(0.3f, H, 0.3f, BigTrim, new Vector3(x, H / 2, z));
        // The far gable: a small loft hatch.
        B(0.1f, 1.2f, 1.4f, BigTrim, new Vector3(len + 0.05f, H + 0.9f, 0));
        B(0.12f, 1f, 1.2f, BigDoor, new Vector3(len + 0.07f, H + 0.9f, 0));

        // Three entrances with level concrete landings and individual treads, built outside the tilted body.
        float[] doors = { len * 0.2f, len * 0.5f, len * 0.8f };
        var approaches = new System.Collections.Generic.List<Vector2[]>();
        var porches = new Node3D();
        n.AddChild(porches);
        for (int entrance = 0; entrance < doors.Length; entrance++)
        {
            float dx = doors[entrance];
            B(3.4f, 3.25f, 0.14f, BigTrim, new Vector3(dx, 1.62f, Hd + 0.06f));
            foreach (float px in new[] { -0.75f, 0.75f })
            {
                B(1.45f, 3f, 0.16f, BigDoor, new Vector3(dx + px, 1.5f, Hd + 0.1f));
                for (float sx = -0.5f; sx <= 0.51f; sx += 0.5f)
                    Dt(0.06f, 2.9f, 0.05f, BigPlank, new Vector3(dx + px + sx, 1.5f, Hd + 0.2f));
                Dt(0.08f, 0.30f, 0.10f, DormPlinth, new Vector3(dx + MathF.Sign(px) * 0.18f, 1.35f, Hd + 0.23f));
                foreach (float hy in new[] { 0.45f, 2.5f })
                    Dt(0.22f, 0.10f, 0.08f, BigTrim, new Vector3(dx + MathF.Sign(px) * 1.4f, hy, Hd + 0.23f));
            }
            B(4.2f, 0.16f, 1.5f, BigRoof, new Vector3(dx, 3.55f, Hd + 0.8f), new Vector3(-10, 0, 0));    // a canopy
            var lampAt = new Vector3(dx, 3.36f, Hd + 1.38f);
            body.AddChild(NightGlow.Part(Props.Box(0.36f, 0.12f, 0.25f), DormPlinth,
                new Color(1f, 0.76f, 0.43f), 1.5f, lampAt));
            var lamp = new OmniLight3D { Position = lampAt + new Vector3(0, -0.12f, 0.35f),
                LightColor = new Color(1f, 0.75f, 0.44f), OmniRange = 8f, ShadowEnabled = false, LightEnergy = 0f };
            lamp.SetMeta("night_energy", 1.45f);
            body.AddChild(lamp);
            Dt(4.2f, 0.40f, 0.12f, BigTrim, new Vector3(dx, 3.64f, Hd + 1.55f));
            Dt(3.2f, 0.08f, 0.30f, DormPlinth, new Vector3(dx, 0.04f, Hd + 0.20f));
            body.AddChild(Buildings.TextPanel($"{entrance + 1}", 0.35f, 0.42f,
                new Vector3(dx + 1.9f, 2.2f, Hd + 0.13f), BigTrim, SignCream));
            foreach (float side in new[] { -1f, 1f })
                Dt(0.10f, 0.60f, 0.10f, BigTrim, new Vector3(dx + side * 1.85f, 3.2f, Hd + 1.25f));

            float doorX = dx / MathF.Sqrt(1 + slope * slope), topY = Floor(doorX) + 0.02f;
            float groundY = Ground(doorX, Hd + 2.3f), rise = MathF.Max(0.1f, topY - groundY);
            int risers = Math.Max(2, (int)MathF.Ceiling(rise / 0.17f));
            void PorchBox(float width, float depth, float z, float top, Color color)
            {
                float bottom = MathF.Min(Ground(doorX - width / 2, z), Ground(doorX + width / 2, z)) - 0.15f;
                if (top <= bottom) return;
                porches.AddChild(Props.Part(Props.Box(width, top - bottom, depth), color,
                    new Vector3(doorX, (top + bottom) / 2, z)));
            }
            PorchBox(3.6f, 1.35f, Hd + 0.75f, topY, DormPlinth);
            for (int i = 1; i < risers; i++)
                PorchBox(3.6f, 0.34f, Hd + 1.425f + (i - 0.5f) * 0.32f,
                    topY - rise * i / risers, i % 2 == 0 ? DormPlinth : DormPlinth.Lightened(0.06f));
            if (t.Id == "ilyich")
            {
                float footZ = Hd + 1.425f + (risers - 1) * 0.32f;
                Vector3 foot = origin + basis * new Vector3(doorX, 0, footZ);
                Vector2 at = new(foot.X, foot.Z);
                int road = t.NearestCenter(at, out _);
                // The two left entrances stop a tile short of the road, so the earth does not run into its edge.
                Vector2 end = t.Center[road];
                if (entrance < 2) end += (at - end).Normalized() * (t.HalfWidth[road] + t.Tile);
                approaches.Add(new[] { at, at + new Vector2(basis.Z.X, basis.Z.Z) * 2f, end });
            }
            foreach (float side in new[] { -1f, 1f })
            {
                float railX = doorX + side * 1.72f;
                float farZ = Hd + 1.425f + (risers - 1) * 0.32f;
                Vector3 a = new(railX, topY + 0.88f, Hd + 0.2f);
                Vector3 b = new(railX, topY + 0.88f, Hd + 1.425f);
                Vector3 c = new(railX, groundY + rise / risers + 0.88f, farZ);
                void Rail(Vector3 from, Vector3 to)
                {
                    Vector3 delta = to - from;
                    porches.AddChild(Props.Part(Props.Box(0.07f, 0.07f, delta.Length()), BigTrim,
                        (from + to) / 2, new Vector3(-Mathf.RadToDeg(MathF.Atan2(delta.Y, delta.Z)), 0, 0)));
                }
                Rail(a, b); Rail(b, c);
                foreach (var post in new[] { a, b, c })
                    porches.AddChild(Props.Part(Props.Box(0.07f, 0.88f, 0.07f), BigTrim, post - Vector3.Up * 0.44f));
            }
        }
        body.AddChild(Buildings.TextPanel("ОБЩЕЖИТИЕ КОЛХОЗА", 4.1f, 0.4f, new Vector3(doors[1], 3.64f, Hd + 1.63f), Red, SignCream));
        foreach (float sx in new[] { (doors[0] + doors[1]) / 2, (doors[1] + doors[2]) / 2 })
            body.AddChild(Buildings.TextPanel(sx < doors[1] ? "СЛАВА ТРУДУ!" : "ПЯТИЛЕТКУ В 4 ГОДА!", 9f, 0.9f, new Vector3(sx, H - 0.75f, Hd + 0.12f), Red, SignCream));

        // A row of small square windows high on both long walls, clear of the doors and banners.
        foreach (float side in new[] { -1f, 1f })
            for (float x = 2f; x < len - 1.5f; x += 3f)
            {
                bool nearDoor = false;
                foreach (float dx in doors) nearDoor |= MathF.Abs(x - dx) < 2.6f;
                if (side > 0 && nearDoor) continue;
                Dt(1.1f, 1.1f, 0.12f, BigTrim, new Vector3(x, 2.05f, side * (Hd + 0.06f)));
                var pane = Props.Box(0.85f, 0.85f, 0.14f);
                if (((int)x + (side > 0 ? 1 : 0)) % 4 == 0)
                    body.AddChild(NightGlow.Part(pane, DormGlass, new Color(1f, 0.73f, 0.38f), 0.8f,
                        new Vector3(x, 2.05f, side * (Hd + 0.08f))));
                else Dt(0.85f, 0.85f, 0.14f, DormGlass, new Vector3(x, 2.05f, side * (Hd + 0.08f)));
                Dt(0.07f, 0.85f, 0.16f, BigTrim, new Vector3(x, 2.05f, side * (Hd + 0.1f)));
                Dt(0.85f, 0.07f, 0.17f, SignCream.Darkened(0.2f), new Vector3(x, 2.05f, side * (Hd + 0.13f)));
                Dt(1.3f, 0.12f, 0.34f, DormPlinth, new Vector3(x, 1.45f, side * (Hd + 0.14f)));
            }

        // Gutters and downpipes break up the long facade and tie the roof to the ground.
        foreach (float side in new[] { -1f, 1f })
        {
            Dt(len + 0.6f, 0.13f, 0.16f, DormPlinth.Darkened(0.28f), new Vector3(len / 2, H - 0.08f, side * (Hd + Eave)));
            foreach (float x in new[] { 0.6f, len / 3, len * 2 / 3, len - 0.6f })
            {
                Dt(0.14f, H - 0.25f, 0.14f, DormPlinth.Darkened(0.28f), new Vector3(x, H / 2, side * (Hd + 0.24f)));
                Dt(0.14f, 0.14f, 0.55f, DormPlinth.Darkened(0.28f), new Vector3(x, 0.2f, side * (Hd + 0.45f)));
            }
        }

        // Ventilation cupolas on the ridge and brick chimneys on the back slope.
        for (int i = 0; i < 4; i++)
        {
            float x = len * (i + 0.5f) / 4;
            B(1.2f, 1.1f, 1.2f, BigWall, new Vector3(x, Ridge + 0.5f, 0));
            B(1.0f, 0.5f, 1.25f, BigTrim, new Vector3(x, Ridge + 0.55f, 0));
            body.AddChild(Props.Part(new PrismMesh { Size = new Vector3(1.8f, 0.7f, 1.6f) }, BigRoof, new Vector3(x, Ridge + 1.4f, 0), new Vector3(0, 90, 0)));
            float cx = len * (i + 1f) / 4 - len / 8 + 3f;
            B(0.8f, 2f, 0.8f, DormBrick, new Vector3(cx, Ridge + 0.1f, -2.2f));
            B(0.95f, 0.15f, 0.95f, DormPlinth, new Vector3(cx, Ridge + 1.15f, -2.2f));
        }
        if (t.Id == "ilyich")
        {
            Vector2 World(float x, float z)
            {
                Vector3 w = origin + basis * new Vector3(x, 0, z);
                return new Vector2(w.X, w.Z);
            }
            // A worn connecting walk ties the three entrances into one inhabited frontage.
            approaches.Add(new[] { World(8, Hd + 3.8f), World(length - 3, Hd + 3.8f) });
            n.AddChild(KolkhozFrontage(t, origin, basis, approaches));
        }
        DrunkTractor.BatchStatic(detail, null);
        DrunkTractor.BatchStatic(porches, null);
        return n;
    }
}
