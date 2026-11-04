using System;
using Godot;

namespace CCCPRacing.Track;

public static partial class Village
{
    public const float MonumentScale = 1.33f;
    public const float MonumentApproachLength = 11f;

    /// <summary>A ceremonial square outside the church hairpin's stone wall, facing the village.</summary>
    public static Node3D MonumentSquare(TrackData t)
    {
        var root = new Node3D { Name = "MonumentSquare" };
        if (t.Id != "ilyich") return root;
        var statue = t.Props.Find(p => p.Type == "statue");
        if (statue == null) return root;
        var basis = new Basis(Vector3.Up, statue.Rotation);
        Vector3 At(float x, float z, float height = 0)
        {
            var p = statue.Position + basis * new Vector3(x, 0, z);
            return new Vector3(p.X, t.MeshHeight(p.X, p.Z) + height, p.Z);
        }
        var solid = new Node3D();
        var ground = new SurfaceTool();
        ground.Begin(Mesh.PrimitiveType.Triangles);
        // Small slabs follow the hillside rather than floating as one rigid platform.
        void Paving(float x0, float z0, float x1, float z1)
        {
            for (float z = z0; z < z1; z += 1f)
            for (float x = x0; x < x1; x += 1f)
            {
                var color = new Color(0.27f, 0.25f, 0.21f) * (0.93f + 0.12f * Noise.Hash01((int)(x * 10), (int)(z * 10), 1922));
                Vector3 a = At(x, z, 0.04f), b = At(Math.Min(x + 1, x1), z, 0.04f);
                Vector3 c = At(Math.Min(x + 1, x1), Math.Min(z + 1, z1), 0.04f), d = At(x, Math.Min(z + 1, z1), 0.04f);
                foreach (var p in new[] { a, c, b, a, d, c })
                { ground.SetColor(color); ground.SetNormal(Vector3.Up); ground.AddVertex(p); }
            }
        }
        Paving(-7, -8, 7, 5);
        Paving(-1.5f, -MonumentApproachLength, 1.5f, -8);
        ground.Index();
        root.AddChild(new MeshInstance3D { Name = "ParadePaving", Mesh = ground.Commit(),
            MaterialOverride = new StandardMaterial3D { VertexColorUseAsAlbedo = true,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.PerVertex, Roughness = 1,
                SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled, CullMode = BaseMaterial3D.CullModeEnum.Disabled },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        void Box(float x, float z, Vector3 size, Color color, float height)
        {
            var part = Props.Part(Props.Box(size.X, size.Y, size.Z), color, At(x, z, height));
            part.Basis = basis;
            solid.AddChild(part);
        }
        var curb = new Color(0.56f, 0.54f, 0.47f);
        for (float z = -7.5f; z < 5; z++)
            foreach (float x in new[] { -7f, 7f }) Box(x, z, new Vector3(0.24f, 0.18f, 0.94f), curb, 0.09f);
        for (float x = -6.5f; x < 7; x++)
        {
            Box(x, 5, new Vector3(0.94f, 0.18f, 0.24f), curb, 0.09f);
            if (MathF.Abs(x) > 1.5f) Box(x, -8, new Vector3(0.94f, 0.18f, 0.24f), curb, 0.09f);
        }
        // Symmetric red and cream flower beds frame the approach; the existing wreath stays at the plinth.
        foreach (float x in new[] { -4.8f, 4.8f })
        {
            Box(x, -4.5f, new Vector3(1.35f, 0.2f, 3f), curb, 0.1f);
            Box(x, -4.5f, new Vector3(1.1f, 0.12f, 2.75f), new Color(0.22f, 0.15f, 0.09f), 0.22f);
            for (int k = 0; k < 8; k++)
                Box(x + (k % 2 == 0 ? -0.28f : 0.28f), -5.6f + k / 2 * 0.7f,
                    new Vector3(0.3f, 0.24f, 0.3f), k % 3 == 0 ? Line : Red, 0.4f);
        }
        var warm = new Color(1f, 0.82f, 0.56f);
        foreach (float x in new[] { -3.6f, 3.6f })
        {
            Box(x, -5.7f, new Vector3(0.65f, 0.22f, 0.55f), Tin, 0.2f);
            root.AddChild(NightGlow.Part(Props.Box(0.45f, 0.1f, 0.4f), Lamp, warm, 2f, At(x, -5.7f, 0.36f)));
            var light = new SpotLight3D { Name = "MonumentFloodlight", Position = At(x, -5.7f, 0.5f),
                LightColor = warm, LightEnergy = 0, SpotRange = 14, SpotAngle = 38,
                ShadowEnabled = false, LightSpecular = 0, Visible = false };
            light.Basis = Basis.LookingAt(At(0, 0, 4.6f) - light.Position, Vector3.Up);
            light.SetMeta("night_energy", 7f);
            root.AddChild(light);
        }
        DrunkTractor.BatchStatic(solid, null);
        root.AddChild(solid);
        return root;
    }
}
