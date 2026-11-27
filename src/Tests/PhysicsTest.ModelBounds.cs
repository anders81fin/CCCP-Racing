using System;
using System.Collections.Generic;
using System.Linq;
using CCCPRacing.Car;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    /// <summary>The union of the mesh bounds under a node, in the node's own frame (the node's transform is not applied, its children's are): the model's real vertical extent.</summary>
    static Aabb ModelBounds(Node3D root)
    {
        Aabb box = default; bool any = false;
        void Walk(Node n, Transform3D xf)
        {
            Transform3D here = xf;
            if (n is Node3D n3 && n != root) here = xf * n3.Transform;
            if (n is MeshInstance3D mi && mi.Mesh != null && mi.Visible)
            {
                var b = here * mi.Mesh.GetAabb();
                box = any ? box.Merge(b) : b; any = true;
            }
            foreach (var c in n.GetChildren()) Walk(c, here);
        }
        Walk(root, Transform3D.Identity);
        return box;
    }
}
