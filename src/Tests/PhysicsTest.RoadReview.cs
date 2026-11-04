using System;
using System.IO;
using System.Linq;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;
public partial class PhysicsTest
{
    async void RoadReview(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var oldDef=TrackDef.Load("res://tracks/ilyich.json");oldDef.RoadCharacter=null;
            var before=TrackBuilder.Build(oldDef);
            var track=TrackBuilder.Build(TrackDef.Load("res://tracks/ilyich.json"));
            Check(before.Center.SequenceEqual(track.Center) && before.CenterH.SequenceEqual(track.CenterH),"road: racing centreline, grades and jump centre heights unchanged");
            bool protectedSame=true,periodic=true,clearance=true,surface=true;
            float minimum=99,maxAsymmetry=0,maxStep=0;
            for(int i=0;i<track.Center.Length;i++)
            {
                var right=new Vector2(-track.CenterDir(i).Y,track.CenterDir(i).X);
                float left=track.RoadHalfWidth(i,track.Center[i]-right),r=track.RoadHalfWidth(i,track.Center[i]+right);
                minimum=Math.Min(minimum,left+r);maxAsymmetry=Math.Max(maxAsymmetry,Math.Abs(left-r));
                if(i<430 || i>=1005 || Math.Abs(i-905)<=10)
                {
                    protectedSame &= track.HalfWidth[i]==before.HalfWidth[i] && left==r && left==before.HalfWidth[i];
                    foreach(float lateral in new[] {-before.HalfWidth[i]-2,0,before.HalfWidth[i]+2})
                    {
                        var p=track.Center[i]+right*lateral;
                        protectedSame &= Math.Abs(track.MeshHeight(p.X,p.Y)-before.MeshHeight(p.X,p.Y))<.001f;
                    }
                }
                // Production bus lane: half-width * .45; the bus body is 2.5 m wide.
                clearance &= Math.Min(left,r)-track.HalfWidth[i]*.45f-1.25f>.25f;
                int j=(i+1)%track.Center.Length;
                maxStep=Math.Max(maxStep,Math.Abs((track.HalfWidth[i]-before.HalfWidth[i])-(track.HalfWidth[j]-before.HalfWidth[j])));
                periodic &= float.IsFinite(left+r) && left+r>=before.HalfWidth[i]*1.6f-.001f;
            }
            Check(protectedSame,"road: main straight, jump/ford corridor and bus stop keep original widths and shoulder heights");
            Check(before.FordIndex==track.FordIndex && before.WaterLevel==track.WaterLevel,"road: ford geometry reference and water level unchanged");
            Check(periodic && maxStep<.08f,$"road: narrowing at most 20%, smooth added closed-loop variation (max width step {2*maxStep:F3} m)");
            Check(maxAsymmetry>.08f,$"road: independent left/right wear (max {maxAsymmetry:F2} m)");
            Check(clearance,$"road: production bus lane retains body clearance; minimum full road width {minimum:F2} m");
            foreach(var pinch in TrackDef.Load("res://tracks/ilyich.json").RoadCharacter.Pinches)
            {
                int i=(int)((pinch.From+pinch.To)*.5f);
                float ratio=track.HalfWidth[i]/before.HalfWidth[i];
                Check(ratio<.9f && ratio>=.8f,$"road: authored pinch {i} m at {track.Center[i]} narrows {2*before.HalfWidth[i]:F2} -> {2*track.HalfWidth[i]:F2} m before edge wear");
            }
            for(int z=0;z<track.N;z++)for(int x=0;x<track.N;x++)
            {
                var p=new Vector2((x+.5f)*track.Tile,(z+.5f)*track.Tile);
                int ci=track.NearestCenter(p,out float distance);
                if(ci<470 || ci>860 || distance>track.HalfWidth[ci]+3)continue;
                if(track.TileSurface(x,z) is SurfaceType.Water or SurfaceType.Shallow)continue;
                if(distance<track.RoadHalfWidth(ci,p)-.1f)
                    {
                        var actual=track.TileSurface(x,z);
                        surface &= actual==track.CenterSurf[ci] || actual==SurfaceType.Puddle;
                    }
            }
            Check(surface,"road: grip tiles agree with the shaped driving edges");
            // Walk the real bus through the reshaped route and test its four body corners.
            var fx=new CCCPRacing.Car.TyreEffects();AddChild(fx);fx.Init(track);
            var bus=new SovietBus();AddChild(bus);bus.Init(track,fx,500,null,track.Obstacles.Count);
            float busMargin=99;
            for(int i=470;i<1000;i+=2)
            {
                bus.Warp(i);var right=new Vector2(-bus.Forward2D.Y,bus.Forward2D.X);
                foreach(float longitudinal in new[] {-4f,4f})
                foreach(float lateral in new[] {-1.25f,1.25f})
                {
                    var p=bus.Position2D+bus.Forward2D*longitudinal+right*lateral;
                    int ci=track.NearestCenter(p,out float distance);
                    busMargin=Math.Min(busMargin,track.RoadHalfWidth(ci,p)-distance);
                }
            }
            Check(busMargin>=0,$"road: real bus body stays within shaped edges through the village route (minimum margin {busMargin:F2} m)");
            bus.Free();fx.Free();
            Check(TrackSignature.Compute(before)!=TrackSignature.Compute(track),"road: changed competitive width has a distinct record signature");
            IlyichTerrainTest(track);
            var vp=new SubViewport {Size=new Vector2I(1280,720),OwnWorld3D=true,RenderTargetUpdateMode=SubViewport.UpdateMode.Always};AddChild(vp);
            var world=new Node3D();vp.AddChild(world);
            world.AddChild(new WorldEnvironment {Environment=new Godot.Environment {BackgroundMode=Godot.Environment.BGMode.Color,
                BackgroundColor=new Color(.57f,.62f,.61f),AmbientLightSource=Godot.Environment.AmbientSource.Color,
                AmbientLightColor=Colors.White,AmbientLightEnergy=.65f}});
            world.AddChild(new DirectionalLight3D {RotationDegrees=new Vector3(-40,-25,0),LightEnergy=1});
            var camera=new Camera3D {Current=true,Fov=50,Far=1800};world.AddChild(camera);
            foreach(var sample in new[] {("before",before),("after",track)})
            {
                var landscape=new Node3D();world.AddChild(landscape);
                landscape.AddChild(TerrainMesh.Build(sample.Item2));landscape.AddChild(WaterMesh.Build(sample.Item2));
                landscape.AddChild(Props.Build(sample.Item2));landscape.AddChild(KolkhozWorkers.Build(sample.Item2));
                landscape.AddChild(WorldBackdrop.Build(sample.Item2,Enumerable.Repeat(1f,sample.Item2.N*sample.Item2.N).ToArray(),TileAtlas.Build()));
                foreach(int station in new[] {595,805,965})
                {
                    var c=sample.Item2.Center[station];var forward=sample.Item2.CenterDir(station);
                    var right=new Vector2(-forward.Y,forward.X);
                    var at=c-forward*32+right*24;
                    camera.Position=new Vector3(at.X,sample.Item2.CenterH[station]+31,at.Y);
                    camera.LookAt(new Vector3(c.X,sample.Item2.CenterH[station],c.Y));
                    await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                    await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                    using var img=vp.GetTexture().GetImage();
                    Check(img.SavePng(Path.Combine(dir,$"{sample.Item1}-{station}.png"))==Error.Ok,$"road view: {sample.Item1}-{station}");
                }
                landscape.Free();
            }
            vp.Free();
        }
        catch(Exception e){GD.PrintErr(e);_failures++;}
        GetTree().Quit(_failures==0?0:1);
    }
}
