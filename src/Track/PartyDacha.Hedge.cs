using System;
using System.Collections.Generic;
using Godot;

namespace CCCPRacing.Track;

public static partial class PartyDacha
{
    /// <summary>
    /// A clipped thuja hedge swept along a polyline as one continuous surface: a flat-topped block with rounded shoulders, 1.9 m high and 1.1 m thick, mitred at the corners, the foliage a fine
    /// vertex-coloured relief (small noise in the surface and in the green, lighter towards the clipped top, darker at the foot) so it reads as dense evergreen, not as blocks.
    /// </summary>
    internal static MeshInstance3D HedgeRun(TrackData t, Vector2[] run)
    {
        const float Half = 0.56f, Height = 1.9f, Step = 0.3f;
        // (across, up) fractions of the profile, left foot to right foot.
        var left = new (float u, float v)[] { (-1f, 0f), (-1f, 0.2f), (-1f, 0.4f), (-1f, 0.6f), (-1f, 0.78f), (-0.96f, 0.89f), (-0.85f, 0.955f), (-0.62f, 0.99f), (-0.32f, 1f), (0f, 1f) };
        var prof = new List<(float u, float v)>(left);
        for (int k = left.Length - 2; k >= 0; k--) prof.Add((-left[k].u, left[k].v));
        // Resample: corners are exact samples, with a mitre scale.
        var pts = new List<(Vector2 p, Vector2 n, float miter)>();
        for (int i = 0; i + 1 < run.Length; i++)
        {
            Vector2 a = run[i], b = run[i + 1], d = (b - a).Normalized(); int cnt = Math.Max(1, (int)MathF.Ceiling(a.DistanceTo(b) / Step));
            for (int k = 0; k < cnt; k++)
            {
                Vector2 dir = d; float miter = 1f;
                if (k == 0 && i > 0) { Vector2 pd = (a - run[i - 1]).Normalized(); dir = (pd + d).Normalized(); miter = 1f / Math.Max(0.5f, dir.Dot(d)); }
                pts.Add((a.Lerp(b, k / (float)cnt), new Vector2(-dir.Y, dir.X), miter));
            }
        }
        { Vector2 d = (run[^1] - run[^2]).Normalized(); pts.Add((run[^1], new Vector2(-d.Y, d.X), 1f)); }
        int rows = pts.Count, cols = prof.Count;
        var pos = new Vector3[rows * cols]; var col = new Color[rows * cols];
        var green = new Color(0.045f, 0.135f, 0.065f);
        // The hedge's foot follows the ground smoothed along its length (a moving average over about 6 m, kept within a hand's breadth of the real ground): over a bank or a dike it rides the line instead of zigzagging.
        var baseY = new float[rows]; var ground0 = new float[rows];
        for (int i = 0; i < rows; i++) ground0[i] = t.MeshHeight(pts[i].p.X, pts[i].p.Y);
        for (int i = 0; i < rows; i++) { float sum = 0; int cnt = 0; for (int k = -10; k <= 10; k++) { int j = Math.Clamp(i + k, 0, rows - 1); sum += ground0[j]; cnt++; } baseY[i] = Math.Clamp(sum / cnt, ground0[i] - 0.4f, ground0[i] + 0.3f) - 0.12f; }
        for (int i = 0; i < rows; i++)
        {
            var (p, n, miter) = pts[i]; float g = baseY[i];
            for (int j = 0; j < cols; j++)
            {
                var (u, v) = prof[j];
                Vector2 q = p + n * (u * Half * miter);
                float y = g + v * Height;
                // Small relief: displace along the outward direction by a smooth world-space noise.
                float relief = (Noise.Value(q.X * 3.1f, q.Y * 2.7f + y * 3.3f, 61) * 0.5f + 0.5f) * 0.075f - 0.02f;
                if (v > 0.12f) { Vector2 o = n * MathF.Sign(u == 0 ? 1 : u); float side = Math.Clamp(MathF.Abs(u), 0f, 1f); q += o * relief * side * 0.9f; y += relief * (1f - side * 0.7f) * (v > 0.9f ? 1f : 0.3f); }
                float tone = 1f + 0.22f * Noise.Value(q.X * 2.4f, q.Y * 2.4f + y * 4.1f, 63) + 0.1f * Noise.Value(q.X * 7f, q.Y * 7f + y * 9f, 64);
                float vertical = 0.68f + 0.38f * v;                          // darker at the foot, lighter at the clipped top
                col[i * cols + j] = new Color(green.R * tone * vertical, green.G * tone * vertical * (0.97f + 0.06f * v), green.B * tone * vertical);
                pos[i * cols + j] = new Vector3(q.X, y, q.Y);
            }
        }
        var st = new SurfaceTool(); st.Begin(Mesh.PrimitiveType.Triangles);
        void V(int idx) { st.SetColor(col[idx]); st.SetUV(new Vector2(pos[idx].X, pos[idx].Z) * 0.3f); st.AddVertex(pos[idx]); }
        for (int i = 0; i + 1 < rows; i++)
            for (int j = 0; j + 1 < cols; j++)
            {
                int a = i * cols + j, b = (i + 1) * cols + j, c = (i + 1) * cols + j + 1, d = i * cols + j + 1;
                V(a); V(b); V(c); V(a); V(c); V(d);
            }
        // End caps: the cross-section ring as a fan.
        foreach (int i in new[] { 0, rows - 1 })
            for (int j = 1; j + 1 < cols; j++) { int a = i * cols, b = i * cols + j, c = i * cols + j + 1; if (i == 0) { V(a); V(c); V(b); } else { V(a); V(b); V(c); } }
        st.GenerateNormals();
        var mat = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 1f, SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
        return new MeshInstance3D { Name = "ThujaHedge", Mesh = st.Commit(), MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.On };
    }

    /// <summary>The dry-stone walls along both edges of the entrance drive, from the road to the gate posts (world points, ground heights applied); a run is cut where the main road or water is.</summary>
    public static IEnumerable<List<Vector3>> DriveWalls(TrackData t)
    {
        if (t.Id != "ilyich") yield break;
        foreach (float side in new[] { 1f, -1f })
        {
            var pts = new List<Vector3>();
            var path = new List<Vector2>();
            Vector2 a0 = Drive[0], b0 = Drive[1], dir0 = (b0 - a0).Normalized(), n0 = new(-dir0.Y, dir0.X);
            for (float d = 0; d < a0.DistanceTo(b0) - 1f; d += 2f) path.Add(a0 + dir0 * d + n0 * side * 4.7f);
            path.Add(new Vector2(Gate.X + 1.4f, Gate.Y - side * 4.2f));
            foreach (var p in path)
            {
                int road = t.NearestCenter(p, out float roadDistance);
                bool blocked = roadDistance < t.HalfWidth[road] + 1.6f || t.Sample(p.X, p.Y) is var g && (g.WaterDepth > 0f || g.Surface is SurfaceType.Water or SurfaceType.Shallow);
                if (!blocked && Math.Abs(p.X - BoothAt.X) < 1.9f && p.Y > Drive[0].Y) blocked = true;       // an opening in the wall in front of the guard booth
                if (blocked) { if (pts.Count >= 2) yield return pts; pts = new List<Vector3>(); continue; }
                pts.Add(new Vector3(p.X, t.MeshHeight(p.X, p.Y), p.Y));
            }
            if (pts.Count >= 2) yield return pts;
        }
    }
}
