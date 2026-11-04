using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using CCCPRacing.Car;
using CCCPRacing.Audio;
using CCCPRacing.Race;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;
public partial class PhysicsTest
{
    async void KozlikScaleReview(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);const string key="Kozlik:Kozlik";
            var track=TrackBuilder.Build(TrackDef.Load("res://tracks/ilyich.json"));
            var fx=new TyreEffects();AddChild(fx);fx.Init(track);
            KozlikPatrol Make(){var p=new KozlikPatrol();AddChild(p);p.Init(track,fx,null,null,220);return p;}
            WorldTapeIndex Index(KozlikPatrol p)=>new(new[]{(key,(Node3D)p)});
            Node3D Part(WorldTapeIndex idx,string name)=>Enumerable.Range(0,idx.Count).Select(idx.NodeOf).First(n=>n.HasMeta("tape_part") && n.GetMeta("tape_part").AsString()==name);
            var source=Make();var index=Index(source);
            Check(Math.Abs(source.Body.Scale.X-1.05f)<.0001f && index.StructureOf(key).Version==2,"kozlik scale: production model is 1.05 and tape version is 2");
            Aabb Bounds(Node3D root,params Node[] skip)
            {
                bool first=true;Aabb result=default;
                void Walk(Node n,Transform3D parent)
                {
                    if(skip.Contains(n))return;
                    var xf=n is Node3D node?parent*node.Transform:parent;
                    if(n is MeshInstance3D m && m.Mesh!=null && m.Visible)
                    {
                        var box=m.Mesh.GetAabb();
                        for(int k=0;k<8;k++)
                        {
                            var v=xf*box.GetEndpoint(k);
                            if(first){result=new Aabb(v,Vector3.Zero);first=false;}else result=result.Expand(v);
                        }
                    }
                    foreach(Node child in n.GetChildren())Walk(child,xf);
                }
                Walk(root,Transform3D.Identity);return result;
            }
            var vehicleBounds=Bounds(source.Body,Part(index,"bear"),Part(index,"driverHead").GetParent(),Part(index,"beacon"));
            Check(Math.Abs(vehicleBounds.Size.X-2.0475f)<.02f && Math.Abs(vehicleBounds.Size.Z-4.578f)<.03f,$"kozlik scale: actual vehicle mesh {vehicleBounds.Size.X:F3} m wide x {vehicleBounds.Size.Z:F3} m long");
            // Init adds exactly three active hull circles and all are posed by Tick, not by this test.
            var hull=track.Obstacles.TakeLast(3).ToArray();
            Check(hull.All(o=>Math.Abs(o.Radius-1.02375f)<.0001f) && Math.Abs(hull[0].Center.DistanceTo(hull[2].Center)-2.52f)<.001f,
                "kozlik scale: production collision circles match 2.048 m width and 2.52 m axle span");
            var rec=new WorldTapeRecorder();rec.Begin(index);var ghost=new Ghost();
            var car=new CarSim(new CarParams(),track);car.Reset(track.StartPos,track.StartYaw);
            var poses=new Dictionary<int,Transform3D[]>();var soundSamples=new Dictionary<int,float>();
            var sourceSound=source.Body.GetParent().GetChildren().OfType<DieselSound>().Single();
            for(int n=0;n<=960;n++)
            {
                if(n>0)source.Tick(1f/120,null,0);
                int prior=rec.Samples;rec.Record(n/120f);ghost.Record(n/120f,car,0,0,0);
                if(n%12==0)soundSamples[n/12]=sourceSound.Speed;
                if(rec.Samples>prior)poses[rec.Samples-1]=Enumerable.Range(0,index.Count).Select(i=>index.NodeOf(i).Transform).ToArray();
            }
            var saved=ghost.CopyWithTime(8);var tape=rec.Finish(8,WorldTapeFingerprint.Of(saved));tape.TrackId="ilyich";
            var pair=WorldTapeFile.SavePair(dir+"/ghost_ilyich.bin",dir+"/tape_ilyich.bin",saved,tape,WorldTapeRealOps.Instance,WorldTapeCodec.Encode,WorldTapeCodec.Decode);
            var loadedGhost=Ghost.Load(dir+"/ghost_ilyich.bin",new CarParams(),track);
            var loaded=WorldTapeFile.Load(dir+"/tape_ilyich.bin",loadedGhost,WorldTapeCodec.Decode);
            foreach(var counter in rec.CountersByPart())GD.Print($"kozlik tape diagnostic: {counter.Part}, abrupt turns {counter.Unregistered}, scale changes {counter.Unscaled}");
            bool wheelsSafe=rec.CountersByPart().All(c=>!c.Part.StartsWith("wheel") || c.Unregistered==0);
            Check(pair.Whole && loaded!=null && rec.UnscaledChanges==0 && wheelsSafe,"kozlik scale: production pair saves/loads, no changing scales or unregistered wheel spins");
            var target=Make();var targetIndex=Index(target);var player=new WorldTapePlayer(loaded,targetIndex,new TapeSink());
            bool matches=player.Covers(key),soundMatches=true;float worst=0;
            var targetSound=target.Body.GetParent().GetChildren().OfType<DieselSound>().Single();
            foreach(var sample in poses.Keys.Order())
            {
                player.Apply(sample/WorldTapeConst.TapeRate,1/WorldTapeConst.TapeRate);
                int soundFrame=(int)MathF.Floor(sample/WorldTapeConst.TapeRate*10+.001f);
                soundMatches &= Math.Abs(targetSound.Speed-soundSamples[soundFrame])<.3f;
                for(int i=0;i<targetIndex.Count;i++)
                {
                    var actual=targetIndex.NodeOf(i).Transform;var want=poses[sample][i];
                    worst=Math.Max(worst,actual.Origin.DistanceTo(want.Origin));
                    matches &= actual.Origin.DistanceTo(want.Origin)<.004f && actual.Basis.X.DistanceTo(want.Basis.X)<.006f && actual.Basis.Y.DistanceTo(want.Basis.Y)<.006f;
                }
            }
            Check(matches,$"kozlik scale: saved playback matches every production part over 8 s (max position error {worst:F5} m)");
            Check(soundMatches,"kozlik scale: saved engine sound state follows the recorded production speed");
            foreach(int sample in new[]{0,15,90,30,105,0,60})
            {
                player.Seek(sample/WorldTapeConst.TapeRate);bool same=true;
                for(int i=0;i<targetIndex.Count;i++)same &= targetIndex.NodeOf(i).Position.DistanceTo(poses[sample][i].Origin)<.004f;
                Check(same && Math.Abs(target.Body.Scale.X-1.05f)<.0001f,$"kozlik scale: seek/loop to {sample/15f:F1} s preserves parts and new scale");
            }
            player.Release();target.Reset();var oldPos=target.Position2D;target.Tick(.1f,null,0);
            Check(target.Position2D.DistanceTo(oldPos)>.01f && target.Body.Scale.X==1.05f,"kozlik scale: replay release/reset returns to live movement at the new scale");
            var legacy=Make();legacy.Body.Scale=Vector3.One*1.2f;Tape.Version(legacy,1);
            var oldRec=new WorldTapeRecorder();oldRec.Begin(Index(legacy));oldRec.Record(0);
            var oldTape=WorldTapeCodec.Decode(WorldTapeCodec.Encode(oldRec.Finish(1,1)));
            var oldPlayer=new WorldTapePlayer(oldTape,targetIndex,new TapeSink());var preserved=target.Body.Transform;
            oldPlayer.Seek(0);
            Check(!oldPlayer.Covers(key) && target.Body.Transform==preserved,"kozlik scale: legacy version-1/1.2-scale recording is rejected without applying the old size");oldPlayer.Release();
            if(OS.GetCmdlineUserArgs().Contains("--kozlik-scale-images"))
            {
                var vp=new SubViewport{Size=new Vector2I(1280,720),OwnWorld3D=true,RenderTargetUpdateMode=SubViewport.UpdateMode.Always};AddChild(vp);
                var world=new Node3D();vp.AddChild(world);
                world.AddChild(new WorldEnvironment{Environment=new Godot.Environment{BackgroundMode=Godot.Environment.BGMode.Color,BackgroundColor=new Color(.35f,.4f,.43f),AmbientLightSource=Godot.Environment.AmbientSource.Color,AmbientLightColor=Colors.White,AmbientLightEnergy=.7f}});
                world.AddChild(new DirectionalLight3D{RotationDegrees=new Vector3(-40,-25,0),LightEnergy=1});
                world.AddChild(new MeshInstance3D{Mesh=new PlaneMesh{Size=new Vector2(22,22)},MaterialOverride=Props.Mat(new Color(.27f,.32f,.23f))});
                source.Reset();source.Reparent(world);legacy.Reparent(world);
                var sourceModel=source.Body.GetParent<Node3D>();var legacyModel=legacy.Body.GetParent<Node3D>();
                sourceModel.GlobalTransform=new Transform3D(Basis.Identity,new Vector3(0,0,0));legacyModel.GlobalTransform=new Transform3D(Basis.Identity,new Vector3(4,0,0));
                var parameters=new CarParams();var flat=new FlatGround();var playerCar=new CarSim(parameters,flat);playerCar.Reset(new Vector3(-4,.64f,0),0);
                var visual=CarVisual.Create(parameters,new Color(.7f,.69f,.12f));world.AddChild(visual);visual.Sync(playerCar,flat);
                foreach(var label in new[]{("LADA",-4f),("UAZ 1.05",0f),("OLD UAZ 1.20",4f)})
                    world.AddChild(new Label3D{Text=label.Item1,Position=new Vector3(label.Item2,4.7f,0),FontSize=60,PixelSize=.008f,Billboard=BaseMaterial3D.BillboardModeEnum.Enabled});
                var camera=new Camera3D{Current=true,Projection=Camera3D.ProjectionType.Orthogonal,Size=15};world.AddChild(camera);
                foreach(var shot in new[]{("front",new Vector3(0,4,-15)),("overview",new Vector3(8,12,-14))})
                {
                    camera.Position=shot.Item2;camera.LookAt(new Vector3(0,1.4f,0));
                    await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                    using var img=vp.GetTexture().GetImage();Check(img.SavePng(Path.Combine(dir,"compare-"+shot.Item1+".png"))==Error.Ok,"kozlik scale view: "+shot.Item1);
                }
                vp.Free();
                // Label3D releases its render resources on the next frame.
                await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            }
            else {source.Free();legacy.Free();}
            target.Free();fx.Free();
        }
        catch(Exception e){GD.PrintErr(e);_failures++;}
        GetTree().Quit(_failures==0?0:1);
    }
}
