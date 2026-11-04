using System;
using System.Reflection;
using CCCPRacing.Car;
using CCCPRacing.Race;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

/// <summary>Real title replay, watch focus and restoring the driving camera before the clutch card.</summary>
public partial class ReplayCameraIntegrationTest : Node
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    int _frames;
    static object Read(RaceScene race, string name) => typeof(RaceScene).GetField(name, Private)!.GetValue(race);
    static object Call(RaceScene race, string name, params object[] args) => typeof(RaceScene).GetMethod(name, Private)!.Invoke(race, args);
    static RaceScene Find(Node node)
    {
        if (node is RaceScene race) return race;
        foreach (var child in node.GetChildren()) { var found = Find(child); if (found != null) return found; }
        return null;
    }
    public override void _Ready()
    {
        if (!Array.Exists(OS.GetCmdlineUserArgs(), arg => arg.StartsWith("--data-dir=/tmp/")))
        { GD.PushError("Replay camera integration requires an isolated --data-dir=/tmp/... directory"); GetTree().Quit(1); return; }
        GetTree().Root.CallDeferred(Node.MethodName.AddChild,
            GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate());
    }
    public override void _Process(double delta)
    {
        var race = Find(GetTree().Root);
        if (++_frames > 1800) { GD.PushError("replay camera integration timed out"); GetTree().Quit(1); return; }
        if (race == null || Read(race, "_cam") == null || (bool)Read(race, "_booting")) return;
        SetProcess(false);
        try
        {
            race.ProcessMode = ProcessModeEnum.Disabled;
            var track = (TrackData)Read(race, "_track");
            var car = (CarSim)Read(race, "_car");
            var camera = (Camera3D)Read(race, "_cam");
            var book = (CameraDiscoveries)Read(race, "_cameraDiscoveries");
            bool discoveries = book.HasAlternative(track.Id);
            if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--bundled-replay-test"))
            {
                var demo = (Ghost)Read(race, "_bundledTitleGhost");
                if (demo == null || Math.Abs(demo.LapTime - 39.809597f) > .001f
                    || !(bool)typeof(RaceScene).GetProperty("BundledTitleReplay", Private)!.GetValue(race))
                    throw new Exception("fresh title did not select the bundled 0:39:81 lap");
                foreach (var ghost in (Ghost[])Read(race, "_ghosts"))
                    if (ghost != null) throw new Exception("demo contaminated player racing ghosts");
                foreach (var board in (Leaderboard[])Read(race, "_boards"))
                    if (board.Entries.Count != 0) throw new Exception("demo contaminated player leaderboard");
                Call(race, "EnterTitle");
                for (int i = 0; i <= 12 * 30; i++)
                {
                    typeof(RaceScene).GetField("_replayClock", Private)!.SetValue(race, i / 30f);
                    demo.Apply(i / 30f, car, 0);
                    Call(race, "ReplayCamera", 1f / 30);
                }
                if (Math.Abs(camera.Fov - 75) > .01f) throw new Exception("bundled replay did not select bonnet camera");
                ((Ghost[])Read(race, "_ghosts"))[0] = new Ghost();
                if ((bool)typeof(RaceScene).GetProperty("BundledTitleReplay", Private)!.GetValue(race))
                    throw new Exception("bundled demo overrides a player's own replay");
                GD.Print("PASS: fresh install plays bundled 0:39:81 with cameras, keeps player records empty, yields to player replay");
                GetTree().Quit();
                return;
            }
            if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--autopilot-camera-test"))
            {
                foreach (int i in new[] { 0, 1, 2, 3 }) ((Ghost[])Read(race, "_ghosts"))[i] = null;
                typeof(RaceScene).GetField("_bundledTitleGhost", Private)!.SetValue(race, null);
                Call(race, "EnterTitle");
                for (int i = 0; i < 12 * 60; i++)
                {
                    ((AutoPilot)Read(race, "_ap")).Drive(car, 1f / 60);
                    car.Step(1f / 60);
                    Call(race, "ReplayCamera", 1f / 60);
                }
                if (Math.Abs(camera.Fov - 75) > .01f || !(bool)Read(race, "_replayCameraActive"))
                    throw new Exception("fresh install autopilot did not select bonnet camera");
                GD.Print("PASS: fresh install autopilot uses automatic bonnet camera without a recorded lap");
                GetTree().Quit();
                return;
            }
            var record = new Ghost();
            for (int i = 0; i <= 50 * 30; i++)
            {
                int at = (i / 2) % track.Center.Length;
                var d = track.CenterDir(at);
                car.Reset(track.CenterPos(at) + Vector3.Up * .7f, TrackData.YawFor(d));
                car.Vel = new Vector3(d.X, 0, d.Y) * 15;
                record.Record(i / 30f, car, 0, 0, 0);
            }
            record.LapTime = 50;
            var records = (RecordStore)Read(race, "_records");
            record.Save(records.GhostPath); // isolated --data-dir also supplies a graphical review fixture
            records.WriteSignature();
            ((Ghost[])Read(race, "_ghosts"))[0] = record;
            Call(race, "EnterTitle");
            float fov = camera.Fov, near = camera.Near;
            void Frame(float clock)
            {
                typeof(RaceScene).GetField("_replayClock", Private)!.SetValue(race, clock);
                record.Apply(clock, car, 0);
                Call(race, "ReplayCamera", 1f / 60);
            }
            for (int i = 0; i <= 12 * 30; i++) Frame(i / 30f);
            if (Math.Abs(camera.Fov - 75) > .01f) throw new Exception("title did not select bonnet shot");
            Call(race, "EnterWatch"); Frame(13);
            if (Math.Abs(camera.Fov - 75) > .01f) throw new Exception("watch lost replay camera");
            if (book.HasAlternative(track.Id) != discoveries) throw new Exception("replay unlocked a driving camera");
            var state = typeof(RaceScene).GetField("_state", Private)!;
            state.SetValue(race, Enum.Parse(state.FieldType, "Countdown"));
            Call(race, "PlaceAtStart");
            if ((bool)Read(race, "_replayCameraActive") || Math.Abs(camera.Fov - fov) > .01f || Math.Abs(camera.Near - near) > .001f)
                throw new Exception("driving projection did not restore before countdown");
            GD.Print("PASS: title changes shots, watch continues, discoveries preserved, driving camera restored");
            GetTree().Quit();
        }
        catch (Exception e) { GD.PushError(e.ToString()); GetTree().Quit(1); }
    }
}
