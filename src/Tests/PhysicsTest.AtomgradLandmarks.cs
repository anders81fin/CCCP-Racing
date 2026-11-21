using System;
using System.Collections.Generic;
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

    /// <summary>The power plant's walls have no gaps for the car: the collision is circles, and a long box laid out as a row of circles as wide as it is leaves its corners open (the turbine hall's four corners were: the car drove through them). Every point along the footprints of the turbine hall, the reactor blocks (with their wider annex at the front) and the sarcophagus, a little inside the wall, is inside some obstacle circle.</summary>
    void AtomgradPlantWallsTest()
    {
        var def = TrackDef.Load("res://tracks/atomgrad.json");
        var track = TrackBuilder.Build(def);
        int holes = 0, samples = 0; string first = "";
        foreach (var b in def.Props.Where(p => p.Type is "turbinehall" or "reactor" or "sarcophagus"))
        {
            float rot = Mathf.DegToRad(b.Rot); Vector2 pos = new(b.X, b.Z);
            Vector2 ax = new(MathF.Cos(rot), -MathF.Sin(rot)), az = new(MathF.Sin(rot), MathF.Cos(rot));      // (the model's frame: x along ax, z along az)
            // footprints in the model's frame: (centre x, centre z, width x, depth z), as the models are built (Atomgrad.Plant.cs)
            var rects = b.Type switch
            {
                "turbinehall" => new[] { (0f, 0f, 80f, 22f) },
                "reactor" => new[] { (0f, 0f, 24f, 28f), (0f, 17f, 30f, 8f) },
                _ => new[] { (0f, 0f, 30f, 32f) },
            };
            foreach (var (cx, cz, w, d) in rects)
            {
                const float inset = 0.5f;      // (inside the wall; the solid boxes round their corners with a 1.5 m circle, which leaves a sliver 0.6 m deep at the very corner: not a gap a car fits in)
                for (float u = -w / 2f + inset; u <= w / 2f - inset + 0.001f; u += 0.5f)
                    foreach (float z in new[] { cz - d / 2f + inset, cz + d / 2f - inset }) Probe(pos + ax * (cx + u) + az * z, b.Type, cx + u, z);
                for (float v = -d / 2f + inset; v <= d / 2f - inset + 0.001f; v += 0.5f)
                    foreach (float x in new[] { cx - w / 2f + inset, cx + w / 2f - inset }) Probe(pos + ax * x + az * (cz + v), b.Type, x, cz + v);
            }
        }
        void Probe(Vector2 at, string type, float x, float z)
        {
            samples++;
            if (track.Obstacles.Any(o => o.Center.DistanceTo(at) < o.Radius)) return;
            holes++; if (first.Length == 0) first = $"{type} at model ({x:F1}, {z:F1})";
        }
        Check(holes == 0 && samples > 500, $"atomgrad plant walls: {samples} points just inside the turbine hall, reactor and sarcophagus walls, {holes} not inside any obstacle circle{(holes > 0 ? " (first: " + first + ")" : "")}");
    }

    /// <summary>Every building-sized box the track builder lays out as circles (TrackBuilder.AddBox: the church, izba, hangar, barracks, field tent, barns and the like) is closed all round, at any angle: the points just inside its footprint are inside some circle, and a thin wall (under 3 m wide) is still a plain row.</summary>
    void BuildingBoxWallsTest()
    {
        int holes = 0, samples = 0; string first = "";
        foreach (var (w, d) in new[] { (7f, 17f), (7f, 6f), (18.5f, 22f), (6.2f, 8.8f), (7.2f, 7f), (8f, 13f), (11f, 31f), (22f, 80f), (3.7f, 3f), (10f, 14f) })
        foreach (float rotDeg in new[] { 0f, 30f, 90f, 137f })
        {
            var t = new TrackData();
            float rot = Mathf.DegToRad(rotDeg); Vector2 c = new(10f, -5f);
            TrackBuilder.AddBox(t, c, rot, w, d);
            Vector2 along = new(MathF.Sin(rot), MathF.Cos(rot)), across = new(along.Y, -along.X);
            const float inset = 0.5f;
            void Probe(float u, float v)
            {
                samples++; Vector2 at = c + across * u + along * v;
                if (t.Obstacles.Any(o => o.Center.DistanceTo(at) < o.Radius)) return;
                holes++; if (first.Length == 0) first = $"{w}x{d} at {rotDeg} deg, ({u:F1}, {v:F1})";
            }
            for (float u = -w / 2f + inset; u <= w / 2f - inset + 0.001f; u += 0.5f) { Probe(u, -d / 2f + inset); Probe(u, d / 2f - inset); }
            for (float v = -d / 2f + inset; v <= d / 2f - inset + 0.001f; v += 0.5f) { Probe(-w / 2f + inset, v); Probe(w / 2f - inset, v); }
        }
        var thin = new TrackData(); TrackBuilder.AddBox(thin, Vector2.Zero, 0f, 0.3f, 13f);
        Check(holes == 0 && samples > 400 && thin.Obstacles.All(o => o.Radius < 0.2f), $"building boxes: {samples} points just inside the footprints of ten box sizes at four angles, {holes} not inside any circle{(holes > 0 ? " (first: " + first + ")" : "")}; a thin wall stays a row of small circles ({thin.Obstacles.Count})");
    }

    /// <summary>The decontamination station's yard has colliders for what stands in it (the water tank on legs, the hose reel, the drums, the traffic-light posts): the model's parts' foot points are inside an obstacle circle.</summary>
    void AtomgradWashYardTest()
    {
        var def = TrackDef.Load("res://tracks/atomgrad.json");
        var track = TrackBuilder.Build(def);
        var wash = def.Props.First(p => p.Type == "wash");
        float rot = Mathf.DegToRad(wash.Rot); Vector2 pos = new(wash.X, wash.Z);
        Vector2 ax = new(MathF.Cos(rot), -MathF.Sin(rot)), az = new(MathF.Sin(rot), MathF.Cos(rot));
        var parts = new List<(string name, float x, float z)> { ("tank", -7f, -3f), ("tank leg", -8.2f, -4.2f), ("tank leg", -5.8f, -1.8f), ("hose reel", -4.5f, 3.5f), ("operators' room", 7.2f, 3f), ("light post", -5.2f, 8.9f), ("light post", -5.2f, -8.9f) };
        for (int k = 0; k < 5; k++) parts.Add(("drum", -5.2f + (k % 3) * 0.75f, 6.3f + (k / 3) * 0.8f));
        var open = parts.Where(q => { var at = pos + ax * q.x + az * q.z; return !track.Obstacles.Any(o => o.Center.DistanceTo(at) < o.Radius); }).Select(q => $"{q.name} ({q.x}, {q.z})").ToList();
        Check(open.Count == 0, $"atomgrad wash yard: {parts.Count} parts (tank and its legs, hose reel, drums, light posts, the operators' room) are inside an obstacle circle{(open.Count > 0 ? "; open: " + string.Join(", ", open) : "")}");
    }
}
