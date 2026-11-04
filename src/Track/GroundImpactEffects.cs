using System;
using Godot;
namespace CCCPRacing.Track;

/// <summary>Cosmetic, terrain-following blast scars and layered fine particles; no terrain or collision edits.</summary>
public partial class GroundImpactEffects : Node3D
{
    struct Fleck { public Vector3 P,V;public Color C;public float Age,Life,A,B,G,Drag; }
    readonly Fleck[] _particles=new Fleck[240];
    readonly RandomNumberGenerator _rng=new();
    MultiMesh _mesh,_chips;
    TrackData _track;
    Vector3 _at;
    bool _aircraft;
    int _count;
    float _age,_smokeClock;
    static StandardMaterial3D _material,_chipMaterial;
    public static GroundImpactEffects Create(TrackData track,Vector3 at,bool aircraft=false) => new() {
        _track=track,_at=at,_aircraft=aircraft,Position=at,Name=aircraft?"AircraftImpactDetail":"ShellImpactDetail" };
    static StandardMaterial3D Material()
    {
        if(_material!=null) return _material;
        var image=Image.CreateEmpty(64,64,false,Image.Format.Rgba8);
        for(int y=0;y<64;y++) for(int x=0;x<64;x++) {
            float px=(x-31.5f)/31.5f,py=(y-31.5f)/31.5f;
            float angle=MathF.Atan2(py,px),radius=MathF.Sqrt(px*px+py*py);
            float edge=.82f+MathF.Sin(angle*5)*.055f+MathF.Cos(angle*9)*.035f;
            float density=Math.Clamp((edge-radius)*3.5f,0,1);
            float grain=.83f+Noise.Hash01(x/2,y/2,2018)*.17f;
            image.SetPixel(x,y,new Color(grain,grain,grain,density*density*.8f));
        }
        _material=NightGlow.Keep(new StandardMaterial3D {
            AlbedoTexture=ImageTexture.CreateFromImage(image),VertexColorUseAsAlbedo=true,
            ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded,Transparency=BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode=BaseMaterial3D.BillboardModeEnum.Enabled,TextureFilter=BaseMaterial3D.TextureFilterEnum.Linear,
            CullMode=BaseMaterial3D.CullModeEnum.Disabled,NoDepthTest=false },.3f);
        return _material;
    }
    public override void _Ready()
    {
        _rng.Seed=(ulong)(Math.Abs(_at.X*113+_at.Z*193)+1953);
        _mesh=new MultiMesh { TransformFormat=MultiMesh.TransformFormatEnum.Transform3D,UseColors=true,
            Mesh=new QuadMesh(),InstanceCount=_particles.Length,VisibleInstanceCount=0 };
        AddChild(new MultiMeshInstance3D { Multimesh=_mesh,MaterialOverride=Material(),ExtraCullMargin=35,
            CastShadow=GeometryInstance3D.ShadowCastingSetting.Off });
        // Earth and stone fragments are small lit solids, not round smoke billboards.
        _chips=new MultiMesh {TransformFormat=MultiMesh.TransformFormatEnum.Transform3D,UseColors=true,
            Mesh=Props.Ball(.5f,5),InstanceCount=_particles.Length,VisibleInstanceCount=0};
        _chipMaterial??=new StandardMaterial3D {VertexColorUseAsAlbedo=true,Roughness=1,
            ShadingMode=BaseMaterial3D.ShadingModeEnum.PerVertex,SpecularMode=BaseMaterial3D.SpecularModeEnum.Disabled};
        AddChild(new MultiMeshInstance3D {Multimesh=_chips,MaterialOverride=_chipMaterial,ExtraCullMargin=35,
            CastShadow=GeometryInstance3D.ShadowCastingSetting.Off});
        AddChild(Scar());
        float scale=_aircraft?1.7f:1;
        // A compact hot core, then small incandescent fragments instead of giant orange blobs.
        for(int k=0;k<22;k++) Emit(new Vector3(0,.45f,0),Radial(1,8)*scale,
            new Color(1,_rng.RandfRange(.55f,.94f),.18f),_rng.RandfRange(.13f,.35f),(_aircraft?.85f:.35f)*scale,.05f,5,1);
        for(int k=0;k<72;k++) {
            var v=Radial(2,11)*scale;v.Y=_rng.RandfRange(3,17)*scale;
            float size=_rng.RandfRange(.05f,.20f)*scale;
            Emit(new Vector3(0,.12f,0),v,new Color(.24f+_rng.Randf()*.15f,.21f+_rng.Randf()*.12f,.15f),
                _rng.RandfRange(.9f,2.3f),size,size*.55f,16,.3f);
        }
        for(int k=0;k<20;k++) {
            var v=Radial(2,9)*scale;v.Y=_rng.RandfRange(.3f,2);
            Emit(new Vector3(0,.15f,0),v,new Color(.53f,.45f,.33f,.65f),_rng.RandfRange(1,2.6f),.35f*scale,2.3f*scale,.1f,1.4f);
        }
    }
    Vector3 Radial(float min,float max)
    {
        float a=_rng.Randf()*MathF.Tau,r=_rng.RandfRange(min,max);
        return new Vector3(MathF.Cos(a)*r,_rng.RandfRange(.5f,4),MathF.Sin(a)*r);
    }
    void Emit(Vector3 at,Vector3 velocity,Color color,float life,float start,float end,float gravity,float drag)
    {
        if(_count>=_particles.Length) return;
        _particles[_count++]=new Fleck {P=at,V=velocity,C=color,Life=life,A=start,B=end,G=gravity,Drag=drag};
    }
    MeshInstance3D Scar()
    {
        var st=new SurfaceTool();st.Begin(Mesh.PrimitiveType.Triangles);
        float radius=_aircraft?5.7f:2.35f;
        Vector3 Point(int ring,int sector,out Color colour) {
            float a=sector*MathF.Tau/64, f=ring/7f;
            float r=radius*f*(.88f+.10f*MathF.Sin(a*7)+.055f*MathF.Cos(a*13));
            var p=new Vector3(MathF.Cos(a)*r,0,MathF.Sin(a)*r*(_aircraft?.77f:1));
            p.Y=_track.MeshHeight(_at.X+p.X,_at.Z+p.Z)-_at.Y+.025f;
            float grain=Noise.Hash01(sector,ring,1908);
            var dark=new Color(.065f,.060f,.050f);var earth=new Color(.34f,.28f,.20f);
            colour=dark.Lerp(earth,Math.Clamp(f*1.2f-.2f,0,1));colour*=.8f+grain*.3f;
            colour.A=(1-MathF.Pow(f,3))*.86f;
            return p;
        }
        void Vertex(int ring,int sector) {var p=Point(ring,sector,out var c);st.SetColor(c);st.SetNormal(Vector3.Up);st.AddVertex(p);}
        for(int r=0;r<7;r++)for(int k=0;k<64;k++) {
            Vertex(r,k);Vertex(r+1,k+1);Vertex(r+1,k);
            Vertex(r,k);Vertex(r,k+1);Vertex(r+1,k+1);
        }
        return new MeshInstance3D {Mesh=st.Commit(),MaterialOverride=NightGlow.Keep(new StandardMaterial3D {
            VertexColorUseAsAlbedo=true,ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency=BaseMaterial3D.TransparencyEnum.Alpha,CullMode=BaseMaterial3D.CullModeEnum.Disabled },.12f),
            CastShadow=GeometryInstance3D.ShadowCastingSetting.Off};
    }
    public override void _Process(double delta) => Step(Math.Min((float)delta,.1f));
    /// <summary>The world tape restores this effect by its age: it has been going for <paramref name="age"/> seconds already (the same steps as the frames would have made).</summary>
    public void PreRoll(float age)
    {
        for(float t=0;t<age;t+=1f/30f) Step(Math.Min(1f/30f,age-t));
    }
    void Step(float dt)
    {
        _age+=dt;
        if(_age<(_aircraft?16:2.2f) && (_smokeClock-=dt)<=0) {
            _smokeClock=_aircraft?.10f:.13f;
            float scale=_aircraft?1.8f:1;
            for(int k=0;k<3;k++) {
                Vector3 at=new(_rng.RandfRange(-.6f,.6f)*scale,_rng.RandfRange(.2f,1),_rng.RandfRange(-.6f,.6f)*scale);
                Emit(at,new Vector3(.35f+_rng.RandfRange(-.3f,.3f),_rng.RandfRange(1.4f,3.5f),.15f),
                    _aircraft?new Color(.085f,.08f,.07f,.78f):new Color(.39f,.33f,.25f,.6f),
                    _rng.RandfRange(2,4),.35f*scale,2.5f*scale,.05f,.4f);
                if(_aircraft && _age<7) Emit(at,new Vector3(_rng.RandfRange(-.4f,.4f),2,.2f),
                    new Color(1,_rng.RandfRange(.3f,.65f),.05f),.5f,.6f,.1f,0,.1f);
            }
        }
        int smokeCount=0,chipCount=0;
        for(int i=0;i<_count;) {
            ref var p=ref _particles[i];p.Age+=dt;
            if(p.Age>=p.Life) {_particles[i]=_particles[--_count];continue;}
            p.V.Y-=p.G*dt;p.V*=MathF.Exp(-p.Drag*dt);p.P+=p.V*dt;
            float ground=_track.MeshHeight(_at.X+p.P.X,_at.Z+p.P.Z)-_at.Y+.035f;
            if(p.G>1 && p.P.Y<ground) {p.P.Y=ground;p.V=new Vector3(p.V.X*.5f,Math.Abs(p.V.Y)*.15f,p.V.Z*.5f);}
            float u=p.Age/p.Life,size=Mathf.Lerp(p.A,p.B,u);
            var colour=p.C;colour.A*=Math.Min(1,(1-u)*4);
            if(p.G>10) {
                var spin=Basis.FromEuler(new Vector3(p.Age*3,p.Age*5,p.Age*2));
                _chips.SetInstanceTransform(chipCount,new Transform3D(spin.Scaled(new Vector3(size,size*.65f,size*.8f)),p.P));
                _chips.SetInstanceColor(chipCount++,colour);
            } else {
                _mesh.SetInstanceTransform(smokeCount,new Transform3D(Basis.Identity.Scaled(Vector3.One*size),p.P));
                _mesh.SetInstanceColor(smokeCount++,colour);
            }
            i++;
        }
        _mesh.VisibleInstanceCount=smokeCount;_chips.VisibleInstanceCount=chipCount;
        if(_age>(_aircraft?28:35)) QueueFree();
    }
}
