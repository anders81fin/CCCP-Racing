using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Tests;

/// <summary>Pair-save file operations for the T7 cut points: throws at the n-th call of a Step hook, otherwise the real operations.</summary>
class TapeT7Ops : ITapeFileOps
{
    public string ThrowAt; public int Nth = 1; int _seen;
    public readonly List<string> Warnings = new();
    public void Step(string name) { if (name == ThrowAt && ++_seen == Nth) throw new InvalidOperationException($"injected at {name} #{Nth}"); }
    public void Replace(string tmp, string final) => WorldTapeRealOps.Instance.Replace(tmp, final);
    public void Move(string tmp, string final) => WorldTapeRealOps.Instance.Move(tmp, final);
    public void Warn(string message) => Warnings.Add(message);
}

// T7: the QA review items 1-4 (duplicate part names, scale, the load boundary, the pair result). Run from the T1 install hook (the T5a list is not edited);
// they use the real index, recorder, codec and file code directly.
public partial class PhysicsTest
{
    partial void TapeT7Tests();

    partial void TapeT7Tests()
    {
        if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--tape-mutation-tests") >= 0 || Array.IndexOf(OS.GetCmdlineUserArgs(), "--tape-t2-child") >= 0) return;
        GD.Print("World tape review fixes (T7)");
        TapeRun("tape T7-1", TapeT7Duplicates); TapeRun("tape T7-2", TapeT7Scale); TapeRun("tape T7-3", TapeT7Boundary); TapeRun("tape T7-4", TapeT7PairResult);
    }

    static Node3D T7N(string name, string part, Node3D parent = null)
    {
        var n = new Node3D { Name = name }; if (part != null) Tape.Part(n, part);
        parent?.AddChild(n); return n;
    }
    static IReadOnlyList<(string Key, Node3D Root)> T7Actors(params (string, Node3D)[] a) => a.ToList();
    static float T7Deg(Quaternion a, Quaternion b) => 2f * MathF.Acos(MathF.Min(1f, MathF.Abs(a.Dot(b)))) * 180f / MathF.PI;

    // ---- 1: a duplicate part name leaves the whole actor uncovered ----
    void TapeT7Duplicates()
    {
        var a = T7N("A", "root"); T7N("a1", "x", a); T7N("a2", "x", a); T7N("a3", "y", a);
        var b = T7N("B", "root"); T7N("b1", "wheel", b);
        var c1 = T7N("C", "root"); T7N("p", "a", c1); T7N("q", "b", c1);
        var c2 = T7N("C2", "root"); T7N("q", "b", c2); T7N("p", "a", c2);          // the same two parts, children in the other order
        try
        {
            var idx = new WorldTapeIndex(T7Actors(("A", a), ("B", b), ("C", c1)));
            TapeCheck(!idx.ActorKeys.Contains("A") && idx.ActorKeys.Contains("B") && idx.ActorKeys.Contains("C"), "tape T7-1: an actor with two parts of one name is not indexed, the others are");
            TapeCheck(idx.Count == 5 && !idx.TryFind(new TapePartId("A", "x"), out _) && !idx.TryFind(new TapePartId("A", "x#2"), out _) && !idx.TryFind(new TapePartId("A", "root"), out _) && !idx.TryFind(new TapePartId("A", "y"), out _),
                $"tape T7-1: none of the duplicate actor's parts is indexed and no numbered name is invented (index holds {idx.Count} parts)");
            TapeCheck(idx.TryFind(new TapePartId("B", "wheel"), out int w) && idx.NodeOf(w).Name == "b1", "tape T7-1: the other actor's parts are unaffected");
            TapeCheck(idx.StructureOf("A").Hash == 0 && idx.StructureOf("B").Hash != 0, "tape T7-1: the uncovered actor's structure matches no tape");
            var rec = new WorldTapeRecorder(); rec.Begin(idx); rec.Record(0.5f);
            var tape = rec.Finish(0.5f, 1);
            TapeCheck(!tape.ActorStructure.ContainsKey("A") && tape.ActorStructure.ContainsKey("B") && WorldTapeCodec.ReadBody(tape.Payload).Parts.All(p => p.ActorKey != "A"), "tape T7-1: a recorded tape holds nothing of the duplicate actor");

            var idx2 = new WorldTapeIndex(T7Actors(("C", c2)));
            bool same = idx.TryFind(new TapePartId("C", "a"), out int pa) && idx2.TryFind(new TapePartId("C", "a"), out int pa2) && idx.NodeOf(pa).Name == "p" && idx2.NodeOf(pa2).Name == "p"
                && idx.TryFind(new TapePartId("C", "b"), out int pb) && idx2.TryFind(new TapePartId("C", "b"), out int pb2) && idx.NodeOf(pb).Name == "q" && idx2.NodeOf(pb2).Name == "q";
            TapeCheck(same, "tape T7-1: with unique names a part's id follows its name, not its place among the children");
        }
        finally { foreach (var n in new[] { a, b, c1, c2 }) n.Free(); }
    }

    // ---- 2: scale ----
    static void T7Pose(Node3D n, float t, int k, Vector3 sc) =>
        n.Transform = new Transform3D(new Basis(new Quaternion(new Vector3(.3f + k, 1f, .2f).Normalized(), t * 1.7f + k)) * Basis.FromScale(sc), new Vector3(MathF.Sin(t + k) * 5f, t * 3f, k));

    void TapeT7Scale()
    {
        var root = T7N("S", "root"); var u = T7N("u", "u", root); var nu = T7N("n", "n", root); var tiny = T7N("t", "tiny", root); var pulse = T7N("p", "pulse", root);
        try
        {
            var uni = new Vector3(1.6f, 1.6f, 1.6f); var non = new Vector3(1f, 2f, .5f);
            void Drive(float t, bool withPulse)
            {
                T7Pose(u, t, 0, uni); T7Pose(nu, t, 1, non); T7Pose(tiny, t, 2, Vector3.One * (1f + .005f * MathF.Sin(t * 5)));
                T7Pose(pulse, t, 3, withPulse ? Vector3.One * (1f + .5f * MathF.Sin(t * 3)) : Vector3.One);
            }
            Drive(0, false);
            var idx = new WorldTapeIndex(T7Actors(("S", root)));
            var rec = new WorldTapeRecorder(); var expected = new List<Dictionary<string, (Vector3 P, Quaternion Q)>>();
            Drive(0, false); rec.Begin(idx);
            for (int n = 0; n <= 240; n++)
            {
                float t = n / 60f; Drive(t, false); rec.Record(t);
                if (n % 4 == 0)
                {
                    var d = new Dictionary<string, (Vector3, Quaternion)>();
                    for (int i = 0; i < idx.Count; i++) { var nd = idx.NodeOf(i); d[idx.IdOf(i).Part] = (nd.Position, nd.Basis.Orthonormalized().GetRotationQuaternion()); }
                    expected.Add(d);
                }
            }
            TapeCheck(rec.UnscaledChanges == 0, $"tape T7-2: scaled parts whose scale stays (1.6 uniform, 1:2:0.5, a 0.5 % wobble) trip nothing (got {rec.UnscaledChanges})");
            var tape = rec.Finish(4f, 1); var bytes = WorldTapeCodec.Encode(tape); var dec = WorldTapeCodec.Decode(bytes);
            TapeCheck(dec != null, "tape T7-2: the tape decodes");
            if (dec == null) return;
            var body = WorldTapeCodec.ReadBody(dec.Payload);
            var cur = new Dictionary<int, TapePartState>(); double worstP = 0, worstR = 0; int missing = 0;
            for (int k = 0; k < body.Samples.Count; k++)
            {
                foreach (var (p, st) in body.Samples[k]) cur[p] = st;
                foreach (var (p, st) in cur)
                {
                    if (!expected[k].TryGetValue(body.Parts[p].Part, out var e)) { missing++; continue; }
                    worstP = Math.Max(worstP, st.Pos.DistanceTo(e.P)); worstR = Math.Max(worstR, T7Deg(st.Rot, e.Q));
                }
            }
            TapeCheck(missing == 0 && body.Samples.Count == expected.Count && worstP <= .001 && worstR <= .1, $"tape T7-2: scaled parts decode to the same position and rotation (worst {worstP * 1000:F3} mm, {worstR:F4} degree, {body.Samples.Count} samples)");
            var st0 = dec.ActorStructure["S"];
            TapeCheck(st0.RestScale("u").DistanceTo(uni) < 1e-4f && st0.RestScale("n").DistanceTo(non) < 1e-4f && st0.RestScale("root").DistanceTo(Vector3.One) < 1e-4f, $"tape T7-2: the rest scales survive the codec (u {st0.RestScale("u")}, n {st0.RestScale("n")})");
            idx.TryFind(new TapePartId("S", "n"), out int pn);
            TapeCheck(idx.RestScale(pn).DistanceTo(non) < 1e-4f, "tape T7-2: the index exposes a part's rest scale");

            var rec2 = new WorldTapeRecorder(); Drive(0, true); rec2.Begin(idx);
            for (int n = 0; n <= 240; n++) { float t = n / 60f; Drive(t, true); rec2.Record(t); }
            TapeCheck(rec2.UnscaledChanges > 0, $"tape T7-2: a part whose scale is animated by 50 % is counted (got {rec2.UnscaledChanges})");
        }
        finally { root.Free(); }
    }

    // ---- 3: the load boundary ----
    void TapeT7Boundary()
    {
        var root = T7N("S", "root"); var u = T7N("u", "u", root);
        var dir = TapeTmpDir();
        try
        {
            var idx = new WorldTapeIndex(T7Actors(("S", root)));
            var ghost = TapeGhost(70); ulong fp = WorldTapeFingerprint.Of(ghost);
            var rec = new WorldTapeRecorder(); T7Pose(u, 0, 0, Vector3.One); rec.Begin(idx);
            for (int n = 0; n <= 700; n++) { float t = n / 60f; T7Pose(u, t, 0, Vector3.One); rec.Record(t); }
            var tape = rec.Finish(11.7f, fp); var good = WorldTapeCodec.Encode(tape);
            TapeCheck(WorldTapeCodec.Decode(good) is { } d && d.SampleCount == tape.SampleCount && d.SampleCount > 150, "tape T7-3: a valid tape (several keyframes) still decodes");
            int off = good.Length - tape.Payload.Length;
            byte[] Cut(int n) => good.AsSpan(0, n).ToArray();
            byte[] Flip(int from, int n) { var b = (byte[])good.Clone(); for (int i = from; i < from + n; i++) b[i] ^= 0xFF; return b; }
            WorldTape Re(Action<WorldTape> mod) { var t = WorldTapeCodec.Decode(good); mod(t); return t; }
            var cases = new (string Name, byte[] Bytes)[]
            {
                ("truncated payload", Cut(good.Length - 10)), ("truncated to the header", Cut(off)), ("cut in half", Cut(good.Length / 2)),
                ("flipped body magic", Flip(off, 2)), ("flipped bytes in the first chunk", Flip(off + 20, 24)), ("flipped bytes in the middle", Flip(off + tape.Payload.Length / 2, 24)),
                ("samples count too high", WorldTapeCodec.Encode(Re(t => t.SampleCount += 3))), ("samples count too low", WorldTapeCodec.Encode(Re(t => t.SampleCount -= 80))),
                ("empty payload", WorldTapeCodec.Encode(Re(t => t.Payload = Array.Empty<byte>()))), ("payload of zeros", WorldTapeCodec.Encode(Re(t => t.Payload = new byte[64]))),
            };
            foreach (var (name, bytes) in cases) TapeCheck(WorldTapeCodec.Decode(bytes) == null, $"tape T7-3 ({name}): Decode returns null");
            var path = Path.Combine(dir, "tape_t.bin");
            File.WriteAllBytes(path, good);
            TapeCheck(WorldTapeFile.Load(path, ghost, WorldTapeCodec.Decode) != null, "tape T7-3: the valid file loads");
            var bad = Re(t => t.Payload = t.Payload.Take(t.Payload.Length / 2).ToArray());
            TapeCheck(WorldTapeFile.Load(path, ghost, _ => bad) == null, "tape T7-3: the loader checks the body itself, whatever decoder it is given");
            var wrong = Re(t => t.SampleCount += 1);
            TapeCheck(WorldTapeFile.Load(path, ghost, _ => wrong) == null, "tape T7-3: the loader rejects a header that disagrees with the body");
        }
        finally { root.Free(); TapeRmDir(dir); }
    }

    // ---- 4: the pair result follows the file system ----
    void TapeT7PairResult()
    {
        var cases = new (string Name, string At, int Nth, bool Ghost, bool Tape, bool GhostNew, bool TapeNew)[]
        {
            ("no throw", null, 1, true, true, true, true),
            ("throw at the hook after the ghost replacement", "after-replace", 1, true, false, true, false),
            ("throw at the hook after the tape replacement", "after-replace", 2, true, true, true, true),
            ("throw at ghost-replaced", "ghost-replaced", 1, true, false, true, false),
            ("throw at tape-replaced", "tape-replaced", 1, true, true, true, true),
            ("throw before the first replacement", "before-replace", 1, false, false, false, false),
            ("throw before the second replacement", "before-replace", 2, true, false, true, false),
        };
        foreach (var (name, at, nth, wantG, wantT, gNew, tNew) in cases)
        {
            var f = TapePair(true);
            try
            {
                var ops = new TapeT7Ops { ThrowAt = at, Nth = nth };
                var res = WorldTapeFile.SavePair(f.Gp, f.Tp, f.G1, f.T1, ops, WorldTapeCodec.Encode, WorldTapeCodec.Decode);
                var g = TapeLoadGhost(f.Gp); bool gIsNew = g != null && WorldTapeFingerprint.Of(g) == WorldTapeFingerprint.Of(f.G1);
                bool tIsNew = SameFile(f.Tp, f.NewT), tIsOld = SameFile(f.Tp, f.OldT);
                TapeCheck(res.GhostSaved == wantG && res.TapeSaved == wantT, $"tape T7-4 ({name}): the result says {res}");
                TapeCheck(gIsNew == gNew && (tIsNew == tNew && tIsNew != tIsOld) && res.GhostSaved == gIsNew && res.TapeSaved == tIsNew, $"tape T7-4 ({name}): the result matches the files (ghost new {gIsNew}, tape new {tIsNew})");
                TapeCheck(Tmps(f.Dir).Length == 0, $"tape T7-4 ({name}): no .tmp remains");
            }
            finally { TapeRmDir(f.Dir); }
        }
    }
}
