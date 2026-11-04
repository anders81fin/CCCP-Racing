using System;
using System.IO;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;
public partial class PhysicsTest
{
    async void RiverReview(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var oldDef = TrackDef.Load("res://tracks/ilyich.json");
            // Baseline authored river before this scenery-only change.
            oldDef.Water.Rivers[0].Points = new[] {
                new[] { 228f, 218f, 7f },
                new[] { 233f, 224f, 8.5f },
                new[] { 238f, 230f, 10.5f },
                new[] { 245f, 236f, 13.5f },
                new[] { 251f, 241.6f, 12f },
                new[] { 256f, 247f, 12f },
                new[] { 261f, 251.7f, 12f },
                new[] { 265.5f, 256f, 10f },
                new[] { 270f, 259.5f, 12.5f },
                new[] { 273f, 264f, 10f },
                new[] { 278f, 269f, 9f },
                new[] { 283f, 279f, 8f },
                new[] { 290f, 285f, 7f },
                new[] { 298f, 283f, 7.5f },
                new[] { 305f, 286f, 6.5f },
                new[] { 310f, 294f, 7f },
                new[] { 315f, 299f, 8f },
                new[] { 320f, 297f, 8f }
            };
            var before = TrackBuilder.Build(oldDef);
            var track = TrackBuilder.Build(TrackDef.Load("res://tracks/ilyich.json"));
            GD.Print($"River layout signature: {TrackSignature.Compute(before)} -> {TrackSignature.Compute(track)} (new crossing; existing record-store layout archival applies)");
            Check(Math.Abs(before.FordIndex-track.FordIndex) <= 3 && Math.Abs(before.WaterLevel-track.WaterLevel) < .15f,
                "river shape: reshaped ford stays at the same crossing and within 15 cm of its water level");
            int changed = 0, jumpChanged = 0;
            for (int offset = -50; offset <= 50; offset++)
            {
                int i = (track.FordIndex + offset + track.Center.Length) % track.Center.Length;
                var direction = track.CenterDir(i); var side = new Vector2(-direction.Y,direction.X);
                for (float lateral = -track.HalfWidth[i]-2; lateral <= track.HalfWidth[i]+2; lateral += 1)
                {
                    var p = track.Center[i]+side*lateral;
                    if (Math.Abs(before.MeshHeight(p.X,p.Y)-track.MeshHeight(p.X,p.Y)) > .0001f
                        || before.Sample(p.X,p.Y).Surface != track.Sample(p.X,p.Y).Surface) { changed++; if (offset < -20) jumpChanged++; }
                }
            }
            Check(jumpChanged == 0,$"river shape: jump approach unchanged ({jumpChanged} changed samples); reshaped ford corridor has {changed} updated samples");
            var a = oldDef.Water.Rivers[0].Points; var b = track.Water.Rivers[0].Points;
            int start = Array.FindIndex(a,p => p[0] == 265.5f);
            int current = Array.FindIndex(b,p => p[0] == 265.5f);
            bool same = a.Length-start == b.Length-current;
            for (int i=start; i<a.Length && same; i++)
                for (int k=0;k<a[i].Length;k++) same &= a[i][k] == b[current+i-start][k];
            Check(same,"river shape: accepted downstream centre line and base widths unchanged");
            var plain = new RiverDef {Points=new[] {new[] {0f,0f,8f},new[] {40f,0f,8f}}};
            var varied = new RiverDef {Points=new[] {new[] {0f,0f,8f,1.2f},new[] {40f,0f,8f,1.2f}}};
            float leftMin=99,leftMax=0,rightMin=99,rightMax=0,asymmetry=0;
            for(int x=4;x<=36;x+=2)
            {
                float Shore(float sign)
                {
                    float lo=0,hi=6;
                    for(int j=0;j<20;j++){float mid=(lo+hi)*.5f;if(varied.EdgeDistance(new Vector2(x,sign*mid))<0)lo=mid;else hi=mid;}
                    return (lo+hi)*.5f;
                }
                float l=Shore(1),r=Shore(-1);
                leftMin=Math.Min(leftMin,l);leftMax=Math.Max(leftMax,l);
                rightMin=Math.Min(rightMin,r);rightMax=Math.Max(rightMax,r);asymmetry=Math.Max(asymmetry,Math.Abs(l-r));
            }
            Check(leftMax-leftMin>.5f && rightMax-rightMin>.5f && asymmetry>.3f,
                "river banks: both shorelines wander independently instead of straightening the opposite bank");
            Check(plain.EdgeDistance(new Vector2(10,4))==0 && plain.MaxWidth==8,
                "river banks: definitions without variation retain their original footprint");
            var inset = new RiverDef {Points=new[] {new[] {0f,0f,8f,0f,2f},new[] {40f,0f,8f,0f,2f}}};
            Check(inset.EdgeDistance(new Vector2(20,2))==0 && inset.EdgeDistance(new Vector2(20,-4))==0
                && plain.EdgeDistance(new Vector2(20,4))==0,
                "one-bank correction: requested bank moves inward two metres, opposite bank remains exactly fixed");
            FordTest();
            var vp = new SubViewport {Size=new Vector2I(1280,720),OwnWorld3D=true,RenderTargetUpdateMode=SubViewport.UpdateMode.Always};
            AddChild(vp);
            var world = new Node3D(); vp.AddChild(world);
            world.AddChild(new WorldEnvironment {Environment=new Godot.Environment {BackgroundMode=Godot.Environment.BGMode.Color,
                BackgroundColor=new Color(.52f,.57f,.57f),AmbientLightSource=Godot.Environment.AmbientSource.Color,
                AmbientLightColor=Colors.White,AmbientLightEnergy=.8f}});
            world.AddChild(new DirectionalLight3D {RotationDegrees=new Vector3(-50,-25,0),LightEnergy=.8f});
            world.AddChild(TerrainMesh.Build(track));
            var water=WaterMesh.Build(track);world.AddChild(water);
            int deepPads=0,deepStems=0;bool shallowClear=true;
            var lilies=water.FindChild("Water lilies",true,false) as MeshInstance3D;
            if(lilies!=null)
            {
                var vertices=lilies.Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                for(int i=0;i<vertices.Length;i+=30)
                {
                    var p=new Vector2(vertices[i].X,vertices[i].Z);
                    int ci=track.NearestCenter(p,out float rd);
                    if(rd>=track.HalfWidth[ci])continue;
                    deepPads++;shallowClear &= WaterMesh.DeepFordPlantSite(track,p);
                }
            }
            var stalks=water.FindChild("Reed stalks",true,false) as MultiMeshInstance3D;
            if(stalks!=null)for(int i=0;i<stalks.Multimesh.InstanceCount;i++)
            {
                var pose=stalks.Multimesh.GetInstanceTransform(i);
                var p=new Vector2(pose.Origin.X,pose.Origin.Z);
                int ci=track.NearestCenter(p,out float rd);
                if(rd>=track.HalfWidth[ci] || p.DistanceTo(track.Center[track.FordIndex])>8)continue;
                deepStems++;shallowClear &= track.WaterLevel-track.MeshHeight(p.X,p.Y)>.22f;
            }
            var pondStalks=water.GetNodeOrNull<MultiMeshInstance3D>("Pond shoreline reeds/Reed stalks");
            bool pondClear=true;int pondCount=pondStalks?.Multimesh.InstanceCount??0;
            if(pondStalks!=null)for(int i=0;i<pondCount;i++)
            {
                var origin=pondStalks.Multimesh.GetInstanceTransform(i).Origin;
                var p=new Vector2(origin.X,origin.Z);float radius=p.DistanceTo(PartyRowboat.PondCentre);
                pondClear &= radius>16 && radius<19.5f && !(p.Y<197.5f && Math.Abs(p.X-220)<4);
                for(int second=0;second<126;second+=2)
                    pondClear &= p.DistanceTo(PartyRowboat.PositionAt(second))>3.5f
                        && p.DistanceTo(PartyPondDuck.PositionAt(second))>1.5f;
            }
            Check(pondCount>=48 && pondClear,$"pond shoreline: {pondCount} reed stems near banks, pier/boat/duck routes clear");
            Check(deepPads>=2 && deepStems>=6,$"deep ford plants: {deepPads} lily pads and {deepStems} reed stems in the crossing");
            Check(shallowClear,"deep ford plants: the fast shallow line stays free of added plants");
            var scenery=Props.IlyichEdgeScenery(track);world.AddChild(scenery);
            int pondPatchVertices=0;bool pondPatchesDry=true;
            var pondMeadow=scenery.GetNode<Node3D>("Pond shoreline meadow");
            foreach(var child in pondMeadow.GetChildren())if(child is MeshInstance3D mesh)
                for(int surface=0;surface<mesh.Mesh.GetSurfaceCount();surface++)
                foreach(var vertex in mesh.Mesh.SurfaceGetArrays(surface)[(int)Mesh.ArrayType.Vertex].AsVector3Array())
                {
                    var p=mesh.GlobalTransform*vertex;
                    pondPatchVertices++;
                    pondPatchesDry &= new Vector2(p.X,p.Z).DistanceTo(PartyRowboat.PondCentre)>=18.19f;
                }
            Check(pondPatchVertices>50 && pondPatchesDry,$"pond meadow: {pondPatchVertices} dark ground vertices, all outside water footprint");
            var camera = new Camera3D {Current=true,Fov=50,Far=1600};world.AddChild(camera);
            foreach (var shot in new[] {("overview",new Vector3(365,140,385),new Vector3(272,0,259)),
                ("outlet",new Vector3(289,70,288),new Vector3(237,0,229)),
                ("pond-to-ford",new Vector3(155,95,140),new Vector3(247,0,239)),
                ("pond-close",new Vector3(244,26,227),new Vector3(220,3.33f,205)),
                ("continuation",new Vector3(365,90,355),new Vector3(334,0,292))})
            {
                camera.Position=shot.Item2; camera.LookAt(shot.Item3);
                await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                using var image=vp.GetTexture().GetImage();
                Check(image.SavePng(Path.Combine(dir,shot.Item1+".png"))==Error.Ok,"river view: "+shot.Item1);
            }
            vp.Free();
        }
        catch(Exception e){GD.PrintErr(e);_failures++;}
        GetTree().Quit(_failures==0?0:1);
    }
}
