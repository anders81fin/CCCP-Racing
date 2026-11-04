using System;
using Godot;

namespace CCCPRacing.Track;

/// <summary>
/// ATOMGRAD-4's buildings and landmarks (track 3): the power plant (turbine hall, reactor blocks,
/// block 4 under its sarcophagus, the striped ventilation stack), the pipe bridges, the town's
/// prefab apartment blocks, the square's ferris wheel and palace of culture, the zone checkpoint,
/// the decontamination station, the vehicle graveyard's rusting fleet, the railway, radiation signs.
/// Grey prefab concrete, rust and a few lit windows (NightGlow). Front is local +Z unless said.
/// </summary>
public static partial class Atomgrad
{
    static readonly Color Concrete = new(0.62f, 0.62f, 0.6f), ConcreteDark = new(0.44f, 0.44f, 0.43f), ConcreteStain = new(0.36f, 0.36f, 0.34f);
    static readonly Color Window = new(0.15f, 0.18f, 0.21f), WindowBroken = new(0.05f, 0.05f, 0.05f), WindowLit = new(0.9f, 0.78f, 0.4f), WindowGlow = new(1f, 0.78f, 0.38f);
    static readonly Color Rust = new(0.47f, 0.25f, 0.13f), RustDark = new(0.32f, 0.18f, 0.1f), Steel = new(0.35f, 0.37f, 0.39f);
    static readonly Color Red = new(0.78f, 0.12f, 0.08f), White = new(0.9f, 0.9f, 0.88f), Yellow = new(0.95f, 0.78f, 0.1f), Black = new(0.08f, 0.08f, 0.08f);
    static readonly Color Gold = new(1f, 0.85f, 0.3f), Glass = new(0.3f, 0.38f, 0.44f), Blue = new(0.2f, 0.36f, 0.62f);

    static MeshInstance3D P(Mesh m, Color c, float x, float y, float z, float rx = 0, float ry = 0, float rz = 0) =>
        Props.Part(m, c, new Vector3(x, y, z), new Vector3(rx, ry, rz));

    /// <summary>A window grid on one face (z: the face's offset, side ±1), some lit at night, some broken.</summary>
    static void Windows(Node3D n, float len, float face, int floors, float storey, float y0, int perFloor, int seed, bool alongX = true, float litShare = 0.06f)
    {
        var rng = new RandomNumberGenerator { Seed = (ulong)seed };
        for (int f = 0; f < floors; f++)
            for (int w = 0; w < perFloor; w++)
            {
                float a = -len / 2f + (w + 0.5f) * len / perFloor, y = y0 + f * storey;
                var at = alongX ? new Vector3(a, y, face) : new Vector3(face, y, a);
                var box = alongX ? Props.Box(len / perFloor * 0.55f, storey * 0.45f, 0.06f) : Props.Box(0.06f, storey * 0.45f, len / perFloor * 0.55f);
                float r = rng.Randf();
                n.AddChild(r < litShare ? NightGlow.Part(box, Window, WindowGlow, 1.1f, at) : Props.Part(box, r < 0.3f ? WindowBroken : Window, at));
            }
    }

    // ------------------------------------------------------------ the plant

    /// <summary>The ventilation stack: a tall cylinder banded red and white in a steel lattice frame, a red lamp on top.</summary>
    public static Node3D Stack()
    {
        var n = new Node3D();
        const float H = 54f;
        for (int k = 0; k < 9; k++)
            n.AddChild(P(Props.Cyl(2.6f - k * 0.08f, H / 9f, 12, 2.6f - (k + 1) * 0.08f), k % 2 == 0 ? Red : White, 0, (k + 0.5f) * H / 9f, 0));
        for (int k = 0; k < 4; k++)                                     // the lattice's four legs
        {
            float a = k * MathF.PI / 2f + MathF.PI / 4f;
            n.AddChild(P(Props.Box(0.5f, H * 0.95f, 0.5f), Steel, MathF.Cos(a) * 4.2f, H * 0.475f, MathF.Sin(a) * 4.2f));
        }
        for (int k = 1; k < 8; k++)                                     // ...and its rings
            n.AddChild(P(Props.Cyl(4.4f, 0.4f, 8, 4.4f), Steel, 0, k * H / 8f, 0));
        n.AddChild(NightGlow.Part(Props.Box(0.9f, 0.9f, 0.9f), Red, Red, 2.5f, new Vector3(0, H + 0.6f, 0)));
        return n;
    }

    /// <summary>A pipe bridge over the road: two thick pipes on lattice trestles, span along X (the road runs along Z under it).</summary>
    public static Node3D PipeBridge(float span)
    {
        var n = new Node3D();
        const float Y = 6.5f;
        foreach (float x in new[] { -span / 2f, span / 2f })
        {
            n.AddChild(P(Props.Box(0.6f, Y, 0.6f), Steel, x, Y / 2f, -1.2f));
            n.AddChild(P(Props.Box(0.6f, Y, 0.6f), Steel, x, Y / 2f, 1.2f));
            n.AddChild(P(Props.Box(1.2f, 0.4f, 3.2f), Steel, x, Y, 0));
        }
        n.AddChild(P(Props.Cyl(0.6f, span + 4f, 10, 0.6f), new Color(0.72f, 0.72f, 0.7f), 0, Y + 0.8f, -0.7f, 0, 0, 90));
        n.AddChild(P(Props.Cyl(0.45f, span + 4f, 10, 0.45f), Rust, 0, Y + 0.65f, 0.75f, 0, 0, 90));
        return n;
    }

    // ------------------------------------------------------------ the town

    /// <summary>A prefab apartment block along X: floors (Text) of 3 m, 50 × 12 m, flat roof, a slogan on the roof edge.</summary>
    public static Node3D Apartments(string text)
    {
        var n = new Node3D();
        string[] parts = (text ?? "9").Split(';');
        int floors = int.TryParse(parts[0], out int f) ? f : 9;
        const float L = 50f, D = 12f;
        float H = floors * 3f + 0.6f;
        n.AddChild(P(Props.Box(L, H, D), Concrete, 0, H / 2f, 0));
        n.AddChild(P(Props.Box(L + 0.3f, 0.5f, D + 0.3f), ConcreteDark, 0, H + 0.15f, 0));
        for (int k = 0; k <= 10; k++)
            foreach (float s in new[] { -1f, 1f })
                n.AddChild(P(Props.Box(0.08f, H, 0.06f), ConcreteDark, -L / 2f + k * 5f, H / 2f, s * (D / 2f + 0.02f)));
        for (int k = 0; k < floors; k++)                                     // balconies' slabs
            n.AddChild(P(Props.Box(L * 0.96f, 0.15f, 0.9f), ConcreteDark, 0, 3f + k * 3f, D / 2f + 0.45f));
        Windows(n, L, D / 2f + 0.05f, floors, 3f, 1.8f, 16, floors * 31 + (int)L);
        Windows(n, L, -D / 2f - 0.05f, floors, 3f, 1.8f, 16, floors * 37 + 3);
        if (parts.Length > 1)
            n.AddChild(Buildings.TextPanel(parts[1], L * 0.8f, 2.2f, new Vector3(0, H + 1.6f, D / 2f - 0.5f), Red, Gold));
        return n;
    }

    /// <summary>The ferris wheel the town never opened: a yellow ring of gondolas on an A-frame, the wheel in the XY plane.</summary>
    public static Node3D FerrisWheel()
    {
        var n = new Node3D();
        const float R = 13f, Hub = 15f;
        foreach (float z in new[] { -1.6f, 1.6f })
        {
            n.AddChild(P(Props.Box(0.7f, Hub + 1f, 0.7f), Steel, -5f, Hub / 2f, z * 2f, 0, 0, -18f));
            n.AddChild(P(Props.Box(0.7f, Hub + 1f, 0.7f), Steel, 5f, Hub / 2f, z * 2f, 0, 0, 18f));
        }
        n.AddChild(P(Props.Cyl(0.8f, 4.8f, 8, 0.8f), Steel, 0, Hub, 0, 90, 0, 0));
        const int Seg = 24;
        for (int k = 0; k < Seg; k++)
        {
            float a = k * MathF.Tau / Seg, a2 = (k + 0.5f) * MathF.Tau / Seg;
            foreach (float z in new[] { -1.4f, 1.4f })
                n.AddChild(P(Props.Box(0.35f, R * MathF.Tau / Seg + 0.2f, 0.35f), Yellow, MathF.Cos(a2) * R, Hub + MathF.Sin(a2) * R, z, 0, 0, Mathf.RadToDeg(a2)));
            if (k % 2 == 0)
            {
                n.AddChild(P(Props.Box(0.18f, R, 0.18f), Steel, MathF.Cos(a) * R / 2f, Hub + MathF.Sin(a) * R / 2f, 0, 0, 0, Mathf.RadToDeg(a) - 90f));
                // A gondola hanging under the rim: a yellow tub under a little roof.
                float gx = MathF.Cos(a) * R, gy = Hub + MathF.Sin(a) * R - 1.6f;
                n.AddChild(P(Props.Box(1.8f, 1.1f, 1.6f), Yellow, gx, gy, 0));
                n.AddChild(P(Props.Box(2f, 0.2f, 1.8f), Red, gx, gy + 1.3f, 0));
            }
        }
        return n;
    }

    /// <summary>The palace of culture: a low wide hall with a glass front and its name on the roof, 26 × 16 m.</summary>
    public static Node3D PalaceOfCulture()
    {
        var n = new Node3D();
        n.AddChild(P(Props.Box(26f, 8f, 16f), Concrete, 0, 4f, 0));
        n.AddChild(P(Props.Box(22f, 5.6f, 0.08f), Glass, 0, 3.6f, 8.05f));
        for (int k = 0; k <= 8; k++) n.AddChild(P(Props.Box(0.2f, 5.6f, 0.12f), ConcreteDark, -11f + k * 2.75f, 3.6f, 8.1f));
        n.AddChild(P(Props.Box(28f, 0.6f, 18f), ConcreteDark, 0, 8.3f, 0));
        n.AddChild(Buildings.TextPanel("ДК ЭНЕРГЕТИК", 14f, 2f, new Vector3(0, 9.8f, 6f), Red, Gold));
        return n;
    }

    /// <summary>The bumper cars' pavilion: a flat roof on posts over a floor with abandoned cars, 14 × 10 m.</summary>
    public static Node3D BumperCars()
    {
        var n = new Node3D();
        n.AddChild(P(Props.Box(14f, 0.3f, 10f), ConcreteDark, 0, 0.15f, 0));
        foreach (float x in new[] { -6.6f, 0f, 6.6f })
            foreach (float z in new[] { -4.6f, 4.6f })
                n.AddChild(P(Props.Box(0.25f, 4f, 0.25f), Steel, x, 2f, z));
        n.AddChild(P(Props.Box(14.6f, 0.4f, 10.6f), Blue, 0, 4.2f, 0));
        var rng = new RandomNumberGenerator { Seed = 1970 };
        for (int k = 0; k < 6; k++)
            n.AddChild(P(Props.Box(1.4f, 0.6f, 1.8f), k % 2 == 0 ? Red : Yellow, rng.RandfRange(-5.5f, 5.5f), 0.6f, rng.RandfRange(-3.5f, 3.5f), 0, rng.RandfRange(0, 360), 0));
        return n;
    }

    // ------------------------------------------------------------ the zone

    /// <summary>The zone checkpoint's booth: a small glazed box with a red-white striped boom raised beside it and the ZONE sign.</summary>
    public static Node3D Checkpoint()
    {
        var n = new Node3D();
        n.AddChild(P(Props.Box(3f, 2.8f, 3f), White, 0, 1.4f, 0));
        n.AddChild(P(Props.Box(3.1f, 0.9f, 3.1f), Glass, 0, 2.1f, 0));
        n.AddChild(P(Props.Box(3.4f, 0.25f, 3.4f), Blue, 0, 2.95f, 0));
        n.AddChild(NightGlow.Part(Props.Box(3.12f, 0.6f, 3.12f), Glass, WindowGlow, 0.9f, new Vector3(0, 2.15f, 0)));
        // The boom: a post and an arm raised at 70°, banded red and white.
        n.AddChild(P(Props.Box(0.5f, 1.2f, 0.5f), ConcreteDark, 3f, 0.6f, 0));
        var arm = new Node3D { Position = new Vector3(3f, 1.2f, 0), RotationDegrees = new Vector3(0, 0, 70f) };
        n.AddChild(arm);
        for (int k = 0; k < 8; k++) arm.AddChild(Props.Part(Props.Box(1f, 0.2f, 0.2f), k % 2 == 0 ? Red : White, new Vector3(-0.5f - k, 0, 0)));
        n.AddChild(RadSign("ЗОНА", new Vector3(-3.5f, 0, 0)));
        return n;
    }

    /// <summary>
    /// Where the dosimetrist stands in the checkpoint's frame (+X along the booth, +Z towards the road; the checkpoint at the start is turned
    /// 180 degrees; ZoneDosimetrist puts him there on the ground, a figure of his own that the car can knock over): at the road's edge a few metres past the start line in the racing direction, clear of the start gate's legs, facing
    /// the road (his speech bubble goes over him).
    /// </summary>
    public static readonly Vector3 DosimetristAt = new(-12f, 0, 6.7f);

    /// <summary>His size: officials are 1.75 (the KGB officers), the people 1.6; he must read from the high camera.</summary>
    internal const float DosimetristScale = 1.75f;

    /// <summary>
    /// The dosimetrist: a white coverall and hood, boots, a grey respirator, gloves, a probe on a lead held out, a meter on a strap, and
    /// seen from above an orange high-visibility vest with reflective stripes and orange shoulder plates and a cap, so he shows against the concrete.
    /// </summary>
    internal static Node3D Dosimetrist(Vector3 at)
    {
        var n = new Node3D { Position = at, Scale = Vector3.One * DosimetristScale };
        var suit = new Color(0.92f, 0.93f, 0.9f);
        var vest = new Color(0.98f, 0.42f, 0.04f);
        var stripe = new Color(0.88f, 0.9f, 0.8f);
        foreach (float x in new[] { -0.13f, 0.13f })
        {
            n.AddChild(P(Props.Box(0.18f, 0.7f, 0.2f), suit, x, 0.45f, 0));
            n.AddChild(P(Props.Box(0.2f, 0.12f, 0.3f), new Color(0.12f, 0.1f, 0.08f), x, 0.06f, 0.03f));       // boots
        }
        n.AddChild(P(Props.Box(0.5f, 0.7f, 0.3f), suit, 0, 1.15f, 0));
        n.AddChild(P(Props.Box(0.54f, 0.5f, 0.34f), vest, 0, 1.22f, 0));                                    // the vest
        n.AddChild(P(Props.Box(0.56f, 0.06f, 0.36f), vest, 0, 1.5f, 0));                                    // orange across the shoulders, for the camera above
        foreach (float y in new[] { 1.1f, 1.3f }) n.AddChild(P(Props.Box(0.56f, 0.06f, 0.36f), stripe, 0, y, 0));
        n.AddChild(P(Props.Ball(0.17f), suit, 0, 1.68f, 0));                                                // the hood
        n.AddChild(P(Props.Box(0.36f, 0.07f, 0.36f), vest, 0, 1.83f, 0));                                   // an orange cap on it
        n.AddChild(P(Props.Box(0.22f, 0.16f, 0.12f), new Color(0.3f, 0.32f, 0.3f), 0, 1.64f, 0.14f));       // respirator
        n.AddChild(P(Props.Cyl(0.06f, 0.1f, 8), Black, 0, 1.6f, 0.24f, 90, 0, 0));                          // its filter
        n.AddChild(P(Props.Box(0.12f, 0.12f, 0.55f), suit, 0.3f, 1.3f, 0.2f));                              // the arm held out
        n.AddChild(P(Props.Box(0.14f, 0.14f, 0.14f), Black, 0.3f, 1.3f, 0.5f));                             // the glove
        n.AddChild(P(Props.Cyl(0.03f, 0.5f, 6), Steel, 0.3f, 1.3f, 0.8f, 90, 0, 0));                        // the probe
        n.AddChild(P(Props.Box(0.2f, 0.12f, 0.08f), Yellow, -0.3f, 1.1f, 0.1f));                            // the meter on its strap
        n.AddChild(P(Props.Box(0.12f, 0.5f, 0.12f), suit, -0.31f, 1.15f, 0));                               // the other arm, down
        return n;
    }

    /// <summary>A radiation warning: the yellow trefoil sign on a post, a word under it.</summary>
    public static Node3D RadSign(string word = null, Vector3? at = null)
    {
        var n = new Node3D { Position = at ?? Vector3.Zero };
        n.AddChild(P(Props.Box(0.12f, 2.4f, 0.12f), Steel, 0, 1.2f, 0));
        var board = new Node3D { Position = new Vector3(0, 2.3f, 0.08f) };
        n.AddChild(board);
        board.AddChild(Props.Part(Props.Box(1.3f, 1.3f, 0.05f), Yellow, Vector3.Zero, new Vector3(0, 0, 45f)));
        board.AddChild(Props.Part(Props.Cyl(0.13f, 0.06f, 10), Black, new Vector3(0, 0, 0.04f), new Vector3(90, 0, 0)));
        for (int k = 0; k < 3; k++)     // the trefoil's three blades
        {
            float a = k * 120f + 90f;
            board.AddChild(Props.Part(Props.Box(0.3f, 0.32f, 0.05f), Black,
                new Vector3(MathF.Cos(Mathf.DegToRad(a)) * 0.3f, MathF.Sin(Mathf.DegToRad(a)) * 0.3f, 0.03f), new Vector3(0, 0, a - 90f)));
        }
        if (word != null) n.AddChild(Buildings.TextPanel(word, 1.4f, 0.4f, new Vector3(0, 1.3f, 0.1f), Yellow, Black));
        return n;
    }

    /// <summary>A length of railway along Z, len metres, laid on the ground: every sleeper and every
    /// 2 m of rail at the ground's own height (t.MeshHeight) so it never floats over a dip or the road's edge.
    /// at: the prop's place (its local origin), rot: its yaw.</summary>
    public static Node3D Rails(float len, TrackData t, Vector3 at, float rot)
    {
        var n = new Node3D { Name = "Railway" };
        var st = new SurfaceTool(); st.Begin(Mesh.PrimitiveType.Triangles);
        Vector2 az = new(MathF.Sin(rot), MathF.Cos(rot)), ax = new(MathF.Cos(rot), -MathF.Sin(rot));
        Vector2 World(float x, float z) => new Vector2(at.X, at.Z) + ax * x + az * z;
        float Y(float x, float z) { var w = World(x, z); return t.MeshHeight(w.X, w.Y) - at.Y; }
        bool Road(float z)
        {
            int ci = t.NearestCenter(World(0, z), out float distance);
            return ci >= 0 && distance < t.HalfWidth[ci] + 0.6f;
        }
        void Box(float w, float h, float l, Color color, float x, float y, float z, float pitch = 0)
        {
            Fences.AddBox(st, new Transform3D(Basis.FromEuler(new Vector3(pitch, 0, 0)), new Vector3(x, y, z)), new Vector3(w, h, l), color);
        }
        const float step = 1f;
        for (float z = -len / 2f; z < len / 2f - 0.01f; z += step)
        {
            float length = Math.Min(step, len / 2f - z), mid = z + length / 2;
            bool crossing = Road(mid);
            if (!crossing)
            {
                float pitch = -MathF.Atan2(Y(0, z + length) - Y(0, z), length);
                // Broad granular ballast shoulder, narrower dark centre beneath the sleepers.
                Box(3.65f, 0.1f, length + 0.02f, new Color(0.32f, 0.32f, 0.29f), 0, Y(0, mid) + 0.015f, mid, pitch);
                Box(2.85f, 0.07f, length + 0.02f, new Color(0.39f, 0.38f, 0.34f), 0, Y(0, mid) + 0.045f, mid, pitch);
            }
            else
            {
                // Flush crossing panels leave a narrow flangeway beside each running rail.
                Box(1.27f, 0.15f, length - 0.035f, ConcreteDark, 0, Y(0, mid) + 0.18f, mid);
                foreach (float side in new[] { -1f, 1f })
                    Box(0.7f, 0.15f, length - 0.035f, ConcreteDark, side * 1.23f, Y(side * 1.23f, mid) + 0.18f, mid);
            }
            foreach (float x in new[] { -0.76f, 0.76f })
            {
                float y0 = Y(x, z), y1 = Y(x, z + length);
                float pitch = -MathF.Atan2(y1 - y0, length), y = (y0 + y1) / 2;
                Box(0.17f, 0.045f, length + 0.015f, RustDark, x, y + 0.15f, mid, pitch);
                Box(0.055f, 0.11f, length + 0.015f, Rust, x, y + 0.215f, mid, pitch);
                Box(0.09f, 0.035f, length + 0.015f, new Color(0.52f, 0.53f, 0.5f), x, y + 0.285f, mid, pitch);
            }
        }
        var rng = new RandomNumberGenerator { Seed = 1987 };
        for (float z = -len / 2f; z <= len / 2f; z += 0.65f)
        {
            if (Road(z)) continue;
            Box(2.6f, 0.12f, 0.24f, new Color(0.23f + rng.Randf() * 0.05f, 0.18f, 0.13f), 0, Y(0, z) + 0.08f, z);
            foreach (float side in new[] { -1f, 1f })
            {
                Box(0.28f, 0.035f, 0.2f, RustDark, side * 0.76f, Y(side * 0.76f, z) + 0.15f, z);
                float x = side * rng.RandfRange(1.35f, 1.85f);
                Box(rng.RandfRange(0.08f, 0.2f), 0.07f, rng.RandfRange(0.1f, 0.25f), ConcreteDark, x, Y(x, z) + 0.08f, z);
                if (rng.Randf() < 0.18f)
                {
                    x = side * rng.RandfRange(1.8f, 2.2f);
                    Box(0.055f, rng.RandfRange(0.15f, 0.36f), 0.055f, new Color(0.29f, 0.3f, 0.17f), x, Y(x, z) + 0.12f, z);
                }
            }
        }
        n.AddChild(new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = new StandardMaterial3D {
            VertexColorUseAsAlbedo = true, Roughness = 1f, SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled
        } });
        return n;
    }

    /// <summary>The level crossing's warning post: a cross-buck, a red lamp pair that blinks at night.</summary>
    public static Node3D CrossingSign()
    {
        var n = new Node3D();
        n.AddChild(P(Props.Box(0.15f, 3f, 0.15f), White, 0, 1.5f, 0));
        foreach (float a in new[] { 35f, -35f })
            n.AddChild(P(Props.Box(1.6f, 0.25f, 0.05f), Red, 0, 2.6f, 0.1f, 0, 0, a));
        foreach (float x in new[] { -0.3f, 0.3f })
        {
            n.AddChild(P(Props.Cyl(0.2f, 0.12f, 16), Black, x, 1.8f, 0.12f, 90, 0, 0));
            var lens = NightGlow.Part(Props.Cyl(0.115f, 0.025f, 16), Red, Red, 1.5f, new Vector3(x, 1.8f, 0.19f));
            lens.RotationDegrees = new Vector3(90, 0, 0);
            n.AddChild(lens);
            n.AddChild(P(Props.Box(0.39f, 0.06f, 0.28f), Black, x, 2.01f, 0.22f));
        }
        return n;
    }

    // ------------------------------------------------------------ the hot spots at night

    static readonly Color HotGlow = new(0.32f, 0.85f, 0.22f);

    /// <summary>
    /// The hot spots' glow after dark: soft green blots on the ground (moss, leaves, the dust) round
    /// each hot spot, never on the road. Additive over black: by day they add nothing, the night
    /// lights them (NightGlow), bigger towards the middle.
    /// </summary>
    public static Node3D HotSpotGlow(TrackData t, RadiationDef rad)
    {
        var n = new Node3D { Name = "HotSpotGlow" };
        var mat = new StandardMaterial3D
        {
            // Shaded (an unshaded material ignores its emission), black and matt: only the glow adds.
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            AlbedoColor = Colors.Black,
            Roughness = 1f,
            SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
            EmissionEnabled = true,
            Emission = HotGlow,
            EmissionEnergyMultiplier = 0f,
        };
        NightGlow.Glows.Add((mat, 0.3f));
        var rng = new RandomNumberGenerator { Seed = 1986 };
        var xfs = new System.Collections.Generic.List<Transform3D>();
        foreach (var h in rad.HotSpots)
        {
            int count = (int)(h.R * h.R * 0.05f * Math.Min(1f, h.Rate * 1.6f));
            for (int k = 0, tries = 0; k < count && tries < count * 10; tries++)
            {
                float a = rng.RandfRange(0, MathF.Tau), r = h.R * MathF.Pow(rng.Randf(), 0.8f);
                var p = new Vector2(h.X + MathF.Cos(a) * r, h.Z + MathF.Sin(a) * r);
                int ci = t.NearestCenter(p, out float d);
                if (ci >= 0 && d < t.HalfWidth[ci] + 1.2f) continue;
                if (p.X < 2 || p.Y < 2 || p.X > t.WorldSize - 2 || p.Y > t.WorldSize - 2) continue;
                float size = rng.RandfRange(0.5f, 1.5f) * (1.3f - 0.6f * r / h.R);
                var b = new Basis(Vector3.Up, rng.RandfRange(0, MathF.Tau)).Scaled(new Vector3(size, 1f, size * rng.RandfRange(0.6f, 1.2f)));
                xfs.Add(new Transform3D(b, new Vector3(p.X, t.MeshHeight(p.X, p.Y) + 0.06f, p.Y)));
                k++;
            }
        }
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = new CylinderMesh { TopRadius = 0.5f, BottomRadius = 0.5f, Height = 0.02f, RadialSegments = 10, Rings = 1 }, InstanceCount = xfs.Count };
        for (int i = 0; i < xfs.Count; i++) mm.SetInstanceTransform(i, xfs[i]);
        n.AddChild(new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        if (OS.HasEnvironment("DOSEDBG")) GD.Print($"dose: {xfs.Count} glowing blots round {rad.HotSpots.Count} hot spots");
        return n;
    }
}
