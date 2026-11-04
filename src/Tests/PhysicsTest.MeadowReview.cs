using System;
using System.IO;
using System.Linq;
using CCCPRacing.Track;
using Godot;
namespace CCCPRacing.Tests;
public partial class PhysicsTest
{
    async void MeadowReview(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var oldDef=TrackDef.Load("res://tracks/ilyich.json");oldDef.Meadow=null;
            var before=TrackBuilder.Build(oldDef);
            var track=TrackBuilder.Build(TrackDef.Load("res://tracks/ilyich.json"));
            float roadDelta=0;int changed=0;
            for(int i=0;i<track.Center.Length;i++)
            {
                roadDelta=Math.Max(roadDelta,Math.Abs(before.CenterH[i]-track.CenterH[i]));
                var d=track.CenterDir(i);var side=new Vector2(-d.Y,d.X);
                foreach(float lateral in new[] {-track.HalfWidth[i]-3,0,track.HalfWidth[i]+3})
                {
                    var p=track.Center[i]+side*lateral;
                    roadDelta=Math.Max(roadDelta,Math.Abs(before.MeshHeight(p.X,p.Y)-track.MeshHeight(p.X,p.Y)));
                }
            }
            Check(roadDelta<.001f,$"meadow: complete racing road and shoulders unchanged (max {roadDelta:F4} m)");
            Check(before.FordIndex==track.FordIndex && before.WaterLevel==track.WaterLevel,"meadow: ford and pond water level unchanged");
            float oldEdge=0,newEdge=0,oldInside=0,newInside=0;int edgeCount=0,insideCount=0;
            for(int z=0;z<=track.N;z++)for(int x=0;x<=track.N;x++)
            {
                int k=z*(track.N+1)+x;var p=new Vector2(x*track.Tile,z*track.Tile);
                if(Math.Abs(track.Heights[k]-before.Heights[k])>.25f)changed++;
                if(x<6 || z<6 || x>track.N-6 || z>track.N-6)
                {oldEdge+=before.Heights[k];newEdge+=track.Heights[k];edgeCount++;}
                else if(x>25&&z>25&&x<track.N-25&&z<track.N-25)
                {oldInside+=before.Heights[k];newInside+=track.Heights[k];insideCount++;}
            }
            float oldContrast=oldEdge/edgeCount-oldInside/insideCount,newContrast=newEdge/edgeCount-newInside/insideCount;
            Check(changed>1000,$"meadow: broad off-road variation ({changed} terrain vertices change by over 25 cm)");
            Check(Math.Abs(newContrast)<Math.Abs(oldContrast)*.7f,$"meadow: rim/bowl contrast {oldContrast:F2} -> {newContrast:F2} m");
            float low=99,high=-99;
            for(int z=60;z<=160;z+=4)for(int x=110;x<=245;x+=4)
            {float h=track.Meadow.HeightAt(x,z);low=Math.Min(low,h);high=Math.Max(high,h);}
            Check(high-low>2.5f,$"meadow: broad field profile has rolling relief ({low:F2} .. {high:F2} m)");
            bool joined=true;
            for(int i=0;i<=320;i+=8)
            foreach(var p in new[] {new Vector2(0,i),new Vector2(320,i),new Vector2(i,0),new Vector2(i,320)})
                joined &= Math.Abs(WorldBackdrop.HeightAt(track,p)-track.MeshHeight(p.X,p.Y))<.001f;
            Check(joined,"meadow: backdrop joins every map edge at the exact driving mesh height");
            Check(TrackSignature.Compute(before)!=TrackSignature.Compute(track),"meadow: changed physical off-road layout has a distinct record signature");
            IlyichTerrainTest(track);
            // Inspect the real crop mesh, including transformed fields, rather than only a helper height.
            foreach(var prop in track.Props.Where(p=>p.Type=="wheatfield"))
            {
                var size=prop.Text.Split(';');
                var centre=new Vector2(prop.Position.X,prop.Position.Z);
                var field=Village.WheatField(float.Parse(size[0],System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(size[1],System.Globalization.CultureInfo.InvariantCulture),1922,centre,prop.Rotation,
                    KolkhozWorkers.Layout(track),track,prop.Position.Y);
                var mesh=((MeshInstance3D)field.GetChild(0)).Mesh;
                var arrays=mesh.SurfaceGetArrays(0);
                var vertices=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                var indices=arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
                float rootError=0;
                var basis=new Basis(Vector3.Up,prop.Rotation);
                for(int i=0;i<indices.Length;i+=42)
                {
                    var root=prop.Position+basis*((vertices[indices[i]]+vertices[indices[i+1]])*.5f);
                    rootError=Math.Max(rootError,Math.Abs(root.Y-track.MeshHeight(root.X,root.Z)));
                }
                Check(indices.Length>420 && rootError<.0001f,$"crop roots at {centre}: {indices.Length/42} stalks meet terrain (max {rootError:F6} m)");
                float grade=0;Vector3 steepest=default;float oldGrade=0;
                float width=float.Parse(size[0],System.Globalization.CultureInfo.InvariantCulture),depth=float.Parse(size[1],System.Globalization.CultureInfo.InvariantCulture);
                for(float z=-depth/2;z<=depth/2;z+=1)
                for(float x=-width/2;x<=width/2;x+=1)
                {
                    var p=prop.Position+basis*new Vector3(x,0,z);
                    track.MeshHeight(p.X,p.Z,out var normal);
                    float slope=new Vector2(normal.X,normal.Z).Length()/normal.Y;
                    if(slope>grade){grade=slope;steepest=p;}
                    before.MeshHeight(p.X,p.Z,out var oldNormal);
                    oldGrade=Math.Max(oldGrade,new Vector2(oldNormal.X,oldNormal.Z).Length()/oldNormal.Y);
                }
                Check(grade<.5f,$"cultivated parcel at {centre} has no abrupt terrace (max grade {grade:P1}, was {oldGrade:P1}, at {steepest})");
                field.Free();
            }
            // A rotated field with a deliberately different origin catches local/world height mistakes.
            {
                var field=Village.WheatField(4,4,1922,new Vector2(170,278),.7f,null,track,12);
                var mesh=((MeshInstance3D)field.GetChild(0)).Mesh;
                var arrays=mesh.SurfaceGetArrays(0);var vertices=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                var indices=arrays[(int)Mesh.ArrayType.Index].AsInt32Array();float error=0;
                for(int i=0;i<indices.Length;i+=42)
                {
                    var root=new Vector3(170,12,278)+new Basis(Vector3.Up,.7f)*((vertices[indices[i]]+vertices[indices[i+1]])*.5f);
                    error=Math.Max(error,Math.Abs(root.Y-track.MeshHeight(root.X,root.Z)));
                }
                Check(error<.0001f,"rotated crop preserves world ground height independently of prop origin");field.Free();
            }
            var workers=KolkhozWorkers.Build(track);bool grounded=true;
            foreach(Node3D worker in workers.GetChildren())
                grounded &= Math.Abs(worker.Position.Y-track.MeshHeight(worker.Position.X,worker.Position.Z))<.0001f;
            Check(grounded,"production workers stand on the updated terrain");workers.Free();
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
                landscape.AddChild(Props.Build(sample.Item2));
                landscape.AddChild(KolkhozWorkers.Build(sample.Item2));
                landscape.AddChild(WorldBackdrop.Build(sample.Item2,Enumerable.Repeat(1f,sample.Item2.N*sample.Item2.N).ToArray(),TileAtlas.Build()));
                foreach(var shot in new[] {("overview",new Vector3(435,270,455),new Vector3(160,2,160)),
                    ("meadow",new Vector3(218,38,215),new Vector3(181,3,122)),
                    ("edge",new Vector3(90,42,-65),new Vector3(170,3,65)),
                    ("west-crops",new Vector3(104,track.MeshHeight(104,274)+3,274),new Vector3(123,track.MeshHeight(123,286)+.5f,286)),
                    ("east-crops",new Vector3(295,track.MeshHeight(295,211)+2,211),new Vector3(311,track.MeshHeight(311,224)+.5f,224)),
                    ("south-crops",new Vector3(152,track.MeshHeight(152,267)+2,267),new Vector3(177,track.MeshHeight(177,285)+.5f,285))})
                {
                    camera.Position=shot.Item2;camera.LookAt(shot.Item3);
                    await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                    await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                    using var img=vp.GetTexture().GetImage();
                    Check(img.SavePng(Path.Combine(dir,sample.Item1+"-"+shot.Item1+".png"))==Error.Ok,"meadow view: "+sample.Item1+"-"+shot.Item1);
                }
                landscape.Free();
            }
            vp.Free();
        }
        catch(Exception e){GD.PrintErr(e);_failures++;}
        GetTree().Quit(_failures==0?0:1);
    }
}
