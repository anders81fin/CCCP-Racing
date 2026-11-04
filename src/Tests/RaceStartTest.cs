using System;
using System.Reflection;
using Godot;
using CCCPRacing.Car;
using CCCPRacing.Race;
using CCCPRacing.Track;
using CCCPRacing.UI;

namespace CCCPRacing.Tests;

/// <summary>Real pedals, physics, grid placement and lap invalidation. Run with --start --track=ID.</summary>
public partial class RaceStartTest : Node
{
    int _frames;
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static object Read(RaceScene race, string field) => typeof(RaceScene).GetField(field, Private)!.GetValue(race);
    static void Write(RaceScene race, string field, object value) => typeof(RaceScene).GetField(field, Private)!.SetValue(race, value);
    static void Call(RaceScene race, string method) => typeof(RaceScene).GetMethod(method, Private)!.Invoke(race, null);
    static RaceScene Find(Node n)
    {
        if (n is RaceScene race) return race;
        foreach (var child in n.GetChildren()) { var found = Find(child); if (found != null) return found; }
        return null;
    }
    static void Require(bool yes, string message) { if (!yes) throw new Exception(message); }
    public override void _Ready() => GetTree().Root.CallDeferred(Node.MethodName.AddChild,
        GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate());
    public override void _Process(double delta)
    {
        if (++_frames != 45) return;
        try
        {
            var race = Find(GetTree().Root) ?? throw new Exception("Race not ready");
            race.SetPhysicsProcess(false);
            race.SetProcess(false);
            Write(race, "_showKeys", false);
            Call(race, "EnterCountdown");
            Write(race, "_startRevLeft", 0f);
            var car = (CarSim)Read(race, "_car");
            var track = (TrackData)Read(race, "_track");
            var timer = (LapTimer)Read(race, "_timer");
            var hud = (Hud)Read(race, "_hud");
            Vector2 dir = track.CenterDir(0);
            float gap = -(new Vector2(car.Pos.X, car.Pos.Z) - track.Center[0]).Dot(dir) - 2.11f;
            Require(Math.Abs(gap - 0.4f) < 0.01f, $"Bumper gap {gap}, expected 0.4m");
            Vector3 waitingPosition = car.Pos;
            float waitingCountdown = (float)Read(race, "_countdown");
            Input.ActionPress("accelerate");
            for (int i = 0; i < 60; i++)
            {
                race._PhysicsProcess(1.0 / 60);
                race._Process(1.0 / 60);
            }
            Require(car.Pos == waitingPosition && (float)Read(race, "_countdown") == waitingCountdown,
                "Handoff must freeze the world and countdown even with the throttle pressed");
            Require(hud.StartClutchWaiting && hud.Frozen, "Handoff instructions must be visible while frozen");
            Input.ActionPress("clutch");
            race._Process(1.0 / 60);
            Require(!hud.StartClutchWaiting && (float)Read(race, "_countdown") < waitingCountdown,
                "Held clutch must dismiss the handoff and resume the countdown immediately");
            Input.ActionPress("accelerate");
            for (int i = 0; i < 60; i++) race._PhysicsProcess(1.0 / 60);
            Require(car.Clutch && !car.Neutral && car.Rpm > 4000, "Clutch must freely rev the engine");
            Require(!(bool)Read(race, "_falseStart"), "Holding the clutch caused a false start");
            Input.ActionRelease("clutch");
            for (int i = 0; i < 120; i++) race._PhysicsProcess(1.0 / 60);
            Require(car.Speed > 1, "Gas without the clutch must move the car during countdown");
            Require((bool)Read(race, "_falseStart"), "Crossing before green did not cause a false start");
            Call(race, "UpdateHud");
            Require(hud.LapVoid && hud.FalseStart, "False-start HUD must be immediate, before GO");
            Call(race, "StartRacing");
            Require(timer.LapVoid, "GO erased the invalid lap");
            timer.CompleteLapForTest(70);
            Require(timer.LastLapVoid && !timer.LapVoid, "Only the false-start lap should be void");
            Input.ActionRelease("accelerate");
            Call(race, "EnterCountdown");
            Require(!(bool)Read(race, "_falseStart"), "Restart retained a false start");
            Write(race, "_startRevLeft", 0f);
            Input.ActionPress("clutch");
            Input.ActionPress("accelerate");
            for (int i = 0; i < 60; i++) race._PhysicsProcess(1.0 / 60);
            Call(race, "StartRacing");
            Require(!timer.LapVoid && car.Clutch, "Green must preserve the driver's held clutch and valid lap");
            Input.ActionRelease("clutch");
            for (int i = 0; i < 60; i++) race._PhysicsProcess(1.0 / 60);
            Require(car.Speed > 1 && !timer.LapVoid, "Releasing after green must launch a valid lap");
            GD.Print("RACE START PASS " + track.Id);
            GetTree().Quit();
        }
        catch (Exception e) { GD.PushError("RACE START FAIL " + e); GetTree().Quit(1); }
        finally { Input.ActionRelease("accelerate"); Input.ActionRelease("clutch"); }
    }
}
