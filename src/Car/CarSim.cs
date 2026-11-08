using System;
using System.Collections.Generic;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Car;

public class Wheel
{
    public Vector3 Local;          // suspension top, body frame
    public bool Front, Left;

    public bool Contact;
    public float Compression, PrevCompression;
    public float Load;
    public float SlipLat;          // lateral sliding speed m/s
    public float SlipLong;         // driven tyre overspeed relative to the road
    public bool Spinning;          // longitudinal grip exceeded
    public SurfaceType Surface;
    public float SpinSpeed, SpinAngle;
    public float VisualLength;     // spring length for rendering
    public bool HandbrakeHeld;     // the handbrake holds this wheel against the engine (latched: no chatter at the balance point)
    public bool Locked;            // not turning while the car moves: handbrake or brakes past grip
    public Vector3 ContactPoint;    // world, on the ground
    public Vector3 ContactVel;     // world velocity of the contact patch
    public float HitCooldown;      // one pothole = one bang
    public float Slick;            // slurry or oil on the tread, 0..1: picked up at once from the road, wiped off over SlickClean seconds of driving (more in water)
    public float ForceLong, VelLong, LongLimit;   // last longitudinal tyre force, patch speed along the wheel, and the grip it was held to (diagnostics)
}

/// <summary>
/// Rigid body car on raycast-style wheels. Every wheel runs its own spring/damper against the
/// height field; tyre grip = load × surface friction, so bumps, crests and body roll directly
/// change how much the car grips. No engine physics objects are used, so this also runs in tests.
/// </summary>
public partial class CarSim
{
    public readonly CarParams P;
    public IGround Ground;
    public IReadOnlyList<Obstacle> Obstacles = Array.Empty<Obstacle>();
    /// <summary>A winter road's snow banks (null: none). SnowbankHit: the hardest push into one since the reader zeroed it (m/s), at SnowbankPoint.</summary>
    public Snowbanks Banks;
    public float SnowbankHit;
    public Vector3 SnowbankPoint;
    /// <summary>How hard the car ploughs through a bank's snow (speed m/s x depth m), the most since the reader zeroed it.</summary>
    public float SnowbankPlough;
    int _bankHint = -1;
    /// <summary>
    /// Asked when a hit is hard enough to break the obstacle (index, impact m/s, car velocity before
    /// the hit, car position): true if it gave way (and is gone from Obstacles). Null: nothing ever
    /// breaks (tests).
    /// </summary>
    public Func<int, float, Vector3, Vector3, bool> BreakObstacle;
    public float WorldMin = 4f, WorldMax = 316f;

    public Vector3 Pos, Vel, AngVel;
    public Basis Rot = Basis.Identity;
    public readonly Wheel[] Wheels = new Wheel[4];

    // Driver inputs (set via SetControls).
    public float ThrottleTarget, Brake, SteerIn, Handbrake;
    public float Throttle;             // ramps toward ThrottleTarget so keyboard taps meter power
    public float DriveAcceleration { get; private set; } // actual horizontal acceleration under power, m/s²

    public int Gear = 1;               // -1 = reverse
    public bool Spun;                  // sliding tail first in a forward gear: swapping ends
    public float Rpm;
    public float SteerAngle;           // + = right
    /// <summary>Clutch in / neutral: the engine revs freely and drives nothing (start line).</summary>
    bool _neutral, _autoNeutral;
    /// <summary>Explicit neutral, or the automatic box resting in N at a standstill.</summary>
    public bool Neutral
    {
        get => _neutral || _autoNeutral;
        set { _neutral = value; _autoNeutral = false; }
    }
    /// <summary>Driver's clutch pedal (Shift) held to the floor: like Neutral, and releasing it
    /// snaps engine and wheels back together (a clutch dump kicks the rear wheels loose).</summary>
    public bool Clutch;
    /// <summary>Engine disconnected from the wheels: start-line neutral or clutch pedal down.</summary>
    public bool Disengaged => Neutral || Clutch;
    /// <summary>Seconds left of the post-release clutch kick (for HUD/tests).</summary>
    public float ClutchKick => _dumpTimer;
    public bool Limiter;               // bouncing off the rev limiter (for sound/HUD)
    /// <summary>Monotonic event counter for loaded, high-rpm upshifts, including shifts between physics substeps.</summary>
    public ulong PowerUpshifts { get; private set; }
    public float PowerUpshiftStrength { get; private set; }
    /// <summary>Drive force multiplier (training laps run at 25-100 % of the plan).</summary>
    public float PowerScale = 1f;
    /// <summary>The share of the engine at the front wheels, 0..1 (CarParams.DriveFront; the lab slider may go outside).</summary>
    float DriveFrontShare => _frontShare;
    float _frontShare;
    /// <summary>The split for this step: DriveFront, moved by the viscous centre lock towards the axle that spins less (smoothed; with no lock exactly DriveFront).</summary>
    void UpdateFrontShare(float dt)
    {
        float basis = Math.Clamp(P.DriveFront, 0f, 1f);
        if (P.CenterLock <= 0f || basis <= 0f) { _frontShare = basis; return; }
        float front = (Math.Abs(Wheels[0].SpinSpeed) + Math.Abs(Wheels[1].SpinSpeed)) * 0.5f;
        float rear = (Math.Abs(Wheels[2].SpinSpeed) + Math.Abs(Wheels[3].SpinSpeed)) * 0.5f;
        float want = Math.Clamp(basis + P.CenterLock * Math.Clamp((rear - front) / 4f, -1f, 1f), 0f, 1f);
        _frontShare += (want - _frontShare) * (1f - MathF.Exp(-dt / 0.06f));
    }
    /// <summary>The turbo (rocket fuel): at full boost the engine has this much more power than PowerScale gives (0: no turbo). Boost follows the throttle and the revs (Turbo).</summary>
    public float TurboGain;
    float _turbo;
    /// <summary>The boost builds with the clutch down too (the car tour's revving on the stand; cosmetic, nothing is driven).</summary>
    public bool FreeBoost;
    /// <summary>The turbo's boost now, 0..1.</summary>
    public float Boost => _turbo;
    /// <summary>Stakhanov overdrive 0..1 (350 % = 0.5, 500 % = 1, else 0): taller gearing and more
    /// grip scaled by the CarParams Overdrive* values. Not tied to engine health.</summary>
    public float Overdrive;
    /// <summary>A CarParams Overdrive* multiplier at the current overdrive (exactly 1 at 0).</summary>
    float Od(float full) => 1f + (full - 1f) * Overdrive;
    /// <summary>Final drive with the overdrive's taller gearing.</summary>
    float FinalDrive => P.FinalDrive / Od(P.OverdriveGearing);
    public bool IgnitionCut => _limiterCut > 0;
    float _shiftTimer, _limiterCut, _limiterHold, _limiterTime;
    float _dumpTimer, _clutchDrag;
    bool _clutchWas;

    // Telemetry for HUD, sound and messages.
    public float UpsideDownTime, AirTime, OffRoadTime;
    float _landing;       // seconds of steadied landing left (CarParams.LandingSteady)
    public float LastImpact;           // strongest collision this step, m/s
    public Vector3 LastImpactLocal;    // where it hit, body frame
    public Vector3 LastImpactDir;      // which way it pushed that panel (into the car), body frame
    public bool LastImpactBirch;       // the strongest collision this step was with a birch
    public bool LastImpactBroke;       // ...and that obstacle gave way
    public ImpactMaterial LastImpactMaterial;
    public float SuspensionHit;        // how far over normal the hardest wheel load spiked, 0..
    public int PotholeHits;            // total wall hits, for tests and telemetry
    public float PotholeBangStep;      // strongest pothole hit this step (severity), 0 = none
    public float LastPotholeSeverity;
    public float BodyScrape;           // body touching the ground, 0..1
    public float Slide;                // tyre sliding amount, 0..1
    public bool LooseSurface;
    /// <summary>The surface under most of the wheels touching the ground (the tyres' sound), null in the air
    /// (works for puppets too: their wheels carry the surface).</summary>
    public SurfaceType? GroundSurface
    {
        get
        {
            SurfaceType? best = null;
            int most = 0;
            foreach (var w in Wheels)
            {
                if (!w.Contact) continue;
                int n = 0;
                foreach (var o in Wheels) if (o.Contact && o.Surface == w.Surface) n++;
                if (n > most) { most = n; best = w.Surface; }
            }
            return best;
        }
    }
    /// <summary>At least half the wheels touching the ground are on ice (works for puppets too: their wheels carry the surface).</summary>
    public bool OnIce
    {
        get
        {
            int on = 0, ice = 0;
            foreach (var w in Wheels) if (w.Contact) { on++; if (w.Surface == SurfaceType.Ice) ice++; }
            return on > 0 && ice * 2 >= on;
        }
    }

    static readonly Vector3[] BodyPoints =
    {
        // Matches assets/cars/pyatyorka2105.glb: bumper corners, sills, roof corners.
        new(-0.88f, -0.35f, -2.0f), new(0.88f, -0.35f, -2.0f),
        new(-0.88f, -0.35f, 2.0f),  new(0.88f, -0.35f, 2.0f),
        new(-0.9f, 0.25f, 0f),      new(0.9f, 0.25f, 0f),
        new(-0.72f, 0.9f, -0.3f),   new(0.72f, 0.9f, -0.3f),
        new(-0.72f, 0.9f, 0.6f),    new(0.72f, 0.9f, 0.6f),
    };
    public static readonly float[] CollisionCircleZ = { -1.3f, 0f, 1.3f };
    public const float CollisionRadius = 0.92f;
    const float G = 9.81f;
    /// <summary>Seconds of driving that wipe slurry or oil off a tyre completely (CarSim: Wheel.Slick).</summary>
    public const float SlickClean = 7f;

    public Vector3 Forward => -Rot.Z;
    public Vector3 Right => Rot.X;
    public Vector3 Up => Rot.Y;
    public float Speed => Vel.Length();
    public float ForwardSpeed => Vel.Dot(Forward);
    public Transform3D Transform => new(Rot, Pos);

    public CarSim(CarParams p, IGround ground)
    {
        P = p;
        Ground = ground;
        for (int i = 0; i < 4; i++)
        {
            bool front = i < 2, left = i % 2 == 0;
            Wheels[i] = new Wheel
            {
                Front = front,
                Left = left,
                Local = new Vector3(left ? -p.HalfTrack : p.HalfTrack, p.AttachY, front ? p.FrontZ : p.RearZ),
                VisualLength = p.RestLength,
            };
        }
    }

    /// <summary>The wheels' places are worked out from the parameters in the constructor only: the LAB calls this after it moved one of them.</summary>
    public void RefreshGeometry()
    {
        foreach (var w in Wheels)
            w.Local = new Vector3(w.Left ? -P.HalfTrack : P.HalfTrack, P.AttachY, w.Front ? P.FrontZ : P.RearZ);
    }

    public void Reset(Vector3 pos, float yaw)
    {
        _autoNeutral = false;
        Pos = pos;
        Rot = new Basis(Vector3.Up, yaw);
        Vel = AngVel = Vector3.Zero;
        Gear = 1;
        Rpm = P.IdleRpm;
        DriveAcceleration = 0;
        _dumpTimer = _clutchDrag = 0;
        SteerAngle = 0;
        UpsideDownTime = AirTime = _landing = _turbo = 0;
        foreach (var w in Wheels)
        {
            w.Compression = w.PrevCompression = w.SlipLong = 0;
            w.Slick = 0;
            w.SpinSpeed = w.SpinAngle = 0;
            w.Contact = w.Spinning = w.HandbrakeHeld = false;
        }
        _rotBoost = _flickSide = 0;
        _flickAge = 99f;
        _clutchSlide = false;
        _techSlideT = -1f;
        LastTechnique = null;
        LastFlickLifted = false;
    }

    /// <summary>
    /// Maps keyboard-style inputs to throttle/brake/gear like Rally-Sport: down brakes while rolling
    /// (either way: a car spun round and rolling backwards is braked, not driven on in reverse) and
    /// reverses when stopped. Throttle and reverse are never applied together.
    /// </summary>
    public void SetControls(bool up, bool down, float steer, bool handbrake)
    {
        LeaveIdleNeutral(up || down);
        float v = ForwardSpeed;
        ThrottleTarget = Brake = 0;
        if (Disengaged)
        {
            // With the clutch down the pedals are independent; braking must not select reverse.
            ThrottleTarget = up ? 1f : 0f;
            Brake = down ? 1f : 0f;
        }
        else if (Gear > 0)
        {
            if (up) ThrottleTarget = 1;
            else if (down)
            {
                if (Moving) Brake = 1;
                else { Gear = -1; ThrottleTarget = 1; }
            }
        }
        else
        {
            if (down) ThrottleTarget = 1;
            else if (up)
            {
                if (v < -1.0f || Moving && v < 1.0f) Brake = 1;
                else { Gear = 1; ThrottleTarget = 1; }
            }
        }
        SteerIn = Math.Clamp(steer, -1f, 1f);
        Handbrake = handbrake ? 1 : 0;
        _padSteer = false;
    }

    /// <summary>Rolling any way, sideways too (a car swapping ends is not stopped): the brake brakes, it does not select reverse.</summary>
    bool Moving => new Vector2(Vel.X, Vel.Z).Length() > 1.0f;

    /// <summary>Below this a pad's brake pedal at a standstill only holds the car; above it selects reverse.</summary>
    public const float ReversePedal = 0.1f;
    bool _padSteer;

    /// <summary>
    /// Analog pedals (0..1, a pad's triggers) with the same Rally-Sport logic as the keys: the brake
    /// pedal brakes while rolling forward and, pressed past ReversePedal at a standstill, selects reverse
    /// and is then the throttle backwards (the throttle pedal brakes it). The throttle wins when both
    /// are pressed. padSteer: the steering follows at PadSteerRate (a stick), else at the keys' rate.
    /// </summary>
    public void SetControls(float throttle, float brake, float steer, bool handbrake, bool padSteer)
    {
        LeaveIdleNeutral(throttle > .01f || brake >= ReversePedal);
        throttle = Math.Clamp(throttle, 0f, 1f);
        brake = Math.Clamp(brake, 0f, 1f);
        float v = ForwardSpeed;
        ThrottleTarget = Brake = 0;
        if (Disengaged)
        {
            ThrottleTarget = throttle;
            Brake = brake;
        }
        else if (Gear > 0)
        {
            if (throttle > 0) ThrottleTarget = throttle;
            else if (brake > 0)
            {
                if (Moving || brake < ReversePedal) Brake = brake;
                else { Gear = -1; ThrottleTarget = brake; }
            }
        }
        else
        {
            if (brake > 0) ThrottleTarget = brake;
            else if (throttle > 0)
            {
                if (v < -1.0f || Moving && v < 1.0f || throttle < ReversePedal) Brake = throttle;
                else { Gear = 1; ThrottleTarget = throttle; }
            }
        }
        SteerIn = Math.Clamp(steer, -1f, 1f);
        Handbrake = handbrake ? 1 : 0;
        _padSteer = padSteer;
    }

    /// <summary>Only the race countdown may hold the car against a slope with the clutch down.</summary>
    public void Step(float dt, bool startCountdown = false, bool waitingForClutch = false)
    {
        bool startHold=startCountdown && (Clutch || waitingForClutch);
        Vector3 startAnchor=Pos;
        if(startHold) HoldStartPosition(startAnchor);
        LastImpact = 0;
        LastImpactBirch = false;
        LastImpactBroke = false;
        LastImpactMaterial = ImpactMaterial.Unknown;
        SuspensionHit = 0;
        PotholeBangStep = 0;
        float pedal = Math.Clamp(ThrottleTarget, 0, 1);
        float throttle = pedal * (1 - P.ThrottleProgression + P.ThrottleProgression * pedal);
        Throttle = throttle < Throttle ? throttle : Math.Min(throttle, Throttle + P.ThrottleRise * dt);
        // A fuel switched in the middle of a run can give the car fewer gears (the diesel's four): the gearbox drops to the highest one it has.
        if (Gear > P.Gears.Length) Gear = P.Gears.Length;
        UpdateSteering(dt);
        UpdateFrontShare(dt);
        float driveForce = UpdateEngine(dt);
        float latGrip = Od(P.OverdriveLateralGrip), driveGrip = Od(P.OverdriveDriveGrip), brakeGrip = P.BrakeGrip * Od(P.OverdriveBrakeGrip);

        // Keep attack acceleration from taking away too much front cornering bite.
        // Use the preceding step's axle loads, shared equally as a multiplier so
        // roll still unloads the inside tyre. No recovery in the air or on the brakes.
        float frontLoad = 0, totalLoad = 0;
        int loadedWheels = 0;
        foreach (var wheel in Wheels)
            if (wheel.Contact)
            {
                totalLoad += wheel.Load;
                if (wheel.Front) frontLoad += wheel.Load;
                loadedWheels++;
            }
        float powerFrontGrip = 1f;
        if (loadedWheels == 4 && totalLoad > P.Mass * G * 0.5f
            && Brake == 0 && Handbrake == 0 && !Disengaged && Gear > 0 && Up.Y > 0.9f)
        {
            float staticFront = totalLoad * P.RearZ / (P.RearZ - P.FrontZ);
            float lostLoad = Math.Clamp(staticFront / Math.Max(frontLoad, totalLoad * 0.15f) - 1, 0, 0.5f);
            powerFrontGrip += lostLoad * Math.Clamp(P.PowerFrontGripRecovery, 0, 1)
                * Throttle * Math.Clamp(DriveAcceleration / 4f, 0, 1);
        }

        Vector3 force = new(0, -G * P.Mass, 0);
        Vector3 torque = Vector3.Zero;
        Vector3 up = Up;
        float mShare = P.Mass * 0.25f;
        float slide = 0, gripSum = 0;
        Vector3 lateralTyres = Vector3.Zero;
        int contacts = 0, loose = 0;
        float waterWheels = 0;      // wheels in the river, by how deep the tyre stands in the water (a whole wheel at 0.4 m)
        bool offRoad = true;
        // Spun round: sliding tail first in a forward gear. The car swaps ends (ApplyHandling) and
        // soft ground digs into the sliding tyres.
        float hSpeed = new Vector2(Vel.X, Vel.Z).Length();
        Spun = Gear > 0 && ForwardSpeed < 0 && hSpeed > P.SwapFrom;

        foreach (var w in Wheels)
        {
            Vector3 attach = Pos + Rot * w.Local;
            w.Contact = false;
            w.Locked = false;
            w.ForceLong = w.VelLong = w.LongLimit = 0;
            w.Load = 0;
            w.Spinning = false;
            w.SlipLat = 0;

            if (up.Y < 0.25f)
            {
                w.PrevCompression = w.Compression = 0;
                w.VisualLength = P.RestLength;
                continue;
            }

            GroundSample g = Ground.Sample(attach.X, attach.Z);
            w.Surface = g.Surface;
            float len = (attach.Y - P.WheelRadius - g.Height) / up.Y;
            float x = P.RestLength - len;
            float stands = g.WaterDepth - (attach.Y - P.WheelRadius - g.Height);      // how far the tyre's lowest point is below the water, flying over the bed or on it
            float sub = stands > 0f ? Math.Min(1f, stands / WadeFull) : 0f;
            waterWheels += sub;

            // Pothole wall: the bang grows with the speed into the wall and the hole's depth.
            // Checked even if the wheel is briefly airborne over the hole (it still hits the lip).
            // Crawling through just rocks the car; at speed it hammers the suspension.
            w.HitCooldown -= dt;
            if (g.Surface == SurfaceType.Pothole && w.HitCooldown <= 0 && x > -0.15f)
            {
                Vector3 relW = attach - Pos;
                Vector3 vw = Vel + AngVel.Cross(relW);
                float slopeLen = g.Slope.Length();
                float rise = g.Slope.X * vw.X + g.Slope.Y * vw.Z;   // dh/dt, m/s
                if (rise > 0.8f && slopeLen > 0.3f)
                {
                    float into = rise / slopeLen;                    // horizontal speed into the wall
                    float depth = Math.Min(1f, Math.Min(slopeLen, 1.2f) / 0.8f);
                    PotholeBang(w, new Vector3(relW.X, relW.Y - P.RestLength, relW.Z), into / 20f * depth);
                    w.HitCooldown = 0.25f;
                }
            }
            if (x <= 0)
            {
                w.PrevCompression = w.Compression = 0;
                w.VisualLength = P.RestLength;
                continue;
            }

            // ---- suspension
            float xdot = (x - w.PrevCompression) / dt;
            w.PrevCompression = w.Compression = x;
            float waterDamp = 1f + (WaterDamper - 1f) * sub;      // the river takes the hit and gives nothing back
            float fs = P.SpringK * x + (xdot > 0 ? P.Damper * waterDamp : P.Damper + (P.ReboundDamper - P.Damper) * Math.Min(1f, -xdot / P.ReboundKnee)) * xdot;      // (the water cushions the way in; the way out is left alone, a heavier rebound damper would pull the wheel off its support)
            if (x > P.MaxTravel)
            {
                // The bump stop: stiff and damped going in, little push left coming out (no rubber-ball landings).
                float stop = P.BumpStopK * (x - P.MaxTravel);
                fs += xdot > 0 ? stop + P.BumpStopDamp * xdot : stop * P.BumpStopReturn;
            }
            fs = Math.Clamp(fs, 0f, P.Mass * G * 8f);
            w.Load = fs;
            w.Contact = true;
            w.VisualLength = Math.Max(len, P.RestLength - P.MaxTravel - 0.05f);
            contacts++;

            force += up * fs;
            torque += (attach - Pos).Cross(up * fs);

            // ---- tyre
            SurfaceInfo si = Surfaces.Get(g.Surface);
            if (si.Loose) loose++;
            if (g.Surface is SurfaceType.Concrete or SurfaceType.Pothole or SurfaceType.Runway or SurfaceType.Gravel or SurfaceType.Mud
                or SurfaceType.Ice or SurfaceType.Snow) offRoad = false;

            // The share of the engine this wheel gets: the rear axle's by default; with DriveFront (the SPUTNIK's four-wheel drive) the front takes its part and the rear the rest.
            float driveShare = w.Front ? DriveFrontShare : 1f - DriveFrontShare;
            bool driven = driveShare > 0f;
            Vector3 n = g.Normal;
            Vector3 fwd = w.Front ? Forward.Rotated(up, -SteerAngle) : Forward;
            Vector3 f = (fwd - n * fwd.Dot(n)).Normalized();
            Vector3 r = f.Cross(n);
            Vector3 contact = new(attach.X, g.Height, attach.Z);
            Vector3 rel = contact - Pos;
            Vector3 vc = Vel + AngVel.Cross(rel);
            float vLong = vc.Dot(f), vLat = vc.Dot(r);
            float wheelSpeed = new Vector2(vc.X, vc.Z).Length();
            // Slurry and oil: the road's film makes the tyre almost as slippery as grease, and the tread keeps it for a while after the car has left the patch (it wipes off over
            // SlickClean seconds of driving, three times faster through water).
            float roadSlick = Math.Max(g.SlurryWetness, g.OilFilm);
            if (roadSlick >= w.Slick) w.Slick = roadSlick;
            else w.Slick = Math.Max(0f, w.Slick - dt / SlickClean * (g.Surface is SurfaceType.Water or SurfaceType.Shallow or SurfaceType.Puddle ? 3f : 1f));
            float mu = (w.Front ? P.FrontGrip : P.RearGrip) * SlurryTrail.Grip(si.GripAt(wheelSpeed), Math.Max(roadSlick, w.Slick));
            float max = mu * fs;
            // HandbrakeForce is the equivalent braking force per rear wheel. While it holds the wheel
            // against the engine the wheel is locked; if the engine overpowers it, the wheel turns and
            // only the surplus reaches the road, through the same grip-limited drive tyre as ever.
            bool handbrakeOn = !w.Front && Handbrake > 0;
            // Held below 85 % of the brake torque, released above 100 %: engine torque itself changes
            // between a locked and a rolling wheel (revs follow the wheel), so one threshold would chatter.
            // The demanded torque decides, so a rev-limiter cut or a gear change does not lock the wheel.
            float engineShare = Math.Max(Math.Abs(driveForce), Math.Abs(_driveDemand)) * 0.5f * driveShare;
            w.HandbrakeHeld = handbrakeOn && engineShare <= P.HandbrakeForce * (w.HandbrakeHeld ? 1f : 0.85f);
            bool locked = w.HandbrakeHeld;

            // Longitudinal: drive on the rear axle, brakes, rolling resistance.
            float fx = driven ? driveForce * 0.5f * driveShare : 0;
            // A wheel the handbrake holds turns at zero: the brake reacts the engine's torque, and the
            // road only gives sliding friction against the patch's motion (the brake term below).
            if (locked) fx = 0;
            // Handbrake on but the engine past the hold: the brake cancels as much of the push as it can.
            // The speed-based brake term below grows with |vLong| (zero at rest); this supplies only the
            // shortfall up to the handbrake torque, so the two never count the same brake twice.
            else if (handbrakeOn)
            {
                float stopCapNow = Math.Abs(vLong) * mShare / dt;
                fx -= Math.Sign(fx) * Math.Min(Math.Abs(fx), Math.Max(0, P.HandbrakeForce - stopCapNow));
            }
            float resist = Brake * P.BrakeForce * 0.5f * (w.Front ? P.BrakeBias : 1f - P.BrakeBias);
            if (handbrakeOn) resist += P.HandbrakeForce;   // always a brake against the patch's motion, engine or not
            // Rolling resistance is the tyre's own: a drive wheel spinning against the car's motion
            // (spun round, on the gas) still has it on top of its grip-limited push (added below).
            bool against = driven && driveForce != 0 && Math.Sign(driveForce) == -Math.Sign(vLong) && Math.Abs(vLong) > 1f;
            float rolling = si.Rolling * fs;
            if (!against) resist += rolling;
            if (driven && Throttle == 0 && !Disengaged) resist += P.EngineBrake * Rpm / P.Redline * driveShare;
            if (driven) resist += _clutchDrag * 0.5f * driveShare;
            float stopCap = Math.Abs(vLong) * mShare / dt;
            fx -= Math.Sign(vLong) * Math.Min(resist, stopCap);

            // Lateral: slip-angle curve, damped at low speed so the car can stand still.
            float alpha = MathF.Atan2(vLat, Math.Max(Math.Abs(vLong), 1f));
            float latMax = max * latGrip, driveMax = max * driveGrip;
            if (w.Front) latMax *= powerFrontGrip;
            float fyCurve = -Math.Sign(alpha) * latMax * TyreCurve(Math.Abs(alpha) / P.PeakSlip);
            float fyDamp = -vLat * mShare / dt * 0.5f;
            float fy = Math.Abs(fyCurve) < Math.Abs(fyDamp) ? fyCurve : fyDamp;
            if (locked) fy *= 0.35f;

            float driveDirection = driveForce != 0 ? Math.Sign(driveForce) : Gear > 0 ? 1 : -1;
            bool storedSpin = driven && !locked && driveDirection * (w.SpinSpeed * P.WheelRadius - vLong) > 0.1f;
            bool driving = driven && !locked && (driveForce != 0 && Math.Sign(fx) == Math.Sign(driveForce) || storedSpin);
            float drivePeak = driveMax * P.DriveGrip * si.DriveBite;
            if (driving && !locked)
            {
                float sideUse = Math.Clamp(Math.Abs(fy) / Math.Max(latMax, 1), 0, 1);
                drivePeak *= MathF.Sqrt(Math.Max(0.25f, 1 - P.DriveLateralShare * sideUse * sideUse));
                // The handbrake's braking surplus is held to the same grip budget as a drive demand
                // (DriveTyre itself only limits the push).
                if (handbrakeOn && driveDirection * fx < -drivePeak) fx = -driveDirection * drivePeak;
                fx = DriveTyre(w, fx, driveDirection, drivePeak, vLong, si, dt);
            }
            else w.SlipLong = 0;

            // DriveTyre already resolves longitudinal wheel slip. Keep brake locking separate.
            bool brakeLocked = false;
            bool braking = !driving && fx != 0 && Math.Sign(fx) == -Math.Sign(vLong);
            // Hard road surfaces can take a firmer brake application before locking.
            // Wetness and wheel load already reduce max, including on broken concrete.
            float lonMax = braking ? max * brakeGrip * si.BrakeBite : driving ? Math.Max(driveMax, drivePeak) : max;
            if (!driving && Math.Abs(fx) > lonMax)
            {
                // Locked by the brakes (no ABS in a Pyatyorka): the wheel stops turning and the patch
                // slides, force opposing the sliding velocity. A locked front wheel cannot steer.
                brakeLocked = !driving && Math.Abs(vLong) > 1f;
                float slipV = Math.Max(Math.Abs(vLong), 0.5f);
                float lat = Math.Abs(vLat);
                float slideLen = MathF.Sqrt(slipV * slipV + lat * lat);
                fx = Math.Sign(fx) * lonMax * 0.9f * slipV / slideLen;
                float fyMax = latMax * 0.9f * lat / slideLen;
                fy = Math.Clamp(fy, -fyMax, fyMax);
            }
            else
            {
                float k = Math.Min(1f, Math.Abs(fx) / Math.Max(lonMax, 1f));
                float fyMax = latMax * MathF.Sqrt(Math.Max(0, 1f - P.LongitudinalShare * k * k));
                fy = Math.Clamp(fy, -fyMax, fyMax);
            }
            gripSum += latMax * (brakeLocked ? 0.2f : locked ? 0.4f : 1f);
            if (against) fx -= Math.Sign(vLong) * Math.Min(rolling, Math.Abs(vLong) * mShare / dt);

            w.ForceLong = fx; w.VelLong = vLong; w.LongLimit = lonMax;
            Vector3 tyre = f * fx + r * fy;
            lateralTyres += r * fy;

            // Hitting a pothole wall or slab edge shoves the wheel back/sideways, scaled by how
            // hard the spring is loaded. One-sided hits yank the steering and cost speed.
            if (g.Slope != Vector2.Zero)
            {
                float speedK = Math.Clamp(wheelSpeed / 15f, 0f, 2f);
                Vector3 edge = new Vector3(-g.Slope.X, 0, -g.Slope.Y) * fs * P.EdgeKick * speedK;
                float el = edge.Length(), cap = fs * 0.8f;
                if (el > cap) edge *= cap / el;
                // Only resist motion into the wall, never propel the car.
                if (edge.Dot(vc) < 0) tyre += edge;
            }

            // Water and deep mud push on the tyre itself (not through its grip) with the square of
            // speed: a splash at 20 km/h, a wall at 90. Applied per wheel, so a puddle under one
            // side pulls the car round.
            float wetDrag = si.WetDrag + (g.Verge ? P.VergeDrag : 0f);
            if (wetDrag > 0f && wheelSpeed > 0.5f)
            {
                var hvc = new Vector3(vc.X, 0, vc.Z);
                float load = Math.Clamp(fs / (P.Mass * G * 0.25f), 0.3f, 2f);
                float drag = Math.Min(wetDrag * wheelSpeed * wheelSpeed * load, wheelSpeed * mShare / dt);
                tyre -= hvc / wheelSpeed * drag;
            }

            // Spun round on soft ground: the tyres sliding tail first or sideways dig in.
            if (Spun && si.Loose && si.Rolling >= 0.05f && wheelSpeed > 0.5f)
            {
                var hvc = new Vector3(vc.X, 0, vc.Z);
                float dig = Math.Min(P.SoftDig * si.Rolling * fs * Math.Min(1f, wheelSpeed / 3f), wheelSpeed * mShare / dt);
                tyre -= hvc / wheelSpeed * dig;
            }

            force += tyre;
            torque += rel.Cross(tyre);
            SuspensionHit = Math.Max(SuspensionHit, fs / (P.Mass * G * 0.25f) - 2.5f);


            w.SlipLat = vLat;
            w.Locked = (locked || brakeLocked) && Math.Abs(vLong) > 1f;
            w.ContactPoint = contact;
            w.ContactVel = vc;
            if (w.Locked) w.SpinSpeed = 0;
            else if (!driven || !driving) w.SpinSpeed = vLong / P.WheelRadius;
            slide = Math.Max(slide, Math.Clamp((Math.Abs(vLat) - 1.5f) / 5f, 0, 1));
            if (w.Spinning && Math.Abs(vLong) > 1) slide = Math.Max(slide, Math.Clamp(w.SlipLong, 0, 1) * 0.6f);
            if (w.Locked) slide = Math.Max(slide, Math.Clamp(Math.Abs(vLong) / 8f, 0.3f, 0.9f));
        }

        foreach (var w in Wheels)
        {
            if (!w.Contact)
            {
                w.HandbrakeHeld = false;   // a lifted wheel starts the next landing unlatched
                if (!w.Front && !Disengaged || w.Front && DriveFrontShare > 0f && !Disengaged)
                {
                    float wheelRatio = (Gear > 0 ? P.Gears[Gear - 1] : P.ReverseGear) * FinalDrive;
                    w.SpinSpeed = Rpm * MathF.Tau / (60f * wheelRatio) * (Gear > 0 ? 1 : -1);
                    if (Brake > 0.1f || Handbrake > 0.1f) w.SpinSpeed = 0;
                }
                else w.SpinSpeed *= MathF.Exp(-0.6f * dt);
            }
            w.SpinAngle += w.SpinSpeed * dt;
        }

        int frontLocked = (Wheels[0].Locked ? 1 : 0) + (Wheels[1].Locked ? 1 : 0);
        // Locked tyres still slide, but retain a little arcade steering authority.
        // Partial lock loses authority in two smaller steps rather than cancelling it.
        float lockedSteering = Math.Clamp(P.LockedBrakeSteering, 0f, 1f);
        ApplyHandling(ref torque, gripSum, dt, 1f - frontLocked * 0.5f * (1f - lockedSteering));

        // Wading: the water pushes on the whole car, not only on the tyres. It takes the speed (a body drag on top of the tyres' own) and the vertical motion with it: a landing in
        // the river is soaked up, no hop off the bed (the cars used to splash through at nearly full speed and bounce like a rubber ball).
        if (waterWheels > 0f)
        {
            float wet = waterWheels / 4f;
            force -= new Vector3(Vel.X, 0, Vel.Z) * (P.Mass * WaterBodyDrag * wet);
            force.Y -= Vel.Y * P.Mass * WaterSink * wet;
        }

        // Arcade drift: side forces from sliding tyres still bend the path, but most of their
        // braking component (scrub against the direction of travel) is forgiven.
        Vector3 hv = new(Vel.X, 0, Vel.Z);
        if (hv.LengthSquared() > 1f && !Spun)      // spun round, the sideways scrub counts in full
        {
            Vector3 vdir = hv.Normalized();
            float scrub = -lateralTyres.Dot(vdir);
            // With the handbrake pulled the forgiveness fades out as the path turns broadside to the car (all of
            // the side force is then scrub, and the car has to slow down), reaching nothing at 90 degrees where the
            // spun-round case counts it in full. Up to 60 degrees of drift and everywhere without the handbrake it
            // is the old rule exactly.
            float along = 1f;
            if (Handbrake > 0)
            {
                // Compare horizontal directions: body pitch or vertical velocity on a hill must not
                // change how much of this horizontal scrub is forgiven.
                var heading = new Vector3(Forward.X, 0, Forward.Z).Normalized();
                float alignment = heading.Dot(vdir) * (Gear >= 0 ? 1f : -1f);
                along = Math.Clamp(alignment / 0.5f, 0f, 1f);
            }
            if (scrub > 0) force += vdir * scrub * P.ScrubCancel * along;
        }

        Slide = slide;
        LooseSurface = loose * 2 >= Math.Max(1, contacts);
        if (contacts > 0 && AirTime > 0.25f) _landing = P.LandingSteady;
        else if (contacts > 0) _landing = Math.Max(0f, _landing - dt);
        AirTime = contacts == 0 ? AirTime + dt : 0;
        OffRoadTime = contacts > 0 && offRoad ? OffRoadTime + dt : (contacts > 0 ? 0 : OffRoadTime);

        // ---- body against the ground (roof, sills, bumpers)
        BodyScrape = 0;
        foreach (var bp in BodyPoints)
        {
            Vector3 rel = Rot * bp;
            Vector3 pw = Pos + rel;
            float gh = Ground.Sample(pw.X, pw.Z).Height;
            float pen = gh - pw.Y;
            if (pen <= 0) continue;
            Vector3 n = Vector3.Up;
            Vector3 v = Vel + AngVel.Cross(rel);
            float vn = v.Dot(n);
            float fn = Math.Max(0, 90000f * pen - 7000f * vn);
            Vector3 vt = v - n * vn;
            float vtl = vt.Length();
            Vector3 fb = n * fn;
            if (vtl > 1e-3f) fb -= vt / vtl * Math.Min(0.6f * fn, vtl * P.Mass / 10f / dt);
            force += fb;
            torque += rel.Cross(fb);
            BodyScrape = Math.Max(BodyScrape, Math.Min(1, vtl / 5f));
            if (-vn > LastImpact)
            {
                LastImpact = -vn;
                LastImpactLocal = bp;
                LastImpactDir = Rot.Transposed() * n;
                LastImpactMaterial = ImpactMaterial.Ground;
            }
        }

        // ---- aero drag
        force -= Vel * Vel.Length() * P.Drag;

        // ---- integrate
        Vector3 travel = new(Vel.X, 0, Vel.Z);
        Vector3 driveHeading = travel.LengthSquared() > 1 ? travel.Normalized() : new Vector3(Forward.X, 0, Forward.Z).Normalized();
        DriveAcceleration = contacts >= 2 && Throttle > 0.1f && !Disengaged && Gear > 0 && ForwardSpeed >= 0
            ? Math.Clamp(force.Dot(driveHeading) / P.Mass, 0, 20) : 0;
        Vel += force / P.Mass * dt;
        Pos += Vel * dt;

        Basis inv = Rot.Transposed();
        Vector3 tl = inv * torque;
        Vector3 wl = inv * AngVel;
        wl += new Vector3(tl.X / P.Inertia.X, tl.Y / P.Inertia.Y, tl.Z / P.Inertia.Z) * dt;
        if (contacts == 0 && AirTime > P.AirFrom && Up.Y > 0.2f)
        {
            // In the air (a jump, not a hop over a pothole): roll and pitch spin damped, the body eased back upright (CarParams.AirSpinDamp).
            float k = MathF.Exp(-P.AirSpinDamp * dt);
            wl.X *= k;
            wl.Z *= k;
            wl.Y *= MathF.Exp(-P.AirYawDamp * dt);
            Vector3 level = inv * Up.Cross(Vector3.Up);       // the axis (and sine) to turn about towards upright, in the body frame
            wl.X += level.X * P.AirLevel * dt;
            wl.Z += level.Z * P.AirLevel * dt;
        }
        if (contacts > 0 && _landing > 0) wl.Y *= MathF.Exp(-P.LandingYawDamp * dt);      // just landed: the swing settles
        AngVel = Rot * wl;
        AngVel *= 1f - P.AngularDamping * dt;
        float wlen = AngVel.Length();
        if (wlen > 1e-6f)
            Rot = (new Basis(AngVel / wlen, wlen * dt) * Rot).Orthonormalized();

        CollideObstacles(dt);
        if(startHold) HoldStartPosition(startAnchor);
        UpsideDownTime = Up.Y < 0.3f ? UpsideDownTime + dt : 0;
    }

    void HoldStartPosition(Vector3 anchor)
    {
        // The suspension and free-revving engine still run; only ground-plane travel is held.
        Pos=new Vector3(anchor.X,Pos.Y,anchor.Z);
        Vel=new Vector3(0,Vel.Y,0);
        AngVel=new Vector3(AngVel.X,0,AngVel.Z);
    }

    /// <summary>
    /// A wheel slams into a pothole wall: rearward + upward impulse at that corner. Costs speed,
    /// twitches the nose toward the hit side and bounces the soft body.
    /// </summary>
    void PotholeBang(Wheel w, Vector3 rel, float severity)
    {
        var hv = new Vector3(Vel.X, 0, Vel.Z);
        if (hv.LengthSquared() < 1f) return;
        Vector3 back = -hv.Normalized();
        Vector3 j = (back * P.PotholeBrake + Vector3.Up * P.PotholeBounce) * severity * P.Mass;
        ApplyImpulse(rel, j);
        SuspensionHit = Math.Max(SuspensionHit, 1f + severity * 2f);
        PotholeHits++;
        LastPotholeSeverity = severity;
        PotholeBangStep = Math.Max(PotholeBangStep, severity);
    }

    /// <summary>Blast wave from an explosion (shell hit): shoves the car away and up, twists it.</summary>
    public void Blast(Vector3 center, float strength, float radius = 12f)
    {
        Vector3 d = Pos - center;
        float dist = d.Length();
        if (dist > radius) return;
        float k = strength * (1f - dist / radius);
        Vector3 away = new Vector3(d.X, 0, d.Z);
        away = away.LengthSquared() > 1e-4f ? away.Normalized() : Forward;
        Vel += (away * 5f + Vector3.Up * 3.5f) * k;
        // Twist around the side nearer the blast.
        Vector3 localAway = Rot.Transposed() * away;
        AngVel += Rot * new Vector3(localAway.Z * 1.2f, localAway.X * 0.8f, -localAway.X * 1.6f) * k;
    }

    void ApplyImpulse(Vector3 rel, Vector3 j)
    {
        Vel += j / P.Mass;
        Basis inv = Rot.Transposed();
        Vector3 tl = inv * rel.Cross(j);
        Vector3 dwl = new(tl.X / P.Inertia.X, tl.Y / P.Inertia.Y, tl.Z / P.Inertia.Z);
        AngVel += Rot * dwl;
    }

    float TyreCurve(float x)
    {
        if (x < 1f) return x * (1.5f - 0.5f * x * x);
        return 1f - (1f - P.SlideGrip) * Math.Min(1f, (x - 1f) / 2f);
    }

    /// <summary>
    /// Rally-Sport style handling on top of the tyre model: the car's yaw follows the steering
    /// and sideways drift bleeds off, both scaled by how much grip the wheels have right now.
    /// A wheel in the air or on mud gives less authority, so bumps and body roll still matter.
    /// Applied at the CG, so it does not cancel the body roll the tyres create.
    /// </summary>
    /// <param name="steerAuthority">1 = front wheels rolling; reduced authority when brake-locked.</param>
    void ApplyHandling(ref Vector3 torque, float gripSum, float dt, float steerAuthority)
    {
        float grip = Math.Clamp(gripSum / (P.Mass * G), 0f, 1.2f);
        if (grip <= 0f) return;
        Vector3 up = Up;
        float hb = Handbrake > 0 ? P.HandbrakeAssist : 1f;

        float vF = ForwardSpeed;
        float wheelbase = P.RearZ - P.FrontZ;
        float target = -vF * MathF.Tan(SteerAngle) / wheelbase * steerAuthority;
        // On loose surfaces the tail is allowed to swing wider than the tyres could hold: the
        // car rotates into the corner in a small drift, rally style.
        // Once sliding, the car may rotate further still, so you steer the drift, not the tyres.
        float looseRotate = LooseSurface ? P.LooseRotation : 0f;
        float vLat = Vel.Dot(Right);
        float slideAmt = Math.Clamp(MathF.Atan2(Math.Abs(vLat), Math.Max(Math.Abs(vF), 1f)) / 0.35f, 0f, 1f);
        float slideRotate = slideAmt * P.SlideRotation;
        float maxYaw = (grip * G * 1.05f + P.BaseRotation + looseRotate + slideRotate) / Math.Max(Math.Abs(vF), 2f);
        float fastSteer = FastSteeringBoost();
        maxYaw *= 1f + fastSteer;
        target = Math.Clamp(target, -maxYaw, maxYaw);
        float yawRate = AngVel.Dot(up);
        SlideTechniques(dt, vF, yawRate);
        if (Math.Abs(_rotBoost) > 0.01f)
        {
            // The technique's swing goes on top of the steering, beyond what the tyres allow.
            target = Math.Clamp(target + _rotBoost, -maxYaw - Math.Abs(_rotBoost), maxYaw + Math.Abs(_rotBoost));
        }
        if (Spun)
        {
            // Tail first the car swaps ends: the heavy nose swings round the short way towards
            // where the car is going (the way it already turns when straight), faster the faster
            // it slides; the steering still adds its bit.
            var vdir = new Vector3(Vel.X, 0, Vel.Z).Normalized();
            float cross = Forward.Cross(vdir).Dot(up);
            float way = MathF.Abs(cross) > 0.05f ? MathF.Sign(cross) : MathF.Abs(yawRate) > 0.05f ? MathF.Sign(yawRate) : SteerIn != 0 ? -MathF.Sign(SteerIn) : 1f;
            float slideSpd = new Vector2(Vel.X, Vel.Z).Length();
            // It is the front wheels flopping round (the caster works the wrong way backwards) that
            // swings it: on the brakes they are locked and the car slides on tail first.
            float swap = P.SwapRate * Math.Clamp((slideSpd - P.SwapFrom) / (P.SwapSpeed - P.SwapFrom), 0f, 1f) * (1f - Brake);
            target = Math.Clamp(target + way * swap, -P.SwapRate * 1.5f, P.SwapRate * 1.5f);
        }
        // Yaw authority falls off gently with grip (sqrt), so slippery ground still steers.
        float authority = MathF.Sqrt(Math.Min(grip, 1f));
        torque += up * ((target - yawRate) * P.YawAssist * P.Inertia.Y * authority * hb);

        // Bleed off drift by turning the velocity toward where the car points, keeping its speed:
        // a slide changes direction instead of scrubbing the car to a halt.
        var hv = new Vector2(Vel.X, Vel.Z);
        float spd = hv.Length();
        if (spd > 0.5f && !Spun)     // spun round, the path does not follow the tail
        {
            var fwd = new Vector2(Forward.X, Forward.Z).Normalized();
            if (vF < 0) fwd = -fwd;
            float ang = hv.AngleTo(fwd);
            float k = Math.Min(1f, P.SideAssist * (_landing > 0 ? P.LandingSideAssist : 1f) * Math.Min(grip, 1f) * hb * dt);
            k = Math.Min(1f, k * (1f + fastSteer));
            if (Math.Abs(_rotBoost) > 0.1f) k *= 0.6f;       // swinging: let the slide run
            // A technique's slide (a flick, a clutch kick) lasts its first second and then as long as the car is sideways.
            if (_techSlideT >= 0 && (_techSlideT += dt) > 1f && Math.Abs(ang) < TechSlideEnd) _techSlideT = -1f;
            if (_techSlideT >= 0 && vF > 0 && (Wheels[2].Spinning || Wheels[3].Spinning)) k = Math.Min(1f, k * (1f + (PowerSlideK - 1f) * Throttle));
            hv = hv.Rotated(ang * k);
            Vel = new Vector3(hv.X, Vel.Y, hv.Y);
        }
    }

    float _rotBoost, _flickSide, _flickAge = 99f;
    /// <summary>The swing of a slide technique now (rad/s, signed as the yaw rate): tests, the school.</summary>
    public float RotationBoost => _rotBoost;
    /// <summary>The last technique that set a swing going: "handbrake", "clutch", "flick" (tests, the school).</summary>
    public string LastTechnique { get; private set; }
    /// <summary>The last flick was done with the throttle lifted (the full swing), not on the gas.</summary>
    public bool LastFlickLifted { get; private set; }
    /// <summary>Flicks so far (tests: how often normal driving sets one off).</summary>
    public int Flicks { get; private set; }

    /// <summary>
    /// The slide techniques' swing (CarParams.HandbrakeRotate etc.): set while its cause lasts, dying
    /// away after. Signed as the yaw rate: the way the steering turns the car (forwards).
    /// </summary>
    void SlideTechniques(float dt, float vF, float yawRate)
    {
        float speedK = Math.Clamp((Math.Abs(vF) - 4f) / 8f, 0f, 1f);
        float into = -MathF.Sign(SteerIn) * MathF.Sign(vF);           // yaw sign the steering asks for
        float want = 0f;
        bool held = false;
        // The handbrake: the tail swings round as long as it is pulled and the wheel is turned.
        if (Handbrake > 0 && MathF.Abs(SteerIn) > 0.2f && vF > 2f)
        {
            want = into * P.HandbrakeRotate * speedK;
            held = true;
            LastTechnique = "handbrake";
        }
        // A clutch kick in a bend: the dump breaks the rear loose; on the throttle with the rear
        // spinning the slide goes on.
        bool spinning = Wheels[2].Spinning || Wheels[3].Spinning;
        if (_dumpTimer > 0 && _kickNow && MathF.Abs(SteerIn) > 0.2f && vF > 4f || _clutchSlide && spinning && Throttle > 0.5f && MathF.Abs(SteerIn) > 0.2f)
        {
            want = into * P.ClutchRotate * speedK;
            held = true;
            _clutchSlide = true;
            LastTechnique = "clutch";
            _techSlideT = 0f;
        }
        else if (!spinning || Throttle < 0.3f) _clutchSlide = false;
        // The Scandinavian flick: hard one way, then hard the other within a moment, the throttle
        // lifted: the car swings into the new way like a pendulum, the harder the faster it was turning.
        _flickAge += dt;
        if (MathF.Abs(SteerIn) > 0.6f)
        {
            float side = MathF.Sign(SteerIn);
            float share = Throttle < 0.4f || Brake > 0 ? 1f : P.FlickOnThrottle;
            if (_flickSide != 0 && side != _flickSide && _flickAge < 0.7f && vF > 7f && share > 0f)
            {
                _rotBoost = into * (P.FlickRotate + 0.8f * MathF.Abs(yawRate)) * speedK * share;
                LastTechnique = "flick";
                LastFlickLifted = share >= 1f;
                _techSlideT = 0f;
                Flicks++;
            }
            _flickSide = side;
            _flickAge = 0;
        }
        if (held) _rotBoost = Mathf.MoveToward(_rotBoost, want, 6f * dt);
        else _rotBoost *= MathF.Exp(-dt / P.RotateDecay);
        if (!held && MathF.Abs(_rotBoost) < 0.02f) _rotBoost = 0;
    }

    bool _kickNow, _clutchSlide;
    float _techSlideT = -1f;                                   // seconds into a technique's slide, -1 none
    const float TechSlideEnd = 0.07f;                          // rad (4°): straight again, the slide over
    static readonly float PowerSlideEnv = System.Environment.GetEnvironmentVariable("PSA") is { Length: > 0 } e ? float.Parse(e, System.Globalization.CultureInfo.InvariantCulture) : -1f;
    float PowerSlideK => PowerSlideEnv > 0 ? PowerSlideEnv : P.PowerSlideAssist;

    // Large deliberate inputs retain more authority at speed. The smooth ramps
    // leave small stick corrections and low-speed steering alone; both control
    // paths use the same handling, without changing the stick's input curve.
    float FastSteeringBoost()
    {
        if (Brake > 0 || Handbrake > 0 || Spun) return 0;
        float speed = Math.Clamp((ForwardSpeed * 3.6f - 50f) / 50f, 0f, 1f);
        float input = Math.Clamp((Math.Abs(SteerIn) - 0.45f) / 0.55f, 0f, 1f);
        speed *= speed * (3f - 2f * speed);
        input *= input * (3f - 2f * input);
        // Overdrive already adds lateral grip. Stacking the full extra steering
        // on top can trip the car during a 500% countersteer on dry concrete.
        return Math.Clamp(P.FastSteerAssist, 0f, 0.5f) * speed * input
            * (1f - Math.Clamp(Overdrive, 0f, 1f));
    }

    void UpdateSteering(float dt)
    {
        float max = P.MaxSteer / (1f + Math.Abs(ForwardSpeed) / P.SteerSpeedFalloff);
        max *= 1f + FastSteeringBoost();
        float target = SteerIn * max;
        float step = (_padSteer ? P.PadSteerRate : P.SteerRate) * dt;
        SteerAngle += Math.Clamp(target - SteerAngle, -step, step);
    }

    /// <summary>Engine force the driver is asking for at the rear axle, ignoring ignition cuts and shift gaps (derived each step, not state).</summary>
    float _driveDemand;

    /// <summary>Updates rpm and automatic gearbox; returns total drive force at the rear axle.</summary>
    float UpdateEngine(float dt)
    {
        _driveDemand = 0;
        // Do not load the engine against the rear wheels while waiting. A neutral
        // latch belongs to the auto box, separately from countdown/mechanic N.
        if (_autoNeutral && (ThrottleTarget > .01f || Moving)) LeaveIdleNeutral(true);
        if (!_neutral && !Clutch && ThrottleTarget <= .01f && Throttle <= .03f
            && new Vector2(Vel.X, Vel.Z).Length() < .2f && Math.Abs(Vel.Y) < .4f
            && Wheels[2].Contact && Wheels[3].Contact)
            _autoNeutral = true;
        _limiterCut -= dt;
        _limiterHold -= dt;
        bool released = _clutchWas && !Clutch && !Neutral;
        _clutchWas = Clutch;
        if (Disengaged)
        {
            _dumpTimer = _clutchDrag = 0;
            FreeRevEngine(dt);
            // A car on show (CAR TOUR) builds its boost on the stand too; in a race the boost waits while the clutch is down, as it always has.
            if (FreeBoost) _turbo = TurboGain > 0f ? Turbo.Step(_turbo, Turbo.Target(Throttle, Rpm), dt, P.TurboSpool) : 0f;
            return 0;
        }

        // Contacts are from the preceding substep. Without either driven tyre touching down,
        // road speed cannot load the engine and airborne limiter hits must not change gear.
        if (!Wheels[2].Contact && !Wheels[3].Contact)
        {
            _dumpTimer = _clutchDrag = 0;
            _limiterTime = 0;
            _shiftTimer = Math.Max(0, _shiftTimer - dt);
            if (Brake > 0.1f || Handbrake > 0.1f)
            {
                Rpm = Mathf.MoveToward(Rpm, P.IdleRpm, P.FreeRevFall * 2 * dt);
                Limiter = false;
            }
            else FreeRevEngine(dt, 0.7f); // engine plus the freely spinning rear wheels
            return 0;
        }

        float vRear = 0;
        int n = 0;
        foreach (var w in Wheels)
            if (!w.Front) { vRear += Vel.Dot(Forward); n++; }
        vRear /= Math.Max(1, n);

        float ratio = Gear > 0 ? P.Gears[Gear - 1] : P.ReverseGear;
        float wheelRpm = Math.Abs(vRear) / P.WheelRadius * ratio * FinalDrive * 60f / MathF.Tau;
        if (released)
        {
            // Pedal up: pick the gear a driver would for this road speed (on the gas the lowest
            // that doesn't over-rev, for the kick; off it the tallest that isn't lugging).
            if (Gear > 0)
            {
                float axleRpm = Math.Abs(vRear) / P.WheelRadius * FinalDrive * 60f / MathF.Tau;
                int g = 1;
                if (Throttle > 0.05f)
                    while (g < P.Gears.Length && axleRpm * P.Gears[g - 1] > P.ShiftUp) g++;
                else
                    while (g < P.Gears.Length && axleRpm * P.Gears[g] >= P.ShiftDown) g++;
                Gear = g;
                ratio = P.Gears[g - 1];
                wheelRpm = axleRpm * ratio;
            }
            _dumpTimer = P.ClutchDumpTime;
            _shiftTimer = _limiterTime = _limiterCut = 0;
        }
        // Clutch dump: the flywheel's surplus revs slam into the driveline for a moment. Revs above
        // road speed kick the rear wheels (the tyres cap it, so loose ground spins them up); revs
        // below it jolt like a hard engine brake. Both fade out as the rpm meets the wheels.
        float kick = 0;
        _clutchDrag = 0;
        _kickNow = false;
        if (_dumpTimer > 0)
        {
            float fade = _dumpTimer / P.ClutchDumpTime;
            _dumpTimer -= dt;
            float diff = Rpm - wheelRpm;
            if (diff > 0) kick = Math.Min(diff * P.ClutchKickForce, P.Mass * G * 2f) * fade;
            else _clutchDrag = Math.Min(-diff * P.ClutchJolt, P.Mass * G) * fade;
            _kickNow = diff > 800f;
        }
        float drivenRpm = (DriveFrontShare > 0f
                ? (Math.Abs(Wheels[0].SpinSpeed) + Math.Abs(Wheels[1].SpinSpeed)) * 0.5f * DriveFrontShare + (Math.Abs(Wheels[2].SpinSpeed) + Math.Abs(Wheels[3].SpinSpeed)) * 0.5f * (1f - DriveFrontShare)
                : (Math.Abs(Wheels[2].SpinSpeed) + Math.Abs(Wheels[3].SpinSpeed)) * 0.5f)
            * ratio * FinalDrive * 60f / MathF.Tau;
        float target = Math.Max(Math.Max(wheelRpm, drivenRpm), Throttle > 0 ? 3000f : P.IdleRpm);
        Rpm = Mathf.Lerp(Rpm, target, 1f - MathF.Exp(-10f * dt));

        // Rev limiter: ignition cut in short bursts (~25 Hz) whenever the engine hits the redline.
        // During a dump the revs are being dragged down by the wheels: only road speed counts.
        bool atLimit = Throttle > 0.05f && (_dumpTimer > 0 ? wheelRpm : Math.Max(wheelRpm, Rpm)) >= P.Redline - 30f;
        if (atLimit && _limiterCut <= -P.LimiterOn) _limiterCut = P.LimiterCut;
        bool cutting = _limiterCut > 0;
        if (atLimit) { _limiterHold = 0.08f; _limiterTime += dt; }
        else if (Math.Max(wheelRpm, Rpm) < P.Redline - 400f) _limiterTime = 0;
        Limiter = _limiterHold > 0;
        Rpm = Math.Min(Rpm, P.Redline) - (cutting ? 180f : 0f);

        // Flat out, it rattles the limiter for a moment before grabbing the next gear, like a
        // rally car; part throttle shifts early as before.
        _shiftTimer -= dt;
        if (Gear > 0 && _shiftTimer <= 0)
        {
            bool shiftUp = Throttle > 0.9f
                ? _limiterTime >= P.LimiterHold && wheelRpm >= P.ShiftUp
                : wheelRpm > P.ShiftUp;
            if (shiftUp && Gear < P.Gears.Length)
            {
                if (Throttle > 0.75f && Rpm > P.Redline * 0.7f)
                {
                    PowerUpshifts++;
                    PowerUpshiftStrength = Math.Clamp(Rpm / P.Redline * Throttle, 0.55f, 1f);
                }
                Gear++;
                _shiftTimer = P.ShiftTime;
                _limiterTime = 0;
            }
            else if (wheelRpm < P.ShiftDown && Gear > 1) { Gear--; _shiftTimer = P.ShiftTime; }
        }

        // Use the newly selected ratio for this substep's torque as well as the next one's RPM.
        ratio = Gear > 0 ? P.Gears[Gear - 1] : P.ReverseGear;

        _turbo = TurboGain > 0f ? Turbo.Step(_turbo, Turbo.Target(Throttle, Rpm), dt, P.TurboSpool) : 0f;
        float power = PowerScale * (1f + TurboGain * Turbo.Power(_turbo, P.TurboKick));
        float f = kick * power;
        if (Throttle > 0)
        {
            float demandTorque = wheelRpm > P.Redline + 300f ? 0 : P.MaxTorque * EngineCurve(Math.Max(Rpm, 2500f));
            _driveDemand = (Gear > 0 ? 1 : -1) * demandTorque * Throttle * power * ratio * FinalDrive * P.Efficiency / P.WheelRadius;
        }
        if (Throttle > 0 && _shiftTimer <= 0.1f && !cutting)
        {
            float rpm = Math.Max(Rpm, 2500f);
            float torque = wheelRpm > P.Redline + 300f ? 0 : P.MaxTorque * EngineCurve(rpm);
            f += torque * Throttle * power * ratio * FinalDrive * P.Efficiency / P.WheelRadius;
        }
        return Gear > 0 ? f : -f;
    }

    void LeaveIdleNeutral(bool demand)
    {
        if (!_autoNeutral || !demand) return;
        _autoNeutral = false;
        if (Gear > 0 && !Moving) Gear = 1;
    }

    void FreeRevEngine(float dt, float riseScale = 1)
    {
        if (Rpm >= P.Redline && _limiterCut <= -P.LimiterOn) _limiterCut = P.LimiterCut;
        if (Rpm >= P.Redline - 150f && Throttle > 0.05f) _limiterHold = 0.08f;
        Limiter = _limiterHold > 0;
        float acc = Throttle > 0.05f && _limiterCut <= 0
            ? P.FreeRevRise * riseScale * Throttle
            : -P.FreeRevFall * (_limiterCut > 0 ? 3f : 1f);
        Rpm = Math.Clamp(Rpm + acc * dt, P.IdleRpm, P.Redline + 50f);
    }

    float EngineCurve(float rpm)
    {
        float x = (rpm - P.PeakRpm) / 3600f;
        return Math.Clamp(1f - P.TorqueCurveFalloff * x * x, 0.3f, 1f);
    }

    void CollideObstacles(float dt)
    {
        // Map bounds.
        if (Pos.X < WorldMin) { Pos.X = WorldMin; Vel.X = Math.Abs(Vel.X) * 0.3f; }
        if (Pos.X > WorldMax) { Pos.X = WorldMax; Vel.X = -Math.Abs(Vel.X) * 0.3f; }
        if (Pos.Z < WorldMin) { Pos.Z = WorldMin; Vel.Z = Math.Abs(Vel.Z) * 0.3f; }
        if (Pos.Z > WorldMax) { Pos.Z = WorldMax; Vel.Z = -Math.Abs(Vel.Z) * 0.3f; }

        foreach (float cz in CollisionCircleZ)
        {
            Vector3 rel = Rot * new Vector3(0, 0, cz);
            Vector2 c = new(Pos.X + rel.X, Pos.Z + rel.Z);
            // Indexed: a breaking obstacle is replaced in the list while we go.
            for (int i = 0; i < Obstacles.Count; i++)
            {
                var o = Obstacles[i];
                float minD = CollisionRadius + o.Radius;
                Vector2 d = c - o.Center;
                float dist2 = d.LengthSquared();
                if (dist2 >= minD * minD || dist2 < 1e-6f) continue;
                float dist = MathF.Sqrt(dist2);
                Vector2 n2 = d / dist;
                Vector3 n = new(n2.X, 0, n2.Y);
                Vector3 v = Vel + AngVel.Cross(rel) - new Vector3(o.Velocity.X, 0, o.Velocity.Y);
                float vn = v.Dot(n);
                if (o.Crumbles && -vn >= o.BreakAt && BreakObstacle != null && BreakObstacle(i, -vn, Vel, Pos))
                {
                    // A stone wall crumbles: the car ploughs on through, a fixed amount of speed
                    // down along its travel, the nose kicked up over the stones. Lighter things
                    // (a bale, a person, a sign: o.Loss) take less, the kick and the blow in proportion.
                    float sp = new Vector2(Vel.X, Vel.Z).Length();
                    float loss = o.Loss >= 0f ? o.Loss : Breakables.WallSpeedLoss, share = o.Loss >= 0f ? o.Loss / Breakables.WallSpeedLoss : 1f;
                    float keep = sp > 1e-3f ? Math.Max(0f, sp - loss) / sp : 0f;
                    Vel = new Vector3(Vel.X * keep, Vel.Y + Breakables.WallJolt * share, Vel.Z * keep);
                    AngVel += Rot * new Vector3(Breakables.WallPitch * share, 0, 0);
                    NoteImpact(-vn * Breakables.ImpactShare * share, rel, n, false, true, o.Material);
                    continue;
                }
                float pen = minD - dist;
                Pos += n * pen;
                c += n2 * pen;
                if (vn >= 0) continue;
                Vector3 before = Vel;
                float rxn = rel.Cross(n).Y;
                float j = -(1f + 0.25f) * vn / (1f / P.Mass + rxn * rxn / P.Inertia.Y);
                Vel += n * j / P.Mass;
                AngVel.Y += rxn * j / P.Inertia.Y;
                // Scrape along the obstacle.
                Vector3 vt = v - n * vn;
                Vel -= new Vector3(vt.X, 0, vt.Z) * 0.2f;
                // A tree or pole: the car bounces off just the same; a hard enough hit then knocks
                // it down (gone from Obstacles), and what gives way takes part of the blow.
                bool broke = !o.Crumbles && o.BreakAt > 0 && -vn >= o.BreakAt && BreakObstacle != null && BreakObstacle(i, -vn, before, Pos);
                NoteImpact(broke ? -vn * Breakables.ImpactShare : -vn, rel, n, o.Birch && !broke, broke,
                    o.Material != ImpactMaterial.Unknown ? o.Material : o.Birch ? ImpactMaterial.Wood : ImpactMaterial.Unknown);
            }
            CollideSnowbank(rel, ref c, dt);
        }
    }

    /// <summary>Of a hard snow bank hit (bottoming out in it), this much counts as a hit (dents, the clank, the shake).</summary>
    public const float SnowbankImpactShare = 0.4f;
    /// <summary>How deep (m) a car can plough into a bank before it is packed hard.</summary>
    public const float SnowbankSoft = 0.9f;
    // Per collision circle: the snow pushes back like a soft spring, soaks up the speed into it, and
    // drags on all the car's speed the deeper it is in.
    const float WaterDamper = 3f, WaterBodyDrag = 4.5f, WaterSink = 10f, WadeFull = 0.4f;      // the river: the suspension's damping times three, 1/s of the speed the whole car loses (a river of ten metres at 90 km/h takes two thirds of it), 1/s of the vertical speed, all by how deep the tyres stand in the water
    const float BankSpring = 60f, BankSoak = 12f, BankDrag = 0.9f;

    /// <summary>
    /// A snow bank is soft: a car noses up to SnowbankSoft into it, the snow pushing it back out,
    /// soaking up the speed into the bank and dragging hard on the rest, the deeper the harder (and
    /// throwing powder, SnowbankPlough). Past that the packed snow stops it like a wall, with only a
    /// little bounce: so a car run square into a bank buries its nose and stops; one brushing it is
    /// slowed and turned back onto the road.
    /// </summary>
    void CollideSnowbank(Vector3 rel, ref Vector2 c, float dt)
    {
        if (Banks == null) return;
        if (!Banks.Query(c, CollisionRadius, ref _bankHint, out Vector2 n2, out float pen, out float top)) return;
        if (Pos.Y - 0.35f > top) return;                     // flying over it
        Vector3 n = new(n2.X, 0, n2.Y);
        Vector3 v = Vel + AngVel.Cross(rel);
        float vn = v.Dot(n);
        float depth = Math.Min(pen, SnowbankSoft);
        float share = 1f / CollisionCircleZ.Length;
        // The soft snow.
        // The push acts where the snow is, so a nose in the bank is turned out of it.
        // Snow does not spring back: the push stops once it is easing the car out at a walk.
        float push = vn < 0.8f ? BankSpring * depth * dt * share : 0f;
        Vel += n * push;
        AngVel.Y += rel.Cross(n).Y * push * P.Mass / P.Inertia.Y;
        if (vn < 0) Vel += n * -vn * Math.Min(1f, BankSoak * depth * dt);
        float drag = Math.Min(1f, BankDrag * depth * dt);
        Vel = new Vector3(Vel.X * (1f - drag), Vel.Y, Vel.Z * (1f - drag));
        AngVel *= 1f - drag * 0.5f;
        float speed = new Vector2(Vel.X, Vel.Z).Length();
        SnowbankPlough = Math.Max(SnowbankPlough, speed * depth);
        SnowbankPoint = Pos + rel - n * CollisionRadius;
        if (vn < 0 && -vn > SnowbankHit) SnowbankHit = -vn;
        if (pen <= SnowbankSoft) return;

        // Packed hard beyond: a wall with little bounce.
        Pos += n * (pen - SnowbankSoft);
        c += n2 * (pen - SnowbankSoft);
        v = Vel + AngVel.Cross(rel);
        vn = v.Dot(n);
        if (vn >= 0) return;
        const float bounce = 0.05f;
        float rxn = rel.Cross(n).Y;
        float j = -(1f + bounce) * vn / (1f / P.Mass + rxn * rxn / P.Inertia.Y);
        Vel += n * j / P.Mass;
        AngVel.Y += rxn * j / P.Inertia.Y;
        NoteImpact(-vn * SnowbankImpactShare, rel, n, false, false, ImpactMaterial.Snow);
    }

    /// <summary>Keeps the strongest collision of the step for damage, sound and messages.</summary>
    void NoteImpact(float hit, Vector3 rel, Vector3 n, bool birch, bool broke, ImpactMaterial material = ImpactMaterial.Unknown)
    {
        if (hit <= LastImpact) return;
        LastImpact = hit;
        Basis inv = Rot.Transposed();
        LastImpactLocal = inv * (rel - n * CollisionRadius);
        LastImpactDir = inv * n;
        LastImpactBirch = birch;
        LastImpactBroke = broke;
        LastImpactMaterial = material;
    }
}
