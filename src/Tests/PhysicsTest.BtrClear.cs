using System;
using System.Linq;
using CCCPRacing.Car;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    // --btr-clear-test: the BTR-80 patrols of Poligon never drive through anything fixed (trees, posts, wooden and stone fences): the clearance of their hull circles to every fixed obstacle over three simulated minutes.
    void BtrClearTest()
    {
        var track = TrackBuilder.Build(TrackDef.Load("res://tracks/bonus.json"));
        var fx = new TyreEffects(); AddChild(fx); fx.Init(track);
        int fixedCount = track.Obstacles.Count;
        var fixedObs = new (Vector2 c, float r, ImpactMaterial m)[fixedCount];
        for (int i = 0; i < fixedCount; i++) fixedObs[i] = (track.Obstacles[i].Center, track.Obstacles[i].Radius, track.Obstacles[i].Material);
        var btr = new BtrPatrols(); AddChild(btr); btr.Init(track, fx);
        int slots = track.Obstacles.Count - fixedCount;
        foreach (var route in btr.Routes) GD.Print("  BTR route x " + route.Min(p => p.X).ToString("F0") + "-" + route.Max(p => p.X).ToString("F0") + " z " + route.Min(p => p.Y).ToString("F0") + "-" + route.Max(p => p.Y).ToString("F0") + ", " + route.Length + " samples");
        foreach (var route in btr.Routes)
        {
            int bad = 0; Vector2 sample = default, who = default;
            foreach (var p in route) foreach (var (c, r, _) in fixedObs) if (c.X > -9000f && p.DistanceTo(c) < r + 3.1f - 0.01f) { bad++; sample = p; who = c; }
            GD.Print("  route samples inside Fit's own margin of an obstacle: " + bad + (bad > 0 ? " e.g. sample " + sample + " obstacle " + who : ""));
        }
        { int n = 0; foreach (var rt in btr.Routes) GD.Print("  route " + (n++) + " first samples " + string.Join(" ", rt.Take(4)) + " ... " + rt[rt.Length / 2]); GD.Print("  BTR positions at t=0: " + string.Join(" ", btr.Positions)); }
        for (int pi = 0; pi < btr.Count; pi++)
        {
            var path = btr.PathSamples(pi, 0.5f).ToArray(); var dense = btr.Routes.ElementAt(pi);
            float away = path.Max(q => dense.Min(d => d.DistanceTo(q.at)));
            int hits = 0; Vector2 firstAt = default, firstObs = default;
            foreach (var (at, f) in path) for (int hh = -1; hh <= 1; hh++)
            {
                Vector2 q = at + f * (hh * 2.2f);
                foreach (var (c, r, _) in fixedObs) if (c.X > -9000f && r > 0.1f && q.DistanceTo(c) < r + 1.45f - 0.1f) { if (hits++ == 0) { firstAt = q; firstObs = c; } }
            }
            GD.Print("  patrol " + pi + ": the driven path is at most " + away.ToString("F2") + " m from its dense samples; " + hits + " hull-circle positions inside an obstacle" + (hits > 0 ? " e.g. at " + firstAt + " in " + firstObs : ""));
        }
        float worst = 99f; Vector2 where = default, what = default; ImpactMaterial material = default; float whenT = 0;
        for (int i = 0; i < 60 * 180; i++)
        {
            btr.Tick(1f / 60f);
            for (int s = fixedCount; s < fixedCount + slots; s++)
            {
                var h = track.Obstacles[s];
                if (h.Center.X < -9000f) continue;
                foreach (var (c, r, m) in fixedObs)
                {
                    if (c.X < -9000f || r < 0.1f) continue;
                    float gap = h.Center.DistanceTo(c) - h.Radius - r;
                    if (gap < worst)
                    {
                        if (gap < -1f && worst >= -1f)
                        {
                            GD.Print("  first deep contact t=" + (i / 60f).ToString("F1") + " slot " + (s - fixedCount) + " hull " + h.Center + " r " + h.Radius + " obstacle " + c + " r " + r + " material " + m + " BTRs " + string.Join(" ", btr.Positions));
                            GD.Print("  nearest dense sample to the hull circle: " + btr.Routes.Min(rt => rt.Min(q => q.DistanceTo(h.Center))).ToString("F2") + " m; to the BTR centres: " + string.Join(" ", btr.Positions.Select(b => btr.Routes.Min(rt => rt.Min(q => q.DistanceTo(new Vector2(b.X, b.Z)))).ToString("F2"))));
                            GD.Print("  nearest dense sample to that obstacle: " + btr.Routes.Min(rt => rt.Min(q => q.DistanceTo(c))).ToString("F2") + " m");
                        }
                        worst = gap; where = h.Center; what = c; material = m; whenT = i / 60f;
                    }
                }
            }
        }
        GD.Print($"BTR CLEAR: closest gap {worst:F2} m at {where} to the obstacle at {what} ({material}) at t={whenT:F1}s; {slots} hull circles");
        Check(worst > -0.2f, $"the BTR patrols never drive into a fixed obstacle (closest gap {worst:F2} m)");
        btr.Free(); fx.Free();
    }
}
