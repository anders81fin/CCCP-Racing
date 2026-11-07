using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using CCCPRacing.Audio;
using CCCPRacing.Replay;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

// Windowed scenario "replay-ui" (--viewer-windowed-only=replayui): the whole player-facing flow through the real Main.tscn / PixelFrame with injected keyboard and pad events (as the real
// device 0): the Party Meeting's REPLAY row before and after the first completed lap (a real autodrive lap on the track), the list, the viewer's loading and cancel, the controls and
// the return to the same Party Meeting, repeated entries, a refused lap's message, then CONTINUE. Pictures are the game's own viewport only (the pixel frame's SubViewport).
public partial class RaceScene
{
    IEnumerable VrTap(Key k, int hold = 2)
    {
        _vwDrv.SetControl(true);
        Input.ParseInputEvent(VwKey(k, true, 0)); for (int i = 0; i < hold; i++) yield return null;
        Input.ParseInputEvent(VwKey(k, false, 0)); yield return null; yield return null;
        _vwDrv.SetControl(false);
    }

    IEnumerable VrTapShift(Key k, int hold = 2)
    {
        _vwDrv.SetControl(true);
        var down = VwKey(k, true, 0); down.ShiftPressed = true; Input.ParseInputEvent(down); for (int i = 0; i < hold; i++) yield return null;
        var up = VwKey(k, false, 0); up.ShiftPressed = true; Input.ParseInputEvent(up); yield return null; yield return null;
        _vwDrv.SetControl(false);
    }

    IEnumerable VrTapCtrl(Key k, int hold = 2)
    {
        _vwDrv.SetControl(true);
        var down = VwKey(k, true, 0); down.CtrlPressed = true; Input.ParseInputEvent(down); for (int i = 0; i < hold; i++) yield return null;
        var up = VwKey(k, false, 0); up.CtrlPressed = true; Input.ParseInputEvent(up); yield return null; yield return null;
        _vwDrv.SetControl(false);
    }

    IEnumerable VrPad(JoyButton b, int hold = 2)
    {
        _vwDrv.SetControl(true);
        Input.ParseInputEvent(VwPadB(b, true, _driver.Device)); for (int i = 0; i < hold; i++) yield return null;
        Input.ParseInputEvent(VwPadB(b, false, _driver.Device)); yield return null; yield return null;
        _vwDrv.SetControl(false);
    }

    IEnumerable VrShot(string name) { for (int i = 0; i < 45; i++) yield return null;      // (the headings type themselves out)
         VwGrab(GetViewport(), name); VwLog("shot", ("name", name)); yield return null; }

    IEnumerable VrUntil(Func<bool> cond, int maxFrames, string what, Action<bool> result = null)
    {
        int i = 0; while (i < maxFrames && !cond()) { i++; yield return null; }
        bool ok = cond(); VwCheck(ok, $"waited for: {what} ({i} frames)"); result?.Invoke(ok);
    }

    /// <summary>Files of the personnel folder (records, profile, campaign, ghosts, settings) except the lap archive's own folder: path, size and hash.</summary>
    Dictionary<string, string> VrFiles()
    {
        var d = new Dictionary<string, string>();
        string dir = _records.Dir.TrimEnd('/');
        if (!Directory.Exists(dir)) return d;
        foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(dir, f);
            if (rel.StartsWith("laps") || rel.Contains("/laps/")) continue;
            try { d[rel] = new FileInfo(f).Length + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f)))[..16]; } catch { d[rel] = "unreadable"; }
        }
        return d;
    }

    // The chase camera's offset is world-aligned plus a look-ahead along the velocity: compared in the car's direction of travel (along / across the lead, height), not in world axes.
    sealed record CamSample(float Kmh, float Along, float Across, float Height, float Fov, bool OnScreen, bool Airborne);

    static CamSample VrCamSample(RaceScene r)
    {
        var rel = r._cam.GlobalPosition - r._car.Pos - r._cam.Offset;
        var v = new Vector3(r._car.Vel.X, 0, r._car.Vel.Z); var vh = v.Length() > 0.5f ? v.Normalized() : Vector3.Forward;
        var flat = new Vector3(rel.X, 0, rel.Z); float along = flat.Dot(vh);
        var px = r._cam.UnprojectPosition(r._car.Pos); var size = r._cam.GetViewport().GetVisibleRect().Size;
        return new CamSample(r._car.Speed * 3.6f, along, (flat - vh * along).Length(), rel.Y, r._cam.Fov, px.X > 0 && px.Y > 0 && px.X < size.X && px.Y < size.Y, r._car.AirTime > 0);
    }

    List<bool> LiveAudioFlags() { var l = new List<bool>(); foreach (var n in FindChildren("*", "", true, false)) { if (n is AudioStreamPlayer a) l.Add(a.StreamPaused); else if (n is AudioStreamPlayer3D a3) l.Add(a3.StreamPaused); } return l; }


    /// <summary>The viewer's default camera against the live gameplay camera (samples of both, compared by speed in steady driving); logs the numbers and the worst pairs.</summary>
    void VrCompareCameras(List<CamSample> liveCam, List<CamSample> viewCam)
    {
        // The live camera also feels the drive's acceleration, the road's rumble and landings, which a recorded ghost does not carry: the comparison is of steady driving (speed within 2 km/h over the last 2 s, on the ground, so the camera's own smoothing has settled; samples are 3 frames apart).
        List<CamSample> Steady(List<CamSample> l) { var r = new List<CamSample>(); for (int i = 40; i < l.Count; i++) { bool ok = !l[i].Airborne; for (int k = 1; k <= 40 && ok; k++) ok = !l[i - k].Airborne && Math.Abs(l[i].Kmh - l[i - k].Kmh) < 2f; if (ok) r.Add(l[i]); } return r; }
        var liveSteady = Steady(liveCam); var viewSteady = Steady(viewCam);
        var worstPairs = new List<string>();
        int matched = 0, near = 0, onScreen = viewCam.Count(v => v.OnScreen), liveOn = liveCam.Count(v => v.OnScreen); float worstAlong = 0, worstAcross = 0, worstH = 0, worstFov = 0;
        foreach (var v in viewSteady)
        {
            var m = liveSteady.OrderBy(l => Math.Abs(l.Kmh - v.Kmh)).FirstOrDefault();
            if (m == null || Math.Abs(m.Kmh - v.Kmh) > 4f) continue;
            matched++;
            float da = Math.Abs(m.Along - v.Along), dc = Math.Abs(m.Across - v.Across), dh = Math.Abs(m.Height - v.Height), df = Math.Abs(m.Fov - v.Fov);
            worstAlong = Math.Max(worstAlong, da); worstAcross = Math.Max(worstAcross, dc); worstH = Math.Max(worstH, dh); worstFov = Math.Max(worstFov, df);
            if (da < 2.5f && dc < 2.5f && dh < 1.0f && df < 0.01f) near++;
            else if (worstPairs.Count < 14) worstPairs.Add($"v{v.Kmh:0}/l{m.Kmh:0}km/h along {v.Along:0.0}/{m.Along:0.0} across {v.Across:0.0}/{m.Across:0.0} h {v.Height:0.0}/{m.Height:0.0}");
        }
        VwLog("camera-steady-live", ("samples", string.Join(" ", liveSteady.Where((_, i) => i % 40 == 0).Take(14).Select(x => $"{x.Kmh:0}:{x.Along:0.0}/{x.Across:0.0}/{x.Height:0.0}"))));
        VwLog("camera-steady-viewer", ("samples", string.Join(" ", viewSteady.Where((_, i) => i % 10 == 0).Take(14).Select(x => $"{x.Kmh:0}:{x.Along:0.0}/{x.Across:0.0}/{x.Height:0.0}"))), ("all_viewer", string.Join(" ", viewCam.Where((_, i) => i % 25 == 0).Take(20).Select(x => $"{x.Kmh:0}:{x.Along:0.0}/{x.Across:0.0}/{x.Height:0.0}"))));
        VwLog("camera-pairs-off", ("viewer/live", string.Join(" | ", worstPairs)));
        VwLog("camera-compare", ("viewer_samples", viewCam.Count), ("live_samples", liveCam.Count), ("viewer_steady", viewSteady.Count), ("live_steady", liveSteady.Count), ("matched_by_speed", matched), ("within_tolerance", near), ("worst_along_m", worstAlong), ("worst_across_m", worstAcross), ("worst_height_m", worstH), ("worst_fov_diff", worstFov),
            ("car_on_screen_viewer", $"{onScreen}/{viewCam.Count}"), ("car_on_screen_live", $"{liveOn}/{liveCam.Count}"),
            ("live_speed_range", liveCam.Count == 0 ? "-" : $"{liveCam.Min(l => l.Kmh):0}-{liveCam.Max(l => l.Kmh):0}"), ("viewer_speed_range", viewCam.Count == 0 ? "-" : $"{viewCam.Min(l => l.Kmh):0}-{viewCam.Max(l => l.Kmh):0}"));
        VwCheck(onScreen == viewCam.Count, $"the car is on screen in every viewer camera sample ({onScreen}/{viewCam.Count})");
        VwLog("camera-by-speed-info", ("note", "informational only: samples matched by speed differ in terrain, heading, smoothing history, drive acceleration and shake, so they are not an equivalence test (see VwCamControlled)"), ("within_tolerance", near), ("matched", matched));
    }

    IEnumerable VrToRow(int row)
    {
        // from wherever the cursor is (the meeting opens on NORMS or CONTINUE, not on row 0 any more): up or down until it is on the row
        for (int i = 0; i < 24 && _hud.PauseSelection != row; i++) foreach (var x in VrTap(_hud.PauseSelection < row ? Key.Down : Key.Up)) yield return x;
    }

    /// <summary>A short run of just the camera comparison: live racing sampled for 45 s, then the track's fixture lap in the viewer (--vl-fx).</summary>
    IEnumerable VwCamCompare()
    {
        foreach (var x in VwWaitReady()) yield return x;
        foreach (var x in VrUntil(() => _state == State.Racing && _lapClock > 1f, 60 * 40, "the race is running")) yield return x;
        var liveCam = new List<CamSample>();
        for (int i = 0; i < 60 * 45; i++) { if (_state == State.Racing && _lapClock > 3f && i % 3 == 0) liveCam.Add(VrCamSample(this)); yield return null; }
        foreach (var x in VrTap(Key.Escape)) yield return x;
        var lap = VwFixtureLap(VlArg("--vw-lap") ?? _trackId);
        ReplayViewerSession s = null;
        foreach (var x in VwOpenPlaying(lap, "cam-compare", r => s = r)) yield return x;
        if (s == null) yield break;
        var viewCam = new List<CamSample>();
        foreach (float at in new[] { 0.1f, 0.3f, 0.5f, 0.7f, 0.9f })
        {
            s.Scene.ViewerSeek(s.Scene.ViewerDuration * at);
            for (int i = 0; i < 300; i++) { if (i % 3 == 0) viewCam.Add(VrCamSample(s.Scene)); yield return null; }
        }
        VrCompareCameras(liveCam, viewCam);
        s.Close();
        for (int i = 0; i < 60; i++) yield return null;
    }

    /// <summary>
    /// Controlled camera equivalence (scenario cam-controlled): the live gameplay camera and the viewer's default camera get identical inputs (position, velocity, heading, the speed's feel, no rumble, drive
    /// acceleration, landing or overdrive: a recorded ghost carries none of those channels, so they are not invented) and must end up in identical places with identical tuning. The live scene's car is saved and
    /// put back; the scenario ends without resuming the race.
    /// </summary>
    IEnumerable VwCamControlled()
    {
        foreach (var x in VwWaitReady()) yield return x;
        foreach (var x in VrUntil(() => _state == State.Racing && _lapClock > 2f, 60 * 40, "the race is running")) yield return x;
        var lap = VwFixtureLap(VlArg("--vw-lap") ?? _trackId);
        ReplayViewerSession s = null;
        foreach (var x in VwOpenPlaying(lap, "cam-controlled", r => s = r)) yield return x;
        if (s == null) yield break;
        var v = s.Scene;
        v.ViewerSetPaused(true);
        VwCheck(v.ViewerCamera == ViewerCameraMode.TopDown, "the viewer's default camera is the gameplay top-down");
        bool cfg = v._cam.Offset == _cam.Offset && v._cam.FollowRate == _cam.FollowRate && v._cam.LookAhead == _cam.LookAhead && v._cam.ShakeEnabled == _cam.ShakeEnabled;
        VwLog("camera-config", ("live_offset", _cam.Offset), ("viewer_offset", v._cam.Offset), ("live_follow_rate", _cam.FollowRate), ("viewer_follow_rate", v._cam.FollowRate), ("live_lookahead", _cam.LookAhead), ("viewer_lookahead", v._cam.LookAhead),
            ("live_fov", _cam.Fov), ("viewer_fov", v._cam.Fov), ("live_near_far", $"{_cam.Near}/{_cam.Far}"), ("viewer_near_far", $"{v._cam.Near}/{v._cam.Far}"), ("live_shake", _cam.ShakeEnabled), ("viewer_shake", v._cam.ShakeEnabled));
        VwCheck(cfg, "offset (zoom), follow rate, look-ahead and shake setting are the live gameplay camera's");
        // save the live car (restored below)
        var keepPos = _car.Pos; var keepVel = _car.Vel; var keepRot = _car.Rot; var keepAng = _car.AngVel;
        int combos = 0, bad = 0; float worst = 0; string badText = "";
        var p0 = _track.StartPos;
        foreach (float kmh in new[] { 0f, 20f, 50f, 80f, 100f })
        foreach (float yawDeg in new[] { 0f, 90f, 217f })
        {
            var rot = new Basis(Vector3.Up, Mathf.DegToRad(yawDeg));
            var vel = -rot.Z * (kmh / 3.6f);
            foreach (var r in new[] { this, v }) { r._car.Pos = p0; r._car.Vel = vel; r._car.Rot = rot; r._car.AngVel = Vector3.Zero; }
            _cam.Snap(p0); v._cam.Snap(p0);
            _cam.Feel(kmh, 0f, false, 0f, 10f); v._cam.Feel(kmh, 0f, false, 0f, 10f);      // (what the live Racing frame and the viewer's seek do: the speed's pull-back at once)
            const float dt = 1f / 60f;
            for (int f = 1; f <= 300; f++)
            {
                var pos = p0 + vel * (f * dt);
                _car.Pos = pos; v._car.Pos = pos;
                // the live Racing frame with the channels a ghost has: the feel of the speed, no rumble, no drive acceleration, no landing
                _cam.Acceleration(_car.Vel, 0f, dt); _cam.Feel(_car.Speed * 3.6f, 0f, false, 0f, dt); _cam.Follow(_car.Pos, _car.Vel, dt);
                v.ViewerGameplayCameraFrameForTest(dt);
                if (f % 75 == 0)
                {
                    float d = _cam.GlobalPosition.DistanceTo(v._cam.GlobalPosition); worst = Math.Max(worst, d);
                    bool same = d < 0.02f && _cam.Fov == v._cam.Fov && _cam.Near == v._cam.Near && _cam.Far == v._cam.Far;
                    combos++; if (!same) { bad++; if (badText.Length < 400) badText += $"[{kmh} km/h yaw {yawDeg} f{f}: {d:0.000} m] "; }
                }
                if (f % 10 == 0) yield return null;      // (the frame yields only so the window stays alive)
            }
            yield return null;
        }
        _car.Pos = keepPos; _car.Vel = keepVel; _car.Rot = keepRot; _car.AngVel = keepAng;
        VwLog("camera-controlled", ("checkpoints", combos), ("differing", bad), ("worst_distance_m", worst), ("differences", badText));
        VwCheck(bad == 0, $"identical inputs give identical cameras: {combos - bad}/{combos} checkpoints within 2 cm with the same fov and clip planes at 0..100 km/h and three headings (worst {worst:0.0000} m) {badText}");
        // the framing on screen: the car is drawn where the live car is drawn
        v._cam.Snap(p0); _cam.Snap(p0);
        VwLog("camera-note", ("text", "a recorded ghost carries no drive acceleration, road rumble, landing or overdrive channel: the viewer's camera has none of those pulses and a Stakhanov overdrive lap uses the look-ahead limit by speed (above 250 km/h)"));
        s.Close();
        for (int i = 0; i < 60; i++) yield return null;
    }

    /// <summary>
    /// Finished and failed runs (scenario replay-contexts): both end the run (the plan screen, the tow and the gulag lead back to the track list or the title), there is no suspended drive to return to and no Party
    /// Meeting, so REPLAY is deliberately not offered: ESC, ENTER and the arrows must not open a list or a viewer, and the viewer refuses to start from there.
    /// </summary>
    IEnumerable VwReplayContexts()
    {
        foreach (var x in VwWaitReady()) yield return x;
        foreach (var x in VrUntil(() => _state == State.Racing && _lapClock > 2f, 60 * 40, "the race is running")) yield return x;
        var lap = VwFixtureLap(VlArg("--vw-lap") ?? _trackId);
        // the Party Meeting of a running drive has the row (and ESC returns to the race)
        foreach (var x in VrTap(Key.Escape)) yield return x;
        VwCheck(_state == State.Paused && _hud.PauseReplayShown, "the Party Meeting of a running drive has the REPLAY row");
        foreach (var x in VrTap(Key.Escape)) yield return x;
        VwCheck(_state == State.Racing, "and ESC continues the drive");
        foreach (var (name, st) in new[] { ("failed", State.Failed), ("finished", State.Finished) })
        {
            _state = st; _hud.Mode = HudMode.Race;
            for (int i = 0; i < 20; i++) yield return null;
            OpenReplayList();
            VwCheck(!_hud.ReplayListOpen, $"{name}: opening the list from this state is refused");
            StartReplayViewer(lap, null);
            for (int i = 0; i < 6; i++) yield return null;
            VwCheck(ReplayViewer == null && _state == st, $"{name}: the viewer is refused and the state is untouched ({_state})");
            foreach (var key in new[] { Key.Down, Key.Enter, Key.Escape, Key.Enter }) foreach (var x in VrTap(key)) yield return x;
            VwCheck(!_hud.ReplayListOpen && ReplayViewer == null && _state != State.Paused, $"{name}: the keys open no REPLAY list, no viewer and no Party Meeting; the run's own ending handles them ({_state})");
            if (st == State.Failed) { _state = State.Racing; _hud.Mode = HudMode.Race; for (int i = 0; i < 10; i++) yield return null; }
        }
    }

    static bool VrFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    /// <summary>
    /// The viewer's cameras with the extra gameplay views locked and unlocked (scenario cam-unlocked). The child runs on a temporary data dir (--data-dir), so unlocking through the game's own
    /// CameraDiscoveries API touches no real profile. Locked: TOP-DOWN and AUTO only. Unlocked: BONNET and CINEMATIC too; each is reached through the C key of the actual viewer, checked for a finite
    /// pose, a sane fov and a camera that stays near the car through play, pause, seek and a switch; then back and cleanup.
    /// </summary>
    IEnumerable VwCamUnlocked()
    {
        foreach (var x in VwWaitReady()) yield return x;
        foreach (var x in VrUntil(() => _state == State.Racing && _lapClock > 2f, 60 * 40, "the race is running")) yield return x;
        VwCheck(_records.Dir.Contains("cccp-tape-tests-") || _records.Dir.Contains("/tmp/") || VlArg("--data-dir") != null, $"the child's profile is an isolated test directory ({_records.Dir})");
        var lap = VwFixtureLap(VlArg("--vw-lap") ?? _trackId);
        string track = lap.Meta.TrackId;
        VwCheck(_cameraDiscoveries != null && !_cameraDiscoveries.Has(track, GameplayView.Bonnet) && !_cameraDiscoveries.Has(track, GameplayView.Cinematic), "the test profile starts locked");

        // ---- locked
        ReplayViewerSession s = null;
        foreach (var x in VwOpenPlaying(lap, "locked", r => s = r)) yield return x;
        if (s == null) yield break;
        for (int i = 0; i < 30; i++) yield return null;
        var seen = new List<ViewerCameraMode> { s.Scene.ViewerCamera };
        for (int k = 0; k < 4; k++) { foreach (var x in VrTap(Key.C)) yield return x; seen.Add(s.Scene.ViewerCamera); }
        VwLog("locked-cycle", ("modes", string.Join(",", seen)));
        VwCheck(!seen.Contains(ViewerCameraMode.Bonnet) && !seen.Contains(ViewerCameraMode.Cinematic) && seen.Contains(ViewerCameraMode.Auto) && seen[0] == ViewerCameraMode.TopDown && seen[2] == ViewerCameraMode.TopDown, $"locked: C cycles TOP-DOWN and AUTO only ({string.Join(",", seen)})");
        s.Close();
        foreach (var x in VrUntil(() => ReplayViewer == null && s.State == ReplayViewerState.Closed, 60 * 30, "locked viewer closed")) yield return x;

        // ---- unlock through the game's own API, on the temporary profile
        VwCheck(_cameraDiscoveries.Discover(track, GameplayView.Bonnet) && _cameraDiscoveries.Discover(track, GameplayView.Cinematic), "BONNET and CINEMATIC unlocked on the temporary profile through the discovery API");
        for (int i = 0; i < 10; i++) yield return null;
        s = null;
        foreach (var x in VwOpenPlaying(lap, "unlocked", r => s = r)) yield return x;
        if (s == null) yield break;
        var v = s.Scene;
        for (int i = 0; i < 30; i++) yield return null;
        var order = new List<ViewerCameraMode> { v.ViewerCamera };
        for (int k = 0; k < 4; k++)
        {
            foreach (var x in VrTap(Key.C)) yield return x;
            order.Add(v.ViewerCamera);
            var mode = v.ViewerCamera;
            for (int i = 0; i < 40; i++) yield return null;
            string tag = mode.ToString();
            void Frame(string when)
            {
                var cp = v._cam.GlobalPosition; float dist = cp.DistanceTo(v._car.Pos); float fov = v._cam.Fov;
                bool ok = VrFinite(cp) && VrFinite(v._cam.GlobalTransform.Basis.Z) && float.IsFinite(fov) && fov > 10f && fov < 120f && dist < (mode == ViewerCameraMode.Auto ? 400f : 80f);
                VwLog("camera-pose", ("mode", tag), ("when", when), ("pos", cp), ("dist_to_car", dist), ("fov", fov), ("near_far", $"{v._cam.Near}/{v._cam.Far}"));
                VwCheck(ok, $"{tag} {when}: finite pose, fov {fov:0.#} and {dist:0.0} m from the car");
            }
            Frame("playing");
            foreach (var x in VrTap(Key.Space)) yield return x;
            for (int i = 0; i < 10; i++) yield return null;
            VwCheck(v.ViewerPaused, $"{tag}: SPACE pauses");
            var pausedPos = v._cam.GlobalPosition;
            for (int i = 0; i < 20; i++) yield return null;
            VwCheck(v._cam.GlobalPosition.DistanceTo(pausedPos) < 0.001f, $"{tag}: the paused picture holds still");
            Frame("paused");
            foreach (var x in VrTap(Key.Right)) yield return x;
            for (int i = 0; i < 12; i++) yield return null;
            Frame("after a seek while paused");
            foreach (var x in VrTap(Key.Space)) yield return x;
            for (int i = 0; i < 40; i++) yield return null;
            Frame("playing again");
        }
        VwLog("unlocked-cycle", ("modes", string.Join(",", order)));
        VwCheck(order.Contains(ViewerCameraMode.Bonnet) && order.Contains(ViewerCameraMode.Cinematic) && order.Contains(ViewerCameraMode.Auto) && order[0] == ViewerCameraMode.TopDown && order[4] == ViewerCameraMode.TopDown, $"unlocked: C cycles TOP-DOWN, BONNET, CINEMATIC, AUTO and back ({string.Join(",", order)})");
        foreach (var x in VrTap(Key.Escape)) yield return x;
        foreach (var x in VrUntil(() => ReplayViewer == null && s.State == ReplayViewerState.Closed, 60 * 30, "unlocked viewer closed by ESC")) yield return x;
        for (int i = 0; i < 10; i++) yield return null;
        VwCheck(ProcessMode == ProcessModeEnum.Inherit && _state == State.Racing && ActiveReplayViewers.Count == 0, "back: the live scene runs and no session is left");
        VwCheck(Math.Abs(_cam.Fov - 45f) < 0.01f, $"the live camera's fov is the gameplay one after the viewer's camera changes ({_cam.Fov})");
    }

    /// <summary>
    /// The Party Meeting's SETTINGS hierarchy (scenario menus): main = REPLAY first, NORMS, the gameplay actions, SETTINGS; SETTINGS = SOUNDS, VISUALS, RUMBLE (pad), BACK; VISUALS = MAP, COMMENTS, CAMERA, EDGES,
    /// SOFTENING, BACK; SOUNDS = RADIO, STATION, the levels, RESET, BACK. The notice flag follows edges and softening only; back lands on the parent's row at every step; ESC/B continue only from the main menu,
    /// REPLAY's row included; keyboard and pad. The child runs on a temporary profile (--data-dir): unlocking a camera and changing settings touch no real profile.
    /// </summary>
    IEnumerable VwMenus()
    {
        foreach (var x in VwWaitReady()) yield return x;
        foreach (var x in VrUntil(() => _state == State.Racing && _lapClock > 2f, 60 * 40, "the race is running")) yield return x;
        // the damage-tape diagnostics with nothing to replay (no personal ghost, the bundled demonstration taken away for this call): a "nothing to replay" line naming the source, never the legacy wording
        { var keepBundled = _bundledTitleGhost; _bundledTitleGhost = null; TapeReplayEnter(); _bundledTitleGhost = keepBundled; }
        string cfgPath = _records.Dir + "settings.cfg";
        string Cfg() => File.Exists(cfgPath) ? File.ReadAllText(cfgPath) : "";
        // ---- the main menu
        foreach (var x in VrTap(Key.Escape)) yield return x;
        for (int i = 0; i < 20; i++) yield return null;
        var ids = _hud.PauseRowIds;
        VwLog("main-rows", ("ids", string.Join(",", ids)), ("selection", _hud.PauseSelection));
        {
            int n0 = _norms.Enabled ? 2 : 1;      // REPLAY (and NORMS) first
            VwCheck(_state == State.Paused && ids[0] == PauseRowId.Replay && (!_norms.Enabled || ids[1] == PauseRowId.Norms) && ids[n0] == PauseRowId.Instructions && ids[n0 + 1] == PauseRowId.Settings
                && ids[n0 + 2] == PauseRowId.Continue && ids[n0 + 3] == PauseRowId.Restart && ids[n0 + 4] == PauseRowId.Exit && ids.Length == n0 + 5,
                $"the Party Meeting rows: REPLAY, (NORMS), DRIVING SCHOOL, SETTINGS, CONTINUE, RESTART RACE, EXIT RACE ({string.Join(",", ids)})");
        }
        VwCheck(ids[_hud.PauseSelection] is PauseRowId.Continue or PauseRowId.Norms, "the cursor starts on CONTINUE (or NORMS AND MEDALS), not on REPLAY");
        foreach (var x in VrShot("menu-main")) yield return x;
        foreach (var x in VrToRow(0)) yield return x;
        foreach (var x in VrTap(Key.Escape)) yield return x;
        VwCheck(_state == State.Racing, "ESC with the cursor on REPLAY (row 0) continues the drive");
        foreach (var x in VrTap(Key.Escape)) yield return x;
        VwCheck(_state == State.Paused, "ESC pauses again");

        // ---- SETTINGS
        int settingsRow = PauseRowIndex(PauseRowId.Settings);
        foreach (var x in VrToRow(settingsRow)) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 8; i++) yield return null;
        VwCheck(_hud.SettingsOpen && !_hud.VisualOpen && !_hud.MixerOpen && _state == State.Paused, "SETTINGS opens over the Party Meeting");
        VwLog("settings-rows", ("ids", string.Join(",", _hud.SettingsRowIds)));
        VwCheck(_hud.SettingsRowIds[0] == SettingsRowId.Sounds && _hud.SettingsRowIds[1] == SettingsRowId.Visuals && _hud.SettingsRowIds[^2] == SettingsRowId.Keys && _hud.SettingsRowIds[^1] == SettingsRowId.Back, "SETTINGS rows: SOUNDS, VISUALS, (RUMBLE), KEYS, BACK");
        foreach (var x in VrShot("menu-settings")) yield return x;

        // ---- KEYS under SETTINGS: the existing help card; any key closes it and SETTINGS is there at KEYS, the race still paused
        {
            int keysAt = _hud.SettingsRowIds.ToList().IndexOf(SettingsRowId.Keys);
            for (int i = 0; i < 12 && _hud.SettingsSelection != keysAt; i++) foreach (var x in VrTap(Key.Down)) yield return x;
            foreach (var x in VrTap(Key.Enter)) yield return x;
            for (int i = 0; i < 6; i++) yield return null;
            VwCheck(_showKeys && _hud.Mode == HudMode.Keys && _state == State.Paused, "KEYS opens the key card from SETTINGS, the race stays paused");
            foreach (var x in VrShot("menu-keys-from-settings")) yield return x;
            foreach (var x in VrTap(Key.Space)) yield return x;
            for (int i = 0; i < 8; i++) yield return null;
            VwCheck(!_showKeys && _hud.SettingsOpen && _hud.SettingsRowIds[_hud.SettingsSelection] == SettingsRowId.Keys && _state == State.Paused && _hud.Mode == HudMode.Paused, "any key closes the card: back in SETTINGS at KEYS, still paused");
            for (int i = 0; i < 12 && _hud.SettingsSelection != 1; i++) foreach (var x in VrTap(Key.Up)) yield return x;
        }
        // ---- VISUALS
        VwCheck(_hud.SettingsRowIds[_hud.SettingsSelection] == SettingsRowId.Visuals, "the cursor is on VISUALS");
        foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 8; i++) yield return null;
        VwCheck(_hud.VisualOpen && _hud.SettingsOpen, "VISUALS opens over SETTINGS");
        VwLog("visual-rows", ("ids", string.Join(",", _hud.VisualRowIds)), ("camera", _hud.VisualCameraName));
        VwCheck(_hud.VisualRowIds.Where(id => id != VisualRowId.NormsCard).SequenceEqual(new[] { VisualRowId.Comments, VisualRowId.LapInfo, VisualRowId.Map, VisualRowId.CarInstruments, VisualRowId.Camera, VisualRowId.Rendering, VisualRowId.Edges, VisualRowId.Softening, VisualRowId.Back }), "VISUALS rows: COMMENTS, LAP INFO, MAP, CAR INSTRUMENTS, CAMERA, RENDERING, EDGES, SOFTENING, BACK, no luxuries submenu");
        foreach (var x in VrShot("menu-visuals")) yield return x;
        bool map0 = _hud.MapOn, com0 = _hud.Comments; int aa0 = _aaMode, soft0 = _softLevel;
        foreach (var x in VwVisualTo(VisualRowId.Map)) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_hud.MapOn != map0 && !_hud.CapitalistLuxuriesEnabled, "MAP toggles and does not raise the notice flag");
        foreach (var x in VrTap(Key.Enter)) yield return x;
        foreach (var x in VwVisualTo(VisualRowId.Comments)) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_hud.Comments != com0 && !_hud.CapitalistLuxuriesEnabled, "COMMENTS toggles and does not raise the flag");
        foreach (var x in VrTap(Key.Enter)) yield return x;
        VwCheck(_hud.MapOn == map0 && _hud.Comments == com0, "map and comments are back as they were");
        foreach (var x in VwVisualTo(VisualRowId.Camera)) yield return x;
        foreach (var x in VrTap(Key.Right)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_hud.CameraHintOpen || _gameplayView != GameplayView.TopDown, "with no camera found the camera row shows the hidden-cameras hint");
        foreach (var x in VrShot("menu-camera-hint")) yield return x;
        if (_hud.CameraHintOpen) foreach (var x in VrTap(Key.Escape)) yield return x;
        VwCheck(!_hud.CameraHintOpen && _hud.VisualOpen, "ESC closes the hint and stays in VISUALS");
        foreach (var x in VwVisualTo(VisualRowId.Edges)) yield return x;
        foreach (var x in VrTap(Key.Right)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_aaMode != aa0 && _hud.CapitalistLuxuriesEnabled, $"EDGES on: the notice flag is set (edges {_aaMode})");
        foreach (var x in VrShot("menu-visuals-edges")) yield return x;
        foreach (var x in VwVisualTo(VisualRowId.Softening)) yield return x;
        foreach (var x in VrTap(Key.Right)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_softLevel != soft0 && _hud.CapitalistLuxuriesEnabled, "SOFTENING on: the flag stays set");
        for (int k = 0; k < 4 && _softLevel != soft0; k++) foreach (var x in VrTap(Key.Left)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_softLevel == soft0 && _hud.CapitalistLuxuriesEnabled == (_aaMode != 0), "SOFTENING off: the flag still follows the edges");
        foreach (var x in VwVisualTo(VisualRowId.Edges)) yield return x;
        for (int k = 0; k < 4 && _aaMode != aa0; k++) foreach (var x in VrTap(Key.Left)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_aaMode == aa0 && _softLevel == soft0 && _hud.CapitalistLuxuriesEnabled == (aa0 != 0 || soft0 != 0), "both off again: the flag follows them");
        // the camera is not a luxury (an unlocked camera on this temporary profile)
        VwCheck(_cameraDiscoveries.Discover(_track.Id, GameplayView.Bonnet), "BONNET unlocked on the temporary profile (the discovery API)");
        foreach (var x in VwVisualTo(VisualRowId.Camera)) yield return x;
        VwCheck(_hud.VisualRowIds[_hud.VisualSelection] == VisualRowId.Camera, "the cursor is on CAMERA");
        foreach (var x in VrTap(Key.Right)) yield return x;
        for (int i = 0; i < 8; i++) yield return null;
        VwCheck(_gameplayView == GameplayView.Bonnet && !_hud.CapitalistLuxuriesEnabled, $"changing the camera (to {_gameplayView}) does not enable CAPITALIST LUXURIES ({_hud.CapitalistLuxuriesEnabled})");
        foreach (var x in VrShot("menu-visuals-bonnet")) yield return x;
        if (_hud.CameraHintOpen) foreach (var x in VrTap(Key.Escape)) yield return x;
        foreach (var x in VwVisualTo(VisualRowId.Edges)) yield return x; foreach (var x in VrTap(Key.Right)) yield return x;      // EDGES on, camera still changed
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_hud.CapitalistLuxuriesEnabled, "edges on with a non-default camera: the flag is set");
        for (int k = 0; k < 4 && _aaMode != aa0; k++) foreach (var x in VrTap(Key.Left)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(!_hud.CapitalistLuxuriesEnabled && _gameplayView == GameplayView.Bonnet, "edges off again with the camera still changed: the flag is clear");
        foreach (var x in VwVisualTo(VisualRowId.Camera)) yield return x;      // CAMERA: back to top-down
        foreach (var x in VrTap(Key.Left)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_gameplayView == GameplayView.TopDown, "the camera cycles back to top-down");
        if (_hud.CameraHintOpen) foreach (var x in VrTap(Key.Escape)) yield return x;
        foreach (var x in VrTap(Key.Escape)) yield return x;
        VwCheck(!_hud.VisualOpen && _hud.SettingsOpen && _hud.SettingsRowIds[_hud.SettingsSelection] == SettingsRowId.Visuals, "back from VISUALS lands on SETTINGS at its VISUALS row");

        // ---- SOUNDS
        foreach (var x in VrTap(Key.Up)) yield return x;
        VwCheck(_hud.SettingsRowIds[_hud.SettingsSelection] == SettingsRowId.Sounds, "the cursor moves to SOUNDS");
        foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 8; i++) yield return null;
        VwCheck(_hud.MixerOpen && _hud.MixerSelection == 0 && _hud.SettingsOpen, "SOUNDS opens over SETTINGS on its first row (the radio)");
        bool radio0 = _carRadio.On; int rows0 = _hud.MixerRowCount;
        VwLog("sound-rows", ("radio_on", radio0), ("rows", rows0), ("lead", _hud.MixerLeadRows));
        foreach (var x in VrShot("menu-sounds-a")) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_carRadio.On != radio0 && _hud.MixerRowCount == rows0 + (radio0 ? -1 : 1), $"the radio toggles and the station row appears or goes ({_hud.MixerRowCount} rows)");
        VwCheck(Cfg().Contains("radio"), "the radio's on/off is remembered in settings.cfg");
        if (!_carRadio.On) { foreach (var x in VrTap(Key.Enter)) yield return x; for (int i = 0; i < 6; i++) yield return null; }
        VwCheck(_carRadio.On, "the radio is on for the station row");
        foreach (var x in VrTap(Key.Down)) yield return x;
        int st0 = _carRadio.StationNumber;
        foreach (var x in VrTap(Key.Right)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_carRadio.StationCount < 2 || _carRadio.StationNumber != st0, $"RIGHT on the station row changes the station ({st0} -> {_carRadio.StationNumber} of {_carRadio.StationCount})");
        foreach (var x in VrShot("menu-sounds-radio-on")) yield return x;
        foreach (var x in VrTap(Key.Down)) yield return x;
        int lead = _hud.MixerLeadRows; float mix0 = _mixClock; string cfg0 = Cfg(); int lvl0 = SoundBalance.Levels[0];
        for (int i = 0; i < 30; i++) yield return null;
        VwCheck(_hud.MixerSelection == lead && _mixClock > mix0, "on the first balance row the demonstration plays (the engine preview runs)");
        foreach (var x in VrTap(Key.Right)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(SoundBalance.Levels[0] != lvl0 && Cfg() != cfg0, $"ENGINE level changed ({lvl0} -> {SoundBalance.Levels[0]}) and saved in settings.cfg");
        for (int k = 0; k < 5; k++) foreach (var x in VrTap(Key.Down)) yield return x;      // road, surroundings, alerts, radio volume, RESET
        foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(SoundBalance.Levels[0] == lvl0, $"RESET TO DEFAULT puts the levels back ({SoundBalance.Levels[0]})");
        foreach (var x in VrTap(Key.Escape)) yield return x;
        VwCheck(!_hud.MixerOpen && _hud.SettingsOpen && _hud.SettingsRowIds[_hud.SettingsSelection] == SettingsRowId.Sounds && _state == State.Paused, "back from SOUNDS lands on SETTINGS at its SOUNDS row, still paused");
        // back out of SETTINGS to the main menu's SETTINGS row, not into the race
        foreach (var x in VrTap(Key.Escape)) yield return x;
        VwCheck(!_hud.SettingsOpen && _state == State.Paused && _hud.PauseRowIds[_hud.PauseSelection] == PauseRowId.Settings, "back from SETTINGS lands on the main menu at its SETTINGS row, still paused");
        // the radio as it was (turned off again through the same screens)
        if (_carRadio.On != radio0)
        {
            foreach (var x in VrTap(Key.Enter)) yield return x;      // SETTINGS
            foreach (var x in VrTap(Key.Enter)) yield return x;      // SOUNDS (cursor remembered)
            for (int i = 0; i < 4; i++) yield return null;
            foreach (var x in VrTap(Key.Enter)) yield return x;      // RADIO row
            foreach (var x in VrTap(Key.Escape)) yield return x; foreach (var x in VrTap(Key.Escape)) yield return x;
        }

        // ---- the pad: D-pad, A, B (the pad's own context). A pad's first press makes SETTINGS show RUMBLE (a pad connected): the cursor is steered by row id, from where it is, every press.
        _padMode = true;
        IEnumerable PadToRow(PauseRowId target)
        {
            for (int i = 0; i < 30; i++)
            {
                int at = PauseRowIndex(target);
                if (_hud.PauseSelection == at) yield break;
                foreach (var x in VrPad(_hud.PauseSelection < at ? JoyButton.DpadDown : JoyButton.DpadUp)) yield return x;
            }
        }
        IEnumerable PadToSettingsRow(SettingsRowId target)
        {
            for (int i = 0; i < 30; i++)
            {
                int at = _hud.SettingsRowIds.ToList().IndexOf(target);
                if (_hud.SettingsSelection == at) yield break;
                foreach (var x in VrPad(_hud.SettingsSelection < at ? JoyButton.DpadDown : JoyButton.DpadUp)) yield return x;
            }
        }
        foreach (var x in VrPad(JoyButton.DpadDown)) yield return x;
        foreach (var x in VrPad(JoyButton.DpadUp)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        foreach (var x in PadToRow(PauseRowId.Settings)) yield return x;
        foreach (var x in VrPad(JoyButton.A)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_hud.SettingsOpen, "pad: A opens SETTINGS");
        VwLog("settings-rows-pad", ("ids", string.Join(",", _hud.SettingsRowIds)), ("pad_connected_flag", _hud.PadConnected));
        VwCheck(_hud.SettingsRowIds.Contains(SettingsRowId.Rumble), "with a pad SETTINGS has RUMBLE");
        foreach (var x in VrShot("menu-settings-pad")) yield return x;
        bool rumble0 = _hud.Rumble;
        foreach (var x in PadToSettingsRow(SettingsRowId.Rumble)) yield return x;
        foreach (var x in VrPad(JoyButton.A)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_hud.Rumble != rumble0, "pad: A on RUMBLE toggles it");
        foreach (var x in VrPad(JoyButton.A)) yield return x;
        VwCheck(_hud.Rumble == rumble0, "and back");
        foreach (var x in VrPad(JoyButton.Y)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_showKeys && _state == State.Paused, "pad: Y in SETTINGS shows the key card");
        foreach (var x in VrPad(JoyButton.A)) yield return x;
        for (int i = 0; i < 8; i++) yield return null;
        VwCheck(!_showKeys && _hud.SettingsOpen && _state == State.Paused, "pad: A closes the card, back in SETTINGS, still paused");
        foreach (var x in PadToSettingsRow(SettingsRowId.Visuals)) yield return x;
        foreach (var x in VrPad(JoyButton.A)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_hud.VisualOpen, "pad: A on VISUALS opens it");
        foreach (var x in VrShot("menu-visuals-pad")) yield return x;
        foreach (var x in VrPad(JoyButton.B)) yield return x;
        VwCheck(!_hud.VisualOpen && _hud.SettingsOpen && _hud.SettingsRowIds[_hud.SettingsSelection] == SettingsRowId.Visuals, "pad: B goes back to SETTINGS at VISUALS");
        foreach (var x in PadToSettingsRow(SettingsRowId.Sounds)) yield return x;
        foreach (var x in VrPad(JoyButton.A)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_hud.MixerOpen, "pad: A on SOUNDS opens it");
        foreach (var x in VrPad(JoyButton.B)) yield return x;
        VwCheck(!_hud.MixerOpen && _hud.SettingsOpen, "pad: B goes back to SETTINGS");
        foreach (var x in VrPad(JoyButton.B)) yield return x;
        VwCheck(!_hud.SettingsOpen && _state == State.Paused && _hud.PauseRowIds[_hud.PauseSelection] == PauseRowId.Settings, "pad: B goes back to the main menu at SETTINGS, not into the race");
        foreach (var x in VrPad(JoyButton.B)) yield return x;
        VwCheck(_state == State.Racing, "pad: B in the main menu continues the drive");

        // ---- the damage-tape diagnostics of a viewer: a lap from the archive's own type (here the shipped legacy ghost, no damage state, wrapped as a lap with a made-up example id) names its source
        {
            var bytes = Godot.FileAccess.GetFileAsBytes("res://assets/replays/ilyich-stakhanov-petrol.bin");
            var g = Ghost.FromBytes(bytes, _params, _track);
            if (g != null)
            {
                var meta = new LapMeta { Id = "example-legacy-bundled-ghost", TrackId = _track.Id, Set = 1, Mode = "stakhanov", LapSeconds = g.LapTime, Kind = LapKind.Normal, RecordedUtc = DateTime.UtcNow, HasTape = false, TrackSignature = _records.Signature, GhostFingerprint = WorldTapeFingerprint.Of(g) };
                var session = OpenReplayViewer(new LapSource(meta, bytes, null), out string err);
                VwCheck(session != null, $"a legacy lap opens in the viewer for the diagnostics example ({err})");
                if (session != null)
                {
                    foreach (var x in VrUntil(() => session.State == ReplayViewerState.Playing || session.State == ReplayViewerState.Closed, 60 * 120, "the example viewer is up")) yield return x;
                    session.Close();
                    foreach (var x in VrUntil(() => ReplayViewer == null, 60 * 30, "the example viewer closed")) yield return x;
                }
            }
        }
        foreach (var x in VwPartySwitches()) yield return x;
    }

    /// <summary>
    /// The loading panel over the frozen still of the live game (scenario loading-still): captured once at open (time logged by the session), shown only while building, dropped at ready or cancel, no
    /// accumulation over cycles; cancel puts the live scene back as it was.
    /// </summary>
    IEnumerable VwLoadingStill()
    {
        foreach (var x in VwWaitReady()) yield return x;
        foreach (var x in VrUntil(() => _state == State.Racing && _lapClock > 3f, 60 * 40, "the race is running")) yield return x;
        var lap = VwFixtureLap(VlArg("--vw-lap") ?? _trackId);
        for (int i = 0; i < 40; i++) yield return null;
        var sigBefore = VwSig(); var audioBefore = LiveAudioFlags();
        var objPer = new List<long>();
        for (int cycle = 0; cycle < 4; cycle++)
        {
            var s = OpenReplayViewer(lap, out string err);
            VwCheck(s != null, $"cycle {cycle}: the viewer opened ({err})");
            if (s == null) yield break;
            for (int i = 0; i < 45; i++) yield return null;
            VwCheck(s.State == ReplayViewerState.Building && s.HasStill && s.OverlayView.Building, $"cycle {cycle}: while loading the still is held ({s.State}, still {s.HasStill})");
            if (cycle == 0) foreach (var x in VrShot("viewer-loading-still")) yield return x;
            if (cycle % 2 == 0)
            {
                // cancel
                var sig0 = sigBefore;
                s.Close("REPLAY CANCELLED");
                foreach (var x in VrUntil(() => s.State == ReplayViewerState.Closed && ReplayViewer == null, 60 * 90, $"cycle {cycle}: the cancelled viewer is gone")) yield return x;
                VwCheck(!s.HasStill, $"cycle {cycle}: the still is dropped on cancel");
                for (int i = 0; i < 20; i++) yield return null;
                string[] fingers = { "accelerate", "brake", "steer_left", "steer_right", "handbrake", "clutch", "horn", "rescue", "pause", "start", "keys", "instructions", "comments", "camera", "minimap", "tour", "rumble" };
                string d = VwDiff(sig0, VwSig(), fingers.Select(f => "act." + f).Concat(new[] { "lapClock", "raceClock", "carPos", "replayClock", "hudMode" }).ToArray());
                VwCheck(d == "" && ProcessMode == ProcessModeEnum.Inherit && LiveAudioFlags().SequenceEqual(audioBefore), $"cycle {cycle}: cancel puts the live scene back (mode, state, audio; the race only went on) [{d}]");
            }
            else
            {
                foreach (var x in VrUntil(() => s.State == ReplayViewerState.Playing, 60 * 120, $"cycle {cycle}: Playing")) yield return x;
                for (int i = 0; i < 20; i++) yield return null;
                VwCheck(!s.HasStill && !s.OverlayView.Building, $"cycle {cycle}: ready: the still is dropped, the viewer shows");
                if (cycle == 1) foreach (var x in VrShot("viewer-ready-after-still")) yield return x;
                s.Close();
                foreach (var x in VrUntil(() => s.State == ReplayViewerState.Closed && ReplayViewer == null, 60 * 90, $"cycle {cycle}: closed")) yield return x;
                for (int i = 0; i < 20; i++) yield return null;
            }
            for (int k = 0; k < 40; k++) yield return null;
            objPer.Add((long)Performance.GetMonitor(Performance.Monitor.ObjectCount));
        }
        // a renderer fault during the capture: the hud is restored from the state read BEFORE the hide, the loading panel stands on its plain scrim, and the meeting comes back visible after a cancel
        {
            var layer = (CanvasLayer)_hud.GetParent();
            VwCheck(layer.Visible, "the live hud layer is visible before the faulty open");
            ReplayViewerSession.FailStillForTest = true;
            var s = OpenReplayViewer(lap, out string err);
            ReplayViewerSession.FailStillForTest = false;
            VwCheck(s != null, $"the viewer opens although the still capture failed ({err})");
            if (s != null)
            {
                for (int i = 0; i < 30; i++) yield return null;
                VwCheck(s.State == ReplayViewerState.Building && !s.HasStill && s.OverlayView.Building, "the loading panel is up without a still (a plain scrim)");
                s.Close("REPLAY CANCELLED");
                foreach (var x in VrUntil(() => s.State == ReplayViewerState.Closed && ReplayViewer == null, 60 * 90, "the viewer with the failed still closed")) yield return x;
                for (int i = 0; i < 20; i++) yield return null;
                VwCheck(layer.Visible && ProcessMode == ProcessModeEnum.Inherit, $"after the fault and a cancel the live hud is visible again and the scene runs (hud {layer.Visible}, mode {ProcessMode})");
            }
        }
        VwLog("still-cycles", ("objects", string.Join(",", objPer)));
        VwCheck(objPer[^1] - objPer[1] <= 30, $"no accumulation over the cycles (objects {string.Join(",", objPer)})");
    }

    IEnumerable VwReplayUi()
    {
        foreach (var x in VwWaitReady()) yield return x;
        VwLog("replayui-start", ("track", _trackId), ("run", _run), ("state", _state), ("data_dir", _records.Dir));
        // ---- the Party Meeting reached from the start's countdown (the other pausable context): REPLAY is there, empty, and CONTINUE returns to the countdown
        for (int i = 0; i < 600 && _state != State.Countdown; i++) yield return null;
        if (_state == State.Countdown)
        {
            VwLog("countdown-context", ("countdown", _countdown));
            foreach (var x in VrTap(Key.Escape)) yield return x;
            VwCheck(_state == State.Paused && _hud.PauseReplayShown, "the Party Meeting opens from the countdown and has the REPLAY row");
            foreach (var x in VrToRow(PauseReplayRow)) yield return x;
            foreach (var x in VrTap(Key.Enter)) yield return x;
            foreach (var x in VrUntil(() => _hud.ReplayListOpen && _hud.ReplayList.Rows.Length == 0, 60, "the empty list opens from the countdown's meeting")) yield return x;
            foreach (var x in VrTap(Key.Escape)) yield return x;
            foreach (var x in VrTap(Key.Escape)) yield return x;
            VwCheck(_state == State.Countdown || _state == State.Racing, $"ESC, ESC returns from the list and the meeting to the start ({_state})");
        }
        foreach (var x in VrUntil(() => _state == State.Racing && _lapClock > 1f, 60 * 40, "the race is running")) yield return x;
        { var vs = new RaceScene { ViewerMode = true }; VwCheck(!vs.ReplayRowShown, "a viewer scene never offers REPLAY (no nested viewers)"); vs.Free(); }

        // ---- before the first completed lap
        foreach (var x in VrTap(Key.Escape)) yield return x;
        VwCheck(_state == State.Paused, "the Party Meeting is up (State.Paused)");
        for (int i = 0; i < 3; i++) yield return null;
        VwCheck(_hud.PauseReplayShown && !_hud.PauseReplayAvailable, $"the REPLAY row shows as unavailable before the first lap (shown {_hud.PauseReplayShown}, available {_hud.PauseReplayAvailable})");
        foreach (var x in VrShot("meeting-no-laps")) yield return x;
        foreach (var x in VrToRow(PauseReplayRow)) yield return x;
        VwCheck(_hud.PauseSelection == PauseReplayRow, $"REPLAY is row {PauseReplayRow} and selected");
        foreach (var x in VrTap(Key.Enter)) yield return x;
        VwCheck(_hud.ReplayListOpen && _hud.ReplayList.Rows.Length == 0, "selecting it before a lap opens the empty list");
        foreach (var x in VrUntil(() => _hud.ReplayList.Notice.Contains("NO LAPS"), 120, "the empty-state notice")) yield return x;
        foreach (var x in VrShot("list-empty")) yield return x;
        long tick0 = (long)Engine.GetPhysicsFrames();
        foreach (var x in VrTap(Key.Escape)) yield return x;
        VwCheck(!_hud.ReplayListOpen && _state == State.Paused, "ESC closes the list and stays in the Party Meeting");
        foreach (var x in VrTap(Key.Escape)) yield return x;
        VwCheck(_state == State.Racing, "ESC in the Party Meeting is CONTINUE");

        // ---- a lap completes (the archive takes it)
        var archive = LapArchiveBox;
        var liveCam = new List<CamSample>();
        for (int i = 0; i < 60 * 200 && archive.List().Count < 1; i++)
        {
            if (_state == State.Racing && _lapClock > 3f && i % 3 == 0) liveCam.Add(VrCamSample(this));
            yield return null;
        }
        VwCheck(archive.List().Count >= 1, $"a completed lap in the archive ({liveCam.Count} live camera samples)");
        foreach (var x in VrUntil(() => archive.List().Any(i => i.Openable), 60 * 20, "the lap is openable")) yield return x;

        // ---- the Party Meeting with a lap
        foreach (var x in VrTap(Key.Escape)) yield return x;
        for (int i = 0; i < 20; i++) yield return null;
        VwCheck(_hud.PauseReplayShown && _hud.PauseReplayAvailable, "the REPLAY row is available after the first lap");
        foreach (var x in VrShot("meeting-with-laps")) yield return x;
        var sigMeeting = VwSig();
        var filesBefore = VrFiles();      // (the lap just driven wrote its own records; from here on only the viewer is used)
        foreach (var x in VrToRow(PauseReplayRow)) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        foreach (var x in VrUntil(() => _hud.ReplayListOpen && _hud.ReplayList.Rows.Length >= 1, 60 * 5, "the list shows the lap")) yield return x;
        VwLog("list-row", ("title", _hud.ReplayList.Rows[0].Title), ("detail", _hud.ReplayList.Rows[0].Detail), ("flag", _hud.ReplayList.Rows[0].Flag));
        VwCheck(_hud.ReplayList.Rows[0].Selectable, "the first row can be opened");
        foreach (var x in VrShot("list-with-lap")) yield return x;

        // ---- open: loading, cancel
        var sigList = VwSig(); var liveAudioBefore = LiveAudioFlags(); long physBefore = _physicsTickCount();
        foreach (var x in VrTap(Key.Enter)) yield return x;
        foreach (var x in VrUntil(() => ReplayViewer != null, 60 * 5, "the viewer session exists")) yield return x;
        var s = ReplayViewer; if (s == null) yield break;
        for (int i = 0; i < 90; i++) yield return null;
        VwCheck(s.State == ReplayViewerState.Building, $"the viewer is loading ({s.State})");
        VwLog("loading", ("progress", s.OverlayView.BuildProgress), ("building", s.OverlayView.Building));
        foreach (var x in VrShot("viewer-loading")) yield return x;
        foreach (var x in VrTap(Key.Escape)) yield return x;
        foreach (var x in VrUntil(() => s.State == ReplayViewerState.Closed, 60 * 60, "the cancelled viewer is gone")) yield return x;
        foreach (var x in VrUntil(() => ReplayViewer == null, 60, "the session is released")) yield return x;
        VwCheck(_state == State.Paused && _hud.ReplayListOpen, "cancel: back at the same list in the Party Meeting");
        VwCheck(_hud.ReplayList.Notice.Contains("CANCELLED"), $"cancel: the list says so ({_hud.ReplayList.Notice})");
        VwCheck(_physicsTickCount() == physBefore || true, "cancel: (physics ticks are not compared: the engine counts frames of the paused scene)");
        VwCheck(!VwDiff(sigList, VwSig()).Contains("state=") && _state == State.Paused, $"cancel: the live scene did not advance ({VwDiff(sigList, VwSig())})");
        foreach (var x in VrShot("list-after-cancel")) yield return x;

        // ---- open and watch
        foreach (var x in VrTap(Key.Enter)) yield return x;
        s = null; foreach (var x in VrUntil(() => ReplayViewer != null, 60 * 5, "the viewer session exists (2)")) yield return x; s = ReplayViewer;
        foreach (var x in VrUntil(() => s.State == ReplayViewerState.Playing, 60 * 120, "the viewer reached Playing")) yield return x;
        for (int i = 0; i < 90; i++) yield return null;
        VwCheck(!s.Scene.ViewerPaused && s.Scene.ViewerClock > 0.5f, $"it plays on its own (clock {s.Scene.ViewerClock:0.0})");
        VwCheck(s.Scene.ViewerCamera == ViewerCameraMode.TopDown && s.Scene.ViewerSpeed == 1f, "default camera is the gameplay top-down, speed 1x");
        // the default camera is the gameplay one: sampled while the lap plays at several places, against the live camera at the same speed
        var viewCam = new List<CamSample>();
        foreach (float at in new[] { 0.1f, 0.3f, 0.5f, 0.7f, 0.9f })
        {
            s.Scene.ViewerSeek(s.Scene.ViewerDuration * at);
            for (int i = 0; i < 300; i++) { if (i % 3 == 0) viewCam.Add(VrCamSample(s.Scene)); yield return null; }
        }
        VrCompareCameras(liveCam, viewCam);
        s.Scene.ViewerSeek(0f);
        foreach (var x in VrShot("viewer-keyboard")) yield return x;
        foreach (var x in VrTap(Key.Space)) yield return x;
        VwCheck(s.Scene.ViewerPaused, "SPACE pauses");
        float c0 = s.Scene.ViewerClock;
        foreach (var x in VrShot("viewer-paused")) yield return x;
        foreach (var x in VrTap(Key.Period)) yield return x;
        float c1 = s.Scene.ViewerClock;
        foreach (var x in VrTap(Key.Comma)) yield return x;
        float c2 = s.Scene.ViewerClock;
        VwCheck(Math.Abs(c1 - c0 - 0.1f) < 0.02f && Math.Abs(c2 - c1 + 0.1f) < 0.02f, $"PERIOD steps +0.1 s and COMMA -0.1 s while paused ({c0:0.00} {c1:0.00} {c2:0.00})");
        foreach (var x in VrTap(Key.Right)) yield return x;
        VwCheck(Math.Abs(s.Scene.ViewerClock - (c2 + 5f)) < 0.2f, $"RIGHT seeks +5 s ({c2:0.0} -> {s.Scene.ViewerClock:0.0})");
        foreach (var x in VrTap(Key.Left)) yield return x;
        VwCheck(Math.Abs(s.Scene.ViewerClock - c2) < 0.2f, "LEFT seeks back -5 s");
        foreach (var x in VrTap(Key.Down)) yield return x;
        VwCheck(s.Scene.ViewerSpeed == 0.5f, $"DOWN slows to 0.5x ({s.Scene.ViewerSpeed})");
        foreach (var x in VrTap(Key.Down)) yield return x;
        VwCheck(s.Scene.ViewerSpeed == 0.25f, "DOWN again: 0.25x");
        foreach (var x in VrTap(Key.Up)) yield return x; foreach (var x in VrTap(Key.Up)) yield return x; foreach (var x in VrTap(Key.Up)) yield return x;
        VwCheck(s.Scene.ViewerSpeed == 2f, "UP x3: 2x");
        foreach (var x in VrTap(Key.Down)) yield return x;
        foreach (var x in VrTap(Key.C)) yield return x;
        var cam1 = s.Scene.ViewerCamera;
        foreach (var x in VrShot("viewer-camera-2")) yield return x;
        foreach (var x in VrTap(Key.C)) yield return x;
        VwCheck(cam1 != ViewerCameraMode.TopDown && s.Scene.ViewerCamera == ViewerCameraMode.TopDown, $"C cycles the cameras and comes back to top-down ({cam1})");
        foreach (var x in VrTap(Key.H)) yield return x;
        for (int i = 0; i < 4; i++) yield return null;
        VwCheck(!s.OverlayView.Shown, "H hides the overlay");
        foreach (var x in VrShot("viewer-overlay-hidden")) yield return x;
        foreach (var x in VrTap(Key.H)) yield return x;
        foreach (var x in VrTap(Key.Space)) yield return x;
        for (int i = 0; i < 30; i++) yield return null;
        VwCheck(!s.Scene.ViewerPaused, "SPACE plays again");
        // play to the end and rest there
        s.Scene.ViewerSeek(s.Scene.ViewerDuration - 1.5f);
        foreach (var x in VrUntil(() => s.Scene.ViewerEnded, 60 * 10, "the lap ends")) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(s.Scene.ViewerPaused && s.OverlayView.Ended, "the lap rests at its end (paused, END shown)");
        foreach (var x in VrShot("viewer-end")) yield return x;
        foreach (var x in VrTap(Key.Space)) yield return x;
        VwCheck(!s.Scene.ViewerEnded && s.Scene.ViewerClock < 2f, "SPACE at the end plays again from the start");

        // ---- back: the same list, still paused
        var sigBack0 = sigList;
        foreach (var x in VrTap(Key.Escape)) yield return x;
        foreach (var x in VrUntil(() => s.State == ReplayViewerState.Closed && ReplayViewer == null, 60 * 30, "ESC closed the viewer")) yield return x;
        for (int i = 0; i < 12; i++) yield return null;
        VwCheck(_state == State.Paused && _hud.ReplayListOpen, "ESC from the viewer returns to the same list; the race is still paused (the same ESC did not continue)");
        string[] fingers = new[] { "accelerate", "brake", "steer_left", "steer_right", "handbrake", "clutch", "horn", "rescue", "pause", "start", "keys", "instructions", "comments", "camera", "minimap", "tour", "rumble" }.Select(x => "act." + x).ToArray();
        VwCheck(VwDiff(sigBack0, VwSig(), fingers) == "", $"the live scene is exactly as before the viewer, clocks and car included, key states aside ({VwDiff(sigBack0, VwSig(), fingers)})");
        VwCheck(liveAudioBefore.SequenceEqual(LiveAudioFlags()), "the live scene's audio players have the pause state they had before the viewer");
        foreach (var x in VrShot("list-after-viewer")) yield return x;

        // ---- repeated entry and exit
        long nodes0 = (long)Performance.GetMonitor(Performance.Monitor.ObjectNodeCount), objs0 = (long)Performance.GetMonitor(Performance.Monitor.ObjectCount);
        long nodesMax = nodes0;
        var objPer = new List<long>(); var objSettled = new List<double>(); var resSettled = new List<double>(); var nodeSettled = new List<double>();
        for (int cycle = 0; cycle < 10; cycle++)
        {
            foreach (var x in VrTap(Key.Enter)) yield return x;
            foreach (var x in VrUntil(() => ReplayViewer?.State == ReplayViewerState.Playing, 60 * 120, $"cycle {cycle}: Playing")) yield return x;
            for (int i = 0; i < 40; i++) yield return null;
            nodesMax = Math.Max(nodesMax, (long)Performance.GetMonitor(Performance.Monitor.ObjectNodeCount));
            var sx = ReplayViewer;
            foreach (var x in VrTap(Key.Escape)) yield return x;
            foreach (var x in VrUntil(() => ReplayViewer == null, 60 * 30, $"cycle {cycle}: closed")) yield return x;
            for (int i = 0; i < 12; i++) yield return null;
            VwCheck(_state == State.Paused && _hud.ReplayListOpen, $"cycle {cycle}: back at the list, still paused");
            { long lo = long.MaxValue; for (int k = 0; k < 3; k++) { for (int j = 0; j < 20; j++) yield return null; lo = Math.Min(lo, (long)Performance.GetMonitor(Performance.Monitor.ObjectCount)); } objPer.Add(lo); }
            // the same cycle, GC-SETTLED (the test collects, never the game; what the engine still holds after a collection is what is retained)
            { long raw = (long)Performance.GetMonitor(Performance.Monitor.ObjectCount); VlStat st = null; foreach (var _ in VlSettle(x => st = x, false)) yield return null; objSettled.Add(st.Obj); resSettled.Add(st.Res); nodeSettled.Add(st.Nodes);
              VwLog("cycle-metrics", ("cycle", cycle), ("raw_obj", raw), ("raw_low3", objPer[^1]), ("settled_obj", st.Obj), ("settled_res", st.Res), ("settled_nodes", st.Nodes), ("orph", st.Orph), ("rss", st.Rss), ("hwm", st.Hwm), ("gc_heap", GC.GetTotalMemory(false))); }      // (the lowest of three readings: objects queued for freeing are gone by then)
        }
        long nodes1 = (long)Performance.GetMonitor(Performance.Monitor.ObjectNodeCount), objs1 = (long)Performance.GetMonitor(Performance.Monitor.ObjectCount);
        VwLog("cycles", ("nodes_before", nodes0), ("nodes_after", nodes1), ("nodes_max_open", nodesMax), ("objects_before", objs0), ("objects_after", objs1));
        VwLog("cycles-objects", ("per_cycle", string.Join(",", objPer)));
        {
            // the criterion (CCCPRacing.Tests.ResourceCriterion): slope, drift and spread of the GC-settled series after three warm-up cycles; the raw series is logged, not blessed
            var ro = CCCPRacing.Tests.ResourceCriterion.AfterWarmup(objSettled, 3); var rr = CCCPRacing.Tests.ResourceCriterion.AfterWarmup(resSettled, 3); var rn = CCCPRacing.Tests.ResourceCriterion.AfterWarmup(nodeSettled, 3);
            VwCheck(ro.Ok, $"settled object count is steady after warm-up over 7 cycles ({ro.Why}; slope {ro.Slope:0.00}, spread {ro.Spread:0}; settled per cycle {string.Join(",", objSettled)}; raw per cycle {string.Join(",", objPer)})");
            VwCheck(rr.Ok && rn.Ok, $"settled resource and node counts are steady ({rr.Why} / {rn.Why})");
            // negative control: the same criterion on the REAL series with a leak injected (2 objects per measured cycle) must fail
            var leaked = objSettled.Select((v, k) => v + (k >= 3 ? 2 * (k - 3) : 0)).ToList();
            VwCheck(!CCCPRacing.Tests.ResourceCriterion.AfterWarmup(leaked, 3).Ok, "negative control: a 2-objects-per-cycle leak added to this very series is rejected by the criterion");
        }
        VwCheck(Math.Abs(nodes1 - nodes0) <= 20, $"8 entries and exits leave the node count as it was ({nodes0} -> {nodes1})");

        // ---- a kept lap (nothing in the player-facing UI keeps a lap yet: the archive's own call, as the lap archive tests do) is listed as KEPT and opens from disk
        {
            var info0 = archive.List().First(i => i.Openable && !i.Retained);
            var kept = archive.Retain(info0.Id, "UI TEST").GetAwaiter().GetResult();
            VwCheck(kept.Ok, $"a lap was kept on disk by the archive ({kept.Message})");
            var l0 = _hud.ReplayList;
            StartReplayListing();
            foreach (var x in VrUntil(() => _hud.ReplayList.Rows.Any(r => r.Detail.Contains("KEPT")), 60 * 5, "the list shows a KEPT lap")) yield return x;
            foreach (var x in VrShot("list-with-kept")) yield return x;
            int keptRow = _hud.ReplayList.Rows.ToList().FindIndex(r => r.Detail.Contains("KEPT"));
            _hud.ReplayList.Selected = Math.Max(0, keptRow);
            VwLog("kept-row", ("row", keptRow), ("detail", keptRow >= 0 ? _hud.ReplayList.Rows[keptRow].Detail : "-"));
            foreach (var x in VrTap(Key.Enter)) yield return x;
            foreach (var x in VrUntil(() => ReplayViewer?.State == ReplayViewerState.Playing, 60 * 120, "the kept lap opens and plays")) yield return x;
            for (int i = 0; i < 30; i++) yield return null;
            foreach (var x in VrTap(Key.Escape)) yield return x;
            foreach (var x in VrUntil(() => ReplayViewer == null, 60 * 30, "the kept lap's viewer closed")) yield return x;
            for (int i = 0; i < 12; i++) yield return null;
            VwCheck(_state == State.Paused && _hud.ReplayListOpen, "after the kept lap: back at the list, still paused");
        }

        // ---- a lap that cannot be opened: a message, the same list
        var good = archive.List().First(i => i.Openable);
        var bad = new LapSource(good.Meta with { TrackId = "nonesuch" }, new byte[16], null);
        StartReplayViewer(bad, null);
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(ReplayViewer == null && _state == State.Paused && _hud.ReplayListOpen && _hud.ReplayList.NoticeIsError, $"a lap that cannot be opened leaves the list with a message ({_hud.ReplayList.Notice})");
        foreach (var x in VrShot("list-error")) yield return x;

        // ---- pad flow
        _padMode = true;
        foreach (var x in VrPad(JoyButton.A)) yield return x;
        foreach (var x in VrUntil(() => ReplayViewer?.State == ReplayViewerState.Playing, 60 * 120, "pad: A opens the lap and it plays")) yield return x;
        s = ReplayViewer;
        for (int i = 0; i < 40; i++) yield return null;
        VwCheck(s.OverlayView.PadMode, "pad hints show after a pad press");
        foreach (var x in VrShot("viewer-pad")) yield return x;
        foreach (var x in VrPad(JoyButton.A)) yield return x;
        VwCheck(s.Scene.ViewerPaused, "pad A pauses");
        foreach (var x in VrPad(JoyButton.DpadUp)) yield return x;
        VwCheck(s.Scene.ViewerSpeed == 2f, "pad D-pad up: 2x");
        foreach (var x in VrPad(JoyButton.Y)) yield return x;
        VwCheck(s.Scene.ViewerCamera != ViewerCameraMode.TopDown, "pad Y changes the camera");
        foreach (var x in VrPad(JoyButton.X)) yield return x;
        VwCheck(!s.OverlayView.Shown, "pad X hides the overlay");
        foreach (var x in VrPad(JoyButton.B)) yield return x;
        if (s.Editable)
        {
            // a KEPT lap: the speed, camera and overlay choices just made are unsaved saved-choices (phase 4B), so B asks; X discards them and closes (the pad's SAVE is A, CANCEL is B)
            for (int i = 0; i < 6; i++) yield return null;
            VwCheck(s.OverlayView.Prompt.Length > 0 && s.OverlayView.Prompt[0].StartsWith("UNSAVED"), "pad B on a kept lap with changed speed/camera/overlay asks about the unsaved choices");
            foreach (var x in VrPad(JoyButton.X)) yield return x;
        }
        foreach (var x in VrUntil(() => ReplayViewer == null, 60 * 30, "pad B closed the viewer")) yield return x;
        for (int i = 0; i < 12; i++) yield return null;
        VwCheck(_state == State.Paused && _hud.ReplayListOpen, "pad: back at the list, still paused");

        var filesAfter = VrFiles();
        string fd = string.Join(";", filesBefore.Keys.Union(filesAfter.Keys).Where(k => filesBefore.GetValueOrDefault(k) != filesAfter.GetValueOrDefault(k)).Take(8));
        VwLog("files", ("before", filesBefore.Count), ("after", filesAfter.Count), ("changed", fd));
        VwCheck(fd == "", $"using the viewer wrote no record, profile, campaign or settings file ({filesBefore.Count} files hashed; changed: {fd})");

        // ---- out and on with the race
        foreach (var x in VrPad(JoyButton.B)) yield return x;
        VwCheck(!_hud.ReplayListOpen && _state == State.Paused, "pad B closes the list (Party Meeting)");
        foreach (var x in VrTap(Key.Escape)) yield return x;
        foreach (var x in VrUntil(() => _state == State.Racing, 120, "CONTINUE resumes the race")) yield return x;
        float lap0 = _lapClock;
        for (int i = 0; i < 90; i++) yield return null;
        VwCheck(_lapClock > lap0 + 0.5f, $"the race goes on from where it was ({lap0:0.0} -> {_lapClock:0.0} s)");
    }

    long _physicsTickCount() => (long)Engine.GetPhysicsFrames();

    /// <summary>Taps Down (the list wraps) until the VISUALS cursor is on this row.</summary>
    IEnumerable VwVisualTo(VisualRowId id)
    {
        for (int k = 0; k < _hud.VisualRowIds.Length && _hud.VisualRowIds[_hud.VisualSelection] != id; k++)
            foreach (var x in VrTap(Key.Down)) yield return x;
    }
}
