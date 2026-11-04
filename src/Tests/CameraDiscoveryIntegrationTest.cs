using System;
using System.Reflection;
using CCCPRacing.Car;
using CCCPRacing.Race;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

/// <summary>Actual race scene: discover a secret, select its camera, restart, switch personnel files.</summary>
public partial class CameraDiscoveryIntegrationTest : Node
{
    int _frames;
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static object Read(RaceScene race, string name) => typeof(RaceScene).GetField(name, Private)!.GetValue(race);
    static void Call(RaceScene race, string name, params object[] args) => typeof(RaceScene).GetMethod(name, Private)!.Invoke(race, args);
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
            var race = Find(GetTree().Root) ?? throw new Exception("race not loaded");
            var track = (TrackData)Read(race, "_track");
            var car = (CarSim)Read(race, "_car");
            var state = typeof(RaceScene).GetField("_state", Private)!;
            void Racing() => state.SetValue(race, Enum.Parse(state.FieldType, "Racing"));
            CameraDiscoveries Book() => (CameraDiscoveries)Read(race, "_cameraDiscoveries");
            if (Book().HasAlternative(track.Id)) throw new Exception("test requires a fresh personnel file");
            Call(race, "CycleGameplayCamera");
            if ((GameplayView)Read(race, "_gameplayView") != GameplayView.TopDown) throw new Exception("locked camera selected");
            Racing();
            if (track.Id == "ilyich")
            {
                var duck = (PartyPondDuck)Read(race, "_cameraDuck");
                if (!duck.Active) throw new Exception("live duck missing");
                car.Reset(duck.WorldPosition + Vector3.Up * 0.6f, 0);
                Call(race, "CameraDiscoveryTick", car.Pos);
                if (!Book().Has(track.Id, GameplayView.Bonnet)) throw new Exception("duck did not unlock bonnet");
                Call(race, "CycleGameplayCamera");
                if ((GameplayView)Read(race, "_gameplayView") != GameplayView.Bonnet) throw new Exception("duck reward is not selectable");
                Call(race, "GameplayCamera", 1f / 60f);
                if (MathF.Abs(((Camera3D)Read(race, "_cam")).Near - 0.06f) > 0.001f) throw new Exception("bonnet pose not active");
                Call(race, "PauseChoose", 1);
                if (!Book().Has(track.Id, GameplayView.Bonnet)) throw new Exception("restart lost discovery");
            }
            Racing();
            foreach (var spot in CameraSecrets.Layout(track.Id))
            {
                var p = spot.Position;
                car.Reset(new Vector3(p.X, track.MeshHeight(p.X, p.Y) + 0.6f, p.Y), 0);
                Call(race, "CameraDiscoveryTick", car.Pos);
                if (!Book().Has(track.Id, spot.View)) throw new Exception($"{spot.View} camera object did not unlock");
            }
            if (!Book().Has(track.Id, GameplayView.Bonnet) || !Book().Has(track.Id, GameplayView.Cinematic)) throw new Exception("missing a track reward");
            var models = (Node3D)Read(race, "_cameraSecretModels");
            foreach (Node3D model in models.GetChildren()) if (model.Visible) throw new Exception("collected camera remains visible");
            Call(race, "UseFile", 2, true);
            if (Book().HasAlternative(track.Id) || (GameplayView)Read(race, "_gameplayView") != GameplayView.TopDown) throw new Exception("camera leaked into another personnel file");
            Call(race, "UseFile", 1, true);
            if (!Book().Has(track.Id, GameplayView.Bonnet) || !Book().Has(track.Id, GameplayView.Cinematic)) throw new Exception("file reload lost discoveries");
            string id = track.Id; track.Id = id == "ilyich" ? "siberia" : "ilyich";
            Call(race, "CycleGameplayCamera");
            if ((GameplayView)Read(race, "_gameplayView") != GameplayView.TopDown) throw new Exception("camera leaked into another track");
            track.Id = id;
            GD.Print($"DISCOVERY INTEGRATION PASS {id}: real secrets, selection, restart, file reload and track isolation");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError("DISCOVERY INTEGRATION FAIL: " + error); GetTree().Quit(1); }
    }
}
