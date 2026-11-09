using System.Linq;
using System.IO;
namespace CCCPRacing.Net;
// Transport-free fixtures exercise the same receive handlers as ENet; production smoke covers real peers and scene reload.
public partial class LanSession
{
    internal static LanSession LoadingFixture(string track,int round=1)
    {
        var n=new LanSession {IsHost=true,State=Phase.Loading,RoundNumber=round};
        n.Reveal=new LanReveal {Round=round,Track=track,Tied=new(){100},Grid=new(){1,2}};
        n.Players[1]=new LanPlayer {Id=1,Name="HOST",Slot=0};n.Players[2]=new LanPlayer {Id=2,Name="CLIENT",Slot=1};
        return n;
    }
    /// <summary>Test only: the host's count seed (production draws it at random).</summary>
    internal int? TestSeed;
    /// <summary>A host lobby of <paramref name="players"/> comrades (host id 1, clients 2..n) offering the tracks.</summary>
    internal static LanSession AbstentionFixture(int players,params string[] options)
    {
        var n=BallotFixture(options);
        for(int id=3;id<=players;id++) n.Players[id]=new LanPlayer{Id=id,Name="COMRADE "+id,Slot=id-1};
        return n;
    }
    /// <summary>A client's Vote packet through the production receive handler.</summary>
    internal void FixtureVote(long peer,int fuel,bool damage,bool fuelCons,string track,bool obstacles=true)
    {
        using var m=new MemoryStream();using(var w=new BinaryWriter(m,System.Text.Encoding.UTF8,true))
        {w.Write((byte)Msg.Vote);w.Write((ushort)fuel);w.Write(damage);w.Write(fuelCons);w.Write(track);w.Write(obstacles);}
        OnPacket(peer,m.ToArray());
    }
    /// <summary>Test: a client says hello (the production receive handler) with what he has opened; id 2 and up.</summary>
    internal void FixtureHello(long peer,string name,LanUnlocks unlocks)
    {
        _pending[peer]=Now;
        using var m=new MemoryStream();using(var w=new BinaryWriter(m,System.Text.Encoding.UTF8,true))
        {w.Write((byte)Msg.Hello);w.Write(Version+"/lan"+Protocol);w.Write("");w.Write(name);w.Write("");w.Write(System.Guid.NewGuid().ToString("N"));w.Write(unlocks.WriteTracks());w.Write((byte)unlocks.Fuels);}
        OnPacket(peer,m.ToArray());
    }
    internal void FixtureLeave(long peer)=>OnPeerDisconnected(peer);
    internal void FixtureOpenBallot()=>State=Phase.Ballot;
    /// <summary>Test: the host has opened every fuel (the ballot then offers them all to a lobby of rookies).</summary>
    internal void FixtureHostOpenedAll(){Players[1].Unlocks=new LanUnlocks(new[]{"ilyich"},LanUnlocks.AllFuels);RefreshOptions();}
    /// <summary>The lobby message as the host writes it, read back by a fresh client (the track options and the fuel mask travel in it).</summary>
    internal static (System.Collections.Generic.List<string> tracks,int fuels) FixtureOptionsRoundTrip(LanSession host)
    {
        using var m=new MemoryStream();using(var w=new BinaryWriter(m,System.Text.Encoding.UTF8,true)) host.WriteTrackOptions(w);
        var client=new LanSession();using var rd=new BinaryReader(new MemoryStream(m.ToArray()));client.ReadTrackOptions(rd);
        return (client.TrackOptions,client.FuelMask);
    }
    internal static LanSession BallotFixture(params string[] options)
    {var n=LoadingFixture("ilyich");n.State=Phase.Lobby;n.Reveal=null;n.RoundNumber=0;n.ConfigureTracks(options);return n;}
    internal byte[] RevealFixturePacket(bool loading)
    {using var m=new MemoryStream();using(var w=new BinaryWriter(m,System.Text.Encoding.UTF8,true)){w.Write((byte)(loading?Msg.LoadTrack:Msg.Reveal));WriteReveal(w,Reveal);}return m.ToArray();}
}

public partial class LanSession
{
    internal static LanSession ScoreFixture()
    {
        var n=LoadingFixture("ilyich");n.State=Phase.Race;
        foreach(var p in n.Players.Values){p.SessionToken=p.Id.ToString();n.RegisterScore(p.Id,p.SessionToken,p);n._roundRoster[p.Id]=p;}
        return n;
    }
    internal void ScoreFixtureFinish(long winner)
    {WinnerId=winner;foreach(var p in Players.Values){p.Finished=p.Id==winner;p.Dnf=p.Id!=winner;p.FinishTime=p.Finished?42:-1;}CheckRaceOver();}
    internal void ScoreFixtureAward()=>AwardSessionWin();
    internal void ScoreFixtureNext(){RoundNumber++;State=Phase.Race;Reveal=new LanReveal{Round=RoundNumber,Tied=new(){100}};WinnerId=0;_roundRoster.Clear();foreach(var p in Players.Values){p.Finished=p.Dnf=false;p.FinishTime=-1;_roundRoster[p.Id]=p;}}
    internal void ScoreFixtureDisconnect(long id){DisconnectScore(id);Players.Remove(id);}
    internal void ScoreFixtureReconnect(long id){var p=new LanPlayer{Id=id,Name="CLIENT",Slot=1,SessionToken="2"};Players[id]=p;RegisterScore(id,"2",p);}
    internal void ScoreFixtureNoFinish(){foreach(var p in Players.Values)p.Dnf=true;CheckRaceOver();}
    internal void ScoreFixtureRound(int round)=>RoundNumber=round;
    internal byte[] ScoreFixtureEndPacket()
    {using var m=new MemoryStream();using(var w=new BinaryWriter(m,System.Text.Encoding.UTF8,true)){w.Write((byte)Msg.SessionEnd);w.Write(RoundNumber);WriteScores(w);}return m.ToArray();}
    internal byte[] ScoreFixtureResultsPacket()
    {using var m=new MemoryStream();using(var w=new BinaryWriter(m,System.Text.Encoding.UTF8,true)){w.Write((byte)Msg.Results);w.Write(RoundNumber);w.Write((byte)Results.Count);foreach(var x in Results){w.Write(x.Id);w.Write(x.Name);w.Write((byte)x.Slot);w.Write(x.Time);w.Write(x.Best);w.Write((byte)(x.Pad?1:0));}WriteScores(w);}return m.ToArray();}
}

public partial class LanSession
{
    internal static LanSession CapacityFixture()
    {
        var n=new LanSession{IsHost=true,InLobby=true,State=Phase.Lobby};
        var p=new LanPlayer{Id=1,Name="HOST",Slot=0,SessionToken=System.Guid.NewGuid().ToString("N")};
        n.Players[1]=p;n.RegisterScore(1,p.SessionToken,p);return n;
    }
    internal void CapacityHello(long peer)
    {
        _pending[peer]=Now;
        using var m=new MemoryStream();using(var w=new BinaryWriter(m,System.Text.Encoding.UTF8,true))
        {w.Write((byte)Msg.Hello);w.Write(Version+"/lan"+Protocol);w.Write("unused-background-signature");w.Write("COMRADE "+peer);w.Write("bonus");w.Write(System.Guid.NewGuid().ToString("N"));}
        OnPacket(peer,m.ToArray());
    }
    internal void CapacityFinishAll()
    {State=Phase.Race;foreach(var p in Players.Values.ToArray()) Finished(p,40+p.Slot,40+p.Slot,false);}
    internal void CapacityVote(long peer, int fuel=100)
    {
        using var m=new MemoryStream();using(var w=new BinaryWriter(m,System.Text.Encoding.UTF8,true))
        {w.Write((byte)Msg.Vote);w.Write((ushort)fuel);w.Write(true);w.Write(false);w.Write("ilyich");w.Write(true);}
        OnPacket(peer,m.ToArray());
    }
}
