using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using CCCPRacing.Car;
using CCCPRacing.Race;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

// The finish line serialises the ghost on the pair-save worker (TapePairJob with ghostBytes null). Proof of the premise (the finished ghost, _best = _rec.CopyWithTime(time),
// is immutable: ToBytes reads the lists, playback moves only the cursor), byte identity with the main-thread bytes, and the failure path.
// AUDIT (read, not just tested): writers of Ghost's lists are Record/Clear (called on the recorder _rec only), CopyWithTime (writes the NEW ghost) and FromBytes/ReadDamage/Reconstruct
// (a ghost under construction). Apply/Sample/Rewind/DayClockAt/CollisionBefore/ApplyDamage read the lists; Apply/Rewind write only _lastApplied, ApplyDamage only _damageWarning;
// ToBytes reads neither. Nothing is built lazily. The damage States are immutable records whose arrays are copies (CaptureTapeState: Parts.ToArray, _chips.ToArray) and whose
// Geometry.Data byte[] is only ever read (replaced, never written, in CarDamage).
public partial class PhysicsTest
{
    static Ghost GwGhost(int n, bool damage)
    {
        var car = NewCar(new FlatGround()); var rec = new Ghost();
        var rng = new Random(7);
        for (int i = 0; i < n; i++)
        {
            car.Pos = new Vector3(i * 0.5f, 0.8f, MathF.Sin(i * 0.1f) * 3); car.Rot = new Basis(new Quaternion(Vector3.Up, i * 0.01f));
            car.Rpm = 2000 + 3000 * MathF.Abs(MathF.Sin(i * 0.03f)); car.Gear = 1 + i / 700 % 4;
            rec.Record((i + 0.5f) / Ghost.SampleRate, car, i % 97 == 0 ? 6f : 0, i % 53 == 0 ? 1f : 0, 0, 0.3f + i * 1e-4f, ImpactMaterial.Unknown, i % 97 == 0);
        }
        var g = rec.CopyWithTime(n / Ghost.SampleRate);
        if (damage)
        {
            // Real-size damage stream (a lap's worth: 2000+ states, 30 parts, a few chips) as the immutable records CarDamage.CaptureTapeState makes.
            var list = (List<CarDamage.State>)typeof(Ghost).GetField("_damage", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(g);
            var shapes = new[] { new CarDamage.Geometry(11UL, Enumerable.Range(0, 3000).Select(k => (byte)(k * 7)).ToArray()), new CarDamage.Geometry(12UL, Enumerable.Range(0, 3100).Select(k => (byte)(k * 5)).ToArray()) };
            for (int i = 0; i < n; i++)
            {
                var parts = Enumerable.Range(0, 30).Select(k => new CarDamage.PartPose(i > 900 && k % 7 == 0, new Transform3D(new Basis(new Quaternion(Vector3.Up, (float)rng.NextDouble())), new Vector3(k, i * .01f, (float)rng.NextDouble())))).ToArray();
                var chips = Enumerable.Range(0, i % 5).Select(k => new CarDamage.Chip(i / 30f, new Vector3(k, 0, i), Vector3.Up, new Color(.2f, .3f, .4f), 1, .1f, .2f, 2, 9.8f, .1f, k % 2 == 0)).ToArray();
                list.Add(new CarDamage.State(shapes[i > 1500 ? 1 : 0], parts, chips, i / 30f));
            }
        }
        return g;
    }

    static string GwHash(byte[] b) => Convert.ToHexString(SHA256.HashData(b));
    static string GwHashFile(string p) => File.Exists(p) ? GwHash(File.ReadAllBytes(p)) : "<none>";

    void GhostBytesWorkerTests()
    {
        GD.Print("--- ghost bytes on the pair-save worker ---");
        TapeRun("gw 1", GwImmutability); TapeRun("gw 2", GwIdentity); TapeRun("gw 3", GwFailure); TapeRun("gw memory", GwMemoryBudget);
        GD.Print($"--- ghost bytes worker tests: {_failures} failure(s) ---");
    }

    void GwMemoryBudget()
    {
        var car=NewCar(new FlatGround());var visual=CarVisual.Create(new CarParams(),Colors.Red);AddChild(visual);
        Check(visual.Damage!=null,"gw memory: real damage model is available");
        if(visual.Damage==null) {visual.Free();return;}
        var recording=new Ghost();recording.Record(0,car,0,0,0,damage:visual.Damage);
        long first=recording.RecordingEstimatedBytes;
        recording.Record(2,car,0,0,0,damage:visual.Damage);
        Check(recording.RecordingEstimatedBytes>first,"gw memory: recording estimate accounts for retained original damage frames");
        var frozen=recording.CopyWithTime(2);byte[] bytes=frozen.ToBytes();
        Check(frozen.RecordingEstimatedBytes==recording.RecordingEstimatedBytes,"gw memory: worker handoff preserves the budget estimate");
        recording.Clear();
        Check(recording.RecordingEstimatedBytes==0 && frozen.RecordingEstimatedBytes>0,"gw memory: clearing recorder releases its accounting without changing the frozen source");
        Check(bytes.SequenceEqual(frozen.ToBytes()),"gw memory: budget accounting does not change serialization or immutable worker bytes");
        visual.Free();
    }

    // 1. Immutability: a worker serialises the ghost again and again while the main thread plays it back; every result is identical, and the ghost itself is unchanged afterwards.
    void GwImmutability()
    {
        foreach (bool damage in new[] { false, true })
        {
            string tag = damage ? "V4 (damage)" : "V3";
            var g = GwGhost(2242, damage);
            TapeCheck(g.HasDamage == damage, $"gw 1 {tag}: the ghost has damage = {damage} ({g.Count} samples)");
            byte[] expected = g.ToBytes(); string h0 = GwHash(expected);
            TapeCheck(BitConverter.ToUInt32(expected, 0) == (damage ? 0x34434343u : 0x33434343u), $"gw 1 {tag}: the file magic is {(damage ? "CCC4" : "CCC3")}");
            int rounds = damage ? 6 : 25; var hashes = new List<string>(); Exception fault = null; int played = 0;
            var worker = new Thread(() =>
            {
                try { for (int r = 0; r < rounds; r++) hashes.Add(GwHash(g.ToBytes())); } catch (Exception e) { fault = e; }
            });
            var car = NewCar(new FlatGround());
            worker.Start();
            while (worker.IsAlive)      // the main thread: everything that plays a ghost back, over and over, wrapping round like the replay
            {
                for (int k = 0; k < 400; k++)
                {
                    float t = (k * 0.37f) % g.Duration;
                    g.Apply(t, car, 1f / 60f); g.Sample(t); if (g.HasDayClock) g.DayClockAt(t); g.CollisionBefore(t);
                    if (k % 50 == 49) g.Rewind();
                    played++;
                }
                g.Rewind();
            }
            worker.Join();
            TapeCheck(fault == null && hashes.Count == rounds, $"gw 1 {tag}: the worker made {hashes.Count}/{rounds} serialisations without a fault ({fault?.GetType().Name})");
            TapeCheck(hashes.All(h => h == h0), $"gw 1 {tag}: every serialisation made while the main thread played the ghost back ({played} playback steps) is byte-identical (SHA-256 {h0[..12]})");
            TapeCheck(GwHash(g.ToBytes()) == h0, $"gw 1 {tag}: and the ghost is unchanged afterwards");
        }
    }

    // 2. Byte identity: worker-made bytes == main-thread ToBytes == the file WriteTo writes; FromBytes round trips; an old V3 file loads.
    void GwIdentity()
    {
        var dir = TapeTmpDir();
        try
        {
            foreach (bool damage in new[] { false, true })
            {
                string tag = damage ? "V4" : "V3"; var g = GwGhost(damage ? 2242 : 900, damage);
                byte[] main = g.ToBytes();
                var rec = new WorldTapeRecorder(); using var w = TapeWorld.Build(2, 3, false); TapeAsyncLap(rec, w, 3f, 0f);
                string gp = Path.Combine(dir, $"ghost_{tag}.bin"), tp = Path.Combine(dir, $"tape_{tag}.bin");
                var job = new TapePairJob(new WorldTapeStorage(WorldTapeCodec.Encode, WorldTapeCodec.Decode), TapeAsyncSnapshot(rec), 3f, WorldTapeFingerprint.Of(g), "unit", 0, gp, tp, null, WorldTapeRealOps.Instance, null, g, g);
                TapePairResult res = default; var warns = new List<string>();
                var saves = new TapePairSaves((j, r, t) => res = r, s => { }, s => warns.Add(s));
                saves.Submit(job); saves.Join("test");
                TapeCheck(res.Whole && warns.Count == 0 && job.GhostMs >= 0, $"gw 2 {tag}: the pair save with the worker-made bytes is whole ({res}), ghost serialised on the worker in {job.GhostMs} ms");
                TapeCheck(TapeSame(gp, main), $"gw 2 {tag}: the ghost file the worker saved is byte for byte the main thread's ToBytes() ({main.Length} bytes)");
                string wt = Path.Combine(dir, $"writeto_{tag}.bin");
                TapeCheck(g.WriteTo(wt) && TapeSame(wt, main), $"gw 2 {tag}: and the file Ghost.WriteTo writes");
                var back = Ghost.FromBytes(File.ReadAllBytes(gp), TapeParams, null, out string err);
                TapeCheck(back != null && back.Count == g.Count && back.HasDamage == damage && back.LapTime == g.LapTime && GwHash(back.ToBytes()) == GwHash(main), $"gw 2 {tag}: Ghost.FromBytes round-trips the saved file ({err ?? "ok"}): same samples, damage and bytes again");
                TapeCheck(TapeLoadGhost(gp) is { } loaded && loaded.Count == g.Count && WorldTapeFingerprint.Of(loaded) == WorldTapeFingerprint.Of(g), $"gw 2 {tag}: Ghost.Load of the saved file has the same fingerprint");
            }
            // an old V3 file (written before the damage stream existed): the bytes of a ghost without damage load and play, damage is simply absent
            var old = GwGhost(300, false); byte[] v3 = old.ToBytes();
            var o = Ghost.FromBytes(v3, TapeParams, null);
            TapeCheck(BitConverter.ToUInt32(v3, 0) == 0x33434343u && o != null && !o.HasDamage && o.Count == 300, "gw 2: an old V3 file (no damage stream) still loads");
        }
        finally { TapeRmDir(dir); }
    }

    // 3. Failure: ToBytes throws on the worker => the previous pair on disk is untouched, a visible failure line, nothing replaced, no tmp, the next save works.
    void GwFailure()
    {
        var dir = TapeTmpDir();
        try
        {
            string gp = Path.Combine(dir, "ghost_u.bin"), tp = Path.Combine(dir, "tape_u.bin");
            var good = GwGhost(600, true); var bad = GwGhost(600, true);
            var wheels = typeof(Ghost).GetField("_wheels", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(bad);
            wheels.GetType().GetMethod("Clear").Invoke(wheels, null);      // ToBytes now throws (no wheel frames)
            bool threwOnMain = false; try { bad.ToBytes(); } catch (ArgumentOutOfRangeException) { threwOnMain = true; }
            TapeCheck(threwOnMain, "gw 3: setup, the broken ghost's ToBytes throws");
            var rec = new WorldTapeRecorder(); using var w = TapeWorld.Build(2, 3, false); TapeAsyncLap(rec, w, 3f, 0f);
            var store = new WorldTapeStorage(WorldTapeCodec.Encode, WorldTapeCodec.Decode);
            var applied = new List<(TapePairJob Job, TapePairResult Res, WorldTape Tape)>(); var warns = new List<string>(); var log = new List<string>();
            var saves = new TapePairSaves((j, r, t) => applied.Add((j, r, t)), s => log.Add(s), s => warns.Add(s));
            saves.Submit(new TapePairJob(store, TapeAsyncSnapshot(rec), 3f, WorldTapeFingerprint.Of(good), "unit", 0, gp, tp, null, WorldTapeRealOps.Instance, null, good, good)); saves.Join("test");
            TapeCheck(applied.Count == 1 && applied[0].Res.Whole, "gw 3: setup, the previous pair is on disk");
            string hg = GwHashFile(gp), ht = GwHashFile(tp); applied.Clear();
            var job = new TapePairJob(store, TapeAsyncSnapshot(rec), 3f, WorldTapeFingerprint.Of(bad), "unit", 0, gp, tp, null, WorldTapeRealOps.Instance, null, bad, bad);
            saves.Submit(job); saves.Join("the scene's exit");
            TapeCheck(applied.Count == 1 && applied[0].Res.Refused && !applied[0].Res.GhostSaved && !applied[0].Res.TapeSaved && applied[0].Tape == null && saves.Pending == null, $"gw 3: the failed serialisation is applied as refused ({applied.FirstOrDefault().Res}), the pending slot is free");
            TapeCheck(warns.Count == 1 && warns[0].Contains("save failed on the worker") && warns[0].Contains("ArgumentOutOfRangeException") && warns[0].Contains("phase 'serialising'"), $"gw 3: one visible failure line names the exception and the phase ({string.Join(" | ", warns)})");
            TapeCheck(GwHashFile(gp) == hg && GwHashFile(tp) == ht && TapeAsyncTmp(dir) == "", "gw 3: the previous pair is unchanged on disk (SHA-256 of both files before == after) and no tmp is left");
            TapeCheck(applied[0].Job.Tag == bad, "gw 3: the job hands the same ghost back as its tag (the scene keeps the new ghost in memory without a tape, as for any refused save; the files on disk keep the old record)");
            warns.Clear(); applied.Clear();
            saves.Submit(new TapePairJob(store, TapeAsyncSnapshot(rec), 3f, WorldTapeFingerprint.Of(good), "unit", 0, gp, tp, null, WorldTapeRealOps.Instance, null, good, good)); saves.Join("test");
            TapeCheck(applied.Count == 1 && applied[0].Res.Whole && warns.Count == 0 && GwHashFile(gp) == hg, "gw 3: the next save after the failure works");
            // a gate held after the serialisation: the main thread is free and nothing is on disk yet
            var g2 = new TapeWorkerGate("serialised"); File.Delete(gp); File.Delete(tp); applied.Clear();
            var held = new TapePairJob(store, TapeAsyncSnapshot(rec), 3f, WorldTapeFingerprint.Of(good), "unit", 0, gp, tp, null, WorldTapeRealOps.Instance, g2, good, good);
            saves.Submit(held);
            TapeCheck(TapeAsyncWaitReached(g2) && held.Phase == "serialised" && held.GhostMs >= 0 && !File.Exists(gp) && !File.Exists(tp) && !File.Exists(gp + ".tmp"), "gw 3: a worker held after the ghost's serialisation has written nothing yet; the main thread was free");
            saves.Join("test");
            TapeCheck(applied.Count == 1 && applied[0].Res.Whole && GwHashFile(gp) == hg, "gw 3: released, it saves the same ghost bytes");
        }
        finally { TapeRmDir(dir); }
    }
}
