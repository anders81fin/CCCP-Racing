using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// The world tape's effects in the game (docs/replay-tape.md, phase 3). Recording: the actors that make one-shot effects (the Hinds and the battery, the tanks, the
/// BTRs) and the breakables tell the scene where they spawn the live effect (<see cref="ITapeFxHost"/>); the scene stamps the lap time, logs the event while a lap is
/// recorded and remembers the ones with a life, so a lap that starts while they run (the previous lap's smoke, fires, warning lamps) records them as already
/// going and what is already broken as broken at 0. Playback: the sink hands every event to <see cref="TapeFxPlayer"/>.
/// </summary>
public partial class RaceScene
{
    TapeFxPlayer _tapeFx;
    TapeEmitters _tapeEmit;      // the covered vehicles' per-step dust and spray (TapeEmitters.cs), made while the replay runs
    Node3D _tapeFxRoot;
    float _tapeWorld;                                   // the physics clock: advances every unpaused step, across laps
    readonly List<(TapeEvent E, float Start)> _tapeFxLive = new(128);       // effects with a life that have begun (Start on the physics clock)
    readonly Dictionary<int, (Vector3 Vel, Vector3 At)> _tapeBrokenHow = new();     // how each breakable went down, for the lap that starts with it broken
    readonly List<int> _tapeBrokenIds = new();
    bool _tapeWood;
    bool _tapeBreakables;                               // the tape being replayed recorded the breakables: the replay does not guess them from the car
    // statistics (--tape-debug)
    long _tapeFxAlloc; int _tapeFxCalls, _tapeFxEvents, _tapeFxCarried, _tapeFxBrokenAtStart;
    readonly SortedDictionary<string, int> _tapeFxByKind = new();

    /// <summary>One actor's view of the effects it makes: its key is the events' Source.</summary>
    sealed class TapeFxHost : ITapeFxHost
    {
        readonly RaceScene _s; readonly string _key;
        public TapeFxHost(RaceScene s, string key) { _s = s; _key = key; }
        public void Fx(in TapeEvent e) => _s.TapeFxEvent(e, _key);
    }

    /// <summary>Once the world is built: the player's visuals, the actors' hosts, the breakables' news.</summary>
    void TapeFxInit()
    {
        _tapeFxRoot = new Node3D { Name = "TapeFx" };
        AddChild(_tapeFxRoot);
        _tapeFx = new TapeFxPlayer(_fx, _track, _scenery, _tapeFxRoot)
        {
            Feel = (e, age) =>
            {
                if (e.Kind == TapeFxKind.BabushkaLaunch) { float k = e.Param; _snd.Thump(0.3f + 0.4f * k); _snd.Impact(2f + 3f * k); _cam.Shake(0.15f + 0.35f * k); }
                else if (e.Kind == TapeFxKind.WoodBreak && age < .1f) _snd.Crack(Math.Clamp(e.Velocity.Length() / 20f, .35f, 1f));
                else if (e.Kind == TapeFxKind.CanisterBurst) FuelSplash(e.Pos, age < .3f);
                else if (e.Kind == TapeFxKind.BabushkaLens) { _snd.Thump(1f); _snd.Impact(6f); _cam.Shake(1.2f); }
                else if (e.Kind == TapeFxKind.MechanicWhack) _cam.Shake(e.Param > .5f ? .5f : .25f);
            },
            Lens = (e, age) => _hud?.RestoreTapeLensEvent(e, age),
            Radar = (e, age) => _trap?.RestoreTapeReading(e, age),
        };
        _tapeFx.Wood = _wood;
        _tapeEmit = new TapeEmitters(_fx, _track);
        if (_tapeRec == null) return;       // (recording off: nothing reports)
        foreach (var (a, key) in _tapeKeys)
        {
            if (a.Node == null) continue;
            if (a.Node == _hindsAll) _hindsAll.TapeFx = new TapeFxHost(this, key);
            else if (a.Node == _tanksAll) _tanksAll.TapeFx = new TapeFxHost(this, key);
            else if (a.Node == _btrAll) _btrAll.TapeFx = new TapeFxHost(this, key);
            else if (a.Node == _jetsAll) _jetsAll.TapeFx = new TapeFxHost(this, key);
            else if (a.Node == _busAll) _busAll.TapeFx = new TapeFxHost(this, key);
            else if (a.Node == _babushkasAll) _babushkasAll.TapeFx = new TapeFxHost(this, key);
            else if (a.Node == _drunksAll) _drunksAll.TapeFx = new TapeFxHost(this, key);
            else if (a.Node == _trap) _trap.TapeFx = new TapeFxHost(this, key);
        }
        if (_busAll != null) _busAll.TapeReset += () => _tapeFxLive.RemoveAll(x => x.E.Kind == TapeFxKind.BusFire);
        _wood.TapeBroken += (id, hit, velocity, state) => TapeFxEvent(new TapeEvent(0, TapeEventType.Effect,
            Source: "None:WoodFences", Kind: TapeFxKind.WoodBreak, Parent: state.ToString("X16", CultureInfo.InvariantCulture),
            Pos: new Vector3(hit.X, 0, hit.Y), Velocity: velocity, Param: id, Life: float.MaxValue), "None:WoodFences");
        _wood.Restarted += () => _tapeFxLive.RemoveAll(x => x.E.Kind == TapeFxKind.WoodBreak);
        _scenery.Broken += (id, vel, at) =>
        {
            _tapeBrokenHow[id] = (vel, at);
            TapeFxEvent(new TapeEvent(0f, TapeEventType.Effect, Kind: TapeFxKind.Breakable, Effect: TapeEffect.BreakableBroken, Pos: at, Dir: vel, Param: id), null);
        };
        _scenery.Restarted += () => _tapeBrokenHow.Clear();
    }

    /// <summary>An actor made an effect that began <c>e.Time</c> seconds ago (0: now). Reads only; allocation-free except when a log block fills.</summary>
    void TapeFxEvent(in TapeEvent e, string source)
    {
        if (_tapeRec == null) return;
        long a0 = _tapeDebug ? GC.GetAllocatedBytesForCurrentThread() : 0;
        float ago = e.Time;
        // Fence breaks and bus ignition follow their current step's integration, before the lap clock advances.
        // Their newborn debris/fire therefore belongs to the end-of-step ghost timestamp.
        // _tapeWorld already advanced in TapeTestTick, so the carried start below
        // must stay at _tapeWorld (adding the step there would shift it twice).
        float phase = e.Kind is TapeFxKind.WoodBreak or TapeFxKind.BusFire or TapeFxKind.BabushkaLens or TapeFxKind.MechanicWhack ? (float)GetPhysicsProcessDeltaTime() : 0f;
        var ev = e with { Time = _lapClock + phase - ago, Source = source };
        if (_tapeDebug && ev.Kind is TapeFxKind.BabushkaLaunch or TapeFxKind.CanisterBurst or TapeFxKind.WoodBreak or TapeFxKind.BusFire) TapeLog($"fx event {ev.Kind} at {ev.Time:F2} (recording {_tapeOn}, from {source})");
        if (_tapeOn)
        {
            _tapeRec.Event(ev);
            if (_tapeDebug) { _tapeFxEvents++; _tapeFxByKind[ev.Kind ?? "?"] = _tapeFxByKind.GetValueOrDefault(ev.Kind ?? "?") + 1; }
        }
        if (ev.Life > ago)      // still going: a lap that starts now records it as already going
        {
            if (_tapeFxLive.Count == _tapeFxLive.Capacity) TapeFxPrune();
            _tapeFxLive.Add((ev, _tapeWorld - ago));
        }
        if (_tapeDebug) { _tapeFxAlloc += GC.GetAllocatedBytesForCurrentThread() - a0; _tapeFxCalls++; }
    }

    void TapeFxPrune()
    {
        int w = 0;
        for (int i = 0; i < _tapeFxLive.Count; i++)
            if (_tapeFxLive[i].Start + _tapeFxLive[i].E.Life > _tapeWorld) _tapeFxLive[w++] = _tapeFxLive[i];
        _tapeFxLive.RemoveRange(w, _tapeFxLive.Count - w);
    }

    /// <summary>The world was reset (a race or the title starts): the effects it had are gone.</summary>
    void TapeFxReset() { _tapeFxLive.Clear(); _tapeBrokenHow.Clear(); }

    /// <summary>A lap starts (after <c>Begin</c>): the marker, the effects still going, what is already broken. They are at time 0 or before it, so the first keyframe holds them.</summary>
    void TapeFxAlive()
    {
        _tapeFxByKind.Clear(); _tapeFxEvents = 0; _tapeFxAlloc = 0; _tapeFxCalls = 0;
        _tapeRec.Event(new TapeEvent(0f, TapeEventType.Effect, Kind: TapeFxKind.Marker, Effect: TapeEffect.Marker));
        TapeFxPrune();
        _tapeFxCarried = _tapeFxLive.Count;
        foreach (var (ev, start) in _tapeFxLive) _tapeRec.Event(ev with { Time = start - _tapeWorld });
        _scenery.BrokenIds(_tapeBrokenIds);
        _tapeFxBrokenAtStart = _tapeBrokenIds.Count;
        foreach (int id in _tapeBrokenIds)
        {
            var how = _tapeBrokenHow.GetValueOrDefault(id);
            // (at the time it broke, a negative one: a tree that was still falling when the lap began is that far into its fall in the first frames, as a seek shows it)
            _tapeRec.Event(new TapeEvent(-_scenery.BreakAge(id), TapeEventType.Effect, Kind: TapeFxKind.Breakable, Effect: TapeEffect.BreakableBroken, Pos: how.At, Dir: how.Vel, Param: id));
        }
    }

    // test flag --tape-blast=A,B: lap k (from 0) fells the nearest standing tree or pole to the car A (B) seconds into it (a breakable that breaks at a known time)
    float[] _tapeBlast = Array.Empty<float>();
    bool _tapeBlasted;
    int _tapeBrokenSeen, _tapeLyingSeen;

    void TapeBlastTick()
    {
        if (_tapeBlast.Length <= _tapeLaps || _tapeBlasted || _lapClock < _tapeBlast[_tapeLaps]) return;
        _tapeBlasted = true;
        if (_scenery.NearestStanding(_car.Pos) is Vector3 p)
        {
            int n = _scenery.Blast(p, 1f);
            TapeLog($"blast: lap {_tapeLaps + 1} at {_lapClock:F2} s felled {n} at ({p.X:F0},{p.Z:F0}), {_car.Pos.DistanceTo(p):F0} m from the car");
        }
    }

    string TapeFxKinds() => string.Join(",", _tapeFxByKind.Select(kv => $"{kv.Key}={kv.Value}"));
    string TapeFxMade() => string.Join(",", _tapeFx.Made.Select(kv => $"{kv.Key}={kv.Value}"));
}
