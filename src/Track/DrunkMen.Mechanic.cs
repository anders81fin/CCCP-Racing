using System;
using CCCPRacing.Car;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Track;

/// <summary>
/// The man asleep on the first bench is the kolkhoz's only mechanic. A honk within earshot wakes
/// him. If the car then stops near the shelter he staggers over, looks at the engine, beats it
/// with a sledgehammer for a time proportional to the damage and the Pyatyorka is as good as new: the engine's health climbs evenly while he works, each blow
/// knocks the dents out a little, and the player can break it off by driving away (the throttle).
/// The race clock keeps running the whole time. Afterwards he goes back to the bench.
/// RaceScene feeds him the car and the horn each tick, holds the car while <see cref="Repairing"/>
/// and reacts to the events.
/// </summary>
public partial class DrunkMen
{
    bool AircraftMechanic => _t.Id == "bonus";

    enum Mech { Asleep, Waking, Awake, Walking, Repairing, Returning, LyingDown, ToJob, Servicing }

    /// <summary>How far the horn carries to the bench (m).</summary>
    public const float HearRange = 60f;
    /// <summary>A stopped car this close to the mechanic's post counts as parked at the stop (m).</summary>
    public const float ParkRange = 16f;
    public const float RepairTime = 10f;
    const float MinRepairWork = 1.6f;
    /// <summary>Work time includes one hammer swing; the remaining work scales with missing engine health.</summary>
    public static float RepairWorkFor(float health) => MinRepairWork + (DoneAt - MinRepairWork) * (1f - Math.Clamp(health, 0f, 1f));
    const float WalkSpeed = 3f, WakeTime = 2.6f, LieTime = 1.6f, AwakeTime = 25f, ParkedNeeded = 0.8f;
    const float StopSpeed = 1.5f;
    /// <summary>Body-frame spot beside the bonnet where he stands to swing (m).</summary>
    const float WorkX = 1.75f, WorkZ = -1.45f;
    const float HammerRest = 0.35f, HammerUp = 2.9f, HammerDown = 0.87f;
    /// <summary>Repair-clock times of the hammer blows; the last one straightens the panels out.</summary>
    static readonly float[] Whacks = { 1.6f, 2.8f, 4.0f, 5.2f, 6.4f, 7.6f };
    const float DoneAt = 8.0f, SipFrom = 8.4f, SipTo = 9.6f, SipLift = 0.5f;
    static readonly string[] WhackShouts = { "БАМ!", "ЭХ!", "ДАВАЙ!", "БАМ!", "ОП!", "БАМ!" };

    /// <summary>Bonus track: a Hind stands on the mechanic's pad. Without one he sleeps on a camp cot at the pad's edge.</summary>
    public bool AircraftPresent;
    Node3D _cot;

    Mech _mechPhase;
    float _mechT, _awakeLeft, _parkedT, _walkPhase;
    float _repairWorkTime = DoneAt;
    float RepairClock => _mechT + DoneAt - _repairWorkTime;
    int _nextWhack, _firstWhack;
    bool _toldFine;
    Vector2 _stand, _fromPos;
    float _fromYaw;
    Vector3 _carPark;
    Vector2 _work;                 // where he stands beside the bonnet
    Vector2 _workFace;             // direction he faces there
    Vector3 _hitPoint;
    Node3D _hammer;

    /// <summary>He is at work on the car, or on his way to it: the car must be held still.</summary>
    public bool HoldsCar => _mechPhase is Mech.Walking or Mech.Repairing;

    /// <summary>The repair itself is under way.</summary>
    public bool Repairing => _mechPhase == Mech.Repairing;

    /// <summary>How far the repair has got (0..1): the engine's health climbs evenly with it.</summary>
    public float RepairProgress => _mechPhase == Mech.Repairing ? Math.Clamp(_mechT / _repairWorkTime, 0f, 1f) : 0f;

    /// <summary>The player drives off: he stops where he is and goes back to the bench (the engine keeps what it got).</summary>
    public void Interrupt()
    {
        if (!HoldsCar) return;
        bool working = _mechPhase == Mech.Repairing;
        GoHome();
        Say(working ? "ЭЙ!" : "ЛАДНО...", 1.2f);
        if (working) RepairInterrupted?.Invoke();
    }

    /// <summary>He is up (or walking about) rather than asleep.</summary>
    public bool MechanicUp => _mechPhase is not (Mech.Asleep or Mech.Servicing);

    /// <summary>Where he is (on the bench asleep, or about), for the driving school's arrow.</summary>
    public Vector2 MechanicPosition => _mech?.Pos ?? Vector2.Zero;

    /// <summary>The fixed service location, even while the mechanic walks over to a car.</summary>
    public Vector2? MechanicPostPosition => _mech == null ? null : new Vector2(_shelter.Origin.X, _shelter.Origin.Z);

    public event Action Woke, RepairStarted, Repaired, NothingToFix, RepairInterrupted;
    /// <summary>A hammer blow lands: where, and whether it is the big last one.</summary>
    public event Action<Vector3, bool> Whack;
    public ITapeFxHost TapeFx;
    public int LastWhackSeed { get; private set; }

    /// <summary>A new run: back on the bench, asleep.</summary>
    public void ResetMechanic()
    {
        foreach (var m in _men) m.Down.Reset();      // a new run: the knocked-over men stand again
        if (_mech == null) return;
        _mechPhase = Mech.Asleep;
        _mechT = _parkedT = 0;
        _repairWorkTime = DoneAt;
        _nextWhack = _firstWhack = 0;
        _done = false;
        _toldFine = false;
        _alert = _toJob = false;
        _mech.Lying = true;
        _mech.Pos = LyingPos();
        _mech.Yaw = _shelterYaw;     // facing out of the shelter: on his side, he lies along the bench
        _mech.Cheer = 0;
        LyingPose(_mech);
        if (_hammer != null) _hammer.Visible = false;
        _mech.Bottle.Visible = true;
    }

    Vector2 LyingPos()
    {
        // Bonus: the cot stands 7.1 m from the service spot, towards the pad's outer edge, clear of the rotor disc.
        Vector3 p = _shelter * new Vector3(0, 0, AircraftMechanic ? 7.45f : 0.35f);
        return new Vector2(p.X, p.Z);
    }

    static void LyingPose(Man m)
    {
        m.Body.Rotation = new Vector3(0, 0, MathF.PI / 2f);
        m.Body.Position = new Vector3(0.75f, 0.62f, 0);
        m.LegL.Rotation = m.LegR.Rotation = Vector3.Zero;
    }

    void EnsureHammer()
    {
        if (_hammer != null) return;
        _hammer = new Node3D { Name = "MechanicSledgehammer", Visible = false };
        _hammer.AddChild(Props.Part(Props.Cyl(0.03f, 0.6f, 6), new Color(0.45f, 0.32f, 0.2f), new Vector3(0, -0.8f, -0.05f)));
        // The head's long axis lies in the swing plane (the arm turns about X), so its striking faces lead the blow.
        _hammer.AddChild(Props.Part(Props.Box(0.15f, 0.15f, 0.3f), new Color(0.25f, 0.25f, 0.28f), new Vector3(0, -1.12f, -0.05f)));
        _mech.ArmR.AddChild(_hammer);
    }

    /// <summary>The taped arm carries the tool; the taped bottle's visibility
    /// already records the exact mutually-exclusive bottle/hammer choice.
    /// Restoring this fixed attachment also works with existing recordings.
    /// No live repair timer, car interaction or random animation runs here.</summary>
    public void RestoreMechanicTapeTool()
    {
        if (_mech == null) return;
        EnsureHammer();
        _hammer.Visible = !_mech.Bottle.Visible;
    }

    public void ClearMechanicTapeTool()
    {
        if (_hammer != null) _hammer.Visible = false;
    }

    /// <summary>Shared live/replay impact visuals, using the recorded effect RNG seed.</summary>
    public static void WhackParticles(TyreEffects fx, RandomNumberGenerator rng, Vector3 at, bool big)
    {
        for (int i = 0; i < (big ? 14 : 7); i++)
            fx.Burst(at, new Vector3(rng.RandfRange(-2.5f, 2.5f), rng.RandfRange(1.5f, 4f), rng.RandfRange(-2.5f, 2.5f)),
                new Color(1f, .8f, .3f), 1f, .09f, .03f, .35f, 9f, .4f, false);
        for (int i = 0; i < 3; i++)
            fx.Smoke(at + Vector3.Up * .1f, new Vector3(rng.RandfRange(-.6f, .6f), rng.RandfRange(1.5f, 2.5f), rng.RandfRange(-.6f, .6f)),
                new Color(.35f, .35f, .33f), .7f, .5f, 1.8f, 1.1f);
    }

    Vector2 StandPos()
    {
        if (AircraftMechanic)
        {
            Vector3 w = _shelter * new Vector3(0, 0, 0.35f);
            return new Vector2(w.X, w.Z);
        }
        Vector3 p = _shelter * new Vector3(-1.0f, 0, -1.3f);
        return new Vector2(p.X, p.Z);
    }

    void Say(string text, float seconds = 1.6f)
    {
        _mech.Shout = text;
        _mech.Cheer = seconds;
    }

    void SetMech(Mech phase)
    {
        _mechPhase = phase;
        _mechT = 0;
        _fromPos = _mech.Pos;
        _fromYaw = _mech.Yaw;
        if (OS.HasEnvironment("DRUNKDBG")) GD.Print($"mechanic: {phase} at {_clock:F1} s");
    }

    /// <summary>Per tick, before <see cref="Tick"/>. racing: a real run is under way.</summary>
    public void MechanicTick(float dt, CarSim car, bool racing, bool honking, bool damaged, float health)
    {
        if (_mech == null) return;
        EnsureHammer();
        _mechT += dt;
        var carPos2 = new Vector2(car.Pos.X, car.Pos.Z);

        if (!racing && _mechPhase is Mech.Walking or Mech.Repairing or Mech.Awake or Mech.Waking)
            GoHome();
        if (AircraftMechanic) AircraftShift(dt);

        switch (_mechPhase)
        {
            case Mech.Asleep:
                if (racing && honking && _mech.Pos.DistanceTo(carPos2) < HearRange)
                {
                    _mech.Lying = false;
                    SetMech(Mech.Waking);
                    _alert = true;
                    Say("КТО ТАМ?!", 2f);
                    Woke?.Invoke();
                }
                break;

            case Mech.Servicing:
                if (racing && honking && _mech.Pos.DistanceTo(carPos2) < HearRange)
                {
                    _alert = true;
                    SetMech(Mech.Awake);
                    _stand = StandPos(); _awakeLeft = AwakeTime; _parkedT = 0; _toldFine = false;
                    Say("КТО ТАМ?!", 2f);
                    Woke?.Invoke();
                }
                break;

            case Mech.Waking:
                if (AircraftMechanic)
                {
                    // Sits up on the cot, then walks over to the helicopter.
                    if (_mechT >= WakeTime) { _toJob = false; SetMech(Mech.ToJob); }
                }
                else if (_mechT >= WakeTime)
                {
                    _stand = StandPos();
                    _mech.Pos = _stand;
                    SetMech(Mech.Awake);
                    _awakeLeft = AwakeTime;
                    _parkedT = 0;
                    _toldFine = false;
                }
                else
                {
                    // Sits up, and the first steps carry him from the bench to the front of the shelter.
                    float k = Mathf.SmoothStep(0.45f, 1f, _mechT / WakeTime);
                    if (!AircraftMechanic) _mech.Pos = _fromPos.Lerp(StandPos(), k);
                    _mech.Yaw = Mathf.LerpAngle(_fromYaw, _shelterYaw, Mathf.SmoothStep(0.1f, 0.9f, _mechT / WakeTime));
                }
                break;

            case Mech.Awake:
                _awakeLeft -= dt;
                bool parked = car.Speed < StopSpeed && _stand.DistanceTo(carPos2) < ParkRange;
                _parkedT = parked ? _parkedT + dt : 0;
                if (!parked) _toldFine = false;
                if (_parkedT >= ParkedNeeded)
                {
                    if (damaged)
                    {
                        _carPark = car.Pos;
                        SetMech(Mech.Walking);
                        Say("ИДУ!", 1f);
                    }
                    else if (!_toldFine)
                    {
                        _toldFine = true;
                        Say("ВСЕ В ПОРЯДКЕ", 2.5f);
                        NothingToFix?.Invoke();
                    }
                }
                else if (_awakeLeft <= 0) GoHome();
                break;

            case Mech.Walking:
                PlanWorkSpot(car);
                if (car.Pos.DistanceTo(_carPark) > 4f || !damaged) { SetMech(Mech.Awake); _awakeLeft = Math.Max(_awakeLeft, 10f); _parkedT = 0; break; }
                Vector2 to = _work - _mech.Pos;
                float dist = to.Length();
                if (dist < 0.12f)
                {
                    _mech.Pos = _work;
                    _mech.Yaw = TrackData.YawFor(_workFace);
                    SetMech(Mech.Repairing);
                    _repairWorkTime = RepairWorkFor(health);
                    _nextWhack = 0;
                    // Short repairs use the final blows at their normal pace, rather than
                    // speeding the whole animation up. Leave room to raise the hammer.
                    while (_nextWhack < Whacks.Length - 1 && Whacks[_nextWhack] < RepairClock + 0.7f) _nextWhack++;
                    _firstWhack = _nextWhack;
                    Say("ТАК...", 1f);
                    RepairStarted?.Invoke();
                }
                else Walk(Math.Min(dt, dist / WalkSpeed), to / dist);
                break;

            case Mech.Repairing:
                PlanWorkSpot(car);
                if (car.Pos.DistanceTo(_carPark) > 2.5f)
                {
                    if (OS.HasEnvironment("DRUNKDBG")) GD.Print($"mechanic: car moved {car.Pos.DistanceTo(_carPark):F1} m, giving up");
                    GoHome();
                    break;
                }   // R and away, or a tractor
                _mech.Pos = _work;
                _mech.Yaw = TrackData.YawFor(_workFace);
                while (_nextWhack < Whacks.Length && RepairClock >= Whacks[_nextWhack])
                {
                    bool big = _nextWhack == Whacks.Length - 1;
                    _hitPoint = car.Pos + car.Rot * new Vector3(0.35f * Math.Sign(_workSide), 0.9f, WorkZ);
                    Say(WhackShouts[_nextWhack], 0.7f);
                    LastWhackSeed = unchecked((int)new RandomNumberGenerator().Randi());
                    TapeFx?.Fx(new TapeEvent(0, TapeEventType.Effect, Kind: TapeFxKind.MechanicWhack,
                        Pos: _hitPoint, Param: big ? 1 : 0, Instance: LastWhackSeed, Life: 1.8f));
                    Whack?.Invoke(_hitPoint, big);
                    _nextWhack++;
                }
                if (_nextWhack == Whacks.Length && RepairClock >= DoneAt && !_done)
                {
                    _done = true;
                    Say("ГОТОВО!", 2f);
                    Repaired?.Invoke();
                }
                if (RepairClock >= RepairTime) GoHome();
                break;

            case Mech.Returning:
                Vector2 target = _returnStage == 0 ? StandPos() : LyingPos();
                Vector2 d = target - _mech.Pos;
                float dl = d.Length();
                if (dl < 0.12f)
                {
                    _mech.Pos = target;
                    if (_returnStage == 0) _returnStage = 1;
                    else SetMech(Mech.LyingDown);
                }
                else Walk(Math.Min(dt, dl / WalkSpeed), d / dl);
                break;

            case Mech.LyingDown:
                if (_mechT >= LieTime)
                {
                    ResetMechanic();
                    break;
                }
                _mech.Yaw = Mathf.LerpAngle(_fromYaw, _shelterYaw, Mathf.SmoothStep(0f, 0.6f, _mechT / LieTime));
                break;
        }
    }

    /// <summary>Canvas camp bed, a rolled blanket and a tin mug at the outer edge of the pad where the mechanic sleeps.</summary>
    void BuildCot()
    {
        Vector2 at = LyingPos();
        var canvas = new Color(.33f, .36f, .22f);
        var frame = new Color(.2f, .21f, .18f);
        _cot = new Node3D { Name = "MechanicCot" };
        _cot.Position = new Vector3(at.X, _t.MeshHeight(at.X, at.Y), at.Y);
        _cot.Rotation = new Vector3(0, _shelterYaw, 0);
        AddChild(_cot);
        // The lying body spans x -1.5 (cap) .. +1.3 (boots) of the scaled root, about 0.55 m above the ground.
        const float cx = -.1f, length = 3.0f, top = .5f;
        _cot.AddChild(Props.Part(Props.Box(length, .06f, .95f), canvas, new Vector3(cx, top - .03f, 0)));
        foreach (float sx in new[] { -1f, 1f })
        {
            foreach (float sz in new[] { -1f, 1f })
                _cot.AddChild(Props.Part(Props.Box(.06f, top - .06f, .06f), frame, new Vector3(cx + sx * (length / 2 - .1f), (top - .06f) / 2, sz * .43f)));
            _cot.AddChild(Props.Part(Props.Box(.07f, .05f, 1.0f), frame, new Vector3(cx + sx * (length / 2 - .03f), top - .05f, 0)));
        }
        foreach (float sz in new[] { -1f, 1f })
            _cot.AddChild(Props.Part(Props.Box(length, .05f, .05f), frame, new Vector3(cx, top - .08f, sz * .48f)));
        _cot.AddChild(Props.Part(Props.Box(.5f, .12f, .65f), new Color(.5f, .48f, .38f), new Vector3(cx - length / 2 + .45f, top + .06f, 0)));
        _cot.AddChild(Props.Part(Props.Box(1.1f, .1f, .95f), new Color(.38f, .3f, .2f), new Vector3(cx + .55f, top + .05f, 0)));
    }

    /// <summary>Hind on the pad: up from the cot, over to the helicopter and tinkering; Hind gone: back to the cot.</summary>
    void AircraftShift(float dt)
    {
        switch (_mechPhase)
        {
            case Mech.Asleep when AircraftPresent:
                _mech.Lying = false;
                _mech.Cheer = 0;
                SetMech(Mech.Waking);
                _toJob = true;
                break;
            case Mech.ToJob:
            {
                Vector2 to = StandPos() - _mech.Pos;
                float dist = to.Length();
                if (dist < 0.12f)
                {
                    _mech.Pos = _stand = StandPos();
                    _mech.Yaw = _shelterYaw;
                    if (_alert)
                    {
                        SetMech(Mech.Awake);
                        _awakeLeft = AwakeTime; _parkedT = 0; _toldFine = false;
                    }
                    else SetMech(Mech.Servicing);
                }
                else Walk(Math.Min(dt, dist / WalkSpeed), to / dist);
                break;
            }
            case Mech.Servicing when !AircraftPresent:
                GoHome();
                break;
        }
    }
    bool _toJob, _alert;

    float _workSide;
    bool _done;
    int _returnStage;

    void GoHome()
    {
        _done = false;
        _returnStage = _mech.Pos.DistanceTo(StandPos()) < 0.5f ? 1 : 0;
        _mech.Bottle.Visible = true;
        _hammer.Visible = false;
        SetMech(Mech.Returning);
    }

    void Walk(float dt, Vector2 dir)
    {
        _mech.Pos += dir * WalkSpeed * dt;
        _mech.Yaw = Mathf.LerpAngle(_mech.Yaw, TrackData.YawFor(dir), 1f - MathF.Exp(-8f * dt));
        _walkPhase += dt * 9f;
    }

    /// <summary>Beside the bonnet, on the side he comes from, facing the car.</summary>
    void PlanWorkSpot(CarSim car)
    {
        Vector3 local = car.Rot.Transposed() * (new Vector3(_mech.Pos.X, car.Pos.Y, _mech.Pos.Y) - car.Pos);
        if (_mechPhase != Mech.Repairing) _workSide = local.X >= 0 ? 1f : -1f;
        Vector3 spot = car.Pos + car.Rot * new Vector3(_workSide * WorkX, 0, WorkZ);
        Vector3 face = car.Rot * new Vector3(-_workSide, 0, 0);
        _work = new Vector2(spot.X, spot.Z);
        _workFace = new Vector2(face.X, face.Z).Normalized();
    }

    // ------------------------------------------------------------ pose

    void PlaceMechanic(Man m)
    {
        float y = _t.MeshHeight(m.Pos.X, m.Pos.Y);
        m.Root.GlobalTransform = new Transform3D(new Basis(Vector3.Up, m.Yaw).Scaled(Vector3.One * SizeScale), new Vector3(m.Pos.X, y, m.Pos.Y));
        m.LegL.Rotation = m.LegR.Rotation = Vector3.Zero;
        m.Body.Position = Vector3.Zero;
        float s1 = MathF.Sin(_clock * 1.1f), s2 = MathF.Sin(_clock * 0.8f + 1.7f);
        bool carrying = _mechPhase is Mech.Awake or Mech.Walking or Mech.Repairing or Mech.Servicing || (AircraftMechanic && _mechPhase == Mech.ToJob);
        _hammer.Visible = carrying && !(_mechPhase == Mech.Repairing && RepairClock >= SipFrom + SipLift * 0.5f);      // (the tools change hands in the middle of the arm's swing, not while it hangs still)
        m.Bottle.Visible = !_hammer.Visible;

        switch (_mechPhase)
        {
            case Mech.Servicing:
                m.Body.Rotation = new Vector3(-.18f + MathF.Sin(_clock * 3) * .06f, 0, 0);
                m.Head.Rotation = new Vector3(.25f, 0, 0);
                m.ArmR.Rotation = new Vector3(1.25f + MathF.Sin(_clock * 5) * .5f, 0, -.2f);
                break;
            case Mech.Waking:
            {
                float p = _mechT / WakeTime;
                float up = Mathf.SmoothStep(0.1f, 0.8f, p);
                m.Body.Rotation = new Vector3(0, 0, MathF.PI / 2f * (1f - up));
                m.Body.Position = new Vector3(0.75f * (1f - up), 0.62f * (1f - up), 0);
                m.Head.Rotation = new Vector3(0.1f, MathF.Sin(_clock * 6f) * 0.3f * (1f - p), 0);
                // Rubbing his eyes, then a stretch.
                m.ArmR.Rotation = new Vector3(2.6f + MathF.Sin(_clock * 9f) * 0.15f, 0, -0.3f);
                break;
            }
            case Mech.Awake:
                m.Body.Rotation = new Vector3(s2 * 0.1f, 0, s1 * 0.14f);
                m.Head.Rotation = new Vector3(0.2f, s2 * 0.5f, s1 * 0.15f);
                m.ArmR.Rotation = new Vector3(0.3f + s1 * 0.2f, 0, 0);
                break;
            case Mech.Walking:
            case Mech.ToJob:
            case Mech.Returning:
            {
                float sw = MathF.Sin(_walkPhase);
                m.LegL.Rotation = new Vector3(sw * 0.6f, 0, 0);
                m.LegR.Rotation = new Vector3(-sw * 0.6f, 0, 0);
                m.Body.Position = new Vector3(0, MathF.Abs(sw) * 0.04f, 0);
                m.Body.Rotation = new Vector3(-0.08f, 0, s1 * 0.12f + sw * 0.05f);
                m.Head.Rotation = new Vector3(0.1f, s2 * 0.3f, 0);
                m.ArmR.Rotation = new Vector3(_mechPhase == Mech.Walking ? HammerRest + sw * 0.1f : 0.3f - sw * 0.4f, 0, 0);
                break;
            }
            case Mech.Repairing:
                PoseRepair(m);
                break;
            case Mech.LyingDown:
            {
                float lie = Mathf.SmoothStep(0.3f, 1f, _mechT / LieTime);
                m.Body.Rotation = new Vector3(0, 0, MathF.PI / 2f * lie);
                m.Body.Position = new Vector3(0.75f * lie, 0.62f * lie, 0);
                m.Head.Rotation = new Vector3(0.2f, 0, 0);
                m.ArmR.Rotation = new Vector3(0.3f, 0, 0);
                break;
            }
        }
    }

    void PoseRepair(Man m)
    {
        float t = RepairClock;
        float ang = HammerRest;
        for (int i = _firstWhack; i < Whacks.Length; i++)
        {
            float w = Whacks[i];
            float u = t - w;
            if (u >= -0.7f && u < -0.1f)
            {
                float e = (u + 0.7f) / 0.6f;
                ang = Mathf.Lerp(HammerRest, HammerUp, e * e * (3f - 2f * e));
            }
            else if (u >= -0.1f && u < 0f) ang = Mathf.Lerp(HammerUp, HammerDown, (u + 0.1f) / 0.1f);
            else if (u >= 0f && u < 0.4f) ang = Mathf.Lerp(HammerDown, HammerRest, u / 0.4f);
        }
        float lean = Mathf.Lerp(-0.25f, 0.2f, Math.Clamp((ang - HammerDown) / (HammerUp - HammerDown), 0f, 1f));
        m.Body.Rotation = new Vector3(lean, 0, 0);
        m.Head.Rotation = new Vector3(0.35f, 0, 0);
        m.ArmR.Rotation = new Vector3(ang, 0, 0);
        if (t < Whacks[_firstWhack] - 0.7f)
        {
            // Sizing up the engine: a shake of the head, the hammer hanging at his side (no scratching with it in his hand).
            m.ArmR.Rotation = new Vector3(HammerRest, 0, 0);
            m.Head.Rotation = new Vector3(0.1f, MathF.Sin(t * 5f) * 0.4f, 0);
            m.Body.Rotation = new Vector3(0, 0, 0);
        }
        else if (t >= SipFrom && t < SipTo)
        {
            // The arm swings up to the mouth (the hammer is swapped for the bottle half way), then the sip.
            float k = Mathf.SmoothStep(0f, 1f, Math.Clamp((t - SipFrom) / SipLift, 0f, 1f));
            m.ArmR.Rotation = new Vector3(Mathf.Lerp(HammerRest, 2.2f, k), 0, Mathf.Lerp(0f, -0.5f, k));
            m.Head.Rotation = new Vector3(Mathf.Lerp(0.35f, -0.6f, k), 0, 0);
            m.Body.Rotation = Vector3.Zero;
        }
        else if (t >= SipTo)
        {
            m.ArmR.Rotation = new Vector3(2.8f + MathF.Sin(_clock * 14f) * 0.25f, 0, 0.3f);   // bottle raised: done
            m.Head.Rotation = new Vector3(-0.3f, 0, 0);
            m.Body.Rotation = Vector3.Zero;
        }
    }
}
