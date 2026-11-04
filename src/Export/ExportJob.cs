using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CCCPRacing.Export;

/// <summary>What the job needs from the host, gathered on the main thread (the job itself touches no Godot object).</summary>
public sealed record ExportEnvironment(string ExePath, string ProjectDir, string JobsRoot, string DefaultDestination, string ProfileDir, string FfmpegConfigured, int ParentPid, string SettingsText = null)
{
    // SettingsText: the settings.cfg the child reads, as the host's CURRENT state (disk settings with the live sound balance, edges and softening applied: unsaved runtime choices are exported as shown).
    // Without it only the profile's settings.cfg is copied (never the leaderboards, laps or records).
    /// <summary>The viewport the child renders: the game's native pixel frame.</summary>
    public const int NativeW = 480, NativeH = 270;
}

/// <summary>
/// One MP4 export (docs/replay-mp4-export-proposal.md and its amendment). The worker thread: preflight, stage the immutable source, write the plan, start the exporter CHILD
/// (a second game process rendering the native 480x270 frame through Godot's movie writer), CONSUME its PNG frames while they are written (bounded: the child waits when 8 are
/// outstanding), pipe them to ffmpeg (nearest scale, libx264), trim the child's WAV to the exact samples, mux AAC, and rename the finished file atomically. Only files this job
/// created are ever deleted. The parent game keeps running; its UI reads <see cref="Progress"/>.
/// </summary>
public sealed class ExportJob
{
    public const int MaxOutstanding = 8;               // the child's PNG backlog (also the staging bound)
    const int BootTimeoutSec = 240, StallTimeoutSec = 120;

    readonly IExportSource _source; readonly ExportRequest _req; readonly ExportControl _control; readonly ExportEnvironment _env;
    volatile ExportProgress _progress = new(ExportPhase.Preparing, 0, 0, "PREPARING", 0);
    volatile Process _child, _ff1, _ff2; volatile Stream _ffIn; volatile bool _finished;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    public ExportProgress Progress => _progress;
    public Task Done { get; private set; }
    /// <summary>The exporter child's process id once started (tests check it is gone after cancel/failure), else 0.</summary>
    public int ChildPid { get { try { return _child?.Id ?? 0; } catch { return 0; } } }
    /// <summary>Tests only: a stand-in for the free-space query (path -> bytes free).</summary>
    public static Func<string, long> FreeSpaceOverride;
    public string JobId { get; } = DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N")[..6];

    ExportJob(IExportSource source, ExportRequest req, ExportControl control, ExportEnvironment env) { _source = source; _req = req; _control = control; _env = env; }

    public static ExportJob Start(IExportSource source, ExportRequest request, ExportControl control, ExportEnvironment env)
    {
        var job = new ExportJob(source, request, control ?? new ExportControl(), env);
        job.Done = Task.Factory.StartNew(job.Run, TaskCreationOptions.LongRunning);
        return job;
    }

    void Set(ExportPhase phase, string message, int done = -1, string output = null, string log = null, bool error = false)
        => _progress = new ExportProgress(phase, done >= 0 ? done : _progress.FramesDone, _req.FrameCount, message, _clock.Elapsed.TotalSeconds, output, log, error);

    // The TERMINAL state (Done, Cancelled, Failed) is published only after the job's cleanup has finished: a UI that sees "finished" can rely on the child being gone and the job folder deleted.
    volatile ExportProgress _terminal;
    void Terminal(ExportPhase phase, string message, int done = -1, string output = null, string log = null, bool error = false)
        => _terminal = new ExportProgress(phase, done >= 0 ? done : _progress.FramesDone, _req.FrameCount, message, _clock.Elapsed.TotalSeconds, output, log, error);

    // ------------------------------------------------------------------ helpers (pure, unit-tested)

    /// <summary>The first NAME.mp4 / NAME-2.mp4 ... that does not exist in the folder (never overwrites).</summary>
    public static string UniqueName(string dir, string name)
    {
        string baseName = string.Concat(name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-')).Trim('-');
        if (baseName.Length == 0) baseName = "replay";
        for (int n = 1; ; n++)
        {
            string path = Path.Combine(dir, n == 1 ? baseName + ".mp4" : $"{baseName}-{n}.mp4");
            if (!File.Exists(path) && !File.Exists(path + ".part")) return path;
        }
    }

    /// <summary>The bytes of the WAV's data chunk (offset, length) after checking it is 48 kHz stereo 32-bit PCM (what Godot's movie writer produces).</summary>
    public static (long Offset, long Length) WavData(string path)
    {
        using var f = File.OpenRead(path); using var r = new BinaryReader(f);
        if (new string(r.ReadChars(4)) != "RIFF") throw new InvalidDataException("not a WAV");
        r.ReadUInt32(); if (new string(r.ReadChars(4)) != "WAVE") throw new InvalidDataException("not a WAV");
        int ch = 0, rate = 0, bits = 0;
        while (f.Position + 8 <= f.Length)
        {
            string id = new string(r.ReadChars(4)); uint size = r.ReadUInt32();
            if (id == "fmt ") { r.ReadUInt16(); ch = r.ReadUInt16(); rate = (int)r.ReadUInt32(); r.ReadUInt32(); r.ReadUInt16(); bits = r.ReadUInt16(); f.Position += size - 16; }
            else if (id == "data")
            {
                if (ch != 2 || rate != 48000 || bits != 32) throw new InvalidDataException($"unexpected audio format {ch} ch {rate} Hz {bits} bit");
                long len = Math.Min(size, f.Length - f.Position);       // (a file closed abnormally has no final size: take what is there)
                return (f.Position, len);
            }
            else f.Position += size + (size & 1);
        }
        throw new InvalidDataException("no data chunk");
    }

    /// <summary>Copies exactly the samples [startFrame, startFrame+frames) (800 stereo 32-bit samples per movie frame at 60 fps) into a raw file; false with the reason if the WAV is too short.</summary>
    public static bool TrimWav(string wav, string rawOut, int startFrame, int frames, int fps, out string why)
    {
        why = null;
        if (48000 % fps != 0) { why = $"{fps} fps does not divide the 48 kHz mix rate"; return false; }
        long bytesPerFrame = 48000L / fps * 8;
        var (off, len) = WavData(wav);
        long from = startFrame * bytesPerFrame, count = frames * bytesPerFrame;
        if (from + count > len) { why = $"the audio holds {len / bytesPerFrame} frames but {startFrame + frames} are needed"; return false; }
        using var src = File.OpenRead(wav); src.Position = off + from;
        using var dst = File.Create(rawOut);
        var buf = new byte[1 << 16]; long left = count;
        while (left > 0) { int n = src.Read(buf, 0, (int)Math.Min(buf.Length, left)); if (n <= 0) { why = "audio read ended early"; return false; } dst.Write(buf, 0, n); left -= n; }
        return true;
    }

    /// <summary>Everything a malformed request or source could break, checked BEFORE any frame count or division: null when fine, else the player-facing reason.</summary>
    public static string Validate(ExportRequest r, IExportSource s)
    {
        if (!float.IsFinite(r.InClock) || !float.IsFinite(r.OutClock)) return "THE SPAN IS NOT A NUMBER";
        if (r.Fps != 30 && r.Fps != 60) return "THE FRAME RATE MUST BE 30 OR 60";
        if (r.OutHeight is not (540 or 720 or 1080)) return "THE SIZE MUST BE 540P, 720P OR 1080P";
        if (r.Crf < 10 || r.Crf > 40) return "THE QUALITY IS OUT OF RANGE";
        float min = s.MinClockSeconds, max = s.DurationSeconds;
        if (!float.IsFinite(min) || !float.IsFinite(max) || max <= min) return "THE RECORDING HAS NO PLAYABLE SPAN";
        if (r.ValidateCameras(max) is string cameraError) return cameraError;
        if (r.HasFreeCameraKeys && s.Kind != "sp-pair") return "FREE CAMERA EXPORT IS NOT SUPPORTED FOR THIS SOURCE YET";
        if (r.InClock < min - 1e-3f || r.OutClock > max + 1e-3f) return $"THE SPAN IS OUTSIDE THE RECORDING ({min:0.0} - {max:0.0} S)";
        if (r.OutClock <= r.InClock) return "THE SPAN IS EMPTY";
        if (r.FrameCount < 2) return "THE SPAN IS TOO SHORT";
        // the engine's movie WAV has 32-bit RIFF sizes (4 GB): the child also spools its boot (bounded 5 minutes)
        if ((r.OutClock - r.InClock + 300.0) * 384_000.0 > 4_000_000_000.0) return "THE SPAN IS TOO LONG FOR THE AUDIO FILE (ABOUT 2.8 HOURS AT MOST)";
        return null;
    }

    /// <summary>Bytes the export may need: (jobs volume, destination volume). Staging is bounded: 8 native PNGs, the WAV spool (384 KB/s), the bootstrap copy; the video part and the final file on the destination.</summary>
    public static (long Jobs, long Dest) Estimate(ExportRequest r, long copiedBytes)
    {
        double span = Math.Max(0.1, (r.OutClock - r.InClock));
        long wav = (long)(span * 384_000 * 2) + 80L * 1024 * 1024;       // the child's WAV (the whole boot too) and the trimmed raw copy of the span
        long png = MaxOutstanding * 400_000L;
        double pixels = r.OutWidth * (double)r.OutHeight / (1920.0 * 1080.0);
        long video = (long)(span * 2_000_000 * Math.Clamp(pixels, 0.25, 1.0)) + 5_000_000;      // maxrate-bounded
        long audio = (long)(span * 30_000) + 100_000;
        return (copiedBytes + wav + png + 100L * 1024 * 1024, 2 * (video + audio) + 200L * 1024 * 1024);
    }

    // ------------------------------------------------------------------ the worker

    sealed class Owned { public readonly List<string> Files = new(); public string JobDir; }

    void Run()
    {
        var owned = new Owned(); string log = null; string finalPath = null;
        string dest = _req.DestinationDir ?? _env.DefaultDestination;
        var disposables = new List<IDisposable>();
        // the cancel WATCHDOG: whoever is blocked (a pipe write, a wait for ffmpeg or the child) is released by killing the owned processes and closing the pipe
        var watchdog = Task.Run(async () => { while (!_finished) { if (_control.CancelRequested) { Kill(_child); Kill(_ff1); Kill(_ff2); try { _ffIn?.Close(); } catch { } } await Task.Delay(40); } });
        try
        {
            Set(ExportPhase.Preparing, "CHECKING FFMPEG AND DISK");
            string ffmpeg = FfmpegLocator.Find(Path.GetDirectoryName(_env.ExePath), _env.FfmpegConfigured, out string ffErr);
            if (ffmpeg == null) throw new ExportFailure(ffErr ?? FfmpegLocator.MissingText);
            if (!ExportBootstrap.Supported(out string unsup)) throw new ExportFailure(unsup);
            string bad = Validate(_req, _source); if (bad != null) throw new ExportFailure(bad);
            Directory.CreateDirectory(dest); Directory.CreateDirectory(_env.JobsRoot);
            long copy = ExportBootstrap.CopyBytes(_env.ExePath, _env.ProjectDir != null);
            var (needJobs, needDest) = Estimate(_req, copy);
            long freeJobs = Free(_env.JobsRoot), freeDest = Free(dest);
            bool sameVolume = Path.GetPathRoot(Path.GetFullPath(dest)) == Path.GetPathRoot(Path.GetFullPath(_env.JobsRoot)) && SameDevice(dest, _env.JobsRoot);
            if (sameVolume ? freeJobs < needJobs + needDest : freeJobs < needJobs || freeDest < needDest)
                throw new ExportFailure($"NOT ENOUGH DISK SPACE: NEED {(sameVolume ? needJobs + needDest : Math.Max(needJobs, needDest)) / 1048576} MB, FREE {Math.Min(freeJobs, freeDest) / 1048576} MB");
            if (_control.CancelRequested) throw new OperationCanceledException();

            owned.JobDir = Path.Combine(_env.JobsRoot, JobId);
            Directory.CreateDirectory(owned.JobDir);
            File.WriteAllText(Path.Combine(owned.JobDir, ".cccp-export-job"), JobId + "\n");      // the only ownership proof cleanup trusts
            log = Path.Combine(owned.JobDir, "export.log");
            void Log(string s) { try { File.AppendAllText(log, $"[{_clock.Elapsed.TotalSeconds:F1}] {s}\n"); } catch { } }

            Set(ExportPhase.Preparing, "STAGING THE RECORDING");
            string sourceDir = Path.Combine(owned.JobDir, "source"); Directory.CreateDirectory(sourceDir);
            var staged = _source.Stage(sourceDir);
            Log($"staged {staged.Files.Count} file(s), {staged.Bytes} bytes, id {staged.SourceId}");
            // the source's real size is only known now: the estimate is checked again with it
            if (Free(_env.JobsRoot) < needJobs + staged.Bytes + (sameVolume ? needDest : 0)) throw new ExportFailure($"NOT ENOUGH DISK SPACE FOR THE STAGED RECORDING ({staged.Bytes / 1048576} MB)");
            string dataDir = Path.Combine(owned.JobDir, "data"); Directory.CreateDirectory(dataDir);
            if (_env.SettingsText != null) File.WriteAllText(Path.Combine(dataDir, "settings.cfg"), _env.SettingsText);
            else if (_env.ProfileDir != null && File.Exists(Path.Combine(_env.ProfileDir, "settings.cfg"))) File.Copy(Path.Combine(_env.ProfileDir, "settings.cfg"), Path.Combine(dataDir, "settings.cfg"));      // the one settings file: never the leaderboards, laps or records
            var plan = new ExportPlan { JobId = JobId, SourceKind = _source.Kind, JobDir = owned.JobDir, SourceDir = sourceDir, SourceId = staged.SourceId, TrackId = _source.TrackId, Request = _req,
                ParentPid = _env.ParentPid, Extra = staged.Extra ?? new() };
            string planPath = Path.Combine(owned.JobDir, "plan.json"); File.WriteAllText(planPath, plan.ToJson());
            var launch = ExportBootstrap.Build(owned.JobDir, _env.ExePath, _env.ProjectDir, planPath, dataDir, _req.Fps, _source.Title, _req.DebugFullRes ? _req.OutWidth : ExportEnvironment.NativeW, _req.DebugFullRes ? _req.OutHeight : ExportEnvironment.NativeH);
            Log($"launch {launch.Exe} {string.Join(' ', launch.Args)}");
            if (_control.CancelRequested) throw new OperationCanceledException();

            Set(ExportPhase.Starting, "PREPARING VIDEO");
            var psi = new ProcessStartInfo(launch.Exe) { WorkingDirectory = launch.WorkDir, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in launch.Args) psi.ArgumentList.Add(a);
            psi.Environment["XDG_DATA_HOME"] = Path.Combine(owned.JobDir, "xdg");      // the child never touches the real user data
            string hiddenReady = ExportWindowHider.Configure(psi, owned.JobDir);
            var child = _child = Process.Start(psi); disposables.Add(child);
            var childLog = new StreamWriter(Path.Combine(owned.JobDir, "child.log")) { AutoFlush = true }; disposables.Add(childLog);
            child.OutputDataReceived += (_, e) => { if (e.Data != null) lock (childLog) childLog.WriteLine(e.Data); };
            child.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (childLog) childLog.WriteLine(e.Data); };
            child.BeginOutputReadLine(); child.BeginErrorReadLine();

            // ---- the consuming queue
            string movie = Path.Combine(owned.JobDir, "movie"); string markers = Path.Combine(owned.JobDir, "markers.txt");
            string Png(int i) => Path.Combine(movie, $"m{i:D8}.png");
            string name = Path.GetFileNameWithoutExtension(finalPath = UniqueName(dest, _req.Name));
            string part = finalPath + ".part"; string videoPart = Path.Combine(dest, $".{name}.{JobId}.video.part");
            owned.Files.Add(part); owned.Files.Add(videoPart);
            int next = 0, sent = 0, startIdx = -1, total = _req.FrameCount; var sw = Stopwatch.StartNew(); long lastActivity = Environment.TickCount64, lastDisk = lastActivity; string childError = null;
            Stream ffIn = null; Process ff1 = null;
            int fps = _req.Fps;
            bool hiddenVerified = false;
            while (true)
            {
                if (_control.CancelRequested) throw new OperationCanceledException();
                bool alive = !child.HasExited;
                if (startIdx < 0 || childError == null) ReadMarkers(markers, ref startIdx, ref childError);
                bool ready = File.Exists(Png(next + 1)) || (!alive && File.Exists(Png(next)));
                if (ready)
                {
                    if (!hiddenVerified)
                    {
                        if (!ExportWindowHider.Ready(hiddenReady))
                            throw new ExportFailure("BACKGROUND VIDEO RENDERER DID NOT INITIALIZE");
                        hiddenVerified = true;
                        Log("renderer window mapping suppressed (native audit ready)");
                    }
                    if (startIdx < 0 && alive && !File.Exists(Png(next + 1))) { Thread.Sleep(2); continue; }
                    if (startIdx >= 0 && next >= startIdx && next < startIdx + total)
                    {
                        if (ffIn == null)
                        {
                            if (_req.HasFreeCameraKeys && !File.ReadAllLines(markers).Contains("CAMERA_KEYS 2"))
                                throw new ExportFailure("THE EXPORT RENDERER DOES NOT SUPPORT FREE CAMERA KEYS - UPDATE THE GAME");
                            if (_req.HasCameraMotion && !File.ReadAllLines(markers).Contains("CAMERA_MOTION 3"))
                                throw new ExportFailure("THE EXPORT RENDERER DOES NOT SUPPORT CAMERA MOTION - UPDATE THE GAME");
                            Set(ExportPhase.Rendering, "RENDERING");
                            ff1 = _ff1 = StartFfmpeg(ffmpeg, Video(fps, _req, videoPart), Path.Combine(owned.JobDir, "ffmpeg-video.log"), out ffIn); _ffIn = ffIn; disposables.AddRange(Dispose); Dispose.Clear();
                        }
                        byte[] bytes = File.ReadAllBytes(Png(next));
                        try { ffIn.Write(bytes, 0, bytes.Length); ffIn.Flush(); }
                        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
                        {
                            if (_control.CancelRequested) throw new OperationCanceledException();
                            throw new ExportFailure("FFMPEG STOPPED (SEE THE LOG): " + Tail(Path.Combine(owned.JobDir, "ffmpeg-video.log")));
                        }
                        sent++;
                        double fpsNow = sent / Math.Max(0.1, sw.Elapsed.TotalSeconds);
                        Set(ExportPhase.Rendering, $"EXPORTING {sent}/{total}  ETA {(int)((total - sent) / Math.Max(0.1, fpsNow))} S", sent);
                    }
                    File.Delete(Png(next)); next++; lastActivity = Environment.TickCount64;
                    continue;
                }
                if (!alive) break;
                if (Environment.TickCount64 - lastDisk > 2000)      // live disk bound: a full disk fails now, not at the end
                {
                    lastDisk = Environment.TickCount64;
                    if (Free(_env.JobsRoot) < 150L * 1024 * 1024 || Free(dest) < 150L * 1024 * 1024) throw new ExportFailure("THE DISK IS FULL");
                }
                if (Environment.TickCount64 - lastActivity > (startIdx < 0 && sent == 0 ? BootTimeoutSec : StallTimeoutSec) * 1000L) throw new ExportFailure("THE EXPORT GAME STALLED");
                Thread.Sleep(2);
            }
            child.WaitForExit();
            if (_control.CancelRequested) throw new OperationCanceledException();
            ReadMarkers(markers, ref startIdx, ref childError);
            if (child.ExitCode != 0 || childError != null)
                throw new ExportFailure(childError != null ? "EXPORT GAME: " + childError.ToUpperInvariant() : $"THE EXPORT GAME EXITED WITH CODE {child.ExitCode}");
            if (ffIn == null || sent != total) throw new ExportFailure($"INCOMPLETE: {sent} OF {total} FRAMES");
            Log($"consumed {next} images, {sent} sent, start image {startIdx}");
            ffIn.Close(); ff1.WaitForExit();
            if (_control.CancelRequested) throw new OperationCanceledException();
            if (ff1.ExitCode != 0) throw new ExportFailure("FFMPEG FAILED: " + Tail(Path.Combine(owned.JobDir, "ffmpeg-video.log")));

            Set(ExportPhase.Encoding, _req.Audio ? "ENCODING THE AUDIO" : "FINISHING", total);
            if (_req.Audio)
            {
                string raw = Path.Combine(owned.JobDir, "audio.raw");
                if (!TrimWav(Path.Combine(movie, "m.wav"), raw, startIdx, total, fps, out string wavWhy)) throw new ExportFailure("AUDIO: " + wavWhy.ToUpperInvariant());
                var ff2 = _ff2 = StartFfmpeg(ffmpeg, new[] { "-hide_banner", "-loglevel", "error", "-y", "-i", videoPart, "-f", "s32le", "-ar", "48000", "-ac", "2", "-i", raw,
                    "-c:v", "copy", "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart", "-f", "mp4", part }, Path.Combine(owned.JobDir, "ffmpeg-mux.log"), out var _); disposables.AddRange(Dispose); Dispose.Clear();
                ff2.WaitForExit();
                if (_control.CancelRequested) throw new OperationCanceledException();
                if (ff2.ExitCode != 0) throw new ExportFailure("FFMPEG (AUDIO) FAILED: " + Tail(Path.Combine(owned.JobDir, "ffmpeg-mux.log")));
                File.Delete(videoPart);
            }
            else File.Move(videoPart, part);
            Set(ExportPhase.Finishing, "FINISHING");
            // atomic final name, never over an existing file
            for (int tries = 0; ; tries++)
            {
                try { File.Move(part, finalPath, false); break; }
                catch (IOException) when (File.Exists(finalPath) && tries < 50) { finalPath = UniqueName(dest, _req.Name + "-" + (tries + 2)); }
            }
            owned.Files.Remove(part);
            Log($"done {finalPath}");
            if (_req.KeepDiagnostics)
            {
                string diag = Path.Combine(dest, Path.GetFileNameWithoutExtension(finalPath) + ".diag"); Directory.CreateDirectory(diag);
                foreach (var f in new[] { "markers.txt", "frames.jsonl", "window-hidden.ready", "free-camera-poses.jsonl", "export.log", "child.log", "plan.json", "ffmpeg-video.log", "ffmpeg-mux.log" })
                    if (File.Exists(Path.Combine(owned.JobDir, f))) File.Copy(Path.Combine(owned.JobDir, f), Path.Combine(diag, f), true);
                if (_req.DebugKeepAudio)
                    foreach (var f in new[] { Path.Combine("movie", "m.wav"), "audio.raw" })
                        if (File.Exists(Path.Combine(owned.JobDir, f))) File.Copy(Path.Combine(owned.JobDir, f), Path.Combine(diag, Path.GetFileName(f)), true);
            }
            Terminal(ExportPhase.Done, "DONE", total, finalPath);
        }
        catch (OperationCanceledException) { Terminal(ExportPhase.Cancelled, "EXPORT CANCELLED"); }
        catch (ExportFailure f) { Fail(f.Message, owned, dest, ref log); }
        catch (Exception e) { Fail("EXPORT FAILED: " + e.Message.ToUpperInvariant(), owned, dest, ref log); }
        finally
        {
            _finished = true;
            foreach (var p in new[] { _ff1, _ff2, _child }) Kill(p);
            try { _ffIn?.Close(); } catch { }
            foreach (var p in new[] { _ff1, _ff2, _child }) { try { p?.WaitForExit(3000); } catch { } }
            foreach (var d in disposables) { try { d.Dispose(); } catch { } }      // logs and process handles closed BEFORE anything is deleted
            try { watchdog.Wait(1000); } catch { }
            bool ok = _terminal?.Phase == ExportPhase.Done;
            if (!ok) foreach (var f in owned.Files) TryDelete(f);
            if (owned.JobDir != null) DeleteJobDir(owned.JobDir, _env.JobsRoot, JobId);
            _progress = _terminal ?? new ExportProgress(ExportPhase.Failed, _progress.FramesDone, _req.FrameCount, "THE EXPORT ENDED UNEXPECTEDLY", _clock.Elapsed.TotalSeconds, null, null, true);      // (published last)
        }
    }

    void Fail(string message, Owned owned, string dest, ref string log)
    {
        string keep = null;
        try
        {
            if (log != null && File.Exists(log))
            {
                string childLog = Path.Combine(owned.JobDir, "child.log");
                keep = Path.Combine(dest, $"export-failed-{JobId}.log");
                using var o = File.Create(keep); using var w = new StreamWriter(o);
                w.WriteLine("EXPORT FAILED: " + message); w.WriteLine(File.ReadAllText(log));
                if (File.Exists(childLog)) { w.WriteLine("--- child ---"); w.WriteLine(string.Join('\n', File.ReadLines(childLog).TakeLast(120))); }
            }
        }
        catch { keep = null; }
        Terminal(ExportPhase.Failed, message, log: keep, error: true);
    }

    static string[] Video(int fps, ExportRequest r, string output) => new[] { "-hide_banner", "-loglevel", "error", "-y", "-f", "image2pipe", "-framerate", fps.ToString(), "-c:v", "png", "-i", "pipe:0",
        "-vf", $"scale={r.OutWidth}:{r.OutHeight}:flags=neighbor", "-c:v", "libx264", "-crf", r.Crf.ToString(), "-preset", "veryfast", "-maxrate", "16M", "-bufsize", "32M",
        "-pix_fmt", "yuv420p", "-movflags", "+faststart", "-an", "-f", "mp4", output };

    [ThreadStatic] static List<IDisposable> _dispose;
    static List<IDisposable> Dispose => _dispose ??= new();
    static Process StartFfmpeg(string ffmpeg, string[] args, string logPath, out Stream stdin)
    {
        var psi = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardError = true, RedirectStandardOutput = true, CreateNoWindow = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        var p = Process.Start(psi); var w = new StreamWriter(logPath) { AutoFlush = true }; Dispose.Add(w); Dispose.Add(p);
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (w) w.WriteLine(e.Data); };
        p.OutputDataReceived += (_, e) => { };
        p.BeginErrorReadLine(); p.BeginOutputReadLine();
        stdin = p.StandardInput.BaseStream;
        return p;
    }

    static void ReadMarkers(string path, ref int start, ref string error)
    {
        try
        {
            if (!File.Exists(path)) return;
            using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); using var r = new StreamReader(f);
            string line;
            while ((line = r.ReadLine()) != null)
            {
                if (line.StartsWith("START ") && int.TryParse(line.AsSpan(6), out int s)) start = s;
                else if (line.StartsWith("ERROR ")) error = line[6..];
            }
        }
        catch (IOException) { }
    }

    static string Tail(string path) { try { return string.Join(" ", File.ReadLines(path).TakeLast(3)); } catch { return ""; } }
    static long Free(string dir) { if (FreeSpaceOverride != null) return FreeSpaceOverride(dir); try { return new DriveInfo(Path.GetFullPath(dir)).AvailableFreeSpace; } catch { return long.MaxValue; } }
    static bool SameDevice(string a, string b)
    {
        try { return new DriveInfo(Path.GetFullPath(a)).Name == new DriveInfo(Path.GetFullPath(b)).Name; } catch { return true; }
    }
    static void Kill(Process p) { try { if (p != null && !p.HasExited) p.Kill(true); } catch { } }
    static void TryDelete(string f) { try { if (File.Exists(f)) File.Delete(f); } catch { } }

    /// <summary>Deletes the job folder only when it is a direct child of the jobs root and carries this job's ownership marker.</summary>
    public static void DeleteJobDir(string jobDir, string jobsRoot, string jobId)
    {
        try
        {
            string full = Path.GetFullPath(jobDir), root = Path.GetFullPath(jobsRoot);
            if (Path.GetDirectoryName(full) != root || Path.GetFileName(full) != jobId) return;
            string marker = Path.Combine(full, ".cccp-export-job");
            if (!File.Exists(marker) || File.ReadAllText(marker).Trim() != jobId) return;
            // symlinks inside (the bootstrap's) are removed as links, never followed
            foreach (var entry in Directory.EnumerateFileSystemEntries(full, "*", SearchOption.TopDirectoryOnly))
            {
                var fi = new FileInfo(entry);
                if (fi.LinkTarget != null) { if (Directory.Exists(entry)) Directory.Delete(entry); else File.Delete(entry); }
            }
            string game = Path.Combine(full, "game");
            if (Directory.Exists(game))
                foreach (var entry in Directory.EnumerateFileSystemEntries(game)) { var fi = new FileInfo(entry); if (fi.LinkTarget != null) { if (Directory.Exists(entry)) Directory.Delete(entry); else File.Delete(entry); } }
            Directory.Delete(full, true);
        }
        catch { }
    }

    sealed class ExportFailure : Exception { public ExportFailure(string m) : base(m) { } }
}
