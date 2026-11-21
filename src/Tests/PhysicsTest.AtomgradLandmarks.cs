using System;
using System.Linq;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    /// <summary>Two Atomgrad apartment blocks have a landmark of their own (a stair tower on one end, an entrance canopy on the street side): both are built, their solid parts are obstacles in the right places, and none of it reaches the road.</summary>
    void AtomgradLandmarkTest()
    {
        var def = TrackDef.Load("res://tracks/atomgrad.json");
        var track = TrackBuilder.Build(def);
        var blocks = def.Props.Where(p => p.Type == "apartments").ToList();
        var stair = blocks.Where(p => (p.Text ?? "").Split(';').ElementAtOrDefault(2)?.Trim() == "stair").ToList();
        var canopy = blocks.Where(p => (p.Text ?? "").Split(';').ElementAtOrDefault(2)?.Trim() == "canopy").ToList();
        bool distinct = blocks.Count == 5 && stair.Count == 1 && canopy.Count == 1 && stair[0] != canopy[0];
        // The colliders sit where the model's parts are (the block's frame: x along it, z across it, yaw rot) and keep clear of the road's edge by a car's width.
        float worstClear = float.MaxValue; int found = 0, expected = 0; bool nodes = true;
        foreach (var b in new[] { stair[0], canopy[0] })
        {
            float rot = Mathf.DegToRad(b.Rot); Vector2 pos = new(b.X, b.Z);
            Vector2 ax = new(MathF.Cos(rot), -MathF.Sin(rot)), az = new(MathF.Sin(rot), MathF.Cos(rot));
            foreach (var (x, z, r) in Atomgrad.ApartmentFeatureCircles(b.Text))
            {
                expected++;
                Vector2 at = pos + ax * x + az * z;
                if (track.Obstacles.Any(o => o.Center.DistanceTo(at) < 0.01f && MathF.Abs(o.Radius - r) < 0.01f)) found++;
                int ci = track.NearestCenter(at, out float dist);
                worstClear = MathF.Min(worstClear, dist - r - track.HalfWidth[ci]);
            }
            var model = Atomgrad.Apartments(b.Text);
            nodes &= model.GetChildCount() > 60;
            model.QueueFree();
        }
        Check(distinct && expected == 5 && found == expected && worstClear > 2f && nodes,
            $"atomgrad landmarks: one block with a stair end, one with an entrance canopy (distinct {distinct}); {found}/{expected} colliders are obstacles, the nearest is {worstClear:F1} m clear of the road's edge");
    }
}
