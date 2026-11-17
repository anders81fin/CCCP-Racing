using System;
using System.Collections.Generic;
using Godot;

namespace CCCPRacing.Track;

/// <summary>
/// The village's water tower ("watertower" prop): a square brick shaft with a pale lintel course, an arched door and window slits, a platform and a wooden tank with iron hoops under a
/// green conical roof, a pipe and ladder down the side. Local frame: the tower at the origin.
/// </summary>
public static class WaterTower
{
    static readonly Color Brick = new(0.55f, 0.27f, 0.2f), Lintel = new(0.78f, 0.74f, 0.64f), Dark = new(0.07f, 0.07f, 0.08f), Timber = new(0.34f, 0.24f, 0.15f);
    static readonly Color Iron = new(0.25f, 0.17f, 0.12f), Cap = new(0.24f, 0.38f, 0.3f), Stone = new(0.5f, 0.49f, 0.45f);

    public static IEnumerable<(float x, float z, float r)> Circles()
    {
        yield return (0f, 0f, 1.9f);
    }

    public static Node3D Build()
    {
        var n = new Node3D();
        // The shaft: laid brick, course by course, with a door and slit windows left open, a pale lintel course and a plinth.
        var bk = new Vector3(-1.4f, 0, -1.4f);
        BuildKit.Masonry(n, new Vector3(-1.4f, 0, 1.4f), true, 2.8f, 5.4f, 0.32f, 0.22f, 0.22f, 0.36f, Brick, 61, 0.1f, new BuildKit.Hole(0.95f, 1.85f, 0f, 1.95f), new BuildKit.Hole(0.6f, 0.9f, 2.9f, 3.7f), new BuildKit.Hole(1.9f, 2.2f, 2.9f, 3.7f), new BuildKit.Hole(0.6f, 0.9f, 4.2f, 5.0f), new BuildKit.Hole(1.9f, 2.2f, 4.2f, 5.0f));
        BuildKit.Masonry(n, new Vector3(-1.4f, 0, -1.4f), true, 2.8f, 5.4f, 0.32f, 0.22f, 0.22f, 0.36f, Brick, 62, 0.1f, new BuildKit.Hole(1.1f, 1.7f, 3.0f, 3.9f));
        BuildKit.Masonry(n, new Vector3(-1.4f, 0, -1.4f), false, 2.8f, 5.4f, 0.32f, 0.22f, 0.22f, 0.36f, Brick, 63, 0.1f, new BuildKit.Hole(1.1f, 1.7f, 3.0f, 3.9f));
        BuildKit.Masonry(n, new Vector3(1.4f, 0, -1.4f), false, 2.8f, 5.4f, 0.32f, 0.22f, 0.22f, 0.36f, Brick, 64, 0.1f, new BuildKit.Hole(1.1f, 1.7f, 3.0f, 3.9f));
        n.AddChild(Props.Part(Props.Box(2.6f, 5.2f, 2.6f), Brick.Darkened(0.35f), new Vector3(0, 2.6f, 0)));            // the core behind the courses (no light through the joints)
        n.AddChild(Props.Part(Props.Box(1.0f, 2.0f, 0.1f), Dark, new Vector3(0, 1.0f, 1.2f)));
        foreach (float y in new[] { 0.28f, 5.35f }) n.AddChild(Props.Part(Props.Box(3.1f, 0.3f, 3.1f), Lintel, new Vector3(0, y, 0)));
        foreach (float y in new[] { 2.2f, 3.9f }) n.AddChild(Props.Part(Props.Box(3.0f, 0.1f, 3.0f), Lintel, new Vector3(0, y, 0)));     // string courses
        foreach (float sx in new[] { -1f, 1f }) foreach (float sz in new[] { -1f, 1f }) for (int k = 0; k < 14; k++)                // pale corner quoins
            n.AddChild(Props.Part(Props.Box(k % 2 == 0 ? 0.55f : 0.35f, 0.2f, k % 2 == 0 ? 0.35f : 0.55f), Lintel, new Vector3(sx * 1.42f, 0.33f + k * 0.37f, sz * 1.42f)));
        n.AddChild(Props.Part(Props.Box(1.1f, 0.26f, 0.2f), Lintel, new Vector3(0, 1.98f, 1.5f)));              // door lintel
        n.AddChild(Props.Part(Props.Box(0.86f, 1.9f, 0.08f), Timber, new Vector3(0, 0.95f, 1.36f)));            // plank door
        for (float x = -0.35f; x <= 0.36f; x += 0.14f) n.AddChild(Props.Part(Props.Box(0.025f, 1.85f, 0.03f), Dark, new Vector3(x, 0.95f, 1.41f)));
        n.AddChild(Props.Part(Props.Box(0.88f, 0.07f, 0.04f), Iron, new Vector3(0, 0.5f, 1.43f)));
        n.AddChild(Props.Part(Props.Box(0.88f, 0.07f, 0.04f), Iron, new Vector3(0, 1.5f, 1.43f)));
        n.AddChild(Props.Part(Props.Box(1.3f, 0.16f, 0.7f), Lintel, new Vector3(0, 0.08f, 1.8f)));
        foreach (float y in new[] { 3.3f, 4.6f }) foreach (float sx in new[] { -1f, 1f })               // slit windows with stone sills
            n.AddChild(Props.Part(Props.Box(0.5f, 0.1f, 0.12f), Lintel, new Vector3(sx * 0.65f, y - 0.45f, 1.47f)));
        // The platform and the tank.
        n.AddChild(Props.Part(Props.Box(3.6f, 0.3f, 3.6f), Timber, new Vector3(0, 5.65f, 0)));
        n.AddChild(Props.Part(Props.Cyl(1.55f, 2.7f, 14), Timber, new Vector3(0, 7.15f, 0)));
        foreach (float y in new[] { 6.2f, 7.15f, 8.1f }) n.AddChild(Props.Part(Props.Cyl(1.6f, 0.1f, 14), Iron, new Vector3(0, y, 0)));
        n.AddChild(Props.Part(Props.Cyl(1.9f, 1.2f, 14, 0.12f), Cap, new Vector3(0, 9.1f, 0)));
        n.AddChild(Props.Part(Props.Cyl(0.1f, 0.5f, 6), Iron, new Vector3(0, 9.95f, 0)));
        for (float y = 0.9f; y <= 5.1f; y += 0.55f) n.AddChild(Props.Part(Props.Box(0.5f, 0.07f, 0.1f), Iron, new Vector3(1.15f, y, 1.5f)));     // the ladder's rungs
        foreach (float x in new[] { 0.92f, 1.38f }) n.AddChild(Props.Part(Props.Box(0.07f, 4.8f, 0.1f), Iron, new Vector3(x, 3.0f, 1.5f)));
        n.AddChild(Props.Part(Props.Cyl(0.1f, 5.6f, 6), Iron, new Vector3(-1.55f, 2.8f, 0.5f)));                 // the pipe down the side
        n.AddChild(Props.Part(Props.Box(0.4f, 0.4f, 0.4f), Iron, new Vector3(-1.55f, 0.6f, 0.9f)));              // the valve and its tap
        n.AddChild(Props.Part(Props.Cyl(0.05f, 0.5f, 5), Iron, new Vector3(-1.55f, 0.45f, 1.25f), new Vector3(90, 0, 0)));
        DrunkTractor.BatchStatic(n, null);
        return n;
    }
}
