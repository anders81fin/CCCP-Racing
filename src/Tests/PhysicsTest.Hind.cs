using System;
using CCCPRacing.Car;
using CCCPRacing.Race;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    void MilitaryNightLightsTest()
    {
        float previous=NightGlow.Level;NightGlow.Level=.6f;
        var model=new Node3D {Position=new(10,2,20)};AddChild(model);
        var lights=MilitaryLights.Vehicle(model,new(0,1,-3),new(0,1,3),1);
        model.AddChild(NightGlow.Part(Props.Ball(.2f,6),Colors.Red,Colors.Red,3,Vector3.Up*3));
        int VisibleLamps() {int n=0;foreach(Node child in lights.GetChildren()) if(child is MeshInstance3D m && m.Visible) n++;return n;}
        SpotLight3D beam=null;foreach(Node child in lights.GetChildren()) if(child is SpotLight3D s) beam=s;
        lights.UpdateLights(0);Check(VisibleLamps()==0 && !beam.Visible,"military lights are off in daylight");
        lights.UpdateLights(1);Check(VisibleLamps()==4 && beam.Visible && beam.LightEnergy==4 && !beam.ShadowEnabled,
            "night brings front and tail lamps plus a shadowless terrain-lighting beam");
        lights.BeamAllowed=false;lights.UpdateLights(1);
        Check(VisibleLamps()==4 && !beam.Visible,"a Hind can keep navigation lamps while its landing beam is off");
        lights.BeamAllowed=true;lights.UpdateLights(1);
        var client=new Node3D();AddChild(client);
        var sender=new CCCPRacing.Net.LanWorld(this);var receiver=new CCCPRacing.Net.LanWorld(client);
        var obstacles=new System.Collections.Generic.List<Obstacle>();
        void Deliver(byte[] bytes,bool prototype) {if(prototype) receiver.MeshIn(bytes);else receiver.StateIn(bytes,obstacles);}
        sender.Capture(new[]{model},obstacles,System.Array.Empty<int>(),Deliver);
        var replicas=client.GetChild<Node3D>(0);SpotLight3D remote=null;bool keep=false,emission=false;
        foreach(Node node in replicas.GetChildren()) {
            if(node is SpotLight3D s) remote=s;
            if(node is MeshInstance3D m) {
                var mat=(StandardMaterial3D)m.Mesh.SurfaceGetMaterial(0);
                keep |= mat.HasMeta(NightGlow.KeepMeta);
                emission |= mat.EmissionEnabled && MathF.Abs(mat.EmissionEnergyMultiplier-1.8f)<.001f;
            }
        }
        Check(receiver.ReplicaLightCount==1 && remote!=null && remote.Visible && remote.LightEnergy==beam.LightEnergy
            && remote.GlobalPosition.DistanceTo(beam.GlobalPosition)<.001f && keep && emission,
            "LAN preserves real beams, glowing materials and night-visible lamp colours");
        lights.Position+=Vector3.Right*2;sender.Capture(new[]{model},obstacles,System.Array.Empty<int>(),Deliver);receiver.Frame(1);
        Check(remote.GlobalPosition.DistanceTo(beam.GlobalPosition)<.001f,"replicated headlights follow their moving hull");
        model.Visible=false;sender.Capture(new[]{model},obstacles,System.Array.Empty<int>(),Deliver);
        Check(!remote.Visible,"hidden or removed military actors leave no LAN light behind");
        lights.Powered=false;lights.UpdateLights(1);Check(VisibleLamps()==0 && !beam.Visible,"wrecked Hinds switch their lamps off");
        sender.Free();receiver.Free();client.QueueFree();model.QueueFree();NightGlow.Level=previous;
    }

    void BtrPatrolTest()
    {
        var track=TrackBuilder.Build(TrackDef.Load("res://tracks/bonus.json"));
        int slots=track.Obstacles.Count;
        var patrols=new BtrPatrols();AddChild(patrols);patrols.Init(track,null);
        Check(patrols.Count==2 && track.Obstacles.Count==slots+6,"two BTR patrols own three moving hull circles each");
        bool outside=true,moving=false,grounded=true,crossed=false,clearRoute=true;
        foreach(var route in patrols.Routes)
            foreach(var point in route) {
                outside &= !BonusAirbase.Protected(point,2);
                for(int k=0;k<slots;k++) if(point.DistanceTo(track.Obstacles[k].Center)<track.Obstacles[k].Radius+2f) clearRoute=false;
            }
        for(int frame=0;frame<900*30;frame++)
        {
            patrols.Tick(1f/30);
            foreach(Vector3 at in patrols.Positions) {
                int road=track.NearestCenter(new(at.X,at.Z),out float distance);
                crossed |= distance<track.HalfWidth[road]+1;
                outside &= !BonusAirbase.Protected(new(at.X,at.Z),2);
                grounded &= MathF.Abs(at.Y-track.MeshHeight(at.X,at.Z))<.01f;
            }
            for(int k=slots;k<track.Obstacles.Count;k++) moving |= track.Obstacles[k].Velocity.Length()>2;
        }
        Check(outside && clearRoute && grounded && moving,"BTRs follow terrain on wide loops that stay clear of the camp and static obstacles");
        Check(crossed,"BTRs cross the race road into the southern field");
        Check(patrols.Bursts>0,"BTRs stop at ruined barns and fire cosmetic bursts now and then");
        var client=new Node3D();AddChild(client);
        var sender=new CCCPRacing.Net.LanWorld(this);var receiver=new CCCPRacing.Net.LanWorld(client);
        var replicaObstacles=new System.Collections.Generic.List<Obstacle>(track.Obstacles);
        var owned=new[]{slots,slots+1,slots+2,slots+3,slots+4,slots+5};
        foreach(int i in owned) replicaObstacles[i]=new Obstacle(Vector2.Zero,.1f);
        sender.Capture(new[]{patrols},track.Obstacles,owned,(bytes,prototype)=> {
            if(prototype) receiver.MeshIn(bytes);else receiver.StateIn(bytes,replicaObstacles);
        });
        bool replicated=true;foreach(int i in owned) replicated &= replicaObstacles[i]==track.Obstacles[i];
        Check(replicated && receiver.ReplicaCount>=28 && receiver.ReplicaCount<=40 && receiver.ReplicaLightCount==2,
            "LAN shares both complete BTRs and their moving hulls with a small number of mesh poses");
        sender.Free();receiver.Free();client.QueueFree();
        patrols.Reset();Check(track.Obstacles.Count==slots+6,"BTR restart preserves its collision slots");patrols.Free();
    }

    void CampGateTest()
    {
        var track=TrackBuilder.Build(TrackDef.Load("res://tracks/bonus.json"));
        int slots=track.Obstacles.Count;
        var gate=new CampGate();AddChild(gate);gate.Init(track);
        int circles=track.Obstacles.Count-slots;
        var car=new CCCPRacing.Car.CarSim(new CCCPRacing.Car.CarParams(),new FlatGround());
        car.Reset(new Vector3(136,.6f,124.7f+9),0);
        bool closed=gate.Now==CampGate.State.Closed && track.Obstacles[slots].Center.X>0;
        for(int i=0;i<60;i++) gate.Tick(1f/30,car,false);
        bool silent=gate.Now==CampGate.State.Closed;
        gate.Tick(1f/30,car,true);
        bool opening=gate.Now==CampGate.State.Opening;
        for(int i=0;i<90;i++) gate.Tick(1f/30,car,false);
        bool open=gate.Now==CampGate.State.Open && track.Obstacles[slots].Center.X<-1000;
        car.Reset(new Vector3(136,.6f,124.7f+60),0);
        for(int i=0;i<30*14;i++) gate.Tick(1f/30,car,false);
        bool closes=gate.Now==CampGate.State.Closed && track.Obstacles[slots].Center.X>0;
        // 8 November: the guard hears the horn from the hut's earshot while the car still rolls; far away he does not.
        car.Reset(new Vector3(136,.6f,124.7f+60),0);car.Vel=new Vector3(0,0,-20);
        gate.Tick(1f/30,car,true);
        bool deaf=gate.Now==CampGate.State.Closed;
        car.Reset(new Vector3(136,.6f,124.7f+40),0);car.Vel=new Vector3(0,0,-20);
        gate.Tick(1f/30,car,true);
        bool hears=gate.Now==CampGate.State.Opening;
        for(int i=0;i<30*14;i++) gate.Tick(1f/30,car,false);
        gate.Reset();
        var knocks=new System.Collections.Generic.List<(int,System.Func<Vector3,bool>)>(gate.Knocks());
        bool broke=knocks[0].Item2(new Vector3(0,0,-14)) && gate.Now==CampGate.State.Broken && track.Obstacles[slots].Center.X<-1000;
        gate.Reset();
        bool restored=gate.Now==CampGate.State.Closed && track.Obstacles[slots].Center.X>0;
        Check(circles>=8 && closed && silent,"the camp barrier starts closed with a solid row across the gate mouth, and a quiet car does not open it");
        Check(opening && open && closes,"stopping at the gate and honking raises the barrier; it comes down again once the car has gone");
        Check(deaf && hears,"the guard opens the barrier for a horn heard within 45 m even from a moving car, and not from farther away");
        Check(broke && restored,"a hard hit breaks the barrier and drives on through; a restart puts it back");
        gate.Free();
    }

    void PoligonTest()
    {
        var definition=TrackDef.Load("res://tracks/bonus.json");
        var track=TrackBuilder.Build(definition);
        int pads=definition.Pads.Count,props=definition.Props.Count;
        BonusCampLayout.Apply(definition);
        Check(definition.Pads.Count==pads && definition.Props.Count==props && definition.Name==BonusCampLayout.Name,
            "the shared Poligon camp layout is idempotent and owns the visible name");
        int widened=0,transitions=0;bool smooth=true;
        for(int i=0;i<track.Center.Length;i++)
        {
            if(track.HalfWidth[i]>5.5f) widened++;
            if(track.HalfWidth[i]>5.5f && track.HalfWidth[(i+1)%track.Center.Length]<=5.5f) transitions++;
            smooth &= MathF.Abs(track.HalfWidth[i]-track.HalfWidth[(i+1)%track.Center.Length])<.2f;
        }
        Check(widened>60 && transitions==2 && smooth,"two passing places have smooth tapers and leave the remaining road narrow");
        var guides=BonusRouteGuides.Build(track);
        Check(guides.GetChildCount()>100,"road reflectors and bend chevrons mark the driving corridor");guides.Free();
        var exercise=new BonusExerciseControl();
        exercise.Tick(14);Check(exercise.GroundAllowed && !exercise.ReserveAir(1),"ground exercise has its own time window");
        exercise.Tick(30);exercise.GroundBusy(true);
        Check(!exercise.ReserveAir(1),"aircraft wait for committed ground hazards to clear");
        exercise.Tick(4);Check(exercise.ReserveAir(1) && !exercise.ReserveAir(2) && !exercise.GroundAllowed,"only one Hind can reserve a firing run");
        exercise.EndAir(1);Check(!exercise.ReserveAir(2),"a quiet interval follows a firing run");
        exercise.Tick(8);Check(exercise.ReserveAir(2),"the next aircraft can enter after recovery time");
        exercise.Reset();Check(!exercise.AirActive && exercise.Clock==0,"restarting clears exercise reservations");
        Check(HindSorties.BurstOffset(0)<=-50 && HindSorties.BurstOffset(HindSorties.BurstRounds-1)>=50
            && HindSorties.BurstRounds*HindSorties.BurstInterval>=4.5f,"a long Hind burst sweeps open ground on both sides of the road");
        bool fuels=Training.Count==3;
        for(int k=1;k<=Training.Count;k++) fuels &= Training.PercentForKey(k)==FuelGrades.Percent[k-1]
            && Training.FuelName(Training.PercentForKey(k))==FuelGrades.Names[k-1];
        Check(fuels && !Training.Valid(75) && !Training.Valid(500),"LAN and Stakhanov offer exactly the three fuel powers");
    }

    void BonusPerimeterTest()
    {
        var definition=TrackDef.Load("res://tracks/bonus.json");
        var track=TrackBuilder.Build(definition);
        Vector2 anchor=new(136,104);
        bool clear=true,level=true,continuous=true,serviceClear=true;
        float minNormal=1,maxBerm=0;Vector2 steep=default;
        foreach(var (from,to) in BonusFieldCamp.PerimeterWalls())
        {
            int samples=(int)Math.Ceiling(from.DistanceTo(to)*4);
            for(int i=0;i<=samples;i++)
            {
                Vector2 p=anchor+from.Lerp(to,i/(float)samples);
                int road=track.NearestCenter(p,out float distance);
                // The gate mouth itself opens onto the road verge by design; the rest of the boundary keeps its clearance.
                if(p.DistanceTo(anchor+new Vector2(0,BonusFieldCamp.GateZ))>BonusFieldCamp.GateHalfWidth+12) { clear &= distance-track.HalfWidth[road]-1.3f>=3; }
                serviceClear &= BonusAirbase.ServiceDistance(p)>=3;
                track.MeshHeight(p.X,p.Y,out Vector3 normal);
                if(normal.Y<minNormal) {minNormal=normal.Y;steep=p;}
                maxBerm=Math.Max(maxBerm,BonusAirbase.BermHeight(p));
                level &= normal.Y>=.85f && BonusAirbase.BermHeight(p)<.15f;
            }
        }
        Vector2? previous=null;
        foreach(var bag in BonusFieldCamp.PerimeterBags()) if(bag.Row==0)
        {
            if(previous.HasValue) continuous &= previous.Value.DistanceTo(bag.Center)<=2.15f;
            previous=bag.Center;
        }
        Check(clear,"the complete camp boundary leaves at least three metres of clear road verge");
        if(!level) GD.Print($"Perimeter ground: minimum normal Y={minNormal:F3} at {steep}, maximum berm height={maxBerm:F3}");
        Check(level,"the complete camp boundary avoids cliffs and raised earth banks");
        Check(continuous,"bags remain connected on both gate approaches and around the northern camp");
        Check(serviceClear,"the full bag footprint stays outside the camp service road");
        var polygon=new System.Collections.Generic.List<Vector2>();
        foreach(var wall in BonusFieldCamp.PerimeterWalls()) polygon.Add(wall.From);
        polygon.Add(new Vector2(BonusFieldCamp.GateHalfWidth,BonusFieldCamp.GateZ));
        bool Inside(Vector2 p)
        {
            p-=anchor;bool inside=false;
            for(int i=0,j=polygon.Count-1;i<polygon.Count;j=i++)
                if((polygon[i].Y>p.Y)!=(polygon[j].Y>p.Y) && p.X<(polygon[j].X-polygon[i].X)*(p.Y-polygon[i].Y)/(polygon[j].Y-polygon[i].Y)+polygon[i].X)
                    inside=!inside;
            return inside;
        }
        bool equipmentInside=true,towersInside=true;
        foreach(var prop in track.Props)
        {
            Vector2 half=prop.Type switch {"fieldfuel"=>new(3.6f,3.5f),"fieldgenerator"=>new(1.4f,1.15f),
                "fieldtruck"=>new(1.4f,4.3f),"fieldtent"=>new(3.1f,4.4f),"helipad"=>new(11,12),
                "powercabinet" when prop.Text=="field distribution"=>new(.65f,.65f),_=>Vector2.Zero};
            if(half==Vector2.Zero) continue;
            foreach(float x in new[]{-half.X,half.X}) foreach(float z in new[]{-half.Y,half.Y})
            {
                Vector3 point=prop.Position+new Basis(Vector3.Up,prop.Rotation)*new Vector3(x,0,z);
                equipmentInside &= Inside(new Vector2(point.X,point.Z));
            }
        }
        foreach(Vector2 tower in BonusFieldCamp.PerimeterTowers)
            foreach(float x in new[]{-1.4f*BonusFieldCamp.TowerScale,1.4f*BonusFieldCamp.TowerScale}) foreach(float z in new[]{-1.4f*BonusFieldCamp.TowerScale,1.4f*BonusFieldCamp.TowerScale})
                towersInside &= Inside(anchor+tower+new Vector2(x,z));
        Check(equipmentInside,"all landing pads and field-camp equipment fit completely inside the fence");
        Check(towersInside,"all watchtower footprints lie inside the fence");
        bool yardsInside=true;
        foreach(var pad in definition.Pads)
            if(pad.Surface=="dirt")
                foreach(float x in new[]{pad.X0,pad.X1}) foreach(float z in new[]{pad.Z0,pad.Z1})
                    yardsInside &= Inside(new Vector2(x,z));
        Check(yardsInside,"both complete rectangular dirt yards lie inside the camp boundary");
        int gai=(int)definition.SpeedTrap.At;
        Vector2 line=track.Center[gai];
        Check(line.X<60 && line.Y>220 && line.Y<240 && track.CenterDir(gai).Y>0,
            "GAI measures the southbound western start straight just before its final bend");
        Check(!track.Props.Exists(p=>p.Type=="hay"),"bonus fields have no hay bales");
    }

    void BonusSoldierContactTest()
    {
        var track=TrackBuilder.Build(TrackDef.Load("res://tracks/bonus.json"));
        int obstacleCount=track.Obstacles.Count;
        var soldiers=new Troopers();AddChild(soldiers);soldiers.Init(track,2);
        Check(track.Obstacles.Count==obstacleCount,"soldiers add no immovable stone-like obstacles");
        for(int i=0;i<2;i++)
        {
            Vector2 p=soldiers.WalkerPosition(i);
            var car=new CarSim(new CarParams(),track);
            car.Reset(new Vector3(p.X,track.MeshHeight(p.X,p.Y)+.6f,p.Y+1.5f),0);
            car.Vel=new Vector3(0,0,-20);
            soldiers.CollideCar(car);
            float speed=car.Speed;
            soldiers.CollideCar(car);
            Check(soldiers.HitsThisTick==i+1 && speed>18 && speed<20 && Math.Abs(speed-car.Speed)<.001f,
                $"driver {i+1} knocks a soldier down with a light single impact and keeps moving");
        }
        soldiers.Free();
    }

    void HindImpactTest()
    {
        var car=new CarSim(new CarParams(),new FlatGround());
        car.Reset(new Vector3(10,1,20),.7f);
        Vector3 World(Vector3 p)=>car.Pos+car.Rot*p;
        Vector3 from=World(new Vector3(.7f,5,-1.9f)),to=World(new Vector3(.7f,-2,-1.9f));
        bool hit=ProjectileHits.Segment(car,from,to,out float fraction);
        Check(hit && fraction>0 && fraction<1,"a fast round hits the rotated bonnet before passing through the car");
        Vector3 contact=from.Lerp(to,fraction);
        Check(ProjectileHits.Distance(car,contact)<.001f && EngineHealth.CannonDamage(ProjectileHits.Distance(car,contact))==1,
            "a direct bonnet hit is lethal even away from the car centre");
        Check(ProjectileHits.Segment(car,World(new Vector3(0,5,0)),World(new Vector3(0,-2,0)),out _),"rounds hit the roof");
        Check(!ProjectileHits.Segment(car,World(new Vector3(1.3f,5,0)),World(new Vector3(1.3f,-2,0)),out _),"rounds alongside the body miss");
        Check(!ProjectileHits.Segment(car,World(new Vector3(-5,3,0)),World(new Vector3(5,3,0)),out _),"rounds above the roof miss");
        var health=new EngineHealth();health.Reset(true);
        float near=EngineHealth.CannonDamage(1),far=EngineHealth.CannonDamage(3);
        health.Damage(near,"cannon");health.Damage(near,"cannon");
        Check(near>far && far>0 && Math.Abs(health.Health-(1-2*near))<.0001f && EngineHealth.CannonDamage(4)==0,
            "nearby rounds accumulate damage with distance falloff and no damage outside four metres");

        var track=TrackBuilder.Build(TrackDef.Load("res://tracks/bonus.json"));
        track.Props.RemoveAll(p=>p.Type=="buk21");
        var fx=new TyreEffects();AddChild(fx);fx.Init(track);
        var hinds=new HindSorties();AddChild(hinds);hinds.Init(track,fx);
        bool shot=false,removed=false,failed=false;
        try
        {
            for(int frame=0;frame<180*30;frame++)
            {
                hinds.Tick(1f/30,null);
                if(!shot) foreach(var target in hinds.AirTargets) {shot=hinds.ShootDown(target.Id);break;}
                if(!removed)
                {
                    var debris=hinds.GetNode<Node3D>("HindDebris");
                    foreach(var child in debris.GetChildren())
                        if(child is GroundImpactEffects) {child.Free();removed=true;break;}
                }
                if(removed)
                {
                    // Exercise cleanup after an impact effect was independently freed.
                    for(int tick=0;tick<31*30;tick++) hinds.Tick(1f/30,null);
                    break;
                }
            }
        }
        catch(ObjectDisposedException) {failed=true;}
        Check(shot && removed && !failed,"Hind cleanup tolerates an already freed aircraft impact effect");
        hinds.Free();fx.Free();
    }

    void HindAirbaseTest()
    {
        GD.Print("Bonus Hind base: dispersed field post, winding service road and vertical departures");
        var track=TrackBuilder.Build(TrackDef.Load("res://tracks/bonus.json"));
        Check(!track.Props.Exists(p=>p.Type is "su25" or "hangar" or "hind" or "barracks"),"temporary field post has no hangar, barracks or static aircraft");
        Check(track.Props.FindAll(p=>p.Type=="helipad").Count==3,"three dispersed landing spots in the northern field");
        bool flat=true,paintGrounded=true,spacing=true;
        float apronHeight=track.MeshHeight(BonusAirbase.Pads[0].X,BonusAirbase.Pads[0].Y);
        foreach(var centre in BonusAirbase.Pads)
        {
            // Full rotor and tail footprint, including the former floating right-hand circle.
            for(float z=-11;z<=11;z+=1) for(float x=-10;x<=10;x+=1)
                if(MathF.Abs(track.MeshHeight(centre.X+x,centre.Y+z)-apronHeight)>.005f) flat=false;
            var prop=track.Props.Find(p=>p.Type=="helipad" && new Vector2(p.Position.X,p.Position.Z).DistanceTo(centre)<.1f);
            var pad=BonusAirbase.BuildPad(track,prop.Position,prop.Rotation);
            var mesh=(MeshInstance3D)pad.GetChild(0);
            foreach(var vertex in mesh.Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array())
            {
                Vector3 world=prop.Position+new Basis(Vector3.Up,prop.Rotation)*vertex;
                if(MathF.Abs(world.Y-track.MeshHeight(world.X,world.Z)-.035f)>.002f) paintGrounded=false;
            }
            pad.Free();
        }
        for(int a=0;a<BonusAirbase.Pads.Length;a++) for(int b=a+1;b<BonusAirbase.Pads.Length;b++)
            if(BonusAirbase.Pads[a].DistanceTo(BonusAirbase.Pads[b])<40) spacing=false;
        Check(flat,"all three complete landing footprints share the graded field datum");
        Check(paintGrounded,"every helipad paint vertex follows the real terrain surface");
        Check(track.Runway==null,"field post has no runway markings or edge-light circuit");
        Check(track.Props.Exists(p=>p.Type=="fieldtent") && track.Props.Exists(p=>p.Type=="fieldtruck") &&
            track.Props.Exists(p=>p.Type=="fieldnet") && track.Props.Exists(p=>p.Type=="fieldfuel") &&
            track.Props.Exists(p=>p.Type=="fieldgenerator"),"camp has tents, camouflaged vehicle maintenance, portable fuel and generator");
        bool grass=true,banks=true;
        foreach(var berm in BonusAirbase.Berms)
        {
            if(track.TileSurface((int)(berm.X/track.Tile),(int)(berm.Y/track.Tile))!=SurfaceType.Grass) grass=false;
            if(track.MeshHeight(berm.X,berm.Y)-apronHeight<2.2f) banks=false;
        }
        Check(grass && banks,"grass-covered earth banks separate the individual helicopter stands");
        bool northTurn=false,southTurn=false,narrow=true,roadClearStatic=true;
        int northernSamples=0;
        for(int i=0;i<track.Center.Length;i++)
        {
            Vector2 at=track.Center[i];
            if(at.Y>96 || at.X<60 || at.X>225) continue;
            northernSamples++;
            var direction=track.CenterDir(i);
            northTurn|=direction.Y<-.18f; southTurn|=direction.Y>.18f;
            if(track.HalfWidth[i]>4.1f) narrow=false;
            foreach(var obstacle in track.Obstacles)
                if(at.DistanceTo(obstacle.Center)<obstacle.Radius+1.3f) roadClearStatic=false;
        }
        Check(northernSamples>120 && northTurn && southTurn && narrow,"the former wide straight is a narrow sequence of opposite service-road bends");
        Check(roadClearStatic,"the new northern driving corridor stays clear of camp props and trees");
        Check(spacing,"rotor and tail envelopes are well separated at the dispersed stands");
        bool InRoadLoop(Vector2 point)
        {
            bool inside=false;
            for(int i=0,j=track.Center.Length-1;i<track.Center.Length;j=i++)
            {
                Vector2 a=track.Center[i],b=track.Center[j];
                if((a.Y>point.Y)!=(b.Y>point.Y) && point.X<(b.X-a.X)*(point.Y-a.Y)/(b.Y-a.Y)+a.X)
                    inside=!inside;
            }
            return inside;
        }
        bool enclosed=true;
        foreach(var centre in BonusAirbase.Pads)
            foreach(float x in new[]{-10f,0f,10f}) foreach(float z in new[]{-11f,0f,11f})
                if(!InRoadLoop(centre+new Vector2(x,z))) enclosed=false;
        foreach(var prop in track.Props)
            if(prop.Type is "fieldtent" or "fieldtruck" or "fieldnet" or "fieldfuel" or "fieldgenerator")
                if(!InRoadLoop(new Vector2(prop.Position.X,prop.Position.Z))) enclosed=false;
        Check(enclosed,"the driving loop surrounds every complete landing stand and the temporary camp");
        bool clear=true, usable=true, timing=true;
        for(int segment=1;segment<BonusAirbase.Bypass.Length;segment++)
        {
            Vector2 a=BonusAirbase.Bypass[segment-1],b=BonusAirbase.Bypass[segment];
            int steps=(int)MathF.Ceiling(a.DistanceTo(b));
            for(int i=0;i<=steps;i++)
            {
                Vector2 p=a.Lerp(b,i/(float)steps);
                foreach(var obstacle in track.Obstacles)
                    if(p.DistanceTo(obstacle.Center)<obstacle.Radius+1.4f) { if(clear) GD.Print($"bypass obstacle at {obstacle.Center}, radius {obstacle.Radius}, route {p}"); clear=false; }
                int road=track.NearestCenter(p,out float d);
                if(d>track.HalfWidth[road]+25) timing=false;
                Vector2 tangent=(b-a).Normalized(),side=new(-tangent.Y,tangent.X);
                foreach(float offset in new[]{-1f,0f,1f})
                {
                    Vector2 q=p+side*offset;
                    var surface=track.TileSurface((int)(q.X/track.Tile),(int)(q.Y/track.Tile));
                    if(surface is not (SurfaceType.Dirt or SurfaceType.Runway or SurfaceType.Concrete or SurfaceType.Gravel or SurfaceType.Pothole)) { if(usable) GD.Print($"bypass surface/grade at {q}: {surface}"); usable=false; }
                    float slope=MathF.Abs(track.MeshHeight(q.X+tangent.X,q.Y+tangent.Y)-track.MeshHeight(q.X,q.Y));
                    if(slope>.45f) { if(usable) GD.Print($"bypass grade at {q}: {slope}"); usable=false; }
                }
            }
        }
        Check(clear,"entire bypass has a car-wide corridor clear of props, poles, trees and fences");
        Check(usable,"bypass joins the road with a continuous driving surface and manageable grades");
        Check(timing,"bypass remains within the lap timer road tolerance");
        var route=(Vector2[])BonusAirbase.Bypass.Clone();
        int entry=track.NearestCenter(route[0],out _),exit=track.NearestCenter(route[^1],out _);
        if((exit-entry+track.Center.Length)%track.Center.Length>track.Center.Length/2)
        { Array.Reverse(route); (entry,exit)=(exit,entry); }
        var timer=new LapTimer(track); timer.Start();
        bool bypassUsed=false,shortcut=false;
        timer.ShortcutDetected+=()=>shortcut=true;
        for(int metre=0;metre<=track.Center.Length+2;metre++)
        {
            int index=metre%track.Center.Length;
            timer.Update(.05f,track.Center[index]);
            if(index!=entry || bypassUsed) continue;
            for(int s=1;s<route.Length;s++)
            {
                int steps=(int)MathF.Ceiling(route[s-1].DistanceTo(route[s]));
                for(int j=0;j<=steps;j++) timer.Update(.05f,route[s-1].Lerp(route[s],j/(float)steps));
            }
            metre+=(exit-entry+track.Center.Length)%track.Center.Length;
            timer.Update(.05f,track.Center[exit]); bypassUsed=true;
        }
        Check(bypassUsed && !shortcut && timer.LapsDone==1,"a complete lap through the bypass passes the real timing gates");
        // Exercise the flight routine without the independent live-fire battery shooting it down.
        track.Props.RemoveAll(p=>p.Type=="buk21");
        var fx=new TyreEffects(); AddChild(fx); fx.Init(track);
        var hinds=new HindSorties(); AddChild(hinds); hinds.Init(track,fx);
        int parked=0;
        foreach(var sample in hinds.FlightSamples)
            if(sample.Phase=="Parked") parked++;
        Check(parked==3 && hinds.ActiveCount==3,"all three Hinds are visible on their pads at the start");
        bool roadClear=true,lift=false,flight=false,returned=false,heading=true;
        bool banked=false,foldingGear=false,retractedGear=false;
        int attacks=0,activeMax=0,protectedHits=0,burstHits=0,completeBursts=0;
        float clock=0,warningAt=-100,firstAt=0;Vector3 firstHit=default;
        bool warned=true,longSweep=true;
        var timetable=new BonusExerciseControl();hinds.Exercise=timetable;
        hinds.AttackApproaching+=_=>warningAt=clock;
        hinds.AttackStarted+=_=> {attacks++;burstHits=0;warned &= clock-warningAt>=BonusExerciseControl.WarningLead-.04f;};
        hinds.CannonImpact+=p=> {
            if(burstHits++==0) {firstHit=p;firstAt=clock;}
            if(burstHits==HindSorties.BurstRounds) {
                completeBursts++;longSweep &= clock-firstAt>4.2f && p.DistanceTo(firstHit)>110;
            }
        };
        hinds.CannonImpact+=p=> { if(BonusAirbase.Protected(new Vector2(p.X,p.Z),0) || (track.MaintenanceHall?.Contains(new(p.X,p.Z)) ?? false)) protectedHits++; };
        // The Hind reserves its slots before the ground crew; inspect all newly allocated
        // flight positions as well, rather than relying on the runway-busy flag alone.
        for(int frame=0;frame<600*30;frame++)
        {
            clock+=1f/30;timetable.Tick(1f/30);
            hinds.Tick(1f/30,null);
            activeMax=Math.Max(activeMax,hinds.ActiveCount);
            foreach(var sample in hinds.FlightSamples)
            {
                var p=new Vector2(sample.Position.X,sample.Position.Z);
                int road=track.NearestCenter(p,out float distance);
                float height=sample.Position.Y-track.MeshHeight(p.X,p.Y);
                if(sample.Phase is "Parked" or "Spool" or "Descend")
                {
                    Vector2 nose=new(-MathF.Sin(sample.Yaw),-MathF.Cos(sample.Yaw));
                    if(nose.Dot(new Vector2(1,1).Normalized())<.999f) heading=false;
                }
                banked|=MathF.Abs(sample.Roll)>.15f;
                foldingGear|=sample.GearDown>.05f && sample.GearDown<.95f;
                retractedGear|=sample.GearDown<.01f;
                if(sample.Phase=="Lift") lift=true;
                if(sample.Phase=="Approach") flight=true;
                if(sample.Phase=="Descend") returned=true;
                if(sample.Phase is not ("Falling" or "Wreck") && height<10 && distance<track.HalfWidth[road]+8)
                { if(roadClear) GD.Print($"low Hind: {sample.Phase} at {sample.Position}, clearance {height:F2}, clock {clock:F1}");roadClear=false; }
                if(hinds.RunwayBusyAt(p)) roadClear=false;
            }
        }
        Check(activeMax==3 && lift && flight,"three Hinds depart in stages and rise vertically before translating into flight");
        Check(roadClear,"normal departures and returns never occupy the driving strip at car height");
        Check(banked && foldingGear && retractedGear,"Hinds bank through turns and retract their gear through intermediate poses");
        Check(heading,"Hind noses face southeast when parked, spooling and descending onto the pads");
        Check(attacks>0 && returned,"Hinds complete firing runs and return to their own pads");
        Check(warned && completeBursts>0 && longSweep,"scheduled Hinds warn three seconds ahead and finish uninterrupted long sweeps across both verges");
        Check(protectedHits==0,"normal exercise cannon impacts stay outside the field camp and northern road");
        int slots=track.Obstacles.Count;
        hinds.Reset();
        Check(hinds.ActiveCount==3 && track.Obstacles.Count==slots,"restart restores three parked Hinds without leaking collision slots");
        hinds.Free(); fx.Free();
    }
}
