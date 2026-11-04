using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using CCCPRacing.Export;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// The exporter CHILD process (a normal game start with --export-plan=PATH, launched by ExportJob inside its movie-writer bootstrap). It never touches the player's data: it opens the
/// viewer for the plan's source, shows the exact IN state paused, writes START (the movie image index that shows exactly IN) BEFORE that frame is rendered, plays exactly N frames
/// and ends. It writes markers.txt and frames.jsonl in the job folder, waits when the parent's consumer is behind (backpressure) and exits when cancelled or when the parent is gone.
/// </summary>
public static partial class ExportChild
{
    static ExportPlan _plan; static bool _parsed;
    public static RaceScene Live { get; private set; }

    public static ExportPlan Plan
    {
        get
        {
            if (_parsed) return _plan;
            _parsed = true;
            string arg = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--export-plan="))?["--export-plan=".Length..];
            if (arg == null) return null;
            try { _plan = ExportPlan.FromJson(File.ReadAllText(arg)); }
            catch (Exception e) { GD.PushError("export: cannot read the plan: " + e.Message); }
            return _plan;
        }
    }

    public static bool Active => Plan != null;

    /// <summary>The child renders the native 480x270 pixel frame whatever physical window the compositor gives it: a virtual root viewport (content scale, viewport mode).</summary>
    public static void ApplyViewportScale(Window w)
    {
        bool full = Plan?.Request.DebugFullRes == true;
        w.ContentScaleMode = Window.ContentScaleModeEnum.Viewport;
        w.ContentScaleAspect = Window.ContentScaleAspectEnum.Keep;
        w.ContentScaleSize = full ? new Vector2I(Plan.Request.OutWidth, Plan.Request.OutHeight) : new Vector2I(ExportEnvironment.NativeW, ExportEnvironment.NativeH);
        DisplayServer.WindowSetTitle("CCCP RACING EXPORT");
    }

    /// <summary>Adapter hook (a LAN adapter implements this partial in its own file to register its factory in <see cref="ExportViewerFactories.ByKind"/>).</summary>
    static partial void RegisterExtraKinds(RaceScene live);

    /// <summary>Called by the live scene's _Ready in a child started with --export-plan.</summary>
    public static void Begin(RaceScene live)
    {
        Live = live;
        ApplyViewportScale(live.GetTree().Root);
        ExportViewerFactories.ByKind["sp-pair"] = plan => new SpExportViewer(plan);
        RegisterExtraKinds(live);
        var driver = new ExportDriver { Name = "ExportDriver", ProcessMode = Node.ProcessModeEnum.Always, ProcessPriority = -100000 };
        live.GetTree().Root.CallDeferred(Node.MethodName.AddChild, driver);
    }
}

public partial class ExportDriver : Node
{
    readonly ExportPlan _plan = ExportChild.Plan;
    IEnumerator _script; StreamWriter _markers, _frames; string _movie, _cancel; ulong _frame;
    int _start = -1;

    public override void _Ready()
    {
        _movie = Path.Combine(_plan.JobDir, "movie"); _cancel = Path.Combine(_plan.JobDir, "cancel");
        _markers = new StreamWriter(new FileStream(Path.Combine(_plan.JobDir, "markers.txt"), FileMode.Create, System.IO.FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
        _frames = new StreamWriter(new FileStream(Path.Combine(_plan.JobDir, "frames.jsonl"), FileMode.Create, System.IO.FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
        _markers.WriteLine("CAMERA_KEYS 2");
        _markers.WriteLine("CAMERA_MOTION 3");
        _markers.WriteLine($"WINDOW mode={DisplayServer.WindowGetMode()} no_focus={GetTree().Root.Unfocusable}");
        _script = Script().GetEnumerator();
    }

    void Fail(string text) { _markers.WriteLine("ERROR " + text.Replace('\n', ' ')); GD.PushError("export: " + text); GetTree().Quit(2); _script = null; }

    bool ParentGone() => _plan.ParentPid > 0 && !Directory.Exists("/proc/" + _plan.ParentPid.ToString(CultureInfo.InvariantCulture));

    int Outstanding()
    {
        try { return Directory.EnumerateFiles(_movie, "m*.png").Count(); } catch { return 0; }
    }

    public override void _Process(double delta)
    {
        if (_script == null) return;
        // backpressure BEFORE advancing another movie frame: wait while the parent's consumer is behind (bounded; the parent's liveness and the cancel file are checked while waiting)
        long t0 = (long)Time.GetTicksMsec();
        while (Outstanding() > ExportJob.MaxOutstanding)
        {
            OS.DelayMsec(2);
            if (File.Exists(_cancel)) { GetTree().Quit(3); _script = null; return; }
            if (ParentGone()) { GetTree().Quit(4); _script = null; return; }
            if ((long)Time.GetTicksMsec() - t0 > 120_000) { Fail("the export queue stalled"); return; }
        }
        if (File.Exists(_cancel)) { GetTree().Quit(3); _script = null; return; }
        if ((Engine.GetProcessFrames() & 31) == 0 && ParentGone()) { GetTree().Quit(4); _script = null; return; }
        try { if (!_script.MoveNext()) _script = null; }
        catch (Exception e) { Fail("the exporter failed: " + e.Message); }
    }

    IEnumerable Script()
    {
        var live = ExportChild.Live;
        // the world must be up: the title scene running, no loading card
        for (int i = 0; i < 60 * 240 && !live.ExportReady; i++) yield return null;
        if (!live.ExportReady) { Fail("the game did not start"); yield break; }
        for (int i = 0; i < 30; i++) yield return null;
        if (!ExportViewerFactories.ByKind.TryGetValue(_plan.SourceKind, out var factory)) { Fail("no viewer for source kind " + _plan.SourceKind); yield break; }
        IExportViewer viewer = factory(_plan);
        for (int i = 0; i < 60 * 240 && !viewer.Ready && viewer.Error == null; i++) yield return null;
        if (viewer.Error != null || !viewer.Ready) { Fail(viewer.Error ?? "the viewer did not become ready"); yield break; }
        var req = _plan.Request;
        if (req.ValidateCameras(viewer.Duration) is string cameraError) { Fail(cameraError); yield break; }
        if (req.HasFreeCameraKeys && _plan.SourceKind != "sp-pair") { Fail("free camera export is not supported for this source"); yield break; }
        viewer.Configure(_plan);
        viewer.SeekPaused(req.InClock);
        for (int i = 0; i < 8; i++) yield return null;          // (settle: the paused camera step, effects; these images are never included)
        viewer.SeekPaused(req.InClock);                          // (exactly IN again, so the first included image is IN whatever the settle did)
        yield return null;
        // this very frame shows IN: START is written before the frame is rendered; from here RenderFrame(IN + k/fps, k) is called once per frame at the start of its _process
        _start = (int)Engine.GetProcessFrames();
        _markers.WriteLine("START " + _start.ToString(CultureInfo.InvariantCulture));
        int total = req.FrameCount;
        for (int k = 0; k < total; k++)
        {
            if (k > 0) yield return null;
            if ((int)Engine.GetProcessFrames() != _start + k) { Fail($"a frame was skipped (process frame {Engine.GetProcessFrames()}, expected {_start + k})"); yield break; }
            viewer.RenderFrame(req.InClock + k / (float)req.Fps, k);
            LogFrame(viewer);
        }
        yield return null;                                       // (the last included image is _start + total - 1: it is rendered after this frame's _process)
        _markers.WriteLine("END " + (_start + total));
        GetTree().Quit();
    }

    void LogFrame(IExportViewer v) =>
        _frames.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{{\"f\":{Engine.GetProcessFrames()},\"clock\":{v.Clock:0.00000},\"paused\":{(v.Paused ? 1 : 0)},\"cam\":\"{v.CameraName}\"}}"));
}
