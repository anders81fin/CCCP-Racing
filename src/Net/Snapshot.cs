using System;
using System.Collections.Generic;
using System.IO;
using CCCPRacing.Car;
using CCCPRacing.Race;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Net;

/// <summary>
/// One car at one moment of a LAN race, as a ghost sample (Ghost.Frame and four WheelFrames) plus
/// where it stands in the race. Time is elapsed host time from the round's reveal, so the grid's
/// variable clutch wait and the race share one uninterrupted pose timeline.
/// </summary>
public class Snapshot
{
    public float Time;
    public uint Ack;
    public int RepairSerial;
    public int FuelPickupSerial;
    public float FinishTime=-1, WreckLeft, Health=1, LapTime, LastLap=-1, BestLap=-1;
    public float FuelLitres = -1;
    public float WorldLightProgress;
    public int GridReadyCount;
    public byte[] State=Array.Empty<byte>();
    public Vector3 Pos;
    public Quaternion Rot = Quaternion.Identity;
    public Ghost.Frame Frame;
    public readonly Ghost.WheelFrame[] Wheels = new Ghost.WheelFrame[4];
    public int Laps, NextGate = 1, Progress;
    public bool Finished, Destroyed, MechanicRepairing, Refuelling;
    public byte BrokenHeadlights; // bit 0 left, bit 1 right; persistent cosmetic state, not an impact event

    public static Snapshot Of(CarSim car, float time, float impact, int laps, int nextGate, int progress, bool finished)
    {
        var s = new Snapshot
        {
            Time = time, Pos = car.Pos, Rot = car.Rot.GetRotationQuaternion(),
            Frame = new Ghost.Frame
            {
                Vel = car.Vel, Steer = car.SteerAngle, Throttle = car.Throttle, Brake = car.Brake, Handbrake = car.Handbrake,
                Rpm = car.Rpm, Slide = car.Slide, Scrape = car.BodyScrape, Gear = car.Gear, Limiter = car.Limiter,
                Loose = car.LooseSurface, Impact = impact,
            },
            Laps = laps, NextGate = nextGate, Progress = progress, Finished = finished,
        };
        for (int k = 0; k < 4; k++)
        {
            var w = car.Wheels[k];
            s.Wheels[k] = new Ghost.WheelFrame
            {
                Length = w.VisualLength, SlipLat = w.SlipLat, SpinSpeed = w.SpinSpeed, Surface = w.Surface,
                Contact = w.Contact, Spinning = w.Spinning, Locked = w.Locked,
            };
        }
        return s;
    }

    /// <summary>A car standing still at a pose (a grid slot before its first snapshot).</summary>
    public static Snapshot Standing(Vector3 pos, float yaw, float time, float idleRpm)
    {
        var s = new Snapshot { Time = time, Pos = pos, Rot = new Basis(Vector3.Up, yaw).GetRotationQuaternion() };
        s.Frame.Rpm = idleRpm;
        s.Frame.Gear = 1;
        for (int k = 0; k < 4; k++) s.Wheels[k] = new Ghost.WheelFrame { Length = 0.3f, Contact = true, Surface = SurfaceType.Concrete };
        return s;
    }

    public void Write(BinaryWriter w)
    {
        w.Write(Ack); w.Write(RepairSerial); w.Write(FuelPickupSerial); w.Write(FinishTime); w.Write(WreckLeft); w.Write(Health); w.Write(LapTime); w.Write(LastLap); w.Write(BestLap); w.Write(FuelLitres); w.Write(WorldLightProgress); w.Write((byte)GridReadyCount);
        w.Write((ushort)State.Length); w.Write(State);
        w.Write(Time);
        W(w, Pos);
        w.Write(Rot.X); w.Write(Rot.Y); w.Write(Rot.Z); w.Write(Rot.W);
        var f = Frame;
        W(w, f.Vel);
        w.Write(f.Steer); w.Write(f.Throttle); w.Write(f.Brake); w.Write(f.Handbrake);
        w.Write(f.Rpm); w.Write(f.Slide); w.Write(f.Scrape); w.Write(f.Impact);
        w.Write((sbyte)f.Gear);
        w.Write((byte)((f.Limiter ? 1 : 0) | (f.Loose ? 2 : 0) | (Finished ? 4 : 0) | (Destroyed ? 8 : 0) | (MechanicRepairing ? 16 : 0) | (Refuelling ? 32 : 0) | ((BrokenHeadlights & 3) << 6)));
        foreach (var wf in Wheels)
        {
            w.Write(wf.Length); w.Write(wf.SlipLat); w.Write(wf.SpinSpeed);
            w.Write((byte)wf.Surface);
            w.Write((byte)((wf.Contact ? 1 : 0) | (wf.Spinning ? 2 : 0) | (wf.Locked ? 4 : 0)));
        }
        w.Write((byte)Math.Clamp(Laps, 0, 255));
        w.Write((ushort)Math.Clamp(NextGate, 0, ushort.MaxValue));
        w.Write((ushort)Math.Clamp(Progress, 0, ushort.MaxValue));
    }

    public static Snapshot Read(BinaryReader r)
    {
        var s = new Snapshot { Ack=r.ReadUInt32(), RepairSerial=r.ReadInt32(), FuelPickupSerial=r.ReadInt32(), FinishTime=r.ReadSingle(), WreckLeft=r.ReadSingle(), Health=r.ReadSingle(), LapTime=r.ReadSingle(), LastLap=r.ReadSingle(), BestLap=r.ReadSingle(), FuelLitres=r.ReadSingle(), WorldLightProgress=r.ReadSingle(), GridReadyCount=r.ReadByte() };
        int length=r.ReadUInt16(); if(length>4096) throw new InvalidDataException("car state length");
        s.State=r.ReadBytes(length);
        s.Time=r.ReadSingle(); s.Pos=V(r);
        s.Rot = new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle()).Normalized();
        var f = new Ghost.Frame
        {
            Vel = V(r), Steer = r.ReadSingle(), Throttle = r.ReadSingle(), Brake = r.ReadSingle(), Handbrake = r.ReadSingle(),
            Rpm = r.ReadSingle(), Slide = r.ReadSingle(), Scrape = r.ReadSingle(), Impact = r.ReadSingle(), Gear = r.ReadSByte(),
        };
        int flags = r.ReadByte();
        f.Limiter = (flags & 1) != 0;
        f.Loose = (flags & 2) != 0;
        s.Finished = (flags & 4) != 0; s.Destroyed=(flags&8)!=0; s.MechanicRepairing=(flags&16)!=0; s.Refuelling=(flags&32)!=0;
        s.BrokenHeadlights=(byte)((flags>>6)&3);
        s.Frame = f;
        for (int k = 0; k < 4; k++)
        {
            var wf = new Ghost.WheelFrame { Length = r.ReadSingle(), SlipLat = r.ReadSingle(), SpinSpeed = r.ReadSingle(), Surface = (SurfaceType)r.ReadByte() };
            int wflags = r.ReadByte();
            wf.Contact = (wflags & 1) != 0;
            wf.Spinning = (wflags & 2) != 0;
            wf.Locked = (wflags & 4) != 0;
            s.Wheels[k] = wf;
        }
        s.Laps = r.ReadByte();
        s.NextGate = r.ReadUInt16();
        s.Progress = r.ReadUInt16();
        if (!s.Pos.IsFinite() || !float.IsFinite(s.Time) || !float.IsFinite(s.WorldLightProgress)) throw new InvalidDataException("bad snapshot");
        return s;
    }

    static void W(BinaryWriter w, Vector3 v) { w.Write(v.X); w.Write(v.Y); w.Write(v.Z); }
    static Vector3 V(BinaryReader r) => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
}

/// <summary>
/// A remote car's snapshots in race-time order. It is drawn Delay behind the race clock, between
/// the two snapshots around that moment; when none has come yet for it, the newest is carried on
/// along its velocity for up to MaxExtrapolate, then held.
/// </summary>
public class SnapshotBuffer
{
    public const float Delay = 0.05f, MaxExtrapolate = 0.25f, Keep = 1.5f;
    readonly List<Snapshot> _s = new();

    public int Count => _s.Count;
    public Snapshot Latest => _s.Count > 0 ? _s[^1] : null;

    /// <summary>Adds a snapshot in time order (late ones are slotted in, duplicates dropped, old ones forgotten).</summary>
    public void Add(Snapshot s)
    {
        int i = _s.Count;
        while (i > 0 && _s[i - 1].Time > s.Time) i--;
        if (i > 0 && Math.Abs(_s[i - 1].Time - s.Time) < 1e-5f) return;
        _s.Insert(i, s);
        float cut = _s[^1].Time - Keep;
        int drop = 0;
        while (drop < _s.Count - 2 && _s[drop + 1].Time < cut) drop++;
        if (drop > 0) _s.RemoveRange(0, drop);
    }

    public void Clear() => _s.Clear();

    /// <summary>Current/historical collision estimate, separate from delayed rendering. Never collide against an unknown future or stale frozen car.</summary>
    public const float ContactExtrapolate = .1f;
    public bool ContactPose(float time, CarSim scratch)
    {
        if(_s.Count==0 || time<_s[0].Time || time-_s[^1].Time>ContactExtrapolate) return false;
        Sample(time,out var a,out var b,out float u,out float extra);
        scratch.Pos=a.Pos.Lerp(b.Pos,u)+b.Frame.Vel*extra;
        scratch.Rot=new Basis(a.Rot.Slerp(b.Rot,u));
        scratch.Vel=a.Frame.Vel.Lerp(b.Frame.Vel,u);
        scratch.AngVel=Vector3.Zero;
        return true;
    }

    public float PredictContact(float time, CarSim local, CarSim scratch)
        => ContactPose(time,scratch) ? CarContacts.Predict(local,scratch) : 0;

    /// <summary>Keep distant cars smooth, but bring nearby contact partners to the local car's current timeline.</summary>
    public float PresentationTime(float now, Vector3 observer)
    {
        if(_s.Count==0 || now-_s[^1].Time>ContactExtrapolate || !Pose(now,out var p,out _)) return now-Delay;
        float near=Math.Clamp((32f-p.DistanceTo(observer))/20f,0,1);
        return now-Delay*(1-near);
    }

    /// <summary>
    /// The snapshots around render time t: a and b with the blend u between them, and extra, how
    /// far (s, capped at MaxExtrapolate) the pose runs on past b along b's velocity. False when empty.
    /// </summary>
    public bool Sample(float t, out Snapshot a, out Snapshot b, out float u, out float extra)
    {
        a = b = null;
        u = extra = 0;
        if (_s.Count == 0) return false;
        if (t <= _s[0].Time) { a = b = _s[0]; return true; }
        var last = _s[^1];
        if (t >= last.Time)
        {
            a = b = last;
            extra = Math.Min(t - last.Time, MaxExtrapolate);
            return true;
        }
        int i = _s.Count - 2;
        while (i > 0 && _s[i].Time > t) i--;
        a = _s[i];
        b = _s[i + 1];
        u = Math.Clamp((t - a.Time) / Math.Max(b.Time - a.Time, 1e-5f), 0f, 1f);
        return true;
    }

    /// <summary>Just the body pose at render time t (interpolated or extrapolated).</summary>
    public bool Pose(float t, out Vector3 pos, out Quaternion rot)
    {
        pos = Vector3.Zero;
        rot = Quaternion.Identity;
        if (!Sample(t, out var a, out var b, out float u, out float extra)) return false;
        pos = a.Pos.Lerp(b.Pos, u) + b.Frame.Vel * extra;
        rot = a.Rot.Slerp(b.Rot, u);
        return true;
    }

    /// <summary>
    /// Poses the puppet as Ghost.Apply does: body, velocity, pedals, revs, steering, springs and
    /// tyres, so CarVisual, TyreEffects and EngineSound treat it like the real thing. Returns the
    /// hardest hit reported since the previous call.
    /// </summary>
    public float Apply(float t, CarSim car, float dt, ref float appliedTime)
    {
        if (!Sample(t, out var a, out var b, out float u, out float extra)) return 0;
        float impact = 0;
        foreach (var s in _s)
            if (s.Time > appliedTime && s.Time <= t) impact = Math.Max(impact, s.Frame.Impact);
        appliedTime = Math.Max(appliedTime, t);
        var n = u < 0.5f ? a : b;
        Ghost.Frame fa = a.Frame, fb = b.Frame, fn = n.Frame;
        car.Pos = a.Pos.Lerp(b.Pos, u) + fb.Vel * extra;
        car.Rot = new Basis(a.Rot.Slerp(b.Rot, u));
        car.Vel = fa.Vel.Lerp(fb.Vel, u);
        car.AngVel = Vector3.Zero;
        car.SteerAngle = Mathf.Lerp(fa.Steer, fb.Steer, u);
        car.Throttle = car.ThrottleTarget = Mathf.Lerp(fa.Throttle, fb.Throttle, u);
        car.Brake = fn.Brake;
        car.Handbrake = fn.Handbrake;
        car.Rpm = Mathf.Lerp(fa.Rpm, fb.Rpm, u);
        car.Gear = fn.Gear;
        car.Slide = Mathf.Lerp(fa.Slide, fb.Slide, u);
        car.BodyScrape = Mathf.Lerp(fa.Scrape, fb.Scrape, u);
        car.Limiter = fn.Limiter;
        car.LooseSurface = fn.Loose;
        car.UpsideDownTime = 0;
        for (int k = 0; k < 4; k++)
        {
            Wheel w = car.Wheels[k];
            Ghost.WheelFrame wa = a.Wheels[k], wb = b.Wheels[k], wn = n.Wheels[k];
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
        return impact;
    }
}
