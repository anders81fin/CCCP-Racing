using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CCCPRacing.Car;
using CCCPRacing.Race;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    void FuelTapeSceneTests()
    {
        string output = ProjectSettings.GlobalizePath("res://media/fuel-tape-v1/production-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff"));
        Directory.CreateDirectory(output);
        foreach (string id in new[] { "siberia", "atomgrad", "bonus" })
        {
            string dir = output + "/" + id; Directory.CreateDirectory(dir);
            string laps = dir + "/laps"; Directory.CreateDirectory(laps);
            var record = TapeGame(dir + "/record-data", new[] { "--track=" + id, "--start", "--autodrive", "--no-record", "--tape-debug",
                "--at-depot", "--fuel=0.1", "--tape-session-laps=" + laps, "--tape-quit-laps=1" }, 180);
            Check(record.Exit == 0 && !record.TimedOut && !record.Crashed, $"fuel scene {id}: real game completes isolated lap after depot start");
            var lap = Directory.GetDirectories(laps, "lap-*").OrderBy(x => x).FirstOrDefault();
            if (lap == null) { Check(false, $"fuel scene {id}: completed paired lap exists"); continue; }
            var track = TrackBuilder.Build(TrackDef.Load($"res://tracks/{id}.json"));
            var ghost = Ghost.Load(lap + $"/ghost_{id}.bin", new CarParams(), track);
            var tape = WorldTapeFile.Load(lap + $"/tape_{id}.bin", ghost, WorldTapeCodec.Decode);
            var body = tape == null ? null : WorldTapeCodec.ReadBody(tape.Payload);
            bool pouring = body?.Sounds.Any(frame => frame.Any(kv => kv.Key.Part.ActorKey == "Depot:FuelDepot"
                && kv.Key.Channel == TapeSoundChannel.FuelPouring && kv.Value > 127)) == true;
            Check(tape?.ActorStructure.ContainsKey("Depot:FuelDepot") == true && pouring,
                $"fuel scene {id}: live FuelTick records real pouring into saved ghost/world pair");
            var replay = TapeGame(lap, new[] { "--track=" + id, "--watch", "--tape-debug", "--tape-wrap-every=2", "--tape-replay-quit=6" }, 120);
            bool covered = replay.All("from tape: covers").Any(line => line.Split("], live")[0].Contains("Depot:FuelDepot"));
            Check(replay.Exit == 0 && !replay.TimedOut && !replay.Crashed && covered && !replay.Has("ERROR:"),
                $"fuel scene {id}: actual Watch uses depot tape through repeated loops and cleanup without engine errors");
            File.WriteAllText(dir + "/verification.json", System.Text.Json.JsonSerializer.Serialize(new {
                record_exit=record.Exit, replay_exit=replay.Exit, depot_covered=covered, actual_pouring_recorded=pouring, lap
            }));
        }
        GD.Print("FUEL PRODUCTION TEST OUTPUT " + output);
    }

    void FuelTapeTests()
    {
        string output = ProjectSettings.GlobalizePath("res://media/fuel-tape-v1/tests-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff"));
        Directory.CreateDirectory(output);
        foreach (string id in new[] { "ilyich", "siberia", "atomgrad", "bonus" }) FuelDepotTapeTest(id, output);
        GD.Print("FUEL TAPE TEST OUTPUT " + output);
    }

    void FuelDepotTapeTest(string id, string output)
    {
        const string key = "Depot:FuelDepot";
        var def = TrackDef.Load($"res://tracks/{id}.json"); var track = TrackBuilder.Build(def);
        FuelDepot Make()
        {
            var depot = new FuelDepot { Name = "FuelDepot" }; depot.Init(track, def.Depot); AddChild(depot); return depot;
        }
        var source = Make(); var index = new WorldTapeIndex(new[] { (key, (Node3D)source) });
        var tank = new FuelTank(new CarParams()) { Enabled = true }; tank.Refill(.1f);
        source.Level = tank.Fraction;
        var rec = new WorldTapeRecorder(); rec.Begin(index);
        var ghost = new Ghost(); var car = new CarSim(new CarParams(), track);
        car.Reset(new Vector3(source.ParkPos.X, track.MeshHeight(source.ParkPos.X, source.ParkPos.Y) + .55f, source.ParkPos.Y), source.ParkYaw);
        var samples = new Dictionary<int, (bool Drip, bool Pouring, float Level, float Envelope)>();
        var soundSamples = new Dictionary<int, (bool Pouring, float Level, float Envelope)>();
        // Real depot presentation and synthesis: stop, fill, leave mid-fill, return,
        // then finish. This includes the 5 s keyframe and a closing sound tail.
        for (int n = 0; n <= 1200; n++)
        {
            float t = n / 120f;
            bool pouring = t >= 1 && t < 3 || t >= 5 && t < 8;
            source.Pouring = pouring;
            if (pouring && source.FillsCar(new Vector2(car.Pos.X, car.Pos.Z), 0)) tank.Fill(FuelDepot.FillRate / 120f);
            source.Level = tank.Fraction; source._Process(1 / 120.0);
            for (int k = 0; k < (n % 4 == 0 ? 183 : 184); k++) source.Sound.Sample();
            int prior = rec.Samples; rec.Record(t); ghost.Record(t, car, 0, 0, 0);
            if (rec.Samples > prior) samples[rec.Samples - 1] = (source.StreamNode.Visible, source.Pouring, source.Level, source.Sound.Envelope);
            if (n % 12 == 0) soundSamples[n / 12] = (source.Pouring, source.Level, source.Sound.Envelope);
        }
        var savedGhost = ghost.CopyWithTime(10); var tape = rec.Finish(10, WorldTapeFingerprint.Of(savedGhost)); tape.TrackId = id;
        string folder = output + "/" + id; Directory.CreateDirectory(folder);
        var pair = WorldTapeFile.SavePair(folder + $"/ghost_{id}.bin", folder + $"/tape_{id}.bin", savedGhost, tape,
            WorldTapeRealOps.Instance, WorldTapeCodec.Encode, WorldTapeCodec.Decode);
        File.WriteAllText(folder + $"/track_{id}.sig", TrackSignature.Compute(track) + "\n");
        File.WriteAllText(folder + $"/records_{id}.ver", RecordStore.Layout + "\n");
        Vector3 aim = source.StreamNode.GlobalPosition;
        File.WriteAllText(folder + "/camera-sequence.json", System.Text.Json.JsonSerializer.Serialize(new { shots = new[] {
            new { id="fuel-stop", start=0f, end=10f, camera="trailer-fixed", eye=new[]{aim.X+7,aim.Y+5,aim.Z+9}, look=new[]{aim.X,aim.Y,aim.Z}, fov=45 }
        }}));
        var loaded = WorldTapeFile.Load(folder + $"/tape_{id}.bin", savedGhost, WorldTapeCodec.Decode);
        Check(pair.Whole && loaded != null, $"fuel tape {id}: production paired save/load preserves the complete fuel stop");
        var replay = Make(); var replayIndex = new WorldTapeIndex(new[] { (key, (Node3D)replay) });
        var player = new WorldTapePlayer(loaded, replayIndex, new TapeSink());
        replay.Level = .33f; replay.Pouring = false;
        replay.SetTapeDriven(true);
        Check(player.Covers(key) && rec.UnscaledChanges == 0 && rec.UnregisteredSpinners == 0,
            $"fuel tape {id}: fresh production depot covers compatible root and visible stream");
        bool matches = true;
        foreach (var (sample, value) in samples)
        {
            float t = sample / WorldTapeConst.TapeRate; player.Apply(t, 1 / WorldTapeConst.TapeRate);
            var snd = soundSamples[(int)MathF.Floor(t * 10 + .001f)];
            matches &= replay.StreamNode.Visible == value.Drip && replay.Pouring == snd.Pouring
                && Math.Abs(replay.Level - snd.Level) <= 1 / 255f && Math.Abs(replay.Sound.Envelope - snd.Envelope) <= 1 / 255f;
        }
        Check(matches, $"fuel tape {id}: all stream frames, valve states, pitch levels and sound envelopes match saved samples");
        foreach (float t in new[] { 0f, 1.2f, 2.4f, 3.1f, 5.2f, 7.4f, 8.1f, 9f, 1.2f })
        {
            replay.SetTapeDriven(true); player.Seek(t);
            var want = samples[(int)MathF.Floor(t * 15 + .001f)]; var snd = soundSamples[(int)MathF.Floor(t * 10 + .001f)];
            Check(replay.StreamNode.Visible == want.Drip && replay.Pouring == snd.Pouring
                && Math.Abs(replay.Level - snd.Level) <= 1 / 255f && Math.Abs(replay.Sound.Envelope - snd.Envelope) <= 1 / 255f,
                $"fuel tape {id}: seek/loop {t:F1}s restores the actual aged presentation");
        }
        var frozen = replay.StreamNode.Visible; var level = replay.Level;
        replay._Process(20); replay.Pouring = false; replay.Level = 0;
        Check(replay.StreamNode.Visible == frozen && replay.Pouring && replay.Level == level,
            $"fuel tape {id}: live drip clock and fuel updates cannot fight recorded playback");
        double energy = 0; for (int n = 0; n < 2205; n++) { float v = replay.Sound.Sample(); energy += v * v; }
        Check(energy > .01, $"fuel tape {id}: restored active gurgle synthesises audible nonzero audio");
        replay.SetTapeDriven(true); player.Seek(9);
        Check(!replay.Pouring && replay.Sound.Sample() == 0, $"fuel tape {id}: seek past filling clears old sound instead of leaking a tail");
        player.Release(); replay.SetTapeDriven(false);
        Check(!replay.TapeDriven && !replay.Pouring && Math.Abs(replay.Level - .33f) < .0001f && !replay.StreamNode.Visible,
            $"fuel tape {id}: exit restores live depot state and removes replay sound/stream");
        // A lap can begin while already filling; no start event is required.
        source.Pouring = true; source.Level = .5f; source.Sound.Envelope = 1;
        var carried = new WorldTapeRecorder(); carried.Begin(index); carried.Record(0);
        var carriedTape = WorldTapeCodec.Decode(WorldTapeCodec.Encode(carried.Finish(.1f, 1)));
        var carriedPlayer = new WorldTapePlayer(carriedTape, replayIndex, new TapeSink());
        replay.SetTapeDriven(true); carriedPlayer.Seek(0);
        Check(replay.Pouring && replay.Sound.Envelope > .99f && Math.Abs(replay.Level-.5f)<.003,
            $"fuel tape {id}: initial keyframe restores an already running fill without a new fade-in");
        carriedPlayer.Release(); replay.SetTapeDriven(false);
        Tape.Version(replay, 2);
        var incompatible = new WorldTapePlayer(loaded, new WorldTapeIndex(new[] { (key, (Node3D)replay) }), new TapeSink());
        incompatible.Seek(2);
        Check(!incompatible.Covers(key) && !replay.Pouring, $"fuel tape {id}: incompatible depot cannot start stray recorded sound");
        incompatible.Release();
        var legacy = WorldTapeCodec.Decode(WorldTapeCodec.Encode(loaded)); legacy.ActorStructure.Remove(key);
        var old = new WorldTapePlayer(legacy, replayIndex, new TapeSink()); old.Seek(2);
        Check(!old.Covers(key) && !replay.Pouring, $"fuel tape {id}: old tapes invent no missing refuelling state");
        old.Release(); replay.Free(); source.Free();
    }
}
