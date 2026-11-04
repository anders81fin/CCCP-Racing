using System;
using Godot;

namespace CCCPRacing.Track;

public partial class TankBattle
{
    static void BuildRunningGear(Tank k,Color olive,Color dark,Color steel)
    {
        // Separate rigid batches retain their animated transforms in LAN replicas too.
        foreach(float side in new[]{-1f,1f})
        {
            for(int index=0;index<6;index++)
            {
                var wheel=new Node3D {Name=$"Wheel{(side<0?"L":"R")}{index}",Position=new(side*1.93f,.46f,-2.6f+index*1.04f)};
                k.Hull.AddChild(wheel); k.Wheels.Add(wheel);
                wheel.AddChild(Props.Part(Props.Cyl(.43f,.2f,14),dark,Vector3.Zero,new(0,0,90)));
                wheel.AddChild(Props.Part(Props.Cyl(.335f,.035f,12),olive.Darkened(.12f),new(side*.12f,0,0),new(0,0,90)));
                wheel.AddChild(Props.Part(Props.Cyl(.245f,.045f,10),olive,new(side*.145f,0,0),new(0,0,90)));
                wheel.AddChild(Props.Part(Props.Cyl(.105f,.075f,10),steel,new(side*.175f,0,0),new(0,0,90)));
                for(int bolt=0;bolt<6;bolt++)
                {
                    float a=bolt*MathF.Tau/6;
                    wheel.AddChild(Props.Part(Props.Box(.03f,.05f,.05f),steel,new(side*.19f,MathF.Sin(a)*.18f,MathF.Cos(a)*.18f)));
                }
                DrunkTractor.BatchStatic(wheel,null);
            }
            foreach(float height in new[]{.875f,.045f})
            {
                var belt=new Node3D {Name=$"{(side<0?"Left":"Right")}{(height>.5f?"Upper":"Lower")}Tread"};k.Hull.AddChild(belt);k.Belts.Add(belt);
                for(int link=0;link<25;link++)
                    belt.AddChild(Props.Part(Props.Box(.72f,.055f,.09f),link%4==0?new Color(.24f,.21f,.13f):steel.Darkened(.28f),new(side*1.55f,height,-3.25f+link*.289f)));
                DrunkTractor.BatchStatic(belt,null);
            }
        }
        k.Flash=new MeshInstance3D {Name="MuzzleFlash",Position=new(0,0,-5.35f),Visible=false,
            Mesh=new SphereMesh {Radius=.38f,Height=1.4f,RadialSegments=8,Rings=4},RotationDegrees=new(90,0,0),
            MaterialOverride=NightGlow.Keep(new StandardMaterial3D {AlbedoColor=new(1,.82f,.42f),ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded})};
        k.Barrel.AddChild(k.Flash);
    }
}
