using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace CCCPRacing.Race;

/// <summary>
/// TEST SEAM (replay-tape.md section 17): holds, or faults, the pair-save worker at a named point so a test can do things on the main thread while the worker is in a known place.
/// Points: "start" (after the recording was copied, before the compression), "compressed", and every step name of <see cref="ITapeFileOps"/> ("tmp-written", "verified", "before-replace",
/// "after-replace", "ghost-replaced", "tape-replaced"); a point fires the first time the worker passes it. The production default is no gate at all (a null reference, one test per point).
/// </summary>
public sealed class TapeWorkerGate
{
    public readonly string HoldAt, ThrowAt;
    public bool ReleaseOnMainWait = true;      // the main thread is blocked in a wait for this very job: that is the moment a test releases it (deterministic, no timer)
    readonly ManualResetEventSlim _reached = new(false), _go = new(false);
    readonly ConcurrentQueue<string> _trace = new();
    volatile string _state = "armed";
    int _held, _thrown;

    public TapeWorkerGate(string holdAt, string throwAt = null) { HoldAt = holdAt; ThrowAt = throwAt; }
    public string State => _state;
    public bool Reached => _reached.IsSet;
    public bool Released => _go.IsSet;
    public string[] Trace => _trace.ToArray();
    public bool WaitReached(int ms) => _reached.Wait(ms);
    public void Release() { if (!_go.IsSet) { _trace.Enqueue("released by the test"); _go.Set(); } }

    /// <summary>Called on the worker at each point.</summary>
    internal void At(string point)
    {
        if (point == HoldAt && Interlocked.Exchange(ref _held, 1) == 0)
        {
            _state = "held at " + point; _trace.Enqueue("held at " + point); _reached.Set();
            AutoRelease();      // (the main thread may have been waiting for a while already: it is let in only now, when the worker is really held)
            _go.Wait();
            _state = "released at " + point; _trace.Enqueue("passed " + point);
        }
        if (point == ThrowAt && Interlocked.Exchange(ref _thrown, 1) == 0) { _trace.Enqueue("fault at " + point); throw new InvalidOperationException("test: worker fault at " + point); }
    }

    /// <summary>Called on the main thread when it starts to wait for the job.</summary>
    internal void MainWaiting(string where)
    {
        _trace.Enqueue("main waits at " + where);
        lock (_lk) { _mainWhere = where; _mainSeen = true; }
        AutoRelease();
    }

    // The hold ends when the main thread waits AND the worker is held (in either order): a wait that begins before the worker has reached its point never skips the hold.
    readonly object _lk = new(); bool _mainSeen; string _mainWhere;
    void AutoRelease()
    {
        lock (_lk)
            if (ReleaseOnMainWait && _mainSeen && _reached.IsSet && !_go.IsSet) { _trace.Enqueue("released: main waits at " + _mainWhere); _go.Set(); }
    }
}

/// <summary>The worker's file operations: the given ones, but a warning waits for the main thread to say it (and the gate sees every step).</summary>
public sealed class BufferedOps : ITapeFileOps
{
    readonly ITapeFileOps _inner; readonly TapeWorkerGate _gate;
    public readonly ConcurrentQueue<string> Warnings = new();
    public BufferedOps(ITapeFileOps inner, TapeWorkerGate gate = null) { _inner = inner; _gate = gate; }
    public void Step(string name) { _gate?.At(name); _inner.Step(name); }
    public void Replace(string tmp, string final) => _inner.Replace(tmp, final);
    public void Move(string tmp, string final) => _inner.Move(tmp, final);
    public void Warn(string message) => Warnings.Enqueue(message);
}

/// <summary>
/// One lap's pair save on a worker task, on plain data only (the copied recording, absolute paths, the ghost's bytes and fingerprint): the compression, then
/// <see cref="WorldTapeStorage.SavePairBytes"/>. Created on the main thread, started by <see cref="TapePairSaves.Submit"/>.
/// </summary>
public sealed class TapePairJob
{
    public Task<(TapePairResult Res, WorldTape Tape)> Task { get; private set; }
    public readonly BufferedOps Ops; public readonly TapeWorkerGate Gate;
    public readonly int Set; public readonly float Time; public readonly object Tag;      // Tag: whatever the owner wants back when the result is applied (the scene: the ghost)
    public long WorkerMs, CompressMs, GhostMs, FinishMs, MainMs;
    public volatile string Phase = "created";      // for the wait diagnostics
    readonly WorldTapeStorage _store; readonly WorldTapeRecorder.Recording _rec; readonly string _gp, _tp, _track; byte[] _ghostBytes; readonly Ghost _ghost; readonly ulong _fp;

    /// <summary>ghostBytes: the ghost file's bytes when the caller already made them, or null: then <paramref name="ghost"/> must be given and the WORKER serialises it first (Ghost.ToBytes, managed code only; the finished ghost is immutable, see PhysicsTest.GhostBytesWorker.cs). A failing serialisation is a failed job: nothing is replaced.</summary>
    public TapePairJob(WorldTapeStorage store, WorldTapeRecorder.Recording rec, float time, ulong fingerprint, string trackId, int set, string gp, string tp, byte[] ghostBytes, ITapeFileOps inner, TapeWorkerGate gate = null, object tag = null, Ghost ghost = null)
    {
        if (ghostBytes == null && ghost == null) throw new ArgumentException("a pair save needs the ghost's bytes or the ghost to serialise");
        _ghost = ghost;
        _store = store; _rec = rec; Time = time; _fp = fingerprint; _track = trackId; Set = set; _gp = gp; _tp = tp; _ghostBytes = ghostBytes; Gate = gate; Tag = tag;
        Ops = new BufferedOps(inner, gate);
    }

    internal void Start()
    {
        if (Task != null) throw new InvalidOperationException("the pair save was started twice");
        Phase = "queued";
        Task = System.Threading.Tasks.Task.Run(() =>
        {
            var w = Stopwatch.StartNew(); WorldTape tape = null;
            try
            {
                Phase = "start"; Gate?.At("start");
                if (_ghostBytes == null)
                {
                    Phase = "serialising"; _ghostBytes = _ghost.ToBytes();
                    GhostMs = w.ElapsedMilliseconds; Phase = "serialised"; Gate?.At("serialised");
                }
                Phase = "compressing"; tape = WorldTapeRecorder.Compress(_rec, Time, _fp); tape.TrackId = _track; tape.Set = Set;
                CompressMs = w.ElapsedMilliseconds;
                Phase = "compressed"; Gate?.At("compressed");
                Phase = "saving";
                return (_store.SavePairBytes(_gp, _tp, _ghostBytes, _fp, tape, Ops), tape);
            }
            catch (Exception ex) { Ops.Warn($"world tape: save failed on the worker ({ex.GetType().Name}: {ex.Message}) at phase '{Phase}', gate {(Gate == null ? "none" : Gate.State + " [" + string.Join("; ", Gate.Trace) + "]")}, thread {Environment.CurrentManagedThreadId}"); return (new TapePairResult(false, false), tape); }
            finally { WorkerMs = w.ElapsedMilliseconds; Phase = "done"; }
        });
    }
}

/// <summary>
/// The lifecycle of the pair saves of a scene (and of the tests): at most one at a time, submitted on the main thread; the main thread polls it (<see cref="Poll"/>), applies
/// the result when it is done, and WAITS for it (<see cref="Join"/>) only before the next save, before anything reads the slot and at the exit. Every wait that lasts more than
/// <see cref="DiagAfterMs"/> (and then every further <see cref="DiagAfterMs"/>) logs a diagnostic line; it never gives up on a worker that may still be writing the final paths.
/// </summary>
public class TapePairSaves
{
    public delegate void ApplyFn(TapePairJob job, TapePairResult res, WorldTape tape);
    readonly ApplyFn _apply; readonly Action<string> _log, _warn;
    public int DiagAfterMs = 5000;
    public int Diagnostics;      // how many diagnostic lines were logged
    public TapePairJob Pending { get; protected set; }

    public TapePairSaves(ApplyFn apply, Action<string> log, Action<string> warn) { _apply = apply; _log = log; _warn = warn; }

    /// <summary>Hands over a new job: an earlier one is finished and applied first (two saves never run at once), then the new one starts.</summary>
    public virtual void Submit(TapePairJob job)
    {
        Join("the next record");
        Pending = job; job.Start();
    }

    /// <summary>The job, if it is done: applied.</summary>
    public virtual void Poll()
    {
        var j = Pending;
        if (j == null || !j.Task.IsCompleted) return;
        Complete(j, false, "");
    }

    /// <summary>Waits for the pending job (if any) and applies it.</summary>
    public virtual void Join(string where)
    {
        var j = Pending;
        if (j != null) Complete(j, true, where);
    }

    void Complete(TapePairJob job, bool wait, string where)
    {
        long waited = 0; bool blocked = false;
        if (wait && !job.Task.IsCompleted)
        {
            var sw = Stopwatch.StartNew(); blocked = true;
            job.Gate?.MainWaiting(where);
            int rounds = 0;
            try
            {
                while (!job.Task.Wait(DiagAfterMs))
                {
                    rounds++; Diagnostics++;
                    _log(Diagnose(job, where, sw.ElapsedMilliseconds, rounds));
                }
            }
            catch (AggregateException) { }      // (a faulted task is reported below; the worker catches its own exceptions, so this is the last resort)
            waited = sw.ElapsedMilliseconds;
        }
        Pending = null;      // (cleared before anything can throw: a failing apply never leaves the slot stuck)
        (TapePairResult Res, WorldTape Tape) r;
        if (job.Task.IsFaulted) { _warn($"world tape: the pair save task faulted ({job.Task.Exception?.GetBaseException().Message}), treated as refused"); r = (new TapePairResult(false, false), null); }
        else r = job.Task.Result;
        while (job.Ops.Warnings.TryDequeue(out var w)) _warn(w);
        if (job.Gate != null) _log($"World tape: gate trace [{string.Join("; ", job.Gate.Trace)}]");
        _apply(job, r.Res, r.Tape);
        if (blocked) _log($"World tape: the main thread waited {waited} ms for that pair save at {where}");
    }

    /// <summary>The diagnostic of a long wait: where, how long, the task, the gate and the main thread's own stack.</summary>
    string Diagnose(TapePairJob job, string where, long elapsedMs, int round) =>
        $"World tape: WAIT DIAGNOSTIC #{round}: the main thread has waited {elapsedMs} ms at {where} for the pair save of set {job.Set} lap {job.Time:F2} s; task {job.Task.Status}, worker phase '{job.Phase}', " +
        $"gate {(job.Gate == null ? "none" : job.Gate.State + " [" + string.Join("; ", job.Gate.Trace) + "]")}, main thread id {Environment.CurrentManagedThreadId}; main thread stack:\n{Environment.StackTrace}";
}
