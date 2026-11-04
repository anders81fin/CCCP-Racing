using System;
using System.Collections.Generic;
using System.Linq;

namespace CCCPRacing.Export;

public enum ExportUiAction { Up, Down, Left, Right, Accept, Back }

/// <summary>What the viewer that hosts the export UI knows (SP viewer or LAN viewer): re-read each time the settings card opens, so the CURRENT working state (saved or unsaved) is exported.</summary>
public sealed record ExportContext
{
    public IExportSource Source { get; init; }
    /// <summary>The viewer's cut window [In, Out) when one is on, else null (only WHOLE is offered).</summary>
    public (float In, float Out)? Cut { get; init; }
    public IReadOnlyList<ExportCameraKey> CameraKeys { get; init; } = Array.Empty<ExportCameraKey>();
    public string DefaultCamera { get; init; } = "TopDown";
    public string Name { get; init; } = "replay";
}

/// <summary>
/// The export card and progress panel as a Godot-free state machine, shared by the SP viewer and (root's) LAN viewer. The host maps its keys/pad buttons to <see cref="ExportUiAction"/>, calls
/// <see cref="Handle"/> while <see cref="IsOpen"/> (true: consumed), <see cref="Tick"/> every frame, and draws <see cref="Rows"/>/<see cref="Lines"/>/<see cref="Fraction"/> (the overlay does it for
/// the viewers' ReplayOverlayView). Nothing here saves or discards the host's edits: the request is a snapshot.
/// </summary>
public sealed class ExportUiController
{
    internal static bool KeepDiagnosticsForTest;
    public enum State { Closed, Settings, Running, ConfirmCancel, Result }

    readonly Func<ExportContext> _context; readonly ExportEnvironment _env; readonly Action<string> _openFolder;
    State _state = State.Closed; int _sel;
    ExportContext _ctx; ExportJob _job; ExportControl _control; ExportProgress _last;
    // the settings (kept for the next export in this session)
    bool _whole = true; int _height = 1080; int _crf = 18; bool _audio = true;
    static readonly int[] Heights = { 1080, 720, 540 };

    public ExportUiController(Func<ExportContext> context, ExportEnvironment env, Action<string> openFolder = null) { _context = context; _env = env; _openFolder = openFolder; }

    public State Current => _state;
    public bool IsOpen => _state != State.Closed;
    public bool Running => _state is State.Running or State.ConfirmCancel;
    public ExportJob Job => _job;
    /// <summary>The job that finished last (kept after the result card so a test can await its Done and inspect the folders it must have cleaned).</summary>
    public ExportJob LastJob { get; private set; }

    public void Open()
    {
        if (_state != State.Closed) return;
        _ctx = _context();
        if (_ctx?.Source == null) return;
        if (_ctx.Cut == null) _whole = true;
        _sel = 0; _state = State.Settings;
    }

    /// <summary>The viewer is going away: a running job is cancelled (its child killed, its job-owned files removed).</summary>
    public void Dispose() { _control?.Cancel(); }

    string DestDir => _env.DefaultDestination;

    // ------------------------------------------------------------------ rows

    public string Title => _state switch { State.Settings => "EXPORT MP4", State.Running or State.ConfirmCancel => "EXPORTING", _ => _last?.Phase == ExportPhase.Done ? "EXPORT DONE" : _last?.Phase == ExportPhase.Cancelled ? "EXPORT CANCELLED" : "EXPORT FAILED" };

    public IReadOnlyList<string> Rows
    {
        get
        {
            if (_state != State.Settings) return Array.Empty<string>();
            var (a, b) = Span();
            return new[]
            {
                _ctx.Cut == null ? "SPAN  WHOLE" : "SPAN  < " + (_whole ? "WHOLE" : "THE CUT") + " >",
                $"SIZE  < {_height}P >", $"QUALITY  < {(_crf <= 18 ? "HIGH" : "SMALL")} >", $"AUDIO  < {(_audio ? "ON" : "OFF")} >",
                $"LENGTH  {Fmt(b - a)}  ({(int)MathF.Round((b - a) * 60)} FRAMES AT 60)",
                "START EXPORT", "OPEN FOLDER  " + Short(DestDir), "BACK",
            };
        }
    }
    public int Selected => _sel;

    public IReadOnlyList<string> Lines
    {
        get
        {
            var p = _last;
            switch (_state)
            {
                case State.Running: return new[] { p?.Message ?? "STARTING" };
                case State.ConfirmCancel: return new[] { "CANCEL THE EXPORT?", "NOTHING IS KEPT", "ENTER YES  ESC KEEP GOING" };
                case State.Result:
                    var l = new List<string> { p.Message };
                    if (p.OutputPath != null) l.Add(Short(p.OutputPath, 52));
                    if (p.LogPath != null) l.Add("LOG " + Short(p.LogPath, 48));
                    l.Add("ENTER OK"); return l;
                default: return Array.Empty<string>();
            }
        }
    }

    /// <summary>0..1 while rendering/encoding, else -1.</summary>
    public float Fraction => _state == State.Running && _last != null ? (float)_last.Fraction : -1f;

    (float, float) Span() => _whole || _ctx.Cut == null ? (_ctx.Source.MinClockSeconds, _ctx.Source.DurationSeconds) : _ctx.Cut.Value;
    static string Fmt(float s) { int m = (int)(s / 60); return $"{m}:{s - m * 60:00.0}"; }
    static string Short(string s, int max = 30) => s.Length <= max ? s : "..." + s[^(max - 3)..];

    // ------------------------------------------------------------------ input

    public bool Handle(ExportUiAction a)
    {
        switch (_state)
        {
            case State.Closed: return false;
            case State.Settings:
                int n = Rows.Count;
                if (a == ExportUiAction.Back) { _state = State.Closed; return true; }
                if (a == ExportUiAction.Up) _sel = (_sel + n - 1) % n;
                else if (a == ExportUiAction.Down) _sel = (_sel + 1) % n;
                else if (a is ExportUiAction.Left or ExportUiAction.Right or ExportUiAction.Accept) Change(a == ExportUiAction.Left ? -1 : 1, a == ExportUiAction.Accept);
                return true;
            case State.Running:
                if (a == ExportUiAction.Back) _state = State.ConfirmCancel;
                return true;
            case State.ConfirmCancel:
                if (a == ExportUiAction.Accept) { _control.Cancel(); _state = State.Running; }
                else if (a == ExportUiAction.Back) _state = State.Running;
                return true;
            case State.Result:
                if (a is ExportUiAction.Accept or ExportUiAction.Back) _state = State.Closed;
                return true;
        }
        return false;
    }

    void Change(int step, bool accept)
    {
        switch (_sel)
        {
            case 0: if (_ctx.Cut != null) _whole = !_whole; break;
            case 1: _height = Heights[(Array.IndexOf(Heights, _height) + step + Heights.Length) % Heights.Length]; break;
            case 2: _crf = _crf <= 18 ? 23 : 18; break;
            case 3: _audio = !_audio; break;
            case 5: if (accept) Start(); break;
            case 6: if (accept) { try { System.IO.Directory.CreateDirectory(DestDir); } catch { } _openFolder?.Invoke(DestDir); } break;
            case 7: if (accept) _state = State.Closed; break;
        }
    }

    void Start()
    {
        var (a, b) = Span();
        var req = new ExportRequest
        {
            InClock = a, OutClock = b, OutHeight = _height, Crf = _crf, Audio = _audio, Fps = 60, DestinationDir = DestDir, Name = _ctx.Name,
            CameraKeys = _ctx.CameraKeys.ToList(), DefaultCamera = _ctx.DefaultCamera, KeepDiagnostics = KeepDiagnosticsForTest,
        };
        _control = new ExportControl();
        _job = ExportJob.Start(_ctx.Source, req, _control, _env);
        _last = _job.Progress; _state = State.Running;
    }

    /// <summary>Every frame while the host lives: follow the job.</summary>
    public void Tick()
    {
        if (_job == null) return;
        _last = _job.Progress;
        // finished only counts once the job's own task is complete (its cleanup has run): then the result card, never before
        if (_last.Finished && _job.Done.IsCompleted && _state is State.Running or State.ConfirmCancel) { _state = State.Result; LastJob = _job; _job = null; }
    }
}
