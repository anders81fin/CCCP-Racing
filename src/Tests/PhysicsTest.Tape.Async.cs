using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Tests;

// T10: the async pair save, deterministically. The REAL recorder (Snapshot), the REAL TapePairJob / TapePairSaves (the production lifecycle: Submit, Poll, Join) and the REAL SavePairBytes on a REAL worker
// task, with the worker HELD at a named point by a TapeWorkerGate (replay-tape.md section 17), so the main thread does its things while the worker is in a known place. No sleeps decide anything:
// the test waits for the gate to be reached and the production code releases the gate when the main thread starts to wait (or the test does).
//   (a) the recorder is reused for a different lap while the worker is held: the first pair on disk is the first lap, byte for byte
//   (b) the exit while the worker has replaced the ghost and not the tape: after the wait both are on disk
//   (c) the title's replay waits for the slot (and a poll never blocks)
//   (d) a next save while one is pending: serialised in order, the final files are the second pair
//   (e) a failing ghost replacement and a failing tape replacement through the worker: what is applied is what is on disk
//   (f) a worker exception is surfaced and never leaves the pending slot stuck
//   (g) a long wait logs its diagnostic
public partial class PhysicsTest
{
    static string TapeMut = "";      // the mutant of the mutation run: "alias" (the snapshot aliases the recorder's buffers), "nowait" (a next save does not wait), "early" (the result is applied before the file is written)

    sealed class TapeNoWaitSaves : TapePairSaves
    {
        public TapeNoWaitSaves(ApplyFn a, Action<string> l, Action<string> w) : base(a, l, w) { }
        public override void Submit(TapePairJob job) { Pending = job; job.Start(); }      // the mutant: an earlier pending save is neither waited for nor applied
    }
    sealed class TapeEarlySaves : TapePairSaves
    {
        readonly ApplyFn _a;
        public TapeEarlySaves(ApplyFn a, Action<string> l, Action<string> w) : base(a, l, w) { _a = a; }
        public override void Submit(TapePairJob job) { base.Submit(job); _a(job, new TapePairResult(true, true), null); }      // the mutant: the slot is told "saved" before the worker has written anything
    }

    static TapePairSaves TapeAsyncSaves(TapePairSaves.ApplyFn apply, Action<string> log, Action<string> warn) => TapeMut switch
    {
        "nowait" => new TapeNoWaitSaves(apply, log, warn),
        "early" => new TapeEarlySaves(apply, log, warn),
        _ => new TapePairSaves(apply, log, warn),
    };

    /// <summary>The recording of the real recorder, copied: or (the mutant) the recorder's own buffers.</summary>
    static WorldTapeRecorder.Recording TapeAsyncSnapshot(WorldTapeRecorder rec)
    {
        var r = rec.Snapshot();
        if (TapeMut != "alias") return r;
        object Field(string n) => typeof(WorldTapeRecorder).GetField(n, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(rec);
        void Set(string n, object v) => typeof(WorldTapeRecorder.Recording).GetField(n, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(r, v);
        Set("Samples", Field("_samples"));
        return r;
    }

    static void TapeAsyncLap(WorldTapeRecorder rec, TapeWorld w, float seconds, float off)
    {
        w.Tick(off); rec.Begin(w.Index);
        int ticks = (int)MathF.Round(seconds * 60f);
        for (int n = 0; n <= ticks; n++) { float t = n / 60f; w.Tick(off + t); rec.Record(t); }
    }

    /// <summary>The tape the lap should give, made by a second recorder and world that never saw anything else.</summary>
    static byte[] TapeAsyncExpected(float seconds, float off, ulong fp, string track, int set)
    {
        using var w = TapeWorld.Build(2, 3, false); var rec = new WorldTapeRecorder();
        TapeAsyncLap(rec, w, seconds, off);
        var t = WorldTapeRecorder.Compress(rec.Snapshot(), seconds, fp); t.TrackId = track; t.Set = set;
        return WorldTapeCodec.Encode(t);
    }

    static TapePairJob TapeAsyncJob(WorldTapeRecorder rec, float seconds, Ghost g, string dir, ITapeFileOps inner, TapeWorkerGate gate, int set = 0)
        => new(new WorldTapeStorage(WorldTapeCodec.Encode, WorldTapeCodec.Decode), TapeAsyncSnapshot(rec), seconds, WorldTapeFingerprint.Of(g), "unit", set, Path.Combine(dir, "ghost_u.bin"), Path.Combine(dir, "tape_u.bin"), g.ToBytes(), inner, gate, g);

    static bool TapeSame(string path, byte[] bytes) => File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes);
    static bool TapeAsyncWaitReached(TapeWorkerGate g) => g.WaitReached(20000);
    static string TapeAsyncTmp(string dir) => string.Join(",", Directory.GetFiles(dir, "*.tmp").Select(Path.GetFileName));

    void TapeT23()
    {
        var dir = TapeTmpDir(); var toFree = new List<TapePairJob>();
        var log = new List<string>(); var warns = new List<string>(); var applied = new List<(TapePairJob Job, TapePairResult Res, WorldTape Tape)>();
        string gp = Path.Combine(dir, "ghost_u.bin"), tp = Path.Combine(dir, "tape_u.bin");
        TapePairSaves Saves() => TapeAsyncSaves((j, r, t) => applied.Add((j, r, t)), s => log.Add(s), s => warns.Add(s));
        TapePairJob Track(TapePairJob j) { toFree.Add(j); return j; }
        var real = WorldTapeRealOps.Instance;
        TapeWorld w = null;
        try
        {
            w = TapeWorld.Build(2, 3, false);
            Ghost gA = TapeGhost(300, 1.5f), gB = TapeGhost(400, 3f);
            ulong fpA = WorldTapeFingerprint.Of(gA), fpB = WorldTapeFingerprint.Of(gB);
            byte[] tapeA = TapeAsyncExpected(3f, 0f, fpA, "unit", 0), tapeB = TapeAsyncExpected(5f, 40f, fpB, "unit", 0);
            TapeCheck(!tapeA.AsSpan().SequenceEqual(tapeB), "tape 23: setup, the two laps give different tapes");

            // ---- (a) the recorder is reused for a different lap while the worker is held after the snapshot
            {
                var rec = new WorldTapeRecorder(); var saves = Saves(); applied.Clear();
                TapeAsyncLap(rec, w, 3f, 0f);
                var gate = new TapeWorkerGate("start");
                var job = Track(TapeAsyncJob(rec, 3f, gA, dir, real, gate)); saves.Submit(job);
                bool reached = TapeAsyncWaitReached(gate);
                TapeCheck(reached && gate.State == "held at start" && !File.Exists(gp) && !File.Exists(tp), $"tape 23a: the worker is held after the snapshot, before it wrote anything (gate '{gate.State}')");
                TapeCheck(applied.Count == 0 && saves.Pending == job, "tape 23a: while the worker is held nothing is applied and the save is pending");
                rec.Clear(); TapeAsyncLap(rec, w, 5f, 40f);      // the next lap begins at the same crossing, in the same buffers
                TapeCheck(rec.Samples > 70, $"tape 23a: the reused recorder holds the other lap ({rec.Samples} samples)");
                gate.Release(); saves.Join("test");
                TapeCheck(applied.Count == 1 && applied[0].Res.Whole && File.Exists(gp) && File.Exists(tp), "tape 23a: the held save finished and was applied whole");
                TapeCheck(TapeSame(tp, tapeA), "tape 23a: the first saved pair's tape is the FIRST lap's, byte for byte, although the recorder was reused for another lap before the worker ran");
                TapeCheck(TapeSame(gp, gA.ToBytes()) && File.Exists(tp) && WorldTapeCodec.Decode(File.ReadAllBytes(tp)).GhostFingerprint == fpA && Math.Abs(WorldTapeCodec.Decode(File.ReadAllBytes(tp)).LapTime - 3f) < 1e-4f, "tape 23a: the ghost file is lap one's and the tape decodes to lap one (fingerprint and lap time)");
                // the lap recorded meanwhile is itself intact (the worker did not take anything from the recorder)
                var meanwhile = WorldTapeRecorder.Compress(rec.Snapshot(), 5f, fpB); meanwhile.TrackId = "unit"; meanwhile.Set = 0;
                TapeCheck(WorldTapeCodec.Encode(meanwhile).AsSpan().SequenceEqual(tapeB), "tape 23a: and the lap recorded meanwhile is intact too (it gives the second lap's tape, byte for byte)");
                File.Delete(gp); File.Delete(tp);
            }

            // ---- (b) the exit while the ghost is replaced and the tape not: the wait finishes the pair
            {
                var rec = new WorldTapeRecorder(); var saves = Saves(); applied.Clear(); log.Clear();
                TapeAsyncLap(rec, w, 3f, 0f);
                var gate = new TapeWorkerGate("ghost-replaced");
                var job = Track(TapeAsyncJob(rec, 3f, gA, dir, real, gate)); saves.Submit(job);
                bool reached = TapeAsyncWaitReached(gate);
                TapeCheck(reached && File.Exists(gp) && !File.Exists(tp) && TapeSame(gp, gA.ToBytes()), "tape 23b: the worker is held with the ghost replaced and the tape not (the pair is half there)");
                rec.Clear(); TapeAsyncLap(rec, w, 5f, 40f);
                saves.Join("the scene's exit");
                var tr = gate.Trace;
                TapeCheck(applied.Count == 1 && applied[0].Res.Whole && saves.Pending == null && File.Exists(tp) && TapeSame(tp, tapeA) && TapeAsyncTmp(dir) == "", $"tape 23b: the exit waited for the worker: the pair is whole on disk, the tape is lap one's, no tmp left ('{TapeAsyncTmp(dir)}')");
                int iHeld = Array.IndexOf(tr, "held at ghost-replaced"), iWait = Array.IndexOf(tr, "main waits at the scene's exit"), iPass = Array.IndexOf(tr, "passed ghost-replaced");
                TapeCheck(iHeld >= 0 && iWait > iHeld && iPass > iWait && log.Any(l => l.Contains("waited") && l.Contains("the scene's exit")), $"tape 23b: the order was held, then the main thread waits at the exit, then the worker goes on [{string.Join("; ", tr)}]");
                File.Delete(gp); File.Delete(tp);
            }

            // ---- (c) the title's replay waits for the slot; a poll never blocks
            {
                var rec = new WorldTapeRecorder(); var saves = Saves(); applied.Clear();
                TapeAsyncLap(rec, w, 3f, 0f);
                var gate = new TapeWorkerGate("tmp-written");
                var job = Track(TapeAsyncJob(rec, 3f, gA, dir, real, gate)); saves.Submit(job);
                TapeCheck(TapeAsyncWaitReached(gate) && File.Exists(gp + ".tmp") && File.Exists(tp + ".tmp") && !File.Exists(gp) && !File.Exists(tp), "tape 23c: the worker is held with both tmp files written and no final file");
                var sw = System.Diagnostics.Stopwatch.StartNew(); for (int i = 0; i < 20; i++) saves.Poll();
                TapeCheck(sw.ElapsedMilliseconds < 1000 && applied.Count == 0 && saves.Pending == job, $"tape 23c: polling a held worker returns at once and applies nothing ({sw.ElapsedMilliseconds} ms for 20 polls)");
                TapeCheck(applied.Count == 0, "tape 23c: before the title waits, the slot knows nothing of the save");
                saves.Join("the title's replay");
                var slot = applied.Count == 1 ? applied[0] : default;
                var disk = File.Exists(gp) ? TapeSeams.LoadTape(tp, TapeLoadGhost(gp)) : null;
                TapeCheck(slot.Job != null && slot.Res.Whole && slot.Tape != null && disk != null && Math.Abs(slot.Tape.LapTime - disk.LapTime) < 1e-4f && slot.Tape.GhostFingerprint == fpA, "tape 23c: after the wait the slot holds the tape that is on disk, matching the ghost");
                TapeCheck(gate.Trace.ToList().IndexOf("main waits at the title's replay") > gate.Trace.ToList().IndexOf("held at tmp-written"), "tape 23c: the title waited for the held worker");
                File.Delete(gp); File.Delete(tp);
            }

            // ---- (d) a next save while one is pending: serialised in order, never interleaved
            {
                var rec = new WorldTapeRecorder(); var saves = Saves(); applied.Clear(); var order = new List<string>(); var sync = new object();
                ITapeFileOps Spy(string tag) => new TapeInjectOps { OnStep = s => { lock (sync) order.Add(tag + ":" + s); } };
                TapeAsyncLap(rec, w, 3f, 0f);
                var gateA = new TapeWorkerGate("start");
                var jobA = Track(TapeAsyncJob(rec, 3f, gA, dir, Spy("A"), gateA)); saves.Submit(jobA);
                TapeAsyncWaitReached(gateA);
                rec.Clear(); TapeAsyncLap(rec, w, 5f, 40f);      // lap two finishes while the save of lap one is still pending
                var jobB = Track(TapeAsyncJob(rec, 5f, gB, dir, Spy("B"), null));
                saves.Submit(jobB);      // must wait for A (which this very wait releases), apply it, and only then start B
                TapeCheck(applied.Count >= 1 && applied[0].Job == jobA && applied[0].Res.Whole, "tape 23d: the earlier save was waited for and applied first");
                saves.Join("test");
                TapeCheck(applied.Count == 2 && applied[0].Job == jobA && applied[1].Job == jobB && saves.Pending == null, $"tape 23d: both saves were applied, in order (applied {applied.Count})");
                int lastA = order.FindLastIndex(o => o.StartsWith("A:")), firstB = order.FindIndex(o => o.StartsWith("B:"));
                TapeCheck(lastA >= 0 && firstB > lastA, $"tape 23d: every file step of the first save came before any of the second (last A at {lastA}, first B at {firstB})");
                TapeCheck(TapeSame(tp, tapeB) && TapeSame(gp, gB.ToBytes()) && TapeAsyncTmp(dir) == "", "tape 23d: the final files are the second pair, tape and ghost together, and no tmp is left");
                File.Delete(gp); File.Delete(tp);
            }

            // ---- (e) failures through the worker: what is applied is what is on disk
            {
                var rec = new WorldTapeRecorder(); var saves = Saves(); applied.Clear(); warns.Clear();
                TapeAsyncLap(rec, w, 3f, 0f);
                saves.Submit(Track(TapeAsyncJob(rec, 3f, gA, dir, real, null))); saves.Join("test");      // the old pair
                byte[] oldG = File.ReadAllBytes(gp), oldT = File.ReadAllBytes(tp);
                TapeCheck(applied.Count == 1 && applied[0].Res.Whole, "tape 23e: setup, the old pair is saved through the worker");
                // the ghost replacement fails (the first Replace)
                applied.Clear(); warns.Clear(); rec.Clear(); TapeAsyncLap(rec, w, 5f, 40f);
                var inj1 = new TapeInjectOps { ThrowOnReplaceNo = 1 }; var gate1 = new TapeWorkerGate("verified");
                var job1 = Track(TapeAsyncJob(rec, 5f, gB, dir, inj1, gate1)); saves.Submit(job1); saves.Join("test");
                TapeCheck(applied.Count == 1 && applied[0].Res.Refused && !applied[0].Res.TapeSaved && TapeSame(gp, oldG) && TapeSame(tp, oldT), "tape 23e: a failing ghost replacement is applied as refused (GhostSaved false, TapeSaved false) and both old files stay byte for byte");
                TapeCheck(TapeAsyncTmp(dir) == "" && warns.Count == 1 && warns[0].Contains("save refused"), $"tape 23e: no tmp is left and one warning came back from the worker to the main thread ({string.Join(" | ", warns)})");
                // the tape replacement fails (the second Replace): the ghost is the record
                applied.Clear(); warns.Clear(); rec.Clear(); TapeAsyncLap(rec, w, 5f, 40f);
                var inj2 = new TapeInjectOps { ThrowOnReplaceNo = 2 }; var gate2 = new TapeWorkerGate("verified");
                var job2 = Track(TapeAsyncJob(rec, 5f, gB, dir, inj2, gate2)); saves.Submit(job2); saves.Join("test");
                TapeCheck(applied.Count == 1 && applied[0].Res.GhostSaved && !applied[0].Res.TapeSaved && applied[0].Tape != null && TapeSame(gp, gB.ToBytes()) && TapeSame(tp, oldT), "tape 23e: a failing tape replacement is applied as GhostSaved without TapeSaved: the new ghost is on disk, the old tape stays");
                TapeCheck(TapeAsyncTmp(dir) == "" && warns.Count == 1 && warns[0].Contains("ghost saved, tape not"), $"tape 23e: no tmp is left and the warning says 'ghost saved, tape not' ({string.Join(" | ", warns)})");
                TapeCheck(WorldTapeFile.Load(tp, TapeLoadGhost(gp), WorldTapeCodec.Decode) == null, "tape 23e: and the old tape no longer matches the new ghost: it is dropped by its fingerprint");
                File.Delete(gp); File.Delete(tp);
            }

            // ---- (f) a worker exception is surfaced and the pending slot is never stuck
            {
                var rec = new WorldTapeRecorder(); var saves = Saves(); applied.Clear(); warns.Clear();
                TapeAsyncLap(rec, w, 3f, 0f);
                var gate = new TapeWorkerGate("start", "start");
                var job = Track(TapeAsyncJob(rec, 3f, gA, dir, real, gate)); saves.Submit(job);
                TapeAsyncWaitReached(gate);
                saves.Join("the scene's exit");
                TapeCheck(applied.Count == 1 && applied[0].Res.Refused && saves.Pending == null && !File.Exists(gp) && !File.Exists(tp) && TapeAsyncTmp(dir) == "", "tape 23f: a worker that throws is applied as refused, writes nothing, and the pending slot is free");
                TapeCheck(warns.Count == 1 && warns[0].Contains("InvalidOperationException") && warns[0].Contains("phase 'start'") && warns[0].Contains("released at start") && warns[0].Contains("held at start"), $"tape 23f: the exception is surfaced with its type, the phase and the held state ({string.Join(" | ", warns)})");
                applied.Clear(); warns.Clear(); rec.Clear(); TapeAsyncLap(rec, w, 5f, 40f);
                saves.Submit(Track(TapeAsyncJob(rec, 5f, gB, dir, real, null))); saves.Join("test");
                TapeCheck(applied.Count == 1 && applied[0].Res.Whole && TapeSame(tp, tapeB), "tape 23f: the next save after the exception runs and saves normally");
                // an apply that throws also leaves the slot free
                var thrower = new TapePairSaves((j, r, t) => throw new InvalidOperationException("apply failed"), s => log.Add(s), s => warns.Add(s));
                rec.Clear(); TapeAsyncLap(rec, w, 3f, 0f);
                thrower.Submit(Track(TapeAsyncJob(rec, 3f, gA, dir, real, null)));
                bool threw = false; try { thrower.Join("test"); } catch (InvalidOperationException) { threw = true; }
                TapeCheck(threw && thrower.Pending == null, "tape 23f: an apply that throws leaves the pending slot free (it cannot be waited for forever)");
                File.Delete(gp); File.Delete(tp);
            }

            // ---- (g) a long wait logs its diagnostic, and still never gives up
            {
                var rec = new WorldTapeRecorder(); var saves = Saves(); applied.Clear(); log.Clear(); saves.DiagAfterMs = 50;
                TapeAsyncLap(rec, w, 3f, 0f);
                var gate = new TapeWorkerGate("compressed") { ReleaseOnMainWait = false };
                var job = Track(TapeAsyncJob(rec, 3f, gA, dir, real, gate)); saves.Submit(job);
                TapeAsyncWaitReached(gate);
                var releaser = new Thread(() => { Thread.Sleep(400); gate.Release(); }) { IsBackground = true }; releaser.Start();
                saves.Join("the title's replay");
                var diag = log.Where(l => l.Contains("WAIT DIAGNOSTIC")).ToList();
                TapeCheck(diag.Count >= 2 && saves.Diagnostics == diag.Count, $"tape 23g: a wait of 400 ms with a 50 ms threshold logged {diag.Count} diagnostics");
                TapeCheck(diag.Count > 0 && diag[0].Contains("at the title's replay") && diag[0].Contains("task Running") && diag[0].Contains("worker phase 'compressed'") && diag[0].Contains("held at compressed") && diag[0].Contains("main thread id " + System.Environment.CurrentManagedThreadId) && diag[0].Contains("TapePairSaves"),
                    $"tape 23g: the diagnostic names the wait site, the task status, the worker phase, the gate, the thread and carries the main thread's stack ({diag.FirstOrDefault()?.Split('\n')[0]})");
                TapeCheck(applied.Count == 1 && applied[0].Res.Whole, "tape 23g: and the wait still ended with the worker's result: nothing was abandoned");
                File.Delete(gp); File.Delete(tp);
            }
        }
        finally
        {
            foreach (var j in toFree) try { j.Gate?.Release(); j.Task?.Wait(20000); } catch { }      // (a mutant can leave a worker held: it is let go and finished before the files go)
            w?.Dispose(); TapeRmDir(dir);
        }
    }
}
