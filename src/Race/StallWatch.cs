using System;
using System.IO;
using System.Threading;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// A watchdog for a frozen game (2026-11-19, a Steam Deck froze for good while a new track loaded). A background thread watches the heartbeat the main thread gives every frame (RaceScene._Process);
/// when it goes quiet for <see cref="ThresholdMs"/> it writes what the main thread was last known to do (the loading checkpoint, a wait for a save) and the tape save worker's phase to the log and to
/// user://stall.log (read with a plain file manager after the game was killed), again every <see cref="RepeatMs"/> while the freeze lasts, and one line when the main thread wakes up. Nothing here
/// touches the game's state: the thread only reads a few fields. It never ends the freeze, it only says where it is.
/// </summary>
public static class StallWatch
{
    public static int ThresholdMs = 8000, RepeatMs = 5000;      // (8 s: the start of the game is silent for about 5 s on a laptop, which is not a freeze)
    const int MaxLines = 300;
    static long _beat = System.Environment.TickCount64;
    static volatile string _where = "";
    static volatile Func<string> _detail;
    static volatile string _track = "";
    static volatile int _loadSteps, _loadLine;
    static string _path;
    static int _lines;
    static Thread _thread;
    static volatile bool _stop;

    /// <summary>The main thread is alive (every frame).</summary>
    public static void Beat() => Volatile.Write(ref _beat, System.Environment.TickCount64);

    /// <summary>The loading build passed a checkpoint (the track, the checkpoint count, the source line of the call).</summary>
    public static void Load(string track, int steps, int line) { _track = track ?? ""; _loadSteps = steps; _loadLine = line; }

    /// <summary>The main thread begins to block on something (a wait for a save): what it is, and an optional detail the watchdog reads while the wait lasts. Pass null when the wait ends.</summary>
    public static void Waiting(string where, Func<string> detail = null) { _detail = detail; _where = where ?? ""; }

    public static void Start(string userDir, int thresholdMs = 0, int repeatMs = 0)
    {
        if (_thread != null) return;
        if (thresholdMs > 0) ThresholdMs = thresholdMs;
        if (repeatMs > 0) RepeatMs = repeatMs;
        _path = Path.Combine(userDir, "stall.log");
        _stop = false; Beat();
        _thread = new Thread(Run) { IsBackground = true, Name = "StallWatch" };
        _thread.Start();
    }

    public static void Stop() { _stop = true; _thread = null; }

    static void Run()
    {
        long reportedAt = 0; bool inStall = false; long stallStart = 0; int rounds = 0;
        while (!_stop)
        {
            Thread.Sleep(250);
            long now = System.Environment.TickCount64, quiet = now - Volatile.Read(ref _beat);
            if (quiet >= ThresholdMs)
            {
                if (!inStall) { inStall = true; stallStart = now - quiet; reportedAt = 0; rounds = 0; }
                if (reportedAt == 0 || now - reportedAt >= RepeatMs)
                {
                    reportedAt = now; rounds++;
                    string detail = "";
                    try { detail = _detail?.Invoke() ?? ""; } catch (Exception ex) { detail = "(detail failed: " + ex.GetType().Name + ")"; }
                    Write($"STALL #{rounds}: the main thread has been silent for {quiet / 1000.0:F1} s. Loading: track '{_track}', checkpoint {_loadSteps}, last checkpoint at RaceScene line {_loadLine}. "
                        + $"Waiting: '{(_where.Length == 0 ? "nothing known" : _where)}'{(detail.Length > 0 ? " (" + detail + ")" : "")}. Memory: {System.Environment.WorkingSet / 1048576} MB working set, {GC.GetTotalMemory(false) / 1048576} MB managed, GC gen0/1/2 {GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}.");
                }
            }
            else if (inStall)
            {
                inStall = false;
                Write($"STALL over: the main thread is running again after {(now - stallStart) / 1000.0:F1} s (it was waiting: '{(_where.Length == 0 ? "nothing known" : _where)}').");
            }
        }
    }

    static void Write(string text)
    {
        if (Interlocked.Increment(ref _lines) > MaxLines) return;
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {text}";
        try { GD.Print("StallWatch: " + line); } catch { }
        try { File.AppendAllText(_path, line + "\n"); } catch { }
    }
}
