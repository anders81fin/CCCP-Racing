using System;
using CCCPRacing.Car;
using Godot;

namespace CCCPRacing.Track;

/// <summary>
/// Builds all fence runs: a rickety wooden post-and-rail fence (leaning posts, sagging and
/// missing rails, breakable, see WoodFences) and a low dry-stone wall of uneven grey stones
/// (in ~2 m segments that a hard hit knocks down, see StoneWalls).
/// </summary>
public static class Fences
{
    public static Node3D Build(TrackData t, TyreEffects fx = null) => Build(t, fx, out _, out _);

    public static Node3D Build(TrackData t, TyreEffects fx, out WoodFences wood, out StoneWalls stone)
    {
        var root = new Node3D { Name = "Fences" };

        var mat = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.PerVertex,
            Roughness = 1f,
            SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        wood = new WoodFences { Name = "WoodFences" };
        wood.Init(t, fx, mat);
        root.AddChild(wood);
        stone = new StoneWalls { Name = "StoneWalls" };
        stone.Init(t, fx, mat);
        root.AddChild(stone);
        return root;
    }

    internal static Color Jit(Color c, RandomNumberGenerator rng, float a)
    {
        float d = rng.RandfRange(-a, a);
        return new Color(c.R + d, c.G + d, c.B + d * 0.8f);
    }

    static readonly Vector3[] FaceNormals = { Vector3.Right, Vector3.Left, Vector3.Up, Vector3.Down, Vector3.Back, Vector3.Forward };

    /// <summary>Flat-shaded box into the SurfaceTool, one colour, counter-clockwise from outside.</summary>
    internal static void AddBox(SurfaceTool st, Transform3D xf, Vector3 size, Color col)
    {
        Vector3 h = size * 0.5f;
        foreach (var n in FaceNormals)
        {
            // Two axes spanning the face.
            Vector3 u = Math.Abs(n.Y) > 0.5f ? Vector3.Right : Vector3.Up;
            Vector3 v = n.Cross(u);
            Vector3 c = n * h;
            Vector3 hu = u * h, hv = v * h;
            Vector3[] q = { c - hu - hv, c + hu - hv, c + hu + hv, c - hu + hv };
            Vector3 wn = (xf.Basis * n).Normalized();
            st.SetColor(col);
            st.SetNormal(wn);
            // Godot's front faces are clockwise: 0-2-1, 0-3-2.
            foreach (int i in new[] { 0, 2, 1, 0, 3, 2 })
            {
                st.SetColor(col);
                st.SetNormal(wn);
                st.AddVertex(xf * q[i]);
            }
        }
    }
}
