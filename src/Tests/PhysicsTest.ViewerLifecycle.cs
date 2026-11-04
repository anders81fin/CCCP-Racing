using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Godot;

namespace CCCPRacing.Tests;

// --viewer-lifecycle-tests: judges the VIEWERLIFECYCLE lines of the child games (see RaceScene.ViewerLifecycleTest.cs).
// One headless child game at a time (own launcher, below: TapeGame always passes --fixed-fps and the stall measurement must not). Every child writes its log to
// <project>/build/player-replay/phase2/logs/vl-<name>.log. Every verdict is an explicit count or value from the child's lines, never "no ERROR lines"; the known shutdown line
// "ERROR: 1 resources still in use at exit" is counted and reported per child, not filtered.
// Options: --viewer-lifecycle-only=fixtures|cycles|cancel|fail|rapid|quit|audio|writes|stall (comma list), --viewer-lifecycle-tracks=ilyich,siberia,..., --viewer-lifecycle-cycles=N (default 20),
// --viewer-lifecycle-series=normal,restore (the second series restores the live DayNight lists after each close, the workaround for the NodeAdded leak).
public partial class PhysicsTest
{
    const string VlTmp = "/tmp/cccp-lc";
    static readonly string[] VlAllTracks = { "ilyich", "siberia", "atomgrad", "bonus" };
    int _vlPass, _vlFail;
    int _vlShutdownErrors;

    static string VlOpt(string name) => OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith(name + "="))?[(name.Length + 1)..];

    void VlCheck(bool ok, string what) { if (ok) _vlPass++; else _vlFail++; TapeCheck(ok, what); }

    static string VlLogDir => Path.Combine(TapeProjectDir, "build", "player-replay", "phase2", "logs");
    static string VlFx => VlTmp + "-fx";             // VlFx/<track> = a retained-laps folder
    static string VlFxData => VlTmp + "-fxdata";     // VlFxData/<track> = the producer's whole data dir (records, profiles)

    // ------------------------------------------------------------------ child launcher

    TapeChild VlGame(string name, string[] args, int timeoutSec, bool fixedFps = true, string seedFrom = null, string dir = null)
    {
        dir ??= $"{VlTmp}-{name}";
        foreach (var d in new[] { dir, dir + ".xdg" }) try { if (Directory.Exists(d)) Directory.Delete(d, true); } catch { }
        Directory.CreateDirectory(dir);
        if (seedFrom != null) VlCopyTree(seedFrom, dir);
        var psi = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
        var head = new List<string> { "-c", "exec \"$0\" \"$@\" > \"$VL_OUT\" 2>&1", OS.GetExecutablePath(), "--headless" };
        if (fixedFps) { head.Add("--fixed-fps"); head.Add("60"); }
        head.AddRange(new[] { "--path", TapeProjectDir, "--" });
        foreach (var s in head) psi.ArgumentList.Add(s);
        foreach (var a in args.Concat(new[] { "--no-intro", "--babushka-seed=1", "--rng-seed=1", "--data-dir=" + dir })) psi.ArgumentList.Add(a);
        Directory.CreateDirectory(VlLogDir);
        string log = Path.Combine(VlLogDir, $"vl-{name}.log");
        psi.Environment["VL_OUT"] = log;
        psi.Environment["XDG_DATA_HOME"] = dir + ".xdg";
        psi.Environment.Remove("CCCP_TAPE");
        var sw = Stopwatch.StartNew();
        using var p = Process.Start(psi);
        bool timedOut = false;
        while (!p.WaitForExit(200))
            if (sw.Elapsed.TotalSeconds > timeoutSec) { timedOut = true; try { p.Kill(true); } catch { } p.WaitForExit(); break; }      // (only this runner's own child, by its PID)
        var lines = File.Exists(log) ? File.ReadAllLines(log) : Array.Empty<string>();
        var c = new TapeChild(p.ExitCode, timedOut, lines, sw.Elapsed.TotalSeconds);
        int sh = lines.Count(l => l.Contains("resources still in use at exit"));
        _vlShutdownErrors += sh;
        TapeInfo($"child {name}: exit {c.Exit}, {c.Seconds:F0} s{(timedOut ? ", TIMED OUT" : "")}, 'resources still in use at exit' lines {sh}, other ERROR lines {lines.Count(l => l.Contains("ERROR") && !l.Contains("resources still in use at exit"))}, log {log}");
        return c;
    }

    static void VlCopyTree(string from, string to)
    {
        if (!Directory.Exists(from)) return;
        foreach (var d in Directory.GetDirectories(from, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, d)));
        foreach (var f in Directory.GetFiles(from, "*", SearchOption.AllDirectories)) File.Copy(f, Path.Combine(to, Path.GetRelativePath(from, f)), true);
    }

    static void VlRmDir(string d) { try { if (d.StartsWith(VlTmp) && Directory.Exists(d)) Directory.Delete(d, true); } catch { } }

    static Dictionary<string, string> VlHash(string root)
    {
        var d = new Dictionary<string, string>();
        if (!Directory.Exists(root)) return d;
        foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(root, f);
            if (rel.Split(Path.DirectorySeparatorChar).Contains("logs")) continue;
            d[rel] = new FileInfo(f).Length + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f)))[..16];
        }
        return d;
    }

    // ------------------------------------------------------------------ line parsing and statistics

    static List<Dictionary<string, string>> VlLines(TapeChild c, string tag)
    {
        var res = new List<Dictionary<string, string>>();
        string prefix = "VIEWERLIFECYCLE: " + tag + "|";
        foreach (var l in c.Lines)
        {
            if (!l.StartsWith(prefix)) continue;
            var d = new Dictionary<string, string>();
            foreach (var kv in l[prefix.Length..].Split('|')) { int i = kv.IndexOf('='); if (i > 0) d[kv[..i]] = kv[(i + 1)..]; }
            res.Add(d);
        }
        return res;
    }

    static double VlD(Dictionary<string, string> d, string k) => d.TryGetValue(k, out var v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ? x : double.NaN;
    static string VlS(Dictionary<string, string> d, string k) => d.TryGetValue(k, out var v) ? v : null;

    static (double slope, double intercept) VlFit(IList<(double x, double y)> pts)
    {
        if (pts.Count < 2) return (double.NaN, double.NaN);
        double mx = pts.Average(p => p.x), my = pts.Average(p => p.y);
        double sxx = pts.Sum(p => (p.x - mx) * (p.x - mx)), sxy = pts.Sum(p => (p.x - mx) * (p.y - my));
        double s = sxx == 0 ? 0 : sxy / sxx;
        return (s, my - s * mx);
    }

    bool VlChildOk(TapeChild c, string what)
    {
        bool ok = !c.TimedOut && c.Exit == 0 && !c.Crashed && !c.Lines.Any(l => l.Contains("script FAILED") || l.Contains("VIEWERLIFECYCLE: fatal"));
        VlCheck(ok, $"{what}: the child ended with exit code 0, no timeout, no crash and no script failure (exit {c.Exit}, {c.Seconds:F0} s, timed out {c.TimedOut}, crashed {c.Crashed}, fatal {c.Find("VIEWERLIFECYCLE: fatal")?["VIEWERLIFECYCLE: ".Length..]})");
        VlCheck(c.Has("VIEWERLIFECYCLE: script-done") || what.Contains("quit"), $"{what}: the script reached its end");
        return ok;
    }

    // ------------------------------------------------------------------ entry

    void ViewerLifecycleTests()
    {
        GD.Print("Viewer lifecycle tests (real game, one headless child at a time)");
        if (!OperatingSystem.IsLinux()) { Skip("viewer lifecycle: the children go through /bin/sh (Linux only)"); return; }
        var only = (VlOpt("--viewer-lifecycle-only") ?? "fixtures,cycles,cancel,fail,rapid,quit,audio,writes,stall").Split(',');
        var tracks = (VlOpt("--viewer-lifecycle-tracks") ?? string.Join(",", VlAllTracks)).Split(',');
        Directory.CreateDirectory(VlLogDir);
        bool On(string n) => only.Contains(n);
        try
        {
            VlFixtures(VlAllTracks);
            if (On("cycles")) VlCycleTests(tracks);
            if (On("alternate")) VlAlternateTests(tracks);
            if (On("inject")) VlInjectTests();
            if (On("cancel")) VlCancelTests(tracks);
            if (On("fail")) VlFailTests();
            if (On("rapid")) VlRapidTests();
            if (On("quit")) VlQuitTests();
            if (On("audio")) VlAudioTests();
            if (On("writes")) VlWriteTests();
            if (On("stall")) VlStallTests(tracks);
        }
        finally
        {
            TapeInfo($"the known shutdown message 'ERROR: 1 resources still in use at exit' appeared {_vlShutdownErrors} time(s) over all children (information only; counted per child above, never filtered from a log)");
            GD.Print($"VIEWERLIFECYCLE SUMMARY: {_vlPass} checks passed, {_vlFail} failed (explicit counts above)");
        }
        GD.Print(_failures == 0 ? "Viewer lifecycle tests: all passed" : $"Viewer lifecycle tests: {_failures} FAILED");
    }

    // ------------------------------------------------------------------ fixtures: one retained lap (ghost + tape) per track, made by a producer child once and kept

    void VlFixtures(string[] tracks)
    {
        foreach (var t in tracks)
        {
            string lapDir = Path.Combine(VlFx, t);
            if (Directory.Exists(lapDir) && Directory.GetDirectories(lapDir).Any(d => File.Exists(Path.Combine(d, "meta.json"))) && Directory.Exists(Path.Combine(VlFxData, t))) { TapeInfo($"fixture {t}: reused ({lapDir})"); continue; }
            var args = new List<string> { "--start", "--autodrive", "--track=" + t, "--tape-quit-laps=1", "--lap-archive-retain-all" };
            if (t == "bonus") args.Add("--ap-speed=0.8");
            var c = VlGame("fixture-" + t, args.ToArray(), 900);
            string data = $"{VlTmp}-fixture-{t}";
            string laps = Directory.Exists(data) ? Directory.GetDirectories(data, "laps", SearchOption.AllDirectories).FirstOrDefault() : null;
            bool ok = laps != null && Directory.GetDirectories(laps).Any(d => File.Exists(Path.Combine(d, "meta.json")) && File.Exists(Path.Combine(d, "tape.bin")));
            VlCheck(ok && c.Exit == 0, $"fixture {t}: the producer child archived and retained a lap with a world tape (exit {c.Exit}, laps folder {laps})");
            if (!ok) continue;
            VlRmDir(lapDir); VlRmDir(Path.Combine(VlFxData, t));
            VlCopyTree(laps, lapDir); VlCopyTree(data, Path.Combine(VlFxData, t));
            VlRmDir(data); VlRmDir(data + ".xdg");
        }
    }

    string[] VlFxArgs(string track, string lapTrack = null, params string[] more) =>
        new[] { "--start", "--autodrive", "--track=" + track, "--vl-fx=" + VlFx, "--vl-lap-track=" + (lapTrack ?? track) }.Concat(more).ToArray();

    string[] VlTrackArgs(string t) => t == "bonus" ? new[] { "--ap-speed=0.8" } : Array.Empty<string>();

    static bool VlHasFx(string t) => Directory.Exists(Path.Combine(VlFx, t)) && Directory.GetDirectories(Path.Combine(VlFx, t)).Length > 0;

    // ------------------------------------------------------------------ 1: cycles

    void VlCycleTests(string[] tracks)
    {
        int n = int.Parse(VlOpt("--viewer-lifecycle-cycles") ?? "20");
        var series = (VlOpt("--viewer-lifecycle-series") ?? "normal,restore").Split(',');
        var combos = tracks.Select(t => (live: t, lap: t)).ToList();
        if (tracks.Contains("ilyich") && VlHasFx("siberia")) combos.Add(("ilyich", "siberia"));
        foreach (var ser in series)
            foreach (var (live, lap) in combos)
            {
                if (!VlHasFx(lap)) { Skip($"cycles {live}/{lap}: no fixture lap"); continue; }
                string name = $"cycles-{ser}-{live}" + (live != lap ? "-lap-" + lap : "");
                var args = VlFxArgs(live, lap, "--viewer-lifecycle=cycles", "--vl-cycles=" + n).Concat(VlTrackArgs(live)).Concat(ser == "restore" ? new[] { "--vl-purge" } : Array.Empty<string>()).ToArray();
                var c = VlGame(name, args, 2400);
                string label = $"cycles [{ser}] live {live}, lap {lap}";
                VlChildOk(c, label);
                VlJudgeCycles(c, label, n);
                VlRmDir($"{VlTmp}-{name}"); VlRmDir($"{VlTmp}-{name}.xdg");
            }
    }

    // ------------------------------------------------------------------ 1b: alternating tracks and the injected-leak control (gate R)

    void VlAlternateTests(string[] tracks)
    {
        int rounds = int.Parse(VlOpt("--viewer-lifecycle-rounds") ?? "6");
        var have = tracks.Where(VlHasFx).ToArray();
        if (have.Length < 2) { Skip("alternate: fewer than two fixture tracks"); return; }
        string live = have[0]; string name = "alternate";
        var args = new[] { "--start", "--autodrive", "--track=" + live, "--vl-fx=" + VlFx, "--viewer-lifecycle=alternate", "--vl-lap-tracks=" + string.Join(",", have), "--vl-rounds=" + rounds };
        var c = VlGame(name, args, 2400);
        VlChildOk(c, "alternate tracks");
        VlJudgeAlternate(c, "alternate tracks", have, rounds, expectLeak: false);
        VlRmDir($"{VlTmp}-{name}"); VlRmDir($"{VlTmp}-{name}.xdg");
    }

    /// <summary>The negative control: the same alternating run with 2 Resources leaked per cycle must be REJECTED by the criterion (and not by luck: the leak is real and retained).</summary>
    void VlInjectTests()
    {
        var have = VlAllTracks.Where(VlHasFx).Take(2).ToArray();
        if (have.Length < 2) { Skip("inject: fewer than two fixture tracks"); return; }
        string name = "alternate-injected";
        var args = new[] { "--start", "--autodrive", "--track=" + have[0], "--vl-fx=" + VlFx, "--viewer-lifecycle=alternate", "--vl-lap-tracks=" + string.Join(",", have), "--vl-rounds=9", "--vl-inject-leak=2" };
        var c = VlGame(name, args, 2400);
        VlChildOk(c, "alternate tracks with an injected leak");
        VlJudgeAlternate(c, "alternate tracks with an injected leak of 2 objects per cycle", have, 9, expectLeak: true);
        VlRmDir($"{VlTmp}-{name}"); VlRmDir($"{VlTmp}-{name}.xdg");
    }

    void VlJudgeAlternate(TapeChild c, string label, string[] tracks, int rounds, bool expectLeak)
    {
        var rows = VlLines(c, "cycle").Where(r => VlS(r, "phase") == "closed").OrderBy(r => VlD(r, "n")).ToList();
        VlCheck(rows.Count == tracks.Length * rounds, $"{label}: every one of the {tracks.Length * rounds} open/close cycles was measured ({rows.Count})");
        string f(double x) => x.ToString("0.###", CultureInfo.InvariantCulture);
        TapeInfo($"{label}: per cycle (track raw_obj/settled obj/res/rss MB/build ms/close ms): " + string.Join(" | ", rows.Select(r => $"{VlS(r, "lap_track")} {VlS(r, "raw_obj")}/{VlS(r, "obj")}/{VlS(r, "res")}/{VlD(r, "rss"):0}/{VlS(r, "build_ms")}/{VlS(r, "close_ms")}")));
        foreach (var t in tracks)
        {
            var mine = rows.Where(r => VlS(r, "lap_track") == t).ToList();
            int warm = 2;      // (the first two visits of a track warm its caches; the rest is judged)
            var ro = ResourceCriterion.Evaluate(mine.Skip(warm).Select(r => VlD(r, "obj")).ToList(), spreadMax: 60);
            var rr = ResourceCriterion.Evaluate(mine.Skip(warm).Select(r => VlD(r, "res")).ToList(), spreadMax: 60);
            if (expectLeak) VlCheck(!ro.Ok, $"{label}: {t}: the criterion REJECTS the injected leak in the object series (objects: {ro.Why}; the resource monitor counts only path-registered resources, so the injected anonymous ones show only in objects: {rr.Why})");
            else
            {
                VlCheck(ro.Ok, $"{label}: {t}: settled object series steady across the rounds after warm-up ({ro.Why}; slope {f(ro.Slope)}/cycle, spread {f(ro.Spread)})");
                VlCheck(rr.Ok, $"{label}: {t}: settled resource series steady across the rounds ({rr.Why}; slope {f(rr.Slope)}/cycle, spread {f(rr.Spread)})");
            }
        }
        if (!expectLeak)
        {
            var bl = VlLines(c, "cycle").FirstOrDefault(r => VlS(r, "n") == "0");
            VlCheck(rows.All(r => VlD(r, "orph") == VlD(bl, "orph") && VlD(r, "nodes") == VlD(bl, "nodes")), $"{label}: node and orphan counts equal the baseline after every close (nodes {f(VlD(bl, "nodes"))}, orphans {f(VlD(bl, "orph"))})");
        }
    }

    void VlJudgeCycles(TapeChild c, string label, int n)
    {
        var rows = VlLines(c, "cycle");
        var bl = rows.FirstOrDefault(r => VlS(r, "n") == "0");
        var cyc = rows.Where(r => VlS(r, "phase") == "closed").OrderBy(r => VlD(r, "n")).ToList();
        VlCheck(bl != null && cyc.Count == n, $"{label}: baseline and all {n} cycles were measured (cycles measured {cyc.Count})");
        if (bl == null || cyc.Count < 3) return;
        VlCheck(cyc.All(r => VlS(r, "state") == "Closed" && VlS(r, "wrapped") == "True" && VlS(r, "viewer_ref") == "null" && VlS(r, "active") == "0"),
            $"{label}: in every cycle the session reached Closed, the loop wrapped, the owner reference was cleared and no session stayed active (closed {cyc.Count(r => VlS(r, "state") == "Closed")}, wrapped {cyc.Count(r => VlS(r, "wrapped") == "True")}, ref cleared {cyc.Count(r => VlS(r, "viewer_ref") == "null")}, active 0 in {cyc.Count(r => VlS(r, "active") == "0")} of {cyc.Count})");
        foreach (int win in new[] { 10, n }.Distinct().Where(w => w <= cyc.Count))
        {
            var pts = cyc.Where(r => VlD(r, "n") >= 2 && VlD(r, "n") <= win).ToList();
            (double s, double i) Fit(string k) => VlFit(pts.Select(r => (VlD(r, "n"), VlD(r, k))).ToList());
            var (so, io) = Fit("obj"); var (sr, ir) = Fit("res"); var (st, it) = Fit("threads"); var (sm, im) = Fit("rss"); var (sn, _) = Fit("nodes"); var (sor, _) = Fit("orph");
            string f(double x) => x.ToString("0.###", CultureInfo.InvariantCulture);
            TapeInfo($"{label}: cycles 2..{win}: objects slope {f(so)}/cycle intercept {f(io)} (baseline {f(VlD(bl, "obj"))}, cycle 1 {f(VlD(cyc[0], "obj"))}), resources slope {f(sr)} intercept {f(ir)} (baseline {f(VlD(bl, "res"))}), threads slope {f(st)} intercept {f(it)} (baseline {f(VlD(bl, "threads"))}), rss slope {f(sm)} MB/cycle intercept {f(im)} (baseline {f(VlD(bl, "rss"))} MB), nodes slope {f(sn)}, orphans slope {f(sor)}");
            VlCheck(Math.Abs(so) <= 2, $"{label}: object count plateau over cycles 2..{win}: |slope| {f(Math.Abs(so))} <= 2 objects/cycle (intercept {f(io)}, warm-up over baseline {f(io - VlD(bl, "obj"))})");
            VlCheck(Math.Abs(sr) <= 2, $"{label}: resource count plateau over cycles 2..{win}: |slope| {f(Math.Abs(sr))} <= 2 /cycle (intercept {f(ir)})");
            VlCheck(Math.Abs(st) <= 0.25, $"{label}: thread count plateau over cycles 2..{win}: |slope| {f(Math.Abs(st))} <= 0.25 /cycle (intercept {f(it)}, min {pts.Min(r => VlD(r, "threads"))}, max {pts.Max(r => VlD(r, "threads"))})");
            VlCheck(pts.All(r => VlD(r, "nodes") == VlD(bl, "nodes")), $"{label}: node count equals the baseline {f(VlD(bl, "nodes"))} in every cycle 2..{win} (values {string.Join(",", pts.Select(r => f(VlD(r, "nodes"))).Distinct())}; cycle 1: {f(VlD(cyc[0], "nodes"))})");
            VlCheck(pts.All(r => VlD(r, "orph") == VlD(bl, "orph")), $"{label}: orphan count equals the baseline {f(VlD(bl, "orph"))} in every cycle 2..{win} (values {string.Join(",", pts.Select(r => f(VlD(r, "orph"))).Distinct())}; cycle 1: {f(VlD(cyc[0], "orph"))})");
            var (spm, ipm) = VlFit(pts.Select(r => (VlD(r, "n"), VlD(r, "props_materials"))).ToList()); var (spb, ipb) = VlFit(pts.Select(r => (VlD(r, "n"), VlD(r, "props_batch"))).ToList());
            VlCheck(Math.Abs(spm) <= 0.1 && Math.Abs(spb) <= 0.1, $"{label}: the shared Props caches stop growing after cycle 1 (cycles 2..{win}: Materials slope {f(spm)} intercept {f(ipm)}, BatchCache slope {f(spb)} intercept {f(ipb)}; cycle 1: {VlS(cyc[0], "props_materials")}/{VlS(cyc[0], "props_batch")}, baseline run start: see cache_diffs)");
            VlCheck(pts.All(r => VlD(r, "buses") == VlD(cyc[0], "buses")) && VlD(cyc[0], "buses") <= VlD(bl, "buses") + 1, $"{label}: audio bus count is flat from cycle 1 ({f(VlD(cyc[0], "buses"))}; baseline {f(VlD(bl, "buses"))}, at most one lazily created mix-group bus such as Jet) in every cycle 2..{win}");
        }
        // the gate-R criterion on the settled series after a three-cycle warm-up (slope, drift and spread: CCCPRacing.Tests.ResourceCriterion), objects and resources
        if (cyc.Count >= 7)
        {
            var co = ResourceCriterion.AfterWarmup(cyc.Select(r => VlD(r, "obj")).ToList(), 3); var cr = ResourceCriterion.AfterWarmup(cyc.Select(r => VlD(r, "res")).ToList(), 3);
            VlCheck(co.Ok, $"{label}: steady-state criterion on the settled object series after 3 warm-up cycles ({co.Why}; slope {co.Slope:0.00}/cycle, spread {co.Spread:0})");
            VlCheck(cr.Ok, $"{label}: steady-state criterion on the settled resource series after 3 warm-up cycles ({cr.Why}; slope {cr.Slope:0.00}/cycle, spread {cr.Spread:0})");
        }
        TapeInfo($"{label}: per-cycle audio buses {string.Join(" ", cyc.Select(r => VlS(r, "buses")))} (baseline {VlS(bl, "buses")}); cache_diffs cycle 1: {VlS(cyc[0], "cache_diffs")}");
        TapeInfo($"{label}: per-cycle objects {string.Join(" ", cyc.Select(r => VlS(r, "obj")))}; resources {string.Join(" ", cyc.Select(r => VlS(r, "res")))}; rss MB {string.Join(" ", cyc.Select(r => VlD(r, "rss").ToString("0")))}; DayNight unlit {string.Join(" ", cyc.Select(r => VlS(r, "dn_unlit")))}; build ms {string.Join(" ", cyc.Select(r => VlS(r, "build_ms")))}");
        VlCheck(cyc.Skip(1).All(r => VlD(r, "live_diffs") == 0) && (VlD(cyc[0], "live_diffs") == 0 || (VlS(cyc[0], "live_diff_names") ?? "").StartsWith("buses:") ), $"{label}: the live scene's modes, hud, buses and shared statics equal the baseline after every close (cycles with a difference {cyc.Count(r => VlD(r, "live_diffs") != 0)}: {cyc.FirstOrDefault(r => VlD(r, "live_diffs") != 0)?["live_diff_names"]})");
    }

    // ------------------------------------------------------------------ 2: cancel

    void VlCancelTests(string[] tracks)
    {
        foreach (var t in tracks)
        {
            if (!VlHasFx(t)) { Skip($"cancel {t}: no fixture lap"); continue; }
            var c = VlGame("cancel-" + t, VlFxArgs(t, null, "--viewer-lifecycle=cancel").Concat(VlTrackArgs(t)).ToArray(), 1500);
            string label = $"cancel {t}";
            VlChildOk(c, label);
            var full = VlLines(c, "full").FirstOrDefault();
            VlCheck(full != null && VlS(full, "state") == "Playing", $"{label}: the uncancelled control build reached Playing in {full?["steps"]} build steps");
            var all = VlLines(c, "cancel");
            var rows = all.Where(r => VlS(r, "exercised") == "True").GroupBy(r => VlS(r, "point")).Select(g => g.First()).ToList();
            VlCheck(rows.Count == 4, $"{label}: all four checkpoints (zero, early, mid, late) were cancelled while the session was really Building ({rows.Count} of 4; attempts {all.Count}; not exercised: {string.Join(",", all.Where(r => VlS(r, "exercised") != "True").Select(r => VlS(r, "point") + "#" + VlS(r, "attempt") + ":" + VlS(r, "state_at_cancel")))})");
            foreach (var r in rows)
            {
                string p = $"{label} @{VlS(r, "point")} (after {VlS(r, "steps_at_cancel")} steps, state {VlS(r, "state_at_cancel")})";
                VlCheck(VlS(r, "state_after") == "Closed" && VlS(r, "exception") == "none" && VlS(r, "active") == "0" && VlS(r, "viewer_ref") == "null", $"{p}: Closed in {VlS(r, "closed_frames")} frames, no exception ({VlS(r, "exception")}), no active session, owner reference cleared");
                VlCheck(VlS(r, "mode_ok") == "True" && VlS(r, "visible") == "True", $"{p}: live ProcessMode and Visible restored (mode ok {VlS(r, "mode_ok")}, visible {VlS(r, "visible")})");
                VlCheck(VlD(r, "orph") <= VlD(r, "base_orph") && VlD(r, "nodes") <= VlD(r, "base_nodes"), $"{p}: orphans {VlS(r, "orph")} vs {VlS(r, "base_orph")} just before this open (new {VlD(r, "orph") - VlD(r, "base_orph")}), nodes {VlS(r, "nodes")} vs {VlS(r, "base_nodes")}, objects {VlS(r, "obj")} vs {VlS(r, "base_obj")}" + (VlS(r, "orphans") is { Length: > 0 } o ? " ORPHANS: " + o : ""));
            }
            foreach (var r in VlLines(c, "cancel-live").Where(r => rows.Any(x => VlS(x, "point") == VlS(r, "point") && VlS(x, "attempt") == VlS(r, "attempt"))))
                VlCheck(VlD(r, "diffs_nonaudio") == 0 && VlD(r, "diffs_paused") == 0, $"{label} @{VlS(r, "point")}: live scene (modes, hud, buses, statics, players' paused flags) unchanged: non-audio diffs {VlS(r, "diffs_nonaudio")}, paused diffs {VlS(r, "diffs_paused")} {VlS(r, "nonaudio")} {VlS(r, "paused")}");
            VlRmDir($"{VlTmp}-cancel-{t}"); VlRmDir($"{VlTmp}-cancel-{t}.xdg");
        }
    }

    // ------------------------------------------------------------------ 3: failure paths

    void VlFailTests()
    {
        foreach (var (live, other) in new[] { ("ilyich", "siberia") })
        {
            if (!VlHasFx(live)) continue;
            var c = VlGame("fail-" + live, VlFxArgs(live, null, "--viewer-lifecycle=fail", "--vl-other-track=" + other), 1200);
            string label = $"failure paths (live {live})";
            VlChildOk(c, label);
            var rows = VlLines(c, "fail");
            var live0 = VlLines(c, "fail-live");
            foreach (var r in rows)
            {
                string cs = VlS(r, "case"), exp = VlS(r, "expect"), outc = VlS(r, "outcome");
                if (cs is "open-while-open")
                {
                    VlCheck(VlS(r, "first_ok") == "True" && VlS(r, "second_null") == "True" && VlS(r, "third_null") == "True" && VlS(r, "first_plays") == "True" && VlS(r, "active") == "0" && VlS(r, "viewer_ref") == "null",
                        $"{label}: open while open: first opened and played ({VlS(r, "first_plays")}), 2nd and 3rd refused ({VlS(r, "second_null")}/{VlS(r, "third_null")}: '{VlS(r, "second_reason")}'), session closed cleanly");
                    continue;
                }
                if (cs is "open-while-booting")
                {
                    VlCheck(VlS(r, "null") == "True" && VlS(r, "active") == "0", $"{label}: open while the live scene is booting: refused ({VlS(r, "null")}: '{VlS(r, "reason")}'), no session");
                    continue;
                }
                bool ok = outc switch
                {
                    "EXCEPTION" => false,
                    "null" => exp is "null" or "ghost" or "any",
                    "plays-ghost-only" => exp is "ghost" or "any",
                    "plays-with-tape" => exp == "any",
                    _ => exp == "any" && outc is "opened-then-Closing" or "opened-then-Closed",
                };
                VlCheck(ok && VlS(r, "active") == "0" && VlS(r, "viewer_ref") == "null" && (outc != "null" || !string.IsNullOrEmpty(VlS(r, "reason"))),
                    $"{label}: case {cs} (expected {exp}): outcome {outc}, reason '{VlS(r, "reason")}', state after waiting {VlS(r, "state_then")} ({VlS(r, "frames_until_not_building")} frames)");
            }
            VlCheck(rows.Count >= 19, $"{label}: all cases ran ({rows.Count} result lines)");
            VlCheck(live0.Count == rows.Count && live0.All(r => VlD(r, "diffs_nonaudio") == 0 && VlD(r, "diffs_paused") == 0),
                $"{label}: after every case the live scene is untouched (ProcessMode, Visible, hud, buses, shared statics, players' paused flags): {live0.Count(r => VlD(r, "diffs_nonaudio") == 0 && VlD(r, "diffs_paused") == 0)} of {live0.Count} cases clean; " + string.Join(" || ", live0.Where(r => VlD(r, "diffs_nonaudio") != 0 || VlD(r, "diffs_paused") != 0).Take(5).Select(r => VlS(r, "case") + ": " + VlS(r, "nonaudio") + " " + VlS(r, "paused"))));
            VlRmDir($"{VlTmp}-fail-{live}"); VlRmDir($"{VlTmp}-fail-{live}.xdg");
        }
    }

    // ------------------------------------------------------------------ 4: rapid close, open after close, quit

    void VlRapidTests()
    {
        foreach (var t in new[] { "ilyich", "bonus" })
        {
            if (!VlHasFx(t)) continue;
            var c = VlGame("rapid-" + t, VlFxArgs(t, null, "--viewer-lifecycle=rapid").Concat(VlTrackArgs(t)).ToArray(), 1500);
            string label = $"rapid close/open ({t})";
            VlChildOk(c, label);
            var rows = VlLines(c, "rapid");
            VlCheck(rows.Count == 7, $"{label}: all 7 sequences ran ({rows.Count})");
            foreach (var r in rows)
            {
                string cs = VlS(r, "case"), ex = VlS(r, "extra") ?? "";
                bool second = cs.StartsWith("open-right-after-closed");
                VlCheck(VlS(r, "state") == "Closed" && VlS(r, "restored_events") == "1" && VlS(r, "active") == "0" && VlS(r, "viewer_ref") == "null" && !ex.Contains("exc=") | ex.Contains("exc=none"),
                    $"{label}: {cs}: Closed, the Restored event fired {VlS(r, "restored_events")}x (expected 1), no active session, reference cleared {ex}");
                if (second) VlCheck(ex.Contains("second open ok") && ex.Contains("state Playing") && ex.Contains("restored events 1"), $"{label}: {cs}: the viewer opened right after Closed reached Playing and closed with one restore ({ex})");
                VlCheck(VlD(r, "orph") <= VlD(r, "base_orph") && VlD(r, "nodes") <= VlD(r, "base_nodes"), $"{label}: {cs}: orphans {VlS(r, "orph")} (before this sequence {VlS(r, "base_orph")}, at the start of the scenario {VlS(r, "start_orph")}), nodes {VlS(r, "nodes")}/{VlS(r, "base_nodes")} (after/baseline), objects {VlS(r, "obj")}/{VlS(r, "base_obj")}");
            }
            foreach (var r in VlLines(c, "rapid-live"))
                VlCheck(VlD(r, "diffs_nonaudio") == 0 && VlD(r, "diffs_paused") == 0, $"{label}: {VlS(r, "case")}: live scene unchanged (non-audio diffs {VlS(r, "diffs_nonaudio")}, paused diffs {VlS(r, "diffs_paused")}) {VlS(r, "nonaudio")} {VlS(r, "paused")}");
            VlRmDir($"{VlTmp}-rapid-{t}"); VlRmDir($"{VlTmp}-rapid-{t}.xdg");
        }
    }

    void VlQuitTests()
    {
        string t = "ilyich";
        if (!VlHasFx(t)) return;
        var results = new Dictionary<string, (TapeChild c, Dictionary<string, string> data)>();
        foreach (var mode in new[] { "control", "open", "building", "closing" })
        {
            string name = "quit-" + mode;
            var c = VlGame(name, VlFxArgs(t, null, "--viewer-lifecycle=quit-" + mode), 600);
            string dir = $"{VlTmp}-{name}";
            results[mode] = (c, VlHash(dir));
            VlCheck(c.Exit == 0 && !c.TimedOut && !c.Crashed, $"quit [{mode}]: exit code 0, no timeout, no crash (exit {c.Exit}, {c.Seconds:F0} s, crashed {c.Crashed})");
            var now = VlLines(c, "quit-now").FirstOrDefault();
            VlCheck(now != null, $"quit [{mode}]: the quit was issued in the intended state ({now?["session_state"] ?? "-"}, steps {now?["steps"] ?? "-"})");
            if (mode == "building") VlCheck(now != null && VlS(now, "session_state") == "Building", "quit [building]: the viewer was still Building when the game quit");
            if (mode == "open") VlCheck(now != null && VlS(now, "session_state") == "Playing", "quit [open]: the viewer was Playing when the game quit");
        }
        var ctl = results["control"].data;
        TapeInfo($"quit control (no viewer): {ctl.Count} files: {string.Join(", ", ctl.Keys.OrderBy(x => x))}");
        foreach (var mode in new[] { "open", "building", "closing" })
        {
            var d = results[mode].data;
            var added = d.Keys.Except(ctl.Keys).ToList(); var removed = ctl.Keys.Except(d.Keys).ToList();
            var changed = d.Where(kv => ctl.TryGetValue(kv.Key, out var h) && h != kv.Value).Select(kv => kv.Key).ToList();
            VlCheck(added.Count == 0 && removed.Count == 0, $"quit [{mode}]: the data dir holds exactly the control's {ctl.Count} files (files {d.Count}; added {added.Count} [{string.Join(", ", added)}], removed {removed.Count} [{string.Join(", ", removed)}])");
            var risky = changed.Where(f => f.Contains("ghost") || f.Contains("tape") || f.Contains("records") || f.Contains("laps") || f.Contains("profile")).ToList();
            VlCheck(risky.Count == 0, $"quit [{mode}]: no record/ghost/tape/lap/profile file differs from the control (differing files overall {changed.Count} [{string.Join(", ", changed)}], risky {risky.Count} [{string.Join(", ", risky)}])");
            VlCheck(!d.Keys.Any(f => f.Contains("laps") || f.EndsWith("ghost.bin") || f.EndsWith("tape.bin")), $"quit [{mode}]: no lap archive or ghost/tape file was written (lap-related files {d.Keys.Count(f => f.Contains("laps") || f.Contains("ghost") || f.Contains("tape"))})");
        }
        foreach (var m in new[] { "control", "open", "building", "closing" }) { VlRmDir($"{VlTmp}-quit-{m}"); VlRmDir($"{VlTmp}-quit-{m}.xdg"); }
    }

    // ------------------------------------------------------------------ 5: audio

    void VlAudioTests()
    {
        foreach (var t in new[] { "bonus", "ilyich" })
        {
            if (!VlHasFx(t)) continue;
            var c = VlGame("audio-" + t, VlFxArgs(t, null, "--viewer-lifecycle=audio").Concat(VlTrackArgs(t)).ToArray(), 900);
            string label = $"audio ({t})";
            VlChildOk(c, label);
            var rows = VlLines(c, "audio");
            VlCheck(rows.Count == 2, $"{label}: two open/close rounds measured ({rows.Count})");
            foreach (var r in rows)
            {
                string p = $"{label} round {VlS(r, "round")}";
                TapeInfo($"{p}: {VlS(r, "players_before")} players, paused before {VlS(r, "paused_before")}, playing before {VlS(r, "playing_before")}; after suspend {VlS(r, "paused_after_suspend")}; during {VlS(r, "paused_during")}; at Closed {VlS(r, "closed_paused")}; mismatches closed/+1/+3/+63 frames: {VlS(r, "mismatch_closed")}/{VlS(r, "mismatch_plus1")}/{VlS(r, "mismatch_plus3")}/{VlS(r, "mismatch_plus63")}; gone {VlS(r, "gone_closed")} new {VlS(r, "new_closed")}");
                VlCheck(VlD(r, "players_before") > 0, $"{p}: the live scene has audio players to compare ({VlS(r, "players_before")}, {VlS(r, "paused_before")} paused before)");
                VlCheck(VlS(r, "closed_paused").StartsWith(VlS(r, "paused_before") + "p"), $"{p}: the number of paused players at Closed ({VlS(r, "closed_paused")}) equals the number before ({VlS(r, "paused_before")})");
                VlCheck(VlD(r, "mismatch_closed") == 0 && VlD(r, "gone_closed") == 0, $"{p}: every player's StreamPaused flag at the Closed frame equals its flag before the open (mismatches {VlS(r, "mismatch_closed")}: {VlS(r, "sample_closed")})");
                VlCheck(VlD(r, "mismatch_plus1") == 0, $"{p}: and one frame later (the engine's own unpause on the mode change has run): mismatches {VlS(r, "mismatch_plus1")}");
                VlCheck(VlD(r, "mismatch_plus3") == 0, $"{p}: and three frames later: mismatches {VlS(r, "mismatch_plus3")}: {VlS(r, "sample_plus3")}");
            }
            VlRmDir($"{VlTmp}-audio-{t}"); VlRmDir($"{VlTmp}-audio-{t}.xdg");
        }
    }

    // ------------------------------------------------------------------ 6: writes

    void VlWriteTests()
    {
        // (name, live track, lap track, seed data dir from a producer of this track or null for a fresh dir)
        var cases = new (string name, string live, string lap, string seed)[]
        {
            ("fresh-same", "ilyich", "ilyich", null),
            ("fresh-cross", "siberia", "ilyich", null),
            ("records-of-lap-track", "ilyich", "siberia", "siberia"),
            ("records-of-third-track", "ilyich", "siberia", "atomgrad"),
            ("records-same-track", "ilyich", "ilyich", "ilyich"),
        };
        foreach (var (name, live, lap, seed) in cases)
        {
            if (!VlHasFx(live) || !VlHasFx(lap) || (seed != null && !Directory.Exists(Path.Combine(VlFxData, seed)))) { Skip($"writes {name}: fixtures missing"); continue; }
            string cname = "writes-" + name, dir = $"{VlTmp}-{cname}";
            var c = VlGame(cname, VlFxArgs(live, lap, "--viewer-lifecycle=writes", "--vl-roots=" + dir + ";" + dir + ".xdg").Concat(VlTrackArgs(live)).ToArray(), 900, seedFrom: seed == null ? null : Path.Combine(VlFxData, seed));
            string label = $"writes [{name}] live {live}, lap {lap}, data dir {(seed == null ? "fresh" : "with the records of " + seed)}";
            VlChildOk(c, label);
            var rows = VlLines(c, "writes");
            VlCheck(rows.Count == 2, $"{label}: both trees (data dir and XDG user dir) were hashed ({rows.Count})");
            foreach (var r in rows)
            {
                bool xdg = VlS(r, "root").EndsWith(".xdg");
                VlCheck(VlD(r, "diff_during") == 0 && VlD(r, "diff_at_closed") == 0, $"{label}: {(xdg ? "XDG user dir" : "data dir")} ({VlS(r, "files_before")} files hashed name+size+sha256): changed while the viewer ran {VlS(r, "diff_during")}, at Closed {VlS(r, "diff_at_closed")} {VlS(r, "during")} {VlS(r, "at_closed")}");
                TapeInfo($"{label}: {(xdg ? "XDG" : "data")} after 120 further live frames: {VlS(r, "diff_after_120_live_frames")} difference(s) {VlS(r, "later")}");
            }
            VlRmDir(dir); VlRmDir(dir + ".xdg");
        }
    }

    // ------------------------------------------------------------------ 7: stall + memory

    void VlStallTests(string[] tracks)
    {
        var xvfb = new[] { "Xvfb", "xvfb-run", "weston", "Xwayland" }.Where(x => (System.Environment.GetEnvironmentVariable("PATH") ?? "").Split(':').Any(d => File.Exists(Path.Combine(d, x)))).ToList();
        TapeInfo($"display tools found (looked for Xvfb, xvfb-run, weston, Xwayland): [{string.Join(", ", xvfb)}]. Xwayland alone is not a display: it needs a running Wayland compositor and the only one is the user's own desktop, which this test never uses. Without a virtual display the windowed frame stalls are UNVERIFIED: the children below are headless with the dummy renderer and audio, real frame times of the CPU-side work only, no GPU upload or shader compile stalls.");
        var combos = tracks.Select(t => (live: t, lap: t)).ToList();
        if (tracks.Contains("ilyich") && VlHasFx("siberia")) combos.Add(("ilyich", "siberia"));
        foreach (var (live, lap) in combos)
        {
            if (!VlHasFx(lap)) continue;
            string name = $"stall-{live}" + (live != lap ? "-lap-" + lap : "");
            var c = VlGame(name, VlFxArgs(live, lap, "--viewer-lifecycle=stall").Concat(VlTrackArgs(live)).ToArray(), 900, fixedFps: false);
            string label = $"stall live {live}, lap {lap}";
            VlChildOk(c, label);
            var s = VlLines(c, "stall").FirstOrDefault(); var m = VlLines(c, "mem").FirstOrDefault(); var sy = VlLines(c, "sync").FirstOrDefault();
            VlCheck(s != null && VlS(s, "state") == "Playing" && VlD(s, "build_frames") > 0 && VlD(s, "build_max_ms") > 0, $"{label}: the sliced build was measured and the viewer reached Playing (state {VlS(s ?? new(), "state")}, build frames {VlS(s ?? new(), "build_frames")})");
            if (s != null) TapeInfo($"{label}: SLICED BUILD (headless, real wall time, NOT fixed-fps): elapsed {VlS(s, "build_total_ms")} ms over {VlS(s, "build_frames")} frames ({VlS(s, "build_steps")} steps), OpenReplayViewer call itself {VlS(s, "open_call_ms")} ms; frame time max {VlS(s, "build_max_ms")} ms, p99 {VlS(s, "build_p99_ms")} ms, median {VlS(s, "build_p50_ms")} ms, frames >100 ms {VlS(s, "build_over100")}, >250 ms {VlS(s, "build_over250")}; playing max {VlS(s, "play_max_ms")} ms p99 {VlS(s, "play_p99_ms")} ms; live scene before: {VlS(s, "live_frames")}");
            var top = VlLines(c, "stall-top").FirstOrDefault();
            if (top != null) TapeInfo($"{label}: ten worst build frames (ms): {VlS(top, "build_frames_ms_top10")}");
            if (sy != null) TapeInfo($"{label}: SYNCHRONOUS nested build of the same track (one AddChild of an instruction-mode RaceScene, the pre-viewer way): {VlS(sy, "addchild_ms")} ms in one frame, first frame after {VlS(sy, "first_frame_ms")} ms (still booting {VlS(sy, "booting_after")})");
            VlCheck(sy != null, $"{label}: the synchronous comparison build was measured");
            if (m != null)
            {
                TapeInfo($"{label}: MEMORY (dummy renderer/audio, a measurement not a guarantee): RSS baseline {VlS(m, "rss_base")} MB (boot-time HWM {VlS(m, "hwm_base")}: read before the peak reset took effect, so it can exceed the open RSS; the peak below is only meaningful when it is above it), viewer open {VlS(m, "rss_open")} MB, peak during build+play {VlS(m, "hwm_peak")} MB, after close + GC + malloc_trim {VlS(m, "rss_closed_trim")} MB; objects {VlS(m, "obj_base")} -> {VlS(m, "obj_closed")}, nodes {VlS(m, "nodes_base")} -> {VlS(m, "nodes_closed")}, orphans {VlS(m, "orph_base")} -> {VlS(m, "orph_closed")}");
                VlCheck(VlD(m, "rss_base") > 0 && VlD(m, "rss_open") > 0 && VlD(m, "rss_closed_trim") > 0 && VlD(m, "hwm_peak") >= VlD(m, "rss_base"), $"{label}: memory samples are present and the open peak is not below the baseline");
            }
            VlRmDir($"{VlTmp}-{name}"); VlRmDir($"{VlTmp}-{name}.xdg");
        }
    }
}
