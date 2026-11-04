using System;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    void KolkhozWorkersTest()
    {
        GD.Print("Kolkhoz crews: visible work sites without blocking the road or entrances");
        // Check the visible action over a complete cycle, including the formerly static props.
        foreach (var job in new[] { KolkhozJob.Reap, KolkhozJob.Pitchfork, KolkhozJob.CarrySack, KolkhozJob.Load, KolkhozJob.Foreman, KolkhozJob.Rest, KolkhozJob.Fish })
        {
            var model = KolkhozWorkerModel.Build(job, 0);
            var body = model.GetNode<Node3D>("Body");
            var arm = body.GetNode<Node3D>(job == KolkhozJob.Rest ? "LeftArm" : "RightArm");
            var work = body.GetNode<Node3D>("HeldWork");
            Vector3 low = new(float.MaxValue, float.MaxValue, float.MaxValue), high = -low;
            bool fishingJoined = true;
            for (float clock = 0; clock < 8; clock += 0.1f)
            {
                model.Animate(clock);
                Vector3 at = job is KolkhozJob.Pitchfork or KolkhozJob.Load or KolkhozJob.CarrySack
                    ? body.Transform * (work.Transform * new Vector3(0, 1, -0.4f))
                    : body.Transform * (arm.Transform * new Vector3(0, -0.65f, 0));
                low = new(Math.Min(low.X, at.X), Math.Min(low.Y, at.Y), Math.Min(low.Z, at.Z));
                high = new(Math.Max(high.X, at.X), Math.Max(high.Y, at.Y), Math.Max(high.Z, at.Z));
                if (job == KolkhozJob.Fish)
                {
                    var rod = model.GetNode<Node3D>("FishingRod");
                    var line = model.GetNode<Node3D>("FishingLine");
                    fishingJoined &= (rod.Transform * Vector3.Up).DistanceTo(line.Position) < 0.001f;
                    fishingJoined &= (line.Transform * Vector3.Up).DistanceTo(new(0.05f, 0.045f, -3.5f)) < 0.001f;
                }
            }
            Check((high - low).Length() > 0.25f && model.Position == Vector3.Zero,
                $"{job}: visible hand/tool action while the feet remain at the collision position");
            if (job == KolkhozJob.Fish) Check(fishingJoined, "moving rod stays attached to its line and the float stays on the water");
            model.Free();
        }
        var t = TrackBuilder.Build(TrackDef.Load("res://tracks/ilyich.json"));
        var workers = KolkhozWorkers.Layout(t);
        Check(workers.Count >= 23 && workers.Count <= 28, "23-28 workers form the field, loading and fishing crews");
        int fishermen = 0;
        bool floatsInWater = true, fishingRoadClear = true;
        bool upstream = false, downstream = false;
        Vector2 ford = t.Center[t.FordIndex];
        bool roadClear = true, dry = true, space = true, collision = true, gateClear = true;
        Vector2[] loadingPath = { new(46.2f, 183.8f), new(47.1f, 185.4f), new(48.1f, 187.2f), new(49, 189.2f), new(49, 191.2f) };
        foreach (var w in workers)
        {
            if (w.Job == KolkhozJob.Fish)
            {
                fishermen++;
                int roadIndex = t.NearestCenter(w.Position, out float roadDistance);
                fishingRoadClear &= roadDistance >= t.HalfWidth[roadIndex] + 10f;
                float alongRiver = (w.Position - ford).Dot(new Vector2(1, 1).Normalized());
                upstream |= alongRiver < -15f;
                downstream |= alongRiver > 15f;
                var floatAt = w.Position + new Vector2(-MathF.Sin(w.Yaw), -MathF.Cos(w.Yaw)) * (3.5f * KolkhozWorkers.SizeScale);
                floatsInWater &= t.TileSurface((int)(floatAt.X / t.Tile), (int)(floatAt.Y / t.Tile)) is SurfaceType.Water or SurfaceType.Shallow;
            }
            int ci = t.NearestCenter(w.Position, out float d);
            if (d < t.HalfWidth[ci] + 1.5f)
            {
                GD.Print($"  worker too close to road: {w.Position} ({d - t.HalfWidth[ci]:F2} m)");
                roadClear = false;
            }
            dry &= t.TileSurface((int)(w.Position.X / t.Tile), (int)(w.Position.Y / t.Tile))
                is not (SurfaceType.Water or SurfaceType.Shallow or SurfaceType.Ice);
            gateClear &= !Village.NearAccessPath(w.Position, loadingPath, 1.6f);
            bool found = false;
            foreach (var o in t.Obstacles)
            {
                float dist = o.Center.DistanceTo(w.Position);
                if (dist < 0.001f && MathF.Abs(o.Radius - KolkhozWorkers.BodyRadius) < 0.001f) { found = true; continue; }
                if (dist < o.Radius + KolkhozWorkers.BodyRadius + 0.05f)
                {
                    GD.Print($"  worker {w.Position} overlaps obstacle {o.Center} (r={o.Radius:F2})");
                    space = false;
                }
            }
            collision &= found;
        }
        var fieldWorkers = new System.Collections.Generic.List<KolkhozWorkerSpec>(KolkhozWorkers.FieldCrew(t));
        bool fieldSpacing = true, insideFields = true;
        for (int i = 0; i < fieldWorkers.Count; i++)
        {
            var at = fieldWorkers[i].Position;
            insideFields &= t.Sample(at.X, at.Y).Surface == SurfaceType.Field;
            for (int j = i + 1; j < fieldWorkers.Count; j++)
                fieldSpacing &= at.DistanceTo(fieldWorkers[j].Position) >= 8f;
        }
        Check(fieldSpacing && insideFields, "field workers stand inside fields, with at least eight metres between them");
        Check(fishermen == 4 && floatsInWater, "four fishermen stand on the bank with their floats in the river");
        Check(fishingRoadClear && upstream && downstream, "fishermen are upstream and downstream of the ford, at least 10 m from the road edge");
        Check(roadClear && dry, "crews stand on dry ground at least 1.5 m outside the driving surface");
        Check(space, "workers stand clear of buildings, fences, hay bales, trees and each other");
        Check(collision, "each worker has a body collision circle matching the stationary work pose");
        Check(gateClear, "the church northwest loading gate and its path remain passable");
        Check(KolkhozWorkers.Layout(new TrackData { Id = "siberia" }).Count == 0, "work crews are limited to the Ilyich kolkhoz");
    }
}
