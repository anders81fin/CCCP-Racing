using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using CCCPRacing.Net;
using CCCPRacing.Audio;
using Godot;
using System.Collections.Generic;
namespace CCCPRacing.Tests;
public partial class PhysicsTest
{
    void LanObstaclesTest()
    {
        LanHiddenSoundTest();
        foreach(bool on in new[]{false,true}) {
            var h=LanSession.AbstentionFixture(3,"siberia"); h.StartBallot();
            h.Vote(100,true,true,"siberia",on); h.BallotDeadline(h.BallotEndsHost);
            Check(h.Reveal.Obstacles==on && h.Reveal.Damage && h.Reveal.Fuel && h.Reveal.ObstacleVotes.SequenceEqual(new bool?[]{on,null,null}),"LAN obstacles: sole submitted "+on+" wins independently of damage/fuel and abstentions");
            var c=new LanSession();c.OnPacket(1,h.RevealFixturePacket(true));
            Check(c.Reveal?.Obstacles==on && c.Reveal.ObstacleVotes.SequenceEqual(h.Reveal.ObstacleVotes),"LAN obstacles: result and nullable votes survive production reveal codec "+on);
            c.Free();h.Free();
        }
        var m=LanSession.AbstentionFixture(3,"siberia");m.StartBallot();m.Vote(100,false,false,"siberia",true);
        m.FixtureVote(2,100,false,false,"siberia",false);m.FixtureVote(3,100,false,false,"siberia",false);
        Check(!m.Reveal.Obstacles && !m.Reveal.Damage && !m.Reveal.Fuel,"LAN obstacles: client packets join the shared majority");m.Free();
        var drawn=new System.Collections.Generic.HashSet<bool>();bool deterministic=true;
        for(int seed=0;seed<60;seed++) {
            bool Run() {var h=LanSession.AbstentionFixture(2,"siberia");h.TestSeed=seed;h.StartBallot();h.BallotDeadline(h.BallotEndsHost);bool v=h.Reveal.Obstacles;h.Free();return v;}
            bool a=Run();drawn.Add(a);deterministic&=a==Run();
        }
        Check(drawn.Count==2 && deterministic,"LAN obstacles: nobody voted draws either value reproducibly");
        // Legacy JSON lacks the new member: it must preserve the old populated world.
        foreach(int protocol in new[]{11,12,13}) {
            string json="{\"Track\":\"siberia\",\"Signature\":\"0123456789abcdef\",\"GameVersion\":\"0.853\",\"Protocol\":"+protocol+",\"Seed\":1,\"LocalId\":1,\"Players\":[{\"Id\":1,\"Name\":\"HOST\",\"Slot\":0}],\"Fuel\":100}";
            var old=JsonSerializer.Deserialize<LanReplayFile.Header>(json);LanReplayFile.Validate(old);
            Check(old.Obstacles,"LAN obstacles: legacy protocol "+protocol+" metadata defaults to populated world");
        }
        string path=Path.Combine(Path.GetTempPath(),"lan-obstacle-"+Guid.NewGuid()+".lanreplay");
        try {
            var header=new LanReplayFile.Header("siberia","0123456789abcdef","0.853",LanSession.Protocol,1,1,new[]{new LanReplayFile.Player(1,"HOST",0)},100,false);
            using(var w=new LanReplayFile.Writer(path,header)){w.Add(0,LanReplayFile.Kind.Go,new byte[]{1});w.Finish().GetAwaiter().GetResult();}
            using var r=new LanReplayFile.Reader(path);
            Check(!r.Metadata.Obstacles && r.Metadata.Protocol==LanSession.Protocol && r.Read(r.Entries[0]).SequenceEqual(new byte[]{1}),"LAN obstacles: OFF recording saves and reloads with payload intact");
        } finally {if(File.Exists(path))File.Delete(path);}
    }
    void LanHiddenSoundTest()
    {
        var host=new Node3D();var client=new Node3D();AddChild(host);AddChild(client);
        var engine=new DieselSound();host.AddChild(engine);
        var sender=new LanWorld(host);var receiver=new LanWorld(client);
        var states=new List<byte[]>();var obstacles=new List<CCCPRacing.Track.Obstacle>();
        void Sample() => sender.Capture(new[]{host},obstacles,Array.Empty<int>(),(b,mesh)=>{if(!mesh)states.Add(b);});
        Sample();host.Hide();Sample();host.Show();Sample();
        bool good=true;
        foreach(int at in new[]{0,1,2,0,1,2}) {
            receiver.ResetPlaybackState();receiver.StateIn(states[at],obstacles);receiver.Snap();receiver.SetPaused(false);
            bool audible=at!=1;
            good &= receiver.AudibleReplicaCount==(audible?1:0);
            if(audible) good &= client.FindChildren("*","AudioStreamPlayer3D",true,false).OfType<AudioStreamPlayer3D>().Any(a=>!a.StreamPaused);
        }
        Check(good,"LAN empty world: hidden actor engines are omitted on replicas, including backward seek and loop to audible state");
        sender.Free();receiver.Free();host.QueueFree();client.QueueFree();
    }

}
