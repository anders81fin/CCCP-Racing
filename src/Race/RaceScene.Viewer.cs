using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CCCPRacing.Replay;
using CCCPRacing.Track;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// Replay viewer mode (docs/player-replay-phase2-proposal.md). A viewer is a second RaceScene in a SubViewport with its own
/// World3D; the live scene is frozen beside it. Viewer mode gates every write, gameplay tick, menu and global input path.
/// </summary>
public partial class RaceScene
{
    /// <summary>True for a scene that only plays back an archived lap; never set on the live scene.</summary>
    public bool ViewerMode { get; set; }

    /// <summary>The command line for the scene's test hooks; a viewer never reads it, so a flag meant for the live scene cannot leak in.</summary>
    string[] CmdArgs() => ViewerMode ? Array.Empty<string>() : OS.GetCmdlineUserArgs();

    /// <summary>
    /// A viewer's stand-in for LoadPlayerFiles: the record store computes paths only (no migration, no stale-file clean-up), the campaign
    /// and speed records stay in memory (their null-dir defaults), and no ghost, tape, board, pad, school or LAN setup runs.
    /// </summary>
    void ViewerInitFiles()
    {
        _records = RecordStore.Inert(_track.Id, TrackSignature.Compute(_track));
        _noRecord = true;
    }

    /// <summary>What a viewer plays: one archived lap's immutable bytes. The viewer parses its own ghost and tape from them with its own car parameters and track, so nothing is shared with the live scene.</summary>
    public sealed class ViewerLapSource
    {
        public LapSource Lap;
    }

    /// <summary>Set before the scene enters the tree.</summary>
    public ViewerLapSource ViewerSource { get; set; }

    /// <summary>The viewer's camera: the player's own gameplay views (top-down is the default, bonnet and cinematic only where the player has found them) or the title replay's automatic director.</summary>
    public enum ViewerCameraMode { TopDown, Bonnet, Cinematic, Auto }
    ViewerCameraMode _viewerCamera = ViewerCameraMode.Auto;      // (a replay always opens in the automatic broadcast)
    bool _viewerCamDirty = true;
    public ViewerCameraMode ViewerCamera
    {
        get => _viewerCamera;
        set
        {
            if (_viewerCamera == value && _viewerSavedFree == null) return;
            if (!_viewerFreeControl && _viewerSavedFree != null) ClearFreeCamera();
            _viewerCamera = value;
            _gameplayView = value switch { ViewerCameraMode.Bonnet => GameplayView.Bonnet, ViewerCameraMode.Cinematic => GameplayView.Cinematic, _ => GameplayView.TopDown };
            if (ViewerFreeCameraActive) return;
            if (ViewerReady) { _replayCamera.Reset(); _gameplayCamera.Reset(); _cam.Snap(_car.Pos); ViewerLeaveDirector(); _viewerCamDirty = true; }      // (the new camera shows at once, also while paused)
        }
    }

    // Auto's director changes fov and clip planes; the gameplay views set their own: each is put back when the camera changes over.
    void ViewerLeaveDirector()
    {
        if (!_replayCameraActive) return;
        _cam.Fov = _replayOldFov; _cam.Near = _replayOldNear; _cam.Far = _replayOldFar;
        _replayCameraActive = false; _replayCamera.Reset(); _tapePlanLastShot = -1;
    }
    void ViewerLeaveGameplayCamera()
    {
        if (!_gameplayCameraActive) return;
        _cam.Fov = _gameplayOldFov; _cam.Near = _gameplayOldNear; _cam.Far = _gameplayOldFar;
        _gameplayCameraActive = false; _gameplayCamera.Reset();
    }
    internal void ViewerGameplayCameraFrameForTest(float dt) => ViewerGameplayCameraFrame(dt);      // (the windowed camera equivalence test drives it with controlled inputs)
    void ViewerGameplayCameraFrame(float vdt)
    {
        ViewerLeaveDirector();
        if (_viewerCamera != ViewerCameraMode.TopDown && GameplayCamera(vdt)) return;
        ViewerLeaveGameplayCamera();
        _cam.MaxLead = _car.Speed > 69f ? 8f : float.PositiveInfinity;      // (as in driving, where overdrive limits the look-ahead: at 250+ km/h a Stakhanov rocket lap would run off the bottom of the screen; the ghost carries no overdrive flag)
        CameraFeel(vdt, replayDriving: true);      // (the same framing as driving: the speed's pull-back included, no zoom of its own)
        _cam.Follow(_car.Pos, _car.Vel, vdt);
    }

    /// <summary>The saved editorial choices that act on playback (docs/player-replay-phase4b-design.md): the cut window and the camera keys. Empty by default: the whole lap, the chosen camera throughout.</summary>
    public sealed class ViewerEditPlan
    {
        public bool TrimEnabled = true;
        public float? In, Out;
        /// <summary>Camera mode from time T on; sorted, unique T (the session keeps it so).</summary>
        public readonly System.Collections.Generic.List<(float T, ViewerCameraMode Mode)> Keys = new();
        public readonly Dictionary<float, LapFreeCamera> FreeKeys = new();
        /// <summary>The camera before the first key (a seek or wrap there goes back to it).</summary>
        public ViewerCameraMode DefaultCamera = ViewerCameraMode.Auto;
        public ViewerCameraMode? KeyModeAt(float clock)
        {
            ViewerCameraMode? m = null;
            foreach (var k in Keys) { if (k.T <= clock + 1e-4f) m = k.Mode; else break; }
            return m;
        }
    }

    /// <summary>Set by the session before the viewer plays (and when the player edits).</summary>
    public ViewerEditPlan ViewerEdits { get; set; } = new();

    /// <summary>A valid, enabled cut: 0 <= In < Out <= duration (a collapsed or out-of-range window is ignored, never trusted).</summary>
    public bool ViewerWindowed
    {
        get
        {
            var e = ViewerEdits; float d = ViewerDuration;
            if (!e.TrimEnabled || (e.In == null && e.Out == null)) return false;
            float a = e.In ?? 0f, b = e.Out ?? d;
            return float.IsFinite(a) && float.IsFinite(b) && a >= 0f && a < b && b <= d + 1e-3f;
        }
    }
    public float ViewerWindowStart => ViewerWindowed ? ViewerEdits.In ?? 0f : 0f;
    public float ViewerWindowEnd => ViewerWindowed ? Mathf.Min(ViewerEdits.Out ?? ViewerDuration, ViewerDuration) : ViewerDuration;

    int _viewerKeyApplied = -1;
    /// <summary>The camera key in force at the clock (the last key at or before it) becomes the camera; during playback only when a key boundary was crossed (a manual camera choice holds until the next key), on a seek always.</summary>
    void ViewerApplyCameraKeys(bool force, float? keyClock = null)
    {
        var keys = ViewerEdits.Keys;
        if (keys.Count == 0) { _viewerKeyApplied = -1; if (_viewerSavedFree != null && !_viewerFreeControl) ViewerEndFreeCamera(); return; }
        int idx = -1;
        float clock = keyClock ?? _replayClock;
        for (int i = 0; i < keys.Count; i++) { if (keys[i].T <= clock + 1e-4f) idx = i; else break; }
        if (_viewerFreeControl) { _viewerKeyApplied = idx; return; }
        if (idx < 0) { if (force || _viewerKeyApplied >= 0) { _viewerKeyApplied = -1; ClearFreeCamera(); ViewerCamera = ViewerEdits.DefaultCamera; } return; }      // (before the first key: the lap's own camera, put back on a seek or wrap)
        if (!force && idx == _viewerKeyApplied) return;
        _viewerKeyApplied = idx;
        if (ViewerEdits.FreeKeys.TryGetValue(keys[idx].T, out var free)) ViewerSelectFreeKey(free, keys[idx].T);
        else { ClearFreeCamera(); ViewerCamera = keys[idx].Mode; }
    }

    /// <summary>Playback controls, driven by the session owner. Speed is this scene's own dt factor; Engine.TimeScale is never touched.</summary>
    public bool ViewerPaused { get; private set; }
    public bool ViewerLoop { get; set; } = true;      // (the phase 3 player turns it off: a lap plays once and stops at its end)
    public float ViewerSpeed { get; set; } = 1f;
    public bool ViewerReady { get; private set; }
    /// <summary>Why the viewer could not start (null while building or playing).</summary>
    public string ViewerError { get; private set; }
    /// <summary>True once a non-looping playback reached the end of the lap.</summary>
    public bool ViewerEnded { get; private set; }
    public float ViewerClock => _replayClock;
    public Vector3 ViewerCarPosition => _car.Pos;
    /// <summary>The recorded events of the lap being viewed (tests choose real events to cut around); empty without a world tape.</summary>
    public System.Collections.Generic.IReadOnlyList<TapeEvent> ViewerTapeEvents() => (IReadOnlyList<TapeEvent>)_tapes[TitleSet]?.Body?.Events ?? Array.Empty<TapeEvent>();
    /// <summary>Effects and projectiles the viewer's tape fx player has made so far (a test counts the ones a cut replays).</summary>
    public int ViewerFxCount => _tapeFx == null ? 0 : _tapeFx.Made.Values.Sum() + _tapeFx.ProjectilesFlown;
    public int[] ViewerLiveInstanceIds() => _tapePlayer == null ? Array.Empty<int>() : _tapePlayer.LiveInstances.Keys.OrderBy(k => k).ToArray();
    public ulong ViewerPoseHash() => TapePoseHash();
    public ulong ViewerLastWrapHash { get; private set; }
    public int ViewerWraps { get; private set; }
    public float ViewerLastWrapClock { get; private set; }
    public Vector3 ViewerLastWrapPose { get; private set; }
    public ViewerCameraMode ViewerLastWrapCamera { get; private set; }
    public float ViewerDuration => TitleGhost?.Duration ?? 0f;
    public bool ViewerHasTape => _tapePlayer != null;

    /// <summary>
    /// The viewer's stand-in for EnterTitle: the lap's ghost and tape go into this scene's own slots and the title replay machinery plays them.
    /// No menu, radio, run mode, global bus change or record is involved; the hud layer is hidden (phase 3 draws its own).
    /// </summary>
    async Task ViewerEnter()
    {
        if (_viewerCancelled) return;
        if (ViewerSource?.Lap is not { } lap)
        {
            ViewerError = "Replay viewer started without a lap";
            GD.PushError(ViewerError);
            return;
        }
        await LoadingFrame();      // (the steps below are each tens to hundreds of ms: a frame may go by before each)
        if (_viewerCancelled) return;
        // (managed code only, see LapPlayback: parsed on a worker thread while the frames go on; the continuation is back on the main thread)
        string signature = _records.Signature; var carParams = _params; var ground = _track;
        var prep = await Task.Run(() => LapPlayback.Prepare(lap, carParams, ground, signature));
        if (_viewerCancelled) return;
        LoadProf.Mark("ViewerEnter: Prepare (parse ghost+tape)");
        if (!prep.Ok)
        {
            ViewerError = prep.Message;
            GD.PushError("Replay viewer: " + prep.Message);
            return;
        }
        if (lap.HasTape && !prep.HasTape) GD.Print($"Replay viewer: playing without the world tape ({prep.TapeError}): {prep.Message}");
        int set = lap.Meta.Set;
        _viewerFuels = ParseLapFuels(lap.Meta.Fuels);
        var ghost = prep.Ghost;
        _ghosts[set] = ghost;
        _tapes[set] = new TapeSlot { Ghost = ghost, Tape = prep.Tape?.Tape, Body = prep.Tape?.Body };
        ((CanvasLayer)_hud.GetParent()).Visible = false;
        _replayCamera.Reset();
        _state = State.Title;
        _run = RunMode.Title;
        _hud.Mode = HudMode.Watch;
        _watching = _replaySelected = true;
        _titleReplayStart = TapeStart(0f);
        _replayClock = _titleReplayStart;
        _timer.Stop();
        _ghostVis.Visible = false;
        SetSaturation(1f);
        await LoadingFrame();
        if (_viewerCancelled) return;
        // The actors whose models the tape shows are told first (what TapeSeek does anyway, idempotent): PlaceAtStart resets every actor, and a live-driven Hind
        // would spawn its three sorties here (about 150 ms) only for the tape's first Seek to remove and rebuild them.
        if (prep.HasTape && TapePlayer() != null) foreach (var a in _tapeDriven) a.TapeDrive(true);
        PlaceAtStart();
        LoadProf.Mark("ViewerEnter: PlaceAtStart");
        await LoadingFrame();
        if (_viewerCancelled) return;
        if (Replaying)
        {
            TitleGhost.Rewind();
            TitleGhost.Apply(_replayClock, _car, 0);
            LoadProf.Mark("ViewerEnter: ghost apply");
            await LoadingFrame();
            if (_viewerCancelled) return;
            // The aircraft the first Seek would build (about 70 ms each, no yield possible inside it) are built here, one per frame; Spawn takes them.
            if (prep.HasTape && _tapeSink != null && prep.Tape?.Body is { } body)
            {
                var kf = body.Keys[0];
                foreach (var k in body.Keys) if (k.Sample / WorldTapeConst.TapeRate <= _replayClock + 1e-4f) kf = k;
                foreach (var e in kf.LiveInstances)
                {
                    if (e.Kind == null || !e.Kind.StartsWith("hind/")) continue;
                    _tapeSink.Prebuild(e.Kind);
                    await LoadingFrame();
                    if (_viewerCancelled) { _tapeSink.DiscardStash(); return; }
                }
            }
            TapeReplayEnter();      // (the world tape's first Seek: pose and spawns, WorldTapePlayer.Seek; one block from here)
            _tapeSink?.DiscardStash();
            LoadProf.Mark("ViewerEnter: TapeReplayEnter");
            await LoadingFrame();
            _cam.Snap(_car.Pos);
        }
        ViewerReady = Replaying;
        if (!ViewerReady) GD.PushError("Replay viewer: the lap is too short to play");
    }

    /// <summary>
    /// The physics step of a viewer: false while paused, not yet built or ended; scales dt by the speed. A non-looping playback takes
    /// its last step exactly onto the end of the lap (the final pose and effects are applied once, never a wrap) and ends after it.
    /// </summary>
    bool ViewerStep(ref float dt)
    {
        if (!ViewerReady || ViewerPaused || ViewerEnded) return false;
        dt *= ViewerSpeed;
        float end = ViewerWindowEnd;      // (the lap's end, or the saved cut's Out)
        if (!ViewerLoop && _replayClock + dt >= end)
        {
            dt = Mathf.Max(0f, end - _replayClock);
            ViewerEnded = true;      // (this step still runs)
        }
        else if (ViewerLoop && ViewerWindowed && _replayClock + dt >= end)
        {
            ViewerSeek(ViewerWindowStart);      // a cut wraps through the same seek as any jump (however large the step or short the window): the next frame is the window's start
            dt = 0f;
            ViewerWraps++; ViewerLastWrapClock = _replayClock; ViewerLastWrapPose = _car.Pos; ViewerLastWrapCamera = _viewerCamera; ViewerLastWrapHash = _tapePlayer == null ? 0UL : TapePoseHash();      // (what a test reads: exactly the state the wrap produced, independent of how many steps a frame ran)
        }
        return true;
    }

    /// <summary>Jump to a lap clock: what a loop wrap does (rewind the ghost, reset the breakables) and then the tape poses the world at that clock.</summary>
    public void ViewerSeek(float clock)
    {
        if (!ViewerReady) return;
        _replayClock = Mathf.Clamp(clock, ViewerWindowStart, ViewerWindowEnd);      // (the saved cut's window while it is on; the whole lap otherwise)
        ViewerEnded = false;
        ViewerApplyCameraKeys(force: true);      // (the camera the keys give for this clock is in place before the frame that shows it)
        TitleGhost.Rewind();
        TitleGhost.Apply(_replayClock, _car, 0);
        _wood.Reset(); _scenery.Reset(); _cones?.Reset(); _carVis.Damage?.Reset();
        ClearCarFeedback();
        _fx.Clear();      // (the tyre marks, dust and mud of the old clock are not the new clock's; the tape's own effects come back in TapeReplayEnter)
        TapeReplayEnter();
        _replayCamera.Reset();
        if (!ViewerFreeCameraActive) _cam.Snap(_car.Pos);      // (while paused the frame has no dt to move the camera: a seek shows its place at once)
        _cam.Feel(_car.Speed * 3.6f, 0f, false, 0f, 10f); _cam.Acceleration(_car.Vel, 0f, 10f);      // (the speed's pull-back at the new place settles at once, not over the next seconds)
        _viewerCamDirty = true;
        if (ViewerFreeCameraActive) ViewerFreeCameraPose();
    }

    /// <summary>The viewer's frame: camera, effects, day and night and sound of the replay. No hud, input, pad, radio, cards, files or ducking. Everything that moves takes the viewer's own dt (0 while paused, scaled by the speed).</summary>
    void ViewerFrame(float dt)
    {
        if (!ViewerReady) return;
        float vdt = ViewerPaused ? 0f : dt * ViewerSpeed;
        FadeUnder.Car = _car.Pos;
        SeeThrough.Car = _car.Pos;
        _carVis.Sync(_car, _track);
        if (ViewerFreeCameraActive) ViewerFreeCameraPose();
        else if (!ViewerPaused)      // (a paused picture holds still: the camera's shake adds random jitter on every Follow, and a seek snaps it)
        {
            if (ViewerCamera == ViewerCameraMode.Auto)
            {
                ViewerLeaveGameplayCamera();
                PrepareReplayCamera();
                if (!ReplayCamera(vdt))
                {
                    CameraFeel(vdt);
                    _cam.Follow(_car.Pos, _car.Vel, vdt);
                }
            }
            else ViewerGameplayCameraFrame(vdt);
            _viewerCamDirty = false;
        }
        else if (_viewerCamDirty)      // (a seek or a camera change while paused: one camera step puts the chosen view in place, then it holds still again)
        {
            _viewerCamDirty = false;
            if (ViewerCamera == ViewerCameraMode.Auto) { ViewerLeaveGameplayCamera(); PrepareReplayCamera(); if (!ReplayCamera(1f / 60f)) { CameraFeel(1f / 60f); _cam.Follow(_car.Pos, _car.Vel, 1f / 60f); } }
            else ViewerGameplayCameraFrame(1f / 60f);
        }
        _fx.Tick(vdt, _cam.GlobalTransform.Basis);
        DayNightFrame(vdt);
        UpdateSound(vdt);
        if (ViewerPaused) ViewerFreezeNodes(true);      // last: nothing in the frame may start a sound again
    }

    // The viewer's one-shot sounds and particle nodes run on the engine's clock, not on dt: a pause stops them explicitly and a resume gives back what each had.
    readonly System.Collections.Generic.Dictionary<Node, float> _viewerFrozen = new();
    bool _viewerFrozenNow;

    // The actors keep their own _Process clocks (markers, walkers, rowboat, mallards): a pause turns their processing off and a resume gives each node its mode back.
    readonly System.Collections.Generic.Dictionary<Node, ProcessModeEnum> _viewerDisabled = new();

    /// <summary>The live scene's edge state of the left stick and the held direction: a release that happened while a viewer owned the input must not leave it pressed.</summary>
    internal void ResetPadEdgeState() { _padHeld = null; _padHeldFor = _padRepeat = 0; _stickX = _stickY = 0; }

    void ViewerFreezeNodes(bool freeze)
    {
        if (freeze)
        {
            foreach (var c in GetChildren())
                if (c.ProcessMode != ProcessModeEnum.Disabled && !_viewerDisabled.ContainsKey(c)) { _viewerDisabled[c] = c.ProcessMode; c.ProcessMode = ProcessModeEnum.Disabled; }
            foreach (var n in FindChildren("*", "", true, false))
            {
                if (_viewerFrozen.ContainsKey(n))
                {
                    if (n is AudioStreamPlayer fa) fa.StreamPaused = true; else if (n is AudioStreamPlayer3D fa3) fa3.StreamPaused = true;      // (UpdateSound re-pauses and un-pauses the hind and BTR players itself)
                    continue;
                }
                switch (n)
                {
                    case AudioStreamPlayer a when !a.StreamPaused: a.StreamPaused = true; _viewerFrozen[n] = 0; break;
                    case AudioStreamPlayer3D a3 when !a3.StreamPaused: a3.StreamPaused = true; _viewerFrozen[n] = 0; break;
                    case CpuParticles3D cp when cp.SpeedScale != 0f: _viewerFrozen[n] = (float)cp.SpeedScale; cp.SpeedScale = 0f; break;
                    case GpuParticles3D gp when gp.SpeedScale != 0f: _viewerFrozen[n] = (float)gp.SpeedScale; gp.SpeedScale = 0f; break;
                }
            }
            _viewerFrozenNow = true;
            return;
        }
        foreach (var (n, was) in _viewerFrozen)
        {
            if (!IsInstanceValid(n)) continue;
            switch (n)
            {
                case AudioStreamPlayer a: a.StreamPaused = false; break;
                case AudioStreamPlayer3D a3: a3.StreamPaused = false; break;
                case CpuParticles3D cp: cp.SpeedScale = was; break;
                case GpuParticles3D gp: gp.SpeedScale = was; break;
            }
        }
        _viewerFrozen.Clear();
        foreach (var (c, mode) in _viewerDisabled) if (IsInstanceValid(c)) c.ProcessMode = mode;
        _viewerDisabled.Clear();
        _viewerFrozenNow = false;
    }

    /// <summary>Pause or resume the whole viewer: the physics step, the frame's dt, and the engine-clocked sounds and particles.</summary>
    public void ViewerSetPaused(bool paused)
    {
        if (paused == ViewerPaused) return;
        ViewerPaused = paused;
        ViewerFreezeNodes(paused);
    }

    /// <summary>
    /// Asks a building viewer to give up. The build itself carries on to its end in slices (a half-built world has nodes only the builder holds, and dropping the builder would leave them as orphans);
    /// nothing is started after it, and the session owner frees the world once ViewerBuildDone is set.
    /// </summary>
    public void ViewerCancel() => _viewerCancelled = true;
    bool _viewerCancelled;
    /// <summary>Number of loading checkpoints passed (progress for the loading cover; the total is not known in advance).</summary>
    public int ViewerBuildSteps { get; private set; }
    /// <summary>True when the build coroutine has ended (finished, failed or run out after a cancel); only then may the world be freed.</summary>
    public bool ViewerBuildDone { get; private set; }

    void ViewerBuildCheckpoint()
    {
        ViewerBuildSteps++;
    }

    /// <summary>A viewer's exit: release its private tape player and effects only. The record-pair joins, the archive flush, the shared prop cache and the shared shader flags belong to the live scene.</summary>
    void ViewerExit()
    {
        if (_tapePlayer != null && _tapePosed) { _tapePosed = false; TapeParkCircles(false); _tapePlayer.Release(); }
        _tapeFx?.Free();
        _tapeSink?.DiscardStash();
    }

    /// <summary>The track id the viewer session snapshots and restores (a nested build of another track overwrites it).</summary>
    internal string TrackIdForRestore { get => _trackId; set => _trackId = value; }
}
