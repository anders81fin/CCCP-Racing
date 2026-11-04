using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CCCPRacing.Export;
using CCCPRacing.Replay;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    // --replay-export-tests: a real export of a fixture lap through ExportJob (the exporter CHILD is a GPU game process whose window is never mapped), then ffprobe checks. Options:
    // --re-fx=DIR (fixture laps: DIR/TRACK, default /tmp/cccp-lc-fx), --re-track=ilyich, --re-in=10 --re-out=16, --re-out-dir=DIR (media/...), --re-height=1080, --re-audio=1, --re-renderer=PACKAGED_EXE (optional).
    static string ReOpt(string name) => OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith(name + "="))?[(name.Length + 1)..];

    async void ReplayExportTests()
    {
        try { await ReplayExportTestsBody(); }
        catch (Exception e) { GD.PrintErr(e); _failures++; }
        GD.Print(_failures == 0 ? "Replay export tests: all ok" : $"Replay export tests: {_failures} FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    void ReCheck(bool ok, string what) { GD.Print($"  [{(ok ? "ok" : "FAIL")}] {what}"); if (!ok) _failures++; }

    static string Sha(string path) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));

    async Task ReplayExportTestsBody()
    {
        if (ReOpt("--re-failures") == "1") { await ReplayExportFailureTests(); return; }
        string track = ReOpt("--re-track") ?? "ilyich", fx = ReOpt("--re-fx") ?? "/tmp/cccp-lc-fx";
        float tin = float.Parse(ReOpt("--re-in") ?? "10", System.Globalization.CultureInfo.InvariantCulture), tout = float.Parse(ReOpt("--re-out") ?? "16", System.Globalization.CultureInfo.InvariantCulture);
        int height = int.Parse(ReOpt("--re-height") ?? "1080"); bool audio = (ReOpt("--re-audio") ?? "1") == "1";
        string outDir = ReOpt("--re-out-dir") ?? Path.Combine(TapeProjectDir, "media", "replay-export-20261028");
        Directory.CreateDirectory(outDir);
        var box = LapArchive.For(Path.Combine(fx, track));
        var info = box.List().FirstOrDefault(i => i.Openable);
        if (info == null) { ReCheck(false, $"a fixture lap exists for {track} in {fx}"); return; }
        var lap = box.Open(info.Id);
        string lapDir = info.Dir;
        var before = Directory.GetFiles(lapDir).ToDictionary(f => Path.GetFileName(f), Sha);
        string srcSha = lap.Sha256();
        var req = new ExportRequest
        {
            InClock = tin, OutClock = tout, OutHeight = height, Audio = audio, DestinationDir = outDir, Name = $"{track}-export-test-{height}p" + (ReOpt("--re-fullres") == "1" ? "-fullres" : ""), KeepDiagnostics = true, DebugKeepAudio = ReOpt("--re-keep-audio") == "1", DebugFullRes = ReOpt("--re-fullres") == "1",
            CameraKeys = new() { new ExportCameraKey((tin + tout) / 2f - 1f, "Auto"), new ExportCameraKey((tin + tout) / 2f + 1f, "TopDown") }, DefaultCamera = "TopDown",
        };
        if (ReOpt("--re-camera-plan") is string cameraPlan)
        {
            var supplied = ExportPlan.FromJson(File.ReadAllText(cameraPlan)).Request;
            req = req with { CameraKeys = supplied.CameraKeys.ToList(), DefaultCamera = supplied.DefaultCamera };
        }
        // A release template cannot select PhysicsTest.tscn on its command line. Keep the
        // test parent in the editor and exercise the real packaged renderer/bootstrap instead.
        string renderer = ReOpt("--re-renderer");
        var env = new ExportEnvironment(renderer ?? OS.GetExecutablePath(), renderer == null && OS.HasFeature("editor") ? ProjectSettings.GlobalizePath("res://").TrimEnd('/') : null, Path.Combine(outDir, "jobs"), outDir, null, null, System.Environment.ProcessId);
        var source = new RaceScene.SpExportSource(lap, $"{track} test");
        var control = new ExportControl();
        var sw = Stopwatch.StartNew();
        var job = ExportJob.Start(source, req, control, env);
        int lastPrint = -1;
        while (!job.Done.IsCompleted)
        {
            await ToSignal(GetTree().CreateTimer(1.0), SceneTreeTimer.SignalName.Timeout);
            var p = job.Progress; if ((int)sw.Elapsed.TotalSeconds / 5 != lastPrint) { lastPrint = (int)sw.Elapsed.TotalSeconds / 5; GD.Print($"  progress: {p.Phase} {p.FramesDone}/{p.FramesTotal} {p.Message} ({sw.Elapsed.TotalSeconds:F0} s)"); }
            if (sw.Elapsed.TotalSeconds > 1500) { control.Cancel(); break; }
        }
        await job.Done;
        var res = job.Progress;
        GD.Print($"  result: {res.Phase} '{res.Message}' output {res.OutputPath} log {res.LogPath} in {sw.Elapsed.TotalSeconds:F0} s");
        if (ReOpt("--re-expect-camera-refusal") == "1")
        {
            ReCheck(res.Phase == ExportPhase.Failed && res.Message.Contains("DOES NOT SUPPORT FREE CAMERA KEYS"), "legacy renderer is visibly refused instead of substituting a preset view");
            ReCheck(lap.Sha256() == srcSha && before.All(kv => Sha(Path.Combine(lapDir, kv.Key)) == kv.Value), "refused renderer leaves original recording and edits unchanged");
            ReCheck(!Directory.Exists(Path.Combine(outDir, "jobs", job.JobId)) && !Directory.GetFiles(outDir, "*.part*").Any(), "refused renderer cleans owned processes, job and parts");
            return;
        }
        ReCheck(res.Phase == ExportPhase.Done && File.Exists(res.OutputPath), $"the export finished: {res.Phase} {res.Message}");
        var after = Directory.GetFiles(lapDir).ToDictionary(f => Path.GetFileName(f), Sha);
        ReCheck(before.Count == after.Count && before.All(kv => after.GetValueOrDefault(kv.Key) == kv.Value) && lap.Sha256() == srcSha, $"the original lap files are unchanged ({before.Count} files, source SHA-256 {srcSha[..12]})");
        ReCheck(!Directory.Exists(Path.Combine(outDir, "jobs", job.JobId)), "the job-owned folder is gone");
        ReCheck(!Directory.GetFiles(outDir, "*.part*").Any(), "no .part file is left");
        if (res.Phase != ExportPhase.Done) return;
        // ffprobe
        var psi = new ProcessStartInfo("ffprobe") { RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var a in new[] { "-v", "error", "-show_streams", "-show_format", "-of", "json", res.OutputPath }) psi.ArgumentList.Add(a);
        using var pr = Process.Start(psi); string json = await pr.StandardOutput.ReadToEndAsync(); await pr.WaitForExitAsync();
        File.WriteAllText(res.OutputPath + ".ffprobe.json", json);
        using var doc = JsonDocument.Parse(json); var streams = doc.RootElement.GetProperty("streams").EnumerateArray().ToList();
        var v = streams.First(s => s.GetProperty("codec_type").GetString() == "video");
        int n = req.FrameCount; double span = tout - tin;
        ReCheck(v.GetProperty("codec_name").GetString() == "h264" && v.GetProperty("pix_fmt").GetString() == "yuv420p", "H.264 yuv420p");
        ReCheck(v.GetProperty("width").GetInt32() == req.OutWidth && v.GetProperty("height").GetInt32() == req.OutHeight, $"{req.OutWidth}x{req.OutHeight}");
        ReCheck(v.GetProperty("nb_frames").GetString() == n.ToString() && v.GetProperty("r_frame_rate").GetString() == "60/1", $"exactly {n} frames at 60 fps (nb_frames {v.GetProperty("nb_frames").GetString()})");
        ReCheck(Math.Abs(double.Parse(v.GetProperty("duration").GetString(), System.Globalization.CultureInfo.InvariantCulture) - span) < 0.001, $"video duration {v.GetProperty("duration").GetString()} s = {span} s");
        var dec = await ReDecode(res.OutputPath, req.OutWidth, req.OutHeight, new[] { 0, n / 2, n - 1 });
        ReCheck(dec.FrameDev.All(d => d > 3), $"decoded frames 0, {n / 2}, {n - 1} are full pictures (no grey corner, not flat): deviations {string.Join(",", dec.FrameDev)}");
        if (audio) ReCheck(dec.AudioRms > 0.02 && dec.AudioPeak > 0.1, $"the exported audio has real signal (RMS {dec.AudioRms:0.0000}, peak {dec.AudioPeak:0.000})");
        if (audio)
        {
            var a = streams.First(s => s.GetProperty("codec_type").GetString() == "audio");
            ReCheck(a.GetProperty("codec_name").GetString() == "aac" && a.GetProperty("sample_rate").GetString() == "48000" && a.GetProperty("channels").GetInt32() == 2, "AAC 48 kHz stereo");
            ReCheck(Math.Abs(double.Parse(a.GetProperty("duration").GetString(), System.Globalization.CultureInfo.InvariantCulture) - span) < 0.03, $"audio duration {a.GetProperty("duration").GetString()} s = {span} s (AAC priming)");
        }
    }
}

public partial class PhysicsTest
{
    /// <summary>Decoded picture and sound checks (not just container metadata): corners are never the engine's grey, frames differ over time, audio has real signal.</summary>
    static async Task<(double AudioRms, double AudioPeak, int[] FrameDev)> ReDecode(string mp4, int width, int height, int[] frames)
    {
        var psi = new ProcessStartInfo("ffmpeg") { RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var a in new[] { "-v", "error", "-i", mp4, "-vn", "-f", "s16le", "-ar", "48000", "-ac", "1", "-" }) psi.ArgumentList.Add(a);
        using var pa = Process.Start(psi); var ms = new MemoryStream(); await pa.StandardOutput.BaseStream.CopyToAsync(ms); await pa.WaitForExitAsync();
        var bytes = ms.ToArray(); double sum = 0, peak = 0; int n = bytes.Length / 2;
        for (int i = 0; i < n; i++) { double v = BitConverter.ToInt16(bytes, i * 2) / 32768.0; sum += v * v; peak = Math.Max(peak, Math.Abs(v)); }
        var devs = new List<int>();
        foreach (int f in frames)
        {
            var p2 = new ProcessStartInfo("ffmpeg") { RedirectStandardOutput = true, UseShellExecute = false };
            foreach (var a in new[] { "-v", "error", "-i", mp4, "-vf", $"select=eq(n\\,{f})", "-vframes", "1", "-f", "rawvideo", "-pix_fmt", "rgb24", "-" }) p2.ArgumentList.Add(a);
            using var pv = Process.Start(p2); var fm = new MemoryStream(); await pv.StandardOutput.BaseStream.CopyToAsync(fm); await pv.WaitForExitAsync();
            var px = fm.ToArray(); if (px.Length < width * height * 3) { devs.Add(-1); continue; }
            double mean = 0; for (int i = 0; i < px.Length; i += 3 * 97) mean += px[i + 1]; mean /= (px.Length / (3 * 97));
            double var = 0; for (int i = 0; i < px.Length; i += 3 * 97) var += (px[i + 1] - mean) * (px[i + 1] - mean); var /= (px.Length / (3 * 97));
            // the four corners must not be the engine's flat grey (76,76,76)
            bool grey = false; foreach (var (cx, cy) in new[] { (3, 3), (width - 4, 3), (3, height - 4), (width - 4, height - 4) }) { int o = (cy * width + cx) * 3; if (px[o] == 76 && px[o + 1] == 76 && px[o + 2] == 76) grey = true; }
            devs.Add(grey ? -2 : (int)Math.Sqrt(var));
        }
        return (Math.Sqrt(sum / Math.Max(1, n)), peak, devs.ToArray());
    }

    async Task ReplayExportFailureTests()
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("The export failure diagnostic requires Linux shell fixtures and Unix file permissions.");
        string outDir = ReOpt("--re-out-dir") ?? Path.Combine(TapeProjectDir, "media", "replay-export-20261028"); Directory.CreateDirectory(outDir);
        string fx = ReOpt("--re-fx") ?? "/tmp/cccp-lc-fx";
        var box = LapArchive.For(Path.Combine(fx, "ilyich")); var info = box.List().First(i => i.Openable); var lap = box.Open(info.Id);
        var before = Directory.GetFiles(info.Dir).ToDictionary(f => Path.GetFileName(f), Sha);
        string proj = OS.HasFeature("editor") ? ProjectSettings.GlobalizePath("res://").TrimEnd('/') : null;
        ExportEnvironment Env(string ff = null) => new(OS.GetExecutablePath(), proj, Path.Combine(outDir, "jobs"), outDir, null, ff, System.Environment.ProcessId);
        ExportRequest Req(string name, float a = 10, float b = 13) => new() { InClock = a, OutClock = b, OutHeight = 540, Name = name, DestinationDir = outDir };
        var src = new RaceScene.SpExportSource(lap, "failure test");
        async Task<ExportProgress> Run(ExportRequest r, ExportEnvironment env, Action<ExportJob, ExportControl> during = null)
        {
            var c = new ExportControl(); var j = ExportJob.Start(src, r, c, env); var sw = Stopwatch.StartNew();
            while (!j.Done.IsCompleted) { await ToSignal(GetTree().CreateTimer(0.2), SceneTreeTimer.SignalName.Timeout); during?.Invoke(j, c); if (sw.Elapsed.TotalSeconds > 300) { c.Cancel(); break; } }
            await j.Done; return j.Progress;
        }
        // invalid requests are refused before anything starts
        foreach (var (name, r, text) in new (string, ExportRequest, string)[] {
            ("NaN in", Req("bad1") with { InClock = float.NaN }, "NOT A NUMBER"), ("infinity out", Req("bad2") with { OutClock = float.PositiveInfinity }, "NOT A NUMBER"),
            ("fps 0", Req("bad3") with { Fps = 0 }, "FRAME RATE"), ("fps 45", Req("bad4") with { Fps = 45 }, "FRAME RATE"), ("height 480", Req("bad5") with { OutHeight = 480 }, "SIZE"),
            ("before the start", Req("bad6", -5, 3), "OUTSIDE"), ("after the end", Req("bad7", 10, 99999), "OUTSIDE"), ("empty", Req("bad8", 12, 12), "EMPTY"), ("reversed", Req("bad9", 14, 12), "EMPTY") })
        {
            var p = await Run(r, Env()); ReCheck(p.Phase == ExportPhase.Failed && p.Message.Contains(text), $"refused up front: {name} -> {p.Message}");
        }
        ReCheck(new ExportRequest { InClock = float.NaN, OutClock = 5, Fps = 0 }.FrameCount == 0, "FrameCount is safe for NaN / fps 0");
        // FFmpeg missing and a configured ffmpeg that never answers
        var saved = System.Environment.GetEnvironmentVariable("PATH");
        System.Environment.SetEnvironmentVariable("PATH", "/nonexistent");
        var pm = await Run(Req("noffmpeg"), Env()); System.Environment.SetEnvironmentVariable("PATH", saved);
        ReCheck(pm.Phase == ExportPhase.Failed && pm.Message.Contains("FFMPEG NOT FOUND"), $"missing FFmpeg: {pm.Message}");
        string stub = Path.Combine(outDir, "fake-ffmpeg-hang.sh"); File.WriteAllText(stub, "#!/bin/sh\nsleep 600\n"); File.SetUnixFileMode(stub, (UnixFileMode)0b111_101_101);
        var sw0 = Stopwatch.StartNew(); string ffErr; string found = FfmpegLocator.Find(null, stub, out ffErr, 1500); sw0.Stop();
        ReCheck(found == null && sw0.ElapsedMilliseconds < 6000 && ffErr.Contains("DID NOT ANSWER"), $"a configured ffmpeg that never exits is refused in {sw0.ElapsedMilliseconds} ms (no silent fall back to another ffmpeg): {ffErr}");
        string stub2 = Path.Combine(outDir, "fake-ffmpeg-inherited-pipes.sh"); File.WriteAllText(stub2, "#!/bin/sh\nsleep 30 &\nexit 0\n"); File.SetUnixFileMode(stub2, (UnixFileMode)0b111_101_101);
        sw0.Restart(); found = FfmpegLocator.Find(null, stub2, out ffErr, 1500); sw0.Stop();
        ReCheck(found == null && sw0.ElapsedMilliseconds < 6000 && ffErr != null, $"a configured ffmpeg that exits but leaves a child holding its pipes is refused in {sw0.ElapsedMilliseconds} ms: {ffErr}");
        // disk preflight
        ExportJob.FreeSpaceOverride = _ => 50L * 1024 * 1024;
        var pd = await Run(Req("nodisk"), Env()); ExportJob.FreeSpaceOverride = null;
        ReCheck(pd.Phase == ExportPhase.Failed && pd.Message.Contains("NOT ENOUGH DISK SPACE"), $"disk preflight: {pd.Message}");
        // cancel in the middle of rendering: Cancelled, no output, no .part, job folder gone, child process gone
        int pid = 0; string cname = "cancelled";
        var pc = await Run(Req(cname, 10, 40), Env(), (j, c) => { if (pid == 0 && j.Progress.Phase == ExportPhase.Rendering && j.Progress.FramesDone > 30) { pid = j.ChildPid; c.Cancel(); } });
        await Task.Delay(500);
        ReCheck(pc.Phase == ExportPhase.Cancelled && pid != 0, $"cancel during rendering: {pc.Phase} (child pid {pid})");
        ReCheck(pid != 0 && !Directory.Exists("/proc/" + pid), "the exporter child is gone after cancel");
        ReCheck(!File.Exists(Path.Combine(outDir, cname + ".mp4")) && !Directory.GetFiles(outDir, ".*.part*").Any() && !Directory.GetFiles(outDir, "*.part").Any(), "no output and no .part file after cancel");
        // the child killed from outside mid-render: a visible failure, clean folders
        pid = 0;
        var pk = await Run(Req("killed", 10, 40), Env(), (j, c) => { if (pid == 0 && j.Progress.Phase == ExportPhase.Rendering && j.Progress.FramesDone > 30) { pid = j.ChildPid; try { Process.GetProcessById(pid).Kill(true); } catch { } } });
        ReCheck(pk.Phase == ExportPhase.Failed && pk.Error, $"the exporter child killed mid-render fails visibly: {pk.Message}");
        ReCheck(pk.LogPath != null && File.Exists(pk.LogPath), "a failure log was kept next to the output");
        // nothing job-owned remains; the original is untouched
        ReCheck(!Directory.Exists(Path.Combine(outDir, "jobs")) || !Directory.GetDirectories(Path.Combine(outDir, "jobs")).Any(), "no job folder remains after any of these");
        var after = Directory.GetFiles(info.Dir).ToDictionary(f => Path.GetFileName(f), Sha);
        ReCheck(before.Count == after.Count && before.All(kv => after.GetValueOrDefault(kv.Key) == kv.Value), "the original lap files are unchanged after all of them");
    }
}
