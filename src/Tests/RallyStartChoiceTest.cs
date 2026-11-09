using System;
using System.Reflection;
using Godot;
using CCCPRacing.Race;
using CCCPRacing.UI;

namespace CCCPRacing.Tests;

/// <summary>Saved-rally menu choices and their real norm/save state. Use a disposable --data-dir.</summary>
public partial class RallyStartChoiceTest : Node
{
    int _frames;
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic;
    static object Read(RaceScene r, string n) => typeof(RaceScene).GetField(n, Fields)!.GetValue(r);
    static void Write(RaceScene r, string n, object v) => typeof(RaceScene).GetField(n, Fields)!.SetValue(r, v);
    static object Call(RaceScene r, string n, params object[] args) => typeof(RaceScene).GetMethod(n, Fields)!.Invoke(r, args);
    static object RaceMode => Enum.Parse(typeof(RaceScene).GetMethod("AskOrRun", Fields)!.GetParameters()[0].ParameterType, "Race");
    static RaceScene Find(Node n)
    {
        if (n is RaceScene r) return r;
        foreach (var child in n.GetChildren()) { var found = Find(child); if (found != null) return found; }
        return null;
    }
    static void Require(bool yes, string why) { if (!yes) throw new Exception(why); }
    static void Choose(RaceScene r, int selection) { Write(r, "_cardSel", selection); Call(r, "ChooseTitleCard"); }
    static void ExpectCard(RaceScene r, string name) => Require(Read(r, "_card").ToString() == name, "Expected " + name);
    public override void _Ready() => GetTree().Root.CallDeferred(Node.MethodName.AddChild,
        GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate());
    public override void _Process(double delta)
    {
        if (++_frames != 45) return;
        // Suppress only the graphical loading transition; exercise the real choices,
        // PrepareRun/RestoreRallyRun and checkpoint consumption below.
        var loadingGuard = new TrackLoadingScreen();
        var active = typeof(TrackLoadingScreen).GetProperty("Active")!;
        try
        {
            var r = Find(GetTree().Root) ?? throw new Exception("Race not ready");
            r.SetPhysicsProcess(false); r.SetProcess(false);
            active.SetValue(null, loadingGuard);
            Write(r, "_rallyFuel", FuelGrade.Rocket);
            Call(r, "PrepareRun", RaceMode);
            string dir = (string)typeof(RaceScene).GetProperty("FileDir", Fields)!.GetValue(r);
            string sig = (string)typeof(RaceScene).GetProperty("ResumeSignature", Fields)!.GetValue(r);
            var point = new RallyResume.Checkpoint(3, 2, 9, 1, 123.5f, 9, 6);
            Require(RallyResume.Save(dir, "ilyich", sig, point, FuelGrade.Rocket) == Error.Ok, "Fixture save failed");
            RallyResume.Save(dir, "siberia", "other", point);
            Call(r, "AskOrRun", RaceMode);
            ExpectCard(r, "RallyResume");
            Require((int)Read(r, "_cardSel") == 0, "Continue must be default");
            Choose(r, 1); ExpectCard(r, "RallyFuel");
            // an earlier mode or run left NIGHT behind: the new rally still starts at DAY, with no time-of-day card
            typeof(RaceScene).GetField("_rallyPhase", Fields)!.SetValue(null, DayNight.Phase.Night);
            Choose(r, (int)FuelGrade.Diesel); ExpectCard(r, "None");
            Require((DayNight.Phase)typeof(RaceScene).GetField("_rallyPhase", Fields)!.GetValue(null) == DayNight.Phase.Day, "A new rally must start at DAY whatever phase was left behind");
            Require(RallyResume.Exists(dir, "ilyich"), "Choosing new fuel must preserve the save until starting");
            Call(r, "AskOrRun", RaceMode); ExpectCard(r, "RallyResume");
            Choose(r, 0);
            Require((FuelGrade)Read(r, "_rallyFuel") == FuelGrade.Rocket, "Continue lost saved fuel");
            Call(r, "PrepareRun", RaceMode); Call(r, "RestoreRallyRun");
            Require(((NormPlan)Read(r, "_norms")).Step == 3 && (bool)Read(r, "_resumeLoaded"), "Continue failed to restore progress");
            Call(r, "AskOrRun", RaceMode); Choose(r, 1); Choose(r, (int)FuelGrade.Diesel);
            ExpectCard(r, "None");
            Require((string)Read(r, "_newRallyTrack") == "ilyich" && RallyResume.Exists(dir, "ilyich"), "New rally must start directly and defer removal until loading completes");
            Call(r, "PrepareRun", RaceMode); Call(r, "RestoreRallyRun");
            Require(((NormPlan)Read(r, "_norms")).Step == 0 && !(bool)Read(r, "_resumeLoaded"), "New rally restored old progress");
            Require((FuelGrade)Read(r, "_rallyFuel") == FuelGrade.Diesel, "New rally lost chosen fuel");
            Call(r, "ConsumeRallyCheckpoint");
            Require(!RallyResume.Exists(dir, "ilyich") && RallyResume.Exists(dir, "siberia"), "Replacement must remove only this track's save");
            Require(Read(r, "_newRallyTrack") == null, "Replacement request survived consumption");
            // The car first, the track next, and each fuel has the progression of its own: what the diesel opened is not open on the petrol until the petrol has opened it.
            var camp = new Campaign(null);
            camp.Award("ilyich", NormPlan.Medal.Gold, FuelGrade.Diesel);
            Write(r, "_campaign", camp);
            Call(r, "OpenRallyStart", -1); ExpectCard(r, "RallyFuel");
            Require((bool)Read(r, "_rallyFuelFirst"), "The rally must start with the fuel card");
            Choose(r, (int)FuelGrade.Diesel); ExpectCard(r, "Track");
            Require((FuelGrade)Read(r, "_rallyFuel") == FuelGrade.Diesel && (bool)Read(r, "_rallyFuelPicked"), "The fuel is chosen before the track");
            var locked = (bool[])Call(r, "LockedNow", new object[] { null });
            Require(!locked[0] && !locked[1] && locked[2] && !locked[3], "On the diesel: Kolkhoz and Siberia (the gold) and Poligon (a gold on any track) are open, Atomgrad is locked");
            Call(r, "OpenRallyStart", -1); Choose(r, (int)FuelGrade.Petrol); ExpectCard(r, "Track");
            locked = (bool[])Call(r, "LockedNow", new object[] { null });
            Require(!locked[0] && locked[1] && locked[2] && locked[3], "On the petrol only Kolkhoz is open: the diesel's medals open nothing here");
            Choose(r, 1); ExpectCard(r, "Track");      // a locked track refuses
            camp.Award("ilyich", NormPlan.Medal.Silver, FuelGrade.Petrol);
            locked = (bool[])Call(r, "LockedNow", new object[] { null });
            Require(!locked[1] && locked[3] && locked[2], "A silver on the petrol opens Siberia on the petrol, and only that");
            camp.Award("ilyich", NormPlan.Medal.Gold, FuelGrade.Petrol);
            locked = (bool[])Call(r, "LockedNow", new object[] { null });
            Require(!locked[3], "A gold on the petrol opens Poligon on the petrol");
            Choose(r, 3); ExpectCard(r, "None");
            Require((FuelGrade)Read(r, "_rallyFuel") == FuelGrade.Petrol && (string)Read(r, "_chosenRunTrack") == "bonus", "The petrol rally starts on Poligon with no second fuel card");
            // The petrol opens by silvers on the three first tracks with the diesel (not before); the jet fuel by four silvers on the petrol, the Sputnik by four on the jet fuel.
                        var fc = new Campaign(null); Write(r, "_campaign", fc);
            fc.Award("ilyich", NormPlan.Medal.Silver, FuelGrade.Diesel);
            fc.Award("siberia", NormPlan.Medal.Silver, FuelGrade.Diesel);
            Require((bool)Call(r, "FuelLocked", FuelGrade.Petrol), "Two silvers on the diesel must not open the petrol");
            fc.Award("atomgrad", NormPlan.Medal.Silver, FuelGrade.Diesel);
            Require(!(bool)Call(r, "FuelLocked", FuelGrade.Petrol) && (bool)Call(r, "FuelLocked", FuelGrade.Rocket), "Three silvers on the diesel open the petrol and nothing more");
            foreach (var t in new[] { "ilyich", "siberia", "atomgrad" }) fc.Award(t, NormPlan.Medal.Silver, FuelGrade.Petrol);
            Require((bool)Call(r, "FuelLocked", FuelGrade.Rocket), "The jet fuel needs the Poligon too");
            fc.Award("bonus", NormPlan.Medal.Silver, FuelGrade.Petrol);
            Require(!(bool)Call(r, "FuelLocked", FuelGrade.Rocket) && (bool)Call(r, "FuelLocked", FuelGrade.Sputnik), "Four silvers on the petrol open the jet fuel");
            foreach (var t in new[] { "ilyich", "siberia", "atomgrad", "bonus" }) fc.Award(t, NormPlan.Medal.Silver, FuelGrade.Rocket);
            Require(!(bool)Call(r, "FuelLocked", FuelGrade.Sputnik), "Four silvers on the jet fuel open the Sputnik");
            fc.Award("ilyich", NormPlan.Medal.Bronze, FuelGrade.Diesel);
            Write(r, "_campaign", camp);
            // The Stakhanov laps ask for the car first too: the fuel, the track, the time of day.
            Call(r, "OpenLapsStart"); ExpectCard(r, "LapsFuel");
            Choose(r, (int)FuelGrade.Diesel); ExpectCard(r, "Track");
            Require((FuelGrade)Read(r, "_lapsFuelPick") == FuelGrade.Diesel, "The laps' fuel is chosen before the track");
            var lapsCamp = new Campaign(null);
            lapsCamp.Award("ilyich", NormPlan.Medal.Bronze, FuelGrade.Petrol);
            Write(r, "_campaign", lapsCamp);
            locked = (bool[])Call(r, "LockedNow", new object[] { null });
            Require(locked[1], "A bronze on the petrol does not open Siberia for the laps on the diesel");
            lapsCamp.Award("ilyich", NormPlan.Medal.Bronze, FuelGrade.Diesel);
            locked = (bool[])Call(r, "LockedNow", new object[] { null });
            Require(!locked[1], "A bronze on the diesel opens Siberia for the laps on the diesel");
            Write(r, "_campaign", camp);
            Choose(r, 0); ExpectCard(r, "LapsTime");
            Choose(r, 0); ExpectCard(r, "None");
            GD.Print("RALLY START CHOICE PASS: the fuel before the track, continue fuel/progress, new fuel/fresh norms, safe cancellation, deferred track-only replacement");
            GetTree().Quit();
        }
        catch (Exception e) { GD.PushError("RALLY START CHOICE FAIL " + e); GetTree().Quit(1); }
        finally { active.SetValue(null, null); loadingGuard.Free(); }
    }
}
