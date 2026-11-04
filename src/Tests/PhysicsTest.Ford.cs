using System;
using CCCPRacing.Car;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    /// <summary>
    /// The ford on ILYICH'S LAMP: the gravel bar round the outside of the bend is longer but still
    /// faster than the short, deep line on the inside, and nobody gets stuck in the deep line.
    /// </summary>
    void FordTest()
    {
        GD.Print("The ford (ILYICH'S LAMP)");
        var t = TrackBuilder.Build(TrackDef.Load("res://tracks/ilyich.json"));
        Check(!float.IsNaN(t.WaterLevel) && t.FordIndex >= 0, $"the river crosses the road once, at {t.FordIndex} m, water at {t.WaterLevel:F2} m");
        int n = t.Center.Length, f = t.FordIndex;
        Vector2 d0 = t.CenterDir((f - 8 + n) % n), d1 = t.CenterDir((f + 8) % n);
        float barSide = d0.Cross(d1) >= 0 ? -1f : 1f;     // the outside of the bend
        float Depth(float lateral)
        {
            Vector2 dir = t.CenterDir(f), right = new(-dir.Y, dir.X);
            Vector2 p = t.Center[f] + right * lateral * barSide;
            return t.WaterLevel - t.MeshHeight(p.X, p.Y);
        }
        GD.Print($"  depth across the ford: the bar side 3 m {Depth(3f):F2} m, centre {Depth(0f):F2} m, the deep side 3 m {Depth(-3f):F2} m");
        Check(Depth(3f) < 0.2f && Depth(-3f) > 0.4f, "a shallow bar round the outside of the bend, the deep line on the inside");

        // 60 km/h from 25 m before the ford, flat out, steered along the road on a line (lateral, + the bar side).
        (float exitKmh, float time) Cross(float lateral)
        {
            var car = new CarSim(new CarParams(), t) { Obstacles = t.Obstacles, WorldMax = t.WorldSize - 4 };
            int from = (f - 25 + n) % n;
            Vector2 dir = t.CenterDir(from), right = new(-dir.Y, dir.X);
            Vector2 p = t.Center[from] + right * lateral * barSide;
            car.Reset(new Vector3(p.X, t.MeshHeight(p.X, p.Y) + 0.6f, p.Y), TrackData.YawFor(dir));
            Run(car, 0.5f);
            car.Gear = 2;
            car.Vel = new Vector3(dir.X, 0, dir.Y) * (60f / 3.6f);
            float time = 0;
            for (; time < 8f; time += Dt)
            {
                var cp = new Vector2(car.Pos.X, car.Pos.Z);
                int ci = t.NearestCenter(cp, out _);
                if (((ci - f + n) % n) is > 25 and < 200) break;     // 25 m past the ford
                Vector2 cd = t.CenterDir(ci), cr = new(-cd.Y, cd.X);
                Vector2 target = t.Center[(ci + 6) % n] + cr * lateral * barSide;
                var fwd = new Vector2(car.Forward.X, car.Forward.Z);
                car.SetControls(true, false, Math.Clamp(fwd.AngleTo(target - cp) * 2.5f, -1f, 1f), false);
                car.Step(Dt);
            }
            return (car.Speed * 3.6f, time);
        }
        var bar = Cross(3f);
        var deep = Cross(-3f);
        GD.Print($"  60 km/h into the ford, flat out, 50 m: the bar {bar.time:F2} s (out at {bar.exitKmh:F0} km/h), the deep line {deep.time:F2} s (out at {deep.exitKmh:F0} km/h)");
        // The narrower ford shortens the deep-water penalty, but the shallow bar must still win.
        Check(bar.time < deep.time - 0.1f && bar.exitKmh > deep.exitKmh + 15f, "the longer bar is still faster than the deep line, and much faster out of it");
        Check(deep.time < 6f, "the deep line is slow but never a trap");
    }
}
