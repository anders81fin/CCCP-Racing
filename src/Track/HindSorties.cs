using System;
using System.Collections.Generic;
using CCCPRacing.Audio;
using CCCPRacing.Car;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Track;

/// <summary>Bonus Hind exercise: vertical apron departures, airborne firing runs and return to individual pads.</summary>
public partial class HindSorties : Node3D
{
    public BonusExerciseControl Exercise;
    public const int BurstRounds = 120;
    public const float BurstInterval = .04f, BurstStart = -60, BurstEnd = 60;
    public static float BurstOffset(int shot) => Mathf.Lerp(BurstStart, BurstEnd, Math.Clamp(shot, 0, BurstRounds - 1) / (float)(BurstRounds - 1));
    /// <summary>The two exhaust mouths (the model's own space; the Mi-24's front is -Z), the size of each against the player's car, and the speed under which a hovering-at-rest one makes none.</summary>
    internal static readonly Vector3[] SootPipes = { new(-1.21f, 3.2f, 2.5f), new(1.21f, 3.2f, 2.5f) };
    internal const float SootPower = 1.5f, SootMinSpeed = 0.5f;
    enum Phase { Parked, Spool, Lift, Climb, Approach, Lineup, Attack, Return, Descend, Exit, Falling, Wreck }
    sealed class Aircraft
    {
        public Node3D Model, Gear, Rotor, TailRotor, GunMount;
        public MeshInstance3D Flash;
        public RotorSound Engine;
        public MilitaryLights Lights;
        public Node3D Beacons;
        public HindGunSound Gun;
        public Phase Phase;
        public Vector3 Pos, Previous, Velocity;
        public Vector2 Target, Direction, Entry;
        public float Yaw, Roll, Pitch, Speed, Time, ShotClock, SmokeClock, FlightAge, Soot;
        public double RotorTotal, TailTotal;      // the unwrapped angles the world tape records (the wrapped ones above drive the model)
        public float RotorSpeed, RotorAngle, TailRotorAngle, DepartureDelay, TurnRate, VerticalSpeed, GearDown = 1;
        public int Passes, Shots, Slot, Id;
        public bool Fired;
    }
    sealed class Round
    {
        public Vector3 Position, Target, Origin;
        public MeshInstance3D Tracer;
        public float Remaining;
        public float Born, Flight;      // the world tape: when it left the gun (the sorties' clock) and how long it flies if nothing stops it
        public Vector3 Kick;
    }
    sealed class Remnant
    {
        public Node3D Node;
        public Vector3 Velocity, Spin;
        public float Life;
        public bool Falling;
    }
    readonly List<Aircraft> _planes = new();
    readonly List<Round> _rounds = new();
    readonly List<Remnant> _remnants = new();
    readonly List<int> _targets = new();
    readonly RandomNumberGenerator _rng = new() { Seed = 1981 };
    TrackData _track;
    TyreEffects _fx;
    int _obstacles;
    float _next;
    bool _ready;
    int _nextAircraftId;
    Buk21Battery _battery;
    internal Buk21Battery Battery => _battery;
    BonusExerciseLife _life;
    public event Action<Vector3> AttackApproaching;
    public event Action SoupUnshaken;
    int _retaliationPasses;
    Vector2 _retaliationAt;
    public bool CallRetaliation(Vector3 at)
    {
        if(!_ready || _retaliationPasses>0) return false;
        foreach(var p in _planes) if(p.Phase is not (Phase.Wreck or Phase.Falling)) {
            _retaliationAt=Flat(at);_retaliationPasses=2;p.Passes=2;
            if(p.Phase==Phase.Parked) p.DepartureDelay=0;
            else if(Airborne(p) && p.Phase!=Phase.Attack) Plan(p);
            return true;
        }
        return false;
    }
    public void GroundShot(Vector3 at, CarSim car) => _life?.Shot(at, car);
    static readonly bool DebugSorties = OS.HasEnvironment("HINDDBG");
    float _debugClock;
    int _debugImpacts, _debugProtectedImpacts;
    Node3D _effects;
    Mesh _tracerMesh;
    StandardMaterial3D _tracerMaterial;
    public event Action<Vector3> Crashed;
    public event Action<Vector3> CannonImpact;
    public Func<IEnumerable<CarSim>> CannonTargets;
    public event Action<Vector3> AttackStarted;
    public event Action<Vector3> Collision;
    /// <summary>The world tape's view of the aircraft created and freed here (null: nothing records). Set by the race.</summary>
    public ITapeInstanceHost TapeHost;
    public Vector3? FirstJet => _planes.Count > 0 ? _planes[0].Pos : null;
    public int ActiveCount => _planes.Count;
    internal IEnumerable<(Vector3 Position, string Phase, float Yaw, float Roll, float GearDown)> FlightSamples
    {
        get { foreach (var p in _planes) yield return (p.Pos, p.Phase.ToString(), p.Yaw, p.Roll, p.GearDown); }
    }
    Vector2 Flat(Vector3 p) => new(p.X, p.Z);
    Vector2 Heading(Aircraft p) => new(-MathF.Sin(p.Yaw), -MathF.Cos(p.Yaw));
    Vector3 Ground(Vector2 p, float height = 0) => new(p.X, _track.MeshHeight(p.X, p.Y) + height, p.Y);

    public void Init(TrackData track, TyreEffects fx)
    {
        foreach (var _ in InitSteps(track, fx)) { }
    }

    // Keep aircraft/crew construction and reset order identical for old world tapes.
    public IEnumerable<int> InitSteps(TrackData track, TyreEffects fx)
    {
        _track = track; _fx = fx;
        if (track.Id != "bonus") yield break;
        Tape.Version(this, 3); Tape.Part(this, "root");      // the world tape: the sorties' actor (the aircraft are its instances)
        int n = track.Center.Length;
        // Only complete impact corridors outside the field-camp exclusion zone are eligible.
        for (int i = 0; i < n; i += 8)
            if (!Protected(track.Center[i], 30f)) _targets.Add(i);
        _obstacles = track.Obstacles.Count;
        for (int i = 0; i < 15; i++) track.Obstacles.Add(new Obstacle(new Vector2(-10000, -10000), .1f));
        _effects = new Node3D { Name = "HindDebris" }; AddChild(_effects);
        _tracerMesh = Props.Box(.16f, .16f, 1);
        _tracerMaterial = TracerMaterial();
        var batterySite = track.Props.Find(p => p.Type == "buk21");
        if (batterySite != null)
        {
            _battery = new Buk21Battery { Name = "Buk21Battery" }; AddChild(_battery);
            _battery.Init(track, fx, this, batterySite);
            yield return 0;
        }
        _life = new BonusExerciseLife { Name="ExerciseCrew" }; AddChild(_life);
        foreach (var step in _life.InitSteps(track,fx)) yield return step;
        _life.SoupUnshaken += () => SoupUnshaken?.Invoke();
        _ready = true;
        foreach (var step in ResetSteps()) yield return step;
    }

    public void Reset()
    {
        foreach (var _ in ResetSteps()) { }
    }

    IEnumerable<int> ResetSteps()
    {
        foreach (var p in _planes) { p.Gun.Firing = false; TapeHost?.Gone(p.Model); p.Model.QueueFree(); }
        _planes.Clear(); _rounds.Clear(); _next = 5;
        _debugClock = 0; _nextAircraftId = 0;_retaliationPasses=0;
        _battery?.Reset();
        _life?.Reset();
        _debugImpacts = _debugProtectedImpacts = 0;
        _remnants.Clear();
        if (_effects != null) foreach (var child in _effects.GetChildren()) child.QueueFree();
        if (_ready)
        {
            ParkObstacles();
            if (!_tapeDriven) for (int slot = 0; slot < BonusAirbase.Pads.Length; slot++)
            {
                Spawn(5 + slot * 18);
                yield return 0;
            }
            UpdateGroundObstacles();
        }
    }

    void Spawn(float departureDelay = 5)
    {
        int slot = 0;
        while (_planes.Exists(p => p.Slot == slot)) slot++;
        string number = (25 + slot * 7).ToString();
        var model = BuildAircraft(number, out var engine, out var gun);
        AddChild(model);
        var p = new Aircraft { Model = model, Gear = model.GetNode<Node3D>("Gear"), Rotor = model.GetNode<Node3D>("Rotor"), TailRotor = model.GetNode<Node3D>("TailRotor"),
            Beacons=model.GetNode<Node3D>("Beacons"), Lights=model.GetNode<MilitaryLights>("NightLights"), GunMount=model.GetNode<Node3D>("Gun"), Flash = model.GetNode<MeshInstance3D>("Gun/Flash"), Slot = slot, Id = ++_nextAircraftId, Phase = Phase.Parked, DepartureDelay = departureDelay,
            Pos = Ground(BonusAirbase.Pads[slot % BonusAirbase.Pads.Length]), Passes = _rng.RandiRange(1, 2) };
        p.Yaw = BonusAirbase.ParkedYaw;
        p.Engine = engine; p.Gun = gun;
        MarkTape(model, number, () => p.RotorTotal, () => p.TailTotal, engine, gun, p.Flash, () => AirState(p));
        _planes.Add(p); Place(p);
        TapeHost?.Born(model);
    }

    public void Tick(float dt, CarSim car)
    {
        if (!_ready || dt <= 0) return;
        if (_tapeDriven) { TickBesidesAircraft(dt, car); return; }
        _debugClock += dt;
        if(_retaliationPasses>0 && car!=null) _retaliationAt=Flat(car.Pos);
        _next -= dt;
        // Each helicopter owns a separate pad. No taxi or takeoff roll on the driving route.
        if (_next <= 0 && _planes.Count < BonusAirbase.Pads.Length)
        { Spawn(); _next = _rng.RandfRange(13f, 19f); }
        for (int i = _planes.Count - 1; i >= 0; i--)
        {
            var p = _planes[i]; p.Previous = p.Pos; p.Time += dt;
            var before = p.Phase;
            p.Gun.Firing = false; p.Flash.Visible = false;
            if (Step(p, dt)) { TapeHost?.Gone(p.Model); p.Model.QueueFree(); _planes.RemoveAt(i); continue; }
            if (DebugSorties && before != p.Phase)
                GD.Print($"HIND {_debugClock:F1}s #{p.Slot} {before}->{p.Phase}, pos={p.Pos}, target={p.Target}, shots={p.Shots}");
            if(before!=p.Phase && p.Phase==Phase.Attack) _life?.RadioCall(Ground(p.Target),car);
            Place(p);
        }
        Vector3? taxi = null;
        foreach (var plane in _planes)
            if (plane.Phase is Phase.Spool or Phase.Lift) { taxi = plane.Pos; break; }
        _life?.Tick(dt,car,taxi,_battery?.Alert ?? false);
        // Swept body/wing envelopes catch fast crossings between physics frames.
        for (int a = 0; a < _planes.Count; a++)
            for (int b = a + 1; b < _planes.Count; b++)
            {
                var x = _planes[a]; var y = _planes[b];
                if (!Airborne(x) || !Airborne(y)) continue;
                if (!Touches(x, y)) continue;
                Collision?.Invoke((x.Pos + y.Pos) * .5f);
                Fall(x, -1); Fall(y, 1);
            }
        _battery?.Tick(dt);
        for (int i = _rounds.Count - 1; i >= 0; i--)
        {
            var r = _rounds[i]; float step = Math.Min(dt, r.Remaining);
            Vector3 previous=r.Position;
            r.Position = r.Position.Lerp(r.Target, step / Math.Max(.0001f, r.Remaining));
            r.Remaining -= step;
            float firstHit=float.MaxValue;
            foreach(var target in CannonTargets?.Invoke() ?? (car==null?Array.Empty<CarSim>():new[]{car}))
                if(ProjectileHits.Segment(target,previous,r.Position,out float fraction))
                    firstHit=Math.Min(firstHit,fraction);
            if(firstHit<=1)
            {
                r.Position=previous.Lerp(r.Position,firstHit);
                TapeRound(r, Math.Min(r.Flight, _debugClock - r.Born));
                Impact(r.Position);r.Tracer.QueueFree();_rounds.RemoveAt(i);
                continue;
            }
            Vector3 direction = (r.Target - r.Origin).Normalized();
            float length = Math.Min(6.5f, r.Origin.DistanceTo(r.Position));
            r.Tracer.Visible = length > .02f;
            r.Tracer.Transform = new Transform3D(Basis.LookingAt(direction, MathF.Abs(direction.Y) > .98f ? Vector3.Right : Vector3.Up)
                .Scaled(new Vector3(1, 1, Math.Max(.02f, length))), r.Position - direction * length * .5f);
            if (r.Remaining > .0001f) continue;
            TapeRound(r, r.Flight);
            Impact(r.Target); r.Tracer.QueueFree(); _rounds.RemoveAt(i);
        }
        for (int i = _remnants.Count - 1; i >= 0; i--)
        {
            var fragment = _remnants[i];
            if(!GodotObject.IsInstanceValid(fragment.Node)) { _remnants.RemoveAt(i);continue; }
            fragment.Life -= dt;
            if (fragment.Life <= 0) { fragment.Node.QueueFree(); _remnants.RemoveAt(i); continue; }
            if (!fragment.Falling) continue;
            fragment.Velocity += Vector3.Down * 14 * dt;
            fragment.Node.Position += fragment.Velocity * dt;
            fragment.Node.Rotation += fragment.Spin * dt;
            Vector3 at = fragment.Node.Position;
            float ground = _track.MeshHeight(at.X, at.Z);
            if (at.Y < ground + .2f)
            { fragment.Node.Position = new Vector3(at.X, ground + .2f, at.Z); fragment.Falling = false; }
        }
        UpdateGroundObstacles();
    }

    void UpdateGroundObstacles()
    {
        ParkObstacles();
        foreach (var p in _planes)
        {
            if (p.Phase is not (Phase.Parked or Phase.Spool or Phase.Lift or Phase.Descend or Phase.Wreck) || p.Pos.Y > Ground(Flat(p.Pos), 4).Y) continue;
            Vector2 f = Heading(p);
            for (int k = 0; k < 3; k++)
                _track.Obstacles[_obstacles + p.Slot * 5 + k] = new Obstacle(Flat(p.Pos) + f * (k - 1) * 3f,
                    p.Phase == Phase.Wreck ? 2.5f : 2f, Flat(p.Velocity));
            Vector2 side = new(-f.Y, f.X);
            for (int k = 0; k < 2; k++)
                _track.Obstacles[_obstacles + p.Slot * 5 + 3 + k] = new Obstacle(Flat(p.Pos) + side * (k == 0 ? -3.5f : 3.5f),
                    2f, Flat(p.Velocity));
        }
    }

    static bool Airborne(Aircraft p) => p.Phase is Phase.Climb or Phase.Approach or Phase.Lineup or Phase.Attack or Phase.Return or Phase.Descend or Phase.Exit;
    bool Step(Aircraft p, float dt)
    {
        Vector2 at = Flat(p.Pos), heading = Heading(p);
        if (Airborne(p) && p.Phase is not (Phase.Exit or Phase.Return or Phase.Descend))
        {
            p.FlightAge+=dt;
            // End a long sortie at its own pad; never cut an already reserved firing pass short.
            if(p.FlightAge>150 && p.Phase!=Phase.Attack)
            { Exercise?.EndAir(p.Id);p.Phase=Phase.Return;p.Time=0; }
        }
        switch (p.Phase)
        {
            case Phase.Parked:
                if (p.Time >= p.DepartureDelay) { p.Phase = Phase.Spool; p.Time = 0; }
                break;
            case Phase.Spool:
                // Spin up in place, well beyond the driving road and the bypass.
                if (p.Time > 4f) { p.Phase = Phase.Lift; p.Time = 0; }
                break;
            case Phase.Lift:
                p.Pos.Y = Mathf.MoveToward(p.Pos.Y, Ground(Flat(p.Pos), 18).Y, dt * 3f);
                p.Pitch = 0;
                if (p.Pos.Y >= Ground(Flat(p.Pos), 18).Y - .1f)
                { p.Phase = Phase.Climb; p.Time = 0; }
                break;
            case Phase.Climb:
                // Translate only once the entire helicopter has cleared road traffic.
                p.Speed = Mathf.MoveToward(p.Speed, 30, dt * 3);
                p.Pos += new Vector3(heading.X * p.Speed, 2, heading.Y * p.Speed) * dt;
                p.Pitch = Mathf.MoveToward(p.Pitch, -.08f, dt * .08f);

                if (p.Time > 4) Plan(p);
                break;
            case Phase.Approach:
                Fly(p, p.Entry, 18, dt);
                // Capture a broad approach waypoint before aligning the firing pass.
                if (at.DistanceTo(p.Entry) < 85)
                { p.Phase = Phase.Lineup; p.Time = 0; }
                else if (p.Time > 35) Plan(p);
                break;
            case Phase.Lineup:
                Fly(p, p.Target, 10, dt, 24);
                Vector2 toTarget = p.Target - at;
                float entryDistance = toTarget.Length();
                Vector2 attackDirection = toTarget.Normalized();
                // Lock the actual aligned run-in, rather than demanding an exact
                // heading through a point the aircraft has already flown past.
                if (entryDistance > 130 && Heading(p).Dot(attackDirection) > .98f && SafeAttack(p.Target, attackDirection)
                    && (Exercise==null || Exercise.ReserveAir(p.Id)))
                {
                    p.Direction = attackDirection;
                    p.Phase = Phase.Attack; p.Time = 0; p.Shots = 0; p.Fired = false; p.ShotClock = 0;
                    WarnRun(p);
                }
                // An approach that missed its alignment point is flown again, not snapped into place.
                else if (entryDistance < 115 || p.Time > 24) Plan(p);
                break;
            case Phase.Attack:
                bool pullUp = (at - p.Target).Dot(p.Direction) > 20;
                Fly(p, p.Target + p.Direction * 180, pullUp ? 22 : 6, dt, pullUp ? 30 : 24);
                float along = (Flat(p.Pos) - p.Target).Dot(p.Direction);
                // Keep the firing aircraft in the same driving-camera view as its impacts.
                // The pass stays above vehicle height while the last rounds still fire.
                if ((along >= -110 || p.Fired) && p.Shots < BurstRounds && p.Time>=BonusExerciseControl.WarningLead)
                {
                    if (!p.Fired) AttackStarted?.Invoke(Ground(p.Target));
                    p.Gun.Firing = true; p.Flash.Visible = true; p.Fired = true;
                    p.Flash.Scale = new Vector3(.85f, .85f, p.Shots % 2 == 0 ? 2.5f : 1.7f);
                    p.ShotClock -= dt;
                    while (p.ShotClock <= 0 && p.Shots < BurstRounds)
                    { p.ShotClock += BurstInterval; Fire(p); }
                }
                if ((along > 90 && p.Shots >= BurstRounds) || p.Time > 20)
                {
                    Exercise?.EndAir(p.Id);
                    if (!p.Fired) { Plan(p); break; }
                    if (--p.Passes > 0) Plan(p);
                    else { p.Phase = Phase.Return; p.Time = 0; }
                }
                break;
            case Phase.Return:
                Vector2 home = BonusAirbase.Pads[p.Slot % BonusAirbase.Pads.Length];
                Vector2 delta = home - at;
                if (delta.Length() < 2)
                    p.Yaw += Math.Clamp(Mathf.AngleDifference(p.Yaw, BonusAirbase.ParkedYaw), -.45f * dt, .45f * dt);
                else Steer(p, home, dt, .45f);
                // Brake before the pad. Translation has inertia; hover translation tilts the airframe.
                float alignment=Math.Max(0,Heading(p).Dot(delta.Normalized()));
                float desiredSpeed=Math.Min(22,delta.Length()*.45f);
                if(delta.Length()>65) desiredSpeed*=alignment;
                p.Speed = Mathf.MoveToward(p.Speed, desiredSpeed, dt * 3);
                Vector2 desiredVelocity=(delta.Length()>65?Heading(p):delta.Normalized())*p.Speed;
                Vector2 horizontal=Flat(p.Velocity).MoveToward(desiredVelocity,dt*3.5f);
                Vector2 position=at+horizontal*dt;
                float ceiling = Math.Max(_track.MeshHeight(position.X, position.Y) + 20, Ground(home, 20).Y);
                p.Pos = new Vector3(position.X, Mathf.MoveToward(p.Pos.Y, ceiling, dt * 4), position.Y);
                float acceleration=(horizontal-Flat(p.Velocity)).Dot(Heading(p))/dt;
                p.Pitch=Mathf.Lerp(p.Pitch,Math.Clamp(-acceleration*.045f-p.Speed*.002f,-.20f,.22f),1-MathF.Exp(-dt*2));

                if (delta.Length() < .5f && MathF.Abs(Mathf.AngleDifference(p.Yaw, BonusAirbase.ParkedYaw)) < .02f)
                { p.Phase = Phase.Descend; p.Time = 0; }
                break;
            case Phase.Descend:
                Vector3 landing = Ground(BonusAirbase.Pads[p.Slot % BonusAirbase.Pads.Length]);
                p.Pos = p.Pos.MoveToward(landing, dt * 2.5f);
                p.Pitch = Mathf.MoveToward(p.Pitch, 0, dt * .1f);
                p.Roll = Mathf.MoveToward(p.Roll, 0, dt * .1f);
                p.Speed = 0;
                if (p.Pos.DistanceTo(landing) < .05f)
                {
                    // Touched down: stay on the pad (the mechanic goes to work on it) until the next sortie.
                    p.Phase = Phase.Parked; p.Time = 0; p.DepartureDelay = _rng.RandfRange(14f, 20f);
                    p.Pos = landing; p.Speed = 0; p.Shots = 0; p.FlightAge = 0; p.Fired = false;
                    p.Passes = _rng.RandiRange(1, 2); p.VerticalSpeed = 0;
                }
                break;
            case Phase.Exit:
                p.Pos += new Vector3(heading.X * 34, 4, heading.Y * 34) * dt;
                p.Pitch = Mathf.Lerp(p.Pitch, .1f, Math.Min(1, dt * 2));
                float map = _track.WorldSize;
                if (p.Time > 2 && (p.Pos.X < -100 || p.Pos.Z < -100 || p.Pos.X > map + 100 || p.Pos.Z > map + 100)) return true;
                break;
            case Phase.Falling:
                p.Velocity += Vector3.Down * 14 * dt;
                p.Pos += p.Velocity * dt;
                p.Roll += dt * (p.Slot % 2 == 0 ? 2.6f : -2.9f); p.Pitch -= dt * .4f;
                Trail(p, dt);
                if (p.Pos.Y <= _track.MeshHeight(p.Pos.X, p.Pos.Z) + .5f)
                {
                    p.Pos.Y = _track.MeshHeight(p.Pos.X, p.Pos.Z) + .3f;
                    p.Phase = Phase.Wreck; p.Time = 0; p.Velocity = Vector3.Zero;
                    p.Engine.Stop();
                    p.Model.Scale = new Vector3(1, .55f, 1);
                    Explode(p.Pos); Crashed?.Invoke(p.Pos);
                }
                break;
            case Phase.Wreck:
                Trail(p, dt);
                if (p.Time > 16) return true;
                break;
        }
        if (p.Phase is not (Phase.Falling or Phase.Wreck)) p.Velocity = (p.Pos - p.Previous) / dt;
        if (_fx != null && p.Phase is not (Phase.Parked or Phase.Spool or Phase.Falling or Phase.Wreck) && p.Velocity.Length() > SootMinSpeed)
            foreach (var pipe in SootPipes)      // the player's diesel smoke from both exhausts (HindReplayFx makes the same in a replay)
                _fx.EngineSoot(p.Model.GlobalTransform * pipe, p.Model.GlobalTransform.Basis.Z, p.Velocity, Mathf.Clamp(p.Engine.Level, 0.3f, 1f), SootPower, ref p.Soot, dt, kerosene: true);
        bool gearWanted=p.Phase is Phase.Parked or Phase.Spool or Phase.Lift or Phase.Descend
            || p.Phase==Phase.Climb && p.Time<1 || p.Phase==Phase.Return && Flat(p.Pos).DistanceTo(BonusAirbase.Pads[p.Slot%BonusAirbase.Pads.Length])<65;
        p.GearDown=Mathf.MoveToward(p.GearDown,gearWanted?1:0,dt*.42f);
        p.Gear.Visible=p.GearDown>.01f;
        foreach(Node3D leg in p.Gear.GetChildren()) leg.Rotation=new Vector3((1-p.GearDown)*1.35f,0,0);
        // The rotor spins down slowly after landing (about 20 s to a stop) and a new spool-up starts from whatever speed is left.
        p.RotorSpeed=p.Phase switch {
            Phase.Spool=>Math.Max(p.RotorSpeed,Math.Min(1,p.Time/4f)),
            Phase.Parked=>Math.Max(0,p.RotorSpeed-dt*(.02f+.07f*p.RotorSpeed)),
            Phase.Wreck=>0,
            _=>1 };
        float rotorStep=dt*(MathF.Tau*4)*p.RotorSpeed;
        p.RotorAngle=Mathf.PosMod(p.RotorAngle+rotorStep,MathF.Tau);
        p.TailRotorAngle=Mathf.PosMod(p.TailRotorAngle+rotorStep*4.5f,MathF.Tau);
        p.RotorTotal+=rotorStep; p.TailTotal+=rotorStep*4.5f;
        if(p.Phase!=Phase.Attack) p.GunMount.Rotation=p.GunMount.Rotation.Lerp(Vector3.Zero,1-MathF.Exp(-dt*2));
        if (p.Phase is Phase.Lift or Phase.Descend && _fx != null && _rng.Randf() < dt * 12)
        {
            float angle = _rng.RandfRange(0, MathF.Tau);
            Vector3 outward = new(MathF.Sin(angle), 0, MathF.Cos(angle));
            Vector3 wash = Ground(Flat(p.Pos), .3f) + outward * 4;
            WashFx(_fx, wash, outward);
            TapeFx?.Fx(new TapeEvent(0f, TapeEventType.Effect, Kind: TapeFxKind.RotorWash, Effect: TapeEffect.Smoke, Pos: wash, Dir: outward, Life: 2f));
        }
        p.Engine.Level = p.Phase is Phase.Spool ? Mathf.Lerp(.2f, .9f, Math.Clamp(p.Time / 4f, 0, 1)) : p.Phase is Phase.Parked ? .9f * p.RotorSpeed : p.Phase is Phase.Wreck ? 0 : p.Phase is Phase.Falling ? .1f : .9f;
        return false;
    }

    void Fly(Aircraft p, Vector2 goal, float height, float dt, float speed = 30)
    {
        // Decelerate for the intercept instead of orbiting a point inside the bank-limited turn radius.
        // The old fixed 30 m/s approach needs a roughly 109 m turn, larger than its 85 m capture gate.
        if(p.Phase is Phase.Approach or Phase.Lineup)
        {
            float error=MathF.Abs(Mathf.AngleDifference(p.Yaw,TrackData.YawFor(goal-Flat(p.Pos))));
            speed*=1-.5f*Math.Clamp(error/MathF.PI,0,1);
        }
        Steer(p, goal, dt, .8f);
        float previousSpeed=p.Speed;
        p.Speed = Mathf.MoveToward(p.Speed, speed, dt * 3.5f);
        Vector2 xy = Flat(p.Pos) + Heading(p) * p.Speed * dt;
        float clearance = height <= 10 ? 14 : 18;
        // Keep waiting/approaching aircraft in separate height bands above the active firing pass.
        // Only the aircraft with the exercise reservation descends into the attack corridor.
        if(p.Phase is Phase.Approach or Phase.Lineup) clearance=30+p.Slot*14;
        // Look along the upcoming turning arc early enough to climb before a ridge.
        // The acceleration-limited climb cannot recover from a twelve-metre look-ahead at 30 m/s.
        float ground=_track.MeshHeight(xy.X,xy.Y);
        Vector2 ahead=xy;
        for(int sample=0;sample<6;sample++)
        {
            ahead+=Heading(p).Rotated(-p.TurnRate*(sample+.5f)*.5f)*Math.Max(8,p.Speed)*.5f;
            ground=Math.Max(ground,_track.MeshHeight(ahead.X,ahead.Y));
        }
        float desired = Math.Max(ground + clearance, _track.MeshHeight(p.Target.X, p.Target.Y) + height);
        float desiredRise = Math.Clamp((desired - p.Pos.Y) * .8f, -6, 6);
        p.VerticalSpeed=Mathf.MoveToward(p.VerticalSpeed,desiredRise,dt*3);
        p.Pos = new Vector3(xy.X, p.Pos.Y + p.VerticalSpeed * dt, xy.Y);
        float acceleration=(p.Speed-previousSpeed)/dt;
        float attitude=Math.Clamp(-p.Speed*.0025f-acceleration*.04f+p.VerticalSpeed*.025f,-.23f,.22f);
        p.Pitch = Mathf.Lerp(p.Pitch,attitude,1-MathF.Exp(-dt*2));
    }
    // With local forward -Z, positive yaw turns left and positive roll lowers the left side.
    internal static float BankForTurn(float speed,float yawRate) =>
        Math.Clamp(MathF.Atan2(speed*yawRate,9.81f),-.70f,.70f);

    void Steer(Aircraft p, Vector2 goal, float dt, float rate)
    {
        float error = Mathf.AngleDifference(p.Yaw, TrackData.YawFor(goal - Flat(p.Pos)));
        // A fast turn needs bank: cap yaw rate by the lift-vector tilt the model can show.
        float allowed=Math.Min(rate,9.81f*MathF.Tan(.70f)/Math.Max(4,p.Speed));
        float turn = Math.Clamp(error * 1.8f, -allowed, allowed);
        p.TurnRate=Math.Clamp(Mathf.MoveToward(p.TurnRate,turn,dt*.9f),-allowed,allowed);
        p.Yaw += p.TurnRate * dt;
        p.Roll = Mathf.Lerp(p.Roll,BankForTurn(p.Speed,p.TurnRate),1-MathF.Exp(-dt*2));
    }
    void Plan(Aircraft p)
    {
        int retaliationIndex=-1;
        if(_retaliationPasses>0) {
            float best=float.MaxValue;
            foreach(int candidate in _targets) {
                float distance=_track.Center[candidate].DistanceSquaredTo(_retaliationAt);
                if(distance<best) {best=distance;retaliationIndex=candidate;}
            }
        }
        for (int attempt = 0; attempt < 80 && _targets.Count > 0; attempt++)
        {
            int index = attempt==0 && retaliationIndex>=0?retaliationIndex:_targets[_rng.RandiRange(0, _targets.Count - 1)];
            Vector2 road = _track.CenterDir(index), normal = new(-road.Y, road.X);
            Vector2 direction = normal.Rotated(_rng.RandfRange(-.25f, .25f));
            Vector2 target = _track.Center[index];
            // Use the nearer end of the same random crossing, avoiding an unnecessary
            // trip across the map and a 180-degree turn at the far end.
            if ((Flat(p.Pos) - target).Dot(direction) > 0) direction = -direction;
            if (!SafeAttack(target, direction)) continue;
            if(index==retaliationIndex) _retaliationPasses--;
            p.Target = target; p.Direction = direction; p.Entry = target - direction * 280;
            p.Phase = Phase.Approach; p.Time = 0;
            return;
        }
        p.Phase = Phase.Exit; p.Time = 0;
    }
    bool SafeAttack(Vector2 target, Vector2 direction)
    {
        int road = _track.NearestCenter(target, out _);
        if (MathF.Abs(direction.Dot(_track.CenterDir(road))) > .7f) return false;
        for (int s = (int)BurstStart; s <= BurstEnd; s += 2)
            if (Protected(target + direction * s, 15)) return false;
        return true;
    }
    bool Protected(Vector2 point, float margin) => BonusAirbase.Protected(point, margin) || (_track.MaintenanceHall?.Contains(point,margin) ?? false);
    void WarnRun(Aircraft p)
    {
        Vector3 target=Ground(p.Target);
        AttackApproaching?.Invoke(target);
        int road=_track.NearestCenter(p.Target,out _);
        Vector2 side=_track.CenterDir(road).Orthogonal()*(_track.HalfWidth[road]+2.2f);
        var warning=new HindRunWarning();
        _effects.AddChild(warning);
        warning.Init(_track,p.Target-side,p.Target+side);
        Vector2 left=p.Target-side,right=p.Target+side;
        TapeFx?.Fx(new TapeEvent(0f, TapeEventType.Effect, Kind: TapeFxKind.RunWarning, Effect: TapeEffect.RunWarning, Pos: new Vector3(left.X,0,left.Y), Dir: new Vector3(right.X,0,right.Y), Life: 14f));
    }
    void Fire(Aircraft p)
    {
        // One uninterrupted burst walks from open ground through the road to the far side.
        float along = BurstOffset(p.Shots);
        Vector2 side = new(-p.Direction.Y, p.Direction.X);
        Vector2 xy = p.Target + p.Direction * along + side * _rng.RandfRange(-.8f, .8f);
        p.Shots++;
        if (DebugSorties && p.Shots == 1) GD.Print($"HIND {_debugClock:F1}s #{p.Slot} firing across {p.Target}");
        if (Protected(xy, 12)) return;
        Vector3 target = Ground(xy, .08f);
        p.Model.Position=p.Pos;p.Model.Rotation=new(p.Pitch,p.Yaw,p.Roll);
        Vector3 local=p.Model.ToLocal(target)-p.GunMount.Position;
        float yaw=Math.Clamp(MathF.Atan2(-local.X,-local.Z),-1.2f,1.2f);
        float pitch=Math.Clamp(MathF.Atan2(local.Y,new Vector2(local.X,local.Z).Length()),-.85f,.25f);
        p.GunMount.Rotation=new(pitch,yaw,0);
        Vector3 muzzle = p.GunMount.GlobalTransform * new Vector3(0,0,-1.3f);
        var tracer = new MeshInstance3D { Mesh = _tracerMesh, MaterialOverride = _tracerMaterial, Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        _effects.AddChild(tracer);
        float flight = muzzle.DistanceTo(target) / 180f;
        _rounds.Add(new Round { Origin = muzzle, Position = muzzle, Target = target, Tracer = tracer,
            Remaining = flight, Born = _debugClock, Flight = flight, Kick = p.Velocity * .2f });
        MuzzleFx(_fx, muzzle, p.Velocity * .2f);
    }
    void Impact(Vector3 at)
    {
        if (DebugSorties)
        {
            _debugImpacts++;
            if (Protected(Flat(at), 0)) _debugProtectedImpacts++;
            if (_debugImpacts % 50 == 0)
                GD.Print($"HIND {_debugClock:F1}s impacts={_debugImpacts}, protected field impacts={_debugProtectedImpacts}");
        }
        bool hard = _track.Sample(at.X, at.Z).Surface is SurfaceType.Concrete or SurfaceType.Runway;
        ImpactFx(_fx, _rng, at, hard, _effects);
        TapeFx?.Fx(new TapeEvent(0f, TapeEventType.Effect, Kind: TapeFxKind.HindImpact, Effect: TapeEffect.ShellImpact, Pos: at, Param: hard ? 1f : 0f, Life: 3.5f));
        CannonImpact?.Invoke(at);
    }
    bool Touches(Aircraft a, Aircraft b)
    {
        // Swept fuselage and rotor envelopes, including vertical separation.
        Vector3 A(Aircraft p, bool wing, float side, bool old)
        {
            Vector3 local = wing ? new Vector3(side * 8.5f, 4.2f, .3f) : new Vector3(0, 1.7f, side * 8f);
            return (old ? p.Previous : p.Pos) + p.Model.Basis * local;
        }
        foreach (bool aw in new[] { false, true })
            foreach (bool bw in new[] { false, true })
                for (int sample = 0; sample <= 3; sample++)
                {
                    float t = sample / 3f;
                    Vector3 a0 = A(a, aw, -1, true).Lerp(A(a, aw, -1, false), t);
                    Vector3 a1 = A(a, aw, 1, true).Lerp(A(a, aw, 1, false), t);
                    Vector3 b0 = A(b, bw, -1, true).Lerp(A(b, bw, -1, false), t);
                    Vector3 b1 = A(b, bw, 1, true).Lerp(A(b, bw, 1, false), t);
                    // Sample each narrow capsule at metre intervals against the other segment.
                    for (int k = 0; k <= 14; k++)
                    {
                        Vector3 point = a0.Lerp(a1, k / 14f), d = b1 - b0;
                        float u = Math.Clamp((point - b0).Dot(d) / Math.Max(.001f, d.LengthSquared()), 0, 1);
                        if (point.DistanceSquaredTo(b0 + d * u) < 1.6f * 1.6f) return true;
                    }
                }
        return false;
    }
    void Fall(Aircraft p, float side)
    {
        Exercise?.EndAir(p.Id);
        p.Phase = Phase.Falling; p.Time = 0; p.Gun.Firing = false; p.Flash.Visible = false;
        p.Velocity += new Vector3(side * 8, -4, -side * 6);
        p.Engine.Level = .15f;
        FallFx(_fx, _rng, _effects, _effects, p.Pos, side, p.Velocity, (piece, velocity, spin) => _remnants.Add(new Remnant { Node = piece, Falling = true, Life = 20, Velocity = velocity, Spin = spin }));
        TapeFx?.Fx(new TapeEvent(0f, TapeEventType.Effect, Kind: TapeFxKind.HindFall, Effect: TapeEffect.Explosion, Pos: p.Pos, Velocity: p.Velocity, Param: side, Life: 20f));
    }
    void Trail(Aircraft p, float dt)
    {
        p.SmokeClock -= dt;
        if (p.SmokeClock > 0) return;
        p.SmokeClock += .06f;
        TrailFx(_fx, p.Pos);
        TapeFx?.Fx(new TapeEvent(0f, TapeEventType.Effect, Kind: TapeFxKind.HindTrail, Effect: TapeEffect.Smoke, Pos: p.Pos, Life: 3f));
    }
    void Explode(Vector3 at)
    {
        ExplodeFx(_track, _effects, _effects, at);
        TapeFx?.Fx(new TapeEvent(0f, TapeEventType.Effect, Kind: TapeFxKind.HindCrash, Effect: TapeEffect.JetCrash, Pos: at, Life: 28f));
        // GroundImpactEffects owns its lifetime; only physical debris belongs in _remnants.
    }

    void Place(Aircraft p)
    {
        p.Rotor.Basis = new Basis(Vector3.Right,-p.Pitch*.3f)*new Basis(Vector3.Back,-p.Roll*.22f)*new Basis(Vector3.Up,p.RotorAngle);
        p.TailRotor.Rotation = new Vector3(p.TailRotorAngle, 0, 0);
        p.Lights.Powered=p.Phase is not (Phase.Falling or Phase.Wreck);
        p.Lights.BeamAllowed=p.Phase is Phase.Spool or Phase.Lift or Phase.Climb or Phase.Descend;
        p.Lights.UpdateLights(NightGlow.Level);
        p.Beacons.Visible=p.Lights.Powered && NightGlow.Level>.01f;
        p.Model.Position = p.Pos;
        p.Model.Rotation = new Vector3(p.Pitch, p.Yaw, p.Roll);
    }
    void ParkObstacles()
    {
        for (int i = 0; i < 15; i++) _track.Obstacles[_obstacles + i] = new Obstacle(new Vector2(-10000, -10000), .1f);
    }
    public IEnumerable<(int Id, Vector3 Position, Vector3 Velocity)> AirTargets
    {
        get
        {
            foreach (var p in _planes)
                if (p.Phase is Phase.Approach or Phase.Lineup or Phase.Attack)
                    yield return (p.Id, p.Pos, p.Velocity);
        }
    }
    public bool ShootDown(int id)
    {
        foreach (var p in _planes)
            if (p.Id == id && Airborne(p))
            {
                if (DebugSorties) GD.Print($"HIND {_debugClock:F1}s BUK-21 hit aircraft {id}");
                Fall(p, p.Slot % 2 == 0 ? -1 : 1);
                return true;
            }
        return false;
    }
    /// <summary>A Hind stands parked on this pad (the mechanic works on it only then).</summary>
    public bool PadOccupied(int slot) => _planes.Exists(p => p.Slot == slot && p.Phase == Phase.Parked);
    public bool JetOnGroundNear(Vector2 point, float radius) => _planes.Exists(p => p.Phase is Phase.Parked or Phase.Spool or Phase.Lift or Phase.Descend && Flat(p.Pos).DistanceTo(point) < radius);
    public bool RunwayBusyAt(Vector2 point) => false; // Vertical departures never reserve the runway.
    public void SetAudioPaused(bool paused)
    {
        foreach (var p in _planes)
        {
            p.Engine.StreamPaused = paused || p.Phase is Phase.Wreck || p.Phase is Phase.Parked && p.RotorSpeed < .03f;
            p.Gun.StreamPaused = paused || !p.Gun.Firing;
        }
        _battery?.SetAudioPaused(paused);
        _life?.SetAudioPaused(paused);
        if (_effects != null)
            foreach (var child in _effects.GetChildren())
                if (child is AudioStreamPlayer3D sound) sound.StreamPaused = paused;
    }
}
