using System;
using System.Globalization;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// Day and night (DayNight) in the rally, STAKHANOV LAPS and LAN races: the laps driven in the run
/// set the time of day, the phase changes are announced (in the rally with the climate change
/// allowance), and a lap started at dusk or at night goes on the board with a night mark. In a LAN
/// race the host's leading car sets the shared clock. The SOCIALIST RALLY starts at the time of day the driver
/// chose (the TIME OF DAY card; the day then goes on lap by lap); STAKHANOV LAPS start at the time of day the driver chose (the TIME OF DAY card) and the light
/// always goes on lap by lap; a LAN race draws one of five
/// starting phases from the host's seed, then advances one phase per leading-car lap. Tests: --phase=day|sunset|dusk|night|dawn or
/// --laps-driven=N start the rally that far on, --time-of-day=0..1 pins the clock (0 the start of
/// the run, 1 the end of the dawn), --one-beam gives the car one combined headlight; env DAYNIGHTDBG.
/// </summary>
public partial class RaceScene
{
    DayNight _dayNight;
    DirectionalLight3D _sunLight;
    MeshInstance3D _terrainMesh;
    int _runLaps;                 // laps completed in this run, void ones too (a restart request keeps them)
    bool _lapNight;               // the lap just completed was a night lap
    bool _dnNewRun = true;
    bool _dnPinned;               // --phase / --laps-driven chose the start: no draw
    bool _dnDraw;                 // a new run's time of day is still to be drawn
    float _climateAlertAt = -1, _climateSayAt = -1;
    float? _dnTestPin;            // --time-of-day
    DayNight.Phase? _lanLastLightPhase;
    static readonly int[] LanLightBoundaries = { 0, 2, 4, 6, 10, 12 };

    static int LanLightStart(int seed) => (int)(((uint)seed * 2654435761u) >> 8) % 5;

    /// <summary>One lighting phase per LAN lap: day, sunset, dusk, night and dawn, then repeat.</summary>
    public static float LanLightClock(float leadingLaps, int seed)
    {
        int start=LanLightStart(seed), whole=Math.Max(0,(int)MathF.Floor(leadingLaps));
        float clock=LanLightBoundaries[start];
        for(int lap=0;lap<whole;lap++)
        {
            int phase=(start+lap)%5;
            clock+=LanLightBoundaries[phase+1]-LanLightBoundaries[phase];
        }
        int now=(start+whole)%5;
        return clock+(leadingLaps-whole)*(LanLightBoundaries[now+1]-LanLightBoundaries[now]);
    }
    // Stakhanov laps: the time of day chosen on the title's cards (kept until the game closes) (the light always goes on, lap by lap).
    static DayNight.Phase _lapsPhase = DayNight.Phase.Day;
    static DayNight.Phase _rallyPhase = DayNight.Phase.Day;      // the rally's start, chosen on its TIME OF DAY card (kept until the game closes)
    /// <summary>The rally's fuel (RaceScene.Fuel.cs, FuelGrades): chosen on the FUEL card, kept until the game closes.</summary>
    static FuelGrade _rallyFuel = FuelGrade.Petrol;
    static readonly string[] LapsPhases = { "DAY", "SUNSET", "DUSK", "NIGHT", "DAWN" };   // in DayNight.Phase order

    /// <summary>The time of day, for the norm check: the climate's allowance for the lap being driven.</summary>
    public DayNight DayNightNode => _dayNight;

    /// <summary>End of _Ready: the world is built, the night can collect its materials and hang its lamps.</summary>
    void InitDayNight()
    {
        _dayNight = new DayNight { DeferInitialCollect = _booting };
        foreach (string a in CmdArgs())
        {
            if (a.StartsWith("--phase=") && DayNight.Parse(a["--phase=".Length..]) is { } p) { _dayNight.LapOffset = DayNight.FirstLap(p) - 1; _dnPinned = true; }  // test: the run starts at the first lap of this phase
            else if (a.StartsWith("--laps-driven=")) { _dayNight.LapOffset = int.Parse(a["--laps-driven=".Length..]); _dnPinned = true; }   // test: the run starts with N laps already driven
            else if (a.StartsWith("--time-of-day=")) _dnTestPin = DayNight.CycleLaps * float.Parse(a["--time-of-day=".Length..], CultureInfo.InvariantCulture);  // test: the light pinned (0 start .. 1 end of the first dawn)
            else if (a == "--one-beam") _dayNight.OneBeam = true;         // test: one combined headlight beam
            else if (a.StartsWith("--laps-time=") && DayNight.Parse(a["--laps-time=".Length..]) is { } lp) _lapsPhase = lp;   // test: the TIME OF DAY card's choice for Stakhanov laps
        }
        _dayNight.PinnedClock = _dnTestPin;
        _dayNight.Init(_env, _sunLight, _terrainMesh, _track, _carVis, _busAll, _kozlikAll, _jetsAll, _tanksAll, _kgb, _fx);
        _dayNight.Truck = _truck;
        _dayNight.Tractor = _tractorAll;
        _dayNight.Rescue = _rescue;
        _dayNight.Slurry = _slurryAll;
        if (_weather != null)
        {
            _dayNight.Overcast = Weather.OvercastAt;
            _dayNight.ExtraFog = c => Weather.FogAt(c) * 0.03f + Weather.RainAt(c) * 0.004f;
        }
        AddChild(_dayNight);
    }

    /// <summary>A rally run from the title (not a restart request): the day starts again.</summary>
    void DayNightNewRun()
    {
        _runLaps = 0;
        _lanLastLightPhase = null;
        _dnNewRun = true;
        _dnDraw = true;           // drawn on the first frame (a --start run begins before InitDayNight reads --phase)
        _climateAlertAt = _climateSayAt = -1;
    }

    /// <summary>A lap (valid or void) reached the line: count it, and announce a new phase.</summary>
    void DayNightLap()
    {
        _lapNight = false;
        _runLaps++;
        if (InLanRace) return; // LAN phase changes belong to the shared leading-car clock
        int lap = _runLaps + _dayNight.LapOffset;              // the lap just completed, 1-based
        _lapNight = DayNight.IsNight(DayNight.PhaseForLap(lap));
        var next = DayNight.PhaseForLap(lap + 1);
        if (OS.HasEnvironment("DAYNIGHTDBG")) GD.Print($"daynight: lap {lap} done ({DayNight.PhaseForLap(lap)}{(_lapNight ? ", a night lap" : "")}), lap {lap + 1} is {next}, allowance +{DayNight.AllowanceFor(next):0.0} s");
        if (next == DayNight.PhaseForLap(lap)) return;
        _climateAlertAt = _clock + 1.2f;       // after the lap time has had its moment
        _climateSayAt = _clock + 1.6f;
    }

    /// <summary>Per frame: feed the night the run's laps and the lap's progress; alerts when due.</summary>
    void DayNightFrame(float dt)
    {
        bool active = _state != State.Title && !InSchool;
        // The title's replay is lit as its lap was driven (records from before 0.51 stay in the plain day).
        bool replayLit = Replaying && TitleGhost.HasDayClock;
        bool schoolLit = (InSchool || TrackPreviewActive) && _scLightClock.HasValue;
        if (_titleRec && active) TitleRecLightTick(dt);
        _dayNight.PinnedClock = schoolLit ? _scLightClock : replayLit ? TitleGhost.DayClockAt(_replayClock)
            : _titleRec && active ? _titleRecClock : _dnTestPin ?? (active && InLanRace && _lanReveal != null ? LanLightClock(_lanSharedLightProgress,_lanReveal.Seed)
            : null);
        if (_dnDraw && active && !_dnPinned)
        {
            _dnDraw = false;
            // LAN uses its host-seeded phase in the shared clock; other modes use the selected starting phase.
            _dayNight.LapOffset = InLanRace ? 0
                : StakhanovRules ? DayNight.FirstLap(_lapsPhase) - 1 : DayNight.FirstLap(_rallyPhase) - 1;
            if (OS.HasEnvironment("DAYNIGHTDBG")) GD.Print($"daynight: the run starts at lap {_dayNight.LapOffset + 1} of the day ({DayNight.PhaseForLap(_dayNight.LapOffset + 1)})");
        }
        float frac = 0;
        if (_state is State.Racing or State.Paused or State.Failed or State.Finished)
        {
            int n = _track.Center.Length;
            frac = (float)_timer.Progress / n;
            if (_timer.NextGate <= 1 && frac > 0.5f) frac = 0;           // still behind the line at the start of the lap
            if (_timer.NextGate >= _timer.GateCount && frac < 0.5f) frac = 1;   // over the line before the lap counts
        }
        int lightLaps = InLanRace && _dayNight.PinnedClock is float shared ? (int)MathF.Floor(shared) : _runLaps;
        _dayNight.Update(dt, active || replayLit || schoolLit, lightLaps, frac, _dnNewRun && active, _car.Brake);
        _weather?.Tick(_dayNight.Active ? _dayNight.Clock : _dayNight.PinnedClock ?? 0.5f, dt);
        if (active) _dnNewRun = false;
        if (_state != State.Racing || InSchool) return;
        var phase = _dayNight.LapPhase;
        if (InLanRace)
        {
            if (_lanLastLightPhase is { } previous && previous != phase)
            {
                _climateAlertAt = _clock + 1.2f;
                _climateSayAt = _clock + 1.6f;
            }
            _lanLastLightPhase = phase;
        }
        if (_climateAlertAt > 0 && _clock >= _climateAlertAt)
        {
            _climateAlertAt = -1;
            string head = StakhanovRules || phase == DayNight.Phase.Day ? DayNight.Title(phase)       // no norm eased: just the time of day
                : $"CLIMATE CHANGE ALLOWANCE: +{(DayNight.AllowanceFor(phase) * ClimateScale()).ToString("0.0", CultureInfo.InvariantCulture)} SEC";
            _hud.Alert(head, DayNight.Motto(phase), 4.5f, phase == DayNight.Phase.Night ? Hud.Paper : Hud.Gold, StakhanovRules ? Prio.Nice : Prio.Important);
        }
        if (_climateSayAt > 0 && _clock >= _climateSayAt)
        {
            _climateSayAt = -1;
            _kgb.Say("КЛИМАТ!", null, 2.5f);
        }
    }
}
