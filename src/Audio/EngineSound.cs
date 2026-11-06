using System;
using CCCPRacing.Car;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Audio;

/// <summary>
/// Procedural four-cylinder rasp with tyre squeal, impact clonks, dull suspension thumps (slab
/// joints, potholes) and exhaust backfire bangs on lift-off. Sliding and braking tyres sound of the
/// ground under them (Ground): rubber howl and the brake pads' screech on concrete, stones crunching
/// on gravel, a dull rasp on dirt, a squelch in mud, a swish on grass, a squeaky crunch on snow, a
/// thin hiss on ice, spray in water.
/// No samples: everything is synthesised into an AudioStreamGenerator.
/// </summary>
public partial class EngineSound : Node
{
    const float MixRate = 22050f;
    /// <summary>Output gain of the whole car group (engine, tyres, bangs...). Was -6 dB; +6 dB after
    /// play-testing found the car too quiet under the rest of the mix. The Master limiter catches peaks.</summary>
    public const float GainDb = 0f;

    public float Rpm = 1200f;
    public float Throttle;
    public float Slide;        // 0..1
    public bool Loose;         // loose ground (where Ground is not known: a gravel crunch instead of squeal)
    public bool Ice;           // on ice the tyres only hiss
    /// <summary>The ground under most of the wheels (null: from Loose and Ice).</summary>
    public SurfaceType? Ground;
    public float Scrape;       // body on ground 0..1
    public float Volume = 0.8f;
    /// <summary>The rally's fuel: 0 diesel (a duller, heavier voice with the knock of the combustion), 1 petrol (the voice as it has always been), 2 rocket fuel
    /// (a turbo that spools up with the throttle and the revs, whistles and blows off on the lift).</summary>
    public int Grade = 1;
    /// <summary>The SPUTNIK's inline five: 2.5 firings per revolution at uneven intervals and strengths (the exhaust pulses of the Quattro warble: a half-order beat under the note).</summary>
    public bool FiveCylinder;
    int _fire;
    static readonly float[] FiveInterval = { 1.07f, .93f, 1.05f, .95f, 1f }, FiveAmp = { 1f, .78f, 1.12f, .72f, .93f };
    float _fiveAmp = 1f;
    double _warble;
    /// <summary>The turbo whistle's spool-up time (s), the car's TurboSpool.</summary>
    public float TurboSpool = 0.5f;
    /// <summary>OCTANE BOOSTER running (rocket fuel too): a whomp as it lights, then the nitro's hiss and roar.</summary>
    public bool Boosted;
    // The fuels' voices: the diesel's knock, the turbo and the nitro.
    bool _blowArmed;
    float _blowAge;
    double _blowPhase, _blowTonePhase;
    float _knock, _tick, _tickLp, _knockLp, _boost, _blow, _blowLp, _nitro, _nitroLp, _whomp, _dieselLp, _prevThrottle;
    double _dieselKnockPhase, _whistlePhase, _whistle2Phase, _whompPhase;
    bool _wasBoosted;
    // Mix calibration, not driving state. Keep internal so offline auditions can use the old mix.
    internal float GearboxWhineLevel = 0.05f;
    // Internal A/B calibration; production uses the loaded four-cylinder/intake voice.
    internal float EngineCharacterMix = 1f;
    // The combustion/intake body leads the gearbox voice; leave exhaust/shift transients alone.
    internal float CombustionGain = 1.5f;
    internal float RallyRaspMix = 1f;
    internal float EngineAggressionMix = .5f;
    internal float ClosedThrottleRasp = 1f;
    public bool Limiter;       // bouncing off the rev limiter: stuttering "brrrap" with crackle
    public float BrakeSqueal;  // 0..1, metallic brake squeal while braking at speed
    /// <summary>Car speed (m/s) and gear: the tyres' rolling, the wind, the gearbox's whine and the stones off the road follow them.</summary>
    public float Speed;
    public float Enclosure;
    readonly float[] _hallEcho=new float[4096];
    int _hallCursor;
    float _hallMix,_hallJointPhase,_hallJointNoise;
    public int Gear = 1;
    /// <summary>1 when the engine drives the wheels (in gear, clutch up), 0 in neutral: the gearbox whines and the engine brakes only then.</summary>
    public float Driven = 1f;
    /// <summary>The wheels are off the ground: no rolling noise.</summary>
    public bool Airborne;
    /// <summary>Engine note level: 1 running, 0 seized (the other voices carry on).</summary>
    public float EngineLevel = 1f;
    /// <summary>Dying engine 0..1: firing strokes drop out at random and cough.</summary>
    public float Misfire;

    AudioStreamPlayer _player;
    AudioStreamGeneratorPlayback _pb;
    double _phase, _squealPhase;
    float _lp, _noiseLp, _impact, _impactLp;
    readonly CollisionVoice _collision = new(MixRate);
    float _collisionDuck;
    float _pedalAmp, _pedalLow, _gearMetal;
    double _gearMetalPhase;
    bool _driveKnown, _lastClutch;
    int _lastDriveGear;
    float _rpmSmooth = 1200f;
    // One-shot voices.
    float _thumpAmp, _thumpFreq = 90f, _thumpNoise;
    // A body struck by the bumper (a babushka, a drunk): a short dull tump of the bonnet and a soft rustle of cloth, nothing like a crash.
    float _bodyAmp, _bodyFreq = 150f, _bodyRustle, _bodyLp;
    double _bodyPhase;
    // A landing after a big jump: a deep boom of the whole car coming down, the body's crunch and a ring of loose metal.
    float _landAmp, _landFreq = 70f, _landCrunch, _landClank, _landLp;
    double _landPhase, _clankPhase;
    double _thumpPhase;
    float _bangCrack, _bangBody, _bangBoom, _bangLp;
    double _bangPhase;
    float _shiftCutLeft, _shiftGate = 1f;
    double _limPhase;
    // Brake screech: stick-slip saw + chatter gate + resonant noise band, then grit.
    double _sqPhase;
    float _sqAmp, _sqFreq = 1150f, _sqCycleJitter = 1f, _sqChatter, _sqChatterTimer, _sqGate;
    float _bpLow, _bpBand;       // state-variable band-pass for the noise band
    float _tyreLow, _tyreBand, _tyreJitter = 1f;
    // The ground's voice: grains (stones, snow crystals) through a band-pass, a rumble under them,
    // and a slow wobble (mud's squelch, grass blades).
    float _grain, _gLow, _gBand, _rumble, _hiss, _wob = 1f, _wobTarget = 1f, _wobTimer, _squelchFreq = 120f;
    double _squelchPhase;
    float _limGate = 1f, _crackle;
    // Rolling, overrun, whine, stones off the road, wind.
    double _pwmPhase, _whinePhase, _pingPhase;
    // The hard hit drops the engine and the tyres for a moment (_duck), a slide begins with a chirp (_chirp), a gear goes in with a clunk.
    float _duck, _chirp, _prevSlide, _clunk, _whineDip;
    double _clunkPhase;
    float _clunkLp;
    float _ovLp, _rollLp, _pingAmp, _pingFreq = 2400f, _windLp;
    float _voiceLoad, _intakeLow, _intakeBody;
    double _intakeCycle;
    float _rawEngineDc, _rawEngineTone;
    float _pressureRough, _pressurePrevious, _pressureDc, _pressureTone;
    readonly Random _rng = new(1917);

    public override void _ExitTree()
    {
        using var driverLock = AudioMix.LockDriver();
        using var generator = GodotObject.IsInstanceValid(_player) ? _player.Stream as AudioStreamGenerator : null;
        // The player/mixer also owns the generator playback. Release that ownership
        // before disposing our managed reference, including shutdown in a test's first frame.
        if (GodotObject.IsInstanceValid(_player))
        {
            _player.StreamPaused = false; _player.Stop();
            _player.Stream = null;
        }
        if (GodotObject.IsInstanceValid(_pb)) _pb.Stop(); _pb?.Dispose();
        _pb = null;
    }

    public override void _Ready()
    {
        var gen = new AudioStreamGenerator { MixRate = MixRate, BufferLength = 0.08f };
        _player = new AudioStreamPlayer { Stream = gen, VolumeDb = GainDb, Bus = AudioMix.BusFor(AudioMix.Engine) };
        AddChild(_player);
        _player.TreeExiting += _ExitTree;
        _player.Play();
        _pb = _player.GetStreamPlayback() as AudioStreamGeneratorPlayback;
    }

    /// <summary>Drop queued PCM on an isolated replay seek, then fill a fresh playback from the new pose.</summary>
    public void ResetPlaybackBuffer()
    {
        if(AudioMix.Closing || _player==null) return;
        using var driverLock = AudioMix.LockDriver();
        _player.StreamPaused = true;
        _pb.Stop(); _pb.ClearBuffer(); _pb.Start();
    }

    public void Impact(float strength)
    {
        _impact = Math.Max(_impact, Math.Clamp(strength / 10f, 0.2f, 1f));
        if (strength >= 3f) _duck = Math.Max(_duck, Math.Clamp(strength / 12f, 0.35f, 1f));
    }

    /// <summary>A body collision; speed is relative to the contact normal, not
    /// the speedometer. Ghost replays supply the same recorded impact speed.</summary>
    public void Collision(float normalSpeed, float massKg, ImpactMaterial material = ImpactMaterial.Unknown, bool broke = false)
    {
        var hit = CollisionResponse.Of(normalSpeed, massKg);
        if (_collision.Trigger(hit, material, broke)) _collisionDuck = Math.Max(_collisionDuck, hit.Duck);
    }

    public void ClearCollision() { _collision.Clear(); _collisionDuck = 0; }

    public void RestoreCollision(float speed, float mass, ImpactMaterial material, bool broke, float age)
    {
        var hit = CollisionResponse.Of(speed, mass);
        _collision.Restore(hit, material, broke, age);
        _collisionDuck = Math.Max(0, hit.Duck - age / .45f);
    }

    /// <summary>Edges of the actual drivetrain state, shared by live driving and
    /// Ghost playback. A baseline/reset never invents a pedal press or gear change.</summary>
    public void Drivetrain(int gear, bool clutch, bool neutral, float throttle, float rpm, bool audible)
    {
        if (!audible) { _driveKnown = false; return; }
        if (_driveKnown)
        {
            float load = Math.Clamp(throttle * rpm / 6000f, 0, 1);
            bool shifted = gear != _lastDriveGear && gear != 0 && _lastDriveGear != 0;
            if (shifted) GearChange(gear > _lastDriveGear, load);
            if (clutch != _lastClutch)
            {
                _pedalAmp = clutch ? .13f : .09f;
                // One engagement clonk: a simultaneous gear change already made it.
                if (!clutch && !neutral && gear != 0 && !shifted)
                {
                    _clunk = Math.Max(_clunk, .2f + .5f * load);
                    _clunkPhase = 0;
                    _gearMetal = .15f + .25f * load;
                    _gearMetalPhase = 0;
                }
            }
        }
        _driveKnown = true; _lastDriveGear = gear; _lastClutch = clutch;
    }

    public void ClearDrivetrain()
    {
        _driveKnown = false; _pedalAmp = _gearMetal = _clunk = 0;
        _shiftCutLeft = _whineDip = 0; _shiftGate = 1;
    }

    /// <summary>A shift of gears: the dog clutch clunks in, the torque is cut for a moment and the gearbox's whine dips.
    /// A downshift bangs in harder than an upshift.</summary>
    public void GearChange(bool up, float load = .5f)
    {
        _clunk = (up ? .65f : .8f) + .3f * Math.Clamp(load, 0, 1);
        _clunkPhase = 0;
        _gearMetal = (up ? .35f : .45f) + .25f * Math.Clamp(load, 0, 1);
        _gearMetalPhase = 0;
        _shiftCutLeft = Math.Max(_shiftCutLeft, up ? 0.05f : 0.035f);
        _whineDip = 1f;
    }

    /// <summary>Dull, low "tump" of the suspension hitting something (0..1).</summary>
    public void Thump(float strength)
    {
        _thumpAmp = Math.Max(_thumpAmp, Math.Clamp(strength, 0f, 1f));
        _thumpFreq = 85f + 25f * strength;
        _thumpPhase = 0;
        if (strength >= 0.6f) _duck = Math.Max(_duck, 0.4f * strength);
    }

    /// <summary>A person struck by the car (0..1 by the speed): a dull tump, about 70 ms, with a soft rustle; the engine ducks only a little.</summary>
    public void BodyHit(float strength)
    {
        float k = Math.Clamp(strength, 0f, 1f);
        _bodyAmp = Math.Max(_bodyAmp, 0.55f + 0.45f * k);
        _bodyFreq = 130f + 60f * k;
        _bodyPhase = 0;
        _bodyRustle = Math.Max(_bodyRustle, 0.4f + 0.4f * k);
        _duck = Math.Max(_duck, 0.25f + 0.2f * k);
    }

    /// <summary>The car comes down hard after a jump (0..1): a boom that falls from about 70 Hz to 28 Hz over half a second, a crunch of the bodywork and the springs, loose metal ringing; the engine and the tyres drop under it.</summary>
    public void Landing(float strength)
    {
        float k = Math.Clamp(strength, 0f, 1f);
        _landAmp = Math.Max(_landAmp, 0.45f + 0.55f * k);
        _landFreq = 64f + 22f * k;
        _landPhase = 0;
        _landCrunch = Math.Max(_landCrunch, 0.5f + 0.5f * k);
        _landClank = Math.Max(_landClank, 0.3f + 0.7f * k);
        _duck = Math.Max(_duck, 0.7f + 0.3f * k);
        _collisionDuck = Math.Max(_collisionDuck, 0.8f * k);
    }

    /// <summary>Exhaust backfire: sharp crack, a body of noise and a low boom (0..1).</summary>
    public void Bang(float strength)
    {
        float a = Math.Clamp(strength, 0.3f, 1f);
        _bangCrack = a;
        _bangBody = a;
        _bangBoom = a * .22f;
        _bangPhase = 0;
    }

    float _expl, _explLp; double _explPhase;

    /// <summary>The rocket fuel's turbo-lift detonation: a Bang with a big low thump and a fat burst of noise behind it, nearly an explosion (0..1).</summary>
    public void Explosion(float strength)
    {
        float a = Math.Clamp(strength, 0.4f, 1f);
        Bang(1f);
        _expl = a;
        _explPhase = 0;
    }

    public void ClearBackfire() { _bangCrack = _bangBody = _bangBoom = _bangLp = _expl = 0; }

    /// <summary>A single sharp flat-shift report over the gearbox's existing torque interruption.</summary>
    public void PowerShift(float strength)
    {
        _shiftCutLeft = 0.08f;
        Bang(strength);
        // Less low tail than the longer lift-off crackle: one crisp report per upshift.
        _bangBoom *= 0.45f;
    }

    /// <summary>
    /// Rotten fence giving way: a quick burst of dry snaps (splintering), a hollow knock of the
    /// post and a short clatter of boards (0..1).
    /// </summary>
    public void Crack(float strength)
    {
        _crackAmp = Math.Max(_crackAmp, Math.Clamp(strength, 0.3f, 1f));
        _crackSnaps = 4 + (int)(strength * 6);
        _crackNext = 0;
        _knockAmp = _crackAmp;
        _knockPhase = 0;
        _knockFreq = 190f + (float)_rng.NextDouble() * 60f;
    }

    /// <summary>Engine seizing: a metallic clatter of knocks that slows and dies away (~1.5 s).</summary>
    public void Seize()
    {
        _clatter = 1f;
        _clatterNext = 0;
        _clatterGap = 0.02f;
    }

    float _clatter, _clatterNext, _clatterGap, _clatterRing, _missGate = 1f;
    double _clatterPhase;
    bool _miss;

    float _crackAmp, _crackNext, _snapAmp, _snapBand, _snapLow, _knockAmp, _knockFreq;
    double _knockPhase;
    int _crackSnaps;

    public override void _Process(double delta)
    {
        if (_pb == null) return;
        int frames = _pb.GetFramesAvailable();
        if (frames <= 0) return;

        for (int i = 0; i < frames; i++)
        {
            float s = NextSample();
            _pb.PushFrame(new Vector2(s, s));
        }
    }

    /// <summary>How the ground under sliding or braking tyres sounds.</summary>
    public enum GroundSound { Hard, Gravel, Dirt, Mud, Grass, Snow, Ice, Water }

    public static GroundSound SoundOf(SurfaceType s) => s switch
    {
        SurfaceType.Concrete or SurfaceType.Runway or SurfaceType.Pothole => GroundSound.Hard,
        SurfaceType.Gravel => GroundSound.Gravel,
        SurfaceType.Dirt => GroundSound.Dirt,
        SurfaceType.Mud => GroundSound.Mud,
        SurfaceType.Grass or SurfaceType.Field or SurfaceType.DeadGrass => GroundSound.Grass,
        SurfaceType.Snow or SurfaceType.DeepSnow => GroundSound.Snow,
        SurfaceType.Ice => GroundSound.Ice,
        _ => GroundSound.Water,      // puddles, the river, the ford
    };

    /// <summary>The brake pads' screech, as loud as the ground lets it be heard: full on concrete, under
    /// the crunch and the squelch elsewhere.</summary>
    static float ScreechOn(GroundSound g) => g switch
    {
        GroundSound.Hard => 1f, GroundSound.Ice => 0.4f, GroundSound.Snow => 0.15f, GroundSound.Dirt => 0.15f,
        GroundSound.Grass => 0.15f, GroundSound.Gravel => 0.1f, _ => 0.05f,
    };

    float _groundAmp;

    /// <summary>A tyre sliding or braking on soft ground (amt 0..1), one sample.</summary>
    float GroundVoice(GroundSound g, float amt, float noise)
    {
        switch (g)
        {
            case GroundSound.Gravel:
            {
                // Stones: a rattle of short clicks, more of them the harder it slides, over a deep rumble.
                Grains(150f + 900f * amt, 0.993f);
                float clicks = Band(noise * _grain, 0.36f, 0.6f);             // ~1.3 kHz, wide
                _rumble += (noise - _rumble) * 0.05f;                         // ~180 Hz
                return (clicks * 1.4f + _rumble * 4.5f) * amt * 0.16f;
            }
            case GroundSound.Dirt:
            {
                // Packed dirt: a dull, dry rasp, few and soft grains.
                Grains(60f + 300f * amt, 0.985f);
                float grit = Band(noise * (0.35f + _grain), 0.17f, 0.7f);      // ~600 Hz
                _rumble += (noise - _rumble) * 0.08f;                          // ~280 Hz
                return (grit * 0.9f + _rumble * 3.5f) * amt * 0.15f;
            }
            case GroundSound.Mud:
            {
                // Mud: a wet squelch, gulps of low noise and a falling slurp, each 30-90 ms.
                Wobble(0.03f, 0.06f, true);
                _squelchFreq += (70f - _squelchFreq) * 0.0004f;
                _squelchPhase += _squelchFreq / MixRate;
                _rumble += (noise - _rumble) * 0.04f;                          // ~140 Hz
                float slurp = Band(noise, 0.1f, 0.15f);                           // ~350 Hz, resonant
                return (_rumble * 5f + slurp * 0.8f + (float)Math.Sin(_squelchPhase * Math.Tau) * 0.35f) * _wob * amt * 0.2f;
            }
            case GroundSound.Grass:
            {
                // Grass: a soft swish of the blades, fluttering.
                Wobble(0.04f, 0.1f, false);
                _hiss += (noise - _hiss) * 0.3f;
                float swish = Band(noise - _hiss, 0.9f, 0.7f);                    // ~3.5 kHz
                _rumble += (noise - _rumble) * 0.03f;
                return (swish * 0.8f + _rumble * 2.5f) * _wob * amt * 0.09f;
            }
            case GroundSound.Snow:
            {
                // Packed snow: a squeaky crunch, the crystals ringing in a narrow band.
                Grains(200f + 600f * amt, 0.99f);
                float squeak = Band(noise * _grain, 0.4f, 0.12f);                // ~1.4 kHz, resonant
                _rumble += (noise - _rumble) * 0.06f;
                return (MathF.Tanh(squeak * 2f) * 0.6f + _rumble * 3f) * amt * 0.14f;
            }
            case GroundSound.Ice:
            {
                // Ice: a thin, high hiss over a faint low grind, well under the engine.
                _hiss += (noise - _hiss) * 0.5f;
                _rumble += (noise - _rumble) * 0.03f;
                return ((noise - _hiss) * 0.05f + _rumble * 0.15f) * amt;
            }
            default:
            {
                // Water: spray hissing off the tyres, in gusts, over a low slosh.
                Wobble(0.02f, 0.05f, false);
                _hiss += (noise - _hiss) * 0.2f;
                _rumble += (noise - _rumble) * 0.02f;
                return ((noise - _hiss) * 0.9f * _wob + _rumble * 4f) * amt * 0.13f;
            }
        }
    }

    /// <summary>A grain envelope: new grains at about rate a second, each decaying by decay a sample.</summary>
    void Grains(float rate, float decay)
    {
        if (_rng.NextDouble() < rate / MixRate) _grain = Math.Max(_grain, 0.4f + 0.6f * (float)_rng.NextDouble());
        _grain *= decay;
    }

    /// <summary>A slow random swell: a new level every lo..hi seconds (a squelch also restarts its slurp).</summary>
    void Wobble(float lo, float hi, bool squelch)
    {
        _wobTimer -= 1f / MixRate;
        if (_wobTimer <= 0)
        {
            _wobTimer = lo + (hi - lo) * (float)_rng.NextDouble();
            _wobTarget = 0.2f + 0.8f * (float)_rng.NextDouble();
            if (squelch) _squelchFreq = 130f + 70f * (float)_rng.NextDouble();
        }
        _wob += (_wobTarget - _wob) * 0.004f;
    }

    /// <summary>The ground's band-pass (Chamberlin state-variable filter, fc 2 sin(pi f / rate), q 1/Q).</summary>
    float Band(float x, float fc, float q)
    {
        _gLow += fc * _gBand;
        _gBand += fc * (x - _gLow - q * _gBand);
        return _gBand;
    }

    /// <summary>The next sample of the car's sound (the tests render it offline, no audio server).</summary>
    public float NextSample()
    {
        float rpmTarget = Rpm;
        {
            _rpmSmooth += (rpmTarget - _rpmSmooth) * 0.0008f;
            // 4-stroke 4-cylinder: two firings per revolution.
            // Keep the old stepped voice available for auditions. The rally voice follows revs
            // continuously: four cylinders, four strokes, two firing pulses per revolution.
            float character = Math.Clamp(EngineCharacterMix, 0f, 1f);
            double f = Mathf.Lerp(MathF.Round(_rpmSmooth / 40f) * 40f, _rpmSmooth, character) / 60.0 * (FiveCylinder ? 2.5 / FiveInterval[_fire] : 2.0) * (Grade == 0 ? .78 : 1.0);      // (the diesel sounds a fourth lower at the same revs: a big slow engine)
            _phase += f / MixRate;
            if (_phase > 1)
            {
                _phase -= 1;
                if (Grade == 0) { _knock = 1f; _tick = 1f; }      // the diesel knocks at every firing
                if (FiveCylinder) _fire = (_fire + 1) % 5;
                // Misfire: this stroke fails to fire (and sometimes the next one pops).
                _miss = Misfire > 0 && _rng.NextDouble() < Misfire * 0.35;
            }
            _missGate += ((_miss ? 0.1f : 1f) - _missGate) * 0.05f;
            float saw = (float)(_phase * 2 - 1);
            // Pulse width modulation: thin and buzzy under load, fat and woolly on a closed throttle, with a slow wobble.
            _pwmPhase += 3.1 / MixRate;
            float width = 0.40f - 0.22f * Throttle + 0.03f * (float)Math.Sin(_pwmPhase * Math.Tau);
            float pulse = (_phase < width ? 1f : -0.2f) - (width - 0.2f * (1f - width));
            float noise = (float)_rng.NextDouble() * 2 - 1;
            // A worn Pyatyorka: uneven firing, loose exhaust.
            float raw = saw * 0.45f + pulse * (0.35f + 0.3f * Throttle) + noise * (0.08f + 0.18f * Throttle + 0.1f * Slide);
            _lp += (raw - _lp) * (0.18f + 0.25f * Throttle);
            float engine = _lp * (0.35f + 0.25f * Throttle);
            _voiceLoad += (Math.Clamp(Throttle, 0f, 1f) - _voiceLoad) * .0025f;
            // Firing harmonics retain a low body while the upper orders open with load/revs.
            // All orders stay well below Nyquist at the car's limiter; no hard-edged oscillator.
            float angle = (float)(_phase * Math.Tau);
            float brightness = Math.Clamp((_rpmSmooth - 2000f) / 4500f, 0f, 1f);
            float hs = Grade == 0 ? .3f : 1f, ras = Grade == 0 ? .4f : 1f;      // (the diesel's upper orders and its exhaust rasp, softer; petrol: 1, as it was)
            float firing = MathF.Cos(angle) * .55f + MathF.Cos(2 * angle) * .28f
                + MathF.Cos(3 * angle) * (.10f + .12f * _voiceLoad)
                + MathF.Cos(4 * angle) * (.035f + .085f * brightness * _voiceLoad) * hs
                + MathF.Cos(6 * angle) * .04f * brightness * _voiceLoad * hs;
            // Band-limited breath, pulsed by the intake cycle; it closes on lift-off instead of
            // adding a constant layer of white hiss. Reuse noise so other one-shot RNG is intact.
            _intakeLow += (noise - _intakeLow) * (.30f + .18f * brightness);
            _intakeBody += (_intakeLow - _intakeBody) * .055f;
            float intake = (_intakeLow - _intakeBody) * (.35f + .65f * Math.Max(0, MathF.Sin(angle)));
            float rallyEngine = firing * (.19f + .16f * _voiceLoad)
                + intake * _voiceLoad * (.28f + .12f * brightness)
                + _lp * (.055f + .025f * (1 - _voiceLoad));
            // Open-exhaust pressure pulses: a broad set of firing orders, rather than the
            // previous handful of soft harmonics. Recurrence avoids a trig call per order.
            // Smoothly roll off before Nyquist, including free revs outside the normal limiter.
            float c = MathF.Cos(angle), s = MathF.Sin(angle), cn = c, sn = s;
            float weight = 1, exhaustPulse = 0;
            // An open rally exhaust keeps a rough pressure/intake note off the pedal.
            // The extra character fades with load; the full-throttle voice is unchanged.
            float closedRasp = Math.Clamp(ClosedThrottleRasp, 0f, 1f) * (1 - _voiceLoad) * (1 - _voiceLoad);
            float breathLoad = _voiceLoad + .22f * closedRasp;
            float orderDecay = .78f + .11f * _voiceLoad + .045f * closedRasp;
            for (int order = 1; order <= 18; order++)
            {
                float edge = Math.Clamp((MixRate * .45f - (float)f * order) / (MixRate * .12f), 0, 1);
                exhaustPulse += (cn - .22f * sn) * weight * edge;
                float nextCos = cn * c - sn * s;
                sn = sn * c + cn * s; cn = nextCos;
                weight *= orderDecay;
            }
            exhaustPulse *= .055f + .025f * _voiceLoad;
            // A complete 720-degree cycle gives the four intake strokes small unequal weights.
            // Tonal suction pulses supply the throaty burble; filtered air alone was only hiss.
            _intakeCycle += f / (4 * MixRate);
            _intakeCycle -= Math.Floor(_intakeCycle);
            float cycle = (float)(_intakeCycle * Math.Tau);
            // A cammed four-cylinder at idle: four unequal combustion strokes over
            // 720 degrees, with a slow intake/exhaust lope. Fade out before driving
            // revs so the agreed full-load engine character stays intact.
            float idle = Math.Clamp((1750f - _rpmSmooth) / 400f, 0f, 1f)
                * (1 - Math.Clamp(_voiceLoad * 8, 0f, 1f));
            float uneven = 1 + .10f * MathF.Sin(cycle) + .06f * MathF.Sin(2 * cycle + .8f);
            float suction = .56f * s + .27f * MathF.Sin(2 * angle - .6f)
                + .13f * MathF.Sin(3 * angle - 1.1f);
            float throat = (suction * .085f + MathF.Sin(cycle) * .035f + intake * .18f)
                * breathLoad * uneven;
            float rawRallyEngine = .28f * rallyEngine + .74f * exhaustPulse + throat;
            // Load turns the smooth periodic note into uneven, pressure-driven bark. Reusing
            // noise leaves the random sequence and timing of all other sound events intact.
            _pressureRough += (noise - _pressureRough) * .018f;
            float body = (MathF.Sin(2 * cycle) + .55f * MathF.Sin(cycle)) * .055f * breathLoad;
            float grit = (_intakeLow - _intakeBody) * breathLoad * .32f * (.3f + .7f * Math.Max(0, s));
            float pressure = rawRallyEngine * (1 + breathLoad * (.18f * MathF.Sin(cycle) + .7f * _pressureRough))
                + body + grit;
            float pressureDrive = 1.6f + 4.8f * breathLoad;
            // Two interpolated saturation samples temper the hard edge. This shapes only the
            // engine voice, never the final mix or the separate exhaust-bang voices.
            float bark = (Math.Clamp((pressure + _pressurePrevious) * .5f * pressureDrive, -.55f, .72f)
                + Math.Clamp(pressure * pressureDrive, -.55f, .72f)) * .24f;
            _pressurePrevious = pressure;
            _pressureDc += (bark - _pressureDc) * .0015f;
            _pressureTone += (bark - _pressureDc - _pressureTone) * .72f;
            // Round the individual pressure peaks, filling the sustained growl between them.
            // This is engine timbre, before mixing: whine, bangs and road audio are untouched.
            rawRallyEngine = .42f * MathF.Tanh(rawRallyEngine * (2.2f + 1.8f * breathLoad));
            _rawEngineDc += (rawRallyEngine - _rawEngineDc) * .0015f;
            _rawEngineTone += (rawRallyEngine - _rawEngineDc - _rawEngineTone) * .6f;
            rawRallyEngine = Mathf.Lerp(_rawEngineTone, _pressureTone,
                Math.Clamp(EngineAggressionMix, 0, 1) * ras * (.3f + .7f * breathLoad));
            // Match the sustained body of the preceding mix after rounding the pressure peaks.
            // Reserve the calibration boost for driving, yielding while the strong impact
            // occupies the mix. The existing duck restores it smoothly as the hit settles.
            rawRallyEngine *= 1f + .2f * (1f - Math.Clamp(_collisionDuck * 4, 0f, 1f));
            rallyEngine = Mathf.Lerp(rallyEngine, rawRallyEngine, Math.Clamp(RallyRaspMix, 0, 1) * ras);
            engine = Mathf.Lerp(engine, rallyEngine, character);
            float idleLope = 1 + .26f * MathF.Sin(cycle + .4f) + .16f * MathF.Sin(2 * cycle - .7f)
                + .10f * MathF.Sin((float)(_pwmPhase * Math.Tau));
            float idleBurble = (.055f * MathF.Sin(cycle) + .035f * MathF.Sin(3 * cycle + .6f)
                + .025f * intake) * idle;
            engine = engine * (1 + idle * (idleLope - 1)) + idleBurble;
            // Yield the extra engine gain during the existing collision duck so the impact
            // retains headroom. Normal driving keeps the full engine/gearbox balance.
            float combustionGain = 1f + (CombustionGain - 1f) * (1f - Math.Clamp(_collisionDuck, 0f, 1f));
            engine *= combustionGain;
            if (FiveCylinder)
            {
                _fiveAmp += (FiveAmp[_fire] - _fiveAmp) * .004f;      // (each firing's strength, eased over a few ms)
                engine *= _fiveAmp;
                _warble = (_warble + f / 5.0 / MixRate) % 1.0;
                engine += MathF.Sin((float)(_warble * Math.Tau)) * (.04f + .05f * _voiceLoad);      // the half-order warble under the note
            }
            // The diesel: a slow, heavy engine. A dull knock and a sharp tick at every firing (the knock is loudest at idle and light throttle and still there flat out),
            // a rumble of the half-order under it, the top rolled off and the whole thickened by a soft clip.
            if (Grade == 0)
            {
                _knockLp += (noise - _knockLp) * .22f;
                _dieselKnockPhase += (140.0 + 60.0 * _voiceLoad) / MixRate;
                _tickLp += (noise - _tickLp) * .5f;
                float knockAmp = _knock * (1f - .4f * _voiceLoad) * Math.Clamp(1.4f - (_rpmSmooth - 1200f) / 5000f, .3f, 1f);
                engine += ((float)Math.Sin(_dieselKnockPhase * Math.Tau) * .7f + _knockLp * 1.2f) * knockAmp * .8f;      // the dull knock
                engine += (noise - _tickLp) * _tick * (.35f - .12f * _voiceLoad);      // the tick
                engine += MathF.Sin(angle * .5f) * (.05f + .05f * _voiceLoad);      // the rumble
                _knock *= .9965f;      // ~12 ms
                _tick *= .986f;      // ~3 ms
                _dieselLp += (engine - _dieselLp) * .38f;
                engine = Mathf.Lerp(engine, _dieselLp, .75f);
                engine = MathF.Tanh(engine * 1.5f) * .75f;
            }
            // Rocket fuel: the turbo spools up with the throttle and the revs (about half a second), whistles higher the more boost, blows off when the throttle lifts;
            // and the OCTANE BOOSTER's nitro: a whomp as it lights, then a hiss over a deeper roar.
            float turbo = 0, nitro = 0;
            if (Grade == 2)
            {
                float want = Math.Clamp(Throttle, 0f, 1f) * Math.Clamp((_rpmSmooth - 2500f) / 3500f, 0f, 1f);
                _boost += (want - _boost) * (want > _boost ? 1f / (TurboSpool * MixRate) : 1f / (0.18f * MixRate));
                // The lift-off: the pedal was down (armed above 0.6) and is now nearly up (below 0.3), however fast or in however many steps, with some boost on.
                if (Throttle > .6f) _blowArmed = true;
                else if (Throttle < .3f && _blowArmed)
                {
                    _blowArmed = false;
                    if (_boost > .3f) { _blow = Math.Max(_blow, .5f + .5f * _boost); _blowAge = 0; _blowPhase = 0; _blowTonePhase = 0; }
                }
                _prevThrottle = Throttle;
                float b2 = _boost * _boost;
                _whistlePhase += (1800.0 + 2800.0 * _boost) / MixRate;
                _whistle2Phase += (3650.0 + 5600.0 * _boost) / MixRate;
                _blowLp += (noise - _blowLp) * .35f;
                turbo = ((float)Math.Sin(_whistlePhase * Math.Tau) + .25f * (float)Math.Sin(_whistle2Phase * Math.Tau)) * b2 * .14f
                    + (noise - _blowLp) * b2 * .06f;
                if (_blow > .001f)
                {
                    // The blow-off: a rush of air that flutters, "fh-ty-ty-ty-ty" (the compressor surging against the closed throttle) and settles into a hiss, with a falling whistle in it.
                    _blowAge += 1f / MixRate;
                    float settle = Math.Clamp(1f - _blowAge / .9f, 0f, 1f);
                    _blowPhase += (24.0 - 10.0 * (1f - settle)) / MixRate;
                    float gate = Mathf.Lerp(1f, (_blowPhase % 1.0) < .5 ? 1f : .15f, settle);
                    _blowTonePhase += (3200.0 - 1700.0 * Math.Min(1f, _blowAge / .8f)) / MixRate;
                    turbo += ((noise - _blowLp) * .38f + (float)Math.Sin(_blowTonePhase * Math.Tau) * .07f) * _blow * gate;
                }
                _blow *= .99994f;      // the blow-off: ~0.75 s
            }
            if (Boosted && !_wasBoosted) { _whomp = 1f; _whompPhase = 0; }
            _wasBoosted = Boosted;
            _nitro += ((Boosted ? 1f : 0f) - _nitro) * .0004f;
            if (_nitro > .001f || _whomp > .001f)
            {
                _nitroLp += (noise - _nitroLp) * .08f;
                _whompPhase += (62.0 - 28.0 * (1.0 - _whomp)) / MixRate;
                nitro = (noise - _nitroLp) * .13f * _nitro + _nitroLp * .26f * _nitro * (.8f + .2f * MathF.Sin(angle))
                    + ((float)Math.Sin(_whompPhase * Math.Tau) * 1.1f + _nitroLp * .7f) * _whomp;
                _whomp *= .99988f;      // ~0.4 s
            }

            // Rev limiter: ignition cut ~24 times a second, unburnt fuel crackling in the pipe.
            float limiter = 0;
            if (Limiter)
            {
                _limPhase += 24.0 / MixRate;
                if (_limPhase > 1) _limPhase -= 1;
                bool fired = _limPhase < 0.55;
                _limGate += ((fired ? 1.15f : 0.12f) - _limGate) * 0.02f;
                if (!fired && _rng.Next(900) == 0) _crackle = 1f;
            }
            else _limGate += (1f - _limGate) * 0.01f;
            _shiftCutLeft = Math.Max(0, _shiftCutLeft - 1f / MixRate);
            _shiftGate += ((_shiftCutLeft > 0 ? 0.06f : 1f) - _shiftGate) * 0.015f;
            engine *= _limGate * _missGate * EngineLevel * _shiftGate;
            if (_crackle > 0.001f)
            {
                limiter = noise * _crackle * 0.7f;
                _crackle *= 0.993f;
            }
            limiter *= _shiftGate;

            // Engine braking: foot off at speed, the engine drags the car: a dull, lumpy burble and now and then a pop.
            float overrunAmt = (1f - Throttle) * Math.Clamp((_rpmSmooth - 1800f) / 3000f, 0f, 1f) * Math.Clamp(Speed / 4f, 0f, 1f) * Driven;
            _ovLp += (noise - _ovLp) * 0.06f;
            float overrun = _ovLp * overrunAmt * (_phase < 0.5 ? 1f : 0.35f) * 1.0f * EngineLevel;
            float rallyOverrun = (_ovLp * (.45f + .55f * Math.Max(0, MathF.Sin(angle))) * .55f
                + MathF.Sin(angle) * .035f) * overrunAmt * EngineLevel;
            overrun = Mathf.Lerp(overrun, rallyOverrun, character);
            overrun *= combustionGain;
            if (overrunAmt > 0.3f && _rng.Next(5000) == 0) _crackle = Math.Max(_crackle, 0.45f * overrunAmt);

            // The gearbox: a thin whine that rises with the revs and the gear (a Lada's gearbox sings), steps of 4 bits.
            float whine = 0;
            _whineDip = Math.Max(0f, _whineDip - 1f / (0.12f * MixRate));
            float whineAmp = Driven * Math.Clamp(Speed / 6f, 0f, 1f) * EngineLevel * GearboxWhineLevel * (0.6f + 0.8f * overrunAmt) * (1f - 0.8f * _whineDip);
            if (whineAmp > 0.0005f)
            {
                _whinePhase += _rpmSmooth / 60.0 * (8 + 2 * Math.Abs(Gear)) / MixRate;
                whine = MathF.Round((float)Math.Sin(_whinePhase * Math.Tau) * 6f) / 6f * whineAmp;
            }

            // The tyres on the ground: a slide (a locked wheel is one), and on soft ground the
            // braking alone (the stones crunch under a braking tyre before it locks).
            var ground = SoundOf(Ground ?? (Ice ? SurfaceType.Ice : Loose ? SurfaceType.Gravel : SurfaceType.Concrete));
            // A slide starting is a bite: the rubber lets go with a chirp that fades in about a tenth of a second.
            if (Slide > 0.35f && _prevSlide <= 0.35f) _chirp = 1f;
            _prevSlide = Slide;
            _chirp *= 0.9996f;
            float slideAmt = Slide > 0.05f ? Math.Min(1f, Slide * 1.25f) * (1f + 0.5f * _chirp) : 0f;
            // Rolling: even without a slide the tyres hum on the road and rattle on stones, more the faster it goes.
            float roll = Airborne ? 0f : Math.Clamp(Speed / 35f, 0f, 1f);
            float groundAmt = ground == GroundSound.Hard ? slideAmt : Math.Max(Math.Max(slideAmt, BrakeSqueal * 0.6f), roll * roll * 0.3f);
            _groundAmp += (groundAmt - _groundAmp) * 0.004f;            // ~10 ms: no clicks as it starts and stops
            float tyre = 0;
            if (ground == GroundSound.Hard)
            {
                if (_groundAmp > 0.003f)
                {
                    // Rubber on concrete: a wavering howl with a rough noise band on top.
                    if (_rng.Next(300) == 0) _tyreJitter = 0.94f + (float)_rng.NextDouble() * 0.12f;
                    _squealPhase += (820 + 60 * Math.Sin(_phase * 6.28) + 260 * Slide + 380 * _chirp) * _tyreJitter / MixRate;
                    const float tfc = 0.28f, tq = 0.3f;              // band around ~1 kHz
                    _tyreLow += tfc * _tyreBand;
                    _tyreBand += tfc * (noise - _tyreLow - tq * _tyreBand);
                    float howl = (float)Math.Sin(_squealPhase * Math.Tau) * 0.55f + _tyreBand * 0.6f;
                    tyre = MathF.Tanh(howl * (1.8f + 2.2f * Slide)) * _groundAmp * (0.12f + 0.09f * Slide);
                }
            }
            else if (_groundAmp > 0.003f) tyre = GroundVoice(ground, _groundAmp, noise);

            _rollLp += (noise - _rollLp) * 0.09f;
            float hum = ground == GroundSound.Hard || ground == GroundSound.Ice ? _rollLp * roll * roll * 0.6f : 0f;

            // Stones thrown up against the underbody and the arches: sharp little pings.
            if (!Airborne && Speed > 6f && ground is GroundSound.Gravel or GroundSound.Dirt)
            {
                float rate = Speed / 30f * (5f + 40f * Slide) * (ground == GroundSound.Gravel ? 1f : 0.35f);
                if (_rng.NextDouble() < rate / MixRate) { _pingAmp = 0.3f + 0.7f * (float)_rng.NextDouble(); _pingFreq = 1700f + 1800f * (float)_rng.NextDouble(); }
            }
            float ping = 0;
            if (_pingAmp > 0.002f)
            {
                _pingPhase += _pingFreq / MixRate;
                ping = (float)Math.Sin(_pingPhase * Math.Tau) * _pingAmp * 0.2f;
                _pingAmp *= 0.9965f;                 // ~13 ms
            }

            // Wind round the Lada's flat windscreen above ~80 km/h.
            float wk = Math.Clamp((Speed - 22f) / 25f, 0f, 1f);
            _windLp += (noise - _windLp) * 0.2f;
            float wind = wk > 0 ? (noise - _windLp) * wk * wk * 0.07f : 0f;

            float scrape = Scrape > 0.05f ? noise * Scrape * 0.25f : 0;

            float clonk = 0;
            if (_impact > 0.001f)
            {
                _impactLp += (noise - _impactLp) * 0.08f;
                clonk = _impactLp * _impact * 1.25f;
                _impact *= 0.9993f;
            }

            float thump = 0;
            if (_thumpAmp > 0.001f)
            {
                // Falling-pitch sine through a soft body: felt more than heard.
                _thumpFreq += (42f - _thumpFreq) * 0.0006f;
                _thumpPhase += _thumpFreq / MixRate;
                _thumpNoise += (noise - _thumpNoise) * 0.02f;
                thump = ((float)Math.Sin(_thumpPhase * Math.Tau) + _thumpNoise * 0.6f) * _thumpAmp * 0.45f;
                _thumpAmp *= 0.99955f;           // ~100 ms
            }

            float bodyHit = 0;
            if (_bodyAmp > 0.001f || _bodyRustle > 0.001f)
            {
                _bodyFreq += (62f - _bodyFreq) * 0.0016f;
                _bodyPhase += _bodyFreq / MixRate;
                _bodyLp += (noise - _bodyLp) * 0.18f;
                bodyHit = (float)Math.Sin(_bodyPhase * Math.Tau) * _bodyAmp * 0.85f + _bodyLp * _bodyRustle * 1.1f;
                _bodyAmp *= 0.99935f;          // ~70 ms
                _bodyRustle *= 0.9985f;        // ~30 ms
            }

            float landing = 0;
            if (_landAmp > 0.001f || _landCrunch > 0.001f || _landClank > 0.001f)
            {
                _landFreq += (28f - _landFreq) * 0.00028f;
                _landPhase += _landFreq / MixRate;
                _landLp += (noise - _landLp) * 0.1f;
                _clankPhase += 215.0 / MixRate;
                float boom = MathF.Tanh((float)Math.Sin(_landPhase * Math.Tau) * 2.4f) * _landAmp * 0.95f;
                float crunch = _landLp * _landCrunch * 1.9f;
                float clank = ((float)Math.Sin(_clankPhase * Math.Tau) * 0.55f + (float)Math.Sin(_clankPhase * 1.52 * Math.Tau) * 0.35f + noise * 0.35f) * _landClank * 0.45f;
                landing = boom + crunch + clank;
                _landAmp *= 0.99989f;          // the boom: ~0.4 s
                _landCrunch *= 0.9993f;        // the crunch: ~65 ms
                _landClank *= 0.99965f;        // the rattle: ~130 ms
            }

            float bang = 0;
            if (_bangBody > 0.001f || _bangBoom > 0.001f)
            {
                _bangLp += (noise - _bangLp) * 0.25f;
                _bangPhase += 110.0 / MixRate;
                bang = noise * _bangCrack * 0.9f                     // crack, ~9 ms
                     + _bangLp * _bangBody * 1.6f                     // short pop body, ~20 ms
                     + (float)Math.Sin(_bangPhase * Math.Tau) * _bangBoom * 0.9f; // light tail, ~38 ms
                _bangCrack *= 0.995f;
                _bangBody *= 0.9978f;
                _bangBoom *= 0.9988f;
            }

            if (_expl > 0.001f)
            {
                _explLp += (noise - _explLp) * 0.08f;
                _explPhase += (48.0 + 40.0 * _expl) / MixRate;
                bang += (_explLp * 3.2f + noise * .25f * _expl + (float)Math.Sin(_explPhase * Math.Tau) * 1.1f) * _expl;
                _expl *= 0.99985f;      // ~0.3 s
            }

            // Brake screech: pads stick and slip on the disc. A buzzy sawtooth whose every cycle
            // is a little off pitch, chopped by an irregular 30-70 Hz chatter, plus a resonant band
            // of grinding noise, all pushed into soft clipping for a rasping, grating squeal.
            float squeal = 0;
            _sqAmp += (BrakeSqueal * ScreechOn(ground) - _sqAmp) * 0.0015f;
            if (_sqAmp > 0.003f)
            {
                _sqPhase += _sqFreq * _sqCycleJitter / MixRate;
                if (_sqPhase >= 1.0)
                {
                    _sqPhase -= 1.0;
                    _sqCycleJitter = 0.9f + (float)_rng.NextDouble() * 0.2f;           // ±10 % per cycle
                    _sqFreq += ((1000f + 400f * _sqAmp) - _sqFreq) * 0.02f + ((float)_rng.NextDouble() - 0.5f) * 30f;
                }
                float sqSaw = (float)(_sqPhase * 2.0 - 1.0);

                // Stick-slip chatter: gate flips at an irregular 30-70 Hz rate.
                _sqChatterTimer -= 1f / MixRate;
                if (_sqChatterTimer <= 0)
                {
                    _sqChatter = _sqChatter > 0.5f ? 0.25f + (float)_rng.NextDouble() * 0.2f : 1f;
                    _sqChatterTimer = 1f / (30f + (float)_rng.NextDouble() * 40f) * 0.5f;
                }
                _sqGate += (_sqChatter - _sqGate) * 0.05f;

                // Grinding noise band around 2.2 kHz (Chamberlin state-variable filter, Q ≈ 4).
                const float fc = 2.0f * 0.30f;   // 2 sin(pi * 2200 / 22050) ≈ 0.6
                const float q = 0.25f;
                _bpLow += fc * _bpBand;
                float high = noise - _bpLow - q * _bpBand;
                _bpBand += fc * high;
                float grind = _bpBand * 0.5f;

                float sqRaw = (sqSaw * 0.8f + grind * 0.9f) * _sqGate;
                float gritty = MathF.Tanh(sqRaw * 2.5f);                               // rasp
                squeal = gritty * _sqAmp * 0.2f;
            }

            float wood = 0;
            if (_crackAmp > 0.001f)
            {
                // Snaps come irregularly, 5-40 ms apart, each a click of band-passed noise.
                _crackNext -= 1f / MixRate;
                if (_crackNext <= 0 && _crackSnaps > 0)
                {
                    _crackSnaps--;
                    _snapAmp = _crackAmp * (0.6f + 0.4f * (float)_rng.NextDouble());
                    _crackNext = 0.005f + (float)_rng.NextDouble() * 0.035f;
                }
                const float wfc = 0.55f, wq = 0.35f;           // band around ~2 kHz
                _snapLow += wfc * _snapBand;
                _snapBand += wfc * (noise - _snapLow - wq * _snapBand);
                _knockPhase += _knockFreq / MixRate;
                wood = _snapBand * _snapAmp * 1.1f
                     + (float)Math.Sin(_knockPhase * Math.Tau) * _knockAmp * 0.7f;
                _snapAmp *= 0.996f;                             // ~6 ms click
                _knockAmp *= 0.9992f;                           // ~30 ms hollow knock
                if (_crackSnaps == 0 && _snapAmp < 0.001f) _crackAmp *= 0.99f;
            }

            float clatter = 0;
            if (_clatter > 0.001f)
            {
                // Rods knocking: sharp metallic pings, further apart as the crank stops.
                _clatterNext -= 1f / MixRate;
                if (_clatterNext <= 0)
                {
                    _clatterRing = _clatter * (0.6f + 0.4f * (float)_rng.NextDouble());
                    _clatterGap *= 1.12f;
                    _clatterNext = _clatterGap * (0.7f + 0.6f * (float)_rng.NextDouble());
                }
                _clatterPhase += (1350.0 + 400.0 * _clatterRing) / MixRate;
                clatter = ((float)Math.Sin(_clatterPhase * Math.Tau) * 0.7f + noise * 0.5f) * _clatterRing * 0.8f;
                _clatterRing *= 0.9975f;                        // ~20 ms ping
                _clatter *= 0.99985f;                           // dies away over ~1.5 s
            }

            float clunk = 0;
            if (_clunk > 0.001f)
            {
                _clunkLp += (noise - _clunkLp) * 0.12f;
                _clunkPhase += (150.0 - 60.0 * Math.Min(1.0, _clunkPhase * 22.0)) / MixRate;
                clunk = (_clunkLp * 2.4f + (float)Math.Sin(_clunkPhase * Math.Tau) * 0.5f) * _clunk * 0.5f;
                _clunk *= 0.9994f;                  // ~75 ms
            }

            // A hard hit drops the engine and the tyres under it for a third of a second, so the bang is heard.
            float duck = 1f - 0.6f * Math.Max(_duck, _collisionDuck);
            _duck = Math.Max(0f, _duck - 1f / (0.3f * MixRate));
            _collisionDuck = Math.Max(0f, _collisionDuck - 1f / (.45f * MixRate));
            _pedalLow += (noise - _pedalLow) * .35f;
            float pedal = (noise - _pedalLow) * _pedalAmp;
            _pedalAmp *= .994f; // short, quiet linkage click
            _gearMetalPhase += 920.0 / MixRate;
            float gearMetal = (MathF.Sin((float)_gearMetalPhase * MathF.Tau) * .28f + noise * .3f) * _gearMetal;
            _gearMetal *= .998f;
            // The mixer (SoundBalance): the machinery and the road are two levels of their own.
            float machinery = (engine + limiter + overrun + whine + squeal) * duck + bang + clatter + clunk + pedal + gearMetal + turbo + nitro;
            float road = (hum + wind + tyre) * duck + ping + scrape + clonk + thump + landing + bodyHit + wood + _collision.Sample();
            _hallMix+=(Math.Clamp(Enclosure,0,1)-_hallMix)*.0008f;
            _hallJointPhase=(_hallJointPhase+Math.Abs(Speed)/(4*MixRate))%1;
            _hallJointNoise+=(noise-_hallJointNoise)*.06f;
            float joint=_hallJointNoise*MathF.Exp(-_hallJointPhase*35)*Math.Clamp(Speed/16,0,1)*.18f;
            float dry=(machinery*SoundBalance.EngineGain+(road+joint*_hallMix)*SoundBalance.RoadGain)*Volume;
            float echo=_hallEcho[(_hallCursor+4096-1000)%4096]*.12f+_hallEcho[(_hallCursor+4096-2200)%4096]*.08f;
            _hallEcho[_hallCursor]=dry;_hallCursor=(_hallCursor+1)%4096;
            return Math.Clamp(dry+echo*_hallMix,-1f,1f);
        }
    }
}
