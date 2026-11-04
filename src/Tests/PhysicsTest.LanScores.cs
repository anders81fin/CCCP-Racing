using System;
using System.Linq;
using System.IO;
using CCCPRacing.Net;
namespace CCCPRacing.Tests;
public partial class PhysicsTest
{
    void LanSessionScoresTest()
    {
        var n=LanSession.ScoreFixture();
        n.ScoreFixtureFinish(1);
        Check(n.Scores.First().Wins==1 && n.Scores.First().Name=="HOST","LAN session: host-validated winner receives one win");
        n.ScoreFixtureAward();n.ScoreFixtureAward();
        Check(n.Scores.Sum(s=>s.Wins)==1,"LAN session: repeated completion cannot award duplicate wins");
        n.BackToLobby();
        Check(n.Scores.Sum(s=>s.Wins)==1,"LAN session: lobby return retains wins");
        n.ScoreFixtureNext();n.ScoreFixtureFinish(2);
        Check(n.Scores.All(s=>s.Wins==1),"LAN session: next round awards its winner, retaining previous totals");
        n.ScoreFixtureDisconnect(2);
        Check(n.Scores.Count()==2 && n.Scores.Any(s=>s.Name=="CLIENT" && !s.Connected && s.Wins==1),"LAN session: disconnected winner remains in totals");
        n.ScoreFixtureReconnect(9);
        Check(n.Scores.Count()==2 && n.Scores.Single(s=>s.Name=="CLIENT").Connected && n.Scores.Single(s=>s.Name=="CLIENT").Wins==1,"LAN session: reconnect identity recovers wins without a name merge");
        n.ScoreFixtureNext();n.ScoreFixtureNoFinish();
        Check(n.Scores.Sum(s=>s.Wins)==2,"LAN session: all DNF awards no win");
        var c=new LanSession();
        c.ScoreFixtureRound(n.RoundNumber);
        int events=0;c.ResultsIn+=()=>events++;
        var packet=n.ScoreFixtureResultsPacket();c.OnPacket(1,packet);c.OnPacket(1,packet);
        Check(events==1 && c.Scores.Sum(s=>s.Wins)==2,"LAN session: actual result decoder replaces totals once, ignoring duplicate packets");
        c.ScoreFixtureRound(n.RoundNumber+1);c.OnPacket(1,packet);
        Check(events==1,"LAN session: stale round results cannot reopen the results screen");
        c.EndSession();
        Check(c.State==LanSession.Phase.Results,"LAN session: client cannot end the host session");
        int ended=0;n.SessionEnded+=()=>ended++;
        n.EndSession();n.EndSession();
        Check(n.State==LanSession.Phase.SessionFinished && ended==1 && n.Scores.Sum(s=>s.Wins)==2,"LAN session: host ends once without clearing totals");
        n.BackToLobby();
        Check(n.State==LanSession.Phase.SessionFinished,"LAN session: ended session cannot start another round");
        c.ScoreFixtureRound(n.RoundNumber);int finals=0;c.SessionEnded+=()=>finals++;
        var final=n.ScoreFixtureEndPacket();c.OnPacket(1,final);c.OnPacket(1,final);
        Check(c.State==LanSession.Phase.SessionFinished && finals==1 && c.Scores.Sum(s=>s.Wins)==2,"LAN session: all clients receive the final totals and champion event once");
        n.Leave();
        Check(!n.Scores.Any(),"LAN session: closing session clears the ledger");
        c.Free();n.Free();
    }
}
