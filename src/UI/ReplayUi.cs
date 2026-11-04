using System;

namespace CCCPRacing.UI;

// The data the replay screens draw (docs/work-orders/player-replay.md phase 3). The race scene (RaceScene.ReplayMenu.cs) fills these in; the draw files
// (HudReplay.cs: the list inside the Party Meeting, ReplayOverlay.cs: the viewer's overlay) read them and own no game state. All texts are prepared, upper-case ASCII.

/// <summary>One lap in the list: a title line ("ILYICH RALLY  1:23.45"), a detail line ("RECORD  14:30  KEPT") and a flag for what is wrong with it, if anything.</summary>
public sealed class ReplayRowView
{
    public string Title = "", Detail = "", Flag = "";      // Flag: "" (fine), "PREPARING", "NO WORLD TAPE", "UNAVAILABLE" ...
    public bool Selectable = true;                           // false: shown dimmed, cannot be opened
}

/// <summary>The list screen of the Party Meeting. Empty Rows with a Notice is the empty state ("NO LAPS RECORDED YET").</summary>
public sealed class ReplayListView
{
    public ReplayRowView[] Rows = Array.Empty<ReplayRowView>();
    public int Selected;
    public string Notice = "";          // one readable line under the list (an error, a cancel, "OPENING..."), or ""
    public bool NoticeIsError;
    public string Heading = "REPLAY";   // "REPLAY" in the Party Meeting, "RECORDINGS" in the Party Office
    public bool AllowKeep = true;       // the footer offers KEEP (K / Y)
}

/// <summary>The viewer's overlay: the timeline, the controls in force and the hints; or the loading progress while the viewer world builds.</summary>
public sealed class ReplayOverlayView
{
    public bool Shown = true;                 // false: the overlay toggle hid everything except a one-line "H SHOW CONTROLS" hint
    public bool Building;                      // the world is still building: draw LOADING + progress + the cancel hint, nothing else
    public float BuildProgress;                // 0..1
    public string Title = "";                 // "ILYICH RALLY 1:23.45 RECORD"
    public float Clock, Duration;             // seconds
    public bool Paused, Ended;
    public float Speed = 1f;                   // 0.25, 0.5, 1, 2
    public string Camera = "GAMEPLAY";       // "GAMEPLAY", "BONNET", "AUTO", ...
    public bool FreeCamera, FreeFollow, FreeLook;
    public Godot.Vector3 FreePosition;               // the free camera's world position
    public float FreeHeading, FreePitch;       // degrees: compass heading 0-359 (0 = -Z, 90 = +X), pitch -90..90 (0 = level)
    public bool PadMode;                       // hints in pad buttons
    public string Notice = "";                // short transient line ("NO WORLD TAPE: CAR ONLY"), or ""
    // Saved editorial choices (phase 4B). All off/empty for a lap that cannot be edited or has no edits.
    public bool Editable;                      // a kept lap: the EDIT hint shows
    public float WindowIn = -1, WindowOut = -1;   // the cut window in seconds (-1: open end); drawn on the timeline
    public bool TrimOn = true;
    public float[] KeyTimes = System.Array.Empty<float>();   // camera key ticks on the timeline
    public bool Dirty;                         // unsaved edits (a * in the title)
    public bool Pinned;
    public bool EditOpen;                      // the EDIT panel
    public string[] EditRows = System.Array.Empty<string>();
    public int EditSelected;
    public string[] Prompt = System.Array.Empty<string>();   // a modal question (UNSAVED EDITS / RESET EDITS), drawn instead of the controls
    // The MP4 export card / progress panel (src/Export/ExportUiController.cs fills these for the SP and the LAN viewer alike).
    public string ExportTitle = "";
    public string[] ExportRows = System.Array.Empty<string>();      // the settings rows (empty while only lines show)
    public int ExportSelected;
    public string[] ExportLines = System.Array.Empty<string>();     // progress / question / result lines
    public float ExportFraction = -1f;                              // 0..1 progress bar, -1: none
}
