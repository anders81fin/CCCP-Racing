using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Godot;

namespace CCCPRacing.Tests;

// --viewer-freeze-tests (phase 2 spike 2): does ProcessMode.Disabled + an explicit audio pause freeze the live race, what do the nested world's hooks and ExitTree do to the live
// world, and does a plain run repeat. One headless child game at a time (TapeGame, PhysicsTest.Tape.Scene.cs) under /tmp/cccp-spike2-*; the child's script is
// RaceScene.ViewerFreeze.cs and prints "VIEWERFREEZE:" lines. Raw lines go to <project>/viewer-freeze.log. --viewer-freeze-only=freeze|hooks|repeat runs one part.
// Checks are the freeze invariants and the measurements being present; audit results that are hazards (not invariants) are logged as HAZARD lines and do not fail the run.
public partial class PhysicsTest
{
    static readonly Regex VfFieldLine = new(@"^VIEWERFREEZE: cond=(?<c>[^|]+)\|f\|(?<n>[^|]+)\|(?<b>[^|]*)\|(?<a>[^|]*)\|(?<ch>[01])$");
    string _vfLogPath;

    static bool VfSimField(string n) =>
        n.StartsWith("car.") || n.StartsWith("clock.") || n.StartsWith("timer.") || n.StartsWith("rec.") || n.StartsWith("fuel.") || n.StartsWith("day.") || n.StartsWith("static.") || n.StartsWith("weather.")
        || n is "countdown" or "obstacles.hash" or "obstacles.moving" or "obstacles.count" or "phys.ticks" or "nodes.hashXor" or "nodes.node3d";

    void VfAppend(string header, TapeChild c)
    {
        using var w = File.AppendText(_vfLogPath);
        w.WriteLine($"==== {header} (exit {c.Exit}, {c.Seconds:F1} s{(c.TimedOut ? ", TIMED OUT" : "")})");
        foreach (var l in c.Lines) if (l.StartsWith("VIEWERFREEZE") || l.Contains("ERROR") || l.Contains("Exception") || l.Contains("WARNING")) w.WriteLine(l);
    }

    TapeChild VfRun(string name, string header, string[] args, Dictionary<string, string> env = null, int timeout = 200)
    {
        string dir = "/tmp/cccp-spike2-" + name;
        foreach (var d in new[] { dir, dir + ".xdg" }) try { if (Directory.Exists(d)) Directory.Delete(d, true); } catch { }
        var c = TapeGame(dir, args, timeout, env: env);
        VfAppend(header, c);
        foreach (var d in new[] { dir, dir + ".xdg" }) try { Directory.Delete(d, true); } catch { }
        try { File.Delete(dir + ".log"); } catch { }
        TapeCheck(!c.TimedOut && c.Exit == 0 && !c.Crashed && !c.Lines.Any(l => l.Contains("script FAILED")), $"{header}: the child ended cleanly (exit {c.Exit}, {c.Seconds:F0} s)");
        return c;
    }

    void ViewerFreezeTests()
    {
        GD.Print("Viewer freeze spike tests (real game, one headless child at a time)");
        if (!OperatingSystem.IsLinux()) { Skip("viewer freeze: the child runs go through /bin/sh (Linux only)"); return; }
        _vfLogPath = Path.Combine(TapeProjectDir, "viewer-freeze.log");
        string only = OS.GetCmdlineUserArgs().FirstOrDefault(x => x.StartsWith("--viewer-freeze-only="))?["--viewer-freeze-only=".Length..];
        File.WriteAllText(_vfLogPath, $"viewer-freeze spike 2, {DateTime.Now:O}, Godot {Engine.GetVersionInfo()["string"]}, project {TapeProjectDir}\n");
        if (only is null or "freeze") VfFreezeRuns();
        if (only is null or "hooks") VfHookRuns();
        if (only is null or "repeat") VfRepeatRuns();
        GD.Print(_failures == 0 ? "Viewer freeze spike tests: all passed" : $"Viewer freeze spike tests: {_failures} FAILED");
    }

    // ---- 1: the freeze table ----

    sealed record VfRow(string Before, string After, bool Changed);

    static Dictionary<string, Dictionary<string, VfRow>> VfParseFields(TapeChild c)
    {
        var d = new Dictionary<string, Dictionary<string, VfRow>>();
        foreach (var l in c.Lines)
        {
            var m = VfFieldLine.Match(l);
            if (!m.Success) continue;
            if (!d.TryGetValue(m.Groups["c"].Value, out var f)) d[m.Groups["c"].Value] = f = new();
            f[m.Groups["n"].Value] = new VfRow(m.Groups["b"].Value, m.Groups["a"].Value, m.Groups["ch"].Value == "1");
        }
        return d;
    }

    static string VfCondLine(TapeChild c, string cond, string kind) => c.Lines.FirstOrDefault(l => l.StartsWith($"VIEWERFREEZE: cond={cond}|{kind}|"));
    static int VfNum(string line, string key) { var m = Regex.Match(line ?? "", Regex.Escape(key) + @"=(\d+)"); return m.Success ? int.Parse(m.Groups[1].Value) : -1; }

    void VfFreezeRuns()
    {
        var runs = new (string name, string[] args)[]
        {
            ("ilyich", new[] { "--start", "--autodrive", "--track=ilyich" }),
            ("atomgrad", new[] { "--start", "--autodrive", "--track=atomgrad" }),
            ("atomgrad-night", new[] { "--start", "--autodrive", "--track=atomgrad", "--phase=night" }),
            ("siberia", new[] { "--start", "--autodrive", "--track=siberia" }),
            ("bonus", new[] { "--start", "--autodrive", "--track=bonus", "--viewer-freeze-warm=20" }),
        };
        foreach (var (name, args) in runs)
        {
            var c = VfRun("freeze-" + name, $"freeze {name}", args.Concat(new[] { "--viewer-freeze-child=freeze", "--viewer-freeze-frames=90" }).ToArray());
            var f = VfParseFields(c);
            TapeCheck(f.ContainsKey("A1-run") && f.ContainsKey("B-disabled+audiopause") && f.ContainsKey("C-state-paused") && f.ContainsKey("E-state-paused+disabled+audiopause"), $"freeze {name}: every condition was measured");
            if (!f.ContainsKey("B-disabled+audiopause")) continue;
            // positive control: the live scene really moves in the plain run (otherwise "unchanged" proves nothing)
            foreach (var key in new[] { "car.pos", "clock.lap", "clock.scene", "timer.lapTime", "rec.tapeSamples", "rec.ghostSamples", "day.skySeconds" })
                TapeCheck(f["A1-run"][key].Changed, $"freeze {name}: baseline (a), plain run: {key} changes ({f["A1-run"][key].Before} -> {f["A1-run"][key].After})");
            TapeCheck(VfNum(VfCondLine(c, "A1-run", "nodes"), "moved") > 0, $"freeze {name}: baseline (a): scene nodes move ({VfCondLine(c, "A1-run", "nodes")?.Split('|').LastOrDefault()})");
            TapeInfo($"freeze {name}: baseline (a) moving groups: " + string.Join(", ", c.Lines.Where(l => l.StartsWith("VIEWERFREEZE: cond=A1-run|movedgroup|")).Select(l => l.Split('|')[2] + "=" + l.Split('|')[3])));
            foreach (var cond in new[] { "B-disabled+audiopause", "E-state-paused+disabled+audiopause" })
            {
                var sim = f[cond].Where(kv => VfSimField(kv.Key)).ToList();
                var changed = sim.Where(kv => kv.Value.Changed).Select(kv => $"{kv.Key} {kv.Value.Before}->{kv.Value.After}").ToList();
                TapeCheck(sim.Count > 20 && changed.Count == 0, $"freeze {name}: {cond}: {sim.Count} simulation fields (car, timers, clock, recorder samples, fuel, day clock, sky, obstacles, node hash) unchanged over 90 frames" + (changed.Count > 0 ? " CHANGED: " + string.Join("; ", changed) : ""));
                var nodes = VfCondLine(c, cond, "nodes");
                TapeCheck(VfNum(nodes, "moved") == 0 && VfNum(nodes, "added") == 0 && VfNum(nodes, "removed") == 0, $"freeze {name}: {cond}: no Node3D moved, appeared or vanished ({nodes?.Split('|').LastOrDefault()})");
                var post = VfCondLine(c, cond, "post-apply-to-end");
                TapeCheck(post != null && post.Contains("changed fields []") && VfNum(post, "extraChanged") == 0 && VfNum(post, "nodesMoved") == 0, $"freeze {name}: {cond}: from the frame it was applied to its end nothing changed, audio players and particles included ({post?.Split('|').Skip(2).FirstOrDefault()} {post?.Split('|').Skip(3).FirstOrDefault()})");
                var canp = VfCondLine(c, cond, "canprocess");
                TapeCheck(VfNum(canp, "CanProcess()") == 0, $"freeze {name}: {cond}: no node under the scene can process (CanProcess() true count; a child set to Always would show here): {canp?.Split('|').LastOrDefault()}");
                var probe = c.Lines.Where(l => l.StartsWith($"VIEWERFREEZE: cond={cond}|probe|")).ToArray();
                TapeCheck(probe.Length == 2 && probe[0].Contains("particleLive emitting=True") && probe[0].Contains("particleControl emitting=False"), $"freeze {name}: {cond}: a one-shot CpuParticles3D in the live scene is still emitting, its control beside the scene has finished ({probe.FirstOrDefault()?.Split('|').Skip(2).Aggregate((x, y) => x + "; " + y)})");
                TapeCheck(probe.Length == 2 && probe[1].Contains("tweenLiveBound x=0.00 running=True") && probe[1].Contains("tweenControl x=100.00 running=False"), $"freeze {name}: {cond}: a Tween bound to a node of the live scene did not advance, its control finished ({probe.LastOrDefault()?.Split('|').Skip(2).Aggregate((x, y) => x + "; " + y)})");
                TapeInfo($"freeze {name}: {cond}: unbound SceneTree tween: {probe.LastOrDefault()?.Split('|').LastOrDefault()}");
            }
            // the audio recipe: does Disabled alone pause the players?
            var dPost = VfCondLine(c, "D-disabled-NO-audiopause", "post-apply-to-end");
            TapeInfo($"freeze {name}: D (Disabled, NO explicit audio pause): from apply to end {dPost?.Split('|').Skip(2).FirstOrDefault()} extraChanged={VfNum(dPost, "extraChanged")}; streamPaused count {f["D-disabled-NO-audiopause"]["audio.streamPaused"].Before} -> {f["D-disabled-NO-audiopause"]["audio.streamPaused"].After}, playing {f["D-disabled-NO-audiopause"]["audio.playing"].Before} -> {f["D-disabled-NO-audiopause"]["audio.playing"].After}");
            foreach (var cond in new[] { "B-disabled+audiopause", "D-disabled-NO-audiopause", "E-state-paused+disabled+audiopause" })
                TapeInfo($"freeze {name}: {VfCondLine(c, cond, "after-undo")?.Replace("VIEWERFREEZE: ", "")} / {VfCondLine(c, cond, "after-undo+1frame")?.Replace("VIEWERFREEZE: ", "")}");
            // State.Paused baseline (b): which sim fields stop and which keep going
            var cSim = f["C-state-paused"].Where(kv => VfSimField(kv.Key));
            TapeCheck(cSim.Where(kv => kv.Key.StartsWith("car.") || kv.Key.StartsWith("timer.") || kv.Key.StartsWith("rec.") || kv.Key.StartsWith("fuel.") || kv.Key == "clock.lap" || kv.Key == "phys.ticks").All(kv => !kv.Value.Changed), $"freeze {name}: State.Paused stops car, lap timer, recorders, fuel and the lap clock");
            foreach (var cond in new[] { "C-state-paused", "E-state-paused+disabled+audiopause" })
                TapeInfo($"freeze {name}: {cond} changed: {string.Join("; ", f[cond].Where(kv => kv.Value.Changed && kv.Key is not "scene.frames").Select(kv => $"{kv.Key} {kv.Value.Before}->{kv.Value.After}"))}; node groups moved [{string.Join(", ", c.Lines.Where(l => l.StartsWith($"VIEWERFREEZE: cond={cond}|movedgroup|")).Select(l => l.Split('|')[2] + "=" + l.Split('|')[3]))}]; {VfCondLine(c, cond, "obstacles-moved")?.Split('|').Skip(2).Aggregate((x, y) => x + " " + y)}");
            TapeInfo($"freeze {name}: tweens.processed in the plain run: {f["A1-run"]["tweens.processed"].Before}->{f["A1-run"]["tweens.processed"].After}; weather/wetness/glow in B: wet {f["B-disabled+audiopause"]["static.SurfacesWetness"].Before}->{f["B-disabled+audiopause"]["static.SurfacesWetness"].After}, glowLevel {f["B-disabled+audiopause"]["static.NightGlowLevel"].Before}->{f["B-disabled+audiopause"]["static.NightGlowLevel"].After}");
        }
    }

    // ---- 2: hooks, shared caches, the nested world's exit ----

    void Hazard(string text, bool present) { using var w = File.AppendText(_vfLogPath); w.WriteLine($"HAZARD {(present ? "CONFIRMED" : "not observed")}: {text}"); TapeInfo($"HAZARD {(present ? "CONFIRMED" : "not observed")}: {text}"); }

    static string VfKv(string line, string key) { var m = Regex.Match(line ?? "", @"\|" + Regex.Escape(key) + @"=([^|]*)"); return m.Success ? m.Groups[1].Value : null; }

    void VfHookRuns()
    {
        var runs = new (string name, string live, string nested, string[] extra)[]
        {
            ("ilyich-ilyich", "ilyich", "ilyich", Array.Empty<string>()),
            ("ilyich-night-ilyich", "ilyich", "ilyich", new[] { "--phase=night" }),
            ("atomgrad-ilyich", "atomgrad", "ilyich", Array.Empty<string>()),
            ("atomgrad-atomgrad", "atomgrad", "atomgrad", Array.Empty<string>()),
            ("ilyich-atomgrad", "ilyich", "atomgrad", Array.Empty<string>()),
        };
        foreach (var (name, live, nested, extra) in runs)
        {
            var c = VfRun("hooks-" + name, $"hooks live {live} nested {nested}{(extra.Length > 0 ? " " + extra[0] : "")}", new[] { "--start", "--autodrive", "--track=" + live, "--viewer-freeze-child=hooks", "--viewer-freeze-nested=" + nested }.Concat(extra).ToArray());
            string St(string tagStart) => c.Lines.FirstOrDefault(l => l.StartsWith("VIEWERFREEZE: hooks|" + tagStart));
            var before = St("2-paused-before-open"); var opened = St("3-just-after"); var running = St("4-nested"); var queued = St("5-after-QueueFree"); var q3 = St("6-QueueFree+3"); var restored = St("7-after"); var r1 = St("8-live"); var r6 = St("9-live"); var r126 = St("10-live"); var gc = St("11-after-GC");
            TapeCheck(before != null && opened != null && running != null && q3 != null && restored != null && r126 != null, $"hooks {name}: all measurement points were reached");
            if (before == null || opened == null || r126 == null) continue;
            string live0 = Regex.Match(before, @"trackId=\w+").Value;
            TapeInfo($"hooks {name}: DayNight NodeAdded hook: the LIVE DayNight's _unlit {VfKv(before, "liveDayNight._unlit")} -> {VfKv(opened, "liveDayNight._unlit")}, _sceneryLights {VfKv(before, "liveDayNight._sceneryLights")} -> {VfKv(opened, "liveDayNight._sceneryLights")}; after the nested world is gone and the live one resumed: {VfKv(r126, "liveDayNight._unlit")} / {VfKv(r126, "liveDayNight._sceneryLights")}");
            Hazard($"hooks {name}: the live DayNight collected the nested world's unlit materials and scenery lights ({VfKv(before, "liveDayNight._unlit")}->{VfKv(opened, "liveDayNight._unlit")}, {VfKv(before, "liveDayNight._sceneryLights")}->{VfKv(opened, "liveDayNight._sceneryLights")}) and keeps them after the nested world was freed ({VfKv(r126, "liveDayNight._unlit")}, {VfKv(r126, "liveDayNight._sceneryLights")})", int.Parse(VfKv(opened, "liveDayNight._unlit")) > int.Parse(VfKv(before, "liveDayNight._unlit")));
            var sharedLine = c.Lines.FirstOrDefault(l => l.StartsWith("VIEWERFREEZE: hooks|3|nested DayNight._unlit="));
            Hazard($"hooks {name}: unshaded materials shared between the worlds are recorded with another 'day' colour by the nested DayNight ({sharedLine?.Replace("VIEWERFREEZE: hooks|3|", "")})", sharedLine != null && Regex.IsMatch(sharedLine, @"of which [1-9]"));
            foreach (var l in c.Lines.Where(l => l.StartsWith("VIEWERFREEZE: hooks|3|"))) TapeInfo($"hooks {name}: {l.Replace("VIEWERFREEZE: hooks|3|", "")}");
            Hazard($"hooks {name}: a nested DachaPatrol bound to the LIVE boat", c.Lines.Any(l => l.Contains("DachaPatrol in NESTED is bound to a boat in LIVE")));
            // the live world while the nested one ran
            var dig = c.Lines.FirstOrDefault(l => l.Contains("live-digest while the nested world ran"));
            var digItems = dig == null ? null : Regex.Match(dig, @"changed fields \[(.*?)\] nodesMoved").Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries);
            TapeCheck(digItems != null && digItems.All(x => x.StartsWith("audio.") || x.StartsWith("scene.frames") || x.StartsWith("static.")) && dig.Contains("nodesMoved=0 nodesAdded=0 nodesRemoved=0"), $"hooks {name}: the live scene's own state (car, timers, recorders, fuel, day clock, sky, obstacles, nodes) does not change while the nested world runs for 60 frames; only the audio players the engine paused, the global frame counter and the shared statics move ({dig?.Replace("VIEWERFREEZE: hooks|", "")})");
            Hazard($"hooks {name}: shared statics written by the nested world while it runs, visible in the live digest: [{string.Join(",", (digItems ?? Array.Empty<string>()).Where(x => x.StartsWith("static.")))}]", digItems != null && digItems.Any(x => x.StartsWith("static.")));
            TapeCheck(c.Lines.Any(l => l.Contains("nested scene: state=Racing") || l.Contains("nested scene: state=")), $"hooks {name}: the nested scene really ran (clock advanced)");
            // the exit
            TapeCheck(c.Lines.Any(l => l.Contains("6-QueueFree+1-frames (viewport valid=False, nested scene valid=False)")), $"hooks {name}: after QueueFree the nested viewport and scene are gone one frame later (deferred ExitTree has run)");
            TapeInfo($"hooks {name}: exit side effects, the live game as the statics show it:");
            foreach (var key in new[] { "trackId", "wetness", "concreteGrip@30", "NightGlow.Level", "live day.glow", "NightGlow.Glows", "glowsOfLiveWorld", "radarsOfLiveWorld", "RadarHead.All", "SeeThrough.Off", "FadeUnder.Off", "Props.BatchCache", "Props.Materials", "SfxMuted", "GulagTruck._onRoad", "Missile._serial", "treeNodes", "objects", "objResources", "orphans", "nodeAddedConnections" })
                TapeInfo($"   {key,-18} before {VfKv(before, key)} | nested open {VfKv(opened, key)} | nested ran {VfKv(running, key)} | QueueFree same frame {VfKv(queued, key)} | +3 frames {VfKv(q3, key)} | after restore {VfKv(restored, key)} | live +1 {VfKv(r1, key)} | live +126 {VfKv(r126, key)}" + (gc != null ? $" | after GC {VfKv(gc, key)}" : ""));
            Hazard($"hooks {name}: NightGlow.Level differs from the live glow after the restore and 126 live frames (level {VfKv(r126, "NightGlow.Level")} vs live glow {VfKv(r126, "live day.glow")})", Math.Abs(float.Parse(VfKv(r126, "NightGlow.Level"), CultureInfo.InvariantCulture) - float.Parse(VfKv(r126, "live day.glow"), CultureInfo.InvariantCulture)) > 0.01f);
            Hazard($"hooks {name}: live Surfaces.Wetness differs from before the viewer directly after the restore ({VfKv(before, "wetness")} -> {VfKv(restored, "wetness")}; the live weather moves it on by itself later: +126 frames {VfKv(r126, "wetness")}); during the nested run it was {VfKv(running, "wetness")} (grip@30 {VfKv(before, "concreteGrip@30")} -> {VfKv(running, "concreteGrip@30")}), after the nested exit and before the restore {VfKv(q3, "wetness")} (grip@30 {VfKv(q3, "concreteGrip@30")})", VfKv(before, "wetness") != VfKv(restored, "wetness"));
            Hazard($"hooks {name}: NightGlow lists no longer hold the live world's entries after the restore", VfKv(restored, "glowsOfLiveWorld") != VfKv(before, "glowsOfLiveWorld"));
            Hazard($"hooks {name}: while the nested world exists NightGlow.Glows holds its materials, not the live ones ({VfKv(running, "glowsOfLiveWorld")}, radars {VfKv(running, "radarsOfLiveWorld")})", VfKv(running, "glowsOfLiveWorld") != VfKv(before, "glowsOfLiveWorld"));
            TapeCheck(c.Lines.Any(l => l.Contains("live DayNight.SetGlow walked the grown collections")), $"hooks {name}: the live DayNight survives its grown collections (SetGlow forced over the lists, IsInstanceValid-guarded)");
            var moved = c.Lines.FirstOrDefault(l => l.Contains("live car moved on after resume"));
            TapeInfo($"hooks {name}: {moved?.Replace("VIEWERFREEZE: hooks|", "")}");
        }
    }

    // ---- 3: does the plain run repeat? ----

    void VfRepeatRuns()
    {
        var runs = new TapeChild[3];
        var traces = new string[3];
        for (int i = 0; i < 3; i++)
        {
            string trace = $"/tmp/cccp-spike2-repeat-trace-{i + 1}.txt"; traces[i] = trace;
            try { File.Delete(trace); } catch { }
            var args = new List<string> { "--start", "--autodrive", "--track=ilyich", "--viewer-freeze-child=repeat" };
            if (i == 2) args.AddRange(new[] { "--viewer-freeze-ab-at=25", "--viewer-freeze-frames=300" });      // run 3 = the A/B run: the live scene frozen (Disabled + audio pause) for 300 driver frames mid-lap
            runs[i] = VfRun(i < 2 ? "repeat-" + (i + 1) : "repeat-ab", i < 2 ? $"repeat run {i + 1} (ilyich, seeds 1, WORLDTRACE)" : "A/B run (ilyich, seeds 1, WORLDTRACE): frozen 300 frames at lap clock 25 s", args.ToArray(), new Dictionary<string, string> { ["WORLDTRACE"] = trace, ["WORLDTRACE_FRAMES"] = "30000" }, 300);
        }
        Dictionary<(int frame, string key), string> Parse(TapeChild c)
        {
            var d = new Dictionary<(int, string), string>();
            foreach (var l in c.Lines)
            {
                if (!l.StartsWith("VIEWERFREEZE: rep|")) continue;
                var p = l.Split('|');
                if (p[1] == "final") d[(-1, p[2])] = string.Join("|", p.Skip(3));
                else if (p.Length >= 4 && int.TryParse(p[1], out var fr)) d[(fr, p[2])] = p[3];
            }
            return d;
        }
        var a = Parse(runs[0]); var b = Parse(runs[1]);
        TapeCheck(a.Count > 1000 && a.Count == b.Count, $"repeat: both runs logged the same {a.Count} samples ({b.Count})");
        var diffKeys = new SortedDictionary<string, (int first, int count)>(StringComparer.Ordinal);
        foreach (var (k, v) in a) if (b.TryGetValue(k, out var w) && w != v)
        {
            string key = k.key;
            diffKeys[key] = diffKeys.TryGetValue(key, out var e) ? (Math.Min(e.first == -1 ? int.MaxValue : e.first, k.frame == -1 ? int.MaxValue : k.frame), e.count + 1) : (k.frame, 1);
        }
        bool Sim(string key) => VfSimField(key) && key is not ("rec.tapeEntries" or "nodes.hashXor" or "nodes.node3d") || key is "state" or "tweens.elapsed" or "tweens.processed";      // (scene.frames is Engine.GetProcessFrames(): global, the frozen frames count too)
        var simDiff = diffKeys.Where(kv => Sim(kv.Key)).ToList();
        TapeCheck(simDiff.Count == 0, $"repeat: the simulation digest (car, velocities, timers, clocks, lap/ghost sample counts, fuel, day clock, sky, wetness, obstacles) is identical in two plain runs, every 30 frames over {a.Keys.Where(k => k.frame >= 0).Max(k => k.frame)} frames" + (simDiff.Count > 0 ? " DIFFERS: " + string.Join(", ", simDiff.Select(kv => kv.Key + "@" + kv.Value.first)) : ""));
        TapeInfo("repeat: NONDETERMINISTIC fields between two identical plain runs (first sampled frame, number of differing samples): " + string.Join("; ", diffKeys.Select(kv => $"{kv.Key} (first {kv.Value.first}, {kv.Value.count}x)")));
        TapeCheck(a.GetValueOrDefault((-1, "ghostSha256")) is { } g1 && g1 == b.GetValueOrDefault((-1, "ghostSha256")) && a[(-1, "ghostBytes")] == b[(-1, "ghostBytes")], $"repeat: the completed lap's ghost bytes (lap archive) are byte-identical in the two runs ({a.GetValueOrDefault((-1, "ghostBytes"))} bytes, sha {a.GetValueOrDefault((-1, "ghostSha256"))?[..16]})");
        string LapOf(Dictionary<(int frame, string key), string> d) => d.GetValueOrDefault((-1, "lapTime"))?.Split('|')[0];
        TapeCheck(LapOf(a) != null && LapOf(a) == LapOf(b), $"repeat: the lap time is identical ({LapOf(a)} and {LapOf(b)} s; the frames at which the child noticed the archived lap and quit differ, as the archive worker finishes in wall time: {a.GetValueOrDefault((-1, "lapTime"))?.Split('|').Last()} vs {b.GetValueOrDefault((-1, "lapTime"))?.Split('|').Last()})");
        TapeInfo($"repeat: world tape bytes of the lap: run 1 {a.GetValueOrDefault((-1, "tapeBytes"))} B sha {a.GetValueOrDefault((-1, "tapeSha256"))?[..16]}; run 2 {b.GetValueOrDefault((-1, "tapeBytes"))} B sha {b.GetValueOrDefault((-1, "tapeSha256"))?[..16]} (identical: {a.GetValueOrDefault((-1, "tapeSha256")) == b.GetValueOrDefault((-1, "tapeSha256"))})");
        // WORLDTRACE: the refactoring check's per-step hash of the car and every collision circle
        // (the child quits when the lap archive's worker has finished, a wall-clock moment: the traces have equal steps up to the shorter one, and the lap itself ends long before)
        (bool same, int lenX, int lenY, string where) TraceCompare(string fx, string fy)
        {
            if (!File.Exists(fx) || !File.Exists(fy)) return (false, 0, 0, "missing");
            var x = File.ReadAllLines(fx); var y = File.ReadAllLines(fy);
            for (int i = 0; i < Math.Min(x.Length, y.Length); i++) if (x[i] != y[i]) return (false, x.Length, y.Length, $"first differing step {i}: {x[i]} vs {y[i]}");
            return (true, x.Length, y.Length, x.Length == y.Length ? "identical" : "common prefix identical");
        }
        var tc = TraceCompare(traces[0], traces[1]);
        TapeCheck(tc.same && Math.Min(tc.lenX, tc.lenY) > 8000, $"repeat: WORLDTRACE (car + every collision circle + jets, each physics step) is identical in the two runs over the common {Math.Min(tc.lenX, tc.lenY)} steps ({tc.where}; lengths {tc.lenX} and {tc.lenY}, the runs quit at different wall-clock moments after the lap)");
        // which actor groups moved when the A/B freeze hit (lap clock 25 s, driver frame 1500) and over the whole lap, in the plain run: "not observed moving" is not "frozen"
        {
            var groups = a.Keys.Where(k => k.key.StartsWith("grp.")).Select(k => k.key).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
            var movedAt = groups.Where(g => a.TryGetValue((1500, g), out var x) && a.TryGetValue((1530, g), out var y) && x != y).Select(g => g[4..]).ToList();
            var movedEver = groups.Where(g => a.Where(kv => kv.Key.key == g && kv.Key.frame >= 0).Select(kv => kv.Value).Distinct().Count() > 1).Select(g => g[4..]).ToList();
            TapeInfo($"coverage (ilyich plain lap): groups that moved at the A/B freeze point (frames 1500-1530): [{string.Join(", ", movedAt)}]");
            TapeInfo($"coverage (ilyich plain lap): groups that moved at some sample during the lap: [{string.Join(", ", movedEver)}]");
            TapeInfo($"coverage (ilyich plain lap): groups that never changed in any sample: [{string.Join(", ", groups.Select(g => g[4..]).Except(movedEver))}]");
        }
        // the A/B run: is the freeze transparent to the rest of the lap?
        var ab = Parse(runs[2]);
        var abFields = VfParseFields(runs[2]);
        TapeCheck(abFields.ContainsKey("AB-freeze") && abFields["AB-freeze"].Where(kv => VfSimField(kv.Key)).All(kv => !kv.Value.Changed), $"A/B: during the 300 frozen frames every simulation field was unchanged ({runs[2].Lines.FirstOrDefault(l => l.Contains("rep|ab|frozeAtLapClock"))?.Replace("VIEWERFREEZE: ", "")})");
        var abDiff = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var (k, v) in a) if (ab.TryGetValue(k, out var w) && w != v && Sim(k.key)) abDiff[k.key] = k.frame;
        TapeCheck(abDiff.Count == 0 && ab.Count == a.Count, $"A/B: the simulation digest of the A/B run equals the plain run at every 30-frame sample (samples {ab.Count} vs {a.Count})" + (abDiff.Count > 0 ? " DIFFERS: " + string.Join(", ", abDiff.Select(kv => kv.Key + "@" + kv.Value)) : ""));
        TapeCheck(a.GetValueOrDefault((-1, "ghostSha256")) == ab.GetValueOrDefault((-1, "ghostSha256")) && LapOf(a) == LapOf(ab), $"A/B: the completed lap's ghost bytes and lap time equal the plain run's (ghost {ab.GetValueOrDefault((-1, "ghostSha256"))?[..16]}, lap {LapOf(ab)} s)");
        var tab = TraceCompare(traces[0], traces[2]);
        TapeCheck(tab.same && Math.Min(tab.lenX, tab.lenY) > 8000, $"A/B: WORLDTRACE of the A/B run (physics steps skipped while frozen simply do not exist) is identical to the plain run's over the common {Math.Min(tab.lenX, tab.lenY)} steps ({tab.where}; lengths {tab.lenX} and {tab.lenY})");
        TapeInfo($"A/B: world tape bytes of the lap: A/B {ab.GetValueOrDefault((-1, "tapeBytes"))} B sha {ab.GetValueOrDefault((-1, "tapeSha256"))?[..16]} (plain runs: {a.GetValueOrDefault((-1, "tapeBytes"))} and {b.GetValueOrDefault((-1, "tapeBytes"))} B)");
        var abDiffKeys = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var (k, v) in a) if (ab.TryGetValue(k, out var w) && w != v && !Sim(k.key)) abDiffKeys[k.key] = abDiffKeys.GetValueOrDefault(k.key) + 1;
        TapeInfo("A/B: non-simulation fields that differ from plain run 1 (compare with the plain-vs-plain list above): " + string.Join("; ", abDiffKeys.Select(kv => $"{kv.Key} {kv.Value}x")));
        foreach (var t in traces) try { File.Delete(t); } catch { }
    }
}
