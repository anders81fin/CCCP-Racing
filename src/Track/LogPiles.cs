using System;
using System.Collections.Generic;
using CCCPRacing.Car;
using Godot;

namespace CCCPRacing.Track;

/// <summary>
/// Stacks of felled logs by a winter road (TrackDef.LogPiles): placed by TrackBuilder behind the
/// snow bank, or out in the open where there is none (then they are obstacles too), drawn here: a
/// pyramid of bark-brown logs of mixed thickness and length, pale sawn ends, snow lying on top.
/// </summary>
public static class LogPiles
{
    static readonly Color Bark = new(0.27f, 0.2f, 0.15f), BarkDark = new(0.2f, 0.15f, 0.11f);
    static readonly Color Cut = new(0.78f, 0.64f, 0.44f), Snow = new(0.95f, 0.96f, 0.99f);
    public const float Length = 5f;

    /// <summary>Where a pile goes: ground position, yaw of its long axis (along the road), seed.</summary>
    public readonly record struct Pile(Vector3 Pos, float Yaw, int Seed);

    public static Node3D Build(TrackData t)
    {
        Node3D result = null;
        foreach (var step in BuildSteps(t)) result = step ?? result;
        return result;
    }

    /// <summary>The same build in slices (a null between them: a frame may go by while a loading card runs); the last item is the piles.
    /// A build abandoned between slices frees the half-made node.</summary>
    public static IEnumerable<Node3D> BuildSteps(TrackData t)
    {
        var root = new Node3D { Name = "LogPiles" };
        bool handedOver = false;
        try
        {
            foreach (var p in t.LogPiles)
            {
                var mi = new MeshInstance3D { Mesh = Stack(p.Seed), Position = p.Pos, Rotation = new Vector3(0, p.Yaw, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
                root.AddChild(mi);
                yield return null;
            }
            handedOver = true;
            yield return root;
        }
        finally { if (!handedOver) root.Free(); }
    }

    /// <summary>Obstacle circles down the pile's length (for piles a car can reach).</summary>
    public static IEnumerable<Obstacle> Obstacles(Pile p)
    {
        Vector3 along = new Basis(Vector3.Up, p.Yaw) * Vector3.Back;
        for (float z = -Length / 2f + 1f; z <= Length / 2f - 1f + 0.01f; z += 1.5f)
            yield return new Obstacle(new Vector2(p.Pos.X + along.X * z, p.Pos.Z + along.Z * z), 1.4f, Material: ImpactMaterial.Wood);
    }

    static readonly Color CabGreen = new(0.22f, 0.3f, 0.2f), Rust = new(0.35f, 0.22f, 0.14f), Tyre = new(0.07f, 0.07f, 0.07f);
    static readonly Color Glass = new(0.14f, 0.18f, 0.2f), Soot = new(0.12f, 0.11f, 0.1f);

    /// <summary>Where a wreck's engine smokes, in its own frame (the cab end).</summary>
    public static readonly Vector3 WreckSmokeAt = new(0f, 1.0f, -4.2f);

    /// <summary>
    /// A timber lorry that ran off the road and lies on its side in the snow: green cab, rusty
    /// frame, the wheels in the air, a few logs still chained to the bunks, the rest of the load
    /// spilled across the snow, snow drifted against the roof. Its front is -Z; RaceScene adds the smoke.
    /// </summary>
    public static Node3D LorryWreck()
    {
        var root = new Node3D();
        // The truck upright, then laid on its left side (rolled 88° about its long axis): the roof
        // towards -x, the wheels in the air towards +x.
        var truck = new Node3D { RotationDegrees = new Vector3(0, 0, 88f), Position = new Vector3(-0.6f, 1.25f, 0) };
        root.AddChild(truck);
        truck.AddChild(Props.Part(Props.Box(1.1f, 0.35f, 9.5f), Rust, new Vector3(0, -0.1f, 0.4f)));            // frame
        Cab(truck);
        foreach (float z in new[] { -4.0f, 1.4f, 3.1f })
        foreach (float x in new[] { -1.15f, 1.15f })
            truck.AddChild(Props.Part(Props.Cyl(0.55f, 0.45f, 10), Tyre, new Vector3(x, -0.55f, z), new Vector3(0, 0, 90)));
        // Bunks and the logs still chained on.
        foreach (float z in new[] { -1.6f, 1.2f, 3.8f })
        {
            truck.AddChild(Props.Part(Props.Box(2.5f, 0.2f, 0.25f), Rust, new Vector3(0, 0.25f, z)));
            foreach (float x in new[] { -1.2f, 1.2f }) truck.AddChild(Props.Part(Props.Box(0.15f, 1.3f, 0.15f), Rust, new Vector3(x, 0.9f, z)));
        }
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var rng = new RandomNumberGenerator { Seed = 1979 };
        for (int k = 0; k < 4; k++) Log(st, new Vector3(-0.7f + k * 0.47f, 0.6f, 1.2f), 0.23f, 6.5f, k % 2 == 0 ? Bark : BarkDark);
        for (int k = 0; k < 2; k++) Log(st, new Vector3(-0.35f + k * 0.47f, 1.0f, 1.3f), 0.22f, 6.3f, Bark);
        st.GenerateNormals();
        st.SetMaterial(Mat());
        truck.AddChild(new MeshInstance3D { Mesh = st.Commit(), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        SiberiaModelDetails.Wreck(truck);
        // The spilled load: logs lying every which way beside it in the snow (in the root's frame).
        for (int k = 0; k < 9; k++)
        {
            var one = new SurfaceTool();
            one.Begin(Mesh.PrimitiveType.Triangles);
            float r = rng.RandfRange(0.18f, 0.26f);
            Log(one, new Vector3(0, r, 0), r, rng.RandfRange(4.5f, 6.5f), rng.Randf() < 0.5f ? Bark : BarkDark);
            one.GenerateNormals();
            one.SetMaterial(Mat());
            root.AddChild(new MeshInstance3D
            {
                Mesh = one.Commit(),
                Position = new Vector3(rng.RandfRange(-3.2f, -7f), rng.RandfRange(-0.15f, 0.1f), rng.RandfRange(-2f, 5f)),
                RotationDegrees = new Vector3(rng.RandfRange(-6f, 6f), rng.RandfRange(-55f, 55f), 0),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }
        // Snow drifted against the roof side and over the cab, black soot on the snow by the bonnet.
        root.AddChild(Props.Part(new PrismMesh { Size = new Vector3(1.6f, 0.9f, 9.5f) }, Snow, new Vector3(1.9f, 0.4f, 0.4f), new Vector3(0, 0, 25f)));   // drifted against the underside
        root.AddChild(Props.Part(Props.Box(3.5f, 0.04f, 2.5f), Soot, new Vector3(0.2f, 0.03f, -5.6f)));
        return root;
    }

    /// <summary>
    /// The wreck's cab, bonnet and running gear (truck frame: front -Z, right +X, which lies uppermost): a cab with
    /// its right door hanging open on one hinge and the cabin showing (seat, wheel, dashboard), the windscreen cracked on
    /// one side and gone on the other, a mirror, roof lamps and a step; a bonnet with its right side panel bent open on
    /// the engine (block, heads, air cleaner, radiator, fan, hoses, battery), a scorched top, a dented wing, one headlamp
    /// broken, a bent bumper with tow hooks and a number plate; chassis cross-members, springs, axles, a drive shaft, a
    /// fuel tank and the exhaust stack; wheel rims. All merged into one mesh (the plate is its own panel).
    /// </summary>
    static void Cab(Node3D truck)
    {
        var solid = new Node3D();
        Color steel = new(0.45f, 0.46f, 0.44f), dark = new(0.07f, 0.07f, 0.08f), engine = new(0.28f, 0.29f, 0.29f), pale = new(0.8f, 0.78f, 0.7f);
        Color valve = new(0.5f, 0.18f, 0.12f), seat = new(0.3f, 0.2f, 0.12f), amber = new(0.85f, 0.55f, 0.1f), glassLight = new(0.35f, 0.42f, 0.45f);
        Color green = CabGreen, greenDark = CabGreen * 0.8f;
        void Add(Node3D parent, Mesh m, Color c, Vector3 at, Vector3? rot = null) => parent.AddChild(Props.Part(m, c, at, rot));
        void Bar(Vector3 a, Vector3 b, float w, Color c)
        {
            Vector3 d = b - a;
            var part = Props.Part(Props.Box(w, w, d.Length()), c, (a + b) / 2f);
            part.Basis = Basis.LookingAt(d.Normalized(), MathF.Abs(d.Normalized().Y) > 0.99f ? Vector3.Right : Vector3.Up);
            solid.AddChild(part);
        }
        // ---- the cab shell: floor, back wall, roof with a visor, the left side (underneath), the right sill and pillars.
        Add(solid, Props.Box(2.4f, 0.08f, 1.9f), greenDark, new Vector3(0, 0.1f, -3.3f));
        Add(solid, Props.Box(2.4f, 1.9f, 0.1f), green, new Vector3(0, 1.0f, -2.4f));
        Add(solid, Props.Box(2.4f, 0.1f, 1.95f), green, new Vector3(0, 1.95f, -3.3f));
        Add(solid, Props.Box(2.4f, 0.06f, 0.4f), greenDark, new Vector3(0, 2.0f, -4.4f), new Vector3(-12, 0, 0));
        Add(solid, Props.Box(0.08f, 1.9f, 1.9f), green, new Vector3(-1.17f, 1.0f, -3.3f));
        Add(solid, Props.Box(0.08f, 0.55f, 1.9f), green, new Vector3(1.17f, 0.35f, -3.3f));
        foreach (float z in new[] { -4.2f, -3.25f, -2.45f })
            Add(solid, Props.Box(0.1f, 1.35f, 0.1f), green, new Vector3(1.15f, 1.27f, z));
        Add(solid, Props.Box(2.3f, 0.55f, 0.1f), green, new Vector3(0, 0.6f, -4.22f));                 // the cowl under the windscreen
        // Windscreen: a centre pillar, the left pane cracked, the right pane gone with shards left in the frame.
        Add(solid, Props.Box(0.1f, 0.8f, 0.1f), green, new Vector3(0, 1.55f, -4.22f));
        Add(solid, Props.Box(1.05f, 0.75f, 0.03f), glassLight, new Vector3(-0.58f, 1.55f, -4.25f));
        Bar(new Vector3(-1.0f, 1.2f, -4.28f), new Vector3(-0.3f, 1.85f, -4.28f), 0.02f, pale);
        Bar(new Vector3(-0.9f, 1.85f, -4.28f), new Vector3(-0.2f, 1.3f, -4.28f), 0.02f, pale);
        Bar(new Vector3(-0.6f, 1.2f, -4.28f), new Vector3(-0.55f, 1.9f, -4.28f), 0.02f, pale);
        Add(solid, new PrismMesh { Size = new Vector3(0.25f, 0.3f, 0.03f) }, glassLight, new Vector3(0.2f, 1.28f, -4.24f));
        Add(solid, new PrismMesh { Size = new Vector3(0.2f, 0.22f, 0.03f) }, glassLight, new Vector3(0.62f, 1.25f, -4.24f), new Vector3(0, 0, 12));
        Add(solid, new PrismMesh { Size = new Vector3(0.18f, 0.25f, 0.03f) }, glassLight, new Vector3(0.95f, 1.85f, -4.24f), new Vector3(0, 0, 180));
        // Roof lamps, a mirror on a bent arm, and a running step.
        foreach (float x in new[] { -0.55f, 0f, 0.55f })
            Add(solid, Props.Box(0.2f, 0.1f, 0.12f), amber, new Vector3(x, 2.04f, -4.15f));
        Bar(new Vector3(1.2f, 1.55f, -4.1f), new Vector3(1.6f, 1.8f, -4.3f), 0.04f, dark);
        Add(solid, Props.Box(0.05f, 0.4f, 0.22f), steel, new Vector3(1.62f, 1.82f, -4.3f), new Vector3(0, 0, 25));
        Add(solid, Props.Box(0.4f, 0.06f, 0.55f), steel, new Vector3(1.35f, 0.02f, -3.6f));
        // The cabin showing through the open door: seat, backrest, steering wheel and column, dashboard, gear lever.
        Add(solid, Props.Box(0.95f, 0.22f, 0.7f), seat, new Vector3(0.45f, 0.45f, -3.1f));
        Add(solid, Props.Box(0.95f, 0.75f, 0.12f), seat, new Vector3(0.45f, 0.85f, -2.65f), new Vector3(8, 0, 0));
        Add(solid, Props.Cyl(0.19f, 0.04f, 10), dark, new Vector3(0.45f, 1.0f, -3.85f), new Vector3(70, 0, 0));
        Add(solid, Props.Cyl(0.035f, 0.5f, 5), dark, new Vector3(0.45f, 0.85f, -3.95f), new Vector3(70, 0, 0));
        Add(solid, Props.Box(2.2f, 0.35f, 0.3f), greenDark, new Vector3(0, 1.0f, -4.05f));
        Add(solid, Props.Cyl(0.02f, 0.45f, 4), dark, new Vector3(0.05f, 0.62f, -3.4f), new Vector3(15, 0, 0));
        Add(solid, Props.Ball(0.05f, 5), dark, new Vector3(0.05f, 0.85f, -3.35f));
        // The right door, torn from its latch and hanging open on the front hinge: a skin, a window frame, a handle.
        var door = new Node3D { Position = new Vector3(1.2f, 1.0f, -4.15f), RotationDegrees = new Vector3(0, 72, 10) };
        solid.AddChild(door);
        Add(door, Props.Box(0.06f, 1.6f, 1.0f), green, new Vector3(0, 0, 0.5f));
        Add(door, Props.Box(0.07f, 0.55f, 0.75f), glassLight * 0.55f, new Vector3(0.01f, 0.45f, 0.5f));
        Add(door, Props.Box(0.1f, 0.05f, 0.2f), pale, new Vector3(0.05f, 0.0f, 0.85f));
        Add(door, Props.Box(0.08f, 0.5f, 0.04f), Rust, new Vector3(0.03f, -0.4f, 0.2f));
        // ---- the bonnet: the top (scorched at the front), the left side, and the right side panel bent open.
        Add(solid, Props.Box(1.15f, 0.1f, 1.3f), green * 0.9f, new Vector3(0, 0.98f, -4.82f), new Vector3(-7, 0, 0));
        Add(solid, Props.Box(0.95f, 0.02f, 0.6f), Soot, new Vector3(0, 1.05f, -5.1f), new Vector3(-7, 0, 0));
        Add(solid, Props.Box(0.05f, 0.7f, 1.3f), green * 0.85f, new Vector3(-0.58f, 0.62f, -4.82f));
        var panel = new Node3D { Position = new Vector3(0.58f, 0.95f, -4.82f), RotationDegrees = new Vector3(0, 0, 72) };
        solid.AddChild(panel);
        Add(panel, Props.Box(0.05f, 0.7f, 1.3f), green * 0.85f, new Vector3(0, -0.35f, 0));
        for (int k = 0; k < 5; k++) Add(panel, Props.Box(0.02f, 0.035f, 0.65f), dark, new Vector3(0.03f, -0.15f - k * 0.09f, 0));
        Add(panel, Props.Box(0.06f, 0.08f, 0.3f), Soot, new Vector3(0.01f, -0.62f, 0.45f));
        // The engine in the open bay: block, two heads with valve covers, the air cleaner, a manifold, hoses, a belt and the battery.
        Add(solid, Props.Box(0.75f, 0.5f, 0.9f), engine, new Vector3(0, 0.4f, -4.78f));
        foreach (float x in new[] { -0.22f, 0.22f })
        {
            Add(solid, Props.Box(0.3f, 0.16f, 0.85f), engine * 1.1f, new Vector3(x, 0.72f, -4.78f));
            Add(solid, Props.Box(0.26f, 0.08f, 0.7f), valve, new Vector3(x, 0.84f, -4.78f));
        }
        Add(solid, Props.Cyl(0.2f, 0.28f, 10), steel, new Vector3(0.1f, 0.98f, -4.6f));
        Add(solid, Props.Cyl(0.23f, 0.06f, 10), dark, new Vector3(0.1f, 1.14f, -4.6f));
        Add(solid, Props.Box(0.12f, 0.12f, 0.8f), Soot, new Vector3(0.45f, 0.5f, -4.78f));                 // the exhaust manifold, black
        Bar(new Vector3(-0.2f, 0.9f, -5.2f), new Vector3(0.1f, 0.95f, -4.75f), 0.07f, dark);               // hoses to the radiator
        Bar(new Vector3(0.3f, 0.35f, -5.2f), new Vector3(0.05f, 0.35f, -4.9f), 0.07f, dark);
        Add(solid, Props.Cyl(0.12f, 0.04f, 8), dark, new Vector3(0, 0.45f, -5.0f), new Vector3(90, 0, 0));    // the belt pulley
        Add(solid, Props.Box(0.38f, 0.3f, 0.26f), dark, new Vector3(0.7f, 0.25f, -4.3f));                      // the battery
        // The radiator with its fan and the grille frame and emblem at the front.
        Add(solid, Props.Box(1.3f, 0.75f, 0.1f), engine * 0.9f, new Vector3(0, 0.5f, -5.3f));
        for (int k = 0; k < 6; k++) Add(solid, Props.Box(1.2f, 0.02f, 0.04f), steel, new Vector3(0, 0.25f + k * 0.1f, -5.36f));
        Add(solid, Props.Cyl(0.36f, 0.03f, 12), dark, new Vector3(0, 0.55f, -5.15f), new Vector3(90, 0, 0));
        for (int k = 0; k < 4; k++) Add(solid, Props.Box(0.7f, 0.06f, 0.015f), steel, new Vector3(0, 0.55f, -5.17f), new Vector3(0, 0, k * 45f));
        foreach (float x in new[] { -0.82f, 0.82f }) Add(solid, Props.Box(0.08f, 0.75f, 0.1f), steel, new Vector3(x, 0.5f, -5.5f));
        Add(solid, Props.Box(1.7f, 0.08f, 0.1f), steel, new Vector3(0, 0.85f, -5.5f));
        Add(solid, Props.Box(0.2f, 0.2f, 0.04f), new Color(0.75f, 0.1f, 0.08f), new Vector3(0, 0.9f, -5.55f), new Vector3(0, 0, 45));   // the star emblem
        // The wings: the right one dented, the lamps (the left one smashed), the bumper bent with tow hooks, the winch roller.
        Add(solid, Props.Box(0.5f, 0.1f, 1.3f), green, new Vector3(1.0f, 0.75f, -4.85f), new Vector3(0, 0, -8));
        Add(solid, Props.Box(0.08f, 0.5f, 1.3f), greenDark, new Vector3(1.22f, 0.5f, -4.85f), new Vector3(0, 8, 0));
        Add(solid, Props.Box(0.5f, 0.1f, 1.3f), green, new Vector3(-1.0f, 0.75f, -4.85f));
        Add(solid, Props.Cyl(0.19f, 0.07f, 10), steel, new Vector3(0.97f, 0.66f, -5.5f), new Vector3(90, 0, 0));
        Add(solid, Props.Cyl(0.15f, 0.06f, 10), new Color(0.9f, 0.85f, 0.6f), new Vector3(0.97f, 0.66f, -5.55f), new Vector3(90, 0, 0));
        Add(solid, Props.Cyl(0.19f, 0.07f, 10), steel, new Vector3(-0.97f, 0.66f, -5.5f), new Vector3(90, 0, 0));
        Add(solid, Props.Cyl(0.15f, 0.04f, 6), dark, new Vector3(-0.97f, 0.66f, -5.53f), new Vector3(90, 0, 0));
        Add(solid, Props.Box(2.5f, 0.24f, 0.24f), steel, new Vector3(-0.1f, 0.0f, -5.68f), new Vector3(0, 6, 0));
        Add(solid, Props.Box(0.9f, 0.24f, 0.24f), steel, new Vector3(1.2f, 0.12f, -5.5f), new Vector3(0, 55, 12));
        foreach (float x in new[] { -0.5f, 0.55f }) Add(solid, Props.Cyl(0.05f, 0.25f, 5), dark, new Vector3(x, -0.02f, -5.85f), new Vector3(90, 0, 0));
        Add(solid, Props.Cyl(0.13f, 1.7f, 8), Rust, new Vector3(0, -0.2f, -5.55f), new Vector3(0, 0, 90));
        // The chassis: cross-members, leaf springs on the axles, the axles themselves, a drive shaft, the fuel tank, the exhaust stack.
        foreach (float z in new[] { -5.0f, -2.0f, 0.4f, 3.4f })
            Add(solid, Props.Box(1.3f, 0.12f, 0.14f), Rust, new Vector3(0, -0.22f, z));
        foreach (float z in new[] { -4.0f, 1.4f, 3.1f })
        {
            Add(solid, Props.Cyl(0.08f, 2.3f, 6), steel, new Vector3(0, -0.55f, z), new Vector3(0, 0, 90));
            foreach (float x in new[] { -0.65f, 0.65f }) Add(solid, Props.Box(0.12f, 0.1f, 1.3f), dark, new Vector3(x, -0.38f, z));
        }
        Add(solid, Props.Ball(0.22f, 6), steel, new Vector3(0, -0.55f, 1.4f));
        Add(solid, Props.Ball(0.22f, 6), steel, new Vector3(0, -0.55f, 3.1f));
        Add(solid, Props.Cyl(0.05f, 3.3f, 5), dark, new Vector3(0, -0.4f, -0.2f), new Vector3(90, 0, 0));
        Add(solid, Props.Cyl(0.34f, 1.5f, 10), engine, new Vector3(0.95f, -0.1f, -1.0f), new Vector3(90, 0, 0));
        foreach (float z in new[] { -1.55f, -0.45f }) Add(solid, Props.Box(0.08f, 0.8f, 0.06f), steel, new Vector3(0.95f, -0.1f, z));
        Add(solid, Props.Cyl(0.07f, 2.3f, 6), Soot, new Vector3(1.05f, 1.15f, -2.2f));
        Add(solid, Props.Cyl(0.11f, 0.08f, 6), Soot, new Vector3(1.05f, 2.33f, -2.2f));
        Bar(new Vector3(1.05f, 0.6f, -2.3f), new Vector3(1.2f, 0.6f, -2.45f), 0.04f, steel);
        // Wheel rims and hubs (the tyres are the bare cylinders added with the frame).
        foreach (float z in new[] { -4.0f, 1.4f, 3.1f })
        foreach (float x in new[] { -1.15f, 1.15f })
        {
            Add(solid, Props.Cyl(0.33f, 0.5f, 10), steel * 0.85f, new Vector3(x, -0.55f, z), new Vector3(0, 0, 90));
            Add(solid, Props.Cyl(0.14f, 0.56f, 8), dark, new Vector3(x, -0.55f, z), new Vector3(0, 0, 90));
        }
        DrunkTractor.BatchStatic(solid, null);
        truck.AddChild(solid);
        truck.AddChild(Buildings.TextPanel("ЧИ 17-52", 0.7f, 0.16f, new Vector3(-0.4f, 0.1f, -5.82f), pale, dark));      // the number plate, on the bumper
    }

    static StandardMaterial3D Mat() => new()
    {
        VertexColorUseAsAlbedo = true,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.PerVertex,
        Roughness = 1f,
        SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    static Mesh Stack(int seed)
    {
        var rng = new RandomNumberGenerator { Seed = (ulong)seed };
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        int[] rows = { 6, 5, 4, 2 + rng.RandiRange(0, 1) };
        float y = 0f, top = 0f;
        const float R = 0.22f;
        for (int r = 0; r < rows.Length; r++)
        {
            float w = rows[r] * 2f * R;
            for (int k = 0; k < rows[r]; k++)
            {
                float rad = R * rng.RandfRange(0.75f, 1.1f);
                float x = -w / 2f + R + k * 2f * R + rng.RandfRange(-0.04f, 0.04f);
                float len = Length * rng.RandfRange(0.85f, 1.05f), dz = rng.RandfRange(-0.25f, 0.25f);
                Log(st, new Vector3(x, y + rad, dz), rad, len, rng.Randf() < 0.5f ? Bark : BarkDark);
                top = Math.Max(top, y + 2f * rad);
            }
            y += 2f * R * 0.87f;                               // the next row sits in the grooves
        }
        // Snow on the top: a low ridge along the pile, a little wider than the top row.
        float sw = rows[^1] * 2f * R + 0.5f;
        Prism(st, new Vector3(0, top - 0.06f, 0), sw, 0.28f, Length * 0.95f, Snow);
        st.GenerateNormals();
        st.SetMaterial(new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.PerVertex,
            Roughness = 1f,
            SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        });
        return st.Commit();
    }

    /// <summary>A log lying along z: 6-sided bark, pale sawn ends.</summary>
    static void Log(SurfaceTool st, Vector3 c, float r, float len, Color bark)
    {
        const int Seg = 6;
        float z0 = c.Z - len / 2f, z1 = c.Z + len / 2f;
        for (int k = 0; k < Seg; k++)
        {
            float a0 = k * MathF.Tau / Seg, a1 = (k + 1) * MathF.Tau / Seg;
            Vector3 p0 = new(c.X + MathF.Cos(a0) * r, c.Y + MathF.Sin(a0) * r, 0), p1 = new(c.X + MathF.Cos(a1) * r, c.Y + MathF.Sin(a1) * r, 0);
            Quad(st, p0 + Vector3.Back * z0, p1 + Vector3.Back * z0, p1 + Vector3.Back * z1, p0 + Vector3.Back * z1, bark);
            Tri(st, new Vector3(c.X, c.Y, z0), p1 + Vector3.Back * z0, p0 + Vector3.Back * z0, Cut);
            Tri(st, new Vector3(c.X, c.Y, z1), p0 + Vector3.Back * z1, p1 + Vector3.Back * z1, Cut);
        }
    }

    static void Prism(SurfaceTool st, Vector3 baseC, float w, float h, float len, Color c)
    {
        Vector3 l0 = baseC + new Vector3(-w / 2f, 0, -len / 2f), r0 = baseC + new Vector3(w / 2f, 0, -len / 2f), t0 = baseC + new Vector3(0, h, -len / 2f);
        Vector3 dz = new(0, 0, len);
        Quad(st, l0, t0, t0 + dz, l0 + dz, c);
        Quad(st, t0, r0, r0 + dz, t0 + dz, c);
        Tri(st, l0, r0, t0, c);
        Tri(st, l0 + dz, t0 + dz, r0 + dz, c);
    }

    static void Quad(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col)
    {
        Tri(st, a, b, c, col);
        Tri(st, a, c, d, col);
    }

    static void Tri(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Color col)
    {
        st.SetColor(col); st.AddVertex(a);
        st.SetColor(col); st.AddVertex(b);
        st.SetColor(col); st.AddVertex(c);
    }
}

/// <summary>A wreck's engine smouldering: dark smoke puffing up from it, leaning with the wind.</summary>
public partial class WreckSmoke : Node3D
{
    public TyreEffects Fx;
    float _puff;
    readonly RandomNumberGenerator _rng = new() { Seed = 1980 };

    public override void _Process(double delta)
    {
        if (Fx == null) return;
        _puff -= (float)delta;
        if (_puff > 0) return;
        _puff = 0.18f;
        Vector3 at = GlobalPosition + new Vector3(_rng.RandfRange(-0.4f, 0.4f), 0, _rng.RandfRange(-0.4f, 0.4f));
        float g = _rng.RandfRange(0.12f, 0.24f);
        Fx.Smoke(at, new Vector3(0.6f, _rng.RandfRange(1.4f, 2.2f), 0.2f), new Color(g, g, g), 0.6f, 0.6f, 3.2f, 4.5f);
    }
}
