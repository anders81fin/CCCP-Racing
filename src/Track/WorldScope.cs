using Godot;

namespace CCCPRacing.Track;

/// <summary>
/// Which world a node belongs to. The replay viewer runs a second RaceScene in a SubViewport with its own World3D beside the live
/// one, so tree-wide lookups (SceneTree signals, groups, FindChild from the root) would reach the other world's nodes: anything
/// that wants "its own world" asks here instead.
/// </summary>
public static class WorldScope
{
    /// <summary>The 3D world the node renders in (the viewport's own, or the one it inherits); null off the tree.</summary>
    public static World3D Of(Node n) => n != null && n.IsInsideTree() ? n.GetViewport()?.FindWorld3D() : null;

    /// <summary>Both nodes are in the tree and render in the same world.</summary>
    public static bool Same(Node a, Node b) => Of(a) is { } wa && wa == Of(b);

    /// <summary>The node called name below the top of a's viewport (its scene), not looking into other viewports; null if none.</summary>
    public static Node Find(Node a, string name)
    {
        Node top = a;
        while (top.GetParent() is { } p && p is not Viewport) top = p;
        return Search(top, name);
    }

    static Node Search(Node n, string name)
    {
        if (n.Name == name) return n;
        foreach (var c in n.GetChildren())
        {
            if (c is Viewport) continue;
            if (Search(c, name) is { } found) return found;
        }
        return null;
    }
}
