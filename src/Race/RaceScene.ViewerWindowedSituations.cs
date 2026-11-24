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

    IEnumerable VwSitConcreteCard()
    {
        foreach (var x in SitStart("atomgrad")) yield return x;
        _autodrive = false;
        StartConcreteRevenge();
        foreach (var x in VrUntil(() => _stopCard?.Kind == "revenge_concrete", 600, "the mixer revenge card opens")) yield return x;
        VwCheck(_stopCard?.Lines.Any(line => line.Contains("RIBBON")) == true, "the card explains the fresh concrete ribbon");
        foreach (var x in VrUntil(() => _hud.StopCardReady, 600, "the card accepts dismissal")) yield return x;
        foreach (var x in VrPad(JoyButton.B)) yield return x;
        VwCheck(_stopCard == null && _ploughsAll.ConcreteRevengePending, "pad B dismisses the card and leaves the revenge armed");
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
                string want = pad ? "START > WATCH LAST LAP AS REPLAY" : "F2 > WATCH LAST LAP AS REPLAY";
                VwCheck(_hud.EndWatchHint == want, $"{which}, {(pad ? "pad" : "keyboard")}: the end screen offers '{_hud.EndWatchHint}'");
                if (!pad) foreach (var x in VrShot($"sit-end-{(gulag ? "gulag" : "plan")}")) yield return x;
                if (pad) foreach (var x in VrPad(JoyButton.Start)) yield return x; else foreach (var x in VrTap(Key.F2)) yield return x;
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
        _campaign.PresentFuel(FuelGrade.Diesel);      // (the fuel question is asked: no fuel ceremony in the way)
        BeginSputnikCeremony();
        VwCheck(_spStage == SpStage.None, "no Sputnik medals: no ceremony");
        // other fuels' golds on every track do nothing
        foreach (var t in SputnikStanding.AllTracks) { _campaign.Award(t, G, FuelGrade.Rocket); _campaign.Award(t, G, FuelGrade.Petrol); }
        foreach (var g in new[] { FuelGrade.Petrol, FuelGrade.Rocket, FuelGrade.Sputnik }) _campaign.PresentFuel(g);      // (the gold on the other fuels opens fuels: their ceremonies are not what this scenario looks at)
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
        _campaign.PresentFuel(FuelGrade.Diesel);
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
        foreach (var x in VrTap(Key.F2)) yield return x;
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
        foreach (var x in VrTap(Key.F2)) yield return x;
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
        foreach (var x in VrTap(Key.F2)) yield return x;
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

    // ------------------------------------------------------------------ the ceremonies wait for a clear title (the Deck's launch: a licence over the launch flow that took no key)
    IEnumerable VwSitCeremonyGate()
    {
        foreach (var x in VwWaitReady()) yield return x;
        for (int i = 0; i < 600 && !(_state == State.Title && _card == TitleCard.None && !_showLog && !_askName); i++) yield return null;
        for (int i = 0; i < 60; i++) yield return null;
        foreach (var b in _boards) b.LastName = "ANDERS81FIN";
        BeginFuelCeremony(); _fuelCeremony = null; _hud.EndFuelCeremony();
        foreach (var t in SputnikStanding.MainTracks) _campaign.Award(t, NormPlan.Medal.Silver, FuelGrade.Sputnik);      // (earned before, never presented: the profile of the Deck)
        // the launch flow: the archive is up (at launch) and the startup banner runs: the ceremony stays away
        _filesAtLaunch = true; _showFiles = true; _hud.Mode = HudMode.Files; _ceremonyDue = true;
        for (int i = 0; i < 120; i++) yield return null;
        VwCheck(_spStage == SpStage.None && !_hud.SpOn, "with the KGB ARCHIVE open at launch no ceremony starts under it");
        CloseFiles();
        _hud.BeginStartupBanner(); _ceremonyDue = true;
        int stillPending = 0; for (int i = 0; i < 30; i++) { if (_hud.StartupArchivePending) stillPending++; yield return null; }
        VwCheck(stillPending == 0 || (_spStage == SpStage.None && !_hud.SpOn), "during the startup banner no ceremony starts");
        // clear: it starts by itself
        for (int i = 0; i < 60 * 12 && !_hud.SpOn; i++) yield return null;
        VwCheck(_hud.SpOn && _spStage == SpStage.Card && !_hud.StartupArchivePending, $"once the title is clear the licence comes ({_hud.SpOn}, {_spStage})");
        foreach (var x in SpWaitAge(3.5f)) yield return x;
        foreach (var x in VrShot("ceremony-gate")) yield return x;
        // a key closes it
        foreach (var x in SpPress()) yield return x;
        VwCheck(_spStage == SpStage.None && !_hud.SpOn && _campaign.SputnikPresented(SputnikAward.TestPilot), "a key closes it");
        // a loading card over the title keeps it away too
        _campaign.Award("bonus", NormPlan.Medal.Silver, FuelGrade.Sputnik); _ceremonyDue = true;
        _titleMenuHidden = true;
        for (int i = 0; i < 60; i++) yield return null;
        VwCheck(_spStage == SpStage.None, "with the menu hidden (SPACE) nothing starts");
        _titleMenuHidden = false;
        for (int i = 0; i < 60 * 6 && _spStage == SpStage.None; i++) yield return null;
        VwCheck(_spStage == SpStage.Card && _spMain == SputnikAward.Third, "and when it is shown again the third class comes");
    }

    // ------------------------------------------------------------------ the saved rally keeps the car and loses the lap
    IEnumerable VwSitResume() => SitResume("atomgrad");
    IEnumerable VwSitResumeFuel() => SitResume("siberia");

    /// <summary>Atomgrad has a dose and no fuel, Siberia a tank and no dose: each keeps what the track has, and the other is not kept (-1).</summary>
    IEnumerable SitResume(string track)
    {
        foreach (var x in SitStart(track)) yield return x;
        _autodrive = false;
        bool dose = _rad != null && _doseOn, fuel = _tank.Enabled;
        VwCheck(_health.Enabled && _norms.Enabled, $"{track}: an engine and the norms are there (dose {dose}, tank {fuel})");
        _norms.Restore(2, 4, 3);
        _health.Set(0.40f); ApplyTrainingPower();
        if (dose) _dose = 0.35f * _rad.Limit;
        if (fuel) _tank.Litres = 22f;
        var point = RallyCheckpointNow();
        VwCheck(Math.Abs(point.Health - 0.40f) < 1e-4f && point.Left == 4 && (dose ? Math.Abs(point.Dose - 0.35f) < 1e-4f : point.Dose < 0f) && (fuel ? Math.Abs(point.Litres - 22f) < 1e-3f : point.Litres < 0f),
            $"{track}: the checkpoint holds the car ({point.Health:0.00}, dose {point.Dose:0.00}, {point.Litres:0.0} l, {point.Left} laps); what the track does not have is not kept");
        VwCheck(RallyResume.Save(FileDir, _trackId, ResumeSignature, point, _rallyFuel) == Error.Ok, $"{track}: saved");
        var back = RallyResume.Load(FileDir, _trackId, ResumeSignature);
        VwCheck(back != null && Math.Abs(back.Health - 0.40f) < 1e-4f && (!dose || Math.Abs(back.Dose - 0.35f) < 1e-4f) && (!fuel || Math.Abs(back.Litres - 22f) < 1e-3f), $"{track}: and read back from the file");
        // the run starts again (a new countdown: everything as new), then the resume puts the car and the norm back
        _norms.Restore(0, 5, 0); _health.Set(1f); if (dose) _dose = 0f; if (fuel) _tank.Litres = _tank.Capacity; _newRallyTrack = null;
        RestoreRallyRun();
        VwCheck(_resumeLoaded && _norms.Step == 2 && _norms.Left == 3 && _norms.Used == 3, $"{track}: resumed: norm {_norms.Step + 1}, {_norms.Left} laps left (one fewer than the 4 saved), {_norms.Used} used");
        VwCheck(Math.Abs(_health.Health - 0.40f) < 1e-3f, $"{track}: resumed: the engine as it was ({_health.Health:0.00})");
        if (dose) VwCheck(Math.Abs(_dose / _rad.Limit - 0.35f) < 1e-3f, $"{track}: resumed: the dose as it was ({_dose / _rad.Limit:0.00} of the limit)");
        if (fuel) VwCheck(Math.Abs(_tank.Litres - 22f) < 1e-2f, $"{track}: resumed: the fuel as it was ({_tank.Litres:0.0} l)");
        RallyResume.Clear(FileDir, _trackId);
        // a save from before this (no car in it, one lap left) still resumes, with its lap
        var old = new RallyResume.Checkpoint(1, 1, 1, 0, 30f, 1, 0) { PassedTimes = new float[NormPlan.Steps], PassedTargets = new float[NormPlan.Steps] };
        RallyResume.Save(FileDir, _trackId, ResumeSignature, old, _rallyFuel);
        _health.Set(0.7f); _newRallyTrack = null;
        RestoreRallyRun();
        VwCheck(_resumeLoaded && _norms.Left == 1 && Math.Abs(_health.Health - 0.7f) < 1e-3f, $"{track}: an old save (no car kept, one lap left): the lap stays and the engine is left as the new run has it");
        RallyResume.Clear(FileDir, _trackId);
        // the revenges already under way come along: started, kept in the checkpoint, the new run ends them, the resume starts them again
        {
            var kinds = new List<string>();
            if (_slurry != null && _tanks == null) { for (int i = 0; i < 60 * 150 && !_slurry.HasDeparted; i++) _slurryAll.Tick(1f / 60f, true, null); if (_slurry.HasDeparted) { StartSlurryRevenge(); kinds.Add("slurry"); } }      // (the tractor is run ahead in time: it leaves its barn some 70 to 90 s into a run)
            if (_tanks != null) { StartTankRevenge(); kinds.Add("tanks"); }
            if (_jets != null || _hinds != null) { StartJetRevenge(); kinds.Add("jet"); }
            VwLog("sit-resume-revenge", ("track", track), ("kinds", string.Join(",", kinds)), ("active", ActiveRevenges()));
            string active = ActiveRevenges();
            if (kinds.Count > 0 && active.Length == 0) VwLog("sit-resume-revenge-skipped", ("track", track), ("why", "no avenger was ready to start"));      // (the jets are not always ready: the other tracks cover them)
            else VwCheck(true, $"{track}: the revenges started ({string.Join(",", kinds)}) are seen as under way: '{active}'");
            if (active.Length > 0)
            {
                var withRevenge = RallyCheckpointNow();
                VwCheck(withRevenge.Revenges == active, $"{track}: the checkpoint keeps them ('{withRevenge.Revenges}')");
                VwCheck(RallyResume.Save(FileDir, _trackId, ResumeSignature, withRevenge, _rallyFuel) == Error.Ok && RallyResume.Load(FileDir, _trackId, ResumeSignature)?.Revenges == active, $"{track}: and the file");
                ResetActors();      // the new run
                VwCheck(ActiveRevenges() == "", $"{track}: the new run starts with none");
                _norms.Restore(0, 5, 0); _newRallyTrack = null;
                RestoreRallyRun();
                if (_state == State.Paused) _state = State.Racing;      // (the test window may have lost focus and paused the race: the revenge starts in a race that runs)
                for (int i = 0; i < 60 * 240 && _resumeRevenges.Count > 0; i++) { if (_state == State.Paused) _state = State.Racing; if (_slurry != null) _slurryAll.Tick(1f / 60f, true, null); ResumedRevengeTick(1f / 60f); if (i % 600 == 599) yield return null; }
                VwLog("sit-resume-slurry", ("pending", _resumeRevenges.Count), ("departed", _slurry?.HasDeparted), ("avenging", _slurry?.Avenging), ("lapsLeft", _slurry?.RevengeLapsLeft), ("wait", _resumeRevengeWait), ("state", _state), ("fateHasCar", FateHasCar), ("world", InWorld(Actors.Slurry)));
                VwCheck(_resumeRevenges.Count == 0 && ActiveRevenges() == active, $"{track}: the resumed run has them under way again: '{ActiveRevenges()}' (wanted '{active}')");
                RallyResume.Clear(FileDir, _trackId);
            }
            else VwCheck(RallyResume.CleanRevenges("jet,tanks,slurry:3,bogus,slurry:0,jet") == "jet,tanks,slurry:3", "the checkpoint reads only the revenges the game knows");
        }
        // the window's last lap: nothing is offered, the way out is straight to the track select
        _norms.Restore(2, 1, 8);
        AskLeaveRally();
        for (int i = 0; i < 10; i++) yield return null;
        VwCheck(!_hud.RallyExitOpen && _state == State.Title, $"{track}: on the last lap of the window the save is not offered: out of the rally at once");
        VwCheck(!RallyResume.Exists(FileDir, _trackId), $"{track}: and nothing was saved");
    }

    // ------------------------------------------------------------------ the lap that ends the run is in the archive
    IEnumerable VwSitInterrupted()
    {
        foreach (var x in VwWaitReady()) yield return x;
        foreach (var x in VrUntil(() => _state == State.Racing && _lapClock > 6f, 60 * 60, "the race is running")) yield return x;
        VwCheck(_lastLapId == null && _timer.LapsDone == 0, "no lap done yet: nothing in the archive");
        float clockAtEnd = _lapClock;
        MissionFailed(ReasonPlan);      // the run ends in the middle of the first lap
        for (int i = 0; i < 90; i++) yield return null;      // (the worker prepares the tape)
        VwCheck(_lastLapId != null && _sessionLapArchives.ContainsKey(_lastLapId), "the lap that ended the run is in the archive");
        var entry = _lapArchiveUsed?.List().FirstOrDefault(e => e.Id == _lastLapId);
        VwCheck(entry != null && entry.Meta.Kind == LapKind.Void && !entry.Meta.Ranked && entry.Meta.LapNumber == 1 && Math.Abs(entry.Meta.LapSeconds - clockAtEnd) < 0.5f && entry.Meta.HasTape,
            $"it is a void lap, never a record: lap {entry?.Meta.LapNumber}, {entry?.Meta.LapSeconds:0.0} s (the lap clock was {clockAtEnd:0.0}), tape {entry?.Meta.HasTape}");
        VwCheck(_lastLapKind == LapKind.Void && _lastLapNumber == 1, "and WATCH LAST LAP is that lap");
        EnterGulag();
        for (int i = 0; i < 30; i++) yield return null;
        VwCheck(_hud.EndWatchHint != null, $"the GULAG screen offers it ('{_hud.EndWatchHint}')");
        foreach (var x in VrTap(Key.F2)) yield return x;
        foreach (var x in VrUntil(() => ReplayViewer is { State: ReplayViewerState.Playing }, 60 * 30, "the viewer plays")) yield return x;
        VwCheck(ReplayViewer?.LapId == _lastLapId && ReplayViewer.LapKindOf == LapKind.Void, "F3 opens the lap in the replay viewer");
        foreach (var x in VrShot("interrupted-lap-viewer")) yield return x;
        foreach (var x in VrTap(Key.Escape)) yield return x;
        foreach (var x in VrUntil(() => ReplayViewer == null, 60 * 20, "the viewer closed")) yield return x;
        VwCheck(_gulag, "and back on the GULAG screen");
    }

    // ------------------------------------------------------------------ WATCH CURRENT LAP
    IEnumerable VwSitThisLap()
    {
        foreach (var x in VwWaitReady()) yield return x;
        foreach (var x in VrUntil(() => _state == State.Racing && _lapClock > 8f, 60 * 60, "the race is running")) yield return x;
        foreach (var x in VrTap(Key.Escape)) yield return x;
        foreach (var x in VrUntil(() => _state == State.Paused, 60 * 5, "the Party Meeting")) yield return x;
        VwCheck(PauseRowsNow[0] == PauseRowId.Continue && PauseRowsNow[1] == PauseRowId.WatchThisLap, $"WATCH CURRENT LAP is the row after CONTINUE ({string.Join(",", PauseRowsNow)})");
        VwCheck(_sessionLapOrder.Count == 0 && _lastLapId == null, "and no real lap is done: WATCH LAST LAP is not offered");
        float clock = _lapClock;
        foreach (var x in VrToRow(PauseRowIndex(PauseRowId.WatchThisLap))) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        foreach (var x in VrUntil(() => ReplayViewer is { State: ReplayViewerState.Playing }, 60 * 30, "the viewer plays")) yield return x;
        var v = ReplayViewer; string first = v?.LapId;
        VwCheck(v != null && v.LapKindOf == LapKind.Void && Math.Abs(v.LapSeconds - clock) < 0.5f, $"the lap so far opens as a void lap ({v?.LapKindOf} {v?.LapSeconds:0.0} s, lap clock {clock:0.0})");
        VwCheck(_lastLapId == null, "it is not the last lap");
        foreach (var x in VrTap(Key.Escape)) yield return x;
        foreach (var x in VrUntil(() => ReplayViewer == null && _state == State.Paused, 60 * 20, "back in the meeting")) yield return x;
        VwCheck(Math.Abs(_lapClock - clock) < 1e-4f, "the live lap did not move");
        foreach (var x in VrToRow(PauseRowIndex(PauseRowId.WatchThisLap))) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        foreach (var x in VrUntil(() => ReplayViewer is { State: ReplayViewerState.Playing }, 60 * 30, "the viewer plays again")) yield return x;
        VwCheck(ReplayViewer != null && ReplayViewer.LapId != first && _sessionLapOrder.Count == 1, $"a new look replaces the old one (one partial lap in the run: {_sessionLapOrder.Count})");
        // KEEP on the lap so far (the viewer's KEEP RECORDING does this): a kept lap survives the next look and the lap's ending
        string keptId = ReplayViewer?.LapId;
        var keepArchive = _sessionLapArchives[keptId];
        var keepRes = keepArchive.Retain(keptId, "THIS LAP SO FAR TEST").GetAwaiter().GetResult();
        VwCheck(keepRes.Ok, $"KEEP on the lap so far: {keepRes.Message}");
        foreach (var x in VrTap(Key.Escape)) yield return x;
        foreach (var x in VrUntil(() => ReplayViewer == null, 60 * 20, "closed")) yield return x;
        foreach (var x in VrTap(Key.Escape)) yield return x;      // CONTINUE
        foreach (var x in VrUntil(() => _state == State.Racing, 60 * 10, "racing again")) yield return x;
        LapArchiveCapture(_lapClock, LapKind.Normal, false, false);      // the lap ends for real
        VwCheck(_partialLapId == null && _sessionLapOrder.Count == 1, "the lap's real ending removes the partial lap from the run");
        VwCheck(keepArchive.List().Any(i => i.Id == keptId && i.Retained), "but the kept one is still in the archive");
    }

    // ------------------------------------------------------------------ the padlock of a newly opened fuel
    IEnumerable VwSitFuelUnlock()
    {
        foreach (var x in VwWaitReady()) yield return x;
        foreach (var x in VrUntil(() => _state == State.Racing && _lapClock > 2f, 60 * 60, "the race is running")) yield return x;
        foreach (var t in FuelTracks(FuelGrade.Petrol)) _campaign.Award(t, NormPlan.Medal.Silver, FuelGrade.Diesel);
        _campaign.PresentFuel(FuelGrade.Diesel);      // (the question has been asked on this profile)
        foreach (var t in Tracks) { _campaign.PresentLaps(t.id, false); _campaign.PresentLaps(t.id, true); }
        VwCheck(!FuelLocked(FuelGrade.Petrol) && !_campaign.FuelPresented(FuelGrade.Petrol) && !_campaign.FuelUnlockShown(FuelGrade.Petrol), "PETROL is open by the medals and has not been unlocked on the card yet");
        ReturnRallyToTrackSelect();
        foreach (var x in VrUntil(() => _fuelCeremony == FuelGrade.Petrol, 60 * 10, "the new fuel's car is shown first")) yield return x;
        VwCheck(_card == TitleCard.None && _fuelUnlock == null, "the car's ceremony comes before the cards");
        foreach (var x in VrUntil(() => _hud.FuelCeremonyCanClose, 60 * 20, "the ceremony can be closed")) yield return x;
        foreach (var x in VrTap(Key.Enter)) yield return x;
        foreach (var x in VrUntil(() => _card == TitleCard.RallyFuel, 60 * 10, "then the fuel card comes up")) yield return x;
        VwCheck(_state == State.Title && _card == TitleCard.RallyFuel && _fuelUnlock == FuelGrade.Petrol && _cardSel == (int)FuelGrade.Petrol, $"back from the run: CHOOSE FUEL on PETROL with the padlock ({_card}, sel {_cardSel}, {_fuelUnlock})");
        foreach (var x in VrTap(Key.Right)) yield return x;
        VwCheck(_cardSel == (int)FuelGrade.Petrol, "the card takes no keys while the padlock is up");
        foreach (var x in VrUntil(() => _fuelUnlockAge >= 1.0f, 60 * 12, "the padlock at 1.0f s")) yield return x;
        foreach (var x in VrShot("fuelunlock-1-shaking")) yield return x;
        foreach (var x in VrUntil(() => _fuelUnlockAge >= 2.0f, 60 * 12, "the padlock at 2.0 s")) yield return x;
        foreach (var x in VrShot("fuelunlock-2-cracked")) yield return x;
        foreach (var x in VrUntil(() => _fuelUnlockAge >= 2.5f, 60 * 12, "the padlock at 2.5 s")) yield return x;
        foreach (var x in VrShot("fuelunlock-3-burst")) yield return x;
        foreach (var x in VrUntil(() => _fuelUnlockAge >= 3.4f, 60 * 12, "the padlock at 3.4 s")) yield return x;
        foreach (var x in VrShot("fuelunlock-4-open")) yield return x;
        foreach (var x in VrUntil(() => _fuelUnlock == null, 60 * 10, "the padlock has finished")) yield return x;
        VwCheck(_campaign.FuelUnlockShown(FuelGrade.Petrol) && _card == TitleCard.RallyFuel && _cardSel == (int)FuelGrade.Petrol, "it is marked shown, and the card is given back on PETROL");
        foreach (var x in VrShot("fuelunlock-5-card")) yield return x;
        ReturnRallyToTrackSelect();
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_fuelUnlock == null, "a second return shows no padlock");
        VwCheck(_card == TitleCard.Track && _rallyFuelPicked && _trackFor == MenuItem.Race && _cardSel == Array.FindIndex(Tracks, t => t.id == _trackId), $"and the rally lands on the track card of its own fuel, on the track just driven ({_card}, sel {_cardSel})");
        foreach (var x in VrTap(Key.Escape)) yield return x;
        for (int i = 0; i < 6; i++) yield return null;
        VwCheck(_card == TitleCard.RallyFuel && _cardSel == (int)_rallyFuel, "Esc on the track card is one step back to CHOOSE FUEL, the same fuel selected");
    }

    // ------------------------------------------------------------------ the body comes back as it was
    IEnumerable VwSitResumeBody()
    {
        foreach (var x in VwWaitReady()) yield return x;
        foreach (var x in VrUntil(() => _state == State.Racing && _lapClock > 2f, 60 * 60, "the race is running")) yield return x;
        var dmg = _carVis.Damage;
        VwCheck(dmg != null && !dmg.IsDamaged, "a whole car to begin with");
        dmg.Hit(14f, new Vector3(0.8f, 0.2f, -1.9f), new Vector3(-0.4f, 0f, 1f), _car, null);
        for (int i = 0; i < 6; i++) yield return null;
        dmg.Hit(16f, new Vector3(-0.9f, 0.1f, 0.5f), new Vector3(1f, 0f, 0f), _car, null);
        VwCheck(dmg.IsDamaged && dmg.Dents > 0, $"the car is dented ({dmg.Dents} dents)");
        int dents = dmg.Dents;
        string text = dmg.SavedText();
        var point = RallyCheckpointNow();
        VwCheck(point.Body == text && text.Length > 20, $"the checkpoint holds the body ({text.Length} characters)");
        VwCheck(RallyResume.Save(FileDir, _trackId, ResumeSignature, point, _rallyFuel) == Error.Ok && RallyResume.Load(FileDir, _trackId, ResumeSignature)?.Body == text, "and the file keeps it");
        dmg.Reset();
        VwCheck(!dmg.IsDamaged && dmg.Dents == 0, "repaired: whole again");
        VwCheck(!dmg.RestoreSaved("garbage") && !dmg.RestoreSaved("0123456789abcdef:AAAA") && !dmg.IsDamaged, "a damaged text or another model's restores nothing");
        VwCheck(dmg.RestoreSaved(text) && dmg.Dents == dents && dmg.IsDamaged, $"the saved body goes back: {dmg.Dents} dents (was {dents})");
        VwCheck(dmg.SavedText() == text, "and it is the same body, bit for bit");
        for (int i = 0; i < 30; i++) yield return null;
        foreach (var x in VrShot("resume-body-restored")) yield return x;
        RallyResume.Clear(FileDir, _trackId);
    }

    // ------------------------------------------------------------------ the Poligon's own look: the low straw light and the dust streak (before / after pictures and the replay checks)
    IEnumerable VwSitPoligon()
    {
        foreach (var x in VwWaitReady()) yield return x;
        foreach (var x in VrUntil(() => _state == State.Racing && _lapClock > 1f, 60 * 60, "the race is running")) yield return x;
        VwCheck(_trackId == "bonus" && _poligonDust != null && _dayNight.PoligonLook, "the Poligon has the light and the dust");
        var dust = _poligonDust; int n = _track.Center.Length, ridge = dust.RidgeIndex;
        VwLog("poligon-site", ("ridge", ridge), ("at", _track.Center[ridge]), ("height", _track.CenterH[ridge]), ("wind", dust.Wind), ("origin", dust.Origin), ("sunAz", _dayNight.PoligonSunAz));
        VwCheck(_track.CenterSurf[ridge] == SurfaceType.Gravel && dust.MudShareAt(dust.Origin) == 0f, "the ridge is gravel and its shoulder is not mud");
        int mudAt = -1;
        for (int i = 10; i < _track.CenterSurf.Length - 10 && mudAt < 0; i++) { bool all = true; for (int k = -8; k <= 8 && all; k += 4) all = _track.CenterSurf[i + k] == SurfaceType.Mud; if (all) mudAt = i; }
        VwCheck(mudAt >= 0 && dust.MudShareAt(_track.Center[mudAt]) > 0.99f, $"the mud is mud to the dust (share {dust.MudShareAt(_track.Center[Math.Max(0, mudAt)])})");

        // the light: the day is the Poligon's, the dusk, the night and the dawn are the common ones, and the cycle is continuous
        var plainDay = DayNight.LookAt(0f); var poligonDay = _dayNight.LookOf(0f);
        VwCheck(poligonDay.SunElev < plainDay.SunElev - 10f && poligonDay.SunColor.B < plainDay.SunColor.B - 0.1f, $"the day light is lower ({poligonDay.SunElev:0} deg against {plainDay.SunElev:0}) and straw-yellow (blue {poligonDay.SunColor.B:0.00})");
        foreach (float lap in new[] { 4.9f, 6f, 8f, 10.2f, 11.3f })
            VwCheck(_dayNight.LookOf(lap).SunElev == DayNight.LookAt(lap).SunElev && _dayNight.LookOf(lap).SunEnergy == DayNight.LookAt(lap).SunEnergy, $"lap {lap}: the dusk, night and dawn are the common ones");
        VwCheck(Math.Abs(_dayNight.LookOf(DayNight.CycleLaps).SunElev - poligonDay.SunElev) < 0.01f && _dayNight.LookOf(1f).SunElev == poligonDay.SunElev, "the day wraps onto itself");
        float prev = _dayNight.LookOf(2f).SunElev; bool smooth = true;
        for (float lap = 2f; lap <= 4.8f; lap += 0.05f) { float e = _dayNight.LookOf(lap).SunElev; smooth &= Math.Abs(e - prev) < 3f; prev = e; }
        VwCheck(smooth, "the sunset eases out of the Poligon's day without a jump");

        // the schedule is a pure function of the clock
        float tMid = PoligonDust.GustStart(0) + 4f;
        var a1 = string.Join(";", PoligonDust.Alive(tMid)); var elsewhere = string.Join(";", PoligonDust.Alive(123.4f)); var a2 = string.Join(";", PoligonDust.Alive(tMid));
        VwCheck(a1.Length > 0 && a1 == a2, $"the same clock gives the same streaks after any other ({a1})");
        float quiet = PoligonDust.GustStart(0) - 1.5f;
        VwCheck(quiet < 0 || !PoligonDust.Alive(quiet).Any(), "before the first gust there is no streak");

        // the pictures, from the chase camera: before (both switches off), then after, at the start straight and on the ridge
        var spots = new (string name, Vector3 pos, float yaw)[]
        {
            ("start", _track.StartPos + Vector3.Up * 0.2f, _track.StartYaw),
            ("ridge-far", _track.CenterPos(((ridge - 22) % n + n) % n) + Vector3.Up * 0.7f, TrackData.YawFor(_track.CenterDir(((ridge - 22) % n + n) % n))),
            ("ridge-near", _track.CenterPos(((ridge - 4) % n + n) % n) + Vector3.Up * 0.7f, TrackData.YawFor(_track.CenterDir(((ridge - 4) % n + n) % n))),
        };
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://media/poligon-trial2-20261123/"));
        OS.Execute("python3", new[] { ProjectSettings.GlobalizePath("res://tools/screenshots/hypr_window_grab.py"), OS.GetProcessId().ToString(), "/tmp/claude-1000/poligon-prepare.png" });      // (floats the window and sizes it to 1280 x 800 physical)
        for (int i = 0; i < 90; i++) yield return null;
        foreach (bool after in new[] { false, true })
        {
            DayNight.PoligonLookEnabled = after; PoligonDust.Enabled = after; _dayNight.Refresh();
            foreach (var (name, pos, yaw) in spots)
            {
                for (int i = 0; i < 100; i++) { _car.Reset(pos, yaw); _hud.ClearMessages(); _lapClock = PoligonDust.GustStart(0) + 2.4f; yield return null; }      // (the car held, the clock held: before and after are the same instant)
                string file = $"{(after ? "trial2" : "original")}-{name}.png";
                var o = new Godot.Collections.Array();
                int code = OS.Execute("python3", new[] { ProjectSettings.GlobalizePath("res://tools/screenshots/hypr_window_grab.py"), OS.GetProcessId().ToString(), ProjectSettings.GlobalizePath("res://media/poligon-trial2-20261123/") + file }, o);
                VwCheck(code == 0, $"the real window picture {file}: {string.Join("", o).Trim()}");
            }
        }
        DayNight.PoligonLookEnabled = true; PoligonDust.Enabled = true; _dayNight.Refresh();
        for (int i = 0; i < 5; i++) { _lapClock = tMid; yield return null; }
        VwCheck(dust.VisiblePanels > 0, $"the streak shows at its clock ({dust.VisiblePanels} panels)");
        _lapClock = Math.Max(0f, quiet); if (quiet < 0) _lapClock = 0f;
        yield return null; yield return null;
        VwCheck(quiet < 0 || dust.VisiblePanels == 0, "and it is gone between the gusts");

        // the replay: the same lap seen through the viewer at the same clocks, a seek forward and back leaves no trace
        _lapClock = 16f;
        foreach (var x in VrUntil(() => _rec.Count > 60 * 14, 60 * 30, "a long enough ghost")) yield return x;
        LapArchiveCapture(_lapClock, LapKind.Normal, false, false);
        for (int i = 0; i < 120; i++) yield return null;
        foreach (var x in VrTap(Key.F2)) yield return x;
        foreach (var x in VrUntil(() => ReplayViewer is { State: ReplayViewerState.Playing }, 60 * 30, "the viewer plays")) yield return x;
        var vs = ReplayViewer.Scene; var vd = vs._poligonDust;
        VwCheck(vd != null && vd != dust, "the viewer's own world has its own dust");
        VwCheck(vs._poligonVeg != null && vs._poligonVeg != _poligonVeg && vs._poligonVeg.HayTufts == _poligonVeg.HayTufts && vs._poligonVeg.Shrubs == _poligonVeg.Shrubs && vs._poligonVeg.Visible, "and the same static vegetation (the trial is scenery: nothing on the tape)");
        vs.ViewerSetPaused(true);      // (paused: the same clock is the same picture to the millimetre)
        vs.ViewerSeek(tMid); for (int i = 0; i < 6; i++) yield return null;
        var poseA = vd.FirstPanelPose; int panelsA = vd.VisiblePanels;
        VwCheck(panelsA > 0 && Math.Abs(vs._replayClock - tMid) < 0.2f, $"the replay shows the streak at the recorded clock ({panelsA} panels, clock {vs._replayClock:0.00})");
        vs.ViewerSeek(Math.Max(0f, quiet)); for (int i = 0; i < 6; i++) yield return null;
        VwCheck(quiet < 0 || vd.VisiblePanels == 0, "a seek to a quiet moment leaves no streak behind");
        vs.ViewerSeek(tMid); for (int i = 0; i < 6; i++) yield return null;
        VwCheck(vd.VisiblePanels == panelsA && vd.FirstPanelPose.Origin.DistanceTo(poseA.Origin) < 0.05f, "and a seek back to the first clock shows exactly the first picture");
        foreach (var x in VrShot("poligon-replay-seek")) yield return x;
        // playing on from a seek: the picture follows the clock (the panels shown are those of the puffs alive at the viewer's own clock), through the gust and past its end
        vs.ViewerSeek(PoligonDust.GustStart(0)); vs.ViewerSetPaused(false);
        int played = 0, withDust = 0; bool fits = true; float last = vs._replayClock, endClock = Math.Min(vs.TitleGhost.Duration - 0.3f, PoligonDust.GustStart(0) + PoligonDust.GustSpan);
        for (int i = 0; i < 60 * 14 && vs._replayClock < endClock; i++)
        {
            yield return null; played++;
            int alive = PoligonDust.Alive(vs._replayClock).Count(), shown = vd.VisiblePanels;
            fits &= shown <= 2 * alive + 2 && vs._replayClock >= last - 1e-3f; last = vs._replayClock;
            if (shown > 0) withDust++;
        }
        VwCheck(fits && withDust > 20, $"playing on: the panels follow the puffs alive at the clock (panels shown in {withDust} of {played} frames, clock {vs._replayClock:0.0})");
        vs.ViewerSetPaused(true);
        foreach (var x in VrTap(Key.Escape)) yield return x;
        foreach (var x in VrUntil(() => ReplayViewer == null, 60 * 20, "the viewer closed")) yield return x;
        VwCheck(IsInstanceValid(dust) && dust.VisiblePanels >= 0, "the drive's own dust is untouched by the replay");
    }

    // the clock at which the autodrive reaches the Poligon's dust ridge (the review drive is timed to meet a gust there)
    IEnumerable VwSitPoligonPass()
    {
        foreach (var x in VwWaitReady()) yield return x;
        int ridge = _poligonDust.RidgeIndex, n = _track.Center.Length; float seen = -1f;
        for (int i = 0; i < 60 * 60 && seen < 0f; i++)
        {
            if (_state == State.Racing)
            {
                var p = new Vector2(_car.Pos.X, _car.Pos.Z);
                int near = 0; float best = float.MaxValue;
                for (int k = 0; k < n; k++) { float d = _track.Center[k].DistanceSquaredTo(p); if (d < best) { best = d; near = k; } }
                if (i % 30 == 0) VwLog("poligon-pass", ("clock", _lapClock), ("index", near), ("kmh", _car.Speed * 3.6f));
                if (near >= ridge - 6 && near <= ridge + 6) seen = _lapClock;
            }
            yield return null;
        }
        VwLog("poligon-pass-at", ("ridge", ridge), ("clock", seen));
        VwCheck(seen > 0f, $"the autodrive reaches the ridge at lap clock {seen:0.0} s");
    }

    // the Poligon's look belongs to the Poligon alone (run on the other tracks as well)
    IEnumerable VwSitPoligonScope()
    {
        foreach (var x in VwWaitReady()) yield return x;
        foreach (var x in VrUntil(() => _state == State.Racing && _lapClock > 2f, 60 * 60, "the race is running")) yield return x;
        VwCheck(_trackId != "bonus" && _poligonDust == null && !_dayNight.PoligonLook, $"{_trackId}: no dust node and no Poligon light");
        foreach (float lap in new[] { 0f, 1f, 2.5f, 3.5f, 5f, 8f, 11f })
        {
            var a = _dayNight.LookOf(lap); var b = DayNight.LookAt(lap);
            VwCheck(a.SunElev == b.SunElev && a.SunEnergy == b.SunEnergy && a.SunColor == b.SunColor && a.AmbientEnergy == b.AmbientEnergy && a.LitMix == b.LitMix, $"{_trackId}: the light at {lap} laps is the common one");
        }
        var scopeCols = (Color[])_terrainMesh.Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Color];
        VwCheck(scopeCols.Length > 600 && scopeCols.All(c => c.A > 0.999f), $"{_trackId}: the terrain's vertex colours carry no dry grass ({scopeCols.Length} vertices, all alpha 1)");
        DayNight.PoligonLookEnabled = true; PoligonDust.Enabled = true;
        VwCheck(PoligonDust.TryCreate(_track) == null && _poligonVeg == null && PoligonVegetation.TryCreate(_track, 100) == null, $"{_trackId}: neither the dust nor the vegetation trial is made for it");
    }

    // ------------------------------------------------------------------ the Poligon's static vegetation trial and its drier grass (before/after pictures from the same place, light and countdown moment)
    IEnumerable VwSitPoligonVeg()
    {
        foreach (var x in VwWaitReady()) yield return x;
        foreach (var x in VrUntil(() => _state == State.Racing && _lapClock > 1f, 60 * 60, "the race is running")) yield return x;
        var veg = _poligonVeg; var dust = _poligonDust;
        VwCheck(veg != null && dust != null, "the Poligon has its vegetation trial");
        VwLog("poligon-veg", ("shrubs", veg.Shrubs), ("birches", veg.Birches), ("hay", veg.HayTufts), ("leeBushes", veg.LeeBushes), ("startIndex", veg.StartIndex), ("side", veg.StartSide), ("wind", veg.Wind));
        VwCheck(veg.Shrubs >= 6 && veg.Birches >= 3 && veg.HayTufts >= 20 && veg.LeeBushes >= 3, $"enough of each: {veg.Shrubs} shrubs, {veg.Birches} birches, {veg.HayTufts} hay tufts, {veg.LeeBushes} lee bushes");
        VwCheck(veg.GetChildren().All(c => c.Name != "StartPatches"), "the soft patch overlay (StartPatches) is gone");
        int n = _track.Center.Length, ridge = dust.RidgeIndex; float crest = _track.CenterH[ridge];
        float Gap(Vector3 p) { float best = float.MaxValue; var q = new Vector2(p.X, p.Z); for (int i = 0; i < n; i++) best = Math.Min(best, _track.Center[i].DistanceTo(q) - _track.HalfWidth[i]); return best; }
        VwCheck(veg.ShrubPositions.All(p => Gap(p) >= 9f) && veg.BirchPositions.All(p => Gap(p) >= 9f), "the start straight's shrubs and birches stay off the road's edge (>= 9 m)");
        VwCheck(veg.TuftPositions.All(p => Gap(p) >= 0.8f) && veg.BushPositions.All(p => Gap(p) >= 4f), "the ridge's hay stays off the road (>= 0.8 m) and the lee bushes well clear (>= 4 m)");
        bool noMud = veg.TuftPositions.Concat(veg.ShrubPositions).Concat(veg.BushPositions).All(p => _track.TileSurface((int)(p.X / _track.Tile), (int)(p.Z / _track.Tile)) is not (SurfaceType.Mud or SurfaceType.Water or SurfaceType.Puddle or SurfaceType.Concrete or SurfaceType.Gravel));
        VwCheck(noMud, "nothing stands on the mud, the water, the concrete or the gravel of the road itself");
        var ridgeXZ = new Vector2(_track.Center[ridge].X, _track.Center[ridge].Y);
        VwCheck(veg.ShrubPositions.Concat(veg.BirchPositions).Concat(veg.BushPositions).All(p => new Vector2(p.X, p.Z).DistanceTo(ridgeXZ) > 22f || p.Y < crest - 0.5f), "the crest stays open: no shrub, birch or bush up on it");
        var hay = veg.GetNode<MultiMeshInstance3D>("RidgeHay").Multimesh; bool leans = true; float worst = 1f;
        for (int i = 0; i < hay.InstanceCount; i++)
        {
            var lean = hay.GetInstanceTransform(i).Basis.X; var l2 = new Vector2(lean.X, lean.Z).Normalized(); float dot = l2.Dot(veg.Wind); worst = Math.Min(worst, dot); leans &= dot > 0.97f;
        }
        VwCheck(leans, $"all {hay.InstanceCount} hay tufts lean the way the windsock blows (worst cosine {worst:0.000}, wind {veg.Wind})");
        var again = PoligonVegetation.TryCreate(_track, ridge); var again2 = PoligonVegetation.TryCreate(_track, ridge);
        VwCheck(again != null && again2 != null && again.TuftPositions.SequenceEqual(again2.TuftPositions) && again.BirchPositions.SequenceEqual(again2.BirchPositions) && again.ShrubPositions.SequenceEqual(again2.ShrubPositions), "built twice from the same data it is the same scenery (the world's own copy was built before the actors' obstacles existed: it may differ by a tuft or two)");
        again?.Free(); again2?.Free();
        int obstacles = _track.Obstacles.Count;
        PoligonVegetation.Enabled = false; veg.Apply(); VwCheck(!veg.Visible, "the trial's switch hides it");
        PoligonVegetation.Enabled = true; veg.Apply();
        VwCheck(_track.Obstacles.Count == obstacles, $"no obstacle was made for any of it ({obstacles} obstacles as before)");

        // ---- the drier grass: measured on the map and on the terrain mesh the game draws
        var map = new DryGrassMap(_track);
        int si = veg.StartIndex; var left = new List<float>(); var rightSide = new List<float>(); var far = new List<float>(); bool surfacesOk = true, nonGrassZero = true;
        for (int k = -10; k <= 28; k += 2)
            for (float off = 2f; off <= 12f; off += 2f)
            {
                int i = ((si + k) % n + n) % n; Vector2 c = _track.Center[i], d = _track.CenterDir(i), q = new(-d.Y, d.X);
                var pl = c + q * (_track.HalfWidth[i] + off); var pr = c - q * (_track.HalfWidth[i] + off);
                int ixl = (int)(pl.X / _track.Tile), izl = (int)(pl.Y / _track.Tile), ixr = (int)(pr.X / _track.Tile), izr = (int)(pr.Y / _track.Tile);
                if (_track.TileSurface(ixl, izl) == SurfaceType.Grass) { left.Add(map.At(ixl, izl, (int)SurfaceType.Grass)); surfacesOk &= _track.Sample(pl.X, pl.Y).Surface == SurfaceType.Grass; }
                if (_track.TileSurface(ixr, izr) == SurfaceType.Grass) rightSide.Add(map.At(ixr, izr, (int)SurfaceType.Grass));
            }
        for (int i = 0; i < n; i += 9) { var c = _track.Center[i]; int ix = (int)(c.X / _track.Tile), iz = (int)(c.Y / _track.Tile); nonGrassZero &= map.At(ix, iz, (int)_track.TileSurface(ix, iz)) == 0f || _track.TileSurface(ix, iz) == SurfaceType.Grass; }
        for (int ix = 8; ix < _track.N - 8; ix += 5) for (int iz = 8; iz < _track.N - 8; iz += 5)
            if (_track.TileSurface(ix, iz) == SurfaceType.Grass && new Vector2((ix + .5f) * _track.Tile, (iz + .5f) * _track.Tile).DistanceTo(_track.Center[si]) > 70f && map.ClearedWeight((ix + .5f) * _track.Tile, (iz + .5f) * _track.Tile) == 0f) far.Add(map.At(ix, iz, (int)SurfaceType.Grass));      // (the earth where a tree was left out is the one exception)
        float lm = left.Average(), rm = rightSide.Average(), fm = far.Average();
        VwLog("poligon-dry", ("leftVerge", lm), ("leftMin", left.Min()), ("leftMax", left.Max()), ("otherSide", rm), ("farGrass", fm), ("farMax", far.Max()), ("tiles", left.Count));
        VwCheck(left.Count >= 20 && lm > 0.7f && left.Min() >= 0.5f * left.Max(), $"the worn left start verge is the driest ({left.Count} grass tiles, mean {lm:0.00}, from {left.Min():0.00} to {left.Max():0.00}: irregular, but no clean patch)");
        VwCheck(rm < 0.5f * lm && fm < 0.5f * lm && far.Max() <= DryGrassMap.Base + 0.001f, $"the grass elsewhere leans only a little dry (the other side {rm:0.00}, far away {fm:0.00}, at most {far.Max():0.00})");
        VwCheck(surfacesOk && nonGrassZero, "only grass tiles are affected, and the tiles' surfaces are the grass they were (grip is read from them, not from the drawing)");
        // the terrain mesh the game draws carries it in the vertex colour's alpha (1 - amount); first chunk, tile by tile
        var arrays = _terrainMesh.Mesh.SurfaceGetArrays(0); var cols = (Color[])arrays[(int)Mesh.ArrayType.Color]; int chunk = Math.Max(1, (int)MathF.Floor(32f / _track.Tile)), match = 0, total = 0;
        for (int iz = 0; iz < chunk; iz++) for (int ix = 0; ix < chunk; ix++)
        {
            int start = (iz * chunk + ix) * 6; float want = map.At(ix, iz, (int)_track.Surf[iz * _track.N + ix]);
            total++; if (Math.Abs(cols[start].A - (1f - want)) < 0.01f && Math.Abs(cols[start + 5].A - (1f - want)) < 0.01f) match++;
        }
        VwCheck(match == total && total > 100, $"the drawn terrain's vertex colours carry the map ({match} of {total} tiles of the first chunk)");

        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://media/poligon-dry-20261123/"));
        OS.Execute("python3", new[] { ProjectSettings.GlobalizePath("res://tools/screenshots/hypr_window_grab.py"), OS.GetProcessId().ToString(), "/tmp/claude-1000/poligon-prepare.png" });
        for (int i = 0; i < 90; i++) yield return null;
        void Grab(string file)
        {
            var o = new Godot.Collections.Array();
            int code = OS.Execute("python3", new[] { ProjectSettings.GlobalizePath("res://tools/screenshots/hypr_window_grab.py"), OS.GetProcessId().ToString(), ProjectSettings.GlobalizePath("res://media/poligon-dry-20261123/") + file }, o);
            VwCheck(code == 0, $"the real window picture {file}: {string.Join("", o).Trim()}");
        }
        // the start straight as the countdown shows it (the ordinary camera, the car on the line): ONE countdown held still, the grass switched between the two pictures, so the camera, the light and the
        // moment are the same to the pixel
        EnterCountdown();
        foreach (bool after in new[] { false, true })
        {
            TerrainMesh.SetDryScale(_terrainMesh, after ? 1f : 0f);
            for (int i = 0; i < 240; i++) { _countdown = 3f; _hud.ClearMessages(); yield return null; }
            Grab($"{(after ? "after" : "before")}-start-countdown.png");
        }
        TerrainMesh.SetDryScale(_terrainMesh, 1f);
    }

    // the same view of the Ilyich's field belts (the reference), ordinary camera
    IEnumerable VwSitPoligonIlyichRef()
    {
        foreach (var x in VwWaitReady()) yield return x;
        foreach (var x in VrUntil(() => _state == State.Racing && _lapClock > 1f, 60 * 60, "the race is running")) yield return x;
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://media/poligon-veg-20261123/"));
        OS.Execute("python3", new[] { ProjectSettings.GlobalizePath("res://tools/screenshots/hypr_window_grab.py"), OS.GetProcessId().ToString(), "/tmp/claude-1000/poligon-prepare.png" });
        for (int i = 0; i < 90; i++) yield return null;
        // in the field south of the windbreak belt at z = 85 (x 122..248), looking at it: the belt, the field and the open grass as the Ilyich has them
        float bx = 190f, bz = 100f; float gy = _track.Sample(bx, bz).Height;
        for (int i = 0; i < 300; i++) { _car.Reset(new Vector3(bx, gy + 0.8f, bz), TrackData.YawFor(new Vector2(0f, -1f))); _hud.ClearMessages(); yield return null; }
        var o = new Godot.Collections.Array();
        int code = OS.Execute("python3", new[] { ProjectSettings.GlobalizePath("res://tools/screenshots/hypr_window_grab.py"), OS.GetProcessId().ToString(), ProjectSettings.GlobalizePath("res://media/poligon-veg-20261123/") + "ilyich-field-belt-reference.png" }, o);
        VwLog("ilyich-ref", ("at", new Vector2(bx, bz)));
        VwCheck(code == 0, $"the Ilyich reference picture: {string.Join("", o).Trim()}");
    }

    // ------------------------------------------------------------------ the Poligon's cleared ground: the live world's trees, a replayed fall of a tree by an old id, and the before/after pictures (the same grass tone in both)
    IEnumerable VwSitPoligonTrees()
    {
        foreach (var x in VwWaitReady()) yield return x;
        foreach (var x in VrUntil(() => _state == State.Racing && _lapClock > 1f, 60 * 60, "the race is running")) yield return x;
        bool cleared = BonusTrees.Enabled; string tag = cleared ? "after" : "before";
        int Count(string type) => _track.Props.Count(p => p.Type == type);
        VwLog("poligon-trees", ("cleared", cleared), ("birch", Count("birch")), ("pine", Count("pine")), ("spruce", Count("spruce")), ("clearedSpots", _track.ClearedTrees.Count), ("obstacles", _track.Obstacles.Count));
        VwCheck(cleared ? Count("spruce") == 0 && Count("pine") <= 8 && _track.ClearedTrees.Count > 40 : Count("spruce") > 20 && _track.ClearedTrees.Count == 0, $"{tag}: the live world has {Count("birch")} birches, {Count("pine")} pines, {Count("spruce")} spruces ({_track.ClearedTrees.Count} spots cleared)");
        if (cleared)
        {
            // an older tape's broken-tree ids: a birch that stands, and the id of a tree that is gone: the first falls, the second is ignored and nothing throws
            int standing = -1, gone = -1;
            foreach (var (prop, obstacle) in _track.PropObstacle) if (_track.Props[prop].Type == "birch") { standing = obstacle; break; }
            for (int i = 0; i < _track.Obstacles.Count && gone < 0; i++) if (_track.Obstacles[i].Center.X < -9000f && _track.Obstacles[i].Radius < 0.2f && i > 100) gone = i;
            _scenery.ReplaySet(new[] { standing, gone }, null);
            var fallen = _scenery.FallenPoses();
            VwCheck(fallen.Count == 1 && fallen[0].Id == standing, $"an old tape's ids: the birch {standing} falls, the id {gone} of a tree that is gone is ignored ({fallen.Count} fallen)");
            _scenery.ReplaySet(new int[0], null);
            VwCheck(_scenery.FallenPoses().Count == 0, "and an empty set stands everything up again");
        }
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://media/poligon-trees-20261123/"));
        OS.Execute("python3", new[] { ProjectSettings.GlobalizePath("res://tools/screenshots/hypr_window_grab.py"), OS.GetProcessId().ToString(), "/tmp/claude-1000/poligon-prepare.png" });
        for (int i = 0; i < 90; i++) yield return null;
        void Grab(string file)
        {
            var o = new Godot.Collections.Array();
            int code = OS.Execute("python3", new[] { ProjectSettings.GlobalizePath("res://tools/screenshots/hypr_window_grab.py"), OS.GetProcessId().ToString(), ProjectSettings.GlobalizePath("res://media/poligon-trees-20261123/") + file }, o);
            VwCheck(code == 0, $"the real window picture {file}: {string.Join("", o).Trim()}");
        }
        int n = _track.Center.Length, ridge = _poligonDust.RidgeIndex;
        EnterCountdown();
        for (int i = 0; i < 240; i++) { _countdown = 3f; _hud.ClearMessages(); yield return null; }
        Grab($"{tag}-start-countdown.png");
        _state = State.Racing; _hud.Mode = HudMode.Race;
        foreach (var (name, idx) in new[] { ("ridge-near", ((ridge - 4) % n + n) % n), ("ridge-far", ((ridge - 22) % n + n) % n), ("ridge-wide", ((ridge - 40) % n + n) % n) })
        {
            for (int i = 0; i < 300; i++) { _car.Reset(_track.CenterPos(idx) + Vector3.Up * 0.7f, TrackData.YawFor(_track.CenterDir(idx))); _hud.ClearMessages(); _lapClock = 14f; yield return null; }
            Grab($"{tag}-{name}.png");
        }
        // close-ups where trees stood and were left out: the nearest ones to the ridge and to the start line (the cleared world's spots, hard-coded so the old world is shot at the same places): the car stands
        // 6 m from the spot, towards the road, and looks at it
        foreach (var (name, spot) in new[] { ("spot-ridge", new Vector2(37f, 258f)), ("spot-start", new Vector2(45f, 52f)) })
        {
            int ci = 0; float best = float.MaxValue; for (int i = 0; i < n; i++) { float d = _track.Center[i].DistanceTo(spot); if (d < best) { best = d; ci = i; } }
            Vector2 toRoad = (_track.Center[ci] - spot).Normalized(); Vector2 car = spot + toRoad * 6f;
            float gy = _track.Sample(car.X, car.Y).Height;
            for (int i = 0; i < 300; i++) { _car.Reset(new Vector3(car.X, gy + 0.8f, car.Y), TrackData.YawFor(-toRoad)); _hud.ClearMessages(); _lapClock = 14f; yield return null; }
            Grab($"{tag}-{name}.png");
        }
    }
}
