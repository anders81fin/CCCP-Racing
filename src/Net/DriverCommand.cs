using System;
using System.IO;
using CCCPRacing.Car;
namespace CCCPRacing.Net;

/// <summary>A fixed physics tick of driver intent. Position and forward gears are never client authority.</summary>
public struct DriverCommand
{
    public uint Sequence;
    public float Throttle, Brake, Steer, Handbrake;
    public bool Clutch, Neutral, Pad, Reverse, Horn;
    public bool WaitingForGrid; // local prediction history only; the host derives this from its round state
    public float PoseTime; // local history only: collision replay samples remote cars at this command's time, not today's pose
    public bool Countdown; // local history only
    // Cosmetic owner report only; never changes the host's physics, health or race state.
    public byte BrokenHeadlights;
    public int RepairSerial;
    /// <summary>Cosmetic host relay: a delayed report must not undo a replacement/repair.</summary>
    public byte HeadlightsAfter(byte previous, int repairSerial, bool damageEnabled)
        => !damageEnabled ? (byte)0 : RepairSerial==repairSerial ? BrokenHeadlights : previous;
    public static DriverCommand Of(CarSim c, uint seq, bool horn) => new() {
        Sequence=seq, Throttle=c.ThrottleTarget, Brake=c.Brake, Steer=c.SteerIn,
        Handbrake=c.Handbrake, Clutch=c.Clutch, Neutral=c.ExplicitNeutral, Pad=c.PadSteering, Reverse=c.Gear<0, Horn=horn };
    public void Apply(CarSim c)
    {
        c.ThrottleTarget=Throttle; c.Brake=Brake; c.SteerIn=Steer; c.Handbrake=Handbrake;
        c.Clutch=Clutch; c.Neutral=Neutral;
        c.SetNetworkSteering(Pad);
        if (Math.Abs(c.ForwardSpeed)<2f && Reverse != (c.Gear<0)) c.Gear=Reverse ? -1 : 1;
    }
    public void Write(BinaryWriter w)
    {
        w.Write(Sequence); w.Write(Throttle); w.Write(Brake); w.Write(Steer); w.Write(Handbrake);
        w.Write((byte)((Clutch?1:0)|(Neutral?2:0)|(Pad?4:0)|(Reverse?8:0)|(Horn?16:0)));
        w.Write(BrokenHeadlights); w.Write(RepairSerial);
    }
    public static DriverCommand Read(BinaryReader r)
    {
        var c=new DriverCommand {Sequence=r.ReadUInt32(),Throttle=r.ReadSingle(),Brake=r.ReadSingle(),Steer=r.ReadSingle(),Handbrake=r.ReadSingle()};
        int b=r.ReadByte(); c.Clutch=(b&1)!=0; c.Neutral=(b&2)!=0; c.Pad=(b&4)!=0; c.Reverse=(b&8)!=0; c.Horn=(b&16)!=0;
        c.BrokenHeadlights=r.ReadByte(); c.RepairSerial=r.ReadInt32();
        if(c.BrokenHeadlights>3 || c.RepairSerial<0) throw new InvalidDataException("headlight report");
        if (!float.IsFinite(c.Throttle+c.Brake+c.Steer+c.Handbrake) || c.Throttle<0 || c.Throttle>1 || c.Brake<0 || c.Brake>1 || Math.Abs(c.Steer)>1 || c.Handbrake<0 || c.Handbrake>1) throw new InvalidDataException("driver input");
        return c;
    }
}
