using System;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Track;

/// <summary>Barracks, a red flag, rotting barns, dead trees and the absurd radar station.</summary>
public static class Buildings
{
    static readonly Color Concrete = new(0.6f, 0.6f, 0.57f);
    static readonly Color ConcreteDark = new(0.46f, 0.46f, 0.44f);
    static readonly Color Window = new(0.16f, 0.19f, 0.22f);
    static readonly Color WindowLit = new(0.9f, 0.78f, 0.4f);
    static readonly Color WindowGlow = new(1f, 0.78f, 0.38f);     // a window with the light on, at night
    static readonly Color Red = new(0.78f, 0.1f, 0.08f);
    static readonly Color Rotten = new(0.42f, 0.38f, 0.33f);

    /// <summary>Three storeys of grim prefab concrete, 30 × 10 m, facade (and door) towards +Z.</summary>
    public static Node3D Barracks(bool winter = false)
    {
        var n = new Node3D();
        var rng = new RandomNumberGenerator { Seed = 1968 };
        const float L = 30f, D = 10f, H = 9.3f;
        n.AddChild(Props.Part(Props.Box(L, H, D), Concrete, new Vector3(0, H / 2f, 0)));
        n.AddChild(Props.Part(Props.Box(L + 0.4f, 0.35f, D + 0.4f), ConcreteDark, new Vector3(0, H + 0.1f, 0)));   // roof edge
        // Panel joints and windows on both long sides.
        foreach (float side in new[] { -1f, 1f })
        {
            float z = side * (D / 2f + 0.02f);
            for (int floor = 0; floor < 3; floor++)
            {
                float y = 1.6f + floor * 3.0f;
                n.AddChild(Props.Part(Props.Box(L, 0.08f, 0.05f), ConcreteDark, new Vector3(0, y + 1.35f, z)));
                for (int w = 0; w < 10; w++)
                {
                    float x = -L / 2f + 1.5f + w * 3f;
                    if (side > 0 && floor == 0 && w == 5) continue;   // door goes here
                    bool lit = rng.Randf() < 0.15f;
                    bool broken = !lit && rng.Randf() < 0.1f;
                    // At night the lit ones glow and about a third of the dark ones have the light on too
                    // (picked by a hash, so the day rng and the day windows stay as they were).
                    bool night = lit || !broken && Noise.Hash01(w + (int)side * 17, floor, 1917) < 0.3f;
                    var box = Props.Box(1.4f, 1.3f, 0.06f);
                    var at = new Vector3(x, y, z);
                    n.AddChild(night ? NightGlow.Part(box, lit ? WindowLit : Window, WindowGlow, lit ? 1.3f : 0.9f, at)
                        : Props.Part(box, broken ? new Color(0.05f, 0.05f, 0.05f) : Window, at));
                }
            }
            for (int w = 0; w <= 5; w++)
                n.AddChild(Props.Part(Props.Box(0.06f, H, 0.06f), ConcreteDark, new Vector3(-L / 2f + w * 6f, H / 2f, z)));
        }
        // Entrance with a slab canopy and steps; red banner over it.
        n.AddChild(Props.Part(Props.Box(2f, 2.3f, 0.1f), new Color(0.25f, 0.18f, 0.12f), new Vector3(0, 1.15f, D / 2f + 0.03f)));
        n.AddChild(Props.Part(Props.Box(3.4f, 0.2f, 1.6f), ConcreteDark, new Vector3(0, 2.6f, D / 2f + 0.8f)));
        n.AddChild(Props.Part(Props.Box(3.4f, 0.3f, 1.2f), ConcreteDark, new Vector3(0, 0.15f, D / 2f + 0.6f)));
        n.AddChild(TextPanel("СЛАВА ВВС!", 9f, 1.3f, new Vector3(0, 7.9f, D / 2f + 0.05f), Red, new Color(1f, 0.85f, 0.3f)));
        SiberiaModelDetails.Barracks(n, winter);
        return n;
    }

    /// <summary>
    /// A flagpole: concrete footing and a stepped plinth, a tapered steel pole with a gilt ball, a halyard with its
    /// cleat, and a waving red flag in strips with a gold star, flying towards +X.
    /// </summary>
    public static Node3D FlagPole()
    {
        var n = new Node3D();
        var solid = new Node3D();
        Color steel = new(0.72f, 0.72f, 0.7f), gold = new(0.9f, 0.75f, 0.2f), concrete = ConcreteDark;
        solid.AddChild(Props.Part(Props.Box(1.1f, 0.3f, 1.1f), concrete, new Vector3(0, 0.15f, 0)));
        solid.AddChild(Props.Part(Props.Box(0.7f, 0.25f, 0.7f), Concrete, new Vector3(0, 0.425f, 0)));
        solid.AddChild(Props.Part(Props.Cyl(0.16f, 0.25f, 8, 0.1f), steel, new Vector3(0, 0.67f, 0)));    // the socket
        solid.AddChild(Props.Part(Props.Cyl(0.12f, 8.3f, 8, 0.045f), steel, new Vector3(0, 4.7f, 0)));     // tapered pole
        solid.AddChild(Props.Part(Props.Ball(0.09f, 6), gold, new Vector3(0, 8.92f, 0)));
        solid.AddChild(Props.Part(Props.Box(0.025f, 6.2f, 0.025f), new Color(0.82f, 0.8f, 0.7f), new Vector3(0.14f, 3.7f, 0.04f)));   // halyard
        solid.AddChild(Props.Part(Props.Box(0.06f, 0.14f, 0.06f), Rotten, new Vector3(0.12f, 1.2f, 0.04f)));                         // cleat
        foreach (float y in new[] { 7.6f, 8.2f })
            solid.AddChild(Props.Part(Props.Cyl(0.075f, 0.05f, 6), steel, new Vector3(0, y, 0)));          // flag rings
        // The flag: five strips along +X, each turned a little to the wind and shifted in depth so it waves.
        for (int k = 0; k < 5; k++)
        {
            float w = MathF.Sin(k * 1.35f);
            solid.AddChild(Props.Part(Props.Box(0.5f, 1.55f, 0.03f), k % 2 == 0 ? Red : Red * 0.84f,
                new Vector3(0.3f + k * 0.5f, 8.1f - k * 0.025f, w * 0.09f), new Vector3(0, w * 13f, 0)));
        }
        // The star in the corner by the pole (two squares turned 45 degrees on top of each other).
        solid.AddChild(Props.Part(Props.Box(0.34f, 0.34f, 0.04f), gold, new Vector3(0.62f, 8.55f, 0.015f), new Vector3(0, 0, 45)));
        solid.AddChild(Props.Part(Props.Box(0.34f, 0.34f, 0.04f), gold, new Vector3(0.62f, 8.55f, 0.015f)));
        DrunkTractor.BatchStatic(solid, null);
        n.AddChild(solid);
        return n;
    }

    /// <summary>A barn left to rot: bleached grey planks, leaning walls, a caved-in roof.</summary>
    public static Node3D RuinedBarn()
    {
        var n = new Node3D();
        var rng = new RandomNumberGenerator { Seed = 1937 };
        var tilt = new Node3D { RotationDegrees = new Vector3(0, 0, 4f) };   // the whole thing leans
        n.AddChild(tilt);
        // Walls from vertical planks with gaps.
        for (float x = -3.8f; x <= 3.8f; x += 0.55f)
            foreach (float z in new[] { -6.4f, 6.4f })
                if (rng.Randf() > 0.18f && !(z > 0 && MathF.Abs(x) < 1.65f))
                    tilt.AddChild(Props.Part(Props.Box(0.45f, rng.RandfRange(3.2f, 4.3f), 0.1f), Rotten * rng.RandfRange(0.85f, 1.1f), new Vector3(x, 2f, z)));
        for (float z = -6.2f; z <= 6.2f; z += 0.55f)
            foreach (float x in new[] { -4f, 4f })
                if (rng.Randf() > 0.2f)
                    tilt.AddChild(Props.Part(Props.Box(0.1f, rng.RandfRange(3.3f, 4.3f), 0.45f), Rotten * rng.RandfRange(0.85f, 1.1f), new Vector3(x, 2f, z)));
        // Roof: one side intact but sagging, the other half fallen in.
        tilt.AddChild(Props.Part(Props.Box(4.8f, 0.12f, 13.4f), new Color(0.32f, 0.3f, 0.3f), new Vector3(-2.1f, 5f, 0), new Vector3(0, 0, 32)));
        tilt.AddChild(Props.Part(Props.Box(4.8f, 0.12f, 6f), new Color(0.3f, 0.28f, 0.28f), new Vector3(2.1f, 5f, -3.6f), new Vector3(8, 0, -30)));
        tilt.AddChild(Props.Part(Props.Box(4.8f, 0.12f, 5f), new Color(0.28f, 0.26f, 0.26f), new Vector3(1.5f, 1.4f, 3.3f), new Vector3(-25, 10, -50)));
        tilt.AddChild(Props.Part(Props.Box(0.2f, 0.2f, 13.4f), new Color(0.3f, 0.24f, 0.18f), new Vector3(0, 6.3f, 0), new Vector3(0, 0, 6)));   // ridge beam
        // Rubbish: a broken cart wheel and some planks on the ground.
        n.AddChild(Props.Part(Props.Cyl(0.6f, 0.12f, 8), new Color(0.3f, 0.24f, 0.18f), new Vector3(5f, 0.1f, 2f), new Vector3(0, 0, 0)));
        n.AddChild(Props.Part(Props.Box(2.5f, 0.08f, 0.35f), Rotten, new Vector3(4.6f, 0.05f, -2f), new Vector3(0, 30, 0)));
        Color timber = new(0.27f, 0.23f, 0.18f), rust = new(0.37f, 0.22f, 0.14f);
        void Beam(Node3D parent, Vector3 a, Vector3 b, float width, Color colour)
        {
            Vector3 d = b - a;
            var part = Props.Part(Props.Box(width, width, d.Length()), colour, (a + b) * 0.5f);
            part.Basis = Basis.LookingAt(d.Normalized(), MathF.Abs(d.Normalized().Y) > 0.99f ? Vector3.Right : Vector3.Up);
            parent.AddChild(part);
        }
        // Stone footings and exposed timber framing remain visible through the missing boards.
        foreach (float x in new[] { -3.85f, 3.85f })
        {
            foreach (float z in new[] { -6.25f, -2.1f, 2.1f, 6.25f })
            {
                n.AddChild(Props.Part(Props.Box(0.75f, 0.35f, 0.7f), ConcreteDark * 0.8f,
                    new Vector3(x, 0.15f, z), new Vector3(0, rng.RandfRange(-12f, 12f), 0)));
                Beam(tilt, new Vector3(x, 0.15f, z), new Vector3(x, 4.1f, z), 0.23f, timber);
            }
            foreach (float y in new[] { 0.45f, 2.7f, 4.05f })
                Beam(tilt, new Vector3(x, y, -6.3f), new Vector3(x, y, 6.3f), 0.18f, timber);
            Beam(tilt, new Vector3(x, 0.5f, -6.1f), new Vector3(x, 3.9f, -2.2f), 0.15f, timber);
            Beam(tilt, new Vector3(x, 0.5f, 6.1f), new Vector3(x, 3.9f, 2.2f), 0.15f, timber);
            // Uneven repair boards over the original weathered siding.
            for (int i = 0; i < 4; i++)
                tilt.AddChild(Props.Part(Props.Box(0.12f, 0.24f, 1.8f), Rotten.Lightened(0.08f),
                    new Vector3(x * 1.055f, 0.9f + i * 0.63f, -3.9f + i * 2.1f),
                    new Vector3(i % 2 == 0 ? 8f : -11f, 0, 0)));
        }
        // Open gables and roof trusses show the skeleton where the roof has fallen away.
        foreach (float z in new[] { -6.3f, -2.2f, 2.1f, 6.3f })
        {
            Beam(tilt, new Vector3(-4f, 3.9f, z), new Vector3(0, 6.3f, z), 0.18f, timber);
            Beam(tilt, new Vector3(0, 6.3f, z), new Vector3(4f, 3.9f, z), 0.18f, timber);
            Beam(tilt, new Vector3(-3.9f, 3.95f, z), new Vector3(3.9f, 3.95f, z), 0.17f, timber);
            Beam(tilt, new Vector3(0, 4f, z), new Vector3(0, 6.25f, z), 0.14f, timber);
        }
        // Raised sheet seams, rust repairs and torn battens break up the broad roof planes.
        for (int i = 0; i < 12; i++)
        {
            float z = -6.1f + i * 1.1f;
            Beam(tilt, new Vector3(-4.12f, 3.82f, z), new Vector3(-0.08f, 6.34f, z),
                0.045f, i % 3 == 0 ? rust : ConcreteDark * 0.75f);
        }
        foreach (float z in new[] { 0.7f, 2.8f, 4.9f })
            Beam(tilt, new Vector3(0.15f, 6.05f, z), new Vector3(2.4f, 4.7f, z + 0.2f), 0.1f, Rotten);
        tilt.AddChild(Props.Part(Props.Box(1.1f, 0.04f, 1.6f), rust,
            new Vector3(-2.7f, 4.72f, -2.6f), new Vector3(0, 0, 32)));
        // The entrance has a split lintel and two doors hanging at different angles.
        foreach (float side in new[] { -1f, 1f })
        {
            Beam(tilt, new Vector3(side * 1.7f, 0.1f, 6.5f), new Vector3(side * 1.7f, 3.7f, 6.5f), 0.25f, timber);
            var door = new Node3D { Position = new Vector3(side * 1.65f, 0.25f, 6.55f),
                RotationDegrees = new Vector3(0, side * 24f, side * 5f) };
            tilt.AddChild(door);
            for (int i = 0; i < 5; i++)
                door.AddChild(Props.Part(Props.Box(0.25f, 2.9f - i * 0.07f, 0.12f), Rotten * (0.8f + i * 0.04f),
                    new Vector3(-side * (0.15f + i * 0.28f), 1.45f, 0)));
            Beam(door, new Vector3(-side * 0.1f, 0.4f, 0.1f), new Vector3(-side * 1.3f, 2.55f, 0.1f), 0.13f, timber);
            foreach (float y in new[] { 0.6f, 2.35f })
                door.AddChild(Props.Part(Props.Box(0.65f, 0.09f, 0.06f), rust, new Vector3(-side * 0.35f, y, 0.13f)));
        }
        Beam(tilt, new Vector3(-1.8f, 3.7f, 6.5f), new Vector3(1.8f, 3.55f, 6.5f), 0.24f, timber);
        // Fallen boards and masonry stay close to the building's existing collision footprint.
        for (int i = 0; i < 9; i++)
            n.AddChild(Props.Part(Props.Box(rng.RandfRange(0.8f, 2.6f), 0.07f, 0.22f), Rotten * rng.RandfRange(0.75f, 1.15f),
                new Vector3(rng.RandfRange(-3.4f, 3.4f), 0.08f + i * 0.015f, rng.RandfRange(3.8f, 6.3f)),
                new Vector3(0, rng.RandfRange(-70f, 70f), rng.RandfRange(-4f, 4f))));
        // The old supply has been disconnected: insulators, fuse box and a short loose tail only.
        tilt.AddChild(VillagePower.ServiceMast("ruinbarn", new Vector3(4.2f, 3.8f, -3f)));
        tilt.AddChild(Props.Part(Props.Box(0.04f, 0.7f, 0.04f), new Color(0.1f, 0.1f, 0.09f),
            new Vector3(4.27f, 3.45f, -3f), new Vector3(0, 0, -18)));
        DrunkTractor.BatchStatic(n, null);
        return n;
    }

    /// <summary>Abandoned grain store: broken shell, torn roof, exposed rebar and a ruined ladder.</summary>
    public static Node3D RuinedSilo()
    {
        var root = new Node3D();
        var rng = new RandomNumberGenerator { Seed = 1973 };
        Color concrete = new(.43f,.42f,.37f), rust = new(.35f,.23f,.15f), dark = new(.15f,.16f,.14f);
        var st = new SurfaceTool(); st.Begin(Mesh.PrimitiveType.Triangles);
        void Tri(Vector3 a, Vector3 b, Vector3 c, Color colour)
        {
            st.SetColor(colour); st.SetNormal((b-a).Cross(c-a).Normalized());
            st.AddVertex(a); st.AddVertex(b); st.AddVertex(c);
        }
        Vector3 At(float radius,float y,int sector)
        { float angle=sector*MathF.Tau/16; return new Vector3(MathF.Cos(angle)*radius,y,MathF.Sin(angle)*radius); }
        float[] top = new float[17];
        for(int i=0;i<16;i++) top[i]=i<6?rng.RandfRange(9.3f,11):rng.RandfRange(11.4f,12.1f);
        top[16]=top[0];
        for(int i=0;i<16;i++)
        {
            for(int band=0;band<4;band++)
            {
                if ((band == 2 && i is 2 or 3 or 10) || (band == 3 && i is 1 or 2 or 3 or 7 or 11)) continue;
                float y0=band*2.3f,y1=band==3?top[i]:(band+1)*2.3f,y2=band==3?top[i+1]:y1;
                Color colour=concrete*rng.RandfRange(.8f,1.14f);
                Vector3 a=At(2.4f,y0,i),b=At(2.4f,y0,i+1),c=At(2.4f,y2,i+1),d=At(2.4f,y1,i);
                Tri(a,b,c,colour);Tri(a,c,d,colour);
                a=At(2.13f,y0,i);b=At(2.13f,y0,i+1);c=At(2.13f,y2,i+1);d=At(2.13f,y1,i);
                Tri(a,c,b,dark);Tri(a,d,c,dark);
            }
            if(i>6) Tri(At(2.48f,12.15f,i),At(2.48f,12.15f,i+1),new Vector3(.4f,13.1f,.2f),rust*rng.RandfRange(.8f,1.15f));
            if(i<7) root.AddChild(Props.Part(Props.Box(.045f,.85f,.045f),rust,At(2.27f,top[i]+.3f,i),new Vector3(i*3,0,-8)));
            if(i%3==0) root.AddChild(Props.Part(Props.Box(.07f,rng.RandfRange(2,5),.07f),rust,At(2.42f,7,i),new Vector3(0,0,3)));
        }
        // Broad rust runs and damp concrete stains remain readable from the driving camera.
        for(int i=0;i<16;i++)
        {
            float angle=i*MathF.Tau/16, y=4.5f+(i%3)*1.1f;
            Vector3 Patch(float a,float h) => new(MathF.Cos(a)*2.425f,h,MathF.Sin(a)*2.425f);
            Color stain=i%2==0?new Color(.3f,.24f,.17f):new Color(.29f,.32f,.25f);
            Tri(Patch(angle-.07f,y+1.7f),Patch(angle+.09f,y+1.4f),Patch(angle+.04f,y-1.3f),stain);
            Tri(Patch(angle-.07f,y+1.7f),Patch(angle+.04f,y-1.3f),Patch(angle-.04f,y-.4f),stain);
            Tri(Patch(angle-.17f,.05f),Patch(angle+.17f,.05f),Patch(angle+.09f,1.4f+i%3*.2f),new Color(.3f,.32f,.25f));
        }
        root.AddChild(new MeshInstance3D { Mesh=st.Commit(),MaterialOverride=new StandardMaterial3D { VertexColorUseAsAlbedo=true,CullMode=BaseMaterial3D.CullModeEnum.Disabled } });
        foreach(float y in new[]{1.5f,4.5f,7.5f,9f})
            for(int i=0;i<16;i++)
            {
                Vector3 a=At(2.43f,y,i),b=At(2.43f,y,i+1),d=b-a;
                var band=Props.Part(Props.Box(.07f,.07f,d.Length()),rust,(a+b)*.5f);
                band.Basis=Basis.LookingAt(d.Normalized(),Vector3.Up);root.AddChild(band);
            }
        foreach(float x in new[]{-.32f,.32f}) root.AddChild(Props.Part(Props.Box(.07f,6.8f,.07f),rust,new Vector3(x,5.5f,2.43f),new Vector3(0,0,x*5)));
        for(int i=0;i<12;i++) if(i%4!=1) root.AddChild(Props.Part(Props.Box(.66f,.06f,.06f),rust,new Vector3(0,2.4f+i*.55f,2.47f),new Vector3(0,0,i%3==0?9:0)));
        root.AddChild(Props.Part(Props.Box(1.1f,2.1f,.045f),dark,new Vector3(0,1.05f,2.42f)));
        root.AddChild(Props.Part(Props.Box(.65f,1.9f,.07f),rust,new Vector3(.7f,.9f,2.55f),new Vector3(0,35,-14)));
        root.AddChild(Props.Part(Props.Box(.16f,1.3f,.16f),rust,new Vector3(-1.4f,.8f,2.12f),new Vector3(0,0,17)));
        root.AddChild(Props.Part(Props.Box(2.3f,.08f,1.1f),rust,new Vector3(-1.2f,.2f,-2.15f),new Vector3(8,25,-13)));
        for(int i=0;i<9;i++) root.AddChild(Props.Part(Props.Box(.3f,.2f,.4f),concrete*rng.RandfRange(.7f,1),At(rng.RandfRange(2.4f,2.9f),.12f,i),new Vector3(0,i*31,12)));
        DrunkTractor.BatchStatic(root,null);
        root.AddChild(TextPanel("ЗЕРНО",1.5f,.5f,new Vector3(-.25f,7.8f,2.45f),rust,new Color(.64f,.58f,.4f)));
        return root;
    }

    /// <summary>Bare, bleached dead tree: trunk and a few crooked branches.</summary>
    public static Node3D DeadTree(float scale)
    {
        var n = new Node3D();
        var bark = new Color(0.45f, 0.42f, 0.38f);
        n.AddChild(Props.Part(Props.Cyl(0.18f, 5f, 5, 0.1f), bark, new Vector3(0, 2.5f, 0)));
        var rng = new RandomNumberGenerator { Seed = (ulong)(scale * 1000) };
        for (int k = 0; k < 5; k++)
        {
            float a = k * 72f + rng.RandfRange(-20, 20);
            float y = rng.RandfRange(2.5f, 4.6f);
            var br = new Node3D { Position = new Vector3(0, y, 0), RotationDegrees = new Vector3(rng.RandfRange(35, 65), a, 0) };
            br.AddChild(Props.Part(Props.Cyl(0.06f, 1.8f, 4, 0.03f), bark, new Vector3(0, 0.9f, 0)));
            n.AddChild(br);
        }
        n.Scale = Vector3.One * scale;
        return n;
    }

    /// <summary>
    /// The radar station: bunker, lattice tower, an absurdly large dish crowned with a Yagi rake,
    /// all spinning, and a red beacon. Grass and trees around it are dead.
    /// </summary>
    public static Node3D Radar(bool winter = false)
    {
        var n = new Node3D();
        var steel = new Color(0.5f, 0.52f, 0.48f);
        var olive = new Color(0.34f, 0.38f, 0.26f);
        n.AddChild(Props.Part(Props.Box(7f, 3f, 7f), ConcreteDark, new Vector3(0, 1.5f, 0)));             // bunker
        n.AddChild(Props.Part(Props.Box(1.2f, 2f, 0.1f), new Color(0.2f, 0.22f, 0.18f), new Vector3(0, 1f, 3.52f)));
        // Lattice tower on the bunker roof. Kept short: the chase camera is only ~31 m up, and a
        // dish much higher than this would fill the screen or lean out of it beside the road.
        const float TopY = 9f;
        foreach (float sx in new[] { -1.4f, 1.4f })
        foreach (float sz in new[] { -1.4f, 1.4f })
            n.AddChild(Props.Part(Props.Box(0.16f, TopY - 3f, 0.16f), steel, new Vector3(sx * 0.8f, 3f + (TopY - 3f) / 2f, sz * 0.8f), new Vector3(sz * 3f, 0, -sx * 3f)));
        for (float y = 4.5f; y < TopY; y += 2.2f)
            foreach (float r in new[] { 0f, 90f })
                n.AddChild(Props.Part(Props.Box(2.6f, 0.1f, 0.1f), steel, new Vector3(0, y, 0), new Vector3(0, r, 38)));
        n.AddChild(Props.Part(Props.Box(3.4f, 0.25f, 3.4f), steel, new Vector3(0, TopY, 0)));

        // Spinning head: huge tilted dish + a giant rake of dipoles + a counterweight cabin.
        var head = new RadarHead { Position = new Vector3(0, TopY + 0.2f, 0) };
        n.AddChild(head);
        var dish = Props.Part(Props.Ball(5.5f, 12), new Color(0.82f, 0.82f, 0.78f), new Vector3(0, 3.2f, -1.2f), new Vector3(-70, 0, 0));
        dish.Scale = new Vector3(1f, 0.25f, 0.8f);
        head.AddChild(dish);
        head.AddChild(Props.Part(Props.Cyl(0.12f, 3.5f, 4), steel, new Vector3(0, 3.6f, -3.2f), new Vector3(-70, 0, 0)));  // feed horn
        head.AddChild(Props.Part(Props.Box(12f, 0.15f, 0.15f), steel, new Vector3(0, 7.4f, 0)));
        for (int k = -5; k <= 5; k++)
            head.AddChild(Props.Part(Props.Box(0.08f, 1.6f, 0.08f), steel, new Vector3(k * 1.1f, 7.4f, 0)));
        head.AddChild(Props.Part(Props.Box(2.4f, 1.8f, 2.2f), olive, new Vector3(0, 0.9f, 1.8f)));
        head.Beacon = NightGlow.Part(Props.Ball(0.3f), Red, new Color(1f, 0.1f, 0.05f), 2.5f, new Vector3(0, 8.4f, 0));
        head.AddChild(head.Beacon);

        // "KEEP OUT" plate on the bunker.
        n.AddChild(TextPanel("НЕ ВХОДИТЬ!", 5f, 1f, new Vector3(0, 2.6f, 3.56f), Red, Colors.White));
        SiberiaModelDetails.Radar(n, winter);
        RadarExtras(n, head, winter);
        return n;
    }

    /// <summary>
    /// The radar station's working parts: footing, roof parapet, a framed hazard-striped door with canopy, steps and lamp,
    /// a chimney, fan box, whip and link antennas, a generator shed with a pipe and fuel drums, cable runs, red and white
    /// aviation bands and a railed platform on the tower, a lightning rod, a turntable under the spinning head and marker lamps on its rake.
    /// </summary>
    static void RadarExtras(Node3D n, RadarHead head, bool winter)
    {
        var solid = new Node3D();
        Color steel = new(0.5f, 0.52f, 0.48f), tin = new(0.38f, 0.4f, 0.42f), hazardY = new(0.9f, 0.72f, 0.1f), dark = new(0.1f, 0.1f, 0.1f);
        Color white = new(0.9f, 0.9f, 0.86f), olive = new(0.34f, 0.38f, 0.26f), rust = new(0.4f, 0.24f, 0.15f), snow = new(0.95f, 0.96f, 0.99f);
        void Add(Mesh mesh, Color c, Vector3 at, Vector3? rot = null) => solid.AddChild(Props.Part(mesh, c, at, rot));
        void Rod(Vector3 a, Vector3 b, float w, Color c)
        {
            Vector3 d = b - a;
            var part = Props.Part(Props.Box(w, w, d.Length()), c, (a + b) / 2f);
            part.Basis = Basis.LookingAt(d.Normalized(), MathF.Abs(d.Normalized().Y) > 0.99f ? Vector3.Right : Vector3.Up);
            solid.AddChild(part);
        }
        // The bunker: footing, parapet round the roof, a framed door with hazard stripes, a canopy, two steps.
        Add(Props.Box(7.5f, 0.25f, 7.5f), ConcreteDark * 0.9f, new Vector3(0, 0.125f, 0));
        foreach (float s in new[] { -1f, 1f })
        {
            Add(Props.Box(7.2f, 0.3f, 0.22f), ConcreteDark, new Vector3(0, 3.15f, s * 3.4f));
            Add(Props.Box(0.22f, 0.3f, 6.6f), ConcreteDark, new Vector3(s * 3.4f, 3.15f, 0));
            Add(Props.Box(0.14f, 2.15f, 0.14f), steel, new Vector3(s * 0.7f, 1.08f, 3.56f));
        }
        Add(Props.Box(1.55f, 0.16f, 0.14f), steel, new Vector3(0, 2.1f, 3.56f));
        for (int k = 0; k < 5; k++)
            Add(Props.Box(0.14f, 0.55f, 0.04f), k % 2 == 0 ? hazardY : dark, new Vector3(-0.48f + k * 0.24f, 0.4f, 3.59f), new Vector3(0, 0, 35));
        Add(Props.Box(1.9f, 0.1f, 1.0f), ConcreteDark, new Vector3(0, 2.4f, 4.0f), new Vector3(6, 0, 0));
        foreach (float s in new[] { -1f, 1f }) Rod(new Vector3(s * 0.85f, 2.35f, 4.4f), new Vector3(s * 0.85f, 2.0f, 3.58f), 0.06f, steel);
        Add(Props.Box(1.7f, 0.18f, 0.55f), ConcreteDark, new Vector3(0, 0.09f, 4.0f));
        Add(Props.Box(1.4f, 0.18f, 0.4f), ConcreteDark, new Vector3(0, 0.27f, 3.78f));
        // Roof: chimney, a fan housing with louvres, a whip antenna and a small link dish on a bracket.
        Add(Props.Cyl(0.3f, 1.3f, 8, 0.24f), tin, new Vector3(-2.3f, 3.75f, -2.1f));
        Add(Props.Cyl(0.36f, 0.1f, 8), dark, new Vector3(-2.3f, 4.45f, -2.1f));
        Add(Props.Box(1.1f, 0.55f, 1.1f), olive, new Vector3(2.0f, 3.3f, -1.8f));
        for (int k = 0; k < 4; k++) Add(Props.Box(0.9f, 0.04f, 0.04f), steel, new Vector3(2.0f, 3.18f + k * 0.1f, -1.24f));
        Add(Props.Cyl(0.02f, 3.2f, 4), steel, new Vector3(2.7f, 4.6f, 2.5f));
        Add(Props.Cyl(0.05f, 0.9f, 5), steel, new Vector3(-2.6f, 3.65f, 2.5f));
        var link = Props.Part(Props.Cyl(0.5f, 0.12f, 12), white * 0.95f, new Vector3(-2.6f, 4.2f, 2.65f), new Vector3(90, 0, 0));
        solid.AddChild(link);
        Add(Props.Cyl(0.06f, 0.35f, 5), steel, new Vector3(-2.6f, 4.2f, 2.4f), new Vector3(90, 0, 0));
        // A generator shed against the back wall, with a pipe, a hatch, three drums, a conduit and a warning plate.
        Add(Props.Box(3.0f, 2.2f, 1.4f), tin, new Vector3(-1.0f, 1.1f, -4.2f));
        Add(new PrismMesh { Size = new Vector3(3.3f, 0.5f, 1.7f) }, tin * 0.85f, new Vector3(-1.0f, 2.45f, -4.2f));
        Add(Props.Box(0.9f, 1.8f, 0.05f), rust, new Vector3(-1.0f, 0.9f, -4.93f));
        Add(Props.Cyl(0.1f, 1.4f, 6), dark, new Vector3(0.2f, 2.9f, -4.5f));
        for (int k = 0; k < 3; k++)
            Add(Props.Cyl(0.3f, 0.9f, 10), k == 1 ? Red : k == 0 ? olive : rust, new Vector3(1.0f + k * 0.65f, 0.45f, -4.45f));
        Rod(new Vector3(-0.5f, 2.0f, -3.45f), new Vector3(0.5f, 3.3f, -3.0f), 0.06f, dark);       // a cable run up to the roof...
        Rod(new Vector3(0.5f, 3.3f, -3.0f), new Vector3(0.7f, 3.35f, -0.3f), 0.06f, dark);        // ...and over it to the tower
        // The tower: red and white bands on the legs, a railed platform and a lightning rod.
        foreach (float sx in new[] { -1f, 1f })
        foreach (float sz in new[] { -1f, 1f })
            foreach (float y in new[] { 4.6f, 6.4f, 8.2f })
                Add(Props.Box(0.2f, 0.45f, 0.2f), y > 5f && y < 7f ? white : Red, new Vector3(sx * 0.8f * (1f - (y - 3f) * 0.0f), y, sz * 0.8f));
        const float TopY = 9f;
        foreach (float sx in new[] { -1f, 1f })
        foreach (float sz in new[] { -1f, 1f })
            Add(Props.Box(0.06f, 1.0f, 0.06f), steel, new Vector3(sx * 1.62f, TopY + 0.6f, sz * 1.62f));
        foreach (float y in new[] { TopY + 0.65f, TopY + 1.0f })
        {
            Add(Props.Box(3.3f, 0.05f, 0.05f), steel, new Vector3(0, y, 1.62f));
            Add(Props.Box(3.3f, 0.05f, 0.05f), steel, new Vector3(0, y, -1.62f));
            Add(Props.Box(0.05f, 0.05f, 3.3f), steel, new Vector3(1.62f, y, 0));
            Add(Props.Box(0.05f, 0.05f, 3.3f), steel, new Vector3(-1.62f, y, 0));
        }
        Add(Props.Cyl(0.03f, 2.6f, 4), steel, new Vector3(1.5f, TopY + 1.6f, -1.5f));                   // the lightning rod
        if (winter)
        {
            Add(Props.Box(3.4f, 0.1f, 3.4f), snow, new Vector3(0, TopY + 0.16f, 0));
            Add(new PrismMesh { Size = new Vector3(3.3f, 0.2f, 1.7f) }, snow, new Vector3(-1.0f, 2.62f, -4.2f));
        }
        DrunkTractor.BatchStatic(solid, null);
        n.AddChild(solid);
        n.AddChild(TextPanel("ВНИМАНИЕ!", 1.4f, 0.4f, new Vector3(-1.0f, 1.9f, -4.94f), Red, Colors.White));
        // The lamp over the door glows after dark.
        n.AddChild(NightGlow.Part(Props.Box(0.3f, 0.16f, 0.22f), steel, new Color(1f, 0.82f, 0.5f), 1.6f, new Vector3(-0.95f, 2.3f, 3.68f)));
        // The head: a turntable under it, hatch and louvres on the counterweight cabin, marker lamps at the ends of the rake.
        var turn = new Node3D();
        turn.AddChild(Props.Part(Props.Cyl(1.25f, 0.4f, 14), dark, new Vector3(0, 0.1f, 0)));
        turn.AddChild(Props.Part(Props.Cyl(1.0f, 0.3f, 14), steel, new Vector3(0, 0.4f, 0)));
        foreach (float s in new[] { -1f, 1f })
        {
            for (int k = 0; k < 4; k++) turn.AddChild(Props.Part(Props.Box(0.04f, 0.06f, 1.6f), dark, new Vector3(s * 1.21f, 0.5f + k * 0.28f, 1.8f)));
            turn.AddChild(Props.Part(Props.Box(0.06f, 1.0f, 0.12f), dark, new Vector3(s * 0.55f, 0.9f, 2.92f)));
        }
        turn.AddChild(Props.Part(Props.Box(0.9f, 0.1f, 0.6f), steel, new Vector3(0, 1.85f, 1.8f)));      // roof hatch
        DrunkTractor.BatchStatic(turn, null);
        head.AddChild(turn);
        foreach (float s in new[] { -1f, 1f })
            head.AddChild(NightGlow.Part(Props.Ball(0.14f, 6), Red, new Color(1f, 0.1f, 0.05f), 3f, new Vector3(s * 6.1f, 7.4f, 0)));
    }

    /// <summary>Double-sided text plate (pixel font), like the billboards.</summary>
    internal static MeshInstance3D TextPanel(string text, float w, float h, Vector3 pos, Color bg, Color fg)
    {
        return new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(w, h) },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoTexture = ImageTexture.CreateFromImage(PixelFont.RenderImage(text, fg, bg, 2)),
                TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            },
            Position = pos,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }
}

/// <summary>Rotates the radar head and blinks its beacon.</summary>
public partial class RadarHead : Node3D
{
    /// <summary>Radar heads in the scene as RaceScene's track build and the school's restore keep it (every build clears it): readers use Heads and InWorld.</summary>
    public static readonly System.Collections.Generic.List<RadarHead> All = new();
    /// <summary>Every radar head in the tree, whichever world it is in; only a head's own enter and exit change it (read it with World).</summary>
    public static readonly System.Collections.Generic.List<RadarHead> Heads = new();
    /// <summary>The 3D world this head is in (set on entering the tree).</summary>
    public World3D World { get; private set; }

    /// <summary>The radar heads of one world. A null world: all of them.</summary>
    public static System.Collections.Generic.List<RadarHead> InWorld(World3D world) =>
        Heads.FindAll(head => world == null || head.World == world);
    /// <summary>Spin rate (rad/s): one sweep every 2π/1.1 ≈ 5.7 s.</summary>
    public const float SpinRate = 1.1f;

    public MeshInstance3D Beacon;
    float _t;

    /// <summary>Horizontal unit direction the dish is pointing (it faces the head's local -Z).</summary>
    public Vector2 Beam
    {
        get { var z = -GlobalTransform.Basis.Z; return new Vector2(z.X, z.Z).Normalized(); }
    }

    public override void _EnterTree() { All.Add(this); World = WorldScope.Of(this); Heads.Add(this); }
    public override void _ExitTree() { All.Remove(this); Heads.Remove(this); }

    public override void _Process(double delta)
    {
        _t += (float)delta;
        RotateY((float)delta * SpinRate);
        if (Beacon != null) Beacon.Visible = (int)(_t * 1.6f) % 2 == 0;
    }
}
