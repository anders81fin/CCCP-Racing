using System;
using System.Collections.Generic;
using System.Linq;
using CCCPRacing.Car;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// The world tape's dynamic instances in the game (docs/replay-tape.md, phase 2): the Hinds and the battery's missiles are made and freed
/// during a lap. Recording: the actors tell <see cref="Born"/> / <see cref="Gone"/>; the scene keeps the live ones in the index (so a lap
/// that starts with some alive records them in its first keyframe) and writes Spawn / Despawn events on the sample grid. Playback: the
/// sink's factories build the same models without simulating them, and an actor whose sorties the tape shows (WorldActor.TapeDrive) makes none
/// of its own while the replay runs.
/// </summary>
public partial class RaceScene
{
    sealed record TapeInst(int Id, string Kind, string Parent);

    readonly Dictionary<Node3D, TapeInst> _tapeInst = new();        // the live instances of the actors (not the replay's own)
    readonly Dictionary<string, Node3D> _tapeRoots = new();
    readonly HashSet<WorldActor> _tapeDriven = new();
    int _tapeNextInst, _tapeBorn, _tapeGone, _tapeAtBegin, _tapeBornInLap, _tapeGoneInLap;
    readonly HashSet<string> _tapeUnmarked = new();
    string _tapeAtBeginIds = "";

    /// <summary>One actor's view: the instances it makes belong to its key.</summary>
    sealed class TapeHost : ITapeInstanceHost
    {
        readonly RaceScene _s; readonly string _key;
        public TapeHost(RaceScene s, string key) { _s = s; _key = key; }
        public void Born(Node3D model) => _s.TapeBorn(model, _key);
        public void Gone(Node3D model) => _s.TapeGone(model);
    }

    /// <summary>After the index is built: the hosts, and the instances that already exist (made while the world was built).</summary>
    void TapeAdoptInstances()
    {
        foreach (var (a, key) in _tapeKeys) if (a.Node != null) _tapeRoots[key] = a.Node;
        foreach (var (a, key) in _tapeKeys)
        {
            if (_hindsAll != null && a.Node == _hindsAll) _hindsAll.TapeHost = new TapeHost(this, key);
            if (_busAll != null && a.Node == _busAll) _busAll.TapeHost = new TapeHost(this, key);
            if (_jetsAll != null && a.Node == _jetsAll) _jetsAll.TapeHost = new TapeHost(this, key);
        }
        foreach (var (key, root) in _tapeRoots)
            if (_tapeIndex.StructureOf(key).ParentPaths.ContainsKey("root")) Adopt(key, root);
        void Adopt(string key, Node n)
        {
            foreach (var c in n.GetChildren())
            {
                if (c is Node3D c3 && c3.HasMeta(WorldTapeConst.KindMeta)) TapeBorn(c3, key); else Adopt(key, c);
            }
        }
    }

    void TapeBorn(Node3D model, string parent)
    {
        if (_tapeRec == null || _tapeIndex == null || _tapeInst.ContainsKey(model)) return;
        if (!model.HasMeta(WorldTapeConst.KindMeta)) { if (_tapeUnmarked.Add(parent)) TapeLog($"{parent} made a model without {WorldTapeConst.KindMeta}: not recorded"); return; }
        var kind = (string)model.GetMeta(WorldTapeConst.KindMeta);
        int id = ++_tapeNextInst; _tapeInst[model] = new TapeInst(id, kind, parent);
        _tapeIndex.AddInstance(parent, id, model);
        _tapeBorn++; _tapeBornInLap++;
        // The first sample taken from now on carries its first pose: that sample's time is the Spawn's, so the replay never shows it unposed.
        if (_tapeOn) _tapeRec.Event(new TapeEvent(_tapeRec.Samples / WorldTapeConst.TapeRate, TapeEventType.Spawn, Source: parent, Kind: kind, Parent: parent, Instance: id));
    }

    void TapeGone(Node3D model)
    {
        if (!_tapeInst.Remove(model, out var inst)) return;
        _tapeIndex.RemoveInstance(inst.Id);
        _tapeGone++; _tapeGoneInLap++;
        // The first sample taken from now on lacks it: the replay removes it at that sample's time.
        if (_tapeOn) _tapeRec.Event(new TapeEvent(_tapeRec.Samples / WorldTapeConst.TapeRate, TapeEventType.Despawn, Source: inst.Parent, Instance: inst.Id));
    }

    /// <summary>A lap starts with these already alive: they are in the first keyframe and spawn at 0.</summary>
    void TapeSpawnAlive()
    {
        _tapeAtBegin = _tapeInst.Count; _tapeBornInLap = _tapeGoneInLap = 0; _tapeAtBeginIds = string.Join(",", _tapeInst.Values.OrderBy(i => i.Id).Select(i => i.Id));
        foreach (var inst in _tapeInst.Values.OrderBy(i => i.Id))
            _tapeRec.Event(new TapeEvent(0f, TapeEventType.Spawn, Source: inst.Parent, Kind: inst.Kind, Parent: inst.Parent, Instance: inst.Id));
    }

    // test flags: --tape-wrap-every=S (the replay wraps every S seconds, as if the ghost had ended), --tape-replay-start=T (the replay starts at T),
    // --tape-pos-at=T (the recorded lap logs where its instances are at the sample taken at T: what a replay seeking to T must show)
    float _tapeWrapEvery = -1, _tapeReplayStart = -1, _tapePosAt = -1, _tapePosEvery = -1;      // --tape-pos-every=S: the same, at every S seconds of the lap
    int _tapePosAtLap = -1, _tapePosEverySample = -1;
    bool TapeWrapDue() => _tapeWrapEvery > 0 && _replayClock - _titleReplayStart >= _tapeWrapEvery;
    float TapeStart(float start) => _tapeReplayStart >= 0 ? _tapeReplayStart : start;

    void TapePosAt()
    {
        if (_tapePosEvery > 0)
        {
            int k = _tapeRec.Samples - 1, every = (int)MathF.Round(_tapePosEvery * WorldTapeConst.TapeRate);
            if (k > 0 && k % every == 0 && k != _tapePosEverySample)
            {
                _tapePosEverySample = k;
                TapeLog($"pos-at {k / WorldTapeConst.TapeRate:F2} lap {_tapeLaps + 1}: [{string.Join(",", _tapeInst.OrderBy(kv => kv.Value.Id).Select(kv => $"{kv.Value.Id}={TapeXyz(kv.Key.Position)}"))}]");
            }
        }
        if (_tapePosAt < 0 || _tapeRec.Samples - 1 != (int)MathF.Round(_tapePosAt * WorldTapeConst.TapeRate) || _tapePosAtLap == _tapeLaps) return;
        _tapePosAtLap = _tapeLaps;
        TapeLog($"pos-at {_tapePosAt:F2} lap {_tapeLaps + 1}: [{string.Join(",", _tapeInst.OrderBy(kv => kv.Value.Id).Select(kv => $"{kv.Value.Id}={TapeXyz(kv.Key.Position)}"))}]");
    }

    /// <summary>--tape-debug: where the instances are every 5 s of the recorded lap and of the replay (the two series must agree).</summary>
    void TapeTrack(bool recording, float clock)
    {
        int k = (int)MathF.Round(clock * 60f);
        if (k % 300 != 0) return;
        string list = recording ? string.Join(",", _tapeInst.OrderBy(kv => kv.Value.Id).Select(kv => $"{kv.Value.Id}={TapeXyz(kv.Key.Position)}"))
            : string.Join(",", _tapeSink.Live.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={TapeXyz(kv.Value.Position)}"));
        TapeLog($"{(recording ? "rec" : "rep")}-track {clock:F2}: [{list}]");
    }

    static string TapeXyz(Vector3 p) => FormattableString.Invariant($"({p.X:F2},{p.Y:F2},{p.Z:F2})");

    /// <summary>The sink's factory: the model of a kind (optionally "kind/argument"), built without its simulation; null for an unknown kind.</summary>
    static Node3D TapeBuild(string kind, TyreEffects fx)
    {
        if (kind == null) return null;
        int slash = kind.IndexOf('/');
        string k = slash < 0 ? kind : kind[..slash], arg = slash < 0 ? "" : kind[(slash + 1)..];
        return k switch
        {
            "bus-passenger" => Track.SovietBus.BuildReplayPassenger(arg),
            "hind" => Track.HindSorties.BuildReplayAircraft(arg, fx),
            "buk-missile" => Track.Buk21Battery.BuildReplayMissile(),
            "jet" => Track.BalalaikaSorties.BuildReplayJet(fx),
            "missile" => Track.Missile.BuildReplay(fx),
            _ => null,
        };
    }

    /// <summary>The sink: the dynamic instances through the factories; the effects, projectiles and breakables through <see cref="TapeFxPlayer"/> (RaceScene.Tape.Fx.cs).</summary>
    sealed class TapeSink : IWorldTapeSink, IWorldTapeSinkRecycle
    {
        readonly RaceScene _s;
        readonly HashSet<string> _said = new();
        readonly Dictionary<int, Node3D> _inst = new();
        public int Spawned, Despawned, MostAtOnce, Recycled;
        public TapeSink(RaceScene s) { _s = s; }
        void Say(string what) { if (_said.Add(what)) TapeLog($"{what} not supported yet"); }
        public IReadOnlyDictionary<int, Node3D> Live => _inst;
        // models built ahead of the first seek (the viewer builds them one per frame, see ViewerEnter) and taken by Spawn; the rest is freed by DiscardStash
        readonly Dictionary<string, Queue<Node3D>> _stash = new();
        public void Prebuild(string kind)
        {
            var n = TapeBuild(kind, _s._fx);
            if (n == null) return;
            if (!_stash.TryGetValue(kind, out var q)) _stash[kind] = q = new Queue<Node3D>();
            q.Enqueue(n);
        }
        public void DiscardStash()
        {
            foreach (var q in _stash.Values) foreach (var n in q) if (GodotObject.IsInstanceValid(n)) { if (n.IsInsideTree()) n.QueueFree(); else n.Free(); }
            _stash.Clear();
        }
        public Node3D Spawn(string kind, string parentActorKey, int instance)
        {
            if (!_s._tapeRoots.TryGetValue(parentActorKey ?? "", out var root)) return null;
            Node3D n = null;
            if (kind != null && _stash.TryGetValue(kind, out var q) && q.Count > 0) n = q.Dequeue();
            n ??= TapeBuild(kind, _s._fx);
            if (n == null) { Say($"Spawn of kind '{kind}'"); return null; }
            if (_inst.Remove(instance, out var old)) Free(old);
            root.AddChild(n); _inst[instance] = n; Spawned++; MostAtOnce = Math.Max(MostAtOnce, _inst.Count);
            return n;
        }
        /// <summary>Only the Hind is kept across a seek: its replay model is posed whole by the tape plus two recorded channels, which Reset puts back.</summary>
        public bool CanRecycle(string kind) => kind != null && kind.StartsWith("hind/");
        public bool Recycle(int instance, string kind, string parentActorKey)
        {
            if (!CanRecycle(kind) || !_inst.TryGetValue(instance, out var n) || !GodotObject.IsInstanceValid(n) || n.IsQueuedForDeletion()) return false;
            if (!n.HasMeta(WorldTapeConst.KindMeta) || (string)n.GetMeta(WorldTapeConst.KindMeta) != kind) return false;
            if (!_s._tapeRoots.TryGetValue(parentActorKey ?? "", out var root) || n.GetParent() != root) return false;
            if (!Track.HindSorties.ResetReplayAircraft(n)) return false;
            Recycled++;
            return true;
        }
        public void Despawn(int instance)
        {
            if (!_inst.Remove(instance, out var n)) return;
            Despawned++; Free(n);
        }
        static void Free(Node3D n)
        {
            if (!GodotObject.IsInstanceValid(n)) return;
            n.GetParent()?.RemoveChild(n); n.QueueFree();
        }
        public void Effect(in TapeEvent e, float age) => _s._tapeFx?.Effect(e, age);
        public void ClearEffects() => _s._tapeFx?.Clear();
        public void SetBrokenBreakables(IReadOnlyCollection<int> obstacleIds) => _s._tapeFx?.SetBroken(obstacleIds);
        public void SetBrokenBreakables(IReadOnlyCollection<int> obstacleIds, float clock) => _s._tapeFx?.SetBroken(obstacleIds, clock);
    }

    /// <summary>--tape-debug: the kinds of nodes now in the tree, by their tape_kind (what a leak shows up as).</summary>
    string TapeKindCounts()
    {
        var count = new SortedDictionary<string, int>();
        void Walk(Node n)
        {
            if (n.HasMeta(WorldTapeConst.KindMeta)) { var k = (string)n.GetMeta(WorldTapeConst.KindMeta); k = k.Contains('/') ? k[..k.IndexOf('/')] : k; count[k] = count.GetValueOrDefault(k) + 1; }
            foreach (var c in n.GetChildren()) Walk(c);
        }
        Walk(this);
        return string.Join(",", count.Select(kv => $"{kv.Key}={kv.Value}"));
    }
}
