using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CCCPRacing.Net;
using CCCPRacing.Race;
namespace CCCPRacing.Tests;
public partial class PhysicsTest
{
    void LanBoardReviewTests()
    {
        string root=Path.Combine(Path.GetTempPath(),"cccp-board-review-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try {
            var a=NewMachine(root,"tie-a");var b=NewMachine(root,"tie-b");
            foreach(string n in new[]{"C","D","E"})a.Row("ilyich",0,n,10);
            foreach(string n in new[]{"A","B","C"})b.Row("ilyich",0,n,10);
            var am=a.Store.BuildManifest(out _);var bm=b.Store.BuildManifest(out _);
            var known=new Dictionary<string,string>{{"ilyich",SigI}};
            a.Store.Merge(bm,2,known);b.Store.Merge(am,1,known);
            Check(a.Live[0].Entries.Select(e=>e.Name).SequenceEqual(new[]{"A","B","C"}) && a.Live[0].Entries.Select(Leaderboard.RowKey).SequenceEqual(b.Live[0].Entries.Select(Leaderboard.RowKey)),"LAN board review: opposite merge directions retain the same top-three tied rows");
            Check(a.Disk("ilyich",0).Entries.Select(Leaderboard.RowKey).SequenceEqual(b.Disk("ilyich",0).Entries.Select(Leaderboard.RowKey)),"LAN board review: tied podium convergence survives real board saves and reloads");
            var c=NewMachine(root,"tie-order");c.Live[0].LastName="OWNER";
            foreach(string n in new[]{"C","B","A"})c.Row("ilyich",0,n,10);
            var merged=c.Store.Merge(bm,2,known);
            Check(merged.RowsAdded==0 && c.Disk("ilyich",0).Entries.Select(e=>e.Name).SequenceEqual(new[]{"A","B","C"}) && c.Disk("ilyich",0).LastName=="OWNER","LAN board review: duplicate-only resync normalizes and persists tied order while preserving the local driver name");
            var old=NewMachine(root,"old-layout");old.Mark("siberia",null,1);
            var offer=new LanBoardManifest{BuiltTrack="siberia",BuiltSignature=SigS};
            offer.Tracks.Add(new LanBoardTrack("siberia",SigS,new List<(int,IReadOnlyList<LanBoardRow>)>{(0,new[]{new LanBoardRow("COMRADE",10,"2026-11-06",0,false,false,false)})}));
            var skipped=old.Store.Merge(offer,2,new Dictionary<string,string>{{"siberia",SigS}});
            Check(skipped.Skipped.Count>0 && !File.Exists(old.BoardPath("siberia",0)) && File.ReadAllText(old.Dir+"records_siberia.ver").Trim()=="1","LAN board review: an old layout marker cannot silently tag newly imported results as old records");
            using var rig=new LapRig(this,2,2);
            BoardReadCancellationReview(root,rig,false);
            BoardReadCancellationReview(root,rig,true);
            string rec=BoardRecording(a,rig,3);var info=a.Laps.List().Single(i=>i.Id==rec);
            Check(a.Laps.FindRecording("ilyich",0,3,info.Meta.GhostFingerprint)==rec,"LAN board review: intact local recordings are reused");
            string ghost=Path.Combine(info.Dir,"ghost.bin");var broken=File.ReadAllBytes(ghost);broken[20]^=1;File.WriteAllBytes(ghost,broken);
            Check(a.Laps.FindRecording("ilyich",0,3,info.Meta.GhostFingerprint)==null && File.ReadAllBytes(ghost).SequenceEqual(broken),"LAN board review: corrupt local payload is not mistaken for a recording already here and is never overwritten");
        } finally {if(Directory.Exists(root))Directory.Delete(root,true);}
        var net=new LanSession.BoardNet("HOST","A","B");
        try {
            net.Peers[2].SetBoardShare(true);net.Peers[3].SetBoardShare(true);
            var pending=net.Wire.ToArray();net.Wire.Clear();
            foreach(var m in pending) {
                if(LanSession.IsBoardOffer(m.Bytes))net.Wire.Enqueue(m);
                else net.Peers[m.To].OnPacket(m.From,m.Bytes);
            }
            Check(net.Host.BoardSyncPending && !net.Host.StartBallot(),"LAN board review: consent alone holds the ballot until the initial offers arrive, before any transfer exists");
            net.Pump();
            Check(net.Host.BoardSettled(2) && net.Host.StartBallot(),"LAN board review: acknowledged empty offers release the initial barrier without requiring recordings");
        } finally {foreach(var p in net.Peers.Values){p.BoardFixtureClose();p.Free();}}
    }
    sealed class BoardReadGate : ILanBoardStore
    {
        public ILanBoardStore Inner; public bool Started;
        public readonly TaskCompletionSource<bool> Gate=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public LanBoardManifest BuildManifest(out string note)=>Inner.BuildManifest(out note);
        public LanBoardMergeResult Merge(LanBoardManifest m,long from,IReadOnlyDictionary<string,string> known)=>Inner.Merge(m,from,known);
        public bool StillWanted(LanBoardWant w)=>Inner.StillWanted(w);
        public async Task<byte[]> ReadPayload(string id) {Started=true;await Gate.Task.ConfigureAwait(false);return await Inner.ReadPayload(id).ConfigureAwait(false);}
        public Task<LanBoardStage> Stage(LanBoardWant w,byte[] b)=>Inner.Stage(w,b);
        public string Commit(LanBoardStage s)=>Inner.Commit(s);
        public void Discard(LanBoardStage s)=>Inner.Discard(s);
    }
    void BoardReadCancellationReview(string root,LapRig rig,bool withdraw)
    {
        string tag=withdraw?"withdraw":"disconnect";
        var giver=NewMachine(root,"giver-"+tag);var receiver=NewMachine(root,"receiver-"+tag);
        string id=BoardRecording(giver,rig,3);giver.Row("ilyich",0,"GIVER",3,rec:id);
        var gate=new BoardReadGate{Inner=giver.Store};var net=new LanSession.BoardNet("HOST","RECEIVER","OTHER");
        var detached=net.Peers[2];
        try {
            net.Host.BoardStore=gate;net.Peers[2].BoardStore=receiver.Store;
            net.Host.SetBoardShare(true);net.Peers[2].SetBoardShare(true);
            net.Pump(until:()=>gate.Started);
            Check(gate.Started,"LAN board review: real recording request reaches a delayed source read ("+tag+")");
            if(withdraw) {net.Peers[2].SetBoardShare(false);net.Pump(20);}else net.Leave(2);
            Check(net.Host.BoardBusyLocal && net.Host.BoardSyncPending && !net.Host.StartBallot(),"LAN board review: "+tag+" drains the source worker before the race may start");
            gate.Gate.TrySetResult(true);net.Pump();
            Check(!net.Host.BoardBusyLocal && !net.Host.BoardSyncPending && net.Host.StartBallot(),"LAN board review: "+tag+" releases the barrier when the discarded read truly finishes");
        } finally {gate.Gate.TrySetResult(true);net.Pump();if(!net.Peers.ContainsKey(2)){detached.BoardFixtureClose();detached.Free();}foreach(var p in net.Peers.Values){p.BoardFixtureClose();p.Free();}}
    }

}
