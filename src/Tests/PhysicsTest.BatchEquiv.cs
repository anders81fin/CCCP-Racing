using System;
using System.Collections.Generic;
using System.Linq;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

// --batch-equiv-test: the managed DrunkTractor.BatchStaticSteps merge must give the same meshes as the SurfaceTool reference (DrunkTractor.BatchReference).
// Builds each track's props twice (reference, managed) and compares every batched surface array exactly (vertices, normals, colours, indices) and the build times.
// Options: --batch-equiv-tracks=ilyich,siberia,atomgrad,bonus
public partial class PhysicsTest
{
    static void BatchCollect(Node n, List<MeshInstance3D> into)
    {
        if (n is MeshInstance3D mi && mi.Name == "BatchedMTAgro") into.Add(mi);
        foreach (var c in n.GetChildren()) BatchCollect(c, into);
    }

    /// <summary>Same triangles up to order and the engine's own rounding of face positions (it reads them back from compressed vertex data, about 1e-4 off): each triangle of one has a counterpart of the other within 1e-3 on every corner.</summary>
    static bool SameTriangles(Vector3[] a, Vector3[] b)
    {
        if (a.Length != b.Length) return false;
        var used = new bool[b.Length / 3];
        for (int i = 0; i + 2 < a.Length; i += 3)
        {
            bool found = false;
            for (int j = 0; j < used.Length && !found; j++)
            {
                if (used[j]) continue;
                bool all = true;
                for (int c = 0; c < 3 && all; c++)
                {
                    bool match = false;
                    for (int d = 0; d < 3 && !match; d++) match = a[i + c].DistanceTo(b[j * 3 + d]) < 1e-3f;
                    all = match;
                }
                if (all) { used[j] = true; found = true; }
            }
            if (!found) return false;
        }
        return true;
    }

    void BatchEquivTest()
    {
        GD.Print("Batch merge equivalence (managed arrays vs SurfaceTool reference)");
        // The shape cache against the engine itself: arrays, faces and bounds of boxes, cylinders and spheres of many parameters (the first use fills the cache, the second reads it).
        var rng = new RandomNumberGenerator { Seed = 7 };
        int shapes = 0, shapeBad = 0;
        for (int k = 0; k < 120; k++)
        {
            PrimitiveMesh m = (k % 3) switch
            {
                0 => Props.Box(rng.Randf() * 4 + 0.02f, rng.Randf() * 4 + 0.02f, rng.Randf() * 4 + 0.02f),
                1 => Props.Cyl(rng.Randf() * 2 + 0.05f, rng.Randf() * 3 + 0.05f, 3 + (int)(rng.Randi() % 14), k % 2 == 0 ? -1 : rng.Randf()),
                _ => Props.Ball(rng.Randf() * 2 + 0.05f, 4 + (int)(rng.Randi() % 10)),
            };
            for (int pass = 0; pass < 2; pass++)
            {
                var got = DrunkTractor.PrimitiveArrays(m); var want = m.GetMeshArrays();
                bool ok = got[(int)Mesh.ArrayType.Vertex].AsVector3Array().SequenceEqual(want[(int)Mesh.ArrayType.Vertex].AsVector3Array())
                    && got[(int)Mesh.ArrayType.Normal].AsVector3Array().SequenceEqual(want[(int)Mesh.ArrayType.Normal].AsVector3Array())
                    && got[(int)Mesh.ArrayType.Index].AsInt32Array().SequenceEqual(want[(int)Mesh.ArrayType.Index].AsInt32Array());
                var wantFaces = m.GetFaces();
                bool prim = DrunkTractor.TryPrimitiveFaces(m, out var faces, out var bounds);
                bool facesOk = prim && SameTriangles(faces, wantFaces), boundsOk = bounds.Position.DistanceTo(m.GetAabb().Position) < 1e-3f && bounds.Size.DistanceTo(m.GetAabb().Size) < 1e-3f;
                ok &= facesOk && boundsOk;
                shapes++; if (!ok) shapeBad++;
            }
        }
        Check(shapeBad == 0, $"primitive shape cache: {shapes} reads of boxes, cylinders and spheres equal the engine's arrays, faces and bounds ({shapeBad} differing)");
        string opt = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--batch-equiv-tracks="))?["--batch-equiv-tracks=".Length..];
        foreach (var id in (opt ?? "ilyich,siberia,atomgrad,bonus").Split(','))
        {
            var def = TrackDef.Load($"res://tracks/{id}.json");
            TrackData t = null;
            foreach (var step in TrackBuilder.BuildSteps(def)) if (step != null) t = step;
            DrunkTractor.BatchReference = true;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var refProps = Props.Build(t); double refMs = sw.Elapsed.TotalMilliseconds;
            DrunkTractor.BatchReference = false;
            sw.Restart();
            var newProps = Props.Build(t); double newMs = sw.Elapsed.TotalMilliseconds;
            var a = new List<MeshInstance3D>(); var b = new List<MeshInstance3D>();
            BatchCollect(refProps, a); BatchCollect(newProps, b);
            bool same = a.Count == b.Count && a.Count > 0; int verts = 0, bad = 0;
            if (same)
                for (int i = 0; i < a.Count; i++)
                {
                    var ma = a[i].Mesh; var mb = b[i].Mesh;
                    if (ma.GetSurfaceCount() != mb.GetSurfaceCount()) { bad++; continue; }
                    for (int s = 0; s < ma.GetSurfaceCount(); s++)
                    {
                        var aa = ma.SurfaceGetArrays(s); var bb = mb.SurfaceGetArrays(s);
                        bool eq = aa[(int)Mesh.ArrayType.Vertex].AsVector3Array().SequenceEqual(bb[(int)Mesh.ArrayType.Vertex].AsVector3Array())
                            && aa[(int)Mesh.ArrayType.Normal].AsVector3Array().SequenceEqual(bb[(int)Mesh.ArrayType.Normal].AsVector3Array())
                            && aa[(int)Mesh.ArrayType.Color].AsColorArray().SequenceEqual(bb[(int)Mesh.ArrayType.Color].AsColorArray())
                            && aa[(int)Mesh.ArrayType.Index].AsInt32Array().SequenceEqual(bb[(int)Mesh.ArrayType.Index].AsInt32Array());
                        if (!eq)
                        {
                            bad++;
                            var va = aa[(int)Mesh.ArrayType.Vertex].AsVector3Array(); var vb = bb[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                            var ia = aa[(int)Mesh.ArrayType.Index].AsInt32Array(); var ib = bb[(int)Mesh.ArrayType.Index].AsInt32Array();
                            GD.Print($"    differs: mesh #{i} under {a[i].GetParent()?.Name}/{a[i].GetParent()?.GetParent()?.Name}: vertices {va.Length}/{vb.Length}, indices {ia.Length}/{ib.Length}, first vertex diff at {Enumerable.Range(0, Math.Min(va.Length, vb.Length)).FirstOrDefault(k => va[k] != vb[k], -1)}, first index diff at {Enumerable.Range(0, Math.Min(ia.Length, ib.Length)).FirstOrDefault(k => ia[k] != ib[k], -1)}");
                        }
                        verts += aa[(int)Mesh.ArrayType.Vertex].AsVector3Array().Length;
                    }
                }
            Check(same && bad == 0, $"batch equivalence {id}: {a.Count} batched meshes, {verts} vertices, {bad} differing; Props.Build reference {refMs:0} ms, managed {newMs:0} ms");
            refProps.Free(); newProps.Free();
        }
    }
}
