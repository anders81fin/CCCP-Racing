using System;
using System.Collections.Generic;
using CCCPRacing.Car;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// Records a lap as sampled body transforms plus what the driver and the car were doing
/// (steering, pedals, revs, springs, tyres, hits), and plays it back into a CarSim puppet so the
/// replay and the ghost show steered wheels, working suspension, smoke, mud and backfires.
/// Each sample also has the time of day it was driven at (DayNight.Clock), so the replay is lit
/// as the lap was: dusk, night, headlights.
/// </summary>
public partial class Ghost
{
    public const float SampleRate = 30f;
    const uint MagicV1 = 0x43434350; // "CCCP": poses only
    const uint MagicV2 = 0x32434343; // "CCC2": poses + car state
    const uint MagicV3 = 0x33434343; // "CCC3": poses + car state + time of day

    public struct WheelFrame
    {
        public float Length, SlipLat, SpinSpeed;
        public SurfaceType Surface;
        public bool Contact, Spinning, Locked;
    }

    public struct Frame
    {
        public Vector3 Vel;
        public float Steer, Throttle, Brake, Handbrake, Rpm, Slide, Scrape;
        public int Gear;
        public bool Limiter, Loose, Clutch, Neutral;
        // Strongest hits since the previous sample, replayed as bangs and camera shake.
        public float Impact, Pothole, Suspension;
        public ImpactMaterial ImpactMaterial;
        public bool ImpactBroke;
    }

    /// <summary>Hits passed over since the last Apply.</summary>
    public struct Events
    {
        public float Impact, Pothole, Suspension;
        public ImpactMaterial ImpactMaterial;
        public bool ImpactBroke;
    }

    readonly List<Vector3> _pos = new();
    readonly List<Quaternion> _rot = new();
    readonly List<Frame> _frames = new();
    readonly List<WheelFrame> _wheels = new();   // 4 per sample
    readonly List<float> _day = new();           // DayNight.Clock per sample, -1: the plain day (none in older records)
    float _pendImpact, _pendPothole, _pendSusp;
    ImpactMaterial _pendMaterial;
    bool _pendBroke;
    int _lastApplied = -1;
    public float LapTime = -1;

    public int Count => _pos.Count;
    public readonly record struct Collision(float Time, float Speed, ImpactMaterial Material, bool Broke);

    /// <summary>Last accepted contact in the short audible history. Match the
    /// collision voice's contact grouping instead of inventing a fresh attack on seek.</summary>
    public Collision? CollisionBefore(float clock)
    {
        Collision? last = null;
        int first = Math.Max(0, (int)MathF.Floor((clock - 1.5f) * SampleRate));
        int end = Math.Min(_frames.Count - 1, (int)MathF.Floor(clock * SampleRate + .001f));
        for (int i = first; i <= end; i++)
        {
            var f = _frames[i]; if (f.Impact <= 3) continue;
            float t = i / SampleRate;
            if (last is not Collision p || t - p.Time >= .085f || f.Impact * f.Impact >= p.Speed * p.Speed * 1.4f)
                last = new(t, f.Impact, f.ImpactMaterial, f.ImpactBroke);
        }
        return last;
    }
    static byte Flags(Frame f) => (byte)((f.Limiter ? 1 : 0) | (f.Loose ? 2 : 0) | (f.Clutch ? 4 : 0)
        | (f.Neutral ? 8 : 0) | (((int)f.ImpactMaterial & 7) << 4) | (f.ImpactBroke ? 128 : 0));
    /// <summary>Raw stored sample (no interpolation): what the world tape's fingerprint hashes.</summary>
    public Vector3 PositionAt(int i) => _pos[i];
    public Quaternion RotationAt(int i) => _rot[i];
    public float Duration => _pos.Count / SampleRate;

    public void Clear()
    {
        _pos.Clear();
        _rot.Clear();
        _frames.Clear();
        _wheels.Clear();
        _day.Clear();
        _damage.Clear(); _damageWarning = false;
        _recordingDamageBytes=0;_recordingLastShape=null;
        _pendImpact = _pendPothole = _pendSusp = 0;
        _pendMaterial = ImpactMaterial.Unknown; _pendBroke = false;
        LapTime = -1;
    }

    /// <summary>
    /// Call every physics tick with time since lap start; stores samples at SampleRate. The hits
    /// of this tick are carried into the next stored sample. dayClock: the time of day (DayNight.Clock), -1 for the plain day.
    /// </summary>
    public void Record(float lapClock, CarSim car, float impact, float pothole, float suspension, float dayClock = -1,
        ImpactMaterial impactMaterial = ImpactMaterial.Unknown, bool impactBroke = false, CarDamage damage = null)
    {
        if (impact > _pendImpact) { _pendImpact = impact; _pendMaterial = impactMaterial; _pendBroke = impactBroke; }
        _pendPothole = Math.Max(_pendPothole, pothole);
        _pendSusp = Math.Max(_pendSusp, suspension);
        while (_pos.Count <= lapClock * SampleRate)
        {
            _pos.Add(car.Pos);
            if (damage != null) {var state=damage.CaptureTapeState();_damage.Add(state);AccountDamage(state);}
            _day.Add(dayClock);
            _rot.Add(car.Rot.GetRotationQuaternion());
            _frames.Add(new Frame
            {
                Vel = car.Vel,
                Steer = car.SteerAngle,
                Throttle = car.Throttle,
                Brake = car.Brake,
                Handbrake = car.Handbrake,
                Rpm = car.Rpm,
                Slide = car.Slide,
                Scrape = car.BodyScrape,
                Gear = car.Gear,
                Limiter = car.Limiter,
                Loose = car.LooseSurface,
                Clutch = car.Clutch,
                Neutral = car.Neutral,
                Impact = _pendImpact,
                ImpactMaterial = _pendMaterial,
                ImpactBroke = _pendBroke,
                Pothole = _pendPothole,
                Suspension = _pendSusp,
            });
            _pendImpact = _pendPothole = _pendSusp = 0;
            _pendMaterial = ImpactMaterial.Unknown; _pendBroke = false;
            foreach (var w in car.Wheels)
                _wheels.Add(new WheelFrame
                {
                    Length = w.VisualLength, SlipLat = w.SlipLat, SpinSpeed = w.SpinSpeed, Surface = w.Surface,
                    Contact = w.Contact, Spinning = w.Spinning, Locked = w.Locked,
                });
        }
    }

    public Transform3D Sample(float lapClock)
    {
        if (_pos.Count == 0) return Transform3D.Identity;
        Index(lapClock, out int i, out int j, out float u);
        return new Transform3D(new Basis(_rot[i].Slerp(_rot[j], u)), _pos[i].Lerp(_pos[j], u));
    }

    void Index(float lapClock, out int i, out int j, out float u)
    {
        float f = Math.Clamp(lapClock * SampleRate, 0, _pos.Count - 1);
        i = (int)f;
        j = Math.Min(i + 1, _pos.Count - 1);
        u = f - i;
    }

    /// <summary>
    /// Poses the puppet car as it was at lapClock: body, velocity, pedals, revs, steering, springs
    /// and tyre state, so CarVisual, TyreEffects and EngineSound treat it like the real thing.
    /// Returns the hits passed since the previous call (none when jumping backwards).
    /// </summary>
    public Events Apply(float lapClock, CarSim car, float dt)
    {
        var ev = new Events();
        if (_pos.Count == 0) return ev;
        Index(lapClock, out int i, out int j, out float u);
        int near = u < 0.5f ? i : j;

        if (i > _lastApplied && _lastApplied >= 0)
            for (int k = _lastApplied + 1; k <= i; k++)
            {
                if (_frames[k].Impact > ev.Impact)
                {
                    ev.Impact = _frames[k].Impact; ev.ImpactMaterial = _frames[k].ImpactMaterial; ev.ImpactBroke = _frames[k].ImpactBroke;
                }
                ev.Pothole = Math.Max(ev.Pothole, _frames[k].Pothole);
                ev.Suspension = Math.Max(ev.Suspension, _frames[k].Suspension);
            }
        _lastApplied = i;

        Frame a = _frames[i], b = _frames[j], n = _frames[near];
        car.Pos = _pos[i].Lerp(_pos[j], u);
        car.Rot = new Basis(_rot[i].Slerp(_rot[j], u));
        car.Vel = a.Vel.Lerp(b.Vel, u);
        car.AngVel = Vector3.Zero;
        car.SteerAngle = Mathf.Lerp(a.Steer, b.Steer, u);
        car.Throttle = car.ThrottleTarget = Mathf.Lerp(a.Throttle, b.Throttle, u);
        car.Brake = n.Brake;
        car.Handbrake = n.Handbrake;
        car.Rpm = Mathf.Lerp(a.Rpm, b.Rpm, u);
        car.Gear = n.Gear;
        car.Clutch = n.Clutch;
        car.Neutral = n.Neutral;
        car.Slide = Mathf.Lerp(a.Slide, b.Slide, u);
        car.BodyScrape = Mathf.Lerp(a.Scrape, b.Scrape, u);
        car.Limiter = n.Limiter;
        car.LooseSurface = n.Loose;
        car.UpsideDownTime = 0;

        for (int k = 0; k < 4; k++)
        {
            Wheel w = car.Wheels[k];
            WheelFrame wa = _wheels[i * 4 + k], wb = _wheels[j * 4 + k], wn = _wheels[near * 4 + k];
            w.VisualLength = Mathf.Lerp(wa.Length, wb.Length, u);
            w.SlipLat = Mathf.Lerp(wa.SlipLat, wb.SlipLat, u);
            w.SpinSpeed = Mathf.Lerp(wa.SpinSpeed, wb.SpinSpeed, u);
            w.SpinAngle += w.SpinSpeed * dt;
            w.Surface = wn.Surface;
            w.Contact = wn.Contact;
            w.Spinning = wn.Spinning;
            w.Locked = wn.Locked;
            Vector3 attach = car.Pos + car.Rot * w.Local;
            w.ContactPoint = new Vector3(attach.X, car.Ground.Sample(attach.X, attach.Z).Height, attach.Z);
        }
        return ev;
    }

    /// <summary>The lap was recorded with its time of day (older records and the plain day have none).</summary>
    public bool HasDayClock => _day.Count == _pos.Count && _day.Count > 0 && _day[0] >= 0;

    /// <summary>The time of day (DayNight.Clock) at lapClock; only when HasDayClock.</summary>
    public float DayClockAt(float lapClock)
    {
        Index(lapClock, out int i, out int j, out float u);
        return Mathf.Lerp(_day[i], _day[j], u);
    }

    /// <summary>Forget the playback position, e.g. when a replay wraps round to the start.</summary>
    public void Rewind() => _lastApplied = -1;

    public Ghost CopyWithTime(float lapTime)
    {
        var g = new Ghost { LapTime = lapTime };
        g._pos.AddRange(_pos);
        g._rot.AddRange(_rot);
        g._frames.AddRange(_frames);
        g._wheels.AddRange(_wheels);
        g._day.AddRange(_day);
        g._damage.AddRange(_damage);
        g._recordingDamageBytes=_recordingDamageBytes;g._recordingLastShape=_recordingLastShape;
        return g;
    }

    /// <summary>Atomic: writes path.tmp, then replaces the old file (or moves into place); the final path always holds a complete ghost.</summary>
    public void Save(string path)
    {
        string tmp = path + ".tmp";
        if (!WriteTo(tmp)) return;
        if (!WorldTapeFile.CommitFile(tmp, path)) GD.PushWarning($"Cannot save ghost to {path}: the old file is kept");
    }

    /// <summary>The ghost file's bytes, made with managed code only (no Godot object), byte for byte what <see cref="WriteTo"/> writes (a test compares them): the pair save serialises on the main thread and a worker writes the file.</summary>
    internal byte[] ToBytes()
    {
        var ms = new System.IO.MemoryStream(32 + _pos.Count * 160);
        using (var f = new System.IO.BinaryWriter(ms, System.Text.Encoding.UTF8, true))
        {
            f.Write(HasDamage ? MagicV4 : MagicV3); f.Write(LapTime); f.Write((uint)_pos.Count);
            for (int i = 0; i < _pos.Count; i++)
            {
                f.Write(_pos[i].X); f.Write(_pos[i].Y); f.Write(_pos[i].Z);
                f.Write(_rot[i].X); f.Write(_rot[i].Y); f.Write(_rot[i].Z); f.Write(_rot[i].W);
                Frame fr = _frames[i];
                f.Write(fr.Vel.X); f.Write(fr.Vel.Y); f.Write(fr.Vel.Z);
                f.Write(fr.Steer); f.Write(fr.Throttle); f.Write(fr.Brake); f.Write(fr.Handbrake);
                f.Write(fr.Rpm); f.Write(fr.Slide); f.Write(fr.Scrape);
                f.Write(fr.Impact); f.Write(fr.Pothole); f.Write(fr.Suspension);
                f.Write((byte)(sbyte)fr.Gear);
                f.Write(Flags(fr));
                for (int k = 0; k < 4; k++)
                {
                    WheelFrame w = _wheels[i * 4 + k];
                    f.Write(w.Length); f.Write(w.SlipLat); f.Write(w.SpinSpeed);
                    f.Write((byte)w.Surface);
                    f.Write((byte)((w.Contact ? 1 : 0) | (w.Spinning ? 2 : 0) | (w.Locked ? 4 : 0)));
                }
                f.Write(i < _day.Count ? _day[i] : -1f);
            }
            if (HasDamage) WriteDamage(f);
        }
        return ms.ToArray();
    }

    /// <summary>Writes the ghost file format to exactly this path (no tmp, no replace).</summary>
    internal bool WriteTo(string path)
    {
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        if (f == null) { GD.PushWarning($"Cannot save ghost to {path}: {FileAccess.GetOpenError()}"); return false; }
        f.StoreBuffer(ToBytes());
        return true;
    }

    /// <summary>
    /// Loads a ghost. Old pose-only records (v1) get their car state reconstructed from the motion
    /// and the ground so they replay the same way, only a little less exactly.
    /// </summary>
    public static Ghost Load(string path, CarParams p, IGround ground)
    {
        if (!FileAccess.FileExists(path)) return null;
        var bytes = FileAccess.GetFileAsBytes(path);
        if (bytes == null || bytes.Length == 0) return null;
        var g = FromBytes(bytes, p, ground, out string error);
        if (g == null && error != null && error.StartsWith("damage")) GD.PushWarning($"Car tape: invalid damage stream in {path}; recording ignored");
        return g;
    }

    /// <summary>
    /// The ghost of a ghost file's bytes (what <see cref="ToBytes"/> or Save wrote), or null when they are not a whole ghost file.
    /// Managed code only (no Godot object is created or touched), so it is safe on any thread; the result owns all its data, nothing is
    /// shared with <paramref name="bytes"/> or with another ghost. <see cref="Load"/> reads the file and calls this, so the result is identical.
    /// A truncated file is refused (a file read by Godot's FileAccess used to be padded with zeros); trailing bytes after the samples of a v1-v3 file are ignored as before.
    /// </summary>
    public static Ghost FromBytes(byte[] bytes, CarParams p, IGround ground) => FromBytes(bytes, p, ground, out _);

    /// <summary>As <see cref="FromBytes(byte[],CarParams,IGround)"/>; <paramref name="error"/> says why a null result is null.</summary>
    public static Ghost FromBytes(byte[] bytes, CarParams p, IGround ground, out string error)
    {
        error = null;
        if (bytes == null || bytes.Length < 12) { error = "too short"; return null; }
        try
        {
            using var f = new System.IO.BinaryReader(new System.IO.MemoryStream(bytes, false));
            uint magic = f.ReadUInt32();
            if (magic != MagicV1 && magic != MagicV2 && magic != MagicV3 && magic != MagicV4) { error = "not a ghost file"; return null; }
            var g = new Ghost { LapTime = f.ReadSingle() };
            uint n = f.ReadUInt32();
            if (n > 100000) { error = "too many samples"; return null; }
            long perSample = magic == MagicV1 ? 28 : magic == MagicV2 ? 138 : 142;
            if (12 + n * perSample > bytes.Length || (magic == MagicV4 && bytes.Length < 16 + (long)n * 142)) { error = "truncated"; return null; }
            if (magic == MagicV1 && ground == null) { error = "a pose-only ghost needs the ground"; return null; }
            for (int i = 0; i < n; i++)
            {
                g._pos.Add(new Vector3(f.ReadSingle(), f.ReadSingle(), f.ReadSingle()));
                g._rot.Add(new Quaternion(f.ReadSingle(), f.ReadSingle(), f.ReadSingle(), f.ReadSingle()));
                if (magic == MagicV1) continue;
                var fr = new Frame
                {
                    Vel = new Vector3(f.ReadSingle(), f.ReadSingle(), f.ReadSingle()),
                    Steer = f.ReadSingle(), Throttle = f.ReadSingle(), Brake = f.ReadSingle(), Handbrake = f.ReadSingle(),
                    Rpm = f.ReadSingle(), Slide = f.ReadSingle(), Scrape = f.ReadSingle(),
                    Impact = f.ReadSingle(), Pothole = f.ReadSingle(), Suspension = f.ReadSingle(),
                    Gear = (sbyte)f.ReadByte(),
                };
                int flags = f.ReadByte();
                fr.Limiter = (flags & 1) != 0;
                fr.Loose = (flags & 2) != 0;
                // Optional bits in the existing flags byte: old recordings read false;
                // file length and all existing fields/fingerprints remain unchanged.
                fr.Clutch = (flags & 4) != 0;
                fr.Neutral = (flags & 8) != 0;
                int material = (flags >> 4) & 7;
                fr.ImpactMaterial = material <= (int)ImpactMaterial.Soft ? (ImpactMaterial)material : ImpactMaterial.Unknown;
                fr.ImpactBroke = (flags & 128) != 0;
                g._frames.Add(fr);
                for (int k = 0; k < 4; k++)
                {
                    var w = new WheelFrame { Length = f.ReadSingle(), SlipLat = f.ReadSingle(), SpinSpeed = f.ReadSingle(), Surface = (SurfaceType)f.ReadByte() };
                    int wf = f.ReadByte();
                    w.Contact = (wf & 1) != 0;
                    w.Spinning = (wf & 2) != 0;
                    w.Locked = (wf & 4) != 0;
                    g._wheels.Add(w);
                }
                if (magic == MagicV3 || magic == MagicV4) g._day.Add(f.ReadSingle());
            }
            if (magic == MagicV4)
            {
                uint length = f.ReadUInt32();
                long left = bytes.Length - f.BaseStream.Position;
                if (length > 128 * 1024 * 1024 || length != left || !g.ReadDamage(f.ReadBytes((int)length))) { error = "damage stream invalid"; return null; }
            }
            if (magic == MagicV1) g.Reconstruct(p, ground);
            return g;
        }
        catch (Exception ex) when (ex is System.IO.IOException or ArgumentException or OverflowException) { error = "damaged: " + ex.Message; return null; }
    }

    /// <summary>
    /// Guesses the car state of a pose-only recording: velocity and yaw rate from neighbouring
    /// samples, steering from the turn radius, pedals from the acceleration, the gear from the
    /// speed, springs and tyres from the ground under each wheel.
    /// </summary>
    void Reconstruct(CarParams p, IGround ground)
    {
        int n = _pos.Count;
        float wheelbase = p.RearZ - p.FrontZ;
        var vel = new Vector3[n];
        var yawRate = new float[n];
        for (int i = 0; i < n; i++)
        {
            int a = Math.Max(0, i - 1), b = Math.Min(n - 1, i + 1);
            float span = Math.Max(1, b - a) / SampleRate;
            vel[i] = (_pos[b] - _pos[a]) / span;
            Vector3 fa = new Basis(_rot[a]) * Vector3.Forward, fb = new Basis(_rot[b]) * Vector3.Forward;
            // Positive = turning right (clockwise seen from above), like SteerAngle.
            yawRate[i] = MathF.Atan2(fa.X * fb.Z - fa.Z * fb.X, fa.X * fb.X + fa.Z * fb.Z) / span;
        }

        float steer = 0, throttle = 0;
        int gear = 1;
        for (int i = 0; i < n; i++)
        {
            var rot = new Basis(_rot[i]);
            Vector3 fwd = -rot.Z, right = rot.X;
            float vf = vel[i].Dot(fwd);
            int a = Math.Max(0, i - 3), b = Math.Min(n - 1, i + 3);
            float accel = (vel[b].Dot(fwd) - vel[a].Dot(fwd)) / (Math.Max(1, b - a) / SampleRate);

            // Kinematic steer angle; drifting makes it overshoot, so clamp to the rack.
            float maxSteer = p.MaxSteer / (1f + Math.Abs(vf) / p.SteerSpeedFalloff);
            float target = Math.Abs(vf) > 1.5f ? MathF.Atan(wheelbase * yawRate[i] / vf) : steer;
            steer = Mathf.Lerp(steer, Math.Clamp(target, -maxSteer, maxSteer), 0.5f);

            float thrTarget = accel > 0.4f ? 1f : accel > -1.5f && vf > 3f ? 0.35f : 0f;
            throttle = Mathf.Lerp(throttle, thrTarget, 0.35f);
            bool braking = accel < -5f && vf > 3f;

            // Automatic box: lowest gear under the shift point, with a little hysteresis.
            float Rpm(int gi) => Math.Abs(vf) / p.WheelRadius * p.Gears[gi] * p.FinalDrive * 60f / MathF.Tau;
            while (gear < p.Gears.Length && Rpm(gear - 1) > p.ShiftUp) gear++;
            while (gear > 1 && Rpm(gear - 2) < p.ShiftUp * 0.7f) gear--;
            float rpm = Math.Clamp(Rpm(gear - 1), p.IdleRpm, p.Redline);

            var fr = new Frame
            {
                Vel = vel[i], Steer = steer, Throttle = throttle, Brake = braking ? 1 : 0,
                Rpm = rpm, Gear = gear, Suspension = 0,
            };

            Vector3 angVel = Vector3.Up * -yawRate[i];
            int contacts = 0, loose = 0;
            for (int k = 0; k < 4; k++)
            {
                bool front = k < 2, left = k % 2 == 0;
                var local = new Vector3(left ? -p.HalfTrack : p.HalfTrack, p.AttachY, front ? p.FrontZ : p.RearZ);
                Vector3 attach = _pos[i] + rot * local;
                GroundSample gs = ground.Sample(attach.X, attach.Z);
                float len = (attach.Y - p.WheelRadius - gs.Height) / Math.Max(0.25f, rot.Y.Y);
                bool contact = len < p.RestLength;
                Vector3 vc = vel[i] + angVel.Cross(attach - _pos[i]);
                Vector3 wf = front ? fwd.Rotated(Vector3.Up, -steer) : fwd;
                float vLat = vc.Dot(front ? wf.Cross(Vector3.Up) : right);
                bool spinning = !front && contact && throttle > 0.7f && gear <= 2 && Surfaces.Get(gs.Surface).Loose && Math.Abs(vf) > 1f;
                bool locked = braking && contact && accel < -8f;
                if (contact) { contacts++; if (Surfaces.Get(gs.Surface).Loose) loose++; }
                fr.Slide = Math.Max(fr.Slide, Math.Clamp((Math.Abs(vLat) - 1.5f) / 5f, 0, 1));
                _wheels.Add(new WheelFrame
                {
                    Length = contact ? Math.Max(len, p.RestLength - p.MaxTravel - 0.05f) : p.RestLength,
                    SlipLat = contact ? vLat : 0,
                    SpinSpeed = locked ? 0 : spinning ? 60f : vc.Dot(wf) / p.WheelRadius,
                    Surface = gs.Surface,
                    Contact = contact, Spinning = spinning, Locked = locked,
                });
            }
            fr.Loose = loose * 2 >= Math.Max(1, contacts);
            _frames.Add(fr);
        }
    }
}
