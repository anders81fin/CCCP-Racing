using System;
using CCCPRacing.Car;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    // --busstop-leave-test: a car standing on the road or verge in front of the Ilyich bus stop (where the mechanic repairs) can drive off: forward, or with full lock either way, or in reverse.
    void BusStopLeaveTest()
    {
        var t = TrackBuilder.Build(TrackDef.Load("res://tracks/ilyich.json"));
        var stop = t.BusStops[0]; int cn = t.Center.Length; int bi = stop.Index;
        var fx = new TyreEffects(); fx.Init(t); AddChild(fx);
        bool withBus = Array.IndexOf(OS.GetCmdlineUserArgs(), "--with-bus") >= 0;
        if (withBus)
        {
            var bus = new SovietBus(); AddChild(bus); bus.Init(t, fx, 0, null, t.Obstacles.Count); bus.Warp((bi + 22) % cn);
            for (int k = 0; k < 60 * 40; k++) { bus.Tick(1f / 60, null); if (k > 120 && bus.Speed < 0.05f && bus.Position2D.DistanceTo(stop.Position) < 14f) break; }
            GD.Print($"bus standing at {bus.Position2D} (stop {stop.Position}) fwd {bus.Forward2D}");
        }
        GD.Print($"stop at {stop.Position}, road half width {t.HalfWidth[bi]}, shelter lateral {(stop.Position - t.Center[bi]).Length():F1}");
        int tried = 0, stuckAll = 0, stuckForward = 0;
        for (float along = -10; along <= 10; along += 2.5f)
        for (float lat = -3f; lat <= 6f; lat += 1.5f)
        for (int dirSign = -1; dirSign <= 1; dirSign += 2)
        {
            int i = ((bi + (int)along) % cn + cn) % cn; Vector2 d = t.CenterDir(i), right = new(-d.Y, d.X);
            Vector2 p = t.Center[i] + right * (stop.Position - t.Center[bi]).Dot(new Vector2(-t.CenterDir(bi).Y, t.CenterDir(bi).X)).CompareTo(0) * lat;
            bool overlapping = false; foreach (var o in t.Obstacles) if (o.Center.DistanceTo(p) < o.Radius + 1.5f) overlapping = true;
            if (overlapping) continue;
            tried++;
            float yaw = MathF.Atan2(-d.X * dirSign, -d.Y * dirSign);
            bool Try(float steer, bool reverse)
            {
                var car = new CarSim(new CarParams(), t) { WorldMin = -1e6f, WorldMax = 1e6f, Obstacles = t.Obstacles };
                car.Reset(new Vector3(p.X, t.MeshHeight(p.X, p.Y) + 0.8f, p.Y), yaw);
                Run(car, 0.5f);
                var start = car.Pos;
                Run(car, 6f, () => car.SetControls(!reverse, reverse, steer, false));
                return car.Pos.DistanceTo(start) > 9f;
            }
            bool fwd = Try(0f, false);
            if (!fwd) { stuckForward++; GD.Print($"  no straight exit at along {along}, lat {lat}, dir {dirSign}, pos {p}"); if (!(Try(0.6f, false) || Try(-0.6f, false) || Try(0f, true) || Try(0.6f, true) || Try(-0.6f, true))) { stuckAll++; GD.Print($"  STUCK at {p} yaw dir {dirSign} (along {along}, lat {lat})"); } }
        }
        GD.Print($"BUS STOP LEAVE: {tried} start poses, {stuckForward} do not drive off straight ahead, {stuckAll} cannot get out at all");
        Check(stuckAll == 0, "a car in front of the bus stop can always get away");
    }
}
