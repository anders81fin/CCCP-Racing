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
    public static int ThresholdMs = 8000, RepeatMs = 5000, DumpAfterMs = 120000;      // (8 s: the start of the game is silent for about 5 s on a laptop, which is not a freeze)
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
    public static void Beat()
    {
        long now = System.Environment.TickCount64, gap = now - Volatile.Read(ref _beat);
        Volatile.Write(ref _beat, now);
        if (gap >= HitchMs && gap < ThresholdMs && _path != null) Hitch(gap);
    }

    /// <summary>A frame longer than this is written to stall.log as a HITCH (with what the game was showing): a menu that "reads the key two seconds late" is a frame that took two seconds.</summary>
    public static int HitchMs = 400;
    /// <summary>What the game is showing now (the state, the card, the menu row): read only when a frame was long. Set by RaceScene.</summary>
    public static Func<string> ContextProvider;
    static int _hitches;
    static void Hitch(long gapMs)
    {
        if (++_hitches > 120) return;
        string ctx = ""; try { ctx = ContextProvider?.Invoke() ?? ""; } catch { }
        Write($"HITCH: a frame took {gapMs} ms ({ctx}); load '{_track}' checkpoint {_loadSteps}");
    }

    /// <summary>The loading build passed a checkpoint (the track, the checkpoint count, the source line of the call).</summary>
    public static void Load(string track, int steps, int line)
    {
        _track = track ?? ""; _loadSteps = steps; _loadLine = line;
        if (steps > 0 && steps % 25 == 0 && _path != null) Write($"LOAD track '{_track}' checkpoint {steps} (line {line}): {GpuState()}; managed {GC.GetTotalMemory(false) / 1048576} MB, working set {System.Environment.WorkingSet / 1048576} MB");      // (a trace of the GPU and memory through every load, frozen or not: stall.log)
    }

    /// <summary>The main thread begins to block on something (a wait for a save): what it is, and an optional detail the watchdog reads while the wait lasts. Pass null when the wait ends.</summary>
    public static void Waiting(string where, Func<string> detail = null) { _detail = detail; _where = where ?? ""; }

    public static void Start(string userDir, int thresholdMs = 0, int repeatMs = 0)
    {
        if (_thread != null) return;
        if (thresholdMs > 0) ThresholdMs = thresholdMs;
        if (repeatMs > 0) RepeatMs = repeatMs;
        _path = Path.Combine(userDir, "stall.log");
        _stop = false; Beat();
        try { if (OperatingSystem.IsLinux()) Prctl(0x59616d61, -1, 0, 0, 0); } catch { }      // PR_SET_PTRACER(ANY): the kernel's ptrace rule lets only a parent trace a thread; this lets the watchdog thread sample the other threads of its own process
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
                    detail += (detail.Length > 0 ? "; " : "") + ProcessState();
                    if (rounds <= 3 || rounds % 12 == 0) { try { Write(SampleThreads()); } catch (Exception ex) { Write("(thread sample failed: " + ex.GetType().Name + ": " + ex.Message + ")"); } }
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

    /// <summary>An AMD GPU's own counters (the Steam Deck's): how busy it is and how much video memory is used of the total, and the system's free memory. A GPU that is 100 % busy for minutes, or memory that is full, points at the driver, not at the game's code.</summary>
    static string GpuState()
    {
        var sb = new System.Text.StringBuilder();
        try
        {
            foreach (var dev in Directory.GetDirectories("/sys/class/drm", "card?"))
            {
                string d = dev + "/device"; string Rd(string f) { try { return File.ReadAllText(d + "/" + f).Trim(); } catch { return null; } }
                string busy = Rd("gpu_busy_percent"), used = Rd("mem_info_vram_used"), tot = Rd("mem_info_vram_total"), gtt = Rd("mem_info_gtt_used"), gttTot = Rd("mem_info_gtt_total");
                if (busy == null && used == null) continue;
                string Mb(string v) => long.TryParse(v, out long b) ? (b / 1048576) + " MB" : "?";
                sb.Append($"{Path.GetFileName(dev)}: GPU busy {busy ?? "?"} %, VRAM {Mb(used)} of {Mb(tot)}, GTT {Mb(gtt)} of {Mb(gttTot)}; ");
            }
            foreach (var line in File.ReadLines("/proc/meminfo")) if (line.StartsWith("MemAvailable:")) { sb.Append("system " + line.Trim()); break; }
        }
        catch { }
        return sb.Length == 0 ? "" : sb.ToString().TrimEnd(' ', ';');
    }

    static volatile int _spinnerTid;
    static bool _dumped;
    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "prctl", SetLastError = true)] static extern int Prctl(int option, long arg2, long arg3, long arg4, long arg5);
    /// <summary>Linux x86-64 only: where the busy thread and the main thread are, and which library or the executable each is in. A small helper program (tools/stallprobe, 9 KB, written here from the string below) attaches to one thread
    /// for a moment (ptrace from outside this process: the kernel does not let a thread attach to its own process's threads), reads the instruction pointer and the words on the stack, and detaches; the return addresses on the
    /// stack are looked up in /proc/self/maps. A busy thread inside the graphics driver's compiler looks different from one in the engine or in the game's own code. Nothing is changed in either thread.</summary>
    static string SampleThreads()
    {
        if (!OperatingSystem.IsLinux() || System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64) return "";
        string exe = Path.Combine(Path.GetDirectoryName(_path) ?? ".", "stallprobe");
        if (!File.Exists(exe)) { File.WriteAllBytes(exe, Convert.FromBase64String(ProbeBase64)); File.SetUnixFileMode(exe, (UnixFileMode)0b111_101_101); }
        int pid = System.Environment.ProcessId; var maps = new System.Collections.Generic.List<(ulong a, ulong b, bool x, ulong off, string path)>();
        foreach (var line in File.ReadLines("/proc/self/maps"))
        {
            var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries); if (p.Length < 5) continue;
            var r = p[0].Split('-'); maps.Add((Convert.ToUInt64(r[0], 16), Convert.ToUInt64(r[1], 16), p[1].Contains('x'), Convert.ToUInt64(p[2], 16), p.Length >= 6 ? p[5] : ""));
        }
        string Where(ulong addr) { foreach (var m in maps) if (addr >= m.a && addr < m.b) return (m.path.Length == 0 ? "anon" : Path.GetFileName(m.path)) + "+0x" + (addr - m.a + m.off).ToString("x"); return "?"; }
        bool Exec(ulong addr) { foreach (var m in maps) if (addr >= m.a && addr < m.b) return m.x && m.path.Length > 0; return false; }
        var sb = new System.Text.StringBuilder("THREAD SAMPLE: ");
        foreach (int tid in new[] { _spinnerTid, pid })
        {
            if (tid == 0) continue;
            sb.Append(tid == pid ? "main " : "busy ").Append("tid ").Append(tid).Append(": ");
            var psi = new System.Diagnostics.ProcessStartInfo(exe, $"{tid} /proc/{pid}/mem") { RedirectStandardOutput = true, UseShellExecute = false };
            using var proc = System.Diagnostics.Process.Start(psi);
            string text = proc.StandardOutput.ReadToEnd(); if (!proc.WaitForExit(3000)) { try { proc.Kill(); } catch { } }
            if (!text.StartsWith("rip=")) { sb.Append(text.Trim()).Append("; "); continue; }
            var lines = text.Split('\n'); ulong Hex(string t) => Convert.ToUInt64(t.Trim().Split('=')[^1].Replace("0x", ""), 16);
            var head = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            sb.Append("rip ").Append(Where(Hex(head[0]))).Append(" | stack: ");
            int shown = 0;
            foreach (var w in lines[1].Replace("stack:", "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                ulong v = Convert.ToUInt64(w.Replace("0x", ""), 16);
                if (Exec(v)) { sb.Append(Where(v)).Append(' '); if (++shown >= 16) break; }
            }
            sb.Append("; ");
        }
        return sb.ToString().TrimEnd(' ', ';');
    }

    const string ProbeBase64 = "f0VMRgIBAQAAAAAAAAAAAAIAPgABAAAAABBAAAAAAABAAAAAAAAAAEAhAAAAAAAAAAAAAEAAOAAIAEAACQAIAAEAAAAEAAAAAAAAAAAAAAAAAEAAAAAAAAAAQAAAAAAAJAIAAAAAAAAkAgAAAAAAAAAQAAAAAAAAAQAAAAUAAAAAEAAAAAAAAAAQQAAAAAAAABBAAAAAAAAnBAAAAAAAACcEAAAAAAAAABAAAAAAAAABAAAABAAAAAAgAAAAAAAAACBAAAAAAAAAIEAAAAAAAMgAAAAAAAAAyAAAAAAAAAAAEAAAAAAAAAEAAAAGAAAAABAAAAAAAAAAMEAAAAAAAAAwQAAAAAAAAAAAAAAAAAAAIAAAAAAAAAAQAAAAAAAABAAAAAQAAAAAAgAAAAAAAAACQAAAAAAAAAJAAAAAAAAkAAAAAAAAACQAAAAAAAAABAAAAAAAAAAEAAAABAAAAJggAAAAAAAAmCBAAAAAAACYIEAAAAAAADAAAAAAAAAAMAAAAAAAAAAIAAAAAAAAAFPldGQEAAAAmCAAAAAAAACYIEAAAAAAAJggQAAAAAAAMAAAAAAAAAAwAAAAAAAAAAgAAAAAAAAAUeV0ZAYAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAEAAAAAAAAAAEAAAAFAAAAAMAAABHTlUAPjxO/u/myPQD9vkVMLu11kB8LDkAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA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AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAB1c2FnZQoAYXR0YWNoLWZhaWxlZAoAZ2V0cmVncy1mYWlsZWQKAHJpcD0AcnNwPQByYnA9AApzdGFjazoAMDEyMzQ1Njc4OWFiY2RlZgAAABQAAAAAAAAAAXpSAAF4EAEbDAcIkAEAACgAAAAcAAAAsO///wcEAAAARw7YAUiOA2ODB4YGjAWNBAMPAwoOCEELAAAAAAAAAAQAAAAgAAAABQAAAEdOVQABAAHABAAAAAkAAAAAAAAAAgABwAQAAAABAAAAAAAAAEdDQzogKEdOVSkgMTYuMi4xIDIwMjYwODEwAAAuc2hzdHJ0YWIALm5vdGUuZ251LmJ1aWxkLWlkAC50ZXh0AC5yb2RhdGEALmVoX2ZyYW1lAC5ub3RlLmdudS5wcm9wZXJ0eQAuYnNzAC5jb21tZW50AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAALAAAABwAAAAIAAAAAAAAAAAJAAAAAAAAAAgAAAAAAACQAAAAAAAAAAAAAAAAAAAAEAAAAAAAAAAAAAAAAAAAAHgAAAAEAAAAGAAAAAAAAAAAQQAAAAAAAABAAAAAAAAAnBAAAAAAAAAAAAAAAAAAAIAAAAAAAAAAAAAAAAAAAACQAAAABAAAAMgAAAAAAAAAAIEAAAAAAAAAgAAAAAAAATgAAAAAAAAAAAAAAAAAAAAEAAAAAAAAAAQAAAAAAAAAsAAAAAQAAAAIAAAAAAAAAUCBAAAAAAABQIAAAAAAAAEQAAAAAAAAAAAAAAAAAAAAIAAAAAAAAAAAAAAAAAAAANgAAAAcAAAACAAAAAAAAAJggQAAAAAAAmCAAAAAAAAAwAAAAAAAAAAAAAAAAAAAACAAAAAAAAAAAAAAAAAAAAEkAAAAIAAAAAwAAAAAAAAAAMEAAAAAAAAAwAAAAAAAAACAAAAAAAAAAAAAAAAAAACAAAAAAAAAAAAAAAAAAAABOAAAAAQAAADAAAAAAAAAAAAAAAAAAAADIIAAAAAAAABsAAAAAAAAAAAAAAAAAAAABAAAAAAAAAAEAAAAAAAAAAQAAAAMAAAAAAAAAAAAAAAAAAAAAAAAA4yAAAAAAAABXAAAAAAAAAAAAAAAAAAAAAQAAAAAAAAAAAAAAAAAAAA==";

    static long _cpuPrev;
    /// <summary>Linux only: what the OS says the main thread (the thread whose id is the process id) and the busiest threads are doing: a thread state (R running, S sleeping, D waiting in the kernel, as on a GPU driver call), the kernel function it sleeps in and the CPU ticks it used since the last report. This tells a spinning loop from a wait on a driver or a lock.</summary>
    static string ProcessState()
    {
        try
        {
            if (!OperatingSystem.IsLinux()) return "";
            int pid = System.Environment.ProcessId; var sb = new System.Text.StringBuilder("OS view: ");
            string Stat(string path) { try { return File.ReadAllText(path); } catch { return ""; } }
            string Wchan(int tid) { try { return File.ReadAllText($"/proc/{pid}/task/{tid}/wchan").Trim(); } catch { return "?"; } }
            long total = 0, topRunning = -1; _spinnerTid = 0; var rows = new System.Collections.Generic.List<(long cpu, string text)>();
            foreach (var dir in Directory.GetDirectories($"/proc/{pid}/task"))
            {
                int tid = int.Parse(Path.GetFileName(dir)); string st = Stat(dir + "/stat"); int close = st.LastIndexOf(')'); if (close < 0) continue;
                string name = st[(st.IndexOf('(') + 1)..close]; var f = st[(close + 2)..].Split(' ');      // f[0] state, f[11] utime, f[12] stime
                long cpu = long.Parse(f[11]) + long.Parse(f[12]); total += cpu;
                rows.Add((cpu, $"{(tid == pid ? "MAIN " : "")}{name}[{f[0]}, {Wchan(tid)}, {cpu} ticks, tid {tid}]"));
                if (tid != pid && f[0] == "R" && name != "StallWatch" && cpu > topRunning) { topRunning = cpu; _spinnerTid = tid; }
            }
            long delta = total - _cpuPrev; _cpuPrev = total;
            sb.Append($"process CPU +{delta} ticks since the last report; ");
            foreach (var r in rows.FindAll(r => r.text.StartsWith("MAIN ")))
                sb.Append(r.text).Append("; ");
            rows.Sort((a, b) => b.cpu.CompareTo(a.cpu));
            int shown = 0; foreach (var r in rows) { if (r.text.StartsWith("MAIN ")) continue; sb.Append(r.text).Append("; "); if (++shown >= 5) break; }
            try { sb.Append("; main syscall: ").Append(File.ReadAllText($"/proc/{pid}/task/{pid}/syscall").Trim()); } catch { }
            sb.Append("; ").Append(GpuState());
            return sb.ToString().TrimEnd(' ', ';');
        }
        catch (Exception ex) { return "(OS view failed: " + ex.GetType().Name + ")"; }
    }

    static void Write(string text)
    {
        if (Interlocked.Increment(ref _lines) > MaxLines) return;
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {text}";
        try { GD.Print("StallWatch: " + line); } catch { }
        try { File.AppendAllText(_path, line + "\n"); } catch { }
    }
}
