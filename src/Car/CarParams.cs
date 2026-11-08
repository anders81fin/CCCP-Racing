using Godot;

namespace CCCPRacing.Car;

/// <summary>
/// Pyatyorka 2105, kolkhoz tune. Power: ACCORDING TO PLAN. Suspension: NYET.
/// Local frame: -Z forward, +X right, +Y up, origin at centre of gravity.
/// </summary>
[GlobalClass]
public partial class CarParams : Resource
{
    [ExportGroup("Body")]
    [Export] public float Mass { get; set; } = 1050f;
    /// <summary>Inertia around local X (pitch), Y (yaw), Z (roll). Low roll inertia = tippy.</summary>
    [Export] public Vector3 Inertia { get; set; } = new(1500f, 1650f, 400f);
    [Export] public float Drag { get; set; } = 0.42f;
    [Export] public float AngularDamping { get; set; } = 0.3f;
    /// <summary>In the air (no wheel down): the roll and pitch spin dies away at this rate (1/s), and the
    /// body is turned back towards upright at AirLevel (1/s²): a hard landing on a roller no longer
    /// sends the car over in the next jump. Yaw is left alone; a car already on its back is left alone.</summary>
    [Export] public float AirSpinDamp { get; set; } = 5f;
    [Export] public float AirLevel { get; set; } = 8f;
    /// <summary>Seconds off the ground before the air steadies the body (a hop over a bump or pothole is left as it was).</summary>
    [Export] public float AirFrom { get; set; } = 0.15f;
    /// <summary>In the air the yaw spin dies away at this rate (1/s) too: a jump does not turn the car round.</summary>
    [Export] public float AirYawDamp { get; set; } = 2.5f;
    /// <summary>After a jump (more than 0.25 s in the air) for this many seconds the landing is steadied: the car's path
    /// turns towards its heading faster (LandingSideAssist times SideAssist) and extra yaw damping (LandingYawDamp, 1/s) settles the swing.</summary>
    [Export] public float LandingSteady { get; set; } = 0.7f;
    [Export] public float LandingSideAssist { get; set; } = 3f;
    [Export] public float LandingYawDamp { get; set; } = 4f;

    [ExportGroup("Suspension")]
    [Export] public float FrontZ { get; set; } = -1.175f;
    [Export] public float RearZ { get; set; } = 1.128f;
    // Flare outer face: (0.81 + 0.09) * 1.11 = 0.999 m; tyre half-width 0.11 m.
    [Export] public float HalfTrack { get; set; } = 0.889f;
    /// <summary>Suspension top relative to CG. Higher CG relative to wheels = more body roll.</summary>
    [Export] public float AttachY { get; set; } = 0.0f;
    [Export] public float WheelRadius { get; set; } = 0.34f;
    [Export] public float RestLength { get; set; } = 0.42f;
    [Export] public float MaxTravel { get; set; } = 0.2f;
    [Export] public float SpringK { get; set; } = 20000f;
    [Export] public float Damper { get; set; } = 1500f;
    /// <summary>Damping while the spring extends again (N·s/m): well above Damper, so a landing settles instead of springing the body back up.</summary>
    [Export] public float ReboundDamper { get; set; } = 4200f;
    /// <summary>The rebound damping rises from Damper to ReboundDamper with the spring's speed, reaching it at this (m/s): road noise is left as it was, a landing's rebound is held.</summary>
    [Export] public float ReboundKnee { get; set; } = 0.8f;
    [Export] public float BumpStopK { get; set; } = 120000f;
    /// <summary>Damping inside the bump stop (N·s/m): the stop eats the landing's energy as a rubber one does.</summary>
    [Export] public float BumpStopDamp { get; set; } = 9000f;
    /// <summary>Share of the bump stop's push left while it lets go: a rubber stop does not throw the body back.</summary>
    [Export] public float BumpStopReturn { get; set; } = 0.25f;
    /// <summary>The verge strip just beyond the road's edge drags a wheel on it with the square of speed (N per (m/s)² at normal load): the first metre off the road already costs a little.</summary>
    [Export] public float VergeDrag { get; set; } = 0.07f;
    /// <summary>Horizontal jolt from pothole walls and slab joints, per unit slope and wheel load.</summary>
    [Export] public float EdgeKick { get; set; } = 0.35f;
    /// <summary>Speed (m/s) a full-severity pothole hit knocks off at that wheel.</summary>
    [Export] public float PotholeBrake { get; set; } = 0.9f;
    /// <summary>Upward kick (m/s) a pothole hit gives that corner of the body.</summary>
    [Export] public float PotholeBounce { get; set; } = 0.3f;

    [ExportGroup("Tyres")]
    [Export] public float FrontGrip { get; set; } = 1.0f;
    [Export] public float RearGrip { get; set; } = 0.95f;
    /// <summary>Slip angle (rad) of peak lateral force.</summary>
    [Export] public float PeakSlip { get; set; } = 0.15f;
    /// <summary>Fraction of peak lateral grip left in a full slide.</summary>
    [Export] public float SlideGrip { get; set; } = 0.92f;
    /// <summary>How much full drive/brake force eats side grip (0 = none, 1 = friction circle).</summary>
    [Export] public float LongitudinalShare { get; set; } = 0.45f;
    /// <summary>Longitudinal bite at the best slip, independently of cornering grip.</summary>
    [Export] public float DriveGrip { get; set; } = 2.05f;
    /// <summary>How strongly cornering uses the driven tyre's available bite.</summary>
    [Export] public float DriveLateralShare { get; set; } = 0.65f;
    /// <summary>Bounded recovery of front lateral grip lost to powered axle load transfer.</summary>
    [Export] public float PowerFrontGripRecovery { get; set; } = 0.65f;
    /// <summary>Wheel inertia plus the engine inertia reflected through the current gear.</summary>
    [Export] public float DriveWheelInertia { get; set; } = 1.2f;
    [Export] public float EngineInertia { get; set; } = 0.04f;
    /// <summary>Fraction of sliding-tyre scrub that does not slow the car (0 = realistic, 1 = none).</summary>
    [Export] public float ScrubCancel { get; set; } = 0.7f;
    /// <summary>Longitudinal grip multiplier while braking: arcade brakes that stop 1.5× harder.</summary>
    [Export] public float BrakeGrip { get; set; } = 1.5f;
    /// <summary>Remaining arcade steering authority with both front tyres brake-locked.</summary>
    [Export] public float LockedBrakeSteering { get; set; } = 0.2f;

    [ExportGroup("Engine")]
    /// <summary>Peak torque at PeakRpm (Nm). 175 (owner's choice, 8 November; it was 387.5, then 200, 310 before 2 November), with the mass 1050 kg (was 1000). The petrol car is the base of every mode: the rally's petrol, Stakhanov, LAN and the school; every power percent follows (50 % diesel in the LAN, 200 % rocket fuel). The rally's DIESEL has an engine of its own (FuelGrades.DieselEngine).</summary>
    [Export] public float MaxTorque { get; set; } = 175f;
    /// <summary>The share of the engine's force that goes to the front wheels: 0 is the rear-wheel drive of the Pyatyorka, 0.4 the four-wheel drive of the SPUTNIK fuel (40 front, 60 rear).</summary>
    [Export] public float DriveFront { get; set; } = 0f;
    /// <summary>The viscous centre lock of the four-wheel drive: how far the split moves from DriveFront towards the axle that grips (0: fixed split; 0.35: up to 35 points of the engine go to the front when the rear spins faster, and to the rear when the front does).</summary>
    [Export] public float CenterLock { get; set; } = 0f;
    /// <summary>The turbo's spool-up time (s): how long the boost takes to build (63 %) with the throttle down. The jet fuel's is 0.5.</summary>
    [Export] public float TurboSpool { get; set; } = 0.5f;
    /// <summary>The turbo's kick: 0 lets the boost build evenly, 1 holds the power back through the lag and then delivers it in one violent surge (an 80s Group B turbo).</summary>
    [Export] public float TurboKick { get; set; } = 0f;
    /// <summary>Pedal response: fast opening, immediate lift; progression keeps partial throttle measured.</summary>
    [Export] public float ThrottleRise { get; set; } = 14f;
    [Export] public float ThrottleProgression { get; set; } = 0.45f;
    [Export] public float TorqueCurveFalloff { get; set; } = 0.28f;
    [Export] public float PeakRpm { get; set; } = 4800f;
    [Export] public float IdleRpm { get; set; } = 1200f;
    [Export] public float Redline { get; set; } = 7000f;
    [Export] public float ShiftUp { get; set; } = 6600f;
    [Export] public float ShiftDown { get; set; } = 3200f;
    /// <summary>Five rally gears: roughly 40/62/86/113/149 km/h at the limiter with the standard final drive.</summary>
    [Export] public float[] Gears { get; set; } = { 4.4f, 2.85f, 2.05f, 1.55f, 1.18f };
    [Export] public float ShiftTime { get; set; } = 0.18f;
    /// <summary>Free-revving in neutral (start line): rpm/s up with full throttle, down when released.</summary>
    [Export] public float FreeRevRise { get; set; } = 9000f;
    [Export] public float FreeRevFall { get; set; } = 4500f;
    /// <summary>Rev limiter ignition cut and re-fire times (s): together they set the "brrrap" rate.</summary>
    [Export] public float LimiterCut { get; set; } = 0.02f;
    [Export] public float LimiterOn { get; set; } = 0.02f;
    /// <summary>Flat out, how long the engine rattles the limiter before the next upshift.</summary>
    [Export] public float LimiterHold { get; set; } = 0.25f;
    [Export] public float ReverseGear { get; set; } = 3.4f;
    [Export] public float FinalDrive { get; set; } = 5.1f;
    [Export] public float Efficiency { get; set; } = 0.85f;
    [Export] public float EngineBrake { get; set; } = 350f;
    /// <summary>Clutch dump: how long (s) the flywheel's surplus revs keep kicking after release.</summary>
    [Export] public float ClutchDumpTime { get; set; } = 0.3f;
    /// <summary>Clutch dump: extra drive (N) per rpm the engine is above road speed (tyres cap it).</summary>
    [Export] public float ClutchKickForce { get; set; } = 3f;
    /// <summary>Clutch released below road speed: braking (N) per rpm the engine is short.</summary>
    [Export] public float ClutchJolt { get; set; } = 2f;

    [ExportGroup("Control")]
    /// <summary>Full steering angle at a standstill (rad; 0.8 is 46 degrees). It falls with speed: MaxSteer / (1 + speed / SteerSpeedFalloff). (0.7 / 50 / 0.25 until 2026-11-02: a sharper car at speed, a little tighter in the slow bends.)</summary>
    [Export] public float MaxSteer { get; set; } = 0.8f;
    /// <summary>Speed (m/s) at which the full steering angle has halved.</summary>
    [Export] public float SteerSpeedFalloff { get; set; } = 70f;
    /// <summary>Extra steering and path-following headroom at large inputs above 50 km/h.</summary>
    [Export] public float FastSteerAssist { get; set; } = 0.46f;
    [Export] public float SteerRate { get; set; } = 12f;
    /// <summary>Steering rate (rad/s) when a pad's stick steers: the wheel follows the stick, not a key's ramp.</summary>
    [Export] public float PadSteerRate { get; set; } = 30f;
    /// <summary>Per second: how hard yaw is pulled toward what the steering asks for.</summary>
    [Export] public float YawAssist { get; set; } = 6f;
    /// <summary>Rotation headroom (m/s²) beyond tyre grip on any surface: how eagerly the nose tucks in.</summary>
    [Export] public float BaseRotation { get; set; } = 5f;
    /// <summary>Per second: how fast the velocity swings toward the car's heading (drift bleed-off).</summary>
    [Export] public float SideAssist { get; set; } = 1.6f;
    /// <summary>A slide set going by a flick or a clutch kick, the gas on and the rear spinning: the drive
    /// pushes the car where its nose points, SideAssist this many times over at full throttle: the gas
    /// steers the slide. Other slides keep SideAssist alone.</summary>
    [Export] public float PowerSlideAssist { get; set; } = 1.5f;
    /// <summary>Extra rotation (m/s² of yaw headroom) allowed on gravel, dirt, mud and grass.</summary>
    [Export] public float LooseRotation { get; set; } = 4f;
    /// <summary>Extra rotation headroom while the car is already sliding (steer the drift).</summary>
    [Export] public float SlideRotation { get; set; } = 5f;
    /// <summary>Spun round and sliding tail first in a forward gear above this speed (m/s): the car
    /// swaps ends. A front-engined car will not run backwards straight, its heavy nose wants to lead.</summary>
    [Export] public float SwapFrom { get; set; } = 4f;
    /// <summary>Yaw rate (rad/s) the car swings nose-first at, once going SwapSpeed (m/s) or faster tail first.</summary>
    [Export] public float SwapRate { get; set; } = 1.5f;
    [Export] public float SwapSpeed { get; set; } = 12f;
    /// <summary>Tail first on soft ground (grass, field, mud, deep snow): the sliding tyres dig in, this
    /// many times the surface's rolling resistance on top of it.</summary>
    [Export] public float SoftDig { get; set; } = 1.5f;
    /// <summary>
    /// The slide techniques (each its own feel, the school teaches them): extra yaw rate (rad/s) the
    /// car swings at, on top of the steering's: the handbrake swings the tail round (the tightest
    /// turn, slow hairpins); a clutch kick in a bend breaks the rear loose and keeps it sliding on
    /// the throttle (a power slide); the Scandinavian flick, steering away and then hard in with the
    /// throttle lifted, swings the car the other way like a pendulum (fast long bends); on the
    /// throttle it swings too, FlickOnThrottle as much (the user's choice B, 2026-10-07).
    /// </summary>
    [Export] public float HandbrakeRotate { get; set; } = 1.2f;
    [Export] public float ClutchRotate { get; set; } = 0.5f;
    [Export] public float FlickRotate { get; set; } = 0.7f;
    /// <summary>Share of the flick's swing on the throttle (lifted it is the whole of it).</summary>
    [Export] public float FlickOnThrottle { get; set; } = 0.5f;
    /// <summary>Seconds the swing takes to die away once its cause is gone.</summary>
    [Export] public float RotateDecay { get; set; } = 0.6f;
    /// <summary>Assist multiplier with handbrake pulled: lets the tail swing for hairpins.</summary>
    [Export] public float HandbrakeAssist { get; set; } = 0.25f;
    [Export] public float BrakeForce { get; set; } = 22500f;
    [Export] public float BrakeBias { get; set; } = 0.70f;
    [Export] public float HandbrakeForce { get; set; } = 4000f;

    // The rally's fuel (FuelTank); Stakhanov laps and LAN races never use it.
    [ExportGroup("Fuel")]
    /// <summary>Litres in a full tank.</summary>
    [Export] public float TankLitres { get; set; } = 50f;
    /// <summary>Litres per second ticking over, whatever the pedal.</summary>
    [Export] public float FuelIdle { get; set; } = 0.004f;
    /// <summary>Litres per second more at full throttle at the redline (the kolkhoz tune drinks like a tractor up there). 1.6: a tank lasts 31 s flat out at the redline on petrol (2.22 until 2026-11-02: 22 s, too thirsty with the throttle down).</summary>
    [Export] public float FuelFull { get; set; } = 1.6f;
    /// <summary>Share of that at full throttle below the knee; above it the rest grows with the square of the way to the redline.</summary>
    [Export] public float FuelRevBase { get; set; } = 0.01f;
    [Export] public float FuelRevKnee { get; set; } = 4500f;

    // Stakhanov overdrive (350 % and 500 % power only). Each value is the multiplier at full
    // overdrive (500 %); CarSim.Overdrive blends linearly from 1 (at 200 % and below).
    [ExportGroup("Overdrive")]
    /// <summary>Final drive divided by this: compensates for the short rally box at 500 % power.</summary>
    [Export] public float OverdriveGearing { get; set; } = 2.56f;
    /// <summary>Longitudinal grip of the driven rear wheels, so the power reaches the road.</summary>
    [Export] public float OverdriveDriveGrip { get; set; } = 1.7f;
    /// <summary>Cornering grip of all wheels: a little more bite, still a sliding Pyatyorka.</summary>
    [Export] public float OverdriveLateralGrip { get; set; } = 1.25f;
    /// <summary>Braking grip of all wheels. Kept mild: more bite stops the fronts locking and the
    /// full brake force then tips the car onto its nose (a 20-30° stoppie at 380 km/h).</summary>
    [Export] public float OverdriveBrakeGrip { get; set; } = 1.15f;
}
