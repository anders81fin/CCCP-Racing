using System;
using System.Collections.Generic;
using Godot;

namespace CCCPRacing.Track;

/// <summary>
/// Wheel tracks in the packed snow of a winter road: two lanes, each a pair of polished, greyer
/// ruts that wander a little across the road and fade in and out, laid just over the ground along
/// the centre line (so they follow the road whichever way it runs). Not on ice or concrete.
/// </summary>
public static class SnowTracks
{
    const float TrackWidth = 0.5f, WheelHalf = 0.72f, Lift = 0.04f;

    public static MeshInstance3D Build(TrackData t)
    {
        MeshInstance3D result = null;
        foreach (var step in BuildSteps(t)) result = step ?? result;
        return result;
    }

    /// <summary>The same build in slices (a null between them: a frame may go by while a loading card runs); the last item is the mesh.</summary>
    public static IEnumerable<MeshInstance3D> BuildSteps(TrackData t)
    {
        var slice = System.Diagnostics.Stopwatch.StartNew();
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        st.SetNormal(Vector3.Up);          // lit like the flat road under it, whichever way a strip winds
        int n = t.Center.Length;
        // Lane centres as a share of the half width (left of the driving direction is +).
        // Each track twice: a wide faint halo of trodden snow, then the polished core over it.
        foreach (bool core in new[] { false, true })
        foreach (float lane in new[] { 0.42f, -0.42f })
        foreach (float wheel in new[] { -WheelHalf, WheelHalf })
        {
            int seed = (lane > 0 ? 1950 : 1960) + (wheel > 0 ? 1 : 0);
            Vector3[] prev = null;
            float prevShade = 0;
            for (int i = 0; i <= n; i++)
            {
                if (slice.ElapsedMilliseconds > 10) { yield return null; slice.Restart(); }
                int k = i % n;
                if (t.CenterSurf[k] is not SurfaceType.Snow) { prev = null; continue; }
                // The lane wanders a little; each track fades where traffic spread out.
                float wander = 0.4f * Noise.Value(k / 15f, lane * 10f, seed - (wheel > 0 ? 1 : 0));
                float fade = Math.Clamp(0.5f + 0.7f * Noise.Value(k / 9f, wheel * 5f, seed + 7), 0f, 1f);
                float lat = lane * t.HalfWidth[k] + wander + wheel;
                Vector2 dir = t.CenterDir(k), left = new(dir.Y, -dir.X);
                Vector2 c = t.Center[k] + left * lat;
                float w = TrackWidth * (0.8f + 0.4f * Noise.Hash01(k, seed, 3)) * (core ? 1f : 2.2f);
                Vector2 a = c - left * w * 0.5f, b = c + left * w * 0.5f;
                var row = new[] { G(t, a, core), G(t, b, core) };
                float shade = fade * (core ? 1f : 0.35f);
                if (prev != null && (shade > 0.05f || prevShade > 0.05f))
                {
                    Color ca = Rut(prevShade), cb = Rut(shade);
                    st.SetColor(ca); st.AddVertex(prev[0]);
                    st.SetColor(cb); st.AddVertex(row[0]);
                    st.SetColor(cb); st.AddVertex(row[1]);
                    st.SetColor(ca); st.AddVertex(prev[0]);
                    st.SetColor(cb); st.AddVertex(row[1]);
                    st.SetColor(ca); st.AddVertex(prev[1]);
                }
                prev = row;
                prevShade = shade;
            }
        }
        var mat = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.PerVertex,
            Roughness = 1f,
            SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        st.SetMaterial(mat);
        yield return null;      // (the commit below is one native call)
        yield return new MeshInstance3D { Name = "SnowTracks", Mesh = st.Commit(), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
    }

    static Vector3 G(TrackData t, Vector2 p, bool core) => new(p.X, t.MeshHeight(p.X, p.Y) + Lift + (core ? 0.01f : 0f), p.Y);

    /// <summary>The rut's colour: the road's near-white where it fades out, polished blue-grey where traffic ran.</summary>
    static Color Rut(float shade) => new Color(0.9f, 0.91f, 0.94f).Lerp(new Color(0.8f, 0.83f, 0.89f), shade);
}
