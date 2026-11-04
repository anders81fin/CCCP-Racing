using System;
using System.Linq;
using CCCPRacing.Audio;
using CCCPRacing.Car;
using CCCPRacing.Track;
using CCCPRacing.Net;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    void GroundEngineSoundTest()
    {
        var track=TrackBuilder.Build(TrackDef.Load("res://tracks/bonus.json"));
        var fx=new TyreEffects();AddChild(fx);fx.Init(track);
        var tanks=new TankBattle();AddChild(tanks);var tr=track.TankRange;
        tanks.Init(track,fx,new(tr.X0,tr.Z0,tr.X1-tr.X0,tr.Z1-tr.Z0),tr.Tanks);
        var btr=new BtrPatrols();AddChild(btr);btr.Init(track,fx);
        var kozlik=new KozlikPatrol();AddChild(kozlik);kozlik.Init(track,fx,null,null,0);
        DieselSound Engine(Node root)=>root.FindChildren("*","",true,false).OfType<DieselSound>().First();
        var tankSound=Engine(tanks);var btrSound=Engine(btr);var jeepSound=Engine(kozlik);
        Check(tankSound.Voice==DieselSound.EngineVoice.Tank && tankSound.UnitSize==65 && tankSound.MaxDistance==380,
            "tank engine: heavy idle voice remains spatially audible well beyond the firing position");
        Check(btrSound.Voice==DieselSound.EngineVoice.Armoured && btrSound.VolumeDb>=0 && btrSound.MaxDistance==260,
            "BTR engine: armoured diesel is no longer a quiet 75-metre source");
        Check(jeepSound.Voice==DieselSound.EngineVoice.Petrol && jeepSound.UnitSize==30 && jeepSound.MaxDistance==180,
            "bear's open-top Kozlik has a distinct audible petrol idle");
        var client=new Node3D();AddChild(client);
        var sender=new LanWorld(this);var receiver=new LanWorld(client);
        var obstacles=new System.Collections.Generic.List<Obstacle>();
        sender.Capture(new Node3D[]{tanks,btr,kozlik},obstacles,Array.Empty<int>(),(bytes,mesh)=> {
            if(mesh)receiver.MeshIn(bytes);else receiver.StateIn(bytes,obstacles);
        });
        var remote=client.FindChildren("*","",true,false).OfType<DieselSound>().ToArray();
        Check(new[]{tankSound,btrSound,jeepSound}.All(source=>remote.Any(r=>r.Voice==source.Voice && r.UnitSize==source.UnitSize
            && r.Reach==source.Reach && r.VolumeDb==source.VolumeDb && !r.Dozing && !r.StreamPaused)),
            "LAN preserves all three engine voices, idle levels, reference distances and ranges");
        tankSound.Dozing=true;
        sender.Capture(new Node3D[]{tanks},obstacles,Array.Empty<int>(),(bytes,mesh)=> {if(mesh)receiver.MeshIn(bytes);else receiver.StateIn(bytes,obstacles);});
        Check(remote.Any(r=>r.Voice==DieselSound.EngineVoice.Tank && r.Dozing),"LAN keeps engine voice and dozing as separate settings");
        sender.Free();receiver.Free();client.Free();kozlik.Free();btr.Free();tanks.Free();fx.Free();
    }
}
