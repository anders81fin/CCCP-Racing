using System;
using System.Reflection;
using CCCPRacing.Audio;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    /// <summary>The rally's last norm: the radio plays the menu's theme song "He katsoivat länteen" on repeat until the player changes the song, the radio goes off or the run ends.</summary>
    void RadioLastNormTest()
    {
        var radio = new CarRadio(); AddChild(radio);
        int n = radio.StationCount, theme = n - 1;
        Check(n >= 2 && radio.StationName(theme) == "He katsoivat länteen", $"radio: the theme song is the last of {n} stations");
        if (n < 2) { radio.QueueFree(); return; }
        bool Loops(int i) => radio.StationStream(i) is AudioStreamMP3 m && m.Loop;

        // A radio that is off stays off.
        Check(!radio.PlayThemeOnRepeat() && !radio.RepeatTheme && !radio.On, "radio off: the theme is not forced on");
        // On at another station: the theme comes on repeat.
        radio.Select(0);
        Check(radio.PlayThemeOnRepeat() && radio.RepeatTheme && radio.CurrentStation == theme && Loops(theme), "radio on at station 1: tuned to the theme song, on repeat (the stream loops, no gap)");
        // The player changes the song: the repeat ends and the song no longer loops.
        radio.StepStation(1);
        Check(!radio.RepeatTheme && radio.CurrentStation != theme && !Loops(theme), "the player steps the station: the repeat ends");
        // Already on the theme: no new tuning, the repeat starts.
        radio.Select(theme);
        Check(radio.PlayThemeOnRepeat() && radio.RepeatTheme && radio.CurrentStation == theme && Loops(theme), "already on the theme: it stays and repeats");
        // The radio is switched off: the repeat ends, and on again it plays as usual.
        radio.Toggle();
        Check(!radio.On && !radio.RepeatTheme && !Loops(theme), "the player switches the radio off: the repeat ends");
        radio.Toggle();
        Check(radio.On && !radio.RepeatTheme, "switched on again: no repeat");
        // StopRepeat (a new run) ends it too.
        radio.PlayThemeOnRepeat(); radio.StopRepeat();
        Check(!radio.RepeatTheme && !Loops(theme), "a new run: the repeat ends");

        // The scene's trigger: the last step of the rally's ladder, once per run.
        const BindingFlags f = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(RaceScene); var scene = new RaceScene();
        var rm = type.GetField("_runMode", f); var runModeType = type.GetNestedType("RunMode", BindingFlags.NonPublic);
        var norms = (NormPlan)type.GetField("_norms", f).GetValue(scene);
        type.GetField("_carRadio", f).SetValue(scene, radio);
        var trigger = type.GetMethod("LastNormRadio", f);
        var done = type.GetField("_lastNormSong", f);
        rm.SetValue(scene, Enum.Parse(runModeType, "Rally"));
        radio.Select(0);
        norms.Reset(true, 0, -1); trigger.Invoke(scene, null);
        Check(!radio.RepeatTheme && radio.CurrentStation == 0 && !(bool)done.GetValue(scene), "norm 1 of the ladder: the radio is left alone");
        norms.Reset(true, NormPlan.Steps - 2, -1); trigger.Invoke(scene, null);
        Check(!radio.RepeatTheme && radio.CurrentStation == 0, "the last but one norm: the radio is left alone");
        norms.Reset(true, NormPlan.Steps - 1, -1); trigger.Invoke(scene, null);
        Check(radio.RepeatTheme && radio.CurrentStation == theme && (bool)done.GetValue(scene), "the last norm remains: the theme song on repeat");
        radio.StepStation(1); trigger.Invoke(scene, null);
        Check(!radio.RepeatTheme && radio.CurrentStation != theme, "the player changed the song: it is not forced again in this run");
        // A new run (the plan resets): the trigger is armed again.
        type.GetMethod("ResetPlan", f).Invoke(scene, new object[] { true });
        Check(!(bool)done.GetValue(scene) && !radio.RepeatTheme, "a new run arms it again");
        rm.SetValue(scene, Enum.Parse(runModeType, "Training"));
        norms.Reset(true, NormPlan.Steps - 1, -1); trigger.Invoke(scene, null);
        Check(!radio.RepeatTheme, "a training run has no norms: the radio is left alone");
        rm.SetValue(scene, Enum.Parse(runModeType, "Rally"));
        radio.Toggle(); norms.Reset(true, NormPlan.Steps - 1, -1); trigger.Invoke(scene, null);
        Check(!radio.On && !radio.RepeatTheme, "the radio is off at the last norm: it stays off");
        radio.QueueFree();

        // A rally's restart request takes a lap of the window (the owner's order of 2026-11-26): the same lap is not free again by restarting, a wreck's or a tow's lap is not charged twice,
        // and a restart with one lap left is the end of the plan. Training and the title have no norms: free.
        var plan = new NormPlan(); plan.Reset(true, 0, -1);
        bool charged = RaceScene.RestartCharge(plan, false, out bool out1);
        Check(charged && !out1 && plan.Left == NormPlan.WindowLaps - RaceScene.RestartLaps, $"restart: a rally restart takes {RaceScene.RestartLaps} lap of the window ({plan.Left} of {NormPlan.WindowLaps} left)");
        int guard = 0; while (plan.Left > 1 && guard++ < 20) RaceScene.RestartCharge(plan, false, out _);
        bool lastCharged = RaceScene.RestartCharge(plan, false, out bool outLast);
        Check(lastCharged && outLast && plan.Finished, "restart: a restart with one lap left leaves none for the one to be driven: the plan has run out");
        var paidPlan = new NormPlan(); paidPlan.Reset(true, 0, -1);
        Check(!RaceScene.RestartCharge(paidPlan, true, out _) && paidPlan.Left == NormPlan.WindowLaps, "restart: a restart after a wreck or a tow (already paid) takes nothing more");
        var freePlan = new NormPlan(); freePlan.Reset(false, 0, -1);
        Check(!RaceScene.RestartCharge(freePlan, false, out _) && freePlan.Left == NormPlan.WindowLaps, "restart: a run without norms (training, the title) restarts free");
        // The rally never runs a track endlessly by restarting: five restarts end it.
        var endless = new NormPlan(); endless.Reset(true, 0, -1); int restarts = 0; bool ended = false;
        while (!ended && restarts < 50) { RaceScene.RestartCharge(endless, false, out ended); restarts++; }
        Check(ended && restarts == NormPlan.WindowLaps, $"restart: the window ends after {restarts} restarts without a lap driven");
    }
}
