using System;
using System.Collections.Generic;
using CCCPRacing.Car;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Track;

/// <summary>
/// Babushkas trudging along the verges, both ways round the track. Tear past one and she turns,
/// shakes her fist and yells at you. Hit one and she goes flying (the Pyatyorka barely notices): she
/// tumbles, lands, lies there a moment, gets up furious and walks back to her verge. A hard,
/// square hit can send her all the way up into the camera lens. Honk at one you are about to
/// run down and she hears it: most dive for the ditch, some just freeze with their hands up.
/// In the rally every other one carries a jerrycan of fuel for the driver (Babushkas.Canister.cs).
/// </summary>
public partial class Babushkas : Node3D
{
    /// <summary>Soldiers instead of grandmothers (the bonus track's <see cref="Troopers"/>): their model, shouts and sway.</summary>
    protected virtual bool Troops => false;

    const float TriggerDistance = 12f;
    const float TriggerSpeed = 11f;          // m/s, ~40 km/h
    const float SizeScale = 1.6f;               // cartoon-sized so she reads from the high camera
    const float Radius = 0.45f * SizeScale;
    /// <summary>Her collision radius (m) against the car's body circles.</summary>
    public const float BodyRadius = Radius;
    const float CarRadius = 0.92f;              // CarSim's body circles
    static readonly float[] CarCircleZ = { -1.3f, 0f, 1.3f };
    const float HitSpeed = 1.2f;                // m/s closing speed that counts as a hit
    const float ComHeight = 0.7f * SizeScale;   // centre of mass above her boots
    const float LieHeight = 0.5f;               // centre of mass above the ground when lying down
    const float LensSpeed = 19.5f;              // m/s (~70 km/h): fast enough to reach the camera
    const float LensChance = 0.4f;
    const float LensFlight = 1.0f;              // seconds up to the glass
    public const float LensHold = 1.7f;         // seconds stuck on the glass (the HUD draws her)
    static readonly string[] Shouts = { "ХУЛИГАН!", "ИДИОТ!", "БАНДИТ!" };
    // The horn: she hears it this far off (m) and reacts only to a car coming at her fast.
    public const float HearRange = 60f;
    const float DangerSpeed = 11.1f;            // m/s, 40 km/h
    const float DangerTime = 3f;                // s until the car reaches her
    const float DangerMiss = 3f;                // m: its path passes this close to her
    const float DiveChance = 0.75f;             // the rest freeze
    const float DiveTime = 0.5f, ProneTime = 2f, FreezeTime = 2.2f;
    const float DiveClear = 3.2f;               // m off the car's path where she wants to land
    /// <summary>What she yells after a dive into the ditch (I remember you / I'll show you / ...).</summary>
    static readonly string[] MilitaryShouts = { "ДОРОГУ СЛУЖБЕ!", "СМОТРИ КУДА ЕДЕШЬ!", "ТОВАРИЩ, ОСТОРОЖНО!" };
    static readonly string[] MilitaryDiveShouts = { "ЛОЖИСЬ!", "ДОЛОЖУ КОМАНДИРУ!", "БЕРЕГИСЬ!" };
    static readonly string[] DiveShouts = { "Я ТЕБЯ ЗАПОМНИЛА!", "Я ТЕБЕ ПОКАЖУ!", "СОВСЕМ ОБНАГЛЕЛ!", "Я ВСЕ ВИДЕЛА!" };

    /// <summary>What the Ministry says when a babushka is launched.</summary>
    public static readonly string[] HitSlogans =
    {
        "BABUSHKA IN ORBIT!",
        "A COSMONAUT'S GRANDMOTHER!",
        "FIRST WOMAN IN SPACE!",
        "POYEKHALI, BABUSHKA!",
        "SPUTNIK LAUNCHED!",
    };

    enum Mode { Walk, Fly, Lens, OnGlass, Down, Rise, Angry, Return, Dive, Freeze, Offer }

    class Walker
    {
        public int Index;
        public float S, Dir, Speed, Lateral, Phase, Angry, Yaw;
        public Vector2 Pos;
        public Node3D Root, Body, ArmL, ArmR;
        public string Shout;
        public Color Scarf;
        public Mode Mode;
        // Flight: centre of mass, velocity, spin (world axis * rad/s), orientation.
        public Vector3 C, V, Spin, LensFrom, LensCtrl;
        public Basis Ori = Basis.Identity;
        public Quaternion RiseFrom;
        public Vector3 RiseFromC;
        public Vector2 LaunchAt;
        public float Timer, Peak, Launch;
        // The horn: seconds until she reacts (-1 none), the dive, and a pause before she can be startled again.
        public float React = -1, HornCd;
        public Vector2 DiveFrom, DiveTo;
        public Quaternion DiveUp, DiveProne;
        public bool Dived;
        // The canister (rally): she carries one (Carrier), has it now (HasCan), gets a new one when
        // CanCd runs out; Offer: holding it out at the verge; a word of her own (Say) for a while.
        public bool Carrier, HasCan, Golden;
        public CarSim OfferCar;
        public float CanCd, OfferCd, OfferTime, PrevAlong = float.NaN, SayTime;
        public string Say;
        public Node3D Can, GoldCan, GoldVest, LegL, LegR;
    }

    TrackData _t;
    readonly List<Walker> _w = new();
    readonly RandomNumberGenerator _rng = new() { Seed = 1945 };
    readonly RandomNumberGenerator _hornRng = new();     // reactions differ every run
    static readonly bool HornDebug = OS.HasEnvironment("HORNDBG");
    readonly bool _debug = OS.HasEnvironment("BABUDBG");
    float _dbgAfter = -1, _dbgBefore;
    int _dbgWalker = -1;
    CarSim _dbgCar;

    /// <summary>Test hook: true sends every hit faster than ~30 km/h into the camera lens, false never.</summary>
    public bool? ForceLens;

    public Vector2 WalkerPosition(int i) => _w[Math.Clamp(i, 0, _w.Count - 1)].Pos;

    /// <summary>Test hook: make one babushka furious for a while.</summary>
    public void Enrage(int i, float seconds)
    {
        var w = _w[Math.Clamp(i, 0, _w.Count - 1)];
        w.Shout = Shouts[0];
        w.Angry = seconds;
        if (w.Mode == Mode.Walk) w.Mode = Mode.Angry;
    }

    /// <summary>
    /// Test hook: put the car on the road some metres before babushka i, rolling straight at
    /// where she will be, at the given speed (m/s) and in a sensible gear. A negative speed
    /// comes at her against the race direction.
    /// </summary>
    public void AimCar(CarSim car, int i, float speed, float distance)
    {
        var w = _w[Math.Clamp(i, 0, _w.Count - 1)];
        int n = _t.Center.Length;
        int k = (int)w.S % n;
        Vector2 dir = _t.CenterDir(k) * Math.Sign(speed);
        speed = Math.Abs(speed);
        Vector2 side = new(-dir.Y, dir.X);
        // Start on the near half of the road and aim at her predicted position.
        float lane = Math.Clamp(w.Lateral * 0.4f, -_t.HalfWidth[k] * 0.5f, _t.HalfWidth[k] * 0.5f);
        Vector2 from = _t.Center[k] - dir * distance + side * lane;
        Vector2 target = w.Pos + _t.CenterDir(k) * w.Dir * w.Speed * (distance / Math.Max(speed, 3f));
        SetRolling(car, from, (target - from).Normalized(), speed);
    }

    /// <summary>What angry (or airborne) babushkas are yelling, anchored above their heads (for the HUD).</summary>
    public IEnumerable<(Vector3 pos, string text)> ActiveShouts()
    {
        foreach (var w in _w)
        {
            if (w.SayTime > 0 && w.Say != null && w.Mode is Mode.Walk or Mode.Angry or Mode.Return or Mode.Offer)
                yield return (w.Root.GlobalPosition + Vector3.Up * 2.4f * SizeScale, w.Say);
            else if (w.Mode == Mode.Fly && w.Launch > 12f)
                yield return (w.C + Vector3.Up * 2.2f, "ОЙ!");
            else if (w.Mode == Mode.Freeze)
                yield return (w.Root.GlobalPosition + Vector3.Up * 2.4f * SizeScale, "ОЙ!");
            else if (w.Angry > 0 && w.Mode is Mode.Walk or Mode.Angry or Mode.Return)
                yield return (w.Root.GlobalPosition + Vector3.Up * 2.4f * SizeScale, w.Shout);
        }
    }

    /// <summary>How many babushkas the car launched this step.</summary>
    public int HitsThisTick { get; private set; }
    /// <summary>Closing speed of the hardest hit this step, m/s.</summary>
    public float HitSpeedThisTick { get; private set; }
    /// <summary>A launched babushka is on her way into the camera (set on the hit step).</summary>
    public bool LensBoundThisTick { get; private set; }
    /// <summary>A babushka smacked into the camera lens this step.</summary>
    public bool LensHitThisTick { get; private set; }
    /// <summary>Babushkas who dived for the ditch and got up unhurt this step.</summary>
    public int DodgedThisTick { get; private set; }
    /// <summary>Headscarf colour of the one on the glass (for the HUD).</summary>
    public Color LensScarf { get; private set; }
    int _lensSerial;
    public int LensSeedThisTick { get; private set; }

    /// <summary>(index) the car launched this babushka — fired during <see cref="Tick"/>.</summary>
    public event Action<int> Hit;
    /// <summary>The world tape's view of what the hits make (the thump and the burst can): set by the race (docs/replay-tape.md, section 15).</summary>
    public ITapeFxHost TapeFx;
    /// <summary>Index of the Balalaika pilot's grandmother this run (a surprise: she looks like all the others).</summary>
    public int PilotMother { get; private set; } = -1;
    /// <summary>Index of the tank driver's grandmother this run.</summary>
    public int TankMother { get; private set; } = -1;
    /// <summary>Tests (--pilot-mother / --tank-mother): babushka 0 is always that grandmother.</summary>
    public bool ForcePilotMother, ForceTankMother;
    ulong? _testSeed;
    /// <summary>Test: the same mothers drawn and the same reactions each run (else they differ every run).</summary>
    public void SeedForTest(ulong seed) { _testSeed = seed; _hornRng.Seed = seed; }
    /// <summary>The driving school: a startled babushka always dives for the ditch (if there is room), never freezes.</summary>
    public bool ForceDive;
    /// <summary>(index) she heard the horn and has just dived or frozen.</summary>
    public event Action<int> Startled;

    /// <summary>The driving school: the walkers within r m of p (but except) are sent further along the lap, out of the lesson's picture.</summary>
    public void KeepAway(Vector2 p, float r, int except = -1)
    {
        int n = _t.Center.Length;
        foreach (var w in _w)
        {
            if (w.Index == except || w.Pos.DistanceTo(p) > r) continue;
            float s0 = w.S;
            for (int k = 1; k < 8 && w.Pos.DistanceTo(p) <= r; k++)      // the lap loops back on itself: a place truly away
            {
                w.S = (s0 + k * n / 8f) % n;
                w.Pos = PathPos(w);
            }
            w.Mode = Mode.Walk;
            w.Pos = PathPos(w);
            Place(w, 0, null);
        }
    }

    /// <summary>
    /// The driving school: babushka i walks (no can in hand) at road metre at, lateral m from the
    /// centre line (+ = the right of the race direction), in the race direction.
    /// </summary>
    public void PutOnRoad(int i, int at, float lateral)
    {
        var w = _w[Math.Clamp(i, 0, _w.Count - 1)];
        int n = _t.Center.Length;
        w.S = (at % n + n) % n;
        w.Dir = 1f;
        w.Speed = 0.9f;
        w.Lateral = lateral;
        w.Angry = 0;
        w.React = -1;
        w.HornCd = 0;
        w.Dived = false;
        w.Mode = Mode.Walk;
        w.HasCan = false;
        ShowCan(w, false);
        w.Pos = PathPos(w);
        Place(w, 0, null);
    }

    static readonly Color[] Scarves = { new(0.8f, 0.15f, 0.12f), new(0.9f, 0.7f, 0.2f), new(0.25f, 0.45f, 0.75f), new(0.85f, 0.85f, 0.8f) };
    static readonly Color[] Skirts = { new(0.2f, 0.2f, 0.3f), new(0.35f, 0.22f, 0.15f), new(0.22f, 0.3f, 0.22f) };
    static readonly Color[] Coats = { new(0.3f, 0.28f, 0.25f), new(0.45f, 0.3f, 0.3f), new(0.3f, 0.34f, 0.42f) };
    static readonly Color Skin = new(0.86f, 0.68f, 0.58f);

    public void Init(TrackData t, int count)
    {
        _t = t;
        _hornRng.Randomize();
        Tape.Version(this, 1); Tape.Part(this, "root");
        for (int i = 0; i < count; i++)
        {
            var w = new Walker { Index = i };
            BuildModel(w);
            MarkTape(w);
            _w.Add(w);
        }
        Reset();
    }

    /// <summary>The world tape parts of one walker: her root (it moves, the Babushkas node does not), body, arms, a trooper's legs, and the canister / vest nodes that show and hide.</summary>
    static void MarkTape(Walker w)
    {
        string n = $"{w.Index + 1}/";
        Tape.Part(w.Root, n + "walker"); Tape.Part(w.Body, n + "body"); Tape.Part(w.ArmL, n + "armL"); Tape.Part(w.ArmR, n + "armR");
        if (w.LegL != null) { Tape.Part(w.LegL, n + "legL"); Tape.Part(w.LegR, n + "legR"); }
        Tape.Part(w.Can, n + "can"); Tape.Part(w.GoldCan, n + "goldCan");
        if (w.GoldVest != null) Tape.Part(w.GoldVest, n + "goldVest");
    }

    public void Reset()
    {
        int n = _t.Center.Length;
        for (int i = 0; i < _w.Count; i++)
        {
            var w = _w[i];
            w.S = (i + _rng.RandfRange(0.1f, 0.9f)) * n / _w.Count;
            w.Dir = i % 2 == 0 ? 1f : -1f;
            w.Speed = _rng.RandfRange(0.8f, 1.3f);
            float side = _rng.Randf() < 0.5f ? -1f : 1f;
            w.Lateral = side * (_t.HalfWidth[(int)w.S % n] + _rng.RandfRange(-0.4f, 1.0f));
            w.Angry = 0;
            w.React = -1;
            w.HornCd = 0;
            w.Dived = false;
            w.Phase = _rng.RandfRange(0, 10);
            w.Mode = Mode.Walk;
            w.Root.Visible = true;
            ResetCan(w);
            w.Pos = PathPos(w);
            Place(w, 0, null);
        }
        _dbgAfter = -1;
        ClearFlyingCans();
        PickMothers();
    }

    /// <summary>A new run: two different babushkas, picked at random, are the grandmothers (identifiers still say Mother).</summary>
    void PickMothers()
    {
        PilotMother = TankMother = -1;
        int n = _w.Count;
        if (n == 0) return;
        var rng = new RandomNumberGenerator();
        if (_testSeed is ulong seed) rng.Seed = seed; else rng.Randomize();
        PilotMother = rng.RandiRange(0, n - 1);
        TankMother = n > 1 ? (PilotMother + rng.RandiRange(1, n - 1)) % n : -1;
        if (ForcePilotMother) PilotMother = 0;
        if (ForceTankMother) TankMother = 0;
        if (ForcePilotMother && ForceTankMother) TankMother = n > 1 ? 1 : -1;
        else if (ForcePilotMother && TankMother == 0) TankMother = n > 1 ? 1 : -1;
        else if (ForceTankMother && PilotMother == 0) PilotMother = n > 1 ? 1 : -1;
    }

    void BuildModel(Walker w)
    {
        Color scarf = Scarves[_rng.RandiRange(0, Scarves.Length - 1)];
        Color skirt = Skirts[_rng.RandiRange(0, Skirts.Length - 1)];
        Color coat = Coats[_rng.RandiRange(0, Coats.Length - 1)];
        w.Scarf = scarf;
        if(Troops) {BuildSoldier(w);return;}

        w.Root = new Node3D { Scale = Vector3.One * SizeScale };
        AddChild(w.Root);
        w.Body = new Node3D();
        w.Root.AddChild(w.Body);
        // Long skirt down to the boots.
        w.Body.AddChild(Props.Part(Props.Cyl(0.34f, 0.8f, 8, 0.22f), skirt, new Vector3(0, 0.4f, 0)));
        w.Body.AddChild(Props.Part(Props.Box(0.2f, 0.08f, 0.26f), new Color(0.12f, 0.1f, 0.1f), new Vector3(-0.1f, 0.04f, -0.06f)));
        w.Body.AddChild(Props.Part(Props.Box(0.2f, 0.08f, 0.26f), new Color(0.12f, 0.1f, 0.1f), new Vector3(0.1f, 0.04f, -0.06f)));
        // Hunched quilted coat, head and headscarf.
        var torso = new Node3D { Position = new Vector3(0, 0.8f, 0), RotationDegrees = new Vector3(-16, 0, 0) };
        w.Body.AddChild(torso);
        torso.AddChild(Props.Part(Props.Box(0.5f, 0.55f, 0.34f), coat, new Vector3(0, 0.27f, 0)));
        torso.AddChild(Props.Part(Props.Ball(0.13f), Skin, new Vector3(0, 0.66f, -0.04f)));
        torso.AddChild(Props.Part(Props.Ball(0.16f), scarf, new Vector3(0, 0.7f, 0.02f)));
        torso.AddChild(Props.Part(Props.Box(0.1f, 0.12f, 0.08f), scarf, new Vector3(0, 0.52f, -0.14f)));   // knot
        // Arms on shoulder pivots; the right one ends in a fist, the left carries the string bag.
        w.ArmL = new Node3D { Position = new Vector3(-0.3f, 0.5f, 0) };
        w.ArmR = new Node3D { Position = new Vector3(0.3f, 0.5f, 0) };
        torso.AddChild(w.ArmL);
        torso.AddChild(w.ArmR);
        w.ArmL.AddChild(Props.Part(Props.Box(0.12f, 0.5f, 0.12f), coat, new Vector3(0, -0.25f, 0)));
        w.ArmL.AddChild(Props.Part(Props.Box(0.22f, 0.25f, 0.12f), new Color(0.8f, 0.75f, 0.3f), new Vector3(0, -0.6f, 0)));
        w.ArmR.AddChild(Props.Part(Props.Box(0.12f, 0.5f, 0.12f), coat, new Vector3(0, -0.25f, 0)));
        w.ArmR.AddChild(Props.Part(Props.Ball(0.08f), Skin, new Vector3(0, -0.54f, 0)));
        w.Carrier = w.Index % 2 == 1;
        w.Can = BuildCan();
        w.Can.Position = new Vector3(0, -0.66f, 0);
        w.ArmR.AddChild(w.Can);
        w.GoldCan = BuildCan(true);
        w.GoldCan.Position = w.Can.Position;
        w.GoldCan.Visible = false;
        w.ArmR.AddChild(w.GoldCan);
    }

    /// <param name="impact">strongest collision the car had this physics tick (m/s), unused now</param>
    /// <param name="lens">the camera a hard hit may send her into; null = never (title replay)</param>
    /// <param name="horn">the car's horn is sounding</param>
    public void Tick(float dt, CarSim car, float impact, Camera3D lens = null, bool horn = false, IReadOnlyList<CarSim> cars = null)
    {
        HitsThisTick = DodgedThisTick = 0;
        HitSpeedThisTick = 0;
        LensHitThisTick = LensBoundThisTick = false;
        var carPos = new Vector2(car.Pos.X, car.Pos.Z);
        DebugAfterHit(dt, car);
        TickFlyingCans(dt, car);
        foreach (var w in _w)
        {
            if (cars != null && w.Mode == Mode.Offer)
            {
                bool present = false;
                foreach (var candidate in cars) present |= candidate == w.OfferCar;
                if (!present) EndOffer(w);
            }
            CarSim target = w.Mode == Mode.Offer && w.OfferCar != null ? w.OfferCar : car;
            if (cars != null && w.Mode != Mode.Offer)
            {
                float best = float.MaxValue;
                foreach (var candidate in cars)
                {
                    float distance = candidate.Pos.DistanceSquaredTo(new Vector3(w.Pos.X, candidate.Pos.Y, w.Pos.Y));
                    if (distance < best) { best = distance; target = candidate; }
                }
                if (_canisters && w.HasCan && w.Mode is (Mode.Walk or Mode.Angry or Mode.Return))
                {
                    best = float.MaxValue;
                    foreach (var candidate in cars)
                    {
                        var p = new Vector2(candidate.Pos.X, candidate.Pos.Z);
                        if (!Sees(w.Pos, p, new Vector2(candidate.Vel.X, candidate.Vel.Z))) continue;
                        float distance = w.Pos.DistanceSquaredTo(p);
                        if (distance < best) { best = distance; target = candidate; }
                    }
                }
            }
            Vector2 targetPos = new(target.Pos.X, target.Pos.Z);
            CanTimers(w, dt);
            switch (w.Mode)
            {
                case Mode.Walk:
                case Mode.Angry:
                case Mode.Return:
                    if (TryHit(w, target, lens)) break;
                    if (Startle(w, dt, target, horn)) break;
                    if ((cars == null || cars.Count > 0) && StartOffer(w, target, targetPos)) break;
                    Stand(w, dt, target, targetPos);
                    break;
                case Mode.Offer:
                    if (TryHit(w, target, lens)) break;
                    if (Startle(w, dt, target, horn)) break;
                    Offer(w, dt, target, targetPos);
                    break;
                case Mode.Dive:
                    if (TryHit(w, car, lens)) break;
                    Dive(w, dt);
                    break;
                case Mode.Freeze:
                    if (TryHit(w, car, lens)) break;
                    Freeze(w, dt, carPos);
                    break;
                case Mode.Fly: Fly(w, dt); break;
                case Mode.Lens: FlyToLens(w, dt, lens); break;
                case Mode.OnGlass: OnGlass(w, dt, lens); break;
                case Mode.Down:
                    if (w.Dived && TryHit(w, car, lens)) break;     // lying in the ditch: still in harm's way
                    Down(w, dt, carPos);
                    break;
                case Mode.Rise: Rise(w, dt, carPos); break;
            }
        }
    }

    /// <summary>LAN host checks every driver each substep; people impart a light knock, never a wall collision.</summary>
    public void CollideCar(CarSim car)
    {
        foreach(var w in _w)
            if(w.Mode is Mode.Walk or Mode.Angry or Mode.Return or Mode.Offer or Mode.Dive or Mode.Freeze
                || (w.Mode==Mode.Down && w.Dived))
                TryHit(w,car,null);
    }

    // ------------------------------------------------------------ on her feet

    Vector2 PathPos(Walker w)
    {
        int n = _t.Center.Length;
        int i = (int)w.S % n;
        float f = w.S - MathF.Floor(w.S);
        Vector2 c = _t.Center[i].Lerp(_t.Center[(i + 1) % n], f);
        Vector2 dir = _t.CenterDir(i);
        return c + new Vector2(-dir.Y, dir.X) * w.Lateral;
    }

    void Stand(Walker w, float dt, CarSim car, Vector2 carPos)
    {
        int n = _t.Center.Length;
        float d = w.Pos.DistanceTo(carPos);
        if (d < TriggerDistance && car.Speed > TriggerSpeed)
        {
            if (w.Angry <= 0) w.Shout = (Troops?MilitaryShouts:Shouts)[_rng.RandiRange(0, Shouts.Length - 1)];
            w.Angry = Math.Max(w.Angry, 2.5f);
            if (w.Mode == Mode.Walk) w.Mode = Mode.Angry;
        }

        Vector2? face = null;
        if (w.Angry > 0)
        {
            w.Angry -= dt;
            if (w.Angry <= 0 && w.Mode == Mode.Angry)
                w.Mode = w.Pos.DistanceTo(PathPos(w)) > 0.3f ? Mode.Return : Mode.Walk;
        }
        else if (w.Mode == Mode.Return)
        {
            // Back to her verge, a bit quicker than her usual trudge.
            Vector2 to = PathPos(w) - w.Pos;
            float step = 1.7f * dt;
            if (to.Length() <= step) { w.Pos += to; w.Mode = Mode.Walk; }
            else { w.Pos += to.Normalized() * step; face = to.Normalized(); }
            w.Phase += dt * 1.7f * 5f;
        }
        else
        {
            w.Mode = Mode.Walk;
            w.S = ((w.S + w.Dir * w.Speed * dt) % n + n) % n;
            w.Phase += dt * w.Speed * 5f;
            w.Pos = PathPos(w);
        }
        Place(w, dt, w.Angry > 0 ? carPos : null, face);
    }

    void Place(Walker w, float dt, Vector2? lookAt, Vector2? faceDir = null)
    {
        int n = _t.Center.Length;
        Vector2 dir = _t.CenterDir((int)w.S % n);
        float y = _t.MeshHeight(w.Pos.X, w.Pos.Y);

        Vector2 face = faceDir ?? dir * w.Dir;
        if (lookAt is Vector2 target && target.DistanceTo(w.Pos) > 0.5f) face = (target - w.Pos).Normalized();
        float want = TrackData.YawFor(face);
        w.Yaw = dt > 0 ? Mathf.LerpAngle(w.Yaw, want, Math.Min(1f, dt * 6f)) : want;
        w.Root.GlobalTransform = new Transform3D(new Basis(Vector3.Up, w.Yaw).Scaled(Vector3.One * SizeScale), new Vector3(w.Pos.X, y, w.Pos.Y));

        if (w.Angry > 0)
        {
            // Fist up and shaking, the other arm on the hip.
            float shake = MathF.Sin(Time.GetTicksMsec() * 0.05f) * 0.35f;
            w.ArmR.Rotation = new Vector3(2.6f + shake, 0, 0.2f);
            w.ArmL.Rotation = new Vector3(0, 0, -0.6f);
            w.Body.Position = new Vector3(0, MathF.Abs(shake) * 0.05f, 0);
            w.Body.Rotation = Vector3.Zero;
        }
        else
        {
            // Waddle: bob, sway, arms swinging opposite to each other.
            float s = MathF.Sin(w.Phase);
            w.ArmR.Rotation = new Vector3(s * 0.45f, 0, 0);
            w.ArmL.Rotation = new Vector3(-s * 0.3f, 0, 0);
            w.Body.Position = new Vector3(0, MathF.Abs(s) * 0.04f, 0);
            w.Body.Rotation = new Vector3(0, 0, s * (Troops?.025f:.07f));
            if(w.LegL!=null) {w.LegL.Rotation=new Vector3(s*.35f,0,0);w.LegR.Rotation=new Vector3(-s*.35f,0,0);}
        }
    }

    // ------------------------------------------------------------ the horn

    /// <summary>
    /// The car is fast, close and aimed at her: its path passes within <see cref="DangerMiss"/>
    /// and reaches her within <see cref="DangerTime"/>. dir: its direction of travel; lateral:
    /// which side of that path she is on (+ = the (-dir.Y, dir.X) side); eta: seconds to her.
    /// </summary>
    bool InDanger(Walker w, CarSim car, out Vector2 dir, out float lateral, out float eta)
    {
        Vector2 v = new(car.Vel.X, car.Vel.Z);
        float sp = v.Length();
        dir = sp > 0.1f ? v / sp : new Vector2(car.Forward.X, car.Forward.Z).Normalized();
        Vector2 rel = w.Pos - new Vector2(car.Pos.X, car.Pos.Z);
        float along = rel.Dot(dir);
        lateral = dir.Cross(rel);
        eta = along / Math.Max(sp, 0.1f);
        return sp > DangerSpeed && rel.Length() < HearRange && along > 0 && eta < DangerTime && MathF.Abs(lateral) < DangerMiss;
    }

    /// <summary>
    /// Hears the horn of a car about to run her down; a moment later she either dives for the
    /// verge (away from its path) or freezes on the spot. True when she has just done so.
    /// </summary>
    bool Startle(Walker w, float dt, CarSim car, bool horn)
    {
        w.HornCd -= dt;
        if (w.React < 0)
        {
            if (horn && w.HornCd <= 0 && InDanger(w, car, out _, out float lat, out float eta))
            {
                w.React = _hornRng.RandfRange(0.4f, 0.8f);
                w.HornCd = 5f;
                if (HornDebug) GD.Print($"horn: babushka {w.Index} hears it, car {car.Speed * 3.6f:F0} km/h, {eta:F2} s away, path {lat:F1} m off, reacts in {w.React:F2} s");
            }
            return false;
        }
        w.React -= dt;
        if (w.React > 0) return false;
        w.React = -1;
        InDanger(w, car, out Vector2 dir, out float side, out float t);
        if (t < -0.3f) return false;      // it has gone past already
        bool dive = (ForceDive || _hornRng.Randf() < DiveChance) && StartDive(w, dir, side);
        if (!dive)
        {
            w.Mode = Mode.Freeze;
            w.Timer = Math.Max(FreezeTime, t + 0.8f);     // rooted there until the car has been
            w.Angry = 0;
        }
        if (HornDebug) GD.Print($"horn: babushka {w.Index} {(dive ? $"DIVES {w.DiveFrom.DistanceTo(w.DiveTo):F1} m" : "FREEZES")}, car {t:F2} s away, path {side:F1} m off");
        Startled?.Invoke(w.Index);
        return true;
    }

    /// <summary>A leap sideways off the car's path onto open, walkable ground; false if there is none.</summary>
    bool StartDive(Walker w, Vector2 dir, float lateral)
    {
        float side = lateral > 0.15f ? 1f : lateral < -0.15f ? -1f : (_hornRng.Randf() < 0.5f ? 1f : -1f);
        Vector2 perp = new Vector2(-dir.Y, dir.X) * side;
        float h0 = _t.MeshHeight(w.Pos.X, w.Pos.Y);
        float max = _t.WorldSize - 4f;
        float want = Math.Clamp(DiveClear - MathF.Abs(lateral), 2f, 3.4f);
        for (float d = want; d >= 1.2f; d -= 0.4f)
        {
            Vector2 to = w.Pos + perp * d;
            if (to.X < 4f || to.Y < 4f || to.X > max || to.Y > max) continue;
            if (MathF.Abs(_t.MeshHeight(to.X, to.Y) - h0) > 1.5f) continue;     // a bank of earth, a deep pit
            if (Blocked(to) || Blocked(w.Pos + perp * d * 0.5f)) continue;
            w.Mode = Mode.Dive;
            w.Timer = 0;
            w.Angry = 0;
            w.Dived = true;
            w.DiveFrom = w.Pos;
            w.DiveTo = to;
            w.Yaw = TrackData.YawFor(perp);
            w.DiveUp = new Basis(Vector3.Up, w.Yaw).GetRotationQuaternion();
            // Prone: head the way she leapt, face down (her front is -Z).
            Vector3 y = new(perp.X, 0, perp.Y);
            w.DiveProne = new Basis(y.Cross(Vector3.Up), y, Vector3.Up).GetRotationQuaternion();
            return true;
        }
        return false;
    }

    bool Blocked(Vector2 p)
    {
        foreach(var o in _t.Obstacles)
            if (o.Radius > 0.1f && p.DistanceTo(o.Center) < o.Radius + Radius) return true;
        return false;
    }

    /// <summary>The leap: a low arc, stretching out flat, landing face down in the grass.</summary>
    void Dive(Walker w, float dt)
    {
        w.Timer += dt;
        float u = Math.Min(1f, w.Timer / DiveTime);
        w.Pos = w.DiveFrom.Lerp(w.DiveTo, u);
        float ground = _t.MeshHeight(w.Pos.X, w.Pos.Y);
        w.C = new Vector3(w.Pos.X, ground + Mathf.Lerp(ComHeight, LieHeight, u) + 0.7f * MathF.Sin(MathF.PI * u), w.Pos.Y);
        float k = Math.Min(1f, u * 1.4f);
        w.Ori = new Basis(w.DiveUp.Slerp(w.DiveProne, k * k * (3f - 2f * k)));
        // Arms out in front, like a goalkeeper.
        w.ArmR.Rotation = new Vector3(2.9f, 0, 0.25f);
        w.ArmL.Rotation = new Vector3(2.9f, 0, -0.25f);
        w.Body.Position = Vector3.Zero;
        w.Body.Rotation = Vector3.Zero;
        PlaceBody(w);
        if (u < 1f) return;
        w.Ori = new Basis(w.DiveProne);
        w.Mode = Mode.Down;
        w.Timer = ProneTime + _hornRng.RandfRange(0f, 0.4f);
    }

    /// <summary>Rooted to the spot, hands up: "ОЙ!". Then back to normal, and cross.</summary>
    void Freeze(Walker w, float dt, Vector2 carPos)
    {
        w.Timer -= dt;
        Place(w, dt, carPos);
        float quiver = MathF.Sin(Time.GetTicksMsec() * 0.06f) * 0.08f;
        w.ArmR.Rotation = new Vector3(3.0f + quiver, 0, 0.35f);
        w.ArmL.Rotation = new Vector3(3.0f - quiver, 0, -0.35f);
        w.Body.Rotation = new Vector3(0.12f, 0, 0);
        if (w.Timer > 0) return;
        w.Shout = (Troops?MilitaryShouts:Shouts)[_hornRng.RandiRange(0, Shouts.Length - 1)];
        w.Angry = 2.5f;
        w.HornCd = 4f;
        w.Mode = Mode.Angry;
    }

    // ------------------------------------------------------------ the hit

    /// <summary>
    /// Car body circles against her. A hit launches her with the car's velocity at the contact
    /// point (a bit more, she is light) and gives the Pyatyorka only a small knock: ~70 kg against
    /// a tonne, exaggerated a little.
    /// </summary>
    bool TryHit(Walker w, CarSim car, Camera3D lens)
    {
        float ground = _t.MeshHeight(w.Pos.X, w.Pos.Y);
        if (car.Pos.Y - ground > 2.5f) return false;       // jumped clean over her
        for (int ci = 0; ci < CarCircleZ.Length; ci++)
        {
            Vector3 rel = car.Rot * new Vector3(0, 0, CarCircleZ[ci]);
            Vector2 c = new(car.Pos.X + rel.X, car.Pos.Z + rel.Z);
            Vector2 d = w.Pos - c;
            float dist = d.Length();
            if (dist >= CarRadius + Radius) continue;
            Vector2 n2 = dist > 1e-3f ? d / dist : new Vector2(car.Forward.X, car.Forward.Z).Normalized();
            Vector3 v = car.Vel + car.AngVel.Cross(rel);
            Vector2 vh = new(v.X, v.Z);
            float vn = vh.Dot(n2);
            if (vn < HitSpeed) continue;
            Launch(w, car, rel, n2, vh, vn, ci == 0, lens);
            return true;
        }
        return false;
    }

    void Launch(Walker w, CarSim car, Vector3 rel, Vector2 n2, Vector2 vh, float vn, bool front, Camera3D lens)
    {
        float speed = vh.Length();
        BurstCan(w);
        TapeFx?.Fx(new TapeEvent(0f, TapeEventType.Effect, Kind: TapeFxKind.BabushkaLaunch, Effect: TapeEffect.BabushkaLaunch, Pos: new Vector3(w.Pos.X, _t.MeshHeight(w.Pos.X, w.Pos.Y), w.Pos.Y), Param: Math.Clamp(vn / 28f, 0f, 1f), Life: 0.5f));
        HitsThisTick++;
        HitSpeedThisTick = Math.Max(HitSpeedThisTick, vn);
        if (_debug)
        {
            _dbgBefore = car.Speed;
            _dbgCar = car;
        }

        // The car: a few percent of speed, a nudge of yaw and a nod of the nose.
        Vector3 n = new(n2.X, 0, n2.Y);
        float loss = 0.07f + 0.02f * Math.Clamp(vn / 28f, 0f, 1f);
        car.Vel -= n * vn * loss;
        float kick = Math.Clamp(vn / 25f, 0.1f, 1f);
        float rxn = rel.Cross(n).Y;
        car.AngVel += Vector3.Up * (-rxn * 0.12f * kick + _rng.RandfRange(-0.15f, 0.15f) * kick);
        car.AngVel += car.Right * _rng.RandfRange(0.15f, 0.35f) * kick;          // nose bobs up over her
        car.AngVel += car.Forward * _rng.RandfRange(-0.2f, 0.2f) * kick;

        // Her: the car's velocity at the contact point and more (she is light), pushed off
        // sideways along the normal, lifted more the faster it was. A nudge just topples her.
        float boost = Mathf.Lerp(0.6f, 1.35f, Math.Clamp((vn - 4f) / 20f, 0f, 1f));
        Vector2 vt = vh - n2 * vn;
        Vector2 hv = n2 * (vn * boost + 0.5f) + vt * (0.35f + 0.5f * Math.Clamp(vn / Math.Max(speed, 0.1f), 0f, 1f));
        float up = 0.31f * vn + 0.0103f * vn * vn;
        float ground = _t.MeshHeight(w.Pos.X, w.Pos.Y);
        w.C = new Vector3(w.Pos.X, ground + ComHeight, w.Pos.Y);
        w.LaunchAt = w.Pos;
        w.V = new Vector3(hv.X, up, hv.Y);
        w.Ori = new Basis(Vector3.Up, w.Yaw);
        // Tumble head over heels about the horizontal axis across her flight, plus a twist.
        Vector3 across = Vector3.Up.Cross(new Vector3(hv.X, 0, hv.Y)).Normalized();
        if (!across.IsFinite() || across.LengthSquared() < 0.5f) across = Vector3.Right;
        w.Spin = across * (1.5f + 0.45f * vn) * (_rng.Randf() < 0.8f ? -1f : 1f)
               + Vector3.Up * _rng.RandfRange(-1f, 1f) * (0.5f + 0.15f * vn);
        w.Peak = 0;
        w.Launch = vn;
        w.Angry = 0;
        if (HornDebug) GD.Print($"horn: babushka {w.Index} HIT ({w.Mode}{(w.Dived ? ", after a dive" : "")}) at {vn * 3.6f:F0} km/h");
        w.Dived = false;
        w.React = -1;
        w.Mode = Mode.Fly;

        bool toLens = lens != null && (ForceLens is bool force ? force && vn > 8f : front && vn > LensSpeed && _rng.Randf() < LensChance);
        if (toLens)
        {
            // Aimed up at the camera: she grows on screen and smacks into the glass.
            w.Mode = Mode.Lens;
            w.Timer = 0;
            w.LensFrom = w.C;
            w.LensCtrl = w.C + new Vector3(hv.X, 0, hv.Y) * 0.15f + Vector3.Up * 14f;
            LensBoundThisTick = true;
        }
        if (_debug)
            GD.Print($"babushka hit at {Time.GetTicksMsec() / 1000f:F2} s: car {_dbgBefore * 3.6f:F1} -> {car.Speed * 3.6f:F1} km/h (closing {vn * 3.6f:F1} km/h, {(front ? "front" : "side/rear")}), launch {w.V.Length() * 3.6f:F0} km/h up {up:F1} m/s{(toLens ? ", INTO THE LENS" : "")}");
        _dbgAfter = 0.3f;
        Hit?.Invoke(w.Index);
    }

    void DebugAfterHit(float dt, CarSim car)
    {
        if (!_debug || _dbgAfter < 0 || car != _dbgCar) return;
        _dbgAfter -= dt;
        if (_dbgAfter < 0) GD.Print($"babushka hit: car {car.Speed * 3.6f:F1} km/h 0.3 s later");
    }

    // ------------------------------------------------------------ flight and landing

    Basis LyingBasis(Walker w, Vector3 along)
    {
        // Stretched out with her head the way she was going, on her back or face down,
        // whichever she is closer to already.
        Vector3 y = new Vector3(along.X, 0, along.Z);
        y = y.LengthSquared() > 1e-4f ? y.Normalized() : new Vector3(w.Ori.Y.X, 0, w.Ori.Y.Z).Normalized();
        if (!y.IsFinite() || y.LengthSquared() < 0.5f) y = Vector3.Forward;
        Vector3 z = w.Ori.Z.Y > 0 ? Vector3.Up : Vector3.Down;
        return new Basis(y.Cross(z), y, z);
    }

    void Fly(Walker w, float dt)
    {
        w.V.Y -= 9.81f * dt;
        w.V -= w.V * w.V.Length() * 0.006f * dt;             // air drag on a quilted coat
        w.C += w.V * dt;
        float max = _t.WorldSize - 4f;
        if (w.C.X < 4f || w.C.X > max) { w.C.X = Math.Clamp(w.C.X, 4f, max); w.V.X *= -0.3f; }
        if (w.C.Z < 4f || w.C.Z > max) { w.C.Z = Math.Clamp(w.C.Z, 4f, max); w.V.Z *= -0.3f; }
        w.Pos = new Vector2(w.C.X, w.C.Z);
        float ground = _t.MeshHeight(w.C.X, w.C.Z);
        w.Peak = Math.Max(w.Peak, w.C.Y - ComHeight - ground);

        if (w.C.Y < ground + LieHeight && w.V.Y < 0)
        {
            w.C.Y = ground + LieHeight;
            if (w.V.Y < -2.5f)
            {
                // Bounce: a thud, a little hop, less spin.
                w.V = new Vector3(w.V.X * 0.5f, -w.V.Y * 0.3f, w.V.Z * 0.5f);
                w.Spin *= 0.55f;
            }
            else
            {
                // Sliding along the grass on her coat.
                w.V.Y = 0;
                Vector3 h = new(w.V.X, 0, w.V.Z);
                float hs = h.Length();
                float ns = Math.Max(0, hs - 12f * dt);
                w.V = hs > 1e-4f ? h * (ns / hs) : Vector3.Zero;
                w.Spin *= MathF.Exp(-8f * dt);
                var want = LyingBasis(w, w.V.LengthSquared() > 0.01f ? w.V : w.Ori.Y);
                w.Ori = new Basis(w.Ori.GetRotationQuaternion().Slerp(want.GetRotationQuaternion(), Math.Min(1f, dt * 8f)));
                if (ns < 0.25f)
                {
                    w.Ori = want;
                    w.Mode = Mode.Down;
                    w.Timer = 1.2f + Math.Min(1.3f, w.Launch * 0.05f) + _rng.RandfRange(0f, 0.6f);
                    if (_debug) GD.Print($"babushka down at {Time.GetTicksMsec() / 1000f:F2} s: peak {w.Peak:F1} m, landed {w.Pos.DistanceTo(w.LaunchAt):F0} m away");
                }
            }
        }
        Tumble(w, dt);
        PoseFlailing(w, 1f);
        PlaceBody(w);
    }

    void Tumble(Walker w, float dt)
    {
        float a = w.Spin.Length();
        if (a > 1e-3f) w.Ori = new Basis(w.Spin / a, a * dt) * w.Ori;
        w.Ori = w.Ori.Orthonormalized();
    }

    void FlyToLens(Walker w, float dt, Camera3D lens)
    {
        if (lens == null) { w.V = (w.C - w.LensFrom) * 0.5f; w.Mode = Mode.Fly; Fly(w, dt); return; }
        w.Timer += dt;
        float u = Math.Min(1f, w.Timer / LensFlight);
        // Quadratic Bezier to just in front of the (moving) lens.
        Vector3 fwd = -lens.GlobalTransform.Basis.Z;
        Vector3 end = lens.GlobalPosition + fwd * 1.0f;
        Vector3 a = w.LensFrom.Lerp(w.LensCtrl, u), b = w.LensCtrl.Lerp(end, u);
        Vector3 p = a.Lerp(b, u);
        w.V = (p - w.C) / Math.Max(dt, 1e-4f);
        w.C = p;
        w.Peak = Math.Max(w.Peak, w.C.Y - ComHeight - _t.MeshHeight(w.C.X, w.C.Z));
        // Tumbling on the way up, straightening out face first for the last stretch.
        Tumble(w, dt);
        if (u > 0.6f)
        {
            Vector3 zAxis = -fwd;                              // face (-Z) towards the glass
            Vector3 yAxis = lens.GlobalTransform.Basis.Y;
            var faceCam = new Basis(yAxis.Cross(zAxis).Normalized(), yAxis, zAxis);
            float k = (u - 0.6f) / 0.4f;
            w.Ori = new Basis(w.Ori.GetRotationQuaternion().Slerp(faceCam.GetRotationQuaternion(), k * k));
        }
        PoseFlailing(w, 1f);
        PlaceBody(w);
        if (u >= 1f)
        {
            LensHitThisTick = true;
            LensScarf = w.Scarf;
            LensSeedThisTick = ++_lensSerial;
            // The same actor is a supply trooper on bonus. Colour, crack seed,
            // appearance and lifetime belong to the original hit, not the replay camera.
            TapeFx?.Fx(new TapeEvent(0f, TapeEventType.Effect, Kind: TapeFxKind.BabushkaLens,
                Dir: new Vector3(w.Scarf.R, w.Scarf.G, w.Scarf.B), Param: _t.Id == "bonus" ? 1 : 0,
                Instance: LensSeedThisTick, Life: LensHold));
            w.Root.Visible = false;
            w.Mode = Mode.OnGlass;
            w.Timer = LensHold;
            if (_debug) GD.Print($"babushka on the lens at {Time.GetTicksMsec() / 1000f:F2} s: peak {w.Peak:F1} m");
        }
    }

    void OnGlass(Walker w, float dt, Camera3D lens)
    {
        w.Timer -= dt;
        if (lens != null)
        {
            // Stay with the camera while the HUD shows her on the glass.
            Vector3 fwd = -lens.GlobalTransform.Basis.Z;
            w.C = lens.GlobalPosition + fwd * 1.0f;
        }
        if (w.Timer > 0) return;
        // Off the glass: she drops, below and behind the camera's view, back to the ground.
        w.C += Vector3.Down * 2f;
        w.V = new Vector3(_rng.RandfRange(-1f, 1f), -1f, _rng.RandfRange(-1f, 1f));
        w.Spin = new Vector3(_rng.RandfRange(-4f, 4f), _rng.RandfRange(-2f, 2f), _rng.RandfRange(-4f, 4f));
        w.Root.Visible = true;
        w.Mode = Mode.Fly;
    }

    void Down(Walker w, float dt, Vector2 carPos)
    {
        w.Timer -= dt;
        // Arms and legs out, a feeble wave of the fist now and then.
        w.ArmR.Rotation = new Vector3(2.4f + MathF.Sin(Time.GetTicksMsec() * 0.012f) * 0.3f, 0, 0.3f);
        w.ArmL.Rotation = new Vector3(2.2f, 0, -0.5f);
        w.Body.Position = Vector3.Zero;
        w.Body.Rotation = Vector3.Zero;
        PlaceBody(w);
        if (w.Timer > 0) return;
        w.Mode = Mode.Rise;
        w.Timer = 0;
        w.RiseFrom = w.Ori.GetRotationQuaternion();
        w.RiseFromC = w.C;
        w.Pos = new Vector2(w.C.X, w.C.Z);
        Vector2 face = carPos - w.Pos;
        w.Yaw = TrackData.YawFor(face.LengthSquared() > 0.1f ? face.Normalized() : Vector2.Up);
    }

    void Rise(Walker w, float dt, Vector2 carPos)
    {
        const float time = 0.8f;
        w.Timer += dt;
        float u = Math.Min(1f, w.Timer / time);
        float s = u * u * (3f - 2f * u);
        float ground = _t.MeshHeight(w.Pos.X, w.Pos.Y);
        var upright = new Basis(Vector3.Up, w.Yaw).GetRotationQuaternion();
        w.Ori = new Basis(w.RiseFrom.Slerp(upright, s));
        w.C = w.RiseFromC.Lerp(new Vector3(w.Pos.X, ground + ComHeight, w.Pos.Y), s);
        w.ArmR.Rotation = new Vector3(Mathf.Lerp(2.4f, 0.5f, s), 0, 0.3f);
        w.ArmL.Rotation = new Vector3(Mathf.Lerp(2.2f, 0.4f, s), 0, -0.5f);
        PlaceBody(w);
        if (u < 1f) return;
        // Up again, and furious.
        w.Shout = (Troops?MilitaryShouts:Shouts)[_rng.RandiRange(0, Shouts.Length - 1)];
        if (w.Dived)
        {
            // Out of the ditch unhurt: she will remember that car.
            w.Shout = (Troops?MilitaryDiveShouts:DiveShouts)[_hornRng.RandiRange(0, (Troops?MilitaryDiveShouts:DiveShouts).Length - 1)];
            w.Dived = false;
            w.HornCd = 4f;
            DodgedThisTick++;
            if (HornDebug) GD.Print($"horn: babushka {w.Index} DODGED, up again");
        }
        w.Angry = 3.5f;
        w.Mode = Mode.Angry;
        Place(w, 0, carPos);
    }

    void PoseFlailing(Walker w, float amount)
    {
        float t = Time.GetTicksMsec() * 0.03f;
        w.ArmR.Rotation = new Vector3(2.8f + MathF.Sin(t) * 0.6f * amount, 0, 0.5f);
        w.ArmL.Rotation = new Vector3(2.8f + MathF.Sin(t + 2f) * 0.6f * amount, 0, -0.5f);
        w.Body.Position = Vector3.Zero;
        w.Body.Rotation = Vector3.Zero;
    }

    /// <summary>Model root from the flight state: orientation about her centre of mass.</summary>
    void PlaceBody(Walker w)
    {
        Vector3 origin = w.C - w.Ori * new Vector3(0, ComHeight, 0);
        w.Root.GlobalTransform = new Transform3D(w.Ori.Scaled(Vector3.One * SizeScale), origin);
    }
}
