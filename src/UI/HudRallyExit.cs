using Godot;

namespace CCCPRacing.UI;

public partial class Hud
{
    public bool RallyExitOpen;
    public void SetRallyExitOpen(bool open)
    {
        RallyExitOpen = open;
        _menuTextClock = 0;
    }

    public int RallyExitSelection;
    public string RallyExitError;

    public int RallyExitRowAt(Vector2 pos)
    {
        var r = RallyExitRect;
        if (pos.X < r.Position.X + 4 || pos.X >= r.End.X - 4) return -1;
        int row = (int)Mathf.Floor((pos.Y - r.Position.Y - 76) / 14);
        return row >= 0 && row < 3 ? row : -1;
    }

    Rect2 RallyExitRect => new(Mathf.Floor(Screen.X / 2 - 150), Mathf.Floor(Screen.Y / 2 - 72), 300, 144);

    void DrawRallyExit()
    {
        var r = RallyExitRect;
        Panel(r, 1f);
        DrawHeading("CONTINUE NEXT TIME?", Screen.X / 2, r.Position.Y + 10, _menuTextClock);
        PixelFont.DrawCentered(this, "RETURN TO THIS RALLY WHEN YOU PICK", Screen.X / 2, r.Position.Y + 34, Paper);
        PixelFont.DrawCentered(this, "THIS TRACK AGAIN?", Screen.X / 2, r.Position.Y + 44, Paper);
        PixelFont.DrawCentered(this, "SAME NORM AND LAPS - FROM THE START LINE", Screen.X / 2, r.Position.Y + 60, Grey);
        string[] options = { "YES - SAVE AND LEAVE", "NO - LEAVE WITHOUT SAVING", "CANCEL - BACK TO PARTY MEETING" };
        for (int i = 0; i < options.Length; i++)
        {
            float y = r.Position.Y + 78 + i * 14;
            bool selected = i == RallyExitSelection;
            if (selected) DrawRect(new Rect2(r.Position.X + 4, y - 2, r.Size.X - 8, 12), new Color(Red, 0.55f));
            PixelFont.DrawCentered(this, options[i], Screen.X / 2, y, selected ? Gold : Paper);
        }
        if (RallyExitError != null) PixelFont.DrawCentered(this, RallyExitError, Screen.X / 2, r.End.Y - 26, Red);
        PixelFont.DrawCentered(this, P("UP/DOWN CHOOSE  ENTER SELECT  ESC BACK", "D-PAD CHOOSE  A SELECT  B BACK"), Screen.X / 2, r.End.Y - 12, Grey);
    }
}
