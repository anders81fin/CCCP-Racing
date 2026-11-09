using System;
using CCCPRacing.Car;
using CCCPRacing.Race;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    struct PowerRun
    {
        public float T100, T200, Top, Brake100, BrakeTop, SpinShare;
    }

    static CarSim PowerCar(int percent, SurfaceType surface = SurfaceType.Concrete, FuelGrade? fuel = null)
    {
        var engine = new CarParams();
        if (fuel is { } g) FuelGrades.ApplyEngine(engine, g);      // (the rally's fuel: the diesel is an engine of its own)
        var car = NewCar(new FlatGround { Surface = surface }, engine);
        car.PowerScale = percent / 100f;
        if (fuel is { } fg) car.TurboGain = FuelGrades.TurboGainFor(fg);
        car.Overdrive = Training.Overdrive(percent);
        Run(car, 1f);
        return car;
    }

    /// <summary>Flat concrete at a Stakhanov power: 0-100, 0-200, top speed, straight-line stops.</summary>
    static PowerRun MeasurePower(int percent, FuelGrade? fuel = null)
    {
        var r = new PowerRun { T100 = -1, T200 = -1 };
        var car = PowerCar(percent, SurfaceType.Concrete, fuel);
        float t = 0;
        int spin = 0, steps = 0;
        Run(car, 60f, () =>
        {
            car.SetControls(true, false, 0, false);
            t += Dt;
            if (t < 2f) { steps++; if (car.Wheels[2].Spinning || car.Wheels[3].Spinning) spin++; }
            if (r.T100 < 0 && car.Speed > 100f / 3.6f) r.T100 = t;
            if (r.T200 < 0 && car.Speed > 200f / 3.6f) r.T200 = t;
            r.Top = Math.Max(r.Top, car.Speed * 3.6f);
        });
        r.SpinShare = (float)spin / steps;
        r.BrakeTop = StopDistance(car);

        car = PowerCar(percent, SurfaceType.Concrete, fuel);
        car.Vel = car.Forward * (100f / 3.6f);
        r.Brake100 = StopDistance(car);
        return r;
    }

    /// <summary>Brake in a straight line until (nearly) stopped; the distance travelled.</summary>
    static float StopDistance(CarSim car)
    {
        Vector3 p0 = car.Pos;
        for (float t = 0; t < 30f && car.ForwardSpeed > 0.5f; t += Dt)
        {
            car.SetControls(false, car.ForwardSpeed > 1.5f, 0, false);
            car.Step(Dt);
        }
        return car.Pos.DistanceTo(p0);
    }

    /// <summary>Full throttle + full lock on gravel from 12 m/s for 2 s: the largest slip angle.</summary>
    static float PowerSlide(int percent)
    {
        var car = PowerCar(percent, SurfaceType.Gravel);
        car.Vel = car.Forward * 12f;
        float maxSlip = 0;
        Run(car, 2f, () =>
        {
            car.SetControls(true, false, 1f, false);
            if (car.Speed > 8f) maxSlip = Math.Max(maxSlip, SlipDeg(car));
        });
        return car.Up.Y > 0.5f ? maxSlip : -1;
    }

    // Five-speed rally gearing with driven-wheel slip, 60 s on concrete; no overdrive at 100/200 %.
    // (100 % was 155.5 / 3.767 s until petrol got 25 % more torque, 2026-11-02; 200 % was already traction-limited and did not change)
    const float Base100Top = 155.5f, Base100T100 = 5.737f, Base200Top = 155.6f, Base200T100 = 3.525f;      // (9 November: the petrol car 200 Nm and 1050 kg; was 155.5 / 6.537 / 155.6 / 3.537 at 175 Nm, 155.6 / 3.500 / 155.7 / 3.133 at 387.5 Nm and 1000 kg)

    /// <summary>
    /// Stakhanov overdrive: nothing changes up to 200 %; 350 % and 500 % gear taller and grip harder,
    /// so the extra power finally shows as top speed and a rocket launch.
    /// </summary>
    void OverdriveTest()
    {
        GD.Print("Stakhanov overdrive (flat concrete)");
        Check(Training.Overdrive(50) == 0 && Training.Overdrive(100) == 0 && Training.Overdrive(200) == 0
              && Math.Abs(Training.Overdrive(350) - 0.5f) < 1e-6f && Training.Overdrive(500) == 1f, "overdrive 0 up to 200 %, 0.5 at 350 %, 1 at 500 %");
        int[] levels = { 100, 200, 350, 500 };
        var runs = new PowerRun[levels.Length];
        for (int i = 0; i < levels.Length; i++)
        {
            var r = runs[i] = MeasurePower(levels[i]);
            GD.Print($"  {levels[i],3} %: top {r.Top:F1} km/h, 0-100 {r.T100:F3} s, 0-200 {r.T200:F2} s, 100-0 {r.Brake100:F1} m, top-0 {r.BrakeTop:F0} m, launch wheelspin {r.SpinShare * 100:F0}% of 2 s");
        }
        Check(Math.Abs(runs[0].Top - Base100Top) < 0.1f && Math.Abs(runs[0].T100 - Base100T100) < 0.005f
              && Math.Abs(runs[1].Top - Base200Top) < 0.1f && Math.Abs(runs[1].T100 - Base200T100) < 0.005f,
              "100 % and 200 % retain the measured rally gearbox baseline without overdrive");
        Check(runs[3].Top > 330f && runs[2].Top > runs[1].Top + 50f, "500 % tops 330 km/h, 350 % well above 200 %");
        Check(runs[3].T100 <= 2.2f && runs[3].T100 <= runs[1].T100 * 0.70f && runs[2].T100 < runs[1].T100 && runs[3].T100 < runs[2].T100,
              "500 % reaches 100 in 2.2 s or less, at least 30% quicker than 200 %, with 350 % in between");
        Check(runs[3].SpinShare > 0.05f, "500 % still spins the rear wheels at launch");
        Check(runs[3].BrakeTop > 0 && runs[3].BrakeTop < 450f && runs[3].Brake100 <= runs[1].Brake100, "500 % stops from top speed, no worse from 100");

        float s100 = PowerSlide(100), s500 = PowerSlide(500);
        GD.Print($"  power slide on gravel, max slip: 100 % {s100:F0}°, 500 % {s500:F0}°");
        Check(s500 > 10f, "500 % still slides (not glued), stays on its wheels");

        // 380 km/h is 0.44 m per physics step (240 Hz, as in the race): into a stone wall (a chain
        // of 0.45 m circles), a lone pole and a building, head-on and glancing, it must bounce off.
        bool solid = true;
        string worst = "";
        foreach (var (name, obstacles) in new (string, Obstacle[])[]
        {
            ("stone wall", WallAt(30f, 0.45f, 0.9f)),
            ("pole", new[] { new Obstacle(new Vector2(0, -30f), 0.2f) }),
            ("building", WallAt(30f, 4f, 8f)),
        })
        foreach (float angle in new[] { 0f, 30f, 60f })
        {
            if (name == "pole" && angle > 0) continue;     // a glancing hit may slide round a pole
            var car = PowerCar(500);
            car.Obstacles = obstacles;
            float a0 = Mathf.DegToRad(angle);
            // Aim at the obstacle line 30 m ahead (-Z), approaching at this angle off square.
            car.Reset(new Vector3(30f * MathF.Tan(a0), 0.65f, 0), a0);
            Run(car, 0.5f);
            car.Vel = car.Forward * (380f / 3.6f);
            float impact = 0;
            Run(car, 1.5f, () => { car.SetControls(true, false, 0, false); impact = Math.Max(impact, car.LastImpact); });
            bool ok = car.Pos.Z > -30f + 0.2f && float.IsFinite(car.Pos.X + car.Pos.Y + car.Pos.Z + car.Vel.X + car.Vel.Y + car.Vel.Z) && impact > 0;
            if (!ok) { solid = false; worst += $" {name}@{angle}° (z {car.Pos.Z:F1}, impact {impact:F0})"; }
            if (angle == 0) GD.Print($"  380 km/h head-on into a {name}: impact {impact:F0} m/s, stopped at z {car.Pos.Z:F1} (obstacle at -30)");
        }
        Check(solid, "380 km/h: never tunnels through a stone wall, pole or building" + worst);
    }

    /// <summary>
    /// Flat out across the real bonus map in every direction for 8 s: it may fly over crests (the
    /// joke), but never NaN, sink into the terrain for good, leave the world or pass an obstacle.
    /// </summary>
    void OverdriveOnTrack()
    {
        GD.Print("Stakhanov overdrive: flat out across the bonus map, 12 headings from 6 points");
        var track = TrackBuilder.Build(TrackDef.Load("res://tracks/bonus.json"));
        bool ok217 = WildRuns(track, 200, 217f), ok380 = WildRuns(track, 500, 380f);
        Check(ok217 && ok380, "380 km/h anywhere on the map: no NaN, never under the terrain for long, out of the world or through an obstacle");
    }

    bool WildRuns(TrackData track, int percent, float kmh)
    {
        float minClear = float.MaxValue, maxClear = 0, maxAir = 0, deepest = 0;
        int runs = 0, bad = 0, flipped = 0;
        for (int start = 0; start < track.Center.Length; start += track.Center.Length / 6)
        for (int h = 0; h < 12; h++)
        {
            var car = new CarSim(new CarParams(), track)
            {
                Obstacles = track.Obstacles, WorldMax = track.WorldSize - 4,
                PowerScale = percent / 100f, Overdrive = Training.Overdrive(percent),
            };
            car.Reset(track.CenterPos(start) + Vector3.Up * 0.7f, h * MathF.Tau / 12f);
            Run(car, 0.3f);
            car.Vel = car.Forward * (kmh / 3.6f);
            runs++;
            string why = null;
            bool wasFlipped = false;
            float t = 0, under = 0;
            Run(car, 8f, () =>
            {
                car.SetControls(true, false, 0, false);
                t += Dt;
                if (why != null) return;
                if (!float.IsFinite(car.Pos.X + car.Pos.Y + car.Pos.Z + car.Vel.X + car.Vel.Y + car.Vel.Z + car.AngVel.X + car.AngVel.Y + car.AngVel.Z)) { why = "NaN"; return; }
                float clear = car.Pos.Y - track.Sample(car.Pos.X, car.Pos.Z).Height;
                minClear = Math.Min(minClear, clear);
                maxClear = Math.Max(maxClear, clear);
                maxAir = Math.Max(maxAir, car.AirTime);
                // Ramming the steep bank at the map edge the body sinks in for a moment (2-3 m at
                // 380 km/h, 1 m at 217) before the ground throws it sky high; staying under or
                // sinking deeper would be falling through.
                under = clear < 0 ? under + Dt : 0;
                if (under > 0.3f || clear < -4f) why = $"under the terrain ({clear:F2} m for {under:F2} s)";
                if (car.Pos.X < 3.9f || car.Pos.Z < 3.9f || car.Pos.X > track.WorldSize - 3.9f || car.Pos.Z > track.WorldSize - 3.9f) why = $"outside the world {car.Pos}";
                if (car.UpsideDownTime > 1f) wasFlipped = true;
                // Tunnelling: a body circle's centre inside an obstacle (a normal hit stays outside it).
                foreach (float cz in CollisionZ)
                {
                    Vector3 c = car.Pos + car.Rot * new Vector3(0, 0, cz);
                    foreach (var o in track.Obstacles)
                    {
                        float d = new Vector2(c.X, c.Z).DistanceTo(o.Center), minD = o.Radius + 0.92f;
                        if (d < minD) deepest = Math.Max(deepest, (minD - d) / minD);
                        if (d < o.Radius) why = $"inside an obstacle (r {o.Radius}) at {o.Center}";
                    }
                }
                if (why != null) why += $" after {t:F2} s";
            });
            if (why != null) { bad++; GD.Print($"    BAD: from centre {start} heading {h * 30}°: {why}"); }
            if (wasFlipped) flipped++;
        }
        GD.Print($"  {percent} % from {kmh:F0} km/h, {runs} runs: CG above ground {minClear:F2} .. {maxClear:F1} m, longest flight {maxAir:F1} s, " +
                 $"deepest obstacle overlap {deepest * 100:F0}% of contact distance, {flipped} ended on the roof, {bad} bad");
        return bad == 0;
    }

    static readonly float[] CollisionZ = { -1.3f, 0f, 1.3f };

    /// <summary>A straight row of circles across the road (along X) at z = -dist, like TrackBuilder's walls.</summary>
    static Obstacle[] WallAt(float dist, float radius, float spacing)
    {
        var list = new System.Collections.Generic.List<Obstacle>();
        for (float x = -60f; x <= 60f; x += spacing) list.Add(new Obstacle(new Vector2(x, -dist), radius));
        return list.ToArray();
    }
}
