using System;
using Godot;

namespace CCCPRacing.Track;

public partial class ZoneLife
{
    static void BuildTrainBody(Node3D root)
    {
        var st = new SurfaceTool(); st.Begin(Mesh.PrimitiveType.Triangles);
        Color green = new(0.23f, 0.36f, 0.27f), yellow = new(0.72f, 0.59f, 0.24f);
        Color frame = new(0.15f, 0.16f, 0.15f), steel = new(0.32f, 0.33f, 0.3f);
        Color rust = new(0.4f, 0.24f, 0.16f), darkRust = new(0.27f, 0.18f, 0.14f);
        Color glass = new(0.19f, 0.25f, 0.27f), rubber = new(0.09f, 0.1f, 0.09f);
        void Box(float w, float h, float l, Color color, float x, float y, float z) =>
            Fences.AddBox(st, new Transform3D(Basis.Identity, new Vector3(x, y, z)), new Vector3(w, h, l), color);
        void Cylinder(float r, float length, Color color, Vector3 at, Vector3 rotation)
        {
            var mesh = Props.Cyl(r, length, 16);
            var arrays = DrunkTractor.PrimitiveArrays(mesh);
            var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var normals = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
            var basis = Basis.FromEuler(rotation * (Mathf.Pi / 180f));
            foreach (int index in arrays[(int)Mesh.ArrayType.Index].AsInt32Array())
            {
                st.SetColor(color); st.SetNormal(basis * normals[index]); st.AddVertex(basis * vertices[index] + at);
            }
        }
        void Bogie(float z)
        {
            Box(1.9f, 0.34f, 2.2f, frame, 0, 0.88f, z);
            foreach (float axle in new[] { -0.72f, 0.72f })
            {
                Cylinder(0.1f, 1.85f, steel, new Vector3(0, 0.73f, z + axle), new Vector3(0, 0, 90));
                foreach (float side in new[] { -1f, 1f })
                {
                    Cylinder(0.45f, 0.22f, rubber, new Vector3(side * 0.82f, 0.73f, z + axle), new Vector3(0, 0, 90));
                    Cylinder(0.22f, 0.025f, steel, new Vector3(side * 0.945f, 0.73f, z + axle), new Vector3(0, 0, 90));
                    Box(0.22f, 0.24f, 0.32f, frame, side * 1.03f, 0.75f, z + axle);
                }
            }
            foreach (float side in new[] { -1f, 1f })
            {
                Box(0.16f, 0.25f, 2.3f, steel, side * 1.02f, 0.95f, z);
                Box(0.2f, 0.16f, 0.6f, rubber, side * 1.04f, 1.08f, z);
            }
        }
        void Chassis(float z, float length)
        {
            Box(2.85f, 0.26f, length, frame, 0, 1.3f, z);
            foreach (float x in new[] { -1.22f, 1.22f }) Box(0.16f, 0.35f, length, darkRust, x, 1.15f, z);
            Bogie(z - length * 0.31f); Bogie(z + length * 0.31f);
            foreach (float end in new[] { -1f, 1f })
            {
                Box(0.35f, 0.22f, 0.65f, frame, 0, 1.05f, z + end * (length / 2 + 0.2f));
                foreach (float x in new[] { -0.95f, 0.95f })
                    Cylinder(0.17f, 0.18f, frame, new Vector3(x, 1.1f, z + end * (length / 2 + 0.12f)), new Vector3(90, 0, 0));
            }
        }
        Node3D Flush(string name, float centre, float halfWheelbase)
        {
            var vehicle = new Node3D { Name = name };
            vehicle.SetMeta("rail_center", centre);
            vehicle.SetMeta("rail_halfbase", halfWheelbase);
            vehicle.AddChild(new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = new StandardMaterial3D {
                VertexColorUseAsAlbedo = true, Roughness = 1f, SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled
            } });
            root.AddChild(vehicle);
            st = new SurfaceTool(); st.Begin(Mesh.PrimitiveType.Triangles);
            return vehicle;
        }
        // Long, narrow engine hood and rear cab, with exposed side walkways: a diesel shunter.
        Chassis(-6.5f, 12.6f);
        Box(1.95f, 2.05f, 8.3f, green, 0, 2.47f, -8.4f);
        Box(2.02f, 0.16f, 8.4f, green.Lightened(0.08f), 0, 3.52f, -8.4f);
        Box(2.75f, 2.65f, 2.65f, green, 0, 2.85f, -2.75f);
        Box(2.95f, 0.16f, 2.9f, frame, 0, 4.25f, -2.75f);
        foreach (float s in new[] { -1f, 1f })
        {
            Box(0.035f, 0.8f, 1.8f, glass, s * 1.39f, 3.55f, -2.8f);
            Box(0.05f, 0.84f, 0.08f, green, s * 1.42f, 3.55f, -2.8f);
            Box(0.04f, 0.18f, 11.7f, yellow, s * 1.44f, 1.48f, -6.5f);
            for (int k = 0; k < 18; k++)
                Box(0.04f, 1.05f, 0.075f, frame, s * 0.99f, 2.66f, -11.8f + k * 0.25f);
            for (int k = 0; k < 6; k++) Box(0.045f, 0.84f, 0.045f, steel, s * 1.33f, 1.96f, -12.4f + k * 1.5f);
            Box(0.06f, 0.06f, 8.05f, steel, s * 1.33f, 2.38f, -8.4f);
            for (int k = 0; k < 3; k++) Box(0.36f, 0.07f, 0.7f, steel, s * 1.36f, 0.67f + k * 0.25f, -1.65f);
        }
        foreach (float x in new[] { -0.68f, 0.68f })
            Box(1.08f, 0.8f, 0.035f, glass, x, 3.56f, -4.09f);
        Box(2.2f, 0.8f, 0.035f, glass, 0, 3.56f, -1.405f);
        Cylinder(0.17f, 0.85f, frame, new Vector3(0.45f, 3.94f, -6.5f), Vector3.Zero);
        Box(0.55f, 0.13f, 0.45f, steel, 0.45f, 4.4f, -6.5f);
        Box(2.8f, 0.34f, 0.17f, yellow, 0, 1.6f, -12.89f);
        Box(1.4f, 1.15f, 0.035f, frame, 0, 2.55f, -12.57f);
        var locomotive = Flush("Locomotive", -6.5f, 3.9f);
        // Two ribbed covered wagons, an open scrap wagon, and a weathered tank wagon.
        for (int k = 0; k < 4; k++)
        {
            float z = 4 + k * 10;
            Chassis(z, 9.2f);
            if (k < 2)
            {
                var paint = k == 0 ? rust : new Color(0.34f, 0.3f, 0.23f);
                Box(2.85f, 2.6f, 9f, paint, 0, 2.75f, z);
                Box(2.95f, 0.2f, 9.15f, darkRust, 0, 4.14f, z);
                foreach (float side in new[] { -1f, 1f })
                {
                    for (int rib = 0; rib < 9; rib++) Box(0.09f, 2.65f, 0.09f, darkRust, side * 1.46f, 2.76f, z - 4.2f + rib * 1.05f);
                    Box(0.08f, 2.3f, 2.5f, paint.Darkened(0.16f), side * 1.49f, 2.65f, z);
                    Box(0.1f, 0.08f, 3f, steel, side * 1.52f, 3.85f, z);
                    Box(0.11f, 0.45f, 0.07f, steel, side * 1.55f, 2.55f, z - 0.85f);
                }
            }
            else if (k == 2)
            {
                foreach (float side in new[] { -1f, 1f })
                {
                    Box(0.13f, 1.35f, 9f, rust, side * 1.38f, 2.1f, z);
                    for (int rib = 0; rib < 9; rib++) Box(0.1f, 1.45f, 0.1f, darkRust, side * 1.47f, 2.1f, z - 4.2f + rib * 1.05f);
                    Box(2.85f, 1.35f, 0.12f, rust, 0, 2.1f, z + side * 4.45f);
                }
                for (int scrap = 0; scrap < 14; scrap++)
                {
                    var xf = new Transform3D(Basis.FromEuler(new Vector3(scrap * 0.17f, scrap * 1.8f, 0.2f)), new Vector3(MathF.Sin(scrap * 3.7f) * 0.6f, 2.4f + scrap % 3 * 0.15f, z - 3.8f + scrap * 0.58f));
                    Fences.AddBox(st, xf, new Vector3(0.4f, 0.3f, 1.8f), scrap % 2 == 0 ? steel : darkRust);
                }
            }
            else
            {
                Cylinder(1.35f, 7.9f, new Color(0.4f, 0.39f, 0.28f), new Vector3(0, 2.8f, z), new Vector3(90, 0, 0));
                foreach (float end in new[] { -1f, 1f })
                {
                    Cylinder(1.37f, 0.1f, darkRust, new Vector3(0, 2.8f, z + end * 2.6f), new Vector3(90, 0, 0));
                    Cylinder(1.18f, 0.18f, new Color(0.36f, 0.35f, 0.25f), new Vector3(0, 2.8f, z + end * 4f), new Vector3(90, 0, 0));
                }
                Cylinder(0.4f, 0.25f, steel, new Vector3(0, 4.19f, z), Vector3.Zero);
                Box(0.8f, 0.06f, 2.2f, steel, 0, 4.08f, z);
                foreach (float x in new[] { -0.38f, 0.38f }) Box(0.045f, 2.7f, 0.045f, steel, x, 2.8f, z + 4.15f);
                for (int rung = 0; rung < 9; rung++) Box(0.8f, 0.045f, 0.045f, steel, 0, 1.6f + rung * 0.3f, z + 4.15f);
            }
            Flush("Wagon" + k, z, 2.85f);
        }
        var headlamp = NightGlow.Part(Props.Cyl(0.22f, 0.07f, 16), new Color(0.85f, 0.8f, 0.59f), new Color(1f, 0.95f, 0.7f), 2f,
            new Vector3(0, 3.22f, -12.64f));
        headlamp.RotationDegrees = new Vector3(90, 0, 0);
        locomotive.AddChild(headlamp);
        var smokeGradient = new Gradient {
            Colors = new[] { new Color(0.19f, 0.2f, 0.18f, 0.22f), new Color(0.27f, 0.28f, 0.25f, 0f) },
            Offsets = new[] { 0f, 1f }
        };
        locomotive.AddChild(new GpuParticles3D {
            Name = "DieselExhaust", Position = new Vector3(0.45f, 4.45f, -6.5f),
            Amount = 12, Lifetime = 2.4f, LocalCoords = false,
            VisibilityAabb = new Aabb(new Vector3(-5, -2, -20), new Vector3(10, 15, 40)),
            DrawPass1 = new SphereMesh { Radius = 0.22f, Height = 0.44f, RadialSegments = 8, Rings = 4,
                Material = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 1f } },
            ProcessMaterial = new ParticleProcessMaterial {
                Direction = Vector3.Up, Spread = 12f, InitialVelocityMin = 0.7f, InitialVelocityMax = 1.3f,
                Gravity = new Vector3(0.18f, 0.25f, 0), ScaleMin = 0.8f, ScaleMax = 1.8f,
                ColorRamp = new GradientTexture1D { Gradient = smokeGradient }
            }
        });

    }
}
