using System;
using Godot;

namespace CCCPRacing.Track;

/// <summary>
/// ATOMGRAD-4's power plant, the three big landmarks seen from far: the turbine hall, the reactor blocks and block 4 under its
/// sarcophagus. Same idiom as the other tracks' landmarks (RESEARCH: skills/cccp-objects): big masses first, then the trim
/// (plinths, cornices, pilasters, window bays with broken and lit panes, doors, roof monitors, vents, pipe racks, ladders, signs),
/// the stationary parts merged into one mesh, the glowing windows, lamps and signs kept as their own nodes.
/// </summary>
public static partial class Atomgrad
{
    static void Add(Node3D n, Mesh m, Color c, float x, float y, float z, float rx = 0, float ry = 0, float rz = 0) => n.AddChild(P(m, c, x, y, z, rx, ry, rz));

    /// <summary>A thin straight bar from a to b.</summary>
    static void Rod(Node3D n, Vector3 a, Vector3 b, float w, Color c)
    {
        Vector3 d = b - a;
        var part = Props.Part(Props.Box(w, w, d.Length()), c, (a + b) / 2f);
        part.Basis = Basis.LookingAt(d.Normalized(), MathF.Abs(d.Normalized().Y) > 0.99f ? Vector3.Right : Vector3.Up);
        n.AddChild(part);
    }

    /// <summary>
    /// A grid of window bays on a face parallel to X at z (side +1 looks towards +Z, -1 towards -Z): a frame, a pane
    /// (some broken and dark), a mullion; a few panes light up at night (they go into glow, not the merged mesh).
    /// </summary>
    static void WindowBays(Node3D solid, Node3D glow, float x0, float step, int cols, float y0, float rowStep, int rows, float w, float h, float z, float side, int seed, float litShare, float brokenShare)
    {
        var rng = new RandomNumberGenerator { Seed = (ulong)seed };
        for (int r = 0; r < rows; r++)
        for (int c = 0; c < cols; c++)
        {
            float x = x0 + c * step, y = y0 + r * rowStep;
            Add(solid, Props.Box(w + 0.3f, h + 0.3f, 0.12f), ConcreteDark, x, y, z + side * 0.03f);
            float roll = rng.Randf();
            var pane = Props.Box(w, h, 0.06f);
            var at = new Vector3(x, y, z + side * 0.1f);
            if (roll < litShare) glow.AddChild(NightGlow.Part(pane, Window, WindowGlow, 1.1f, at));
            else solid.AddChild(Props.Part(pane, roll < litShare + brokenShare ? WindowBroken : Window, at));
            Add(solid, Props.Box(0.07f, h, 0.1f), ConcreteDark, x, y, z + side * 0.13f);
            Add(solid, Props.Box(w + 0.5f, 0.12f, 0.3f), Concrete, x, y - h / 2f - 0.15f, z + side * 0.12f);        // the sill
        }
    }

    static void Merge(Node3D n, Node3D solid)
    {
        DrunkTractor.BatchStatic(solid, null);
        n.AddChild(solid);
    }

    // ------------------------------------------------------------ the turbine hall

    /// <summary>
    /// The turbine hall, 80 x 22 m, 18 m high, along X, front (the slogan) on +Z: plinth, cornice and belt course, pilasters with
    /// bases and caps, two rows of window bays on both long faces, a roller door and a staff door with canopy and steps on the
    /// front, two glazed roof monitors with a ventilator row and a long roof duct, pipe racks along both walls, big pipes through
    /// the end walls, floodlights on the cornice and the slogan.
    /// </summary>
    public static Node3D TurbineHall()
    {
        var n = new Node3D();
        var solid = new Node3D();
        var glow = new Node3D();
        const float L = 80f, D = 22f, H = 18f;
        Color pipe = new(0.58f, 0.59f, 0.56f), tin = new(0.4f, 0.42f, 0.43f);
        Add(solid, Props.Box(L, H, D), Concrete, 0, H / 2f, 0);
        Add(solid, Props.Box(L + 1f, 1f, D + 1f), ConcreteDark, 0, 0.5f, 0);
        Add(solid, Props.Box(L + 0.6f, 0.7f, D + 0.6f), ConcreteDark, 0, H + 0.3f, 0);
        Add(solid, Props.Box(L + 0.3f, 0.4f, D + 0.3f), ConcreteStain, 0, 10.5f, 0);
        Add(solid, Props.Box(L + 0.3f, 0.4f, D + 0.3f), ConcreteStain, 0, 3.6f, 0);
        for (int k = 0; k <= 16; k++)
        {
            float x = -L / 2f + k * L / 16f;
            foreach (float s in new[] { -1f, 1f })
            {
                Add(solid, Props.Box(0.6f, H - 0.5f, 0.5f), ConcreteDark, x, H / 2f, s * (D / 2f + 0.2f));
                Add(solid, Props.Box(0.95f, 0.5f, 0.8f), ConcreteStain, x, 0.75f, s * (D / 2f + 0.3f));
                Add(solid, Props.Box(0.85f, 0.35f, 0.7f), ConcreteStain, x, H - 0.4f, s * (D / 2f + 0.28f));
            }
        }
        // Window bays between the pilasters: two rows on each long face (the slogan fills the band between them on the front).
        foreach (float side in new[] { -1f, 1f })
            WindowBays(solid, glow, -L / 2f + L / 32f, L / 16f, 16, 14.4f, -7.0f, 2, 3.6f, 2.5f, side * (D / 2f + 0.02f), side, side > 0 ? 1917 : 1986, 0.05f, 0.22f);
        // The front: a roller door and a staff door with a canopy, steps and lamps; a drainpipe at each end.
        Add(solid, Props.Box(9.4f, 6.6f, 0.2f), ConcreteDark, -18f, 3.3f, D / 2f + 0.1f);
        Add(solid, Props.Box(8.8f, 6.1f, 0.18f), tin, -18f, 3.05f, D / 2f + 0.2f);
        for (int k = 0; k < 11; k++) Add(solid, Props.Box(8.8f, 0.07f, 0.22f), ConcreteStain, -18f, 0.6f + k * 0.55f, D / 2f + 0.3f);
        for (int k = 0; k < 8; k++) Add(solid, Props.Box(0.6f, 0.45f, 0.24f), k % 2 == 0 ? Yellow : Black, -21.7f + k * 1.05f, 0.35f, D / 2f + 0.32f);
        Add(solid, Props.Box(1.4f, 2.4f, 0.14f), Rust, 12f, 1.2f, D / 2f + 0.1f);
        Add(solid, Props.Box(1.9f, 0.18f, 1.1f), ConcreteDark, 12f, 2.9f, D / 2f + 0.7f);
        Add(solid, Props.Box(1.8f, 0.2f, 0.7f), ConcreteDark, 12f, 0.1f, D / 2f + 0.9f);
        foreach (float x in new[] { -L / 2f + 0.6f, L / 2f - 0.6f }) Add(solid, Props.Cyl(0.15f, H, 6), steelPipe(), x, H / 2f, D / 2f + 0.65f);
        // Roof: two glazed monitors, a duct between them on supports, a row of ventilators and the access hatch.
        foreach (float z in new[] { -5.5f, 5.5f })
        {
            Add(solid, Props.Box(64f, 2.1f, 5f), ConcreteDark, 0, H + 1.35f, z);
            Add(solid, Props.Box(62f, 0.9f, 5.1f), Glass, 0, H + 1.55f, z);
            Add(solid, Props.Box(64.6f, 0.2f, 5.8f), tin, 0, H + 2.5f, z);
        }
        Add(solid, Props.Cyl(0.6f, 74f, 10), pipe, 0, H + 1.5f, 0, 0, 0, 90);
        for (int k = 0; k < 10; k++)
        {
            float x = -33f + k * 7.3f;
            Add(solid, Props.Box(0.35f, 1.0f, 0.35f), Steel, x, H + 1.0f, 0);
            Add(solid, Props.Cyl(0.68f, 0.18f, 10), ConcreteStain, x, H + 1.5f, 0, 90, 0, 0);
        }
        for (int k = 0; k < 6; k++)
        {
            float x = -28f + k * 11f;
            Add(solid, Props.Cyl(0.85f, 1.3f, 10), tin, x, H + 1.1f, -9.2f);
            Add(solid, Props.Cyl(1.2f, 0.4f, 10, 0.2f), Rust, x, H + 2.0f, -9.2f);
        }
        foreach (float x in new[] { -33f, 33f }) Add(solid, Props.Cyl(0.7f, 5f, 8, 0.55f), Rust, x, H + 2.8f, 0);
        Add(solid, Props.Box(2.2f, 1.6f, 2.2f), ConcreteDark, 30f, H + 1.2f, 7.4f);
        // Pipe racks along both long walls (two pipes each on trestles) and big pipes through the end walls.
        foreach (float s in new[] { -1f, 1f })
        {
            Add(solid, Props.Cyl(0.38f, 74f, 10), pipe, 0, 4.4f, s * (D / 2f + 1.7f), 0, 0, 90);
            Add(solid, Props.Cyl(0.28f, 74f, 10), Rust, 0, 6.0f, s * (D / 2f + 1.7f), 0, 0, 90);
            for (int k = 0; k < 10; k++)
            {
                float x = -33f + k * 7.3f;
                Add(solid, Props.Box(0.22f, 6.2f, 0.22f), Steel, x, 3.1f, s * (D / 2f + 1.7f));
                Add(solid, Props.Box(0.22f, 0.22f, 1.2f), Steel, x, 6.2f, s * (D / 2f + 1.7f));
                Add(solid, Props.Cyl(0.46f, 0.1f, 10), Steel, x + 3.65f, 4.4f, s * (D / 2f + 1.7f), 0, 0, 90);
            }
        }
        foreach (float s in new[] { -1f, 1f })
        {
            Add(solid, Props.Cyl(1.0f, 7f, 12), pipe, s * (L / 2f + 3f), 6.5f, 3f, 0, 0, 90);
            Add(solid, Props.Cyl(1.15f, 0.3f, 12), Steel, s * (L / 2f + 0.4f), 6.5f, 3f, 0, 0, 90);
            Add(solid, Props.Box(0.5f, 6.5f, 0.5f), Steel, s * (L / 2f + 5.5f), 3.25f, 3f);
            Add(solid, Props.Box(0.9f, 1.2f, 4f), ConcreteDark, s * (L / 2f + 0.5f), 0.6f, -4f);
        }
        Merge(n, solid);
        n.AddChild(glow);
        n.AddChild(Buildings.TextPanel("СЛАВА ЭНЕРГЕТИКАМ!", 22f, 2.2f, new Vector3(0, H - 7f, D / 2f + 0.5f), Red, Gold));
        for (int k = 0; k < 6; k++)         // floodlights on the cornice
            n.AddChild(NightGlow.Part(Props.Box(0.7f, 0.28f, 0.45f), Steel, new Color(1f, 0.92f, 0.7f), 1.8f, new Vector3(-26f + k * 10.4f, H + 0.95f, D / 2f + 0.15f)));
        n.AddChild(NightGlow.Part(Props.Box(0.5f, 0.2f, 0.3f), Steel, new Color(1f, 0.85f, 0.5f), 1.6f, new Vector3(12f, 3.0f, D / 2f + 1.3f)));
        return n;
    }

    static Color steelPipe() => new(0.5f, 0.5f, 0.48f);

    // ------------------------------------------------------------ a reactor block

    /// <summary>
    /// A reactor block (main part 24 x 28 m, 30 m high, a stepped upper part, the annex 30 x 8 m towards +Z): plinth, belt courses and
    /// ribs, window bays on both faces, louvres on the upper part, a gantry crane on the roof with ventilators and a stack, a railed
    /// loading door in the annex with hazard stripes and its rails, a vertical steam pipe down the wall, transformers and tanks at the foot,
    /// and the block's number (Text) on the wall.
    /// </summary>
    public static Node3D ReactorBlock(string number = null)
    {
        var n = new Node3D();
        var solid = new Node3D();
        var glow = new Node3D();
        Color pipe = new(0.58f, 0.59f, 0.56f), tin = new(0.4f, 0.42f, 0.43f), hazard = Yellow;
        Add(solid, Props.Box(24f, 30f, 28f), Concrete, 0, 15f, 0);
        Add(solid, Props.Box(18f, 6f, 20f), ConcreteDark, 0, 33f, 0);
        Add(solid, Props.Box(30f, 12f, 8f), Concrete, 0, 6f, 17f);
        Add(solid, Props.Box(25f, 1.2f, 29f), ConcreteStain, 0, 0.6f, 0);
        foreach (float y in new[] { 9.5f, 19.5f, 29.4f }) Add(solid, Props.Box(24.4f, 0.5f, 28.4f), ConcreteStain, 0, y, 0);
        Add(solid, Props.Box(18.5f, 0.5f, 20.5f), ConcreteStain, 0, 36.2f, 0);
        Add(solid, Props.Box(30.4f, 0.5f, 8.4f), ConcreteDark, 0, 12.2f, 17f);
        for (int k = -3; k <= 3; k++)       // ribs on the long faces
            foreach (float s in new[] { -1f, 1f })
                Add(solid, Props.Box(0.5f, 29f, 0.5f), ConcreteDark, k * 3.4f, 14.5f, s * 14.2f);
        // Window bays on the front (+Z, above the annex) and the back, and a few on the end walls.
        WindowBays(solid, glow, -9.6f, 3.2f, 7, 16f, 4.0f, 3, 1.8f, 2.0f, 14.1f, 1f, 1986, 0.06f, 0.25f);
        WindowBays(solid, glow, -9.6f, 3.2f, 7, 4f, 4.5f, 6, 1.8f, 2.0f, -14.1f, -1f, 1987, 0.06f, 0.25f);
        // The upper part: louvres on the long sides and a safety rail round its roof.
        foreach (float s in new[] { -1f, 1f })
        for (int k = 0; k < 8; k++)
            Add(solid, Props.Box(1.6f, 0.12f, 0.15f), Black, -7f + k * 2f, 31f + (k % 2) * 0.0f, s * 10.1f);
        foreach (float y in new[] { 36.6f, 37.2f })
        {
            Add(solid, Props.Box(18.2f, 0.07f, 0.07f), Steel, 0, y, 9.9f);
            Add(solid, Props.Box(18.2f, 0.07f, 0.07f), Steel, 0, y, -9.9f);
            Add(solid, Props.Box(0.07f, 0.07f, 19.8f), Steel, 8.9f, y, 0);
            Add(solid, Props.Box(0.07f, 0.07f, 19.8f), Steel, -8.9f, y, 0);
        }
        // Roof of the upper part: a gantry crane on two rails, a hatch, ventilators and a stack banded red and white.
        foreach (float z in new[] { -6f, 6f }) Add(solid, Props.Box(17f, 0.3f, 0.4f), Steel, 0, 36.6f, z);
        Add(solid, Props.Box(0.5f, 0.6f, 14f), Yellow, 3f, 37.1f, 0);
        Add(solid, Props.Box(1.6f, 1.2f, 1.6f), Yellow * 0.85f, 3f, 37.9f, 2f);
        Add(solid, Props.Box(0.08f, 3f, 0.08f), Black, 3f, 36.0f, 2f);
        Add(solid, Props.Box(0.5f, 0.5f, 0.5f), Steel, 3f, 34.4f, 2f);
        Add(solid, Props.Box(2.4f, 0.6f, 2.4f), ConcreteDark, -5f, 36.9f, 5f);
        foreach (float x in new[] { -3f, 0f }) Add(solid, Props.Cyl(0.8f, 1.2f, 10), tin, x - 3f, 37.2f, -6f);
        for (int k = 0; k < 6; k++)
            Add(solid, Props.Cyl(1.3f - k * 0.0f, 2.2f, 12), k % 2 == 0 ? Red : White, -6f, 37.3f + (k + 0.5f) * 2.2f, -2f);
        Add(solid, Props.Box(0.5f, 0.5f, 0.5f), Red, -6f, 51f, -2f);
        // A vertical steam pipe down the side wall with brackets, and a horizontal run to the annex.
        Add(solid, Props.Cyl(0.8f, 28f, 10), pipe, 12.9f, 14f, 8f);
        for (int k = 0; k < 7; k++) Add(solid, Props.Box(0.6f, 0.3f, 1.4f), Steel, 12.6f, 3f + k * 4f, 8f);
        Add(solid, Props.Cyl(0.7f, 9f, 10), Rust, 14f, 11f, 12f, 90, 0, 0);
        Add(solid, Props.Cyl(0.9f, 0.3f, 10), Steel, 12.9f, 28f, 8f);
        // The annex: parapet, a loading door with hazard stripes, rails coming out of it, a canopy, a ramp, a pair of lamps.
        Add(solid, Props.Box(30.4f, 0.6f, 8.4f), ConcreteDark, 0, 12.4f, 17f);
        Add(solid, Props.Box(8.4f, 7.6f, 0.3f), ConcreteDark, 0, 3.8f, 21.1f);
        Add(solid, Props.Box(7.6f, 7.0f, 0.2f), tin * 0.8f, 0, 3.5f, 21.25f);
        for (int k = 0; k < 12; k++) Add(solid, Props.Box(7.6f, 0.07f, 0.24f), ConcreteStain, 0, 0.6f + k * 0.55f, 21.35f);
        for (int k = 0; k < 8; k++) Add(solid, Props.Box(0.9f, 0.5f, 0.26f), k % 2 == 0 ? hazard : Black, -3.5f + k * 1.0f, 0.4f, 21.4f);
        Add(solid, Props.Box(9f, 0.3f, 2f), ConcreteDark, 0, 8.0f, 22.1f);
        foreach (float x in new[] { -3.6f, 3.6f }) Add(solid, Props.Box(0.2f, 6.5f, 0.2f), Steel, x, 3.25f, 22.9f);
        foreach (float x in new[] { -0.76f, 0.76f }) Add(solid, Props.Box(0.12f, 0.16f, 14f), Steel, x, 0.12f, 28f);
        for (int k = 0; k < 9; k++) Add(solid, Props.Box(2.6f, 0.12f, 0.3f), new Color(0.26f, 0.2f, 0.15f), 0, 0.06f, 22f + k * 1.6f);
        Add(solid, Props.Box(10f, 0.3f, 3f), ConcreteDark, 0, 0.15f, 23f);
        // Transformers and tanks at the foot of the annex: a box on legs with a row of insulators, and two tanks on trestles.
        foreach (float x in new[] { -12f, 12f })
        {
            Add(solid, Props.Box(4.2f, 3.2f, 3f), tin, x, 2.0f, 24f);
            Add(solid, Props.Box(4.6f, 0.3f, 3.4f), ConcreteDark, x, 0.4f, 24f);
            for (int k = 0; k < 4; k++) Add(solid, Props.Cyl(0.16f, 0.9f, 6), White, x - 1.5f + k * 1.0f, 4.0f, 24f);
            Add(solid, Props.Cyl(0.5f, 0.2f, 8), Steel, x, 0.3f, 22.2f);
            Add(solid, Props.Cyl(1.5f, 6f, 12), pipe, x * 1.3f, 1.7f, 31f, 0, 0, 90);
            Add(solid, Props.Box(0.3f, 1.6f, 1.6f), Steel, x * 1.3f - 2f, 0.8f, 31f);
            Add(solid, Props.Box(0.3f, 1.6f, 1.6f), Steel, x * 1.3f + 2f, 0.8f, 31f);
        }
        // Rust streaks down the wall under the cornices.
        for (int k = 0; k < 5; k++) Add(solid, Props.Box(0.6f, 6f + k, 0.05f), RustDark, -9f + k * 4.5f, 26f - k * 0.6f, 14.3f);
        Merge(n, solid);
        n.AddChild(glow);
        if (number != null)
        {
            n.AddChild(Buildings.TextPanel("БЛОК " + number, 11f, 1.9f, new Vector3(0, 24.5f, 14.35f), Red, Gold));
            var back = Buildings.TextPanel("БЛОК " + number, 11f, 1.9f, new Vector3(0, 24.5f, -14.35f), Red, Gold);
            back.RotationDegrees = new Vector3(0, 180, 0);
            n.AddChild(back);
        }
        foreach (float x in new[] { -4.6f, 4.6f })
            n.AddChild(NightGlow.Part(Props.Box(0.5f, 0.2f, 0.3f), Steel, new Color(1f, 0.9f, 0.65f), 1.7f, new Vector3(x, 8.4f, 22.5f)));
        return n;
    }

    // ------------------------------------------------------------ block 4

    /// <summary>
    /// Block 4 under its sarcophagus: a steel shell of ribbed panels in a concrete collar, the stepped cascade wall behind, buttresses
    /// braced by girders, a sloped roof with seams, a vent stack and a striped parapet, scaffolding on the front, rust and cracks,
    /// debris at the foot, floodlight masts, and a crawler crane still at work (a real lattice jib, a cab, a counterweight and tracks,
    /// the warning light on its tip).
    /// </summary>
    public static Node3D Sarcophagus()
    {
        var n = new Node3D();
        var solid = new Node3D();
        var glow = new Node3D();
        Color pipe = new(0.58f, 0.59f, 0.56f);
        Add(solid, Props.Box(30f, 26f, 32f), Steel, 0, 13f, 0);
        Add(solid, Props.Box(31.6f, 3f, 33.6f), ConcreteDark, 0, 1.5f, 0);       // the concrete collar
        Add(solid, Props.Box(31.2f, 0.6f, 33.2f), ConcreteStain, 0, 3.2f, 0);
        for (int k = 0; k < 5; k++)                                     // the stepped "cascade" wall to the north
            Add(solid, Props.Box(30f, 6f + k * 5f, 3.5f), k % 2 == 0 ? ConcreteDark : ConcreteStain, 0, (6f + k * 5f) / 2f, -16f - 3.5f * (4 - k));
        // The corrugated panels: ribs on the front face and the sides, a seam band at mid height.
        for (int k = -9; k <= 9; k++)
            Add(solid, Props.Box(0.18f, 22f, 0.16f), RustDark, k * 1.5f, 14.5f, 16.1f);
        for (int k = -10; k <= 10; k++)
            foreach (float s in new[] { -1f, 1f })
                Add(solid, Props.Box(0.16f, 22f, 0.18f), RustDark, s * 15.1f, 14.5f, k * 1.5f);
        foreach (float y in new[] { 8f, 14f, 20f })
            Add(solid, Props.Box(30.2f, 0.4f, 32.2f), ConcreteStain, 0, y, 0);
        // The roof: a mono-pitch slab with seams along it. Its low edge rests on the front wall (the striped parapet along it), its high edge on the taller back wall, and the side
        // walls are carried up under it, so nothing hangs in the air; a vent stack with a cowl and three ventilators stand on its surface.
        const float roofTilt = 8f, roofMid = 28.3f, roofHalf = 0.6f;
        float tan = MathF.Tan(Mathf.DegToRad(roofTilt));
        float Under(float z) => roofMid - z * tan - roofHalf;      // (the slab's underside at this z; the high side is the back, z < 0)
        float Top(float z) => roofMid - z * tan + roofHalf;
        Add(solid, Props.Box(32f, 1.2f, 33.2f), RustDark, 0, roofMid, 0, roofTilt, 0, 0);
        for (int k = -5; k <= 5; k++) Add(solid, Props.Box(0.2f, 0.25f, 33f), Rust, k * 2.8f, roofMid + roofHalf + 0.1f, 0, roofTilt, 0, 0);
        for (int k = 0; k < 16; k++) Add(solid, Props.Box(2f, 0.7f, 0.5f), k % 2 == 0 ? Red : White, -15f + k * 2f, 26.9f, 15.6f);
        // The back wall rises to the high edge of the roof; the side walls follow the slope up to it.
        float backTop = Under(-16f) + 0.3f;
        Add(solid, Props.Box(30.4f, backTop - 26f, 0.8f), Steel, 0, 26f + (backTop - 26f) / 2f, -15.6f);
        for (float z = -16f; z < 16f; z += 2f)
        {
            float top = Under(z + 1f) + 0.3f;
            if (top - 26f < 0.15f) continue;
            foreach (float side in new[] { -1f, 1f })
                Add(solid, Props.Box(0.5f, top - 26f, 2f), Steel, side * 14.9f, 26f + (top - 26f) / 2f, z + 1f);
        }
        Add(solid, Props.Cyl(1.1f, 5f, 10), pipe, 8f, Top(-8f) + 2.4f, -8f);
        Add(solid, Props.Cyl(1.6f, 0.5f, 10, 0.2f), Rust, 8f, Top(-8f) + 5.2f, -8f);
        for (int k = 0; k < 3; k++) Add(solid, Props.Box(1.2f, 0.9f, 1.2f), ConcreteDark, -9f + k * 3.2f, Top(-2f) + 0.4f, -2f);
        // Buttresses on the long sides, braced to the roof edge by girders.
        for (int k = -3; k <= 3; k++)
            foreach (float s in new[] { -1f, 1f })
            {
                Add(solid, Props.Box(1.4f, 24f, 4f), ConcreteDark, s * 15.6f, 12f, k * 4.5f, 0, 0, s * 8f);
                Rod(solid, new Vector3(s * 16.2f, 3f, k * 4.5f), new Vector3(s * 15.2f, 25f, k * 4.5f + 2.2f), 0.2f, Steel);
            }
        // Scaffolding on the front: poles, ledgers and planks on four levels.
        for (int k = 0; k <= 6; k++) Add(solid, Props.Cyl(0.07f, 22f, 5), Steel, -9f + k * 3f, 12f, 17.3f);
        for (int lvl = 0; lvl < 4; lvl++)
        {
            float y = 5f + lvl * 5.2f;
            Add(solid, Props.Box(18.2f, 0.07f, 0.07f), Steel, 0, y, 17.3f);
            Add(solid, Props.Box(18f, 0.1f, 1.1f), new Color(0.38f, 0.28f, 0.18f), 0, y + 0.05f, 16.8f);
            Add(solid, Props.Box(18.2f, 0.07f, 0.07f), Steel, 0, y + 1f, 17.3f);
        }
        for (int k = 0; k < 6; k++) Rod(solid, new Vector3(-9f + k * 3f, 5f, 17.3f), new Vector3(-6f + k * 3f, 20f, 17.3f), 0.06f, Steel);
        // Rust streaks and cracks, a patched hole, debris at the foot, floodlight masts.
        for (int k = 0; k < 6; k++) Add(solid, Props.Box(0.8f, 10f + k * 1.5f, 0.06f), Rust, -12f + k * 4.8f, 20f - k, 16.2f);
        Rod(solid, new Vector3(-6f, 22f, 16.3f), new Vector3(-3f, 17f, 16.3f), 0.1f, Black);
        Rod(solid, new Vector3(-3f, 17f, 16.3f), new Vector3(-4.5f, 12f, 16.3f), 0.1f, Black);
        Add(solid, Props.Box(3f, 2.4f, 0.1f), RustDark, 9f, 17f, 16.25f);
        var rng = new RandomNumberGenerator { Seed = 1986 };
        for (int k = 0; k < 18; k++)
            Add(solid, Props.Box(rng.RandfRange(0.5f, 2.2f), rng.RandfRange(0.4f, 1.2f), rng.RandfRange(0.5f, 1.8f)), k % 3 == 0 ? Rust : ConcreteStain,
                rng.RandfRange(-14f, 14f), 0.3f, rng.RandfRange(18.5f, 22f), 0, rng.RandfRange(0, 180), rng.RandfRange(-12, 12));
        foreach (float x in new[] { -17f, 17f })
        {
            Add(solid, Props.Cyl(0.18f, 14f, 6), Steel, x, 7f, 20f);
            Add(solid, Props.Box(1.6f, 0.9f, 0.4f), Steel, x, 14.2f, 20f);
        }
        // The crawler crane: tracks with shoes, a deck, a cab with windows, a counterweight, an A-frame, a lattice jib with cables.
        var crane = new Node3D { Position = new Vector3(-10f, 0, -32f), RotationDegrees = new Vector3(0, 160f, 0) };     // beside the cascade wall
        var cs = new Node3D();
        crane.AddChild(cs);
        foreach (float x in new[] { -2.4f, 2.4f })
        {
            Add(cs, Props.Box(1.5f, 1.4f, 9f), Black, x, 0.8f, 0);
            for (int k = 0; k < 9; k++) Add(cs, Props.Box(1.6f, 0.15f, 0.6f), Rust, x, 1.55f, -4f + k * 1f);
            foreach (float z in new[] { -3.6f, 3.6f }) Add(cs, Props.Cyl(0.7f, 1.4f, 10), Steel, x, 0.8f, z, 0, 0, 90);
        }
        Add(cs, Props.Box(5.6f, 0.8f, 6.4f), Steel, 0, 1.9f, 0);
        Add(cs, Props.Box(5f, 4f, 6f), Yellow, 0, 3.6f + 0.4f, 0);
        Add(cs, Props.Box(5.1f, 1.4f, 0.06f), Glass, 0, 5.0f, 3.02f);
        Add(cs, Props.Box(0.06f, 1.4f, 3.2f), Glass, 2.52f, 5.0f, 1.2f);
        Add(cs, Props.Box(5.2f, 0.4f, 6.2f), Black, 0, 5.9f, 0);
        Add(cs, Props.Box(5f, 2.4f, 2.4f), Steel, 0, 3f, -4.3f);                                          // the counterweight
        Add(cs, Props.Box(0.7f, 5f, 0.7f), Yellow, 0, 8.4f, 1.6f, -15f, 0, 0);                             // the A-frame mast
        var jib = new Node3D { Position = new Vector3(0, 5f, 1.8f), RotationDegrees = new Vector3(58f, 0, 0) };      // (raised forwards over the cab, along the tracks, and away from the wall: it used to lean sideways through the cascade steps)
        crane.AddChild(jib);
        var lat = new Node3D();
        jib.AddChild(lat);
        foreach (float x in new[] { -0.55f, 0.55f })
            foreach (float z in new[] { -0.55f, 0.55f })
                Add(lat, Props.Box(0.22f, 44f, 0.22f), Yellow, x, 22f, z);
        for (int k = 0; k < 11; k++)
        {
            float y = 2f + k * 4f;
            foreach (float s in new[] { -1f, 1f })
            {
                Rod(lat, new Vector3(s * 0.55f, y, -0.55f), new Vector3(-s * 0.55f, y + 4f, -0.55f), 0.1f, Yellow);
                Rod(lat, new Vector3(s * 0.55f, y, 0.55f), new Vector3(-s * 0.55f, y + 4f, 0.55f), 0.1f, Yellow);
            }
            Add(lat, Props.Box(1.2f, 0.1f, 0.1f), Yellow, 0, y, -0.55f);
        }
        Add(lat, Props.Box(1.2f, 1.0f, 1.2f), Black, 0, 44.4f, 0);          // the head sheave box
        DrunkTractor.BatchStatic(cs, null);
        DrunkTractor.BatchStatic(lat, null);
        n.AddChild(crane);
        Merge(n, solid);
        n.AddChild(glow);
        jib.AddChild(NightGlow.Part(Props.Box(0.8f, 0.8f, 0.8f), Red, Red, 2f, new Vector3(0, 45.3f, 0)));
        foreach (float x in new[] { -17f, 17f })
            n.AddChild(NightGlow.Part(Props.Box(1.4f, 0.7f, 0.3f), Steel, new Color(1f, 0.92f, 0.7f), 2.2f, new Vector3(x, 14.2f, 20.3f)));
        n.AddChild(Buildings.TextPanel("БЛОК 4", 7f, 1.4f, new Vector3(0, 21f, 16.4f), Red, Gold));
        return n;
    }
}
