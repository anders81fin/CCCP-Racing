using System;
using System.IO;
using CCCPRacing.Car;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;
public partial class PhysicsTest
{
    async void ModelReview(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var vp = new SubViewport { Size = new Vector2I(960, 600), OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
            AddChild(vp);
            var world = new Node3D(); vp.AddChild(world);
            world.AddChild(new WorldEnvironment { Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(.26f,.29f,.31f), AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = Colors.White, AmbientLightEnergy = .7f, TonemapMode = Godot.Environment.ToneMapper.Linear } });
            world.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-40,-25,0), LightEnergy = 1.1f, ShadowEnabled = false });
            var camera = new Camera3D { Current = true, Fov = 45, Near = .05f }; world.AddChild(camera);
            var parameters = new CarParams(); var ground = new FlatGround();
            var car = new CarSim(parameters, ground); car.Reset(new Vector3(0,.64f,0),0);
            var visual = CarVisual.Create(parameters,new Color(.7f,.69f,.12f)); world.AddChild(visual); visual.Sync(car,ground);
            foreach (var shot in new[] { ("car-left",new Vector3(-6,.5f,0),new Vector3(0,.7f,0)),
                ("car-right",new Vector3(6,.5f,0),new Vector3(0,.7f,0)),
                ("car-front",new Vector3(-3,1,-5),new Vector3(0,.65f,0)),
                ("car-floor",new Vector3(-2,-4,-3),new Vector3(0,-.35f,0)),
                ("car-wing",new Vector3(-2.6f,2.7f,4.4f),new Vector3(0,1.7f,1.8f)) })
            {
                camera.Position = shot.Item2; camera.LookAt(shot.Item3);
                await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                Check(vp.GetTexture().GetImage().SavePng(Path.Combine(dir,shot.Item1+".png")) == Error.Ok,"production model review: "+shot.Item1);
            }
            visual.Free();
            var dacha=PartyDacha.Building();world.AddChild(dacha);
            var dachaGround=new MeshInstance3D {Mesh=new PlaneMesh {Size=new Vector2(32,28)},
                Position=new Vector3(0,-.01f,0),MaterialOverride=Props.Mat(new Color(.25f,.34f,.17f))};world.AddChild(dachaGround);
            foreach(var shot in new[] {("dacha-front",new Vector3(25,14,29),new Vector3(0,4,3)),
                ("dacha-balcony",new Vector3(13,9,17),new Vector3(0,5.5f,7.5f)),
                ("dacha-footings",new Vector3(17,2,19),new Vector3(0,.7f,8))})
            {
                camera.Position=shot.Item2;camera.LookAt(shot.Item3);
                await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                using var img=vp.GetTexture().GetImage();
                Check(img.SavePng(Path.Combine(dir,shot.Item1+".png"))==Error.Ok,"production model review: "+shot.Item1);
            }
            dacha.Free();dachaGround.Free();
            var hind = HindSorties.BuildReplayAircraft("24"); world.AddChild(hind);
            foreach (var shot in new[] { ("hind-side",new Vector3(17,4,0),new Vector3(0,2.5f,0)),
                ("hind-mounts",new Vector3(12,.6f,-10),new Vector3(0,1.9f,-.4f)) })
            {
                camera.Position = shot.Item2; camera.LookAt(shot.Item3);
                await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                Check(vp.GetTexture().GetImage().SavePng(Path.Combine(dir,shot.Item1+".png")) == Error.Ok,"production model review: "+shot.Item1);
            }
            hind.Free();
            var track = TrackBuilder.Build(TrackDef.Load("res://tracks/bonus.json"));
            var fx = new TyreEffects(); fx.Init(track); world.AddChild(fx);
            var tanks = new TankBattle(); world.AddChild(tanks); var tr = track.TankRange;
            tanks.Init(track,fx,new Rect2(tr.X0,tr.Z0,tr.X1-tr.X0,tr.Z1-tr.Z0),tr.Tanks);
            var hull = tanks.FindChild("T-72",true,false) as Node3D;
            hull.Reparent(world); hull.Transform = Transform3D.Identity;
            foreach (var shot in new[] { ("tank-number-side",new Vector3(5,2.0f,.4f)),
                ("tank-number-rear",new Vector3(5,2.2f,5)), ("tank-number-front",new Vector3(5,2.1f,-5)) })
            {
                camera.Position = shot.Item2; camera.LookAt(new Vector3(0,1.8f,.4f));
                await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                Check(vp.GetTexture().GetImage().SavePng(Path.Combine(dir,shot.Item1+".png")) == Error.Ok,"production model review: "+shot.Item1);
            }
            // Representative moving-track trail through the shared live/replay emitter.
            fx.Position = Vector3.Up * -track.MeshHeight(0,0);
            camera.Position = new Vector3(7,3,7); camera.LookAt(new Vector3(0,.5f,2));
            for (int i = 0; i < 12; i++)
            {
                TankBattle.DustFx(fx,track,new Vector2(0,4+i*.24f));
                fx.Tick(1f/30,camera.GlobalBasis);
            }
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            using (var dust = vp.GetTexture().GetImage())
                Check(dust.SavePng(Path.Combine(dir,"tank-dust.png")) == Error.Ok,"production shared tank dust review");
            Check(fx.ParticleCount > 0,"shared tank dust emits visible particles");
            fx.ResetPlaybackEmission();
            fx.Tick(0,camera.GlobalBasis);
            Check(fx.ParticleCount == 0,"tank dust seek/reset clears particles");
            vp.Free();
            TapeT21(); // Existing production emitters: speed, stationary hull, seek discontinuity and cleanup.
        }
        catch (Exception e) { GD.PrintErr(e); _failures++; }
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }
}
