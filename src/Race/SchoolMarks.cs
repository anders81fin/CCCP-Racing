using System;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// The driving school's road markings for the slide lessons: a see-through strip of colour painted
/// across the road for each part of the technique (CLUTCH IN, then LET GO), from the road metre
/// where it starts to where it ends, a bright line at each end; a striped strip (bars and gaps: TAP,
/// TAP, TAP) for pumping the brake. The words over them are the HUD's
/// (RaceScene.SchoolHud). Unshaded, so they read the same at night.
/// </summary>
public partial class SchoolMarks : Node3D
{
    const float Lift = 0.07f, Edge = 0.45f;

    TrackData _t;
    MeshInstance3D _mesh;
    readonly StandardMaterial3D _mat = NightGlow.Keep(new StandardMaterial3D
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        VertexColorUseAsAlbedo = true,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    });

    public void Init(TrackData t)
    {
        _t = t;
        _mesh = new MeshInstance3D { Name = "Strips", CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, MaterialOverride = _mat };
        AddChild(_mesh);
    }

    public void Clear() => _mesh.Mesh = null;

    /// <summary>The strips: each from road metre a to b (absolute, a before b along the race) in its colour; striped: bars and gaps.</summary>
    /// <param name="strips">side: 0 the whole road, +1 its right half, -1 its left half (a line to keep to),
    /// +2 a half-road lane running across from the left half to the right, -2 from the right half to the left</param>
    public void Show((int a, int b, Color c, bool striped, int side)[] strips)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        int n = _t.Center.Length;
        foreach (var (a, b, c, striped, side) in strips)
        {
            _side = side;
            int len = ((b - a) % n + n) % n;
            _len = len;
            if (striped)
            {
                Band(st, a, 0f, len, new Color(c, 0.12f));
                for (float u = 0; u < len; u += 2f) Band(st, a, u, Math.Min(u + 1f, len), new Color(c, 0.75f));   // a bar a tap
                continue;
            }
            Band(st, a, 0f, len, new Color(c, 0.38f));
            Band(st, a, 0f, Edge, new Color(c, 0.9f));              // a bright line where it starts...
            Band(st, a, len - Edge, len, new Color(c, 0.9f));       // ...and where it ends
        }
        _side = 0;
        _mesh.Mesh = st.Commit();
    }

    int _side;      // the strip being built covers the whole road (0) or one half (+1 right, -1 left), or runs across (±2)
    int _len;       // its length (the lane across goes from one half to the other over it)

    /// <summary>Quads across the road from u0 to u1 metres past road metre a.</summary>
    void Band(SurfaceTool st, int a, float u0, float u1, Color col)
    {
        for (float u = u0; u < u1 - 1e-3f; u += 1f)
        {
            float v = Math.Min(u + 1f, u1);
            float lo0, hi0, lo1, hi1;
            if (Math.Abs(_side) == 2)
            {
                // Half the road wide, its middle from one half's middle (-0.5 or 0.5) to the other's.
                float m0 = Math.Sign(_side) * (u / Math.Max(1, _len) - 0.5f), m1 = Math.Sign(_side) * (v / Math.Max(1, _len) - 0.5f);
                (lo0, hi0, lo1, hi1) = (m0 - 0.5f, m0 + 0.5f, m1 - 0.5f, m1 + 0.5f);
            }
            else
            {
                lo0 = lo1 = _side > 0 ? 0f : -1f;
                hi0 = hi1 = _side < 0 ? 0f : 1f;
            }
            Vector3 l0 = Across(a, u, lo0), r0 = Across(a, u, hi0), l1 = Across(a, v, lo1), r1 = Across(a, v, hi1);
            foreach (var p in new[] { l0, r0, r1, l0, r1, l1 })
            {
                st.SetColor(col);
                st.AddVertex(p);
            }
        }
    }

    /// <summary>A point u metres past road metre a, side across the road (-1 near the left edge, 0 the centre line, 1 near the right edge), just above the ground.</summary>
    Vector3 Across(int a, float u, float side)
    {
        int n = _t.Center.Length;
        int i = ((a + (int)MathF.Floor(u)) % n + n) % n, j = (i + 1) % n;
        float f = u - MathF.Floor(u);
        Vector2 c = _t.Center[i].Lerp(_t.Center[j], f), d = _t.CenterDir(i), right = new(-d.Y, d.X);
        float hw = Mathf.Lerp(_t.HalfWidth[i], _t.HalfWidth[j], f) * 0.85f;
        Vector2 p = c + right * (side * hw);
        return new Vector3(p.X, _t.MeshHeight(p.X, p.Y) + Lift, p.Y);
    }
}
