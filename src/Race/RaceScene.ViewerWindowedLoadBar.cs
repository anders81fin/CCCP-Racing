using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// Windowed scenario load-bar (--viewer-windowed=load-bar --vw-load-target=TRACK [--vw-load-pad] [--track=FROM]): the real menu route (StartRun on another track, or the same track = a fast load with no rebuild)
/// with every frame of the TrackLoadingScreen bar sampled. Checks: never decreases, below 1 until ready, 1 at ready, a press before ready does nothing, a key (or pad button) after ready continues.
/// Screenshots (start, middle, ready) are the root viewport, saved under --vw-media (never overwritten).
/// </summary>
public partial class RaceScene
{
    static void VwShot(SceneTree tree, string name)
    {
        string dir = VlArg("--vw-media");
        var img = tree.Root.GetTexture().GetImage();
        if (dir == null) return;
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, name + ".png");
        for (int i = 2; File.Exists(path); i++) path = Path.Combine(dir, $"{name}-{i}.png");
        var err = img.SavePng(path);
        VwLog("shot", ("path", path), ("error", err));
    }

    IEnumerable VwLoadBar()
    {
        string target = VlArg("--vw-load-target") ?? "ilyich";
        bool pad = OS.GetCmdlineUserArgs().Contains("--vw-load-pad");
        foreach (var x in VwWaitReady()) yield return x;
        foreach (var _ in VwFrames(120)) yield return null;
        var tree = GetTree();
        var old = this;
        string from = _trackId;
        bool fast = from == target;
        _chosenRunTrack = target;
        VwLog("loadbar-start", ("from", from), ("to", target), ("fast", fast), ("pad", pad));
        StartRun(MenuItem.Race);      // (from here on this instance is freed by a reload: statics and locals only)
        float last = -1, min = 9, max = -1; int samples = 0, back = 0, early1 = 0, distinct = 0, steps = 0;
        bool shotStart = false, shotMid = false, shotReady = false, preKey = false, ready = false; int readyFrames = 0;
        float atPreKey = -1; bool preKeyIgnored = true;
        TrackLoadingScreen card = null;
        int f = 0, controlOff = -1;      // (an injected press is delivered a frame after it is parsed: control stays marked for a few frames so it is not counted as foreign input)
        for (; f < 60 * 240; f++)
        {
            yield return null;
            if (controlOff >= 0 && f >= controlOff) { _vwDrv.SetControl(false); controlOff = -1; }
            var c = TrackLoadingScreen.Active;
            if (c == null) break;
            card = c;
            float p = c.Progress;
            samples++;
            if (p < last - 1e-6f) back++;
            if (p != last) distinct++;
            last = p; min = Math.Min(min, p); max = Math.Max(max, p);
            var fresh = VwFindLive(tree.Root);
            if (fresh != null && !ReferenceEquals(fresh, old)) steps = Math.Max(steps, fresh._loadSteps);
            if (!c.IsReady && p >= 1f) early1++;
            if (!c.IsReady)
            {
                if (!shotStart && (fast || (fresh != null && !ReferenceEquals(fresh, old) && fresh._loadSteps > 0))) { shotStart = true; VwShot(tree, $"loadbar-{target}-1-start"); }
                if (!shotMid && p >= 0.45f) { shotMid = true; VwShot(tree, $"loadbar-{target}-2-middle"); }
                if (!preKey && p >= 0.3f)
                {
                    preKey = true; atPreKey = p;
                    _vwDrv.SetControl(true);
                    Input.ParseInputEvent(VwKey(Key.Enter, true, 0)); Input.ParseInputEvent(VwKey(Key.Enter, false, 0));
                    controlOff = f + 4;
                }
                if (preKey && TrackLoadingScreen.Active == null) preKeyIgnored = false;
                continue;
            }
            // ready
            readyFrames++;
            if (!ready) { ready = true; VwLog("loadbar-ready", ("progress", p), ("frames", f), ("steps", steps)); }
            if (readyFrames == 3 && !shotReady) { shotReady = true; VwShot(tree, $"loadbar-{target}-3-ready"); }
            if (readyFrames == 20)
            {
                _vwDrv.SetControl(true);
                if (pad) { Input.ParseInputEvent(new InputEventJoypadButton { ButtonIndex = JoyButton.A, Pressed = true, Device = 0 }); Input.ParseInputEvent(new InputEventJoypadButton { ButtonIndex = JoyButton.A, Pressed = false, Device = 0 }); }
                else { Input.ParseInputEvent(VwKey(Key.Enter, true, 0)); Input.ParseInputEvent(VwKey(Key.Enter, false, 0)); }
                controlOff = f + 4;
            }
        }
        _vwDrv.SetControl(false);
        bool gone = TrackLoadingScreen.Active == null;
        VwLog("loadbar-end", ("to", target), ("frames", f), ("samples", samples), ("min", min), ("max_before_ready", ready ? 1f : max), ("final", card?.Progress ?? -1f), ("distinct", distinct), ("build_steps", steps), ("back", back), ("early1", early1), ("pre_key_at", atPreKey));
        VwCheck(ready, $"loadbar {from}->{target}: the card reached ready (frames {f})");
        VwCheck(back == 0, $"loadbar {from}->{target}: the bar never went backwards ({samples} samples, {distinct} distinct values)");
        VwCheck(early1 == 0, $"loadbar {from}->{target}: the bar was below 1.0 until ready");
        VwCheck(card != null && card.Progress == 1f, $"loadbar {from}->{target}: the bar is 1.0 at ready ({card?.Progress})");
        VwCheck(fast || (steps > 0 && distinct > 3), $"loadbar {from}->{target}: the build moved the bar by its checkpoints (steps {steps}, distinct {distinct})");
        VwCheck(fast || preKey, $"loadbar {from}->{target}: a press was sent before ready");
        VwCheck(preKeyIgnored, $"loadbar {from}->{target}: a press before ready did not continue");
        VwCheck(gone, $"loadbar {from}->{target}: a {(pad ? "pad button" : "key")} after ready continues (card gone)");
        foreach (var _ in VwFrames(30)) yield return null;
        var live = VwFindLive(tree.Root);
        VwCheck(live != null && live.ProcessMode == ProcessModeEnum.Inherit, $"loadbar {from}->{target}: the world runs after the press (mode {live?.ProcessMode})");
    }
}
