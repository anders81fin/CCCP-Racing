using System;
using System.Collections.Generic;
using Godot;

namespace CCCPRacing.Track;

/// <summary>
/// A building that fades see-through while it stands between the camera and the player's car, so
/// the car never vanishes behind a block of flats or a hangar. Added to a building's root (Props.Build):
/// its triangle geometry is cached in its own frame; each frame the line from the camera
/// to the car is tested against it, and every mesh in it fades: while faded its meshes wear
/// see-through copies of their materials (GeometryInstance3D.Transparency does nothing in this
/// renderer), the shared originals untouched and put back once clear. The car comes from the race (Car, as FadeUnder's).
/// </summary>
public partial class SeeThrough : Node
{
    /// <summary>The player's car, set by the race every frame.</summary>
    public static Vector3 Car;
    /// <summary>The drone tour flies over the buildings with the car parked far away: nothing is to fade.</summary>
    public static bool Off;
    /// <summary>The two fading classes' statics (Car and Off of SeeThrough and FadeUnder) as they were (Capture).</summary>
    public readonly record struct FadeStatics(Vector3 SeeCar, bool SeeOff, Vector3 FadeCar, bool FadeOff);

    /// <summary>
    /// For a session owner that lets a second world run (the replay viewer): the nested race sets Car every frame and its exit clears Off
    /// (RaceScene._ExitTree); Restore puts the live values back. Car would correct itself on the live race's first frame anyway.
    /// </summary>
    public static FadeStatics Capture() => new(Car, Off, FadeUnder.Car, FadeUnder.Off);

    public static void Restore(FadeStatics s)
    {
        Car = s.SeeCar; Off = s.SeeOff; FadeUnder.Car = s.FadeCar; FadeUnder.Off = s.FadeOff;
    }

    /// <summary>How see-through a building in the way goes (0 solid .. 1 gone).</summary>
    const float Faded = 0.72f;
    /// <summary>Seconds to fade see-through and back solid, eased at both ends: no hard switch.</summary>
    const float FadeIn = 0.35f, FadeOut = 0.5f;

    Node3D _building;
    readonly BuildingOcclusion _occlusion = new();
    readonly List<MeshInstance3D> _meshes = new();
    readonly List<Material> _originals = new();
    readonly Dictionary<Material, StandardMaterial3D> _clones = new();
    bool _swapped;
    float _t;
    bool _was;

    public override void _Ready()
    {
        _building = GetParent<Node3D>();
        Collect(_building, Transform3D.Identity);
        AddToGroup("sight_occluder");
    }

    public bool BlocksSight(Vector3 from, Vector3 to)
    {
        var inverse = _building.GlobalTransform.AffineInverse();
        return _occlusion.Blocks(inverse * from, inverse * to);
    }

    void Collect(Node n, Transform3D xf)
    {
        foreach (var c in n.GetChildren())
        {
            if (c is not Node3D c3) continue;
            var cx = xf * c3.Transform;
            if (c is MeshInstance3D mi)
            {
                _meshes.Add(mi);
                _originals.Add(mi.MaterialOverride);
                if (mi.Mesh != null) _occlusion.Add(mi.Mesh, cx);
            }
            Collect(c, cx);
        }
    }

    public override void _Process(double delta)
    {
        var cam = GetViewport()?.GetCamera3D();
        if (cam == null || _meshes.Count == 0) return;
        var inv = _building.GlobalTransform.AffineInverse();
        Vector3 a = inv * cam.GlobalPosition, b = inv * (Car + Vector3.Up * 0.8f);
        bool blocks = !Off && _occlusion.Blocks(a, b);
        if (blocks != _was && OS.HasEnvironment("SEEDBG"))
            GD.Print($"see-through: {_building.Name} {(blocks ? "in the way" : "clear")} at {Time.GetTicksMsec() / 1000f:F1} s, car ({Car.X:F0},{Car.Z:F0})");
        _was = blocks;
        float target = blocks && !OS.HasEnvironment("NOSEETHROUGH") ? Faded : 0f;     // (NOSEETHROUGH: tests, the before picture)
        float t = Mathf.MoveToward(_t, target, (float)delta * Faded / (target > _t ? FadeIn : FadeOut));
        if (t == _t) return;
        _t = t;
        Fade(Faded * Mathf.SmoothStep(0f, Faded, t));
    }

    /// <summary>Faded by t (0: the originals back on).</summary>
    void Fade(float t)
    {
        if (t <= 0f)
        {
            if (!_swapped) return;
            for (int k = 0; k < _meshes.Count; k++) if (IsInstanceValid(_meshes[k])) _meshes[k].MaterialOverride = _originals[k];
            _swapped = false;
            return;
        }
        if (!_swapped)
        {
            for (int k = 0; k < _meshes.Count; k++)
            {
                if (_originals[k] is not StandardMaterial3D m || !IsInstanceValid(_meshes[k])) continue;
                if (!_clones.TryGetValue(m, out var c))
                {
                    c = (StandardMaterial3D)m.Duplicate();
                    c.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
                    c.CullMode = BaseMaterial3D.CullModeEnum.Back;
                    // Depth written as when solid: the swap at the start of a fade does not show.
                    c.DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Always;
                    _clones[m] = c;
                }
                // Lit windows keep their night glow as it is now.
                if (m.EmissionEnabled) c.EmissionEnergyMultiplier = m.EmissionEnergyMultiplier;
                c.AlbedoColor = m.AlbedoColor;
                _meshes[k].MaterialOverride = c;
            }
            _swapped = true;
        }
        foreach (var (m, c) in _clones) c.AlbedoColor = new Color(((StandardMaterial3D)m).AlbedoColor, 1f - t);
    }

}

/// <summary>Cached mesh triangles: empty space within a building's bounds never hides the car.</summary>
internal sealed class BuildingOcclusion
{
    readonly List<(Aabb box, Vector3[] faces)> _parts = new();

    public void Add(Mesh mesh, Transform3D transform)
    {
        // (a primitive mesh is read from the shape cache: GetFaces / GetAabb would have the engine upload the mesh and read it back, about a millisecond each on a windowed renderer)
        bool prim = DrunkTractor.TryPrimitiveFaces(mesh, out Vector3[] faces, out Aabb bounds);
        if (!prim) faces = mesh.GetFaces();
        if (faces.Length == 0) return;
        for (int i = 0; i < faces.Length; i++) faces[i] = transform * faces[i];
        _parts.Add((transform * (prim ? bounds : mesh.GetAabb()), faces));
    }

    public bool Blocks(Vector3 from, Vector3 to)
    {
        foreach (var (box, faces) in _parts)
        {
            if (!SegmentBox(from, to, box)) continue;
            for (int i = 0; i + 2 < faces.Length; i += 3)
                if (SegmentTriangle(from, to, faces[i], faces[i + 1], faces[i + 2])) return true;
        }
        return false;
    }

    static bool SegmentTriangle(Vector3 from, Vector3 to, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 direction = to - from, edge1 = b - a, edge2 = c - a;
        Vector3 cross = direction.Cross(edge2);
        float determinant = edge1.Dot(cross);
        if (MathF.Abs(determinant) < 1e-6f) return false;
        float inverse = 1f / determinant;
        Vector3 offset = from - a;
        float u = offset.Dot(cross) * inverse;
        if (u < 0 || u > 1) return false;
        Vector3 q = offset.Cross(edge1);
        float v = direction.Dot(q) * inverse;
        if (v < 0 || u + v > 1) return false;
        float along = edge2.Dot(q) * inverse;
        return along >= 0 && along <= 1;
    }

    static bool SegmentBox(Vector3 a, Vector3 b, Aabb box)
    {
        Vector3 d = b - a, lo = box.Position, hi = box.End;
        float t0 = 0f, t1 = 1f;
        for (int k = 0; k < 3; k++)
        {
            float o = a[k], dk = d[k];
            if (MathF.Abs(dk) < 1e-6f) { if (o < lo[k] || o > hi[k]) return false; continue; }
            float u0 = (lo[k] - o) / dk, u1 = (hi[k] - o) / dk;
            if (u0 > u1) (u0, u1) = (u1, u0);
            t0 = Math.Max(t0, u0);
            t1 = Math.Min(t1, u1);
            if (t0 > t1) return false;
        }
        return true;
    }
}
