using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using CCCPRacing.Car;
using CCCPRacing.Track;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// The world tape in the game (docs/replay-tape.md, sections 3 and 8): the index built once after the actors are listed, the recorder
/// that runs through every rally and Stakhanov lap (only a record is kept, saved with its ghost as a pair), and the title replay's
/// player that poses the actors the tape covers (their live tick is skipped) and seeks at the start and at every wrap.
/// CCCP_TAPE=off disables recording (read once). Test-only flags: --tape-debug, --tape-quit-laps=N, --tape-lap-speeds=A,B,
/// --tape-replay-quit=S, --tape-fail=tape|ghost, --tape-title-trip=A,B, --tape-title-after-lap=N, --tape-test-part (tests: RaceScene.Tape.Scene).
/// </summary>
public partial class RaceScene
{
    static readonly bool TapeOff = OS.GetEnvironment("CCCP_TAPE") == "off";
    static readonly WorldTapeStorage TapeStore = new(WorldTapeCodec.Encode, WorldTapeCodec.Decode);

    /// <summary>A record set's tape in memory: bound to the ghost object it was made with; Tape null: none (or unusable).</summary>
    sealed class TapeSlot { public Ghost Ghost; public WorldTape Tape; public TapeBody Body; }

    WorldTapeIndex _tapeIndex;                  // built once after ListActors, reused for every lap and replay of this scene
    WorldTapeRecorder _tapeRec;
    readonly List<double> _tapeCostMs = new(2048);      // --tape-debug: ms per recorded sample of the lap
    long _tapeCostAlloc, _tapeSpawnAlloc, _tapeKeyAlloc, _tapeSteadyMax;
    int _tapeSpawnSamples, _tapeSteadyCalls, _tapeSteadyAllocCalls, _tapeKeyCalls, _tapeGcInWindow;
    readonly List<(int Sample, long Bytes)> _tapeAllocWorst = new();
    bool _tapeOn;                               // a lap is being recorded
    readonly TapeSlot[] _tapes = new TapeSlot[RecordStore.Sets];
    string _tapeSessionLapsDir;                 // opt-in trailer session: immutable pair for every completed lap
    TapePairSaves _tapeSessionSavesBox;
    TapePairSaves TapeSessionSaves => _tapeSessionSavesBox ??= new TapePairSaves(
        (job, result, tape) =>
        {
            string folder = (string)job.Tag;
            GD.Print($"tape session: {Path.GetFileName(folder)} {job.Time:F2} s: {result}, {tape?.SampleCount ?? 0} samples at {folder}");
            if (!result.Whole) GD.PushWarning($"Tape session lap is incomplete: {folder}");
            if (IsInstanceValid(_hud) && _hud.IsInsideTree())
                _hud.Show(result.Whole ? "TAKE SAVED" : "TAKE SAVE FAILED",
                    Path.GetFileName(folder).ToUpperInvariant(), 4f,
                    result.Whole ? Hud.Gold : Hud.Red, Prio.Critical);
        }, s => GD.Print(s), s => GD.PushWarning(s));
    readonly Dictionary<WorldActor, string> _tapeKeys = new();
    readonly HashSet<WorldActor> _tapeSkip = new();
    (Transform3D Local, bool Visible)[] _tapeRest = Array.Empty<(Transform3D, bool)>();
    WorldTapePlayer _tapePlayer;
    Ghost _tapePlayerGhost;
    bool _tapePlayerBuilt, _tapePosed;
    TapeSink _tapeSink;
    // test flags and counters
    bool _tapeDebug, _tapeTestPart, _tapeTraceAlloc;      // --tape-trace-alloc: the recorder says which phase and which part allocated (it perturbs the cost a little)
    int _tapeQuitLaps, _tapeLaps, _tapeSeeks, _tapeFail;                 // _tapeFail: 1 tape, 2 ghost
    float[] _tapeLapSpeeds = Array.Empty<float>();
    float _tapeReplayQuit = -1, _tapeTripA = -1, _tapeTripB = -1, _tapeT, _tapeReplayT;
    bool _tapeTripGone, _tapeTripBack, _tapeTripPreview;
    int _tapeReloadAfter, _tapeTitleAfter;      // --tape-title-after-lap=N: after lap N is saved, go to the title in this same scene (no reload)
    bool _tapeTitleNow;
    static bool _tapeReloaded;                  // (static: the reloaded scene reads the same flags again)
    static int _tapeScenes;
    float _tapeTestErr; int _tapeTestTicks;
    readonly Dictionary<string, Vector3> _tapeLast = new();
    readonly Dictionary<string, float> _tapeTravel = new();
    Node3D _tapeTestNode;
    readonly Dictionary<string, int> _tapeLive = new(), _tapeSkipped = new();

    // test flag --tape-count-queries: the car's list of collision circles counts how often CarSim reads it (it reads it only in Step), split into the replay and the rest
    float _tapeQuitAt = -1;      // --tape-quit-at=S: the race quits at lap clock S (a hit that is not a lap: the babushka launch)
    bool _tapeCountQueries; ObstacleQueryCounter _tapeObsCount; long _tapeReplayQueries;
    sealed class ObstacleQueryCounter : IReadOnlyList<Obstacle>
    {
        readonly IReadOnlyList<Obstacle> _l; public long Queries;
        public ObstacleQueryCounter(IReadOnlyList<Obstacle> l) { _l = l; }
        public int Count { get { Queries++; return _l.Count; } }
        public Obstacle this[int i] { get { Queries++; return _l[i]; } }
        public IEnumerator<Obstacle> GetEnumerator() { Queries++; return _l.GetEnumerator(); }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    // The collision circles of the actors the tape poses (and the replay skips): the replay's car is posed by the ghost and never stepped, so nothing collides with a circle in a replay,
    // but the circles stay where the live world left them (a frozen bus, a tractor at its start) and the replay's other readers (the replay camera's view test, the tanks' crush test)
    // would meet them. They are parked off the map while the tape poses their actors and put back as built when the replay is released (the actors' own Reset places the movers).
    int _tapeParked, _tapeParkedLive, _tapeParkSlots;
    void TapeParkCircles(bool on)
    {
        int parked = 0, live = 0, slots = 0;
        foreach (var (who, i, o) in _worldObstacles)
        {
            bool covered = false;
            foreach (var a in _tapeSkip) if (a.Who == who) { covered = true; break; }
            if (!covered) continue;
            slots++;
            if (on)
            {
                if (_track.Obstacles[i].Center.X > -1e3f) live++;
                _track.Obstacles[i] = new Obstacle(new Vector2(-1e4f, -1e4f), 0.1f); parked++;
            }
            else if (_track.Obstacles[i].Center.X < -1e3f) { _track.Obstacles[i] = o; parked++; }
        }
        _tapeParked = parked; _tapeParkedLive = live; _tapeParkSlots = slots;
        if (_tapeDebug) TapeLog($"circles of covered actors {(on ? "parked" : "restored")}: {parked} of {slots} slots{(on ? $" ({live} were on the map)" : "")}");
    }

    static void TapeLog(string s) => GD.Print("tape: " + s);

    /// <summary>The structure the tape depends on, taken once the actors are listed and before any of them has moved.</summary>
    void BuildTapeIndex()
    {
        foreach (string a in InstructionTrack == null ? OS.GetCmdlineUserArgs() : Array.Empty<string>())
        {
            if (a == "--tape-debug") _tapeDebug = true;
            else if (a == "--tape-test-part") _tapeTestPart = true;
            else if (a == "--tape-trace-alloc") _tapeTraceAlloc = true;
            else if (a.StartsWith("--tape-quit-laps=")) _tapeQuitLaps = int.Parse(a["--tape-quit-laps=".Length..]);
            else if (a.StartsWith("--tape-session-laps=")) _tapeSessionLapsDir = a["--tape-session-laps=".Length..];
            else if (a.StartsWith("--tape-lap-speeds=")) _tapeLapSpeeds = Array.ConvertAll(a["--tape-lap-speeds=".Length..].Split(','), s => float.Parse(s, CultureInfo.InvariantCulture));
            else if (a.StartsWith("--tape-replay-quit=")) _tapeReplayQuit = float.Parse(a["--tape-replay-quit=".Length..], CultureInfo.InvariantCulture);
            else if (a.StartsWith("--tape-hold-worker=")) _tapeHoldAt = a["--tape-hold-worker=".Length..];
            else if (a.StartsWith("--tape-worker-throw=")) _tapeThrowAt = a["--tape-worker-throw=".Length..];
            else if (a == "--tape-title-nopoll") _tapeTitleNoPoll = true;
            else if (a.StartsWith("--tape-hold-release-ms=")) _tapeHoldMs = int.Parse(a["--tape-hold-release-ms=".Length..]);
            else if (a.StartsWith("--tape-fail=")) _tapeFail = a.EndsWith("ghost") ? 2 : 1;
            else if (a.StartsWith("--tape-blast=")) _tapeBlast = Array.ConvertAll(a["--tape-blast=".Length..].Split(','), s => float.Parse(s, CultureInfo.InvariantCulture));
            else if (a.StartsWith("--tape-wrap-every=")) _tapeWrapEvery = float.Parse(a["--tape-wrap-every=".Length..], CultureInfo.InvariantCulture);
            else if (a.StartsWith("--tape-replay-start=")) _tapeReplayStart = float.Parse(a["--tape-replay-start=".Length..], CultureInfo.InvariantCulture);
            else if (a.StartsWith("--tape-quit-at=")) _tapeQuitAt = float.Parse(a["--tape-quit-at=".Length..], CultureInfo.InvariantCulture);
            else if (a == "--tape-count-queries") _tapeCountQueries = true;
            else if (a.StartsWith("--tape-seek-probe=")) _tapeProbe = Array.ConvertAll(a["--tape-seek-probe=".Length..].Split(','), x => float.Parse(x, CultureInfo.InvariantCulture));
            else if (a.StartsWith("--tape-pos-every=")) _tapePosEvery = float.Parse(a["--tape-pos-every=".Length..], CultureInfo.InvariantCulture);
            else if (a.StartsWith("--tape-pos-at=")) _tapePosAt = float.Parse(a["--tape-pos-at=".Length..], CultureInfo.InvariantCulture);
            else if (a.StartsWith("--tape-title-after-lap="))_tapeTitleAfter = int.Parse(a["--tape-title-after-lap=".Length..]);
            else if (a.StartsWith("--tape-reload-after-lap=")) _tapeReloadAfter = int.Parse(a["--tape-reload-after-lap=".Length..]);
            else if (a.StartsWith("--tape-title-trip="))      // A,B[,preview]: A s into the replay leave it (a race, or a track preview), B s later come back
            {
                var t = a["--tape-title-trip=".Length..].Split(',');
                _tapeTripA = float.Parse(t[0], CultureInfo.InvariantCulture); _tapeTripB = float.Parse(t[1], CultureInfo.InvariantCulture); _tapeTripPreview = t.Length > 2;
            }
        }
        if (_tapeCountQueries && _car != null) _car.Obstacles = _tapeObsCount = new ObstacleQueryCounter(_track.Obstacles);
        var list = new List<(string, Node3D)>(); var seen = new HashSet<string>();
        foreach (var a in _actors)
        {
            if (a.Node == null || !IsInstanceValid(a.Node)) continue;
            var key = $"{a.Who}:{a.Node.Name}";
            if (!seen.Add(key)) continue;          // (a second actor under one key is never skipped: the tape cannot tell them apart)
            _tapeKeys[a] = key;
            // An actor whose root carries no "root" mark is never covered (it has no placement to record), so it is not indexed either: the
            // liaison officer's figure lives under the KGB actor's tree and is recorded there; a second index entry would only record it twice.
            if (a.Node.HasMeta(WorldTapeConst.PartMeta) && (string)a.Node.GetMeta(WorldTapeConst.PartMeta) == "root") list.Add((key, a.Node));
        }
        if (_tapeTestPart)
            foreach (var (key, root) in list)
                if (root.HasMeta(WorldTapeConst.PartMeta))
                {
                    _tapeTestNode = new Node3D { Name = "TapeTestPart", Scale = new Vector3(1.6f, 1f, 0.5f) };
                    root.AddChild(_tapeTestNode); Tape.Part(_tapeTestNode, "testpart");
                    break;
                }
        _tapeIndex = new WorldTapeIndex(list);
        _tapeRest = new (Transform3D, bool)[_tapeIndex.Count];
        for (int i = 0; i < _tapeIndex.Count; i++) _tapeRest[i] = (_tapeIndex.NodeOf(i).Transform, _tapeIndex.NodeOf(i).Visible);
        _tapeRec = TapeOff ? null : new WorldTapeRecorder();
        _tapeSink = new TapeSink(this);
        _tapeScenes++;
        if (_tapeDebug)
        {
            TapeLog($"scene build #{_tapeScenes}: index {_tapeIndex.Count} parts in {_tapeIndex.ActorKeys.Count()} actors, recording {(_tapeRec != null ? "on" : "off")}");
            var per = new Dictionary<string, int>();
            for (int i = 0; i < _tapeIndex.Count; i++) per[_tapeIndex.IdOf(i).ActorKey] = per.GetValueOrDefault(_tapeIndex.IdOf(i).ActorKey) + 1;
            TapeLog($"index actors [{string.Join(", ", per.Select(kv => $"{kv.Key}={kv.Value}"))}] unmarked [{string.Join(", ", _tapeKeys.Values.Where(k => !per.ContainsKey(k)))}]");
        }
        TapeAdoptInstances();      // (after the report: the instances made while the world was built are not static parts)
        TapeFxInit();
    }

    // ------------------------------------------------------------ recording

    bool TapeEligible => _tapeRec != null && _state == State.Racing && _run is RunMode.Rally or RunMode.Training && !InSchool
        && !_trailerCapture && !_trailerV2 && !_cinematicTour && !_droneTour && !_previewMovie;

    /// <summary>A lap starts (or is thrown away): whatever was recorded goes, and an eligible lap records from now on.</summary>
    void TapeBegin()      // (a void lap restarts the recording and re-arms --tape-blast: its tree falls again in the lap that counts)
    {
        if (_tapeRec == null) return;
        _tapePosAtLap = -1; _tapePosEverySample = -1; _tapeBlasted = false; _tapeRec.Clear(); _tapeOn = false; _tapeCostMs.Clear(); _tapeCostAlloc = _tapeSpawnAlloc = _tapeKeyAlloc = _tapeSteadyMax = 0; _tapeSpawnSamples = _tapeSteadyCalls = _tapeSteadyAllocCalls = _tapeKeyCalls = _tapeGcInWindow = 0; _tapeAllocWorst.Clear();
        if (!TapeEligible) return;
        _tapeRec.Begin(_tapeIndex); _tapeOn = true;
        TapeSpawnAlive();
        TapeFxAlive();
    }

    void TapeRecord()
    {
        if (_tapeOn && _tapeDebug)      // --tape-debug: the cost of every sample actually taken in this lap (the hot path itself stays as it is)
        {
            _tapeRec.Trace = _tapeTraceAlloc;
            int before = _tapeRec.Samples, rs = _tapeRec.Resyncs, gc0 = GC.CollectionCount(0); long a0 = GC.GetAllocatedBytesForCurrentThread(), t0 = Stopwatch.GetTimestamp();
            _tapeRec.Record(_lapClock);
            long t1 = Stopwatch.GetTimestamp(), a1 = GC.GetAllocatedBytesForCurrentThread();
            if (GC.CollectionCount(0) != gc0) _tapeGcInWindow++;
            if (_tapeRec.Samples > before && before > 1)
            {
                _tapeCostMs.Add((t1 - t0) * 1000.0 / Stopwatch.Frequency);
                long d = a1 - a0;
                if (_tapeRec.Resyncs != rs) { _tapeSpawnSamples++; _tapeSpawnAlloc += d; }      // a sample that took an instance in or out of the index: allowed to allocate
                else
                {
                    _tapeCostAlloc += d; _tapeSteadyCalls++;
                    if (d > 0) { _tapeSteadyAllocCalls++; _tapeSteadyMax = Math.Max(_tapeSteadyMax, d); if (_tapeAllocWorst.Count < 6) _tapeAllocWorst.Add((before, d)); }
                    if (before % WorldTapeConst.KeyframeSamples == 0 && before > 0) { _tapeKeyCalls++; _tapeKeyAlloc += d; }      // (the sample that wrote a keyframe)
                }
            }
        }
        else if (_tapeOn) _tapeRec.Record(_lapClock);
        if (_tapeOn && _tapeDebug) { if (_tapePosAt >= 0 || _tapePosEvery > 0) TapePosAt(); TapeTrack(true, _lapClock); }
        if (_tapeTestNode != null) _tapeTestNode.Rotation = new Vector3(0, 0.2f * MathF.Sin(0.7f * _lapClock), 0);      // test: a moving part with a scale
        TapeBlastTick();
    }

    /// <summary>The lap has ended (before the recording is thrown away).</summary>
    void TapeLapReport(float time, bool record, bool fake)
    {
        _tapeSaves.Join("the lap report");      // (the save of the lap before is applied first)
        if (_tapeDebug && _tapes[RecordSet] is { Tape: not null } prev)      // the tape of an earlier record against the actors as they are now
        {
            var p = new WorldTapePlayer(prev.Tape, _tapeIndex, _tapeSink, prev.Body ?? WorldTapePlayer.Validate(prev.Tape));
            TapeLog($"the saved tape covers [{string.Join(", ", _tapeKeys.Values.Where(p.Covers))}] against the actors of scene #{_tapeScenes} before lap {_tapeLaps + 1} ends");
        }
        if (_tapeDebug) TapeLog($"lap {_tapeLaps + 1}: outside the tape: the gulag truck is {(_truck.Active ? "ACTIVE" : "idle")}, the rescue tractor is {(_rescue.Active ? "active" : "idle")}, the mission {(_failed ? "FAILED" : "stands")}");
        if (_tapeDebug) TapeLog($"lap {_tapeLaps + 1} {time:F2} s record {record} fake {fake} recording {_tapeOn}" + (_tapeOn ? $" samples {_tapeRec.Samples} entries {_tapeRec.Entries} unregistered {_tapeRec.UnregisteredSpinners}" : ""));
        if (_tapeDebug && _tapeOn && _tapeCostMs.Count > 0)
        {
            var ms = _tapeCostMs.ToArray(); Array.Sort(ms); double avg = ms.Average();
            TapeLog($"record cost of lap {_tapeLaps + 1}: {ms.Length} samples, avg {avg:F3} ms, p99 {ms[(int)(ms.Length * 0.99)]:F3} ms, max {ms[^1]:F3} ms, allocated {_tapeCostAlloc} bytes in the {_tapeSteadyCalls} samples that kept the index ({_tapeSteadyAllocCalls} of them allocated, at most {_tapeSteadyMax} bytes; {_tapeKeyAlloc} bytes in the {_tapeKeyCalls} keyframe samples; first allocating samples [{string.Join(", ", _tapeAllocWorst.Select(w => $"#{w.Sample}={w.Bytes}"))}]; by phase in the recorder: sound ticks {_tapeRec.AllocSound}, part samples {_tapeRec.AllocSample}, keyframes {_tapeRec.AllocKey}; a gen0 collection ran inside {_tapeGcInWindow} of the windows; parts that allocated [{string.Join("; ", _tapeRec.AllocNotes)}]), {_tapeSpawnAlloc} bytes in {_tapeSpawnSamples} that took an instance in or out");
            TapeLog($"fx of lap {_tapeLaps + 1}: {_tapeFxEvents} events logged by the actors [{TapeFxKinds()}], {_tapeFxCarried} effects carried over from the previous lap and {_tapeFxBrokenAtStart} breakables broken at its start, the hook itself allocated {_tapeFxAlloc} bytes in {_tapeFxCalls} calls, the recorder holds {_tapeRec.EventCount} events");
            TapeLog($"instances of lap {_tapeLaps + 1}: alive at the start {_tapeAtBegin} [{_tapeAtBeginIds}], born {_tapeBornInLap}, gone {_tapeGoneInLap}, alive now {_tapeInst.Count} [{string.Join(",", _tapeInst.Values.OrderBy(i => i.Id).Select(i => i.Kind + "#" + i.Id))}]");
        }
        if (_tapeDebug && _tapeOn)
        {
            var by = _tapeRec.CountersByPart();
            TapeLog($"counters of lap {_tapeLaps + 1}: unregistered spins by part [{string.Join(", ", by.Where(c => c.Unregistered > 0).Select(c => $"{c.Actor}/{c.Part}={c.Unregistered}"))}] unscaled by part [{string.Join(", ", by.Where(c => c.Unscaled > 0).Select(c => $"{c.Actor}/{c.Part}={c.Unscaled}"))}]");
        }
    }

    /// <summary>The lap is a record and not a test: the ghost is saved, with the tape as a pair when one was recorded.</summary>
    void TapeSaveRecord(float time)
    {
        _tapeSaves.Join("the next record");      // (an earlier save is finished and applied first: two saves never run at once)
        int set = RecordSet; var ghost = _best; string gp = _records.GhostForSet(set);
        if (!_tapeOn)
        {
            ghost.Save(gp); _records.WriteSignature();
            _tapes[set] = new TapeSlot { Ghost = ghost };
            return;
        }
        var sw = Stopwatch.StartNew();
        // The finish line, on the main thread: the lap's recording is COPIED out of the recorder (its buffers are the next lap's, which begins at once), the ghost is fingerprinted (its file bytes are made by the worker: the finished ghost is immutable, nothing writes it),
        // and the rest goes to a worker task on PLAIN DATA ONLY: the compression of the tape (Brotli, the slow part of Finish), then the pair save (write both tmp files, read them back and
        // verify, replace the ghost and then the tape). The worker touches no Godot object: it gets the copied recording, absolute paths (globalised here), the ghost file's bytes and
        // fingerprint and a buffered ops object. The main thread polls it every step and applies the slot and the signature when it is done (TapeApplySave), and waits for it only
        // before the next save, before the title's replay reads the slot, and at the scene's exit (TapePairSaves, WorldTapePairJob.cs).
        ulong fingerprint = WorldTapeFingerprint.Of(ghost);
        var rec = _tapeRec.Snapshot();
        string gpAbs = WorldTapeFile.Real(gp), tpAbs = WorldTapeFile.Real(_records.TapeForSet(set));
        long finishMs = sw.ElapsedMilliseconds;      // (the part of Finish that stays here: the copy)
        var job = new TapePairJob(TapeStore, rec, time, fingerprint, _track.Id, set, gpAbs, tpAbs, null, new TapeSceneOps(_tapeFail), TapeTakeGate(), ghost, ghost) { FinishMs = finishMs };
        _tapeSaves.Submit(job);
        job.MainMs = sw.ElapsedMilliseconds;      // (the finish line's cost on the main thread: the copy and the hand-over, Task.Run included; a still-pending earlier save was joined before)
    }

    /// <summary>Save every completed lap of an opt-in trailer session, regardless of record time.</summary>
    void TapeSaveSessionLap(float time, bool ranked = true)
    {
        if (string.IsNullOrEmpty(_tapeSessionLapsDir)) return;
        if (!_tapeOn) { GD.PushWarning("Tape session lap has no world recording; nothing saved"); return; }
        string folder = Path.Combine(_tapeSessionLapsDir, $"lap-{_tapeLaps + 1:D3}");
        if (Directory.Exists(folder)) { GD.PushWarning($"Tape session lap already exists: {folder}"); return; }
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, $"track_{_track.Id}.sig"), _records.Signature + "\n");
        File.WriteAllText(Path.Combine(folder, $"records_{_track.Id}.ver"), RecordStore.Layout + "\n");
        File.WriteAllText(Path.Combine(folder, "session-lap.json"),
            System.Text.Json.JsonSerializer.Serialize(new { seconds = time, ranked }) + "\n");
        var ghost = _rec.CopyWithTime(time);
        var job = new TapePairJob(TapeStore, _tapeRec.Snapshot(), time, WorldTapeFingerprint.Of(ghost),
            _track.Id, RecordSet, Path.Combine(folder, $"ghost_{_track.Id}.bin"),
            Path.Combine(folder, $"tape_{_track.Id}.bin"), ghost.ToBytes(), WorldTapeRealOps.Instance,
            tag: folder);
        TapeSessionSaves.Submit(job);
        GD.Print($"tape session: completed lap {_tapeLaps + 1} in {time:F2} s, saving {folder}");
    }

    TapePairSaves _tapeSavesBox;
    TapePairSaves _tapeSaves => _tapeSavesBox ??= new TapePairSaves(TapeApplySave, s => GD.Print(s), s => GD.PushWarning(s));
    string _tapeHoldAt, _tapeThrowAt; bool _tapeTitleNoPoll; int _tapeHoldMs = -1;      // test flags --tape-hold-worker=POINT, --tape-worker-throw=POINT (the first save only), --tape-hold-release-ms=N (a timer releases the hold instead of the main thread's wait), --tape-title-nopoll
    System.Threading.Timer _tapeGateTimer;
    TapeWorkerGate TapeTakeGate()
    {
        if (_tapeHoldAt == null && _tapeThrowAt == null) return null;
        var g = new TapeWorkerGate(_tapeHoldAt, _tapeThrowAt); _tapeHoldAt = _tapeThrowAt = null;      // (once: the next save runs free)
        if (_tapeHoldMs >= 0) { g.ReleaseOnMainWait = false; _tapeGateTimer = new System.Threading.Timer(_ => g.Release(), null, _tapeHoldMs, System.Threading.Timeout.Infinite); }
        GD.Print($"World tape: test gate on the worker of this save: hold at {g.HoldAt ?? "-"}, fault at {g.ThrowAt ?? "-"}, {(_tapeHoldMs >= 0 ? $"released by a timer after {_tapeHoldMs} ms" : "released when the main thread waits")}");
        return g;
    }

    /// <summary>The result of a pair save, applied on the main thread once the worker is done.</summary>
    void TapeApplySave(TapePairJob job, TapePairResult res, WorldTape tape)
    {
        var ghost = (Ghost)job.Tag;
        if (_tapeDebug && tape != null) TapeJumps(tape);      // (debug scan: not part of the cost reported below)
        // Explicit, never a bool shortcut: the ghost is the record, the tape only decorates it.
        if (res.GhostSaved)
        {
            _records.WriteSignature();
            _tapes[job.Set] = new TapeSlot { Ghost = ghost, Tape = res.TapeSaved ? tape : null };
            if (!res.TapeSaved) GD.Print("World tape: the new ghost is the record, the tape could not be saved (the old tape is dropped by its fingerprint)");
        }
        else
        {
            _tapes[job.Set] = new TapeSlot { Ghost = ghost };
            GD.Print("World tape: the pair save was refused, the old record stays on disk");
        }
        // The finish line's cost is Finish plus the hand-over (main thread); the save itself ran on the worker. A wait is reported on its own line, with where it happened.
        GD.Print($"World tape: set {job.Set} lap {job.Time:F2} s: {res}, {tape?.SampleCount ?? 0} samples, finish {job.FinishMs + job.CompressMs} ms (compression {job.CompressMs} ms on the worker), pair save {job.WorkerMs - job.CompressMs} ms (on the worker), total {job.FinishMs + job.WorkerMs} ms, main thread {job.MainMs} ms");
    }

    /// <summary>Debug: which parts turned more than 25 degrees between two samples (what the recorder counts as an unregistered spinner), and when.</summary>
    static void TapeJumps(WorldTape tape)
    {
        var body = WorldTapeCodec.ReadBody(tape.Payload);
        if (body == null) return;
        var last = new Quaternion?[body.Parts.Count];
        for (int s = 0; s < body.Samples.Count; s++)
            foreach (var (p, st) in body.Samples[s])
            {
                if (last[p] is Quaternion q && !st.Spin && MathF.Abs(q.Dot(st.Rot)) < 0.9763f)
                    TapeLog($"jump {body.Parts[p]} at sample {s} ({s / WorldTapeConst.TapeRate:F2} s): {Mathf.RadToDeg(2 * MathF.Acos(MathF.Min(1, MathF.Abs(q.Dot(st.Rot))))):F0} degrees");
                last[p] = st.Rot;
            }
    }

    /// <summary>After a real lap: the next lap's autopilot speed, the lap limit.</summary>
    void TapeLapDone()
    {
        _tapeLaps++; _tapeBlasted = false;
        if (_tapeReloadAfter > 0 && _tapeLaps >= _tapeReloadAfter && !_tapeReloaded)
        {
            _tapeReloaded = true; TapeLog("reloading the scene");
            GetTree().CallDeferred(SceneTree.MethodName.ReloadCurrentScene);
            return;
        }
        if (_tapeTitleAfter > 0 && _tapeLaps >= _tapeTitleAfter) { _tapeTitleAfter = 0; _tapeTitleNow = true; }
        if (_tapeLaps < _tapeLapSpeeds.Length && _ap != null) _ap.Aggression = _tapeLapSpeeds[_tapeLaps];
        if (_tapeQuitLaps > 0 && _tapeLaps >= _tapeQuitLaps) { TapeSummary("laps done"); GetTree().Quit(); }
    }

    /// <summary>The pair's file operations: the real ones, or (a test) a tape or ghost replacement that fails.</summary>
    sealed class TapeSceneOps : ITapeFileOps
    {
        readonly int _fail;
        public TapeSceneOps(int fail) { _fail = fail; }
        public void Step(string name) { }
        bool Fails(string path) => _fail == 1 ? System.IO.Path.GetFileName(path).StartsWith("tape_") : _fail == 2 && System.IO.Path.GetFileName(path).StartsWith("ghost_");
        public void Replace(string tmp, string final) { if (Fails(final)) throw new System.IO.IOException("test: replacement failed"); WorldTapeRealOps.Instance.Replace(tmp, final); }
        public void Move(string tmp, string final) { if (Fails(final)) throw new System.IO.IOException("test: move failed"); WorldTapeRealOps.Instance.Move(tmp, final); }
        public void Warn(string message) => WorldTapeRealOps.Instance.Warn(message);
    }

    // ------------------------------------------------------------ loading

    /// <summary>The set's tape beside its ghost: absent, foreign or inconsistent (it must be playable, body included) is none.</summary>
    TapeSlot LoadTapeSlot(int set)
    {
        var ghost = _ghosts[set];
        var slot = new TapeSlot { Ghost = ghost };
        if (ghost == null || TapeOff) return slot;
        var tape = TapeStore.Load(_records.TapeForSet(set), ghost);
        if (tape == null) return slot;
        var body = WorldTapePlayer.Validate(tape);
        if (body == null) { GD.Print($"World tape: {_records.TapeForSet(set)} has a corrupt or inconsistent body, ignored"); return slot; }
        slot.Tape = tape; slot.Body = body;
        return slot;
    }

    // ------------------------------------------------------------ the title replay

    /// <summary>The player for the ghost the title replays (null: replay live exactly as before).</summary>
    WorldTapePlayer TapePlayer()
    {
        _tapeSaves.Join("the title's replay");      // (a menu transition: the replay must see the finished slot)
        var ghost = TitleGhost;
        if (_tapePlayerBuilt && _tapePlayerGhost == ghost) return _tapePlayer;
        // Switching car recordings also resets presentation when no world tape exists.
        ClearCarFeedback();
        TapeRelease();
        _tapePlayer = null; _tapeSkip.Clear(); _tapeDriven.Clear(); _tapePlayerGhost = ghost; _tapePlayerBuilt = true; _tapeBreakables = false; _tapeWood = false; _tapeFx.WoodReplay = false;
        if (ghost == null || BundledTitleReplay || _tapeIndex == null) return null;
        var slot = _tapes[TitleSet];
        if (slot == null || slot.Ghost != ghost) return null;
        if (slot.Tape == null) { if (_tapeDebug) TapeLog($"replay of set {TitleSet} runs live: no usable tape"); return null; }
        // A tape saved in this session has no validated body yet (LoadTapeSlot fills it for the loaded ones): validate on first use, once.
        if (slot.Body == null && (slot.Body = WorldTapePlayer.Validate(slot.Tape)) == null) { slot.Tape = null; GD.Print($"World tape: the tape of set {TitleSet} has a corrupt or inconsistent body, ignored"); return null; }
        _tapePlayer = new WorldTapePlayer(slot.Tape, _tapeIndex, _tapeSink, slot.Body);
        _tapeWood = _tapePlayer.Covers("None:WoodFences");
        _tapeFx.WoodReplay = _tapeWood;
        _tapeBreakables = slot.Body.Events.Any(e => e.Kind == TapeFxKind.Marker);      // a tape that records the breakables: the replay does not guess them from the car
        _tapeFx.SetBreakInfo(slot.Body.Events);
        var covered = new List<string>(); var live = new List<string>();
        foreach (var (a, key) in _tapeKeys)
            if (_tapePlayer.Covers(key)) { if (a.TapeDrive != null) _tapeDriven.Add(a); else _tapeSkip.Add(a); covered.Add(key); } else live.Add(key);
        _tapeEmit.Clear();      // the covered vehicles' dust and spray are made by the replay's emitters, from the poses
        foreach (var (a, key) in _tapeKeys)
        {
            if (!_tapePlayer.Covers(key)) continue;
            if (a.Node == _tanksAll) foreach (var h in _tanksAll.TapeHulls()) _tapeEmit.Add(h, TapeEmitters.Kind.Tank);
            else if (a.Node == _btrAll) foreach (var h in _btrAll.TapeHulls()) _tapeEmit.Add(h, TapeEmitters.Kind.Btr);
            else if (a.Node == _ploughsAll) foreach (var h in _ploughsAll.TapePloughs()) _tapeEmit.Add(h, TapeEmitters.Kind.Plough);
        }
        TapeLog($"replay of set {TitleSet} from tape: covers [{string.Join(", ", covered)}], live [{string.Join(", ", live)}]");
        if (!_tapePlayer.Covers("Kozlik:Kozlik") && slot.Tape.ActorStructure.TryGetValue("Kozlik:Kozlik",out var oldKozlik))
            GD.Print($"Kozlik tape: recorded model version {oldKozlik.Version} is incompatible with current model version {_tapeIndex.StructureOf("Kozlik:Kozlik").Version}; patrol runs live at its current size. Source: {TapeSourceText()}.");
        if (!_tapePlayer.Covers("None:PlayerPower"))
            GD.Print("Octane tape: player boost/power state is absent or incompatible; no missing booster is invented.");
        if (_fuelOnTrack && !_tapePlayer.Covers("Depot:FuelDepot"))
            GD.Print("Fuel tape: depot state is absent or incompatible; recorded pouring sound/stream cannot be restored from this tape.");
        if (!_tapePlayer.Covers("None:RescueTowRope"))
            GD.Print("Tow rope tape: endpoints are absent or incompatible; no rope is invented for this recording. Compatible tractor motion still replays.");
        return _tapePlayer;
    }

    string TapeFxState() => $"nodes {_tapeFx.Nodes} particles {_tapeFx.Particles} projectiles {_tapeFx.Projectiles} pieces {_tapeFx.PiecesAlive} broken {_scenery.BrokenCount} lying {_scenery.LyingCount}";

    // test flag --tape-seek-probe=A,B,C: when the replay (playing, not seeking) reaches each clock, after the step's scenery tick, it seeks to that same clock and ticks the scenery again
    // (what a wrap does), and logs the felled trees' model poses before and after: a seek must show what playing shows.
    float[] _tapeProbe; int _tapeProbeAt;
    void TapeSeekProbe(float dt)
    {
        if (_tapeProbe == null || _tapePlayer == null || !_tapePosed || _tapeProbeAt >= _tapeProbe.Length || _replayClock < _tapeProbe[_tapeProbeAt]) return;
        int k = _tapeProbeAt++;
        var played = _scenery.FallenPoses(); float clock = _replayClock;
        _tapePlayer.Seek(clock); TapeRestoreDerivedParts(); _tapeFx.Update(clock); _scenery.Tick(dt, _car, false);
        var seeked = _scenery.FallenPoses();
        float worst = played.Count == seeked.Count ? 0f : 99f; var parts = new List<string>();
        for (int i = 0; i < Math.Min(played.Count, seeked.Count); i++)
        {
            var a = played[i].Pose; var b = seeked[i].Pose;
            float d = a.Origin.DistanceTo(b.Origin);
            foreach (var (x, y) in new[] { (a.Basis.X, b.Basis.X), (a.Basis.Y, b.Basis.Y), (a.Basis.Z, b.Basis.Z) }) d = MathF.Max(d, (x - y).Length());
            worst = MathF.Max(worst, d);
            parts.Add(FormattableString.Invariant($"{played[i].Id}: playing {played[i].Angle:F3} rad, seek {seeked[i].Angle:F3} rad (age {_scenery.BreakAge(played[i].Id):F3} s)"));
        }
        TapeLog(FormattableString.Invariant($"seek probe #{k + 1} at {clock:F3}: fallen {played.Count}/{seeked.Count} worst model difference {worst:F5} [{string.Join("; ", parts)}]"));
    }

    void TapeSeek(float clock)
    {
        ClearCarFeedback(); // no one-shot tail from the old tape time
        foreach (var a in _tapeDriven) a.TapeDrive(true);      // the actors whose models the tape shows make none of their own (idempotent)
        if (!_tapePosed) TapeParkCircles(true);
        _tapeEmit?.Restart();
        _tapePlayer.Seek(clock); TapeRestoreDerivedParts(); _tapeFx.Update(clock); RestoreCollisionFeedback(clock);
        TitleGhost?.ApplyDamage(clock, _carVis.Damage, _fx); _tapePosed = true; _tapeSeeks++;
        if (_tapeDebug) TapeLog($"seek #{_tapeSeeks} at {clock:F2} poses {TapePoseHash():x16} instances [{string.Join(",", _tapePlayer.LiveInstances.OrderBy(kv => kv.Key).Select(kv => FormattableString.Invariant($"{kv.Key}@{kv.Value:F2}") + (_tapeSink.Live.TryGetValue(kv.Key, out var nd) ? "=" + TapeXyz(nd.Position) : "")))}] nodes {TapeKindCounts()}");
        if (_tapeDebug) TapeLog($"seek fx #{_tapeSeeks} at {clock:F2}: {TapeFxState()}");
    }

    /// <summary>The title (re)starts its replay at the start position.</summary>
    void TapeReplayEnter()
    {
        if (TapePlayer() != null) TapeSeek(_replayClock);
        else
        {
            RestoreCollisionFeedback(_replayClock);
            TitleGhost?.ApplyDamage(_replayClock, _carVis.Damage, _fx);
        }
        if (TitleGhost == null) GD.Print($"Car tape: nothing to replay ({TapeSourceText()}); no damage state to restore.");
        else if (!TitleGhost.HasDamage)
            GD.Print($"Car tape: legacy recording has no original damage state; collision-based approximation only. Source: {TapeSourceText()}.");
    }

    /// <summary>
    /// What is being replayed, for the diagnostics (never an invented archive id): a viewer's lap by its archive id and meta; the shipped title demonstration by its asset; a personal record by its ghost file.
    /// </summary>
    string TapeSourceText()
    {
        float dur = TitleGhost?.Duration ?? 0f;
        string duration = TitleGhost == null ? "no ghost" : $"{dur:F2} s, {TitleGhost.Count} samples";
        if (ViewerMode)
        {
            if (ViewerSource?.Lap is { } lap)
            {
                var m = lap.Meta;
                return $"viewer lap archive id {m.Id}, {m.TrackId} set {m.Set} {m.Mode} {m.Kind} lap {m.LapSeconds:F2} s recorded {m.RecordedUtc:O}, ghost {duration}, tape {(lap.HasTape ? "present" : "absent")}";
            }
            return $"viewer without a lap source, {_track?.Id}, ghost {duration}";
        }
        if (BundledTitleReplay)
            return $"bundled title replay res://assets/replays/ilyich-stakhanov-petrol.bin, track {_track?.Id}, set {TitleSet}, ghost {duration}";
        string path = _records == null ? "?" : _records.GhostForSet(TitleSet);
        return $"title replay of the personal record file {path}, track {_track?.Id}, set {TitleSet}, ghost {duration}";
    }

    void RestoreCollisionFeedback(float clock)
    {
        if (TitleGhost?.CollisionBefore(clock) is not Ghost.Collision hit) return;
        float age = clock - hit.Time;
        if (_tapeDebug) TapeLog($"collision tail at {clock:F3}: hit {hit.Time:F3}, age {age:F3}, material {hit.Material}, broke {hit.Broke}");
        _snd.RestoreCollision(hit.Speed, _params.Mass, hit.Material, hit.Broke, age);
        _cam.RestoreCollision(hit.Speed, _params.Mass, age, TitleGhost.Sample(hit.Time).Origin);
        _collisionQuiet = Math.Max(0, CollisionResponse.Of(hit.Speed, _params.Mass).Duration + .15f - age);
    }

    void TapeReplayTick(float dt, bool wrapped)
    {
        var p = TapePlayer();
        if (p == null) { if (wrapped) RestoreCollisionFeedback(_replayClock); return; }
        if (wrapped || !_tapePosed) TapeSeek(_replayClock);
        else { p.Apply(_replayClock, dt); TapeRestoreDerivedParts(); _tapeFx.Update(_replayClock); }
        if (_tapePosed) _tapeEmit?.Tick(dt);
        if (_tapeDebug && _tapePosed) TapeTrack(false, _replayClock);
        if (_tapeDebug && _tapePosed && (_scenery.BrokenCount != _tapeBrokenSeen || _scenery.LyingCount != _tapeLyingSeen)) { _tapeBrokenSeen = _scenery.BrokenCount; _tapeLyingSeen = _scenery.LyingCount; TapeLog($"replay broken {_tapeBrokenSeen} lying {_tapeLyingSeen} at {_replayClock:F2}"); }
        if (_tapeDebug)
            foreach (var (a, key) in _tapeKeys)
            {
                Vector3 pos = a.Node.GlobalPosition; int nc = 0; foreach (var ch in a.Node.GetChildren()) if (ch is Node3D c3 && !c3.TopLevel) { pos += c3.GlobalPosition; nc++; }      // (the root of several actors stays put; its children move)
                pos /= nc + 1;
                if (_tapeLast.TryGetValue(key, out var l)) _tapeTravel[key] = _tapeTravel.GetValueOrDefault(key) + (pos - l).Length();
                _tapeLast[key] = pos;
            }
        if (_tapeTestNode != null && _tapeDebug && _tapePosed && _replayClock < TitleGhost.Duration - 0.1f)       // (the tape's last sample is up to a sample before the ghost's end)
        {
            // The scaled part must come back as R * S (a node's own composition), not S * R: compare the whole basis with the one it had when recorded.
            var want = Basis.FromEuler(new Vector3(0, 0.2f * MathF.Sin(0.7f * _replayClock), 0)) * Basis.FromScale(new Vector3(1.6f, 1f, 0.5f)); var got = _tapeTestNode.Basis;
            foreach (var (g, w) in new[] { (got.X, want.X), (got.Y, want.Y), (got.Z, want.Z) }) _tapeTestErr = MathF.Max(_tapeTestErr, MathF.Max(MathF.Abs(g.X - w.X), MathF.Max(MathF.Abs(g.Y - w.Y), MathF.Abs(g.Z - w.Z))));
            _tapeTestTicks++;
        }
    }

    void TapeRestoreDerivedParts()
    {
        if (_tapePlayer?.Covers("Drunks:DrunkMen") == true) _drunksAll?.RestoreMechanicTapeTool();
        if (_tapePlayer?.Covers("None:RescueTowRope") == true) _rescue.RestoreTapeRope();
    }

    /// <summary>Whether the replay skips this actor's live tick (the tape poses it).</summary>
    bool TapeSkips(WorldActor a, bool replay)
    {
        if (_tapeDebug && _tapeKeys.TryGetValue(a, out var key))
        {
            var d = replay && (_tapeSkip.Contains(a) || _tapeDriven.Contains(a)) ? _tapeSkipped : _tapeLive;      // (a driven actor's aircraft are skipped; its crew and battery tick)
            d[key] = d.GetValueOrDefault(key) + 1;
        }
        return replay && _tapeSkip.Count > 0 && _tapeSkip.Contains(a);
    }

    /// <summary>The replay is over (a run starts, a preview opens, another ghost): the covered parts go back to the rest the world was built in.</summary>
    void TapeRelease()
    {
        if (_tapeSequenceGoldenSaved is bool golden)
        { _carVis.Golden = golden; _tapeSequenceGoldenSaved = null; }

        _carVis?.Damage?.ReleaseTape(_fx);
        if (!_tapePosed) return;
        ClearCarFeedback();
        _tapePosed = false; _tapeBrokenSeen = _tapeLyingSeen = 0;
        if (_tapePlayer == null) return;
        TapeParkCircles(false);
        _tapePlayer.Release();
        if (_tapePlayer.Covers("Drunks:DrunkMen")) _drunksAll?.ClearMechanicTapeTool();
        _tapeFx.WoodReplay = _tapeWood = false;      // the replay's own instances and effects go (their parts leave the index)
        foreach (var a in _tapeDriven) a.TapeDrive(false);
        for (int i = 0; i < _tapeRest.Length; i++)      // (the static parts come first in the index; instances are appended)
        {
            var id = _tapeIndex.IdOf(i);
            if (!_tapePlayer.Covers(id.ActorKey)) continue;
            var node = _tapeIndex.NodeOf(i);
            if (!IsInstanceValid(node)) continue;
            if (id.Part != "root") node.Transform = _tapeRest[i].Local;      // (the actors' own Reset places the roots)
            node.Visible = _tapeRest[i].Visible;
        }
        HideDepotWithoutFuel();      // (the depot was built in view: its rest is visible, and on a track without fuel it must stay out of the world)
        if (_tapeDebug) TapeLog($"released: covered parts back at rest; replay instances {_tapeSink.Spawned} made, {_tapeSink.Despawned} removed, {_tapeSink.Live.Count} left, nodes {TapeKindCounts()}");
        if (_tapeDebug) TapeLog($"released fx: {TapeFxState()}");
    }

    /// <summary>The scene leaves the tree (a reload, a track change, the quit): whatever the replay made goes first.</summary>
    void TapeExit()
    {
        _tapeSaves.Join("the scene's exit");      // (a save in flight is finished before the scene goes: the quit, a reload and a track change never cut it)
        _tapeSessionSavesBox?.Join("the scene's exit");
        LapArchiveExit();
        if (_tapePlayer == null || !_tapePosed) { _tapeFx?.Free(); return; }
        _tapePosed = false; TapeParkCircles(false); _tapePlayer.Release();
        _powerTape?.SetTapeDriven(false);
        if (_tapeDebug) TapeLog($"scene exit: replay instances {_tapeSink.Spawned} made, {_tapeSink.Despawned} removed, {_tapeSink.Live.Count} left");
        if (_tapeDebug) TapeLog($"scene exit fx: {TapeFxState()}");
        _tapeFx?.Free();
    }

    ulong TapePoseHash()
    {
        ulong h = 1469598103934665603UL;
        void Mix(float v) { h = (h ^ (uint)(int)MathF.Round(v * 1000f)) * 1099511628211UL; }
        for (int i = 0; i < _tapeIndex.Count; i++)
        {
            if (_tapePlayer == null || !_tapePlayer.Covers(_tapeIndex.IdOf(i).ActorKey)) continue;
            var n = _tapeIndex.NodeOf(i); var t = n.Transform;
            Mix(t.Origin.X); Mix(t.Origin.Y); Mix(t.Origin.Z); Mix(t.Basis.X.X); Mix(t.Basis.X.Z); Mix(t.Basis.Z.X); Mix(t.Basis.Z.Z); h = (h ^ (n.Visible ? 7u : 3u)) * 1099511628211UL;
        }
        return h;
    }

    /// <summary>How many covered non-root parts are not at the rest they were built in (after a run starts: 0).</summary>
    int TapeOffRest()
    {
        int off = 0;
        if (_tapePlayer == null) return 0;
        for (int i = 0; i < _tapeRest.Length; i++)
        {
            var id = _tapeIndex.IdOf(i);
            if (!_tapePlayer.Covers(id.ActorKey)) continue;
            var n = _tapeIndex.NodeOf(i);
            if (n.Visible != _tapeRest[i].Visible || (id.Part != "root" && !n.Transform.IsEqualApprox(_tapeRest[i].Local))) { off++; _tapeOffBy[id.ActorKey] = _tapeOffBy.GetValueOrDefault(id.ActorKey) + 1; _tapeOffParts.Add(id.ToString()); }
        }
        return off;
    }
    readonly Dictionary<string, int> _tapeOffBy = new();      // (debug) TapeOffRest by actor, and the parts
    readonly List<string> _tapeOffParts = new();

    void TapeSummary(string why)
    {
        string Fmt(Dictionary<string, int> d) { var l = new List<string>(); foreach (var (k, v) in d) l.Add($"{k}={v}"); l.Sort(); return string.Join(",", l); }
        string Travel() { var l = new List<string>(); foreach (var (k, v) in _tapeTravel) l.Add($"{k}={v:F0}"); l.Sort(); return string.Join(",", l); }
        string Roots() { var l = new List<string>(); foreach (var (a, k) in _tapeKeys) if (_tapePlayer != null && _tapePlayer.Covers(k)) l.Add($"{k}={a.Node.Visible}"); return string.Join(",", l); }
        if (_tapeTestNode != null) TapeLog($"testpart basis error {_tapeTestErr:F4} over {_tapeTestTicks} ticks, scale ({_tapeTestNode.Basis.Scale.X:F2}, {_tapeTestNode.Basis.Scale.Y:F2}, {_tapeTestNode.Basis.Scale.Z:F2})");
        TapeLog($"replay instances ({why}): made {_tapeSink.Spawned} removed {_tapeSink.Despawned} live {_tapeSink.Live.Count} most {_tapeSink.MostAtOnce} in the tree [{TapeKindCounts()}] live actors' own [{_tapeInst.Count}] hind crew ticks {_hindsAll?.CrewTicks ?? 0}");
        if (_tapeObsCount != null) TapeLog($"car obstacle queries ({why}): in the replay {_tapeReplayQueries}, everywhere {_tapeObsCount.Queries}");
        TapeLog($"travel [{Travel()}] covered roots visible [{Roots()}]");
        if (_tapeEmit != null) TapeLog($"emitters ({why}): {_tapeEmit.Count} vehicles made [{string.Join(",", _tapeEmit.Made.Select(kv => kv.Key + "=" + kv.Value))}]");
        TapeLog($"fx ({why}): made [{TapeFxMade()}] projectiles flown {_tapeFx.ProjectilesFlown}, {TapeFxState()}");
        TapeLog($"summary ({why}) laps {_tapeLaps} seeks {_tapeSeeks} posed {_tapePosed} player {(_tapePlayer != null)} skipped [{Fmt(_tapeSkipped)}] live [{Fmt(_tapeLive)}]");
    }

    /// <summary>Test flags that need the sim clock: the replay's length, a trip from the title into a race and back.</summary>
    void TapeTestTick(float dt)
    {
        _tapeWorld += dt;
        _tapeSaves.Poll();      // the pair save of the lap that just ended, once the worker is done
        _tapeSessionSavesBox?.Poll();
        LapArchiveTick();
        ViewerSpikeTick(dt);
        ViewerFreezeTick(dt);
        ViewerTestTick(dt);
        ViewerLifecycleTick(dt);
        ViewerFidelityTick(dt);
        if (_tapeQuitAt >= 0 && _state == State.Racing && _lapClock >= _tapeQuitAt) { _tapeQuitAt = -1; TapeLog($"quit at lap clock {_lapClock:F2}"); TapeSummary("quit"); GetTree().Quit(); return; }
        if (_tapeTitleNow && _tapeSaves.Pending != null && !_tapeTitleNoPoll) { }      // test: the title waits for the worker (it polls, so the finish line does not wait)
        else if (_tapeTitleNow)      // test: the lap is saved (its pair is in _tapes); leave the race for the title without reloading the scene
        {
            _tapeTitleNow = false; _tapeLive.Clear(); _tapeSkipped.Clear(); EnterTitle();      // (the tick counters count the title replay only)
            TapeLog($"title entered in the same scene #{_tapeScenes} after lap {_tapeLaps}: replaying {Replaying}, ghost set {TitleSet}, saved tape in memory {_tapes[TitleSet]?.Tape != null}");
        }
        if (_tapePosed && !Replaying) TapeRelease();       // any way out of the replay (a preview, a board, a run) gives the parts back
        if (_tapeReplayQuit < 0 && _tapeTripA < 0) return;
        _tapeT += dt;
        if (Replaying) _tapeReplayT += dt;
        if (_tapeReplayQuit >= 0 && _tapeReplayT >= _tapeReplayQuit) { TapeSummary("replay done"); GetTree().Quit(); return; }
        if (_tapeTripA >= 0 && !_tapeTripGone && _tapeReplayT >= _tapeTripA)
        {
            _tapeTripGone = true; TapeSummary("before the trip");
            if (_tapeTripPreview)      // an exit that does not go through PlaceAtStart: the track preview opens over the title
            {
                OpenMenu(MenuItem.Single, MenuItem.Race); OpenTrackCard(MenuItem.Race);
                _cardSel = Array.FindIndex(Tracks, t => t.id == _trackId);
                _tourOpen = _previewFocus = _previewFull = true;
                TapeLog($"trip: preview opened, replaying {Replaying}");
            }
            else
            {
                PrepareRun(MenuItem.Race);
                { _tapeOffBy.Clear(); _tapeOffParts.Clear(); int offRest = TapeOffRest(); TapeLog($"trip: race started, state {_state}, covered parts off rest {offRest} by actor [{string.Join(", ", _tapeOffBy.Select(kv => $"{kv.Key}={kv.Value}"))}] parts [{string.Join(", ", _tapeOffParts.Take(60))}], posed {_tapePosed}"); }
            }
        }
        else if (_tapeTripGone && !_tapeTripBack && _tapeT >= _tapeTripB && _tapeReplayT >= _tapeTripA)
        {
            _tapeTripBack = true; TapeSummary("end of the race part");
            if (_tapeTripPreview) { CloseTour(); StopTrackPreview(); _card = TitleCard.None; }
            EnterTitle();
            TapeLog($"trip: back on the title, replaying {Replaying}");
        }
    }
}
