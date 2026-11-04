using System;
using CCCPRacing.UI;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// The KGB ARCHIVE: the five PERSONNEL FILES (PlayerFiles), each driver's own medals, KGB file,
/// lessons seen and GAI readings. A plain launch opens it first (after the TEST VERSION card): the
/// driver opens their file or a new one, then the main menu; the title's FILES opens it again. Up/Down
/// choose, Enter takes a file into use (an empty one asks its driver's name first), Delete (a pad: Y)
/// erases one after ERASE FROM HISTORY?, Esc back (at launch: quits, as the top row does). The boards
/// of honour stay everyone's.
/// </summary>
public partial class RaceScene
{
    int _file = 1;
    bool _showFiles, _filesErase, _filesAtLaunch;
    static bool _archiveDone;      // the archive at launch: once a session (a track change rebuilds the scene)
    int _filesSel;
    int _askFile;          // the name question is for this new file (1..), not for a run

    /// <summary>The personnel file in use: its own settings.cfg, campaign.cfg and speedtrap.cfg.</summary>
    string FileDir => PlayerFiles.Dir(_records.Dir, _file);

    /// <summary>A plain launch (no command line; --archive in tests): the archive opens first, once the title is clear.</summary>
    void ArchiveAtLaunch()
    {
        if (_archiveDone || _state != State.Title || _showLog || _watching || _showBoard || _showFiles || _askName) return;
        _archiveDone = true;
        var args = CmdArgs();
        if (args.Length > 0 && Array.IndexOf(args, "--archive") < 0) return;
        var menu = _menu;
        EnterFiles();
        _menu = menu;           // the main menu comes up where it would have
        _filesAtLaunch = true;
        _hud.BeginStartupBanner();
        FilesHud();
    }

    /// <summary>Open the launch archive as soon as the test-version card closes, before the cover is removed.</summary>
    public void PrepareStartupArchive() => ArchiveAtLaunch();

    void EnterFiles()
    {
        _showFiles = true;
        _filesAtLaunch = false;
        _filesErase = false;
        _filesSel = _file - 1;
        _menu = MenuItem.Files;
        _overlayFrame = Engine.GetProcessFrames();
        _hud.Mode = HudMode.Files;
        FilesHud();
    }

    void CloseFiles()
    {
        _showFiles = _filesErase = _filesAtLaunch = false;
        _overlayExitFrame = Engine.GetProcessFrames();
        _hud.Mode = HudMode.Title;
    }

    /// <summary>File n in use from now: its medals, lessons, GAI readings and name.</summary>
    void UseFile(int n, bool touch = true)
    {
        _file = n;
        PlayerFiles.Activate(_records.Dir, n, touch);
        _campaign = new Campaign(FileDir);
        _speeds = new SpeedRecords(FileDir);
        LoadCameraDiscoveries();
        _tutorial = new TutorialFile(FileDir);
        LoadDriverName();
        if (OS.HasEnvironment("FILESDBG")) GD.Print($"files: file {n} in use, {FileDir}");
    }

    /// <summary>A new file has its name: on to the main menu, a word that it is opened.</summary>
    void FileCreated()
    {
        CloseFiles();
    }

    /// <summary>The files' raw input (keys, the pad's d-pad, A, B and Y): true while the files are up.</summary>
    bool FilesInput(InputEvent e)
    {
        if (!_showFiles) return false;
        if (_hud.StartupArchivePending) return true;
        if (Engine.GetProcessFrames() <= _overlayFrame || _askName) return true;
        bool Is(Key k1, Key k2, JoyButton b) => e is InputEventKey { Pressed: true, Echo: false } k && (k.Keycode == k1 || k.Keycode == k2)
            || e is InputEventJoypadButton { Pressed: true } j && j.ButtonIndex == b;
        bool enter = e is InputEventKey { Pressed: true, Echo: false, AltPressed: false, Keycode: Key.Enter or Key.KpEnter }
            || e is InputEventJoypadButton { Pressed: true, ButtonIndex: JoyButton.A or JoyButton.Start };
        bool back = Is(Key.Escape, Key.Escape, JoyButton.B);
        var sel = PlayerFiles.Read(_records.Dir, _filesSel + 1);
        if (_filesErase)
        {
            if (enter)
            {
                PlayerFiles.Erase(_records.Dir, _filesSel + 1);
                string who = sel.Name.Length > 0 ? sel.Name : "TOVARISH";
                _hud.MenuNote($"{who} WAS NEVER HERE", $"FILE {_filesSel + 1} ERASED FROM HISTORY", 3f);
                if (_filesSel + 1 == _file) UseFile(_file, touch: false);       // the file in use goes on empty
                _filesErase = false;
            }
            else if (back) _filesErase = false;
            FilesHud();
            return true;
        }
        if (Is(Key.Up, Key.W, JoyButton.DpadUp)) _filesSel = (_filesSel + PlayerFiles.Count - 1) % PlayerFiles.Count;
        else if (Is(Key.Down, Key.S, JoyButton.DpadDown)) _filesSel = (_filesSel + 1) % PlayerFiles.Count;
        else if (Is(Key.Delete, Key.Delete, JoyButton.Y) && sel.Used) _filesErase = true;
        else if (back && _filesAtLaunch) AskResignation();      // at launch the archive is the top: Esc resigns (RaceScene.Resign.cs)
        else if (back) CloseFiles();
        else if (enter)
        {
            if (!sel.Used)
            {
                // A new file: its driver's name first (YOUR NAME, TOVARISH?), then it is in use.
                _askName = true;
                _askFile = _filesSel + 1;
                _askFrame = Engine.GetProcessFrames();
                _entryName = "";
            }
            else
            {
                if (_filesSel + 1 != _file) UseFile(_filesSel + 1);
                CloseFiles();
            }
        }
        FilesHud();
        return true;
    }

    /// <summary>The files card's rows: number, name, each plan track's medal, last played.</summary>
    void FilesHud()
    {
        var rows = new Hud.FileRow[PlayerFiles.Count];
        for (int i = 0; i < rows.Length; i++)
        {
            var f = PlayerFiles.Read(_records.Dir, i + 1);
            var medals = Array.ConvertAll(Tracks, t => f.Medals.TryGetValue(t.id, out var m) ? (int)m : 0);
            string played = f.Played > 0 ? Time.GetDatetimeStringFromUnixTime(f.Played, true)[..10] : "";
            rows[i] = new Hud.FileRow(f.Used, f.Name, medals, played, i + 1 == _file, f.Cameras);
        }
        _hud.Files = rows;
        _hud.FilesAtLaunch = _filesAtLaunch;
        _hud.FilesSelection = _filesSel;
        _hud.FilesErase = _filesErase;
    }

    /// <summary>The title's corner: whose file is in use.</summary>
    string FileLabel()
    {
        string name = _board.LastName.Length > 0 ? _board.LastName : "NEW";
        return $"KGB FILE {_file}: {name}";
    }

    /// <summary>The live scene's record, profile, campaign, board and ghost loading (was inline in _Ready; a viewer skips it).</summary>
    async System.Threading.Tasks.Task LoadPlayerFiles()
    {
        // Records made on a differently shaped road are archived: the new track starts empty.
        _records = new RecordStore(_dataDir, _track.Id, TrackSignature.Compute(_track));
        _carRadio.LoadVolume(_records.Dir);
        InitPad();       // RaceScene.Pad.cs: pads, the rumble setting, pad test flags
        PlayerFiles.Migrate(_records.Dir);     // once: the progress so far becomes PERSONNEL FILE 1 (RaceScene.Files.cs)
        _file = PlayerFiles.Active(_records.Dir);
        DirAccess.MakeDirRecursiveAbsolute(FileDir);
        if (_booting) await LoadingFrame();
        _campaign = new Campaign(FileDir);
        _speeds = new SpeedRecords(FileDir);
        LoadCameraDiscoveries();
        InitSchool();    // RaceScene.School.cs: the driving school's settings and test flags
        string rallyBoard = _boardPath ?? _records.BoardPath;
        string lastName = Leaderboard.Load(rallyBoard).LastName;     // the driver's name outlives an archive
        var archived = _records.ArchiveIfChanged();
        bool newTerrain = archived.Count > 0;
        archived.AddRange(_records.ArchiveOldLayout());     // once: one set of records per run
        if (archived.Count > 0) _hud.TitleNotice = newTerrain ? RecordStore.ArchivedNotice : RecordStore.LayoutNotice;
        for (int m = 0; m < RecordStore.Sets; m++)
        {
            if (!_noRecord) _ghosts[m] = Ghost.Load(_records.GhostForSet(m), _params, _track);
            _bestLaps[m] = _ghosts[m]?.LapTime ?? -1;
            _tapes[m] = LoadTapeSlot(m);      // RaceScene.Tape.cs: the tape beside the ghost, if it is whole
            _boards[m] = Leaderboard.Load(m == 0 ? rallyBoard : _records.BoardForSet(m), _bestLaps[m], m == 1 ? Leaderboard.StakhanovSize : Leaderboard.Size);
        }
        // The shipped demonstration stays separate from all player records and racing ghosts.
        const string demo = "res://assets/replays/ilyich-stakhanov-petrol";
        if (_track.Id == "ilyich" && FileAccess.FileExists(demo + ".sig")
            && FileAccess.GetFileAsString(demo + ".sig").StripEdges() == _records.Signature)
            _bundledTitleGhost = Ghost.Load(demo + ".bin", _params, _track);
        if (_rallyBoard.LastName == "" && lastName != "")
        {
            _rallyBoard.LastName = lastName;
            _rallyBoard.Save();
            _records.WriteSignature();
        }
        for (int m = 1; m < RecordStore.Sets; m++)
            if (_boards[m].LastName == "") _boards[m].LastName = _rallyBoard.LastName;
        LoadDriverName();     // RaceScene.Name.cs: the name given at a run's start, on every board
    }
}
