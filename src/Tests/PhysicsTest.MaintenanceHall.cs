using System;
using CCCPRacing.Audio;
using CCCPRacing.Car;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    void MaintenanceHallTest()
    {
        var def=TrackDef.Load("res://tracks/bonus.json");var track=TrackBuilder.Build(def);var site=track.MaintenanceHall;
        Check(site!=null,"Poligon has one authored drive-through maintenance hall");
        if(site==null) return;
        GD.Print($"HALL site={site.Center}, forward={site.Forward}, race metre={track.NearestCenter(site.Center,out _)}");
        bool clearance=true,floor=true,dry=true,trees=true;int samples=0;
        float minObstacle=float.MaxValue,maxCorridor=0;Vector2 worst=default;
        foreach(var p in track.Props)
            if(p.Type is "birch" or "spruce" or "pine" or "redpine") trees &= !site.Contains(new(p.Position.X,p.Position.Z),1);
        for(int i=0;i<track.Center.Length;i++) {
            Vector2 local=site.Local(track.Center[i]);
            if(Math.Abs(local.Y)>site.HalfLength || Math.Abs(local.X)>site.HalfWidth) continue;
            samples++;maxCorridor=Math.Max(maxCorridor,Math.Abs(local.X)+track.HalfWidth[i]+1);clearance &= Math.Abs(local.X)+track.HalfWidth[i]+1<MaintenanceHall.ClearHalfWidth;
            floor &= track.Sample(track.Center[i].X,track.Center[i].Y).Surface==SurfaceType.Concrete;
            dry &= site.Oil(track.Center[i])==0;
            foreach(var o in track.Obstacles) {
                float gap=track.Center[i].DistanceTo(o.Center)-track.HalfWidth[i]-o.Radius;
                if(gap<minObstacle) {minObstacle=gap;worst=o.Center;}
                clearance &= gap>.6f;
            }
            if(track.Sample(track.Center[i].X,track.Center[i].Y).Surface!=SurfaceType.Concrete) GD.Print($"HALL floor sample {i}, local={local}, surface={track.Sample(track.Center[i].X,track.Center[i].Y).Surface}");
        }
        GD.Print($"HALL clearance corridor={maxCorridor}, obstacle gap={minObstacle} at {worst}");
        Check(samples>=20 && clearance,"both open portals and the curved road retain a clear two-car driving corridor");
        Check(floor && dry,"the middle line has worn concrete, no hidden potholes and no unavoidable oil");
        Check(trees,"trees and vegetation leave the complete hall footprint clear");
        bool slippery=true;
        foreach(var stain in MaintenanceHallSite.Stains) {
            Vector2 p=site.World(stain.Center.X,stain.Center.Y);
            slippery &= track.Sample(p.X,p.Y).OilFilm>.99f;
        }
        Check(slippery && site.Oil(site.World(0,0))==0,"only the visible marginal oil patches reduce grip");
        var dryCar=NewCar(new FlatGround());var oilCar=NewCar(new FlatGround {OilFilm=1});
        foreach(var car in new[]{dryCar,oilCar}) {Run(car,1);car.Vel=new(0,0,-20);car.SetControls(false,true,0,false);Run(car,1);}
        Check(oilCar.Speed>dryCar.Speed+1,"an oily slab requires a longer braking distance than dry concrete");
        bool passage=true;
        foreach(float lane in new[]{-1.6f,1.6f}) {
            Vector2 entry=site.World(lane,site.HalfLength+3);
            var car=new CarSim(new CarParams(),track) {Obstacles=track.Obstacles,WorldMax=track.WorldSize-4};
            car.Reset(new(entry.X,track.MeshHeight(entry.X,entry.Y)+.7f,entry.Y),TrackData.YawFor(site.Forward));
            car.Neutral=true;car.Vel=new(site.Forward.X*12,0,site.Forward.Y*12);car.SetControls(false,false,0,false);
            Run(car,4);
            Vector2 end=site.Local(new(car.Pos.X,car.Pos.Z));
            if(end.Y>=-site.HalfLength || car.Speed<6) GD.Print($"HALL lane={lane} end={end} speed={car.Speed}");
            passage &= end.Y<-site.HalfLength && car.Speed>6;
        }
        Check(passage,"real car physics pass through both LAN lanes without hitting walls or equipment");
        var building=MaintenanceHall.Build(track);AddChild(building);
        var roof=building.GetNode<Node3D>("CameraCutawayRoof");var occlusion=new BuildingOcclusion();
        foreach(Node child in roof.GetChildren()) if(child is MeshInstance3D mesh) occlusion.Add(mesh.Mesh,mesh.Transform);
        float ground=track.MeshHeight(site.Center.X,site.Center.Y)-building.Position.Y;
        Check(roof.GetNodeOrNull<SeeThrough>("SeeThrough")!=null
            && occlusion.Blocks(new(0,ground+15,0),new(0,ground+1,0))
            && !occlusion.Blocks(new(0,ground+1,site.HalfLength+4),new(0,ground+1,-site.HalfLength-4)),
            "the roof triggers camera cutaway while both ends remain visibly open");
        bool grounded=true;
        var slab=building.GetNode<MeshInstance3D>("WornConcreteFloor");
        foreach(var vertex in slab.Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array()) {
            var world=building.GlobalTransform*vertex;grounded &= Math.Abs(world.Y-track.MeshHeight(world.X,world.Z)-.025f)<.002f;
        }
        var vertices=slab.Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        for(int i=0;i<vertices.Length;i+=3) {
            Vector3 local=(vertices[i]+vertices[i+1]+vertices[i+2])/3;
            Vector3 world=building.GlobalTransform*local;
            grounded &= Math.Abs(world.Y-track.MeshHeight(world.X,world.Z)-.025f)<.002f;
            grounded &= (vertices[i+2]-vertices[i]).Cross(vertices[i+1]-vertices[i]).Y>=-.0001f;
        }
        Check(grounded,"complete slab triangles follow the terrain without holes or reversed faces");
        Check(MaintenanceHall.BlocksFire(track,site.World(0,-40),site.World(0,40))
            && !MaintenanceHall.BlocksFire(track,site.World(35,-40),site.World(35,40)),"military firing lines avoid the sheltered hall");
        var audio=new EngineSound {Enclosure=1,Speed=16,Rpm=3300,Throttle=.5f};bool sound=true;
        for(int i=0;i<22050;i++) {float v=audio.NextSample();sound &= float.IsFinite(v) && Math.Abs(v)<=1;}
        Check(sound,"concrete joint rumble and short hall reflections remain finite and bounded");audio.Free();
        building.Free();
    }
}
