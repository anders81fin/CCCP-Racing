using System.IO;
using Godot;
namespace CCCPRacing.Car;
// Complete integrator state for authoritative corrections followed by input replay.
public partial class CarSim
{
    const uint HandbrakeStateTag = 0x31424843; // CHB1: optional tail after the legacy integrator state
    public bool PadSteering => _padSteer;
    /// <summary>Explicit start/service hold, excluding the gearbox's automatically released idle N.</summary>
    public bool ExplicitNeutral => _neutral;
    public void SetNetworkSteering(bool pad) => _padSteer=pad;
    public byte[] NetworkState()
    {
        using var m = new MemoryStream(); using var w = new BinaryWriter(m);
        void V(Vector3 v) { w.Write(v.X); w.Write(v.Y); w.Write(v.Z); }
        V(Pos); V(Vel); V(AngVel); V(Rot.X); V(Rot.Y); V(Rot.Z);
        w.Write(DriveAcceleration); w.Write(BodyScrape); w.Write(Slide);
        w.Write(ThrottleTarget);
        w.Write(Brake);
        w.Write(SteerIn);
        w.Write(Handbrake);
        w.Write(Throttle);
        w.Write(Rpm);
        w.Write(SteerAngle);
        w.Write(PowerScale);
        w.Write(Overdrive);
        w.Write(UpsideDownTime);
        w.Write(AirTime);
        w.Write(OffRoadTime);
        w.Write(_shiftTimer);
        w.Write(_limiterCut);
        w.Write(_limiterHold);
        w.Write(_limiterTime);
        w.Write(_dumpTimer);
        w.Write(_clutchDrag);
        w.Write(_landing);
        w.Write(_rotBoost);
        w.Write(_flickSide);
        w.Write(_flickAge);
        w.Write(_techSlideT);
        w.Write(Spun);
        w.Write(_neutral);
        w.Write(_autoNeutral);
        w.Write(Clutch);
        w.Write(Limiter);
        w.Write(_clutchWas);
        w.Write(_padSteer);
        w.Write(_kickNow);
        w.Write(_clutchSlide);
        w.Write(Gear);
        w.Write(_bankHint);
        foreach (var a in Wheels) {
            w.Write(a.Compression);
            w.Write(a.PrevCompression);
            w.Write(a.Load);
            w.Write(a.SlipLat);
            w.Write(a.SlipLong);
            w.Write(a.SpinSpeed);
            w.Write(a.SpinAngle);
            w.Write(a.VisualLength);
            w.Write(a.HitCooldown);
            w.Write(a.Contact);
            w.Write(a.Spinning);
            w.Write(a.Locked);
            w.Write((byte)a.Surface); V(a.ContactPoint); V(a.ContactVel);
        }
        byte held = 0;
        for (int i = 0; i < Wheels.Length; i++)
            if (Wheels[i].HandbrakeHeld) held |= (byte)(1 << i);
        w.Write(HandbrakeStateTag); w.Write(held);
        return m.ToArray();
    }
    public void RestoreNetwork(byte[] bytes)
    {
        using var r = new BinaryReader(new MemoryStream(bytes));
        Vector3 V() => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        Pos=V(); Vel=V(); AngVel=V(); Rot=new Basis(V(),V(),V());
        DriveAcceleration=r.ReadSingle(); BodyScrape=r.ReadSingle(); Slide=r.ReadSingle();
        ThrottleTarget=r.ReadSingle();
        Brake=r.ReadSingle();
        SteerIn=r.ReadSingle();
        Handbrake=r.ReadSingle();
        Throttle=r.ReadSingle();
        Rpm=r.ReadSingle();
        SteerAngle=r.ReadSingle();
        PowerScale=r.ReadSingle();
        Overdrive=r.ReadSingle();
        UpsideDownTime=r.ReadSingle();
        AirTime=r.ReadSingle();
        OffRoadTime=r.ReadSingle();
        _shiftTimer=r.ReadSingle();
        _limiterCut=r.ReadSingle();
        _limiterHold=r.ReadSingle();
        _limiterTime=r.ReadSingle();
        _dumpTimer=r.ReadSingle();
        _clutchDrag=r.ReadSingle();
        _landing=r.ReadSingle();
        _rotBoost=r.ReadSingle();
        _flickSide=r.ReadSingle();
        _flickAge=r.ReadSingle();
        _techSlideT=r.ReadSingle();
        Spun=r.ReadBoolean();
        _neutral=r.ReadBoolean();
        _autoNeutral=r.ReadBoolean();
        Clutch=r.ReadBoolean();
        Limiter=r.ReadBoolean();
        _clutchWas=r.ReadBoolean();
        _padSteer=r.ReadBoolean();
        _kickNow=r.ReadBoolean();
        _clutchSlide=r.ReadBoolean();
        Gear=r.ReadInt32(); _bankHint=r.ReadInt32();
        foreach (var a in Wheels) {
            a.Compression=r.ReadSingle();
            a.PrevCompression=r.ReadSingle();
            a.Load=r.ReadSingle();
            a.SlipLat=r.ReadSingle();
            a.SlipLong=r.ReadSingle();
            a.SpinSpeed=r.ReadSingle();
            a.SpinAngle=r.ReadSingle();
            a.VisualLength=r.ReadSingle();
            a.HitCooldown=r.ReadSingle();
            a.Contact=r.ReadBoolean();
            a.Spinning=r.ReadBoolean();
            a.Locked=r.ReadBoolean();
            a.HandbrakeHeld=false; // legacy snapshots must not retain a previous correction's latch
            a.Surface=(CCCPRacing.Track.SurfaceType)r.ReadByte(); a.ContactPoint=V(); a.ContactVel=V();
        }
        // Existing snapshots end here. Keep their prefix and framing readable; new peers additionally
        // preserve the brake's hysteresis even at rest, where the visual Locked flag is false.
        if (r.BaseStream.Position < r.BaseStream.Length)
        {
            if (r.BaseStream.Length-r.BaseStream.Position != 5 || r.ReadUInt32()!=HandbrakeStateTag)
                throw new InvalidDataException("Unknown car integrator state extension");
            byte held=r.ReadByte();
            if ((held & ~((1 << Wheels.Length)-1)) != 0)
                throw new InvalidDataException("Invalid handbrake state mask");
            for (int i=0;i<Wheels.Length;i++) Wheels[i].HandbrakeHeld=(held & (1 << i))!=0;
        }
    }
    public void NetworkImpact(float speed, Vector3 point, Vector3 normal) => NoteImpact(speed, point-Pos, normal, false, false);
}
