using System;
using System.IO;
using System.Linq;
using CCCPRacing.Car;
using CCCPRacing.Race;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    void OctaneTapeSceneTests()
    {
        string dir = ProjectSettings.GlobalizePath("res://media/octane-tape-v1/production-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff"));
        Directory.CreateDirectory(dir); string laps = dir + "/laps"; Directory.CreateDirectory(laps);
        var record = TapeGame(dir + "/data", new[] { "--track=siberia", "--start", "--autodrive", "--ap-speed=0.7", "--no-record",
            "--golden-test", "--canister-test=50", "--canister-miss=2", "--canister-test-resume", "--tape-debug",
            "--tape-session-laps=" + laps, "--tape-quit-laps=1" }, 240, env: new() { ["FUELDBG"] = "1" });
        Check(record.Exit == 0 && !record.TimedOut && !record.Crashed && record.Has("fuel: OCTANE BOOSTER"),
            "octane scene: real golden can catch activates boost and complete lap saves");
        var lap = Directory.GetDirectories(laps, "lap-*").OrderBy(x => x).FirstOrDefault();
        if (lap == null) { Check(false, "octane scene: saved completed ghost/world pair exists"); return; }
        var track = TrackBuilder.Build(TrackDef.Load("res://tracks/siberia.json"));
        var ghost = Ghost.Load(lap + "/ghost_siberia.bin", new CarParams(), track);
        var tape = WorldTapeFile.Load(lap + "/tape_siberia.bin", ghost, WorldTapeCodec.Decode);
        var body = tape == null ? null : WorldTapeCodec.ReadBody(tape.Payload);
        bool boost = body?.Sounds.Any(frame => frame.Any(kv => kv.Key.Part.ActorKey == "None:PlayerPower"
            && kv.Key.Channel == TapeSoundChannel.PlayerBoostLeft && kv.Value > 0)) == true;
        bool gold = body?.Sounds.Any(frame => frame.Any(kv => kv.Key.Part.ActorKey == "None:PlayerPower"
            && kv.Key.Channel == TapeSoundChannel.PlayerGolden && kv.Value == 255)) == true;
        Check(boost && gold && tape.ActorStructure.ContainsKey("None:PlayerPower"),
            "octane scene: actual saved production tape includes positive boost and golden appearance");
        int first = body?.Sounds.FindIndex(frame => frame.Any(kv => kv.Key.Channel == TapeSoundChannel.PlayerBoostLeft && kv.Value > 0)) ?? 0;
        var watch = TapeGame(lap, new[] { "--track=siberia", "--watch", "--tape-debug", "--tape-replay-start=" + ((first + 15) / 10f).ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--tape-wrap-every=2", "--tape-replay-quit=6" }, 120);
        Check(watch.Exit == 0 && !watch.TimedOut && !watch.Crashed && !watch.Has("ERROR:")
            && watch.All("from tape: covers").Any(line => line.Split("], live")[0].Contains("None:PlayerPower")),
            "octane scene: actual Watch enters mid-boost, loops and exits using compatible recorded player state");
        GD.Print("OCTANE PRODUCTION TEST OUTPUT " + dir);
    }

    void OctaneTapeTests()
    {
        const string key = "None:PlayerPower";
        string dir = ProjectSettings.GlobalizePath("res://media/octane-tape-v1/tests-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff"));
        Directory.CreateDirectory(dir);
        var track = TrackBuilder.Build(TrackDef.Load("res://tracks/siberia.json"));
        var car = new CarSim(new CarParams(), track);
        var state = new PlayerPowerTape.State(0, 1, 0, false);
        var source = new PlayerPowerTape(() => state, v => state = v); AddChild(source);
        var index = new WorldTapeIndex(new[] { (key, (Node3D)source) });
        var recorder = new WorldTapeRecorder(); recorder.Begin(index);
        var ghost = new Ghost();
        for (int n = 0; n <= 3600; n++)
        {
            float t = n / 120f;
            bool boost = t >= 2 && t < 22;
            state = new(boost ? 22 - t : 0, boost ? 3.5f : 1f, boost ? .5f : 0f, boost, boost ? .12f : 0f);
            recorder.Record(t); ghost.Record(t, car, 0, 0, 0);
        }
        var saved = ghost.CopyWithTime(30); var tape = recorder.Finish(30, WorldTapeFingerprint.Of(saved)); tape.TrackId = "siberia";
        var pair = WorldTapeFile.SavePair(dir + "/ghost_siberia.bin", dir + "/tape_siberia.bin", saved, tape,
            WorldTapeRealOps.Instance, WorldTapeCodec.Encode, WorldTapeCodec.Decode);
        var loaded = WorldTapeFile.Load(dir + "/tape_siberia.bin", saved, WorldTapeCodec.Decode);
        Check(pair.Whole && loaded != null, "octane: production paired save/load retains boost actor and sampled state");
        var live = new PlayerPowerTape.State(7, 2, 0, false);
        var target = new PlayerPowerTape(() => live, v => live = v); AddChild(target);
        var targetIndex = new WorldTapeIndex(new[] { (key, (Node3D)target) });
        var player = new WorldTapePlayer(loaded, targetIndex, new TapeSink()); target.SetTapeDriven(true);
        foreach (float t in new[] { 0f, 1f, 2.2f, 8f, 18f, 21.5f, 22.5f, 29f, 8f, 0f })
        {
            player.Seek(t); bool boosted = t >= 2 && t < 22;
            Check(player.Covers(key) && live.Golden == boosted && Math.Abs(live.BoostLeft - (boosted ? 22 - t : 0)) < .2f
                && Math.Abs(live.Power - (boosted ? 3.5f : 1)) < .025f && Math.Abs(live.Overdrive - (boosted ? .5f : 0)) < .005f
                && Math.Abs(live.RadBoost - (boosted ? .12f : 0)) < .002f,
                $"octane: seek/loop {t:F1}s restores golden body, remaining duration, actual power/overdrive and the radiation boost");
        }
        player.Release(); target.SetTapeDriven(false);
        Check(live == new PlayerPowerTape.State(7, 2, 0, false), "octane: replay exit restores original live boost/power presentation");
        // A lap may begin while the previous lap's booster is still active.
        state = new(9, 3.5f, .5f, true);
        var carried = new WorldTapeRecorder(); carried.Begin(index); carried.Record(0);
        var carriedPlayer = new WorldTapePlayer(WorldTapeCodec.Decode(WorldTapeCodec.Encode(carried.Finish(.1f, 1))), targetIndex, new TapeSink());
        target.SetTapeDriven(true); carriedPlayer.Seek(0);
        Check(live.Golden && Math.Abs(live.BoostLeft - 9) < .1f, "octane: initial keyframe restores boost carried across the lap line");
        carriedPlayer.Release(); target.SetTapeDriven(false);
        Tape.Version(target, 2);
        var incompatible = new WorldTapePlayer(loaded, new WorldTapeIndex(new[] { (key, (Node3D)target) }), new TapeSink());
        incompatible.Seek(8);
        Check(!incompatible.Covers(key) && !target.Driven && live.BoostLeft == 7, "octane: incompatible actor cannot invent or overwrite live boost");
        incompatible.Release();
        var legacy = WorldTapeCodec.Decode(WorldTapeCodec.Encode(loaded)); legacy.ActorStructure.Remove(key);
        var old = new WorldTapePlayer(legacy, targetIndex, new TapeSink()); old.Seek(8);
        Check(!old.Covers(key) && live.BoostLeft == 7, "octane: legacy tape safely leaves absent boost state unrecorded"); old.Release();
        source.Free(); target.Free(); GD.Print("OCTANE TAPE TEST OUTPUT " + dir);
    }
}
