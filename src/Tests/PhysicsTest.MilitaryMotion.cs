using System;
using System.Linq;
using CCCPRacing.Car;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    void MilitaryMotionTest()
    {
        foreach(float yaw in new[]{0f,.7f,MathF.PI,-MathF.PI/2})
            foreach(float turn in new[]{-.2f,.2f})
            {
                float roll=HindSorties.BankForTurn(30,turn);
                var attitude=Basis.FromEuler(new Vector3(0,yaw,roll));
                Vector3 forward=new(-MathF.Sin(yaw),0,-MathF.Cos(yaw));
                Vector3 next=new(-MathF.Sin(yaw+turn*.01f),0,-MathF.Cos(yaw+turn*.01f));
                Vector3 inward=(next-forward).Normalized();
                Vector3 leftWing=attitude*new Vector3(-3,0,0),rightWing=attitude*new Vector3(3,0,0);
                Check(attitude.Y.Dot(inward)>.1f && (turn>0?leftWing.Y<rightWing.Y:rightWing.Y<leftWing.Y),
                    $"Hind heading {yaw:F2}, turn {turn:F1}: lift tilts into the turn and the inner wing lowers");
                var forwardFlight=Basis.FromEuler(new Vector3(-.12f,yaw,roll));
                Check((forwardFlight*new Vector3(0,0,-5)).Y<(forwardFlight*new Vector3(0,0,5)).Y,
                    "Hind forward-flight pitch places the nose below the tail in both turn directions");
            }

        var track=TrackBuilder.Build(TrackDef.Load("res://tracks/bonus.json"));
        var fx=new TyreEffects();AddChild(fx);fx.Init(track);
        var tanks=new TankBattle();AddChild(tanks);
        var tr=track.TankRange;
        tanks.Init(track,fx,new Rect2(tr.X0,tr.Z0,tr.X1-tr.X0,tr.Z1-tr.Z0),tr.Tanks);
        var hull=tanks.GetChildren().OfType<Node3D>().First(n=>n.Name.ToString().StartsWith("T-72"));
        var wheel=hull.GetNode<Node3D>("WheelL0");
        var upper=hull.GetNode<Node3D>("LeftUpperTread");var lower=hull.GetNode<Node3D>("LeftLowerTread");
        var still=hull.Transform;bool idleStable=true,moved=false,opposed=false,flash=false,recoil=false;
        tanks.Fired+=_=>flash=true;
        for(int i=0;i<60*45;i++)
        {
            tanks.Tick(1f/60,null);
            if(i<30 && !hull.Transform.IsEqualApprox(still)) idleStable=false;
            moved|=MathF.Abs(wheel.Rotation.X)>.1f;
            opposed|=upper.Position.Z<-.01f && MathF.Abs(upper.Position.Z+lower.Position.Z)<.0001f;
            foreach(var gun in hull.GetChildren().OfType<Node3D>().SelectMany(n=>n.GetChildren().OfType<Node3D>()))
                if(gun.GetNodeOrNull<MeshInstance3D>("MuzzleFlash")!=null)
                    recoil|=gun.Position.Z>-.99f && gun.Position.Z<=-.68f;
        }
        Check(idleStable,"tank suspension stays still when the vehicle is stationary");
        Check(moved && opposed,"tank road wheels rotate and upper/lower tread runs travel in opposite directions");
        Check(flash && recoil,"tank exercise fires and the barrel recoils inside its hydraulic travel");
        tanks.Reset();Check(wheel.Rotation.IsZeroApprox() && upper.Position.IsZeroApprox(),"restart resets running-gear motion");
        tanks.Free();fx.Free();

        var hind=HindSorties.BuildModel();AddChild(hind);
        Check(hind.GetNode<Node3D>("Gear").GetChildCount()==3 && hind.GetNodeOrNull<MeshInstance3D>("Gun/Flash")!=null,
            "Hind has three articulated gear legs and its muzzle flash belongs to the aiming gun");
        var client=new Node3D();AddChild(client);
        var sender=new CCCPRacing.Net.LanWorld(this);var receiver=new CCCPRacing.Net.LanWorld(client);
        var obstacles=new System.Collections.Generic.List<Obstacle>();
        void Deliver(byte[] bytes,bool mesh) {if(mesh)receiver.MeshIn(bytes);else receiver.StateIn(bytes,obstacles);}
        sender.Capture(new[]{hind},obstacles,Array.Empty<int>(),Deliver);
        int definitions=0;
        hind.GetNode<Node3D>("Gun").Rotation=new(-.4f,.25f,0);
        hind.GetNode<Node3D>("Gear").GetChild<Node3D>(0).Rotation=new(1,0,0);
        sender.Capture(new[]{hind},obstacles,Array.Empty<int>(),(bytes,mesh)=>{if(mesh)definitions++;Deliver(bytes,mesh);});
        Check(receiver.ReplicaCount>10 && definitions==0,"LAN updates articulated Hind poses without resending mesh definitions");
        sender.Free();receiver.Free();client.Free();hind.Free();
    }
}
