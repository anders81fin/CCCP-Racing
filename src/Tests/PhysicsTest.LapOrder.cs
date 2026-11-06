using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CCCPRacing.Replay;
namespace CCCPRacing.Tests;
public partial class PhysicsTest
{
    void LapOrderTest()
    {
        string dir=Path.Combine(Path.GetTempPath(),"cccp-lap-order-"+Guid.NewGuid().ToString("N"));
        try {
            using var rig=new LapRig(this,2,3);
            var normal=new LapArchive(Path.Combine(dir,"recent"));
            var records=new LapArchive(Path.Combine(dir,"leaderboard"));
            var order=new Dictionary<string,int>();var ids=new List<string>();
            // Actual record, ordinary and void laps go to different archives. The wall clock
            // moves backward; a pin on the third lap must precede the other laps.
            for(int lap=1;lap<=3;lap++) {
                var ghost=rig.Drive(2+lap*.1f,-1,0);
                var cap=LapCap(ghost,rig.Rec,kind:lap==1?LapKind.Record:lap==3?LapKind.Void:LapKind.Normal,
                    utc:new DateTime(2026,11,6,12,0,0,DateTimeKind.Utc).AddMinutes(lap==2?-10:lap));
                cap.LapNumber=lap;
                var a=lap==1?records:normal;var result=a.Submit(cap);
                Check(result.Accepted && result.Id.Contains($"-lap{lap:D4}-"),"lap order: lap "+lap+" is identified in its recording directory");
                ids.Add(result.Id);order[result.Id]=lap;
                Check(a.Retain(result.Id,"LAP "+lap).GetAwaiter().GetResult().Ok,"lap order: numbered capture saves through the real archive "+lap);
            }
            var shuffled=normal.List().Concat(records.List()).Select(i=>i.Meta.LapNumber==3?i with {Pinned=true}:i).ToArray();
            var sorted=LapListOrder.CurrentRun(shuffled,order).ToArray();
            Check(sorted.Select(i=>i.Id).SequenceEqual(new[]{ids[2],ids[0],ids[1]}),"lap order: pinned lap comes first across archives, then other laps keep capture order despite clock rollback");
            var twoPins=shuffled.Select(i=>i.Meta.LapNumber==1?i with {Pinned=true}:i);
            Check(LapListOrder.CurrentRun(twoPins,order).Select(i=>i.Meta.LapNumber).SequenceEqual(new[]{1,3,2}),"lap order: multiple pinned laps keep capture order within the pinned group");
            var reloaded=new LapArchive(Path.Combine(dir,"recent")).List().Concat(new LapArchive(Path.Combine(dir,"leaderboard")).List()).ToArray();
            Check(LapListOrder.CurrentRun(reloaded,order).Select(i=>i.Meta.LapNumber).SequenceEqual(new[]{1,2,3}),"lap order: actual lap numbers survive retention and a fresh disk scan");
            bool opens=true;
            foreach(var i in reloaded) {var a=i.Meta.LapNumber==1?records:normal;var s=a.Open(i.Id,out _);opens&=s!=null && s.Meta.LapNumber==i.Meta.LapNumber && s.HasTape;}
            Check(opens,"lap order: numbered recordings reopen with their original paired ghost and world tape");
            Check(LapMeta.TryParse("{\"Id\":\"lap-legacy\"}",out var legacy,out _) && legacy.LapNumber==0,"lap order: legacy metadata remains readable without inventing a lap number");
        } finally {if(Directory.Exists(dir))Directory.Delete(dir,true);}
    }
}
