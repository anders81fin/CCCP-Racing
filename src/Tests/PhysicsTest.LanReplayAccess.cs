using System;
using System.IO;
using CCCPRacing.Net;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    void LanReplayAccessTest()
    {
        var session=LanSession.ScoreFixture();
        try {
        session.FinishFor(1,42,42);
        Check(session.State==LanSession.Phase.Race && LanReplayAccess.For(true,session.State)==LanReplayAccess.Permission.Closed,
            "LAN replay: own finish does not unlock watching while another player races");
        session.EndSession();
        Check(LanReplayAccess.For(true,session.State)==LanReplayAccess.Permission.Closed,"LAN replay: session cannot end early to unlock export");
        session.FinishFor(2,45,45);
        Check(session.State==LanSession.Phase.Results && LanReplayAccess.For(true,session.State)==LanReplayAccess.Permission.Watch,
            "LAN replay: all finishers unlock watching but not MP4 export");
        session.BackToLobby();
        Check(LanReplayAccess.For(true,session.State)==LanReplayAccess.Permission.Closed,"LAN replay: continuing to the lobby revokes watching");
        session.ScoreFixtureNext();session.ScoreFixtureNoFinish();
        Check(LanReplayAccess.For(true,session.State)==LanReplayAccess.Permission.Watch,"LAN replay: an all-DNF race also unlocks watching");
        session.EndSession();
        Check(LanReplayAccess.For(true,session.State)==LanReplayAccess.Permission.Export,"LAN replay: host ending the session unlocks export");
        foreach(var phase in new[]{LanSession.Phase.Lobby,LanSession.Phase.Ballot,LanSession.Phase.Loading,LanSession.Phase.Race})
            Check(LanReplayAccess.For(true,phase)==LanReplayAccess.Permission.Closed,"LAN replay: blocked during "+phase);
        Check(LanReplayAccess.For(false,LanSession.Phase.SessionFinished)==LanReplayAccess.Permission.Closed,"LAN replay: an inactive session alone does not grant access");
        Check(LanReplayAccess.OwnerAlive(System.Environment.ProcessId) && !LanReplayAccess.OwnerAlive(int.MaxValue),"LAN replay: parent liveness works without Godot child-process ownership");

        var lease=new LanReplayAccess.Lease(1,Guid.NewGuid().ToString("N"),System.Environment.ProcessId,1,LanReplayAccess.Permission.Watch);
        string path=LanReplayAccess.PathFor(lease.Token);
        try {
            LanReplayAccess.Write(lease);
            Check(LanReplayAccess.Read(path,out var copy) && copy==lease,"LAN replay: parent/viewer access lease round-trips atomically");
            Check(LanReplayAccess.Resolve(copy,true)==LanReplayAccess.Permission.Watch && LanReplayAccess.Resolve(copy,false)==LanReplayAccess.Permission.Closed,
                "LAN replay: parent loss during an unfinished session revokes access");
            lease=lease with {Access=LanReplayAccess.Permission.Export};LanReplayAccess.Write(lease);LanReplayAccess.Read(path,out copy);
            Check(LanReplayAccess.Resolve(copy,false)==LanReplayAccess.Permission.Export,"LAN replay: completed-session offline export can outlive its parent");
            lease=lease with {Access=LanReplayAccess.Permission.Closed};LanReplayAccess.Write(lease);LanReplayAccess.Read(path,out copy);
            Check(LanReplayAccess.Resolve(copy,true)==LanReplayAccess.Permission.Closed,"LAN replay: a new race revokes an already-open viewer");
            File.WriteAllText(path,"incomplete");
            Check(!LanReplayAccess.Read(path,out _),"LAN replay: invalid access fails closed");
            LanReplayAccess.DeleteOwned(path);
            Check(File.Exists(path),"LAN replay: cleanup does not delete an unrecognised file");
            LanReplayAccess.Write(lease);LanReplayAccess.DeleteOwned(path);
            Check(!File.Exists(path) && !File.Exists(path+".tmp"),"LAN replay: only the owned control file is cleaned up");
        } finally {File.Delete(path);File.Delete(path+".tmp");}
        } finally {session.Free();}
    }
}
