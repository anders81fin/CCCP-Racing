using System;
using Godot;

namespace CCCPRacing.Track;

/// <summary>
/// ATOMGRAD-4's service objects: the decontamination station (a game object: the bay the car washes in) and the vehicle graveyard's
/// rusting fleet (a lorry, a bus and an armoured carrier). Built in the idiom of the other tracks' objects: the stationary parts merged
/// into one mesh, the glowing things (the wrecks' faint green, lit panes, lamps) and the signs as their own nodes.
/// </summary>
public static partial class Atomgrad
{
    // ------------------------------------------------------------ the decontamination station

    /// <summary>
    /// The decontamination station: a hall over a wash bay open at both ends (along Z, 16 x 8 m, walls at x +-4, clear inside +-3.4),
    /// tiled walls with hazard bands and a kerb, a drained floor with a grating down the middle, four spray gantries with nozzles and side
    /// sprayers, roller brushes, water pipes, roof trusses, lamps, a roof monitor, fans and a duct, a header with its name at each end and
    /// a traffic light, a radiation sign; the operators' room with windows, a door, steps, canopy and a conditioner, a water tank on legs,
    /// yellow drums, a hose reel and puddles.
    /// </summary>
    public static Node3D WashStation()
    {
        var n = new Node3D();
        var solid = new Node3D();
        var glow = new Node3D();
        const float L = 16f, W = 8f, H = 6f;
        Color tile = new(0.74f, 0.76f, 0.74f), pipeBlue = new(0.2f, 0.4f, 0.62f), pipe = new(0.58f, 0.59f, 0.56f), wet = new(0.26f, 0.3f, 0.34f);
        Color tin = new(0.4f, 0.42f, 0.43f), drum = new(0.9f, 0.7f, 0.1f);
        // Floor with drains.
        Add(solid, Props.Box(W - 0.4f, 0.12f, L), ConcreteStain, 0, 0.06f, 0);
        Add(solid, Props.Box(0.9f, 0.07f, L - 1f), Black, 0, 0.1f, 0);
        for (int k = 0; k < 32; k++) Add(solid, Props.Box(0.9f, 0.04f, 0.1f), Steel, 0, 0.14f, -L / 2f + 0.8f + k * 0.47f);
        foreach (float s in new[] { -1f, 1f }) Add(solid, Props.Box(0.3f, 0.05f, L - 1f), Black, s * 3.2f, 0.1f, 0);
        // Walls: tiled, a kerb, seams, a hazard band on the inside, pipes along the top and trestles outside.
        foreach (float s in new[] { -1f, 1f })
        {
            Add(solid, Props.Box(0.5f, H, L), tile, s * W / 2f, H / 2f, 0);
            Add(solid, Props.Box(0.7f, 0.9f, L + 0.2f), ConcreteDark, s * W / 2f, 0.45f, 0);
            for (int k = 0; k < 7; k++) Add(solid, Props.Box(0.55f, H, 0.06f), ConcreteStain, s * W / 2f, H / 2f, -L / 2f + 1f + k * 2.33f);
            for (int k = 0; k < 4; k++) Add(solid, Props.Box(0.55f, 0.06f, L), ConcreteStain, s * W / 2f, 1.6f + k * 1.1f, 0);
            for (int k = 0; k < 16; k++) Add(solid, Props.Box(0.06f, 0.4f, 0.9f), k % 2 == 0 ? Yellow : Black, s * (W / 2f - 0.28f), 0.7f, -L / 2f + 0.5f + k * 1.0f);
            Add(solid, Props.Cyl(0.15f, L, 8), pipeBlue, s * (W / 2f - 0.5f), H - 0.45f, 0, 90, 0, 0);
            Add(solid, Props.Cyl(0.11f, L, 8), pipe, s * (W / 2f - 0.45f), 4.6f, 0, 90, 0, 0);
            for (int k = 0; k < 4; k++) Add(solid, Props.Box(0.3f, 1.5f, 0.3f), Steel, s * (W / 2f + 0.5f), 0.75f, -L / 2f + 2f + k * 4f);
        }
        // Roof: slab, edge trim, a glazed monitor along it, fans, a duct, trusses inside, lamps.
        Add(solid, Props.Box(W + 1f, 0.6f, L + 0.6f), Blue, 0, H + 0.3f, 0);
        Add(solid, Props.Box(W + 1.2f, 0.2f, L + 0.8f), ConcreteDark, 0, H + 0.65f, 0);
        Add(solid, Props.Box(3.4f, 0.9f, L - 2f), ConcreteDark, 0, H + 1.2f, 0);
        Add(solid, Props.Box(3.0f, 0.2f, L - 2.4f), Glass, 0, H + 1.7f, 0);
        foreach (float z in new[] { -4.5f, 0.5f, 5.5f })
        {
            Add(solid, Props.Cyl(0.8f, 0.5f, 10), tin, 2.4f, H + 1.2f, z);
            Add(solid, Props.Cyl(1.1f, 0.3f, 10, 0.15f), Rust, 2.4f, H + 1.7f, z);
        }
        Add(solid, Props.Cyl(0.4f, L - 2f, 8), pipe, -2.4f, H + 1.2f, 0, 90, 0, 0);
        for (int k = 0; k < 6; k++)
        {
            float z = -L / 2f + 1.3f + k * 2.68f;
            Add(solid, Props.Box(W - 0.6f, 0.28f, 0.22f), Steel, 0, H - 0.15f, z);
            Add(solid, Props.Box(0.1f, 0.6f, 0.1f), Steel, -1.4f, H - 0.6f, z);
            Add(solid, Props.Box(0.1f, 0.6f, 0.1f), Steel, 1.4f, H - 0.6f, z);
        }
        // Four spray gantries: a manifold with nozzles, the two uprights, three side sprayers each.
        for (int k = 0; k < 4; k++)
        {
            float z = -L / 2f + 2f + k * 4f;
            Add(solid, Props.Box(W - 0.6f, 0.15f, 0.15f), Steel, 0, H - 0.8f, z);
            foreach (float s in new[] { -1f, 1f })
            {
                Add(solid, Props.Cyl(0.09f, H - 0.7f, 8), pipeBlue, s * (W / 2f - 0.4f), (H - 0.7f) / 2f + 0.1f, z);
                for (int j = 0; j < 3; j++)
                    Add(solid, Props.Cyl(0.05f, 0.4f, 6, 0.01f), Steel, s * (W / 2f - 0.75f), 1.2f + j * 1.1f, z, 0, 0, s * 90f);
            }
            for (int j = 0; j < 9; j++) Add(solid, Props.Cyl(0.05f, 0.22f, 6, 0.01f), Steel, -3.2f + j * 0.8f, H - 1.0f, z, 0, 0, 180);
        }
        // The roller brushes: two vertical rollers against the walls at the middle of the bay.
        foreach (float s in new[] { -1f, 1f })
        {
            Add(solid, Props.Cyl(0.2f, 3.3f, 10), new Color(0.5f, 0.7f, 0.82f), s * 3.55f, 1.85f, 0.4f);
            Add(solid, Props.Cyl(0.26f, 0.15f, 10), Steel, s * 3.55f, 3.55f, 0.4f);
            Add(solid, Props.Cyl(0.26f, 0.15f, 10), Steel, s * 3.55f, 0.25f, 0.4f);
        }
        // Headers at both ends and the entrance's traffic lights, a trefoil sign by the road.
        foreach (float s in new[] { -1f, 1f })
        {
            Add(solid, Props.Box(W + 0.6f, 1.4f, 0.4f), Blue, 0, H - 0.5f, s * (L / 2f - 0.1f));
            Add(solid, Props.Box(0.5f, 1.2f, 0.5f), ConcreteDark, -W / 2f, H - 0.6f, s * (L / 2f));
            Add(solid, Props.Box(0.5f, 1.2f, 0.5f), ConcreteDark, W / 2f, H - 0.6f, s * (L / 2f));
            Add(solid, Props.Box(0.15f, 3.2f, 0.15f), Steel, -W / 2f - 1.2f, 1.6f, s * (L / 2f + 0.9f));
            Add(solid, Props.Box(0.5f, 1.0f, 0.3f), Black, -W / 2f - 1.2f, 3.1f, s * (L / 2f + 0.9f));
        }
        // Water tank on legs with a ladder, a hose reel, drums and puddles.
        Add(solid, Props.Cyl(1.8f, 4f, 12), pipeBlue, -7f, 4.6f, -3f);
        Add(solid, Props.Cyl(1.5f, 0.4f, 12, 0.2f), tin, -7f, 6.8f, -3f);
        foreach (float dx in new[] { -1.2f, 1.2f })
            foreach (float dz in new[] { -1.2f, 1.2f })
                Add(solid, Props.Box(0.25f, 2.8f, 0.25f), Steel, -7f + dx, 1.4f, -3f + dz);
        Add(solid, Props.Box(0.08f, 6f, 0.08f), Steel, -5.4f, 3.2f, -3f);
        for (int k = 0; k < 10; k++) Add(solid, Props.Box(0.08f, 0.05f, 0.5f), Steel, -5.4f, 0.6f + k * 0.55f, -3f);
        Rod(solid, new Vector3(-5.8f, 3.1f, -3f), new Vector3(-4.3f, 1.1f, -3f), 0.18f, pipeBlue);
        Add(solid, Props.Cyl(0.5f, 0.25f, 12), Red, -4.5f, 1.0f, 3.5f, 0, 0, 90);
        Add(solid, Props.Box(0.12f, 1.5f, 0.12f), Steel, -4.5f, 0.75f, 3.5f);
        for (int k = 0; k < 5; k++)
        {
            Add(solid, Props.Cyl(0.3f, 0.9f, 10), drum, -5.2f + (k % 3) * 0.75f, 0.45f, 6.3f + (k / 3) * 0.8f);
            Add(solid, Props.Cyl(0.31f, 0.06f, 10), Black, -5.2f + (k % 3) * 0.75f, 0.9f, 6.3f + (k / 3) * 0.8f);
        }
        foreach (var (x, z) in new[] { (0f, L / 2f + 1.8f), (0.3f, -L / 2f - 1.6f) })
            Add(solid, Props.Box(3.6f, 0.03f, 2.2f), wet, x, 0.02f, z);
        // The operators' room: walls, roof with an overhang, windows, door with steps and canopy, a conditioner and its pipe to the bay.
        Add(solid, Props.Box(5f, 4f, 6f), Concrete, W / 2f + 3.2f, 2f, 3f);
        Add(solid, Props.Box(5.7f, 0.3f, 6.7f), Blue, W / 2f + 3.2f, 4.15f, 3f);
        Add(solid, Props.Box(5.8f, 0.15f, 6.8f), ConcreteDark, W / 2f + 3.2f, 4.35f, 3f);
        Add(solid, Props.Box(5.1f, 0.5f, 6.1f), ConcreteStain, W / 2f + 3.2f, 0.25f, 3f);
        foreach (float z in new[] { 1f, 2.8f, 4.6f })
        {
            Add(solid, Props.Box(0.12f, 1.2f, 1.4f), ConcreteDark, W / 2f + 0.65f, 2.4f, z);
            Add(solid, Props.Box(0.1f, 0.95f, 1.15f), Window, W / 2f + 0.6f, 2.4f, z);
        }
        foreach (float x in new[] { 8.5f, 10.5f })
        {
            Add(solid, Props.Box(1.2f, 1.2f, 0.12f), ConcreteDark, x, 2.4f, 6.06f);
            Add(solid, Props.Box(0.95f, 0.95f, 0.1f), Window, x, 2.4f, 6.1f);
        }
        Add(solid, Props.Box(1.2f, 2.3f, 0.12f), Rust, 9.5f, 1.15f, 6.05f);                               // the door
        Add(solid, Props.Box(1.8f, 0.2f, 1.0f), Blue, 9.5f, 2.75f, 6.5f);
        Add(solid, Props.Box(1.6f, 0.18f, 0.5f), ConcreteDark, 9.5f, 0.09f, 6.45f);
        Add(solid, Props.Box(1.3f, 0.9f, 0.9f), Steel, 11.4f, 4.9f, 1.5f);
        Add(solid, Props.Cyl(0.35f, 0.1f, 10), Black, 11.4f, 5.4f, 1.5f);
        Add(solid, Props.Cyl(0.18f, 3.4f, 8), pipe, 5.9f, 3.1f, 2f, 0, 0, 90);                             // a pipe from the room into the bay wall
        Merge(n, solid);
        n.AddChild(glow);
        // Signs, lamps: the name at both ends, a trefoil, lit panes in the operators' room, traffic lights (red: stop at the entrance), ceiling lamps.
        foreach (float z in new[] { -L / 2f, L / 2f })
            n.AddChild(Buildings.TextPanel("ДЕЗАКТИВАЦИЯ", W, 1.1f, new Vector3(0, H - 0.4f, z * 1.01f), Blue, White));
        n.AddChild(NightGlow.Part(Props.Box(0.9f, 0.9f, 0.1f), Window, WindowGlow, 1.2f, new Vector3(W / 2f + 0.55f, 2.4f, 2.8f)));
        n.AddChild(NightGlow.Part(Props.Box(0.95f, 0.95f, 0.1f), Window, WindowGlow, 1.2f, new Vector3(8.5f, 2.4f, 6.14f)));
        foreach (float s in new[] { -1f, 1f })
        {
            n.AddChild(NightGlow.Part(Props.Ball(0.17f, 6), new Color(0.5f, 0.05f, 0.04f), new Color(1f, 0.1f, 0.05f), 3f, new Vector3(-W / 2f - 1.2f, 3.35f, s * (L / 2f + 1.07f))));
            n.AddChild(NightGlow.Part(Props.Ball(0.17f, 6), new Color(0.05f, 0.35f, 0.1f), new Color(0.2f, 1f, 0.3f), 2f, new Vector3(-W / 2f - 1.2f, 2.9f, s * (L / 2f + 1.07f))));
        }
        foreach (float z in new[] { -4.5f, 0f, 4.5f })
            n.AddChild(NightGlow.Part(Props.Box(0.9f, 0.14f, 0.4f), Steel, new Color(1f, 0.95f, 0.8f), 1.8f, new Vector3(0, H - 0.5f, z)));
        var sign = RadSign(null, new Vector3(-W / 2f - 2.4f, 0, -L / 2f - 1.5f));
        n.AddChild(sign);
        return n;
    }

    // ------------------------------------------------------------ the vehicle graveyard

    /// <summary>
    /// The vehicle graveyard's rusting fleet, by kind: 0 a forward-control lorry (z -4.3 .. 4.1), 1 a bus (10 m), 2 an armoured carrier (7 m, 2.8
    /// wide). Front is -Z. Each glows a little green after dark (it served at block 4); the details are one merged mesh.
    /// </summary>
    public static Node3D Wreck(int kind)
    {
        var n = new Node3D();
        var solid = new Node3D();
        Color rubber = Black, rim = new(0.4f, 0.28f, 0.2f), glass = new(0.1f, 0.12f, 0.13f), pale = new(0.72f, 0.7f, 0.62f), seat = new(0.3f, 0.2f, 0.12f);
        switch (kind)
        {
            case 0:
                LorryWreck(n, solid, rubber, rim, glass, pale);
                break;
            case 1:
                BusWreck(n, solid, rubber, rim, glass, pale, seat);
                break;
            default:
                CarrierWreck(n, solid, rubber, rim, pale);
                break;
        }
        Merge(n, solid);
        return n;
    }

    static void WheelWithRim(Node3D solid, float x, float z, float r, float w, Color tyre, Color rim, bool flat = false)
    {
        Add(solid, Props.Cyl(r, w, 10), tyre, x, flat ? r * 0.82f : r, z, 0, 0, 90);
        Add(solid, Props.Cyl(r * 0.62f, w + 0.04f, 8), rim, x, flat ? r * 0.82f : r, z, 0, 0, 90);
        Add(solid, Props.Cyl(r * 0.22f, w + 0.1f, 6), Steel, x, flat ? r * 0.82f : r, z, 0, 0, 90);
    }

    static void LorryWreck(Node3D n, Node3D solid, Color rubber, Color rim, Color glass, Color pale)
    {
        // Cab (glowing a little), its windscreen frame with the glass gone, doors, grille, lamps, bumper, mirrors, steps.
        n.AddChild(NightGlow.Part(Props.Box(2.4f, 2f, 2.2f), Rust, HotGlow, 0.3f, new Vector3(0, 1.6f, -3.2f)));
        Add(solid, Props.Box(2.3f, 0.12f, 2.1f), RustDark, 0, 2.66f, -3.2f);
        Add(solid, Props.Box(2.3f, 0.7f, 0.06f), glass, 0, 2.2f, -4.33f);
        foreach (float x in new[] { -1.1f, 0f, 1.1f }) Add(solid, Props.Box(0.08f, 0.8f, 0.1f), RustDark, x, 2.2f, -4.34f);
        foreach (float s in new[] { -1f, 1f })
        {
            Add(solid, Props.Box(0.05f, 1.2f, 1.05f), RustDark, s * 1.22f, 1.55f, -3.1f);
            Add(solid, Props.Box(0.06f, 0.55f, 0.8f), glass, s * 1.23f, 2.0f, -3.1f);
            Add(solid, Props.Box(0.08f, 0.06f, 0.2f), pale, s * 1.26f, 1.5f, -2.8f);
            Rod(solid, new Vector3(s * 1.2f, 2.2f, -4.1f), new Vector3(s * 1.55f, 2.4f, -4.25f), 0.04f, Black);
            Add(solid, Props.Box(0.05f, 0.4f, 0.22f), Steel, s * 1.58f, 2.4f, -4.25f, 0, 0, s * 20f);
            Add(solid, Props.Box(0.3f, 0.06f, 0.5f), Steel, s * 1.4f, 0.55f, -3.4f);
        }
        Add(solid, Props.Box(1.9f, 0.9f, 0.1f), Black, 0, 1.0f, -4.33f);                                  // the grille opening
        for (int k = 0; k < 6; k++) Add(solid, Props.Box(0.05f, 0.8f, 0.04f), Steel, -0.8f + k * 0.32f, 1.0f, -4.4f);
        Add(solid, Props.Box(2.5f, 0.28f, 0.3f), Steel, 0, 0.55f, -4.5f, 0, 8, 0);
        Add(solid, Props.Cyl(0.15f, 0.1f, 8), new Color(0.8f, 0.75f, 0.5f), 0.9f, 1.55f, -4.4f, 90, 0, 0);
        Add(solid, Props.Cyl(0.15f, 0.06f, 6), Black, -0.9f, 1.55f, -4.4f, 90, 0, 0);
        // The frame, the flat bed with its boards (some rotten), stakes, the tailgate hanging, a fuel tank, exhaust, spare wheel, tail lamps.
        Add(solid, Props.Box(1.2f, 0.3f, 8.2f), RustDark, 0, 0.85f, 0);
        Add(solid, Props.Box(2.5f, 0.5f, 6f), RustDark, 0, 1.1f, 1f);
        for (int k = 0; k < 10; k++) Add(solid, Props.Box(0.15f, 0.12f, 6f), k % 3 == 0 ? Rust : new Color(0.3f, 0.22f, 0.15f), -1.15f + k * 0.255f, 1.4f, 1f);
        foreach (float s in new[] { -1f, 1f })
        {
            for (int k = 0; k < 5; k++) Add(solid, Props.Box(0.06f, 0.5f, 1f), k == 2 ? RustDark : new Color(0.33f, 0.24f, 0.16f), s * 1.27f, 1.65f + (k == 3 ? -0.1f : 0f), -1.5f + k * 1.2f);
            for (int k = 0; k < 4; k++) Add(solid, Props.Box(0.1f, 1.2f, 0.12f), Steel, s * 1.28f, 1.95f, -1.5f + k * 2.2f);
        }
        Add(solid, Props.Box(2.5f, 1.2f, 0.2f), Rust, 0, 1.8f, 4f);
        Add(solid, Props.Box(2.4f, 0.12f, 1.4f), Rust, 0, 0.7f, 4.4f, 55, 0, 0);                           // the tailgate dropped on its chains
        Add(solid, Props.Cyl(0.36f, 1.2f, 10), RustDark, 1.0f, 0.55f, -0.6f, 90, 0, 0);
        Add(solid, Props.Cyl(0.06f, 1.4f, 6), Black, -1.0f, 1.8f, -1.9f);
        foreach (float s in new[] { -1f, 1f }) Add(solid, Props.Box(0.25f, 0.14f, 0.05f), new Color(0.6f, 0.08f, 0.06f), s * 1.0f, 1.0f, 4.1f);
        // Wheels: three present (one flat), one gone with the axle stub left.
        WheelWithRim(solid, -1.2f, -2.5f, 0.5f, 0.4f, rubber, rim);
        WheelWithRim(solid, 1.2f, -2.5f, 0.5f, 0.4f, rubber, rim, true);
        WheelWithRim(solid, -1.2f, 2.5f, 0.5f, 0.4f, rubber, rim);
        Add(solid, Props.Cyl(0.12f, 0.5f, 6), Steel, 1.1f, 0.5f, 2.5f, 0, 0, 90);
        Add(solid, Props.Cyl(0.5f, 0.35f, 10), rubber, 1.9f, 0.18f, 3.2f, 90, 0, 0);                     // the wheel that fell off
    }

    static void BusWreck(Node3D n, Node3D solid, Color rubber, Color rim, Color glass, Color pale, Color seat)
    {
        n.AddChild(NightGlow.Part(Props.Box(2.5f, 2.6f, 10f), new Color(0.5f, 0.38f, 0.24f), HotGlow, 0.25f, new Vector3(0, 1.7f, 0)));
        // The window band: dark openings between pillars on both sides, the frames, some glass left.
        foreach (float s in new[] { -1f, 1f })
        {
            Add(solid, Props.Box(0.05f, 0.85f, 9.4f), glass, s * 1.27f, 2.2f, 0);
            for (int k = 0; k <= 8; k++) Add(solid, Props.Box(0.08f, 1.0f, 0.12f), new Color(0.42f, 0.32f, 0.2f), s * 1.28f, 2.2f, -4.6f + k * 1.15f);
            Add(solid, Props.Box(0.08f, 0.1f, 9.6f), new Color(0.42f, 0.32f, 0.2f), s * 1.28f, 1.7f, 0);
            Add(solid, Props.Box(0.08f, 0.1f, 9.6f), new Color(0.42f, 0.32f, 0.2f), s * 1.28f, 2.7f, 0);
            for (int k = 0; k < 3; k++) Add(solid, Props.Box(0.05f, 0.45f, 0.5f), pale * 0.6f, s * 1.285f, 2.2f, -2.4f + k * 2.9f + (s > 0 ? 0.7f : 0f));       // shards still in the frame
            for (int k = 0; k < 3; k++) Add(solid, Props.Box(0.04f, 0.5f, 1.3f), k == 1 ? RustDark : Rust, s * 1.27f, 0.95f, -3.2f + k * 3.2f);    // rust patches on the skirt
            foreach (float z in new[] { -3.2f, 3.2f }) Add(solid, Props.Box(0.12f, 0.55f, 1.2f), Black, s * 1.26f, 0.55f, z);                      // the wheel arches
            Rod(solid, new Vector3(s * 1.3f, 2.5f, -4.4f), new Vector3(s * 1.7f, 2.7f, -4.6f), 0.04f, Black);
            Add(solid, Props.Box(0.05f, 0.4f, 0.22f), Steel, s * 1.72f, 2.7f, -4.6f);
        }
        // The seats inside, seen through the windows.
        for (int k = 0; k < 5; k++)
            foreach (float s in new[] { -1f, 1f })
            {
                Add(solid, Props.Box(0.6f, 0.3f, 0.8f), seat, s * 0.7f, 1.25f, -3.2f + k * 1.6f);
                Add(solid, Props.Box(0.6f, 0.6f, 0.1f), seat, s * 0.7f, 1.7f, -3.55f + k * 1.6f);
            }
        // Roof: ribs, a hatch, a hole where it collapsed; the front: windscreen, a destination board, lamps, a wiper; the rear.
        for (int k = 0; k < 8; k++) Add(solid, Props.Box(2.55f, 0.08f, 0.14f), RustDark, 0, 3.02f, -4.2f + k * 1.2f);
        Add(solid, Props.Box(0.9f, 0.12f, 0.9f), Steel, 0, 3.05f, -1.0f);
        Add(solid, Props.Box(1.1f, 0.05f, 1.4f), Black, 0.2f, 3.04f, 2.4f);
        Add(solid, Props.Box(2.3f, 0.95f, 0.06f), glass, 0, 2.25f, -5.02f);
        Add(solid, Props.Box(0.08f, 1.0f, 0.1f), RustDark, 0, 2.25f, -5.04f);
        Add(solid, Props.Box(1.6f, 0.4f, 0.05f), Black, 0, 2.95f, -5.04f);
        Add(solid, Props.Box(2.6f, 0.2f, 0.25f), Steel, 0, 0.45f, -5.1f);
        foreach (float s in new[] { -1f, 1f })
        {
            Add(solid, Props.Cyl(0.18f, 0.08f, 8), new Color(0.8f, 0.75f, 0.5f), s * 0.85f, 0.95f, -5.06f, 90, 0, 0);
            Add(solid, Props.Box(0.035f, 0.035f, 0.5f), Black, s * 0.4f, 1.8f, -5.07f, -12, 0, 0);
            Add(solid, Props.Box(0.22f, 0.14f, 0.05f), new Color(0.6f, 0.08f, 0.06f), s * 1.0f, 1.1f, 5.03f);
            Add(solid, Props.Box(0.04f, 1.9f, 0.55f), Rust, s * 1.28f, 1.3f, -4.3f, 0, s * 70f, 0);          // the folding door, standing open
        }
        Add(solid, Props.Box(1.4f, 1.0f, 0.1f), RustDark, 0, 1.4f, 5.04f);                                 // the engine hatch at the back
        Add(solid, Props.Cyl(0.08f, 1.6f, 6), Black, 0.9f, 0.35f, 5.4f, 90, 0, 0);                         // the exhaust pipe, hanging
        // Wheels: one axle sagging (a wheel missing), the others on rims.
        WheelWithRim(solid, -1.2f, -3.2f, 0.52f, 0.4f, rubber, rim);
        WheelWithRim(solid, 1.2f, -3.2f, 0.52f, 0.4f, rubber, rim);
        WheelWithRim(solid, -1.2f, 3.2f, 0.52f, 0.4f, rubber, rim, true);
        Add(solid, Props.Cyl(0.12f, 0.5f, 6), Steel, 1.1f, 0.52f, 3.2f, 0, 0, 90);
        Add(solid, Props.Cyl(0.52f, 0.36f, 10), rubber, 2.1f, 0.18f, 4.3f, 90, 0, 0);
    }

    static void CarrierWreck(Node3D n, Node3D solid, Color rubber, Color rim, Color pale)
    {
        n.AddChild(NightGlow.Part(Props.Box(2.8f, 1.6f, 7f), new Color(0.32f, 0.33f, 0.24f), HotGlow, 0.3f, new Vector3(0, 1.4f, 0)));
        Color hull = new(0.3f, 0.31f, 0.23f);
        // The sloped front plates, the roof deck and the turret ring with a hatch, the machine gun and its mount, the driver's hatch.
        Add(solid, Props.Box(2.6f, 0.2f, 2.0f), hull, 0, 2.05f, -3.3f, 28, 0, 0);
        Add(solid, Props.Box(2.5f, 0.2f, 1.6f), hull * 0.9f, 0, 1.2f, -4.0f, -35, 0, 0);
        Add(solid, Props.Box(2.4f, 0.2f, 3.6f), hull, 0, 2.28f, 0.8f);
        Add(solid, Props.Cyl(0.85f, 0.5f, 12), hull * 1.1f, 0, 2.62f, -0.8f);
        Add(solid, Props.Cyl(0.6f, 0.1f, 10), RustDark, -0.15f, 2.92f, -0.8f);
        Add(solid, Props.Cyl(0.06f, 1.7f, 6), Black, 0.1f, 2.7f, -1.9f, 90, 0, 0);
        Add(solid, Props.Box(0.3f, 0.3f, 0.5f), Black, 0.1f, 2.7f, -1.0f);
        Add(solid, Props.Cyl(0.35f, 0.12f, 8), RustDark, 0.65f, 2.4f, -2.6f, 0, 0, 6);
        // Vision slits, side hatches, seams, bullet holes, tow hooks, the exhaust box and a broken antenna mast at the back.
        foreach (float s in new[] { -1f, 1f })
        {
            for (int k = 0; k < 3; k++) Add(solid, Props.Box(0.04f, 0.14f, 0.5f), Black, s * 1.41f, 1.8f, -2.2f + k * 0.9f);
            Add(solid, Props.Box(0.05f, 1.0f, 0.8f), RustDark, s * 1.41f, 1.2f, 1.2f);
            Add(solid, Props.Box(0.06f, 0.08f, 0.2f), pale, s * 1.44f, 1.3f, 1.4f);
            for (int k = 0; k < 5; k++) Add(solid, Props.Box(0.03f, 0.06f, 0.06f), Black, s * 1.41f, 0.9f + (k % 3) * 0.2f, -0.5f + k * 0.4f);
            Add(solid, Props.Box(0.08f, 0.3f, 0.3f), Steel, s * 0.8f, 0.7f, -3.6f);
            Add(solid, Props.Cyl(0.16f, 0.08f, 8), new Color(0.75f, 0.7f, 0.5f), s * 1.0f, 1.5f, -3.55f, 90, 0, 0);
            Add(solid, Props.Box(0.04f, 0.5f, 7f), RustDark, s * 1.41f, 0.8f, 0);
        }
        Add(solid, Props.Box(1.2f, 0.8f, 0.8f), Black, 0, 1.1f, 3.8f);
        Rod(solid, new Vector3(1.0f, 2.45f, 2.6f), new Vector3(1.4f, 4.1f, 3.0f), 0.04f, Black);
        // Eight wheels (four a side) on rims: a couple flat.
        foreach (float s in new[] { -1f, 1f })
            for (int k = 0; k < 4; k++)
                WheelWithRim(solid, s * 1.5f, -2.7f + k * 1.8f, 0.52f, 0.42f, rubber, rim, k == 2 && s > 0);
    }
}
