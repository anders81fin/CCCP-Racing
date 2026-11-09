using System;
using System.Linq;
using CCCPRacing.Camera;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    /// <summary>
    /// The replay's broadcast director on every track: a lap driven along the road with a hop over the ground and a slide put in. The plan covers the
    /// whole clip without gaps, no shot is short, the cameras vary, the fixed ones stand on dry ground with the car in sight, the frames are
    /// finite and a function of the clock alone, and the picture moves smoothly except at the cuts.
    /// </summary>
    void BroadcastCameraTest()
    {
        foreach (string id in new[] { "ilyich", "siberia", "atomgrad", "bonus" })
        {
            var track = TrackBuilder.Build(TrackDef.Load($"res://tracks/{id}.json"));
            int n = track.Center.Length;
            const float speed = 24f;
            float duration = Math.Min(70f, n / speed);
            float hopAt = duration * .3f, slideAt = duration * .55f;
            Transform3D Sample(float t)
            {
                float s = Math.Clamp(t, 0, duration) * speed;
                int i = (int)s % n, j = (i + 1) % n;
                var a = new Vector3(track.Center[i].X, track.CenterH[i] + .5f, track.Center[i].Y);
                var b = new Vector3(track.Center[j].X, track.CenterH[j] + .5f, track.Center[j].Y);
                var p = a.Lerp(b, s - (int)s);
                var forward = (b - a); forward.Y = 0;
                forward = forward.LengthSquared() > 1e-6f ? forward.Normalized() : Vector3.Forward;
                if (t > hopAt && t < hopAt + 1.2f) p.Y += 2.2f * MathF.Sin((t - hopAt) / 1.2f * MathF.PI);
                float yawOffset = t > slideAt && t < slideAt + 1.5f ? .6f * MathF.Sin((t - slideAt) / 1.5f * MathF.PI) : 0;
                return new Transform3D(Basis.LookingAt(forward.Rotated(Vector3.Up, yawOffset), Vector3.Up), p);
            }
            var director = new BroadcastDirector();
            director.Build(track, Sample, duration, id);
            var plan = director.Plan;
            bool contiguous = plan.Count > 0 && plan[0].Start <= .001f && plan[^1].End >= duration - .001f;
            for (int i = 1; i < plan.Count; i++) contiguous &= MathF.Abs(plan[i].Start - plan[i - 1].End) < .01f;
            Check(contiguous, $"broadcast camera {id}: the shots cover the clip from the first frame to the last without a gap ({plan.Count} shots in {duration:0} s)");
            Check(plan.All(s => s.End - s.Start >= BroadcastDirector.MinShot - .05f || s.Equals(plan[^1])), $"broadcast camera {id}: no shot is shorter than {BroadcastDirector.MinShot} s");
            var kinds = plan.Select(s => s.Kind).Distinct().ToList();
            Check(kinds.Count >= 4, $"broadcast camera {id}: the cameras vary ({string.Join(", ", kinds)})");
            Check(plan.Zip(plan.Skip(1), (a, b) => a.Kind != b.Kind || (a.Kind is BroadcastDirector.Kind.Roadside or BroadcastDirector.Kind.HeadOn or BroadcastDirector.Kind.Fly && a.Eye.DistanceTo(b.Eye) > 12f)).All(x => x),
                $"broadcast camera {id}: never the same kind of camera twice in a row (two fixed cameras of one kind only stand well apart)");
            float cuts = plan.Count / Math.Max(1f, duration);
            Check(cuts > 1f / 12f && cuts < 1f / 2f, $"broadcast camera {id}: a cut every {1f / cuts:0.0} s on average");
            bool fixedOk = true, finite = true, same = true, smooth = true;
            Vector3 lastEye = default; int lastIndex = -1; float lastFov = 0;
            for (float t = 0; t <= duration; t += 1f / 30f)
            {
                var v = director.Frame(t);
                var again = director.Frame(t);
                same &= v.Index == again.Index && v.Eye.IsEqualApprox(again.Eye) && v.Target.IsEqualApprox(again.Target);
                if (v.Kind == BroadcastDirector.Kind.TopDown) { lastIndex = v.Index; lastEye = default; continue; }
                finite &= float.IsFinite(v.Eye.X + v.Eye.Y + v.Eye.Z + v.Target.X + v.Target.Y + v.Target.Z + v.Fov) && v.Fov > 10f && v.Fov < 95f && v.Eye.DistanceTo(v.Target) > .5f;
                if (v.Index == lastIndex && lastEye != default) smooth &= v.Eye.DistanceTo(lastEye) < 3f && MathF.Abs(v.Fov - lastFov) < 6f;
                lastEye = v.Eye; lastIndex = v.Index; lastFov = v.Fov;
            }
            foreach (var s in plan.Where(s => s.Kind is BroadcastDirector.Kind.Roadside or BroadcastDirector.Kind.HeadOn or BroadcastDirector.Kind.Fly or BroadcastDirector.Kind.Jump or BroadcastDirector.Kind.Splash))
            {
                bool dry = track.Sample(s.Eye.X, s.Eye.Z).WaterDepth <= 0f;
                float ground = WorldBackdrop.HeightAt(track, new Vector2(s.Eye.X, s.Eye.Z));
                bool seen = true;
                for (float t = s.Start; t <= s.End; t += .5f)
                {
                    var car = Sample(t).Origin;
                    seen &= ReplayCameraDirector.ClearView(track, s.Eye, car);
                    // Independently of the director's own tests: no taiga tree crown on the line or round the camera, no snow bank in the way.
                    for (int k = 0; k <= 24 && seen; k++)
                    {
                        var p = (car + Vector3.Up).Lerp(s.Eye, k / 24f);
                        foreach (var (pos, scale, _, _) in track.Taiga)
                            if ((pos.X - p.X) * (pos.X - p.X) + (pos.Z - p.Z) * (pos.Z - p.Z) < MathF.Pow(1.7f * scale, 2) && p.Y < pos.Y + 8f * scale) { seen = false; break; }
                        if (seen && track.Banks != null && k > 0)
                        {
                            int hint = track.NearestCenter(new Vector2(p.X, p.Z), out _);
                            if (track.Banks.Query(new Vector2(p.X, p.Z), .1f, ref hint, out _, out float pen, out float top) && p.Y < top) seen = false;
                        }
                    }
                }
                if (!(dry && s.Eye.Y > ground + .3f && seen)) GD.Print($"  bad fixed camera {id} {s.Kind} {s.Start:0.0}-{s.End:0.0} dry={dry} aboveGround={s.Eye.Y > ground + .3f} seen={seen} eye={s.Eye}");
                fixedOk &= dry && s.Eye.Y > ground + .3f && seen;
            }
            Check(finite, $"broadcast camera {id}: every frame is finite, with a sane lens");
            Check(same, $"broadcast camera {id}: a frame depends on the clock alone");
            Check(smooth, $"broadcast camera {id}: the picture moves smoothly inside a shot");
            Check(fixedOk, $"broadcast camera {id}: the fixed cameras stand on dry ground and see the car through their shots");
            GD.Print($"  broadcast {id}: " + string.Join(" ", plan.Select(s => $"{s.Start:0.0}-{s.End:0.0} {s.Kind}({s.Why})")));
        }
    }
}

public partial class PhysicsTest
{
    /// <summary>The title take's day: each change of the car brings the next hour (noon, the golden sunset, dusk, the dead of night), smoothly and never backwards while the cars go up.</summary>
    void TitleRecLightTest()
    {
        float clock = Race.RaceScene.TitleRecLights[0];
        bool rises = true, smooth = true; float last = clock;
        var fuels = new[] { Race.FuelGrade.Diesel, Race.FuelGrade.Petrol, Race.FuelGrade.Rocket, Race.FuelGrade.Sputnik };
        foreach (var fuel in fuels)
        {
            float target = Race.RaceScene.TitleRecLights[(int)fuel];
            for (int i = 0; i < 60 * 12; i++)
            {
                clock = Race.RaceScene.TitleRecLightStep(clock, fuel, 1f / 60f);
                rises &= clock >= last - 1e-5f; smooth &= Math.Abs(clock - last) < .02f; last = clock;
            }
            Check(Math.Abs(clock - target) < .001f, $"title take: the {fuel}'s hour is reached ({clock:0.00} of {target:0.00})");
        }
        Check(rises && smooth, "title take: the day moves on smoothly through noon, sunset, dusk and night");
        float afterChange = Race.RaceScene.TitleRecLightStep(Race.RaceScene.TitleRecLights[1], Race.FuelGrade.Rocket, 3f);
        Check(afterChange > Race.RaceScene.TitleRecLights[1] + .5f && afterChange < Race.RaceScene.TitleRecLights[2] + 1e-4f, "title take: the light does not jump when the car changes, it takes a few seconds");
    }
}
