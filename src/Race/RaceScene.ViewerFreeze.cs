using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CCCPRacing.Audio;
using CCCPRacing.Track;
using CCCPRacing.UI;
using Godot;
using Snap = CCCPRacing.Race.ViewerFreezeSnapshot;

namespace CCCPRacing.Race;

/// <summary>
/// Phase 2 spike 2: proof (or disproof) that ProcessMode.Disabled plus an explicit audio pause freezes the live single-player race, and an audit of what a nested
/// world touches (the DayNight NodeAdded hook, DachaPatrol's tree-wide lookup, statics, the nested scene's _ExitTree). Spike code only; owner: spike agent 2.
/// Child-game flags (read here, never from the nested scene): --viewer-freeze-child=freeze|hooks|repeat, --viewer-freeze-frames=N, --viewer-freeze-nested=TRACK.
/// The script runs in a ViewerFreezeDriver beside the scene (ProcessMode Always) because the scene's own _PhysicsProcess stops when it is Disabled.
/// Every result line starts with "VIEWERFREEZE:" and is judged by the harness (PhysicsTest.ViewerFreeze.cs).
/// </summary>
public partial class RaceScene
{
    string _vfMode, _vfNested; int _vfFrames = 90; float _vfWarm = 6f, _vfAbAt = -1f; bool _vfParsed; ViewerFreezeDriver _vfDriver; long _vfPhysTicks;

    partial void ViewerFreezeTick(float dt)
    {
        if (!_vfParsed)
        {
            _vfParsed = true;
            if (InstructionTrack != null) return;      // (the nested scene never runs the spike)
            foreach (var a in OS.GetCmdlineUserArgs())
            {
                if (a.StartsWith("--viewer-freeze-child=")) _vfMode = a["--viewer-freeze-child=".Length..];
                else if (a.StartsWith("--viewer-freeze-frames=")) _vfFrames = int.Parse(a["--viewer-freeze-frames=".Length..]);
                else if (a.StartsWith("--viewer-freeze-warm=")) _vfWarm = float.Parse(a["--viewer-freeze-warm=".Length..], Snap.Inv);
                else if (a.StartsWith("--viewer-freeze-ab-at=")) _vfAbAt = float.Parse(a["--viewer-freeze-ab-at=".Length..], Snap.Inv);
                else if (a.StartsWith("--viewer-freeze-nested=")) _vfNested = a["--viewer-freeze-nested=".Length..];
            }
        }
        if (_vfMode == null) return;
        _vfPhysTicks++;      // (counts this scene's own physics ticks that reached the recorder path: it must not grow while the scene is Disabled or State.Paused)
        if (_vfDriver != null || _state != State.Racing) return;
        _vfDriver = new ViewerFreezeDriver { Name = "ViewerFreezeDriver", ProcessMode = ProcessModeEnum.Always };
        GetParent().CallDeferred(Node.MethodName.AddChild, _vfDriver);
        IEnumerable script = _vfMode switch { "freeze" => VfFreezeRun(), "hooks" => VfHooksRun(), "repeat" => VfRepeatRun(), _ => VfFail("unknown mode " + _vfMode) };
        _vfDriver.Start(script);
        _vfDriver.OnDone = () => { VfLog("script done"); CCCPRacing.Audio.AudioMix.Quit(this); };
    }

    static IEnumerable VfFail(string why) { GD.Print("VIEWERFREEZE: FAIL " + why); yield break; }
    static void VfLog(string s) => GD.Print("VIEWERFREEZE: " + s);

    // ------------------------------------------------------------ the digest

    static object RfAny(object o, Type t, string name)
    {
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.FlattenHierarchy;
        for (var ty = t; ty != null; ty = ty.BaseType)
        {
            var fi = ty.GetField(name, all | BindingFlags.DeclaredOnly); if (fi != null) return fi.GetValue(o);
            var pi = ty.GetProperty(name, all | BindingFlags.DeclaredOnly); if (pi != null) return pi.GetValue(o);
        }
        return null;
    }
    static string Rf(object o, string name) => o == null ? "n/a" : Convert.ToString(RfAny(o, o.GetType(), name) ?? "n/a", Snap.Inv);
    static object RfObj(object o, string name) => o == null ? null : RfAny(o, o.GetType(), name);
    static object RfStatic(Type t, string name) => RfAny(null, t, name);
    static int CountOf(object collection) => collection is System.Collections.ICollection c ? c.Count : -1;

    Snap VfCapture(ICollection<Tween> excludeTweens = null)
    {
        var s = new Snap(); var f = s.Fields;
        string F(float v) => Snap.F(v);
        f["state"] = _state.ToString();
        f["car.pos"] = Snap.V(_car.Pos); f["car.vel"] = Snap.V(_car.Vel); f["car.angvel"] = Snap.V(_car.AngVel); f["car.rpm"] = F(_car.Rpm);
        f["car.rotX"] = Snap.V(_car.Rot.X); f["car.rotZ"] = Snap.V(_car.Rot.Z);
        f["car.ctl"] = $"{F(_car.Throttle)},{F(_car.Brake)},{F(_car.SteerIn)},{F(_car.SteerAngle)},{_car.Gear}";
        f["clock.scene"] = F(_clock); f["clock.lap"] = F(_lapClock); f["clock.tapeWorld"] = F(_tapeWorld); f["countdown"] = F(_countdown);
        f["timer.lapTime"] = F(_timer.LapTime); f["timer.progress"] = _timer.Progress.ToString(); f["timer.nextGate"] = _timer.NextGate.ToString(); f["timer.laps"] = _timer.LapsDone.ToString();
        f["rec.ghostSamples"] = _rec.Count.ToString(); f["rec.tapeSamples"] = (_tapeRec?.Samples ?? -1).ToString(); f["rec.tapeEntries"] = (_tapeRec?.Entries ?? -1).ToString();
        f["fuel.litres"] = F(_tank.Litres);
        f["day.clock"] = F(_dayNight.Clock); f["day.glow"] = F(_dayNight.Glow); f["day.active"] = _dayNight.Active.ToString(); f["day.lapsDriven"] = _dayNight.LapsDriven.ToString();
        f["day.skySeconds"] = Rf(RfObj(_dayNight, "_sky"), "_seconds");
        f["static.NightGlowLevel"] = F(NightGlow.Level); f["static.SurfacesWetness"] = F(Surfaces.Wetness);
        if (_weather != null) foreach (var w in new[] { "_rainNow", "_level", "_nextStrike", "_flashT", "_thunderIn", "_thunder" }) f["weather." + w] = Rf(_weather, w);
        if (_weather != null && RfObj(_weather, "_rng") is RandomNumberGenerator wrng) f["weather.rngState"] = wrng.State.ToString();
        ulong oh = 1469598103934665603; int moving = 0;
        foreach (var o in _track.Obstacles) { oh = Snap.Mix(oh, o.Center.X); oh = Snap.Mix(oh, o.Center.Y); oh = Snap.Mix(oh, o.Radius); if (o.Velocity != Vector2.Zero) moving++; }
        s.Obs = _track.Obstacles.Select(o => o.Center).ToArray();
        f["obstacles.count"] = _track.Obstacles.Count.ToString(); f["obstacles.hash"] = oh.ToString("x16"); f["obstacles.moving"] = moving.ToString();
        f["phys.ticks"] = _vfPhysTicks.ToString(); f["scene.frames"] = Engine.GetProcessFrames().ToString();
        int tw = 0; double twElapsed = 0;
        foreach (var t in GetTree().GetProcessedTweens()) { if (excludeTweens != null && excludeTweens.Contains(t)) continue; tw++; twElapsed += t.GetTotalElapsedTime(); }
        f["tweens.processed"] = tw.ToString(); f["tweens.elapsed"] = twElapsed.ToString("R", Snap.Inv);
        int players = 0, playing = 0, paused = 0; long gen = 0; double posSum = 0;
        foreach (var child in GetChildren()) VfWalk(child, child.Name, s, ref players, ref playing, ref paused, ref gen, ref posSum);
        f["audio.players"] = players.ToString(); f["audio.playing"] = playing.ToString(); f["audio.streamPaused"] = paused.ToString();
        f["audio.generatorFramesAvailable"] = gen.ToString(); f["audio.playbackPositionSum"] = posSum.ToString("F4", Snap.Inv);
        f["nodes.node3d"] = s.Nodes.Count.ToString();
        ulong all = 0; foreach (var n in s.Nodes.Values) all ^= n.Hash; f["nodes.hashXor"] = all.ToString("x16");
        return s;
    }

    void VfWalk(Node n, string group, Snap s, ref int players, ref int playing, ref int paused, ref long gen, ref double posSum)
    {
        if (n.Name.ToString().StartsWith("VfProbe")) return;
        if (n is Node3D d)
        {
            ulong h = Snap.MixT(1469598103934665603, d.Transform); h = (h ^ (d.Visible ? 7UL : 3UL)) * 1099511628211UL;
            foreach (char ch in n.Name.ToString()) h = (h ^ ch) * 1099511628211UL;
            s.Nodes[n.GetInstanceId()] = new Snap.NodeInfo(n.Name, n.GetClass(), group, h);
            s.Groups[group] = s.Groups.GetValueOrDefault(group) ^ h;
            if (d is CpuParticles3D cp) s.Extra[$"ptc:{group}/{n.Name}#{n.GetInstanceId()}"] = $"cpu emitting={cp.Emitting} oneShot={cp.OneShot} lifetime={cp.Lifetime}";
            else if (d is GpuParticles3D gp) s.Extra[$"ptc:{group}/{n.Name}#{n.GetInstanceId()}"] = $"gpu emitting={gp.Emitting} oneShot={gp.OneShot} speedScale={gp.SpeedScale}";
        }
        if (n is AudioStreamPlayer a) VfAudio(n, group, a.Playing, a.StreamPaused, a.GetPlaybackPosition(), a.Stream, VfPlayback(a, a.Playing, a.StreamPaused), s, ref players, ref playing, ref paused, ref gen, ref posSum);
        else if (n is AudioStreamPlayer3D a3) VfAudio(n, group, a3.Playing, a3.StreamPaused, a3.GetPlaybackPosition(), a3.Stream, VfPlayback(a3, a3.Playing, a3.StreamPaused), s, ref players, ref playing, ref paused, ref gen, ref posSum);
        foreach (var c in n.GetChildren()) VfWalk(c, group, s, ref players, ref playing, ref paused, ref gen, ref posSum);
    }

    readonly HashSet<ulong> _vfPlayed = new();      // players seen playing: Playing reads false while StreamPaused, but the playback (a generator's buffer) is still there
    AudioStreamPlayback VfPlayback(Node player, bool playing, bool streamPaused)
    {
        ulong id = player.GetInstanceId();
        if (playing) _vfPlayed.Add(id);
        if (!playing && !(streamPaused && _vfPlayed.Contains(id))) return null;
        return player is AudioStreamPlayer a ? a.GetStreamPlayback() : ((AudioStreamPlayer3D)player).GetStreamPlayback();
    }

    static void VfAudio(Node n, string group, bool isPlaying, bool isPaused, double pos, AudioStream stream, AudioStreamPlayback pb, Snap s, ref int players, ref int playing, ref int paused, ref long gen, ref double posSum)
    {
        players++; if (isPlaying) playing++; if (isPaused) paused++;
        int avail = -1;
        if (stream is AudioStreamGenerator && pb is AudioStreamGeneratorPlayback g) { avail = g.GetFramesAvailable(); gen += avail; }
        posSum += pos;
        s.Extra[$"aud:{group}/{n.Name}#{n.GetInstanceId()}"] = $"{n.GetClass()} playing={isPlaying} streamPaused={isPaused} pos={pos:F4} genFree={avail}";
    }

    void VfPrintDiff(string cond, Snap a, Snap b, int frames, long wallMs)
    {
        var d = Snap.Diff(a, b);
        VfLog($"cond={cond}|info|frames={frames} wallMs={wallMs}");
        foreach (var r in d.Rows)
            if (r.kind == "f") VfLog($"cond={cond}|f|{r.name}|{r.before}|{r.after}|{(r.changed ? 1 : 0)}");
        if (a.Obs != null && b.Obs != null && a.Obs.Length == b.Obs.Length)
        {
            var who = new Dictionary<int, string>(); foreach (var (w, j, _) in _worldObstacles) who[j] = w.ToString();
            var ch = Enumerable.Range(0, a.Obs.Length).Where(i => a.Obs[i] != b.Obs[i]).ToList();
            VfLog($"cond={cond}|obstacles-moved|{ch.Count}|{string.Join(",", ch.Take(20).Select(i => i + ":" + who.GetValueOrDefault(i, "static")))}");
        }
        int xChanged = d.Rows.Count(r => r.kind == "x" && r.changed), xAll = d.Rows.Count(r => r.kind == "x");
        VfLog($"cond={cond}|nodes|node3d={a.Nodes.Count} moved={d.NodesMoved} added={d.NodesAdded} removed={d.NodesRemoved} extraEntries={xAll} extraChanged={xChanged}");
        foreach (var g in d.MovedByGroup.OrderByDescending(kv => kv.Value).Take(40)) VfLog($"cond={cond}|movedgroup|{g.Key}|{g.Value}");
        foreach (var m in d.Movers.Take(25)) VfLog($"cond={cond}|mover|{m}");
        foreach (var r in d.Rows.Where(r => r.kind == "x" && r.changed).Take(40)) VfLog($"cond={cond}|xchanged|{r.name}|{r.before}|{r.after}");
        foreach (var r in d.Rows.Where(r => r.kind == "x" && r.name.StartsWith("ptc:")).Take(40)) VfLog($"cond={cond}|particle|{r.name}|{r.before}|{r.after}|{(r.changed ? 1 : 0)}");
    }

    // ------------------------------------------------------------ probes: a one-shot particle emitter and tweens inside the live scene (the subject) and beside it (the control)

    sealed class VfProbe
    {
        public Node3D LiveN, LiveN2, CtlN; public CpuParticles3D LiveP, CtlP; public Tween LiveT, CtlT, TreeT;
        public List<Tween> Tweens => new() { LiveT, CtlT, TreeT };
    }

    VfProbe VfProbeStart()
    {
        var p = new VfProbe();
        CpuParticles3D Burst() => new() { Amount = 4, Lifetime = 0.5, OneShot = true, Explosiveness = 1f, Emitting = false };
        p.LiveN = new Node3D { Name = "VfProbeLive" }; p.LiveN2 = new Node3D { Name = "VfProbeLive2" };
        p.CtlN = new Node3D { Name = "VfProbeControl", ProcessMode = ProcessModeEnum.Always };
        AddChild(p.LiveN); AddChild(p.LiveN2); GetParent().AddChild(p.CtlN);
        p.LiveP = Burst(); p.LiveN.AddChild(p.LiveP); p.CtlP = Burst(); p.CtlN.AddChild(p.CtlP);
        p.LiveP.Emitting = true; p.CtlP.Emitting = true;
        p.LiveT = p.LiveN.CreateTween(); p.LiveT.TweenProperty(p.LiveN, "position:x", 100f, 0.5);       // bound to a node of the live scene (how Missile/BalalaikaSorties make theirs)
        p.CtlT = p.CtlN.CreateTween(); p.CtlT.TweenProperty(p.CtlN, "position:x", 100f, 0.5);
        p.TreeT = GetTree().CreateTween(); p.TreeT.TweenProperty(p.LiveN2, "position:x", 100f, 0.5);    // not bound to any node
        return p;
    }

    void VfProbeReport(string cond, VfProbe p)
    {
        VfLog($"cond={cond}|probe|particleLive emitting={p.LiveP.Emitting}|particleControl emitting={p.CtlP.Emitting}");
        VfLog($"cond={cond}|probe|tweenLiveBound x={p.LiveN.Position.X:F2} running={p.LiveT.IsRunning()} valid={p.LiveT.IsValid()}|tweenControl x={p.CtlN.Position.X:F2} running={p.CtlT.IsRunning()} valid={p.CtlT.IsValid()}|tweenUnbound x={p.LiveN2.Position.X:F2} running={p.TreeT.IsRunning()} valid={p.TreeT.IsValid()}");
    }

    void VfProbeFree(VfProbe p)
    {
        foreach (var t in p.Tweens) if (t.IsValid()) t.Kill();
        p.LiveN.Free(); p.LiveN2.Free(); p.CtlN.Free();
    }

    // ------------------------------------------------------------ the live scene frozen the way OpenInstruction does it

    /// <summary>The recipe of OpenInstruction (RaceScene.Pause.cs), without the nested world: audio pause of every player under the scene, HUD layer hidden, ProcessMode Disabled, Visible=false. Returns the undo.</summary>
    Action VfFreezeLive(bool audioPause)
    {
        var resume = new List<Action>();
        if (audioPause)
            foreach (var node in FindChildren("*", "", true, false))
            {
                if (node is AudioStreamPlayer audio) { bool was = audio.StreamPaused; audio.StreamPaused = true; resume.Add(() => audio.StreamPaused = was); }
                else if (node is AudioStreamPlayer3D spatial) { bool was = spatial.StreamPaused; spatial.StreamPaused = true; resume.Add(() => spatial.StreamPaused = was); }
            }
        var layer = (CanvasLayer)_hud.GetParent(); bool layerVisible = layer.Visible, sceneVisible = Visible;
        layer.Visible = false; ProcessMode = ProcessModeEnum.Disabled; Visible = false;
        return () => { foreach (var r in resume) r(); layer.Visible = layerVisible; ProcessMode = ProcessModeEnum.Inherit; Visible = sceneVisible; };
    }

    IEnumerable VfWait(int frames, int delayMs = 16) { for (int i = 0; i < frames; i++) { if (delayMs > 0) OS.DelayMsec(delayMs); yield return null; } }

    static string VfPausedOf(string entry) => entry.Split(' ').FirstOrDefault(x => x.StartsWith("streamPaused=")) ?? "";

    readonly Dictionary<string, (Snap before, Snap after)> _vfResults = new();

    IEnumerable VfCond(string cond, int n, Func<Action> apply, bool outside = false)
    {
        var before = VfCapture();
        var probe = VfProbeStart();
        var undo = apply?.Invoke();
        foreach (var x in VfWait(5)) yield return x;      // settle: a paused AudioStreamPlayer fades out over its last mix buffer (about 12 ms of position)
        var mid = VfCapture(probe.Tweens);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        foreach (var x in VfWait(n)) yield return x;
        var after = VfCapture(probe.Tweens);
        long ms = sw.ElapsedMilliseconds;
        VfPrintDiff(cond, before, after, n, ms);
        {
            var md = Snap.Diff(mid, after);      // the strict check: from the frame the condition was applied to its last frame
            VfLog($"cond={cond}|post-apply-to-end|changed fields [{string.Join(",", md.Rows.Where(r => r.kind == "f" && r.changed && r.name != "scene.frames").Select(r => r.name + " " + r.before + "->" + r.after))}]|extraChanged={md.Rows.Count(r => r.kind == "x" && r.changed)}|nodesMoved={md.NodesMoved} added={md.NodesAdded} removed={md.NodesRemoved}");
        }
        VfProbeReport(cond, probe);
        VfLog($"cond={cond}|info|canProcess scene={CanProcess()} hud={_hud.CanProcess()} cam={_cam.CanProcess()} dayNight={_dayNight.CanProcess()} weather={(_weather?.CanProcess().ToString() ?? "n/a")} physicsProcessing={IsPhysicsProcessing()} processing={IsProcessing()}");
        {
            int can = 0, total = 0; var by = new SortedDictionary<string, int>(StringComparer.Ordinal);
            void Walk(Node nd) { if (nd.Name.ToString().StartsWith("VfProbe")) return; total++; if (nd.CanProcess()) { can++; by[nd.GetClass()] = by.GetValueOrDefault(nd.GetClass()) + 1; } foreach (var ch in nd.GetChildren()) Walk(ch); }
            foreach (var ch in GetChildren()) Walk(ch);
            VfLog($"cond={cond}|canprocess|nodes under the scene={total} with CanProcess()={can} [{string.Join(",", by.Select(kv => kv.Key + "=" + kv.Value))}]");
        }
        if (outside) VfOutside(cond);
        undo?.Invoke();
        if (apply != null)
        {
            var post = VfCapture(probe.Tweens);
            var pd = Snap.Diff(before, post);      // audio must come back as it was: players paused before stay paused, playing ones play
            VfLog($"cond={cond}|after-undo|streamPaused before={before.Fields["audio.streamPaused"]} afterUndo={post.Fields["audio.streamPaused"]}|playing before={before.Fields["audio.playing"]} afterUndo={post.Fields["audio.playing"]}|players whose streamPaused differs from before={pd.Rows.Count(r => r.kind == "x" && r.name.StartsWith("aud:") && VfPausedOf(r.before) != VfPausedOf(r.after))}");
        }
        if (apply != null)
        {
            yield return null;
            var later = VfCapture(probe.Tweens);
            VfLog($"cond={cond}|after-undo+1frame|streamPaused={later.Fields["audio.streamPaused"]} playing={later.Fields["audio.playing"]}");
        }
        VfProbeFree(probe);
        _vfResults[cond] = (before, after);
    }

    /// <summary>What lives outside the scene's subtree (audio players, processing nodes): the recipe does not touch it.</summary>
    void VfOutside(string cond)
    {
        var byClass = new SortedDictionary<string, int>(StringComparer.Ordinal); int audio = 0, audioPlaying = 0, audioPaused = 0;
        void Walk(Node n, string path)
        {
            if (n == this || n is ViewerFreezeDriver || n.Name.ToString().StartsWith("VfProbe")) return;
            if (n.CanProcess() && n.GetChildCount() == 0 || n is Control or CanvasLayer or AudioStreamPlayer or AudioStreamPlayer3D)
                byClass[n.GetClass()] = byClass.GetValueOrDefault(n.GetClass()) + (n.CanProcess() ? 1 : 0);
            if (n is AudioStreamPlayer a) { audio++; if (a.Playing) audioPlaying++; if (a.StreamPaused) audioPaused++; VfLog($"cond={cond}|outside-audio|{path}/{n.Name}|playing={a.Playing}|streamPaused={a.StreamPaused}|stream={a.Stream?.GetClass()}"); }
            else if (n is AudioStreamPlayer3D a3) { audio++; if (a3.Playing) audioPlaying++; if (a3.StreamPaused) audioPaused++; VfLog($"cond={cond}|outside-audio|{path}/{n.Name}|playing={a3.Playing}|streamPaused={a3.StreamPaused}|stream={a3.Stream?.GetClass()}"); }
            foreach (var c in n.GetChildren()) Walk(c, path + "/" + n.Name);
        }
        Walk(GetTree().Root, "");
        VfLog($"cond={cond}|outside|audioPlayers={audio} playing={audioPlaying} streamPaused={audioPaused}|canProcess by class [{string.Join(",", byClass.Select(kv => kv.Key + "=" + kv.Value))}]");
    }

    // ------------------------------------------------------------ scenario 1: the freeze

    IEnumerable VfFreezeRun()
    {
        while (_lapClock < _vfWarm || _state != State.Racing) yield return null;
        int n = _vfFrames;
        VfLog($"scenario freeze track={_trackId} frames={n} lapClock={_lapClock:F2} physicsHz={Engine.PhysicsTicksPerSecond} tape={_tapeOn} weather={_weather != null}");
        VfLog($"setup|day phase={_dayNight.LapPhase} glow={_dayNight.Glow:F2}");
        foreach (var x in VfCond("A1-run", n, null)) yield return x;                                   // baseline (a): the live scene running normally
        foreach (var x in VfCond("B-disabled+audiopause", n, () => VfFreezeLive(true), outside: true)) yield return x;       // the proposal
        foreach (var x in VfCond("A2-run-after-B", n, null)) yield return x;
        foreach (var x in VfCond("D-disabled-NO-audiopause", n, () => VfFreezeLive(false))) yield return x;        // what the audio pause adds
        foreach (var x in VfCond("A3-run-after-D", n, null)) yield return x;
        foreach (var x in VfCond("C-state-paused", n, () => { _state = State.Paused; return () => _state = State.Racing; })) yield return x;       // baseline (b): the game's own pause
        foreach (var x in VfCond("E-state-paused+disabled+audiopause", n, () =>
        {
            _state = State.Paused; var u = VfFreezeLive(true);
            return () => { u(); _state = State.Racing; };
        }, outside: true)) yield return x;
        foreach (var x in VfCond("A4-run-after-E", n, null)) yield return x;
    }

    // ------------------------------------------------------------ scenario 2: hooks, shared caches and the nested world's exit

    HashSet<ulong> _vfLiveGlows, _vfLiveRadars;      // identity of the live world's entries in the shared static lists, taken before the nested world is built

    string VfStatics(string tag)
    {
        _vfLiveGlows ??= NightGlow.Glows.Select(g => g.mat.GetInstanceId()).ToHashSet();
        _vfLiveRadars ??= RadarHead.All.Select(r => r.GetInstanceId()).ToHashSet();
        string lights = Rf(_dayNight, "_sceneryLights") == "n/a" ? "n/a" : CountOf(RfObj(_dayNight, "_sceneryLights")).ToString();
        var gulag = RfStatic(typeof(GulagTruck), "_onRoad");
        string line = $"{tag}|trackId={_trackId}|wetness={Snap.F(Surfaces.Wetness)}|concreteGrip@0={Snap.F(Surfaces.Get(SurfaceType.Concrete).GripAt(0f))}|concreteGrip@30={Snap.F(Surfaces.Get(SurfaceType.Concrete).GripAt(30f))}"
            + $"|glowsOfLiveWorld={NightGlow.Glows.Count(g => GodotObject.IsInstanceValid(g.mat) && _vfLiveGlows.Contains(g.mat.GetInstanceId()))}/{NightGlow.Glows.Count}|radarsOfLiveWorld={RadarHead.All.Count(r => GodotObject.IsInstanceValid(r) && _vfLiveRadars.Contains(r.GetInstanceId()))}/{RadarHead.All.Count}"
            + $"|NightGlow.Level={Snap.F(NightGlow.Level)}|live day.glow={Snap.F(_dayNight.Glow)}|NightGlow.Glows={NightGlow.Glows.Count}|NightGlow.Lamps={NightGlow.Lamps.Count}|RadarHead.All={RadarHead.All.Count}"
            + $"|SeeThrough.Off={SeeThrough.Off}|FadeUnder.Off={FadeUnder.Off}|Props.BatchCache={CountOf(RfStatic(typeof(Props), "BatchCache"))}|Props.Materials={CountOf(RfStatic(typeof(Props), "Materials"))}"
            + $"|GulagTruck._onRoad={(gulag == null ? "null" : "set")}|Missile._serial={Convert.ToString(RfStatic(typeof(Missile), "_serial"), Snap.Inv)}|SfxMuted={AudioServer.IsBusMute(AudioServer.GetBusIndex("Sfx"))}"
            + $"|liveDayNight._unlit={CountOf(RfObj(_dayNight, "_unlit"))}|liveDayNight._sceneryLights={lights}"
            + $"|treeNodes={GetTree().GetNodeCount()}|objects={Performance.GetMonitor(Performance.Monitor.ObjectCount):F0}|objNodes={Performance.GetMonitor(Performance.Monitor.ObjectNodeCount):F0}|objResources={Performance.GetMonitor(Performance.Monitor.ObjectResourceCount):F0}|nodeAddedConnections={GetTree().GetSignalConnectionList(SceneTree.SignalName.NodeAdded).Count}|orphans={Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount):F0}";
        VfLog("hooks|" + line);
        return line;
    }

    void VfPatrols(string tag)
    {
        var nestedRoot = _instructionPlayer;
        string Where(Node n) => n == null ? "null" : !IsInstanceValid(n) ? "freed" : nestedRoot != null && IsInstanceValid(nestedRoot) && nestedRoot.IsAncestorOf(n) ? "NESTED" : IsAncestorOf(n) ? "LIVE" : "other";
        void Walk(Node n)
        {
            if (n is DachaPatrol dp) { var boat = RfObj(dp, "_boat") as Node; VfLog($"hooks|{tag}|DachaPatrol in {Where(dp)} is bound to a boat in {Where(boat)} (patrol {dp.GetInstanceId()}, boat {boat?.GetInstanceId()})"); }
            if (n is PartyRowboat rb) VfLog($"hooks|{tag}|PartyRowboat in {Where(rb)} ({rb.GetInstanceId()})");
            foreach (var c in n.GetChildren()) Walk(c);
        }
        Walk(GetTree().Root);
        int live = 0, nested = 0, other = 0;
        foreach (var node in GetTree().GetNodesInGroup("sight_occluder")) { var w = Where(node); if (w == "LIVE") live++; else if (w == "NESTED") nested++; else other++; }
        VfLog($"hooks|{tag}|group sight_occluder (tree-wide) live={live} nested={nested} other={other}: a SpeedTrap caches the whole group once");
    }

    IEnumerable VfHooksRun()
    {
        while (_lapClock < 3f || _state != State.Racing) yield return null;
        string nested = _vfNested ?? _trackId;
        VfLog($"scenario hooks live={_trackId} nested={nested} lapClock={_lapClock:F2} day.glow={_dayNight.Glow:F2}");
        VfStatics("1-live-racing");
        // the real flow: the lessons open from the pause menu (State.Paused)
        var hudMode = _hud.Mode; _state = State.Paused; _hud.Mode = HudMode.Paused;
        foreach (var x in VfWait(2, 0)) yield return x;
        var liveBefore = VfCapture();
        var statics0 = VfStatics("2-paused-before-open");
        var liveUnlitBefore = new Dictionary<object, string>(); var liveLightsBefore = new HashSet<object>();
        foreach (System.Collections.DictionaryEntry kv in (System.Collections.IDictionary)RfObj(_dayNight, "_unlit")) liveUnlitBefore[kv.Key] = Convert.ToString(RfObj(kv.Value, "Item1"), Snap.Inv);      // the recorded "day" colour
        foreach (var t in (System.Collections.IEnumerable)RfObj(_dayNight, "_sceneryLights")) liveLightsBefore.Add(RfObj(t, "Item1"));
        OpenInstruction((int)SchoolCourse.Rally, nested, 0);
        var nestedViewport = _instructionViewport; var nestedPlayer = _instructionPlayer;
        {
            var nd = RfObj(nestedPlayer, "_dayNight"); var nu = (System.Collections.IDictionary)RfObj(nd, "_unlit"); var lu = (System.Collections.IDictionary)RfObj(_dayNight, "_unlit");
            int shared = 0, sharedDifferentDay = 0, inLiveNow = 0;
            foreach (System.Collections.DictionaryEntry kv in nu)
            {
                if (lu.Contains(kv.Key)) inLiveNow++;
                if (liveUnlitBefore.TryGetValue(kv.Key, out var liveDay)) { shared++; if (Convert.ToString(RfObj(kv.Value, "Item1"), Snap.Inv) != liveDay) sharedDifferentDay++; }
            }
            int nl = 0, nlShared = 0, nlInLive = 0; var liveLightsNow = new HashSet<object>(); foreach (var t in (System.Collections.IEnumerable)RfObj(_dayNight, "_sceneryLights")) liveLightsNow.Add(RfObj(t, "Item1"));
            foreach (var t in (System.Collections.IEnumerable)RfObj(nd, "_sceneryLights")) { nl++; var l = RfObj(t, "Item1"); if (liveLightsBefore.Contains(l)) nlShared++; if (liveLightsNow.Contains(l)) nlInLive++; }
            VfLog($"hooks|3|nested DayNight._unlit={nu.Count}: {inLiveNow} of them are now also in the LIVE DayNight._unlit; {shared} were already in the live one BEFORE the nested world existed (shared material objects), of which {sharedDifferentDay} carry a different recorded day colour in the nested one");
            VfLog($"hooks|3|nested DayNight._sceneryLights={nl}: {nlInLive} of them are now in the LIVE DayNight._sceneryLights; {nlShared} were already live lights (shared)");
        }
        VfStatics("3-just-after-OpenInstruction-returned");
        VfPatrols("3");
        foreach (var x in VfWait(60, 8)) yield return x;
        VfStatics("4-nested-has-run-60-frames");
        var liveDuring = VfCapture();
        var dd = Snap.Diff(liveBefore, liveDuring);
        VfLog($"hooks|live-digest while the nested world ran 60 frames: changed fields [{string.Join(",", dd.Rows.Where(r => r.kind == "f" && r.changed).Select(r => r.name + " " + r.before + "->" + r.after))}] nodesMoved={dd.NodesMoved} nodesAdded={dd.NodesAdded} nodesRemoved={dd.NodesRemoved}");
        VfLog($"hooks|nested scene: state={nestedPlayer._state} lapClock={nestedPlayer._lapClock:F2} clock={nestedPlayer._clock:F2} (it runs)");
        // exit the way the proposal says: QueueFree, let the deferred ExitTree callbacks run, restore the statics only afterwards
        Input.Singleton.JoyConnectionChanged -= nestedPlayer.OnJoyConnection;
        var image = _instructionImage;
        nestedViewport.QueueFree(); image.QueueFree();
        VfStatics("5-after-QueueFree-same-frame");
        for (int i = 1; i <= 3; i++)
        {
            yield return null;
            VfStatics($"6-QueueFree+{i}-frames (viewport valid={IsInstanceValid(nestedViewport)}, nested scene valid={IsInstanceValid(nestedPlayer)})");
        }
        _instructionPlayer = null; _instructionViewport = null; _instructionImage = null;
        _restoreInstructionWorld(); _restoreInstructionWorld = null;      // the existing restore list, after the ExitTree callbacks
        foreach (var resume in _resumeRaceAudio) resume();
        _resumeRaceAudio.Clear();
        ((CanvasLayer)_hud.GetParent()).Visible = true; ProcessMode = ProcessModeEnum.Inherit; Visible = true; _showLessons = false; _overlayExitFrame = Engine.GetProcessFrames();
        VfStatics("7-after-the-existing-restore-list");
        _state = State.Racing; _hud.Mode = hudMode;
        foreach (var x in VfWait(1, 0)) yield return x;
        VfStatics("8-live-resumed+1-frame");
        foreach (var x in VfWait(5, 0)) yield return x;
        VfStatics("9-live-resumed+6-frames");
        foreach (var x in VfWait(120, 0)) yield return x;
        VfStatics("10-live-resumed+126-frames");
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        foreach (var x in VfWait(3, 0)) yield return x;
        VfStatics("11-after-GC.Collect+3-frames");
        // does the live DayNight survive the foreign entries? SetGlow returns early when the glow is unchanged, so force a real walk over the grown lists: another value, then back
        {
            var setGlow = typeof(DayNight).GetMethod("SetGlow", BindingFlags.Instance | BindingFlags.NonPublic);
            float g = _dayNight.Glow, levelBefore = NightGlow.Level;
            try { setGlow.Invoke(_dayNight, new object[] { g > 0.5f ? g - 0.25f : g + 0.25f }); setGlow.Invoke(_dayNight, new object[] { g }); VfLog($"hooks|live DayNight.SetGlow walked the grown collections (forced to another value and back): no exception; NightGlow.Level {levelBefore:F2} -> {NightGlow.Level:F2} (glow {g:F2})"); }
            catch (Exception e) { VfLog("hooks|live DayNight.SetGlow FAILED: " + e.InnerException?.Message); }
        }
        var liveAfter = VfCapture();
        var da = Snap.Diff(liveBefore, liveAfter);
        VfLog($"hooks|live car moved on after resume: pos {liveBefore.Fields["car.pos"]} -> {liveAfter.Fields["car.pos"]}, lapClock {liveBefore.Fields["clock.lap"]} -> {liveAfter.Fields["clock.lap"]}");
        VfLog($"hooks|state before {statics0}");
    }

    // ------------------------------------------------------------ scenario 3: does a plain run repeat?

    IEnumerable VfRepeatRun()
    {
        VfLog($"scenario repeat track={_trackId} seedsFromTapeGame=1");
        int frame = 0; string archived = null; bool froze = _vfAbAt < 0;
        while (true)
        {
            if (!froze && _lapClock >= _vfAbAt)      // the A/B run: freeze the live scene mid-lap (the OpenInstruction recipe), let the driver run frames, resume; the rest of the lap must come out as in the plain run
            {
                froze = true;
                var b4 = VfCapture(); var undo = VfFreezeLive(true);
                foreach (var x in VfWait(_vfFrames, 0)) yield return x;
                var af = VfCapture(); undo();
                VfPrintDiff("AB-freeze", b4, af, _vfFrames, 0);
                VfLog($"rep|ab|frozeAtLapClock|{Snap.F(_lapClock)}|frame|{frame}|driverFrames|{_vfFrames}");
                continue;
            }
            if (frame % 30 == 0)
            {
                var s = VfCapture();
                foreach (var (k, v) in s.Fields) VfLog($"rep|{frame}|{k}|{v}");
                foreach (var (g, h) in s.Groups) VfLog($"rep|{frame}|grp.{g}|{h:x16}");
                if (frame == 30 || frame == 180) { int idx = 0; foreach (var (nid, nd) in s.Nodes) VfLog($"repnode|{frame}|{idx++}|{(GodotObject.InstanceFromId(nid) is Node nn ? GetPathTo(nn) : nd.Name)}:{nd.Class}|{nd.Hash:x16}"); }      // (which node first parts two runs)
            }
            if (_timer.LapsDone >= 1 && frame % 10 == 0 && archived == null)
            {
                var box = LapArchiveBox;
                var info = box.List().FirstOrDefault(i => i.Openable);
                if (info != null)
                {
                    var src = box.Open(info.Id, out var why);
                    if (src != null)
                    {
                        archived = info.Id;
                        VfLog($"rep|final|lapId|{info.Id}");
                        VfLog($"rep|final|ghostSha256|{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(src.GhostBytes.Span))}");
                        VfLog($"rep|final|ghostBytes|{src.GhostBytes.Length}");
                        VfLog($"rep|final|tapeSha256|{(src.HasTape ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(src.TapeBytes.Span)) : "none")}");
                        VfLog($"rep|final|tapeBytes|{(src.HasTape ? src.TapeBytes.Length : 0)}");
                        VfLog($"rep|final|lapTime|{Snap.F(_timer.LastLap)}|atFrame|{frame}");
                        break;
                    }
                }
            }
            if (frame > 40000) { VfLog("rep|final|FAIL no archived lap within 40000 frames"); break; }
            frame++;
            yield return null;
        }
    }
}
