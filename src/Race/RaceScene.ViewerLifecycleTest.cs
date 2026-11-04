using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using CCCPRacing.Replay;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// Replay viewer lifecycle scenarios (flag --viewer-lifecycle=NAME in a child game; PhysicsTest.ViewerLifecycle.cs judges the VIEWERLIFECYCLE lines).
/// The script runs in a ViewerFreezeDriver beside the scene (ProcessMode Always) because the live scene is Disabled while the viewer is open.
/// Arguments: --vl-fx=DIR (fixtures: DIR/TRACK is a retained-laps folder), --vl-lap-track=TRACK (default: the live track), --vl-cycles=N, --vl-purge,
/// --vl-roots=DIR1;DIR2 (the trees the writes scenario hashes). Line format: "VIEWERLIFECYCLE: tag|key=value|key=value".
/// </summary>
public partial class RaceScene
{
    string _vlMode; bool _vlParsed; ViewerFreezeDriver _vlDriver;

    static void VlLog(string tag, params (string k, object v)[] kv)
    {
        var sb = new System.Text.StringBuilder("VIEWERLIFECYCLE: ").Append(tag);
        foreach (var (k, v) in kv) sb.Append('|').Append(k).Append('=').Append(v is double d ? d.ToString("0.###", CultureInfo.InvariantCulture) : v is float f ? f.ToString("0.###", CultureInfo.InvariantCulture) : (v?.ToString() ?? "null").Replace('|', '/').Replace('\n', ' '));
        GD.Print(sb.ToString());
    }

    static string VlArg(string name) => OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith(name + "="))?[(name.Length + 1)..];
    static bool VlFlag(string name) => OS.GetCmdlineUserArgs().Contains(name);

    void ViewerLifecycleTick(float dt)
    {
        if (!_vlParsed)
        {
            _vlParsed = true;
            if (InstructionTrack != null || ViewerMode) return;
            _vlMode = VlArg("--viewer-lifecycle");
        }
        if (_vlMode == null || _vlDriver != null || _state != State.Racing) return;
        _vlDriver = new ViewerFreezeDriver { Name = "ViewerLifecycleDriver", ProcessMode = ProcessModeEnum.Always };
        GetParent().CallDeferred(Node.MethodName.AddChild, _vlDriver);
        IEnumerable script = _vlMode switch
        {
            "cycles" => VlCycles(), "alternate" => VlAlternate(), "cancel" => VlCancel(), "fail" => VlFail(), "rapid" => VlRapid(), "audio" => VlAudio(), "writes" => VlWrites(), "stall" => VlStall(),
            "quit-open" => VlQuit("open"), "quit-building" => VlQuit("building"), "quit-closing" => VlQuit("closing"), "quit-control" => VlQuit("control"),
            _ => VlBad("unknown scenario " + _vlMode),
        };
        _vlDriver.Start(script);
        _vlDriver.OnDone = () => { VlLog("script-done"); GetTree().Quit(); };
    }

    /// <summary>The count out of SpikeStaticValue's "n{count} dead.. first.." text.</summary>
    static int VlCount(string v) { var m = System.Text.RegularExpressions.Regex.Match(v ?? "", @"^n(\d+)"); return m.Success ? int.Parse(m.Groups[1].Value) : -1; }

    static IEnumerable VlBad(string why) { VlLog("fatal", ("why", why)); yield break; }

    // ------------------------------------------------------------------ helpers

    /// <summary>The first openable lap of the fixture folder of a track, or null.</summary>
    static LapSource VlLap(string track)
    {
        string fx = VlArg("--vl-fx");
        if (fx == null) return null;
        var box = LapArchive.For(Path.Combine(fx, track));
        var info = box.List().FirstOrDefault(i => i.Openable);
        return info == null ? null : box.Open(info.Id);
    }

    sealed record VlStat(long Obj, long Nodes, long Orph, long Res, int Buses, int Threads, double Rss, double Hwm);

    static VlStat VlNow()
    {
        var (rss, hwm) = SpikeMem();
        return new VlStat((long)Performance.GetMonitor(Performance.Monitor.ObjectCount), (long)Performance.GetMonitor(Performance.Monitor.ObjectNodeCount),
            (long)Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount), (long)Performance.GetMonitor(Performance.Monitor.ObjectResourceCount),
            AudioServer.BusCount, SpikeCount("/proc/self/task"), rss, hwm);
    }

    static (string, object)[] VlKv(VlStat s) => new (string, object)[] { ("obj", s.Obj), ("nodes", s.Nodes), ("orph", s.Orph), ("res", s.Res), ("buses", s.Buses), ("threads", s.Threads), ("rss", s.Rss), ("hwm", s.Hwm) };

    /// <summary>Collect, let the engine free what is queued (3 frames), then measure.</summary>
    static IEnumerable VlSettle(Action<VlStat> got, bool trim = false)
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        for (int i = 0; i < 3; i++) yield return null;
        if (trim) SpikeMallocTrim(0);
        got(VlNow());
    }

    /// <summary>The live scene as the viewer must give it back: modes, hud, audio players, the shared statics. A dictionary so a difference names its key.</summary>
    Dictionary<string, string> VlLive()
    {
        var d = new Dictionary<string, string>
        {
            ["live.ProcessMode"] = ProcessMode.ToString(), ["live.Visible"] = Visible.ToString(), ["live.hud"] = ((CanvasLayer)_hud.GetParent()).Visible.ToString(),
            ["live.state"] = _state.ToString(), ["live.run"] = _run.ToString(), ["live.trackId"] = _trackId, ["engine.timescale"] = Engine.TimeScale.ToString(CultureInfo.InvariantCulture),
            ["input.mouse"] = Input.MouseMode.ToString(), ["buses"] = AudioServer.BusCount.ToString(),
        };
        for (int i = 0; i < AudioServer.BusCount; i++) d["bus." + AudioServer.GetBusName(i)] = $"{AudioServer.GetBusVolumeDb(i):0.###}/{AudioServer.IsBusMute(i)}/{AudioServer.GetBusEffectCount(i)}";
        foreach (var (t, m) in SpikeStatics) d[$"static.{t}.{m}"] = SpikeStaticValue(t, m);
        foreach (var (k, v) in VlPlayers()) d["player." + k] = v;
        return d;
    }

    /// <summary>Every audio player of the live scene: path (with an index for twins) -> "paused/playing".</summary>
    Dictionary<string, string> VlPlayers()
    {
        var d = new Dictionary<string, string>();
        foreach (var n in FindChildren("*", "", true, false))
        {
            bool paused, playing;
            if (n is AudioStreamPlayer a) { paused = a.StreamPaused; playing = a.Playing; }
            else if (n is AudioStreamPlayer3D a3) { paused = a3.StreamPaused; playing = a3.Playing; }
            else continue;
            string key = GetPathTo(n).ToString(); int i = 1; while (d.ContainsKey(key + (i > 1 ? "#" + i : ""))) i++;
            d[key + (i > 1 ? "#" + i : "")] = $"{paused}/{playing}";
        }
        return d;
    }

    static List<string> VlDiff(Dictionary<string, string> a, Dictionary<string, string> b, Func<string, bool> only = null)
    {
        var l = new List<string>();
        foreach (var (k, v) in a)
        {
            if (only != null && !only(k)) continue;
            if (!b.TryGetValue(k, out var w)) l.Add($"{k}: {v} -> (gone)");
            else if (w != v) l.Add($"{k}: {v} -> {w}");
        }
        foreach (var (k, w) in b) if ((only == null || only(k)) && !a.ContainsKey(k)) l.Add($"{k}: (none) -> {w}");
        return l;
    }

    /// <summary>The live-scene check line: the differences between two VlLive() snapshots, split into the audio players' paused flags, the engine-owned playing flags and the rest.</summary>
    void VlLiveCompare(string tag, Dictionary<string, string> before, Dictionary<string, string> after, params (string, object)[] extra)
    {
        // Per-frame or log-only statics the live scene itself rewrites the moment it runs again (the car position the fade/see-through shaders follow, the tape scene build counter): reported as noise, not judged.
        static bool Noise(string k) => k is "static.SeeThrough.Car" or "static.FadeUnder.Car" or "static.RaceScene._tapeScenes";
        var noiseDiffs = VlDiff(before, after, Noise);
        before = before.Where(kv => !Noise(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value); after = after.Where(kv => !Noise(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
        var all = VlDiff(before, after);
        var pausedOnly = VlDiff(before.ToDictionary(kv => kv.Key, kv => kv.Key.StartsWith("player.") ? kv.Value.Split('/')[0] : kv.Value), after.ToDictionary(kv => kv.Key, kv => kv.Key.StartsWith("player.") ? kv.Value.Split('/')[0] : kv.Value));
        var rest = pausedOnly.Where(x => !x.StartsWith("player.")).ToList();
        var paused = pausedOnly.Where(x => x.StartsWith("player.")).ToList();
        var playing = all.Where(x => x.StartsWith("player.")).Except(paused).Count();
        var kv2 = new List<(string, object)> { ("diffs_total", all.Count), ("diffs_nonaudio", rest.Count), ("diffs_paused", paused.Count), ("diffs_playingonly", playing),
            ("noise", string.Join(" ;; ", noiseDiffs)), ("nonaudio", string.Join(" ;; ", rest.Take(12))), ("paused", string.Join(" ;; ", paused.Take(8))) };
        kv2.AddRange(extra);
        VlLog(tag, kv2.ToArray());
    }

    /// <summary>Wait for a session to leave Building (Playing, Closing or Closed), at most a number of frames; false on the cap.</summary>
    static IEnumerable VlWaitBuilt(ReplayViewerSession s, Action<int> frames, int cap = 60 * 240)
    {
        int n = 0;
        while (s.State == ReplayViewerState.Building && n < cap) { n++; yield return null; }
        frames(n);
    }

    static IEnumerable VlWaitClosed(ReplayViewerSession s, Action<int> frames, int cap = 600)
    {
        int n = 0;
        while (s.State != ReplayViewerState.Closed && n < cap) { n++; yield return null; }
        frames(n);
    }

    static IEnumerable VlFrames(int n) { for (int i = 0; i < n; i++) yield return null; }

    /// <summary>Snapshot of the live DayNight's collections, and the way back: the workaround for the nested world's NodeAdded leak documented in RaceScene.ViewerSpike.cs (the live lists are put back as they were).</summary>
    Action VlDayNightSnapshot()
    {
        var u = SpikeField(_dayNight, "_unlit") as IDictionary; var s = SpikeField(_dayNight, "_sceneryLights") as IList;
        var ue = new List<DictionaryEntry>(); if (u != null) { var en = u.GetEnumerator(); while (en.MoveNext()) ue.Add(new DictionaryEntry(en.Key, en.Value)); } var se = s?.Cast<object>().ToList();
        return () =>
        {
            if (u != null) { u.Clear(); foreach (var e in ue) u[e.Key] = e.Value; }
            if (s != null && se != null) { s.Clear(); foreach (var o in se) s.Add(o); }
        };
    }

    static string VlOrphans(int max = 12)
    {
        var ids = Node.GetOrphanNodeIds();
        return string.Join(",", ids.Take(max).Select(id => GodotObject.InstanceFromId((ulong)id) is Node n ? $"{n.GetType().Name}:{n.Name}(+{n.GetChildCount()})" : "#" + id));
    }

    // ------------------------------------------------------------------ 1: open / seek / loop / close cycles

    IEnumerable VlCycles()
    {
        string lt = VlArg("--vl-lap-track") ?? _trackId; int n = int.Parse(VlArg("--vl-cycles") ?? "10"); bool purge = VlFlag("--vl-purge");
        var lap = VlLap(lt);
        if (lap == null) { VlLog("fatal", ("why", "no fixture lap for " + lt)); yield break; }
        VlLog("start", ("live", _trackId), ("lap_track", lt), ("cycles", n), ("purge", purge), ("lap_seconds", lap.Meta.LapSeconds), ("tape", lap.HasTape));
        foreach (var _ in VlFrames(120)) yield return null;
        VlStat s0 = null;
        foreach (var _ in VlSettle(s => s0 = s, true)) yield return null;
        VlLog("cycle", ("n", 0), ("phase", "baseline"), ("obj", s0.Obj), ("nodes", s0.Nodes), ("orph", s0.Orph), ("res", s0.Res), ("buses", s0.Buses), ("threads", s0.Threads), ("rss", s0.Rss), ("hwm", s0.Hwm));
        var live0 = VlLive();
        for (int c = 1; c <= n; c++)
        {
            Action restoreDn = purge ? VlDayNightSnapshot() : null;
            var liveBefore = VlLive();
            var session = OpenReplayViewer(lap, out string err);
            if (session == null) { VlLog("cycle", ("n", c), ("fail", "open: " + err)); break; }
            int built = 0; var t0 = Time.GetTicksMsec();
            foreach (var _ in VlWaitBuilt(session, f => built = f)) yield return null;
            if (session.State != ReplayViewerState.Playing) { VlLog("cycle", ("n", c), ("fail", $"state {session.State} after {built} frames, error {session.Error}")); session.Close(); break; }
            ulong buildMs = Time.GetTicksMsec() - t0;
            var v = session.Scene;
            v.ViewerLoop = true;      // (the player-facing session plays a lap once; this cycle test exercises the loop/wrap path, still a supported mode, and its cleanup)
            foreach (var _ in VlFrames(20)) yield return null;
            v.ViewerSeek(v.ViewerDuration * 0.5f); foreach (var _ in VlFrames(10)) yield return null;
            v.ViewerSetPaused(true); foreach (var _ in VlFrames(5)) yield return null; v.ViewerSetPaused(false);
            v.ViewerSeek(Mathf.Max(0f, v.ViewerDuration - 0.4f));
            float prev = v.ViewerClock; bool wrapped = false;
            for (int i = 0; i < 90; i++) { yield return null; if (v.ViewerClock < prev - 0.2f) wrapped = true; prev = v.ViewerClock; }
            var open = VlNow();
            CloseReplayViewer();
            int closedFrames = 0;
            foreach (var _ in VlWaitClosed(session, f => closedFrames = f)) yield return null;
            restoreDn?.Invoke();
            VlInjectLeak();
            VlStat s = null;
            foreach (var _ in VlSettle(x => s = x, true)) yield return null;
            var liveAfter = VlLive();
            var diff = VlDiff(liveBefore, liveAfter, k => !k.StartsWith("player.") && !(k is "static.SeeThrough.Car" or "static.FadeUnder.Car" or "static.RaceScene._tapeScenes" or "static.Props.Materials" or "static.Props.BatchCache"));
            var cacheDiff = VlDiff(liveBefore, liveAfter, k => k is "static.Props.Materials" or "static.Props.BatchCache");      // (the shared prop caches fill once per new material: warm-up, reported not judged)
            VlLog("cycle", ("n", c), ("phase", "closed"), ("state", session.State), ("build_frames", built), ("build_ms", buildMs), ("wrapped", wrapped), ("loop_clock", prev), ("closed_frames", closedFrames),
                ("open_rss", open.Rss), ("open_hwm", open.Hwm), ("obj", s.Obj), ("nodes", s.Nodes), ("orph", s.Orph), ("res", s.Res), ("buses", s.Buses), ("threads", s.Threads), ("rss", s.Rss), ("hwm", s.Hwm),
                ("dn_unlit", SpikeField(_dayNight, "_unlit") is ICollection a ? a.Count : -1), ("dn_scenery", SpikeField(_dayNight, "_sceneryLights") is ICollection b ? b.Count : -1),
                ("cache_diffs", string.Join(" ;; ", cacheDiff)), ("props_materials", VlCount(SpikeStaticValue("Props", "Materials"))), ("props_batch", VlCount(SpikeStaticValue("Props", "BatchCache"))), ("live_diffs", diff.Count), ("live_diff_names", string.Join(" ;; ", diff.Take(6))), ("active", ActiveReplayViewers.Count), ("viewer_ref", ReplayViewer == null ? "null" : "set"));
        }
        VlLog("cycles-end");
    }

    // The negative control (gate R): --vl-inject-leak=N keeps N fresh engine Resources alive per cycle in a static list, a real, retained leak of exactly N objects (and resources) per cycle,
    // so the criterion that judges the cycles can be shown to reject it. Released at the end of the script.
    static readonly List<GodotObject> VlLeakSink = new();
    static void VlInjectLeak() { int k = int.Parse(VlArg("--vl-inject-leak") ?? "0"); for (int i = 0; i < k; i++) VlLeakSink.Add(new Godot.Resource()); }

    // ------------------------------------------------------------------ 1b: alternating tracks (gate R)

    /// <summary>Open/play/seek/loop/close over ALTERNATING tracks: --vl-lap-tracks=a,b,c,d, --vl-rounds=N (every round visits every track once). One "cycle" line per open, with the lap track, so the judge can follow each track's series across the rounds.</summary>
    IEnumerable VlAlternate()
    {
        var tracks = (VlArg("--vl-lap-tracks") ?? "ilyich,siberia,atomgrad,bonus").Split(','); int rounds = int.Parse(VlArg("--vl-rounds") ?? "3");
        var laps = new Dictionary<string, LapSource>();
        foreach (var t in tracks) { var l = VlLap(t); if (l == null) { VlLog("fatal", ("why", "no fixture lap for " + t)); yield break; } laps[t] = l; }
        VlLog("start", ("live", _trackId), ("tracks", string.Join(",", tracks)), ("rounds", rounds));
        foreach (var _ in VlFrames(120)) yield return null;
        VlStat s0 = null;
        foreach (var _ in VlSettle(s => s0 = s, true)) yield return null;
        VlLog("cycle", ("n", 0), ("phase", "baseline"), ("obj", s0.Obj), ("nodes", s0.Nodes), ("orph", s0.Orph), ("res", s0.Res), ("threads", s0.Threads), ("rss", s0.Rss), ("hwm", s0.Hwm));
        int n = 0;
        for (int r = 1; r <= rounds; r++)
            foreach (var t in tracks)
            {
                n++;
                var lap = laps[t];
                var session = OpenReplayViewer(lap, out string err);
                if (session == null) { VlLog("cycle", ("n", n), ("round", r), ("lap_track", t), ("fail", "open: " + err)); yield break; }
                int built = 0; var t0 = Time.GetTicksMsec();
                foreach (var _ in VlWaitBuilt(session, f => built = f)) yield return null;
                if (session.State != ReplayViewerState.Playing) { VlLog("cycle", ("n", n), ("round", r), ("lap_track", t), ("fail", $"state {session.State}, error {session.Error}")); session.Close(); yield break; }
                ulong buildMs = Time.GetTicksMsec() - t0;
                var v = session.Scene; v.ViewerLoop = true;
                foreach (var _ in VlFrames(20)) yield return null;
                v.ViewerSeek(v.ViewerDuration * 0.5f); foreach (var _ in VlFrames(10)) yield return null;
                v.ViewerSeek(Mathf.Max(0f, v.ViewerDuration - 0.4f));
                float prev = v.ViewerClock; bool wrapped = false;
                for (int i = 0; i < 90; i++) { yield return null; if (v.ViewerClock < prev - 0.2f) wrapped = true; prev = v.ViewerClock; }
                var open = VlNow(); ulong tc = Time.GetTicksMsec();
                CloseReplayViewer();
                int closedFrames = 0;
                foreach (var _ in VlWaitClosed(session, f => closedFrames = f)) yield return null;
                ulong closeMs = Time.GetTicksMsec() - tc;
                VlInjectLeak();
                VlStat raw = VlNow(); VlStat st = null;
                foreach (var _ in VlSettle(x => st = x, true)) yield return null;
                VlLog("cycle", ("n", n), ("round", r), ("lap_track", t), ("phase", "closed"), ("state", session.State), ("build_frames", built), ("build_ms", buildMs), ("close_ms", closeMs), ("wrapped", wrapped),
                    ("open_obj", open.Obj), ("open_res", open.Res), ("open_rss", open.Rss), ("raw_obj", raw.Obj), ("raw_res", raw.Res), ("obj", st.Obj), ("nodes", st.Nodes), ("orph", st.Orph), ("res", st.Res), ("threads", st.Threads), ("rss", st.Rss), ("hwm", st.Hwm),
                    ("gc_heap", GC.GetTotalMemory(false)));
            }
        foreach (var o in VlLeakSink) if (IsInstanceValid(o) && o is not RefCounted) o.Free();
        VlLeakSink.Clear();
        VlLog("cycles-end");
    }

    // ------------------------------------------------------------------ 2: cancel during the sliced build

    IEnumerable VlCancel()
    {
        string lt = VlArg("--vl-lap-track") ?? _trackId;
        var lap = VlLap(lt);
        if (lap == null) { VlLog("fatal", ("why", "no fixture lap for " + lt)); yield break; }
        foreach (var _ in VlFrames(120)) yield return null;
        // a complete build first: the total number of build steps and the baseline after a full open/close
        var s0 = OpenReplayViewer(lap, out string err);
        if (s0 == null) { VlLog("fatal", ("why", "open " + err)); yield break; }
        foreach (var _ in VlWaitBuilt(s0, f => { })) yield return null;
        int total = s0.Scene.ViewerBuildSteps; var st = s0.State;
        CloseReplayViewer();
        foreach (var _ in VlWaitClosed(s0, f => { })) yield return null;
        VlStat bl = null; foreach (var _ in VlSettle(x => bl = x, true)) yield return null;
        VlLog("full", ("lap_track", lt), ("state", st), ("steps", total), ("orph", bl.Orph), ("nodes", bl.Nodes), ("obj", bl.Obj), ("res", bl.Res));
        var points = new (string name, int steps)[] { ("zero", 0), ("early", 2), ("mid", Math.Max(3, total / 2)), ("late", Math.Max(3, total * 3 / 4)) };
        foreach (var (name, steps) in points)
        {
            for (int attempt = 1; attempt <= 4; attempt++)
            {
                VlStat pre = null; foreach (var _ in VlSettle(x => pre = x, true)) yield return null;      // this point's own baseline: earlier points' leaks do not count against it
                var live0 = VlLive();
                string exc = null;
                ReplayViewerSession s = null;
                try { s = OpenReplayViewer(lap, out err); } catch (Exception e) { exc = e.GetType().Name + ": " + e.Message; }
                if (s == null) { VlLog("cancel", ("point", name), ("fail", "open " + (exc ?? err))); break; }
                int guard = 0;
                while (s.State == ReplayViewerState.Building && (s.Scene?.ViewerBuildSteps ?? 0) < steps && guard++ < 60 * 240) yield return null;
                var stateAt = s.State; int stepsAt = s.Scene?.ViewerBuildSteps ?? -1;
                try { CloseReplayViewer(); } catch (Exception e) { exc = e.GetType().Name + ": " + e.Message; }
                int closedFrames = 0;
                foreach (var _ in VlWaitClosed(s, f => closedFrames = f)) yield return null;
                var liveNow = VlLive();
                VlStat after = null; foreach (var _ in VlSettle(x => after = x, true)) yield return null;
                bool exercised = stateAt == ReplayViewerState.Building;
                if (after.Orph > pre.Orph) { VlLog("orphan-dump-begin", ("point", name), ("attempt", attempt), ("new_orphans", after.Orph - pre.Orph), ("total_orphans", after.Orph)); Node.PrintOrphanNodes(); VlLog("orphan-dump-end"); }
                VlLiveCompare("cancel-live", live0, liveNow, ("point", name), ("attempt", attempt));
                VlLog("cancel", ("point", name), ("attempt", attempt), ("exercised", exercised), ("wanted_steps", steps), ("steps_at_cancel", stepsAt), ("state_at_cancel", stateAt), ("state_after", s.State), ("closed_frames", closedFrames), ("exception", exc ?? "none"),
                    ("active", ActiveReplayViewers.Count), ("viewer_ref", ReplayViewer == null ? "null" : "set"), ("obj", after.Obj), ("nodes", after.Nodes), ("orph", after.Orph), ("base_orph", pre.Orph), ("base_nodes", pre.Nodes), ("base_obj", pre.Obj),
                    ("mode_ok", ProcessMode.ToString() == live0["live.ProcessMode"]), ("visible", Visible), ("orphans", after.Orph > pre.Orph ? VlOrphans(3) : ""));
                if (exercised) break;      // (the build finished before the cancel: try the point again)
            }
        }
        VlLog("cancel-end");
    }

    // ------------------------------------------------------------------ 3: failure paths

    IEnumerable VlFail()
    {
        string lt = VlArg("--vl-lap-track") ?? _trackId;
        var good = VlLap(lt);
        if (good == null) { VlLog("fatal", ("why", "no fixture lap for " + lt)); yield break; }
        string other = VlArg("--vl-other-track");
        var otherLap = other != null ? VlLap(other) : null;
        foreach (var _ in VlFrames(120)) yield return null;
        byte[] ghost = good.GhostBytes.ToArray(); byte[] tape = good.TapeBytes.ToArray(); var meta = good.Meta;
        byte[] Flip(byte[] b, int at) { var c = (byte[])b.Clone(); c[at] ^= 0xFF; return c; }
        byte[] Cut(byte[] b, int len) => b.Take(len).ToArray();
        byte[] Version(byte[] b) { var c = (byte[])b.Clone(); BitConverter.GetBytes(WorldTapeConst.FormatVersion - 1).CopyTo(c, 4); return c; }
        var rnd = new Random(7); byte[] garbage = new byte[4096]; rnd.NextBytes(garbage);
        // (name, source, expect: "null" the open is refused, "ghost" opens and plays ghost only, "any" either, but cleanly)
        var cases = new List<(string name, Func<LapSource> make, string expect)>
        {
            ("null-lap", () => null, "null"),
            ("ghost-empty", () => new LapSource(meta, Array.Empty<byte>(), null), "null"),
            ("ghost-null-bytes", () => new LapSource(meta, null, null), "null"),
            ("ghost-truncated-half", () => new LapSource(meta, Cut(ghost, ghost.Length / 2), tape), "null"),
            ("ghost-truncated-8", () => new LapSource(meta, Cut(ghost, 8), null), "null"),
            ("ghost-garbage", () => new LapSource(meta, garbage, null), "null"),
            ("ghost-flipped-middle", () => new LapSource(meta, Flip(ghost, ghost.Length / 2), tape), "any"),      // known limit: WorldTapeFingerprint hashes count, time, positions and rotations only, so a flip in another ghost channel is not detected (reported, not judged)
            ("ghost-flipped-every-13th", () => { var c = (byte[])ghost.Clone(); for (int i = 16; i < c.Length; i += 13) c[i] ^= 0xFF; return new LapSource(meta, c, tape); }, "null"),
            ("unknown-track", () => new LapSource(meta with { TrackId = "nosuchtrack" }, ghost, tape), "null"),
            ("track-signature-mismatch", () => new LapSource(meta with { TrackSignature = "0000deadbeef" }, ghost, tape), "null"),
            ("record-set-out-of-range", () => new LapSource(meta with { Set = 99 }, ghost, tape), "null"),
            ("meta-notape-but-bytes", () => new LapSource(meta with { HasTape = false }, ghost, tape), "any"),
            ("tape-missing-meta-says-tape", () => new LapSource(meta with { HasTape = true }, ghost, null), "ghost"),
            ("tape-truncated-half", () => new LapSource(meta, ghost, Cut(tape, tape.Length / 2)), "ghost"),
            ("tape-truncated-4", () => new LapSource(meta, ghost, Cut(tape, 4)), "ghost"),
            ("tape-garbage", () => new LapSource(meta, ghost, garbage), "ghost"),
            ("tape-legacy-version", () => new LapSource(meta, ghost, Version(tape)), "ghost"),
            ("tape-flipped-body", () => new LapSource(meta, ghost, Flip(tape, tape.Length * 3 / 4)), "ghost"),
        };
        if (otherLap?.HasTape == true) cases.Add(("tape-of-another-lap", () => new LapSource(meta, ghost, otherLap.TapeBytes.ToArray()), "ghost"));
        if (!good.HasTape) VlLog("note", ("why", "the fixture lap has no tape: tape cases are meaningless"));
        foreach (var (name, make, expect) in cases)
        {
            var live0 = VlLive();
            LapSource src = null; string exc = null; ReplayViewerSession s = null; string err = null;
            try { src = make(); s = OpenReplayViewer(src, out err); } catch (Exception e) { exc = e.GetType().Name + ": " + e.Message; }
            string outcome; bool tapeOn = false; string verr = null; int builtFrames = 0; string stateAfterWait = "";
            if (exc != null) outcome = "EXCEPTION";
            else if (s == null) outcome = "null";
            else
            {
                foreach (var _ in VlWaitBuilt(s, f => builtFrames = f)) yield return null;
                stateAfterWait = s.State.ToString();
                if (s.State == ReplayViewerState.Playing) { outcome = s.Scene.ViewerHasTape ? "plays-with-tape" : "plays-ghost-only"; tapeOn = s.Scene.ViewerHasTape; for (int i = 0; i < 20; i++) yield return null; }
                else outcome = "opened-then-" + s.State;
                verr = s.Error;
                CloseReplayViewer();
                foreach (var _ in VlWaitClosed(s, f => { })) yield return null;
            }
            foreach (var _ in VlFrames(3)) yield return null;
            var liveNow = VlLive();
            VlLiveCompare("fail-live", live0, liveNow, ("case", name));
            VlLog("fail", ("case", name), ("expect", expect), ("outcome", outcome), ("reason", err ?? verr ?? exc ?? ""), ("tape_on", tapeOn), ("frames_until_not_building", builtFrames), ("state_then", stateAfterWait), ("active", ActiveReplayViewers.Count), ("viewer_ref", ReplayViewer == null ? "null" : "set"));
        }
        // open while already open
        {
            var live0 = VlLive();
            var a = OpenReplayViewer(good, out string e1);
            var b = OpenReplayViewer(good, out string e2);
            var c2 = OpenReplayViewer(good, out string e3);
            foreach (var _ in VlWaitBuilt(a, f => { })) yield return null;
            bool aPlays = a.State == ReplayViewerState.Playing;
            CloseReplayViewer();
            foreach (var _ in VlWaitClosed(a, f => { })) yield return null;
            foreach (var _ in VlFrames(3)) yield return null;
            VlLiveCompare("fail-live", live0, VlLive(), ("case", "open-while-open"));
            VlLog("fail", ("case", "open-while-open"), ("first_ok", a != null), ("second_null", b == null), ("second_reason", e2), ("third_null", c2 == null), ("third_reason", e3), ("first_plays", aPlays), ("active", ActiveReplayViewers.Count), ("viewer_ref", ReplayViewer == null ? "null" : "set"));
        }
        // open while the live scene is booting (the flag the owner checks)
        {
            var live0 = VlLive();
            _booting = true;
            ReplayViewerSession s = null; string err = null; string exc = null;
            try { s = OpenReplayViewer(good, out err); } catch (Exception e) { exc = e.Message; }
            _booting = false;
            foreach (var _ in VlFrames(3)) yield return null;
            VlLiveCompare("fail-live", live0, VlLive(), ("case", "open-while-booting"));
            VlLog("fail", ("case", "open-while-booting"), ("null", s == null), ("reason", err ?? exc), ("active", ActiveReplayViewers.Count));
            if (s != null) { CloseReplayViewer(); foreach (var _ in VlWaitClosed(s, f => { })) yield return null; }
        }
        VlLog("fail-end");
    }

    // ------------------------------------------------------------------ 4: rapid close, open after close

    IEnumerable VlRapid()
    {
        string lt = VlArg("--vl-lap-track") ?? _trackId;
        var lap = VlLap(lt);
        if (lap == null) { VlLog("fatal", ("why", "no fixture lap for " + lt)); yield break; }
        foreach (var _ in VlFrames(120)) yield return null;
        VlStat bl = null; foreach (var _ in VlSettle(x => bl = x, true)) yield return null;
        VlLog("rapid-base", ("orph", bl.Orph), ("nodes", bl.Nodes), ("obj", bl.Obj));

        long prevOrph = bl.Orph, prevNodes = bl.Nodes;
        IEnumerable Check(string name, ReplayViewerSession s, int restored, Dictionary<string, string> live0, string extra = "")
        {
            foreach (var _ in VlFrames(3)) yield return null;
            VlStat after = null; foreach (var _ in VlSettle(x => after = x, true)) yield return null;
            VlLiveCompare("rapid-live", live0, VlLive(), ("case", name));
            VlLog("rapid", ("case", name), ("state", s.State), ("restored_events", restored), ("active", ActiveReplayViewers.Count), ("viewer_ref", ReplayViewer == null ? "null" : "set"), ("orph", after.Orph), ("base_orph", prevOrph), ("start_orph", bl.Orph),
                ("nodes", after.Nodes), ("base_nodes", prevNodes), ("obj", after.Obj), ("base_obj", bl.Obj), ("extra", extra));
            prevOrph = after.Orph; prevNodes = after.Nodes;
        }

        // a) close x5 in one frame while playing
        {
            var live0 = VlLive(); int restored = 0;
            var s = OpenReplayViewer(lap, out string err);
            if (s == null) { VlLog("fatal", ("why", err)); yield break; }
            s.Restored += () => restored++;
            foreach (var _ in VlWaitBuilt(s, f => { })) yield return null;
            string exc = "none";
            try { for (int i = 0; i < 5; i++) CloseReplayViewer(); s.Close(); } catch (Exception e) { exc = e.GetType().Name + ": " + e.Message; }
            foreach (var _ in VlWaitClosed(s, f => { })) yield return null;
            foreach (var _ in Check("close-x5-playing", s, restored, live0, "exc=" + exc)) yield return null;
        }
        // b) close in the same frame as open
        {
            var live0 = VlLive(); int restored = 0; string exc = "none";
            var s = OpenReplayViewer(lap, out string err);
            if (s == null) { VlLog("fatal", ("why", err)); yield break; }
            s.Restored += () => restored++;
            try { CloseReplayViewer(); } catch (Exception e) { exc = e.GetType().Name + ": " + e.Message; }
            foreach (var _ in VlWaitClosed(s, f => { })) yield return null;
            foreach (var _ in Check("close-same-frame-as-open", s, restored, live0, "exc=" + exc)) yield return null;
        }
        // c) close x5 on five consecutive frames while building
        {
            var live0 = VlLive(); int restored = 0;
            var s = OpenReplayViewer(lap, out string err);
            if (s == null) { VlLog("fatal", ("why", err)); yield break; }
            s.Restored += () => restored++;
            for (int i = 0; i < 5; i++) { CloseReplayViewer(); yield return null; }
            foreach (var _ in VlWaitClosed(s, f => { })) yield return null;
            foreach (var _ in Check("close-x5-over-frames-building", s, restored, live0)) yield return null;
        }
        // d) open in the very frame the session reports Closed, play, close
        for (int round = 1; round <= 3; round++)
        {
            var live0 = VlLive(); int restored = 0;
            var s = OpenReplayViewer(lap, out string err);
            if (s == null) { VlLog("fatal", ("why", err)); yield break; }
            s.Restored += () => restored++;
            foreach (var _ in VlFrames(round * 3)) yield return null;      // (round 1-3: closed at a different phase of the build)
            CloseReplayViewer();
            foreach (var _ in VlWaitClosed(s, f => { })) yield return null;
            int liveAfter = restored;
            var s2 = OpenReplayViewer(lap, out string err2);      // (in the frame that saw Closed)
            string st = "second open " + (s2 == null ? "REFUSED: " + err2 : "ok");
            if (s2 != null)
            {
                int r2 = 0; s2.Restored += () => r2++;
                foreach (var _ in VlWaitBuilt(s2, f => { })) yield return null;
                st += ", state " + s2.State;
                foreach (var _ in VlFrames(30)) yield return null;
                CloseReplayViewer();
                foreach (var _ in VlWaitClosed(s2, f => { })) yield return null;
                st += $", restored events {r2}";
            }
            foreach (var _ in Check("open-right-after-closed-r" + round, s, liveAfter, live0, st)) yield return null;
        }
        // e) close calls long after closed
        {
            var live0 = VlLive(); int restored = 0;
            var s = OpenReplayViewer(lap, out string err);
            s.Restored += () => restored++;
            foreach (var _ in VlWaitBuilt(s, f => { })) yield return null;
            CloseReplayViewer();
            foreach (var _ in VlWaitClosed(s, f => { })) yield return null;
            foreach (var _ in VlFrames(10)) yield return null;
            string exc = "none";
            try { CloseReplayViewer(); CloseReplayViewer(); s.Close(); } catch (Exception e) { exc = e.GetType().Name + ": " + e.Message; }
            foreach (var _ in Check("close-after-closed", s, restored, live0, "exc=" + exc)) yield return null;
        }
        VlLog("rapid-end");
    }

    // ------------------------------------------------------------------ 4b: quit with the viewer open / building / closing

    IEnumerable VlQuit(string when)
    {
        string lt = VlArg("--vl-lap-track") ?? _trackId;
        var lap = VlLap(lt);
        if (lap == null) { VlLog("fatal", ("why", "no fixture lap for " + lt)); yield break; }
        foreach (var _ in VlFrames(600)) yield return null;      // a partial lap is under way
        VlLog("quit-start", ("when", when), ("lap_clock", _lapClock), ("state", _state));
        ReplayViewerSession s = null;
        if (when != "control")
        {
            s = OpenReplayViewer(lap, out string err);
            if (s == null) { VlLog("fatal", ("why", err)); yield break; }
            if (when == "open") { foreach (var _ in VlWaitBuilt(s, f => { })) yield return null; foreach (var _ in VlFrames(30)) yield return null; }
            else if (when == "building") { int g = 0; while (s.State == ReplayViewerState.Building && s.Scene.ViewerBuildSteps < 6 && g++ < 60 * 120) yield return null; }
            else if (when == "closing") { foreach (var _ in VlWaitBuilt(s, f => { })) yield return null; CloseReplayViewer(); }
        }
        VlLog("quit-now", ("when", when), ("session_state", s?.State.ToString() ?? "none"), ("steps", s?.Scene?.ViewerBuildSteps ?? -1));
        GetTree().Quit();
        yield break;
    }

    // ------------------------------------------------------------------ 5: audio players

    IEnumerable VlAudio()
    {
        string lt = VlArg("--vl-lap-track") ?? _trackId;
        var lap = VlLap(lt);
        if (lap == null) { VlLog("fatal", ("why", "no fixture lap for " + lt)); yield break; }
        foreach (var _ in VlFrames(400)) yield return null;
        for (int round = 1; round <= 2; round++)
        {
            var before = VlPlayers();
            int pausedBefore = before.Values.Count(v => v.StartsWith("True")), playingBefore = before.Values.Count(v => v.EndsWith("True"));
            var s = OpenReplayViewer(lap, out string err);
            if (s == null) { VlLog("fatal", ("why", err)); yield break; }
            var during0 = VlPlayers();
            foreach (var _ in VlWaitBuilt(s, f => { })) yield return null;
            foreach (var _ in VlFrames(60)) yield return null;
            var during = VlPlayers();
            CloseReplayViewer();
            foreach (var _ in VlWaitClosed(s, f => { })) yield return null;
            var atClosed = VlPlayers();      // (the frame that saw Closed: the owner restored in its _Process of this frame)
            yield return null;
            var plus1 = VlPlayers();
            foreach (var _ in VlFrames(2)) yield return null;
            var plus3 = VlPlayers();
            foreach (var _ in VlFrames(60)) yield return null;
            var plus63 = VlPlayers();
            string Flag(Dictionary<string, string> d) => $"{d.Values.Count(v => v.StartsWith("True"))}p/{d.Count}";
            List<string> Mis(Dictionary<string, string> a) => before.Where(kv => a.TryGetValue(kv.Key, out var w) && w.Split('/')[0] != kv.Value.Split('/')[0]).Select(kv => $"{kv.Key} {kv.Value}->{a[kv.Key]}").ToList();
            VlLog("audio", ("round", round), ("live", _trackId), ("players_before", before.Count), ("paused_before", pausedBefore), ("playing_before", playingBefore),
                ("paused_after_suspend", Flag(during0)), ("paused_during", Flag(during)), ("closed_paused", Flag(atClosed)), ("closed_players", atClosed.Count),
                ("mismatch_closed", Mis(atClosed).Count), ("mismatch_plus1", Mis(plus1).Count), ("mismatch_plus3", Mis(plus3).Count), ("mismatch_plus63", Mis(plus63).Count),
                ("gone_closed", before.Keys.Count(k => !atClosed.ContainsKey(k))), ("new_closed", atClosed.Keys.Count(k => !before.ContainsKey(k))),
                ("sample_closed", string.Join(" ;; ", Mis(atClosed).Take(8))), ("sample_plus3", string.Join(" ;; ", Mis(plus3).Take(8))));
            foreach (var _ in VlFrames(30)) yield return null;
        }
        VlLog("audio-end");
    }

    // ------------------------------------------------------------------ 6: writes (file hashes of the data trees around one open/close)

    static Dictionary<string, string> VlHashTree(string root)
    {
        var d = new Dictionary<string, string>();
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return d;
        foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(root, f);
            if (rel.Split(Path.DirectorySeparatorChar).Contains("logs")) continue;      // (the engine's own log files)
            try { using var fs = new FileStream(f, FileMode.Open, System.IO.FileAccess.Read, FileShare.ReadWrite); d[rel] = fs.Length + ":" + Convert.ToHexString(SHA256.HashData(fs))[..16]; }
            catch (Exception e) { d[rel] = "unreadable:" + e.GetType().Name; }
        }
        return d;
    }

    IEnumerable VlWrites()
    {
        string lt = VlArg("--vl-lap-track") ?? _trackId;
        var lap = VlLap(lt);
        if (lap == null) { VlLog("fatal", ("why", "no fixture lap for " + lt)); yield break; }
        var roots = (VlArg("--vl-roots") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries);
        foreach (var _ in VlFrames(300)) yield return null;
        var before = roots.Select(VlHashTree).ToList();
        VlLog("writes-start", ("live", _trackId), ("lap_track", lt), ("files_before", string.Join(",", before.Select(b => b.Count))), ("data_dir", _records.Dir));
        var s = OpenReplayViewer(lap, out string err);
        if (s == null) { VlLog("fatal", ("why", err)); yield break; }
        foreach (var _ in VlWaitBuilt(s, f => { })) yield return null;
        foreach (var _ in VlFrames(120)) yield return null;
        var during = roots.Select(VlHashTree).ToList();
        s.Scene.ViewerSeek(5f); foreach (var _ in VlFrames(30)) yield return null;
        CloseReplayViewer();
        foreach (var _ in VlWaitClosed(s, f => { })) yield return null;
        var atClosed = roots.Select(VlHashTree).ToList();
        foreach (var _ in VlFrames(120)) yield return null;
        var later = roots.Select(VlHashTree).ToList();
        for (int i = 0; i < roots.Length; i++)
        {
            var dDuring = VlDiff(before[i], during[i]); var dClosed = VlDiff(before[i], atClosed[i]); var dLater = VlDiff(before[i], later[i]);
            VlLog("writes", ("root", roots[i]), ("files_before", before[i].Count), ("diff_during", dDuring.Count), ("diff_at_closed", dClosed.Count), ("diff_after_120_live_frames", dLater.Count),
                ("during", string.Join(" ;; ", dDuring.Take(10))), ("at_closed", string.Join(" ;; ", dClosed.Take(10))), ("later", string.Join(" ;; ", dLater.Take(10))));
        }
        VlLog("writes-end");
    }

    // ------------------------------------------------------------------ 7: stalls and memory (run WITHOUT --fixed-fps)

    IEnumerable VlStall()
    {
        string lt = VlArg("--vl-lap-track") ?? _trackId;
        var lap = VlLap(lt);
        if (lap == null) { VlLog("fatal", ("why", "no fixture lap for " + lt)); yield break; }
        foreach (var _ in VlFrames(240)) yield return null;
        // the live scene's own frames for context
        var live = new List<double>(); ulong last = Time.GetTicksUsec();
        for (int i = 0; i < 120; i++) { yield return null; ulong now = Time.GetTicksUsec(); live.Add((now - last) / 1000.0); last = now; }
        VlStat s0 = null; foreach (var _ in VlSettle(x => s0 = x, true)) yield return null;
        SpikeResetPeak();
        var (rss0, hwm0) = SpikeMem();
        ulong t0 = Time.GetTicksUsec();
        var session = OpenReplayViewer(lap, out string err);
        if (session == null) { VlLog("fatal", ("why", err)); yield break; }
        ulong tOpened = Time.GetTicksUsec();
        var build = new List<double>(); last = tOpened;
        int guard = 0;
        while (session.State == ReplayViewerState.Building && guard++ < 60 * 600) { yield return null; ulong now = Time.GetTicksUsec(); build.Add((now - last) / 1000.0); last = now; }
        double total = (Time.GetTicksUsec() - t0) / 1000.0;
        var play = new List<double>();
        if (session.State == ReplayViewerState.Playing)
        {
            for (int i = 0; i < 180; i++) { yield return null; ulong now = Time.GetTicksUsec(); play.Add((now - last) / 1000.0); last = now; }
        }
        var (rssOpen, hwmOpen) = SpikeMem();
        var live2 = VlNow();
        double Pct(List<double> l, double p) { if (l.Count == 0) return -1; var s = l.OrderBy(x => x).ToList(); return s[Math.Min(s.Count - 1, (int)Math.Ceiling(p * s.Count) - 1)]; }
        string Stats(List<double> l) => l.Count == 0 ? "none" : $"n{l.Count} max{l.Max():0.#} p99{Pct(l, .99):0.#} p50{Pct(l, .5):0.#}";
        VlLog("stall", ("live_track", _trackId), ("lap_track", lt), ("state", session.State), ("open_call_ms", (tOpened - t0) / 1000.0), ("build_total_ms", total), ("build_frames", build.Count), ("build_steps", session.Scene?.ViewerBuildSteps ?? -1),
            ("build_max_ms", build.Count > 0 ? build.Max() : -1), ("build_p99_ms", Pct(build, .99)), ("build_p50_ms", Pct(build, .5)), ("build_over100", build.Count(x => x > 100)), ("build_over250", build.Count(x => x > 250)),
            ("play_max_ms", play.Count > 0 ? play.Max() : -1), ("play_p99_ms", Pct(play, .99)), ("play_p50_ms", Pct(play, .5)), ("live_frames", Stats(live)),
            ("rss_base", rss0), ("hwm_base", hwm0), ("rss_open", rssOpen), ("hwm_open_peak", hwmOpen), ("obj_open", live2.Obj), ("nodes_open", live2.Nodes), ("threads_open", live2.Threads));
        VlLog("stall-top", ("build_frames_ms_top10", string.Join(",", build.OrderByDescending(x => x).Take(10).Select(x => x.ToString("0.#", CultureInfo.InvariantCulture)))));
        CloseReplayViewer();
        foreach (var _ in VlWaitClosed(session, f => { })) yield return null;
        VlStat s1 = null; foreach (var _ in VlSettle(x => s1 = x, true)) yield return null;
        VlLog("mem", ("track", lt), ("rss_base", s0.Rss), ("hwm_base", s0.Hwm), ("rss_open", rssOpen), ("hwm_peak", hwmOpen), ("rss_closed_trim", s1.Rss), ("hwm_closed", s1.Hwm), ("obj_base", s0.Obj), ("obj_closed", s1.Obj), ("nodes_base", s0.Nodes), ("nodes_closed", s1.Nodes), ("orph_base", s0.Orph), ("orph_closed", s1.Orph));
        // the same world built synchronously, as a nested instruction scene (the pre-viewer way): one AddChild
        {
            var vp = new SubViewport { Size = (Vector2I)GetViewport().GetVisibleRect().Size, OwnWorld3D = true, HandleInputLocally = false, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
            GetParent().AddChild(vp);
            var nested = new RaceScene { InstructionTrack = lt, _dataDir = _records.Dir, _noRecord = true, _padMode = _padMode };
            ProcessMode = ProcessModeEnum.Disabled;
            ulong a = Time.GetTicksUsec();
            vp.AddChild(nested);
            ulong b = Time.GetTicksUsec();
            yield return null; ulong c = Time.GetTicksUsec();
            VlLog("sync", ("track", lt), ("addchild_ms", (b - a) / 1000.0), ("first_frame_ms", (c - b) / 1000.0), ("booting_after", nested.Booting));
            vp.QueueFree();
            foreach (var _ in VlFrames(5)) yield return null;
            ProcessMode = ProcessModeEnum.Inherit;
        }
        VlLog("stall-end");
    }
}
