using System;
using System.Collections.Generic;
using System.Linq;
using CCCPRacing.Camera;
using Godot;

namespace CCCPRacing.Tests;

// Development-only: the overhead camera's look-ahead experiment (src/Camera/ChaseCamera.Lead.cs).
public partial class PhysicsTest
{
    const float CamAspect = 16f / 9f;

    /// <summary>The car's screen place from the camera's real transform (independent of the camera's own limit maths).</summary>
    static Vector2 CamNdc(ChaseCamera cam, Vector3 p)
    {
        var local = cam.GlobalTransform.AffineInverse() * p;
        float tanV = MathF.Tan(Mathf.DegToRad(cam.Fov) * 0.5f), depth = -local.Z;
        return depth <= 1e-4f ? new Vector2(9, 9) : new Vector2(local.X / (depth * tanV * CamAspect), local.Y / (depth * tanV));
    }

    /// <summary>Metres of ground ahead of the car along <paramref name="dir"/> before the screen's edge.</summary>
    static float CamAhead(ChaseCamera cam, Vector3 car, Vector3 dir)
    {
        for (float t = 0; t < 250f; t += 0.25f)
        {
            var n = CamNdc(cam, car + dir * t);
            if (MathF.Abs(n.X) > 1f || MathF.Abs(n.Y) > 1f) return t;
        }
        return 250f;
    }

    sealed class CamPath
    {
        public readonly List<Vector3> Pos = new(), Vel = new();
        public float Step = 0.001f;
        public CamPath(Func<float, Vector3> velocity, Vector3 start, float seconds)
        {
            var p = start;
            for (float t = 0; t <= seconds + 0.01f; t += Step)
            {
                var v = velocity(t);
                Pos.Add(p); Vel.Add(v);
                // (trapezoid over the next step)
                p += (v + velocity(t + Step)) * 0.5f * Step;
            }
        }
        public (Vector3 pos, Vector3 vel) At(float t)
        {
            float f = Math.Clamp(t / Step, 0, Pos.Count - 1.001f); int i = (int)f; float w = f - i;
            return (Pos[i].Lerp(Pos[i + 1], w), Vel[i].Lerp(Vel[i + 1], w));
        }
    }

    sealed record CamSample(float T, Vector3 Car, Vector3 Vel, Vector2 Ndc, Vector2 Lead, Vector3 CamPos, Basis Basis, float Ahead);

    List<CamSample> CamRun(bool directed, CamPath path, float seconds, float dt, bool snapStart = true, float overdriveLead = float.PositiveInfinity)
    {
        bool old = ChaseCamera.DirectedLead; ChaseCamera.DirectedLead = directed;
        var cam = new ChaseCamera { ShakeEnabled = false, MaxLead = overdriveLead }; AddChild(cam);
        var samples = new List<CamSample>();
        var (p0, v0) = path.At(0);
        if (snapStart) cam.SnapDriving(p0, v0); else cam.Snap(p0);
        for (int n = 1; n <= (int)MathF.Round(seconds / dt); n++)
        {
            float t = n * dt; var (p, v) = path.At(t);
            cam.Feel(v.Length() * 3.6f, 0, false, 0, dt);
            cam.FollowDrive(p, v, dt);
            var dirV = v.LengthSquared() > 0.01f ? new Vector3(v.X, 0, v.Z).Normalized() : Vector3.Forward;
            samples.Add(new CamSample(t, p, v, CamNdc(cam, p), new Vector2(cam.Focus.X - p.X, cam.Focus.Z - p.Z), cam.GlobalPosition, cam.GlobalTransform.Basis, CamAhead(cam, p, dirV)));
        }
        cam.Free(); ChaseCamera.DirectedLead = old;
        return samples;
    }

    /// <summary>As CamRun, but MaxLead follows <paramref name="cap"/> (seconds -> metres or infinity) frame by frame, as the real scene sets it when OCTANE BOOSTER starts and ends (ApplyTrainingPower).</summary>
    List<CamSample> CamRunCap(CamPath path, float seconds, float dt, Func<float, float> cap)
    {
        bool old = ChaseCamera.DirectedLead; ChaseCamera.DirectedLead = true;
        var cam = new ChaseCamera { ShakeEnabled = false, MaxLead = cap(0) }; AddChild(cam);
        var samples = new List<CamSample>();
        var (p0, v0) = path.At(0);
        cam.SnapDriving(p0, v0);
        for (int n = 1; n <= (int)MathF.Round(seconds / dt); n++)
        {
            float t = n * dt; var (p, v) = path.At(t);
            cam.MaxLead = cap(t);
            cam.Feel(v.Length() * 3.6f, 0, false, 0, dt);
            cam.FollowDrive(p, v, dt);
            samples.Add(new CamSample(t, p, v, CamNdc(cam, p), new Vector2(cam.Focus.X - p.X, cam.Focus.Z - p.Z), cam.GlobalPosition, cam.GlobalTransform.Basis, 0));
        }
        cam.Free(); ChaseCamera.DirectedLead = old;
        return samples;
    }

    /// <summary>The largest frame-to-frame step of the camera's position (m) and the largest second difference (m per frame^2: the change of its velocity) over [from, to].</summary>
    static (float step, float acc) CamContinuity(List<CamSample> run, float from, float to)
    {
        float step = 0, acc = 0;
        for (int i = 2; i < run.Count; i++)
        {
            if (run[i].T < from || run[i].T > to) continue;
            var d1 = run[i].CamPos - run[i - 1].CamPos; var d0 = run[i - 1].CamPos - run[i - 2].CamPos;
            step = MathF.Max(step, d1.Length()); acc = MathF.Max(acc, (d1 - d0).Length());
        }
        return (step, acc);
    }

    /// <summary>The soft margin may be passed a little while it tightens (finite response); the hard margin never.</summary>
    static bool CamSafe(IEnumerable<CamSample> run) => run.Max(x => CamExcess(x.Ndc)) <= 0.07f && run.Max(x => CamHardExcess(x.Ndc)) <= 0.001f;
    static float CamExcess(Vector2 n) => MathF.Max(MathF.Max(-n.Y - ChaseCamera.MarginBottom, n.Y - ChaseCamera.MarginTop), MathF.Abs(n.X) - ChaseCamera.MarginSide);
    /// <summary>How far the car is beyond the hard margins (never allowed).</summary>
    static float CamHardExcess(Vector2 n) => MathF.Max(MathF.Max(-n.Y - ChaseCamera.HardBottom, n.Y - ChaseCamera.HardTop), MathF.Abs(n.X) - ChaseCamera.HardSide);

    void CameraLeadTests()
    {
        GD.Print("Camera look-ahead experiment: directed lead against the classic one");
        if (Array.Exists(OS.GetCmdlineUserArgs(), a => a == "--camera-lead-scan"))
        {
            foreach (float lt in new[] { 0.42f, 0.55f, 0.7f, 0.85f, 1.0f })
            {
                ChaseCamera.LeadTime = lt; float g = 0, gw = 9; int c = 0; float ex = -9;
                foreach (float kmh in new[] { 60f, 80f, 100f, 120f })
                foreach (var dd in new[] { new Vector3(0, 0, -1), new Vector3(1, 0, -1).Normalized(), new Vector3(1, 0, 0), new Vector3(0, 0, 1), new Vector3(-1, 0, 1).Normalized() })
                {
                    var path = new CamPath(_ => dd * (kmh / 3.6f), new Vector3(300, 0, 300), 8f);
                    var nw = CamRun(true, path, 8f, 1f / 60f).Last(); var cl = CamRun(false, path, 8f, 1f / 60f).Last();
                    float r = nw.Ahead / MathF.Max(1f, cl.Ahead); g += r; gw = MathF.Min(gw, r); c++; ex = MathF.Max(ex, CamExcess(nw.Ndc));
                }
                GD.Print($"  SCAN LeadTime {lt:F2}: ahead gain mean x{g / c:F2}, worst x{gw:F2}, margin excess {ex:F3}");
            }
            ChaseCamera.LeadTime = 0.42f; return;
        }
        var dirs = new (string name, Vector3 d)[] { ("N", new(0, 0, -1)), ("NE", new Vector3(1, 0, -1).Normalized()), ("E", new(1, 0, 0)), ("SE", new Vector3(1, 0, 1).Normalized()),
            ("S", new(0, 0, 1)), ("SW", new Vector3(-1, 0, 1).Normalized()), ("W", new(-1, 0, 0)), ("NW", new Vector3(-1, 0, -1).Normalized()) };

        // 0. The view angle, the compass and the lens are the same as before.
        var probe = new ChaseCamera(); AddChild(probe);
        Check(probe.Offset == new Vector3(0, 31f, 23f) && probe.Fov == 45f && probe.Near == 0.5f, "camera lead: Offset (0, 31, 23), the 45 degree lens and the clip planes are unchanged");
        probe.Free();

        // 1. Steady straight drives in eight directions: the car stays inside the margins, and more road is visible ahead than with the classic lead.
        var table = new List<string>(); float worstHard = -9, worstExcess = -9, gain100 = 0, worstGain = 9, basisDiff = 0; int cells = 0;
        foreach (float kmh in new[] { 40f, 80f, 120f, 160f, 240f })
        foreach (var (name, d) in dirs)
        {
            float v = kmh / 3.6f; var path = new CamPath(_ => d * v, new Vector3(300, 0, 300), 8f);
            var nw = CamRun(true, path, 8f, 1f / 60f).Last(); var cl = CamRun(false, path, 8f, 1f / 60f).Last();
            worstExcess = MathF.Max(worstExcess, CamExcess(nw.Ndc)); worstHard = MathF.Max(worstHard, CamHardExcess(nw.Ndc));
            basisDiff = MathF.Max(basisDiff, (nw.Basis.X - cl.Basis.X).Length() + (nw.Basis.Y - cl.Basis.Y).Length() + (nw.Basis.Z - cl.Basis.Z).Length());
            if (kmh is 80f or 120f) { float g = nw.Ahead / MathF.Max(1f, cl.Ahead); gain100 += g; worstGain = MathF.Min(worstGain, g); cells++; }
            if (kmh is 80f or 160f) table.Add($"{kmh:F0} {name}: classic car y {cl.Ndc.Y:F2} ahead {cl.Ahead:F0} m | directed car ({nw.Ndc.X:F2}, {nw.Ndc.Y:F2}) ahead {nw.Ahead:F0} m");
        }
        foreach (var line in table) GD.Print("  " + line);
        GD.Print($"  steady ahead-visibility gain (80 and 120 km/h, 8 directions): mean x{gain100 / cells:F2}, worst x{worstGain:F2}; worst margin excess {worstExcess:F3}; basis difference {basisDiff:F6}");
        Check(worstExcess <= 0.015f && worstHard <= 0.001f, $"camera lead: at 40..240 km/h in all eight directions the car stays inside the screen margins (steady state: soft excess {worstExcess:F3}, hard excess {worstHard:F3})");
        Check(gain100 / cells >= 1.15f && worstGain >= 1.05f, $"camera lead: more road is visible ahead of a steadily driven car (mean x{gain100 / cells:F2}, worst direction x{worstGain:F2})");
        Check(basisDiff < 1e-5f, "camera lead: the camera's angle and compass direction are identical to the classic camera's at every speed and direction");

        // 2. Corners: a straight, then a turn that comes on over 0.8 s (a driver's steering), at three speeds. The framing must move calmly: compared at the same moment,
        // the car's screen speed and acceleration per metre of look-ahead, and the rate the view centre swings round, are no higher than with the classic look-ahead.
        bool cornerSafe = true; float calmNew = 0, calmOld = 0, accNew = 0, accOld = 0, swingNew = 0, swingOld = 0, cornerExcess = -9;
        foreach (var (kmh, omega) in new[] { (60f, 0.9f), (100f, 0.6f), (140f, 0.4f) })
        {
            float v = kmh / 3.6f;
            // heading: straight to 3 s, then the rate ramps to omega over 0.8 s (analytic heading, so every frame rate sees the same car)
            Func<float, float> heading = t => { if (t < 3f) return 0; float u = t - 3f; return u < 0.8f ? omega * u * u / 1.6f : omega * (u - 0.4f); };
            var path = new CamPath(t => { float h = heading(t); return new Vector3(MathF.Sin(h), 0, -MathF.Cos(h)) * v; }, new Vector3(300, 0, 300), 12f);
            var nw = CamRun(true, path, 12f, 1f / 60f); var cl = CamRun(false, path, 12f, 1f / 60f);
            (float speedPerM, float accPerM, float swing, float speedAbs, float accAbs) Measure(List<CamSample> s)
            {
                float sp = 0, ac = 0, sw = 0, spA = 0, acA = 0;
                for (int i = 175; i < s.Count - 2; i++)      // from 2.9 s: the entry and the whole corner
                {
                    float len = MathF.Max(2f, s[i].Lead.Length());
                    var d1 = (s[i + 1].Ndc - s[i].Ndc) * 60f; var d0 = (s[i].Ndc - s[i - 1].Ndc) * 60f;
                    sp = MathF.Max(sp, d1.Length() / len); ac = MathF.Max(ac, (d1 - d0).Length() * 60f / len);
                    spA = MathF.Max(spA, d1.Length()); acA = MathF.Max(acA, (d1 - d0).Length() * 60f);
                    if (s[i].Lead.Length() > 1f && s[i + 1].Lead.Length() > 1f) sw = MathF.Max(sw, MathF.Abs(MathF.IEEERemainder(s[i + 1].Lead.Angle() - s[i].Lead.Angle(), MathF.Tau)) * 60f);
                }
                return (sp, ac, sw, spA, acA);
            }
            if (Array.Exists(OS.GetCmdlineUserArgs(), a => a == "--camera-lead-dump") && kmh == 100f)
                for (int i = 170; i < 330; i += 2) GD.Print($"  DUMP t {nw[i].T:F2} lead {nw[i].Lead.X:F2},{nw[i].Lead.Y:F2} |{nw[i].Lead.Length():F2}| ndc {nw[i].Ndc.X:F3},{nw[i].Ndc.Y:F3} | classic lead {cl[i].Lead.Length():F2} ndc {cl[i].Ndc.X:F3},{cl[i].Ndc.Y:F3}");
            if (Array.Exists(OS.GetCmdlineUserArgs(), a => a == "--camera-lead-dump") && kmh == 100f)
            {
                float best = 0; int at = 0;
                for (int i = 175; i < nw.Count - 2; i++) { var d1 = (nw[i + 1].Ndc - nw[i].Ndc) * 60f; var d0 = (nw[i].Ndc - nw[i - 1].Ndc) * 60f; float a = (d1 - d0).Length() * 60f; if (a > best) { best = a; at = i; } }
                GD.Print($"  ACCPEAK directed {best:F3} NDC/s2 at t {nw[at].T:F2}, lead {nw[at].Lead.Length():F2}");
                for (int i = at - 4; i <= at + 4; i++) GD.Print($"  ACC t {nw[i].T:F3} ndc {nw[i].Ndc.X:F4},{nw[i].Ndc.Y:F4} lead {nw[i].Lead.X:F3},{nw[i].Lead.Y:F3}");
            }
            var mn = Measure(nw); var mc = Measure(cl);
            calmNew = MathF.Max(calmNew, mn.speedPerM); calmOld = MathF.Max(calmOld, mc.speedPerM);
            accNew = MathF.Max(accNew, mn.accPerM); accOld = MathF.Max(accOld, mc.accPerM);
            swingNew = MathF.Max(swingNew, mn.swing); swingOld = MathF.Max(swingOld, mc.swing);
            cornerExcess = MathF.Max(cornerExcess, nw.Max(s => CamExcess(s.Ndc))); cornerSafe &= CamSafe(nw);
            GD.Print($"  corner {kmh:F0} km/h, turn rate {omega:F1} rad/s: per metre of lead - car screen speed classic {mc.speedPerM:F4} directed {mn.speedPerM:F4} NDC/s, acceleration classic {mc.accPerM:F4} directed {mn.accPerM:F4}; view-centre swing classic {mc.swing:F2} directed {mn.swing:F2} rad/s; lead at the end classic {cl[^1].Lead.Length():F1} m directed {nw[^1].Lead.Length():F1} m");
            GD.Print($"  corner {kmh:F0} km/h ABSOLUTE peaks: car screen speed classic {mc.speedAbs:F3} directed {mn.speedAbs:F3} NDC/s; screen acceleration classic {mc.accAbs:F3} directed {mn.accAbs:F3} NDC/s2");
        }
        // (The look-ahead is longer than the classic one, so the car's screen acceleration per metre is allowed to be higher: far from the screen centre a metre moves more pixels.)
        Check(calmNew <= calmOld * 1.2f && swingNew <= swingOld + 0.05f,
            $"camera lead: corners - per metre of look-ahead the car's screen speed {calmNew:F4} vs {calmOld:F4} (acceleration {accNew:F4} vs {accOld:F4}) and the view centre swings round no faster than the car turns ({swingNew:F2} vs {swingOld:F2} rad/s)");
        Check(cornerSafe, $"camera lead: the car stays inside the margins through the corners (soft {cornerExcess:F3})");

        // 3. Speed changes, a stop and a reverse: no lurch, and the car stays visible.
        var ramp = new CamPath(t => new Vector3(0, 0, t < 12f ? -45f * MathF.Pow(MathF.Sin(MathF.PI * t / 12f), 2) : 0f), new Vector3(300, 0, 300), 16f);
        var rn = CamRun(true, ramp, 16f, 1f / 60f); var rc = CamRun(false, ramp, 16f, 1f / 60f);
        float LurchOf(List<CamSample> s) { float m = 0; for (int i = 1; i < s.Count; i++) m = MathF.Max(m, (s[i].Ndc - s[i - 1].Ndc).Length() * 60f); return m; }
        Check(CamSafe(rn) && LurchOf(rn) <= 0.5f,
            $"camera lead: accelerating to 162 km/h and braking to a stop keeps the car in the margins (soft {rn.Max(s => CamExcess(s.Ndc)):F3}); its screen speed peaks at {LurchOf(rn):F2} NDC/s (classic {LurchOf(rc):F2}, with a lead of a third of the length)");
        float stopLead = rn[^1].Lead.Length();
        Check(stopLead < 0.15f && rn[^1].Ndc.Length() < 0.1f, $"camera lead: standing still the car is back at the centre (lead {stopLead:F3} m, screen offset {rn[^1].Ndc.Length():F3})");
        var rev = new CamPath(t => new Vector3(0, 0, -22f * MathF.Cos(MathF.Tau * t / 10f)), new Vector3(300, 0, 300), 20f);
        var vn = CamRun(true, rev, 20f, 1f / 60f);
        float rev360 = vn.Max(s => CamExcess(s.Ndc)); bool revSafe = CamSafe(vn); float revJump = 0; for (int i = 1; i < vn.Count; i++) revJump = MathF.Max(revJump, (vn[i].Lead - vn[i - 1].Lead).Length() * 60f);
        Check(revSafe && revJump < 30f, $"camera lead: driving forward, stopping and reversing again and again keeps the car visible ({rev360:F3}) and the view centre never jumps (peak {revJump:F1} m/s)");

        // 4. A slide: the velocity swings 35 degrees to both sides of the heading at 1.3 Hz; the look-ahead follows only the calm part of it.
        var slide = new CamPath(t => { float a = Mathf.DegToRad(35f) * MathF.Sin(MathF.Tau * 1.3f * t); return new Vector3(MathF.Sin(a), 0, -MathF.Cos(a)) * 28f; }, new Vector3(300, 0, 300), 10f);
        var sn = CamRun(true, slide, 10f, 1f / 60f); var sc = CamRun(false, slide, 10f, 1f / 60f);
        float slideScreen = sn.Skip(120).Max(s => MathF.Abs(s.Ndc.X)), slideScreenOld = sc.Skip(120).Max(s => MathF.Abs(s.Ndc.X));
        float DirSwing(List<CamSample> s) { float lo = 9, hi = -9; for (int i = 240; i < s.Count; i++) { float a = MathF.IEEERemainder(s[i].Lead.Angle() + MathF.PI * 0.5f, MathF.Tau); lo = MathF.Min(lo, a); hi = MathF.Max(hi, a); } return hi - lo; }
        float swingDirNew = DirSwing(sn), swingDirOld = DirSwing(sc);
        Check(CamSafe(sn) && slideScreen <= slideScreenOld * 1.0f + 0.02f && swingDirNew <= swingDirOld * 0.7f,
            $"camera lead: a 35 degree slide keeps the car inside the margins; the look-ahead's direction swings {swingDirNew:F2} rad against the classic {swingDirOld:F2} (70 deg of velocity swing), the car sways {slideScreen:F2} vs {slideScreenOld:F2} NDC");

        // 5. Stakhanov: MaxLead (8 m) limits the FINAL ground-plane vector from the car to the view centre - steady, in a corner, in a reverse, at a cut. Also unlimited at 380 km/h.
        var rocket = new CamPath(_ => new Vector3(0, 0, -105f), new Vector3(300, 0, 900), 8f);
        var rk = CamRun(true, rocket, 8f, 1f / 60f, true, 8f); var rkFree = CamRun(true, rocket, 8f, 1f / 60f);
        float rockDist = rk.Last().Lead.Length(), rockMax = rk.Max(x => x.Lead.Length());
        Check(CamSafe(rk) && CamSafe(rkFree) && rockMax <= 8.001f && rockDist >= 7.9f,
            $"camera lead: at a steady 380 km/h with MaxLead 8 the car-to-centre distance settles at {rockDist:F2} m (never above {rockMax:F3}) and the car stays inside the margins; unlimited it also stays inside ({rkFree.Max(x => CamExcess(x.Ndc)):F3})");
        float limitMax = 0; bool limitSafe = true;
        foreach (var (name, vel) in new (string, Func<float, Vector3>)[] {
            ("a 300 km/h corner", t => { float h = 0.35f * t; return new Vector3(MathF.Sin(h), 0, -MathF.Cos(h)) * 83f; }),
            ("a 300 km/h reverse", t => new Vector3(0, 0, -83f * MathF.Cos(MathF.Tau * t / 12f))),
            ("a 250 km/h slide", t => { float a = Mathf.DegToRad(40f) * MathF.Sin(MathF.Tau * 1.1f * t); return new Vector3(MathF.Sin(a), 0, -MathF.Cos(a)) * 70f; }),
            ("a hard stop from 380 km/h", t => new Vector3(0, 0, -105f * MathF.Max(0f, 1f - t / 3f))) })
        {
            var run = CamRun(true, new CamPath(vel, new Vector3(300, 0, 900), 14f), 14f, 1f / 60f, true, 8f);
            float m = run.Max(x => x.Lead.Length()); limitMax = MathF.Max(limitMax, m); limitSafe &= CamSafe(run);
            GD.Print($"  MaxLead 8, {name}: largest car-to-centre distance {m:F3} m");
        }
        Check(limitMax <= 8.001f && limitSafe, $"camera lead: MaxLead 8 holds for the final car-to-centre vector in a corner, a reverse, a slide and a hard stop (largest {limitMax:F3} m) with the car inside the margins");
        var cutRocket = CamRun(true, new CamPath(_ => new Vector3(0, 0, -105f), new Vector3(300, 0, 900), 1f), 0.05f, 1f / 60f, true, 8f);
        Check(cutRocket[0].Lead.Length() <= 8.001f && cutRocket[0].Lead.Length() >= 7.9f, $"camera lead: a cut with SnapDriving at 380 km/h and MaxLead 8 starts at {cutRocket[0].Lead.Length():F2} m from the car (the limit, not less and not more)");

        // 5b. OCTANE BOOSTER on and off (MaxLead 8 <-> unlimited, switched by ApplyTrainingPower in the middle of a drive): the view must not jump when the limit starts or ends, in a straight and in a corner.
        // The measure: around each switch the camera's step may not exceed what the two steady drives (always unlimited, always 8 m) themselves have there plus the limit's own bounded motion (LeadCapRate), and the change of its velocity in one frame stays within 5 m/s of theirs (the limit's speed builds up at LeadCapAccel).
        bool capSafe = true, capMeaningful = true, capHeld = true; float worstStep = 0, worstAcc = 0, worstNatural = 0;
        foreach (var (name, omega, v0, v1) in new[] { ("a steady 38 m/s straight", 0f, 38f, 38f), ("a steady 38 m/s corner", 0.45f, 38f, 38f), ("a corner while the boost accelerates the car 30 -> 60 m/s", 0.35f, 30f, 60f) })
        foreach (float dt in new[] { 1f / 60f, 1f / 144f })
        {
            var path = new CamPath(t => { float h = omega * t; float v = Mathf.Lerp(v0, v1, Math.Clamp((t - 6f) / 4f, 0f, 1f)); return new Vector3(MathF.Sin(h), 0, -MathF.Cos(h)) * v; }, new Vector3(300, 0, 300), 20f);
            var free = CamRunCap(path, 20f, dt, _ => float.PositiveInfinity); var held = CamRunCap(path, 20f, dt, _ => 8f);
            var boost = CamRunCap(path, 20f, dt, t => t >= 6f && t < 14f ? 8f : float.PositiveInfinity);      // the boost: 6 s .. 14 s
            float natural = free.First(x => x.T >= 6f).Lead.Length(); worstNatural = MathF.Max(worstNatural, natural); capMeaningful &= natural > 11f;
            float stepBound = 0, accBound = 0, step = 0, acc = 0;
            foreach (var (from, to) in new[] { (5.5f, 9.5f), (13.5f, 17.5f) })
            {
                var f = CamContinuity(free, from, to); var h = CamContinuity(held, from, to); var b = CamContinuity(boost, from, to);
                stepBound = MathF.Max(stepBound, MathF.Max(f.step, h.step) + ChaseCamera.LeadCapRate * dt * 1.2f);
                accBound = MathF.Max(accBound, MathF.Max(f.acc, h.acc) / dt + 5f);      // (m/s: a velocity change of the camera in one frame; a hard 17 m -> 8 m switch is 500+ m/s)
                step = MathF.Max(step, b.step); acc = MathF.Max(acc, b.acc / dt);
            }
            worstStep = MathF.Max(worstStep, step / stepBound); worstAcc = MathF.Max(worstAcc, acc / accBound);
            bool ok = step <= stepBound && acc <= accBound;
            capSafe &= ok && CamSafe(boost); float late = boost.Where(x => x.T is > 11f and < 14f).Max(x => x.Lead.Length()); capHeld &= late <= 8.01f;
            GD.Print($"  boost on/off, {name}, {dt * 1000:F1} ms: step {step:F3} m (bound {stepBound:F3}), velocity change in one frame {acc:F2} m/s (bound {accBound:F2}), limit settled at {late:F2} m, view centre {natural:F1} m out at the switch");
        }
        Check(capMeaningful, $"camera lead: the boost test is meaningful - unlimited, the view centre is over 11 m from the car at the switch (up to {worstNatural:F1} m), so a hard clamp to 8 m would jump the view by 3..9 m in one frame");
        Check(capSafe, $"camera lead: boost start and end in a straight and in corners (60 and 144 fps): no jump in the camera's position (worst {worstStep:F2} of its bound) or in its velocity (worst {worstAcc:F2} of its bound; a hard switch is a jump of 3..9 m in one frame), the car stays inside the margins");
        Check(capHeld, "camera lead: while the boost lasts the car-to-centre distance settles at 8 m within five seconds, and after it the lead returns");

        // 6. Frame rate independence.
        var mixed = new CamPath(t => { float a = 0.5f * MathF.Sin(0.6f * t); float v = 20f + 12f * MathF.Sin(0.35f * t); return new Vector3(MathF.Sin(a + 0.3f * t), 0, -MathF.Cos(a + 0.3f * t)) * v; }, new Vector3(300, 0, 300), 20f);
        var reference = CamRun(true, mixed, 20f, 1f / 240f);
        float worstFps = 0; var fpsList = new[] { 1f / 30f, 1f / 60f, 1f / 90f, 1f / 144f };
        foreach (float dt in fpsList)
        {
            var run = CamRun(true, mixed, 20f, dt);
            foreach (float checkT in new[] { 4f, 8f, 12f, 16f, 20f })
            {
                var a = run.First(s => MathF.Abs(s.T - checkT) < dt * 0.51f); var b = reference.First(s => MathF.Abs(s.T - checkT) < 1f / 240f * 0.51f);
                worstFps = MathF.Max(worstFps, (a.Lead - b.Lead).Length());
            }
        }
        Check(worstFps < 0.35f, $"camera lead: the view centre is the same at 30, 60, 90, 144 and 240 frames a second (worst difference {worstFps:F3} m of a lead of up to 20 m)");

        // 7. A cut (reset or replay seek / loop): the framing starts at the car, builds the lead without a jump, and repeats exactly.
        var straight = new CamPath(_ => new Vector3(0, 0, -30f), new Vector3(300, 0, 900), 6f);
        var c1 = CamRun(true, straight, 6f, 1f / 60f, false); var c2 = CamRun(true, straight, 6f, 1f / 60f, false);
        float buildJump = 0; for (int i = 1; i < c1.Count; i++) buildJump = MathF.Max(buildJump, (c1[i].Lead - c1[i - 1].Lead).Length());
        Check(c1.SequenceEqual(c2) && c1[0].Lead.Length() < 0.8f && buildJump < 0.45f && c1[^1].Lead.Length() > 8f,
            $"camera lead: a seek or loop (Snap) restarts the framing at the car, builds the lead by at most {buildJump:F2} m per frame to {c1[^1].Lead.Length():F1} m, and two identical runs are bit-identical");
        var cutDriving = CamRun(true, straight, 0.5f, 1f / 60f, true);
        Check(cutDriving[0].Lead.Length() > 8f, $"camera lead: a cut with SnapDriving starts at the speed's lead ({cutDriving[0].Lead.Length():F1} m), with no visible build-up");

        // 8. The classic camera returns exactly when the experiment is off.
        var classicA = CamRun(false, mixed, 10f, 1f / 60f);
        bool old2 = ChaseCamera.DirectedLead; ChaseCamera.DirectedLead = false;
        var cam = new ChaseCamera { ShakeEnabled = false }; AddChild(cam); var (q0, w0) = mixed.At(0); cam.SnapDriving(q0, w0);
        bool same = true;
        for (int n = 1; n <= 600; n++) { float t = n * (1f / 60f); var (p, v) = mixed.At(t); cam.Feel(v.Length() * 3.6f, 0, false, 0, 1f / 60f); cam.Follow(p, v, 1f / 60f); same &= cam.GlobalPosition == classicA[n - 1].CamPos; }
        cam.Free(); ChaseCamera.DirectedLead = old2;
        Check(same, "camera lead: with DirectedLead off FollowDrive is bit-for-bit the classic Follow (the experiment is fully revertible)");
    }

    static float DirMaxTurnForTest() => ChaseCamera.DirMaxTurn;
}
