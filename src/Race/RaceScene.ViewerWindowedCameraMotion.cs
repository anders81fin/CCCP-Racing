using System;
using System.Collections;
using CCCPRacing.Replay;
using Godot;

namespace CCCPRacing.Race;
public partial class RaceScene
{
    IEnumerable VwConfigureMotion(ReplayViewerSession s, RaceScene v)
    {
        float clock = v.ViewerClock;
        foreach (var x in VrTap(Key.B)) yield return x;
        VwCheck(s.CameraMotionOpen && s.OverlayView.ExportTitle.Contains("CAMERA MOTION"), "B opens composable camera motion card");
        foreach (var x in VrTap(Key.Right)) yield return x; // World -> Side
        foreach (var x in VrTap(Key.Down)) yield return x; // aim
        foreach (var x in VrTap(Key.Down)) yield return x; // duration
        foreach (var x in VrTap(Key.Left)) yield return x; // 5.5 sec
        foreach (var x in VrTap(Key.Down)) yield return x; // crane
        foreach (var x in VrTap(Key.Right)) yield return x;
        foreach (var x in VrTap(Key.Right)) yield return x; // 4m
        foreach (var x in VrTap(Key.Down)) yield return x; // dolly
        foreach (var x in VrTap(Key.Right)) yield return x; // 2m
        foreach (var x in VrTap(Key.Down)) yield return x;
        foreach (var x in VrTap(Key.Down)) yield return x; // end lens
        foreach (var x in VrTap(Key.Left)) yield return x;
        var m = s.CameraMotionDraft;
        VwCheck(m?.Tracking == LapCameraMotion.Side && m.Duration == 5.5f && m.Rise == 4 && m.Dolly == 2 && m.EndFov == v.ViewerCameraFov - 5,
            "UI composes relative tracking, duration, crane, dolly and explicit lens");
        VwCheck(Mathf.Abs(v.ViewerClock - clock) < .001f && s.FreeCameraActive, "motion card arrows never seek or leave free controls");
        foreach (var x in VrShot("camera-motion-card")) yield return x;
        foreach (var x in VrTap(Key.Escape)) yield return x;
        VwCheck(!s.CameraMotionOpen && s.FreeCameraActive, "ESC closes motion card to placed camera, not the viewer");
    }
}
