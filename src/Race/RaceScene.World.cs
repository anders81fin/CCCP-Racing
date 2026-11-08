using System;
using System.Collections.Generic;
using CCCPRacing.Car;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// STAKHANOV LAPS run on an empty road: no bus, Kozlik, tanks, tractor, drunks, babushkas or Balalaikas,
/// no radar in the radio and no dents in the car. The actors are reset, hidden, stopped (their
/// sounds pause with them) and their collision circles parked off the map; during such a run
/// their fields are null, so every "not on this track" check in the race skips them. The KGB
/// officers, the rescue tractor and everything static stay; the liaison officer and the fuel depot,
/// which only the rally's norms and fuel need, go with the actors. The title and the rally have it all.
/// The driving school (RaceScene.School.cs) keeps only the actors its lessons need (Actors.School).
/// </summary>
public partial class RaceScene
{
    /// <summary>The actors that can leave the world (ApplyWorld).</summary>
    [Flags]
    enum Actors
    {
        None = 0, Tractor = 1, Babushkas = 2, Drunks = 4, Bus = 8, Kozlik = 16, Jets = 32, Tanks = 64, Liaison = 128, Depot = 256, Ploughs = 512, Column = 1024, Zone = 2048, Slurry = 4096,
        Details = 8192, Btr = 16384,
        All = 32767,
        /// <summary>The rally school: babushkas, the mechanic, the liaison officer and the depot; nothing that shoots or runs you down.</summary>
        School = Babushkas | Drunks | Liaison | Depot,
    }

    // Each actor as built (null: not on this track), and in the world now (null also while out of it).
    DrunkTractor _tractorAll;
    SlurryTractor _slurryAll;
    Babushkas _babushkasAll;
    DrunkMen _drunksAll;
    SovietBus _busAll;
    KozlikPatrol _kozlikAll;
    BalalaikaSorties _jetsAll;
    HindSorties _hindsAll;
    BonusExerciseControl _bonusExercise;
    TankBattle _tanksAll;
    BtrPatrols _btrAll;
    CampGate _campGate;                     // the bonus camp's barrier boom
    SiberiaDetails _detailsAll;
    SnowPloughs _ploughsAll;                // the winter road's snowploughs
    PrisonerColumn _columnAll;              // ...the labour detail on the march
    ZoneLife _zoneAll;                      // ATOMGRAD-4's horses, lunokhod and train (the catfish too)
    DrunkTractor _tractor => InWorld(Actors.Tractor) ? _tractorAll : null;
    SlurryTractor _slurry => InWorld(Actors.Slurry) ? _slurryAll : null;
    Babushkas _babushkas => InWorld(Actors.Babushkas) ? _babushkasAll : null;
    DrunkMen _drunks => InWorld(Actors.Drunks) ? _drunksAll : null;
    SovietBus _bus => InWorld(Actors.Bus) ? _busAll : null;
    KozlikPatrol _kozlik => InWorld(Actors.Kozlik) ? _kozlikAll : null;
    BalalaikaSorties _jets => InWorld(Actors.Jets) ? _jetsAll : null;
    HindSorties _hinds => InWorld(Actors.Jets) ? _hindsAll : null;
    TankBattle _tanks => InWorld(Actors.Tanks) ? _tanksAll : null;
    SiberiaDetails _details => InWorld(Actors.Details) ? _detailsAll : null;
    SnowPloughs _ploughs => InWorld(Actors.Ploughs) ? _ploughsAll : null;
    PrisonerColumn _column => InWorld(Actors.Column) ? _columnAll : null;
    ZoneLife _zone => InWorld(Actors.Zone) ? _zoneAll : null;

    Actors _worldNow = Actors.All;
    bool _emptyWorld => _worldNow == Actors.None;
    bool InWorld(Actors who) => who == Actors.None || (_worldNow & who) != 0;
    // The actors' collision circles as built, restored when they come back.
    readonly List<(Actors who, int index, Obstacle o)> _worldObstacles = new();

    /// <summary>A moving part of the world as the race drives it: what it sees of the player in a step,
    /// how it starts a run over and how it leaves the world. All of them in one list (ListActors).</summary>
    sealed class WorldActor
    {
        public Actors Who;                  // Actors.None: never leaves the world
        public Node3D Node;                 // hidden, stopped and silenced while out of the world
        public Action<float, bool> Tick;    // (dt, replaying the title's ghost)
        public bool AfterCar;               // stepped after the car, with its impact
        public Action Reset;                // at each start (PlaceAtStart); null: keeps its state
        public Action Leave;                // on leaving the world (null: Reset)
        public Action<bool> Show;           // in or out of the world, instead of hiding Node
        public Func<IEnumerable<(Vector3 pos, string text)>> Shouts;   // speech bubbles
        public Action<bool> TapeDrive;      // the title replay's tape shows this actor's moving models: it keeps ticking, but makes none of its own (null: it is skipped whole when covered)
        public Func<Vector3> Focus;         // meaningful observer position when an actor root stays at world origin
    }
    readonly List<WorldActor> _actors = new();

    /// <summary>After the actors' Init: list them and remember each one's obstacle slots [from, to).</summary>
    void KeepWorld(params (Actors who, int from, int to)[] obstacles)
    {
        ListActors();
        BuildTapeIndex();      // RaceScene.Tape.cs: once, before anything has moved
        foreach (var (who, from, to) in obstacles)
            for (int i = from; i < to; i++) _worldObstacles.Add((who, i, _track.Obstacles[i]));
    }

    /// <summary>Every actor, in the order they step (before the car, then after it). Here, and only here,
    /// each one is told what it may see of the player: on the title the car is the autopilot's or the ghost's.</summary>
    void ListActors()
    {
        void Add(object actor, Func<WorldActor> a) { if (actor != null) _actors.Add(a()); }    // (not on this track: not listed)
        CarSim Seen() => _state == State.Title ? null : _car;    // (a replay is on the title too)
        Add(_tractorAll, () => new() { Who = Actors.Tractor, Node = _tractorAll, Reset = _tractorAll.Reset,
            Tick = (dt, replay) => _tractorAll.Tick(dt, !replay && _state == State.Racing, _bus) });
        Add(_slurryAll, () => new() { Who = Actors.Slurry, Node = _slurryAll, Reset = _slurryAll.Reset, TapeDrive = on => _slurryAll.TapeDriven = on,
            Tick = (dt, replay) => _slurryAll.Tick(dt, !replay && _state == State.Racing, _bus) });
        Add(_busAll, () => new() { Who = Actors.Bus, Node = _busAll, Reset = _busAll.Reset, TapeDrive = on => _busAll.TapeDriven = on, Shouts = _busAll.ActiveShouts,
            Tick = (dt, replay) => _busAll.Tick(dt, _scEnvironment ? null : Seen(), !replay && WorldHorn, _tractor, _slurry) });
        Add(_ploughsAll, () => new() { Who = Actors.Ploughs, Node = _ploughsAll, Reset = _ploughsAll.Reset, Tick = (dt, _) => _ploughsAll.Tick(dt, Seen()), TapeDrive = on => _ploughsAll.TapeDriven = on });
        Add(_columnAll, () => new() { Who = Actors.Column, Node = _columnAll, Reset = _columnAll.Reset, Shouts = _columnAll.ActiveShouts,
            Tick = (dt, replay) => _columnAll.Tick(dt, replay ? null : _car) });
        Add(_zoneAll, () => new() { Who = Actors.Zone, Node = _zoneAll, Reset = _zoneAll.Reset, Tick = (dt, _) => _zoneAll.Tick(dt, Seen()) });
        Add(_kozlikAll, () => new() { Who = Actors.Kozlik, Node = _kozlikAll, Reset = _kozlikAll.Reset, Shouts = _kozlikAll.ActiveShouts,
            Tick = (dt, _) => _kozlikAll.Tick(dt, _car, _maxImpact) });
        Add(_jetsAll, () => new() { Who = Actors.Jets, Node = _jetsAll, Reset = _jetsAll.Reset, TapeDrive = on => _jetsAll.TapeDriven = on, Tick = (dt, _) => _jetsAll.Tick(dt, _car) });
        Add(_hindsAll, () => new() { Who = Actors.Jets, Node = _hindsAll, Reset = _hindsAll.Reset, TapeDrive = on => _hindsAll.TapeDriven = on, Tick = (dt, _) => _hindsAll.Tick(dt, _state == State.Racing ? _car : null) });
        // The airbase crew sees the ghost's car in a replay, not the autopilot's on the title.
        Add(_detailsAll, () => new() { Who = Actors.Details, Node = _detailsAll, Show = _detailsAll.SetWorkersActive, Reset = _detailsAll.Reset,
            Tick = (dt, replay) => _detailsAll.Tick(dt, replay ? _car : Seen()) });
        Add(_kgb, () => new() { Who = Actors.None, Node = _kgb, Reset = _kgb.Reset, Shouts = _kgb.ActiveShouts, Tick = (dt, _) => _kgb.Tick(dt, _car) });
        Add(_campGate, () => new() { Who=Actors.None, Node=_campGate, Reset=_campGate.Reset, Tick=(dt,replay)=>_campGate.Tick(dt,replay?null:_car,!replay&&_state==State.Racing&&_horn.Pressed) });
        Add(_btrAll, () => new() { Who=Actors.Btr, Node=_btrAll, Reset=_btrAll.Reset, Tick=(dt,_)=>_btrAll.Tick(dt,_state==State.Racing?_car.Pos:null) });
        Add(_tanksAll, () => new() { Who = Actors.Tanks, Node = _tanksAll, Reset = _tanksAll.Reset, Tick = (dt, _) => _tanksAll.Tick(dt, _state == State.Racing ? _car : null) });
        Add(_babushkasAll, () => new() { Who = Actors.Babushkas, Node = _babushkasAll, Reset = _babushkasAll.Reset, Shouts = _babushkasAll.ActiveShouts, AfterCar = true,
            Tick = (dt, replay) => _babushkasAll.Tick(dt, _car, _maxImpact, _state == State.Racing ? _cam : null, !replay && WorldHorn,
                InLanRace && _net.IsHost ? _lanCars : null) });
        Add(_drunksAll, () => new() { Who = Actors.Drunks, Node = _drunksAll, Leave = _drunksAll.ResetMechanic, Shouts = _drunksAll.ActiveShouts, AfterCar = true,
            Tick = (dt, replay) =>
            {
                if (!replay && !InLanRace)
                {
                    MechanicDriveOff(dt);
                    _drunksAll.AircraftPresent = _hindsAll?.PadOccupied(1) ?? false;
                    _drunksAll.MechanicTick(dt, _car, _state == State.Racing && !FateHasCar, _horn.Pressed, CarNeedsMechanic, _health.Health);
                    MechanicRepairTick();
                }
                _drunksAll.Tick(dt, _car, _maxImpact);
            } });
        // The rescue tractor ticks in the race's own step (RaceScene.cs), never in the title's replay: a listing for the world tape, so a lap that was pulled out of a ditch shows the tractor.
        Add(_rescue, () => new() { Who = Actors.None, Node = _rescue, TapeDrive = _rescue.SetTapeDriven });
        Add(_rescue?.TapeRopeRoot, () => new() { Who = Actors.None, Node = _rescue.TapeRopeRoot, TapeDrive = _rescue.SetTapeRopeDriven });
        Add(_wood, () => new() { Who = Actors.None, Node = _wood });
        Add(_trap, () => new() { Who = Actors.None, Node = _trap, Reset = _trap.Reset, AfterCar = true,
            TapeDrive = _trap.SetTapeDriven, Focus = () => _trap.InspectorNode.GlobalPosition,
            Tick = (dt, _) => _trap.Tick(dt, _car.Pos) });
        // Only there for the rally's norms and fuel: no step of their own (the KGB officers move the liaison).
        _actors.Add(new() { Who = Actors.Liaison, Node = _kgb.Liaison });
        _actors.Add(new() { Who = Actors.Depot, Node = _depot, TapeDrive = _depot.SetTapeDriven });
        _actors.Add(new() { Who = Actors.None, Node = _powerTape, TapeDrive = _powerTape.SetTapeDriven });
    }

    /// <summary>One physics step of the actors in the world: those before the car, or those after it.</summary>
    bool WorldHorn => InLanRace && _net.IsHost ? System.Linq.Enumerable.FirstOrDefault(_lanDrivers.Values,d=>d.Car==_car)?.Last.Horn ?? false : _horn.Pressed;

    void TickActors(float dt, bool replay, bool afterCar)
    {
        if(InLanRace && !_net.IsHost) return;
        if(!afterCar && InWorld(Actors.Jets | Actors.Tanks)) _bonusExercise?.Tick(dt);
        var own=_car;float ownImpact=_maxImpact;
        try {
            foreach(var a in _actors) if(a.AfterCar==afterCar && InWorld(a.Who)) {
                if(TapeSkips(a,replay)) continue;      // the title replay's tape poses this one
                if(InLanRace && _lanDrivers.Count>0) {
                    // Prefer the driver nearest to this actor's live hulls.
                    float best=float.MaxValue;
                    foreach(var d in _lanDrivers.Values) {
                        float distance=float.MaxValue;var pos=new Vector2(d.Car.Pos.X,d.Car.Pos.Z);
                        foreach(var slot in _worldObstacles) if(slot.who==a.Who) {
                            var o=_track.Obstacles[slot.index];distance=Math.Min(distance,pos.DistanceSquaredTo(o.Center));
                        }
                        if(distance==float.MaxValue && a.Node!=null) distance=d.Car.Pos.DistanceSquaredTo(a.Focus?.Invoke() ?? a.Node.GlobalPosition);
                        if(distance<best) {best=distance;_car=d.Car;_maxImpact=d.Impact;}
                    }
                }
                a.Tick?.Invoke(dt,replay);
                _car=own;_maxImpact=ownImpact;
            }
        } finally {_car=own;_maxImpact=ownImpact;}
    }

    /// <summary>The start of a run: the actors in the world back to their places.</summary>
    void ResetActors()
    {
        _bonusExercise?.Reset();
        foreach (var a in _actors)
        {
            if (!InWorld(a.Who)) continue;
            long t0 = LoadProf.On ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            a.Reset?.Invoke();
            if (LoadProf.On && (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency >= 10) GD.Print($"LOADPROF reset actor {a.Who}: {(System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency:F1} ms");
        }
    }

    /// <summary>What the actors in the world are saying, and where.</summary>
    IEnumerable<(Vector3 pos, string text)> ActorShouts()
    {
        foreach (var a in _actors)
            if (a.Shouts != null && InWorld(a.Who))
                foreach (var s in a.Shouts()) yield return s;
    }

    /// <summary>
    /// No fuel on this track (TrackDef.Fuel false: the first): the depot is out of the world from the build on. ApplyWorld does it for a run, but only when one
    /// starts: a replay, the viewer or the title built the world and never ran it, and showed the tank and its marker where there is no fuel.
    /// </summary>
    void HideDepotWithoutFuel()
    {
        if (_fuelOnTrack) return;
        _worldNow &= ~Actors.Depot;      // (idempotent: the replay's release puts the parts back at the rest they were built in, and calls this again)
        foreach (var (who, i, _) in _worldObstacles)
            if (who == Actors.Depot) _track.Obstacles[i] = new Obstacle(new Vector2(-1e4f, -1e4f), 0.1f);
        foreach (var a in _actors)
        {
            if (a.Who != Actors.Depot || a.Node == null) continue;
            a.Node.Visible = false;
            a.Node.ProcessMode = ProcessModeEnum.Disabled;
            foreach (var c in a.Node.FindChildren("*", "", true, false))
                if (c is AudioStreamPlayer3D p3) p3.StreamPaused = true;
                else if (c is AudioStreamPlayer p) p.StreamPaused = true;
        }
    }

    /// <summary>The one mode switch: empty for a Stakhanov run, full for the title and the rally.</summary>
    void ApplyWorld(bool empty) => ApplyWorld(empty ? Actors.None : Actors.All);

    /// <summary>These actors in the world, the others gone (cleaned up first: the tractor's furrows
    /// leave the road, missiles and shells go, the mechanic back on his bench).</summary>
    void ApplyWorld(Actors want)
    {
        if (!_fuelOnTrack) want &= ~Actors.Depot;      // no fuel on this track (TrackDef.Fuel): no depot either
        bool empty = want == Actors.None;
        _carRadio.NoRadar = empty;
        if (_carVis.Damage != null) _carVis.Damage.Enabled = !empty;
        if (want == _worldNow) return;
        foreach (var a in _actors)
        {
            if (a.Who == Actors.None) continue;
            bool had = (_worldNow & a.Who) != 0, has = (want & a.Who) != 0;
            if (had == has) continue;
            if (!has) (a.Leave ?? a.Reset)?.Invoke();
            foreach (var (who, i, o) in _worldObstacles)
                if (who == a.Who) _track.Obstacles[i] = has ? o : new Obstacle(new Vector2(-1e4f, -1e4f), 0.1f);   // PlaceAtStart resets the movers next
            if (a.Show != null) { a.Show(has); continue; }
            var n = a.Node;
            if (n == null) continue;
            n.Visible = has;
            n.ProcessMode = has ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
            // Stopping the node does not pause its engines' 3D players: silence them too.
            foreach (var c in n.FindChildren("*", "", true, false))
                if (c is AudioStreamPlayer3D p3) p3.StreamPaused = !has;
                else if (c is AudioStreamPlayer p) p.StreamPaused = !has;
        }
        _worldNow = want;
        if (OS.HasEnvironment("WORLDDBG")) GD.Print($"world: {(empty ? "empty (Stakhanov)" : want == Actors.All ? "full" : want.ToString())} at {_clock:F1} s, {_worldObstacles.Count} obstacle slots");
    }

    /// <summary>WORLDDBG: collision circles of actors out of the world that are not parked off the map (invisible walls).</summary>
    void WorldStrays()
    {
        int strays = 0;
        foreach (var (who, i, _) in _worldObstacles)
            if ((_worldNow & who) == 0 && _track.Obstacles[i].Center.X > -1e3f)
            {
                strays++;
                GD.Print($"world: stray {who} obstacle #{i} at ({_track.Obstacles[i].Center.X:F0},{_track.Obstacles[i].Center.Y:F0})");
            }
        GD.Print($"world: {strays} stray obstacles");
        // Every collision circle on the road surface, with its owner (the actors' slots, else static).
        int onRoad = 0;
        for (int i = 0; i < _track.Obstacles.Count; i++)
        {
            var o = _track.Obstacles[i];
            if (o.Center.X < -1e3f) continue;
            int k = _track.NearestCenter(o.Center, out float d);
            if (d - o.Radius >= _track.HalfWidth[k]) continue;
            onRoad++;
            string owner = "static";
            foreach (var (who, j, _) in _worldObstacles) if (j == i) { owner = who + ((_worldNow & who) == 0 ? " (OUT OF THE WORLD)" : ""); break; }
            GD.Print($"world: on the road #{i} at ({o.Center.X:F1},{o.Center.Y:F1}) r {o.Radius:F1}, {d:F1} m off the centre line at road metre {k}, {owner}");
        }
        GD.Print($"world: {onRoad} collision circles on the road");
    }

    /// <summary>DAMAGEDBG: the collision circle nearest the car (within 4 m of its edge) and whose it is.</summary>
    string NearestObstacle()
    {
        var p = new Vector2(_car.Pos.X, _car.Pos.Z);
        int best = -1;
        float bestD = 4f;
        for (int i = 0; i < _track.Obstacles.Count; i++)
        {
            float d = _track.Obstacles[i].Center.DistanceTo(p) - _track.Obstacles[i].Radius;
            if (d < bestD) { bestD = d; best = i; }
        }
        if (best < 0) return ", no collision circle near (terrain, bank or body)";
        string owner = "static";
        foreach (var (who, j, _) in _worldObstacles) if (j == best) { owner = who + ((_worldNow & who) == 0 ? " (OUT OF THE WORLD)" : ""); break; }
        return $", obstacle #{best} r {_track.Obstacles[best].Radius:F1} {bestD:F1} m off, {owner}";
    }
}
