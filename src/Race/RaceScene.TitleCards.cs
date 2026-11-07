using System;
using CCCPRacing.Track;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// The title's cards before a run: the track card, the rally's TIME OF DAY (where its day starts; the light then goes on lap by
/// lap), and the Stakhanov laps' TIME OF DAY (where it starts; the light always goes on lap by lap).
/// A rally starts after its track and its fuel, always at DAY (the SOCIALIST RALLY has no time-of-day card; the day then goes on lap by lap). No TRACK INSTRUCTIONS question: the rally schools stay, EnterSchool.
/// </summary>
public partial class RaceScene
{
    enum TitleCard { None, Track, LapsTime, RallyFuel, RallyResume, LapsFuel }

    TitleCard _card;
    int _cardSel;
    ulong _cardFrame;
    static string _newRallyTrack;       // new rally replaces this save when loading completes
    FuelGrade _savedRallyFuel;

    // The Stakhanov laps' CHOOSE FUEL card (never saved): the pick waits here until TIME OF DAY is chosen, then rides through a track's
    // rebuild in the static (a new RaceScene parses --power again) and is taken once by PrepareRun. The last one opens the card next time.
    FuelGrade _lapsFuelPick;
    static FuelGrade? _lapsFuelStart, _lapsFuelLast;

    /// <summary>Before either run its time of day (RaceScene.DayNight.cs).</summary>
    void AskOrRun(MenuItem mode)
    {
        if (mode == MenuItem.Training) { OpenCard(TitleCard.LapsFuel); return; }     // Stakhanov laps: the fuel, then the time of day
        if (mode == MenuItem.Race)
        {
            _newRallyTrack = null;
            if (RallyResume.Exists(FileDir, _chosenRunTrack ?? _trackId)) OpenCard(TitleCard.RallyResume);
            else OpenCard(TitleCard.RallyFuel);
            return;
        }
        StartRun(mode);
    }

    /// <summary>INSTRUCTIONS, TOVARISH? before a rally (YES first until the track's school has been seen), or the track card.</summary>
    void OpenCard(TitleCard c)
    {
        if (c == TitleCard.RallyResume) _savedRallyFuel = RallyResume.FuelOf(FileDir, _chosenRunTrack ?? _trackId);
        _card = c;
        _cardSel = c switch
        {
            TitleCard.Track => 0,
            TitleCard.LapsTime => (int)_lapsPhase,
            TitleCard.RallyFuel => (int)(_rallyFuel = OpenFuel(FuelGrades.Load(_records.Dir))),
            TitleCard.LapsFuel => (int)OpenFuel(_lapsFuelLast ?? Training.GradeOf(_powerStart) ?? FuelGrade.Petrol),
            _ => 0,
        };
        _cardFrame = Engine.GetProcessFrames();
        if (CardDbg) GD.Print($"title card: {c}, selection {_cardSel}{(c == TitleCard.RallyFuel ? ", " + FuelCardLine() : c == TitleCard.LapsFuel ? ", " + LapsFuelLine() : "")}");
    }

    /// <summary>The title's card has the keys: Left/Right choose, Enter goes, Esc back to the menu.</summary>
    bool TitleCardInput()
    {
        if (_card == TitleCard.None || _state != State.Title) return false;
        if (_lapsRewardIndex >= 0) return true;
        if (Engine.GetProcessFrames() <= _cardFrame) return true;
        if (_previewMovie) return true;      // --tour-movie: no menu controls
        if (CarTourInput()) return true;     // a car's tour is up (RaceScene.CarTour.cs)
        if (TourInput()) return true;        // a track's tour is up (RaceScene.TrackPreview.cs)
        if (_card == TitleCard.Track && _trackFor != MenuItem.Lan && Input.IsActionJustPressed("tour")) { OpenTour(); return true; }
        if (TrackCardBoardsOffered && Input.IsActionJustPressed("track_boards")) { OpenTrackCardBoards(); return true; }      // L / X: the highlighted track's boards (RaceScene.TrackCardBoards.cs)
        if (_card == TitleCard.RallyFuel && Input.IsActionJustPressed("tour")) { OpenCarTour(); return true; }      // the car of the highlighted fuel
        // One step at a time: the track card has more than two.
        if (Input.IsActionJustPressed("steer_left")) _cardSel = Math.Max(0, _cardSel - 1);
        else if (Input.IsActionJustPressed("steer_right")) _cardSel = Math.Min(CardCount - 1, _cardSel + 1);
        else if (Input.IsActionJustPressed("pause")) _card = TitleCard.None;
        else if (Input.IsActionJustPressed("start") && !Input.IsKeyPressed(Key.Alt))
        {
            if (_card is TitleCard.RallyFuel or TitleCard.LapsFuel && FuelLocked((FuelGrade)_cardSel)) return true;      // (the card says what opens it)
            ChooseTitleCard();
        }
        return true;
    }

    void ChooseTitleCard()
    {
        var c = _card;
        _card = TitleCard.None;
        _overlayExitFrame = Engine.GetProcessFrames();
        if (c == TitleCard.Track) TrackChosen(_cardSel);
        else if (c == TitleCard.RallyFuel)
        {
            _rallyFuel = (FuelGrade)_cardSel; FuelGrades.Save(_records.Dir, _rallyFuel);
            _rallyPhase = DayNight.Phase.Day;      // a new SOCIALIST RALLY always starts at DAY, whatever an earlier mode or run left here (the cycle then goes on lap by lap)
            if (RallyResume.Exists(FileDir, _chosenRunTrack ?? _trackId)) _newRallyTrack = _chosenRunTrack ?? _trackId;
            StartRun(MenuItem.Race);
        }
        else if (c == TitleCard.RallyResume)
        {
            if (_cardSel == 0)
            {
                _rallyFuel = _savedRallyFuel;
                StartRun(MenuItem.Race);
            }
            else OpenCard(TitleCard.RallyFuel);
        }
        else if (c == TitleCard.LapsFuel) { _lapsFuelPick = (FuelGrade)_cardSel; OpenCard(TitleCard.LapsTime); }      // (Esc on TIME OF DAY drops the pick)
        else if (c == TitleCard.LapsTime)
        {
            _lapsPhase = (DayNight.Phase)_cardSel;      // the light then always goes on, lap by lap
            bool free = TrackLoadingScreen.Active == null && _replayViewer is not { State: not ReplayViewerState.Closed };      // (StartRun's own guard: a refused start leaves no pick behind)
            if (free) _lapsFuelStart = _lapsFuelLast = _lapsFuelPick;
            StartRun(MenuItem.Training);
        }
    }

    /// <summary>The Stakhanov laps' CHOOSE FUEL line: the fuel's own line (no rally medals here), or what opens it.</summary>
    string LapsFuelLine()
    {
        var g = (FuelGrade)Math.Clamp(_cardSel, 0, FuelGrades.Count - 1);
        return FuelLocked(g) ? FuelLockedLine(g) : FuelGrades.CardLine((int)g);
    }

    string FuelLockedLine(FuelGrade g) => $"LOCKED: A SILVER ON {TrackName(TrainingTrack)} IN THE RALLY ON {FuelGrades.Names[(int)FuelKey(g).Value]} OPENS IT";

    /// <summary>The power the card's fuel starts this run on (taken once), or null: --power or 100 (NewRunPower).</summary>
    int? TakeLapsFuelStart()
    {
        var g = _lapsFuelStart;
        _lapsFuelStart = null;
        return g is FuelGrade f ? Training.PercentForKey((int)f + 1) : null;      // (the Sputnik is its own 201, not the jet fuel's 200)
    }

    // The FUEL card's line: the power and the chosen track's norms at that pace.
    (string track, int fuel, string text) _fuelLine;

    /// <summary>The fuel that has to win a silver on the bonus track in the rally to open this one: PETROL needs it on DIESEL, JET FUEL on PETROL, SPUTNIK on JET FUEL (null: open).</summary>
    static FuelGrade? FuelKey(FuelGrade g) => g switch { FuelGrade.Petrol => FuelGrade.Diesel, FuelGrade.Rocket => FuelGrade.Petrol, FuelGrade.Sputnik => FuelGrade.Rocket, _ => null };

    bool FuelLocked(FuelGrade g) => !CheatUnlock && FuelKey(g) is FuelGrade k && _campaign.BestOn(TrainingTrack, k) < NormPlan.Medal.Silver;

    /// <summary>The fuel the card opens on: the saved one, or the best open one below it.</summary>
    FuelGrade OpenFuel(FuelGrade g) { while (FuelLocked(g)) g = FuelKey(g).Value; return g; }

    string FuelCardLine()
    {
        if (FuelLocked((FuelGrade)Math.Clamp(_cardSel, 0, FuelGrades.Count - 1)))
            return $"LOCKED: A SILVER ON {TrackName(TrainingTrack)} IN THE RALLY ON {FuelGrades.Names[(int)FuelKey((FuelGrade)Math.Clamp(_cardSel, 0, FuelGrades.Count - 1)).Value]} OPENS IT";
        string track = _chosenRunTrack ?? _trackId;
        int sel = Math.Clamp(_cardSel, 0, FuelGrades.Count - 1);
        if (_fuelLine.text == null || _fuelLine.track != track || _fuelLine.fuel != sel)
        {
            var medal = _campaign.BestOn(track, (FuelGrade)sel);
            _fuelLine = (track, sel, FuelGrades.CardLine(sel) + (medal > NormPlan.Medal.None ? ". " + Campaign.Name(medal) + " MEDAL" : ""));
        }
        return _fuelLine.text;
    }

    int CardCount => _card switch { TitleCard.Track => Tracks.Length, TitleCard.LapsTime => LapsPhases.Length, TitleCard.RallyFuel => FuelGrades.Count, TitleCard.LapsFuel => Training.LapsCount, _ => 2 };

    void CardHud()
    {
        _hud.CardTitle = _card switch
        {
            TitleCard.None => null,
            TitleCard.Track => "CHOOSE TRACK",
            TitleCard.LapsTime => "TIME OF DAY",
            TitleCard.RallyFuel or TitleCard.LapsFuel => "CHOOSE FUEL",
            TitleCard.RallyResume => "SAVED RALLY",
            _ => null,
        };
        _hud.CardSub = _card switch
        {
            TitleCard.Track => TrackCardSub(),
            TitleCard.LapsTime => "THE STAKHANOV LAPS START AT",
            TitleCard.RallyFuel => FuelCardLine(),
            TitleCard.LapsFuel => LapsFuelLine(),
            TitleCard.RallyResume => _cardSel == 0
                ? "CONTINUE WITH " + FuelGrades.Names[(int)_savedRallyFuel]
                : "CHOOSE FUEL FOR A NEW RALLY",
            _ => null,
        };
        _hud.CardOptions = _card switch
        {
            TitleCard.Track => Array.ConvertAll(Tracks, t => t.name),   // a locked one says so under it (TrackCardSub)
            TitleCard.LapsTime => LapsPhases,
            TitleCard.RallyFuel or TitleCard.LapsFuel => FuelGrades.Names,
            TitleCard.RallyResume => new[] { "CONTINUE", "NEW RALLY" },
            _ => Array.Empty<string>(),
        };
        _hud.CardNote = _card == TitleCard.LapsFuel     // the laps' keys still switch the fuel on the move (RaceScene.cs PowerKey, RaceScene.Pad.cs PadPower)
            ? _padMode ? "D-PAD UP/DOWN SWITCHES FUEL WHILE DRIVING" : "KEYS 1-4 SWITCH FUEL WHILE DRIVING"
            : null;
        _hud.CardSelection = _cardSel;
        _hud.CardLocked = _card == TitleCard.Track ? LockedNow() : null;
        if (_card == TitleCard.Track && _trackFor != MenuItem.Lan)
            for (int i = 1; i < Tracks.Length; i++)
                if (!CheatUnlock && !_campaign.LapsPresented(Tracks[i].id, _trackFor == MenuItem.Race)) _hud.CardLocked[i] = true;
        _hud.LapsRewardSource = _lapsRewardIndex >= 0 ? _lapsRewardSource : null;
        _hud.LapsRewardAge = _lapsRewardAge;
        _hud.CardTour = _card == TitleCard.Track && _trackFor != MenuItem.Lan && HasTour(Tracks[Math.Clamp(_cardSel, 0, Tracks.Length - 1)].id)
            || _card == TitleCard.RallyFuel;
        _hud.CardBoards = TrackCardBoardsOffered;
    }

    /// <summary>Every menu-started run shows its controls while loading, then waits for a press.</summary>
    void StartRun(MenuItem mode)
    {
        if (TrackLoadingScreen.Active != null || _replayViewer is { State: not ReplayViewerState.Closed }) return;      // (a run cannot start under an open replay viewer)
        var screen = TrackLoadingScreen.Show(GetTree(), _padMode);
        screen.AdoptMusic(_radio);      // the title music goes on over the card
        ProcessMode = ProcessModeEnum.Disabled;
        BeginLoadingRun(screen, mode);
    }

    async void BeginLoadingRun(TrackLoadingScreen screen, MenuItem mode)
    {
        await screen.PresentLoadingHeading();
        string target = _chosenRunTrack ?? _trackId;
        if (target != _trackId)
        {
            _trackId = target;
            _loadingRunMode = mode;
            var error = GetTree().ReloadCurrentScene();
            if (error != Error.Ok)
            {
                _loadingRunMode = null;
                _newRallyTrack = null;
                _lapsFuelStart = null;
                ProcessMode = ProcessModeEnum.Inherit;
                screen.Cancel();
                GD.PushError($"Cannot load track: {error}");
            }
            return;
        }
        FinishLoadingRun(mode);
    }

    void FinishLoadingRun(MenuItem mode)
    {
        _showLog = _askName = false;
        PrepareRun(mode);
        if (mode == MenuItem.Race) RestoreRallyRun();
        UpdateHud();
        ProcessMode = ProcessModeEnum.Disabled;
        Audio.AudioMix.MuteSfx(true);
        _carRadio.Suspend(true);
        TrackLoadingScreen.Active?.Loaded(() =>
        {
            CoverSetsProcessMode(ProcessModeEnum.Inherit);
            _overlayExitFrame = Engine.GetProcessFrames();
            Audio.AudioMix.MuteSfx(false);
            _carRadio.Suspend(false);
            ConsumeRallyCheckpoint();
            // (no norms table here: the Socialist Rally goes straight into its countdown; NORMS AND MEDALS stays in the Party Meeting and the target card shows the current norm)
        });
    }

    void ConsumeRallyCheckpoint()
    {
        if (!_resumeLoaded && _newRallyTrack != _trackId) return;
        var error = RallyResume.Clear(FileDir, _trackId);
        if (error != Error.Ok)
        {
            GD.PushError($"Cannot consume rally checkpoint: {error}");
            _hud.Alert("SAVE COULD NOT BE REMOVED", "SAVING THIS RALLY WILL REPLACE IT", 5f, Hud.Red, urgent: true);
        }
        _resumeLoaded = false;
        _newRallyTrack = null;
    }

    void PrepareRun(MenuItem mode)
    {
        MenuPlayed(mode);
        if (!_labMode) PlayerFiles.Touch(_records.Dir, _file);     // the file's last played (a lab run leaves no mark)
        _runMedal = NormPlan.Medal.None;
        _rallyNextOpenedThisRun = false;
        _run = mode == MenuItem.Training ? RunMode.Training : RunMode.Rally;
        NewRunPower(mode == MenuItem.Training ? TakeLapsFuelStart() : null);
        _carRadio.SetOn(RadioPreference(), false);
        DefaultComments();
        EnterCountdown();
    }
}
