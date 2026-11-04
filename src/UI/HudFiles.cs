using Godot;

namespace CCCPRacing.UI;

/// <summary>The KGB ARCHIVE (RaceScene.Files.cs): five personnel files, their drivers, medals, KGB file and last run.</summary>
public partial class Hud
{
    /// <summary>A file's row: in use at all, the driver, each track's medal (0 none .. 3 gold), last played, the file in use.</summary>
    public readonly record struct FileRow(bool Used, string Name, int[] Medals, string Played, bool InUse, int Cameras = 0);

    public FileRow[] Files = System.Array.Empty<FileRow>();
    public int FilesSelection;
    public bool FilesErase;
    /// <summary>The archive at launch: Esc quits (it is the top), the driver is asked to open a file.</summary>
    public bool FilesAtLaunch;
    /// <summary>The title's corner: the file in use.</summary>
    public string FileLabel = "";

    static readonly Color Silver = new(0.8f, 0.82f, 0.86f), Bronze = new(0.78f, 0.48f, 0.22f), NoMedal = new(0.3f, 0.29f, 0.27f);

    void DrawFiles()
    {
        var s = Screen;
        float cx = s.X / 2;
        DrawRect(new Rect2(0, 0, s.X, s.Y), new Color(0, 0, 0, 0.35f * StartupBackdropOpacity));
        DrawTitleBanner();
        if (StartupArchivePending) return;
        const int RowH = 16;
        float w = 330, h = 40 + Files.Length * RowH + 30;
        var r = new Rect2(Mathf.Floor(cx - w / 2), Mathf.Floor((s.Y - h) / 2 + 14), w, h);
        DrawRect(r, new Color(0.05f, 0.03f, 0.03f, 0.88f));
        DrawRect(r, new Color(Red, 0.9f), false, 1f);
        DrawTypewriter("KGB ARCHIVE", cx, r.Position.Y + 7, Gold, _menuTextClock, HeadingRate, 2, HeadingShadow);
        PixelFont.DrawCentered(this, FilesAtLaunch ? "OPEN YOUR FILE, TOVARISH, OR A NEW ONE" : "PERSONNEL FILES - THE PARTY KNOWS EVERY DRIVER",
            cx, r.Position.Y + 25, Grey);
        float x0 = r.Position.X + 14, y = r.Position.Y + 40;
        for (int i = 0; i < Files.Length; i++, y += RowH)
        {
            var f = Files[i];
            bool sel = i == FilesSelection;
            if (sel) DrawRect(new Rect2(r.Position.X + 4, y - 4, w - 8, RowH - 1), new Color(Red, FilesErase ? 0.85f : 0.55f));
            Color c = sel ? Gold : f.Used ? Paper : Grey;
            PixelFont.Draw(this, $"{i + 1}.", new Vector2(x0, y), c);
            PixelFont.Draw(this, f.Used ? (f.Name.Length > 0 ? f.Name : Race.Leaderboard.DefaultName) : "- NEW FILE -", new Vector2(x0 + 16, y), c);
            if (!f.Used) continue;
            // Each track's medal as a star.
            float mx = x0 + 16 + 13 * PixelFont.Advance;
            for (int k = 0; k < f.Medals.Length; k++)
                DrawStar(new Vector2(mx + k * 10 + 3, y + 3), 1, f.Medals[k] switch { 3 => Gold, 2 => Silver, 1 => Bronze, _ => NoMedal });
            if (f.Cameras > 0) PixelFont.Draw(this, "C:" + f.Cameras, new Vector2(mx + 48, y), sel ? Gold : Grey);
            PixelFont.Draw(this, f.Played, new Vector2(r.End.X - 14 - PixelFont.Measure(f.Played), y), sel ? Gold : Grey);
            if (f.InUse) PixelFont.Draw(this, "<", new Vector2(r.End.X - 10, y), Gold);
        }
        string foot = FilesErase
            ? P("ERASE FROM HISTORY?  ENTER YES  ESC NO", "ERASE FROM HISTORY?  A YES  B NO")
            : FilesAtLaunch ? P("UP/DOWN CHOOSE  ENTER OPEN  DEL ERASE  ESC QUIT", "D-PAD CHOOSE  A OPEN  Y ERASE  B QUIT")
            : P("UP/DOWN CHOOSE  ENTER OPEN  DEL ERASE  ESC BACK", "D-PAD CHOOSE  A OPEN  Y ERASE  B BACK");
        if (!FilesErase || (int)(_clock * 2) % 2 == 0)
            PixelFont.DrawCentered(this, foot, cx, r.End.Y - 14, FilesErase ? Red : Grey);
        DrawMenuNote(cx, Mathf.Floor(r.GetCenter().Y - 12));      // FILE OPENED, ... WAS NEVER HERE: over the window's middle
    }
}
