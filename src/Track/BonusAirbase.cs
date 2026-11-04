using System;
using System.Collections.Generic;
using Godot;

namespace CCCPRacing.Track;

/// <summary>Shared helipad placement and the narrow, slower alternative to the main airstrip.</summary>
public static class BonusAirbase
{
    public static readonly Vector2 AbandonedCombine = new(216,221);
    public static Vector2[] Pads=>BonusCampLayout.Pads;
    public static readonly Vector2[] Bypass = {
        new(44,96), new(47,65), new(70,48), new(95,53), new(120,46),
        new(145,42), new(169,53), new(194,58), new(235,68)
    };
    public static readonly Vector2[] Berms = { new(84,87), new(136,84), new(207,106) };
    public static readonly Vector2 Kitchen = new(111,118);
    static Vector2[] ServicePath=>BonusCampLayout.ServicePath;
    internal static float ServiceDistance(Vector2 point) => Math.Min(PathDistance(point,ServicePath),
        point.DistanceTo(new Vector2(136,Math.Clamp(point.Y,116,124)))); // spur to the middle-pad gate
    public const float HalfWidth = 2.7f;
    public static bool Protected(Vector2 p, float margin) => BonusCampLayout.ProtectedBounds.Grow(margin).HasPoint(p);
    public static float BermHeight(Vector2 p)
    {
        float height=0;
        for(int i=0;i<Berms.Length;i++)
        {
            Vector2 d=p-Berms[i];
            if(i<2) d=new Vector2(d.Y,d.X);
            float r=MathF.Sqrt(d.X*d.X/25+d.Y*d.Y/169);
            if(r<1) height=Math.Max(height,2.5f*.5f*(1+MathF.Cos(r*MathF.PI)));
        }
        return height;
    }
    static float PathDistance(Vector2 point, Vector2[] path)
    {
        float best=float.MaxValue;
        for(int i=1;i<path.Length;i++)
        {
            Vector2 a=path[i-1],d=path[i]-a;
            best=Math.Min(best,point.DistanceTo(a+d*Math.Clamp((point-a).Dot(d)/d.LengthSquared(),0,1)));
        }
        return best;
    }
    // Local -Z is the nose: positive X/east and positive Z/south is southeast.
    public const float ParkedYaw = -3f * MathF.PI / 4f;
    public static float BaseHeight(TrackData t) => t.CenterH[t.NearestCenter(new Vector2(136,104),out _)];

    public static bool OnStand(Vector2 p,float margin=0)
    {
        foreach(var stand in Pads)
            if(MathF.Abs(p.X-stand.X)<=11+margin && MathF.Abs(p.Y-stand.Y)<=12+margin) return true;
        return false;
    }
    public static float Distance(Vector2 point,out Vector2 closest) => Distance(point,out closest,out _);
    static float Distance(Vector2 point,out Vector2 closest,out float along)
    {
        float best=float.MaxValue; closest=Bypass[0]; along=0; float travelled=0;
        for(int i=1;i<Bypass.Length;i++)
        {
            Vector2 a=Bypass[i-1], d=Bypass[i]-a;
            float u=Math.Clamp((point-a).Dot(d)/d.LengthSquared(),0,1);
            Vector2 p=a+d*u;
            float distance=p.DistanceTo(point);
            if(distance<best) { best=distance; closest=p; along=travelled+d.Length()*u; }
            travelled+=d.Length();
        }
        return best;
    }
    public static bool Reserved(TrackData t, Vector2 p, float margin=0) =>
        t.Id=="bonus" && Distance(p,out _) < HalfWidth+margin;

    public static void Grade(TrackData t) { foreach(var _ in GradeSteps(t)) { } }

    /// <summary>Grade in slices: every item is a point where a frame may go by (a loading card keeps running).</summary>
    public static IEnumerable<int> GradeSteps(TrackData t)
    {
        if(t.Id!="bonus") yield break;
        var slice=System.Diagnostics.Stopwatch.StartNew();
        // One gently graded field for the dispersed landing spots and temporary camp.
        // Grass between the pads remains visible; only their individual slabs are painted.
        float apron=BaseHeight(t);
        for(int z=0;z<=t.N;z++)
        {
        if(slice.ElapsedMilliseconds>10) { yield return 0; slice.Restart(); }
        for(int x=0;x<=t.N;x++)
        {
            Vector2 p=new(x*t.Tile,z*t.Tile);
            var bounds=BonusCampLayout.GradingBounds;
            float dx=Math.Max(0,Math.Max(bounds.Position.X-p.X,p.X-bounds.End.X));
            float dz=Math.Max(0,Math.Max(bounds.Position.Y-p.Y,p.Y-bounds.End.Y));
            float distance=MathF.Sqrt(dx*dx+dz*dz);
            if(distance>=20) continue;
            int road=t.NearestCenter(p,out float rd);
            if(rd<t.HalfWidth[road]+1.5f) continue;
            float weight=1-Math.Clamp(distance/20,0,1);
            weight=weight*weight*(3-2*weight);
            float shoulder=Math.Clamp((rd-t.HalfWidth[road]-1.5f)/8.5f,0,1);
            weight*=shoulder*shoulder*(3-2*shoulder);
            foreach(var stand in Pads)
                if(MathF.Abs(p.X-stand.X)<=11 && MathF.Abs(p.Y-stand.Y)<=12) weight=1;
            int vertex=z*(t.N+1)+x;
            t.Heights[vertex]=Mathf.Lerp(t.Heights[vertex],apron,weight)+BermHeight(p);
        }
        }
        float total=0;
        for(int i=1;i<Bypass.Length;i++) total+=Bypass[i-1].DistanceTo(Bypass[i]);
        int count=(int)MathF.Ceiling(total)+1;
        var profile=new float[count];
        for(int i=0;i<count;i++)
        {
            float left=Math.Min(i,total); Vector2 point=Bypass[^1];
            for(int leg=1;leg<Bypass.Length;leg++)
            {
                float length=Bypass[leg-1].DistanceTo(Bypass[leg]);
                if(left<=length) { point=Bypass[leg-1].Lerp(Bypass[leg],left/length); break; }
                left-=length;
            }
            profile[i]=t.MeshHeight(point.X,point.Y);
        }
        // Smooth the longitudinal grade as well as flattening across the trail.
        // This removes natural noise steps at the eastern merge into the main road.
        for(int pass=0;pass<3;pass++)
        {
            var smooth=(float[])profile.Clone();
            for(int i=1;i<count-1;i++)
            {
                float sum=0; int samples=0;
                for(int j=Math.Max(0,i-6);j<=Math.Min(count-1,i+6);j++) { sum+=profile[j]; samples++; }
                smooth[i]=sum/samples;
            }
            profile=smooth;
        }
        var source=(float[])t.Heights.Clone();
        for(int z=0;z<=t.N;z++)
        {
        if(slice.ElapsedMilliseconds>10) { yield return 0; slice.Restart(); }
        for(int x=0;x<=t.N;x++)
        {
            Vector2 p=new(x*t.Tile,z*t.Tile);
            float distance=Distance(p,out _,out float along);
            if(distance>HalfWidth+4) continue;
            int road=t.NearestCenter(p,out float rd);
            if(rd<t.HalfWidth[road]+1) continue;
            // Follow the natural ground along the path, flattening across it with soft shoulders.
            int sample=Math.Min(count-2,(int)along);
            float height=Mathf.Lerp(profile[sample],profile[sample+1],Math.Clamp(along-sample,0,1));
            float weight=1-Math.Clamp((distance-HalfWidth)/4,0,1);
            weight=weight*weight*(3-2*weight);
            // Read all source heights before applying any edits: no dependence on grid iteration order.
            source[z*(t.N+1)+x]=Mathf.Lerp(t.Heights[z*(t.N+1)+x],height,weight);
        }
        }
        t.Heights=source;
    }
    public static void Paint(TrackData t) { foreach(var _ in PaintSteps(t)) { } }

    /// <summary>Paint in slices (see GradeSteps).</summary>
    public static IEnumerable<int> PaintSteps(TrackData t)
    {
        if(t.Id!="bonus") yield break;
        var slice=System.Diagnostics.Stopwatch.StartNew();
        for(int z=0;z<t.N;z++)
        {
        if(slice.ElapsedMilliseconds>10) { yield return 0; slice.Restart(); }
        for(int x=0;x<t.N;x++)
        {
            Vector2 p=new((x+.5f)*t.Tile,(z+.5f)*t.Tile);
            int road=t.NearestCenter(p,out float distance);
            if(distance>t.HalfWidth[road] && (ServiceDistance(p)<1.7f ||
                p.X>109 && p.X<113 && p.Y>85 && p.Y<118))
            { t.Surf[z*t.N+x]=SurfaceType.Dirt; t.Verge[z*t.N+x]=false; }
            if(BermHeight(p)>.02f) t.Surf[z*t.N+x]=SurfaceType.Grass;
            if(Reserved(t,p) && distance>t.HalfWidth[road])
            { t.Surf[z*t.N+x]=SurfaceType.Dirt; t.Verge[z*t.N+x]=false; }
        }
        }
        yield return 0; slice.Restart();
        foreach(var step in RoughenYardsSteps(t)) { yield return step; slice.Restart(); }
        // The yard's southern tile row touches the sandbag wall: grass, not bare sand, along the bags.
        var yard=BonusCampLayout.MaintenanceYard;
        int southRow=(int)((yard.End.Y-.01f)/t.Tile);
        for(int x=0;x<t.N;x++)
        {
            Vector2 p=new((x+.5f)*t.Tile,(southRow+.5f)*t.Tile);
            if(p.X<yard.Position.X || p.X>yard.End.X || p.Y<yard.Position.Y) continue;
            if(t.Surf[southRow*t.N+x]!=SurfaceType.Dirt) continue;
            t.NearestCenter(p,out float roadDistance);
            if(roadDistance>12f) t.Surf[southRow*t.N+x]=SurfaceType.Grass;
        }
        TrimWestPad(t);
        yield return 0; slice.Restart();
        foreach(var step in ConcreteEastPadSteps(t)) { yield return step; slice.Restart(); }
        foreach(var step in PaintGateConnectorSteps(t)) { yield return step; slice.Restart(); }
    }
    // The western helipad: its northern tile row is grass and its southern two rows are solid concrete
    // (the service path used to leave them patchy).
    static void TrimWestPad(TrackData t)
    {
        Vector2 c=BonusCampLayout.Pads[0];
        Rect2 pad=new(c-new Vector2(11,12),new Vector2(22,24));
        int north=(int)(pad.Position.Y/t.Tile),south=(int)((pad.End.Y-.01f)/t.Tile);
        for(int x=0;x<t.N;x++)
        {
            float px=(x+.5f)*t.Tile;
            if(px<pad.Position.X || px>pad.End.X) continue;
            for(int z=north;z<=south;z++)
            {
                if(z!=north && z<south-1) continue;
                Vector2 p=new(px,(z+.5f)*t.Tile);
                if(p.Y<pad.Position.Y || p.Y>pad.End.Y) continue;
                t.NearestCenter(p,out float roadDistance);
                if(roadDistance<=12f) continue;
                t.Surf[z*t.N+x]=z==north?SurfaceType.Grass:SurfaceType.Concrete;
                t.Verge[z*t.N+x]=false;
            }
        }
    }
    // Gate mouth and the access road from the camp's gate to the race road: sand like the service paths.
    static readonly Vector2[] GateConnector={new(136,118),new(136,126),new(139,128),new(142,130)};
    // Trodden footpaths from the gate to the kitchen and to the command and crew tents.
    static readonly Vector2[] KitchenPath={new(134,121),new(124,119),new(114,121)};
    static readonly Vector2[] TentPath={new(138,119),new(148,112),new(155,106),new(158.5f,100)};
    static IEnumerable<int> PaintGateConnectorSteps(TrackData t)
    {
        var slice=System.Diagnostics.Stopwatch.StartNew();
        for(int z=0;z<t.N;z++)
        {
        if(slice.ElapsedMilliseconds>10) { yield return 0; slice.Restart(); }
        for(int x=0;x<t.N;x++)
        {
            Vector2 p=new((x+.5f)*t.Tile,(z+.5f)*t.Tile);
            if(PathDistance(p,GateConnector)>3.4f && !(Math.Abs(p.X-136)<=6 && Math.Abs(p.Y-124)<=1.5f)) continue;
            int road=t.NearestCenter(p,out float roadDistance);
            if(roadDistance<=t.HalfWidth[road] || t.Surf[z*t.N+x]==SurfaceType.Concrete) continue;
            t.Surf[z*t.N+x]=SurfaceType.Dirt;
            t.Verge[z*t.N+x]=false;
        }
        }
        foreach(var footpath in new[]{KitchenPath,TentPath})
            for(int z=0;z<t.N;z++)
            {
            if(slice.ElapsedMilliseconds>10) { yield return 0; slice.Restart(); }
            for(int x=0;x<t.N;x++)
            {
                Vector2 p=new((x+.5f)*t.Tile,(z+.5f)*t.Tile);
                if(PathDistance(p,footpath)>1.1f) continue;
                if(t.Surf[z*t.N+x] is SurfaceType.Concrete or SurfaceType.Dirt) continue;
                int road=t.NearestCenter(p,out float roadDistance);
                if(roadDistance<=t.HalfWidth[road]) continue;
                t.Surf[z*t.N+x]=SurfaceType.Dirt;
                t.Verge[z*t.N+x]=false;
            }
            }
    }

    // Break the straight sand/grass border of the two camp yards with noise, so it does not read as a grid.
    static IEnumerable<int> RoughenYardsSteps(TrackData t)
    {
        var slice=System.Diagnostics.Stopwatch.StartNew();
        foreach(Rect2 yard in new[]{BonusCampLayout.MaintenanceYard,BonusCampLayout.CrewYard})
            for(int z=0;z<t.N;z++)
            {
            if(slice.ElapsedMilliseconds>10) { yield return 0; slice.Restart(); }
            for(int x=0;x<t.N;x++)
            {
                Vector2 p=new((x+.5f)*t.Tile,(z+.5f)*t.Tile);
                bool inside=yard.HasPoint(p);
                float edge=inside?Math.Min(Math.Min(p.X-yard.Position.X,yard.End.X-p.X),Math.Min(p.Y-yard.Position.Y,yard.End.Y-p.Y))
                    :Math.Max(Math.Max(yard.Position.X-p.X,p.X-yard.End.X),Math.Max(yard.Position.Y-p.Y,p.Y-yard.End.Y));
                if(edge>2.1f) continue;
                var current=t.Surf[z*t.N+x];
                if(current is not (SurfaceType.Dirt or SurfaceType.Grass)) continue;
                int road=t.NearestCenter(p,out float roadDistance);
                if(roadDistance<=t.HalfWidth[road]+2f) continue;
                bool sand=Noise.Value(p.X/2.3f,p.Y/2.3f,717)>(inside?.28f:.68f);
                t.Surf[z*t.N+x]=sand?SurfaceType.Dirt:SurfaceType.Grass;
                t.Verge[z*t.N+x]=false;
            }
            }
    }
    // The eastern helipad: every sand tile left inside the slab (the service path ends in its south rows) becomes concrete.
    static IEnumerable<int> ConcreteEastPadSteps(TrackData t)
    {
        var slice=System.Diagnostics.Stopwatch.StartNew();
        Vector2 c=BonusCampLayout.Pads[2];
        Rect2 pad=new(c-new Vector2(11,12),new Vector2(22,24));
        for(int z=0;z<t.N;z++)
        {
        if(slice.ElapsedMilliseconds>10) { yield return 0; slice.Restart(); }
        for(int x=0;x<t.N;x++)
        {
            Vector2 p=new((x+.5f)*t.Tile,(z+.5f)*t.Tile);
            if(!pad.HasPoint(p) || t.Surf[z*t.N+x]!=SurfaceType.Dirt) continue;
            int road=t.NearestCenter(p,out float roadDistance);
            if(roadDistance<=t.HalfWidth[road]) continue;
            t.Surf[z*t.N+x]=SurfaceType.Concrete;
            t.Verge[z*t.N+x]=false;
        }
        }
    }
    public static Node3D BuildPad(TrackData track, Vector3 origin, float yaw)
    {
        var root=new Node3D { Name="Helipad" };
        var mesh=new SurfaceTool(); mesh.Begin(Mesh.PrimitiveType.Triangles);
        var basis=new Basis(Vector3.Up,yaw);
        Vector3 Vertex(Vector2 p)
        {
            Vector3 world=origin+basis*new Vector3(p.X,0,p.Y);
            return new Vector3(p.X,track.MeshHeight(world.X,world.Z)+.035f-origin.Y,p.Y);
        }
        void Quad(Vector2 a,Vector2 b,Vector2 c,Vector2 d)
        {
            foreach(var p in new[]{a,b,c,a,c,d}) { mesh.SetNormal(Vector3.Up); mesh.AddVertex(Vertex(p)); }
        }
        foreach(float side in new[]{-1f,1f})
        {
            float x=side*1.2f;
            Quad(new(x-.35f,-2.8f),new(x+.35f,-2.8f),new(x+.35f,2.8f),new(x-.35f,2.8f));
        }
        Quad(new(-1.55f,-.35f),new(1.55f,-.35f),new(1.55f,.35f),new(-1.55f,.35f));
        for(int i=0;i<64;i++)
        {
            float a=i*MathF.Tau/64,b=(i+1)*MathF.Tau/64;
            Vector2 At(float angle,float radius)=>new(MathF.Sin(angle)*radius,MathF.Cos(angle)*radius);
            Quad(At(a,7.8f),At(a,8.7f),At(b,8.7f),At(b,7.8f));
        }
        mesh.SetMaterial(new StandardMaterial3D { AlbedoColor=new Color(.85f,.84f,.68f),
            Roughness=1,CullMode=BaseMaterial3D.CullModeEnum.Disabled });
        root.AddChild(new MeshInstance3D { Mesh=mesh.Commit(),CastShadow=GeometryInstance3D.ShadowCastingSetting.Off });
        return root;
    }
}
