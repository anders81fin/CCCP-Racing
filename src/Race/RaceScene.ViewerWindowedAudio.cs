using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CCCPRacing.Audio;
using Godot;

namespace CCCPRacing.Race;

// Windowed scenario "hind-audio" (--viewer-windowed-only=audio): the replay viewer's Hind rotor through the game's own audio bus with the machine's real audio driver.
// Only the game's own mix is read: an AudioEffectCapture on the bus the rotor plays on (AudioMix.Jet), added for the run and removed at the end. No desktop or microphone
// capture. Quantitative only (RMS of the bus output): nothing here claims the sound was listened to.
public partial class RaceScene
{
    static float VwRms(List<Vector2> s) { if (s.Count == 0) return 0; double a = 0; foreach (var v in s) a += 0.5 * (v.X * v.X + v.Y * v.Y); return (float)Math.Sqrt(a / s.Count); }

    IEnumerable VwCaptureFor(AudioEffectCapture cap, double seconds, List<Vector2> into)
    {
        ulong t0 = Time.GetTicksMsec();
        while ((Time.GetTicksMsec() - t0) / 1000.0 < seconds)
        {
            int n = cap.GetFramesAvailable();
            if (n > 0) into.AddRange(cap.GetBuffer(n));
            yield return null;
        }
    }

    static void VwRotors(Node n, List<RotorSound> into) { if (n is RotorSound r) into.Add(r); foreach (var c in n.GetChildren()) VwRotors(c, into); }

    IEnumerable VwHindAudio()
    {
        var lap = VwFixtureLap(VlArg("--vw-lap") ?? _trackId);
        foreach (var x in VwWaitReady()) yield return x;
        VwLog("audio-driver", ("driver", AudioServer.GetDriverName()), ("output_device", AudioServer.GetOutputDevice()), ("mix_rate", AudioServer.GetMixRate()), ("playback_speed", AudioServer.PlaybackSpeedScale));
        VwCheck(AudioServer.GetDriverName() != "Dummy", $"the audio driver is a real one ({AudioServer.GetDriverName()})");
        // live scene's audio state before the viewer opens (restored exactly after close)
        var livePlayers = new List<Node>(); foreach (var n in FindChildren("*", "", true, false)) if (n is AudioStreamPlayer or AudioStreamPlayer3D) livePlayers.Add(n);
        bool Paused(Node n) => n is AudioStreamPlayer a ? a.StreamPaused : ((AudioStreamPlayer3D)n).StreamPaused;
        var liveBefore = livePlayers.Select(Paused).ToList();
        ReplayViewerSession s = null;
        foreach (var x in VwOpenPlaying(lap, "hind-audio", r => s = r)) yield return x;
        if (s == null) yield break;
        var v = s.Scene; float dur = v.ViewerDuration;
        string bus = AudioMix.BusFor(AudioMix.Jet);
        int bi = AudioServer.GetBusIndex(bus);
        var cap = new AudioEffectCapture { BufferLength = 0.5f };
        AudioServer.AddBusEffect(bi, cap);
        int effectIndex = AudioServer.GetBusEffectCount(bi) - 1;
        var report = new List<string>();
        try
        {
            // find a clock with a flying Hind: the rotor level the tape writes is above 0.05; the nearest one to the listener wins
            float tOn = -1, best = float.MaxValue, tOffScan = 0, lowest = float.MaxValue; var scan = new List<string>();
            for (int i = 0; i <= 39; i++)
            {
                v.ViewerSeek(dur * i / 40f);
                var rs = new List<RotorSound>(); VwRotors(v, rs);
                float mx = rs.Count == 0 ? 0 : rs.Max(r => r.Level);
                scan.Add($"{dur * i / 40f:0.0}:{rs.Count}:{mx:0.00}");
                if (mx < lowest) { lowest = mx; tOffScan = dur * i / 40f; }
                foreach (var r in rs)
                    if (r.Level > 0.3f && r.IsInsideTree())
                    {
                        var cam = v.GetViewport().GetCamera3D(); float d = cam == null ? 0 : r.GlobalPosition.DistanceTo(cam.GlobalPosition);
                        if (d < best) { best = d; tOn = dur * i / 40f; }
                    }
                yield return null;
            }
            VwLog("hind-clock", ("t_on", tOn), ("nearest_distance_m", best), ("lowest_level", lowest), ("t_off", tOffScan), ("scan_t:rotors:maxlevel", string.Join(" ", scan)));
            VwCheck(tOn >= 0, "a clock with a flying Hind was found in the lap");
            if (tOn < 0) yield break;
            // a clock where no rotor is on: the lap start
            float tOff = tOffScan;
            v.ViewerSeek(tOff); { var rs0 = new List<RotorSound>(); VwRotors(v, rs0); VwLog("rotors-off", ("t_off", tOff), ("levels", string.Join(",", rs0.Select(r => r.Level.ToString("0.00"))))); }
            yield return null;

            // 1) a fresh aircraft (built by the viewer, first seek): play, measured after the sound has settled
            v.ViewerSetPaused(false); v.ViewerSeek(tOn);
            foreach (var x in VwWait(0.5)) yield return x;
            var play = new List<Vector2>(); cap.ClearBuffer();
            foreach (var x in VwCaptureFor(cap, 0.6, play)) yield return x;
            float rmsFresh = VwRms(play);
            // 2) paused: silence
            v.ViewerSetPaused(true);
            foreach (var x in VwWait(0.35)) yield return x;
            var paused = new List<Vector2>(); cap.ClearBuffer();
            foreach (var x in VwCaptureFor(cap, 0.5, paused)) yield return x;
            float rmsPaused = VwRms(paused);
            // 3) resume: nonzero again
            v.ViewerSetPaused(false);
            foreach (var x in VwWait(0.3)) yield return x;
            var resumed = new List<Vector2>(); cap.ClearBuffer();
            foreach (var x in VwCaptureFor(cap, 0.5, resumed)) yield return x;
            float rmsResumed = VwRms(resumed);
            // 4) seek to a clock with no rotor: no stale buffered rotor (the generator buffer is 0.2 s, the level smoothing about 0.1 s)
            v.ViewerSeek(tOff);
            cap.ClearBuffer();
            var stale = new List<Vector2>();
            foreach (var x in VwCaptureFor(cap, 0.25, stale)) yield return x;
            float rmsStale = VwRms(stale);
            // 5) back to the flying clock: the kept (reset) aircraft against the fresh one
            v.ViewerSeek(tOn);
            foreach (var x in VwWait(0.5)) yield return x;
            var again = new List<Vector2>(); cap.ClearBuffer();
            foreach (var x in VwCaptureFor(cap, 0.6, again)) yield return x;
            float rmsAgain = VwRms(again);
            // 6) repeated seeks and wraps do not leave sound behind
            for (int k = 0; k < 6; k++) { v.ViewerSeek(k % 2 == 0 ? tOff : tOn); yield return null; yield return null; }
            v.ViewerSeek(tOff); cap.ClearBuffer();
            var after = new List<Vector2>();
            foreach (var x in VwCaptureFor(cap, 0.25, after)) yield return x;
            float rmsAfter = VwRms(after);
            VwLog("hind-audio-rms", ("fresh", rmsFresh), ("paused", rmsPaused), ("resumed", rmsResumed), ("after_seek_to_off", rmsStale), ("reset_again", rmsAgain), ("after_6_seeks_off", rmsAfter),
                ("frames_fresh", play.Count), ("frames_paused", paused.Count), ("frames_stale", stale.Count));
            VwCheck(rmsFresh > 1e-4f, $"the fresh Hind rotor reaches the bus (RMS {rmsFresh:0.000000})");
            VwCheck(rmsPaused < 1e-5f || rmsPaused < 0.02f * rmsFresh, $"paused viewer: the bus is silent (RMS {rmsPaused:0.000000} vs playing {rmsFresh:0.000000})");
            VwCheck(rmsResumed > 0.3f * rmsFresh, $"resumed viewer: rotor output returns (RMS {rmsResumed:0.000000})");
            VwLog("note", ("text", "this fixture lap has every Hind rotor at tape level 0.90 at every clock (scan above), so seeks between its clocks cannot show a stale tail; the discriminating test is the controlled one below"));
            // 7) controlled stale-buffer test through the production reset (HindSorties.ResetReplayAircraft, what TapeSink.Recycle calls): one replay aircraft in front of the
            //    listener, the viewer paused (the other Hinds silent). Control: the tape turns the level to 0 without a reset -> the 0.2 s generator buffer and the level smoothing leave a tail.
            //    Reset: the same, with the reset -> silence.
            v.ViewerSetPaused(true);
            foreach (var x in VwWait(0.4)) yield return x;
            var ac = Track.HindSorties.BuildReplayAircraft("99");
            var eng = ac.GetChildren().OfType<RotorSound>().First();
            ac.ProcessMode = ProcessModeEnum.Always;
            v.GetParent().AddChild(ac);      // (the viewer's SubViewport: same world and listener, but outside the scene's pause freeze)
            var camA = v.GetViewport().GetCamera3D(); ac.GlobalPosition = camA.GlobalPosition + (-camA.GlobalTransform.Basis.Z) * 18f + new Vector3(0, 6f, 0);
            float rmsOn = 0, rmsCtl = 0, rmsRst = 0, rmsCtlLate = 0, rmsRstLate = 0; string ctlWin = "", rstWin = "";
            float Late(List<Vector2> l) => VwRms(l.Skip((int)(0.06 * AudioServer.GetMixRate()) * 1).ToList());      // after the first 60 ms (the output pipeline's own latency holds audio mixed before the reset)
            string Wins(List<Vector2> l) { int w = (int)(0.025 * AudioServer.GetMixRate()); return string.Join(",", Enumerable.Range(0, l.Count / w).Select(i => VwRms(l.Skip(i * w).Take(w).ToList()).ToString("0.000"))); }
            for (int pass = 0; pass < 2; pass++)
            {
                eng.Level = 0.9f; eng.StreamPaused = false;
                foreach (var x in VwWait(1.0)) yield return x;
                var on = new List<Vector2>(); cap.ClearBuffer();
                foreach (var x in VwCaptureFor(cap, 0.4, on)) yield return x;
                float r0 = VwRms(on);
                if (pass == 0) { rmsOn = r0; eng.Level = 0f; }                      // control: no reset
                else { Track.HindSorties.ResetReplayAircraft(ac); }                    // reset (also sets the level to 0)
                cap.ClearBuffer();
                var tail = new List<Vector2>();
                foreach (var x in VwCaptureFor(cap, 0.25, tail)) yield return x;
                if (pass == 0) { rmsCtl = VwRms(tail); rmsCtlLate = Late(tail); ctlWin = Wins(tail); } else { rmsRst = VwRms(tail); rmsRstLate = Late(tail); rstWin = Wins(tail); }
            }
            ac.QueueFree();
            VwLog("hind-audio-controlled", ("playing", rmsOn), ("control_tail_no_reset", rmsCtl), ("control_after_60ms", rmsCtlLate), ("after_reset", rmsRst), ("reset_after_60ms", rmsRstLate), ("control_25ms_windows", ctlWin), ("reset_25ms_windows", rstWin));
            VwCheck(rmsOn > 1e-3f, $"controlled test: the test aircraft reaches the bus (RMS {rmsOn:0.000000})");
            VwCheck(rmsCtl > 0.15f * rmsOn, $"controlled test: WITHOUT the reset a stale tail follows the level change (RMS {rmsCtl:0.000000} = {100 * rmsCtl / Math.Max(rmsOn, 1e-9f):0}% of playing), so the test can see a stale buffer");
            VwCheck(rmsRstLate < 0.02f * rmsOn && rmsCtlLate > 0.15f * rmsOn, $"controlled test: after ResetReplayAircraft the bus is silent once the output pipeline has drained (after 60 ms: RMS {rmsRstLate:0.000000} = {100 * rmsRstLate / Math.Max(rmsOn, 1e-9f):0.0}% of playing; control without reset {100 * rmsCtlLate / Math.Max(rmsOn, 1e-9f):0}%; whole 0.25 s window {100 * rmsRst / Math.Max(rmsOn, 1e-9f):0.0}%)");
            VwCheck(rmsAgain > 0.5f * rmsFresh && rmsAgain < 2f * rmsFresh, $"the reset aircraft sounds like the fresh one (RMS {rmsAgain:0.000000} vs {rmsFresh:0.000000})");
        }
        finally
        {
            for (int i = AudioServer.GetBusEffectCount(bi) - 1; i >= 0; i--) if (AudioServer.GetBusEffect(bi, i) == cap) AudioServer.RemoveBusEffect(bi, i);
        }
        // close: the live scene's own audio players are back as they were
        CloseReplayViewer();
        for (int i = 0; i < 600 && s.State != ReplayViewerState.Closed; i++) yield return null;
        VwCheck(s.State == ReplayViewerState.Closed, "the viewer closed");
        var liveAfter = livePlayers.Select(n => IsInstanceValid(n) ? Paused(n) : false).ToList();
        int diff = 0; for (int i = 0; i < liveBefore.Count; i++) if (liveBefore[i] != liveAfter[i]) diff++;
        VwCheck(diff == 0, $"the live scene's {liveBefore.Count} audio players have the same pause state as before the viewer ({diff} differ)");
        VwCheck(AudioServer.GetBusEffectCount(bi) == 0 || AudioServer.GetBusEffectCount(bi) == effectIndex, "the capture effect was removed from the bus (bus settings as before)");
    }

    static IEnumerable VwWait(double seconds) { ulong t0 = Time.GetTicksMsec(); while ((Time.GetTicksMsec() - t0) / 1000.0 < seconds) yield return null; }
}
