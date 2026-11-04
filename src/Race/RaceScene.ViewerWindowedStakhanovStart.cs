using System;
using System.Collections;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

// Windowed scenario "stakhanov-start" (--viewer-windowed-only=stakhanovstart): SINGLE PLAYER > STAKHANOV LAPS asks only TIME OF DAY (no THE LIGHT card), starts at the chosen phase
// and the light then advances lap by lap, whatever an earlier choice left behind.
public partial class RaceScene
{
    IEnumerable VwStakhanovStart()
    {
        foreach (var x in VwWaitReady()) yield return x;
        for (int i = 0; i < 600 && !(_state == State.Title && _card == TitleCard.None && !_showLog && !_askName); i++) yield return null;
        for (int i = 0; i < 90; i++) yield return null;
        VwCheck(_state == State.Title && _menuParent == null, "the title's top row is up");
        for (int i = 0; i < 6 && _menu != MenuItem.Single; i++) foreach (var x in VrTap(Key.Left)) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        for (int i = 0; i < 4 && _menu != MenuItem.Training; i++) foreach (var x in VrTap(Key.Right)) yield return x;
        VwCheck(_menuParent == MenuItem.Single && _menu == MenuItem.Training, "STAKHANOV LAPS is selected");
        foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_card == TitleCard.Track, "the track card comes first");
        foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_card == TitleCard.LapsTime && _hud.CardTitle == "TIME OF DAY", $"the first card is TIME OF DAY ({_card})");
        foreach (var x in VrShot("stakhanov-1-time-of-day")) yield return x;
        foreach (var x in VrTap(Key.Right)) yield return x;      // SUNSET
        for (int i = 0; i < 4; i++) yield return null;
        VwCheck(_cardSel == (int)DayNight.Phase.Sunset, "SUNSET chosen");
        foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 4; i++) yield return null;
        VwCheck(_card == TitleCard.None, $"no second card (THE LIGHT is gone): the run goes straight on ({_card})");
        int readyFor = 0, controlOff = -1; bool clutchHeld = false, countdown = false, racing = false;
        for (int frames = 0; frames < 60 * 90 && !racing; frames++)
        {
            yield return null;
            if (TrackLoadingScreen.Active is { IsReady: true } && ++readyFor == 30)
            {
                _vwDrv.SetControl(true); controlOff = frames + 5;
                Input.ParseInputEvent(VwKey(Key.Enter, true, 0)); Input.ParseInputEvent(VwKey(Key.Enter, false, 0));
            }
            if (_state == State.Countdown && !clutchHeld) { clutchHeld = true; _vwDrv.SetControl(true); controlOff = frames + 5; Input.ParseInputEvent(VwKey(Key.Shift, true, 0)); }
            else if (_state == State.Racing && clutchHeld) { clutchHeld = false; _vwDrv.SetControl(true); controlOff = frames + 5; Input.ParseInputEvent(VwKey(Key.Shift, false, 0)); }
            if (controlOff >= 0 && frames >= controlOff) { _vwDrv.SetControl(false); controlOff = -1; }
            if (_state == State.Countdown) countdown = true;
            if (_state == State.Racing && countdown) racing = true;
        }
        VwCheck(racing && _run == RunMode.Training, "Stakhanov laps are racing");
        for (int i = 0; i < 30; i++) yield return null;
        int first = DayNight.FirstLap(DayNight.Phase.Sunset);
        VwCheck(_dayNight.LapOffset == first - 1 && DayNight.PhaseForLap(_runLaps + _dayNight.LapOffset + 1) == DayNight.Phase.Sunset, $"it starts in SUNSET (offset {_dayNight.LapOffset}, lap-driven phase {DayNight.PhaseForLap(_runLaps + _dayNight.LapOffset + 1)})");
        float clock0 = _dayNight.Clock;
        foreach (var x in VrShot("stakhanov-2-start-sunset")) yield return x;
        _timer.CompleteLapForTest(80f); _timer.CompleteLapForTest(80f);
        for (int i = 0; i < 120; i++) yield return null;
        var now = DayNight.PhaseForLap(_runLaps + _dayNight.LapOffset + 1);
        VwCheck(_runLaps >= 2 && now == DayNight.Phase.Dusk, $"two laps on, the light has moved on to {now} (laps driven {_runLaps})");
        VwCheck(_dayNight.LapsDriven == first - 1 + 2 && _dayNight.Clock > clock0 + 0.05f, $"the light clock heads for the new lap (laps driven incl. start {_dayNight.LapsDriven}; the clock never jumps, it catches up at five laps a minute: {clock0:0.00} -> {_dayNight.Clock:0.00})");
        foreach (var x in VrShot("stakhanov-3-after-two-laps")) yield return x;
    }
}
