using System;
using Godot;

namespace CCCPRacing.Track;

/// <summary>Small lived-in dressing for the bonus field camp: the mechanic's cot corner, gate post, washing line and pad fire points.</summary>
public static class BonusCampDetails
{
    static readonly Color Olive=new(.28f,.32f,.19f),Steel=new(.22f,.23f,.2f),Rust=new(.36f,.22f,.12f),
        Red=new(.62f,.07f,.05f),White=new(.86f,.85f,.78f),Dark=new(.1f,.1f,.09f),Khaki=new(.55f,.5f,.34f);

    public static Node3D Build(TrackData t)
    {
        var root=new Node3D { Name="BonusCampDetails" };
        if(t.MechanicPost is { } post) CotCorner(root,t,post);
        Gate(root,t);
        WashingLine(root,t);
        Lanterns(root,t);
        foreach(Vector2 pad in BonusCampLayout.Pads) FirePoint(root,t,pad+new Vector2(9.5f,-10.5f));
        DrunkTractor.BatchStatic(root,null);
        // Wet ground and wheel tracks are not batched: the water keeps its gloss.
        root.AddChild(GroundMarks(t));
        return root;
    }

    static Node3D Site(Node3D root,TrackData t,float x,float z,float yaw=0)
    {
        var n=new Node3D { Position=new Vector3(x,t.MeshHeight(x,z),z),Rotation=new Vector3(0,yaw,0) };
        root.AddChild(n);return n;
    }
    static void Box(Node3D n,Vector3 size,Vector3 at,Color color,Vector3? rot=null) => n.AddChild(Props.Part(Props.Box(size.X,size.Y,size.Z),color,at,rot));
    static void Cyl(Node3D n,float r,float h,Vector3 at,Color color,Vector3? rot=null) => n.AddChild(Props.Part(Props.Cyl(r,h,8),color,at,rot));

    /// <summary>Boots, a flask, a radio on a crate and a coat on a peg around the cot. Local x runs along the bed, the head end at -x.</summary>
    static void CotCorner(Node3D root,TrackData t,Transform3D post)
    {
        Vector3 at=post*new Vector3(0,0,7.45f);
        var n=Site(root,t,at.X,at.Z,post.Basis.GetEuler().Y);
        foreach(float z in new[]{-.2f,.2f}) Box(n,new(.34f,.17f,.12f),new(1.0f,.09f,z),Dark);
        Cyl(n,.07f,.3f,new(-.3f,.15f,-.8f),new Color(.45f,.5f,.45f));
        Box(n,new(.5f,.45f,.5f),new(-1.95f,.23f,0),Rust);
        Box(n,new(.34f,.2f,.15f),new(-1.95f,.55f,0),Steel);
        Cyl(n,.01f,.5f,new(-1.85f,.9f,.05f),Steel);
        Cyl(n,.035f,1.7f,new(.2f,.85f,.95f),Steel);
        Box(n,new(.14f,.75f,.5f),new(.2f,1.2f,.95f),Olive);
        Box(n,new(.1f,.06f,.32f),new(.2f,1.62f,.95f),Dark);
    }

    /// <summary>A sentry hut beside the camp gate.</summary>
    static void Gate(Node3D root,TrackData t)
    {
        var hut=Site(root,t,148.0f,121.6f,-.15f);   // outside the fence, east of the gate, tucked between the sandbag wall and the road (the guard's post)
        Box(hut,new(1.7f,2.2f,1.6f),new(0,1.1f,0),Olive);
        Box(hut,new(2.1f,.12f,2.0f),new(0,2.28f,0),Steel);
        Box(hut,new(1.0f,.7f,.05f),new(0,1.5f,.82f),Dark);
        Box(hut,new(.7f,1.5f,.05f),new(-.2f,.75f,-.82f),Rust);
    }

    /// <summary>A line of shirts between the command and crew tents.</summary>
    static void WashingLine(Node3D root,TrackData t)
    {
        var line=Site(root,t,165.5f,96.5f);
        foreach(float z in new[]{-3.4f,3.4f}) Cyl(line,.045f,2.2f,new(0,1.1f,z),Rust);
        Box(line,new(.02f,.02f,6.8f),new(0,2.05f,0),Dark);
        Color[] cloth={Olive,White,Khaki,Olive};
        for(int i=0;i<4;i++)
        {
            Box(line,new(.04f,.62f,.5f),new(0,1.7f,-2.4f+i*1.55f),cloth[i]);
            Box(line,new(.05f,.2f,.8f),new(0,1.95f,-2.4f+i*1.55f),cloth[(i+1)%4]);
        }
    }

    /// <summary>Lanterns between the tents and by the gate hut: they glow at night.</summary>
    static void Lanterns(Node3D root,TrackData t)
    {
        foreach(var at in new[]{new Vector2(157.5f,96.5f),new Vector2(150f,112f),new Vector2(141.4f,118.6f)})
        {
            var n=Site(root,t,at.X,at.Y);
            Cyl(n,.04f,2.6f,new(0,1.3f,0),Steel);
            Box(n,new(.5f,.05f,.05f),new(.2f,2.55f,0),Steel);
            n.AddChild(NightGlow.Part(Props.Box(.2f,.28f,.2f),new Color(.9f,.78f,.4f),new Color(1f,.8f,.4f),1.6f,new Vector3(.42f,2.35f,0)));
        }
    }

    // ---------------------------------------------------------------- wet ground and wheel tracks

    static readonly Vector2[][] RutPaths={
        new Vector2[]{new(136,118),new(136,126),new(139,128),new(142,130)},
        new Vector2[]{new(104,120),new(123,117),new(136,116)}};
    // Centre, radius: ruts collect water where the ground is lowest, plus one by the kitchen and one before the gate.
    static readonly Vector3[] Puddles={new(106.5f,121.5f,1.3f),new(134.4f,126.2f,1.6f),new(128.5f,125.4f,.9f),new(122f,117.6f,1.0f),new(127.5f,119.8f,1.1f)};

    static Node3D GroundMarks(TrackData t)
    {
        var root=new Node3D {Name="CampGroundMarks"};
        var rutMaterial=new StandardMaterial3D {AlbedoColor=new Color(.15f,.125f,.09f),Roughness=1,CullMode=BaseMaterial3D.CullModeEnum.Disabled};
        var ridgeMaterial=new StandardMaterial3D {AlbedoColor=new Color(.40f,.34f,.22f),Roughness=1,CullMode=BaseMaterial3D.CullModeEnum.Disabled};
        var wetMaterial=new StandardMaterial3D {AlbedoColor=new Color(.20f,.16f,.11f),Roughness=.55f,CullMode=BaseMaterial3D.CullModeEnum.Disabled};
        var waterMaterial=new StandardMaterial3D {AlbedoColor=new Color(.11f,.14f,.15f,.92f),Roughness=.04f,Metallic=.15f,
            Transparency=BaseMaterial3D.TransparencyEnum.Alpha,CullMode=BaseMaterial3D.CullModeEnum.Disabled};
        var pebbleMaterial=new StandardMaterial3D {AlbedoColor=new Color(.45f,.42f,.36f),Roughness=1};
        foreach(var path in RutPaths)
            foreach(float lane in new[]{-.95f,.95f})
            {
                root.AddChild(Strip(t,path,lane,.34f,.05f,rutMaterial,lane*7.1f));                       // the trodden-down track
                root.AddChild(Strip(t,path,lane+Math.Sign(lane)*.36f,.13f,.065f,ridgeMaterial,lane*3.3f+11)); // churned earth thrown up beside it
                root.AddChild(Strip(t,path,lane-Math.Sign(lane)*.34f,.10f,.062f,ridgeMaterial,lane*5.1f+23)); // ...and a lip on the inner side
            }
        int seed=0;
        foreach(var puddle in Puddles)
        {
            seed++;
            Vector2 c=new(puddle.X,puddle.Y);
            root.AddChild(Blob(t,c,puddle.Z*1.35f,seed*7,.045f,wetMaterial));      // wet earth round the water
            root.AddChild(Blob(t,c,puddle.Z,seed*7+3,.062f,waterMaterial));
            for(int k=0;k<5;k++)
            {
                float a=seed*2.1f+k*1.3f,r=puddle.Z*(1.25f+.35f*Noise.Value(k,seed,41));
                Vector2 p=c+new Vector2(MathF.Cos(a),MathF.Sin(a)*.75f)*r;
                var pebble=Props.Part(Props.Ball(.07f+.05f*Noise.Value(seed,k,43),5),pebbleMaterial.AlbedoColor,new Vector3(p.X,t.MeshHeight(p.X,p.Y)+.04f,p.Y));
                pebble.Scale=new Vector3(1.3f,.55f,1f);
                root.AddChild(pebble);
            }
        }
        return root;
    }

    /// <summary>A meandering ribbon of varying width that hugs the ground along a path, with gaps where the track fades out.</summary>
    static MeshInstance3D Strip(TrackData t,Vector2[] path,float lane,float width,float lift,Material material,float seed)
    {
        var st=new SurfaceTool();st.Begin(Mesh.PrimitiveType.Triangles);
        Vector2? previousLeft=null,previousRight=null;
        float distance=0;
        for(int i=0;i+1<path.Length;i++)
        {
            Vector2 a=path[i],b=path[i+1];float length=a.DistanceTo(b);Vector2 d=(b-a)/length,n=d.Orthogonal();
            for(float along=0;along<length;along+=.7f)
            {
                float d0=distance+along;
                // Intermittent: the track thins out and breaks where the wheels rolled lighter.
                float presence=Noise.Value(d0*.21f+seed,seed*.37f,53);
                if(presence<.28f) {previousLeft=previousRight=null;continue;}
                float meander=(Noise.Value(d0*.33f,seed,59)-.5f)*.34f;
                float w=width*(.55f+.9f*Noise.Value(d0*.47f+seed,3.1f,61))*Mathf.Clamp((presence-.28f)*6,.25f,1);
                Vector2 c=a+d*along+n*(lane+meander);
                Vector2 l=c-n*w*.5f,r=c+n*w*.5f;
                if(previousLeft is Vector2 pl && previousRight is Vector2 pr)
                {
                    Vector3 Up(Vector2 q)=>new(q.X,t.MeshHeight(q.X,q.Y)+lift,q.Y);
                    foreach(var v in new[]{Up(pl),Up(pr),Up(r),Up(pl),Up(r),Up(l)}) {st.SetNormal(Vector3.Up);st.AddVertex(v);}
                }
                previousLeft=l;previousRight=r;
            }
            distance+=length;
        }
        st.GenerateNormals();
        return new MeshInstance3D {Mesh=st.Commit(),MaterialOverride=material,CastShadow=GeometryInstance3D.ShadowCastingSetting.Off};
    }

    /// <summary>An irregular, slightly elongated blob of a given material lying on the ground.</summary>
    static MeshInstance3D Blob(TrackData t,Vector2 centre,float radius,float seed,float lift,Material material)
    {
        var st=new SurfaceTool();st.Begin(Mesh.PrimitiveType.Triangles);
        const int Points=16;
        Vector3 At(Vector2 p)=>new(p.X,t.MeshHeight(p.X,p.Y)+lift,p.Y);
        float stretch=1.25f+.3f*Noise.Value(seed,1.7f,67);
        float turn=Noise.Value(seed,2.3f,71)*MathF.Tau;
        Vector2 Ring(int i)
        {
            float a=i*MathF.Tau/Points;
            float r=radius*(.62f+.75f*Noise.Value(MathF.Cos(a)*1.4f+seed,MathF.Sin(a)*1.4f,73));
            Vector2 q=new(MathF.Cos(a)*r*stretch,MathF.Sin(a)*r);
            return centre+new Vector2(q.X*MathF.Cos(turn)-q.Y*MathF.Sin(turn),q.X*MathF.Sin(turn)+q.Y*MathF.Cos(turn));
        }
        for(int i=0;i<Points;i++)
            foreach(var v in new[]{At(centre),At(Ring(i)),At(Ring((i+1)%Points))}) {st.SetNormal(Vector3.Up);st.AddVertex(v);}
        return new MeshInstance3D {Mesh=st.Commit(),MaterialOverride=material,CastShadow=GeometryInstance3D.ShadowCastingSetting.Off};
    }

    /// <summary>Sand box, extinguisher and shovel beside a helipad.</summary>
    static void FirePoint(Node3D root,TrackData t,Vector2 at)
    {
        var n=Site(root,t,at.X,at.Y);
        Box(n,new(.9f,.5f,.6f),new(0,.25f,0),Red);
        Box(n,new(.8f,.06f,.5f),new(0,.52f,0),Khaki);
        Cyl(n,.1f,.5f,new(.75f,.25f,0),Red);
        Cyl(n,.025f,.9f,new(-.75f,.45f,0),Rust);
        Box(n,new(.2f,.04f,.25f),new(-.75f,.05f,.1f),Steel);
    }
}
