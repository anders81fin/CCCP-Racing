using System;
using Godot;

namespace CCCPRacing.Track;

public partial class SnowPloughs
{
    // All decorative colour lies on the shell, including the helical paint bands.
    static MeshInstance3D MixerDrum()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        (float z, float r)[] profile = {
            (-2.15f, 0.28f), (-1.95f, 0.65f), (-1.5f, 1.12f),
            (-1.15f, 1.26f), (-0.5f, 1.29f), (0.4f, 1.24f),
            (1.1f, 1.02f), (1.8f, 0.62f), (2.3f, 0.4f)
        };
        const int sides = 64;
        for (int j = 0; j < profile.Length - 1; j++)
        {
            var a = profile[j]; var b = profile[j + 1];
            // Subdivide length so the painted stripe follows the taper without long jagged edges.
            for (int step = 0; step < 12; step++)
            for (int k = 0; k < sides; k++)
            {
                float t0 = step / 12f, t1 = (step + 1) / 12f;
                float z0 = Mathf.Lerp(a.z, b.z, t0), z1 = Mathf.Lerp(a.z, b.z, t1);
                float r0 = Mathf.Lerp(a.r, b.r, t0), r1 = Mathf.Lerp(a.r, b.r, t1);
                float angle0 = k * Mathf.Tau / sides, angle1 = (k + 1) * Mathf.Tau / sides;
                float phase = Mathf.PosMod((angle0 + angle1) * 0.5f, Mathf.Pi);
                st.SetColor(phase < 1.18f ? DrumStripe : Drum);
                Vector3 Point(float angle, float r, float z) => new(MathF.Cos(angle) * r, MathF.Sin(angle) * r, z);
                Vector3 Normal(float angle) => new Vector3(MathF.Cos(angle), MathF.Sin(angle), -(b.r - a.r) / (b.z - a.z)).Normalized();
                float[] angles = { angle0 + z0 * 1.4f, angle1 + z0 * 1.4f, angle1 + z1 * 1.4f, angle0 + z1 * 1.4f };
                Vector3[] q = { Point(angles[0], r0, z0), Point(angles[1], r0, z0), Point(angles[2], r1, z1), Point(angles[3], r1, z1) };
                foreach (int i in new[] { 0, 2, 1, 0, 3, 2 })
                {
                    st.SetNormal(Normal(angles[i]));
                    st.AddVertex(q[i]);
                }
            }
        }
        return new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = new StandardMaterial3D {
            VertexColorUseAsAlbedo = true, Roughness = 0.85f
        } };
    }

    static void BuildMixerCab(Node3D m)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
        {
            st.SetColor(color);
            st.SetNormal((b - a).Cross(c - a).Normalized());
            Vector3[] q = { a, b, c, d };
            foreach (int i in new[] { 0, 2, 1, 0, 3, 2 }) st.AddVertex(q[i]);
        }
        Vector3[] Ring(float y, float w, float front, float rear, float bevel) => new[] {
            new Vector3(-w + bevel, y, front), new Vector3(w - bevel, y, front),
            new Vector3(w, y, front + bevel), new Vector3(w, y, rear - bevel),
            new Vector3(w - bevel, y, rear), new Vector3(-w + bevel, y, rear),
            new Vector3(-w, y, rear - bevel), new Vector3(-w, y, front + bevel)
        };
        var rings = new[] { Ring(1.25f, 1.2f, -4.02f, -2.05f, 0.2f),
            Ring(2.25f, 1.2f, -4.02f, -2.05f, 0.2f), Ring(3.22f, 1.08f, -3.67f, -2.13f, 0.22f) };
        for (int j = 0; j < rings.Length - 1; j++)
        for (int k = 0; k < 8; k++)
        {
            int next = (k + 1) % 8;
            Quad(rings[j][k], rings[j + 1][k], rings[j + 1][next], rings[j][next], MixerCab);
        }
        for (int k = 1; k < 7; k++)
            Quad(rings[2][0], rings[2][k + 1], rings[2][k], rings[2][k], MixerCab);
        // Windscreen follows the sloping upper front; a narrow painted central pillar.
        foreach (float s in new[] { -1f, 1f })
        {
            float left = s < 0 ? -0.94f : 0.045f, right = s < 0 ? -0.045f : 0.94f;
            Quad(new(left, 2.36f, -3.99f), new(left * 0.88f, 3.08f, -3.73f),
                new(right * 0.88f, 3.08f, -3.73f), new(right, 2.36f, -3.99f), Glass);
            // Side panes taper inward with the roof.
            Vector3 a = new(s * 1.195f, 2.38f, -3.69f), b = new(s * 1.105f, 3.08f, -3.41f);
            Vector3 c = new(s * 1.105f, 3.08f, -2.43f), d = new(s * 1.195f, 2.38f, -2.35f);
            if (s > 0) Quad(a, b, c, d, Glass); else Quad(a, d, c, b, Glass);
            m.AddChild(Props.Part(Props.Box(0.08f, 0.06f, 0.28f), Steel, new Vector3(s * 1.21f, 2.15f, -2.5f)));
            m.AddChild(Props.Part(Props.Box(0.3f, 0.12f, 0.75f), Steel, new Vector3(s * 1.18f, 1.16f, -2.42f)));
            m.AddChild(Props.Part(Props.Box(0.12f, 0.12f, 0.48f), Steel, new Vector3(s * 1.3f, 2.65f, -3.5f)));
            m.AddChild(Props.Part(Props.Box(0.16f, 0.36f, 0.24f), Black, new Vector3(s * 1.43f, 2.67f, -3.63f)));
        }
        m.AddChild(new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = new StandardMaterial3D {
            VertexColorUseAsAlbedo = true, Roughness = 0.9f
        } });
        m.AddChild(Props.Part(Props.Box(2.45f, 0.19f, 0.22f), Steel, new Vector3(0, 1.24f, -4.06f)));
        m.AddChild(Props.Part(Props.Box(1.15f, 0.42f, 0.04f), Black, new Vector3(0, 1.7f, -4.045f)));
        for (int k = 0; k < 4; k++)
            m.AddChild(Props.Part(Props.Box(1.1f, 0.035f, 0.035f), Steel, new Vector3(0, 1.55f + k * 0.1f, -4.075f)));
    }
}
