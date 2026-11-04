using System;
using CCCPRacing.Camera;
using CCCPRacing.Replay;
using CCCPRacing.Export;
using Godot;

namespace CCCPRacing.Tests;
public partial class PhysicsTest
{
    void CameraMotionTests()
    {
        Transform3D Straight(float t) => new(Basis.Identity, new Vector3(t * 10, 0, 0));
        Transform3D Curve(float t) => new(Basis.Identity, new Vector3(30 * Mathf.Sin(t / 3), 0, 30 * (1 - Mathf.Cos(t / 3))));
        var shot = new LapFreeCamera { X = 20, Y = 12, Z = -15, Fov = 60, Look = true };
        ReplayCameraMotion.Frame Eval(LapCameraMotion m, float t, Func<float, Transform3D> path = null) => ReplayCameraMotion.Evaluate(shot with { Motion = m }, 2, t, path ?? Straight);
        var fixedMotion = new LapCameraMotion();
        Check(Eval(fixedMotion, 5).Pose.Origin == new Vector3(20, 12, -15) && Eval(fixedMotion, 5).Fov == 60, "motion: fixed has no automatic move or zoom");
        var crane = fixedMotion with { Rise = 20, Duration = 4 };
        Check(Eval(crane, 2).Pose.Origin.Y == 12 && Eval(crane, 4).Pose.Origin.Y == 22 && Eval(crane, 7).Pose.Origin.Y == 32, "motion: crane eases from anchor and holds endpoint");
        var zoom = fixedMotion with { EndFov = 30, Duration = 4 };
        Check(Eval(zoom, 4).Fov == 45 && Eval(zoom, 8).Fov == 30 && Eval(zoom, 4).Pose.Origin == Eval(fixedMotion, 4).Pose.Origin, "motion: explicit zoom changes lens only");
        var dolly = fixedMotion with { Dolly = 8, Duration = 4 };
        Check(Mathf.Abs(Eval(dolly, 4).Pose.Origin.DistanceTo(Eval(fixedMotion, 4).Pose.Origin) - 4) < .001f && Eval(dolly, 4).Fov == 60, "motion: dolly physically approaches without changing lens");
        var retreat = fixedMotion with { Dolly = -8, Duration = 4 };
        Check(Eval(retreat, 4).Pose.Origin.DistanceTo(Straight(4).Origin) > Eval(fixedMotion, 4).Pose.Origin.DistanceTo(Straight(4).Origin), "motion: negative dolly retreats");
        var side = fixedMotion with { Tracking = LapCameraMotion.Side };
        var heli = side with { Tracking = LapCameraMotion.Helicopter };
        var world = side with { Tracking = LapCameraMotion.World };
        Check(Eval(side, 2, Curve).Pose.Origin.DistanceTo(new Vector3(20, 12, -15)) < .0001f && Eval(heli, 2, Curve).Pose.Origin.DistanceTo(new Vector3(20, 12, -15)) < .0001f, "motion: tracking starts at precisely the placed framing");
        var ps = Eval(side, 6, Curve).Pose.Origin; var pw = Eval(world, 6, Curve).Pose.Origin;
        Check(ps.DistanceTo(pw) > 5, "motion: relative side follows travel direction through a bend, independent of fixed body yaw");
        Check(Eval(heli, 6, Curve).Pose.Origin.DistanceTo(ps) > .1f, "motion: helicopter has a distinct softer recorded-history response");
        var p = Eval(heli, 6, Curve); Eval(heli, 15, Curve); Eval(heli, 1, Curve);
        Check(Eval(heli, 6, Curve) == p, "motion: forward/back seeks do not change pose or lens");
        Check((-p.Pose.Basis.Z).Dot((Curve(6).Origin - p.Pose.Origin).Normalized()) > .999f, "motion: independent look-at keeps moving subject in frame");
        for (int i = 0; i < 3; i++)
        {
            var m = heli with { Rise = 10, Dolly = 3, EndFov = 40 };
            var frame = Eval(m, new[] { 2f, 4f, 9f }[i], Curve);
            Check(frame.Pose.IsFinite() && frame.Fov <= 60 && frame.Fov >= 40, "motion: composed crane/tracking/dolly/zoom remains finite");
        }
        var noAim = shot with { Look = false, Motion = heli };
        var a = ReplayCameraMotion.Evaluate(noAim, 2, 2, Curve); var b = ReplayCameraMotion.Evaluate(noAim, 2, 8, Curve);
        Check(a.Pose.Basis.IsEqualApprox(b.Pose.Basis), "motion: manual orientation stays independent of tracking");
        Transform3D Stopped(float t) => Curve(Math.Min(t, 6));
        Check(Eval(side, 12, Stopped).Pose.Origin.DistanceTo(Eval(side, 8, Stopped).Pose.Origin) < .001f,
            "motion: a stopped car retains its final travel heading after smoothing settles");
        var request = new ExportRequest { InClock = 3, OutClock = 5, CameraKeys = new() { new(2, LapEdits.Free, shot with { Motion = heli }) } };
        var parsed = ExportPlan.FromJson(new ExportPlan { Request = request }.ToJson());
        Check(parsed.Request.HasCameraMotion && parsed.Request.CameraKeys[0].Free.Motion == heli && parsed.Request.ValidateCameras(10) == null, "motion: complete behavior survives export serialization including key before IN");
        bool Refused(string j) { try { ExportPlan.FromJson(j); return false; } catch { return true; } }
        Check(Refused(new ExportPlan { Version = 2, Request = request }.ToJson()), "motion: legacy renderer plan cannot silently ignore animation");
        foreach (var bad in new[] { heli with { Duration = 0 }, heli with { Duration = float.NaN }, heli with { Tracking = "RearDrone" }, heli with { Rise = float.PositiveInfinity }, heli with { Dolly = 500 }, heli with { EndFov = 180 } })
            Check(bad.Validate() != null, "motion: invalid behavior refused");
    }
}
