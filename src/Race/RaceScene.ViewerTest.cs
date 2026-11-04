using System;
using System.Collections;
using System.Linq;
using CCCPRacing.Replay;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// Test scenarios of the replay viewer session (flag --viewer-test=NAME in a child game; PhysicsTest.ViewerTest.cs judges the VIEWERTEST lines).
/// The script runs in a driver beside the scene (ProcessMode Always) because the live scene is Disabled while the viewer is open.
/// </summary>
public partial class RaceScene
{
    string _vtMode; bool _vtParsed; ViewerFreezeDriver _vtDriver;

    static void VtLog(string s) => GD.Print("VIEWERTEST: " + s);

    void ViewerTestTick(float dt)
    {
        if (!_vtParsed)
        {
            _vtParsed = true;
            if (InstructionTrack != null || ViewerMode) return;
            foreach (var a in OS.GetCmdlineUserArgs())
                if (a.StartsWith("--viewer-test=")) _vtMode = a["--viewer-test=".Length..];
        }
        if (_vtMode == null || _vtDriver != null || _state != State.Racing) return;
        _vtDriver = new ViewerFreezeDriver { Name = "ViewerTestDriver", ProcessMode = ProcessModeEnum.Always };
        GetParent().CallDeferred(Node.MethodName.AddChild, _vtDriver);
        IEnumerable script = _vtMode switch { "smoke" => VtSmoke(), _ => VtFail("unknown scenario " + _vtMode) };
        _vtDriver.Start(script);
        _vtDriver.OnDone = () => { VtLog("script done"); GetTree().Quit(); };
    }

    static IEnumerable VtFail(string why) { VtLog("FAIL " + why); yield break; }

    /// <summary>Waits for the first completed lap to be in the archive (the child runs an autodrive lap), returns its source.</summary>
    IEnumerable VtWaitLap(Action<LapSource> got)
    {
        int waited = 0;
        while (true)
        {
            var box = _lapArchiveUsed;
            var info = box?.List().FirstOrDefault(i => i.Openable);
            if (info != null) { var src = box.Open(info.Id); if (src != null) { got(src); yield break; } }
            if (++waited > 60 * 400) { VtLog("FAIL no lap reached the archive"); GetTree().Quit(3); yield break; }
            yield return null;
        }
    }

    IEnumerable VtSmoke()
    {
        LapSource lap = null;
        foreach (var _ in VtWaitLap(l => lap = l)) yield return null;
        VtLog($"lap {lap.Meta.Id} {lap.Meta.LapSeconds:F2} s tape={lap.HasTape}");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var session = OpenReplayViewer(lap, out string err);
        if (session == null) { VtLog("FAIL open: " + err); yield break; }
        int frames = 0; double worst = 0; ulong last = Time.GetTicksUsec();
        while (session.State == ReplayViewerState.Building && frames++ < 60 * 120)
        {
            yield return null;
            ulong now = Time.GetTicksUsec(); worst = Math.Max(worst, (now - last) / 1000.0); last = now;
        }
        VtLog($"built: state {session.State} in {sw.ElapsedMilliseconds} ms, {frames} frames, worst frame {worst:F1} ms, steps {session.Scene?.ViewerBuildSteps}, error {session.Error}");
        if (session.State != ReplayViewerState.Playing) { session.Close(); yield break; }
        var v = session.Scene;
        for (int i = 0; i < 120; i++) yield return null;
        VtLog($"playing: clock {v.ViewerClock:F2} of {v.ViewerDuration:F2}, tape {v.ViewerHasTape}");
        v.ViewerSeek(10f); yield return null; yield return null;
        VtLog($"after seek 10: clock {v.ViewerClock:F2}");
        v.ViewerSetPaused(true);
        float c = v.ViewerClock;
        for (int i = 0; i < 30; i++) yield return null;
        VtLog($"paused: clock {c:F3} -> {v.ViewerClock:F3}");
        v.ViewerSetPaused(false);
        CloseReplayViewer();
        int guard = 0;
        while (session.State != ReplayViewerState.Closed && guard++ < 600) yield return null;
        VtLog($"closed: state {session.State}, live mode {ProcessMode}, visible {Visible}, active viewers {ActiveReplayViewers.Count}");
    }
}
