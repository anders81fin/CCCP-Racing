using System;
using System.Collections;
using System.Linq;
using CCCPRacing.Replay;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

// Windowed scenario "make-clip" (--viewer-windowed-only=makeclip): MAKE CLIP in the real viewer of an unkept F3 lap, through the production paths. A lap is captured, F3 opens it, the picture is stopped, M makes the
// clip (8 s before, 4 s after, in the TV broadcast, looping), ENTER opens the export card on THE CUT with 60 FPS at normal speed, ESC drops the clip and gives the viewer's own plan back, V on a clip of a lap that
// is not kept asks to KEEP FIRST, and a stop near the start clips from 0:00.
public partial class RaceScene
{
    IEnumerable VwMakeClip()
    {
        foreach (var x in VwWaitReady()) yield return x;
        foreach (var x in VrUntil(() => _state == State.Racing && _lapClock > 32f, 60 * 90, "the race has run 32 s")) yield return x;      // (the lap is archived as far as it has run: the clip needs a lap of some length)
        LapArchiveCapture(47.55f, LapKind.Normal, true, false);
        string lapId = _lastLapId;
        for (int i = 0; i < 90; i++) yield return null;
        foreach (var x in VrTap(Key.F3)) yield return x;
        foreach (var x in VrUntil(() => ReplayViewer is { State: ReplayViewerState.Playing }, 60 * 30, "the viewer plays")) yield return x;
        var v = ReplayViewer;
        VwCheck(v != null && v.LapId == lapId && v.ClipSpan == null, "F3 opened the lap, no clip yet");
        var scene = v.Scene;
        bool loopBefore = scene.ViewerLoop; var cameraBefore = scene.ViewerCamera;

        // ---- stop the picture 6 s before the end and make the clip
        float dur = scene.ViewerDuration, stop = dur - 6f;
        VwCheck(dur > 20f, $"the lap is long enough ({dur:F1} s)");
        scene.ViewerSeek(stop); scene.ViewerSetPaused(true);
        for (int i = 0; i < 20; i++) yield return null;
        foreach (var x in VrTap(Key.M)) yield return x;
        for (int i = 0; i < 20; i++) yield return null;
        var clip = v.ClipSpan;
        VwCheck(clip is { } c1 && Math.Abs(c1.In - (stop - 8f)) < 0.3f && Math.Abs(c1.Out - (stop + 4f)) < 0.3f, $"M makes the clip 8 s before and 4 s after the stop ({clip?.In:F2} - {clip?.Out:F2})");
        VwCheck(scene.ViewerCamera == ViewerCameraMode.Auto && scene.ViewerLoop && scene.ViewerEdits != null && scene.ViewerEdits.TrimEnabled && Math.Abs((scene.ViewerEdits.In ?? -1) - (stop - 8f)) < 0.3f && !scene.ViewerPaused,
            "the clip plays in the TV broadcast, looped, from its start");
        VwCheck(!v.Dirty, "a clip is not an edit: nothing is marked unsaved");
        foreach (var x in VrShot("makeclip-overlay")) yield return x;
        // the loop keeps the playback inside the clip
        float lo = 99f, hi = -1f;
        ulong loopStart = Time.GetTicksMsec();      // (14 s of the clock, not 840 frames: a fast machine draws more than 60 a second)
        while (Time.GetTicksMsec() - loopStart < 14000) { yield return null; float t = scene.ViewerClock; lo = Math.Min(lo, t); hi = Math.Max(hi, t); }
        VwCheck(lo >= stop - 8.5f && hi <= stop + 4.5f && hi - lo > 10f, $"the playback loops inside the clip ({lo:F2} - {hi:F2})");

        // ---- ENTER opens the export card on the cut
        foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 10; i++) yield return null;
        VwCheck(v.ExportCardOpen && v.ExportCardRows[0].Contains("THE CUT") && v.ExportCardRows[4].Contains("12.0") && v.ExportCardLines.Contains("60 FPS · NORMAL SPEED"),
            $"ENTER opens the export card on THE CUT, 12 s, 60 FPS · NORMAL SPEED ({(v.ExportCardOpen ? v.ExportCardRows[0] + " / " + v.ExportCardRows[4] : "closed")})");
        foreach (var x in VrShot("makeclip-export")) yield return x;
        foreach (var x in VrTap(Key.Escape)) yield return x;
        for (int i = 0; i < 10; i++) yield return null;
        VwCheck(!v.ExportCardOpen && v.ClipSpan != null, "ESC closes the card, the clip stays");

        // ---- V on a clip of an unkept lap: KEEP FIRST, the clip waits
        foreach (var x in VrTap(Key.V)) yield return x;
        for (int i = 0; i < 10; i++) yield return null;
        VwCheck(v.KeepFirstAsked && v.ClipSpan != null, "V on the clip of a lap that is not kept asks to KEEP FIRST and the clip stays");
        foreach (var x in VrTap(Key.Escape)) yield return x;
        for (int i = 0; i < 10; i++) yield return null;

        // ---- ESC drops the clip: the lap's own plan, loop and camera are back
        foreach (var x in VrTap(Key.Escape)) yield return x;
        for (int i = 0; i < 10; i++) yield return null;
        VwCheck(v.ClipSpan == null && scene.ViewerLoop == loopBefore && scene.ViewerCamera == cameraBefore && (scene.ViewerEdits == null || scene.ViewerEdits.In == null), "ESC drops the clip: the loop, the camera and the plan are the lap's own again");

        // ---- a stop near the start clips from 0:00
        scene.ViewerSeek(3f); scene.ViewerSetPaused(true);
        for (int i = 0; i < 20; i++) yield return null;
        foreach (var x in VrTap(Key.M)) yield return x;
        for (int i = 0; i < 20; i++) yield return null;
        var early = v.ClipSpan;
        VwCheck(early is { } c2 && c2.In == 0f && Math.Abs(c2.Out - 7f) < 0.3f, $"a stop at 3 s clips 0:00 - 0:07 ({early?.In:F2} - {early?.Out:F2})");
        foreach (var x in VrTap(Key.M)) yield return x;      // (M again drops it)
        for (int i = 0; i < 10; i++) yield return null;
        VwCheck(v.ClipSpan == null, "M again drops the clip");
        foreach (var x in VrTap(Key.Escape)) yield return x;
        foreach (var x in VrUntil(() => ReplayViewer == null && _state == State.Racing, 60 * 20, "the viewer closed, back in the drive")) yield return x;
    }
}
