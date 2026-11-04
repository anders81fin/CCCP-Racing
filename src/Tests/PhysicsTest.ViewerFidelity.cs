using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Godot;

namespace CCCPRacing.Tests;

// --viewer-fidelity-tests: judges the VIEWERFIDELITY lines of the child games (see RaceScene.ViewerFidelityTest.cs). One headless child at a time (TapeGame), data under /tmp/cccp-vfid-*.
//   --viewer-fidelity-only=playback|ab|fixtures   --viewer-fidelity-track=ilyich|siberia|atomgrad|bonus   --viewer-fidelity-fixture=boost|damage|missile|mission
// playback (A+B): one lap per track, the viewer opened on it, poses against the original, time control. ab (C): seed lap, then plain x2 / viewer / freeze runs from copies of the seed's data
// dir; fixtures (D): the same with a deterministic state active when the viewer (or the freeze) starts. Logs of every child go to build/player-replay/phase2/logs/.
public partial class PhysicsTest
{
    static readonly string[] VfiTracks = { "ilyich", "siberia", "atomgrad", "bonus" };
    int _vfiOk, _vfiBad;
    string _vfiLogDir;

    void VfiCheck(bool ok, string what) { if (ok) _vfiOk++; else _vfiBad++; TapeCheck(ok, what); }

    string VfiArg(string name) => OS.GetCmdlineUserArgs().FirstOrDefault(x => x.StartsWith(name + "="))?[(name.Length + 1)..];

    void ViewerFidelityTests()
    {
        GD.Print("Viewer fidelity tests (real game, one headless child at a time)");
        if (!OperatingSystem.IsLinux()) { Skip("viewer fidelity: the child runs go through /bin/sh (Linux only)"); return; }
        _vfiLogDir = Path.Combine(TapeProjectDir, "build/player-replay/phase2/logs");
        Directory.CreateDirectory(_vfiLogDir);
        string only = VfiArg("--viewer-fidelity-only"), trackArg = VfiArg("--viewer-fidelity-track"), fixArg = VfiArg("--viewer-fidelity-fixture");
        var tracks = trackArg != null ? new[] { trackArg } : VfiTracks;
        if (only is null or "playback") foreach (var t in tracks) VfiPlayback(t);
        if (only is null or "ab") foreach (var t in tracks) VfiAbSuite($"ab-{t}", t, "none", VfiTrackExtra(t), 25f);
        if (only is null or "fixtures")
        {
            var fx = new (string name, string track, string[] extra, float at)[]
            {
                ("boost", "ilyich", new[] { "--grade=rocket" }, 25f),
                ("damage", "ilyich", Array.Empty<string>(), 25f),
                ("missile", "siberia", Array.Empty<string>(), 25f),
                ("shell", "bonus", new[] { "--training" }, 47.5f),
                ("mission", "ilyich", new[] { "--norm-step=9", "--laps-left=1" }, 25f),
            };
            foreach (var f in fx) if (fixArg == null || fixArg == f.name) VfiAbSuite($"fx-{f.name}", f.track, f.name, f.extra, f.at);
        }
        GD.Print($"Viewer fidelity: {_vfiOk} checks passed, {_vfiBad} FAILED (the known shutdown line 'ERROR: 1 resources still in use at exit' of each child is counted separately below)");
        GD.Print($"Viewer fidelity: child shutdown lines 'resources still in use at exit': {_vfiShutdown}");
        GD.Print(_failures == 0 ? "Viewer fidelity tests: all passed" : $"Viewer fidelity tests: {_failures} FAILED");
    }

    int _vfiShutdown;

    /// <summary>The bonus track (a tank battle at about 48 s of the autopilot's lap) destroys the car in a real rally; a training run shrugs the hit off, so the lap can be driven and archived.</summary>
    static string[] VfiTrackExtra(string track) => track == "bonus" ? new[] { "--training" } : Array.Empty<string>();

    TapeChild VfiRun(string label, string dir, string[] args, Dictionary<string, string> env = null, int timeout = 900)
    {
        foreach (var d in new[] { dir, dir + ".xdg" }) try { if (Directory.Exists(d)) Directory.Delete(d, true); } catch { }
        return VfiRunKeep(label, dir, args, env, timeout);
    }

    TapeChild VfiRunKeep(string label, string dir, string[] args, Dictionary<string, string> env, int timeout)
    {
        var c = TapeGame(dir, args, timeout, env: env);
        File.WriteAllLines(Path.Combine(_vfiLogDir, label + ".log"), c.Lines);
        _vfiShutdown += c.Lines.Count(l => l.Contains("resources still in use at exit"));
        var errors = c.Lines.Where(l => (l.Contains("ERROR") && !l.Contains("resources still in use at exit")) || l.Contains("Unhandled exception") || l.Contains("script FAILED")).ToList();
        VfiCheck(!c.TimedOut && c.Exit == 0 && !c.Crashed && errors.Count == 0, $"{label}: the child ended cleanly (exit {c.Exit}, {c.Seconds:F0} s, errors other than the known shutdown line: {errors.Count}{(errors.Count > 0 ? " first: " + errors[0] : "")})");
        return c;
    }

    // ------------------------------------------------------------------------------------------------ playback (A, B)

    void VfiPlayback(string track)
    {
        string dir = $"/tmp/cccp-vfid-playback-{track}";
        var c = VfiRun($"playback-{track}", dir, new[] { "--start", "--autodrive", "--track=" + track, "--viewer-fidelity=playback" }.Concat(VfiTrackExtra(track)).ToArray());
        int pass = 0, fail = 0;
        foreach (var l in c.Lines.Where(l => l.StartsWith("VIEWERFIDELITY: ")))
        {
            var p = l["VIEWERFIDELITY: ".Length..].Split('|');
            if (p[0] == "PASS") { pass++; VfiCheck(true, $"playback {track}: {p[1]}"); }
            else if (p[0] == "FAIL") { fail++; VfiCheck(false, $"playback {track}: {p[1]} [{(p.Length > 2 ? p[2] : "")}]"); }
            else if (p[0] is "A" or "B" or "playback") TapeInfo($"playback {track}: {string.Join(" | ", p)}");
        }
        string summary = c.Lines.FirstOrDefault(l => l.StartsWith("VIEWERFIDELITY: SUMMARY"));
        VfiCheck(summary != null && pass >= 30, $"playback {track}: the script ran to its end ({summary?["VIEWERFIDELITY: ".Length..]}, {pass} passed, {fail} failed)");
    }

    // ------------------------------------------------------------------------------------------------ A/B continuation (C) and fixtures (D)

    sealed class VfiResult
    {
        public TapeChild Child; public string Dir;
        public Dictionary<string, List<(long tick, string val)>> Digest = new();
        public string[] Trace = Array.Empty<string>(), WorldTrace = Array.Empty<string>();
        public Dictionary<string, string> Final = new();
        public long QuitTick => Final.TryGetValue("quitTick", out var q) ? long.Parse(q) : -1;
    }

    static void VfiCopyDir(string from, string to)
    {
        if (Directory.Exists(to)) Directory.Delete(to, true);
        Directory.CreateDirectory(to);
        foreach (var d in Directory.GetDirectories(from, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, d)));
        foreach (var f in Directory.GetFiles(from, "*", SearchOption.AllDirectories)) File.Copy(f, Path.Combine(to, Path.GetRelativePath(from, f)), true);
    }

    VfiResult VfiAbRun(string label, string seedDir, string name, string[] baseArgs, string variant, string fixture, float at, int frames)
    {
        string dir = $"/tmp/cccp-vfid-{name}";
        foreach (var d in new[] { dir, dir + ".xdg" }) try { if (Directory.Exists(d)) Directory.Delete(d, true); } catch { }
        VfiCopyDir(seedDir, dir);
        if (Directory.Exists(seedDir + ".xdg")) VfiCopyDir(seedDir + ".xdg", dir + ".xdg");
        string digest = dir + ".digest", trace = dir + ".trace", wt = dir + ".wt";
        foreach (var f in new[] { digest, trace, wt }) try { File.Delete(f); } catch { }
        var args = baseArgs.Concat(new[] { "--viewer-fidelity=ab", "--vfi-variant=" + variant, "--vfi-fixture=" + fixture, "--vfi-at=" + at.ToString(CultureInfo.InvariantCulture), "--vfi-frames=" + frames, "--vfi-out=" + digest, "--vfi-trace=" + trace }).ToArray();
        var c = VfiRunKeep(label, dir, args, new Dictionary<string, string> { ["WORLDTRACE"] = wt, ["WORLDTRACE_FRAMES"] = "100000000" }, 900);
        var r = new VfiResult { Child = c, Dir = dir };
        if (File.Exists(digest))
            foreach (var l in File.ReadLines(digest))
            {
                var p = l.Split('\t'); if (p.Length < 3) continue;
                if (!r.Digest.TryGetValue(p[1], out var list)) r.Digest[p[1]] = list = new();
                list.Add((long.Parse(p[0]), p[2].Replace(dir, "<dir>")));
            }
        if (File.Exists(trace)) r.Trace = File.ReadAllLines(trace);
        if (File.Exists(wt)) r.WorldTrace = File.ReadAllLines(wt);
        foreach (var l in c.Lines) { var m = Regex.Match(l, @"^VIEWERFIDELITY: ab\|final\|(\w+)\|(.*)$"); if (m.Success) r.Final[m.Groups[1].Value] = m.Groups[2].Value; }
        foreach (var f in new[] { digest, trace, wt }) try { File.Delete(f); } catch { }
        try { Directory.Delete(dir, true); } catch { }
        try { Directory.Delete(dir + ".xdg", true); } catch { }
        try { File.Delete(dir + ".log"); } catch { }
        return r;
    }

    /// <summary>Keys whose value differs between two digests at some sampled tick up to maxTick (the delta streams are replayed as step functions).</summary>
    static SortedDictionary<string, (long first, string a, string b, int count)> VfiDiff(VfiResult x, VfiResult y, long maxTick)
    {
        var res = new SortedDictionary<string, (long, string, string, int)>(StringComparer.Ordinal);
        foreach (var key in x.Digest.Keys.Union(y.Digest.Keys))
        {
            var la = x.Digest.GetValueOrDefault(key) ?? new(); var lb = y.Digest.GetValueOrDefault(key) ?? new();
            var ticks = la.Select(e => e.tick).Concat(lb.Select(e => e.tick)).Where(t => t <= maxTick).Distinct().OrderBy(t => t).ToList();
            int ia = -1, ib = -1; long first = -1; string fa = null, fb = null; int count = 0;
            foreach (var t in ticks)
            {
                while (ia + 1 < la.Count && la[ia + 1].tick <= t) ia++;
                while (ib + 1 < lb.Count && lb[ib + 1].tick <= t) ib++;
                string va = ia >= 0 ? la[ia].val : "<absent>", vb = ib >= 0 ? lb[ib].val : "<absent>";
                if (va != vb) { if (first < 0) { first = t; fa = va; fb = vb; } count++; }
            }
            if (first >= 0) res[key] = (first, fa, fb, count);
        }
        return res;
    }

    static string VfiTraceCompare(string[] a, string[] b, out bool same)
    {
        int n = Math.Min(a.Length, b.Length);
        for (int i = 0; i < n; i++) if (a[i] != b[i]) { same = false; return $"first differing step {i} (lengths {a.Length} and {b.Length}; first lines {a.FirstOrDefault()?[..Math.Min(40, a.FirstOrDefault().Length)]} vs {b.FirstOrDefault()?[..Math.Min(40, b.FirstOrDefault().Length)]})"; }
        same = n > 0; return $"identical over the common {n} steps (lengths {a.Length} and {b.Length})";
    }

    // The nondeterministic fields the spike measured between two plain runs (report.md section 2): wall-clock babushka animation (Time.GetTicksMsec), unseeded VillageLife props,
    // audio counters and one-shot node counts. A field outside these patterns that differs between two plain runs is a FAILED baseline check, never an allowed difference.
    // Found by the first plain-vs-plain run and listed here on purpose: grp.Babushkas (Time.GetTicksMsec), grp.@Camera3D@N (an auto-named node), _fluffNext/_insectNext (the lens effects' unseeded RNG, LensFx.cs:47),
    // _rumble.* (gamepad rumble on wall time), _tapeRec.* (the tape recorder's buffers), _lapArchiveUsed.* (the archive worker's wall-clock progress).
    static readonly Regex VfiKnownNondeterministic = new(@"^(cap\.audio\.|cap\.nodes\.|cap\.tweens\.|grp\.Props$|grp\.Babushkas$|grp\.@|grp\.BalalaikaSorties$|(cap\.)?weather\.|cap\.rec\.tapeEntries$|scene\._(fluffNext|insectNext|insectCool|rumble\.|tapeRec\.|lapArchiveUsed\.))");

    void VfiAbSuite(string label, string track, string fixture, string[] extra, float at)
    {
        GD.Print($"== A/B suite {label}: track {track}, fixture {fixture}");
        string root = $"/tmp/cccp-vfid-{label}";
        string seed = root + "-seed";
        var baseArgs = new[] { "--start", "--autodrive", "--track=" + track, "--lap-archive-retain-all" }.Concat(extra).ToArray();
        var fxArgs = new[] { "--viewer-fidelity=ab", "--vfi-variant=plain", "--vfi-fixture=" + fixture, "--vfi-at=" + at.ToString(CultureInfo.InvariantCulture), "--vfi-tail=30" };
        var s = VfiRun($"{label}-seed", seed, baseArgs.Concat(fxArgs).ToArray());
        if (!s.Lines.Any(l => l.Contains("ab|final|ghostSha256"))) { VfiCheck(false, $"{label}: the seed run archived a lap"); return; }
        var p1 = VfiAbRun($"{label}-plain1", seed, label + "-plain1", baseArgs, "plain", fixture, at, 0);
        var p2 = VfiAbRun($"{label}-plain2", seed, label + "-plain2", baseArgs, "plain", fixture, at, 0);
        var va = VfiAbRun($"{label}-viewer", seed, label + "-viewer", baseArgs, "viewer", fixture, at, 0);
        string fl = va.Child.Lines.FirstOrDefault(l => l.Contains("ab|viewerFrames|"));
        int frames = fl == null ? 0 : int.Parse(fl.Split('|').Last());
        VfiCheck(frames > 0, $"{label}: the viewer run reports its frame count ({frames} driver frames from OpenReplayViewer to Closed)");
        var vb = VfiAbRun($"{label}-freeze", seed, label + "-freeze", baseArgs, "freeze", fixture, at, frames);
        foreach (var r in new[] { p1, p2, va, vb }) { }

        // viewer-run checks of its own (fixtures, the controls it drove)
        foreach (var l in va.Child.Lines.Where(l => l.StartsWith("VIEWERFIDELITY: ")))
        {
            var p = l["VIEWERFIDELITY: ".Length..].Split('|');
            if (p[0] == "PASS") VfiCheck(true, $"{label} viewer: {p[1]}");
            else if (p[0] == "FAIL") VfiCheck(false, $"{label} viewer: {p[1]} [{(p.Length > 2 ? p[2] : "")}]");
            else if (p[0] is "fixture" or "NOTCOVERED" or "ab") TapeInfo($"{label}: {string.Join(" | ", p)}");
        }

        // ---- baseline: two plain runs
        long common = new[] { p1.QuitTick, p2.QuitTick }.Min();
        VfiCheck(p1.QuitTick > 0 && p2.QuitTick > 0 && p1.Digest.Count > 100, $"{label}: both plain runs wrote a digest ({p1.Digest.Count} fields; quit at ticks {p1.QuitTick} and {p2.QuitTick})");
        var nd = VfiDiff(p1, p2, common);
        TapeInfo($"{label}: NONDETERMINISTIC fields between the two plain runs ({nd.Count}, first tick, differing samples): " + string.Join("; ", nd.Select(kv => $"{kv.Key} (tick {kv.Value.first}, {kv.Value.count}x)")));
        var unknown = nd.Keys.Where(k => !VfiKnownNondeterministic.IsMatch(k)).ToList();
        VfiCheck(unknown.Count == 0, $"{label}: baseline repeats: every field of the digest outside the known nondeterministic set (audio counters, node counts, tween clocks, VillageLife props, tape entries) is identical in two plain runs ({p1.Digest.Count} fields)" + (unknown.Count > 0 ? " UNEXPECTED: " + string.Join(", ", unknown.Select(k => $"{k} [{nd[k].a} vs {nd[k].b} @ {nd[k].first}]")) : ""));
        var t12 = VfiTraceCompare(p1.Trace, p2.Trace, out bool sameT12);
        VfiCheck(sameT12, $"{label}: baseline: the per-tick trace (car, every collision circle, jets) of two plain runs: {t12}");
        var w12 = VfiTraceCompare(p1.WorldTrace, p2.WorldTrace, out bool sameW12);
        VfiCheck(sameW12 && p1.WorldTrace.Length > 1000, $"{label}: baseline: WORLDTRACE of two plain runs: {w12}");
        VfiCheck(p1.Final.GetValueOrDefault("ghostSha256") != null && p1.Final.GetValueOrDefault("ghostSha256") == p2.Final.GetValueOrDefault("ghostSha256") && p1.Final.GetValueOrDefault("lapTime") == p2.Final.GetValueOrDefault("lapTime"), $"{label}: baseline: the new lap's archived ghost bytes and lap time are identical (sha {p1.Final.GetValueOrDefault("ghostSha256")?[..16]}, {p1.Final.GetValueOrDefault("lapTime")} s)");
        TapeInfo($"{label}: tape bytes of the new lap: plain1 {p1.Final.GetValueOrDefault("tapeBytes")} B sha {p1.Final.GetValueOrDefault("tapeSha256")?[..16]}; plain2 {p2.Final.GetValueOrDefault("tapeBytes")} B sha {p2.Final.GetValueOrDefault("tapeSha256")?[..16]}; viewer {va.Final.GetValueOrDefault("tapeBytes")} B sha {va.Final.GetValueOrDefault("tapeSha256")?[..16]}; freeze {vb.Final.GetValueOrDefault("tapeBytes")} B sha {vb.Final.GetValueOrDefault("tapeSha256")?[..16]} (known nondeterministic: Babushkas.cs wall-clock transforms)");

        // ---- A (viewer) and B (freeze, no viewer) against the plain run
        foreach (var (name, r) in new[] { ("A viewer", va), ("B freeze", vb) })
        {
            long cm = new[] { p1.QuitTick, r.QuitTick }.Min();
            var df = VfiDiff(p1, r, cm);
            var bad = df.Where(kv => !nd.ContainsKey(kv.Key) && !VfiKnownNondeterministic.IsMatch(kv.Key) && kv.Key != "cap.scene.frames" && !(name == "A viewer" && kv.Key == "scene._overlayExitFrame")).ToList();
            TapeInfo($"{label}: {name} vs plain: by construction (not judged): cap.scene.frames = Engine.GetProcessFrames(), the process-wide frame counter, which counts the frozen frames{(name == "A viewer" ? "; scene._overlayExitFrame, which the session owner sets on purpose to debounce the menu key (plain: " + (df.TryGetValue("scene._overlayExitFrame", out var oe) ? oe.a + ", viewer: " + oe.b : "same") + ")" : "")}");
            var masked = df.Where(kv => nd.ContainsKey(kv.Key)).Select(kv => kv.Key).ToList();
            VfiCheck(r.Digest.Count > 100 && r.QuitTick > 0, $"{label}: {name}: the run finished and wrote its digest (quit tick {r.QuitTick}, plain {p1.QuitTick})");
            VfiCheck(bad.Count == 0, $"{label}: {name} vs plain: every digest field outside the nondeterministic set is identical at every sampled tick up to {cm} ({p1.Digest.Count} fields)" + (bad.Count > 0 ? " DIFFERS: " + string.Join("; ", bad.Take(25).Select(kv => $"{kv.Key} [{kv.Value.a} vs {kv.Value.b} @ tick {kv.Value.first}, {kv.Value.count}x]")) : ""));
            TapeInfo($"{label}: {name} vs plain: nondeterministic fields that also differ here (not judged): {masked.Count} [{string.Join(", ", masked.Take(40))}]");
            var tc = VfiTraceCompare(p1.Trace, r.Trace, out bool sameT);
            VfiCheck(sameT, $"{label}: {name} vs plain: per-tick trace (car, collision circles, jets): {tc}");
            var wc = VfiTraceCompare(p1.WorldTrace, r.WorldTrace, out bool sameW);
            VfiCheck(sameW, $"{label}: {name} vs plain: WORLDTRACE file: {wc}");
            VfiCheck(p1.Final.GetValueOrDefault("ghostSha256") == r.Final.GetValueOrDefault("ghostSha256") && p1.Final.GetValueOrDefault("lapTime") == r.Final.GetValueOrDefault("lapTime"), $"{label}: {name} vs plain: the archived lap's ghost bytes and lap time are identical (sha {r.Final.GetValueOrDefault("ghostSha256")?[..16]}, {r.Final.GetValueOrDefault("lapTime")} s)");
        }
    }
}
