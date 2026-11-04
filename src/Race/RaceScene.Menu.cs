using System;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// The title menu in two levels. The top row: SINGLE PLAYER | MULTIPLAYER | PARTY OFFICE;
/// Enter opens a row (DRIVING SCHOOL | SOCIALIST RALLY | STAKHANOV LAPS; LAN RACE;
/// KGB FILES | LEADERBOARD | ABOUT THE GAME | RESIGN; ABOUT THE GAME is a third row: CHANGELOG | CREDITS | BACK;
/// the DRIVING SCHOOL teaches car control on ILYICH'S LAMP and comes back here, a rally's own school
/// is offered before a rally on a track that has one, Stakhanov laps have none),
/// Esc goes back up with the parent selected (on the top row Esc hands in the resignation). Left/Right move
/// along a row, Down/Up select and leave the replay line at both levels. Back from a run the top row
/// has the parent of the mode played last, and Enter opens its row on that mode. In a mode row a
/// hint box in the middle of the screen says what the selected mode is.
/// </summary>
public partial class RaceScene
{
    enum MenuItem { Race, Training, Lan, Board, Single, Multi, Quit, School, Files, Office, Credits, Recordings }

    static readonly MenuItem[] TopItems = { MenuItem.Single, MenuItem.Multi, MenuItem.Office };
    static readonly MenuItem[] SingleItems = { MenuItem.School, MenuItem.Race, MenuItem.Training };
    static readonly MenuItem[] MultiItems = { MenuItem.Lan };
    static readonly MenuItem[] OfficeItems = { MenuItem.Files, MenuItem.Board, MenuItem.Recordings, MenuItem.Credits, MenuItem.Quit };
    static readonly string[] TopLabels = { "SINGLE PLAYER", "MULTIPLAYER", "PARTY OFFICE" }, TopShort = { "1 PLAYER", "MULTI", "OFFICE" };
    static readonly string[] OfficeLabels = { "KGB FILES", "LEADERBOARD", "RECORDINGS", "CREDITS", "RESIGN" }, OfficeShort = { "FILES", "TOP 10", "REC", "CREDITS", "RESIGN" };
    static readonly string[] SingleLabels = { "DRIVING SCHOOL", Hud.RaceLabel, Hud.TrainingLabel }, SingleShort = { "SCHOOL", Hud.RaceShort, Hud.TrainingShort };
    static readonly string[] MultiLabels = { Hud.LanLabel }, MultiShort = { Hud.LanShort };

    MenuItem _menu = MenuItem.Multi;
    MenuItem? _menuParent;                  // the open row's parent (null: the top row)
    MenuItem _lastPlayed = MenuItem.Lan;
    bool _menuSingleTest;

    MenuItem[] MenuRow => _menuParent switch { MenuItem.Single => SingleItems, MenuItem.Multi => MultiItems, MenuItem.Office => OfficeItems, _ => TopItems };

    static MenuItem ParentOf(MenuItem m) => m switch
    {
        MenuItem.Training or MenuItem.Race or MenuItem.School => MenuItem.Single,
        MenuItem.Lan => MenuItem.Multi,
        MenuItem.Files or MenuItem.Board or MenuItem.Recordings or MenuItem.Credits or MenuItem.Quit => MenuItem.Office,
        _ => m,
    };

    /// <summary>The title's menu after EnterTitle: the top row, on the parent of the mode played last.</summary>
    void ResetMenu()
    {
        _menuParent = null;
        _menu = ParentOf(_lastPlayed);
    }

    /// <summary>A mode chosen from the title (or started by a flag): the menu comes back to it.</summary>
    void MenuPlayed(MenuItem mode) => _lastPlayed = mode;

    /// <summary>Opens a row (Single Player defaults to SOCIALIST RALLY; other rows use the last mode or first).</summary>
    void OpenMenu(MenuItem parent, MenuItem? at = null)
    {
        _menuParent = parent;
        var row = MenuRow;
        MenuItem pick = at ?? (parent == MenuItem.Single ? MenuItem.Race : _lastPlayed);
        _menu = Array.IndexOf(row, pick) >= 0 ? pick : row[0];
    }

    /// <summary>Title Enter on the selected item.</summary>
    void MenuEnter()
    {
        switch (_menu)
        {
            case MenuItem.Single or MenuItem.Multi or MenuItem.Office: OpenMenu(_menu, _menu == MenuItem.Office ? MenuItem.Files : null); break;
            case MenuItem.Board: EnterBoard(); break;
            case MenuItem.Files: EnterFiles(); break;     // RaceScene.Files.cs
            case MenuItem.Recordings: EnterRecordings(); break;     // RaceScene.Recordings.cs
            case MenuItem.Credits: EnterCredits(); break;
            case MenuItem.Lan: MenuPlayed(MenuItem.Lan); EnterLanMenu(); break;
            case MenuItem.Quit: AskResignation(); break;      // RaceScene.Resign.cs
            case MenuItem.School:
                MenuPlayed(MenuItem.School);
                EnterLessons();           // the lessons' tree (RaceScene.SchoolMenu.cs)
                break;
            case MenuItem.Training or MenuItem.Race:
                OpenTrackCard(_menu);     // CHOOSE TRACK (RaceScene.Tracks.cs): laps go on the board under the file's name
                break;
        }
    }

    /// <summary>Title Esc: out of a row to the top (true), or nothing to go back to (false: quit).</summary>
    bool MenuBack()
    {
        if (_menuParent is not MenuItem parent) return false;
        _menuParent = null;
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
        _hud.MenuHeader = top ? null : TopLabels[Array.IndexOf(TopItems, _menuParent.Value)];
        _hud.MenuDim = -1;
        _hud.MenuSelection = Math.Max(0, Array.IndexOf(row, _menu));
        _hud.MenuHint = top || _askName ? null : MenuHint(_menu);   // over a card too: the card goes under it
    }

    /// <summary>What a mode is, in a few lines for the hint box (the pad's words when a pad is in use).</summary>
    (string title, string[] lines)? MenuHint(MenuItem m) => m switch
    {
        MenuItem.Training => (Hud.TrainingLabel, new[]
        {
            "TRACK PRACTICE ON AN EMPTY ROAD.",
            "NO DAMAGE, NO FUEL, NO NORMS. JUST SPEED.",
            _padMode ? "D-PAD: DIESEL / PETROL / JET FUEL." : "1 DIESEL  2 PETROL  3 JET FUEL.",
            "BEAT THE RECORD ON THE BOARD OF HONOUR.",
        }),
        MenuItem.Race => (Hud.RaceLabel, new[]
        {
            "SOCIALISM AS IT WAS MEANT TO WORK.",
            "A PEASANT CHOSEN TO BECOME A HERO OF THE STATE.",
            "BRUTAL QUALIFYING. THE WINNER TAKES IT ALL.",
            "THE GULAG HAS ROOM FOR THE REST.",
        }),
        MenuItem.School => ("DRIVING SCHOOL", new[]
        {
            "CAR HANDLING: CLUTCH STARTS, STEERING,",
            "BRAKING AND DRIFTING, SHOWN BY THE TEACHER.",
            "TRACK INSTRUCTIONS FOR EVERY OPEN TRACK.",
            "WATCH IT ALL, OR ONLY THE LESSON YOU NEED.",
        }),
        MenuItem.Lan => (Hud.LanLabel, new[]
        {
            "RACE UP TO 5 COMRADES ON THE SAME NETWORK.",
            "VOTE FOR TRACK AND RULES WITH YOUR COMRADES.",
            "VOTE FOR THE POWER. RACE 3, 5 OR 10 LAPS.",
            "SHARED WORLD. CAR-TO-CAR COLLISIONS.",
        }),
        MenuItem.Files => ("KGB FILES", new[]
        {
            "FIVE PERSONNEL FILES IN THE ARCHIVE.",
            "EACH DRIVER KEEPS HIS OWN NAME, MEDALS,",
            "LESSONS SEEN AND SPEED RECORDS.",
            "OPEN YOURS, START A NEW ONE, OR ERASE ONE.",
        }),
        MenuItem.Board => ("LEADERBOARD", new[]
        {
            "THE BOARD OF HONOUR: THE TEN BEST LAPS",
            "OF EVERY TRACK, RALLY AND STAKHANOV LAPS.",
            "THE PARTY REMEMBERS EVERY HERO OF LABOUR.",
        }),
        MenuItem.Recordings => ("RECORDINGS", new[]
        {
            "THE LAPS YOU DROVE, WATCHED AGAIN.",
            "THIS SESSION'S LAPS AND THE ONES YOU KEPT.",
            "KEEP A LAP TO SAVE IT FOR NEXT TIME.",
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
