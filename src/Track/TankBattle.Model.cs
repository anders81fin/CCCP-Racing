using Godot;

namespace CCCPRacing.Track;

public partial class TankBattle
{
    /// <summary>1980s T-72 field fittings, built into the hull/turret/gun batches.</summary>
    static void BuildDetails(Tank k, Color olive, Color dark, Color steel)
    {
        Color edge = olive.Lightened(0.14f), armour = olive.Darkened(0.12f);
        Color rubber = new(0.075f, 0.08f, 0.065f), mud = new(0.24f, 0.21f, 0.13f);
        Color glass = new(0.12f, 0.23f, 0.25f), canvas = new(0.43f, 0.42f, 0.27f);
        void Part(Node3D n, Mesh mesh, Color c, float x, float y, float z, float rx = 0, float ry = 0, float rz = 0)
            => n.AddChild(Props.Part(mesh, c, new Vector3(x, y, z), new Vector3(rx, ry, rz)));
        void Box(Node3D n, Color c, float x, float y, float z, float w, float h, float d, float rx = 0, float ry = 0, float rz = 0)
            => Part(n, Props.Box(w, h, d), c, x, y, z, rx, ry, rz);

        foreach (float side in new[] { -1f, 1f })
        {
            for (int wheel = 0; wheel < 6; wheel++)
            {
                float z = -2.6f + wheel * 1.04f;
                // Separate rubber side-skirt panels and their mounting strip.
                Box(k.Hull, armour, side * 2.015f, 1.04f, z, 0.09f, 0.38f, 0.96f);
                Box(k.Hull, edge, side * 2.07f, 1.205f, z, 0.04f, 0.045f, 0.88f);
                Box(k.Hull, steel, side * 2.075f, 1.10f, z - 0.32f, 0.03f, 0.06f, 0.07f);
            }
            foreach (float end in new[] { -1f, 1f })
            {
                Part(k.Hull, Props.Cyl(0.30f, 0.09f, 10), steel, side * 1.97f, 0.52f, end * 3.12f, 0, 0, 90);
                Part(k.Hull, Props.Cyl(0.12f, 0.11f, 8), dark, side * 2.02f, 0.52f, end * 3.12f, 0, 0, 90);
                Box(k.Hull, rubber, side * 1.56f, 0.60f, end * 3.49f, 0.69f, 0.46f, 0.07f, end * 12);
            }
            // Fender edge, tool box, towing eyes and protected headlights.
            Box(k.Hull, edge, side * 1.73f, 1.35f, -0.05f, 0.50f, 0.09f, 6.65f);
            Box(k.Hull, armour, side * 1.55f, 1.52f, 1.35f, 0.40f, 0.28f, 1.1f);
            Box(k.Hull, edge, side * 1.55f, 1.67f, 1.35f, 0.43f, 0.035f, 1.12f);
            Box(k.Hull, steel, side * 0.96f, 0.64f, -3.94f, 0.19f, 0.15f, 0.26f);
            Box(k.Hull, dark, side * 0.96f, 0.65f, -4.08f, 0.08f, 0.075f, 0.025f);
            Part(k.Hull, Props.Cyl(0.16f, 0.13f, 8), dark, side * 1.23f, 1.22f, -3.64f, 90);
            Part(k.Hull, Props.Cyl(0.115f, 0.02f, 8), new Color(0.76f, 0.73f, 0.48f), side * 1.23f, 1.22f, -3.72f, 90);
            Box(k.Hull, steel, side * 1.23f, 1.40f, -3.70f, 0.36f, 0.035f, 0.32f);
            foreach (float bar in new[] { -1f, 1f })
                Box(k.Hull, steel, side * 1.23f + bar * 0.17f, 1.25f, -3.78f, 0.025f, 0.29f, 0.025f);
            // Drum end plates and retaining bands on the original rear fuel drums.
            foreach (float band in new[] { -1f, 1f })
                Part(k.Hull, Props.Cyl(0.315f, 0.055f, 10), steel, side * 0.6f + band * 0.25f, 1.3f, 3.5f, 0, 0, 90);
            Part(k.Hull, Props.Cyl(0.26f, 0.025f, 10), armour, side * 1.065f, 1.3f, 3.5f, 0, 0, 90);
            // Rear lights and the engine-deck grille: dark recesses, separate ribs.
            Box(k.Hull, dark, side * 1.38f, 1.08f, 3.53f, 0.25f, 0.18f, 0.10f);
            Box(k.Hull, new Color(0.43f, 0.07f, 0.05f), side * 1.38f, 1.1f, 3.59f, 0.16f, 0.08f, 0.025f);
            Box(k.Hull, dark, side * 0.65f, 1.465f, 2.0f, 1.0f, 0.035f, 1.7f);
            for (int slat = 0; slat < 10; slat++)
                Box(k.Hull, armour, side * 0.65f, 1.495f, 1.27f + slat * 0.16f, 0.91f, 0.035f, 0.07f);
        }

        // The angled glacis carries spaced reactive-armour bricks, not a plain slab.
        for (int row = 0; row < 2; row++)
            for (int col = 0; col < 6; col++)
            {
                float x = -1.2f + col * 0.48f;
                Box(k.Hull, row == 0 ? edge : olive.Lightened(0.07f), x, 1.19f + row * 0.24f, -3.72f + row * 0.46f, 0.43f, 0.18f, 0.40f, -28);
            }
        for (int row = 0; row < 2; row++)
            for (int col = 0; col < 6; col++)
            {
                if (row == 1 && col is 1 or 2) continue; // leave the driver's hatch and vision clear
                float x = -1.2f + col * 0.48f, z = -2.94f + row * 0.39f;
                Box(k.Hull, dark, x, 1.48f, z, 0.45f, 0.035f, 0.35f);
                Box(k.Hull, (row + col) % 3 == 0 ? edge : olive.Lightened(0.06f), x, 1.56f, z, 0.41f, 0.15f, 0.31f);
            }
        // Driver's hatch and vision block, engine access covers, exhaust and rear unditching log.
        Part(k.Hull, Props.Cyl(0.34f, 0.08f, 8), armour, -0.35f, 1.5f, -2.2f);
        Box(k.Hull, dark, -0.35f, 1.58f, -2.46f, 0.36f, 0.12f, 0.12f);
        Box(k.Hull, glass, -0.35f, 1.60f, -2.53f, 0.26f, 0.055f, 0.025f);
        Box(k.Hull, armour, 0, 1.49f, 2.05f, 0.19f, 0.045f, 1.65f);
        Box(k.Hull, dark, -1.73f, 1.01f, 2.65f, 0.38f, 0.20f, 0.55f);
        Part(k.Hull, Props.Cyl(0.145f, 2.75f, 10), new Color(0.38f, 0.27f, 0.15f), 0, 0.56f, 3.7f, 0, 0, 90);
        foreach (float x in new[] { -0.95f, 0.95f })
            Box(k.Hull, steel, x, 0.57f, 3.86f, 0.10f, 0.27f, 0.07f);

        Part(k.Turret, Props.Cyl(1.14f, 0.18f, 12), armour, 0, 0.04f, 0.2f);
        Part(k.Turret, Props.Cyl(0.34f, 0.62f, 10), armour, 0, 0.3f, -1.05f, 90);
        foreach (float side in new[] { -1f, 1f })
        {
            // Armour on the front cheeks, leaving the numbered sides exposed.
            for (int brick = 0; brick < 3; brick++)
                Box(k.Turret, brick % 2 == 0 ? edge : olive, side * (0.50f + brick * 0.31f),
                    0.55f - brick * 0.08f, -0.97f + brick * 0.20f, 0.30f, 0.22f, 0.55f, -12, side * 25);
            // Three smoke grenade launchers on each cheek, with dark open mouths.
            for (int tube = 0; tube < 3; tube++)
            {
                float x = side * (1.10f + tube * 0.07f), z = -0.42f + tube * 0.19f;
                Part(k.Turret, Props.Cyl(0.075f, 0.32f, 6), dark, x, 0.50f, z, -35, 0, -side * 30);
            }
            Box(k.Turret, armour, side * 0.72f, 0.33f, 1.45f, 0.83f, 0.39f, 0.35f);
            Box(k.Turret, edge, side * 0.72f, 0.54f, 1.45f, 0.86f, 0.045f, 0.38f);
            // Grab rail in three visible pieces.
            Box(k.Turret, steel, side * 1.27f, 0.64f, 0.72f, 0.035f, 0.035f, 0.65f);
            foreach (float z in new[] { 0.42f, 1.02f })
                Box(k.Turret, steel, side * 1.27f, 0.58f, z, 0.035f, 0.14f, 0.035f);
        }
        // Commander's cupola, loader's hatch and blue-black periscopes.
        Part(k.Turret, Props.Cyl(0.35f, 0.25f, 10), armour, 0.5f, 0.79f, 0.5f);
        Part(k.Turret, Props.Cyl(0.37f, 0.065f, 10), edge, 0.5f, 0.95f, 0.5f);
        Part(k.Turret, Props.Cyl(0.29f, 0.07f, 10), edge, -0.48f, 0.80f, 0.28f);
        foreach (float x in new[] { -0.48f, 0.5f })
            Box(k.Turret, steel, x, x > 0 ? 1.0f : 0.86f, 0.55f, 0.18f, 0.04f, 0.035f);
        Box(k.Turret, dark, -0.42f, 0.77f, -0.45f, 0.32f, 0.17f, 0.25f);
        Box(k.Turret, glass, -0.42f, 0.79f, -0.58f, 0.24f, 0.085f, 0.025f);
        Box(k.Turret, glass, 0.5f, 0.81f, 0.14f, 0.25f, 0.075f, 0.035f);
        // Cupola machine gun, mount and ammunition box, all turning with the turret.
        Part(k.Turret, Props.Cyl(0.075f, 0.28f, 6), steel, 0.58f, 1.1f, 0.30f);
        Box(k.Turret, dark, 0.58f, 1.26f, 0.15f, 0.14f, 0.14f, 0.44f);
        Part(k.Turret, Props.Cyl(0.035f, 0.96f, 6), dark, 0.58f, 1.27f, -0.54f, 90);
        Box(k.Turret, armour, 0.80f, 1.18f, 0.2f, 0.25f, 0.24f, 0.33f);
        Box(k.Turret, steel, 0.58f, 1.38f, -0.10f, 0.035f, 0.13f, 0.035f);
        // Canvas roll and two straps across the turret's rear bustle.
        Part(k.Turret, Props.Cyl(0.16f, 1.18f, 8), canvas, 0, 0.58f, 1.51f, 0, 0, 90);
        foreach (float x in new[] { -0.38f, 0.38f })
            Part(k.Turret, Props.Cyl(0.17f, 0.055f, 8), armour, x, 0.58f, 1.51f, 0, 0, 90);
        Part(k.Turret, Props.Cyl(0.08f, 0.14f, 6), dark, -0.86f, 0.70f, 0.98f);
        Box(k.Turret, dark, -0.94f, 1.49f, 1.0f, 0.035f, 1.5f, 0.035f, 0, 0, -6);

        // Heavy cast-turret seams, lifting eyes and spare track shoes on the cheeks.
        foreach(float side in new[]{-1f,1f})
        {
            for(int segment=0;segment<5;segment++)
            {
                float z=-.5f+segment*.30f;
                Box(k.Turret,armour.Darkened(.3f),side*1.28f,.19f,z,.035f,.045f,.27f);
                Box(k.Turret,edge,side*1.28f,.23f,z,.04f,.018f,.23f);
            }
            Part(k.Turret,new TorusMesh {InnerRadius=.075f,OuterRadius=.125f,Rings=10,RingSegments=6},steel,side*.92f,.76f,.0f,90);
            for(int shoe=0;shoe<3;shoe++)
            {
                Box(k.Turret,dark,side*1.25f,.45f,.72f+shoe*.22f,.12f,.27f,.18f);
                Box(k.Turret,steel,side*1.32f,.45f,.72f+shoe*.22f,.035f,.055f,.19f);
            }
            // Tow rope runs around the fender; clamps and chipped leading edges catch light.
            Box(k.Hull,steel,side*1.78f,1.43f,.6f,.035f,.04f,4.5f);
            for(int clamp=0;clamp<5;clamp++)
                Box(k.Hull,armour,side*1.78f,1.45f,-1.2f+clamp*.86f,.13f,.07f,.08f);
            for(int chip=0;chip<7;chip++)
                Box(k.Hull,new Color(.26f,.24f,.18f),side*(.35f+chip*.18f),.89f,-3.99f,.10f,.025f,.025f);
        }

        // Gun fittings follow the existing recoil, and retain the exact original muzzle position.
        Part(k.Barrel, Props.Cyl(0.21f, 0.58f, 10), armour, 0, 0, -2.95f, 90);
        foreach (float z in new[] { -1.0f, -1.7f, -3.65f, -4.8f })
            Part(k.Barrel, Props.Cyl(0.135f, 0.065f, 8), steel, 0, 0, z, 90);
        Part(k.Barrel, Props.Cyl(0.135f, 0.22f, 8), steel, 0, 0, -5.10f, 90);
        Part(k.Barrel, Props.Cyl(0.085f, 0.025f, 8), rubber, 0, 0, -5.225f, 90);
    }
}
