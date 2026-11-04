using System;
using System.IO;
using System.Linq;
using CCCPRacing.Net;
using CCCPRacing.Track;
namespace CCCPRacing.Tests;
public partial class PhysicsTest
{
    void LanTrackBallotTest()
    {
        var options=new[]{"ilyich","siberia","atomgrad","bonus"};
        Check(LanBallot.Track(new[]{"siberia","siberia","bonus"},options,1,out var tied)=="siberia" && tied.SequenceEqual(new[]{"siberia"}),"LAN track ballot: majority wins among host options");
        Check(LanBallot.Track(new[]{"bonus",null},new[]{"ilyich"},1,out _)=="ilyich","LAN track ballot: one available track is automatic and defaults stay inside host options");
        var result=LanBallot.Track(new[]{"ilyich","siberia"},options,72,out tied);
        Check(tied.SequenceEqual(new[]{"ilyich","siberia"}) && LanBallot.Track(new[]{"siberia","ilyich"},options,72,out _)==result,"LAN track ballot: tied draw is reproducible independent of vote arrival order");
        Check(Enumerable.Range(0,1000).Select(seed=>LanBallot.Track(new[]{"ilyich","siberia"},options,seed,out _)).Distinct().Count()==2,"LAN track ballot: a tie can choose either tied track, never an unvoted track");
        var n=LanSession.BallotFixture("ilyich","siberia");n.StartBallot();n.Vote(100,true,true,"bonus");
        Check(!n.Me.Voted,"LAN track ballot: host rejects a vote outside the host's advertised tracks");
        n.Vote(100,true,true,"ilyich");
        void Vote(LanSession net,string track)
        {using var m=new MemoryStream();using(var w=new BinaryWriter(m,System.Text.Encoding.UTF8,true)){w.Write((byte)7);w.Write((ushort)100);w.Write(true);w.Write(true);w.Write(track);}net.OnPacket(2,m.ToArray());}
        Vote(n,"ilyich");
        Check(n.State==LanSession.Phase.Loading && n.Reveal.Track=="ilyich" && !n.Reveal.Fuel && n.Reveal.GoHost==0,"LAN track ballot: tally selects the track, enforces Ilyich no-fuel rule and waits for loading");
        var client=new LanSession();client.OnPacket(1,n.RevealFixturePacket(true));
        Check(client.State==LanSession.Phase.Loading && client.SelectedTrack==n.SelectedTrack && client.RoundNumber==n.RoundNumber,"LAN track ballot: client receives selected track and round through the production packet decoder");
        client.Free();n.Free();
        string sig=TrackSignature.Compute(TrackBuilder.Build(TrackDef.Load("res://tracks/ilyich.json")));
        string other=TrackSignature.Compute(TrackBuilder.Build(TrackDef.Load("res://tracks/siberia.json")));
        void Ready(LanSession net,long id,int round,string track,string signature)
        {using var m=new MemoryStream();using(var w=new BinaryWriter(m,System.Text.Encoding.UTF8,true)){w.Write((byte)23);w.Write(round);w.Write(track);w.Write(signature);}net.OnPacket(id,m.ToArray());}
        n=LanSession.LoadingFixture("ilyich",10);int reveals=0;n.RevealIn+=()=>reveals++;
        Ready(n,99,10,"ilyich",sig);Ready(n,2,9,"ilyich",sig);
        Check(n.LoadedCount==0,"LAN loading: unknown peers and stale ready packets cannot open the gate");
        Ready(n,2,10,"ilyich",sig);n.StartCountdown();
        Check(n.State==LanSession.Phase.Loading && n.LoadedCount==1 && n.Reveal.GoHost==0,"LAN loading: ready client cannot start before the host world is ready");
        Ready(n,1,10,"ilyich",sig);
        Check(n.State==LanSession.Phase.Race && reveals==1 && n.Reveal.GoHost==0,"LAN loading: matching worlds open reveal exactly once and still wait for clutch countdown");
        Ready(n,2,10,"ilyich",sig);
        Check(reveals==1,"LAN loading: duplicate ready packets cannot repeat reveal");n.Free();
        n=LanSession.LoadingFixture("ilyich");Ready(n,1,1,"ilyich",sig);Ready(n,2,1,"ilyich",other);
        Check(n.State==LanSession.Phase.Lobby && n.LastLoadError=="TRACK DATA MISMATCH" && n.Players.Count==2,"LAN loading: different built signatures abort only the round, retaining the lobby");n.Free();
        n=LanSession.LoadingFixture("ilyich");Ready(n,1,1,"ilyich",sig);Ready(n,2,1,"bonus",sig);
        Check(n.State==LanSession.Phase.Lobby && n.Reveal==null,"LAN loading: a ready report for the wrong map cannot start a race");n.Free();
    }
}
