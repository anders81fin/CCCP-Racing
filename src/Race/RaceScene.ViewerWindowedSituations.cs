using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CCCPRacing.UI;
using CCCPRacing.Track;
using CCCPRacing.Replay;
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
        int totalRuns = 0;
        for (int round = 1; round <= 2; round++)
        {
            _car.Reset(new Vector3(spot.X, _track.Sample(spot.X, spot.Y).Height + 0.8f, spot.Y), TrackData.YawFor(fwd));
            int runsInRound = 1; bool wasOff = false;      // (the rolling car may touch the road for an instant: that ends its run off the road and the next may have its joke: the joke is once per RUN, so count the runs)
            for (float t = 0; t < 26f; t += 1f / 60f)      // (a joke needs 10 quiet seconds; the car is kept rolling so that "STUCK?" does not take the screen)
            {
                if (_car.Speed < 6f) _car.Vel = new Vector3(fwd.X, 0f, fwd.Y) * 6f;
                SitSample(1f / 60f); yield return null;
                if (_car.OffRoadTime > 0f) wasOff = true; else if (wasOff) { runsInRound++; wasOff = false; }
            }
            totalRuns += runsInRound;
            int jokes = _sitCount.Where(kv => Array.IndexOf(Slogans.Snow, kv.Key) >= 0).Sum(kv => kv.Value);
            VwLog("sit-snow", ("round", round), ("jokes", jokes), ("runs", totalRuns), ("log", string.Join(" | ", _sitLog)));
            VwCheck(jokes >= round && jokes <= totalRuns, $"deep snow, round {round}: {jokes} snow joke(s) in all so far, at least one per round and at most one per run off the road ({totalRuns} run(s) so far) ({string.Join(" | ", _sitLog)})");
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

    // ------------------------------------------------------------------ the end screens: watch the last lap
    IEnumerable VwSitEnd()
    {
        foreach (var x in SitStart("ilyich")) yield return x;
        _autodrive = false;
        LapArchiveCapture(41.20f, LapKind.Record, true, false);
        for (int i = 0; i < 90; i++) yield return null;      // (the worker prepares the tape)
        VwCheck(_lastLapId != null && _sessionLapArchives.ContainsKey(_lastLapId), "a lap in the run's archive");
        foreach (bool gulag in new[] { false, true })
        {
            foreach (bool pad in new[] { false, true })
            {
                string which = gulag ? "GULAG" : "PLAN FULFILLED";
                _padMode = pad;
                if (gulag) { _failReason = "TEST"; _failed = true; _state = State.Failed; EnterGulag(); } else { _state = State.Finished; EnterPlanScreen(); }
                for (int i = 0; i < 20; i++) yield return null;
                string want = pad ? "START  WATCH LAST LAP AS REPLAY" : "F3  WATCH LAST LAP AS REPLAY";
                VwCheck(_hud.EndWatchHint == want, $"{which}, {(pad ? "pad" : "keyboard")}: the end screen offers '{_hud.EndWatchHint}'");
                if (!pad) foreach (var x in VrShot($"sit-end-{(gulag ? "gulag" : "plan")}")) yield return x;
                if (pad) foreach (var x in VrPad(JoyButton.Start)) yield return x; else foreach (var x in VrTap(Key.F3)) yield return x;
                foreach (var x in VrUntil(() => ReplayViewer is { State: ReplayViewerState.Playing }, 60 * 30, $"{which}: the viewer plays the last lap")) yield return x;
                VwCheck(ReplayViewer != null && (_gulag || _planScreen), $"{which}: the replay of the last lap opens over the end screen");
                foreach (var x in VrTap(Key.Escape)) yield return x;
                foreach (var x in VrUntil(() => ReplayViewer == null, 60 * 20, "the viewer closed")) yield return x;
                for (int i = 0; i < 20; i++) yield return null;
                VwCheck(gulag ? _gulag : _planScreen, $"{which}: back on the same end screen");
                // leave it (any other button / key) for the next round
                if (gulag) _gulag = false; else _planScreen = false;
                _failed = false; _state = State.Racing; _hud.Mode = HudMode.Race;
            }
        }
    }

    // ------------------------------------------------------------------ death: no truck
    IEnumerable VwSitDeath()
    {
        foreach (var x in SitStart("ilyich")) yield return x;
        _autodrive = false; _grace = 0f;
        // the plan has nothing left: the next wreck is the end
        _wrecks = 99;
        _failed = false; _wreck = false;
        float t0 = _clock;
        DestroyCar(_car.Pos + _car.Forward * 1.2f + Vector3.Up * 0.2f, "TANK SHELL");
        VwLog("sit-death", ("state", _state), ("deathEnd", _deathEnd), ("wreck", _wreck));
        for (int i = 0; i < 60; i++) yield return null;
        VwCheck(_state == State.Failed && !_gulag, $"a second after the hit the car burns, the GULAG screen is not up yet (state {_state})");
        VwGrab(GetViewport(), "sit-death-burning");
        foreach (var x in VrUntil(() => _gulag, 60 * 8, "the GULAG screen comes after the burning")) yield return x;
        float took = _clock - t0;
        VwCheck(_gulag && _hud.GulagDeath && !_truckCalled && took > 2f && took < 4.5f, $"no truck was called ({_truckCalled}); the screen came {took:0.0} s after the hit and says that death does not cancel the sentence");
        for (int i = 0; i < 30; i++) yield return null;
        VwGrab(GetViewport(), "sit-death-gulag");
    }

    // ------------------------------------------------------------------ a new fuel
    IEnumerable VwSitFuel()
    {
        foreach (var x in VwWaitReady()) yield return x;
        for (int i = 0; i < 600 && !(_state == State.Title && _card == TitleCard.None && !_showLog && !_askName); i++) yield return null;
        for (int i = 0; i < 60; i++) yield return null;
        BeginFuelCeremony();      // (a profile that has never been asked: nothing yet, the question is marked as asked)
        VwCheck(_fuelCeremony == null && _campaign.FuelPresentationStarted, "a profile that has never been asked treats what is open as old news");
        // silver on the first three tracks on diesel opens PETROL
        foreach (var id in Tracks.Take(3).Select(t => t.id)) _campaign.Award(id, NormPlan.Medal.Silver, FuelGrade.Diesel);
        VwCheck(FuelOpenByMedals(FuelGrade.Petrol) && !_campaign.FuelPresented(FuelGrade.Petrol), "PETROL is open by the silvers and has not had its ceremony");
        BeginFuelCeremony();
        for (int i = 0; i < 20; i++) yield return null;
        VwCheck(FuelCeremonyActive && _fuelCeremony == FuelGrade.Petrol && _hud.FuelCeremonyOn, "the ceremony of PETROL is up over the title");
        foreach (float at in new[] { 1.5f, 3.2f, 6.0f, 9.5f, 12.5f })
        {
            for (int i = 0; i < 900 && _hud.FuelCeremonyAge < at; i++) yield return null;
            VwGrab(GetTree().Root, $"sit-fuel-{at:0.0}"); yield return null;
        }
        // a key before it can close does nothing; after, it closes, is marked, and does not come back
        foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 10; i++) yield return null;
        VwCheck(!_hud.FuelCeremonyOn && _campaign.FuelPresented(FuelGrade.Petrol) && _fuelCeremony == null, "a key closes the ceremony once it is ready, and PETROL is marked as presented");
        BeginFuelCeremony();
        VwCheck(_fuelCeremony == null, "it does not come back");
        // JET FUEL: silver on all four tracks on petrol
        foreach (var id in Tracks.Select(t => t.id)) _campaign.Award(id, NormPlan.Medal.Silver, FuelGrade.Petrol);
        BeginFuelCeremony();
        for (int i = 0; i < 20; i++) yield return null;
        VwCheck(_fuelCeremony == FuelGrade.Rocket, "the next fuel to open (JET FUEL) has its own ceremony");
        for (int i = 0; i < 900 && _hud.FuelCeremonyAge < 4.6f; i++) yield return null;
        VwGrab(GetTree().Root, "sit-fuel-jet");
    }

    // ------------------------------------------------------------------ the SPUTNIK test pilots' programme
    IEnumerable SpWaitAge(float age, float max = 60f)
    {
        for (int i = 0; i < 60 * max && _hud.SpAge < age; i++) yield return null;
    }

    IEnumerable SpPress()
    {
        foreach (var x in VrTap(Key.Enter)) yield return x;
        for (int i = 0; i < 8; i++) yield return null;
    }

    IEnumerable VwSitSputnik()
    {
        foreach (var x in VwWaitReady()) yield return x;
        for (int i = 0; i < 600 && !(_state == State.Title && _card == TitleCard.None && !_showLog && !_askName); i++) yield return null;
        for (int i = 0; i < 60; i++) yield return null;
        foreach (var b in _boards) b.LastName = "ANTTI KORSUMAKI";
        const NormPlan.Medal N = NormPlan.Medal.None, S = NormPlan.Medal.Silver, G = NormPlan.Medal.Gold;
        void Award(string t, NormPlan.Medal m) => _campaign.Award(t, m, FuelGrade.Sputnik);
        BeginFuelCeremony(); _fuelCeremony = null; _hud.EndFuelCeremony();      // (no fuel ceremony in the way)
        BeginSputnikCeremony();
        VwCheck(_spStage == SpStage.None, "no Sputnik medals: no ceremony");
        // other fuels' golds on every track do nothing
        foreach (var t in SputnikStanding.AllTracks) { _campaign.Award(t, G, FuelGrade.Rocket); _campaign.Award(t, G, FuelGrade.Petrol); }
        BeginSputnikCeremony();
        VwCheck(_spStage == SpStage.None, "gold on the other fuels: no ceremony");

        // 1: the test pilot's licence
        foreach (var t in SputnikStanding.MainTracks) Award(t, S);
        BeginSputnikCeremony();
        for (int i = 0; i < 20; i++) yield return null;
        VwCheck(_spStage == SpStage.Card && _spMain == SputnikAward.TestPilot && _spFresh.Length == 1, "silver on the three main tracks: the test pilot's licence, alone");
        foreach (var x in SpWaitAge(3.5f)) yield return x;
        VwGrab(GetTree().Root, "sp1-licence");
        foreach (var x in SpPress()) yield return x;
        VwCheck(_spStage == SpStage.None && _campaign.SputnikPresented(SputnikAward.TestPilot), "a key closes it and marks it presented");
        BeginSputnikCeremony();
        VwCheck(_spStage == SpStage.None, "it is not shown twice");

        // 2: the third class (Poligon's silver)
        Award("bonus", S);
        BeginSputnikCeremony();
        foreach (var x in SpWaitAge(3.6f)) yield return x;
        VwCheck(_spStage == SpStage.Card && _spMain == SputnikAward.Third, "Poligon's silver: the third class");
        VwGrab(GetTree().Root, "sp2-third");
        foreach (var x in SpPress()) yield return x;

        // 3: the second class (two golds)
        Award("ilyich", G); Award("atomgrad", G);
        BeginSputnikCeremony();
        foreach (var x in SpWaitAge(3.6f)) yield return x;
        VwCheck(_spStage == SpStage.Card && _spMain == SputnikAward.Second, "two golds: the second class");
        VwGrab(GetTree().Root, "sp3-second");
        foreach (var x in SpPress()) yield return x;

        // 4: the third gold on the last main track: first class and champion at once, one ceremony, the first class on the card, both in the letter
        Award("siberia", G);
        BeginSputnikCeremony();
        foreach (var x in SpWaitAge(3.6f)) yield return x;
        VwCheck(_spStage == SpStage.Card && _spMain == SputnikAward.First && _spFresh.Length == 2 && _spFresh.Contains(SputnikAward.Champion), "the third gold: first class on the card, the champion in the letter, one ceremony");
        VwGrab(GetTree().Root, "sp4-first");
        foreach (var x in SpPress()) yield return x;
        VwCheck(_spStage == SpStage.Letter, "a second page: the grant letter");
        foreach (var x in SpWaitAge(1.6f)) yield return x;
        VwGrab(GetTree().Root, "sp4-letter");
        foreach (var x in SpPress()) yield return x;
        VwCheck(_spStage == SpStage.None && _campaign.SputnikPresented(SputnikAward.Champion) && _campaign.SputnikPresented(SputnikAward.First), "both are marked presented");

        // 5: the finale
        Award("bonus", G);
        BeginSputnikCeremony();
        foreach (var x in SpWaitAge(4.2f)) yield return x;
        VwCheck(_spStage == SpStage.Card && _spMain == SputnikAward.Chief && _spFresh.Length == 1, "four golds: the chief test pilot");
        VwGrab(GetTree().Root, "sp5-chief");
        foreach (var x in SpPress()) yield return x;
        VwCheck(_spStage == SpStage.Paint, "the paint scene follows");
        foreach (float at in new[] { 1.0f, 4.0f, 6.5f, 8.8f, 11.0f, 13.5f })
        {
            for (int i = 0; i < 60 * 30 && _hud.SpPaintAge < at; i++) yield return null;
            VwGrab(GetTree().Root, $"sp5-paint-{at:0.0}"); yield return null;
        }
        VwCheck(_carVis.HasLivery && _carVis.LiveryStars == 4 && _carVis.LiveryName == "ANTTI KORSUMAKI", $"the livery is on the car being shown ('{_carVis.LiveryName}', {_carVis.LiveryStars} stars)");
        for (int i = 0; i < 60 * 8 && !(_hud.SpPaintDone && _hud.SpPaintAge > _hud.SpPaintDoneAt + 1.3f); i++) yield return null;
        foreach (var x in SpPress()) yield return x;
        for (int i = 0; i < 30; i++) yield return null;
        VwCheck(_spStage == SpStage.None && _campaign.SputnikPresented(SputnikAward.Chief) && _campaign.SputnikPaintedName == "ANTTI KORSUMAKI", "closed: presented, and the painted name kept");
        BeginSputnikCeremony();
        VwCheck(_spStage == SpStage.None, "nothing more to show");
    }

    IEnumerable VwSitSputnikOrder()
    {
        foreach (var x in VwWaitReady()) yield return x;
        for (int i = 0; i < 600 && !(_state == State.Title && _card == TitleCard.None && !_showLog && !_askName); i++) yield return null;
        for (int i = 0; i < 60; i++) yield return null;
        foreach (var b in _boards) b.LastName = "A VERY LONG NAME";
        const NormPlan.Medal G = NormPlan.Medal.Gold;
        BeginFuelCeremony(); _fuelCeremony = null; _hud.EndFuelCeremony();
        // the three main golds before Poligon: the champion's pennant (and the licence), no order
        foreach (var t in SputnikStanding.MainTracks) _campaign.Award(t, G, FuelGrade.Sputnik);
        BeginSputnikCeremony();
        foreach (var x in SpWaitAge(3.6f)) yield return x;
        VwCheck(_spStage == SpStage.Card && _spMain == SputnikAward.Champion && _spFresh.Length == 2 && !_spFresh.Contains(SputnikAward.Third), "three main golds before Poligon: the champion and the licence, no order");
        VwGrab(GetTree().Root, "sp6-champion");
        foreach (var x in SpPress()) yield return x;
        foreach (var x in SpWaitAge(1.6f)) yield return x;
        VwGrab(GetTree().Root, "sp6-letter");
        foreach (var x in SpPress()) yield return x;
        // then Poligon's third gold: the chief test pilot straight from a champion's profile; the champion is not shown again
        _campaign.Award("bonus", G, FuelGrade.Sputnik);
        BeginSputnikCeremony();
        VwCheck(_spStage == SpStage.Card && _spFresh.Length == 4 && !_spFresh.Contains(SputnikAward.Champion), $"Poligon's gold after the three main golds: third, second, first and the chief at once, the champion not again ({_spFresh.Length} new)");
        foreach (var x in SpWaitAge(4.2f)) yield return x;
        foreach (var x in SpPress()) yield return x;
        VwGrab(GetTree().Root, "sp6-letter-all");
    }

    // ------------------------------------------------------------------ the chief test pilot's livery in a run and in the laps' replays (ownership)
    IEnumerable VwSitLivery()
    {
        foreach (var x in VwWaitReady()) yield return x;
        foreach (var x in VrUntil(() => _state == State.Racing && _lapClock > 3f, 60 * 60, "the race is running")) yield return x;
        VwCheck(_run == RunMode.Rally && _norms.Enabled, "a single-player Socialist Rally");
        // a profile that never had the paint: no livery on a Sputnik
        _rallyFuel = FuelGrade.Sputnik; for (int i = 0; i < 10; i++) yield return null;
        VwCheck(EngineFuel == FuelGrade.Sputnik && !_carVis.HasLivery, "a Sputnik without the paint: a plain car");
        // painted: the livery on the Sputnik in a run, never on another fuel's car
        _campaign.PaintSputnikName("KOSTIA");
        for (int i = 0; i < 10; i++) yield return null;
        VwCheck(_carVis.HasLivery && _carVis.LiveryName == "KOSTIA" && _carVis.LiveryStars == 4, $"painted: the Sputnik wears it in the run ('{_carVis.LiveryName}', {_carVis.LiveryStars} stars)");
        foreach (var x in VrShot("livery-run")) yield return x;
        _rallyFuel = FuelGrade.Petrol; for (int i = 0; i < 10; i++) yield return null;
        VwCheck(EngineFuel == FuelGrade.Petrol && !_carVis.HasLivery, "the petrol car does not wear it");
        // laps: one on the Sputnik (painted), one on petrol, one on the Sputnik after the paint is gone (a profile without it)
        string Meta(string id) => _lapArchiveUsed.List().First(e => e.Id == id).Meta.Livery;
        _rallyFuel = FuelGrade.Sputnik;
        LapArchiveCapture(45.0f, LapKind.Normal, true, false); string sputnikLap = _lastLapId;
        for (int i = 0; i < 90; i++) yield return null;
        VwCheck(sputnikLap != null && Meta(sputnikLap) == "KOSTIA|4", $"the Sputnik lap's metadata carries the livery ('{(sputnikLap == null ? "-" : Meta(sputnikLap))}')");
        foreach (var x in VrTap(Key.F3)) yield return x;
        foreach (var x in VrUntil(() => ReplayViewer is { State: ReplayViewerState.Playing }, 60 * 30, "the viewer plays")) yield return x;
        for (int i = 0; i < 20; i++) yield return null;
        VwCheck(ReplayViewer?.LapId == sputnikLap && EngineFuel == FuelGrade.Sputnik && _carVis.HasLivery && _carVis.LiveryName == "KOSTIA", $"the replay of that lap shows the livery on its own car ('{_carVis.LiveryName}')");
        foreach (var x in VrShot("livery-replay")) yield return x;
        // the review picture: the ordinary replay of that saved Sputnik lap, the existing free camera placed at the car's right side (ViewerPlaceFreeKey, the camera key's own path), the controls left as they are
        {
            var rv = ReplayViewer.Scene;
            rv.ViewerSetPaused(true); GetWindow().GrabFocus();
            foreach (var x in VrTap(Key.W, 8)) yield return x;
            VwCheck(rv.ViewerFreeCameraActive, "the free camera is up in the replay");
            Vector3 carPos = rv._car.Pos; Basis rot = rv._car.Rot;
            Vector3 eye = carPos + rot * new Vector3(3.3f, 0.45f, 0f), look = carPos + rot * new Vector3(0.9f, 0.0f, -0.03f), dir = (look - eye).Normalized();
            rv.ViewerPlaceFreeKey(new CCCPRacing.Replay.LapFreeCamera { X = eye.X, Y = eye.Y, Z = eye.Z, Yaw = Mathf.Atan2(-dir.X, -dir.Z), Pitch = Mathf.Asin(dir.Y), Fov = 32f, Near = 0.15f, Far = 260f });
            for (int i = 0; i < 20; i++) yield return null;
            VwCheck(rv._car.Pos.DistanceTo(carPos) < 1e-3f, "the replay stands still while the picture is taken");
            VwGrab(GetTree().Root, "livery-replay-side");
            rv.ViewerEndFreeCamera(); rv.ViewerSetPaused(false);
            for (int i = 0; i < 10; i++) yield return null;
        }
        foreach (var x in VrTap(Key.Escape)) yield return x;
        foreach (var x in VrUntil(() => ReplayViewer == null && _state == State.Racing, 60 * 20, "the viewer closed")) yield return x;
        _rallyFuel = FuelGrade.Petrol;
        LapArchiveCapture(47.0f, LapKind.Normal, true, false); string petrolLap = _lastLapId;
        for (int i = 0; i < 90; i++) yield return null;
        VwCheck(petrolLap != null && petrolLap != sputnikLap && Meta(petrolLap) == "", $"a petrol lap of the same profile has none ('{(petrolLap == null ? "-" : Meta(petrolLap))}')");
        foreach (var x in VrTap(Key.F3)) yield return x;
        foreach (var x in VrUntil(() => ReplayViewer is { State: ReplayViewerState.Playing }, 60 * 30, "the viewer plays")) yield return x;
        for (int i = 0; i < 20; i++) yield return null;
        VwCheck(ReplayViewer?.LapId == petrolLap && !_carVis.HasLivery, "its replay is a plain car");
        foreach (var x in VrTap(Key.Escape)) yield return x;
        foreach (var x in VrUntil(() => ReplayViewer == null && _state == State.Racing, 60 * 20, "the viewer closed")) yield return x;
        // the paint is kept: the replay of the first lap is the same later, whoever drives now
        _campaign.PaintSputnikName(""); _rallyFuel = FuelGrade.Sputnik;
        LapArchiveCapture(49.0f, LapKind.Normal, true, false); string bareLap = _lastLapId;
        for (int i = 0; i < 90; i++) yield return null;
        VwCheck(bareLap != null && Meta(bareLap) == "", "a Sputnik lap of a profile without the paint: no livery in the metadata (older laps are the same)");
        foreach (var x in VrTap(Key.F3)) yield return x;
        foreach (var x in VrUntil(() => ReplayViewer is { State: ReplayViewerState.Playing }, 60 * 30, "the viewer plays")) yield return x;
        for (int i = 0; i < 20; i++) yield return null;
        VwCheck(ReplayViewer?.LapId == bareLap && EngineFuel == FuelGrade.Sputnik && !_carVis.HasLivery, "its replay is a plain Sputnik");
        foreach (var x in VrTap(Key.Escape)) yield return x;
    }

    // ------------------------------------------------------------------ KGB FILES > PERSONNEL > G: the Sputnik gallery
    IEnumerable VwSitGallery()
    {
        foreach (var x in VwWaitReady()) yield return x;
        for (int i = 0; i < 600 && !(_state == State.Title && _card == TitleCard.None && !_showLog && !_askName); i++) yield return null;
        for (int i = 0; i < 60; i++) yield return null;
        foreach (var b in _boards) b.LastName = "ANTTI KORSUMAKI";
        var cfg = new ConfigFile(); cfg.Load(FileDir + "settings.cfg"); cfg.SetValue("driver", "name", "ANTTI KORSUMAKI"); cfg.Save(FileDir + "settings.cfg");
        BeginFuelCeremony(); _fuelCeremony = null; _hud.EndFuelCeremony();
        const NormPlan.Medal S = NormPlan.Medal.Silver, G = NormPlan.Medal.Gold;
        // 1: silver on all four tracks: the licence and the third class, the pennant and the car locked
        foreach (var t in SputnikStanding.AllTracks) _campaign.Award(t, S, FuelGrade.Sputnik);
        EnterFiles();
        for (int i = 0; i < 20; i++) yield return null;
        VwCheck(_showFiles && _hud.Files[0].Used && _filesSel == 0, "the personnel files are open on the driver's row");
        foreach (var x in VrShot("gal0-files")) yield return x;
        foreach (var x in VrTap(Key.G)) yield return x;
        for (int i = 0; i < 10; i++) yield return null;
        VwCheck(_galleryOpen && _galleryPage == 0 && _hud.GalleryOn && _hud.GalleryEarned[0] && _hud.GalleryEarned[1] && !_hud.GalleryEarned[2] && !_hud.GalleryEarned[3], "G opens the gallery: licence and order earned, pennant and car not");
        foreach (var x in VrShot("gal1-licence")) yield return x;
        foreach (var x in VrTap(Key.Right)) yield return x;
        for (int i = 0; i < 10; i++) yield return null;
        VwCheck(_galleryPage == 1 && _hud.GalleryOrderClass == SputnikAward.Third, "page 2: the order, the third class");
        foreach (var x in VrShot("gal2-order-third")) yield return x;
        foreach (var x in VrTap(Key.Right)) yield return x;
        for (int i = 0; i < 10; i++) yield return null;
        foreach (var x in VrShot("gal3-pennant-locked")) yield return x;
        foreach (var x in VrTap(Key.Right)) yield return x;
        for (int i = 0; i < 10; i++) yield return null;
        VwCheck(_galleryPage == 3 && !GalleryCarActive, "page 4 without the chief test pilot: a locked card, no car");
        foreach (var x in VrShot("gal4-car-locked")) yield return x;
        foreach (var x in VrTap(Key.Right)) yield return x;
        VwCheck(_galleryPage == 0, "the pages wrap round");
        foreach (var x in VrTap(Key.Left)) yield return x;
        VwCheck(_galleryPage == 3, "and back");
        foreach (var x in VrTap(Key.Escape)) yield return x;
        for (int i = 0; i < 10; i++) yield return null;
        VwCheck(!_galleryOpen && _showFiles, "Esc closes the gallery, the files stay");
        // 2: four golds and the painted name: the whole gallery, the car on its own page
        foreach (var t in SputnikStanding.AllTracks) _campaign.Award(t, G, FuelGrade.Sputnik);
        _campaign.PaintSputnikName("KOSTIA");
        foreach (var x in VrTap(Key.G)) yield return x;
        for (int i = 0; i < 10; i++) yield return null;
        VwCheck(_hud.GalleryEarned.All(e => e) && _hud.GalleryOrderClass == SputnikAward.First, "four golds: all four pages earned, the order at the first class");
        foreach (var x in VrShot("gal5-licence-gold")) yield return x;
        foreach (var x in VrTap(Key.Right)) yield return x;
        for (int i = 0; i < 10; i++) yield return null;
        foreach (var x in VrShot("gal6-order-first")) yield return x;
        foreach (var x in VrTap(Key.Right)) yield return x;
        for (int i = 0; i < 10; i++) yield return null;
        foreach (var x in VrShot("gal7-pennant")) yield return x;
        foreach (var x in VrTap(Key.Right)) yield return x;
        for (int i = 0; i < 90; i++) yield return null;
        VwCheck(GalleryCarActive && _hud.GalleryOnCar && _carVis.HasLivery && _carVis.LiveryName == "KOSTIA" && _carVis.LiveryStars == 4, $"the car page: the world's car with the painted name ('{_carVis.LiveryName}')");
        foreach (float age in new[] { 1.5f, 5.0f })
        {
            for (int i = 0; i < 60 * 12 && _hud.GalleryAge < age; i++) yield return null;
            VwGrab(GetTree().Root, $"gal8-car-{age:0.0}"); yield return null;
        }
        foreach (var x in VrTap(Key.Right)) yield return x;
        for (int i = 0; i < 10; i++) yield return null;
        VwCheck(!GalleryCarActive && _galleryPage == 0, "leaving the car page takes the car off the screen");
        foreach (var x in VrTap(Key.Escape)) yield return x;
        foreach (var x in VrTap(Key.Escape)) yield return x;
        for (int i = 0; i < 20; i++) yield return null;
        VwCheck(!_showFiles && !_galleryOpen && _state == State.Title, "Esc again leaves the files");
        VwCheck(_campaign.SputnikPaintedName == "KOSTIA", "the gallery wrote nothing");
    }
}
