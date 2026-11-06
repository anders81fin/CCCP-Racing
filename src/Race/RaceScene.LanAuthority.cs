using System;
using System.Collections.Generic;
using System.Linq;
using CCCPRacing.Car;
using CCCPRacing.Audio;
using CCCPRacing.Net;
using CCCPRacing.Track;
using CCCPRacing.UI;
using Godot;
namespace CCCPRacing.Race;

public partial class RaceScene
{
    sealed class LanDriver
    {
        public CarSim Car;
        public FuelTank Tank;
        public float DryWait;
        public LapTimer Timer;
        public readonly EngineHealth Health=new();
        public readonly SortedDictionary<uint,DriverCommand> Pending=new();
        public DriverCommand Last;
        public uint Ack;
        public double LastInput;
        public float Total, Best=-1, LastLap=-1, Impact, Wreck, RescueAt=-10;
        public ImpactMaterial Material;
        public bool ImpactBroke;
        public float RepairFrom=-1, DentsKept=1, DriveOff;
        public int RepairSerial;
        public int FuelPickupSerial;
        public byte BrokenHeadlights;
        public bool Dented, WasHeld;
        public bool Finished;
    }
    readonly Dictionary<long,LanDriver> _lanDrivers=new();
    readonly List<DriverCommand> _lanInputs=new();
    uint _lanSequence;
    Snapshot _lanCorrection;
    int _lanContactCount, _lanInputCount, _lanPredictedContacts;
    float _lanCorrectionError;
    LanWorld _lanWorld;
    float _lanWorldClock, _lanWreckLeft;
    float _lanSharedLightProgress;
    bool _lanWasDead, _lanWasDestroyed;
    LanDriver _lanMechanicTarget;
    int _lanRepairSerial;
    int _lanFuelPickupSerial;
    bool _lanMechanicRepairing;
    bool LanMechanicIsLocal => _lanMechanicTarget?.Car==_car;
    readonly List<(byte[] bytes,bool mesh)> _lanWorldPending=new();
    readonly List<(ServiceMarker marker, Node parent)> _lanServiceMarkers=new();
    readonly List<CarSim> _lanCars=new();
    ServiceMarker _lanFuelMarker;
    bool _lanFuelMarkerVisible;
    readonly List<Node3D> _lanExtraRoots=new();
    readonly List<int> _lanExtraSlots=new();
    readonly List<(Node node, ProcessModeEnum process, bool visible)> _lanExtraRestore=new();

    void LanExtraWorld()
    {
        _lanExtraRoots.Clear();_lanExtraSlots.Clear();
        foreach(Node child in FindChildren("*","",true,false))
        {
            if(child is KolkhozWorkers or VillageLife or PartyRowboat or PartyPondDuck or ZoneDosimetrist || child==_guards)
                _lanExtraRoots.Add((Node3D)child);
            else if(child is not DachaPatrol) continue;
            if(!_net.IsHost)
            {
                _lanExtraRestore.Add((child,child.ProcessMode,child is Node3D n && n.Visible));
                child.ProcessMode=ProcessModeEnum.Disabled;
                if(child is Node3D root) root.Hide();
            }
        }
        foreach(var entry in _workers?.Knocks() ?? Array.Empty<(int,Func<Vector3,bool>)>()) _lanExtraSlots.Add(entry.Item1);
        foreach(var entry in _guards?.Knocks() ?? Array.Empty<(int,Func<Vector3,bool>)>()) _lanExtraSlots.Add(entry.Item1);
        foreach(var entry in _dosimetrist?.Knocks() ?? Array.Empty<(int,Func<Vector3,bool>)>()) _lanExtraSlots.Add(entry.Item1);
    }

    void LanRestoreExtraWorld()
    {
        foreach(var (node,process,visible) in _lanExtraRestore)
            if(GodotObject.IsInstanceValid(node)) { node.ProcessMode=process; if(node is Node3D root) root.Visible=visible; }
        _lanExtraRestore.Clear();_lanExtraRoots.Clear();_lanExtraSlots.Clear();
    }

    void LanServiceMarkers(bool fuel)
    {
        _lanFuelMarker=null;
        foreach(Node child in _depot.FindChildren("*","",true,false))
            if(child is ServiceMarker marker) { _lanFuelMarker=marker; break; }
        if(_lanFuelMarker!=null) { _lanFuelMarkerVisible=_lanFuelMarker.Visible; _lanFuelMarker.Visible=fuel && _fuelOnTrack; }
        if(_net.IsHost) return;
        foreach(Node3D actor in new Node3D[] { _drunksAll, _depot })
        {
            if(actor==null) continue;
            foreach(Node child in actor.FindChildren("*","",true,false))
            {
                if(child is not ServiceMarker marker || !marker.Visible) continue;
                if ((actor==_drunksAll && !InWorld(Actors.Drunks)) || (actor==_depot && !InWorld(Actors.Depot))) continue;
                _lanServiceMarkers.Add((marker,marker.GetParent()));
                marker.Reparent(this,true); // fixed service signs stay visible when client actor roots are hidden
            }
        }
    }

    void LanRestoreServiceMarkers()
    {
        foreach(var (marker,parent) in _lanServiceMarkers)
            if(GodotObject.IsInstanceValid(marker) && GodotObject.IsInstanceValid(parent)) marker.Reparent(parent,true);
        _lanServiceMarkers.Clear();
        if(_lanFuelMarker!=null && GodotObject.IsInstanceValid(_lanFuelMarker)) _lanFuelMarker.Visible=_lanFuelMarkerVisible;
        _lanFuelMarker=null;
    }
    void LanWorldPacket(byte[] bytes,bool mesh)
    {
        if(_net.IsHost) return;
        if(!InLanRace || _lanWorld==null) {if(_lanWorldPending.Count<20000) _lanWorldPending.Add((bytes,mesh));return;}
        if(mesh) _lanWorld.MeshIn(bytes);else _lanWorld.StateIn(bytes,_track.Obstacles);
    }
    void LanEffectIn(Vector3 p,int kind)
    {
        if(!InLanRace) return;
        if(kind==4) {ShowHindWarning(p);return;}
        if(kind==5)
        {
            AddChild(new ExplosionSound {Position=p,Loudness=8,Reach=900,ReferenceDistance=160,Cannon=true});
            TankBlastFeel(p);return;
        }
        if(kind==3) {AddChild(new CannonStrikeSound {Position=p});HindStrikeFeel(p);}
        float d=p.DistanceTo(_car.Pos);
        if(kind!=3 && d<80) _cam.Shake(Math.Max(.05f,1-d/80));
        if(kind!=3) AddChild(new ExplosionSound {Position=p,Reach=kind==1?1200:650,Loudness=2});
        if(kind is 0 or 1 or 2) {AddChild(GroundImpactEffects.Create(_track,p,kind==1));return;}
        _fx.Smoke(p,Vector3.Up*3, new Color(.38f,.32f,.24f),.8f,.8f,3,2);
        if(kind!=3) for(int k=0;k<8;k++) _fx.Burst(p+Vector3.Up,new Vector3((k%3-1)*3,3+k%4,(k/3-1)*3),new Color(1,.6f,.15f),.7f,.3f,.3f,.5f,1,1,false);
    }

    void LanAuthorityInit()
    {
        _lanSequence=0;_lanInputs.Clear();_lanCorrection=null;_lanDrivers.Clear();_lanCars.Clear();_lanMechanicTarget=null;_lanRepairSerial=_lanFuelPickupSerial=0;_lanMechanicRepairing=false;_lanWorldClock=_lanSharedLightProgress=0;_lanWasDead=_lanWasDestroyed=false;_lanWreckLeft=0;
        _lanPredictedContacts=0;
        _lanWorld?.Free();_lanWorld=new LanWorld(this);
        _lanReadyCount=0;
        LanServiceMarkers(_lanReveal.Fuel && InWorld(Actors.Depot));
        LanExtraWorld();
        if(_net.IsHost)
        {
            foreach(long id in _lanReveal.Grid.Where(_net.Players.ContainsKey))
            {
                var car=id==_net.MyId?_car:new CarSim(_params,_track) {Obstacles=_track.Obstacles,Banks=_track.Banks,WorldMax=_car.WorldMax,BreakObstacle=LanBreak};
                if(id!=_net.MyId) { LanRace.GridPose(_track,_lanReveal.Grid.IndexOf(id),out var p,out float yaw); car.Reset(p,yaw); }
                LanRace.ConfigureCar(car, _power);
                var driver=new LanDriver {Car=car,Timer=new LapTimer(_track),LastInput=LanSession.Now};
                driver.Health.Reset(_lanReveal.Damage);
                driver.Tank = id == _net.MyId ? _tank : new FuelTank(_params);
                driver.Tank.Enabled = _lanReveal.Fuel;
                driver.Tank.Rate = _fuelRate * FuelGrades.Burn[(int)LanRace.Grade(_power)];
                driver.Tank.Refill(_fuelStart);
                driver.Timer.LapCompleted+=(time,_)=>LanAuthorityLap(id,driver,time);
                _lanDrivers[id]=driver;
                _lanCars.Add(car);
            }
        }
        else
        {
            _car.BreakObstacle=null;
            foreach(var packet in _lanWorldPending) {if(packet.mesh) _lanWorld.MeshIn(packet.bytes); else _lanWorld.StateIn(packet.bytes,_track.Obstacles);}
            _lanWorldPending.Clear();
            // Replicas carry visible actor poses. Original AI and audio remain dormant.
            foreach(var a in _actors) if(a.Node!=null) {
                a.Node.Visible=false;
                foreach(var child in a.Node.FindChildren("*","",true,false)) {
                    if(child is AudioStreamPlayer3D p3) p3.StreamPaused=true;
                    else if(child is AudioStreamPlayer p) p.StreamPaused=true;
                }
            }
        }
    }
    void LanAuthorityLap(long id,LanDriver d,float time)
    {
        // Race timing follows the host clock even when a rendering frame stalls.
        time=Math.Max(0,LanRaceClock-d.Total);d.Total=LanRaceClock;d.LastLap=time;
        d.Best=d.Best<0?time:Math.Min(time,d.Best);
        if(id==_net.MyId) {_timer.CopyNetwork(d.Timer.LapsDone,d.Timer.NextGate,d.Timer.Progress,d.Timer.LapTime,time,d.Best);LanLap(time);}
        if(d.Timer.LapsDone>=_lanReveal.Laps) {d.Finished=true;d.Timer.Stop();_net.FinishFor(id,d.Total,d.Best,id==_net.MyId&&_lanPad);}
    }

    void LanCanisterIn(CarSim car,bool golden)
    {
        if(!InLanRace || !_net.IsHost || _state!=State.Racing || !_lanReveal.Fuel) return;
        foreach(var d in _lanDrivers.Values)
        {
            if(d.Car!=car || d.Finished || d.Health.Dead) continue;
            float got=d.Tank.Fill(Babushkas.CanisterLitres);
            d.FuelPickupSerial++;
            if(_lanDrivers.GetValueOrDefault(_net.MyId)?.Car==car) _hud.Alert(_trackId=="bonus"?"SPASIBO, TOVARISH! +FUEL":"SPASIBO, BABUSHKA! +FUEL",
                $"+{got:0} LITRES IN THE TANK",3f,Hud.Gold);
            break;
        }
    }
    void LanInputIn(long id,DriverCommand c)
    {
        if(!InLanRace || !_net.IsHost || !_lanDrivers.TryGetValue(id,out var d) || c.Sequence<=d.Ack || c.Sequence>_lanSequence+4096) return;
        if(d.Pending.Count<64) d.Pending.TryAdd(c.Sequence,c);
        d.LastInput=LanSession.Now;_lanInputCount++;
    }
    void LanSnapshotIn(long id,Snapshot s)
    {
        if(!InLanRace) return;
        if(id==_net.MyId) { if(_lanCorrection==null || s.Time>_lanCorrection.Time) _lanCorrection=s; }
        else if(_remote.TryGetValue(id,out var remote)) remote.Buffer.Add(s);
    }
    void LanRescueIn(long id)
    {
        if(!InLanRace || !_net.IsHost || !_lanDrivers.TryGetValue(id,out var d) || _state!=State.Racing || d.Health.Dead || LanRaceClock-d.RescueAt<3) return;
        d.RescueAt=LanRaceClock;LanPutOnRoad(d,false);
    }
    void LanPutOnRoad(LanDriver d,bool repair)
    {
        int i=d.Timer.RescueMetre(new Vector2(d.Car.Pos.X,d.Car.Pos.Z));
        int start=i;
        for(int attempt=0;attempt<8;attempt++) {
            i=(start-attempt*5+_track.Center.Length)%_track.Center.Length;
            var candidate=_track.CenterPos(i)+Vector3.Up*.6f;
            bool blocked=_lanDrivers.Values.Any(other=>other!=d && other.Car.Pos.DistanceSquaredTo(candidate)<25);
            if(!blocked && !_track.Obstacles.Any(o=>o.Center.DistanceTo(new Vector2(candidate.X,candidate.Z))<o.Radius+1.2f)) break;
        }
        var p=_track.Center[i];var dir=_track.CenterDir(i);
        d.Car.Reset(new Vector3(p.X,_track.MeshHeight(p.X,p.Y)+.6f,p.Y),TrackData.YawFor(dir));
        d.Timer.ResetMovementHistory();
        // A rescue keeps gate progress; it cannot advance a lap or skip checkpoints.
        if(repair) {d.Health.Reset(_lanReveal.Damage);d.Tank.Refill();d.DryWait=0;d.Wreck=0;d.Dented=false;d.RepairSerial++;d.BrokenHeadlights=0;}
    }

    /// <summary>The host assigns the single field mechanic to the driver who honks nearest the post.</summary>
    void LanMechanicTick(float dt)
    {
        if(!_net.IsHost || _drunks==null || _state!=State.Racing || _net.State!=LanSession.Phase.Race) return;
        if(_lanMechanicTarget!=null && !_lanDrivers.Values.Contains(_lanMechanicTarget))
        {
            _drunksAll.ResetMechanic();
            _lanMechanicTarget=null;
        }
        if(_lanMechanicTarget==null)
        {
            if(_drunksAll.MechanicUp) return;
            Vector2 post=_drunksAll.MechanicPosition;
            _lanMechanicTarget=_lanDrivers.Values
                .Where(d=>!d.Finished && !d.Health.Dead && d.Last.Horn && new Vector2(d.Car.Pos.X,d.Car.Pos.Z).DistanceTo(post)<DrunkMen.HearRange)
                .OrderBy(d=>new Vector2(d.Car.Pos.X,d.Car.Pos.Z).DistanceSquaredTo(post)).FirstOrDefault();
            if(_lanMechanicTarget==null) return;
        }

        var target=_lanMechanicTarget;
        target.DriveOff=_drunksAll.HoldsCar && target.Last.Throttle>.3f ? target.DriveOff+dt : 0;
        if(target.DriveOff>.25f) {target.DriveOff=0;_drunksAll.Interrupt();}
        _drunksAll.MechanicTick(dt,target.Car,true,target.Last.Horn,!target.Health.Dead && (target.Health.Health<.995f || target.Dented),target.Health.Health);
        if(_drunksAll.HoldsCar)
        {
            target.Car.SetControls(false,false,0f,true);
            target.Car.Brake=1;
            target.Car.Neutral=true;
            target.WasHeld=true;
        }
        else if(target.WasHeld && target.Car==_car) {target.Car.Neutral=false;target.WasHeld=false;}
        if(_drunksAll.Repairing)
        {
            if(target.RepairFrom<0) {target.RepairFrom=target.Health.Health;target.DentsKept=1;}
            target.Health.RepairTo(target.RepairFrom+(1-target.RepairFrom)*_drunksAll.RepairProgress);
            if(target.Car==_car) ApplyTrainingPower();
        }
        else target.RepairFrom=-1;
        if(!_drunksAll.MechanicUp) _lanMechanicTarget=null;
    }
    void LanAuthorityPhysics(float dt)
    {
        LanContactProbeTick();
        bool racing=_state==State.Racing && _net.State==LanSession.Phase.Race;
        var command=DriverCommand.Of(_car,++_lanSequence,_horn.Pressed);
        command.BrokenHeadlights=(byte)((_carVis.Damage?.HeadlightWorks(true)==false?1:0) | (_carVis.Damage?.HeadlightWorks(false)==false?2:0));
        command.RepairSerial=_net.IsHost && _lanDrivers.TryGetValue(_net.MyId,out var localDriver) ? localDriver.RepairSerial : _lanRepairSerial;
        command.WaitingForGrid=LanGridWaiting;
        command.PoseTime=LanPoseClock;command.Countdown=_state==State.Countdown;
        _maxImpact=0;
        _impactMaterial=ImpactMaterial.Unknown;_impactBroke=false;
        if(_net.IsHost)
        {
            foreach(long id in _lanDrivers.Keys.Where(id=>!_net.Players.ContainsKey(id)).ToArray())
            { _lanCars.Remove(_lanDrivers[id].Car); _lanDrivers.Remove(id); }
            if(!_lanDrivers.TryGetValue(_net.MyId,out var own)) return;
            own.Last=command;own.Ack=command.Sequence;
            foreach(var (id,d) in _lanDrivers)
            {
                if(id!=_net.MyId) {
                    while(d.Pending.Count>8) d.Pending.Remove(d.Pending.First().Key);
                    if(d.Pending.Count>0) {var next=d.Pending.First();d.Last=next.Value;d.Ack=next.Key;d.Pending.Remove(next.Key);}
                    if(LanSession.Now-d.LastInput>.35) d.Last=new DriverCommand {Sequence=d.Ack,Brake=1,Handbrake=1};
                    d.Last.Apply(d.Car);
                }
                if(d.Health.Dead || d.Finished || _net.State is LanSession.Phase.Results or LanSession.Phase.SessionFinished) {d.Car.ThrottleTarget=0;d.Car.Brake=.6f;}
                // Ignore queued reports from the car replaced before this command was sent.
                d.BrokenHeadlights=d.Last.HeadlightsAfter(d.BrokenHeadlights,d.RepairSerial,_lanReveal.Damage);
                d.Car.PowerScale=d.Tank.Empty ? 0 : FuelGrades.PhysicsPercent(LanRace.Grade(_power))/100f*d.Health.PowerFactor;
                if (d.Tank.Empty) d.Car.ThrottleTarget = d.Car.Throttle = 0;
                if(racing && !d.Timer.Running && !d.Finished) d.Timer.Start();
                d.Impact=0;d.Material=ImpactMaterial.Unknown;d.ImpactBroke=false;
            }
            _lanReadyCount=_lanDrivers.Values.Count(d=>d.Last.Clutch);
            if(LanGridWaiting && _lanReadyCount==_lanDrivers.Count && _lanReadyCount>0) _net.StartCountdown();
            if(LanGridWaiting) foreach(var d in _lanDrivers.Values)
                if(!d.Last.Clutch) {d.Car.Neutral=true;d.Car.ThrottleTarget=0;}
            _lanCars.Clear();
            if(racing) foreach(var d in _lanDrivers.Values) if(!d.Finished && !d.Health.Dead) _lanCars.Add(d.Car);
            _wood.Tick(dt);_scenery.Tick(dt,_car,true);
            LanMechanicTick(dt);
            TickActors(dt,false,false);TankCrush();
            // Shorter substeps at extreme voted powers prevent tunnelling between hulls.
            var drivers=_lanDrivers.Values.ToArray();
            int steps=CarContacts.Steps(dt,drivers.Max(d=>d.Car.Speed),Substeps);
            for(int s=0;s<steps;s++) {
                foreach(var d in drivers) {d.Car.Step(dt/steps, _state==State.Countdown, LanGridWaiting);
                    if(d.Car.LastImpact>d.Impact) {d.Impact=d.Car.LastImpact;d.Material=d.Car.LastImpactMaterial;d.ImpactBroke=d.Car.LastImpactBroke;} }
                if(_babushkas!=null)
                {
                    var ownCar=_car;
                    try { foreach(var d in drivers) {_car=d.Car;_babushkas.CollideCar(d.Car);} }
                    finally {_car=ownCar;}
                }
                for(int a=0;a<drivers.Length;a++) for(int b=a+1;b<drivers.Length;b++) {
                    float hit=CarContacts.Resolve(drivers[a].Car,drivers[b].Car);
                    if(hit>1) _lanContactCount++;
                    if(hit>drivers[a].Impact) {drivers[a].Impact=hit;drivers[a].Material=ImpactMaterial.Metal;drivers[a].ImpactBroke=false;}
                    if(hit>drivers[b].Impact) {drivers[b].Impact=hit;drivers[b].Material=ImpactMaterial.Metal;drivers[b].ImpactBroke=false;}
                }
            }
            _maxImpact=own.Impact;_impactMaterial=own.Material;_impactBroke=own.ImpactBroke;TickActors(dt,false,true);
            foreach(var d in drivers) {
                _wood.Smash(d.Car);
                if(racing) {
                    d.Health.Impact(d.Impact,dt);
                    if(_lanReveal.Damage && d.Impact>4f) d.Dented=true;
                    if (!d.Finished && !d.Health.Dead) LanFuelTick(d, dt);
                    if(d.Health.Dead) {d.Wreck+=dt;if(d.Wreck>=10) LanPutOnRoad(d,true);}
                    if(!d.Finished) d.Timer.Update(dt,new Vector2(d.Car.Pos.X,d.Car.Pos.Z));
                }
            }
            _health.NetworkStatus(own.Health.Health,own.Health.Destroyed);
            _lanWreckLeft=own.Health.Dead?Math.Max(0,10-own.Wreck):0;
            _timer.CopyNetwork(own.Timer.LapsDone,own.Timer.NextGate,own.Timer.Progress,Math.Max(0,LanRaceClock-own.Total),own.LastLap,own.Best);
            if(racing)
            {
                int gates=Math.Max(1,_track.Center.Length/LapTimer.GateStep);
                foreach(var d in drivers)
                    _lanSharedLightProgress=Math.Max(_lanSharedLightProgress,
                        Math.Clamp(LanRace.Score(d.Timer.LapsDone,d.Timer.NextGate,d.Timer.Progress,
                            _track.Center.Length,LapTimer.GateStep)/gates,0,_lanReveal.Laps));
            }
            if(++_lanTick%4==0) foreach(var (id,d) in _lanDrivers) {
                var snap=Snapshot.Of(d.Car,LanPoseClock,d.Impact,d.Timer.LapsDone,d.Timer.NextGate,d.Timer.Progress,d.Finished);
                snap.GridReadyCount=_lanReadyCount;
                snap.BrokenHeadlights=d.BrokenHeadlights;
                snap.WorldLightProgress=_lanSharedLightProgress;
                snap.FinishTime=d.Finished?d.Total:-1;snap.Ack=d.Ack;snap.RepairSerial=d.RepairSerial;snap.FuelPickupSerial=d.FuelPickupSerial;snap.MechanicRepairing=d==_lanMechanicTarget && _drunksAll is { Repairing:true };snap.State=d.Car.NetworkState();snap.Refuelling=LanRefuelling(d);snap.FuelLitres=d.Tank.Litres;snap.Health=d.Health.Health;snap.Destroyed=d.Health.Destroyed;snap.WreckLeft=d.Health.Dead?Math.Max(0,10-d.Wreck):0;
                snap.LapTime=Math.Max(0,LanRaceClock-d.Total);snap.LastLap=d.LastLap;snap.BestLap=d.Best;
                _net.SendSnapshot(id,snap);
                if(id!=_net.MyId && _remote.TryGetValue(id,out var rc)) rc.Buffer.Add(snap);
            }
            if((_lanWorldClock+=dt)>=.05f) {_lanWorldClock=0;_lanWorld.Capture(_actors.Select(a=>a.Node).Concat(_lanExtraRoots),_track.Obstacles,_worldObstacles.Select(o=>o.index).Concat(_lanExtraSlots),(bytes,mesh)=>_net.SendWorld(bytes,mesh));}
        }
        else
        {
            _wood.Tick(dt);_scenery.Tick(dt,_car,false);
            if(_lanCorrection is {} correction) {
                _lanCorrection=null;
                if(correction.State.Length>0) {
                    var predicted=_car.Pos;
                    _car.RestoreNetwork(correction.State);
                    if(_net.State is LanSession.Phase.Results or LanSession.Phase.SessionFinished) _lanInputs.Clear();
                    else _lanInputs.RemoveAll(c=>c.Sequence<=correction.Ack);
                    foreach(var c in _lanInputs) {c.Apply(_car);LanPredictDrive(c,dt,false);}
                    _lanCorrectionError=Math.Max(_lanCorrectionError,predicted.DistanceTo(_car.Pos));
                }
                _health.NetworkStatus(correction.Health,correction.Destroyed);_lanWreckLeft=correction.WreckLeft;
                if (correction.FuelLitres >= 0) _tank.Litres = correction.FuelLitres;
                _lanSharedLightProgress=Math.Max(_lanSharedLightProgress,correction.WorldLightProgress);
                _lanReadyCount=correction.GridReadyCount;
                if(correction.FuelPickupSerial>_lanFuelPickupSerial)
                {
                    _lanFuelPickupSerial=correction.FuelPickupSerial;
                    _hud.Alert(_trackId=="bonus"?"SPASIBO, TOVARISH! +FUEL":"SPASIBO, BABUSHKA! +FUEL",
                        "JERRYCAN RECEIVED - FUEL ADDED",3f,Hud.Gold);
                }
                _refuelling = correction.Refuelling;
                bool repaired=correction.RepairSerial>_lanRepairSerial;
                if(repaired) {_carVis.Damage?.Reset();_lanRepairSerial=correction.RepairSerial;}
                if(!_net.IsHost)
                {
                    if(correction.MechanicRepairing && !_lanMechanicRepairing) _hud.Show("THE MECHANIC IS ON THE JOB","THE ENGINE MENDS BY THE SECOND. THROTTLE BREAKS IT OFF",3f,Hud.Paper);
                    if(!correction.MechanicRepairing && _lanMechanicRepairing) _hud.Show(repaired?"AS GOOD AS NEW!":"REPAIR BROKEN OFF",repaired?"THE MECHANIC'S FEE: A BOTTLE":$"ENGINE AT {Math.Round(correction.Health*100)} %",3f,repaired?Hud.Gold:Hud.Paper,plain:repaired?"REPAIRED":"REPAIR STOPPED");
                }
                _lanMechanicRepairing=correction.MechanicRepairing;
                bool lap=correction.Laps>_timer.LapsDone;
                _timer.CopyNetwork(correction.Laps,correction.NextGate,correction.Progress,correction.LapTime,correction.LastLap,correction.BestLap);
                if(lap && _net.State==LanSession.Phase.Race) LanLap(correction.LastLap);
                if(_net.State==LanSession.Phase.Race) _lanDone=correction.Finished;
            }
            command.Apply(_car);
            if(LanGridWaiting && !command.Clutch) {_car.Neutral=true;_car.ThrottleTarget=0;}
            if (_tank.Empty) { _car.ThrottleTarget = _car.Throttle = 0; _car.PowerScale = 0; }
            if(_net.State==LanSession.Phase.Race) _lanInputs.Add(command);
            // A stalled connection must not grow replay work without bound.
            if(_lanInputs.Count>240) _lanInputs.RemoveRange(0,_lanInputs.Count-240);
            LanPredictDrive(command,dt,true);
            if(++_lanTick%4==0) _net.SendInputs(_lanInputs);
        }
        HitFeedback(dt,0,0);
        _carVis.Damage?.Hit(_maxImpact,_car.LastImpactLocal,_car.LastImpactDir,_car,_fx);
        if(_health.Dead && (!_lanWasDead || _health.Destroyed&&!_lanWasDestroyed)) {
            if(_health.Destroyed) _engineFx.Explode(_car,_carVis,_car.Pos);else _engineFx.Seize(_car);
            _hud.Alert("CAR WRECKED", "A NEW PYATYORKA IN TEN SECONDS",3f,CCCPRacing.UI.Hud.Red);
        }
        if(!_health.Dead && _lanWasDead) {_engineFx.Reset();_carVis.Damage?.Reset();}
        _lanWasDead=_health.Dead;_lanWasDestroyed=_health.Destroyed;
        _snd.EngineLevel=_health.Dead || _tank.Empty?0:1;_snd.Misfire=_health.Smoking?.5f:0;
        _engineFx.Tick(dt,_car,_health);_fx.Emit(_car,dt);
        if (_tank.Empty && !_dry) _hud.Alert("OUT OF FUEL", "STOP FOR EMERGENCY FUEL - TEN SECONDS", 4f, Hud.Red);
        _dry = _tank.Empty;
        if (_net.IsHost)
        {
            _refuelling = _lanDrivers.TryGetValue(_net.MyId, out var local) && LanRefuelling(local);
            _depot.Pouring = _lanDrivers.Values.Any(LanRefuelling);
        }
        _lapClock=_timer.LapTime;
        foreach(var rc in _remote.Values)
        {
            float time=rc.Buffer.PresentationTime(LanPoseClock,_car.Pos);
            rc.Buffer.Apply(time,rc.Puppet,dt,ref rc.AppliedTime);
            if(_net.IsHost && _lanDrivers.TryGetValue(rc.Id,out var actual))
            {rc.Puppet.Pos=actual.Car.Pos;rc.Puppet.Rot=actual.Car.Rot;rc.Puppet.Vel=actual.Car.Vel;}
            rc.TickEngine(dt);_fx.Emit(rc.Puppet,dt,rc.FxSlot);
        }
        LanReplaySample();
    }

    /// <summary>Used for live client prediction and unacknowledged-input replay. Remote poses are read at each input's historical time.</summary>
    void LanPredictDrive(DriverCommand command,float dt,bool feedback)
    {
        float speed=_car.Speed;
        foreach(var rc in _remote.Values) speed=Math.Max(speed,rc.Buffer.Latest?.Frame.Vel.Length() ?? 0);
        int steps=CarContacts.Steps(dt,speed,Substeps);
        float h=dt/steps;
        for(int s=0;s<steps;s++)
        {
            _car.Step(h,command.Countdown,command.WaitingForGrid);
            if(feedback && _car.LastImpact>_maxImpact)
            {_maxImpact=_car.LastImpact;_impactMaterial=_car.LastImpactMaterial;_impactBroke=_car.LastImpactBroke;}
            if(command.WaitingForGrid) continue;
            float time=command.PoseTime-dt+(s+1)*h;
            foreach(var rc in _remote.Values)
            {
                float hit=rc.Buffer.PredictContact(time,_car,rc.ContactProxy);
                if(feedback && hit>1) _lanPredictedContacts++;
                if(feedback && hit>_maxImpact)
                {_maxImpact=hit;_impactMaterial=ImpactMaterial.Metal;_impactBroke=false;}
            }
        }
    }

    bool LanRefuelling(LanDriver d) => _state == State.Racing && !d.Finished && !d.Health.Dead && d.Tank.Enabled && d.Tank.Fraction < .999f
        && InWorld(Actors.Depot) && _depot.FillsCar(new Vector2(d.Car.Pos.X,d.Car.Pos.Z),d.Car.Speed);

    // Host owns every tank. Depot refuelling keeps the race clock running; a dry,
    // stationary car receives emergency fuel after ten seconds to avoid a permanent DNF.
    void LanFuelTick(LanDriver d, float dt)
    {
        LanRace.TickFuel(d.Tank, d.Car, d.Last.Pad,
            InWorld(Actors.Depot) && _depot.FillsCar(new Vector2(d.Car.Pos.X,d.Car.Pos.Z),d.Car.Speed), dt, ref d.DryWait);
    }

    IEnumerable<CarSim> HindCannonTargets()
    {
        if(_state!=State.Racing) yield break;
        if(InLanRace)
        {
            if(_net.IsHost) foreach(var driver in _lanDrivers.Values) yield return driver.Car;
        }
        else yield return _car;
    }

    // Explosions and cannon rounds affect every authoritative driver, even if an NPC targeted another player.
    bool LanWorldHit(Vector3 p,int kind)
    {
        if(!InLanRace) return false;
        if(!_net.IsHost || _state!=State.Racing) return true;
        _net.SendEffect(p,kind);
        _scenery.Blast(p,kind==3?1.2f:kind==0?Breakables.ShellReach:Breakables.BlastReach);
        foreach(var d in _lanDrivers.Values) {
            float distance=kind==3?ProjectileHits.Distance(d.Car,p):p.DistanceTo(d.Car.Pos);
            float damage=kind switch {0=>EngineHealth.ShellDamage(distance),1=>EngineHealth.JetDamage(distance),2=>EngineHealth.MissileDamage(distance),_=>EngineHealth.CannonDamage(distance)};
            if(damage>=1) d.Health.Destroy("LAN world hit");else d.Health.Damage(damage,"LAN world hit");
            if(_lanReveal.Damage && kind==3 && damage>0) d.Dented=true;
            if(distance<12 && kind!=3) d.Car.Blast(p,kind==2?1.5f:1f);
        }
        return true;
    }
}
