using System;
using System.Collections.Generic;
using System.Linq;
using CCCPRacing.Car;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    // --tank-btr-test: the tanks and the BTR-80 patrols of Poligon share the range for ten simulated minutes and never drive through each other (their hull circles never overlap by more than a few
    // centimetres), and a BTR that meets a tank waits for it.
    void TankBtrTest()
    {
        var track = TrackBuilder.Build(TrackDef.Load("res://tracks/bonus.json"));
        for (int d = 0; d < (int.TryParse(OS.GetEnvironment("TB_DUMMY"), out int nd) ? nd : 0); d++) track.Obstacles.Add(new Obstacle(new Vector2(-500f - d, -500f), 1f));      // (a far-away obstacle: how much a change that does not touch the range changes the ten minutes)
        var fx = new TyreEffects(); AddChild(fx); fx.Init(track);
        var tanks = new TankBattle(); AddChild(tanks);
        var tr = track.TankRange;
        tanks.Init(track, fx, new Rect2(tr.X0, tr.Z0, tr.X1 - tr.X0, tr.Z1 - tr.Z0), tr.Tanks);
        var btr = new BtrPatrols(); AddChild(btr); btr.Init(track, fx);
        if (!Array.Exists(OS.GetCmdlineUserArgs(), a => a == "--unwired")) { tanks.Movers = btr.HullCircles; btr.Tanks = tanks.HullCircles; }      // (--unwired shows what the test catches: the old behaviour)
        var t = new List<(Vector2 p, float r)>(); var b = new List<(Vector2 p, float r)>();
        float deepest = -99f; Vector2 where = default; float when = 0f; int close = 0;
        float travelledBtr = 0f; var prev = btr.Positions.Select(p => new Vector2(p.X, p.Z)).ToArray();
        for (int i = 0; i < 60 * 60 * 10; i++)
        {
            tanks.Tick(1f / 60f, null); btr.Tick(1f / 60f);
            t.Clear(); b.Clear(); tanks.HullCircles(t); btr.HullCircles(b);
            foreach (var (tp, tr2) in t) foreach (var (bp, br) in b)
            {
                float pen = tr2 + br - tp.DistanceTo(bp);
                if (pen > deepest) { deepest = pen; where = tp; when = i / 60f; }
                if (pen > 0.3f && OS.HasEnvironment("TB_DBG") && i % 30 == 0) GD.Print($"TBDBG t={i / 60f:F1} tank {tp} r{tr2} btr {bp} r{br} pen {pen:F2}; static near: " + string.Join(" ", track.Obstacles.Where(o => o.Center.DistanceTo(tp) < 14f && o.Center.X > -9000f && o.Radius > 0.5f).Select(o => $"({o.Center.X:F0},{o.Center.Y:F0})r{o.Radius:F1}").Take(14)));
                if (pen > -1f) close++;
            }
            if (i % 60 == 59) { int k = 0; foreach (var p in btr.Positions) { var q = new Vector2(p.X, p.Z); travelledBtr += q.DistanceTo(prev[k]); prev[k++] = q; } }
        }
        GD.Print($"TANK/BTR: deepest hull overlap {deepest:F2} m at {where} t={when:F0}s; {close} close-frames; the BTRs drove {travelledBtr:F0} m");
        Check(deepest < 0.3f, $"no tank drives through a BTR or the other way round (deepest overlap {deepest:F2} m)");
        Check(travelledBtr > 400f, $"the BTRs still patrol ({travelledBtr:F0} m in ten minutes)");
    }
}
