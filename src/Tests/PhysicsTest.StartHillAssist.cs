using System;
using CCCPRacing.Car;
using CCCPRacing.Track;
using CCCPRacing.Net;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    sealed class StartSlope : IGround
    {
        public Vector2 Gradient;
        public GroundSample Sample(float x,float z) => new() {
            Height=Gradient.X*x+Gradient.Y*z,
            Normal=new Vector3(-Gradient.X,1,-Gradient.Y).Normalized(),Surface=SurfaceType.Concrete };
    }

    void StartHillAssistTest()
    {
        foreach(var gradient in new[]{new Vector2(0,.20f),new Vector2(0,-.20f),new Vector2(.18f,.12f)})
        foreach(float step in new[]{1f/60,1f/240})
        {
            var ground=new StartSlope {Gradient=gradient};var car=new CarSim(new CarParams(),ground);
            car.Reset(new(0,.6f,0),0);car.Clutch=true;car.SetControls(1f,0,0,false,false);
            for(int i=0;i<(int)(4/step);i++)car.Step(step,true);
            Check(new Vector2(car.Pos.X,car.Pos.Z).Length()<.00001f && new Vector2(car.Vel.X,car.Vel.Z).Length()<.00001f,
                $"start hill assist: clutch alone holds slope {gradient} at {1/step:F0} Hz");
            Check(car.Rpm>car.P.IdleRpm+1500 && car.Brake==0 && car.Handbrake==0,
                "start hill assist: engine revs freely without forcing either brake pedal");
            car.Clutch=false;car.Step(step,true);bool kick=car.ClutchKick>0;
            for(int i=0;i<(int)(1/step);i++)car.Step(step,true);
            Check(kick && new Vector2(car.Pos.X,car.Pos.Z).Length()>.10f,
                "start hill assist: clutch release allows a real launch before GO");

            car.Reset(new(0,.6f,0),0);car.Clutch=true;car.SetControls(0f,0,0,false,false);
            for(int i=0;i<(int)(2/step);i++)car.Step(step,true);
            var before=car.Pos;
            for(int i=0;i<(int)(3/step);i++)car.Step(step); // GO: no countdown context, clutch still down.
            Check(new Vector2(car.Pos.X-before.X,car.Pos.Z-before.Z).Length()>.10f,
                "start hill assist: GO releases the hold even while the clutch stays down");
        }
        var slope=new StartSlope {Gradient=new(.1f,.15f)};
        var host=new CarSim(new CarParams(),slope);host.Reset(new(0,.6f,0),0);
        host.Clutch=true;host.SetControls(1f,0,0,false,false);
        for(int i=0;i<240;i++)host.Step(Dt,true);
        var client=new CarSim(new CarParams(),slope);client.RestoreNetwork(host.NetworkState());
        for(int i=0;i<240;i++) {host.Step(Dt,true);client.Step(Dt,true);}
        Check(host.Pos.DistanceTo(client.Pos)<.00001f,"start hill assist: LAN authoritative state replays the held countdown without drift");
        var release=DriverCommand.Of(host,1,false);release.Clutch=false;release.Apply(host);release.Apply(client);
        for(int i=0;i<120;i++) {host.Step(Dt,false);client.Step(Dt,false);}
        Check(host.Pos.DistanceTo(client.Pos)<.00001f && host.Speed>.1f,
            "start hill assist: LAN clutch release and GO replay the same launch");
    }
}
