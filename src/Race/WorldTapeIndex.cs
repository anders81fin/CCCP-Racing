using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// The recordable parts of the world (docs/replay-tape.md, section 4): built once from the actors (key and root node), it
/// holds the nodes that carry the <c>tape_part</c> meta under stable <see cref="TapePartId"/>s, each actor's
/// <see cref="TapeActorStructure"/> as it was at build time, and the dynamic instances added and removed during a lap.
/// </summary>
public sealed class WorldTapeIndex : IWorldTapeIndex
{
    Node3D[] _nodes = new Node3D[64];
    ITapeSpinner[] _spin = new ITapeSpinner[64];
    TapePartId[] _ids = new TapePartId[64];
    int[] _inst = new int[64];                 // 0: static part of its actor, else the dynamic instance id
    int _n;
    readonly Dictionary<TapePartId, int> _map = new();
    Vector3[] _scale = new Vector3[64];        // rest scale of the part, read when it was added
    readonly List<string> _actors = new();
    readonly Dictionary<string, Node3D> _roots = new();
    readonly Dictionary<string, TapeActorStructure> _struct = new();

    public WorldTapeIndex(IReadOnlyList<(string Key, Node3D Root)> actors)
    {
        foreach (var (key, root) in actors)
        {
            var parts = Collect(key, root);
            if (parts == null) continue;                 // a duplicate part name: the whole actor is uncovered
            _actors.Add(key); _roots[key] = root;
            foreach (var (n, name) in parts) Add(key, n, name, 0);
        }
        Rebuild();
        foreach (var key in _actors) _struct[key] = Compute(key);
    }

    /// <summary>Changes whenever the part numbering changes (an instance added or removed).</summary>
    public int Generation { get; private set; }
    public int Count => _n;
    internal Node3D[] Nodes => _nodes;
    internal ITapeSpinner[] Spinners => _spin;
    public TapePartId IdOf(int part) => _ids[part];
    public Node3D NodeOf(int part) => _nodes[part];
    public bool TryFind(TapePartId id, out int part) => _map.TryGetValue(id, out part);
    public IEnumerable<string> ActorKeys => _actors;
    /// <summary>An actor that is not indexed (a duplicate part name) has an empty structure: it matches no tape.</summary>
    public TapeActorStructure StructureOf(string actorKey) => _struct.TryGetValue(actorKey, out var st) ? st : new TapeActorStructure();
    /// <summary>The rest scale of an indexed part (its scale when the index was built), the one the player re-applies.</summary>
    public Vector3 RestScale(int part) => _scale[part];

    /// <summary>The marked nodes under <paramref name="n"/>. A dynamic instance (a node carrying <c>tape_kind</c>) is a subtree of its own: an actor's static parts never include one, only <see cref="AddInstance"/> walks into it.</summary>
    static void Walk(Node n, Action<Node3D> f, bool top = true)
    {
        if (!top && n.HasMeta(WorldTapeConst.KindMeta)) return;
        if (n is Node3D n3 && n.HasMeta(WorldTapeConst.PartMeta)) f(n3);
        foreach (var c in n.GetChildren()) Walk(c, f, false);
    }

    /// <summary>The marked nodes under a root in tree order with their names; null (and one warning) when two of them share a name: such a builder bug leaves the actor uncovered, names are never numbered.</summary>
    static List<(Node3D, string)> Collect(string actorKey, Node3D root)
    {
        var list = new List<(Node3D, string)>(); var seen = new HashSet<string>();
        Walk(root, n => list.Add((n, (string)n.GetMeta(WorldTapeConst.PartMeta))));
        foreach (var (_, name) in list)
            if (!seen.Add(name)) { GD.PushWarning($"world tape: actor '{actorKey}' has two parts named '{name}'; the actor is not recorded"); return null; }
        return list;
    }

    void Add(string actorKey, Node3D node, string name, int inst)
    {
        if (inst != 0) name = $"{inst}/{name}";
        var id = new TapePartId(actorKey, name);
        if (_n == _nodes.Length) { int m = _n * 2; Array.Resize(ref _nodes, m); Array.Resize(ref _spin, m); Array.Resize(ref _ids, m); Array.Resize(ref _inst, m); Array.Resize(ref _scale, m); }
        _nodes[_n] = node; _spin[_n] = Tape.SpinnerOf(node); _ids[_n] = id; _inst[_n] = inst; _scale[_n] = node.Basis.Scale; _n++;
        _map[id] = _n - 1;
    }

    void Rebuild()
    {
        _map.Clear();
        for (int i = 0; i < _n; i++) _map[_ids[i]] = i;
        Generation++;
    }

    public int AddInstance(string actorKey, int instance, Node3D root)
    {
        int first = _n;
        var parts = Collect($"{actorKey} (instance {instance})", root);
        if (parts != null) foreach (var (n, name) in parts) Add(actorKey, n, name, instance);
        Rebuild();
        return first;
    }

    public void RemoveInstance(int instance)
    {
        int w = 0;
        for (int i = 0; i < _n; i++)
        {
            if (_inst[i] == instance) continue;
            _nodes[w] = _nodes[i]; _spin[w] = _spin[i]; _ids[w] = _ids[i]; _inst[w] = _inst[i]; _scale[w] = _scale[i]; w++;
        }
        for (int i = w; i < _n; i++) { _nodes[i] = null; _spin[i] = null; _ids[i] = default; }
        _n = w;
        Rebuild();
    }

    static int Q(float v, float s) => (int)MathF.Round(v * s);

    static string PathOf(Node3D root, Node3D n)
    {
        var parts = new List<string>();
        for (Node c = n; c != root; c = c.GetParent()) parts.Add($"{c.GetParent().GetChildren().IndexOf(c)}:{c.GetClass()}");
        parts.Reverse();
        return string.Join("/", parts);
    }

    static Transform3D InRoot(Node3D root, Node3D n)
    {
        var t = Transform3D.Identity;
        for (Node3D c = n; c != root; c = (Node3D)c.GetParent()) t = c.Transform * t;
        return t;
    }

    /// <summary>Rest pose into the hash: 1 mm, 0.1 degree, 0.1 % scale. The root's own placement is the actor's business (it is recorded every sample), so only its scale counts.</summary>
    static TapeHash Rest(TapeHash h, Transform3D t, bool placement)
    {
        var e = t.Basis.Orthonormalized().GetEuler(); var s = t.Basis.Scale;
        if (placement)
        {
            h = h.Add(Q(t.Origin.X, 1000)).Add(Q(t.Origin.Y, 1000)).Add(Q(t.Origin.Z, 1000));
            h = h.Add(Q(e.X, 572.958f)).Add(Q(e.Y, 572.958f)).Add(Q(e.Z, 572.958f));
        }
        return h.Add(Q(s.X, 1000)).Add(Q(s.Y, 1000)).Add(Q(s.Z, 1000));
    }

    TapeActorStructure Compute(string actorKey)
    {
        var root = _roots[actorKey];
        var st = new TapeActorStructure { Version = root.HasMeta(WorldTapeConst.VersionMeta) ? (int)root.GetMeta(WorldTapeConst.VersionMeta) : 0 };
        var h = TapeHash.Start().Add(st.Version);
        var mine = new List<int>();
        for (int i = 0; i < _n; i++) if (_inst[i] == 0 && _ids[i].ActorKey == actorKey) mine.Add(i);
        mine.Sort((a, b) => string.CompareOrdinal(_ids[a].Part, _ids[b].Part));
        foreach (int i in mine)
        {
            var node = _nodes[i]; var part = _ids[i].Part;
            var path = PathOf(root, node); var rel = InRoot(root, node);
            st.ParentPaths[part] = path; st.RestPoses[part] = node.Transform; st.RestInRoot[part] = rel;
            foreach (var ch in part + "|" + node.GetClass() + "|" + path) h = h.Add((int)ch);
            bool isRoot = node == root;
            h = Rest(Rest(h, node.Transform, !isRoot), rel, !isRoot);
        }
        st.Hash = (uint)(h.Value ^ (h.Value >> 32));
        return st;
    }
}

public static class TapeActorStructureExt
{
    /// <summary>The scale of a part's rest pose (the file keeps the rest poses whole): what the player multiplies onto the recorded rotation. One when the part is unknown.</summary>
    public static Vector3 RestScale(this TapeActorStructure st, string part) => st.RestPoses.TryGetValue(part, out var t) ? t.Basis.Scale : Vector3.One;
}
