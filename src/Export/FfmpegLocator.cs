using System;
using System.Diagnostics;
using System.IO;

namespace CCCPRacing.Export;

/// <summary>Finds FFmpeg (PATH, then next to the game, then a configured path) and checks that it has libx264 and AAC. FFmpeg is NOT bundled (libx264 is GPL): a missing one is a clear message, nothing else.</summary>
public static class FfmpegLocator
{
    public const string MissingText = "FFMPEG NOT FOUND - INSTALL FFMPEG WITH H.264 AND AAC";

    public static string Find(string gameDir, string configured, out string error, int probeTimeoutMs = 15000)
    {
        error = null;
        string exe = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        var candidates = new System.Collections.Generic.List<string>();
        // an explicitly configured ffmpeg is the only candidate: a broken one is a clear error, never a silent fall back to another binary
        if (!string.IsNullOrWhiteSpace(configured)) candidates.Add(configured);
        else
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            candidates.Add(Path.Combine(dir, exe));
        if (string.IsNullOrWhiteSpace(configured) && !string.IsNullOrEmpty(gameDir)) candidates.Add(Path.Combine(gameDir, exe));
        foreach (var c in candidates)
        {
            try { if (!File.Exists(c)) continue; } catch { continue; }
            string why = Probe(c, probeTimeoutMs);
            if (why == null) return c;
            error = why;
        }
        error ??= MissingText;
        return null;
    }

    /// <summary>null when this ffmpeg can encode what we need; else the reason.</summary>
    static string Probe(string ffmpeg, int timeoutMs)
    {
        try
        {
            var psi = new ProcessStartInfo(ffmpeg) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            psi.ArgumentList.Add("-hide_banner"); psi.ArgumentList.Add("-encoders");
            using var p = Process.Start(psi);
            // both pipes are drained asynchronously and the wait is bounded: a stub that never exits (or never closes its pipes) cannot hang the export
            var so = new System.Text.StringBuilder(); var eof = new System.Threading.CountdownEvent(2);
            p.OutputDataReceived += (_, e) => { if (e.Data == null) { try { eof.Signal(); } catch { } } else lock (so) so.AppendLine(e.Data); };
            p.ErrorDataReceived += (_, e) => { if (e.Data == null) { try { eof.Signal(); } catch { } } };
            p.BeginOutputReadLine(); p.BeginErrorReadLine();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (!p.WaitForExit(timeoutMs)) { try { p.Kill(true); } catch { } return "FFMPEG DID NOT ANSWER"; }
            // the process is gone, but a child it spawned may still hold its pipes: the drain has the remaining time only, then the readers are cancelled (never an unbounded wait for EOF)
            if (!eof.Wait(Math.Max(300, timeoutMs - (int)sw.ElapsedMilliseconds))) { try { p.CancelOutputRead(); } catch { } try { p.CancelErrorRead(); } catch { } }
            string o; lock (so) o = so.ToString();
            if (!o.Contains("libx264")) return "FFMPEG HAS NO H.264 ENCODER (LIBX264)";
            if (!o.Contains(" aac ")) return "FFMPEG HAS NO AAC ENCODER";
            return null;
        }
        catch (Exception e) { return "CANNOT RUN FFMPEG: " + e.Message.ToUpperInvariant(); }
    }
}
