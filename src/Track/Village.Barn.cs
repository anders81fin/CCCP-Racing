using System;
using Godot;

namespace CCCPRacing.Track;

public static partial class Village
{
    /// <summary>Weathered cladding and race dressings for the start barn, batched into two
    /// surfaces: walls/doors/timber framing and the sheet-metal details that fade with the roof.</summary>
    static void AddBarnDetails(Node3D barn, FadeUnder roof, float half, float length, float line)
    {
        var walls = new SurfaceTool();
        var overhead = new SurfaceTool();
        walls.Begin(Mesh.PrimitiveType.Triangles);
        overhead.Begin(Mesh.PrimitiveType.Triangles);
        float wx = half + 0.25f, end = length / 2;
        float roofHalf = wx + 0.8f;
        var faded = new Color(0.48f, 0.26f, 0.18f);
        var bare = new Color(0.46f, 0.39f, 0.29f);
        var trim = new Color(0.67f, 0.62f, 0.49f);
        var iron = new Color(0.18f, 0.19f, 0.18f);
        var hay = new Color(0.72f, 0.58f, 0.28f);
        var roofSeam = new Color(0.21f, 0.23f, 0.23f);
        var roofPatch = new Color(0.4f, 0.32f, 0.26f);

        void Box(SurfaceTool st, Vector3 size, Color color, Vector3 at, Basis? basis = null) =>
            Fences.AddBox(st, new Transform3D(basis ?? Basis.Identity, at), size, color);
        void Beam(SurfaceTool st, Vector3 a, Vector3 b, float width, Color color)
        {
            Vector3 d = b - a;
            Box(st, new Vector3(width, d.Length(), width), color, (a + b) / 2,
                new Basis(new Quaternion(Vector3.Up, d.Normalized())));
        }

        foreach (float side in new[] { -1f, 1f })
        {
            // Wide tonal variation reads as old paint and replacement planks from the race camera.
            int count = (int)MathF.Ceiling(length / 0.65f);
            float step = length / count;
            for (int i = 0; i < count; i++)
            {
                float z = -end + (i + 0.5f) * step;
                Color color = i % 11 == 3 ? bare : i % 4 == 0 ? faded : Plank;
                Box(walls, new Vector3(0.045f, BarnHeight, step - 0.045f), color,
                    new Vector3(side * (wx + 0.275f), BarnHeight / 2, z));
                if (i % 5 == 1)
                    Box(walls, new Vector3(0.055f, 1.05f, step - 0.08f), bare.Darkened(0.12f),
                        new Vector3(side * (wx + 0.305f), 0.6f, z));
            }
            foreach (float y in new[] { 0.3f, 4.8f })
                Box(walls, new Vector3(0.16f, 0.24f, length + 0.25f), Timber,
                    new Vector3(side * (wx + 0.29f), y, 0));
            foreach (float e in new[] { -1f, 1f })
            {
                float z = e * end;
                // Pale, dirty door jambs rather than bright white corner boards.
                Box(walls, new Vector3(0.5f, BarnHeight + 0.1f, 0.18f), trim,
                    new Vector3(side * wx, BarnHeight / 2, z + e * 0.4f));
                // Open leaves stay beside the walls, clear of the drive-through corridor.
                var doorBasis = new Basis(Vector3.Up, Mathf.DegToRad(side * e * 70));
                var doorAt = new Vector3(side * (wx + 1.9f), 2.1f, e * (end + 0.15f));
                void DoorBox(Vector3 size, Color color, Vector3 at) =>
                    Box(walls, size, color, doorAt + doorBasis * at, doorBasis);
                DoorBox(new Vector3(3.4f, 4.2f, 0.16f), PlankDark, Vector3.Zero);
                for (int i = 0; i < 7; i++)
                    DoorBox(new Vector3(0.44f, 4.12f, 0.045f), i % 3 == 0 ? faded : Plank,
                        new Vector3(-1.44f + i * 0.48f, 0, e * 0.11f));
                foreach (float y in new[] { -1.65f, 1.65f })
                    DoorBox(new Vector3(3.3f, 0.24f, 0.16f), bare, new Vector3(0, y, e * 0.17f));
                Vector3 a = doorAt + doorBasis * new Vector3(-1.5f, -1.55f, e * 0.18f);
                Vector3 b = doorAt + doorBasis * new Vector3(1.5f, 1.55f, e * 0.18f);
                Beam(walls, a, b, 0.22f, bare);
                foreach (float y in new[] { -1.45f, 1.45f })
                    DoorBox(new Vector3(0.85f, 0.16f, 0.08f), iron,
                        new Vector3(-side * 1.15f, y, e * 0.28f));

                // Attached flag poles mark both portals without intruding into the road.
                float fx = side * (wx + 0.6f), fz = z + e * 0.5f;
                Box(walls, new Vector3(0.1f, 6.8f, 0.1f), iron, new Vector3(fx, 3.4f, fz));
                Box(walls, new Vector3(1.35f, 1.7f, 0.055f), Red,
                    new Vector3(fx + side * 0.7f, 5.75f, fz));
                Box(walls, new Vector3(0.16f, 0.5f, 0.07f), Gold,
                    new Vector3(fx + side * 0.3f, 6.05f, fz + e * 0.04f));
            }
        }

        // Loft strips run along the walls, leaving the middle open above the driving line.
        float loftWidth = Math.Min(2.1f, half * 0.3f);
        foreach (float side in new[] { -1f, 1f })
        {
            float x = side * (half - loftWidth / 2);
            Box(walls, new Vector3(loftWidth, 0.25f, length - 3), bare, new Vector3(x, 4.45f, 0));
            for (float z = -end + 2.8f; z < end - 2; z += 3.5f)
            {
                Box(walls, new Vector3(loftWidth * 0.85f, 0.65f, 1.8f), hay,
                    new Vector3(x, 4.9f, z));
                foreach (float dz in new[] { -0.5f, 0.5f })
                    Box(walls, new Vector3(loftWidth * 0.88f, 0.045f, 0.08f), Timber,
                        new Vector3(x, 5.25f, z + dz));
            }
        }
        // Repeated timber trusses remain visible when the sheet-metal roof fades over the car.
        for (float z = -end; z <= end + 0.01f; z += length / 4)
        {
            Beam(walls, new Vector3(-wx, 4.75f, z), new Vector3(wx, 4.75f, z), 0.28f, Timber);
            foreach (float side in new[] { -1f, 1f })
            {
                Beam(walls, new Vector3(side * wx, 4.85f, z), new Vector3(0, 8.2f, z), 0.24f, bare);
                Beam(walls, new Vector3(side * wx, 3.5f, z), new Vector3(side * (wx - 1.8f), 4.75f, z), 0.24f, Timber);
            }
            Beam(walls, new Vector3(0, 4.75f, z), new Vector3(0, 8.2f, z), 0.2f, Timber);
        }
        // Standing seams and broad repairs on the old sheet-metal roof.
        float slope = MathF.Atan2(3.4f, roofHalf);
        foreach (float side in new[] { -1f, 1f })
        {
            var slopeBasis = new Basis(Vector3.Forward, side * slope);
            for (float z = -end; z <= end + 0.01f; z += 1.7f)
                Box(overhead, new Vector3(MathF.Sqrt(roofHalf * roofHalf + 3.4f * 3.4f), 0.08f, 0.09f), roofSeam,
                    new Vector3(side * roofHalf / 2, 6.75f, z), slopeBasis);
            foreach (float z in new[] { -end * 0.65f, end * 0.4f })
                Box(overhead, new Vector3(2.4f, 0.055f, 2.6f), roofPatch,
                    new Vector3(side * roofHalf * 0.6f, 6.43f, z), slopeBasis);
        }
        Box(overhead, new Vector3(0.3f, 0.18f, length + 1.1f), Tin,
            new Vector3(0, 8.47f, 0));

        // A dedicated lamp housing and suspension over the starting stripe.
        Box(walls, new Vector3(half * 1.75f, 0.12f, 0.12f), iron,
            new Vector3(0, 4.95f, line));
        foreach (float x in new[] { -half * 0.65f, half * 0.65f })
        {
            Box(walls, new Vector3(0.08f, 0.5f, 0.08f), iron, new Vector3(x, 4.7f, line));
            Box(walls, new Vector3(0.85f, 0.2f, 0.55f), iron, new Vector3(x, 4.4f, line));
            barn.AddChild(NightGlow.Part(Props.Box(0.65f, 0.08f, 0.4f), Lamp, Lamp, 1.6f,
                new Vector3(x, 4.27f, line)));
        }
        StandardMaterial3D Material() => new()
        {
            VertexColorUseAsAlbedo = true,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.PerVertex,
            Roughness = 1f,
            SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
        };
        walls.Index();
        overhead.Index();
        barn.AddChild(new MeshInstance3D
        {
            Name = "BarnWallDetails", Mesh = walls.Commit(), MaterialOverride = Material(),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        var roofDetails = roof.Part(overhead.Commit(), Colors.White, Vector3.Zero);
        roofDetails.Name = "BarnRoofDetails";
        var roofMaterial = (StandardMaterial3D)roofDetails.MaterialOverride;
        roofMaterial.VertexColorUseAsAlbedo = true;
        // Transparent roof meshes sort by node origin; draw the raised sheet details last.
        roofMaterial.RenderPriority = 1;
        roof.AddChild(roofDetails);
    }
}
