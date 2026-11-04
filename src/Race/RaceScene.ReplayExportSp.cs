using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CCCPRacing.Export;
using CCCPRacing.Replay;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

/// <summary>The single-player side of MP4 export: the parent-side source (immutable staged copy of a lap's pair, also for memory-only THIS SESSION laps) and the child-side viewer.</summary>
public partial class RaceScene
{
    /// <summary>The live scene is running the title/menu (no loading card): the child may open the viewer.</summary>
    public bool ExportReady => !ViewerMode && _hud != null && _dayNight != null && !_booting && _timer != null && _state == State.Title && TrackLoadingScreen.Active == null && ProcessMode == ProcessModeEnum.Inherit;

    /// <summary>The export's host values, gathered on the main thread: executable, project (dev), the default exports folder (user://exports) and the job-owned temp root, the profile's settings folder, a configured ffmpeg path.</summary>
    public ExportEnvironment ExportEnvironmentHere()
    {
        string user = OS.GetUserDataDir();
        bool dev = OS.HasFeature("editor");      // developer output belongs under media/ (AGENTS.md); the shipped game writes to user://exports
        string devRoot = dev ? ProjectSettings.GlobalizePath("res://media/replay-exports").TrimEnd('/') : null;
        string dest = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--export-dir="))?["--export-dir=".Length..] ?? devRoot ?? Path.Combine(user, "exports");
        string jobs = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--export-jobs-dir="))?["--export-jobs-dir=".Length..] ?? (devRoot != null ? Path.Combine(devRoot, "jobs") : Path.Combine(user, "exports", ".jobs"));
        string ffmpeg = null;
        var cfg = new ConfigFile();
        if (cfg.Load(_records.Dir + "settings.cfg") == Error.Ok) ffmpeg = cfg.GetValue("export", "ffmpeg", "").AsString();
        return new ExportEnvironment(OS.GetExecutablePath(), dev ? ProjectSettings.GlobalizePath("res://").TrimEnd('/') : null, jobs, dest,
            ProjectSettings.GlobalizePath(_records.Dir).TrimEnd('/'), string.IsNullOrWhiteSpace(ffmpeg) ? null : ffmpeg, System.Environment.ProcessId, ExportSettingsSnapshot());
    }

    /// <summary>The settings.cfg the exporter child reads: the disk file with the CURRENT runtime state laid over it (sound balance, edges, softening), so what is exported sounds and looks as it does now.</summary>
    public string ExportSettingsSnapshot()
    {
        var cfg = new ConfigFile();
        cfg.Load(_records.Dir + "settings.cfg");
        for (int i = 0; i < Audio.SoundBalance.Count; i++) cfg.SetValue("sound", Audio.SoundBalance.Keys[i], Audio.SoundBalance.Levels[i]);
        cfg.SetValue("video", "antialiasing", _aaMode); cfg.SetValue("video", "softening", _softLevel);
        return cfg.EncodeToText();
    }

    /// <summary>Parent side: stage a lap's immutable bytes (ghost, tape, meta, layout) as a retained-lap folder the child opens with the normal archive. The original lap is only read.</summary>
    public sealed class SpExportSource : IExportSource
    {
        readonly LapSource _lap; readonly string _title; readonly float _duration;
        /// <param name="duration">the ghost's own duration (the viewer's clock range); the meta's lap seconds when omitted</param>
        public SpExportSource(LapSource lap, string title, float duration = -1f) { _lap = lap; _title = title; _duration = duration > 0 ? duration : lap.Meta.LapSeconds; }
        public string Kind => "sp-pair";
        public string Title => _title;
        public string TrackId => _lap.Meta.TrackId;
        public float DurationSeconds => _duration;
        public ExportStaged Stage(string sourceDir)
        {
            string id = _lap.Meta.Id, dir = Path.Combine(sourceDir, id); Directory.CreateDirectory(dir);
            var files = new List<string>(); long bytes = 0;
            void W(string name, byte[] data) { File.WriteAllBytes(Path.Combine(dir, name), data); files.Add(Path.Combine(id, name)); bytes += data.Length; }
            W("ghost.bin", _lap.GhostBytes.ToArray());
            if (_lap.HasTape) W("tape.bin", _lap.TapeBytes.ToArray());
            W("meta.json", System.Text.Encoding.UTF8.GetBytes((_lap.Meta with { Retained = true }).ToJson()));
            W("records.ver", System.Text.Encoding.UTF8.GetBytes(_lap.Meta.Layout + "\n"));
            return new ExportStaged(id, files, bytes);
        }
    }
}

/// <summary>Child side: the viewer for a staged SP pair. Everything runs on the main thread through the normal Party Meeting / RECORDINGS viewer machinery.</summary>
public sealed class SpExportViewer : IExportViewer
{
    readonly RaceScene _live = ExportChild.Live;
    readonly RaceScene.ReplayViewerSession _s;
    public string Error { get; private set; }

    public SpExportViewer(ExportPlan plan)
    {
        var archive = LapArchive.For(plan.SourceDir);
        var lap = archive.Open(plan.SourceId, out string why);
        if (lap == null) { Error = "cannot open the staged lap: " + why; return; }
        _s = _live.OpenReplayViewer(lap, out string err);
        if (_s == null) Error = "cannot open the viewer: " + err;
    }

    RaceScene Scene => _s?.Scene;
    public bool Ready => _s != null && _s.State == RaceScene.ReplayViewerState.Playing && Scene != null && Scene.ViewerReady;
    public float Clock => Scene?.ViewerClock ?? 0f;
    public float Duration => Scene?.ViewerDuration ?? 0f;
    public bool Paused => Scene?.ViewerPaused ?? true;
    public string CameraName => Scene?.ViewerFreeCameraActive == true ? LapEdits.Free : Scene?.ViewerCamera.ToString() ?? "";
    string _poseLog;

    public void Configure(ExportPlan plan)
    {
        var r = plan.Request; var sc = Scene;
        if (r.ValidateCameras(sc.ViewerDuration) is string cameraError) throw new InvalidOperationException(cameraError);
        if (r.KeepDiagnostics) { _poseLog = Path.Combine(plan.JobDir, "free-camera-poses.jsonl"); File.WriteAllText(_poseLog, ""); }
        var avail = _s.AvailableCameraModes();
        RaceScene.ViewerCameraMode Mode(string m)
        {
            var mode = Enum.TryParse<RaceScene.ViewerCameraMode>(m, true, out var x) ? x : RaceScene.ViewerCameraMode.TopDown;
            return avail.Contains(mode) ? mode : RaceScene.ViewerCameraMode.TopDown;      // a camera the profile has not found: top-down, never an unlock
        }
        var edits = new RaceScene.ViewerEditPlan { TrimEnabled = true, In = r.InClock, Out = r.OutClock, DefaultCamera = Mode(r.DefaultCamera) };
        foreach (var k in r.CameraKeys.OrderBy(k => k.T))
        {
            edits.Keys.Add((k.T, k.Free != null ? RaceScene.ViewerCameraMode.TopDown : Mode(k.Mode)));
            if (k.Free != null) edits.FreeKeys[k.T] = k.Free;
        }
        sc.ViewerEdits = edits;
        sc.ViewerLoop = false; sc.ViewerSpeed = 1f;
        sc.ViewerCamera = edits.DefaultCamera;
        _s.HideForExport();
    }

    public void SeekPaused(float clock) { Scene.ViewerSeek(clock); Scene.ViewerSetPaused(true); }

    /// <summary>The SP viewer runs its own fixed-step clock in lock-step with the movie (120 Hz physics, 2 ticks per 60 fps frame): k = 0 shows IN paused and unpauses at the end of the call; every later frame
    /// is checked against the exact clock (half a frame tolerance): a drift fails the export instead of writing wrong footage.</summary>
    public void RenderFrame(float clock, int index)
    {
        if (index == 0) Scene.ViewerSetPaused(false);
        else
        {
            float tol = 0.5f / 60f;
            if (Scene.ViewerEnded || MathF.Abs(Scene.ViewerClock - clock) > tol) throw new InvalidOperationException($"the viewer clock {Scene.ViewerClock:0.0000} is not {clock:0.0000} at frame {index}");
        }
        Scene.ViewerPrepareExportCamera(clock);
        if (_poseLog != null)
        {
            var pose = Scene.ViewerCameraPose; var car = Scene.ViewerCarPosition;
            File.AppendAllText(_poseLog, System.Text.Json.JsonSerializer.Serialize(new { index, clock, sourceClock = Scene.ViewerClock,
                free = Scene.ViewerFreeCameraActive, eye = new[] { pose.Origin.X, pose.Origin.Y, pose.Origin.Z },
                forward = new[] { -pose.Basis.Z.X, -pose.Basis.Z.Y, -pose.Basis.Z.Z },
                car = new[] { car.X, car.Y, car.Z }, fov = Scene.ViewerCameraFov }) + "\n");
        }
    }
}
