using System;
using System.Collections.Generic;
using Godot;

namespace CCCPRacing.Track;

/// <summary>
/// Draws TrackData.Taiga: thousands of snowy conifers as MultiMeshes, one per kind and 64 m cell
/// (so what is off screen is culled), three low-poly models built once: a slim spruce, a broad
/// spruce, a pine; snow on every tier and on the crown, flat-shaded.
/// </summary>
public static class TaigaMesh
{
    const float Cell = 64f;
    static readonly Color Needles = new(0.06f, 0.15f, 0.08f), NeedlesLight = new(0.09f, 0.2f, 0.11f);
    static readonly Color Snow = new(0.95f, 0.96f, 0.99f), SnowShade = new(0.84f, 0.88f, 0.95f);
    static readonly Color Trunk = new(0.3f, 0.22f, 0.17f), PineBark = new(0.48f, 0.26f, 0.16f), PineLeaf = new(0.1f, 0.19f, 0.1f);

    public static Node3D Build(TrackData t)
    {
        Node3D result = null;
        foreach (var step in BuildSteps(t)) result = step ?? result;
        return result;
    }

    /// <summary>The same build in slices (a null between them: a frame may go by while a loading card runs); the last item is the taiga.
    /// A build abandoned between slices frees the half-made node.</summary>
    public static IEnumerable<Node3D> BuildSteps(TrackData t)
    {
        var root = new Node3D { Name = "Taiga" };
        bool handedOver = false;
        try
        {
        var slice = System.Diagnostics.Stopwatch.StartNew();
        if (t.Taiga.Count == 0) { handedOver = true; yield return root; yield break; }
        var spruce3 = Spruce(8.5f, 2.0f, 3);
        yield return null;
        var spruce4 = Spruce(7.2f, 2.7f, 4);
        yield return null;
        Mesh[] models = { spruce3, spruce4, Pine() };
        yield return null;
        slice.Restart();
        var mat = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.PerVertex,
            Roughness = 1f,
            SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        var groups = new Dictionary<(int, int, int), List<Transform3D>>();
        foreach (var (pos, scale, yaw, kind) in t.Taiga)
        {
            var key = ((int)(pos.X / Cell), (int)(pos.Z / Cell), kind);
            if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<Transform3D>();
            list.Add(new Transform3D(new Basis(Vector3.Up, yaw).Scaled(Vector3.One * scale), pos));
        }
        foreach (var (key, list) in groups)
        {
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = models[key.Item3], InstanceCount = list.Count };
            for (int i = 0; i < list.Count; i++) mm.SetInstanceTransform(i, list[i]);
            root.AddChild(new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
            if (slice.ElapsedMilliseconds > 10) { yield return null; slice.Restart(); }
        }
        handedOver = true;
        yield return root;
        }
        finally { if (!handedOver) root.Free(); }
    }

    /// <summary>Stacked cones down to the ground, each with a snow cap sharing its tip.</summary>
    static Mesh Spruce(float h, float w, int tiers)
    {
        var st = Begin();
        Cone(st, 0f, 0.9f, 0.16f, 0.12f, 5, Trunk);
        for (int k = 0; k < tiers; k++)
        {
            float f = k / (float)tiers;
            float ch = h * (tiers <= 3 ? 0.46f : 0.38f);
            float y0 = 0.3f + (h - ch - 0.3f) * (k / (float)(tiers - 1));
            float r = w * (1f - 0.62f * f);
            Cone(st, y0, ch, r, 0f, 7, k % 2 == 0 ? Needles : NeedlesLight, 0.2f * k);
            if (k < tiers - 1) SpruceBoughs(st, y0, ch, r, k);
            if (k == tiers - 1)
            {
                // The crown keeps its snow cap.
                float cover = 0.62f + 0.08f * (k % 2);
                Cone(st, y0 + ch * (1f - cover) + 0.12f, ch * cover, r * cover * 1.04f, 0f, 7, Snow, 0.2f * k);
            }
            else SnowPatches(st, y0, ch, r, k);
        }
        return st.Commit();
    }

    /// <summary>Low, down-swept branch whorls break the smooth cone silhouette.</summary>
    static void SpruceBoughs(SurfaceTool st, float y0, float h, float radius, int tier)
    {
        for (int b = 0; b < 3; b++)
        {
            float a = b * (MathF.Tau / 3f) + tier * 0.41f;
            Vector3 dir = new(MathF.Sin(a), 0, MathF.Cos(a));
            Vector3 from = Vector3.Up * (y0 + h * 0.28f) + dir * radius * 0.4f;
            Vector3 to = from + dir * radius * 0.55f - Vector3.Up * (radius * 0.08f);
            Branch(st, from, to, radius * 0.065f, NeedlesLight);
        }
    }

    static void Branch(SurfaceTool st, Vector3 a, Vector3 b, float width, Color color)
    {
        Vector3 axis = (b - a).Normalized();
        Vector3 side = axis.Cross(Vector3.Up).Normalized() * width;
        if (side.LengthSquared() < 0.001f) side = Vector3.Right * width;
        Vector3 depth = axis.Cross(side).Normalized() * width * 0.7f;
        Vector3 endSide = side * 0.22f, endDepth = depth * 0.22f;
        Vector3[] baseV = { a - side - depth, a + side - depth, a + side + depth, a - side + depth };
        Vector3[] tipV = { b - endSide - endDepth, b + endSide - endDepth, b + endSide + endDepth, b - endSide + endDepth };
        st.SetColor(color);
        for (int i = 0; i < 4; i++)
        {
            int j = (i + 1) % 4;
            Tri(st, baseV[i], baseV[j], tipV[j], Vector3.Zero);
            Tri(st, baseV[i], tipV[j], tipV[i], Vector3.Zero);
        }
    }

    static void SnowPatches(SurfaceTool st, float y0, float h, float radius, int tier)
    {
        const int around = 4, rows = 1;
        float phase = tier * 0.73f;
        for (int p = 0; p < 3; p++)
        {
            float center = phase + p * (MathF.Tau / 3f)
                + (Noise.Hash01(tier * 13 + p, 23, 71) - 0.5f) * 0.55f;
            float span = 0.95f + 0.55f * Noise.Hash01(tier * 17 + p, 29, 73);
            var v = new Vector3[rows + 1, around + 1];
            st.SetColor(p == 1 ? SnowShade : Snow);
            for (int x = 0; x <= around; x++)
            {
                float u = x / (float)around;
                float angle = center + (u - 0.5f) * span
                    + (Noise.Hash01(tier * 31 + p * 11 + x, 37, 83) - 0.5f) * 0.14f;
                float low = 0.015f + 0.06f * Noise.Hash01(tier * 19 + p * 7 + x, 31, 79);
                float high = 0.19f + 0.16f * Noise.Hash01(tier * 23 + p * 11 + x, 33, 81);
                for (int row = 0; row <= rows; row++)
                {
                    float t = row / (float)rows;
                    float fraction = Mathf.Lerp(low, high, t);
                    float outer = 1.035f + fraction * 0.58f
                        + (Noise.Hash01(tier * 47 + p * 17 + x, row + 53, 97) - 0.5f) * 0.1f;
                    float r = radius * (1f - fraction) * outer;
                    v[row, x] = new Vector3(MathF.Cos(angle) * r, y0 + h * fraction + 0.025f, MathF.Sin(angle) * r);
                }
            }
            for (int x = 0; x < around; x++)
            for (int row = 0; row < rows; row++)
            {
                Tri(st, v[row, x], v[row, x + 1], v[row + 1, x + 1], Vector3.Zero);
                Tri(st, v[row, x], v[row + 1, x + 1], v[row + 1, x], Vector3.Zero);
            }
        }
    }

    /// <summary>A bare reddish trunk, the crown high up in two lopsided clumps, a little snow in the middle of each.</summary>
    static Mesh Pine()
    {
        var st = Begin();
        const float h = 8f;
        Cone(st, 0f, h * 0.95f, 0.16f, 0.07f, 5, PineBark);
        foreach (var (at, r) in new[] { (new Vector3(0.45f, h * 0.84f, 0.1f), 1.25f), (new Vector3(-0.5f, h * 0.84f + 0.7f, -0.25f), 0.95f) })
        {
            Cone(st, at.Y, 0.6f, 0.2f, r, 6, PineLeaf, 0.3f, at);
            Cone(st, at.Y + 0.6f, 0.55f, r, 0.25f, 6, NeedlesLight, 0.3f, at);
            Cone(st, at.Y + 0.75f, 0.45f, r * 0.55f, 0.08f, 6, Snow, 0.3f, at);
        }
        return st.Commit();
    }

    static SurfaceTool Begin()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        return st;
    }

    /// <summary>A frustum from y0 up h, radius r0 at the bottom to r1 at the top (0: a cone), sides only, faces lit outwards.</summary>
    static void Cone(SurfaceTool st, float y0, float h, float r0, float r1, int seg, Color c, float turn = 0f, Vector3 at = default)
        => ConeSection(st, y0, h, r0, r1, seg, c, turn, MathF.Tau, at);

    /// <summary>A complete frustum or an angular patch on its surface.</summary>
    static void ConeSection(SurfaceTool st, float y0, float h, float r0, float r1, int seg, Color c, float turn, float sweep, Vector3 at = default)
    {
        Vector3 off = new(at.X, 0, at.Z);
        st.SetColor(c);
        for (int k = 0; k < seg; k++)
        {
            float a0 = turn + k * sweep / seg, a1 = turn + (k + 1) * sweep / seg;
            Vector3 b0 = new(MathF.Cos(a0) * r0, y0, MathF.Sin(a0) * r0), b1 = new(MathF.Cos(a1) * r0, y0, MathF.Sin(a1) * r0);
            Vector3 t0 = new(MathF.Cos(a0) * r1, y0 + h, MathF.Sin(a0) * r1), t1 = new(MathF.Cos(a1) * r1, y0 + h, MathF.Sin(a1) * r1);
            Tri(st, b0 + off, b1 + off, t1 + off, off);
            if (r1 > 0.001f) Tri(st, b0 + off, t1 + off, t0 + off, off);
        }
    }

    static void Tri(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 axis)
    {
        Vector3 n = (b - a).Cross(c - a).Normalized();
        Vector3 mid = (a + b + c) / 3f - axis;
        if (n.Dot(new Vector3(mid.X, 0.3f, mid.Z)) < 0) { (b, c) = (c, b); n = -n; }   // outwards (and up)
        st.SetNormal(n); st.AddVertex(a);
        st.SetNormal(n); st.AddVertex(b);
        st.SetNormal(n); st.AddVertex(c);
    }
}
