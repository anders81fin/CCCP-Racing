using System;
using System.Collections;
using System.Linq;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

// Windowed scenarios of the Stakhanov laps' start through the real title (--viewer-windowed-only=stakhanovstart):
// "stakhanov-start": SINGLE PLAYER > STAKHANOV LAPS > CHOOSE TRACK > CHOOSE FUEL > TIME OF DAY (no THE LIGHT card) on a fresh profile: a locked fuel does not start,
//   Esc on either card goes back to the menu with nothing started or saved, the pad's words on the card, the chosen JET FUEL drives the run on the same track, the run starts
//   at the chosen phase and the light then advances lap by lap, keys 1-4 and the D-pad still switch the fuel on the move, and a restart keeps the fuel driven.
// "stakhanov-fuel-reload" (--unlock-all --power=50): SPUTNIK chosen for another track survives the real ReloadCurrentScene (the new scene parses --power=50 again) with its AWD.
// "stakhanov-legacy" (--start --training --power=201 --unlock-all): the command line start still drives the --power fuel.
public partial class RaceScene
{
    /// <summary>Settings' [rally] fuel as on file (the Stakhanov card must never write it).</summary>
    string VwRallyFuelOnFile()
    {
        var cfg = new ConfigFile();
        return cfg.Load(_records.Dir + "settings.cfg") != Error.Ok ? "<no settings.cfg>" : cfg.HasSectionKey("rally", "fuel") ? (string)cfg.GetValue("rally", "fuel") : "<no rally fuel>";
    }

    /// <summary>The engine the car really has now: the fuel, its power value, the drive split, the turbo and the redline.</summary>
    string VwEngineNow() => $"power {_power}, fuel {EngineFuel}, label {_hud.PowerLabel}, drive front {_car.P.DriveFront:0.00}, centre lock {_car.P.CenterLock:0.00}, turbo gain {_car.TurboGain:0.000}, redline {_car.P.Redline:0}";

    bool VwEngineIs(FuelGrade g) => _power == Training.PercentForKey((int)g + 1) && EngineFuel == g && _hud.PowerLabel == FuelGrades.Names[(int)g]
        && Math.Abs(_car.P.DriveFront - FuelGrades.DriveFrontFor(g)) < 1e-4f && Math.Abs(_car.TurboGain - FuelGrades.TurboGainFor(g)) < 1e-4f
        && Math.Abs(_car.P.Redline - (g == FuelGrade.Diesel ? 5000f : new CCCPRacing.Car.CarParams().Redline)) < 1e-3f;

    /// <summary>The title's top row to SINGLE PLAYER > STAKHANOV LAPS (keyboard or pad).</summary>
    IEnumerable VwToStakhanov(bool pad)
    {
        for (int i = 0; i < 6 && _menuParent != null; i++) foreach (var x in pad ? VrPad(JoyButton.B) : VrTap(Key.Escape)) yield return x;
        for (int i = 0; i < 6 && _menu != MenuItem.Single; i++) foreach (var x in pad ? VrPad(JoyButton.DpadLeft) : VrTap(Key.Left)) yield return x;
        foreach (var x in pad ? VrPad(JoyButton.A) : VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        for (int i = 0; i < 4 && _menu != MenuItem.Training; i++) foreach (var x in pad ? VrPad(JoyButton.DpadRight) : VrTap(Key.Right)) yield return x;
        VwCheck(_menuParent == MenuItem.Single && _menu == MenuItem.Training, $"STAKHANOV LAPS is selected ({(pad ? "pad" : "keyboard")})");
    }

    bool VwFuelCardUp(bool pad) => _card == TitleCard.LapsFuel && _hud.CardTitle == "CHOOSE FUEL" && _hud.CardOptions.SequenceEqual(FuelGrades.Names) && !_hud.CardTour
        && _hud.CardNote == (pad ? "D-PAD UP/DOWN SWITCHES FUEL WHILE DRIVING" : "KEYS 1-4 SWITCH FUEL WHILE DRIVING");

    bool VwNothingStarted() => _state == State.Title && TrackLoadingScreen.Active == null && _lapsFuelStart == null;

    /// <summary>From the TIME OF DAY Enter to racing: the loading card's press and the clutch held through the countdown (the scene may be rebuilt: who() finds the live one).</summary>
    static IEnumerable VwStakhanovGo(Func<RaceScene> who, ViewerWindowedDriver drv, Action<bool> raced)
    {
        int readyFor = 0, controlOff = -1; bool clutchHeld = false, countdown = false, racing = false;
        for (int frames = 0; frames < 60 * 150 && !racing; frames++)
        {
            yield return null;
            var s = who();
            if (TrackLoadingScreen.Active is { IsReady: true } && ++readyFor == 30)
            {
                drv.SetControl(true); controlOff = frames + 5;
                Input.ParseInputEvent(VwKey(Key.Enter, true, 0)); Input.ParseInputEvent(VwKey(Key.Enter, false, 0));
            }
            if (s != null && TrackLoadingScreen.Active == null)
            {
                if (s._state == State.Countdown && !clutchHeld) { clutchHeld = true; drv.SetControl(true); controlOff = frames + 5; Input.ParseInputEvent(VwKey(Key.Shift, true, 0)); }
                else if (s._state == State.Racing && clutchHeld) { clutchHeld = false; drv.SetControl(true); controlOff = frames + 5; Input.ParseInputEvent(VwKey(Key.Shift, false, 0)); }
                if (s._state == State.Countdown) countdown = true;
                if (s._state == State.Racing && countdown) racing = true;
            }
            if (controlOff >= 0 && frames >= controlOff) { drv.SetControl(false); controlOff = -1; }
        }
        raced(racing);
    }

    IEnumerable VwStakhanovStart()
    {
        foreach (var x in VwWaitReady()) yield return x;
        for (int i = 0; i < 600 && !(_state == State.Title && _card == TitleCard.None && !_showLog && !_askName); i++) yield return null;
        for (int i = 0; i < 90; i++) yield return null;
        VwCheck(_state == State.Title && _menuParent == null, "the title's top row is up");
        var rallyBefore = _rallyFuel; string rallyFileBefore = VwRallyFuelOnFile();
        VwLog("stakhanov-fuel-before", ("rally_fuel", rallyBefore), ("rally_fuel_on_file", rallyFileBefore), ("power_start", _powerStart));
        _padMode = false;      // (the keyboard's words first, whatever pad is plugged in)
        foreach (var x in VwToStakhanov(false)) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_card == TitleCard.Track, "the track card comes first");
        foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        // 1. a fresh profile: only DIESEL is open (PETROL needs a silver on the bonus track on DIESEL), the card opens on it
        VwCheck(VwFuelCardUp(false), $"after the track comes CHOOSE FUEL with the four fuels and the keyboard's hint, no tour ({_card}, '{_hud.CardTitle}', '{_hud.CardNote}', tour {_hud.CardTour})");
        VwCheck(_cardSel == (int)FuelGrade.Diesel && FuelLocked(FuelGrade.Petrol) && _hud.CardSub == FuelGrades.CardLine((int)FuelGrade.Diesel),
            $"a fresh profile: the card opens on the open DIESEL with its line ('{_hud.CardSub}')");
        foreach (var x in VrShot("stakhanov-1-choose-fuel-keys")) yield return x;
        foreach (var x in VrTap(Key.Right)) yield return x;
        for (int i = 0; i < 4; i++) yield return null;
        VwCheck(_cardSel == (int)FuelGrade.Petrol && _hud.CardSub.StartsWith("LOCKED: A SILVER ON") && _hud.CardSub.EndsWith("ON DIESEL OPENS IT"), $"PETROL is locked and says what opens it ('{_hud.CardSub}')");
        foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 20; i++) yield return null;
        VwCheck(_card == TitleCard.LapsFuel && _cardSel == (int)FuelGrade.Petrol && VwNothingStarted(), $"Enter on the locked PETROL does nothing: the card stays, no loading, no run ({_card}, {_state}, loading {TrackLoadingScreen.Active != null})");
        foreach (var x in VrShot("stakhanov-2-locked-petrol")) yield return x;
        foreach (var x in VrTap(Key.Escape)) yield return x;
        for (int i = 0; i < 8; i++) yield return null;
        VwCheck(_card == TitleCard.None && _menuParent == MenuItem.Single && _menu == MenuItem.Training && VwNothingStarted(), $"Esc on CHOOSE FUEL goes back to the menu on STAKHANOV LAPS, nothing started ({_card}, {_menuParent}/{_menu})");
        VwCheck(_rallyFuel == rallyBefore && VwRallyFuelOnFile() == rallyFileBefore, $"the rally's fuel is untouched ({_rallyFuel}, on file {VwRallyFuelOnFile()})");
        // 2. medals open PETROL and JET FUEL (on this temporary profile only; SPUTNIK stays locked); the pad's way: its hint, a pick and B on TIME OF DAY drops it
        _campaign.Award(TrainingTrack, NormPlan.Medal.Silver, FuelGrade.Diesel); _campaign.Award(TrainingTrack, NormPlan.Medal.Silver, FuelGrade.Petrol);
        VwCheck(!FuelLocked(FuelGrade.Petrol) && !FuelLocked(FuelGrade.Rocket) && FuelLocked(FuelGrade.Sputnik), "test medals: PETROL and JET FUEL open, SPUTNIK locked");
        rallyFileBefore = VwRallyFuelOnFile();
        _padMode = true;
        foreach (var x in VwToStakhanov(true)) yield return x;
        foreach (var x in VrPad(JoyButton.A)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_card == TitleCard.Track, "pad: the track card");
        foreach (var x in VrPad(JoyButton.A)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(VwFuelCardUp(true) && _cardSel == (int)FuelGrade.Petrol, $"pad: CHOOSE FUEL with the pad's hint, on PETROL (the default 100 %) ({_card}, sel {_cardSel}, '{_hud.CardNote}')");
        foreach (var x in VrPad(JoyButton.DpadRight)) yield return x;
        for (int i = 0; i < 4; i++) yield return null;
        VwCheck(_cardSel == (int)FuelGrade.Rocket && _hud.CardSub == FuelGrades.CardLine((int)FuelGrade.Rocket), $"pad: D-pad right to JET FUEL ('{_hud.CardSub}')");
        foreach (var x in VrShot("stakhanov-3-choose-fuel-pad")) yield return x;
        foreach (var x in VrPad(JoyButton.A)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_card == TitleCard.LapsTime && _hud.CardTitle == "TIME OF DAY" && VwNothingStarted(), $"pad: A on JET FUEL opens TIME OF DAY, nothing started yet ({_card})");
        foreach (var x in VrPad(JoyButton.B)) yield return x;
        for (int i = 0; i < 8; i++) yield return null;
        VwCheck(_card == TitleCard.None && _menuParent == MenuItem.Single && _menu == MenuItem.Training && VwNothingStarted() && _lapsFuelLast == null,
            $"pad: B on TIME OF DAY goes back to the menu, nothing started, the pick dropped ({_card}, last {_lapsFuelLast})");
        _padMode = false;
        // 3. the keyboard's run: JET FUEL, SUNSET
        foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(VwFuelCardUp(false) && _cardSel == (int)FuelGrade.Petrol, $"the card opens on PETROL again: a cancelled pick is not kept (sel {_cardSel})");
        foreach (var x in VrTap(Key.Right)) yield return x;
        for (int i = 0; i < 4; i++) yield return null;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_card == TitleCard.LapsTime && _hud.CardTitle == "TIME OF DAY", $"after the fuel comes TIME OF DAY ({_card})");
        foreach (var x in VrShot("stakhanov-4-time-of-day")) yield return x;
        foreach (var x in VrTap(Key.Right)) yield return x;      // SUNSET
        for (int i = 0; i < 4; i++) yield return null;
        VwCheck(_cardSel == (int)DayNight.Phase.Sunset, "SUNSET chosen");
        foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 4; i++) yield return null;
        VwCheck(_card == TitleCard.None, $"no further card (THE LIGHT is gone): the run goes straight on ({_card})");
        bool racing = false;
        foreach (var x in VwStakhanovGo(() => this, _vwDrv, r => racing = r)) yield return x;
        VwCheck(racing && _run == RunMode.Training && _trackId == "ilyich", "Stakhanov laps are racing on the same track");
        for (int i = 0; i < 30; i++) yield return null;
        VwCheck(VwEngineIs(FuelGrade.Rocket) && _lapsFuelStart == null, $"the run drives the chosen JET FUEL ({VwEngineNow()})");
        VwCheck(_lapPeak == 200 && _lapFuels.Count > 0 && _lapFuels[0] == (0f, (int)FuelGrade.Rocket), $"the lap's peak and its fuel timeline start on JET FUEL (peak {_lapPeak}, first {(_lapFuels.Count > 0 ? _lapFuels[0].ToString() : "none")})");
        VwCheck(_rallyFuel == rallyBefore && VwRallyFuelOnFile() == rallyFileBefore, $"the rally's fuel is still untouched ({_rallyFuel}, on file {VwRallyFuelOnFile()})");
        int first = DayNight.FirstLap(DayNight.Phase.Sunset);
        VwCheck(_dayNight.LapOffset == first - 1 && DayNight.PhaseForLap(_runLaps + _dayNight.LapOffset + 1) == DayNight.Phase.Sunset, $"it starts in SUNSET (offset {_dayNight.LapOffset}, lap-driven phase {DayNight.PhaseForLap(_runLaps + _dayNight.LapOffset + 1)})");
        float clock0 = _dayNight.Clock;
        foreach (var x in VrShot("stakhanov-5-start-sunset-jet")) yield return x;
        _timer.CompleteLapForTest(80f); _timer.CompleteLapForTest(80f);
        for (int i = 0; i < 120; i++) yield return null;
        var now = DayNight.PhaseForLap(_runLaps + _dayNight.LapOffset + 1);
        VwCheck(_runLaps >= 2 && now == DayNight.Phase.Dusk, $"two laps on, the light has moved on to {now} (laps driven {_runLaps})");
        VwCheck(_dayNight.LapsDriven == first - 1 + 2 && _dayNight.Clock > clock0 + 0.05f, $"the light clock heads for the new lap (laps driven incl. start {_dayNight.LapsDriven}; the clock never jumps, it catches up at five laps a minute: {clock0:0.00} -> {_dayNight.Clock:0.00})");
        foreach (var x in VrShot("stakhanov-6-after-two-laps")) yield return x;
        // 4. on the move: key 1 DIESEL, key 4 the locked SPUTNIK refused, D-pad up PETROL
        foreach (var x in VrTap(Key.Key1)) yield return x;
        for (int i = 0; i < 10; i++) yield return null;
        VwCheck(_state == State.Racing && VwEngineIs(FuelGrade.Diesel), $"key 1 while driving: DIESEL's engine ({VwEngineNow()})");
        foreach (var x in VrTap(Key.Key4)) yield return x;
        for (int i = 0; i < 10; i++) yield return null;
        VwCheck(VwEngineIs(FuelGrade.Diesel), $"key 4 while driving: the locked SPUTNIK is refused, DIESEL stays ({VwEngineNow()})");
        foreach (var x in VrPad(JoyButton.DpadUp)) yield return x;
        for (int i = 0; i < 10; i++) yield return null;
        VwCheck(_state == State.Racing && VwEngineIs(FuelGrade.Petrol), $"pad D-pad up while driving: one step up to PETROL ({VwEngineNow()})");
        foreach (var x in VrShot("stakhanov-7-switched-petrol")) yield return x;
        // 5. a restart on the same track keeps the fuel driven (it does not go back to the card's)
        foreach (var x in VrTap(Key.Escape)) yield return x;
        for (int i = 0; i < 12; i++) yield return null;
        VwCheck(_state == State.Paused && _hud.PauseRowIds.Contains(PauseRowId.Restart), "the Party Meeting has RESTART");
        for (int i = 0; i < 16 && _hud.PauseSelection != PauseRowIndex(PauseRowId.Restart); i++)
            foreach (var x in VrTap(_hud.PauseSelection < PauseRowIndex(PauseRowId.Restart) ? Key.Down : Key.Up)) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 20; i++) yield return null;
        VwCheck(_state == State.Countdown && _run == RunMode.Training && VwEngineIs(FuelGrade.Petrol) && _trackId == "ilyich", $"RESTART: the countdown again with the fuel driven last ({_state}, {VwEngineNow()})");
    }

    IEnumerable VwStakhanovFuelReload()
    {
        foreach (var x in VwWaitReady()) yield return x;
        for (int i = 0; i < 600 && !(_state == State.Title && _card == TitleCard.None && !_showLog && !_askName); i++) yield return null;
        for (int i = 0; i < 90; i++) yield return null;
        var tree = GetTree(); var old = this; var drv = _vwDrv;
        string from = _trackId, target = "siberia";
        VwCheck(from == "ilyich" && _powerStart == 50 && CheatUnlock, $"built on {from}, --power=50 parsed ({_powerStart}), --unlock-all ({CheatUnlock})");
        foreach (var x in VwToStakhanov(false)) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        int ti = Array.FindIndex(Tracks, t => t.id == target);
        for (int i = 0; i < 6 && _cardSel != ti; i++) foreach (var x in VrTap(Key.Right)) yield return x;
        VwCheck(_card == TitleCard.Track && _cardSel == ti && !Locked(ti), $"the track card on {target} (open)");
        foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(VwFuelCardUp(false) && _cardSel == (int)FuelGrade.Diesel, $"CHOOSE FUEL opens on --power=50's DIESEL (sel {_cardSel})");
        for (int i = 0; i < 4 && _cardSel != (int)FuelGrade.Sputnik; i++) foreach (var x in VrTap(Key.Right)) yield return x;
        VwCheck(_cardSel == (int)FuelGrade.Sputnik && _hud.CardSub == "TOP SECRET: AWD PROTOTYPE", $"SPUTNIK highlighted with its line ('{_hud.CardSub}')");
        foreach (var x in VrShot("stakhanov-reload-1-choose-sputnik")) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_card == TitleCard.LapsTime, $"TIME OF DAY after the fuel ({_card})");
        VwLog("stakhanov-reload-start", ("from", from), ("to", target));
        foreach (var x in VrTap(Key.Enter)) yield return x;      // (from here on this instance is freed by the reload: statics, locals and the fresh scene only)
        RaceScene fresh = null;
        RaceScene Who() { var f = VwFindLive(tree.Root); if (f != null && !ReferenceEquals(f, old) && fresh == null) { fresh = f; fresh._vwDrv = drv; } return fresh; }
        bool racing = false;
        foreach (var x in VwStakhanovGo(Who, drv, r => racing = r)) yield return x;
        VwCheck(fresh != null && racing, $"the scene was rebuilt and Stakhanov laps are racing (fresh {fresh != null}, racing {racing})");
        if (fresh == null) yield break;
        foreach (var x in fresh.VwStakhanovFuelReloadOn(target)) yield return x;
    }

    /// <summary>The rebuilt scene's half of stakhanov-fuel-reload (its own viewport, its own fields).</summary>
    IEnumerable VwStakhanovFuelReloadOn(string target)
    {
        for (int i = 0; i < 30; i++) yield return null;
        VwCheck(_trackId == target && _run == RunMode.Training && _powerStart == 50, $"the run is on {_trackId}, Stakhanov laps; the new scene parsed --power=50 again ({_powerStart})");
        VwCheck(VwEngineIs(FuelGrade.Sputnik) && _power == Training.SputnikPercent && _car.P.CenterLock > 0.3f && _lapsFuelStart == null,
            $"the chosen SPUTNIK survived the rebuild: its own 201 and four-wheel drive, not --power's diesel ({VwEngineNow()})");
        VwCheck(_lapPeak == Training.SputnikPercent && _lapFuels.Count > 0 && _lapFuels[0] == (0f, (int)FuelGrade.Sputnik), $"the lap's peak and its fuel timeline start on SPUTNIK (peak {_lapPeak}, first {(_lapFuels.Count > 0 ? _lapFuels[0].ToString() : "none")})");
        foreach (var x in VrShot("stakhanov-reload-2-sputnik-racing")) yield return x;
        foreach (var x in VrTap(Key.Key3)) yield return x;
        for (int i = 0; i < 10; i++) yield return null;
        VwCheck(VwEngineIs(FuelGrade.Rocket) && _car.P.DriveFront == 0f, $"key 3: JET FUEL's 200 and rear drive (the Sputnik is another fuel) ({VwEngineNow()})");
        foreach (var x in VrTap(Key.Key4)) yield return x;
        for (int i = 0; i < 10; i++) yield return null;
        VwCheck(VwEngineIs(FuelGrade.Sputnik), $"key 4: SPUTNIK again ({VwEngineNow()})");
    }

    IEnumerable VwStakhanovLegacy()
    {
        foreach (var x in VwWaitReady()) yield return x;
        for (int i = 0; i < 60 * 30 && _state != State.Countdown; i++) yield return null;
        for (int i = 0; i < 30; i++) yield return null;
        VwCheck(_state is State.Countdown or State.Racing && _run == RunMode.Training && _card == TitleCard.None, $"--start --training: straight into Stakhanov laps, no card ({_state}, {_run}, {_card})");
        VwCheck(VwEngineIs(FuelGrade.Sputnik) && _powerStart == Training.SputnikPercent && _lapsFuelStart == null, $"--power=201 still picks the start fuel ({VwEngineNow()})");
    }
}
