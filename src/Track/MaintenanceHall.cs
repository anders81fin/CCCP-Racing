using System;
using System.Collections.Generic;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Track;

/// <summary>One surveyed footprint for the road, building, oil, collision and quiet exercise area.</summary>
public sealed class MaintenanceHallSite
{
    public Vector2 Center,Forward;
    public float HalfWidth,HalfLength;
    public Vector2 Right=>new(-Forward.Y,Forward.X);
    public Vector2 World(float x,float z)=>Center+Right*x-Forward*z;
    public Vector2 Local(Vector2 p) {var d=p-Center;return new(d.Dot(Right),-d.Dot(Forward));}
    public bool Contains(Vector2 p,float margin=0) {p=Local(p);return Math.Abs(p.X)<=HalfWidth+margin && Math.Abs(p.Y)<=HalfLength+margin;}
    public float Enclosure(Vector2 p)
    {p=Local(p);return Math.Clamp((HalfLength-Math.Abs(p.Y))/3,0,1)*Math.Clamp((HalfWidth-Math.Abs(p.X))/2,0,1);}
    public float Oil(Vector2 p)
    {
        p=Local(p);float film=0;
        foreach(var stain in Stains) {
            Vector2 d=(p-stain.Center)/stain.Size;
            film=Math.Max(film,Math.Clamp((1-d.Length())*5,0,1));
        }
        return film;
    }
    public static readonly (Vector2 Center,Vector2 Size)[] Stains={
        (new(-4.35f,-4),new(.95f,2.4f)),(new(4.4f,4),new(1,2.1f))};
}

/// <summary>Open-ended corrugated repair shelter: a car-wide clear route, camera cutaway and grounded details.</summary>
public partial class MaintenanceHall : Node3D
{
    public const float ClearHalfWidth=6.8f;
    const float BayCenter=7.7f;
    Node3D _daylight;
    public override void _Process(double delta) {if(_daylight!=null) _daylight.Visible=NightGlow.Level<.85f;}
    public static void Prepare(TrackData t,MaintenanceHallDef def)
    {
        if(t.Id!="bonus" || def==null) return;
        int i=t.NearestCenter(new(def.X,def.Z),out _),n=t.Center.Length;
        t.MaintenanceHall=new MaintenanceHallSite {Center=t.Center[i],Forward=(t.Center[(i+10)%n]-t.Center[(i-10+n)%n]).Normalized(),
            HalfWidth=def.Width*.5f,HalfLength=def.Length*.5f};
    }
    public static void Grade(TrackData t) { foreach(var _ in GradeSteps(t)) { } }

    /// <summary>Grade in slices: every item is a point where a frame may go by (a loading card keeps running).</summary>
    public static IEnumerable<int> GradeSteps(TrackData t)
    {
        if(t.MaintenanceHall is not {} site) yield break;
        var slice=System.Diagnostics.Stopwatch.StartNew();
        for(int z=0;z<=t.N;z++) {
        if(slice.ElapsedMilliseconds>10) { yield return 0; slice.Restart(); }
        for(int x=0;x<=t.N;x++) {
            Vector2 world=new(x*t.Tile,z*t.Tile),p=site.Local(world);
            float edge=Math.Max(Math.Abs(p.X)-site.HalfWidth,Math.Abs(p.Y)-site.HalfLength);
            if(edge>=4) continue;
            int i=t.NearestCenter(world,out _);
            float weight=1-Mathf.SmoothStep(0,4,Math.Max(0,edge));
            t.Heights[z*(t.N+1)+x]=Mathf.Lerp(t.Heights[z*(t.N+1)+x],t.CenterH[i],weight);
        }
        }
    }
    public static void Paint(TrackData t) { foreach(var _ in PaintSteps(t)) { } }

    /// <summary>Paint in slices (see GradeSteps).</summary>
    public static IEnumerable<int> PaintSteps(TrackData t)
    {
        if(t.MaintenanceHall is not {} site) yield break;
        var slice=System.Diagnostics.Stopwatch.StartNew();
        for(int z=0;z<t.N;z++) {
            if(slice.ElapsedMilliseconds>10) { yield return 0; slice.Restart(); }
            for(int x=0;x<t.N;x++)
            if(site.Contains(new((x+.5f)*t.Tile,(z+.5f)*t.Tile),1.5f)) {
                t.Surf[z*t.N+x]=SurfaceType.Concrete;t.Verge[z*t.N+x]=false;
            }
        }
        for(int i=0;i<t.Center.Length;i++) if(site.Contains(t.Center[i])) t.CenterSurf[i]=SurfaceType.Concrete;
    }
    public static void Reserve(TrackData t,List<Vector2> occupied)
    {
        if(t.MaintenanceHall is not {} site) return;
        for(float x=-site.HalfWidth-3;x<=site.HalfWidth+3;x+=2)
            for(float z=-site.HalfLength-4;z<=site.HalfLength+4;z+=2) occupied.Add(site.World(x,z));
        foreach(float side in new[]{-1f,1f}) {
            for(float z=-site.HalfLength;z<=site.HalfLength;z+=1.2f)
                t.Obstacles.Add(new(site.World(side*(site.HalfWidth-.15f),z),.65f));
            foreach(float z in new[]{-10f,-7f,0f,3.8f,7f,8.7f,10.1f}) t.Obstacles.Add(new(site.World(side*BayCenter,z),.8f));
        }
    }
    public static bool BlocksFire(TrackData t,Vector2 from,Vector2 to)
    {
        if(t.MaintenanceHall is not {} site) return false;
        int steps=Math.Max(1,(int)MathF.Ceiling(from.DistanceTo(to)/3));
        for(int i=0;i<=steps;i++) if(site.Contains(from.Lerp(to,i/(float)steps),8)) return true;
        return false;
    }
    public static Node3D Build(TrackData t)
    {
        var root=new MaintenanceHall {Name="DriveThroughMaintenanceHall"};
        if(t.MaintenanceHall is not {} site) return root;
        float datum=t.MeshHeight(site.Center.X,site.Center.Y);
        root.Position=new(site.Center.X,datum,site.Center.Y);root.Rotation=new(0,TrackData.YawFor(site.Forward),0);
        float H(float x,float z) {Vector2 p=site.World(x,z);return t.MeshHeight(p.X,p.Y)-datum;}
        Vector3 At(float x,float y,float z)=>new(x,H(x,z)+y,z);
        Color steel=new(.38f,.4f,.34f),dark=new(.18f,.20f,.16f),rust=new(.36f,.24f,.15f),olive=new(.31f,.36f,.23f);
        var fixtures=new Node3D {Name="RepairBays"};root.AddChild(fixtures);
        void Box(Vector3 size,Color color,Vector3 p,Vector3 rotation=default)=>fixtures.AddChild(Props.Part(Props.Box(size.X,size.Y,size.Z),color,p,rotation));
        void Rod(Node3D parent,Vector3 a,Vector3 b,float radius,Color color) {
            var rod=Props.Part(Props.Cyl(radius,a.DistanceTo(b),6),color,(a+b)*.5f);
            rod.Quaternion=new Quaternion(Vector3.Up,(b-a).Normalized());parent.AddChild(rod);
        }
        // Low side plinths. Both end portals remain completely open.
        foreach(float side in new[]{-1f,1f}) for(float z=-site.HalfLength+1;z<site.HalfLength;z+=2)
            Box(new(.45f,1.25f,2),new(.34f,.33f,.28f),At(side*(site.HalfWidth-.2f),.6f,z));
        // Floor follows the actual physics terrain; joints and damaged corners never form steps.
        var floor=new SurfaceTool();floor.Begin(Mesh.PrimitiveType.Triangles);
        void GroundPolygon(SurfaceTool st,Vector2[] boundary,Color color,float lift)
        {
            Vector2 lo=new(float.MaxValue,float.MaxValue),hi=new(float.MinValue,float.MinValue);
            foreach(var p in boundary) {Vector2 w=site.World(p.X,p.Y);lo=lo.Min(w);hi=hi.Max(w);}
            Vector3 Vertex(int x,int z) {Vector2 p=site.Local(new(x*t.Tile,z*t.Tile));return new(p.X,t.VertexHeight(x,z)-datum,p.Y);}
            for(int z=Math.Max(0,(int)MathF.Floor(lo.Y/t.Tile));z<=Math.Min(t.N-1,(int)MathF.Floor(hi.Y/t.Tile));z++)
                for(int x=Math.Max(0,(int)MathF.Floor(lo.X/t.Tile));x<=Math.Min(t.N-1,(int)MathF.Floor(hi.X/t.Tile));x++)
                {
                    Vector3 a=Vertex(x,z),b=Vertex(x+1,z),c=Vertex(x+1,z+1),d=Vertex(x,z+1);
                    foreach(var triangle in new[]{new[]{a,b,c},new[]{a,c,d}}) {
                        var polygon=new List<Vector3>(triangle);
                        for(int edge=0;edge<boundary.Length && polygon.Count>0;edge++) {
                            Vector2 from=boundary[edge],direction=boundary[(edge+1)%boundary.Length]-from;
                            float Distance(Vector3 p)=>direction.Cross(new Vector2(p.X,p.Z)-from);
                            var clipped=new List<Vector3>();Vector3 previous=polygon[^1];float pd=Distance(previous);
                            foreach(Vector3 current in polygon) {
                                float cd=Distance(current);
                                if((pd>=0)!=(cd>=0)) clipped.Add(previous.Lerp(current,pd/(pd-cd)));
                                if(cd>=0) clipped.Add(current);
                                previous=current;pd=cd;
                            }
                            polygon=clipped;
                        }
                        if(polygon.Count<3) continue;
                        st.SetColor(color.SrgbToLinear());st.SetNormal((triangle[2]-triangle[0]).Cross(triangle[1]-triangle[0]).Normalized());
                        for(int i=1;i<polygon.Count-1;i++) foreach(int j in new[]{0,i,i+1}) st.AddVertex(polygon[j]+Vector3.Up*lift);
                    }
                }
        }
        void FloorQuad(SurfaceTool st,float x0,float z0,float x1,float z1,Color color,float lift=.025f)=>
            GroundPolygon(st,new[]{new Vector2(x0,z0),new(x1,z0),new(x1,z1),new(x0,z1)},color,lift);
        var grout=new SurfaceTool();grout.Begin(Mesh.PrimitiveType.Triangles);
        FloorQuad(grout,-site.HalfWidth,-site.HalfLength,site.HalfWidth,site.HalfLength,new(.18f,.18f,.16f),.012f);
        root.AddChild(new MeshInstance3D {Name="ConcreteJointBed",Mesh=grout.Commit(),MaterialOverride=new StandardMaterial3D {
            VertexColorUseAsAlbedo=true,Roughness=1,SpecularMode=BaseMaterial3D.SpecularModeEnum.Disabled}});
        for(float z=-site.HalfLength;z<site.HalfLength;z+=4) for(float x=-site.HalfWidth;x<site.HalfWidth;x+=3) {
            float shade=.26f+Noise.Hash01((int)x,(int)z,71)*.08f;
            FloorQuad(floor,x+.035f,z+.035f,Math.Min(x+3-.035f,site.HalfWidth),Math.Min(z+4-.035f,site.HalfLength),new(shade,shade*.98f,shade*.9f));
        }
        root.AddChild(new MeshInstance3D {Name="WornConcreteFloor",Mesh=floor.Commit(),MaterialOverride=new StandardMaterial3D {
            VertexColorUseAsAlbedo=true,Roughness=1,SpecularMode=BaseMaterial3D.SpecularModeEnum.Disabled,CullMode=BaseMaterial3D.CullModeEnum.Disabled}});
        // Curved ribs and corrugated sheet panels: narrow open seams admit bands of daylight.
        var roof=new Node3D {Name="CameraCutawayRoof"};root.AddChild(roof);
        Vector3 Arch(float angle,float z,float ridge=0) {float x=MathF.Cos(angle)*(site.HalfWidth+ridge);return new(x,H(x,z)+1.22f+MathF.Sin(angle)*(6.1f+ridge),z);}
        var sheets=new SurfaceTool();sheets.Begin(Mesh.PrimitiveType.Triangles);
        void Triangle(SurfaceTool st,Vector3 a,Vector3 b,Vector3 c) {st.SetNormal((b-a).Cross(c-a).Normalized());st.AddVertex(a);st.AddVertex(c);st.AddVertex(b);}
        int rows=(int)MathF.Ceiling(site.HalfLength*2/.3f);
        for(int row=0;row<rows;row++) {
            float z0=-site.HalfLength+row*.3f,z1=Math.Min(z0+.3f,site.HalfLength);
            if(row%10==0) z0+=.035f;
            for(int a=0;a<32;a++) {
                float a0=a*MathF.PI/32,a1=(a+1)*MathF.PI/32;
                float ridge=row%2==0?.025f:-.025f;
                Vector3 p0=Arch(a0,z0,ridge),p1=Arch(a1,z0,ridge),p2=Arch(a1,z1,-ridge),p3=Arch(a0,z1,-ridge);
                sheets.SetColor((row%13==0?rust:steel).SrgbToLinear());
                Triangle(sheets,p0,p1,p2);Triangle(sheets,p0,p2,p3);
            }
        }
        roof.AddChild(new MeshInstance3D {Name="CorrugatedSteel",Mesh=sheets.Commit(),MaterialOverride=new StandardMaterial3D {
            VertexColorUseAsAlbedo=true,Roughness=1,CullMode=BaseMaterial3D.CullModeEnum.Disabled},CastShadow=GeometryInstance3D.ShadowCastingSetting.Off});
        for(float z=-site.HalfLength;z<=site.HalfLength+.1f;z+=site.HalfLength/4)
            for(int a=0;a<24;a++) Rod(roof,Arch(a*MathF.PI/24,z,-.13f),Arch((a+1)*MathF.PI/24,z,-.13f),.065f,dark);
        DrunkTractor.BatchStatic(roof,null);
        roof.AddChild(new SeeThrough {Name="SeeThrough"});
        // Visible slivers of sunlight on the shaded slab floor fade out at night.
        root._daylight=new Node3D {Name="LightThroughSeams"};root.AddChild(root._daylight);
        var strips=new SurfaceTool();strips.Begin(Mesh.PrimitiveType.Triangles);
        for(float z=-site.HalfLength+2;z<site.HalfLength-1;z+=3)
            FloorQuad(strips,-site.HalfWidth+.7f,z,site.HalfWidth-.7f,z+.1f,new(.65f,.61f,.44f),.04f);
        root._daylight.AddChild(new MeshInstance3D {Mesh=strips.Commit(),MaterialOverride=new StandardMaterial3D {
            VertexColorUseAsAlbedo=true,Roughness=1,CullMode=BaseMaterial3D.CullModeEnum.Disabled}});
        // (The patch was almost black, albedo .06: a headlight beam, which has no specular, lit it to nothing and the lamps seemed to fail over the oil. Dark but lit now.)
        // The only slippery patches sit at the margins; a generous dry middle line stays clear.
        foreach(var stain in MaintenanceHallSite.Stains) {
            var st=new SurfaceTool();st.Begin(Mesh.PrimitiveType.Triangles);
            var boundary=new Vector2[28];
            for(int i=0;i<boundary.Length;i++) {float a=i*MathF.Tau/boundary.Length;boundary[i]=stain.Center+new Vector2(MathF.Cos(a),MathF.Sin(a))*stain.Size;}
            GroundPolygon(st,boundary,Colors.White,.045f);
            root.AddChild(new MeshInstance3D {Name="OilStain",Mesh=st.Commit(),MaterialOverride=new StandardMaterial3D {
                AlbedoColor=new(.15f,.16f,.14f),Roughness=.23f,CullMode=BaseMaterial3D.CullModeEnum.Disabled}});
        }
        // Shallow maintenance bays: tyres, drums, engine stand, toolbox and coiled air hose.
        foreach(float side in new[]{-1f,1f}) {
            float x=side*BayCenter;
            foreach(float z in new[]{-7f,0f,7f}) {
                Box(new(1.2f,.18f,1),rust,At(x,.2f,z));
                foreach(float leg in new[]{-.45f,.45f}) Box(new(.09f,.8f,.65f),steel,At(x+leg,.45f,z));
                Box(new(1,.62f,.7f),olive,At(x,1.0f,z));
                for(int i=0;i<5;i++) Box(new(.06f,.42f,.75f),dark,At(x-.36f+i*.18f,1.03f,z));
            }
            for(int i=0;i<3;i++) fixtures.AddChild(Props.Part(new TorusMesh {InnerRadius=.28f,OuterRadius=.57f,Rings=12,RingSegments=6},dark,At(x,.25f+i*.32f,-10)));
            foreach(float z in new[]{8.7f,10.1f}) {
                fixtures.AddChild(Props.Part(Props.Cyl(.39f,1,12),olive,At(x,.5f,z)));
                foreach(float y in new[]{.2f,.78f}) fixtures.AddChild(Props.Part(new TorusMesh {InnerRadius=.38f,OuterRadius=.41f,Rings=12,RingSegments=4},steel,At(x,y,z)));
            }
            Box(new(1.1f,.7f,.65f),rust,At(x,.4f,3.8f));
            for(int i=0;i<3;i++) Box(new(.6f,.025f,.025f),steel,At(x,.38f+i*.18f,3.45f));
            for(int i=0;i<20;i++) {
                float a=i*MathF.Tau/20,b=(i+1)*MathF.Tau/20;
                Rod(fixtures,At(x+MathF.Cos(a)*.48f,.08f,-3+MathF.Sin(a)*.6f),At(x+MathF.Cos(b)*.48f,.08f,-3+MathF.Sin(b)*.6f),.028f,dark);
            }
            foreach(float z in new[]{-site.HalfLength-.2f,site.HalfLength+.2f}) {
                Box(new(.22f,1.3f,.18f),steel,At(side*ClearHalfWidth,.65f,z));
                fixtures.AddChild(NightGlow.Part(Props.Box(.25f,.16f,.2f),new(.8f,.75f,.52f),new(1,.85f,.4f),1.5f,At(side*ClearHalfWidth,1.25f,z)));
            }
        }
        foreach(float side in new[]{-1f,1f}) for(int i=0;i<16;i++) {
            float x=side*(ClearHalfWidth+.25f+Noise.Hash01(i,3,83)*.8f),z=(Noise.Hash01(i,7,83)-.5f)*(site.HalfLength*2-1);
            Box(new(.12f+Noise.Hash01(i,2,83)*.15f,.07f,.14f),new(.38f,.36f,.3f),At(x,.04f,z),new(0,i*53,0));
        }
        // Fine branching cracks and crumbling edge chips stay graphical, without hidden steps.
        for(int i=0;i<13;i++) {
            float x=(Noise.Hash01(i,3,72)-.5f)*11,z=(Noise.Hash01(i,7,72)-.5f)*(site.HalfLength*2-2);
            Rod(fixtures,At(x,.043f,z),At(x+.4f,.043f,z+.35f),.014f,dark);
            Rod(fixtures,At(x+.4f,.043f,z+.35f),At(x+.25f,.043f,z+.7f),.012f,dark);
        }
        // Small working lamps at the sides leave the roof and the road beyond dark.
        foreach(float z in new[]{-6f,6f}) {
            Rod(fixtures,At(6.8f,5.3f,z),At(6.8f,3.8f,z),.025f,steel);
            var lamp=new SpotLight3D {Position=At(6.8f,3.8f,z),RotationDegrees=new(-85,0,0),SpotRange=8,SpotAngle=48,
                LightColor=new(1,.85f,.58f),ShadowEnabled=false,LightSpecular=0,Visible=false};
            lamp.SetMeta("night_energy",1.6f);root.AddChild(lamp);
            fixtures.AddChild(NightGlow.Part(Props.Box(.45f,.12f,.3f),new(.7f,.67f,.5f),new(1,.85f,.58f),1.4f,At(6.8f,3.8f,z)));
        }
        // Short, legible portal name, well above the vehicles.
        var label=ImageTexture.CreateFromImage(PixelFont.RenderImage("REMONT",new(.83f,.81f,.65f),new(.22f,.26f,.18f),1));
        foreach(float end in new[]{-1f,1f}) foreach(float x in new[]{-.95f,.95f})
            Rod(fixtures,At(x,7.3f,end*site.HalfLength),At(x,6,end*site.HalfLength),.022f,steel);
        foreach(float end in new[]{-1f,1f}) root.AddChild(new MeshInstance3D {Mesh=new QuadMesh {Size=new(2.4f,.55f)},
            Position=At(0,5.7f,end*(site.HalfLength+.08f)),RotationDegrees=new(0,end<0?180:0,0),
            MaterialOverride=new StandardMaterial3D {AlbedoTexture=label,TextureFilter=BaseMaterial3D.TextureFilterEnum.Nearest,Roughness=1}});
        var glowing=new List<Node>();
        foreach(Node child in fixtures.GetChildren())
            if(child is MeshInstance3D mesh && mesh.MaterialOverride is StandardMaterial3D mat && mat.EmissionEnabled) glowing.Add(child);
        foreach(var lamp in glowing) fixtures.RemoveChild(lamp);
        DrunkTractor.BatchStatic(fixtures,null);
        foreach(var lamp in glowing) fixtures.AddChild(lamp);
        return root;
    }
}
