using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    void BuildingVisibilityTest()
    {
        GD.Print("Building visibility: only geometry between camera and car can fade");
        var box = new BoxMesh { Size = new(2, 2, 2) };
        var solid = new BuildingOcclusion();
        solid.Add(box, Transform3D.Identity);
        Check(solid.Blocks(new(0, 0, 4), new(0, 0, -4)), "building between camera and car blocks the segment");
        Check(!solid.Blocks(new(0, 0, 4), new(0, 0, 2)), "building beyond the car does not fade");
        Check(!solid.Blocks(new(0, 0, 2), new(0, 0, 4)), "building behind the camera does not fade");
        Check(!solid.Blocks(new(2, 0, 4), new(2, 0, -4)), "view beside the building stays opaque");
        Check(solid.Blocks(new(0, 0, 4), Vector3.Zero), "a car inside a building still needs the obstructing walls faded");

        var open = new BuildingOcclusion();
        open.Add(box, new(Basis.Identity, new(-3, 0, 0)));
        open.Add(box, new(Basis.Identity, new(3, 0, 0)));
        Check(!open.Blocks(new(0, 0, 4), new(0, 0, -4)), "open space between meshes does not trigger a bounding-box false positive");
        // A rotated slender mesh has empty corners inside its axis-aligned bounds.
        var diagonal = new BuildingOcclusion();
        diagonal.Add(new BoxMesh { Size = new(4, 2, 0.2f) }, new(new Basis(Vector3.Up, Mathf.Pi / 4), Vector3.Zero));
        Check(!diagonal.Blocks(new(1.3f, 3, 1.3f), new(1.3f, -3, 1.3f)), "empty corner of a rotated mesh's bounds stays opaque");

        var roof = new BuildingOcclusion();
        roof.Add(new BoxMesh { Size = new(8, 0.2f, 12) }, new(Basis.Identity, new(0, 5, 0)));
        Check(!roof.Blocks(new(0, 2, 10), new(0, 1, 0)), "a car under a roof does not fade it when viewed through the open end");
        Check(roof.Blocks(new(0, 10, 3), new(0, 1, 0)), "the same roof fades when it actually hides the car from above");
    }
}
