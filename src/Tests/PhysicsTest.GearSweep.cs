using System;
using CCCPRacing.Car;
using CCCPRacing.Race;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    /// <summary>--gear-sweep: for each fuel, full throttle from a standstill on flat concrete at a range of final drives: 0-100, the time over 400 m and 1000 m, top speed.</summary>
    static float[] SpreadGears(float g1, float p)
    {
        const float g5 = 1.18f;
        var g = new float[5];
        for (int i = 0; i < 5; i++) g[i] = g5 * MathF.Pow(g1 / g5, MathF.Pow((4 - i) / 4f, p));
        return g;
    }

    /// <summary>--grade-report: the steepest 30 m, 60 m and 100 m grades along a track's centre line, and the share of the lap above 5 and 8 %.</summary>
    void PropRoadReport(string id)
    {
        var track = TrackBuilder.Build(TrackDef.Load($"res://tracks/{id}.json"));
        var probe = Array.Find(OS.GetCmdlineUserArgs(), a => a.StartsWith("--probe="));
        if (probe != null)
        {
            foreach (var pt in probe["--probe=".Length..].Split(';'))
            {
                var xy = pt.Split(',');
                var q = new Vector2(float.Parse(xy[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(xy[1], System.Globalization.CultureInfo.InvariantCulture));
                int k = track.NearestCenter(q, out float dd);
                GD.Print($"PROBE inside-camp {BonusCampLayout.Inside(q)} ({q.X:F0}, {q.Y:F0}): {dd:F1} m from the centre line at ({track.Center[k].X:F0}, {track.Center[k].Y:F0}), {dd - track.HalfWidth[k]:F1} m from the road edge");
            }
            return;
        }
        foreach (var pr in track.Props)
        {
            if (pr.Type is not ("powercabinet" or "powerconsumer" or "pole" or "reactor" or "turbinehall" or "sarcophagus" or "stack" or "gaiservice")) continue;
            int i = track.NearestCenter(new Vector2(pr.Position.X, pr.Position.Z), out float d);
            float edge = d - track.HalfWidth[i];
            if (edge < 6f) GD.Print($"PROP {pr.Type} '{pr.Text}' at ({pr.Position.X:F0}, {pr.Position.Z:F0}): {d:F1} m from the centre line, {edge:F1} m from the road edge (half width {track.HalfWidth[i]:F1})");
        }
    }

    void GradeReport(string id)
    {
        var track = TrackBuilder.Build(TrackDef.Load($"res://tracks/{id}.json"));
        int n = track.Center.Length;
        float[] h = new float[n];
        for (int i = 0; i < n; i++) h[i] = track.MeshHeight(track.Center[i].X, track.Center[i].Y);
        foreach (int w in new[] { 30, 60, 100 })
        {
            float best = 0; int at = 0; int over5 = 0, over8 = 0;
            for (int i = 0; i < n; i++)
            {
                float g = (h[(i + w) % n] - h[i]) / w;
                if (g > best) { best = g; at = i; }
                if (g > 0.05f) over5++;
                if (g > 0.08f) over8++;
            }
            GD.Print($"{id}: steepest {w} m climb {best * 100:F1} % at {at} m; {over5 * 100f / n:F0} % of the lap climbs over 5 %, {over8 * 100f / n:F0} % over 8 %");
        }
    }

    class SlopeGround : IGround
    {
        public float Grade;
        public SurfaceType Surf = SurfaceType.Concrete;
        public Vector2 Dir = new(1, 0);      // (the way up the hill: the car's heading)
        public GroundSample Sample(float x, float z) => new()
        {
            Height = Grade * (x * Dir.X + z * Dir.Y), Normal = new Vector3(-Grade * Dir.X, 1f, -Grade * Dir.Y).Normalized(), Slope = Vector2.Zero, Surface = Surf,
        };
    }

    static float[] GearsN(int n, float g1, float p)
    {
        const float top = 1.18f;
        var g = new float[n];
        for (int i = 0; i < n; i++) g[i] = top * MathF.Pow(g1 / top, MathF.Pow((n - 1 - i) / (float)(n - 1), p));
        return g;
    }

    /// <summary>Seconds to cover 150 m uphill on the grade from a rolling 50 km/h start at full throttle (30 when it never gets there); also the speed at the end.</summary>
    static float ClimbTime(CarParams engine, FuelGrade fuel, float grade, out float endKmh)
    {
        var ground = new SlopeGround { Grade = grade };
        var car = new CarSim(engine, ground) { WorldMin = -1e6f, WorldMax = 1e6f };
        car.Reset(new Vector3(0, 0.8f, 0), 0);
        ground.Dir = new Vector2(car.Forward.X, car.Forward.Z);
        car.Reset(new Vector3(0, 0.8f, 0), 0);
        car.PowerScale = FuelGrades.PhysicsPercent(fuel) / 100f;
        car.TurboGain = FuelGrades.TurboGainFor(fuel);
        Run(car, 0.5f);
        car.Vel = car.Forward * (50f / 3.6f);
        Vector3 p0 = car.Pos;
        float t = 0;
        for (; t < 30f; t += Dt)
        {
            car.SetControls(true, false, 0, false);
            car.Step(Dt);
            if (car.Pos.DistanceTo(p0) > 150f) break;
        }
        endKmh = car.Speed * 3.6f;
        return t;
    }

    /// <summary>--grade-surfaces: the current gears of each fuel, uphill from 50 km/h at full throttle for 12 s on each surface: speed at the end and the share of the time in each gear.</summary>
    void GradeSurfaces()
    {
        foreach (var fuel in new[] { FuelGrade.Diesel })
        foreach (var surf in new[] { SurfaceType.Concrete, SurfaceType.Gravel, SurfaceType.Mud, SurfaceType.Grass })
        foreach (float grade in new[] { 0.06f, 0.10f, 0.15f, 0.20f })
        {
            var e = new CarParams(); FuelGrades.ApplyEngine(e, fuel);
            var ground = new SlopeGround { Grade = grade, Surf = surf };
            var car = new CarSim(e, ground) { WorldMin = -1e6f, WorldMax = 1e6f };
            car.Reset(new Vector3(0, 0.8f, 0), 0);
            ground.Dir = new Vector2(car.Forward.X, car.Forward.Z);
            car.Reset(new Vector3(0, 0.8f, 0), 0);
            car.PowerScale = FuelGrades.PhysicsPercent(fuel) / 100f;
            Run(car, 0.5f);
            car.Vel = car.Forward * (50f / 3.6f);
            var inGear = new int[8]; int steps = 0, shifts = 0, last = car.Gear;
            for (float t = 0; t < 12f; t += Dt)
            {
                car.SetControls(true, false, 0, false);
                car.Step(Dt);
                inGear[Math.Clamp(car.Gear, 0, 7)]++; steps++;
                if (car.Gear != last) { shifts++; last = car.Gear; }
            }
            GD.Print($"SURF {fuel} {surf} {grade * 100:F0} %: {car.Speed * 3.6f:F0} km/h after 12 s, gears 1-5 {inGear[1] * 100 / steps}/{inGear[2] * 100 / steps}/{inGear[3] * 100 / steps}/{inGear[4] * 100 / steps} %, {shifts} shifts, rpm {car.Rpm:F0}");
        }
    }

    void GradeGearSearch()
    {
        var fuel = FuelGrade.Diesel;
        var rows = new System.Collections.Generic.List<(float S, string D)>();
        foreach (int n in new[] { 4, 5 })
        foreach (float fd in new[] { 3.6f, 4.0f, 4.4f, 4.8f, 5.2f })
        foreach (float g1 in new[] { 3.8f, 4.6f, 5.4f })
        foreach (float pw in new[] { 1.0f, 1.3f })
        {
            var e = new CarParams(); FuelGrades.ApplyEngine(e, fuel);
            e.FinalDrive = fd; e.Gears = GearsN(n, g1, pw);
            var (t100, _, t400) = GearRun(e, fuel);
            float c6 = ClimbTime(e, fuel, 0.06f, out _), c10 = ClimbTime(e, fuel, 0.10f, out float v10), c15 = ClimbTime(e, fuel, 0.15f, out float v15);
            float S = t400 + c6 + c10 + c15;
            rows.Add((S, $"{n} gears FD {fd:F1} g1 {g1:F1} p {pw:F1} [{string.Join("/", Array.ConvertAll(e.Gears, x => x.ToString("F2")))}]: 0-100 {t100:F1}, 400 m {t400:F1}, 150 m uphill 6 % {c6:F1} s, 10 % {c10:F1} s ({v10:F0} km/h), 15 % {c15:F1} s ({v15:F0} km/h), S {S:F1}"));
        }
        var cur = new CarParams(); FuelGrades.ApplyEngine(cur, fuel);
        var (a, _, b) = GearRun(cur, fuel);
        float k6 = ClimbTime(cur, fuel, 0.06f, out _), k10 = ClimbTime(cur, fuel, 0.10f, out float w10), k15 = ClimbTime(cur, fuel, 0.15f, out float w15);
        GD.Print($"CURRENT diesel FD {cur.FinalDrive}: 0-100 {a:F1}, 400 m {b:F1}, 6 % {k6:F1} s, 10 % {k10:F1} s ({w10:F0} km/h), 15 % {k15:F1} s ({w15:F0} km/h), S {b + k6 + k10 + k15:F1}");
        rows.Sort((x, y) => x.S.CompareTo(y.S));
        for (int i = 0; i < Math.Min(8, rows.Count); i++) GD.Print("BEST " + rows[i].D);
    }

    void GearSweep()
    {
        foreach (var fuel in new[] { FuelGrade.Diesel, FuelGrade.Petrol, FuelGrade.Rocket, FuelGrade.Sputnik })
        {
            (float J, float t100, float mid, float t400, float g1, float p) best = (1e9f, 0, 0, 0, 0, 0), cur = default;
            for (int gi = 0; gi <= 12; gi++) for (int pi = 0; pi <= 6; pi++)
            {
                float g1 = 3.4f + 0.2f * gi, pw = 0.9f + 0.1f * pi;
                var engine = new CarParams(); FuelGrades.ApplyEngine(engine, fuel);
                engine.Gears = SpreadGears(g1, pw);
                var (t100, mid, t400) = GearRun(engine, fuel);
                float J = t100 + mid + 0.25f * t400;
                if (gi == 0 && pi == 0) { }
                if (J < best.J) best = (J, t100, mid, t400, g1, pw);
            }
            var def = new CarParams(); FuelGrades.ApplyEngine(def, fuel);
            var (d100, dmid, d400) = GearRun(def, fuel);
            var bg = SpreadGears(best.g1, best.p);
            GD.Print($"== {fuel}: current gears {string.Join("/", def.Gears)}: 0-100 {d100:F2}, 80-140 {dmid:F2}, 400 m {d400:F2}, J {d100 + dmid + 0.25f * d400:F2}");
            GD.Print($"   best g1 {best.g1:F1} p {best.p:F1}: gears {string.Join("/", Array.ConvertAll(bg, x => x.ToString("F2")))}: 0-100 {best.t100:F2}, 80-140 {best.mid:F2}, 400 m {best.t400:F2}, J {best.J:F2}");
        }
    }

    (float t100, float mid, float t400) GearRun(CarParams engine, FuelGrade fuel)
    {
        var car = new CarSim(engine, new FlatGround { Surface = SurfaceType.Concrete }) { WorldMin = -1e6f, WorldMax = 1e6f };
        car.Reset(new Vector3(0, 0.8f, 0), 0);
        car.PowerScale = FuelGrades.PhysicsPercent(fuel) / 100f;
        car.TurboGain = FuelGrades.TurboGainFor(fuel);
        Run(car, 1f);
        Vector3 p0 = car.Pos;
        float t = 0, t100 = 99, t80 = -1, t140 = -1, t400 = 99;
        for (; t < 60f; t += Dt)
        {
            car.SetControls(true, false, 0, false);
            car.Step(Dt);
            float v = car.Speed * 3.6f;
            if (t100 == 99 && v > 100f) t100 = t;
            if (t80 < 0 && v > 80f) t80 = t;
            if (t140 < 0 && v > 140f) t140 = t;
            if (t400 == 99 && car.Pos.DistanceTo(p0) > 400f) t400 = t;
            if (t400 < 99 && t140 > 0) break;
        }
        float mid = t140 > 0 && t80 > 0 ? t140 - t80 : 60f;
        return (t100, mid, t400);
    }
}
