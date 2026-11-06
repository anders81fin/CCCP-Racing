using System;
using System.Collections.Generic;
using CCCPRacing.Replay;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// The viewer's controls and overlay (docs/work-orders/player-replay.md phase 3). The live scene is frozen and its hud hidden while a viewer is open, so the
/// session owns both: it reads the pad and keys itself (the live scene's pad-to-key mapping does not run) and feeds a <see cref="ReplayOverlay"/> of its own, a
/// canvas layer beside the viewer's picture so it scales with the pixel frame. Keys: SPACE/ENTER play-pause, LEFT/RIGHT seek (hold repeats), UP/DOWN speed,
/// COMMA/PERIOD step, C camera, H overlay, ESC/BACKSPACE back (cancel while loading). Pad: A, D-pad, LB/RB, Y, X, B. Nothing is saved: speed and camera are
/// this viewing's only (saved playback preferences belong to phase 4).
/// </summary>
public partial class RaceScene
{
    public sealed partial class ReplayViewerSession
    {
        static readonly float[] Speeds = { 0.25f, 0.5f, 1f, 2f };
        const float SeekStep = 5f, FrameStep = 0.1f, HoldDelay = 0.35f, HoldRepeat = 0.12f;
        // Steps of a typical build (measured: ilyich 206, siberia 82, atomgrad 60, bonus 73): the progress bar is an estimate and never reaches 1 before the build is done.
        static readonly Dictionary<string, int> ExpectedSteps = new() { ["ilyich"] = 206, ["siberia"] = 82, ["atomgrad"] = 60, ["bonus"] = 73 };

        CanvasLayer _uiLayer;
        ReplayOverlay _overlay;
        readonly ReplayOverlayView _view = new();
        bool _overlayHidden;
        string _notice = ""; float _noticeLeft;
        float _holdFor, _holdNext; int _holdKind;      // what is held (see HeldKind), for how long, when it repeats
        bool _holdAwaitRelease;
        /// <summary>True while the loading still (the live game's frozen picture) is held.</summary>
        public bool HasStill => _still != null;
        public string EndNotice { get; private set; } = "";      // why the session ended when the player did not close it themself (shown in the Party Meeting's list)
        public bool EndedByError { get; private set; }

        /// <summary>The overlay's current view (tests read it).</summary>
        public ReplayOverlayView OverlayView => _view;

        void BuildUi()
        {
            _uiLayer = new CanvasLayer { Layer = 3 };
            _overlay = new ReplayOverlay { Name = "ReplayOverlay" };
            _uiLayer.AddChild(_overlay);
            _live.GetParent().AddChild(_uiLayer);
            _overlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            _view.PadMode = _live._padMode;
            _overlay.Backdrop = _still;
            RefreshView();
        }

        void FreeUi()
        {
            _export?.Dispose();      // a running export is cancelled with its viewer
            if (_overlay != null) _overlay.Backdrop = null;
            _still = null;
            if (IsInstanceValid(_uiLayer)) _uiLayer.QueueFree();
            _uiLayer = null; _overlay = null;
        }

        /// <summary>The cameras this personnel file may use (for the exporter's camera keys): the same list the C key cycles.</summary>
        public List<ViewerCameraMode> AvailableCameraModes() => AvailableCameras();

        /// <summary>The exporter child: no overlay, no hints, no notices (the picture is the world only).</summary>
        public void HideForExport() { if (_uiLayer != null) _uiLayer.Visible = false; _overlayHidden = true; _exportChild = true; }

        /// <summary>The cameras the player may pick: the gameplay top-down, the bonnet and the cinematic one only where this personnel file has found them, and the automatic director.</summary>
        List<ViewerCameraMode> AvailableCameras()
        {
            var list = new List<ViewerCameraMode> { ViewerCameraMode.TopDown };
            var found = _live._cameraDiscoveries;
            if (found != null)
            {
                if (found.Has(_track, GameplayView.Bonnet)) list.Add(ViewerCameraMode.Bonnet);
                if (found.Has(_track, GameplayView.Cinematic)) list.Add(ViewerCameraMode.Cinematic);
            }
            list.Add(ViewerCameraMode.Auto);
            return list;
        }

        static string CameraName(ViewerCameraMode m) => m switch { ViewerCameraMode.Bonnet => "BONNET", ViewerCameraMode.Cinematic => "CINEMATIC", ViewerCameraMode.Auto => "AUTO", _ => "TOP-DOWN" };

        public void SetNotice(string text, float seconds = 3f) { _notice = text ?? ""; _noticeLeft = seconds; }

        // ------------------------------------------------------------------ controls

        /// <summary>A key or pad press (an event: the held keys are polled in <see cref="UiFrame"/>).</summary>
        void UiInput(InputEvent e)
        {
            if (_state == ReplayViewerState.Closing || _state == ReplayViewerState.Closed) return;
            if (FreeCameraInput(e)) return;
            if (ViewerKeepInput(e)) return;
            if (ExportInput(e)) return;      // the MP4 export card / progress (src/Export/ExportUiController.cs)
            if (DirectFreeCameraInput(e)) return;      // RaceScene.ReplayViewerFreeCamera.cs: W/A/S/D or the mouse fly off in the free camera (before the edit keys: CTRL+S saves)
            if (EditInput(e)) return;      // RaceScene.ReplayViewerEdits.cs: the EDIT panel, its direct keys and the modal questions
            if (e is InputEventKey { Pressed: true, Echo: false } k)
            {
                _view.PadMode = false;
                switch (k.PhysicalKeycode)
                {
                    case Key.Escape: case Key.Backspace: UiBack(); break;
                    case Key.Space: case Key.Enter: case Key.KpEnter: UiTogglePlay(); break;
                    case Key.Up: UiSpeed(+1); break;
                    case Key.Down: UiSpeed(-1); break;
                    case Key.C: UiCamera(); break;
                    case Key.H: UiToggleOverlay(); break;
                }
            }
            else if (e is InputEventJoypadButton { Pressed: true } b)
            {
                _view.PadMode = true;
                switch (b.ButtonIndex)
                {
                    case JoyButton.A: UiTogglePlay(); break;
                    case JoyButton.B: UiBack(); break;
                    case JoyButton.Y: UiCamera(); break;
                    case JoyButton.X: UiToggleOverlay(); break;
                    case JoyButton.DpadUp: UiSpeed(+1); break;
                    case JoyButton.DpadDown: UiSpeed(-1); break;
                }
            }
        }

        void UiBack()
        {
            if (_state == ReplayViewerState.Building) Close("REPLAY CANCELLED");
            else if (EditBack()) { }      // (a save in flight, the open panel or unsaved edits answer first)
            else Close();
        }

        bool UiReady => _state == ReplayViewerState.Playing && _scene != null && IsInstanceValid(_scene) && _scene.ViewerReady;

        void UiTogglePlay()
        {
            if (!UiReady) return;
            if (_scene.ViewerEnded) { _scene.ViewerSeek(_scene.ViewerWindowStart); _scene.ViewerSetPaused(false); }
            else _scene.ViewerSetPaused(!_scene.ViewerPaused);
        }

        void UiSpeed(int dir)
        {
            if (!UiReady || PrefsBlocked()) return;
            int i = Array.IndexOf(Speeds, _scene.ViewerSpeed); if (i < 0) i = 2;
            _scene.ViewerSpeed = Speeds[Math.Clamp(i + dir, 0, Speeds.Length - 1)];
            PrefChanged();
        }

        void UiCamera()
        {
            if (!UiReady || PrefsBlocked()) return;
            var all = AvailableCameras();
            int i = all.IndexOf(_scene.ViewerCamera);
            _scene.ViewerCamera = all[(i + 1) % all.Count];
            if (_scene.ViewerEdits.Keys.Count == 0 || _scene.ViewerClock < _scene.ViewerEdits.Keys[0].T) { _wCamera = _scene.ViewerCamera; _scene.ViewerEdits.DefaultCamera = _wCamera; }      // (before the first key the chosen camera IS the lap's camera: a wrap or seek back there returns to it)
            SetNotice(CameraName(_scene.ViewerCamera) + " CAMERA", 1.5f);
            PrefChanged();
        }

        void UiToggleOverlay() { if (PrefsBlocked()) return; _overlayHidden = !_overlayHidden; PrefChanged(); }

        // held direction keys and buttons: first press at once, then a repeat after HoldDelay (rate-limited: a seek costs tens of ms)
        enum HeldKind { None, SeekBack, SeekForward, StepBack, StepForward }

        HeldKind Held()
        {
            int dev = _live._driver.Device;
            if (FreeCameraActive)
            {
                // The left stick flies the camera there, so only the D-pad seeks; the step keys and shoulders are as in the plain viewer.
                bool fback = Input.IsPhysicalKeyPressed(Key.Left) || Input.IsJoyButtonPressed(dev, JoyButton.DpadLeft);
                bool fforward = Input.IsPhysicalKeyPressed(Key.Right) || Input.IsJoyButtonPressed(dev, JoyButton.DpadRight);
                bool fsb = Input.IsPhysicalKeyPressed(Key.Comma) || Input.IsJoyButtonPressed(dev, JoyButton.LeftShoulder);
                bool fsf = Input.IsPhysicalKeyPressed(Key.Period) || Input.IsJoyButtonPressed(dev, JoyButton.RightShoulder);
                if (fback != fforward) return fback ? HeldKind.SeekBack : HeldKind.SeekForward;
                if (fsb != fsf) return fsb ? HeldKind.StepBack : HeldKind.StepForward;
                return HeldKind.None;
            }
            bool left = Input.IsKeyPressed(Key.Left) || Input.IsJoyButtonPressed(dev, JoyButton.DpadLeft) || Input.GetJoyAxis(dev, JoyAxis.LeftX) < -0.6f;
            bool right = Input.IsKeyPressed(Key.Right) || Input.IsJoyButtonPressed(dev, JoyButton.DpadRight) || Input.GetJoyAxis(dev, JoyAxis.LeftX) > 0.6f;
            bool sb = Input.IsKeyPressed(Key.Comma) || Input.IsJoyButtonPressed(dev, JoyButton.LeftShoulder);
            bool sf = Input.IsKeyPressed(Key.Period) || Input.IsJoyButtonPressed(dev, JoyButton.RightShoulder);
            if (left != right) return left ? HeldKind.SeekBack : HeldKind.SeekForward;
            if (sb != sf) return sb ? HeldKind.StepBack : HeldKind.StepForward;
            return HeldKind.None;
        }

        void UiHold(float dt)
        {
            if (_editOpen || _prompt != EditPrompt.None || ExportOpen || _viewerKeepOpen || KeepBusy || _motionOpen)
            {
                _holdKind = (int)HeldKind.None; _holdFor = _holdNext = 0;
                _holdAwaitRelease = true;
                return; // modal controls own both input events and global held-key/button polling
            }
            var h = UiReady ? Held() : HeldKind.None;
            if (_holdAwaitRelease) { if (h == HeldKind.None) _holdAwaitRelease = false; return; }
            if ((int)h != _holdKind) { _holdKind = (int)h; _holdFor = 0; _holdNext = 0; if (h == HeldKind.None) return; }
            else if (h == HeldKind.None) return;
            else { _holdFor += dt; if (_holdFor < HoldDelay) return; _holdNext -= dt; if (_holdNext > 0) return; }
            _holdNext = HoldRepeat;
            float step = h is HeldKind.SeekBack or HeldKind.SeekForward ? SeekStep : FrameStep;
            float sign = h is HeldKind.SeekBack or HeldKind.StepBack ? -1f : 1f;
            if (h is HeldKind.StepBack or HeldKind.StepForward) _scene.ViewerSetPaused(true);      // (stepping is a paused thing: the picture then stays where the step put it)
            _scene.ViewerSeek(_scene.ViewerClock + sign * step);
        }

        // ------------------------------------------------------------------ overlay

        void UiFrame(float dt)
        {
            if (_overlay == null) return;
            EditsFrame();
            DirectFreeFrame();
            FreeCameraFrame(dt);
            if (UiReady && _scene.ViewerEnded && !_scene.ViewerPaused) _scene.ViewerSetPaused(true);      // a lap plays once and rests at its end
            UiHold(dt);
            if (_noticeLeft > 0) { _noticeLeft -= dt; if (_noticeLeft <= 0) _notice = ""; }
            RefreshView();
        }

        void RefreshView()
        {
            var v = _view;
            v.Shown = !_overlayHidden || _motionOpen;
            v.FreeCamera = FreeCameraActive;
            v.FreeFollow = FreeCameraActive && _scene.ViewerFreeRig.Follow;
            v.FreeLook = FreeCameraActive && _scene.ViewerFreeRig.Look;
            if (FreeCameraActive)
            {
                var pose = _scene.ViewerCameraPose;
                var fwd = -pose.Basis.Z;
                v.FreePosition = pose.Origin;
                v.FreeHeading = (Mathf.RadToDeg(Mathf.Atan2(fwd.X, -fwd.Z)) % 360f + 360f) % 360f;
                v.FreePitch = Mathf.RadToDeg(Mathf.Asin(Mathf.Clamp(fwd.Y, -1f, 1f)));
            }
            v.Building = _state == ReplayViewerState.Building;
            if (v.Building)
            {
                int steps = _scene != null && IsInstanceValid(_scene) ? _scene.ViewerBuildSteps : 0;
                int total = ExpectedSteps.TryGetValue(_track, out int t) ? t : 100;
                v.BuildProgress = Math.Min(0.97f, steps / (float)total);
            }
            else v.BuildProgress = 1f;
            v.Title = _title;
            if (_scene != null && IsInstanceValid(_scene) && _scene.ViewerReady)
            {
                v.Clock = _scene.ViewerClock; v.Duration = _scene.ViewerDuration;
                v.Paused = _scene.ViewerPaused; v.Ended = _scene.ViewerEnded; v.Speed = _scene.ViewerSpeed;
                v.Camera = _scene.ViewerFreeCameraActive ? "FREE" : CameraName(_scene.ViewerCamera);
            }
            v.Notice = _notice;
            FillEditView(v);
            if (_overlay != null) _overlay.View = v;
        }

        string _title = "";
        /// <summary>The line the overlay shows for the lap ("ILYICH RALLY 1:23.45 RECORD").</summary>
        public void SetTitle(string title) { _title = title ?? ""; }
    }
}
