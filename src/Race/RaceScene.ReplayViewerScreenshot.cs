using System;
using System.IO;
using System.Linq;
using Godot;

namespace CCCPRacing.Race;
public partial class RaceScene
{
    public sealed partial class ReplayViewerSession
    {
        internal string LastScreenshot { get; private set; }
        internal string ScreenshotError { get; private set; }
        internal bool ScreenshotBusy { get; private set; }

        async void SaveViewerScreenshot()
        {
            if (!UiReady || ScreenshotBusy) return;
            ScreenshotBusy = true; ScreenshotError = null;
            try
            {
                // Capture after camera placement has reached the renderer. The viewer's own
                // SubViewport contains the world only; menus/controls are on the parent.
                await _live.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                if (!UiReady || !IsInstanceValid(_scene)) return;
                using var image = _scene.GetViewport().GetTexture().GetImage();
                if (image == null || image.IsEmpty()) throw new IOException("NO RENDERED IMAGE");
                var png = image.SavePngToBuffer();
                if (png.Length == 0) throw new IOException("PNG ENCODING FAILED");
                string dir = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--screenshot-dir="))?["--screenshot-dir=".Length..]
                    ?? (OS.HasFeature("editor") ? ProjectSettings.GlobalizePath("res://media/replay-screenshots") : Path.Combine(OS.GetUserDataDir(), "screenshots"));
                Directory.CreateDirectory(dir);
                string name = $"{_track}-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid().ToString("N")[..8]}.png";
                string path = Path.GetFullPath(Path.Combine(dir, name));
                using (var file = new FileStream(path, FileMode.CreateNew, System.IO.FileAccess.Write, FileShare.Read)) file.Write(png);
                LastScreenshot = path;
                SetNotice("SCREENSHOT SAVED: " + path, 8f);
                GD.Print("Replay screenshot: " + path);
            }
            catch (Exception ex)
            {
                ScreenshotError = ex.Message;
                if (_state == ReplayViewerState.Playing) SetNotice("SCREENSHOT FAILED: " + ex.Message, 8f);
                GD.PrintErr("Replay screenshot failed: " + ex.Message);
            }
            finally { ScreenshotBusy = false; }
        }
    }
}
