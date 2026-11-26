using System;
using System.Collections.Generic;
using System.Reflection;
using CCCPRacing.Car;
using CCCPRacing.Race;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    /// <summary>What a snowplough's blade throws depends on the ground under it: snow on the road, ice chips on the frozen river, sparks off the bare runway.</summary>
    void PloughSurfaceTest()
    {
        var track = TrackBuilder.Build(TrackDef.Load("res://tracks/siberia.json"));
        // A spot in the middle of each surface: the same surface all round it (the wings are a few metres off the centre).
        (Vector3 at, Vector3 dir)? Spot(SurfaceType want)
        {
            for (int i = 0; i < track.Center.Length - 12; i += 1)
            {
                if (track.CenterSurf[i] != want) continue;
                var c = track.Center[i]; var d2 = (track.Center[i + 10] - c).Normalized(); var side2 = new Vector2(-d2.Y, d2.X);
                bool all = true;
                for (float along = -5f; along <= 11f && all; along += 1f)
                    for (float lat = -4.5f; lat <= 4.5f && all; lat += 1f)
                    {
                        var q = c + d2 * along + side2 * lat;
                        all = track.TileSurface((int)Math.Floor(q.X / track.Tile), (int)Math.Floor(q.Y / track.Tile)) == want;
                    }
                if (all) return (new Vector3(c.X, track.MeshHeight(c.X, c.Y) + 0.3f, c.Y), new Vector3(d2.X, 0, d2.Y));
            }
            return null;
        }
        var snow = Spot(SurfaceType.Snow); var ice = Spot(SurfaceType.Ice); var run = Spot(SurfaceType.Runway);
        Check(snow != null && ice != null && run != null, $"the winter track has road snow, river ice and a runway with room for a plough to throw its spray (snow {snow != null}, ice {ice != null}, runway {run != null})");
        if (snow == null || ice == null || run == null) return;

        var pf = typeof(TyreEffects).GetField("_p", BindingFlags.NonPublic | BindingFlags.Instance);
        var cf = typeof(TyreEffects).GetField("_count", BindingFlags.NonPublic | BindingFlags.Instance);
        // (sparks, ice chips, snow cloud, snow chunks) among the live particles, told apart by size and colour.
        (int sparks, int chips, int cloud, int chunks) Count(TyreEffects fx)
        {
            var arr = (Array)pf.GetValue(fx); int n = (int)cf.GetValue(fx);
            int sp = 0, ch = 0, cl = 0, ck = 0;
            for (int i = 0; i < n; i++)
            {
                object q = arr.GetValue(i);
                float s0 = (float)q.GetType().GetField("Size0").GetValue(q);
                if (Math.Abs(s0 - 0.16f) < 0.001f) sp++;
                else if (Math.Abs(s0 - 0.2f) < 0.001f) ch++;
                else if (Math.Abs(s0 - 1.0f) < 0.001f) cl++;
                else if (Math.Abs(s0 - 0.45f) < 0.001f || Math.Abs(s0 - 0.35f) < 0.001f) ck++;
            }
            return (sp, ch, cl, ck);
        }
        (int sparks, int chips, int cloud, int chunks) Spray((Vector3 at, Vector3 dir) sp)
        {
            var (at, dir) = sp; Vector3 side = new(-dir.Z, 0, dir.X);
            var fx = new TyreEffects(); AddChild(fx); fx.Init(track);
            float accL = 0, accR = 0;
            for (int k = 0; k < 90; k++)       // 1.5 s at 8 m/s
            {
                Vector3 p = at + dir * (8f * k / 60f);
                fx.PloughSpray(p - side * 2f, -side, dir * 8f, 8f, ref accL, 1f / 60f);
                fx.PloughSpray(p + side * 2f, side, dir * 8f, 8f, ref accR, 1f / 60f);
            }
            var c = Count(fx); fx.QueueFree(); return c;
        }
        var cs = Spray(snow.Value); var ci = Spray(ice.Value); var cr = Spray(run.Value);
        Check(cs.cloud > 5 && cs.chunks > 20 && cs.sparks == 0 && cs.chips == 0, $"plough on road snow: the usual snow cloud and chunks, no sparks or ice ({cs})");
        Check(ci.chips > 20 && ci.sparks == 0 && ci.cloud == 0 && ci.chunks == 0, $"plough on the frozen river: hard ice chips, no snow cloud or sparks ({ci})");
        Check(cr.sparks > 20 && cr.cloud == 0 && cr.sparks > cr.chunks * 3, $"plough on the bare runway: sparks, hardly any snow ({cr})");

        // The world tape's emitter makes the same thing from a covered plough's pose.
        (int sparks, int chips, int cloud, int chunks) Taped((Vector3 at, Vector3 dir) sp)
        {
            var (at, dir) = sp;
            var fx = new TyreEffects(); AddChild(fx); fx.Init(track);
            var em = new TapeEmitters(fx, track);
            var node = new Node3D(); AddChild(node); node.Position = at;
            em.Add(node, TapeEmitters.Kind.Plough);
            for (int k = 0; k < 90; k++) { node.Position = at + dir * (8f * k / 60f); node.LookAt(node.Position + dir, Vector3.Up); em.Tick(1f / 60f); }
            var c = Count(fx); node.QueueFree(); fx.QueueFree(); return c;
        }
        var ts = Taped(snow.Value); var ti = Taped(ice.Value); var tr = Taped(run.Value);
        Check(ts.cloud + ts.chunks > 20 && ts.sparks == 0 && ts.chips == 0, $"the replay's plough on road snow throws snow ({ts})");
        Check(ti.chips > 10 && ti.cloud == 0 && ti.sparks == 0, $"the replay's plough on the ice throws ice chips ({ti})");
        Check(tr.sparks > 10 && tr.cloud == 0, $"the replay's plough on the runway throws sparks ({tr})");
    }
}
