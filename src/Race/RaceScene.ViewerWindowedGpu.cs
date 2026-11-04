using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using CCCPRacing.Replay;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// Windowed GPU / frame evidence of the replay viewer (scenario --viewer-windowed=gpu): a live autodrive lap in the real window, the viewer opened on it,
/// real per-frame wall milliseconds while Building and Playing, memory, performance monitors, PNGs of the viewer's own SubViewport texture at several
/// clocks, the particle visibility test (the same paused frame with and without the viewer's particle nodes) and the live scene before/after.
/// Arguments: --vw-media=DIR (PNGs are never overwritten), --vl-fx=DIR with --vw-cross=TRACK (a second run on that track's fixture lap, over the same live scene).
/// </summary>
public partial class RaceScene
{
    // ------------------------------------------------------------------ small tools

    ulong _vwPrev; bool _vwSkip;

    double VwLap() { ulong now = Time.GetTicksUsec(); double ms = (now - _vwPrev) / 1000.0; _vwPrev = now; return ms; }

    /// <summary>n frames, each real frame time (ms between two driver frames) appended to the list; a frame after a GPU read-back is left out.</summary>
    IEnumerable VwTimed(List<double> into, int n)
    {
        _vwPrev = Time.GetTicksUsec();
        for (int i = 0; i < n; i++)
        {
            yield return null;
            double ms = VwLap();
            if (_vwSkip) _vwSkip = false; else into?.Add(ms);
        }
    }

    static double VwPct(List<double> sorted, double p) => sorted.Count == 0 ? double.NaN : sorted[Math.Min(sorted.Count - 1, (int)Math.Ceiling(p * sorted.Count) - 1)];

    static void VwStats(string track, string phase, List<double> v)
    {
        if (v.Count == 0) { VwLog("frames", ("track", track), ("phase", phase), ("n", 0)); return; }
        var s = v.OrderBy(x => x).ToList();
        VwLog("frames", ("track", track), ("phase", phase), ("n", v.Count), ("mean_ms", v.Average()), ("p50_ms", VwPct(s, 0.5)), ("p95_ms", VwPct(s, 0.95)), ("p99_ms", VwPct(s, 0.99)), ("max_ms", s[^1]),
            ("gt50", v.Count(x => x > 50)), ("gt100", v.Count(x => x > 100)), ("gt250", v.Count(x => x > 250)), ("sum_ms", v.Sum()));
        VwLog("framesraw", ("track", track), ("phase", phase), ("ms", string.Join(",", v.Select(x => x.ToString("0.0", CultureInfo.InvariantCulture)))));
    }

    static Dictionary<string, double> VwPerf()
    {
        double Mon(Performance.Monitor m) { try { return Performance.GetMonitor(m); } catch { return double.NaN; } }
        return new Dictionary<string, double>
        {
            ["tex_mb"] = Mon(Performance.Monitor.RenderTextureMemUsed) / 1048576.0, ["buf_mb"] = Mon(Performance.Monitor.RenderBufferMemUsed) / 1048576.0, ["video_mb"] = Mon(Performance.Monitor.RenderVideoMemUsed) / 1048576.0,
            ["objs"] = Mon(Performance.Monitor.RenderTotalObjectsInFrame), ["prims"] = Mon(Performance.Monitor.RenderTotalPrimitivesInFrame), ["draws"] = Mon(Performance.Monitor.RenderTotalDrawCallsInFrame),
            ["pipe_canvas"] = Mon(Performance.Monitor.PipelineCompilationsCanvas), ["pipe_mesh"] = Mon(Performance.Monitor.PipelineCompilationsMesh), ["pipe_surface"] = Mon(Performance.Monitor.PipelineCompilationsSurface),
            ["pipe_draw"] = Mon(Performance.Monitor.PipelineCompilationsDraw), ["pipe_spec"] = Mon(Performance.Monitor.PipelineCompilationsSpecialization),
            ["fps"] = Mon(Performance.Monitor.TimeFps), ["objects"] = Mon(Performance.Monitor.ObjectCount), ["nodes"] = Mon(Performance.Monitor.ObjectNodeCount),
        };
    }

    static void VwLogPerf(string track, string when) => VwLog("perf", new[] { ("track", (object)track), ("when", when) }.Concat(VwPerf().Select(kv => (kv.Key, (object)kv.Value))).ToArray());

    string VwMediaPath(string name)
    {
        string dir = VlArg("--vw-media");
        if (dir == null) return null;
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, name + ".png");
        for (int i = 2; File.Exists(path); i++) path = Path.Combine(dir, $"{name}-{i}.png");      // never overwrite
        return path;
    }

    Image VwGrab(Viewport vp, string pngName)
    {
        var img = vp.GetTexture().GetImage();
        _vwSkip = true;
        string path = pngName == null ? null : VwMediaPath(pngName);
        if (path != null) { var err = img.SavePng(path); if (err != Error.Ok) VwLog("png-error", ("path", path), ("error", err)); }
        return img;
    }

    /// <summary>Rendered-ness of a frame: fraction of pixels brighter than 16/255 and the number of distinct colours (a blank or clear-colour frame has almost none).</summary>
    static string VwRenderInfo(Image img)
    {
        var c = (Image)img.Duplicate(); c.Convert(Image.Format.Rgba8);
        var d = c.GetData(); int n = d.Length / 4, lit = 0; var colours = new HashSet<int>();
        for (int i = 0; i < n; i++)
        {
            int r = d[i * 4], g = d[i * 4 + 1], b = d[i * 4 + 2];
            if (Math.Max(r, Math.Max(g, b)) > 16) lit++;
            colours.Add((r >> 3) << 10 | (g >> 3) << 5 | (b >> 3));
        }
        return $"{c.GetWidth()}x{c.GetHeight()} lit {100.0 * lit / n:0.0}% colours(5bit) {colours.Count}";
    }

    /// <summary>Pixel difference of two frames: differing pixels (any channel), pixels over 8/255, mean absolute channel difference and the box around the over-8 pixels.</summary>
    static (int any, int over8, double mean, string box) VwDiffImg(Image a, Image b)
    {
        if (a.GetSize() != b.GetSize()) return (-1, -1, double.NaN, "size differs");
        var ca = (Image)a.Duplicate(); ca.Convert(Image.Format.Rgba8);
        var cb = (Image)b.Duplicate(); cb.Convert(Image.Format.Rgba8);
        var da = ca.GetData(); var db = cb.GetData(); int w = ca.GetWidth(), n = da.Length / 4, any = 0, over = 0; double sum = 0;
        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
        for (int i = 0; i < n; i++)
        {
            int dr = Math.Abs(da[i * 4] - db[i * 4]), dg = Math.Abs(da[i * 4 + 1] - db[i * 4 + 1]), dbl = Math.Abs(da[i * 4 + 2] - db[i * 4 + 2]);
            int m = Math.Max(dr, Math.Max(dg, dbl));
            sum += (dr + dg + dbl) / 3.0;
            if (m > 0) any++;
            if (m > 8) { over++; int x = i % w, y = i / w; x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); }
        }
        return (any, over, sum / n, over == 0 ? "-" : $"({x0},{y0})-({x1},{y1})");
    }

    static string VwDiffText(Image a, Image b) { var (any, over8, mean, box) = VwDiffImg(a, b); return $"differing {any}, over8 {over8}, mean {mean:0.000}, box {box}"; }

    // ------------------------------------------------------------------ the scenario

    IEnumerable VwGpu()
    {
        string track = _trackId;
        foreach (var x in VwWaitReady()) yield return x;
        VwLog("gpu-start", ("track", track), ("window", DisplayServer.WindowGetSize()), ("live_viewport", GetViewport().GetVisibleRect().Size), ("adapter", RenderingServer.GetVideoAdapterName()), ("vendor", RenderingServer.GetVideoAdapterVendor()),
            ("method", RenderingServer.GetCurrentRenderingMethod()), ("driver", RenderingServer.GetCurrentRenderingDriverName()));
        // 1. the live scene plays a lap (the autodrive); wait for it to be archived
        int w = 0;
        while (_state != State.Racing && w++ < 60 * 120) yield return null;
        LapSource lap = null;
        foreach (var _ in VtWaitLap(l => lap = l)) yield return null;
        if (lap == null) { VwLog("fatal", ("why", "no lap reached the archive")); yield break; }
        VwLog("lap", ("track", track), ("id", lap.Meta.Id), ("seconds", lap.Meta.LapSeconds), ("tape", lap.HasTape));
        // 2. stop the car so the live frame is steady, then the "before" picture
        _autodrive = false;
        Input.ActionPress("brake");
        w = 0; while (_car.Vel.Length() > 0.2f && _state == State.Racing && w++ < 60 * 40) yield return null;      // (a live game already in its pause card is left as it is)
        Input.ActionRelease("brake");
        VwLog("stopped", ("speed", _car.Vel.Length()), ("frames", w), ("state", _state));
        foreach (var _ in VwFrames(90)) yield return null;
        VwLog("live-state", ("track", track), ("state", _state), ("speed", _car.Vel.Length()));
        // 3. the same-track run
        foreach (var x in VwViewerRun(lap, track, "same")) yield return x;
        // 4. the cross-track run (ilyich live with a siberia lap)
        string cross = VlArg("--vw-cross");
        if (cross != null)
        {
            var crossLap = VlLap(cross);
            if (crossLap == null) VwLog("cross-skipped", ("why", "no fixture lap for " + cross));
            else foreach (var x in VwViewerRun(crossLap, track, "cross-" + cross)) yield return x;
        }
    }

    IEnumerable VwViewerRun(LapSource lap, string liveTrack, string kind)
    {
        string tag = $"{liveTrack}-{kind}";
        // settle and measure the baseline
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        foreach (var _ in VwFrames(10)) yield return null;
        var (rss0, hwm0) = SpikeMem();
        bool reset = SpikeResetPeak();
        var (rssR, hwmR) = SpikeMem();
        VwLogPerf(tag, "baseline");
        VwLog("mem", ("tag", tag), ("when", "baseline"), ("rss_mb", rss0), ("hwm_mb_lifetime", hwm0), ("hwm_reset_ok", reset), ("hwm_after_reset_mb", hwmR));
        // the live scene's own change over 3 real frames (the yardstick), then the "before" picture taken in the very frame of the open
        var natA = VwGrab(GetViewport(), $"{tag}-live-natural-a");
        foreach (var x in VwTimed(null, 170)) yield return x;      // (about as long as the viewer session will last: the live world's own change over that time)
        var natB = VwGrab(GetViewport(), null);
        foreach (var x in VwTimed(null, 3)) yield return x;
        var liveBefore = VwGrab(GetViewport(), $"{tag}-live-before-open");
        var natural = VwDiffImg(natA, natB);
        var sigBefore = VwSig();
        // open (the call itself is synchronous: Suspend + the first build step)
        var sw = Stopwatch.StartNew();
        var s = OpenReplayViewer(lap, out string err);
        double openCallMs = sw.Elapsed.TotalMilliseconds;
        VwLog("open", ("tag", tag), ("ok", s != null), ("error", err), ("open_call_ms", openCallMs));
        VwCheck(s != null, $"{tag}: the viewer opened (error {err})");
        if (s == null) yield break;
        var building = new List<double>(); var first30 = new List<double>(); var steady = new List<double>(); var paused = new List<double>(); var wrap = new List<double>(); var closing = new List<double>();
        // Building: every frame
        _vwPrev = Time.GetTicksUsec(); int bf = 0;
        var bDp = new List<int>(); var bDn = new List<long>(); int bPipe = VwPipeSum(); long bNodes = (long)Performance.GetMonitor(Performance.Monitor.ObjectNodeCount);
        while (s.State == ReplayViewerState.Building && bf++ < 60 * 300)
        {
            yield return null; building.Add(VwLap());
            int bp = VwPipeSum(); bDp.Add(bp - bPipe); bPipe = bp;
            long bn = (long)Performance.GetMonitor(Performance.Monitor.ObjectNodeCount); bDn.Add(bn - bNodes); bNodes = bn;
        }
        VwStallTable(tag + "-building", building, bDp, bDn);
        double buildElapsed = sw.Elapsed.TotalMilliseconds;
        var (rssB, hwmB) = SpikeMem();
        VwCheck(s.State == ReplayViewerState.Playing, $"{tag}: the viewer reached Playing (state {s.State}, error {s.Error})");
        VwLog("built", ("tag", tag), ("build_elapsed_ms", buildElapsed), ("frames", building.Count), ("steps", s.Scene?.ViewerBuildSteps), ("rss_mb", rssB), ("hwm_mb_since_reset", hwmB));
        VwStats(tag, "building", building);
        if (s.State != ReplayViewerState.Playing) { s.Close(); yield break; }
        var v = s.Scene;
        VwLogPerf(tag, "playing-first-frame");
        // Playing: the first 30 frames apart (shader / pipeline hitches), then steady
        foreach (var x in VwTimed(first30, 30)) yield return x;
        VwLogPerf(tag, "playing-30");
        foreach (var x in VwTimed(steady, 300)) yield return x;
        VwStats(tag, "playing-first30", first30);
        VwStats(tag, "playing-steady", steady);
        VwLogPerf(tag, "playing-steady");
        var (rssP, hwmP) = SpikeMem();
        VwLog("mem", ("tag", tag), ("when", "playing"), ("rss_mb", rssP), ("hwm_mb_since_reset", hwmP));
        VwCheck(ProcessMode == ProcessModeEnum.Disabled && _vwModeViolations == 0, $"{tag}: the live world stayed Disabled through Building and Playing");
        // particles in the viewer's own world
        var gpu = v.FindChildren("*", "", true, false).OfType<GpuParticles3D>().ToList();
        var cpu = v.FindChildren("*", "", true, false).OfType<CpuParticles3D>().ToList();
        VwLog("particles", ("tag", tag), ("gpu_nodes", gpu.Count), ("gpu_emitting", gpu.Count(p => p.Emitting)), ("gpu_visible", gpu.Count(p => p.IsVisibleInTree())), ("cpu_nodes", cpu.Count), ("cpu_emitting", cpu.Count(p => p.Emitting)), ("cpu_visible", cpu.Count(p => p.IsVisibleInTree())),
            ("gpu_names", string.Join(",", gpu.Select(p => $"{p.Name}({p.Amount})").Take(14))), ("cpu_names", string.Join(",", cpu.Select(p => $"{p.Name}({p.Amount})").Take(14))));
        // a playing frame
        VwGrab(v.GetViewport(), $"{tag}-viewer-playing-c{v.ViewerClock:0.0}".Replace(',', '.'));
        // clocks: seek, play 1.5 s so the particles exist, pause, compare
        float dur = v.ViewerDuration;
        var clocks = new[] { 0.04f, 0.15f, 0.3f, 0.45f, 0.6f, 0.75f, 0.9f };
        var particleRows = new List<string>();
        int clockNo = 0, gpuPositive = 0, cpuPositive = 0, frozenOk = 0, frozenTotal = 0, resumeChanged = 0;
        foreach (float f in clocks)
        {
            clockNo++;
            float target = dur * f;
            v.ViewerSeek(target);
            foreach (var x in VwTimed(null, 100)) yield return x;      // play on: particles emitted
            v.ViewerSetPaused(true);
            foreach (var x in VwTimed(paused, 8)) yield return x;
            string stem = $"{tag}-viewer-c{clockNo}-t{v.ViewerClock:0.0}".Replace(',', '.');
            var a = VwGrab(v.GetViewport(), stem);
            VwLog("clock", ("tag", tag), ("n", clockNo), ("clock", v.ViewerClock), ("duration", dur), ("render", VwRenderInfo(a)));
            // frozen: the paused frame again after 3, 5 and 10 real frames
            var snap0 = v.FindChildren("*", "", true, false).OfType<Node3D>().ToDictionary(n => n, n => n.Transform);
            var gaps = new[] { 3, 5, 10 }; var dsum = new List<string>(); bool allSame = true;
            foreach (int gap in gaps)
            {
                foreach (var x in VwTimed(paused, gap)) yield return x;
                var b = VwGrab(v.GetViewport(), null);
                var (any, over8, mean, box) = VwDiffImg(a, b);
                dsum.Add($"+{gap}f:{any}/{over8}"); if (any != 0) allSame = false;
            }
            frozenTotal++; if (allSame) frozenOk++;
            if (!allSame)
            {
                // which Node3D moved while the viewer was paused (3+5+10 frames), and which animation players / tweens run
                var moved = new List<string>();
                foreach (var (n, t0) in snap0)
                    if (IsInstanceValid(n) && (n.Transform.Origin.DistanceTo(t0.Origin) > 1e-4f || (n.Transform.Basis.X - t0.Basis.X).Length() > 1e-4f || (n.Transform.Basis.Y - t0.Basis.Y).Length() > 1e-4f))
                        moved.Add($"{v.GetPathTo(n)}[{n.GetType().Name}]d={n.Transform.Origin.DistanceTo(t0.Origin):0.####}");
                var anims = v.FindChildren("*", "", true, false).OfType<AnimationPlayer>().Where(p => p.IsPlaying()).Select(p => v.GetPathTo(p).ToString()).ToList();
                VwLog("paused-motion", ("tag", tag), ("clock_no", clockNo), ("nodes_moved", moved.Count), ("moved", string.Join(" ; ", moved.Take(12))), ("animation_players_playing", anims.Count), ("anims", string.Join(" ; ", anims.Take(6))), ("tweens", GetTree().GetProcessedTweens().Count));
            }
            // with / without the particle nodes (same paused world, only Visible differs)
            var gHidden = gpu.Where(p => p.Visible).ToList(); var cHidden = cpu.Where(p => p.Visible).ToList();
            foreach (var p in gHidden) p.Visible = false;
            foreach (var x in VwTimed(paused, 3)) yield return x;
            var noGpu = VwGrab(v.GetViewport(), clockNo == 1 || clockNo == 4 ? stem + "-no-gpu-particles" : null);
            foreach (var p in gHidden) p.Visible = true;
            foreach (var p in cHidden) p.Visible = false;
            foreach (var x in VwTimed(paused, 3)) yield return x;
            var noCpu = VwGrab(v.GetViewport(), clockNo == 1 || clockNo == 4 ? stem + "-no-cpu-particles" : null);
            foreach (var p in gHidden) p.Visible = false;
            foreach (var x in VwTimed(paused, 3)) yield return x;
            var noAny = VwGrab(v.GetViewport(), null);
            foreach (var p in gHidden) p.Visible = true;
            foreach (var p in cHidden) p.Visible = true;
            foreach (var x in VwTimed(paused, 3)) yield return x;
            var back = VwGrab(v.GetViewport(), null);
            var dG = VwDiffImg(a, noGpu); var dC = VwDiffImg(a, noCpu); var dA = VwDiffImg(a, noAny); var dBack = VwDiffImg(a, back);
            if (dG.over8 > 0) gpuPositive++; if (dC.over8 > 0) cpuPositive++;
            // resume: the frame changes
            v.ViewerSetPaused(false);
            foreach (var x in VwTimed(null, 20)) yield return x;
            var res = VwGrab(v.GetViewport(), null);
            var dR = VwDiffImg(a, res); if (dR.over8 > 0) resumeChanged++;
            particleRows.Add($"c{clockNo}@{target:0.0}s gpuNodes visible {gHidden.Count} cpuNodes visible {cHidden.Count} | hide gpu: {dG.any}/{dG.over8} box {dG.box} | hide cpu: {dC.any}/{dC.over8} box {dC.box} | hide both: {dA.any}/{dA.over8} | restored: {dBack.any} | paused again {string.Join(" ", dsum)} | resume+20f: {dR.any}/{dR.over8}");
            VwLog("particle-test", ("tag", tag), ("clock_no", clockNo), ("t", target), ("gpu_visible", gHidden.Count), ("cpu_visible", cHidden.Count), ("hide_gpu_diff_any", dG.any), ("hide_gpu_diff_over8", dG.over8), ("hide_gpu_box", dG.box),
                ("hide_cpu_diff_any", dC.any), ("hide_cpu_diff_over8", dC.over8), ("hide_cpu_box", dC.box), ("hide_both_over8", dA.over8), ("restored_diff_any", dBack.any), ("paused_repeat", string.Join(" ", dsum)), ("paused_identical", allSame),
                ("resume_diff_any", dR.any), ("resume_diff_over8", dR.over8));
        }
        VwLog("particle-summary", ("tag", tag), ("clocks", clocks.Length), ("clocks_with_gpu_particle_pixels", gpuPositive), ("clocks_with_cpu_particle_pixels", cpuPositive), ("clocks_paused_frames_identical", $"{frozenOk}/{frozenTotal}"), ("clocks_changed_after_resume", resumeChanged));
        VwStats(tag, "paused", paused);
        // seek + loop wrap
        v.ViewerSetPaused(false);
        v.ViewerLoop = true;
        v.ViewerSeek(Math.Max(0f, dur - 2f));
        float last = v.ViewerClock; bool wrapped = false; int wf = 0;
        _vwPrev = Time.GetTicksUsec();
        while (wf++ < 60 * 20 && !wrapped) { yield return null; wrap.Add(VwLap()); if (v.ViewerClock < last - 1f) wrapped = true; last = v.ViewerClock; }
        VwLog("wrap", ("tag", tag), ("wrapped", wrapped), ("frames", wf), ("clock_after", v.ViewerClock));
        VwCheck(wrapped, $"{tag}: the loop wrapped from the end of the lap (clock now {v.ViewerClock:0.00})");
        VwStats(tag, "seek-and-wrap", wrap);
        VwCheck(frozenOk == frozenTotal, $"{tag}: paused viewer frames were pixel-identical across 3/5/10 real frames at {frozenOk} of {frozenTotal} clocks");
        VwCheck(resumeChanged > 0, $"{tag}: resuming changes the frame (at {resumeChanged} of {clocks.Length} clocks)");
        // close
        var (rssC0, hwmC0) = SpikeMem();
        s.Close();
        _vwPrev = Time.GetTicksUsec(); int cf = 0;
        while (s.State != ReplayViewerState.Closed && cf++ < 600) { yield return null; closing.Add(VwLap()); }
        VwStats(tag, "closing", closing);
        VwCheck(s.State == ReplayViewerState.Closed && ProcessMode == ProcessModeEnum.Inherit, $"{tag}: closed and the live scene restored (state {s.State}, mode {ProcessMode})");
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        foreach (var _ in VwFrames(6)) yield return null;
        SpikeMallocTrim(0);
        var (rssD, hwmD) = SpikeMem();
        VwLogPerf(tag, "closed");
        VwLog("mem", ("tag", tag), ("when", "closed"), ("rss_mb", rssD), ("hwm_mb_since_reset", hwmD), ("rss_before_close_mb", rssC0), ("baseline_rss_mb", rss0));
        // the live scene after
        foreach (var _ in VwFrames(3)) yield return null;
        var liveAfter = VwGrab(GetViewport(), $"{tag}-live-after-close");
        var d = VwDiffImg(liveBefore, liveAfter);
        VwLog("live-compare", ("tag", tag), ("before_render", VwRenderInfo(liveBefore)), ("after_render", VwRenderInfo(liveAfter)), ("differing_pixels", d.any), ("over8_pixels", d.over8), ("mean_abs_diff", d.mean), ("box_over8", d.box),
            ("total_pixels", liveBefore.GetWidth() * liveBefore.GetHeight()), ("natural_170frames_over8", natural.over8), ("natural_170frames_any", natural.any), ("natural_box", natural.box), ("live_state", _state), ("sig_diff_vs_before_open", VwDiff(sigBefore, VwSig(), "lapClock", "raceClock", "replayClock", "carPos")));
        VwCheck(d.any >= 0 && d.over8 <= Math.Max(natural.over8 * 3 / 2, liveBefore.GetWidth() * liveBefore.GetHeight() / 100), $"{tag}: the live scene 3 frames after the close matches the one at the open at least as well as the live scene matches itself over 170 frames (1.5x allowed) ({d.over8} pixels over 8/255 vs {natural.over8} natural, of {liveBefore.GetWidth() * liveBefore.GetHeight()}; box {d.box})");
    }
}
