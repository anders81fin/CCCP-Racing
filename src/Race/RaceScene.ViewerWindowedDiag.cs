using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using CCCPRacing.Replay;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// Windowed diagnostics of the replay viewer (follow-up scenarios): the existing track-load path as a baseline for the viewer's stalls (load-baseline),
/// the pieces of ViewerSeek / TapeSeek timed on a real window (seek-diag), and a clean GPU-particle case with a test camera placed on the atomgrad train (gpu-particle).
/// Test code only: nothing here edits a production path; the test camera lives in the viewer's own world and is freed with it.
/// </summary>
public partial class RaceScene
{
    static int VwPipeSum()
    {
        double Mon(Performance.Monitor m) { try { return Performance.GetMonitor(m); } catch { return 0; } }
        return (int)(Mon(Performance.Monitor.PipelineCompilationsCanvas) + Mon(Performance.Monitor.PipelineCompilationsMesh) + Mon(Performance.Monitor.PipelineCompilationsSurface)
            + Mon(Performance.Monitor.PipelineCompilationsDraw) + Mon(Performance.Monitor.PipelineCompilationsSpecialization));
    }

    static void VwStallTable(string tag, List<double> ms, List<int> dpipe, List<long> dnodes)
    {
        VwStats(tag, "frames", ms);
        var big = new List<string>();
        for (int i = 0; i < ms.Count; i++) if (ms[i] > 100) big.Add($"#{i}:{ms[i]:0}ms/pipe+{dpipe[i]}/nodes{(dnodes[i] >= 0 ? "+" : "")}{dnodes[i]}");
        int frames100 = ms.Count(x => x > 100);
        double meanPipeBig = frames100 == 0 ? 0 : Enumerable.Range(0, ms.Count).Where(i => ms[i] > 100).Average(i => dpipe[i]);
        var rest = Enumerable.Range(0, ms.Count).Where(i => ms[i] <= 100).ToList();
        double meanPipeRest = rest.Count == 0 ? 0 : rest.Average(i => dpipe[i]);
        VwLog("stalls", ("tag", tag), ("over100", frames100), ("total_pipeline_compiles", dpipe.Sum()), ("frames_over100_with_compile", Enumerable.Range(0, ms.Count).Count(i => ms[i] > 100 && dpipe[i] > 0)),
            ("mean_compiles_in_over100", meanPipeBig), ("mean_compiles_in_rest", meanPipeRest), ("list", string.Join(" ", big)));
    }

    // ------------------------------------------------------------------ B: the existing track load, windowed (the game's own route: a menu run start on another track)

    /// <summary>The title starts a run on another track: StartRun shows the TrackLoadingScreen, the scene is reloaded and rebuilt in slices. Real wall frame times from the call to the end of the build.</summary>
    IEnumerable VwLoadBaseline()
    {
        string target = VlArg("--vw-load-target") ?? "ilyich";
        foreach (var x in VwWaitReady()) yield return x;
        foreach (var _ in VwFrames(120)) yield return null;
        var tree = GetTree();
        var old = this;
        string from = _trackId;
        VwLog("baseline-start", ("from", from), ("to", target), ("state", _state), ("booting", _booting));
        _chosenRunTrack = target;
        var ms = new List<double>(); var dp = new List<int>(); var dn = new List<long>();
        int pipe0 = VwPipeSum(); long nodes0 = (long)Performance.GetMonitor(Performance.Monitor.ObjectNodeCount);
        ulong prev = Time.GetTicksUsec();
        var sw = Stopwatch.StartNew();
        StartRun(MenuItem.Race);      // (from here on this instance is freed by the reload: statics and locals only)
        RaceScene fresh = null; int frames = 0; double buildMs = -1;
        while (frames++ < 60 * 180)
        {
            yield return null;
            ulong now = Time.GetTicksUsec(); ms.Add((now - prev) / 1000.0); prev = now;
            int p = VwPipeSum(); dp.Add(p - pipe0); pipe0 = p;
            long n = (long)Performance.GetMonitor(Performance.Monitor.ObjectNodeCount); dn.Add(n - nodes0); nodes0 = n;
            fresh = VwFindLive(tree.Root);
            if (fresh != null && !ReferenceEquals(fresh, old) && IsInstanceValid(fresh) && !fresh._booting && fresh._hud != null && fresh._dayNight != null && fresh._timer != null) { buildMs = sw.Elapsed.TotalMilliseconds; break; }
        }
        VwLog("baseline-done", ("to", target), ("ready", buildMs >= 0), ("elapsed_ms", buildMs), ("frames", ms.Count), ("track_now", _trackId));
        VwCheck(buildMs >= 0, $"load-baseline {from}->{target}: the track change finished loading (elapsed {buildMs:0} ms)");
        VwStallTable("load-" + target, ms, dp, dn);
        // let the new scene settle a few frames and record its first frames too (the first frames' hitches are part of what a player sees)
        var after = new List<double>(); var ap = new List<int>(); var an = new List<long>();
        for (int i = 0; i < 120; i++)
        {
            yield return null;
            ulong now = Time.GetTicksUsec(); after.Add((now - prev) / 1000.0); prev = now;
            int p = VwPipeSum(); ap.Add(p - pipe0); pipe0 = p;
            long n = (long)Performance.GetMonitor(Performance.Monitor.ObjectNodeCount); an.Add(n - nodes0); nodes0 = n;
        }
        VwStallTable("load-" + target + "-first120-after", after, ap, an);
    }

    // ------------------------------------------------------------------ B2: the viewer build only (frame stalls while Building; the fixture lap is used, no lap has to be driven)

    /// <summary>Opens the viewer on the track's fixture lap over the idle live scene and records every real frame of the build (scenario --viewer-windowed=build-diag, --vl-fx=DIR, --vw-rounds=N). Test aid for the build slicing.</summary>
    IEnumerable VwBuildDiag()
    {
        string track = _trackId;
        foreach (var x in VwWaitReady()) yield return x;
        foreach (var _ in VwFrames(120)) yield return null;
        var lap = VlLap(track);
        if (lap == null) { VwLog("fatal", ("why", "no fixture lap for " + track)); yield break; }
        int rounds = int.TryParse(VlArg("--vw-rounds"), out int r) ? r : 1;
        for (int round = 0; round < rounds; round++)
        {
            string tag = $"{track}-build{round}";
            var sw = Stopwatch.StartNew();
            var s = VwOpen(lap, tag, out string err);
            double openMs = sw.Elapsed.TotalMilliseconds;
            VwLog("open", ("tag", tag), ("ok", s != null), ("error", err), ("open_call_ms", openMs));
            if (s == null) yield break;
            var ms = new List<double>(); var dp = new List<int>(); var dn = new List<long>();
            int pipe0 = VwPipeSum(); long nodes0 = (long)Performance.GetMonitor(Performance.Monitor.ObjectNodeCount);
            ms.Add(openMs); dp.Add(0); dn.Add(0); var gcMs = new List<double> { 0 }; var yl = new List<int> { 0 }; var procMs = new List<string> { "-" }; ulong tProc = 0, tPre = 0, tPost = 0; Action onProc = () => tProc = Time.GetTicksUsec(); Action onPre = () => tPre = Time.GetTicksUsec(); Action onPost = () => tPost = Time.GetTicksUsec();
            GetTree().ProcessFrame += onProc; RenderingServer.FramePreDraw += onPre; RenderingServer.FramePostDraw += onPost; double gc0 = GC.GetTotalPauseDuration().TotalMilliseconds; int g20 = GC.CollectionCount(2);
            ulong prev = Time.GetTicksUsec(); int f = 0;
            while (s.State == ReplayViewerState.Building && f++ < 60 * 300)
            {
                yield return null;
                ulong now = Time.GetTicksUsec(); ms.Add((now - prev) / 1000.0); prev = now;
                int p = VwPipeSum(); dp.Add(p - pipe0); pipe0 = p;
                long n = (long)Performance.GetMonitor(Performance.Monitor.ObjectNodeCount); dn.Add(n - nodes0); nodes0 = n;
                double gc1 = GC.GetTotalPauseDuration().TotalMilliseconds; gcMs.Add(gc1 - gc0); gc0 = gc1; yl.Add(LoadProf.LastYield); procMs.Add($"frame start->predraw {(tPre - tProc) / 1000.0:0} ms, predraw->postdraw {(tPost - tPre) / 1000.0:0} ms, postdraw->sample {(now - tPost) / 1000.0:0} ms");
            }
            GetTree().ProcessFrame -= onProc; RenderingServer.FramePreDraw -= onPre; RenderingServer.FramePostDraw -= onPost;
            VwLog("stall-lines", ("tag", tag), ("list", string.Join(" ", Enumerable.Range(0, ms.Count).Where(i => ms[i] > 100).Select(i => $"#{i}:{ms[i]:0}ms@L{yl[i]}({procMs[i]})"))));
            VwLog("gc", ("tag", tag), ("gen2", GC.CollectionCount(2) - g20), ("pause_ms_total", gcMs.Sum()), ("pause_ms_in_over100", Enumerable.Range(0, ms.Count).Where(i => ms[i] > 100).Sum(i => gcMs[i])), ("max_pause_ms", gcMs.Max()));
            VwStallTable(tag, ms, dp, dn);
            VwLog("build-done", ("tag", tag), ("state", s.State), ("error", s.Error), ("elapsed_ms", sw.Elapsed.TotalMilliseconds), ("frames", ms.Count), ("max_ms", ms.Max()), ("gt100", ms.Count(x => x > 100)), ("gt250", ms.Count(x => x > 250)));
            VwCheck(s.State == ReplayViewerState.Playing, $"{tag}: the viewer reached Playing (state {s.State}, error {s.Error})");
            foreach (var _ in VwFrames(60)) yield return null;
            s.Close();
            f = 0; while (s.State != ReplayViewerState.Closed && f++ < 600) yield return null;
            foreach (var _ in VwFrames(30)) yield return null;
        }
    }

    // ------------------------------------------------------------------ viewer helper

    IEnumerable VwOpenPlaying(LapSource lap, string tag, Action<ReplayViewerSession> got)
    {
        var s = VwOpen(lap, tag, out string err);
        VwCheck(s != null, $"{tag}: the viewer opened ({err})");
        if (s == null) yield break;
        int w = 0; while (s.State == ReplayViewerState.Building && w++ < 60 * 300) yield return null;
        VwCheck(s.State == ReplayViewerState.Playing, $"{tag}: the viewer reached Playing (state {s.State}, error {s.Error})");
        if (s.State == ReplayViewerState.Playing) got(s);
    }

    // ------------------------------------------------------------------ C: where the seek / wrap hitch goes

    /// <summary>ViewerSeek / TapeSeek with a stopwatch around each step (the same calls in the same order; see RaceScene.Viewer.cs ViewerSeek and RaceScene.Tape.cs TapeSeek).</summary>
    void VwSeekTimed(float clock, List<(string name, double ms)> into)
    {
        var sw = Stopwatch.StartNew(); double last = 0;
        void T(string n) { double e = sw.Elapsed.TotalMilliseconds; into.Add((n, e - last)); last = e; }
        _replayClock = Mathf.Clamp(clock, 0f, ViewerDuration); ViewerEnded = false;
        TitleGhost.Rewind(); TitleGhost.Apply(_replayClock, _car, 0); T("ghost rewind+apply");
        _wood.Reset(); T("_wood.Reset"); _scenery.Reset(); T("_scenery.Reset"); _cones?.Reset(); T("_cones.Reset"); _carVis.Damage?.Reset(); T("damage.Reset");
        ClearCarFeedback(); _fx.Clear(); T("ClearCarFeedback");
        // TapeReplayEnter -> TapeSeek
        if (TapePlayer() != null)
        {
            ClearCarFeedback(); T("TapeSeek: ClearCarFeedback");
            foreach (var a in _tapeDriven) a.TapeDrive(true); T("TapeSeek: TapeDrive(true) x" + _tapeDriven.Count);
            if (!_tapePosed) TapeParkCircles(true); T("TapeSeek: TapeParkCircles");
            _tapeEmit?.Restart(); T("TapeSeek: _tapeEmit.Restart");
            _tapePlayer.Seek(_replayClock); T("TapeSeek: _tapePlayer.Seek");
            TapeRestoreDerivedParts(); T("TapeSeek: TapeRestoreDerivedParts");
            _tapeFx.Update(_replayClock); T("TapeSeek: _tapeFx.Update");
            RestoreCollisionFeedback(_replayClock); T("TapeSeek: RestoreCollisionFeedback");
            TitleGhost?.ApplyDamage(_replayClock, _carVis.Damage, _fx); T("TapeSeek: ApplyDamage");
            _tapePosed = true; _tapeSeeks++;
        }
        _replayCamera.Reset(); _cam.Snap(_car.Pos); T("camera");
    }

    IEnumerable VwSeekDiag()
    {
        var lap = VwFixtureLap(VlArg("--vw-lap") ?? _trackId);
        foreach (var x in VwWaitReady()) yield return x;
        ReplayViewerSession s = null;
        foreach (var x in VwOpenPlaying(lap, "seek-diag", r => s = r)) yield return x;
        if (s == null) yield break;
        var v = s.Scene; float dur = v.ViewerDuration;
        foreach (var _ in VwFrames(60)) yield return null;
        string track = _trackId;
        // seeks, cold then warm, with the following frames timed
        var plan = new[] { 0.1f, 0.9f, 0.5f, 0.9f, 0.1f, 0.9f };
        int no = 0;
        foreach (float f in plan)
        {
            no++;
            var pieces = new List<(string, double)>();
            int pipeA = VwPipeSum(); long nodesA = (long)Performance.GetMonitor(Performance.Monitor.ObjectNodeCount); int liveA = v._tapePlayer?.LiveInstances.Count ?? -1; ulong spawnedA = (ulong)(v._tapeSink?.Spawned ?? 0);
            var swAll = Stopwatch.StartNew();
            v.VwSeekTimed(dur * f, pieces);
            double cpu = swAll.Elapsed.TotalMilliseconds;
            int pipeB = VwPipeSum(); long nodesB = (long)Performance.GetMonitor(Performance.Monitor.ObjectNodeCount); int liveB = v._tapePlayer?.LiveInstances.Count ?? -1; ulong spawnedB = (ulong)(v._tapeSink?.Spawned ?? 0);
            var next = new List<double>(); var nextPipe = new List<int>();
            ulong prev = Time.GetTicksUsec(); int pp = VwPipeSum();
            for (int i = 0; i < 3; i++) { yield return null; ulong now = Time.GetTicksUsec(); next.Add((now - prev) / 1000.0); prev = now; int p2 = VwPipeSum(); nextPipe.Add(p2 - pp); pp = p2; }
            VwLog("seek", ("track", track), ("n", no), ("to", dur * f), ("cpu_total_ms", cpu), ("pieces", string.Join(" ; ", pieces.Where(p => p.Item2 >= 0.5).Select(p => $"{p.Item1} {p.Item2:0.0}"))),
                ("pipeline_compiles_in_call", pipeB - pipeA), ("nodes_in_call", nodesB - nodesA), ("tape_live_instances", $"{liveA}->{liveB}"), ("tape_spawned_total", $"{spawnedA}->{spawnedB}"),
                ("next3_frames_ms", string.Join(",", next.Select(x => x.ToString("0")))), ("next3_pipeline_compiles", string.Join(",", nextPipe)));
        }
        // inside WorldTapePlayer.Seek: after a spawn or despawn ForgetNodes() empties the node cache (WorldTapePlayer.cs:98,115,120) and every part is resolved again on the next pose. Time that directly (reflection; test only).
        {
            var pl = v._tapePlayer; var t = pl.GetType(); var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var nodes = (Node3D[])t.GetField("_nodes", flags).GetValue(pl);
            var forget = t.GetMethod("ForgetNodes", flags); var nodeOf = t.GetMethod("Node", flags);
            var dirtyF = (List<int>)t.GetField("_dirty", flags).GetValue(pl);
            int np = nodes.Length;
            for (int rep = 0; rep < 3; rep++)
            {
                forget.Invoke(pl, null);
                var swr = Stopwatch.StartNew(); int resolved = 0;
                for (int tp = 0; tp < np; tp++) if (nodeOf.Invoke(pl, new object[] { tp }) != null) resolved++;
                VwLog("node-cache", ("track", track), ("rep", rep), ("parts", np), ("resolved", resolved), ("resolve_all_ms", swr.Elapsed.TotalMilliseconds), ("spawned_instances", ((System.Collections.IDictionary)t.GetField("_spawned", flags).GetValue(pl)).Count));
            }
            // and the whole Seek with a warm cache: no instance to spawn means no ForgetNodes; time Seek itself again for reference
            var swS = Stopwatch.StartNew(); pl.Seek(v.ViewerClock); VwLog("player-seek", ("track", track), ("ms", swS.Elapsed.TotalMilliseconds));
        }
        // the models a Seek spawns: build each live instance's kind again (RaceScene.Tape.Instances.cs TapeBuild, what TapeSink.Spawn runs) and time it; the nodes are freed at once
        foreach (var (id, node) in v._tapeSink.Live.ToArray())
        {
            string kind = node.HasMeta(WorldTapeConst.KindMeta) ? (string)node.GetMeta(WorldTapeConst.KindMeta) : "?";
            for (int rep = 0; rep < 3; rep++)
            {
                var swB = Stopwatch.StartNew(); long n0 = (long)Performance.GetMonitor(Performance.Monitor.ObjectNodeCount);
                var m = TapeBuild(kind, v._fx);
                double bms = swB.Elapsed.TotalMilliseconds; long nn = m == null ? 0 : SpikeNodes(m);
                m?.Free();
                VwLog("spawn-build", ("track", track), ("instance", id), ("kind", kind), ("rep", rep), ("build_ms", bms), ("nodes", nn));
            }
        }
        // the natural loop wrap: per frame ms, compiles and nodes across it
        v.ViewerSeek(Math.Max(0f, dur - 1.5f));
        v.ViewerLoop = true;
        var ms = new List<double>(); var dp = new List<int>(); var dn = new List<long>(); var live = new List<string>();
        float last = v.ViewerClock; bool wrapped = false; int wf = 0; int pipe0 = VwPipeSum(); long nodes0 = (long)Performance.GetMonitor(Performance.Monitor.ObjectNodeCount); ulong pv = Time.GetTicksUsec();
        while (wf++ < 60 * 12 && !wrapped)
        {
            yield return null;
            ulong now = Time.GetTicksUsec(); ms.Add((now - pv) / 1000.0); pv = now;
            int p = VwPipeSum(); dp.Add(p - pipe0); pipe0 = p;
            long n = (long)Performance.GetMonitor(Performance.Monitor.ObjectNodeCount); dn.Add(n - nodes0); nodes0 = n;
            live.Add((v._tapePlayer?.LiveInstances.Count ?? -1).ToString());
            if (v.ViewerClock < last - 1f) wrapped = true;
            last = v.ViewerClock;
        }
        VwLog("wrap", ("track", track), ("wrapped", wrapped), ("frames", ms.Count));
        VwStallTable("wrap-" + track, ms, dp, dn);
        int wi = ms.Count - 1;
        VwLog("wrap-frame", ("track", track), ("ms", ms[wi]), ("pipeline_compiles", dp[wi]), ("nodes_delta", dn[wi]), ("tape_live_instances_before_after", live.Count > 1 ? live[^2] + "->" + live[^1] : "-"));
        // a second wrap (warm) for comparison
        v.ViewerSeek(Math.Max(0f, dur - 1.5f)); wrapped = false; wf = 0; last = v.ViewerClock; var ms2 = new List<double>(); var dp2 = new List<int>(); pipe0 = VwPipeSum(); pv = Time.GetTicksUsec();
        while (wf++ < 60 * 12 && !wrapped)
        {
            yield return null;
            ulong now = Time.GetTicksUsec(); ms2.Add((now - pv) / 1000.0); pv = now;
            int p = VwPipeSum(); dp2.Add(p - pipe0); pipe0 = p;
            if (v.ViewerClock < last - 1f) wrapped = true;
            last = v.ViewerClock;
        }
        VwLog("wrap-frame-second", ("track", track), ("ms", ms2[^1]), ("pipeline_compiles", dp2[^1]));
        s.Close();
        int g = 0; while (s.State != ReplayViewerState.Closed && g++ < 600) yield return null;
    }

    // ------------------------------------------------------------------ D: a clean GPU particle case

    IEnumerable VwGpuParticle()
    {
        var lap = VwFixtureLap(VlArg("--vw-lap") ?? _trackId);
        foreach (var x in VwWaitReady()) yield return x;
        ReplayViewerSession s = null;
        foreach (var x in VwOpenPlaying(lap, "gpu-particle", r => s = r)) yield return x;
        if (s == null) yield break;
        var v = s.Scene; float dur = v.ViewerDuration;
        string track = _trackId;
        var gp = v.FindChildren("DieselExhaust", "", true, false).OfType<GpuParticles3D>().FirstOrDefault();
        VwCheck(gp != null, $"{track}: the viewer world has the train's DieselExhaust GpuParticles3D");
        if (gp == null) { s.Close(); yield break; }
        VwLog("emitter", ("path", v.GetPathTo(gp)), ("emitting", gp.Emitting), ("amount", gp.Amount), ("lifetime", gp.Lifetime), ("speed_scale", gp.SpeedScale), ("visible_in_tree", gp.IsVisibleInTree()), ("local_coords", gp.LocalCoords));
        // scan the lap: where is the locomotive, when is it nearest to the car
        v.ViewerSetPaused(true);
        double bestD = 1e9; float bestT = 0; var samples = new List<string>(); Vector3 prevPos = Vector3.Zero; float maxMove = 0;
        for (float t = 0; t < dur; t += 1f)
        {
            v.ViewerSeek(t);
            var p = gp.GlobalPosition; double d = p.DistanceTo(v._car.Pos);
            if (t > 0) maxMove = Math.Max(maxMove, p.DistanceTo(prevPos));
            prevPos = p;
            if (d < bestD) { bestD = d; bestT = t; }
            if ((int)t % 10 == 0) samples.Add($"{t:0}s:{d:0}m");
        }
        VwLog("scan", ("track", track), ("nearest_clock", bestT), ("nearest_m", bestD), ("max_move_per_s", maxMove), ("samples", string.Join(" ", samples)));
        // play to the nearest clock for 3.2 s (the smoke needs its lifetime to exist), then look at the exhaust with a camera riding on the locomotive
        float start = Math.Max(0f, bestT - 3.2f);
        v.ViewerSeek(start);
        var loco = gp.GetParent<Node3D>();
        var cam = new Camera3D { Name = "VwTestCamera", Fov = 45f, Near = 0.1f, Far = 400f };
        loco.AddChild(cam);
        var pos = gp.Position + new Vector3(8f, 1.5f, 3f);
        cam.Transform = new Transform3D(Basis.Identity, pos).LookingAt(gp.Position + new Vector3(-1f, 1.2f, -2f), Vector3.Up);
        cam.MakeCurrent();
        v.ViewerSetPaused(false);
        int pl = 0; while (v.ViewerClock < bestT && pl++ < 60 * 8) yield return null;
        v.ViewerSetPaused(true);
        foreach (var _ in VwFrames(10)) yield return null;
        VwLog("aim", ("clock", v.ViewerClock), ("current_camera_is_test_camera", v.GetViewport().GetCamera3D() == cam), ("emitting", gp.Emitting), ("speed_scale_paused", gp.SpeedScale), ("emitter_world", gp.GlobalPosition), ("camera_world", cam.GlobalPosition),
            ("in_frustum", cam.IsPositionInFrustum(gp.GlobalPosition)), ("emitter_in_tree_visible", gp.IsVisibleInTree()));
        var a = VwGrab(v.GetViewport(), $"{track}-gpuparticle-with");
        VwLog("render", ("with", VwRenderInfo(a)));
        // frozen while paused
        var snapT = gp.GlobalPosition; var dsum = new List<string>(); bool same = true;
        foreach (int gap in new[] { 3, 5, 10, 30 })
        {
            foreach (var _ in VwFrames(gap)) yield return null;
            var b = VwGrab(v.GetViewport(), null);
            var (any, over8, mean, box) = VwDiffImg(a, b); dsum.Add($"+{gap}f any {any} over8 {over8}"); if (any != 0) same = false;
        }
        VwLog("paused-frames", ("diffs_vs_first_paused", string.Join(" | ", dsum)), ("identical", same), ("emitter_moved_m", gp.GlobalPosition.DistanceTo(snapT)), ("speed_scale", gp.SpeedScale));
        // without the emitter
        gp.Visible = false;
        foreach (var _ in VwFrames(3)) yield return null;
        var noGp = VwGrab(v.GetViewport(), $"{track}-gpuparticle-without");
        gp.Visible = true;
        foreach (var _ in VwFrames(3)) yield return null;
        var back = VwGrab(v.GetViewport(), null);
        var dW = VwDiffImg(a, noGp); var dB = VwDiffImg(a, back);
        VwLog("with-vs-without", ("differing", dW.any), ("over8", dW.over8), ("mean_abs", dW.mean), ("box", dW.box), ("restored_vs_with_differing", dB.any), ("without_render", VwRenderInfo(noGp)));
        // resume: the particles move again
        v.ViewerSetPaused(false);
        foreach (var _ in VwFrames(20)) yield return null;
        var res = VwGrab(v.GetViewport(), $"{track}-gpuparticle-resumed");
        var dR = VwDiffImg(a, res);
        VwLog("resumed", ("clock", v.ViewerClock), ("differing", dR.any), ("over8", dR.over8), ("emitter_moved_m", gp.GlobalPosition.DistanceTo(snapT)));
        // the same with the particles' own clock stopped but the world running is not tested here: the pause test above is the viewer's own pause
        VwCheck(dW.over8 > 0, $"{track}: the GPU emitter is drawn: hiding only it changes {dW.over8} pixels (over 8/255), {dW.any} pixels in all, box {dW.box}");
        VwCheck(same, $"{track}: paused frames with the GPU emitter in view are pixel-identical after 3/5/10/30 frames ({string.Join("; ", dsum)})");
        VwCheck(dR.over8 > 0 || dR.any > 0, $"{track}: resuming changes the frame ({dR.any} pixels, {dR.over8} over 8/255)");
        cam.QueueFree();
        s.Close();
        int g = 0; while (s.State != ReplayViewerState.Closed && g++ < 600) yield return null;
    }
}
