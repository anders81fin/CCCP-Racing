using System;
using System.Collections.Generic;
using CCCPRacing.Audio;
using CCCPRacing.Car;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Track;

/// <summary>
/// Two off-road BTR patrols on wide loops that cross the race road into the southern field, with terrain-following hulls and
/// host-owned moving collision slots. Now and then one halts at a derelict barn, turns its turret and fires a cosmetic
/// machine-gun burst at the roof out of boredom (sound, tracers and dust only; nothing is damaged).
/// </summary>
public partial class BtrPatrols : Node3D
{
    sealed class Stop {public int Waypoint;public Vector2 Barn;}
    sealed class Patrol : ITapeSpinner
    {
        public Vector3 Axis=>Vector3.Right;public float Angle=>Spin;     // the wheels turn about their local X by the unwrapped Spin
        public Node3D Model,Turret,Flash;
        public HindGunSound Gun;
        public DieselSound Sound;
        public readonly List<Node3D> Steering=new(),Wheels=new();
        public Vector2[] Route,Dense;public float[] Along;public float Length;
        public Stop[] Stops=Array.Empty<Stop>();public float[] StopAt=Array.Empty<float>();
        public float S,Speed,Spin,Dust,TurretYaw,FireWait,Phase,Timer;
        public int Slot,Number,Stopping,Bursts,Rounds;
        public float Roundclock;
        public Vector2 Fire;
        public int State; // 0 driving, 1 braking, 2 aiming, 3 firing, 4 pausing
    }
    sealed class Tracer {public MeshInstance3D Node;public Vector3 From,To;public float T,Duration;}
    // Wide loops: the first stays west of the south road and the second east of it; both cross the race road twice a lap.
    static readonly Vector2[][] Loops={
        new Vector2[]{new(170,224),new(190,236),new(180,282),new(150,292),new(128,282),new(140,268),new(160,246)},
        new Vector2[]{new(214,214),new(222,176),new(216,160),new(224,200),new(230,240),new(236,282),new(252,296),new(216,300),new(204,282),new(210,244)}};
    // Waypoint index and the ruined barn it faces.
    static readonly (int waypoint,Vector2 barn)[][] BarnStops={
        new[]{(0,new Vector2(182,214))},
        new[]{(2,new Vector2(228,150)),(0,new Vector2(182,214))}};
    readonly List<Patrol> _patrols=new();
    readonly List<Tracer> _tracers=new();
    readonly RandomNumberGenerator _rng=new(){Seed=721};
    TrackData _track;
    TyreEffects _fx;
    float _clock;
    public int Count=>_patrols.Count;
    internal IEnumerable<Vector3> Positions {get {foreach(var p in _patrols) yield return p.Model.Position;}}
    internal int Bursts {get {int n=0;foreach(var p in _patrols) n+=p.Bursts;return n;}}
    internal IEnumerable<Vector2[]> Routes {get {foreach(var p in _patrols) yield return p.Dense;}}

    public void Init(TrackData track,TyreEffects fx)
    {
        foreach (var _ in InitSteps(track, fx)) { }
    }

    // The loading caller may draw between patrols; other callers build both synchronously.
    public IEnumerable<int> InitSteps(TrackData track,TyreEffects fx)
    {
        _track=track;_fx=fx;
        if(track.Id!="bonus") yield break;
        Tape.Version(this,1);Tape.Part(this,"root");
        for(int n=0;n<Loops.Length;n++)
        {
            var route=(Vector2[])Loops[n].Clone();
            if(!Fit(route,out var dense)) {GD.PushWarning($"BTR loop {n+1} could not be cleared; using a small local circle");route=Circle(n);Fit(route,out dense);}
            yield return 0;
            var p=new Patrol {Route=route,Dense=dense,Number=n,Slot=track.Obstacles.Count};
            Measure(p);
            var stops=new List<Stop>();
            if(Loops[n].Length==route.Length) foreach(var (waypoint,barn) in BarnStops[n]) stops.Add(new Stop {Waypoint=waypoint,Barn=barn});
            p.Stops=stops.ToArray();
            p.StopAt=new float[p.Stops.Length];
            for(int i=0;i<p.Stops.Length;i++) p.StopAt[i]=ArcOf(p,route[p.Stops[i].Waypoint]);
            yield return 0;
            p.Model=Model(p);AddChild(p.Model);
            yield return 0;
            p.Sound=new DieselSound {Voice=DieselSound.EngineVoice.Armoured,BaseRate=38,RatePerSpeed=4,Body=.045f,Loudness=1,Reach=260,Position=new(0,1,2)};
            p.Model.AddChild(p.Sound);
            p.Gun=new HindGunSound {Position=new(0,2.4f,-2.4f)};p.Model.AddChild(p.Gun);
            p.Gun.VolumeDb=-9;p.Gun.UnitSize=40;p.Gun.MaxDistance=320;
            MarkTape(p);
            for(int i=0;i<3;i++) track.Obstacles.Add(new Obstacle(new(-10000,-10000),1.45f));
            _patrols.Add(p);
            yield return 0;
        }
        Reset();
    }
    /// <summary>The world tape parts of one BTR: the hull (it moves, the BtrPatrols node does not), turret, steering axles, spinning wheels, engine and gun sound.</summary>
    static void MarkTape(Patrol p)
    {
        string n=$"{p.Number+1}/";
        Tape.Part(p.Model,n+"hull");Tape.Part(p.Turret,n+"turret");
        for(int i=0;i<p.Steering.Count;i++) Tape.Part(p.Steering[i],n+"steer"+i);
        for(int i=0;i<p.Wheels.Count;i++) {Tape.Part(p.Wheels[i],n+"wheel"+i);Tape.Spinner(p.Wheels[i],p);}
        Tape.Sound(p.Model,TapeSoundChannel.DieselSpeed,()=>p.Sound.Speed,v=>p.Sound.Speed=v);
        Tape.Sound(p.Model,TapeSoundChannel.GunFiring,()=>p.Gun.Firing?1f:0f,v=>p.Gun.Firing=v>.5f);
    }
    Vector2[] Circle(int n)
    {
        Vector2 center=n==0?new(174,233):new(216,238);
        var route=new Vector2[8];
        for(int i=0;i<8;i++) route[i]=center+new Vector2(MathF.Cos(i*MathF.Tau/8)*12,MathF.Sin(i*MathF.Tau/8)*8);
        return route;
    }
    /// <summary>Samples the closed Catmull-Rom loop and nudges waypoints away from anything it touches.</summary>
    bool Fit(Vector2[] route,out Vector2[] dense)
    {
        dense=Sample(route);
        for(int attempt=0;attempt<80;attempt++)
        {
            Vector2? bad=null;int index=0;Vector2 away=Vector2.Zero;
            for(int i=0;i<dense.Length && bad==null;i++)
            {
                Vector2 p=dense[i];
                if(p.X<14 || p.Y<14 || p.X>_track.WorldSize-14 || p.Y>_track.WorldSize-14) {bad=p;away=new Vector2(_track.WorldSize/2-p.X,_track.WorldSize/2-p.Y).Normalized();}
                else if(BonusAirbase.Protected(p,6)) {bad=p;away=new Vector2(0,1);}
                else
                {
                    _track.MeshHeight(p.X,p.Y,out Vector3 normal);
                    if(normal.Y<.88f) {bad=p;away=new Vector2(normal.X,normal.Z).Normalized();if(away==Vector2.Zero) away=new Vector2(0,1);}
                    else foreach(var o in _track.Obstacles)
                        if(o.Center.X>-9000 && p.DistanceTo(o.Center)<o.Radius+3.1f) {bad=p;away=(p-o.Center).Normalized();break;}
                }
                if(bad!=null) index=i;
            }
            if(bad==null) return true;
            // The waypoint that owns this stretch of the loop moves away by 2.5 m (the spline passes through its waypoints).
            int nearest=0;float best=float.MaxValue;
            for(int w=0;w<route.Length;w++)
            {
                float d=route[w].DistanceTo(bad.Value);
                if(d<best) {best=d;nearest=w;}
            }
            route[nearest]+=away*2.5f;
            dense=Sample(route);
        }
        return false;
    }
    static Vector2[] Sample(Vector2[] route)
    {
        int n=route.Length;var points=new List<Vector2>();
        for(int i=0;i<n;i++)
        {
            Vector2 p0=route[(i+n-1)%n],p1=route[i],p2=route[(i+1)%n],p3=route[(i+2)%n];
            for(int k=0;k<24;k++)
            {
                float t=k/24f,t2=t*t,t3=t2*t;
                points.Add(.5f*(2*p1+(p2-p0)*t+(2*p0-5*p1+4*p2-p3)*t2+(3*p1-p0-3*p2+p3)*t3));
            }
        }
        return points.ToArray();
    }
    static void Measure(Patrol p)
    {
        int n=p.Dense.Length;p.Along=new float[n+1];
        for(int i=0;i<n;i++) p.Along[i+1]=p.Along[i]+p.Dense[i].DistanceTo(p.Dense[(i+1)%n]);
        p.Length=p.Along[n];
    }
    static float ArcOf(Patrol p,Vector2 waypoint)
    {
        int best=0;float distance=float.MaxValue;
        for(int i=0;i<p.Dense.Length;i++)
        {
            float d=p.Dense[i].DistanceTo(waypoint);
            if(d<distance) {distance=d;best=i;}
        }
        return p.Along[best];
    }
    static Vector2 At(Patrol p,float s,out Vector2 forward)
    {
        s=Mathf.PosMod(s,p.Length);
        int n=p.Dense.Length,i=Math.Clamp(Array.BinarySearch(p.Along,0,n,s),-1,n);
        if(i<0) i=~i-1;
        i=Math.Clamp(i,0,n-1);
        float span=p.Along[i+1]-p.Along[i];
        Vector2 a=p.Dense[i],b=p.Dense[(i+1)%n];
        forward=(b-a).Normalized();
        return a.Lerp(b,span>0?(s-p.Along[i])/span:0);
    }
    public void Reset()
    {
        _clock=0;
        foreach(var t in _tracers) {RemoveChild(t.Node);t.Node.Free();}
        _tracers.Clear();
        foreach(var p in _patrols)
        {
            p.S=p.Number*p.Length*.03f;p.Speed=p.Spin=p.Dust=0;p.State=0;p.Stopping=-1;p.TurretYaw=0;
            p.FireWait=_rng.RandfRange(18f,45f)+p.Number*20;
            p.Gun.Firing=false;p.Flash.Visible=false;p.Turret.Rotation=Vector3.Zero;
            Place(p,Vector2.Zero);
        }
    }
    public void Tick(float dt,Vector3? car=null)
    {
        if(dt<=0) return;
        _clock+=dt;
        foreach(var p in _patrols)
        {
            Vector2 before=At(p,p.S,out _);
            Drive(p,dt);
            Fire(p,dt,car);
            p.S=Mathf.PosMod(p.S+p.Speed*dt,p.Length);
            p.Spin+=p.Speed*dt/.64f;
            Vector2 after=At(p,p.S,out _);
            Vector2 velocity=(after-before)/dt;
            Place(p,velocity);
            Vector2 ahead=At(p,p.S+3,out Vector2 f2),here=At(p,p.S,out Vector2 f1);
            float turn=MathF.Atan2(f1.X*f2.Y-f1.Y*f2.X,f1.X*f2.X+f1.Y*f2.Y)/3f;
            foreach(var axle in p.Steering) axle.Rotation=new Vector3(0,-MathF.Atan(3.6f*turn),0);
            foreach(var wheel in p.Wheels) wheel.Rotation=new Vector3(p.Spin,0,0);
            p.Sound.Speed=p.Speed;
            if(_fx!=null && (p.Dust-=dt)<=0 && p.Speed>.5f)
            {
                p.Dust=.35f;
                DustFx(_fx,p.Model.Position+p.Model.Basis.Z*3+Vector3.Up*.3f);
            }
        }
        for(int i=_tracers.Count-1;i>=0;i--)
        {
            var t=_tracers[i];t.T+=dt;
            float u=Math.Clamp(t.T/t.Duration,0,1);
            Vector3 pos=t.From.Lerp(t.To,u);
            t.Node.Position=pos;
            if(u>=1)
            {
                if(_fx!=null) TracerEndFx(_fx,t.To);
                RemoveChild(t.Node);t.Node.Free();_tracers.RemoveAt(i);
            }
        }
    }
    /// <summary>The world tape's view of the effects made here (null: nothing records). Set by the race.</summary>
    public ITapeFxHost TapeFx;
    /// <summary>A tracer round (a thin glowing box; live play and the tape's replay make it alike).</summary>
    internal static MeshInstance3D TracerNode() => new MeshInstance3D {Mesh=new BoxMesh {Size=new(.05f,.05f,1.1f)},
        MaterialOverride=new StandardMaterial3D {AlbedoColor=new Color(1f,.82f,.3f),ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded,EmissionEnabled=true,Emission=new Color(1f,.7f,.2f),EmissionEnergyMultiplier=2f},
        CastShadow=GeometryInstance3D.ShadowCastingSetting.Off};
    /// <summary>The puff where a tracer ends.</summary>
    /// <summary>One puff of dust behind a rolling BTR (live play and the title replay's emitters).</summary>
    internal static void DustFx(TyreEffects fx,Vector3 at) => fx.Smoke(at,Vector3.Up*.6f,new(.48f,.43f,.32f),.35f,.6f,1.1f,1.2f);
    /// <summary>The BTRs' hulls, for the replay's dust emitters.</summary>
    internal IEnumerable<Node3D> TapeHulls() { foreach(var p in _patrols) yield return p.Model; }
    internal static void TracerEndFx(TyreEffects fx,Vector3 at) => fx.Smoke(at,new Vector3(.3f,1.1f,.1f),new(.46f,.40f,.30f),.3f,.55f,.9f,1.0f);
    /// <summary>Cruise, slow across the race road, stop at a barn when it is time to fire.</summary>
    void Drive(Patrol p,float dt)
    {
        Vector2 here=At(p,p.S,out _);
        int road=_track.NearestCenter(here,out float distance);
        float target=distance<_track.HalfWidth[road]+9?2.1f:3.4f;
        if(p.State==0 && p.FireWait<=0)
        {
            for(int i=0;i<p.Stops.Length;i++)
            {
                float ahead=Mathf.PosMod(p.StopAt[i]-p.S,p.Length);
                if(ahead<24) {p.Stopping=i;p.State=1;break;}
            }
        }
        if(p.State==1)
        {
            float ahead=Mathf.PosMod(p.StopAt[p.Stopping]-p.S,p.Length);
            if(ahead>p.Length-3) ahead=0;
            target=Math.Min(target,Math.Clamp(ahead*.22f,0,3.4f));
            if(ahead<.8f && p.Speed<.15f) {p.State=2;p.Timer=0;p.Fire=p.Stops[p.Stopping].Barn;p.Speed=0;}
        }
        else if(p.State>=2) target=0;
        float rate=target<p.Speed?1.5f:.8f;
        p.Speed=Mathf.MoveToward(p.Speed,target,rate*dt);
        if(p.State==0) p.FireWait-=dt;
    }
    void Fire(Patrol p,float dt,Vector3? car)
    {
        if(p.State<2) {Aim(p,dt,Vector2.Zero,false);p.Gun.Firing=false;p.Flash.Visible=false;return;}
        Vector3 roof=new(p.Fire.X,_track.MeshHeight(p.Fire.X,p.Fire.Y)+4.4f,p.Fire.Y);
        Aim(p,dt,p.Fire,true);
        p.Timer+=dt;
        if(p.State==2)
        {
            if(MathF.Abs(Mathf.AngleDifference(p.TurretYaw,TurretTarget(p,p.Fire)))<.05f && p.Timer>1.2f)
            {
                if(car is Vector3 c && SegmentDistance(new Vector2(c.X,c.Z),At(p,p.S,out _),p.Fire)<9f) {p.Timer=1.2f;return;}
                p.State=3;p.Timer=0;p.Rounds=0;p.Roundclock=0;p.Phase=0;p.Bursts++;
            }
            return;
        }
        if(p.State==3)
        {
            p.Gun.Firing=true;
            p.Roundclock+=dt;
            while(p.Roundclock>=.1f && p.Rounds<14)
            {
                p.Roundclock-=.1f;p.Rounds++;
                Vector3 muzzle=p.Turret.ToGlobal(new Vector3(0,2.32f,-2.3f));
                Vector3 hit=roof+new Vector3(_rng.RandfRange(-4f,4f),_rng.RandfRange(-.8f,.8f),_rng.RandfRange(-3f,3f));
                var tracer=TracerNode();
                AddChild(tracer);tracer.Position=muzzle;tracer.LookAtFromPosition(muzzle,hit);
                float duration=Math.Max(.08f,muzzle.DistanceTo(hit)/140f);
                _tracers.Add(new Tracer {Node=tracer,From=muzzle,To=hit,Duration=duration});
                TapeFx?.Fx(new TapeEvent(0f,TapeEventType.Projectile,Kind:TapeFxKind.BtrTracer,Projectile:TapeProjectile.Tracer,Pos:muzzle,Velocity:(hit-muzzle)/duration,Life:duration));
                p.Flash.Visible=true;
            }
            if(p.Roundclock>.05f) p.Flash.Visible=false;
            if(p.Rounds>=14) {p.Gun.Firing=false;p.Flash.Visible=false;p.State=4;p.Timer=0;}
            return;
        }
        // State 4: a pause, then another burst, then back to patrol.
        p.Gun.Firing=false;p.Flash.Visible=false;
        if(p.Timer>1.2f)
        {
            if(++p.Phase<2+p.Bursts%2) {p.State=3;p.Timer=0;p.Rounds=0;p.Roundclock=0;p.Bursts++;}
            else {p.State=0;p.Stopping=-1;p.Phase=0;p.FireWait=_rng.RandfRange(70f,150f);}
        }
    }
    static float SegmentDistance(Vector2 point,Vector2 a,Vector2 b)
    {
        Vector2 ab=b-a;float t=ab.LengthSquared()>0?Math.Clamp((point-a).Dot(ab)/ab.LengthSquared(),0,1):0;
        return point.DistanceTo(a+ab*t);
    }
    float TurretTarget(Patrol p,Vector2 target)
    {
        Vector3 local=p.Model.GlobalTransform.Basis.Inverse()*new Vector3(target.X-p.Model.GlobalPosition.X,0,target.Y-p.Model.GlobalPosition.Z);
        return MathF.Atan2(-local.X,-local.Z);
    }
    void Aim(Patrol p,float dt,Vector2 target,bool active)
    {
        float wanted=active?TurretTarget(p,target):0;
        p.TurretYaw=Mathf.MoveToward(p.TurretYaw,p.TurretYaw+Mathf.AngleDifference(p.TurretYaw,wanted),dt*(active?1.1f:.9f));
        p.Turret.Rotation=new Vector3(0,p.TurretYaw,0);
    }
    void Place(Patrol p,Vector2 velocity)
    {
        Vector2 at=At(p,p.S,out Vector2 forward);
        _track.MeshHeight(at.X,at.Y,out Vector3 normal);
        Vector3 f=new(forward.X,0,forward.Y);f=(f-normal*f.Dot(normal)).Normalized();
        Vector3 right=f.Cross(normal).Normalized();
        p.Model.Transform=new Transform3D(new Basis(right,(-f).Cross(right).Normalized(),-f),new(at.X,_track.MeshHeight(at.X,at.Y),at.Y));
        for(int i=0;i<3;i++) _track.Obstacles[p.Slot+i]=new Obstacle(at+forward*((i-1)*2.2f),1.45f,velocity, Material: ImpactMaterial.Metal);
    }
    public void SetAudioPaused(bool paused) {foreach(var p in _patrols) {p.Sound.StreamPaused=paused;if(paused) p.Gun.Firing=false;}}

    static Node3D Model(Patrol patrol)
    {
        var root=new Node3D {Name=$"BTR80_{patrol.Number+1}"};
        Color olive=new(.39f,.44f,.29f),steel=new(.25f,.28f,.20f),rubber=new(.10f,.11f,.085f);
        // The turret pivots about the hatch (z = -0.4); parts built while `sink` is the turret are shifted into its frame.
        var turret=new Node3D {Name="Turret",Position=new(0,0,-.4f)};root.AddChild(turret);patrol.Turret=turret;
        Node3D sink=root;
        void Add(MeshInstance3D part) {if(sink==turret) part.Position+=new Vector3(0,0,.4f);sink.AddChild(part);}
        void Box(Vector3 size,Color color,Vector3 at,Vector3 rotation=default)=>Add(Props.Part(Props.Box(size.X,size.Y,size.Z),color,at,rotation));
        // Chamfered bow and shoulders, rather than a rectangular armoured bus.
        var mesh=new SurfaceTool();mesh.Begin(Mesh.PrimitiveType.Triangles);
        Vector3[] low={new(-.85f,.8f,-3.55f),new(.85f,.8f,-3.55f),new(1.25f,.8f,-2.35f),new(1.25f,.8f,2.8f),new(.95f,.8f,3.5f),new(-.95f,.8f,3.5f),new(-1.25f,.8f,2.8f),new(-1.25f,.8f,-2.35f)};
        Vector3[] high={new(-.72f,1.65f,-3.05f),new(.72f,1.65f,-3.05f),new(1.04f,1.95f,-2.15f),new(1.04f,1.95f,2.65f),new(.85f,1.6f,3.25f),new(-.85f,1.6f,3.25f),new(-1.04f,1.95f,2.65f),new(-1.04f,1.95f,-2.15f)};
        void Tri(Vector3 a,Vector3 b,Vector3 c) {mesh.SetNormal((b-a).Cross(c-a).Normalized());mesh.AddVertex(a);mesh.AddVertex(b);mesh.AddVertex(c);}
        for(int i=0;i<8;i++) {int j=(i+1)%8;Tri(low[i],high[i],high[j]);Tri(low[i],high[j],low[j]);Tri(new(0,1.95f,0),high[j],high[i]);}
        root.AddChild(new MeshInstance3D {Mesh=mesh.Commit(),MaterialOverride=new StandardMaterial3D {AlbedoColor=olive,CullMode=BaseMaterial3D.CullModeEnum.Disabled}});
        for(int a=0;a<4;a++) foreach(float side in new[]{-1f,1f})
        {
            var axle=new Node3D {Position=new(side*1.32f,.64f,-2.4f+a*1.6f)};
            root.AddChild(axle);if(a<2) patrol.Steering.Add(axle);
            var spin=new Node3D();axle.AddChild(spin);patrol.Wheels.Add(spin);
            spin.AddChild(Props.Part(Props.Cyl(.64f,.4f,16),rubber,Vector3.Zero,new(0,0,90)));
            spin.AddChild(Props.Part(Props.Cyl(.31f,.44f,12),steel,Vector3.Zero,new(0,0,90)));
            spin.AddChild(Props.Part(Props.Cyl(.12f,.49f,10),olive,Vector3.Zero,new(0,0,90)));
            for(int bolt=0;bolt<8;bolt++)
            {
                float angle=bolt*MathF.Tau/8;
                spin.AddChild(Props.Part(Props.Cyl(.035f,.46f,6),new(.52f,.51f,.39f),new(0,MathF.Cos(angle)*.23f,MathF.Sin(angle)*.23f),new(0,0,90)));
            }
            for(int k=0;k<12;k++)
            {
                float angle=k*MathF.Tau/12;
                spin.AddChild(Props.Part(Props.Box(.43f,.09f,.19f),rubber,new(0,MathF.Cos(angle)*.62f,MathF.Sin(angle)*.62f),new(Mathf.RadToDeg(angle),0,15)));
            }
            if(a==0) Box(new(.23f,.12f,6.45f),steel,new(side*1.28f,1.35f,0));
        }
        sink=turret;
        Add(Props.Part(new CylinderMesh {TopRadius=.38f,BottomRadius=.65f,Height=.55f,RadialSegments=10},olive,new(0,2.18f,-.4f)));
        Box(new(.19f,.19f,1.85f),steel,new(0,2.32f,-1.7f));
        Box(new(.08f,.08f,1.2f),rubber,new(.27f,2.28f,-1.4f));
        Add(Props.Part(Props.Cyl(.38f,.08f,12),steel,new(0,2.5f,-.4f)));
        sink=root;
        foreach(float x in new[]{-.65f,.65f})
        {
            Box(new(.48f,.31f,.055f),steel,new(x,1.76f,-2.57f),new(-25,0,0));
            Box(new(.35f,.2f,.065f),new(.12f,.18f,.19f),new(x,1.77f,-2.61f),new(-25,0,0));
            Box(new(.52f,.065f,.43f),steel,new(x,2.01f,-1.8f));
            root.AddChild(NightGlow.Part(Props.Ball(.16f,8),new(.72f,.69f,.5f),new(.9f,.8f,.5f),.4f,new(x,1.24f,-3.3f)));
            Box(new(.19f,.12f,.08f),new(.45f,.08f,.04f),new(x,1.35f,3.48f));
        }
        foreach(float side in new[]{-1f,1f})
        {
            Box(new(.07f,.7f,.8f),steel,new(side*1.14f,1.53f,.1f));
            Box(new(.08f,.5f,.65f),olive,new(side*1.19f,1.56f,.1f));
            for(int i=0;i<4;i++) Box(new(.07f,.1f,.14f),rubber,new(side*1.12f,1.8f,-1.35f+i*.82f));
            Box(new(.09f,.09f,5.2f),steel,new(side*1.09f,2,0));
            Box(new(.25f,.23f,1.8f),steel,new(side*.9f,1.8f,2.2f));
        }
        for(int i=0;i<9;i++) Box(new(1.55f,.04f,.07f),steel,new(0,1.99f,1.3f+i*.15f));
        Box(new(.045f,1.6f,.045f),rubber,new(.8f,2.7f,.8f));
        Box(new(.85f,.3f,.9f),new(.4f,.39f,.26f),new(.2f,2.12f,2.4f));
        void Rod(Vector3 a,Vector3 b,float radius,Color color)
        {
            var part=Props.Part(Props.Cyl(radius,a.DistanceTo(b),8),color,(a+b)*.5f);
            part.Quaternion=new Quaternion(Vector3.Up,(b-a).Normalized());Add(part);
        }
        Color dark=new(.16f,.19f,.12f),worn=new(.52f,.50f,.35f),canvas=new(.49f,.46f,.30f);
        // Side split doors, hinge pins, grab handles, firing-port covers and welded seams.
        foreach(float side in new[]{-1f,1f})
        {
            Box(new(.025f,.025f,.78f),dark,new(side*1.24f,1.52f,.1f));
            Box(new(.035f,.055f,.14f),worn,new(side*1.25f,1.73f,.12f));
            foreach(float z in new[]{-.28f,.48f}) {
                Box(new(.055f,.56f,.035f),dark,new(side*1.24f,1.53f,z));
                foreach(float y in new[]{1.32f,1.77f}) Rod(new(side*1.26f,y-.06f,z),new(side*1.26f,y+.06f,z),.035f,steel);
            }
            foreach(float z in new[]{-1.4f,.95f,1.75f}) {
                root.AddChild(Props.Part(Props.Cyl(.10f,.045f,8),steel,new(side*1.15f,1.76f,z),new(0,0,90)));
                Box(new(.045f,.06f,.18f),dark,new(side*1.17f,1.79f,z));
            }
            for(int h=0;h<4;h++) {
                float z=-1.8f+h*1.12f;
                Rod(new(side*1.08f,2.03f,z),new(side*1.08f,2.12f,z),.025f,worn);
                Rod(new(side*1.08f,2.12f,z),new(side*1.08f,2.12f,z+.28f),.025f,worn);
                Rod(new(side*1.08f,2.12f,z+.28f),new(side*1.08f,2.03f,z+.28f),.025f,worn);
            }
            // Coiled towing cable and clasp on the rear side plate.
            for(int i=0;i<20;i++) {
                float a=i*MathF.Tau/20,b=(i+1)*MathF.Tau/20;
                Rod(new(side*1.18f,1.6f+MathF.Sin(a)*.22f,2.38f+MathF.Cos(a)*.48f),
                    new(side*1.18f,1.6f+MathF.Sin(b)*.22f,2.38f+MathF.Cos(b)*.48f),.025f,worn);
            }
            Box(new(.09f,.15f,.12f),steel,new(side*1.23f,1.83f,2.38f));
            Box(new(.055f,.27f,.68f),dark,new(side*1.16f,1.6f,-1.25f));
            var digits=CCCPRacing.UI.PixelFont.RenderImage((721+patrol.Number*3).ToString(),Colors.White,Colors.Transparent,0);
            for(int y=0;y<digits.GetHeight();y++) for(int x=0;x<digits.GetWidth();x++)
                if(digits.GetPixel(x,y).A>.5f) Box(new(.028f,.045f,.045f),new(.83f,.81f,.66f),new(side*1.2f,1.72f-y*.045f,.78f+(side>0?digits.GetWidth()-1-x:x)*.045f));
            digits.Dispose();
            // Mud flaps, suspension arms, rear exhaust and spaced exhaust shield.
            Box(new(.48f,.5f,.055f),rubber,new(side*1.31f,.65f,3.12f));
            for(int i=0;i<4;i++) Rod(new(side*.65f,.65f,-2.4f+i*1.6f),new(side*1.23f,.7f,-2.4f+i*1.6f),.09f,steel);
            Rod(new(side*.94f,1.78f,2.3f),new(side*.94f,1.78f,3.4f),.11f,dark);
            for(int i=0;i<5;i++) Box(new(.29f,.035f,.04f),worn,new(side*.94f,1.91f,2.45f+i*.17f));
        }
        // Folded bow wave board, towing eyes, protected headlights and winch fairlead.
        Box(new(1.52f,.36f,.07f),olive,new(0,1.13f,-3.5f),new(-18,0,0));
        Box(new(.55f,.16f,.12f),steel,new(0,.87f,-3.59f));
        foreach(float side in new[]{-1f,1f}) {
            foreach(float z in new[]{-3.48f,3.43f})
                root.AddChild(Props.Part(new TorusMesh {InnerRadius=.075f,OuterRadius=.125f,Rings=10,RingSegments=5},worn,new(side*.7f,.89f,z),new(90,0,0)));
            foreach(float x in new[]{-.13f,.13f}) Rod(new(side*.65f+x,1.08f,-3.38f),new(side*.65f+x,1.43f,-3.38f),.02f,steel);
            Rod(new(side*.65f-.13f,1.43f,-3.38f),new(side*.65f+.13f,1.43f,-3.38f),.02f,steel);
            // Driver hatches, hinges and three periscopes.
            Box(new(.42f,.035f,.34f),olive,new(side*.65f,2.06f,-1.8f));
            Rod(new(side*.65f-.22f,2.08f,-1.67f),new(side*.65f+.22f,2.08f,-1.67f),.025f,worn);
            for(int i=0;i<3;i++) {
                Box(new(.11f,.10f,.10f),steel,new(side*.65f+(i-1)*.14f,2.07f,-2.08f));
                Box(new(.085f,.035f,.015f),new(.19f,.29f,.29f),new(side*.65f+(i-1)*.14f,2.09f,-2.14f));
            }
        }
        // Turret vision blocks, hatch rim and handle, mantlet and perforated barrel sleeve.
        sink=turret;
        Add(Props.Part(new TorusMesh {InnerRadius=.31f,OuterRadius=.36f,Rings=12,RingSegments=5},worn,new(0,2.55f,-.4f)));
        Rod(new(-.10f,2.57f,-.4f),new(-.10f,2.64f,-.4f),.025f,steel);
        Rod(new(-.10f,2.64f,-.4f),new(.10f,2.64f,-.4f),.025f,steel);
        Box(new(.36f,.3f,.3f),olive,new(0,2.32f,-.95f));
        for(int i=0;i<6;i++) Box(new(.205f,.055f,.07f),rubber,new(0,2.41f,-1.3f-i*.19f));
        Box(new(.24f,.23f,.14f),steel,new(-.33f,2.5f,-.66f));
        Box(new(.13f,.1f,.02f),new(.18f,.29f,.3f),new(-.33f,2.54f,-.74f));
        sink=root;
        DrunkTractor.BatchStatic(turret,null);
        // Muzzle flash on the barrel tip, hidden except while firing.
        patrol.Flash=NightGlow.Part(Props.Ball(.22f,8),new(1f,.8f,.35f),new(1f,.7f,.2f),3f,new(0,2.32f,-2.5f));
        patrol.Flash.Visible=false;turret.AddChild(patrol.Flash);
        // Tools and stowage are strapped down, with distinct engine-deck grilles.
        Rod(new(-.8f,2.09f,.65f),new(-.8f,2.09f,2.45f),.035f,canvas);
        Box(new(.35f,.045f,.45f),steel,new(-.8f,2.09f,2.55f));
        Rod(new(-.6f,2.07f,.8f),new(-.6f,2.07f,1.7f),.04f,canvas);
        Box(new(.4f,.09f,.15f),steel,new(-.6f,2.07f,.75f));
        foreach(float z in new[]{2.16f,2.58f}) Box(new(.9f,.035f,.075f),dark,new(.2f,2.28f,z));
        for(int i=0;i<4;i++) Box(new(.035f,.06f,.65f),olive,new(-.6f+i*.4f,2.03f,1.92f));
        // Merge fixed geometry; only the eight wheels and lamps need separate LAN poses.
        foreach(var wheel in patrol.Wheels) DrunkTractor.BatchStatic(wheel,null);
        var dynamicChildren=new List<Node>();
        foreach(Node child in root.GetChildren())
            if(child is Node3D && child is not MeshInstance3D || child is MeshInstance3D m && m.MaterialOverride is StandardMaterial3D mat && mat.EmissionEnabled)
                dynamicChildren.Add(child);
        foreach(var child in dynamicChildren) root.RemoveChild(child);
        DrunkTractor.BatchStatic(root,null);
        foreach(var child in dynamicChildren) root.AddChild(child);
        MilitaryLights.Vehicle(root,new(0,1.24f,-3.42f),new(0,1.35f,3.55f),.65f);
        return root;
    }
}
