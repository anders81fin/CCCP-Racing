using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using CCCPRacing.Car;
using CCCPRacing.Replay;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// Replay viewer fidelity scenarios (flag --viewer-fidelity=NAME in a child game; PhysicsTest.ViewerFidelity.cs judges the VIEWERFIDELITY lines).
/// The script runs in a ViewerFreezeDriver beside the scene (the live scene is Disabled while a viewer is open); see RaceScene.ViewerTest.cs for the pattern.
///
///   --viewer-fidelity=playback   a lap is driven (autodrive), the poses of the original world are captured at the tape samples, then the viewer opens on the
///                                archived lap: A (poses against the original) and B (time control, seek, end, loop, audio and particles).
///   --viewer-fidelity=ab         the A/B continuation oracle. --vfi-variant=plain|viewer|freeze  --vfi-fixture=none|boost|damage|missile|mission
///                                --vfi-at=SECONDS (the lap clock the viewer or the freeze starts at)  --vfi-frames=N (freeze length, taken from the viewer run)
///                                --vfi-tail=TICKS (live ticks to run after the lap line)  --vfi-out=FILE (digest, delta coded)  --vfi-trace=FILE (per-tick hash).
/// Every line starts with "VIEWERFIDELITY:".
/// </summary>
public partial class RaceScene
{
    string _vfiMode, _vfiVariant = "plain", _vfiFixture = "none", _vfiOut, _vfiTracePath;
    float _vfiAt = 25f; int _vfiN, _vfiTail = 120; bool _vfiParsed; ViewerFreezeDriver _vfiDriver; long _vfiTicks;
    int _vfiPass, _vfiFail;

    static void VfiLog(string s) => GD.Print("VIEWERFIDELITY: " + s);
    static string VfiF(float v) => v.ToString("R", CultureInfo.InvariantCulture);

    void VfiChk(string name, bool ok, string detail = "")
    {
        if (ok) _vfiPass++; else _vfiFail++;
        VfiLog($"{(ok ? "PASS" : "FAIL")}|{name}|{detail}");
    }

    void ViewerFidelityTick(float dt)
    {
        if (!_vfiParsed)
        {
            _vfiParsed = true;
            if (InstructionTrack != null || ViewerMode) return;      // (a viewer never runs a scenario)
            foreach (var a in OS.GetCmdlineUserArgs())
            {
                if (a.StartsWith("--viewer-fidelity=")) _vfiMode = a["--viewer-fidelity=".Length..];
                else if (a.StartsWith("--vfi-variant=")) _vfiVariant = a["--vfi-variant=".Length..];
                else if (a.StartsWith("--vfi-fixture=")) _vfiFixture = a["--vfi-fixture=".Length..];
                else if (a.StartsWith("--vfi-at=")) _vfiAt = float.Parse(a["--vfi-at=".Length..], CultureInfo.InvariantCulture);
                else if (a.StartsWith("--vfi-frames=")) _vfiN = int.Parse(a["--vfi-frames=".Length..]);
                else if (a.StartsWith("--vfi-tail=")) _vfiTail = int.Parse(a["--vfi-tail=".Length..]);
                else if (a.StartsWith("--vfi-out=")) _vfiOut = a["--vfi-out=".Length..];
                else if (a.StartsWith("--vfi-trace=")) _vfiTracePath = a["--vfi-trace=".Length..];
            }
            if (_vfiMode == "ab") VfiAbInit();
        }
        if (_vfiMode == null) return;
        if (_vfiMode == "playback")
        {
            VfiCaptureTick();
            if (_vfiDriver == null && _state == State.Racing) VfiStart(VfiPlayback());
        }
        else if (_vfiMode == "ab") VfiAbTick();
        else if (_vfiDriver == null) VfiStart(VfiFailScript("unknown scenario " + _vfiMode));
    }

    static IEnumerable VfiFailScript(string why) { VfiLog("FAIL|scenario|" + why); yield break; }

    void VfiStart(IEnumerable script)
    {
        _vfiDriver = new ViewerFreezeDriver { Name = "ViewerFidelityDriver", ProcessMode = ProcessModeEnum.Always };
        GetParent().CallDeferred(Node.MethodName.AddChild, _vfiDriver);
        _vfiDriver.Start(script);
        _vfiDriver.OnDone = () => { if (_vfiMode == "ab") { VfiLog("ab|script done"); return; } VfiLog($"SUMMARY|pass={_vfiPass}|fail={_vfiFail}"); VfiLog("script done"); GetTree().Quit(); };
    }

    // =====================================================================================================================================================
    // Original-world capture (playback): the poses of the tape's static parts at the tape samples, taken at the start of the next physics tick (the recorder runs at the end of one)
    // =====================================================================================================================================================

    sealed class VfiCap
    {
        public int Index; public Transform3D[] T; public bool[] Vis; public Dictionary<int, Vector3> Inst = new();
    }

    readonly Dictionary<int, VfiCap> _vfiGrid = new();
    readonly List<VfiCap> _vfiRoll = new();
    TapePartId[] _vfiIds;
    int _vfiLastSamples, _vfiGhostCount; bool _vfiStopCapture;
    readonly Dictionary<int, Vector3> _vfiCarPos = new();

    void VfiCaptureTick()
    {
        if (_tapeRec == null || _tapeIndex == null || !_tapeOn || _vfiStopCapture) return;
        int n = _tapeRec.Samples;
        if (n < _vfiLastSamples) { _vfiStopCapture = true; return; }      // the next lap began: only lap 1 is captured
        if (n > _vfiLastSamples)
        {
            int stat = _tapeRest.Length;
            if (_vfiIds == null) { _vfiIds = new TapePartId[stat]; for (int i = 0; i < stat; i++) _vfiIds[i] = _tapeIndex.IdOf(i); }
            var cap = new VfiCap { Index = n - 1, T = new Transform3D[stat], Vis = new bool[stat] };
            for (int i = 0; i < stat; i++) { var nd = _tapeIndex.NodeOf(i); if (IsInstanceValid(nd)) { cap.T[i] = nd.Transform; cap.Vis[i] = nd.Visible; } }
            foreach (var (node, inst) in _tapeInst) if (IsInstanceValid(node)) cap.Inst[inst.Id] = node.Position;
            if ((n - 1) % 15 == 0) _vfiGrid[n - 1] = cap;
            _vfiRoll.Add(cap); if (_vfiRoll.Count > 4) _vfiRoll.RemoveAt(0);
            _vfiLastSamples = n;
        }
        int g = _rec.Count;
        if (g > _vfiGhostCount) { _vfiCarPos[g - 1] = _car.Pos; _vfiGhostCount = g; }
    }

    // =====================================================================================================================================================
    // playback: A (poses) and B (time control)
    // =====================================================================================================================================================

    const float VfiPosTol = 0.001f;      // the tape stores positions on a 1/1024 m grid (TapeWire.PosScale): half a step is 0.00049 m; the spike measured 0.0006 m
    const float VfiBasisTol = 0.001f;     // rotations: smallest-three quaternion, spinners 1/2048 rad: about 0.0003 in a basis element

    IEnumerable VfiPlayback()
    {
        LapSource lap = null;
        int waited = 0;
        while (lap == null)
        {
            var box = _lapArchiveUsed; var info = box?.List().FirstOrDefault(i => i.Openable);
            if (info != null) lap = box.Open(info.Id);
            if (lap != null) break;
            if (++waited > 60 * 1500) { VfiChk("A0 a lap reached the archive", false, $"none after {waited} frames, lap clock {_lapClock:F1}, state {_state} {_failReason}"); VfiLog($"SUMMARY|pass={_vfiPass}|fail={_vfiFail}"); GetTree().Quit(); yield break; }
            yield return null;
        }
        VfiLog($"playback|track={_trackId}|lap={lap.Meta.Id}|seconds={lap.Meta.LapSeconds:F2}|tape={lap.HasTape}|captured grid samples={_vfiGrid.Count} roll={string.Join(",", _vfiRoll.Select(r => r.Index))} ghost car samples={_vfiCarPos.Count}");
        var session = OpenReplayViewer(lap, out string err);
        VfiChk("A0 viewer opens", session != null, err);
        if (session == null) yield break;
        int guard = 0;
        while (session.State == ReplayViewerState.Building && guard++ < 60 * 180) yield return null;
        VfiChk("A0 viewer reaches Playing", session.State == ReplayViewerState.Playing, $"state {session.State} error {session.Error}");
        if (session.State != ReplayViewerState.Playing) { session.Close(); yield break; }
        var v = session.Scene;
        v.ViewerSetPaused(true);
        for (int i = 0; i < 3; i++) yield return null;

        // ---- A: viewer world against the original
        var ghost = Ghost.FromBytes(lap.GhostBytes.ToArray(), _params, _track, out string gerr);
        VfiChk("A0 independent ghost parse", ghost != null, gerr);
        int nTape = v._tapes[lap.Meta.Set]?.Tape?.SampleCount ?? -1;
        VfiLog($"A|viewer duration {v.ViewerDuration:F3} s, ghost samples {ghost?.Count}, tape samples {nTape} ({nTape / WorldTapeConst.TapeRate:F3} s), tape in viewer {v.ViewerHasTape}");
        var covered = v._tapeKeys.Values.Where(k => v._tapePlayer?.Covers(k) == true).OrderBy(x => x).ToList();
        var uncovered = v._tapeKeys.Values.Where(k => v._tapePlayer?.Covers(k) != true).OrderBy(x => x).ToList();
        VfiLog($"A|tape covers [{string.Join(", ", covered)}]");
        VfiLog($"A|NOT covered (live in the viewer, not compared) [{string.Join(", ", uncovered)}]");
        VfiChk("A0 viewer has the tape", v.ViewerHasTape && covered.Count > 0, $"{covered.Count} covered actors");
        var picks = new List<(string name, int idx)>();
        if (nTape > 0)
        {
            int last = nTape - 1;
            int Snap(float f) => (int)MathF.Round(f * last / 15f) * 15;
            picks.Add(("0", 0)); picks.Add(("5s", 75)); picks.Add(("25%", Snap(.25f))); picks.Add(("50%", Snap(.5f))); picks.Add(("90%", Snap(.9f)));
            var endCap = _vfiRoll.OrderBy(r => r.Index).LastOrDefault();
            if (endCap != null) { picks.Add(("end", endCap.Index)); VfiLog($"A|end sample = last captured {endCap.Index} of the tape's {last} (a gap of {last - endCap.Index}: the finish tick resets the recorder before the next tick starts)"); }
        }
        foreach (var (name, idx) in picks)
        {
            var cap = _vfiGrid.GetValueOrDefault(idx) ?? _vfiRoll.FirstOrDefault(r => r.Index == idx);
            if (cap == null) { VfiChk($"A clock {name} captured", false, $"sample {idx} was not captured in the original run"); continue; }
            float clock = idx / WorldTapeConst.TapeRate;
            v.ViewerSeek(clock);
            for (int i = 0; i < 2; i++) yield return null;
            VfiPoseCompare(name, clock, idx, cap, v, ghost);
        }

        // ---- B: time control
        foreach (var x in VfiTimeControl(v, ghost)) yield return x;

        // ---- close
        CloseReplayViewer();
        guard = 0;
        while (session.State != ReplayViewerState.Closed && guard++ < 600) yield return null;
        VfiChk("B close: session Closed, live restored", session.State == ReplayViewerState.Closed && ProcessMode == ProcessModeEnum.Inherit && Visible && ActiveReplayViewers.Count == 0, $"state {session.State} mode {ProcessMode} visible {Visible} active {ActiveReplayViewers.Count}");
    }

    static float VfiMax(Vector3 d) => MathF.Max(MathF.Abs(d.X), MathF.Max(MathF.Abs(d.Y), MathF.Abs(d.Z)));

    void VfiPoseCompare(string name, float clock, int idx, VfiCap cap, RaceScene v, Ghost ghost)
    {
        int parts = 0, skipped = 0, visBad = 0, missing = 0; float maxPos = 0, maxBasis = 0; string worstPos = "", worstBasis = "";
        var perActor = new Dictionary<string, float>();
        for (int i = 0; i < cap.T.Length; i++)
        {
            var id = _vfiIds[i];
            if (v._tapePlayer?.Covers(id.ActorKey) != true) { skipped++; continue; }
            if (!v._tapeIndex.TryFind(id, out int j) || j >= v._tapeRest.Length) { missing++; continue; }
            var tv = v._tapeIndex.NodeOf(j).Transform; var tl = cap.T[i];
            float dp = (tv.Origin - tl.Origin).Length();
            float db = MathF.Max(MathF.Max(VfiMax(tv.Basis.X - tl.Basis.X), VfiMax(tv.Basis.Y - tl.Basis.Y)), VfiMax(tv.Basis.Z - tl.Basis.Z));
            if (v._tapeIndex.NodeOf(j).Visible != cap.Vis[i]) visBad++;
            parts++;
            if (dp > maxPos) { maxPos = dp; worstPos = id.ToString(); }
            if (db > maxBasis) { maxBasis = db; worstBasis = id.ToString(); }
            perActor[id.ActorKey] = MathF.Max(perActor.GetValueOrDefault(id.ActorKey), MathF.Max(dp / VfiPosTol, db / VfiBasisTol));
        }
        var bad = perActor.Where(kv => kv.Value > 1f).Select(kv => $"{kv.Key} x{kv.Value:F1}").ToList();
        VfiLog($"A|clock {name} (sample {idx}, {clock:F3} s)|parts compared {parts}, not covered {skipped}, missing in viewer {missing}|max position error {maxPos:F5} m at {worstPos}|max basis error {maxBasis:F5} at {worstBasis}|visibility mismatches {visBad}");
        VfiChk($"A clock {name}: {parts} tape-covered parts match the original (position <= {VfiPosTol} m, basis <= {VfiBasisTol}, visibility equal)", parts > 0 && maxPos <= VfiPosTol && maxBasis <= VfiBasisTol && visBad == 0 && missing == 0, $"maxPos {maxPos:F5} ({worstPos}) maxBasis {maxBasis:F5} ({worstBasis}) vis {visBad} missing {missing} over-tolerance actors [{string.Join("; ", bad)}]");
        // instances (spawned effects and actors)
        var viewerInst = v._tapePlayer?.LiveInstances.Keys.ToHashSet() ?? new HashSet<int>();
        var onlyLive = cap.Inst.Keys.Where(k => !viewerInst.Contains(k)).ToList(); var onlyViewer = viewerInst.Where(k => !cap.Inst.ContainsKey(k)).ToList();
        float instErr = 0;
        foreach (var (id, pos) in cap.Inst) if (viewerInst.Contains(id) && v._tapeSink.Live.TryGetValue(id, out var nd)) instErr = MathF.Max(instErr, (nd.Position - pos).Length());
        VfiChk($"A clock {name}: spawned instances ({cap.Inst.Count} in the original) are the same set and place", onlyLive.Count == 0 && onlyViewer.Count == 0 && instErr <= VfiPosTol, $"only original [{string.Join(",", onlyLive)}] only viewer [{string.Join(",", onlyViewer)}] max position error {instErr:F5}");
        // the car
        int g = idx * 2;
        float carVsGhost = ghost != null && g < ghost.Count ? (v._car.Pos - ghost.PositionAt(g)).Length() : -1f;
        float carVsLive = _vfiCarPos.TryGetValue(g, out var lp) ? (v._car.Pos - lp).Length() : -1f;
        VfiChk($"A clock {name}: viewer car equals the archived ghost sample {g}", carVsGhost >= 0 && carVsGhost <= VfiPosTol, $"{carVsGhost:F5} m");
        if (carVsLive >= 0) VfiLog($"A|clock {name}|viewer car vs the original's car at the tick that recorded ghost sample {g}: {carVsLive:F5} m (informational: the original is read one tick after the recorder)");
        VfiLog($"A|clock {name}|viewer TapePoseHash {v.TapePoseHash():x16} dayClock {v._dayNight.Clock:F4}");
    }

    // ---------------------------------------------------------------- B

    IEnumerable VfiTimeControl(RaceScene v, Ghost ghost)
    {
        double dt = 1.0 / Engine.PhysicsTicksPerSecond;
        float dur = v.ViewerDuration;
        VfiLog($"B|physics {Engine.PhysicsTicksPerSecond} Hz, duration {dur:F3} s");
        v.ViewerLoop = true;
        foreach (float speed in new[] { 0.25f, 1f, 2f })
        {
            v.ViewerSetPaused(true);
            v.ViewerSeek(dur * 0.2f);
            yield return null; yield return null;
            v.ViewerSpeed = speed; v.ViewerSetPaused(false);
            yield return null;
            float c0 = v.ViewerClock; ulong p0 = Engine.GetPhysicsFrames(); int bad = 0; float worst = 0; long steps = 0;
            double dn0 = v._dayNight.Clock;
            for (int f = 0; f < 120; f++)
            {
                yield return null;
                float c1 = v.ViewerClock; ulong p1 = Engine.GetPhysicsFrames();
                long k = (long)(p1 - p0);
                float want = (float)(dt) * speed * k;
                float err = MathF.Abs((c1 - c0) - want);
                float tol = 8e-6f * Math.Max(1, k);      // float accumulation: the clock is a float near 30 s (1.9e-6 s per ulp)
                if (err > tol) { bad++; worst = MathF.Max(worst, err); }
                steps += k; c0 = c1; p0 = p1;
            }
            VfiChk($"B speed {speed}x: the clock advanced by speed*dt per physics tick over 120 frames ({steps} ticks)", bad == 0 && steps > 0, $"{bad} frames off, worst {worst:E2} s, dt {dt:F6}");
            double dn1 = v._dayNight.Clock;
            if (v.TitleGhost.HasDayClock)
            {
                float want = v.TitleGhost.DayClockAt(v.ViewerClock);
                VfiChk($"B speed {speed}x: DayNight clock follows the replay clock", MathF.Abs((float)dn1 - want) < 0.02f, $"DayNight {dn1:F4} vs ghost day clock at {v.ViewerClock:F2} s = {want:F4}; moved {dn1 - dn0:F5} in the run");
            }
            else VfiLog($"B|speed {speed}x|DayNight: the lap has no day clock (HasDayClock false), not checked");
        }
        v.ViewerSpeed = 1f;

        // ---- paused: clock, day clock, pose hash, audio and particles
        v.ViewerSetPaused(true);
        v.ViewerSeek(dur * 0.3f);
        yield return null; yield return null;
        // positive controls: real audio players and particle nodes of the viewer
        var particles = v.FindChildren("*", "", true, false).Where(n => n is CpuParticles3D or GpuParticles3D).ToList();
        v.ViewerSetPaused(false);
        yield return null; yield return null; yield return null;
        var before = new Dictionary<Node, float>();
        foreach (var n in v.FindChildren("*", "", true, false))
            if (n is CpuParticles3D cp) before[n] = (float)cp.SpeedScale; else if (n is GpuParticles3D gp) before[n] = (float)gp.SpeedScale;
        var playingBefore = v.FindChildren("*", "", true, false).Where(n => n is AudioStreamPlayer { Playing: true } or AudioStreamPlayer3D { Playing: true }).ToList();
        var probeV = new CpuParticles3D { Name = "VfiProbeViewer", Amount = 4, Lifetime = 2.0, OneShot = true, Explosiveness = 1f, Emitting = true };
        var probeC = new CpuParticles3D { Name = "VfiProbeControl", Amount = 4, Lifetime = 2.0, OneShot = true, Explosiveness = 1f, Emitting = true, ProcessMode = ProcessModeEnum.Always };
        var wav = new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format8Bits, MixRate = 8000, Data = Enumerable.Repeat((byte)128, 8000 * 6).ToArray() };
        var audV = new AudioStreamPlayer { Name = "VfiAudioViewer", Stream = wav };
        var audC = new AudioStreamPlayer { Name = "VfiAudioControl", Stream = wav, ProcessMode = ProcessModeEnum.Always };
        v.AddChild(probeV); v.AddChild(audV); GetParent().AddChild(probeC); GetParent().AddChild(audC);
        audV.Play(); audC.Play();
        yield return null; yield return null;
        VfiLog($"B|positive controls in the viewer: {particles.Count} particle nodes, {before.Count} at the pause, {playingBefore.Count} audio players playing");
        v.ViewerSetPaused(true);
        yield return null; yield return null; yield return null;      // (the driver can run before the viewer's own frame: the day clock catches up with the last step first)
        float clockP = v.ViewerClock; double dayP = v._dayNight.Clock; ulong hashP = v.TapePoseHash(); int seeksP = v._tapeSeeks;
        double posV0 = audV.GetPlaybackPosition(), posC0 = audC.GetPlaybackPosition();
        for (int f = 0; f < 240; f++) yield return null;
        VfiChk("B paused: the clock stays constant over 240 frames", v.ViewerClock == clockP, $"{clockP:F5} -> {v.ViewerClock:F5}");
        VfiChk("B paused: the DayNight clock stays constant", v._dayNight.Clock == dayP, $"{dayP:F6} -> {v._dayNight.Clock:F6}");
        VfiChk("B paused: the world pose hash stays constant and no re-seek happens", v.TapePoseHash() == hashP && v._tapeSeeks == seeksP, $"{hashP:x16} -> {v.TapePoseHash():x16}, seeks {seeksP} -> {v._tapeSeeks}");
        bool partOk = true; int nonzero = 0;
        foreach (var n in before.Keys.Where(IsInstanceValid)) { float s = n is CpuParticles3D c ? (float)c.SpeedScale : (float)((GpuParticles3D)n).SpeedScale; if (s != 0f) { partOk = false; nonzero++; } }
        VfiChk($"B paused: every particle node of the viewer ({before.Count}) has SpeedScale 0", partOk, $"{nonzero} still running");
        var unpaused = new List<string>(); foreach (var n in playingBefore.Where(IsInstanceValid)) { bool sp = n is AudioStreamPlayer a ? a.StreamPaused : ((AudioStreamPlayer3D)n).StreamPaused; if (!sp) unpaused.Add(v.GetPathTo(n) + " (" + n.GetClass() + ")"); }
        int stillPlaying = unpaused.Count;
        VfiChk($"B paused: every audio player that was playing ({playingBefore.Count}) is StreamPaused", stillPlaying == 0, $"{stillPlaying} not paused: [{string.Join("; ", unpaused)}]");
        if (stillPlaying > 0) { yield return null; int again = 0; foreach (var n in playingBefore.Where(IsInstanceValid)) { bool sp = n is AudioStreamPlayer a ? a.StreamPaused : ((AudioStreamPlayer3D)n).StreamPaused; if (!sp) again++; } VfiLog($"B|one more frame later {again} of them are not paused (UpdateSound runs after the freeze in every ViewerFrame: RaceScene.cs:2379-2380 SetAudioPaused(_state is Paused ...) un-pauses the hind and BTR sounds again)"); }
        VfiChk("B paused: a one-shot particle probe in the viewer is still emitting, its control beside it has finished (engine clock ran)", probeV.Emitting && !probeC.Emitting, $"viewer probe emitting {probeV.Emitting}, control {probeC.Emitting}");
        double posV1 = audV.GetPlaybackPosition(), posC1 = audC.GetPlaybackPosition();
        bool audioClockRuns = posC1 > posC0 + 0.5;
        if (audioClockRuns) VfiChk("B paused: an audio probe in the viewer did not advance, its control did", audV.StreamPaused && Math.Abs(posV1 - posV0) < 0.05, $"viewer probe {posV0:F3} -> {posV1:F3} (StreamPaused {audV.StreamPaused}), control {posC0:F3} -> {posC1:F3}");
        else { VfiLog($"B|audio playback position does not advance under the dummy audio driver (control {posC0:F3} -> {posC1:F3}): only the StreamPaused flag can be checked here"); VfiChk("B paused: the audio probe is StreamPaused", audV.StreamPaused, $"viewer probe pos {posV0:F3} -> {posV1:F3}"); }
        // seek while paused: the world is posed once
        int sk0 = v._tapeSeeks; ulong h0 = v.TapePoseHash();
        float target = dur * 0.6f;
        v.ViewerSeek(target);
        ulong h1 = v.TapePoseHash(); int sk1 = v._tapeSeeks;
        for (int f = 0; f < 20; f++) yield return null;
        VfiChk("B seek while paused: the pose changes at once, exactly one tape seek, then stays", h1 != h0 && sk1 == sk0 + 1 && v.TapePoseHash() == h1 && v._tapeSeeks == sk1 && MathF.Abs(v.ViewerClock - target) < 1e-6f, $"hash {h0:x16} -> {h1:x16} -> {v.TapePoseHash():x16}, seeks {sk0} -> {sk1} -> {v._tapeSeeks}, clock {v.ViewerClock:F4} (target {target:F4})");
        // resume: restored
        v.ViewerSetPaused(false);
        yield return null; yield return null;
        int notRestored = 0; foreach (var (n, was) in before) if (IsInstanceValid(n)) { float s = n is CpuParticles3D c ? (float)c.SpeedScale : (float)((GpuParticles3D)n).SpeedScale; if (s != was) notRestored++; }
        VfiChk("B resume: every particle node has its own SpeedScale back", notRestored == 0, $"{notRestored} differ");
        int stuck = 0; foreach (var n in playingBefore.Where(IsInstanceValid)) { bool sp = n is AudioStreamPlayer a ? a.StreamPaused : ((AudioStreamPlayer3D)n).StreamPaused; if (sp) stuck++; }
        VfiChk("B resume: the audio players that were playing are no longer StreamPaused", stuck == 0 && !audV.StreamPaused, $"{stuck} still paused, probe paused {audV.StreamPaused}");
        for (int f = 0; f < 150; f++) yield return null;
        VfiChk("B resume: the probe then finishes on its own clock (it was frozen, not killed)", !probeV.Emitting, $"viewer probe emitting {probeV.Emitting}");
        probeV.QueueFree(); probeC.QueueFree(); audV.QueueFree(); audC.QueueFree();

        // ---- looping wraps
        v.ViewerSetPaused(true); v.ViewerLoop = true; v.ViewerSpeed = 1f;
        v.ViewerSeek(dur - 0.3f);
        yield return null;
        int skW = v._tapeSeeks;
        v.ViewerSetPaused(false);
        float maxClock = 0; bool wrapped = false;
        for (int f = 0; f < 120; f++) { yield return null; maxClock = MathF.Max(maxClock, v.ViewerClock); if (v.ViewerClock < dur - 1f) wrapped = true; }
        VfiChk("B loop: past the end the clock wraps to the start and the world is re-posed by a tape seek", wrapped && !v.ViewerEnded && v._tapeSeeks > skW, $"clock max {maxClock:F3} now {v.ViewerClock:F3} duration {dur:F3}, seeks {skW} -> {v._tapeSeeks}, ended {v.ViewerEnded}");

        // ---- non-looping: the end is applied once
        v.ViewerSetPaused(true);
        v.ViewerLoop = false;
        v.ViewerSeek(dur - 0.3f);
        yield return null;
        int skE = v._tapeSeeks;
        v.ViewerSetPaused(false);
        float over = 0; int guard = 0;
        while (!v.ViewerEnded && guard++ < 240) { yield return null; over = MathF.Max(over, v.ViewerClock - dur); }
        yield return null;
        ulong hEnd = v.TapePoseHash(); float cEnd = v.ViewerClock; Vector3 carEnd = v._car.Pos;
        for (int f = 0; f < 120; f++) yield return null;
        VfiChk("B non-looping: ViewerEnded, the clock is exactly the duration (no overshoot, no wrap), and it stays", v.ViewerEnded && MathF.Abs(cEnd - dur) < 1e-5f && over <= 1e-5f && v.ViewerClock == cEnd && v.TapePoseHash() == hEnd, $"clock {cEnd:F6} duration {dur:F6} overshoot {over:E2}, 120 frames later clock {v.ViewerClock:F6} hash {hEnd:x16} -> {v.TapePoseHash():x16}");
        VfiChk("B non-looping: no wrap seek happened on the way (the end is applied by stepping, not re-posed)", v._tapeSeeks == skE, $"seeks {skE} -> {v._tapeSeeks}");
        float carVsEnd = ghost != null ? (carEnd - ghost.Sample(dur).Origin).Length() : -1f;
        VfiChk("B non-looping: the final car pose is the ghost's pose at the duration", carVsEnd >= 0 && carVsEnd <= VfiPosTol, $"{carVsEnd:F5} m");
        v.ViewerSetPaused(true);
        v.ViewerSeek(dur); yield return null; yield return null;
        VfiChk("B non-looping: the final world pose equals the pose a seek to the duration gives", v.TapePoseHash() == hEnd, $"{hEnd:x16} vs {v.TapePoseHash():x16}");
        v.ViewerLoop = true;
    }
}

// ---------------------------------------------------------------------------------------------------------------------------------------------------------
// ab: the A/B continuation oracle (plain run / viewer opened mid-lap / the same Disabled freeze without a viewer) and the state fixtures
// ---------------------------------------------------------------------------------------------------------------------------------------------------------
public partial class RaceScene
{
    HashSet<string> _vfiSeedIds = new();
    float _vfiTrigLapClock; readonly Dictionary<string, string> _vfiFacts = new();
    int _vfiLapDoneTick = -1, _vfiTrigTick = -1, _vfiFixStep; bool _vfiFired, _vfiQuit;
    int _vfiMissile0 = -1;
    readonly Dictionary<string, string> _vfiPrev = new();
    StreamWriter _vfiW; StringBuilder _vfiTrace;
    static readonly System.Text.RegularExpressions.Regex VfiSkip = new("^(_vf|_vt|_spike|_vfi)");

    void VfiAbInit()
    {
        foreach (var i in LapArchiveBox.List()) if (i.Openable) _vfiSeedIds.Add(i.Id);
        if (_vfiOut != null) _vfiW = new StreamWriter(_vfiOut) { AutoFlush = true };
        if (_vfiTracePath != null) _vfiTrace = new StringBuilder();
        VfiLog($"ab|variant {_vfiVariant}|fixture {_vfiFixture}|at {_vfiAt}|frames {_vfiN}|tail {_vfiTail}|seed laps [{string.Join(",", _vfiSeedIds)}]|track {_trackId}");
    }

    // ---- the per-tick hash (the WORLDTRACE formula; the viewer's own TraceTick would open the same WORLDTRACE file again, see the report)
    void VfiTraceTick()
    {
        ulong h = 1469598103934665603;
        void Mix(float v) { h = (h ^ (uint)BitConverter.SingleToInt32Bits(v)) * 1099511628211; }
        void V3(Vector3 v) { Mix(v.X); Mix(v.Y); Mix(v.Z); }
        V3(_car.Pos); V3(_car.Vel); V3(_car.AngVel); V3(_car.Rot.X); V3(_car.Rot.Z); Mix(_car.Rpm);
        foreach (var o in _track.Obstacles) { Mix(o.Center.X); Mix(o.Center.Y); Mix(o.Radius); Mix(o.Velocity.X); Mix(o.Velocity.Y); }
        if (_jetsAll != null) h = (h ^ _jetsAll.StateHash()) * 1099511628211;
        _vfiTrace.Append(h.ToString("x16")).Append('\n');
    }

    // ---- fixtures: deterministic states the baseline lap does not exercise, applied in every variant at the same lap clocks

    // ---- the projectile fixture: a Balalaika of the siberia sorties fires one missile (the production FireMissile path, tape hooks included) at a fixed point away from the car
    readonly Dictionary<int, List<(int Id, Vector3 Pos)>> _vfiMissCap = new();
    int _vfiMissLast; float _vfiFireClock = -1f; Vector3 _vfiFireTarget;

    void VfiMissileFire()
    {
        if (_vfiFixStep != 0 || _jetsAll == null || _lapClock < 5f) return;
        int id = _jetsAll.FlyingJet;
        if (id < 0 || !_jetsAll.HasMissile(id, -1)) return;
        var jp = _jetsAll.JetPosition(id).Value; var jv = _jetsAll.JetVelocity(id).Value;
        var fwd = new Vector3(jv.X, 0, jv.Z).Normalized();
        _vfiFireTarget = jp + fwd * 700f + Vector3.Down * 30f;      // far ahead of the jet, nowhere near the car's road
        var tgt = _vfiFireTarget;
        bool ok = _jetsAll.FireMissile(id, -1, () => tgt);
        _vfiFixStep = 1; _vfiFireClock = _lapClock;
        VfiLog($"fixture|missile fired by jet {id} (production FireMissile) at lap clock {_lapClock:F2}: {ok}; jet at ({jp.X:F0},{jp.Y:F0},{jp.Z:F0}) car at ({_car.Pos.X:F0},{_car.Pos.Z:F0}), target {tgt.X:F0},{tgt.Y:F0},{tgt.Z:F0}, jets' missiles in flight {_jetsAll.Missiles.Count}");
        foreach (var m in _jetsAll.Missiles) m.Exploded += (p, d) => VfiLog($"fixture|missile exploded at ({p.X:F0},{p.Y:F0},{p.Z:F0}), {d:F0} m from its target, lap clock {_lapClock:F2}");
    }

    void VfiMissileCapture()
    {
        if (_tapeRec == null || !_tapeOn) return;
        int n = _tapeRec.Samples;
        if (n > _vfiMissLast)
        {
            var l = new List<(int, Vector3)>();
            foreach (var (node, inst) in _tapeInst) if (inst.Kind == "missile" && IsInstanceValid(node)) l.Add((inst.Id, node.Position));
            if (l.Count > 0 || _vfiMissCap.Count > 0) _vfiMissCap[n - 1] = l;
        }
        _vfiMissLast = n;
    }

    void VfiFixtureTick()
    {
        if (_state != State.Racing) return;
        float t = _lapClock;
        switch (_vfiFixture)
        {
            case "missile": VfiMissileFire(); break;
            case "boost":
                if (_vfiFixStep == 0 && t >= _vfiAt - 8f) { _vfiFixStep = 1; StartBoost(10f); VfiLog($"fixture|boost started at lap clock {t:F2}: boostLeft {_boostLeft:F1}, fuel {_tank.Litres:F1} l"); }
                break;
            case "damage":
                {
                    // three hard hits (front left, front right, rear) 0.6 s apart: the bumpers and a lamp should come loose or off
                    float[] at = { _vfiAt - 8f, _vfiAt - 7.4f, _vfiAt - 6.8f };
                    Vector3[] where = { new(-0.7f, 0.3f, -2.0f), new(0.7f, 0.3f, -2.0f), new(0f, 0.3f, 2.0f) };
                    Vector3[] dir = { new(0, 0, 1), new(0, 0, 1), new(0, 0, -1) };
                    if (_vfiFixStep < 3 && t >= at[_vfiFixStep])
                    {
                        int k = _vfiFixStep++;
                        _carVis.Damage?.Hit(16f, where[k], dir[k], _car, _fx);
                        VfiLog($"fixture|damage hit {k + 1} at lap clock {t:F2}: dents {_carVis.Damage?.Dents}, damaged {_carVis.Damage?.IsDamaged}");
                    }
                    break;
                }
        }
    }

    bool VfiTriggerDue()
    {
        if (_state != State.Racing && _vfiFixture != "mission") return false;
        switch (_vfiFixture)
        {
            case "missile":
                {
                    int serial = Convert.ToInt32(RfStatic(typeof(Missile), "_serial"), CultureInfo.InvariantCulture);
                    if (_vfiMissile0 < 0) _vfiMissile0 = serial;
                    if (serial != _vfiMissile0 && _vfiTrigTick < 0) { _vfiTrigTick = (int)_vfiTicks; VfiLog($"fixture|missile launched at lap clock {_lapClock:F2} (tick {_vfiTicks})"); }
                    return _vfiTrigTick >= 0 && _vfiTicks >= _vfiTrigTick + 60;
                }
            case "mission":
                if (_planStop && _vfiTrigTick < 0) { _vfiTrigTick = (int)_vfiTicks; VfiLog($"fixture|plan stop begins at tick {_vfiTicks}, plan clock {_planClock:F2}, laps left {_norms.Left}"); }
                return _vfiTrigTick >= 0 && _vfiTicks >= _vfiTrigTick + 30;
            default: return _lapClock >= _vfiAt;
        }
    }

    void VfiAbTick()
    {
        _vfiTicks++;
        if (_vfiQuit) return;
        if (_vfiTrace != null) VfiTraceTick();
        if (_vfiFixture == "missile" && !_vfiFired) VfiMissileCapture();      // (before the fixture's own tick: the recorder sampled at the end of the previous tick, before a missile fired now existed)
        VfiFixtureTick();
        if (_vfiTicks % 30 == 0 && _vfiW != null) VfiDigestWrite();
        if (_vfiTicks % 1200 == 0) VfiLog($"ab|progress|tick {_vfiTicks}|state {_state}|lap clock {_lapClock:F1}|laps {_timer.LapsDone}|progress {_timer.Progress}/{_track.Center.Length}|speed {_car.Speed * 3.6f:F0} km/h|failed {_failed} {_failReason}");
        if (!_vfiFired && _vfiVariant != "plain" && VfiTriggerDue())
        {
            _vfiFired = true;
            VfiLog($"ab|trigger at tick {_vfiTicks}, lap clock {_lapClock:F3}, state {_state}");
            _vfiTrigLapClock = _lapClock; VfiLiveFacts();
            VfiStart(_vfiVariant == "viewer" ? VfiAbViewer() : VfiAbFreeze());
        }
        if (_timer.LapsDone >= 1 && _vfiLapDoneTick < 0) { _vfiLapDoneTick = (int)_vfiTicks; VfiLog($"ab|lap done at tick {_vfiTicks}, lap time {VfiF(_timer.LastLap)}"); }
        int tail = _vfiFixture == "mission" ? Math.Max(_vfiTail, 900) : _vfiTail;
        if (_vfiLapDoneTick >= 0 && !_vfiFired && _vfiVariant != "plain" && _vfiTicks == _vfiLapDoneTick + tail + 600) VfiChk($"AB fixture {_vfiFixture}: the trigger fired before the end of the run", false, "the lap ended and the trigger never fired");
        if (_vfiLapDoneTick >= 0 && _vfiTicks >= _vfiLapDoneTick + tail && (_vfiVariant == "plain" || _vfiFired || _vfiTicks > _vfiLapDoneTick + tail + 600))
        {
            var box = LapArchiveBox;
            var info = box.List().FirstOrDefault(i => i.Openable && !_vfiSeedIds.Contains(i.Id));
            var src = info == null ? null : box.Open(info.Id, out _);
            if (src == null) { if (_vfiTicks > _vfiLapDoneTick + tail + 6000) { VfiLog("FAIL|ab|no archived lap after the line"); Finish(); } return; }
            VfiLog($"ab|final|ghostSha256|{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(src.GhostBytes.Span))}");
            VfiLog($"ab|final|ghostBytes|{src.GhostBytes.Length}");
            VfiLog($"ab|final|tapeSha256|{(src.HasTape ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(src.TapeBytes.Span)) : "none")}");
            VfiLog($"ab|final|tapeBytes|{(src.HasTape ? src.TapeBytes.Length : 0)}");
            VfiLog($"ab|final|lapTime|{VfiF(_timer.LastLap)}");
            VfiLog($"ab|final|lapSeconds|{src.Meta.LapSeconds:R}");
            VfiLog($"ab|final|quitTick|{_vfiTicks}");
            Finish();
        }
        void Finish()
        {
            _vfiQuit = true;
            if (_vfiTrace != null) File.WriteAllText(_vfiTracePath, _vfiTrace.ToString());
            _vfiW?.Dispose();
            VfiLog($"SUMMARY|pass={_vfiPass}|fail={_vfiFail}");
            GetTree().Quit();
        }
    }


    static (int Loose, int Off, int Parts) VfiDamageParts(RaceScene s)
    {
        var dmg = s._carVis?.Damage; if (dmg == null) return (-1, -1, -1);
        var parts = typeof(CarDamage).GetField("_parts", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(dmg) as IList; if (parts == null) return (-1, -1, -1);
        int loose = 0, off = 0;
        foreach (var p in parts) { int st = Convert.ToInt32(p.GetType().GetField("State").GetValue(p)); if (st == 1) loose++; else if (st == 2) off++; }
        return (loose, off, parts.Count);
    }

    static int VfiTapeMissileNodes(Node root) { int n = root is Node3D && root.HasMeta(WorldTapeConst.KindMeta) && (string)root.GetMeta(WorldTapeConst.KindMeta) == "missile" ? 1 : 0; foreach (var c in root.GetChildren()) n += VfiTapeMissileNodes(c); return n; }

    static int VfiMissileNamedNodes(Node root) { int n = root.Name.ToString().Contains("Missile") ? 1 : 0; foreach (var c in root.GetChildren()) n += VfiMissileNamedNodes(c); return n; }

    static int VfiMissileNodes(Node root) { int n = root is Missile ? 1 : 0; foreach (var c in root.GetChildren()) n += VfiMissileNodes(c); return n; }

    void VfiLiveFacts()
    {
        var dp = VfiDamageParts(this);
        _vfiFacts["boostLeft"] = VfiF(_boostLeft); _vfiFacts["power"] = VfiF(_car.PowerScale); _vfiFacts["fuel"] = VfiF(_tank.Litres);
        _vfiFacts["loose"] = dp.Loose.ToString(); _vfiFacts["off"] = dp.Off.ToString(); _vfiFacts["dents"] = (_carVis?.Damage?.Dents ?? -1).ToString();
        _vfiFacts["missiles"] = VfiMissileNodes(this).ToString(); _vfiFacts["planStop"] = _planStop.ToString();
        VfiLog($"fixture|live facts at the trigger ({_vfiFixture}): {string.Join(", ", _vfiFacts.Select(kv => kv.Key + "=" + kv.Value))}");
    }

    /// <summary>With the viewer at the lap clock the live scene was frozen at: is the fixture's state in the replay?</summary>
    IEnumerable VfiFixtureInViewer(RaceScene v, float dur)
    {
        if (_vfiFixture == "none") yield break;
        v.ViewerSetPaused(true);
        float at = MathF.Min(_vfiTrigLapClock, dur);
        v.ViewerSeek(at);
        for (int i = 0; i < 3; i++) yield return null;
        switch (_vfiFixture)
        {
            case "boost":
                {
                    float live = float.Parse(_vfiFacts["boostLeft"], CultureInfo.InvariantCulture);
                    bool cov = v._tapePlayer?.Covers("None:PlayerPower") == true;
                    VfiChk("FX boost: the tape covers PlayerPower", cov, "");
                    VfiChk("FX boost: the viewer shows the boost running (boostLeft near the original)", cov && MathF.Abs(v._boostLeft - live) < 0.5f && v._boostLeft > 0, $"viewer boostLeft {v._boostLeft:F2} at {at:F2} s vs original {live:F2}; PowerScale viewer {v._car.PowerScale:F2} vs original {_vfiFacts["power"]}; golden {v._carVis.Golden}");
                    break;
                }
            case "damage":
                {
                    var dp = VfiDamageParts(v);
                    int lo = int.Parse(_vfiFacts["loose"]), of = int.Parse(_vfiFacts["off"]);
                    VfiChk("FX damage: the original really had loose/detached parts", lo + of > 0, $"original loose {lo} off {of} of {VfiDamageParts(this).Parts} parts");
                    VfiChk("FX damage: the viewer shows the same loose and detached parts", dp.Loose == lo && dp.Off == of, $"viewer loose {dp.Loose} off {dp.Off}; original loose {lo} off {of}; viewer IsDamaged {v._carVis.Damage?.IsDamaged}");
                    break;
                }
            case "missile":
                {
                    int liveM0 = int.Parse(_vfiFacts["missiles"]);
                    VfiChk("FX missile: a missile is in flight in the ORIGINAL (live) scene at the suspend", liveM0 > 0 && _vfiFireClock >= 0, $"{liveM0} Missile nodes, fired at lap clock {_vfiFireClock:F2}, suspended at {_vfiTrigLapClock:F2}");
                    var idxs = _vfiMissCap.Where(kv => kv.Value.Count > 0).Select(kv => kv.Key).OrderBy(i => i).ToList();
                    VfiChk("FX missile: the tape recorded the projectile (original instances captured at tape samples)", idxs.Count > 0 && v.ViewerHasTape, $"{idxs.Count} tape samples with a missile instance, {(idxs.Count > 0 ? idxs[0] + ".." + idxs[^1] : "-")}; viewer tape {v.ViewerHasTape}");
                    // (1) playback: the viewer's replayed instances at each of those clocks against the original's
                    int bad = 0, countBad = 0; float worst = 0; int compared = 0;
                    foreach (int i in idxs)
                    {
                        v.ViewerSeek(i / WorldTapeConst.TapeRate);
                        var orig = _vfiMissCap[i];
                        var inst = v._tapePlayer?.LiveInstances.Keys.Where(k => v._tapeSink.Live.ContainsKey(k) && v._tapeSink.Live[k].HasMeta(WorldTapeConst.KindMeta) && (string)v._tapeSink.Live[k].GetMeta(WorldTapeConst.KindMeta) == "missile").ToList() ?? new List<int>();
                        if (inst.Count != orig.Count) countBad++;
                        string per = "";
                        foreach (var (id, pos) in orig) { if (v._tapeSink.Live.TryGetValue(id, out var nd)) { compared++; float e = (nd.Position - pos).Length(); worst = MathF.Max(worst, e); if (e > VfiPosTol) bad++; per += $" id{id} err {e:F5}"; } else { bad++; per += $" id{id} MISSING in viewer (viewer ids [{string.Join(",", inst)}])"; } }
                        VfiLog($"fixture|missile sample {i} ({i / WorldTapeConst.TapeRate:F3} s): original {orig.Count}, viewer {inst.Count}:{per}");
                    }
                    VfiChk($"FX missile (1): the viewer replays the projectile in flight at the same clocks: count equal and position within {VfiPosTol} m at every one of {idxs.Count} tape samples", idxs.Count > 0 && countBad == 0 && bad == 0, $"{compared} positions compared, worst {worst:F5} m, {bad} out of tolerance or missing, {countBad} samples with another count");
                    VfiLog($"fixture|missile original positions: {string.Join(" ", idxs.Take(4).Select(i => $"{i}:({_vfiMissCap[i][0].Pos.X:F1},{_vfiMissCap[i][0].Pos.Y:F1},{_vfiMissCap[i][0].Pos.Z:F1})"))}");
                    // (3) seek / wrap clear and respawn
                    int Count() => v._tapePlayer?.LiveInstances.Keys.Count(k => v._tapeSink.Live.TryGetValue(k, out var nd) && nd.HasMeta(WorldTapeConst.KindMeta) && (string)nd.GetMeta(WorldTapeConst.KindMeta) == "missile") ?? -1;
                    int MissileNodes() => VfiTapeMissileNodes(v);
                    if (idxs.Count > 0)
                    {
                        int mid = idxs[idxs.Count / 2];
                        v.ViewerSeek(idxs[0] / WorldTapeConst.TapeRate - 3f); int before = Count(); int nodesBase = MissileNodes();
                        v.ViewerSeek(mid / WorldTapeConst.TapeRate); int during = Count(); int nodesDuring = MissileNodes();
                        v.ViewerSeek(0f); int atStart = Count(); int nodesStart = MissileNodes();
                        v.ViewerSeek(mid / WorldTapeConst.TapeRate); int again = Count();
                        VfiChk("FX missile (3): seek before the launch has none, into the flight respawns it, back to the start clears it, into the flight respawns it again", before == 0 && during == 1 && atStart == 0 && again == 1 && nodesStart == nodesBase && nodesDuring > nodesBase, $"counts before {before}, in flight {during}, start {atStart}, again {again}; tape-marked missile nodes of the viewer: before the launch {nodesBase}, in flight {nodesDuring}, after seek to 0 {nodesStart}");
                        // loop wrap: play from the flight to the end with looping on; after the wrap the world is at the start with no projectile
                        v.ViewerSeek(mid / WorldTapeConst.TapeRate); v.ViewerLoop = true; v.ViewerSpeed = 1f; v.ViewerSeek(v.ViewerDuration - 0.2f);
                        int atEnd = Count();
                        v.ViewerSetPaused(false);
                        bool wrapped = false; for (int f = 0; f < 120 && !wrapped; f++) { yield return null; if (v.ViewerClock < 5f) wrapped = true; }
                        yield return null;
                        VfiChk("FX missile (3): the loop wrap re-poses the world with no leftover projectile", wrapped && Count() == 0 && MissileNodes() == nodesBase, $"wrapped {wrapped} clock {v.ViewerClock:F2}; projectiles near the end {atEnd}, after the wrap {Count()}, Missile-named nodes {MissileNodes()}");
                        v.ViewerSetPaused(true);
                    }
                    int liveM = int.Parse(_vfiFacts["missiles"]); int viewerP = v._tapeFx.Projectiles; int viewerNodes = VfiMissileNodes(v);
                    VfiLog($"fixture|missile: original had {liveM} Missile nodes in flight at lap clock {at:F2}; the viewer has {viewerP} replayed projectiles (fx) and {viewerNodes} live Missile nodes");
                    if (liveM > 0) VfiLog("fixture|missile: (fx.Projectiles counts tank/effect shells, not tape missile instances; the instance checks above are the evidence)");
                    else VfiLog("NOTCOVERED|FX missile: the original had no projectile in flight at the trigger (the fixture did not hold)");
                    break;
                }
            case "shell":
                {
                    float best = -1; int bestN = 0; var found = new List<string>();
                    for (float t = 30f; t <= MathF.Min(dur, 70f); t += 0.25f)
                    {
                        v.ViewerSeek(t);
                        int n = v._tapeFx.Projectiles;
                        if (n > 0) { found.Add($"{t:F2}s:{n}"); if (n > bestN) { bestN = n; best = t; } }
                    }
                    VfiLog($"fixture|shell: replayed projectiles in flight at tape clocks 30-70 s (every 0.25 s): [{string.Join(" ", found)}]");
                    { if (bestN > 0) VfiChk("FX shell: the tape replays a projectile (tank shell) in flight during the bonus tank battle", true, $"max {bestN} at {best:F2} s"); else VfiLog($"NOTCOVERED|FX shell: no projectile in flight at any scanned clock of the bonus tank battle ({found.Count} of the scanned clocks); a projectile replay is NOT verified"); }
                    if (best >= 0) { v.ViewerSeek(best); for (int i = 0; i < 3; i++) yield return null; VfiLog($"fixture|shell: at {best:F2} s the viewer has {v._tapeFx.Projectiles} projectiles, {v._tapeFx.Nodes} fx nodes"); }
                    break;
                }
            case "mission":
                VfiLog($"NOTCOVERED|FX mission: plan-stop timers (planStop {_vfiFacts["planStop"]}) are live-only state; the tape has no mission channel, the viewer shows the lap only. The live state is compared bit for bit by the digest.");
                break;
        }
        v.ViewerSetPaused(false);
    }

    // ---- the viewer variant: open on the seed's lap, drive it through every control, close
    IEnumerable VfiAbViewer()
    {
        int frames = 0;
        var box = LapArchiveBox;
        var info = box.List().FirstOrDefault(i => i.Openable && _vfiSeedIds.Contains(i.Id));
        var lap = info == null ? null : box.Open(info.Id, out _);
        if (lap == null) { VfiChk("AB viewer: a seeded lap exists", false, "no openable seed lap"); yield break; }
        int treeNodes0 = GetTree().GetNodeCount(), orphans0 = (int)Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount), missileNodes0 = VfiMissileNamedNodes(GetTree().Root), missileLive0 = VfiMissileNodes(this);
        var session = OpenReplayViewer(lap, out string err);
        if (session == null) { VfiChk("AB viewer opens", false, err); yield break; }
        while (session.State == ReplayViewerState.Building && frames < 60 * 180) { frames++; yield return null; }
        VfiChk("AB viewer reaches Playing", session.State == ReplayViewerState.Playing, $"state {session.State} after {frames} frames, error {session.Error}");
        if (session.State == ReplayViewerState.Playing)
        {
            var v = session.Scene; float dur = v.ViewerDuration;
            foreach (var x in VfiFixtureInViewer(v, dur)) { frames++; yield return x; }
            IEnumerable Wait(int n) { for (int i = 0; i < n; i++) { frames++; yield return null; } }
            foreach (var x in Wait(60)) yield return x;
            v.ViewerSeek(5f); foreach (var x in Wait(10)) yield return x;
            v.ViewerSpeed = 2f; foreach (var x in Wait(60)) yield return x;
            v.ViewerLoop = true; v.ViewerSeek(dur - 0.5f); foreach (var x in Wait(180)) yield return x;      // loops over the end
            v.ViewerSetPaused(true); v.ViewerSeek(dur * 0.5f); foreach (var x in Wait(30)) yield return x;
            v.ViewerSeek(dur * 0.9f); foreach (var x in Wait(10)) yield return x;
            v.ViewerSpeed = 0.25f; v.ViewerSetPaused(false); foreach (var x in Wait(30)) yield return x;
            v.ViewerSpeed = 1f; v.ViewerLoop = false; v.ViewerSeek(dur - 0.3f); foreach (var x in Wait(120)) yield return x;      // plays to the end
            VfiLog($"ab|viewer ended {v.ViewerEnded} clock {v.ViewerClock:F3}/{dur:F3}");
            v.ViewerSeek(0f); foreach (var x in Wait(20)) yield return x;
        }
        session.Close();
        int guard = 0;
        while (session.State != ReplayViewerState.Closed && guard++ < 1200) { frames++; yield return null; }
        VfiLog($"ab|viewerFrames|{frames}");
        yield return null; yield return null;
        {
            int treeNodes1 = GetTree().GetNodeCount(), orphans1 = (int)Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount), missileNodes1 = VfiMissileNamedNodes(GetTree().Root), missileLive1 = VfiMissileNodes(this);
            VfiLog($"ab|nodes|tree {treeNodes0} -> {treeNodes1}|orphans {orphans0} -> {orphans1}|Missile-named nodes in the whole tree {missileNodes0} -> {missileNodes1}|live Missile nodes {missileLive0} -> {missileLive1}");
            VfiChk("AB viewer exit: no node, orphan or Missile node is left over in the tree (+2 live frames' own churn allowed: none seen)", treeNodes1 <= treeNodes0 + 2 && orphans1 <= orphans0 && missileNodes1 <= missileNodes0 + 1, $"tree {treeNodes0} -> {treeNodes1}, orphans {orphans0} -> {orphans1}, Missile-named {missileNodes0} -> {missileNodes1}");
        }
        VfiChk("AB viewer closed, live restored", session.State == ReplayViewerState.Closed && ProcessMode == ProcessModeEnum.Inherit && Visible && ActiveReplayViewers.Count == 0, $"state {session.State} mode {ProcessMode} visible {Visible}");
    }

    // ---- the freeze variant: the same Disabled suspension for the same number of driver frames, no viewer
    IEnumerable VfiAbFreeze()
    {
        var undo = VfFreezeLive(true);
        for (int i = 0; i < _vfiN; i++) yield return null;
        undo();
        VfiLog($"ab|freezeFrames|{_vfiN}");
    }

    // ---- the digest: delta coded "tick<TAB>key<TAB>value" lines; the keys are the scene's own fields plus the car, tank, health, plan, timer, day, weather, damage
    void VfiDigestWrite()
    {
        if (_vfiW == null) return;
        var d = VfiDigest();
        foreach (var (k, v) in d)
            if (!_vfiPrev.TryGetValue(k, out var old) || old != v) { _vfiPrev[k] = v; _vfiW.Write(_vfiTicks.ToString(CultureInfo.InvariantCulture)); _vfiW.Write('\t'); _vfiW.Write(k); _vfiW.Write('\t'); _vfiW.Write(v); _vfiW.Write('\n'); }
    }

    SortedDictionary<string, string> VfiDigest()
    {
        var d = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var s = VfCapture();
        foreach (var (k, v) in s.Fields) d["cap." + k] = v;
        foreach (var (g, h) in s.Groups) d["grp." + g] = h.ToString("x16");
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        VfiDump("scene.", this, d, 0, seen, true);
        var named = new (string, object)[] { ("car", _car), ("tank", _tank), ("health", _health), ("norms", _norms), ("timer", _timer), ("dayNight", _dayNight), ("weather", _weather), ("ap", _ap), ("damage", _carVis?.Damage), ("kgb", _kgb), ("rescue", _rescue), ("truck", _truck) };
        foreach (var (n, o) in named) if (o != null) VfiDump(n + ".", o, d, 0, seen, false);
        VfiStatics(d);
        d["dayNight.ClimateAllowance"] = VfiF(_dayNight?.ClimateAllowance ?? 0f); d["scene.ClimateAllowance()"] = VfiF(ClimateAllowance());
        d["static.Missile._serial"] = Convert.ToString(RfStatic(typeof(Missile), "_serial"), CultureInfo.InvariantCulture);
        int ms = 0;
        void Walk(Node n)
        {
            if (n is Missile m) { VfiDump($"missile.{ms}.", m, d, 0, seen, false); d[$"missile.{ms}.global"] = $"{m.GlobalPosition.X:R},{m.GlobalPosition.Y:R},{m.GlobalPosition.Z:R}"; ms++; }
            foreach (var c in n.GetChildren()) Walk(c);
        }
        foreach (var c in GetChildren()) Walk(c);
        d["missile.count"] = ms.ToString();
        if (_jetsAll != null) { d["jets.missilesInFlight"] = _jetsAll.Missiles.Count.ToString(); d["jets.stateHash"] = _jetsAll.StateHash().ToString("x16"); }
        return d;
    }


    /// <summary>Process-wide statics a nested world writes (docs/player-replay-phase2-proposal.md "One suspend/restore helper"): the plan ladders and factor, glow, wetness, the see-through flags, radar and glow lists.</summary>
    static void VfiStatics(SortedDictionary<string, string> d)
    {
        foreach (var (type, member) in new (Type, string)[] { (typeof(NormPlan), "Ladder"), (typeof(NormPlan), "BaseLadder"), (typeof(NormPlan), "Factor"), (typeof(NormPlan), "BronzeAt"), (typeof(NormPlan), "SilverAt"), (typeof(NightGlow), "Level"), (typeof(NightGlow), "Glows"), (typeof(NightGlow), "Lamps"), (typeof(RadarHead), "All"), (typeof(Surfaces), "Wetness"), (typeof(SeeThrough), "Off"), (typeof(FadeUnder), "Off"), (typeof(RaceScene), "_trackId") })
        {
            object v; try { v = RfStatic(type, member); } catch { v = "ERR"; }
            string key = $"static.{type.Name}.{member}";
            if (v == null) d[key] = "null";
            else if (v is Array arr) d[key] = VfiHashSeq(arr).ToString("x16") + "/" + arr.Length;
            else if (VfiPrim(v)) d[key] = VfiFmt(v);
            else if (v is ICollection c) d[key] = "n" + c.Count;
            else d[key] = v.GetType().Name;
        }
    }

    static string VfiFmt(object v) => v switch
    {
        float f => f.ToString("R", CultureInfo.InvariantCulture),
        double f => f.ToString("R", CultureInfo.InvariantCulture),
        Vector3 p => $"{p.X:R},{p.Y:R},{p.Z:R}",
        Vector2 p => $"{p.X:R},{p.Y:R}",
        Godot.Quaternion q => $"{q.X:R},{q.Y:R},{q.Z:R},{q.W:R}",
        Color c => $"{c.R:R},{c.G:R},{c.B:R},{c.A:R}",
        string s => s.Length > 80 ? s[..80] : s,
        IFormattable fm => fm.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString(),
    };

    static bool VfiPrim(object v) => v is float or double or int or uint or long or ulong or short or ushort or byte or sbyte or bool or string or Enum or Vector2 or Vector3 or Godot.Quaternion or Color;

    static ulong VfiHashSeq(IEnumerable e)
    {
        ulong h = 1469598103934665603;
        foreach (var x in e)
        {
            switch (x)
            {
                case float f: h = (h ^ (uint)BitConverter.SingleToInt32Bits(f)) * 1099511628211; break;
                case Vector3 p: foreach (var c in new[] { p.X, p.Y, p.Z }) h = (h ^ (uint)BitConverter.SingleToInt32Bits(c)) * 1099511628211; break;
                case Vector2 p: foreach (var c in new[] { p.X, p.Y }) h = (h ^ (uint)BitConverter.SingleToInt32Bits(c)) * 1099511628211; break;
                default: h = (h ^ (ulong)(x?.GetHashCode() ?? 0)) * 1099511628211; break;
            }
        }
        return h;
    }

    void VfiDump(string prefix, object o, SortedDictionary<string, string> d, int depth, HashSet<object> seen, bool declaredOnly)
    {
        if (o == null || depth > 2 || !seen.Add(o)) return;
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | (declaredOnly ? BindingFlags.DeclaredOnly : 0);
        foreach (var f in o.GetType().GetFields(flags))
        {
            string name = f.Name.StartsWith('<') ? f.Name[1..f.Name.IndexOf('>')] : f.Name;
            if (VfiSkip.IsMatch(name)) continue;
            object v; try { v = f.GetValue(o); } catch { continue; }
            if (v == null) continue;
            if (VfiPrim(v)) { d[prefix + name] = VfiFmt(v); continue; }
            if (v is GodotObject) continue;
            var t = v.GetType();
            if (v is Array arr && arr.Length > 0 && (arr.GetValue(0) is float or Vector3 or Vector2 or int or bool)) { d[prefix + name + "#hash"] = VfiHashSeq(arr).ToString("x16") + "/" + arr.Length; continue; }
            if (v is IList list && !(v is Array))
            {
                int n = list.Count;
                if (n > 0 && (list[0] is float or Vector3 or Vector2 or int)) { d[prefix + name + "#hash"] = VfiHashSeq(list).ToString("x16") + "/" + n; continue; }
                if (n > 0 && list[0] != null && !list[0].GetType().IsValueType && list[0].GetType().Namespace?.StartsWith("CCCPRacing") == true && depth < 2)
                {
                    d[prefix + name + ".count"] = n.ToString();
                    for (int i = 0; i < Math.Min(n, 300); i++) VfiDump($"{prefix}{name}[{i}].", list[i], d, depth + 1, seen, false);
                }
                else if (!(list is Array)) d[prefix + name + ".count"] = n.ToString();
                continue;
            }
            if (t.Namespace?.StartsWith("CCCPRacing") == true && !t.IsValueType && depth < 1) VfiDump(prefix + name + ".", v, d, depth + 1, seen, false);
        }
    }
}
