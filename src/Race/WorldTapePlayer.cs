using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// Plays a world tape onto the live index (docs/replay-tape.md, sections 5 to 7): <see cref="Apply"/> poses the covered parts frame
/// by frame from the 15 Hz samples (position lerp, rotation slerp, visibility stepped, a spinner's unwrapped angle interpolated and
/// composed after the base rotation, the rest scale put back), writes the sound bytes back and fires the events; a jump goes through
/// <see cref="Seek"/>, which restores the nearest keyframe. It is driven by a clock and a sink only; it never touches the camera or
/// the simulation. An actor is covered only when its structure in the tape equals the live one; the others are left alone.
/// </summary>
public sealed class WorldTapePlayer : IWorldTapePlayer
{
    readonly TapeBody _d;
    readonly IWorldTapeIndex _idx;
    readonly IWorldTapeSink _sink;
    readonly Dictionary<string, bool> _covers = new();
    readonly bool[] _partCovered;
    readonly Vector3[] _scale;                 // rest scale per tape part (One: none)
    readonly Node3D[] _nodes;
    readonly ITapeSpinner[] _spin;
    readonly TapePartState?[] _s;              // the state of every part at the current sample
    readonly int[] _stamp;
    readonly bool[] _inDirty;
    readonly List<int> _dirty = new(), _interp = new(), _prevInterp = new();
    readonly Dictionary<TapeSoundKey, byte> _snd = new();
    readonly HashSet<int> _broken = new();                 // the breakables broken at the clock a seek restored
    readonly Dictionary<int, float> _spawned = new();      // the instances this player made (id -> the tape time of their Spawn): its to destroy
    readonly Dictionary<int, (string Kind, string Parent)> _spawnInfo = new();      // what each of those is (a seek may keep an instance the tape wants again)
    readonly Dictionary<int, (string Kind, string Parent)> _kept = new();           // during a seek only: the instances kept from before, not yet wanted again
    public int Recycled { get; private set; }
    int _i = -1, _ev, _sn, _frame;
    float _clock;
    bool _has;

    public WorldTapePlayer(WorldTape tape, IWorldTapeIndex index, IWorldTapeSink sink) : this(tape, index, sink, Validate(tape)) { }

    /// <summary>Plays an already validated body (see <see cref="Validate"/>); a null body plays nothing and covers nothing.</summary>
    public WorldTapePlayer(WorldTape tape, IWorldTapeIndex index, IWorldTapeSink sink, TapeBody body)
    {
        _idx = index; _sink = sink; _d = body;
        int np = body?.Parts.Count ?? 0;
        _partCovered = new bool[np]; _scale = new Vector3[np]; _nodes = new Node3D[np]; _spin = new ITapeSpinner[np]; _s = new TapePartState?[np]; _stamp = new int[np]; _inDirty = new bool[np];
        if (body == null) return;
        var live = index.ActorKeys.ToHashSet();
        foreach (var a in body.Parts.Select(p => p.ActorKey).Concat(tape.ActorStructure.Keys).Distinct())
            _covers[a] = tape.ActorStructure.TryGetValue(a, out var st) && live.Contains(a) && st.ParentPaths.ContainsKey("root") && index.StructureOf(a).Hash == st.Hash;
        for (int tp = 0; tp < np; tp++)
        {
            var id = body.Parts[tp];
            _partCovered[tp] = Covers(id.ActorKey);
            _scale[tp] = Vector3.One;
            if (_partCovered[tp] && index.StructureOf(id.ActorKey).RestPoses.TryGetValue(id.Part, out var rest)) _scale[tp] = rest.Basis.Scale;      // the live rest (equal to the tape's: the hashes match)
        }
    }

    /// <summary>
    /// The load boundary (section 4.3): the tape's body decoded and checked against its header, or null when it is corrupt, truncated or
    /// inconsistent (counts, key order, part numbers); a null body means the tape cannot be played and nothing may be covered by it.
    /// </summary>
    public static TapeBody Validate(WorldTape tape)
    {
        var b = tape == null ? null : WorldTapeCodec.ReadBody(tape.Payload);
        if (b == null || b.Samples.Count != tape.SampleCount || b.Samples.Count == 0 || b.Keys.Count == 0 || b.Keys[0].Sample != 0) return null;
        int np = b.Parts.Count, last = -1;
        foreach (var k in b.Keys)
        {
            if (k.Sample <= last || k.Sample >= b.Samples.Count) return null;
            last = k.Sample;
            foreach (var (p, _) in k.Poses) if (p < 0 || p >= np) return null;
        }
        foreach (var s in b.Samples) foreach (var (p, _) in s) if (p < 0 || p >= np) return null;
        float prev = float.NegativeInfinity;
        foreach (var e in b.Events) { if (!float.IsFinite(e.Time) || e.Time < prev) return null; prev = e.Time; }
        return b;
    }

    /// <summary>The instances alive now: their ids and the tape time they were spawned at (their age is the clock minus it).</summary>
    public IReadOnlyDictionary<int, float> LiveInstances => _spawned;

    /// <summary>The replay is over (a race, a preview, the scene's exit): every instance and effect this player made goes, and the next Apply seeks.</summary>
    public void Release()
    {
        if (_d == null) return;
        _sink.ClearEffects();
        _sink.SetBrokenBreakables(Array.Empty<int>());      // what the replay felled stands again
        foreach (var id in _spawned.Keys.ToArray()) DespawnInstance(id);
        _has = false;
    }

    public bool Covers(string actorKey) => actorKey != null && _covers.TryGetValue(actorKey, out var c) && c;
    bool EventCovered(in TapeEvent e) => (e.Source == null || Covers(e.Source)) && (e.Kind == TapeFxKind.WoodBreak || e.Parent == null || Covers(e.Parent));

    int SampleIndex(float clock) => Math.Clamp((int)MathF.Floor(clock * WorldTapeConst.TapeRate + 1e-3f), 0, Math.Max(0, _d.Samples.Count - 1));

    Node3D Node(int tp)
    {
        if (_nodes[tp] != null) return _nodes[tp];
        if (!_idx.TryFind(_d.Parts[tp], out int n)) return null;
        _spin[tp] = Tape.SpinnerOf(_nodes[tp] = _idx.NodeOf(n));
        // Dynamic parts are absent from the static structure. Their factory supplies the
        // rest scale when they first appear (e.g. the bus's 1.6x passenger figures).
        if (!_idx.StructureOf(_d.Parts[tp].ActorKey).RestPoses.ContainsKey(_d.Parts[tp].Part))
            _scale[tp] = _nodes[tp].Basis.Scale;
        return _nodes[tp];
    }

    void ForgetNodes() { Array.Clear(_nodes); Array.Clear(_spin); }

    void SpawnInstance(in TapeEvent e)
    {
        if (_spawned.ContainsKey(e.Instance)) DespawnInstance(e.Instance);      // (never two bodies of one instance)
        if (_kept.Remove(e.Instance, out var old))
        {
            // a seek's second look at an instance it kept: the same id, kind and parent and the sink's say-so, else it goes and a new one is built
            if (old == (e.Kind, e.Parent) && _sink is IWorldTapeSinkRecycle r && r.Recycle(e.Instance, e.Kind, e.Parent))
            {
                _spawned[e.Instance] = e.Time; _spawnInfo[e.Instance] = old; Recycled++; ForgetNodes();
                return;
            }
            _idx.RemoveInstance(e.Instance); _sink.Despawn(e.Instance);
        }
        var node = _sink.Spawn(e.Kind, e.Parent, e.Instance);
        if (node != null) _idx.AddInstance(e.Parent, e.Instance, node);
        _spawned[e.Instance] = e.Time; _spawnInfo[e.Instance] = (e.Kind, e.Parent); ForgetNodes();
    }

    void DespawnInstance(int id)
    {
        if (!_spawned.Remove(id)) return;
        _spawnInfo.Remove(id);
        _idx.RemoveInstance(id); _sink.Despawn(id); ForgetNodes();
    }

    void Fire(in TapeEvent e, float age, bool seek)
    {
        if (!EventCovered(e)) return;
        switch (e.Type)
        {
            case TapeEventType.Spawn: SpawnInstance(e); break;
            case TapeEventType.Despawn: DespawnInstance(e.Instance); break;
            default: _sink.Effect(e, seek || (e.Type == TapeEventType.Effect && (e.Effect == TapeEffect.BreakableBroken || e.Kind is TapeFxKind.WoodBreak or TapeFxKind.BusFire)) ? age : 0f); break;      // (a breakable's fall is seekable: played late by up to a step, it is that far on, as a seek shows it)
        }
    }

    void ApplyDelta(int sample)
    {
        foreach (var (p, st) in _d.Samples[sample]) { _s[p] = st; if (!_inDirty[p]) { _inDirty[p] = true; _dirty.Add(p); } }
    }

    void AdvanceSounds(float clock, bool write)
    {
        bool any = false;
        while (_sn < _d.Sounds.Count && _sn / WorldTapeConst.SoundRate <= clock + 1e-3f) { foreach (var (k, b) in _d.Sounds[_sn]) { _snd[k] = b; any = true; } _sn++; }
        if (any || write) WriteSounds();
    }

    void WriteSounds()
    {
        foreach (var (k, b) in _snd)
        {
            if (!Covers(k.Part.ActorKey) || !_idx.TryFind(k.Part, out int n)) continue;
            var regs = Tape.SoundsOf(_idx.NodeOf(n));
            if (regs != null) foreach (var r in regs) if (r.Channel == k.Channel) r.Set(TapeSoundRange.FromByte(k.Channel, b));
        }
    }

    void PoseNodes(float clock)
    {
        float u = 0; int n = _d.Samples.Count;
        if (_i + 1 < n) u = Math.Clamp(clock * WorldTapeConst.TapeRate - _i, 0f, 1f);
        _frame++; _interp.Clear();
        if (u > 0)
            foreach (var (tp, np) in _d.Samples[_i + 1])
                if (_partCovered[tp] && _s[tp] is TapePartState a && Node(tp) is Node3D node) { Set(tp, node, a, np, u); _stamp[tp] = _frame; _interp.Add(tp); }
        foreach (int tp in _prevInterp) Settle(tp);
        foreach (int tp in _dirty) { _inDirty[tp] = false; Settle(tp); }
        _dirty.Clear();
        _prevInterp.Clear(); _prevInterp.AddRange(_interp);
    }

    void Settle(int tp)
    {
        if (_stamp[tp] == _frame || !_partCovered[tp] || _s[tp] is not TapePartState a || Node(tp) is not Node3D node) return;
        Set(tp, node, a, a, 0);
        _stamp[tp] = _frame;
    }

    void Set(int tp, Node3D node, in TapePartState a, in TapePartState b, float u)
    {
        var q = u == 0 ? a.Rot.Normalized() : a.Rot.Normalized().Slerp(b.Rot.Normalized(), u);
        if (a.Spin && _spin[tp] is ITapeSpinner s) q = (q * new Quaternion(s.Axis, a.Angle + (b.Angle - a.Angle) * u)).Normalized();
        var pos = u == 0 ? a.Pos : a.Pos.Lerp(b.Pos, u);
        var basis = new Basis(q);
        var sc = _scale[tp];
        if (sc != Vector3.One) basis *= Basis.FromScale(sc);       // the rest scale is the part's, not the tape's (local scale: R * S, as a node holds it)
        node.Transform = new Transform3D(basis, pos);
        node.Visible = a.Visible;
    }

    public void Seek(float clock)
    {
        if (_d == null) return;
        _sink.ClearEffects();
        // the instances of a recyclable kind are kept until the events show whether the tape wants them again at this clock
        foreach (var i0 in _spawned.Keys.ToArray())
        {
            if (_sink is IWorldTapeSinkRecycle rc && _spawnInfo.TryGetValue(i0, out var info) && rc.CanRecycle(info.Kind)) { _kept[i0] = info; _spawned.Remove(i0); _spawnInfo.Remove(i0); }
            else DespawnInstance(i0);
        }
        try { SeekFrom(clock); }
        finally { DropKept(); }
    }

    void DropKept()
    {
        foreach (var id in _kept.Keys.ToArray()) { _idx.RemoveInstance(id); _sink.Despawn(id); }
        _kept.Clear(); ForgetNodes();
    }

    void SeekFrom(float clock)
    {
        int i = SampleIndex(clock);
        TapeBodyKey kf = _d.Keys[0];
        foreach (var k in _d.Keys) if (k.Sample <= i) kf = k;
        float tk = kf.Sample / WorldTapeConst.TapeRate;
        foreach (var e in kf.LiveInstances) Fire(e, 0, false);
        foreach (var e in kf.LiveEffects) if (EventCovered(e) && e.End >= clock) _sink.Effect(e, clock - e.Time);
        Array.Clear(_s);
        foreach (var (p, st) in kf.Poses) _s[p] = st;
        for (int k = kf.Sample + 1; k <= i; k++) ApplyDelta(k);
        _i = i;
        for (int p = 0; p < _s.Length; p++) if (_s[p] != null && !_inDirty[p]) { _inDirty[p] = true; _dirty.Add(p); }
        _prevInterp.Clear();
        _snd.Clear(); foreach (var (k, b) in kf.Sounds) _snd[k] = b;
        _sn = (int)MathF.Floor(tk * WorldTapeConst.SoundRate + 1e-3f) + 1;
        AdvanceSounds(clock, true);
        _ev = 0;
        // The breakables broken at this instant: the keyframe's set and what broke after it (a breakable is a state, not an effect with a life: the
        // sink makes the world match the whole set, which also stands up everything that is not in it, e.g. what the previous pass broke).
        _broken.Clear(); foreach (int b in kf.Broken) _broken.Add(b);
        while (_ev < _d.Events.Count && _d.Events[_ev].Time <= clock)
        {
            var e = _d.Events[_ev++];
            if (e.Type == TapeEventType.Effect && e.Effect == TapeEffect.BreakableBroken) { if (EventCovered(e)) _broken.Add((int)e.Param); continue; }
            if (e.Time > tk && (e.Type is TapeEventType.Spawn or TapeEventType.Despawn || e.End >= clock)) Fire(e, clock - e.Time, true);
        }
        _sink.SetBrokenBreakables(_broken, clock);
        _clock = clock; _has = true;
        PoseNodes(clock);
    }

    public void Apply(float lapClock, float dt)
    {
        if (_d == null) return;
        if (!_has || lapClock < _clock - 1e-6f || lapClock - _clock > WorldTapeConst.SeekJump) { Seek(lapClock); return; }
        int i = SampleIndex(lapClock);
        while (_i < i) ApplyDelta(++_i);
        AdvanceSounds(lapClock, false);
        while (_ev < _d.Events.Count && _d.Events[_ev].Time <= lapClock) { var e = _d.Events[_ev++]; Fire(e, MathF.Max(0f, lapClock - e.Time), false); }
        _clock = lapClock;
        PoseNodes(lapClock);
    }
}
