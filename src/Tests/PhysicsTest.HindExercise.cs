using System;
using CCCPRacing.Car;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    void HindExerciseTest()
    {
        RunLiveHindExercise(1f/30);
        RunLiveHindExercise(1f/60);
    }

    void RunLiveHindExercise(float dt)
    {
        GD.Print($"Live bonus exercise at {1/dt:F0} Hz");
        // Keep the real BUK battery and tank exercise; the isolated base test removes the battery.
        var track=TrackBuilder.Build(TrackDef.Load("res://tracks/bonus.json"));
        var fx=new TyreEffects();AddChild(fx);fx.Init(track);
        var control=new BonusExerciseControl();
        var hinds=new HindSorties {Exercise=control};AddChild(hinds);hinds.Init(track,fx);
        var tanks=new TankBattle {Exercise=control};AddChild(tanks);var tr=track.TankRange;
        tanks.Init(track,fx,new(tr.X0,tr.Z0,tr.X1-tr.X0,tr.Z1-tr.Z0),tr.Tanks);
        var car=new CarSim(new CarParams(),track);
        float clock=0,first=-1,warning=-100;int attacks=0,hits=0,complete=0,crashes=0;
        bool warnings=true;
        hinds.AttackApproaching+=_=>warning=clock;
        hinds.AttackStarted+=p=> {
            if(first<0)first=clock;
            warnings&=clock-warning>=BonusExerciseControl.WarningLead-.04f;
            attacks++;hits=0;GD.Print($"live exercise: attack {attacks} at {clock:F1}s, target {p}");
        };
        hinds.CannonImpact+=_=>{if(++hits==HindSorties.BurstRounds)complete++;};
        hinds.Crashed+=_=>crashes++;
        for(int frame=0;frame<(int)(360/dt);frame++)
        {
            clock+=dt;control.Tick(dt);
            int metre=(int)(clock*16)%track.Center.Length;
            car.Pos=track.CenterPos(metre)+Vector3.Up*.6f;
            var direction=track.CenterDir(metre);car.Vel=new(direction.X*16,0,direction.Y*16);
            car.Rot=new Basis(Vector3.Up,TrackData.YawFor(direction));
            tanks.Tick(dt,car);hinds.Tick(dt,car);
        }
        GD.Print($"live exercise: first {first:F1}s, attacks {attacks}, complete bursts {complete}, crashes {crashes}");
        Check(first>0 && first<120,"live bonus exercise: Hind attacks within the first two air windows with tanks and BUK enabled");
        Check(attacks>=3 && complete>=3,"live bonus exercise: repeated complete Hind attack runs occur within six minutes");
        Check(warnings,"live bonus exercise: every attack retains its three-second warning");
        tanks.Free();hinds.Free();fx.Free();
    }
}
