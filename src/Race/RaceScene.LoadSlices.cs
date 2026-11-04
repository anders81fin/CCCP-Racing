using Godot;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CCCPRacing.Race;

public partial class RaceScene
{
    /// <summary>
    /// Drains a step builder (null items are checkpoints, the last non-null item is the result) and offers a loading frame at each
    /// checkpoint while the world builds in slices. Call sites: BuildWorld. A builder holding a detached node across its checkpoints
    /// frees it itself when the build is abandoned (a cancelled replay viewer), so nothing here needs to hold it.
    /// </summary>
    async Task<T> BuildSliced<T>(IEnumerable<T> steps, [System.Runtime.CompilerServices.CallerLineNumber] int profLine = 0) where T : class
    {
        T result = null;
        foreach (var step in steps)
        {
            if (step != null) result = step;
            else if (_booting) await LoadingFrame(profLine);
        }
        return result;
    }

    /// <summary>Nodes in a subtree that one slice of the viewer's tree entry may take (entering the tree costs about 1 ms per mesh node on a windowed renderer).</summary>
    const int AddSliceNodes = 12;

    /// <summary>True when the subtree (the node included) holds fewer nodes than the limit; stops counting at the limit.</summary>
    static bool SmallSubtree(Node n, ref int budget)
    {
        if (--budget < 0) return false;
        foreach (var c in n.GetChildren()) if (!SmallSubtree(c, ref budget)) return false;
        return true;
    }

    /// <summary>
    /// AddChild of a finished subtree, a few dozen nodes at a time with a loading checkpoint after each (a replay viewer only: entering the tree of thousands
    /// of nodes at once was a single frame of 0.2-1.6 s). Only plain Node3D groups are opened up (no script, so nothing reads its children when it becomes
    /// ready); every other node, and every small subtree, enters whole. The children enter in their own order under the same parents, so the tree ends up
    /// identical (a SeeThrough, the last child of its building, still finds all its siblings). A build that fails halfway frees the children it still holds.
    /// The normal game's load adds the subtree in one go as before.
    /// </summary>
    async Task AddSliced(Node parent, Node child)
    {
        int budget = AddSliceNodes;
        if (!ViewerMode || !_booting || child.GetType() != typeof(Node3D) || SmallSubtree(child, ref budget))
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            parent.AddChild(child);
            if (LoadProf.On && (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000 / System.Diagnostics.Stopwatch.Frequency >= 40)
                GD.Print($"LOADPROF addchild {child.Name} ({child.GetType().Name}, {child.FindChildren("*", "", true, false).Count} nodes, parent {parent.Name}): {(System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000 / System.Diagnostics.Stopwatch.Frequency} ms");
            return;
        }
        var kids = child.GetChildren();
        foreach (var k in kids) child.RemoveChild(k);
        parent.AddChild(child);
        int added = 0;
        try
        {
            foreach (var k in kids)
            {
                await AddSliced(child, k);
                added++;
                await LoadingFrame();
            }
        }
        finally
        {
            for (int j = added; j < kids.Count; j++) if (IsInstanceValid(kids[j]) && kids[j].GetParent() == null) kids[j].Free();
        }
    }
}
