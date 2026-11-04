using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CCCPRacing.Race;
using IOAccess = System.IO.FileAccess;

namespace CCCPRacing.Replay;

/// <summary>
/// The player lap archive (docs/player-replay-design.md): the recent laps in memory (temporary) and the explicitly retained laps on disk.
/// Plain data only, no Godot node. One worker, one job at a time; the main thread only hands over a <see cref="LapCapture"/>.
/// </summary>
public sealed partial class LapArchive
{
    internal sealed class Entry
    {
        public long Seq;                                   // admission order across all archives (the process-wide cap evicts the lowest first)
        public bool Making;                                // the tape / ghost bytes are still to be made (queued, or in flight)
        public LapMeta Meta;
        public byte[] Ghost, Tape; public Ghost GhostSrc;                         // memory entries; the tape appears when the worker (or Open) has compressed the recording
        public WorldTapeRecorder.Recording Rec;            // until compressed
        public LapEntryState State; public string Message = "", TapeWarn;
        public bool Retained, RetainBusy, Evicted, Deleting;
        public bool EditsBusy, Pinned, HasTrim, HasKeys; public LapEditsState EditsState;      // retained laps: the edits sidecar's list markers and its one-save-at-a-time flag (LapArchive.Edits.cs)
        public string Dir;                                 // set for entries that live on disk
        public long Size;
        public int Claim; public readonly ManualResetEventSlim Done = new(false);      // who compresses (worker or Open), and when it is finished
        public string Id => Meta.Id;
    }

    readonly string _dir;
    readonly ITapeFileOps _ops;
    readonly int _maxRecent, _maxPendingRetains; readonly long _maxRecentBytes, _maxLapBytes, _maxPendingRetainBytes;
    LapArchiveGroup _group;
    long _inFlight;                                        // bytes of the payload(s) being compressed right now (worker, or an Open doing it itself); counted once, whether or not their entry is still alive
    static long _seq;
    /// <summary>Test seam: when set, every evicted entry is appended (it owns no payload any more), so a test can inspect the fields.</summary>
    internal List<Entry> EvictedTrace;
    readonly object _lock = new();
    readonly List<Entry> _recent = new();                  // not retained, oldest first
    Dictionary<string, Entry> _disk = new();               // retained and scanned entries by id
    int _scanState;                                        // 0 never, 1 queued, 2 done
    readonly object _queueLock = new(); Task _tail = Task.CompletedTask;
    readonly ConcurrentQueue<string> _messages = new();
    static readonly ConcurrentDictionary<string, LapArchive> Shared = new();
    static readonly Random Rng = new();

    /// <summary>Test seam: called on the worker at the start of a compression ("compress:id") and of a retain ("retain:id"); may block or throw.</summary>
    public Action<string> WorkerHook;

    public string RetainedDir => _dir;

    public LapArchive(string retainedDir, ITapeFileOps ops = null, int maxRecent = LapArchiveLimits.MaxRecent, long maxRecentBytes = LapArchiveLimits.MaxRecentBytes, long maxLapBytes = LapArchiveLimits.MaxLapBytes,
        int maxPendingRetains = LapArchiveLimits.MaxPendingRetains, long maxPendingRetainBytes = LapArchiveLimits.MaxPendingRetainBytes, LapArchiveGroup group = null)
    {
        _maxPendingRetains = Math.Max(1, maxPendingRetains); _maxPendingRetainBytes = maxPendingRetainBytes;
        _dir = Path.GetFullPath(retainedDir);
        _ops = ops ?? WorldTapeRealOps.Instance;
        _maxRecent = Math.Max(1, maxRecent); _maxRecentBytes = maxRecentBytes; _maxLapBytes = maxLapBytes;
        group?.Join(this);
    }

    /// <summary>The process-lifetime instance for a directory (so the recent laps survive a scene reload).</summary>
    public static LapArchive For(string retainedDir)
    {
        var a = Shared.GetOrAdd(Path.GetFullPath(retainedDir), d => new LapArchive(d));
        Process.Join(a);
        return a;
    }

    /// <summary>All shared archives (one per profile folder) together: capped at <see cref="LapArchiveLimits.MaxProcessBytes"/> of recent laps.</summary>
    public static readonly LapArchiveGroup Process = new(LapArchiveLimits.MaxProcessBytes);

    internal void SetGroup(LapArchiveGroup g) { lock (_lock) _group = g; }

    void Say(string line) { _messages.Enqueue(line); while (_messages.Count > 200) _messages.TryDequeue(out _); }

    /// <summary>The visible log lines produced since the last call (accepted, refused, evicted, failed ...). Each is returned once.</summary>
    public IEnumerable<string> DrainMessages() { var l = new List<string>(); while (_messages.TryDequeue(out var s)) l.Add(s); return l; }

    // ------------------------------------------------------------------ worker

    void Enqueue(Action work)
    {
        lock (_queueLock)
            _tail = _tail.ContinueWith(_ =>
            {
                try { work(); } catch (Exception ex) { Say($"LAP ARCHIVE: worker step failed ({ex.GetType().Name}: {ex.Message})"); }
            }, TaskScheduler.Default);
    }

    /// <summary>Waits for the queued worker steps; false when they did not finish in time (a message says so).</summary>
    public bool Flush(TimeSpan timeout)
    {
        var end = DateTime.UtcNow + timeout;
        while (true)
        {
            Task t; lock (_queueLock) t = _tail;
            var left = end - DateTime.UtcNow;
            if (left < TimeSpan.Zero) left = TimeSpan.Zero;
            if (!t.Wait(left)) { Say($"LAP ARCHIVE: still saving after {timeout.TotalSeconds:F0} s, not waiting any longer"); return false; }
            lock (_queueLock) if (ReferenceEquals(t, _tail)) return true;
        }
    }

    // ------------------------------------------------------------------ submit

    /// <summary>What the lap costs in memory before its tape is compressed: ghost bytes plus the raw recording (an estimate for the event list).</summary>
    internal static long CaptureBytes(LapCapture c) => (c.GhostBytes?.Length ?? EstimateGhost(c.OwnedGhost)) + RecordingBytes(c.Recording);

    /// <summary>A deferred ghost's size before ToBytes has run: a deliberately high 1000 B per sample (measured V4 laps are 520-780).</summary>
    static long EstimateGhost(Ghost g) => g == null ? 0 : (long)g.Count * (g.HasDamage ? 1000 : 150);

    static long RecordingBytes(WorldTapeRecorder.Recording r)
        => r == null ? 0 : (long)r.Samples.N + r.Snd.N + r.Keys.N + (long)r.Events.Count * 64 + (long)r.KSample.Length * 12 + 4096;

    public LapSubmitResult Submit(LapCapture c)
    {
        try
        {
            if (c == null || (c.GhostBytes == null && (c.OwnedGhost == null || c.OwnedGhost.Count == 0))) return Refuse(null, "no lap data");
            if (Interlocked.Exchange(ref c.Used, 1) != 0) return Refuse(null, "this capture was already submitted");
            string label = $"{c.TrackId} {c.LapSeconds:F1} s";
            bool deferred = c.GhostBytes == null;
            if (!deferred)
            {
                var fp = LapGhostBytes.Fingerprint(c.GhostBytes, out _, out _);
                if (fp == null) return Refuse(label, "the lap's ghost data is not a complete ghost file");
                if (fp.Value != c.GhostFingerprint) return Refuse(label, "the ghost fingerprint does not match the ghost data");
            }
            long raw = CaptureBytes(c);
            if (raw > _maxLapBytes) return Refuse(label, $"lap too large to keep ({raw / 1048576.0:F1} MB, the limit is {_maxLapBytes / 1048576.0:F1} MB per lap)");

            Entry e;
            lock (_lock)
            {
                string id;
                do id = $"lap-{c.RecordedUtc:yyyyMMddHHmmss}-{Clean(c.TrackId)}-s{c.Set}-{Rng.Next(0x10000):x4}"; while (Find(id) != null);
                e = new Entry
                {
                    Seq = Interlocked.Increment(ref _seq), Making = c.Recording != null || deferred,
                    Ghost = c.GhostBytes, GhostSrc = deferred ? c.OwnedGhost : null, Rec = c.Recording, State = c.Recording != null || deferred ? LapEntryState.Pending : LapEntryState.Ready, Size = raw,
                    Meta = new LapMeta
                    {
                        Id = id, TrackId = c.TrackId, Set = c.Set, Mode = c.Mode, LapSeconds = c.LapSeconds, Kind = c.Kind, Ranked = c.Ranked, RecordedUtc = c.RecordedUtc,
                        DayClock = c.DayClock, HasTape = c.Recording != null, GhostFingerprint = c.GhostFingerprint, Profile = c.Profile, TrackSignature = c.TrackSignature,
                        Layout = c.Layout, TotalBytes = raw, Fuels = c.Fuels ?? "",
                    },
                };
                if (c.Recording == null && !deferred) { e.Claim = 1; e.Done.Set(); }
                _recent.Add(e);
                EvictLocked(e);
            }
            _group?.Enforce(e);
            if (c.Recording != null || deferred) Enqueue(() => EnsureTape(e, true));
            Say($"LAP ARCHIVE: kept {e.Id} ({c.LapSeconds:F1} s, {c.Kind}{(c.Recording == null ? ", ghost only" : "")}{(deferred ? ", ghost bytes made on the worker" : "")}, {raw / 1048576.0:F1} MB)");
            return new LapSubmitResult(true, e.Id, "");
        }
        catch (Exception ex) { return Refuse(null, $"{ex.GetType().Name}: {ex.Message}"); }
    }

    LapSubmitResult Refuse(string label, string reason)
    {
        Say($"LAP ARCHIVE: lap not kept{(label == null ? "" : " (" + label + ")")}: {reason}");
        return new LapSubmitResult(false, null, reason);
    }

    static string Clean(string s) { var sb = new StringBuilder(); foreach (char ch in (s ?? "").ToLowerInvariant()) sb.Append(char.IsAsciiLetterOrDigit(ch) || ch == '-' ? ch : '_'); return sb.Length == 0 ? "x" : sb.ToString(); }

    Entry Find(string id) => _recent.FirstOrDefault(x => x.Id == id) ?? (_disk.TryGetValue(id, out var d) ? d : null);

    /// <summary>The bytes an entry references right now (Ghost + Tape + Rec + GhostSrc estimates); the payload being compressed is counted in <see cref="_inFlight"/> instead.</summary>
    static long OwnedOf(Entry e) => (e.Ghost?.Length ?? 0) + (e.Tape?.Length ?? 0) + RecordingBytes(e.Rec) + EstimateGhost(e.GhostSrc);

    /// <summary>What the memory ceiling counts: the recent entries' references plus the payload being compressed. Caller holds the lock.</summary>
    long HeldBytesLocked() { long b = _inFlight; foreach (var x in _recent) b += OwnedOf(x); return b; }

    /// <summary>Drops an entry from memory: every large reference it owns is released at once, queued work included. Caller holds the lock.</summary>
    void DropLocked(Entry v, string why)
    {
        _recent.Remove(v); v.Evicted = true; v.Ghost = null; v.Tape = null; v.Rec = null; v.GhostSrc = null; v.Size = 0;
        EvictedTrace?.Add(v);
        Say($"LAP ARCHIVE: dropped the oldest recent lap {v.Id} ({why}); retained laps are never dropped");
    }

    /// <summary>Oldest non-retained entries go first, by count and by bytes; the entry just added, retained entries and anything on disk never.</summary>
    void EvictLocked(Entry keep)
    {
        while (true)
        {
            if (_recent.Count <= _maxRecent && HeldBytesLocked() <= _maxRecentBytes) return;
            var victim = _recent.FirstOrDefault(x => x != keep && !x.RetainBusy);
            if (victim == null) return;
            DropLocked(victim, _recent.Count > _maxRecent ? "more than " + _maxRecent + " recent laps" : "memory limit");
        }
    }

    // ---- process-wide cap and accounting (used by LapArchiveGroup)

    internal long HeldBytes() { lock (_lock) return HeldBytesLocked(); }

    /// <summary>The oldest recent lap that can go (not retained or being retained, not <paramref name="keep"/>, not being compressed right now), or null.</summary>
    internal Entry OldestEvictable(Entry keep)
    {
        lock (_lock) return _recent.Where(x => x != keep && !x.RetainBusy && !(x.Making && x.Claim == 1)).OrderBy(x => x.Seq).FirstOrDefault();
    }

    internal void DropIfStill(Entry v, string why) { lock (_lock) if (!v.Evicted && !v.RetainBusy && _recent.Contains(v)) DropLocked(v, why); }

    /// <summary>A read-only snapshot of what this archive holds.</summary>
    public LapAccounting Accounting()
    {
        lock (_lock)
        {
            long owned = 0, pend = 0; int pc = 0;
            foreach (var x in _recent) { owned += OwnedOf(x); if (x.RetainBusy) { pend += x.Size; pc++; } }
            foreach (var x in _disk.Values) owned += OwnedOf(x);
            return new LapAccounting(_recent.Count, owned, _inFlight, pend, pc);
        }
    }

    /// <summary>Test access to an entry by id.</summary>
    internal Entry Probe(string id) { lock (_lock) return Find(id); }

    // ------------------------------------------------------------------ compression (worker or Open, whoever claims first)

    void EnsureTape(Entry e, bool viaWorker, int waitMs = 120000)
    {
        if (Interlocked.CompareExchange(ref e.Claim, 1, 0) != 0) { e.Done.Wait(waitMs); return; }
        long held = 0;
        try
        {
            WorldTapeRecorder.Recording rec; Ghost src;
            lock (_lock)
            {
                if (e.Evicted) return;
                // the payload moves from the entry into this call (the in-flight allowance, counted once); an eviction from here on cannot free it, but nothing is published afterwards
                rec = e.Rec; src = e.GhostSrc; e.Rec = null; e.GhostSrc = null;
                held = RecordingBytes(rec) + EstimateGhost(src); _inFlight += held;
            }
            if (viaWorker) WorkerHook?.Invoke("compress:" + e.Id);
            lock (_lock) if (e.Evicted) return;
            if (src != null && !PrepareGhost(e, src, rec)) return;
            src = null;
            if (rec == null) { lock (_lock) { if (e.State == LapEntryState.Pending) e.State = LapEntryState.Ready; } return; }
            var tape = WorldTapeRecorder.Compress(rec, e.Meta.LapSeconds, e.Meta.GhostFingerprint);
            tape.TrackId = e.Meta.TrackId; tape.Set = e.Meta.Set;
            var bytes = WorldTapeCodec.Encode(tape);
            var back = WorldTapeCodec.Decode(bytes);
            if (back == null || !WorldTapeCodec.Validate(back) || back.GhostFingerprint != e.Meta.GhostFingerprint) throw new InvalidDataException("the compressed tape does not verify");
            rec = null;
            lock (_lock)
            {
                if (e.Evicted) return;                      // dropped while compressing: the result is thrown away
                e.Tape = bytes; e.Size = (e.Ghost?.Length ?? 0) + bytes.Length;
                e.Meta = e.Meta with { TotalBytes = e.Size };
                if (e.State == LapEntryState.Pending) e.State = LapEntryState.Ready;
            }
        }
        catch (Exception ex)
        {
            bool say;
            lock (_lock)
            {
                say = !e.Evicted;
                e.Rec = null; e.GhostSrc = null;
                if (say)
                {
                    e.Size = e.Ghost?.Length ?? 0;
                    e.Meta = e.Meta with { HasTape = false, TotalBytes = e.Size }; e.State = LapEntryState.Failed; e.Message = e.TapeWarn = $"world tape not made ({ex.Message}); the lap is ghost only";
                }
            }
            if (say) Say($"LAP ARCHIVE: {e.Id}: {e.Message}");
        }
        finally { lock (_lock) { _inFlight -= held; e.Making = false; } e.Done.Set(); }
    }

    /// <summary>The deferred ghost: the file bytes and the fingerprint, made here on the worker from the archive's own copy of the ghost (so the finish line does not pay for the damage compression).</summary>
    bool PrepareGhost(Entry e, Ghost src, WorldTapeRecorder.Recording rec)
    {
        string why = null;
        try
        {
            var bytes = src.ToBytes(); ulong fp = WorldTapeFingerprint.Of(src);
            var check = LapGhostBytes.Fingerprint(bytes, out _, out _);
            long total = bytes.Length + RecordingBytes(rec);
            if (check == null || check.Value != fp) why = "the ghost data does not verify";
            else if (total > _maxLapBytes) why = $"lap too large to keep ({total / 1048576.0:F1} MB, the limit is {_maxLapBytes / 1048576.0:F1} MB per lap)";
            else
                lock (_lock)
                {
                    if (e.Evicted) return false;          // dropped while the bytes were made: do not repopulate
                    e.Ghost = bytes; e.GhostSrc = null; e.Size = total;
                    e.Meta = e.Meta with { GhostFingerprint = fp, TotalBytes = total };
                    return true;
                }
        }
        catch (Exception ex) { why = $"{ex.GetType().Name}: {ex.Message}"; }
        lock (_lock) { if (e.Evicted) return false; e.GhostSrc = null; e.Rec = null; e.Size = 0; e.State = LapEntryState.Failed; e.Message = "lap not kept: " + why; _recent.Remove(e); e.Evicted = true; }
        Say($"LAP ARCHIVE: lap not kept ({e.Meta.TrackId} {e.Meta.LapSeconds:F1} s): {why}");
        return false;
    }

    // ------------------------------------------------------------------ list

    /// <summary>Recent laps in memory and retained laps on disk, newest first, one row per id. The first call on a fresh archive scans the folder itself (small files); later scans are queued by <see cref="Refresh"/>.</summary>
    public IReadOnlyList<LapEntryInfo> List()
    {
        bool scanNow; lock (_lock) { scanNow = _scanState == 0; if (scanNow) _scanState = 2; }
        if (scanNow) { var found = Scan(); lock (_lock) MergeScan(found); }
        lock (_lock)
        {
            var rows = new Dictionary<string, Entry>();
            foreach (var d in _disk.Values) rows[d.Id] = d;
            foreach (var r in _recent) rows[r.Id] = r;
            return rows.Values.OrderByDescending(x => x.Meta.RecordedUtc).ThenByDescending(x => x.Id, StringComparer.Ordinal).Select(Info).ToList();
        }
    }

    static LapEntryInfo Info(Entry e)
    {
        bool openable = !e.Deleting && (e.Dir == null ? e.Ghost != null : e.State == LapEntryState.Ready);
        return new LapEntryInfo(e.Meta, e.State, e.Message, e.Retained, e.Dir == null, e.Size, openable, e.Dir, e.Pinned, e.HasTrim, e.HasKeys, e.EditsState);
    }

    /// <summary>Queues a rescan of the retained folder on the worker (after another process or a profile switch changed it).</summary>
    public void Refresh()
    {
        lock (_lock) _scanState = 1;
        Enqueue(() => { var found = Scan(); lock (_lock) { MergeScan(found); _scanState = 2; } });
    }

    void MergeScan(List<Entry> found)
    {
        var next = new Dictionary<string, Entry>();
        foreach (var f in found) next[f.Id] = f;
        foreach (var d in _disk.Values) if (d.RetainBusy || d.EditsBusy || d.Deleting) next[d.Id] = d;
        _disk = next;
    }

    List<Entry> Scan()
    {
        var list = new List<Entry>();
        string[] dirs;
        try { dirs = System.IO.Directory.Exists(_dir) ? System.IO.Directory.GetDirectories(_dir) : Array.Empty<string>(); }
        catch (Exception ex) { Say($"LAP ARCHIVE: cannot read the retained laps folder ({ex.Message})"); return list; }
        foreach (var d in dirs)
        {
            string name = Path.GetFileName(d);
            var e = new Entry { Dir = d, Retained = true, Meta = new LapMeta { Id = name, Retained = true }, Claim = 1 };
            e.Done.Set();
            try
            {
                string mp = Path.Combine(d, "meta.json"), gp = Path.Combine(d, "ghost.bin"), tp = Path.Combine(d, "tape.bin");
                if (!File.Exists(mp)) { e.State = LapEntryState.Failed; e.Message = "incomplete: meta.json is missing"; }
                else if (!LapMeta.TryParse(File.ReadAllText(mp), out var m, out var err)) { e.State = LapEntryState.Failed; e.Message = "meta.json unreadable: " + err; }
                else
                {
                    e.Meta = m with { Id = name, Retained = true };
                    if (m.Id != name) { e.State = LapEntryState.Failed; e.Message = "meta.json belongs to another lap id"; }
                    else if (m.Version > LapArchiveLimits.MetaVersion) { e.State = LapEntryState.Incompatible; e.Message = $"saved by a newer game (meta version {m.Version})"; }
                    else
                    {
                        int layout = m.Layout;
                        string vp = Path.Combine(d, "records.ver");
                        if (File.Exists(vp)) layout = int.TryParse(File.ReadAllText(vp).Trim(), out int v) ? v : -1;
                        if (layout != RecordStore.Layout) { e.State = LapEntryState.Incompatible; e.Message = $"record layout {layout}, this game uses {RecordStore.Layout}"; }
                        else if (!File.Exists(gp)) { e.State = LapEntryState.Failed; e.Message = "ghost.bin is missing"; }
                        else
                        {
                            e.State = LapEntryState.Ready;
                            if (m.HasTape && !File.Exists(tp)) e.Message = "tape.bin is missing, the lap is ghost only";
                            ReadEditsMarkers(e, d, e.Meta, File.Exists(tp) ? new FileInfo(tp).Length : 0);
                        }
                    }
                    e.Size = (File.Exists(gp) ? new FileInfo(gp).Length : 0) + (File.Exists(tp) ? new FileInfo(tp).Length : 0);
                }
            }
            catch (Exception ex) { e.State = LapEntryState.Failed; e.Message = "cannot read: " + ex.Message; }
            if (e.State != LapEntryState.Ready) Say($"LAP ARCHIVE: retained lap {name}: {e.State.ToString().ToUpperInvariant()}, {e.Message}");
            list.Add(e);
        }
        return list;
    }

    // ------------------------------------------------------------------ open

    public LapSource Open(string id) => Open(id, out _);

    /// <summary>
    /// The immutable source of a lap, or null with the reason. Waits only for this entry's tape compression when it is still pending (or does it itself when the worker has not started it).
    /// A retained lap whose tape is damaged or belongs to another ghost opens ghost only, with <see cref="LapSource.Warning"/> and a visible message.
    /// </summary>
    public LapSource Open(string id, out string reason)
    {
        reason = null;
        try
        {
            Entry e; lock (_lock) e = Find(id);
            if (e == null && _scanState == 0) { List(); lock (_lock) e = Find(id); }
            if (e == null) { reason = "no such lap"; return Bad(id, reason); }
            lock (_lock) if (e.Deleting || e.Evicted) { reason = "the recording is being removed"; return Bad(id, reason); }
            return e.Dir == null ? OpenMemory(e, out reason) : OpenDisk(e, out reason);
        }
        catch (Exception ex) { reason = $"{ex.GetType().Name}: {ex.Message}"; return Bad(id, reason); }
    }

    LapSource Bad(string id, string reason) { Say($"LAP ARCHIVE: cannot open {id}: {reason}"); return null; }

    LapSource OpenMemory(Entry e, out string reason)
    {
        reason = null;
        lock (_lock) if (e.Evicted) { reason = "the lap was dropped from memory"; return Bad(e.Id, reason); }
        EnsureTape(e, false);
        lock (_lock)
        {
            if (e.Ghost == null) { reason = "the lap was dropped from memory"; return Bad(e.Id, reason); }
            string warn = e.TapeWarn;
            if (e.Making || (e.Meta.HasTape && e.Tape == null && warn == null)) { reason = "the world tape is still being made"; return Bad(e.Id, reason); }
            return new LapSource(e.Meta, e.Ghost, e.Tape, warn);
        }
    }

    LapSource OpenDisk(Entry e, out string reason)
    {
        reason = null;
        if (e.State != LapEntryState.Ready) { reason = e.Message; return Bad(e.Id, reason); }
        string gp = Path.Combine(e.Dir, "ghost.bin"), tp = Path.Combine(e.Dir, "tape.bin");
        byte[] ghost = File.ReadAllBytes(gp);
        var fp = LapGhostBytes.Fingerprint(ghost, out _, out _);
        if (fp == null) { reason = "ghost.bin is damaged"; return Bad(e.Id, reason); }
        if (fp.Value != e.Meta.GhostFingerprint) { reason = "ghost.bin does not match meta.json"; return Bad(e.Id, reason); }
        byte[] tape = null; string warn = null;
        if (e.Meta.HasTape)
        {
            try
            {
                if (!File.Exists(tp)) warn = "tape.bin is missing, the lap opens ghost only";
                else
                {
                    var bytes = File.ReadAllBytes(tp);
                    var t = WorldTapeCodec.Decode(bytes);
                    if (t == null || t.SampleCount < 0 || !WorldTapeCodec.Validate(t)) warn = "tape.bin is damaged, the lap opens ghost only";
                    else if (t.GhostFingerprint != fp.Value) warn = "tape.bin belongs to another ghost, the lap opens ghost only";
                    else tape = bytes;
                }
            }
            catch (Exception ex) { warn = $"tape.bin cannot be read ({ex.Message}), the lap opens ghost only"; }
        }
        if (warn != null) Say($"LAP ARCHIVE: {e.Id}: {warn}");
        return new LapSource(e.Meta with { HasTape = tape != null }, ghost, tape, warn);
    }

    // ------------------------------------------------------------------ retain

    /// <summary>Writes the lap to <c>&lt;dir&gt;/&lt;id&gt;/</c> on the worker (tmp, verify, replace); the Task completes with the outcome and never throws. On a lap already retained it only renames.</summary>
    public Task<RetainResult> Retain(string id, string name)
    {
        var tcs = new TaskCompletionSource<RetainResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        Entry e;
        lock (_lock)
        {
            e = Find(id);
            if (e == null || e.RetainBusy || e.Deleting || e.Evicted) { var r = new RetainResult(false, e == null ? "no such lap" : e.Deleting || e.Evicted ? "recording removed or being removed" : "already being saved", null); Say($"LAP ARCHIVE: cannot keep {id}: {r.Message}"); tcs.SetResult(r); return tcs.Task; }
            if (e.Dir == null)
            {
                int pc = 0; long pend = 0; foreach (var x in _recent) if (x.RetainBusy) { pc++; pend += x.Size; }
                if (pc >= _maxPendingRetains || pend + e.Size > _maxPendingRetainBytes)
                {
                    var r = new RetainResult(false, $"too many laps are waiting to be saved ({pc} waiting, {pend / 1048576.0:F1} MB; the limit is {_maxPendingRetains} laps / {_maxPendingRetainBytes / 1048576.0:F1} MB), try again in a moment", null);
                    Say($"LAP ARCHIVE: cannot keep {id}: {r.Message}"); tcs.SetResult(r); return tcs.Task;
                }
            }
            e.RetainBusy = true;
        }
        name = CleanName(name);
        Enqueue(() =>
        {
            RetainResult r;
            try { r = RetainWork(e, name); }
            catch (Exception ex) { r = new RetainResult(false, $"{ex.GetType().Name}: {ex.Message}", null); }
            lock (_lock) e.RetainBusy = false;
            if (!r.Ok) Say($"LAP ARCHIVE: could not keep {e.Id}: {r.Message}");
            tcs.TrySetResult(r);
        });
        return tcs.Task;
    }

    static string CleanName(string s)
    {
        var sb = new StringBuilder(); foreach (char ch in (s ?? "").Trim()) if (!char.IsControl(ch)) sb.Append(ch);
        return sb.Length > 48 ? sb.ToString(0, 48) : sb.ToString();
    }

    RetainResult RetainWork(Entry e, string name)
    {
        WorkerHook?.Invoke("retain:" + e.Id);
        var ops = new BufferedOps(_ops);
        string Warnings() { var l = new List<string>(); while (ops.Warnings.TryDequeue(out var w)) l.Add(w); return string.Join("; ", l); }
        if (e.Dir != null) return Rename(e, name, ops, Warnings);

        EnsureTape(e, true);
        byte[] ghost, tape; LapMeta meta;
        lock (_lock) { ghost = e.Ghost; tape = e.Tape; meta = e.Meta; }
        if (ghost == null) return Fail(e, "the lap was dropped from memory");
        string dir = Path.Combine(_dir, e.Id), gp = Path.Combine(dir, "ghost.bin"), tp = Path.Combine(dir, "tape.bin");
        try
        {
            System.IO.Directory.CreateDirectory(dir);
            if (tape != null)
            {
                var res = WorldTapeFile.SavePairBytes(gp, tp, ghost, meta.GhostFingerprint, WorldTapeCodec.Decode(tape), ops, _ => tape, WorldTapeCodec.Decode);
                if (!res.Whole) return Fail(e, $"the pair was not saved ({res}){Suffix(Warnings())}");
            }
            else SaveFile(ops, gp, ghost);
            SaveFile(ops, Path.Combine(dir, "track.sig"), Encoding.UTF8.GetBytes(meta.TrackSignature + "\n"));
            SaveFile(ops, Path.Combine(dir, "records.ver"), Encoding.UTF8.GetBytes(meta.Layout + "\n"));
            var final = meta with { Name = name, Retained = true, TotalBytes = ghost.Length + (tape?.Length ?? 0) };
            SaveFile(ops, Path.Combine(dir, "meta.json"), Encoding.UTF8.GetBytes(final.ToJson()));      // last: its presence says the lap is complete
            lock (_lock)
            {
                e.Meta = final; e.Retained = true; e.Dir = dir; e.Ghost = null; e.Tape = null; e.Rec = null; e.GhostSrc = null; e.Size = final.TotalBytes; e.State = LapEntryState.Ready; e.Message = "";
                _recent.Remove(e); _disk[e.Id] = e;
            }
            Say($"LAP ARCHIVE: kept {e.Id} for good{(name.Length > 0 ? " as \"" + name + "\"" : "")} ({final.TotalBytes / 1048576.0:F1} MB)");
            return new RetainResult(true, "", dir);
        }
        catch (Exception ex) { return Fail(e, $"{ex.Message}{Suffix(Warnings())}"); }
    }

    static string Suffix(string w) => w.Length == 0 ? "" : " [" + w + "]";

    RetainResult Fail(Entry e, string why)
    {
        lock (_lock) { e.State = LapEntryState.Failed; e.Message = "could not be kept: " + why; }
        return new RetainResult(false, why, null);
    }

    RetainResult Rename(Entry e, string name, BufferedOps ops, Func<string> warnings)
    {
        if (e.State != LapEntryState.Ready) return new RetainResult(false, "this retained lap is " + e.State + ": " + e.Message, e.Dir);
        try
        {
            var final = e.Meta with { Name = name };
            SaveFile(ops, Path.Combine(e.Dir, "meta.json"), Encoding.UTF8.GetBytes(final.ToJson()));
            lock (_lock) e.Meta = final;
            return new RetainResult(true, "", e.Dir);
        }
        catch (Exception ex) { return new RetainResult(false, ex.Message + Suffix(warnings()), e.Dir); }
    }

    /// <summary>One small file with the pair save's discipline: tmp written and flushed, read back, then replaced (or moved into place). Throws; the tmp is removed and the old file stays.</summary>
    static void SaveFile(ITapeFileOps ops, string path, byte[] bytes)
    {
        string tmp = path + ".tmp";
        try
        {
            using (var fs = new FileStream(tmp, FileMode.Create, IOAccess.Write)) { fs.Write(bytes, 0, bytes.Length); fs.Flush(true); }
            ops.Step("tmp-written");
            if (!File.ReadAllBytes(tmp).AsSpan().SequenceEqual(bytes)) throw new IOException(Path.GetFileName(path) + " tmp unreadable");
            ops.Step("verified");
            ops.Step("before-replace");
            if (File.Exists(path)) ops.Replace(tmp, path); else ops.Move(tmp, path);
            ops.Step("after-replace");
        }
        catch { try { if (File.Exists(tmp)) File.Delete(tmp); } catch { } throw; }
    }
}

/// <summary>A read-only snapshot of an archive's (or a group's) memory: references actually held, the payload being compressed, and the size of the laps waiting for a retain.</summary>
public readonly record struct LapAccounting(int RecentCount, long OwnedBytes, long InFlightBytes, long PendingRetainBytes, int PendingRetainCount);

/// <summary>
/// Archives that share one memory cap (<see cref="LapArchive.Process"/> holds every <see cref="LapArchive.For"/> archive). When a capture is admitted the group evicts the oldest evictable recent lap
/// of any member until the sum of the members' held bytes (references + payloads in flight) is within the cap. Lock order: group gate, then one archive lock at a time.
/// </summary>
public sealed class LapArchiveGroup
{
    public readonly long MaxBytes;
    readonly object _gate = new(); readonly List<LapArchive> _members = new();
    public LapArchiveGroup(long maxBytes) { MaxBytes = maxBytes; }

    internal void Join(LapArchive a) { lock (_gate) if (!_members.Contains(a)) { _members.Add(a); a.SetGroup(this); } }

    public LapAccounting Accounting()
    {
        List<LapArchive> m; lock (_gate) m = _members.ToList();
        int rc = 0, pc = 0; long o = 0, f = 0, p = 0;
        foreach (var a in m) { var s = a.Accounting(); rc += s.RecentCount; o += s.OwnedBytes; f += s.InFlightBytes; p += s.PendingRetainBytes; pc += s.PendingRetainCount; }
        return new LapAccounting(rc, o, f, p, pc);
    }

    internal void Enforce(LapArchive.Entry keep)
    {
        lock (_gate)
        {
            while (true)
            {
                long total = 0; foreach (var a in _members) total += a.HeldBytes();
                if (total <= MaxBytes) return;
                LapArchive owner = null; LapArchive.Entry best = null;
                foreach (var a in _members) { var c = a.OldestEvictable(keep); if (c != null && (best == null || c.Seq < best.Seq)) { best = c; owner = a; } }
                if (best == null) return;
                owner.DropIfStill(best, "process-wide memory limit");
            }
        }
    }
}
