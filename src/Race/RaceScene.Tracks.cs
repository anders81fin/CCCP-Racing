using System;
using System.Linq;
using CCCPRacing.Track;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// Which track the world is built from. STAKHANOV LAPS asks for the track (a card over the title);
/// another track than the one built reloads the scene beneath the loading card after the time
/// and light choices. The rally and LAN ask for their tracks too; LAN hosts use bronze unlocks,
/// while joining drivers may select any track.
/// The driving school starts on the bonus track. Records and ghosts are kept per track (RecordStore).
/// Test: --track=ID builds that track from the start.
/// </summary>
public partial class RaceScene
{
    /// <summary>
    /// The tracks to choose from (tracks/ID.json): the five-year plan in order, each opened by a
    /// rally medal on the one it requires (null: open), then the bonus track (Bonus), opened
    /// by a gold on any track. A bronze opens a plan track for STAKHANOV LAPS, the SOCIALIST
    /// RALLY on it takes a silver (MedalToOpen); the bonus track opens for both at once.
    /// </summary>
    static readonly (string id, string name, string requires)[] Tracks =
        { ("ilyich", "KOLKHOZ", null), ("siberia", "SIBERIA", "ilyich"), ("atomgrad", "ATOMGRAD-4", "siberia"), ("bonus", "POLIGON 17", Bonus) };
    /// <summary>"requires" of the bonus track: a gold on any track.</summary>
    const string Bonus = "*";
    /// <summary>The bonus track: the driving school starts here, open or not.</summary>
    const string TrainingTrack = "bonus";

    // Survive the reload: the track to build and what the title does once it is built.
    static string _trackId;
    static MenuItem? _resume;
    string _chosenRunTrack;
    static MenuItem? _loadingRunMode;
    static bool _resumeReplay;     // the title's replay line switched tracks: select it again

    MenuItem _trackFor = MenuItem.Training;   // the mode the track card is for
    bool _bonusOpened;                        // this run's gold opened the bonus track (the PLAN FULFILLED screen says so)
    NormPlan.Medal _runMedal;                 // best medal earned in this run, even if the campaign already had it
    bool _rallyNextOpenedThisRun;
    Campaign _campaign = new(null);

    // The title's LEADERBOARD shows any track's boards; the ones not built are read once a screen.
    readonly System.Collections.Generic.Dictionary<string, Leaderboard> _otherBoards = new();

    /// <summary>A board of a track other than the one built, read from its file (cached until the scene reloads).</summary>
    Leaderboard OtherBoard(string id, int set)
    {
        string key = id + "_" + set;
        if (!_otherBoards.TryGetValue(key, out var b))
            _otherBoards[key] = b = Leaderboard.Load(new RecordStore(_records.Dir, id, null).BoardForSet(set), capacity: set == 1 ? Leaderboard.StakhanovSize : Leaderboard.Size, fuelPartitions: set == 1);
        return b;
    }

    /// <summary>The leaderboard's pages in Left/Right order: all rally fuels together, then Stakhanov laps.</summary>
    static readonly int[] BoardOrder = { 0, 1 };

    /// <summary>This page if the track has laps on it, else the first page that has (the page itself when none has).</summary>
    int BoardPageWithLaps(string id, int page)
    {
        page = page == 1 ? 1 : 0;
        bool Has(int p) => p == 1 ? BoardFor(id, 1).Count > 0 : new[] { 0, 2, 3, 4 }.Any(f => BoardFor(id, f).Count > 0);
        if (Has(page)) return page;
        foreach (int p in BoardOrder) if (Has(p)) return p;
        return page;
    }

    static string TrackName(string id) => Array.Find(Tracks, t => t.id == id).name ?? id.ToUpperInvariant();

    /// <summary>The plan's next track after this one (null: none built yet).</summary>
    static string NextTrack(string id)
    {
        foreach (var t in Tracks) if (t.requires == id) return t.id;
        return null;
    }

    /// <summary>A bronze-only run may still have opened the next track for STAKHANOV LAPS.</summary>
    string BronzeStakhanovNote()
    {
        if (_runMedal != NormPlan.Medal.Bronze || NextTrack(_trackId) is not { } next) return null;
        int i = Array.FindIndex(Tracks, t => t.id == next);
        return i >= 0 && !Locked(i, MenuItem.Training) ? $"BRONZE: {TrackName(next)} OPEN IN STAKHANOV LAPS" : null;
    }

    /// <summary>After a Socialist Rally ends, return to its track card, highlighting a track opened by this run.</summary>
    void ReturnRallyToTrackSelect()
    {
        string selected = _trackId;
        if (_rallyNextOpenedThisRun && NextTrack(_trackId) is { } next)
        {
            int nextIndex = Array.FindIndex(Tracks, t => t.id == next);
            if (nextIndex >= 0 && !Locked(nextIndex, MenuItem.Race)) selected = next;
        }
        int selectedIndex = Array.FindIndex(Tracks, t => t.id == selected);
        EnterTitle();
        OpenMenu(MenuItem.Single, MenuItem.Race);
        OpenRallyStart(Math.Max(0, selectedIndex));      // the fuel again first, then the track card on the track this run opened
        // (a track-opening ceremony of the laps reward comes with the track card)
    }

    /// <summary>The rally medal on the required track that opens a track for this mode: bronze for Stakhanov laps, silver for the rally.</summary>
    static NormPlan.Medal MedalToOpen(MenuItem mode) => mode == MenuItem.Race ? NormPlan.Medal.Silver : NormPlan.Medal.Bronze;

    /// <summary>Track i is still locked for this mode (the track card's mode by default). anyFuel: the rally counts the medals of every fuel (the school's lists, where no fuel is chosen).</summary>
    bool Locked(int i, MenuItem? mode = null, bool anyFuel = false) => !(CheatUnlock && (mode ?? _trackFor) != MenuItem.Lan) && LockedByMedals(i, mode, anyFuel);      // (the LAN host's tracks follow his real medals)

    /// <summary>
    /// The track is locked by the medals, whatever the cheat says (the unlocking ceremonies follow the real ones). In the SOCIALIST RALLY each fuel has a progression of its own (the fuel is chosen
    /// before the track): a track opens only by the medals won on the chosen fuel (_rallyFuel), so what the diesel opened is not open on the petrol until the petrol has opened it. The Stakhanov
    /// laps do the same with the bronze on the fuel picked first; the LAN and the school count the medals of every fuel.
    /// </summary>
    bool LockedByMedals(int i, MenuItem? mode = null, bool anyFuel = false)
    {
        mode ??= _trackFor;
        if (mode == MenuItem.Lan && _lanTrackHosting != true) return false;
        FuelGrade? own = anyFuel ? null : mode == MenuItem.Race ? _rallyFuel : mode == MenuItem.Training && _lapsFuelPicked ? _lapsFuelPick : null;
        NormPlan.Medal Medal(string id) => own is FuelGrade f ? _campaign.BestOn(id, f) : _campaign.Best(id);
        if (Tracks[i].requires == Bonus) return !Tracks.Any(t => Medal(t.id) == NormPlan.Medal.Gold);
        string r = Tracks[i].requires;
        return r != null && Medal(r) < MedalToOpen(mode.Value);
    }


    /// <summary>What opens a locked track for the track card's mode, for its notes.</summary>
    string Opens(int i)
    {
        string fuel = _trackFor == MenuItem.Race ? $" ON {FuelGrades.Names[(int)_rallyFuel]}" : _trackFor == MenuItem.Training && _lapsFuelPicked ? $" ON {FuelGrades.Names[(int)_lapsFuelPick]}" : "";      // (the medal of the chosen fuel)
        return Tracks[i].requires == Bonus ? "A GOLD ON ANY TRACK" + fuel : $"A {Campaign.Name(MedalToOpen(_trackFor))} ON {TrackName(Tracks[i].requires)}{fuel}";
    }

    /// <summary>The tracks locked now for this mode (the track card's by default), by index.</summary>
    bool[] LockedNow(MenuItem? mode = null) => Enumerable.Range(0, Tracks.Length).Select(i => Locked(i, mode)).ToArray();

    /// <summary>Start of _Ready: the track to build (the flag, else the last chosen, else the plan's first).</summary>
    string TrackToBuild()
    {
        if (InstructionTrack != null) _trackId = InstructionTrack;
        if(InstructionTrack==null && _lanWorldRebuild && CCCPRacing.Net.LanSession.Current?.SelectedTrack is {} chosen) _trackId=chosen;
        bool training = false;
        foreach (string a in InstructionTrack == null ? OS.GetCmdlineUserArgs() : Array.Empty<string>())
        {
            if (a.StartsWith("--track=") && _resume == null && _loadingRunMode == null && _previewResume < 0 && !_lanWorldRebuild) _trackId = TrackIds.FromCommandLine(a["--track=".Length..]);   // test: build this track
            // The school and LAN flags skip the menu that would take them to the bonus track: build it at once.
            training |= a.StartsWith("--school=") || a == "--lan-host" || a.StartsWith("--lan-join=");
        }
        _trackId ??= training ? TrainingTrack : Tracks[0].id;
        // The world's registries are static: a rebuilt world starts them empty.
        RadarHead.All.Clear();
        NightGlow.Glows.Clear();
        NightGlow.Level = 0f;
        NightGlow.Lamps.Clear();
        return _trackId;
    }

    /// <summary>
    /// The mode needs this track: true when it is the one built; otherwise the scene is rebuilt on
    /// it and the title resumes with the mode (false: the caller does nothing more now).
    /// </summary>
    bool OnTrack(string id, MenuItem mode)
    {
        if (id == _trackId) return true;
        if (OS.HasEnvironment("TRACKDBG")) GD.Print($"track: {_trackId} -> {id} for {mode}");
        _trackId = id;
        _resume = mode;
        GetTree().CallDeferred(SceneTree.MethodName.ReloadCurrentScene);   // after this frame: it still runs on the old world
        return false;
    }

    string[] _replayTracks;

    /// <summary>The tracks with a record lap on file (the rally's, else the Stakhanov one), in Tracks order: the title can replay them.</summary>
    string[] ReplayTracks() => _replayTracks ??= Array.FindAll(Tracks, t => t.id == _trackId && TitleGhost != null
        || Enumerable.Range(0, RecordStore.Sets).Any(set => FileAccess.FileExists(new RecordStore(_records.Dir, t.id, null).GhostForSet(set)))).Select(t => t.id).ToArray();

    /// <summary>A board's laps (page 0 rally, 1 Stakhanov) of any track: the built one's from memory, others from file.</summary>
    System.Collections.Generic.IReadOnlyList<Leaderboard.Entry> BoardFor(string id, int page) =>
        id == _trackId ? _boards[page].Entries : OtherBoard(id, page).Entries;

    /// <summary>Left/Right on the title's replay line: the next track's record lap (the world is rebuilt on it).</summary>
    void SwitchReplayTrack(int dir)
    {
        var ids = ReplayTracks();
        int i = Array.IndexOf(ids, _trackId);
        if (ids.Length < 2 || i < 0) return;
        _trackId = ids[(i + dir + ids.Length) % ids.Length];
        if (OS.HasEnvironment("TRACKDBG")) GD.Print($"track: replay of {_trackId}");
        _resumeReplay = true;
        GetTree().CallDeferred(SceneTree.MethodName.ReloadCurrentScene);
    }

    /// <summary>End of _Ready after a track change: back to where the player was going.</summary>
    void ResumeAfterReload()
    {
        if (_loadingRunMode is MenuItem loadingMode)
        {
            _loadingRunMode = null;
            FinishLoadingRun(loadingMode);
            return;
        }
        if (_previewResume >= 0)
        {
            _askName = false;
            int selected = _previewResume;
            _previewResume = -1;
            _showLog = false;
            _hud.Mode = HudMode.Title;
            OpenMenu(MenuItem.Single, _previewResumeFor);
            OpenTrackCard(_previewResumeFor);
            if (_lapsRewardIndex < 0) _cardSel = selected;
            if (_tourResume) { _tourResume = false; if (_lapsRewardIndex < 0) OpenTour(); }      // let a pending unlock show before a tour
            return;
        }
        if (_resumeReplay)
        {
            _resumeReplay = false;
            _showLog = false;
            _hud.Mode = HudMode.Title;
            return;
        }
        if (_resume is not MenuItem mode) return;
        _resume = null;
        _showLog = false;
        _hud.Mode = HudMode.Title;
        MenuPlayed(mode);
        ResetMenu();
        OpenMenu(ParentOf(mode), mode);
        switch (mode)
        {
            case MenuItem.Training: AskOrRun(MenuItem.Training); break;
            case MenuItem.Race: AskOrRun(MenuItem.Race); break;   // the track was chosen before the rebuild
            case MenuItem.School:     // the lesson's track is built
                if (_lessonResume != null) ResumeLesson();
                else EnterSchool(SchoolCourse.Driving, true);
                break;
            case MenuItem.Lan:
                if (ResumeLanJoin()) break;
                if (_lanTrackHosting.HasValue) FinishLanTrackChoice();
                else EnterLanMenu();
                break;
        }
    }

    /// <summary>STAKHANOV LAPS or the SOCIALIST RALLY: the track card, always on the plan's first track (a locked one shows in black and white).</summary>
    void OpenTrackCard(MenuItem mode)
    {
        _trackFor = mode;
        OpenCard(TitleCard.Track);
        _cardSel = 0;
        if (mode == MenuItem.Lan) _cardSel = Math.Max(0, Array.FindIndex(Tracks,t=>t.id==_trackId));
        else BeginLapsReward();
    }

    /// <summary>The track card's Enter: the chosen track (rebuilt if need be), then INSTRUCTIONS, TOVARISH?</summary>
    void TrackChosen(int sel)
    {
        sel = Math.Clamp(sel, 0, Tracks.Length - 1);
        if (Locked(sel))
        {
            _hud.MenuNote("LOCKED", $"WIN {Opens(sel)} FIRST", 3f);
            OpenCard(TitleCard.Track);
            _cardSel = sel;
            return;
        }
        _chosenRunTrack = Tracks[sel].id;
        if (_trackFor == MenuItem.Lan)
        {
            if (OnTrack(_chosenRunTrack, MenuItem.Lan)) FinishLanTrackChoice();
            return;
        }
        AskOrRun(_trackFor);
    }

    /// <summary>The track card's second line: the highlighted track's rally medal, or what opens it.</summary>
    string TrackCardSub()
    {
        int i = Math.Clamp(_cardSel, 0, Tracks.Length - 1);
        if (Locked(i)) return (Tracks[i].requires == Bonus ? "BONUS TRACK. " : "") + $"LOCKED: {Opens(i)} OPENS IT";
        if (_trackFor == MenuItem.Lan) return _lanTrackHosting == true
            ? "HOST A RACE ON A TRACK YOU HAVE OPENED"
            : "CHOOSE THE HOST'S TRACK - ALL TRACKS OPEN TO JOIN";
        string gai = SpeedText(_trackFor == MenuItem.Race ? "rally" : "stakhanov", Tracks[i].id);
        gai = gai == "" ? "" : "   " + gai;
        // (the rally shows the medal of the chosen fuel, the laps the best of any)
        NormPlan.Medal shown = _trackFor == MenuItem.Race ? _campaign.BestOn(Tracks[i].requires == Bonus ? TrainingTrack : Tracks[i].id, _rallyFuel) : _campaign.Best(Tracks[i].requires == Bonus ? TrainingTrack : Tracks[i].id);
        if (Tracks[i].requires == Bonus) return "BONUS TRACK. RALLY MEDAL: " + Campaign.Name(shown) + gai;
        return "RALLY MEDAL: " + Campaign.Name(shown) + gai;
    }

    /// <summary>Persist improved campaign medals and celebrate each new medal of this run without stopping.</summary>
    void CheckMedal(int beaten)
    {
        var medal = NormPlan.MedalFor(beaten);
        bool celebrate = medal > _runMedal;
        if (celebrate) _runMedal = medal;
        _bonusOpened = false;
        var rallyBefore = LockedNow(MenuItem.Race);
        var lapsBefore = LockedNow(MenuItem.Training);
        if (medal == NormPlan.Medal.None) return;
        bool improved = _campaign.Award(_trackId, medal, _rallyFuel);
        string[] Opened(bool[] before, MenuItem mode) => Enumerable.Range(0, Tracks.Length).Where(i => before[i] && !Locked(i, mode)).Select(i => Tracks[i].name).ToArray();
        var rally = improved ? Opened(rallyBefore, MenuItem.Race) : Array.Empty<string>();
        var laps = improved ? Opened(lapsBefore, MenuItem.Training) : Array.Empty<string>();
        if (improved && OS.HasEnvironment("PLANDBG")) GD.Print($"plan: {Campaign.Name(medal)} medal on {_trackId} ({beaten} norms), opened: rally [{string.Join(", ", rally)}], Stakhanov laps [{string.Join(", ", laps)}]");
        if (NextTrack(_trackId) is { } nextTrack && rallyBefore[Array.FindIndex(Tracks, t => t.id == nextTrack)]
            && !Locked(Array.FindIndex(Tracks, t => t.id == nextTrack), MenuItem.Race))
            _rallyNextOpenedThisRun = true;
        _bonusOpened = medal == NormPlan.Medal.Gold && rally.Any(n => Array.Exists(Tracks, t => t.name == n && t.requires == Bonus));
        if (!celebrate) return;
        string unlock = rally.Length > 0 ? string.Join(" + ", rally) + ": RALLY UNLOCKED"
            : laps.Length > 0 ? string.Join(" + ", laps) + ": STAKHANOV UNLOCKED" : "GLORY TO YOUR GOOD WORK!";
        _hud.CelebrateMedal(medal, unlock);
        if (OS.HasEnvironment("PLANDBG")) GD.Print($"medal celebration: {medal}, race continues");
    }
}
