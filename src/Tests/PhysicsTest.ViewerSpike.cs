using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Godot;

namespace CCCPRacing.Tests;

// --viewer-spike-tests: phase 2 measurement spike 1 (nested World3D build time, RSS, leak deltas, statics). One headless child game at a time
// (TapeGame helper, own /tmp/cccp-spike1-* data dir and XDG dir); the child (RaceScene.ViewerSpike.cs, --viewer-spike=MODE) prints VSPIKE| lines.
// Options: --viewer-spike-log=FILE (default viewer-spike-measurements.log)  --viewer-spike-only=TRACK  --viewer-spike-reps=N (default 3)  --viewer-spike-extra=0 (skip the control/cross/nounsub runs)
// The raw log and the tables go to <project>/viewer-spike-measurements.log.
public partial class PhysicsTest
{
    static readonly string[] SpikeTracks = { "ilyich", "siberia", "atomgrad", "bonus" };

    sealed class SpikeRun
    {
        public string Label, Track, Mode; public int Exit; public double Seconds; public bool TimedOut; public string LoadBefore, Contended;
        public List<Dictionary<string, string>> Rows = new(); public List<string> Errors = new(); public string[] Raw;
        public IEnumerable<Dictionary<string, string>> Of(string kind) => Rows.Where(r => r["kind"] == kind);
        public Dictionary<string, string> Snap(string phase, int cycle) => Rows.FirstOrDefault(r => r["kind"] == "snap" && r["phase"] == phase && r["cycle"] == cycle.ToString());
        public double N(string kind, string key) => double.Parse(Of(kind).First()[key], CultureInfo.InvariantCulture);
    }

    static double SpikeD(Dictionary<string, string> r, string k) => r != null && r.TryGetValue(k, out var v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : double.NaN;
    static string SpikeStat(IEnumerable<double> xs, string fmt = "0")
    {
        var l = xs.Where(x => !double.IsNaN(x)).OrderBy(x => x).ToList();
        if (l.Count == 0) return "n/a";
        double med = l.Count % 2 == 1 ? l[l.Count / 2] : (l[l.Count / 2 - 1] + l[l.Count / 2]) / 2;
        return $"{l[0].ToString(fmt, CultureInfo.InvariantCulture)} / {med.ToString(fmt, CultureInfo.InvariantCulture)} / {l[^1].ToString(fmt, CultureInfo.InvariantCulture)} (n={l.Count})";
    }

    static string SpikePower()
    {
        string Rd(string f) { try { return File.ReadAllText(f).Trim(); } catch { return "?"; } }
        string ac = "?"; try { foreach (var d in Directory.GetDirectories("/sys/class/power_supply")) if (Rd(d + "/type") == "Mains") ac = Rd(d + "/online") == "1" ? "AC" : "BATTERY"; } catch { }
        double mhz = 0; int n = 0; try { foreach (var l in File.ReadLines("/proc/cpuinfo")) if (l.StartsWith("cpu MHz")) { mhz += double.Parse(l.Split(':')[1], CultureInfo.InvariantCulture); n++; } } catch { }
        return $"power={ac} governor={Rd("/sys/devices/system/cpu/cpu0/cpufreq/scaling_governor")} cpu_mhz_avg={(n > 0 ? mhz / n : 0):0}";
    }

    /// <summary>Wait (up to ~4 min) until no other Godot process is running; returns a note when it still was not quiet.</summary>
    static string SpikeWaitIdle()
    {
        for (int i = 0; i < 48; i++)
        {
            var others = new List<string>();
            foreach (var d in Directory.GetDirectories("/proc"))
            {
                var name = Path.GetFileName(d);
                if (!int.TryParse(name, out int pid) || pid == System.Environment.ProcessId) continue;
                try { var comm = File.ReadAllText(d + "/comm").Trim(); if (comm.Contains("godot", StringComparison.OrdinalIgnoreCase)) others.Add($"{pid}:{comm}"); } catch { }
            }
            if (others.Count == 0) return null;
            if (i == 47) return "NOT QUIET: " + string.Join(",", others);
            System.Threading.Thread.Sleep(5000);
        }
        return null;
    }

    void ViewerSpikeTests()
    {
        GD.Print("Viewer spike 1 (nested World3D measurements; one headless child at a time)");
        if (!OperatingSystem.IsLinux()) { GD.Print("viewer spike: Linux only"); return; }
        string logPath = Path.Combine(TapeProjectDir, OS.GetCmdlineUserArgs().FirstOrDefault(x => x.StartsWith("--viewer-spike-log="))?["--viewer-spike-log=".Length..] ?? "viewer-spike-measurements.log");
        string only = OS.GetCmdlineUserArgs().FirstOrDefault(x => x.StartsWith("--viewer-spike-only="))?["--viewer-spike-only=".Length..];
        int reps = int.Parse(OS.GetCmdlineUserArgs().FirstOrDefault(x => x.StartsWith("--viewer-spike-reps="))?["--viewer-spike-reps=".Length..] ?? "3");
        bool extra = !OS.GetCmdlineUserArgs().Contains("--viewer-spike-extra=0");
        var sb = new StringBuilder();
        void Out(string s) { GD.Print(s); sb.AppendLine(s); File.WriteAllText(logPath, sb.ToString()); }
        Out($"# viewer-spike-measurements.log, {DateTime.Now:O}");
        Out($"# engine {Engine.GetVersionInfo()["string"]}, executable {OS.GetExecutablePath()}, cpu {OS.GetProcessorName()} x{OS.GetProcessorCount()}, os {OS.GetName()}");
        Out("# children: --headless --fixed-fps 60 (dummy renderer and dummy audio: NO GPU memory, NO render time, NO audio output); live scene ProcessMode.Disabled+hidden while the nested one runs 120 frames, then QueueFree + 2-3 frames");

        var runs = new List<SpikeRun>();
        var plan = new List<(string label, string track, string mode)>();
        foreach (var t in SpikeTracks.Where(t => only == null || t == only))
        {
            for (int r = 1; r <= reps; r++) plan.Add(($"{t}-run{r}", t, "run"));
            if (extra)
            {
                plan.Add(($"{t}-control", t, "control"));
                plan.Add(($"{t}-cross", t, "run,cross=" + SpikeTracks[(Array.IndexOf(SpikeTracks, t) + 1) % SpikeTracks.Length]));
            }
        }
        if (extra && (only == null || only == "ilyich")) { plan.Add(("ilyich-nounsub", "ilyich", "run,nounsub,cycles=5")); plan.Add(("ilyich-purge", "ilyich", "run,purge,cycles=10")); }

        // --viewer-spike-plan=label:track:mode;label:track:mode (mode as --viewer-spike=, e.g. run,cycles=20 or run,cross=atomgrad,cycles=10) replaces the plan above and skips the tables (the raw VSPIKE lines are the product).
        string custom = OS.GetCmdlineUserArgs().FirstOrDefault(x => x.StartsWith("--viewer-spike-plan="))?["--viewer-spike-plan=".Length..];
        if (custom != null) { plan.Clear(); foreach (var item in custom.Split(';')) { var f = item.Split(':'); plan.Add((f[0], f[1], f[2])); } }
        string dirPrefix = OS.GetCmdlineUserArgs().FirstOrDefault(x => x.StartsWith("--viewer-spike-dir-prefix="))?["--viewer-spike-dir-prefix=".Length..] ?? "/tmp/cccp-spike1-";

        foreach (var (label, track, mode) in plan)
        {
            string note = SpikeWaitIdle();
            string dir = dirPrefix + label;
            foreach (var d in new[] { dir, dir + ".xdg" }) try { if (Directory.Exists(d)) Directory.Delete(d, true); } catch { }
            string load0 = File.ReadAllText("/proc/loadavg").Split(' ')[0];
            int spikeCycles = int.Parse(mode.Split(',').FirstOrDefault(o => o.StartsWith("cycles="))?["cycles=".Length..] ?? "10");
            var c = TapeGame(dir, new[] { "--track=" + track, "--viewer-spike=" + mode }, 600 + 40 * spikeCycles);
            var run = new SpikeRun { Label = label, Track = track, Mode = mode, Exit = c.Exit, Seconds = c.Seconds, TimedOut = c.TimedOut, LoadBefore = load0, Contended = note, Raw = c.Lines };
            foreach (var l in c.Lines)
            {
                if (l.StartsWith("VSPIKE|"))
                {
                    var parts = l.Split('|'); var d = new Dictionary<string, string> { ["kind"] = parts[1] };
                    foreach (var p in parts.Skip(2)) { int e = p.IndexOf('='); if (e > 0) d[p[..e]] = p[(e + 1)..]; else d["text"] = p; }
                    run.Rows.Add(d);
                }
                else if (l.Contains("ERROR") || l.Contains("Unhandled exception") || l.Contains("WARNING") && l.Contains("leak")) run.Errors.Add(l);
            }
            runs.Add(run);
            Out($"## RUN {label} track={track} mode={mode} exit={c.Exit} timedOut={c.TimedOut} wall={c.Seconds:F0}s load_before={load0} load_after={File.ReadAllText("/proc/loadavg").Split(' ')[0]} quiet={(note ?? "yes")} {SpikePower()} errors={run.Errors.Count}");
            foreach (var l in c.Lines.Where(l => l.StartsWith("VSPIKE|"))) sb.AppendLine(l);
            foreach (var e in run.Errors.Take(12)) sb.AppendLine("  ERRLINE: " + e);
            File.WriteAllText(logPath, sb.ToString());
            foreach (var d in new[] { dir, dir + ".xdg" }) try { Directory.Delete(d, true); } catch { }
            try { File.Delete(dir + ".log"); } catch { }
        }

        if (custom != null) { GD.Print("Viewer spike 1: custom plan, log at " + logPath); return; }
        Out("");
        Out("# ================= TABLES (min / median / max over the repeats; n = samples) =================");
        foreach (var t in SpikeTracks.Where(t => only == null || t == only))
        {
            var rr = runs.Where(r => r.Track == t && r.Mode == "run" && r.Rows.Any(x => x["kind"] == "end")).ToList();
            Out($"\n## {t.ToUpperInvariant()}  ({rr.Count} complete 'run' repeats)");
            if (rr.Count == 0) { Out("  no complete run"); continue; }
            Out($"  live cold build ms (EnterTree->Ready, first scene of the process, JIT cold)  {SpikeStat(rr.Select(r => r.N("start", "live_build_ms")))}");
            Out($"  nested build ms, ALL cycles (AddChild wall)                        {SpikeStat(rr.SelectMany(r => r.Of("build").Select(b => SpikeD(b, "build_ms"))))}");
            Out($"  nested build ms, CYCLE 1 only (first nested, warm JIT)                {SpikeStat(rr.Select(r => SpikeD(r.Of("build").First(), "build_ms")))}");
            Out($"  nested build ms, cycles 2..10                                       {SpikeStat(rr.SelectMany(r => r.Of("build").Skip(1).Select(b => SpikeD(b, "build_ms"))))}");
            Out($"  nested Ready-signal ms (EnterTree->Ready, same thing from inside)    {SpikeStat(rr.SelectMany(r => r.Of("build").Select(b => SpikeD(b, "ready_signal_ms"))))}");
            Out($"  first frame after add ms / second frame ms                         {SpikeStat(rr.SelectMany(r => r.Of("build").Select(b => SpikeD(b, "frame1_ms"))))}  /  {SpikeStat(rr.SelectMany(r => r.Of("build").Select(b => SpikeD(b, "frame2_ms"))))}");
            Out($"  nested node count (cycle 1) / live node count                       {rr[0].Of("build").First()["nested_nodes"]} / {rr[0].Of("start").First()["live_nodes"]}");
            string R(string phase, int cyc, string key) => SpikeStat(rr.Select(r => SpikeD(r.Snap(phase, cyc), key)), "0");
            Out($"  RSS MB baseline (30 frames + GC)                                    {R("baseline", 0, "rss_mb")}");
            Out($"  RSS MB baseline after malloc_trim                                   {R("baseline_trim", 0, "rss_mb")}");
            Out($"  RSS MB with nested open, cycle 1 / cycle 10                          {R("open", 1, "rss_mb")}  /  {R("open", 10, "rss_mb")}");
            Out($"  peak RSS (VmHWM since reset) cycle 1 open / cycle 10 open            {R("open", 1, "hwm_mb")}  /  {R("open", 10, "hwm_mb")}");
            Out($"  peak RSS cycle 1 incl. close                                        {R("closed", 1, "hwm_mb")}");
            Out($"  RSS MB after close (raw, 2-3 frames) cycle 1 / 5 / 10                {R("closed", 1, "rss_mb")}  /  {R("closed", 5, "rss_mb")}  /  {R("closed", 10, "rss_mb")}");
            Out($"  RSS MB after close + GC cycle 1 / 5 / 10                             {R("closed_gc", 1, "rss_mb")}  /  {R("closed_gc", 5, "rss_mb")}  /  {R("closed_gc", 10, "rss_mb")}");
            Out($"  RSS MB after close + GC + malloc_trim cycle 1 / 5 / 10               {R("closed_trim", 1, "rss_mb")}  /  {R("closed_trim", 5, "rss_mb")}  /  {R("closed_trim", 10, "rss_mb")}");
            Out($"  RSS growth cycle 1->10 (closed_trim), MB total / per cycle           {SpikeStat(rr.Select(r => SpikeD(r.Snap("closed_trim", 10), "rss_mb") - SpikeD(r.Snap("closed_trim", 1), "rss_mb")), "0.0")}  / per cycle /9");
            Out($"  nested freed after N frames of QueueFree                            {SpikeStat(rr.SelectMany(r => r.Of("free").Select(b => SpikeD(b, "frames_until_nested_freed"))))}");
            var ctl = runs.FirstOrDefault(r => r.Track == t && r.Mode == "control" && r.Rows.Any(x => x["kind"] == "end"));
            if (ctl != null)
                Out($"  CONTROL (same schedule, no nested) RSS baseline -> c1 / c5 / c10 closed_trim: {SpikeD(ctl.Snap("baseline", 0), "rss_mb"):0} -> {SpikeD(ctl.Snap("closed_trim", 1), "rss_mb"):0} / {SpikeD(ctl.Snap("closed_trim", 5), "rss_mb"):0} / {SpikeD(ctl.Snap("closed_trim", 10), "rss_mb"):0}; hwm c1 {SpikeD(ctl.Snap("open", 1), "hwm_mb"):0}");
            // count deltas vs baseline, per cycle, first repeat (all repeats listed in the raw log)
            foreach (var key in new[] { "obj", "nodes", "orphans", "res", "players", "threads", "fds", "buses", "nodeadded_conns", "joy_conns", "dn_unlit", "dn_scenery" })
            {
                var r0 = rr[0]; var b0 = r0.Snap("baseline", 0);
                var d = Enumerable.Range(1, 10).Select(cy => SpikeD(r0.Snap("closed_gc", cy), key) - SpikeD(b0, key)).ToList();
                var o1 = SpikeD(r0.Snap("open", 1), key) - SpikeD(b0, key);
                var med = Enumerable.Range(1, 10).Select(cy => SpikeStat(rr.Select(r => SpikeD(r.Snap("closed_gc", cy), key) - SpikeD(r.Snap("baseline", 0), key)), "0")).Take(0);
                Out($"  {key,-16} baseline {SpikeD(b0, key):0}  open(c1) +{o1:0}  closed_gc delta vs baseline per cycle 1..10 (repeat 1): [{string.Join(",", d.Select(x => x.ToString("0")))}]   cycle10 over all repeats: {SpikeStat(rr.Select(r => SpikeD(r.Snap("closed_gc", 10), key) - SpikeD(r.Snap("baseline", 0), key)))}");
            }
            Out($"  mon_mem_static_mb baseline / open c1 / closed_gc c1 / closed_gc c10 (repeat 1): {SpikeD(rr[0].Snap("baseline", 0), "mon_mem_static_mb"):0.0} / {SpikeD(rr[0].Snap("open", 1), "mon_mem_static_mb"):0.0} / {SpikeD(rr[0].Snap("closed_gc", 1), "mon_mem_static_mb"):0.0} / {SpikeD(rr[0].Snap("closed_gc", 10), "mon_mem_static_mb"):0.0}");
            Out($"  gc_heap_mb baseline / open c1 / closed_gc c1 / closed_gc c10 (repeat 1): {SpikeD(rr[0].Snap("baseline", 0), "gc_heap_mb"):0.0} / {SpikeD(rr[0].Snap("open", 1), "gc_heap_mb"):0.0} / {SpikeD(rr[0].Snap("closed_gc", 1), "gc_heap_mb"):0.0} / {SpikeD(rr[0].Snap("closed_gc", 10), "gc_heap_mb"):0.0}");
            Out($"  vram/tex/buf MB at open c1 (dummy renderer): {SpikeD(rr[0].Snap("open", 1), "vram_mb"):0.0}/{SpikeD(rr[0].Snap("open", 1), "tex_mb"):0.0}/{SpikeD(rr[0].Snap("open", 1), "buf_mb"):0.0}");
            Out($"  error lines in the child logs: {string.Join(", ", rr.Select(r => r.Errors.Count))}");
        }

        Out("\n## STATICS (repeat 1 of each track: baseline -> open c1 -> closed c1 -> closed c10; only rows that differ somewhere; '=' = same as baseline)");
        foreach (var t in SpikeTracks.Where(t => only == null || t == only))
        {
            var r0 = runs.FirstOrDefault(r => r.Track == t && r.Mode == "run" && r.Rows.Any(x => x["kind"] == "end"));
            if (r0 == null) continue;
            Dictionary<string, string> S(string ph, int cy) => r0.Of("statics").FirstOrDefault(x => x["phase"] == ph && x["cycle"] == cy.ToString());
            var b = S("baseline", 0); var o = S("open", 1); var c1 = S("closed", 1); var c10 = S("closed", 10);
            Out($"\n### {t}");
            int same = 0;
            foreach (var k in b.Keys.Where(k => k != "kind" && k != "phase" && k != "cycle"))
            {
                string Vv(Dictionary<string, string> d) => d != null && d.TryGetValue(k, out var v) ? v : "?";
                if (Vv(o) == Vv(b) && Vv(c1) == Vv(b) && Vv(c10) == Vv(b)) { same++; continue; }
                Out($"  {k,-34} {Vv(b)} -> {Vv(o)} -> {Vv(c1)} -> {Vv(c10)}");
            }
            Out($"  ({same} tracked statics unchanged through all four points)");
        }
        Out("\n## EXTRA RUNS");
        foreach (var r in runs.Where(r => r.Mode != "run" && r.Mode != "control"))
        {
            Out($"### {r.Label} ({r.Mode}) exit={r.Exit} errors={r.Errors.Count}");
            foreach (var bd in r.Of("build")) Out($"  build cycle {bd["cycle"]} track {bd["track"]}: {SpikeD(bd, "build_ms"):0} ms");
            var b0 = r.Snap("baseline", 0);
            foreach (var key in new[] { "obj", "nodes", "orphans", "res", "joy_conns", "nodeadded_conns", "dn_unlit", "dn_scenery" })
                Out($"  {key,-16} baseline {SpikeD(b0, key):0} closed_gc per cycle: [{string.Join(",", r.Of("snap").Where(s => s["phase"] == "closed_gc").Select(s => (SpikeD(s, key) - SpikeD(b0, key)).ToString("0")))}]");
            Out($"  rss closed_trim per cycle: [{string.Join(",", r.Of("snap").Where(s => s["phase"] == "closed_trim").Select(s => SpikeD(s, "rss_mb").ToString("0")))}]");
            var st = r.Of("statics").ToList();
            if (st.Count > 1)
                foreach (var k in st[0].Keys.Where(k => k != "kind" && k != "phase" && k != "cycle"))
                    if (st.Any(s => s.TryGetValue(k, out var v) && v != st[0][k])) Out($"  static {k}: {string.Join(" -> ", st.Select(s => s.GetValueOrDefault(k, "?")))}");
            var end = r.Of("end").FirstOrDefault();
            if (end != null) Out($"  data dir files at end: {end["data_files"]} ({end.GetValueOrDefault("files")})");
        }
        var any = runs.Where(r => r.Rows.Any(x => x["kind"] == "end")).Select(r => r.Of("end").First()["files"]).Distinct().ToList();
        Out($"\n## DATA DIR after the runs (names:bytes, distinct sets across runs): {string.Join(" | ", any)}");
        int bad = runs.Count(r => !r.Rows.Any(x => x["kind"] == "end"));
        Out($"\n# {runs.Count} runs, {bad} without an 'end' row");
        if (bad > 0) _failures++;
        GD.Print("Viewer spike 1: log at " + logPath);
    }
}
