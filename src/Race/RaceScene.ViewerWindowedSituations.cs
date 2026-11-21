using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CCCPRacing.UI;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Race;

// Windowed scenarios "sit-*" (--viewer-windowed-only=situations): the driving messages in the real game, in the situations that a normal run only meets by chance. The car and the actors are PLACED (the tractor, the car out in
// the field, the car across a stretch of road, the cook's kitchen under a shell) and the game itself decides what to say; the scenario watches the two HUD lines and screenshots them. Development only (not in the release build).
public partial class RaceScene
{
    // what the HUD lines said and how many times each text appeared (a text that goes away and comes back counts again)
    readonly Dictionary<string, int> _sitCount = new();
    readonly List<string> _sitLog = new();
    string _sitCenter, _sitTop;
    float _sitT;

    void SitSample(float dt)
    {
        _sitT += dt;
        string c = _hud.CenterText, t = _hud.TopText;
        if (c != _sitCenter) { _sitCenter = c; if (c != null) { _sitCount[c] = _sitCount.GetValueOrDefault(c) + 1; _sitLog.Add($"{_sitT:0.0} s center '{c}'"); } }
        if (t != _sitTop) { _sitTop = t; if (t != null) { _sitCount[t] = _sitCount.GetValueOrDefault(t) + 1; _sitLog.Add($"{_sitT:0.0} s top '{t}'"); } }
    }

    IEnumerable SitWatch(float seconds, Func<bool> until = null)
    {
        for (float t = 0; t < seconds && (until == null || !until()); t += 1f / 60f) { SitSample(1f / 60f); yield return null; }
    }

    IEnumerable SitStart(string track)
    {
        foreach (var x in VwWaitReady()) yield return x;
        foreach (var x in VrUntil(() => _state == State.Racing && _lapClock > 3f, 60 * 60, "the race is running")) yield return x;
    }

    readonly HashSet<string> _sitGrabbed = new();
    void VwGrabOnce(string name) { if (_sitGrabbed.Add(name)) VwGrab(GetViewport(), name); }

    Vector2 SitRoadForward(out int index)
    {
        Vector2 heading = new(-_car.Rot.Z.X, -_car.Rot.Z.Z);
        return RoadWarnings.RoadForward(_track, new Vector2(_car.Pos.X, _car.Pos.Z), heading.LengthSquared() < 1e-4f ? Vector2.Down : heading.Normalized(), out index);
    }

    /// <summary>Road sample index <paramref name="metres"/> ahead of road sample <paramref name="from"/> in the direction of travel.</summary>
    int SitAhead(int from, Vector2 forward, float metres)
    {
        int n = _track.Center.Length; bool inc = (_track.Center[(from + 3) % n] - _track.Center[from]).Dot(forward) >= 0f;
        float arc = 0; int i = from;
        while (arc < metres) { int next = inc ? (i + 1) % n : (i - 1 + n) % n; arc += _track.Center[i].DistanceTo(_track.Center[next]); i = next; }
        return i;
    }

    // ------------------------------------------------------------------ the tractor
    IEnumerable VwSitTractor()
    {
        foreach (var x in SitStart("ilyich")) yield return x;
        // two encounters: with another message already up (an Important one, then a Critical one); the CROSSING must show over it, once, and the AHEAD that follows when the tractor reaches the road must not
        foreach (var busy in new[] { Prio.Important, Prio.Critical })
        {
            _sitCount.Clear(); _sitLog.Clear(); _sitT = 0;
            for (int i = 0; i < 300; i++) { _hud.ClearMessages(); _hud.ClearAlert(); yield return null; }
            // place the tractor where the car will meet it; if the real game does not count it as an encounter at once (the autopilot's speed and the road's shape differ from place to place), try another distance
            RoadWarnings.TractorState worst = RoadWarnings.TractorState.None; bool crossingSeen = false; Vector2 far0 = default;
            foreach (float metres in new[] { 48f, 58f, 40f, 66f })
            {
                var fwd = SitRoadForward(out int ci);
                int ti = SitAhead(ci, fwd, metres);
                Vector2 centre = _track.Center[ti], outward = new Vector2(-_track.CenterDir(ti).Y, _track.CenterDir(ti).X);
                float hw = _track.RoadHalfWidth(ti, centre);
                Vector2 pos = centre + outward * (hw + 5.5f), toRoad = -outward;
                _hud.ClearMessages(); _hud.ClearAlert(); _sitCount.Clear(); _sitLog.Clear(); _sitT = 0;
                VwLog("sit-tractor-gate", ("busy", busy), ("given_before", _tractorGate.IsGiven), ("metres", metres));
                _hud.Show($"OTHER {busy.ToString().ToUpper()} MESSAGE", "THE SCREEN IS BUSY", 6f, Hud.Gold, busy);
                _tractor.PlaceForTest(pos, toRoad, centre - outward * (hw + 25f), 6f);      // (fast enough to be on the road before the car has passed: CROSSING first, then AHEAD in the same encounter)
                worst = RoadWarnings.TractorState.None; crossingSeen = false; int crossFrames = 0;
                for (float t = 0; t < 9f; t += 1f / 60f)
                {
                    SitSample(1f / 60f);
                    Vector2 h = new(-_car.Rot.Z.X, -_car.Rot.Z.Z);
                    var st = RoadWarnings.Tractor(_track, _car.Pos, h.Normalized(), _car.Speed, _tractor.Position2D, _tractor.Forward2D, _tractor.Speed, _tractor.Position2D - _tractor.Forward2D * 3.5f, out _);
                    if (st == RoadWarnings.TractorState.Crossing) crossingSeen = true;
                    if (st > worst) worst = st;
                    if (_sitCenter == "TRACTOR CROSSING!") { if (++crossFrames == 4) VwGrabOnce($"sit-tractor-warning-{busy}"); } else crossFrames = 0;      // (a few frames after it appears: the picture is the last one drawn)
                    yield return null;
                }
                if (crossingSeen && worst == RoadWarnings.TractorState.Ahead) break;      // a real encounter happened: judge it
                Vector2 cp0 = new(_car.Pos.X, _car.Pos.Z); float W0 = _track.WorldSize; far0 = new[] { new Vector2(40, 40), new Vector2(W0 - 40, 40), new Vector2(40, W0 - 40), new Vector2(W0 - 40, W0 - 40) }.OrderByDescending(c => c.DistanceTo(cp0)).First();
                _tractor.PlaceForTest(far0, Vector2.Down, far0, 0f);
                for (int i = 0; i < 300; i++) yield return null;
            }
            VwLog("sit-tractor", ("busy", busy), ("states_seen", $"crossing {crossingSeen}, worst {worst}"), ("log", string.Join(" | ", _sitLog)));
            VwCheck(_sitCount.GetValueOrDefault("TRACTOR CROSSING!") == 1, $"over a busy HUD ({busy}) the real game shows TRACTOR CROSSING! once ({string.Join(" | ", _sitLog)})");
            VwCheck(worst == RoadWarnings.TractorState.Ahead, $"(the tractor really did get onto the road: its state went as far as {worst})");
            VwCheck(_sitCount.GetValueOrDefault("TRACTOR AHEAD!") == 0, $"and TRACTOR AHEAD! does not follow in the same encounter");
            Vector2 cp = new(_car.Pos.X, _car.Pos.Z); float W = _track.WorldSize; Vector2 far = new[] { new Vector2(40, 40), new Vector2(W - 40, 40), new Vector2(40, W - 40), new Vector2(W - 40, W - 40) }.OrderByDescending(c => c.DistanceTo(cp)).First();
            _tractor.PlaceForTest(far, Vector2.Down, far, 0f);      // (far away: the next encounter is a new one)
            for (int i = 0; i < 240; i++) { SitSample(1f / 60f); yield return null; }
        }
    }

    // ------------------------------------------------------------------ stuck, the roof, the shortcut
    IEnumerable VwSitStuck()
    {
        foreach (var x in SitStart("ilyich")) yield return x;
        _autodrive = false;
        var fwd = SitRoadForward(out int ci);
        Vector2 centre = _track.Center[ci], outward = new Vector2(-_track.CenterDir(ci).Y, _track.CenterDir(ci).X);
        Vector2 spot = centre + outward * (_track.RoadHalfWidth(ci, centre) + 16f);
        _car.Reset(new Vector3(spot.X, _track.Sample(spot.X, spot.Y).Height + 0.8f, spot.Y), TrackData.YawFor(-outward));
        _sitCount.Clear(); _sitLog.Clear(); _sitT = 0; _padMode = false; _slowCd = 0;
        foreach (var x in SitWatch(12f, () => _sitCenter != null && _sitCenter.StartsWith("STUCK"))) yield return x;
        VwCheck(_sitCenter == "STUCK? PRESS R: LAP VOID", $"a car stuck in the field, keyboard: the heading is the action and the lap's fate ('{_sitCenter}')");
        VwGrab(GetViewport(), "sit-stuck-keyboard");
        _hud.ClearMessages(); for (int i = 0; i < 30; i++) yield return null;
        _padMode = true; _slowCd = 0; _slowTime = 7f;
        foreach (var x in SitWatch(4f, () => _sitCenter != null && _sitCenter.StartsWith("STUCK") && _sitCenter.Contains("B"))) yield return x;
        VwCheck(_sitCenter == "STUCK? PRESS B: LAP VOID", $"the same with a pad shows B ('{_sitCenter}')");
        VwGrab(GetViewport(), "sit-stuck-pad");
        // the roof
        _hud.ClearMessages(); _padMode = false; _roofHinted = false; _car.Reset(_track.CenterPos(ci) + Vector3.Up * 0.7f, TrackData.YawFor(_track.CenterDir(ci)));
        for (float t = 0; t < 6f && !(_sitCenter != null && _sitCenter.StartsWith("UPSIDE")); t += 1f / 60f)
        {
            _car.Rot = new Basis(Vector3.Forward, Mathf.Pi) * new Basis(Vector3.Up, _car.Rot.GetEuler().Y); _car.Vel = Vector3.Zero; _car.AngVel = Vector3.Zero;      // (held on its roof)
            SitSample(1f / 60f); yield return null;
        }
        VwCheck(_sitCenter == "UPSIDE DOWN: PRESS R, LAP VOID", $"on the roof: ('{_sitCenter}')");
        VwGrab(GetViewport(), "sit-roof");
    }

    IEnumerable VwSitShortcut()
    {
        foreach (var x in SitStart("ilyich")) yield return x;
        _autodrive = false;
        var fwd = SitRoadForward(out int ci);
        int jump = SitAhead(ci, fwd, 110f);      // the first road touched after a cut across the field is 110 m ahead of the timer's gate: more than the two gates and a bit that are forgiven (the timer has not followed the road yet)
        _timer.UnfollowForTest();
        _sitCount.Clear(); _sitLog.Clear(); _sitT = 0;
        VwLog("sit-shortcut-timer", ("progress_before", _timer.Progress), ("next_gate", _timer.NextGate), ("car_road_index", ci), ("jump_to", jump));
        _car.Reset(_track.CenterPos(jump) + Vector3.Up * 0.7f, TrackData.YawFor(_track.CenterDir(jump)));
        foreach (var x in SitWatch(4f, () => _sitCenter != null && _sitCenter.StartsWith("SHORTCUT"))) yield return x;
        VwLog("sit-shortcut-timer", ("progress_after", _timer.Progress), ("next_gate", _timer.NextGate));
        VwCheck(_sitCenter == "SHORTCUT: DRIVE THE MISSED STRETCH", $"a skipped stretch of road: the heading says what to do ('{_sitCenter}')");
        VwGrab(GetViewport(), "sit-shortcut");
    }

    // ------------------------------------------------------------------ the slurry
    IEnumerable VwSitSlurry()
    {
        foreach (var x in SitStart("ilyich")) yield return x;
        _sitCount.Clear(); _sitLog.Clear(); _sitT = 0;
        if (_slurryAll == null) { VwCheck(false, "the track has no slurry tractor"); yield break; }
        foreach (var x in VrUntil(() => _lapClock > 16f, 60 * 40, "the start's reserved seconds are over")) yield return x;      // (the opening norm keeps the screen for itself: only a Critical message may interrupt it)
        _hud.ClearAlert(); _hud.ClearMessages(); yield return null;
        _slurryAll.RaiseRevengeOnTheRoadForTest();
        foreach (var x in SitWatch(10f, () => _sitTop != null && _sitTop.StartsWith("SLURRY"))) yield return x;      // (it waits in the queue behind the norm alert at the start of a run)
        VwCheck(_sitTop == "SLURRY ON THE ROAD: EASE OFF, DO NOT BRAKE", $"the slurry on the road: the driving advice is in the heading ('{_sitTop}')");
        VwGrab(GetViewport(), "sit-slurry");
    }

    // ------------------------------------------------------------------ deep snow, once per run off the road
    IEnumerable VwSitSnow()
    {
        foreach (var x in SitStart("siberia")) yield return x;
        _autodrive = false;
        var fwd = SitRoadForward(out int ci);
        // the nearest deep snow off the road, found by looking sideways from the road at growing distances
        Vector2 centre = _track.Center[ci], side = new Vector2(-_track.CenterDir(ci).Y, _track.CenterDir(ci).X);
        Vector2 spot = default; bool found = false;
        for (float d = 8f; d < 60f && !found; d += 2f)
            foreach (float sgn in new[] { 1f, -1f })
            {
                Vector2 p = centre + side * sgn * (_track.RoadHalfWidth(ci, centre) + d);
                if (_track.Sample(p.X, p.Y).Surface == SurfaceType.DeepSnow) { spot = p; found = true; break; }
            }
        VwCheck(found, "deep snow was found beside the road on Siberia");
        if (!found) yield break;
        _sitCount.Clear(); _sitLog.Clear(); _sitT = 0; _grassCd = 0;
        for (int round = 1; round <= 2; round++)
        {
            _car.Reset(new Vector3(spot.X, _track.Sample(spot.X, spot.Y).Height + 0.8f, spot.Y), TrackData.YawFor(fwd));
            for (float t = 0; t < 26f; t += 1f / 60f)      // (a joke needs 10 quiet seconds: one run off the road of 26 s may have exactly one; the car is kept rolling so that "STUCK?" does not take the screen)
            {
                if (_car.Speed < 6f) _car.Vel = new Vector3(fwd.X, 0f, fwd.Y) * 6f;
                SitSample(1f / 60f); yield return null;
            }
            int jokes = _sitCount.Where(kv => Array.IndexOf(Slogans.Snow, kv.Key) >= 0).Sum(kv => kv.Value);
            VwLog("sit-snow", ("round", round), ("jokes", jokes), ("log", string.Join(" | ", _sitLog)));
            VwCheck(jokes == round, $"deep snow, run off the road number {round}: {round} snow joke(s) in all so far, one per run however long the car stays ({string.Join(" | ", _sitLog)})");
            // back on the road for a moment: the next run off it may have its joke
            _car.Reset(_track.CenterPos(ci) + Vector3.Up * 0.7f, TrackData.YawFor(fwd));
            for (int i = 0; i < 120; i++) { SitSample(1f / 60f); yield return null; }
            _grassCd = 0;
        }
        VwGrab(GetViewport(), "sit-snow");
    }

    // ------------------------------------------------------------------ the field kitchen's cook
    IEnumerable VwSitKitchen()
    {
        foreach (var x in SitStart("bonus")) yield return x;
        _autodrive = false;
        Vector3 kitchen = new(BonusAirbase.Kitchen.X, 0, BonusAirbase.Kitchen.Y); kitchen.Y = _track.Sample(kitchen.X, kitchen.Z).Height;
        Vector3 carAt = kitchen + new Vector3(18f, 0.9f, 10f); carAt.Y = _track.Sample(carAt.X, carAt.Z).Height + 0.9f;
        _car.Reset(carAt, 0f);
        for (int i = 0; i < 30; i++) yield return null;
        _hinds?.GroundShot(kitchen + new Vector3(-30f, 0, -10f), _car);
        VwCheck(_hinds != null && _hinds.ActiveShouts().Any(), "a shell near the field kitchen makes the cook speak: a bubble at the kitchen");
        foreach (var x in SitWatch(1.0f)) yield return x;
        // look at the kitchen with the camera
        _cam.GlobalPosition = kitchen + new Vector3(0, 9f, 14f); _cam.LookAt(kitchen + Vector3.Up * 2f);
        for (int i = 0; i < 20; i++) yield return null;
        VwGrab(GetViewport(), "sit-kitchen");
        VwCheck(_sitCount.Keys.All(k => !k.Contains("SOUP")), "and there is no HUD line about the soup any more");
    }
}
