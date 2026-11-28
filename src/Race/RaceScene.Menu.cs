using System;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// The title menu's nested rows. The top row: SINGLE PLAYER | MULTIPLAYER | PARTY OFFICE (SETTINGS is only in the Party Meeting, the pause menu: the owner's order, 2026-11-28);
/// Enter opens a row (DRIVING SCHOOL | SOCIALIST RALLY | STAKHANOV LAPS; LAN / INTERNET;
/// KGB FILES | CREDITS | RESIGN; KGB FILES is the archive itself (no row of its own);
/// the DRIVING SCHOOL teaches car control on ILYICH'S LAMP and comes back here, a rally's own school
/// is offered before a rally on a track that has one, Stakhanov laps have none),
/// Esc goes back up one level with the parent selected (on the top row Esc opens PARTY OFFICE with RESIGN selected). Left/Right move
/// along a row, Down/Up select and leave the replay line at every level. Back from a run the top row
/// has the parent of the mode played last, and Enter opens its row on that mode. In a mode row a
/// hint box in the middle of the screen says what the selected mode is.
/// </summary>
public partial class RaceScene
{
    enum MenuItem { Race, Training, Lan, Board, Single, Multi, Quit, School, Files, Office, Credits, Custom }

    static readonly MenuItem[] TopItems = { MenuItem.Single, MenuItem.Multi, MenuItem.Office };
    static readonly MenuItem[] SingleItems = { MenuItem.School, MenuItem.Race, MenuItem.Training, MenuItem.Custom };
    static readonly MenuItem[] MultiItems = { MenuItem.Lan };
    static readonly MenuItem[] OfficeItems = { MenuItem.Files, MenuItem.Credits, MenuItem.Quit };
    static readonly string[] TopLabels = { "SINGLE PLAYER", "MULTIPLAYER", "PARTY OFFICE" }, TopShort = { "1 PLAYER", "MULTI", "OFFICE" };
    static readonly string[] OfficeLabels = { "KGB FILES", "CREDITS", "RESIGN" }, OfficeShort = { "FILES", "CREDITS", "RESIGN" };
    static readonly string[] SingleLabels = { "DRIVING SCHOOL", Hud.RaceLabel, Hud.TrainingLabel, Hud.CustomLabel }, SingleShort = { "SCHOOL", Hud.RaceShort, Hud.TrainingShort, Hud.CustomShort };
    static readonly string[] MultiLabels = { Hud.LanLabel }, MultiShort = { Hud.LanShort };

    MenuItem _menu = MenuItem.Single;      // a first launch: SINGLE PLAYER, and in it SOCIALIST RALLY (the owner's order, 2026-11-19)
    MenuItem? _menuParent;                  // the open row's parent (null: the top row)
    MenuItem _lastPlayed = MenuItem.Race;      // (a returning player: the mode played last, kept between launches in settings.cfg [menu] last)
    bool _menuMemoryLoaded;
    bool _menuSingleTest;

    MenuItem[] MenuRow => _menuParent switch { MenuItem.Single => SingleItems, MenuItem.Multi => MultiItems, MenuItem.Office => OfficeItems, _ => TopItems };

    static MenuItem ParentOf(MenuItem m) => m switch
    {
        MenuItem.Training or MenuItem.Race or MenuItem.School or MenuItem.Custom => MenuItem.Single,
        MenuItem.Lan => MenuItem.Multi,
        MenuItem.Board or MenuItem.Files or MenuItem.Credits or MenuItem.Quit => MenuItem.Office,
        _ => m,
    };

    /// <summary>The title's menu after EnterTitle: the top row, on the parent of the mode played last.</summary>
    void ResetMenu()
    {
        if (!_menuMemoryLoaded && _records != null && !ViewerMode) { _menuMemoryLoaded = true; _lastPlayed = MenuMemory.Load(_records.Dir, _lastPlayed); }
        _menuParent = null;
        _menu = ParentOf(_lastPlayed);
    }

    /// <summary>A mode chosen from the title (or started by a flag): the menu comes back to it, and the next launch finds it there.</summary>
    void MenuPlayed(MenuItem mode)
    {
        if (mode != _lastPlayed && _records != null && !ViewerMode && InstructionTrack == null) MenuMemory.Save(_records.Dir, mode);
        _lastPlayed = mode;
    }

    /// <summary>Opens a row (Single Player always opens on SOCIALIST RALLY; other rows use the last mode or first).</summary>
    void OpenMenu(MenuItem parent, MenuItem? at = null)
    {
        if (parent == MenuItem.Multi)      // MULTIPLAYER has no row of its own: it is the LAN race's start (EnterLanMenu); coming back stands on MULTIPLAYER in the top row
        {
            _menuParent = null; _menu = MenuItem.Multi;
            return;
        }
        _menuParent = parent;
        var row = MenuRow;
        MenuItem pick = at ?? (parent == MenuItem.Single ? MenuItem.Race : _lastPlayed);      // (SINGLE PLAYER always opens on SOCIALIST RALLY, whatever was played last: the owner, 2026-11-28; a run's way back passes its own mode in `at`)
        _menu = Array.IndexOf(row, pick) >= 0 ? pick : row[0];
    }

    /// <summary>Title Enter on the selected item.</summary>
    void MenuEnter()
    {
        switch (_menu)
        {
            case MenuItem.Multi: MenuPlayed(MenuItem.Lan); EnterLanMenu(); break;      // MULTIPLAYER goes straight to the LAN race's start
            case MenuItem.Single or MenuItem.Office:
                OpenMenu(_menu, _menu == MenuItem.Office ? MenuItem.Files : null);
                break;
            case MenuItem.Files: EnterFiles(); break;     // KGB FILES is the archive itself (RaceScene.Files.cs): the personnel files, their recordings and galleries
            case MenuItem.Custom: OpenCustomTracks(); break;      // RaceScene.CustomTracks.cs: no fuel or time card, no medals
            case MenuItem.Credits: EnterCredits(); break;
            case MenuItem.Lan: MenuPlayed(MenuItem.Lan); EnterLanMenu(); break;
            case MenuItem.Quit: AskResignation(); break;      // RaceScene.Resign.cs
            case MenuItem.School:
                MenuPlayed(MenuItem.School);
                EnterLessons();           // the lessons' tree (RaceScene.SchoolMenu.cs)
                break;
            case MenuItem.Training or MenuItem.Race:
                if (_menu == MenuItem.Race) OpenRallyStart();      // the rally: CHOOSE FUEL, then CHOOSE TRACK
                else if (_menu == MenuItem.Training) OpenLapsStart();      // the Stakhanov laps: the same, then the time of day
                else OpenTrackCard(_menu);     // CHOOSE TRACK (RaceScene.Tracks.cs): laps go on the board under the file's name
                break;
        }
    }

    /// <summary>Title Esc: out of a row one level up (true; KGB FILES goes to PARTY OFFICE), or nothing to go back to (false: quit).</summary>
    bool MenuBack()
    {
        if (_menuParent is not MenuItem parent) return false;
        _menuParent = Array.IndexOf(TopItems, parent) >= 0 ? null : ParentOf(parent);
        _menu = parent;
        return true;
    }

    void MoveMenu(int step)
    {
        var row = MenuRow;
        int i = Math.Max(0, Array.IndexOf(row, _menu));
        _menu = row[Math.Clamp(i + step, 0, row.Length - 1)];
    }

    void MenuHud()
    {
        // Back from a PARTY OFFICE screen opened elsewhere (the leaderboard after a lap): stand on its row's parent.
        for (int up = 0; up < 3 && _menuParent == null && Array.IndexOf(TopItems, _menu) < 0; up++) _menu = ParentOf(_menu);
        var row = MenuRow;
        bool top = _menuParent == null;
        _hud.MenuLabels = top ? TopLabels : _menuParent switch { MenuItem.Single => SingleLabels, MenuItem.Office => OfficeLabels, _ => MultiLabels };
        _hud.MenuShort = top ? TopShort : _menuParent switch { MenuItem.Single => SingleShort, MenuItem.Office => OfficeShort, _ => MultiShort };
        _hud.MenuHeader = top ? null : MenuHeaderLabel(_menuParent.Value);
        _hud.MenuDim = -1;
        _hud.TitleMenuHidden = _titleMenuHidden;
        _hud.MenuSelection = Math.Max(0, Array.IndexOf(row, _menu));
        _hud.MenuHint = _askName ? null : top ? TopHint(_menu) : MenuHint(_menu);   // over a card too: the card goes under it
    }

    /// <summary>A row's header: its parent's own label (a top-row item, or KGB FILES in PARTY OFFICE).</summary>
    static string MenuHeaderLabel(MenuItem parent)
    {
        int i = Array.IndexOf(TopItems, parent);
        if (i >= 0) return TopLabels[i];
        i = Array.IndexOf(OfficeItems, parent);
        return i >= 0 ? OfficeLabels[i] : null;
    }

    /// <summary>The hint of a top-row item that has no row of its own: MULTIPLAYER (the LAN race's explanation) and PARTY OFFICE (what is in the archive). SINGLE PLAYER shows its modes' hints one level down.</summary>
    (string title, string[] lines)? TopHint(MenuItem m) => m switch
    {
        MenuItem.Multi => MenuHint(MenuItem.Lan),
        MenuItem.Office => ("PARTY OFFICE", new[] { "DRIVER FILES WITH REPLAYS,", "CREDITS AND EXIT GAME." }),
        _ => null,
    };

    /// <summary>What a mode is: explanatory lines; the shared HUD leaves a blank row below the heading.</summary>
    (string title, string[] lines)? MenuHint(MenuItem m) => m switch
    {
        MenuItem.Training => (Hud.TrainingLabel, new[]
        {
            "TRACK PRACTICE ON AN EMPTY ROAD.",
            "NO DAMAGE, NO FUEL, NO NORMS. JUST SPEED.",
            "BEAT THE RECORD ON THE BOARD OF HONOUR.",
        }),
        MenuItem.Race => (Hud.RaceLabel, new[]
        {
            "A PEASANT CHOSEN TO BECOME A HERO OF THE STATE.",
            "BEAT LAP NORMS TO EARN MEDALS.",
            "BRUTAL QUALIFYING. THE WINNER TAKES IT ALL.",
            "THE GULAG HAS ROOM FOR THE REST.",
        }),
        MenuItem.Custom => (Hud.CustomLabel, new[]
        {
            "TRACKS MADE BY PLAYERS AND THEIR AI AGENTS.",
            "FREE LAPS: NO MEDALS, NO NORMS.",
            "SEE CUSTOM-TRACKS.MD TO MAKE ONE.",
        }),
        MenuItem.School => ("DRIVING SCHOOL", new[]
        {
            "INSTRUCTIONS FOR CAR HANDLING,",
            "GAME MECHANICS AND TRACKS. WATCH IT ALL,",
            "OR ONLY THE LESSONS YOU NEED.",
        }),
        MenuItem.Lan => (Hud.LanLabel, new[]
        {
            "RACE AGAINST UP TO 5 COMRADES ON YOUR",
            "LOCAL NETWORK, OR ONLINE THROUGH A",
            "VIRTUAL NETWORK.",
        }),
        MenuItem.Files => ("KGB FILES", new[]
        {
            "THE ARCHIVE OF THE PARTY: PERSONNEL FILES",
            "AND THE RECORDINGS OF EVERY DRIVER.",
            "R OPENS THE SELECTED DRIVER'S RECORDINGS.",
        }),
        MenuItem.Credits => ("CREDITS", new[]
        {
            "WHO BUILT THIS CAR, AND WHO PLAYS THE HARMONIUM.",
            "THE TOOLS, THE LICENCES AND THE SMALL PRINT.",
        }),
        MenuItem.Quit => ("RESIGN", new[]
        {
            "HAND IN YOUR RESIGNATION FROM THE PARTY.",
            "THE PARTY WILL PROCESS IT. AT ONCE.",
        }),
        _ => null,
    };
}
