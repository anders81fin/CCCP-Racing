using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using CCCPRacing.Car;
using CCCPRacing.Race;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;
public partial class PhysicsTest
{
    record RidgeFlight(int From,int To,float Seconds,float Distance,float LaunchKmh,float Lateral,bool Landed=true);
    (List<RidgeFlight> Flights,float MinUp) RidgeDrive(TrackData track,string path,int from,int to,float kmh)
    {
        var car=new CarSim(new CarParams(),track){WorldMax=track.WorldSize-4};
        SchoolScript.SetRolling(car,track,SchoolScript.RoadPoint(track,from,0),track.CenterDir(from),kmh/3.6f);
        var flights=new List<RidgeFlight>();int hint=from,start=from,last=from;float air=0,launchSpeed=0,minUp=1;
        Vector3 takeoff=car.Pos;
        using var csv=new StreamWriter(path);csv.WriteLine("time,station,x,y,z,speed_kmh,air,up");
        for(int frame=0;frame<2400;frame++)
        {
            float speed=car.Speed*3.6f;
            car.SetControls(speed<kmh?1:0,speed>kmh+4?.25f:0,SchoolScript.SlideSteer(car,track,ref hint),false,false);
            car.Step(Dt);int at=SchoolScript.Follow(track,new Vector2(car.Pos.X,car.Pos.Z),ref hint);last=at;
            bool airborne=car.Wheels.All(w=>!w.Contact);
            if(airborne)
            {
                if(air==0){start=at;takeoff=car.Pos;launchSpeed=car.Speed*3.6f;}
                air+=Dt;
            }
            else
            {
                if(air>.10f)
                {
                    var p=new Vector2(car.Pos.X,car.Pos.Z);track.NearestCenter(p,out float lateral);
                    flights.Add(new(start,at,air,new Vector2(car.Pos.X-takeoff.X,car.Pos.Z-takeoff.Z).Length(),launchSpeed,lateral));
                }
                air=0;
            }
            minUp=Math.Min(minUp,car.Up.Y);
            if(frame%3==0)csv.WriteLine(FormattableString.Invariant($"{frame*Dt:F3},{at},{car.Pos.X:F3},{car.Pos.Y:F3},{car.Pos.Z:F3},{car.Speed*3.6f:F2},{(airborne?1:0)},{car.Up.Y:F3}"));
            if(at>=to)break;
        }
        if(air>.1f)
        {
            track.NearestCenter(new Vector2(car.Pos.X,car.Pos.Z),out float lateral);
            flights.Add(new(start,last,air,new Vector2(car.Pos.X-takeoff.X,car.Pos.Z-takeoff.Z).Length(),launchSpeed,lateral,false));
        }
        foreach(var f in flights)GD.Print($"ridge flight {Path.GetFileNameWithoutExtension(path)}: {f.From}->{f.To}, {f.Seconds:F2}s, {f.Distance:F1}m, launch {f.LaunchKmh:F1}km/h, landing lateral {f.Lateral:F2}m, landed {f.Landed}");
        GD.Print($"ridge drive {Path.GetFileNameWithoutExtension(path)}: minimum up {minUp:F3}");
        return(flights,minUp);
    }

    async void RidgeReview(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var oldDef=TrackDef.Load("res://tracks/ilyich.json");
            oldDef.Points.Find(p=>p.Z==140).X=288;
            oldDef.Features.RemoveAll(f=>f.At>=210&&f.At<=370);
            oldDef.Features.InsertRange(0,new[]{new FeatureDef{Type="crest",At=251,Length=38,Height=.85f},
                new FeatureDef{Type="crest",At=295,Length=38,Height=1},new FeatureDef{Type="jump",At=335,Length=14,Height=1.3f,Drop=6}});
            var before=TrackBuilder.Build(oldDef);
            var track=TrackBuilder.Build(TrackDef.Load("res://tracks/ilyich.json"));
            var ribbonDef=TrackDef.Load("res://tracks/ilyich.json");foreach(var f in ribbonDef.Features)f.TerrainWidth=0;
            var ribbon=TrackBuilder.Build(ribbonDef);
            Check(track.Center[0].DistanceTo(before.Center[0])<.001f,"ridge: start/finish alignment unchanged; takeoff S-wiggle straightened by 2 m");
            Check(track.CenterH[251]>before.CenterH[251]+3,"ridge: main takeoff substantially higher than former roller");
            Check(track.CenterH[335]<before.CenterH[335]-2,"ridge: former river jump lowered by over two metres");
            var oldFord=before.Center[before.FordIndex];int fi=track.NearestCenter(oldFord,out _);
            float fordDistance=99;
            foreach(int other in new[]{fi-1,fi+1})
            {
                var a=track.Center[fi];var ab=track.Center[other]-a;
                float u=Math.Clamp((oldFord-a).Dot(ab)/ab.LengthSquared(),0,1);
                fordDistance=Math.Min(fordDistance,oldFord.DistanceTo(a+ab*u));
            }
            Check(Math.Abs(track.WaterLevel-before.WaterLevel)<.02f && fordDistance<.01f,$"ridge: ford crossing stays in place (path delta {fordDistance:F4} m, water delta {track.WaterLevel-before.WaterLevel:F4} m)");
            foreach(int station in new[]{245,251,267,281})
            {
                var c=track.Center[station];var d=track.CenterDir(station);var right=new Vector2(-d.Y,d.X);
                foreach(int side in new[]{-1,1})
                {
                    var p=c+right*side*12;float rise=track.MeshHeight(p.X,p.Y)-ribbon.MeshHeight(p.X,p.Y);
                    Check(rise>.5f,$"ridge: real supporting terrain at {station} m, side {side}, 12 m from centre rises {rise:F2} m");
                }
            }
            Check(TrackSignature.Compute(track)!=TrackSignature.Compute(before),"ridge: old lap comparisons are archived under their original layout");
            using(var profile=new StreamWriter(Path.Combine(dir,"profile.csv")))
            {
                profile.WriteLine("station,before,after,road_surface,x,z");
                for(int i=195;i<=390;i++)profile.WriteLine(FormattableString.Invariant($"{i},{before.CenterH[i]:F4},{track.CenterH[i]:F4},{track.MeshHeight(track.Center[i].X,track.Center[i].Y):F4},{track.Center[i].X:F4},{track.Center[i].Y:F4}"));
            }
            foreach(float speed in new[]{60,80,100,120})
            {
                var run=RidgeDrive(track,Path.Combine(dir,$"main-{speed}.csv"),205,325,speed);
                var leap=run.Flights.Where(f=>f.From>=230&&f.From<=260).OrderByDescending(f=>f.Seconds).FirstOrDefault();
                if(speed>=80)
                {
                    Check(leap!=null && leap.Seconds>.45f,$"ridge: {speed} km/h produces a proper rally leap");
                    Check(run.MinUp>.25f,$"ridge: {speed} km/h main sequence remains upright");
                    if(leap!=null)
                    {
                        Check(leap.Landed && leap.To<320 && leap.Lateral<track.HalfWidth[leap.To]-1,$"ridge: {speed} km/h lands on the road before the river approach");
                        float landingGrade=(track.CenterH[leap.To+2]-track.CenterH[leap.To-2])/4;
                        Check(landingGrade<0,$"ridge: {speed} km/h landing receives the car on a downslope (grade {landingGrade:P1})");
                    }
                }
                var river=RidgeDrive(track,Path.Combine(dir,$"river-{speed}.csv"),310,365,speed);
                var riverAir=river.Flights.Where(f=>f.From>=315).Select(f=>f.Seconds).DefaultIfEmpty().Max();
                Check(riverAir<.35f,$"ridge: {speed} km/h former river launch is only a small knoll (air {riverAir:F2}s)");
            }
            IlyichTerrainTest(track);
            if(OS.GetCmdlineUserArgs().Contains("--ridge-images"))
            {
                var vp=new SubViewport{Size=new Vector2I(1280,720),OwnWorld3D=true,RenderTargetUpdateMode=SubViewport.UpdateMode.Always};AddChild(vp);
                var world=new Node3D();vp.AddChild(world);
                world.AddChild(new WorldEnvironment{Environment=new Godot.Environment{BackgroundMode=Godot.Environment.BGMode.Color,BackgroundColor=new Color(.57f,.62f,.61f),AmbientLightSource=Godot.Environment.AmbientSource.Color,AmbientLightColor=Colors.White,AmbientLightEnergy=.65f}});
                world.AddChild(new DirectionalLight3D{RotationDegrees=new Vector3(-35,-40,0),LightEnergy=1});
                var camera=new Camera3D{Current=true,Fov=50,Far=1800};world.AddChild(camera);
                foreach(var sample in new[]{("before",before),("after",track)})
                {
                    var landscape=new Node3D();world.AddChild(landscape);landscape.AddChild(TerrainMesh.Build(sample.Item2));
                    landscape.AddChild(WaterMesh.Build(sample.Item2));landscape.AddChild(Props.Build(sample.Item2));
                    foreach(var shot in new[]{("main",new Vector3(235,45,150),new Vector3(290,7,150)),("river",new Vector3(255,22,230),new Vector3(280,6,223)),("ridge-side",new Vector3(260,18,118),new Vector3(291,9,139))})
                    {
                        camera.Position=shot.Item2;camera.LookAt(shot.Item3);
                        await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                        using var img=vp.GetTexture().GetImage();Check(img.SavePng(Path.Combine(dir,$"{sample.Item1}-{shot.Item1}.png"))==Error.Ok,$"ridge view: {sample.Item1}-{shot.Item1}");
                    }
                    landscape.Free();
                }
                vp.Free();
            }
        }
        catch(Exception e){GD.PrintErr(e);_failures++;}
        GetTree().Quit(_failures==0?0:1);
    }
}
