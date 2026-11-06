using System;
using System.IO;
using System.Linq;
using CCCPRacing.Car;
using CCCPRacing.Net;
using CCCPRacing.Track;
using Godot;
namespace CCCPRacing.Tests;
public partial class PhysicsTest
{
    void LanFuelTest()
    {
        var track=TrackBuilder.Build(TrackDef.Load("res://tracks/ilyich.json"));
        var fx=new TyreEffects();AddChild(fx);
        foreach (int key in Enumerable.Range(1, LanRace.FuelCount))
        {
            int fuel=CCCPRacing.Race.Training.PercentForKey(key);
            var grade=(CCCPRacing.Race.FuelGrade)(key-1);
            var host=LanSession.BallotFixture("ilyich");host.StartBallot();
            host.Vote(fuel);host.CapacityVote(2,fuel);
            var client=new LanSession();client.OnPacket(1,host.RevealFixturePacket(true));
            Check(host.Reveal.Power==fuel && client.Reveal.Power==fuel && client.Reveal.Ballots.All(b=>b.pct==fuel), $"LAN fuel {grade}: actual ballot and reveal preserve the distinct fuel ID");
            var p=new CarParams();var car=new CarSim(p,track);LanRace.ConfigureCar(car,client.Reveal.Power);
            var solo=new CarParams();CCCPRacing.Race.FuelGrades.ApplyEngine(solo,grade);
            Check(p.Redline==solo.Redline && p.MaxTorque==solo.MaxTorque && p.FinalDrive==solo.FinalDrive && p.DriveFront==solo.DriveFront && p.CenterLock==solo.CenterLock && p.TurboKick==solo.TurboKick && car.PowerScale==CCCPRacing.Race.FuelGrades.PhysicsPercent(grade)/100f && car.TurboGain==CCCPRacing.Race.FuelGrades.TurboGainFor(grade), $"LAN fuel {grade}: authority and prediction use solo engine, turbo and drivetrain");
            var remote=new RemoteCar(2,"FUEL",1,1,p,track,this,fx);remote.ConfigureFuel(fuel);
            var visual=CarVisual.Create(solo,new Color(.7f,.2f,.1f));CCCPRacing.Race.FuelGrades.ApplyVisual(visual,grade);
            Check(remote.Vis.Wing==visual.Wing && remote.Vis.Damage.KitWorn==visual.Damage.KitWorn && remote.Sound.FiveCylinder==(grade==CCCPRacing.Race.FuelGrade.Sputnik) && remote.Sound.Grade==(int)(grade==CCCPRacing.Race.FuelGrade.Sputnik?CCCPRacing.Race.FuelGrade.Rocket:grade), $"LAN fuel {grade}: remote body and engine voice match the fuel");
            car.Reset(track.StartPos,track.StartYaw);
            for(int i=0;i<480;i++) {car.SetControls(1,0,0,false,true);car.Step(Dt);}
            if (car.TurboGain > 0) car.RestoreBoost(.72f); // exercise a loaded turbo, not an idle zero
            var corrected=new CarSim(p,track);LanRace.ConfigureCar(corrected,fuel);corrected.RestoreNetwork(car.NetworkState());
            bool exact=car.Boost==corrected.Boost;
            for(int i=0;i<120;i++) {
                car.SetControls(.7f,0,.1f,false,true);corrected.SetControls(.7f,0,.1f,false,true);
                car.Step(Dt);corrected.Step(Dt);exact &= car.NetworkState().SequenceEqual(corrected.NetworkState());
            }
            Check(exact,$"LAN fuel {grade}: turbo/integrator correction resumes without drift");
            var buffer=new SnapshotBuffer();var snap=Snapshot.Of(car,1,0,0,1,0,false);snap.State=car.NetworkState();buffer.Add(snap);
            float applied=-1;buffer.Apply(1,remote.Puppet,0,ref applied);
            Check(remote.Puppet.Boost==car.Boost,$"LAN fuel {grade}: recorded snapshot restores turbo presentation without live simulation");
            visual.Free();remote.Free();client.Free();host.Free();
        }
        Check(!LanRace.ValidFuel(175) && !LanRace.ValidFuel(0),"LAN fuel: arbitrary power values cannot enter a ballot");
        fx.QueueFree();
    }

    void LanAuthorityTest()
    {
        LanRulesTest();
        LanCanisterTest();
        LanWorldRoundtripTest();
        LanVisualsTest();
        LanPacketRecoveryTest();
        LanWreckVisualTest();
        LanIdleNeutralTest();
        LanHeadlightTest();
        LanContactPredictionTest();
        LanLeavePromptTest();
        LanTrackBallotTest();
        LanObstaclesTest();
        LanSessionScoresTest();
        LanCapacityTest();
        LanReplayAccessTest();
        LanHandbrakeStateTest();
        var p=new CarParams();var ground=new FlatGround();
        CarSim Make(Vector3 pos,Vector3 vel) {var c=new CarSim(p,ground);c.Reset(pos,0);c.Vel=vel;return c;}
        var a=Make(new Vector3(100,.6f,100),new Vector3(10,0,0));
        var b=Make(new Vector3(101.7f,.6f,100),new Vector3(-10,0,0));
        float momentum=(a.Vel+b.Vel).X;float energy=a.Vel.LengthSquared()+b.Vel.LengthSquared();
        float hit=CarContacts.Resolve(a,b);
        Check(hit>15 && (a.Vel-b.Vel).X<0,"LAN: head-on cars exchange impulses and separate");
        Check(Math.Abs((a.Vel+b.Vel).X-momentum)<.001f,"LAN: contacts conserve linear momentum");
        Check(a.Vel.LengthSquared()+b.Vel.LengthSquared()<=energy+.01f,"LAN: contact restitution does not add kinetic energy");
        a=Make(new Vector3(100,.6f,100),new Vector3(10,0,0));b=Make(new Vector3(101.7f,4,100),Vector3.Zero);
        Check(CarContacts.Resolve(a,b)==0 && a.Vel.X==10,"LAN: a jumping car clears a car beneath it");
        a=Make(new Vector3(100,.6f,100),new Vector3(10,0,0));b=Make(new Vector3(101.7f,.6f,102.2f),Vector3.Zero);
        CarContacts.Resolve(a,b);Check(Math.Abs(a.AngVel.Y)+Math.Abs(b.AngVel.Y)>.01f,"LAN: glancing contacts produce yaw");
        a=Make(new Vector3(100,.6f,100),Vector3.Zero);
        for(int i=0;i<240;i++){a.Clutch=i<20;a.SetControls(.9f,.05f,.6f,true,true);a.Step(Dt);}
        byte[] state=a.NetworkState();b=Make(Vector3.Zero,Vector3.Zero);b.RestoreNetwork(state);
        for(int i=0;i<120;i++) {a.SetControls(.7f,0,-.3f,false,true);b.SetControls(.7f,0,-.3f,false,true);a.Step(Dt);b.Step(Dt);}
        Check(a.Pos.DistanceTo(b.Pos)<.00001f && a.Vel.DistanceTo(b.Vel)<.00001f && Math.Abs(a.Rpm-b.Rpm)<.001f,"LAN: restored integrator replays the same controls without drift");
        var cmd=DriverCommand.Of(a,123,true);using var m=new MemoryStream();using(var w=new BinaryWriter(m,System.Text.Encoding.UTF8,true))cmd.Write(w);m.Position=0;
        var back=DriverCommand.Read(new BinaryReader(m));Check(back.Sequence==123&&back.Horn&&back.Steer==cmd.Steer,"LAN: sequenced driver intent survives the wire");
        var snap=Snapshot.Of(a,2,0,1,4,60,false);snap.State=a.NetworkState();snap.Ack=123;snap.Refuelling=true;snap.FuelLitres=12.3f;snap.Health=.4f;snap.Destroyed=true;snap.WreckLeft=8;snap.FinishTime=72;
        using var sm=new MemoryStream();using(var w=new BinaryWriter(sm,System.Text.Encoding.UTF8,true))snap.Write(w);sm.Position=0;
        var sb=Snapshot.Read(new BinaryReader(sm));Check(sb.Refuelling&&sb.FuelLitres==12.3f&&sb.Ack==123&&sb.Health==.4f&&sb.Destroyed&&sb.WreckLeft==8&&sb.FinishTime==72&&sb.State.SequenceEqual(snap.State),"LAN: authoritative state carries its input acknowledgement and health");
    }
    void LanContactPredictionTest()
    {
        CarSim Make(Vector3 pos,Vector3 vel) {var c=NewCar(new FlatGround());c.Reset(pos,0);c.Vel=vel;return c;}
        float Depth(CarSim a,CarSim b)
        {
            float depth=0;
            foreach(float za in CarSim.CollisionCircleZ) foreach(float zb in CarSim.CollisionCircleZ)
            {
                var d=a.Pos+a.Rot*new Vector3(0,0,za)-b.Pos-b.Rot*new Vector3(0,0,zb);d.Y=0;
                depth=Math.Max(depth,2*CarSim.CollisionRadius-d.Length());
            }
            return depth;
        }
        var a=Make(new(100,.8f,100),new(0,0,10));var b=Make(new(100,.8f,104.4f),new(0,0,-10));
        float hit=CarContacts.Resolve(a,b);
        Check(hit>15 && Depth(a,b)<.001f,"LAN contacts: frontal hulls separate fully in the collision tick");
        a=Make(new(100,.8f,100),new(10,0,0));b=Make(new(101.8f,.8f,100),new(-10,0,0));
        CarContacts.Resolve(a,b);
        Check(Depth(a,b)<.001f,"LAN contacts: side-by-side cars retain no soft penetration gap");
        var buffer=new SnapshotBuffer();
        var sample=new Snapshot {Time=1,Pos=new(100,.8f,104.4f)};sample.Frame.Vel=new(0,0,-10);buffer.Add(sample);
        var original=sample.Pos;var velocity=sample.Frame.Vel;
        var proxy=Make(Vector3.Zero,Vector3.Zero);a=Make(new(100,.8f,100),new(0,0,10));
        hit=buffer.PredictContact(1,a,proxy);
        Check(hit>15 && a.Vel.Z<0 && Depth(a,proxy)<.001f,
            "LAN prediction: joined car reacts and separates immediately before a host correction");
        Check(sample.Pos==original && sample.Frame.Vel==velocity && proxy.Pos==original,
            "LAN prediction: scratch contact never edits remote snapshots or moves the visible remote car");
        a=Make(new(100,.8f,100),new(0,0,10));var before=a.NetworkState();
        Check(buffer.PredictContact(1.2f,a,proxy)==0 && before.SequenceEqual(a.NetworkState()),
            "LAN prediction: stale remote data cannot create a phantom blocking car");
        Check(buffer.PredictContact(.9f,a,proxy)==0,"LAN prediction: no collision against snapshots from the future");
        buffer.Add(new Snapshot {Time=1.1f,Pos=new(120,.8f,100)});
        a=Make(new(100,.8f,100),new(0,0,10));
        Check(buffer.PredictContact(1,a,proxy)>15,"LAN prediction: input replay uses the historical contact, not the latest remote position");
        a=Make(new(100,.8f,100),new(0,0,10));
        Check(buffer.PredictContact(1.1f,a,proxy)==0,"LAN prediction: a later command does not replay an old contact");
        var timing=new SnapshotBuffer();var moving=new Snapshot {Time=2,Pos=new(100,.8f,100)};moving.Frame.Vel=new(20,0,0);timing.Add(moving);
        Check(Math.Abs(timing.PresentationTime(2.04f,new(100,.8f,100))-2.04f)<1e-5,
            "LAN presentation: close contact partner uses the local present, not a fixed 50 ms delay");
        Check(Math.Abs(timing.PresentationTime(2.04f,new(200,.8f,100))-1.99f)<1e-5,
            "LAN presentation: distant cars retain snapshot interpolation");
        Check(Math.Abs(timing.PresentationTime(2.2f,new(100,.8f,100))-2.15f)<1e-5,
            "LAN presentation: stale updates do not extrapolate contact partners indefinitely");
        a=Make(new(100,.8f,100),new(0,0,40));b=Make(new(100,.8f,104.4f),new(0,0,-40));
        var history=a.NetworkState();var remoteHistory=new SnapshotBuffer();remoteHistory.Add(Snapshot.Of(b,1,0,0,1,0,false));
        remoteHistory.PredictContact(1,a,proxy);var predicted=a.NetworkState();
        a.RestoreNetwork(history);remoteHistory.PredictContact(1,a,proxy);
        Check(a.NetworkState().SequenceEqual(predicted),"LAN prediction: restoring and replaying a contact applies its impulse once, with identical integrator state");
        a=Make(new(100,.8f,100),new(0,0,10));var overhead=new SnapshotBuffer();overhead.Add(new Snapshot {Time=1,Pos=new(100,4,100)});
        Check(overhead.PredictContact(1,a,proxy)==0,"LAN prediction: airborne cars still clear a car below");
        Check(CarContacts.Steps(1f/120,100)>2 && CarContacts.Steps(1f/120,10000)==16,
            "LAN contacts: both prediction and host bound high-speed relative travel with extra substeps");
        a=Make(new(100,.8f,100),new(0,0,80));b=Make(new(100,.8f,108),new(0,0,-80));
        hit=0;int steps=CarContacts.Steps(.12f,80);
        for(int i=0;i<steps;i++) {a.Step(.12f/steps);b.Step(.12f/steps);hit=Math.Max(hit,CarContacts.Resolve(a,b));}
        Check(hit>100 && a.Pos.Z<b.Pos.Z && Depth(a,b)<.001f,
            "LAN contacts: fast opposing cars hit before crossing through each other");
        a=Make(new(100,.8f,100),new(0,0,10));var miss=new SnapshotBuffer();
        miss.Add(new Snapshot {Time=1,Pos=new(101.86f,.8f,100)});before=a.NetworkState();
        Check(miss.PredictContact(1,a,proxy)==0 && before.SequenceEqual(a.NetworkState()),
            "LAN prediction: a genuine narrow pass is not turned into an early collision");
    }

    void LanHeadlightTest()
    {
        var track=TrackBuilder.Build(TrackDef.Load("res://tracks/ilyich.json"));
        var fx=new TyreEffects();AddChild(fx);fx.Init(track);
        var owner=CarVisual.Create(new CarParams(),new Color(.7f,.13f,.1f));AddChild(owner);owner.Damage.Enabled=true;
        var car=new CarSim(new CarParams(),track);car.Reset(track.StartPos,track.StartYaw);
        owner.Damage.ShootHeadlamp(car,fx);
        byte broken=(byte)((owner.Damage.HeadlightWorks(true)?0:1)|(owner.Damage.HeadlightWorks(false)?0:2));
        Check(broken==1,"LAN headlights: actual headlamp damage supplies the left-only owner report");
        var command=DriverCommand.Of(car,1,false);command.BrokenHeadlights=broken;command.RepairSerial=2;
        using var m=new MemoryStream();using(var w=new BinaryWriter(m,System.Text.Encoding.UTF8,true))command.Write(w);m.Position=0;
        var report=DriverCommand.Read(new BinaryReader(m));
        Check(report.BrokenHeadlights==1 && report.RepairSerial==2,"LAN headlights: owner report and repair generation survive input wire");
        Check(report.HeadlightsAfter(0,3,true)==0 && report.HeadlightsAfter(2,3,true)==2,
            "LAN headlights: old repair-generation reports cannot break repaired lamps or overwrite current state");
        Check(report.HeadlightsAfter(3,2,false)==0,"LAN headlights: damage OFF suppresses reported lamp failures");
        var remote=new RemoteCar(2,"LAMPS",1,1,new CarParams(),track,this,fx);
        Snapshot Wire(Snapshot s)
        {
            using var stream=new MemoryStream();using(var w=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))s.Write(w);
            stream.Position=0;return Snapshot.Read(new BinaryReader(stream));
        }
        int bulbs() => remote.Lights.GetChildren().OfType<MeshInstance3D>().Count(n=>n.Position.Z<0 && n.Visible);
        var beam=remote.Lights.GetChildren().OfType<SpotLight3D>().Single();
        var first=Wire(new Snapshot {Time=1,BrokenHeadlights=report.HeadlightsAfter(0,2,true),RepairSerial=2,Refuelling=true});
        Check(first.BrokenHeadlights==1 && first.Refuelling,"LAN headlights: snapshot preserves lamp bits beside existing flags");
        remote.Buffer.Add(first);remote.UpdateLights(1,1);
        Check(bulbs()==1 && beam.Visible && beam.Position.X>0,"LAN headlights: observer shows only the right bulb and moves the combined beam right");
        remote.Buffer.Add(Wire(new Snapshot {Time=3,BrokenHeadlights=3,RepairSerial=2})); // time 2 was lost
        remote.UpdateLights(1,1);
        Check(bulbs()==0 && !beam.Visible,"LAN headlights: next full snapshot recovers a missed failure and extinguishes both lamps and beam");
        remote.Buffer.Add(Wire(new Snapshot {Time=2,BrokenHeadlights=0,RepairSerial=2}));remote.UpdateLights(1,1);
        Check(bulbs()==0 && !beam.Visible,"LAN headlights: out-of-order old healthy snapshot cannot relight broken lamps");
        remote.Buffer.Add(Wire(new Snapshot {Time=4,BrokenHeadlights=0,RepairSerial=3}));remote.TickEngine(0);remote.UpdateLights(1,1);
        Check(bulbs()==2 && beam.Visible && Math.Abs(beam.Position.X)<.001f,"LAN headlights: repaired/replacement car restores both observer lamps and centered beam");
        remote.Buffer.Add(Wire(new Snapshot {Time=5,BrokenHeadlights=2,RepairSerial=3}));remote.UpdateLights(1,1);
        Check(bulbs()==1 && beam.Visible && beam.Position.X<0,"LAN headlights: right-only failure leaves the left bulb and beam working");
        remote.UpdateLights(1,0);
        Check(bulbs()==0 && !beam.Visible,"LAN headlights: daylight still switches off remote lamps");
        remote.Free();owner.QueueFree();fx.QueueFree();
    }

    void LanIdleNeutralTest()
    {
        CarSim Idle()
        {
            var c=NewCar(new FlatGround());
            for(int i=0;i<240;i++) {c.SetControls(false,false,0,false);c.Step(Dt);}
            return c;
        }
        var idle=Idle();
        Check(idle.Neutral,"LAN neutral fixture: stopped automatic gearbox rests in N");
        var restored=Idle();restored.RestoreNetwork(idle.NetworkState());
        restored.SetControls(true,false,0,false);
        for(int i=0;i<240;i++) restored.Step(Dt);
        GD.Print($"  LAN idle restore: neutral {restored.Neutral}, explicit {restored.ExplicitNeutral}, speed {restored.Speed:F2}, gear {restored.Gear}");
        Check(!restored.Neutral && restored.Speed>1,"LAN: authoritative idle correction can accelerate out of N");
        var joined=Idle();
        var host=Idle();
        DriverCommand Wire(DriverCommand c)
        {
            using var m=new MemoryStream();
            using(var w=new BinaryWriter(m,System.Text.Encoding.UTF8,true))c.Write(w);
            m.Position=0;return DriverCommand.Read(new BinaryReader(m));
        }
        for(uint i=0;i<240;i++)
        {
            joined.SetControls(false,false,0,false);
            Wire(DriverCommand.Of(joined,i,false)).Apply(host);host.Step(Dt);
            joined.RestoreNetwork(host.NetworkState());
        }
        for(uint i=240;i<720;i++)
        {
            joined.SetControls(true,false,0,false);
            var command=Wire(DriverCommand.Of(joined,i,false));
            command.Apply(host);host.Step(Dt);
            joined.RestoreNetwork(host.NetworkState());
        }
        GD.Print($"  LAN joined idle: neutral {host.Neutral}, explicit {host.ExplicitNeutral}, speed {host.Speed:F2}, gear {host.Gear}");
        Check(!host.Neutral && host.Speed>5 && host.Gear>1,"LAN: joined driver launches and shifts after repeated idle input/correction round trips");
        for(int i=0;i<1440;i++) {host.SetControls(false,false,0,true);host.Brake=1;host.Step(Dt);}
        joined.RestoreNetwork(host.NetworkState());
        Check(joined.Neutral && !joined.ExplicitNeutral,"LAN: correction preserves automatic N after stopping");
        for(uint i=722;i<1202;i++)
        {
            joined.SetControls(1f,0f,0,false,true);
            Wire(DriverCommand.Of(joined,i,false)).Apply(host);host.Step(Dt);
            joined.RestoreNetwork(host.NetworkState());
        }
        Check(!host.Neutral && host.Speed>5,"LAN: controller throttle restarts a stopped joined car after corrections");
        var reverse=Idle();reverse.RestoreNetwork(Idle().NetworkState());
        reverse.SetControls(0f,1f,0,false,true);
        var reversing=Idle();Wire(DriverCommand.Of(reverse,1203,false)).Apply(reversing);
        for(int i=0;i<240;i++) reversing.Step(Dt);
        Check(!reversing.Neutral && reversing.Gear<0 && reversing.ForwardSpeed< -1,"LAN: controller reverse from idle N reaches the authoritative car");
        host.Neutral=true;joined.RestoreNetwork(host.NetworkState());
        joined.SetControls(true,false,0,false);
        Wire(DriverCommand.Of(joined,721,false)).Apply(host);
        Check(host.Neutral && joined.Neutral,"LAN: explicit service/start neutral survives correction and throttle intent");
        host.Neutral=false;host.Clutch=true;host.SetControls(true,false,0,false);
        joined.RestoreNetwork(host.NetworkState());
        Check(joined.Clutch && joined.Disengaged,"LAN: authoritative correction preserves held clutch independently of neutral");
    }
    void LanRulesTest()
    {
        float[] light={0,2,4,6,10,12};
        bool stages=true;
        for(int lap=0;lap<light.Length;lap++) stages &= Math.Abs(CCCPRacing.Race.RaceScene.LanLightClock(lap,0)-light[lap])<1e-5f;
        Check(stages && Math.Abs(CCCPRacing.Race.RaceScene.LanLightClock(3.5f,0)-8)<1e-5f,
            "LAN lighting: the leader advances through day, sunset, dusk, night and dawn in five laps");
        var p = new CarParams();
        var car = new CarSim(p, new FlatGround());
        car.Reset(new Vector3(0, .6f, 0), 0);
        car.Throttle = 1; car.Rpm = p.Redline;
        foreach (bool damage in new[] { false, true })
        foreach (bool fuel in new[] { false, true })
        {
            var health = new EngineHealth(); health.Reset(damage);
            var tank = new FuelTank(p) { Enabled = fuel };
            float before = tank.Litres, wait = 0;
            health.Impact(30, 1f/120);
            LanRace.TickFuel(tank, car, false, false, 1, ref wait);
            Check((health.Health < 1) == damage && (tank.Litres < before) == fuel,
                $"LAN rules: damage {damage}, consumption {fuel} operate independently");
        }
        var supply = new FuelTank(p) { Enabled = true };
        supply.Refill(.2f); float dryWait = 0, start = supply.Litres;
        LanRace.TickFuel(supply, car, false, true, 1, ref dryWait);
        Check(supply.Litres > start, "LAN fuel: depot supplies fuel while the race continues");
        supply.Refill(0);
        LanRace.TickFuel(supply, car, false, false, 9, ref dryWait);
        Check(supply.Empty, "LAN fuel: emergency supply cannot refill before ten stopped seconds");
        LanRace.TickFuel(supply, car, false, false, 1, ref dryWait);
        Check(supply.Fraction == 1 && dryWait == 0, "LAN fuel: ten stopped seconds revive an empty tank");
        supply.Refill(0); dryWait = 9; car.Vel = Vector3.Forward * 5;
        LanRace.TickFuel(supply, car, false, false, 1, ref dryWait);
        Check(supply.Empty && dryWait == 0, "LAN fuel: moving resets the emergency supply wait");
    }

    void LanCanisterTest()
    {
        var track=TrackBuilder.Build(TrackDef.Load("res://tracks/bonus.json"));
        var people=new Troopers();AddChild(people);people.Init(track,4);people.Canisters=true;
        var p=new CarParams();
        var first=new CarSim(p,track);first.Reset(new Vector3(-500,.6f,-500),0);
        var second=new CarSim(p,track);
        people.AimPass(second,1,60,50f/3.6f,10f,2f);
        CarSim receiver=null;
        people.CanCaughtForCar+=(car,_)=>receiver=car;
        for(int i=0;i<360 && receiver==null;i++)
        {
            second.SetControls(true,false,0f,false);
            second.Step(Dt);
            people.Tick(Dt,first,0,null,false,new[] {first,second});
        }
        if(receiver==null) GD.Print($"  LAN canister fixture: car {second.Pos}, trooper {people.WalkerPosition(1)}, speed {second.Speed:F1}");
        Check(receiver==second,"LAN fuel: a thrown can follows and credits its intended car, not the host car");
        people.QueueFree();
    }

    void LanWorldRoundtripTest()
    {
        var host=new Node3D();var client=new Node3D();AddChild(host);AddChild(client);
        var actor=new Node3D();host.AddChild(actor);
        var mesh=new MeshInstance3D {Mesh=new BoxMesh {Size=new Vector3(2,1,3)},Position=new Vector3(12,2,18)};
        actor.AddChild(mesh);
        var sender=new LanWorld(host);var receiver=new LanWorld(client);
        var hostObstacles=new System.Collections.Generic.List<CCCPRacing.Track.Obstacle> {new(new Vector2(12,18),2,new Vector2(3,4))};
        var clientObstacles=new System.Collections.Generic.List<CCCPRacing.Track.Obstacle> {new(Vector2.Zero,.1f)};
        void Deliver(byte[] bytes,bool prototype) {if(prototype) receiver.MeshIn(bytes);else receiver.StateIn(bytes,clientObstacles);}
        sender.Capture(new[]{actor},hostObstacles,new[]{0},Deliver);
        var root=(Node3D)client.GetChild(0);var replica=(MeshInstance3D)root.GetChild(0);
        Check(receiver.ReplicaCount==1 && replica.GlobalPosition.DistanceTo(mesh.GlobalPosition)<.001f && replica.Mesh.GetSurfaceCount()==1,"LAN: procedural actor geometry and pose survive world replication");
        Check(clientObstacles[0]==hostObstacles[0],"LAN: moving obstacle position, size and velocity are shared");
        // Drop a complete world update. A later full state must recover without a delta dependency.
        mesh.Position+=Vector3.Right*5;
        sender.Capture(new[]{actor},hostObstacles,new[]{0},(_,_)=>{});
        mesh.Position+=Vector3.Right*3;mesh.Visible=false;
        sender.Capture(new[]{actor},hostObstacles,new[]{0},Deliver);receiver.Frame(1);
        Check(!replica.Visible && replica.GlobalPosition.DistanceTo(mesh.GlobalPosition)<.001f,"LAN: next world state recovers a lost update and shares visibility");
        sender.Free();receiver.Free();host.QueueFree();client.QueueFree();
    }

    void LanPacketRecoveryTest()
    {
        var net=new LanSession();byte[] received=null;int delivered=0;
        net.WorldIn+=bytes=>{received=bytes;delivered++;};
        var source=Enumerable.Range(0,2173).Select(i=>(byte)(i*37)).ToArray();
        var parity=new byte[1000];for(int i=0;i<source.Length;i++)parity[i%1000]^=source[i];
        void Part(uint frame,int part) {
            using var m=new MemoryStream();using(var w=new BinaryWriter(m,System.Text.Encoding.UTF8,true)) {
                w.Write(frame);w.Write((ushort)part);w.Write((ushort)3);w.Write(source.Length);
                byte[] payload=part==3?parity:source.Skip(part*1000).Take(1000).ToArray();
                w.Write((ushort)payload.Length);w.Write(payload);
            }
            m.Position=0;net.ReadWorldPart(new BinaryReader(m));
        }
        Part(10,3);Part(10,2);Part(10,0); // parity arrives first, middle fragment is lost
        Check(delivered==1 && received.SequenceEqual(source),"LAN: parity recovers a missing fragment despite packet reordering");
        Part(10,1);Part(9,0);Part(9,1);Part(9,2);
        Check(delivered==1,"LAN: duplicate and older world frames cannot rewind the world");
        Part(11,0);Part(11,3); // two data fragments lost: wait for the next frame
        Part(12,0);Part(12,1);Part(12,2);
        Check(delivered==2 && received.SequenceEqual(source),"LAN: unrecoverable loss is replaced by the next complete world frame");
        // A relayed client's reject message must have no effect on this client session.
        bool closed=false;net.Closed+=_=>closed=true;
        using var rejected=new MemoryStream();using(var w=new BinaryWriter(rejected,System.Text.Encoding.UTF8,true)){w.Write((byte)2);w.Write("spoof");}
        net.OnPacket(99,rejected.ToArray());
        Check(!closed,"LAN: client lifecycle packets are accepted only from the host");
        string required=null;net.TrackRequired+=track=>required=track;
        using var redirect=new MemoryStream();
        using(var w=new BinaryWriter(redirect,System.Text.Encoding.UTF8,true)){w.Write((byte)21);w.Write("siberia");}
        net.OnPacket(99,redirect.ToArray());
        Check(required==null,"LAN: another client cannot redirect our track");
        net.OnPacket(1,redirect.ToArray());
        Check(required=="siberia","LAN: the host supplies the track before lobby admission");
        net.Free();
    }

    void LanWreckVisualTest()
    {
        var track=CCCPRacing.Track.TrackBuilder.Build(CCCPRacing.Track.TrackDef.Load("res://tracks/bonus.json"));
        var timer=new CCCPRacing.Race.LapTimer(track);timer.Start();
        for(int i=0;i<track.Center.Length-1;i++) timer.Update(1f/120,track.Center[i]);
        timer.ResetMovementHistory();timer.Update(1f/120,track.Center[track.Center.Length-37]);
        Check(timer.LapsDone==0,"LAN: rescue teleport near the finish cannot award a lap");
        var fx=new TyreEffects();AddChild(fx);fx.Init(track);
        var remote=new RemoteCar(2,"TEST",1,1,new CarParams(),track,this,fx);
        remote.Puppet.Reset(track.StartPos,track.StartYaw);
        remote.Buffer.Add(new Snapshot {Time=0,Health=0,Destroyed=true,WreckLeft=10});
        remote.TickEngine(1f/120);remote.UpdateSound(track.StartPos,1,false);
        Check(remote.Health.Destroyed && remote.Sound.EngineLevel==0,"LAN: a replicated wreck starts its visual effects and silences its engine");
        remote.Buffer.Add(new Snapshot {Time=1,Health=1});
        remote.TickEngine(1f/120);remote.UpdateSound(track.StartPos,1,false);
        Check(!remote.Health.Dead && !remote.Health.Destroyed && remote.Sound.EngineLevel==1,"LAN: replicated replacement clears the wreck and restores its engine");
        remote.Free();fx.QueueFree();
    }

}
