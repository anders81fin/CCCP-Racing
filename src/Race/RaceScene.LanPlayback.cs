using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CCCPRacing.Net;
using CCCPRacing.Audio;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Race;

/// <summary>Read-only LAN viewer in a separate game process: no transport, campaign IO or live simulation.</summary>
public partial class RaceScene
{
    public LanReplayFile.Reader LanPlaybackSource {get;private set;}
    bool _lanPlaybackOldMute;
    public static RaceScene CreateLanPlayback(LanReplayFile.Reader source,bool externallyDriven=false)
    {
        var scene=new RaceScene {LanPlaybackSource=source,_lanPlaybackExternallyDriven=externallyDriven,InstructionTrack=source.Metadata.Track,Visible=false,_lanPlaybackOldMute=AudioServer.IsBusMute(0)};
        if(!externallyDriven) scene.LanPlaybackInitAccess();
        foreach(string arg in OS.GetCmdlineUserArgs()) {
            if(arg.StartsWith("--lan-replay-sounds=")) {
                var levels=arg["--lan-replay-sounds=".Length..].Split(',');
                if(levels.Length!=SoundBalance.Count) throw new InvalidDataException("Invalid sound settings");
                for(int i=0;i<levels.Length;i++) SoundBalance.Levels[i]=Math.Clamp(int.Parse(levels[i]),0,SoundBalance.Max);
                SoundBalance.Apply();
            }
            if(arg.StartsWith("--lan-replay-video=")) {
                var levels=arg["--lan-replay-video=".Length..].Split(',');
                if(levels.Length is not (2 or 3)) throw new InvalidDataException("Invalid video settings");
                _aaMode=Math.Clamp(int.Parse(levels[0]),0,AaNames.Length-1);_softLevel=Math.Clamp(int.Parse(levels[1]),0,1);
                CCCPRacing.UI.PixelFrame.Native=levels.Length==3 && int.Parse(levels[2])==1;      // VISUALS > RENDERING of the race window (absent from older launchers: PIXEL)
            }
            if(arg=="--lan-replay-muted") scene._lanPlaybackOldMute=true;
        }
        DisplayServer.WindowSetTitle("CCCP Racing - LAN REPLAY");
        AudioServer.SetBusMute(0,true); // world construction must not emit live actor sounds
        return scene;
    }
    bool _lanPlaybackExternallyDriven;
    public bool LanReplayExportReady=>LanPlaybackSource!=null && !_booting && _lanPlaybackLabel!=null;
    public string LanReplayExportPath=>LanPlaybackSource?.SourcePath;
    public float LanReplayExportStart=>_lanPlaybackStart;
    public float LanReplayExportEnd=>LanPlaybackSource?.Duration ?? 0;
    public float LanReplayExportClock=>_lanPlaybackTime;
    public bool LanReplayExportPaused=>_lanPlaybackPaused;
    public long LanReplayExportFocus=>LanPlaybackSource?.Metadata.Players[_lanPlaybackCar].Id ?? 0;
    /// <summary>The export child, never the running LAN host/client, owns this deterministic driver.</summary>
    public void PrepareLanReplayExport(float start,long focus)
    {
        if(!LanReplayExportReady || !float.IsFinite(start) || start<_lanPlaybackStart || start>=LanPlaybackSource.Duration)
            throw new InvalidDataException("LAN export start is outside the recorded race");
        int car=Array.FindIndex(LanPlaybackSource.Metadata.Players,p=>p.Id==focus);
        if(car<0) throw new InvalidDataException("LAN export focus is not in the recorded roster");
        _lanPlaybackExternallyDriven=true;_lanPlaybackCar=car;_lanPlaybackPaused=true;_lanPlaybackLoop=false;
        AudioMix.MuteSfx(false); // the export child's title scene previously muted this shared bus
        _lanPlaybackSpeed=1;_lanPlaybackLabel.Hide();LanPlaybackSeek(start);
    }
    public void RenderLanReplayExportFrame(float time)
    {
        if(!_lanPlaybackExternallyDriven || !LanReplayExportReady || !float.IsFinite(time) || time<_lanPlaybackTime || time>LanPlaybackSource.Duration)
            throw new InvalidDataException("LAN export frame clock is invalid");
        float dt=time-_lanPlaybackTime;
        if(dt>.10001f) throw new InvalidDataException("LAN export frame step exceeds recording playback limit");
        _lanPlaybackPaused=false;LanPlaybackFrame(dt,time);
    }
    float _lanPlaybackTime,_lanPlaybackStart,_lanPlaybackSpeed=1;
    int _lanPlaybackIndex,_lanPlaybackCar;
    bool _lanPlaybackPaused,_lanPlaybackLoop;
    Ghost _lanPlaybackOwn;
    readonly List<(GroundImpactEffects node,float advanced)> _lanPlaybackEffects=new();
    readonly List<Node> _lanPlaybackOneShots=new();
    float _lanPlaybackBreakTime;
    Label _lanPlaybackLabel;
    readonly Dictionary<long,RemoteCar> _lanPlaybackCars=new();
    readonly Dictionary<long,Snapshot> _lanPlaybackLatest=new();
    readonly HashSet<long> _lanPlaybackDecodedPrototypes=new();
    void LanPlaybackEnter()
    {
        if (LanPlaybackSource.Metadata.Protocol >= 12)
            FuelGrades.ApplyEngine(_params, LanRace.Grade(LanPlaybackSource.Metadata.Fuel));
        _lanPlaybackCheck=OS.GetCmdlineUserArgs().Contains("--lan-replay-check");
        if(TrackSignature.Compute(_track)!=LanPlaybackSource.Metadata.Signature) {
            GD.PushError("LAN replay: track signature mismatch; playback refused");CCCPRacing.Audio.AudioMix.Quit(this, 2);return;
        }
        // This is a private process. Disable all original actor/audio scripts; only recorded replicas run.
        foreach(Node n in FindChildren("*","",true,false)) {
            n.ProcessMode=ProcessModeEnum.Disabled;
            if(n is AudioStreamPlayer3D a) a.Stop();
            if(n is AudioStreamPlayer b) b.Stop();
        }
        foreach(var actor in _actors) actor.Node?.Hide();
        foreach(Node n in FindChildren("*","",true,false))
            if(n is KolkhozWorkers or VillageLife or PartyRowboat or PartyPondDuck or ZoneDosimetrist or DachaPatrol || n==_guards)
                if(n is Node3D root) root.Hide();
        _carVis.Hide();_ghostVis.Hide();_hud.Hide();
        var layer=new CanvasLayer {Layer=20};AddChild(layer);
        _lanPlaybackLabel=new Label {Position=new Vector2(8,8),MouseFilter=Control.MouseFilterEnum.Ignore};
        _lanPlaybackLabel.AddThemeFontSizeOverride("font_size",11);layer.AddChild(_lanPlaybackLabel);
        _lanPlaybackCar=Math.Max(0,Array.FindIndex(LanPlaybackSource.Metadata.Players,p=>p.Id==LanPlaybackSource.Metadata.LocalId));
        _lanPlaybackStart=LanPlaybackSource.Entries.FirstOrDefault(e=>e.Type==LanReplayFile.Kind.Go)?.Time ?? 0;
        var own=LanPlaybackSource.Entries.LastOrDefault(e=>e.Type==LanReplayFile.Kind.OwnCar);
        if(own!=null) {
            _lanPlaybackOwn=Ghost.FromBytes(LanPlaybackSource.Read(own),_params,_track,out string error);
            if(_lanPlaybackOwn==null) throw new InvalidDataException("LAN own-car tape: "+error);
        } else GD.PushWarning("LAN replay: development network-only recording; original local body damage absent");
        LanPlaybackSeek(_lanPlaybackStart);
        ApplyAntialiasing(GetViewport());ApplySoftening();
        foreach(string arg in OS.GetCmdlineUserArgs()) {
            if(arg.StartsWith("--lan-replay-shot=")) _lanPlaybackShot=arg["--lan-replay-shot=".Length..];
        }
        if(_lanPlaybackShot!=null) {
            _lanPlaybackPaused=true;_lanPlaybackLabel.Hide();
            string clock=OS.GetCmdlineUserArgs().FirstOrDefault(a=>a.StartsWith("--lan-replay-shot-time="));
            if(clock!=null) LanPlaybackSeek(float.Parse(clock["--lan-replay-shot-time=".Length..],System.Globalization.CultureInfo.InvariantCulture));
        }
        Visible=true;AudioServer.SetBusMute(0,_lanPlaybackOldMute);
        GD.Print($"LAN replay: viewer ready, {LanPlaybackSource.Metadata.Players.Length} cars, {LanPlaybackSource.Entries.Count} packets, {LanPlaybackSource.Duration:F2} s");
    }
    void LanPlaybackReset()
    {
        if(_lanWorld==null) _lanWorld=new LanWorld(this,128L*1024*1024);else _lanWorld.ResetPlaybackState();
        _lanWorld.SetPaused(true);_lanPlaybackLatest.Clear();
        int grid=0;
        foreach(var p in LanPlaybackSource.Metadata.Players) {
            if(!_lanPlaybackCars.TryGetValue(p.Id,out var c)) {
                c=new RemoteCar(p.Id,p.Name,p.Slot,p.Slot,_params,_track,this,_fx);_lanPlaybackCars[p.Id]=c;
            } else c.ResetPlayback(_fx);
            if (LanPlaybackSource.Metadata.Protocol >= 12) c.ConfigureFuel(LanPlaybackSource.Metadata.Fuel);
            LanRace.GridPose(_track,grid++,out var at,out float yaw);c.Puppet.Reset(at,yaw);
            var standing=Snapshot.Standing(at,yaw,0,_params.IdleRpm);
            c.Buffer.Add(standing);_lanPlaybackLatest[p.Id]=standing;
        }
        foreach(var (node,_) in _lanPlaybackEffects) {node.Hide();node.QueueFree();}
        _lanPlaybackEffects.Clear();
        foreach(var node in _lanPlaybackOneShots) if(GodotObject.IsInstanceValid(node)) {if(node is AudioStreamPlayer3D audio) audio.Stop();node.ProcessMode=ProcessModeEnum.Disabled;node.QueueFree();}
        _lanPlaybackOneShots.Clear();_fx.ResetPlaybackEmission();
        _wood.Reset();_scenery.Reset();_lanPlaybackIndex=0;_lanPlaybackBreakTime=0;
        _lanPlaybackOwn?.Rewind();
    }
    static Vector3 LanReplayVector(BinaryReader r)=>new(r.ReadSingle(),r.ReadSingle(),r.ReadSingle());
    void LanPlaybackPacket(LanReplayFile.Entry e,bool seek)
    {
        LanPlaybackAdvanceBreaks(e.Time);
        byte[] bytes=LanPlaybackSource.Read(e);
        using var r=new BinaryReader(new MemoryStream(bytes));
        switch(e.Type) {
            case LanReplayFile.Kind.Mesh:
                if(_lanPlaybackDecodedPrototypes.Add(e.Offset)) _lanWorld.MeshIn(bytes);break;
            case LanReplayFile.Kind.World:_lanWorld.StateIn(bytes,_track.Obstacles);break;
            case LanReplayFile.Kind.Car:
                long id=r.ReadInt64();var snap=Snapshot.Read(r);
                if(!_lanPlaybackCars.TryGetValue(id,out var c)) throw new InvalidDataException("Unknown replay player");
                c.Buffer.Add(snap);_lanPlaybackLatest[id]=snap;
                if(snap.State.Length>0) c.Puppet.RestoreNetwork(snap.State);
                float applied=float.NegativeInfinity;c.Buffer.Apply(snap.Time,c.Puppet,0,ref applied);
                c.TickEngine(0);break;
            case LanReplayFile.Kind.Break:
                if(seek) LanPlaybackAdvanceBreaks(e.Time);
                int i=r.ReadInt32();float hit=r.ReadSingle();var vel=LanReplayVector(r);var pos=LanReplayVector(r);
                if(i<0) _wood.BreakNetwork(-1-i,vel);else if(i<_track.Obstacles.Count) _scenery.TryBreak(i,hit,vel,pos);
                break;
            case LanReplayFile.Kind.Effect:
                var at=LanReplayVector(r);int kind=r.ReadInt32();
                if(kind is <0 or >5) throw new InvalidDataException("Unsupported LAN effect type");
                float age=Math.Max(0,_lanPlaybackTime-e.Time);
                if(kind==3) _fx.Smoke(at,Vector3.Up*3,new Color(.38f,.32f,.24f),.8f,.8f,3,2);
                if(kind is 0 or 1 or 2 && age<(kind==1?28:35)) {
                    var effect=GroundImpactEffects.Create(_track,at,kind==1);effect.ProcessMode=ProcessModeEnum.Disabled;
                    AddChild(effect);effect.PreRoll(age);_lanPlaybackEffects.Add((effect,_lanPlaybackTime));
                }
                if(!seek && kind!=4) {
                    Node sound=kind==3?new CannonStrikeSound {Position=at}:new ExplosionSound {Position=at,Cannon=kind==5,Reach=kind==5?900:650,Loudness=kind==5?8:2};
                    AddChild(sound);_lanPlaybackOneShots.Add(sound);
                }
                break;
        }
    }
    void LanPlaybackSeek(float t)
    {
        var watch=System.Diagnostics.Stopwatch.StartNew();
        long allocated=GC.GetAllocatedBytesForCurrentThread();
        bool muted=AudioServer.IsBusMute(0);AudioServer.SetBusMute(0,true);
        _lanPlaybackTime=Math.Clamp(t,_lanPlaybackStart,LanPlaybackSource.Duration);
        LanPlaybackReset();
        var entries=LanPlaybackSource.Entries;
        int end=0;while(end<entries.Count && entries[end].Time<=_lanPlaybackTime) end++;
        // Prototypes are immutable; a full world packet is already a keyframe. Historical
        // world decoding/mesh creation cannot affect car, health or break-event reconstruction.
        int world=-1;
        for(int i=0;i<end;i++) {
            var e=entries[i];
            if(e.Type==LanReplayFile.Kind.Mesh && _lanPlaybackDecodedPrototypes.Add(e.Offset)) _lanWorld.MeshIn(LanPlaybackSource.Read(e));
            if(e.Type==LanReplayFile.Kind.World) world=i;
        }
        for(int i=0;i<end;i++) if(entries[i].Type is LanReplayFile.Kind.Car or LanReplayFile.Kind.Break or LanReplayFile.Kind.Effect)
            LanPlaybackPacket(entries[i],true);
        if(world>=0) _lanWorld.StateIn(LanPlaybackSource.Read(entries[world]),_track.Obstacles);
        _lanPlaybackIndex=end;
        LanPlaybackAdvanceBreaks(_lanPlaybackTime);
        _lanWorld.Snap();_cam.ClearCollision();
        if(_lanPlaybackOwn?.CollisionBefore(_lanPlaybackTime) is Ghost.Collision cameraHit)
            _cam.RestoreCollision(cameraHit.Speed,_params.Mass,_lanPlaybackTime-cameraHit.Time,_lanPlaybackOwn.Sample(_lanPlaybackTime).Origin);
        LanPlaybackPose(0,true);
        _lanWorld.SetPaused(_lanPlaybackPaused);
        if(_lanPlaybackOwn?.CollisionBefore(_lanPlaybackTime) is Ghost.Collision hit)
            _lanPlaybackCars[LanPlaybackSource.Metadata.LocalId].Sound.RestoreCollision(hit.Speed,_params.Mass,hit.Material,hit.Broke,_lanPlaybackTime-hit.Time);
        _fx.Tick(0,_cam.GlobalBasis);
        AudioServer.SetBusMute(0,muted);
        if(_lanPlaybackCheck) GD.Print($"LANREPLAYMETRIC seek={_lanPlaybackTime:F2} ms={watch.Elapsed.TotalMilliseconds:F1} allocated={GC.GetAllocatedBytesForCurrentThread()-allocated} objects={Performance.GetMonitor(Performance.Monitor.ObjectCount)}");
    }
    void LanPlaybackAdvanceBreaks(float target)
    {
        // Broken scenery has a persistent final pose. Age it on the recorded clock, including backward seeks.
        while(_lanPlaybackBreakTime<target) {
            float step=Math.Min(.05f,target-_lanPlaybackBreakTime);
            _wood.Tick(step);_scenery.Tick(step,_car,false);
            foreach(var c in _lanPlaybackCars.Values) {c.TickEngine(step);_fx.Emit(c.Puppet,step,c.FxSlot);LanCarSmoke(c.Puppet,c.Fuel,c.FxSlot,step);}
            _fx.AdvancePlayback(step);_lanPlaybackBreakTime+=step;
        }
    }
    void LanPlaybackPose(float dt,bool snapCamera=false)
    {
        var roster=LanPlaybackSource.Metadata.Players;
        long focus=roster[_lanPlaybackCar].Id;
        float progress=0;
        foreach(var (id,c) in _lanPlaybackCars) {
            // Arrival-time playback must not make a late packet rewind the viewer clock.
            if(!_lanPlaybackLatest.TryGetValue(id,out var s)) {c.Vis.Hide();continue;}
            c.Vis.Show();
            float applied=float.NegativeInfinity;
            c.Buffer.Apply(Math.Max(0,_lanPlaybackTime-SnapshotBuffer.Delay),c.Puppet,dt,ref applied);
            if(id==LanPlaybackSource.Metadata.LocalId && _lanPlaybackOwn!=null) {
                var events=_lanPlaybackOwn.Apply(_lanPlaybackTime,c.Puppet,dt);
                if(dt>0 && events.Impact>3) {c.Sound.Collision(events.Impact,_params.Mass,events.ImpactMaterial,events.ImpactBroke);_cam.Collision(events.Impact,_params.Mass,c.Puppet.Pos);}
                _lanPlaybackOwn.ApplyDamage(_lanPlaybackTime,c.Vis.Damage,_fx);
            }
            c.Health.NetworkStatus(s.Health,s.Destroyed);
            c.Sound.EngineLevel=c.Health.Dead || s.FuelLitres==0?0:1;
            if(dt>0 && c.Sound.Gear!=c.Puppet.Gear) c.Sound.GearChange(c.Puppet.Gear>c.Sound.Gear,c.Puppet.Throttle);
            c.Sound.Speed=c.Puppet.Speed;c.Sound.Gear=c.Puppet.Gear;c.Sound.Airborne=c.Puppet.AirTime>0;
            c.Sound.Driven=c.Puppet.Clutch || c.Puppet.Neutral?0:1;
            c.Vis.Sync(c.Puppet,_track);
            c.UpdateSound(_lanPlaybackCars[focus].Puppet.Pos,1,_lanPlaybackPaused);
            c.Sound.ProcessMode=_lanPlaybackPaused?ProcessModeEnum.Disabled:ProcessModeEnum.Inherit;
            foreach(Node n in c.Sound.GetChildren()) if(n is AudioStreamPlayer audio) audio.StreamPaused=_lanPlaybackPaused;
            c.UpdateLights(dt,_dayNight.Beams);
            if(id==focus) c.Sound.Volume=_lanPlaybackPaused?0:.8f;
            progress=Math.Max(progress,s.WorldLightProgress);
        }
        _dayNight.PinnedClock=LanLightClock(progress,LanPlaybackSource.Metadata.Seed);
        _dayNight.Update(dt,true,0,0,true,0);
        var car=_lanPlaybackCars[focus].Puppet;
        if(snapCamera) _cam.SnapDriving(car.Pos,car.Vel);else if(!_lanPlaybackPaused) _cam.Follow(car.Pos,car.Vel,dt);
        _lanWorld.SetPaused(_lanPlaybackPaused);
        _lanWorld.Frame(dt);
        if(!_lanPlaybackPaused) LanSootTick(dt);
        string exportHint=LanPlaybackMayExport?"E export MP4":"MP4 AVAILABLE AFTER LAN SESSION";
        _lanPlaybackLabel.Text=$"LAN REPLAY  {roster[_lanPlaybackCar].Name}\n{_lanPlaybackTime-_lanPlaybackStart:F1} / {LanPlaybackSource.Duration-_lanPlaybackStart:F1} s  {_lanPlaybackSpeed:0.##}x\nSPACE pause  LEFT/RIGHT seek  TAB car\nUP/DOWN speed  HOME restart  L loop\n{exportHint}  H controls  ESC close\nREMOTE BODY DENTS NOT RECORDED";
    }
    void LanPlaybackFrame(float dt,float? forcedClock=null)
    {
        if(_lanPlaybackLabel==null) return;
        try {
            if(forcedClock.HasValue) _lanPlaybackTime=forcedClock.Value;
            else if(!_lanPlaybackPaused) _lanPlaybackTime=Math.Min(LanPlaybackSource.Duration,_lanPlaybackTime+Math.Min(dt,.1f)*_lanPlaybackSpeed);
            while(_lanPlaybackIndex<LanPlaybackSource.Entries.Count && LanPlaybackSource.Entries[_lanPlaybackIndex].Time<=_lanPlaybackTime)
                LanPlaybackPacket(LanPlaybackSource.Entries[_lanPlaybackIndex++],false);
            LanPlaybackPose(_lanPlaybackPaused?0:dt*_lanPlaybackSpeed);
            if(!_lanPlaybackPaused) {
                LanPlaybackAdvanceBreaks(_lanPlaybackTime);
                for(int i=0;i<_lanPlaybackEffects.Count;i++) {
                    var (node,advanced)=_lanPlaybackEffects[i];
                    if(GodotObject.IsInstanceValid(node)) node.PreRoll(Math.Max(0,_lanPlaybackTime-advanced));
                    _lanPlaybackEffects[i]=(node,_lanPlaybackTime);
                }
            }
            _fx.Tick(0,_cam.GlobalBasis);
            _lanPlaybackEffects.RemoveAll(x=>!GodotObject.IsInstanceValid(x.node));
            _lanPlaybackOneShots.RemoveAll(x=>!GodotObject.IsInstanceValid(x));
            foreach(var node in _lanPlaybackOneShots) {
                node.ProcessMode=_lanPlaybackPaused?ProcessModeEnum.Disabled:ProcessModeEnum.Inherit;
                if(node is AudioStreamPlayer3D a) a.StreamPaused=_lanPlaybackPaused;
            }
            if(_lanPlaybackTime>=LanPlaybackSource.Duration) {
                if(_lanPlaybackLoop) { LanPlaybackSeek(0); if(_lanPlaybackCheck) _lanPlaybackProbeLooped=true; }else _lanPlaybackPaused=true;
            }
            LanPlaybackProbe();
            if(_lanPlaybackShot!=null && ++_lanPlaybackShotFrames==5) {
                var image=GetViewport().GetTexture().GetImage();
                var result=image.SavePng(_lanPlaybackShot);image.Dispose();
                GD.Print("LAN replay screenshot: "+result+" "+_lanPlaybackShot);CCCPRacing.Audio.AudioMix.Quit(this, result==Error.Ok?0:2);
            }
        } catch(Exception e) {GD.PushError("LAN replay failed safely: "+e.Message);CCCPRacing.Audio.AudioMix.Quit(this, 2);}
    }
    string _lanPlaybackShot;
    int _lanPlaybackShotFrames;
    int _lanPlaybackProbeFrame,_lanPlaybackProbeStep;
    bool _lanPlaybackCheck, _lanPlaybackProbeLooped;
    void LanPlaybackProbe()
    {
        if(!_lanPlaybackCheck || ++_lanPlaybackProbeFrame%12!=0) return;
        float[] seeks={0,.8f,.25f,1,.5f,0,.9f};
        if(_lanPlaybackProbeStep>=seeks.Length) {
            if(_lanPlaybackProbeStep==seeks.Length) {
                GD.Print($"LANREPLAYTEST: PASS {seeks.Length} seeks, { _lanPlaybackCars.Count} cars, own damage {_lanPlaybackOwn?.HasDamage ?? false}, no LAN transport {LanSession.Current==null}");
                _lanPlaybackProbeStep++;_lanPlaybackPaused=false;_lanPlaybackLoop=true;
                LanPlaybackSeek(Math.Max(_lanPlaybackStart,LanPlaybackSource.Duration-.05f));
                _lanPlaybackProbeFrame=0;return;
            }
            if(_lanPlaybackProbeFrame>=120) {
                if(!_lanPlaybackProbeLooped) throw new InvalidDataException("LAN viewer did not loop after resuming");
                GD.Print("LANREPLAYTEST: resumed and looped after seeks");CCCPRacing.Audio.AudioMix.Quit(this);
            }
            return;
        }
        _lanPlaybackPaused=true;
        _lanPlaybackCar=_lanPlaybackProbeStep%LanPlaybackSource.Metadata.Players.Length;
        LanPlaybackSeek(_lanPlaybackStart+(LanPlaybackSource.Duration-_lanPlaybackStart)*seeks[_lanPlaybackProbeStep++]);
        if(!_lanWorld.AtTargets() || _lanPlaybackCars.Values.Any(c=>!c.Puppet.Pos.IsFinite()) || LanSession.Current!=null)
            throw new InvalidDataException("LAN viewer isolation/pose check failed");
        if (LanPlaybackSource.Metadata.Protocol >= 12)
        {
            var grade=LanRace.Grade(LanPlaybackSource.Metadata.Fuel);
            int wing=grade switch { FuelGrade.Diesel=>3,FuelGrade.Rocket=>1,FuelGrade.Sputnik=>2,_=>0 };
            if (_lanPlaybackCars.Values.Any(c=>c.Vis.Wing!=wing || c.Vis.Damage.KitWorn!=(grade==FuelGrade.Sputnik) || c.Sound.FiveCylinder!=(grade==FuelGrade.Sputnik) || c.Puppet.P.DriveFront!=FuelGrades.DriveFrontFor(grade)))
                throw new InvalidDataException("LAN viewer did not restore the recorded fuel/body");
        }
        if(_lanPlaybackOwn is {HasDamage:true}) {
            var own=_lanPlaybackCars[LanPlaybackSource.Metadata.LocalId];
            if(own.Puppet.Pos.DistanceTo(_lanPlaybackOwn.Sample(_lanPlaybackTime).Origin)>.001f || !own.Vis.Damage.TapeDriven)
                throw new InvalidDataException("Original own-car pose/damage was not restored");
        }
        GD.Print($"LANREPLAYTEST: seek {_lanPlaybackTime:F2}, replicas {_lanWorld.ReplicaCount}, visible {_lanWorld.VisibleReplicaCount}, audible {_lanWorld.AudibleReplicaCount}, obstacles {LanPlaybackSource.Metadata.Obstacles}, effects {_lanPlaybackEffects.Count}, cars {_lanPlaybackLatest.Count}");
    }
    void LanPlaybackInput(InputEvent e)
    {
        if(_booting || e is not InputEventKey {Pressed:true,Echo:false} k) return;
        try {
            if(LanExportInput(k.Keycode)) return;
            switch(k.Keycode) {
                case Key.Escape:CCCPRacing.Audio.AudioMix.Quit(this);break;
                case Key.Space:_lanPlaybackPaused=!_lanPlaybackPaused;break;
                case Key.Left:LanPlaybackSeek(_lanPlaybackTime-5);break;
                case Key.Right:LanPlaybackSeek(_lanPlaybackTime+5);break;
                case Key.H:_lanPlaybackLabel.Visible=!_lanPlaybackLabel.Visible;break;
                case Key.Home:LanPlaybackSeek(0);break;
                case Key.L:_lanPlaybackLoop=!_lanPlaybackLoop;break;
                case Key.Tab:_lanPlaybackCar=(_lanPlaybackCar+1)%LanPlaybackSource.Metadata.Players.Length;LanPlaybackPose(0,true);break;
                case Key.Up:_lanPlaybackSpeed=Math.Min(2,_lanPlaybackSpeed*2);break;
                case Key.Down:_lanPlaybackSpeed=Math.Max(.25f,_lanPlaybackSpeed/2);break;
            }
        }catch(Exception ex){GD.PushError("LAN replay seek failed safely: "+ex.Message);CCCPRacing.Audio.AudioMix.Quit(this, 2);}
    }
}
