using System;
using System.Collections.Generic;
using CCCPRacing.Audio;
using CCCPRacing.Replay;
using CCCPRacing.Car;
using CCCPRacing.Track;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// The session owner of a replay viewer (docs/player-replay-phase2-proposal.md): one object that suspends the live scene, builds the
/// viewer world beside it and gives the live scene back exactly. Phase 2 has a test API only; the Party Meeting entry is phase 3.
/// </summary>
public partial class RaceScene
{
    ReplayViewerSession _replayViewer;

    /// <summary>The open viewer session, or null.</summary>
    public ReplayViewerSession ReplayViewer => _replayViewer;

    /// <summary>
    /// Open a viewer for one archived lap. The lap is validated first (nothing of the live scene is touched when it cannot be played);
    /// then the live scene is suspended and the viewer world builds in slices. Returns null and the reason when it cannot start.
    /// </summary>
    public ReplayViewerSession OpenReplayViewer(LapSource lap, out string error)
    {
        error = null;
        if (_replayViewer != null) { error = "a replay viewer is already open"; return null; }
        if (_booting || TrackLoadingScreen.Active != null || !IsInsideTree() || ProcessMode != ProcessModeEnum.Inherit) { error = "the world is not ready"; return null; }      // (not running: a start-up cover or a loading card still owns it)
        if (lap == null) { error = "no lap"; return null; }
        if (lap.Meta.Set < 0 || lap.Meta.Set >= RecordStore.Sets) { error = $"invalid record set {lap.Meta.Set}"; return null; }
        string track = lap.Meta.TrackId;
        TrackDef def;
        try { def = TrackDef.Load($"res://tracks/{track}.json"); }
        catch (Exception e) { error = $"unknown track '{track}': {e.Message}"; return null; }
        long tp = System.Diagnostics.Stopwatch.GetTimestamp();
        var check = LapPlayback.Prepare(lap, CarParams.ForTrack(def), null, track == _trackId ? _records.Signature : null, ghostOnly: true);      // (a read-only look: the real objects are built by the viewer itself)
        if (LoadProf.On) GD.Print($"LOADPROF open: validation Prepare {(System.Diagnostics.Stopwatch.GetTimestamp() - tp) * 1000.0 / System.Diagnostics.Stopwatch.Frequency:F1} ms");
        if (!check.Ok) { error = check.Message; return null; }
        _replayViewer = new ReplayViewerSession(this, lap, track);
        _replayViewer.Start();
        return _replayViewer;
    }

    /// <summary>
    /// The start-up loading cover wants to start the world (PixelFrame). While a viewer is open the world stays frozen and the cover's wish
    /// becomes the mode the session gives back; otherwise it applies at once.
    /// </summary>
    public void CoverSetsProcessMode(ProcessModeEnum mode)
    {
        if (_replayViewer is { State: not ReplayViewerState.Closed } v) v.DeferProcessMode(mode);
        else ProcessMode = mode;
    }

    /// <summary>Close the viewer (idempotent). The live scene is restored once the viewer's deferred teardown has run.</summary>
    public void CloseReplayViewer() => _replayViewer?.Close();

    /// <summary>All viewer sessions alive in the process, to account for worlds independently of the original scene.</summary>
    public static IReadOnlyCollection<ReplayViewerSession> ActiveReplayViewers => ReplayViewerSession.Active;

    public enum ReplayViewerState { Building, Playing, Closing, Closed }

    public sealed partial class ReplayViewerSession : Node
    {
        public static readonly List<ReplayViewerSession> Active = new();

        readonly RaceScene _live;
        readonly LapSource _lap;
        readonly string _track;
        SubViewport _viewport;
        TextureRect _image;
        RaceScene _scene;
        ReplayViewerState _state = ReplayViewerState.Building;
        bool _restored;
        // the live scene as it was before the suspend
        ProcessModeEnum _priorMode;
        bool _priorVisible, _priorHud;
        readonly List<(Node Player, bool Paused)> _audio = new();
        Action _restoreWorld;

        /// <summary>Input events the session received instead of the frozen live scene (counted; a phase 3 viewer UI will consume them).</summary>
        public int RoutedInputCount { get; private set; }
        public event Action<InputEvent> InputRouted;
        internal void RouteInput(InputEvent e) { RoutedInputCount++; InputRouted?.Invoke(e); UiInput(e); }
        internal void DeferProcessMode(ProcessModeEnum mode) => _priorMode = mode;

        public ReplayViewerState State => _state;
        public RaceScene Scene => _scene;
        public RaceScene Live => _live;
        /// <summary>Why the viewer failed to build, or null.</summary>
        public string Error => _scene?.ViewerError;
        /// <summary>Called on the main thread once, when the session is closed and the live scene has been given back.</summary>
        public event Action Restored;

        internal ReplayViewerSession(RaceScene live, LapSource lap, string track) { _live = live; _lap = lap; _track = track; ProcessMode = ProcessModeEnum.Always; Name = "ReplayViewerSession"; }

        internal void Start()
        {
            _live.GetParent().AddChild(this);
            Active.Add(this);
            try
            {
                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                Suspend();
                if (LoadProf.On) GD.Print($"LOADPROF open: Suspend {(System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency:F1} ms");
                long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
                Build();
                if (LoadProf.On) GD.Print($"LOADPROF open: Build (scene enters the tree, first build slice) {(System.Diagnostics.Stopwatch.GetTimestamp() - t1) * 1000.0 / System.Diagnostics.Stopwatch.Frequency:F1} ms");
            }
            catch (Exception e)
            {
                GD.PushError("Replay viewer: start failed: " + e.Message);
                Close();
            }
        }

        // --------------------------------------------------------------- suspend

        bool _sfxWasMuted;
        ImageTexture _still;      // the live game as it was when the viewer opened (no hud), shown behind the loading panel; dropped when the viewer is ready or closed

        /// <summary>
        /// One synchronous capture of the game's own 3D viewport with the live hud hidden (so the Party Meeting is not in it): a forced draw and one readback, measured and logged, never repeated per frame.
        /// Null when there is no renderer to read (the headless tests): the loading panel then stands on a plain scrim.
        /// </summary>
        /// <summary>Test seam: make the capture fail, to prove the live hud and the loading fallback survive a renderer fault.</summary>
        internal static bool FailStillForTest;

        ImageTexture CaptureStill()
        {
            var layer = IsInstanceValid(_live) && IsInstanceValid(_live._hud) ? _live._hud.GetParent() as CanvasLayer : null;
            bool was = layer != null && layer.Visible;      // (read before anything is hidden: the state Suspend and Finish snapshot is the player's, never this brief hide)
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                if (layer != null) layer.Visible = false;
                if (FailStillForTest) throw new InvalidOperationException("injected capture failure");
                if (DisplayServer.GetName() == "headless") return null;      // (no renderer to read: the dummy texture storage answers GetImage with a native ERROR "Parameter t is null" on every open; the loading panel then stands on a plain scrim)
                RenderingServer.ForceDraw(false);
                var img = _live.GetViewport().GetTexture().GetImage();
                if (img == null || img.IsEmpty() || img.GetWidth() < 8) return null;
                var tex = ImageTexture.CreateFromImage(img);
                GD.Print($"Replay viewer: loading still captured in {sw.Elapsed.TotalMilliseconds:F1} ms ({img.GetWidth()}x{img.GetHeight()})");
                return tex;
            }
            catch (Exception e) { GD.PushWarning("Replay viewer: no loading still: " + e.Message); return null; }
            finally { if (layer != null && IsInstanceValid(layer)) layer.Visible = was; }
        }

        void Suspend()
        {
            var live = _live;
            _still = CaptureStill();
            _priorMode = live.ProcessMode; _priorVisible = live.Visible;
            _priorHud = ((CanvasLayer)live._hud.GetParent()).Visible;
            var snapshot = ViewerGlobals.Capture(live);
            _restoreWorld = () => snapshot.Restore(live);
            // The title mutes the sound-effect bus (it plays music only): a viewer opened from the title (PARTY OFFICE > RECORDINGS, the MP4 exporter) would be silent. The viewer's own engine and world sound
            // need the bus open; the live scene's state is given back at the end.
            _sfxWasMuted = AudioMix.SfxMuted; AudioMix.MuteSfx(false);
            foreach (var node in live.FindChildren("*", "", true, false))
            {
                if (node is AudioStreamPlayer a) { _audio.Add((a, a.StreamPaused)); a.StreamPaused = true; }
                else if (node is AudioStreamPlayer3D a3) { _audio.Add((a3, a3.StreamPaused)); a3.StreamPaused = true; }
            }
            ((CanvasLayer)live._hud.GetParent()).Visible = false;
            live.ReleasePadKeys(); live.ResetPadEdgeState();
            live.ProcessMode = ProcessModeEnum.Disabled;
            live.Visible = false;
        }

        void Build()
        {
            var size = (Vector2I)_live.GetViewport().GetVisibleRect().Size;
            _viewport = new SubViewport
            {
                Size = size, OwnWorld3D = true, HandleInputLocally = false, AudioListenerEnable3D = true,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
                CanvasItemDefaultTextureFilter = Viewport.DefaultCanvasItemTextureFilter.Nearest,
            };
            ApplyAntialiasing(_viewport);
            _live.GetParent().AddChild(_viewport);
            _image = new TextureRect { Texture = _viewport.GetTexture(), Size = _viewport.Size, MouseFilter = Control.MouseFilterEnum.Ignore };
            _live.GetParent().AddChild(_image);
            _image.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            _scene = new RaceScene
            {
                InstructionTrack = _track, ViewerMode = true, ViewerSource = new ViewerLapSource { Lap = _lap },
                _dataDir = _live._records.Dir, _noRecord = true, _padMode = _live._padMode,
            };
            _viewport.AddChild(_scene);
            _scene.ViewerLoop = false;
            BuildUi();
        }

        // --------------------------------------------------------------- run

        public override void _Process(double delta)
        {
            if (_state is ReplayViewerState.Building or ReplayViewerState.Playing) UiFrame((float)delta);
            switch (_state)
            {
                case ReplayViewerState.Building:
                    if (_scene.ViewerReady && EditsGate()) { _state = ReplayViewerState.Playing; if (_overlay != null) _overlay.Backdrop = null; _still = null; if (!_scene.ViewerHasTape) SetNotice("NO WORLD TAPE: CAR ONLY", 4f); }
                    else if (_scene.ViewerError != null || !IsInstanceValid(_scene)) { EndedByError = true; Close("REPLAY FAILED: " + Short(_scene?.ViewerError)); }
                    break;
                case ReplayViewerState.Closing:
                    if (_uiLayer != null) _uiLayer.Visible = false;
                    TryFreeWorld();
                    // the viewer's deferred ExitTree has run once its viewport is gone
                    if (_viewport == null || !IsInstanceValid(_viewport)) Finish();
                    break;
            }
        }

        /// <summary>Close the viewer: cancel a building one, queue-free its world, and restore the live scene when the teardown is complete.</summary>
        public void Close() => Close("");

        static string Short(string s) { s = (s ?? "UNKNOWN ERROR").ToUpperInvariant(); return s.Length > 48 ? s[..48] : s; }

        /// <summary>Close with a reason the Party Meeting's list shows (empty: the player simply went back).</summary>
        public void Close(string endNotice)
        {
            if (_state is ReplayViewerState.Closing or ReplayViewerState.Closed) return;
            EndFreeCamera();
            EndNotice = endNotice ?? "";
            _state = ReplayViewerState.Closing;
            try
            {
                if (IsInstanceValid(_scene)) { _scene.ViewerCancel(); _scene.ViewerSetPaused(true); }
                if (IsInstanceValid(_image)) _image.QueueFree();
                TryFreeWorld();
            }
            catch (Exception e) { GD.PushError("Replay viewer: close failed: " + e.Message); }
        }

        bool _worldQueued;

        /// <summary>The world is freed once its build coroutine has ended; a cancelled build runs out first (see ViewerCancel).</summary>
        void TryFreeWorld()
        {
            if (_worldQueued) return;
            if (IsInstanceValid(_scene) && !_scene.ViewerBuildDone) return;
            _worldQueued = true;
            if (IsInstanceValid(_viewport)) _viewport.QueueFree();
        }

        /// <summary>The one place that gives the live scene back. Every step is separate, so one failure cannot skip the rest; it runs once.</summary>
        void Finish()
        {
            if (_restored) return;
            _restored = true;
            var live = _live;
            void Step(string what, Action a) { try { a(); } catch (Exception e) { GD.PushError($"Replay viewer: restore '{what}' failed: {e.Message}"); } }
            if (IsInstanceValid(live))
            {
                Step("process mode", () => { live.ProcessMode = _priorMode; live.Visible = _priorVisible; });      // first: the engine unpauses players when this changes
                Step("sfx bus", () => AudioMix.MuteSfx(_sfxWasMuted));
                Step("audio", () =>
                {
                    foreach (var (p, paused) in _audio)
                    {
                        if (!IsInstanceValid(p)) continue;
                        if (p is AudioStreamPlayer a) a.StreamPaused = paused; else if (p is AudioStreamPlayer3D a3) a3.StreamPaused = paused;
                    }
                });
                Step("world", () => _restoreWorld?.Invoke());
                Step("hud", () => ((CanvasLayer)live._hud.GetParent()).Visible = _priorHud);
                Step("input", () => { live.ReleasePadKeys(); live.ResetPadEdgeState(); live._overlayExitFrame = Engine.GetProcessFrames(); });
            }
            _audio.Clear();
            FreeUi();
            _state = ReplayViewerState.Closed;
            Active.Remove(this);
            if (live._replayViewer == this) live._replayViewer = null;
            try { Restored?.Invoke(); } catch (Exception e) { GD.PushError("Replay viewer: Restored handler failed: " + e.Message); }
            QueueFree();
        }

        public override void _ExitTree()
        {
            // The live scene is going away too (the quit): free the viewer world at once and leave the dying scene as it is.
            if (_state != ReplayViewerState.Closed)
            {
                if (IsInstanceValid(_image)) _image.QueueFree();      // (not Free: the parent may be busy removing children at the quit)
                if (IsInstanceValid(_viewport)) _viewport.QueueFree();
                FreeUi();
                _restored = true;
                _state = ReplayViewerState.Closed;
                Active.Remove(this);
                if (_live != null && _live._replayViewer == this) _live._replayViewer = null;
            }
        }
    }
}
