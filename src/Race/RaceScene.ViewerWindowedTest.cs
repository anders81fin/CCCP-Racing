using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using CCCPRacing.Replay;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// Windowed replay-viewer scenarios (flag --viewer-windowed=NAME in a child game started from PhysicsTest.tscn; PhysicsTest.ViewerWindowed.cs launches and judges them).
/// Ownership part (this file): the production PixelFrame + LoadingCover path, real input events through the window's viewport, and the
/// track-loading card, against the session owner (RaceScene.ReplayViewer.cs). The GPU/frame part is RaceScene.ViewerWindowedGpu.cs.
/// The child starts as PhysicsTest.tscn, adds a driver under the root and changes to the real Main.tscn (PixelFrame); the driver survives the change
/// and runs the script beside the live scene (it keeps running while the live scene is Disabled). Line format: "VIEWERWIN: tag|key=value|...".
/// Arguments: --vl-fx=DIR (fixture laps, DIR/TRACK), --vw-media=DIR (PNG output, never overwritten), --vw-track=TRACK, --vw-lap=TRACK (the lap to open).
/// Injected events carry Device 0x7E57 so a real key or pad press that reaches the window is told apart and counted as foreign.
/// </summary>
public partial class ViewerWindowedDriver : Node
{
    IEnumerator _script;
    bool _moved;
    public Action OnDone;
    public Action PerFrame;

    public const int Sentinel = 0x7E57;
    public sealed record Arrival(ulong Frame, string What, bool Sentinel, bool Restored);
    public readonly List<Arrival> Arrivals = new();
    public int SentinelArrivals, Foreign, ForeignMotion;
    public Func<bool> RestoredNow = () => false;

    public void Start(IEnumerable script) { _script = script.GetEnumerator(); }

    public override void _Input(InputEvent e)
    {
        // Last in the root's child order, so first to see every event (before PixelFrame can handle it).
        bool pad = e is InputEventKey { Device: 0x5AD };
        if (e is InputEventMouseMotion or InputEventScreenDrag)
        {
            if (e.Device == Sentinel) return;
            ForeignMotion++;
            // A real pointer over the test window (or a warp) would now take the replay viewer into the free camera: it is kept away from the game unless a probe asks for it.
            if (!PassForeignMotion) GetViewport().SetInputAsHandled();
            else ForeignMotionPassed++;
            return;
        }
        string what = e switch
        {
            InputEventKey k => $"key:{k.Keycode}:{k.Pressed}",
            InputEventJoypadButton b => $"joyb:{b.ButtonIndex}:{b.Pressed}",
            InputEventJoypadMotion m => $"joym:{m.Axis}:{m.AxisValue:0.0}",
            InputEventMouseButton mb => $"mouse:{mb.ButtonIndex}:{mb.Pressed}",
            _ => e.GetType().Name,
        };
        bool sent = e.Device == Sentinel;
        if (sent) SentinelArrivals++; else if (!pad && !_control) Foreign++;
        Arrivals.Add(new Arrival(Engine.GetProcessFrames(), what, sent, RestoredNow()));
    }

    bool _control;
    public bool PassForeignMotion;
    public int ForeignMotionPassed;
    public void SetControl(bool on) => _control = on;      // (events injected as the real device 0: not foreign)

    public override void _Process(double delta)
    {
        if (!_moved && GetTree().CurrentScene is { } cs && cs.Name == "Main")
        {
            _moved = true;
            GetParent().MoveChild(this, GetParent().GetChildCount() - 1);
        }
        if (_script == null) return;
        try { PerFrame?.Invoke(); }
        catch (Exception e) { GD.Print("VIEWERWIN: fatal|why=PerFrame " + e); }
        bool more;
        try { more = _script.MoveNext(); }
        catch (Exception e) { GD.Print("VIEWERWIN: fatal|why=script FAILED: " + e); GD.PrintErr(e); _script = null; GetTree().Quit(2); return; }
        if (!more) { _script = null; OnDone?.Invoke(); }
    }
}

public partial class RaceScene
{
    ViewerWindowedDriver _vwDrv;
    int _vwPass, _vwFail;
    bool _vwExpectDisabled, _vwRestored;
    int _vwModeViolations;
    string _vwViolationText = "";
    Dictionary<string, string> _vwSigAtRestore;
    ulong _vwRestoredFrame;

    static void VwLog(string tag, params (string k, object v)[] kv)
    {
        var sb = new System.Text.StringBuilder("VIEWERWIN: ").Append(tag);
        foreach (var (k, v) in kv) sb.Append('|').Append(k).Append('=').Append(v is double d ? d.ToString("0.###", CultureInfo.InvariantCulture) : v is float f ? f.ToString("0.###", CultureInfo.InvariantCulture) : (v?.ToString() ?? "null").Replace('|', '/').Replace('\n', ' '));
        GD.Print(sb.ToString());
    }

    void VwCheck(bool ok, string what) { if (ok) _vwPass++; else _vwFail++; VwLog("check", ("ok", ok), ("what", what)); }
    static void VwFinding(string id, string text) => VwLog("finding", ("id", id), ("text", text));

    // ------------------------------------------------------------------ entry (called by the one-line hook in PhysicsTest._Ready)

    /// <summary>Child entry: a driver beside the scene, then the real Main scene (PixelFrame, LoadingCover).</summary>
    public static void ViewerWindowedBegin(Node host)
    {
        var tree = host.GetTree();
        string name = VlArg("--viewer-windowed") ?? "";
        var drv = new ViewerWindowedDriver { Name = "ViewerWindowedDriver", ProcessMode = ProcessModeEnum.Always };
        drv.Start(VwMain(tree, drv, name));
        drv.OnDone = () => { VwLog("script-done"); tree.Quit(); };
        tree.Root.CallDeferred(Node.MethodName.AddChild, drv);
        tree.CallDeferred(SceneTree.MethodName.ChangeSceneToFile, "res://scenes/Main.tscn");
    }

    static RaceScene VwFindLive(Node n, int depth = 0)
    {
        if (n is RaceScene r && !r.ViewerMode) return r;
        if (depth > 4) return null;
        foreach (var c in n.GetChildren())
            if (VwFindLive(c, depth + 1) is { } f) return f;
        return null;
    }

    static IEnumerable VwMain(SceneTree tree, ViewerWindowedDriver drv, string name)
    {
        RaceScene live = null;
        for (int i = 0; i < 60 * 60 && live == null; i++) { live = VwFindLive(tree.Root); if (live == null) yield return null; }
        if (live == null) { VwLog("fatal", ("why", "no live RaceScene")); tree.Quit(3); yield break; }
        live._vwDrv = drv;
        drv.RestoredNow = () => live._vwRestored;
        VwLog("start", ("scenario", name), ("display", DisplayServer.GetName()), ("window", DisplayServer.WindowGetSize()), ("vsync", DisplayServer.WindowGetVsyncMode()),
            ("refresh_hz", DisplayServer.ScreenGetRefreshRate()), ("pads", Input.GetConnectedJoypads().Count), ("track", _trackId), ("renderer", RenderingServer.GetVideoAdapterName()));
        IEnumerable script = name switch
        {
            "cover-intro" => live.VwCoverIntro(),
            "cover-start" => live.VwCoverStart(),
            "headless-input" => live.VwHeadlessInput(),
            "loadcard-a" => live.VwLoadCardA(),
            "loadcard-b" => live.VwLoadCardB(),
            "gpu" => live.VwGpu(),
            "load-baseline" => live.VwLoadBaseline(),
            "load-bar" => live.VwLoadBar(),
            "build-diag" => live.VwBuildDiag(),
            "seek-diag" => live.VwSeekDiag(),
            "gpu-particle" => live.VwGpuParticle(),
            "hind-audio" => live.VwHindAudio(),
            "replay-ui" => live.VwReplayUi(),
            "lap-order" => live.VwLapOrder(),
            "session-best" => live.VwSessionBest(),
            "stakhanov-board" => live.VwStakhanovBoard(),
            "hud-toggles" => live.VwHudToggles(),
            "native-render" => live.VwNativeRender(),
            "export-ui" => live.VwExportUi(),
            "recording-delete" => live.VwRecordingDelete(),
            "viewer-keep" => live.VwViewerKeep(),
            "free-camera" => live.VwFreeCamera(),
            "leaderboard-replay" => live.VwLeaderboardReplay(),
            "lan-board-import" => live.VwLanBoardImport(),
            "free-camera-keys" => live.VwFreeCameraKeys(),
            "edits-bonus" => live.VwEditsBonus(),
            "edits-keep" => live.VwEditsKeep(),
            "edits-save" => live.VwEditsSave(),
            "edits-reload" => live.VwEditsReload(),
            "recordings-keep" => live.VwRecordingsKeep(),
            "recordings-restart" => live.VwRecordingsRestart(),
            "stakhanov-start" => live.VwStakhanovStart(),
            "stakhanov-fuel-reload" => live.VwStakhanovFuelReload(),
            "stakhanov-legacy" => live.VwStakhanovLegacy(),
            "rally-start" => live.VwRallyStart(false),
            "rally-start-resume" => live.VwRallyStart(true),
            "norm-card" => live.VwNormCard(),
            "loading-still" => live.VwLoadingStill(),
            "title-menu" => live.VwTitleMenu(),
            "menus" => live.VwMenus(),
            "replay-contexts" => live.VwReplayContexts(),
            "cam-compare" => live.VwCamCompare(),
            "cam-controlled" => live.VwCamControlled(),
            "cam-unlocked" => live.VwCamUnlocked(),
            _ => VwBad("unknown scenario " + name),
        };
        foreach (var x in script) yield return x;
        VwLog("summary", ("pass", live._vwPass), ("fail", live._vwFail), ("foreign_key_events", drv.Foreign), ("foreign_mouse_motion", drv.ForeignMotion));
    }

    static IEnumerable VwBad(string why) { VwLog("fatal", ("why", why)); yield break; }

    static IEnumerable VwFrames(int n) { for (int i = 0; i < n; i++) yield return null; }

    // ------------------------------------------------------------------ live state signature

    static readonly string[] VwActions = { "accelerate", "brake", "steer_left", "steer_right", "handbrake", "clutch", "horn", "rescue", "pause", "start", "keys", "instructions", "comments", "camera", "minimap", "tour", "rumble" };

    /// <summary>Everything observable of the live scene that a stray key or pad press would change.</summary>
    Dictionary<string, string> VwSig()
    {
        var d = new Dictionary<string, string>();
        void A(string k, object v) => d[k] = v?.ToString() ?? "null";
        A("mode", ProcessMode); A("visible", Visible); A("hudLayer", ((CanvasLayer)_hud.GetParent()).Visible);
        A("state", _state); A("run", _run); A("menu", _menu); A("hudMode", _hud.Mode); A("pauseSel", _hud.PauseSelection);
        A("showLog", _showLog); A("watching", _watching); A("board", _showBoard); A("keys", _showKeys); A("files", _showFiles); A("lessons", _showLessons);
        A("askName", _askName); A("resignOut", _resignOut); A("lanLeaveAsk", _lanLeaveAsk); A("card", CardShown);
        A("padMode", _padMode); A("padDevice", _driver.Device); A("padKeys", _padKeys.Count); A("padHeld", _padHeld);
        A("loadingCard", TrackLoadingScreen.Active != null);
        A("lapClock", _lapClock.ToString("R", CultureInfo.InvariantCulture)); A("raceClock", _raceClock.ToString("R", CultureInfo.InvariantCulture));
        A("replayClock", _replayClock.ToString("R", CultureInfo.InvariantCulture)); A("carPos", _car.Pos.ToString("R"));
        A("lan", CCCPRacing.Net.LanSession.Current != null);
        foreach (var a in VwActions) if (InputMap.HasAction(a)) A("act." + a, Input.IsActionPressed(a));
        return d;
    }

    static string VwDiff(Dictionary<string, string> a, Dictionary<string, string> b, params string[] ignore)
    {
        var parts = new List<string>();
        foreach (var k in a.Keys.Union(b.Keys))
        {
            if (ignore.Contains(k)) continue;
            a.TryGetValue(k, out var x); b.TryGetValue(k, out var y);
            if (x != y) parts.Add($"{k}: {x} -> {y}");
        }
        return string.Join("; ", parts);
    }

    /// <summary>What a refused open must leave alone: the live signature plus tree sizes, audio flags and the session list.</summary>
    string VwTouch() => string.Join(";", VwSig().OrderBy(k => k.Key).Select(k => k.Key + "=" + k.Value))
        + $"|rootChildren={GetTree().Root.GetChildCount()}|parentChildren={GetParent().GetChildCount()}|sessions={ActiveReplayViewers.Count}|viewer={(ReplayViewer == null ? "null" : "set")}|players={string.Join(",", VlPlayers().OrderBy(k => k.Key).Select(k => k.Key + ":" + k.Value))}";

    // ------------------------------------------------------------------ events

    static InputEventKey VwKey(Key k, bool pressed, int device = ViewerWindowedDriver.Sentinel) => new() { Keycode = k, PhysicalKeycode = k, Pressed = pressed, Device = device };
    static InputEventJoypadButton VwPadB(JoyButton b, bool pressed, int device = ViewerWindowedDriver.Sentinel) => new() { ButtonIndex = b, Pressed = pressed, Device = device };
    static InputEventJoypadMotion VwPadM(JoyAxis a, float v, int device = ViewerWindowedDriver.Sentinel) => new() { Axis = a, AxisValue = v, Device = device };

    static List<InputEvent> VwMatrixEvents()
    {
        var l = new List<InputEvent>();
        // (phase 3: the viewer's own controls act on keys: ESC and the pad's B go back, so they are left out of the matrix of a viewer that must stay open; the back path is tested by --viewer-windowed-only=replayui)
        foreach (var k in new[] { Key.Enter, Key.Space, Key.Up, Key.Down, Key.Left, Key.Right, Key.R, Key.P, Key.K, Key.I, Key.T, Key.C, Key.M, Key.Tab, Key.Shift })
        { l.Add(VwKey(k, true)); l.Add(VwKey(k, false)); }
        foreach (var b in new[] { JoyButton.A, JoyButton.X, JoyButton.Y, JoyButton.Start, JoyButton.Back, JoyButton.DpadUp, JoyButton.DpadDown, JoyButton.DpadLeft, JoyButton.DpadRight, JoyButton.LeftShoulder, JoyButton.RightShoulder })
        { l.Add(VwPadB(b, true)); l.Add(VwPadB(b, false)); }
        foreach (var (ax, v) in new[] { (JoyAxis.LeftX, 0.9f), (JoyAxis.LeftX, -0.9f), (JoyAxis.LeftY, 0.9f), (JoyAxis.LeftY, -0.9f), (JoyAxis.TriggerRight, 1f), (JoyAxis.TriggerLeft, 1f) })
        { l.Add(VwPadM(ax, v)); l.Add(VwPadM(ax, 0f)); }
        l.Add(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = new Vector2(240, 135), Device = ViewerWindowedDriver.Sentinel });
        l.Add(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = new Vector2(240, 135), Device = ViewerWindowedDriver.Sentinel });
        return l;
    }

    // ------------------------------------------------------------------ session helpers

    ReplayViewerSession VwOpen(LapSource lap, string label, out string err)
    {
        var s = OpenReplayViewer(lap, out err);
        if (s == null) { VwLog("open", ("label", label), ("ok", false), ("error", err)); return null; }
        _vwExpectDisabled = true; _vwRestored = false; _vwModeViolations = 0; _vwViolationText = ""; _vwSigAtRestore = null;
        _vwDrv.PerFrame = () =>
        {
            if (_vwExpectDisabled && ProcessMode != ProcessModeEnum.Disabled && !_vwRestored)
            {
                _vwModeViolations++;
                if (_vwViolationText.Length < 300) _vwViolationText += $"[frame {Engine.GetProcessFrames()} mode {ProcessMode} viewer {s.State}] ";
            }
        };
        s.Restored += () =>
        {
            _vwRestored = true; _vwRestoredFrame = Engine.GetProcessFrames(); _vwExpectDisabled = false;
            _vwSigAtRestore = VwSig();
        };
        VwLog("open", ("label", label), ("ok", true), ("state", s.State), ("frame", Engine.GetProcessFrames()), ("live_mode", ProcessMode));
        return s;
    }

    static LapSource VwFixtureLap(string track) => VlLap(track);

    /// <summary>One input phase: the matrix, one event per frame, against an open session. The live signature must not move and every event must be counted by the session.</summary>
    IEnumerable VwMatrixPhase(ReplayViewerSession s, string phase)
    {
        var sig0 = VwSig(); int r0 = s.RoutedInputCount; int a0 = _vwDrv.SentinelArrivals; int f0 = _vwDrv.Foreign; var st0 = s.State;
        int arrivalsFrom = _vwDrv.Arrivals.Count;
        var events = VwMatrixEvents();
        foreach (var e in events) { Input.ParseInputEvent(e); yield return null; }
        for (int i = 0; i < 4; i++) yield return null;
        var sig1 = VwSig(); int routed = s.RoutedInputCount - r0; int arrived = _vwDrv.SentinelArrivals - a0; int foreign = _vwDrv.Foreign - f0;
        var late = _vwDrv.Arrivals.Skip(arrivalsFrom).Where(a => a.Sentinel).Select(a => (long)a.Frame).ToList();
        string diff = VwDiff(sig0, sig1);
        VwLog("matrix", ("phase", phase), ("state_start", st0), ("state_end", s.State), ("injected", events.Count), ("arrived", arrived), ("routed", routed), ("foreign_key_events", foreign),
            ("sig_diff", diff == "" ? "none" : diff), ("frames", late.Count == 0 ? "-" : (late.Max() - late.Min()).ToString()));
        VwCheck(arrived == events.Count, $"{phase}: every injected event reached the window's input layer ({arrived} of {events.Count})");
        VwCheck(routed == arrived + foreign, $"{phase}: the session counted every event the window received (routed {routed}, injected arrivals {arrived}, foreign {foreign})");
        VwCheck(diff == "", $"{phase}: the live scene's observable state (mode, state, menu, hud, overlays, pad keys, stick, actions, clocks, LAN) is unchanged [{diff}]");
        VwCheck(_vwModeViolations == 0, $"{phase}: the live ProcessMode stayed Disabled on every frame so far [{_vwViolationText}]");
    }

    /// <summary>Close the viewer while a flood of Escape (Down on the title)/Space presses (four events a frame) runs through Closing and a few frames beyond. Every event that arrived before the restore must be routed; the live state must equal the state before the open at the restore.</summary>
    IEnumerable VwFloodClose(ReplayViewerSession s, Dictionary<string, string> sigBeforeOpen, string label)
    {
        int r0 = s.RoutedInputCount; int idx0 = _vwDrv.Arrivals.Count;
        s.Close();
        int post = 0; int frames = 0; ulong closeFrame = Engine.GetProcessFrames();
        while (frames++ < 600 && post < 4)
        {
            var fk = StateOf(sigBeforeOpen) == State.Title ? Key.Down : Key.Escape;      // (an Escape on the title could mean quit: the flood must not depend on that)
            Input.ParseInputEvent(VwKey(fk, true)); Input.ParseInputEvent(VwKey(fk, false));
            Input.ParseInputEvent(VwKey(Key.Space, true)); Input.ParseInputEvent(VwKey(Key.Space, false));
            yield return null;
            if (_vwRestored) post++;
        }
        for (int i = 0; i < 3; i++) yield return null;
        var arr = _vwDrv.Arrivals.Skip(idx0).Where(a => a.Sentinel).ToList();
        int before = arr.Count(a => !a.Restored), after = arr.Count(a => a.Restored);
        int routed = s.RoutedInputCount - r0;
        VwLog("flood", ("label", label), ("close_frame", closeFrame), ("restored_frame", _vwRestoredFrame), ("frames_closing", (long)_vwRestoredFrame - (long)closeFrame), ("arrived_before_restore", before), ("arrived_after_restore", after), ("routed", routed), ("state", s.State),
            ("live_mode_at_restore", _vwSigAtRestore?["mode"]), ("sig_diff_at_restore", _vwSigAtRestore == null ? "no restore" : VwDiff(sigBeforeOpen, _vwSigAtRestore)));
        VwCheck(s.State == ReplayViewerState.Closed && _vwRestored, $"{label}: the session reached Closed and restored the live scene (state {s.State})");
        VwCheck(routed == before, $"{label}: the session was routed exactly the events that arrived before the restore (routed {routed}, arrived before {before}, after {after})");
        VwCheck(_vwSigAtRestore != null && VwDiff(sigBeforeOpen, _vwSigAtRestore) == "", $"{label}: at the restore the live scene equals its state before the open [{(_vwSigAtRestore == null ? "no restore" : VwDiff(sigBeforeOpen, _vwSigAtRestore))}]");
        VwCheck(_vwModeViolations == 0, $"{label}: the live ProcessMode was Disabled on every frame until the restore (violations {_vwModeViolations} {_vwViolationText})");
        VwCheck(ProcessMode == ProcessModeEnum.Inherit && ReplayViewer == null && ActiveReplayViewers.Count == 0, $"{label}: after Closed the live ProcessMode is Inherit, the owner reference is cleared and no session is active (mode {ProcessMode}, viewer {(ReplayViewer == null ? "null" : "set")}, active {ActiveReplayViewers.Count})");
        // the flood's post-restore Escapes act on the live scene again; bring it back to what it was
        for (int i = 0; i < 6 && StateOf(sigBeforeOpen) != State.Title && _state != StateOf(sigBeforeOpen); i++)
        {
            Input.ParseInputEvent(VwKey(Key.Escape, true)); Input.ParseInputEvent(VwKey(Key.Escape, false));
            for (int k = 0; k < 4; k++) yield return null;
        }
        for (int i = 0; i < 3; i++) yield return null;
    }

    State StateOf(Dictionary<string, string> sig) => Enum.Parse<State>(sig["state"]);

    /// <summary>After Closed: the same keys and pad do reach the live scene again.</summary>
    IEnumerable VwControl(string label)
    {
        for (int i = 0; i < 4; i++) yield return null;      // (the restore ignores the press of its own frame by design)
        var sig0 = VwSig();
        _vwDrv.SetControl(true);
        if (_state is State.Racing or State.Countdown)
        {
            Input.ParseInputEvent(VwKey(Key.Escape, true, 0)); yield return null; Input.ParseInputEvent(VwKey(Key.Escape, false, 0));
            for (int i = 0; i < 4; i++) yield return null;
            var sig1 = VwSig();
            VwCheck(_state == State.Paused, $"{label}: control, after Closed Escape reaches the live scene again and pauses it (state {sig0["state"]} -> {sig1["state"]})");
            Input.ParseInputEvent(VwKey(Key.Escape, true, 0)); yield return null; Input.ParseInputEvent(VwKey(Key.Escape, false, 0));
            for (int i = 0; i < 6; i++) yield return null;
            VwCheck(_state == State.Racing || _state == State.Countdown, $"{label}: control, a second Escape resumes it (state {_state})");
        }
        else
        {
            Input.ParseInputEvent(VwKey(Key.Down, true, 0)); yield return null; Input.ParseInputEvent(VwKey(Key.Down, false, 0));
            Input.ParseInputEvent(VwPadM(JoyAxis.LeftY, 0.9f, 0)); yield return null; Input.ParseInputEvent(VwPadM(JoyAxis.LeftY, 0f, 0));
            for (int i = 0; i < 4; i++) yield return null;
            var sig1 = VwSig();
            string diff = VwDiff(sig0, sig1);
            VwCheck(diff != "", $"{label}: control, after Closed the same Down key and pad stick reach the live scene again [{diff}]");
        }
        _vwDrv.SetControl(false);
    }

    // ------------------------------------------------------------------ reflection into PixelFrame

    PixelFrame VwPixelFrame() => GetParent()?.GetParent() as PixelFrame;
    static object VwField(object o, string name) => o?.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(o);
    LoadingCover VwCover() => VwField(VwPixelFrame(), "_cover") as LoadingCover;
    bool VwCoverNotice() => VwField(VwPixelFrame(), "_coverIntro") is true;

    IEnumerable VwWaitReady(int maxFrames = 60 * 60)
    {
        for (int i = 0; i < maxFrames && !(_hud != null && _dayNight != null && !_booting && _timer != null); i++) yield return null;
    }

    // ------------------------------------------------------------------ the shared stick probe: a release routed to the viewer is lost to the live scene

    IEnumerable VwStickProbe(LapSource lap, string label)
    {
        // The stick is pushed (as the real device 0) before the open, released while the viewer owns the input, the viewer closes: the live scene's stick state.
        _vwDrv.SetControl(true);
        Input.ParseInputEvent(VwPadM(JoyAxis.LeftY, 0.9f, 0)); yield return null; yield return null;
        int stickBefore = _stickY;
        var sigBefore = VwSig();
        var s = VwOpen(lap, label + " stick", out string err);
        if (s == null) { VwCheck(false, $"{label}: stick probe open failed ({err})"); _vwDrv.SetControl(false); yield break; }
        for (int i = 0; i < 6; i++) yield return null;
        Input.ParseInputEvent(VwPadM(JoyAxis.LeftY, 0f, 0)); yield return null; yield return null;      // released while the viewer is open: routed to the session
        _vwDrv.SetControl(false);
        foreach (var x in VwFloodClose(s, sigBefore, label + " stick-close")) yield return x;
        VwLog("stick", ("label", label), ("stickY_before_open", stickBefore), ("stickY_after_close", _stickY), ("pad_keys", _padKeys.Count));
        if (_stickY != 0)
        {
            VwFinding("stale-stick", $"{label}: the left stick was pushed before the viewer opened and released while it was open (the release went to the session); after Closed the live scene still holds _stickY={_stickY} (RaceScene.Pad.cs StickAsDpad edge state). The next push in that direction is ignored as 'no change'.");
            // the consequence in a pad menu: the pause menu ignores the first push
            _vwDrv.SetControl(true);
            Input.ParseInputEvent(VwKey(Key.Escape, true, 0)); yield return null; Input.ParseInputEvent(VwKey(Key.Escape, false, 0));
            for (int i = 0; i < 6; i++) yield return null;
            bool inPause = _state == State.Paused;
            Input.ParseInputEvent(VwPadM(JoyAxis.LeftY, 0.9f, 0)); yield return null; yield return null;
            bool heldStale = _padHeld != null;
            Input.ParseInputEvent(VwPadM(JoyAxis.LeftY, 0f, 0)); yield return null; yield return null;      // clears the stale state (now 0 != was 1)
            Input.ParseInputEvent(VwPadM(JoyAxis.LeftY, 0.9f, 0)); yield return null; yield return null;
            bool heldFresh = _padHeld != null;
            Input.ParseInputEvent(VwPadM(JoyAxis.LeftY, 0f, 0)); yield return null; yield return null;
            Input.ParseInputEvent(VwKey(Key.Escape, true, 0)); yield return null; Input.ParseInputEvent(VwKey(Key.Escape, false, 0));
            for (int i = 0; i < 6; i++) yield return null;
            _vwDrv.SetControl(false);
            VwLog("stick-consequence", ("in_pause_menu", inPause), ("first_push_seen_with_stale_state", heldStale), ("push_seen_after_clearing", heldFresh));
        }
        else
        {
            _vwDrv.SetControl(true);
            Input.ParseInputEvent(VwPadM(JoyAxis.LeftY, 0f, 0)); yield return null;
            _vwDrv.SetControl(false);
        }
    }

    // ------------------------------------------------------------------ scenario: the real cover with the launch notice (title)

    /// <summary>Task 1 (a)/(b)/(c) on the production start-up path: LoadingCover + the TEST VERSION notice, so the world resumes in CloseCoverIntro before the cover is Gone.</summary>
    IEnumerable VwCoverIntro()
    {
        var lap = VwFixtureLap(VlArg("--vw-lap") ?? _trackId);
        if (lap == null) { VwLog("fatal", ("why", "no fixture lap")); yield break; }
        // wait for the cover
        int w = 0;
        while ((VwCover() == null || _hud == null) && w++ < 60 * 30) yield return null;
        VwCheck(VwCover() != null, "the production PixelFrame created a LoadingCover (windowed, --cover)");
        VwCheck(ProcessMode == ProcessModeEnum.Disabled, $"start-up phase: the live world is Disabled under the cover (mode {ProcessMode})");
        // (a) refused at start-up, touches nothing
        for (int attempt = 0; attempt < 3; attempt++)
        {
            string before = VwTouch();
            var s0 = OpenReplayViewer(lap, out string err);
            string after = VwTouch();
            VwCheck(s0 == null && !string.IsNullOrEmpty(err), $"start-up phase #{attempt + 1}: the open is refused with a reason ({err})");
            VwCheck(before == after, $"start-up phase #{attempt + 1}: a refused open touched nothing" + (before == after ? "" : " [changed]"));
            foreach (var x in VwFrames(90)) yield return x;
        }
        // wait for the launch notice over the cover, press Enter until the production code resumes the world (CloseCoverIntro)
        w = 0;
        while (!(VwCoverNotice() && StartupIntroVisible) && w++ < 60 * 40) yield return null;
        VwLog("intro", ("frames_waited", w), ("cover_intro", VwCoverNotice()), ("notice_visible", StartupIntroVisible), ("mode", ProcessMode));
        VwCheck(VwCoverNotice() && StartupIntroVisible, "the launch notice is shown above the cover (PixelFrame._coverIntro)");
        var cover = VwCover();
        VwCheck(ProcessMode == ProcessModeEnum.Disabled, "while the notice is up the world is still Disabled");
        {
            string before = VwTouch();
            var s0 = OpenReplayViewer(lap, out string err);
            VwCheck(s0 == null && before == VwTouch(), $"with the notice up: the open is refused and touches nothing ({err})");
        }
        ulong goneFrame = 0; string goneInfo = null; ReplayViewerSession session = null; ProcessModeEnum goneMode = ProcessModeEnum.Inherit; ReplayViewerState goneState = ReplayViewerState.Closed; bool gone = false;
        cover.Gone += () => { gone = true; goneFrame = Engine.GetProcessFrames(); goneMode = ProcessMode; goneState = session?.State ?? ReplayViewerState.Closed; goneInfo = $"mode {ProcessMode}, viewer {(session == null ? "none" : session.State.ToString())}"; };
        _vwDrv.SetControl(true);
        w = 0;
        while (ProcessMode != ProcessModeEnum.Inherit && w++ < 60 * 10)
        {
            if (w % 30 == 1) { Input.ParseInputEvent(VwKey(Key.Enter, true, 0)); }
            else if (w % 30 == 3) { Input.ParseInputEvent(VwKey(Key.Enter, false, 0)); }
            yield return null;
        }
        _vwDrv.SetControl(false);
        bool fading = IsInstanceValid(cover) && !gone;
        VwLog("resumed", ("frames", w), ("mode", ProcessMode), ("cover_alive", IsInstanceValid(cover)), ("gone", gone), ("cover_alpha", IsInstanceValid(cover) ? cover.Modulate.A : -1f));
        VwCheck(ProcessMode == ProcessModeEnum.Inherit && fading, $"CloseCoverIntro resumed the world (Inherit) while the cover is still fading (mode {ProcessMode}, cover alive {IsInstanceValid(cover)}, Gone seen {gone})");
        if (!fading) yield break;
        var sigBeforeOpen = VwSig();
        session = VwOpen(lap, "cover-intro", out string openErr);
        VwCheck(session != null, $"the viewer opened over the fading cover (error {openErr})");
        if (session == null) yield break;
        // during the fade PixelFrame gives all input to the cover (LoadingCover.Press), not RawInput: production behaviour, labelled
        int fadeRouted0 = session.RoutedInputCount; var fadeSig0 = VwSig();
        int fadeFrames = 0;
        while (!gone && fadeFrames < 60 * 3)
        {
            if (fadeFrames < 6) Input.ParseInputEvent(VwKey(Key.Enter, fadeFrames % 2 == 0));      // (the cover takes it)
            fadeFrames++; yield return null;
        }
        VwLog("fade", ("frames", fadeFrames), ("session_state_at_gone", goneState), ("routed_during_fade", session.RoutedInputCount - fadeRouted0), ("sig_diff", VwDiff(fadeSig0, VwSig())));
        VwCheck(gone, "the cover's Gone fired while the viewer was open");
        VwCheck(goneMode == ProcessModeEnum.Disabled, $"at Gone (PixelFrame's handler has run CoverSetsProcessMode(Inherit)) the live ProcessMode is still Disabled [{goneInfo}]");
        VwCheck(goneState is ReplayViewerState.Building or ReplayViewerState.Playing, $"Gone fired with the viewer Building or Playing (was {goneState})");
        VwCheck(goneState == ReplayViewerState.Building, $"the fade ended while the viewer was still Building (was {goneState}; Playing would also keep the world frozen)");
        VwCheck(VwDiff(fadeSig0, VwSig(), "mode") == "" , "the fade (input to the cover): the live scene did not change");
        // (b) after the cover is gone: Building (if still), Playing
        if (session.State == ReplayViewerState.Building) foreach (var x in VwMatrixPhase(session, "cover-intro building (after Gone)")) yield return x;
        w = 0; while (session.State == ReplayViewerState.Building && w++ < 60 * 120) yield return null;
        VwCheck(session.State == ReplayViewerState.Playing, $"the viewer reached Playing (state {session.State}, error {session.Error})");
        if (session.State != ReplayViewerState.Playing) { session.Close(); yield break; }
        VwCheck(ProcessMode == ProcessModeEnum.Disabled, "Playing: the live ProcessMode is still Disabled after the cover is gone");
        foreach (var _ in VwFrames(40)) yield return null;
        foreach (var x in VwMatrixPhase(session, "cover-intro playing")) yield return x;
        foreach (var x in VwFloodClose(session, sigBeforeOpen, "cover-intro close")) yield return x;
        foreach (var x in VwControl("cover-intro")) yield return x;
        // (c) more rapid cycles from the title: close at once, close in the middle of the build
        foreach (var x in VwRapidCycles(lap, "cover-intro")) yield return x;
    }

    // ------------------------------------------------------------------ scenario: --start under the cover (racing), also the refusal during the fade

    IEnumerable VwCoverStart()
    {
        var lap = VwFixtureLap(VlArg("--vw-lap") ?? _trackId);
        if (lap == null) { VwLog("fatal", ("why", "no fixture lap")); yield break; }
        int w = 0;
        while ((VwCover() == null || _hud == null) && w++ < 60 * 30) yield return null;
        var cover = VwCover();
        VwCheck(cover != null && ProcessMode == ProcessModeEnum.Disabled, $"start-up: the production cover is up and the live world is Disabled (mode {ProcessMode})");
        bool gone = false; ProcessModeEnum goneMode = ProcessModeEnum.Disabled;
        if (cover != null) cover.Gone += () => { gone = true; goneMode = ProcessMode; };
        // Without the notice the cover fades as soon as it is ready: the world is Disabled during the whole fade and resumes only at Gone. Open must be refused at every frame of it.
        int refused = 0, attempts = 0; bool refusedInFade = false; ulong fadeSeen = 0;
        w = 0;
        while (!gone && w++ < 60 * 60)
        {
            if (w % 15 == 0)
            {
                string before = VwTouch();
                var s0 = OpenReplayViewer(lap, out string err);
                attempts++;
                if (s0 == null && before == VwTouch()) refused++;
                if (IsInstanceValid(cover) && cover.Modulate.A < 0.999f) { refusedInFade = true; fadeSeen++; }
                else if (s0 != null) { s0.Close(); }
            }
            yield return null;
        }
        VwLog("startup-refusals", ("attempts", attempts), ("refused_untouched", refused), ("during_fade_seen", fadeSeen));
        VwCheck(attempts > 0 && refused == attempts, $"start-up: every open attempt under the cover was refused and touched nothing ({refused} of {attempts})");
        VwCheck(refusedInFade, "start-up: at least one refused attempt happened while the cover was already fading (world still Disabled)");
        VwCheck(gone && goneMode == ProcessModeEnum.Inherit, $"no viewer: Gone resumes the world (CoverSetsProcessMode applies at once; mode {goneMode})");
        foreach (var x in VwRacingFlow(lap, "cover-start", withStick: true)) yield return x;
    }

    // ------------------------------------------------------------------ scenario: the same, headless (no cover)

    IEnumerable VwHeadlessInput()
    {
        var lap = VwFixtureLap(VlArg("--vw-lap") ?? _trackId);
        if (lap == null) { VwLog("fatal", ("why", "no fixture lap")); yield break; }
        foreach (var x in VwWaitReady()) yield return x;
        foreach (var x in VwRacingFlow(lap, "headless", withStick: true)) yield return x;
    }

    /// <summary>The racing live scene: Building, Playing and Closing input matrices, control, stick probe, rapid cycles.</summary>
    IEnumerable VwRacingFlow(LapSource lap, string label, bool withStick)
    {
        int w = 0;
        while (_state != State.Racing && w++ < 60 * 60) yield return null;
        VwCheck(_state == State.Racing, $"{label}: the live scene is Racing (autodrive) before the open (state {_state}, mode {ProcessMode})");
        foreach (var _ in VwFrames(60)) yield return null;
        var sigBefore = VwSig();
        var s = VwOpen(lap, label, out string err);
        VwCheck(s != null, $"{label}: the viewer opened on the running world (error {err})");
        if (s == null) yield break;
        VwCheck(ProcessMode == ProcessModeEnum.Disabled, $"{label}: the open suspended the live world");
        foreach (var x in VwMatrixPhase(s, label + " building")) yield return x;
        w = 0; while (s.State == ReplayViewerState.Building && w++ < 60 * 120) yield return null;
        VwCheck(s.State == ReplayViewerState.Playing, $"{label}: the viewer reached Playing (state {s.State}, error {s.Error})");
        if (s.State != ReplayViewerState.Playing) { s.Close(); yield break; }
        foreach (var _ in VwFrames(40)) yield return null;
        foreach (var x in VwMatrixPhase(s, label + " playing")) yield return x;
        foreach (var x in VwFloodClose(s, sigBefore, label + " closing")) yield return x;
        foreach (var x in VwControl(label)) yield return x;
        // phase 3: the viewer's own back controls close it (cancel while loading, back while playing): through the same finally-safe path, the live scene exactly as before
        foreach (var (how, building) in new[] { ("escape", true), ("pad-b", true), ("escape", false), ("backspace", false), ("pad-b", false) })
            foreach (var x in VwCancelByInput(lap, label, how, building)) yield return x;
        if (withStick) foreach (var x in VwStickProbe(lap, label)) yield return x;
        foreach (var x in VwRapidCycles(lap, label)) yield return x;
        // polled input channel: a key held across the whole viewer session
        foreach (var x in VwHeldKeyProbe(lap, label)) yield return x;
    }

    /// <summary>An explicit back input (ESC, BACKSPACE, the pad's B) while the viewer builds or plays: it closes, restores the live scene exactly and says why.</summary>
    IEnumerable VwCancelByInput(LapSource lap, string label, string how, bool building)
    {
        string tag = $"{label} cancel-{how}-{(building ? "building" : "playing")}";
        var sigBefore = VwSig();
        var s = VwOpen(lap, tag, out string err);
        if (s == null) { VwCheck(false, $"{tag}: open failed ({err})"); yield break; }
        int g = 0;
        if (building) for (int i = 0; i < 20; i++) yield return null;
        else { while (s.State == ReplayViewerState.Building && g++ < 60 * 120) yield return null; for (int i = 0; i < 20; i++) yield return null; }
        VwCheck(s.State == (building ? ReplayViewerState.Building : ReplayViewerState.Playing), $"{tag}: the viewer is {(building ? "still building" : "playing")} when the input comes (state {s.State})");
        int r0 = s.RoutedInputCount; ulong sent = Engine.GetProcessFrames();
        switch (how)
        {
            case "escape": Input.ParseInputEvent(VwKey(Key.Escape, true)); yield return null; Input.ParseInputEvent(VwKey(Key.Escape, false)); break;
            case "backspace": Input.ParseInputEvent(VwKey(Key.Backspace, true)); yield return null; Input.ParseInputEvent(VwKey(Key.Backspace, false)); break;
            default: Input.ParseInputEvent(VwPadB(JoyButton.B, true)); yield return null; Input.ParseInputEvent(VwPadB(JoyButton.B, false)); break;
        }
        g = 0; while (!_vwRestored && g++ < 60 * 90) yield return null;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(s.State == ReplayViewerState.Closed && _vwRestored, $"{tag}: the input closed the session and the live scene was restored (state {s.State})");
        VwCheck(s.RoutedInputCount > r0, $"{tag}: the input reached the session ({s.RoutedInputCount - r0} events)");
        // (the held state of the very key that closed it is the player's finger, not the live scene's: the restore can come a frame before its release; everything else must be equal)
        string[] fingers = VwActions.Select(a => "act." + a).ToArray();
        VwCheck(_vwSigAtRestore != null && VwDiff(sigBefore, _vwSigAtRestore, fingers) == "", $"{tag}: at the restore the live scene equals its state before the open, key states aside [{(_vwSigAtRestore == null ? "no restore" : VwDiff(sigBefore, _vwSigAtRestore, fingers))}] (all differences incl. key states: [{(_vwSigAtRestore == null ? "-" : VwDiff(sigBefore, _vwSigAtRestore))}])");
        for (int i = 0; i < 12; i++) yield return null;
        { var now = VwSig(); VwCheck(VwActions.All(x => !now.TryGetValue("act." + x, out var v) || v == "False"), $"{tag}: once the key is up no action stays pressed in the live scene"); }
        VwCheck(_vwModeViolations == 0, $"{tag}: the live ProcessMode stayed Disabled until the restore (violations {_vwModeViolations})");
        VwCheck(ProcessMode == ProcessModeEnum.Inherit && ReplayViewer == null && ActiveReplayViewers.Count == 0, $"{tag}: the live scene runs again and no session is left (mode {ProcessMode})");
        VwCheck(s.EndNotice == (building ? "REPLAY CANCELLED" : ""), $"{tag}: the list's message is right (\"{s.EndNotice}\")");
        for (int i = 0; i < 10; i++) yield return null;
    }

    /// <summary>(c) Rapid close plus input: open and close at once, open and close in the middle of the build, close twice, reopen while Closing.</summary>
    IEnumerable VwRapidCycles(LapSource lap, string label)
    {
        foreach (var (name, pre) in new[] { ("immediate", 0), ("early", 12), ("mid", 70) })
        {
            if (_state == State.Paused) { Input.ParseInputEvent(VwKey(Key.Escape, true, 0)); yield return null; Input.ParseInputEvent(VwKey(Key.Escape, false, 0)); foreach (var _ in VwFrames(6)) yield return null; }
            var sigBefore = VwSig();
            var s = VwOpen(lap, $"{label} rapid-{name}", out string err);
            if (s == null) { VwCheck(false, $"{label} rapid-{name}: open failed ({err})"); continue; }
            for (int i = 0; i < pre; i++)
            {
                Input.ParseInputEvent(VwKey(Key.Space, true)); Input.ParseInputEvent(VwKey(Key.Space, false));
                yield return null;
            }
            // reopen while open and while Closing is refused
            var again = OpenReplayViewer(lap, out string againErr);
            VwCheck(again == null, $"{label} rapid-{name}: a second open while one is open is refused ({againErr})");
            foreach (var x in VwFloodClose(s, sigBefore, $"{label} rapid-{name} close")) yield return x;
            s.Close();      // idempotent after Closed
            VwCheck(s.State == ReplayViewerState.Closed, $"{label} rapid-{name}: a Close after Closed changes nothing");
        }
        // reopen right after Closed
        {
            var sigBefore = VwSig();
            var s1 = VwOpen(lap, $"{label} rapid-reopen A", out string e1);
            if (s1 != null)
            {
                for (int i = 0; i < 5; i++) yield return null;
                s1.Close();
                var during = OpenReplayViewer(lap, out string duringErr);
                VwCheck(during == null, $"{label} rapid-reopen: an open while the first is Closing is refused ({duringErr})");
                int g = 0; while (s1.State != ReplayViewerState.Closed && g++ < 600) yield return null;
                VwCheck(s1.State == ReplayViewerState.Closed && ProcessMode == ProcessModeEnum.Inherit, $"{label} rapid-reopen: the first session closed and restored (mode {ProcessMode})");
                var s2 = VwOpen(lap, $"{label} rapid-reopen B", out string e2);
                VwCheck(s2 != null, $"{label} rapid-reopen: the second open directly after Closed works ({e2})");
                if (s2 != null)
                {
                    for (int i = 0; i < 5; i++) yield return null;
                    foreach (var x in VwFloodClose(s2, sigBefore, $"{label} rapid-reopen close")) yield return x;
                }
            }
        }
    }

    /// <summary>The polled channel: a key held (press before the open, release while open) and a key pressed while open and never released.</summary>
    IEnumerable VwHeldKeyProbe(LapSource lap, string label)
    {
        if (_state == State.Paused) yield break;
        _vwDrv.SetControl(true);
        Input.ParseInputEvent(VwKey(Key.Up, true, 0)); yield return null; yield return null;
        bool heldBefore = Input.IsActionPressed("accelerate");
        var s = VwOpen(lap, label + " held", out string err);
        if (s == null) { _vwDrv.SetControl(false); yield break; }
        for (int i = 0; i < 6; i++) yield return null;
        Input.ParseInputEvent(VwKey(Key.Up, false, 0)); yield return null; yield return null;      // released while the viewer owns the input
        bool heldDuring = Input.IsActionPressed("accelerate");
        Input.ParseInputEvent(VwKey(Key.Down, true, 0)); yield return null; yield return null;       // pressed while open, never released until after Closed
        var sigBefore = VwSig();
        s.Close(); int g = 0; while (s.State != ReplayViewerState.Closed && g++ < 600) yield return null;
        bool downAfter = Input.IsActionPressed("brake");
        Input.ParseInputEvent(VwKey(Key.Down, false, 0)); yield return null; yield return null;
        bool upAfter = Input.IsActionPressed("accelerate"), downReleased = !Input.IsActionPressed("brake");
        _vwDrv.SetControl(false);
        VwLog("held-key", ("accelerate_before_open", heldBefore), ("accelerate_after_release_in_viewer", heldDuring), ("brake_held_at_restore", downAfter), ("accelerate_after_close", upAfter), ("brake_released_after", downReleased));
        VwCheck(heldBefore && !heldDuring && !upAfter, $"{label}: the global input state is never stale: a release during the viewer is seen by the polled actions (before {heldBefore}, during {heldDuring}, after close {upAfter})");
        VwCheck(downAfter && downReleased, $"{label}: a key pressed during the viewer is still held at the restore (physical state) and releasable afterwards");
    }

    // ------------------------------------------------------------------ scenario: (d) the track-loading card

    /// <summary>A loading card active first: the open is refused. (StartRun is what a menu choice calls.)</summary>
    IEnumerable VwLoadCardA()
    {
        var lap = VwFixtureLap(VlArg("--vw-lap") ?? _trackId);
        if (lap == null) { VwLog("fatal", ("why", "no fixture lap")); yield break; }
        int w = 0;
        while (VwCover() != null && w++ < 60 * 90) yield return null;
        while (_state != State.Racing && w++ < 60 * 150) yield return null;
        foreach (var _ in VwFrames(30)) yield return null;
        VwCheck(_state == State.Racing, $"loadcard-a: racing before the card (state {_state})");
        StartRun(MenuItem.Race);      // the call every menu choice and the school's end make
        w = 0; while (TrackLoadingScreen.Active == null && w++ < 120) yield return null;
        VwCheck(TrackLoadingScreen.Active != null, "loadcard-a: the loading card is active");
        foreach (var _ in VwFrames(30)) yield return null;
        string before = VwTouch();
        var s = OpenReplayViewer(lap, out string err);
        VwCheck(s == null && !string.IsNullOrEmpty(err), $"loadcard-a: while the card is up the open is refused with a reason ({err})");
        VwCheck(before == VwTouch(), "loadcard-a: the refused open touched nothing");
        TrackLoadingScreen.Active?.Cancel();
        foreach (var _ in VwFrames(30)) yield return null;
        VwLog("loadcard-a-end", ("active", TrackLoadingScreen.Active != null), ("mode", ProcessMode));
    }

    /// <summary>A viewer open first, then a loading card (a code path with no input behind it). Records what the production code does; findings, not fixes.</summary>
    IEnumerable VwLoadCardB()
    {
        var lap = VwFixtureLap(VlArg("--vw-lap") ?? _trackId);
        if (lap == null) { VwLog("fatal", ("why", "no fixture lap")); yield break; }
        int w = 0;
        while (VwCover() != null && w++ < 60 * 90) yield return null;
        while (_state != State.Racing && w++ < 60 * 150) yield return null;
        foreach (var _ in VwFrames(30)) yield return null;
        var sigBefore = VwSig();
        var s = VwOpen(lap, "loadcard-b", out string err);
        VwCheck(s != null, $"loadcard-b: the viewer opened ({err})");
        if (s == null) yield break;
        w = 0; while (s.State == ReplayViewerState.Building && w++ < 60 * 120) yield return null;
        foreach (var _ in VwFrames(30)) yield return null;
        var sig0 = VwSig();
        // the call a menu choice would make; here nothing but code reaches it (the routed input cannot)
        StartRun(MenuItem.Race);
        w = 0; while (TrackLoadingScreen.Active == null && w++ < 120) yield return null;
        bool shown = TrackLoadingScreen.Active != null;
        VwLog("loadcard-b", ("card_shown_over_open_viewer", shown), ("viewer_state", s.State), ("live_mode", ProcessMode));
        if (shown) VwFinding("loadcard-over-viewer", "RaceScene.TitleCards.cs:159 StartRun has no viewer guard: with a session open it shows a TrackLoadingScreen over the viewer (reachable by School.cs:194 and by any non-input caller; routed input cannot reach it because RawInput returns at RaceScene.cs:958 first).");
        // let the card finish loading (same track: FinishLoadingRun), then press once: the card's callback sets the live ProcessMode
        w = 0; while (TrackLoadingScreen.Active != null && w++ < 60 * 40)
        {
            if (w % 60 == 30) { _vwDrv.SetControl(true); Input.ParseInputEvent(VwKey(Key.Enter, true, 0)); }
            else if (w % 60 == 32) { Input.ParseInputEvent(VwKey(Key.Enter, false, 0)); _vwDrv.SetControl(false); }
            yield return null;
            if (ProcessMode != ProcessModeEnum.Disabled && s.State != ReplayViewerState.Closed) break;
        }
        var sig1 = VwSig();
        VwLog("loadcard-b-after", ("card_active", TrackLoadingScreen.Active != null), ("viewer_state", s.State), ("live_mode", ProcessMode), ("sig_diff", VwDiff(sig0, sig1)));
        if (ProcessMode != ProcessModeEnum.Disabled && s.State != ReplayViewerState.Closed)
            VwFinding("loadcard-resumes-live", $"with the viewer {s.State} the loading card's Loaded() callback (RaceScene.TitleCards.cs:199-206) set the live ProcessMode to {ProcessMode} (and un-muted SFX): the frozen world runs under the viewer.");
        VwCheck(ProcessMode == ProcessModeEnum.Disabled || s.State == ReplayViewerState.Closed, $"loadcard-b: with the viewer open the live world is not resumed by a loading card (mode {ProcessMode}, viewer {s.State})");
        s.Close();
        w = 0; while (s.State != ReplayViewerState.Closed && w++ < 600) yield return null;
        VwLog("loadcard-b-closed", ("viewer_state", s.State), ("live_mode", ProcessMode), ("card_active", TrackLoadingScreen.Active != null));
    }
}
