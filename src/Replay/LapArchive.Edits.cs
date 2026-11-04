using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CCCPRacing.Race;
using IOAccess = System.IO.FileAccess;

namespace CCCPRacing.Replay;

// The edits sidecar API of the archive (contract: header comment in LapEdits.cs). Worker-only IO, one job at a time on the archive's queue.
public sealed partial class LapArchive
{
    const string EditsName = "edits.json";

    readonly ConcurrentDictionary<string, (long Len, long Ticks, string Sha)> _hashes = new();
    int _hashRuns;
    /// <summary>Test seam: how many full-file SHA-256 computations the edits binding has made.</summary>
    internal int HashRuns => Volatile.Read(ref _hashRuns);

    // ------------------------------------------------------------------ binding hashes (worker only)

    /// <summary>SHA-256 of the whole file, cached per (path, length, mtime); null when the file does not exist. The identity is read before and after hashing, a file that changed meanwhile is not cached.</summary>
    string HashFile(string path, out long length)
    {
        length = 0;
        var fi = new FileInfo(path);
        if (!fi.Exists) return null;
        length = fi.Length; long ticks = fi.LastWriteTimeUtc.Ticks;
        if (_hashes.TryGetValue(path, out var c) && c.Len == length && c.Ticks == ticks) return c.Sha;
        string sha;
        using (var fs = new FileStream(path, FileMode.Open, IOAccess.Read, FileShare.Read))
        {
            length = fs.Length;
            sha = Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
        }
        Interlocked.Increment(ref _hashRuns);
        fi.Refresh();
        if (fi.Length == length && fi.LastWriteTimeUtc.Ticks == ticks) _hashes[path] = (length, ticks, sha); else _hashes.TryRemove(path, out _);
        return sha;
    }

    /// <summary>The pair's binding values. Throws when ghost.bin cannot be read.</summary>
    (string Ghost, string Tape, long TapeBytes) Bind(string dir)
    {
        string g = HashFile(Path.Combine(dir, "ghost.bin"), out _) ?? throw new FileNotFoundException("ghost.bin is missing");
        string t = HashFile(Path.Combine(dir, "tape.bin"), out long tl);
        return (g, t ?? LapEdits.Absent, t == null ? 0 : tl);
    }

    // ------------------------------------------------------------------ list markers (no hashing)

    /// <summary>Reads edits.json beside a scanned lap into the entry's markers: parse + validate + the cheap binding only (fingerprint, tape size, duration).</summary>
    void ReadEditsMarkers(Entry e, string dir, LapMeta meta, long tapeBytes)
    {
        var (state, ed) = Classify(Path.Combine(dir, EditsName), meta, tapeBytes);
        lock (_lock) SetMarkers(e, state, ed);
    }

    static void SetMarkers(Entry e, LapEditsState state, LapEdits ed)
    {
        e.EditsState = state;
        bool ok = state == LapEditsState.Ok && ed != null;
        e.Pinned = ok && ed.Pinned; e.HasTrim = ok && ed.HasTrim; e.HasKeys = ok && ed.HasCameraKeys;
    }

    static (LapEditsState, LapEdits) Classify(string path, LapMeta meta, long tapeBytes)
    {
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists) return (LapEditsState.None, null);
            if (fi.Length > LapEdits.MaxFileBytes) return (LapEditsState.Unreadable, null);
            switch (LapEdits.TryParse(File.ReadAllText(path), out var ed, out _))
            {
                case LapEditsLoadKind.NewerVersion: return (LapEditsState.NewerVersion, null);
                case LapEditsLoadKind.Ok:
                    if (ed.Validate(meta.LapSeconds) != null) return (LapEditsState.Unreadable, null);
                    return ed.CheckCheap(meta, tapeBytes) != null ? (LapEditsState.Mismatch, null) : (LapEditsState.Ok, ed);
                default: return (LapEditsState.Unreadable, null);
            }
        }
        catch { return (LapEditsState.Unreadable, null); }
    }

    // ------------------------------------------------------------------ API

    Entry EditsEntry(string id, out string refusal)
    {
        refusal = null;
        Entry e; lock (_lock) e = Find(id);
        if (e == null && _scanState == 0) { List(); lock (_lock) e = Find(id); }
        if (e == null) refusal = "no such lap";
        else if (e.Deleting || e.Evicted) { refusal = "recording removed or being removed"; e = null; }
        else if (e.Dir == null) { refusal = "keep it first: only kept laps can be edited"; e = null; }
        else if (e.State != LapEntryState.Ready) { refusal = "this kept lap is " + e.State + (string.IsNullOrEmpty(e.Message) ? "" : ": " + e.Message); e = null; }
        return e;
    }

    /// <summary>Reads and checks the lap's saved edits on the worker (hashing the pair once per file identity). Never writes. Never throws.</summary>
    public Task<LapEditsLoad> LoadEdits(string id)
    {
        var tcs = new TaskCompletionSource<LapEditsLoad>(TaskCreationOptions.RunContinuationsAsynchronously);
        var e = EditsEntry(id, out var why);
        if (e == null) { Say($"LAP ARCHIVE: cannot load edits of {id}: {why}"); tcs.SetResult(LapEditsLoad.Refused(why)); return tcs.Task; }
        Enqueue(() =>
        {
            LapEditsLoad r;
            try { WorkerHook?.Invoke("edits-load:" + e.Id); r = LoadWork(e); }
            catch (Exception ex) { r = LapEditsLoad.Refused($"{ex.GetType().Name}: {ex.Message}"); }
            if (r.Kind is LapEditsLoadKind.Invalid or LapEditsLoadKind.Mismatch or LapEditsLoadKind.NewerVersion) Say($"LAP ARCHIVE: {e.Id}: saved edits {r.Kind}: {r.Reason}");
            tcs.TrySetResult(r);
        });
        return tcs.Task;
    }

    LapEditsLoad LoadWork(Entry e)
    {
        LapMeta meta; lock (_lock) meta = e.Meta;
        string path = Path.Combine(e.Dir, EditsName);
        var fi = new FileInfo(path);
        if (!fi.Exists) { lock (_lock) SetMarkers(e, LapEditsState.None, null); return LapEditsLoad.Missing(); }
        var b = Bind(e.Dir);
        var (r, ed) = ReadEdits(path, meta, b);
        lock (_lock)
            SetMarkers(e, r.Kind switch { LapEditsLoadKind.Ok => LapEditsState.Ok, LapEditsLoadKind.Mismatch => LapEditsState.Mismatch, LapEditsLoadKind.NewerVersion => LapEditsState.NewerVersion, _ => LapEditsState.Unreadable }, ed);
        return r;
    }

    /// <summary>Classifies the file at <paramref name="path"/> against the pair's binding.</summary>
    static (LapEditsLoad, LapEdits) ReadEdits(string path, LapMeta meta, (string Ghost, string Tape, long TapeBytes) b)
    {
        var fi = new FileInfo(path);
        if (fi.Length > LapEdits.MaxFileBytes) return (LapEditsLoad.Invalid("the file is larger than 1 MB"), null);
        switch (LapEdits.TryParse(File.ReadAllText(path), out var ed, out var why))
        {
            case LapEditsLoadKind.NewerVersion: return (LapEditsLoad.NewerVersion(why), null);
            case LapEditsLoadKind.Ok:
                var bad = ed.Validate(meta.LapSeconds);
                if (bad != null) return (LapEditsLoad.Invalid(bad), null);
                var mm = ed.CheckBinding(meta, b.Ghost, b.Tape, b.TapeBytes);
                return mm != null ? (LapEditsLoad.Mismatch(mm), null) : (LapEditsLoad.Ok(ed), ed);
            default: return (LapEditsLoad.Invalid(why), null);
        }
    }

    /// <summary>Saves the lap's edits (the binding is filled in by the archive). Refused visibly when the lap is not kept, is being saved, or the values are invalid. Never throws.</summary>
    public Task<EditsSaveResult> SaveEdits(string id, LapEdits edits) => EditsJob(id, edits, false);

    /// <summary>Replaces the sidecar with a validated default one (previous good file kept as edits.json.bak). Never deletes anything.</summary>
    public Task<EditsSaveResult> ResetEdits(string id) => EditsJob(id, null, true);

    Task<EditsSaveResult> EditsJob(string id, LapEdits edits, bool reset)
    {
        var tcs = new TaskCompletionSource<EditsSaveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        string verb = reset ? "reset" : "save";
        Task<EditsSaveResult> Refuse(string why) { Say($"LAP ARCHIVE: cannot {verb} edits of {id}: {why}"); tcs.SetResult(new EditsSaveResult(false, why, id, null)); return tcs.Task; }
        if (!reset && edits == null) return Refuse("no edits given");
        var e = EditsEntry(id, out var refusal);
        if (e == null) return Refuse(refusal);
        if (!reset)
        {
            LapMeta m; lock (_lock) m = e.Meta;
            // the values are checked now with placeholder binding so that a plain mistake is refused without queueing; the worker checks the final, bound file again
            var probe = edits with { Version = LapEdits.CurrentVersion, GhostSha256 = new string('0', 64), TapeSha256 = LapEdits.Absent, GhostFingerprint = m.GhostFingerprint, LapSeconds = m.LapSeconds };
            var bad = probe.Validate(m.LapSeconds);
            if (bad != null) return Refuse("the edits are not valid: " + bad);
        }
        lock (_lock)
        {
            if (e.EditsBusy || e.Deleting || e.Evicted) { string why = e.EditsBusy ? "already being saved" : "recording removed or being removed"; Say($"LAP ARCHIVE: cannot {verb} edits of {id}: {why}"); tcs.SetResult(new EditsSaveResult(false, why, id, null)); return tcs.Task; }
            e.EditsBusy = true;
        }
        Enqueue(() =>
        {
            EditsSaveResult r;
            try { WorkerHook?.Invoke("edits-" + verb + ":" + e.Id); r = EditsWork(e, edits, reset); }
            catch (Exception ex) { r = new EditsSaveResult(false, $"{ex.GetType().Name}: {ex.Message}", e.Id, null); }
            try { RefreshMarkers(e); } catch { }
            lock (_lock) e.EditsBusy = false;
            if (!r.Ok) Say($"LAP ARCHIVE: could not {verb} edits of {e.Id}: {r.Message}");
            tcs.TrySetResult(r);
        });
        return tcs.Task;
    }

    void RefreshMarkers(Entry e)
    {
        LapMeta meta; lock (_lock) meta = e.Meta;
        string tp = Path.Combine(e.Dir, "tape.bin");
        ReadEditsMarkers(e, e.Dir, meta, File.Exists(tp) ? new FileInfo(tp).Length : 0);
    }

    // ------------------------------------------------------------------ the write

    /// <summary>
    /// Order (design 9.3): stage edits.json.tmp (flushed), read it back and validate it against the pair; then keep what is there (a good file becomes edits.json.bak, an unusable one edits.json.bad-n,
    /// each staged as a tmp and moved in, never deleted); only then replace edits.json atomically. Every step failure leaves the previous edits.json as it was.
    /// </summary>
    EditsSaveResult EditsWork(Entry e, LapEdits edits, bool reset)
    {
        var ops = new BufferedOps(_ops);
        string Warn() { var l = new System.Collections.Generic.List<string>(); while (ops.Warnings.TryDequeue(out var w)) l.Add(w); return l.Count == 0 ? "" : " [" + string.Join("; ", l) + "]"; }
        LapMeta meta; lock (_lock) meta = e.Meta;
        string dir = e.Dir, final = Path.Combine(dir, EditsName), tmp = final + ".tmp", bakTmp = final + ".bak.tmp", badTmp = final + ".bad.tmp";
        try
        {
            var b = Bind(dir);
            var want = reset ? LapEdits.Default(meta, b.Ghost, b.Tape, b.TapeBytes)
                : edits with { Version = LapEdits.CurrentVersion, GhostSha256 = b.Ghost, TapeSha256 = b.Tape, GhostFingerprint = meta.GhostFingerprint, TapeBytes = b.TapeBytes, LapSeconds = meta.LapSeconds };
            var bad = want.Validate(meta.LapSeconds);
            if (bad != null) return new EditsSaveResult(false, "the edits are not valid: " + bad, e.Id, null);
            byte[] bytes = Encoding.UTF8.GetBytes(want.ToJson());

            using (var fs = new FileStream(tmp, FileMode.Create, IOAccess.Write)) { fs.Write(bytes, 0, bytes.Length); fs.Flush(true); }
            ops.Step("edits-tmp-written");
            var back = File.ReadAllBytes(tmp);
            var (chk, _) = back.AsSpan().SequenceEqual(bytes) ? ReadEdits(tmp, meta, b) : (LapEditsLoad.Invalid("the staged file reads back different"), null);
            if (chk.Kind != LapEditsLoadKind.Ok) throw new IOException("the staged file does not verify: " + chk.Reason);
            ops.Step("edits-verified");

            if (File.Exists(final))
            {
                var (old, _) = ReadEditsSafe(final, meta, b);
                byte[] oldBytes = File.ReadAllBytes(final);
                if (old.Kind == LapEditsLoadKind.Ok)
                {
                    Stage(bakTmp, oldBytes); ops.Step("edits-bak-staged");
                    Place(ops, bakTmp, final + ".bak"); ops.Step("edits-bak-written");
                }
                else
                {
                    string dup = DuplicateBad(final, oldBytes);
                    if (dup == null)
                    {
                        int n = 1; while (File.Exists(final + ".bad-" + n)) n++;
                        Stage(badTmp, oldBytes); ops.Step("edits-bad-staged");
                        Place(ops, badTmp, final + ".bad-" + n); ops.Step("edits-bad-written");
                    }
                }
            }
            ops.Step("edits-before-replace");
            Place(ops, tmp, final);
            ops.Step("edits-after-replace");
            Say($"LAP ARCHIVE: {(reset ? "reset" : "saved")} the edits of {e.Id}");
            return new EditsSaveResult(true, "", e.Id, want);
        }
        catch (Exception ex) { return new EditsSaveResult(false, ex.Message + Warn(), e.Id, null); }
        finally { foreach (var t in new[] { tmp, bakTmp, badTmp }) { try { if (File.Exists(t)) File.Delete(t); } catch { } } }
    }

    static (LapEditsLoad, LapEdits) ReadEditsSafe(string path, LapMeta meta, (string Ghost, string Tape, long TapeBytes) b)
    {
        try { return ReadEdits(path, meta, b); } catch (Exception ex) { return (LapEditsLoad.Invalid(ex.Message), null); }
    }

    static void Stage(string tmp, byte[] bytes)
    {
        using var fs = new FileStream(tmp, FileMode.Create, IOAccess.Write); fs.Write(bytes, 0, bytes.Length); fs.Flush(true);
    }

    static void Place(ITapeFileOps ops, string tmp, string dest) { if (File.Exists(dest)) ops.Replace(tmp, dest); else ops.Move(tmp, dest); }

    /// <summary>An existing edits.json.bad-n with exactly these bytes (a retry must not pile up copies), or null.</summary>
    static string DuplicateBad(string final, byte[] bytes)
    {
        try
        {
            foreach (var f in Directory.GetFiles(Path.GetDirectoryName(final), Path.GetFileName(final) + ".bad-*"))
                if (!f.EndsWith(".tmp") && new FileInfo(f).Length == bytes.Length && File.ReadAllBytes(f).AsSpan().SequenceEqual(bytes)) return f;
        }
        catch { }
        return null;
    }
}
