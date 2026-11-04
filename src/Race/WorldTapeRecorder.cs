using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// Records the indexed parts of the world on the 15 Hz tape grid (docs/replay-tape.md, sections 3 to 5). It reads node state and
/// never sets anything. The hot path (<see cref="Record"/>) allocates nothing once <see cref="Begin"/> has sized its buffers;
/// all compression is in <see cref="Finish"/>. It knows nothing of the scene, the menu or what a record lap is.
/// </summary>
public sealed class WorldTapeRecorder : IWorldTapeRecorder
{
    struct PartSt { public Transform3D Raw; public Quaternion Rot; public float AngRaw; public TapePartQ Q; public bool Sent; }       // Sent: the sample stream has carried this part (its first entry is absolute: the reader starts from nothing)
    const float TurnLimit = 0.97630f;      // cos(12.5 degrees): the quaternion dot of a 25 degree turn

    IWorldTapeIndex _idx;
    WorldTapeIndex _fast;
    int _gen = -1, _cnt = -1;
    // index position -> recorder part; the part table only ever grows
    Node3D[] _nodes = new Node3D[64];
    ITapeSpinner[] _spins = new ITapeSpinner[64];
    int[] _map = new int[64];
    PartSt[] _st = new PartSt[64];
    bool[] _isSpin = new bool[64];
    Vector3[] _rest = new Vector3[64];          // rest scale per recorder part
    int[] _unregBy = new int[64], _unsBy = new int[64];      // per recorder part: samples counted as an unregistered spin / an unrecordable scale change (reported by actor)
    readonly List<TapePartId> _parts = new();
    readonly Dictionary<TapePartId, int> _tpOf = new();
    // sound table: (part, channel) keys, and the registrations of the parts now in the index
    readonly List<(int Part, TapeSoundChannel Ch)> _sk = new();
    readonly Dictionary<TapeSoundKey, int> _skOf = new();
    int[] _skPrev = new int[16], _skInit = new int[16];
    TapeSoundReg[] _regs = new TapeSoundReg[16];
    int[] _regKey = new int[16];
    int _nRegs, _skInitN;
    // streams
    readonly TapeBuf _samples = new(2 << 20), _snd = new(1 << 16), _keys = new(1 << 20), _scr = new(1 << 16);
    int[] _sampleChunk = new int[64], _soundChunk = new int[64];
    int _nSampleChunks, _nSoundChunks;
    int[] _kSample = new int[256], _kStart = new int[256], _kCount = new int[256];
    int _nKeys, _nextSample, _nextSound, _unreg, _uns;
    readonly TapeEventLog _events = new();

    public int UnregisteredSpinners => _unreg;
    /// <summary>Samples that saw a part whose scale differs from its rest scale by more than 1 %: the tape keeps position and rotation only (section 4.1).</summary>
    public int UnscaledChanges => _uns;
    /// <summary>Statistics for measurements: samples taken so far and part entries written (every change of a part is one).</summary>
    public int Samples => _nextSample;
    public long Entries { get; private set; }
    /// <summary>How many times the cached view of the index was rebuilt (an instance added or removed): the samples that do that allocate, the others do not.</summary>
    public int Resyncs { get; private set; }

    public void Begin(IWorldTapeIndex index)
    {
        Clear();
        _idx = index; _fast = index as WorldTapeIndex;
        Resync();
        for (int i = 0; i < _idx.Count; i++)
        {
            var t = _nodes[i].Transform; var sp = _spins[i]; float ang = sp != null ? sp.Angle : 0f;
            ref var s = ref _st[_map[i]];
            if (OffScale(t.Basis, _rest[_map[i]])) { _uns++; _unsBy[_map[i]]++; }
            s.Q = Quantise(t, _nodes[i].Visible, sp, ang, out s.Rot); s.Raw = t; s.AngRaw = ang;
        }
        for (int r = 0; r < _nRegs; r++) _skPrev[_regKey[r]] = TapeSoundRange.ToByte(_regs[r].Channel, _regs[r].Get());
        _skInitN = _sk.Count; Array.Copy(_skPrev, _skInit, _skInitN);
        WriteKey(0);
    }

    /// <summary>The safety-net counters of this recording by actor and part ("Actor: part=n, ..."), for reports (not the hot path). Unregistered spins: fast real motions of parts that have no spinner; unscaled: scale changes the tape cannot carry.</summary>
    public (string Actor, string Part, int Unregistered, int Unscaled)[] CountersByPart()
    {
        var list = new List<(string, string, int, int)>();
        for (int tp = 0; tp < _parts.Count; tp++)
            if (_unregBy[tp] > 0 || _unsBy[tp] > 0) list.Add((_parts[tp].ActorKey, _parts[tp].Part, _unregBy[tp], _unsBy[tp]));
        return list.ToArray();
    }

    /// <summary>Appends an event. Allocation: none until a 256-event block is full (one 24 KB block per 256 events, kept for the next lap).</summary>
    public void Event(in TapeEvent e) => _events.Add(e);
    /// <summary>Events logged so far this lap (statistics).</summary>
    public int EventCount => _events.Count;

    public void Clear()
    {
        _idx = null; _fast = null; _gen = _cnt = -1;
        _parts.Clear(); _tpOf.Clear(); _sk.Clear(); _skOf.Clear(); _nRegs = 0; _skInitN = 0;
        _samples.N = _snd.N = _keys.N = _scr.N = 0;
        _nSampleChunks = _nSoundChunks = _nKeys = _nextSample = _nextSound = _unreg = _uns = 0; Entries = 0; Resyncs = 0; AllocSound = AllocSample = AllocKey = 0; AllocNotes.Clear();
        _events.Clear();
        Array.Clear(_st); Array.Clear(_nodes); Array.Clear(_spins); Array.Clear(_unregBy); Array.Clear(_unsBy);
    }

    /// <summary>Rebuilds the cached view of the index (first use, or an instance added or removed).</summary>
    void Resync()
    {
        int n = _idx.Count;
        if (n > _nodes.Length)
        {
            int m = Math.Max(n, _nodes.Length * 2);
            _nodes = new Node3D[m]; _spins = new ITapeSpinner[m]; _map = new int[m];
        }
        Array.Clear(_nodes); Array.Clear(_spins);
        _nRegs = 0;
        for (int i = 0; i < n; i++)
        {
            var id = _idx.IdOf(i); var node = _idx.NodeOf(i);
            var sp = _fast != null ? _fast.Spinners[i] : Tape.SpinnerOf(node);
            _nodes[i] = node; _spins[i] = sp;
            if (!_tpOf.TryGetValue(id, out int tp))
            {
                tp = _parts.Count; _parts.Add(id); _tpOf[id] = tp;
                if (tp >= _st.Length) { Array.Resize(ref _st, _st.Length * 2); Array.Resize(ref _isSpin, _st.Length); Array.Resize(ref _rest, _st.Length); Array.Resize(ref _unregBy, _st.Length); Array.Resize(ref _unsBy, _st.Length); }
                _isSpin[tp] = sp != null; _rest[tp] = _fast != null ? _fast.RestScale(i) : node.Basis.Scale;
            }
            _map[i] = tp;
            var regs = Tape.SoundsOf(node);
            if (regs == null) continue;
            foreach (var reg in regs)
            {
                var key = new TapeSoundKey(id, reg.Channel);
                if (!_skOf.TryGetValue(key, out int k))
                {
                    k = _sk.Count; _sk.Add((tp, reg.Channel)); _skOf[key] = k;
                    if (k >= _skPrev.Length) { Array.Resize(ref _skPrev, _skPrev.Length * 2); Array.Resize(ref _skInit, _skPrev.Length); }
                    _skPrev[k] = -1;
                }
                if (_nRegs == _regs.Length) { Array.Resize(ref _regs, _nRegs * 2); Array.Resize(ref _regKey, _nRegs * 2); }
                _regs[_nRegs] = reg; _regKey[_nRegs++] = k;
            }
        }
        _scr.Reserve(n * 48 + 64); Resyncs++;
        _gen = _fast != null ? _fast.Generation : 0; _cnt = n;
    }

    static TapePartQ Quantise(in Transform3D t, bool vis, ITapeSpinner sp, float ang, out Quaternion q)
    {
        var o = t.Origin;
        q = t.Basis.Orthonormalized().GetRotationQuaternion();      // never on a scaled basis
        if (sp != null) q = (q * new Quaternion(sp.Axis, -ang)).Normalized();
        TapeWire.QuatQ(q, out int ri, out int a, out int b, out int c);
        return new TapePartQ
        {
            Has = true, Vis = vis, X = (int)MathF.Round(o.X * TapeWire.PosScale), Y = (int)MathF.Round(o.Y * TapeWire.PosScale), Z = (int)MathF.Round(o.Z * TapeWire.PosScale),
            RI = ri, RA = a, RB = b, RC = c, Ang = sp != null ? (int)MathF.Round(ang * TapeWire.AngleScale) : 0,
        };
    }

    static bool OffScale(in Basis b, Vector3 rest)
    {
        var s = b.Scale;
        return MathF.Abs(s.X - rest.X) > 0.01f * MathF.Abs(rest.X) || MathF.Abs(s.Y - rest.Y) > 0.01f * MathF.Abs(rest.Y) || MathF.Abs(s.Z - rest.Z) > 0.01f * MathF.Abs(rest.Z);
    }

    static bool Same(in TapePartQ p, in TapePartQ c) =>
        p.Vis == c.Vis && p.X == c.X && p.Y == c.Y && p.Z == c.Z && p.RI == c.RI && p.RA == c.RA && p.RB == c.RB && p.RC == c.RC && p.Ang == c.Ang;

    public void Record(float lapClock)
    {
        if (_idx == null) return;
        if (_fast != null ? _fast.Generation != _gen : _idx.Count != _cnt) Resync();
        long a0 = Trace ? GC.GetAllocatedBytesForCurrentThread() : 0, a1;
        while (_nextSound / WorldTapeConst.SoundRate <= lapClock + 1e-3f) SoundTick();
        if (Trace) { a1 = GC.GetAllocatedBytesForCurrentThread(); AllocSound += a1 - a0; a0 = a1; }
        bool first = true;
        while (_nextSample / WorldTapeConst.TapeRate <= lapClock + 1e-3f)
        {
            Sample(first); first = false;
            if (Trace) { a1 = GC.GetAllocatedBytesForCurrentThread(); AllocSample += a1 - a0; a0 = a1; }
            if (_nextSample % WorldTapeConst.KeyframeSamples == 0) WriteKey(_nextSample);
            if (Trace) { a1 = GC.GetAllocatedBytesForCurrentThread(); AllocKey += a1 - a0; a0 = a1; }
            _nextSample++;
        }
    }

    /// <summary>Diagnostics (--tape-debug): while on, <see cref="Record"/> adds the bytes it allocates to these, by phase (the sound tick, the part sample, the keyframe). Off in play.</summary>
    public bool Trace;
    public long AllocSound, AllocSample, AllocKey;
    /// <summary>Diagnostics: the first parts whose read or write allocated while <see cref="Trace"/> was on ("actor/part read 448 B").</summary>
    public readonly List<string> AllocNotes = new();
    /// <summary>The three reads of a part, each measured (diagnostics only): which of them allocated, how long the part's read took, and whether an immediate repeat allocates too (a repeat that allocates nothing: a one-off cost of the first call of something, not a per-sample one).</summary>
    (Transform3D, bool, float) TracedRead(int tp, Node3D node, ITapeSpinner sp)
    {
        long a0 = GC.GetAllocatedBytesForCurrentThread(), w0 = System.Diagnostics.Stopwatch.GetTimestamp();
        var t = node.Transform; long a1 = GC.GetAllocatedBytesForCurrentThread();
        bool vis = node.Visible; long a2 = GC.GetAllocatedBytesForCurrentThread();
        float ang = sp != null ? sp.Angle : 0f; long a3 = GC.GetAllocatedBytesForCurrentThread(), w1 = System.Diagnostics.Stopwatch.GetTimestamp();
        if (a3 > a0 && AllocNotes.Count < 8)
        {
            long b0 = GC.GetAllocatedBytesForCurrentThread();
            for (int k = 0; k < 3; k++) { var t2 = node.Transform; bool v2 = node.Visible; float g2 = sp != null ? sp.Angle : 0f; if (t2.Origin.X == float.NaN && v2 && g2 == 0f) break; }
            long again = GC.GetAllocatedBytesForCurrentThread() - b0;
            AllocNotes.Add($"{_parts[tp]} ({node.GetClass()}{(sp != null ? ", spinner" : "")}): Transform {a1 - a0} B, Visible {a2 - a1} B, spinner angle {a3 - a2} B, the three reads took {(w1 - w0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency:F2} ms; three more reads {again} B");
        }
        return (t, vis, ang);
    }
    void NoteAlloc(int tp, string what, long bytes)
    {
        if (bytes > 0 && AllocNotes.Count < 8) AllocNotes.Add($"{_parts[tp]} {what} {bytes} B");
    }

    void SoundTick()
    {
        if (_nextSound % TapeWire.ChunkSamples == 0) Mark(ref _soundChunk, ref _nSoundChunks, _snd.N);
        _scr.N = 0; int changed = 0;
        for (int r = 0; r < _nRegs; r++)
        {
            var reg = _regs[r]; int k = _regKey[r];
            int b = TapeSoundRange.ToByte(reg.Channel, reg.Get());
            if (_skPrev[k] == b) continue;
            _skPrev[k] = b; _scr.Var((uint)k); _scr.U8(b); changed++;
        }
        _snd.Var((uint)changed); _snd.Bytes(_scr.Span);
        _nextSound++;
    }

    static void Mark(ref int[] marks, ref int n, int at)
    {
        if (n == marks.Length) Array.Resize(ref marks, n * 2);
        marks[n++] = at;
    }

    void Sample(bool first)
    {
        if (_nextSample % TapeWire.ChunkSamples == 0) Mark(ref _sampleChunk, ref _nSampleChunks, _samples.N);
        if (!first) { _samples.U8(0); return; }
        _scr.N = 0; int changed = 0, prevTp = 0, n = _idx.Count;
        for (int i = 0; i < n; i++)
        {
            int tp = _map[i]; ref var s = ref _st[tp];
            var node = _nodes[i]; var sp = _spins[i];
            Transform3D t; bool vis; float ang;
            if (Trace) (t, vis, ang) = TracedRead(tp, node, sp);
            else { t = node.Transform; vis = node.Visible; ang = sp != null ? sp.Angle : 0f; }
            if (s.Sent && s.Q.Vis == vis && s.AngRaw == ang && t.Equals(s.Raw)) continue;
            long w0 = Trace ? GC.GetAllocatedBytesForCurrentThread() : 0;
            s.Raw = t; s.AngRaw = ang;
            if (OffScale(t.Basis, _rest[tp])) { _uns++; _unsBy[tp]++; }
            var cur = Quantise(t, vis, sp, ang, out var q);
            if (s.Sent)
            {
                if (Same(s.Q, cur)) continue;
                if (sp == null && MathF.Abs(q.Dot(s.Rot)) < TurnLimit) { _unreg++; _unregBy[tp]++; }
            }
            TapeWire.WriteEntry(_scr, tp - prevTp, s.Sent ? s.Q : default, cur, _isSpin[tp]);
            s.Sent = true;
            prevTp = tp; s.Q = cur; s.Rot = q; changed++;
            if (Trace) NoteAlloc(tp, "write", GC.GetAllocatedBytesForCurrentThread() - w0);
        }
        _samples.Var((uint)changed); _samples.Bytes(_scr.Span);
        Entries += changed;
    }

    /// <summary>A keyframe after the delta of <paramref name="sample"/>: every part in the index, absolute. Sample 0 replaces the one Begin wrote.</summary>
    void WriteKey(int sample)
    {
        if (sample == 0) { _nKeys = 0; _keys.N = 0; }
        if (_nKeys == _kSample.Length) { int m = _nKeys * 2; Array.Resize(ref _kSample, m); Array.Resize(ref _kStart, m); Array.Resize(ref _kCount, m); }
        _kSample[_nKeys] = sample; _kStart[_nKeys] = _keys.N; int count = 0;
        for (int i = 0, n = _idx.Count; i < n; i++)
        {
            int tp = _map[i]; ref var s = ref _st[tp];
            if (!s.Q.Has) continue;
            _keys.Var((uint)tp); TapeWire.WritePose(_keys, s.Q, _isSpin[tp]); count++;
        }
        _kCount[_nKeys++] = count;
    }

    /// <summary>
    /// A lap's recording, copied out of the recorder (its buffers are reused by the next lap, which can begin at once): everything <see cref="Compress"/> needs, plain managed data,
    /// no Godot object. Taking it costs a few array copies; the compression, the slow part of finishing a lap, can then run on a worker.
    /// </summary>
    public sealed class Recording
    {
        internal List<TapeEvent> Events; internal List<TapePartId> Parts; internal bool[] IsSpin; internal List<(int Part, TapeSoundChannel Ch)> Sk;
        internal int NKeys, SkInitN, NextSample, NextSound, NSampleChunks, NSoundChunks;
        internal int[] KSample, KStart, KCount, SkInit, SampleChunk, SoundChunk;
        internal TapeBuf Keys, Snd, Samples;
        internal Dictionary<string, TapeActorStructure> Structure;
    }

    static TapeBuf Copy(TapeBuf b) { var c = new TapeBuf(Math.Max(1, b.N)); b.Span.CopyTo(c.B); c.N = b.N; return c; }

    /// <summary>The lap so far, copied (see <see cref="Recording"/>).</summary>
    public Recording Snapshot()
    {
        var r = new Recording
        {
            Events = _events.ToList(), Parts = new List<TapePartId>(_parts), IsSpin = _isSpin[.._parts.Count], Sk = new List<(int, TapeSoundChannel)>(_sk),
            NKeys = _nKeys, SkInitN = _skInitN, NextSample = _nextSample, NextSound = _nextSound, NSampleChunks = _nSampleChunks, NSoundChunks = _nSoundChunks,
            KSample = _kSample[.._nKeys], KStart = _kStart[.._nKeys], KCount = _kCount[.._nKeys], SkInit = _skInit[..Math.Max(_skInitN, 0)],
            SampleChunk = _sampleChunk[.._nSampleChunks], SoundChunk = _soundChunk[.._nSoundChunks],
            Keys = Copy(_keys), Snd = Copy(_snd), Samples = Copy(_samples), Structure = new(),
        };
        foreach (var a in _idx.ActorKeys)
        {
            var st = _idx.StructureOf(a);
            r.Structure[a] = new TapeActorStructure { Version = st.Version, Hash = st.Hash, ParentPaths = new(st.ParentPaths), RestPoses = new(st.RestPoses), RestInRoot = new(st.RestInRoot) };
        }
        return r;
    }

    public WorldTape Finish(float lapTime, ulong ghostFingerprint) => Compress(Snapshot(), lapTime, ghostFingerprint);

    /// <summary>The tape of a snapshot: the tables, the keyframes and the Brotli chunks. Static and on plain data: it can run on any thread.</summary>
    public static WorldTape Compress(Recording r, float lapTime, ulong ghostFingerprint)
    {
        var events = r.Events.OrderBy(e => e.Time).ToList();
        var strings = new Dictionary<string, int>(); var table = new List<string>();
        int S(string s) { if (!strings.TryGetValue(s, out int i)) { i = table.Count; table.Add(s); strings[s] = i; } return i; }

        var parts = new TapeBuf(1024); parts.Var((uint)r.Parts.Count);
        for (int tp = 0; tp < r.Parts.Count; tp++) { parts.Var((uint)S(r.Parts[tp].ActorKey)); parts.Var((uint)S(r.Parts[tp].Part)); parts.U8(r.IsSpin[tp] ? 1 : 0); }
        var sks = new TapeBuf(256); sks.Var((uint)r.Sk.Count);
        foreach (var (p, ch) in r.Sk) { sks.Var((uint)p); sks.U8((int)ch); }
        var evs = new TapeBuf(1024); evs.Var((uint)events.Count);
        foreach (var e in events) TapeWire.WriteEvent(evs, e, strings, table);

        var despawn = new Dictionary<int, float>();
        foreach (var e in events) if (e.Type == TapeEventType.Despawn && (!despawn.TryGetValue(e.Instance, out float d) || e.Time < d)) despawn[e.Instance] = e.Time;
        var keyBuf = new TapeBuf(1 << 16); keyBuf.Var((uint)r.NKeys);
        var state = new int[r.Sk.Count]; Array.Fill(state, -1); Array.Copy(r.SkInit, state, Math.Min(r.SkInitN, state.Length));
        var snd = new TapeRd(r.Snd.B, 0, r.Snd.N); int tick = 0;
        for (int k = 0; k < r.NKeys; k++)
        {
            float t = r.KSample[k] / WorldTapeConst.TapeRate;
            keyBuf.Var((uint)r.KSample[k]); keyBuf.Var((uint)r.KCount[k]);
            int end = k + 1 < r.NKeys ? r.KStart[k + 1] : r.Keys.N;
            keyBuf.Bytes(new ReadOnlySpan<byte>(r.Keys.B, r.KStart[k], end - r.KStart[k]));
            // the sound state a seek restores: every tick up to the keyframe's time (the player resumes at the next one)
            int lastTick = (int)MathF.Floor(t * WorldTapeConst.SoundRate + 1e-3f);
            for (; tick <= lastTick && tick < r.NextSound; tick++)
                for (int c = (int)snd.Var(), j = 0; j < c; j++) { int sk = (int)snd.Var(); state[sk] = snd.U8(); }
            int ns = 0; foreach (int v in state) if (v >= 0) ns++;
            keyBuf.Var((uint)ns); for (int sk = 0; sk < state.Length; sk++) if (state[sk] >= 0) { keyBuf.Var((uint)sk); keyBuf.U8(state[sk]); }
            var inst = new List<int>(); var eff = new List<int>(); var broken = new List<int>();
            for (int i = 0; i < events.Count; i++)
            {
                var e = events[i];
                if (e.Time > t) break;
                if (e.Type == TapeEventType.Spawn && !(despawn.TryGetValue(e.Instance, out float dt) && dt <= t)) inst.Add(i);
                if ((e.Type == TapeEventType.Effect || e.Type == TapeEventType.Projectile) && e.Life > 0 && e.End > t) eff.Add(i);
                if (e.Type == TapeEventType.Effect && e.Effect == TapeEffect.BreakableBroken) broken.Add((int)e.Param);
            }
            keyBuf.Var((uint)inst.Count); foreach (int i in inst) keyBuf.Var((uint)i);
            keyBuf.Var((uint)eff.Count); foreach (int i in eff) keyBuf.Var((uint)i);
            keyBuf.Var((uint)broken.Count); foreach (int b in broken) keyBuf.Zig(b);
        }

        var meta = new TapeBuf(1 << 16);
        meta.Var((uint)r.NextSample); meta.Var((uint)r.NextSound);
        meta.Var((uint)table.Count); foreach (var s in table) meta.Str(s);
        meta.Bytes(parts.Span); meta.Bytes(sks.Span); meta.Bytes(evs.Span); meta.Bytes(keyBuf.Span);

        var body = new TapeBuf(1 << 16);
        body.Bytes(WorldTapeCodec.BodyMagic); body.F32(TapeWire.PosScale);
        TapeWire.Chunk(body, meta.Span);
        Chunks(body, r.Samples, r.SampleChunk, r.NSampleChunks);
        Chunks(body, r.Snd, r.SoundChunk, r.NSoundChunks);

        var tape = new WorldTape { TrackId = "", Set = 0, LapTime = lapTime, SampleCount = r.NextSample, GhostFingerprint = ghostFingerprint, Payload = body.Span.ToArray() };
        foreach (var (a, st) in r.Structure) tape.ActorStructure[a] = st;
        return tape;
    }

    static void Chunks(TapeBuf dst, TapeBuf src, int[] starts, int n)
    {
        dst.Var((uint)n);
        for (int c = 0; c < n; c++)
        {
            int from = starts[c], to = c + 1 < n ? starts[c + 1] : src.N;
            TapeWire.Chunk(dst, new ReadOnlySpan<byte>(src.B, from, to - from));
        }
    }
}

/// <summary>
/// The event log of a lap in fixed blocks: appending never copies what is already there (a growing List would copy a large array, in the large object
/// heap), and the blocks stay for the next lap, so after the first lap an event costs nothing but its own 100 bytes in a block.
/// </summary>
sealed class TapeEventLog
{
    const int Block = 256;
    readonly List<TapeEvent[]> _blocks = new();
    int _n;
    public int Count => _n;
    public void Add(in TapeEvent e)
    {
        int b = _n / Block;
        if (b == _blocks.Count) _blocks.Add(new TapeEvent[Block]);
        _blocks[b][_n % Block] = e; _n++;
    }
    public void Clear() => _n = 0;
    public List<TapeEvent> ToList()
    {
        var l = new List<TapeEvent>(_n);
        for (int i = 0; i < _n; i++) l.Add(_blocks[i / Block][i % Block]);
        return l;
    }
}
