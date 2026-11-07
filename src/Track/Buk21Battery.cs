using System;
using System.Collections.Generic;
using CCCPRacing.Audio;
using CCCPRacing.Car;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Track;

/// <summary>BUK-21 exercise battery. Aircraft are its only targets; missiles self-destruct on a lost track.</summary>
public partial class Buk21Battery : Node3D
{
    sealed class Launcher
    {
        public Node3D Model, Turret, Rack, Radar;
        public int Loaded = 4;
        public float Soot;
        public float Reload, RadarAngle;      // (RadarAngle: the dish's unwrapped turn, for the world tape)
    }
    sealed class MissileFlight
    {
        public Node3D Model;
        public Vector3 Position, Velocity;
        public int Target;
        public float Age, Smoke;
    }
    readonly List<Launcher> _launchers = new();
    readonly List<MissileFlight> _missiles = new();
    readonly RandomNumberGenerator _rng = new() { Seed = 1984 };
    TrackData _track;
    TyreEffects _fx;
    HindSorties _aircraft;
    float _nextShot, _alert, _loadingTime;
    Node3D _loader, _crane, _loaderWheels, _hoist, _liftedLoad;
    readonly List<Node3D> _wheels=new();
    Vector3 _lastLoaderPosition;
    Launcher _loading;
    Vector3 _loaderHome, _loaderWork;
    int _loaderObstacle;
    float _wheelAngle;      // the loader's wheels' unwrapped turn (they all turn alike), for the world tape
    public bool Alert => _alert > 0;

    // The battery's engines (diesel, as the tracked launchers' and the generator's really are): the player's diesel smoke (TyreEffects.EngineSoot) at idle, the launchers' and the generator's
    // always (the dish turns all the time), the loader truck's while it drives. Pipes are in each part's own space; TapeEmitters makes the same smoke from the poses in a replay.
    internal static readonly Vector3 LauncherPipe = new(0.9f, 1.95f, 3.7f), GeneratorPipe = new(-0.6f, 1.95f, 1.0f), LoaderPipe = new(1.1f, 2.95f, -1.3f);
    internal const float LauncherSootPower = 0.6f, GeneratorSootPower = 0.5f, LoaderSootPower = 1.8f, LoaderMinSpeed = 0.3f, LoaderSpeedRef = 2f;
    internal const float IdleThrottle = 0f, GeneratorThrottle = 0.3f;
    float _genSoot, _loaderSoot, _loaderSpeed;
    Node3D _generator;
    /// <summary>The parts that smoke, for the replay's emitters.</summary>
    internal IEnumerable<Node3D> TapeLaunchers() { foreach (var l in _launchers) yield return l.Model; }
    internal Node3D TapeGenerator => _generator;
    internal Node3D TapeLoader => _loader;
    public event Action<Vector3> Launched;

    public void Init(TrackData track, TyreEffects fx, HindSorties aircraft, PropInstance site)
    {
        _track = track; _fx = fx; _aircraft = aircraft;
        int placement = 0;
        foreach (Vector2 offset in new[] { new Vector2(-6, 0), new Vector2(6, 10) })
        {
            Vector2 at = new Vector2(site.Position.X, site.Position.Z) + offset;
            var model = LauncherModel();
            float heading = Mathf.DegToRad(placement++ == 0 ? 32f : -43f);
            model.Rotation = new Vector3(0, heading, 0);
            model.Position = new Vector3(at.X, track.MeshHeight(at.X, at.Y), at.Y);
            AddChild(model);
            for (int circle = -1; circle <= 1; circle++)
            {
                Vector3 local = new Basis(Vector3.Up, heading) * new Vector3(0, 0, circle * 2.5f);
                track.Obstacles.Add(new Obstacle(at + new Vector2(local.X, local.Z), 2));
            }
            model.AddChild(Props.Part(Props.Cyl(.09f,.4f,6),new Color(.1f,.1f,.09f),LauncherPipe-new Vector3(0,.2f,0)));      // the engine's exhaust stub, rear deck
            var launcher = new Launcher { Model = model, Turret = model.GetNode<Node3D>("Turret"),
                Rack = model.GetNode<Node3D>("Turret/Rack"), Radar = model.GetNode<Node3D>("Turret/Radar") };
            _launchers.Add(launcher);
            // The world tape: where the launcher aims and which of its rounds are on the rack (the Hind actor's static parts, "l<n>/...").
            string tag = $"l{placement - 1}/";
            Tape.Part(launcher.Turret, tag + "turret"); Tape.Part(launcher.Rack, tag + "rack");
            for (int k = 0; k < 4; k++) Tape.Part(launcher.Rack.GetNode<Node3D>($"M{k}"), tag + "m" + k);
            Tape.Part(launcher.Radar, tag + "radar"); Tape.Spin(launcher.Radar, Vector3.Up, () => launcher.RadarAngle);      // (the dish turns all the time)
        }
        // A generator and ammunition crates make the old site read as a field deployment.
        var support = new Node3D { Position = site.Position + new Vector3(7,0,-6) };
        support.Position = new Vector3(support.Position.X, track.MeshHeight(support.Position.X,support.Position.Z),support.Position.Z);
        support.AddChild(Props.Part(Props.Box(2.1f,1.4f,2.8f),new Color(.3f,.33f,.23f),new Vector3(0,.8f,0)));
        for(int i=0;i<4;i++) support.AddChild(Props.Part(Props.Box(1.1f,.6f,1.7f),new Color(.36f,.31f,.2f),new Vector3(-3,.3f+i%2*.6f,i/2*1.9f)));
        Color net = new(.26f,.3f,.19f);
        foreach(float x in new[]{-4.6f,-1.4f})
            foreach(float z in new[]{-1f,3f})
                support.AddChild(Props.Part(Props.Box(.065f,2.3f,.065f),net,new Vector3(x,1.15f,z)));
        for(int x=0;x<9;x++)
            for(int z=0;z<10;z++)
            {
                float y=2.15f-MathF.Sin(x/8f*MathF.PI)*MathF.Sin(z/9f*MathF.PI)*.3f;
                support.AddChild(Props.Part(Props.Box(.42f,.025f,.035f),net,new Vector3(-4.6f+x*.4f,y,-1+z*.4f)));
                support.AddChild(Props.Part(Props.Box(.035f,.025f,.42f),net,new Vector3(-4.6f+x*.4f,y,-1+z*.4f)));
                if(Noise.Hash01(x,z,1984)>.55f)
                    support.AddChild(Props.Part(Props.Box(.26f,.025f,.28f),net*(.8f+Noise.Hash01(z,x,12)*.4f),new Vector3(-4.6f+x*.4f,y,-1+z*.4f),new Vector3(0,x*17,0)));
            }
        DrunkTractor.BatchStatic(support,null); AddChild(support);
        _generator = support;
        support.AddChild(Props.Part(Props.Cyl(.07f,.5f,6),new Color(.1f,.1f,.09f),GeneratorPipe-new Vector3(0,.25f,0)));      // the generator's exhaust stub
        support.AddChild(Props.Part(Props.Box(.08f,3,.08f),new Color(.28f,.29f,.22f),new Vector3(1,1.5f,0)));
        var work=new MilitaryLights {Name="BatteryWorkLight"};support.AddChild(work);
        work.Lamp(new(1,3,0),new(1,.8f,.5f),.12f);work.Beam(new(1,3,0),-85,10,2,48);
        track.Obstacles.Add(new Obstacle(new Vector2(support.Position.X,support.Position.Z),1.7f));
        // One shared crane truck services the two launchers, then returns to its waiting bay.
        _loader=LoaderModel(); AddChild(_loader);
        _loaderHome=new Vector3(300,track.MeshHeight(300,175),175);
        _loader.Position=_loaderHome;
        _crane=_loader.GetNode<Node3D>("Crane");
        // The world tape: the loader truck (where it stands, its crane, hoist, the round it lifts, its wheels' turn).
        Tape.Part(_loader,"loader"); Tape.Part(_crane,"crane"); Tape.Part(_hoist,"hoist"); Tape.Part(_liftedLoad,"lifted");
        for(int w=0;w<_wheels.Count;w++) { Tape.Part(_wheels[w],"wheel"+w); Tape.Spin(_wheels[w],Vector3.Right,()=>_wheelAngle); }
        _loaderObstacle=track.Obstacles.Count;
        track.Obstacles.Add(new Obstacle(new Vector2(300,175),2));
        Reset();
    }
    public void Reset()
    {
        foreach(var m in _missiles) { _aircraft?.TapeHost?.Gone(m.Model); m.Model.QueueFree(); }
        _missiles.Clear(); _nextShot = 60; _alert=0;
        _loading=null; _loadingTime=0;
        if(_loader!=null) { _loader.Position=_loaderHome; _loader.Rotation=Vector3.Zero; _crane.Rotation=Vector3.Zero; _lastLoaderPosition=_loaderHome; if(_liftedLoad!=null)_liftedLoad.Visible=false;
            foreach(var wheel in _wheels) wheel.Rotation=Vector3.Zero;
            _track.Obstacles[_loaderObstacle]=new Obstacle(new Vector2(_loaderHome.X,_loaderHome.Z),2); }
        foreach(var child in GetChildren()) if(child is AudioStreamPlayer3D sound) { sound.Stop(); sound.QueueFree(); }
        foreach(var l in _launchers)
        {
            l.Loaded = 4; l.Reload = 0; l.Turret.Rotation = Vector3.Zero;
            l.Rack.Rotation = new Vector3(.5f,0,0);
            for(int k=0;k<4;k++) l.Rack.GetNode<Node3D>($"M{k}").Visible=true;
        }
    }
    public void Tick(float dt)
    {
        _nextShot -= dt; _alert=Math.Max(0,_alert-dt);
        LoadingTick(dt);
        if (_fx != null && dt > 0)
        {
            foreach (var l in _launchers) _fx.EngineSoot(l.Model.GlobalTransform * LauncherPipe, l.Model.GlobalTransform.Basis.Z, Vector3.Zero, IdleThrottle, LauncherSootPower, ref l.Soot, dt);
            if (_generator != null) _fx.EngineSoot(_generator.GlobalTransform * GeneratorPipe, _generator.GlobalTransform.Basis.Z, Vector3.Zero, GeneratorThrottle, GeneratorSootPower, ref _genSoot, dt);
            if (_loader != null && _loaderSpeed >= LoaderMinSpeed)
            {
                Vector3 fwd = -_loader.GlobalTransform.Basis.Z.Normalized();
                _fx.EngineSoot(_loader.GlobalTransform * LoaderPipe, -fwd, fwd * _loaderSpeed, Mathf.Clamp(_loaderSpeed / LoaderSpeedRef, 0.25f, 1f), LoaderSootPower, ref _loaderSoot, dt);
            }
        }
        foreach(var l in _launchers)
        {
            l.Radar.RotateY(dt*.8f); l.RadarAngle+=dt*.8f;
            if(l.Loaded==0)
            {
                l.Reload-=dt;
                if(l.Reload<=0 && _loading==null)
                {
                    _loading=l; _loadingTime=0;
                    _loaderWork=l.Model.Position+new Vector3(6,0,0);
                    _loaderWork.Y=_track.MeshHeight(_loaderWork.X,_loaderWork.Z);
                }
            }
            int target=-1; Vector3 targetPosition=default; float best=270;
            foreach(var aircraft in _aircraft.AirTargets)
            {
                float distance=l.Model.GlobalPosition.DistanceTo(aircraft.Position);
                if(distance<45 || distance>=best || aircraft.Position.X<0 || aircraft.Position.Z<0 || aircraft.Position.X>_track.WorldSize || aircraft.Position.Z>_track.WorldSize) continue;
                target=aircraft.Id; targetPosition=aircraft.Position+aircraft.Velocity*.6f; best=distance;
            }
            if(target<0) continue;
            _alert=2;
            if (_loading==l) continue;
            Vector3 to=targetPosition-l.Model.GlobalPosition;
            float yaw=TrackData.YawFor(new Vector2(to.X,to.Z)) - l.Model.Rotation.Y;
            l.Turret.Rotation=new Vector3(0,Mathf.LerpAngle(l.Turret.Rotation.Y,yaw,1-MathF.Exp(-dt*1.2f)),0);
            float elevation=Math.Clamp(MathF.Atan2(Math.Max(4,to.Y),new Vector2(to.X,to.Z).Length()),.28f,.75f);
            l.Rack.Rotation=new Vector3(Mathf.MoveToward(l.Rack.Rotation.X,elevation,dt*.35f),0,0);
            if((_aircraft.Exercise!=null && !_aircraft.Exercise.AirDefenceAllowed) || _nextShot>0 || l.Loaded<=0 || _missiles.Count>=2 || MathF.Abs(Mathf.AngleDifference(l.Turret.Rotation.Y,yaw))>.1f) continue;
            Launch(l,target);
            _nextShot=_rng.RandfRange(28,42);
        }
        for(int i=_missiles.Count-1;i>=0;i--)
        {
            var missile=_missiles[i]; missile.Age+=dt;
            bool tracked=false; Vector3 target=default, velocity=default;
            foreach(var p in _aircraft.AirTargets)
                if(p.Id==missile.Target) { tracked=true;target=p.Position;velocity=p.Velocity;break; }
            float speed=Math.Min(95,25+missile.Age*28);
            if(tracked && missile.Age>1)
            {
                float lead=Math.Clamp(missile.Position.DistanceTo(target)/speed*.8f,0,1.8f);
                Vector3 direction=(target+velocity*lead-missile.Position).Normalized();
                Vector3 ahead=missile.Position+missile.Velocity.Normalized()*18;
                if(missile.Position.DistanceTo(target)>35 && missile.Position.Y<_track.MeshHeight(ahead.X,ahead.Z)+7)
                    direction=new Vector3(direction.X,Math.Max(.3f,direction.Y),direction.Z).Normalized();
                missile.Velocity=missile.Velocity.Normalized().Slerp(direction,Math.Min(1,dt*3.5f))*speed;
            }
            else missile.Velocity=missile.Velocity.Normalized()*speed;
            Vector3 old=missile.Position;
            missile.Position+=missile.Velocity*dt;
            missile.Model.Position=missile.Position;
            missile.Model.Basis=Basis.LookingAt(missile.Velocity.Normalized(),MathF.Abs(missile.Velocity.Normalized().Y)>.98f?Vector3.Right:Vector3.Up);
            missile.Smoke-=dt;
            if(missile.Smoke<=0)
            { missile.Smoke+=.04f; TrailFx(_fx,missile.Position); _aircraft.TapeFx?.Fx(new TapeEvent(0f,TapeEventType.Effect,Kind:TapeFxKind.BukTrail,Effect:TapeEffect.Smoke,Pos:missile.Position,Life:2.3f)); }
            Vector3 step=missile.Position-old;
            float u=tracked?Math.Clamp((target-old).Dot(step)/Math.Max(.001f,step.LengthSquared()),0,1):0;
            bool hit=tracked && (old+step*u).DistanceTo(target)<6;
            bool expired=missile.Age>11 || (!tracked && missile.Age>1) || missile.Position.Y<_track.MeshHeight(missile.Position.X,missile.Position.Z)+1;
            if(!hit && !expired) continue;
            if(hit) _aircraft.ShootDown(missile.Target);
            if(OS.HasEnvironment("SU25DBG")) GD.Print($"BUK21 missile finished: hit={hit}, tracked={tracked}, age={missile.Age:F1}, distance={(tracked?missile.Position.DistanceTo(target):-1):F1}");
            Burst(missile.Position,hit);
            _aircraft.TapeHost?.Gone(missile.Model); missile.Model.QueueFree(); _missiles.RemoveAt(i);
        }
    }
    void Launch(Launcher launcher,int target)
    {
        int tube=4-launcher.Loaded;
        var round=launcher.Rack.GetNode<Node3D>($"M{tube}");
        Vector3 at=round.GlobalTransform*new Vector3(0,0,-1);
        Vector3 forward=(-round.GlobalTransform.Basis.Z.Normalized()+Vector3.Up*.35f).Normalized();
        round.Visible=false;
        if(--launcher.Loaded==0) launcher.Reload=80;
        var model=MissileModel(true); TapeMark(model); AddChild(model); model.Position=at;
        _missiles.Add(new MissileFlight { Model=model,Position=at,Velocity=forward*25,Target=target });
        _aircraft.TapeHost?.Born(model);
        LaunchFx(_fx,_track,this,at,forward);
        _aircraft.TapeFx?.Fx(new TapeEvent(0f,TapeEventType.Effect,Kind:TapeFxKind.BukLaunch,Effect:TapeEffect.Launch,Pos:at,Dir:forward,Life:2.6f));
        Launched?.Invoke(at);
        if(OS.HasEnvironment("SU25DBG")) GD.Print($"BUK21 launch at {at}, target {target}");
    }
    void Burst(Vector3 at,bool hit)
    {
        BurstFx(_fx,_rng,this,at,hit);
        _aircraft.TapeFx?.Fx(new TapeEvent(0f,TapeEventType.Effect,Kind:TapeFxKind.BukBurst,Effect:TapeEffect.Explosion,Pos:at,Param:hit?1f:0f,Life:2f));
    }
    /// <summary>A missile leaving its rack: the boom, the plume behind it and a ring of dust at the ground (live play and the tape's replay).</summary>
    internal static void LaunchFx(TyreEffects fx,TrackData track,Node parent,Vector3 at,Vector3 forward)
    {
        parent.AddChild(new ExplosionSound { Position=at,Loudness=-3,Reach=1000 });
        fx.Smoke(at,-forward*3+Vector3.Up,new Color(.7f,.68f,.6f),1,2,6,2.5f);
        for(int k=0;k<10;k++)
        {
            float angle=k*MathF.Tau/10;
            fx.Smoke(new Vector3(at.X,track.MeshHeight(at.X,at.Z)+.2f,at.Z),
                new Vector3(MathF.Cos(angle)*5,1,MathF.Sin(angle)*5),new Color(.46f,.4f,.27f),.7f,1,4,2.6f);
        }
    }
    /// <summary>One puff of a missile's smoke trail.</summary>
    internal static void TrailFx(TyreEffects fx,Vector3 at) => fx.Smoke(at,Vector3.Up*.6f,new Color(.75f,.74f,.67f),.85f,.7f,2.4f,2.3f);
    /// <summary>A missile going off (a hit or lost track): the boom, fire and a cloud.</summary>
    internal static void BurstFx(TyreEffects fx,RandomNumberGenerator rng,Node parent,Vector3 at,bool hit)
    {
        parent.AddChild(new ExplosionSound { Position=at,Loudness=hit?2:-3,Reach=650 });
        for(int k=0;k<18;k++) fx.Burst(at,new Vector3(rng.RandfRange(-8,8),rng.RandfRange(-4,10),rng.RandfRange(-8,8)),new Color(1,.6f,.15f),1,1,.2f,.7f,5,0,false);
        fx.Smoke(at,Vector3.Up*2,new Color(.24f,.24f,.22f),.85f,2,6,2);
    }
    public void SetAudioPaused(bool paused)
    {
        foreach (var child in GetChildren())
            if (child is AudioStreamPlayer3D sound) sound.StreamPaused = paused;
    }
    void LoadingTick(float dt)
    {
        _loaderSpeed=0;
        if(_loading==null) return;
        _loadingTime+=dt;
        Vector3 from=_loadingTime<22?_loaderHome:_loaderWork;
        Vector3 to=_loadingTime<22?_loaderWork:_loaderHome;
        float travel=_loadingTime<22?Math.Min(1,_loadingTime/10):Math.Min(1,(_loadingTime-22)/10);
        _loader.Position=from.Lerp(to,Mathf.SmoothStep(0,1,travel));
        _loader.Position=new Vector3(_loader.Position.X,_track.MeshHeight(_loader.Position.X,_loader.Position.Z),_loader.Position.Z);
        float travelDistance=new Vector2(_loader.Position.X-_lastLoaderPosition.X,_loader.Position.Z-_lastLoaderPosition.Z).Length();
        foreach(var wheel in _wheels) wheel.RotateX(travelDistance/.55f);
        _wheelAngle+=travelDistance/.55f;
        _lastLoaderPosition=_loader.Position;
        if(dt>0) _loaderSpeed=travelDistance/dt;
        Vector3 direction=to-from;
        _loader.Rotation=new Vector3(0,TrackData.YawFor(new Vector2(direction.X,direction.Z)),0);
        _track.Obstacles[_loaderObstacle]=new Obstacle(new Vector2(_loader.Position.X,_loader.Position.Z),2);
        float lift=_loadingTime>10 && _loadingTime<22?MathF.Sin((_loadingTime-10)/12*MathF.PI):0;
        _crane.Rotation=new Vector3(0,MathF.Sin(_loadingTime*.22f)*.12f,-lift*.65f);
        _hoist.Position=new Vector3(3.4f,1.55f-lift*.72f,0);
        _liftedLoad.Visible=_loadingTime>10 && _loadingTime<22;
        // Four sequential placements make the replenishment visible on the actual rack.
        if(_loadingTime>=10 && _loadingTime<=22)
            for(int k=0;k<4;k++) _loading.Rack.GetNode<Node3D>($"M{k}").Visible=_loadingTime>=12+k*2.5f;
        if(_loadingTime>=32)
        { _loading.Loaded=4; _loading.Reload=0; _loading=null; _crane.Rotation=Vector3.Zero; _liftedLoad.Visible=false; }
    }
    Node3D LoaderModel()
    {
        var root=new Node3D { Name="BatteryLoader" }; var body=new Node3D();root.AddChild(body);
        Color olive=new(.3f,.34f,.21f),steel=new(.17f,.18f,.15f),rubber=new(.07f,.07f,.06f);
        body.AddChild(Props.Part(Props.Box(2.5f,.35f,6.5f),steel,new Vector3(0,.8f,0)));
        body.AddChild(Props.Part(Props.Box(2.45f,1.8f,1.7f),olive,new Vector3(0,1.95f,-2.4f)));
        body.AddChild(Props.Part(Props.Box(2.1f,.7f,.04f),new Color(.12f,.22f,.24f),new Vector3(0,2.3f,-3.27f)));
        foreach(float x in new[]{-.88f,.88f})
        {
            body.AddChild(Props.Part(Props.Box(.06f,.75f,.04f),steel,new Vector3(x,2.3f,-3.31f)));
            body.AddChild(Props.Part(Props.Box(.32f,.08f,.055f),new Color(.7f,.62f,.35f),new Vector3(x,2.65f,-3.33f)));
            body.AddChild(NightGlow.Part(Props.Ball(.11f,6),new Color(.75f,.68f,.38f),new Color(1,.68f,.24f),1.2f,new Vector3(x,1.3f,-3.31f)));
            body.AddChild(Props.Part(Props.Box(.42f,.1f,.18f),new Color(.16f,.17f,.14f),new Vector3(x,1.25f,3.27f)));
        }
        for(int x=-2;x<=2;x++) body.AddChild(Props.Part(Props.Box(.08f,.55f,.08f),steel,new Vector3(x*.38f,1.35f,-3.31f)));
        body.AddChild(Props.Part(Props.Box(2.8f,.18f,.25f),steel,new Vector3(0,.48f,-3.42f)));
        body.AddChild(Props.Part(Props.Box(2.8f,.2f,.3f),steel,new Vector3(0,.48f,3.42f)));
        for(int x=-1;x<=1;x++) body.AddChild(Props.Part(Props.Box(.12f,.06f,2.3f),new Color(.17f,.18f,.15f),new Vector3(x*.75f,.99f,.55f)));
        body.AddChild(Props.Part(Props.Box(.4f,.16f,.38f),new Color(.32f,.12f,.08f),new Vector3(1.05f,1.1f,2.1f)));
        _loaderWheels=new Node3D { Name="RollingWheels" };root.AddChild(_loaderWheels);
        foreach(float x in new[]{-1.3f,1.3f}) foreach(float z in new[]{-2.1f,1.1f,2.5f})
        {
            var wheel=new Node3D { Position=new Vector3(x,.55f,z) };_loaderWheels.AddChild(wheel);_wheels.Add(wheel);
            wheel.AddChild(Props.Part(Props.Cyl(.55f,.35f,12),rubber,Vector3.Zero,new Vector3(0,0,90)));
            wheel.AddChild(Props.Part(Props.Cyl(.27f,.39f,10),new Color(.35f,.36f,.3f),Vector3.Zero,new Vector3(0,0,90)));
            for(int lug=0;lug<10;lug++)
            {
                float a=lug*MathF.Tau/10;
                wheel.AddChild(Props.Part(Props.Box(.39f,.1f,.07f),rubber,new Vector3(0,MathF.Cos(a)*.53f,MathF.Sin(a)*.53f),new Vector3(Mathf.RadToDeg(a),0,0)));
            }
        }
        foreach(float x in new[]{-.9f,-.3f,.3f,.9f})
        { var missile=MissileModel(false); missile.Position=new Vector3(x,1.5f,.6f);body.AddChild(missile); }
        var crane=new Node3D { Name="Crane",Position=new Vector3(0,1.3f,2.5f) };root.AddChild(crane);
        crane.AddChild(Props.Part(Props.Box(.35f,2.3f,.35f),olive,new Vector3(0,1.15f,0)));
        crane.AddChild(Props.Part(Props.Box(3.8f,.3f,.3f),olive,new Vector3(1.6f,2.3f,0)));
        crane.AddChild(Props.Part(Props.Cyl(.025f,1.4f,6),steel,new Vector3(3.4f,1.55f,0)));
        crane.AddChild(Props.Part(Props.Box(.14f,.13f,.13f),steel,new Vector3(3.4f,.8f,0)));
        _hoist=new Node3D { Name="Hoist",Position=new Vector3(3.4f,1.55f,0) };crane.AddChild(_hoist);
        _hoist.AddChild(Props.Part(Props.Cyl(.025f,1.2f,6),steel,new Vector3(0,-.6f,0)));
        _hoist.AddChild(Props.Part(Props.Box(.6f,.08f,.5f),steel,new Vector3(0,-1.2f,0)));
        _liftedLoad=MissileModel(false);_liftedLoad.Scale=Vector3.One*.28f;_liftedLoad.Position=new Vector3(0,-2.2f,0);
        _liftedLoad.RotationDegrees=new Vector3(90,0,0);_hoist.AddChild(_liftedLoad);_liftedLoad.Visible=false;
        foreach(float side in new[]{-1f,1f})
        {
            crane.AddChild(Props.Part(Props.Box(.12f,1.1f,.12f),steel,new Vector3(side*1.5f,.45f,1.6f)));
            crane.AddChild(Props.Part(Props.Box(.28f,.08f,.2f),steel,new Vector3(side*1.5f,.06f,1.6f)));
        }
        DrunkTractor.BatchStatic(body,null);DrunkTractor.BatchStatic(crane,_hoist);
        MilitaryLights.Vehicle(root,new(0,1.3f,-3.42f),new(0,1.25f,3.46f),.88f,22);return root;
    }
    /// <summary>The world tape: a missile in flight is a dynamic instance (its pose is the only thing it has).</summary>
    static void TapeMark(Node3D model) { model.SetMeta(WorldTapeConst.KindMeta,"buk-missile"); Tape.Part(model,"root"); }
    /// <summary>The missile of a tape: the flying model, marked alike, nothing simulated.</summary>
    public static Node3D BuildReplayMissile() { var m=MissileModel(true); TapeMark(m); return m; }
    static Node3D MissileModel(bool burning)
    {
        var root=new Node3D(); Color olive=new(.38f,.4f,.29f),dark=new(.14f,.16f,.13f);
        root.AddChild(Props.Part(Props.Cyl(.2f,4.8f,8),olive,Vector3.Zero,new Vector3(90,0,0)));
        root.AddChild(Props.Part(Props.Cyl(.2f,.7f,8,0),new Color(.7f,.7f,.62f),new Vector3(0,0,-2.75f),new Vector3(-90,0,0)));
        foreach(float z in new[]{-1.6f,1.8f})
        {
            root.AddChild(Props.Part(Props.Box(1,.055f,.6f),olive,new Vector3(0,0,z),new Vector3(0,15,0)));
            root.AddChild(Props.Part(Props.Box(.055f,1,.6f),olive,new Vector3(0,0,z),new Vector3(15,0,0)));
        }
        root.AddChild(Props.Part(Props.Cyl(.15f,.12f,8),dark,new Vector3(0,0,2.45f),new Vector3(90,0,0)));
        DrunkTractor.BatchStatic(root,null);
        if(burning) root.AddChild(new MeshInstance3D { Mesh=Props.Ball(.35f,6),Position=new Vector3(0,0,2.8f),Scale=new Vector3(.5f,.5f,2.8f),
            MaterialOverride=new StandardMaterial3D { AlbedoColor=new Color(1,.65f,.15f),ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded } });
        return root;
    }
    static Node3D LauncherModel()
    {
        var root=new Node3D { Name="BUK21" }; var body=new Node3D(); root.AddChild(body);
        Color olive=new(.29f,.34f,.22f),dark=new(.11f,.12f,.1f),metal=new(.4f,.42f,.34f);
        body.AddChild(Props.Part(Props.Box(3.2f,1.2f,7.2f),olive,new Vector3(0,1.1f,0)));
        foreach(float side in new[]{-1f,1f})
        {
            body.AddChild(Props.Part(Props.Box(.65f,.85f,7.1f),dark,new Vector3(side*1.45f,.5f,0)));
            for(int k=0;k<6;k++)
            {
                body.AddChild(Props.Part(Props.Cyl(.39f,.2f,10),metal,new Vector3(side*1.8f,.47f,-2.6f+k*1.04f),new Vector3(0,0,90)));
                body.AddChild(Props.Part(Props.Box(.12f,.4f,1),olive,new Vector3(side*1.79f,1.15f,-2.6f+k*1.04f)));
            }
            body.AddChild(Props.Part(Props.Box(.7f,.5f,1.5f),olive,new Vector3(side*.9f,1.9f,2.3f)));
            body.AddChild(Props.Part(Props.Box(.55f,.2f,.1f),new Color(.13f,.19f,.2f),new Vector3(side*.75f,1.75f,-3.64f)));
        }
        for(int i=0;i<7;i++) body.AddChild(Props.Part(Props.Box(1.6f,.04f,.08f),dark,new Vector3(0,1.73f,2+i*.17f)));
        Color rubber=new(.07f,.075f,.065f), rust=new(.29f,.2f,.12f), glass=new(.12f,.24f,.27f);
        foreach(float side in new[]{-1f,1f})
        {
            // Individual track shoes, road-wheel hubs and angled suspension arms.
            for(int k=0;k<25;k++)
                foreach(float y in new[]{.12f,.93f})
                    body.AddChild(Props.Part(Props.Box(.73f,.09f,.19f),k%5==0?rust:metal,new Vector3(side*1.48f,y,-3.25f+k*.27f)));
            for(int k=0;k<6;k++)
            {
                float z=-2.6f+k*1.04f;
                body.AddChild(Props.Part(Props.Cyl(.26f,.23f,10),rubber,new Vector3(side*1.82f,.47f,z),new Vector3(0,0,90)));
                body.AddChild(Props.Part(Props.Cyl(.13f,.26f,8),olive,new Vector3(side*1.85f,.47f,z),new Vector3(0,0,90)));
                body.AddChild(Props.Part(Props.Box(.08f,.12f,.65f),metal,new Vector3(side*1.76f,.68f,z+.15f),new Vector3(30,0,0)));
            }
            body.AddChild(Props.Part(Props.Box(.13f,.13f,6.6f),metal,new Vector3(side*1.64f,1.45f,0)));
            body.AddChild(Props.Part(Props.Box(.55f,.07f,.65f),olive,new Vector3(side*.85f,1.74f,-2.7f)));
            body.AddChild(Props.Part(Props.Box(.35f,.18f,.08f),glass,new Vector3(side*.85f,1.86f,-2.85f)));
            body.AddChild(Props.Part(Props.Cyl(.16f,.12f,8),new Color(.82f,.76f,.48f),new Vector3(side*1.25f,1.35f,-3.65f),new Vector3(90,0,0)));
            body.AddChild(Props.Part(Props.Box(.25f,.14f,.1f),new Color(.4f,.06f,.04f),new Vector3(side*1.3f,1.25f,3.65f)));
            for(int k=0;k<4;k++)
                body.AddChild(Props.Part(Props.Box(.055f,.035f,.3f),metal,new Vector3(side*.85f,1.8f,-2.8f+k*.18f)));
            // Fender boxes, tools, lifting eyes and hull seams.
            body.AddChild(Props.Part(Props.Box(.55f,.42f,1.2f),olive,new Vector3(side*1.12f,1.84f,.8f)));
            body.AddChild(Props.Part(Props.Box(.035f,.045f,2.5f),metal,new Vector3(side*1.55f,1.72f,-.5f)));
            for(int k=0;k<12;k++)
                body.AddChild(Props.Part(Props.Box(.065f,.065f,.065f),metal,new Vector3(side*1.61f,1.42f,-3+k*.55f)));
            body.AddChild(Props.Part(Props.Cyl(.045f,2.8f,6),dark,new Vector3(side*1.1f,3.2f,2.8f)));
        }
        body.AddChild(Props.Part(Props.Cyl(.5f,.09f,12),metal,new Vector3(-.65f,1.78f,-1.7f)));
        body.AddChild(Props.Part(Props.Box(.32f,.06f,.07f),dark,new Vector3(-.65f,1.85f,-1.7f)));
        body.AddChild(Props.Part(Props.Cyl(.13f,.8f,8),dark,new Vector3(1.15f,2.05f,2.85f)));
        DrunkTractor.BatchStatic(body,null);
        var turret=new Node3D { Name="Turret",Position=new Vector3(0,1.9f,0) };root.AddChild(turret);
        turret.AddChild(Props.Part(Props.Cyl(1.1f,.35f,12),olive,Vector3.Zero));
        var rack=new Node3D { Name="Rack",Position=new Vector3(0,1.15f,.2f),Rotation=new Vector3(.5f,0,0) };turret.AddChild(rack);
        rack.AddChild(Props.Part(Props.Box(2.9f,.25f,4.7f),metal,new Vector3(0,-.35f,0)));
        foreach(float side in new[]{-1f,1f})
        {
            rack.AddChild(Props.Part(Props.Box(.12f,.55f,4.5f),olive,new Vector3(side*1.48f,-.45f,0)));
            turret.AddChild(Props.Part(Props.Cyl(.12f,1.25f,8),dark,new Vector3(side*.95f,.55f,.65f),new Vector3(25,0,0)));
            turret.AddChild(Props.Part(Props.Cyl(.065f,.8f,8),metal,new Vector3(side*.95f,1.25f,.35f),new Vector3(25,0,0)));
        }
        for(int k=0;k<4;k++)
        {
            float x=-1.05f+k*.7f;
            rack.AddChild(Props.Part(Props.Box(.12f,.18f,5.1f),dark,new Vector3(x,-.18f,0)));
            foreach(float z in new[]{-1.45f,1.4f})
                rack.AddChild(Props.Part(Props.Box(.45f,.12f,.18f),metal,new Vector3(x,-.05f,z)));
        }
        for(int k=0;k<4;k++)
        { var missile=MissileModel(false); missile.Name=$"M{k}";missile.Position=new Vector3(-1.05f+k*.7f,0,0);rack.AddChild(missile); }
        var radar=new Node3D { Name="Radar",Position=new Vector3(0,.8f,-2.5f) };turret.AddChild(radar);
        radar.AddChild(Props.Part(Props.Cyl(.1f,1.2f,6),metal,new Vector3(0,.3f,0)));
        var dish=Props.Part(Props.Ball(.8f,10),olive,new Vector3(0,1,0),new Vector3(-15,0,0)); dish.Scale=new Vector3(1,.7f,.2f);radar.AddChild(dish);
        radar.AddChild(Props.Part(Props.Box(.65f,.08f,.08f),metal,new Vector3(0,1,-.3f)));
        for(int k=0;k<7;k++)
        {
            float x=-.65f+k*.215f;
            radar.AddChild(Props.Part(Props.Box(.025f,.85f,.025f),metal,new Vector3(x,1,-.18f)));
            radar.AddChild(Props.Part(Props.Box(1.3f,.025f,.025f),metal,new Vector3(0,.65f+k*.12f,-.19f)));
        }
        radar.AddChild(Props.Part(Props.Cyl(.035f,.55f,6),dark,new Vector3(0,1,-.5f),new Vector3(90,0,0)));
        // Batch each rigid assembly independently so the radar and elevation still animate.
        DrunkTractor.BatchStatic(radar,null);
        MilitaryLights.Vehicle(root,new(0,1.35f,-3.76f),new(0,1.25f,3.76f),1.25f,24);
        return root;
    }
}
