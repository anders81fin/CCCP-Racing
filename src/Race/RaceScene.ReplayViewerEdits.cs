using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CCCPRacing.Export;
using CCCPRacing.Replay;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// The viewer's saved editorial choices (docs/player-replay-phase4b-design.md, amendments in section 9): the EDIT panel, the cut window, camera keys, pin, loop and the playback
/// preferences of a KEPT lap, saved beside the pair by the archive's worker (LoadEdits / SaveEdits / ResetEdits). The pair and meta are never written. Dirty edits stay until a
/// successful save or an explicit discard; the viewer does not close while a save is unresolved; an unkept lap can be kept directly in the viewer.
/// </summary>
public partial class RaceScene
{
    public sealed partial class ReplayViewerSession
    {
        LapArchive _archive;
        string _lapId;
        bool _editable;
        Task<LapEditsLoad> _loadTask;
        Task<EditsSaveResult> _saveTask;
        bool _resetting, _closeAfterSave, _editsApplied;
        LapEdits _baseline;                      // what is on disk (null: nothing valid)
        // the working copy
        bool _wPinned, _wTrim = true, _wLoop, _wOverlay = true;
        float? _wIn, _wOut;
        readonly List<(float T, ViewerCameraMode Mode)> _wKeys = new();
        readonly Dictionary<float, LapFreeCamera> _wFreeKeys = new();
        float _wSpeed = 1f; ViewerCameraMode _wCamera = ViewerCameraMode.TopDown;
        bool _editOpen; int _editSel;
        enum EditPrompt { None, KeepFirst, Unsaved, Reset }
        EditPrompt _prompt;

        /// <summary>Test seams: delay the edits' load / save (seconds) to prove no stray first frame, no edit lost to a late load and the pending-save rules on a slow disk.</summary>
        internal static float LoadDelayForTest, SaveDelayForTest;
        bool _gateDone, _gatePaused; ulong _gateUntil;

        static async Task<T> Delayed<T>(float seconds, Task<T> t) { if (seconds > 0) await Task.Delay((int)(seconds * 1000)); return await t; }

        /// <summary>
        /// The Building state's last step: the viewer world is ready but stays held (paused, loading panel still up) until the saved edits have arrived; then the plan, the preferences and the
        /// cut's start are put in place and the first visible playing frame is already the edited one. No control acts before it (every key but cancel needs Playing).
        /// </summary>
        bool EditsGate()
        {
            if (_gateDone) return true;
            if (_loadTask == null) { _gateDone = true; return true; }      // (not a kept lap, or nothing to wait for)
            if (!_scene.ViewerPaused) { _scene.ViewerSetPaused(true); _gatePaused = true; }
            if (LoadDelayForTest > 0)
            {
                if (_gateUntil == 0) _gateUntil = Time.GetTicksMsec() + (ulong)(LoadDelayForTest * 1000);      // (the seam holds the gate this long once the world is ready, whatever the build took)
                if (Time.GetTicksMsec() < _gateUntil) return false;
            }
            if (_loadTask != null)
            {
                if (!_loadTask.IsCompleted) return false;
                var t = _loadTask; _loadTask = null;
                ApplyLoaded(t.IsFaulted ? new LapEditsLoad(LapEditsLoadKind.Invalid, null, t.Exception?.GetBaseException().Message ?? "unknown error") : t.Result);
            }
            _gateDone = true;
            if (_gatePaused) _scene.ViewerSetPaused(false);
            return true;
        }

        /// <summary>The list's context for this lap; only a KEPT lap is editable.</summary>
        public void SetEditContext(LapArchive archive, LapEntryInfo info)
        {
            _archive = archive; _lapId = info?.Id; _keepInfo = info; _editable = info != null && info.Retained && archive != null;
            if (_editable) _loadTask = archive.LoadEdits(_lapId);
        }

        public bool Editable => _editable;
        public bool Dirty { get; private set; }
        public bool SaveBusy => _saveTask != null;
        public bool EditOpen => _editOpen;
        public string[] EditRowTexts => EditRows();

        static string Mode(ViewerCameraMode m) => m switch { ViewerCameraMode.Bonnet => LapEdits.Bonnet, ViewerCameraMode.Cinematic => LapEdits.Cinematic, ViewerCameraMode.Auto => LapEdits.Auto, _ => LapEdits.TopDown };
        static ViewerCameraMode ModeOf(string s) => s switch { LapEdits.Bonnet => ViewerCameraMode.Bonnet, LapEdits.Cinematic => ViewerCameraMode.Cinematic, LapEdits.Auto => ViewerCameraMode.Auto, _ => ViewerCameraMode.TopDown };

        // ---------------------------------------------------------------- load

        /// <summary>Per frame: the loaded edits arrive (once the viewer is playing), and a save's outcome.</summary>
        void EditsFrame()
        {
            _export?.Tick();
            ViewerKeepFrame();
            if (_saveTask is { IsCompleted: true } st) { _saveTask = null; SaveDone(st.IsFaulted ? new EditsSaveResult(false, st.Exception?.GetBaseException().Message ?? "unknown error", _lapId, null) : st.Result); }
        }

        void ApplyLoaded(LapEditsLoad r)
        {
            switch (r.Kind)
            {
                case LapEditsLoadKind.Ok:
                    _baseline = r.Edits; LoadWorking(r.Edits); break;
                case LapEditsLoadKind.Missing: break;
                case LapEditsLoadKind.Refused: _editable = false; break;
                case LapEditsLoadKind.NewerVersion: SetNotice("SAVED EDITS ARE FROM A NEWER GAME - IGNORED", 6f); break;
                case LapEditsLoadKind.Mismatch: SetNotice("SAVED EDITS DO NOT MATCH THIS LAP - IGNORED", 6f); break;
                default: SetNotice("SAVED EDITS ARE UNREADABLE - IGNORED", 6f); break;
            }
            PushPlan(seekToStart: true);
        }

        void LoadWorking(LapEdits e)
        {
            _wPinned = e.Pinned; _wTrim = e.TrimEnabled; _wIn = e.In; _wOut = e.Out;
            _wKeys.Clear(); _wFreeKeys.Clear();
            var avail = AvailableCameras(); bool lost = false;
            foreach (var k in e.CameraKeys)
            {
                var m = ModeOf(k.Mode);
                if (k.Mode == LapEdits.Free) { m = ViewerCameraMode.TopDown; _wFreeKeys[k.T] = k.Free; }
                else if (!avail.Contains(m)) { m = ViewerCameraMode.TopDown; lost = true; }      // (a camera this file has not found: top-down, said out loud, never a silent unlock)
                _wKeys.Add((k.T, m));
            }
            _wLoop = e.Playback.Loop; _wOverlay = e.Playback.Overlay; _wSpeed = Speeds.Contains(e.Playback.Speed) ? e.Playback.Speed : 1f;
            var cam = ModeOf(e.Playback.Camera);
            if (!avail.Contains(cam)) { cam = ViewerCameraMode.TopDown; lost = true; }
            _wCamera = cam;
            if (lost) SetNotice("A SAVED CAMERA IS NOT FOUND YET - TOP-DOWN USED", 6f);
        }

        /// <summary>The working copy to the scene: the plan, the preferences, the window's start.</summary>
        void PushPlan(bool seekToStart)
        {
            if (_scene == null || !IsInstanceValid(_scene) || !_scene.ViewerReady) return;
            var plan = new ViewerEditPlan { TrimEnabled = _wTrim, In = _wIn, Out = _wOut, DefaultCamera = _wCamera };
            foreach (var k in _wKeys.OrderBy(k => k.T)) plan.Keys.Add(k);
            foreach (var k in _wFreeKeys) plan.FreeKeys[k.Key] = k.Value;
            _scene.ViewerEdits = plan;
            if (plan.Keys.Count == 0 && _scene._viewerSavedFree != null && !_scene.ViewerFreeCameraControlled) _scene.ViewerEndFreeCamera();
            if (!_editsApplied)
            {
                _editsApplied = true;
                _scene.ViewerSpeed = _wSpeed; _scene.ViewerLoop = _wLoop; _overlayHidden = !_wOverlay;
                _scene.ViewerCamera = _wCamera;
            }
            if (seekToStart && (_scene.ViewerWindowed || plan.Keys.Count > 0)) _scene.ViewerSeek(_scene.ViewerWindowStart);
        }

        // ---------------------------------------------------------------- the panel

        // row ids: 0 IN, 1 OUT, 2 CLEAR TRIM, 3 TRIM, 4 ADD KEY, 5 CLEAR KEYS, 6 LOOP, 7 PIN, 8 SAVE, 9 RESET, 10 CLOSE, 11 EXPORT MP4, 12 KEEP/RENAME
        // A kept lap lists all of them; a lap that is not kept lists EXPORT (always available), KEEP, and CLOSE.
        int[] EditRowIds() => _editable ? new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 11, 12, 13, 10 } : new[] { 11, 12, 13, 10 };

        string[] EditRows()
        {
            string t(float? v) => v == null ? "OPEN" : ReplayOverlay.FormatTime(v.Value);
            string Text(int id) => id switch
            {
                0 => "SET IN HERE  " + t(_wIn), 1 => "SET OUT HERE  " + t(_wOut), 2 => "CLEAR TRIM", 3 => "TRIM " + (_wTrim ? "ON" : "OFF"),
                4 => $"ADD CAMERA KEY  ({_wKeys.Count})", 5 => "CLEAR CAMERA KEYS", 6 => "LOOP " + (_wLoop ? "ON" : "OFF"), 7 => "PIN " + (_wPinned ? "ON" : "OFF"),
                8 => "SAVE", 9 => "RESET EDITS", 11 => "EXPORT MP4...", 13 => "FREE CAMERA", 12 => _editable ? "RENAME RECORDING..." : "KEEP RECORDING...", _ => "CLOSE",
            };
            return EditRowIds().Select(Text).ToArray();
        }

        // ---------------------------------------------------------------- MP4 export (the shared controller: src/Export/ExportUiController.cs)

        ExportUiController _export;
        public bool ExportOpen => _export?.IsOpen == true;
        public ExportJob LastExportJob => _export?.LastJob;

        ExportContext BuildExportContext()
        {
            float dur = _scene.ViewerDuration;
            (float, float)? cut = null;
            if (_wTrim && (_wIn != null || _wOut != null)) { float a = _wIn ?? 0f, b = _wOut ?? dur; if (a >= 0 && b > a && b <= dur + 1e-3f) cut = (a, MathF.Min(b, dur)); }
            string cam = Mode(_wKeys.Count == 0 ? _scene.ViewerCamera : _wCamera);
            return new ExportContext
            {
                Source = new SpExportSource(_lap, _title, dur), Cut = cut, DefaultCamera = cam, Name = string.IsNullOrWhiteSpace(_title) ? "replay" : _title,
                CameraKeys = _wKeys.OrderBy(k => k.T).Select(k => new ExportCameraKey(k.T, _wFreeKeys.ContainsKey(k.T) ? LapEdits.Free : Mode(k.Mode), _wFreeKeys.GetValueOrDefault(k.T))).ToList(),
            };
        }

        void OpenExport()
        {
            if (!UiReady) return;
            _export ??= new ExportUiController(BuildExportContext, _live.ExportEnvironmentHere(), dir => OS.ShellOpen(dir));
            _editOpen = false; _export.Open();
        }

        /// <summary>The export card/progress consumes the keys while it is up (true: consumed). A running export is never left behind: closing the viewer cancels it.</summary>
        bool ExportInput(InputEvent e)
        {
            if (_export == null || !_export.IsOpen) return false;
            ExportUiAction? a = null;
            if (e is InputEventKey { Pressed: true, Echo: false } k)
                a = k.PhysicalKeycode switch { Key.Up => ExportUiAction.Up, Key.Down => ExportUiAction.Down, Key.Left => ExportUiAction.Left, Key.Right => ExportUiAction.Right,
                    Key.Enter or Key.KpEnter => ExportUiAction.Accept, Key.Escape or Key.Backspace => ExportUiAction.Back, _ => null };
            else if (e is InputEventJoypadButton { Pressed: true } b)
                a = b.ButtonIndex switch { JoyButton.DpadUp => ExportUiAction.Up, JoyButton.DpadDown => ExportUiAction.Down, JoyButton.DpadLeft => ExportUiAction.Left, JoyButton.DpadRight => ExportUiAction.Right,
                    JoyButton.A => ExportUiAction.Accept, JoyButton.B or JoyButton.Back => ExportUiAction.Back, _ => null };
            if (a != null) _export.Handle(a.Value);
            return true;      // (nothing else acts while the card is up)
        }

        /// <summary>The persisted viewing choices (speed, the lap's camera, the overlay) follow the same rule as the edits: a kept lap marks them unsaved, and while a save or reset is pending they do not change (so what was written is exactly what was asked and nothing is lost or unreported).</summary>
        bool PrefsBlocked()
        {
            if (_saveTask == null && !KeepBusy && !_viewerKeepOpen) return false;
            SetNotice("SAVING - WAIT", 1.5f);
            return true;
        }

        /// <summary>After a speed, camera or overlay change by hand: the working copy takes the live value; a change of camera after the first key is a live override of the keys and is not a saved choice (the keys' own playback never marks anything).</summary>
        void PrefChanged()
        {
            if (!_editable || !_gateDone || !UiReady) return;
            _wSpeed = _scene.ViewerSpeed; _wOverlay = !_overlayHidden;
            Dirty = !SameAsBaseline();
        }

        void Touch() { Dirty = !SameAsBaseline(); PushPlan(seekToStart: false); }

        bool SameAsBaseline()
        {
            var b = _baseline;
            if (b == null) return _wPinned || _wIn != null || _wOut != null || _wKeys.Count > 0 || _wLoop || !_wTrim || !_wOverlay || _wSpeed != 1f || _wCamera != ViewerCameraMode.TopDown ? false : true;
            if (b.Pinned != _wPinned || b.TrimEnabled != _wTrim || b.In != _wIn || b.Out != _wOut || b.Playback.Loop != _wLoop || b.Playback.Overlay != _wOverlay || b.Playback.Speed != _wSpeed || ModeOf(b.Playback.Camera) != _wCamera || b.CameraKeys.Count != _wKeys.Count) return false;
            for (int i = 0; i < _wKeys.Count; i++) if (Math.Abs(b.CameraKeys[i].T - _wKeys[i].T) > 1e-4f || ModeOf(b.CameraKeys[i].Mode) != _wKeys[i].Mode
                || b.CameraKeys[i].Free != (_wFreeKeys.TryGetValue(_wKeys[i].T, out var free) ? free : null)) return false;
            return true;
        }

        void EditDo(int row)
        {
            if (!UiReady || _saveTask != null) { if (_saveTask != null) SetNotice("SAVING - WAIT", 1.5f); return; }
            float clock = MathF.Round(_scene.ViewerClock, 2), dur = _scene.ViewerDuration;
            var ids = EditRowIds(); if (row < 0 || row >= ids.Length) return;
            switch (ids[row])
            {
                case 11: OpenExport(); break;
                case 12: BeginViewerKeep(); break;
                case 13: OpenFreeCamera(); break;
                case 0:
                    if (_wOut != null && clock >= _wOut.Value) { SetNotice("IN MUST COME BEFORE OUT", 2.5f); return; }
                    _wIn = clock; Touch(); SetNotice("IN " + ReplayOverlay.FormatTime(clock), 1.5f); break;
                case 1:
                    if (clock <= (_wIn ?? 0f) || clock > dur) { SetNotice("OUT MUST COME AFTER IN", 2.5f); return; }
                    _wOut = clock; Touch(); SetNotice("OUT " + ReplayOverlay.FormatTime(clock), 1.5f); break;
                case 2: _wIn = _wOut = null; Touch(); SetNotice("TRIM CLEARED", 1.5f); break;
                case 3: _wTrim = !_wTrim; Touch(); SetNotice("TRIM " + (_wTrim ? "ON" : "OFF"), 1.5f); break;
                case 4: AddCameraKeyHere(); break;
                case 5: _wKeys.Clear(); _wFreeKeys.Clear(); Touch(); _scene.ViewerReviewCameraKeys(); SetNotice("CAMERA KEYS CLEARED", 1.5f); break;
                case 6: _wLoop = !_wLoop; _scene.ViewerLoop = _wLoop; Touch(); SetNotice("LOOP " + (_wLoop ? "ON" : "OFF"), 1.5f); break;
                case 7: _wPinned = !_wPinned; Touch(); SetNotice(_wPinned ? "PINNED" : "UNPINNED", 1.5f); break;
                case 8: StartSave(false); break;
                case 9: _prompt = EditPrompt.Reset; break;
                case 10: _editOpen = false; break;
            }
        }

        void AddCameraKeyHere()
        {
            if (!UiReady || !_editable || SaveBusy || KeepBusy)
            { SetNotice(_editable ? "SAVING - WAIT" : "KEEP RECORDING BEFORE ADDING CAMERA KEYS", 4f); return; }
            float clock = _scene.ViewerClock;
            bool replaces = _wKeys.Any(k => Math.Abs(k.T - clock) < .05f);
            if (_wKeys.Count >= LapEdits.MaxCameraKeys && !replaces) { SetNotice("TOO MANY CAMERA KEYS", 3f); return; }
            var shot = _scene.ViewerCaptureFreeCamera();
            if (shot != null && FreeCameraActive) shot = shot with { Motion = _freeMotion };
            foreach (var k in _wKeys.Where(k => Math.Abs(k.T - clock) < .05f)) _wFreeKeys.Remove(k.T);
            _wKeys.RemoveAll(k => Math.Abs(k.T - clock) < .05f);
            _wKeys.Add((clock, shot != null ? ViewerCameraMode.TopDown : _scene.ViewerCamera));
            if (shot != null) _wFreeKeys[clock] = shot;
            _wKeys.Sort((a, b) => a.T.CompareTo(b.T));
            Touch(); SetNotice((shot != null ? "FREE CAMERA" : CameraName(_scene.ViewerCamera)) + " FROM " + ReplayOverlay.FormatTime(clock), 3f);
        }

        /// <summary>Tab / Shift+Tab: the clock goes to the next / previous camera key (round the ends) and its camera is in place. In the free camera a free key puts the rig there and flying goes on from it; a preset key ends the free camera.</summary>
        void StepCameraKey(int dir)
        {
            if (!UiReady) return;
            if (_wKeys.Count == 0) { SetNotice("NO CAMERA KEYS - K ADDS ONE", 3f); return; }
            var keys = _wKeys.OrderBy(k => k.T).ToList();
            float clock = _scene.ViewerClock;
            int i = dir > 0 ? keys.FindIndex(k => k.T > clock + .05f) : keys.FindLastIndex(k => k.T < clock - .05f);
            if (i < 0) i = dir > 0 ? 0 : keys.Count - 1;
            var key = keys[i];
            bool free = _wFreeKeys.TryGetValue(key.T, out var shot);
            if (FreeCameraActive && !free) EndFreeCamera();
            _scene.ViewerSeek(key.T);
            if (FreeCameraActive && free)
            {
                _scene.ViewerPlaceFreeKey(shot);
                _freeMotion = shot.Motion; _motionFov = shot.Fov;
            }
            SetNotice($"KEY {i + 1}/{keys.Count}  {ReplayOverlay.FormatTime(key.T)}  " + (free ? "FREE CAMERA" : CameraName(key.Mode)), 3f);
        }

        /// <summary>Del: removes the camera key at the clock (the one Tab just went to).</summary>
        void RemoveCameraKeyHere()
        {
            if (!UiReady || !_editable || SaveBusy || KeepBusy)
            { SetNotice(_editable ? "SAVING - WAIT" : "KEEP RECORDING BEFORE REMOVING CAMERA KEYS", 4f); return; }
            float clock = _scene.ViewerClock;
            int at = _wKeys.FindIndex(k => Math.Abs(k.T - clock) < .05f);
            if (at < 0) { SetNotice("NO KEY HERE - TAB TO SELECT ONE", 3f); return; }
            float t = _wKeys[at].T;
            _wKeys.RemoveAt(at); _wFreeKeys.Remove(t);
            Touch();
            if (!FreeCameraActive) _scene.ViewerReviewCameraKeys();      // (the key before it, or the lap's own camera, takes over; a free camera in hand stays as it is)
            SetNotice($"KEY REMOVED {ReplayOverlay.FormatTime(t)}  ({_wKeys.Count} LEFT)", 3f);
        }

        LapEdits BuildEdits()
        {
            var cam = _wKeys.Count == 0 ? _scene.ViewerCamera : _wCamera;      // (the lap's own camera: the one chosen while no key says otherwise)
            return new LapEdits
            {
                Pinned = _wPinned, TrimEnabled = _wTrim, In = _wIn, Out = _wOut,
                CameraKeys = _wKeys.OrderBy(k => k.T).Select(k => new LapCameraKey { T = k.T, Mode = _wFreeKeys.ContainsKey(k.T) ? LapEdits.Free : Mode(k.Mode), Free = _wFreeKeys.GetValueOrDefault(k.T) }).ToList(),
                Playback = new LapEditsPlayback { Speed = _scene.ViewerSpeed, Loop = _wLoop, Camera = Mode(cam), Overlay = !_overlayHidden },
            };
        }

        void StartSave(bool thenClose)
        {
            if (_saveTask != null) { SetNotice("SAVING - WAIT", 1.5f); return; }
            _closeAfterSave = thenClose;
            _wSpeed = _scene.ViewerSpeed; _wOverlay = !_overlayHidden;
            _saveTask = SaveDelayForTest > 0 ? Delayed(SaveDelayForTest, _archive.SaveEdits(_lapId, BuildEdits())) : _archive.SaveEdits(_lapId, BuildEdits());
            SetNotice("SAVING EDITS...", 30f);
        }

        void SaveDone(EditsSaveResult r)
        {
            bool close = _closeAfterSave; _closeAfterSave = false;
            if (_resetting)
            {
                _resetting = false;
                if (r.Ok) { _baseline = r.Edits; LoadWorking(r.Edits); Dirty = false; _editsApplied = false; PushPlan(seekToStart: true); SetNotice("EDITS RESET (THE OLD ONES ARE KEPT AS A BACKUP)", 4f); }
                else SetNotice(("RESET FAILED: " + r.Message).ToUpperInvariant(), 6f);
                return;
            }
            if (r.Ok)
            {
                _baseline = r.Edits; Dirty = !SameAsBaseline(); SetNotice("EDITS SAVED", 2.5f);      // (what was written is the baseline; anything still different stays unsaved)
                if (close) Close();
            }
            else SetNotice(("NOT SAVED: " + r.Message).ToUpperInvariant(), 8f);      // (the edits stay, still unsaved)
        }

        // ---------------------------------------------------------------- input (true: consumed)

        bool EditInput(InputEvent e)
        {
            if (_state != ReplayViewerState.Playing) return false;
            // a modal question
            if (_prompt != EditPrompt.None)
            {
                bool yes = false, no = false, discard = false;
                if (e is InputEventKey { Pressed: true, Echo: false } k) { yes = k.PhysicalKeycode is Key.Enter or Key.KpEnter; no = k.PhysicalKeycode is Key.Escape; discard = k.PhysicalKeycode == Key.D; }
                else if (e is InputEventJoypadButton { Pressed: true } b) { yes = b.ButtonIndex == JoyButton.A; no = b.ButtonIndex == JoyButton.B; discard = b.ButtonIndex == JoyButton.X; }
                else return true;
                var p = _prompt;
                if (no) { _prompt = EditPrompt.None; return true; }
                if (p == EditPrompt.KeepFirst && yes) { BeginViewerKeep(); }
                else if (p == EditPrompt.Reset && yes) { _prompt = EditPrompt.None; _resetting = true; _saveTask = SaveDelayForTest > 0 ? Delayed(SaveDelayForTest, _archive.ResetEdits(_lapId)) : _archive.ResetEdits(_lapId); SetNotice("RESETTING...", 30f); }
                else if (p == EditPrompt.Unsaved && yes) { _prompt = EditPrompt.None; StartSave(true); }
                else if (p == EditPrompt.Unsaved && discard) { _prompt = EditPrompt.None; Dirty = false; Close(); }
                return true;
            }
            int rows = EditRows().Length;
            if (e is InputEventKey { Pressed: true, Echo: false } key)
            {
                _view.PadMode = false;
                var c = key.PhysicalKeycode;
                if (c == Key.E) { OpenEdit(); return true; }
                if (_editOpen)
                {
                    if (c is Key.Up) { _editSel = (_editSel + rows - 1) % rows; return true; }
                    if (c is Key.Down) { _editSel = (_editSel + 1) % rows; return true; }
                    if (c is Key.Enter or Key.KpEnter) { EditDo(_editSel); return true; }
                    if (c is Key.Escape or Key.Backspace) { _editOpen = false; return true; }
                }
                if (c == Key.Tab) { StepCameraKey(key.ShiftPressed ? -1 : 1); return true; }
                if (c == Key.Delete) { RemoveCameraKeyHere(); return true; }
                int direct = c switch { Key.I => 0, Key.O => 1, Key.T => 3, Key.K => 4, Key.L => 6, Key.P => 7, Key.S => 8, _ => -1 };
                if (direct >= 0 && UiReady) { if (!_editable) _prompt = EditPrompt.KeepFirst; else EditDo(direct); return true; }
            }
            else if (e is InputEventJoypadButton { Pressed: true } jb)
            {
                _view.PadMode = true;
                if (jb.ButtonIndex == JoyButton.Back) { OpenEdit(); return true; }
                if (_editOpen)
                {
                    switch (jb.ButtonIndex)
                    {
                        case JoyButton.DpadUp: _editSel = (_editSel + rows - 1) % rows; return true;
                        case JoyButton.DpadDown: _editSel = (_editSel + 1) % rows; return true;
                        case JoyButton.A: EditDo(_editSel); return true;
                        case JoyButton.B: _editOpen = false; return true;
                    }
                    return true;      // (nothing else acts while the panel is up: no seek, camera or speed by accident)
                }
            }
            return false;
        }

        void OpenEdit()
        {
            if (!UiReady) return;
            _editOpen = !_editOpen;      // (every lap: a lap that is not kept lists EXPORT and the way to KEEP FIRST)
            if (_editOpen) _editSel = 0;      // (a panel that opens again starts at its first row, not where it was left)
        }

        /// <summary>Leaving: with unsaved edits the player chooses; a save in flight is waited for.</summary>
        bool EditBack()
        {
            if (_saveTask != null || KeepBusy || _viewerKeepOpen) { SetNotice("SAVING - WAIT", 1.5f); return true; }
            if (_editOpen) { _editOpen = false; return true; }
            if (Dirty) { _prompt = EditPrompt.Unsaved; return true; }
            return false;
        }

        void FillEditView(ReplayOverlayView v)
        {
            v.Editable = true;      // (the EDIT hint shows for every lap: an unkept one answers KEEP FIRST)
            v.Dirty = Dirty; v.Pinned = _wPinned; v.TrimOn = _wTrim;
            v.WindowIn = _wIn ?? -1f; v.WindowOut = _wOut ?? -1f;
            v.KeyTimes = _wKeys.Select(k => k.T).ToArray();
            bool ex = _export != null && _export.IsOpen;
            v.EditOpen = _editOpen && !ex; v.EditRows = _editOpen && !ex ? EditRows() : Array.Empty<string>(); v.EditSelected = _editSel;
            v.ExportTitle = ex ? _export.Title : ""; v.ExportRows = ex ? _export.Rows.ToArray() : Array.Empty<string>(); v.ExportSelected = ex ? _export.Selected : 0;
            v.ExportLines = ex ? _export.Lines.ToArray() : Array.Empty<string>(); v.ExportFraction = ex ? _export.Fraction : -1f;
            bool pad = v.PadMode;
            if (_motionOpen) { v.ExportTitle = "CAMERA MOTION - NEW SHOT"; v.ExportRows = MotionRows(); v.ExportSelected = _motionRow;
                v.ExportLines = new[] { "ARROWS CHANGE - ENTER SELECT", "B/ESC DONE - THEN K ADD KEY", "TAB STEPS THROUGH THE KEYS" }; }
            v.Prompt = _viewerKeepOpen || KeepBusy ? ViewerKeepPrompt(pad) : _prompt switch
            {
                EditPrompt.KeepFirst => new[] { "KEEP THIS LAP FIRST", "EDITS BELONG TO A KEPT LAP", pad ? "A KEEP RECORDING  B CANCEL" : "ENTER KEEP RECORDING  ESC CANCEL" },
                EditPrompt.Unsaved => new[] { "UNSAVED EDITS", pad ? "A SAVE  X DISCARD  B CANCEL" : "ENTER SAVE  D DISCARD  ESC CANCEL" },
                EditPrompt.Reset => new[] { "RESET EDITS?", "THE OLD ONES STAY AS A BACKUP", pad ? "A RESET  B CANCEL" : "ENTER RESET  ESC CANCEL" },
                _ => Array.Empty<string>(),
            };
        }
    }
}
