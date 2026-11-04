using System;
using System.Collections.Generic;
using CCCPRacing.Audio;
using CCCPRacing.Car;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Track;

/// <summary>
/// Every 30 s a fresh Balalaika rolls out of the hangar, taxis to the end of the highway strip,
/// takes off down the main straight on full afterburner, beats up the kolkhoz at low level
/// and, sooner or later, flies into the ground: fireball, smoke column, burning wreck, crater.
/// Taxiing/rolling jets and burning wrecks are obstacles. Each jet carries two missiles on its
/// wingtip rails; in normal play they never fire, but <see cref="FireMissile"/> can launch them.
/// </summary>
public partial class BalalaikaSorties : Node3D
{
    const float Interval = 30f;
    const float TaxiSpeed = 6f, FlySpeed = 46f, RotateSpeed = 42f;
    const int ObstacleSlots = 12;
    const float HoldAt = 8f;   // road metres past the runway start where the jet lines up

    enum Phase { Taxi, Hold, Takeoff, Fly, Dive }

    class Jet
    {
        public Phase Phase;
        public int Id;
        public Node3D Model, Gear;
        public Node3D[] Rails;        // [0] left, [1] right: the missile under each wingtip rail
        public MeshInstance3D Flame;
        public JetSound Sound;
        public Vector3 Pos, PrevPos, Vel;
        public float Yaw, Pitch, Roll, Speed, Climb, PhaseTime, FlightLeft, Afterburner, RwS;
        public int TaxiIdx;
        public float Waited;          // the hold for a plough still to run out (s)
        public Vector2 Waypoint, CrashAt;
        // Revenge: this jet hunts the car with both missiles before its sortie ends as usual.
        public bool Avenger, Hunting, Extend, Hit;
        public int Fired;
        public float FireClock;
        public Missile First;
    }

    class Wreck
    {
        public Node3D Node;
        public Vector3 Pos;
        public float Life;
    }

    TrackData _t;
    TyreEffects _fx;
    readonly List<Jet> _jets = new();
    readonly List<Wreck> _wrecks = new();
    readonly List<Missile> _missiles = new();
    readonly List<Vector2> _taxi = new();
    readonly RandomNumberGenerator _rng = new() { Seed = 1956 };
    Vector2 _rwStart;
    float _rwLen, _rwHalfWidth, _timer;
    int _obsBase = -1, _rwFrom, _nextId;
    float _testClock;
    int _testJet = -1;
    Node3D _scorches;
    bool _ok;
    /// <summary>The actual authored taxi centreline, also used to keep ground crews clear.</summary>
    public IReadOnlyList<Vector2> TaxiPath => _taxi;

    // The world tape (docs/replay-tape.md, phase 4): the jets and the guided missiles are dynamic instances (BalalaikaSorties.Tape.cs).
    /// <summary>The world tape's view of the jets and missiles made here (null: nothing records). Set by the race.</summary>
    public ITapeInstanceHost TapeHost;
    /// <summary>The world tape's view of the effects made here (the crash, the missile burst). Set by the race.</summary>
    public ITapeFxHost TapeFx;

    /// <summary>Ground crew follows real traffic; a stopped aircraft takes precedence over moving ones.</summary>
    public (bool Moving, bool Waiting) GroundSignal()
    {
        if (!Visible) return (false, false);
        bool moving = false;
        foreach (var jet in _jets)
        {
            if (jet.Phase is not (Phase.Taxi or Phase.Hold)) continue;
            if (jet.Speed < .1f || jet.Phase == Phase.Hold) return (false, true);
            moving = true;
        }
        return (moving, false);
    }

    /// <summary>Nozzles of the jets with the afterburner lit, and how hard it burns (0..1): the night's afterburner lights.</summary>
    public IEnumerable<(Vector3 nozzle, float burn)> Burners()
    {
        foreach (var j in _jets)
            if (j.Flame.Visible) yield return (j.Flame.GlobalPosition, j.Afterburner);
        if (_tapeDriven) foreach (var b in ReplayBurners()) yield return b;      // the jets the title replay's tape shows
    }

    /// <summary>(position) when a jet hits the ground — for camera shake and messages.</summary>
    public event Action<Vector3> Crashed;
    /// <summary>A jet starts its take-off roll.</summary>
    public event Action TakeoffRoll;
    /// <summary>A missile left its rail.</summary>
    public event Action<Missile> MissileLaunched;
    /// <summary>(position, distance to its target) when a missile goes off. HOOK: RaceScene subscribes
    /// here to shake the camera, blast the car and (Balalaika revenge) call DestroyCar on a hit.</summary>
    public event Action<Vector3, float> MissileExploded;

    /// <summary>Test (--missile-test): the first flying jet fires left at this target, right ~2 s later.</summary>
    public Func<Vector3> TestTarget;
    /// <summary>Test (--missile-test-repeat): every flying jet that is ahead of the car fires both missiles (not only the first jet of a reset), so a recorded lap holds missiles.</summary>
    public bool TestRepeat;

    // ------------------------------------------------------------ revenge (the pilot's mother)

    const float HuntTime = 45f;             // the avenger gives up and flies on after this long
    const float FireMin = 90f, FireMax = 300f, SecondDelay = 2.2f;
    static readonly bool RevengeDbg = OS.HasEnvironment("REVENGEDBG");
    bool _avengerPending;
    int _avengerId = -1;

    /// <summary>Where the avenger's missiles go (the car); set by the race.</summary>
    public Func<Vector3> AvengerTarget;
    /// <summary>(jet id) the avenger starts its take-off roll.</summary>
    public event Action<int> AvengerRolling;
    /// <summary>(jet id, rail side -1/+1, missile) the avenger fired at the car.</summary>
    public event Action<int, int, Missile> AvengerFired;
    /// <summary>The avenger crashed: the revenge is over.</summary>
    public event Action AvengerGone;

    /// <summary>A revenge is on: an avenger is marked (waiting for its sortie) or flying.</summary>
    public bool RevengeActive => _avengerPending || _avengerId >= 0;
    /// <summary>Id of the avenger jet once it exists, else -1.</summary>
    public int AvengerId => _avengerId;
    /// <summary>The avenger is airborne and still after the car.</summary>
    public bool AvengerHunting => Find(_avengerId) is { Hunting: true };

    /// <summary>
    /// The pilot's mother has been run over: the next sortie to take off (one still in the hangar
    /// or on the taxiway, else the next one out) is flown by her son. False if one is already on.
    /// </summary>
    public bool StartRevenge()
    {
        if (!_ok || RevengeActive) return false;
        foreach (var j in _jets)
            if (j.Phase is Phase.Taxi or Phase.Hold) { MakeAvenger(j); return true; }
        _avengerPending = true;
        if (RevengeDbg) GD.Print($"revenge Balalaika: pending, next sortie in {_timer:F1} s");
        return true;
    }

    void MakeAvenger(Jet j)
    {
        j.Avenger = true;
        _avengerId = j.Id;
        _avengerPending = false;
        if (RevengeDbg) GD.Print($"revenge Balalaika: avenger is jet #{j.Id} ({j.Phase})");
    }

    void EndRevenge(Jet j, string why)
    {
        if (!j.Avenger || j.Id != _avengerId) return;
        _avengerId = -1;
        if (RevengeDbg) GD.Print($"revenge Balalaika: jet #{j.Id} {why}, revenge reset");
        AvengerGone?.Invoke();
    }

    /// <summary>The avenger in the air: line up on the car from far enough out, fire left, then right
    /// at least 2 s later once the first has gone off, unless it got the car. Afterwards the sortie
    /// ends as usual.</summary>
    void Hunt(Jet j, float dt)
    {
        Vector3 tp = AvengerTarget?.Invoke() ?? j.Pos;
        Vector2 p = new(j.Pos.X, j.Pos.Z), c = new(tp.X, tp.Z), to = c - p;
        float dist = to.Length();
        float dot = dist > 1f ? to.Normalized().Dot(Heading(j)) : 1f;
        // Too close to turn onto the car (or just past it): fly on out and come round again.
        if (!j.Extend && (dist < FireMin * 0.8f || (dist < 170f && dot < 0.3f))) j.Extend = true;
        if (j.Extend && dist > 260f) j.Extend = false;
        if (j.Extend) Steer(j, p + Heading(j) * 100f, dt, 0.2f);
        else Steer(j, c, dt, 0.9f);

        j.FireClock += dt;
        bool lined = !j.Extend && dist > FireMin && dist < FireMax && dot > 0.9f;
        if (lined && j.Fired == 0) Fire(j, -1);
        else if (lined && j.Fired == 1 && j.FireClock >= SecondDelay && !j.Hit && j.First is not { Alive: true }) Fire(j, 1);

        if (j.Hit || j.Fired >= 2 || j.PhaseTime > HuntTime)
        {
            // Job done (or given up): back to beating up the kolkhoz, and the usual end.
            j.Hunting = false;
            j.FlightLeft = _rng.RandfRange(5f, 9f);
            j.Waypoint = RandomPoint();
            if (RevengeDbg) GD.Print($"revenge Balalaika: jet #{j.Id} stops hunting ({(j.Hit ? "hit" : j.Fired >= 2 ? "both fired" : "time up")}), crashes in ~{j.FlightLeft:F0} s");
        }
    }

    void Fire(Jet j, int side)
    {
        if (!FireMissile(j.Id, side, AvengerTarget)) return;
        j.Fired++;
        j.FireClock = 0;
        var m = _missiles[^1];
        j.First ??= m;
        if (RevengeDbg)
        {
            Vector3 tp = AvengerTarget();
            GD.Print($"revenge Balalaika: jet #{j.Id} fires {(side < 0 ? "left" : "right")} at {tp.DistanceTo(j.Pos):F0} m (alt {j.Pos.Y - _t.MeshHeight(j.Pos.X, j.Pos.Z):F0} m)");
            m.Exploded += (p, d) => GD.Print($"revenge Balalaika: {(side < 0 ? "left" : "right")} missile goes off {d:F1} m from the car (closest {m.MinDist:F1} m)");
        }
        AvengerFired?.Invoke(j.Id, side, m);
    }

    public void Init(TrackData t, TyreEffects fx)
    {
        _t = t;
        _fx = fx;
        PropInstance hangar = null;
        foreach (var p in t.Props) if (p.Type == "hangar") hangar = p;
        if (hangar == null || t.Runway == null) return;

        int n = t.Center.Length;
        int from = ((int)t.Runway.From % n + n) % n;
        _rwLen = ((int)(t.Runway.To - t.Runway.From) % n + n) % n;
        _rwFrom = from;
        _rwStart = RwPoint(HoldAt);
        _rwHalfWidth = t.HalfWidth[from];

        // Taxi route: out of the hangar mouth, across the apron, onto the runway threshold.
        var h = new Vector2(hangar.Position.X, hangar.Position.Z);
        Vector2 outDir = new(MathF.Sin(hangar.Rotation), MathF.Cos(hangar.Rotation));   // hangar opens along local +Z
        _taxi.Add(h - outDir * 4f);
        _holdShort = -1;
        if (t.Runway.ClearBehind > 0f)
        {
            // A strip with a blind approach: straight out of the hangar door, then along a taxiway
            // that follows the strip's edge (a wingtip clear of the edge lights, well clear of the
            // buildings behind) east to the turning place beside the strip's straight first metres
            // (its very start may still be in the bend up to it); there a jet waits while a
            // snowplough is near the strip (Taxi), never on the strip itself, then turns onto it.
            const float At = HoldAt + 10f, Lane = 3.5f, Door = 13f;  // Lane: the 7 m wings between the hangar fronts and the edge lights; Door: hangar half-length + 2 m
            Vector2 Edge(float s)
            {
                Vector2 c = RwPoint(s), along = RwDir(s), side = new(-along.Y, along.X);
                if (side.Dot(h - c) < 0) side = -side;
                int i = ((_rwFrom + (int)s) % n + n) % n;
                return c + side * (t.HalfWidth[i] + Lane);
            }
            _taxi.Add(h + outDir * Door);                                  // out of the door
            // The taxiway from abreast of the hangar to the turning place, every 10 m of the strip.
            float sh = 0, best = float.MaxValue;
            for (float s = 0; s <= _rwLen; s += 1f)
                if (RwPoint(s).DistanceSquaredTo(h) < best) { best = RwPoint(s).DistanceSquaredTo(h); sh = s; }
            for (float s = sh; s > At; s -= 10f) _taxi.Add(Edge(s));
            _holdShort = _taxi.Count;
            _taxi.Add(Edge(At));
            if (OS.HasEnvironment("JETDBG")) GD.Print("Balalaika taxi route: " + string.Join(" ", _taxi.ConvertAll(q => $"({q.X:F0},{q.Y:F0})")));
        }
        else
        {
            _taxi.Add(h + outDir * 16f);
            Vector2 mid = h + outDir * 16f;
            _taxi.Add(new Vector2(mid.X, _rwStart.Y - 10f));
        }
        // Onto the road at the runway start and along its centre line to the hold point.
        _taxi.Add(RwPoint(0f));
        _taxi.Add(RwPoint(HoldAt * 0.5f));
        _taxi.Add(_rwStart);

        _obsBase = t.Obstacles.Count;
        for (int k = 0; k < ObstacleSlots; k++) t.Obstacles.Add(new Obstacle(new Vector2(-1e4f, -1e4f), 0.1f));
        _scorches = new Node3D { Name = "Scorches" };
        AddChild(_scorches);
        _ok = true;
        Tape.Version(this, 1); Tape.Part(this, "root");      // the world tape: the sorties' actor (the jets and their missiles are its instances); only a track that flies jets is marked
        Reset();
    }

    public void Reset()
    {
        if (!_ok) return;
        foreach (var j in _jets) if (j.Model != null) { TapeHost?.Gone(j.Model); j.Model.QueueFree(); }
        foreach (var w in _wrecks) w.Node.QueueFree();
        foreach (var c in _scorches.GetChildren()) c.QueueFree();
        foreach (var m in _missiles) { TapeHost?.Gone(m); m.Remove(); }
        _missiles.Clear();
        _testJet = -1;
        _avengerPending = false;
        _avengerId = -1;
        _jets.Clear();
        _wrecks.Clear();
        _timer = 6f;   // first sortie soon after the start
    }

    /// <summary>One aircraft waiting off the runway. The ordinary plough clearance and takeoff logic still apply.</summary>
    public void CueSchoolSortie()
    {
        if (!_ok || _holdShort < 0) return;
        Reset();
        Spawn();
        _timer = Interval;
        var j = _jets[0];
        Vector2 p = _taxi[_holdShort];
        j.TaxiIdx = _holdShort + 1;
        j.Pos = new(p.X, _t.MeshHeight(p.X, p.Y), p.Y);
        j.PrevPos = j.Pos;
        j.Yaw = TrackData.YawFor(_taxi[j.TaxiIdx] - p);
        j.Speed = 0;
        j.Waited = PloughWait;
        Place(j);
    }

    // ------------------------------------------------------------ spawning

    void Spawn()
    {
        var model = BuildJet(out var flame, out var sound);
        AddChild(model);
        var j = new Jet
        {
            Id = ++_nextId,
            Rails = new[] { model.GetNode<Node3D>("MissileL/Missile"), model.GetNode<Node3D>("MissileR/Missile") },
            Phase = Phase.Taxi,
            Model = model,
            Gear = model.GetNode<Node3D>("Gear"),
            Flame = flame,
            Sound = sound,
            Pos = new Vector3(_taxi[0].X, _t.MeshHeight(_taxi[0].X, _taxi[0].Y), _taxi[0].Y),
            Yaw = TrackData.YawFor(_taxi[1] - _taxi[0]),
            TaxiIdx = 1,
        };
        j.PrevPos = j.Pos;
        _jets.Add(j);
        if (_avengerPending) MakeAvenger(j);
        Place(j);
        MarkTape(model, sound, () => j.Phase == Phase.Dive ? 2 : 0);
        TapeHost?.Born(model);
    }

    // ------------------------------------------------------------ update

    public void Tick(float dt, CarSim car)
    {
        if (!_ok) return;
        if (_tapeDriven) { UpdateObstacles(); return; }      // the title replay's tape shows the jets and their wrecks: nothing flies here (and no circle stays)
        _clock += dt;
        _timer -= dt;
        if (_timer <= 0) { _timer = Interval; Spawn(); }

        for (int i = _jets.Count - 1; i >= 0; i--)
        {
            var j = _jets[i];
            j.PhaseTime += dt;
            switch (j.Phase)
            {
                case Phase.Taxi: Taxi(j, dt); break;
                case Phase.Hold: Hold(j, dt); break;
                case Phase.Takeoff: Takeoff(j, dt); break;
                case Phase.Fly: Fly(j, dt); break;
                case Phase.Dive: Dive(j, dt); break;
            }
            if (j.Model == null) { _jets.RemoveAt(i); continue; }
            if (dt > 0) j.Vel = (j.Pos - j.PrevPos) / dt;
            j.PrevPos = j.Pos;
            Place(j);
            Exhaust(j, dt);
        }

        for (int i = _missiles.Count - 1; i >= 0; i--)
            if (!_missiles[i].Tick(dt)) { TapeHost?.Gone(_missiles[i]); _missiles.RemoveAt(i); }
        if (TestTarget != null) MissileTest(dt);

        for (int i = _wrecks.Count - 1; i >= 0; i--)
        {
            var w = _wrecks[i];
            w.Life -= dt;
            if (_rng.Randf() < dt * 14f)
                _fx.Smoke(w.Pos + Vector3.Up * 0.8f, Vector3.Up * _rng.RandfRange(2f, 4f) + new Vector3(_rng.RandfRange(-0.5f, 0.5f), 0, _rng.RandfRange(-0.5f, 0.5f)),
                    new Color(0.1f, 0.1f, 0.1f), 0.7f, 1.2f, 4.5f, 3.5f);
            if (w.Life > 3f && _rng.Randf() < dt * 20f)
                _fx.Burst(w.Pos + new Vector3(_rng.RandfRange(-1.5f, 1.5f), 0.5f, _rng.RandfRange(-1.5f, 1.5f)), Vector3.Up * _rng.RandfRange(1.5f, 3f),
                    new Color(1f, _rng.RandfRange(0.35f, 0.7f), 0.1f), 1f, 0.9f, 0.2f, 0.5f, -2f, 1f, false);
            if (w.Life <= 0) { w.Node.QueueFree(); _wrecks.RemoveAt(i); }
        }

        UpdateObstacles();
    }

    float _clock;
    int _holdShort = -1;     // index of the apron holding point in _taxi (-1: none)

    /// <summary>The winter road's snowploughs (null: none): a jet keeps off the strip while one is near it.</summary>
    public SnowPloughs Ploughs;

    const float PloughComing = 150f;    // road metres: a plough coming at the strip from its far end
    const float PloughWait = 3f;        // a jet that has waited at the strip still waits this long after the plough

    /// <summary>A snowplough on the strip, or coming up to it: RunwayDef.ClearBehind metres short of
    /// its start going the race's way, PloughComing short of its far end going against it.</summary>
    bool PloughNear()
    {
        if (_holdShort < 0 || Ploughs == null) return false;
        int n = _t.Center.Length;
        float len = _rwLen, behind = _t.Runway.ClearBehind;
        foreach (var (s, against) in Ploughs.Lanes)
        {
            float into = ((s - _rwFrom) % n + n) % n;          // road metres past the strip's start
            if (into <= len) return true;
            if (!against && into >= n - behind) return true;
            if (against && into <= len + PloughComing) return true;
        }
        return false;
    }

    /// <summary>Another jet on the ground ahead of j on the taxi route, within gap metres (or turning onto the strip).</summary>
    bool JetAhead(Jet j, Vector2 p, float gap)
    {
        foreach (var o in _jets)
        {
            if (o == j || o.Model == null) continue;
            bool onStrip = o.Phase is Phase.Hold || o.Phase is Phase.Takeoff && o.Climb < 3f;
            bool ahead = onStrip || o.Phase is Phase.Taxi && (o.TaxiIdx > j.TaxiIdx || o.TaxiIdx == j.TaxiIdx && o.Id < j.Id);
            if (!ahead) continue;
            // At the holding point: the strip must be clear of the one in front altogether.
            if (j.TaxiIdx == _holdShort + 1 && (onStrip || o.TaxiIdx > _holdShort + 1)) return true;
            if (new Vector2(o.Pos.X, o.Pos.Z).DistanceTo(p) < gap) return true;
        }
        return false;
    }

    void Taxi(Jet j, float dt)
    {
        Vector2 p = new(j.Pos.X, j.Pos.Z), target = _taxi[j.TaxiIdx];
        // At the holding point beside the strip: wait there while a snowplough is on or near it
        // (and a moment after it has gone), or while the jet in front is still on the strip.
        bool atHold = _holdShort >= 0 && j.TaxiIdx == _holdShort + 1 && p.DistanceTo(_taxi[_holdShort]) < 0.5f;
        if (atHold && PloughNear()) j.Waited = PloughWait;
        if (atHold) j.Waited -= dt;
        if (atHold && j.Waited > 0 || JetAhead(j, p, 16f))
        {
            if (OS.HasEnvironment("JETDBG") && j.Speed > 0) GD.Print($"Balalaika {j.Id} holds at ({p.X:F0},{p.Y:F0}) at {_clock:F1} s: {(atHold && j.Waited > 0 ? "a plough is near the strip" : "a jet ahead")}");
            j.Speed = 0;
            j.Sound.Level = 0.2f;
            return;
        }
        if (OS.HasEnvironment("JETDBG") && j.Speed == 0 && j.TaxiIdx > 1) GD.Print($"Balalaika {j.Id} taxis on at {_clock:F1} s");
        Vector2 to = target - p;
        float step = TaxiSpeed * dt;
        if (to.Length() <= step)
        {
            p = target;
            if (++j.TaxiIdx >= _taxi.Count) { j.Phase = Phase.Hold; j.PhaseTime = 0; j.RwS = HoldAt; }
        }
        else p += to.Normalized() * step;
        j.Speed = TaxiSpeed;
        if (to.LengthSquared() > 0.01f) j.Yaw = Mathf.LerpAngle(j.Yaw, TrackData.YawFor(to), Math.Min(1f, dt * 2.5f));
        j.Pos = new Vector3(p.X, _t.MeshHeight(p.X, p.Y), p.Y);
        j.Pitch = Mathf.Lerp(j.Pitch, GroundPitch(p, j.Yaw), Math.Min(1f, dt * 4f));
        j.Sound.Level = 0.2f;
    }

    void Hold(Jet j, float dt)
    {
        j.Speed = 0;
        // Swing the nose round onto the centre line while spooling up.
        Vector2 p = RwPoint(j.RwS);
        j.Yaw = Mathf.LerpAngle(j.Yaw, TrackData.YawFor(RwDir(j.RwS)), Math.Min(1f, dt * 4f));
        j.Pos = new Vector3(p.X, _t.MeshHeight(p.X, p.Y), p.Y);
        j.Pitch = Mathf.Lerp(j.Pitch, GroundPitch(p, j.Yaw), Math.Min(1f, dt * 4f));
        j.Sound.Level = Mathf.Clamp(j.PhaseTime / 1.5f, 0.2f, 1f);
        j.Afterburner = j.PhaseTime > 1.2f ? 1f : 0f;
        if (j.PhaseTime > 1.8f)
        {
            j.Phase = Phase.Takeoff;
            j.PhaseTime = 0;
            if (OS.HasEnvironment("JETDBG")) GD.Print($"Balalaika {j.Id} rolls at {_clock:F1} s at ({j.Pos.X:F0},{j.Pos.Z:F0}) heading {Mathf.RadToDeg(j.Yaw):F0}°");
            TakeoffRoll?.Invoke();
            if (j.Avenger) AvengerRolling?.Invoke(j.Id);
        }
    }

    void Takeoff(Jet j, float dt)
    {
        j.Speed += 17f * dt;
        j.Afterburner = 1f;
        j.Sound.Level = 1f;
        j.RwS += j.Speed * dt;
        Vector2 p;
        if (j.Climb <= 0f && j.RwS < _rwLen - 3f)
        {
            // Wheels on the concrete: roll along the road's centre line, nose following its curve.
            p = RwPoint(j.RwS);
            j.Yaw = Mathf.LerpAngle(j.Yaw, TrackData.YawFor(RwDir(j.RwS)), Math.Min(1f, dt * 8f));
        }
        else p = new Vector2(j.Pos.X, j.Pos.Z) + Heading(j) * j.Speed * dt;
        float ground = _t.MeshHeight(p.X, p.Y), gp = GroundPitch(p, j.Yaw);
        if (j.Speed > RotateSpeed)
        {
            // Rotate: height over the (sloping) ground grows once the nose is above the slope.
            j.Pitch = Mathf.MoveToward(j.Pitch, 0.25f, dt * 0.6f);
            j.Climb = Math.Max(0f, j.Climb + j.Speed * (MathF.Sin(j.Pitch) - MathF.Sin(gp)) * dt);
        }
        else j.Pitch = Mathf.Lerp(j.Pitch, gp, Math.Min(1f, dt * 8f));
        j.Pos = new Vector3(p.X, ground + j.Climb, p.Y);
        if (j.Climb > 3f) j.Gear.Visible = false;
        if (j.Climb > 14f)
        {
            j.Phase = Phase.Fly;
            j.PhaseTime = 0;
            if (OS.HasEnvironment("JETDBG")) GD.Print($"Balalaika {j.Id} airborne at {_clock:F1} s at ({j.Pos.X:F0},{j.Pos.Z:F0}), {j.RwS:F0} m down the strip");
            j.FlightLeft = _rng.RandfRange(9f, 18f);
            j.Waypoint = RandomPoint();
            if (j.Avenger) { j.Hunting = true; j.FlightLeft = HuntTime + 60f; }
        }
    }

    void Fly(Jet j, float dt)
    {
        j.Speed = Mathf.MoveToward(j.Speed, FlySpeed, dt * 8f);
        j.Afterburner = Mathf.MoveToward(j.Afterburner, 0f, dt * 0.4f);
        j.Sound.Level = 0.8f;
        Vector2 p = new(j.Pos.X, j.Pos.Z);
        if (j.Hunting) Hunt(j, dt);
        else
        {
            if (p.DistanceTo(j.Waypoint) < 30f) j.Waypoint = RandomPoint();
            Steer(j, j.Waypoint, dt, 0.7f);
        }
        // Contour-hugging: hold 15-35 m over the ground ahead (lower while hunting).
        Vector2 ahead = p + Heading(j) * 30f;
        float floor = Math.Max(_t.MeshHeight(p.X, p.Y), _t.MeshHeight(ahead.X, ahead.Y));
        float wantAlt = j.Hunting ? floor + 15f + MathF.Sin(j.PhaseTime * 0.9f) * 3f : floor + 22f + MathF.Sin(j.PhaseTime * 0.7f) * 9f;
        float vy = Math.Clamp((wantAlt - j.Pos.Y) * 0.8f, -12f, 12f);
        j.Pitch = Mathf.Lerp(j.Pitch, MathF.Asin(Math.Clamp(vy / j.Speed, -0.5f, 0.5f)), Math.Min(1f, dt * 2f));
        Advance(j, dt, vy);

        j.FlightLeft -= dt;
        if (j.FlightLeft <= 0)
        {
            // Something goes badly wrong: pick a spot ahead and fly into it.
            j.Phase = Phase.Dive;
            j.PhaseTime = 0;
            Vector2 c = p + Heading(j) * _rng.RandfRange(70f, 130f);
            float w = _t.WorldSize;
            j.CrashAt = new Vector2(Math.Clamp(c.X, 30f, w - 30f), Math.Clamp(c.Y, 30f, w - 30f));
        }
    }

    void Dive(Jet j, float dt)
    {
        j.Sound.Level = 0.9f;
        j.Afterburner = 0;
        Steer(j, j.CrashAt, dt, 1.0f);
        Vector2 p = new(j.Pos.X, j.Pos.Z);
        float dist = Math.Max(5f, p.DistanceTo(j.CrashAt));
        float ground = _t.MeshHeight(p.X, p.Y);
        float vy = -(j.Pos.Y - ground) / (dist / j.Speed) - 2f;
        j.Pitch = Mathf.Lerp(j.Pitch, MathF.Asin(Math.Clamp(vy / j.Speed, -0.8f, 0.2f)), Math.Min(1f, dt * 3f));
        j.Roll += MathF.Sin(j.PhaseTime * 9f) * dt * 2.5f;                  // flailing
        Advance(j, dt, vy);
        // Trailing black smoke and bits of fire.
        _fx.Smoke(j.Pos + Vector3.Up * 1.6f, Vector3.Up * 0.5f, new Color(0.08f, 0.08f, 0.08f), 0.75f, 0.8f, 3f, 2.5f);
        if (_rng.Randf() < 0.5f)
            _fx.Burst(j.Pos + Vector3.Up * 1.6f, Vector3.Zero, new Color(1f, 0.55f, 0.1f), 1f, 0.9f, 0.3f, 0.25f, 0f, 1f, false);

        if (j.Pos.Y <= _t.MeshHeight(j.Pos.X, j.Pos.Z) + 0.6f) Explode(j);
    }

    // ------------------------------------------------------------ helpers

    Vector2 Heading(Jet j) => new(-MathF.Sin(j.Yaw), -MathF.Cos(j.Yaw));

    /// <summary>Point on the road centre line, s metres past the runway start (samples are ~1 m apart).</summary>
    Vector2 RwPoint(float s)
    {
        int n = _t.Center.Length, i = (int)MathF.Floor(s);
        Vector2 a = _t.Center[((_rwFrom + i) % n + n) % n], b = _t.Center[((_rwFrom + i + 1) % n + n) % n];
        return a.Lerp(b, s - i);
    }

    /// <summary>Centre-line direction at s, taken over a jet-sized chord so the nose turns smoothly.</summary>
    Vector2 RwDir(float s) => (RwPoint(s + 3f) - RwPoint(s - 3f)).Normalized();

    /// <summary>Pitch that sits the nose and main wheels on the ground along the heading.</summary>
    float GroundPitch(Vector2 p, float yaw)
    {
        Vector2 h = new Vector2(-MathF.Sin(yaw), -MathF.Cos(yaw)) * 3f;
        return MathF.Atan2(_t.MeshHeight(p.X + h.X, p.Y + h.Y) - _t.MeshHeight(p.X - h.X, p.Y - h.Y), 6f);
    }

    Vector2 RandomPoint()
    {
        float w = _t.WorldSize;
        return new Vector2(_rng.RandfRange(40f, w - 40f), _rng.RandfRange(40f, w - 40f));
    }

    void Steer(Jet j, Vector2 target, float dt, float maxRate)
    {
        Vector2 p = new(j.Pos.X, j.Pos.Z);
        float want = TrackData.YawFor(target - p);
        float err = Mathf.AngleDifference(j.Yaw, want);
        float rate = Math.Clamp(err * 1.5f, -maxRate, maxRate);
        j.Yaw += rate * dt;
        j.Roll = Mathf.Lerp(j.Roll, rate * 1.1f, Math.Min(1f, dt * 3f));    // bank into the turn
    }

    void Advance(Jet j, float dt, float vy)
    {
        Vector2 h = Heading(j);
        j.Pos += new Vector3(h.X, 0, h.Y) * j.Speed * dt + Vector3.Up * vy * dt;
        // Keep over the map; if it strays out, swing it back in.
        float w = _t.WorldSize;
        if (j.Pos.X < 10 || j.Pos.Z < 10 || j.Pos.X > w - 10 || j.Pos.Z > w - 10) j.Waypoint = new Vector2(w / 2, w / 2);
    }

    void Place(Jet j)
    {
        var b = new Basis(Vector3.Up, j.Yaw) * new Basis(Vector3.Right, j.Pitch) * new Basis(Vector3.Back, j.Roll);
        j.Model.GlobalTransform = new Transform3D(b, j.Pos);
        j.Flame.Visible = j.Afterburner > 0.05f;
        if (j.Flame.Visible)
        {
            float f = 0.8f + 0.4f * _rng.Randf();
            j.Flame.Scale = new Vector3(1f, f * j.Afterburner, 1f);
        }
        j.Sound.Afterburner = j.Afterburner;
    }

    void Exhaust(Jet j, float dt)
    {
        if (j.Afterburner < 0.05f) return;
        Vector3 tail = j.Model.GlobalTransform * new Vector3(0, 1.6f, 6.6f);
        Vector3 back = j.Model.GlobalTransform.Basis.Z;
        for (int k = 0; k < 2; k++)
            _fx.Burst(tail + back * _rng.RandfRange(0.5f, 2.5f), back * 12f, _rng.Randf() < 0.5f ? new Color(1f, 0.85f, 0.4f) : new Color(1f, 0.45f, 0.1f),
                1f, 0.7f, 0.2f, 0.18f, 0f, 2f, false);
        if (_rng.Randf() < 0.4f)
            _fx.Smoke(tail + back * 3f, Vector3.Up * 0.5f, new Color(0.45f, 0.45f, 0.45f), 0.35f, 0.8f, 3f, 1.8f);
    }

    void Explode(Jet j)
    {
        Vector3 p = new(j.Pos.X, _t.MeshHeight(j.Pos.X, j.Pos.Z), j.Pos.Z);
        TapeFx?.Fx(new TapeEvent(0f, TapeEventType.Effect, Kind: TapeFxKind.JetCrash, Effect: TapeEffect.JetCrash, Pos: p, Life: CrashLife));
        CrashFx(_fx, _rng, this, this, p, out var scorch, out var wreck);
        _scorches.AddChild(scorch);
        if (_scorches.GetChildCount() > 8) _scorches.GetChild(0).QueueFree();
        AddChild(wreck);
        _wrecks.Add(new Wreck { Node = wreck, Pos = p, Life = WreckLife });

        Crashed?.Invoke(p);
        EndRevenge(j, "crashed");
        TapeHost?.Gone(j.Model);
        j.Model.QueueFree();
        j.Model = null;
    }

    /// <summary>The jet hits the ground: fireball, smoke and debris (<paramref name="fx"/>), the flash (under <paramref name="light"/>), the boom (under <paramref name="sound"/>), and the scorch and the wreck as nodes for the caller to place.
    /// Live play and the title replay (the world tape's sink) both make it here; the order of the random draws is that of the live code.</summary>
    internal static void CrashFx(TyreEffects fx, RandomNumberGenerator rng, Node light, Node sound, Vector3 p, out MeshInstance3D scorch, out Node3D wreck)
    {
        // Fireball, smoke column and debris.
        // (fx null: the particles are long over, a replay restoring the crash late; the draws are made all the same, so the scorch and the wreck come out as when it is played.)
        for (int k = 0; k < 70; k++)
        {
            Vector3 v = new Vector3(rng.RandfRange(-1, 1), rng.RandfRange(0.3f, 1.2f), rng.RandfRange(-1, 1)) * rng.RandfRange(4f, 16f);
            var hot = rng.Randf() < 0.5f ? new Color(1f, 0.8f, 0.3f) : new Color(1f, 0.4f, 0.08f);
            float life = rng.RandfRange(1.5f, 3f), size = rng.RandfRange(0.4f, 0.9f);
            fx?.Burst(p + Vector3.Up, v, hot, 1f, life, 0.4f, size, -2f, 2.5f, false);
        }
        for (int k = 0; k < 35; k++)
        {
            var at = p + new Vector3(rng.RandfRange(-3, 3), rng.RandfRange(1, 5), rng.RandfRange(-3, 3));
            var up = new Vector3(rng.RandfRange(-2, 2), rng.RandfRange(3, 8), rng.RandfRange(-2, 2));
            fx?.Smoke(at, up, new Color(0.07f, 0.07f, 0.07f), 0.85f, 2f, 7f, rng.RandfRange(3f, 6f));
        }
        for (int k = 0; k < 30; k++)
        {
            var v = new Vector3(rng.RandfRange(-12, 12), rng.RandfRange(6, 18), rng.RandfRange(-12, 12));
            fx?.Burst(p + Vector3.Up, v, new Color(0.2f, 0.2f, 0.22f), 1f, 0.35f, 0.3f, 3f, 14f, 0.2f, true);
        }

        // Flash.
        var flash = new OmniLight3D { LightColor = new Color(1f, 0.7f, 0.3f), LightEnergy = 6f, OmniRange = 45f, Position = p + Vector3.Up * 3f };
        light.AddChild(flash);
        var tw = flash.CreateTween();
        tw.TweenProperty(flash, "light_energy", 0f, 0.6f);
        tw.TweenCallback(Callable.From(flash.QueueFree));

        // Boom.
        var boom = new ExplosionSound { Position = p, Loudness = 2f, Reach = 1500f };
        sound.AddChild(boom);

        // Crater: a dark, ragged scorch on the ground.
        scorch = new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(9f, 9f) },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoTexture = ScorchTexture(),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        scorch.Transform = new Transform3D(new Basis(Vector3.Up, rng.RandfRange(0, MathF.Tau)) * new Basis(Vector3.Right, -MathF.PI / 2f), p + Vector3.Up * 0.07f);

        // Burning wreck: a few twisted pieces.
        wreck = new Node3D { Position = p };
        var metal = new Color(0.25f, 0.25f, 0.27f);
        wreck.AddChild(Props.Part(Props.Cyl(0.6f, 5f, 8), metal, new Vector3(0, 0.4f, 0), new Vector3(80, rng.RandfRange(0, 360), 15)));
        wreck.AddChild(Props.Part(Props.Box(3f, 0.15f, 2f), metal * 0.8f, new Vector3(2f, 0.3f, 1f), new Vector3(20, 40, 10)));
        wreck.AddChild(Props.Part(Props.Box(0.15f, 1.8f, 1.5f), metal * 0.9f, new Vector3(-2f, 0.6f, -1.5f), new Vector3(10, 20, 35)));
    }

    static ImageTexture _scorchTex;
    static ImageTexture ScorchTexture()
    {
        if (_scorchTex != null) return _scorchTex;
        var img = Image.CreateEmpty(24, 24, false, Image.Format.Rgba8);
        var rng = new RandomNumberGenerator { Seed = 77 };
        for (int y = 0; y < 24; y++)
        for (int x = 0; x < 24; x++)
        {
            float d = new Vector2(x - 11.5f, y - 11.5f).Length() / 12f + rng.RandfRange(-0.15f, 0.15f);
            float a = d < 0.45f ? 0.9f : d < 1f ? 0.9f * (1f - (d - 0.45f) / 0.55f) : 0f;
            float c = d < 0.3f ? 0.05f : 0.12f;
            img.SetPixel(x, y, new Color(c, c * 0.9f, c * 0.8f, a));
        }
        return _scorchTex = ImageTexture.CreateFromImage(img);
    }

    /// <summary>Jets on the ground and burning wrecks block the way; the rest of the slots park far away.</summary>
    void UpdateObstacles()
    {
        int slot = 0;
        void Put(Vector2 c, float r, Vector2 v)
        {
            if (slot < ObstacleSlots) _t.Obstacles[_obsBase + slot++] = new Obstacle(c, r, v);
        }
        foreach (var j in _jets)
        {
            if (j.Pos.Y - _t.MeshHeight(j.Pos.X, j.Pos.Z) > 2f) continue;
            Vector2 h = Heading(j), p = new(j.Pos.X, j.Pos.Z);
            foreach (float z in new[] { -4.5f, -1.5f, 1.5f, 4.5f }) Put(p - h * z, 1.0f, h * j.Speed);
        }
        foreach (var w in _wrecks) Put(new Vector2(w.Pos.X, w.Pos.Z), 2.2f, Vector2.Zero);
        while (slot < ObstacleSlots) Put(new Vector2(-1e4f, -1e4f), 0.1f, Vector2.Zero);
    }

    /// <summary>True while a jet is holding or rolling on the runway and the point is on it.</summary>
    public bool RunwayBusyAt(Vector2 p)
    {
        foreach (var j in _jets)
            if (j.Phase is Phase.Hold or Phase.Takeoff && j.Climb < 3f)
            {
                // Measured along the road itself: the strip bends at its west end.
                int n = _t.Center.Length;
                int ci = _t.NearestCenter(p, out float lat);
                int along = ((ci - _rwFrom) % n + n) % n;
                if (along > n / 2) along -= n;
                return along > HoldAt - 10f && along < _rwLen && lat < _rwHalfWidth + 2f;
            }
        return false;
    }

    /// <summary>True while a jet on the ground (taxiing, holding or rolling) is within r of p.</summary>
    public bool JetOnGroundNear(Vector2 p, float r)
    {
        foreach (var j in _jets)
            if (j.Phase is Phase.Taxi or Phase.Hold or Phase.Takeoff && j.Climb < 3f
                && new Vector2(j.Pos.X, j.Pos.Z).DistanceTo(p) < r)
                return true;
        return false;
    }

    // ------------------------------------------------------------ missiles

    Jet Find(int id)
    {
        foreach (var j in _jets) if (j.Id == id) return j;
        return null;
    }

    /// <summary>Id of the first jet in normal flight (not diving), or -1.</summary>
    public int FlyingJet
    {
        get
        {
            foreach (var j in _jets) if (j.Phase == Phase.Fly) return j.Id;
            return -1;
        }
    }

    /// <summary>Id of the next jet that can still fly a sortie (on the ground or flying, not diving), or -1.</summary>
    public int NextJet
    {
        get
        {
            foreach (var j in _jets) if (j.Phase != Phase.Dive) return j.Id;
            return -1;
        }
    }

    /// <summary>True while the jet is in normal flight (the only time it should fire).</summary>
    public bool IsFlying(int jetId) => Find(jetId)?.Phase == Phase.Fly;

    public Vector3? JetPosition(int jetId) => Find(jetId)?.Pos;
    public Vector3? JetVelocity(int jetId) => Find(jetId)?.Vel;

    /// <summary>Whether the jet still has a missile on that rail (side -1 left, +1 right).</summary>
    public bool HasMissile(int jetId, int side) => side != 0 && Find(jetId) is { } j && j.Rails[side < 0 ? 0 : 1].Visible;

    /// <summary>Missiles in flight.</summary>
    public IReadOnlyList<Missile> Missiles => _missiles;

    /// <summary>Launch the missile on the jet's left (side -1) or right (+1) rail at the target.
    /// False if there is no such jet or that rail is already empty.</summary>
    public bool FireMissile(int jetId, int side, Func<Vector3> target)
    {
        if (!HasMissile(jetId, side)) return false;
        var j = Find(jetId);
        var rail = j.Rails[side < 0 ? 0 : 1];
        rail.Visible = false;
        var m = Missile.Launch(this, _t, _fx, rail.GlobalTransform, j.Vel, target);
        m.Exploded += (p, d) =>
        {
            if (j.Avenger && d < EngineHealth.MissileLethal) j.Hit = true;   // got the car
            MissileExploded?.Invoke(p, d);
        };
        _missiles.Add(m);
        m.TapeFx = TapeFx;
        TapeHost?.Born(m);
        MissileLaunched?.Invoke(m);
        return true;
    }

    void MissileTest(float dt)
    {
        if (_testJet < 0)
        {
            // Wait until the car is ahead of the jet within range (or give up waiting after 6 s).
            int id = FlyingJet;
            if (id < 0) return;
            var j = Find(id);
            Vector3 to = TestTarget() - j.Pos;
            Vector2 flat = new Vector2(to.X, to.Z);
            bool ahead = flat.Length() < 320f && flat.Normalized().Dot(Heading(j)) > 0.5f;
            if (!ahead && j.PhaseTime < 6f) return;
            _testJet = id;
            _testClock = 0;
            FireMissile(_testJet, -1, TestTarget);
            return;
        }
        _testClock += dt;
        if (_testClock >= 2f && HasMissile(_testJet, 1)) FireMissile(_testJet, 1, TestTarget);
        else if (TestRepeat && _testClock >= 2f) _testJet = -1;      // both away: the next jet that is ahead fires
    }

    /// <summary>Test (WORLDTRACE): a hash of every jet's, missile's and wreck's state, so a trace parts at the first changed draw of the random numbers.</summary>
    public ulong StateHash()
    {
        ulong h = 1469598103934665603UL;
        void Mix(float v) { h = (h ^ (uint)BitConverter.SingleToInt32Bits(v)) * 1099511628211UL; }
        foreach (var j in _jets) { Mix(j.Pos.X); Mix(j.Pos.Y); Mix(j.Pos.Z); Mix(j.Yaw); Mix(j.Pitch); Mix(j.Roll); Mix(j.Afterburner); Mix((int)j.Phase); }
        foreach (var m in _missiles) { Mix(m.Pos.X); Mix(m.Pos.Y); Mix(m.Pos.Z); }
        foreach (var w in _wrecks) { Mix(w.Pos.X); Mix(w.Life); }
        Mix(_jets.Count); Mix(_missiles.Count); Mix(_wrecks.Count);
        return h;
    }

    /// <summary>Test hook: position of the first jet, if any.</summary>
    public Vector3? FirstJet => _jets.Count > 0 ? _jets[0].Pos : null;
}
