using System;
using System.Collections.Generic;
using CCCPRacing.Audio;
using CCCPRacing.Car;
using Godot;

namespace CCCPRacing.Track;

/// <summary>Ground routines around the exercise: a marshal, battery crew and a very steady cook.</summary>
public partial class BonusExerciseLife : Node3D
{
    sealed class Worker
    {
        public DrunkMen.Figure Figure;
        public Vector3 Home;
        public int Job;
        public float WalkPhase, WorkPhase;
        public Node3D Tool, Flag;
        public readonly Knockdown Down = new();
    }
    readonly List<Worker> _workers = new();
    TrackData _track;
    TyreEffects _fx;
    float _time, _smoke, _alarm, _kitchenCooldown;
    Vector3 _shot;
    AudioStreamPlayer _radio;
    DieselSound _generator,_campGenerator;
    Node3D _lid, _stoveGlow, _ladle, _cookSteam, _combineWheels, _combineReel;
    public event Action SoupUnshaken;
    static readonly Color Olive = new(.28f,.32f,.19f), Steel = new(.22f,.23f,.2f), Rust = new(.36f,.22f,.12f);
    internal static readonly Vector2 RadioOperatorPost = new(301,160);

    public void Init(TrackData track, TyreEffects fx)
    {
        foreach (var _ in InitSteps(track, fx)) { }
    }

    public System.Collections.Generic.IEnumerable<int> InitSteps(TrackData track, TyreEffects fx)
    {
        _track=track; _fx=fx;
        AddChild(BuildScenery());
        yield return 0;
        AddChild(BonusCampDetails.Build(track));
        yield return 0;
        Person(new Vector2(97,121),0); // marshal at the apron edge, clear of the landing pads
        yield return 0;
        Person(RadioOperatorPost,1); // battery crew on the far side of the launchers, away from the road
        yield return 0;
        Person(new Vector2(291,172),1);
        yield return 0;
        Person(new Vector2(126,198),2);
        yield return 0;
        Person(new Vector2(BonusAirbase.Kitchen.X-1.1f,BonusAirbase.Kitchen.Y+.2f),3); // the cook at the cooker's south-east corner, stirring
        yield return 0;
        for(int k=0;k<3;k++) { Person(BonusAirbase.Kitchen+new Vector2(1+k*1.2f,4),4); yield return 0; }
        Person(new Vector2(207f,114.5f),5); // off-duty guard on a crate at the foot of the east tower
        yield return 0;
        _radio=new AudioStreamPlayer { Bus=AudioMix.BusFor(AudioMix.Warning),VolumeDb=-10 };
        AddChild(_radio); _radio.Stream=RadioSignal();
        _generator=new DieselSound { Position=Ground(track,288,149),BaseRate=18,RatePerSpeed=0,Body=.025f,Loudness=-9,Reach=65 };
        AddChild(_generator);
        // The camp's own set hums quietly beside the kitchen.
        _campGenerator=new DieselSound { Position=Ground(track,BonusCampLayout.Generator.X,BonusCampLayout.Generator.Y),BaseRate=16,RatePerSpeed=0,Body=.03f,Loudness=-12,Reach=45 };
        AddChild(_campGenerator);
        Reset();
        yield return 0;
    }
    void Person(Vector2 at,int job)
    {
        var f=DrunkMen.BuildFigure(job==3?new Color(.72f,.7f,.61f):Olive);
        f.Bottle.Visible=false;
        Vector3 home=Ground(_track,at.X,at.Y);
        f.Root.Position=home; AddChild(f.Root);
        f.Head.AddChild(Props.Part(Props.Ball(.19f,8),job==3?new Color(.85f,.83f,.74f):Olive,new Vector3(0,.08f,0)));
        // Distinct silhouettes: reflective marshal vest, radio harness, rifle sling, cook apron.
        if(job==0)
        {
            f.Body.AddChild(Props.Part(Props.Box(.51f,.2f,.315f),new Color(.92f,.43f,.06f),new Vector3(0,1.06f,-.012f)));
            for(int side=-1;side<=1;side+=2)
                f.Body.AddChild(Props.Part(Props.Box(.055f,.56f,.02f),new Color(.92f,.8f,.23f),new Vector3(side*.14f,1.06f,-.17f)));
            f.ArmR.AddChild(Props.Part(Props.Box(.07f,.7f,.07f),new Color(.94f,.5f,.09f),new Vector3(0,-.55f,0)));
        }
        if(job==1)
        {
            f.Body.AddChild(Props.Part(Props.Box(.5f,.2f,.34f),new Color(.12f,.14f,.12f),new Vector3(0,.78f,0)));
            f.Body.AddChild(Props.Part(Props.Box(.28f,.22f,.19f),new Color(.1f,.12f,.1f),new Vector3(0,1.38f,-.04f)));
            f.Head.AddChild(Props.Part(Props.Box(.34f,.08f,.37f),new Color(.3f,.34f,.22f),new Vector3(0,.16f,0)));
        }
        if(job==2)
        {
            f.Body.AddChild(Props.Part(Props.Box(.53f,.2f,.35f),new Color(.13f,.15f,.13f),new Vector3(0,.9f,0)));
            f.Body.AddChild(Props.Part(Props.Cyl(.16f,.08f,8),new Color(.12f,.13f,.11f),new Vector3(.29f,1.12f,0),new Vector3(0,0,90)));
            f.Head.AddChild(Props.Part(Props.Box(.34f,.08f,.37f),Olive,new Vector3(0,.15f,0)));
        }
        if(job==3)
        {
            f.Body.AddChild(Props.Part(Props.Box(.58f,.54f,.035f),new Color(.53f,.44f,.27f),new Vector3(0,1.03f,-.17f)));
            f.Head.AddChild(Props.Part(Props.Cyl(.26f,.12f,10),new Color(.72f,.7f,.62f),new Vector3(0,.19f,0)));
            f.Head.AddChild(Props.Part(Props.Cyl(.06f,.36f,8),Steel,new Vector3(0,.23f,0),new Vector3(90,0,0)));
            var worker=new Worker { Figure=f,Home=home,Job=job };
        if(job==0)
        {
            worker.Flag=new Node3D(); f.ArmR.AddChild(worker.Flag);
            worker.Flag.AddChild(Props.Part(Props.Cyl(.025f,1.2f,6),Steel,new Vector3(0,-.65f,0)));
            worker.Flag.AddChild(Props.Part(Props.Box(.7f,.4f,.025f),new Color(.78f,.08f,.04f),new Vector3(.28f,-1.1f,0)));
        }
        if(job==2)
        {
            worker.Tool=new Node3D();f.ArmR.AddChild(worker.Tool);
            worker.Tool.AddChild(Props.Part(Props.Cyl(.035f,.95f,8),new Color(.38f,.25f,.13f),new Vector3(0,-.62f,0)));
            worker.Tool.AddChild(Props.Part(Props.Box(.58f,.42f,.1f),new Color(.68f,.06f,.035f),new Vector3(.32f,-1.08f,0)));
        }
        _workers.Add(worker);
            return;
        }
        if(job==2)
        {
            f.ArmR.AddChild(Props.Part(Props.Cyl(.025f,1.2f,6),Steel,new Vector3(0,-.65f,0)));
            f.ArmR.AddChild(Props.Part(Props.Box(.6f,.4f,.025f),new Color(.7f,.05f,.03f),new Vector3(.3f,-1,0)));
        }
        if(job==3)
        {
            f.ArmR.AddChild(Props.Part(Props.Cyl(.025f,.8f,6),Steel,new Vector3(0,-.65f,0)));
            f.ArmR.AddChild(Props.Part(Props.Ball(.1f,6),Steel,new Vector3(0,-1.05f,0)));
        }
        if(job==4)
        {
            // Mess tin held in the queue.
            f.ArmR.AddChild(Props.Part(Props.Cyl(.1f,.06f,8),Steel,new Vector3(0,-.5f,-.12f)));
            f.Bottle.Visible=false;
        }
        if(job==5)
        {
            f.ArmR.AddChild(NightGlow.Part(Props.Box(.025f,.025f,.06f),new Color(.9f,.35f,.1f),new Color(1f,.4f,.1f),1.4f,new Vector3(0,-.55f,-.1f)));
            var crate=Props.Part(Props.Box(.7f,.5f,.5f),Rust,new Vector3(home.X,home.Y+.25f,home.Z+.15f));
            AddChild(crate);
        }
        _workers.Add(new Worker { Figure=f,Home=home,Job=job });
    }
    public void Reset()
    {
        _time=_smoke=_alarm=_kitchenCooldown=0;
        if(_combineWheels!=null) _combineWheels.Rotation=Vector3.Zero;
        if(_combineReel!=null) _combineReel.Rotation=Vector3.Zero;
        _radio?.Stop();
        foreach(var w in _workers) { w.Down.Reset(); w.Figure.Root.Position=w.Home; }
    }
    public void Shot(Vector3 at,CarSim car)
    {
        _shot=at; _alarm=2.8f;
        if(car!=null && at.DistanceTo(Ground(_track,BonusAirbase.Kitchen.X,BonusAirbase.Kitchen.Y))<260 && car.Pos.DistanceTo(Ground(_track,BonusAirbase.Kitchen.X,BonusAirbase.Kitchen.Y))<60 && _kitchenCooldown<=0)
        { SoupUnshaken?.Invoke(); _kitchenCooldown=75; }
    }
    public void RadioCall(Vector3 target,CarSim car)
    {
        if(car!=null && target.DistanceTo(car.Pos)<230) _radio.Play();
    }
    public void SetAudioPaused(bool paused) { _radio.StreamPaused=paused; _generator.StreamPaused=paused; _campGenerator.StreamPaused=paused; }
    public void Tick(float dt,CarSim car,Vector3? taxi,bool batteryAlert)
    {
        _time+=dt; _alarm=Math.Max(0,_alarm-dt); _kitchenCooldown-=dt; _smoke-=dt;
        if(_smoke<=0)
        {
            _smoke=.24f;
            _fx.Smoke(Ground(_track,169,38)+Vector3.Up*3.2f,new Vector3(.4f,1.2f,.1f),new Color(.3f,.31f,.28f),.55f,.35f,1.5f,3);
            if(taxi is Vector3 jet && jet.DistanceTo(Ground(_track,62,45))<45)
                _fx.Smoke(jet+Vector3.Up*.2f,new Vector3(.5f,1,1),new Color(.5f,.44f,.32f),.25f,.8f,3,1.5f);
        }
        if(_combineWheels!=null) _combineWheels.Rotation=new Vector3(MathF.Sin(_time*.13f)*.004f,0,MathF.Sin(_time*.17f)*.003f);
        if(_combineReel!=null) _combineReel.RotateX(dt*1.1f);
        if(_cookSteam!=null) _cookSteam.Scale=new Vector3(1,.8f+MathF.Sin(_time*3)*.16f,1);
        foreach(var w in _workers)
        {
            var f=w.Figure;
            if(car!=null && new Vector2(car.Pos.X-f.Root.Position.X,car.Pos.Z-f.Root.Position.Z).Length()<1.1f && car.Speed>2) w.Down.Hit(car.Vel);
            w.Down.Tick(dt);
            Vector3 target=w.Home;
            bool signal=w.Job==0 && taxi.HasValue && taxi.Value.DistanceTo(w.Home)<50;
            if(signal) target+=new Vector3(3,0,-2); // step out of the way while guiding the aircraft
            if(w.Job==1 && batteryAlert) target+=new Vector3(w.Home.X<280?-3:3,0,2);
            target.Y=_track.MeshHeight(target.X,target.Z);
            Vector3 oldPosition=f.Root.Position;
            f.Root.Position=f.Root.Position.MoveToward(target,dt*(signal||batteryAlert?2.8f:1.8f));
            f.Root.Position=new Vector3(f.Root.Position.X,_track.MeshHeight(f.Root.Position.X,f.Root.Position.Z),f.Root.Position.Z);
            float movement=new Vector2(f.Root.Position.X-oldPosition.X,f.Root.Position.Z-oldPosition.Z).Length();
            if(movement>.0001f) w.WalkPhase+=movement*5.5f;
            Vector3 face=signal?taxi.Value-f.Root.Position:new Vector3(0,0,-1);
            if(w.Job==1) face=new Vector3(281,0,155)-f.Root.Position;
            if(w.Job==3) face=new Vector3(BonusAirbase.Kitchen.X-3,0,BonusAirbase.Kitchen.Y-1)-f.Root.Position; // the cook faces the pot he stirs
            float yaw=TrackData.YawFor(new Vector2(face.X,face.Z));
            f.Root.Basis=w.Down.Tilt*new Basis(Vector3.Up,yaw).Scaled(Vector3.One*1.6f);
            bool duck=_alarm>0 && w.Job!=3 && w.Home.DistanceTo(_shot)<260;
            f.Body.Position=new Vector3(0,duck?-.45f:0,0);
            f.Body.Rotation=new Vector3(duck?-.55f:0,0,0);
            float stride=MathF.Sin(w.WalkPhase)*Math.Clamp(movement*24,0,1);
            f.LegL.Rotation=new Vector3(stride+(duck?.5f:0),0,0);
            f.LegR.Rotation=new Vector3(-stride+(duck?.5f:0),0,0);
            float workPulse=MathF.Sin(_time*(w.Job==1?2.8f:4f));
            if(w.Job==0 && signal) { f.LegL.Rotation=new Vector3(stride-.2f,0,0); f.LegR.Rotation=new Vector3(-stride-.2f,0,0); }
            if(w.Job==2 && !duck) f.ArmR.Rotation=new Vector3(1.4f+workPulse*.8f,0,-.2f);
            float arm=signal?1.55f+MathF.Sin(_time*5)*.7f:w.Job==1?(batteryAlert?2.6f:1.35f+workPulse*.45f):w.Job==2?1.7f+workPulse*.18f:0;
            f.ArmR.Rotation=new Vector3(duck?2.2f:arm,0,signal?-.55f:duck?.25f:0);
            if(w.Tool!=null) w.Tool.Rotation=new Vector3(MathF.Sin(_time*5)*.3f,0,0);
            if(w.Flag!=null) w.Flag.Rotation=new Vector3(signal?MathF.Sin(_time*8)*.22f:0,0,signal?MathF.Sin(_time*5)*.35f:0);
            if(w.Job==1)
            {
                f.Body.Position=new Vector3(0,batteryAlert?-.10f:0,0);
                f.Body.Rotation=new Vector3(batteryAlert?-.18f:MathF.Sin(_time*1.4f)*.025f,0,batteryAlert?MathF.Sin(_time*4)*.025f:0);
                f.Head.Rotation=new Vector3(batteryAlert?.15f:0,MathF.Sin(_time*1.7f+w.Home.X)*.25f,0);
            }
            else if(w.Job==3)
            {
                w.WorkPhase+=dt;
                f.Body.Position=new Vector3(0,MathF.Sin(w.WorkPhase*2)*.025f,0);
                f.Body.Rotation=new Vector3(MathF.Sin(w.WorkPhase*1.5f)*.07f,0,MathF.Sin(w.WorkPhase*2)*.04f);
                f.Head.Rotation=new Vector3(.18f+MathF.Sin(w.WorkPhase*1.3f)*.08f,MathF.Sin(w.WorkPhase*.8f)*.15f,0);
                _ladle.Rotation=new Vector3(0,0,MathF.Sin(w.WorkPhase*3)*.5f);
                _lid.Rotation=new Vector3(.04f+MathF.Sin(w.WorkPhase*2.1f)*.035f,0,MathF.Sin(w.WorkPhase*1.7f)*.04f);
                _stoveGlow.Scale=new Vector3(1,.72f+MathF.Sin(w.WorkPhase*7)*.18f,1);
                f.ArmR.Rotation=new Vector3(1.45f+MathF.Sin(w.WorkPhase*3)*.28f,0,-.22f);
            }
            else if(w.Job==5)
            {
                // Sitting on the crate: hips down, legs forward, an occasional drag on the cigarette.
                float drag=MathF.Max(0,MathF.Sin(_time*.55f))*MathF.Max(0,MathF.Sin(_time*.55f));
                f.Body.Position=new Vector3(0,-.42f,0);
                f.Body.Rotation=new Vector3(.05f,0,0);
                f.LegL.Rotation=f.LegR.Rotation=new Vector3(1.5f,0,0);
                f.ArmR.Rotation=new Vector3(.5f+1.9f*drag,0,-.1f);
                f.Head.Rotation=new Vector3(.1f-.12f*drag,MathF.Sin(_time*.3f+w.Home.X)*.5f,0);
            }
            else
            {
                f.Body.Position=new Vector3(0,MathF.Abs(stride)*.04f,0);
                f.Body.Rotation=new Vector3(duck?-.55f:0,0,signal?MathF.Sin(_time*8)*.1f:MathF.Sin(_time*1.2f+w.Home.X)*.05f);
                f.Head.Rotation=new Vector3(0,MathF.Sin(_time*.6f+w.Home.Z)*.2f,0);
            }
        }
    }
    static AudioStreamWav RadioSignal()
    {
        const int rate=22050; var data=new byte[rate]; var rng=new Random(25);
        for(int i=0;i<data.Length/2;i++)
        {
            float t=i/(float)rate;
            bool tone=t<.09f || (t>.17f && t<.28f);
            float envelope=Math.Min(1,t*100)*Math.Min(1,(.5f-t)*50);
            float value=((tone?MathF.Sin(t*MathF.Tau*(t<.1f?1250:900))*.35f:0)+(float)(rng.NextDouble()*2-1)*.07f)*envelope;
            short sample=(short)(value*26000); data[i*2]=(byte)sample; data[i*2+1]=(byte)(sample>>8);
        }
        return new AudioStreamWav { Format=AudioStreamWav.FormatEnum.Format16Bits,MixRate=rate,Data=data };
    }
    static void BatchWithDynamics(Node3D site,params Node3D[] animated)
    {
        var saved=new List<(Node3D node,Transform3D transform)>();
        foreach(var node in animated)
        {
            saved.Add((node,node.Transform)); site.RemoveChild(node);
        }
        DrunkTractor.BatchStatic(site,null);
        foreach(var (node,transform) in saved) { site.AddChild(node); node.Transform=transform; }
    }
    static Vector3 Ground(TrackData t,float x,float z) => new(x,t.MeshHeight(x,z),z);

    /// <summary>Static dressing stays visible independently of the military actors.</summary>
    Node3D BuildScenery()
    {
        var t=_track; var root=new Node3D { Name="BonusExerciseScenery" };
        Node3D Site(float x,float z)
        { var n=new Node3D { Position=Ground(t,x,z) };root.AddChild(n);return n; }
        void Box(Node3D n,Vector3 size,Vector3 at,Color color) => n.AddChild(Props.Part(Props.Box(size.X,size.Y,size.Z),color,at));
        // Aircraft service trolley, opened access panel and tool tray beside the mechanic.
        var service=Site(92,119);
        Box(service,new(1.6f,.22f,1),new(0,.7f,0),Olive);
        foreach(float x in new[]{-.6f,.6f}) foreach(float z in new[]{-.35f,.35f})
            service.AddChild(Props.Part(Props.Cyl(.22f,.14f,8),Steel,new Vector3(x,.3f,z),new Vector3(0,0,90)));
        Box(service,new(1.25f,.08f,.7f),new(0,1.35f,0),Steel);
        Box(service,new(.8f,.1f,.45f),new(.1f,1.47f,0),Rust);
        var panel=Props.Part(Props.Box(1.1f,.06f,.8f),Olive,new Vector3(-1,.5f,0),new Vector3(0,0,60));service.AddChild(panel);
        for(int k=0;k<7;k++) Box(service,new(.035f,.04f,.3f),new(-.45f+k*.14f,1.55f,0),new Color(.6f,.61f,.57f));
        service.AddChild(Props.Part(Props.Cyl(.19f,.08f,12),new Color(.12f,.13f,.12f),new Vector3(.35f,1.55f,.35f),new Vector3(90,0,0)));
        service.AddChild(Props.Part(Props.Cyl(.12f,.025f,10),new Color(.83f,.12f,.06f),new Vector3(.35f,1.6f,.3f),new Vector3(90,0,0)));
        for(int k=0;k<4;k++) Box(service,new(.045f,.08f,.52f),new(-.48f+k*.22f,1.62f,.36f),k==2?new Color(.13f,.17f,.2f):Steel);
        for(int side=-1;side<=1;side+=2)
        {
            service.AddChild(Props.Part(Props.Cyl(.035f,.95f,6),Rust,new Vector3(side*.78f,.42f,.48f),new Vector3(0,0,side*16)));
            Box(service,new(.28f,.06f,.18f),new(side*.78f,.1f,.48f),Steel);
            Box(service,new(.3f,.12f,.18f),new(side*.78f,1.65f,-.35f),new Color(.42f,.08f,.04f));
        }
        // Field kitchen: a wheeled cooker, chimney, serving table, bowls and ration sacks.
        var kitchen=Site(BonusAirbase.Kitchen.X-3,BonusAirbase.Kitchen.Y-1);
        Box(kitchen,new(2.4f,1.1f,1.6f),new(0,1,0),Olive);
        foreach(float x in new[]{-1.3f,1.3f}) kitchen.AddChild(Props.Part(Props.Cyl(.55f,.24f,12),Steel,new Vector3(x,.55f,0),new Vector3(0,0,90)));
        var pot=Props.Part(Props.Cyl(.85f,.18f,12),Steel,new Vector3(0,1.6f,0));kitchen.AddChild(pot);
        _lid=new Node3D { Position=new Vector3(0,1.72f,0) }; kitchen.AddChild(_lid);
        _lid.AddChild(Props.Part(Props.Cyl(.82f,.1f,12),new Color(.27f,.28f,.25f),Vector3.Zero));
        _lid.AddChild(Props.Part(Props.Cyl(.12f,.16f,8),Steel,new Vector3(0,.12f,0)));
        _stoveGlow=new Node3D { Position=new Vector3(0,.94f,0) }; kitchen.AddChild(_stoveGlow);
        var flame=Props.Part(Props.Ball(.34f,8),new Color(1,.28f,.035f),Vector3.Zero); flame.Scale=new Vector3(1,.7f,1);_stoveGlow.AddChild(flame);
        _ladle=new Node3D { Position=new Vector3(0,1.82f,.1f) }; kitchen.AddChild(_ladle);
        _ladle.AddChild(Props.Part(Props.Cyl(.035f,.85f,6),new Color(.48f,.34f,.18f),new Vector3(.35f,.12f,0),new Vector3(0,0,-52)));
        _ladle.AddChild(Props.Part(Props.Ball(.12f,6),Steel,new Vector3(.7f,.28f,0)));
        _cookSteam=new Node3D { Position=new Vector3(0,3.45f,0) };kitchen.AddChild(_cookSteam);
        for(int puff=0;puff<4;puff++)
        {
            var wisp=Props.Part(Props.Ball(.16f+puff*.025f,7),new Color(.38f,.39f,.35f,.32f),
                new Vector3(MathF.Sin(puff*2)*.1f,puff*.3f,MathF.Cos(puff)*.08f));
            wisp.Scale=new Vector3(.6f,1.4f,.6f);_cookSteam.AddChild(wisp);
        }
        kitchen.AddChild(Props.Part(Props.Cyl(.12f,2.2f,8),Steel,new Vector3(0,2.2f,0)));
        Box(kitchen,new(.6f,.08f,.5f),new(0,3.35f,0),Steel);
        Box(kitchen,new(2.5f,.12f,.8f),new(3,1,2),Rust);
        foreach(float x in new[]{2,4}) Box(kitchen,new(.12f,1,.6f),new(x,.5f,2),Steel);
        for(int k=0;k<4;k++)
        {
            var bowl=Props.Part(Props.Cyl(.16f,.1f,8),new Color(.6f,.6f,.53f),new Vector3(2.2f+k*.5f,1.12f,2));kitchen.AddChild(bowl);
            kitchen.AddChild(Props.Part(Props.Cyl(.12f,.025f,8),new Color(.38f,.3f,.16f),new Vector3(2.2f+k*.5f,1.18f,2)));
        }
        for(int k=0;k<3;k++)
        {
            var sack=Props.Part(Props.Ball(.36f,8),new Color(.43f,.37f,.24f),new Vector3(-2,.55f+k*.2f,1+k*.22f));sack.Scale=new Vector3(1.1f,.85f,1.3f);kitchen.AddChild(sack);
            kitchen.AddChild(Props.Part(Props.Cyl(.12f,.22f,8),new Color(.25f,.27f,.2f),new Vector3(-2,.88f+k*.2f,1+k*.22f)));
        }
        // Three legged cookfire, side draught doors and firebox seams.
        for(int side=-1;side<=1;side+=2)
            kitchen.AddChild(Props.Part(Props.Box(.12f,.65f,.14f),Steel,new Vector3(side*.78f,.36f,.45f),new Vector3(0,0,side*12)));
        Box(kitchen,new(.75f,.43f,.05f),new(0,.95f,-.82f),Steel);
        for(int k=0;k<5;k++) Box(kitchen,new(.07f,.04f,.02f),new(-.22f+k*.11f,.98f,-.85f),new Color(.07f,.07f,.06f));
        for(int k=0;k<4;k++) Box(kitchen,new(.7f,.4f,.5f),new(-2,.2f+k%2*.4f,1+k/2*.6f),new Color(.47f,.4f,.27f));
        // Earth firing positions for the tank exercise.
        foreach(var at in new[]{new Vector2(73,198),new Vector2(103,289)})
        {
            var site=Site(at.X,at.Y);
            for(int k=0;k<9;k++) site.AddChild(Props.Part(Props.Ball(1.5f,8),new Color(.32f,.29f,.18f),new Vector3(-5+k*1.3f,.15f,5)));
            foreach(float side in new[]{-1f,1f}) for(int k=0;k<16;k++)
                Box(site,new(.65f,.025f,.18f),new(side*1.5f,.035f,-5+k*.6f),new Color(.17f,.16f,.12f));
        }
        Vector2 radioDirection=(new Vector2(281,155)-RadioOperatorPost).Normalized();
        Vector2 radioTable=RadioOperatorPost+radioDirection*1.6f;
        var phone=Site(radioTable.X,radioTable.Y);
        phone.Rotation=new Vector3(0,TrackData.YawFor(radioDirection),0);
        Box(phone,new(1.2f,.12f,.6f),new(0,.9f,0),Rust);
        foreach(float x in new[]{-.5f,.5f}) Box(phone,new(.08f,.9f,.4f),new(x,.45f,0),Steel);
        Box(phone,new(.35f,.22f,.25f),new(0,1.07f,0),Olive);
        Box(phone,new(.42f,.08f,.1f),new(0,1.23f,0),Steel);
        for(int k=0;k<12;k++)
            phone.AddChild(Props.Part(Props.Box(.035f,.035f,.35f),Steel,new Vector3(.9f+MathF.Cos(k*MathF.Tau/12)*.4f,.4f+MathF.Sin(k*MathF.Tau/12)*.4f,0),new Vector3(0,0,k*30)));
        // Rusted combine: stepped chassis, tapered grain hopper and exposed drive machinery.
        var combine=Site(BonusAirbase.AbandonedCombine.X,BonusAirbase.AbandonedCombine.Y);combine.RotationDegrees=new Vector3(3,25,-7);
        Box(combine,new(2.9f,1.65f,3.9f),new(0,1.65f,.15f),Rust);
        Box(combine,new(2.3f,.28f,4.1f),new(0,.64f,.15f),Steel);
        foreach(float side in new[]{-1f,1f})
            combine.AddChild(Props.Part(Props.Box(.58f,.75f,3.8f),Rust.Darkened(.12f),new Vector3(side*1.3f,.85f,.15f),new Vector3(0,0,side*18)));
        // Raised grain tank and driver's cab give the old machine a clear harvester silhouette.
        var hopper=new SurfaceTool();hopper.Begin(Mesh.PrimitiveType.Triangles);
        Vector3[] lower={new(-.55f,2.5f,.02f),new(1.05f,2.5f,.02f),new(1.05f,2.5f,1.28f),new(-.55f,2.5f,1.28f)};
        Vector3[] upper={new(-1f,3.45f,-.4f),new(1.5f,3.45f,-.4f),new(1.5f,3.45f,1.7f),new(-1f,3.45f,1.7f)};
        for(int side=0;side<4;side++)
        {
            int next=(side+1)%4;
            foreach(Vector3 p in new[]{lower[side],upper[next],upper[side],lower[side],lower[next],upper[next]}) hopper.AddVertex(p);
        }
        hopper.GenerateNormals();hopper.SetMaterial(new StandardMaterial3D {AlbedoColor=new(.29f,.31f,.22f),Roughness=1,CullMode=BaseMaterial3D.CullModeEnum.Disabled});
        combine.AddChild(new MeshInstance3D {Mesh=hopper.Commit()});
        Box(combine,new(2.35f,.08f,1.95f),new(.25f,3.3f,.65f),new Color(.12f,.14f,.09f));
        for(int edge=0;edge<4;edge++)
        {
            Vector3 a=upper[edge],b=upper[(edge+1)%4];
            var lip=Props.Part(Props.Box(.065f,.065f,a.DistanceTo(b)),Steel,(a+b)*.5f);
            lip.Basis=Basis.LookingAt((b-a).Normalized());combine.AddChild(lip);
        }
        Box(combine,new(1.45f,1.45f,1.45f),new(-.55f,3,-1),new Color(.31f,.34f,.24f));
        Box(combine,new(1.23f,.78f,.055f),new(-.55f,3.13f,-1.75f),new Color(.10f,.17f,.17f));
        // Framed, dusty glazing, a roof lip and a door make the cab read as a cab at track distance.
        foreach(float xCab in new[]{-1.2f,.1f}) Box(combine,new(.09f,1.18f,.09f),new(xCab,3,-1.78f),Steel);
        Box(combine,new(1.55f,.09f,.12f),new(-.55f,3.67f,-1.74f),Rust);
        Box(combine,new(.07f,1.18f,.09f),new(.2f,3,-.95f),Steel);
        Box(combine,new(.55f,.08f,.08f),new(-.55f,2.47f,-1.78f),Steel);
        for(int bolt=0;bolt<5;bolt++) Box(combine,new(.075f,.075f,.04f),new(-1.02f+bolt*.23f,3.78f,-1.1f),new Color(.65f,.48f,.25f));
        // Engine grille, worn warning stripe, headlamps and the access ladder on the left flank.
        for(int slat=0;slat<7;slat++) Box(combine,new(.045f,.85f,.05f),new(-1.57f,1.6f+slat*.12f,.25f),Steel);
        Box(combine,new(.045f,.17f,2.7f),new(-1.57f,2.3f,.15f),new Color(.63f,.47f,.16f));
        foreach(float zLamp in new[]{-1.5f,-.8f}) Box(combine,new(.22f,.17f,.12f),new(-1.55f,2.25f,zLamp),new Color(.77f,.62f,.31f));
        for(int rung=0;rung<5;rung++) Box(combine,new(.55f,.055f,.06f),new(1.68f,.25f+rung*.34f,1.25f),Steel);
        foreach(float zRail in new[]{1.02f,1.48f}) Box(combine,new(.06f,1.75f,.06f),new(1.68f,1.05f,zRail),Rust);
        // A folded unloading auger and weathered sheets on the grain tank.
        Box(combine,new(.12f,.12f,2.4f),new(1.3f,3.38f,.2f),Steel);
        Box(combine,new(.09f,.09f,1.5f),new(1.9f,3.95f,-.65f),Rust);
        Box(combine,new(.3f,.18f,.18f),new(1.9f,3.95f,-1.36f),Steel);
        for(int patch=0;patch<4;patch++) Box(combine,new(.035f,.75f,.55f),new(-.72f+patch*.43f,3.33f,.63f),patch%2==0?Rust:Steel);
        Box(combine,new(5.8f,.4f,1.5f),new(0,.5f,-3),Steel);
        for(int k=0;k<20;k++) Box(combine,new(.08f,.15f,.7f),new(-2.8f+k*.3f,.4f,-3.8f),Rust);
        _combineReel=new Node3D { Position=new Vector3(0,1.05f,-3.65f),Name="AbandonedHeaderReel" };combine.AddChild(_combineReel);
        _combineReel.AddChild(Props.Part(Props.Cyl(.55f,5.1f,16),Rust,Vector3.Zero,new Vector3(0,0,90)));
        _combineReel.AddChild(Props.Part(Props.Cyl(.68f,.12f,12),Steel,new Vector3(-2.55f,0,0),new Vector3(0,0,90)));
        _combineReel.AddChild(Props.Part(Props.Cyl(.68f,.12f,12),Steel,new Vector3(2.55f,0,0),new Vector3(0,0,90)));
        for(int spoke=0;spoke<8;spoke++)
        {
            float a=spoke*MathF.Tau/8;
            _combineReel.AddChild(Props.Part(Props.Box(5.2f,.07f,.07f),Steel,new Vector3(0,MathF.Cos(a)*.53f,MathF.Sin(a)*.53f),new Vector3(Mathf.RadToDeg(a),0,0)));
            for(int tine=0;tine<16;tine++)
            {
                float x=-2.4f+tine*.32f;
                _combineReel.AddChild(Props.Part(Props.Box(.055f,.3f,.06f),Rust,
                    new Vector3(x,MathF.Cos(a)*.68f,MathF.Sin(a)*.68f),new Vector3(Mathf.RadToDeg(a),0,0)));
            }
        }
        var wheels=new Node3D { Name="CombineWheels" }; combine.AddChild(wheels); _combineWheels=wheels;
        foreach(float x in new[]{-1.5f,1.5f}) foreach(float z in new[]{-1.1f,1.6f})
        {
            float radius=z<0?.75f:.45f, y=z<0?.6f:.35f;
            var pivot=new Node3D { Position=new Vector3(x,y,z) };wheels.AddChild(pivot);
            pivot.AddChild(Props.Part(Props.Cyl(radius,.4f,12),new Color(.09f,.09f,.07f),Vector3.Zero,new Vector3(0,0,90)));
            pivot.AddChild(Props.Part(Props.Cyl(radius*.48f,.43f,10),Rust,Vector3.Zero,new Vector3(0,0,90)));
            for(int spoke=0;spoke<8;spoke++)
            {
                float a=spoke*MathF.Tau/8;
                pivot.AddChild(Props.Part(Props.Box(.06f,radius*1.7f,.07f),Steel,
                    new Vector3(0,MathF.Cos(a)*radius*.48f,MathF.Sin(a)*radius*.48f),new Vector3(Mathf.RadToDeg(a),0,0)));
            }
            for(int lug=0;lug<12;lug++)
            {
                float a=lug*MathF.Tau/12;
                pivot.AddChild(Props.Part(Props.Box(.48f,.13f,.11f),Rust,
                    new Vector3(0,MathF.Cos(a)*radius*.93f,MathF.Sin(a)*radius*.93f),new Vector3(Mathf.RadToDeg(a),0,0)));
            }
        }
        Box(combine,new(.12f,1.2f,.12f),new(-1.45f,2.3f,.75f),Steel);
        Box(combine,new(1.9f,.12f,.18f),new(-1.35f,2.85f,.75f),Steel);
        for(int k=0;k<8;k++) Box(combine,new(.045f,.16f,.12f),new(-2.75f+k*.78f,2.25f,-3.85f),new Color(.48f,.36f,.18f));
        for(int side=-1;side<=1;side+=2)
        {
            Box(combine,new(.08f,.1f,3.5f),new(side*1.62f,2.15f,-.2f),Steel);
            Box(combine,new(.45f,.09f,.12f),new(side*1.85f,2.85f,1.4f),new Color(.66f,.07f,.035f));
        }
        // Rounded exhaust, air cleaner, roof overhang and split wheel arches break the box silhouette.
        combine.AddChild(Props.Part(Props.Cyl(.085f,1.25f,12),Steel,new Vector3(.9f,3.15f,-1.15f)));
        combine.AddChild(Props.Part(Props.Cyl(.14f,.12f,12),new Color(.08f,.07f,.06f),new Vector3(.9f,3.82f,-1.15f)));
        combine.AddChild(Props.Part(Props.Cyl(.23f,.48f,12),Rust,new Vector3(1.2f,2.75f,-.7f)));
        Box(combine,new(1.65f,.10f,1.7f),new(-.55f,3.79f,-1),Rust);
        foreach(float side in new[]{-1f,1f})
            combine.AddChild(Props.Part(Props.Box(.68f,.13f,1.8f),Rust,new Vector3(side*1.45f,1.45f,-1.1f),new Vector3(0,0,side*8)));
        // Exposed pulleys, belt runs and an inspection hatch on the right-hand threshing drive.
        Vector3[] pulleys={new(1.51f,1.4f,.25f),new(1.51f,2.05f,.9f),new(1.51f,1.35f,1.6f)};
        foreach(Vector3 p in pulleys)
        {
            combine.AddChild(Props.Part(Props.Cyl(.26f,.09f,16),new Color(.10f,.10f,.08f),p,new Vector3(0,0,90)));
            combine.AddChild(Props.Part(Props.Cyl(.15f,.11f,12),Steel,p+new Vector3(.02f,0,0),new Vector3(0,0,90)));
            combine.AddChild(Props.Part(Props.Cyl(.045f,.14f,8),Rust,p+new Vector3(.05f,0,0),new Vector3(0,0,90)));
        }
        for(int i=0;i<3;i++)
        {
            Vector3 a=pulleys[i]+new Vector3(.08f,.2f,0),b=pulleys[(i+1)%3]+new Vector3(.08f,.2f,0);
            var belt=Props.Part(Props.Box(.045f,.045f,a.DistanceTo(b)),new Color(.045f,.04f,.03f),(a+b)*.5f);
            belt.Basis=Basis.LookingAt((b-a).Normalized());combine.AddChild(belt);
        }
        Box(combine,new(1.8f,.75f,.06f),new(0,1.7f,2.13f),new Color(.08f,.09f,.07f));
        for(int i=0;i<11;i++) Box(combine,new(.055f,.68f,.09f),new(-.8f+i*.16f,1.7f,2.18f),Rust);
        for(int i=0;i<12;i++)
        {
            Box(combine,new(.025f,.025f,.035f),new(-.77f+i*.14f,2.1f,2.19f),Steel);
            combine.AddChild(Props.Part(Props.Box(.035f,.25f,.025f),Rust.Darkened(.3f),new Vector3(-1.56f,2.15f-i%3*.22f,-1.1f+i*.24f),new Vector3(0,0,-12)));
        }
        // A derelict overhead wire ends at disconnected insulators rather than powering the farm.
        var pole=Site(229,124);Box(pole,new(.2f,7,.2f),new(0,3.5f,0),Rust);
        Box(pole,new(2.5f,.14f,.18f),new(0,6.5f,0),Steel);
        for(int k=0;k<3;k++) Box(pole,new(.08f,2.5f,.08f),new(-.8f+k*.8f,5.1f,0),Steel);
        BatchWithDynamics(kitchen,_lid,_stoveGlow,_ladle,_cookSteam);
        BatchWithDynamics(combine,wheels,_combineReel);
        foreach(var child in root.GetChildren())
            if(child is Node3D site && site!=kitchen && site!=combine) DrunkTractor.BatchStatic(site,null);
        return root;
    }
}
