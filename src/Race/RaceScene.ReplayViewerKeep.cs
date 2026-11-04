using System;
using System.Threading.Tasks;
using CCCPRacing.Replay;
using Godot;

namespace CCCPRacing.Race;

public partial class RaceScene
{
    public sealed partial class ReplayViewerSession
    {
        LapEntryInfo _keepInfo;
        bool _viewerKeepOpen, _viewerKeepPaused, _viewerKeepRename, _viewerKeepHidden;
        string _viewerKeepName = "", _viewerKeepSavedName = "";
        Task<RetainResult> _viewerKeepTask;
        internal static string ViewerKeepFailForTest;
        internal static float ViewerKeepDelayForTest;
        public bool KeepOpen => _viewerKeepOpen;
        public bool KeepBusy => _viewerKeepTask != null;

        void BeginViewerKeep()
        {
            if (!UiReady || _archive == null || _keepInfo == null || _saveTask != null || KeepBusy) return;
            _viewerKeepRename = _editable;
            _viewerKeepName = _editable ? _keepInfo.Meta.Name ?? "" : "";
            _viewerKeepPaused = _scene.ViewerPaused;
            _viewerKeepHidden = _overlayHidden;
            _scene.ViewerSetPaused(true);
            _viewerKeepOpen = true; _editOpen = false; _prompt = EditPrompt.None;
            _overlayHidden = false; _holdAwaitRelease = true;
        }

        void EndViewerKeep()
        {
            _viewerKeepOpen = false; _holdAwaitRelease = true; _overlayHidden = _viewerKeepHidden;
            if (UiReady) _scene.ViewerSetPaused(_viewerKeepPaused);
        }

        void CommitViewerKeep()
        {
            if (KeepBusy) return;
            _viewerKeepSavedName = CleanKeepName(_viewerKeepName);
            if (_viewerKeepSavedName.Length == 0) _viewerKeepSavedName = _live.DefaultKeepName(_keepInfo);
            _live._lapArchiveUsed = _archive;
            _viewerKeepTask = ViewerKeepFailForTest != null
                ? Task.FromResult(new RetainResult(false, ViewerKeepFailForTest, null))
                : _archive.Retain(_lapId, _viewerKeepSavedName);
            if (ViewerKeepDelayForTest > 0) _viewerKeepTask = Delayed(ViewerKeepDelayForTest, _viewerKeepTask);
            SetNotice(_viewerKeepRename ? "SAVING THE NEW NAME..." : "SAVING THE RECORDING...", 30f);
        }

        void ViewerKeepFrame()
        {
            if (_viewerKeepTask is not { IsCompleted: true } task) return;
            _viewerKeepTask = null;
            var result = task.IsCompletedSuccessfully ? task.Result : new RetainResult(false, task.Exception?.GetBaseException().Message ?? "unknown error", null);
            if (!result.Ok) { SetNotice(("NOT KEPT: " + result.Message).ToUpperInvariant(), 8f); return; }
            _keepInfo = _keepInfo with { Retained = true, Dir = result.Dir, Meta = _keepInfo.Meta with { Name = _viewerKeepSavedName, Retained = true } };
            _editable = true;
            _title = _viewerKeepSavedName;
            // Keep the loaded world and working edits, rather than reloading or seeking.
            EndViewerKeep();
            _wSpeed = _scene.ViewerSpeed; _wOverlay = !_overlayHidden;
            Dirty = !SameAsBaseline();
            SetNotice((_viewerKeepRename ? "RENAMED: " : "KEPT: ") + _viewerKeepSavedName, 4f);
        }

        bool ViewerKeepInput(InputEvent e)
        {
            if (!_viewerKeepOpen && !KeepBusy) return false;
            if (KeepBusy) return true;
            if (e is InputEventKey { Pressed: true } k)
            {
                _view.PadMode = false;
                var key = k.PhysicalKeycode;
                if (key is Key.Enter or Key.KpEnter && !k.Echo && !k.AltPressed) CommitViewerKeep();
                else if (key == Key.Escape && !k.Echo) { EndViewerKeep(); SetNotice("KEEP CANCELLED - NOTHING WAS SAVED"); }
                else if (key == Key.Backspace && _viewerKeepName.Length > 0) _viewerKeepName = _viewerKeepName[..^1];
                else if (!k.CtrlPressed && !k.AltPressed && !k.MetaPressed && k.Unicode > 0 && k.Unicode <= char.MaxValue && _viewerKeepName.Length < KeepNameMax)
                {
                    char c = char.ToUpperInvariant((char)k.Unicode);
                    if (Leaderboard.IsNameChar(c) && !(c == ' ' && (_viewerKeepName.Length == 0 || _viewerKeepName[^1] == ' '))) _viewerKeepName += c;
                }
            }
            else if (e is InputEventJoypadButton { Pressed: true } b)
            {
                _view.PadMode = true;
                string t = _viewerKeepName;
                switch (b.ButtonIndex)
                {
                    case JoyButton.Start: CommitViewerKeep(); break;
                    case JoyButton.DpadUp or JoyButton.DpadDown:
                        int i = t.Length == 0 ? -1 : NameChars.IndexOf(t[^1]);
                        char c = NameChars[i < 0 ? 0 : (i + (b.ButtonIndex == JoyButton.DpadUp ? 1 : NameChars.Length - 1)) % NameChars.Length];
                        t = t.Length == 0 ? c.ToString() : t[..^1] + c; break;
                    case JoyButton.A or JoyButton.DpadRight:
                        if (t.Length < KeepNameMax) t += t.Length > 0 ? t[^1] : NameChars[0]; break;
                    case JoyButton.B or JoyButton.DpadLeft:
                        if (t.Length > 0) t = t[..^1];
                        else if (b.ButtonIndex == JoyButton.B) { EndViewerKeep(); SetNotice("KEEP CANCELLED - NOTHING WAS SAVED"); }
                        break;
                    case JoyButton.Back: EndViewerKeep(); SetNotice("KEEP CANCELLED - NOTHING WAS SAVED"); break;
                }
                _viewerKeepName = t;
            }
            return true;
        }

        string[] ViewerKeepPrompt(bool pad) => new[] {
            _viewerKeepRename ? "RENAME RECORDING" : "KEEP RECORDING",
            "> " + _viewerKeepName + " <",
            "EMPTY = DEFAULT NAME",
            KeepBusy ? "SAVING - PLEASE WAIT" : pad ? "UP/DOWN LETTER  A ADD  B DELETE" : "TYPE A NAME  BACKSPACE DELETE",
            pad ? "START SAVE  BACK CANCEL" : "ENTER SAVE  ESC CANCEL" };
    }
}
