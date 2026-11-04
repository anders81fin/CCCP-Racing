using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Linq;
using CCCPRacing.Replay;

namespace CCCPRacing.Export;

// MP4 export of a replay (docs/replay-mp4-export-proposal.md): the shared CONTRACTS. Everything the common SP exporter and a LAN adapter (root) agree on lives in this file:
//   * ExportPlan       - the JSON the parent hands to the exporter child process (--export-plan=<path>);
//   * ExportRequest    - what the viewer UI (or a CLI) asks for;
//   * IExportSource    - parent side: stage the immutable source bytes into the job dir (SP pair, LAN journal);
//   * IExportViewer + ExportViewerFactories - child side: open the viewer for a plan's source and expose the few controls the exporter drives;
//   * ExportProgress / ExportControl - progress snapshots and cancel, polled by any UI.
// Nothing here knows about LAN or about the single-player viewer's internals.

public enum ExportPhase { Preparing, Starting, Rendering, Encoding, Finishing, Done, Cancelled, Failed }

/// <summary>A camera key of the exported span: this camera mode from time T (lap clock seconds) on. Modes are TopDown, Bonnet, Cinematic, Auto, or Free with a required immutable pose.</summary>
public sealed record ExportCameraKey(float T, string Mode, LapFreeCamera Free = null);

/// <summary>What the player asked for. The span is [InClock, OutClock): the first image is exactly InClock, the last is OutClock minus one frame.</summary>
public sealed record ExportRequest
{
    public float InClock { get; init; }
    public float OutClock { get; init; }
    /// <summary>Camera keys and the lap's own camera of the CURRENT working copy (saved or not): the export never saves or discards edits.</summary>
    public List<ExportCameraKey> CameraKeys { get; init; } = new();
    public string DefaultCamera { get; init; } = "TopDown";
    /// <summary>Output size: 1080, 720 or 540 (lines); the native 480x270 pixel frame is scaled by nearest neighbour.</summary>
    public int OutHeight { get; init; } = 1080;
    public int Fps { get; init; } = 60;
    public int Crf { get; init; } = 18;
    public bool Audio { get; init; } = true;
    /// <summary>Destination folder (the final file is written there as NAME.mp4.part then renamed, never over an existing file). Null: the default exports folder.</summary>
    public string DestinationDir { get; init; }
    /// <summary>File name without extension; the exporter makes it unique.</summary>
    public string Name { get; init; } = "replay";
    /// <summary>Developer tests only: keep markers/frames/logs next to the output as NAME.diag/ (they are job-owned files otherwise deleted with the job).</summary>
    public bool KeepDiagnostics { get; init; }
    /// <summary>Developer tests only: render the child at the full OUTPUT size (a real 1080p PixelFrame) instead of the native 480x270 frame, to prove the two give the same picture.</summary>
    public bool DebugFullRes { get; init; }
    /// <summary>Developer tests only: also keep the child's whole movie WAV and the trimmed raw audio in NAME.diag/ (tens of MB).</summary>
    public bool DebugKeepAudio { get; init; }

    [JsonIgnore] public bool HasFreeCameraKeys => CameraKeys?.Any(k => k?.Mode == LapEdits.Free) == true;

    [JsonIgnore] public bool HasCameraMotion => CameraKeys?.Any(k => k?.Free?.Motion != null) == true;

    public string ValidateCameras(float duration)
    {
        if (Array.IndexOf(LapEdits.PresetCameraModes, DefaultCamera) < 0) return "THE DEFAULT CAMERA MODE IS NOT SUPPORTED";
        if (CameraKeys == null || CameraKeys.Count > LapEdits.MaxCameraKeys) return "THE CAMERA KEY LIST IS INVALID";
        float previous = float.NegativeInfinity;
        foreach (var key in CameraKeys)
        {
            if (key == null || !float.IsFinite(key.T) || key.T < 0 || key.T > duration + .001f || key.T <= previous)
                return "CAMERA KEY TIMES MUST BE SORTED, UNIQUE AND INSIDE THE RECORDING";
            if (Array.IndexOf(LapEdits.CameraModes, key.Mode) < 0) return "A CAMERA KEY MODE IS NOT SUPPORTED";
            if (key.Mode == LapEdits.Free)
            {
                if (key.Free == null) return "A FREE CAMERA KEY IS MISSING ITS POSE";
                if (key.Free.Validate() is string error) return error.ToUpperInvariant();
            }
            else if (key.Free != null) return "A PRESET CAMERA KEY CANNOT CARRY A FREE POSE";
            previous = key.T;
        }
        return null;
    }

    public int FrameCount
    {
        get
        {
            float span = OutClock - InClock;
            if (!float.IsFinite(span) || Fps <= 0 || span <= 0) return 0;
            return (int)Math.Min(int.MaxValue / 4, MathF.Round(span * Fps));
        }
    }
    public int OutWidth => OutHeight switch { 1080 => 1920, 720 => 1280, 540 => 960, _ => OutHeight * 16 / 9 };
}

/// <summary>The JSON file the exporter child reads. Written by the parent (ExportJob) after the source adapter staged its files.</summary>
public sealed record ExportPlan
{
    public const int CurrentVersion = 3;
    public int Version { get; init; } = CurrentVersion;
    public string JobId { get; init; } = "";
    /// <summary>Which child-side factory opens the viewer: "sp-pair" (common) or an adapter's own kind ("lan-journal").</summary>
    public string SourceKind { get; init; } = "sp-pair";
    public string JobDir { get; init; } = "";
    /// <summary>The directory (inside the job dir) that holds the staged immutable source files for the factory.</summary>
    public string SourceDir { get; init; } = "";
    /// <summary>The source's own id inside SourceDir (the SP lap id; a LAN journal's file name).</summary>
    public string SourceId { get; init; } = "";
    public string TrackId { get; init; } = "";
    public ExportRequest Request { get; init; } = new();
    /// <summary>The parent's process id: the child exits when it is gone. 0: do not check.</summary>
    public int ParentPid { get; init; }
    /// <summary>Extra values an adapter needs on the child side (opaque to the common code).</summary>
    public Dictionary<string, string> Extra { get; init; } = new();

    static readonly JsonSerializerOptions Options = new() { WriteIndented = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
    public string ToJson() => JsonSerializer.Serialize(this, Options);
    public static ExportPlan FromJson(string json)
    {
        var p = JsonSerializer.Deserialize<ExportPlan>(json, Options) ?? throw new JsonException("empty plan");
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        if (!doc.RootElement.TryGetProperty("Version", out _)) p = p with { Version = 1 };
        if (p.Version < 1 || p.Version > CurrentVersion || p.Request == null) throw new JsonException("unsupported export plan version or missing request");
        if (p.Version == 1 && p.Request.HasFreeCameraKeys) throw new JsonException("free-camera payload requires export plan version 2");
        if (p.Version < 3 && p.Request.HasCameraMotion) throw new JsonException("animated cameras require export plan version 3");
        return p;
    }
}

/// <summary>Parent side: the immutable source of one export. Called on the export worker thread, never the UI thread.</summary>
public interface IExportSource
{
    /// <summary>The source kind the child factory is registered under.</summary>
    string Kind { get; }
    string Title { get; }
    string TrackId { get; }
    /// <summary>The LAST clock the source can show (seconds on the source's own clock).</summary>
    float DurationSeconds { get; }
    /// <summary>The FIRST clock the source can show: 0 for a lap, the recorded GO time for a LAN race (its journal also holds the countdown). "Whole" means [MinClockSeconds, DurationSeconds]; a request outside it is refused.</summary>
    float MinClockSeconds => 0f;
    /// <summary>Copy the immutable source bytes into <paramref name="sourceDir"/> (a job-owned folder) and return the id the child opens and the files created (relative to sourceDir). The ORIGINAL files are only read.</summary>
    ExportStaged Stage(string sourceDir);
}

public sealed record ExportStaged(string SourceId, IReadOnlyList<string> Files, long Bytes, Dictionary<string, string> Extra = null);

/// <summary>Child side: the viewer for a plan's source and the minimal controls the exporter drives. All members are called on the main thread.</summary>
public interface IExportViewer
{
    bool Ready { get; }
    /// <summary>A reason the viewer could not be opened/built, or null.</summary>
    string Error { get; }
    float Clock { get; }
    float Duration { get; }
    bool Paused { get; }
    string CameraName { get; }
    /// <summary>Install the cut window [In, Out) of the request, its camera keys and default camera, loop off, speed 1x, and hide every overlay/hud element.</summary>
    void Configure(ExportPlan plan);
    /// <summary>Called a few times BEFORE the first included frame: show the exact state at <paramref name="clock"/> (= IN), paused, with the camera keys in force at that clock. Never called once frames are being captured.</summary>
    void SeekPaused(float clock);
    /// <summary>
    /// THE per-frame drive. Called exactly once per captured movie frame, k = 0..N-1, with clock = IN + k/fps, at the START of that frame's _process, before the frame is rendered. When it
    /// returns, the world the frame shows must be the state at <paramref name="clock"/>. k = 0 shows exactly IN (no time passes before it). An implementation either advances its own
    /// state by clock minus the previous call's clock (an externally driven LAN viewer: first call dt 0) or lets its own fixed-step clock run in lock-step with the movie (the SP viewer
    /// unpauses at k = 0 and VERIFIES |Clock - clock| stays within half a frame, failing the export otherwise). SeekPaused is never used per frame.
    /// </summary>
    void RenderFrame(float clock, int index);
}

/// <summary>
/// Child side registry. When: the child process starts with --export-plan; the live scene's _Ready calls ExportChild.Begin, which FIRST makes the root window a virtual 480x270 viewport
/// (content scale, viewport mode: the game's PixelFrame then renders its native pixel frame at scale 1, whatever physical window the compositor gives), THEN registers "sp-pair" and calls the
/// partial hook <c>ExportChild.RegisterExtraKinds(RaceScene live)</c> (a LAN adapter implements that partial in its own file and adds its kind), THEN the driver creates the viewer through the
/// factory once the title scene is up. The factory builds its viewer the way the SP viewer does: as a child of <c>ExportChild.Live.GetParent()</c> inside the same PixelFrame-driven root; it must NOT create another
/// window or PixelFrame. The viewer may build in slices: Ready/Error are polled every frame.
/// </summary>
public static class ExportViewerFactories
{
    public static readonly Dictionary<string, Func<ExportPlan, IExportViewer>> ByKind = new();
}

/// <summary>A snapshot of a running export for any UI. Immutable; replaced as the job advances.</summary>
public sealed record ExportProgress(ExportPhase Phase, int FramesDone, int FramesTotal, string Message, double Seconds, string OutputPath = null, string LogPath = null, bool Error = false)
{
    public bool Finished => Phase is ExportPhase.Done or ExportPhase.Cancelled or ExportPhase.Failed;
    public double Fraction => FramesTotal <= 0 ? 0 : Math.Clamp(FramesDone / (double)FramesTotal, 0, 1);
}

/// <summary>Cancel handle shared by the UI and the job (thread safe).</summary>
public sealed class ExportControl
{
    volatile bool _cancel;
    public bool CancelRequested => _cancel;
    public void Cancel() => _cancel = true;
}
