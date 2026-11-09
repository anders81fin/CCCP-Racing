using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using CCCPRacing.Audio;
using CCCPRacing.Camera;
using CCCPRacing.Car;
using CCCPRacing.Race;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    void CollisionFeedbackTest()
    {
        string output = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--feedback-out="))?["--feedback-out=".Length..]
            ?? Path.Combine(Path.GetTempPath(), "cccp-collision-feedback-" + Guid.NewGuid().ToString("N"));
        output = Path.GetFullPath(output);
        if (Directory.Exists(output)) { Check(false, "feedback: choose a fresh output folder"); return; }
        Directory.CreateDirectory(output);
        const int rate = 22050;
        SoundBalance.EngineGain = SoundBalance.RoadGain = 1;
        EngineSound Quiet() => new() { EngineLevel = 0, Volume = 1, Driven = 0 };
        float[] Render(EngineSound sound, Action fire, float seconds = 1.4f)
        {
            var data = new float[(int)(seconds * rate)];
            for (int i = 0; i < data.Length; i++) { if (i == rate / 5) fire(); data[i] = sound.NextSample(); }
            return data;
        }
        void Wav(string name, float[] data)
        {
            using var f = new BinaryWriter(File.Open(Path.Combine(output, name + ".wav"), FileMode.CreateNew));
            f.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); f.Write(36 + data.Length * 2);
            f.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); f.Write(16); f.Write((short)1); f.Write((short)1);
            f.Write(rate); f.Write(rate * 2); f.Write((short)2); f.Write((short)16);
            f.Write(System.Text.Encoding.ASCII.GetBytes("data")); f.Write(data.Length * 2);
            foreach (float v in data) f.Write((short)Math.Clamp((int)MathF.Round(v * 32767), -32767, 32767));
        }
        float Energy(float[] data) => (float)data.Sum(v => (double)v * v);
        var reviews = new List<object>(); var energies = new List<float>();
        foreach (float speed in new[] { 4f, 10f, 20f, 35f })
        {
            var snd = Quiet(); var data = Render(snd, () => snd.Collision(speed, 1000));
            Wav($"collision-{speed:00}-mps-isolated", data);
            float energy = Energy(data); energies.Add(energy);
            var response = CollisionResponse.Of(speed, 1000);
            reviews.Add(new { speed_mps = speed, mass_kg = 1000, response.EnergyJoules, response.Weight,
                response.Duration, peak = data.Max(v => Math.Abs(v)), audio_energy = energy });
            Check(data.All(float.IsFinite) && data.Max(v => Math.Abs(v)) < .99f,
                $"collision {speed} m/s: audible bounded waveform (energy {energy:F2})");
            snd.Free();
        }
        var materialAudio = new Dictionary<ImpactMaterial, float[]>();
        foreach (ImpactMaterial material in Enum.GetValues<ImpactMaterial>())
        {
            var snd = Quiet();
            var data = Render(snd, () => snd.Collision(20, 1000, material, material is ImpactMaterial.Wood or ImpactMaterial.Stone));
            materialAudio[material] = data; Wav("material-" + material.ToString().ToLowerInvariant(), data); snd.Free();
            Check(data.All(float.IsFinite) && data.Max(v => Math.Abs(v)) < .99f,
                $"collision material {material}: finite audible response with headroom");
        }
        float BrightEnergy(float[] samples)
        {
            float lp = 0; double e = 0;
            foreach (float v in samples) { lp += (v - lp) * .44f; float high = v - lp; e += high * high; }
            return (float)e;
        }
        Check(BrightEnergy(materialAudio[ImpactMaterial.Metal]) > BrightEnergy(materialAudio[ImpactMaterial.Snow]) * 3
            && Energy(materialAudio[ImpactMaterial.Metal]) > Energy(materialAudio[ImpactMaterial.Soft]) * 2,
            "collision materials: snow has a softer attack; soft targets do not sound like a heavy metal impact");
        var voice = new CollisionVoice(); var restored = new CollisionVoice(); var profile = CollisionResponse.Of(20, 1000);
        voice.Trigger(profile, ImpactMaterial.Metal, true);
        for (int i = 0; i < 4410; i++) voice.Sample();
        var expected = Enumerable.Range(0, 1024).Select(_ => voice.Sample()).ToArray();
        bool restoredExactly = true;
        for (int loop = 0; loop < 3; loop++)
        {
            restored.Restore(profile, ImpactMaterial.Metal, true, .2f);
            restoredExactly &= expected.SequenceEqual(Enumerable.Range(0, 1024).Select(_ => restored.Sample()));
        }
        restored.Clear();
        Check(restoredExactly && restored.Sample() == 0,
            "collision tail: seek/loop reconstructs exact deterministic sound age and clear removes it");
        Check(energies.Zip(energies.Skip(1)).All(x => x.Second > x.First * 1.2f),
            "collision: 4/10/20/35 m/s have clearly increasing sound energy, including above the old 10 m/s ceiling");
        Check(CollisionResponse.Of(20, 1000).EnergyJoules == CollisionResponse.Of(10, 4000).EnergyJoules
            && CollisionResponse.Of(15, 2000).Weight > CollisionResponse.Of(15, 500).Weight,
            "collision: equivalent normal kinetic energy matches; greater mass at equal speed hits harder");
        var mixed = new EngineSound { Rpm = 5800, Throttle = .9f, Speed = 28, Gear = 3, Slide = .25f, Ground = SurfaceType.Gravel };
        var mix = Render(mixed, () => mixed.Collision(35, 1000)); Wav("collision-35-mps-engine-and-tyres", mix);
        Check(mix.All(float.IsFinite) && mix.Count(v => Math.Abs(v) >= .999f) == 0,
            "collision: production engine/tyre mix keeps headroom without clipped samples at default gains");
        mixed.Free();
        // The Sputnik's engine is 30 % louder than the others': same state, its mix is louder (the five-cylinder's character adds to the gain) and still has headroom.
        var other = new EngineSound { Rpm = 5800, Throttle = .9f, Speed = 28, Gear = 3, Ground = SurfaceType.Gravel, Grade = 2 };
        var sput = new EngineSound { Rpm = 5800, Throttle = .9f, Speed = 28, Gear = 3, Ground = SurfaceType.Gravel, Grade = 2, FiveCylinder = true };
        var otherMix = Render(other, () => { }); var sputMix = Render(sput, () => { });
        double rmsOther = Math.Sqrt(otherMix.Average(v => (double)v * v)), rmsSput = Math.Sqrt(sputMix.Average(v => (double)v * v));
        Check(EngineSound.SputnikLoudness == 1.3f && rmsSput > rmsOther * 1.15 && sputMix.All(float.IsFinite) && sputMix.Count(v => Math.Abs(v) >= .999f) == 0,
            $"sound: the Sputnik's engine is louder than the jet fuel's at the same state, with headroom (RMS {rmsSput:F3} against {rmsOther:F3})");
        other.Free(); sput.Free();

        float PhysicalHit(Vector3 velocity, Vector2 obstacleVelocity)
        {
            var car = new CarSim(new CarParams(), new FlatGround()) { Pos = new Vector3(0, .8f, 0), Vel = velocity,
                WorldMin = -100, WorldMax = 100,
                Obstacles = new[] { new Obstacle(new Vector2(0, -3), 1, obstacleVelocity) } };
            car.Step(1f / 240); return car.LastImpact;
        }
        float head = PhysicalHit(new Vector3(0, 0, -20), Vector2.Zero);
        float glancing = PhysicalHit(new Vector3(38, 0, -5), Vector2.Zero);
        float oncoming = PhysicalHit(new Vector3(0, 0, -20), new Vector2(0, 10));
        Check(head > glancing * 2 && oncoming > head * 1.2f,
            $"collision: actual physics uses contact-normal/relative speed (head {head:F2}, glance {glancing:F2}, oncoming {oncoming:F2})");
        var matCar = new CarSim(new CarParams(), new FlatGround()) { Pos = new Vector3(0, .8f, 0), Vel = new Vector3(0, 0, -20),
            WorldMin = -100, WorldMax = 100, Obstacles = new[] { new Obstacle(new Vector2(0, -3), 1, Material: ImpactMaterial.Wood) } };
        var plainCar = new CarSim(new CarParams(), new FlatGround()) { Pos = matCar.Pos, Vel = matCar.Vel, WorldMin = -100, WorldMax = 100,
            Obstacles = new[] { new Obstacle(new Vector2(0, -3), 1) } };
        matCar.Step(1f / 240); plainCar.Step(1f / 240);
        Check(matCar.LastImpactMaterial == ImpactMaterial.Wood && matCar.Pos == plainCar.Pos && matCar.Vel == plainCar.Vel
            && matCar.AngVel == plainCar.AngVel && matCar.LastImpact == plainCar.LastImpact,
            "collision material: production contact reports target type without changing physics or impact strength");

        var camera = new ChaseCamera(); AddChild(camera);
        Vector3 eye = new(0, 5, 10), target = Vector3.Zero;
        float CameraKick(float speed, float near, bool follow)
        {
            camera.ClearCollision(); camera.Near = near;
            if (follow) camera.Snap(target); else camera.Pose(eye, target, target, 0);
            var before = camera.GlobalTransform;
            camera.Collision(speed, 1000);
            if (follow) camera.Follow(target, Vector3.Zero, 1f / 60); else camera.Pose(eye, target, target, 1f / 60);
            return before.Basis.GetRotationQuaternion().AngleTo(camera.GlobalBasis.GetRotationQuaternion());
        }
        foreach (var mode in new[] { (near: .5f, follow: true), (near: .3f, follow: false), (near: .06f, follow: false) })
        {
            float light = CameraKick(4, mode.near, mode.follow), heavy = CameraKick(35, mode.near, mode.follow);
            Check(heavy > light * 3 && heavy < Mathf.DegToRad(4),
                $"camera collision: increasing bounded angular kick, near {mode.near}, follow {mode.follow}");
        }
        camera.ClearCollision(); camera.Pose(eye, target, target, 0); var rest = camera.GlobalTransform;
        camera.Collision(35, 1000);
        for (int i = 0; i < 120; i++) camera.Pose(eye, target, target, 1f / 60);
        Check(camera.GlobalTransform.IsEqualApprox(rest), "camera collision: settles fully, without lasting roll or offset");
        camera.Collision(35, 1000); camera.ClearCollision(); camera.Pose(eye, target, target, 0);
        bool cleared = camera.GlobalTransform.IsEqualApprox(rest);
        camera.ShakeEnabled = false; camera.Collision(35, 1000); camera.Pose(eye, target, target, .02f);
        Check(cleared && camera.GlobalTransform.IsEqualApprox(rest), "camera collision: seek clear and shake-disabled mode suppress the effect");
        camera.Free();

        var pedal = Quiet(); pedal.Drivetrain(2, false, false, .7f, 4500, true);
        Wav("clutch-pedal-down", Render(pedal, () => pedal.Drivetrain(2, true, false, .7f, 4500, true))); pedal.Free();
        foreach (float load in new[] { .15f, 1f })
        {
            var snd = Quiet(); snd.Drivetrain(2, true, false, load, 6000, true);
            Wav(load < .5f ? "clutch-release-gentle" : "clutch-release-loaded", Render(snd, () => snd.Drivetrain(2, false, false, load, 6000, true))); snd.Free();
        }
        var shift = Quiet(); shift.Drivetrain(2, false, false, 1, 6000, true);
        Wav("gear-engagement-upshift", Render(shift, () => shift.Drivetrain(3, false, false, 1, 6000, true))); shift.Free();
        var once = Quiet(); var held = Quiet();
        foreach (var snd in new[] { once, held }) { snd.Drivetrain(2, true, false, 1, 6000, true); snd.Drivetrain(3, false, false, 1, 6000, true); }
        bool duplicates = false;
        for (int i = 0; i < rate; i++)
        {
            held.Drivetrain(3, false, false, 1, 6000, true);
            duplicates |= once.NextSample() != held.NextSample();
        }
        Check(!duplicates, "drivetrain: held input and unchanged gear never retrigger the engagement");
        held.ClearDrivetrain(); held.ClearCollision();
        held.Drivetrain(1, false, false, 0, 900, false); held.Drivetrain(5, true, false, 1, 6000, true);
        Check(Enumerable.Range(0, 500).All(_ => held.NextSample() == 0), "drivetrain: seek/pause/restart baseline invents no engagement");
        held.ClearDrivetrain(); held.Drivetrain(5, false, false, 1, 6700, true);
        foreach (int gear in new[] { 4, 3, 2, 1 }) held.Drivetrain(gear, false, false, 1, 6700, false);
        held.Drivetrain(1, false, false, 1, 6700, true);
        Check(Enumerable.Range(0, 500).All(_ => held.NextSample() == 0),
            "drivetrain: crash-induced 5-to-1 downshift cascade and recovery baseline make no repeated clunks");
        once.Free(); held.Free();

        var parameters = new CarParams(); var ground = new FlatGround();
        var original = new CarSim(parameters, ground); var ghost = new Ghost();
        for (int frame = 0; frame <= 30; frame++)
        {
            original.Clutch = frame >= 5 && frame < 12;
            original.Neutral = frame >= 20;
            original.Gear = frame >= 12 ? 3 : 2;
            ghost.Record(frame / 30f, original, frame == 15 ? 27f : 0, 0, 0, -1,
                frame == 15 ? ImpactMaterial.Stone : ImpactMaterial.Unknown, frame == 15);
        }
        ghost = ghost.CopyWithTime(1);
        string ghostPath = Path.Combine(output, "drivetrain-roundtrip.bin"); ghost.Save(ghostPath);
        var loaded = Ghost.Load(ghostPath, parameters, ground);
        var puppet = new CarSim(parameters, ground);
        bool flags = loaded != null && File.ReadAllBytes(ghostPath).SequenceEqual(ghost.ToBytes());
        float recordedHit = 0; bool recordedMaterial = false;
        for (int frame = 0; frame <= 30 && loaded != null; frame++)
        {
            var events = loaded.Apply(frame / 30f, puppet, 1f / 30);
            recordedHit = Math.Max(recordedHit, events.Impact);
            if (events.Impact > 0) recordedMaterial = events.ImpactMaterial == ImpactMaterial.Stone && events.ImpactBroke;
            flags &= puppet.Clutch == (frame >= 5 && frame < 12) && puppet.Neutral == (frame >= 20)
                && puppet.Gear == (frame >= 12 ? 3 : 2);
        }
        Check(flags && recordedHit == 27 && recordedMaterial, "ghost file: clutch/neutral/gear, collision speed/material/fracture survive both serialization paths and replay");
        var latest = loaded?.CollisionBefore(.55f);
        Check(latest is Ghost.Collision c && c.Material == ImpactMaterial.Stone && c.Broke && c.Speed == 27
            && loaded.CollisionBefore(.4f) == null,
            "ghost seek: collision history restores original type/fracture and never invents a future hit");
        var pending = new Ghost(); pending.Record(0, original, 0, 0, 0);
        pending.Record(.004f, original, 4, 0, 0, -1, ImpactMaterial.Snow);
        pending.Record(.008f, original, 12, 0, 0, -1, ImpactMaterial.Metal, true);
        pending.Record(.012f, original, 6, 0, 0, -1, ImpactMaterial.Wood);
        pending.Record(1f / 30, original, 0, 0, 0);
        pending.Apply(0, puppet, 0); var strongest = pending.Apply(1f / 30, puppet, 1f / 30);
        Check(strongest.Impact == 12 && strongest.ImpactMaterial == ImpactMaterial.Metal && strongest.ImpactBroke,
            "ghost: strongest sub-sample keeps its own material and fracture, not a later weaker contact's type");
        var root = new Node3D(); Tape.Part(root, "root");
        var recorder = new WorldTapeRecorder(); recorder.Begin(new WorldTapeIndex(new[] { ("Test:Feedback", root) }));
        recorder.Record(0); recorder.Record(1);
        var tape = recorder.Finish(1, WorldTapeFingerprint.Of(ghost));
        string pairGhost = Path.Combine(output, "paired-ghost.bin"), pairTape = Path.Combine(output, "paired-tape.bin");
        var saved = WorldTapeFile.SavePair(pairGhost, pairTape, ghost, tape, WorldTapeRealOps.Instance, WorldTapeCodec.Encode, WorldTapeCodec.Decode);
        var pairedGhost = Ghost.Load(pairGhost, parameters, ground);
        Check(saved.GhostSaved && saved.TapeSaved && pairedGhost != null
            && WorldTapeFile.Load(pairTape, pairedGhost, WorldTapeCodec.Decode) != null,
            "ghost/world tape: new clutch flags preserve fingerprint pairing through the real atomic pair save/load");
        root.Free();
        loaded?.Rewind(); var rewindEvent = loaded?.Apply(0, puppet, 0);
        Check(loaded != null && !puppet.Clutch && !puppet.Neutral && rewindEvent?.Impact == 0,
            "ghost: rewind restores clutch state without replaying an old collision");
        var legacy = new Ghost(); original.Clutch = original.Neutral = false;
        for (int frame = 0; frame <= 3; frame++) legacy.Record(frame / 30f, original, 0, 0, 0);
        legacy = legacy.CopyWithTime(.1f); string oldPath = Path.Combine(output, "legacy-zero-flags.bin"); legacy.Save(oldPath);
        var old = Ghost.Load(oldPath, parameters, ground); puppet.Clutch = puppet.Neutral = true;
        old?.Apply(0, puppet, 0);
        Check(old != null && !puppet.Clutch && !puppet.Neutral && old.ToBytes().SequenceEqual(legacy.ToBytes()),
            "ghost: legacy zero flag bits remain byte-identical and do not invent clutch events");

        // Exercise the production backfire controller with the real throttle/RPM
        // pattern from the Siberia plough impact, without running its emitters.
        var scene = new RaceScene();
        var type = typeof(RaceScene); const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        void Set(string field, object value) => type.GetField(field, fields).SetValue(scene, value);
        T Get<T>(string field) => (T)type.GetField(field, fields).GetValue(scene);
        var update = type.GetMethod("UpdateBackfire", fields);
        void Backfire(float dt) => update.Invoke(scene, new object[] { dt });
        var recovery = new CarSim(parameters, ground) { Throttle = 1, Rpm = 6700 };
        Set("_car", recovery); Set("_prevThrottle", 1f); Set("_pops", 4);
        Set("_collisionQuiet", CollisionResponse.Of(25.284f, 1000).Duration + .15f);
        Backfire(.5f); recovery.Throttle = 0; Backfire(.033f);
        bool recoveryQuiet = Get<int>("_pops") == 0;
        Backfire(.5f);
        Check(recoveryQuiet && Get<int>("_pops") == 0,
            "backfire: high-rpm lift after a collision cancels queued pops and never creates a delayed burst");
        recovery.Throttle = 1; Backfire(0); recovery.Throttle = 0; Backfire(0);
        Check(Get<int>("_pops") >= 2, "backfire: a new deliberate lift after collision recovery still produces normal exhaust crackle");
        scene.Free();
        var exhaust = Quiet(); Wav("exhaust-pop-distinct-from-collision", Render(exhaust, () => exhaust.Bang(.9f))); exhaust.Free();
        File.WriteAllText(Path.Combine(output, "measurements.json"), JsonSerializer.Serialize(reviews, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print("Feedback auditions: " + output);
    }
}
