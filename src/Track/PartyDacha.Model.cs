using System;
using Godot;

namespace CCCPRacing.Track;

public static partial class PartyDacha
{
    static readonly Color Timber = new(0.61f, 0.37f, 0.16f), TimberLight = new(0.73f, 0.49f, 0.23f);
    static readonly Color Trim = new(0.84f, 0.77f, 0.56f), Roof = new(0.17f, 0.28f, 0.21f);
    static readonly Color Glass = new(0.19f, 0.34f, 0.38f), Red = new(0.71f, 0.08f, 0.055f);
    static readonly Color VerandaPaint = new(0.82f, 0.70f, 0.42f), RailingPaint = new(0.93f, 0.96f, 0.92f);

    public static Node3D Building(TrackData ground=null, Vector3 origin=default, float yaw=0)
    {
        var root = new Node3D { Name = "PartyLogPalace" };
        void Box(Vector3 size, Vector3 at, Color color, Vector3 rotation = default) => root.AddChild(Props.Part(Props.Box(size.X, size.Y, size.Z), color, at, rotation));
        void Log(float length, Vector3 at, bool alongX, Color color) => root.AddChild(Props.Part(Props.Cyl(0.23f, length, 10), color, at, alongX ? new Vector3(0, 0, 90) : new Vector3(90, 0, 0)));
        var lighting = new Node3D { Name = "DachaLighting" };
        root.AddChild(lighting);
        void Window(Vector3 size, Vector3 at)
        {
            var glass = NightGlow.Part(Props.Box(size.X, size.Y, size.Z), Glass, new Color(1f, 0.60f, 0.22f), 2.5f, at);
            lighting.AddChild(glass);
        }
        void PointLight(Vector3 at)
        {
            var light = new OmniLight3D
            {
                Position = at, LightColor = new Color(1f, 0.58f, 0.24f), LightEnergy = 0,
                OmniRange = 20f, OmniAttenuation = 1.15f, ShadowEnabled = false,
                LightSpecular = 0, Visible = false,
            };
            light.SetMeta("night_energy", 4f);
            lighting.AddChild(light);
        }
        // Original low plinth beneath the two log storeys.
        Box(new Vector3(18.5f, 1.0f, 12.5f), new Vector3(0, 0.5f, 0), new Color(0.44f, 0.43f, 0.36f));
        // Heavy interlocking round logs: projecting ends make the construction legible at racing distance.
        for (int row = 0; row < 17; row++)
        {
            float y = 1.2f + row * 0.40f;
            Color color = row % 3 == 0 ? TimberLight : Timber;
            foreach (float side in new[] { -1f, 1f })
            {
                Log(19.1f, new Vector3(0, y, side * 6), true, color);
                Log(13.1f, new Vector3(side * 9, y + 0.10f, 0), false, color.Darkened(0.04f));
            }
        }
        // Stone courses and corner blocks rather than a featureless grey foundation.
        for (int x = -8; x <= 8; x += 2)
        foreach (float z in new[] { -6.3f, 6.3f })
            Box(new Vector3(1.83f, 0.36f, 0.14f), new Vector3(x, 0.45f, z), new Color(0.56f, 0.53f, 0.43f));
        foreach (float z in new[] { -6.28f, 6.28f })
        foreach (float y in new[] { 2.45f, 5.65f })
        foreach (float x in new[] { -6.6f, -3.4f, 3.4f, 6.6f })
        {
            Box(new Vector3(1.6f, 1.70f, 0.10f), new Vector3(x, y, z), Trim);
            Window(new Vector3(1.30f, 1.38f, 0.13f), new Vector3(x, y, z * 1.008f));
            if (x is -3.4f or 3.4f) PointLight(new Vector3(x, y, z * 1.2f));
            Box(new Vector3(0.09f, 1.45f, 0.15f), new Vector3(x, y, z * 1.02f), Trim);
            Box(new Vector3(1.4f, 0.09f, 0.15f), new Vector3(x, y, z * 1.02f), Trim);
            Box(new Vector3(1.9f, 0.14f, 0.28f), new Vector3(x, y + 0.92f, z), TimberLight);
        }
        foreach (float side in new[] { -1f, 1f })
        foreach (float y in new[] { 2.45f, 5.65f })
        foreach (float z in side > 0 ? new[] { -4.7f, 4.7f } : new[] { -3f, 1.5f })      // (the east wall's windows stand clear of the porch, which fills its middle)
        {
            Box(new Vector3(0.12f, 1.75f, 1.75f), new Vector3(side * 9.27f, y, z), Trim);
            Window(new Vector3(0.14f, 1.40f, 1.40f), new Vector3(side * 9.35f, y, z));
            Box(new Vector3(0.15f, 1.45f, 0.08f), new Vector3(side * 9.43f, y, z), Trim);
        }
        // Matching double doors from each storey onto its deck.
        foreach(float y in new[] {2.2f,5.6f})
        {
            Box(new Vector3(2.0f,2.45f,.16f),new Vector3(0,y,6.3f),new Color(.24f,.15f,.07f));
            Box(new Vector3(.10f,2.4f,.19f),new Vector3(0,y,6.43f),Trim);
            foreach(float x in new[] {-.5f,.5f})
                Box(new Vector3(.73f,.88f,.07f),new Vector3(x,y+.5f,6.45f),Glass);
        }
        // Ground-connected stone footings support the deck corners and load-bearing posts.
        void Footing(float x,float z)
        {
            float bottom=-.08f;
            if(ground!=null)
            {
                bottom=float.MaxValue;
                foreach(float dx in new[] {-.34f,.34f})foreach(float dz in new[] {-.34f,.34f})
                {
                    var at=origin+new Basis(Vector3.Up,yaw)*new Vector3(x+dx,0,z+dz);
                    bottom=Math.Min(bottom,ground.MeshHeight(at.X,at.Z)-origin.Y-.08f);
                }
            }
            const float top=.885f;
            Box(new Vector3(.68f,Math.Max(.1f,top-bottom),.68f),new Vector3(x,(top+bottom)*.5f,z),new Color(.44f,.43f,.36f));
        }
        foreach(float x in new[] {-9.3f,-5.7f,-2.15f,2.15f,5.7f,9.3f}) Footing(x,9.15f);
        foreach(float x in new[] {-8.8f,8.8f}) Footing(x,6.4f);
        // Wide veranda and an upper balcony, carried by carved-looking timber pillars.
        foreach (float y in new[] { 1.0f, 4.4f })
        {
            Box(new Vector3(19.8f, 0.23f, 4.0f), new Vector3(0, y, 7.8f), VerandaPaint);
            if (y > 1)
                Box(new Vector3(19.8f, 0.20f, 0.20f), new Vector3(0, y + 1.0f, 9.65f), Red);
            else
                foreach (float side in new[] { -1f, 1f })
                    Box(new Vector3(8.2f, 0.20f, 0.20f), new Vector3(side * 5.8f, y + 1.0f, 9.65f), Red);
            for (float x = -9.5f; x <= 9.5f; x += 0.8f)
                if (y > 1 || Math.Abs(x) > 1.7f) Box(new Vector3(0.13f, 0.90f, 0.13f), new Vector3(x, y + 0.50f, 9.65f), RailingPaint);
            // The open ends: the same railing from the log wall to the front.
            foreach (float side in new[] { -1f, 1f })
            {
                Box(new Vector3(0.20f, 0.20f, 3.55f), new Vector3(side * 9.75f, y + 1.0f, 7.975f), Red);
                for (float z = 6.6f; z < 9.5f; z += 0.8f)
                    Box(new Vector3(0.13f, 0.90f, 0.13f), new Vector3(side * 9.75f, y + 0.50f, z), RailingPaint);
            }
        }
        // Outer pillars end underneath the balcony deck: no free-standing stumps or caps upstairs.
        foreach (float x in new[] { -9.3f, -5.7f, 5.7f, 9.3f })
        {
            Box(new Vector3(.33f,3.17f,.33f),new Vector3(x,2.7f,9.15f),TimberLight);
            foreach (float y in new[] { 1.205f, 4.195f }) Box(new Vector3(0.54f, 0.18f, 0.54f), new Vector3(x, y, 9.15f), Trim);
        }
        for (int i = 0; i < 4; i++)
            Box(new Vector3(3.5f, 0.25f * (4 - i), 0.6f), new Vector3(0, 0.125f * (4 - i), 9.5f + i * 0.55f), new Color(0.56f, 0.53f, 0.43f));
        // Deep pitched metal roof with individual standing seams.
        foreach (float side in new[] { -1f, 1f })
        {
            Box(new Vector3(20.4f, 0.22f, 8.05f), new Vector3(0, 9.25f, side * 3.5f), Roof, new Vector3(side * 25f, 0, 0));
            for (float x = -9.8f; x <= 9.8f; x += 1.15f)
                Box(new Vector3(0.045f, 0.09f, 8.05f), new Vector3(x, 9.40f, side * 3.5f), Roof.Lightened(0.08f), new Vector3(side * 25f, 0, 0));
        }
        Box(new Vector3(20.5f, 0.18f, 0.25f), new Vector3(0, 10.97f, 0), Roof.Lightened(0.12f));
        // Timber-filled gable ends, narrowing towards the ridge.
        foreach (float side in new[] { -1f, 1f })
            for (int i = 0; i < 8; i++) Log(10.5f - i * 1.4f, new Vector3(side * 9, 7.95f + i * 0.33f, 0), false, TimberLight);
        // Central ceremonial pediment, facing the pond, carries a large red star.
        Box(new Vector3(4.6f, 0.20f, 3.7f), new Vector3(0, 7.65f, 7.6f), Trim);
        foreach (float side in new[] { -1f, 1f })
            Box(new Vector3(2.75f, 0.2f, 3.8f), new Vector3(side * 1.05f, 8.35f, 7.6f), Roof, new Vector3(0, 0, side * -30));
        // Central posts carry the pediment through the balcony down to the terrace deck.
        foreach (float side in new[] { -1f, 1f })
        {
            Box(new Vector3(0.28f, 6.44f, 0.28f), new Vector3(side * 2.15f, 4.33f, 9.15f), RailingPaint);
            Box(new Vector3(0.18f, 1.0f, 0.18f), new Vector3(side * 1.8f, 7.0f, 9.15f), Trim,
                new Vector3(0, 0, side * 45));
        }
        Box(new Vector3(4.6f, 0.22f, 0.32f), new Vector3(0, 7.44f, 9.15f), Trim);
        root.AddChild(Star(new Vector3(0, 8.3f, 9.55f), 0.85f));
        foreach (float x in new[] { -5.6f, 5.6f })
        {
            Box(new Vector3(0.85f, 2.4f, 0.85f), new Vector3(x, 10.7f, -1.6f), new Color(0.49f, 0.27f, 0.18f));
            Box(new Vector3(1.05f, 0.18f, 1.05f), new Vector3(x, 11.95f, -1.6f), new Color(0.35f, 0.36f, 0.32f));
        }
        // Flags mounted at the veranda corners remain readable without any lettering.
        foreach (float x in new[] { -8.5f, 8.5f })
        {
            Box(new Vector3(.06f,4.535f,.06f),new Vector3(x,6.7825f,9.3f),Trim);
            Box(new Vector3(1.0f, 0.65f, 0.06f), new Vector3(x + 0.5f, 8.65f, 9.3f), Red);
        }
        // A table and two pale garden chairs on the veranda.
        Box(new Vector3(1.3f, 0.10f, 0.9f), new Vector3(-6.8f, 1.85f, 7.6f), Trim);
        foreach (float x in new[] { -7.3f, -6.3f }) Box(new Vector3(0.09f, 0.8f, 0.09f), new Vector3(x, 1.45f, 7.6f), Trim);
        foreach (float x in new[] { -8.2f, -5.3f })
        {
            Box(new Vector3(0.65f, 0.1f, 0.65f), new Vector3(x, 1.5f, 7.6f), Trim);
            Box(new Vector3(0.65f, 0.70f, 0.10f), new Vector3(x, 1.9f, 7.9f), Trim);
        }
        // Preserve emission materials and live lights when merging the opaque building.
        foreach (float x in new[] { -8f, 0f, 8f })
        {
            var at = new Vector3(x, 7.05f, 6.55f);
            Box(new Vector3(.55f,.42f,.07f),new Vector3(x,7.05f,6.29f),new Color(.24f,.20f,.13f));
            Box(new Vector3(.12f,.12f,.30f),new Vector3(x,7.05f,6.37f),Trim);
            lighting.AddChild(NightGlow.Part(Props.Box(0.45f, 0.3f, 0.3f), Trim,
                new Color(1f, 0.82f, 0.5f), 4f, at));
            PointLight(at + Vector3.Back * 1.5f);
        }
        PointLight(new Vector3(-11f, 5f, 0));
        PointLight(new Vector3(11f, 5f, 0));
        // ---- The east entrance porch (the side facing the court): a stepped, columned portico with a gabled roof, a pediment with the star, double glazed doors, a runner, railings, lanterns and urns.
        {
            void PB(Vector3 size, object p1, object p2, Vector3 rot = default) { if (p1 is Color c1 && p2 is Vector3 v2) Box(size, v2, c1, rot); else if (p1 is Vector3 v1 && p2 is Color c2) Box(size, v1, c2, rot); }
            float z0 = 0f, deck = 1.0f, wallX = 9.5f, frontX = 13.0f;
            var stone = new Color(0.8f, 0.78f, 0.72f); var stoneDark = new Color(0.55f, 0.53f, 0.49f); var white = new Color(0.94f, 0.92f, 0.86f); var iron = new Color(0.1f, 0.1f, 0.11f); var gold = new Color(0.88f, 0.72f, 0.2f);
            PB(new Vector3(4.0f, 0.22f, 6.6f), new Vector3(11.5f, deck - 0.11f, z0), stone);                                  // the deck
            PB(new Vector3(4.2f, 0.12f, 6.8f), new Vector3(11.5f, deck - 0.27f, z0), stoneDark);                              // its plinth course
            for (int k = 0; k < 4; k++)                                                                                          // four wide steps down to the court
            {
                float x = frontX + 0.25f + k * 0.5f, top = deck - 0.22f - k * 0.25f;
                PB(new Vector3(0.5f, top, 6.0f + 0.0f * k), k % 2 == 0 ? stone : stone * 0.94f, new Vector3(x, top * 0.5f, z0));
                PB(new Vector3(0.54f, 0.06f, 6.06f), stoneDark, new Vector3(x - 0.03f, top + 0.01f, z0));
            }
            foreach (float sz in new[] { -1f, 1f })                                                                              // cheek walls and urn plinths flanking the steps
            {
                PB(new Vector3(2.6f, 0.62f, 0.5f), stoneDark, new Vector3(14.1f, 0.31f, z0 + sz * 3.25f));
                PB(new Vector3(2.6f, 0.1f, 0.62f), stone, new Vector3(14.1f, 0.67f, z0 + sz * 3.25f));
                PB(new Vector3(0.7f, 0.5f, 0.7f), stoneDark, new Vector3(15.5f, 0.25f, z0 + sz * 3.25f));
                root.AddChild(Props.Part(Props.Cyl(0.42f, 0.7f, 12, 0.3f), white, new Vector3(15.5f, 0.85f, z0 + sz * 3.25f)));
                root.AddChild(Props.Part(Props.Cyl(0.46f, 0.1f, 12), white, new Vector3(15.5f, 1.25f, z0 + sz * 3.25f)));
                for (int k = 0; k < 9; k++) root.AddChild(Props.Part(Props.Ball(0.14f, 5), k % 2 == 0 ? Red : new Color(0.95f, 0.78f, 0.15f), new Vector3(15.5f + ((k % 3) - 1) * 0.18f, 1.38f + (k / 3) * 0.06f, z0 + sz * 3.25f + ((k / 3) - 1) * 0.18f)));
            }
            PB(new Vector3(3.3f, 0.025f, 1.4f), new Vector3(11.7f, deck + 0.015f, z0), Red);                                   // the red runner over the deck
            for (int k = 0; k < 4; k++) PB(new Vector3(0.5f, 0.02f, 1.3f), new Vector3(frontX + 0.28f + k * 0.5f, deck - 0.22f - k * 0.25f + 0.07f, z0), Red);
            // Four columns on the front line, two pilasters on the wall, a carved entablature.
            foreach (float cz in new[] { -2.85f, -1.45f, 1.45f, 2.85f })
            {
                PB(new Vector3(0.62f, 0.2f, 0.62f), new Vector3(frontX, deck + 0.1f, cz + z0 + 0.75f - 0.75f + 0f), stoneDark);
                root.AddChild(Props.Part(Props.Cyl(0.22f, 3.3f, 12, 0.19f), white, new Vector3(frontX, deck + 1.85f, cz)));
                for (float y = deck + 0.7f; y < deck + 3.0f; y += 0.55f) root.AddChild(Props.Part(Props.Cyl(0.225f, 0.04f, 12), stone * 0.9f, new Vector3(frontX, y, cz)));        // fluting rings
                PB(new Vector3(0.7f, 0.16f, 0.7f), new Vector3(frontX, deck + 3.55f, cz), stone);
                PB(new Vector3(0.52f, 0.14f, 0.52f), new Vector3(frontX, deck + 3.4f, cz), stoneDark);
            }
            foreach (float cz in new[] { -2.85f, 2.85f })
            {
                PB(new Vector3(0.34f, 3.5f, 0.34f), new Vector3(wallX + 0.17f, deck + 1.75f, cz), white);
                PB(new Vector3(0.5f, 0.16f, 0.5f), new Vector3(wallX + 0.25f, deck + 3.55f, cz), stone);
            }
            PB(new Vector3(4.2f, 0.34f, 7.4f), new Vector3(11.5f, deck + 3.8f, z0), TimberLight);                              // entablature beam over the columns
            PB(new Vector3(4.4f, 0.1f, 7.6f), new Vector3(11.5f, deck + 3.99f, z0), Trim);
            for (float bz = -2.85f; bz <= 2.86f; bz += 0.57f) PB(new Vector3(0.16f, 0.14f, 0.16f), new Vector3(frontX + 0.28f, deck + 3.55f, bz), Trim);         // dentils
            // The gabled roof and the pediment facing the court, with the star in a gilt wreath.
            root.AddChild(Props.Part(new PrismMesh { Size = new Vector3(8.2f, 1.9f, 4.9f) }, Roof, new Vector3(11.5f, deck + 5.0f, z0), new Vector3(0, 90, 0)));
            root.AddChild(Props.Part(new PrismMesh { Size = new Vector3(7.6f, 1.7f, 0.18f) }, TimberLight, new Vector3(frontX + 0.55f, deck + 4.95f, z0), new Vector3(0, 90, 0)));
            root.AddChild(Props.Part(new PrismMesh { Size = new Vector3(7.7f, 1.78f, 0.1f) }, Trim, new Vector3(frontX + 0.66f, deck + 4.93f, z0), new Vector3(0, 90, 0)));
            root.AddChild(Star(new Vector3(frontX + 0.78f, deck + 4.6f, z0), 0.55f, new Vector3(0, 90, 0)));
            root.AddChild(Props.Part(Props.Cyl(0.7f, 0.05f, 16), gold, new Vector3(frontX + 0.7f, deck + 4.6f, z0), new Vector3(0, 0, 90)));
            PB(new Vector3(5.0f, 0.12f, 0.12f), new Vector3(11.5f, deck + 5.95f, z0), new Color(0.12f, 0.19f, 0.15f));
            // The doors: a carved double leaf with glazed panels, a fanlight, a stone surround, a brass handle and the lanterns.
            PB(new Vector3(0.22f, 2.9f, 2.9f), new Vector3(wallX - 0.04f, deck + 1.55f, z0), stone);
            PB(new Vector3(0.16f, 2.5f, 2.3f), new Vector3(wallX + 0.1f, deck + 1.35f, z0), new Color(0.2f, 0.12f, 0.06f));
            foreach (float dz in new[] { -0.57f, 0.57f })
            {
                lighting.AddChild(NightGlow.Part(Props.Box(0.12f, 1.1f, 0.78f), Glass, new Color(1f, 0.62f, 0.25f), 2.5f, new Vector3(wallX + 0.2f, deck + 1.75f, z0 + dz)));
                PB(new Vector3(0.1f, 0.7f, 0.82f), new Color(0.3f, 0.19f, 0.1f), new Vector3(wallX + 0.19f, deck + 0.7f, z0 + dz));
            }
            lighting.AddChild(NightGlow.Part(Props.Box(0.12f, 0.42f, 2.2f), Glass, new Color(1f, 0.62f, 0.25f), 2.5f, new Vector3(wallX + 0.2f, deck + 2.85f, z0)));      // the fanlight over the doors
            foreach (float dz in new[] { -0.15f, 0.15f }) PB(new Vector3(0.06f, 0.5f, 0.05f), gold, new Vector3(wallX + 0.28f, deck + 1.2f, z0 + dz));
            foreach (float lz in new[] { -1.45f, 1.45f })
            {
                PB(new Vector3(0.06f, 0.4f, 0.06f), iron, new Vector3(frontX - 0.35f, deck + 3.2f, lz), new Vector3(0, 0, 0));
                lighting.AddChild(NightGlow.Part(Props.Ball(0.2f), new Color(0.92f, 0.86f, 0.62f), new Color(1f, 0.8f, 0.42f), 3f, new Vector3(frontX - 0.35f, deck + 2.85f, lz)));
                PointLight(new Vector3(frontX - 0.35f, deck + 2.85f, lz));
            }
            // Balustrades along both sides of the deck: posts, a rail and turned balusters.
            foreach (float sz in new[] { -1f, 1f })
            {
                float bz = z0 + sz * 3.1f;
                PB(new Vector3(3.7f, 0.08f, 0.1f), RailingPaint, new Vector3(11.35f, deck + 1.0f, bz));
                PB(new Vector3(3.7f, 0.06f, 0.1f), RailingPaint, new Vector3(11.35f, deck + 0.28f, bz));
                for (float bx = 9.7f; bx <= 13.01f; bx += 0.3f) PB(new Vector3(0.07f, 0.7f, 0.07f), RailingPaint, new Vector3(bx, deck + 0.64f, bz));
            }
            // The roof's carved bargeboards and a lantern bracket complete the gable.
        }
        DrunkTractor.BatchStatic(root, lighting);
        return root;
    }

    static MeshInstance3D Star(Vector3 at, float radius, Vector3 rotation = default)
    {
        var st = new SurfaceTool(); st.Begin(Mesh.PrimitiveType.Triangles);
        for (int i = 0; i < 10; i++)
        {
            Vector3 Point(int j)
            {
                float a = j * MathF.PI / 5f;
                float r = j % 2 == 0 ? radius : radius * 0.43f;
                return new Vector3(MathF.Sin(a) * r, MathF.Cos(a) * r, 0);
            }
            st.SetNormal(Vector3.Back); st.AddVertex(Vector3.Zero); st.AddVertex(Point(i)); st.AddVertex(Point(i + 1));
        }
        return Props.Part(st.Commit(), Red, at, rotation);
    }
}
