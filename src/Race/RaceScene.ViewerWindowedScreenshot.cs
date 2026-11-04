using System.Collections;
using System.IO;
using System.Linq;
using Godot;

namespace CCCPRacing.Race;
public partial class RaceScene
{
    IEnumerable VwFreeScreenshot(ReplayViewerSession s, RaceScene v)
    {
        float clock = v.ViewerClock; var pose = v.ViewerCameraPose;
        foreach (var x in VrTap(Key.H)) yield return x;
        VwCheck(!s.OverlayView.Shown && s.FreeCameraActive, "H hides controls without leaving free camera");
        foreach (var x in VrTap(Key.H)) yield return x;
        VwCheck(s.OverlayView.Shown && s.FreeCameraActive, "H shows free camera controls again");
        byte[] reference = null;
        void CaptureReference()
        {
            RenderingServer.FramePostDraw -= CaptureReference;
            using var image = v.GetViewport().GetTexture().GetImage(); image.Convert(Image.Format.Rgba8); reference = image.GetData();
        }
        RenderingServer.FramePostDraw += CaptureReference;
        foreach (var x in VrTap(Key.F12)) yield return x;
        foreach (var x in VrUntil(() => !s.ScreenshotBusy, 300, "screenshot")) yield return x;
        string first = s.LastScreenshot;
        VwCheck(first != null && File.Exists(first) && s.ScreenshotError == null, "F12 writes a PNG in the configured screenshot directory");
        if (first != null)
        {
            using var png = Image.LoadFromFile(first); png.Convert(Image.Format.Rgba8);
            VwCheck(reference != null && png.GetData().SequenceEqual(reference), "screenshot pixels match the world viewport exactly, without visible free-camera controls");
        }
        foreach (var x in VrTap(Key.H)) yield return x;
        foreach (var x in VrTap(Key.F12)) yield return x;
        foreach (var x in VrUntil(() => !s.ScreenshotBusy, 300, "hidden screenshot")) yield return x;
        VwCheck(!s.OverlayView.Shown && s.LastScreenshot != first && File.Exists(first) && File.Exists(s.LastScreenshot),
            "F12 also works with hidden controls and never replaces the earlier capture");
        foreach (var x in VrTap(Key.H)) yield return x;
        VwCheck(v.ViewerPaused && v.ViewerClock == clock && v.ViewerCameraPose.IsEqualApprox(pose) && !s.Dirty,
            "screenshots preserve paused clock, camera placement and saved edits");
    }
}
