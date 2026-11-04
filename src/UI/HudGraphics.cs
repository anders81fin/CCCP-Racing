using Godot;

namespace CCCPRacing.UI;

public partial class Hud
{
    public bool CapitalistLuxuriesEnabled;

    void DrawCapitalistLuxuriesNotice()
    {
        if (!CapitalistLuxuriesEnabled || Clean || LoadingTrack || (int)(_clock * 2) % 2 != 0) return;
        bool inGame = Mode is HudMode.Race or HudMode.Countdown or HudMode.Paused or HudMode.Watch;
        if (!inGame && Mode is not (HudMode.Title or HudMode.Files or HudMode.Board or HudMode.Lessons or HudMode.Changelog or HudMode.Lan)) return;
        float y = inGame ? 20 : 2;      // in the game under the radio box (y 4-17)
        const string text = "CAPITALIST LUXURIES ENABLED!";
        float width = PixelFont.Measure(text) + 12;
        DrawRect(new Rect2(Mathf.Floor((Screen.X - width) / 2), y, width, 13), new Color(0, 0, 0, 0.85f));
        PixelFont.DrawCentered(this, text, Screen.X / 2, y + 3, Red);
    }

    public bool CameraUnlocksMissing, CameraHintOpen, CameraSomeFound;

    public void SetCameraHintOpen(bool open)
    {
        CameraHintOpen = open;
        _menuTextClock = 0;
    }

    void DrawCameraHint()
    {
        float width = Mathf.Min(280, Screen.X - 12);
        var r = new Rect2(Mathf.Floor((Screen.X - width) / 2), Mathf.Floor(Screen.Y / 2 - 64), width, 128);
        Panel(r, 1f);
        string heading = CameraSomeFound ? "MORE CAMERAS HIDDEN" : "HIDDEN CAMERAS";
        DrawHeading(heading, Screen.X / 2, r.Position.Y + 10, _menuTextClock,
            PixelFont.Measure(heading, 2) <= width - 12 ? 2 : 1);
        string[] lines = CameraSomeFound
            ? new[] { "NOT ALL CAMERA ANGLES", "HAVE BEEN FOUND ON THIS TRACK.", "MORE HIDING PLACES REMAIN.", "EXPLORE OFF THE RACING LINE." }
            : new[] { "THIS TRACK HIDES PLACES", "THAT UNLOCK NEW CAMERA ANGLES.", "YOU HAVE NOT FOUND THEM YET.", "EXPLORE OFF THE RACING LINE." };
        for (int i = 0; i < lines.Length; i++)
            PixelFont.DrawCentered(this, lines[i], Screen.X / 2, r.Position.Y + 38 + i * 14, Paper);
        PixelFont.DrawCentered(this, P("ESC / ENTER  BACK", "B / A  BACK"), Screen.X / 2, r.End.Y - 14, Gold);
    }
}
