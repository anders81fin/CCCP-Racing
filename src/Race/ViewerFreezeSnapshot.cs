using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;

namespace CCCPRacing.Race;

/// <summary>Phase 2 spike 2: a state digest of the live scene (named fields, per-node transform hashes, particles, audio) and the diff of two of them.</summary>
sealed class ViewerFreezeSnapshot
{
    public sealed record NodeInfo(string Name, string Class, string Group, ulong Hash);
    public readonly SortedDictionary<string, string> Fields = new(StringComparer.Ordinal);      // sim values, exact ("R") strings
    public readonly SortedDictionary<string, string> Extra = new(StringComparer.Ordinal);       // particles and audio players, one entry each
    public Vector2[] Obs;                                                                       // every collision circle's centre
    public readonly Dictionary<ulong, NodeInfo> Nodes = new();                                  // every Node3D of the scene: transform + visibility hash
    public readonly SortedDictionary<string, ulong> Groups = new(StringComparer.Ordinal);       // XOR of the node hashes under each direct child of the scene

    public static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    public static string F(float v) => v.ToString("R", Inv);
    public static string V(Vector3 v) => $"{F(v.X)},{F(v.Y)},{F(v.Z)}";
    public static ulong Mix(ulong h, float v) => (h ^ (uint)BitConverter.SingleToInt32Bits(v)) * 1099511628211UL;
    public static ulong MixT(ulong h, Transform3D t)
    {
        h = Mix(h, t.Basis.X.X); h = Mix(h, t.Basis.X.Y); h = Mix(h, t.Basis.X.Z);
        h = Mix(h, t.Basis.Y.X); h = Mix(h, t.Basis.Y.Y); h = Mix(h, t.Basis.Y.Z);
        h = Mix(h, t.Basis.Z.X); h = Mix(h, t.Basis.Z.Y); h = Mix(h, t.Basis.Z.Z);
        h = Mix(h, t.Origin.X); h = Mix(h, t.Origin.Y); h = Mix(h, t.Origin.Z);
        return h;
    }

    /// <summary>One digest line per changed/unchanged field. Returns the number of changed entries (fields + extras).</summary>
    public static (List<(string kind, string name, string before, string after, bool changed)> Rows, int NodesMoved, int NodesAdded, int NodesRemoved, List<string> Movers, SortedDictionary<string, int> MovedByGroup)
        Diff(ViewerFreezeSnapshot a, ViewerFreezeSnapshot b)
    {
        var rows = new List<(string, string, string, string, bool)>();
        foreach (var k in a.Fields.Keys.Union(b.Fields.Keys).OrderBy(x => x, StringComparer.Ordinal))
        {
            a.Fields.TryGetValue(k, out var x); b.Fields.TryGetValue(k, out var y);
            rows.Add(("f", k, x ?? "-", y ?? "-", x != y));
        }
        foreach (var k in a.Extra.Keys.Union(b.Extra.Keys).OrderBy(x => x, StringComparer.Ordinal))
        {
            a.Extra.TryGetValue(k, out var x); b.Extra.TryGetValue(k, out var y);
            rows.Add(("x", k, x ?? "-", y ?? "-", x != y));
        }
        int moved = 0, added = 0, removed = 0;
        var movers = new List<string>();
        var byGroup = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var (id, n) in a.Nodes)
        {
            if (!b.Nodes.TryGetValue(id, out var m)) { removed++; continue; }
            if (m.Hash != n.Hash) { moved++; movers.Add($"{n.Group}/{n.Name}:{n.Class}"); byGroup[n.Group] = byGroup.GetValueOrDefault(n.Group) + 1; }
        }
        foreach (var id in b.Nodes.Keys) if (!a.Nodes.ContainsKey(id)) added++;
        return (rows, moved, added, removed, movers, byGroup);
    }
}
