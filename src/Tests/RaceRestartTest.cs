using System;
using System.Reflection;
using Godot;
using CCCPRacing.Car;
using CCCPRacing.Race;
using CCCPRacing.Track;

namespace CCCPRacing.Tests;

/// <summary>Integration check of the real pause-menu restart; run with --track=ID --start.</summary>
public partial class RaceRestartTest : Node
{
    int _frames;
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static object Read(RaceScene race, string name) => typeof(RaceScene).GetField(name, Private)!.GetValue(race);
    static void Write(RaceScene race, string name, object value) => typeof(RaceScene).GetField(name, Private)!.SetValue(race, value);
    static RaceScene Find(Node node)
    {
        if (node is RaceScene race) return race;
        foreach (var child in node.GetChildren()) { var found = Find(child); if (found != null) return found; }
        return null;
    }
    public override void _Ready() => GetTree().Root.CallDeferred(Node.MethodName.AddChild,
        GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate());

    public override void _Process(double delta)
    {
        if (++_frames != 45) return;
        try
        {
            var race = Find(GetTree().Root) ?? throw new Exception("Race scene not ready");
            var car = (CarSim)Read(race, "_car");
            var visual = (CarVisual)Read(race, "_carVis");
            var effects = (TyreEffects)Read(race, "_fx");
            var health = race.CarEngine;
            var tank = (FuelTank)Read(race, "_tank");
            var rad = (RadiationDef)Read(race, "_rad");
            var plan = race.Plan;
            bool fuelEnabled = tank.Enabled, healthEnabled = health.Enabled;
            if (plan.Enabled) plan.Reset(true, 2, 3);
            var norm = (plan.Step, plan.Left, plan.Used);
            if (plan.Enabled) Write(race, "_runClock", 12f);
            // Debug starting conditions must not override a manual restart's healthy/full/clean car.
            Write(race, "_startHealth", 0.2f); Write(race, "_fuelStart", 0.15f); Write(race, "_doseStart", 0.8f);
            for (int pass = 0; pass < 2; pass++)
            {
                visual.Damage.Enabled = true;
                visual.Damage.Hit(60, new Vector3(0.8f, 0.25f, -1.8f), Vector3.Back, car, effects);
                if (!visual.Damage.IsDamaged) throw new Exception("fixture failed to dent the real car");
                health.Set(0.2f);
                tank.Litres = pass == 0 ? tank.Capacity * 0.1f : 0;
                if (rad != null) Write(race, "_dose", rad.Limit * 0.8f);
                if (pass == 1)
                {
                    health.Destroy("restart test");
                    Write(race, "_dry", true); Write(race, "_towCalled", true); Write(race, "_wreck", true);
                }
                var state = typeof(RaceScene).GetField("_state", Private)!;
                state.SetValue(race, Enum.Parse(state.FieldType, "Paused"));
                typeof(RaceScene).GetMethod("PauseChoose", Private)!.Invoke(race, new object[] { 1 });
                if (Read(race, "_state").ToString() != "Countdown") throw new Exception("restart did not reach countdown");
                if (visual.Damage.IsDamaged || health.Health != 1f || health.Dead) throw new Exception("damage or engine health survived restart");
                if (MathF.Abs(tank.Litres - tank.Capacity) > 0.001f) throw new Exception("tank was not filled");
                if (rad != null && (float)Read(race, "_dose") != 0f) throw new Exception("radiation dose survived restart");
                if ((bool)Read(race, "_dry") || (bool)Read(race, "_towCalled") || (bool)Read(race, "_wreck")) throw new Exception("dry/wreck/tow state survived restart");
                if (tank.Enabled != fuelEnabled || health.Enabled != healthEnabled) throw new Exception("restart changed enabled features");
                if (plan.Enabled && (float)Read(race, "_runClock") != 12f) throw new Exception("restart discarded rally lap continuity");
                if ((plan.Step, plan.Left, plan.Used) != norm) throw new Exception("restart changed the norm window");
                GD.Print($"[ok] restart pass {pass + 1}: repaired car, full tank, zero applicable dose; enabled features and norms retained");
            }
            GD.Print("RESTART TEST PASS " + ((TrackData)Read(race, "_track")).Id);
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError("RESTART TEST FAIL: " + error);
            GetTree().Quit(1);
        }
    }
}
