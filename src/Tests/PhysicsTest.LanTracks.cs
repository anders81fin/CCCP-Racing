using System;
using System.IO;
using System.Linq;
using CCCPRacing.Net;
using CCCPRacing.Race;
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
        {using var m=new MemoryStream();using(var w=new BinaryWriter(m,System.Text.Encoding.UTF8,true)){w.Write((byte)7);w.Write((ushort)100);w.Write(true);w.Write(true);w.Write(track);w.Write(true);}net.OnPacket(2,m.ToArray());}
        Vote(n,"ilyich");
        Check(n.State==LanSession.Phase.Loading && n.Reveal.Track=="ilyich" && !n.Reveal.Fuel && n.Reveal.GoHost==0,"LAN track ballot: tally selects the track, enforces Ilyich no-fuel rule and waits for loading");
        var dry=LanSession.BallotFixture("atomgrad","siberia");dry.StartBallot();dry.Vote(100,true,true,"atomgrad");Vote(dry,"atomgrad");
        Check(dry.State==LanSession.Phase.Loading && dry.Reveal.Track=="atomgrad" && !dry.Reveal.Fuel,"LAN track ballot: Atomgrad has no fuel either (a fuel vote is ignored), like Ilyich");
        dry.Free();
        static bool Rows(string[] options,params int[] expect)=>RaceScene.LanBallotRows(options).SequenceEqual(expect);
        Check(Rows(new[]{"ilyich"},0,1,4) && Rows(new[]{"atomgrad"},0,1,4) && Rows(new[]{"siberia"},0,1,2,4) && Rows(new[]{"ilyich","atomgrad"},3,0,1,4) && Rows(new[]{"ilyich","siberia"},3,0,1,2,4) && Rows(new[]{"atomgrad","bonus"},3,0,1,2,4),
            "LAN ballot rows: the fuel-consumption row is offered only when some track option has fuel (Ilyich+Atomgrad: dry only, no row; with Siberia or Poligon: the row)");
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
        Check(LanBallot.Track(new[]{"siberia",null,null},options,1,out tied)=="siberia" && tied.Count==1,"LAN track ballot: NOT VOTED rows are skipped (the old first-track default would have won 2-1)");
        LanAbstentionTest();
    }

    /// <summary>
    /// The real host ballot with abstentions: the 60 s deadline, only submitted ballots deciding,
    /// NOT VOTED on the wire and the all-abstain draw over every fuel, rule value and host track.
    /// </summary>
    void LanAbstentionTest()
    {
        var all=new[]{"ilyich","siberia","atomgrad","bonus"};
        // Mixed: one host ballot (Diesel, damage NO, fuel YES, Siberia) against two NOT VOTED.
        // The old defaults (Petrol, YES, NO, first track) would have reversed all four by 2-1.
        bool mixed=true,wire=true,deadline=true;
        for(int seed=0;seed<24;seed++)
        {
            var h=LanSession.AbstentionFixture(3,all);h.TestSeed=seed*977+5;h.StartBallot();
            h.Vote(50,false,true,"siberia");
            h.BallotDeadline(h.BallotEndsHost-1);
            deadline&=h.State==LanSession.Phase.Ballot;
            h.BallotDeadline(h.BallotEndsHost);
            deadline&=h.State==LanSession.Phase.Loading;
            var rv=h.Reveal;
            mixed&=rv.Power==50 && !rv.Drum && !rv.Damage && rv.Fuel && rv.Track=="siberia" && rv.TrackTied.Count==1 && !rv.NoVotes
                && rv.Ballots.Count==3 && rv.Grid.Count==3 && rv.Ballots.Count(b=>!LanBallot.Voted(b.pct))==2
                && rv.RuleVotes.Count(v=>v.damage==null && v.fuel==null)==2 && rv.TrackVotes.Count(t=>t==null)==2;
            var c=new LanSession();c.OnPacket(1,h.RevealFixturePacket(true));
            wire&=c.Reveal!=null && c.Reveal.Power==50 && !c.Reveal.Damage && c.Reveal.Fuel && c.Reveal.Track=="siberia" && !c.Reveal.NoVotes
                && c.Reveal.Ballots.Select(b=>b.pct).SequenceEqual(rv.Ballots.Select(b=>b.pct)) && c.Reveal.RuleVotes.SequenceEqual(rv.RuleVotes) && c.Reveal.Grid.SequenceEqual(rv.Grid);
            c.Free();h.Free();
        }
        Check(deadline,"LAN abstention: the host deadline closes the ballot only when the 60 s are up");
        Check(mixed,"LAN abstention: one submitted ballot decides fuel, damage, fuel consumption and track over two NOT VOTED; they keep their grid places");
        Check(wire,"LAN abstention: NOT VOTED rows (fuel 0, rule byte 2) survive the production reveal codec unchanged");
        // Three ballots and one NOT VOTED: ROCKET 2-1, damage YES 2-1, fuel NO 2-1, Atomgrad 2-1.
        var t=LanSession.AbstentionFixture(4,all);t.TestSeed=11;t.StartBallot();
        t.Vote(200,true,false,"atomgrad");t.FixtureVote(2,200,false,false,"atomgrad");t.FixtureVote(3,100,true,true,"bonus");
        t.BallotDeadline(t.BallotEndsHost+.5);
        Check(t.Reveal.Power==200 && t.Reveal.Track=="atomgrad" && !t.Reveal.Fuel && t.Reveal.Damage && !t.Reveal.Drum && t.Reveal.Ballots.Count(b=>!LanBallot.Voted(b.pct))==1,
            "LAN abstention: with three ballots and one NOT VOTED, only the three submissions count (damage 2-1 YES, fuel 2-1 NO)");
        t.Free();
        // Early completion: everyone has voted, no wait for the deadline.
        var e=LanSession.AbstentionFixture(3,all);e.StartBallot();e.Vote(100,true,false,"ilyich");e.FixtureVote(2,100,true,false,"ilyich");
        Check(e.State==LanSession.Phase.Ballot,"LAN abstention: a missing ballot keeps the box open before the deadline");
        e.FixtureVote(3,201,true,false,"ilyich");
        Check(e.State==LanSession.Phase.Loading && LanSession.Now<e.BallotEndsHost && e.Reveal.Ballots.All(b=>LanBallot.Voted(b.pct)),"LAN abstention: the last ballot closes the box early");
        e.Free();
        // Nobody votes: everything drawn by lot from the seed; no counts or votes are invented.
        var fuels=new System.Collections.Generic.HashSet<int>();var tracks=new System.Collections.Generic.HashSet<string>();
        var dmg=new System.Collections.Generic.HashSet<bool>();var fc=new System.Collections.Generic.HashSet<bool>();
        bool honest=true,repeat=true,ilyichDry=true,noWire=true;
        for(int seed=0;seed<200;seed++)
        {
            LanReveal Run(){var h=LanSession.AbstentionFixture(3,all);h.TestSeed=seed*7919+3;h.StartBallot();h.BallotDeadline(h.BallotEndsHost);var r=h.Reveal;
                if(seed<40){var c=new LanSession();c.OnPacket(1,h.RevealFixturePacket(true));
                    noWire&=c.Reveal!=null && c.Reveal.NoVotes && c.Reveal.Tied.SequenceEqual(LanBallot.AllFuels) && c.Reveal.Power==r.Power && c.Reveal.Damage==r.Damage && c.Reveal.Fuel==r.Fuel && c.Reveal.Track==r.Track && c.Reveal.RuleVotes.All(v=>v.damage==null && v.fuel==null);c.Free();}
                h.Free();return r;}
            var a=Run();var b=Run();
            repeat&=a.Power==b.Power && a.Damage==b.Damage && a.Fuel==b.Fuel && a.Track==b.Track && a.Grid.SequenceEqual(b.Grid);
            honest&=a.NoVotes && a.Tied.SequenceEqual(LanBallot.AllFuels) && a.TrackTied.SequenceEqual(all) && a.Ballots.Count==3 && a.Grid.Count==3
                && a.Ballots.All(x=>x.pct==LanBallot.Abstain) && a.RuleVotes.All(v=>v.damage==null && v.fuel==null) && LanRace.ValidFuel(a.Power);
            if(!TrackIds.HasFuel(a.Track)) ilyichDry&=!a.Fuel; else fc.Add(a.Fuel);
            fuels.Add(a.Power);tracks.Add(a.Track);dmg.Add(a.Damage);
        }
        Check(honest,"LAN no votes: every ballot stays NOT VOTED, all fuels and host tracks go on the drum, nothing is counted");
        Check(fuels.SetEquals(LanBallot.AllFuels) && tracks.SetEquals(all) && dmg.Count==2 && fc.Count==2,$"LAN no votes: every fuel ({fuels.Count}), host track ({tracks.Count}), damage and fuel-consumption value is drawn across seeds");
        Check(repeat,"LAN no votes: the same seed draws the same track, fuel, rules and grid");
        Check(ilyichDry,"LAN no votes: a drawn Ilyich still has no fuel consumption");
        Check(noWire,"LAN no votes: the all-abstain count survives the production reveal codec");
        var two=new System.Collections.Generic.HashSet<string>();
        for(int seed=0;seed<60;seed++){var h=LanSession.AbstentionFixture(2,"siberia","bonus");h.TestSeed=seed;h.StartBallot();h.BallotDeadline(h.BallotEndsHost);two.Add(h.Reveal.Track);h.Free();}
        Check(two.SetEquals(new[]{"siberia","bonus"}),"LAN no votes: only the host's offered tracks can be drawn");
        // Forged counts are rejected, never read as Petrol or as NO.
        var f=LanSession.AbstentionFixture(2,all);f.TestSeed=1;f.StartBallot();f.Vote(100,true,false,"bonus");f.BallotDeadline(f.BallotEndsHost);
        var good=f.Reveal.Ballots[1];
        f.Reveal.RuleVotes[1]=(true,null);var c1=new LanSession();c1.OnPacket(1,f.RevealFixturePacket(true));
        f.Reveal.RuleVotes[1]=(null,null);f.Reveal.Ballots[1]=(good.id,good.name,good.slot,175);var c2=new LanSession();c2.OnPacket(1,f.RevealFixturePacket(true));
        f.Reveal.Ballots[1]=(good.id,good.name,good.slot,100);var c3=new LanSession();c3.OnPacket(1,f.RevealFixturePacket(true));
        f.Reveal.Ballots[1]=good;var c4=new LanSession();c4.OnPacket(1,f.RevealFixturePacket(true));
        Check(c1.Reveal==null && c2.Reveal==null && c3.Reveal==null && c4.Reveal!=null && c4.Reveal.Ballots[1].pct==LanBallot.Abstain,
            "LAN abstention: the decoder rejects half-abstained rows, unknown fuels and a Petrol pct with NOT VOTED rules");
        c1.Free();c2.Free();c3.Free();c4.Free();f.Free();
        bool footer=true;
        foreach(var (compact,panel) in new[]{(true,200),(false,370)})
        {string t2=CCCPRacing.UI.Hud.BallotFooter(60,compact);footer&=t2.Contains("NOT VOTED") && !t2.Contains("DEFAULT") && CCCPRacing.UI.PixelFont.Measure(t2)<=panel-8;}
        bool header=true;
        foreach(int proto in new[]{12,LanSession.Protocol})
            try{LanReplayFile.Validate(new LanReplayFile.Header("siberia","0123456789abcdef","0.853",proto,5,1,new[]{new LanReplayFile.Player(1,"HOST",0),new LanReplayFile.Player(2,"CLIENT",1)},201));}catch(InvalidDataException){header=false;}
        try{LanReplayFile.Validate(new LanReplayFile.Header("siberia","0123456789abcdef","0.853",LanSession.Protocol,5,1,new[]{new LanReplayFile.Player(1,"HOST",0)},0));header=false;}catch(InvalidDataException){}
        Check(header && LanSession.Protocol==17,"LAN abstention: current and stored 12 replay headers pass; a NOT VOTED marker is never a replay fuel");
        Check(footer,"LAN abstention: the ballot footer explains the deadline as NOT VOTED, claims no defaults and fits 208 and 480 cards");
    }
}
