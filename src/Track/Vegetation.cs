using System;
using System.Collections.Generic;
using CCCPRacing.Car;
using Godot;

namespace CCCPRacing.Track;

/// <summary>
/// Green filler: hundreds of bushes and grass tufts drawn as MultiMeshes (one draw each), withered
/// twig bushes inside blights. Not obstacles — drive through a bush and it bursts into leaves.
/// </summary>
public partial class Vegetation : Node3D
{
    TrackData _t;
    TyreEffects _fx;
    readonly List<(Vector3 pos, float r, bool dead)> _bushes = new();
    readonly Dictionary<int, float> _cooldown = new();
    readonly RandomNumberGenerator _rng = new() { Seed = 5 };

    static readonly Color[] Greens = { new(0.28f, 0.45f, 0.2f), new(0.34f, 0.52f, 0.22f), new(0.24f, 0.38f, 0.18f), new(0.4f, 0.5f, 0.24f) };
    static readonly Color Twigs = new(0.42f, 0.36f, 0.28f);
    static readonly Color[] Autumns = { new(0.62f, 0.34f, 0.12f), new(0.7f, 0.52f, 0.16f), new(0.45f, 0.42f, 0.18f), new(0.55f, 0.24f, 0.1f) };

    public void Init(TrackData t, TyreEffects fx)
    {
        _t = t;
        _fx = fx;
        var alive = new List<Transform3D>();
        var aliveCol = new List<Color>();
        var dead = new List<Transform3D>();
        foreach (var (pos, size, yaw, isDead) in t.Bushes)
        {
            var xf = new Transform3D(new Basis(Vector3.Up, yaw).Scaled(new Vector3(size, size * _rng.RandfRange(0.7f, 1f), size)), pos);
            if (isDead) dead.Add(xf);
            else
            {
                alive.Add(xf);
                var pal = t.Autumn ? Autumns : Greens;
                aliveCol.Add(pal[_rng.RandiRange(0, pal.Length - 1)] * _rng.RandfRange(0.9f, 1.1f));
            }
            _bushes.Add((pos, 1.1f * size, isDead));
        }
        AddChild(Multi(BushMesh(), alive, aliveCol, "Bushes"));
        var twigCols = new List<Color>();
        foreach (var _ in dead) twigCols.Add(Twigs * _rng.RandfRange(0.85f, 1.1f));
        AddChild(Multi(TwigMesh(), dead, twigCols, "DeadBushes"));

        var tufts = new List<Transform3D>();
        var tuftCols = new List<Color>();
        foreach (var (pos, size, yaw) in t.Tufts)
        {
            tufts.Add(new Transform3D(new Basis(Vector3.Up, yaw).Scaled(Vector3.One * size), pos));
            bool withered = t.InBlight(new Vector2(pos.X, pos.Z));
            tuftCols.Add(withered ? new Color(0.55f, 0.5f, 0.38f)
                : (t.Autumn ? new Color(0.66f, 0.58f, 0.3f) : new Color(0.42f, 0.58f, 0.24f)) * _rng.RandfRange(0.85f, 1.15f));   // autumn: straw
        }
        AddChild(Multi(TuftMesh(), tufts, tuftCols, "Tufts"));
    }

    static MultiMeshInstance3D Multi(Mesh mesh, List<Transform3D> xfs, List<Color> cols, string name)
    {
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = mesh, InstanceCount = xfs.Count };
        for (int i = 0; i < xfs.Count; i++)
        {
            mm.SetInstanceTransform(i, xfs[i]);
            mm.SetInstanceColor(i, cols[i]);
        }
        return new MultiMeshInstance3D
        {
            Name = name,
            Multimesh = mm,
            MaterialOverride = new StandardMaterial3D
            {
                VertexColorUseAsAlbedo = true,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.PerVertex,
                Roughness = 1f,
                SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    /// <summary>Copies a primitive mesh into the SurfaceTool with white vertex colour, so the
    /// MultiMesh instance colour shows through (a mesh without colours renders black).</summary>
    static void AppendWhite(SurfaceTool st, PrimitiveMesh prim, Transform3D xf)
    {
        var arrays = DrunkTractor.PrimitiveArrays(prim);
        var verts = (Vector3[])arrays[(int)Mesh.ArrayType.Vertex];
        var norms = (Vector3[])arrays[(int)Mesh.ArrayType.Normal];
        var idx = (int[])arrays[(int)Mesh.ArrayType.Index];
        foreach (int i in idx)
        {
            st.SetColor(Colors.White);
            st.SetNormal((xf.Basis * norms[i]).Normalized());
            st.AddVertex(xf * verts[i]);
        }
    }

    /// <summary>Three squashed low-poly blobs.</summary>
    static Mesh BushMesh()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var ball = new SphereMesh { Radius = 0.7f, Height = 1.1f, RadialSegments = 6, Rings = 3 };
        foreach (var (x, y, z, s) in new[] { (0f, 0.5f, 0f, 1f), (0.55f, 0.35f, 0.2f, 0.75f), (-0.45f, 0.38f, -0.25f, 0.8f) })
            AppendWhite(st, ball, new Transform3D(Basis.Identity.Scaled(Vector3.One * s), new Vector3(x, y, z)));
        return st.Commit();
    }

    /// <summary>A tangle of bare twigs.</summary>
    static Mesh TwigMesh()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var twig = new BoxMesh { Size = new Vector3(0.05f, 1.1f, 0.05f) };
        for (int k = 0; k < 9; k++)
        {
            float a = k * 40f * MathF.PI / 180f, tilt = 0.5f + (k % 3) * 0.25f;
            var b = new Basis(Vector3.Up, a) * new Basis(Vector3.Right, tilt);
            AppendWhite(st, twig, new Transform3D(b, b * new Vector3(0, 0.5f, 0)));
        }
        return st.Commit();
    }

    /// <summary>Crossed blades of long grass.</summary>
    static Mesh TuftMesh()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        for (int k = 0; k < 5; k++)
        {
            float a = k * MathF.PI / 5f;
            var d = new Vector3(MathF.Cos(a), 0, MathF.Sin(a)) * 0.18f;
            var lean = new Vector3(MathF.Sin(a * 3f) * 0.15f, 0, MathF.Cos(a * 2f) * 0.15f);
            foreach (var v in new[] { -d, d, new Vector3(0, 0.55f + 0.2f * (k % 2), 0) + lean })
            {
                st.SetColor(Colors.White);
                st.SetNormal(Vector3.Up);
                st.AddVertex(v);
            }
        }
        return st.Commit();
    }

    /// <summary>Leaves (or dry twigs) burst out of bushes the car ploughs through.</summary>
    public void Tick(float dt, CarSim car)
    {
        if (car == null || car.Speed < 3f) return;
        var p = car.Pos;
        for (int i = 0; i < _bushes.Count; i++)
        {
            var (pos, r, dead) = _bushes[i];
            float dx = pos.X - p.X, dz = pos.Z - p.Z;
            if (dx * dx + dz * dz > (r + 1f) * (r + 1f)) continue;
            if (_cooldown.TryGetValue(i, out float until) && until > Time.GetTicksMsec() / 1000f) continue;
            _cooldown[i] = Time.GetTicksMsec() / 1000f + 1.5f;
            for (int k = 0; k < 18; k++)
                _fx.Burst(pos + Vector3.Up * 0.6f, car.Vel * 0.4f + new Vector3(_rng.RandfRange(-3, 3), _rng.RandfRange(1.5f, 4.5f), _rng.RandfRange(-3, 3)),
                    dead ? Twigs : Greens[_rng.RandiRange(0, Greens.Length - 1)], 1f, 0.3f, 0.2f, _rng.RandfRange(0.8f, 1.5f), 5f, 1.5f, true);
        }
    }
}
