using System;
using CCCPRacing.Car;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    // --tractor-block-test: the ploughing tractor (body and plough circles) must never drive into the track's fixed obstacles; counts overlapping frames over 30 simulated minutes.
    void TractorBlockTest()
    {
        var t = TrackBuilder.Build(TrackDef.Load("res://tracks/ilyich.json"));
        var fx = new TyreEffects(); AddChild(fx); fx.Init(t);
        var tractor = new DrunkTractor(); AddChild(tractor);
        int fixedCount = t.Obstacles.Count;
        var fixedObs = new (Vector2 c, float r)[fixedCount]; for (int i = 0; i < fixedCount; i++) fixedObs[i] = (t.Obstacles[i].Center, t.Obstacles[i].Radius);
        tractor.Init(t, fx, new Vector2(142, 36), MathF.PI);
        var doors = new BarnDoors(5, 5, 5, Colors.Brown, Colors.Gray); AddChild(doors); tractor.ConfigureDeparture(doors);
        bool prevHit = false; int dbg = 0; int frames = 0, hits = 0; float deepest = 0; Vector2 where = default; int stuck = 0, jumps = 0; Vector2 prev = tractor.Position2D; float moved = 0;
        for (int i = 0; i < 60 * 60 * 30; i++)
        {
            tractor.Tick(1f / 60f, true); frames++; if (!tractor.HasDeparted) { prev = tractor.Position2D; continue; }
            Vector2 body = t.Obstacles[tractor.BodyObstacle].Center, plough = t.Obstacles[tractor.PloughObstacle].Center; bool hit = false; int hk = -1;
            for (int k = 0; k < fixedCount; k++)
            {
                float pb = fixedObs[k].r + 1.3f - body.DistanceTo(fixedObs[k].c), pp = fixedObs[k].r + 1.1f - plough.DistanceTo(fixedObs[k].c), p = MathF.Max(pb, pp);
                if (p > 0.05f) { hit = true; hk = k; if (p > deepest) { deepest = p; where = fixedObs[k].c; } }
            }
            if (hit && !prevHit && dbg++ < 4) GD.Print($"  ENTER t={i / 60f:F2} pos={tractor.Position2D} yaw={tractor.Forward2D} speed={tractor.Speed:F2} prevPos={prev} obstacle={fixedObs[hk].c} r={fixedObs[hk].r}"); prevHit = hit;
            if (hit) { hits++; if (hits % 4000 == 1) GD.Print($"  hit at t={i / 60f:F1}s body={body} plough={plough} speed={tractor.Speed:F2} obstacle={fixedObs[hk].c} r={fixedObs[hk].r} deepest-so-far={deepest:F2} at {where}"); }
            float step = tractor.Position2D.DistanceTo(prev);
            if (step > 0.5f) { jumps++; if (jumps <= 8) GD.Print($"  JUMP t={i / 60f:F1}s: {step:F1} m in one tick, from {prev} to {tractor.Position2D}, speed {tractor.Speed:F2}"); }
            moved += step; prev = tractor.Position2D;
            if (i % 3600 == 3599) { if (moved < 5f) { stuck++; if (stuck == 1) foreach (var (c, r) in fixedObs) if (c.DistanceTo(tractor.Position2D) < 9f) GD.Print($"    near obstacle {c} r={r:F2} gap to the tractor {c.DistanceTo(tractor.Position2D) - r:F2} m"); if (stuck <= 6) GD.Print($"  minute {i / 3600}: at {tractor.Position2D} fwd {tractor.Forward2D} speed {tractor.Speed:F2} departed={tractor.HasDeparted}"); if (stuck == 1) for (int k = 0; k < fixedCount; k++) if (fixedObs[k].c.DistanceTo(tractor.Position2D) < 9f) GD.Print($"     obs {fixedObs[k].c} r={fixedObs[k].r}"); } moved = 0; }
        }
        GD.Print($"TRACTOR BLOCK: {hits} of {frames} frames overlap a fixed obstacle, deepest {deepest:F2} m at {where}; minutes with <5 m of movement: {stuck} of 30");
        GD.Print($"TRACTOR JUMPS: {jumps} ticks with a step over 0.5 m");
        Check(jumps == 0, "the ploughing tractor never jumps: no tick moves it more than half a metre");
        Check(hits == 0, "the ploughing tractor never overlaps a fixed obstacle");
        Check(stuck <= 3, "the ploughing tractor keeps moving (no minute stuck against an obstacle)");
        RemoveChild(tractor); tractor.Free(); RemoveChild(fx); fx.Free(); RemoveChild(doors); doors.Free();
    }
}
