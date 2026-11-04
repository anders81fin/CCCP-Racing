using System;
using Godot;

namespace CCCPRacing.Track;

/// <summary>Parked kolkhoz delivery tractor and its sack-loaded trailer beside the Ilyich grain store.</summary>
public static class GrainDelivery
{
    // The tractor faces west. The trailer sits behind it toward the church door, clear of the
    // road and the NW gate-to-door access path.
    public const float VisualScale = 1.6f;
    public const float X = 39.0f;
    public const float Z = 193.5f;
    public const float Yaw = Mathf.Pi / 2f;
    public const float TractorWidth = 2.4f * VisualScale;
    public const float TractorLength = 4.5f * VisualScale;
    public const float TrailerWidth = 2.1f * VisualScale;
    public const float TrailerLength = 3.1f * VisualScale;
    public const float TrailerOffset = 3.85f * VisualScale;

    public static Vector2 TractorPosition => new(X, Z);
    public static Vector2 TrailerPosition => TractorPosition + new Vector2(MathF.Sin(Yaw), MathF.Cos(Yaw)) * TrailerOffset;

    public static Node3D Build(TrackData t)
    {
        var root = new Node3D { Name = "ChurchGrainDelivery" };
        if (t.Id != "ilyich") return root;

        float baseY = t.MeshHeight(X, Z);
        root.Position = new Vector3(X, baseY, Z);
        root.Rotation = new Vector3(0, Yaw, 0);
        root.Scale = Vector3.One * VisualScale;

        var tractor = new Node3D { Name = "RedMTAgro" };
        root.AddChild(tractor);
        DrunkTractor.BuildMtAgro(tractor, plough: false, out var driver, out _,
            bodyColor: new Color(0.66f, 0.09f, 0.06f), batchStatic: true);
        driver.Visible = false;

        AddTrailer(root, t, baseY);
        return root;
    }

    /// <summary>Static collision circles for the tractor and trailer, matching the displayed rig.</summary>
    public static void AddObstacles(TrackData t)
    {
        if (t.Id != "ilyich") return;
        AddBox(t, TractorPosition, Yaw, TractorWidth, TractorLength);
        AddBox(t, TrailerPosition, Yaw, TrailerWidth, TrailerLength);
    }

    static void AddBox(TrackData t, Vector2 c, float yaw, float width, float length)
    {
        Vector2 along = new(MathF.Sin(yaw), MathF.Cos(yaw));
        float r = width / 2f;
        int count = Math.Max(1, (int)MathF.Ceiling(length / width));
        for (int i = 0; i < count; i++)
        {
            float offset = count == 1 ? 0 : Mathf.Lerp(-length / 2f + r, length / 2f - r, i / (float)(count - 1));
            t.Obstacles.Add(new Obstacle(c + along * offset, r));
        }
    }

    static void AddTrailer(Node3D root, TrackData t, float tractorY)
    {
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        var wood = new Color(0.28f, 0.19f, 0.11f);
        var woodLight = new Color(0.42f, 0.29f, 0.16f);
        var metal = new Color(0.22f, 0.23f, 0.22f);
        var tire = Props.Rubber;
        var grainSack = new Color(0.68f, 0.49f, 0.27f);
        var sackShade = new Color(0.59f, 0.4f, 0.22f);
        var rope = new Color(0.35f, 0.27f, 0.16f);

        void Box(Vector3 size, Vector3 at, Color color)
        {
            Vector3 rotated = new Basis(Vector3.Up, Yaw) * at;
            var grounded = new Vector3(at.X, at.Y + (t.MeshHeight(X + rotated.X * VisualScale, Z + rotated.Z * VisualScale) - tractorY) / VisualScale, at.Z);
            Fences.AddBox(tool, new Transform3D(Basis.Identity, grounded), size, color);
        }
        void Part(PrimitiveMesh mesh, Vector3 at, Color color, Vector3 scale = default, Vector3 rotation = default)
        {
            var basis = Basis.FromEuler(rotation);
            if (scale != default) basis = basis * Basis.FromScale(scale);
            var arrays = DrunkTractor.PrimitiveArrays(mesh);
            var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var normals = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
            foreach (int index in arrays[(int)Mesh.ArrayType.Index].AsInt32Array())
            {
                tool.SetColor(color);
                tool.SetNormal((basis * normals[index]).Normalized());
                tool.AddVertex(at + basis * vertices[index]);
            }
        }
        Vector3 OnGround(Vector3 local)
        {
            Vector3 rotated = new Basis(Vector3.Up, Yaw) * local;
            float x = X + rotated.X * VisualScale, z = Z + rotated.Z * VisualScale;
            return new Vector3(local.X, (t.MeshHeight(x, z) - tractorY) / VisualScale + local.Y, local.Z);
        }
        void GroundPart(PrimitiveMesh mesh, Vector3 at, Color color, Vector3 rotation = default)
        {
            var basis = Basis.FromEuler(rotation);
            var arrays = DrunkTractor.PrimitiveArrays(mesh);
            var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var normals = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
            foreach (int index in arrays[(int)Mesh.ArrayType.Index].AsInt32Array())
            {
                Vector3 localNormal = basis * normals[index];
                tool.SetColor(color);
                tool.SetNormal(localNormal.Normalized());
                tool.AddVertex(OnGround(at) + basis * vertices[index]);
            }
        }

        // A narrow V drawbar ties the trailer to the tractor's rear linkage.
        Box(new Vector3(0.14f, 0.13f, 0.78f), new Vector3(0, 0.58f, 2.28f), metal);
        Box(new Vector3(0.12f, 0.12f, 0.45f), new Vector3(0, 0.62f, 2.48f), woodLight);

        // Single-axle farm trailer: iron underframe, separate plank floor and raised side rails.
        Box(new Vector3(1.72f, 0.17f, 3.0f), new Vector3(0, 0.88f, 3.88f), wood);
        Box(new Vector3(0.12f, 0.26f, 3.08f), new Vector3(-0.88f, 0.69f, 3.88f), metal);
        Box(new Vector3(0.12f, 0.26f, 3.08f), new Vector3(0.88f, 0.69f, 3.88f), metal);
        Box(new Vector3(1.82f, 0.26f, 0.12f), new Vector3(0, 0.69f, 2.44f), metal);
        Box(new Vector3(1.82f, 0.26f, 0.12f), new Vector3(0, 0.69f, 5.32f), metal);
        for (int i = 0; i < 4; i++)
            Box(new Vector3(1.64f, 0.11f, 0.69f), new Vector3(0, 1.02f, 2.59f + i * 0.8f), i % 2 == 0 ? woodLight : wood);
        foreach (float side in new[] { -1f, 1f })
        {
            const float front = 2.48f, rear = 5.28f;
            const int spans = 3;
            const float spanLength = (rear - front) / spans;
            for (int i = 0; i <= spans; i++)
            {
                float z = front + i * spanLength;
                Box(new Vector3(0.12f, 0.8f, 0.12f), new Vector3(side * 0.87f, 1.39f, z), woodLight);
                // Rails fill only the gaps between posts; none continues beyond the rear post.
                if (i == spans) continue;
                float center = z + spanLength / 2f;
                Box(new Vector3(0.11f, 0.11f, spanLength - 0.12f), new Vector3(side * 0.87f, 1.14f, center), wood);
                Box(new Vector3(0.11f, 0.11f, spanLength - 0.12f), new Vector3(side * 0.87f, 1.61f, center), woodLight);
            }
        }
        foreach (float z in new[] { 2.48f, 5.28f })
            Box(new Vector3(1.72f, 0.68f, 0.12f), new Vector3(0, 1.38f, z), wood);

        // Wheels and axle, with the rubber kept broad enough to read in the overhead view.
        Box(new Vector3(2.0f, 0.15f, 0.16f), new Vector3(0, 0.52f, 3.92f), metal);
        foreach (float side in new[] { -1f, 1f })
        {
            GroundPart(Props.Cyl(0.43f, 0.18f, 10), new Vector3(side * 1.02f, 0.46f, 3.92f), tire, new Vector3(0, 0, Mathf.Pi / 2));
            GroundPart(Props.Cyl(0.22f, 0.2f, 10), new Vector3(side * 1.04f, 0.46f, 3.92f), metal, new Vector3(0, 0, Mathf.Pi / 2));
        }

        // Rounded burlap sacks fill the open bed in uneven, stacked rows. The contrasting tied
        // necks keep them legible as grain sacks from the fixed elevated camera.
        var bag = new SphereMesh { Radius = 0.42f, Height = 0.72f, RadialSegments = 8, Rings = 4 };
        foreach (float x in new[] { -0.42f, 0.42f })
        foreach (float z in new[] { 2.84f, 3.78f, 4.72f })
            Sack(new Vector3(x, 1.38f, z), grainSack, new Vector3(1f, 0.82f, 1.17f));
        foreach (float x in new[] { -0.39f, 0.39f })
        foreach (float z in new[] { 3.24f, 4.18f })
            Sack(new Vector3(x, 1.93f, z), sackShade, new Vector3(0.92f, 0.78f, 1.15f));
        Sack(new Vector3(0, 2.38f, 3.72f), grainSack.Lightened(0.05f), new Vector3(0.93f, 0.75f, 1.12f));

        void Sack(Vector3 at, Color color, Vector3 scale)
        {
            Part(bag, at, color, scale);
            Part(Props.Cyl(0.09f, 0.12f, 6), at + new Vector3(0, 0.31f * scale.Y, -0.02f), rope);
        }

        tool.Index();
        root.AddChild(new MeshInstance3D
        {
            Name = "SackLoadedTrailer",
            Mesh = tool.Commit(),
            MaterialOverride = new StandardMaterial3D
            {
                VertexColorUseAsAlbedo = true,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.PerVertex,
                Roughness = 1f,
                SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }
}
