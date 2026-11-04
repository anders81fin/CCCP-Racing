using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using CCCPRacing.Car;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Tests;

// Reference model of the world tape: an executable specification, deliberately simple. The tests never touch it directly (only via TapeSeams).

readonly record struct RefPose(bool Visible, int X, int Y, int Z, short Qx, short Qy, short Qz, short Qw, bool Spin, float Angle)
{
    public Vector3 Pos(float scale) => new(X / scale, Y / scale, Z / scale);
    public Quaternion Rot => new Quaternion(Qx / 32767f, Qy / 32767f, Qz / 32767f, Qw / 32767f).Normalized();
}

class RefKey
{
    public int Sample;
    public List<(int P, RefPose Pose)> Poses = new();
    public Dictionary<TapeSoundKey, byte> Sounds = new();
    public List<TapeEvent> Inst = new(), Eff = new();
    public List<int> Broken = new();
}

class RefData
{
    public float PosScale = 1024f;
    public List<TapePartId> Parts = new();
    public List<List<(int P, RefPose Pose)>> Samples = new();
    public List<RefKey> Keys = new();
    public List<List<(TapeSoundKey K, byte V)>> Sounds = new();
    public List<TapeEvent> Events = new();
}

static class RefReg
{
    public record SoundReg(TapeSoundChannel Ch, Func<float> Get, Action<float> Set);
    public static readonly ConditionalWeakTable<Node3D, ITapeSpinner> Spin = new();
    public static readonly ConditionalWeakTable<Node3D, List<SoundReg>> Sound = new();
}

static class RefCodec
{
    static void S(BinaryWriter w, string s) { w.Write(s != null); if (s != null) w.Write(s); }
    static string S(BinaryReader r) => r.ReadBoolean() ? r.ReadString() : null;
    static void V(BinaryWriter w, Vector3 v) { w.Write(v.X); w.Write(v.Y); w.Write(v.Z); }
    static Vector3 V(BinaryReader r) => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
    static void Pose(BinaryWriter w, RefPose p) { w.Write(p.Visible); w.Write(p.X); w.Write(p.Y); w.Write(p.Z); w.Write(p.Qx); w.Write(p.Qy); w.Write(p.Qz); w.Write(p.Qw); w.Write(p.Spin); w.Write(p.Angle); }
    static RefPose Pose(BinaryReader r) => new(r.ReadBoolean(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt16(), r.ReadInt16(), r.ReadInt16(), r.ReadInt16(), r.ReadBoolean(), r.ReadSingle());
    static void Key(BinaryWriter w, TapeSoundKey k) { w.Write(k.Part.ActorKey); w.Write(k.Part.Part); w.Write((byte)k.Channel); }
    static TapeSoundKey Key(BinaryReader r) => new(new TapePartId(r.ReadString(), r.ReadString()), (TapeSoundChannel)r.ReadByte());
    static void Ev(BinaryWriter w, TapeEvent e)
    {
        w.Write(e.Time); w.Write((byte)e.Type); S(w, e.Source); S(w, e.Kind); S(w, e.Parent); w.Write(e.Instance); w.Write((byte)e.Effect); w.Write((byte)e.Projectile);
        V(w, e.Pos); V(w, e.Dir); V(w, e.Velocity); w.Write(e.Gravity); w.Write(e.Param); w.Write(e.Life);
    }
    static TapeEvent Ev(BinaryReader r) => new(r.ReadSingle(), (TapeEventType)r.ReadByte(), S(r), S(r), S(r), r.ReadInt32(), (TapeEffect)r.ReadByte(), (TapeProjectile)r.ReadByte(), V(r), V(r), V(r), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
    static void Deltas(BinaryWriter w, List<(int P, RefPose Pose)> l) { w.Write(l.Count); foreach (var (p, q) in l) { w.Write(p); Pose(w, q); } }
    static List<(int, RefPose)> Deltas(BinaryReader r) { int n = r.ReadInt32(); var l = new List<(int, RefPose)>(n); for (int i = 0; i < n; i++) l.Add((r.ReadInt32(), Pose(r))); return l; }

    public static byte[] Write(RefData d)
    {
        var ms = new MemoryStream();
        using (var z = new BrotliStream(ms, CompressionLevel.Fastest, true))
        using (var w = new BinaryWriter(z))
        {
            w.Write(d.PosScale);
            w.Write(d.Parts.Count); foreach (var p in d.Parts) { w.Write(p.ActorKey); w.Write(p.Part); }
            w.Write(d.Samples.Count); foreach (var s in d.Samples) Deltas(w, s);
            w.Write(d.Keys.Count);
            foreach (var k in d.Keys)
            {
                w.Write(k.Sample); Deltas(w, k.Poses);
                w.Write(k.Sounds.Count); foreach (var (key, b) in k.Sounds) { Key(w, key); w.Write(b); }
                w.Write(k.Inst.Count); foreach (var e in k.Inst) Ev(w, e);
                w.Write(k.Eff.Count); foreach (var e in k.Eff) Ev(w, e);
                w.Write(k.Broken.Count); foreach (var b in k.Broken) w.Write(b);
            }
            w.Write(d.Sounds.Count); foreach (var s in d.Sounds) { w.Write(s.Count); foreach (var (key, b) in s) { Key(w, key); w.Write(b); } }
            w.Write(d.Events.Count); foreach (var e in d.Events) Ev(w, e);
        }
        return ms.ToArray();
    }

    public static RefData Read(byte[] payload)
    {
        using var z = new BrotliStream(new MemoryStream(payload), CompressionMode.Decompress);
        using var r = new BinaryReader(z);
        var d = new RefData { PosScale = r.ReadSingle() };
        for (int n = r.ReadInt32(), i = 0; i < n; i++) d.Parts.Add(new TapePartId(r.ReadString(), r.ReadString()));
        for (int n = r.ReadInt32(), i = 0; i < n; i++) d.Samples.Add(Deltas(r));
        for (int n = r.ReadInt32(), i = 0; i < n; i++)
        {
            var k = new RefKey { Sample = r.ReadInt32(), Poses = Deltas(r) };
            for (int m = r.ReadInt32(), j = 0; j < m; j++) k.Sounds[Key(r)] = r.ReadByte();
            for (int m = r.ReadInt32(), j = 0; j < m; j++) k.Inst.Add(Ev(r));
            for (int m = r.ReadInt32(), j = 0; j < m; j++) k.Eff.Add(Ev(r));
            for (int m = r.ReadInt32(), j = 0; j < m; j++) k.Broken.Add(r.ReadInt32());
            d.Keys.Add(k);
        }
        for (int n = r.ReadInt32(), i = 0; i < n; i++) { var l = new List<(TapeSoundKey, byte)>(); for (int m = r.ReadInt32(), j = 0; j < m; j++) l.Add((Key(r), r.ReadByte())); d.Sounds.Add(l); }
        for (int n = r.ReadInt32(), i = 0; i < n; i++) d.Events.Add(Ev(r));
        return d;
    }
}

/// <summary>The tape file (header + payload) and the pair file operations. Knows paths only as strings.</summary>
static class RefFiles
{
    static void T(BinaryWriter w, Transform3D t) { var b = t.Basis; foreach (var v in new[] { b.Column0, b.Column1, b.Column2, t.Origin }) { w.Write(v.X); w.Write(v.Y); w.Write(v.Z); } }
    static Transform3D T(BinaryReader r) { Vector3 V() => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); var a = V(); var b = V(); var c = V(); return new Transform3D(new Basis(a, b, c), V()); }

    public static byte[] Encode(WorldTape t)
    {
        var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, true))
        {
            w.Write(WorldTapeConst.Magic); w.Write(WorldTapeConst.FormatVersion); w.Write(t.TrackId ?? ""); w.Write(t.Set); w.Write(t.LapTime); w.Write(t.SampleCount); w.Write(t.GhostFingerprint);
            w.Write(t.ActorStructure.Count);
            foreach (var (key, st) in t.ActorStructure)
            {
                w.Write(key); w.Write(st.Version); w.Write(st.Hash);
                w.Write(st.ParentPaths.Count); foreach (var (k, v) in st.ParentPaths) { w.Write(k); w.Write(v); }
                w.Write(st.RestPoses.Count); foreach (var (k, v) in st.RestPoses) { w.Write(k); T(w, v); }
                w.Write(st.RestInRoot.Count); foreach (var (k, v) in st.RestInRoot) { w.Write(k); T(w, v); }
            }
            w.Write(t.Payload.Length); w.Write(t.Payload);
        }
        return ms.ToArray();
    }

    public static WorldTape Decode(byte[] bytes)
    {
        try
        {
            using var r = new BinaryReader(new MemoryStream(bytes));
            if (r.ReadUInt32() != WorldTapeConst.Magic || r.ReadInt32() != WorldTapeConst.FormatVersion) return null;
            var t = new WorldTape { TrackId = r.ReadString(), Set = r.ReadInt32(), LapTime = r.ReadSingle(), SampleCount = r.ReadInt32(), GhostFingerprint = r.ReadUInt64() };
            for (int n = r.ReadInt32(), i = 0; i < n; i++)
            {
                var key = r.ReadString(); var st = new TapeActorStructure { Version = r.ReadInt32(), Hash = r.ReadUInt32() };
                for (int m = r.ReadInt32(), j = 0; j < m; j++) st.ParentPaths[r.ReadString()] = r.ReadString();
                for (int m = r.ReadInt32(), j = 0; j < m; j++) st.RestPoses[r.ReadString()] = T(r);
                for (int m = r.ReadInt32(), j = 0; j < m; j++) st.RestInRoot[r.ReadString()] = T(r);
                t.ActorStructure[key] = st;
            }
            int len = r.ReadInt32();
            if (len < 0 || len > bytes.Length) return null;
            t.Payload = r.ReadBytes(len);
            return t.Payload.Length == len ? t : null;
        }
        catch { return null; }
    }

    public static WorldTape Load(string path, Ghost ghost)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var t = Decode(File.ReadAllBytes(path));
            return t != null && t.GhostFingerprint == TapeSeams.Fingerprint(ghost) ? t : null;
        }
        catch { return null; }
    }

    public static int CleanStale(string dir)
    {
        int n = 0;
        foreach (var f in Directory.GetFiles(dir))
        {
            var name = Path.GetFileName(f);
            if ((name.StartsWith("ghost_") || name.StartsWith("tape_")) && name.EndsWith(".bin.tmp")) { File.Delete(f); n++; }
        }
        return n;
    }
}

class RefFileOps : ITapeFileOps
{
    public void Step(string name) { }
    public void Replace(string tmp, string final) => File.Replace(tmp, final, null);
    public void Move(string tmp, string final) => File.Move(tmp, final);
    public void Warn(string message) => GD.PushWarning(message);
}

/// <summary>Atomic pair save: tmp files, verify, replace the ghost then the tape; any failure leaves the old files and removes the tmp files.</summary>
class RefSaver
{
    static readonly CarParams Params = new();
    public TapePairResult Save(string gp, string tp, Ghost ghost, WorldTape tape, ITapeFileOps ops)
    {
        string tg = gp + ".tmp", tt = tp + ".tmp";
        bool committing = false;
        try
        {
            ghost.Save(tg);
            File.WriteAllBytes(tt, RefFiles.Encode(tape));
            ops.Step("tmp-written");
            Verify(tg, tt);
            ops.Step("verified");
            committing = true;
            Commit(ops, tg, gp, tt, tp);
            return new TapePairResult(true, true);
        }
        catch (Exception ex)
        {
            ops.Warn($"world tape: save refused ({ex.Message})");
            bool ghostDone = committing && !File.Exists(tg);     // the ghost tmp was consumed by its replacement
            foreach (var f in new[] { tg, tt }) try { if (File.Exists(f)) File.Delete(f); } catch { }
            return new TapePairResult(ghostDone, false);
        }
    }

    static void Verify(string tg, string tt)
    {
        var g = Ghost.Load(tg, Params, null) ?? throw new IOException("ghost tmp unreadable");
        var t = RefFiles.Decode(File.ReadAllBytes(tt)) ?? throw new IOException("tape tmp unreadable");
        if (t.GhostFingerprint != TapeSeams.Fingerprint(g)) throw new IOException("pair fingerprint mismatch");
    }

    protected virtual void Commit(ITapeFileOps ops, string tg, string gp, string tt, string tp)
    {
        Put(ops, tg, gp); ops.Step("ghost-replaced");
        Put(ops, tt, tp); ops.Step("tape-replaced");
    }

    protected static void Put(ITapeFileOps ops, string tmp, string final)
    {
        ops.Step("before-replace");
        if (File.Exists(final)) ops.Replace(tmp, final); else ops.Move(tmp, final);
        ops.Step("after-replace");
    }
}

class RefIndex : IWorldTapeIndex
{
    readonly List<(TapePartId Id, Node3D Node, int Inst)> _p = new();     // Inst: 0 static, else dynamic instance id
    readonly Dictionary<TapePartId, int> _map = new();
    readonly List<string> _actors = new();
    readonly Dictionary<string, Node3D> _roots = new();

    public RefIndex(IReadOnlyList<(string Key, Node3D Root)> actors)
    {
        foreach (var (key, root) in actors) { _actors.Add(key); _roots[key] = root; Walk(root, n => _p.Add((new TapePartId(key, (string)n.GetMeta(WorldTapeConst.PartMeta)), n, 0))); }
        Rebuild();
        foreach (var key in _actors) _struct[key] = Compute(key);
    }

    readonly Dictionary<string, TapeActorStructure> _struct = new();

    static void Walk(Node n, Action<Node3D> f, bool top = true)
    {
        if (!top && n.HasMeta(WorldTapeConst.KindMeta)) return;      // a dynamic instance is a subtree of its own: only AddInstance walks into it
        if (n is Node3D n3 && n.HasMeta(WorldTapeConst.PartMeta)) f(n3);
        foreach (var c in n.GetChildren()) Walk(c, f, false);
    }

    void Rebuild() { _map.Clear(); for (int i = 0; i < _p.Count; i++) _map[_p[i].Id] = i; }
    public int Count => _p.Count;
    public TapePartId IdOf(int part) => _p[part].Id;
    public Node3D NodeOf(int part) => _p[part].Node;
    public bool TryFind(TapePartId id, out int part) => _map.TryGetValue(id, out part);
    public IEnumerable<string> ActorKeys => _actors;

    public int AddInstance(string actorKey, int instance, Node3D root)
    {
        int first = _p.Count;
        Walk(root, n => _p.Add((new TapePartId(actorKey, $"{instance}/{n.GetMeta(WorldTapeConst.PartMeta)}"), n, instance)));
        Rebuild();
        return first;
    }

    public void RemoveInstance(int instance) { _p.RemoveAll(x => x.Inst == instance); Rebuild(); }

    static int Q(float v, float s) => (int)MathF.Round(v * s);
    static string PathOf(Node3D root, Node3D n)
    {
        var parts = new List<string>();
        for (Node c = n; c != root; c = c.GetParent()) parts.Add($"{c.GetParent().GetChildren().IndexOf(c)}:{c.GetClass()}");
        parts.Reverse();
        return string.Join("/", parts);
    }
    static Transform3D InRoot(Node3D root, Node3D n) { var t = Transform3D.Identity; for (Node3D c = n; c != root; c = (Node3D)c.GetParent()) t = c.Transform * t; return t; }
    static TapeHash Rest(TapeHash h, Transform3D t)
    {
        var e = t.Basis.Orthonormalized().GetEuler(); var s = t.Basis.Scale;
        h = h.Add(Q(t.Origin.X, 1000)).Add(Q(t.Origin.Y, 1000)).Add(Q(t.Origin.Z, 1000));
        h = h.Add(Q(e.X, 572.958f)).Add(Q(e.Y, 572.958f)).Add(Q(e.Z, 572.958f));
        return h.Add(Q(s.X, 1000)).Add(Q(s.Y, 1000)).Add(Q(s.Z, 1000));
    }

    public TapeActorStructure StructureOf(string actorKey) => _struct[actorKey];

    TapeActorStructure Compute(string actorKey)
    {
        var root = _roots[actorKey];
        var st = new TapeActorStructure { Version = root.HasMeta(WorldTapeConst.VersionMeta) ? (int)root.GetMeta(WorldTapeConst.VersionMeta) : 0 };
        var h = TapeHash.Start().Add(st.Version);
        foreach (var (id, node, _) in _p.Where(x => x.Id.ActorKey == actorKey && x.Inst == 0).OrderBy(x => x.Id.Part, StringComparer.Ordinal))
        {
            var path = PathOf(root, node); var rel = InRoot(root, node);
            st.ParentPaths[id.Part] = path; st.RestPoses[id.Part] = node.Transform; st.RestInRoot[id.Part] = rel;
            foreach (var ch in id.Part + "|" + node.GetClass() + "|" + path) h = h.Add((int)ch);
            h = Rest(Rest(h, node.Transform), rel);
        }
        st.Hash = (uint)(h.Value ^ (h.Value >> 32));
        return st;
    }
}

class RefRecorder : IWorldTapeRecorder
{
    protected IWorldTapeIndex Idx;
    protected RefData D = new();
    readonly Dictionary<TapePartId, int> _tp = new();
    readonly List<RefPose?> _prev = new();
    readonly List<float> _ang = new();
    readonly Dictionary<TapeSoundKey, byte> _snd = new();
    int _nextSample, _nextSound;
    public int Unregistered;
    public int UnregisteredSpinners => Unregistered;
    public int UnscaledChanges => 0;

    protected virtual float PosScale => 1024f;
    protected virtual float AngleOf(Node3D n, ITapeSpinner s, float prev) => s.Angle;

    int PartIdx(TapePartId id)
    {
        if (_tp.TryGetValue(id, out int p)) return p;
        p = D.Parts.Count; _tp[id] = p; D.Parts.Add(id); _prev.Add(null); _ang.Add(0); return p;
    }

    RefPose Read(int i, int tp)
    {
        var n = Idx.NodeOf(i); var t = n.Transform; var q = t.Basis.GetRotationQuaternion();
        bool spin = RefReg.Spin.TryGetValue(n, out var s); float ang = 0;
        if (spin) { ang = AngleOf(n, s, _ang[tp]); _ang[tp] = ang; q = (q * new Quaternion(s.Axis, -ang)).Normalized(); }
        if (q.W < 0) q = -q;
        float sc = D.PosScale;
        return new RefPose(n.Visible, (int)MathF.Round(t.Origin.X * sc), (int)MathF.Round(t.Origin.Y * sc), (int)MathF.Round(t.Origin.Z * sc),
            (short)MathF.Round(q.X * 32767f), (short)MathF.Round(q.Y * 32767f), (short)MathF.Round(q.Z * 32767f), (short)MathF.Round(q.W * 32767f), spin, ang);
    }

    public void Begin(IWorldTapeIndex index)
    {
        Clear(); Idx = index; D.PosScale = PosScale;
        for (int i = 0; i < Idx.Count; i++) { int p = PartIdx(Idx.IdOf(i)); _prev[p] = Read(i, p); }
        foreach (var (k, b) in ReadSounds()) _snd[k] = b;
        SetKey(0);
    }

    IEnumerable<(TapeSoundKey, byte)> ReadSounds()
    {
        for (int i = 0; i < Idx.Count; i++)
            if (RefReg.Sound.TryGetValue(Idx.NodeOf(i), out var regs))
                foreach (var r in regs) yield return (new TapeSoundKey(Idx.IdOf(i), r.Ch), TapeSoundRange.ToByte(r.Ch, r.Get()));
    }

    void SetKey(int k)
    {
        var key = new RefKey { Sample = k, Sounds = new(_snd) };
        for (int i = 0; i < Idx.Count; i++) { int p = PartIdx(Idx.IdOf(i)); key.Poses.Add((p, _prev[p].Value)); }
        int at = D.Keys.FindIndex(x => x.Sample == k);
        if (at >= 0) D.Keys[at] = key; else D.Keys.Add(key);
    }

    static float AngleDeg(RefPose a, RefPose b)
    {
        float dot = MathF.Abs((a.Qx * (float)b.Qx + a.Qy * (float)b.Qy + a.Qz * (float)b.Qz + a.Qw * (float)b.Qw) / (32767f * 32767f));
        return 2f * MathF.Acos(MathF.Min(1f, dot)) * 180f / MathF.PI;
    }

    public virtual void Record(float lapClock)
    {
        while (_nextSound / WorldTapeConst.SoundRate <= lapClock + 1e-3f)
        {
            var ch = new List<(TapeSoundKey, byte)>();
            foreach (var (k, b) in ReadSounds()) if (!_snd.TryGetValue(k, out var old) || old != b) { _snd[k] = b; ch.Add((k, b)); }
            D.Sounds.Add(ch); _nextSound++;
        }
        while (_nextSample / WorldTapeConst.TapeRate <= lapClock + 1e-3f)
        {
            var delta = new List<(int, RefPose)>();
            for (int i = 0; i < Idx.Count; i++)
            {
                int p = PartIdx(Idx.IdOf(i)); var cur = Read(i, p);
                if (_prev[p] == cur) continue;
                if (!cur.Spin && _prev[p] is RefPose pp && AngleDeg(pp, cur) > 25f) Unregistered++;
                delta.Add((p, cur)); _prev[p] = cur;
            }
            D.Samples.Add(delta);
            if (_nextSample % WorldTapeConst.KeyframeSamples == 0) SetKey(_nextSample);
            _nextSample++;
        }
    }

    public void Event(in TapeEvent e) => D.Events.Add(e);

    public void Clear()
    {
        Idx = null; D = new(); _tp.Clear(); _prev.Clear(); _ang.Clear(); _snd.Clear(); _nextSample = _nextSound = 0; Unregistered = 0;
    }

    protected virtual bool SpawnKept(in TapeEvent e, float t) => true;
    protected virtual bool EffectKept(in TapeEvent e, float t) => true;

    public WorldTape Finish(float lapTime, ulong ghostFingerprint)
    {
        D.Events = D.Events.OrderBy(e => e.Time).ToList();
        foreach (var k in D.Keys)
        {
            float t = k.Sample / WorldTapeConst.TapeRate;
            foreach (var e in D.Events)
            {
                if (e.Time > t) break;
                if (e.Type == TapeEventType.Spawn && SpawnKept(e, t) && !D.Events.Any(d => d.Type == TapeEventType.Despawn && d.Instance == e.Instance && d.Time <= t)) k.Inst.Add(e);
                if ((e.Type == TapeEventType.Effect || e.Type == TapeEventType.Projectile) && e.Life > 0 && e.End > t && EffectKept(e, t)) k.Eff.Add(e);
                if (e.Type == TapeEventType.Effect && e.Effect == TapeEffect.BreakableBroken) k.Broken.Add((int)e.Param);
            }
        }
        var tape = new WorldTape { TrackId = "test", Set = 0, LapTime = lapTime, SampleCount = D.Samples.Count, GhostFingerprint = ghostFingerprint, Payload = RefCodec.Write(D) };
        foreach (var a in Idx.ActorKeys) tape.ActorStructure[a] = Idx.StructureOf(a);
        return tape;
    }
}

class RefPlayer : IWorldTapePlayer
{
    internal readonly WorldTape Tape;
    internal readonly IWorldTapeIndex Idx;
    internal readonly IWorldTapeSink Sink;
    internal readonly RefData D;
    readonly Dictionary<string, bool> _covers = new();
    internal readonly bool[] PartCovered;
    internal RefPose?[] S;
    internal int I = -1, Ev, Snd;
    internal float Clock;
    internal bool Has;
    internal readonly Dictionary<TapeSoundKey, byte> SndState = new();
    readonly HashSet<int> _spawned = new();
    readonly Node3D[] _nodes;

    public RefPlayer(WorldTape tape, IWorldTapeIndex idx, IWorldTapeSink sink)
    {
        Tape = tape; Idx = idx; Sink = sink; D = RefCodec.Read(tape.Payload);
        var live = idx.ActorKeys.ToHashSet();
        foreach (var a in D.Parts.Select(p => p.ActorKey).Concat(tape.ActorStructure.Keys).Distinct())
            _covers[a] = tape.ActorStructure.TryGetValue(a, out var st) && live.Contains(a) && idx.StructureOf(a).Hash == st.Hash;
        PartCovered = D.Parts.Select(p => Covers(p.ActorKey)).ToArray();
        S = new RefPose?[D.Parts.Count]; _nodes = new Node3D[D.Parts.Count];
    }

    public bool Covers(string actorKey) => actorKey != null && _covers.TryGetValue(actorKey, out var c) && c;
    protected virtual bool EventCovered(in TapeEvent e) => (e.Source == null || Covers(e.Source)) && (e.Parent == null || Covers(e.Parent));
    protected virtual void RestoreEffect(in TapeEvent e, float age) => Sink.Effect(e, age);

    internal int SampleIndex(float clock) => Math.Clamp((int)MathF.Floor(clock * WorldTapeConst.TapeRate + 1e-3f), 0, Math.Max(0, D.Samples.Count - 1));

    Node3D Node(int tp)
    {
        if (_nodes[tp] != null) return _nodes[tp];
        if (!Idx.TryFind(D.Parts[tp], out int n)) return null;
        return _nodes[tp] = Idx.NodeOf(n);
    }

    void SpawnInstance(in TapeEvent e)
    {
        var node = Sink.Spawn(e.Kind, e.Parent, e.Instance);
        Idx.AddInstance(e.Parent, e.Instance, node); _spawned.Add(e.Instance); Array.Clear(_nodes);
    }

    void DespawnInstance(int id)
    {
        if (!_spawned.Remove(id)) return;
        Idx.RemoveInstance(id); Sink.Despawn(id); Array.Clear(_nodes);
    }

    protected virtual void ClearEffectsOnSeek() => Sink.ClearEffects();

    protected virtual void DespawnAllOnSeek() { foreach (var i in _spawned.ToArray()) DespawnInstance(i); }

    internal void ClearWorld()
    {
        ClearEffectsOnSeek();
        DespawnAllOnSeek();
    }

    internal void Fire(in TapeEvent e, float age, bool seek)
    {
        if (!EventCovered(e)) return;
        switch (e.Type)
        {
            case TapeEventType.Spawn: SpawnInstance(e); break;
            case TapeEventType.Despawn: DespawnInstance(e.Instance); break;
            default:
                if (seek) RestoreEffect(e, age); else Sink.Effect(e, 0f);
                break;
        }
    }

    internal void ApplyDelta(int sample) { foreach (var (p, pose) in D.Samples[sample]) S[p] = pose; }

    void AdvanceSounds(float clock, bool write)
    {
        bool any = false;
        while (Snd < D.Sounds.Count && Snd / WorldTapeConst.SoundRate <= clock + 1e-3f) { foreach (var (k, b) in D.Sounds[Snd]) { SndState[k] = b; any = true; } Snd++; }
        if (any || write) WriteSounds();
    }

    void WriteSounds()
    {
        foreach (var (k, b) in SndState)
        {
            if (!Covers(k.Part.ActorKey) || !Idx.TryFind(k.Part, out int n)) continue;
            if (RefReg.Sound.TryGetValue(Idx.NodeOf(n), out var regs))
                foreach (var r in regs) if (r.Ch == k.Channel) r.Set(TapeSoundRange.FromByte(k.Channel, b));
        }
    }

    internal void PoseNodes(float clock)
    {
        float u = 0; int n = D.Samples.Count;
        if (I + 1 < n) u = Math.Clamp(clock * WorldTapeConst.TapeRate - I, 0f, 1f);
        for (int tp = 0; tp < S.Length; tp++) if (PartCovered[tp] && S[tp] is RefPose p && Node(tp) is Node3D node) Set(node, p, p, 0);
        if (u > 0)
            foreach (var (tp, np) in D.Samples[I + 1])
                if (PartCovered[tp] && S[tp] is RefPose p && Node(tp) is Node3D node) Set(node, p, np, u);
    }

    void Set(Node3D node, RefPose a, RefPose b, float u)
    {
        var q = a.Rot.Slerp(b.Rot, u);
        if (a.Spin && RefReg.Spin.TryGetValue(node, out var s)) q = (q * new Quaternion(s.Axis, a.Angle + (b.Angle - a.Angle) * u)).Normalized();
        node.Transform = new Transform3D(new Basis(q), a.Pos(D.PosScale).Lerp(b.Pos(D.PosScale), u));
        node.Visible = a.Visible;
    }

    public virtual void Seek(float clock)
    {
        ClearWorld();
        int i = SampleIndex(clock);
        var kf = D.Keys.Last(k => k.Sample <= i);
        float tk = kf.Sample / WorldTapeConst.TapeRate;
        foreach (var e in kf.Inst) Fire(e, 0, false);
        foreach (var e in kf.Eff) if (EventCovered(e) && e.End >= clock) RestoreEffect(e, clock - e.Time);
        Array.Clear(S);
        foreach (var (p, pose) in kf.Poses) S[p] = pose;
        for (int k = kf.Sample + 1; k <= i; k++) ApplyDelta(k);
        I = i;
        SndState.Clear(); foreach (var (k, b) in kf.Sounds) SndState[k] = b;
        Snd = (int)MathF.Floor(tk * WorldTapeConst.SoundRate + 1e-3f) + 1;
        AdvanceSounds(clock, true);
        Ev = 0;
        var broken = new HashSet<int>(kf.Broken);
        while (Ev < D.Events.Count && D.Events[Ev].Time <= clock)
        {
            var e = D.Events[Ev++];
            if (e.Type == TapeEventType.Effect && e.Effect == TapeEffect.BreakableBroken) { if (EventCovered(e)) broken.Add((int)e.Param); continue; }
            if (e.Time > tk && (e.Type is TapeEventType.Spawn or TapeEventType.Despawn || e.End >= clock)) Fire(e, clock - e.Time, true);
        }
        SetBroken(broken);
        Clock = clock; Has = true;
        PoseNodes(clock);
    }

    /// <summary>Seek: the sink makes the world match the breakables broken at the restored instant (the keyframe's set and what broke after it).</summary>
    protected virtual void SetBroken(IReadOnlyCollection<int> broken) => Sink.SetBrokenBreakables(broken);

    public void Apply(float lapClock, float dt)
    {
        if (!Has || lapClock < Clock - 1e-6f || lapClock - Clock > WorldTapeConst.SeekJump) { Seek(lapClock); return; }
        int i = SampleIndex(lapClock);
        while (I < i) ApplyDelta(++I);
        AdvanceSounds(lapClock, false);
        while (Ev < D.Events.Count && D.Events[Ev].Time <= lapClock) Fire(D.Events[Ev++], 0f, false);
        Clock = lapClock;
        PoseNodes(lapClock);
    }
}

static class TapeReference
{
    public static void Install()
    {
        TapeSeams.NewIndex = a => new RefIndex(a);
        TapeSeams.MarkPart = (n, name) => n.SetMeta(WorldTapeConst.PartMeta, name);
        TapeSeams.RegisterSpinner = (n, s) => RefReg.Spin.AddOrUpdate(n, s);
        TapeSeams.RegisterSound = (n, ch, get, set) => RefReg.Sound.GetOrCreateValue(n).Add(new RefReg.SoundReg(ch, get, set));
        TapeSeams.NewRecorder = () => new RefRecorder();
        TapeSeams.UnregisteredSpinCount = r => ((RefRecorder)r).Unregistered;
        TapeSeams.NewPlayer = (t, i, s) => new RefPlayer(t, i, s);
        TapeSeams.EncodeTape = RefFiles.Encode;
        TapeSeams.DecodeTape = RefFiles.Decode;
        TapeSeams.SavePair = new RefSaver().Save;
        TapeSeams.LoadTape = RefFiles.Load;
        TapeSeams.CleanStale = RefFiles.CleanStale;
        TapeSeams.RealOps = () => new RefFileOps();
    }
}
