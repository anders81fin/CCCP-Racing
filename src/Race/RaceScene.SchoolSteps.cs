using System;
using System.Collections.Generic;
using CCCPRacing.Car;
using CCCPRacing.Track;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// The driving school's lessons in the world: each step's demo (beats) and try. Places on the
/// bonus road: the runway by the start line (cans, the clutch, the handbrake), the depot, the
/// mechanic's bus stop, the nearest stone wall, the sharp corner at 1020 m onto the runway (braking),
/// the mud from 362 m and the straight from 880 m (the bus). Every step starts from a fresh car (ScFresh).
/// </summary>
public partial class RaceScene
{
    const int BrakeLength = 40;
    const float StompPoint = -12f, PumpPoint = -16f, PumpUntil = 38f;   // tuned in the SCHOOLLAB: locked goes ~4 m off, pumped stays on
    bool OnIlyich => _trackId == "ilyich";
    // The braking bend: Red Dawn's corner at 1020 m onto the runway; Ilyich's Lamp's S-bends (ILYICHLAB:
    // at 75 km/h locked brakes go 8 m off, pumping stays on).
    int BrakeEntry => OnIlyich ? 510 : 1008;
    float BrakeKmh => OnIlyich ? 75f : 85f;
    /// <summary>The brake try starts this far before the bend: on Ilyich on the straight after the mud hairpin (it ends near 455 m).</summary>
    int BrakeBack => OnIlyich ? 38 : 75;
    /// <summary>The player's brake try starts further back, time to settle before the TAP bars: on Ilyich in the mud hairpin's gentle tail (450 m), 44 m to the TAP bars (22 m from the demo's start).</summary>
    int BrakeTryBack => OnIlyich ? 60 : BrakeBack;

    // The steering lesson (Ilyich): the same keys up, across and down the screen. (from, to road metres, km/h)
    static readonly (int from, int to, float kmh, string caption)[] SteerDemos =
    {
        (478, 565, 40f, "DRIVING UP: LEFT TURNS LEFT."),           // north, then the left-hand bend to the west
        (790, 838, 30f, "DRIVING RIGHT: LEFT GOES UP."),          // east, then the church hairpin to the left (north, then west)
        (622, 700, 40f, "DRIVING DOWN: RIGHT GOES LEFT!"),         // south, towards the camera, then the right-hand bend to the west
    };
    const int SteerTryFrom = 618, SteerTryEntry = 630, SteerTryLength = 65;

    Beat SteerDemo((int from, int to, float kmh, string caption) d) =>
        B(d.caption, () => { ScFresh(); ScRoll(d.from, 0, d.kmh); }, (t, _) =>
        {
            // Like the keys: full lock or nothing (the keys lit on the screen are the ones a player would press).
            float s = SchoolScript.SteerRoad(_car, _track);
            ScCruise(d.kmh, MathF.Abs(s) > 0.12f ? MathF.Sign(s) : 0f);
            if (SchoolDbg && (int)(t * 2) != (int)((t - 1f / 60f) * 2))
            {
                int i = CarIndex;
                GD.Print($"school: steer demo t {t:F1} metre {i} steer {_car.SteerIn:F0} {Kmh:F0} km/h, {CarXZ.DistanceTo(_track.Center[i]) - _track.HalfWidth[i]:F1} m over the edge");
            }
            return SchoolScript.Along(_track, d.from, CarIndex) >= d.to - d.from || t > 15f;
        });
    // Ilyich's Lamp's slide lessons (ILYICHLAB, the slide steering): the clutch kick into the S-bends
    // at 70 km/h, the slide in the middle of the road (the kick at the entry, the aim 3 m out: it drifts in);
    // the flick into the same bend at 70 km/h; the handbrake round the church hairpin (804 m) at 50 km/h.

    /// <summary>Where a slide lesson's demo and try start: on Ilyich on the straight before the bend (50 m back
    /// would be in the bend before it, the car already sliding), elsewhere 50 m back.</summary>
    int TryBack(string tech) => !OnIlyich ? 50 : tech switch { "kick" or "flick" => KickEntry - 455, _ => 50 };
    float TryKmh(string tech, float kmh) => !OnIlyich ? kmh : tech switch { "kick" or "flick" => KickTryKmh, _ => kmh };
    const float KickTryKmh = 60f;    // from 455 m (the end of the mud hairpin): some 40 m to the CLUTCH IN strip (the flick starts there too)
    static int KickPoint => EnvI("KP", 0);
    static float KickOutward => EnvF("KO", 3f);   // metres outside the centre line the kick's slide aims (it drifts in)   // road metres from the entry where the clutch goes in
    static int KickLength => KickPoint + 58;    // the kick's bend, its first half (to ~546 m): the slide held all through it
    const float KickSlip = 22f;   // the slide's angle the demo holds (the try asks 10-50 degrees, half the bend)
    const int KickEntry = 500, FlickEntry = KickEntry, HairpinEntryAt = 804, SlideLength = 60;
    const float KickKmh = 70f, HairpinKmh = 50f;
    static float FlickKmh => EnvF("FKMH", 70f);
    static int HairpinEntry => EnvI("HE", HairpinEntryAt);
    int _scRoad;              // the slide demos' road metre, followed (SchoolScript.SlideSteer)

    float SlideSteerNow => SchoolScript.SlideSteer(_car, _track, ref _scRoad);

    // The road markings of the slide lessons (SchoolMarks): each part of the technique, road metres from the bend's entry.
    SchoolMarks _scMarkNode;
    readonly List<(int from, int to, string text, Color col, bool striped, int side)> _scMarks = new();
    int _scMarkEntry;
    static readonly Color MarkBlue = new(0.25f, 0.55f, 1f), MarkGreen = new(0.3f, 0.95f, 0.35f), MarkRed = new(1f, 0.3f, 0.2f), MarkYellow = new(1f, 0.85f, 0.2f), MarkWhite = new(0.92f, 0.92f, 0.88f);

    /// <summary>Paints where each part of the technique is done, as the demo does it (SlideDrive's points and holds).</summary>
    /// <summary>The car's metres right (+) of the centre line and the half width there (FLICKDBG).</summary>
    string LateralNow()
    {
        int h = _scRoad, i = SchoolScript.Follow(_track, CarXZ, ref h);
        Vector2 d = _track.CenterDir(i), right = new(-d.Y, d.X);
        return $"{(CarXZ - _track.Center[i]).Dot(right):F1}/{_track.HalfWidth[i]:F1}";
    }

    float _scFlickSteer;      // the flick demo's steering, moved steadily towards each phase's
    static float FlickInside => EnvF("FIN", 1f);     // metres off the centre line, on the inside, as a flick comes into the bend

    /// <summary>+1 for a right-hand bend at this entry, -1 for a left-hander (the steering that turns into it).</summary>
    float BendSign(int entry) => MathF.Sign(_track.CenterDir(entry).AngleTo(_track.CenterDir(SchoolScript.Wrap(_track, entry + 25))));

    /// <summary>The flick's strips, road metres from the bend's entry: AWAY from, LIFT OFF from, IN from, IN to.</summary>
    static int EnvI(string k, int d) => OS.GetEnvironment(k) is { Length: > 0 } v ? int.Parse(v) : d;
    static (int away, int lift, int @in, int inEnd) FlickZones => (EnvI("FA", -6), EnvI("FL", -2), EnvI("FI", 1), KickLength);

    void SlideMarks(string tech, int entry)
    {
        _scMarks.Clear();
        _scMarkEntry = entry;
        string clutch = _padMode ? "LB" : "SHIFT", handbrake = _padMode ? "A" : "SPACE";
        // The bend's way (the car's own left or right, as the steering lesson taught), and away from it.
        Vector2 d0 = _track.CenterDir(entry), d1 = _track.CenterDir(SchoolScript.Wrap(_track, entry + 25));
        bool right = d0.AngleTo(d1) > 0;      // as SlideDrive: a right-hand bend
        string into = right ? "RIGHT" : "LEFT", away = right ? "LEFT" : "RIGHT";
        switch (tech)
        {
            case "kick":
                _scMarks.Add((KickPoint - 2, KickPoint + 5, $"CLUTCH IN ({clutch})", MarkBlue, false, 0));
                _scMarks.Add((KickPoint + 5, KickLength, $"LET GO, GAS ON, STEER {into}", MarkGreen, false, 0));     // as far as the try watches the slide
                break;
            case "flick":
            {
                // At the bend: a steer away from it, the gas lifted, then thrown in and the slide driven on the gas.
                var (a, l, i, e) = FlickZones;
                _scMarks.Add((a, l, $"AWAY: STEER {away}", MarkBlue, false, 0));
                _scMarks.Add((l, i, "LIFT OFF", MarkYellow, false, 0));
                _scMarks.Add((i, e, $"IN: STEER {into}, GAS ON", MarkGreen, false, 0));
                break;
            }
            default:        // the hairpin
                _scMarks.Add((-8, -1, $"STEER {into} + HANDBRAKE ({handbrake})", MarkRed, false, 0));
                _scMarks.Add((-1, 8, "GAS ON", MarkGreen, false, 0));
                break;
        }
        PaintMarks();
    }

    void PaintMarks() => _scMarkNode.Show(_scMarks.ConvertAll(m => (SchoolScript.Wrap(_track, _scMarkEntry + m.from), SchoolScript.Wrap(_track, _scMarkEntry + m.to), m.col, m.striped, m.side)).ToArray());

    /// <summary>The brake lesson's markings, as its demos brake: locked from StompPoint, or pumped (bars: a tap each) from PumpPoint, then round the bend.</summary>
    void BrakeMarks(bool pump)
    {
        _scMarks.Clear();
        _scMarkEntry = BrakeEntry;
        string brake = _padMode ? "LT" : "DOWN";
        if (pump)
        {
            _scMarks.Add(((int)PumpPoint, 8, $"TAP THE BRAKE ({brake}): TAP, TAP", MarkRed, true, 0));
            _scMarks.Add((8, 25, "", MarkGreen, false, 0));    // round the bend: no words, the road shows the way
        }
        else _scMarks.Add(((int)StompPoint, 8, $"BRAKE HARD ({brake}): LOCKED", MarkRed, false, 0));
        PaintMarks();
    }

    /// <summary>The car rolling at kmh on the road metres before a bend's entry (the slide lessons).</summary>
    void SlideStart(int entry, float kmh, int back = 50)
    {
        ScFresh();
        _scRoad = SchoolScript.Wrap(_track, entry - back);
        ScRoll(entry - back, 0, kmh);
    }

    /// <summary>Road metres past the entry of the bend, followed.</summary>
    int SlideAlong(int entry)
    {
        int h = _scRoad;
        return SchoolScript.Along(_track, entry, SchoolScript.Follow(_track, CarXZ, ref h));
    }

    /// <summary>
    /// The slide lessons' driver: kmh up to the technique's point (bp road metres from the entry),
    /// then the technique for hold s, then on through the bend on the gas, catching the slide.
    /// </summary>
    void SlideDrive(string tech, int entry, float kmh, float t, ref float t0)
    {
        int s = SlideAlong(entry);
        float steer = SlideSteerNow;
        int bp = tech switch { "kick" => KickPoint, "hairpin" => -6, _ => FlickZones.away };
        if (s < bp && t0 < 0)
        {
            // A flick comes in along the inside of the bend: the swing carries the car out across the road.
            if (tech.Contains("flick")) steer = SchoolScript.SlideSteer(_car, _track, ref _scRoad, FlickInside * BendSign(entry));
            ScCruise(kmh, steer);
            _scFlickSteer = _car.SteerIn;
            _scThrown = false;
            return;
        }
        if (t0 < 0) t0 = t;
        float u = t - t0;
        // The steering that turns the car into the bend (+ right): a right-hand bend turns the road's
        // direction (x, z) the positive way.
        Vector2 d0 = _track.CenterDir(entry), d1 = _track.CenterDir(SchoolScript.Wrap(_track, entry + 25));
        float into = MathF.Sign(d0.AngleTo(d1));
        if (OS.HasEnvironment("FLICKDBG") && tech.Contains("flick")) GD.Print($"flick {tech} u {u:F2} s {s} into {into} lat {LateralNow()} steerIn {_car.SteerIn:F2} yaw {_car.AngVel.Y:F2} heading ({_car.Forward.X:F2},{_car.Forward.Z:F2}) last {_car.LastTechnique}");
        switch (tech)
        {
            case "kick":
                _car.Clutch = u < 0.25f;
                if (u < 0.25f) _car.SetControls(1f, 0f, steer, false, false);
                else KickHold(into, s);
                break;
            case "flick":
                FlickLine(into, s, kmh);     // by the road markings (FlickZones)
                break;
            default:        // the hairpin
                _car.SetControls(u > 0.45f ? 1f : 0f, 0f, steer, u < 0.25f, false);
                break;
        }
        if (OS.HasEnvironment("SLDBG") && s != _scDbgS)
        {
            _scDbgS = s;
            GD.Print($"sl {tech} s {s} lat {LateralNow()} slip {SlipDeg(into):F0} kmh {Kmh:F0} gas {_car.Throttle:F1} steer {_car.SteerIn:F2} last {_car.LastTechnique}{(_car.LastFlickLifted ? "+lift" : "")}");
        }
    }

    float _scKeyT;
    int _scDbgS;
    bool _scThrown;

    static float EnvF(string k, float d) => OS.GetEnvironment(k) is { Length: > 0 } v ? float.Parse(v, System.Globalization.CultureInfo.InvariantCulture) : d;

    /// <summary>
    /// The Scandinavian flick (FlickZones), at the bend: a firm steer away from it, the gas lifted, then
    /// the wheel thrown in, the gas on as soon as it is over, and the slide held on the gas (SlideHold).
    /// </summary>
    void FlickLine(float into, int s, float kmh)
    {
        var (_, liftAt, inAt, _) = FlickZones;
        float target, gas = Kmh < kmh ? 1f : 0.3f;
        if (s < liftAt) target = -into * EnvF("FX", 0.8f);
        else if (s < inAt) { target = -into * EnvF("FX", 0.8f); gas = 0f; }
        else
        {
            if (_scFlickSteer * into >= 0.65f) _scThrown = true;     // the flick taken lifted: the gas may come
            if (!_scThrown) { target = into; gas = 0f; }
            else (target, gas) = SlideHold(into, EnvF("FO", 0f));
        }
        _scFlickSteer = Mathf.MoveToward(_scFlickSteer, target, EnvF("FR", 6f) / Engine.PhysicsTicksPerSecond);   // the hands at a steady pace
        _car.SetControls(gas, 0f, _scFlickSteer, false, false);
    }

    /// <summary>The car's slip angle in degrees, + with the tail out of a bend turning the into way (+1 right).</summary>
    float SlipDeg(float into)
    {
        Vector2 fwd = new(_car.Forward.X, _car.Forward.Z), right = new(_car.Right.X, _car.Right.Z), vel = new(_car.Vel.X, _car.Vel.Z);
        if (vel.Length() < 2f) return 0f;
        return -into * Mathf.RadToDeg(MathF.Atan2(vel.Dot(right), vel.Dot(fwd)));
    }

    /// <summary>After the kick: the slide held round the bend at KickSlip, the gas holding the angle (lifted
    /// as it grows, under 70 km/h), the wheel the line and a little more into the bend while the tail
    /// comes back. Env KICKKEYS=s: as on the keys, each held or not, tapped every s (the share held the amount).</summary>
    /// <summary>A slide held round the bend at KickSlip: the wheel the line and a little more into the
    /// bend while the tail comes back, the gas holding the angle (lifted as it grows, under 70 km/h).</summary>
    (float steer, float gas) SlideHold(float into, float outward)
    {
        float slip = SlipDeg(into);
        float steer = Math.Clamp(SchoolScript.SlideSteer(_car, _track, ref _scRoad, -into * outward) + into * 0.05f * (KickSlip - slip), -1f, 1f);
        float gas = slip > KickSlip + 8f ? 0.2f : slip > KickSlip ? 0.45f : 1f;
        return (steer, Kmh > 70f ? Math.Min(gas, 0.45f) : gas);
    }

    void KickHold(float into, int s)
    {
        var (steer, gas) = SlideHold(into, KickOutward);
        if (OS.GetEnvironment("KICKKEYS") is { Length: > 0 } keys)
        {
            _scKeyT += 1f / Engine.PhysicsTicksPerSecond;
            float ph = _scKeyT / float.Parse(keys, System.Globalization.CultureInfo.InvariantCulture) % 1f;
            steer = ph < MathF.Abs(steer) ? MathF.Sign(steer) : 0f;
            gas = ph < gas ? 1f : 0f;
        }
        _car.SetControls(gas, 0f, steer, false, false);
        if (OS.HasEnvironment("KICKDBG") && s % 4 == 0)
            GD.Print($"kick s {s} slip {SlipDeg(into):F0} kmh {Kmh:F0} gas {gas:F1} steer {steer:F2} over {CarXZ.DistanceTo(_track.Center[CarIndex]) - _track.HalfWidth[CarIndex]:F1}");
    }

    /// <summary>The technique a slide lesson asks for has been used, the right way.</summary>
    bool SlideUsed(string tech) => tech switch
    {
        "kick" => _car.LastTechnique == "clutch",
        "flick" => _car.LastTechnique == "flick" && _car.LastFlickLifted,
        _ => _car.LastTechnique == "handbrake",
    };

    /// <summary>A slide lesson's demo: up to the bend, the technique in slow motion, through it.</summary>
    Beat SlideBeat(string caption, string tech, int entry, float kmh, string done) =>
        B(caption, () => { SlideStart(entry, TryKmh(tech, kmh), TryBack(tech)); SlideMarks(tech, entry); },
            (t, _) =>
            {
                float t0 = _sc.T0;
                SlideDrive(tech, entry, kmh, t, ref t0);
                if (t0 >= 0 && _sc.T0 < 0) { _sc.T0 = t0; SlowMo(2.2f); }
                if (_sc.T0 >= 0 && t - _sc.T0 > 0.8f && _sc.Caption != done) _sc.Caption = done;
                return SlideAlong(entry) > (tech is "kick" or "flick" ? KickLength : SlideLength) || t > 14f;
            }, SchoolBox.None, null, 1f, 16f);       // no arrow: the road markings show where

    /// <summary>A slide lesson's try: the technique in the bend, and through it on the road. back/startKmh:
    /// where and how fast the try starts (the demo's 50 m back can sit in the bend before: too hard to take over).</summary>
    Trial SlideTrial(string tech, int entry, float kmh, string missing, int back = 50, float startKmh = -1)
    {
        var check = new SlideCheck(SlideLength);
        float t0 = -1;
        return new Trial
        {
            Setup = () => { SlideStart(entry, startKmh > 0 ? startKmh : kmh, back); SlideMarks(tech, entry); check = new SlideCheck(SlideLength); t0 = -1; },
            Judge = (_, _) =>
            {
                int h = _scRoad;
                int idx = SchoolScript.Follow(_track, CarXZ, ref h);
                _scRoad = h;
                float over = CarXZ.DistanceTo(_track.Center[idx]) - _track.HalfWidth[idx];
                var v = check.Feed(SchoolScript.Along(_track, entry, idx), over, SlideUsed(tech), missing);
                if (v == Verdict.Fail) _sc.Why = check.Why;
                if (v != Verdict.Pending && SchoolDbg)
                    GD.Print($"school: {tech} try {v} at road metre {idx} ({SchoolScript.Along(_track, entry, idx)} m in), {over:F1} m over the edge, {_car.Speed * 3.6f:F0} km/h, technique {_car.LastTechnique}");
                return v;
            },
            Auto = (t, _) => SlideDrive(tech, entry, kmh, t, ref t0),
        };
    }

    /// <summary>The clutch kick's try: the kick before the bend, then round it all in a held slide (KickSlideCheck).</summary>
    Trial KickTrial()
    {
        var check = new KickSlideCheck(KickLength);
        float t0 = -1, into = BendSign(KickEntry);
        return new Trial
        {
            Setup = () => { SlideStart(KickEntry, KickKmh, TryBack("kick")); SlideMarks("kick", KickEntry); check = new KickSlideCheck(KickLength); t0 = -1; _scKeyT = 0; },
            Judge = (_, _) =>
            {
                int h = _scRoad;
                int idx = SchoolScript.Follow(_track, CarXZ, ref h);
                _scRoad = h;
                float over = CarXZ.DistanceTo(_track.Center[idx]) - _track.HalfWidth[idx];
                int s = SchoolScript.Along(_track, KickEntry, idx);
                var v = check.Feed(s, over, SlideUsed("kick"), SlipDeg(into), _car.Speed * 3.6f, "USE THE CLUTCH IN THE BEND");
                if (v == Verdict.Fail) _sc.Why = check.Why;
                if (v != Verdict.Pending && SchoolDbg)
                    GD.Print($"school: kick try {v} at road metre {idx} ({s} m in), {over:F1} m over the edge, {_car.Speed * 3.6f:F0} km/h, slid {check.Slid} of {KickLength} m, peak {check.PeakSlip:F0} deg, technique {_car.LastTechnique}");
                return v;
            },
            Auto = (t, _) => SlideDrive("kick", KickEntry, KickKmh, t, ref t0),
        };
    }

    // The mission's mock norm panel and lap.
    bool _scMock;
    int _scMockStep;
    LapTimer _scLap;
    float _scLapTime = -1, _scLapAt;

    Vector2 CarXZ => new(_car.Pos.X, _car.Pos.Z);
    Vector3 Ground(Vector2 p, float up = 0) => new(p.X, _track.MeshHeight(p.X, p.Y) + up, p.Y);
    int CarIndex => _track.NearestCenter(CarXZ, out _);
    float Kmh => _car.ForwardSpeed * 3.6f;

    /// <summary>A fresh step: the school's world, a new car (full tank, or fuel; health), nothing left over.</summary>
    void ScFresh(float fuel = 1f, float health = 1f, bool bus = false, bool cans = false)
    {
        EndEnvironmentTour();
        _scLightClock = null;
        ApplyWorld(bus ? Actors.School | Actors.Bus : Actors.School);
        ResetGaiChase();
        _scenery.Reset();
        _wood.Reset();
        _fx.Clear();
        _rescue.Reset();
        _kgb.Reset();
        _babushkas?.Reset();
        _car.Neutral = _car.Clutch = false;
        _carVis.Damage?.Reset();
        ResetFate(true);
        ResetFuel(true);
        if (_babushkas != null) _babushkas.Canisters = cans;      // cans only in the fuel lesson: no stray fuel news in the others
        if (fuel < 1f) _tank.Refill(fuel);
        _lowWarned = true;       // no FUEL LOW alert over the lesson's banners: the fuel lesson says it itself
        if (health < 1f) { _health.Set(health); ApplyTrainingPower(); }
        _ap = new AutoPilot(_track) { Aggression = 1f };
        if (_rad == null) { _hud.Dose = -1f; _hud.Washing = false; _carVis.SetRadGlow(0f, 0f); }     // the zone lesson's mock dose off
        _hud.ClearMessages();
        _hud.ClearAlert();
        _scTossed = _scCaught = _scBurst = _scHit = _scStartled = _scSwerved = _scRescued = _scRepairing = _scRepaired = false;
        _scMock = false;
        _doseSchoolMock = false;
        _scLap = null;
        _scLook = null;
        _scMarks.Clear();
        _scMarkNode?.Clear();
        _sc.T0 = _sc.T1 = -1;
        _sc.Once = false;
    }

    void SetPowerQuiet(int pct)
    {
        _power = _lapPeak = pct;
        ApplyTrainingPower();
    }

    /// <summary>The car on road metre at, lateral m right of the centre line, rolling at kmh in the race direction.</summary>
    void ScRoll(int at, float lateral, float kmh)
    {
        at = SchoolScript.Wrap(_track, at);
        SchoolScript.SetRolling(_car, _track, SchoolScript.RoadPoint(_track, at, lateral), _track.CenterDir(at), kmh / 3.6f);
        _cam.Snap(_car.Pos);
    }

    /// <summary>Brakes to a stop and holds it there (never into reverse).</summary>
    void ScStop()
    {
        if (_car.ForwardSpeed > 1f) _car.SetControls(0f, 1f, 0f, false, false);
        else
        {
            _car.SetControls(false, false, 0f, true);
            _car.Brake = 1;
        }
    }

    /// <summary>Holds kmh, steering as told.</summary>
    void ScCruise(float kmh, float steer) => _car.SetControls(Kmh < kmh ? 1f : 0f, Kmh > kmh + 6f ? 0.5f : 0f, steer, false, false);

    static Beat B(string caption, Action setup, Func<float, float, bool> drive, SchoolBox box = SchoolBox.None, Func<Vector3?> arrow = null, float speed = 1f, float max = 20f) =>
        new() { Caption = caption, Setup = setup, Drive = drive, Box = box, Arrow = arrow, Speed = speed, MaxTime = max };

    // The horn lesson: on the runway (it runs across the screen, about 37 m either way in view).
    const int HornSpot = 80, BusFrom = 130, BusCarFrom = 55;
    const float HornKmh = 45f, HornAt = 25f, BusHonkAt = 32f;

    /// <summary>SCHOOLDBG: where the target and the car are in the frame at the honk (0..1 across and down); a screenshot of it.</summary>
    void HonkLog(string what, Vector3 target, float d, string shot = "honk")
    {
        if (SchoolDbg) GD.Print($"school: honk at the {what} {d:F1} m away: it is at {FrameFrac(target).X:F2},{FrameFrac(target).Y:F2} of the frame, the car at {FrameFrac(_car.Pos).X:F2},{FrameFrac(_car.Pos).Y:F2}");
        ShotSoon(shot);
    }

    float HornDistance => _babushkas == null ? 999f : _babushkas.WalkerPosition(0).DistanceTo(CarXZ);

    /// <summary>Babushka 0 on the road on a scripted spot, the car rolling straight at her from back m at 45 km/h.</summary>
    void HornSetup(float back)
    {
        ScFresh();
        _babushkas?.PutOnRoad(0, HornSpot, 1.2f);
        ScRoll(HornSpot - (int)back, 1.2f, HornKmh);
        _babushkas?.KeepAway(SchoolScript.RoadPoint(_track, HornSpot, 0), 90f, 0);
        _scLook = () => _babushkas == null || HornDistance > 45f ? null : Ground(_babushkas.WalkerPosition(0));
    }

    Vector3? Babushka(int i) => _babushkas == null ? null : Ground(_babushkas.WalkerPosition(i), 3.3f);

    List<Beat> BeatsFor(string id) => TourBeats(id) ?? id switch
    {
        "snow_bank" => SiberiaBank(),
        "snow_hairpin" => SiberiaHairpin(),
        "snow_plough" => SiberiaPlough(),
        "snow_runway" => new() { SiberiaRoad("THE RUNWAY: GRIP RETURNS.", 640, 775, 65f) },
        "snow_ice" => new() { SiberiaRoad("THE FROZEN RIVER: GENTLE INPUTS.", 950, 1040, 50f) },
        "snow_exit" => SiberiaIceExit(),
        "snow_mechanic" => SiberiaMechanic(),
        "snow_fuel" => new() { SiberiaFuelIntro("IN SIBERIA, THE CAR USES FUEL.", true) },
        "snow_tank" => new() { SiberiaFuelIntro("50 LITRES. A LIGHT FOOT SAVES FUEL.", false) },
        "snow_refuel" => new() { B("CATCH A CAN OR STOP AT THE DEPOT.", ScStop, (t, _) => { ScStop(); return t > 3f; }, SchoolBox.Fuel) },
        "snow_grandma" => SiberiaGrandma(),
        "snow_revenge" => new()
        {
            B("TWO MISSILES. TURN HARD TO DODGE.", ScStop, (t, _) =>
            {
                ScStop();
                if (t > 4f) _sc.Caption = "IT ENDS WHEN HIS JET IS GONE.";
                return t > 8f;
            }),
        },
        "snow_low" => SiberiaLowFuel(),
        "snow_empty" => SiberiaEmptyFuel(),
        "snow_tow" => SiberiaFuelTow(),
        "snow_last_try" => new() { B("NO TRIES LEFT? THE GULAG!", ScStop, (t, _) => { ScStop(); return t > 3f; }) },
        "siberia_valley" or "siberia_forestry" or "siberia_radar" or "siberia_base" or "siberia_wreck" or "siberia_camp" or "siberia_night" => SiberiaEnvironmentBeats(id),
        "atom_tour_forest" or "atom_tour_plant" or "atom_tour_channel" or "atom_tour_town" or "atom_tour_wash" => TourBeats(id),
        "atom_rad_sources" or "atom_rad_dose" or "atom_rad_limit" or "atom_rad_wash" => AtomgradRadiation(id),
        "atom_fuel" => AtomgradFuel(),
        "atom_empty" => AtomgradEmptyFuel(),
        "atom_refuel" => AtomgradRefuel(),
        "atom_repairs" => SiberiaMechanic(),
        "kolkhoz_farm" or "kolkhoz_dacha" or "kolkhoz_fields" or "kolkhoz_village" or "kolkhoz_daynight" => KolkhozBeats(id),
        "kolkhoz_drone" or "siberia_drone" or "atomgrad_drone" or "bonus_drone" => new() { DroneBeat() },
        "tour_gai" => new()
        {
            B("THE GAI RADAR BY THE FIRST BEND.", () =>
            {
                ScFresh();
                _trap.Reset();
                _scRoad = _trap.At - 45;
                ScRoll(_scRoad, 0, 45f);
                _scLook = () => _trap.BoothPosition;
            }, (t, dt) =>
            {
                int i = SchoolScript.Follow(_track, CarXZ, ref _scRoad);
                _ap.Drive(_car, dt);
                return i >= _trap.At - 20 && i <= _trap.At;
            }, SchoolBox.None, () => _trap.BoothPosition + Vector3.Up * 3.5f, 1f, 10f),
        },
        "tour_officer" => new()
        {
            B("THE MILITIAMAN READS YOUR SPEED.", () =>
            {
                _trap.Reset();
                _scLook = () => Ground(_trap.OfficerPosition);
            }, (t, dt) =>
            {
                SchoolScript.Follow(_track, CarXZ, ref _scRoad);
                _ap.Drive(_car, dt);
                // Use the real radar reading and bubble, without recording a speed or starting a chase.
                return _trap.Check(_car.Pos, _car.Vel, out _);
            }, SchoolBox.None, () => Ground(_trap.OfficerPosition, 5f), 1f, 15f),
        },
        "gai_ambush" => new()
        {
            B("THE DUTY OFFICER MAY WAIT HERE.", () =>
            {
                ScFresh();
                ApplyWorld(Actors.School | Actors.Kozlik);
                _kozlik.Reset();
                _kozlik.GuardAtTrap();
                ScRoll(_trap.At + 8, 0, 0);
                _scLook = () => Ground(_kozlik.Position2D);
                ShotSoon("ambush", 2.5f);
            }, (t, _) => { ScStop(); return t > 4f; }, SchoolBox.None, () => Ground(_kozlik.Position2D, 3f)),
        },
        "gai_chase" => new()
        {
            B("PATROL WAITING? DO NOT SPEED!", () =>
            {
                ScFresh();
                ApplyWorld(Actors.School | Actors.Kozlik);
                _kozlik.Reset();
                _kozlik.GuardAtTrap();
                _trap.Reset();
                _scRoad = _trap.At - 15;
                ScRoll(_scRoad, 0, 110f);
                _scLook = () => Ground(_kozlik.Position2D);
            }, (t, dt) =>
            {
                SchoolScript.Follow(_track, CarXZ, ref _scRoad);
                if (!_sc.Once)
                {
                    ScCruise(110f, SlideSteerNow);
                    if (_trap.Check(_car.Pos, _car.Vel, out float kmh) && kmh > _trap.Limit && StartGaiChase(kmh))
                    {
                        _sc.Once = true;
                        _sc.T0 = t;
                        _sc.Caption = "SIRENS ON! THE PATROL CHASES YOU.";
                        ShotSoon("chase", 1.5f);
                    }
                }
                else _ap.Drive(_car, dt);
                return _sc.Once && (t - _sc.T0 > 5f || AutographHolds);
            }, SchoolBox.Speedo, () => Ground(_kozlik.Position2D, 3f), 1f, 12f),
        },
        "can" => new()
        {
            B("THE CAR DRINKS FUEL.", () =>
            {
                ScFresh(fuel: 0.3f, cans: true);
                _babushkas?.AimPass(_car, 1, _trackId == "siberia" ? 700 : 90, 50 / 3.6f, _trackId == "siberia" ? 45f : 70f, 2.3f);
                _cam.Snap(_car.Pos);
            }, (t, _) => CanDemo(t, "SPASIBO, BABUSHKA!", false), SchoolBox.Fuel, null, 1f, 16f),
        },
        "golden" => new()
        {
            B("A GOLD CAN: OCTANE BOOSTER!", () =>
            {
                ScFresh(fuel: 0.5f, cans: true);
                _babushkas?.AimPass(_car, 1, _trackId == "siberia" ? 700 : 90, 50 / 3.6f, _trackId == "siberia" ? 45f : 70f, 2.3f, golden: true);
                _cam.Snap(_car.Pos);
            }, (t, dt) => CanDemo(t, "OCTANE BOOSTER: SUPER POWER!", true, dt), SchoolBox.None, () => Babushka(1), 1f, 16f),
        },
        "depot" => new()
        {
            B("OR STOP AT THE DEPOT.", () => { ScFresh(fuel: 0.14f); ScRoll(DepotIndex - 45, 0, 0); },
                (t, _) =>
                {
                    DepotDrive();
                    if (_refuelling && _sc.T0 < 0) { _sc.T0 = t; _sc.Caption = "THE TANK FILLS UP."; }
                    if (_sc.T0 >= 0) _sc.Speed = _tank.Fraction < 0.97f ? 3f : 1f;
                    return _sc.T0 >= 0 && (_tank.Fraction >= 0.99f && t - _sc.T0 > 1f || t - _sc.T0 > 14f);
                }, SchoolBox.Fuel, () => Ground(_depot.Zone, 1.2f), 1f, 40f),
        },
        // The zone (ATOMGRAD-4) on the training ground: no radiation here, so the dose is mocked on the HUD.
        "dose" => new()
        {
            B("IN THE ZONE THE CAR GETS A DOSE.", () =>
            {
                ScFresh();
                ScRoll(120, 1.2f, 50f);
            }, (t, dt) =>
            {
                _ap.Drive(_car, dt);
                float d = Math.Min(t / 7f, 0.95f);
                MockDose(d * 20f, false);
                if (t > 3.5f) _sc.Caption = "IT GLOWS. THE DOSE GAUGE FILLS.";
                return t > 8f;
            }, SchoolBox.Dose, null, 1f, 10f),
            B("WASH IT OFF AT THE STATION.", () => { }, (t, _) =>
            {
                ScStop();
                float d = Math.Max(0f, 19f - t * 6f);
                MockDose(d, d > 0.01f);
                if (d <= 0.01f) _sc.Caption = "CLEAN AS A NEW KOPECK.";
                return t > 5f;
            }, SchoolBox.Dose, null, 1f, 7f),
        },
        "dosimetrist" => new()
        {
            B("OVER 20 MSV...", () =>
            {
                ScFresh();
                _car.Reset(_track.StartPos, _track.StartYaw);
                _cam.Snap(_car.Pos);
            }, (t, _) =>
            {
                ScStop();
                MockDose(t < 4f ? 23f : 0f, t >= 1.2f && t < 4f);
                if (t > 1.2f) _sc.Caption = t < 4f ? "THE DOSIMETRIST: 10 SECONDS!" : "WASHED. DRIVE ON.";
                return t > 5.5f;
            }, SchoolBox.Dose, null, 1f, 7f),
        },
        "kick" => new() { SlideBeat("CLUTCH IN AND OUT IN THE BEND...", "kick", KickEntry, KickKmh, "THE TAIL SLIDES. GAS ON!") },
        "flick" => new() { SlideBeat("AWAY, LIFT OFF, IN...", "flick", FlickEntry, FlickKmh, "THE PENDULUM SWINGS IT IN!") },
        "hairpin" => new() { SlideBeat("STEER AND PULL THE HANDBRAKE...", "hairpin", HairpinEntry, HairpinKmh, "THE TAIL SWINGS ROUND!") },
        "crash" => new()
        {
            B("CRASHES HURT THE ENGINE.", () =>
            {
                if (OnIlyich) { BusCrashSetup(); return; }
                ScFresh(health: 0.34f);
                AimWall(55f);
            }, (t, dt) =>
            {
                if (_crashKind == "bus" && _bus != null) _crashTarget = _bus.Position2D + _bus.Forward2D * 3.5f;   // its nose
                CrashDrive(dt);
                if (!_sc.Once && CarXZ.DistanceTo(_crashTarget) < 11f && t > 0.5f) { _sc.Once = true; SlowMo(2f); }
                if (_sc.T0 < 0 && _maxImpact > 4f) { _sc.T0 = t; _sc.Arrow = null; }
                return _sc.T0 >= 0 && t - _sc.T0 > 3f;
            }, SchoolBox.Engine, () => Ground(_crashTarget, 1.5f), 1f, 14f),
        },
        "headlights" => new()
        {
            B("DUSK TURNS THE HEADLIGHTS ON.", () =>
            {
                ScFresh();
                ScRoll(65, 0, 0);
                _car.Neutral = true;
                _scLightClock = 8;
                ShotSoon("both_beams", 4);
            }, (t, _) =>
            {
                ScStop();
                if (t > 6 && !_sc.Once)
                {
                    _sc.Once = true;
                    _carVis.Damage?.ShootHeadlamp(_car, _fx);
                    ShotSoon("one_beam", 2);
                }
                if (_sc.Once) _sc.Caption = t > 10 ? "THE MECHANIC RESTORES THE LIGHTS." : "A BROKEN LAMP LOSES ITS BEAM.";
                return t > 14;
            }, max: 15),
        },
        "mechanic" => new()
        {
            B("THE MECHANIC SLEEPS AT THE BUS STOP.", () =>
            {
                ScFresh(health: MechanicHealth);
                ScRoll(MechanicStop - MechanicBack, -1.5f, 35f);
                _carVis.Damage?.Hit(10f, new Vector3(0, -0.05f, -2.05f), Vector3.Back, _car, _fx);
            }, (t, _) =>
            {
                float d = _drunks == null ? 999f : _drunks.MechanicPosition.DistanceTo(CarXZ);
                if (_drunks is not { MechanicUp: true })
                {
                    ScCruise(MechanicKmh(d), SchoolScript.SteerRoad(_car, _track, -1.5f));
                    _sc.Horn = MechanicHonk(d);
                    if (_sc.Horn || t > 3f) _sc.Caption = "WAKE HIM WITH THE HORN.";      // first who he is, then how
                    if (_sc.Horn && _sc.T1 < 0) { _sc.T1 = t; HonkLog("mechanic", Ground(_drunks.MechanicPosition, 1f), d); }
                    return t > 30f;
                }
                if (_sc.Caption != "PARK. HE FIXES IT." && SchoolDbg) GD.Print($"school: mechanic up at {t:F1} s, {d:F0} m away");
                _sc.Caption = "PARK. HE FIXES IT.";
                if (SchoolScript.Along(_track, CarIndex, MechanicStop) > 3) ScCruise(22f, SchoolScript.SteerRoad(_car, _track, -2f));
                else ScStop();
                if (_scRepairing && _sc.T0 < 0) { _sc.T0 = t; if (SchoolDbg) GD.Print($"school: repair starts at {t:F1} s"); }
                // Fast forward: his walk to the parked car, then the repair after its first moments.
                _sc.Speed = _scRepaired ? 1f : _sc.T0 >= 0 ? (t - _sc.T0 > 1.5f ? 4f : 1f) : _car.Speed < 0.5f ? 2.5f : 1f;
                if (_scRepaired && !_sc.Once) { _sc.Once = true; _sc.T0 = t; if (SchoolDbg) GD.Print($"school: repaired at {t:F1} s"); }
                return _sc.Once && t - _sc.T0 > 1.8f;
            }, SchoolBox.Engine, () => _drunks == null ? null : Ground(_drunks.MechanicPosition, 2.6f), 1f, 50f),
        },
        "rescue" => new()
        {
            B("STUCK? PRESS R.", () =>
            {
                ScFresh();
                if (OnIlyich) InTheDitch(); else StuckAtWall();
                if (_padMode) _sc.Caption = "STUCK? PRESS B.";
            },
                (t, _) =>
                {
                    if (OnIlyich && t < 4.5f)
                    {
                        // Full gas in the river bed: the wheels spin in the mud, the bank does not let it out.
                        _car.SetControls(1f, 0f, MathF.Sin(t * 2.5f), false, false);
                        // The lesson shows a car that cannot get out, however good it is: the bank holds it where it sits.
                        Vector3 slip = _car.Pos - _scDitchPos; slip.Y = 0;
                        if (slip.Length() > 0.5f) _car.Pos -= slip.Normalized() * (slip.Length() - 0.5f);
                        _car.Vel = new Vector3(_car.Vel.X * 0.15f, _car.Vel.Y, _car.Vel.Z * 0.15f);
                        if (t > 2f) _sc.Caption = _padMode ? "NO WAY OUT? PRESS B." : "NO WAY OUT? PRESS R.";
                        if (SchoolDbg && (int)(t * 2) != (int)((t - 1f / 60f) * 2)) GD.Print($"school: ditch t {t:F1} at ({_car.Pos.X:F1},{_car.Pos.Z:F1}) {_car.Speed * 3.6f:F0} km/h, {_car.Wheels[2].Surface}");
                        return false;
                    }
                    if (t < 2.8f) { _car.SetControls(1f, 0f, 0f, false, false); return false; }
                    if (!_scRescued)
                    {
                        CallMechanic();
                        if (OnIlyich)
                        {
                            // Back on the road beside the ditch (the school's lap progress is not the car's).
                            int i = (_track.NearestCenter(CarXZ, out _) - 6 + _track.Center.Length) % _track.Center.Length;
                            _car.Reset(_track.CenterPos(i) + Vector3.Up * 0.7f, TrackData.YawFor(_track.CenterDir(i)));
                            _cam.Snap(_car.Pos);
                        }
                        _sc.Caption = "BACK ON THE ROAD. THE LAP IS LOST.";
                    }
                    ScStop();
                    return t > (OnIlyich ? 7.5f : 5.5f);
                }, SchoolBox.None, null, 1f, 10f),
        },
        "brake" => new()
        {
            B("HARD ON THE BRAKE...", () => { ScFresh(); ScRoll(BrakeEntry - BrakeBack, 0, BrakeKmh); BrakeMarks(false); },
                (t, _) =>
                {
                    int s = SchoolScript.Along(_track, BrakeEntry, CarIndex);
                    if (s < StompPoint && _sc.T0 < 0) { ScCruise(BrakeKmh, SchoolScript.SteerRoad(_car, _track)); return false; }
                    if (_sc.T0 < 0) { _sc.T0 = t; SlowMo(2.5f); _sc.Caption = "LOCKED WHEELS DO NOT STEER!"; }
                    if (_car.Speed * 3.6f > 4f) _car.SetControls(0f, 1f, SchoolScript.SteerRoad(_car, _track) > 0 ? 1f : -1f, false, false);
                    else ScStop();
                    return t - _sc.T0 > 4.5f;
                }, SchoolBox.None, null, 1f, 12f),
            B("NOW TAP THE BRAKE...", () => { ScFresh(); ScRoll(BrakeEntry - BrakeBack, 0, BrakeKmh); BrakeMarks(true); },
                (t, _) =>
                {
                    PumpDrive(t);
                    if (_sc.T0 >= 0 && _sc.Caption != "TAP THE BRAKE: THE CAR TURNS!") { SlowMo(2f); _sc.Caption = "TAP THE BRAKE: THE CAR TURNS!"; }
                    return SchoolScript.Along(_track, BrakeEntry, CarIndex) > BrakeLength + 8 || _sc.T0 >= 0 && t - _sc.T0 > 6f;
                }, SchoolBox.None, null, 1f, 12f),
        },
        "handbrake" => new()
        {
            B("STEER AND PULL THE HANDBRAKE.", () => { ScFresh(); ScRoll(45, 2f, 45f); },
                (t, _) =>
                {
                    HandbrakeDrive(t);
                    if (t > 0.3f && _sc.T0 < 0) { _sc.T0 = t; SlowMo(2.2f); }
                    if (t > 1.9f) _sc.Caption = "THE CAR SPINS AROUND.";
                    return t > 4f;
                }, SchoolBox.None, null, 1f, 8f),
        },
        "throttle" => new()
        {
            B("CONCRETE: FULL GAS...", () => { ScFresh(); ScRoll(15, 0, 0); }, (t, _) => SurfaceRun(t, "CONCRETE"), SchoolBox.Speedo, null, 1f, 8f),
            B("MUD: FULL GAS...", () => { ScFresh(); ScRoll(372, 0, 0); }, (t, _) => SurfaceRun(t, "MUD"), SchoolBox.Speedo, null, 1f, 8f),
        },
        "steer" => new() { SteerDemo(SteerDemos[0]), SteerDemo(SteerDemos[1]), SteerDemo(SteerDemos[2]) },
        "clutch" => new()
        {
            // The film plays at the real start: in the first track's start barn, with the lights counting down as in a race.
            B("1. CLUTCH IN. REV TO 5000.", () => { ScFresh(); _car.Reset(_track.StartPos, _track.StartYaw); _cam.Snap(_car.Pos); _countdown = 3f; },
                (t, _) =>
                {
                    bool lights = _countdown > 0f;
                    ClutchDrive(t, lights);
                    // The correct start, step by step: hold the clutch, bring the revs to the engine's strong band and keep them there
                    // until the lights go green, then let go with the gas flat and the wheels straight and watch the car hook up.
                    if (_sc.T0 < 0)
                    {
                        if (lights) _sc.Caption = t < 0.6f ? "1. CLUTCH IN. REV TO 5000." : _car.Rpm < 4700f ? "2. REVS UP..." : "2. HOLD 5000 RPM. WAIT FOR GREEN!";
                        else { _sc.T0 = t; SlowMo(1.6f); _sc.Caption = "3. GREEN! LET GO. GAS FLAT. STRAIGHT!"; }
                    }
                    if (_sc.T0 >= 0 && Kmh >= 50f && _sc.T1 < 0) { _sc.T1 = t; _sc.Caption = $"50 KM/H IN {t - _sc.T0:0.0} S!"; }
                    return _sc.T1 >= 0 && t - _sc.T1 > 1.6f;
                }, SchoolBox.Speedo, null, 1f, 12f),
        },
        "horn" => new()
        {
            B("HONK! SHE JUMPS ASIDE.", () => HornSetup(50f),
                (t, _) =>
                {
                    float d = HornDistance;
                    if (_scStartled && _sc.T0 < 0) { _sc.T0 = t; SlowMo(2.2f); }
                    if (_sc.T0 < 0 && !_sc.Once)
                    {
                        ScCruise(HornKmh, 0f);
                        // Close and well inside the frame (with the car): her dive happens on screen.
                        _sc.Horn = _sc.T1 >= 0 || d <= HornAt && _babushkas != null && WellInFrame(Ground(_babushkas.WalkerPosition(0), 1f)) && WellInFrame(_car.Pos);
                        if (_sc.Horn && _sc.T1 < 0) { _sc.T1 = t; HonkLog("babushka", Ground(_babushkas.WalkerPosition(0), 1f), d); }
                        if (_sc.T1 >= 0 && t - _sc.T1 > 0.5f) _sc.Once = true;
                        return t > 9f;
                    }
                    if (_car.Speed > 1f) _car.SetControls(0f, 0.45f, 0f, false, false);      // off the gas, braking: she has room
                    else ScStop();
                    return _sc.T0 >= 0 && t - _sc.T0 > 3.2f || t > 9f;
                }, SchoolBox.None, () => Babushka(0), 1f, 10f),
            B("EVEN THE BUS!", () =>
            {
                ScFresh(bus: true);
                _bus?.Warp(BusFrom);
                ScRoll(BusCarFrom, 2f, 40f);
                _babushkas?.KeepAway(CarXZ, 110f);
                _scLook = () => _bus == null || _bus.Focus2D.DistanceTo(CarXZ) > 75f ? null : Ground(_bus.Focus2D);   // the car and the bus in one frame
            }, (t, _) =>
            {
                float d = _bus == null ? 999f : _bus.Position2D.DistanceTo(CarXZ);
                if (_scSwerved && _sc.T0 < 0) { _sc.T0 = t; SlowMo(2f); _sc.Arrow = () => _bus == null ? null : Ground(_bus.Focus2D, 4.5f); }
                if (_sc.T0 < 0)
                {
                    ScCruise(40f, SchoolScript.SteerRoad(_car, _track, 2f));
                    // Head-on and close, the bus and the car both well inside the frame: the whole swerve on screen.
                    bool ready = d <= BusHonkAt && _bus != null && WellInFrame(Ground(_bus.Position2D, 1.5f)) && WellInFrame(_car.Pos);
                    _sc.Horn = _sc.T1 >= 0 && t - _sc.T1 < 0.6f || ready && _sc.T1 < 0;
                    if (ready && _sc.T1 < 0) { _sc.T1 = t; HonkLog("bus", Ground(_bus.Position2D, 1.5f), d); }
                    return t > 12f;
                }
                if (_car.Speed > 1f) _car.SetControls(0f, 0.5f, SchoolScript.SteerRoad(_car, _track, 2f), false, false);
                else ScStop();
                if (t - _sc.T0 > 3.2f && !_sc.Once && _bus != null) { _sc.Once = true; HonkLog("bus after the swerve", Ground(_bus.Focus2D, 1.5f), _bus.Focus2D.DistanceTo(CarXZ), "ditch"); }
                return t - _sc.T0 > 4f;
            }, SchoolBox.None, () => _bus == null ? null : Ground(_bus.Position2D, 4.5f), 1f, 16f),
        },
        _ => new(),
    };

    /// <summary>The zone lesson: a dose (mSv of the 20 limit) on the gauge and on the car, WASHING over it.</summary>
    void MockDose(float msv, bool washing)
    {
        _hud.Dose = msv / 20f;
        _hud.DoseMsv = msv;
        _hud.DoseRate = 0.1f;
        _hud.Washing = washing;
        float g = Math.Clamp((msv / 20f - 0.3f) / 0.7f, 0f, 1f);
        if (msv >= 20f) g *= 0.75f + 0.25f * MathF.Sin(_clock * 6f);
        _carVis.SetRadGlow(g, _dayNight.Glow);
    }

    Trial TrialFor(string id)
    {
        switch (id)
        {
            // The tries start on the straight before the bend (the demos' 50 m back is in the bend before it).
            case "kick": return KickTrial();
            case "flick": return SlideTrial("flick", FlickEntry, EnvI("FS", 60), "STEER AWAY, LIFT OFF, THEN IN", TryBack("flick"), TryKmh("flick", -1));
            case "hairpin": return SlideTrial("hairpin", HairpinEntry, HairpinKmh, "PULL THE HANDBRAKE IN THE HAIRPIN");
            case "can":
            {
                float hw = _track.HalfWidth[90];
                return new Trial
                {
                    Setup = () =>
                    {
                        ScFresh(fuel: 0.3f, cans: true);
                        _babushkas?.AimPass(_car, 1, 90, 40 / 3.6f, 70f, hw + 0.35f + hw * 0.5f);
                        _cam.Snap(_car.Pos);
                    },
                    Judge = (_, _) =>
                    {
                        if (_scCaught) return Verdict.Success;
                        if (_scBurst || _scHit) { _sc.Why = "DO NOT HIT HER! PASS CLOSE"; return Verdict.Fail; }
                        if (!_scTossed && _babushkas != null && (_babushkas.WalkerPosition(1) - CarXZ).Dot(_track.CenterDir(90)) < -6f) { _sc.Why = "CLOSER! INTO HER LANE"; return Verdict.Fail; }
                        return Verdict.Pending;
                    },
                    Auto = (_, _) =>
                    {
                        Vector2 d = _track.CenterDir(90), right = new(-d.Y, d.X);
                        ScCruise(40f, _babushkas == null ? 0f : SchoolScript.SteerTo(_car, _babushkas.WalkerPosition(1) - right * 2.3f + d * 4f));
                    },
                    Arrow = () => Babushka(1), Box = SchoolBox.Fuel,
                };
            }
            case "depot":
                return new Trial
                {
                    Setup = () => { ScFresh(fuel: 0.14f); ScRoll(DepotIndex - 50, 0, 0); },
                    Judge = (_, _) =>
                    {
                        if (_refuelling) return Verdict.Success;
                        if (SchoolScript.Along(_track, DepotIndex, CarIndex) > 22) { _sc.Why = "STOP BY THE FUEL TANK"; return Verdict.Fail; }
                        return Verdict.Pending;
                    },
                    Auto = (_, _) => DepotDrive(),
                    Arrow = () => Ground(_depot.Zone, 1.2f), Box = SchoolBox.Fuel,
                };
            case "mechanic":
                return new Trial
                {
                    Setup = () => { ScFresh(health: MechanicHealth); ScRoll(MechanicStop - MechanicBack, -1.5f, 35f); },
                    Judge = (_, _) =>
                    {
                        if (_drunks is { MechanicUp: true }) return Verdict.Success;
                        if (SchoolScript.Along(_track, MechanicStop, CarIndex) > 30) { _sc.Why = "HONK BEFORE YOU PASS HIM"; return Verdict.Fail; }
                        return Verdict.Pending;
                    },
                    Auto = (_, _) =>
                    {
                        float d = _drunks == null ? 999f : _drunks.MechanicPosition.DistanceTo(CarXZ);
                        ScCruise(MechanicKmh(d), SchoolScript.SteerRoad(_car, _track, -1.5f));
                        _sc.Horn = _drunks != null && MechanicHonk(d);
                    },
                    Arrow = () => _drunks == null ? null : Ground(_drunks.MechanicPosition, 2.6f),
                };
            case "rescue":
                return new Trial
                {
                    Setup = () => { ScFresh(); StuckAtWall(); },
                    Judge = (_, _) => _scRescued ? Verdict.Success : Verdict.Pending,
                    Auto = (t, _) => { if (t > 1.5f && !_scRescued) CallMechanic(); else ScStop(); },
                };
            case "brake":
            {
                var check = new PumpBrakeCheck(BrakeLength);
                return new Trial
                {
                    Setup = () => { ScFresh(); ScRoll(BrakeEntry - BrakeTryBack, 0, BrakeKmh - 5f); BrakeMarks(true); check = new PumpBrakeCheck(BrakeLength); },
                    Judge = (_, _) =>
                    {
                        float over = SchoolScript.OffRoad(_track, CarXZ, out int idx);
                        var v = check.Feed(SchoolScript.Along(_track, BrakeEntry, idx), over, _car.Speed * 3.6f);
                        if (v == Verdict.Fail) _sc.Why = check.Why;
                        return v;
                    },
                    Auto = (t, _) => PumpDrive(t),
                };
            }
            case "handbrake":
            {
                var check = new HandbrakeTurnCheck();
                return new Trial
                {
                    Setup = () => { ScFresh(); ScRoll(45, 2f, 45f); check = new HandbrakeTurnCheck(); },
                    Judge = (t, _) =>
                    {
                        var v = check.Feed(t, MathF.Atan2(_car.Forward.X, _car.Forward.Z), _car.Speed * 3.6f, _car.Handbrake > 0);
                        if (v == Verdict.Pending && SchoolScript.Along(_track, 45, CarIndex) > 80) { _sc.Why = "TURN AROUND ON THE RUNWAY"; return Verdict.Fail; }
                        return v;
                    },
                    Auto = (t, _) => HandbrakeDrive(t),
                };
            }
            case "steer":
            {
                var check = new SlideCheck(SteerTryLength);
                return new Trial
                {
                    Setup = () => { ScFresh(); ScRoll(SteerTryFrom, 0, 35f); check = new SlideCheck(SteerTryLength); },
                    Judge = (t, _) =>
                    {
                        int idx = CarIndex;
                        float over = CarXZ.DistanceTo(_track.Center[idx]) - _track.HalfWidth[idx];
                        var v = check.Feed(SchoolScript.Along(_track, SteerTryEntry, idx), over, true, null);
                        if (v == Verdict.Pending && t > 30f) { _sc.Why = "KEEP DRIVING DOWN THE ROAD"; return Verdict.Fail; }
                        if (v == Verdict.Fail) _sc.Why = check.Why;
                        return v;
                    },
                    Auto = (_, _) => ScCruise(40f, SchoolScript.SteerRoad(_car, _track)),
                    Arrow = () => Ground(SchoolScript.RoadPoint(_track, SteerTryEntry + SteerTryLength, 0), 1f),
                };
            }
            case "clutch":
            {
                var check = new ClutchLaunchCheck();
                return new Trial
                {
                    Setup = () => { ScFresh(); ScRoll(15, 0, 0); check = new ClutchLaunchCheck(); },
                    Judge = (_, dt) =>
                    {
                        var v = check.Feed(dt, _car.Speed * 3.6f, _car.Clutch, _car.Rpm, _car.Throttle);
                        if (v == Verdict.Fail) _sc.Why = check.Why;
                        return v;
                    },
                    Auto = (t, _) => ClutchDrive(t, t < ClutchHoldTime),
                    Box = SchoolBox.Speedo,
                };
            }
            case "horn":
                return new Trial
                {
                    Setup = () => HornSetup(60f),
                    Judge = (_, _) =>
                    {
                        // Her reaction only comes to a honk within her range (the car fast and at most ~3 s away); she must be in view too.
                        if (_scStartled && _scStartleSeen) return Verdict.Success;
                        if (_scStartled) { _scStartled = false; }
                        if (_scHit) { _sc.Why = "HONK EARLIER!"; return Verdict.Fail; }
                        if (_car.Speed > 2f && (_babushkas == null || (_babushkas.WalkerPosition(0) - CarXZ).Dot(new Vector2(_car.Vel.X, _car.Vel.Z).Normalized()) < -4f))
                        { _sc.Why = "HONK WHEN SHE IS CLOSE!"; return Verdict.Fail; }
                        if (_car.Speed < 1f && _sc.Sim > 2f) { _sc.Why = "KEEP GOING AND HONK!"; return Verdict.Fail; }
                        return Verdict.Pending;
                    },
                    Auto = (_, _) =>
                    {
                        ScCruise(HornKmh, 0f);
                        _sc.Horn = HornDistance <= HornAt;
                    },
                    Arrow = () => Babushka(0),
                };
        }
        return new Trial { Setup = ScStop, Judge = (_, _) => Verdict.Success, Auto = (_, _) => { } };
    }

    // ------------------------------------------------------------ the demos' drivers

    /// <summary>The can lessons: roll past her at 50 km/h, slow motion as she throws, then (golden) flat out a moment.</summary>
    bool CanDemo(float t, string caught, bool golden, float dt = 0)
    {
        if (!golden && t > 2.2f && !_scTossed && _sc.Arrow == null) { _sc.Caption = "DRIVE PAST HER, IN HER LANE."; _sc.Arrow = () => Babushka(1); _sc.Box = SchoolBox.None; }
        if (_scTossed && !_sc.Once) { _sc.Once = true; SlowMo(2.4f); }
        if (_scCaught && _sc.T0 < 0) { _sc.T0 = t; _sc.Caption = caught; _sc.Arrow = null; _sc.Box = golden ? SchoolBox.Speedo : SchoolBox.Fuel; }
        if (_sc.T0 < 0) { ScCruise(50f, 0f); return t > 14f; }
        if (golden && t - _sc.T0 < 2f) _car.SetControls(1f, 0f, SchoolScript.SteerRoad(_car, _track), false, false);
        else if (golden) _ap.Drive(_car, dt);
        else ScStop();
        return t - _sc.T0 > (golden ? 4.5f : 2.8f);
    }

    int DepotIndex => SchoolScript.Wrap(_track, _track.NearestCenter(_depot.Zone, out _));

    /// <summary>To the depot's pump and stop there (the demo, and --school-auto's try).</summary>
    void DepotDrive()
    {
        Vector2 target = _depot.ParkPos;
        float d = target.DistanceTo(CarXZ), want = Math.Clamp(d * 1.4f, 0f, 25f);
        if (d < 1.8f || _refuelling) { ScStop(); return; }
        float steer = SchoolScript.SteerTo(_car, target);
        _car.SetControls(Kmh < want ? 0.7f : 0f, Kmh > want + 3f ? 0.6f : 0f, steer, false, false);
    }

    /// <summary>The demo honks at the mechanic only near the stop, with him and the car well in view (he hears it from 60 m).</summary>
    bool MechanicHonk(float d) => d <= MechanicHonkAt && _drunks != null && FrameFrac(Ground(_drunks.MechanicPosition, 1f)).Y >= 0.3f && WellInFrame(Ground(_drunks.MechanicPosition, 1f)) && WellInFrame(_car.Pos);
    const float MechanicHonkAt = 13f;
    /// <summary>The horn lesson's engine: smoking, misfiring, nearly done for (it smokes from EngineHealth.SmokeAt 25 %).</summary>
    const float MechanicHealth = 0.12f;
    /// <summary>The car comes in this far before the stop (on Ilyich: just past the church hairpin, before the last bend), at 35 km/h,
    /// slowing to 25 for the last 35 m (the honk and the stop): it stays on the road and the lesson is short.</summary>
    int MechanicBack => OnIlyich ? 60 : 85;
    static float MechanicKmh(float d) => d > 35f ? 35f : 25f;

    /// <summary>The bus stop the mechanic sleeps at (its road metre).</summary>
    int MechanicStop
    {
        get
        {
            Vector2 m = _drunksAll?.MechanicPosition ?? Vector2.Zero;
            if (_track.BusStops.Count == 0) return _track.NearestCenter(m, out _);
            var best = _track.BusStops[0];
            foreach (var s in _track.BusStops) if (s.Position.DistanceTo(m) < best.Position.DistanceTo(m)) best = s;
            return best.Index;
        }
    }

    /// <summary>The nearest stone wall from the start line (as --crash picks it), rammed once at kmh.</summary>
    void AimWall(float kmh)
    {
        _car.Reset(_track.StartPos, _track.StartYaw);
        _crashKind = "wall";
        _crashKmh = kmh;
        _crashRams = 1;
        _crashBack = 0;
        _crashTarget = new Vector2(-1e4f, -1e4f);
        AimAtObstacle();
        if (_crashTarget.X < -1e3f)
        {
            // No dry-stone wall on this track (Ilyich's Lamp): a big tree instead.
            _crashKind = "tree";
            AimAtObstacle();
        }
        _cam.Snap(_car.Pos);
    }

    // The crash on Ilyich: on the start straight, head-on into the bus coming the other way (it brakes and honks too late).
    const int BusCrashBus = 130, BusCrashCar = 30;

    void BusCrashSetup()
    {
        ScFresh(health: 0.7f, bus: true);
        _bus?.Warp(BusCrashBus);
        ScRoll(BusCrashCar, 0, 50f);
        _babushkas?.KeepAway(CarXZ, 120f);
        _crashKind = "bus";
        _crashKmh = 55f;
        _crashRams = 1;
        _crashBack = 0;
        _crashTarget = _bus?.Position2D ?? CarXZ;
        _scLook = () => _bus == null || _bus.Position2D.DistanceTo(CarXZ) > 60f ? null : Ground(_bus.Position2D);
    }

    /// <summary>Ilyich's R lesson: the car down in the hollow east of the road at 225 m, nose at the slope up to the road (full gas gets it ~2 m in 4 s; ILYICHLAB=ditch found it). Env DITCHAT=x,z,dx,dz tries another spot.</summary>
    Vector3 _scDitchPos;

    void InTheDitch()
    {
        var v = (OS.GetEnvironment("DITCHAT") is { Length: > 0 } e ? e : "303,102,-1,0").Split(',');
        float x = float.Parse(v[0], System.Globalization.CultureInfo.InvariantCulture), z = float.Parse(v[1], System.Globalization.CultureInfo.InvariantCulture);
        var dir = new Vector2(float.Parse(v[2], System.Globalization.CultureInfo.InvariantCulture), float.Parse(v[3], System.Globalization.CultureInfo.InvariantCulture));
        _scDitchPos = Ground(new Vector2(x, z), 0.7f);
        _car.Reset(_scDitchPos, TrackData.YawFor(dir.Normalized()));
        // The hollow is near the map's east edge: the camera looks towards the road.
        Vector3 road = Ground(_track.Center[_track.NearestCenter(CarXZ, out _)]);
        _scLook = () => _scRescued ? null : road;
        _cam.Snap(_car.Pos);
    }

    /// <summary>Nose against that stone wall, going nowhere.</summary>
    void StuckAtWall()
    {
        AimWall(10f);
        Vector2 dir = (_crashTarget - CarXZ).Normalized(), p = _crashTarget - dir * 3.1f;
        _car.Reset(Ground(p, 0.7f), TrackData.YawFor(dir));
        _cam.Snap(_car.Pos);
    }

    /// <summary>Into the corner at 85 km/h, pumping the brake from the braking point until 38 km/h, then round it.</summary>
    void PumpDrive(float t)
    {
        int s = SchoolScript.Along(_track, BrakeEntry, CarIndex);
        float steer = SchoolScript.SteerRoad(_car, _track);
        if (s < PumpPoint && _sc.T0 < 0) { ScCruise(BrakeKmh, steer); return; }
        if (_sc.T0 < 0) _sc.T0 = t;
        if (!_sc.Once && _car.Speed * 3.6f > PumpUntil)
        {
            float bt = t - _sc.T0;
            _car.SetControls(0f, bt % 0.3f < 0.17f ? 1f : 0f, steer, false, false);
            return;
        }
        _sc.Once = true;
        ScCruise(35f, steer);
    }

    /// <summary>A handbrake turn: full left lock with the handbrake for 1.2 s, then stop.</summary>
    void HandbrakeDrive(float t)
    {
        if (t < 0.3f) ScCruise(45f, 0f);
        else if (t < 1.5f) _car.SetControls(0f, 0f, -1f, true, false);
        else if (t < 1.9f) _car.SetControls(0f, 0f, -1f, false, false);
        else ScStop();
    }

    /// <summary>A clutch launch: clutch in and full revs for 1.3 s, let go, flat out.</summary>
    /// <summary>How long the clutch is held: the revs come up in about a second, then sit steady for a moment before the release.</summary>
    const float ClutchHoldTime = 2.4f;
    const float LaunchRpm = 5000f;

    /// <summary>A proper clutch start: clutch in, throttle feathered to hold about 5000 rpm (the engine's strong band, not the
    /// limiter), then the clutch up with the gas flat and the wheels straight.</summary>
    void ClutchDrive(float t, bool hold)
    {
        _car.Clutch = hold;
        if (Kmh > 75f) { ScStop(); return; }
        if (hold)
        {
            float throttle = t < 0.6f ? 0f : Mathf.Clamp((LaunchRpm - _car.Rpm) / 450f, 0f, 1f);
            _car.SetControls(throttle, 0f, 0f, false, false);
        }
        else _car.SetControls(1f, 0f, SchoolScript.SteerRoad(_car, _track), false, false);
    }

    /// <summary>Three seconds flat out from a standstill, then the speed it made.</summary>
    bool SurfaceRun(float t, string what)
    {
        if (t < 3f) { _car.SetControls(1f, 0f, SchoolScript.SteerRoad(_car, _track), false, false); return false; }
        if (_sc.T0 < 0) { _sc.T0 = t; _sc.Caption = what == "MUD" ? $"MUD: ONLY {Kmh:0} KM/H. THEY SPIN!" : $"CONCRETE: {Kmh:0} KM/H IN 3 S"; }
        ScStop();
        return t - _sc.T0 > 2.2f;
    }
}
