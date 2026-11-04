using Godot;
using System;
using CCCPRacing.Replay;

namespace CCCPRacing.Race;

public partial class RaceScene
{
    public sealed partial class ReplayViewerSession
    {
        bool _freeMouse, _motionOpen;
        int _motionRow;
        LapCameraMotion _freeMotion;
        float _motionFov;
        readonly string[] _trackingModes = { LapCameraMotion.Fixed, LapCameraMotion.World, LapCameraMotion.Side, LapCameraMotion.Helicopter };
        internal LapCameraMotion CameraMotionDraft => _freeMotion;
        public bool CameraMotionOpen => _motionOpen;
        public string[] CameraMotionRows => MotionRows();
        string[] MotionRows() => new[] {
            "TRACKING  " + (_freeMotion?.Tracking ?? (_scene.ViewerFreeRig.Follow ? "World" : "Fixed")).ToUpperInvariant(),
            "AIM  " + (_scene.ViewerFreeRig.Look ? "AT CAR" : "MANUAL"),
            $"DURATION  {_freeMotion?.Duration ?? 6:0.0} S",
            $"CRANE RISE  {_freeMotion?.Rise ?? 0:0} M",
            $"DOLLY TOWARD CAR  {_freeMotion?.Dolly ?? 0:0} M",
            $"START LENS  {_motionFov:0} DEG",
            "END LENS  " + (_freeMotion?.EndFov is float f ? $"{f:0} DEG" : "OFF"),
            "CLEAR MOTION", "DONE - K ADDS SHOT" };
        void ChangeMotion(int direction)
        {
            var m = _freeMotion ?? new LapCameraMotion { Tracking = _scene.ViewerFreeRig.Follow ? LapCameraMotion.World : LapCameraMotion.Fixed };
            switch (_motionRow)
            {
                case 0: m = m with { Tracking = _trackingModes[(Array.IndexOf(_trackingModes, m.Tracking) + direction + 4) % 4] }; break;
                case 1: _scene.ViewerFreeRig.SetLook(!_scene.ViewerFreeRig.Look, _scene._car.Pos); return;
                case 2: m = m with { Duration = Mathf.Clamp(m.Duration + direction * .5f, .5f, 120) }; break;
                case 3: m = m with { Rise = Mathf.Clamp(m.Rise + direction * 2, -200, 200) }; break;
                case 4: m = m with { Dolly = Mathf.Clamp(m.Dolly + direction * 2, -200, 200) }; break;
                case 5: _motionFov = Mathf.Clamp(_motionFov + direction * 5, 10, 120); _scene._cam.Fov = _motionFov; return;
                case 6:
                    m = m with { EndFov = m.EndFov == null ? Mathf.Clamp(_motionFov + direction * 5, 10, 120)
                        : m.EndFov + direction * 5 is float f && f >= 10 && f <= 120 ? f : null }; break;
                case 7: _freeMotion = null; return;
                case 8: CloseMotion(); return;
            }
            _freeMotion = m;
        }
        bool MotionInput(InputEvent e)
        {
            if (!_motionOpen) return false;
            if (e is InputEventJoypadButton { Pressed: true } pb)
            {
                _view.PadMode = true;
                switch (pb.ButtonIndex)
                {
                    case JoyButton.DpadUp: _motionRow = (_motionRow + 8) % 9; break;
                    case JoyButton.DpadDown: _motionRow = (_motionRow + 1) % 9; break;
                    case JoyButton.DpadLeft: ChangeMotion(-1); break;
                    case JoyButton.DpadRight or JoyButton.A: ChangeMotion(1); break;
                    case JoyButton.B or JoyButton.Start or JoyButton.Back: CloseMotion(); break;
                }
            }
            else if (e is InputEventKey { Pressed: true, Echo: false } k)
                switch (k.PhysicalKeycode)
                {
                    case Key.Up: _motionRow = (_motionRow + 8) % 9; break;
                    case Key.Down: _motionRow = (_motionRow + 1) % 9; break;
                    case Key.Left: ChangeMotion(-1); break;
                    case Key.Right or Key.Enter or Key.KpEnter: ChangeMotion(1); break;
                    case Key.B or Key.Escape or Key.Backspace: CloseMotion(); break;
                }
            return true;
        }

        void CloseMotion()
        {
            _motionOpen = false; _holdAwaitRelease = true;
            if (IsInstanceValid(_freeWindow) && _freeWindow.HasFocus()) CaptureFreeMouse();
        }
        void CaptureFreeMouse()
        {
            if (_freeMouse || !FreeCameraActive || _motionOpen) return;
            _freePriorMouse = Input.MouseMode; _freeMouse = true;
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
        Input.MouseModeEnum _freePriorMouse;
        Window _freeWindow;
        public bool FreeCameraActive => UiReady && _scene.ViewerFreeCameraControlled;

        void OpenFreeCamera()
        {
            if (!UiReady || SaveBusy || KeepBusy) return;
            _editOpen = false; _prompt = EditPrompt.None;
            _freeMotion = _scene._viewerSavedFree?.Motion;
            _scene.ViewerBeginFreeCamera();
            _motionFov = _scene.ViewerCameraFov; _motionOpen = false;
            _freeWindow = _live.GetWindow();
            _freeWindow.FocusExited += ReleaseFreeMouse;
            _freeWindow.FocusEntered += CaptureFreeMouse;
            if (_freeWindow.HasFocus()) CaptureFreeMouse();
            _holdAwaitRelease = true;
            SetNotice("K ADD CAMERA KEY - TAB STEP KEYS - DEL REMOVE KEY", 4f);
        }
        void ReleaseFreeMouse()
        {
            if (!_freeMouse) return;
            _freeMouse = false; Input.MouseMode = _freePriorMouse;
        }
        void EndFreeCamera()
        {
            ReleaseFreeMouse(); _motionOpen = false;
            if (IsInstanceValid(_freeWindow))
            { _freeWindow.FocusExited -= ReleaseFreeMouse; _freeWindow.FocusEntered -= CaptureFreeMouse; }
            _freeWindow = null;
            if (IsInstanceValid(_scene)) _scene.ViewerEndFreeCamera();
            _holdAwaitRelease = true;
        }
        bool FreeCameraInput(InputEvent e)
        {
            if (!FreeCameraActive) return false;
            if (e is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.F12 }) { SaveViewerScreenshot(); return true; }
            if (MotionInput(e)) return true;
            if (e is InputEventMouseMotion motion && _freeMouse) _scene.ViewerFreeRig.Turn(motion.Relative);
            else if (e is InputEventKey { Pressed: true, Echo: false } k)
            {
                _view.PadMode = false;
                switch (k.PhysicalKeycode)
                {
                    case Key.Escape or Key.R: EndFreeCamera(); SetNotice("TOP-DOWN CAMERA"); break;
                    case Key.B: ReleaseFreeMouse(); _motionRow = 0; _motionOpen = true; break;
                    case Key.K: AddCameraKeyHere(); break;
                    case Key.Tab: StepCameraKey(k.ShiftPressed ? -1 : 1); break;
                    case Key.Delete: RemoveCameraKeyHere(); break;
                    case Key.F: FreeToggleFollow(); break;
                    case Key.G: _scene.ViewerFreeRig.SetLook(!_scene.ViewerFreeRig.Look, _scene._car.Pos); break;
                    case Key.Space: UiTogglePlay(); break;
                    case Key.Up: UiSpeed(+1); break;
                    case Key.Down: UiSpeed(-1); break;
                    case Key.H: UiToggleOverlay(); break;
                }
            }
            else if (e is InputEventJoypadButton { Pressed: true } b)
            {
                _view.PadMode = true;
                switch (b.ButtonIndex)
                {
                    case JoyButton.B or JoyButton.Back: EndFreeCamera(); SetNotice("TOP-DOWN CAMERA"); break;
                    case JoyButton.A: UiTogglePlay(); break;
                    case JoyButton.X: AddCameraKeyHere(); break;
                    case JoyButton.Y: FreeToggleFollow(); break;
                    case JoyButton.RightStick: _scene.ViewerFreeRig.SetLook(!_scene.ViewerFreeRig.Look, _scene._car.Pos); break;
                    case JoyButton.Start: ReleaseFreeMouse(); _motionRow = 0; _motionOpen = true; break;
                    case JoyButton.DpadUp: UiSpeed(+1); break;
                    case JoyButton.DpadDown: UiSpeed(-1); break;
                }
            }
            return true; // Camera events own E/S/K and mouse; UiHold separately handles seeking and stepping.
        }
        void FreeToggleFollow()
        {
            _scene.ViewerFreeRig.SetFollow(!_scene.ViewerFreeRig.Follow, _scene._car.Pos);
            if (_freeMotion != null) _freeMotion = _freeMotion with { Tracking = _scene.ViewerFreeRig.Follow ? LapCameraMotion.World : LapCameraMotion.Fixed };
        }
        void FreeCameraFrame(float dt)
        {
            if (!FreeCameraActive || _motionOpen) return;
            float Axis(Key plus, Key minus) => (Input.IsPhysicalKeyPressed(plus) ? 1 : 0) - (Input.IsPhysicalKeyPressed(minus) ? 1 : 0);
            // The pad: left stick flies, triggers lower and raise, right stick looks (not while the camera aims at the car), L3 is fast.
            int dev = _live._driver.Device;
            float Stick(JoyAxis a) { float v = Input.GetJoyAxis(dev, a); return Mathf.Abs(v) < .2f ? 0 : v; }
            var axes = new Vector3(Axis(Key.D, Key.A) + Stick(JoyAxis.LeftX), Axis(Key.E, Key.Q) + Stick(JoyAxis.TriggerRight) - Stick(JoyAxis.TriggerLeft), Axis(Key.W, Key.S) - Stick(JoyAxis.LeftY));
            var look = new Vector2(Stick(JoyAxis.RightX), Stick(JoyAxis.RightY));
            if (look != Vector2.Zero) _scene.ViewerFreeRig.Turn(look * (600f * Mathf.Min(dt, .1f)));
            float speed = Input.IsPhysicalKeyPressed(Key.Ctrl) ? 2.4f : Input.IsPhysicalKeyPressed(Key.Shift) || Input.IsJoyButtonPressed(dev, JoyButton.LeftStick) ? 48f : 12f;
            _scene.ViewerFreeRig.Move(axes, dt, speed);
        }
    }
}
