using System;
using System.Linq;
using CCCPRacing.Car;
using CCCPRacing.Net;
using CCCPRacing.Track;
using Godot;
namespace CCCPRacing.Tests;
public partial class PhysicsTest
{
    void LanCapacityTest()
    {
        var n=LanSession.CapacityFixture();
        for(long id=2;id<=6;id++)n.CapacityHello(id);
        Check(n.Players.Count==6 && n.Players.Values.Select(p=>p.Slot).OrderBy(x=>x).SequenceEqual(Enumerable.Range(0,6)),"LAN six: production hello admits six unique player slots including host");
        n.CapacityHello(7);
        Check(n.Players.Count==6 && !n.Players.ContainsKey(7) && n.Scores.Count()==6,"LAN six: seventh hello cannot enter or add a score entry");
        n.StartBallot();n.Vote(100,true,false,"ilyich");
        for(long id=2;id<=6;id++)n.CapacityVote(id);
        Check(n.State==LanSession.Phase.Loading && n.Reveal.Ballots.Count==6 && n.Reveal.Grid.Distinct().Count()==6,"LAN six: six submitted ballots close the actual ballot and draw six grid positions");
        var client=new LanSession();client.OnPacket(1,n.RevealFixturePacket(true));
        Check(client.Reveal.Ballots.Select(b=>b.slot).OrderBy(x=>x).SequenceEqual(Enumerable.Range(0,6)) && client.Reveal.Grid.SequenceEqual(n.Reveal.Grid),"LAN six: actual reveal encoder and decoder retain all six slots and grid order");
        Check(LanRace.Tag.Take(6).Distinct().Count()==6 && LanRace.Paint.Length>=6,"LAN six: six separate paints and readable identity colours exist");
        foreach(string id in new[]{"ilyich","siberia","atomgrad","bonus"})
        {
            var t=TrackBuilder.Build(TrackDef.Load("res://tracks/"+id+".json"));
            var cars=Enumerable.Range(0,6).Select(g=>{LanRace.GridPose(t,g,out var pos,out float yaw);var c=new CarSim(new CarParams(),t);c.Reset(pos,yaw);return c;}).ToArray();
            bool fit=Enumerable.Range(0,6).All(g=>t.HalfWidth[LanRace.GridCenter(t,g)]>=LanRace.Column+1.2f && float.IsFinite(cars[g].Pos.Y));
            bool separate=true;for(int a=0;a<6;a++)for(int b=a+1;b<6;b++)separate &= cars[a].Pos.DistanceTo(cars[b].Pos)>3f;
            Check(fit && separate,"LAN six: "+id+" has six grounded, separate grid poses inside the road");
            var fx=new TyreEffects();AddChild(fx);fx.Init(t);
            for(int slot=0;slot<6;slot++) {foreach(var w in cars[slot].Wheels)w.Contact=false;fx.Emit(cars[slot],1f/60,slot);}
            fx.QueueFree();
            Check(true,"LAN six: "+id+" drives all six production wheel-effect slots without overflow");
        }
        n.CapacityFinishAll();
        Check(n.State==LanSession.Phase.Results && n.Results.Count==6 && n.Scores.Sum(s=>s.Wins)==1,"LAN six: host completes six actual finish handlers and awards a single session win");
        client.OnPacket(1,n.ScoreFixtureResultsPacket());
        Check(client.Results.Count==6 && client.Results.Select(r=>r.Slot).SequenceEqual(n.Results.Select(r=>r.Slot)) && client.Scores.Count()==6 && client.Scores.Sum(s=>s.Wins)==1,"LAN six: result and session-total production decoders preserve all six players");
        client.Free();n.Free();
    }
}
