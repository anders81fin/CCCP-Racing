using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Tests;

// --viewer-windowed-tests: launches WINDOWED (and one headless) children of the real game and judges their VIEWERWIN lines (see RaceScene.ViewerWindowedTest.cs / ...Gpu.cs).
// The parent runs headless. One child at a time, each with its own data dir (/tmp/cccp-win-N) and XDG_DATA_HOME, a small window (--resolution 960x540), killed by its own PID at the timeout.
// Children capture only their own viewports (SubViewport textures); nothing of the desktop. Logs go to <main checkout>/build/player-replay/phase2/logs/windowed/, PNGs to
// <main checkout>/media/viewer-phase2-windowed-20261027/ (both ignored by git; a name that exists is never overwritten: the child adds a suffix, the logs get a run stamp).
// Options: --viewer-windowed-only=cover-intro,cover-start,headless,loadcard,gpu (default: all), --viewer-windowed-tracks=ilyich,siberia,atomgrad,bonus, --vw-main=DIR (output root),
// --vw-resolution=960x540, --vw-keep (keep the /tmp data dirs).
public partial class PhysicsTest
{
    int _vwPass, _vwFail, _vwChildren, _vwFindings;

    static bool ViewerWindowedChildHook(PhysicsTest host)
    {
        if (OS.GetCmdlineUserArgs().Any(a => a.StartsWith("--viewer-windowed="))) { RaceScene.ViewerWindowedBegin(host); return true; }
        return false;
    }

    string VwOutRoot()
    {
        string a = OS.GetCmdlineUserArgs().FirstOrDefault(x => x.StartsWith("--vw-main="))?["--vw-main=".Length..];
        if (a != null) return a;
        string proj = TapeProjectDir.TrimEnd('/');
        int i = proj.IndexOf("/.claude/worktrees/", StringComparison.Ordinal);
        return i >= 0 ? proj[..i] : proj;
    }

    static string VwOpt(string name) => OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith(name + "="))?[(name.Length + 1)..];

    void VwCheckP(bool ok, string what) { if (ok) _vwPass++; else _vwFail++; TapeCheck(ok, what); }

    /// <summary>One game child to its end (or killed at the timeout); the log is kept (suffix -N when the name exists).</summary>
    (TapeChild child, string log) VwChild(string name, bool headless, string[] gameArgs, int timeoutSec, string sharedDir = null)
    {
        int n = ++_vwChildren;
        string dir = sharedDir ?? $"/tmp/cccp-win-{n}";
        if (sharedDir == null) foreach (var d in new[] { dir, dir + ".xdg" }) try { if (Directory.Exists(d)) Directory.Delete(d, true); } catch { }      // (a shared dir carries one scenario's state from one child to the next: the parent owns it)
        Directory.CreateDirectory(dir);
        string root = VwOutRoot();
        string logDir = Path.Combine(root, "build", "player-replay", "phase2", "logs", "windowed");
        string media = Path.Combine(root, "media", VwOpt("--vw-media-dir") ?? "viewer-phase2-windowed-20261027");
        Directory.CreateDirectory(logDir); Directory.CreateDirectory(media);
        string log = Path.Combine(logDir, $"{name}.log");
        for (int i = 2; File.Exists(log); i++) log = Path.Combine(logDir, $"{name}-{i}.log");
        var psi = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
        var head = new List<string> { "-c", "exec \"$0\" \"$@\" > \"$VW_OUT\" 2>&1", OS.GetExecutablePath() };
        if (headless) head.Add("--headless"); else { head.Add("--resolution"); head.Add(VwOpt("--vw-resolution") ?? "960x540"); }
        head.AddRange(new[] { "--path", TapeProjectDir, "res://scenes/tests/PhysicsTest.tscn", "--" });
        foreach (var s in head) psi.ArgumentList.Add(s);
        foreach (var a in gameArgs.Concat(new[] { "--babushka-seed=1", "--rng-seed=1", "--data-dir=" + dir, "--vw-media=" + media })) psi.ArgumentList.Add(a);
        psi.Environment["VW_OUT"] = log;
        psi.Environment["XDG_DATA_HOME"] = dir + ".xdg";
        psi.Environment.Remove("CCCP_TAPE");
        var sw = Stopwatch.StartNew();
        using var p = Process.Start(psi);
        bool timedOut = false, floated = headless; int floatTries = 0;
        while (!p.WaitForExit(250))
        {
            if (!floated && floatTries < 80 && sw.Elapsed.TotalSeconds > 0.5) { floatTries++; floated = VwFloat(p.Id); }      // (Hyprland tiles a new window full screen: float and size this child's own window by its pid)
            if (sw.Elapsed.TotalSeconds > timeoutSec) { timedOut = true; try { p.Kill(true); } catch { } p.WaitForExit(); break; }      // (only this runner's own child, by its PID)
        }
        if (!headless) TapeInfo($"window floated and sized by pid: {floated} (tries {floatTries})");
        var lines = File.Exists(log) ? File.ReadAllLines(log) : Array.Empty<string>();
        var c = new TapeChild(p.ExitCode, timedOut, lines, sw.Elapsed.TotalSeconds);
        TapeInfo($"child {name}: {(headless ? "headless" : "WINDOWED")}, exit {c.Exit}, {c.Seconds:F0} s{(timedOut ? ", TIMED OUT" : "")}, 'resources still in use at exit' lines {lines.Count(l => l.Contains("resources still in use at exit"))}, log {log}");
        if (sharedDir == null && !Directory.Exists(dir + ".none") && OS.GetCmdlineUserArgs().All(a => a != "--vw-keep")) { try { Directory.Delete(dir, true); } catch { } try { Directory.Delete(dir + ".xdg", true); } catch { } }
        return (c, log);
    }

    static string VwHypr(params string[] args)
    {
        var psi = new ProcessStartInfo("hyprctl") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var q = Process.Start(psi);
        string o = q.StandardOutput.ReadToEnd();
        q.WaitForExit(5000);
        return o;
    }

    /// <summary>Float and size the game's own window (by pid) to 768x432 logical = 960x540 physical at scale 1.25; false until the window exists.</summary>
    static bool VwFloat(int pid)
    {
        try
        {
            if (!VwHypr("clients", "-j").Contains($"\"pid\": {pid},")) return false;
            VwHypr("dispatch", $"hl.dsp.window.fullscreen_state({{ internal = 0, client = 0, window = \"pid:{pid}\" }})");
            VwHypr("dispatch", $"hl.dsp.window.float({{ action = \"enable\", window = \"pid:{pid}\" }})");
            VwHypr("dispatch", $"hl.dsp.window.resize({{ x = 768, y = 432, relative = false, window = \"pid:{pid}\" }})");
            return true;
        }
        catch { return false; }
    }

    static Dictionary<string, string> VwParse(string line, string tag)
    {
        string prefix = "VIEWERWIN: " + tag;
        if (!line.StartsWith(prefix) || (line.Length > prefix.Length && line[prefix.Length] != '|')) return null;
        var d = new Dictionary<string, string>();
        foreach (var kv in line[prefix.Length..].Split('|', StringSplitOptions.RemoveEmptyEntries)) { int i = kv.IndexOf('='); if (i > 0) d[kv[..i]] = kv[(i + 1)..]; }
        return d;
    }

    /// <summary>Judge a child: it ended cleanly, reached its end, and every check line it printed passed. Findings are listed, never counted as failures.</summary>
    void VwJudge(string name, TapeChild c, bool expectChecks = true)
    {
        bool clean = !c.TimedOut && c.Exit == 0 && !c.Crashed && !c.Lines.Any(l => l.Contains("VIEWERWIN: fatal") || l.Contains("SCRIPT ERROR") || l.Contains("Unhandled exception"));
        VwCheckP(clean, $"{name}: the child ended with exit 0, no timeout, no crash, no fatal line (exit {c.Exit}, {c.Seconds:F0} s, timed out {c.TimedOut}, crashed {c.Crashed}, fatal {c.Find("VIEWERWIN: fatal")})");
        VwCheckP(c.Has("VIEWERWIN: script-done"), $"{name}: the script reached its end");
        int ok = 0, bad = 0;
        foreach (var l in c.Lines)
        {
            var d = VwParse(l, "check");
            if (d == null) continue;
            bool pass = d["ok"] == "True";
            if (pass) ok++; else bad++;
            VwCheckP(pass, $"{name}: {d.GetValueOrDefault("what")}");
        }
        VwCheckP(!expectChecks || ok + bad > 0, $"{name}: the child printed checks ({ok} passed, {bad} failed)");
        foreach (var l in c.Lines)
        {
            var f = VwParse(l, "finding");
            if (f != null) { _vwFindings++; TapeInfo($"FINDING [{name}] {f.GetValueOrDefault("id")}: {f.GetValueOrDefault("text")}"); }
        }
        foreach (var l in c.Lines.Where(l => l.StartsWith("VIEWERWIN: ") && !l.StartsWith("VIEWERWIN: check") && !l.StartsWith("VIEWERWIN: framesraw"))) TapeInfo(l["VIEWERWIN: ".Length..]);
    }

    void ViewerWindowedTests()
    {
        GD.Print("Viewer windowed tests (real game windows, one child at a time)");
        if (!OperatingSystem.IsLinux()) { Skip("viewer windowed: the children go through /bin/sh (Linux only)"); return; }
        if (DisplayServer.GetName() != "headless") { TapeInfo("note: the parent is expected to run headless; it opens no window of its own"); }
        string wl = System.Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"), x11 = System.Environment.GetEnvironmentVariable("DISPLAY");
        if (string.IsNullOrEmpty(wl) && string.IsNullOrEmpty(x11)) { VwCheckP(false, "no display (WAYLAND_DISPLAY and DISPLAY are unset): no window can be opened, nothing was run"); return; }
        var only = (VwOpt("--viewer-windowed-only") ?? "cover-intro,cover-start,headless,loadcard,gpu,baseline,seek,particle").Split(',');
        var tracks = (VwOpt("--viewer-windowed-tracks") ?? "ilyich,siberia,atomgrad,bonus").Split(',');
        string fx = "/tmp/cccp-lc-fx";
        bool On(string n) => only.Contains(n);
        VlFixtures(new[] { "ilyich", "siberia" });
        string[] fxArgs = { "--vl-fx=" + fx };
        try
        {
            if (On("headless"))
            {
                var (c, _) = VwChild("headless-input-ilyich", true, new[] { "--viewer-windowed=headless-input", "--start", "--autodrive", "--track=ilyich", "--vw-lap=ilyich", "--no-intro" }.Concat(fxArgs).ToArray(), 600);
                VwJudge("headless-input", c);
            }
            if (On("cover-intro"))
            {
                var (c, _) = VwChild("cover-intro-ilyich", false, new[] { "--viewer-windowed=cover-intro", "--cover", "--track=ilyich", "--vw-lap=ilyich" }.Concat(fxArgs).ToArray(), 600);
                VwJudge("cover-intro", c);
            }
            if (On("cover-start"))
            {
                var (c, _) = VwChild("cover-start-ilyich", false, new[] { "--viewer-windowed=cover-start", "--cover", "--no-intro", "--start", "--autodrive", "--track=ilyich", "--vw-lap=ilyich" }.Concat(fxArgs).ToArray(), 600);
                VwJudge("cover-start", c);
            }
            if (On("loadcard"))
            {
                var (a, _) = VwChild("loadcard-a-ilyich", false, new[] { "--viewer-windowed=loadcard-a", "--cover", "--no-intro", "--start", "--autodrive", "--track=ilyich", "--vw-lap=ilyich" }.Concat(fxArgs).ToArray(), 600);
                VwJudge("loadcard-a", a);
                var (b, _) = VwChild("loadcard-b-ilyich", false, new[] { "--viewer-windowed=loadcard-b", "--cover", "--no-intro", "--start", "--autodrive", "--track=ilyich", "--vw-lap=ilyich" }.Concat(fxArgs).ToArray(), 600);
                VwJudge("loadcard-b", b);
            }
            if (On("baseline"))
            {
                foreach (var (t, from) in new[] { ("ilyich", "bonus"), ("siberia", "bonus"), ("atomgrad", "bonus"), ("bonus", "ilyich") })
                {
                    var (c, _) = VwChild("load-baseline-" + t, false, new[] { "--viewer-windowed=load-baseline", "--vw-load-target=" + t, "--track=" + from, "--no-intro" }, 600);
                    VwJudge("load-baseline-" + t, c);
                }
            }
            if (On("loadbar"))      // (not in the default set: the normal loading card's bar per track, plus a fast same-track load with a pad press)
            {
                foreach (var (t, from, pad) in new[] { ("ilyich", "bonus", false), ("siberia", "bonus", false), ("atomgrad", "bonus", false), ("bonus", "ilyich", false), ("ilyich", "ilyich", true) })
                {
                    var args = new List<string> { "--viewer-windowed=load-bar", "--vw-load-target=" + t, "--track=" + from, "--no-intro" };
                    if (pad) args.Add("--vw-load-pad");
                    var (c, _) = VwChild($"load-bar-{from}-{t}{(pad ? "-fast-pad" : "")}", false, args.ToArray(), 600);
                    VwJudge($"load-bar-{from}-{t}", c);
                }
            }
            if (On("build"))      // (not in the default set: the viewer build alone per track, --vw-rounds=N repeats; run with CCCP_LOADPROF=1 for the checkpoint profile)
            {
                foreach (var t in tracks)
                {
                    var (c, _) = VwChild("build-diag-" + t, false, new[] { "--viewer-windowed=build-diag", "--track=" + t, "--no-intro", "--vw-rounds=" + (VwOpt("--vw-rounds") ?? "1") }.Concat(fxArgs).ToArray(), 600);
                    VwJudge("build-diag-" + t, c);
                }
            }
            if (On("seek"))
            {
                foreach (var t in new[] { "bonus", "ilyich" })
                {
                    var args = new List<string> { "--viewer-windowed=seek-diag", "--start", "--autodrive", "--track=" + t, "--vw-lap=" + t, "--no-intro" };
                    if (t == "bonus") args.Add("--ap-speed=0.8");
                    var (c, _) = VwChild("seek-diag-" + t, false, args.Concat(fxArgs).ToArray(), 900);
                    VwJudge("seek-diag-" + t, c);
                }
            }
            if (On("audio"))
            {
                var (c, _) = VwChild("hind-audio-bonus", false, new[] { "--viewer-windowed=hind-audio", "--start", "--autodrive", "--track=bonus", "--vw-lap=bonus", "--no-intro", "--ap-speed=0.8" }.Concat(fxArgs).ToArray(), 900);
                VwJudge("hind-audio-bonus", c);
            }
            if (On("replayui"))
            {
                var (c, _) = VwChild("replay-ui-ilyich", false, new[] { "--viewer-windowed=replay-ui", "--start", "--autodrive", "--track=ilyich", "--no-intro" }.Concat(fxArgs).ToArray(), 900);
                VwJudge("replay-ui-ilyich", c);
            }
            if (On("camcompare"))
            {
                var (c, _) = VwChild("cam-compare-ilyich", false, new[] { "--viewer-windowed=cam-compare", "--start", "--autodrive", "--track=ilyich", "--vw-lap=ilyich", "--no-intro" }.Concat(fxArgs).ToArray(), 600);
                VwJudge("cam-compare-ilyich", c);
            }
            if (On("camcontrolled"))
            {
                var (c, _) = VwChild("cam-controlled-ilyich", false, new[] { "--viewer-windowed=cam-controlled", "--start", "--autodrive", "--track=ilyich", "--vw-lap=ilyich", "--no-intro" }.Concat(fxArgs).ToArray(), 600);
                VwJudge("cam-controlled-ilyich", c);
            }
            if (On("contexts"))
            {
                var (c, _) = VwChild("replay-contexts-ilyich", false, new[] { "--viewer-windowed=replay-contexts", "--start", "--autodrive", "--track=ilyich", "--vw-lap=ilyich", "--no-intro" }.Concat(fxArgs).ToArray(), 600);
                VwJudge("replay-contexts-ilyich", c);
            }
            if (On("camunlocked"))
            {
                var (c, _) = VwChild("cam-unlocked-ilyich", false, new[] { "--viewer-windowed=cam-unlocked", "--start", "--autodrive", "--track=ilyich", "--vw-lap=ilyich", "--no-intro" }.Concat(fxArgs).ToArray(), 600);
                VwJudge("cam-unlocked-ilyich", c);
            }
            if (On("menus"))
            {
                var (c, _) = VwChild("menus-ilyich", false, new[] { "--viewer-windowed=menus", "--start", "--autodrive", "--track=ilyich", "--no-intro" }.Concat(fxArgs).ToArray(), 600);
                VwJudge("menus-ilyich", c);
            }
            if (On("titlemenu"))
            {
                var (c, _) = VwChild("title-menu", false, new[] { "--viewer-windowed=title-menu", "--no-intro" }.Concat(fxArgs).ToArray(), 300);
                VwJudge("title-menu", c);
            }
            if (On("loadingstill"))
            {
                var (c, _) = VwChild("loading-still-ilyich", false, new[] { "--viewer-windowed=loading-still", "--start", "--autodrive", "--track=ilyich", "--vw-lap=ilyich", "--no-intro" }.Concat(fxArgs).ToArray(), 600);
                VwJudge("loading-still-ilyich", c);
            }
            if (On("normcard"))
            {
                foreach (var t in new[] { "ilyich", "siberia", "atomgrad", "bonus" })
                {
                    var (c, _) = VwChild("norm-card-" + t, false, new[] { "--viewer-windowed=norm-card", "--start", "--autodrive", "--track=" + t, "--no-intro" }.Concat(fxArgs).ToArray(), 300);
                    VwJudge("norm-card-" + t, c);
                }
            }
            if (On("rallystart"))
            {
                var (c, _) = VwChild("rally-start-fresh", false, new[] { "--viewer-windowed=rally-start", "--track=ilyich", "--no-intro" }.Concat(fxArgs).ToArray(), 300);
                VwJudge("rally-start-fresh", c);
                var (d, _) = VwChild("rally-start-resume", false, new[] { "--viewer-windowed=rally-start-resume", "--track=ilyich", "--no-intro" }.Concat(fxArgs).ToArray(), 300);
                VwJudge("rally-start-resume", d);
            }
            if (On("sessionbest"))
            {
                var (c, _) = VwChild("session-best-ilyich", false, new[] { "--viewer-windowed=session-best", "--start", "--autodrive", "--track=ilyich", "--vw-lap=ilyich", "--no-intro" }.Concat(fxArgs).ToArray(), 400);
                VwJudge("session-best-ilyich", c);
            }
            if (On("exportui"))
            {
                string exDir = VwOpt("--vw-export-dir") ?? Path.Combine(TapeProjectDir, "media", "replay-export-20261028", "ui");
                Directory.CreateDirectory(exDir);
                var (cx, _) = VwChild("export-ui", false, new[] { "--viewer-windowed=export-ui", "--no-intro", "--vw-fx=/tmp/cccp-lc-fx", "--vw-native=" + (VwOpt("--vw-native") ?? "0"), "--export-input-only=" + (VwOpt("--export-input-only") ?? "0"), "--export-dir=" + exDir, "--export-jobs-dir=" + Path.Combine(exDir, "jobs") }.Concat(fxArgs).ToArray(), 900);
                VwJudge("export-ui", cx);
            }
            if (On("editsbonus"))
            {
                var (cb, _) = VwChild("edits-bonus", false, new[] { "--viewer-windowed=edits-bonus", "--start", "--training", "--autodrive", "--track=bonus", "--no-intro" }.Concat(fxArgs).ToArray(), 480);
                VwJudge("edits-bonus", cb);
            }
            if (On("edits"))
            {
                string shared = $"/tmp/cccp-win-edits-{System.Environment.ProcessId}";
                foreach (var d in new[] { shared, shared + ".xdg" }) try { if (Directory.Exists(d)) Directory.Delete(d, true); } catch { }
                Directory.CreateDirectory(shared);
                var (c1, _) = VwChild("edits-keep", false, new[] { "--viewer-windowed=edits-keep", "--start", "--training", "--autodrive", "--track=ilyich", "--no-intro" }.Concat(fxArgs).ToArray(), 420, shared);
                VwJudge("edits-keep", c1);
                var (c2, _) = VwChild("edits-save", false, new[] { "--viewer-windowed=edits-save", "--no-intro" }.Concat(fxArgs).ToArray(), 420, shared);
                VwJudge("edits-save", c2);
                // between the processes: the original pair's SHA-256, and a TWIN lap (same files, one tape byte flipped at the same size) with the original's edits.json beside it
                var hashes = new List<string>();
                string lapsDir = null;
                foreach (var f in Directory.EnumerateFiles(shared, "*", SearchOption.AllDirectories).Where(f => (Path.GetFileName(f) is "ghost.bin" or "tape.bin") && f.Contains("/laps/")))
                {
                    string laps = f[..(f.IndexOf("/laps/") + 6)]; lapsDir = laps.TrimEnd('/');
                    hashes.Add(Path.GetRelativePath(laps, f) + "\t" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(f))));
                }
                File.WriteAllLines(Path.Combine(shared, "hashes1.txt"), hashes);
                VwCheckP(hashes.Count >= 2 && lapsDir != null, $"the second process left the kept pair ({hashes.Count} files)");
                if (lapsDir != null)
                {
                    string orig = Directory.GetDirectories(lapsDir).First(); string id = Path.GetFileName(orig), twin = Path.Combine(lapsDir, id + "-twin");
                    Directory.CreateDirectory(twin);
                    foreach (var f in Directory.GetFiles(orig)) File.Copy(f, Path.Combine(twin, Path.GetFileName(f)));
                    string meta = File.ReadAllText(Path.Combine(twin, "meta.json")).Replace(id, id + "-twin");
                    meta = System.Text.RegularExpressions.Regex.Replace(meta, "\"Name\":\\s*\"[^\"]*\"", "\"Name\": \"TWIN\"");
                    File.WriteAllText(Path.Combine(twin, "meta.json"), meta);
                    string tp = Path.Combine(twin, "tape.bin"); var tb = File.ReadAllBytes(tp); tb[tb.Length / 2] ^= 0x01; File.WriteAllBytes(tp, tb);
                    VwCheckP(File.Exists(Path.Combine(orig, "edits.json")) && File.Exists(Path.Combine(twin, "edits.json")) && new FileInfo(tp).Length == new FileInfo(Path.Combine(orig, "tape.bin")).Length, "the twin has the original's edits.json and a same-size tape that differs by one byte");
                }
                if (VwOpt("--vw-edits-children") is string kids && !kids.Contains("reload")) goto editsDone;      // (a diagnostic run of the first two processes only)
                var (c3, _) = VwChild("edits-reload", false, new[] { "--viewer-windowed=edits-reload", "--no-intro", "--vw-hashes=" + Path.Combine(shared, "hashes1.txt") }.Concat(fxArgs).ToArray(), 420, shared);
                VwJudge("edits-reload", c3);
                editsDone:
                if (OS.GetCmdlineUserArgs().All(a => a != "--vw-keep")) foreach (var d in new[] { shared, shared + ".xdg" }) try { Directory.Delete(d, true); } catch { }
            }
            if (On("recordings"))
            {
                string shared = $"/tmp/cccp-win-rec-{System.Environment.ProcessId}";
                foreach (var d in new[] { shared, shared + ".xdg" }) try { if (Directory.Exists(d)) Directory.Delete(d, true); } catch { }
                Directory.CreateDirectory(shared);
                var (c1, _) = VwChild("recordings-keep", false, new[] { "--viewer-windowed=recordings-keep", "--start", "--training", "--autodrive", "--track=ilyich", "--no-intro" }.Concat(fxArgs).ToArray(), 300, shared);
                VwJudge("recordings-keep", c1);
                // between the processes: the SHA-256 of every kept file, and a damaged lap folder beside them
                var hashes = new List<string>();
                foreach (var f in Directory.EnumerateFiles(shared, "*", SearchOption.AllDirectories).Where(f => (Path.GetFileName(f) is "ghost.bin" or "tape.bin") && f.Contains("/laps/")))
                {
                    string laps = f[..(f.IndexOf("/laps/") + 6)];
                    hashes.Add(Path.GetRelativePath(laps, f) + "\t" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(f))));
                    var lapsDir = laps.TrimEnd('/');
                    string dmg = Path.Combine(lapsDir, "damaged-test"); Directory.CreateDirectory(dmg); File.WriteAllText(Path.Combine(dmg, "meta.json"), "{ this is not json");
                }
                File.WriteAllLines(Path.Combine(shared, "hashes1.txt"), hashes);
                VwCheckP(hashes.Count >= 4, $"the first process left {hashes.Count} kept files (two files per kept lap, three kept)");
                var (c2, _) = VwChild("recordings-restart", false, new[] { "--viewer-windowed=recordings-restart", "--no-intro", "--vw-hashes=" + Path.Combine(shared, "hashes1.txt") }.Concat(fxArgs).ToArray(), 300, shared);
                VwJudge("recordings-restart", c2);
                if (OS.GetCmdlineUserArgs().All(a => a != "--vw-keep")) foreach (var d in new[] { shared, shared + ".xdg" }) try { Directory.Delete(d, true); } catch { }
            }
            if (On("stakhanovstart"))
            {
                var (c, _) = VwChild("stakhanov-start-ilyich", false, new[] { "--viewer-windowed=stakhanov-start", "--track=ilyich", "--no-intro" }.Concat(fxArgs).ToArray(), 300);
                VwJudge("stakhanov-start-ilyich", c);
            }
            if (On("native"))
            {
                var (c, _) = VwChild("native-render-ilyich", false, new[] { "--viewer-windowed=native-render", "--start", "--autodrive", "--track=ilyich", "--no-intro" }.Concat(fxArgs).ToArray(), 500);
                VwJudge("native-render-ilyich", c);
            }
            if (On("hudtoggles"))
            {
                var (c, _) = VwChild("hud-toggles-ilyich", false, new[] { "--viewer-windowed=hud-toggles", "--start", "--autodrive", "--track=ilyich", "--no-intro" }.Concat(fxArgs).ToArray(), 400);
                VwJudge("hud-toggles-ilyich", c);
            }
            if (On("particle"))
            {
                var (c, _) = VwChild("gpu-particle-atomgrad", false, new[] { "--viewer-windowed=gpu-particle", "--start", "--autodrive", "--track=atomgrad", "--vw-lap=atomgrad", "--no-intro" }.Concat(fxArgs).ToArray(), 900);
                VwJudge("gpu-particle-atomgrad", c);
            }
            if (On("gpu"))
            {
                foreach (var t in tracks)
                {
                    var args = new List<string> { "--viewer-windowed=gpu", "--start", "--autodrive", "--track=" + t, "--lap-archive-retain-all", "--no-intro" };
                    if (t == "bonus") args.Add("--ap-speed=0.8");
                    if (t == "ilyich") args.Add("--vw-cross=siberia");
                    var (c, _) = VwChild("gpu-" + t, false, args.Concat(fxArgs).ToArray(), 1800);
                    VwJudge("gpu-" + t, c);
                }
            }
        }
        finally
        {
            GD.Print($"VIEWERWINDOWED SUMMARY: {_vwPass} checks passed, {_vwFail} failed, {_vwFindings} findings (listed above as FINDING)");
        }
        GD.Print(_failures == 0 ? "Viewer windowed tests: all passed" : $"Viewer windowed tests: {_failures} FAILED");
    }
}
